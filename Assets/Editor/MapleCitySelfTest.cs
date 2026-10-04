using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless acceptance harness for the MAPLE CITY region.
///
/// WHY THIS EXISTS
/// ---------------
/// Every VISUAL claim about this region is proved by opening a render and looking at it. But three
/// of the things the region has to do are NOT visual, and a screenshot cannot prove any of them:
///
///   1. FAST TRAVEL  - does selecting the Maple City pin on the world map actually put the player
///                     on the Maple City course, in Maple City world space, with Maple City drawn
///                     and its siblings hidden? A render can show the city; it cannot show that
///                     the SESSION agrees with what is on screen.
///   2. GRADE CHANNEL- does the baked course expose a gradient profile that the trainer/simulator
///                     actually responds to? The resistance channel is driven by
///                     <see cref="RideSession.Grade"/>, so if the course were flat (or if the
///                     grade were noisy/discontinuous at the lap seam) the trainer would feel
///                     wrong and nothing on screen would look any different.
///   3. RIDE POSES   - <see cref="KuroRidePose"/> switches Kuro to the out-of-saddle CLIMBER pose
///                     purely from session grade. That only ever fires on this course if the
///                     course crosses the climb threshold, and it can only fire at all if the pose
///                     component exists and is pointed at the same session the region drives.
///
/// So this runs those three as assertions and PRINTS THE NUMBERS. It deliberately does NOT try to
/// prove anything about real hardware: there is no trainer attached to this machine, so the
/// resistance leg is exercised through <see cref="SimulatedTrainerSource"/>, which is the same
/// grade->effort channel a real FTMS trainer sits behind. Anything needing the physical Wahoo
/// stays NOT VERIFIED and is reported as such rather than quietly passing.
///
/// Edit-mode safe: it calls the same methods the world map calls, without entering play mode, so
/// it can run in batchmode alongside the environment/roster passes.
/// </summary>
public static class MapleCitySelfTest
{
    // PROVISIONAL acceptance thresholds. These are not design requirements - they are the bar this
    // harness fails at, chosen to catch a genuinely broken bake rather than to pin down tuning.
    private const float ExpectedLapM = 5000f;
    private const float LapToleranceM = 5f;
    private const float MinClimbFraction = 0.05f;   // >=5% of the lap must be steep enough to climb
    private const float MaxSeamGradeStepPct = 1.5f; // grade must not jump across the start/finish

    [MenuItem("MapleRide/Environment/Self Test Maple City", priority = 34)]
    public static void Run()
    {
        // Batchmode starts on an empty Untitled scene - nothing is loaded unless we load it. A
        // self test that skipped this would report "no RegionDirector" forever and look like a
        // wiring bug in the region rather than an empty editor.
        const string ScenePath = "Assets/Scenes/SakuraPass.unity";
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != ScenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        var pass = true;

        // ------------------------------------------------------------------ 1. fast travel
        var director = Object.FindFirstObjectByType<RegionDirector>();
        if (director == null) { Debug.LogError("[citytest] FAIL no RegionDirector in the scene."); return; }

        var session = Object.FindFirstObjectByType<RideSession>();
        if (session == null) { Debug.LogError("[citytest] FAIL no RideSession in the scene."); return; }

        var before = session.courseId;
        // Stand in for Awake: in edit mode nothing has booted the session yet, so give it the
        // same resolved graph it would hold one frame into play mode before asking it to travel.
        session.EnsureCourse();
        var travelled = director.FastTravel(RegionCatalog.MapleCity);
        pass &= Check(travelled, $"FastTravel('{RegionCatalog.MapleCity}') returned true");

        var region = RegionCatalog.Find(RegionCatalog.MapleCity);
        var course = session.Course;
        pass &= Check(course != null, "session has a course after fast travel");
        if (course == null) { Debug.LogError("[citytest] aborting - no course."); return; }

        pass &= Check(session.courseId == region.BuiltCourseId,
                      $"session.courseId '{session.courseId}' == catalog BuiltCourseId '{region.BuiltCourseId}'");
        pass &= Check(course.Closed, "Maple City course is a CLOSED circuit (it is a city lap, not out-and-back)");
        pass &= Check(Mathf.Abs(course.Length - ExpectedLapM) <= LapToleranceM,
                      $"lap length {course.Length:0.0} m within {LapToleranceM} m of {ExpectedLapM} m");

        // The rider must actually be standing in Maple City's patch of world space, not left behind
        // in Sakura's. All three regions share ONE scene, so this is the assertion that catches a
        // fast travel that changed the course but not the position.
        var p = session.WorldPosition;
        var inX = p.x >= course.BoundsMin.x - 60f && p.x <= course.BoundsMax.x + 60f;
        var inZ = p.z >= course.BoundsMin.y - 60f && p.z <= course.BoundsMax.y + 60f;
        pass &= Check(inX && inZ, $"rider {p} is inside the Maple City course bounds " +
                                  $"x[{course.BoundsMin.x:0}..{course.BoundsMax.x:0}] " +
                                  $"z[{course.BoundsMin.y:0}..{course.BoundsMax.y:0}]");

        // Exactly one region may be drawn, and it must be this one.
        var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .Where(t => t.parent == null)
                          .Where(t => t.name == "Maple City Environment" ||
                                      t.name == "Shiosai Coast Environment" ||
                                      t.name == "Sakura Pass Environment")
                          .ToArray();
        var city = roots.Where(t => t.name == "Maple City Environment").ToArray();
        pass &= Check(city.Length == 1, $"exactly one 'Maple City Environment' root (found {city.Length})");
        pass &= Check(city.All(t => t.gameObject.activeSelf), "Maple City environment is VISIBLE");
        foreach (var other in roots.Where(t => t.name != "Maple City Environment"))
            pass &= Check(!other.gameObject.activeSelf, $"sibling '{other.name}' is HIDDEN while riding the city");

        pass &= Check(course.Checkpoints.Length >= 4,
                      $"course carries {course.Checkpoints.Length} named checkpoints (>=4)");
        foreach (var cp in course.Checkpoints)
            Debug.Log($"[citytest]   checkpoint {cp.Distance,7:0} m  {cp.Name}");

        // ------------------------------------------------------------------ 2. grade channel
        // Walk the lap at the same window RideSession uses, so these ARE the numbers the trainer
        // and the pose system will see at run time.
        var window = session.gradeWindowM;
        const int steps = 1000;
        float minG = float.MaxValue, maxG = float.MinValue, climbM = 0f;
        for (var i = 0; i < steps; i++)
        {
            var d = course.Length * i / steps;
            var g = course.GradeAt(d, window);
            minG = Mathf.Min(minG, g);
            maxG = Mathf.Max(maxG, g);
            if (g >= 0.04f) climbM += course.Length / steps;
        }
        var climbFrac = climbM / course.Length;
        Debug.Log($"[citytest] grade over the lap (window {window:0.0} m): " +
                  $"{minG * 100f:0.00}% .. {maxG * 100f:0.00}%, " +
                  $"{climbFrac * 100f:0.0}% of the lap at or above the 4% climb threshold, " +
                  $"elevation {course.MinElevation:0.0}..{course.MaxElevation:0.0} m, ascent {course.Ascent:0} m.");
        pass &= Check(maxG > 0.04f, "the lap contains real climbing (max grade > 4%)");
        pass &= Check(minG < -0.02f, "the lap contains a real descent (min grade < -2%)");
        pass &= Check(climbFrac >= MinClimbFraction,
                      $"{climbFrac * 100f:0.0}% of the lap is climbable (>= {MinClimbFraction * 100f:0.0}%)");

        // The seam is the one place a closed circuit can feel wrong on a trainer: if the grade
        // steps as the lap rolls over, the resistance would snap every 5 km.
        var gEnd = course.GradeAt(course.Length - 1f, window);
        var gStart = course.GradeAt(1f, window);
        var seamStep = Mathf.Abs(gEnd - gStart) * 100f;
        pass &= Check(seamStep <= MaxSeamGradeStepPct,
                      $"lap seam grade step {seamStep:0.00}% <= {MaxSeamGradeStepPct:0.0}% " +
                      $"(end {gEnd * 100f:0.00}% -> start {gStart * 100f:0.00}%)");

        // Drive the SIMULATED trainer with this course's own gradient and show that the effort the
        // resistance channel asks for actually tracks the road. This is the grade->trainer leg;
        // the real FTMS write is hardware and stays unverified.
        var ftp = 220f;
        float WattsAt(float grade)
        {
            var s = new SimulatedTrainerSource();
            for (var i = 0; i < 600; i++) s.Tick(1f / 60f, grade, 0f, ftp);  // settle 10 s
            return s.Read().Watts;
        }
        var wClimb = WattsAt(maxG);
        var wFlat = WattsAt(0f);
        var wDesc = WattsAt(minG);
        Debug.Log($"[citytest] simulated trainer response on this course: " +
                  $"climb {maxG * 100f:0.0}% -> {wClimb:0} W, flat -> {wFlat:0} W, " +
                  $"descent {minG * 100f:0.0}% -> {wDesc:0} W (FTP {ftp:0} W).");
        pass &= Check(wClimb > wFlat && wFlat > wDesc,
                      "trainer effort rises on the climb and falls on the descent (grade channel live)");

        // ------------------------------------------------------------------ 3. ride poses
        var pose = Object.FindFirstObjectByType<KuroRidePose>(FindObjectsInactive.Include);
        pass &= Check(pose != null, "KuroRidePose is present in the scene");
        if (pose != null)
        {
            pass &= Check(pose.session == null || pose.session == session,
                          "KuroRidePose is bound to the same RideSession the region director drives");
            pass &= Check(maxG >= pose.climbGradeThreshold,
                          $"course max grade {maxG * 100f:0.00}% reaches the climber threshold " +
                          $"{pose.climbGradeThreshold * 100f:0.00}% - the climb pose WILL fire here");
            pass &= Check(maxG >= pose.climbGradeFull,
                          $"course max grade reaches FULL climb commitment {pose.climbGradeFull * 100f:0.00}%");
            // The sprint pose is a held key, not a grade - it is course-independent by design, so
            // all this can assert is that the binding exists.
            Debug.Log($"[citytest] sprinter pose is bound to '{pose.sprintKey}' (held, course-independent).");
        }

        // ------------------------------------------------------------------ restore
        // Leave the editor where we found it so a self test never mutates the authored scene state.
        if (!string.IsNullOrEmpty(before) && before != session.courseId)
        {
            session.SelectCourse(before);
            session.ResetRide();
        }

        Debug.Log(pass
            ? "[citytest] RESULT: PASS - fast travel, grade channel and climb-pose gating all hold on Maple City."
            : "[citytest] RESULT: FAIL - see the [citytest] FAIL lines above.");
    }

    private static bool Check(bool ok, string what)
    {
        Debug.Log($"[citytest] {(ok ? "ok  " : "FAIL")} {what}");
        return ok;
    }

    /// <summary>
    /// SIBLING-REGION REGRESSION for Sakura Pass.
    ///
    /// SakuraPassDiagnostics.Capture photographs whatever is currently VISIBLE - it predates
    /// multi-region and never touches RegionDirector. Once Maple City exists in the same scene,
    /// running it straight after a city pass silently produces a set of empty sky frames,
    /// because Sakura's environment root is switched off. Those blank PNGs look exactly like a
    /// catastrophic regression in Sakura and are in fact just the wrong region being on.
    ///
    /// So: travel to Sakura FIRST, then capture, then put the region back. This touches no
    /// Sakura file - it only drives the same public entry points the world map uses.
    /// </summary>
    [MenuItem("MapleRide/Environment/Capture Sakura Regression (region-safe)", priority = 35)]
    public static void CaptureSakuraRegression()
    {
        const string ScenePath = "Assets/Scenes/SakuraPass.unity";
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != ScenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        var director = Object.FindFirstObjectByType<RegionDirector>();
        var session = Object.FindFirstObjectByType<RideSession>();
        if (director == null || session == null)
        {
            Debug.LogError("[citytest] regression: no RegionDirector/RideSession.");
            return;
        }

        session.EnsureCourse();
        var previous = director.currentRegionId;
        if (!director.FastTravel(RegionCatalog.SakuraPass))
        {
            Debug.LogError("[citytest] regression: could not travel to Sakura Pass.");
            return;
        }
        Debug.Log("[citytest] regression: Sakura Pass is the visible region, capturing.");
        SakuraPassDiagnostics.Capture();

        if (!string.IsNullOrEmpty(previous) && previous != RegionCatalog.SakuraPass)
            director.FastTravel(previous);
    }
}

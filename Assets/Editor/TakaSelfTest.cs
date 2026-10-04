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
///   1. FAST TRAVEL  - does selecting the Taka pin on the world map actually put the player
///                     on the Taka course, in Taka world space, with Taka Mountains drawn
///                     and its siblings hidden? A render can show the fell; it cannot show that
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
public static class TakaSelfTest
{
    // PROVISIONAL acceptance thresholds. These are not design requirements - they are the bar this
    // harness fails at, chosen to catch a genuinely broken bake rather than to pin down tuning.
    private const float ExpectedLapM = 22110f;   // taka_high_road as baked
    private const float LapToleranceM = 80f;
    private const float MinClimbFraction = 0.40f;   // the high road is a climb first and foremost
    private const float MaxSeamGradeStepPct = 1.5f; // grade must not jump across the start/finish

    [MenuItem("MapleRide/Environment/Self Test Taka Mountains", priority = 34)]
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
        if (director == null) { Debug.LogError("[takatest] FAIL no RegionDirector in the scene."); return; }

        var session = Object.FindFirstObjectByType<RideSession>();
        if (session == null) { Debug.LogError("[takatest] FAIL no RideSession in the scene."); return; }

        var before = session.courseId;
        // Stand in for Awake: in edit mode nothing has booted the session yet, so give it the
        // same resolved graph it would hold one frame into play mode before asking it to travel.
        session.EnsureCourse();
        var travelled = director.FastTravel(RegionCatalog.TakaMountains);
        pass &= Check(travelled, $"FastTravel('{RegionCatalog.TakaMountains}') returned true");

        var region = RegionCatalog.Find(RegionCatalog.TakaMountains);
        var course = session.Course;
        pass &= Check(course != null, "session has a course after fast travel");
        if (course == null) { Debug.LogError("[takatest] aborting - no course."); return; }

        pass &= Check(session.courseId == region.BuiltCourseId,
                      $"session.courseId '{session.courseId}' == catalog BuiltCourseId '{region.BuiltCourseId}'");
        // Taka is an OPEN course, like Azora. Asserting !Closed is not pedantry: a course
        // wrongly marked closed makes RouteCourse wrap its arc-length lookups, which would put the
        // col's +8% ramp one metre after the finish descent and step the trainer resistance by
        // 20 points at the seam.
        pass &= Check(!course.Closed, "Taka course is OPEN point-to-point (a pass is ridden, not lapped)");
        pass &= Check(Mathf.Abs(course.Length - ExpectedLapM) <= LapToleranceM,
                      $"route length {course.Length:0.0} m within {LapToleranceM} m of {ExpectedLapM} m");
        pass &= Check(course.MaxElevation > 2790f && course.MaxElevation < 2890f,
                      $"summit elevation {course.MaxElevation:0.0} m is the doc's 2,842 m Kori Lake summit");
        pass &= Check(course.Ascent > 1350f,
                      $"total ascent {course.Ascent:0} m clears the doc's 1,000 m 'earn the view' bar");

        // The rider must actually be standing in Taka's patch of world space, not left behind
        // in Sakura's. All three regions share ONE scene, so this is the assertion that catches a
        // fast travel that changed the course but not the position.
        var p = session.WorldPosition;
        var inX = p.x >= course.BoundsMin.x - 60f && p.x <= course.BoundsMax.x + 60f;
        var inZ = p.z >= course.BoundsMin.y - 60f && p.z <= course.BoundsMax.y + 60f;
        pass &= Check(inX && inZ, $"rider {p} is inside the Taka course bounds " +
                                  $"x[{course.BoundsMin.x:0}..{course.BoundsMax.x:0}] " +
                                  $"z[{course.BoundsMin.y:0}..{course.BoundsMax.y:0}]");

        // Exactly one region may be drawn, and it must be this one.
        var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .Where(t => t.parent == null)
                          .Where(t => t.name == "Taka Mountains Environment" ||
                                      t.name == "Shiosai Coast Environment" ||
                                      t.name == "Maple City Environment" ||
                                      t.name == "Sakura Pass Environment")
                          .ToArray();
        var taka = roots.Where(t => t.name == "Taka Mountains Environment").ToArray();
        pass &= Check(taka.Length == 1, $"exactly one 'Taka Mountains Environment' root (found {taka.Length})");
        pass &= Check(taka.All(t => t.gameObject.activeSelf), "Taka Mountains environment is VISIBLE");
        foreach (var other in roots.Where(t => t.name != "Taka Mountains Environment"))
            pass &= Check(!other.gameObject.activeSelf, $"sibling '{other.name}' is HIDDEN while riding Taka");

        pass &= Check(course.Checkpoints.Length >= 4,
                      $"course carries {course.Checkpoints.Length} named checkpoints (>=4)");
        foreach (var cp in course.Checkpoints)
            Debug.Log($"[takatest]   checkpoint {cp.Distance,7:0} m  {cp.Name}");

        // ------------------------------------------------------------------ 2. grade channel
        // Walk the route at the same window RideSession uses, so these ARE the numbers the trainer
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
        Debug.Log($"[takatest] grade over the route (window {window:0.0} m): " +
                  $"{minG * 100f:0.00}% .. {maxG * 100f:0.00}%, " +
                  $"{climbFrac * 100f:0.0}% of the lap at or above the 4% climb threshold, " +
                  $"elevation {course.MinElevation:0.0}..{course.MaxElevation:0.0} m, ascent {course.Ascent:0} m.");
        pass &= Check(maxG > 0.09f, "the high road contains SUSTAINED steep climbing (max grade > 9%)");
        pass &= Check(minG < -0.08f, "the high road contains the technical descent (min grade < -8%)");
        pass &= Check(climbFrac >= MinClimbFraction,
                      $"{climbFrac * 100f:0.0}% of the route is climbable (>= {MinClimbFraction * 100f:0.0}%)");

        // NO SEAM TEST HERE, deliberately. Maple City asserts that the grade does not step across
        // the start/finish because it is a lap and the player rides that seam every 5 km. Taka
        // has no seam: the road starts at the meadow gate and stops at the highland descent, and
        // the two ends are 800 m apart in elevation. Comparing them would fail by construction
        // and would be testing nothing.

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
        Debug.Log($"[takatest] simulated trainer response on this course: " +
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
            Debug.Log($"[takatest] sprinter pose is bound to '{pose.sprintKey}' (held, course-independent).");
        }

        // ------------------------------------------------------------------ restore
        // Leave the editor where we found it so a self test never mutates the authored scene state.
        if (!string.IsNullOrEmpty(before) && before != session.courseId)
        {
            session.SelectCourse(before);
            session.ResetRide();
        }

        Debug.Log(pass
            ? "[takatest] RESULT: PASS - fast travel, grade channel and climb-pose gating all hold on Taka Mountains."
            : "[takatest] RESULT: FAIL - see the [takatest] FAIL lines above.");
    }

    private static bool Check(bool ok, string what)
    {
        Debug.Log($"[takatest] {(ok ? "ok  " : "FAIL")} {what}");
        return ok;
    }

    /// <summary>
    /// SIBLING-REGION REGRESSION for Sakura Pass.
    ///
    /// SakuraPassDiagnostics.Capture photographs whatever is currently VISIBLE - it predates
    /// multi-region and never touches RegionDirector. Once Taka Mountains exists in the same scene,
    /// running it straight after an Taka pass silently produces a set of empty sky frames,
    /// because Sakura's environment root is switched off. Those blank PNGs look exactly like a
    /// catastrophic regression in Sakura and are in fact just the wrong region being on.
    ///
    /// So: travel to Sakura FIRST, then capture, then put the region back. This touches no
    /// Sakura file - it only drives the same public entry points the world map uses.
    /// </summary>
    [MenuItem("MapleRide/Environment/Capture Sakura Regression (Taka-safe)", priority = 35)]
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
            Debug.LogError("[takatest] regression: no RegionDirector/RideSession.");
            return;
        }

        session.EnsureCourse();
        var previous = director.currentRegionId;
        if (!director.FastTravel(RegionCatalog.SakuraPass))
        {
            Debug.LogError("[takatest] regression: could not travel to Sakura Pass.");
            return;
        }
        Debug.Log("[takatest] regression: Sakura Pass is the visible region, capturing.");
        SakuraPassDiagnostics.Capture();

        if (!string.IsNullOrEmpty(previous) && previous != RegionCatalog.SakuraPass)
            director.FastTravel(previous);
    }

    /// <summary>
    /// Stages the Taka roster and bakes its greeting portraits IN ONE BATCHMODE PASS.
    ///
    /// This exists because <see cref="NpcPortraitBake.BakeAll"/> photographs only riders that are
    /// <c>activeInHierarchy</c> and DELETES the stale portrait chip of any rider that is not. All
    /// regions share one scene and RegionDirector hides every region but the current one, so
    /// running the bake while Sakura or Maple City happens to be the visible region would bake
    /// nothing for Taka and silently throw away whatever had been baked before. Forcing the
    /// region here, in the same process, removes the ordering hazard entirely rather than
    /// documenting it.
    ///
    /// Order matters: stage, THEN make Taka visible, THEN bake, THEN re-apply the serialized
    /// pace (staging already writes it, but re-running it is free and makes this entry point
    /// the single thing to call after a PaceScale change).
    /// </summary>
    [MenuItem("MapleRide/NPCs/Stage + Bake Taka Roster", priority = 63)]
    public static void StageAndBakeRoster()
    {
        const string ScenePath = "Assets/Scenes/SakuraPass.unity";
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path != ScenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        TakaNpcRoster.AddAllToScene();

        // Make Taka the drawn region so the bake can see the riders. FastTravel also moves the
        // RideSession, which is harmless here and is restored below.
        var director = Object.FindFirstObjectByType<RegionDirector>();
        var session = Object.FindFirstObjectByType<RideSession>();
        string before = session != null ? session.courseId : null;
        if (director != null && session != null)
        {
            session.EnsureCourse();
            director.FastTravel(RegionCatalog.TakaMountains);
        }
        else
        {
            Debug.LogWarning("[takatest] no RegionDirector/RideSession - baking against whatever " +
                             "region happens to be visible, which may skip the Taka riders.");
        }

        int visible = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None)
                            .Count(g => g.gameObject.name.StartsWith("Taka NPC ") &&
                                        g.gameObject.activeInHierarchy);
        Debug.Log($"[takatest] {visible} Taka rider(s) are active and bakeable.");

        NpcPortraitBake.BakeAll();
        TakaNpcRoster.ApplyPaceToScene();

        if (!string.IsNullOrEmpty(before) && session != null && before != session.courseId)
        {
            session.SelectCourse(before);
            session.ResetRide();
        }

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("[takatest] stage + portrait bake complete.");
    }
}

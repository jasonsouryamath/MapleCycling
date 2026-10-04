using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShuntaVariantsCheck
{
    const string PlayablePath = "Assets/Scenes/Playable/shunta_metro.unity";
    static int _fails;

    [MenuItem("MapleRide/Shunta/Variants Self-Check")]
    public static void Run() => ShuntaVariantsSelfCheck.RunAndLog();

    static void Check(bool ok, string what)
    {
        Debug.Log("[shunta-variants-course] " + (ok ? "PASS " : "FAIL ") + what);
        if (!ok) _fails++;
    }

    /// <summary>
    /// Proves the variant courses register into the in-memory RouteGraph (no shared file edited), build as RouteCourses with
    /// the expected shape, and START: the session selects each one and the rider is placed on the right start point.
    /// run_steps "ShuntaVariantsCheck.RunCourses|claude_variant_courses.log|1"
    /// </summary>
    [MenuItem("MapleRide/Shunta/Variant Courses Self-Test")]
    public static void RunCourses()
    {
        _fails = 0;
        EditorSceneManager.OpenScene(PlayablePath, OpenSceneMode.Single);
        RouteGraph.Invalidate();
        var g = RouteGraph.Load();
        var baked = Resources.Load<RouteGraph>(RouteGraph.ResourceName);
        Check(g != null && g.Course(ShuntaVariantCourses.BaseId) != null, "graph has the base shunta_metro course");
        if (g == null || g.Course(ShuntaVariantCourses.BaseId) == null) { Finish(); return; }
        Check(g.Course(ShuntaVariantCourses.LoopId) == null, "variants are opt-in (not registered before Ensure)");
        int courses0 = g.courses.Length;
        Check(ShuntaVariantCourses.Ensure(), "ShuntaVariantCourses.Ensure()");
        Check(g.courses.Length == courses0 + 3, $"exactly +3 courses ({courses0}->{g.courses.Length})");
        Check(ShuntaVariantCourses.Ensure() && g.courses.Length == courses0 + 3, "Ensure is idempotent");
        Check(baked != null && baked.Course(ShuntaVariantCourses.LoopId) == null && baked.Course(ShuntaVariantCourses.BaseId) == null, "baked asset on disk NOT modified");

        var baseCourse = RouteCourse.Build(g, g.Course(ShuntaVariantCourses.BaseId));
        var loop = RouteCourse.Build(g, g.Course(ShuntaVariantCourses.LoopId));
        var rev = RouteCourse.Build(g, g.Course(ShuntaVariantCourses.ReverseId));
        var spr = RouteCourse.Build(g, g.Course(ShuntaVariantCourses.SprintId));
        Debug.Log($"[shunta-variants-course] lengths: base {baseCourse.Length:0} loop {loop.Length:0} reverse {rev.Length:0} sprint {spr.Length:0} m");
        Check(Mathf.Abs(baseCourse.Length - 28400f) < 600f, "base course unchanged (" + baseCourse.Length.ToString("0") + " m)");
        Check(Mathf.Abs(rev.Length - baseCourse.Length) < 5f && !rev.Closed, "reverse: same length, point-to-point");
        Check((rev.PositionAt(0f) - baseCourse.PositionAt(baseCourse.Length)).magnitude < 2f, "reverse starts at the base finish");
        Check((rev.PositionAt(rev.Length) - baseCourse.PositionAt(0f)).magnitude < 2f, "reverse ends at the base start");
        Check(rev.Checkpoints.Length == 12, "reverse keeps 12 checkpoints (" + rev.Checkpoints.Length + ")");
        float expSprint = baseCourse.Length - baseCourse.Checkpoints[7].Distance;   // zones 9..12 = after the zone-8 checkpoint
        Check(Mathf.Abs(spr.Length - expSprint) < 30f && !spr.Closed, $"sprint cut ~{expSprint:0} m point-to-point ({spr.Length:0})");
        Check(spr.Checkpoints.Length == 4, "sprint cut has 4 zone checkpoints (" + spr.Checkpoints.Length + ")");
        Check((spr.PositionAt(spr.Length) - baseCourse.PositionAt(baseCourse.Length)).magnitude < 2f, "sprint cut ends at the base finish");
        Check(loop.Closed && loop.Length > 13800f && loop.Length < 30000f, "short loop is closed, length " + loop.Length.ToString("0") + " m");
        Check(loop.Checkpoints.Length >= 6, "short loop checkpoints " + loop.Checkpoints.Length);
        float maxStep = 0f; for (int i = 1; i < loop.Count; i++) maxStep = Mathf.Max(maxStep, Vector3.Distance(loop.Position[i - 1], loop.Position[i]));
        Check(maxStep < 75f, "short loop polyline has no gaps (max step " + maxStep.ToString("0.0") + " m)");

        // ---- start each one through the real session
        var session = Object.FindFirstObjectByType<RideSession>();
        Check(session != null, "playable scene has a RideSession");
        if (session != null)
        {
            session.EnsureCourse();
            foreach (var id in ShuntaVariantCourses.AllIds)
            {
                bool ok = ShuntaVariantCourses.Select(session, id);
                Check(ok && session.courseId == id && session.Course != null && session.Course.Id == id, $"Select({id}) starts the course");
                if (!ok || session.Course == null) continue;
                var expect = session.Course.PositionAt(0f);
                Check((session.WorldPosition - expect).magnitude < 2f, $"{id}: rider placed on its start {session.WorldPosition}");
                Check(session.TotalLaps == (id == ShuntaVariantCourses.LoopId ? ShuntaVariantCourses.LoopLaps : 1), $"{id}: laps {session.TotalLaps}");
                session.SeekTo(session.Course.Length * 0.5f);
                Check(session.Course.SegmentAt(session.DistanceM).Length > 0, $"{id}: mid-course section '{session.Course.SegmentAt(session.DistanceM)}'");
            }
            Check(ShuntaVariantCourses.Select(session, ShuntaVariantCourses.BaseId) && session.Course.Id == ShuntaVariantCourses.BaseId, "can go back to the base course");
            Check(!ShuntaVariantCourses.Select(session, "sakura_circuit"), "Select refuses non-Shunta ids");
        }
        // other courses untouched
        foreach (var c in baked.courses)
        {
            var a = RouteCourse.Build(baked, baked.Course(c.id)); var b = RouteCourse.Build(g, g.Course(c.id));
            Check(Mathf.Approximately(a.Length, b.Length), $"existing course {c.id} unchanged");
        }
        Finish();
    }

    static void Finish()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RouteGraph.Invalidate();   // drop the augmented cache
        if (_fails == 0) Debug.Log("[shunta-variants-course] SELFTEST PASS");
        else { Debug.LogError($"[shunta-variants-course] SELFTEST FAIL ({_fails})"); if (Application.isBatchMode) EditorApplication.Exit(2); }
    }
}

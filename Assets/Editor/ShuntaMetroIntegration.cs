using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Shunta Metro ride integration (editor): builds the playable scene Assets/Scenes/Playable/shunta_metro.unity
/// from the Shiosai playable base (ride rig only, no region scenery) + the Shunta route, registers it in the
/// streaming manifest, and SelfTest proves the course boots through RegionDirector/RideSession.
/// </summary>
public static class ShuntaMetroIntegration
{
    public const string EnvRoot = "Shunta Metro Environment";
    const string BaseScene = "Assets/Scenes/Playable/shiosai_coast.unity";
    static string PlayablePath => MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro);

    [MenuItem("MapleRide/Shunta Metro/Build Playable Scene")]
    public static void BuildPlayableScene()
    {
        var baseScene = EditorSceneManager.OpenScene(BaseScene, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(baseScene, PlayablePath, true)) throw new IOException("cannot save " + PlayablePath);
        var scene = EditorSceneManager.OpenScene(PlayablePath, OpenSceneMode.Single);

        // strip every other region's scenery/rosters; keep the ride rig (Kuro, camera, lights, MapleRide Ride, EventSystem...)
        int removed = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            bool other = RegionCatalog.Regions.Any(r => r.Id != RegionCatalog.ShuntaMetro &&
                (root.name == r.EnvironmentRoot || (!string.IsNullOrEmpty(r.DisplayName) && root.name.StartsWith(r.DisplayName, System.StringComparison.OrdinalIgnoreCase)))) ||
                root.name == EnvRoot;
            if (other) { Object.DestroyImmediate(root); removed++; }
        }

        var env = new GameObject(EnvRoot);
        env.transform.position = ShuntaRouteProvider.WorldOffset;
        var route = new GameObject("Shunta Route");
        route.transform.SetParent(env.transform, false);
        route.AddComponent<ShuntaRouteBuilder>().Rebuild();

        var ride = Object.FindFirstObjectByType<RideBootstrap>(FindObjectsInactive.Include);
        if (ride == null) throw new System.InvalidOperationException("no RideBootstrap in base");
        var streamer = ride.GetComponent<RegionSceneStreamer>();
        if (streamer != null) { streamer.regionId = RegionCatalog.ShuntaMetro; streamer.cells = new RegionSceneStreamer.Cell[0]; }
        var dress = ride.GetComponent<RouteDressingStreamer>();
        if (dress != null) dress.chunks = new RouteDressingStreamer.Chunk[0];
        if (ride.session != null) ride.session.courseId = ShuntaRouteProvider.CourseId;
        if (ride.regions != null) ride.regions.currentRegionId = RegionCatalog.ShuntaMetro;

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("cannot save " + PlayablePath);
        Debug.Log($"[shunta-int] built {PlayablePath}: removed {removed} foreign roots, roots now {scene.GetRootGameObjects().Length}, {new FileInfo(PlayablePath).Length} bytes");
    }

    /// <summary>Adds the playable scene to the streaming manifest (additive, idempotent).</summary>
    public static void RegisterManifest()
    {
        const string mp = MapleRideStreamingScenes.ManifestPath;
        string text = File.ReadAllText(mp);
        if (text.Contains(PlayablePath)) { Debug.Log("[shunta-int] manifest already lists scene"); return; }
        int close = text.LastIndexOf(']');
        int before = close - 1;
        while (char.IsWhiteSpace(text[before])) before--;
        string inserted = text.Substring(0, before + 1) + ",\n        \"" + PlayablePath + "\"" + text.Substring(before + 1);
        File.WriteAllText(mp, inserted);
        AssetDatabase.Refresh();
        MapleRideStreamingScenes.RegisterBuildScenes();
        Debug.Log("[shunta-int] manifest updated; scene registered");
    }

    static int _fails;
    static bool Check(bool ok, string what)
    {
        Debug.Log($"[shunta-int] {(ok ? "PASS" : "FAIL")} {what}");
        if (!ok) _fails++;
        return ok;
    }

    public static void SelfTest()
    {
        _fails = 0;
        var scene = EditorSceneManager.OpenScene(PlayablePath, OpenSceneMode.Single);
        RouteGraph.Invalidate();
        var graph = RouteGraph.Load();
        Check(graph != null, "RouteGraph loads");
        var baked = Resources.Load<RouteGraph>(RouteGraph.ResourceName);
        Check(baked != null && baked.Course(ShuntaRouteProvider.CourseId) == null, "baked asset on disk NOT modified (no shunta course)");
        Check(graph.courses.Length == baked.courses.Length + 1, $"graph has exactly +1 course ({baked.courses.Length}->{graph.courses.Length})");
        foreach (var c in baked.courses)
        {
            var a = RouteCourse.Build(baked, baked.Course(c.id)); var b = RouteCourse.Build(graph, graph.Course(c.id));
            Check(Mathf.Approximately(a.Length, b.Length) && a.Checkpoints.Length == b.Checkpoints.Length, $"existing course {c.id} unchanged (len {b.Length:0})");
        }
        var def = graph.Course(ShuntaRouteProvider.CourseId);
        Check(def != null, "graph.Course('shunta_metro') present");
        if (def == null) { Finish(); return; }
        Check(graph.RegionOfCourse(def.id) == RegionCatalog.ShuntaMetro, "RegionOfCourse == shunta_metro");
        var course = RouteCourse.Build(graph, def);
        Check(!course.Closed, "point-to-point (not closed)");
        Check(Mathf.Abs(course.Length - 28400f) < 600f, $"length {course.Length:0} m ~ 28400");
        Check(Mathf.Abs(course.Ascent - 612f) < 60f, $"ascent {course.Ascent:0} m ~ 612");
        Check(course.Checkpoints.Length == 12, $"12 checkpoints ({course.Checkpoints.Length})");
        Check(course.SegmentOf.Distinct().Count() == 12, $"12 zone names as segments ({course.SegmentOf.Distinct().Count()})");
        float maxStep = 0f; for (int i = 1; i < course.Count; i++) maxStep = Mathf.Max(maxStep, Vector3.Distance(course.Position[i - 1], course.Position[i]));
        Check(maxStep < 75f, $"no gaps in polyline (max step {maxStep:0.0} m)");
        float maxG = 0f; for (float d = 0; d < course.Length; d += 20f) maxG = Mathf.Max(maxG, Mathf.Abs(course.GradeAt(d, 8f)));
        Check(maxG < 0.20f, $"max |grade| {maxG * 100f:0.0}% < 20%");
        Check(course.BoundsMin.y > 20000f, "course parked in empty world space (z > 20 km)");

        // ---- the same boot path another region's SelfTest uses
        var region = RegionCatalog.Find(RegionCatalog.ShuntaMetro);
        string saved = region.BuiltCourseId;
        try
        {
            region.BuiltCourseId = ShuntaRouteProvider.CourseId;   // temporary, restored below
            var director = Object.FindFirstObjectByType<RegionDirector>();
            var session = Object.FindFirstObjectByType<RideSession>();
            Check(director != null && session != null, "playable scene has RegionDirector + RideSession");
            if (director != null && session != null)
            {
                session.EnsureCourse();
                Check(director.CanTravelTo(RegionCatalog.ShuntaMetro), "CanTravelTo(shunta_metro)");
                Check(director.FastTravel(RegionCatalog.ShuntaMetro), "FastTravel(shunta_metro) returned true");
                Check(session.courseId == ShuntaRouteProvider.CourseId && session.Course != null, "session.courseId + Course set");
                var p = session.WorldPosition;
                Check((p - course.PositionAt(0f)).magnitude < 2f, $"rider at km 0 start {p}");
                Check(session.plannedLaps >= 1 && session.targetDurationMinutes > 0f, $"ride plan laps {session.plannedLaps}, {session.targetDurationMinutes:0} min");
                var boot = Object.FindFirstObjectByType<RideBootstrap>();
                if (boot != null && boot.follower != null && boot.follower.rider != null)
                {
                    boot.follower.Apply();
                    var r = boot.follower.rider;
                    Check((r.position - course.PositionAt(0f)).magnitude < 3f, $"Kuro placed on start pad {r.position}");
                    var fwd = course.TangentAt(0f); fwd.y = 0f; fwd.Normalize();
                    var rf = r.forward; rf.y = 0f; rf.Normalize();
                    Check(Vector3.Dot(fwd, rf) > 0.9f, $"Kuro faces the route (dot {Vector3.Dot(fwd, rf):0.00})");
                }
                var roots = scene.GetRootGameObjects().Where(g => g.name.EndsWith("Environment")).ToArray();
                Check(roots.Length == 1 && roots[0].name == EnvRoot && roots[0].activeSelf, $"exactly one environment root '{EnvRoot}' and visible ({roots.Length})");
                int rend = roots.Length > 0 ? roots[0].GetComponentsInChildren<Renderer>(false).Count(x => x.enabled) : 0;
                Check(rend > 0, $"environment has {rend} active renderers");
                session.SeekTo(course.Length * 0.5f);
                Check(session.Course.SegmentAt(session.DistanceM).Length > 0, $"mid-course section '{session.Course.SegmentAt(session.DistanceM)}'");
                foreach (var cp in course.Checkpoints) Debug.Log($"[shunta-int]   checkpoint {cp.Distance,8:0} m {cp.Name} ({cp.Segment})");
            }
            // other regions still travel-able
            var dir2 = Object.FindFirstObjectByType<RegionDirector>();
            foreach (var r in RegionCatalog.Regions.Where(r => r.Unlocked && r.Id != RegionCatalog.ShuntaMetro))
                Check(dir2 != null && dir2.CanTravelTo(r.Id), $"region {r.Id} still travel-able");
        }
        finally { region.BuiltCourseId = saved; }
        Finish();
    }

    static void Finish()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // discard FastTravel state changes
        if (_fails == 0) Debug.Log("[shunta-int] SELFTEST PASS");
        else { Debug.LogError($"[shunta-int] SELFTEST FAIL ({_fails})"); if (Application.isBatchMode) EditorApplication.Exit(2); }
    }
}

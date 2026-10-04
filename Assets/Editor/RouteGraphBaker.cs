using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes <c>Assets/Environment/SakuraPass/SakuraRoute.json</c> into
/// <c>Assets/Resources/SakuraRouteGraph.asset</c>.
///
/// The JSON is the pipeline's publication format (written by tools/blender/sakura_route.py);
/// the ScriptableObject is the runtime format. Baking rather than parsing JSON at play time
/// keeps the 6.7 km network out of the startup path and lets Unity serialize Vector3[] natively.
///
/// Run from the menu, or headless:
///   Unity.exe -projectPath ... -batchmode -quit -executeMethod RouteGraphBaker.Bake
/// </summary>
public static class RouteGraphBaker
{
    private const string RoutePath = "Assets/Environment/SakuraPass/SakuraRoute.json";
    private const string ShiosaiRoutePath = "Assets/Environment/ShiosaiCoast/ShiosaiRoute.json";
    private const string MapleCityRoutePath = "Assets/Environment/MapleCity/MapleRoute.json";
    private const string AzoraRoutePath = "Assets/Environment/AzoraHighlands/AzoraRoute.json";
    private const string TakaRoutePath = "Assets/Environment/TakaMountains/TakaRoute.json";
    private const string FujiRoutePath = "Assets/Environment/FujiRidge/FujiRoute.json";
    private const string MinatoRoutePath = "Assets/Environment/MinatoCoast/MinatoRoute.json";

    /// <summary>
    /// Every published route JSON, with the world-map region its segments and courses belong to.
    ///
    /// One baked <c>RouteGraph</c> holds every region: a region is just a group of segments and
    /// courses, so fast travel needs no second asset, no second Resources load and no scene
    /// swap - it is a course selection plus an environment toggle.
    /// </summary>
    private static readonly (string path, string region)[] Sources =
    {
        (RoutePath, RegionCatalog.SakuraPass),
        (ShiosaiRoutePath, RegionCatalog.ShiosaiCoast),
        (MapleCityRoutePath, RegionCatalog.MapleCity),
        ("Assets/Environment/MapleCity/MapleRouteEast.json", RegionCatalog.MapleCity),   // second district (Harbour & Market)
        (AzoraRoutePath, RegionCatalog.AzoraHighlands),
        (TakaRoutePath, RegionCatalog.TakaMountains),
        (FujiRoutePath, RegionCatalog.FujiRidge),
        (MinatoRoutePath, RegionCatalog.MinatoCoast),
        ("Assets/Environment/NagisaBay/NagisaRoute.json", "nagisa_bay"),   // B5 (copilot)
    };

    // ---------------------------------------------------------------- json DTOs

    [Serializable] private class SampleDto { public float[] p, t, s, u; public float bank, d; }
    [Serializable] private class SegmentDto { public string id, name; public SampleDto[] samples; }
    [Serializable] private class LegDto { public string segment; public float from, to; }
    [Serializable]
    private class CourseDto
    {
        public string id, name;
        public bool closed;
        public float targetMinutes;
        public LegDto[] legs;
    }
    [Serializable] private class CheckpointDto { public string segment, name; public float distance; }
    [Serializable]
    private class GraphDto
    {
        public float roadHalfWidth = 3.5f;
        public float shoulderWidth = 0.55f;
        public float climbLength;
        public float aozoraJunction;
        public SegmentDto[] segments;
        public CourseDto[] courses;
        public CheckpointDto[] checkpoints;
    }

    // ---------------------------------------------------------------- bake

    [MenuItem("MapleRide/Ride/Bake Route Graph", priority = 40)]
    public static void Bake()
    {
        var graph = BakeAsset();
        if (graph == null) return;
        Debug.Log($"[ride] baked route graph: {graph.segments.Length} segments, " +
                  $"{graph.courses.Length} courses, {graph.checkpoints.Length} checkpoints.");
    }

    /// <summary>Bakes and returns the asset. Safe to call repeatedly; overwrites in place.</summary>
    public static RouteGraph BakeAsset()
    {
        var main = AssetDatabase.LoadAssetAtPath<TextAsset>(RoutePath);
        if (main == null)
        {
            Debug.LogError($"[ride] {RoutePath} is missing. Run: " +
                           "blender -b -P tools/blender/build_all.py -- route");
            return null;
        }

        Directory.CreateDirectory("Assets/Resources");
        var graph = AssetDatabase.LoadAssetAtPath<RouteGraph>(RouteGraph.AssetPath);
        bool created = graph == null;
        if (created) graph = ScriptableObject.CreateInstance<RouteGraph>();

        var segs = new List<RouteSegmentData>();
        var courses = new List<CourseData>();
        var cps = new List<CheckpointData>();

        foreach (var (path, region) in Sources)
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (text == null)
            {
                // A region whose route has not been published yet is not an error: its world-map
                // pin simply stays locked.
                Debug.LogWarning($"[ride] {path} is missing - region '{region}' not baked.");
                continue;
            }

            var dto = JsonUtility.FromJson<GraphDto>(text.text);
            if (dto?.segments == null || dto.segments.Length == 0)
            {
                Debug.LogError($"[ride] {path} has no 'segments' array.");
                continue;
            }

            if (region == RegionCatalog.SakuraPass)
            {
                // The pass is still the reference region for the shared road cross-section and
                // for the climb/junction anchors every dressing window is measured against.
                graph.roadHalfWidth = dto.roadHalfWidth;
                graph.shoulderWidth = dto.shoulderWidth;
                graph.climbLength = dto.climbLength;
                graph.aozoraJunction = dto.aozoraJunction;
            }

            ReadInto(dto, region, segs, courses, cps);
        }

        graph.segments = segs.ToArray();
        graph.courses = courses.ToArray();
        graph.checkpoints = cps.ToArray();

        if (created) AssetDatabase.CreateAsset(graph, RouteGraph.AssetPath);
        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(RouteGraph.AssetPath, ImportAssetOptions.ForceSynchronousImport);
        RouteGraph.Invalidate();

        foreach (var s in graph.segments)
            Debug.Log($"[ride]   segment {s.id,-10} {s.Count,5} samples  {s.Length,8:0.0} m");
        foreach (var c in graph.courses)
        {
            var rc = RouteCourse.Build(graph, c);
            Debug.Log($"[ride]   course  {c.id,-16} [{c.regionId,-14}] {rc.Length / 1000f,6:0.00} km  " +
                      $"+{rc.Ascent,4:0} m  {rc.Checkpoints.Length,2} cp  closed={c.closed}");
        }
        return graph;
    }

    /// <summary>Appends one published route JSON's segments, courses and checkpoints.</summary>
    private static void ReadInto(GraphDto dto, string region, List<RouteSegmentData> segs,
                                 List<CourseData> courses, List<CheckpointData> cps)
    {
        foreach (var s in dto.segments)
        {
            if (s?.samples == null || s.samples.Length < 2) continue;
            int n = s.samples.Length;
            var seg = new RouteSegmentData
            {
                id = s.id,
                displayName = string.IsNullOrEmpty(s.name) ? s.id : s.name,
                position = new Vector3[n],
                tangent = new Vector3[n],
                side = new Vector3[n],
                up = new Vector3[n],
                distance = new float[n],
                bank = new float[n],
            };
            for (int i = 0; i < n; i++)
            {
                var q = s.samples[i];
                seg.position[i] = V(q.p);
                seg.tangent[i] = V(q.t).normalized;
                seg.side[i] = V(q.s).normalized;
                seg.up[i] = V(q.u).normalized;
                seg.distance[i] = q.d;
                seg.bank[i] = q.bank;
            }
            segs.Add(seg);
        }

        if (dto.courses != null)
        {
            foreach (var c in dto.courses)
            {
                if (c?.legs == null || c.legs.Length == 0) continue;
                var legs = new CourseLegData[c.legs.Length];
                for (int i = 0; i < c.legs.Length; i++)
                    legs[i] = new CourseLegData
                    {
                        segment = c.legs[i].segment,
                        from = c.legs[i].from,
                        to = c.legs[i].to,
                    };
                courses.Add(new CourseData
                {
                    id = c.id,
                    displayName = string.IsNullOrEmpty(c.name) ? c.id : c.name,
                    regionId = region,
                    closed = c.closed,
                    targetMinutes = c.targetMinutes <= 0f ? 30f : c.targetMinutes,
                    legs = legs,
                });
            }
        }

        if (dto.checkpoints != null)
        {
            foreach (var c in dto.checkpoints)
            {
                if (c == null || string.IsNullOrEmpty(c.segment)) continue;
                cps.Add(new CheckpointData
                {
                    segment = c.segment,
                    distance = c.distance,
                    displayName = string.IsNullOrEmpty(c.name) ? "Checkpoint" : c.name,
                });
            }
        }
    }

    private static Vector3 V(float[] a) =>
        a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// A4 perf probe for Azora Highlands (copilot, 2026-09-26; same method as FujiRidge.Fill2Capture).
/// Logs "[azora-perf]" lines:
///   * renderer / triangle / shadow-caster totals under the Azora root, and the top groups by
///     triangles and by shadow-casting triangles (what to fix first);
///   * per rider's-eye station (lake, both villages, river, ridge, col): frustum + LODGroup
///     visible renderers / tris and the median wall-clock ms of cam.Render()+ReadPixels
///     (GPU-synchronising; an editor proxy for frame cost, not a player FPS).
/// Frames: reference/good_graphics/azora_concept/perf/*.png. Nothing is saved.
///   run_steps.ps1 "AzoraPerfCapture.Run|copilot_azA4_perf.log|1"
/// </summary>
public static class AzoraPerfCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    static readonly (string, float)[] Stations =
    {
        ("lake", 1150f), ("village1", 2560f), ("river", 4550f), ("village2", 12560f), ("ridge", 16000f), ("col", 19600f),
    };

    [MenuItem("MapleRide/QA/Azora Perf Probe")]
    public static void Run()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/azora_concept/perf"));
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[azora-perf] no RegionDirector / route graph"); return; }
            regions.Resolve();
            var region = RegionCatalog.Regions.FirstOrDefault(r => r.Id == RegionCatalog.AzoraHighlands);
            var course = graph.BuildCourse(region.BuiltCourseId);
            regions.currentRegionId = region.Id;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
            var root = GameObject.Find(AzoraHighlandsEnvironment.RootName);
            if (root == null) { Debug.LogError("[azora-perf] no Azora root"); return; }

            long tris = 0, shadowTris = 0; int rend = 0, shadowRend = 0;
            var byGroup = new Dictionary<string, (int r, long t, long st)>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                long t = MeshTris(r);
                bool sh = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
                rend++; tris += t;
                if (sh) { shadowRend++; shadowTris += t; }
                var g = r.transform; while (g.parent != null && g.parent != root.transform) g = g.parent;
                byGroup.TryGetValue(g.name, out var e);
                byGroup[g.name] = (e.r + 1, e.t + t, e.st + (sh ? t : 0));
            }
            Debug.Log($"[azora-perf] TOTAL: {rend} renderers, {tris:N0} tris; shadow casters {shadowRend} renderers, " +
                      $"{shadowTris:N0} tris; LODGroups {root.GetComponentsInChildren<LODGroup>(false).Length}.");
            foreach (var kv in byGroup.OrderByDescending(k => k.Value.t).Take(14))
                Debug.Log($"[azora-perf] group '{kv.Key}': {kv.Value.r} renderers, {kv.Value.t:N0} tris, shadow-casting {kv.Value.st:N0} tris");

            var ms = new List<double>();
            foreach (var (name, d) in Stations)
            {
                if (d > course.Length) continue;
                var p = course.PositionAt(d);
                var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                var (vis, visTris, m) = PerfShot(root, Path.Combine(outDir, $"perf_{name}.png"),
                                                 p - t * 4.0f + Vector3.up * 1.9f, p + t * 25f + Vector3.up * 0.8f);
                ms.Add(m);
                Debug.Log($"[azora-perf] station {name} ({d:0} m): visible {vis} renderers, {visTris:N0} tris, render {m:0.0} ms (median of 7).");
            }
            Debug.Log($"[azora-perf] MEAN render ms over {ms.Count} stations: {ms.Average():0.0}; MAX {ms.Max():0.0}.");
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static long MeshTris(Renderer r)
    {
        Mesh m = null;
        if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
        else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
        if (m == null) return 0;
        long t = 0;
        for (int s = 0; s < m.subMeshCount; s++) t += (long)m.GetIndexCount(s) / 3;
        return t;
    }

    static (int vis, long tris, double ms) PerfShot(GameObject root, string file, Vector3 pos, Vector3 look)
    {
        const int W = 1280, H = 720;
        var go = new GameObject("~AzoraPerfCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look);
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 20000f; cam.allowHDR = true;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;

        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var inLod = new HashSet<Renderer>(); var lodVisible = new HashSet<Renderer>();
        float k2 = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        foreach (var g in root.GetComponentsInChildren<LODGroup>(false))
        {
            var lods = g.GetLODs();
            foreach (var l in lods) foreach (var r in l.renderers) if (r != null) inLod.Add(r);
            var refp = g.transform.TransformPoint(g.localReferencePoint);
            float sc = Mathf.Max(g.transform.lossyScale.x, Mathf.Max(g.transform.lossyScale.y, g.transform.lossyScale.z));
            float rel = g.size * sc / (Vector3.Distance(pos, refp) * k2 + 1e-4f);
            for (int i = 0; i < lods.Length; i++)
                if (rel >= lods[i].screenRelativeTransitionHeight)
                { foreach (var r in lods[i].renderers) if (r != null) lodVisible.Add(r); break; }
        }
        int vis = 0; long vt = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (inLod.Contains(r) && !lodVisible.Contains(r)) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            vis++; vt += MeshTris(r);
        }

        var img = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var times = new List<double>();
        var old = RenderTexture.active;
        for (int n = 0; n < 8; n++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cam.Render();
            RenderTexture.active = rt;
            img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            img.Apply();
            sw.Stop();
            if (n > 0) times.Add(sw.Elapsed.TotalMilliseconds);
        }
        File.WriteAllBytes(file, img.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        times.Sort();
        return (vis, vt, times[times.Count / 2]);
    }
}

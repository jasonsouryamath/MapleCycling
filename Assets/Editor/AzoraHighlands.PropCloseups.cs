using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// A3/A5 prop close-ups for Azora Highlands (copilot, 2026-09-26). The main-file props are merged
/// meshes (all gates in one mesh, all cairns in one ...), so each target is located by clustering
/// the world-space vertices of the matching renderers and framing a cluster from two sides.
/// Frames: reference/good_graphics/azora_concept/props/*.png. Nothing is saved.
///   run_steps.ps1 "AzoraPropCloseups.Capture|copilot_azA3_cap.log|1"
/// </summary>
public static class AzoraPropCloseups
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    // label, renderer-name substring, material-name substring (or null), cluster indexes, view distance, eye height
    static readonly (string, string, string, int[], float, float)[] Targets =
    {
        ("gate",       "Field Gates",     null,               new[] { 0, 3 }, 6f,  1.6f),
        ("grid",       "Cattle Grids",    null,               new[] { 0 },    7f,  1.8f),
        ("cairn",      "Cairns",          null,               new[] { 0, 4 }, 5f,  1.4f),
        ("post",       "Marker Posts",    null,               new[] { 2 },    4f,  1.4f),
        ("colmarker",  "Col View Marker", null,               new[] { 0 },    6f,  1.8f),
        ("shephut",    "Hut Walls",       null,               new[] { 0 },    11f, 2.6f),
        ("sheep",      "Sheep Fleece",    null,               new[] { 0 },    5f,  1.2f),
        ("alphut",     "AlpBox",          "Azora_Vil_Awning", new[] { 0, 2 }, 16f, 4f),
        ("alpdoor",    "AlpBox",          "Azora_Vil_Door",   new[] { 0, 2 }, 9f,  2.2f),
        ("shepdoor",   "Hut Detail",      "Azora_Vil_Door",   new[] { 0 },    7f,  1.8f),
    };

    [MenuItem("MapleRide/QA/Azora Prop Close-ups")]
    public static void Capture()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/azora_concept/props"));
        Directory.CreateDirectory(outDir);
        int n = 0;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            if (regions == null) { Debug.LogError("[azora-props] no RegionDirector"); return; }
            regions.Resolve();
            var region = RegionCatalog.Regions.FirstOrDefault(r => r.Id == RegionCatalog.AzoraHighlands);
            regions.currentRegionId = region.Id;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
            var root = GameObject.Find(AzoraHighlandsEnvironment.RootName);
            if (root == null) { Debug.LogError("[azora-props] no Azora root"); return; }
            var all = root.GetComponentsInChildren<MeshRenderer>(false);

            foreach (var (label, rname, mname, picks, dist, eyeH) in Targets)
            {
                var pts = new List<Vector3>();
                foreach (var r in all)
                {
                    if (!r.name.Contains(rname)) continue;
                    if (mname != null && (r.sharedMaterial == null || !r.sharedMaterial.name.StartsWith(mname))) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var vs = mf.sharedMesh.vertices;
                    for (int k = 0; k < vs.Length; k += 3) pts.Add(r.transform.TransformPoint(vs[k]));
                }
                var clusters = Cluster(pts, 12f);
                Debug.Log($"[azora-props] {label}: {pts.Count} sample verts, {clusters.Count} clusters");
                foreach (int ci in picks)
                {
                    if (ci >= clusters.Count) continue;
                    var c = clusters[ci];
                    int sides = label.EndsWith("door") ? 4 : 2;
                    for (int side = 0; side < sides; side++)
                    {
                        float az = (35f + side * (sides == 4 ? 90f : 180f)) * Mathf.Deg2Rad;
                        var dir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                        var eye = c + dir * dist + Vector3.up * eyeH;
                        if (Physics.Raycast(eye + Vector3.up * 200f, Vector3.down, out var hit, 400f) && eye.y < hit.point.y + 1.2f)
                            eye.y = hit.point.y + 1.2f;
                        string file = Path.Combine(outDir, $"az_prop_{label}_{ci}_{(char)('a' + side)}.png");
                        Shot(file, eye, c + Vector3.up * 0.6f);
                        Debug.Log($"[azora-props] {label} #{ci} at ({c.x:0.0},{c.y:0.0},{c.z:0.0}) -> {Path.GetFileName(file)}");
                        n++;
                    }
                }
            }
            Debug.Log($"[azora-props] RESULT {n} frames -> {outDir}");
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    /// <summary>
    /// Pixel probe: which renderers' bounds does the ray through a concept-capture pixel cross?
    /// Env AZ_PROBE = "d back side up ahead lside lup px,py px,py ..." (1280x720, FOV 60).
    /// </summary>
    public static void Probe()
    {
        var spec = System.Environment.GetEnvironmentVariable("AZ_PROBE");
        if (string.IsNullOrWhiteSpace(spec)) { Debug.LogError("[azora-probe] set AZ_PROBE"); return; }
        var f = spec.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
        float F(int i) => float.Parse(f[i], System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            regions.Resolve();
            var region = RegionCatalog.Regions.FirstOrDefault(r => r.Id == RegionCatalog.AzoraHighlands);
            var course = graph.BuildCourse(region.BuiltCourseId);
            regions.currentRegionId = region.Id;
            regions.ApplyEnvironmentVisibility();
            float d = F(0);
            var p = course.PositionAt(d);
            var t = course.TangentAt(d); t.y = 0f; t.Normalize();
            var s = new Vector3(t.z, 0f, -t.x);
            var eye = p - t * F(1) + s * F(2) + Vector3.up * F(3);
            var look = p + t * F(4) + s * F(5) + Vector3.up * F(6);
            var go = new GameObject("~probeCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f; cam.aspect = 1280f / 720f; cam.pixelRect = new Rect(0, 0, 1280, 720);
            cam.transform.position = eye; cam.transform.LookAt(look);
            var rends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int k = 7; k < f.Length; k++)
            {
                var xy = f[k].Split(',');
                float px = float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture);
                float py = float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture);
                var ray = cam.ScreenPointToRay(new Vector3(px, 720f - py, 0f));
                var hits = new List<(float, string)>();
                foreach (var r in rends)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                    if (r.bounds.IntersectRay(ray, out float dist))
                    {
                        var path = r.name; var tr = r.transform.parent;
                        for (int q = 0; q < 3 && tr != null; q++, tr = tr.parent) path = tr.name + "/" + path;
                        hits.Add((dist, $"{path} [{r.sharedMaterial?.name}] size={r.bounds.size}"));
                    }
                }
                foreach (var h in hits.OrderBy(h => h.Item1).Take(14))
                    Debug.Log($"[azora-probe] px({px},{py}) {h.Item1:0}m {h.Item2}");
            }
            Object.DestroyImmediate(go);
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    /// <summary>Greedy leader clustering in XZ; returns cluster centroids in discovery order.</summary>
    static List<Vector3> Cluster(List<Vector3> pts, float radius)
    {
        var leaders = new List<Vector3>();
        var sums = new List<Vector3>();
        var counts = new List<int>();
        float r2 = radius * radius;
        foreach (var p in pts)
        {
            int hit = -1;
            for (int i = 0; i < leaders.Count; i++)
            {
                float dx = p.x - leaders[i].x, dz = p.z - leaders[i].z;
                if (dx * dx + dz * dz < r2) { hit = i; break; }
            }
            if (hit < 0) { leaders.Add(p); sums.Add(p); counts.Add(1); }
            else { sums[hit] += p; counts[hit]++; }
        }
        var res = new List<Vector3>();
        for (int i = 0; i < leaders.Count; i++) res.Add(sums[i] / counts[i]);
        return res;
    }

    static void Shot(string file, Vector3 pos, Vector3 look, int w = 1280, int h = 720)
    {
        var go = new GameObject("~AzoraPropCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 9000f;
        cam.allowHDR = true;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGBA32, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(file, img.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }
}

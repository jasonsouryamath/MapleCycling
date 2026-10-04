using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// fuji2 (2026-09-26): close-up figure checks + a perf probe for Fuji Ridge.
/// CaptureFill2Before / CaptureFill2After write reference/good_graphics/fuji_fill2/{before|after}_*.png
/// and log "[fuji2-perf]" lines: renderer / triangle / shadow-caster totals under the Fuji root, and
/// per rider's-eye station the frustum+LOD visible renderers/tris and the median wall-clock ms of
/// cam.Render()+ReadPixels (GPU-synchronising; an editor proxy for frame cost, not a player FPS).
/// Nothing is saved.
/// </summary>
public static partial class FujiRidgeEnvironment
{
    static readonly float[] PerfFractions = { 0.03f, 0.08f, 0.25f, 0.45f, 0.65f, 0.85f };

    public static void CaptureFill2Before() => CaptureFill2("before");
    public static void CaptureFill2After() => CaptureFill2("after");

    static void CaptureFill2(string tag)
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/fuji_fill2"));
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[fuji2] no RegionDirector / route graph"); return; }
            regions.Resolve();
            var region = RegionCatalog.Regions.FirstOrDefault(r => r.Id.ToLowerInvariant().Contains("fuji"));
            var course = graph.BuildCourse(region.BuiltCourseId);
            regions.currentRegionId = region.Id;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
            var root = GameObject.Find(RootName);
            if (root == null) { Debug.LogError("[fuji2] no Fuji root"); return; }

            // ---------------- totals
            long tris = 0, shadowTris = 0; int rend = 0, shadowRend = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                long t = MeshTris(r);
                rend++; tris += t;
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) { shadowRend++; shadowTris += t; }
            }
            Debug.Log($"[fuji2-perf] {tag} TOTAL: {rend} renderers, {tris} tris; shadow casters {shadowRend} renderers, {shadowTris} tris; " +
                      $"LODGroups {root.GetComponentsInChildren<LODGroup>(false).Length}.");

            // ---------------- rider's-eye stations: visible counts + render ms
            var msAll = new List<double>();
            for (int k = 0; k < PerfFractions.Length; k++)
            {
                float d = course.Length * PerfFractions[k];
                var p = course.PositionAt(d);
                var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                var eye = p - t * 4.0f + Vector3.up * 1.9f;
                var look = p + t * 25f + Vector3.up * 0.8f;
                var (vis, visTris, ms) = PerfShot(root, Path.Combine(outDir, $"{tag}_eye_{k + 1:00}.png"), eye, look);
                msAll.Add(ms);
                Debug.Log($"[fuji2-perf] {tag} station {PerfFractions[k]:0.00} ({d:0} m): visible {vis} renderers, {visTris} tris, render {ms:0.0} ms (median of 7).");
            }
            Debug.Log($"[fuji2-perf] {tag} MEAN render ms over {msAll.Count} stations: {msAll.Average():0.0}.");

            // ---------------- close-ups (4-6 m)
            var route = FujiRoute.Load();
            Vector3 ToRoad(Vector3 q)
            {
                float best = float.MaxValue; Vector3 bp = q;
                for (int i = 0; i < route.Count; i += 2)
                {
                    float sq = (route.Position[i] - q).sqrMagnitude;
                    if (sq < best) { best = sq; bp = route.Position[i]; }
                }
                var v = bp - q; v.y = 0f; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
            }
            var all = root.GetComponentsInChildren<Transform>(false);
            List<Transform> Named(string prefix) => all.Where(x => x.name.StartsWith(prefix)).ToList();
            var cust = Named("Fuji Cafe Customer ");
            var riders = Named("Fuji Rifugio Rider ");
            var strollers = Named("Fuji Stroller ");
            var tstand = Named("Fuji Cafe Stander ");
            Debug.Log($"[fuji2] figures: {cust.Count} customers, {tstand.Count} table standers, {strollers.Count} strollers, {riders.Count} rifugio riders.");

            var piazza = route.Position[route.IndexAt(PiazzaM)];
            var rifugio = route.Position[route.IndexAt(RifugioM)];
            var picks = new List<(string, Transform)>();
            if (cust.Count > 0) picks.Add(("cafe_town_a", cust[0]));
            if (cust.Count > 7) picks.Add(("cafe_town_b", cust[7]));
            var cp = cust.OrderBy(x => (x.position - piazza).sqrMagnitude).FirstOrDefault();
            if (cp != null) picks.Add(("cafe_piazza", cp));
            var cr = cust.OrderBy(x => (x.position - rifugio).sqrMagnitude).FirstOrDefault();
            if (cr != null) picks.Add(("cafe_rifugio", cr));
            if (strollers.Count > 0) picks.Add(("piazza_stroller", strollers[0]));
            if (tstand.Count > 0) picks.Add(("table_stander", tstand[0]));
            for (int k = 0; k < riders.Count && k < 3; k++) picks.Add(($"rifugio_rider_{k}", riders[k * 2 % riders.Count]));

            foreach (var (name, f) in picks)
            {
                var toRoad = ToRoad(f.position);
                var side = Quaternion.Euler(0f, 35f, 0f) * toRoad;
                ReviewShot(Path.Combine(outDir, $"{tag}_close_{name}.png"), f.position + side * 4.6f + Vector3.up * 1.45f,
                           f.position + Vector3.up * 0.7f);
                var fr = f.forward; fr.y = 0f; fr.Normalize();
                ReviewShot(Path.Combine(outDir, $"{tag}_close_{name}_front.png"), f.position + fr * 4.2f + Vector3.up * 1.3f,
                           f.position + Vector3.up * 0.7f);
            }
            // Wide town + piazza + rifugio + upper slopes.
            {
                int ip = route.IndexAt(PiazzaM);
                var sf = -route.SideFlat(ip);
                var pc = piazza + sf * (CorridorClearM + 17f);
                ReviewShot(Path.Combine(outDir, $"{tag}_piazza_wide.png"), piazza - sf * 2f + Vector3.up * 2.2f + route.Tangent[ip] * -6f, pc + Vector3.up * 1f);
                {
                    // copilot F3: the whole campanile, and a close look at its belfry and cap.
                    var cfwd = -Vector3.Cross(sf, Vector3.up).normalized;
                    var cc = pc + sf * 10f + cfwd * 12f;
                    cc.y = PaveY(route, cc, PiazzaLiftM);   // camera aim only (Height() needs the build-time road grid)
                    ReviewShot(Path.Combine(outDir, $"{tag}_campanile.png"), cc - sf * 30f - cfwd * 18f + Vector3.up * 6f, cc + Vector3.up * 15f);
                    ReviewShot(Path.Combine(outDir, $"{tag}_campanile_belfry.png"), cc - sf * 11f - cfwd * 7f + Vector3.up * 18f, cc + Vector3.up * 24f);
                }
                int ir = route.IndexAt(RifugioM);
                ReviewShot(Path.Combine(outDir, $"{tag}_rifugio_wide.png"), rifugio - route.Tangent[ir] * 12f + Vector3.up * 3f, rifugio + route.Tangent[ir] * 6f);
                foreach (float fr in new[] { 0.55f, 0.75f, 0.9f })
                {
                    float d = course.Length * fr;
                    var p = course.PositionAt(d);
                    var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                    var s = Vector3.Cross(Vector3.up, t);
                    ReviewShot(Path.Combine(outDir, $"{tag}_slopes_{fr:0.00}.png"), p + Vector3.up * 2f - t * 2f, p + s * 120f + t * 60f + Vector3.up * 5f);
                }
            }
            Debug.Log($"[fuji2] {tag}: frames written to {outDir}");
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
        var go = new GameObject("~Fuji2PerfCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look);
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 20000f; cam.allowHDR = true;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;

        // Visible estimate: frustum test + LODGroup selection by screen-relative height.
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
            if (!r.enabled) continue;
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

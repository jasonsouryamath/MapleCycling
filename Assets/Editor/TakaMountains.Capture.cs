using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Taka-only rider's-eye review capture (same camera as RegionReviewCapture, which shoots every
/// region and would overwrite the other regions' review frames). Nothing is saved.
/// Writes reference/good_graphics/taka_fill2/taka_&lt;n&gt;.png (claude-taka2; was taka_fill)
/// </summary>
public static partial class TakaMountainsEnvironment
{
    static readonly float[] FillFractions = { 0.03f, 0.08f, 0.17f, 0.25f, 0.36f, 0.45f, 0.58f, 0.65f, 0.71f, 0.76f, 0.85f, 0.93f, 0.3032f, 0.4264f, 0.6973f, 0.0100f };

    [MenuItem("MapleRide/QA/Capture Taka Fill Review")]
    public static void CaptureFill()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/taka_fill2"));
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[taka-fill] no RegionDirector / route graph"); return; }
            regions.Resolve();
            var course = graph.BuildCourse("taka_high_road");
            if (course == null) { Debug.LogError("[taka-fill] no course"); return; }
            regions.currentRegionId = RegionCatalog.TakaMountains;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
            for (int k = 0; k < FillFractions.Length; k++)
            {
                float d = course.Length * FillFractions[k];
                var p = course.PositionAt(d);
                var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                var eye = p - t * 4.0f + Vector3.up * 1.9f;
                var look = p + t * 25f + Vector3.up * 0.8f;
                FillShot(Path.Combine(outDir, $"taka_{k + 1:00}.png"), eye, look);
            }
            // two wide views: looking out sideways from the summit plateau and back down the climb
            {
                float d = course.Length * 0.72f;
                var p = course.PositionAt(d);
                var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                var side = Vector3.Cross(Vector3.up, t);
                FillShot(Path.Combine(outDir, "taka_side_summit.png"), p + Vector3.up * 2f, p + side * 60f + Vector3.up * 4f);
                d = course.Length * 0.30f; p = course.PositionAt(d);
                t = course.TangentAt(d); t.y = 0f; t.Normalize(); side = Vector3.Cross(Vector3.up, t);
                FillShot(Path.Combine(outDir, "taka_side_climb.png"), p + Vector3.up * 2f, p - side * 60f + Vector3.up * 2f);
            }
            ExtraShots(course, outDir);
            Debug.Log($"[taka-fill] {FillFractions.Length + 2} frames + extras -> {outDir}");
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    /// <summary>claude-taka2: summit tower, shop fronts and guardrail frames, found from the
    /// built geometry (renderer bounds) rather than hard-coded positions.</summary>
    static void ExtraShots(RouteCourse course, string outDir)
    {
        var rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        MeshRenderer Find(string prefix)
        {
            foreach (var r in rends) if (r != null && r.name.StartsWith(prefix, System.StringComparison.Ordinal)) return r;
            return null;
        }
        // ---- summit tower
        var tower = Find("VTowerRed");
        if (tower == null) Debug.LogWarning("[taka-fill] no VTowerRed renderer - tower MISSING");
        else
        {
            var b = tower.bounds;
            float m = NearestM(course, b.center);
            Debug.Log($"[taka-fill] tower bounds centre {b.center:F1} size {b.size:F1}, nearest course {m:0} m");
            var from = course.PositionAt(Mathf.Max(0f, m - 90f)) + Vector3.up * 2.5f;
            FillShot(Path.Combine(outDir, "taka_tower_approach.png"), from, b.center + Vector3.up * 8f);
            var side = b.center - course.PositionAt(m); side.y = 0f; side.Normalize();
            var wide = course.PositionAt(m) - side * 70f + Vector3.up * 25f;
            FillShot(Path.Combine(outDir, "taka_tower_wide.png"), wide, b.center + Vector3.up * 5f);
            FillShot(Path.Combine(outDir, "taka_tower_close.png"), course.PositionAt(m + 25f) + Vector3.up * 1.8f, b.center + Vector3.up * 3f);
        }
        // ---- shop fronts / summit sign
        int k = 0;
        foreach (var r in rends)
        {
            if (r == null) continue;
            string n = r.name;
            if (!(n.StartsWith("ShopSign") || n.StartsWith("SummitSign") || n.StartsWith("CrepesSign"))) continue;
            var c = r.bounds.center;
            float m = NearestM(course, c);
            var eye = course.PositionAt(Mathf.Max(0f, m - 16f)) + Vector3.up * 1.8f;
            FillShot(Path.Combine(outDir, $"taka_life_{n.Replace(" ", "_")}.png"), eye, c - Vector3.up * 1.6f);
            Debug.Log($"[taka-fill] life shot {n} at course {m:0} m");
            k++;
        }
        // ---- guardrail: three samples spread over the built rail meshes
        var rails = new System.Collections.Generic.List<Vector3>();
        foreach (var r in rends)
        {
            if (r == null || !r.name.StartsWith("Guardrail")) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var v = mf.sharedMesh.vertices;
            for (int q = 0; q < v.Length; q += Mathf.Max(1, v.Length / 4)) rails.Add(r.transform.TransformPoint(v[q]));
        }
        Debug.Log($"[taka-fill] guardrail sample points: {rails.Count}");
        for (int q = 0; q < 3 && rails.Count > 0; q++)
        {
            var pt = rails[(q * 2 + 1) * rails.Count / 6];
            float m = NearestM(course, pt);
            var eye = course.PositionAt(Mathf.Max(0f, m - 22f)) + Vector3.up * 1.9f;
            FillShot(Path.Combine(outDir, $"taka_rail_{q + 1}.png"), eye, pt + Vector3.up * 0.3f);
            Debug.Log($"[taka-fill] rail shot {q + 1} at course {m:0} m ({pt:F1})");
        }
        // ---- road names close up (every 230 m from 3000 m)
        for (int q = 0; q < 2; q++)
        {
            float m = 3000f + 230f * (4 + q * 7) + 2f;
            FillShot(Path.Combine(outDir, $"taka_names_{q + 1}.png"), course.PositionAt(m - 9f) + Vector3.up * 2.2f,
                     course.PositionAt(m + 2f));
        }
        // ---- lavender fields / olive groves / chalet forecourt, aimed at the TakaShot_* markers
        // the build leaves at each plot (copilot session 3, T1/T2/T4). The old "first vertex of
        // the first VLavender mesh" shot landed on whatever plot came first and usually looked
        // at a pine trunk.
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (tr == null || !tr.name.StartsWith("TakaShot_", System.StringComparison.Ordinal)) continue;
            var target = tr.position;
            float m = NearestM(course, target);
            var road = course.PositionAt(m);
            var dir = target - road; dir.y = 0f;
            float dist = dir.magnitude; dir = dist > 0.01f ? dir / dist : tr.forward;
            string tag = tr.name.Substring("TakaShot_".Length).ToLowerInvariant();
            if (tag == "chalet")
            {
                FillShot(Path.Combine(outDir, "taka_chalet_approach.png"), course.PositionAt(Mathf.Max(0f, m - 45f)) + Vector3.up * 1.8f,
                         target + Vector3.up * 0.5f);
                FillShot(Path.Combine(outDir, "taka_chalet_front.png"), road - dir * 1.5f + Vector3.up * 1.7f, target);
                FillShot(Path.Combine(outDir, "taka_chalet_past.png"), course.PositionAt(Mathf.Min(course.Length, m + 30f)) + Vector3.up * 1.8f,
                         target + Vector3.up * 0.5f);
            }
            else
            {
                // from the verge at rider height, and a raised three-quarter view of the whole plot
                FillShot(Path.Combine(outDir, $"taka_prov_{tag}_verge.png"), road + dir * 3.2f + Vector3.up * 1.9f, target + Vector3.up * 0.6f);
                var along = Vector3.Cross(Vector3.up, dir);
                FillShot(Path.Combine(outDir, $"taka_prov_{tag}_high.png"), road - dir * 4f + along * 14f + Vector3.up * 11f, target);
            }
            Debug.Log($"[taka-fill] {tr.name} shot at course {m:0} m, {dist:0} m off the road, target {target:F1}");
        }
        // ---- lavender / lower slopes and the moonscape, looking out sideways
        foreach (var (m, tag) in new[] { (900f, "lower_slopes"), (1800f, "lavender"), (12500f, "moonscape"), (14200f, "moonscape2") })
        {
            var p = course.PositionAt(m);
            var side = course.SideAt(m);
            FillShot(Path.Combine(outDir, $"taka_{tag}_L.png"), p + Vector3.up * 3f, p - side * 60f + Vector3.up * 0f);
            FillShot(Path.Combine(outDir, $"taka_{tag}_R.png"), p + Vector3.up * 3f, p + side * 60f + Vector3.up * 0f);
        }
    }

    static float NearestM(RouteCourse course, Vector3 p)
    {
        float best = 0f, bestD = float.MaxValue;
        for (float m = 0f; m <= course.Length; m += 4f)
        {
            float d = (course.PositionAt(m) - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    static void FillShot(string file, Vector3 pos, Vector3 look, int w = 1280, int h = 720)
    {
        var go = new GameObject("~TakaFillCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 9000f; cam.allowHDR = true;
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
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }
}

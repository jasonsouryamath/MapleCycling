using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Azora-only version of RegionReviewCapture (same rider's-eye chase camera), so an Azora pass can
/// be verified without re-shooting every region. Writes reference/good_graphics/azora_fill/azora_&lt;n&gt;.png.
/// Nothing is saved.
/// </summary>
public static class AzoraFillCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    static readonly float[] Fractions = { 0.03f, 0.08f, 0.18f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f, 0.75f, 0.85f, 0.95f };

    [MenuItem("MapleRide/QA/Capture Azora Fill Review")]
    public static void Capture()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/azora_fill2"));
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[azora-fill] no RegionDirector / route graph"); return; }
            regions.Resolve();
            foreach (var region in RegionCatalog.Regions)
            {
                if (!region.Id.ToLowerInvariant().Contains("azora")) continue;
                var course = graph.BuildCourse(region.BuiltCourseId);
                if (course == null) { Debug.LogWarning($"[azora-fill] {region.Id}: no course"); continue; }
                regions.currentRegionId = region.Id;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
                for (int k = 0; k < Fractions.Length; k++)
                {
                    float d = course.Length * Fractions[k];
                    var p = course.PositionAt(d);
                    var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                    var eye = p - t * 4.0f + Vector3.up * 1.9f;
                    var look = p + t * 25f + Vector3.up * 0.8f;
                    Shot(Path.Combine(outDir, $"azora_{k + 1:D2}.png"), eye, look);
                }
                // Village frames (villages centred at route metres 2600 and 12600).
                foreach (float vd in new[] { 2380f, 2560f, 12380f, 12560f })
                {
                    if (vd >= course.Length) continue;
                    var p = course.PositionAt(vd);
                    var t = course.TangentAt(vd); t.y = 0f; t.Normalize();
                    Shot(Path.Combine(outDir, $"azora_village_{vd:0}.png"), p - t * 4.0f + Vector3.up * 1.9f, p + t * 25f + Vector3.up * 0.8f);
                }
                // Close-ups (claude-azora2): chalet ground floors on both sides, cafe/bakery terraces, shrubs.
                foreach (float vd in new[] { 2540f, 12540f })
                {
                    var p = course.PositionAt(vd);
                    var t = course.TangentAt(vd); t.y = 0f; t.Normalize();
                    var side = new Vector3(t.z, 0f, -t.x);
                    Shot(Path.Combine(outDir, $"azora_chalets_L_{vd:0}.png"), p + side * 1.5f + Vector3.up * 1.7f, p - side * 12f + t * 9f + Vector3.up * 2.2f);
                    Shot(Path.Combine(outDir, $"azora_chalets_R_{vd:0}.png"), p - side * 1.5f + Vector3.up * 1.7f, p + side * 12f + t * 9f + Vector3.up * 2.2f);
                }
                foreach (float vc in new[] { 2600f, 12600f })
                {
                    var p = course.PositionAt(vc - 14f);
                    var t = course.TangentAt(vc); t.y = 0f; t.Normalize();
                    var side = new Vector3(t.z, 0f, -t.x);
                    var c = course.PositionAt(vc);
                    Shot(Path.Combine(outDir, $"azora_cafe_L_{vc:0}.png"), p + side * 1.0f + Vector3.up * 1.8f, c - side * 7f + Vector3.up * 1.0f);
                    Shot(Path.Combine(outDir, $"azora_cafe_R_{vc:0}.png"), p - side * 1.0f + Vector3.up * 1.8f, c + side * 7f + Vector3.up * 1.0f);
                    var b = course.PositionAt(vc - 40f);
                    var pb = course.PositionAt(vc - 56f);
                    Shot(Path.Combine(outDir, $"azora_bakery_L_{vc:0}.png"), pb + side * 1.0f + Vector3.up * 1.8f, b - side * 7f + Vector3.up * 1.0f);
                    Shot(Path.Combine(outDir, $"azora_bakery_R_{vc:0}.png"), pb - side * 1.0f + Vector3.up * 1.8f, b + side * 7f + Vector3.up * 1.0f);
                }
                foreach (float fr in new[] { 0.25f, 0.70f, 0.85f })
                {
                    float d = course.Length * fr;
                    var p = course.PositionAt(d);
                    var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                    var side = new Vector3(t.z, 0f, -t.x);
                    Shot(Path.Combine(outDir, $"azora_verge_{fr * 100f:0}.png"), p + side * 3f + Vector3.up * 1.4f, p + side * 14f + t * 10f);
                    Shot(Path.Combine(outDir, $"azora_verge_{fr * 100f:0}_L.png"), p - side * 3f + Vector3.up * 1.4f, p - side * 14f + t * 10f);
                    Shot(Path.Combine(outDir, $"azora_high_{fr * 100f:0}.png"), p - t * 40f + side * 25f + Vector3.up * 28f, p + t * 80f);
                }
                {
                    var p = course.PositionAt(2600f);
                    var t = course.TangentAt(2600f); t.y = 0f; t.Normalize();
                    var side = new Vector3(t.z, 0f, -t.x);
                    Shot(Path.Combine(outDir, "azora_village_overview.png"), p - t * 90f - side * 30f + Vector3.up * 30f, p + t * 20f);
                }
                // One elevated overview looking along the climb.
                {
                    float d = course.Length * 0.3f;
                    var p = course.PositionAt(d);
                    var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                    var side = new Vector3(t.z, 0f, -t.x);
                    Shot(Path.Combine(outDir, "azora_overview.png"), p - t * 60f + side * 40f + Vector3.up * 35f, p + t * 120f);
                }
                Debug.Log($"[azora-fill] {region.Id}: frames along '{region.BuiltCourseId}' ({course.Length:0} m)");
            }
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static void Shot(string file, Vector3 pos, Vector3 look, int w = 1280, int h = 720)
    {
        var go = new GameObject("~AzoraReviewCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 6000f;
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

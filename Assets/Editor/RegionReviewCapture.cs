using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// CAPTURE-ONLY art review of every built region from the same rider's-eye chase camera, so the
/// regions can be compared side by side for low-detail props and empty / far-away space
/// (2026-09-24). For each region: switch RegionDirector to it (visibility + ambience), build its
/// course, and shoot five frames along the route. Nothing is saved.
/// Writes reference/good_graphics/region_review/&lt;region&gt;_&lt;n&gt;.png
/// </summary>
public static class RegionReviewCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    static readonly float[] Fractions = { 0.08f, 0.25f, 0.45f, 0.65f, 0.85f };

    [MenuItem("MapleRide/QA/Capture Region Review (All Regions)")]
    public static void Capture()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath,
                                                      "../reference/good_graphics/region_review"));
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[review] no RegionDirector / route graph"); return; }
            regions.Resolve();
            foreach (var region in RegionCatalog.Regions)
            {
                if (!region.Unlocked) continue;
                var course = graph.BuildCourse(region.BuiltCourseId);
                if (course == null) { Debug.LogWarning($"[review] {region.Id}: no course"); continue; }
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
                    Shot(Path.Combine(outDir, $"{region.Id}_{k + 1}.png"), eye, look);
                }
                Debug.Log($"[review] {region.Id}: 5 frames along '{region.BuiltCourseId}' ({course.Length:0} m)");
            }
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static void Shot(string file, Vector3 pos, Vector3 look, int w = 1280, int h = 720)
    {
        var go = new GameObject("~ReviewCamera");
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

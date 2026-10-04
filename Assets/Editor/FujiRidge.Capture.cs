using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fuji-only rider's-eye review frames (same camera recipe as RegionReviewCapture, but only this
/// region, more stations, a longer far clip so the summit cone is not clipped, and a wide shot).
/// Writes reference/good_graphics/fuji_pilgrimage/fuji_&lt;n&gt;.png. Nothing is saved.
/// </summary>
public static partial class FujiRidgeEnvironment
{
    static readonly float[] ReviewFractions = { 0.03f, 0.08f, 0.18f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f, 0.75f, 0.85f, 0.95f };

    [MenuItem("MapleRide/QA/Capture Fuji Pilgrimage Review")]
    public static void CaptureReview()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath,
                                                      "../reference/good_graphics/fuji_pilgrimage"));
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[fuji-review] no RegionDirector / route graph"); return; }
            regions.Resolve();
            foreach (var region in RegionCatalog.Regions)
            {
                if (!region.Id.ToLowerInvariant().Contains("fuji")) continue;
                var course = graph.BuildCourse(region.BuiltCourseId);
                if (course == null) { Debug.LogWarning($"[fuji-review] {region.Id}: no course"); continue; }
                regions.currentRegionId = region.Id;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
                for (int k = 0; k < ReviewFractions.Length; k++)
                {
                    float d = course.Length * ReviewFractions[k];
                    var p = course.PositionAt(d);
                    var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                    var eye = p - t * 4.0f + Vector3.up * 1.9f;
                    var look = p + t * 25f + Vector3.up * 0.8f;
                    ReviewShot(Path.Combine(outDir, $"fuji_{k + 1:00}.png"), eye, look);
                }
                // Wide, raised look back from 0.6 of the route, and a finish view.
                {
                    float d = course.Length * 0.6f;
                    var p = course.PositionAt(d);
                    var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                    var side = Vector3.Cross(Vector3.up, t);
                    ReviewShot(Path.Combine(outDir, "fuji_wide.png"), p - t * 30f + side * 12f + Vector3.up * 14f,
                               p + t * 60f);
                }
                Debug.Log($"[fuji-review] {region.Id}: frames along '{region.BuiltCourseId}' ({course.Length:0} m)");
            }
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static void ReviewShot(string file, Vector3 pos, Vector3 look, int w = 1280, int h = 720)
    {
        var go = new GameObject("~FujiReviewCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20000f;
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

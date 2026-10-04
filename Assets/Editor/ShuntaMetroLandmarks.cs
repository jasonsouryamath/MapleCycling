using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Applies the high-fidelity Shunta Metro landmark set (<see cref="ShuntaLandmarkSet"/>) to the playable scene.
///   ShuntaMetroLandmarks.ApplyToPlayable : idempotent; run AFTER ShuntaMetroLookPlayable.ApplyToPlayable.
///   ShuntaMetroLandmarks.Capture         : applies, then writes docs/shunta_captures/landmark_*.png (downscaled).
/// </summary>
public static class ShuntaMetroLandmarks
{
    const string RootName = "Shunta Landmarks";

    [MenuItem("MapleRide/Shunta Metro/Apply Landmarks To Playable")]
    public static void ApplyToPlayable()
    {
        string path = MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro);
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var set = Install();
        if (set == null) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log("[shunta-landmarks] APPLY OK. " + set.LastReport);
    }

    static ShuntaLandmarkSet Install()
    {
        var rb = Object.FindFirstObjectByType<ShuntaRouteBuilder>(FindObjectsInactive.Include);
        if (rb == null) { Fail("no ShuntaRouteBuilder (run ShuntaMetroLookPlayable.ApplyToPlayable first)"); return null; }
        if (rb.positionBias != ShuntaRouteProvider.WorldOffset)
            Debug.LogWarning("[shunta-landmarks] route positionBias " + rb.positionBias + " != ShuntaRouteProvider.WorldOffset " + ShuntaRouteProvider.WorldOffset);
        if (rb.Positions == null || rb.Positions.Length < 4) rb.Rebuild();
        var prev = GameObject.Find(RootName); while (prev != null) { Object.DestroyImmediate(prev); prev = GameObject.Find(RootName); }
        var root = new GameObject(RootName);
        var env = GameObject.Find(ShuntaMetroIntegration.EnvRoot);
        if (env != null) root.transform.SetParent(env.transform, true);
        var set = root.AddComponent<ShuntaLandmarkSet>();
        set.route = rb;
        set.BuildAll();
        if (set.LastReport.Contains("FAILED") || !set.LastReport.Contains("built")) { Fail(set.LastReport); return null; }
        return set;
    }

    [MenuItem("MapleRide/Shunta Metro/Capture Landmarks")]
    public static void Capture()
    {
        ApplyToPlayable();
        var set = Object.FindFirstObjectByType<ShuntaLandmarkSet>();
        var d = Object.FindFirstObjectByType<ShuntaLookDriver>();
        if (set == null || d == null || set.route == null) { Fail("capture: set/driver missing"); return; }
        var rb = set.route;
        Directory.CreateDirectory("docs/shunta_captures");
        Vector3 Rd(float km) => rb.PositionAtKm(km) + Vector3.up * 1.8f;
        Vector3 Tn(float km) { var t = rb.TangentAtKm(km); t.y = 0f; return t.normalized; }
        Vector3 Rt(float km) { var t = Tn(km); return new Vector3(t.z, 0f, -t.x); }

        float km0 = 9.3f; d.previewKm = km0; d.ApplyKm(km0);
        Shot("docs/shunta_captures/landmark_tower.png", Rd(km0) - Tn(km0) * 4f, set.TowerBase + Vector3.up * 120f, 62f, 960, 540);

        km0 = 11.2f; d.previewKm = km0; d.ApplyKm(km0);
        Shot("docs/shunta_captures/landmark_fuji.png", Rd(km0), set.FujiBase + Vector3.up * 700f, 55f, 960, 540);

        km0 = 21.15f; d.previewKm = km0; d.ApplyKm(km0);
        Shot("docs/shunta_captures/landmark_bridge.png", Rd(km0) - Tn(km0) * 3f, Rd(km0) + Tn(km0) * 200f + Vector3.up * 30f, 72f, 960, 540);

        km0 = 19.5f; d.previewKm = km0; d.ApplyKm(km0);
        var ip = rb.PositionAtKm(km0);
        Shot("docs/shunta_captures/landmark_interchange.png", ip - Tn(km0) * 90f + Vector3.up * 40f - Rt(km0) * 25f, ip + Tn(km0) * 50f, 66f, 960, 540);

        // contact sheet of the remaining pieces (wheel, tunnel, railway, finish)
        var a = ShotTex(set.WheelCentre - Tn(26f) * 20f + Rt(26f) * -150f + Vector3.up * -20f, set.WheelCentre, 55f, 480, 270, d, 26f);
        var b = ShotTex(Rd(7.4f), Rd(7.4f) + Tn(7.4f) * 60f + Vector3.up * 1f, 70f, 480, 270, d, 7.4f);
        var c = ShotTex(Rd(17.0f), Rd(17.0f) + Tn(17.0f) * 60f + Vector3.up * 2.5f, 70f, 480, 270, d, 17f);
        float kf = rb.Km[rb.Km.Length - 1] - 0.09f;
        var e = ShotTex(Rd(kf), rb.Positions[rb.Positions.Length - 1] + Vector3.up * 13f, 70f, 480, 270, d, kf);
        var sheet = new Texture2D(960, 540, TextureFormat.RGB24, false);
        sheet.SetPixels(0, 270, 480, 270, a.GetPixels()); sheet.SetPixels(480, 270, 480, 270, b.GetPixels());
        sheet.SetPixels(0, 0, 480, 270, c.GetPixels()); sheet.SetPixels(480, 0, 480, 270, e.GetPixels());
        File.WriteAllBytes("docs/shunta_captures/landmark_sheet.png", sheet.EncodeToPNG());
        foreach (var t in new[] { a, b, c, e, sheet }) Object.DestroyImmediate(t);
        Debug.Log("[shunta-landmarks] capture done");
    }

    static void Shot(string path, Vector3 from, Vector3 look, float fov, int w, int h)
    {
        var img = Render(from, look, fov, w, h);
        File.WriteAllBytes(path, img.EncodeToPNG()); Object.DestroyImmediate(img);
        Debug.Log("[shunta-landmarks] wrote " + path);
    }

    static Texture2D ShotTex(Vector3 from, Vector3 look, float fov, int w, int h, ShuntaLookDriver d, float km)
    {
        d.previewKm = km; d.ApplyKm(km);
        return Render(from, look, fov, w, h);
    }

    static Texture2D Render(Vector3 from, Vector3 look, float fov, int w, int h)
    {
        var go = new GameObject("~ShuntaLandmarkCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.transform.position = from; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = 12000f;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render(); cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        return img;
    }

    static void Fail(string msg)
    {
        Debug.LogError("[shunta-landmarks] FAILED: " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}

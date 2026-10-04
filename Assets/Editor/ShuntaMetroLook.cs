using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Shunta Metro phase 2 "the look" editor entry points (use with tools/unity/run_steps.ps1):
///   ShuntaMetroLook.Apply    - opens ShuntaMetroSetup.ScenePath, adds the ShuntaLookDriver (generated content is not saved), saves.
///   ShuntaMetroLook.Capture  - renders a few low-level views (docs/shunta_look_*.png, downscaled) and logs [shunta-look].
/// </summary>
public static class ShuntaMetroLook
{
    const string RootName = "Shunta Look";

    [MenuItem("MapleRide/Shunta Metro/Apply Look")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ShuntaMetroSetup.ScenePath, OpenSceneMode.Single);
        var rb = Object.FindFirstObjectByType<ShuntaRouteBuilder>();
        if (rb == null) { Fail("no ShuntaRouteBuilder in scene"); return; }
        rb.Rebuild();

        // the setup scene's flat grey moon is replaced by the driver's sun/moon
        var oldMoon = GameObject.Find("Moonlight");
        if (oldMoon != null) Object.DestroyImmediate(oldMoon);

        var existing = GameObject.Find(RootName);
        if (existing != null) Object.DestroyImmediate(existing);
        var root = new GameObject(RootName);
        var d = root.AddComponent<ShuntaLookDriver>();
        d.route = rb; d.previewKm = 0.3f;
        d.BuildAll();

        var cam = Camera.main;
        if (cam != null) { cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true; }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ShuntaMetroSetup.ScenePath);
        Debug.Log("[shunta-look] APPLY OK. " + d.LastReport);
    }

    [MenuItem("MapleRide/Shunta Metro/Capture Look")]
    public static void Capture()
    {
        Apply();
        var rb = Object.FindFirstObjectByType<ShuntaRouteBuilder>();
        var d = Object.FindFirstObjectByType<ShuntaLookDriver>();
        if (rb == null || d == null) { Fail("capture: no builder/driver"); return; }
        Directory.CreateDirectory("docs");

        // (1) rider-height view in the rain zone, (2) overview from above the whole route
        float km = 14.6f;
        d.previewKm = km; d.ApplyKm(km);
        var pos = rb.PositionAtKm(km); var tan = rb.TangentAtKm(km);
        Shot("docs/shunta_look_rain_streets.png", pos + Vector3.up * 1.7f - tan * 2f, pos + tan * 60f + Vector3.up * 1.5f, 70f, 960, 540, 4000f);

        km = 9.3f; d.previewKm = km; d.ApplyKm(km);
        pos = rb.PositionAtKm(km); tan = rb.TangentAtKm(km);
        Shot("docs/shunta_look_tower.png", pos + Vector3.up * 12f - tan * 40f, pos + new Vector3(tan.z, 0f, -tan.x).normalized * 380f + Vector3.up * 150f, 62f, 960, 540, 16000f);
    }

    static void Shot(string path, Vector3 from, Vector3 look, float fov, int w, int h, float far)
    {
        var go = new GameObject("~ShuntaLookCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.transform.position = from; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = far;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        // two renders: the first lets HDRP build sky/ambient state
        cam.Render(); cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(img); Object.DestroyImmediate(go);
        Debug.Log("[shunta-look] wrote " + path);
    }

    static void Fail(string msg)
    {
        Debug.LogError("[shunta-look] FAILED: " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}

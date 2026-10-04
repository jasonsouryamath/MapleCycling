using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only verification capture for the poseExtraHeadLiftDegrees "bobblehead" fix. Does NOT
/// stage, prune, or save anything - just points a camera at named riders already in the scene
/// and writes a PNG per rider so the fix can be confirmed by eye.
///
/// Run headless:
///   Unity.exe -projectPath <abs> -batchmode -quit -executeMethod FiHeadliftVerifyCapture.Run
/// </summary>
public static class FiHeadliftVerifyCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    static readonly string OutDir = Path.GetFullPath(Path.Combine(
        Application.dataPath, "..", "fi_bobblehead_restage"));

    // Previously-broken (now patched) + two already-fixed regression checks.
    static readonly string[] Names =
        { "AkiRider", "MidoriRider", "SayakaRider", "IwaoRider", "MikaRider", "MinoriRider" };

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (var name in Names)
            CaptureOne(name);

        Debug.Log("[headlift-verify] done - no scene changes made, nothing saved.");
    }

    static void CaptureOne(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) { Debug.LogWarning($"[headlift-verify] {name} not found in scene."); return; }
        var rig = go.GetComponentInChildren<CoralBikeRig>(true);
        if (rig != null) rig.ForceSolveOnce();
        Debug.Log($"[headlift-verify] {name}: poseExtraHeadLiftDegrees=" +
                  (rig != null ? rig.poseExtraHeadLiftDegrees.ToString() : "N/A"));

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { Debug.LogWarning($"[headlift-verify] {name} has no renderers."); return; }
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        var camGo = new GameObject("~FiHeadliftVerifyCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
        cam.allowHDR = true;
        cam.fieldOfView = 35f;
        float dist = Mathf.Max(b.size.x, b.size.y, b.size.z) * 2.4f + 0.5f;
        Vector3 dir = (new Vector3(0.55f, 0.30f, 1f)).normalized;
        cam.transform.position = b.center + dir * dist;
        cam.transform.LookAt(b.center);

        // Simple two-light rig so the render is not silhouette-only.
        var keyGo = new GameObject("~FiHeadliftVerifyKey");
        var key = keyGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.3f;
        keyGo.transform.rotation = Quaternion.Euler(45f, -120f, 0f);
        var fillGo = new GameObject("~FiHeadliftVerifyFill");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.5f;
        fillGo.transform.rotation = Quaternion.Euler(30f, 60f, 0f);

        int res = 960;
        var tex = new RenderTexture(res, res, 24);
        cam.targetTexture = tex;
        cam.Render();
        RenderTexture.active = tex;
        var img = new Texture2D(res, res, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        img.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);

        string path = Path.Combine(OutDir, $"verify_{name}.png");
        File.WriteAllBytes(path, img.EncodeToPNG());
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(keyGo);
        Object.DestroyImmediate(fillGo);
        Debug.Log($"[headlift-verify] wrote {path}");
    }
}

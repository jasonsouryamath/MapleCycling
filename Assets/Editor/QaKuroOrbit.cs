using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// READ-ONLY QA orbit capture for the player "Kuro on Sakura Pass".
/// Captures a full multi-angle turntable (front, rear, both sides, two 3/4s) plus tight
/// head/helmet close-ups (rear, front, side) under Shiosai ambience (the complaint lighting).
/// Never saves the scene. Test instrument only.
/// </summary>
public static class QaKuroOrbit
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/QA Kuro Orbit")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/qa_kuro_orbit"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[qa-orbit] player not found."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) { regions.Resolve(); regions.currentRegionId = RegionCatalog.ShiosaiCoast; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }

        // Full body bounds.
        Bounds b = default; bool has = false;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        Vector3 c = has ? b.center : player.transform.position + Vector3.up * 0.9f;
        float top = has ? b.max.y : player.transform.position.y + 1.6f;
        float height = has ? (b.max.y - b.min.y) : 1.6f;
        Debug.Log($"[qa-orbit] bounds center={c} size={b.size} top={top} height={height:F3}");

        Vector3 f = player.transform.forward, up = Vector3.up, rt = player.transform.right;
        // full-body pivot slightly above center
        Vector3 pivot = new Vector3(c.x, c.y + height * 0.05f, c.z);
        float dist = Mathf.Max(2.4f, height * 1.7f);

        // Turntable full body
        Shot(dir, "body_rear",   pivot + (-f).normalized * dist + up * 0.15f, pivot, 40f);
        Shot(dir, "body_front",  pivot + ( f).normalized * dist + up * 0.15f, pivot, 40f);
        Shot(dir, "body_left",   pivot + (-rt).normalized * dist + up * 0.15f, pivot, 40f);
        Shot(dir, "body_right",  pivot + ( rt).normalized * dist + up * 0.15f, pivot, 40f);
        Shot(dir, "body_q34_rear_right", pivot + (-f + rt).normalized * dist + up * 0.15f, pivot, 40f);
        Shot(dir, "body_q34_front_left", pivot + ( f - rt).normalized * dist + up * 0.15f, pivot, 40f);

        // Head close-ups
        Vector3 head = new Vector3(c.x, top - 0.10f, c.z);
        Shot(dir, "head_rear",  head + (-f + up * 0.12f).normalized * 0.62f, head, 38f);
        Shot(dir, "head_front", head + ( f + up * 0.12f).normalized * 0.62f, head, 38f);
        Shot(dir, "head_side",  head + ( rt + up * 0.10f).normalized * 0.62f, head, 38f);

        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[qa-orbit] done (scene discarded).");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.ShiosaiAmbience;
        var go = new GameObject("~QaOrbitCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 1000, h = 1000;
        var rtx = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rtx;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rtx;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rtx);
        Object.DestroyImmediate(go);
        Debug.Log($"[qa-orbit] wrote {name}.png");
    }
}

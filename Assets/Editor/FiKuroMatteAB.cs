using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// READ-ONLY matched-lighting A/B for the matte-floor fix. Renders the player rear + front under
/// Shiosai ("complaint") ambience with the fix DISABLED (_MatteFloor=0, _ShadowAmbient=0.38) and
/// then ENABLED, on the in-memory material only. Never saves.
public static class FiKuroMatteAB
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/qa_kuro_orbit"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[ab] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) { regions.Resolve(); regions.currentRegionId = RegionCatalog.ShiosaiCoast; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }

        // The kit material (the cloned CelLit on the skinned mesh).
        Material kit = null;
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var m in smr.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name == "MapleRide/HDRP/CelLit") { kit = m; break; }
        if (kit == null) { Debug.LogError("[ab] kit material not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        Bounds b = default; bool has = false;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        Vector3 c = b.center; float height = b.size.y;
        Vector3 f = player.transform.forward, up = Vector3.up;
        Vector3 pivot = new Vector3(c.x, c.y + height * 0.05f, c.z);
        float dist = Mathf.Max(2.4f, height * 1.7f);

        var savedFloor = kit.GetColor("_MatteFloor");
        var savedAmb = kit.GetFloat("_ShadowAmbient");

        // BEFORE
        kit.SetColor("_MatteFloor", new Color(0, 0, 0, 1));
        kit.SetFloat("_ShadowAmbient", 0.38f);
        Shot(dir, "ab_before_rear",  pivot + (-f).normalized * dist + up * 0.15f, pivot, 40f);
        Shot(dir, "ab_before_front", pivot + ( f).normalized * dist + up * 0.15f, pivot, 40f);

        // AFTER
        kit.SetColor("_MatteFloor", savedFloor);
        kit.SetFloat("_ShadowAmbient", savedAmb);
        Shot(dir, "ab_after_rear",  pivot + (-f).normalized * dist + up * 0.15f, pivot, 40f);
        Shot(dir, "ab_after_front", pivot + ( f).normalized * dist + up * 0.15f, pivot, 40f);

        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log($"[ab] done floor={savedFloor} amb={savedAmb:F2} (scene discarded)");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.ShiosaiAmbience;
        var go = new GameObject("~ABCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain; fx.vignetteStrength = a.vignette;
        fx.vignetteSoftness = a.vignetteSoftness; fx.dofStrength = 0f;
        const int w = 1000, h = 1000;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log("[ab] wrote " + name);
    }
}

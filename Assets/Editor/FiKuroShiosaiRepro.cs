using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// READ-ONLY reproduction probe for the "player Kuro renders pink/green/purple" defect.
/// Renders the ACTUAL in-game player ("Kuro on Sakura Pass") on the bike, from the SIDE and
/// FRONT, under BOTH the Shiosai region ambience (where the evidence PNG was shot) and the
/// Sakura ambience, so the reported symptom can be reproduced or refuted in the exact lighting
/// of the complaint. Never saves the scene (DiscardChanges at the end).
/// </summary>
public static class FiKuroShiosaiRepro
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/Fi Kuro Shiosai Repro")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/fi_kuro_repro"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[fi-repro] player not found."); EditorApplication.Exit(1); return; }

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            s.forceMatrixRecalculationPerRender = true;
            s.updateWhenOffscreen = true;
        }

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = null;
        if (regions != null)
        {
            regions.Resolve();
            previous = regions.currentRegionId;
        }

        // Pivot on the rider's torso.
        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 pivot = player.transform.position + up * 0.55f;

        // ---- Shiosai region (evidence conditions) ----
        SetRegion(regions, RegionCatalog.ShiosaiCoast);
        Shot(dir, "shiosai_side",  pivot + r * 2.1f + up * 0.35f, pivot, 40f, RegionDirector.ShiosaiAmbience);
        Shot(dir, "shiosai_front", pivot + (r + f * 1.2f).normalized * 2.1f + up * 0.35f, pivot, 40f, RegionDirector.ShiosaiAmbience);

        // ---- Sakura region (home lighting) ----
        SetRegion(regions, RegionCatalog.SakuraPass);
        Shot(dir, "sakura_side",   pivot + r * 2.1f + up * 0.35f, pivot, 40f, RegionDirector.SakuraAmbience);

        if (regions != null && previous != null) SetRegion(regions, previous);

        Report(dir, "shiosai_side");
        Report(dir, "sakura_side");

        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[fi-repro] done (scene discarded).");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void SetRegion(RegionDirector regions, string id)
    {
        if (regions == null) return;
        regions.currentRegionId = id;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
    }

    static void Report(string dir, string name)
    {
        string file = Path.Combine(dir, name + ".png");
        if (!File.Exists(file)) return;
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        tex.LoadImage(File.ReadAllBytes(file));
        var px = tex.GetPixels();
        int pink = 0, green = 0;
        float sr = 0, sg = 0, sb = 0;
        foreach (var p in px)
        {
            sr += p.r; sg += p.g; sb += p.b;
            if (p.r > 0.35f && p.b > 0.30f && p.r > p.g + 0.10f && p.b > p.g + 0.05f) pink++;
            if (p.g > 0.35f && p.g > p.r + 0.12f && p.g > p.b + 0.12f) green++;
        }
        int n = px.Length;
        Debug.Log($"[fi-repro] {name}: mean=({sr/n:F3},{sg/n:F3},{sb/n:F3}) pinkPx={100f*pink/n:F2}% greenPx={100f*green/n:F2}%");
        Object.DestroyImmediate(tex);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, RegionDirector.Ambience a)
    {
        var go = new GameObject("~FiReproCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 1100, h = 1100;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[fi-repro] wrote {name}.png");
    }
}

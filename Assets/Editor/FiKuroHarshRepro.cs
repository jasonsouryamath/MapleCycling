using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// READ-ONLY reproduction / validation harness for the "player Kuro regresses in a harsher-lit
/// region" defect. The in-game player ("Kuro on Sakura Pass") is region-swapped by RegionDirector
/// inside SakuraPass.unity, so the exact backdrop AND grade of the user's complaint render is
/// reproduced by switching to that region's environment + ambience and shooting the player from
/// directly BEHIND (the gameplay chase framing). Never saves the scene.
///
/// Renders one rear body shot, one tight helmet/hair shot and one leg shot per region so the four
/// reported defects (blown skin legs, flat-black jersey, merged hair, shoulder spec) are each
/// judged by eye in the real lighting.
/// </summary>
public static class FiKuroHarshRepro
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    public static void Repro() { RunAll("fi_harsh_repro"); }
    public static void Verify() { RunAll("fi_harsh_after"); }

    static void RunAll(string subdir)
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/" + subdir));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[harsh] player not found."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            s.forceMatrixRecalculationPerRender = true;
            s.updateWhenOffscreen = true;
        }

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = null;
        if (regions != null) { regions.Resolve(); previous = regions.currentRegionId; }

        string[] ids =
        {
            RegionCatalog.AzoraHighlands,
            RegionCatalog.FujiRidge,
            RegionCatalog.ShiosaiCoast,
            RegionCatalog.SakuraPass,
        };

        foreach (var id in ids)
        {
            if (regions != null)
            {
                regions.currentRegionId = id;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
            var a = RegionDirector.AmbienceFor(id);

            Transform head = null;
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
                if (t.name == "Head") { head = t; break; }

            Vector3 f = player.transform.forward, up = Vector3.up;
            Vector3 hips = player.transform.position;

            Shot(dir, id + "_rear",   hips - f * 3.4f + up * 1.55f, hips + up * 1.05f, 52f, a);
            if (head != null)
                Shot(dir, id + "_helmet", head.position - f * 1.0f + up * 0.16f, head.position, 26f, a);
            // Legs: low behind shot centred on the thigh/knee line.
            Shot(dir, id + "_legs",   hips - f * 2.4f + up * 0.55f, hips + up * 0.30f, 40f, a);

            Report(dir, id + "_rear");
            Report(dir, id + "_legs");
        }

        if (regions != null && previous != null)
        {
            regions.currentRegionId = previous;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }

        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[harsh] done (scene discarded). dir=" + dir);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Report(string dir, string name)
    {
        string file = Path.Combine(dir, name + ".png");
        if (!File.Exists(file)) return;
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        tex.LoadImage(File.ReadAllBytes(file));
        var px = tex.GetPixels();
        int whiteClip = 0, nearBlack = 0;
        float sr = 0, sg = 0, sb = 0;
        foreach (var p in px)
        {
            sr += p.r; sg += p.g; sb += p.b;
            if (p.r > 0.92f && p.g > 0.92f && p.b > 0.92f) whiteClip++;
            if (p.r < 0.06f && p.g < 0.06f && p.b < 0.06f) nearBlack++;
        }
        int n = px.Length;
        Debug.Log($"[harsh] {name}: mean=({sr/n:F3},{sg/n:F3},{sb/n:F3}) " +
                  $"whiteClip={100f*whiteClip/n:F2}% nearBlack={100f*nearBlack/n:F2}%");
        Object.DestroyImmediate(tex);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                     RegionDirector.Ambience a)
    {
        var go = new GameObject("~HarshCam");
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

        const int w = 1000, h = 1100;
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
        Debug.Log($"[harsh] wrote {name}.png");
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Close-up of the solved hand on the bar hoods, to judge whether the glove reads as fingers
/// or as a mitten blob. Frames off the solved 'LeftHand' bone for the same reason the head
/// shots frame off 'Head': the grafted body sits at a different offset per build.
/// </summary>
public static class AnimeKuroGripShot
{
    private const string PocName = "Shiosai POC Realistic Rider";

    [MenuItem("MapleRide/Shiosai/Capture Anime Kuro Grip Shot", priority = 46)]
    public static void Run()
    {
        string path = ShiosaiCoastEnvironment.ScenePath;
        if (EditorSceneManager.GetActiveScene().path != path)
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/shiosai_real_rider"));
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = null;
        if (regions != null)
        {
            regions.Resolve();
            previous = regions.currentRegionId;
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }

        try
        {
            Transform poc = null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == PocName) { poc = t; break; }
            if (poc == null) { Debug.LogError("[grip-shot] POC not staged."); return; }

            var rig = poc.GetComponent<KuroBikeRig>();
            if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }

            foreach (var s in poc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                s.forceMatrixRecalculationPerRender = true;
                s.updateWhenOffscreen = true;
            }

            Transform hand = null;
            foreach (var t in poc.GetComponentsInChildren<Transform>(true))
                if (t.name == "LeftHand") { hand = t; break; }
            if (hand == null) { Debug.LogError("[grip-shot] no 'LeftHand' bone."); return; }

            Vector3 aim = hand.position;
            Debug.Log($"[grip-shot] LeftHand at {aim}");

            const float Fov = 26f;
            const float D = 1.55f;
            Vector3 f = poc.forward, r = poc.right, up = Vector3.up;

            Shot(dir, "anime_kuro_grip_out", aim + (r * 1.00f + f * 0.35f + up * 0.35f).normalized * D, aim, Fov);
            Shot(dir, "anime_kuro_grip_front", aim + (f * 1.00f + up * 0.30f).normalized * D, aim, Fov);
            Shot(dir, "anime_kuro_grip_top", aim + (up * 1.00f + f * 0.45f + r * 0.35f).normalized * D, aim, Fov);
            Debug.Log("[grip-shot] wrote 3 grip closeups.");
        }
        finally
        {
            if (regions != null && previous != null)
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~GripShotCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.ShiosaiAmbience;
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
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[grip-shot] wrote {name}.png");
    }
}

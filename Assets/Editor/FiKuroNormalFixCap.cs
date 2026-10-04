using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FIX-IMPLEMENTER verification for the Material_0 normal-map neutralization.
/// Renders the PLAYER "Kuro on Sakura Pass" under ACTUAL SakuraPass gameplay lighting
/// (SakuraPostFX + RegionDirector.SakuraAmbience) from REAR (matching
/// reference/improve/PROBLEM_kuro_lower_body_mangled_rear.png), FRONT, LEFT and RIGHT.
/// Read-only: solves the rig for a natural pedal pose but NEVER saves the scene.
/// </summary>
public static class FiKuroNormalFixCap
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    const string Player = "Kuro on Sakura Pass";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/fi_legrepro"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find(Player);
        if (player == null) { Debug.LogError("[nfix] player not found"); EditorApplication.Exit(0); return; }

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        if (rig != null) rig.SendMessage("AdvanceCrank", 35f, SendMessageOptions.DontRequireReceiver);

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            s.forceMatrixRecalculationPerRender = true;
            s.updateWhenOffscreen = true;
        }

        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 o = player.transform.position;

        // REAR matches the PROBLEM framing used by FiKuroLegRepro (low, looking up at legs).
        Shot(dir, "nfix_rear",  o - f * 1.45f + up * 0.28f, o + up * 0.72f, 46f);
        Shot(dir, "nfix_front", o + f * 1.55f + up * 0.36f, o + up * 0.66f, 44f);
        Shot(dir, "nfix_left",  o - r * 1.70f + up * 0.35f + f * 0.10f, o + up * 0.62f, 44f);
        Shot(dir, "nfix_right", o + r * 1.70f + up * 0.35f + f * 0.10f, o + up * 0.62f, 44f);
        // head-focused rear to judge the helmet/hair specifically
        Shot(dir, "nfix_head",  o - f * 0.95f + up * 0.95f, o + up * 1.02f, 40f);

        Debug.Log("[nfix] done (scene NOT saved)");
        EditorApplication.Exit(0);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~NfixCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var a = RegionDirector.SakuraAmbience;
        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 900, h = 1100;
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
        Debug.Log($"[nfix] wrote {name}.png");
    }
}

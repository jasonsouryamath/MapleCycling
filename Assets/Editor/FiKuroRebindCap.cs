using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FIX-IMPLEMENTER verification capture for the PLAYER upper-body re-skin. Identical staging and
/// framing to FiKuroUpperRepro (reimport on-disk GLB -> approved CelLit swap -> KuroBikeRig
/// Setup + ForceSolveOnce so the arms are IK'd onto the hoods and the spine leaned) but writes
/// the five angles the reviewer will judge - front / side / rear / front34 / rear34 - into
/// reference/good_graphics/fi_rebind_ingame/ with those exact names. Read-only: discards scene.
/// </summary>
public static class FiKuroRebindCap
{
    const string Scene  = "Assets/Scenes/SakuraPass.unity";
    const string Player = "Kuro on Sakura Pass";
    const string Glb    = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";

    public static void Run()
    {
        Debug.Log("[fi-rebind] forcing reimport of " + Glb);
        AssetDatabase.ImportAsset(Glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        Debug.Log("[fi-rebind] applying approved Kuro visual swap (re-instantiate + CelLit)");
        KuroPlayerModelSwap.Apply();

        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/fi_rebind_ingame"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find(Player);
        if (player == null) { Debug.LogError("[fi-rebind] player not found"); EditorApplication.Exit(0); return; }

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null)
        {
            rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
            rig.ForceSolveOnce();
            Debug.Log("[fi-rebind] rig.ForceSolveOnce() done.");
        }
        else Debug.LogWarning("[fi-rebind] no KuroBikeRig on player.");

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

        Transform la = null, ra = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "LeftArm")  la = t;
            if (t.name == "RightArm") ra = t;
        }
        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 shoulder = (la != null && ra != null) ? (la.position + ra.position) * 0.5f
                          : player.transform.position + up * 1.1f;
        Vector3 aim = shoulder - up * 0.03f;

        float d = 1.35f, fov = 34f;
        Shot(dir, "front",   aim + f * d + up * 0.02f, aim, fov);
        Shot(dir, "side",    aim + r * d + up * 0.02f, aim, fov);
        Shot(dir, "rear",    aim - f * d + up * 0.04f, aim, fov);
        Shot(dir, "front34", aim + (f * 0.75f + r * 0.75f).normalized * d + up * 0.04f, aim, fov);
        Shot(dir, "rear34",  aim + (-f * 0.75f + r * 0.75f).normalized * d + up * 0.04f, aim, fov);

        Debug.Log("[fi-rebind] wrote captures to " + dir);
        try { MapleRideSceneBootstrap.DiscardChanges(); } catch { }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.SakuraAmbience;
        var go = new GameObject("~RebindCam");
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

        const int w = 1000, h = 1000;
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
        Debug.Log($"[fi-rebind] wrote {name}.png");
    }
}

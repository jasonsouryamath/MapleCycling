using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FIX-IMPLEMENTER in-engine reproduction/verification of the PLAYER upper-body deformation
/// ("upper body is a mess" while legs are fine). Reimports the on-disk player GLB, re-instantiates
/// it into SakuraPass via the approved swap, runs the rig's Setup + ForceSolveOnce so the arms are
/// IK'd onto the brake hoods and the spine is leaned (the exact pose that melts the shoulders),
/// then captures TIGHT upper-body shots framed on the shoulder line from SIDE, REAR, FRONT, and
/// two 3/4 angles - matching the user's four reference images. Read-only: never saves the scene.
///
/// Two entry points so a broken/fixed GLB can be A/B'd without editing the file:
///   RunBefore -> writes *_before_*.png     RunAfter -> writes *_after_*.png
/// </summary>
public static class FiKuroUpperRepro
{
    const string Scene  = "Assets/Scenes/SakuraPass.unity";
    const string Player = "Kuro on Sakura Pass";
    const string Glb    = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";

    public static void RunBefore() { Run("before"); }
    public static void RunAfter()  { Run("after"); }

    static void Run(string tag)
    {
        Debug.Log("[fi-upper] forcing reimport of " + Glb);
        AssetDatabase.ImportAsset(Glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        Debug.Log("[fi-upper] applying approved Kuro visual swap (re-instantiate + CelLit)");
        KuroPlayerModelSwap.Apply();

        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/fi_upper_ingame"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find(Player);
        if (player == null) { Debug.LogError("[fi-upper] player not found"); EditorApplication.Exit(0); return; }

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null)
        {
            rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
            rig.ForceSolveOnce();
            Debug.Log("[fi-upper] rig.ForceSolveOnce() done.");
        }
        else Debug.LogWarning("[fi-upper] no KuroBikeRig on player.");

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

        // Anchor framing on the SHOULDER LINE (mid of the two upper-arm roots) so the deltoid /
        // armpit / upper-arm transition - the region that melts - fills the frame.
        Transform la = null, ra = null, head = null, hips = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "LeftArm")  la = t;
            if (t.name == "RightArm") ra = t;
            if (t.name == "Head")     head = t;
            if (t.name == "Hips")     hips = t;
        }
        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 shoulder = (la != null && ra != null) ? (la.position + ra.position) * 0.5f
                          : player.transform.position + up * 1.1f;
        // Aim slightly below the shoulder line so both shoulders and the chest/upper-back read.
        Vector3 aim = shoulder - up * 0.03f;

        float d = 1.35f, fov = 34f;
        Shot(dir, $"{tag}_side",    aim + r * d + up * 0.02f, aim, fov);
        Shot(dir, $"{tag}_side_l",  aim - r * d + up * 0.02f, aim, fov);
        Shot(dir, $"{tag}_front",   aim + f * d + up * 0.02f, aim, fov);
        Shot(dir, $"{tag}_front34", aim + (f * 0.75f + r * 0.75f).normalized * d + up * 0.04f, aim, fov);
        Shot(dir, $"{tag}_rear",    aim - f * d + up * 0.04f, aim, fov);
        Shot(dir, $"{tag}_rear34",  aim + (-f * 0.75f + r * 0.75f).normalized * d + up * 0.04f, aim, fov);

        Debug.Log("[fi-upper] wrote upper-body captures to " + dir);
        // Read-only pass: discard any scene mutations from the swap so the editor is not left dirty.
        try { MapleRideSceneBootstrap.DiscardChanges(); } catch { }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.SakuraAmbience;
        var go = new GameObject("~UpperReproCam");
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
        Debug.Log($"[fi-upper] wrote {name}.png");
    }
}

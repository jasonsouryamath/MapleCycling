using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// READ-ONLY diagnosis for "Kuro has no shoulders". Renders the rear shoulder region of the
/// in-game player in the RIDING pose (rig solved, arms reaching the hoods) and again with the
/// upper-arm bones returned to their BIND rotation, in the same neutral grade. If the deltoid is
/// full at bind and collapses/pinches when the arm reaches forward, the cause is upper-arm skin
/// weighting under the forward reach (no clavicle bone exists to carry the shoulder). Never saves.
/// </summary>
public static class FiKuroShoulderProbe
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    public static void Run()
    {
        AssetDatabase.ImportAsset("Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb",
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/fi_shoulder"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[sh] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

        Transform armL = null, armR = null, spine = null, head = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "LeftArm") armL = t;
            else if (t.name == "RightArm") armR = t;
            else if (t.name == "Head") head = t;
            else if (t.name == "Spine2" || (spine == null && t.name == "Spine")) spine = t;
        }
        Debug.Log($"[sh] armL={(armL!=null)} armR={(armR!=null)} head={(head!=null)}");

        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) rig.ForceSolveOnce();

        Vector3 f = player.transform.forward, up = Vector3.up;
        Vector3 shoulderPt = (head != null ? head.position : player.transform.position + up * 1.35f) - up * 0.18f;

        // 1) riding pose (as it renders in-game)
        Shot(dir, "shoulder_posed", shoulderPt - f * 1.4f + up * 0.10f, shoulderPt, 30f);

        // 2) upper arms returned to bind rotation (deltoid at rest)
        Quaternion bindL = armL != null ? armL.localRotation : Quaternion.identity;
        Quaternion bindR = armR != null ? armR.localRotation : Quaternion.identity;
        if (armL != null) armL.localRotation = Quaternion.identity;
        if (armR != null) armR.localRotation = Quaternion.identity;
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;
        Shot(dir, "shoulder_bind", shoulderPt - f * 1.4f + up * 0.10f, shoulderPt, 30f);
        if (armL != null) armL.localRotation = bindL;
        if (armR != null) armR.localRotation = bindR;

        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[sh] done -> " + dir);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.SakuraAmbience;
        var go = new GameObject("~ShCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain; fx.vignetteStrength = a.vignette;
        fx.vignetteSoftness = a.vignetteSoftness; fx.dofStrength = 0f;
        const int w = 900, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log("[sh] wrote " + name + ".png");
    }
}

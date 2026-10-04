using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Definitive SOLVED-POSE verification of the SakuraPass player ("Kuro on Sakura Pass") after
/// repointing the loader to the anime lineage (P7 bytes in KuroNPC_KuroAnime_Rigged.glb).
///
/// Unlike AnimeKuroChaseShot.RunPlayer, this explicitly runs the rig's Setup + ForceSolveOnce on
/// the PLAYER before rendering, and forces per-render matrix recalculation, so the captured frame
/// shows the ACTUAL seated pose (hands on the brake hoods, feet on the pedals) rather than the raw
/// bind pose. It renders clean orbit-style angles (front, front-3q, side, rear-3q) plus a tight
/// head close-up, all under Sakura ambience, so we can judge:
///   - whether the head/hair holds together or shreds into shards (the open P7 question), and
///   - whether the on-bike contact is correct (no floating / clipping / exploding), and
///   - the helmet colour in real lighting.
/// </summary>
public static class FiKuroPlayerPoseCheck
{
    /// Single-launch: reimport the (possibly just-overwritten) player GLB, re-instantiate it into
    /// SakuraPass via the approved swap (re-applies matte CelLit + asserts no chrome), then capture
    /// the solved seated pose. Used to A/B a candidate GLB (e.g. Rebound) without releasing the lock.
    public static void ReimportSwapAndVerify()
    {
        const string glb = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
        Debug.Log("[posecheck] forcing reimport of " + glb);
        AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        Debug.Log("[posecheck] applying approved Kuro visual swap (re-instantiate + CelLit)");
        KuroPlayerModelSwap.Apply();
        Run();
    }

    [MenuItem("MapleRide/Kuro/Verify Player Solved Pose", priority = 60)]
    public static void Run()
    {
        const string scene = "Assets/Scenes/SakuraPass.unity";
        if (EditorSceneManager.GetActiveScene().path != scene)
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/fi_player_posecheck"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[posecheck] player 'Kuro on Sakura Pass' not found."); return; }

        var rig = player.GetComponent<KuroBikeRig>();
        // Find head/hips bones up front so we can log their transforms across the solve.
        Transform head0 = null, hips0 = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Head") head0 = t;
            if (t.name == "Hips") hips0 = t;
        }
        if (head0 != null) Debug.Log($"[posecheck] PRE-SOLVE Head pos={head0.position} rot={head0.eulerAngles} lscale={head0.lossyScale}");
        if (hips0 != null) Debug.Log($"[posecheck] PRE-SOLVE Hips pos={hips0.position} rot={hips0.eulerAngles}");

        // Render the raw (pre-solve) pose first, so we can tell whether the solve causes the shred.
        foreach (var s0 in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s0.forceMatrixRecalculationPerRender = true; s0.updateWhenOffscreen = true; }
        {
            Vector3 fr = player.transform.forward, rr = player.transform.right, upr = Vector3.up;
            Vector3 midr = (hips0 != null ? hips0.position : player.transform.position + upr * 0.9f) + upr * 0.15f;
            Shot(dir, "player_rest_side", midr + rr * 2.6f + upr * 0.20f, midr, 38f);
            if (head0 != null) Shot(dir, "player_rest_head", head0.position + rr * 0.9f, head0.position, 22f);
        }

        if (rig != null)
        {
            rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
            rig.ForceSolveOnce();
            Debug.Log("[posecheck] rig.ForceSolveOnce() done.");
        }
        else Debug.LogWarning("[posecheck] no KuroBikeRig on player - rendering raw pose.");

        if (head0 != null) Debug.Log($"[posecheck] POST-SOLVE Head pos={head0.position} rot={head0.eulerAngles} lscale={head0.lossyScale}");
        if (hips0 != null) Debug.Log($"[posecheck] POST-SOLVE Hips pos={hips0.position} rot={hips0.eulerAngles}");

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            s.forceMatrixRecalculationPerRender = true;
            s.updateWhenOffscreen = true;
        }

        // Anchor framing on the head/hips bones so the crop is stable regardless of pivot.
        Transform head = null, hips = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Head") head = t;
            if (t.name == "Hips") hips = t;
        }

        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 mid = (hips != null ? hips.position : player.transform.position + up * 0.9f);
        Vector3 aim = mid + up * 0.15f;

        // Full-body orbit: distance chosen so the whole rider+bike fills ~1000px at fov 38.
        float d = 2.6f;
        Shot(dir, "player_front",     aim + f * d + up * 0.25f, aim, 38f);
        Shot(dir, "player_front34",   aim + (f * 0.8f + r * 0.8f).normalized * d + up * 0.25f, aim, 38f);
        Shot(dir, "player_side",      aim + r * d + up * 0.20f, aim, 38f);
        Shot(dir, "player_rear34",    aim + (-f * 0.8f + r * 0.8f).normalized * d + up * 0.25f, aim, 38f);
        Shot(dir, "player_rear",      aim - f * d + up * 0.25f, aim, 38f);

        // Head close-up (the hair-shred question). ~0.9m out, fov 22.
        if (head != null)
        {
            Vector3 h = head.position;
            Shot(dir, "player_head_front", h + f * 0.9f + up * 0.02f, h, 22f);
            Shot(dir, "player_head_side",  h + r * 0.9f + up * 0.02f, h, 22f);
        }

        Debug.Log("[posecheck] wrote solved-pose captures to " + dir);
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.SakuraAmbience;
        var go = new GameObject("~PoseCheckCam");
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
        Debug.Log($"[posecheck] wrote {name}.png");
    }
}

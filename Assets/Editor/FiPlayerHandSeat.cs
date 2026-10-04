using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Seats the player's SOLVED hands down onto the brake hoods so the closed grip actually wraps
/// the hood geometry, instead of curling in the air ~0.11 m above it. Adjusts ONLY the SYMMETRIC
/// handTargetLocalOffset (X kept 0 -> same world vector on both sides -> arms stay uncrossed and
/// parallel). gripSpreadMetres and handSeatOffsetL/R are left exactly as serialised.
///
///   FiPlayerHandSeat.Capture  - MR_HTO="x,y,z" sets handTargetLocalOffset in-memory, poses,
///                               logs steer axes + hand/hood deltas, renders. Does NOT save.
///   FiPlayerHandSeat.Apply    - same, then SAVES the scene.
/// The shared bike GLB is never touched. If MR_HTO is unset the current value is used (read-only).
/// </summary>
public static class FiPlayerHandSeat
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    const string Player = "Kuro on Sakura Pass";

    static GameObject OpenAndFindPlayer()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        var p = GameObject.Find(Player);
        if (p == null) Debug.LogError("[seat] player not found: " + Player);
        return p;
    }

    static Vector3? ParseV3(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var t = s.Split(',');
        if (t.Length != 3) return null;
        return new Vector3(
            float.Parse(t[0], CultureInfo.InvariantCulture),
            float.Parse(t[1], CultureInfo.InvariantCulture),
            float.Parse(t[2], CultureInfo.InvariantCulture));
    }

    static Transform Find(GameObject p, string n) =>
        p.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

    static void Pose(GameObject player)
    {
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }
    }

    static void LogState(GameObject p)
    {
        var steer = Find(p, "SteerPivot");
        if (steer != null)
            Debug.Log($"[seat] steer axes  right={steer.right.ToString("F3")} up={steer.up.ToString("F3")} fwd={steer.forward.ToString("F3")}");
        var hips = Find(p, "Hips"); var sp = Find(p, "Spine02") ?? Find(p, "Spine01") ?? Find(p, "Spine"); var nk = Find(p, "Neck"); var hd = Find(p, "Head");
        Debug.Log($"[seat] torso: hips={(hips?hips.position.ToString("F3"):"n/a")} spine={(sp?sp.position.ToString("F3"):"n/a")} " +
                  $"neck={(nk?nk.position.ToString("F3"):"n/a")} head={(hd?hd.position.ToString("F3"):"n/a")}");
        foreach (var side in new[] { "Left", "Right" })
        {
            var arm = Find(p, side + "Arm"); var fore = Find(p, side + "ForeArm"); var hand = Find(p, side + "Hand");
            var hood = Find(p, side == "Left" ? "Hood_L" : "Hood_R");
            if (arm == null || fore == null || hand == null || hood == null) continue;
            Vector3 d = hand.position - hood.position;
            Vector3 a = fore.position - arm.position, b = hand.position - fore.position;
            float elbow = 180f - Vector3.Angle(a, b);
            var sh = Find(p, side + "Shoulder");
            Debug.Log($"[seat] {side}: shoulder={(sh!=null?sh.position.ToString("F3"):"n/a")} armRoot={arm.position.ToString("F3")} " +
                      $"elbow(pos)={fore.position.ToString("F3")} hand={hand.position.ToString("F3")} hood={hood.position.ToString("F3")} " +
                      $"delta(world)={d.ToString("F3")} |d|={d.magnitude*1000f:F0}mm elbow={elbow:F1}deg");
        }
    }

    public static void Capture() { Run(false); }
    public static void Apply()   { Run(true); }

    static void Run(bool save)
    {
        var p = OpenAndFindPlayer();
        if (p == null) return;
        var rig = p.GetComponent<KuroBikeRig>();
        if (rig == null) { Debug.LogError("[seat] no KuroBikeRig"); return; }

        Debug.Log($"[seat] LIVE-before pose: spineLean={rig.spineLeanDegrees:F1} extraLean={rig.poseExtraSpineLeanDegrees:F1} " +
                  $"shoulderDrop={rig.shoulderDropDegrees:F1} neckLift={rig.neckLiftDegrees:F1} headLift={rig.headLiftDegrees:F1} " +
                  $"hipTilt={rig.hipTiltDegrees:F1} spread={rig.gripSpreadMetres:F3}");

        var hto = ParseV3(System.Environment.GetEnvironmentVariable("MR_HTO"));
        if (hto.HasValue)
        {
            Debug.Log($"[seat] handTargetLocalOffset {rig.handTargetLocalOffset.ToString("F3")} -> {hto.Value.ToString("F3")}");
            rig.handTargetLocalOffset = hto.Value;
        }
        // Optional absolute pose overrides (only applied when the env var is set).
        void Set(string env, System.Action<float> apply, string label)
        {
            var v = System.Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrEmpty(v) && float.TryParse(v, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            { apply(f); Debug.Log($"[seat] set {label} -> {f:F2}"); }
        }
        Set("MR_LEAN",   f => rig.spineLeanDegrees = f, "spineLeanDegrees");
        Set("MR_HIP",    f => rig.hipTiltDegrees = f, "hipTiltDegrees");
        Set("MR_SHDROP", f => rig.shoulderDropDegrees = f, "shoulderDropDegrees");
        Set("MR_NECK",   f => rig.neckLiftDegrees = f, "neckLiftDegrees");
        Set("MR_HEAD",   f => rig.headLiftDegrees = f, "headLiftDegrees");
        Set("MR_SPREAD", f => rig.gripSpreadMetres = f, "gripSpreadMetres");
        Set("MR_POLE",   f => rig.armElbowPoleSign = f, "armElbowPoleSign");

        Debug.Log($"[seat] using HTO={rig.handTargetLocalOffset.ToString("F3")} spread={rig.gripSpreadMetres:F3} " +
                  $"spineLean={rig.spineLeanDegrees:F1} shoulderDrop={rig.shoulderDropDegrees:F1} " +
                  $"neckLift={rig.neckLiftDegrees:F1} headLift={rig.headLiftDegrees:F1} poleSign={rig.armElbowPoleSign:F1} " +
                  $"seatL={rig.handSeatOffsetL.ToString("F3")} seatR={rig.handSeatOffsetR.ToString("F3")}");

        Pose(p);
        LogState(p);

        string outSub = System.Environment.GetEnvironmentVariable("MR_OUT") ?? "fi_seat_tmp";
        RenderAll(p, outSub);

        if (save)
        {
            EditorSceneManager.MarkSceneDirty(p.scene);
            EditorSceneManager.SaveScene(p.scene);
            Debug.Log("[seat] APPLIED + saved scene with HTO=" + rig.handTargetLocalOffset.ToString("F3"));
        }
        else Debug.Log("[seat] Capture done (scene NOT saved).");
    }

    static void RenderAll(GameObject player, string outSub)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/" + outSub));
        Directory.CreateDirectory(dir);

        var hips = Find(player, "Hips");
        var hoodL = Find(player, "Hood_L"); var hoodR = Find(player, "Hood_R");
        var handR = Find(player, "RightHand");
        Vector3 f = player.transform.forward, up = Vector3.up, r = player.transform.right;
        Vector3 mid = (hips != null ? hips.position : player.transform.position + up * 0.9f);
        Vector3 aim = mid + up * 0.15f;
        float d = 2.6f;
        Shot(dir, "player_front", aim + f * d + up * 0.25f, aim, 38f);
        Shot(dir, "player_side",  aim + r * d + up * 0.20f, aim, 38f);
        // BACK VIEW (directly behind, like the user's reference) - must show upper back + both
        // arms bridging shoulder->hood. Aim a touch higher (upper back) so shoulders/arms frame.
        var neck = Find(player, "Neck"); var spine = Find(player, "Spine1") ?? Find(player, "Spine");
        Vector3 upperBack = (neck != null ? neck.position : (spine != null ? spine.position : aim + up * 0.4f));
        Vector3 backAim = (aim + upperBack) * 0.5f + up * 0.05f;
        Shot(dir, "player_back",   backAim - f * d + up * 0.30f, backAim, 38f);
        // Tighter back-3/4 chase framed on shoulders+arms+hoods to read the reach clearly.
        var hoodMidB = (hoodL != null && hoodR != null) ? (hoodL.position + hoodR.position) * 0.5f : aim + f * 0.4f;
        Vector3 backChaseAim = (upperBack + hoodMidB) * 0.5f;
        Shot(dir, "back_reach",    backChaseAim - f * 1.5f + up * 0.55f, backChaseAim, 40f);

        if (hoodL != null && hoodR != null)
        {
            Vector3 hc = (hoodL.position + hoodR.position) * 0.5f;
            Shot(dir, "hands_closeup_front", hc + f * 0.85f + up * 0.12f, hc, 26f);
            Shot(dir, "hands_closeup_side",  hc + r * 0.85f + up * 0.10f, hc, 26f);
        }
        // Tight near-hand (right) side macro framed on the hood to prove the wrap.
        if (handR != null && hoodR != null)
        {
            Vector3 hood = hoodR.position;
            Shot(dir, "near_hand_macro", hood + r * 0.42f + f * 0.10f + up * 0.10f, hood + up * 0.02f, 24f);
            Shot(dir, "near_hand_macro34", hood + (r * 0.8f - f * 0.6f).normalized * 0.42f + up * 0.12f, hood + up * 0.02f, 24f);
        }
        Debug.Log("[seat] wrote captures to " + dir);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.SakuraAmbience;
        var go = new GameObject("~SeatCam");
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
        Debug.Log("[seat] wrote " + name + ".png");
    }
}

using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// SAFE, reversible, Unity-only hand-seating tweak for the PLAYER only. Adjusts the player
/// KuroBikeRig's serialized <c>handTargetLocalOffset</c> (added to the Hood_L/Hood_R IK targets
/// in the steerer's LOCAL space) so the baked glove fingers seat onto the relocated bar tube
/// instead of splaying/intersecting it. Touches NO .glb and never re-bakes the grip.
///
///   FiHandSeatTweak.Sweep   - renders a grid of candidate offsets (scene NOT saved).
///   FiHandSeatTweak.Commit  - MR_HOFF="x,y,z": sets the offset on the scene rig and SAVES.
/// </summary>
public static class FiHandSeatTweak
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";
    const string Player = "Kuro on Sakura Pass";

    static GameObject OpenPlayer()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        var p = GameObject.Find(Player);
        if (p == null) Debug.LogError("[handseat] player not found: " + Player);
        return p;
    }

    static Transform Find(GameObject p, string n) =>
        p.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

    static void ForceSkin(GameObject p)
    {
        foreach (var s in p.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }
    }

    static void Apply(GameObject p, Vector3 off)
    {
        var rig = p.GetComponent<KuroBikeRig>();
        rig.handTargetLocalOffset = off;
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.ForceSolveOnce();
        ForceSkin(p);
    }

    static string Metrics(GameObject p)
    {
        string one(string side)
        {
            var arm = Find(p, side + "Arm");
            var fore = Find(p, side + "ForeArm");
            var hand = Find(p, side + "Hand");
            var hood = Find(p, side == "Left" ? "Hood_L" : "Hood_R");
            if (!arm || !fore || !hand || !hood) return side + ":missing";
            float elbow = 180f - Vector3.Angle(fore.position - arm.position, hand.position - fore.position);
            return $"{side[0]} elbow={elbow:F1} h2hood={Vector3.Distance(hand.position, hood.position) * 1000f:F0}mm x={hand.position.x:F3}";
        }
        return one("Left") + "  " + one("Right");
    }

    public static void Sweep()
    {
        var p = OpenPlayer();
        if (p == null) return;
        var steer = Find(p, "SteerPivot");
        if (steer != null)
            Debug.Log($"[handseat] steer axes world: right={steer.right:F2} up={steer.up:F2} fwd={steer.forward:F2}  playerFwd={p.transform.forward:F2}");

        var cands = new (string name, Vector3 off)[]
        {
            ("base",   new Vector3(0f, 0f, 0f)),
            ("yUp3",   new Vector3(0f, 0.03f, 0f)),
            ("yDn3",   new Vector3(0f, -0.03f, 0f)),
            ("zFwd3",  new Vector3(0f, 0f, 0.03f)),
            ("zBk3",   new Vector3(0f, 0f, -0.03f)),
            ("yDn2zBk2", new Vector3(0f, -0.02f, -0.02f)),
            ("yUp2zBk2", new Vector3(0f, 0.02f, -0.02f)),
        };
        foreach (var c in cands)
        {
            Apply(p, c.off);
            Debug.Log($"[handseat] {c.name} off={c.off:F3}  {Metrics(p)}");
            RenderSet(p, "fi_handseat/" + c.name);
        }
        Apply(p, Vector3.zero); // leave rig back at committed value; scene NOT saved
        Debug.Log("[handseat] Sweep done (scene NOT saved).");
    }

    // --- Symmetric grip fix (pretzel-arms): zero the PLAYER-only asymmetric per-hand seat
    // offsets (which yank each wrist up/inboard/back until the forearms cross into an X) and
    // instead drive a clean symmetric grip like the NPCs: hands pushed outboard onto their OWN
    // hoods via gripSpreadMetres plus a symmetric Y lift on handTargetLocalOffset. -----------

    static readonly Vector3 AsymL = new Vector3(-0.0297f, 0.0626f, -0.0765f);
    static readonly Vector3 AsymR = new Vector3( 0.0297f, 0.0547f, -0.0628f);

    static void ApplySym(GameObject p, float yLift, float spread)
    {
        var rig = p.GetComponent<KuroBikeRig>();
        rig.handSeatOffsetL = Vector3.zero;   // drop the crossing asymmetric offsets
        rig.handSeatOffsetR = Vector3.zero;
        rig.handTargetLocalOffset = new Vector3(0f, yLift, 0f); // symmetric lift only
        rig.gripSpreadMetres = spread;         // symmetric outboard spread
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.ForceSolveOnce();
        ForceSkin(p);
    }

    public static void SweepSym()
    {
        var p = OpenPlayer();
        if (p == null) return;
        var rig = p.GetComponent<KuroBikeRig>();

        // 0) Reproduce the current asymmetric pretzel in this same run for a like-for-like compare.
        rig.handSeatOffsetL = AsymL; rig.handSeatOffsetR = AsymR;
        rig.handTargetLocalOffset = Vector3.zero; rig.gripSpreadMetres = 0f;
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.ForceSolveOnce(); ForceSkin(p);
        Debug.Log($"[handseat] REPRO_ASYM  {Metrics(p)}");
        RenderSet(p, "fi_symsweep/repro_asym");

        // 1) Symmetric candidates. spread sign swept both ways because the outboard axis sense
        //    is verified by render, not assumed. y lifts the wrists to seat the glove on the hood.
        var cands = new (string name, float y, float spread)[]
        {
            ("sym_s0y0",     0.00f,  0.00f),   // NPC baseline: zero everything (should already uncross)
            ("sym_p05y3",    0.03f,  0.05f),
            ("sym_p08y4",    0.04f,  0.08f),
            ("sym_p06y5",    0.05f,  0.06f),
            ("sym_n05y3",    0.03f, -0.05f),
            ("sym_n08y4",    0.04f, -0.08f),
        };
        foreach (var c in cands)
        {
            ApplySym(p, c.y, c.spread);
            Debug.Log($"[handseat] {c.name} y={c.y:F3} spread={c.spread:F3}  {Metrics(p)}");
            RenderSet(p, "fi_symsweep/" + c.name);
        }

        // Leave the in-memory rig at the NPC baseline; scene NOT saved.
        ApplySym(p, 0f, 0f);
        Debug.Log("[handseat] SweepSym done (scene NOT saved).");
    }

    // Commit the chosen symmetric grip. MR_YLIFT (metres, Y of handTargetLocalOffset) and
    // MR_SPREAD (metres, gripSpreadMetres). Zeroes the asymmetric per-hand seat offsets and SAVES.
    public static void CommitSym()
    {
        var p = OpenPlayer();
        if (p == null) return;
        string ys = System.Environment.GetEnvironmentVariable("MR_YLIFT");
        string ss = System.Environment.GetEnvironmentVariable("MR_SPREAD");
        if (string.IsNullOrEmpty(ys) || string.IsNullOrEmpty(ss))
        { Debug.LogError("[handseat] MR_YLIFT / MR_SPREAD not set"); return; }
        float y = float.Parse(ys, CultureInfo.InvariantCulture);
        float spread = float.Parse(ss, CultureInfo.InvariantCulture);
        var rig = p.GetComponent<KuroBikeRig>();
        rig.handSeatOffsetL = Vector3.zero;
        rig.handSeatOffsetR = Vector3.zero;
        rig.handTargetLocalOffset = new Vector3(0f, y, 0f);
        rig.gripSpreadMetres = spread;
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(p.scene);
        EditorSceneManager.SaveScene(p.scene);
        Debug.Log($"[handseat] COMMIT_SYM: saved scene  seatL=0 seatR=0 handTargetLocalOffset=(0,{y:F3},0) gripSpreadMetres={spread:F3}");
    }

    // Post-commit verification: read back the serialized values, re-solve, render the front view.
    public static void VerifySym()
    {
        var p = OpenPlayer();
        if (p == null) return;
        var rig = p.GetComponent<KuroBikeRig>();
        Debug.Log($"[handseat] VERIFY_SYM serialized seatL={rig.handSeatOffsetL:F4} seatR={rig.handSeatOffsetR:F4} " +
                  $"handTargetLocalOffset={rig.handTargetLocalOffset:F4} gripSpreadMetres={rig.gripSpreadMetres:F4}");
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.ForceSolveOnce();
        ForceSkin(p);
        Debug.Log($"[handseat] VERIFY_SYM metrics: {Metrics(p)}");
        RenderSet(p, "fi_symsweep/verify");
        Debug.Log("[handseat] VerifySym done (scene NOT saved).");
    }

    public static void Commit()
    {
        var p = OpenPlayer();
        if (p == null) return;
        string s = System.Environment.GetEnvironmentVariable("MR_HOFF");
        if (string.IsNullOrEmpty(s)) { Debug.LogError("[handseat] MR_HOFF not set"); return; }
        var parts = s.Split(',');
        Vector3 off = new Vector3(
            float.Parse(parts[0], CultureInfo.InvariantCulture),
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture));
        var rig = p.GetComponent<KuroBikeRig>();
        rig.handTargetLocalOffset = off;
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(p.scene);
        EditorSceneManager.SaveScene(p.scene);
        Debug.Log($"[handseat] COMMIT: saved scene with handTargetLocalOffset={off:F3}");
    }

    public static void Verify()
    {
        var p = OpenPlayer();
        if (p == null) return;
        var rig = p.GetComponent<KuroBikeRig>();
        Debug.Log($"[handseat] VERIFY committed handTargetLocalOffset={rig.handTargetLocalOffset:F3}");
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.ForceSolveOnce();
        ForceSkin(p);
        Debug.Log($"[handseat] VERIFY metrics: {Metrics(p)}");
        RenderSet(p, "fi_hands_onbar_final");
        Debug.Log("[handseat] Verify done (scene NOT saved).");
    }

    static void RenderSet(GameObject p, string outSub)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/" + outSub));
        Directory.CreateDirectory(dir);
        var hoodL = Find(p, "Hood_L"); var hoodR = Find(p, "Hood_R"); var hips = Find(p, "Hips");
        Vector3 f = p.transform.forward, up = Vector3.up, r = p.transform.right;
        if (hoodL && hoodR)
        {
            Vector3 hc = (hoodL.position + hoodR.position) * 0.5f;
            Shot(dir, "hands_closeup_front", hc + f * 0.85f + up * 0.12f, hc, 26f);
            Shot(dir, "hands_closeup_side", hc + r * 0.85f + up * 0.10f, hc, 26f);
        }
        Vector3 aim = (hips ? hips.position : p.transform.position + up * 0.9f) + up * 0.15f;
        Shot(dir, "player_front", aim + f * 2.6f + up * 0.25f, aim, 38f);
    }

    // 3/4 front-side: rider's right side toward camera, slightly high (matches user's view).
    static void Render34(GameObject p, string outSub)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/" + outSub));
        Directory.CreateDirectory(dir);
        var hoodL = Find(p, "Hood_L"); var hoodR = Find(p, "Hood_R");
        var chest = Find(p, "Spine2") ?? Find(p, "Spine1") ?? Find(p, "Spine") ?? Find(p, "Hips");
        Vector3 f = p.transform.forward, up = Vector3.up, r = p.transform.right;
        Vector3 hc = (hoodL.position + hoodR.position) * 0.5f;
        // tight hands close-up from front-right-high 3/4
        Shot(dir, "hands_34", hc + f * 0.58f + r * 0.50f + up * 0.30f, hc, 30f);
        // upper body + bar from front-right-high 3/4
        Vector3 aim = (chest ? chest.position : hc + up * 0.4f);
        Shot(dir, "upper_34", aim + f * 1.75f + r * 1.25f + up * 0.70f, aim, 34f);
    }

    public static void Cur34()
    {
        var p = OpenPlayer();
        if (p == null) return;
        var rig = p.GetComponent<KuroBikeRig>();
        Debug.Log($"[handseat] CUR34 committed offset={rig.handTargetLocalOffset:F3}");
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.ForceSolveOnce();
        ForceSkin(p);
        Debug.Log($"[handseat] CUR34 metrics: {Metrics(p)}");
        Render34(p, "fi_hands_34/current");
        Debug.Log("[handseat] Cur34 done (scene NOT saved).");
    }

    public static void Sweep34()
    {
        var p = OpenPlayer();
        if (p == null) return;
        var cands = new (string name, Vector3 off)[]
        {
            ("cur",        new Vector3(0f, -0.02f, -0.02f)),
            ("yDn35zBk2",  new Vector3(0f, -0.035f, -0.02f)),
            ("yDn35zBk3",  new Vector3(0f, -0.035f, -0.03f)),
            ("yDn45zBk3",  new Vector3(0f, -0.045f, -0.03f)),
            ("yDn3zBk1",   new Vector3(0f, -0.03f, -0.01f)),
            ("yDn4zBk2",   new Vector3(0f, -0.04f, -0.02f)),
        };
        foreach (var c in cands)
        {
            Apply(p, c.off);
            Debug.Log($"[handseat] {c.name} off={c.off:F3}  {Metrics(p)}");
            Render34(p, "fi_hands_34/" + c.name);
        }
        Apply(p, new Vector3(0f, -0.02f, -0.02f)); // restore committed value in-memory; scene NOT saved
        Debug.Log("[handseat] Sweep34 done (scene NOT saved).");
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.SakuraAmbience;
        var go = new GameObject("~SeatCam");
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
    }
}

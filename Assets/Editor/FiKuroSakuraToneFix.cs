using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// REGRESSION FIX: "player Kuro still blows out in REAL Sakura gameplay - bare skin (thighs/calves)
/// clips to pure white and the black jersey crushes to a detail-less blob - even though the earlier
/// _HighlightRolloff pass passed the offline capture harness."
///
/// ROOT CAUSE (verified from the LIVE material dump + the real play render, not from a .mat on disk):
///  * The play rider renders with ONE atlas material, PlayerBody_Image_0_CelLit (its in-scene
///    clone "..._KuroKit"). It already carried _HighlightRolloff 0.85, _ShadowAmbient 0.52 and a
///    matte floor from the prior pass.
///  * The offline harness (fi_harsh_repro etc.) builds a throwaway camera with a Built-in
///    SakuraPostFX grade; under HDRP OnRenderImage never fires, so it renders at Fixed EV100 0 with
///    Tonemapping = None. REAL play renders through the scene volume: Automatic exposure + ACES
///    tonemapping + a +0.6 EV post-exposure. The cel shader applies the highlight soft-clip
///    PRE-exposure toward a 1.35 white point, which the harness never pushed past 1.0 but the play
///    grade multiplies back over the clip point - so skin still blows to white in game.
///
/// FIX (scoped strictly to the player atlas material - no shader default changes, no other
/// material touched):
///  * The soft-clip knee + white point are now material properties (shader defaults 0.55 / 1.35 =
///    the old constants, so every other material is byte-for-byte unchanged). The player gets a
///    LOWER knee + LOWER white point so bright skin is pulled below the ACES/post-exposure clip and
///    keeps a shaded gradient.
///  * _ShadowAmbient raised so the near-black jersey keeps collar/sleeve/fold detail and the
///    shoulders read on the shadowed/backlit side under a bright open sun.
///
/// All tuning is read from environment variables so it can be dialled against the REAL play render
/// without recompiling. Both the in-scene clone (what renders now) and the shared asset (what a
/// fresh scene instantiates) are patched. Asserts no non-player renderer shares a touched material.
/// </summary>
public static class FiKuroSakuraToneFix
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string CelLitShader = "MapleRide/HDRP/CelLit";
    const string SharedBodyMat = "Assets/Kuro/Materials/PlayerBody_Image_0_CelLit.mat";
    // The player's single body atlas. Its in-scene clone is "<name>_KuroKit"; a runtime copy is
    // "<name> (Instance)". Matching on this prefix catches all three.
    const string PlayerAtlasPrefix = "PlayerBody_Image_0_CelLit";

    static float Env(string key, float fallback)
    {
        var s = Environment.GetEnvironmentVariable(key);
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    // The player's body atlas is SHARED by 8 Sakura NPC riders (they use the same Kuro visual).
    // Only the in-scene CLONE "..._KuroKit" is exclusive to the player, so the aggressive fix goes
    // on the CLONE alone; the shared asset is RESTORED to the prior accepted baseline so no NPC is
    // regressed. These baseline numbers are the committed pre-fix values of the shared asset.
    const float BaseRolloff = 0.85f, BaseKnee = 0.55f, BaseWhite = 1.35f, BaseShadowAmb = 0.52f;
    static readonly Color BaseMatte = new Color(0.145f, 0.145f, 0.155f, 1f);

    public static void Run()
    {
        // ---- player tuning (env-overridable so it can be dialled on the real render) -----------
        // Highlight soft-clip is RELAXED vs the region-only fix: with the character self-light rig
        // driving skin brightness (not the region sun), skin no longer over-drives, so a gentle
        // knee/white keeps skin warm without blowing.
        float rolloff   = Env("MR_FIX_ROLLOFF", 0.80f);
        float knee      = Env("MR_FIX_KNEE",    0.55f);
        float white     = Env("MR_FIX_WHITE",   1.10f);
        float shadowAmb = Env("MR_FIX_SHADOWAMB", 0.55f);
        float matte     = Env("MR_FIX_MATTE",   0.165f);
        // Character self-light rig (the structural, region-independent fix).
        float charLight = Env("MR_FIX_CHARLIGHT", 0.85f);
        float keyInt    = Env("MR_FIX_KEYINT",    1.25f);
        float fillInt   = Env("MR_FIX_FILLINT",   0.45f);
        float charAmb   = Env("MR_FIX_CHARAMB",   0.34f);
        float edgeRim   = Env("MR_FIX_EDGERIM",   0.18f);
        Debug.Log($"[tonefix] PLAYER tuning rolloff={rolloff:F2} knee={knee:F2} white={white:F2} " +
                  $"shadowAmb={shadowAmb:F2} matte={matte:F3} | charLight={charLight:F2} key={keyInt:F2} " +
                  $"fill={fillInt:F2} charAmb={charAmb:F2} edgeRim={edgeRim:F2}");

        void AggressiveApply(Material m)
        {
            if (m.HasProperty("_HighlightRolloff")) m.SetFloat("_HighlightRolloff", rolloff);
            if (m.HasProperty("_HighlightKnee"))    m.SetFloat("_HighlightKnee", knee);
            if (m.HasProperty("_HighlightWhite"))   m.SetFloat("_HighlightWhite", white);
            if (m.HasProperty("_ShadowAmbient"))    m.SetFloat("_ShadowAmbient", shadowAmb);
            if (m.HasProperty("_MatteFloor"))       m.SetColor("_MatteFloor", new Color(matte, matte, matte * 1.07f, 1f));
            // --- character self-light rig + edge rim (player only) ---
            if (m.HasProperty("_CharacterLight"))    m.SetFloat("_CharacterLight", charLight);
            if (m.HasProperty("_CharKeyDir"))        m.SetVector("_CharKeyDir", new Vector4(0.35f, 0.60f, 0.55f, 0f));
            if (m.HasProperty("_CharKeyColor"))      m.SetColor("_CharKeyColor", new Color(1f, 0.99f, 0.96f, 1f));
            if (m.HasProperty("_CharKeyIntensity"))  m.SetFloat("_CharKeyIntensity", keyInt);
            if (m.HasProperty("_CharFillColor"))     m.SetColor("_CharFillColor", new Color(0.62f, 0.72f, 0.95f, 1f));
            if (m.HasProperty("_CharFillIntensity")) m.SetFloat("_CharFillIntensity", fillInt);
            if (m.HasProperty("_CharAmbient"))       m.SetFloat("_CharAmbient", charAmb);
            if (m.HasProperty("_EdgeRimColor"))      m.SetColor("_EdgeRimColor", new Color(0.72f, 0.80f, 1.0f, 1f));
            if (m.HasProperty("_EdgeRimStrength"))   m.SetFloat("_EdgeRimStrength", edgeRim);
            if (m.HasProperty("_EdgeRimPower"))      m.SetFloat("_EdgeRimPower", 3.0f);
        }
        void BaselineApply(Material m)
        {
            if (m.HasProperty("_HighlightRolloff")) m.SetFloat("_HighlightRolloff", BaseRolloff);
            if (m.HasProperty("_HighlightKnee"))    m.SetFloat("_HighlightKnee", BaseKnee);
            if (m.HasProperty("_HighlightWhite"))   m.SetFloat("_HighlightWhite", BaseWhite);
            if (m.HasProperty("_ShadowAmbient"))    m.SetFloat("_ShadowAmbient", BaseShadowAmb);
            if (m.HasProperty("_MatteFloor"))       m.SetColor("_MatteFloor", BaseMatte);
            // NPCs must NOT get the character rig or edge rim - keep them at shader-default no-op.
            if (m.HasProperty("_CharacterLight"))   m.SetFloat("_CharacterLight", 0f);
            if (m.HasProperty("_EdgeRimStrength"))  m.SetFloat("_EdgeRimStrength", 0f);
        }

        bool IsPlayerClone(Material m) =>
            m != null && m.shader != null && m.shader.name == CelLitShader &&
            m.name.StartsWith(PlayerAtlasPrefix, StringComparison.Ordinal) &&
            m.name.Contains("_KuroKit");

        // ---- 1. RESTORE the shared asset to baseline (it is shared by 8 NPCs - never leak the
        //         player's aggressive values into them) ---------------------------------------
        var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedBodyMat);
        if (shared != null && shared.shader != null && shared.shader.name == CelLitShader)
        {
            BaselineApply(shared);
            EditorUtility.SetDirty(shared);
            Debug.Log($"[tonefix] RESTORED shared asset {SharedBodyMat} to NPC baseline -> " +
                      $"rolloff={shared.GetFloat("_HighlightRolloff"):F2} knee={shared.GetFloat("_HighlightKnee"):F2} " +
                      $"white={shared.GetFloat("_HighlightWhite"):F2} shadowAmb={shared.GetFloat("_ShadowAmbient"):F2}");
        }
        else Debug.LogWarning("[tonefix] shared body material not found/!CelLit: " + SharedBodyMat);

        // ---- 2. patch ONLY the player-exclusive in-scene clone --------------------------------
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var touched = new HashSet<Material>();
        var usedBy = new Dictionary<Material, List<string>>();

        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (!IsPlayerClone(m)) continue;
                if (!usedBy.TryGetValue(m, out var list)) { list = new List<string>(); usedBy[m] = list; }
                list.Add(PathOf(r.transform));
                if (touched.Add(m))
                {
                    Debug.Log($"[tonefix] BEFORE clone '{m.name}': rolloff={GF(m,"_HighlightRolloff")} " +
                              $"knee={GF(m,"_HighlightKnee")} white={GF(m,"_HighlightWhite")} shadowAmb={GF(m,"_ShadowAmbient")} " +
                              $"matte={(m.HasProperty("_MatteFloor")?m.GetColor("_MatteFloor").ToString("F3"):"n/a")}");
                    AggressiveApply(m);
                    EditorUtility.SetDirty(m);
                    Debug.Log($"[tonefix] AFTER  clone '{m.name}': rolloff={GF(m,"_HighlightRolloff")} " +
                              $"knee={GF(m,"_HighlightKnee")} white={GF(m,"_HighlightWhite")} shadowAmb={GF(m,"_ShadowAmbient")}");
                }
            }
        }
        Debug.Log($"[tonefix] player-exclusive clone materials patched: {touched.Count}");

        // ---- 3. HARD leak assertion: a patched clone may ONLY be used by the player root -------
        int leaks = 0;
        foreach (var kv in usedBy)
        {
            Debug.Log($"[tonefix] clone '{kv.Key.name}' used by: {string.Join(" | ", kv.Value)}");
            foreach (var path in kv.Value)
                if (!path.StartsWith("Kuro on Sakura Pass", StringComparison.Ordinal))
                {
                    Debug.LogError($"[tonefix] LEAK: player clone '{kv.Key.name}' used by non-player '{path}'");
                    leaks++;
                }
        }
        Debug.Log($"[tonefix] clone leaks into NPC/env: {leaks} " + (leaks == 0 ? "-> OK" : "-> MUST FIX"));
        if (touched.Count == 0)
            Debug.LogError("[tonefix] found NO player clone to patch - the live player may not use '_KuroKit'.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[tonefix] saved; dirty=" + scene.isDirty);
        if (Application.isBatchMode) EditorApplication.Exit(leaks == 0 && touched.Count > 0 ? 0 : 1);
    }

    static string GF(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p).ToString("F2") : "n/a";

    static string PathOf(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

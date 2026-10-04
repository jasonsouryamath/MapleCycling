using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FIX-IMPLEMENTER: two scoped player-only repairs for the "Kuro real-gameplay REAR view does not
/// match the character-sheet BACK VIEW" defect.
///
///  FIX A (back-jersey branding): the live player body renders the KuroAnime multiview atlas
///  (embedded "Image_0" in KuroNPC_KuroAnime_Rigged.glb). That atlas has FRONT branding but no
///  clean BACK-panel arrangement, so the back-torso UV island samples a plain dark region and the
///  jersey back reads blank. A projection-bake (Blender) authored a white maple leaf + "KURO" +
///  the 黒 kanji onto exactly the atlas texels the back-facing torso triangles use, saved as the
///  standalone texture <see cref="NewAtlasPath"/>. This repoints ONLY the player's in-scene clone
///  material (<see cref="ClonePrefix"/>..."_KuroKit") at that texture. The 8 Sakura NPC riders use
///  the SHARED asset / embedded Image_0 and are never touched.
///
///  FIX B (leg skin tone): the player clone carries the character self-light rig, which over-drives
///  the warm BARE-SKIN albedo so the thighs/calves clip to pure white under ACES + post-exposure.
///  The CelLit shader gained a skin-aware highlight tame (default _SkinTame 0 = no-op for every
///  other material); this turns it on for the player clone ONLY so the legs read tan again without
///  re-crushing the near-black jersey or touching the new white branding / white socks.
///
/// All values are env-overridable so FIX B can be dialled against the REAL play render without a
/// recompile. Hard-asserts 0 leaks: the patched clone and the new atlas may only touch the player.
/// </summary>
public static class FiKuroBackBrandingAndSkin
{
    const string ScenePath   = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName  = "Kuro on Sakura Pass";
    const string CelLitShader = "MapleRide/HDRP/CelLit";
    const string ClonePrefix = "PlayerBody_Image_0_CelLit";
    const string CloneTag    = "_KuroKit";
    const string NewAtlasPath = "Assets/Kuro/NPC/Textures/KuroAnime_Image_0_PlayerBack.png";
    const string SharedBodyMat = "Assets/Kuro/Materials/PlayerBody_Image_0_CelLit.mat";

    static float Env(string k, float f)
    {
        var s = Environment.GetEnvironmentVariable(k);
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : f;
    }
    static bool EnvBool(string k, bool f)
    {
        var s = Environment.GetEnvironmentVariable(k);
        return string.IsNullOrWhiteSpace(s) ? f : (s == "1" || s.ToLowerInvariant() == "true");
    }

    static bool IsPlayerClone(Material m) =>
        m != null && m.shader != null && m.shader.name == CelLitShader &&
        m.name.StartsWith(ClonePrefix, StringComparison.Ordinal) && m.name.Contains(CloneTag);

    public static void Run()
    {
        // FIX B tuning (env-overridable). Revert => point the clone back at the shared atlas + off.
        bool revert    = EnvBool("MR_BACK_REVERT", false);
        // The old flat _SkinTame only SCALED the lit skin down (blown white * gain = grey), so it
        // made the pale legs worse. Default it OFF now and warm the skin ALBEDO instead.
        float skinTame = Env("MR_SKIN_TAME", 0.0f);
        float skinGain = Env("MR_SKIN_GAIN", 0.58f);
        float skinKnee = Env("MR_SKIN_KNEE", 0.86f);
        float skinShrp = Env("MR_SKIN_SHARP", 3.0f);
        // Warm skin retone (the real fix): deepen/saturate warm skin albedo toward a tan tint.
        float skinWarm = Env("MR_SKIN_WARM", 1.0f);
        float warmR    = Env("MR_SKIN_WARM_R", 1.00f);
        float warmG    = Env("MR_SKIN_WARM_G", 0.80f);
        float warmB    = Env("MR_SKIN_WARM_B", 0.63f);
        var warmCol    = new Color(warmR, warmG, warmB, 1f);
        Debug.Log($"[backfix] revert={revert} skinTame={skinTame:F2} skinWarm={skinWarm:F2} warmCol=({warmR:F2},{warmG:F2},{warmB:F2})");

        var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(NewAtlasPath);
        if (newTex == null && !revert)
        {
            Debug.LogError($"[backfix] new atlas not found at {NewAtlasPath}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        // Resolve the embedded Image_0 the SHARED asset uses (so revert can restore it).
        Texture sharedTex = null;
        var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedBodyMat);
        if (shared != null && shared.HasProperty("_MainTex")) sharedTex = shared.GetTexture("_MainTex");

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Fail("player root not found: " + PlayerName); return; }

        // ---- patch ONLY the player-exclusive clone(s) -----------------------------------------
        var patched = new HashSet<Material>();
        var usedBy = new Dictionary<Material, List<string>>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (!IsPlayerClone(m)) continue;
                if (!usedBy.TryGetValue(m, out var list)) { list = new List<string>(); usedBy[m] = list; }
                list.Add(PathOf(r.transform));
                if (patched.Add(m))
                {
                    Texture target = revert ? sharedTex : newTex;
                    if (m.HasProperty("_MainTex"))          m.SetTexture("_MainTex", target);
                    if (m.HasProperty("baseColorTexture"))  m.SetTexture("baseColorTexture", target);
                    if (m.HasProperty("_SkinTame"))      m.SetFloat("_SkinTame",      revert ? 0f : skinTame);
                    if (m.HasProperty("_SkinTameGain"))  m.SetFloat("_SkinTameGain",  skinGain);
                    if (m.HasProperty("_SkinTameKnee"))  m.SetFloat("_SkinTameKnee",  skinKnee);
                    if (m.HasProperty("_SkinTameSharp")) m.SetFloat("_SkinTameSharp", skinShrp);
                    if (m.HasProperty("_SkinWarm"))      m.SetFloat("_SkinWarm",      revert ? 0f : skinWarm);
                    if (m.HasProperty("_SkinWarmColor")) m.SetColor("_SkinWarmColor", revert ? Color.white : warmCol);
                    EditorUtility.SetDirty(m);
                    Debug.Log($"[backfix] patched clone '{m.name}': _MainTex='{(target!=null?target.name:"null")}' " +
                              $"skinTame={(revert?0f:skinTame):F2} skinWarm={(revert?0f:skinWarm):F2}");
                }
            }
        }
        Debug.Log($"[backfix] player clone materials patched: {patched.Count}");

        // ---- HARD leak assertions --------------------------------------------------------------
        int leaks = 0;
        // (1) patched clone only used under the player root
        foreach (var kv in usedBy)
        {
            Debug.Log($"[backfix] clone '{kv.Key.name}' used by: {string.Join(" | ", kv.Value)}");
            foreach (var p in kv.Value)
                if (!p.StartsWith(PlayerName, StringComparison.Ordinal))
                { Debug.LogError($"[backfix] LEAK: clone used by non-player '{p}'"); leaks++; }
        }
        // (2) the new branded atlas must be referenced by NO material other than a player clone
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || IsPlayerClone(m)) continue;
                if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == newTex && newTex != null)
                { Debug.LogError($"[backfix] LEAK: branded atlas on non-player mat '{m.name}' at '{PathOf(r.transform)}'"); leaks++; }
                // (3) no non-player CelLit material may have the skin tame/warm enabled
                if (m.shader != null && m.shader.name == CelLitShader && m.HasProperty("_SkinTame") && m.GetFloat("_SkinTame") > 0f)
                { Debug.LogError($"[backfix] LEAK: _SkinTame>0 on non-player mat '{m.name}'"); leaks++; }
                if (m.shader != null && m.shader.name == CelLitShader && m.HasProperty("_SkinWarm") && m.GetFloat("_SkinWarm") > 0f)
                { Debug.LogError($"[backfix] LEAK: _SkinWarm>0 on non-player mat '{m.name}'"); leaks++; }
            }
        // (4) shared NPC asset still on the embedded atlas, tame off
        if (shared != null)
        {
            var st = shared.HasProperty("_MainTex") ? shared.GetTexture("_MainTex") : null;
            float sTame = shared.HasProperty("_SkinTame") ? shared.GetFloat("_SkinTame") : 0f;
            float sWarm = shared.HasProperty("_SkinWarm") ? shared.GetFloat("_SkinWarm") : 0f;
            if (st == newTex && newTex != null) { Debug.LogError("[backfix] LEAK: shared NPC asset now points at branded atlas"); leaks++; }
            if (sTame > 0f) { Debug.LogError("[backfix] LEAK: shared NPC asset has _SkinTame>0"); leaks++; }
            if (sWarm > 0f) { Debug.LogError("[backfix] LEAK: shared NPC asset has _SkinWarm>0"); leaks++; }
            Debug.Log($"[backfix] shared NPC asset _MainTex='{(st!=null?st.name:"null")}' _SkinTame={sTame:F2} _SkinWarm={sWarm:F2} (must be embedded + 0)");
        }
        Debug.Log($"[backfix] leaks={leaks} " + (leaks == 0 ? "-> OK" : "-> MUST FIX"));
        if (patched.Count == 0) { Debug.LogError("[backfix] found NO player clone to patch"); leaks++; }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[backfix] saved.");
        if (Application.isBatchMode) EditorApplication.Exit(leaks == 0 && patched.Count > 0 ? 0 : 1);
    }

    static void Fail(string why)
    {
        Debug.LogError("[backfix] " + why);
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }

    static string PathOf(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

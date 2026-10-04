using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FOLLOW-UP FIX: "Player Kuro regresses in the brighter regions (Azora/Fuji/etc.): skin legs+arms
/// clip to flat WHITE, shoulders catch a hard specular blowout, and the near-black kit/hair read
/// as a merged silhouette." The earlier SakuraPass fix (FiKuroMatteFloorFix / KuroKitToneFix) only
/// tuned for Sakura's softer, warm key; under a bright neutral midday sun the same values blow the
/// skin out (there was no highlight control) and let the kit crush.
///
/// ROOT CAUSE (verified by re-rendering the player under each region's ambience, not guessed):
///  * The player is ONE atlas material on MapleRide/HDRP/CelLit. Every lighting term is
///    albedo-multiplied except spec/rim. Skin is a mid-tone (~0.65) albedo; a bright region's
///    sun+sky ambient pushes its diffuse well past 1.0 with NO ceiling, so the legs/arms clip to
///    white and lose all form. The kit already had _MatteFloor from the earlier fix, so it keeps
///    form, but it needed the value carried onto the shared asset too, and the harsh skin blowout
///    had no control at all.
///
/// FIX (robust across every region, not one scene):
///  * Shader gained a zero-default _HighlightRolloff (guaranteed no-op for all other materials): a
///    soft-clip that is identity below a fixed knee and compresses only the clipping range, so
///    bright skin keeps a shaded gradient and spec reads as sheen. Set here on the PLAYER only.
///  * Re-affirms _MatteFloor / _ShadowAmbient and the tamed rim/spec on BOTH the in-scene player
///    material AND the shared Assets/Kuro/Materials/PlayerBody_Image_0_CelLit.mat, so any scene
///    that instantiates the player is correct too.
///
/// SAFETY (mirrors the earlier passes): only renderers under the player root are touched; a
/// material shared outside the player, or an imported .glb sub-asset, is cloned first; asserts no
/// non-player renderer references an adjusted material; idempotent (writes fixed values).
/// </summary>
public static class FiKuroHarshFix
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string CelLitShader = "MapleRide/HDRP/CelLit";
    const string SharedBodyMat = "Assets/Kuro/Materials/PlayerBody_Image_0_CelLit.mat";

    // Authored/sRGB. Kit form + backlit cel shadow (unchanged from the accepted Sakura fix).
    static readonly Color MatteFloor = new Color(0.145f, 0.145f, 0.155f, 1f);
    const float ShadowAmbient = 0.52f;
    // Tamed additive terms (from KuroKitToneFix) - carried onto the shared asset for parity.
    static readonly Color RimColor = new Color(0.78f, 0.86f, 1.00f, 1f);
    const float RimStrength = 0.10f;
    static readonly Color SpecTint = new Color(1.00f, 1.00f, 1.00f, 1f);
    const float SpecStrength = 0.06f;   // was 0.08 - shoulders were still catching a sheen
    const float WeatherAmount = 0f;
    // THE new lever. Tuned against the Azora/Fuji rear renders.
    const float HighlightRolloff = 0.85f;

    public static void Run()
    {
        // ---- 1. shared asset, so a fresh scene that references it is also correct ------------
        var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedBodyMat);
        if (shared != null && shared.shader != null && shared.shader.name == CelLitShader)
        {
            ApplyKit(shared);
            EditorUtility.SetDirty(shared);
            Debug.Log("[harshfix] patched shared asset " + SharedBodyMat + " -> rolloff=" +
                      shared.GetFloat("_HighlightRolloff").ToString("F2"));
        }
        else Debug.LogWarning("[harshfix] shared body material not found/!CelLit: " + SharedBodyMat);

        // ---- 2. the in-scene player material (what actually renders) ------------------------
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == PlayerName);
        if (player == null) { Debug.LogError("[harshfix] player root not found"); return; }

        var outside = new HashSet<Material>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == player) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) outside.Add(m);
        }

        var adjusted = new HashSet<Material>();
        int changed = 0, cloned = 0;

        foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool swap = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name != CelLitShader) continue;

                bool sharedOutside = outside.Contains(m);
                var path = AssetDatabase.GetAssetPath(m);
                bool fromGlb = !string.IsNullOrEmpty(path) &&
                               path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase);
                if (sharedOutside || fromGlb)
                {
                    var clone = new Material(m) { name = m.name + "_KuroKit" };
                    mats[i] = clone; m = clone; swap = true; cloned++;
                    Debug.Log("[harshfix] cloned protected material -> " + clone.name);
                }

                if (!adjusted.Add(m)) continue;

                Debug.Log(string.Format("[harshfix] BEFORE '{0}': matte={1} amb={2:F2} rolloff={3:F2} specStr={4:F2}",
                    m.name,
                    m.HasProperty("_MatteFloor") ? m.GetColor("_MatteFloor").ToString("F3") : "n/a",
                    m.HasProperty("_ShadowAmbient") ? m.GetFloat("_ShadowAmbient") : -1f,
                    m.HasProperty("_HighlightRolloff") ? m.GetFloat("_HighlightRolloff") : -1f,
                    m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength") : -1f));

                ApplyKit(m);
                EditorUtility.SetDirty(m);
                changed++;

                Debug.Log(string.Format("[harshfix] AFTER  '{0}': matte={1} amb={2:F2} rolloff={3:F2} specStr={4:F2}",
                    m.name, m.GetColor("_MatteFloor").ToString("F3"), m.GetFloat("_ShadowAmbient"),
                    m.GetFloat("_HighlightRolloff"), m.GetFloat("_SpecStrength")));
            }
            if (swap) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }

        int leaks = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == player) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && adjusted.Contains(m))
                    { Debug.LogError("[harshfix] LEAK into '" + r.name + "' via '" + m.name + "'"); leaks++; }
        }
        Debug.Log($"[harshfix] adjusted={changed} cloned={cloned} leaks={leaks} " + (leaks == 0 ? "-> OK" : "-> MUST FIX"));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[harshfix] saved; dirty=" + scene.isDirty);
    }

    static void ApplyKit(Material m)
    {
        if (m.HasProperty("_MatteFloor")) m.SetColor("_MatteFloor", MatteFloor);
        if (m.HasProperty("_ShadowAmbient")) m.SetFloat("_ShadowAmbient", ShadowAmbient);
        if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", RimColor);
        if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", RimStrength);
        if (m.HasProperty("_SpecTint")) m.SetColor("_SpecTint", SpecTint);
        if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", SpecStrength);
        if (m.HasProperty("_WeatherAmount")) m.SetFloat("_WeatherAmount", WeatherAmount);
        if (m.HasProperty("_HighlightRolloff")) m.SetFloat("_HighlightRolloff", HighlightRolloff);
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fixes the blue/cyan overexposure ("blown out to near-white") reported on the player and
/// every NPC cyclist under Sakura Pass's HDRP lighting.
///
/// ROOT CAUSE (confirmed by render + property dump, not guessed):
/// <see cref="HdrpVolumeSetup"/> fixes the HDRP camera exposure at EV100 = 0 project-wide.
/// That value is deliberately calibrated for this project's hand-written
/// <c>MapleRide/HDRP/CelLit</c> shader, which self-compensates by multiplying its own LDR
/// output by <c>GetCurrentExposureMultiplier()</c> (see MapleRideCelLit.shader) - so it looks
/// right at ANY fixed EV100. Every Kuro/NPC rider material, however, is still on either the
/// raw glTFast shader ("Shader Graphs/glTF-pbrMetallicRoughness", which carries a real HDRP
/// Lit subtarget) or a naive direct swap to "HDRP/Lit" (Kuro_Body_Matte and its four sibling
/// clones under Assets/Kuro/Materials). Both are lit by HDRP's REAL physically-based light
/// loop (real lux), which at this project's EV100 = 0 exposure is calibrated ~8000x too bright
/// for any physically-lit surface - hence riders (and their bikes) rendering blown out to
/// white/blue while the CelLit-shaded environment (roads, terrain, props) looks correct.
///
/// <see cref="ShiosaiShaderConversion"/> already hit and fixed this EXACT defect for the
/// environment ("glTF/PbrMetallicRoughness ... rendering blown-out white") by routing
/// converted materials through CelLit instead of HDRP/Lit, specifically because CelLit is
/// "immune to that unit mismatch". This pass applies the same, already-proven fix to the
/// character/bike side, which that environment-scoped pass never covered (different shader
/// name, different asset folder).
///
/// Two kinds of material are handled differently so nothing is tinted incorrectly and no fix
/// is silently reverted by a future reimport:
///  - Real, already-owned .mat ASSETS (Assets/Kuro/Materials/*, plus each NPC's own per-rider
///    livery/body clones created by SakuraNpcRoster) are converted IN PLACE. They are not
///    shared, so this cannot leak a tint onto anyone else.
///  - Materials that are still embedded sub-assets of an imported .glb (Brushed_Metal,
///    Road_Tyre, ...) are SHARED BY REFERENCE across every rider and the player. Editing an
///    imported asset in place is reverted by the next reimport (see the NPC skill's
///    troubleshooting table), so each one is cloned ONCE into a persisted, shared .mat asset
///    under Assets/Kuro/Materials and every renderer that referenced the original is
///    re-pointed at the clone - identical treatment for every current and future user, so no
///    livery tint changes and the player's/NPCs' shared parts stay visually identical to each
///    other, just correctly exposed.
/// </summary>
public static class KuroCharacterCelLitFix
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    internal const string SharedMatDir = "Assets/Kuro/Materials";
    const string PlayerName = "Kuro on Sakura Pass";

    internal const string OldGltfShader = "Shader Graphs/glTF-pbrMetallicRoughness";
    const string HdrpLit = "HDRP/Lit";
    internal const string CelLitName = "MapleRide/HDRP/CelLit";

    [MenuItem("MapleRide/Kuro/Fix Rider Exposure (glTF or HDRP-Lit -> CelLit)")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var roots = new List<Transform>();
        var player = GameObject.Find(PlayerName);
        if (player != null) roots.Add(player.transform);
        else Debug.LogWarning("[kuro-cellit] player not found in scene.");

        foreach (var cyc in Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            roots.Add(cyc.transform);

        Debug.Log($"[kuro-cellit] scanning {roots.Count} rider(s) (player + {roots.Count - (player != null ? 1 : 0)} NPCs).");
        if (FixRoots(roots)) EditorApplication.Exit(0);
    }

    /// <summary>
    /// Same fix, for Shiosai Coast's own persistent scene (split out of SakuraPass by
    /// ShiosaiSceneBuilder - see Assets/Scenes/SC_Persistent.unity). Shiosai's traffic riders
    /// use ShiosaiRiderLod, not NPCCyclist, and never coexisted with SakuraPass's roster in the
    /// same scene at the same time this fix originally ran, so they were never reached by
    /// <see cref="Run"/>.
    /// </summary>
    [MenuItem("MapleRide/Kuro/Fix Rider Exposure (SC_Persistent)")]
    public static void RunOnShiosaiPersistent()
    {
        const string ShiosaiScenePath = "Assets/Scenes/SC_Persistent.unity";
        EditorSceneManager.OpenScene(ShiosaiScenePath, OpenSceneMode.Single);

        var roots = new List<Transform>();
        foreach (var lod in Object.FindObjectsByType<ShiosaiRiderLod>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            roots.Add(lod.transform);

        Debug.Log($"[kuro-cellit] scanning {roots.Count} Shiosai rider(s) in {ShiosaiScenePath}.");
        if (FixRoots(roots)) EditorApplication.Exit(0);
    }

    static bool FixRoots(List<Transform> roots)
    {
        var celLit = Shader.Find(CelLitName);
        if (celLit == null) { Debug.LogError($"[kuro-cellit] shader '{CelLitName}' not found."); EditorApplication.Exit(1); return false; }

        if (!AssetDatabase.IsValidFolder(SharedMatDir))
            AssetDatabase.CreateFolder("Assets/Kuro", "Materials");

        var sharedClones = new Dictionary<Material, Material>();
        int inPlace = 0, shared = 0, riders = 0;

        foreach (var root in roots)
        {
            bool touchedAny = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool touched = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null) continue;

                    // HARDENING GUARD (frame-red revert): on the PLAYER only, never route the
                    // frame tubes through the SHARED red CelLit clone. This conversion clones the
                    // embedded/shared "Colnago_Racing_Red" into Shared_Colnago_Racing_Red_CelLit and
                    // re-points the player's frame renderer at it - the exact revert that puts the
                    // approved matte-black KURO frame back to bright red. The approved player frame
                    // is the per-player KuroBike_Frame_Black.mat (PlayerBikeFrameBlackFix /
                    // PlayerFidelityFix). If that approved clone already exists, re-point straight to
                    // it here so the revert is impossible REGARDLESS of pass order; otherwise fall
                    // through so the later frame-black pass owns the recolour. NPC red frames are
                    // untouched (guarded on the player root name only).
                    if (root.name == PlayerName && m.name.Replace(" (Instance)", "").Contains("Racing_Red"))
                    {
                        var frameBlack = AssetDatabase.LoadAssetAtPath<Material>(SharedMatDir + "/KuroBike_Frame_Black.mat");
                        if (frameBlack != null)
                        {
                            if (mats[i] != frameBlack) { mats[i] = frameBlack; touched = true; }
                            Debug.Log("[kuro-cellit] player frame slot pinned to approved KuroBike_Frame_Black (red revert blocked).");
                            continue;
                        }
                        // no approved black clone yet -> leave for the frame-black pass rather than
                        // baking a shared red clone onto the player.
                        continue;
                    }

                    string shaderName = m.shader.name;
                    if (shaderName != OldGltfShader && shaderName != HdrpLit) continue; // already CelLit or something else intentional

                    string assetPath = AssetDatabase.GetAssetPath(m);
                    Material target;
                    if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".mat"))
                    {
                        ConvertToCelLit(m, celLit);
                        target = m;
                        inPlace++;
                    }
                    else
                    {
                        if (!sharedClones.TryGetValue(m, out target))
                        {
                            target = CloneSharedToCelLit(m, celLit);
                            sharedClones[m] = target;
                            shared++;
                        }
                    }

                    if (mats[i] != target) { mats[i] = target; touched = true; }
                }
                if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); touchedAny = true; }
            }
            if (touchedAny) riders++;
        }

        Debug.Log($"[kuro-cellit] done: {inPlace} owned material(s) converted in place, " +
                  $"{shared} shared material(s) cloned to CelLit, {riders} rider(s) touched.");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[kuro-cellit] scene + assets saved.");
        return true;
    }

    /// <summary>
    /// Same value-preserving remap as ShiosaiShaderConversion.ConvertGltfToLit, retargeted at
    /// CelLit instead of HDRP/Lit (that class's environment-only scope never reaches these
    /// materials). Reads every property BEFORE the shader swap, because assigning .shader drops
    /// any property the new shader does not declare.
    /// </summary>
    internal static void ConvertToCelLit(Material mat, Shader celLit)
    {
        Color baseColor = Color.white;
        Texture baseMap = null;
        float metallic = 0f;
        float smoothness = 0.25f;
        Color emissive = Color.black;

        foreach (var n in new[] { "baseColorFactor", "_BaseColorFactor", "_BaseColor", "_Color" })
            if (mat.HasProperty(n)) { baseColor = mat.GetColor(n); break; }
        foreach (var n in new[] { "baseColorTexture", "_BaseColorTexture", "_BaseColorMap", "_MainTex" })
            if (mat.HasProperty(n)) { var t = mat.GetTexture(n); if (t != null) { baseMap = t; break; } }
        foreach (var n in new[] { "emissiveFactor", "_EmissiveFactor" })
            if (mat.HasProperty(n)) { emissive = mat.GetColor(n); break; }

        // Metallic/roughness only matter here as a rough proxy for specular strength/gloss;
        // CelLit has no PBR metallic model, so a stale/mis-set scalar (e.g. the known
        // Kuro_Body_Matte defect: _Metallic = 1 with no real mask map bound) cannot make the
        // whole surface mirror-reflective the way it did under HDRP/Lit - it only nudges a
        // small, clamped highlight term.
        if (mat.HasProperty("metallicFactor")) metallic = mat.GetFloat("metallicFactor");
        else if (mat.HasProperty("_Metallic")) metallic = mat.GetFloat("_Metallic");

        if (mat.HasProperty("_Smoothness"))
        {
            smoothness = mat.GetFloat("_Smoothness");
        }
        else if (mat.HasProperty("roughnessFactor"))
        {
            // glTF stores roughness; CelLit stores gloss (inverse).
            smoothness = 1f - Mathf.Clamp01(mat.GetFloat("roughnessFactor"));
        }

        mat.shader = celLit;
        mat.SetColor("_Color", baseColor);
        if (baseMap != null) mat.SetTexture("_MainTex", baseMap);
        mat.SetFloat("_Gloss", Mathf.Clamp(smoothness, 0.01f, 1f));
        mat.SetFloat("_SpecStrength", Mathf.Lerp(0.05f, 0.45f, Mathf.Clamp01(metallic)));
        if (emissive.maxColorComponent > 0.001f)
            mat.SetColor("_Color", baseColor + emissive * 0.5f);

        EditorUtility.SetDirty(mat);
    }

    /// <summary>
    /// Clones a SHARED, embedded glTF sub-asset (Brushed_Metal, Road_Tyre, ...) into a real,
    /// persisted .mat asset so the fix survives a future reimport of the source .glb (editing
    /// the embedded original in place would silently revert - see the NPC skill's
    /// troubleshooting table). One clone per distinct source name, reused by every rider/player
    /// renderer that referenced the original, so nothing is re-tinted per rider.
    /// </summary>
    internal static Material CloneSharedToCelLit(Material src, Shader celLit)
    {
        string baseName = src.name.Replace(" (Instance)", "");
        string path = $"{SharedMatDir}/Shared_{baseName}_CelLit.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var clone = new Material(src);
        clone.CopyPropertiesFromMaterial(src);
        clone.name = $"Shared_{baseName}_CelLit";
        ConvertToCelLit(clone, celLit);
        AssetDatabase.CreateAsset(clone, path);
        Debug.Log($"[kuro-cellit] shared '{baseName}' -> {path}");
        return clone;
    }
}

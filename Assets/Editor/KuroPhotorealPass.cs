using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Binds the authored PBR atlas set onto Kuro's materials and tunes every Kuro-owned
/// material for real-world specular response inside the Built-in RP / Gamma pipeline.
///
/// WHY THIS EXISTS (read before changing anything):
///  * Kuro's materials use glTFast's "glTF/PbrMetallicRoughness". On that shader
///    _NORMALMAP / _METALLICGLOSSMAP / _OCCLUSION are shader_feature keywords.
///    Assigning a texture WITHOUT EnableKeyword does absolutely nothing - the map is
///    silently ignored and the material still renders off the flat scalar factors.
///    That is the single most common way this task is "done" but invisible.
///  * From glTFUnityStandardInput.cginc:
///        mg.rg = tex2D(metallicRoughnessTexture, uv).bg;   // B = metallic, G = roughness
///        mg.r *= metallicFactor;  mg.g = 1 - (mg.g * roughnessFactor);
///    i.e. once the MR map is bound, metallicFactor/roughnessFactor become MULTIPLIERS.
///    They must therefore be driven to 1.0 and the absolute values baked in the texture,
///    otherwise PlayerFidelityFix's roughnessFactor = 0.90 would re-darken the whole map.
///  * Occlusion samples the R channel through LerpOneTo(occ, occlusionTexture_strength).
///
/// Run AFTER PlayerFidelityFix (it creates/refreshes the Kuro_* material clones this pass
/// then upgrades). Idempotent: re-running only re-asserts the same values.
/// </summary>
public static class KuroPhotorealPass
{
    const string ScenePath  = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    const string MatDir     = "Assets/Kuro/Materials";

    // --- the authored atlas set (built by assets/3d/kuro/build_kuro_photoreal_maps.py) ---
    const string TexBase   = MatDir + "/Kuro_Body_BaseColor_PR.png";
    const string TexNormal = MatDir + "/Kuro_Body_Normal.png";
    const string TexMR     = MatDir + "/Kuro_Body_MR.png";
    const string TexAO     = MatDir + "/Kuro_Body_AO.png";

    // --- glTFast property names (these are the glTF names, NOT _BumpMap/_MetallicGlossMap) ---
    const string PBaseTex   = "baseColorTexture";
    const string PNormalTex = "normalTexture";
    const string PMRTex     = "metallicRoughnessTexture";
    const string POccTex    = "occlusionTexture";
    const string PNormalScl = "normalTexture_scale";
    const string POccStr    = "occlusionTexture_strength";
    const string PMetal     = "metallicFactor";
    const string PRough     = "roughnessFactor";

    // ---------------------------------------------------------------------------------
    // PROVISIONAL TUNING - illustrative values, not confirmed design requirements.
    // Every number below is a starting point for real-world material response under the
    // "Sakura Sunset Key" rig (Directional, ForcePixel, intensity 1.4, Gamma space, no
    // energy conservation). Raise roughness before raising intensity if anything clips.
    // ---------------------------------------------------------------------------------
    const float BodyNormalScale   = 0.22f; // fabric weave / skin micro-detail strength
    const float BodyOcclusionStr  = 0.60f; // crease + contact darkening strength

    // Untextured materials keep SCALAR response: their UV layout is the same Meshy
    // micro-island atlas, so a tiling detail map there would be meaningless noise.
    // Correct metallic/roughness scalars are the honest win for these.
    const float HelmetMetallic    = 0.00f; // painted plastic shell - dielectric
    const float HelmetRoughness   = 0.28f; // glossy aero shell, tight highlight
    const float BibMetallic       = 0.00f;
    const float BibRoughness      = 0.55f; // lycra: soft sheen, not matte
    const float FrameMetallic     = 0.35f; // clearcoated carbon reads part-metal
    const float FrameRoughness    = 0.30f;
    const float RimMetallic       = 0.55f; // anodized alloy rim
    const float RimRoughness      = 0.28f;

    static readonly Color BibBlack = new Color32(0x10, 0x10, 0x14, 0xFF); // Kage kit black

    [MenuItem("MapleRide/Kuro/Apply Photoreal Materials")]
    public static void RunMenu() => Run();

    /// Batchmode entry point: opens the scene, applies, saves, exits.
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (!Apply())
        {
            EditorApplication.Exit(1);
            return;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[kuropr] done + saved scene.");
        EditorApplication.Exit(0);
    }

    /// Does the actual work against the already-open scene. Does NOT save or exit, so it can
    /// be chained from PlayerFidelityFix. THIS MUST RUN LAST: PlayerFidelityFix rebinds the
    /// old base-colour atlas and sets roughnessFactor = 0.90, both of which would undo the
    /// photoreal set (roughnessFactor is a MULTIPLIER once the MR map is bound).
    public static bool Apply()
    {
        ConfigureImporters();

        var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexBase);
        var nrmTex  = AssetDatabase.LoadAssetAtPath<Texture2D>(TexNormal);
        var mrTex   = AssetDatabase.LoadAssetAtPath<Texture2D>(TexMR);
        var aoTex   = AssetDatabase.LoadAssetAtPath<Texture2D>(TexAO);
        if (baseTex == null || nrmTex == null || mrTex == null || aoTex == null)
        {
            Debug.LogError($"[kuropr] missing authored maps (base={baseTex} n={nrmTex} mr={mrTex} ao={aoTex}). " +
                           "Run assets/3d/kuro/build_kuro_photoreal_maps.py first.");
            return false;
        }

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[kuropr] no player object."); return false; }

        // ---- BODY (face / hair / kit / gloves / socks / shoes) --------------------------
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = smr.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string bn = BaseName(mats[i]);

                if (bn.Contains("Kuro_Body_Matte") || bn.Contains("Material_1"))
                {
                    var m = mats[i];
                    SetTex(m, PBaseTex, baseTex);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", baseTex);
                    m.mainTexture = baseTex;

                    SetTex(m, PNormalTex, nrmTex);
                    SetTex(m, PMRTex, mrTex);
                    SetTex(m, POccTex, aoTex);

                    // Without these three the maps above are silently discarded.
                    m.EnableKeyword("_NORMALMAP");
                    m.EnableKeyword("_METALLICGLOSSMAP");
                    m.EnableKeyword("_OCCLUSION");

                    // Factors are multipliers once the MR map is bound - neutralise them.
                    SetF(m, PMetal, 1.0f);
                    SetF(m, PRough, 1.0f);
                    SetF(m, PNormalScl, BodyNormalScale);
                    SetF(m, POccStr, BodyOcclusionStr);

                    EditorUtility.SetDirty(m);
                    touched = true;
                    Debug.Log($"[kuropr] body '{m.name}': bound base/normal/MR/AO, keywords on, factors=1.0");
                }
                else if (bn.Contains("Helmet"))
                {
                    var m = mats[i];
                    SetF(m, PMetal, HelmetMetallic);
                    SetF(m, PRough, HelmetRoughness);
                    SetF(m, "_Metallic", HelmetMetallic);
                    SetF(m, "_Smoothness", 1f - HelmetRoughness);
                    SetF(m, "_Glossiness", 1f - HelmetRoughness);
                    EditorUtility.SetDirty(m);
                    touched = true;
                    Debug.Log($"[kuropr] helmet '{m.name}': metal={HelmetMetallic} rough={HelmetRoughness}");
                }
                else if (bn.Contains("Bib"))
                {
                    // Ships in-scene as an unsaved "(Instance)" - promote it to a real asset
                    // so the tuning survives a scene reload.
                    var m = Promote(mats[i], "Kuro_Bib_Shorts_Black");
                    SetColor(m, BibBlack);
                    SetF(m, PMetal, BibMetallic);
                    SetF(m, PRough, BibRoughness);
                    SetF(m, "_Metallic", BibMetallic);
                    SetF(m, "_Smoothness", 1f - BibRoughness);
                    SetF(m, "_Glossiness", 1f - BibRoughness);
                    EditorUtility.SetDirty(m);
                    mats[i] = m;
                    touched = true;
                    Debug.Log($"[kuropr] bib shorts -> asset '{m.name}' black, rough={BibRoughness}");
                }
            }
            if (touched) { smr.sharedMaterials = mats; EditorUtility.SetDirty(smr); }
        }

        // ---- BIKE ------------------------------------------------------------------------
        // Only Kuro-owned clones are touched here. Brushed_Metal is a SHARED imported GLB
        // material also used by NPC bikes, so it is deliberately NOT edited in place.
        var bikeT = FindChild(player.transform, "Bike");
        if (bikeT != null)
        {
            var seen = new HashSet<Material>();
            foreach (var r in bikeT.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m)) continue;
                    string bn = BaseName(m);
                    if (bn.Contains("KuroBike_Frame_Black"))
                    {
                        SetF(m, PMetal, FrameMetallic); SetF(m, PRough, FrameRoughness);
                        SetF(m, "_Metallic", FrameMetallic); SetF(m, "_Smoothness", 1f - FrameRoughness);
                        SetF(m, "_Glossiness", 1f - FrameRoughness);
                        EditorUtility.SetDirty(m);
                        Debug.Log($"[kuropr] frame: metal={FrameMetallic} rough={FrameRoughness}");
                    }
                    else if (bn.Contains("KuroBike_Rim_Red"))
                    {
                        SetF(m, PMetal, RimMetallic); SetF(m, PRough, RimRoughness);
                        SetF(m, "_Metallic", RimMetallic); SetF(m, "_Smoothness", 1f - RimRoughness);
                        SetF(m, "_Glossiness", 1f - RimRoughness);
                        EditorUtility.SetDirty(m);
                        Debug.Log($"[kuropr] rim: metal={RimMetallic} rough={RimRoughness}");
                    }
                }
        }
        else Debug.LogWarning("[kuropr] no 'Bike' child under player.");

        EditorUtility.SetDirty(player);
        return true;
    }

    // --- helpers ---------------------------------------------------------------------------

    /// Normal maps MUST be imported as NormalMap or Unity hands the shader raw RGB.
    /// MR/AO are data, never sRGB. Project is Gamma space so the sRGB flag is largely moot
    /// for sampling, but it is set correctly so this still behaves if the project is ever
    /// switched to Linear.
    static void ConfigureImporters()
    {
        SetImporter(TexBase,   TextureImporterType.Default,   true);
        SetImporter(TexNormal, TextureImporterType.NormalMap, false);
        SetImporter(TexMR,     TextureImporterType.Default,   false);
        SetImporter(TexAO,     TextureImporterType.Default,   false);
        AssetDatabase.Refresh();
    }

    static void SetImporter(string path, TextureImporterType type, bool srgb)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"[kuropr] no importer for {path}"); return; }
        bool dirty = false;
        if (ti.textureType != type)      { ti.textureType = type; dirty = true; }
        if (ti.sRGBTexture != srgb)      { ti.sRGBTexture = srgb; dirty = true; }
        if (ti.maxTextureSize < 2048)    { ti.maxTextureSize = 2048; dirty = true; }
        if (ti.textureCompression != TextureImporterCompression.CompressedHQ)
        { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
        if (!ti.mipmapEnabled)           { ti.mipmapEnabled = true; dirty = true; }
        if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
        if (dirty) { ti.SaveAndReimport(); Debug.Log($"[kuropr] importer {path}: type={type} sRGB={srgb}"); }
    }

    static string BaseName(Material m) => m.name.Replace(" (Instance)", "");

    /// Turns an in-scene material instance into a real .mat asset (idempotent).
    static Material Promote(Material src, string assetName)
    {
        string path = $"{MatDir}/{assetName}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var m = new Material(src);
        m.CopyPropertiesFromMaterial(src);
        m.name = assetName;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void SetTex(Material m, string prop, Texture t)
    {
        if (m.HasProperty(prop)) m.SetTexture(prop, t);
        else Debug.LogWarning($"[kuropr] '{m.name}' has no property '{prop}'");
    }

    static void SetF(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }

    static void SetColor(Material m, Color c)
    {
        foreach (var p in new[] { "baseColorFactor", "_BaseColor", "_Color" })
            if (m.HasProperty(p)) m.SetColor(p, c);
    }

    static Transform FindChild(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindChild(c, name); if (r != null) return r; }
        return null;
    }
}

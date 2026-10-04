using UnityEditor;
using UnityEngine;

/// <summary>
/// QA follow-up on the blue/cyan cast fix: Kuro_Body_Matte.mat still carries the ORIGINAL,
/// incorrect PBR scalars (_Metallic = 1 / metallicFactor = 1, _Smoothness = 0) left over from
/// the botched HDRP/Lit conversion. Switching the material's shader to
/// MapleRide/HDRP/CelLit (see KuroCharacterCelLitFix) already stops it acting as a mirror
/// (CelLit has no PBR metallic reflection model), but the stale values are still wrong data
/// sitting on the asset, and they were fed verbatim into the CelLit remap - _Metallic = 1 (a
/// "fully metal" reading with no correctly-packed mask map behind it) produced
/// _SpecStrength = 0.45, an inflated highlight for skin/fabric, alongside _Gloss = 0.01
/// (derived from _Smoothness = 0). Skin, hair and a cycling jersey are never metallic.
///
/// This corrects just the PBR scalars (and, derived from them, CelLit's _Gloss/_SpecStrength)
/// to organic, non-metallic values. It does NOT touch _BaseColor / _MainTex / _NormalMap, so
/// the authored base texture, likeness and livery tint are unchanged.
/// </summary>
public static class KuroBodyMaterialHygiene
{
    const string MatPath = "Assets/Kuro/Materials/Kuro_Body_Matte.mat";

    // Matches the matte tuning PlayerFidelityFix already uses elsewhere on this same character
    // (roughnessFactor = 0.90 -> smoothness = 0.10) "so hair reads as hair, not shattered
    // glass" - the same reasoning applies to skin/fabric under CelLit's specular term.
    const float OrganicMetallic = 0f;
    const float OrganicSmoothness = 0.10f;

    [MenuItem("MapleRide/Kuro/Fix Body Material Metallic Hygiene")]
    public static void Run()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { Debug.LogError($"[kuro-body-hygiene] material not found at {MatPath}"); EditorApplication.Exit(1); return; }

        var hdrpLit = Shader.Find("HDRP/Lit");
        var gltfShader = Shader.Find("Shader Graphs/glTF-pbrMetallicRoughness");
        LogRaw("BEFORE", mat, hdrpLit);

        // Material.HasProperty/SetFloat are gated by whatever shader is CURRENTLY assigned.
        // The material is on MapleRide/HDRP/CelLit now, which declares neither _Metallic,
        // metallicFactor, _Smoothness nor roughnessFactor - so those stale scalars are
        // invisible to the API while CelLit is active and would silently survive untouched.
        // They still live in the material's serialized float block from when it was HDRP/Lit
        // (and, before that, the glTFast shader), so temporary, in-memory-only shader swaps
        // expose them for correction; the storage is shader-agnostic, so this does not disturb
        // _Color/_MainTex/_Gloss/_SpecStrength, which are set afterwards on CelLit as normal.
        var celLit = mat.shader;
        if (hdrpLit != null)
        {
            mat.shader = hdrpLit;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", OrganicMetallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", OrganicSmoothness);
            mat.shader = celLit;
        }
        else
        {
            Debug.LogWarning("[kuro-body-hygiene] HDRP/Lit shader not found - could not reach _Metallic/_Smoothness.");
        }

        // metallicFactor/roughnessFactor are glTF-specific names: neither HDRP/Lit nor CelLit
        // declare them, so they are only reachable through the original glTFast shader. They
        // are otherwise fully inert (no currently-assigned shader ever reads them), but QA
        // flagged them explicitly and a stale "fully metallic" value left in the asset is
        // exactly the kind of latent defect that bites the next person who inspects/reuses it.
        if (gltfShader != null)
        {
            mat.shader = gltfShader;
            if (mat.HasProperty("metallicFactor")) mat.SetFloat("metallicFactor", OrganicMetallic);
            if (mat.HasProperty("roughnessFactor")) mat.SetFloat("roughnessFactor", 1f - OrganicSmoothness);
            mat.shader = celLit;
        }
        else
        {
            Debug.LogWarning("[kuro-body-hygiene] glTFast shader not found - could not reach metallicFactor/roughnessFactor.");
        }

        // Re-derive the CelLit-facing controls the same way KuroCharacterCelLitFix's remap
        // does, so they stay consistent with the corrected scalars instead of the stale ones.
        if (mat.HasProperty("_Gloss")) mat.SetFloat("_Gloss", Mathf.Clamp(OrganicSmoothness, 0.01f, 1f));
        if (mat.HasProperty("_SpecStrength")) mat.SetFloat("_SpecStrength", Mathf.Lerp(0.05f, 0.45f, OrganicMetallic));

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        LogRaw("AFTER ", mat, hdrpLit, gltfShader);
        Debug.Log("[kuro-body-hygiene] done + saved.");
        EditorApplication.Exit(0);
    }

    /// <summary>Peeks at the raw PBR scalars by momentarily switching to a shader that
    /// declares them, then restoring whatever shader the material had (CelLit).</summary>
    static void LogRaw(string label, Material m, Shader hdrpLit, Shader gltfShader = null)
    {
        var original = m.shader;
        if (hdrpLit != null) m.shader = hdrpLit;
        string msg =
            $"[kuro-body-hygiene] {label} _Metallic={GetF(m,"_Metallic")} " +
            $"_Smoothness={GetF(m,"_Smoothness")} " +
            $"_BaseColor={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "n/a")} " +
            $"_BaseColorMap={(m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null ? m.GetTexture("_BaseColorMap").name : "NULL")}";
        if (gltfShader != null)
        {
            m.shader = gltfShader;
            msg += $" metallicFactor={GetF(m,"metallicFactor")} roughnessFactor={GetF(m,"roughnessFactor")}";
        }
        m.shader = original;
        msg += $" | on-active-shader({original.name}) _Gloss={GetF(m,"_Gloss")} _SpecStrength={GetF(m,"_SpecStrength")} " +
               $"_Color={(m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "n/a")} " +
               $"_MainTex={(m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null ? m.GetTexture("_MainTex").name : "NULL")}";
        Debug.Log(msg);
    }

    static string GetF(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p).ToString("F3") : "n/a";
}

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SHARED, idempotent "raw glTF metallic chrome -&gt; matte CelLit" conversion.
///
/// ROOT CAUSE (QA finding #2, confirmed by shader/material dump + zoomed renders on four Minato
/// liveries, not guessed): every ambient NPC rider's body mesh (<c>Mesh_0</c>) and
/// <c>SmileDecal</c> (<c>CoralSmile</c>) are left bound to the RAW glTF import shader
/// <c>Shader Graphs/glTF-pbrMetallicRoughness</c> with metallicFactor=1 / roughnessFactor=1. A
/// metallic=1 surface mirrors its surroundings; on a broad flat surface (a face) that reads as
/// one undifferentiated blown-out patch, while on a curved surface (a helmet dome) the SAME
/// defect breaks into directional bands that look like ordinary shading - "half 3D, half not" on
/// one character, same underlying shader bug. <see cref="KuroPlayerBodyCelLit"/> already solved
/// this exact defect for the player by cloning a matte, non-metallic CelLit material (the
/// approved recipe lives at <c>Assets/Kuro/Materials/Player_Material_0.mat</c>) per source
/// texture and re-pointing the player's renderers at it - but that fix is hard-scoped to the
/// literal player GameObject and none of the ambient-traffic rosters (Minato/Shiosai/Sakura/...)
/// ever called anything like it, so every route's ambient riders shipped with the defect.
///
/// This class is the extracted, generalised version of that same recipe so any roster can call
/// ONE routine right after it clones/recolours a rider's kit, instead of each region re-solving
/// (or never solving) the problem on its own. Two entry points, matching the two situations a
/// roster's own material actually needs:
///
///  - <see cref="ConvertInPlace"/> - the roster ALREADY made a private per-rider clone (e.g.
///    ApplyKit's `new Material(src)` body clone, persisted as its own .mat asset). Mutating that
///    clone directly is safe: nothing else references it.
///  - <see cref="GetOrCreatePersistedClone"/> - the renderer still points straight at the
///    SHARED, embedded glTF sub-asset no roster ever cloned (the classic case: every roster
///    disables <c>SmileDecal</c> rather than recolouring it, so its material was never touched).
///    Mutating that in place would repaint every other rider/region referencing the same
///    embedded material, and a GLB reimport would silently revert it anyway - so this instead
///    builds/refreshes a REGION-OWNED, persisted .mat clone and returns it, never touching the
///    shared source.
///
/// <see cref="ConvertRiderBody"/> combines both: point it at one rider's body root right after
/// livery staging and it converts every offending renderer slot with the correct treatment for
/// each, including the SmileDecal (which is swept incidentally - it is just another renderer
/// under the same root), with a per-call cache so many riders sharing one never-cloned source
/// material (the smile) converge on ONE persisted clone instead of one each.
/// </summary>
public static class NpcCelLitConversion
{
    public const string CelLitShaderName = "MapleRide/HDRP/CelLit";
    public const string GltfShaderName = "Shader Graphs/glTF-pbrMetallicRoughness";
    const string TemplatePath = "Assets/Kuro/Materials/Player_Material_0.mat";

    // glTFast materials do not use the built-in _Color/_MainTex names, and which properties
    // exist depends on the import path, so every variant that might actually be present is read.
    static readonly string[] TextureProps =
        { "baseColorTexture", "_BaseColorTexture", "_BaseColorMap", "_BaseMap", "_MainTex" };
    static readonly string[] ColorProps =
        { "baseColorFactor", "_BaseColorFactor", "_BaseColor", "_Color" };

    static Shader _celLit;
    static Material _template;

    static bool EnsureTemplate()
    {
        if (_celLit == null) _celLit = Shader.Find(CelLitShaderName);
        if (_template == null) _template = AssetDatabase.LoadAssetAtPath<Material>(TemplatePath);
        if (_celLit == null) Debug.LogError($"[npc-cellit] shader '{CelLitShaderName}' not found.");
        if (_template == null) Debug.LogError($"[npc-cellit] recipe template not found at {TemplatePath}.");
        return _celLit != null && _template != null;
    }

    /// <summary>True only for the raw, unconverted glTF import shader - never for CelLit or
    /// anything else intentionally assigned.</summary>
    public static bool IsUnconvertedGltf(Material m) =>
        m != null && m.shader != null && m.shader.name == GltfShaderName;

    static Texture ReadBaseMap(Material src)
    {
        foreach (var n in TextureProps)
            if (src.HasProperty(n)) { var t = src.GetTexture(n); if (t != null) return t; }
        return null;
    }

    static Color ReadBaseColor(Material src)
    {
        foreach (var n in ColorProps)
            if (src.HasProperty(n)) return src.GetColor(n);
        return Color.white;
    }

    /// <summary>Applies the approved matte recipe onto `target` (already a private material),
    /// preserving `target`'s OWN base-colour texture/tint captured before the shader swap.
    /// Reads happen in the caller, before this runs, because assigning .shader drops any
    /// property the new shader does not declare.</summary>
    static void ApplyCelLitRecipe(Material target, Texture baseMap, Color baseColor, bool isDecal)
    {
        target.shader = _celLit;
        target.CopyPropertiesFromMaterial(_template);
        target.shader = _celLit; // CopyPropertiesFromMaterial can re-assert the source shader
        if (target.HasProperty("_MainTex")) target.SetTexture("_MainTex", baseMap);
        if (target.HasProperty("_Color")) target.SetColor("_Color", baseColor);
        // Winding-agnostic: a decal quad's normal-facing winding is inconsistent across rigs.
        if (isDecal && target.HasProperty("_Cull")) target.SetFloat("_Cull", 0f);

        // ROOT CAUSE (Minato helmet/jersey "tear" finding, confirmed by reading MR_Weather() in
        // MapleRideCelLit.shader directly, and by two falsified alternative fixes below - not
        // guessed): `Player_Material_0.mat` (the "approved matte recipe" every character clones
        // via CopyPropertiesFromMaterial above) is built on `MapleRide/HDRP/CelLit`, a shader
        // authored for STATIC WORLD-ANCHORED PROPS (rock/building weathering) that bakes in a
        // per-pixel procedural "surface detail / per-prop tint variation / sun-bleach wear /
        // moss / underside grime" system (MR_Weather) driven entirely by ABSOLUTE WORLD POSITION
        // and WORLD-SPACE SURFACE NORMAL, with non-zero authored defaults (_DetailAmount 0.3 at
        // _DetailScale 9 per METRE, _WearAmount 0.25, _MossAmount 0.35, _GrimeAmount 0.30,
        // _DappleStrength 0.35, _TintVariation 0.18). That is fine for a metre-scale stationary
        // rock, but a small (~0.15 m), constantly-moving, highly-curved character surface (a
        // helmet dome) samples this noise at extremely high effective frequency and across every
        // world-space normal direction within one small screen region - producing exactly the
        // jagged, multi-tinted (cream "wear" + dark olive "moss"/"grime") blotch mosaic reported
        // as a "tear", body-wide (jersey and shorts too, not only the helmet), identically on
        // every livery (it is a shader effect, not a texture/tint), and NEVER in a Blender render
        // (Blender never evaluates this HDRP shader at all).
        //
        // Two more plausible-looking fixes were tried and EMPIRICALLY FALSIFIED by fresh Minato
        // captures before this one: forcing FilterMode.Point/no-mip on the base texture (zero
        // visible change - this was never a texture-sampling defect) and a body-wide mesh reweld
        // to close disconnected-island seams (made the pattern WORSE, because it perturbed the
        // very per-face normals MR_Weather reads, at newly-introduced non-manifold junctions).
        // Neither remains applied. This is the actual, verified fix: character materials must
        // not carry a prop's world-space weathering system at all.
        if (target.HasProperty("_DetailAmount")) target.SetFloat("_DetailAmount", 0f);
        if (target.HasProperty("_TintVariation")) target.SetFloat("_TintVariation", 0f);
        if (target.HasProperty("_WearAmount")) target.SetFloat("_WearAmount", 0f);
        if (target.HasProperty("_MossAmount")) target.SetFloat("_MossAmount", 0f);
        if (target.HasProperty("_GrimeAmount")) target.SetFloat("_GrimeAmount", 0f);
        if (target.HasProperty("_DappleStrength")) target.SetFloat("_DappleStrength", 0f);
    }

    /// <summary>
    /// Idempotent. If <paramref name="mat"/> is on the raw glTF metallic shader, rewrites it IN
    /// PLACE onto the matte CelLit recipe, preserving its own base-colour texture + tint. No-op
    /// (returns false) if it is already on any other shader. Call this ONLY on a material you
    /// already privately own (a fresh per-rider/per-character clone) - never on a material still
    /// referenced by other renderers, or every one of them repaints too.
    /// </summary>
    public static bool ConvertInPlace(Material mat, bool isDecal = false)
    {
        if (!IsUnconvertedGltf(mat)) return false;
        if (!EnsureTemplate()) return false;
        var baseMap = ReadBaseMap(mat);
        var baseColor = ReadBaseColor(mat);
        string name = mat.name;
        ApplyCelLitRecipe(mat, baseMap, baseColor, isDecal);
        mat.name = name;
        return true;
    }

    /// <summary>
    /// Idempotent. Ensures a persisted matte-CelLit clone of <paramref name="source"/> exists at
    /// <paramref name="assetPath"/> and returns it, WITHOUT ever touching <paramref name="source"/>
    /// itself (source may be a shared, embedded glTF sub-asset referenced by many renderers across
    /// many regions). If <paramref name="source"/> is already off the glTF shader, returns it
    /// unchanged - nothing to convert. Re-running with the same path refreshes the existing asset
    /// rather than creating a duplicate, so a re-stage converges instead of leaking assets.
    /// </summary>
    public static Material GetOrCreatePersistedClone(Material source, string assetPath, bool isDecal = false)
    {
        if (source == null) return null;
        if (!IsUnconvertedGltf(source)) return source;
        if (!EnsureTemplate()) return source;

        var baseMap = ReadBaseMap(source);
        var baseColor = ReadBaseColor(source);
        string matName = Path.GetFileNameWithoutExtension(assetPath);

        var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null)
        {
            ApplyCelLitRecipe(existing, baseMap, baseColor, isDecal);
            existing.name = matName;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        var built = new Material(_template);
        ApplyCelLitRecipe(built, baseMap, baseColor, isDecal);
        built.name = matName;
        EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
        AssetDatabase.CreateAsset(built, assetPath);
        return built;
    }

    /// <summary>
    /// Walks every renderer under <paramref name="root"/> (skipping anything under
    /// <paramref name="excludeUnder"/>, typically the bike anchor) and converts ANY material
    /// still on the raw glTF shader to matte CelLit, choosing the correct treatment per slot:
    /// a material already living at its own ".mat" asset path (a roster's own per-rider clone,
    /// e.g. the body-kit clone ApplyKit just made) is converted IN PLACE; anything else - the
    /// still-shared, embedded glTF sub-asset a roster never cloned (the SmileDecal, or a rider
    /// whose livery skips the kit clone entirely) - is redirected to a persisted, region-owned
    /// clone under <paramref name="materialDir"/>, cached (via <paramref name="sharedCache"/> if
    /// supplied, so many riders in one Stage() call converge on ONE clone rather than one each)
    /// by source-material identity. Safe/idempotent to call every time a rig is (re)built.
    /// Returns the number of renderer material slots touched.
    /// </summary>
    public static int ConvertRiderBody(GameObject root, string materialDir, Transform excludeUnder = null,
                                        Dictionary<Material, Material> sharedCache = null)
    {
        var cache = sharedCache ?? new Dictionary<Material, Material>();
        int touchedSlots = 0;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (excludeUnder != null && r.transform.IsChildOf(excludeUnder)) continue;
            var mats = r.sharedMaterials;
            bool touched = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!IsUnconvertedGltf(m)) continue;

                bool isDecalSlot = r.gameObject.name == "SmileDecal";
                string assetPath = AssetDatabase.GetAssetPath(m);
                if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".mat"))
                {
                    // Owned per-rider/per-character clone - safe to mutate directly.
                    if (ConvertInPlace(m, isDecalSlot)) { EditorUtility.SetDirty(m); touched = true; touchedSlots++; }
                }
                else
                {
                    // Still the shared, embedded glTF sub-asset - never mutate; redirect to a
                    // persisted, region-owned clone, one per distinct source material.
                    if (!cache.TryGetValue(m, out var clone))
                    {
                        string baseName = Sanitize(m.name.Replace(" (Instance)", ""));
                        string path = $"{materialDir}/Shared_{baseName}_CelLit.mat";
                        clone = GetOrCreatePersistedClone(m, path, isDecalSlot);
                        cache[m] = clone;
                    }
                    if (clone != null && mats[i] != clone) { mats[i] = clone; touched = true; touchedSlots++; }
                }
            }
            if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }
        return touchedSlots;
    }

    static void EnsureFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    /// <summary>
    /// Loud regression guard: returns false (and logs every offender) if ANY renderer material
    /// under <paramref name="root"/> is still on the raw glTF metallic shader. Generalised from
    /// <see cref="KuroPlayerBodyCelLit.AssertNoGltfBody"/> so any roster's self-test can call the
    /// same check.
    /// </summary>
    public static bool AssertNoGltfMaterials(GameObject root, string label, Transform excludeUnder = null)
    {
        var bad = new List<string>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (excludeUnder != null && r.transform.IsChildOf(excludeUnder)) continue;
            foreach (var m in r.sharedMaterials)
                if (IsUnconvertedGltf(m))
                    bad.Add($"{r.name}/{m.name}");
        }
        if (bad.Count > 0)
        {
            Debug.LogError($"[npc-cellit] {label}: {bad.Count} material(s) still on " +
                           $"'{GltfShaderName}' (metallic glTF import -> chrome): {string.Join(", ", bad)}.");
            return false;
        }
        return true;
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Migrates MinatoCoast's persisted crowd material assets off the raw glTF import shader
/// (<c>Shader Graphs/glTF-pbrMetallicRoughness</c>) onto <c>MapleRide/HDRP/CelLit</c>.
///
/// BACKGROUND. <see cref="MinatoCrowdPopulation"/> builds the plaza crowd by CLONING the donor
/// .glb sub-asset materials into persisted assets under
/// <c>Assets/Environment/MinatoCoast/Materials/Crowd</c> (it must clone: the donors are shared by
/// the hero/named riders, so tinting them in place would repaint half the game). Those clones
/// faithfully inherited the donors' shader - the raw glTFast import shader - so 41 crowd material
/// assets stayed on a shader the project's own HDRP repair tooling classifies as
/// <c>BAD / pipeline-incompatible</c>. This is the same class of defect
/// <see cref="EnvironmentHdrpMigration"/> fixed for FujiRidge/TakaMountains/MapleCity/
/// AzoraHighlands, but arriving from the glTF import path instead of the legacy built-in path,
/// and it is the same defect <see cref="NpcCelLitConversion"/> already solved for individual
/// characters - so this pass deliberately reuses that conversion rather than inventing a second
/// recipe.
///
/// VISIBLE SYMPTOM (verified by render, not by log): the crowd cyclists and pedestrians render as
/// near-black silhouettes with a metallic sheen, because the glTF material ships
/// metallicFactor=1 / roughnessFactor=1 and has no cel ambient floor.
///
/// SECOND-ORDER FIX. <see cref="MinatoCrowdPopulation.ApplyDistanceReadability"/> wanted to lift
/// the distant tier's cel ambient floor (<c>_CharAmbient</c>) and rim (<c>_RimStrength</c>), but
/// those properties do not exist on the glTF shader, so that half of the readability fix was
/// silently skipped on every <c>_Dist_</c> material. Once converted, the floors exist, so this
/// pass applies them - using the SAME constants the population pass uses, read from there rather
/// than re-typed, so the two can never drift.
///
/// IDEMPOTENT. Materials already off the glTF shader are left completely untouched, so re-running
/// converges. Running it twice reports 0 converted the second time.
/// </summary>
public static class MinatoCrowdCelLitMigration
{
    const string CrowdMatDir = "Assets/Environment/MinatoCoast/Materials/Crowd";

    /// <summary>Marks the distant-tier clones (see MinatoCrowdPopulation.ApplyDistanceReadability).
    /// Those are the only ones that should carry the lifted readability floors; the near-range
    /// LOD0 tier keeps the standard recipe.</summary>
    const string DistanceMarker = "_Dist_";

    [MenuItem("MapleRide/Environment/Migrate Minato Crowd Materials To HDRP", priority = 22)]
    public static void Migrate() => Run(dryRun: false);

    [MenuItem("MapleRide/Environment/Audit Minato Crowd Materials (HDRP)", priority = 23)]
    public static void Audit() => Run(dryRun: true);

    static void Run(bool dryRun)
    {
        string tag = dryRun ? "[minato-crowd-mig audit]" : "[minato-crowd-mig]";

        if (Shader.Find(NpcCelLitConversion.CelLitShaderName) == null)
        {
            Debug.LogError($"{tag} ABORT - '{NpcCelLitConversion.CelLitShaderName}' not found. " +
                           "Nothing was changed.");
            return;
        }

        var guids = AssetDatabase.FindAssets("t:Material", new[] { CrowdMatDir });
        var pending = new List<Material>();
        var shaderCensus = new Dictionary<string, int>();

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            string s = mat.shader.name;
            shaderCensus[s] = shaderCensus.TryGetValue(s, out var n) ? n + 1 : 1;
            if (NpcCelLitConversion.IsUnconvertedGltf(mat)) pending.Add(mat);
        }

        foreach (var kv in shaderCensus.OrderByDescending(k => k.Value))
            Debug.Log($"{tag} census {kv.Value,4} x '{kv.Key}'");

        if (pending.Count == 0)
        {
            Debug.Log($"{tag} nothing to do - 0 material(s) on " +
                      $"'{NpcCelLitConversion.GltfShaderName}' under {CrowdMatDir}.");
            return;
        }

        if (dryRun)
        {
            Debug.Log($"{tag} {pending.Count} material(s) WOULD be converted to " +
                      $"'{NpcCelLitConversion.CelLitShaderName}'.");
            foreach (var m in pending) Debug.Log($"{tag}   would convert '{m.name}'");
            return;
        }

        int converted = 0, floored = 0;
        foreach (var mat in pending)
        {
            // The smile decal is an unlit-ish quad whose winding is inconsistent across rigs;
            // NpcCelLitConversion handles that by clearing _Cull, exactly as it does for the
            // hero riders. Detected by name here because these are loose assets, not renderers.
            bool isDecal = mat.name.IndexOf("CoralSmile", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!NpcCelLitConversion.ConvertInPlace(mat, isDecal)) continue;
            converted++;

            // Re-apply the distance-readability floors that the glTF shader could not carry.
            if (mat.name.Contains(DistanceMarker))
            {
                if (mat.HasProperty("_CharAmbient"))
                    mat.SetFloat("_CharAmbient", MinatoCrowdPopulation.DistanceCharAmbient);
                if (mat.HasProperty("_RimStrength"))
                    mat.SetFloat("_RimStrength", MinatoCrowdPopulation.DistanceRimStrength);
                floored++;
            }

            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"{tag} converted {converted} material(s) to " +
                  $"'{NpcCelLitConversion.CelLitShaderName}'; " +
                  $"{floored} distant-tier material(s) given the readability floors " +
                  $"(_CharAmbient={MinatoCrowdPopulation.DistanceCharAmbient}, " +
                  $"_RimStrength={MinatoCrowdPopulation.DistanceRimStrength}).");
    }
}

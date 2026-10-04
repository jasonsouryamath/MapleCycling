using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// AMBIENT PEDESTRIANS (copilot, 2026-09-26). Indexes the Maple City Life civilian looks into
/// Assets/Resources/Pedestrians/PedestrianWardrobe.asset (see <see cref="PedestrianWardrobe"/>).
/// References only; the asset is overwritten IN PLACE (CopySerialized) so its GUID never changes.
/// run_steps.ps1 "PedestrianWardrobeBuilder.Build|peds_wardrobe.log|1"
/// </summary>
public static class PedestrianWardrobeBuilder
{
    const string LifeDir = "Assets/Environment/MapleCity/Life";
    const string OutDir = "Assets/Resources/Pedestrians";
    const string OutPath = OutDir + "/PedestrianWardrobe.asset";

    [MenuItem("MapleRide/Pedestrians/Build Wardrobe Index")]
    public static void Build()
    {
        Directory.CreateDirectory(OutDir);
        var fresh = ScriptableObject.CreateInstance<PedestrianWardrobe>();
        var donors = new List<PedestrianWardrobe.Donor>();
        foreach (var guid in AssetDatabase.FindAssets("MapleLife_Helmetless_ t:Mesh", new[] { LifeDir + "/Meshes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) continue;
            string key = Path.GetFileNameWithoutExtension(path).Substring("MapleLife_Helmetless_".Length);
            var d = new PedestrianWardrobe.Donor { key = key, helmetless = mesh };
            var caps = new List<Mesh>();
            for (int s = 0; s < 8; s++)
            {
                var cap = AssetDatabase.LoadAssetAtPath<Mesh>($"{LifeDir}/Meshes/MapleLife_HairCap_{key}_S{s}.asset");
                if (cap == null) break;
                caps.Add(cap);
            }
            d.caps = caps.ToArray();
            var looks = new List<PedestrianWardrobe.Look>();
            for (int v = 0; v < 12; v++)
            {
                string pre = $"{LifeDir}/Materials/MapleLife_Crowd_{key}_V{v}";
                var body = AssetDatabase.LoadAssetAtPath<Material>(pre + "_Body.mat");
                if (body == null) break;
                looks.Add(new PedestrianWardrobe.Look
                {
                    id = "V" + v,
                    body = body,
                    hair = AssetDatabase.LoadAssetAtPath<Material>(pre + "_Hair.mat"),
                    dist = AssetDatabase.LoadAssetAtPath<Material>(pre + "_Dist.mat"),
                    hairCap = AssetDatabase.LoadAssetAtPath<Material>(pre + "_HairCap.mat"),
                });
            }
            d.looks = looks.ToArray();
            if (d.looks.Length == 0 || d.caps.Length == 0)
            {
                Debug.LogWarning($"[peds] wardrobe: {key} has {d.looks.Length} looks / {d.caps.Length} caps - skipped.");
                continue;
            }
            donors.Add(d);
        }
        fresh.donors = donors.OrderBy(d => d.key).ToArray();

        var existing = AssetDatabase.LoadAssetAtPath<PedestrianWardrobe>(OutPath);
        if (existing == null) AssetDatabase.CreateAsset(fresh, OutPath);
        else
        {
            EditorUtility.CopySerialized(fresh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(fresh);
        }
        AssetDatabase.SaveAssets();
        var final = AssetDatabase.LoadAssetAtPath<PedestrianWardrobe>(OutPath);
        Debug.Log($"[peds] wardrobe: {final.donors.Length} donors, " +
                  $"{final.donors.Sum(d => d.looks.Length)} civilian looks, {final.donors.Sum(d => d.caps.Length)} hair caps: " +
                  string.Join(", ", final.donors.Select(d => $"{d.key}({d.looks.Length}L/{d.caps.Length}C)")));
    }
}

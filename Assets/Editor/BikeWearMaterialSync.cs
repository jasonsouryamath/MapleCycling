using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Agent HQ ticket: "Add curvature-baked roughness/edge-wear maps to all bike materials".
///
/// design_assets/3d/kuro/build_kuro_mapleride_aero_bike.py now bakes one curvature (Pointiness)
/// wear mask per bike material into Assets/Kuro/Player/Textures/Wear/<MatName>_Wear.png, packed
/// glTF-style (R unused, G=roughness multiplier, B=metallic multiplier, both in [0,1]). Because
/// metallicRoughnessTexture is multiplicative-only, this only ever darkens/polishes relative to
/// each material's own authored roughnessFactor/metallicFactor/baseColorFactor -- it never
/// changes livery colour or overrides per-character tuning.
///
/// This idempotent sync assigns each baked mask into the matching material's
/// metallicRoughnessTexture slot for:
///   (a) the player's extracted bike materials (Assets/Kuro/Player/Materials/KuroBike_<name>.mat)
///   (b) every per-named-NPC clone that currently exists for that material
///       (Assets/Kuro/NPC/Materials/*_<name>.mat) -- today that is only MR_Aero_Frame,
///       MR_Aero_Highlight and MR_Maple_Accent; the other 4 bike materials have no NPC clones yet
///       and are covered automatically the moment one is created.
/// No other material property is touched. Safe to re-run.
/// </summary>
public static class BikeWearMaterialSync
{
    const string WearDir = "Assets/Kuro/Player/Textures/Wear";
    const string PlayerMaterialDir = "Assets/Kuro/Player/Materials";
    const string NpcMaterialDir = "Assets/Kuro/NPC/Materials";

    static readonly string[] BikeMaterials =
    {
        "MR_Aero_Frame", "MR_Aero_Highlight", "MR_Maple_Accent",
        "MR_Carbon", "MR_Road_Tyre", "MR_Drivetrain_Metal", "MR_Brake_Black",
    };

    [MenuItem("MapleRide/Bikes/Sync Bike Wear Maps")]
    public static void Run()
    {
        int texturesFixed = 0, materialsTouched = 0, materialsSkipped = 0;

        foreach (var matName in BikeMaterials)
        {
            string texPath = $"{WearDir}/{matName}_Wear.png";
            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[bikewear] missing baked texture {texPath}, skipping {matName}");
                continue;
            }
            if (importer.sRGBTexture || importer.textureType != TextureImporterType.Default)
            {
                importer.sRGBTexture = false;
                importer.textureType = TextureImporterType.Default;
                importer.SaveAndReimport();
                texturesFixed++;
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                Debug.LogWarning($"[bikewear] could not load {texPath}, skipping {matName}");
                continue;
            }

            // (a) player's own extracted bike material
            string playerMatPath = $"{PlayerMaterialDir}/KuroBike_{matName}.mat";
            var playerMat = AssetDatabase.LoadAssetAtPath<Material>(playerMatPath);
            if (playerMat != null)
            {
                if (AssignWearTexture(playerMat, tex)) materialsTouched++; else materialsSkipped++;
            }
            else
            {
                Debug.LogWarning($"[bikewear] no player material at {playerMatPath}");
            }

            // (b) every existing per-NPC clone for this material
            string[] cloneGuids = AssetDatabase.FindAssets("t:Material", new[] { NpcMaterialDir });
            foreach (var guid in cloneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith($"_{matName}.mat")) continue;
                var cloneMat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (cloneMat == null) continue;
                if (AssignWearTexture(cloneMat, tex)) materialsTouched++; else materialsSkipped++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[bikewear] done. textures reimported={texturesFixed}, materials updated={materialsTouched}, already-synced={materialsSkipped}");
    }

    /// <summary>Sets metallicRoughnessTexture only if it isn't already this exact texture.
    /// Leaves every other property (colour factors, scalars) untouched.</summary>
    static bool AssignWearTexture(Material mat, Texture2D tex)
    {
        if (!mat.HasProperty("metallicRoughnessTexture"))
        {
            Debug.LogWarning($"[bikewear] {mat.name} has no metallicRoughnessTexture property, skipping");
            return false;
        }
        var current = mat.GetTexture("metallicRoughnessTexture");
        if (current == tex) return false;
        Undo.RecordObject(mat, "Assign bike wear map");
        mat.SetTexture("metallicRoughnessTexture", tex);
        EditorUtility.SetDirty(mat);
        return true;
    }
}

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Import-time gate for the hair/helmet clearance contract.</summary>
public static class HelmetHairShapeKeyValidation
{
    [MenuItem("MapleRide/Diagnostics/Validate helmet-driven hair shape keys")]
    public static void Run()
    {
        const string folder = "Assets/Characters/Pedestrians/Import";
        var guids = AssetDatabase.FindAssets("t:Model PedHair_", new[] { folder });
        int checkedMeshes = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(path).StartsWith("PedHair_", StringComparison.OrdinalIgnoreCase)) continue;
            bool found = false;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var mesh = asset as Mesh;
                if (mesh == null) continue;
                checkedMeshes++;
                int shape = mesh.GetBlendShapeIndex(HelmetDrivenHairShapeKey.ShapeName);
                if (shape < 0)
                    throw new InvalidOperationException($"{path}: missing {HelmetDrivenHairShapeKey.ShapeName}");
                found = true;
                Debug.Log($"[hair-shape] PASS {Path.GetFileName(path)} mesh={mesh.name} verts={mesh.vertexCount} shape={shape}");
            }
            if (!found) throw new InvalidOperationException($"{path}: no imported mesh");
        }
        if (checkedMeshes == 0) throw new InvalidOperationException("No PedHair meshes were imported");
        Debug.Log($"[hair-shape] RESULT {checkedMeshes} imported hair meshes validated");
    }
}

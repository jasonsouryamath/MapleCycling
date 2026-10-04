using UnityEditor;
using UnityEngine;

/// FIX-IMPLEMENTER probe: force-reimports the player GLB and dumps the IMPORTED asset's meshes,
/// to tell whether Unity actually picked up the on-disk bytes (single mesh) or is serving a stale
/// cached composite import.
public static class FiKuroImportProbe
{
    public static void Run()
    {
        const string glb = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
        AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();

        var all = AssetDatabase.LoadAllAssetsAtPath(glb);
        Debug.Log($"[importprobe] {glb} -> {all.Length} sub-assets");
        foreach (var a in all)
        {
            if (a is Mesh m)
                Debug.Log($"[importprobe]   MESH '{m.name}' verts={m.vertexCount} tris={m.triangles.Length / 3}");
            else if (a is Material mat)
                Debug.Log($"[importprobe]   MAT '{mat.name}' shader='{mat.shader.name}'");
            else
                Debug.Log($"[importprobe]   {a.GetType().Name} '{a.name}'");
        }
    }
}

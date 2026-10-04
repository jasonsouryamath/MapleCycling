using UnityEditor;
using UnityEngine;

// Helper: force Unity to reimport the Kuro real rider GLB so batchmode staging picks up
// on-disk geometry edits (the artifact cache can otherwise serve a stale mesh).
public static class ForceReimportKuro
{
    public static void Run()
    {
        const string path = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        Debug.Log("[reimport] forced reimport of " + path);
    }
}

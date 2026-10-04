using UnityEditor;

/// <summary>
/// One-shot forced reimport for the QA rear-helmet-pareidolia fix session: after
/// zz_helmet_vent-adjacent Blender edits to KuroNPC_Coral_Rigged.glb on disk, batchmode Unity
/// does not always pick up the change via its normal file watcher within a single -executeMethod
/// call, so force it synchronously before anything else runs.
/// </summary>
public static class FiCoralHelmetFixReimport
{
    public static void Run()
    {
        AssetDatabase.ImportAsset("Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb",
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();
    }
}

using UnityEditor;
using UnityEngine;

/// <summary>
/// ANIME KURO INSTALL PASS. Forces a reimport of the POC rig GLB and then stages the Shiosai
/// POC rider in ONE batchmode launch.
///
/// Why the forced reimport: Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb is REPLACED ON DISK
/// when a new Kuro base is built (photoreal -> anime). Unity's artifact cache happily serves
/// the PREVIOUS import of that path in batchmode, so Stage() would instantiate the old mesh
/// and the verification render would show a rider that no longer exists on disk. This is the
/// same trap ForceReimportKuro.cs was written for; it is folded in here because
/// ShiosaiRealisticRiderPoc.Stage() calls EditorApplication.Exit(0) in batchmode, which would
/// kill the process before a second -executeMethod could run.
/// </summary>
public static class AnimeKuroReimportAndStage
{
    public static void Run()
    {
        const string glb = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
        Debug.Log("[anime-kuro] forcing reimport of " + glb);
        AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        Debug.Log("[anime-kuro] reimport done -> staging POC");
        ShiosaiRealisticRiderPoc.Stage();   // exits the editor in batchmode
    }
}

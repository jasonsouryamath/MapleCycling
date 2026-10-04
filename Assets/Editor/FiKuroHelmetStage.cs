using UnityEditor;
using UnityEngine;

/// <summary>
/// FIX-IMPLEMENTER helper: reimport the edited Kuro GLB, re-instantiate it into SakuraPass
/// via the approved swap (which re-applies matte CelLit body materials + asserts no chrome),
/// then run the read-only QA orbit capture. One batch launch so the project lock is never
/// released mid-way (see NPC skill batchmode-lock note).
/// </summary>
public static class FiKuroHelmetStage
{
    public static void ReimportSwapCapture()
    {
        const string glb = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
        Debug.Log("[fi-helmet] forcing reimport of " + glb);
        AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();

        Debug.Log("[fi-helmet] applying approved Kuro visual swap (re-instantiate + CelLit)");
        KuroPlayerModelSwap.Apply();

        Debug.Log("[fi-helmet] running QA orbit capture");
        QaKuroOrbit.Run(); // exits editor in batchmode
    }
}

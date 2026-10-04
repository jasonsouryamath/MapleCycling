using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces a reimport of the player GLB (after a mesh swap on disk) BEFORE running the scale
/// comparison capture, so the render can never silently show the stale cached mesh.
/// </summary>
public static class FiReimportAndCapture
{
    public static void Run()
    {
        const string glb = "Assets/Kuro/kuro_cycle_bib_colored.glb";
        Debug.Log("[fi] forcing reimport of " + glb);
        AssetDatabase.ImportAsset(glb, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();
        Debug.Log("[fi] reimport done, running CaptureComparison");
        CoralScaleDiagnostics.CaptureComparison();
        Debug.Log("[fi] capture complete");
    }
}

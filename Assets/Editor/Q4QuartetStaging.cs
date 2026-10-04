using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages every roster that carries one of the four concept-sheet riders, in one editor boot.
///
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -quit -executeMethod Q4QuartetStaging.Run
///
/// The quartet (design_assets/3d/kuro/q4_build_quartet.py) is spread across four rosters and the
/// coast traffic pool, and each of those entry points opens and SAVES Assets/Scenes/SakuraPass.unity
/// - all five regions live in that one scene. Running them from five separate -executeMethod
/// launches costs five asset-database imports and five domain reloads for no benefit, and the
/// order matters: the coast pool is staged under the Shiosai environment root, which has to
/// exist before it can be parented.
///
/// Each staging pass is independently idempotent (exact-name prune then rebuild), so this is
/// safe to re-run.
/// </summary>
public static class Q4QuartetStaging
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Debug.Log("[q4stage] ---- Azora Highlands (Shinobu) ----");
        AzoraNpcRoster.AddAllToScene();

        Debug.Log("[q4stage] ---- Maple City (Akihiro) ----");
        MapleCityNpcRoster.AddAllToScene();

        Debug.Log("[q4stage] ---- Taka Mountains (Akane) ----");
        TakaNpcRoster.AddAllToScene();

        Debug.Log("[q4stage] ---- Shiosai Coast (all four, one slot each) ----");
        // StageFromMenu rather than Stage: the coast environment root is INACTIVE whenever
        // another region is the drawn one, and GameObject.Find skips inactive objects.
        // ShiosaiNpcTraffic's own lookup walks the scene's root list, which does not.
        ShiosaiNpcTraffic.StageFromMenu();

        Debug.Log("[q4stage] done.");
    }

    /// <summary>
    /// Every self test that covers a roster the quartet was added to, in one editor boot.
    ///
    ///   Unity.exe -projectPath &lt;abs&gt; -batchmode -quit -executeMethod Q4QuartetStaging.SelfTests
    ///
    /// Read-only: each of these opens the scene, inspects it and discards its changes, so this
    /// never leaves a crash-recovery backup behind for the next interactive session to restore.
    /// </summary>
    public static void SelfTests()
    {
        Case("Sakura roster greetings", SakuraNpcRosterSelfTest.Run);
        Case("Coral", CoralNpcSelfTest.Run);
        Case("Azora", AzoraSelfTest.Run);
        Case("Maple City", MapleCitySelfTest.Run);
        Case("Taka", TakaSelfTest.Run);
        Case("Sakura traffic", TrafficSelfTest.Run);
        Debug.Log("[q4test] done.");
    }

    /// <summary>
    /// Runs one self test and keeps going if it throws. Several of these predate the current
    /// scene layout and one of them (ShiosaiSceneSelfTest) still opens a scene file that no
    /// longer exists; letting the first casualty abort the batch would hide every test after it.
    /// </summary>
    static void Case(string label, System.Action test)
    {
        Debug.Log($"[q4test] ---- {label} ----");
        try { test(); }
        catch (System.Exception e) { Debug.LogWarning($"[q4test] {label} threw: {e.Message}"); }
    }
}

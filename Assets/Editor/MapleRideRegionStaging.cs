using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The entire world of MapleRide ships inside ONE boot scene, Assets/Scenes/SakuraPass.unity.
/// <see cref="RegionDirector"/> shows/hides a single per-region root ("Sakura Pass Environment",
/// "Shiosai Coast Environment", "Maple City Environment", ...) and every UNLOCKED region must be
/// present in that saved scene, or its world-map pin drops the rider onto the empty checkerboard
/// start line (RideStartPad's universal launch - the only geometry RegionDirector cannot hide).
///
/// Any pass that regenerates the boot scene from an EMPTY scene (as MapleRideKuroSetup.
/// BuildSakuraPass does) silently drops every sibling region root. This class is the ONE authority
/// that re-stages all built regions back into the shared scene and leaves Sakura Pass the active
/// default before the scene is saved. Both the manual repair menu and BuildSakuraPass route
/// through it so the two can never drift, and every save runs the region-root guardrail assertion
/// in <see cref="ShiosaiCoastEnvironment.LogRegionRootStates"/>.
/// </summary>
public static class MapleRideRegionStaging
{
    public const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>
    /// Repairs a boot scene that lost its SIBLING region roots (e.g. after an empty-scene rebuild
    /// clobbered them) WITHOUT re-running Sakura's heavy environment pass. Opens the scene, re-stages
    /// the five non-Sakura regions, restores Sakura as the active default, asserts, and saves. Use
    /// this for the common case where "Sakura Pass Environment" is intact but the others are gone.
    /// </summary>
    [MenuItem("MapleRide/Environment/Repair Missing Region Roots", priority = 21)]
    public static void RepairBootSceneRegions()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        StageSiblingRegions();
        SaveWithSakuraDefault("repair-missing-region-roots");
    }

    /// <summary>
    /// Rebuilds EVERY unlocked region root into the shared boot scene, restores Sakura Pass as the
    /// active default, asserts nothing is missing, and saves. Idempotent - each region pass rebuilds
    /// its own root by exact name and prunes duplicates - so this is the safe way to repair a boot
    /// scene that lost region roots to an empty-scene rebuild.
    /// </summary>
    [MenuItem("MapleRide/Environment/Restage All Regions Into Boot Scene", priority = 20)]
    public static void RestageAllRegions()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Sakura's own environment pass owns the "Sakura Pass Environment" root.
        SakuraPassEnvironment.Apply();
        // The other five built regions.
        StageSiblingRegions();

        SaveWithSakuraDefault("restage-all-regions");
    }

    /// <summary>
    /// Stages every NON-Sakura built region into the ACTIVE scene. Each region's Apply() opens the
    /// shared boot scene when it is not already open (headless), rebuilds ONLY its own root by exact
    /// name, and is safe to run back-to-back in a single editor process. Sakura is excluded because
    /// its environment pass is driven by the Sakura build / preview flows that call this.
    /// </summary>
    public static void StageSiblingRegions()
    {
        ShiosaiCoastEnvironment.Apply();     // "Shiosai Coast Environment"
        MapleCityEnvironment.Apply();        // "Maple City Environment"
        AzoraHighlandsEnvironment.Apply();   // "Azora Highlands Environment"
        TakaMountainsEnvironment.Apply();    // "Taka Mountains Environment"
        FujiRidgeEnvironment.Apply();        // "Fuji Ridge Environment"
    }

    /// <summary>
    /// Restores Sakura Pass as the active/default region, asserts that every unlocked region root
    /// is present (throws in batch mode if not), and saves the active scene. THIS is the only
    /// correct way to leave the shared boot scene: whatever region is active at save time is the
    /// state the game boots into, so it must always be Sakura.
    /// </summary>
    public static void SaveWithSakuraDefault(string when)
    {
        var active = EditorSceneManager.GetActiveScene();

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
            FindObjectsInactive.Include);
        if (regions != null)
        {
            // FastTravel carries the session/course side effects when the graph resolves; the
            // direct visibility enforcement afterwards guarantees the serialized active-root
            // invariant (Sakura shown, every sibling hidden) even when the session is not yet
            // resolved in batch mode and FastTravel early-returns.
            regions.FastTravel(RegionCatalog.SakuraPass);
            regions.currentRegionId = RegionCatalog.SakuraPass;
            regions.ApplyEnvironmentVisibility();
            EditorUtility.SetDirty(regions);
        }
        else
        {
            Debug.LogWarning("[regions] no RegionDirector in scene - cannot set the default region.");
        }

        // Guardrail: never save the boot scene with an unlocked region root missing.
        ShiosaiCoastEnvironment.LogRegionRootStates(when);

        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[regions] saved '{active.path}' with Sakura Pass active ({when}).");
    }
}

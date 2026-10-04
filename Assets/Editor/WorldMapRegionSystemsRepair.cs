using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Restores the World Map's fast-travel plumbing (<see cref="RegionDirector"/>,
/// <see cref="WorldMapHud"/> wiring, the shared <c>EventSystem</c>) into the saved
/// Assets/Scenes/SakuraPass.unity WITHOUT touching any environment art.
///
/// WHY THIS EXISTS: the "no map/route destination is clickable" bug. RegionDirector was
/// missing from the checked-in scene entirely (dropped by an earlier, unsaved editor pass),
/// so <c>WorldMapHud.regions</c> was null and every pin's <see cref="WorldMapHud.Travel"/>
/// hit the "no RegionDirector wired" guard and did nothing - a click a player cannot tell
/// from a dead button. <see cref="ShiosaiCoastEnvironment.SetupRegionSystems"/> is the
/// existing, already-idempotent staging step that wires this (it is normally the tail of the
/// coast's environment build); this entry point calls ONLY that step against the scene as it
/// already is, so it never rebuilds Shiosai/Maple City/Azora/Taka/Fuji terrain that this scene
/// does not currently contain.
/// </summary>
public static class WorldMapRegionSystemsRepair
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Ride/Repair World Map Region Systems", priority = 44)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var graph = RouteGraph.Load();
        ShiosaiCoastEnvironment.SetupRegionSystems(graph);

        var regions = Object.FindFirstObjectByType<RegionDirector>();
        if (regions == null)
        {
            Debug.LogError("[worldmap-repair] SetupRegionSystems ran but no RegionDirector was " +
                           "found afterwards - not saving.");
            return;
        }

        // The scene must always load in Sakura Pass (the only region this scene currently
        // builds) - see RegionDirector.SyncFromSession's own comment on why the saved state
        // has to be correct on its own rather than rely on a runtime repair.
        regions.currentRegionId = RegionCatalog.SakuraPass;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        ShiosaiCoastEnvironment.LogRegionRootStates("post-repair");

        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[worldmap-repair] RegionDirector + WorldMapHud wiring restored and saved to " +
                  $"'{active.path}'. currentRegionId='{regions.currentRegionId}'.");
    }
}

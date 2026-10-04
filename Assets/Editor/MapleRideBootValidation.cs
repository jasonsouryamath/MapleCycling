using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MapleRideBootValidation
{
    private const string Key = "mapleride.boot.validation";
    private static double _deadline;
    private static int _stage;
    private static bool _failed;

    static MapleRideBootValidation()
    {
        if (SessionState.GetBool(Key, false)) MapleRideTitleScreen.forceInBatchMode = true;
        EditorApplication.playModeStateChanged += Changed;
    }

    public static void Run()
    {
        if (EditorBuildSettings.scenes.Length == 0 || EditorBuildSettings.scenes[0].path != MapleRideBoot.ScenePath)
            throw new System.InvalidOperationException("Boot is not the first build scene.");
        EditorSceneManager.OpenScene(MapleRideBoot.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        MapleRideTitleScreen.forceInBatchMode = true;
        EditorApplication.EnterPlaymode();
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _stage = 0;
            _deadline = EditorApplication.timeSinceStartup + 45;
            EditorApplication.update += Poll;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            MapleRideTitleScreen.forceInBatchMode = false;
            EditorApplication.update -= Poll;
            Debug.Log("[boot-validation] " + (_failed ? "FAIL" : "PASS") + ": title -> map without ride/environment load.");
            if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline) { Fail("Timed out."); return; }
        if (_stage == 2)
        {
            if (MapleRideBoot.Active != null) return;
            if (SceneManager.GetActiveScene().path != MapleRideBoot.RegionScenePath(RegionCatalog.ShiosaiCoast))
            { Fail("Wrong region loaded after selecting Shiosai."); return; }
            foreach (var region in RegionCatalog.Regions)
                if (region.Id != RegionCatalog.ShiosaiCoast)
                    foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                        if (root.name == region.EnvironmentRoot)
                        { Fail("Unselected environment loaded: " + root.name); return; }
            var ride = Object.FindAnyObjectByType<RideBootstrap>();
            if (ride == null || ride.regions.currentRegionId != RegionCatalog.ShiosaiCoast)
            { Fail("Selected route was not applied."); return; }
            Debug.Log("[boot-validation] PASS: Shiosai selected and loaded, Sakura environment absent, phase=" +
                      MapleRideFlowDirector.Instance.phase);
            EditorApplication.update -= Poll;
            EditorApplication.ExitPlaymode();
            return;
        }
        if (MapleRideBoot.Active == null) return;
        if (Object.FindAnyObjectByType<RideBootstrap>() != null || SceneManager.sceneCount != 1 ||
            SceneManager.GetActiveScene().path != MapleRideBoot.ScenePath)
        { Fail("A ride scene loaded before selection."); return; }
        if (_stage == 0)
        {
            var title = Object.FindAnyObjectByType<MapleRideTitleScreen>();
            if (title == null) return;
            title.StartRide();
            _stage = 1;
            return;
        }
        var map = Object.FindAnyObjectByType<WorldMapHud>();
        if (map == null || !map.isOpen || !map.selectionMode || string.IsNullOrEmpty(map.FocusedRegionId))
        { Fail("Map was not open with a selectable destination."); return; }
        if (Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length != 1)
        { Fail("Duplicate EventSystems."); return; }
        Debug.Log("[boot-validation] selection ready, focus=" + map.FocusedRegionId + ", no RideBootstrap.");
        _stage = 2;
        _deadline = EditorApplication.timeSinceStartup + 600;
        map.Travel(RegionCatalog.Find(RegionCatalog.ShiosaiCoast));
    }

    private static void Fail(string reason)
    {
        _failed = true;
        Debug.LogError("[boot-validation] " + reason);
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

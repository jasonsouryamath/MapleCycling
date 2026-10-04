using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Exports region payloads from the shared authoring scene without saving changes to it.</summary>
public static class MapleRidePlayableScenes
{
    [MenuItem("MapleRide/Startup/Export Playable Region Scenes")]
    public static void Export()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory("Assets/Scenes/Playable");
        var scene = EditorSceneManager.OpenScene(MapleRideSceneBootstrap.PlayableScene, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();
        var streamers = new Dictionary<RouteDressingStreamer, RouteDressingStreamer.Chunk[]>();
        foreach (var root in roots)
            foreach (var streamer in root.GetComponentsInChildren<RouteDressingStreamer>(true))
                streamers[streamer] = streamer.chunks;
        var holding = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var settings = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MapleRideBoot.ScenePath, true) };
        try
        {
            foreach (var region in RegionCatalog.Regions)
            {
                if (!region.Unlocked) continue;
                if (region.Id == RegionCatalog.ShuntaMetro)
                {
                    // Standalone scene (no baked cells in the shared authoring scene): keep it registered, don't export.
                    string shunta = MapleRideBoot.RegionScenePath(region.Id);
                    if (File.Exists(shunta)) settings.Add(new EditorBuildSettingsScene(shunta, true));
                    Debug.Log("[boot-export] skip " + region.Id + " (standalone scene, no shared-scene export)");
                    continue;
                }
                int environmentRoots = 0;
                foreach (var root in roots) if (root.name == region.EnvironmentRoot) environmentRoots++;
                if (environmentRoots == 0) throw new InvalidOperationException("No environment for " + region.Id);
                foreach (var root in roots)
                    if (BelongsToOtherRegion(root, region)) SceneManager.MoveGameObjectToScene(root, holding);
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == region.EnvironmentRoot) root.SetActive(true);
                var ride = UnityEngine.Object.FindAnyObjectByType<RideBootstrap>();
                if (ride == null || ride.session == null) throw new InvalidOperationException("Missing ride core.");
                ride.session.courseId = region.BuiltCourseId;
                if (ride.regions != null) ride.regions.currentRegionId = region.Id;
                var path = MapleRideBoot.RegionScenePath(region.Id);
                foreach (var entry in streamers)
                {
                    if (entry.Key.gameObject.scene != scene) continue;
                    var chunks = new List<RouteDressingStreamer.Chunk>();
                    foreach (var chunk in entry.Value)
                        if (chunk.root != null && chunk.root.gameObject.scene == scene) chunks.Add(chunk);
                    entry.Key.chunks = chunks.ToArray();
                }
                if (!EditorSceneManager.SaveScene(scene, path, true)) throw new IOException("Cannot save " + path);
                settings.Add(new EditorBuildSettingsScene(path, true));
                Debug.Log("[boot-export] " + region.Id + ": " + new FileInfo(path).Length + " bytes, " +
                          scene.GetRootGameObjects().Length + " roots; " + environmentRoots + " environment roots.");
                foreach (var root in holding.GetRootGameObjects()) SceneManager.MoveGameObjectToScene(root, scene);
                foreach (var entry in streamers) entry.Key.chunks = entry.Value;
            }
            EditorBuildSettings.scenes = settings.ToArray();
            Debug.Log("[boot-export] PASS v2: boot first, " + (settings.Count - 1) + " isolated region payloads; rosters and streamer references filtered.");
        }
        finally
        {
            // Discard the temporary active-state/course changes as well as any moved-root changes.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.Refresh();
        }
        // Every normal export produces streamed payloads, so subsequent environment fixes
        // cannot silently revert playable regions to monolithic loading.
        MapleRideStreamingScenes.ExportAll();
    }

    private static bool BelongsToOtherRegion(GameObject root, RegionCatalog.Region selected)
    {
        string owner = null;
        switch (root.name)
        {
            case "NPCs": case "Route Collision": case "Zones": case "Sakura Townsfolk": owner = RegionCatalog.SakuraPass; break;
            case "Maple City NPCs": owner = RegionCatalog.MapleCity; break;
            case "Azora NPCs": owner = RegionCatalog.AzoraHighlands; break;
            case "Taka NPCs": owner = RegionCatalog.TakaMountains; break;
            case "Fuji NPCs": owner = RegionCatalog.FujiRidge; break;
            case "Nagisa NPCs": owner = "nagisa_bay"; break;
        }
        if (owner != null) return owner != selected.Id;
        foreach (var region in RegionCatalog.Regions)
        {
            if (region.Id == selected.Id) continue;
            if (!string.IsNullOrEmpty(region.EnvironmentRoot) && root.name == region.EnvironmentRoot) return true;
            // Legacy extra roots such as region traffic/sky/rosters are named with their region.
            if (root.name.StartsWith(region.DisplayName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

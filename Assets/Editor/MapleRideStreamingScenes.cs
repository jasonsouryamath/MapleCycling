using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Bakes independent static scenery into additive cells. Authoring remains in SakuraPass;
/// only generated playable scenes are modified. Scene object references never cross cells.
/// </summary>
public static class MapleRideStreamingScenes
{
    public const string ManifestPath = "Assets/Scenes/Playable/StreamingSceneManifest.json";
    [Serializable] private sealed class BuildManifest { public string[] scenes; }
    private const int ExportVersion = 2;
    private const float CellSize = 256f;
    private const float MaximumObjectSpan = 256f;
    private const float LoadDistance = 500f;
    private sealed class Node
    {
        public Transform transform;
        public Bounds bounds;
        public bool hasBounds, staticOnly = true, selfStatic = true;
        public int renderers;
        public readonly List<Node> children = new List<Node>();
    }
    private sealed class Candidate
    {
        public Node node;
        public bool pinned;
    }
    private sealed class Bucket
    {
        public Bounds bounds;
        public readonly List<Candidate> members = new List<Candidate>();
    }

    [MenuItem("MapleRide/Startup/Bake Additive Region Cells")]
    public static void ExportAll()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var settings = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MapleRideBoot.ScenePath, true) };
        // Retain recoverable full payloads outside Assets. A failed bake never replaces a base scene.
        string generation = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string backupDir = "tools/unity/streaming_backups/" + generation;
        Directory.CreateDirectory(backupDir);
        var backups = new Dictionary<string, string>();
        var previousSettings = EditorBuildSettings.scenes;
        string manifestBackup = backupDir + "/StreamingSceneManifest.json";
        if (File.Exists(ManifestPath)) File.Copy(ManifestPath, manifestBackup);
        try
        {
            foreach (var region in RegionCatalog.Regions)
            {
                if (!region.Unlocked) continue;
                string path = MapleRideBoot.RegionScenePath(region.Id);
                if (region.Id == RegionCatalog.ShuntaMetro)
                {
                    // No baked cells: keep the scene registered in settings/manifest, skip streaming export.
                    if (File.Exists(path)) settings.Add(new EditorBuildSettingsScene(path, true));
                    Debug.Log("[stream-export] skip " + region.Id + " (standalone scene, no cells to bake)");
                    continue;
                }
                if (!File.Exists(path)) throw new FileNotFoundException("Missing playable region", path);
                string backup = backupDir + "/" + region.Id + ".unity";
                File.Copy(path, backup, false);
                backups.Add(path, backup);
                try { ExportRegion(region, path, generation, settings); }
                finally
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
            }
            EditorBuildSettings.scenes = settings.ToArray();
            var scenePaths = new List<string>();
            foreach (var entry in settings) scenePaths.Add(entry.path);
            // Lab 2 syncs Assets back, but deliberately does not publish ProjectSettings.
            // A generated asset manifest carries the exact scene registration to both editors.
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(new BuildManifest { scenes = scenePaths.ToArray() }, true));
            AssetDatabase.Refresh();
        }
        catch
        {
            // All region bases and their build registration are one publish operation.
            // New generation cells may remain unused, but the previous playable set stays valid.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var backup in backups)
            {
                File.Copy(backup.Value, backup.Key, true);
                AssetDatabase.ImportAsset(backup.Key, ImportAssetOptions.ForceUpdate);
            }
            EditorBuildSettings.scenes = previousSettings;
            if (File.Exists(manifestBackup)) File.Copy(manifestBackup, ManifestPath, true);
            else if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
            throw;
        }
        Debug.Log("[stream-export] PASS: boot + 8 region bases and additive cells; full-scene backups: " + backupDir);
    }

    [MenuItem("MapleRide/Startup/Register Streamed Scenes")]
    public static void RegisterBuildScenes()
    {
        if (!File.Exists(ManifestPath)) return;
        var manifest = JsonUtility.FromJson<BuildManifest>(File.ReadAllText(ManifestPath));
        if (manifest == null || manifest.scenes == null || manifest.scenes.Length < 9 ||
            manifest.scenes[0] != MapleRideBoot.ScenePath)
            throw new InvalidOperationException("Invalid streaming build manifest.");
        var settings = new List<EditorBuildSettingsScene>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in manifest.scenes)
        {
            if (!seen.Add(path) || !File.Exists(path)) throw new InvalidOperationException("Invalid streaming scene: " + path);
            settings.Add(new EditorBuildSettingsScene(path, true));
        }
        var old = EditorBuildSettings.scenes;
        bool changed = old.Length != settings.Count;
        if (!changed)
            for (int i = 0; i < old.Length; i++)
                if (!old[i].enabled || old[i].path != settings[i].path) { changed = true; break; }
        if (changed) EditorBuildSettings.scenes = settings.ToArray();
        Debug.Log("[stream-register] " + settings.Count + " scenes registered from generated manifest.");
    }

    private static void ExportRegion(RegionCatalog.Region region, string path, string generation,
                                     List<EditorBuildSettingsScene> settings)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        long originalBytes = new FileInfo(path).Length;
        SceneManager.SetActiveScene(scene);
        var ride = Object.FindFirstObjectByType<RideBootstrap>(FindObjectsInactive.Include);
        if (ride == null) throw new InvalidOperationException("Missing ride in " + path);
        var existing = ride.GetComponent<RegionSceneStreamer>();
        settings.Add(new EditorBuildSettingsScene(path, true));
        if (existing != null && existing.cells != null && existing.cells.Length > 0 && existing.exportVersion == ExportVersion)
        {
            foreach (var cell in existing.cells)
            {
                if (!File.Exists(cell.scenePath)) throw new FileNotFoundException("Missing baked cell", cell.scenePath);
                settings.Add(new EditorBuildSettingsScene(cell.scenePath, true));
            }
            Debug.Log("[stream-export] retained already-baked " + region.Id + ": " + existing.cells.Length + " cells.");
            return;
        }
        if (existing != null && existing.cells != null)
        {
            // Upgrade a previous generated manifest without reauthoring or dropping scenery.
            // Old cell assets stay recoverable; only the new base/manifest is published.
            foreach (var cell in existing.cells)
            {
                if (!File.Exists(cell.scenePath)) throw new FileNotFoundException("Missing baked cell", cell.scenePath);
                originalBytes += new FileInfo(cell.scenePath).Length;
                var oldCell = EditorSceneManager.OpenScene(cell.scenePath, OpenSceneMode.Additive);
                foreach (var root in oldCell.GetRootGameObjects()) SceneManager.MoveGameObjectToScene(root, scene);
                EditorSceneManager.CloseScene(oldCell, true);
            }
            SceneManager.SetActiveScene(scene);
        }

        // The old cullers reference in-scene transforms. Actual scene streaming replaces that
        // residency control in generated payloads; their authoring components remain untouched.
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var culler in root.GetComponentsInChildren<RouteDressingStreamer>(true))
            {
                culler.ShowAll();
                culler.chunks = Array.Empty<RouteDressingStreamer.Chunk>();
                culler.enabled = false;
            }
            foreach (var culler in root.GetComponentsInChildren<MinatoRouteStreamer>(true))
            {
                culler.ShowAll();
                culler.cells = Array.Empty<MinatoRouteStreamer.Cell>();
                culler.enabled = false;
            }
        }
        var candidates = new List<Candidate>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != region.EnvironmentRoot) continue;
            root.SetActive(true);
            Physics.SyncTransforms();
            var node = Inspect(root.transform);
            // Keep the environment root in the base for RegionDirector and startup validation.
            foreach (var child in node.children) Collect(child, candidates);
        }
        var owner = new Dictionary<int, int>();
        for (int i = 0; i < candidates.Count; i++)
            foreach (var t in candidates[i].node.transform.GetComponentsInChildren<Transform>(true))
                owner[t.gameObject.GetInstanceID()] = i;
        int references = ProtectReferences(scene, candidates, owner);
        var buckets = new SortedDictionary<string, Bucket>(StringComparer.Ordinal);
        int movedRenderers = 0, movedObjects = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.pinned) continue;
            var p = candidate.node.bounds.center;
            string key = Mathf.FloorToInt(p.x / CellSize) + "_" + Mathf.FloorToInt(p.z / CellSize);
            if (!buckets.TryGetValue(key, out var bucket))
                buckets.Add(key, bucket = new Bucket { bounds = candidate.node.bounds });
            else bucket.bounds.Encapsulate(candidate.node.bounds);
            bucket.members.Add(candidate);
            movedRenderers += candidate.node.renderers;
            movedObjects += candidate.node.transform.GetComponentsInChildren<Transform>(true).Length;
        }
        if (buckets.Count == 0) throw new InvalidOperationException("No safe streaming cells for " + region.Id);
        string folder = "Assets/Scenes/Playable/Cells/" + region.Id + "/" + generation;
        Directory.CreateDirectory(folder);
        var cells = new List<RegionSceneStreamer.Cell>();
        long cellBytes = 0;
        int initialCells = 0;
        long initialBytes = 0;
        var graph = RouteGraph.Load();
        var def = graph != null ? graph.Course(region.BuiltCourseId) : null;
        if (def == null) throw new InvalidOperationException("Missing course " + region.BuiltCourseId);
        var course = RouteCourse.Build(graph, def);
        Vector3 start = course.PositionAt(0);
        foreach (var entry in buckets)
        {
            var cellScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var parents = new Dictionary<Transform, Transform>();
            foreach (var candidate in entry.Value.members)
            {
                var t = candidate.node.transform;
                // Imported prefab instances retain native modification records referring to
                // their former scene parent. Unpack only generated static copies before moving;
                // mesh/material assets and authoring prefab instances are unchanged.
                foreach (var child in t.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject))
                        PrefabUtility.UnpackPrefabInstance(child.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var parent = CopyParent(t.parent, cellScene, parents);
                // Retain local TRS and the complete ancestor transform chain. Reparenting with
                // worldPositionStays can lose shear under rotated, nonuniformly scaled groups.
                t.SetParent(null, false);
                SceneManager.MoveGameObjectToScene(t.gameObject, cellScene);
                t.SetParent(parent, false);
            }
            if (EditorSceneManager.DetectCrossSceneReferences(cellScene) || EditorSceneManager.DetectCrossSceneReferences(scene))
                throw new InvalidOperationException("Cross-scene reference in " + region.Id + "/" + entry.Key);
            string cellPath = folder + "/cell_" + entry.Key + ".unity";
            if (!EditorSceneManager.SaveScene(cellScene, cellPath)) throw new IOException("Cannot save " + cellPath);
            var cell = new RegionSceneStreamer.Cell { scenePath = cellPath, bounds = entry.Value.bounds };
            cells.Add(cell);
            settings.Add(new EditorBuildSettingsScene(cellPath, true));
            long bytes = new FileInfo(cellPath).Length;
            cellBytes += bytes;
            if (Vector3.Distance(start, cell.bounds.ClosestPoint(start)) <= LoadDistance)
            { initialCells++; initialBytes += bytes; }
            EditorSceneManager.CloseScene(cellScene, true);
        }
        if (EditorSceneManager.DetectCrossSceneReferences(scene))
            throw new InvalidOperationException("Cross-scene reference left in base " + region.Id);
        var streamer = existing != null ? existing : ride.gameObject.AddComponent<RegionSceneStreamer>();
        streamer.regionId = region.Id;
        streamer.exportVersion = ExportVersion;
        streamer.loadDistanceM = LoadDistance;
        streamer.unloadDistanceM = 800f;
        streamer.criticalDistanceM = 180f;
        streamer.cells = cells.ToArray();
        if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Cannot save streamed base " + path);
        long baseBytes = new FileInfo(path).Length;
        string summary = $"[stream-export] {region.Id}: original={originalBytes} bytes; base={baseBytes} bytes; {cells.Count} cells={cellBytes} bytes; " +
            $"start={initialCells} cells/{initialBytes} bytes; moved={movedObjects} objects/{movedRenderers} renderers; " +
            $"protected reference edges={references}.";
        Debug.Log(summary);
        File.AppendAllText("tools/unity/streaming_backups/" + generation + "/summary.txt", summary + Environment.NewLine);
    }

    private static Transform CopyParent(Transform source, Scene destination, Dictionary<Transform, Transform> copied)
    {
        if (source == null) return null;
        if (copied.TryGetValue(source, out var found)) return found;
        var parent = CopyParent(source.parent, destination, copied);
        var clone = new GameObject(source.name).transform;
        SceneManager.MoveGameObjectToScene(clone.gameObject, destination);
        clone.SetParent(parent, false);
        clone.localPosition = source.localPosition;
        clone.localRotation = source.localRotation;
        clone.localScale = source.localScale;
        clone.gameObject.SetActive(source.gameObject.activeSelf);
        clone.gameObject.layer = source.gameObject.layer;
        copied.Add(source, clone);
        return clone;
    }

    private static Node Inspect(Transform transform)
    {
        var node = new Node { transform = transform };
        foreach (var component in transform.GetComponents<Component>())
        {
            if (!IsStaticComponent(component)) node.selfStatic = node.staticOnly = false;
            if (component is Renderer renderer)
            {
                Encapsulate(node, renderer.bounds);
                node.renderers++;
            }
            if (component is Collider collider && collider.enabled && collider.gameObject.activeInHierarchy)
                Encapsulate(node, collider.bounds);
        }
        foreach (Transform child in transform)
        {
            var next = Inspect(child);
            node.children.Add(next);
            node.staticOnly &= next.staticOnly;
            node.renderers += next.renderers;
            if (next.hasBounds) Encapsulate(node, next.bounds);
        }
        return node;
    }

    private static void Encapsulate(Node node, Bounds bounds)
    {
        if (!node.hasBounds) { node.bounds = bounds; node.hasBounds = true; }
        else node.bounds.Encapsulate(bounds);
    }

    private static bool IsStaticComponent(Component component)
    {
        // Conservative whitelist: moving actors, shader controllers, volumes, lights, particles,
        // audio, terrain and unknown/missing scripts all remain in their original base hierarchy.
        return component is Transform || component is MeshFilter || component is MeshRenderer ||
               component is MeshCollider || component is BoxCollider || component is SphereCollider ||
               component is CapsuleCollider || component is LODGroup || component is OcclusionArea ||
               component is MinatoRouteStreamer || component is RouteDressingStreamer;
    }

    private static void Collect(Node node, List<Candidate> candidates)
    {
        // Never detach a static-looking child from an actor or a behaviour controlling its parent.
        if (!node.selfStatic) return;
        // A prefab's internal transforms cannot be detached safely. Move the complete instance
        // when small enough, otherwise retain it globally (especially mountain/road prefabs).
        bool prefab = PrefabUtility.IsPartOfPrefabInstance(node.transform.gameObject);
        if (prefab && !PrefabUtility.IsOutermostPrefabInstanceRoot(node.transform.gameObject)) return;
        var size = node.bounds.size;
        // A tall individual landmark stays global. A container spanning a climb must still
        // be traversed: its aggregate height can exceed 100 m even when every tree is small.
        if (node.hasBounds && size.y > 100f && node.staticOnly &&
            (node.transform.GetComponent<Renderer>() != null || prefab)) return;
        if (node.staticOnly && node.hasBounds && node.renderers > 0 &&
            Mathf.Max(size.x, Mathf.Max(size.y, size.z)) <= MaximumObjectSpan)
        {
            candidates.Add(new Candidate { node = node });
            return;
        }
        if (prefab) return;
        foreach (var child in node.children) Collect(child, candidates);
    }

    private static int ProtectReferences(Scene scene, List<Candidate> candidates, Dictionary<int, int> owner)
    {
        int edges = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                int source = owner.TryGetValue(component.gameObject.GetInstanceID(), out int id) ? id : -1;
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var value = property.objectReferenceValue;
                        if (value == null || EditorUtility.IsPersistent(value)) continue;
                        var go = value as GameObject;
                        if (value is Component referenced) go = referenced.gameObject;
                        if (go == null || go.scene != scene) continue;
                        int target = owner.TryGetValue(go.GetInstanceID(), out int other) ? other : -1;
                        if (source == target) continue;
                        if (source >= 0) candidates[source].pinned = true;
                        if (target >= 0) candidates[target].pinned = true;
                        edges++;
                    }
                }
            }
        return edges;
    }
}

// Copy-back imports only the manifest after all cells/bases have been copied. Register on
// the next editor tick so AssetDatabase has completed the whole import before querying GUIDs.
internal sealed class MapleRideStreamingManifestPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (string path in imported)
            if (path == MapleRideStreamingScenes.ManifestPath)
            {
                EditorApplication.delayCall += MapleRideStreamingScenes.RegisterBuildScenes;
                break;
            }
    }
}

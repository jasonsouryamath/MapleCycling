using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Milestone D, spec section 6.1: gives Shiosai Coast its own scene instead of leaving the
/// production coast embedded as one generated root inside the 208 MB shared SakuraPass scene.
///
/// The scene is derived from SakuraPass rather than authored from nothing, deliberately. The
/// ride rig (RideBootstrap, RegionDirector, RideSession, the Kuro rider with its grounding and
/// RouteFollower, the follow camera, the named key/fill lights the ambience system drives by
/// exact name) is validated, hand-tuned state that is not reproducible by script. Deriving
/// keeps every one of those gameplay contracts byte-identical and drops only the art roots
/// belonging to other regions.
///
/// This is NON-DESTRUCTIVE: SakuraPass.unity is opened, pruned in memory, and then written to a
/// NEW path. The original file is never saved over, so the pre-migration state and the other
/// regions' scene remain exactly as they were (spec 4.1).
/// </summary>
public static class ShiosaiSceneBuilder
{
    public const string ShiosaiScenePath = "Assets/Scenes/SC_Persistent.unity";
    private const string SourceScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string ShiosaiRoot = "Shiosai Coast Environment";

    /// <summary>
    /// Roots dropped from the derived scene: art and crowd roots owned by other regions. Matched
    /// by EXACT name and every match is removed, because SakuraPass currently carries three
    /// duplicate "Sakura Pass Environment" roots.
    /// </summary>
    private static readonly string[] DropRoots =
    {
        "Sakura Pass Environment",
        "Azora Highlands Environment",
        "Maple City Environment",
        "Taka Mountains Environment",
        "Fuji Ridge Environment",
        "Azora NPCs",
        "Maple City NPCs",
        "Taka NPCs",
        "NPCs",
    };

    [MenuItem("MapleRide/Environment/Build Shiosai Coast Scene (SC_Persistent)", priority = 23)]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        Debug.Log($"[sc-scene] derived from '{SourceScenePath}' - {scene.rootCount} roots");

        int removed = 0, removedRenderers = 0;
        foreach (var name in DropRoots)
        {
            foreach (var go in scene.GetRootGameObjects().Where(g => g.name == name).ToList())
            {
                removedRenderers += go.GetComponentsInChildren<MeshRenderer>(true).Length
                                  + go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                Object.DestroyImmediate(go);
                removed++;
            }
        }
        Debug.Log($"[sc-scene] pruned {removed} foreign region root(s), {removedRenderers} renderers");

        if (!scene.GetRootGameObjects().Any(g => g.name == ShiosaiRoot))
            Debug.LogWarning($"[sc-scene] '{ShiosaiRoot}' not present yet - it will be generated below.");

        Directory.CreateDirectory(Path.GetDirectoryName(ShiosaiScenePath));
        if (!EditorSceneManager.SaveScene(scene, ShiosaiScenePath))
        {
            Debug.LogError($"[sc-scene] FAILED to save '{ShiosaiScenePath}'");
            return;
        }
        Debug.Log($"[sc-scene] saved '{ShiosaiScenePath}'");

        // From here on the coast builds into its own scene, never back into SakuraPass.
        ShiosaiCoastEnvironment.ScenePath = ShiosaiScenePath;
        ShiosaiCoastEnvironment.Apply();

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null || !regions.FastTravel(RegionCatalog.ShiosaiCoast))
            Debug.LogError("[sc-scene] coast scene built, but Shiosai could not be selected.");
        else
            Debug.Log("[sc-scene] RegionDirector fast-travelled to Shiosai Coast");

        Report();

        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[sc-scene] done -> '{active.path}'");
    }

    /// <summary>Read-only census of the derived scene, so the split is judged on what it holds.</summary>
    private static void Report()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var rows = new List<string>();
        int total = 0;
        foreach (var go in scene.GetRootGameObjects())
        {
            int r = go.GetComponentsInChildren<MeshRenderer>(true).Length
                  + go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            total += r;
            rows.Add($"[sc-scene]   {(go.activeSelf ? "ON " : "off")} {go.name,-40} renderers={r}");
        }
        foreach (var r in rows.OrderBy(x => x)) Debug.Log(r);

        var size = new FileInfo(Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? "", ShiosaiScenePath)).Length;
        Debug.Log($"[sc-scene] result: {scene.rootCount} roots, {total} renderers, " +
                  $"{size / (1024f * 1024f):0.0} MB on disk");
    }
}

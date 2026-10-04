using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Restores the SakuraPass scene's AUTHORING DEFAULT region visibility.
///
/// WHY THIS EXISTS
/// <see cref="RegionDirector"/> shows only the region currently being ridden, by calling
/// <c>SetActive</c> on each region's environment root. That is correct at runtime, but it
/// mutates the *scene*: if a pass (or a human) leaves play mode / a batchmode run while a
/// different region is current, the scene can be saved with "Sakura Pass Environment"
/// deactivated. Everything then still compiles, every self-test still passes, and every
/// edit-mode render - including <c>SakuraPassDiagnostics.Capture</c>, which is the environment
/// skill's verification vehicle - quietly photographs open water.
///
/// The authoring default is: Sakura Pass visible, every other region hidden. This pass pushes
/// that onto the serialized scene and is idempotent (exact-name match, all duplicates handled).
/// Run it after any batchmode work that entered play mode or fast travelled.
/// </summary>
public static class SakuraSceneDefaults
{
    public const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>Region whose environment is visible when the scene is opened for authoring.</summary>
    public const string DefaultRegionId = RegionCatalog.SakuraPass;

    [MenuItem("MapleRide/Scene/Restore Region Visibility Defaults")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int changed = Fix();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[scene-defaults] region visibility restored, {changed} root(s) changed.");
    }

    /// <summary>Applies the default to the already-open scene. Returns roots changed.</summary>
    public static int Fix()
    {
        int changed = 0;
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var region in RegionCatalog.Regions)
        {
            if (string.IsNullOrEmpty(region.EnvironmentRoot)) continue;
            bool show = region.Id == DefaultRegionId;

            foreach (var t in all)
            {
                if (t == null || t.parent != null) continue;          // roots only
                if (t.gameObject.name != region.EnvironmentRoot) continue;  // EXACT name
                if (t.gameObject.activeSelf == show) continue;
                Undo.RecordObject(t.gameObject, "region visibility default");
                t.gameObject.SetActive(show);
                EditorUtility.SetDirty(t.gameObject);
                changed++;
                Debug.Log($"[scene-defaults] '{t.gameObject.name}' -> activeSelf {show}");
            }
        }
        return changed;
    }
}

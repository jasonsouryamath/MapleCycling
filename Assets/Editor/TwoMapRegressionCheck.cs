using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TWO-MAP REGRESSION CHECK.
///
/// Sakura Pass and Shiosai Coast are two separate maps that share ONE scene
/// (Assets/Scenes/SakuraPass.unity); RegionDirector.ApplyEnvironmentVisibility draws only the
/// active region's root and hides every other. The coast overhaul therefore has to satisfy two
/// claims at once, and a render of the coast only proves the first:
///
///   1. the Shiosai Coast region shows the full overhaul, and
///   2. the Sakura Pass region is COMPLETELY UNCHANGED.
///
/// SakuraPassDiagnostics.Capture() cannot prove (2) on its own: it opens the scene but never
/// selects a region, so after a coast build it would render Sakura with Sakura's own root
/// switched OFF and report a broken map that is not broken. This harness fast-travels back to
/// Sakura Pass first, logs the active state of every region root as hard evidence, and then
/// runs the existing Sakura capture.
///
/// It deliberately DOES NOT SAVE. The scene on disk stays in the coast-visible state that
/// ShiosaiCoastEnvironment.BuildCoastPass left it in.
/// </summary>
public static class TwoMapRegressionCheck
{
    [MenuItem("MapleRide/Environment/Regression - Capture Sakura After Coast Build", priority = 32)]
    public static void CaptureSakura()
    {
        string scenePath = ShiosaiCoastEnvironment.ScenePath;
        if (EditorSceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null)
        {
            Debug.LogError("[two-map] no RegionDirector in the scene - cannot select a region.");
            return;
        }

        LogRootStates("before");
        if (!regions.FastTravel(RegionCatalog.SakuraPass))
        {
            Debug.LogError("[two-map] FastTravel to Sakura Pass FAILED.");
            return;
        }
        LogRootStates("after fast travel to Sakura Pass");

        // Same scene path, so Capture() will not re-open and discard the region selection.
        SakuraPassDiagnostics.Capture();

        Debug.Log("[two-map] Sakura capture complete. Scene NOT saved - disk keeps the " +
                  "coast-visible state from BuildCoastPass.");
    }

    private static void LogRootStates(string when)
    {
        foreach (var region in RegionCatalog.Regions)
        {
            if (string.IsNullOrEmpty(region.EnvironmentRoot)) continue;
            int n = 0;
            foreach (var go in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go == null || go.parent != null) continue;
                if (go.name != region.EnvironmentRoot) continue;
                n++;
                Debug.Log($"[two-map] {when}: root '{go.name}' active={go.gameObject.activeSelf} " +
                          $"children={go.childCount}");
            }
            if (n == 0)
                Debug.Log($"[two-map] {when}: root '{region.EnvironmentRoot}' NOT PRESENT.");
        }
    }
}

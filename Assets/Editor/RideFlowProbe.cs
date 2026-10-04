using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnostic for the title -> world map -> countdown flow bug: reports which region environment
/// roots exist in Sakura Pass, whether they are active, and whether they actually hold geometry.
/// Then runs the region director's own resolve path and reports again, so we can tell a
/// "stale saved state" from a "resolve never fixes it" failure.
/// </summary>
public static class RideFlowProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/Ride Flow Probe")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Report("BEFORE");

        var boot = Object.FindFirstObjectByType<RideBootstrap>(FindObjectsInactive.Include);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        var session = Object.FindFirstObjectByType<RideSession>(FindObjectsInactive.Include);
        Debug.Log($"[flow] boot={(boot != null)} regions={(regions != null)} session={(session != null)}");
        if (session != null)
        {
            session.EnsureCourse();
            var g = session.Graph;
            Debug.Log($"[flow] session.courseId={session.courseId} graph={(g != null)} " +
                      $"course={(session.Course != null ? session.Course.DisplayName : "NULL")} " +
                      $"regionOfCourse={(g != null ? g.RegionOfCourse(session.courseId) : "?")}");
        }
        if (regions != null)
        {
            Debug.Log($"[flow] regions.currentRegionId(serialized)={regions.currentRegionId}");
            regions.Resolve();
            regions.SyncFromSession();
            Debug.Log($"[flow] after SyncFromSession currentRegionId={regions.currentRegionId}");
        }

        Report("AFTER SyncFromSession");
        MapleRideSceneBootstrap.DiscardChanges();
    }

    static void Report(string tag)
    {
        foreach (var region in RegionCatalog.Regions)
        {
            if (string.IsNullOrEmpty(region.EnvironmentRoot)) continue;
            int found = 0;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                {
                    if (go.name != region.EnvironmentRoot) continue;
                    found++;
                    int rend = go.GetComponentsInChildren<Renderer>(true).Length;
                    int kids = go.transform.childCount;
                    Debug.Log($"[flow][{tag}] '{go.name}' #{found} activeSelf={go.activeSelf} " +
                              $"children={kids} renderers={rend}");
                }
            }
            if (found == 0)
                Debug.Log($"[flow][{tag}] '{region.EnvironmentRoot}' NOT PRESENT as a scene root");
        }
    }
}

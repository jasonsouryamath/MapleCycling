using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Milestone D regression check for the Shiosai scene split (spec 6.1).
///
/// Proves two things that a render alone cannot:
///   1. The dedicated Shiosai scene stands on its own - ride rig present, Shiosai root present
///      and shown, and RegionDirector can fast-travel to the coast.
///   2. The split did NOT break the other regions. SakuraPass.unity is opened untouched and
///      every region in the catalog that still has a root there is shown and hidden in turn.
///
/// Read-only with respect to both scenes: nothing is marked dirty and nothing is saved.
/// </summary>
public static class ShiosaiSceneSelfTest
{
    private static int _fail;

    public static void Run()
    {
        _fail = 0;
        CheckShiosaiScene();
        CheckOtherRegionsStillSwitch();
        Debug.Log(_fail == 0
            ? "[sc-selftest] SUMMARY fail=0 - scene split is clean"
            : $"[sc-selftest] SUMMARY fail={_fail}");
    }

    private static void Check(bool ok, string label, string detail)
    {
        if (!ok) _fail++;
        Debug.Log($"[sc-selftest] {(ok ? "PASS" : "FAIL")} {label} - {detail}");
    }

    private static void CheckShiosaiScene()
    {
        var scene = EditorSceneManager.OpenScene(ShiosaiSceneBuilder.ShiosaiScenePath,
                                                 OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();

        Check(roots.Any(r => r.name == "Shiosai Coast Environment"),
              "Shiosai root present in dedicated scene", ShiosaiSceneBuilder.ShiosaiScenePath);

        Check(!roots.Any(r => r.name == "Sakura Pass Environment"),
              "Sakura Pass art NOT dragged into the Shiosai scene",
              $"{roots.Length} roots total");

        var director = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        Check(director != null, "RegionDirector survived the split", director != null ? "found" : "missing");

        var follower = Object.FindFirstObjectByType<RouteFollower>(FindObjectsInactive.Include);
        Check(follower != null, "Rider RouteFollower survived the split",
              follower != null ? follower.gameObject.name : "missing");

        var cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        Check(cam != null, "Camera survived the split", cam != null ? cam.name : "missing");

        if (director != null)
        {
            director.Resolve();
            bool travelled = director.FastTravel(RegionCatalog.ShiosaiCoast);
            Check(travelled, "Fast travel to Shiosai Coast in its own scene",
                  travelled ? director.currentRegionId : "refused");

            var shiosai = roots.FirstOrDefault(r => r.name == "Shiosai Coast Environment");
            Check(shiosai != null && shiosai.activeSelf,
                  "Shiosai root is SHOWN after fast travel",
                  shiosai == null ? "absent" : (shiosai.activeSelf ? "active" : "still hidden"));
        }
    }

    private static void CheckOtherRegionsStillSwitch()
    {
        const string shared = "Assets/Scenes/SakuraPass.unity";
        var scene = EditorSceneManager.OpenScene(shared, OpenSceneMode.Single);
        var director = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (director == null) { Check(false, "RegionDirector in shared scene", "missing"); return; }
        director.Resolve();

        foreach (var region in RegionCatalog.Regions)
        {
            if (string.IsNullOrEmpty(region.EnvironmentRoot)) continue;
            var roots = scene.GetRootGameObjects()
                             .Where(r => r.name == region.EnvironmentRoot).ToList();
            if (roots.Count == 0) continue;   // region has moved to its own scene

            director.currentRegionId = region.Id;
            director.ApplyEnvironmentVisibility();
            bool shown = roots.All(r => r.activeSelf);

            director.currentRegionId = RegionCatalog.SakuraPass;
            director.ApplyEnvironmentVisibility();
            bool hidden = region.Id == RegionCatalog.SakuraPass || roots.All(r => !r.activeSelf);

            Check(shown && hidden, $"Region switching intact: {region.Id}",
                  $"{roots.Count} root(s), show={shown} hide={hidden}");
        }
    }
}

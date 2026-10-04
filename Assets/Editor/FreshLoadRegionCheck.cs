using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verifies the shared scene the way the GAME loads it, not the way an editor pass leaves it.
///
/// WHY THIS EXISTS: TwoMapRegressionCheck called FastTravel(SakuraPass) BEFORE it captured, so it
/// only ever validated a state a fresh load never reaches. It passed while the shipped scene was
/// saved with the coast active - the player rode "Sakura Circuit" through Shiosai's ocean plane.
/// A verification pass that first forces the state it wants to see verifies nothing.
///
/// So step 1 here opens the scene and asserts the SERIALIZED state with no fast travel at all.
/// Nothing in this file ever saves.
/// </summary>
public static class FreshLoadRegionCheck
{
    private const string SakuraRoot = "Sakura Pass Environment";
    private const string CoastRoot = "Shiosai Coast Environment";

    public static void Run()
    {
        var scenePath = ShiosaiCoastEnvironment.ScenePath;
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Debug.Log($"[freshload] opened '{scenePath}' (no FastTravel).");

        bool ok = Report("1. FRESH LOAD (as the game boots)", expectSakura: true);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null)
        {
            Debug.LogError("[freshload] FAIL: no RegionDirector in the scene.");
            return;
        }
        regions.Resolve();

        SakuraPassDiagnostics.Capture();
        Debug.Log("[freshload] captured Sakura at its saved default state.");

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        regions.Resolve();
        regions.FastTravel(RegionCatalog.ShiosaiCoast);
        ok &= Report("2. AFTER FastTravel(ShiosaiCoast)", expectSakura: false);

        regions.FastTravel(RegionCatalog.SakuraPass);
        ok &= Report("3. AFTER FastTravel back to SakuraPass", expectSakura: true);

        Debug.Log(ok
            ? "[freshload] RESULT: PASS - scene loads in the Sakura default state and both regions toggle cleanly."
            : "[freshload] RESULT: FAIL - see the region-root states above.");

        // Deliberately never saves: this pass must not be able to change what ships.
    }

    private static bool Report(string label, bool expectSakura)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects()
                         .Where(g => g.name.EndsWith(" Environment"))
                         .ToArray();

        Debug.Log($"[freshload] --- {label} ---");
        foreach (var g in roots)
            Debug.Log($"[freshload]   '{g.name}' active={g.activeSelf}");

        var rd = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        Debug.Log($"[freshload]   currentRegionId = {(rd != null ? rd.currentRegionId : "<none>")}");

        bool sakura = roots.Any(g => g.name == SakuraRoot && g.activeSelf);
        bool coast = roots.Any(g => g.name == CoastRoot && g.activeSelf);

        // The ocean is the thing that actually floods a Sakura ride, so check it by name rather
        // than trusting the root alone.
        var ocean = GameObject.Find("Shiosai Ocean");
        Debug.Log($"[freshload]   'Shiosai Ocean' reachable/active = {(ocean != null ? ocean.activeInHierarchy.ToString() : "not active")}");

        bool ok = expectSakura ? (sakura && !coast) : (coast && !sakura);
        Debug.Log(ok ? $"[freshload]   OK ({label})" : $"[freshload]   FAIL ({label})");
        return ok;
    }
}

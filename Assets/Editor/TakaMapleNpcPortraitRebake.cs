using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Re-bake hook for the Taka Mountains and Maple City roster portraits, mirroring
/// ShiosaiNpcTraffic.BakeCoastPortraits (see that file's header): NpcPortraitBake.BakeAll()
/// skips - and DELETES the stale portrait of - any rider that is not activeInHierarchy, and
/// both rosters are parked (their root inactive) in the shared SakuraPass scene while Sakura is
/// the active region. Neither roster had its own re-bake wrapper (only Shiosai did), which is
/// why their portraits were never refreshed after the HDRP material fix and stayed on their
/// pre-fix, blown-out-white bake (a flat overexposed face compresses far smaller than a real
/// textured one, which is why every corrupted portrait was also the smallest on disk).
///
/// Wakes "Taka NPCs" and "Maple City NPCs" (and any inactive child riders under them), runs one
/// NpcPortraitBake.BakeAll() pass, then restores every toggled object to its exact prior active
/// state and saves. Safe to run repeatedly; converges.
/// </summary>
public static class TakaMapleNpcPortraitRebake
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    static readonly string[] RosterRootNames = { "Taka NPCs", "Maple City NPCs" };

    [MenuItem("MapleRide/NPCs/Bake Taka + Maple Portraits")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var woken = new List<GameObject>();
        var roots = new List<GameObject>();

        foreach (var name in RosterRootNames)
        {
            var root = GameObject.Find(name);
            if (root == null) { Debug.LogWarning($"[taka-maple-portrait] root '{name}' not found - skipped."); continue; }
            roots.Add(root);

            if (!root.activeSelf) { root.SetActive(true); woken.Add(root); }

            // Defensive, same as Shiosai's wake step: wake any individually-inactive rider so a
            // per-child toggle doesn't also cause a silent delete.
            foreach (var g in root.GetComponentsInChildren<NpcGreeting>(true))
            {
                if (g.gameObject.activeSelf) continue;
                g.gameObject.SetActive(true);
                woken.Add(g.gameObject);
            }
        }

        if (roots.Count == 0)
        {
            Debug.LogError("[taka-maple-portrait] neither roster root was found - nothing to bake.");
            return;
        }

        Debug.Log($"[taka-maple-portrait] woke {woken.Count} object(s) across {roots.Count} roster root(s) for the bake.");

        try
        {
            NpcPortraitBake.BakeAll();
        }
        finally
        {
            // Reverse order: children first, then the roots, matching how they were woken.
            for (int i = woken.Count - 1; i >= 0; i--)
                if (woken[i] != null) woken[i].SetActive(false);

            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[taka-maple-portrait] re-parked {woken.Count} object(s) and saved '{active.path}'.");
        }
    }
}

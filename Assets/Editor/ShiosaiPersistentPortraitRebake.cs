using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Re-bake hook for the Shiosai Coast traffic portraits, now that Shiosai's environment and
/// traffic have been split out of the shared SakuraPass.unity into their own persistent scene
/// (see ShiosaiSceneBuilder / Assets/Scenes/SC_Persistent.unity). ShiosaiNpcTraffic's own
/// documented "Bake Shiosai Portraits" hook still hardcodes SakuraPass.unity and can no longer
/// find "Shiosai Coast Environment" there post-split, so it silently does nothing. This is the
/// same wake -> bake -> restore pattern, aimed at the scene that actually holds the coastal
/// riders today.
///
/// Wakes "Shiosai Coast Environment" > "Shiosai Traffic" and every parked pool rider under it,
/// spreads them along a line so no two overlap (see the loop below - every pool rider parks at
/// the exact same origin, and 30 overlapping skinned meshes is what produced the flat-white,
/// rainbow-striped portraits), forces one IK solve pass per rider (they are otherwise posed at
/// runtime only, which never ticks in batchmode), runs one NpcPortraitBake.BakeAll() pass (now
/// scene-aware - see NpcPortraitBake.BakeAll), then restores every toggled object to its exact
/// prior active state and position and saves SC_Persistent.unity. Safe to run repeatedly;
/// converges.
/// </summary>
public static class ShiosaiPersistentPortraitRebake
{
    const string ScenePath = "Assets/Scenes/SC_Persistent.unity";
    const string EnvironmentRootName = "Shiosai Coast Environment";
    const string TrafficRootName = "Shiosai Traffic";

    [MenuItem("MapleRide/NPCs/Bake Shiosai Portraits (SC_Persistent)")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var env = GameObject.Find(EnvironmentRootName);
        var traffic = env != null ? env.transform.Find(TrafficRootName) : GameObject.Find(TrafficRootName)?.transform;
        if (traffic == null)
        {
            Debug.LogError($"[shiosai-sc-portrait] no '{TrafficRootName}' found under '{EnvironmentRootName}' in {ScenePath}.");
            return;
        }

        bool envWasActive = env != null && env.activeSelf;
        if (env != null && !envWasActive) env.gameObject.SetActive(true);
        bool trafficWasActive = traffic.gameObject.activeSelf;
        if (!trafficWasActive) traffic.gameObject.SetActive(true);

        var woken = new List<GameObject>();
        var origPositions = new List<Vector3>();
        int posed = 0, idx = 0;
        foreach (Transform child in traffic)
        {
            var g = child.GetComponent<NpcGreeting>();
            if (g == null || string.IsNullOrEmpty(g.riderName)) continue;
            if (child.gameObject.activeSelf) continue;
            child.gameObject.SetActive(true);
            woken.Add(child.gameObject);
            origPositions.Add(child.position);

            // Second root cause, on top of the pose one below: every pool rider sits parked at
            // the exact same local origin (never moved, because ShiosaiTrafficDirector places
            // them at runtime, which never ticks in batchmode). Waking all 30 at once for one
            // NpcPortraitBake.BakeAll() pass means all 30 skinned meshes are stacked on top of
            // each other at the same point in space - so EVERY rider's portrait camera, no
            // matter whose head it is aimed at, renders all 29 other overlapping riders' hair
            // and faces interleaved into the same tiny frame. That is the flat-white-face,
            // rainbow-striped-hair look: it is bleed-through from the other 29 riders standing
            // inside this one. Spread each rider out along a line before baking so no two
            // overlap, then restore the original (parked) position afterwards.
            child.position = new Vector3(idx * 6f, 0f, 0f);
            idx++;

            // Root cause of the (separately, also real) unposed-bind-pose problem: this pool is
            // posed at RUNTIME by ShiosaiTrafficDirector (LateUpdate), which never ticks in a
            // headless -batchmode -quit run. Waking the GameObject alone leaves the rider
            // sitting in its raw imported bind pose - not seated on the saddle, limbs not IK'd
            // onto the pedals and hoods. Force one solve pass per rider before baking.
            var rig = child.GetComponentInChildren<KuroBikeRig>(true);
            if (rig != null) { rig.ForceSolveOnce(); posed++; }
            else Debug.LogWarning($"[shiosai-sc-portrait] '{child.name}': no KuroBikeRig found - baking unposed.");
        }
        Debug.Log($"[shiosai-sc-portrait] woke {woken.Count} riders for the portrait bake, posed {posed} of them, spread along a line to avoid overlap.");

        try { NpcPortraitBake.BakeAll(); }
        finally
        {
            for (int i = 0; i < woken.Count; i++)
                if (woken[i] != null) woken[i].transform.position = origPositions[i];
            foreach (var go in woken) if (go != null) go.SetActive(false);
            if (!trafficWasActive) traffic.gameObject.SetActive(false);
            if (env != null && !envWasActive) env.gameObject.SetActive(false);

            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[shiosai-sc-portrait] re-parked {woken.Count} riders and saved '{active.path}'.");
        }
    }
}

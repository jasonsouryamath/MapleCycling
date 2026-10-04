// NB5: extra ride-road cyclists. Clones the existing Kuro-based Nagisa riders
// after NagisaNpcRoster.Stage so rig scale, CelLit materials and bike fit stay
// identical to the validated donor pipeline. Rebuilds by exact root name.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NagisaMovingRiders
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string GroupName = "Nagisa Resort Cyclists";
    private const int Target = 48;

    [MenuItem("MapleRide/NPCs/Stage Nagisa Resort Cyclists")]
    public static void Stage()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var baseRoot = GameObject.Find(NagisaNpcRoster.ParentName);
        if (baseRoot == null)
        {
            Debug.LogError("[nagisa-riders] Stage NagisaNpcRoster.Stage first.");
            return;
        }
        var donors = baseRoot.GetComponentsInChildren<NPCCyclist>(true)
            .Where(c => c.transform.parent == baseRoot.transform &&
                        c.name.StartsWith(NagisaNpcRoster.NamePrefix) &&
                        !c.name.EndsWith("Kaimana"))
            .OrderBy(c => c.name).ToArray();
        if (donors.Length < 8)
        {
            Debug.LogError($"[nagisa-riders] Only {donors.Length} Kuro donors; stage the base roster first.");
            return;
        }

        var old = GameObject.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old);
        var group = new GameObject(GroupName);
        group.transform.SetParent(baseRoot.transform, false);
        int needed = Mathf.Max(0, Target - donors.Length - 1); // +1 is Kaimana
        var distances = new List<float>();
        for (float d = 220f; d < 4630f; d += 175f) distances.Add(d);
        for (float d = 12240f; d < 14900f; d += 175f) distances.Add(d);
        int count = 0;
        for (int k = 0; k < distances.Count && count < needed; k++)
        {
            bool reverse = (k % 2 == 0);
            var pool = donors.Where(c => c.reverse == reverse).ToArray();
            if (pool.Length == 0) continue;
            var source = pool[(k / 2) % pool.Length];
            var clone = Object.Instantiate(source.gameObject, group.transform);
            clone.name = $"Nagisa Resort Cyclist {count + 1:D2}";
            var cyclist = clone.GetComponent<NPCCyclist>();
            if (cyclist == null || cyclist.route == null || cyclist.route.Length < 2)
            {
                Object.DestroyImmediate(clone);
                continue;
            }
            int index = Mathf.RoundToInt(distances[k] / NPCCyclist.Spacing);
            cyclist.progress = Mathf.Clamp(reverse ? cyclist.route.Length - 1 - index : index,
                                           0, cyclist.route.Length - 1);
            cyclist.speed = NagisaNpcRoster.StagedSpeed(3.8f + (k % 5) * 0.32f);
            cyclist.ApplyPose();
            var greeting = clone.GetComponent<NpcGreeting>();
            if (greeting != null) greeting.enabled = false; // named donor cards belong to original riders
            if (clone.GetComponent<NagisaRiderLod>() == null) clone.AddComponent<NagisaRiderLod>();
            count++;
        }
        var all = baseRoot.GetComponentsInChildren<NPCCyclist>(true);
        foreach (var cyclist in all)
            NagisaBikeShadow.Attach(cyclist.gameObject);
        var avoid = baseRoot.GetComponent<TrafficAvoidance>();
        if (avoid == null) avoid = baseRoot.AddComponent<TrafficAvoidance>();
        avoid.riders = all;
        EditorUtility.SetDirty(avoid);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[nagisa-riders] {count} additional Kuro cyclists, {all.Length} total; " +
                  "both directions on the ride road; traffic avoidance refreshed.");
    }
}

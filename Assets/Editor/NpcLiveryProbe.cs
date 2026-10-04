using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reports what each staged rider's BICYCLE actually references in the saved scene.
///
/// The generated .mat assets can all be correct and distinct while the scene still resolves to
/// something else entirely - a null slot, a material whose shader failed to load (which Unity
/// draws as magenta, read by a player as "purple"), or one rider's clone shared onto everyone.
/// So this reads the scene, not the asset folder.
/// </summary>
public static class NpcLiveryProbe
{
    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        SakuraSceneDefaults.Fix();

        var seen = new Dictionary<Material, string>();
        foreach (var cyc in Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None))
        {
            string rider = cyc.transform.name;
            var bike = FindChild(cyc.transform, "Bike");
            if (bike == null) { Debug.Log($"[livery] {rider}: NO 'Bike' child"); continue; }

            var mats = bike.GetComponentsInChildren<Renderer>(true)
                           .SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            foreach (var m in mats)
            {
                if (m == null) { Debug.Log($"[livery] {rider}: NULL material slot"); continue; }
                bool badShader = m.shader == null || m.shader.name == "Hidden/InternalErrorShader";
                string col = "-";
                foreach (var p in ColorProps)
                    if (m.HasProperty(p)) { var c = m.GetColor(p); col = $"{c.r:F3},{c.g:F3},{c.b:F3}"; break; }
                string owner;
                string dup = seen.TryGetValue(m, out owner) && owner != rider ? $"  SHARED-WITH:{owner}" : "";
                if (!seen.ContainsKey(m)) seen[m] = rider;
                Debug.Log($"[livery] {rider,-26} {m.name,-34} shader={(m.shader == null ? "NULL" : m.shader.name),-34} " +
                          $"rgb=({col}){(badShader ? "  *** ERROR SHADER (renders magenta) ***" : "")}{dup}");
            }
        }

        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Same question, asked of the Shiosai traffic, which is staged under a different entry
    /// point and is SetActive(false) in the saved scene. Reports every BikeLod_* renderer's
    /// material slots: a null slot here IS the purple-bike defect, because Unity draws a null
    /// material magenta.
    /// </summary>
    public static void RunShiosai()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);

        var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                                                        FindObjectsSortMode.None)
                          .Where(t => t.name.StartsWith("Shiosai Rider "))
                          .OrderBy(t => t.name).ToArray();

        int badRiders = 0, badSlots = 0;
        foreach (var r in roots)
        {
            var lods = r.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(x => x.gameObject.name.StartsWith("BikeLod_")).ToArray();
            int nulls = 0;
            var names = new List<string>();
            foreach (var mr in lods)
                foreach (var m in mr.sharedMaterials)
                {
                    if (m == null) { nulls++; continue; }
                    if (!names.Contains(m.name)) names.Add(m.name);
                }
            if (nulls > 0) { badRiders++; badSlots += nulls; }
            Debug.Log($"[shiosai-livery] {r.name,-30} lodRenderers={lods.Length,-3} " +
                      $"NULLSLOTS={nulls,-3} mats=[{string.Join(", ", names)}]");
        }
        Debug.Log($"[shiosai-livery] SUMMARY riders={roots.Length} ridersWithNullSlots={badRiders} " +
                  $"nullSlots={badSlots}");

        MapleRideSceneBootstrap.DiscardChanges();
        EditorApplication.Exit(0);
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var hit = FindChild(c, name);
            if (hit != null) return hit;
        }
        return null;
    }
}

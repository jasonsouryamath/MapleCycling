using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// DIAGNOSTIC ONLY. Lists the renderers and material names of the buildings nearest the
/// diag_shiosai_ch4_village camera, so a render that still looks warm can be traced to the
/// material that is actually on the mesh instead of to the one the code believes it assigned.
/// </summary>
public static class ShiosaiVillageProbe
{
    public static void Run()
    {
        var eye = new Vector3(4091.86f, 12.67f, -9626.14f);
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);

        var all = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                                                         FindObjectsSortMode.None);
        var near = all.Where(r => Vector3.Distance(r.bounds.center, eye) < 350f
                                  && !r.name.Contains("Flower") && !r.name.Contains("Hydrangea")
                                  && !r.name.Contains("Pine") && !r.name.Contains("Broadleaf")
                                  && !r.name.Contains("Grass") && !r.name.Contains("Guardrail"))
                      .OrderBy(r => Vector3.Distance(r.bounds.center, eye))
                      .Take(22);
        foreach (var r in near)
        {
            string path = r.name;
            for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            Debug.Log($"[probe] {Vector3.Distance(r.bounds.center, eye):0.0} m  {path}  mats=[" +
                      string.Join(", ", r.sharedMaterials.Select(m => m == null ? "null" : m.name)) + "]");
        }
        Debug.Log("[probe] done.");
    }
}

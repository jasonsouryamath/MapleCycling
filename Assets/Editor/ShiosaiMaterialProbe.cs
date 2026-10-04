using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only probe that reports what the Shiosai hero surfaces are actually rendering with
/// after the HDRP conversion: shader, key textures and bounds. Written because the converted
/// carriageway came back covered in a leaf texture, and the difference between "wrong shader",
/// "wrong texture" and "road not drawn, terrain showing through" is not guessable from a
/// screenshot.
///
/// Opens the scene read-only and never saves it (the coast capture tool already carries a
/// routed defect for writing the scene as a side effect).
/// </summary>
public static class ShiosaiMaterialProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    private static readonly string[] Interesting =
    {
        "Carriageway", "Marking", "Shoulder", "Coast", "Shiosai", "Ocean", "Shallows", "Harbour",
        "House", "Village", "Building", "Roof", "Wall", "Machiya", "Terrain", "Ground"
    };

    [MenuItem("MapleRide/Environment/Probe Shiosai Materials")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int reported = 0;
        var seenMaterials = new System.Collections.Generic.HashSet<string>();

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = mr.gameObject.name;
                if (!Interesting.Any(k => n.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;

                var mat = mr.sharedMaterial;
                if (mat == null) continue;

                // One line per distinct material, not per renderer: the coast carries tens of
                // thousands of instances and the previous run hit its cap long before it
                // reached the carriageway, which is the surface actually under investigation.
                // Dedupe by material alone: the coast has thousands of terrain tiles that all
                // share one material, and keying on the object name let them flood the cap
                // before the probe ever reached the carriageway.
                if (!seenMaterials.Add(mat.name)) continue;
                if (reported++ > 60) break;

                var sh = mat.shader;
                string props = "";
                if (sh != null)
                {
                    for (int i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                        string pn = sh.GetPropertyName(i);
                        var t = mat.GetTexture(pn);
                        if (t != null) props += $" {pn}={t.name}";
                    }
                }

                string tint = mat.HasProperty("_Color") ? mat.GetColor("_Color").ToString()
                            : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "-";

                Debug.Log($"[sc-probe] '{n}' mat='{mat.name}' shader='{(sh != null ? sh.name : "<null>")}' " +
                          $"tint={tint} y={mr.bounds.center.y:0.0} tex:{(props == "" ? " <none>" : props)}");
            }
        }

        Debug.Log($"[sc-probe] reported {reported} distinct material/object pairs");
    }
}

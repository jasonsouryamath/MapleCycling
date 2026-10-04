using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// FIX-IMPLEMENTER probe: logs the actual mesh currently bound to the SakuraPass player, so we can
/// tell which candidate GLB (Rebound vs P7) is really in the scene after a swap.
public static class FiKuroMeshProbe
{
    public static void Run()
    {
        const string scene = "Assets/Scenes/SakuraPass.unity";
        if (EditorSceneManager.GetActiveScene().path != scene)
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[meshprobe] player not found"); return; }

        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = smr.sharedMesh;
            Debug.Log($"[meshprobe] renderer='{smr.name}' mesh='{(m ? m.name : "null")}' " +
                      $"verts={(m ? m.vertexCount : -1)} tris={(m ? m.triangles.Length / 3 : -1)} " +
                      $"bones={smr.bones.Length} mat='{(smr.sharedMaterial ? smr.sharedMaterial.name : "null")}' " +
                      $"shader='{(smr.sharedMaterial ? smr.sharedMaterial.shader.name : "null")}'");
        }
    }
}

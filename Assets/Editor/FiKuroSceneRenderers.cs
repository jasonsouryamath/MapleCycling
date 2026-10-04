using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// FIX-IMPLEMENTER probe: lists EVERY SkinnedMeshRenderer / MeshRenderer in SakuraPass with its
/// full hierarchy path and world position, to catch orphan/leftover rider meshes that are not
/// parented under the player rig (and thus survive KuroPlayerModelSwap.Apply).
public static class FiKuroSceneRenderers
{
    public static void Run()
    {
        const string scene = "Assets/Scenes/SakuraPass.unity";
        if (EditorSceneManager.GetActiveScene().path != scene)
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var m = smr.sharedMesh;
            Debug.Log($"[screnders] SMR path='{Path(smr.transform)}' active={smr.gameObject.activeInHierarchy} " +
                      $"mesh='{(m ? m.name : "null")}' verts={(m ? m.vertexCount : -1)} " +
                      $"pos={smr.transform.position} mat='{(smr.sharedMaterial ? smr.sharedMaterial.name : "null")}'");
        }
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Skip the bike parts to keep output focused on rider-ish meshes.
            string p = Path(mr.transform);
            if (p.Contains("/Bike/") || p.Contains("Bike/BikeModel")) continue;
            var mf = mr.GetComponent<MeshFilter>();
            var m = mf ? mf.sharedMesh : null;
            Debug.Log($"[screnders] MR  path='{p}' active={mr.gameObject.activeInHierarchy} " +
                      $"mesh='{(m ? m.name : "null")}' verts={(m ? m.vertexCount : -1)} pos={mr.transform.position}");
        }
    }

    static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

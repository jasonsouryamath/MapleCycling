using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dumps every skinned rider in SakuraPass: owning root, mesh, vert count, world scale.</summary>
public static class FiSceneRiderInspect
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var smrs = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        // group by mesh name
        var byMesh = new Dictionary<string, int>();
        var sb = new StringBuilder();
        foreach (var smr in smrs.OrderBy(s => FullPath(s.transform)))
        {
            string mesh = smr.sharedMesh != null ? smr.sharedMesh.name : "NULL";
            int vc = smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0;
            byMesh[mesh] = byMesh.TryGetValue(mesh, out var c) ? c + 1 : 1;
            Vector3 ls = smr.transform.lossyScale;
            // find owning rider root: nearest ancestor with ShiosaiRiderLod or NPCCyclist or named player
            string owner = OwnerLabel(smr.transform);
            sb.AppendLine($"[fi-inspect] {owner} | mesh='{mesh}' v={vc} | lossyScale=({ls.x:0.00},{ls.y:0.00},{ls.z:0.00}) active={smr.gameObject.activeInHierarchy} path={FullPath(smr.transform)}");
        }
        Debug.Log(sb.ToString());
        Debug.Log("[fi-inspect] MESH HISTOGRAM: " + string.Join("  ", byMesh.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));

        // Traffic roots
        foreach (var lod in Object.FindObjectsByType<ShiosaiRiderLod>(FindObjectsInactive.Include, FindObjectsSortMode.None).Take(6))
        {
            var body = lod.body;
            string meshes = body != null ? string.Join(",", body.Where(b => b != null).Select(b => b.sharedMesh != null ? b.sharedMesh.name : "null")) : "none";
            Debug.Log($"[fi-inspect] RiderLod '{lod.name}' scale={lod.transform.lossyScale.x:0.000} bodyMeshes={meshes}");
        }

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static string OwnerLabel(Transform t)
    {
        for (var c = t; c != null; c = c.parent)
        {
            if (c.GetComponent<ShiosaiRiderLod>() != null) return "TRAFFIC:" + c.name;
            if (c.GetComponent<NPCCyclist>() != null) return "NPC:" + c.name;
            if (c.name == "Kuro on Sakura Pass") return "PLAYER";
        }
        return "?:" + t.root.name;
    }

    static string FullPath(Transform t)
    {
        var s = t.name;
        for (var c = t.parent; c != null; c = c.parent) s = c.name + "/" + s;
        return s;
    }
}

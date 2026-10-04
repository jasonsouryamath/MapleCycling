using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// THROWAWAY QA probe for the "white shader on the road at the start of Minato": casts rays
/// straight DOWN onto the carriageway (centreline and both lanes) over the first 600 m, with
/// temporary mesh colliders on every visible renderer, and lists anything that is hit ABOVE or
/// instead of the road surface. Nothing is saved.
/// </summary>
public static class QaMinatoRoadOverlayProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/QA/Probe Minato Road Overlay")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionCatalog.MinatoCoast;
            regions.ApplyEnvironmentVisibility();
        }
        var temp = new List<GameObject>();
        var owner = new Dictionary<Collider, Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled) continue;
            Mesh mesh = r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh
                      : r is SkinnedMeshRenderer smr ? smr.sharedMesh : null;
            if (mesh == null) continue;
            var go = new GameObject("~probeCol");
            go.transform.SetParent(r.transform, false);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            temp.Add(go);
            owner[mc] = r;
        }
        Physics.SyncTransforms();

        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        var hits = new Dictionary<string, (int n, float d0, float d1, string mat)>();
        for (float d = 0f; d <= 600f; d += 2f)
        {
            int i = route.IndexAt(d);
            var side = route.SideFlat(i);
            foreach (var lat in new[] { -3.5f, -2f, -0.8f, 0f, 0.8f, 2f, 3.5f })
            {
                var top = route.Position[i] + side * lat + Vector3.up * 6f;
                if (!Physics.Raycast(top, Vector3.down, out var h, 12f)) continue;
                if (!owner.TryGetValue(h.collider, out var r)) continue;
                string path = r.name;
                for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                if (path.Contains("/Road/Road_")) continue;           // the carriageway itself
                hits.TryGetValue(path, out var e);
                hits[path] = (e.n + 1, e.n == 0 ? d : Mathf.Min(e.d0, d), Mathf.Max(e.d1, d),
                              r.sharedMaterial ? r.sharedMaterial.name : "-");
            }
        }
        var sb = new StringBuilder("=== MINATO ROAD OVERLAY (things above/instead of the road, 0-600 m) ===\n");
        foreach (var kv in hits.OrderByDescending(k => k.Value.n).Take(20))
            sb.AppendLine($"{kv.Value.n,4} hits  route {kv.Value.d0:0}-{kv.Value.d1:0} m  mat '{kv.Value.mat}'  {kv.Key}");
        Debug.Log(sb.ToString());
        foreach (var g in temp) if (g != null) Object.DestroyImmediate(g);
        MapleRideSceneBootstrap.DiscardChanges();
    }
}

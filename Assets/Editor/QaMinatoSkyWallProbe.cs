using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// THROWAWAY QA probe for the "grey striped wall filling the sky" reported at Minato KM ~0.2:
/// adds TEMPORARY mesh colliders to every renderer the player would see in Minato, casts a fan
/// of rays through the SKY half of a chase-camera view at several route distances, and logs which
/// objects are hit (full hierarchy path, bounds, material, hit distance). Nothing is saved.
/// </summary>
public static class QaMinatoSkyWallProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/QA/Probe Minato Sky Wall")]
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

        var temp = new List<Collider>();
        var owner = new Dictionary<Collider, Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled) continue;
            Mesh mesh = null;
            if (r is MeshRenderer) mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var go = new GameObject("~probeCol");
            go.transform.SetParent(r.transform, false);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            temp.Add(mc);
            owner[mc] = r;
        }
        Physics.SyncTransforms();

        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        var hits = new Dictionary<Renderer, (int n, float minD, float maxD)>();
        foreach (var d in new[] { 60f, 120f, 180f, 210f, 260f, 320f, 400f })
        {
            int i = route.IndexAt(d);
            var fwd = route.Tangent[i].normalized;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var eye = route.Position[i] - fwd * 3.2f + Vector3.up * 1.8f;
            for (float yaw = -45f; yaw <= 45f; yaw += 5f)
                for (float pitch = 2f; pitch <= 30f; pitch += 4f)
                {
                    var dir = Quaternion.AngleAxis(yaw, Vector3.up) *
                              Quaternion.AngleAxis(-pitch, right) * fwd;
                    if (!Physics.Raycast(eye, dir, out var h, 3000f)) continue;
                    if (!owner.TryGetValue(h.collider, out var r)) continue;
                    hits.TryGetValue(r, out var e);
                    hits[r] = (e.n + 1, e.n == 0 ? h.distance : Mathf.Min(e.minD, h.distance),
                               Mathf.Max(e.maxD, h.distance));
                }
        }

        var sb = new StringBuilder("=== MINATO SKY-HALF RAY HITS (most-hit first) ===\n");
        foreach (var kv in hits.OrderByDescending(k => k.Value.n).Take(25))
        {
            var r = kv.Key;
            string path = r.name;
            for (var t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            sb.AppendLine($"{kv.Value.n,4} hits  d {kv.Value.minD:0}-{kv.Value.maxD:0} m  size {r.bounds.size}  " +
                          $"mat '{r.sharedMaterial?.name}' shader '{r.sharedMaterial?.shader.name}'  {path}");
        }
        Debug.Log(sb.ToString());

        foreach (var c in temp) if (c != null) Object.DestroyImmediate(c.gameObject);
        MapleRideSceneBootstrap.DiscardChanges();
    }
}

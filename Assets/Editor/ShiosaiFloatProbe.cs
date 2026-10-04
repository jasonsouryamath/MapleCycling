using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// DIAGNOSTIC ONLY (defect sweep: "floating canopy planks").
///
/// Reconstructs the exact diag_shiosai_tunnel_reveal camera and ray-picks a list of pixels
/// through the scene's renderer bounds, so the floating dark bars in the upper-left quadrant
/// can be named instead of guessed at. Uses the documented ray-vs-bounds technique (compact
/// renderers only) because swept/tiled meshes have AABBs that swallow every ray.
/// </summary>
public static class ShiosaiFloatProbe
{
    [MenuItem("MapleRide/Environment/Probe Shiosai Floating Planks", priority = 99)]
    public static void Run()
    {
        string scenePath = ShiosaiCoastEnvironment.ScenePath;
        if (EditorSceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
        }

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        const float at = 4812f, fwd = 900f, fov = 50f;
        int i = Mathf.Clamp(route.IndexAt(at), 0, route.Count - 1);
        int ahead = Mathf.Clamp(route.IndexAt(at + fwd), 0, route.Count - 1);
        var eye = route.Position[i] + Vector3.up * 2.6f - route.Tangent[i] * 7f;
        var look = route.Position[ahead] + Vector3.up * 1.0f;

        var go = new GameObject("~ShiosaiProbeCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = eye;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = Mathf.Max(14000f, Vector3.Distance(eye, look) * 3f);
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;   // so pixelRect/aspect match the capture exactly

        Debug.Log($"[float] eye {eye} look {look} fov {fov}");

        // Renderers, cached once.
        var rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude,
                                                           FindObjectsSortMode.None)
                          .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                          .ToArray();
        Debug.Log($"[float] {rends.Length} active mesh renderers");

        // Pixels read off diag_shiosai_tunnel_reveal.png (PNG top-left origin).
        var px = new (int x, int y, string tag)[]
        {
            (130, 333, "barA"), (200, 335, "barA2"), (22, 375, "barB"),
            (425, 378, "barC"), (590, 330, "barD"), (380, 483, "barE"),
            (455, 437, "barF"), (760, 380, "barG"), (1020, 432, "barH"),
        };

        foreach (var (x, y, tag) in px)
        {
            // Unity screen space is bottom-left origin.
            var ray = cam.ScreenPointToRay(new Vector3(x + 0.5f, H - y - 0.5f, 0f));
            // Pass 1: every renderer whose AABB the ray crosses (NO size filter - the bars turned
            // out not to be compact props). Pass 2: exact ray-triangle test on those candidates,
            // which is what actually names the surface the pixel shows.
            var hits = new List<(float d, MeshRenderer r, int sub)>();
            foreach (var r in rends)
            {
                if (!r.bounds.IntersectRay(ray)) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                var l2w = r.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = mesh.GetTriangles(s);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        var a = l2w.MultiplyPoint3x4(verts[tris[t]]);
                        var b2 = l2w.MultiplyPoint3x4(verts[tris[t + 1]]);
                        var c = l2w.MultiplyPoint3x4(verts[tris[t + 2]]);
                        if (RayTri(ray, a, b2, c, out float dd) && dd > 0.5f)
                        { hits.Add((dd, r, s)); t = tris.Length; s = mesh.subMeshCount; }
                    }
                }
            }
            hits.Sort((p, q) => p.d.CompareTo(q.d));
            if (hits.Count == 0) { Debug.Log($"[float] {tag} ({x},{y}): NO triangle on ray"); continue; }
            var sb = new System.Text.StringBuilder($"[float] {tag} ({x},{y}):");
            foreach (var (d, r, sub) in hits.Take(4))
            {
                var mats = r.sharedMaterials;
                var m = sub < mats.Length ? mats[sub] : r.sharedMaterial;
                sb.Append($"\n    {d,8:0.0} m  '{Path(r.transform)}'  mesh='{MeshName(r)}'" +
                          $"  sub={sub}/{mats.Length} mat='{(m ? m.name : "none")}'" +
                          $"  bounds={r.bounds.size} centreY={r.bounds.center.y:0.0}" +
                          $"  scale={r.transform.lossyScale.x:0.00}");
            }
            Debug.Log(sb.ToString());
        }

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log("[float] probe done");
    }

    private static bool RayTri(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float dist)
    {
        // Moller-Trumbore, two-sided (a lot of this scene's foliage is single-sided but we want
        // the hit regardless of which way the triangle faces).
        dist = 0f;
        var e1 = b - a; var e2 = c - a;
        var p = Vector3.Cross(ray.direction, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-8f) return false;
        float inv = 1f / det;
        var tv = ray.origin - a;
        float u = Vector3.Dot(tv, p) * inv;
        if (u < 0f || u > 1f) return false;
        var q = Vector3.Cross(tv, e1);
        float v = Vector3.Dot(ray.direction, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        dist = Vector3.Dot(e2, q) * inv;
        return dist > 0f;
    }

    private static string MeshName(MeshRenderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
    }

    private static string Path(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use. Exact mesh pick through a pixel.</summary>
public static class SakuraEnvProbe9
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        int i = Mathf.Clamp(route.IndexAtClimbFraction(0.125f), 0, route.Count - 1);
        int ahead = Mathf.Min(route.Count - 1, i + Mathf.Max(1, route.Count / 40));
        var eye = route.Position[i] + Vector3.up * 2.4f - route.Tangent[i] * 6f - route.Side[i] * 1.2f;
        var look = route.Position[ahead] + Vector3.up * 1.4f;

        var go = new GameObject("~ProbeCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = eye;
        cam.transform.LookAt(look);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 9000f;
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;

        var pixels = new[] { new Vector2(700f, 370f), new Vector2(745f, 380f), new Vector2(820f, 360f) };
        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .Where(r => r.enabled && r is MeshRenderer && r.GetComponent<MeshFilter>() != null
                                    && r.GetComponent<MeshFilter>().sharedMesh != null).ToArray();

        bool prevBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;

        foreach (var p in pixels)
        {
            var ray = cam.ScreenPointToRay(p);
            var candidates = all.Where(r => r.bounds.IntersectRay(ray)).ToList();
            Debug.Log($"[probe9] pixel {p}: {candidates.Count} bounds candidates");

            var temp = new List<MeshCollider>();
            foreach (var r in candidates)
            {
                if (r.GetComponent<Collider>() != null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                temp.Add(mc);
            }
            Physics.SyncTransforms();

            var hits = Physics.RaycastAll(ray, 4000f).OrderBy(h => h.distance).Take(6).ToArray();
            foreach (var h in hits)
            {
                var rr = h.collider.GetComponent<Renderer>();
                Debug.Log($"[probe9]   {h.distance:0.0} m  {Path(h.collider.transform)}  " +
                          $"mat={(rr != null && rr.sharedMaterial != null ? rr.sharedMaterial.name : "?")} " +
                          $"shader={(rr != null && rr.sharedMaterial != null ? rr.sharedMaterial.shader.name : "?")} " +
                          $"point={h.point}");
            }
            foreach (var mc in temp) Object.DestroyImmediate(mc);
        }

        Physics.queriesHitBackfaces = prevBackfaces;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log("[probe9] done");
    }

    private static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

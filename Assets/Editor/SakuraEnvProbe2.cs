using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use.</summary>
public static class SakuraEnvProbe2
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        var jobs = new (string name, float t, Vector2[] px)[]
        {
            ("gate", 0.125f, new[] { new Vector2(745f, 380f), new Vector2(700f, 400f), new Vector2(800f, 370f) }),
        };

        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Debug.Log($"[probe2] {all.Length} renderers in scene");

        foreach (var job in jobs)
        {
            int i = Mathf.Clamp(route.IndexAtClimbFraction(job.t), 0, route.Count - 1);
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

            foreach (var p in job.px)
            {
                var ray = cam.ScreenPointToRay(p);
                var hits = new List<(float d, Renderer r)>();
                foreach (var r in all)
                {
                    if (!r.enabled) continue;
                    if (r.bounds.size.magnitude > 60f) continue;
                    if (r.bounds.IntersectRay(ray, out float dist)) hits.Add((dist, r));
                }
                Debug.Log($"[probe2] --- {job.name} pixel {p} ---");
                foreach (var h in hits.OrderBy(x => x.d).Take(8))
                    Debug.Log($"[probe2]   {h.d:0.0} m  {Path(h.r.transform)}  type={h.r.GetType().Name} " +
                              $"mat={(h.r.sharedMaterial != null ? h.r.sharedMaterial.name : "<null>")} " +
                              $"shader={(h.r.sharedMaterial != null ? h.r.sharedMaterial.shader.name : "?")} " +
                              $"size={h.r.bounds.size}");
            }

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
        Debug.Log("[probe2] done");
    }

    private static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use.</summary>
public static class SakuraEnvProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var m = r != null ? r.sharedMaterial : null;
            string tex = m != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null
                ? m.GetTexture("_MainTex").name : "<none>";
            Debug.Log($"[probe] PS '{Path(ps.transform)}' active={ps.gameObject.activeInHierarchy} " +
                      $"mode={(r != null ? r.renderMode.ToString() : "?")} mat={(m != null ? m.name : "<null>")} " +
                      $"shader={(m != null ? m.shader.name : "?")} tex={tex} " +
                      $"src={(m != null && m.HasProperty("_SrcBlend") ? m.GetFloat("_SrcBlend") : -1)} " +
                      $"dst={(m != null && m.HasProperty("_DstBlend") ? m.GetFloat("_DstBlend") : -1)} " +
                      $"zw={(m != null && m.HasProperty("_ZWrite") ? m.GetFloat("_ZWrite") : -1)} " +
                      $"queue={(m != null ? m.renderQueue : -1)} kw=[{(m != null ? string.Join(",", m.shaderKeywords) : "")}]");
        }

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        int i = Mathf.Clamp(route.IndexAtClimbFraction(0.60f), 0, route.Count - 1);
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

        var pixels = new[] { new Vector2(1330f, 820f), new Vector2(688f, 642f), new Vector2(966f, 594f) };
        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var p in pixels)
        {
            var ray = cam.ScreenPointToRay(p);
            var hits = all
                .Where(r => r.enabled && r.bounds.size.magnitude <= 18f)
                .Select(r => new { r, d = r.bounds.IntersectRay(ray, out float dist) ? dist : -1f })
                .Where(x => x.d >= 0f)
                .OrderBy(x => x.d)
                .Take(6);
            Debug.Log($"[probe] --- pixel {p} ---");
            foreach (var h in hits)
                Debug.Log($"[probe]   {h.d:0.0} m  {Path(h.r.transform)}  mat={(h.r.sharedMaterial != null ? h.r.sharedMaterial.name : "<null>")} " +
                          $"shader={(h.r.sharedMaterial != null ? h.r.sharedMaterial.shader.name : "?")} size={h.r.bounds.size}");
        }

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log("[probe] done");
    }

    private static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

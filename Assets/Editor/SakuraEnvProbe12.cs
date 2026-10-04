using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use.</summary>
public static class SakuraEnvProbe12
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.RenderDir("probe");
        var route = SakuraPassEnvironment.SakuraRoute.Load();

        // 1. What mesh is actually at the washed pixels?
        var cam = MakeCam(route);
        var all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(r => r.enabled && r is MeshRenderer && r.GetComponent<MeshFilter>() != null
                        && r.GetComponent<MeshFilter>().sharedMesh != null).ToArray();
        bool prev = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        foreach (var p in new[] { new Vector2(700f, 370f), new Vector2(745f, 380f), new Vector2(820f, 360f) })
        {
            var ray = cam.ScreenPointToRay(p);
            var candidates = all.Where(r => r.bounds.IntersectRay(ray)).ToList();
            var temp = new List<MeshCollider>();
            foreach (var r in candidates)
            {
                if (r.GetComponent<Collider>() != null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                temp.Add(mc);
            }
            Physics.SyncTransforms();
            Debug.Log($"[probe12] pixel {p}: {candidates.Count} candidates");
            foreach (var h in Physics.RaycastAll(ray, 4000f).OrderBy(x => x.distance).Take(4))
            {
                var rr = h.collider.GetComponent<Renderer>();
                Debug.Log($"[probe12]   {h.distance:0.0} m {HierarchyPath(h.collider.transform)} " +
                          $"mat={(rr != null && rr.sharedMaterial != null ? rr.sharedMaterial.name : "?")} " +
                          $"rendererBounds={(rr != null ? rr.bounds.ToString() : "?")}");
            }
            foreach (var mc in temp) Object.DestroyImmediate(mc);
        }
        Physics.queriesHitBackfaces = prev;
        Object.DestroyImmediate(cam.gameObject);

        // 2. Does forcing the decorative point lights to per-pixel remove the wash?
        var lamps = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(l => l.type == LightType.Point).ToArray();
        Debug.Log($"[probe12] {lamps.Length} point lights, {lamps.Count(l => l.renderMode == LightRenderMode.ForceVertex)} ForceVertex");
        var before = lamps.Select(l => l.renderMode).ToArray();
        foreach (var l in lamps) l.renderMode = LightRenderMode.ForcePixel;
        Shoot(dir, "probe12_forcepixel", route);
        for (int i = 0; i < lamps.Length; i++) lamps[i].renderMode = before[i];
        Debug.Log("[probe12] done");
    }

    private static Camera MakeCam(SakuraPassEnvironment.SakuraRoute route)
    {
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
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        return cam;
    }

    private static void Shoot(string dir, string name, SakuraPassEnvironment.SakuraRoute route)
    {
        var cam = MakeCam(route);
        cam.gameObject.AddComponent<SakuraPostFX>();
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(cam.gameObject);
        Debug.Log($"[probe12] wrote {name}.png");
    }

    private static string HierarchyPath(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

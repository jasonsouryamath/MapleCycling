using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use. Renders the gate shot with ONLY the tunnel visible.</summary>
public static class SakuraEnvProbe10
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.RenderDir("probe");
        Directory.CreateDirectory(dir);
        var route = SakuraPassEnvironment.SakuraRoute.Load();

        var tunnel = GameObject.Find("Sakura Pass Environment/Landmarks/Cliff Tunnel");
        var envRoot = GameObject.Find("Sakura Pass Environment");
        var landmarks = GameObject.Find("Sakura Pass Environment/Landmarks");

        var turnedOff = new List<GameObject>();
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root == envRoot) continue;
            if (root.GetComponentInChildren<Light>(true) != null) continue; // keep lighting
            if (root.activeSelf) { root.SetActive(false); turnedOff.Add(root); }
        }
        foreach (Transform g in envRoot.transform)
            if (g.gameObject != landmarks && g.gameObject.activeSelf)
            { g.gameObject.SetActive(false); turnedOff.Add(g.gameObject); }
        foreach (Transform g in landmarks.transform)
            if (g.gameObject != tunnel && g.gameObject.activeSelf)
            { g.gameObject.SetActive(false); turnedOff.Add(g.gameObject); }

        Shoot(dir, "probe10_only_tunnel", route);

        var kids = tunnel.transform.Cast<Transform>().ToList();
        foreach (var k in kids) k.gameObject.SetActive(false);
        foreach (var k in kids)
        {
            k.gameObject.SetActive(true);
            Shoot(dir, "probe10_only_" + k.name, route);
            k.gameObject.SetActive(false);
        }
        foreach (var k in kids) k.gameObject.SetActive(true);
        foreach (var g in turnedOff) g.SetActive(true);
        Debug.Log("[probe10] done");
    }

    private static void Shoot(string dir, string name, SakuraPassEnvironment.SakuraRoute route)
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
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0.35f, 0.6f, 1f);
        cam.allowHDR = true;

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
        Object.DestroyImmediate(go);
        Debug.Log($"[probe10] wrote {name}.png");
    }
}

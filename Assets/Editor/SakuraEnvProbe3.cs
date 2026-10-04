using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use. A/B renders with the petal VFX disabled.</summary>
public static class SakuraEnvProbe3
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.RenderDir("probe");
        Directory.CreateDirectory(dir);

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        var petals = GameObject.Find("Sakura Pass Environment/Sakura Petal Drift");
        Debug.Log($"[probe3] petal object: {(petals == null ? "NOT FOUND" : petals.name)}");

        Shoot(dir, "probe_gate_with", route, 0.125f);
        Shoot(dir, "probe_tunnel_with", route, 0.60f);

        if (petals != null) petals.SetActive(false);
        Shoot(dir, "probe_gate_without", route, 0.125f);
        Shoot(dir, "probe_tunnel_without", route, 0.60f);
        if (petals != null) petals.SetActive(true);

        Debug.Log("[probe3] done");
    }

    private static void Shoot(string dir, string name, SakuraPassEnvironment.SakuraRoute route, float t)
    {
        int i = Mathf.Clamp(route.IndexAtClimbFraction(t), 0, route.Count - 1);
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
        go.AddComponent<SakuraPostFX>();

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
        Debug.Log($"[probe3] wrote {name}.png");
    }
}

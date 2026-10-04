using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use.</summary>
public static class SakuraEnvProbe7
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var tunnel = GameObject.Find("Sakura Pass Environment/Landmarks/Cliff Tunnel");
        foreach (var r in tunnel.GetComponentsInChildren<Renderer>(true))
            Debug.Log($"[probe7] {Path(r.transform)} enabled={r.enabled} active={r.gameObject.activeInHierarchy} " +
                      $"centre={r.bounds.center} size={r.bounds.size} " +
                      $"mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "<null>")} " +
                      $"shader={(r.sharedMaterial != null ? r.sharedMaterial.shader.name : "?")}");

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        int gi = Mathf.Clamp(route.IndexAtClimbFraction(0.125f), 0, route.Count - 1);
        Debug.Log($"[probe7] gate sample {gi} at {route.Position[gi]}");
        int ti = Mathf.Clamp(route.IndexAtClimbFraction(0.60f), 0, route.Count - 1);
        Debug.Log($"[probe7] tunnel sample {ti} at {route.Position[ti]}");
        Debug.Log("[probe7] done");
    }

    private static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

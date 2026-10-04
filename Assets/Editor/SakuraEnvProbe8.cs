using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>TEMPORARY probe - delete after use.</summary>
public static class SakuraEnvProbe8
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var tunnel = GameObject.Find("Sakura Pass Environment/Landmarks/Cliff Tunnel");
        foreach (var c in tunnel.GetComponentsInChildren<Component>(true))
        {
            if (c is Transform || c is MeshFilter || c is MeshRenderer) continue;
            string extra = "";
            if (c is Light l) extra = $" type={l.type} range={l.range} intensity={l.intensity} spotAngle={l.spotAngle} cookie={(l.cookie != null ? l.cookie.name : "none")} shadows={l.shadows}";
            Debug.Log($"[probe8] {Path(c.transform)} :: {c.GetType().FullName}{extra}");
        }

        // Anything in the scene that is neither a MeshRenderer nor a SkinnedMeshRenderer.
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                Debug.Log($"[probe8] odd renderer {Path(r.transform)} :: {r.GetType().Name} bounds={r.bounds}");

        Debug.Log("[probe8] done");
    }

    private static string Path(Transform t)
    {
        string s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

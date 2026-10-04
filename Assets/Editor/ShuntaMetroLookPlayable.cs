using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Applies the Shunta neon look to the playable scene (z+30000). Run: ShuntaMetroLookPlayable.ApplyToPlayable.
/// The route builder + look driver live at the world origin and the builder's positionBias carries the offset,
/// so the look's mixed local/world maths stays consistent. The original offset ribbon/gates are replaced.
/// </summary>
public static class ShuntaMetroLookPlayable
{
    const string RootName = "Shunta Look Playable";

    [MenuItem("MapleRide/Shunta Metro/Apply Look To Playable")]
    public static void ApplyToPlayable()
    {
        string path = MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro);
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        TextAsset json = null;
        var env = GameObject.Find(ShuntaMetroIntegration.EnvRoot);
        if (env != null)
        {
            var old = env.GetComponentInChildren<ShuntaRouteBuilder>(true);
            if (old != null) { json = old.courseJson; Object.DestroyImmediate(old.gameObject); }
        }
        var prev = GameObject.Find(RootName); if (prev != null) Object.DestroyImmediate(prev);
        var oldMoon = GameObject.Find("Moonlight"); if (oldMoon != null) Object.DestroyImmediate(oldMoon);

        var root = new GameObject(RootName);
        // keep the look inside the environment root so the integration self-test and streaming see it
        if (env != null) root.transform.SetParent(env.transform, true);
        var rgo = new GameObject("Shunta Route"); rgo.transform.SetParent(root.transform, false);
        var rb = rgo.AddComponent<ShuntaRouteBuilder>();
        rb.courseJson = json; rb.positionBias = ShuntaRouteProvider.WorldOffset;
        rb.Rebuild();
        if (rb.Course == null) { Fail("route did not build (courseJson?)"); return; }

        var dgo = new GameObject("Shunta Look"); dgo.transform.SetParent(root.transform, false);
        var d = dgo.AddComponent<ShuntaLookDriver>();
        d.route = rb; d.previewKm = 0.3f;
        d.follow = FindRider();
        d.BuildAll();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[shunta-look-playable] APPLY OK start {rb.PositionAtKm(0f)} follow={(d.follow != null ? d.follow.name : "NULL")} " + d.LastReport);
    }

    static Transform FindRider()
    {
        var rb = Object.FindFirstObjectByType<RideBootstrap>(FindObjectsInactive.Include);
        if (rb != null && rb.rider != null) return rb.rider;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.parent == null && t.name.StartsWith("Kuro")) return t;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.StartsWith("Kuro")) return t.root;
        return null;
    }

    static void Fail(string msg)
    {
        Debug.LogError("[shunta-look-playable] FAILED: " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}

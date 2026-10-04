// One-off diagnostic probe for the ShiosaiCoast start-line milestone.
// Dumps every renderer whose bounds intersect a box around route arc 0, so the
// "flat teal plane above the road" can be identified by NAME rather than guessed at.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShiosaiStartProbe
{
    [MenuItem("MapleRide/Environment/Probe Shiosai Start")]
    public static void Probe()
    {
        const string scenePath = "Assets/Scenes/SakuraPass.unity";
        if (EditorSceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
            Debug.Log("[probe] region set to ShiosaiCoast.");
        }

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        var p = route.Position[0];
        Debug.Log($"[probe] arc0 = {p}  (route y {route.MinY:0.0}..{route.MaxY:0.0})");

        // Box around the opening 0-200 m of the route.
        var probeBox = new Bounds(p + Vector3.up * 10f, new Vector3(400f, 260f, 400f));

        var rends = UnityEngine.Object.FindObjectsByType<MeshRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Debug.Log($"[probe] {rends.Length} active MeshRenderers in scene.");

        var hits = new List<(string path, Bounds b, string shader, string mat)>();
        foreach (var r in rends)
        {
            if (r == null || !r.enabled) continue;
            if (!r.bounds.Intersects(probeBox)) continue;
            var m = r.sharedMaterial;
            hits.Add((FullPath(r.transform), r.bounds,
                      m != null && m.shader != null ? m.shader.name : "<null>",
                      m != null ? m.name : "<null>"));
        }

        Debug.Log($"[probe] {hits.Count} renderers intersect the start box.");
        foreach (var h in hits.OrderBy(h => h.b.center.y))
        {
            Debug.Log($"[probe] Y {h.b.min.y,9:0.00}..{h.b.max.y,9:0.00} | " +
                      $"size {h.b.size.x,8:0}x{h.b.size.y,6:0}x{h.b.size.z,8:0} | " +
                      $"{h.mat} ({h.shader}) | {h.path}");
        }

        // What is directly ABOVE and BELOW the road at arc 0? Raycast-free: report every
        // renderer whose bounds contain the road point in XZ.
        Debug.Log("[probe] --- renderers spanning arc0 in XZ ---");
        foreach (var h in hits)
        {
            if (p.x < h.b.min.x || p.x > h.b.max.x) continue;
            if (p.z < h.b.min.z || p.z > h.b.max.z) continue;
            Debug.Log($"[probe] SPANS-ARC0  Y {h.b.min.y:0.00}..{h.b.max.y:0.00} | {h.mat} | {h.path}");
        }

        // Scene roots and their active state.
        Debug.Log("[probe] --- scene roots ---");
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            Debug.Log($"[probe] root '{go.name}' active={go.activeSelf}");

        // Lights.
        Debug.Log("[probe] --- lights ---");
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Debug.Log($"[probe] light '{FullPath(l.transform)}' type={l.type} active={l.gameObject.activeInHierarchy} " +
                      $"intensity={l.intensity} shadows={l.shadows} colour={l.color} rot={l.transform.eulerAngles}");
    }

    private static string FullPath(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

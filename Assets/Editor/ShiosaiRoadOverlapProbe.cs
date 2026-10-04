using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only spatial probe: what geometry actually occupies the road surface in front of the
/// harbour-road diagnostic camera?
///
/// Written because the carriageway renders covered in a leaf albedo even though its material
/// asset is demonstrably correct (Shiosai_Asphalt._MainTex = Shiosai_Asphalt_Albedo, and that
/// PNG is correct asphalt) and the project sets no global textures anywhere. That leaves two
/// very different explanations - the road shader sampling the wrong texture, or a foliage
/// surface physically lying on top of the road - and a screenshot cannot tell them apart.
/// This reports every renderer overlapping the road surface, nearest first.
///
/// Opens the scene read-only and never saves it.
/// </summary>
public static class ShiosaiRoadOverlapProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Probe Shiosai Road Overlap")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
        }

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();

        // Same stop the harbour-road capture uses (fraction 0.05 of the centreline), then a
        // point a little way down the road - that is the patch of carriageway filling the
        // bottom of the frame.
        int i = Mathf.Clamp(route.IndexAt(0.05f * route.Length), 0, route.Count - 1);
        int ahead = Mathf.Min(route.Count - 1, i + Mathf.Max(1, route.Count / 300));
        Vector3 eye = route.Position[i];
        Vector3 target = route.Position[ahead];

        Debug.Log($"[sc-overlap] eye={eye} target={target}");

        // A small box centred on the carriageway a few metres ahead of the rider.
        var probePoint = target;
        var probe = new Bounds(probePoint, new Vector3(6f, 6f, 6f));

        var hits = new List<(float d, string line)>();

        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.bounds.Intersects(probe)) continue;

                var mat = r.sharedMaterial;
                string tex = "-";
                if (mat != null && mat.HasProperty("_MainTex"))
                {
                    var t = mat.GetTexture("_MainTex");
                    tex = t != null ? t.name : "<unset>";
                }

                float d = Vector3.Distance(r.bounds.ClosestPoint(probePoint), probePoint);
                hits.Add((d, $"[sc-overlap] d={d,7:0.00}m '{r.gameObject.name}' " +
                             $"active={r.gameObject.activeInHierarchy} " +
                             $"mat='{(mat != null ? mat.name : "<null>")}' " +
                             $"shader='{(mat != null && mat.shader != null ? mat.shader.name : "<null>")}' " +
                             $"_MainTex={tex} centre={r.bounds.center} size={r.bounds.size}"));
            }
        }

        foreach (var h in hits.OrderBy(h => h.d).Take(40)) Debug.Log(h.line);
        Debug.Log($"[sc-overlap] {hits.Count} renderers overlap the probe box; DONE");
    }
}

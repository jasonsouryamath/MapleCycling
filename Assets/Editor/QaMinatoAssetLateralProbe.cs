using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// THROWAWAY QA probe: finds every instantiated GameObject whose name matches a given source
/// asset prefix (Unity's Instantiate suffixes clones with "(Clone)"/" (1)" etc., so this uses
/// Contains, not exact match) anywhere under "Minato Coast Environment", and reports each one's
/// lateral distance from the route centreline plus whether it (or any parent/child) carries a
/// Collider. Sorted by |lateral| ascending so the worst offenders are first regardless of how far
/// down the 19 km route they sit.
/// </summary>
public static class QaMinatoAssetLateralProbe
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RootName = "Minato Coast Environment";

    [MenuItem("MapleRide/QA/Probe Minato Asset Lateral Offsets")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogError("[qa-minato2] Minato Coast Environment root not found - build it first.");
            return;
        }

        var route = MinatoCoastEnvironment.MinatoRoute.Load();

        string[] needles = { "Warehouse", "CrateStack", "HeroDistrict", "ShopHouse", "ContainerCrane" };

        var results = new List<(string name, float d, float lateral, bool hasCollider, Vector3 pos)>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!needles.Any(n => t.name.Contains(n))) continue;
            // Only top-of-clone roots: skip descendants of an already-matched instance to avoid
            // reporting every mesh part inside one building separately.
            if (t.parent != null && needles.Any(n => t.parent.name.Contains(n))) continue;

            var pos = t.position;
            float bestLat = float.MaxValue, bestD = -1f; int bestI = -1;
            for (int i = 0; i < route.Count; i++)
            {
                var p = route.Position[i];
                float dx = pos.x - p.x, dz = pos.z - p.z;
                float lat2 = dx * dx + dz * dz;
                if (lat2 < bestLat) { bestLat = lat2; bestI = i; }
            }
            if (bestI < 0) continue;
            bestD = route.Distance[bestI];
            var side = route.SideFlat(bestI);
            var toObj = new Vector3(pos.x - route.Position[bestI].x, 0f, pos.z - route.Position[bestI].z);
            float lateral = Vector3.Dot(toObj, side);
            bool hasCollider = t.GetComponentInParent<Collider>() != null ||
                               t.GetComponentInChildren<Collider>() != null;
            results.Add((t.name, bestD, lateral, hasCollider, pos));
        }

        results.Sort((a, b) => Mathf.Abs(a.lateral).CompareTo(Mathf.Abs(b.lateral)));

        var log = new StringBuilder();
        log.AppendLine($"=== MINATO PORT-PROP LATERAL OFFSETS ({results.Count} instances found) ===");
        foreach (var r in results.Take(40))
            log.AppendLine($"'{r.name}' at d={r.d:N1} m, lateral={r.lateral:N2} m, " +
                            $"pos={r.pos}, HAS_COLLIDER={r.hasCollider}");

        Debug.Log(log.ToString());
        Directory.CreateDirectory("reference/good_graphics");
        File.WriteAllText("reference/good_graphics/qa_minato_asset_lateral.txt", log.ToString());
    }
}

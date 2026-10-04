using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Measurement harness for two reported NPC defects:
///   (1) riders sinking into the road surface, in BOTH courses,
///   (2) riders that are invisible yet still greet the player.
///
/// It reports MEASUREMENTS, not verdicts.
///
/// The course-independent number is <c>contactDY</c>: the lowest drawn point of the rider,
/// expressed relative to the NPC root's own origin. Every placement script in the project seats
/// a rider by putting that root on the road, so if contactDY is negative the rider sinks on
/// every course no matter how good the route data is.
///
/// The ground probe deliberately does NOT take the first hit downward: Sakura Pass stacks a
/// hairpin over itself, so a downward ray from a rider on the lower deck hits the deck ABOVE
/// and reports a bogus "the surface is 4 m over your head". It collects every road hit in a
/// window around the rider and picks the nearest by |dy|.
/// </summary>
public static class NpcDefectProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/NPC Defect Probe")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SakuraSceneDefaults.Fix();

        var greetings = Object.FindObjectsByType<NpcGreeting>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[npcprobe] found {greetings.Length} NpcGreeting components in the scene.");

        var sb = new StringBuilder();
        var seen = new HashSet<GameObject>();

        // Is the route baked into the scene still the route the world is built from? The NPC
        // route arrays are SERIALIZED, so a rebuild of SakuraRoute.json silently leaves every
        // rider riding the shape of an older pass.
        var live = MapleRideKuroSetup.ResampleRoute(MapleRideKuroSetup.GetRoadPoints(),
                                                    SakuraNpcRoster.RouteSpacing);
        Debug.Log($"[npcprobe] live route = {live.Length} points, " +
                  $"first={live[0]:F2} last={live[live.Length - 1]:F2}");

        foreach (var cyc in Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None))
        {
            var baked = cyc.route;
            if (baked == null || baked.Length == 0) { Debug.LogWarning($"[npcprobe] {cyc.name}: no baked route"); continue; }
            var fwd = baked.Reverse().ToArray();   // staged reversed; un-reverse to compare
            float worst = 0f;
            int n = Mathf.Min(fwd.Length, live.Length);
            for (int i = 0; i < n; i++) worst = Mathf.Max(worst, Vector3.Distance(fwd[i], live[i]));
            Debug.Log($"[npcprobe] ROUTE {cyc.name}: baked={baked.Length} live={live.Length} " +
                      $"worstDrift={worst:F3} m");
        }

        foreach (var g in greetings)
        {
            var lod = g.GetComponentInParent<ShiosaiRiderLod>();
            var cyc = g.GetComponentInParent<NPCCyclist>();
            var rootGo = lod != null ? lod.gameObject
                       : cyc != null ? cyc.gameObject
                       : g.gameObject;
            if (!seen.Add(rootGo)) continue;

            var origin = rootGo.transform.position;

            // ---- contact height, relative to the rider's own root -------------------
            // Split by renderer type on purpose. A SkinnedMeshRenderer's bounds here are the
            // FROZEN ones written by SakuraNpcRoster.FreezeSkinnedBounds - padded by 50% and,
            // in batchmode, possibly baked from a mesh that never posed. A plain MeshRenderer's
            // bounds are exact, so the BIKE is the only trustworthy contact measurement.
            float lowSkin = float.MaxValue, lowSolid = float.MaxValue;
            string solidName = "-";
            int rendersOn = 0, rendersTotal = 0;
            foreach (var r in rootGo.GetComponentsInChildren<Renderer>(true))
            {
                rendersTotal++;
                if (r.enabled && r.gameObject.activeInHierarchy) rendersOn++;
                if (r is ParticleSystemRenderer) continue;
                if (r is SkinnedMeshRenderer) lowSkin = Mathf.Min(lowSkin, r.bounds.min.y);
                else if (r.bounds.min.y < lowSolid) { lowSolid = r.bounds.min.y; solidName = r.name; }
            }
            float contactDy = lowSolid == float.MaxValue ? 0f : lowSolid - origin.y;
            float skinDy = lowSkin == float.MaxValue ? 0f : lowSkin - origin.y;

            // ---- nearest road surface, ignoring decks stacked overhead -------------
            string groundTxt = "no-road-within-6m";
            var hits = Physics.RaycastAll(origin + Vector3.up * 6f, Vector3.down, 12f)
                              .Where(h => h.collider.transform.name.Contains("Road"))
                              .OrderBy(h => Mathf.Abs(h.point.y - origin.y))
                              .ToArray();
            if (hits.Length > 0)
                groundTxt = $"road={hits[0].point.y:F3} rootDelta={(origin.y - hits[0].point.y):+0.000;-0.000} " +
                            $"decks={hits.Length}";

            // ---- visibility state --------------------------------------------------
            var smrs = rootGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bad = new List<string>();
            foreach (var smr in smrs)
            {
                // Frozen local bounds that no longer sit around the rider mean the renderer is
                // frustum-culled at the wrong times - or permanently.
                var b = smr.bounds;
                bool sane = b.size.y > 0.05f && b.size.y < 12f &&
                            Vector3.Distance(b.center, smr.transform.position) < 6f;
                if (!sane) bad.Add($"{smr.name}(c={b.center} s={b.size} uwo={smr.updateWhenOffscreen})");
            }

            sb.AppendLine(
                $"[npcprobe] {rootGo.name} active={rootGo.activeInHierarchy} greet={g.enabled} " +
                $"rend={rendersOn}/{rendersTotal} smr={smrs.Length} " +
                $"bikeDY={contactDy:+0.000;-0.000} (from '{solidName}') skinDY={skinDy:+0.000;-0.000} | {groundTxt}" +
                (bad.Count > 0 ? "  BADBOUNDS: " + string.Join("; ", bad) : ""));
        }

        Debug.Log(sb.ToString());
        Debug.Log("[npcprobe] done.");
    }
}

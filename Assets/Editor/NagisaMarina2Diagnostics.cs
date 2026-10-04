using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Worker G (Nagisa destination brief sections 7/12/13/9/11): review cameras + a placement survey for the
/// marina, climb and descent dressing. Same convention as NagisaBayDiagnostics.Capture: region switched
/// visible first, previous region restored in finally, scene never saved.
///   NagisaMarina2Diagnostics.Survey   : logs what already stands in the marina rectangle (by group).
///   NagisaMarina2Diagnostics.Capture  : marina frames (aerial, from sea, from road, fishing harbour).
///   NagisaMarina2Diagnostics.CaptureClimb : climb + descent dressing frames.
/// Output: reference/good_graphics/nagisa_bay/g_*.png
/// </summary>
public static class NagisaMarina2Diagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    private static RegionDirector Enter(out string previous)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = NagisaBayEnvironment.RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        Directory.CreateDirectory(NagisaBayDiagnostics.OutDir);
        return regions;
    }

    private static void Leave(RegionDirector regions, string previous)
    {
        if (regions != null && !string.IsNullOrEmpty(previous))
        {
            regions.currentRegionId = previous;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
    }

    private static string TopGroup(Transform t, Transform root)
    {
        var sb = new StringBuilder();
        var chain = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) chain.Add(c.name);
        chain.Reverse();
        // "NB Overhaul/NB Town/..." -> first two levels under the root
        for (int i = 0; i < chain.Count && i < 2; i++) { if (i > 0) sb.Append('/'); sb.Append(chain[i]); }
        return sb.ToString();
    }

    [MenuItem("MapleRide/Diagnostics/Nagisa Marina2 Survey")]
    public static void Survey()
    {
        var regions = Enter(out var prev);
        try
        {
            var r = NagisaBayEnvironment.NagisaRoute.Load();
            var root = GameObject.Find(NagisaBayEnvironment.RootName);
            if (root == null) { Debug.LogError("[g-survey] no Nagisa root"); return; }
            // rectangle: route m 120..920, 8..200 m on the SEA side and 8..120 m landward
            var counts = new Dictionary<string, int>();
            var samples = new Dictionary<string, string>();
            foreach (var rd in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rd is ParticleSystemRenderer) continue;
                var p = rd.bounds.center;
                float d = r.PlanDistance(p.x, p.z, out int idx);
                float m = r.Distance[idx];
                if (m < 120f || m > 920f || d > 220f) continue;
                string key = TopGroup(rd.transform, root.transform);
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                if (!samples.ContainsKey(key)) samples[key] = $"{rd.name} @m{m:0} d{d:0} y{p.y:0.0}";
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(40))
                Debug.Log($"[g-survey] {kv.Value,6}  {kv.Key}   e.g. {samples[kv.Key]}");
            // pads
            foreach (var kv in r.Pads) Debug.Log($"[g-survey] pad {kv.Key} c={kv.Value.c} h={kv.Value.h}");
            // buildings (LODGroups) near the marina
            var seen = new List<string>();
            foreach (var lg in root.GetComponentsInChildren<LODGroup>(true))
            {
                var p = lg.transform.position;
                float d = r.PlanDistance(p.x, p.z, out int idx);
                float m = r.Distance[idx];
                if (m < 200f || m > 760f || d > 160f) continue;
                seen.Add($"{lg.name}@m{m:0}/d{d:0}");
            }
            Debug.Log($"[g-survey] LODGroups near marina ({seen.Count}): {string.Join("; ", seen.Take(60))}");
        }
        finally { Leave(regions, prev); }
    }

    private static void ShotAt(NagisaBayEnvironment.NagisaRoute r, string file, float m, float seaOff, float up,
                               float ahead, float aheadSea, float fov)
    {
        int i = r.IndexAt(m);
        var sd = r.SideFlat(i) * NagisaBayEnvironment.DiagSeaSign(i);
        var eye = r.Position[i] + sd * seaOff + Vector3.up * up;
        int j = r.IndexAt(Mathf.Clamp(m + ahead, 0f, r.Length));
        var target = r.Position[j] + sd * aheadSea + Vector3.up * 1.0f;
        NagisaBayDiagnostics.Shot(eye, target, fov, file);
    }

    [MenuItem("MapleRide/Diagnostics/Nagisa Marina2 Capture")]
    public static void Capture()
    {
        var regions = Enter(out var prev);
        try
        {
            var r = NagisaBayEnvironment.NagisaRoute.Load();
            // top-down over the marina core and fishing harbour
            int ic = r.IndexAt(470f);
            var sd = r.SideFlat(ic) * NagisaBayEnvironment.DiagSeaSign(ic);
            var c = r.Position[ic] + sd * 90f;
            NagisaBayDiagnostics.Shot(c + Vector3.up * 330f + r.Tangent[ic] * -1f, c, 40f, "g_marina_top.png");
            int ih = r.IndexAt(790f);
            var sd2 = r.SideFlat(ih) * NagisaBayEnvironment.DiagSeaSign(ih);
            var c2 = r.Position[ih] + sd2 * 70f;
            NagisaBayDiagnostics.Shot(c2 + Vector3.up * 260f - r.Tangent[ih] * 1f, c2, 40f, "g_harbour_top.png");
            // NB9 coral outcrops: how many stand in the scene (flora scan + ChatGPT's dedicated shoreline scan)
            {
                int coral = 0, withinSeaBand = 0;
                var rootT = GameObject.Find(NagisaBayEnvironment.RootName);
                if (rootT != null)
                    foreach (var tr in rootT.GetComponentsInChildren<Transform>(true))
                        if (tr.name.StartsWith("Nagisa_NB9_CoralOutcrop") && !tr.name.Contains("_LOD"))
                        {
                            coral++;
                            float dd = r.PlanDistance(tr.position.x, tr.position.z, out _);
                            if (dd < 40f) withinSeaBand++;
                        }
                Debug.Log($"[g-coral] {coral} coral outcrops in the scene, {withinSeaBand} within 40 m of the route.");
            }
            // traffic: count the highway vehicle kinds (polish check) and look along the carriageway from just inland of the ride road
            var kinds = new Dictionary<string, int>();
            foreach (var v in Object.FindObjectsByType<NagisaTrafficVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                kinds[v.kind] = kinds.TryGetValue(v.kind, out int kc) ? kc + 1 : 1;
            Debug.Log($"[g-traffic] {kinds.Values.Sum()} vehicles: " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key.Replace("Nagisa_", "")}")));
            {
                int it = r.IndexAt(1400f);
                var sdt = r.SideFlat(it) * NagisaBayEnvironment.DiagSeaSign(it);
                var eyeT = r.Position[it] - sdt * 3f + Vector3.up * 9f;
                int jt = r.IndexAt(1480f);
                NagisaBayDiagnostics.Shot(eyeT, r.Position[jt] - sdt * 20f + Vector3.up * 1f, 55f, "g_traffic.png");
            }
            // from the sea looking back at the marina frontage
            ShotAt(r, "g_marina_from_sea.png", 460f, 260f, 22f, 0f, -20f, 55f);
            // from the road
            ShotAt(r, "g_marina_road.png", 300f, 3f, 1.7f, 130f, 60f, 62f);
            ShotAt(r, "g_marina_oblique.png", 520f, 20f, 12f, -60f, 70f, 62f);
            ShotAt(r, "g_harbour_oblique.png", 700f, 6f, 9f, 90f, 60f, 62f);
            ShotAt(r, "g_village_road.png", 330f, 2f, 1.7f, 90f, 36f, 62f);
            ShotAt(r, "g_boardwalk.png", 330f, 72f, 1.7f, 100f, 72f, 62f);
            ShotAt(r, "g_harbour_apron.png", 705f, 20f, 5f, 90f, 40f, 62f);
            ShotAt(r, "g_mole.png", 760f, 55f, 6f, 36f, 130f, 62f);
        }
        finally { Leave(regions, prev); }
        Debug.Log("[g] marina frames captured.");
    }

    private static Transform FindDeep(Transform root, System.Func<string, bool> pred)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (pred(t.name)) return t;
        return null;
    }

    /// <summary>Frames of the new climb/descent dressing, located by object name so it works wherever the stage put them.</summary>
    [MenuItem("MapleRide/Diagnostics/Nagisa Marina2 Capture Dressing")]
    public static void CaptureDressing()
    {
        var regions = Enter(out var prev);
        try
        {
            var r = NagisaBayEnvironment.NagisaRoute.Load();
            var rootGo = GameObject.Find(NagisaBayEnvironment.RootName);
            if (rootGo == null) { Debug.LogError("[g] no root"); return; }
            var shots = new List<(string file, Transform target, float behind, float up, float fov)>();
            void Add(string file, System.Func<string, bool> pred, float behind, float up, float fov)
            {
                var t = FindDeep(rootGo.transform, pred);
                if (t != null) shots.Add((file, t, behind, up, fov)); else Debug.Log($"[g] no object for {file}");
            }
            Add("g_dress_lookout0.png", n => n == "Nagisa_CL_LookoutDeck_0 - Beach Sand walk layer" || n == "Nagisa_CL_LookoutDeck_0", 40f, 1.7f, 62f);
            Add("g_dress_lookout1.png", n => n.StartsWith("Nagisa_CL_LookoutDeck_1"), 40f, 1.7f, 62f);
            Add("g_dress_gallery0.png", n => n.StartsWith("Gallery 0"), 38f, 1.7f, 62f);
            Add("g_dress_villa.png", n => n.StartsWith("CL Villa 00"), 30f, 6f, 62f);
            Add("g_dress_resort.png", n => n.StartsWith("CL Resort 00"), 40f, 8f, 62f);
            Add("g_dress_wall.png", n => n.StartsWith("Nagisa_CL_Wall_3"), 25f, 1.7f, 62f);
            Add("g_dress_pines.png", n => n == "Black pines", 30f, 1.7f, 62f);
            foreach (var (file, t, behind, up, fov) in shots)
            {
                var c = t.position;
                foreach (var rr in t.GetComponentsInChildren<Renderer>(true)) { c = rr.bounds.center; break; }
                float d = r.PlanDistance(c.x, c.z, out int idx);
                var eyeBase = r.Position[Mathf.Max(0, idx - Mathf.RoundToInt(behind / 4f))];
                var eye = eyeBase + Vector3.up * up;
                NagisaBayDiagnostics.Shot(eye, c + Vector3.up * 1.5f, fov, file);
            }
        }
        finally { Leave(regions, prev); }
        Debug.Log("[g] dressing frames captured.");
    }

    [MenuItem("MapleRide/Diagnostics/Nagisa Marina2 Capture Climb")]
    public static void CaptureClimb()
    {
        var regions = Enter(out var prev);
        try
        {
            var r = NagisaBayEnvironment.NagisaRoute.Load();
            foreach (var (file, m, side, up, ahead, aheadSide, fov) in new (string, float, float, float, float, float, float)[]
            {
                ("g_climb_a.png", 5000f, 0f, 1.7f, 120f, 25f, 62f),
                ("g_climb_b.png", 5800f, 0f, 1.7f, 120f, 25f, 62f),
                ("g_climb_c.png", 6700f, 0f, 1.7f, 120f, 25f, 62f),
                ("g_climb_d.png", 7600f, 0f, 1.7f, 120f, 25f, 62f),
                ("g_descent_a.png", 8700f, 0f, 1.7f, 120f, 25f, 62f),
                ("g_descent_b.png", 9800f, 0f, 1.7f, 120f, 25f, 62f),
                ("g_descent_c.png", 11200f, 0f, 1.7f, 120f, 25f, 62f),
            })
            {
                int i = r.IndexAt(m);
                var s = r.SideFlat(i);
                var eye = r.Position[i] + s * side + Vector3.up * up;
                int j = r.IndexAt(Mathf.Clamp(m + ahead, 0f, r.Length));
                NagisaBayDiagnostics.Shot(eye, r.Position[j] + s * aheadSide + Vector3.up * 1.2f, fov, file);
            }
        }
        finally { Leave(regions, prev); }
        Debug.Log("[g] climb/descent frames captured.");
    }
}

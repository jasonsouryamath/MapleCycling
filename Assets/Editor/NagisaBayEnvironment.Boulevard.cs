// NAGISA BAY premier resort cycling destination, brief section 5 (+ the bike-lane half of 4): the grand
// beachfront boulevard (Claude worker F, 2026-10-01). Stage "Boulevard", order 91.
//
// Dresses AROUND the ride road, never in the rider's line:
//   * painted JADE BIKE LANES (0.75 m, with bicycle pictograms and a solid inboard line) on both edges of the
//     carriageway along the coastal spans: flat decals 3 cm above the surface, no collider;
//   * a royal-palm AVENUE on the sea side of the road: every ~28 m a palm in a granite planter ring just
//     inside the promenade paving (>= 7.3 m from the centreline), with modern street lights between where the
//     promenade has none;
//   * beach-access boardwalks (timber path, sand-fence rail, outdoor shower) with BEACH ACCESS boards,
//     resort/promenade/direction signage at the boulevard's key points.
// The boulevard keeps its existing curves; no re-route. Everything is occupancy-checked against earlier stages.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private static readonly Vector2[] NbcLaneSpans = { new Vector2(120f, 4700f), new Vector2(12190f, 14960f) };
    private const float NbcLaneIn = 2.45f, NbcLaneOut = 3.2f, NbcLaneLiftM = 0.03f;

    [NagisaStage(91, "Boulevard")]
    private static void BuildBoulevard(Transform group)
    {
        NbcInit();
        Nb9Materials();                      // royal palm / ground slots (Flora registers them at stage 96)
        _nbcLog.Clear(); _nbcPlaced = _nbcSkipped = _nbcPeople = 0;
        var occ = NbcOcc.FromScene(group);
        var rng = new System.Random(9101);
        float R() => (float)rng.NextDouble();

        int lanes = NbcBikeLanes(NbcChild(group, "Bike lanes"));

        // ---- palm avenue + planters + street lights
        var avenue = NbcChild(group, "Palm avenue");
        var lights = NbcChild(group, "Boulevard lights");
        int palms = 0, lamps = 0;
        foreach (var span in NbcLaneSpans)
        {
            float lastPalm = -999f, lastLamp = -999f;
            for (float d = span.x + 20f; d < span.y - 20f; d += 6f)
            {
                int i = _route.IndexAt(d);
                if (_route.OnBridge(i)) continue;
                if (d - lastPalm >= 28f && NbcAt(d, 8.25f, out var p, out var sd, out _))
                {
                    var planter = NbcPut(occ, "Nagisa_NBC_PalmPlanter", avenue, p, R() * 360f, 1.5f, null, 1f, SmallPropCull);
                    if (planter != null)
                    {
                        lastPalm = d;
                        var pp = new Vector3(p.x, _ground.Height(p.x, p.z) + 0.25f, p.z);
                        PlaceWorld("Nagisa_NB9_RoyalPalm", avenue, pp, R() * 360f, 0.95f + R() * 0.2f, 0.0018f);
                        palms++;
                    }
                }
                // modern lights fill the stretches where the promenade has no lamps of its own
                bool beachZone = d > _route.BeachStartM && d < _route.BeachEndM;
                if (!beachZone && d - lastLamp >= 56f && d - lastPalm > 9f && NbcAt(d, 7.7f, out var lp, out var lsd, out _))
                {
                    lp.y -= 0.03f;
                    if (NbcPut(occ, "Nagisa_S_StreetLight", lights, lp, NbcYaw(-lsd), 1.2f, null, 1f, 0.003f) != null)
                    { lastLamp = d; lamps++; }
                }
            }
        }
        _nbcLog.Add($"bike lane quads {lanes}, palms {palms}, lamps {lamps}");

        // ---- beach access boardwalks + signage
        var access = NbcChild(group, "Beach access");
        int ba = 0;
        for (float d = _route.BeachStartM + 90f; d < _route.BeachEndM - 120f; d += 168f)
        {
            if (!NbcAt(d, 15.4f, out var p, out var sd, out var tan)) continue;
            var endP = p + sd * 10f;
            if (_ground.Coast(endP.x, endP.z) < 2.5f) continue;
            float yEnd = _ground.Height(endP.x, endP.z) + 0.1f;
            float drop = yEnd - p.y;
            if (Mathf.Abs(drop) > 2.4f) continue;
            if (occ.Hit(p, 1.6f) || occ.Hit(p + sd * 5f, 1.6f) || occ.Hit(endP, 1.6f)) continue;
            var go = PlaceWorld("Nagisa_NBC_BeachAccess", access, p, NbcYaw(sd), 1f, SmallPropCull);
            if (go == null) continue;
            go.transform.rotation = Quaternion.Euler(Mathf.Atan2(-drop, 10f) * Mathf.Rad2Deg, NbcYaw(sd), 0f);
            occ.AddRect(p + sd * 5f, 1.8f, 5.5f); ba++;
            if (ba % 2 == 1 && NbcAt(d - 5f, 14.6f, out var sp, out _, out var st))
                NbcPut(occ, "Nagisa_NBC_SignBoard", access, sp, NbcYaw(-st) + 6f, 1.5f, NbcSign("Sign", "beach_access"), 0.85f);
        }
        _nbcLog.Add($"beach access boardwalks {ba}");

        // ---- resort / direction signage along the verge (faces the oncoming rider)
        var signs = NbcChild(group, "Resort signage");
        var plan = new (float d, string key, float scale)[]
        {
            (940f, "promenade", 1.25f), (2170f, "resort", 1.4f), (2420f, "overlook", 1.1f), (3000f, "marina_dir", 1.2f),
            (3150f, "resort", 1.4f), (4250f, "town_dir", 1.2f), (12260f, "promenade", 1.25f), (13200f, "overlook", 1.1f),
            (13900f, "resort", 1.4f), (14700f, "marina_dir", 1.2f),
        };
        int ns = 0;
        foreach (var s in plan)
            for (float dm = 0f; dm < 60f; dm += 12f)
                if (NbcAt(s.d + dm, 7.7f, out var p, out _, out var t))
                {
                    if (NbcPut(occ, "Nagisa_NBC_SignBoard", signs, p, NbcYaw(-t) + 10f, 2.3f, NbcSign("Sign", s.key), s.scale, 0.003f) != null) { ns++; break; }
                }
        _nbcLog.Add($"resort/direction boards {ns}");
        foreach (var r in avenue.GetComponentsInChildren<MeshRenderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        NbcFlushLog("boulevard", group);
    }

    /// <summary>Jade cycle lanes on both carriageway edges, one mesh per ~1.2 km chunk (flat decal, no collider).</summary>
    private static int NbcBikeLanes(Transform parent)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{NbcTexDir}/NBC_BikeLane.png");
        var mat = Cel("Nagisa_NBC_BikeLane", Color.white, 0.18f, 0.08f, 0.05f, tex);
        SetF(mat, "_ShadowAmbient", 0.85f);
        int quads = 0, chunk = 0;
        foreach (var span in NbcLaneSpans)
        {
            int i0 = _route.IndexAt(span.x), i1 = _route.IndexAt(span.y);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float chunkStart = _route.Distance[i0];
            void Flush()
            {
                if (t.Count == 0) return;
                var mesh = Finish($"Nagisa_NBC_BikeLane_{chunk}", v, uv, t);
                var go = AddMesh(parent, $"Bike lane {chunk}", mesh, mat, false);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                chunk++; v.Clear(); uv.Clear(); t.Clear();
            }
            for (int i = i0; i < i1; i++)
            {
                if (_route.OnBridge(i) || _route.OnBridge(i + 1)) { Flush(); continue; }
                if (_route.Distance[i] - chunkStart > 1200f) { Flush(); chunkStart = _route.Distance[i]; }
                foreach (float side in new[] { -1f, 1f })
                {
                    float oIn = side * NbcLaneIn, oOut = side * NbcLaneOut;
                    var s0 = _route.SideFlat(i); var s1 = _route.SideFlat(i + 1);
                    var a = _route.Position[i] + s0 * oIn; var b = _route.Position[i] + s0 * oOut;
                    var c = _route.Position[i + 1] + s1 * oIn; var e = _route.Position[i + 1] + s1 * oOut;
                    a.y = RoadY(i, oIn) + NbcLaneLiftM; b.y = RoadY(i, oOut) + NbcLaneLiftM;
                    c.y = RoadY(i + 1, oIn) + NbcLaneLiftM; e.y = RoadY(i + 1, oOut) + NbcLaneLiftM;
                    int n = v.Count;
                    v.Add(a); v.Add(b); v.Add(c); v.Add(e);
                    float v0 = _route.Distance[i] / 40f, v1 = _route.Distance[i + 1] / 40f;
                    uv.Add(new Vector2(0f, v0)); uv.Add(new Vector2(1f, v0)); uv.Add(new Vector2(0f, v1)); uv.Add(new Vector2(1f, v1));
                    // wind so the face points up whichever side we are on
                    if (Vector3.Dot(Vector3.Cross(c - a, b - a), Vector3.up) > 0f) t.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 });
                    else t.AddRange(new[] { n, n + 1, n + 2, n + 1, n + 3, n + 2 });
                    quads++;
                }
            }
            Flush();
        }
        return quads;
    }
}

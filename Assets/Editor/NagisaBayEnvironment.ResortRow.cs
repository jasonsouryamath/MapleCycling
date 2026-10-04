// NB2 pass 3: the INLAND RESORT ROW. A stage (order 60, before NB13's infill at 85) that lines the
// beach strip across the NB3 highway (beyond the side rule's -42 m) with a frontage lane, a paved
// sidewalk with palms and street lights, and a cycle of DIFFERENT resort hotels / condos / boutique
// hotels, each on its own lawn lot with a paved forecourt and drive. Only the existing Nagisa_B_* GLBs
// (build_nagisa_buildings.py + build_nagisa_resorts.py) are placed, always facing the sea.
//
// Offsets are metres INLAND of the ride centreline (SeaSign flips the side on the east coast):
//   43.0 kerb | 43.3-49.7 frontage lane (asphalt) | 49.7 kerb | 50.0-55.0 sidewalk (palms, lamps)
//   55.0 lot front (forecourt + canopy) ... building ... lawn behind it.
// Nothing here is ever placed inside +-42 m (the highway band) or on the ride corridor; steep lots are
// skipped (the cycle just moves on), and a hidden LODGroup marker per 20 m of lot keeps NB13's infill
// buildings off the plots (Nb13Unoccupied checks LODGroup positions) without touching its file.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const float RrLane0 = 43.3f, RrLane1 = 49.7f, RrWalk1 = 55.0f, RrLotFront = 55.0f;
    private const float RrForecourt = 9f;       // sidewalk -> canopy tip
    private const float RrMaxLotSlopeM = 1.7f;   // max height range under a lot (plinth skirt hides the rest)

    private static readonly string[] RrCycle =
    {
        "Nagisa_B_ResortTerrace", "Nagisa_B_Boutique1", "Nagisa_B_ResortTwin", "Nagisa_B_Condo2",
        "Nagisa_B_ResortCrescent", "Nagisa_B_Boutique2", "Nagisa_B_ResortBungalow", "Nagisa_B_Condo1",
        "Nagisa_B_ResortHotel2",
    };

    private sealed class RrFoot { public float hw, front, back, h; }
    private static Dictionary<string, RrFoot> _rrFeet;

    private static RrFoot RrFootOf(string stem)
    {
        if (_rrFeet == null)
        {
            _rrFeet = new Dictionary<string, RrFoot>();
            string path = $"{ModelDir}/Nagisa_B_Footprints.json";
            if (System.IO.File.Exists(path))
            {
                string json = System.IO.File.ReadAllText(path);
                int i = 0;
                while ((i = json.IndexOf("\"Nagisa_", i, System.StringComparison.Ordinal)) >= 0)
                {
                    int e = json.IndexOf('"', i + 1);
                    string name = json.Substring(i + 1, e - i - 1);
                    int close = json.IndexOf('}', e);
                    string body = json.Substring(e, close - e);
                    float hx = JNum(body, "\"hx\""), hz = JNum(body, "\"hz\"");
                    float hw = JNum(body, "\"hw\""), fr = JNum(body, "\"front\""), bk = JNum(body, "\"back\"");
                    if (hw <= 0f) hw = hx;
                    if (body.IndexOf("\"front\"", System.StringComparison.Ordinal) < 0) { fr = -hz - 1f; bk = hz; }
                    _rrFeet[name] = new RrFoot { hw = hw, front = fr, back = bk, h = JNum(body, "\"h\"") };
                    i = close;
                }
            }
        }
        return _rrFeet.TryGetValue(stem, out var f) ? f : new RrFoot { hw = 12f, front = -10f, back = 10f, h = 30f };
    }

    private static Vector3 RrInland(int i) => -(_route.SideFlat(i) * SeaSign(i));

    [NagisaStage(60, "ResortRow")]
    private static void BuildResortRow(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) { PrepareNagisaTextures(); BuildNbMaterials(); }
        _rrFeet = null;
        var rng = new System.Random(6001);
        var bldT = Nb13Child(group, "Resort buildings");
        var landT = Nb13Child(group, "Lots and forecourts");
        var furnT = Nb13Child(group, "Frontage furniture");
        var keepT = Nb13Child(group, "Infill keep-out markers");

        var lane = new RrMesh(); var kerb = new RrMesh(); var walk = new RrMesh();
        var lawn = new RrMesh(); var plaza = new RrMesh();

        int placed = 0, skipped = 0, lamps = 0, palms = 0, cars = 0, k = 0, rank2 = 0;
        var rank1 = new List<(float dc, float backOff, float hw)>();
        var log = new List<string>();
        var lotLines = new List<string>();
        foreach (var span in new[] { (2700f, 4420f), (12260f, 14760f) })
        {
            RrFrontage(span.Item1 - 30f, span.Item2 + 30f, lane, kerb, walk);
            float d = span.Item1;
            int guard = 0;
            while (d < span.Item2 && guard++ < 400)
            {
                string stem = RrCycle[k % RrCycle.Length];
                var f = RrFootOf(stem);
                float dc = d + f.hw;
                if (dc + f.hw > span.Item2) break;
                int i = _route.IndexAt(dc);
                if (_route.OnBridge(i)) { d += 30f; continue; }
                var inland = RrInland(i);
                var sd = -inland;
                float yaw = Mathf.Atan2(-sd.x, -sd.z) * Mathf.Rad2Deg;
                var q = Quaternion.Euler(0f, yaw, 0f);
                float originOff = RrLotFront + RrForecourt - f.front;
                var o = _route.Position[i] + inland * originOff;

                // flatness + land test over the lot (plot rect incl. lawn margin)
                float x0 = -f.hw - 4f, x1 = f.hw + 4f, z0 = f.front - RrForecourt, z1 = f.back + 8f;
                float hMin = float.MaxValue, hMax = float.MinValue; bool ok = true;
                for (float x = x0; x <= x1 + 0.01f && ok; x += (x1 - x0) / 6f)
                    for (float z = z0; z <= z1 + 0.01f; z += (z1 - z0) / 6f)
                    {
                        var w = o + q * new Vector3(x, 0f, z);
                        if (_ground.Coast(w.x, w.z) < 6f || InAnyPad(w.x, w.z, 6f) ||
                            _route.PlanDistance(w.x, w.z, out _) < 44f) { ok = false; break; }
                        float h = _ground.Height(w.x, w.z);
                        hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
                    }
                if (!ok || hMax - hMin > RrMaxLotSlopeM) { skipped++; d += 24f; continue; }

                o.y = hMax + 0.14f;
                var go = PlaceWorld(stem, bldT, o, yaw, 1f, 0.0012f, NbColourway(k * 3 + 1), hero: true);
                if (go == null) { k++; d = dc + f.hw + 20f; continue; }
                placed++;
                rank1.Add((dc, originOff + f.back + 8f, f.hw));
                log.Add($"{stem}@{dc:0}m");
                lotLines.Add($"{stem},{o.x:0.00},{o.y:0.00},{o.z:0.00},{yaw:0.0},{dc:0.0}");

                // lot ground: lawn grid (not under bungalow's own lawn) + paved forecourt + drive
                bool ownLawn = stem.EndsWith("ResortBungalow");
                if (!ownLawn) RrGrid(lawn, o, q, x0, x1, f.front + 1f, z1, 8f, 0.12f, 4f);
                float fw = Mathf.Min(12f, f.hw * 0.5f);
                RrGrid(plaza, o, q, -fw, fw, z0, f.front + 3f, 3f, 0.16f, 2.4f);
                // drive from the sidewalk to the canopy is the same plaza strip; palm rows + hedges frame it
                var pal = new[] { "Nagisa_CoconutPalm_A", "Nagisa_FanPalm", "Nagisa_CoconutPalm_B" };
                for (float x = -f.hw; x <= f.hw + 0.1f; x += 13f)
                {
                    if (Mathf.Abs(x) < fw + 2f) continue;
                    var w = o + q * new Vector3(x, 0f, z0 + 2.2f); w.y = _ground.Height(w.x, w.z) - 0.02f;
                    if (PlaceWorld(pal[rng.Next(pal.Length)], furnT, w, yaw + rng.Next(360), 0.9f + (float)rng.NextDouble() * 0.3f, 0.004f) != null) palms++;
                }
                // hidden infill keep-out markers across the whole lot
                for (float x = x0; x <= x1; x += 20f)
                    for (float z = z0; z <= z1 + 8f; z += 20f)
                    {
                        var w = o + q * new Vector3(x, 0f, z);
                        var m = new GameObject("RR keepout").transform;
                        m.SetParent(keepT, false); m.position = w;
                        var lg = m.gameObject.AddComponent<LODGroup>();
                        lg.SetLODs(new[] { new LOD(0.01f, new Renderer[0]) });
                    }
                k++;
                d = dc + f.hw + 26f + (float)rng.NextDouble() * 14f;
            }
        }

        // second rank: a low-rise block (boutique / bungalow / condo) on the lawn BEHIND each front-rank resort,
        // offset sideways so it is seen between the front buildings. Cheap LODs (not hero) keep the tri budget.
        var r2Cycle = new[] { "Nagisa_B_Boutique2", "Nagisa_B_ResortBungalow", "Nagisa_B_Condo1", "Nagisa_B_Boutique1" };
        for (int ri = 0; ri < rank1.Count; ri++)
        {
            var (rdc, rback, rhw) = rank1[ri];
            string stem2 = r2Cycle[ri % r2Cycle.Length];
            var f2 = RrFootOf(stem2);
            float dc2 = rdc + (ri % 2 == 0 ? 1f : -1f) * (rhw * 0.5f + f2.hw * 0.5f + 6f);
            int i2 = _route.IndexAt(dc2);
            if (_route.OnBridge(i2)) continue;
            var in2 = RrInland(i2); var sd2 = -in2;
            float yaw2 = Mathf.Atan2(-sd2.x, -sd2.z) * Mathf.Rad2Deg;
            var q2 = Quaternion.Euler(0f, yaw2, 0f);
            float off2 = rback + 10f - f2.front;
            var o2 = _route.Position[i2] + in2 * off2;
            float h0 = float.MaxValue, h1 = float.MinValue; bool ok2 = true;
            for (float x = -f2.hw - 3f; x <= f2.hw + 3.01f && ok2; x += (2f * f2.hw + 6f) / 5f)
                for (float z = f2.front - 3f; z <= f2.back + 3.01f; z += (f2.back - f2.front + 6f) / 5f)
                {
                    var w = o2 + q2 * new Vector3(x, 0f, z);
                    if (_ground.Coast(w.x, w.z) < 6f || InAnyPad(w.x, w.z, 6f) || _route.PlanDistance(w.x, w.z, out _) < 44f) { ok2 = false; break; }
                    float h = _ground.Height(w.x, w.z); h0 = Mathf.Min(h0, h); h1 = Mathf.Max(h1, h);
                }
            if (!ok2 || h1 - h0 > RrMaxLotSlopeM) continue;
            o2.y = h1 + 0.14f;
            if (PlaceWorld(stem2, bldT, o2, yaw2, 1f, 0.0015f, NbColourway(ri * 5 + 2), hero: false) == null) continue;
            rank2++;
            for (float x = -f2.hw - 4f; x <= f2.hw + 4f; x += 20f)
                for (float z = f2.front - 4f; z <= f2.back + 6f; z += 20f)
                {
                    var m = new GameObject("RR keepout 2").transform;
                    m.SetParent(keepT, false); m.position = o2 + q2 * new Vector3(x, 0f, z);
                    m.gameObject.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.01f, new Renderer[0]) });
                }
            RrGrid(lawn, o2, q2, -f2.hw - 4f, f2.hw + 4f, f2.front - 3f, f2.back + 4f, 8f, 0.12f, 4f);
        }

        // frontage furniture: lamps every 32 m on the inland edge of the sidewalk, parked cars on the lane
        foreach (var span in new[] { (2700f, 4420f), (12260f, 14760f) })
            for (float d = span.Item1; d < span.Item2; d += 32f)
            {
                int i = _route.IndexAt(d);
                if (_route.OnBridge(i)) continue;
                var inl = RrInland(i);
                var w = _route.Position[i] + inl * (RrWalk1 - 0.7f);
                if (_ground.Coast(w.x, w.z) < 6f) continue;
                w.y = _ground.Height(w.x, w.z) + 0.06f;
                float yawS = Mathf.Atan2(-(-inl).x, -(-inl).z) * Mathf.Rad2Deg;
                if (PlaceWorld("Nagisa_S_StreetLight", furnT, w, yawS, 1f, 0.003f) != null) lamps++;
                if (rng.NextDouble() < 0.45)
                {
                    var tg = _route.Position[_route.IndexAt(d + 6f)] - _route.Position[i]; tg.y = 0f;
                    if (tg.sqrMagnitude < 1e-3f) continue;
                    var cp = _route.Position[_route.IndexAt(d + 14f)] + RrInland(i) * (RrLane1 - 1.3f);
                    cp.y = _ground.Height(cp.x, cp.z) + 0.04f;
                    string car = new[] { "Nagisa_S_CarKei", "Nagisa_S_CarSedan", "Nagisa_S_CarVan" }[rng.Next(3)];
                    if (PlaceWorld(car, furnT, cp, Mathf.Atan2(tg.x, tg.z) * Mathf.Rad2Deg, 1f, 0.003f, NbCarColour(rng.Next(8))) != null) cars++;
                }
            }

        RrFlush(landT, "RR frontage lane", lane, _nb["NB_Asphalt2"], false);
        RrFlush(landT, "RR frontage kerb", kerb, _nb["NB_Kerb"], false);
        RrFlush(landT, "RR frontage sidewalk - Beach Sand walk layer", walk, _nb["NB_Paving"], true);
        RrFlush(landT, "RR lot lawn", lawn, _nb["NB_Lawn"], false);
        RrFlush(landT, "RR forecourt", plaza, _nb["NB_PlazaStone"], false);
        try
        {
            System.IO.Directory.CreateDirectory("reference/good_graphics/nagisa_bay");
            System.IO.File.WriteAllLines("reference/good_graphics/nagisa_bay/nb2_lots.txt", lotLines);
        }
        catch (System.Exception e) { Debug.LogWarning("[nagisa-rr] could not write lots file: " + e.Message); }
        Debug.Log($"[nagisa-rr] resort row: {placed} buildings + {rank2} second-rank ({skipped} lot candidates skipped for slope/land), " +
                  $"{palms} lot palms, {lamps} lamps, {cars} parked cars; " + string.Join(", ", log));
    }

    // ---- generated ground ---------------------------------------------------------------------
    private sealed class RrMesh { public List<Vector3> v = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>(); }

    private static void RrFlush(Transform parent, string name, RrMesh m, Material mat, bool walkLayer)
    {
        if (m.t.Count == 0) return;
        FixWinding(m.v, m.t);
        var go = AddMesh(parent, name, Finish("Nagisa_" + name.Replace(' ', '_').Replace("-", ""), m.v, m.uv, m.t), mat, walkLayer);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void RrQuad(RrMesh m, Vector3 a, Vector3 b, Vector3 c, Vector3 e, float u0, float u1, float v0, float v1)
    {
        int n = m.v.Count;
        m.v.Add(a); m.v.Add(b); m.v.Add(c); m.v.Add(e);
        m.uv.Add(new Vector2(u0, v0)); m.uv.Add(new Vector2(u0, v1));
        m.uv.Add(new Vector2(u1, v0)); m.uv.Add(new Vector2(u1, v1));
        m.t.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 });
    }

    /// <summary>Ground-following grid in a lot's local frame (x right, z toward the lot's back).</summary>
    private static void RrGrid(RrMesh m, Vector3 o, Quaternion q, float x0, float x1, float z0, float z1,
                               float step, float lift, float tile)
    {
        int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / step)), nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / step));
        float sx = (x1 - x0) / nx, sz = (z1 - z0) / nz;
        Vector3 W(float x, float z)
        {
            var w = o + q * new Vector3(x, 0f, z);
            w.y = _ground.Height(w.x, w.z) + lift;
            return w;
        }
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                float xa = x0 + ix * sx, xb = xa + sx, za = z0 + iz * sz, zb = za + sz;
                var a = W(xa, za); var b = W(xa, zb); var c = W(xb, za); var e = W(xb, zb);
                if (_ground.Coast(a.x, a.z) < 2f || _ground.Coast(e.x, e.z) < 2f) continue;
                RrQuad(m, a, b, c, e, xa / tile, xb / tile, za / tile, zb / tile);
            }
    }

    /// <summary>Frontage lane + kerbs + sidewalk along route distance d0..d1 (follows the coast curve).</summary>
    private static void RrFrontage(float d0, float d1, RrMesh lane, RrMesh kerb, RrMesh walk)
    {
        const float step = 6f;
        for (float d = d0; d < d1; d += step)
        {
            int i = _route.IndexAt(d), j = _route.IndexAt(d + step);
            if (j <= i || _route.OnBridge(i) || _route.OnBridge(j)) continue;
            var ni = RrInland(i); var nj = RrInland(j);
            Vector3 P(int idx, Vector3 n, float off, float lift)
            {
                var p = _route.Position[idx] + n * off;
                p.y = _ground.Height(p.x, p.z) + lift;
                return p;
            }
            var pa = P(i, ni, RrLane0, 0.06f);
            if (_ground.Coast(pa.x, pa.z) < 4f) continue;
            float u0 = d / 6f, u1 = (d + step) / 6f;
            // lane
            RrQuad(lane, P(i, ni, RrLane0, 0.07f), P(i, ni, RrLane1, 0.07f), P(j, nj, RrLane0, 0.07f), P(j, nj, RrLane1, 0.07f),
                   u0, u1, 0f, 1.1f);
            // kerbs (two thin raised strips) + sidewalk deck
            RrQuad(kerb, P(i, ni, RrLane0 - 0.3f, 0.16f), P(i, ni, RrLane0, 0.16f), P(j, nj, RrLane0 - 0.3f, 0.16f), P(j, nj, RrLane0, 0.16f),
                   u0, u1, 0f, 0.3f);
            RrQuad(kerb, P(i, ni, RrLane1, 0.17f), P(i, ni, RrLane1 + 0.3f, 0.17f), P(j, nj, RrLane1, 0.17f), P(j, nj, RrLane1 + 0.3f, 0.17f),
                   u0, u1, 0f, 0.3f);
            RrQuad(walk, P(i, ni, RrLane1 + 0.3f, 0.17f), P(i, ni, RrWalk1, 0.17f), P(j, nj, RrLane1 + 0.3f, 0.17f), P(j, nj, RrWalk1, 0.17f),
                   u0 * 2.5f, u1 * 2.5f, 0f, 2f);
        }
    }
}

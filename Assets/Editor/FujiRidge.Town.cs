using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// FUJI RIDGE - EUROPEAN HILL TOWN (2026-09-26 user direction). The lower route climbs through a
/// Dolomites/Tuscan hill town (ochre and terracotta stucco, green shutters, arcades, espresso bar
/// and pasticceria terraces, a piazza with a fountain and campanile), then vineyards and cypress
/// rows, then a rifugio cafe stop full of cyclists near the treeline, then the volcanic slopes.
/// People are clones of the Minato crowd donors made through MapleCityLife's own Clone (called by
/// reflection so its civilian-look / helmet rules are reused unchanged, not forked or edited).
/// </summary>
public static partial class FujiRidgeEnvironment
{
    private const float TownFromM = 140f, TownToM = 1000f;
    private const float PiazzaM = 560f;
    private const float VineToM = 3600f;
    private const float RifugioM = 6150f;
    private const float WalkTopOffset = CorridorClearM + 2.6f; // facade line, 5.8 m from centre

    private struct Seat { public Vector3 Pos; public Quaternion Rot; }
    private static readonly List<Seat> _seats = new List<Seat>();
    private static readonly List<Vector3> _standSpots = new List<Vector3>();
    private static readonly List<(Vector3 a, Vector3 b)> _walkPaths = new List<(Vector3, Vector3)>();
    private static readonly List<(Vector3 p, Quaternion r)> _riderSpots = new List<(Vector3, Quaternion)>();

    private static float PavementY(FujiRoute route, int i) => route.Position[i].y + 0.12f;

    private static void BuildTownDressing(FujiRoute route, Func<int, PMesh> at, System.Random rnd)
    {
        _seats.Clear(); _standSpots.Clear(); _walkPaths.Clear(); _riderSpots.Clear(); _tableStanders.Clear();
        ResetHillTownKit(); // E1: GLB house placements (flushed by BuildPilgrimage after the detail tiles)
        float Rn() => (float)rnd.NextDouble();
        int houses = 0;

        // ---- pavements (both sides, whole town) ---------------------------------------------
        // copilot F2 (2026-09-26): one CONTINUOUS strip per side (shared station edges), not a
        // flat box per 4 m segment. The boxes stepped on the 7% grade and opened wedge gaps on
        // every bend (grey steps + black gaps in after_piazza_wide / after_close_cafe_town_a).
        // The kerb now sits at the carriageway edge, covering the dark cinder shoulder, and the
        // strip runs under the house fronts; the piazza is a sloped strip of its own. Both go in the shadowless far chunk: as shadow
        // CASTERS the flat paving self-shadowed into dark stripes under the low sun (acne).
        int iA = route.IndexAt(TownFromM), iB = route.IndexAt(TownToM);
        for (int s = -1; s <= 1; s += 2)
            PavementStrip(FarAt(route, iA), route, iA, iB, s, KerbOffsetM, PaveOuterM, 0f, PGranite, 0.62f, 0.72f, true);
        {
            // Piazza: sloped with the street, from just inside the pavement's outer edge to the back.
            int p0 = route.IndexAt(PiazzaM - PiazzaHalfLenM), p1 = route.IndexAt(PiazzaM + PiazzaHalfLenM);
            PavementStrip(FarAt(route, iA), route, p0, p1, -1, WalkTopOffset - 0.8f, PiazzaDepthM, PiazzaLiftM, PStucco, 0.55f, 0.66f, false, overTerrain: true);
        }

        // ---- houses both sides --------------------------------------------------------------
        for (int s = -1; s <= 1; s += 2)
        {
            float d = TownFromM + Rn() * 4f;
            while (d < TownToM)
            {
                float w = Mathf.Lerp(6.5f, 10f, Rn());
                bool piazza = s == -1 && d > PiazzaM - 45f && d < PiazzaM + 35f;
                if (!piazza)
                {
                    int i = route.IndexAt(d + w * 0.5f);
                    House(at(i), route, i, s, w, rnd, houses);
                    houses++;
                }
                d += w + (Rn() < 0.15f ? 3f : 0.1f);
            }
        }

        // ---- piazza, fountain, campanile ----------------------------------------------------
        {
            int i = route.IndexAt(PiazzaM);
            var m = at(i);
            var sf = -route.SideFlat(i);
            var fwd = Vector3.Cross(sf, Vector3.up).normalized; fwd = -fwd;
            var pc = route.Position[i] + sf * (CorridorClearM + 17f);
            pc.y = PaveY(route, pc, PiazzaLiftM, true);
            Vector3 OnPiazza(Vector3 q) { q.y = PaveY(route, q, PiazzaLiftM, true); return q; }
            // Fountain (basin sunk 0.25 m so the 7% piazza slope never shows a gap under it).
            if (!FujiKit2Fountain(pc))   // E1: Blender tiered fountain (GLB); the prisms below are the fallback
            {
                m.Prism(pc - Vector3.up * 0.25f, 2.6f, 2.6f, 0.85f, 12, PGranite, 0.5f, 0.7f);
                m.Prism(pc + Vector3.up * 0.1f, 2.3f, 2.3f, 0.45f, 12, PIndigo, 0.8f, 0.9f);
                m.Prism(pc, 0.35f, 0.25f, 1.8f, 8, PGranite, 0.5f, 0.7f, false);
                m.Prism(pc + Vector3.up * 1.8f, 0.4f, 1.1f, 0.35f, 10, PGranite, 0.55f, 0.75f);
                m.Prism(pc + Vector3.up * 2.15f, 0.15f, 0.05f, 0.7f, 6, PGranite, 0.6f, 0.8f, false);
            }
            // Campanile at the back of the piazza (copilot F3: FujiRidge.Campanile.cs).
            var cc = OnPiazza(pc + sf * 10f + fwd * 12f);
            if (!FujiKit2Campanile(cc, -sf)) Campanile(m, cc, fwd, sf, rnd);   // E1: GLB campanile, procedural fallback
            // Cafe terrace on the piazza with a big umbrella per table.
            for (int k = 0; k < 6; k++)
            {
                var tp = OnPiazza(pc - sf * 6f + fwd * ((k - 2.5f) * 4.2f));
                CafeTable(m, tp, fwd, sf, true, rnd, k % 2 == 0 ? PRed : PShutter);
            }
            for (int k = 0; k < 18; k++)
                _standSpots.Add(OnPiazza(pc + fwd * (Rn() - 0.5f) * 50f + sf * (Rn() * 10f - 2f)));
            for (int k = 0; k < 4; k++)
                BikeProp(m, OnPiazza(pc - sf * 3.4f + fwd * (8f + k * 0.7f)), sf, -0.18f);
            // fuji2: market stalls along the back of the piazza and a gelato cart by the fountain.
            for (int k = 0; k < 5; k++)
                MarketStall(DetailAt(pc), OnPiazza(pc + sf * 9.5f + fwd * (-24f + k * 5.2f)), fwd, sf, k, rnd);
            GelatoCart(DetailAt(pc), OnPiazza(pc + fwd * 5.5f - sf * 2.5f), fwd, sf);
            _standSpots.Add(OnPiazza(pc + fwd * 5.5f - sf * 3.6f));   // queue at the gelato cart
            _standSpots.Add(OnPiazza(pc + fwd * 4.7f - sf * 4.1f));
        }

        // ---- vineyards and cypress rows -----------------------------------------------------
        for (int i = route.IndexAt(TownToM + 30f); i < route.IndexAt(VineToM) - 1; i++)
        {
            float d = route.Distance[i];
            var m = at(i);
            if (i % 3 == 0)
                for (int s = -1; s <= 1; s += 2)
                    if (Mathf.PerlinNoise(d * 0.004f, s * 3.3f) > 0.35f)
                    {
                        Foot(route, i, s * (CorridorClearM + 2.2f + Rn() * 0.6f), out var f, out _);
                        float cypH = Mathf.Lerp(8f, 14f, Rn());
                        if (!FujiKit2Cypress(f, cypH, rnd)) Cypress(m, f, cypH, rnd);
                    }
            for (int s = -1; s <= 1; s += 2)
            {
                if (Mathf.PerlinNoise(d * 0.003f + 11f, s * 5.1f) < 0.42f) continue;
                for (float o = 8f; o < 36f; o += 2.3f)
                {
                    Foot(route, i, s * o, out var f0, out _);
                    Foot(route, i + 1, s * o, out var f1, out _);
                    var along = f1 - f0;
                    var sf = route.SideFlat(i) * s;
                    if (FujiKit2VineRow(f0, f1, rnd)) continue;   // E1: GLB trellis bays (posts, wires, canopy, grapes)
                    m.OBox((f0 + f1) * 0.5f + Vector3.up * 0.55f, along * 0.5f, Vector3.up * 0.55f, sf * 0.28f,
                           PVine, Mathf.Lerp(0.5f, 0.85f, Rn()), 0.9f);
                    if (i % 2 == 0)
                        m.OBox(f0 + Vector3.up * 0.7f, along.normalized * 0.04f, Vector3.up * 0.8f, sf * 0.04f, PTimber, 0.5f);
                }
            }
        }

        // ---- rifugio cafe stop --------------------------------------------------------------
        {
            int i = route.IndexAt(RifugioM);
            var m = at(i);
            int side = 1;
            { Foot(route, i, 9f, out var a, out _); Foot(route, i, -9f, out var b, out _); side = a.y > b.y ? -1 : 1; }
            var sf = route.SideFlat(i) * side;
            var fwd = Vector3.Cross(sf, Vector3.up).normalized;
            float top = route.Position[i].y + 0.15f;
            var terr = route.Position[i] + sf * (CorridorClearM + 8f); terr.y = top;
            m.OBox(terr - Vector3.up * 2f, fwd * 16f, Vector3.up * 2f, sf * 7.5f, PTimber, 0.5f, 0.62f);   // deck
            var hc = terr + sf * 12f;
            m.OBox(hc + Vector3.up * 1.6f, fwd * 9f, Vector3.up * 1.6f, sf * 5f, PGranite, 0.5f);          // stone ground floor
            m.OBox(hc + Vector3.up * 4.6f, fwd * 9f, Vector3.up * 1.4f, sf * 5f, PTimber, 0.7f);           // timber upper
            for (int s = -1; s <= 1; s += 2)
            {
                var slope = (sf * s + Vector3.up * -0.6f).normalized;
                float half = 6.4f / Mathf.Cos(Mathf.Atan(0.6f)) * 0.5f;
                var n = Vector3.Cross(fwd, slope).normalized; if (n.y < 0f) n = -n;
                m.OBox(hc + Vector3.up * (6f + 6.4f * 0.6f * 0.5f) + slope * half, fwd * 10f, n * 0.14f, slope * half, PRoof, 0.5f, 0.8f);
            }
            for (int k = -3; k <= 3; k++)
            {
                var wc = hc - sf * 5.02f + fwd * (k * 2.4f);
                m.OBox(wc + Vector3.up * 4.6f, fwd * 0.5f, Vector3.up * 0.55f, sf * 0.03f, PBlack, 0.3f);
                m.OBox(wc + Vector3.up * 4.6f - fwd * 0.75f, fwd * 0.22f, Vector3.up * 0.6f, sf * 0.04f, PRed, 0.7f);
                m.OBox(wc + Vector3.up * 4.6f + fwd * 0.75f, fwd * 0.22f, Vector3.up * 0.6f, sf * 0.04f, PRed, 0.7f);
                m.OBox(wc + Vector3.up * 3.35f - sf * 0.4f, fwd * 0.9f, Vector3.up * 0.12f, sf * 0.4f, PTerracotta, 0.7f); // flower box
            }
            m.OBox(hc - sf * 5.05f + Vector3.up * 2.9f, fwd * 3.2f, Vector3.up * 0.35f, sf * 0.05f, PCream, 0.9f); // "RIFUGIO" board
            for (int k = 0; k < 8; k++)
            {
                var tp = terr + fwd * ((k % 4 - 1.5f) * 6.5f) + sf * (k < 4 ? -3.5f : 2f);
                CafeTable(m, tp, fwd, sf, true, rnd, k % 2 == 0 ? PRed : PWhite);
            }
            // Bike rack along the verge, bikes leaning.
            for (int k = 0; k < 14; k++)
            {
                var bp = terr - sf * 7.1f + fwd * ((k - 6.5f) * 1.1f);
                BikeProp(m, bp, sf, 0f);
            }
            m.OBox(terr - sf * 7.4f + Vector3.up * 0.5f, fwd * 8f, Vector3.up * 0.04f, sf * 0.04f, PBlack, 0.4f);
            for (int k = 0; k < 10; k++)
                _standSpots.Add(terr + fwd * (Rn() - 0.5f) * 28f + sf * (Rn() * 6f - 5.5f));
            // copilot F5: the six roster riders' own bikes lean on a hitching rail that continues
            // the rack line; each rider stands beside their bike (FujiRidge.PeopleLod.cs).
            _rifugioSf = sf; _rifugioFwd = fwd; _rifugioTop = top;
            float[] bikeAlong = { -12.4f, -10.4f, -8.6f, 8.6f, 10.4f, 12.4f };
            for (int k = 0; k < bikeAlong.Length; k++)
                _riderSpots.Add((terr - sf * RifugioBikeLineM + fwd * bikeAlong[k], Quaternion.LookRotation(k % 2 == 0 ? fwd : -fwd)));
        }

        Debug.Log($"[fuji] hill town: {houses} houses, {_seats.Count} cafe seats, {_standSpots.Count} stand spots, " +
                  $"{_walkPaths.Count} walk paths.");
    }

    private static void House(PMesh m, FujiRoute route, int i, int s, float w, System.Random rnd, int idx)
    {
        float Rn() => (float)rnd.NextDouble();
        var sf = route.SideFlat(i) * s;
        var fwd = Vector3.Cross(sf, Vector3.up).normalized;
        float depth = Mathf.Lerp(7f, 10f, Rn());
        float floors = 2 + rnd.Next(3);
        float fh = 3.1f;
        float H = floors * fh + 0.6f;
        float top = PavementY(route, i);
        var front = route.Position[i] + sf * WalkTopOffset; front.y = top;
        Vector3 OnPave(Vector3 q) { q.y = PaveY(route, q, 0f); return q; }
        var c = front + sf * (depth * 0.5f);
        int[] rows = { POchre, PTerracotta, PStucco, POchre, PStucco };
        int row = rows[rnd.Next(rows.Length)];
        float br = Mathf.Lerp(0.6f, 0.92f, Rn());
        // E1 (2026-09-28): a hero GLB townhouse from the Blender kit (FujiRidge.HillTownKit.cs,
        // tools/blender/build_fuji_hilltown.py) replaces the box body, windows, roof and shop
        // front. The street furniture below - laundry lines, cafe terraces, parked bikes and the
        // walk path - is kept unchanged, so town life still finds its seats and paths.
        bool kit = TryKitHouse(front, sf, w, idx % 5, rnd, out int kitFloors);
        if (kit) { floors = kitFloors; H = floors * fh + 0.6f; }
        if (!kit)
        {
        // Body with a deep plinth so a sloping street never shows a gap.
        m.OBox(c + Vector3.up * (H * 0.5f - 1.5f), fwd * (w * 0.5f), Vector3.up * (H * 0.5f + 1.5f), sf * (depth * 0.5f), row, br);
        m.OBox(c + Vector3.up * 0.3f - sf * 0.02f, fwd * (w * 0.5f + 0.02f), Vector3.up * 0.9f, sf * (depth * 0.5f), PGranite, 0.45f); // base band
        m.OBox(c + Vector3.up * (H + 0.1f), fwd * (w * 0.5f + 0.35f), Vector3.up * 0.18f, sf * (depth * 0.5f + 0.35f), PCream, 0.8f); // cornice
        // Low hipped terracotta roof.
        m.Prism(c + Vector3.up * (H + 0.25f), Mathf.Max(w, depth) * 0.68f, 0.3f, 2.0f, 4, PTerracotta, 0.55f, 0.8f, false);
        }
        var fn = -sf;
        var facade = front - sf * 0.02f;
        // Upper floors: windows with shutters and sills, balconies on some.
        int cols = Mathf.Max(1, Mathf.FloorToInt(w / 2.4f));
        if (!kit)
        for (int f = 1; f < floors; f++)
        for (int k = 0; k < cols; k++)
        {
            var wc = facade + fwd * ((k - (cols - 1) * 0.5f) * (w / cols)) + Vector3.up * (f * fh + 1.5f);
            m.OBox(wc, fwd * 0.45f, Vector3.up * 0.75f, fn * 0.04f, PBlack, 0.22f);
            bool open = Rn() < 0.5f;
            for (int q = -1; q <= 1; q += 2)
                m.OBox(wc + fwd * q * (open ? 0.78f : 0.24f) + fn * 0.06f, fwd * (open ? 0.3f : 0.23f), Vector3.up * 0.78f, fn * 0.03f, PShutter, Mathf.Lerp(0.5f, 0.85f, Rn()));
            m.OBox(wc - Vector3.up * 0.85f + fn * 0.12f, fwd * 0.55f, Vector3.up * 0.06f, fn * 0.14f, PStucco, 0.9f);
            if (Rn() < 0.55f) // flower box (fuji2: 30% -> 55%)
                m.OBox(wc - Vector3.up * 0.7f + fn * 0.22f, fwd * 0.5f, Vector3.up * 0.12f, fn * 0.1f, PAutumn, 0.85f);
        }
        // fuji2: laundry line on brackets across some upper facades.
        if (floors >= 3 && Rn() < 0.28f)
        {
            float ly = 2f * fh + 0.35f;
            var a = facade + fn * 0.55f - fwd * (w * 0.4f) + Vector3.up * ly;
            var b = facade + fn * 0.55f + fwd * (w * 0.4f) + Vector3.up * ly;
            var dm = DetailAt(front);
            dm.OBox((a + b) * 0.5f, (b - a) * 0.5f, Vector3.up * 0.008f, fn * 0.008f, PBlack, 0.5f);
            dm.OBox(a - fn * 0.27f, fn * 0.28f, Vector3.up * 0.02f, fwd * 0.02f, PBlack, 0.4f);
            dm.OBox(b - fn * 0.27f, fn * 0.28f, Vector3.up * 0.02f, fwd * 0.02f, PBlack, 0.4f);
            int[] cloth = { PWhite, PRed, PIndigo, PCream, PShutter, POchre };
            for (float t = 0.08f; t < 0.92f; t += 0.11f + Rn() * 0.08f)
            {
                var cpos = Vector3.Lerp(a, b, t);
                float cw = Mathf.Lerp(0.18f, 0.34f, Rn()), ch = Mathf.Lerp(0.25f, 0.5f, Rn());
                dm.OBox(cpos - Vector3.up * ch, fwd * cw, Vector3.up * ch, fn * 0.01f, cloth[rnd.Next(cloth.Length)], Mathf.Lerp(0.7f, 0.95f, Rn()));
            }
        }
        // Ground floor: shop / cafe / arcade.
        int kind = idx % 5;
        if (kind == 2)
        {
            // Arcade: pillars and arches in front of a shadowed loggia.
            if (!kit)
            {
            m.OBox(facade + Vector3.up * 1.6f, fwd * (w * 0.5f - 0.1f), Vector3.up * 1.6f, fn * 0.05f, PBlack, 0.35f);
            int bays = Mathf.Max(2, Mathf.RoundToInt(w / 3f));
            for (int k = 0; k <= bays; k++)
                m.OBox(facade + fwd * ((k / (float)bays - 0.5f) * w) + fn * 0.3f + Vector3.up * 1.6f, fwd * 0.25f, Vector3.up * 1.6f, fn * 0.25f, row, br * 0.9f);
            m.OBox(facade + fn * 0.3f + Vector3.up * 3.4f, fwd * (w * 0.5f), Vector3.up * 0.3f, fn * 0.3f, row, br);
            }
            if (Rn() < 0.6f) BikeProp(m, OnPave(front + fn * 0.85f + fwd * (w * 0.3f)), fwd, 0.22f); // fuji2: parked on its stand
        }
        else
        {
            // Shop window + door + awning + sign.
            int aw = kind == 0 ? PRed : kind == 1 ? PShutter : kind == 3 ? PCream : PTerracotta;
            if (!kit)
            {
            m.OBox(facade + Vector3.up * 1.5f, fwd * (w * 0.32f), Vector3.up * 1.1f, fn * 0.05f, PIndigo, 0.35f);
            m.OBox(facade + fwd * (w * 0.4f) + Vector3.up * 1.2f, fwd * 0.5f, Vector3.up * 1.2f, fn * 0.05f, PTimber, 0.4f);
            var ac = facade + fn * 1.0f + Vector3.up * 2.95f;
            var slope = (fn + Vector3.up * -0.45f).normalized;
            var nrm = Vector3.Cross(fwd, slope).normalized; if (nrm.y < 0f) nrm = -nrm;
            m.OBox(ac, fwd * (w * 0.42f), nrm * 0.03f, slope * 1.05f, aw, 0.8f, 0.9f);
            m.OBox(facade + Vector3.up * 3.55f + fn * 0.05f, fwd * (w * 0.3f), Vector3.up * 0.28f, fn * 0.04f, PCream, 0.95f);
            }
            // Cafe / bakery terraces on the pavement for espresso bars (0) and pasticcerias (3).
            if (kind == 0 || kind == 3)
            {
                for (int k = -1; k <= 1; k += 2)
                    CafeTable(m, OnPave(front + fn * 1.45f + fwd * (k * w * 0.25f)), fwd, -fn, false, rnd, aw);
                BikeProp(m, OnPave(front + fn * 0.3f + fwd * (w * 0.5f - 0.6f)), -fn, -0.2f);
                BikeProp(m, OnPave(front + fn * 0.3f + fwd * (w * 0.5f - 1.3f)), -fn, -0.2f);
            }
        }
        // Walk path along this frontage.
        var pa = OnPave(front + fn * 1.2f - fwd * (w * 0.45f));
        var pb = OnPave(front + fn * 1.2f + fwd * (w * 0.45f));
        _walkPaths.Add((pa, pb));
    }

    /// <summary>Round table + chairs (+ umbrella). Records seats facing the table.</summary>
    private static void CafeTable(PMesh m, Vector3 p, Vector3 fwd, Vector3 sf, bool umbrella, System.Random rnd, int umbrellaRow)
    {
        // fuji2: small furniture goes to the shadowless, distance-culled detail tiles.
        m = DetailAt(p);
        m.Prism(p, 0.05f, 0.05f, 0.72f, 5, PBlack, 0.4f, 0.5f, false);
        m.Prism(p + Vector3.up * 0.72f, 0.38f, 0.38f, 0.04f, 10, PWhite, 0.85f, 0.95f);
        // fuji2 close-up fix: the chair now matches the Minato sit donor's bench frame
        // (MinatoCrowdPopulation.BenchSeatHeight 0.38 m, seat centre 0.08 m in front of the
        // figure origin, backrest ~0.2 m behind it). The old 0.47 m seat buried the hips 9 cm.
        const float SeatTop = 0.38f;
        for (int k = -1; k <= 1; k += 2)
        {
            var cp = p + fwd * (k * 0.66f);                       // seat centre
            var away = fwd * k;                                    // from the table towards the backrest
            m.OBox(cp + Vector3.up * (SeatTop - 0.025f), fwd * 0.2f, Vector3.up * 0.025f, sf * 0.21f, PTimber, 0.6f);
            m.OBox(cp + away * 0.24f + Vector3.up * (SeatTop + 0.24f), fwd * 0.02f, Vector3.up * 0.26f, sf * 0.2f, PTimber, 0.5f);
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
                m.OBox(cp + fwd * a * 0.17f + sf * b * 0.17f + Vector3.up * ((SeatTop - 0.05f) * 0.5f), fwd * 0.015f,
                       Vector3.up * ((SeatTop - 0.05f) * 0.5f), sf * 0.015f, PBlack, 0.4f);
            if (rnd.NextDouble() < 0.8)
            {
                var origin = cp + away * 0.08f;
                _seats.Add(new Seat { Pos = origin, Rot = Quaternion.LookRotation(-away, Vector3.up) });
                // Cup + saucer on the table in front of the customer.
                var cup = p - away * 0.2f + sf * (((float)rnd.NextDouble() - 0.5f) * 0.2f) + Vector3.up * 0.76f;
                m.Prism(cup, 0.065f, 0.065f, 0.012f, 8, PWhite, 0.9f, 0.95f);
                m.Prism(cup, 0.035f, 0.04f, 0.06f, 8, rnd.NextDouble() < 0.3 ? PRed : PWhite, 0.85f, 0.95f);
                // Sometimes a bag on the ground beside the chair.
                if (rnd.NextDouble() < 0.35)
                {
                    int bagRow = rnd.NextDouble() < 0.5 ? PTimber : (rnd.NextDouble() < 0.5 ? PIndigo : PRed);
                    var bp = cp + sf * 0.36f * (rnd.NextDouble() < 0.5 ? -1 : 1) + Vector3.up * 0.13f;
                    m.OBox(bp, fwd * 0.16f, Vector3.up * 0.13f, sf * 0.07f, bagRow, 0.55f);
                }
            }
        }
        if (umbrella)
        {
            m.Prism(p + Vector3.up * 0.76f, 0.03f, 0.03f, 1.6f, 4, PWhite, 0.8f, 0.8f, false);
            m.Prism(p + Vector3.up * 2.1f, 1.5f, 0.05f, 0.45f, 8, umbrellaRow, 0.75f, 0.95f, false);
        }
        // Every ~4th table has someone standing beside it (waiter / friend): a different body.
        if (rnd.NextDouble() < 0.28)
            _tableStanders.Add((p + sf * 0.75f, Quaternion.LookRotation(-sf, Vector3.up)));
    }

    private static readonly List<(Vector3 p, Quaternion r)> _tableStanders = new List<(Vector3, Quaternion)>();

    /// <summary>A simple parked road bike (thin boxes), leaning by <paramref name="lean"/> rad.</summary>
    private static void BikeProp(PMesh m, Vector3 p, Vector3 facing, float lean)
    {
        m = DetailAt(p);
        var fwd = new Vector3(facing.x, 0f, facing.z).normalized;
        var right = Vector3.Cross(Vector3.up, fwd);
        var up = (Vector3.up + right * lean).normalized;
        const float Rw = 0.34f;
        for (int wIdx = -1; wIdx <= 1; wIdx += 2)
        {
            var wc = p + fwd * (wIdx * 0.5f) + up * Rw;
            for (int k = 0; k < 10; k++)
            {
                float a0 = k * Mathf.PI * 2f / 10f, a1 = (k + 1) * Mathf.PI * 2f / 10f;
                var q0 = wc + (fwd * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * Rw;
                var q1 = wc + (fwd * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * Rw;
                m.OBox((q0 + q1) * 0.5f, (q1 - q0) * 0.5f, Vector3.Cross(right, (q1 - q0).normalized) * 0.02f, right * 0.012f, PBlack, 0.3f);
            }
        }
        var rear = p - fwd * 0.5f + up * Rw; var front = p + fwd * 0.5f + up * Rw;
        var seat = p - fwd * 0.18f + up * 0.95f; var bar = p + fwd * 0.38f + up * 0.98f;
        var bb = p + up * 0.3f;
        void Tube(Vector3 a, Vector3 b, int row)
        {
            var d = (b - a) * 0.5f;
            var n = Vector3.Cross(right, d.normalized);
            m.OBox((a + b) * 0.5f, d, n * 0.018f, right * 0.018f, row, 0.75f);
        }
        Tube(rear, bb, PRed); Tube(bb, seat, PRed); Tube(seat, bar, PRed); Tube(bb, bar, PRed);
        Tube(bar, front, PBlack); Tube(rear, seat, PRed);
        m.OBox(bar + up * 0.03f, fwd * 0.03f, up * 0.02f, right * 0.21f, PBlack, 0.3f);
        m.OBox(seat + up * 0.03f, fwd * 0.12f, up * 0.025f, right * 0.04f, PBlack, 0.3f);
    }

    private static void Cypress(PMesh m, Vector3 f, float h, System.Random rnd)
    {
        m.Prism(f - Vector3.up * 0.2f, 0.12f, 0.1f, h * 0.2f, 5, PBark, 0.35f, 0.5f, false);
        float r = h * 0.11f;
        m.Blob(f + Vector3.up * (h * 0.45f), r, 4.2f, PCypress, 0.75f, rnd, 0.18f);
        m.Blob(f + Vector3.up * (h * 0.78f), r * 0.55f, 4.0f, PCypress, 0.9f, rnd, 0.15f);
    }

    // =================================================================== people

    private static void BuildTownLife(Transform group, FujiRoute route)
    {
        var life = new GameObject("Fuji Town Life").transform;
        life.SetParent(group, false);

        var clone = typeof(MapleCityLife).GetMethod("Clone", BindingFlags.NonPublic | BindingFlags.Static);
        if (clone == null) { Debug.LogWarning("[fuji] MapleCityLife.Clone not found - town is unpopulated."); return; }

        var walkers = new List<MinatoCrowdActor>(); var standers = new List<MinatoCrowdActor>(); var sitters = new List<MinatoCrowdActor>();
        var seen = new HashSet<string>();
        foreach (var a in UnityEngine.Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                             .OrderBy(x => x.name, StringComparer.Ordinal))
        {
            if (a == null || a.transform.IsChildOf(group)) continue;
            if (!a.name.StartsWith("Crowd_", StringComparison.Ordinal)) continue;
            if (a.transform.Find("LOD0 High Skinned/Rigged Character") == null) continue;
            if (!seen.Add(a.name)) continue;
            switch (a.motion)
            {
                case MinatoCrowdActor.MotionKind.Walk: walkers.Add(a); break;
                case MinatoCrowdActor.MotionKind.Idle:
                case MinatoCrowdActor.MotionKind.Wave: standers.Add(a); break;
                case MinatoCrowdActor.MotionKind.Sit: sitters.Add(a); break;
            }
        }
        if (walkers.Count + standers.Count + sitters.Count == 0)
        { Debug.LogWarning("[fuji] no Crowd_ donors in scene - town unpopulated."); return; }

        var rnd = new System.Random(4242);
        int made = 0;
        var people = new List<GameObject>();   // copilot F1: every clone, LOD-wrapped at the end
        GameObject Make(List<MinatoCrowdActor> pool, string name, Vector3 pos, Quaternion rot,
                        MinatoCrowdActor.MotionKind kind, float speed)
        {
            if (pool.Count == 0) return null;
            var src = pool[rnd.Next(pool.Count)];
            object[] args = { src, life, name, pos, rot, Mathf.Lerp(0.94f, 1.05f, (float)rnd.NextDouble()),
                              kind, speed, (float)rnd.NextDouble(), null, 0f };
            try
            {
                var go = (GameObject)clone.Invoke(null, args);
                float bottom = (float)args[10];
                if (go != null && kind != MinatoCrowdActor.MotionKind.Sit)
                    go.transform.position -= Vector3.up * bottom;
                made++;
                if (go != null) people.Add(go);
                return go;
            }
            catch (Exception e) { Debug.LogWarning($"[fuji] clone failed: {e.InnerException?.Message ?? e.Message}"); return null; }
        }

        // fuji2 seated variety: the scene has ONE sit donor body (Crowd_06_Sit_Sena, 6 civilian
        // looks). Neighbours now differ by scale (0.9-1.1, hip height compensated so the seat
        // contact stays at 0.38 m), a few degrees of yaw, hats on ~40%, cups/bags on the tables
        // (CafeTable) and a standing waiter/friend with a different body at ~28% of tables.
        var hats = HatMeshes();
        int hatCount = 0;
        foreach (var s in _seats)
        {
            bool sit = sitters.Count > 0;
            float sc = Mathf.Lerp(0.9f, 1.1f, (float)rnd.NextDouble());
            var rot = s.Rot * Quaternion.Euler(0f, ((float)rnd.NextDouble() - 0.5f) * 16f, 0f);
            var pos = s.Pos + Vector3.up * (sit ? SeatDonorTop * (1f - sc) : 0f);
            var go = Make(sit ? sitters : standers, $"Fuji Cafe Customer {made}", pos, rot,
                          sit ? MinatoCrowdActor.MotionKind.Sit : MinatoCrowdActor.MotionKind.Idle, 0f);
            if (go == null) continue;
            go.transform.localScale = Vector3.one * sc;
            if (sit) go.transform.position = pos;
            if (hats != null && rnd.NextDouble() < 0.4 && AddHat(go, hats[rnd.Next(hats.Length)])) hatCount++;
        }
        foreach (var (p, r) in _tableStanders)
        {
            var go = Make(standers.Count > 0 ? standers : walkers, $"Fuji Cafe Stander {made}", p, r, MinatoCrowdActor.MotionKind.Idle, 0f);
            if (go != null && hats != null && rnd.NextDouble() < 0.3 && AddHat(go, hats[rnd.Next(hats.Length)])) hatCount++;
        }
        foreach (var p in _standSpots)
            Make(standers.Count > 0 ? standers : walkers, $"Fuji Stroller {made}", p,
                 Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), MinatoCrowdActor.MotionKind.Idle, 0f);
        for (int k = 0; k < _walkPaths.Count; k++)
        {
            if (k % 2 == 1 && rnd.NextDouble() < 0.5) continue;
            var (a, b) = _walkPaths[k];
            if (rnd.NextDouble() < 0.5) (a, b) = (b, a);
            var go = Make(walkers.Count > 0 ? walkers : standers, $"Fuji Walker {made}", a,
                          Quaternion.LookRotation(Flat3(b - a)), MinatoCrowdActor.MotionKind.Walk, Mathf.Lerp(0.9f, 1.4f, (float)rnd.NextDouble()));
            if (go == null) continue;
            var actor = go.GetComponent<MinatoCrowdActor>();
            var rig = go.transform.Find("LOD0 High Skinned/Rigged Character");
            if (actor != null && rig != null)
                actor.Configure(MinatoCrowdActor.MotionKind.Walk, rig, go.transform.position,
                                go.transform.position + (b - a), actor.moveSpeed, (float)rnd.NextDouble());
        }

        // copilot F4: log one crowd clone's bone names once, so the hat lookup is auditable.
        if (people.Count > 0) LogBoneNames(people[0]);

        // copilot F5: the rifugio riders are no longer frozen mid-pedal on an invisible bike
        // stand. Each roster rider's own bike leans on a hitching rail; the rider (a Minato
        // dismounted-cyclist donor in kit) stands beside it. See FujiRidge.PeopleLod.cs.
        int riders = BuildRifugioRiders(life, standers.Count > 0 ? standers : walkers, people, rnd);

        // copilot F1: LOD every clone through a wrapper LODGroup (skinned+shadows near, static
        // no-shadow mid, clustered proxy far, culled beyond PeopleCullM).
        var lodStats = WrapPeopleLods(life, people);
        Debug.Log($"[fuji] town life: {hatCount} hats (failures: {HatFailures()}), {_tableStanders.Count} table standers.");
        Debug.Log($"[fuji] town life: {made} people ({walkers.Count}/{standers.Count}/{sitters.Count} donor walk/stand/sit), {riders} stopped riders.");
        Debug.Log($"[fuji] F1 people LOD: {lodStats}");
    }
    /// <summary>Sit donor seat contact height (MinatoCrowdPopulation.BenchSeatHeight) at scale 1.</summary>
    private const float SeatDonorTop = 0.38f;

    /// <summary>Unit hats (crown radius 1) in three palette colours: straw, black, red.</summary>
    private static Mesh[] HatMeshes()
    {
        var list = new List<Mesh>();
        foreach (var (name, row, b) in new[] { ("Straw", PCream, 0.8f), ("Black", PBlack, 0.7f), ("Red", PRed, 0.6f) })
        {
            var m = new PMesh();
            m.Prism(Vector3.zero, 1.6f, 1.55f, 0.08f, 12, row, b * 0.8f, b);                 // brim
            m.Prism(Vector3.up * 0.06f, 1.02f, 0.92f, 0.85f, 12, row, b * 0.85f, b);       // crown
            m.Prism(Vector3.up * 0.1f, 1.03f, 1.03f, 0.18f, 12, row == PCream ? PRed : PWhite, 0.6f, 0.7f, false); // band
            var mesh = Finish($"Fuji_Hat_{name}", m.V.ToArray(), m.UV.ToArray(), m.T);
            mesh = SaveMeshInPlace(mesh, $"{MeshDir}/{mesh.name}.asset");   // F4: GUID-stable
            list.Add(mesh);
        }
        return list.ToArray();
    }

    private static void MarketStall(PMesh m, Vector3 c, Vector3 fwd, Vector3 sf, int k, System.Random rnd)
    {
        float Rn() => (float)rnd.NextDouble();
        // Four posts, a counter of crates, a striped sloping canopy.
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
            m.OBox(c + fwd * a * 1.8f + sf * b * 0.9f + Vector3.up * 1.1f, fwd * 0.04f, Vector3.up * 1.35f, sf * 0.04f, PTimber, 0.45f);
        m.OBox(c - sf * 0.5f + Vector3.up * 0.35f, fwd * 1.75f, Vector3.up * 0.55f, sf * 0.4f, PTimber, 0.6f, 0.7f);
        int[] goods = { PAutumn, PVine, PRed, POchre, PShrub };
        for (int q = 0; q < 6; q++)
        {
            var gp = c - sf * 0.5f + fwd * (-1.45f + q * 0.58f) + Vector3.up * 0.95f;
            m.OBox(gp, fwd * 0.26f, Vector3.up * 0.06f, sf * 0.3f, goods[(q + k) % goods.Length], Mathf.Lerp(0.6f, 0.95f, Rn()));
        }
        var slope = (-sf + Vector3.up * 0.3f).normalized;
        var n = Vector3.Cross(fwd, slope).normalized; if (n.y < 0f) n = -n;
        int stripeA = k % 2 == 0 ? PRed : PShutter;
        for (int st = 0; st < 6; st++)
        {
            var sc = c + Vector3.up * 2.45f + fwd * (-1.65f + st * 0.66f);
            m.OBox(sc, fwd * 0.33f, n * 0.02f, slope * 1.1f, st % 2 == 0 ? stripeA : PWhite, 0.85f);
        }
    }

    private static void GelatoCart(PMesh m, Vector3 c, Vector3 fwd, Vector3 sf)
    {
        m.OBox(c + Vector3.up * 0.6f, fwd * 0.8f, Vector3.up * 0.4f, sf * 0.45f, PCream, 0.95f);
        m.OBox(c + Vector3.up * 1.03f, fwd * 0.75f, Vector3.up * 0.03f, sf * 0.4f, PWhite, 0.9f);
        int[] tubs = { PRed, PCream, PShrub, POchre, PAutumn, PWhite };
        for (int q = 0; q < 6; q++)
            m.OBox(c + Vector3.up * 1.07f + fwd * (-0.55f + (q % 3) * 0.55f) + sf * ((q / 3) * 0.36f - 0.18f),
                   fwd * 0.2f, Vector3.up * 0.03f, sf * 0.14f, tubs[q], 0.95f);
        for (int w = -1; w <= 1; w += 2)
            m.Prism(c + fwd * w * 0.6f + sf * 0.47f + Vector3.up * 0.22f, 0.22f, 0.22f, 0.04f, 10, PBlack, 0.3f, 0.35f);
        m.Prism(c + Vector3.up * 1.06f, 0.03f, 0.03f, 1.3f, 4, PWhite, 0.8f, 0.8f, false);
        m.Prism(c + Vector3.up * 2.3f, 1.2f, 0.05f, 0.4f, 8, PShutter, 0.8f, 0.95f, false);
        m.OBox(c + Vector3.up * 1.35f - sf * 0.46f, fwd * 0.5f, Vector3.up * 0.14f, sf * 0.02f, PRed, 0.9f); // sign
    }

    private static Vector3 Flat3(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }
}

using UnityEngine;

// Tuna port, part 8b: the tuna-cutting show, quay dressing, moorings, the fishermen's shrine
// and the breakwater anglers (see TunaPort.Detail.cs).
public static partial class ShiosaiCoastEnvironment
{
    /// <summary>MAGURO KAITAI SHOW on the apron's east end: a steel table with a bluefin half
    /// broken down (loins, belly, head), the cutter with the long knife, a canopy, the red banner,
    /// and a ring of shoppers watching.</summary>
    private static void TunaCuttingShow(PortBins big, PortBins small, PFrame f, System.Random rng, PortCast cast, Transform people)
    {
        const float a = 57f, n = 25.5f;
        big.SetChunk(a); small.SetChunk(a);
        float y = f.RoadY(a) + 0.12f;
        var c = f.W(a, n, y);
        var up = Vector3.up;
        // stainless cutting table
        PBox(big[TPSteelGrey], c + up * 0.88f, f.T * 1.7f, up * 0.04f, f.N * 0.6f);
        foreach (int sa in new[] { -1, 1 })
            foreach (int sn in new[] { -1, 1 })
                PCyl(small[TPSteelGrey], c + f.T * (sa * 1.6f) + f.N * (sn * 0.5f), up, 0.035f, 0.035f, 0.86f, 8, true);
        PBox(small[TPWoodDark], c + up * 0.935f, f.T * 1.45f, up * 0.015f, f.N * 0.48f);           // cutting board
        float top = y + 0.95f;
        // the carcass: the body with the upper loin gone, red meat exposed; the head set aside
        PTuna(small, c + up * 0.1f - f.T * 1.2f + f.N * 0.1f, f.T, up, f.N, 2.2f, false, top);
        PBox(small[TPTunaRed], c + up * 1.2f - f.T * 0.15f + f.N * 0.1f, f.T * 0.62f, up * 0.05f, f.N * 0.19f);
        PBox(_fine[TPTunaPink], c + up * 1.25f - f.T * 0.15f + f.N * 0.1f, f.T * 0.55f, up * 0.012f, f.N * 0.12f);
        // two loins and a slab of otoro on the board in front
        foreach (var (da, mat, len) in new[] { (-0.9f, TPTunaRed, 0.55f), (-0.1f, TPTunaRed, 0.5f), (0.75f, TPTunaPink, 0.38f) })
        {
            var lc = c + up * (top - y + 0.07f) + f.T * da - f.N * 0.32f;
            PBox(small[mat], lc, f.T * len * 0.5f, up * 0.07f, f.N * 0.1f);
            PBox(_fine[TPWhitePaint], lc + up * 0.072f, f.T * len * 0.45f, up * 0.004f, f.N * 0.02f);
        }
        PTuna(small, c + up * 0.1f + f.T * 1.05f - f.N * 0.05f, f.T, up, f.N, 0.9f, false, top);   // head section
        // knives laid on the table and a tub of ice
        PBox(_fine[TPSteelGrey], c + up * 0.95f + f.T * 1.2f + f.N * 0.42f, f.T * 0.45f, up * 0.004f, f.N * 0.03f);
        PBox(_fine[TPWoodDark], c + up * 0.96f + f.T * 1.72f + f.N * 0.42f, f.T * 0.1f, up * 0.015f, f.N * 0.025f);
        PCyl(small[TPCrateBlue], c + f.T * 2.3f + f.N * 0.3f, up, 0.38f, 0.42f, 0.6f, 14, false);
        PCyl(small[TPStyrofoam], c + f.T * 2.3f + f.N * 0.3f + up * 0.55f, up, 0.38f, 0.38f, 0.02f, 14, true);
        // red-white kohaku skirt on the audience side, canopy on four poles
        var kohaku = PM("Kohaku", new Color(0.72f, 0.10f, 0.10f), twoSided: true, gloss: 0.1f);
        for (int k = 0; k < 8; k++)
            PCard(small[k % 2 == 0 ? kohaku : TPCanvas(0)], c - f.N * 0.61f + up * 0.45f + f.T * (-1.49f + k * 0.425f),
                  f.T * 0.2125f, up * 0.43f, -f.N);
        foreach (int sa in new[] { -1, 1 })
            foreach (int sn in new[] { -1, 1 })
                PCyl(small[TPSteelGrey], c + f.T * (sa * 2.6f) + f.N * (sn * 1.9f), up, 0.04f, 0.04f, 3.0f, 8, true);
        var tilt = (up + f.N * 0.14f).normalized;
        PBox(small[TPCanvas(1)], c + up * 3.05f, f.T * 2.8f, tilt * 0.03f, Vector3.Cross(f.T, tilt).normalized * 2.1f);
        for (int k = 0; k < 10; k++)
            PCard(small[k % 2 == 0 ? TPCanvas(1) : TPCanvas(0)], c + up * 2.86f - f.N * 2.07f + f.T * (-2.52f + k * 0.56f),
                  f.T * 0.28f, up * 0.17f, -f.N, twoFaced: true);
        // the banner board facing the road
        var ban = PM("KaitaiBanner", Color.white, "TP_KaitaiBanner", twoSided: true, gloss: 0.2f);
        var bc = c - f.N * 2.15f + up * 3.75f;           // above the canopy fringe: the table stays visible
        PCard(small[ban], bc, f.T * 2.4f, up * 0.6f, -f.N, twoFaced: true);
        foreach (int sa in new[] { -1, 1 })
            PCyl(small[TPSteelGrey], bc + f.T * (sa * 2.45f) - up * 3.75f, up, 0.04f, 0.04f, 4.4f, 8, true);
        // people: the cutter and his assistant behind the table, the audience in an arc
        PortPerson(cast.stand, people, c + f.N * 1.05f - f.T * 0.2f, -f.N, MinatoCrowdActor.MotionKind.Wave, 0.713f, "Cutter");
        PortPerson(cast.stand, people, c + f.N * 1.0f + f.T * 1.8f, -f.N + f.T * -0.5f, MinatoCrowdActor.MotionKind.Idle, 0.284f, "Worker");
        int watchers = 0;
        for (int k = 0; k < 17; k++)
        {
            float th = Mathf.Lerp(-70f, 70f, k / 16f) * Mathf.Deg2Rad;
            float rr = 2.9f + (k % 3) * 0.75f + (float)rng.NextDouble() * 0.3f;
            var p = c + (f.T * Mathf.Sin(th) - f.N * Mathf.Cos(th)) * rr;
            if (rng.NextDouble() < 0.12) continue;
            PortPerson(cast.stand, people, p, c - p, MinatoCrowdActor.MotionKind.Idle, (float)rng.NextDouble(), "Shopper");
            watchers++;
        }
        Debug.Log($"[tunaport] tuna-cutting show at a={a} n={n}: {watchers} watching.");
    }

    /// <summary>Squid drying lines, net racks, octopus pots, coiled ropes, tyre fenders, life rings,
    /// net-menders on benches.</summary>
    private static void QuayDressing(PortBins big, PortBins small, PFrame f, System.Random rng, PortCast cast, Transform people)
    {
        var up = Vector3.up;
        float y = PortQuayY;
        // ---- ika-boshi: dried squid pegged along three lines
        var squid = PMCut("Squid", "TP_Squid");
        for (int line = 0; line < 3; line++)
        {
            float n = 45f + line * 2.2f;
            const float a0 = 18f, a1 = 34f;
            small.SetChunk(a0);
            foreach (float pa in new[] { a0, (a0 + a1) * 0.5f, a1 })
                PCyl(small[TPWood], f.W(pa, n, y), up, 0.05f, 0.04f, 2.1f, 8, true);
            var w0 = f.W(a0, n, y + 1.95f); var w1 = f.W(a1, n, y + 1.95f);
            PRope(_fine[TPBlack], w0, w1, 0.12f, 0.006f, 8);
            for (float a = a0 + 0.5f; a < a1 - 0.3f; a += 0.42f)
            {
                float t = (a - a0) / (a1 - a0);
                var hang = Vector3.Lerp(w0, w1, t) - up * (0.12f * 4f * t * (1f - t));
                PCard(small[squid], hang - up * 0.3f, f.T * 0.15f, up * 0.3f, f.N, twoFaced: false);
            }
        }
        // ---- nets drying on a rack, and a net-mender pair on a bench beside them
        var netM = PMCut("Net", "TP_Net", 0.4f);
        {
            const float a0 = -42f, a1 = -32f, n = 44.5f;
            small.SetChunk(a0);
            for (float pa = a0; pa <= a1 + 0.01f; pa += 2.5f)
                PCyl(small[TPWood], f.W(pa, n, y), up, 0.06f, 0.05f, 2.6f, 8, true);
            Beam(small[TPWood], f.W(a0, n, y + 2.55f), f.W(a1, n, y + 2.55f), 0.05f);
            for (int k = 0; k < 4; k++)
                PCard(small[netM], f.W(a0 + 1.25f + k * 2.5f, n, y + 1.5f) + f.N * (0.05f * (k % 2)), f.T * 1.25f, up * 1.05f, f.N);
            PEllipsoid(small[TPNetGreen], f.W(-36f, 47.6f, y + 0.25f), f.T * 1.4f, up * 0.35f, f.N * 0.9f, 14, 8);
            if (cast.sit.Count > 0)
            {
                PortPerson(cast.sit, people, f.W(-38.2f, 49.4f, y), -f.N, MinatoCrowdActor.MotionKind.Sit, 0.12f, "Fisherman");
                PortPerson(cast.sit, people, f.W(-34.6f, 49.4f, y), -f.N, MinatoCrowdActor.MotionKind.Sit, 0.57f, "Fisherman");
            }
        }
        // ---- takotsubo octopus pots, stacked in roped rows
        for (int row = 0; row < 3; row++)
            for (int k = 0; k < 9; k++)
            {
                float a = 58f + k * 0.62f, n = 50f + row * 0.7f;
                small.SetChunk(a);
                for (int h = 0; h < 1 + (k + row) % 3; h++)
                {
                    var pc = f.W(a, n, y + h * 0.36f);
                    PCyl(small[TPTerracotta], pc, up, 0.2f, 0.26f, 0.36f, 12, false);
                    PCyl(_fine[TPBlack], pc + up * 0.355f, up, 0.2f, 0.2f, 0.004f, 10, true);
                }
            }
        // ---- coiled mooring ropes
        for (int k = 0; k < 7; k++)
        {
            float a = -60f + k * 19f + (float)rng.NextDouble() * 4f, n = PortQuayFar - 1.9f;
            small.SetChunk(a);
            var cc = f.W(a, n, y);
            var mat = k % 2 == 0 ? TPRope : TPRopeBlue;
            for (int t = 0; t < 4; t++) PTorus(small[mat], cc + up * (0.05f + t * 0.07f), up, 0.42f - t * 0.03f, 0.04f, 16, 6);
        }
        // ---- tyre fenders hung on the quay face between the rubber ones, and life-ring stands
        for (float a = -65f; a < PortQuayA1 - 3f; a += 10f)
        {
            small.SetChunk(a);
            PTorus(small[TPRubber], f.W(a, PortQuayFar + 0.18f, y - 0.8f), f.N, 0.42f, 0.14f, 14, 8);
            PRope(_fine[TPRope], f.W(a, PortQuayFar - 0.05f, y + 0.02f), f.W(a, PortQuayFar + 0.18f, y - 0.4f), 0.02f, 0.015f, 3);
        }
        foreach (float a in new[] { -30f, 16f, 52f })
        {
            small.SetChunk(a);
            var pc = f.W(a, PortQuayFar - 1.2f, y);
            PCyl(small[TPWhitePaint], pc, up, 0.04f, 0.04f, 1.3f, 8, true);
            var rc = pc + up * 1.05f + f.N * 0.08f;
            for (int q = 0; q < 4; q++)
            {
                // a red/white life ring built from four quarter arcs
                float t0 = q * 90f, t1 = t0 + 90f;
                var mat = q % 2 == 0 ? TPRedPaint : TPWhitePaint;
                for (int s = 0; s < 4; s++)
                {
                    float ta = Mathf.Lerp(t0, t1, s / 4f) * Mathf.Deg2Rad, tb = Mathf.Lerp(t0, t1, (s + 1) / 4f) * Mathf.Deg2Rad;
                    Beam(small[mat], rc + (f.T * Mathf.Cos(ta) + up * Mathf.Sin(ta)) * 0.3f,
                         rc + (f.T * Mathf.Cos(tb) + up * Mathf.Sin(tb)) * 0.3f, 0.06f);
                }
            }
        }
        // ---- a hose snaking from the hall wall to the landings
        {
            small.SetChunk(-50f);
            var hose = PM("Hose", new Color(0.14f, 0.46f, 0.20f), gloss: 0.5f);
            Vector3 prev = f.W(-60f, PortApronFar + 0.3f, y + 0.03f);
            for (int k = 1; k <= 16; k++)
            {
                float t = k / 16f;
                var p = f.W(-60f + t * 8f + Mathf.Sin(t * 9f) * 1.2f, PortApronFar + 0.3f + t * 23f, y + 0.03f);
                Beam(_fine[hose], prev, p, 0.025f);
                prev = p;
            }
        }
    }

    // pass-1 berths (TunaPort.Fleet.cs BuildPortBoats): quay-face boats (a, n, dir along T, length, style)
    private static readonly (float a, float n, float dir, float len, int style)[] QuayBerths =
        { (-52f, PortQuayFar + 3.4f, 1f, 15f, 1), (-8f, PortQuayFar + 3.2f, -1f, 13f, 1), (10f, PortQuayFar + 3.0f, 1f, 11f, 2),
          (66f, PortQuayFar + 4.4f, -1f, 23f, 0), (90f, PortQuayFar + 3.0f, 1f, 10f, 2) };

    /// <summary>Bow and stern lines to the bollards, spring lines on the pier boats, and squid-
    /// fishing lamp strings (ika-tsuri) on the coastal boats.</summary>
    private static void MooringAndLamps(PortBins small, PFrame f)
    {
        var up = Vector3.up;
        foreach (var (a, n, dir, len, style) in QuayBerths)
        {
            small.SetChunk(a);
            var fwd = f.T * dir;
            var bow = f.W(a, n, 0f) + fwd * (len * 0.46f) + up * (style == 2 ? 1.7f : 2.25f) - f.N * 0.4f;
            var stern = f.W(a, n, 0f) - fwd * (len * 0.46f) + up * (style == 2 ? 0.95f : 1.4f) - f.N * 0.6f;
            float bA = a + dir * (len * 0.5f + 3f), sA = a - dir * (len * 0.5f + 3f);
            var bb = f.W(bA, PortQuayFar - 0.8f, PortQuayY + 0.5f); var sb = f.W(sA, PortQuayFar - 0.8f, PortQuayY + 0.5f);
            PRope(small[TPRope], bow, bb, 0.35f, 0.022f, 8);
            PRope(small[TPRope], stern, sb, 0.35f, 0.022f, 8);
            PCyl(small[TPBlack], bb - up * 0.5f, up, 0.2f, 0.18f, 0.5f, 10, true);
            PCyl(small[TPBlack], sb - up * 0.5f, up, 0.2f, 0.18f, 0.5f, 10, true);
            if (style == 1) SquidLamps(small, f.W(a, n, 0f), fwd, len);
        }
        foreach (float pa in new[] { -25f, 35f })
            foreach (var (da, dn, len, style) in new[] { (-6.3f, 20f, 16f, 0), (6.0f, 18f, 13f, 1), (-6.0f, 37f, 10f, 2) })
            {
                float a = pa + da, n = PortQuayFar + dn;
                small.SetChunk(a);
                float sg = Mathf.Sign(da);
                var mid = f.W(a, n, 0f) - f.T * (sg * len * 0.1f) + up * (style == 2 ? 1.5f : 2.0f);
                var pier = f.W(pa + sg * 2.6f, n - len * 0.2f, PortQuayY - 0.2f);
                var pier2 = f.W(pa + sg * 2.6f, n + len * 0.3f, PortQuayY - 0.2f);
                PRope(small[TPRope], mid - f.N * (len * 0.3f), pier, 0.3f, 0.02f, 8);
                PRope(small[TPRopeBlue], mid + f.N * (len * 0.3f), pier2, 0.3f, 0.02f, 8);
                if (style == 1) SquidLamps(small, f.W(a, n, 0f), f.N, len);
            }
    }

    /// <summary>A line of big bare bulbs strung fore-and-aft over the deck (ika-tsuri-sen).</summary>
    private static void SquidLamps(PortBins small, Vector3 c, Vector3 fwd, float len)
    {
        var up = Vector3.up;
        var p0 = c - fwd * (len * 0.32f) + up * 3.7f; var p1 = c + fwd * (len * 0.38f) + up * 3.5f;
        Beam(small[TPSteelGrey], p0 - up * 2.4f, p0, 0.03f);
        Beam(small[TPSteelGrey], p1 - up * 1.9f, p1, 0.03f);
        PRope(_fine[TPBlack], p0, p1, 0.12f, 0.008f, 8);
        int n = Mathf.Max(4, Mathf.RoundToInt((p1 - p0).magnitude / 0.8f));
        for (int k = 1; k < n; k++)
        {
            float t = (float)k / n;
            var h = Vector3.Lerp(p0, p1, t) - up * (0.12f * 4f * t * (1f - t));
            Beam(_fine[TPBlack], h, h - up * 0.18f, 0.008f);
            PEllipsoid(small[TPBulb], h - up * 0.32f, Vector3.right * 0.13f, up * 0.17f, Vector3.forward * 0.13f, 14, 8);
            PCyl(small[TPWhitePaint], h - up * 0.2f, -up, 0.02f, 0.2f, 0.06f, 10, false);
        }
    }

    /// <summary>A small Ebisu shrine (fishermen's luck) at the breakwater root: vermilion torii,
    /// a hokora on a stone plinth, shimenawa, a pair of stone lanterns, offering sake, a worshipper.</summary>
    private static void FishermensShrine(PortBins big, PortBins small, PFrame f, PortCast cast, Transform people)
    {
        const float a = -66f, n = 47f;
        big.SetChunk(a); small.SetChunk(a);
        var up = Vector3.up;
        float y = PortQuayY;
        var c = f.W(a, n, y);
        var shu = PM("Vermilion", new Color(0.78f, 0.20f, 0.08f), gloss: 0.35f);
        var stone = TPSeaWall;
        // stone plinth + hokora (faces the sea, +N)
        PBox(big[stone], c + up * 0.3f, f.T * 1.0f, up * 0.3f, f.N * 1.0f);
        PBox(big[TPCedar], c + up * 1.15f, f.T * 0.55f, up * 0.55f, f.N * 0.5f);
        PBox(small[TPKoshiWood], c + up * 1.1f + f.N * 0.51f, f.T * 0.4f, up * 0.42f, f.N * 0.02f);
        for (float x = -0.36f; x <= 0.37f; x += 0.08f)
            PBox(_fine[TPKoshiWood], c + up * 1.1f + f.N * 0.535f + f.T * x, f.T * 0.012f, up * 0.4f, f.N * 0.008f);
        foreach (int sg in new[] { -1, 1 })
        {
            var down = (f.N * sg * Mathf.Cos(30f * Mathf.Deg2Rad) - up * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;
            RoofSlab(big, small, c + up * 2.05f, f.T, down, 0.8f, 0.95f, TPKawara(1), 1, 2);
        }
        PBox(big[TPRidge], c + up * 2.1f, f.T * 0.82f, up * 0.07f, f.N * 0.12f);
        // shimenawa rope with white shide paper
        PRope(small[TPRope], c + up * 1.62f + f.N * 0.56f - f.T * 0.55f, c + up * 1.62f + f.N * 0.56f + f.T * 0.55f, 0.1f, 0.05f, 8);
        foreach (float x in new[] { -0.3f, 0f, 0.3f })
            PCard(_fine[TPWhitePaint], c + up * 1.42f + f.N * 0.6f + f.T * x, f.T * 0.04f, up * 0.12f, f.N, twoFaced: true);
        // offering stand with sake bottles and a sea bream
        var off = c + up * 0.6f + f.N * 1.15f;
        PBox(small[TPWood], off, f.T * 0.4f, up * 0.03f, f.N * 0.15f);
        foreach (float x in new[] { -0.25f, 0.25f })
        {
            PCyl(small[TPWhitePaint], off + f.T * x + up * 0.03f, up, 0.06f, 0.05f, 0.24f, 10, true);
            PCyl(small[TPBlack], off + f.T * x + up * 0.27f, up, 0.02f, 0.02f, 0.06f, 6, true);
        }
        PEllipsoid(small[TPRedPaint], off + up * 0.09f, f.T * 0.14f, up * 0.06f, f.N * 0.04f, 14, 8);
        // two stone lanterns and the vermilion torii toward the sea
        foreach (int sg in new[] { -1, 1 })
        {
            var l = c + f.T * (sg * 1.7f) + f.N * 2.2f;
            PBox(big[stone], l + up * 0.12f, f.T * 0.3f, up * 0.12f, f.N * 0.3f);
            PCyl(big[stone], l + up * 0.24f, up, 0.1f, 0.1f, 0.7f, 10, true);
            PBox(big[stone], l + up * 1.06f, f.T * 0.22f, up * 0.12f, f.N * 0.22f);
            PBox(small[TPBlack], l + up * 1.06f, f.T * 0.12f, up * 0.07f, f.N * 0.225f);
            PBox(big[stone], l + up * 1.26f, f.T * 0.32f, up * 0.07f, f.N * 0.32f);
            PEllipsoid(big[stone], l + up * 1.38f, f.T * 0.08f, up * 0.08f, f.N * 0.08f, 14, 8);
        }
        var tc = c + f.N * 4.2f;
        foreach (int sg in new[] { -1, 1 })
        {
            PCyl(big[shu], tc + f.T * (sg * 1.35f), up, 0.15f, 0.13f, 3.3f, 14, true);
            PCyl(small[TPBlack], tc + f.T * (sg * 1.35f), up, 0.19f, 0.19f, 0.25f, 14, true);
        }
        PBox(big[shu], tc + up * 2.7f, f.T * 1.6f, up * 0.1f, f.N * 0.12f);                       // nuki
        PBox(big[shu], tc + up * 3.35f, f.T * 1.95f, up * 0.12f, f.N * 0.18f);                    // shimaki
        PBox(big[TPBlack], tc + up * 3.52f, f.T * 2.15f, up * 0.07f, f.N * 0.22f);                // kasagi
        foreach (int sg in new[] { -1, 1 })
            PBox(big[TPBlack], tc + up * 3.58f + f.T * (sg * 2.05f), f.T * 0.16f, up * 0.07f, f.N * 0.22f);
        PBox(small[TPBlack], tc + up * 3.0f, f.T * 0.16f, up * 0.25f, f.N * 0.02f);               // gakuzuka plaque
        // nobori pair
        foreach (int sg in new[] { -1, 1 })
        {
            var pb = c + f.T * (sg * 2.6f) + f.N * 1.2f;
            PCyl(small[TPSteelGrey], pb, up, 0.03f, 0.03f, 3.3f, 6, true);
            PCard(small[TPNobori(sg < 0 ? 3 : 4)], pb + up * 2.05f + f.T * 0.34f, f.T * 0.32f, up * 1.18f, f.N, twoFaced: true);
        }
        PortPerson(cast.stand, people, c + f.N * 2.0f + f.T * 0.3f, -f.N, MinatoCrowdActor.MotionKind.Idle, 0.905f, "Fisherman");
    }

    /// <summary>Seated anglers along the breakwater with rods out over the open sea.</summary>
    private static void BreakwaterAnglers(PortBins small, PFrame f, PortCast cast, Transform people)
    {
        const float ba = -74f;
        var up = Vector3.up;
        int n = 0;
        foreach (float nn in new[] { 96f, 118f, 141f, 166f })
        {
            small.SetChunk(ba);
            var seat = f.W(ba + 3.1f, nn, 3.0f);
            if (cast.sit.Count > 0)
            {
                PortPerson(cast.sit, people, seat, f.T, MinatoCrowdActor.MotionKind.Sit, Hash01(nn, 9f), "Fisherman");
                n++;
            }
            // rod resting on a holder, line into the basin, cool box and bucket
            var butt = seat + f.T * 0.5f + f.N * 0.6f + up * 0.35f;
            var tip = butt + f.T * 3.4f + up * 2.2f;
            Beam(small[TPBlack], butt, tip, 0.012f);
            Beam(_fine[TPSteelGrey], butt - up * 0.35f, butt + up * 0.1f, 0.02f);
            PRope(_fine[TPWhitePaint], tip, f.W(ba + 11f, nn + 0.6f, 0.02f), 0.6f, 0.002f, 6);
            PBox(small[PM("CoolBox", new Color(0.12f, 0.36f, 0.70f), gloss: 0.5f)], seat + f.N * 1.1f + up * 0.2f, f.T * 0.22f, up * 0.2f, f.N * 0.3f, 1f, true);
            PCyl(small[TPWhitePaint], seat - f.N * 0.9f, up, 0.15f, 0.17f, 0.3f, 12, false);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

// Tuna port, part 2: the road-level market apron, the retaining sea wall, the working quay,
// piers, breakwater, and the MAGURO ICHIBA hall with its tuna floor. See TunaPort.cs.
public static partial class ShiosaiCoastEnvironment
{
    // ------------------------------------------------------------------ palette (PROVISIONAL)
    // True-world albedos; PortGain (TunaPort.cs) handles the coast rig's colour cast.
    private static Material TPConcrete => PMP("Concrete", "Concrete", Color.white, 4f);
    private static Material TPConcreteDark => PMP("ConcreteDark", "Concrete", new Color(0.80f, 0.80f, 0.80f), 4f);
    private static Material TPSeaWall => PMP("SeaWall", "Stone", Color.white, 2.4f, 1.3f);
    private static Material TPWetFloor => PMP("WetFloor", "WetConcrete", Color.white, 4f);
    private static Material TPYellow => PM("PaintYellow", new Color(0.86f, 0.66f, 0.10f), gloss: 0.35f);
    private static Material TPWhitePaint => PM("PaintWhite", new Color(0.80f, 0.81f, 0.82f), gloss: 0.4f);
    private static Material TPSteelGreen => PM("SteelGreen", new Color(0.34f, 0.50f, 0.47f), gloss: 0.45f);
    private static Material TPSteelGrey => PM("SteelGrey", new Color(0.58f, 0.60f, 0.62f), gloss: 0.55f);
    private static Material TPRoofMetal => PMP("RoofMetal", "RoofMetal", Color.white, 1.6f);
    private static Material TPWallMetal => PMP("WallMetal", "WallMetal", Color.white, 1.6f);
    private static Material TPShutter => PM("Shutter", new Color(0.86f, 0.87f, 0.88f), "TP_Shutter", gloss: 0.35f);
    private static Material TPFascia => PM("Fascia", new Color(0.84f, 0.85f, 0.86f), gloss: 0.3f);
    private static Material TPNavy => PM("Navy", new Color(0.07f, 0.14f, 0.36f), gloss: 0.35f);
    private static Material TPBlack => PM("Black", new Color(0.045f, 0.047f, 0.055f), gloss: 0.45f);
    private static Material TPRubber => PM("Rubber", new Color(0.05f, 0.05f, 0.055f), gloss: 0.25f);
    private static Material TPWood => PMP("Wood", "Planks", Color.white, 2f);
    private static Material TPWoodDark => PMP("WoodDark", "Yakisugi", Color.white, 1.8f);
    private static Material TPCrateBlue => PM("CrateBlue", new Color(0.06f, 0.26f, 0.70f), gloss: 0.55f);
    private static Material TPCrateOrange => PM("CrateOrange", new Color(0.88f, 0.34f, 0.06f), gloss: 0.55f);
    private static Material TPStyrofoam => PM("Styrofoam", new Color(0.88f, 0.89f, 0.90f), gloss: 0.12f);
    private static Material TPTuna => PM("Tuna", Color.white, "TP_Tuna", gloss: 0.78f);
    private static Material TPTunaFrozen => PM("TunaFrozen", Color.white, "TP_TunaFrozen", gloss: 0.35f);
    private static Material TPTunaFin => PM("TunaFin", new Color(0.07f, 0.09f, 0.16f), twoSided: true, gloss: 0.65f);
    private static Material TPSignMarket => PM("SignMarket", Color.white, "TP_MarketSign", gloss: 0.3f);
    private static Material TPSignVertical => PM("SignVertical", Color.white, "TP_MarketVertical", twoSided: true, gloss: 0.3f);
    private static Material TPAuctionBoard => PM("AuctionBoard", Color.white, "TP_AuctionBoard", gloss: 0.2f);
    private static Material TPLantern(int i) => PM($"Lantern{i}", Color.white, $"TP_Lantern_{i}", gloss: 0.4f);
    // ------------------------------------------------------------------ apron + quay

    private static void BuildPortApronAndQuay(PortBins big, PortBins small, PFrame f)
    {
        const float step = 6f;
        // ---- road-level market apron --------------------------------------------------------
        for (float a = PortApronA0; a < PortApronA1 - 0.01f; a += step)
        {
            big.SetChunk(a); small.SetChunk(a);
            float a1 = Mathf.Min(a + step, PortApronA1);
            float y0 = f.RoadY(a) + 0.12f, y1 = f.RoadY(a1) + 0.12f;
            var b = _walk[TPConcrete];
            PQ(b, f.W(a, PortApronNear, y0), f.W(a1, PortApronNear, y1), f.W(a1, PortApronFar, y1), f.W(a, PortApronFar, y0),
               new Vector2(a, PortApronNear), new Vector2(a1, PortApronNear),
               new Vector2(a1, PortApronFar), new Vector2(a, PortApronFar), Vector3.up);
            // road kerb
            PQ(big[TPConcreteDark], f.W(a, PortApronNear, y0), f.W(a1, PortApronNear, y1),
               f.W(a1, PortApronNear, y1 - 0.5f), f.W(a, PortApronNear, y0 - 0.5f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -f.N);
            // seaward retaining wall, down to the quay
            PQ(big[TPSeaWall], f.W(a, PortApronFar, y0), f.W(a1, PortApronFar, y1),
               f.W(a1, PortApronFar, PortQuayY - 0.05f), f.W(a, PortApronFar, PortQuayY - 0.05f),
               new Vector2(a, y0), new Vector2(a1, y1),
               new Vector2(a1, PortQuayY), new Vector2(a, PortQuayY), f.N);
            // coping on the wall head
            PBoxF(big[TPConcreteDark], f, f.W((a + a1) * 0.5f, PortApronFar - 0.2f, (y0 + y1) * 0.5f + 0.1f),
                  (a1 - a) * 0.5f, 0.1f, 0.35f);
            // pavement between the road edge and the apron kerb
            PQ(_walk[TPConcreteDark], f.W(a, 4.5f, y0 - 0.08f), f.W(a1, 4.5f, y1 - 0.08f),
               f.W(a1, PortApronNear, y1 - 0.08f), f.W(a, PortApronNear, y0 - 0.08f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            // yellow safety line along the wall head and a white parking/lane line
            PQ(small[TPYellow], f.W(a, PortApronFar - 1.4f, y0 + 0.012f), f.W(a1, PortApronFar - 1.4f, y1 + 0.012f),
               f.W(a1, PortApronFar - 1.1f, y1 + 0.012f), f.W(a, PortApronFar - 1.1f, y0 + 0.012f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            // railing on the wall head
            for (float pa = a; pa < a1 - 0.01f; pa += 2f)
                PBoxF(small[TPSteelGrey], f, f.W(pa, PortApronFar - 0.45f, f.RoadY(pa) + 0.12f + 0.55f), 0.04f, 0.55f, 0.04f);
            foreach (float h in new[] { 1.05f, 0.55f })
                PBoxF(small[TPSteelGrey], f, f.W((a + a1) * 0.5f, PortApronFar - 0.45f, (y0 + y1) * 0.5f + h),
                      (a1 - a) * 0.5f, 0.035f, 0.035f);
        }
        big.SetChunk(PortApronA0);
        float ya0 = f.RoadY(PortApronA0) + 0.12f, ya1 = f.RoadY(PortApronA1) + 0.12f;
        PQ(big[TPSeaWall], f.W(PortApronA0, PortApronNear, ya0), f.W(PortApronA0, PortApronFar, ya0),
           f.W(PortApronA0, PortApronFar, -3f), f.W(PortApronA0, PortApronNear, -3f),
           new Vector2(PortApronNear, ya0), new Vector2(PortApronFar, ya0), new Vector2(PortApronFar, -3f), new Vector2(PortApronNear, -3f), -f.T);
        big.SetChunk(PortApronA1);
        PQ(big[TPSeaWall], f.W(PortApronA1, PortApronNear, ya1), f.W(PortApronA1, PortApronFar, ya1),
           f.W(PortApronA1, PortApronFar, -3f), f.W(PortApronA1, PortApronNear, -3f),
           new Vector2(PortApronNear, ya1), new Vector2(PortApronFar, ya1), new Vector2(PortApronFar, -3f), new Vector2(PortApronNear, -3f), f.T);

        // ---- the working quay ---------------------------------------------------------------
        const float qa0 = -70f;
        for (float a = qa0; a < PortQuayA1 - 0.01f; a += 10f)
        {
            big.SetChunk(a); small.SetChunk(a);
            float a1 = Mathf.Min(a + 10f, PortQuayA1);
            PQ(_walk[TPConcrete], f.W(a, PortApronFar - 0.6f, PortQuayY), f.W(a1, PortApronFar - 0.6f, PortQuayY),
               f.W(a1, PortQuayFar, PortQuayY), f.W(a, PortQuayFar, PortQuayY),
               new Vector2(a, PortApronFar), new Vector2(a1, PortApronFar), new Vector2(a1, PortQuayFar), new Vector2(a, PortQuayFar), Vector3.up);
            PQ(big[TPConcreteDark], f.W(a, PortQuayFar, PortQuayY), f.W(a1, PortQuayFar, PortQuayY),
               f.W(a1, PortQuayFar, -5f), f.W(a, PortQuayFar, -5f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, f.N);
            if (a >= PortApronA1 - 0.01f)   // beyond the apron the quay's landward edge shows
                PQ(big[TPConcreteDark], f.W(a, PortApronFar - 0.6f, PortQuayY), f.W(a1, PortApronFar - 0.6f, PortQuayY),
                   f.W(a1, PortApronFar - 0.6f, -1.5f), f.W(a, PortApronFar - 0.6f, -1.5f),
                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -f.N);
            // yellow edge, fenders, bollards
            PQ(small[TPYellow], f.W(a, PortQuayFar - 0.35f, PortQuayY + 0.012f), f.W(a1, PortQuayFar - 0.35f, PortQuayY + 0.012f),
               f.W(a1, PortQuayFar, PortQuayY + 0.012f), f.W(a, PortQuayFar, PortQuayY + 0.012f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            for (float fa = a + 2.5f; fa < a1; fa += 5f)
                PBoxF(small[TPRubber], f, f.W(fa, PortQuayFar + 0.18f, PortQuayY - 0.9f), 0.45f, 0.75f, 0.18f);
            float ba = a + 5f;
            if (ba < a1)
            {
                var bc = f.W(ba, PortQuayFar - 0.8f, PortQuayY);
                PCyl(big[TPBlack], bc, Vector3.up, 0.22f, 0.2f, 0.55f, 10);
                PEllipsoid(big[TPBlack], bc + Vector3.up * 0.6f, f.T * 0.3f, Vector3.up * 0.1f, f.N * 0.3f, 8, 3);
            }
        }
        big.SetChunk(qa0);
        PQ(big[TPConcreteDark], f.W(qa0, PortApronFar - 0.6f, PortQuayY), f.W(qa0, PortQuayFar, PortQuayY),
           f.W(qa0, PortQuayFar, -5f), f.W(qa0, PortApronFar - 0.6f, -5f),
           Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -f.T);
        big.SetChunk(PortQuayA1);
        PQ(big[TPConcreteDark], f.W(PortQuayA1, PortApronFar - 0.6f, PortQuayY), f.W(PortQuayA1, PortQuayFar, PortQuayY),
           f.W(PortQuayA1, PortQuayFar, -5f), f.W(PortQuayA1, PortApronFar - 0.6f, -5f),
           Vector2.zero, Vector2.right, Vector2.one, Vector2.up, f.T);

        // ---- stairs from the apron down to the quay (seaward face of the wall) ---------------
        big.SetChunk(-60f);
        float top = f.RoadY(-58f) + 0.12f;
        int steps = Mathf.CeilToInt((top - PortQuayY) / 0.36f);
        for (int k = 0; k < steps; k++)
        {
            float yTop = top - k * 0.36f;
            float nC = PortApronFar + 0.5f + k * 0.42f;
            PBoxF(_walk[TPConcreteDark], f, f.W(-58f, nC, (yTop + PortQuayY) * 0.5f - 0.18f), 1.4f,
                  Mathf.Max(0.05f, (yTop - PortQuayY) * 0.5f - 0.18f), 0.21f);
        }

        // ---- piers (on piles) -----------------------------------------------------------------
        foreach (float pa in new[] { -25f, 35f })
        {
            big.SetChunk(pa); small.SetChunk(pa);
            const float len = 42f, hw = 3f;
            float nc = PortQuayFar + len * 0.5f;
            PBoxF(_walk[TPConcrete], f, f.W(pa, nc, PortQuayY - 0.5f), hw, 0.3f, len * 0.5f, bottom: true);
            for (float pn = PortQuayFar + 3f; pn < PortQuayFar + len; pn += 6f)
                foreach (float side in new[] { -hw + 0.5f, hw - 0.5f })
                    PCyl(big[TPConcreteDark], f.W(pa + side, pn, -5f), Vector3.up, 0.32f, 0.32f, 5f + PortQuayY - 0.8f, 8, false);
            for (float pn = PortQuayFar + 5f; pn < PortQuayFar + len; pn += 9f)
                foreach (float side in new[] { -hw + 0.45f, hw - 0.45f })
                {
                    var bc = f.W(pa + side, pn, PortQuayY - 0.2f);
                    PCyl(big[TPBlack], bc, Vector3.up, 0.18f, 0.16f, 0.45f, 8);
                }
            PQ(small[TPYellow], f.W(pa - hw, PortQuayFar, PortQuayY - 0.19f), f.W(pa - hw + 0.3f, PortQuayFar, PortQuayY - 0.19f),
               f.W(pa - hw + 0.3f, PortQuayFar + len, PortQuayY - 0.19f), f.W(pa - hw, PortQuayFar + len, PortQuayY - 0.19f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
        }

        // ---- breakwater arm with parapet ----------------------------------------------------
        {
            big.SetChunk(-80f);
            const float ba = -74f, n0 = PortQuayFar - 8f, n1 = 205f;
            PBoxF(big[TPConcrete], f, f.W(ba, (n0 + n1) * 0.5f, -1.5f), 4.5f, 4.5f, (n1 - n0) * 0.5f);
            PBoxF(big[TPConcreteDark], f, f.W(ba - 3.6f, (n0 + n1) * 0.5f, 3.8f), 0.9f, 0.8f, (n1 - n0) * 0.5f);
            PQ(small[TPYellow], f.W(ba + 4.1f, n0, 3.012f), f.W(ba + 4.4f, n0, 3.012f),
               f.W(ba + 4.4f, n1, 3.012f), f.W(ba + 4.1f, n1, 3.012f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
        }
    }

    // ------------------------------------------------------------------ tuna

    /// <summary>
    /// One tuna. <paramref name="nose"/> is the snout on the body axis; dorsal/lateral are the
    /// fish's own up and side. lyingOnY: if not NaN the fish lies on its flank on that floor
    /// (per-station lift so the whole flank touches the floor).
    /// </summary>
    private static void PTuna(PortBins bins, Vector3 nose, Vector3 dir, Vector3 dorsal, Vector3 lateral,
                              float len, bool frozen, float lyingOnY = float.NaN)
    {
        var body = bins[frozen ? TPTunaFrozen : TPTuna];
        dir.Normalize(); dorsal.Normalize(); lateral.Normalize();
        float R = 0.125f * len;
        const int rings = 22, seg = 18;
        float[] us = new float[rings + 1];
        for (int i = 0; i <= rings; i++) { float t = (float)i / rings; us[i] = 0.97f * (t * t * (3f - 2f * t) * 0.35f + t * 0.65f); }
        float Rad(float u)
        {
            float g = Mathf.Pow(Mathf.Max(u, 0f), 0.5f) * Mathf.Pow(Mathf.Max(1f - u, 0f), 0.75f) / 0.431f;
            return R * (0.07f + 0.93f * g);
        }
        Vector3 C(float u)
        {
            var c = nose + dir * (len * u);
            if (!float.IsNaN(lyingOnY)) c.y = lyingOnY + Rad(u) * 0.78f + 0.01f;
            return c;
        }
        // fusiform cross-section: taller than wide, with a slight keel toward the tail
        Vector3 P(int ri, int k)
        {
            float u = us[ri];
            float th = k * Mathf.PI * 2f / seg;
            float r = Rad(u);
            float lat = 0.8f - 0.25f * Mathf.Clamp01((u - 0.6f) / 0.37f);
            return C(u) + dorsal * (Mathf.Cos(th) * r) + lateral * (Mathf.Sin(th) * r * lat);
        }
        PGrid(body, rings, seg, (ri, k) => P(ri, k), (ri, k) => new Vector2(us[ri], (float)k / seg), (ri, k) => C(us[ri]));        // snout cap
        var tip = C(0f) - dir * (0.03f * len);
        for (int k = 0; k < seg; k++)
            PTri(body, tip, P(0, k), P(0, k + 1), new Vector2(0.01f, 0.5f), new Vector2(0.02f, (float)k / seg),
                 new Vector2(0.02f, (float)(k + 1) / seg), -dir);
        int last = rings;
        var tc = C(us[last]);
        if (frozen)
        {
            for (int k = 0; k < seg; k++)   // sawn tail: red frozen section
                PTri(body, tc, P(last, k + 1), P(last, k), new Vector2(0.97f, 0.5f), new Vector2(0.97f, 0.45f),
                     new Vector2(0.97f, 0.55f), dir);
            return;
        }
        for (int k = 0; k < seg; k++)
            PTri(body, tc + dir * (0.02f * len), P(last, k + 1), P(last, k), new Vector2(0.99f, 0.5f),
                 new Vector2(0.98f, 0.4f), new Vector2(0.98f, 0.6f), dir);
        var fin = bins[TPTunaFin];
        var tb = tc + dir * (0.02f * len);
        // lunate caudal fin
        var tipU = tb + dir * (0.13f * len) + dorsal * (0.17f * len);
        var tipD = tb + dir * (0.13f * len) - dorsal * (0.17f * len);
        var notch = tb + dir * (0.07f * len);
        PTri(fin, tb, tipU, notch, Vector2.zero, Vector2.right, Vector2.one, lateral);
        PTri(fin, tb, notch, tipD, Vector2.zero, Vector2.right, Vector2.one, lateral);
        // dorsal + anal fin
        var d0 = C(0.34f) + dorsal * Rad(0.34f) * 0.95f;
        var d1 = C(0.46f) + dorsal * Rad(0.46f) * 0.95f;
        PTri(fin, d0, d1, d0 + dorsal * (0.1f * len) + dir * (0.02f * len), Vector2.zero, Vector2.right, Vector2.one, lateral);
        var e0 = C(0.55f) - dorsal * Rad(0.55f) * 0.95f;
        var e1 = C(0.64f) - dorsal * Rad(0.64f) * 0.95f;
        PTri(fin, e0, e1, e0 - dorsal * (0.07f * len) + dir * (0.04f * len), Vector2.zero, Vector2.right, Vector2.one, lateral);
        // pectoral fin (the long bluefin/bigeye scythe)
        var pc = C(0.24f) + lateral * Rad(0.24f) * 0.8f;
        PTri(fin, pc, pc + dir * (0.05f * len), pc + dir * (0.2f * len) - dorsal * (0.03f * len) + lateral * 0.02f,
             Vector2.zero, Vector2.right, Vector2.one, lateral);
    }

    private static void PLantern(PortBins bins, Vector3 hang, int variant, float scale = 1f)
    {
        var b = bins[TPLantern(variant)];
        var c = hang - Vector3.up * (0.42f * scale);
        PEllipsoid(b, c, Vector3.right * (0.26f * scale), Vector3.up * (0.36f * scale), Vector3.forward * (0.26f * scale), 10, 6);
        var k = bins[TPBlack];
        PCyl(k, c + Vector3.up * (0.3f * scale), Vector3.up, 0.15f * scale, 0.15f * scale, 0.1f * scale, 8);
        PCyl(k, c - Vector3.up * (0.4f * scale), Vector3.up, 0.15f * scale, 0.15f * scale, 0.1f * scale, 8);
    }

    private static void PCrateStack(PortBins bins, PFrame f, Vector3 at, int count, bool styro, System.Random r)
    {
        var m = styro ? TPStyrofoam : (r.NextDouble() < 0.75 ? TPCrateBlue : TPCrateOrange);
        float h = styro ? 0.34f : 0.3f;
        for (int k = 0; k < count; k++)
        {
            var j = f.T * (((float)r.NextDouble() - 0.5f) * 0.05f);
            PBoxF(bins[m], f, at + j + Vector3.up * (h * (k + 0.5f)), styro ? 0.42f : 0.32f, h * 0.48f, styro ? 0.24f : 0.22f);
        }
    }

    /// <summary>Turret truck (ターレ): the little cylinder-engined market cart.</summary>
    private static void PTurret(PortBins bins, PFrame f, Vector3 at, float yawSign, int colour)
    {
        var body = colour == 0 ? TPSteelGreen : colour == 1 ? TPCrateBlue : TPYellow;
        var fw = f.T * yawSign;
        PBoxF(bins[body], f, at + Vector3.up * 0.35f, 0.62f, 0.12f, 1.0f);
        PCyl(bins[body], at + Vector3.up * 0.47f + fw * 0.0f + f.N * 0.7f, Vector3.up, 0.34f, 0.3f, 0.95f, 10);
        PCyl(bins[TPBlack], at + Vector3.up * 1.42f + f.N * 0.7f, Vector3.up, 0.3f, 0.3f, 0.05f, 10);
        foreach (float s in new[] { -0.55f, 0.55f })
            foreach (float n in new[] { -0.7f, 0.7f })
                PCyl(bins[TPRubber], at + f.T * s + f.N * n + Vector3.up * 0.2f - f.T * 0.08f, f.T, 0.2f, 0.2f, 0.16f, 8);
    }
}

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// Tuna port, part 8: pass-2 street life and dressing ("add extensive detail", 2026-09-27 20:40).
// Everything tiny goes in _fine (culled at PortFineCullM); anything read from the road at
// distance goes in small. Positions are laid against the pass-1 layout constants (TunaPort.cs /
// Quay / Market / Fleet). The pass-2 RNG is separate, so pass-1 placement is unchanged.
// All numbers PROVISIONAL, tuned from the captures. Continued in TunaPort.Detail.Quay/.Life.cs.
public static partial class ShiosaiCoastEnvironment
{
    // ------------------------------------------------------------------ materials

    /// <summary>Alpha-tested, double-sided card material (nets, drying racks, hung squid).</summary>
    private static Material PMCut(string name, string tex, float cutoff = 0.5f)
    {
        string full = "Shiosai_TP_" + name;
        if (MaterialCache.TryGetValue(full, out var cached) && cached != null) return cached;
        var m = PortLitBase(full, Color.white, true, 0.15f, 0f);
        m.SetTexture("_BaseColorMap", PortTex(tex));
        m.SetFloat("_AlphaCutoffEnable", 1f);
        m.SetFloat("_AlphaCutoff", cutoff);
        if (m.HasProperty("_UseShadowThreshold")) m.SetFloat("_UseShadowThreshold", 1f);
        if (m.HasProperty("_AlphaCutoffShadow")) m.SetFloat("_AlphaCutoffShadow", cutoff);
        m.EnableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_MASKMAP");
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        MaterialCache[full] = m;
        return m;
    }

    private static Material TPRope => PM("Rope", new Color(0.70f, 0.62f, 0.44f), gloss: 0.1f);
    private static Material TPRopeBlue => PM("RopeBlue", new Color(0.12f, 0.26f, 0.60f), gloss: 0.2f);
    private static Material TPPuddle => PM("Puddle", new Color(0.10f, 0.11f, 0.12f), gloss: 0.96f, spec: 0.5f);
    private static Material TPRedPaint => PM("PaintRed", new Color(0.70f, 0.08f, 0.07f), gloss: 0.5f);
    private static Material TPTerracotta => PM("Terracotta", new Color(0.58f, 0.32f, 0.20f), gloss: 0.15f);
    private static Material TPTunaRed => PM("TunaRed", new Color(0.62f, 0.07f, 0.09f), gloss: 0.7f);
    private static Material TPTunaPink => PM("TunaPink", new Color(0.86f, 0.46f, 0.46f), gloss: 0.7f);
    private static Material TPBulb => PMGlow("LampBulb", new Color(1f, 0.93f, 0.72f), 90f);
    private static Material TPOrangePaint => PM("MirrorPole", new Color(0.86f, 0.44f, 0.08f), gloss: 0.5f);

    // ------------------------------------------------------------------ helpers

    /// <summary>Smooth torus: ring radius R around axis at c, tube radius r.</summary>
    private static void PTorus(PB b, Vector3 c, Vector3 axis, float R, float r, int seg = 16, int tube = 8)
    {
        axis.Normalize();
        var u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        var w = Vector3.Cross(axis, u);
        Vector3 Ctr(int i) { float a = i * Mathf.PI * 2f / seg; return c + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * R; }
        PGrid(b, seg, tube,
              (i, j) =>
              {
                  var cl = Ctr(i);
                  var o = (cl - c).normalized;
                  float ph = j * Mathf.PI * 2f / tube;
                  return cl + (o * Mathf.Cos(ph) + axis * Mathf.Sin(ph)) * r;
              },
              (i, j) => new Vector2((float)i / seg, (float)j / tube), (i, j) => Ctr(i));
    }

    /// <summary>A sagging rope/cable of half-thickness r (square section).</summary>
    private static void PRope(PB b, Vector3 p0, Vector3 p1, float sag, float r, int segs = 8)
    {
        Vector3 Pt(int i) { float t = (float)i / segs; return Vector3.Lerp(p0, p1, t) - Vector3.up * (sag * 4f * t * (1f - t)); }
        for (int i = 0; i < segs; i++) Beam(b, Pt(i), Pt(i + 1), r);
    }

    /// <summary>Flat ground disc (manholes, puddles): r(theta) radius, UV mapped as a disc.</summary>
    private static void PDisc(PB b, Vector3 c, Vector3 ax, Vector3 az, System.Func<float, float> r, int seg = 20)
    {
        PGrid(b, seg, 1,
              (i, j) =>
              {
                  float t = i * Mathf.PI * 2f / seg;
                  return c + (ax * Mathf.Cos(t) + az * Mathf.Sin(t)) * (j == 0 ? 0.002f : r(t));
              },
              (i, j) =>
              {
                  float t = i * Mathf.PI * 2f / seg;
                  return j == 0 ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f + 0.5f * Mathf.Cos(t), 0.5f + 0.5f * Mathf.Sin(t));
              },
              (i, j) => c - Vector3.up);
    }

    private static float Hash01(float x, float z, int salt = 0) =>
        Mathf.Repeat(Mathf.Sin(x * 12.9898f + z * 78.233f + salt * 37.719f) * 43758.5453f, 1f);

    // ------------------------------------------------------------------ orchestrator

    private static void BuildPortDetail(PortBins big, PortBins small, PFrame s1, PFrame s2, System.Random rng,
                                        PortCast cast, Transform people, Transform root)
    {
        int before = _portPeople;
        LanternStrings(small, s1, -60f, 60f);
        LanternStrings(small, s2, -74f, 74f);
        StreetVehicles(big, small, s1, s2, rng);
        StreetFurniture(big, small, s1, s2, cast, people);
        GroundDetail(small, s1, s2, rng);
        TunaCuttingShow(big, small, s1, rng, cast, people);
        QuayDressing(big, small, s1, rng, cast, people);
        MooringAndLamps(small, s1);
        FishermensShrine(big, small, s1, cast, people);
        BreakwaterAnglers(small, s1, cast, people);
        MarketHallDetail(small, s1, rng);
        Wildlife(small, s1, s2, rng);
        Debug.Log($"[tunaport] detail pass: +{_portPeople - before} townsfolk.");
    }

    // ------------------------------------------------------------------ street

    /// <summary>Strings of chochin paper lanterns across the road on slim festival poles.</summary>
    private static void LanternStrings(PortBins small, PFrame f, float a0, float a1)
    {
        int strings = 0;
        for (float a = a0; a <= a1 + 0.01f; a += 26f)
        {
            small.SetChunk(a);
            var pl = f.W(a, -5.35f, f.RoadY(a) + 0.04f); var pr = f.W(a + 3f, 5.3f, f.RoadY(a + 3f) + 0.12f);
            foreach (var pb in new[] { pl, pr })
            {
                PCyl(small[TPSteelGrey], pb, Vector3.up, 0.05f, 0.04f, 6.4f, 8, true);
                PCyl(small[TPConcrete], pb, Vector3.up, 0.16f, 0.14f, 0.3f, 10, true);
            }
            var t0 = pl + Vector3.up * 6.2f; var t1 = pr + Vector3.up * 6.2f;
            const float sag = 0.9f;
            PRope(small[TPBlack], t0, t1, sag, 0.008f, 10);
            int n = Mathf.FloorToInt((t1 - t0).magnitude / 1.35f);
            for (int k = 1; k < n; k++)
            {
                float t = (float)k / n;
                var p = Vector3.Lerp(t0, t1, t) - Vector3.up * (sag * 4f * t * (1f - t));
                PLantern(small, p, k % 2 == 0 ? 0 : (strings % 2 == 0 ? 1 : 2), 0.52f);
            }
            strings++;
        }
    }

    /// <summary>Kei trucks (the port's white mini trucks), a reefer truck, Super Cubs and mamachari.</summary>
    private static void StreetVehicles(PortBins big, PortBins small, PFrame s1, PFrame s2, System.Random rng)
    {
        int k = 0;
        // nose-in against the sea-wall railing at the apron ends (clear of the hall and the turret trucks)
        foreach (float a in new[] { -63f, -59.2f, -55.4f, 51f })
        {
            big.SetChunk(a); small.SetChunk(a);
            KeiTruck(big, small, s1.W(a, 34.6f, s1.RoadY(a) + 0.12f), s1.N, k++, rng);
        }
        foreach (var (a, n) in new[] { (-44f, 44f), (-6f, 46f), (26f, 43.5f), (58f, 46f) })
        {
            big.SetChunk(a); small.SetChunk(a);
            KeiTruck(big, small, s1.W(a, n, PortQuayY), rng.NextDouble() < 0.5 ? s1.T : -s1.T, k++, rng);
        }
        big.SetChunk(61f); small.SetChunk(61f);
        ReeferTruck(big, small, s1.W(61f, 30.5f, s1.RoadY(61f) + 0.12f), -s1.T);

        int bikes = 0;
        foreach (var (f, a0, a1, poles0) in new[] { (s1, -66f, 58f, -66f), (s2, -76f, 76f, -74f) })
            for (float a = a0; a < a1; a += 3.1f + (float)rng.NextDouble() * 5f)
            {
                if (rng.NextDouble() < 0.45) continue;
                if (Mathf.Repeat(a - poles0, 31f) < 1.6f || Mathf.Repeat(a - poles0, 31f) > 29.4f) continue;   // utility poles
                if (Mathf.Repeat(a + 60f, 26f) < 1.4f) continue;                                               // lantern poles
                big.SetChunk(a); small.SetChunk(a);
                var at = f.W(a, -5.8f - (float)rng.NextDouble() * 0.3f, f.RoadY(a) + 0.04f);
                var dir = Quaternion.Euler(0f, 75f + (float)rng.NextDouble() * 30f, 0f) * f.T;
                if (rng.NextDouble() < 0.4) SuperCub(small, at, dir, bikes); else Mamachari(small, at, dir, bikes);
                bikes++;
            }
        Debug.Log($"[tunaport] vehicles: {k} kei trucks, 1 reefer, {bikes} bikes/scooters.");
    }

    private static readonly Color[] KeiCols =
        { new Color(0.86f, 0.87f, 0.88f), new Color(0.86f, 0.87f, 0.88f), new Color(0.86f, 0.87f, 0.88f),
          new Color(0.30f, 0.44f, 0.62f), new Color(0.74f, 0.74f, 0.70f) };

    private static void KeiTruck(PortBins big, PortBins small, Vector3 at, Vector3 fwd, int idx, System.Random r)
    {
        fwd.y = 0f; fwd.Normalize();
        var sd = Vector3.Cross(Vector3.up, fwd); var up = Vector3.up;
        var col = KeiCols[idx % KeiCols.Length];
        var body = PM("Kei" + ColorUtility.ToHtmlStringRGB(col), col, gloss: 0.7f, spec: 0.4f);
        const float W = 0.72f;
        foreach (float x in new[] { 1.05f, -1.0f })
            foreach (int sg in new[] { -1, 1 })
            {
                var wc = at + fwd * x + sd * (sg * (W - 0.1f)) + up * 0.28f;
                PCyl(big[TPRubber], wc - sd * (sg * 0.09f), sd * sg, 0.28f, 0.28f, 0.18f, 14, true);
                PCyl(_fine[TPSteelGrey], wc + sd * (sg * 0.092f), sd * sg, 0.15f, 0.15f, 0.01f, 10, true);
            }
        PBox(big[TPBlack], at + up * 0.45f, fwd * 1.65f, up * 0.12f, sd * (W - 0.06f));
        var cab = at + fwd * 1.05f + up * 1.2f;
        PBox(big[body], cab, fwd * 0.6f, up * 0.62f, sd * W);
        PEllipsoid(big[body], cab + fwd * 0.55f - up * 0.05f, fwd * 0.12f, up * 0.58f, sd * W * 0.99f, 14, 8);
        PBox(small[TPGlass], cab + fwd * 0.64f + up * 0.28f, fwd * 0.02f, up * 0.24f, sd * (W - 0.1f));
        foreach (int sg in new[] { -1, 1 })
        {
            PCard(small[TPGlass], cab + sd * (sg * (W + 0.005f)) + up * 0.28f + fwd * 0.1f, fwd * 0.38f, up * 0.22f, sd * sg);
            PEllipsoid(_fine[TPWhitePaint], cab + fwd * 0.66f - up * 0.35f + sd * (sg * 0.5f), fwd * 0.03f, up * 0.07f, sd * 0.1f, 14, 8);
            PBox(_fine[TPBlack], cab + fwd * 0.2f + up * 0.35f + sd * (sg * (W + 0.1f)), fwd * 0.08f, up * 0.06f, sd * 0.03f);
        }
        PBox(small[TPBlack], cab + fwd * 0.68f - up * 0.52f, fwd * 0.06f, up * 0.1f, sd * (W + 0.02f));
        var bed = at + fwd * -0.55f + up * 0.62f;
        PBox(big[body], bed, fwd * 1.05f, up * 0.04f, sd * W, 1f, true);
        foreach (int sg in new[] { -1, 1 })
            PBox(small[body], bed + sd * (sg * (W - 0.02f)) + up * 0.18f, fwd * 1.05f, up * 0.16f, sd * 0.02f);
        PBox(small[body], bed - fwd * 1.03f + up * 0.18f, fwd * 0.02f, up * 0.16f, sd * W);
        PBox(small[TPSteelGrey], bed + fwd * 1.02f + up * 0.45f, fwd * 0.03f, up * 0.42f, sd * W);
        var fr = PortFrameFrom(sd, fwd);
        double pick = r.NextDouble();
        if (pick < 0.45)
            for (int k = 0; k < 4; k++)
                PCrateStack(small, fr, bed + up * 0.05f + fwd * (-0.7f + (k % 2) * 0.9f) + sd * ((k / 2 - 0.5f) * 0.66f),
                            1 + r.Next(3), r.NextDouble() < 0.6, r);
        else if (pick < 0.75)
        {
            PBox(small[TPCrateBlue], bed + up * 0.08f, fwd * 1.0f, up * 0.03f, sd * (W - 0.05f));
            PTuna(small, bed + up * 0.3f + fwd * 0.95f, -fwd, up, sd, 1.95f, true, bed.y + 0.12f);
        }
        else
        {
            PEllipsoid(small[TPNetGreen], bed + up * 0.35f, fwd * 0.9f, up * 0.35f, sd * 0.6f, 14, 8);
            foreach (float x in new[] { -0.6f, 0.2f })
                PEllipsoid(small[TPBuoy], bed + up * 0.55f + fwd * x, Vector3.right * 0.22f, up * 0.22f, Vector3.forward * 0.22f, 14, 8);
        }
    }

    private static void ReeferTruck(PortBins big, PortBins small, Vector3 at, Vector3 fwd)
    {
        fwd.y = 0f; fwd.Normalize();
        var sd = Vector3.Cross(Vector3.up, fwd); var up = Vector3.up;
        foreach (float x in new[] { 2.4f, -1.6f, -2.6f })
            foreach (int sg in new[] { -1, 1 })
                PCyl(big[TPRubber], at + fwd * x + sd * (sg * 0.95f) + up * 0.45f - sd * (sg * 0.12f), sd * sg, 0.45f, 0.45f, 0.26f, 14, true);
        PBox(big[TPBlack], at + up * 0.85f, fwd * 3.5f, up * 0.15f, sd * 0.9f);
        var cab = at + fwd * 2.7f + up * 1.9f;
        PBox(big[TPWhitePaint], cab, fwd * 0.8f, up * 1.0f, sd * 1.1f);
        PBox(small[TPGlass], cab + fwd * 0.81f + up * 0.35f, fwd * 0.02f, up * 0.4f, sd * 1.0f);
        var box = at + fwd * -0.9f + up * 2.35f;
        PBox(big[TPWhitePaint], box, fwd * 2.55f, up * 1.35f, sd * 1.2f, 1f, true);
        PBox(small[TPNavy], box + up * 0.2f, fwd * 2.56f, up * 0.12f, sd * 1.21f);
        PBox(small[TPSteelGrey], box + fwd * 2.6f + up * 0.9f, fwd * 0.1f, up * 0.35f, sd * 0.7f);
        foreach (int sg in new[] { -1, 1 })
            PCard(small[TPShopSign(1)], box + sd * (sg * 1.215f) - up * 0.35f, fwd * 1.8f, up * 0.45f, sd * sg);
    }

    private static void SuperCub(PortBins small, Vector3 at, Vector3 fwd, int i)
    {
        fwd.y = 0f; fwd.Normalize();
        var sd = Vector3.Cross(Vector3.up, fwd); var up = Vector3.up;
        var body = i % 3 == 0 ? TPRedPaint : i % 3 == 1 ? PM("CubGreen", new Color(0.36f, 0.52f, 0.40f), gloss: 0.6f) : TPWhitePaint;
        foreach (float x in new[] { 0.62f, -0.6f })
        {
            PCyl(small[TPRubber], at + fwd * x + up * 0.3f - sd * 0.05f, sd, 0.3f, 0.3f, 0.1f, 14, true);
            PCyl(_fine[TPSteelGrey], at + fwd * x + up * 0.3f + sd * 0.052f, sd, 0.12f, 0.12f, 0.005f, 8, true);
        }
        PEllipsoid(small[body], at + up * 0.62f + fwd * 0.28f, fwd * 0.12f, up * 0.36f, sd * 0.24f, 14, 8);
        PEllipsoid(small[body], at + up * 0.55f - fwd * 0.25f, fwd * 0.42f, up * 0.2f, sd * 0.16f, 14, 8);
        PBox(small[TPBlack], at + up * 0.8f - fwd * 0.25f, fwd * 0.28f, up * 0.05f, sd * 0.13f);
        Beam(small[TPSteelGrey], at + up * 0.3f + fwd * 0.62f, at + up * 1.0f + fwd * 0.45f, 0.025f);
        Beam(small[TPSteelGrey], at + up * 1.02f + fwd * 0.44f - sd * 0.33f, at + up * 1.02f + fwd * 0.44f + sd * 0.33f, 0.018f);
        PEllipsoid(_fine[TPWhitePaint], at + up * 1.0f + fwd * 0.55f, fwd * 0.05f, up * 0.08f, sd * 0.08f, 14, 8);
        var rack = at + up * 0.86f - fwd * 0.62f;
        PBox(small[TPSteelGrey], rack, fwd * 0.2f, up * 0.015f, sd * 0.18f);
        PBox(small[TPStyrofoam], rack + up * 0.16f, fwd * 0.22f, up * 0.15f, sd * 0.2f, 1f, true);
    }

    private static void Mamachari(PortBins small, Vector3 at, Vector3 fwd, int i)
    {
        fwd.y = 0f; fwd.Normalize();
        var sd = Vector3.Cross(Vector3.up, fwd); var up = Vector3.up;
        var frame = i % 4 == 0 ? PM("BikeSilver", new Color(0.70f, 0.72f, 0.74f), gloss: 0.7f)
                  : i % 4 == 1 ? PM("BikeCream", new Color(0.82f, 0.78f, 0.66f), gloss: 0.6f)
                  : i % 4 == 2 ? TPNavy : PM("BikeRed", new Color(0.56f, 0.10f, 0.10f), gloss: 0.6f);
        foreach (float x in new[] { 0.52f, -0.52f })
        {
            PTorus(_fine[TPRubber], at + fwd * x + up * 0.33f, sd, 0.31f, 0.02f, 18, 5);
            PCyl(_fine[TPSteelGrey], at + fwd * x + up * 0.33f - sd * 0.02f, sd, 0.04f, 0.04f, 0.04f, 6, true);
        }
        var bb = at + up * 0.3f; var seat = at + up * 0.82f - fwd * 0.2f; var head = at + up * 0.88f + fwd * 0.38f;
        Beam(small[frame], bb, seat, 0.018f);
        Beam(small[frame], bb, head - up * 0.12f, 0.018f);
        Beam(small[frame], head, at + up * 0.33f + fwd * 0.52f, 0.015f);
        Beam(small[frame], bb, at + up * 0.33f - fwd * 0.52f, 0.013f);
        Beam(small[frame], seat, at + up * 0.33f - fwd * 0.52f, 0.013f);
        PBox(small[TPBlack], seat + up * 0.05f, fwd * 0.13f, up * 0.03f, sd * 0.08f);
        Beam(small[TPSteelGrey], head + up * 0.1f - sd * 0.3f, head + up * 0.1f + sd * 0.3f, 0.012f);
        PBox(_fine[TPSteelGrey], head + fwd * 0.2f - up * 0.02f, fwd * 0.18f, up * 0.12f, sd * 0.2f, 1f, true);
        PBox(small[frame], at + up * 0.75f - fwd * 0.52f, fwd * 0.16f, up * 0.015f, sd * 0.12f);
    }

    /// <summary>Red post box, bus stop, traffic mirrors and benches with seated townsfolk.</summary>
    private static void StreetFurniture(PortBins big, PortBins small, PFrame s1, PFrame s2, PortCast cast, Transform people)
    {
        {
            const float a = -30.5f;
            small.SetChunk(a); big.SetChunk(a);
            var at = s1.W(a, -6.1f, s1.RoadY(a) + 0.04f);
            PCyl(big[TPRedPaint], at, Vector3.up, 0.24f, 0.24f, 1.15f, 16, false);
            PEllipsoid(big[TPRedPaint], at + Vector3.up * 1.15f, Vector3.right * 0.27f, Vector3.up * 0.14f, Vector3.forward * 0.27f, 16, 8);
            PBox(_fine[TPBlack], at + Vector3.up * 0.95f + s1.N * 0.24f, s1.T * 0.12f, Vector3.up * 0.02f, s1.N * 0.01f);
            PCyl(small[TPConcreteDark], at - Vector3.up * 0.02f, Vector3.up, 0.3f, 0.3f, 0.06f, 12, true);
        }
        {
            const float a = 34f;
            small.SetChunk(a); big.SetChunk(a);
            var at = s1.W(a, -5.7f, s1.RoadY(a) + 0.04f);
            PCyl(small[TPSteelGrey], at, Vector3.up, 0.04f, 0.04f, 2.6f, 8, true);
            PCyl(small[TPConcrete], at, Vector3.up, 0.22f, 0.2f, 0.18f, 12, true);
            PCard(small[PM("BusStop", Color.white, "TP_BusStop", twoSided: true, gloss: 0.3f)], at + Vector3.up * 2.35f,
                  s1.T * 0.3f, Vector3.up * 0.3f, s1.N, twoFaced: true);
            PBox(small[TPWhitePaint], at + Vector3.up * 1.4f + s1.N * 0.05f, s1.T * 0.2f, Vector3.up * 0.3f, s1.N * 0.02f);
            if (cast.sit.Count > 0)
                PortPerson(cast.sit, people, s1.W(a + 2.2f, -7.6f, s1.RoadY(a) + 0.04f), s1.N, MinatoCrowdActor.MotionKind.Sit,
                           0.31f, "Shopper");
            PortPerson(cast.stand, people, s1.W(a + 0.6f, -6.5f, s1.RoadY(a) + 0.04f), -s1.T, MinatoCrowdActor.MotionKind.Idle,
                       0.63f, "Shopper");
        }
        foreach (float a in new[] { -78f, 78f })
        {
            small.SetChunk(a);
            var at = s2.W(a, -5.4f, s2.RoadY(a) + 0.04f);
            PCyl(small[TPOrangePaint], at, Vector3.up, 0.04f, 0.04f, 3.0f, 8, true);
            var face = (s2.N + (a < 0 ? s2.T : -s2.T) * 0.8f).normalized;
            var mc = at + Vector3.up * 3.0f + face * 0.12f;
            PTorus(small[TPOrangePaint], mc, face, 0.4f, 0.04f, 18, 6);
            PEllipsoid(small[PM("Mirror", new Color(0.72f, 0.78f, 0.84f), gloss: 0.97f, spec: 0.8f)], mc,
                       Vector3.Cross(face, Vector3.up).normalized * 0.4f, Vector3.up * 0.4f, face * 0.06f, 16, 8);
        }
        if (cast.sit.Count > 0)
        {
            foreach (float a in new[] { 55.5f, 58.6f })
                PortPerson(cast.sit, people, s1.W(a, 10.0f, s1.RoadY(a) + 0.12f), -s1.N, MinatoCrowdActor.MotionKind.Sit,
                           Hash01(a, 2f), "Shopper");
            foreach (float a in new[] { -80.5f, 80.5f })
                PortPerson(cast.sit, people, s2.W(a, 8.4f, s2.RoadY(a) + 0.04f), -s2.N, MinatoCrowdActor.MotionKind.Sit,
                           Hash01(a, 5f), "Fisherman");
        }
    }

    /// <summary>Design manhole covers, kerb drain channels with grates, and hose-down puddles.</summary>
    private static void GroundDetail(PortBins small, PFrame s1, PFrame s2, System.Random rng)
    {
        var man = PM("Manhole", Color.white, "TP_Manhole", gloss: 0.45f);
        foreach (var (f, a, n, dy) in new[] { (s1, -52f, 22f, 0.12f), (s1, 30f, 11f, 0.12f), (s1, -12f, -7.6f, 0.04f),
                                              (s1, 44f, -7.2f, 0.04f), (s2, -40f, -7.6f, 0.04f), (s2, 22f, -7.4f, 0.04f),
                                              (s2, 60f, 7.4f, 0.04f) })
        {
            small.SetChunk(a);
            PDisc(small[man], f.W(a, n, f.RoadY(a) + dy + 0.006f), f.T, f.N, t => 0.32f);
        }
        foreach (var (f, a0, a1) in new[] { (s1, -70f, 62f), (s2, -80f, 80f) })
            for (float a = a0; a < a1; a += 1f)
            {
                small.SetChunk(a);
                var c = f.W(a + 0.5f, -4.72f, f.RoadY(a) + 0.045f);
                PBox(small[TPBlack], c, f.T * 0.48f, Vector3.up * 0.004f, f.N * 0.14f);
                for (int k = -2; k <= 2; k++)
                    PBox(_fine[TPSteelGrey], c + f.T * (k * 0.18f) + Vector3.up * 0.006f, f.T * 0.02f, Vector3.up * 0.004f, f.N * 0.14f);
            }
        for (int k = 0; k < 70; k++)
        {
            PFrame f; float a, n, y;
            double w = rng.NextDouble();
            if (w < 0.35) { f = s1; a = -64f + (float)rng.NextDouble() * 128f; n = 40f + (float)rng.NextDouble() * 30f; y = PortQuayY; }
            else if (w < 0.6) { f = s1; a = -64f + (float)rng.NextDouble() * 128f; n = 8.8f + (float)rng.NextDouble() * 4f; y = s1.RoadY(a) + 0.12f; }
            else if (w < 0.8) { f = s1; a = -68f + (float)rng.NextDouble() * 128f; n = -5.4f - (float)rng.NextDouble() * 4.5f; y = s1.RoadY(a) + 0.04f; }
            else { f = s2; a = -78f + (float)rng.NextDouble() * 156f; n = -5.4f - (float)rng.NextDouble() * 4.5f; y = s2.RoadY(a) + 0.04f; }
            small.SetChunk(a);
            float rx = 0.4f + (float)rng.NextDouble() * 1.4f, rz = rx * (0.35f + (float)rng.NextDouble() * 0.5f);
            float rot = (float)rng.NextDouble() * Mathf.PI;
            var ax = new Vector3(Mathf.Cos(rot), 0f, Mathf.Sin(rot)); var az = new Vector3(-ax.z, 0f, ax.x);
            PDisc(small[TPPuddle], f.W(a, n, y + 0.004f), ax * rx, az * rz, t => 1f + 0.18f * Mathf.Sin(t * 3f + rot * 5f), 14);
        }
    }
}

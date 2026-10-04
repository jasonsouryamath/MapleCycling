using UnityEngine;

// Tuna port, part 8c: market-hall dressing, gulls and harbour cats, shopfront extras and rooftop
// clutter (see TunaPort.Detail.cs). Shop and roof extras are hashed from their position, never from
// the pass-1 RNG, so the pass-1 layout is unchanged.
public static partial class ShiosaiCoastEnvironment
{
    /// <summary>Lot placards at every fish, hanging SERI-BA signs, ice tubs, row numbers, scales.</summary>
    private static void MarketHallDetail(PortBins small, PFrame f, System.Random rng)
    {
        var up = Vector3.up;
        var lots = PM("LotCards", Color.white, "TP_LotCards", twoSided: true, gloss: 0.2f);
        var seri = PM("Seri", Color.white, "TP_Seri", twoSided: true, gloss: 0.2f);
        float[] rows = { 16.4f, 18.9f, 23.8f, 26.2f, 30.4f };
        int cards = 0;
        foreach (float n in rows)
        {
            // painted row number at the head of the row
            small.SetChunk(HallA0 + 1f);
            PQ(small[TPYellow], f.W(HallA0 + 0.7f, n - 0.4f, f.RoadY(HallA0) + 0.137f), f.W(HallA0 + 1.6f, n - 0.4f, f.RoadY(HallA0) + 0.137f),
               f.W(HallA0 + 1.6f, n + 0.4f, f.RoadY(HallA0) + 0.137f), f.W(HallA0 + 0.7f, n + 0.4f, f.RoadY(HallA0) + 0.137f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, up);
            for (float a = HallA0 + 2.6f; a < HallA1 - 2.4f; a += 2.35f)
            {
                if ((Mathf.Abs(a + 20f) < 4.8f || Mathf.Abs(a - 20f) < 4.8f) && n > 21f && n < 29f) continue;
                small.SetChunk(a);
                float fl = f.RoadY(a) + 0.135f;
                int i = (cards * 5 + (int)n) % 8;
                float u0 = (i % 4) * 0.25f, v0 = 1f - (i / 4 + 1) * 0.5f;
                var stake = f.W(a + 1.05f, n + 0.62f, fl);
                Beam(_fine[TPWood], stake, stake + up * 0.32f, 0.01f);
                var cn = (-f.N * 0.5f + up * 0.85f).normalized;
                PCard(_fine[lots], stake + up * 0.38f, f.T * 0.1f, Vector3.Cross(cn, f.T).normalized * 0.1f, cn, false,
                      u0, u0 + 0.25f, v0, v0 + 0.5f);
                cards++;
            }
        }
        // せり場 signs hung under the trusses, and blue ice tubs along the aisles
        foreach (float a in new[] { -32f, -8f, 8f, 32f })
        {
            small.SetChunk(a);
            float fl = f.RoadY(a) + 0.12f;
            var sc = f.W(a, (HallN0 + HallN1) * 0.5f, fl + 5.3f);
            PCard(small[seri], sc, f.T * 0.9f, up * 0.45f, -f.N, twoFaced: true);
            foreach (int sg in new[] { -1, 1 })
                Beam(_fine[TPBlack], sc + f.T * (sg * 0.8f) + up * 0.45f, sc + f.T * (sg * 0.8f) + up * (HallRidge - 5.6f), 0.006f);
        }
        for (int k = 0; k < 12; k++)
        {
            float a = HallA0 + 4f + k * 7.6f + (float)rng.NextDouble() * 2f;
            float n = k % 2 == 0 ? 21.2f : 28.6f;
            small.SetChunk(a);
            var c = f.W(a, n, f.RoadY(a) + 0.135f);
            PCyl(small[TPCrateBlue], c, up, 0.42f, 0.48f, 0.55f, 14, false);
            PCyl(small[TPStyrofoam], c + up * 0.5f, up, 0.43f, 0.43f, 0.02f, 14, true);
        }
        // platform scales beside the auctioneers' stands
        foreach (float sa in new[] { -20f, 20f })
        {
            small.SetChunk(sa);
            var c = f.W(sa + (sa < 0 ? -2.2f : 2.2f), 24.6f, f.RoadY(sa) + 0.135f);
            PBoxF(small[TPSteelGrey], f, c + up * 0.06f, 0.6f, 0.06f, 0.45f);
            PCyl(small[TPSteelGrey], c + f.N * 0.45f, up, 0.04f, 0.04f, 1.1f, 8, true);
            PCyl(small[TPWhitePaint], c + f.N * 0.47f + up * 1.2f, -f.N, 0.18f, 0.18f, 0.06f, 14, true);
            PCyl(_fine[TPBlack], c + f.N * 0.405f + up * 1.2f, -f.N, 0.15f, 0.15f, 0.005f, 14, true);
        }
        Debug.Log($"[tunaport] hall detail: {cards} lot placards.");
    }

    // ------------------------------------------------------------------ gulls and cats

    private static Material TPGullWhite => PM("GullWhite", new Color(0.90f, 0.91f, 0.92f), twoSided: true, gloss: 0.2f);
    private static Material TPGullGrey => PM("GullGrey", new Color(0.52f, 0.56f, 0.60f), twoSided: true, gloss: 0.2f);
    private static Material TPBeak => PM("Beak", new Color(0.92f, 0.72f, 0.12f), gloss: 0.4f);

    /// <summary>Black-tailed gull. flying: wings spread in the gull 'M'; else perched, wings folded.</summary>
    private static void Gull(PortBins bins, Vector3 p, Vector3 d, bool flying, float flap = 0f)
    {
        d.y = 0f; d.Normalize();
        var up = Vector3.up; var sd = Vector3.Cross(up, d);
        float bodyY = flying ? 0f : 0.11f;
        var c = p + up * bodyY;
        GEll(bins[TPGullWhite], c, d * 0.21f, up * 0.075f, sd * 0.075f, 8, 5);
        GEll(bins[TPGullWhite], c + d * 0.2f + up * 0.06f, d * 0.055f, up * 0.05f, sd * 0.045f, 8, 5);
        Beam(bins[TPBeak], c + d * 0.25f + up * 0.055f, c + d * 0.32f + up * 0.045f, 0.012f);
        PTri(bins[TPBlack], c - d * 0.18f + up * 0.02f - sd * 0.05f, c - d * 0.18f + up * 0.02f + sd * 0.05f, c - d * 0.3f + up * 0.03f,
             Vector2.zero, Vector2.right, Vector2.up, up);
        if (!flying)
        {
            GEll(bins[TPGullGrey], c + up * 0.035f - d * 0.03f, d * 0.19f, up * 0.05f, sd * 0.08f, 8, 5);
            Beam(bins[TPBlack], c + up * 0.04f - d * 0.15f, c - d * 0.3f + up * 0.05f, 0.02f);
            foreach (int sg in new[] { -1, 1 })
                Beam(bins[TPBeak], c + sd * (sg * 0.03f) - up * 0.05f, p + sd * (sg * 0.03f) + d * 0.02f, 0.008f);
            return;
        }
        foreach (int sg in new[] { -1, 1 })
        {
            float lift = 0.12f + 0.1f * flap;
            var sh = c + up * 0.03f;
            var elbow = sh + sd * (sg * 0.38f) + up * lift + d * 0.02f;
            var tip = elbow + sd * (sg * 0.42f) - up * (0.06f + 0.14f * flap) - d * 0.1f;
            PQ(bins[TPGullGrey], sh + d * 0.08f, elbow + d * 0.08f, elbow - d * 0.1f, sh - d * 0.1f,
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, up);
            PQ(bins[TPGullGrey], elbow + d * 0.08f, tip + d * 0.02f, tip - d * 0.05f, elbow - d * 0.1f,
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, up);
            PTri(bins[TPBlack], tip + d * 0.02f, tip - d * 0.05f, tip + sd * (sg * 0.1f) - up * 0.02f,
                 Vector2.zero, Vector2.right, Vector2.up, up);
        }
    }

    private static readonly Color[] CatCols =
        { new Color(0.86f, 0.85f, 0.82f), new Color(0.78f, 0.44f, 0.14f), new Color(0.05f, 0.05f, 0.06f), new Color(0.44f, 0.43f, 0.40f) };

    /// <summary>Harbour cat: sitting upright or curled in a loaf. calico = white with ginger/black patches.</summary>
    private static void Cat(PortBins bins, Vector3 p, Vector3 d, int coat, bool loaf)
    {
        d.y = 0f; d.Normalize();
        var up = Vector3.up; var sd = Vector3.Cross(up, d);
        bool calico = coat >= CatCols.Length;
        var col = CatCols[calico ? 0 : coat];
        var fur = PM("Cat" + ColorUtility.ToHtmlStringRGB(col), col, gloss: 0.12f);
        var ginger = PM("Cat" + ColorUtility.ToHtmlStringRGB(CatCols[1]), CatCols[1], gloss: 0.12f);
        var pink = PM("CatNose", new Color(0.86f, 0.52f, 0.52f), gloss: 0.3f);
        Vector3 head;
        if (loaf)
        {
            GEll(bins[fur], p + up * 0.085f, d * 0.19f, up * 0.085f, sd * 0.1f, 10, 6);
            head = p + up * 0.13f + d * 0.19f;
            for (int k = 0; k < 4; k++)
            {
                float t0 = k / 4f, t1 = (k + 1) / 4f;
                Vector3 T(float t) => p + up * 0.03f + d * (-0.18f + 0.3f * t) + sd * (0.12f * Mathf.Sin(t * Mathf.PI * 0.9f));
                Beam(bins[fur], T(t0), T(t1), 0.018f);
            }
        }
        else
        {
            GEll(bins[fur], p + up * 0.07f - d * 0.02f, d * 0.12f, up * 0.075f, sd * 0.1f, 10, 6);
            GEll(bins[fur], p + up * 0.17f + d * 0.02f, d * 0.08f, up * 0.12f, sd * 0.075f, 10, 6);
            foreach (int sg in new[] { -1, 1 })
                Beam(bins[fur], p + up * 0.13f + d * 0.08f + sd * (sg * 0.03f), p + d * 0.1f + sd * (sg * 0.03f), 0.016f);
            head = p + up * 0.32f + d * 0.05f;
            Beam(bins[fur], p + up * 0.02f - d * 0.12f, p + up * 0.02f - d * 0.02f + sd * 0.14f, 0.018f);
            Beam(bins[fur], p + up * 0.02f - d * 0.02f + sd * 0.14f, p + up * 0.02f + d * 0.12f + sd * 0.1f, 0.016f);
        }
        GEll(bins[fur], head, d * 0.06f, up * 0.058f, sd * 0.068f, 10, 6);
        foreach (int sg in new[] { -1, 1 })
            PTri(bins[fur], head + up * 0.04f + sd * (sg * 0.02f), head + up * 0.04f + sd * (sg * 0.06f), head + up * 0.1f + sd * (sg * 0.045f),
                 Vector2.zero, Vector2.right, Vector2.up, d);
        GEll(bins[pink], head + d * 0.06f, d * 0.008f, up * 0.008f, sd * 0.01f, 6, 4);
        if (calico)
        {
            GEll(bins[ginger], head + up * 0.03f + sd * 0.025f - d * 0.005f, d * 0.05f, up * 0.035f, sd * 0.05f, 8, 5);
            GEll(bins[PM("Cat" + ColorUtility.ToHtmlStringRGB(CatCols[2]), CatCols[2], gloss: 0.12f)],
                 p + up * (loaf ? 0.14f : 0.11f) - d * 0.06f - sd * 0.03f, d * 0.08f, up * 0.05f, sd * 0.07f, 8, 5);
        }
    }

    private static void Wildlife(PortBins small, PFrame s1, PFrame s2, System.Random rng)
    {
        var up = Vector3.up;
        int gulls = 0, cats = 0;
        Vector3 RandDir() { float t = (float)rng.NextDouble() * Mathf.PI * 2f; return new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)); }
        // perched on the quay-edge bollards
        for (float a = -65f; a < 96f; a += 10f)
        {
            if (rng.NextDouble() < 0.5) continue;
            small.SetChunk(a);
            Gull(_fine, s1.W(a, PortQuayFar - 0.8f, PortQuayY + 0.66f), RandDir(), false); gulls++;
        }
        // along the hall's ridge monitor
        for (int k = 0; k < 9; k++)
        {
            float a = -40f + (float)rng.NextDouble() * 80f;
            small.SetChunk(a);
            Gull(small, s1.W(a, (HallN0 + HallN1) * 0.5f + ((float)rng.NextDouble() - 0.5f) * 3f, s1.RoadY(a) + 0.12f + HallRidge + 1.18f),
                 rng.NextDouble() < 0.5 ? s1.N : -s1.N, false);
            gulls++;
        }
        // on the breakwater parapet
        for (int k = 0; k < 11; k++)
        {
            float n = 70f + (float)rng.NextDouble() * 130f;
            small.SetChunk(-74f);
            Gull(small, s1.W(-77.6f + ((float)rng.NextDouble() - 0.5f) * 1.2f, n, 4.6f), RandDir(), false); gulls++;
        }
        // mobbing the landings, and on the S2 parapet
        foreach (float la in new[] { -56f, 48f })
            for (int k = 0; k < 5; k++)
            {
                small.SetChunk(la);
                Gull(_fine, s1.W(la - 10f + (float)rng.NextDouble() * 20f, PortQuayFar - 2.4f + (float)rng.NextDouble() * 1.6f, PortQuayY),
                     RandDir(), false);
                gulls++;
            }
        for (int k = 0; k < 6; k++)
        {
            float a = -80f + (float)rng.NextDouble() * 160f;
            small.SetChunk(a);
            Gull(_fine, s2.W(a, 10.35f, s2.RoadY(a) + 0.94f), rng.NextDouble() < 0.5 ? s2.T : -s2.T, false); gulls++;
        }
        // wheeling over the basin and the quay
        for (int k = 0; k < 22; k++)
        {
            float a = -70f + (float)rng.NextDouble() * 170f, n = 40f + (float)rng.NextDouble() * 110f;
            small.SetChunk(a);
            Gull(small, s1.W(a, n, 7f + (float)rng.NextDouble() * 18f), RandDir(), true, (float)rng.NextDouble());
            gulls++;
        }
        // harbour cats: waiting by the landings, loafing on the S2 parapet and the shrine plinth
        foreach (var (f, a, n, y, loaf, face) in new[]
                 {
                     (s1, -60.5f, PortQuayFar - 12.5f, PortQuayY, false, s1.N), (s1, -51f, PortQuayFar - 12.2f, PortQuayY, false, s1.N),
                     (s1, 44f, PortQuayFar - 12.4f, PortQuayY, false, s1.N), (s1, 53.5f, PortQuayFar - 3.2f, PortQuayY, true, -s1.T),
                     (s2, -52f, 10.35f, s2.RoadY(-52f) + 0.94f, true, s2.T), (s2, 31f, 10.35f, s2.RoadY(31f) + 0.94f, true, -s2.T),
                     (s1, -65.4f, 47.9f, PortQuayY + 0.6f, true, s1.N), (s1, 47.5f, 10.2f, s1.RoadY(47.5f) + 0.12f, false, -s1.N),
                     (s2, -12f, -9.9f, s2.RoadY(-12f) + 0.04f, false, -s2.N), (s1, -20f, -9.9f, s1.RoadY(-20f) + 0.04f, true, s1.T),
                 })
        {
            small.SetChunk(a);
            Cat(_fine, f.W(a, n, y), face, cats % 5, loaf);
            cats++;
        }
        Debug.Log($"[tunaport] wildlife: {gulls} gulls, {cats} cats.");
    }

    // ------------------------------------------------------------------ shopfront extras (from ShopFront)

    private static Material TPMenu(int i) => PM($"Menu{i}", Color.white, $"TP_Menu_{i}", gloss: 0.25f);

    /// <summary>Per-shop extras hashed from the shop index: an A-frame menu board, a maneki-neko on
    /// the top shelf, a hanging dial scale, a himono drying rack or a box of the day's catch.</summary>
    private static void ShopExtras(PortBins small, Vector3 fb, Vector3 A, Vector3 F, float w, float h1, float recess, int shop)
    {
        if (_fine == null) return;
        var up = Vector3.up;
        // A-frame menu board toward the street
        {
            var mc = fb + F * 0.85f + A * (w * 0.3f);
            var tiltF = (up * 0.94f + F * 0.34f).normalized;
            PCard(small[TPMenu(shop % 3)], mc + up * 0.52f + F * 0.1f, A * 0.22f, tiltF * 0.44f, (F - up * 0.34f).normalized);
            PBox(_fine[TPWoodDark], mc + up * 0.5f + F * 0.08f, A * 0.25f, tiltF * 0.5f, F * 0.012f);
            PBox(_fine[TPWoodDark], mc + up * 0.5f - F * 0.18f, A * 0.23f, (up * 0.94f - F * 0.34f).normalized * 0.5f, F * 0.012f);
        }
        // maneki-neko beckoning from the end of the top shelf
        {
            var nc = fb - F * (recess - 0.3f) + up * (0.55f + 2 * 0.62f - 0.175f) + A * (w * 0.39f);
            GEll(_fine[TPWhitePaint], nc + up * 0.09f, A * 0.075f, up * 0.09f, F * 0.065f, 10, 6);
            GEll(_fine[TPWhitePaint], nc + up * 0.24f, A * 0.075f, up * 0.065f, F * 0.065f, 10, 6);
            GEll(_fine[TPWhitePaint], nc + up * 0.3f + A * 0.07f + F * 0.03f, A * 0.025f, up * 0.04f, F * 0.025f, 8, 5);
            PTorus(_fine[TPRedPaint], nc + up * 0.175f, up, 0.06f, 0.012f, 12, 4);
            PBox(_fine[PM("Gold", new Color(0.86f, 0.68f, 0.18f), gloss: 0.9f)], nc + up * 0.1f + F * 0.068f, A * 0.03f, up * 0.04f, F * 0.004f);
        }
        // hanging dial scale over the counter
        if (shop % 4 == 1)
        {
            var hc = fb - F * 0.7f + A * (w * 0.08f) + up * (h1 - 0.35f);
            Beam(_fine[TPSteelGrey], hc, hc - up * 0.45f, 0.006f);
            PCyl(small[TPWhitePaint], hc - up * 0.6f - F * 0.03f, F, 0.14f, 0.14f, 0.06f, 14, true);
            PCyl(_fine[TPBlack], hc - up * 0.6f + F * 0.031f, F, 0.12f, 0.12f, 0.003f, 12, true);
            foreach (float s in new[] { -1f, 1f })
                Beam(_fine[TPSteelGrey], hc - up * 0.74f, hc - up * 1.05f + A * (s * 0.16f), 0.004f);
            PCyl(small[TPSteelGrey], hc - up * 1.08f, up, 0.2f, 0.2f, 0.03f, 14, false);
        }
        // himono drying rack (the 干物 shops) or a box of the day's catch on a beer crate
        if (shop % 6 == 4)
        {
            var rc = fb + F * 1.45f - A * (w * 0.3f);
            var tilt = (up * 0.5f + F * 0.87f).normalized;
            var along = Vector3.Cross(A, tilt).normalized; if (Vector3.Dot(along, up) < 0) along = -along;
            PCard(small[PMCut("Himono", "TP_Himono")], rc + up * 0.85f, A * 0.62f, along * 0.32f, tilt);
            foreach (int s in new[] { -1, 1 })
            {
                Beam(_fine[TPWood], rc + A * (s * 0.6f) - F * 0.15f, rc + A * (s * 0.6f) + up * 1.1f - F * 0.15f, 0.02f);
                Beam(_fine[TPWood], rc + A * (s * 0.6f) + F * 0.2f, rc + A * (s * 0.6f) + up * 0.6f + F * 0.2f, 0.02f);
            }
        }
        else if (Hash01(fb.x, fb.z, shop) < 0.55f)
        {
            var bc = fb + F * 0.55f - A * (w * 0.3f);
            PBox(small[TPCrateOrange], bc + up * 0.16f, A * 0.22f, up * 0.16f, F * 0.18f, 1f, true);
            PBox(small[TPStyrofoam], bc + up * 0.44f, A * 0.3f, up * 0.12f, F * 0.22f, 1f, true);
            PBox(_fine[TPIceTray], bc + up * 0.565f, A * 0.27f, up * 0.004f, F * 0.19f);
            for (int k = 0; k < 4; k++)
                PTuna(_fine, bc + up * 0.58f + A * (-0.2f + k * 0.12f) - F * 0.14f, F, A, up, 0.3f, false, bc.y + 0.575f);
        }
    }

    // ------------------------------------------------------------------ rooftop clutter (from PMinka)

    /// <summary>Solar water heater or a TV antenna on the street slope, and laundry on the hisashi.</summary>
    private static void RoofExtras(PortBins small, Vector3 C, Vector3 A, Vector3 F, Vector3 ridge, float d, float w,
                                   float pitch, float wallTop, float y1, float h1, bool twoStorey, bool modern)
    {
        if (_fine == null) return;
        var up = Vector3.up;
        float h = Hash01(C.x, C.z, 3), h2 = Hash01(C.z, C.x, 7);
        var down = (F * Mathf.Cos(pitch * Mathf.Deg2Rad) - up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;
        var nr = Vector3.Cross(down, A).normalized; if (nr.y < 0) nr = -nr;
        if (h < 0.22f)
        {
            // taiyo-netsu: black collector panel with the white tank along its top edge
            var pc = ridge + down * (d * 0.22f) - A * (w * 0.18f) + nr * 0.2f;
            PBox(small[TPBlack], pc, A * 0.85f, nr * 0.05f, down * 0.55f);
            PBox(_fine[PM("SolarGlass", new Color(0.10f, 0.14f, 0.22f), gloss: 0.95f, spec: 0.6f)], pc + nr * 0.052f, A * 0.8f, nr * 0.002f, down * 0.5f);
            PCyl(small[TPWhitePaint], pc - down * 0.6f + nr * 0.18f - A * 0.9f, A, 0.2f, 0.2f, 1.8f, 12, true);
            foreach (int s in new[] { -1, 1 })
                Beam(_fine[TPSteelGrey], pc + A * (s * 0.8f) + down * 0.5f, pc + A * (s * 0.8f) + down * 0.5f - nr * 0.2f, 0.015f);
        }
        else if (h < 0.5f)
        {
            // Yagi TV antenna on a mast at the gable end
            var mb = ridge + A * (w * 0.35f) + up * 0.35f;
            Beam(_fine[TPSteelGrey], mb, mb + up * 2.1f, 0.018f);
            var boomC = mb + up * 1.9f;
            var bd = Quaternion.Euler(0f, 30f + h2 * 120f, 0f) * F;
            Beam(_fine[TPSteelGrey], boomC - bd * 0.8f, boomC + bd * 0.8f, 0.012f);
            var bx = Vector3.Cross(up, bd).normalized;
            for (int k = 0; k < 7; k++)
            {
                var e = boomC + bd * (-0.7f + k * 0.23f);
                float half = 0.32f - k * 0.025f;
                Beam(_fine[TPSteelGrey], e - bx * half, e + bx * half, 0.006f);
            }
            Beam(_fine[TPBlack], mb + up * 0.3f, mb - up * 0.3f + F * 0.5f, 0.008f);
        }
        // laundry on a pole over the pent roof (traditional two-storey houses)
        if (twoStorey && !modern && h2 < 0.35f)
        {
            var p0 = C + F * (d * 0.5f + 0.45f) + up * (y1 + h1 + 1.85f - C.y) - A * (w * 0.36f);
            Beam(small[TPAlu], p0, p0 + A * (w * 0.72f), 0.015f);
            int items = 3 + (int)(h * 10f) % 3;
            for (int k = 0; k < items; k++)
            {
                var col = CapCols[(k + (int)(h2 * 10f)) % CapCols.Length];
                var cloth = PM("Laundry" + ColorUtility.ToHtmlStringRGB(col), col, twoSided: true, gloss: 0.05f);
                float x = w * 0.72f * (k + 0.5f) / items;
                float hw = k % 2 == 0 ? 0.28f : 0.2f, hh = k % 2 == 0 ? 0.3f : 0.42f;
                PCard(small[cloth], p0 + A * x - up * (hh + 0.02f), A * hw, up * hh, F, twoFaced: true);
            }
        }
    }
}

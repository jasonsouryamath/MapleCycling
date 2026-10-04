using UnityEngine;

// Tuna port, part 5: the quay's working clutter (crane, ice plant, nets, buoys, forklifts,
// tetrapods, harbour light) and the moored fleet with its tairyo-bata. See TunaPort.cs.
public static partial class ShiosaiCoastEnvironment
{
    private static Material TPHullWhite => PM("HullWhite", new Color(0.86f, 0.87f, 0.88f), gloss: 0.62f);
    private static Material TPHullStripe(int i) => i == 0 ? PM("HullBlue", new Color(0.06f, 0.22f, 0.62f), gloss: 0.6f)
                                                  : i == 1 ? PM("HullRed", new Color(0.68f, 0.08f, 0.08f), gloss: 0.6f)
                                                  : PM("HullGreen", new Color(0.06f, 0.34f, 0.26f), gloss: 0.6f);
    private static Material TPAntifoul => PM("Antifoul", new Color(0.40f, 0.07f, 0.07f), gloss: 0.3f);
    private static Material TPDeck => PMP("Deck", "Planks", new Color(0.82f, 0.84f, 0.86f), 2f, 1f, twoSided: true);
    private static Material TPGlass => PM("WheelGlass", new Color(0.03f, 0.05f, 0.07f), gloss: 0.95f);
    private static Material TPFlag(int i) => PM($"Flag{i}", Color.white, $"TP_Tairyobata_{i}", twoSided: true, gloss: 0.05f);
    private static Material TPBuoy => PM("Buoy", new Color(0.95f, 0.36f, 0.04f), gloss: 0.55f);
    private static Material TPNetGreen => PM("NetGreen", new Color(0.10f, 0.32f, 0.22f), gloss: 0.1f);
    private static Material TPNetOrange => PM("NetOrange", new Color(0.78f, 0.30f, 0.06f), gloss: 0.1f);
    private static void BuildQuayside(PortBins big, PortBins small, PFrame f, System.Random rng,
                                      PortCast cast, Transform people, Transform root)
    {
        // ---- tetrapod armour + harbour light on the breakwater ------------------------------
        int tetra = 0;
        for (float n = PortQuayFar - 2f; n < 208f; n += 4.4f)
            for (int row = 0; row < 2; row++)
            {
                var at = f.W(-74f - 6.4f - row * 3.4f + ((float)rng.NextDouble() - 0.5f) * 1.4f,
                             n + row * 2.2f, row == 0 ? 0.2f : -0.9f + (float)rng.NextDouble() * 0.5f);
                Place("Shiosai_Tetrapod", $"Port Armour {tetra++:000}", root, at, (float)rng.NextDouble() * 360f,
                      1.3f * (0.85f + (float)rng.NextDouble() * 0.35f));
            }
        float yawT = Mathf.Atan2(f.T.x, f.T.z) * Mathf.Rad2Deg;
        var light = Place("Shiosai_Lighthouse", "Shiosai Harbour Light", root, f.W(-74f, 203f, 3.0f), yawT, HarbourLightScale);
        if (light != null) Debug.Log($"[tunaport] harbour light on the breakwater head at {light.transform.position}.");

        // ---- jib crane on the quay edge ------------------------------------------------------
        {
            big.SetChunk(12f); small.SetChunk(12f);
            var c = f.W(12f, PortQuayFar - 3f, PortQuayY);
            PCyl(big[TPYellow], c, Vector3.up, 1.0f, 0.8f, 4.5f, 12);
            PBoxF(big[TPWhitePaint], f, c + Vector3.up * 5.2f, 1.2f, 0.7f, 1.4f);
            var tip = c + Vector3.up * 11f + f.N * 9f;
            Beam(big[TPYellow], c + Vector3.up * 5.6f, tip, 0.28f);
            Beam(small[TPSteelGrey], c + Vector3.up * 6.4f, c + Vector3.up * 9.5f, 0.1f);
            Beam(small[TPSteelGrey], c + Vector3.up * 9.5f, tip, 0.03f);
            Beam(small[TPBlack], tip, tip - Vector3.up * 6.5f, 0.02f);
            PBox(small[TPBlack], tip - Vector3.up * 6.7f, f.T * 0.15f, Vector3.up * 0.2f, f.N * 0.15f);
        }

        // ---- ice plant (製氷) with its conveyor chute -------------------------------------------
        {
            const float ia = 84f, inn = 50f;
            big.SetChunk(ia); small.SetChunk(ia);
            var c = f.W(ia, inn, PortQuayY);
            PBlock(big[TPWallMetal], f, c, 12f, 10f, 8f);
            PBoxF(big[TPNavy], f, c + Vector3.up * 8.2f - f.N * 0.02f, 12.05f, 0.6f, 8.05f);
            PBoxF(big[TPConcreteDark], f, c + Vector3.up * 10.2f, 12.3f, 0.2f, 8.3f);
            for (int k = 0; k < 3; k++)
                PBlock(small[TPSteelGrey], f, c + Vector3.up * 10.4f + f.T * (-7f + k * 7f), 1.3f, 1.2f, 1.6f);
            PCard(small[TPShutter], c + f.N * 8.03f + Vector3.up * 2f + f.T * -5f, f.T * 2.2f, Vector3.up * 2f, f.N);
            PCard(small[TPShutter], c + f.N * 8.03f + Vector3.up * 2f + f.T * 3f, f.T * 2.2f, Vector3.up * 2f, f.N);
            Beam(big[TPSteelGrey], c + Vector3.up * 7f + f.N * 8f, f.W(ia - 4f, PortQuayFar - 1.5f, PortQuayY + 3.6f), 0.45f);
            PCyl(big[TPWhitePaint], f.W(ia - 16f, 45f, PortQuayY), Vector3.up, 2.4f, 2.4f, 5.5f, 14);
        }

        // ---- quay clutter: fish boxes, nets, buoy heaps, drums, forklifts, workers ------------------
        for (int k = 0; k < 46; k++)
        {
            float a = -64f + (float)rng.NextDouble() * 128f;
            float n = PortApronFar + 3f + (float)rng.NextDouble() * (PortQuayFar - PortApronFar - 7f);
            if (Mathf.Abs(a - 12f) < 4f && n > PortQuayFar - 7f) continue;   // crane footprint
            big.SetChunk(a); small.SetChunk(a);
            var at = f.W(a, n, PortQuayY);
            double pick = rng.NextDouble();
            if (pick < 0.42) PCrateStack(small, f, at, 2 + rng.Next(6), rng.NextDouble() < 0.4, rng);
            else if (pick < 0.58)
                PEllipsoid(small[rng.NextDouble() < 0.5 ? TPNetGreen : TPNetOrange], at + Vector3.up * 0.3f,
                           f.T * (1.2f + (float)rng.NextDouble()), Vector3.up * 0.45f, f.N * (0.9f + (float)rng.NextDouble()), 8, 4);
            else if (pick < 0.72)
                for (int b = 0; b < 5; b++)
                    PEllipsoid(small[TPBuoy], at + f.T * (b % 3 * 0.55f) + f.N * (b / 3 * 0.55f) + Vector3.up * (0.28f + (b / 3) * 0.3f),
                               Vector3.right * 0.28f, Vector3.up * 0.28f, Vector3.forward * 0.28f, 8, 4);
            else if (pick < 0.82)
                for (int b = 0; b < 3; b++)
                    PCyl(small[b == 1 ? TPCrateBlue : TPHullStripe(1)], at + f.T * (b * 0.64f), Vector3.up, 0.3f, 0.3f, 0.9f, 10);
            else if (pick < 0.88)
            {
                // forklift
                PBlock(big[TPCrateOrange], f, at, 0.6f, 1.2f, 1.1f);
                PBoxF(big[TPBlack], f, at + Vector3.up * 1.9f - f.N * 0.2f, 0.62f, 0.05f, 0.8f);
                PBoxF(small[TPSteelGrey], f, at + Vector3.up * 1.3f + f.N * 1.25f, 0.5f, 1.3f, 0.06f);
                foreach (float s in new[] { -0.3f, 0.3f })
                    PBoxF(small[TPSteelGrey], f, at + f.T * s + f.N * 1.8f + Vector3.up * 0.1f, 0.06f, 0.04f, 0.55f);
            }
            else
            {
                bool walk = rng.NextDouble() < 0.5;
                PortPerson(walk ? cast.walk : cast.stand, people, at, walk ? f.T : f.N,
                           walk ? MinatoCrowdActor.MotionKind.Walk : MinatoCrowdActor.MotionKind.Idle,
                           (float)rng.NextDouble(), "Worker", at + f.T * 12f);
            }
        }
        // a line of workers unloading along the berth
        for (int k = 0; k < 6; k++)
        {
            float a = -18f + k * 4.2f;
            var at = f.W(a, PortQuayFar - 2.2f, PortQuayY);
            PortPerson(cast.stand, people, at, k % 2 == 0 ? f.N : -f.N, MinatoCrowdActor.MotionKind.Idle,
                       0.37f + k * 0.11f, "Worker");
            small.SetChunk(a);
            PCrateStack(small, f, at + f.T * 1.4f, 2 + (k % 3), k % 2 == 1, rng);
        }
        // landings: the morning catch laid out on blue tarps at the berth, with the landing crew
        var tarp = PM("Tarp", new Color(0.10f, 0.30f, 0.72f), twoSided: true, gloss: 0.5f, spec: 0.3f);
        int landed = 0;
        foreach (float la in new[] { -56f, 48f })
        {
            const float tw = 9f;
            float n0 = PortQuayFar - 11f, n1 = PortQuayFar - 4f;
            big.SetChunk(la); small.SetChunk(la);
            float y = PortQuayY + 0.015f;
            PQ(small[tarp], f.W(la - tw, n0, y), f.W(la + tw, n0, y), f.W(la + tw, n1, y), f.W(la - tw, n1, y),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            for (float n = n0 + 1f; n < n1 - 0.6f; n += 1.45f)
                for (float a = la - tw + 0.6f; a < la + tw - 2.4f; a += 2.5f)
                {
                    if (rng.NextDouble() < 0.1) continue;
                    float len = 1.7f + (float)rng.NextDouble() * 0.6f;
                    PTuna(small, f.W(a, n + ((float)rng.NextDouble() - 0.5f) * 0.2f, y), f.T, f.N, Vector3.up, len,
                          rng.NextDouble() < 0.75, y);
                    landed++;
                }
            for (int k = 0; k < 7; k++)
            {
                var at = f.W(la - tw + (float)rng.NextDouble() * tw * 2f, k < 4 ? n0 - 1.1f : n1 + 0.9f, PortQuayY);
                PortPerson(cast.stand, people, at, k < 4 ? f.N : -f.N, MinatoCrowdActor.MotionKind.Idle,
                           (float)rng.NextDouble(), k % 3 == 0 ? "Inspector" : "Worker");
            }
            // hand-carts waiting to run the fish up to the hall
            for (int k = 0; k < 3; k++)
            {
                var c = f.W(la - tw - 2.2f, n0 + 1.2f + k * 2.2f, PortQuayY);
                PBoxF(small[TPSteelGrey], f, c + Vector3.up * 0.55f, 1.1f, 0.04f, 0.55f);
                foreach (float s in new[] { -0.9f, 0.9f })
                    PCyl(small[TPRubber], c + f.T * s + Vector3.up * 0.2f - f.N * 0.5f, f.N, 0.2f, 0.2f, 1f, 10);
                if (k != 1) PTuna(small, c + Vector3.up * 0.6f - f.T * 0.9f, f.T, f.N, Vector3.up, 1.8f, true, PortQuayY + 0.6f);
            }
        }
        for (int k = 0; k < 10; k++)
        {
            float a = -60f + (float)rng.NextDouble() * 120f;
            var at = f.W(a, PortApronFar + 4f + (float)rng.NextDouble() * (PortQuayFar - PortApronFar - 16f), PortQuayY);
            PortPerson(cast.walk, people, at, rng.NextDouble() < 0.5 ? f.T : -f.T, MinatoCrowdActor.MotionKind.Walk,
                       (float)rng.NextDouble(), "Worker", at + f.T * (rng.NextDouble() < 0.5 ? 14f : -14f));
        }
        Debug.Log($"[tunaport] quay landings: {landed} tuna on the tarps.");
    }

    // ------------------------------------------------------------------ the fleet

    private static void BuildPortBoats(PortBins big, PortBins small, PFrame f, System.Random rng)
    {
        int n = 0;
        void Moor(float a, float nn, Vector3 fwd, float len, int style)
        {
            big.SetChunk(a); small.SetChunk(a);
            fwd.y = 0f;
            var yawJ = Quaternion.Euler(0f, ((float)rng.NextDouble() - 0.5f) * 4f, 0f);
            PBoat(big, small, f.W(a, nn, 0f), yawJ * fwd.normalized, len, style, rng);
            n++;
        }
        // along the quay face (parallel), between the piers
        Moor(-52f, PortQuayFar + 3.4f, f.T, 15f, 1);
        Moor(-8f, PortQuayFar + 3.2f, -f.T, 13f, 1);
        Moor(10f, PortQuayFar + 3.0f, f.T, 11f, 2);
        Moor(66f, PortQuayFar + 4.4f, -f.T, 23f, 0);     // the big tuna longliner
        Moor(90f, PortQuayFar + 3.0f, f.T, 10f, 2);
        // alongside the piers, bows out
        foreach (float pa in new[] { -25f, 35f })
        {
            Moor(pa - 6.3f, PortQuayFar + 20f, f.N, 16f, 0);
            Moor(pa + 6.0f, PortQuayFar + 18f, f.N, 13f, 1);
            Moor(pa - 6.0f, PortQuayFar + 37f, f.N, 10f, 2);
        }
        // swinging at anchor in the basin
        Moor(-40f, 150f, (f.T * 0.4f + f.N).normalized, 14f, 1);
        Moor(20f, 170f, (f.T - f.N * 0.3f).normalized, 18f, 0);
        Debug.Log($"[tunaport] fleet: {n} boats.");
    }

    /// <summary>A Japanese fishing boat: V hull with rising bow sheer, bulwarks, boot stripe,
    /// wheelhouse aft, mast with tairyo-bata. style 0 = tuna longliner, 1 = coastal, 2 = skiff.</summary>
    private static void PBoat(PortBins big, PortBins small, Vector3 c, Vector3 fwd, float L, int style, System.Random r)
    {
        var side = Vector3.Cross(Vector3.up, fwd).normalized;
        float B = L * (style == 2 ? 0.30f : 0.24f);
        float x0 = -0.05f * L;
        const int st = 10;
        float W(float x)
        {
            if (x < x0) return B * 0.5f * (0.84f + 0.16f * (x + L * 0.5f) / (x0 + L * 0.5f));
            float t = (x - x0) / (L * 0.5f - x0);
            return B * 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
        }
        float G(float x) => (style == 2 ? 0.9f : 1.35f) + 1.0f * Mathf.Pow(Mathf.Max(0f, (x - x0) / (L * 0.5f - x0)), 2f);
        var stripe = TPHullStripe(r.Next(3));
        Vector3 Pt(float x, float s, float y) => c + fwd * x + side * s + Vector3.up * y;
        for (int k = 0; k < st; k++)
        {
            float xa = -L * 0.5f + L * k / st, xb = -L * 0.5f + L * (k + 1) / st;
            float wa = W(xa), wb = W(xb), ga = G(xa), gb = G(xb);
            foreach (int sg in new[] { -1, 1 })
            {
                var n = side * sg;
                // white topsides, boot stripe, antifouling to the keel
                PQ(big[TPHullWhite], Pt(xa, sg * wa, ga), Pt(xb, sg * wb, gb), Pt(xb, sg * wb * 0.97f, 0.45f), Pt(xa, sg * wa * 0.97f, 0.45f),
                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up, n);
                PQ(big[stripe], Pt(xa, sg * wa * 0.97f, 0.45f), Pt(xb, sg * wb * 0.97f, 0.45f), Pt(xb, sg * wb * 0.9f, -0.05f), Pt(xa, sg * wa * 0.9f, -0.05f),
                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up, n);
                PQ(small[TPAntifoul], Pt(xa, sg * wa * 0.9f, -0.05f), Pt(xb, sg * wb * 0.9f, -0.05f), Pt(xb, 0f, -0.8f), Pt(xa, 0f, -0.8f),
                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up, n - Vector3.up * 0.5f);
                // bulwark inner face + gunwale cap
                float ia = Mathf.Max(0f, wa - 0.1f), ib = Mathf.Max(0f, wb - 0.1f);
                PQ(small[TPHullWhite], Pt(xa, sg * ia, ga), Pt(xb, sg * ib, gb), Pt(xb, sg * ib, gb - 0.6f), Pt(xa, sg * ia, ga - 0.6f),
                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -n);
                PQ(small[stripe], Pt(xa, sg * wa, ga + 0.01f), Pt(xb, sg * wb, gb + 0.01f), Pt(xb, sg * ib, gb + 0.01f), Pt(xa, sg * ia, ga + 0.01f),
                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            }
            float da = G(xa) - 0.6f, db = G(xb) - 0.6f;
            float iwa = Mathf.Max(0f, wa - 0.1f), iwb = Mathf.Max(0f, wb - 0.1f);
            PQ(big[TPDeck], Pt(xa, -iwa, da), Pt(xb, -iwb, db), Pt(xb, iwb, db), Pt(xa, iwa, da),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
        }
        // transom
        {
            float xs = -L * 0.5f, ws = W(xs), gs = G(xs);
            PQ(big[TPHullWhite], Pt(xs, -ws, gs), Pt(xs, ws, gs), Pt(xs, ws * 0.97f, 0.45f), Pt(xs, -ws * 0.97f, 0.45f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -fwd);
            PQ(big[stripe], Pt(xs, -ws * 0.97f, 0.45f), Pt(xs, ws * 0.97f, 0.45f), Pt(xs, ws * 0.9f, -0.05f), Pt(xs, -ws * 0.9f, -0.05f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, -fwd);
        }
        // wheelhouse
        float deckAft = G(-0.2f * L) - 0.6f;
        float wl = style == 2 ? 0.16f * L : 0.22f * L, wh = style == 2 ? 1.6f : 2.3f;
        var whc = Pt(-0.2f * L, 0f, deckAft);
        PBox(big[TPHullWhite], whc + Vector3.up * (wh * 0.5f), fwd * (wl * 0.5f), Vector3.up * (wh * 0.5f), side * (B * 0.33f));
        PBox(big[stripe], whc + Vector3.up * (wh + 0.08f), fwd * (wl * 0.56f), Vector3.up * 0.08f, side * (B * 0.37f));
        PCard(small[TPGlass], whc + Vector3.up * (wh * 0.72f) + fwd * (wl * 0.5f + 0.01f), side * (B * 0.3f), Vector3.up * (wh * 0.16f), fwd);
        foreach (int sg in new[] { -1, 1 })
            PCard(small[TPGlass], whc + Vector3.up * (wh * 0.72f) + side * (sg * (B * 0.33f + 0.01f)), fwd * (wl * 0.4f), Vector3.up * (wh * 0.16f), side * sg);
        if (style != 2)
        {
            // radar + a second deckhouse tier on the bigger boats
            PBox(big[TPHullWhite], whc + Vector3.up * (wh + 0.55f) - fwd * (wl * 0.1f), fwd * (wl * 0.3f), Vector3.up * 0.4f, side * (B * 0.24f));
            Beam(small[TPBlack], whc + Vector3.up * (wh + 1.25f) - side * 0.7f, whc + Vector3.up * (wh + 1.25f) + side * 0.7f, 0.05f);
        }
        // mast + flags
        float mastH = style == 0 ? 10f : style == 1 ? 7f : 4.5f;
        var mb = Pt(-0.02f * L, 0f, G(-0.02f * L) - 0.6f);
        PCyl(big[TPWhitePaint], mb, Vector3.up, 0.12f, 0.08f, mastH, 8);
        Beam(small[TPWhitePaint], mb + Vector3.up * (mastH * 0.7f) - side * (B * 0.45f), mb + Vector3.up * (mastH * 0.7f) + side * (B * 0.45f), 0.05f);
        var bow = Pt(0.48f * L, 0f, G(0.48f * L) + 0.3f);
        var stern = Pt(-0.48f * L, 0f, G(-0.48f * L) + 0.8f);
        var top = mb + Vector3.up * mastH;
        Beam(small[TPBlack], top, bow, 0.015f);
        Beam(small[TPBlack], top, stern, 0.015f);
        if (style != 2)
        {
            int flags = style == 0 ? 4 : 3;
            for (int k = 0; k < flags; k++)
            {
                bool fore = k % 2 == 0;
                float t = 0.25f + 0.22f * (k / 2);
                var p = Vector3.Lerp(top, fore ? bow : stern, t);
                float fs = style == 0 ? 1.2f : 0.9f;
                PCard(small[TPFlag((k + r.Next(4)) % 4)], p - Vector3.up * (fs * 0.62f), (fore ? fwd : -fwd) * (fs * 0.8f),
                      Vector3.up * (fs * 0.55f), side, twoFaced: true);
            }
        }
        if (style == 0)
        {
            // longline gear: buoy heaps and the line hauler on the foredeck
            for (int k = 0; k < 7; k++)
                PEllipsoid(small[TPBuoy], Pt(0.12f * L + (k % 4) * 0.6f, (k / 4 - 0.5f) * 0.7f, G(0.12f * L) - 0.3f),
                           Vector3.right * 0.3f, Vector3.up * 0.3f, Vector3.forward * 0.3f, 8, 4);
            PCyl(small[TPSteelGrey], Pt(0.3f * L, B * 0.3f, G(0.3f * L) - 0.6f), Vector3.up, 0.35f, 0.35f, 0.8f, 10);
            // outrigger poles
            foreach (int sg in new[] { -1, 1 })
                Beam(small[TPWhitePaint], mb + Vector3.up * 1.5f, mb + Vector3.up * 7f + side * (sg * B * 1.3f), 0.05f);
        }
    }
}

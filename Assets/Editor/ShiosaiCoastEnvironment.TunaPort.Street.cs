using UnityEngine;

// Tuna port, part 6: the fishmonger street (machiya shopfronts), the S2 morning-market tents,
// and the hillside machiya/minka that replace the western harbour-house GLBs. See TunaPort.cs.
public static partial class ShiosaiCoastEnvironment
{
    /// <summary>A throwaway frame whose T/N match a building's axes (for the crate helper).</summary>
    private static PFrame PortFrameFrom(Vector3 F, Vector3 A) => new PFrame { O = Vector3.zero, T = A, N = F };

    /// <summary>Open fishmonger front: shutter box, posts, plank counter with a sloped ice tray of
    /// MODELLED fish, price tags, bare bulbs, framed fascia sign, noren, lanterns, stock, people.
    /// fb = centre of the street line at shop floor level.</summary>
    private static void ShopFront(PortBins big, PortBins small, Vector3 fb, Vector3 A, Vector3 F, float w, float h1, float recess,
                                  int shop, System.Random r, PortCast cast, Transform people)
    {
        var up = Vector3.up;
        var fr = PortFrameFrom(F, A);
        // posts + rolled-shutter housing
        foreach (int sg in new[] { -1, 1 })
            PBox(big[TPKoshiWood], fb + A * (sg * (w * 0.5f - 0.12f)) + up * (h1 * 0.5f) - F * 0.1f, A * 0.11f, up * (h1 * 0.5f), F * 0.11f);
        PBox(big[TPSteelGrey], fb + up * (h1 - 0.25f) - F * 0.12f, A * (w * 0.5f - 0.2f), up * 0.18f, F * 0.16f);
        // back-wall shelving with stock
        for (int k = 0; k < 3; k++)
        {
            var sh = fb - F * (recess - 0.3f) + up * (0.55f + k * 0.62f);
            PBox(small[TPWood], sh - up * 0.2f, A * (w * 0.42f), up * 0.025f, F * 0.26f);
            for (float x = -w * 0.38f; x < w * 0.38f; x += 0.55f)
                if (r.NextDouble() < 0.75)
                    PBox(small[r.NextDouble() < 0.5 ? TPStyrofoam : TPCrateBlue], sh + A * x, A * 0.22f, up * (0.12f + 0.06f * (float)r.NextDouble()), F * 0.2f);
        }
        // the counter
        var tc = fb - F * 0.95f + up * 0.45f;
        PBox(big[TPWood], tc, A * (w * 0.41f), up * 0.45f, F * 0.62f);
        var tn = (up * 0.82f + F * 0.57f).normalized;
        var ts = Vector3.Cross(A, tn).normalized; if (Vector3.Dot(ts, F) < 0) ts = -ts;
        var trayC = tc + up * 0.6f;
        PCard(small[TPIceTray], trayC, A * (w * 0.39f), ts * 0.62f, tn);
        foreach (int sg in new[] { -1, 1 })
            PBox(small[TPSteelGrey], trayC + ts * (sg * 0.63f) + tn * 0.03f, A * (w * 0.4f), tn * 0.04f, ts * 0.02f);
        // modelled fish laid on the ice, heads to the street
        int fish = Mathf.RoundToInt(w * 0.8f);
        for (int k = 0; k < fish; k++)
        {
            float x = -w * 0.34f + (w * 0.68f) * k / Mathf.Max(1, fish - 1);
            float along = ((float)r.NextDouble() - 0.5f) * 0.5f;
            float len = 0.45f + (float)r.NextDouble() * 0.35f;
            var nose = trayC + A * x + ts * (along - len * 0.5f) + tn * 0.06f;
            PTuna(small, nose, ts, A, tn, len, false);
        }
        for (int k = 0; k < 3; k++)
        {
            var tag = trayC + A * (-w * 0.3f + k * w * 0.3f) - ts * 0.55f + tn * 0.18f;
            PCard(small[PM("PriceTag", new Color(0.92f, 0.92f, 0.9f), gloss: 0.2f)], tag, A * 0.12f, up * 0.07f, F);
            PBox(small[TPHullStripe(1)], tag + up * 0.075f + F * 0.003f, A * 0.12f, up * 0.012f, F * 0.004f);
        }
        if (shop % 6 == 2)
        {
            PTuna(small, trayC + tn * 0.35f - A * (w * 0.3f), A, up, F, Mathf.Min(2.3f, w * 0.55f), false);
            PCard(small[TPMaguroCuts], fb - F * (recess - 0.03f) + up * 2.25f, A * 1.2f, up * 0.6f, F);
        }
        // bare bulbs over the counter
        for (int k = 0; k < 3; k++)
        {
            var bulb = fb - F * 0.9f + A * (-w * 0.3f + k * w * 0.3f) + up * (h1 - 0.9f);
            PCyl(small[TPBlack], bulb + up * 0.1f, up, 0.006f, 0.006f, 0.8f, 6, false);
            PEllipsoid(small[PM("Bulb", new Color(1f, 0.92f, 0.7f), gloss: 0.9f)], bulb, A * 0.06f, up * 0.08f, F * 0.06f, 14, 8);
        }
        // stock by the door
        PCrateStack(small, fr, fb + F * 0.25f + A * (w * 0.44f), 2 + r.Next(3), true, r);
        PCrateStack(small, fr, fb + F * 0.3f - A * (w * 0.45f), 1 + r.Next(3), false, r);
        // framed fascia sign, noren, lanterns
        var sc = fb + up * (h1 + 0.72f) + F * 0.07f;
        float sw = Mathf.Min(w * 0.45f, 3.4f);
        PBox(small[TPKoshiWood], sc - F * 0.03f, A * (sw + 0.08f), up * 0.42f, F * 0.04f);
        PCard(small[TPShopSign(shop % 6)], sc + F * 0.02f, A * sw, up * 0.34f, F);
        PCard(small[TPNoren(shop % 4)], fb + up * (h1 - 0.6f) + F * 0.12f + A * (w * 0.2f), A * Mathf.Min(w * 0.24f, 1.4f), up * 0.44f, F, twoFaced: true);
        PCyl(small[TPKoshiWood], fb + up * (h1 - 0.14f) + F * 0.12f + A * (w * 0.2f - Mathf.Min(w * 0.24f, 1.4f) - 0.1f), A, 0.025f, 0.025f,
             Mathf.Min(w * 0.48f, 2.8f) + 0.2f, 6, true);
        PLantern(small, fb + up * (h1 + 0.1f) + F * 1.0f - A * (w * 0.32f), shop % 3);
        if (w > 7f) PLantern(small, fb + up * (h1 + 0.1f) + F * 1.0f + A * (w * 0.36f), (shop + 1) % 3);
        ShopExtras(small, fb, A, F, w, h1, recess, shop);
        if (cast != null)
        {
            PortPerson(cast.stand, people, fb - F * 1.95f, F, MinatoCrowdActor.MotionKind.Wave, (float)r.NextDouble(), "Fishmonger");
            int cust = r.Next(3);
            for (int k = 0; k < cust; k++)
                PortPerson(cast.stand, people, fb + F * (0.95f + (float)r.NextDouble() * 0.6f) + A * ((k - 0.5f) * 1.2f) - up * 0.35f,
                           -F, MinatoCrowdActor.MotionKind.Idle, (float)r.NextDouble(), "Shopper");
        }
    }

    /// <summary>Japanese concrete utility poles with crossarms, insulators, transformers, a street
    /// lamp, and sagging wires between consecutive poles.</summary>
    private static void UtilityPoles(PortBins big, PortBins small, PFrame f, float a0, float a1, float n)
    {
        var wire = PM("Wire", new Color(0.03f, 0.03f, 0.035f), gloss: 0.3f);
        Vector3? prevL = null, prevR = null, prevM = null;
        int k = 0;
        for (float a = a0; a <= a1 + 0.01f; a += 31f, k++)
        {
            big.SetChunk(a); small.SetChunk(a);
            var b = f.W(a, n, f.RoadY(a) + 0.04f);
            PCyl(big[TPConcrete], b, Vector3.up, 0.2f, 0.13f, 11f, 12, true);
            var arm1 = b + Vector3.up * 9.9f; var arm2 = b + Vector3.up * 9.2f;
            Beam(small[TPSteelGrey], arm1 - f.N * 0.9f, arm1 + f.N * 0.9f, 0.04f);
            Beam(small[TPSteelGrey], arm2 - f.N * 0.7f, arm2 + f.N * 0.7f, 0.035f);
            foreach (float s in new[] { -0.8f, 0f, 0.8f })
                PCyl(small[TPWhitePaint], arm1 + f.N * s + Vector3.up * 0.04f, Vector3.up, 0.05f, 0.04f, 0.16f, 8, true);
            if (k % 3 == 1)
            {
                PCyl(big[TPSteelGrey], b + Vector3.up * 7.6f - f.N * 0.45f, Vector3.up, 0.3f, 0.3f, 0.95f, 14, true);
                Beam(small[TPSteelGrey], b + Vector3.up * 8.1f, b + Vector3.up * 8.1f - f.N * 0.2f, 0.05f);
            }
            // street lamp arm over the road
            var lampRoot = b + Vector3.up * 6.2f;
            var lampHead = lampRoot + f.N * 1.6f + Vector3.up * 0.35f;
            Beam(small[TPSteelGrey], lampRoot, lampHead, 0.035f);
            PBox(small[TPSteelGrey], lampHead + f.N * 0.2f, f.T * 0.14f, Vector3.up * 0.06f, f.N * 0.3f);
            // step bolts
            for (int s = 0; s < 8; s++)
                Beam(small[TPSteelGrey], b + Vector3.up * (2.4f + s * 0.45f), b + Vector3.up * (2.4f + s * 0.45f) + (s % 2 == 0 ? f.T : -f.T) * 0.3f, 0.012f);
            var pl = arm1 - f.N * 0.8f + Vector3.up * 0.2f; var pr = arm1 + f.N * 0.8f + Vector3.up * 0.2f; var pm = arm2 + Vector3.up * 0.05f;
            if (prevL.HasValue)
            {
                Catenary(small[wire], prevL.Value, pl, 0.45f);
                Catenary(small[wire], prevR.Value, pr, 0.45f);
                Catenary(small[wire], prevM.Value, pm, 0.6f);
            }
            prevL = pl; prevR = pr; prevM = pm;
        }
    }

    private static void Catenary(PB b, Vector3 p0, Vector3 p1, float sag)
    {
        const int segs = 10;
        Vector3 Pt(int i) { float t = (float)i / segs; return Vector3.Lerp(p0, p1, t) - Vector3.up * (sag * 4f * t * (1f - t)); }
        for (int i = 0; i < segs; i++) Beam(b, Pt(i), Pt(i + 1), 0.012f);
    }

    // ------------------------------------------------------------------ the fishmonger street

    private static void BuildShopRow(PortBins big, PortBins small, PFrame f, float a0, float a1,
                                     System.Random rng, PortCast cast, Transform people, int shopSeed)
    {
        // pavement from the road edge to the shop fronts
        for (float a = a0; a < a1 - 0.01f; a += 6f)
        {
            big.SetChunk(a);
            float b = Mathf.Min(a + 6f, a1);
            float ya = f.RoadY(a) + 0.04f, yb = f.RoadY(b) + 0.04f;
            PQ(_walk[TPConcreteDark], f.W(a, -4.5f, ya), f.W(b, -4.5f, yb), f.W(b, -PortShopFront - 0.3f, yb), f.W(a, -PortShopFront - 0.3f, ya),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            PQ(big[TPConcreteDark], f.W(a, -4.5f, ya), f.W(b, -4.5f, yb), f.W(b, -4.5f, yb - 0.3f), f.W(a, -4.5f, ya - 0.3f),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, f.N);
        }
        int shops = 0, k = 0;
        float at = a0;
        while (at < a1 - 5f)
        {
            float w = Mathf.Min(6f + (float)rng.NextDouble() * 3.2f, a1 - at);
            if (w < 5f) break;
            float ac = at + w * 0.5f;
            big.SetChunk(ac); small.SetChunk(ac);
            var front = f.W(ac, -PortShopFront, f.RoadY(ac) + 0.04f);
            bool shop = rng.NextDouble() < 0.82;
            int storeys = rng.NextDouble() < 0.8 ? 2 : 1;
            PMinka(big, small, front, f.N, w - 0.15f, PortShopDepth, storeys, rng.Next(6), shop ? shopSeed + k : -1, rng, cast, people);
            if (shop) shops++;
            // nobori banners at the kerb, flying toward the oncoming rider
            if (shop && rng.NextDouble() < 0.8)
            {
                var pb = f.W(at + 0.8f, -5.1f, f.RoadY(ac) + 0.04f);
                PCyl(small[TPSteelGrey], pb, Vector3.up, 0.03f, 0.03f, 3.3f, 6, true);
                PCard(small[TPNobori(rng.Next(5))], pb + Vector3.up * 2.05f + f.T * 0.34f, f.T * 0.32f, Vector3.up * 1.18f, f.N, twoFaced: true);
            }
            // passers-by on the pavement
            if (rng.NextDouble() < 0.55)
            {
                bool walk = rng.NextDouble() < 0.6;
                var p = f.W(ac + ((float)rng.NextDouble() - 0.5f) * w, -6.4f - (float)rng.NextDouble() * 2.4f, f.RoadY(ac) + 0.04f);
                PortPerson(walk ? cast.walk : cast.stand, people, p, walk ? (rng.NextDouble() < 0.5 ? f.T : -f.T) : -f.N,
                           walk ? MinatoCrowdActor.MotionKind.Walk : MinatoCrowdActor.MotionKind.Idle,
                           (float)rng.NextDouble(), "Shopper", p + f.T * (rng.NextDouble() < 0.5 ? 16f : -16f));
            }
            at += w;
            k++;
        }
        UtilityPoles(big, small, f, a0 + 6f, a1 - 4f, -4.95f);
        Debug.Log($"[tunaport] shop row @ {f.anchorM:0} m: {k} frontages, {shops} shops.");
    }

    // ------------------------------------------------------------------ S2 morning-market tents

    private static void BuildTentRow(PortBins big, PortBins small, PFrame f, System.Random rng, PortCast cast, Transform people)
    {
        const float a0 = -82f, a1 = 82f;
        for (float a = a0; a < a1 - 0.01f; a += 6f)
        {
            big.SetChunk(a); small.SetChunk(a);
            float b = Mathf.Min(a + 6f, a1);
            float ya = f.RoadY(a) + 0.04f, yb = f.RoadY(b) + 0.04f;
            PQ(_walk[TPConcreteDark], f.W(a, 4.5f, ya), f.W(b, 4.5f, yb), f.W(b, 10.1f, yb), f.W(a, 10.1f, ya),
               Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            // low sea-wall parapet on the cliff edge
            PBoxF(big[TPConcrete], f, f.W((a + b) * 0.5f, 10.35f, (ya + yb) * 0.5f - 0.4f), (b - a) * 0.5f, 1.3f, 0.3f);
        }
        int tents = 0;
        for (float a = a0 + 4f; a < a1 - 3f; a += 6.2f)
        {
            if (rng.NextDouble() < 0.12) continue;
            big.SetChunk(a); small.SetChunk(a);
            float y = f.RoadY(a) + 0.04f;
            var c = f.W(a, 7.4f, y);
            foreach (float s in new[] { -1.4f, 1.4f })
                foreach (float n in new[] { -1.3f, 1.3f })
                    PCyl(small[TPSteelGrey], c + f.T * s + f.N * n, Vector3.up, 0.03f, 0.03f, n < 0 ? 2.4f : 2.1f, 6, true);
            var tilt = (Vector3.up + f.N * 0.12f).normalized;
            var sl = Vector3.Cross(f.T, tilt).normalized; if (Vector3.Dot(sl, f.N) < 0) sl = -sl;
            PBox(small[TPCanvas(tents % 2)], c + Vector3.up * 2.3f, f.T * 1.6f, tilt * 0.04f, sl * 1.45f);
            PBox(small[TPCanvas((tents + 1) % 2)], c + Vector3.up * 2.12f - f.N * 1.45f, f.T * 1.6f, Vector3.up * 0.16f, f.N * 0.02f);
            // table of fish on ice facing the road
            PBoxF(big[TPWood], f, c + Vector3.up * 0.4f - f.N * 0.5f, 1.3f, 0.4f, 0.45f);
            var tn = (Vector3.up * 0.8f - f.N * 0.6f).normalized;
            var ts = Vector3.Cross(f.T, tn).normalized; if (Vector3.Dot(ts, f.N) > 0) ts = -ts;
            PCard(small[TPIceTray], c + Vector3.up * 0.86f - f.N * 0.5f, f.T * 1.25f, ts * 0.5f, tn);
            PCrateStack(small, f, c + f.T * 1.9f + f.N * 0.3f, 2 + rng.Next(3), true, rng);
            PortPerson(cast.stand, people, c + f.N * 0.6f, -f.N, MinatoCrowdActor.MotionKind.Wave, (float)rng.NextDouble(), "Fishmonger");
            if (rng.NextDouble() < 0.7)
                PortPerson(cast.stand, people, c - f.N * 1.9f + f.T * ((float)rng.NextDouble() - 0.5f), f.N,
                           MinatoCrowdActor.MotionKind.Idle, (float)rng.NextDouble(), "Shopper");
            if (tents % 3 == 0)
            {
                var pb = c - f.N * 1.5f + f.T * 1.8f;
                PCyl(small[TPSteelGrey], pb, Vector3.up, 0.03f, 0.03f, 3.3f, 6, true);
                PCard(small[TPNobori(tents % 5)], pb + Vector3.up * 2.05f + f.T * 0.34f, f.T * 0.32f, Vector3.up * 1.18f, f.N, twoFaced: true);
            }
            tents++;
        }
        Debug.Log($"[tunaport] morning market: {tents} tents.");
    }

    // ------------------------------------------------------------------ hillside town

    private static int BuildPortHouses(PortBins big, PortBins small, CoastRoute route, PFrame s1, PFrame s2, System.Random rng)
    {
        int hi = route.AnchorIndex(AnchorVillageCentre, AnchorVillageCentreM);
        float[] rows = { 17f, 26f, 36f, 47f, 59f, 72f, 88f, 106f };
        int built = 0;
        foreach (float off in rows)
        {
            int depth = off < 24f ? 0 : off < 42f ? 1 : off < 60f ? 2 : off < 80f ? 3 : 4;
            int half = Mathf.Max(5, 15 - depth * 2);
            float pitch = 10.5f + depth * 1.2f;
            for (int k = -half; k <= half; k++)
            {
                if (rng.NextDouble() < 0.08) continue;
                float along = k * pitch + ((float)rng.NextDouble() - 0.5f) * pitch * 0.35f;
                int idx = Mathf.Clamp(hi + Mathf.RoundToInt(along / 3f), 2, route.Count - 3);
                float o = off + ((float)rng.NextDouble() - 0.5f) * 5f;
                float w = 6.2f + (float)rng.NextDouble() * 3f, d = 6.4f + (float)rng.NextDouble() * 2.4f;
                // the building's whole plot must be clear of the port and the shop rows
                if (InTunaPortFootprint(route, idx, o - d * 0.5f - 1f) || InTunaPortFootprint(route, idx, o) ||
                    InTunaPortFootprint(route, idx, o + d * 0.5f)) continue;
                if (!OnRibbon(route, idx, o)) continue;
                var at = GroundedPoint(route, idx, o, 6f);
                if (at.y < 1.5f) continue;
                var side = route.SideFlat(idx);
                var F = -side;                                     // face the road
                float yaw = ((float)rng.NextDouble() - 0.5f) * (depth == 0 ? 10f : 34f);
                F = Quaternion.Euler(0f, yaw, 0f) * F;
                var front = at + F * (d * 0.5f);
                front.y = at.y + 0.05f;
                float aLocal = route.Distance[idx] - PortS1AnchorM;
                big.SetChunk(aLocal); small.SetChunk(aLocal);
                int storeys = depth <= 1 ? (rng.NextDouble() < 0.7 ? 2 : 1) : (rng.NextDouble() < 0.45 ? 2 : 1);
                PMinka(big, small, front, F, w, d, storeys, rng.Next(12), -1, rng, null, null, depth <= 1 ? 2 : depth <= 2 ? 1 : 0);
                built++;
            }
        }
        return built;
    }
}

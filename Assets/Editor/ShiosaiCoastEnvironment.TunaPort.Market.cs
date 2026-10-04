using UnityEngine;

// Tuna port, part 3: the MAGURO ICHIBA hall and the quayside working clutter. See TunaPort.cs.
public static partial class ShiosaiCoastEnvironment
{
    private const float HallA0 = -46f, HallA1 = 46f, HallN0 = 13f, HallN1 = 34f;
    private const float HallEaves = 6.6f, HallRidge = 9.4f;

    private static void BuildMarketHall(PortBins big, PortBins small, PFrame f, System.Random rng,
                                        PortCast cast, Transform people)
    {
        float nMid = (HallN0 + HallN1) * 0.5f;
        float halfN = (HallN1 - HallN0) * 0.5f;
        const int bays = 12;
        float bay = (HallA1 - HallA0) / bays;

        for (int k = 0; k <= bays; k++)
        {
            float a = HallA0 + k * bay;
            big.SetChunk(a); small.SetChunk(a);
            float fl = f.RoadY(a) + 0.12f;
            // columns on both long sides + a mid row
            foreach (float n in new[] { HallN0, nMid, HallN1 })
            {
                float h = n == nMid ? HallRidge - 0.5f : HallEaves;
                PBoxF(big[TPSteelGreen], f, f.W(a, n, fl + h * 0.5f), 0.16f, h * 0.5f, 0.16f);
                PBoxF(big[TPConcreteDark], f, f.W(a, n, fl + 0.2f), 0.3f, 0.2f, 0.3f);
            }
            // roof truss rafters
            var e0 = f.W(a, HallN0 - 0.6f, fl + HallEaves + 0.1f);
            var e1 = f.W(a, HallN1 + 0.6f, fl + HallEaves + 0.1f);
            var rg = f.W(a, nMid, fl + HallRidge - 0.2f);
            Beam(big[TPSteelGreen], e0, rg, 0.12f);
            Beam(big[TPSteelGreen], e1, rg, 0.12f);
            Beam(small[TPSteelGreen], f.W(a, HallN0, fl + HallEaves - 0.1f), f.W(a, HallN1, fl + HallEaves - 0.1f), 0.08f);
        }

        // ---- roof: two corrugated slopes with a raised ventilation monitor on the ridge -----------
        for (int k = 0; k < bays; k++)
        {
            float a0 = HallA0 + k * bay - (k == 0 ? 0.8f : 0f);
            float a1 = HallA0 + (k + 1) * bay + (k == bays - 1 ? 0.8f : 0f);
            big.SetChunk(a0);
            float fl0 = f.RoadY(a0) + 0.12f, fl1 = f.RoadY(a1) + 0.12f;
            foreach (int side in new[] { -1, 1 })
            {
                float nEave = side < 0 ? HallN0 - 1.2f : HallN1 + 1.2f;
                float nTop = nMid + side * 1.5f;
                var p0 = f.W(a0, nEave, fl0 + HallEaves + 0.2f);
                var p1 = f.W(a1, nEave, fl1 + HallEaves + 0.2f);
                var p2 = f.W(a1, nTop, fl1 + HallRidge - 0.05f);
                var p3 = f.W(a0, nTop, fl0 + HallRidge - 0.05f);
                var up = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                if (up.y < 0) up = -up;
                PQ(big[TPRoofMetal], p0, p1, p2, p3, Vector2.zero, new Vector2(0, (a1 - a0) / 2f),
                   new Vector2(3f, (a1 - a0) / 2f), new Vector2(3f, 0), up);
                PQ(big[TPRoofMetal], p0, p3, p2, p1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, -up); // underside
                // eave fascia gutter
                PBox(small[TPFascia], (p0 + p1) * 0.5f - Vector3.up * 0.1f, (p1 - p0) * 0.5f, Vector3.up * 0.14f, f.N * 0.08f);
            }
            // ridge monitor (raised clerestory)
            var m0 = f.W((a0 + a1) * 0.5f, nMid, (fl0 + fl1) * 0.5f + HallRidge + 0.55f);
            PBoxF(big[TPRoofMetal], f, m0 + Vector3.up * 0.55f, (a1 - a0) * 0.5f, 0.08f, 2.2f);
            PBoxF(small[TPBlack], f, m0, (a1 - a0) * 0.5f - 0.1f, 0.5f, 1.4f);
        }

        // ---- roadside fascia band + back wall with shutters + gable ends -------------------------
        for (int k = 0; k < bays; k++)
        {
            float a0 = HallA0 + k * bay, a1 = a0 + bay;
            big.SetChunk(a0); small.SetChunk(a0);
            float fl = f.RoadY((a0 + a1) * 0.5f) + 0.12f;
            PBoxF(big[TPFascia], f, f.W((a0 + a1) * 0.5f, HallN0 - 0.25f, fl + HallEaves - 0.55f), bay * 0.5f, 0.55f, 0.12f);
            PBoxF(small[TPNavy], f, f.W((a0 + a1) * 0.5f, HallN0 - 0.39f, fl + HallEaves - 0.75f), bay * 0.5f, 0.12f, 0.02f);
            // back wall: corrugated, with a roll shutter in every other bay
            PBoxF(big[TPWallMetal], f, f.W((a0 + a1) * 0.5f, HallN1 + 0.1f, fl + HallEaves * 0.5f), bay * 0.5f, HallEaves * 0.5f, 0.08f);
            if (k % 2 == 0)
            {
                PCard(small[TPShutter], f.W((a0 + a1) * 0.5f, HallN1 - 0.02f, fl + 2.0f), f.T * (bay * 0.36f),
                      Vector3.up * 2.0f, -f.N);
                PBoxF(small[TPYellow], f, f.W((a0 + a1) * 0.5f, HallN1 - 0.1f, fl + 0.05f), bay * 0.36f, 0.05f, 0.1f);
            }
            // hanging fluorescent fittings (lit tubes underneath)
            var tube = PMGlow("Fluoro", new Color(0.92f, 0.96f, 1f), 40f);
            foreach (float nL in new[] { HallN0 + 5f, nMid, HallN1 - 5f })
                foreach (float aL in new[] { a0 + bay * 0.3f, a0 + bay * 0.7f })
                {
                    var lc = f.W(aL, nL, fl + 5.2f);
                    PBoxF(small[TPWhitePaint], f, lc, 0.12f, 0.05f, 0.7f);
                    PBoxF(small[tube], f, lc - Vector3.up * 0.07f, 0.05f, 0.025f, 0.62f);
                    Beam(small[TPBlack], lc + Vector3.up * 0.05f, lc + Vector3.up * (HallEaves - 5.2f + (nL == nMid ? 2f : 0.3f)), 0.008f);
                }
            // red lanterns along the roadside eave
            for (int j = 0; j < 2; j++)
                PLantern(small, f.W(a0 + bay * (0.25f + 0.5f * j), HallN0 - 0.6f, fl + HallEaves - 1.15f), j % 2 == 0 ? 0 : 2, 0.9f);
        }
        foreach (float ea in new[] { HallA0, HallA1 })
        {
            big.SetChunk(ea);
            float fl = f.RoadY(ea) + 0.12f;
            // half-height end wall + gable infill
            PBoxF(big[TPWallMetal], f, f.W(ea, nMid + halfN * 0.35f, fl + 1.6f), 0.08f, 1.6f, halfN * 0.65f);
            var g0 = f.W(ea, HallN0, fl + HallEaves); var g1 = f.W(ea, HallN1, fl + HallEaves);
            var g2 = f.W(ea, nMid, fl + HallRidge);
            var gn = ea < 0 ? -f.T : f.T;
            PTri(big[TPWallMetal], g0, g1, g2, Vector2.zero, Vector2.right, Vector2.up, gn);
            PTri(big[TPWallMetal], g0, g2, g1, Vector2.zero, Vector2.up, Vector2.right, -gn);
        }

        // ---- the great roof sign, and the tuna riding it ----------------------------------------
        {
            big.SetChunk(0f); small.SetChunk(0f);
            float fl = f.RoadY(0f) + 0.12f;
            float slopeY(float n) => fl + HallEaves + 0.2f + (HallRidge - HallEaves) * Mathf.InverseLerp(HallN0 - 1.2f, nMid - 1.5f, n);
            const float nSign = 16.2f, halfW = 17f, halfH = 2.65f;
            float yb = slopeY(nSign) + 0.6f;
            var sc = f.W(0f, nSign, yb + halfH);
            PCard(big[TPSignMarket], sc - f.N * 0.13f, f.T * halfW, Vector3.up * halfH, -f.N);
            PBoxF(big[TPNavy], f, sc, halfW + 0.2f, halfH + 0.2f, 0.12f);
            for (float a = -halfW + 1f; a <= halfW - 0.9f; a += 5.6f)
            {
                PBoxF(small[TPSteelGrey], f, f.W(a, nSign + 0.5f, (yb + slopeY(nSign + 0.5f)) * 0.5f + halfH), 0.07f, halfH + 0.4f, 0.07f);
                Beam(small[TPSteelGrey], f.W(a, nSign + 0.2f, yb + halfH * 2f), f.W(a, nSign + 3.2f, slopeY(nSign + 3.2f)), 0.06f);
            }
            // a 13 m bluefin leaping along the top of the board
            float tunaY = yb + halfH * 2f + 0.25f + 0.125f * 13f;
            PTuna(big, f.W(-6.5f, nSign, tunaY), f.T, Vector3.up + f.T * 0.08f, f.N, 13f, false);
            foreach (float a in new[] { -3f, 3.5f })
                PBoxF(small[TPSteelGrey], f, f.W(a, nSign, yb + halfH * 2f + 0.5f), 0.06f, 0.5f, 0.06f);
        }

        // ---- 魚市場 tower sign at the approach corner ---------------------------------------------
        {
            float ta = PortApronA0 + 5f, tn = PortApronNear + 1.8f;
            big.SetChunk(ta); small.SetChunk(ta);
            float fl = f.RoadY(ta) + 0.12f;
            PBoxF(big[TPSteelGrey], f, f.W(ta, tn, fl + 5.5f), 0.14f, 5.5f, 0.14f);
            var vc = f.W(ta, tn, fl + 7.2f);
            PCard(big[TPSignVertical], vc, f.N * 1.1f, Vector3.up * 4.3f, -f.T, twoFaced: true);
            PBoxF(big[TPNavy], f, vc, 0.06f, 4.45f, 1.22f);
        }

        // ---- the auction floor --------------------------------------------------------------------
        {
            // wet, glossier floor inside the hall
            for (float a = HallA0; a < HallA1 - 0.01f; a += bay)
            {
                big.SetChunk(a);
                float y = f.RoadY(a) + 0.135f;
                PQ(_walk[TPWetFloor], f.W(a, HallN0 + 0.3f, y), f.W(a + bay, HallN0 + 0.3f, y), f.W(a + bay, HallN1 - 0.2f, y), f.W(a, HallN1 - 0.2f, y),
                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector3.up);
            }
            var rows = new (float n, bool frozen, bool pallet)[]
            {
                (16.4f, true, false), (18.9f, false, true), (23.8f, false, true), (26.2f, true, false), (30.4f, true, false),
            };
            int count = 0;
            foreach (var (n, frozen, pallet) in rows)
            {
                for (float a = HallA0 + 2.6f; a < HallA1 - 2.4f; a += 2.35f)
                {
                    if (Mathf.Abs(a + 20f) < 4.8f || Mathf.Abs(a - 20f) < 4.8f) { if (n > 21f && n < 29f) continue; }
                    if (rng.NextDouble() < 0.08) continue;
                    big.SetChunk(a); small.SetChunk(a);
                    float len = 1.75f + (float)rng.NextDouble() * 0.7f;
                    float fl = f.RoadY(a) + 0.135f;
                    if (pallet) { PBoxF(small[TPWood], f, f.W(a + len * 0.5f, n, fl + 0.07f), len * 0.5f + 0.1f, 0.07f, 0.5f); fl += 0.14f; }
                    // head alternates toward the aisle; fish lie on their flank
                    float dirSign = ((int)(a * 7f + n * 3f) & 1) == 0 ? 1f : -1f;
                    var nose = f.W(a + (dirSign > 0 ? 0f : len), n + ((float)rng.NextDouble() - 0.5f) * 0.3f, fl);
                    PTuna(small, nose, f.T * dirSign + f.N * (((float)rng.NextDouble() - 0.5f) * 0.12f), f.N, Vector3.up, len, frozen, fl);
                    count++;
                }
            }
            Debug.Log($"[tunaport] auction floor: {count} tuna.");

            // auctioneers' stands with hand bells, and the day's board on the back wall
            foreach (float sa in new[] { -20f, 20f })
            {
                big.SetChunk(sa); small.SetChunk(sa);
                float fl = f.RoadY(sa) + 0.135f;
                PBlock(_walk[TPWood], f, f.W(sa, 22.4f, fl), 0.7f, 0.55f, 0.6f);
                PCyl(small[TPYellow], f.W(sa + 0.5f, 22.4f, fl + 0.55f), Vector3.up, 0.08f, 0.05f, 0.14f, 8);
                PortPerson(cast.stand, people, f.W(sa, 22.4f, fl + 0.55f), sa < 0 ? f.T : -f.T,
                           MinatoCrowdActor.MotionKind.Wave, 0.21f + sa * 0.001f, "Auctioneer");
                for (int k = 0; k < 7; k++)
                {
                    float ba2 = sa + (sa < 0 ? 1.6f : -1.6f) + (k % 4) * (sa < 0 ? 0.8f : -0.8f);
                    float bn = 21.4f + (k / 4) * 1.9f + (float)rng.NextDouble() * 0.3f;
                    PortPerson(cast.stand, people, f.W(ba2, bn, fl), (f.W(sa, 22.4f, fl) - f.W(ba2, bn, fl)),
                               MinatoCrowdActor.MotionKind.Idle, (float)rng.NextDouble(), "Buyer");
                }
            }
            big.SetChunk(0f);
            float fb = f.RoadY(0f) + 0.12f;
            PCard(big[TPAuctionBoard], f.W(0f, HallN1 - 0.05f, fb + 3.3f), f.T * 2.4f, Vector3.up * 1.2f, -f.N);

            // buyers inspecting along the aisles, walkers, and the cold-chain carts
            for (int k = 0; k < 26; k++)
            {
                float a = HallA0 + 4f + (float)rng.NextDouble() * (HallA1 - HallA0 - 8f);
                float n = rng.NextDouble() < 0.5 ? 21.4f : 28.3f;
                float fl = f.RoadY(a) + 0.135f;
                bool walk = rng.NextDouble() < 0.45;
                var at = f.W(a, n, fl);
                PortPerson(walk ? cast.walk : cast.stand, people, at, walk ? f.T : (rng.NextDouble() < 0.5 ? f.N : -f.N),
                           walk ? MinatoCrowdActor.MotionKind.Walk : MinatoCrowdActor.MotionKind.Idle,
                           (float)rng.NextDouble(), walk ? "MarketWalker" : "Inspector", at + f.T * 14f);
            }
            for (int k = 0; k < 5; k++)
            {
                float a = -38f + k * 18f + (float)rng.NextDouble() * 4f;
                big.SetChunk(a);
                PTurret(big, f, f.W(a, k % 2 == 0 ? 21.5f : 28.5f, f.RoadY(a) + 0.135f), 1f, k % 3);
            }
            // crate and styrofoam walls along the back
            for (float a = HallA0 + 1.5f; a < HallA1 - 1f; a += 1.1f)
            {
                if (rng.NextDouble() < 0.35) continue;
                small.SetChunk(a);
                PCrateStack(small, f, f.W(a, HallN1 - 1.0f, f.RoadY(a) + 0.135f), 2 + rng.Next(5), rng.NextDouble() < 0.45, rng);
            }
        }

        // ---- apron frontage: vending machines, parked carts, crates, shoppers ------------------------
        {
            var vend = new[] { PM("VendRed", new Color(0.72f, 0.12f, 0.12f), gloss: 0.5f, spec: 0.3f),
                               PM("VendBlue", new Color(0.12f, 0.28f, 0.62f), gloss: 0.5f, spec: 0.3f),
                               PM("VendWhite", new Color(0.72f, 0.76f, 0.82f), gloss: 0.5f, spec: 0.3f) };
            var glass = PM("VendGlass", new Color(0.62f, 0.78f, 0.92f), gloss: 0.8f, spec: 0.6f, rim: 0.3f);
            for (int k = 0; k < 4; k++)
            {
                float a = HallA1 + 3f + k * 1.15f;
                big.SetChunk(a);
                float fl = f.RoadY(a) + 0.12f;
                var c = f.W(a, HallN0 + 1.2f, fl);
                PBlock(big[vend[k % 3]], f, c, 0.5f, 1.85f, 0.4f);
                PCard(small[glass], c + Vector3.up * 1.25f - f.N * 0.41f, f.T * 0.4f, Vector3.up * 0.42f, -f.N);
                PCard(small[TPBlack], c + Vector3.up * 0.35f - f.N * 0.41f, f.T * 0.3f, Vector3.up * 0.12f, -f.N);
            }
            for (int k = 0; k < 16; k++)
            {
                float a = PortApronA0 + 10f + (float)rng.NextDouble() * (PortApronA1 - PortApronA0 - 20f);
                float n = PortApronNear + 1.2f + (float)rng.NextDouble() * 3f;
                small.SetChunk(a);
                if (k < 6) PCrateStack(small, f, f.W(a, n, f.RoadY(a) + 0.12f), 1 + rng.Next(4), rng.NextDouble() < 0.5, rng);
                else
                {
                    bool walk = rng.NextDouble() < 0.6;
                    var at = f.W(a, n + 0.6f, f.RoadY(a) + 0.12f);
                    PortPerson(walk ? cast.walk : cast.stand, people, at, walk ? (rng.NextDouble() < 0.5 ? f.T : -f.T) : f.N,
                               walk ? MinatoCrowdActor.MotionKind.Walk : MinatoCrowdActor.MotionKind.Idle,
                               (float)rng.NextDouble(), "ApronShopper", at + f.T * (rng.NextDouble() < 0.5 ? 18f : -18f));
                }
            }
            for (int k = 0; k < 3; k++)
            {
                float a = HallA0 - 8f - k * 4f;
                big.SetChunk(a);
                PTurret(big, f, f.W(a, HallN0 + 3f + k * 2.2f, f.RoadY(a) + 0.12f), -1f, (k + 1) % 3);
            }
        }
    }

    /// <summary>Square-section beam between two points.</summary>
    private static void Beam(PB b, Vector3 p0, Vector3 p1, float half)
    {
        var d = p1 - p0;
        var ax = d * 0.5f;
        var side = Vector3.Cross(d, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up).normalized * half;
        var up = Vector3.Cross(side, d).normalized * half;
        PBox(b, (p0 + p1) * 0.5f, ax, up, side, 1f, true);
    }
}

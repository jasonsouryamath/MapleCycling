using UnityEngine;

/// <summary>
/// 2026-09-26 (claude-azora2): the route above the treeline was sparse. This pass fills the
/// alpine zone (the upper climb, where the pine density has fallen away) with Swiss alp
/// dressing: flowered meadow cushions and extra grass, scree fans of small flattened stones,
/// boulders, cairns, yellow hiking signposts, alp huts on stone bases with a Swiss flag, a
/// pasture fence and grazing cows and goats. Merged per 600 m tile like the rest of the
/// dressing. Called from the end of BuildHighlandDressing.
/// </summary>
public static partial class AzoraHighlandsEnvironment
{
    private static void BuildAlpineUpper(Transform dressGroup, AzoraRoute route, GlbSource[] rocks, GlbSource grass)
    {
        VillageMaterials();
        var shrubs = ProcShrubs();
        var group = new GameObject("Azora Alpine Upper").transform;
        group.SetParent(dressGroup, false);
        System.Func<string, Material> rockRole = _ => _rock;
        System.Func<string, Material> grassRole = _ => _grassBlade;

        var batch = new TileBatch();
        var bins = new Bins();
        int tile = 0, renderers = 0, cushions = 0, scree = 0, boulders = 0, huts = 0, cows = 0, goats = 0, signs = 0, cairns = 0;
        float nextTile = DressTileM;
        float nextHut = -1f, nextSign = -1f, nextCairn = -1f;
        int hutSide = 1;

        for (float d = 12f; d < route.Length - 12f; d += 3f)
        {
            if (d >= nextTile)
            {
                renderers += batch.Flush(group, "Alp", tile);
                renderers += bins.Flush(group, $"AlpBox{tile}");
                tile++; nextTile += DressTileM;
            }
            if (InVillage(d)) continue;
            int i = route.IndexAt(d);
            float y = route.Position[i].y;
            float frac = route.Frac(i);
            float alp = Mathf.InverseLerp(TreeLineY + 120f, TreeLineY + 420f, y);
            if (frac > 0.52f && frac < 0.97f) alp = Mathf.Max(alp, 0.65f);
            if (alp <= 0.02f) continue;
            if (nextHut < 0f) { nextHut = d + 300f; nextSign = d + 150f; nextCairn = d + 500f; }

            // ---- flowered meadow cushions and extra grass (both sides, near and mid distance)
            if (H01(d, 51.1f) < alp)
            {
                float r = H01(d, 52.2f);
                float off = (r > 0.5f ? 1f : -1f) * Mathf.Lerp(CorridorKeepOutM + 1.5f, 60f, Mathf.Pow(H01(d, 53.3f), 1.4f));
                DFoot(route, i, off, out var f);
                var src = shrubs[4 + (int)(H01(d, 54.4f) * 2f) % 2]; // low wide cushions
                float ch = Mathf.Lerp(0.35f, 0.7f, H01(d, 55.5f));
                if (!Wet(f) && AboveSnow(f, ch, out float cSink, H01(d, 58.1f), 0.15f))
                {
                    batch.Add(src, PlaceOnGround(src, f, r * 700f, ch, 1f, 0.04f + cSink),
                              ShrubRole(H01(d, 56.6f), H01(d, 57.7f) * 0.55f)); // ~65% flowered
                    cushions++;
                }
            }
            for (int k = 0; k < 2; k++)
            {
                if (H01(d, k + 58.8f) > alp) continue;
                float off = (H01(d, k + 59.9f) > 0.5f ? 1f : -1f) * Mathf.Lerp(CorridorKeepOutM + 0.5f, 40f, H01(d, k + 60.1f));
                DFoot(route, i, off, out var f);
                float gh = Mathf.Lerp(0.3f, 0.6f, H01(d, k + 62f));
                if (Wet(f) || !AboveSnow(f, gh, out float gSink, H01(d, k + 63.7f), 0.12f)) continue;
                batch.Add(grass, PlaceOnGround(grass, f, H01(d, k + 61f) * 700f, gh, 1f, 0.05f + gSink), grassRole);
            }

            // ---- dwarf-pine / juniper patches every ~21 m: 4-7 dark low shrubs in a clump
            if (Mathf.Repeat(d, 21f) < 3f && H01(d, 94.4f) < alp)
            {
                float side = H01(d, 95.5f) > 0.5f ? 1f : -1f;
                float cOff = side * Mathf.Lerp(8f, 110f, Mathf.Pow(H01(d, 96.6f), 1.3f));
                int n = 4 + (int)(H01(d, 97.7f) * 4f);
                for (int t = 0; t < n; t++)
                {
                    float off = cOff + (H01(d + t, 98.8f) - 0.5f) * 9f;
                    if (Mathf.Abs(off) < CorridorKeepOutM + 2f) off = side * (CorridorKeepOutM + 2f);
                    int j = route.IndexAt(Mathf.Clamp(d + (H01(d + t, 99.9f) - 0.5f) * 10f, 12f, route.Length - 12f));
                    DFoot(route, j, off, out var f);
                    var src = shrubs[(int)(H01(d + t, 100.1f) * 4f) % 4];
                    float ph = Mathf.Lerp(0.6f, 1.3f, H01(d + t, 102.2f));
                    // dark juniper patches are the one green that should break the ridge snow: 30% spared
                    if (Wet(f) || !AboveSnow(f, ph, out float pSink, H01(d + t, 105.5f), 0.30f)) continue;
                    batch.Add(src, PlaceOnGround(src, f, H01(d + t, 101.1f) * 360f, ph, 1f, 0.08f + pSink),
                              ShrubRole(0.75f + H01(d + t, 103.3f) * 0.25f, t == 0 ? H01(d, 104.4f) * 0.4f : 1f)); // dark greens
                    cushions++;
                }
            }

            // ---- scree fans: a patch of 8-14 flattened stones every ~9 m
            if (Mathf.Repeat(d, 9f) < 3f && H01(d, 63.3f) < alp)
            {
                float side = H01(d, 64.4f) > 0.5f ? 1f : -1f;
                float cOff = side * Mathf.Lerp(7f, 85f, Mathf.Pow(H01(d, 65.5f), 1.2f));
                int n = 8 + (int)(H01(d, 66.6f) * 7f);
                for (int t = 0; t < n; t++)
                {
                    float off = cOff + (H01(d + t, 67.7f) - 0.5f) * 10f;
                    if (Mathf.Abs(off) < 6f) off = side * 6f;
                    int j = route.IndexAt(Mathf.Clamp(d + (H01(d + t, 68.8f) - 0.5f) * 9f, 12f, route.Length - 12f));
                    DFoot(route, j, off, out var f);
                    var src = rocks[t & 1];
                    float h = Mathf.Lerp(0.25f, 0.95f, H01(d + t, 69.9f));
                    if (Wet(f) || !AboveSnow(f, h, out _)) continue;   // small scree vanishes under the pack
                    batch.Add(src, PlaceOnGround(src, f, H01(d + t, 70.1f) * 360f, h, 0.55f, h * 0.15f), rockRole);
                    scree++;
                }
            }

            // ---- boulders every ~21 m
            if (Mathf.Repeat(d, 21f) < 3f && H01(d, 71.1f) < alp)
            {
                float side = H01(d, 72.2f) > 0.5f ? 1f : -1f;
                DFoot(route, i, side * Mathf.Lerp(8f, 130f, Mathf.Pow(H01(d, 73.3f), 1.3f)), out var f);
                var src = rocks[(int)(H01(d, 74.4f) * 2f) & 1];
                float h = Mathf.Lerp(1.4f, 3.8f, H01(d, 75.5f));
                if (!Wet(f, h))
                batch.Add(src, PlaceOnGround(src, f, H01(d, 76.6f) * 360f, h, 1f, h * 0.25f), rockRole);
                boulders++;
            }

            var s = route.SideFlat(i); var sf = new Vector3(s.x, 0f, s.z).normalized; var tg = new Vector3(-sf.z, 0f, sf.x);

            // ---- yellow Swiss hiking signpost (Wanderweg) beside the road
            if (d >= nextSign)
            {
                nextSign = d + 1100f + H01(d, 77.7f) * 600f;
                DFoot(route, i, -(RoadHalfWidth + ShoulderWidth + 1.4f), out var f);
                if (!Wet(f)) {
                BoxB(bins[_steel], f - Vector3.up * 0.3f, sf * 0.05f, tg * 0.05f, 2.7f);
                for (int b = 0; b < 3; b++)
                {
                    float dir = b % 2 == 0 ? 1f : -1f;
                    var bc = f + Vector3.up * (2.2f - b * 0.3f) + tg * dir * 0.45f;
                    BoxB(bins[_alpineYellow], bc, tg * 0.5f, sf * 0.03f, 0.22f);
                    BoxB(bins[_alpineYellow], bc + tg * dir * 0.55f + Vector3.up * 0.03f, tg * 0.06f, sf * 0.03f, 0.16f); // arrow tip
                }
                BoxB(bins[_alpineWhite], f + Vector3.up * 2.45f, tg * 0.14f, sf * 0.035f, 0.14f); // location plate
                signs++; }
            }

            // ---- cairns
            if (d >= nextCairn)
            {
                nextCairn = d + 700f + H01(d, 78.8f) * 500f;
                float side = H01(d, 79.9f) > 0.5f ? 1f : -1f;
                DFoot(route, i, side * Mathf.Lerp(9f, 20f, H01(d, 80.1f)), out var f);
                float sz = 0.7f;
                for (int b = 0; b < 5 && !Wet(f); b++)
                {
                    float a = b * 0.9f;
                    var dd = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var rt = new Vector3(dd.z, 0f, -dd.x);
                    BoxB(bins[_stoneBase], f + Vector3.up * (b * 0.36f - 0.2f), dd * sz, rt * sz * 0.85f, 0.4f);
                    sz *= 0.78f;
                }
                cairns++;
            }

            // ---- alp hut with pasture, fence, cows and goats
            if (d >= nextHut)
            {
                nextHut = d + 1300f + H01(d, 81.1f) * 500f;
                hutSide = -hutSide;
                float side = hutSide;
                float off = side * Mathf.Lerp(26f, 42f, H01(d, 82.2f));
                DFoot(route, i, off, out var f);
                if (Wet(f, 36f)) continue;   // hut + paddock + herd footprint; skip this hut
                var fwd = -sf * side;
                var right = Vector3.Cross(Vector3.up, fwd).normalized;
                AlpHut(bins, f, fwd, right, 3.4f, 4.0f);
                // lean-to cow shed on a stone base beside it
                var shed = f + right * 8.5f;
                shed.y = GH(shed.x, shed.z);
                float sb = MinGround(shed, right * 3.2f, fwd * 3.2f) - 0.4f;
                BoxB(bins[_stoneBase], new Vector3(shed.x, sb, shed.z), right * 3f, fwd * 3f, shed.y + 1.3f - sb);
                BoxB(bins[_chaletTimber], shed + Vector3.up * 1.3f, right * 3f, fwd * 3f, 1.6f);
                BoxB(bins[_chaletRoof], shed + Vector3.up * 2.9f, right * 3.5f, fwd * 3.6f, 0.22f);
                // Swiss flag on a pole
                var pole = f + fwd * 7f - right * 5f; pole.y = GH(pole.x, pole.z);
                BoxB(bins[_alpineWhite], pole - Vector3.up * 0.3f, right * 0.05f, fwd * 0.05f, 7.3f);
                var flag = pole + Vector3.up * 6.2f + right * 0.62f;
                BoxB(bins[_awningRed], flag, right * 0.6f, fwd * 0.03f, 1.1f);
                BoxB(bins[_alpineWhite], flag + Vector3.up * 0.38f - fwd * 0.0f, right * 0.36f, fwd * 0.04f, 0.14f); // cross bar
                BoxB(bins[_alpineWhite], flag + Vector3.up * 0.24f, right * 0.08f, fwd * 0.04f, 0.62f);             // cross stem
                // pasture fence: a square paddock downhill/beside the hut
                var pc = f + fwd * 18f + right * 4f;
                for (int e = 0; e < 4; e++)
                {
                    for (int q = 0; q < 8; q++)
                    {
                        float u0 = -12f + q * 3f, u1 = u0 + 3f;
                        Vector3 A, B;
                        if (e == 0) { A = pc + right * u0 - fwd * 10f; B = pc + right * u1 - fwd * 10f; }
                        else if (e == 1) { A = pc + right * u0 + fwd * 10f; B = pc + right * u1 + fwd * 10f; }
                        else if (e == 2) { A = pc - right * 12f + fwd * (u0 * 0.83f); B = pc - right * 12f + fwd * (u1 * 0.83f); }
                        else { A = pc + right * 12f + fwd * (u0 * 0.83f); B = pc + right * 12f + fwd * (u1 * 0.83f); }
                        if (e == 1 && q == 3) continue; // gate gap
                        A.y = GH(A.x, A.z); B.y = GH(B.x, B.z);
                        BoxB(bins[_chaletTimber], A - Vector3.up * 0.2f, right * 0.06f, fwd * 0.06f, 1.3f);
                        Beam(bins[_chaletTimber], A + Vector3.up * 0.55f, B + Vector3.up * 0.55f, 0.04f);
                        Beam(bins[_chaletTimber], A + Vector3.up * 1.0f, B + Vector3.up * 1.0f, 0.04f);
                    }
                }
                int nc = 3 + (int)(H01(d, 84.4f) * 3f);
                for (int k = 0; k < nc; k++)
                {
                    var cp = pc + right * (H01(d + k, 85.5f) - 0.5f) * 20f + fwd * (H01(d + k, 86.6f) - 0.5f) * 16f;
                    cp.y = GH(cp.x, cp.z);
                    if (Wet(cp)) continue;
                    float a = H01(d + k, 87.7f) * 6.283f;
                    Cow(bins, cp, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), H01(d + k, 88.8f));
                    cows++;
                }
                int ng = 4 + (int)(H01(d, 89.9f) * 5f);
                for (int k = 0; k < ng; k++)
                {
                    var gp = f - fwd * (8f + H01(d + k, 90.1f) * 18f) + right * (H01(d + k, 91.1f) - 0.5f) * 26f;
                    gp.y = GH(gp.x, gp.z);
                    if (Wet(gp)) continue;
                    float a = H01(d + k, 92.2f) * 6.283f;
                    Goat(bins, gp, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), H01(d + k, 93.3f));
                    goats++;
                }
                huts++;
            }
        }
        renderers += batch.Flush(group, "Alp", tile);
        renderers += bins.Flush(group, $"AlpBox{tile}");
        Debug.Log($"[azora] alpine upper: {cushions} flower cushions, {scree} scree stones, {boulders} boulders, {huts} alp huts, " +
                  $"{cows} cows, {goats} goats, {signs} signposts, {cairns} cairns, {renderers} renderers.");
    }

    /// <summary>
    /// Self-contained alp hut (stone plinth, timber walls, deep-eaved gable roof). Kept local to
    /// the alpine pass so it never depends on the village package's chalet builder.
    /// </summary>
    private static void AlpHut(Bins bins, Vector3 f, Vector3 fwd, Vector3 right, float W, float D)
    {
        float g = MinGround(f, right * W, fwd * D);
        float plinth = f.y + 0.9f - (g - 0.4f);
        BoxB(bins[_stoneBase], new Vector3(f.x, g - 0.4f, f.z), right * W, fwd * D, plinth);
        float wallBase = g - 0.4f + plinth;
        const float WallH = 2.4f;
        BoxB(bins[_chaletTimber], new Vector3(f.x, wallBase, f.z), right * (W * 0.96f), fwd * (D * 0.96f), WallH);
        float eave = wallBase + WallH, ridge = eave + W * 0.55f;
        var c = new Vector3(f.x, 0f, f.z);
        Vector3 P(float r, float fw, float y) { var p = c + right * r + fwd * fw; p.y = y; return p; }
        float ro = W * 1.22f, fo = D * 1.18f;
        // pitches fall to the left and right; ridge runs along fwd
        QuadDS(bins[_chaletRoof], P(-ro, -fo, eave - 0.25f), P(-ro, fo, eave - 0.25f), P(0f, fo, ridge), P(0f, -fo, ridge));
        QuadDS(bins[_chaletRoof], P(ro, -fo, eave - 0.25f), P(ro, fo, eave - 0.25f), P(0f, fo, ridge), P(0f, -fo, ridge));
        TriDS(bins[_chaletTimber], P(-W * 0.96f, D * 0.96f, eave), P(W * 0.96f, D * 0.96f, eave), P(0f, D * 0.96f, ridge - 0.12f));
        TriDS(bins[_chaletTimber], P(-W * 0.96f, -D * 0.96f, eave), P(W * 0.96f, -D * 0.96f, eave), P(0f, -D * 0.96f, ridge - 0.12f));
        AlpHutFacade(bins, c, fwd, right, W * 0.96f, D * 0.96f, wallBase, eave);
    }

    /// <summary>
    /// A5 (copilot 2026-09-26): the alp hut read as a blank timber crate at close range. Adds the
    /// front gable's door, shuttered windows with geranium boxes, a gable loft window, one window
    /// per long wall, a bench and a stacked woodpile - the Swiss alp-hut vocabulary.
    /// <paramref name="w"/>/<paramref name="d"/> are the wall half-extents; +fwd is the road-facing gable.
    /// </summary>
    private static void AlpHutFacade(Bins bins, Vector3 c, Vector3 fwd, Vector3 right, float w, float d,
                                     float wallBase, float eave)
    {
        Vector3 At(float r, float fw, float y) { var p = c + right * r + fwd * fw; p.y = y; return p; }

        // Door with a pale frame, on the left of the front gable.
        BoxB(bins[_chaletRender], At(-w * 0.38f, d + 0.03f, wallBase - 0.02f), right * 0.62f, fwd * 0.04f, 2.26f);
        BoxB(bins[_door], At(-w * 0.38f, d + 0.07f, wallBase), right * 0.50f, fwd * 0.04f, 2.1f);
        BoxB(bins[_stoneBase], At(-w * 0.38f, d + 0.45f, wallBase - 0.45f), right * 0.75f, fwd * 0.40f, 0.45f);

        void Window(Vector3 foot, Vector3 across, Vector3 outN, float halfW, float h)
        {
            BoxB(bins[_chaletRender], foot - Vector3.up * 0.06f, across * (halfW + 0.07f), outN * 0.03f, h + 0.12f);
            BoxB(bins[_window], foot + outN * 0.035f, across * halfW, outN * 0.03f, h);
            BoxB(bins[_chaletRender], foot + outN * 0.07f + Vector3.up * (h * 0.5f - 0.025f), across * halfW, outN * 0.02f, 0.05f);
            for (int s = -1; s <= 1; s += 2)
                BoxB(bins[_shutter], foot + across * (s * (halfW + 0.30f)) + outN * 0.05f, across * 0.26f, outN * 0.03f, h);
            // geranium box
            var box = foot + outN * 0.16f - Vector3.up * 0.22f;
            BoxB(bins[_chaletTimber], box, across * (halfW + 0.08f), outN * 0.13f, 0.2f);
            for (int k = -2; k <= 2; k++)
                BoxB(bins[k % 2 == 0 ? _flowerRed : _flowerPink], box + across * (k * halfW * 0.4f) + Vector3.up * 0.18f,
                     across * 0.11f, outN * 0.10f, 0.16f);
        }

        Window(At(w * 0.40f, d, wallBase + 0.95f), right, fwd, 0.38f, 0.85f);
        Window(At(0f, d, eave + 0.30f), right, fwd, 0.26f, 0.60f);     // loft window in the gable
        Window(At(w, 0f, wallBase + 0.95f), fwd, right, 0.38f, 0.85f);
        Window(At(-w, -d * 0.35f, wallBase + 0.95f), -fwd, -right, 0.38f, 0.85f);

        // Bench under the front window.
        var bench = At(w * 0.40f, d + 0.42f, wallBase - 0.02f);
        BoxB(bins[_chaletTimber], bench + Vector3.up * 0.42f, right * 0.85f, fwd * 0.20f, 0.06f);
        for (int s = -1; s <= 1; s += 2)
            BoxB(bins[_chaletTimber], bench + right * (s * 0.72f), right * 0.05f, fwd * 0.16f, 0.42f);

        // Woodpile against the back half of the right-hand wall: a stack with round-log ends.
        var pile = At(-w - 0.45f, -d * 0.55f, wallBase - 0.05f);
        BoxB(bins[_chaletTimber], pile, right * 0.40f, fwd * 1.30f, 1.35f);
        BoxB(bins[_chaletRoof], pile + Vector3.up * 1.40f + right * 0.05f, right * 0.55f, fwd * 1.45f, 0.08f);
        for (int row = 0; row < 4; row++)
            for (int k = -3; k <= 3; k++)
                BoxB(bins[_cowBrown], pile - right * 0.41f + fwd * (k * 0.36f + (row & 1) * 0.18f) + Vector3.up * (0.12f + row * 0.31f),
                     right * 0.02f, fwd * 0.14f, 0.26f);
    }
}
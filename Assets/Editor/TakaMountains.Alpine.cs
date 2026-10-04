using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TAKA ALPINE DRESSING (2026-09-26, REGION_REVIEW_TODO items 4-6). The region shipped as a bare
/// road between blank slopes. This pass fills the corridor with the things a real alpine pass
/// has, all measured off the published centreline and merged into per-route-chunk meshes:
///   * faceted granite crags and boulders whose UP-facing facets carry snow above the snow line
///     (exposed rock through snow, and rock that reads as faceted stone rather than a smooth blob)
///   * a ring of jagged snow-capped peaks 1.3-3 km out (the "distant peaks" the brief wants)
///   * alpine conifers dense at the trailhead, thinning with altitude, frosted near the treeline
///   * galvanised guardrail on every drop-off side, km / hairpin signs, solid road edge lines
///   * refuge huts along the climb, a summit lodge with flagpoles and prayer-flag bunting,
///     bunting strung across the road on the summit plateau
/// Nothing is placed inside RoadHalfWidth + ShoulderWidth; the nearest element is the guardrail
/// at 3.35 m. CelLit ignores vertex colour, so every colour is its own material.
/// </summary>
public static partial class TakaMountainsEnvironment
{
    private const float ChunkM = 1500f;

    private sealed class Bucket
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        public readonly string Name; public readonly Material Mat; public int Part;
        public Bucket(string name, Material mat) { Name = name; Mat = mat; }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int k = V.Count;
            V.Add(a); V.Add(b); V.Add(c); UV.Add(ua); UV.Add(ub); UV.Add(uc);
            T.Add(k); T.Add(k + 1); T.Add(k + 2);
        }
        public void TriW(Vector3 a, Vector3 b, Vector3 c, float s = 0.25f) =>
            Tri(a, b, c, WUV(a, s), WUV(b, s), WUV(c, s));
        /// <summary>Emit with the winding flipped as needed so the face points AWAY from 'from'.</summary>
        public void TriAway(Vector3 a, Vector3 b, Vector3 c, Vector3 from, float s = 0.25f)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f - from) < 0f) { var t = b; b = c; c = t; }
            TriW(a, b, c, s);
        }
        /// <summary>Double-sided triangle on separate vertices (see the note in Quad()).</summary>
        public void Tri2(Vector3 a, Vector3 b, Vector3 c)
        {
            Tri(a, b, c, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0));
            Tri(a, c, b, new Vector2(0, 1), new Vector2(0.5f, 0), new Vector2(1, 1));
        }
        public void Flush(Transform parent)
        {
            if (T.Count == 0) return;
            var mesh = Finish($"TakaA_{Name}_{Part:00}", V.ToArray(), UV.ToArray(), T);
            AddMesh(parent, $"{Name} {Part:00}", mesh, Mat, collider: false);
            Part++; V.Clear(); UV.Clear(); T.Clear();
        }
        public void FlushIfBig(Transform parent) { if (V.Count > 60000) Flush(parent); }
    }

    /// <summary>World-planar UVs: vertical strata on the granite map for side faces.</summary>
    private static Vector2 WUV(Vector3 p, float s) => new Vector2((p.x + p.z) * s, p.y * s);

    private static bool InVentouxSite(float d) =>
        d < 820f || Mathf.Abs(d - 9400f) < 90f || Mathf.Abs(d - CpCol) < 130f;

    private static float SnowLineAt(float x, float z) => 2230f + 90f * Mathf.PerlinNoise(x * 0.004f, z * 0.004f);

    // ------------------------------------------------------------------ materials
    private static Material CragMaterial() =>
        CelMaterial("TakaA_Crag", new Color(0.46f, 0.47f, 0.49f), gloss: 0.10f, spec: 0.08f, rim: 0.10f,
                    texture: TakaTexture("Taka_Granite_Albedo.png"), shade: GroundShade);
    private static Material CragDarkMaterial() =>
        CelMaterial("TakaA_CragDark", new Color(0.33f, 0.34f, 0.36f), gloss: 0.10f, spec: 0.08f, rim: 0.10f,
                    texture: TakaTexture("Taka_Granite_Albedo.png"), shade: GroundShade);
    // 2026-09-26 claude-taka2 (Mt. Ventoux): the up-facing facets above the "snow line" are now
    // sun-bleached pale LIMESTONE, not snow - Ventoux's bald top is bare broken stone. The
    // material keeps its old asset name so nothing else has to be re-pointed.
    // 2026-09-26 copilot session 3 (T3): the limestone map is now warm buff at a mean ~0.52 (was
    // off-white ~0.72); these multipliers are nudged so the product lands a little darker and
    // clearly warmer than before, and the shade is a warm grey rather than a cool one.
    private static Material SnowCapMaterial() =>
        CelMaterial("TakaA_SnowCap", new Color(0.76f, 0.72f, 0.65f), gloss: 0.12f, spec: 0.08f, rim: 0.20f,
                    texture: TakaTexture("Taka_Limestone_Albedo.png"), shade: LimeShade);
    private static Material LimeCragMaterial() =>
        CelMaterial("TakaA_LimeCrag", new Color(0.64f, 0.60f, 0.54f), gloss: 0.10f, spec: 0.08f, rim: 0.12f,
                    texture: TakaTexture("Taka_Limestone_Albedo.png"), shade: LimeShade);
    /// <summary>Warm grey shadow for limestone: the blue GroundShade made it read as snow.</summary>
    private static readonly Color LimeShade = new Color(0.60f, 0.555f, 0.51f, 1f);

    /// <summary>
    /// Guardrail test (claude-taka2). The road cut pins the ground within ~18 m of the centreline,
    /// so the old "9 m out is 1.4 m lower" test never fired (0 bays). Now: the ground 24-36 m out
    /// falls well below the road (an exposed edge), OR the side is the OUTSIDE of a bend and the
    /// ground there still falls away.
    /// </summary>
    private static bool NeedsRail(TakaRoute route, int i, Vector3 p, Vector3 side, int s, float d)
    {
        float h24 = Height(p.x + side.x * s * 24f, p.z + side.z * s * 24f);
        float h36 = Height(p.x + side.x * s * 36f, p.z + side.z * s * 36f);
        float drop = p.y - Mathf.Min(h24, h36);
        if (drop > 5.5f) return true;
        // outside of a bend: the tangent turns AWAY from side s over the next 30 m
        int a = route.IndexAt(Mathf.Max(0f, d - 25f)), b = route.IndexAt(Mathf.Min(route.Length, d + 25f));
        var ta = route.Tangent[a]; ta.y = 0f; var tb = route.Tangent[b]; tb.y = 0f;
        float turn = Vector3.SignedAngle(ta, tb, Vector3.up);   // + = turning right (clockwise from above)
        if (Mathf.Abs(turn) < 28f) return false;
        // side s is outside when the road turns toward -s
        // Cross(up, forward) is the RIGHT-hand side in Unity. Turning right -> outside is the left.
        float sideDot = Vector3.Dot(Vector3.Cross(Vector3.up, ta.normalized), side * s);   // >0: s is the RIGHT side
        bool outside = turn > 0f ? sideDot < 0f : sideDot > 0f;
        return outside && drop > 1.5f;
    }

    // ------------------------------------------------------------------ entry
    private static void BuildAlpineDressing(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Alpine Dressing").transform;
        group.SetParent(root, false);
        var rng = new System.Random(9127);

        var crag = new Bucket("Crag", CragMaterial());
        var cragDark = new Bucket("CragDark", CragDarkMaterial());
        var snow = new Bucket("SnowCap", SnowCapMaterial());
        var needles = new Bucket("Conifer", CelMaterial("TakaA_Conifer", new Color(0.13f, 0.24f, 0.19f),
                                   gloss: 0.12f, spec: 0.05f, rim: 0.30f, shade: new Color(0.30f, 0.42f, 0.52f, 1f)));
        // 2026-09-26 claude-taka2: no frost on Ventoux - this slot is now the blue-green ATLAS CEDAR
        // of the middle band (same asset name, recoloured).
        var frost = new Bucket("ConiferFrost", CelMaterial("TakaA_ConiferFrost", new Color(0.21f, 0.31f, 0.26f),
                                   gloss: 0.12f, spec: 0.05f, rim: 0.30f, shade: new Color(0.30f, 0.42f, 0.52f, 1f)));
        var limeCrag = new Bucket("LimeCrag", LimeCragMaterial());
        var trunk = new Bucket("Trunk", TimberMaterial());
        var steelMat = CelMaterial("TakaA_Galvanised", new Color(0.66f, 0.68f, 0.70f),
                                   gloss: 0.55f, spec: 0.45f, rim: 0.30f, shade: GroundShade);
        if (steelMat.HasProperty("_Cull")) steelMat.SetFloat("_Cull", 0f);
        var steel = new Bucket("Guardrail", steelMat);
        var edge = new Bucket("EdgeLine", CelMaterial("TakaA_EdgePaint", new Color(0.88f, 0.88f, 0.86f),
                                   gloss: 0.2f, spec: 0.1f, rim: 0.05f, shade: GroundShade));
        var signY = new Bucket("SignYellow", CelMaterial("TakaA_SignYellow", new Color(0.95f, 0.72f, 0.10f),
                                   gloss: 0.4f, spec: 0.3f, rim: 0.3f));
        var signB = new Bucket("SignBlue", CelMaterial("TakaA_SignBlue", new Color(0.10f, 0.30f, 0.62f),
                                   gloss: 0.4f, spec: 0.3f, rim: 0.3f));
        var wall = new Bucket("HutWall", TimberMaterial());
        var roof = new Bucket("HutRoof", CelMaterial("TakaA_RoofRed", new Color(0.52f, 0.15f, 0.12f),
                                   gloss: 0.35f, spec: 0.25f, rim: 0.25f));
        var stone = new Bucket("HutStone", StoneMaterial());
        var flagCols = new[]
        {
            new Color(0.12f, 0.35f, 0.80f), new Color(0.93f, 0.93f, 0.90f), new Color(0.82f, 0.16f, 0.12f),
            new Color(0.14f, 0.58f, 0.28f), new Color(0.96f, 0.78f, 0.12f),
        };
        var flags = new Bucket[flagCols.Length];
        for (int c = 0; c < flags.Length; c++)
        {
            var m = CelMaterial($"TakaA_Flag{c}", flagCols[c], gloss: 0.2f, spec: 0.1f, rim: 0.45f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            flags[c] = new Bucket($"Flag{c}", m);
        }
        var all = new List<Bucket> { crag, cragDark, snow, needles, frost, trunk, steel, edge, signY, signB,
                                     wall, roof, stone, limeCrag };
        var moonTuft = new Bucket("MoonTuft", CelMaterial("TakaA_MoonTuft", new Color(0.36f, 0.38f, 0.24f),
                                   gloss: 0.08f, spec: 0.04f, rim: 0.30f, shade: new Color(0.52f, 0.52f, 0.46f, 1f)));
        all.Add(moonTuft);
        int nTufts = 0;
        all.AddRange(flags);

        float edgeOff = RoadHalfWidth + ShoulderWidth;   // 3.05 m: nothing inside this
        int nTrees = 0, nCrags = 0, nRail = 0;
        float nextChunk = ChunkM;

        for (float d = 12f; d < route.Length - 12f; d += 6f)
        {
            if (d >= nextChunk) { foreach (var b in all) b.Flush(group); nextChunk += ChunkM; }

            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            var fwd = Vector3.Cross(side, Vector3.up).normalized * -1f;
            if (Vector3.Dot(fwd, route.Tangent[i]) < 0f) fwd = -fwd;

            // ---- solid edge lines (both sides), 6 m ribbons
            {
                int j = route.IndexAt(d + 6f);
                for (int s = -1; s <= 1; s += 2)
                {
                    float o = s * (RoadHalfWidth - 0.18f);
                    var a0 = route.Position[i] + route.SideFlat(i) * (o - 0.06f);
                    var a1 = route.Position[i] + route.SideFlat(i) * (o + 0.06f);
                    var b0 = route.Position[j] + route.SideFlat(j) * (o - 0.06f);
                    var b1 = route.Position[j] + route.SideFlat(j) * (o + 0.06f);
                    float ya = RoadY(route, i, o) + MarkingLiftM, yb = RoadY(route, j, o) + MarkingLiftM;
                    a0.y = a1.y = ya; b0.y = b1.y = yb;
                    edge.TriAway(a0, b0, a1, (a0 + b1) * 0.5f - Vector3.up);
                    edge.TriAway(a1, b0, b1, (a0 + b1) * 0.5f - Vector3.up);
                }
            }

            // ---- guardrail on drop-off sides (posts every 6 m, a two-strip W-beam)
            for (int s = -1; s <= 1; s += 2)
            {
                if (InVentouxSite(d) || !NeedsRail(route, i, p, side, s, d)) continue;
                int j = route.IndexAt(d + 6f);
                float o = s * (edgeOff + 0.30f);
                var pa = route.Position[i] + route.SideFlat(i) * o; pa.y = RoadY(route, i, o);
                var pb = route.Position[j] + route.SideFlat(j) * o; pb.y = RoadY(route, j, o);
                Post(steel.V, steel.UV, steel.T, null, pa - Vector3.up * 0.3f, pa + Vector3.up * 0.78f, 0.06f, Color.white);
                RailStrip(steel, pa + Vector3.up * 0.62f, pb + Vector3.up * 0.62f, 0.16f, route.SideFlat(i) * s);
                RailStrip(steel, pa + Vector3.up * 0.40f, pb + Vector3.up * 0.40f, 0.10f, route.SideFlat(i) * s);
                nRail++;
            }

            // ---- conifers: dense low, thinning with altitude, frosted near the treeline
            float treeLine = 2330f;
            float lush = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1650f, treeLine, p.y));
            int tries = 3;
            for (int k = 0; k < tries; k++)
            {
                if (rng.NextDouble() > lush * 0.85f) continue;
                int s = rng.NextDouble() < 0.5 ? -1 : 1;
                float o = s * Mathf.Lerp(8.5f, 85f, (float)(rng.NextDouble() * rng.NextDouble()));
                float along = (float)(rng.NextDouble() - 0.5) * 6f;
                var fx = p + side * o + fwd * along;
                if (NearestDist(fx) < 7.5f || InVentouxSite(d) || InProvence(fx, 3.5f)) continue;
                fx.y = Height(fx.x, fx.z);
                float h = Mathf.Lerp(6f, 15f, (float)rng.NextDouble()) * Mathf.Lerp(0.6f, 1f, lush);
                // Ventoux middle band: dark pines low, blue-green Atlas cedars from ~1,750 m up.
                bool frosty = fx.y > 1750f ? rng.NextDouble() < 0.6 : rng.NextDouble() < 0.12;
                if (!Kit2Tree(fx, h, frosty, rng))
                {
                    if (frosty) Cedar(frost, trunk, fx, h, rng); else Conifer(needles, trunk, fx, h, rng);
                }
                // small clusters
                if (rng.NextDouble() < 0.5)
                {
                    var f2 = fx + new Vector3((float)rng.NextDouble() * 5f - 2.5f, 0, (float)rng.NextDouble() * 5f - 2.5f);
                    if (NearestDist(f2) > 7.5f && !InProvence(f2, 3.5f))
                    {
                        f2.y = Height(f2.x, f2.z);
                        if (!Kit2Tree(f2, h * 0.7f, frosty, rng)) Conifer(frosty ? frost : needles, trunk, f2, h * 0.7f, rng);
                        nTrees++;
                    }
                }
                nTrees++;
            }

            // ---- crags and boulders every ~18 m (more above the treeline)
            if (((int)(d / 6f)) % 3 == 0)
            {
                // above the treeline: the limestone moonscape - many more small shards of scree
                bool moon = p.y > 2250f;
                int count = moon ? 7 : 2;
                for (int k = 0; k < count; k++)
                {
                    int s = rng.NextDouble() < 0.5 ? -1 : 1;
                    float u = (float)rng.NextDouble();
                    bool big = moon ? u > 0.85f : u > 0.72f;
                    float rad = big ? Mathf.Lerp(4f, 11f, (float)rng.NextDouble()) : Mathf.Lerp(0.6f, 2.4f, (float)rng.NextDouble());
                    float o = s * (edgeOff + 3.2f + rad + Mathf.Lerp(0f, big ? 70f : 30f, (float)rng.NextDouble()));
                    var c = p + side * o + fwd * ((float)rng.NextDouble() * 6f - 3f);
                    if (NearestDist(c) < rad + 5.5f || InVentouxSite(d) || InProvence(c, rad + 1f)) continue;
                    c.y = Height(c.x, c.z);
                    // Crags stand up (tall ribs of rock), boulders sit low.
                    var radii = big ? new Vector3(rad, rad * Mathf.Lerp(moon ? 0.7f : 1.1f,
                                                                      moon ? 1.45f : 2.2f, (float)rng.NextDouble()), rad * 0.8f)
                                    : new Vector3(rad, rad * 0.7f, rad * 0.85f);
                    var rb = moon ? limeCrag : (rng.NextDouble() < 0.4 ? cragDark : crag);
                    if (moon && !big) radii = new Vector3(radii.x, radii.y * 0.55f, radii.z);   // flat shards
                    if (!(moon && Kit2Rock(c, rad, big, rng)))
                        Rock(rb, snow, c - Vector3.up * radii.y * 0.25f, radii,
                             (float)rng.NextDouble() * 360f, big ? 9 : 6, big ? 6 : 4, rng, snowBias: 0f);
                    nCrags++;
                    if (moon && big)
                    {
                        // A talus apron ties the larger bedding outcrop to the scree slope.
                        // Three low shards share its location but not its silhouette.
                        for (int chip = 0; chip < 3; chip++)
                        {
                            float a = (chip + (float)rng.NextDouble() * 0.35f) * Mathf.PI * 2f / 3f;
                            float r = rad * (0.60f + 0.23f * (float)rng.NextDouble());
                            var q = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                            q.y = Height(q.x, q.z);
                            float size = rad * (0.18f + 0.09f * (float)rng.NextDouble());
                            if (!Kit2Rock(q, size * 1.7f, false, rng))
                                Rock(limeCrag, snow, q - Vector3.up * size * 0.12f,
                                     new Vector3(size * 1.7f, size * 0.45f, size),
                                     (float)rng.NextDouble() * 360f, 6, 3, rng, snowBias: 0f);
                            nCrags++;
                        }
                    }
                }
                // 2026-09-26 copilot session 3 (T3): sparse cushion plants on the moonscape -
                // the only life up there, and the cue that the pale ground is stone, not snow.
                if (moon)
                {
                    int tufts = 1 + rng.Next(4);
                    for (int k = 0; k < tufts; k++)
                    {
                        int s = rng.NextDouble() < 0.5 ? -1 : 1;
                        float uu = (float)rng.NextDouble();
                        float o = s * (edgeOff + 1.0f + 45f * uu * uu);
                        var c = p + side * o + fwd * ((float)rng.NextDouble() * 6f - 3f);
                        if (NearestDist(c) < edgeOff + 0.8f || InVentouxSite(d)) continue;
                        c.y = Height(c.x, c.z);
                        float r = 0.20f + 0.32f * (float)rng.NextDouble();
                        Rock(moonTuft, moonTuft, c - Vector3.up * (r * 0.12f), new Vector3(r, r * 0.42f, r * 0.85f),
                             (float)rng.NextDouble() * 360f, 6, 2, rng, snowBias: -9999f, uvScale: 0.5f);
                        nTufts++;
                    }
                }
            }

            foreach (var b in all) b.FlushIfBig(group);
        }
        foreach (var b in all) b.Flush(group);

        // ---- signs: km boards every 1 km, chevrons on tight bends
        int nSigns = 0;
        for (float d = 500f; d < route.Length - 50f; d += 500f)
        {
            int i = route.IndexAt(d);
            float o = (nSigns % 2 == 0 ? 1f : -1f) * (edgeOff + 1.2f);
            Foot(route, i, o, out var f, out var outward);
            f.y = Mathf.Max(f.y, RoadY(route, i, o) - 0.2f);
            var fwd = Vector3.Cross(Vector3.up, outward).normalized;
            Post(trunk.V, trunk.UV, trunk.T, null, f - Vector3.up * 0.3f, f + Vector3.up * 2.1f, 0.05f, Color.white);
            // board faces along the road
            var c = f + Vector3.up * 2.0f;
            var bucket = (nSigns % 2 == 0) ? signB : signY;
            Box(bucket.V, bucket.UV, bucket.T, null, c, outward.normalized * 0.45f, fwd * 0.03f, 0.55f, Color.white);
            // chevron (yellow) on bends
            int j = route.IndexAt(d + 30f);
            if (Vector3.Angle(route.Tangent[i], route.Tangent[j]) > 25f)
            {
                Foot(route, j, -o, out var f2, out var out2);
                f2.y = Mathf.Max(f2.y, RoadY(route, j, -o) - 0.2f);
                var fwd2 = Vector3.Cross(Vector3.up, out2).normalized;
                Post(trunk.V, trunk.UV, trunk.T, null, f2 - Vector3.up * 0.3f, f2 + Vector3.up * 1.6f, 0.05f, Color.white);
                Box(signY.V, signY.UV, signY.T, null, f2 + Vector3.up * 1.2f, fwd2 * 0.55f, out2 * 0.03f, 0.6f, Color.white);
            }
            nSigns++;
        }

        // ---- refuge huts along the route, a summit lodge with flags
        int nHuts = 0;
        for (float d = 1800f; d < route.Length - 800f; d += 2600f)
        {
            if (Mathf.Abs(d - CpCol) < 900f) continue;
            int i = route.IndexAt(d);
            float o = (nHuts % 2 == 0 ? 1f : -1f) * 22f;
            Foot(route, i, o, out var f, out var outward);
            if (RoadDist(f.x, f.z) < 14f) { o = -o; Foot(route, i, o, out f, out outward); }
            if (RoadDist(f.x, f.z) < 14f) continue;
            Hut(wall, roof, stone, f, outward, 5.2f, 4.0f, 2.6f);
            // a short bunting from the gable to a pole
            var pole = f + outward * 7f; pole.y = Height(pole.x, pole.z);
            Post(trunk.V, trunk.UV, trunk.T, null, pole, pole + Vector3.up * 5f, 0.07f, Color.white);
            Bunting(flags, f + Vector3.up * 4.4f, pole + Vector3.up * 4.8f, 0.5f);
            nHuts++;
        }
        {
            int i = route.IndexAt(CpCol + 160f);
            float o = 26f;
            Foot(route, i, o, out var f, out var outward);
            if (RoadDist(f.x, f.z) < 18f) { o = -o; Foot(route, i, o, out f, out outward); }
            Hut(wall, roof, stone, f, outward, 13f, 8f, 4.2f);
            var along = Vector3.Cross(Vector3.up, outward).normalized;
            // three flagpoles in front of the lodge, towards the road
            for (int k = -1; k <= 1; k++)
            {
                var fp = f - outward * 9f + along * (k * 3.5f); fp.y = Height(fp.x, fp.z);
                Post(steel.V, steel.UV, steel.T, null, fp, fp + Vector3.up * 9f, 0.07f, Color.white);
                var top = fp + Vector3.up * 8.9f;
                var fl = flags[(k + 1) * 2 % flags.Length];
                // rectangular flag streaming downwind
                var w = WindDir * 1.8f; var dn = Vector3.down * 1.1f;
                fl.Tri2(top, top + w, top + dn);
                fl.Tri2(top + w, top + w + dn, top + dn);
                // bunting from the lodge ridge to each pole
                Bunting(flags, f + Vector3.up * 7.5f + along * (k * 4f), top - Vector3.up * 0.3f, 0.9f);
            }
            nHuts++;
        }

        // ---- prayer-flag bunting over the summit plateau: across the road and along the verge
        int nBunt = 0;
        for (float d = CpCol - 1400f; d < CpCol + 1800f; d += 90f)
        {
            int i = route.IndexAt(d);
            Foot(route, i, edgeOff + 1.3f, out var fa, out _);
            Foot(route, i, -(edgeOff + 1.3f), out var fb, out _);
            fa.y = Mathf.Max(fa.y, RoadY(route, i, edgeOff)); fb.y = Mathf.Max(fb.y, RoadY(route, i, -edgeOff));
            float top = 5.6f;
            Post(trunk.V, trunk.UV, trunk.T, null, fa - Vector3.up * 0.4f, fa + Vector3.up * top, 0.07f, Color.white);
            Post(trunk.V, trunk.UV, trunk.T, null, fb - Vector3.up * 0.4f, fb + Vector3.up * top, 0.07f, Color.white);
            if (nBunt % 2 == 0)
                Bunting(flags, fa + Vector3.up * (top - 0.1f), fb + Vector3.up * (top - 0.1f), 0.45f);
            // verge strings running along the road to the next station
            int j = route.IndexAt(d + 30f);
            Foot(route, j, edgeOff + 1.3f, out var fc, out _);
            fc.y = Mathf.Max(fc.y, RoadY(route, j, edgeOff));
            Post(trunk.V, trunk.UV, trunk.T, null, fc - Vector3.up * 0.4f, fc + Vector3.up * 4.2f, 0.06f, Color.white);
            Bunting(flags, fa + Vector3.up * (top - 0.4f), fc + Vector3.up * 4.1f, 0.9f);
            nBunt++;
        }
        foreach (var b in all) b.Flush(group);

        BuildJaggedPeaks(root, route, rng);

        Debug.Log($"[taka] alpine dressing: {nTrees} conifers, {nCrags} crags, {nTufts} moon tufts, {nRail} guardrail bays, " +
                  $"{nSigns} sign posts, {nHuts} huts/lodge, {nBunt} bunting stations.");
    }

    // ------------------------------------------------------------------ primitives

    private static void RailStrip(Bucket b, Vector3 a, Vector3 c, float h, Vector3 outward)
    {
        var up = Vector3.up * (h * 0.5f);
        var o = outward.normalized * 0.04f;
        // front and back faces on separate verts, plus top
        b.Tri(a - up, c - up, a + up, Vector2.zero, Vector2.right, Vector2.up);
        b.Tri(a + up, c - up, c + up, Vector2.up, Vector2.right, Vector2.one);
        b.Tri(a - up + o, a + up + o, c - up + o, Vector2.zero, Vector2.up, Vector2.right);
        b.Tri(a + up + o, c + up + o, c - up + o, Vector2.up, Vector2.one, Vector2.right);
        b.Tri(a + up, c + up, a + up + o, Vector2.zero, Vector2.right, Vector2.up);
        b.Tri(a + up + o, c + up, c + up + o, Vector2.up, Vector2.right, Vector2.one);
    }

    /// <summary>Atlas cedar: short trunk and 3-4 wide, flat, slightly drooping tiers.</summary>
    private static void Cedar(Bucket leaf, Bucket trunk, Vector3 foot, float h, System.Random rng)
    {
        Post(trunk.V, trunk.UV, trunk.T, null, foot - Vector3.up * 0.4f, foot + Vector3.up * h * 0.8f, h * 0.03f, Color.white);
        const int Sides = 7;
        int tiers = 3 + (rng.NextDouble() < 0.5 ? 1 : 0);
        for (int t = 0; t < tiers; t++)
        {
            float y = h * (0.34f + 0.15f * t);
            float r = h * (0.42f - 0.08f * t) * (0.85f + 0.3f * (float)rng.NextDouble());
            var ctr = foot + Vector3.up * y + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * h * 0.1f;
            var top = ctr + Vector3.up * h * 0.13f; var bot = ctr - Vector3.up * h * 0.06f;
            float yaw = (float)rng.NextDouble() * 6.28f;
            for (int s = 0; s < Sides; s++)
            {
                float a0 = yaw + s * Mathf.PI * 2f / Sides, a1 = yaw + (s + 1) * Mathf.PI * 2f / Sides;
                var p0 = ctr + new Vector3(Mathf.Cos(a0) * r, -h * 0.05f, Mathf.Sin(a0) * r);
                var p1 = ctr + new Vector3(Mathf.Cos(a1) * r, -h * 0.05f, Mathf.Sin(a1) * r);
                leaf.TriAway(p0, top, p1, bot, 0.5f);
                leaf.TriAway(p0, p1, bot, top, 0.5f);
            }
        }
    }

    /// <summary>Tiered conifer: trunk plus 4 stacked 7-sided cones, flat-shaded.</summary>
    private static void Conifer(Bucket leaf, Bucket trunk, Vector3 foot, float h, System.Random rng)
    {
        Post(trunk.V, trunk.UV, trunk.T, null, foot - Vector3.up * 0.4f, foot + Vector3.up * h * 0.35f, h * 0.022f, Color.white);
        const int Sides = 7;
        float r0 = h * 0.27f;
        float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
        for (int t = 0; t < 4; t++)
        {
            float baseY = h * (0.16f + 0.19f * t);
            float topY = baseY + h * (0.40f - 0.03f * t);
            float r = r0 * (1f - 0.21f * t);
            var apex = foot + Vector3.up * topY;
            var ring = new Vector3[Sides];
            for (int s = 0; s < Sides; s++)
            {
                float a = yaw + s * Mathf.PI * 2f / Sides + t * 0.4f;
                float jr = r * (0.85f + 0.3f * (float)rng.NextDouble());
                ring[s] = foot + new Vector3(Mathf.Cos(a) * jr, baseY - 0.12f * h * (float)rng.NextDouble() * 0.5f, Mathf.Sin(a) * jr);
            }
            var bc = foot + Vector3.up * (baseY + h * 0.03f);
            for (int s = 0; s < Sides; s++)
            {
                var a = ring[s]; var b = ring[(s + 1) % Sides];
                var axis = foot + Vector3.up * ((a.y + apex.y) * 0.5f - foot.y);
                leaf.TriAway(a, apex, b, axis, 0.5f);
                leaf.TriAway(a, b, bc, apex, 0.5f);
            }
        }
    }

    /// <summary>
    /// Faceted rock: a jittered lat-long ellipsoid emitted on unshared vertices so every facet is
    /// flat-shaded (sharp faceting under the cel ramp, not a smoothed grey balloon). Facets whose
    /// normal points up go to the snow bucket when above the local snow line.
    /// </summary>
    private static void Rock(Bucket rock, Bucket snow, Vector3 c, Vector3 radii, float yawDeg,
                             int lon, int lat, System.Random rng, float snowBias, float uvScale = 0.22f)
    {
        var rot = Quaternion.Euler(0f, yawDeg, 0f);
        var pts = new Vector3[lat + 1, lon];
        for (int y = 0; y <= lat; y++)
        {
            float v = (float)y / lat;
            float phi = Mathf.Lerp(-0.35f, 0.5f, v) * Mathf.PI;   // flat-ish bottom, domed top
            for (int x = 0; x < lon; x++)
            {
                float th = (x + (y % 2) * 0.5f) / lon * Mathf.PI * 2f;
                float j = 0.78f + 0.4f * (float)rng.NextDouble();
                if (y == lat) j = 0.25f + 0.35f * (float)rng.NextDouble();
                var local = new Vector3(Mathf.Cos(phi) * Mathf.Cos(th) * radii.x * j,
                                        (Mathf.Sin(phi) + 0.35f) * radii.y * (0.9f + 0.2f * (float)rng.NextDouble()),
                                        Mathf.Cos(phi) * Mathf.Sin(th) * radii.z * j);
                pts[y, x] = c + rot * local;
            }
        }
        float snowLine = SnowLineAt(c.x, c.z) - snowBias;
        for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int xn = (x + 1) % lon;
                var a = pts[y, x]; var b = pts[y, xn]; var d = pts[y + 1, x]; var e = pts[y + 1, xn];
                Face(rock, snow, a, d, b, c, snowLine, uvScale);
                Face(rock, snow, b, d, e, c, snowLine, uvScale);
            }
        // cap
        var top = Vector3.zero;
        for (int x = 0; x < lon; x++) top += pts[lat, x];
        top /= lon; top += Vector3.up * radii.y * 0.08f;
        for (int x = 0; x < lon; x++)
            Face(rock, snow, pts[lat, x], top, pts[lat, (x + 1) % lon], c, snowLine, uvScale);
    }

    private static void Face(Bucket rock, Bucket snow, Vector3 a, Vector3 b, Vector3 c, Vector3 centre,
                             float snowLine, float s)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-8f) return;
        var mid = (a + b + c) / 3f;
        var from = new Vector3(centre.x, Mathf.Min(centre.y, mid.y - 0.01f), centre.z);
        if (Vector3.Dot(n, mid - from) < 0f) n = -n;
        n.Normalize();
        float cy = (a.y + b.y + c.y) / 3f;
        bool isSnow = cy > snowLine && n.y > 0.42f;
        (isSnow ? snow : rock).TriAway(a, b, c, from, s);
    }

    /// <summary>
    /// A ring of jagged snow-capped peaks 1.3-3 km off the route: the alpine skyline the rolling
    /// massif alone never gives. Each peak is one big faceted rock plus two subsidiary spires,
    /// snow on the up-facing facets above a lowered snow line.
    /// </summary>
    private static void BuildJaggedPeaks(Transform root, TakaRoute route, System.Random rng)
    {
        var group = new GameObject("Taka Jagged Peaks").transform;
        group.SetParent(root, false);
        var rock = new Bucket("PeakRock", CragDarkMaterial());
        var snow = new Bucket("PeakSnow", SnowCapMaterial());
        var placed = new List<Vector3>();
        int n = 0;
        for (float d = 0f; d < route.Length; d += 650f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i]; var side = route.SideFlat(i);
            for (int s = -1; s <= 1; s += 2)
            {
                float off = Mathf.Lerp(1500f, 3000f, (float)rng.NextDouble());
                var c = p + side * s * off;
                float rd = RoadDist(c.x, c.z);
                if (rd < 1100f) continue;
                bool clash = false;
                foreach (var q in placed) if ((q - c).sqrMagnitude < 900f * 900f) { clash = true; break; }
                if (clash) continue;
                placed.Add(c);
                c.y = Height(c.x, c.z) - 60f;
                float r = Mathf.Lerp(420f, 750f, (float)rng.NextDouble());
                float hgt = Mathf.Max(route.MaxY + Mathf.Lerp(250f, 700f, (float)rng.NextDouble()) - c.y, r * 1.1f) / 1.35f;
                Rock(rock, snow, c, new Vector3(r, hgt, r * 0.8f), (float)rng.NextDouble() * 360f, 11, 7, rng,
                     snowBias: 250f, uvScale: 0.01f);
                for (int k = 0; k < 2; k++)
                {
                    var sc = c + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * r * 1.6f;
                    sc.y = c.y;
                    Rock(rock, snow, sc, new Vector3(r * 0.5f, hgt * 0.75f, r * 0.45f), (float)rng.NextDouble() * 360f,
                         8, 6, rng, snowBias: 250f, uvScale: 0.01f);
                }
                n++;
            }
        }
        rock.Flush(group); snow.Flush(group);
        // Distant peaks must never cast onto the ride corridor as black bars.
        foreach (var mr in group.GetComponentsInChildren<MeshRenderer>())
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Debug.Log($"[taka] {n} jagged peaks.");
    }

    /// <summary>Stone-footed timber hut with a red gabled roof and a chimney.</summary>
    private static void Hut(Bucket wall, Bucket roof, Bucket stone, Vector3 f, Vector3 outward,
                            float W, float D, float H)
    {
        outward.y = 0f; outward.Normalize();
        var along = Vector3.Cross(Vector3.up, outward).normalized;
        Box(stone.V, stone.UV, stone.T, null, f - Vector3.up * 1.2f, along * (W * 0.56f), outward * (D * 0.56f), 1.9f, Color.white);
        Box(wall.V, wall.UV, wall.T, null, f + Vector3.up * 0.7f, along * (W * 0.5f), outward * (D * 0.5f), H, Color.white);
        float eave = f.y + 0.7f + H, ridge = eave + D * 0.45f;
        var a0 = f + along * (W * 0.62f) + outward * (D * 0.64f); a0.y = eave - 0.25f;
        var a1 = f - along * (W * 0.62f) + outward * (D * 0.64f); a1.y = eave - 0.25f;
        var b0 = f + along * (W * 0.62f) - outward * (D * 0.64f); b0.y = eave - 0.25f;
        var b1 = f - along * (W * 0.62f) - outward * (D * 0.64f); b1.y = eave - 0.25f;
        var r0 = f + along * (W * 0.62f); r0.y = ridge;
        var r1 = f - along * (W * 0.62f); r1.y = ridge;
        Band(roof.V, roof.UV, roof.T, a0, a1, r0, r1, 0f);
        Band(roof.V, roof.UV, roof.T, b0, b1, r0, r1, 0f);
        // gables
        var g0 = f + along * (W * 0.5f); var g1 = f - along * (W * 0.5f);
        var ge0 = g0 + outward * (D * 0.5f); ge0.y = eave; var gf0 = g0 - outward * (D * 0.5f); gf0.y = eave;
        var ge1 = g1 + outward * (D * 0.5f); ge1.y = eave; var gf1 = g1 - outward * (D * 0.5f); gf1.y = eave;
        var gr0 = g0; gr0.y = ridge - 0.1f; var gr1 = g1; gr1.y = ridge - 0.1f;
        wall.Tri2(ge0, gf0, gr0);
        wall.Tri2(ge1, gf1, gr1);
        var chim = f + along * (W * 0.28f); chim.y = eave + 0.2f;
        Box(stone.V, stone.UV, stone.T, null, chim, along * 0.3f, outward * 0.3f, D * 0.45f + 0.9f, Color.white);
    }

    /// <summary>Prayer-flag bunting: a sagging line of small triangular flags in five colours.</summary>
    private static void Bunting(Bucket[] flags, Vector3 a, Vector3 b, float sag)
    {
        float len = Vector3.Distance(a, b);
        int n = Mathf.Max(2, Mathf.FloorToInt(len / 0.55f));
        var dir = (b - a) / n;
        for (int k = 0; k < n; k++)
        {
            float t0 = (float)k / n, t1 = (k + 0.8f) / n;
            var p0 = Vector3.Lerp(a, b, t0) - Vector3.up * sag * 4f * t0 * (1f - t0);
            var p1 = Vector3.Lerp(a, b, t1) - Vector3.up * sag * 4f * t1 * (1f - t1);
            var tip = (p0 + p1) * 0.5f - Vector3.up * 0.42f + WindDir * 0.08f;
            // square prayer flags: two tris
            var q0 = p0 - Vector3.up * 0.40f + WindDir * 0.06f; var q1 = p1 - Vector3.up * 0.40f + WindDir * 0.06f;
            var fb = flags[k % flags.Length];
            if ((k & 1) == 0) { fb.Tri2(p0, p1, q0); fb.Tri2(p1, q1, q0); }
            else fb.Tri2(p0, p1, tip);
        }
        _ = dir;
    }
}

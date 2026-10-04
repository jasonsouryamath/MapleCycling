using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// FUJI RIDGE - PILGRIMAGE DRESSING (2026-09-26, REGION_REVIEW_TODO items 8-9).
///
/// Replaces the cone-placeholder cedar belt and the flat "brick slab" parapets with a lived-in
/// pilgrimage road: layered sugi/hinoki conifers in the lower half, torii gates over the road,
/// stone lanterns, station huts with noren and nobori banners, stone steps, ishigaki retaining
/// walls, volcanic scree with boulders and hardy shrubs above the treeline, and a Fuji-like
/// summit cone on the skyline.
///
/// EVERYTHING here shares ONE material: CelLit ignores vertex colour, so colour is carried in a
/// small ramp PALETTE texture (one row per material family, dark -> light along U) and each
/// vertex's UV picks its row and brightness. That keeps the whole dressing to one draw call per
/// route kilometre chunk. The road corridor (RoadHalfWidth + ShoulderWidth = 3.2 m) is kept clear.
/// </summary>
public static partial class FujiRidgeEnvironment
{
    // ---- palette rows ------------------------------------------------------------------------
    private const int PCedar = 0, PFir = 1, PBark = 2, PVermilion = 3, PBlack = 4, PGranite = 5,
                      PMoss = 6, PBoulder = 7, PTimber = 8, PRoof = 9, PIndigo = 10, PCream = 11,
                      PShrub = 12, PAutumn = 13, PSnow = 14, PAsh = 15, PRed = 16, PWhite = 17;
    private const int PaletteRows = 27;
    private const int POchre = 18, PTerracotta = 19, PStucco = 20, PShutter = 21, PCypress = 22, PVine = 23;
    // copilot F6 (2026-09-26): upper-slope contrast rows. PROVISIONAL colours.
    private const int PAshWarm = 24, PLava = 25, PScoria = 26;
    private const float CorridorClearM = RoadHalfWidth + ShoulderWidth; // 3.2 m

    private static readonly Color[,] PaletteRamp =
    {
        { new Color(0.05f, 0.10f, 0.07f), new Color(0.28f, 0.44f, 0.25f) }, // cedar
        { new Color(0.05f, 0.10f, 0.11f), new Color(0.26f, 0.42f, 0.37f) }, // fir / hinoki
        { new Color(0.14f, 0.08f, 0.05f), new Color(0.45f, 0.29f, 0.19f) }, // bark
        { new Color(0.42f, 0.07f, 0.04f), new Color(0.92f, 0.27f, 0.12f) }, // vermilion
        { new Color(0.03f, 0.03f, 0.035f), new Color(0.17f, 0.16f, 0.16f) }, // black lacquer
        { new Color(0.28f, 0.28f, 0.27f), new Color(0.74f, 0.73f, 0.69f) }, // granite
        { new Color(0.18f, 0.22f, 0.16f), new Color(0.52f, 0.57f, 0.43f) }, // mossy stone
        { new Color(0.09f, 0.08f, 0.08f), new Color(0.42f, 0.36f, 0.32f) }, // volcanic boulder
        { new Color(0.20f, 0.13f, 0.08f), new Color(0.60f, 0.45f, 0.30f) }, // timber
        { new Color(0.10f, 0.11f, 0.13f), new Color(0.36f, 0.38f, 0.43f) }, // roof
        { new Color(0.04f, 0.07f, 0.19f), new Color(0.16f, 0.25f, 0.50f) }, // indigo noren
        { new Color(0.55f, 0.50f, 0.40f), new Color(0.99f, 0.95f, 0.85f) }, // cream paper
        { new Color(0.10f, 0.15f, 0.06f), new Color(0.46f, 0.53f, 0.23f) }, // shrub olive
        { new Color(0.24f, 0.07f, 0.04f), new Color(0.72f, 0.31f, 0.12f) }, // autumn shrub
        { new Color(0.70f, 0.76f, 0.86f), new Color(1.00f, 1.00f, 1.00f) }, // snow
        { new Color(0.16f, 0.14f, 0.13f), new Color(0.50f, 0.45f, 0.41f) }, // ash scree
        { new Color(0.52f, 0.05f, 0.05f), new Color(0.96f, 0.22f, 0.17f) }, // red banner
        { new Color(0.72f, 0.72f, 0.70f), new Color(0.99f, 0.98f, 0.95f) }, // white banner
        { new Color(0.45f, 0.30f, 0.12f), new Color(0.98f, 0.78f, 0.42f) }, // ochre stucco
        { new Color(0.38f, 0.14f, 0.07f), new Color(0.88f, 0.44f, 0.26f) }, // terracotta
        { new Color(0.52f, 0.46f, 0.38f), new Color(0.99f, 0.92f, 0.80f) }, // cream / rose stucco
        { new Color(0.06f, 0.18f, 0.12f), new Color(0.22f, 0.50f, 0.34f) }, // shutter green
        { new Color(0.03f, 0.08f, 0.05f), new Color(0.18f, 0.32f, 0.16f) }, // cypress
        { new Color(0.10f, 0.18f, 0.05f), new Color(0.50f, 0.62f, 0.22f) }, // vine
        { new Color(0.42f, 0.34f, 0.25f), new Color(0.93f, 0.81f, 0.63f) }, // warm pale ash / pumice (F6)
        { new Color(0.015f, 0.013f, 0.013f), new Color(0.16f, 0.11f, 0.095f) }, // black basalt lava (F6)
        { new Color(0.22f, 0.07f, 0.04f), new Color(0.66f, 0.27f, 0.15f) }, // red scoria (F6)
    };

    /// <summary>A growing mesh plus its palette UVs; one per route-kilometre chunk.</summary>
    private sealed class PMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();

        public int Vert(Vector3 p, int row, float bright)
        {
            V.Add(p);
            UV.Add(new Vector2(Mathf.Clamp(bright, 0.02f, 0.98f), (row + 0.5f) / PaletteRows));
            return V.Count - 1;
        }

        /// <summary>Triangle wound so its normal faces <paramref name="outward"/>.</summary>
        public void Tri(int a, int b, int c, Vector3 outward)
        {
            var n = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(n, outward) < 0f) { int t = b; b = c; c = t; }
            T.Add(a); T.Add(b); T.Add(c);
        }

        /// <summary>Oriented box, per-face vertices (hard edges). Half-extent axis vectors.</summary>
        public void OBox(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, int row, float bright,
                         float topBright = -1f)
        {
            var dirs = new[] { ax, -ax, ay, -ay, az, -az };
            for (int f = 0; f < 6; f++)
            {
                var n = dirs[f];
                Vector3 u, w;
                if (f < 2) { u = ay; w = az; } else if (f < 4) { u = ax; w = az; } else { u = ax; w = ay; }
                float br = f == 2 && topBright >= 0f ? topBright : (f == 3 ? bright * 0.6f : bright);
                var fc = c + n;
                int i0 = Vert(fc - u - w, row, br), i1 = Vert(fc + u - w, row, br);
                int i2 = Vert(fc + u + w, row, br), i3 = Vert(fc - u + w, row, br);
                Tri(i0, i1, i2, n); Tri(i0, i2, i3, n);
            }
        }

        /// <summary>Upright n-gon frustum (no caps needed where it is buried/covered).</summary>
        public void Prism(Vector3 baseC, float r0, float r1, float h, int sides, int row,
                          float b0, float b1, bool capTop = true)
        {
            int start = V.Count;
            for (int k = 0; k <= sides; k++)
            {
                float a = k * Mathf.PI * 2f / sides;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vert(baseC + d * r0, row, b0);
                Vert(baseC + d * r1 + Vector3.up * h, row, b1);
            }
            for (int k = 0; k < sides; k++)
            {
                int a = start + k * 2, c = a + 2;
                var o = (V[a] + V[c]) * 0.5f - baseC; o.y = 0f;
                Tri(a, a + 1, c, o); Tri(c, a + 1, c + 1, o);
            }
            if (!capTop) return;
            int top = Vert(baseC + Vector3.up * h, row, b1);
            for (int k = 0; k < sides; k++)
                Tri(top, start + k * 2 + 1, start + (k + 1) * 2 + 1, Vector3.up);
        }

        /// <summary>A lumpy rock / shrub blob: jittered lat-long ellipsoid.</summary>
        public void Blob(Vector3 c, float r, float squash, int row, float bright,
                         System.Random rnd, float lumpiness = 0.28f)
        {
            const int Lon = 7, Lat = 4;
            int start = V.Count;
            var jit = new float[Lon];
            for (int k = 0; k < Lon; k++) jit[k] = 1f + ((float)rnd.NextDouble() - 0.5f) * 2f * lumpiness;
            for (int la = 0; la <= Lat; la++)
            {
                float phi = Mathf.Lerp(-0.35f * Mathf.PI, 0.5f * Mathf.PI, la / (float)Lat);
                for (int k = 0; k < Lon; k++)
                {
                    float th = k * Mathf.PI * 2f / Lon;
                    float rr = r * jit[k] * (la == Lat ? 0f : 1f)
                             * (1f + ((float)rnd.NextDouble() - 0.5f) * lumpiness);
                    var p = c + new Vector3(Mathf.Cos(th) * Mathf.Cos(phi) * rr,
                                            Mathf.Sin(phi) * r * squash,
                                            Mathf.Sin(th) * Mathf.Cos(phi) * rr);
                    Vert(p, row, bright * Mathf.Lerp(0.55f, 1.1f, la / (float)Lat));
                }
            }
            for (int la = 0; la < Lat; la++)
            for (int k = 0; k < Lon; k++)
            {
                int a = start + la * Lon + k, b = start + la * Lon + (k + 1) % Lon;
                int a2 = a + Lon, b2 = b + Lon;
                var o = (V[a] + V[b2]) * 0.5f - c;
                Tri(a, b, b2, o); Tri(a, b2, a2, o);
            }
        }
    }

    private static Material _pilgrimMat;

    private static Material PilgrimMaterial()
    {
        string texPath = $"{TextureDir}/Fuji_PilgrimPalette.asset";
        var tex = new Texture2D(32, PaletteRows, TextureFormat.RGBA32, false, false)
        {
            name = "Fuji_PilgrimPalette",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        for (int y = 0; y < PaletteRows; y++)
        for (int x = 0; x < 32; x++)
            tex.SetPixel(x, y, Color.Lerp(PaletteRamp[y, 0], PaletteRamp[y, 1], x / 31f));
        tex.Apply(false, false);
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) != null) AssetDatabase.DeleteAsset(texPath);
        AssetDatabase.CreateAsset(tex, texPath);

        MaterialCache.Remove("Fuji_Pilgrim");
        var mat = CelMaterial("Fuji_Pilgrim", new Color(0.95f, 0.95f, 0.95f), gloss: 0.10f,
                              spec: 0.05f, rim: 0.10f, texture: tex, shade: GroundShade);
        mat.SetFloat("_Cull", 0f); // banners, noren and conifer tiers are seen from both sides
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---- entry point ---------------------------------------------------------------------------

    private static void BuildPilgrimage(Transform root, FujiRoute route)
    {
        var group = new GameObject("Fuji Pilgrimage").transform;
        group.SetParent(root, false);
        _pilgrimMat = PilgrimMaterial();
        ResetDetailTiles(); // fuji2: small props -> shadowless, distance-culled detail tiles

        var chunks = new Dictionary<int, PMesh>();
        PMesh At(int i)
        {
            int key = Mathf.FloorToInt(route.Distance[i] / 1000f);
            if (!chunks.TryGetValue(key, out var m)) chunks[key] = m = new PMesh();
            return m;
        }

        var rnd = new System.Random(7771);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        int trees = 0, rocks = 0, shrubs = 0, lanterns = 0, torii = 0, huts = 0, walls = 0;

        // Which side is uphill at station i: +1 / -1.
        int Uphill(int i)
        {
            Foot(route, i, 9f, out var a, out _);
            Foot(route, i, -9f, out var b, out _);
            return a.y >= b.y ? 1 : -1;
        }

        // ---- 1. conifer forest (lower half) + krummholz and scree (upper) -------------------
        for (int i = 2; i < route.Count - 2; i += 2)
        {
            float y = route.Position[i].y;
            float dens = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(TreeLineY - TreeLineFadeM * 2.4f, TreeLineY + 40f, y));
            float high = Mathf.InverseLerp(TreeLineY - 60f, TreeLineY + 200f, y); // scree weight
            var m = At(i);
            float dd = route.Distance[i];
            if (dd > TownFromM - 40f && dd < TownToM + 40f) continue;           // the hill town
            if (dd < VineToM) dens *= 0.25f;                                   // vineyards + cypress

            // Forest: several attempts per 8 m, both sides, packed near the road.
            int tries = Mathf.RoundToInt(14f * dens);
            for (int k = 0; k < tries; k++)
            {
                if (rnd.NextDouble() > dens) continue;
                int sign = rnd.NextDouble() < 0.5 ? -1 : 1;
                float h = R(9f, 24f) * Mathf.Lerp(0.6f, 1f, dens);
                float rad = h * 0.19f;
                float u = (float)rnd.NextDouble();
                float off = sign * (CorridorClearM + 0.6f + rad + u * u * 42f);
                Foot(route, i, off, out var f, out _);
                f += route.Tangent[i] * R(-4f, 4f);
                Conifer(m, f, h, rnd, rnd.NextDouble() < 0.65 ? PCedar : PFir, Mathf.Abs(off) < 30f ? 9 : 6);
                trees++;
            }
            // Understory shrubs in the forest, low mounds right up to the verge.
            if (dens > 0.2f && rnd.NextDouble() < 0.9 * dens)
            {
                int sign = rnd.NextDouble() < 0.5 ? -1 : 1;
                Foot(route, i, sign * R(CorridorClearM + 0.5f, 14f), out var f, out _);
                Shrub(DetailAt(f), f, R(0.5f, 1.2f), rnd, rnd.NextDouble() < 0.8 ? PShrub : PAutumn);
                shrubs++;
            }

            // Krummholz: short wind-bent conifers in the transition band.
            if (y > TreeLineY - 80f && y < TreeLineY + 480f && rnd.NextDouble() < 0.9)
            {
                int sign = rnd.NextDouble() < 0.5 ? -1 : 1;
                float h = R(1.6f, 3.6f);
                Foot(route, i, sign * (CorridorClearM + 1.2f + R(0f, 30f)), out var f, out _);
                Conifer(m, f, h, rnd, PFir, 5);
                trees++;
            }

            // Scree: volcanic boulders + small clasts + hardy shrubs, heavier with height.
            if (high > 0.01f)
            {
                int n = Mathf.RoundToInt(Mathf.Lerp(3f, 14f, high));
                for (int k = 0; k < n; k++)
                {
                    int sign = rnd.NextDouble() < 0.5 ? -1 : 1;
                    float u = (float)rnd.NextDouble();
                    bool big = rnd.NextDouble() < 0.35;
                    float r = big ? R(0.8f, 2.6f) : R(0.15f, 0.55f);
                    float off = sign * (CorridorClearM + 0.3f + r + u * u * (big ? 40f : 14f));
                    Foot(route, i, off, out var f, out _);
                    f += route.Tangent[i] * R(-4f, 4f);
                    (big ? m : DetailAt(f)).Blob(f - Vector3.up * r * 0.25f, r, R(0.5f, 0.8f),
                           rnd.NextDouble() < 0.7 ? PBoulder : PAsh, R(0.45f, 0.9f), rnd);
                    rocks++;
                }
                for (int q = 0; q < 2; q++) if (rnd.NextDouble() < 0.9 * Mathf.Clamp01(1.4f - high * 0.7f))
                {
                    int sign = rnd.NextDouble() < 0.5 ? -1 : 1;
                    Foot(route, i, sign * R(CorridorClearM + 0.6f, 22f), out var f, out _);
                    Shrub(DetailAt(f), f, R(0.35f, 0.9f), rnd, rnd.NextDouble() < 0.55 ? PShrub : PAutumn);
                    shrubs++;
                }
            }
        }

        // ---- 2. torii gates over the road ---------------------------------------------------
        for (float d = 40f; d < route.Length - 20f; d += 1100f)
        {
            int i = route.IndexAt(d);
            Torii(At(i), route, i);
            torii++;
        }
        { int i = route.IndexAt(route.Length - 60f); Torii(At(i), route, i); torii++; }

        // ---- 3. stone lanterns in pairs (dense near gates, every 70 m elsewhere) -------------
        for (float d = 20f; d < route.Length - 20f; d += 70f)
        {
            if (d > TownFromM - 20f && d < TownToM + 20f) continue;
            int i = route.IndexAt(d);
            for (int s = -1; s <= 1; s += 2)
            {
                Foot(route, i, s * (CorridorClearM + 0.9f), out var f, out _);
                Lantern(DetailAt(f), f, route.Tangent[i]);
                lanterns++;
            }
        }

        // ---- 4. station huts with noren, banners and stone steps ----------------------------
        for (float d = 1950f; d < route.Length - 200f; d += 1300f)
        {
            int i = route.IndexAt(d);
            int side = Uphill(i);
            StationHut(At(i), route, i, side, rnd);
            huts++;
        }

        // ---- 5. ishigaki retaining walls on the uphill side ---------------------------------
        for (float d = 300f; d < route.Length - 100f; d += 420f)
        {
            int i0 = route.IndexAt(d);
            int side = Uphill(i0);
            int runSt = Mathf.RoundToInt(R(10f, 18f)); // stations (4 m each) = 40-72 m
            for (int i = i0; i < Mathf.Min(i0 + runSt, route.Count - 2); i++)
                walls += RetainingWallSpan(At(i), route, i, side, rnd);
        }

        // ---- 6. hill town, vineyards, cypress rows, rifugio (FujiRidge.Town.cs) ------------
        BuildTownDressing(route, At, rnd);

        // ---- 7. fuji2: far-field volcanic dressing (scree bands, lava fields, snow patches) ---
        BuildVolcanicFarField(route, new System.Random(9901));

        // ---- flush ---------------------------------------------------------------------------
        int tris = 0;
        foreach (var kv in chunks)
        {
            var m = kv.Value;
            if (m.T.Count == 0) continue;
            var mesh = Finish($"Fuji_Pilgrim_{kv.Key}", m.V.ToArray(), m.UV.ToArray(), m.T);
            AddMesh(group, $"Pilgrimage km {kv.Key}", mesh, _pilgrimMat, collider: false);
            tris += m.T.Count / 3;
        }

        var (detailTris, farTris) = FlushDetailTiles(group);
        tris += detailTris + farTris;
        tris += FlushHillTownKit(group); // E1: hero GLB townhouses, LOD0-2 per 120 m chunk

        BuildSummitCone(group, route);
        BuildTownLife(group, route);

        Debug.Log($"[fuji] pilgrimage: {trees} conifers, {rocks} rocks, {shrubs} shrubs, " +
                  $"{lanterns} lanterns, {torii} torii, {huts} huts, {walls} wall stones; " +
                  $"{tris} tris in {chunks.Count} chunks.");
    }

    // ---- conifer: stacked, lobed, drooping tiers (sugi / hinoki), not a cone ------------------

    private static void Conifer(PMesh m, Vector3 foot, float h, System.Random rnd, int row, int tiers)
    {
        float Rn() => (float)rnd.NextDouble();
        float yaw = Rn() * 6.283f;
        float trunkR = h * 0.018f + 0.08f;
        m.Prism(foot - Vector3.up * 0.3f, trunkR * 1.3f, trunkR * 0.7f, h * 0.55f, 6, PBark, 0.35f, 0.6f, false);

        float crownBase = h * Mathf.Lerp(0.16f, 0.30f, Rn());
        float maxR = h * Mathf.Lerp(0.15f, 0.20f, Rn());
        const int Spokes = 9;
        float bright0 = Mathf.Lerp(0.55f, 0.85f, Rn());
        for (int t = 0; t < tiers; t++)
        {
            float s = t / (float)(tiers - 1);
            float y = Mathf.Lerp(crownBase, h * 0.92f, s);
            float rad = maxR * Mathf.Pow(1f - s, 0.8f) + 0.35f;
            float droop = rad * 0.45f + 0.2f;
            float thick = rad * 0.35f + 0.25f;
            var centreTop = foot + Vector3.up * (y + thick);
            var centreBot = foot + Vector3.up * (y - droop * 0.25f);
            float ty = yaw + t * 0.73f;

            var outer = new Vector3[Spokes];
            for (int k = 0; k < Spokes; k++)
            {
                float a = ty + k * Mathf.PI * 2f / Spokes + (Rn() - 0.5f) * 0.3f;
                float rr = rad * (k % 2 == 0 ? 1f : 0.62f) * Mathf.Lerp(0.82f, 1.15f, Rn());
                outer[k] = foot + new Vector3(Mathf.Cos(a) * rr, y - droop * (k % 2 == 0 ? 1f : 0.6f), Mathf.Sin(a) * rr);
            }
            float br = bright0 * Mathf.Lerp(0.75f, 1.1f, s);
            int ct = m.Vert(centreTop, row, br * 1.05f);
            var top = new int[Spokes];
            for (int k = 0; k < Spokes; k++) top[k] = m.Vert(outer[k], row, br * (k % 2 == 0 ? 0.95f : 0.8f));
            for (int k = 0; k < Spokes; k++)
            {
                var o = (outer[k] + outer[(k + 1) % Spokes]) * 0.5f - foot; o.y = rad;
                m.Tri(ct, top[k], top[(k + 1) % Spokes], o);
            }
            int cb = m.Vert(centreBot, row, br * 0.25f);
            var bot = new int[Spokes];
            for (int k = 0; k < Spokes; k++) bot[k] = m.Vert(outer[k], row, br * 0.4f);
            for (int k = 0; k < Spokes; k++)
                m.Tri(cb, bot[k], bot[(k + 1) % Spokes], Vector3.down);
        }
        // Leader spike.
        m.Prism(foot + Vector3.up * h * 0.9f, 0.3f, 0.02f, h * 0.14f, 5, row, bright0, bright0 * 1.1f, false);
    }

    private static void Shrub(PMesh m, Vector3 foot, float r, System.Random rnd, int row)
    {
        int n = 2 + rnd.Next(3);
        for (int k = 0; k < n; k++)
        {
            var o = new Vector3((float)rnd.NextDouble() - 0.5f, 0f, (float)rnd.NextDouble() - 0.5f) * r * 1.6f;
            float rr = r * Mathf.Lerp(0.6f, 1f, (float)rnd.NextDouble());
            m.Blob(foot + o, rr, 0.75f, row, Mathf.Lerp(0.55f, 0.95f, (float)rnd.NextDouble()), rnd, 0.35f);
        }
    }

    // ---- torii: vermilion, spanning the road, pillars outside the corridor -----------------

    private static void Torii(PMesh m, FujiRoute route, int i)
    {
        var side = route.SideFlat(i);
        var fwd = Vector3.Cross(side, Vector3.up).normalized;
        const float Half = CorridorClearM + 0.9f; // pillars at 4.1 m
        float roadY = route.Position[i].y;
        float baseY = roadY;
        var pl = new Vector3[2];
        for (int s = 0; s < 2; s++)
        {
            Foot(route, i, (s == 0 ? -1 : 1) * Half, out var f, out _);
            baseY = Mathf.Max(baseY, f.y);
            pl[s] = f;
        }
        const float H = 6.4f;
        float topY = baseY + H;
        for (int s = 0; s < 2; s++)
        {
            var p = pl[s];
            m.Prism(p - Vector3.up * 0.4f, 0.36f, 0.36f, 0.9f, 8, PBlack, 0.4f, 0.5f);          // kamebara
            m.Prism(p, 0.30f, 0.25f, topY - p.y, 8, PVermilion, 0.6f, 0.85f, false);           // hashira
        }
        var mid = (pl[0] + pl[1]) * 0.5f; mid.y = 0f;
        var c = new Vector3(mid.x, topY, mid.z);
        float span = Half + 1.4f;
        m.OBox(c - Vector3.up * 1.3f, side * (Half + 0.7f), Vector3.up * 0.2f, fwd * 0.14f, PVermilion, 0.75f); // nuki
        m.OBox(c - Vector3.up * 0.55f, side * 0.18f, Vector3.up * 0.55f, fwd * 0.12f, PBlack, 0.4f);          // gakuzuka
        m.OBox(c - Vector3.up * 0.55f, side * 0.45f, Vector3.up * 0.38f, fwd * 0.16f, PCream, 0.8f);          // plaque
        m.OBox(c + Vector3.up * 0.10f, side * span, Vector3.up * 0.18f, fwd * 0.26f, PVermilion, 0.8f);        // shimaki
        // Kasagi: black top beam with upturned ends (three segments).
        m.OBox(c + Vector3.up * 0.42f, side * (span - 1.2f), Vector3.up * 0.16f, fwd * 0.34f, PBlack, 0.35f, 0.5f);
        for (int s = -1; s <= 1; s += 2)
        {
            var dir = (side * s + Vector3.up * 0.16f).normalized;
            var cc = c + Vector3.up * 0.42f + side * s * (span - 0.6f) + Vector3.up * 0.10f;
            m.OBox(cc, dir * 0.75f, Vector3.Cross(fwd, dir).normalized * 0.16f * s, fwd * 0.34f, PBlack, 0.35f);
        }
    }

    // ---- toro stone lantern ----------------------------------------------------------------

    private static void Lantern(PMesh m, Vector3 f, Vector3 tangent)
    {
        var fwd = new Vector3(tangent.x, 0f, tangent.z).normalized;
        var right = Vector3.Cross(Vector3.up, fwd);
        float b = 0.55f;
        m.OBox(f + Vector3.up * 0.05f, right * 0.34f, Vector3.up * 0.15f, fwd * 0.34f, PMoss, b);          // kiso
        m.Prism(f + Vector3.up * 0.2f, 0.13f, 0.11f, 0.85f, 6, PGranite, 0.45f, 0.6f, false);             // sao
        m.OBox(f + Vector3.up * 1.12f, right * 0.30f, Vector3.up * 0.08f, fwd * 0.30f, PGranite, 0.6f);    // chudai
        m.OBox(f + Vector3.up * 1.40f, right * 0.21f, Vector3.up * 0.20f, fwd * 0.21f, PCream, 0.9f);      // hibukuro
        m.Prism(f + Vector3.up * 1.60f, 0.50f, 0.08f, 0.38f, 6, PGranite, 0.5f, 0.75f, false);            // kasa
        m.Prism(f + Vector3.up * 1.52f, 0.50f, 0.50f, 0.08f, 6, PGranite, 0.35f, 0.4f, false);
        m.Prism(f + Vector3.up * 1.95f, 0.10f, 0.02f, 0.2f, 5, PGranite, 0.6f, 0.7f, false);              // hoju
    }

    // ---- station hut (yamagoya) with noren, nobori, steps -----------------------------------

    private static void StationHut(PMesh m, FujiRoute route, int i, int side, System.Random rnd)
    {
        float Rn() => (float)rnd.NextDouble();
        var sflat = route.SideFlat(i) * side;
        var fwd = Vector3.Cross(sflat, Vector3.up).normalized;
        const float Off = 13f;
        Foot(route, i, side * Off, out var hf, out _);
        float roadY = route.Position[i].y;
        float floorY = Mathf.Max(hf.y, roadY) + 0.6f;
        var hc = new Vector3(hf.x, floorY, hf.z);

        const float W = 4.2f, D = 3.0f, Hw = 2.7f;
        // Stone plinth down to the ground, and a terrace to the road.
        float plinthH = floorY - Mathf.Min(hf.y, roadY) + 1.2f;
        m.OBox(hc - Vector3.up * plinthH * 0.5f, fwd * (W + 1.4f), Vector3.up * plinthH * 0.5f, sflat * (D + 1.2f), PGranite, 0.55f, 0.7f);
        // Timber walls.
        m.OBox(hc + Vector3.up * Hw * 0.5f, fwd * W, Vector3.up * Hw * 0.5f, sflat * D, PTimber, 0.7f);
        // Posts at the corners (darker).
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
            m.OBox(hc + fwd * a * W + sflat * b * D + Vector3.up * Hw * 0.5f, fwd * 0.14f, Vector3.up * Hw * 0.5f, sflat * 0.14f, PTimber, 0.35f);
        // Gable roof: two tilted slabs, ridge along fwd.
        float pitch = 0.55f;
        for (int s = -1; s <= 1; s += 2)
        {
            var slope = (sflat * s + Vector3.up * -pitch).normalized; // down-slope direction
            float halfRun = (D + 0.9f) / Mathf.Cos(Mathf.Atan(pitch)) * 0.5f;
            var cc = hc + Vector3.up * (Hw + (D + 0.9f) * pitch * 0.5f) + slope * halfRun;
            var normal = Vector3.Cross(fwd, slope).normalized;
            if (normal.y < 0f) normal = -normal;
            m.OBox(cc, fwd * (W + 0.8f), normal * 0.12f, slope * halfRun, PRoof, 0.55f, 0.75f);
        }
        m.OBox(hc + Vector3.up * (Hw + (D + 0.9f) * pitch + 0.05f), fwd * (W + 0.9f), Vector3.up * 0.12f, sflat * 0.2f, PBlack, 0.4f);
        // Road-facing front: noren doorway, window, sign board.
        var front = hc - sflat * (D + 0.02f);
        for (int k = 0; k < 3; k++)
        {
            var nc = front + fwd * ((k - 1) * 0.52f - 1.2f) + Vector3.up * 1.75f - sflat * 0.05f;
            m.OBox(nc, fwd * 0.24f, Vector3.up * 0.5f, sflat * 0.02f, PIndigo, 0.75f);
            m.OBox(nc + Vector3.up * 0.1f, fwd * 0.08f, Vector3.up * 0.08f, sflat * 0.03f, PCream, 0.95f); // mon
        }
        m.OBox(front + fwd * 1.6f + Vector3.up * 1.55f - sflat * 0.03f, fwd * 0.9f, Vector3.up * 0.45f, sflat * 0.03f, PCream, 0.85f);
        m.OBox(front + Vector3.up * (Hw + 0.25f) - sflat * 0.15f, fwd * 1.6f, Vector3.up * 0.28f, sflat * 0.05f, PTimber, 0.95f);
        // Benches out front.
        for (int k = -1; k <= 1; k += 2)
            m.OBox(front - sflat * 0.9f + fwd * k * 2.2f + Vector3.up * 0.45f, fwd * 0.9f, Vector3.up * 0.06f, sflat * 0.25f, PRed, 0.7f);
        // Stone steps from the road edge up to the terrace.
        Foot(route, i, side * (CorridorClearM + 0.4f), out var s0, out _);
        float stepY0 = Mathf.Max(s0.y, roadY);
        int nSteps = Mathf.Clamp(Mathf.CeilToInt((floorY - stepY0) / 0.22f), 2, 20);
        float run = (Off - D - 1.2f - CorridorClearM - 0.4f);
        for (int k = 0; k < nSteps; k++)
        {
            float t = (k + 0.5f) / nSteps;
            var p = s0 + sflat * (run * t);
            float top = Mathf.Lerp(stepY0, floorY, (k + 1f) / nSteps);
            float bottom = Mathf.Min(top - 0.25f, Height(p.x, p.z) - 0.3f);
            m.OBox(new Vector3(p.x, (top + bottom) * 0.5f, p.z), fwd * 1.3f, Vector3.up * (top - bottom) * 0.5f,
                   sflat * (run / nSteps * 0.5f + 0.02f), PGranite, Mathf.Lerp(0.5f, 0.7f, Rn()), 0.8f);
        }
        // Nobori banners along the verge, both sides of the steps.
        for (int k = -3; k <= 3; k++)
        {
            if (k == 0) continue;
            int j = Mathf.Clamp(i + k * 2, 0, route.Count - 1);
            Foot(route, j, side * (CorridorClearM + 0.8f), out var bf, out _);
            var bs = route.SideFlat(j) * side;
            var bfwd = Vector3.Cross(bs, Vector3.up).normalized;
            m.OBox(bf + Vector3.up * 2.4f, bfwd * 0.04f, Vector3.up * 2.4f, bs * 0.04f, PBlack, 0.4f);
            int row = (k & 1) == 0 ? PRed : PWhite;
            m.OBox(bf + Vector3.up * 3.0f + bfwd * 0.36f, bfwd * 0.32f, Vector3.up * 1.2f, bs * 0.015f, row, 0.85f);
            m.OBox(bf + Vector3.up * 4.25f + bfwd * 0.36f, bfwd * 0.36f, Vector3.up * 0.03f, bs * 0.03f, PBlack, 0.4f);
        }
        // A pair of lanterns flanking the steps and a stack of firewood.
        for (int s = -1; s <= 1; s += 2)
        {
            var lf = s0 + sflat * 0.9f + fwd * s * 1.9f;
            lf.y = Height(lf.x, lf.z);
            Lantern(m, lf, fwd);
        }
        m.OBox(hc + fwd * (W + 0.9f) + Vector3.up * 0.6f, fwd * 0.4f, Vector3.up * 0.6f, sflat * 1.4f, PBark, 0.55f, 0.8f);
    }

    // ---- ishigaki stones, one station span --------------------------------------------------

    private static int RetainingWallSpan(PMesh m, FujiRoute route, int i, int side, System.Random rnd)
    {
        float Rn() => (float)rnd.NextDouble();
        float off = side * (CorridorClearM + 1.1f);
        Foot(route, i, off, out var f0, out _);
        Foot(route, i + 1, off, out var f1, out _);
        Foot(route, i, side * (CorridorClearM + 6f), out var up, out _);
        float wallH = Mathf.Clamp(up.y - f0.y, 1.0f, 2.8f);
        var along = f1 - f0; along.y = 0f;
        float len = along.magnitude;
        if (len < 0.1f) return 0;
        along /= len;
        var outward = route.SideFlat(i) * side;
        int count = 0;
        float x = 0f;
        while (x < len)
        {
            float sw = Mathf.Lerp(0.55f, 1.1f, Rn());
            float y = -0.2f;
            float batter = 0f;
            while (y < wallH)
            {
                float sh = Mathf.Lerp(0.35f, 0.6f, Rn());
                var baseP = Vector3.Lerp(f0, f1, (x + sw * 0.5f) / len);
                var c = baseP + Vector3.up * (y + sh * 0.5f) + outward * (batter + 0.35f) + along * Rn() * 0.08f;
                int row = Rn() < 0.35f ? PMoss : PGranite;
                m.OBox(c, along * (sw * 0.47f), Vector3.up * (sh * 0.46f), outward * 0.35f, row,
                       Mathf.Lerp(0.35f, 0.75f, Rn()), Mathf.Lerp(0.55f, 0.85f, Rn()));
                y += sh;
                batter += sh * 0.18f;
                count++;
            }
            x += sw;
        }
        return count;
    }

    // ---- the summit cone on the skyline ------------------------------------------------------

    private static void BuildSummitCone(Transform group, FujiRoute route)
    {
        int n = route.Count;
        var end = route.Position[n - 1];
        var back = route.Position[Mathf.Max(0, n - 500)];
        var dir = end - back; dir.y = 0f;
        if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
        dir.Normalize();
        const float BaseR = 3400f;
        var centre = end + dir * (BaseR + 700f);
        float baseY = MassifValleyY;
        float apexY = Mathf.Max(route.MaxY + 1500f, 3300f);
        const int Seg = 72, Rings = 16;

        var rock = CelMaterial("Fuji_SummitRock", new Color(0.36f, 0.34f, 0.40f), gloss: 0.05f,
                               spec: 0.0f, rim: 0.0f, shade: new Color(0.55f, 0.62f, 0.78f, 1f));
        var snow = CelMaterial("Fuji_SummitSnow", new Color(0.93f, 0.95f, 1.0f), gloss: 0.1f,
                               spec: 0.05f, rim: 0.0f, shade: new Color(0.62f, 0.70f, 0.88f, 1f));

        Vector3 P(float t, float a, float lift)
        {
            float r = BaseR * (0.93f * Mathf.Pow(1f - t, 1.7f) + 0.07f);
            float wob = 1f + 0.03f * Mathf.Sin(a * 5f + 1.3f) + 0.02f * Mathf.Sin(a * 11f);
            r = r * wob + lift;
            return new Vector3(centre.x + Mathf.Cos(a) * r, Mathf.Lerp(baseY, apexY, t) + lift * 0.3f,
                               centre.z + Mathf.Sin(a) * r);
        }

        void Shell(string name, Material mat, System.Func<int, float> tStart, float lift)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int k = 0; k <= Seg; k++)
            {
                float a = k * Mathf.PI * 2f / Seg;
                float t0 = tStart(k % Seg);
                for (int r = 0; r <= Rings; r++)
                {
                    float t = Mathf.Lerp(t0, 1f, r / (float)Rings);
                    v.Add(P(t, a, lift));
                    uv.Add(new Vector2(k / (float)Seg, t));
                }
            }
            for (int k = 0; k < Seg; k++)
            for (int r = 0; r < Rings; r++)
            {
                int a = k * (Rings + 1) + r, b = (k + 1) * (Rings + 1) + r;
                // Outward = away from the axis; ring angle increases CCW seen from above.
                tri.Add(a); tri.Add(a + 1); tri.Add(b);
                tri.Add(b); tri.Add(a + 1); tri.Add(b + 1);
            }
            var mesh = Finish(name, v.ToArray(), uv.ToArray(), tri);
            // Guard the winding: if the first normal points inward, flip every triangle.
            var nrm = mesh.normals[Rings / 2];
            var radial = v[Rings / 2] - centre; radial.y = 0f;
            if (Vector3.Dot(nrm, radial) < 0f)
            {
                for (int q = 0; q < tri.Count; q += 3) { int t = tri[q + 1]; tri[q + 1] = tri[q + 2]; tri[q + 2] = t; }
                mesh = Finish(name, v.ToArray(), uv.ToArray(), tri);
            }
            var go = AddMesh(group, name, mesh, mat, collider: false);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Shell("Fuji_SummitCone", rock, _ => 0f, 0f);
        // Snow cap with a ragged lower edge: gullies carry snow further down.
        Shell("Fuji_SummitSnowCap", snow, k =>
        {
            float g = Mathf.PerlinNoise(k * 0.37f, 4.2f);
            return 0.55f + (k % 3 == 0 ? -0.10f : 0.04f) + (g - 0.5f) * 0.14f;
        }, 6f);
        Debug.Log($"[fuji] summit cone at {centre} r {BaseR:0} m, apex {apexY:0} m.");
    }
}

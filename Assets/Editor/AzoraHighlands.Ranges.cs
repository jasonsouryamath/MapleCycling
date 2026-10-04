using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// WP-H1 Azora mountain ranges + depth (copilot session 4, 2026-09-26).
//
// USER: "Part of the map looks like the arctic circle. We need more mountain ranges with depth of
// field view." The far ground drops to a floor that snow turns into tundra, and every view ended
// on a flat white horizon with a few isolated cone horns on it. The old ridge rings sat at
// 1,180 +- 470 m, i.e. BELOW the plateau horizon, so from the upper half of the route they never
// showed at all.
//
// Three depth layers now, each paler and bluer than the one in front (aerial perspective in
// MapleRide/HDRP/AzoraAlpineRange):
//   1. Foothills  1.5-4 km   ridges rising out of the snowfield, crest 650-1,250 m above the road.
//   2. Main range 4-9 km     long massifs with 1-3 horns each, crests 2,900-3,950 m.
//   3. Far chain  11-24 km   a ring of ranges that follows the camera; the shader packs it into
//                            the last slice before the far clip along each view ray, so it keeps
//                            its true angular size and still renders inside the 9 km ride clip.
// Static layers use the shader's soft far knee (the same along-the-ray trick, only past ~0.62 x
// far clip) so a massif never shows a hard far-clip cut from the other end of the route.
// Every massif is height-clamped to the snowfield floor near the road, the lakes and the river,
// so no layer can ever stand on the route or in the water.
public static partial class AzoraHighlandsEnvironment
{
    private const string AlpineRangeShaderName = "MapleRide/HDRP/AzoraAlpineRange";
    private static readonly Color AlpineHazeColour = new Color(0.76f, 0.84f, 0.92f, 1f);
    /// <summary>Massif rim height: below every ground surface in the region (route 900-1,980 m,
    /// massif valley floor 660 m, lake beds ~850 m), so a massif always rises out of the ground.</summary>
    private const float MassifFloorY = 400f;

    private static Vector2[] _rangePts;
    private static float[] _rangeY;

    private struct MassifSpec
    {
        public Vector2 centre;
        public float angle, halfLen, halfWid, crest, seed, clear0, clear1, sharp, concave;
        public int nu, nw;
    }

    // ------------------------------------------------------------------ helpers

    private static void PrepareRangeRoute(AzoraRoute route)
    {
        var pts = new List<Vector2>();
        var ys = new List<float>();
        for (float d = 0f; d <= route.Length; d += 40f)
        {
            var p = route.Position[route.IndexAt(d)];
            pts.Add(new Vector2(p.x, p.z));
            ys.Add(p.y);
        }
        _rangePts = pts.ToArray();
        _rangeY = ys.ToArray();
    }

    private static float RangeRouteDist(float x, float z, out float nearestY)
    {
        float best = float.MaxValue;
        int bi = 0;
        for (int i = 0; i < _rangePts.Length; i++)
        {
            float dx = _rangePts[i].x - x, dz = _rangePts[i].y - z;
            float s = dx * dx + dz * dz;
            if (s < best) { best = s; bi = i; }
        }
        nearestY = _rangeY[bi];
        return Mathf.Sqrt(best);
    }

    /// <summary>0 in (and just around) the water, 1 well clear of it. Continuous, so a massif
    /// that reaches a lake slopes down to the shore instead of being cut off.</summary>
    private static float RangeWaterAtt(float x, float z)
    {
        if (!_waterPrepared) return 1f;
        float a = Mathf.Min(RangeLakeAtt(_approachLake, x, z), RangeLakeAtt(_descentLake, x, z));
        if (_river != null && _river.Length >= 2)
        {
            var rb = _riverBounds;
            if (x > rb.xMin - 600f && x < rb.xMax + 600f && z > rb.yMin - 600f && z < rb.yMax + 600f &&
                ClosestRiver(x, z, out float d, out float w, out _, out _))
                a = Mathf.Min(a, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(w + 60f, w + 500f, d)));
        }
        return a;
    }

    private static float RangeLakeAtt(WaterEllipse e, float x, float z)
    {
        if (!LakeValid(e)) return 1f;
        float beyond = (EllipseN(e, x, z) - 1f) * Mathf.Min(e.radiusAlong, e.radiusAcross);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(80f, 600f, beyond));
    }

    /// <summary>Ridged multifractal: sharp crests and aretes, soft gullies between them.</summary>
    private static float RangeRidged(float x, float z, float freq, int octaves, float seed)
    {
        float sum = 0f, amp = 1f, tot = 0f, weight = 1f, f = freq;
        for (int o = 0; o < octaves; o++)
        {
            float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * f + seed, z * f - seed * 0.7f) * 2f - 1f);
            n *= n;
            n *= weight;
            weight = Mathf.Clamp01(n * 1.8f);
            sum += n * amp;
            tot += amp;
            amp *= 0.5f;
            f *= 2.07f;
        }
        return sum / tot;
    }

    private static Material AlpineRangeMaterial(string name, bool farChain)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var shader = Shader.Find(AlpineRangeShaderName);
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning($"[azora] '{AlpineRangeShaderName}' unavailable - '{name}' falls back to CelLit.");
            var cel = CelMaterial(name, new Color(0.80f, 0.83f, 0.88f), gloss: 0.05f, spec: 0.02f, rim: 0.08f,
                                  shade: new Color(0.55f, 0.64f, 0.80f));
            cel.SetFloat("_Cull", 0f);
            return cel;
        }
        var mat = LoadOrCreate(name, AlpineRangeShaderName);
        mat.SetColor("_RockLit", new Color(0.45f, 0.44f, 0.45f, 1f));
        mat.SetColor("_RockShade", new Color(0.29f, 0.33f, 0.41f, 1f));
        mat.SetColor("_SnowLit", new Color(0.91f, 0.93f, 0.96f, 1f));
        mat.SetColor("_SnowShade", new Color(0.60f, 0.68f, 0.82f, 1f));
        mat.SetColor("_HazeColor", AlpineHazeColour);
        mat.SetFloat("_Wrap", 0.30f);
        mat.SetFloat("_MacroNoise", 0.08f);
        mat.SetFloat("_MacroScale", 1600f);
        mat.SetFloat("_SceneFog", 0f);
        if (farChain)
        {
            mat.SetFloat("_HazeStart", 9000f);
            mat.SetFloat("_HazeFull", 24000f);
            mat.SetFloat("_HazeMin", 0.44f);
            mat.SetFloat("_HazeMax", 0.80f);
            mat.SetFloat("_BaseY", 900f);
            mat.SetFloat("_ValleyHeight", 1300f);
            mat.SetFloat("_ValleyFill", 0.55f);
            mat.SetFloat("_Compress", 1f);
            mat.SetFloat("_NominalNear", FarChainInnerM);
            mat.SetFloat("_NominalFar", FarChainOuterM);
            mat.SetFloat("_GeoNear", 0.82f);
            mat.SetFloat("_GeoFar", 0.97f);
        }
        else
        {
            mat.SetFloat("_HazeStart", 500f);
            mat.SetFloat("_HazeFull", 12000f);
            mat.SetFloat("_HazeMin", 0.10f);
            mat.SetFloat("_HazeMax", 0.62f);
            mat.SetFloat("_BaseY", 800f);
            mat.SetFloat("_ValleyHeight", 600f);
            mat.SetFloat("_ValleyFill", 0.30f);
            mat.SetFloat("_Compress", 0f);
            mat.SetFloat("_SoftFar", 1f);
            mat.SetFloat("_KneeStart", 0.60f);
            mat.SetFloat("_KneeEnd", 0.80f);
        }
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>Snow on everything that is not steep, dark rock on the faces and aretes, mottled
    /// by noise so the snow line breaks up like a real north face. COLOR = (snow, tone, cavity).</summary>
    private static Color[] AlpineColours(Vector3[] v, Vector3[] n, float[] ridged, float seed, float snowLo, float snowHi, float mottleM)
    {
        var c = new Color[v.Length];
        for (int k = 0; k < v.Length; k++)
        {
            float x = v[k].x, z = v[k].z;
            float mottle = Mathf.PerlinNoise(x / mottleM + seed, z / mottleM - seed) - 0.5f
                         + (Mathf.PerlinNoise(x / (mottleM * 0.25f) - seed, z / (mottleM * 0.25f) + seed) - 0.5f) * 0.5f;
            float snow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(snowLo, snowHi, n[k].y + mottle * 0.24f));
            if (v[k].y < MassifFloorY + 40f) snow = 1f;
            float tone = Mathf.PerlinNoise(x / (mottleM * 1.7f) - seed * 1.3f, z / (mottleM * 1.7f) + seed);
            float cav = Mathf.Clamp01(0.40f + 0.9f * ridged[k]);
            c[k] = new Color(snow, tone, cav, 1f);
        }
        return c;
    }

    private static Mesh AlpineMesh(string name, Vector3[] v, Vector2[] uv, List<int> tris, float[] ridged,
                                   float seed, float snowLo, float snowHi, float mottleM, Bounds? cullBounds)
    {
        var mesh = new Mesh { name = name };
        mesh.indexFormat = v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32
                                            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = v;
        mesh.uv = uv;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        // Snow is baked from the normal's up component, so the surface MUST face up.
        var nrm = mesh.normals;
        double upSum = 0;
        for (int k = 0; k < nrm.Length; k++) upSum += nrm[k].y;
        if (upSum < 0)
        {
            for (int t = 0; t < tris.Count; t += 3) { int s = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = s; }
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            nrm = mesh.normals;
        }
        mesh.colors = AlpineColours(v, nrm, ridged, seed, snowLo, snowHi, mottleM);
        mesh.RecalculateBounds();
        if (cullBounds.HasValue)
        {
            // Frustum culling must not drop a massif that is past the far clip in WORLD space:
            // the shader's soft far knee pulls it back inside. Stretch the bounds over the route.
            var b = mesh.bounds;
            b.Encapsulate(cullBounds.Value);
            mesh.bounds = b;
        }
        return mesh;
    }

    // ------------------------------------------------------------------ static massifs

    private static Mesh MassifMesh(string name, MassifSpec m, Bounds cullBounds, out int tris)
    {
        var ax = new Vector2(Mathf.Cos(m.angle), Mathf.Sin(m.angle));
        var pp = new Vector2(-ax.y, ax.x);
        var rng = new System.Random((int)(m.seed * 7919f) + 17);
        int ns = 1 + rng.Next(3);
        var su = new float[ns]; var ss = new float[ns]; var sa = new float[ns];
        for (int k = 0; k < ns; k++)
        {
            su[k] = Mathf.Lerp(-0.50f, 0.50f, (float)rng.NextDouble());
            ss[k] = Mathf.Lerp(0.32f, 0.55f, (float)rng.NextDouble());
            sa[k] = k == 0 ? 1f : Mathf.Lerp(0.70f, 0.92f, (float)rng.NextDouble());
        }
        float saddle = Mathf.Lerp(0.45f, 0.62f, (float)rng.NextDouble());

        // Rim just under the LOWEST ground in the footprint, so the massif rises from the
        // snowfield with its whole flank visible instead of poking up as a steep spike.
        float floorY = float.MaxValue;
        for (int a = -2; a <= 2; a++)
        for (int b = -2; b <= 2; b++)
        {
            var q = m.centre + ax * (a * 0.5f * m.halfLen) + pp * (b * 0.5f * m.halfWid);
            floorY = Mathf.Min(floorY, Height(q.x, q.y));
        }
        floorY = Mathf.Max(MassifFloorY, floorY - 80f);
        float crest = Mathf.Max(m.crest, floorY + 500f);

        int W = m.nu + 1, H = m.nw + 1;
        var v = new Vector3[W * H];
        var uv = new Vector2[v.Length];
        var shp = new float[v.Length];
        var rid = new float[v.Length];
        for (int iw = 0; iw < H; iw++)
        for (int iu = 0; iu < W; iu++)
        {
            float u = iu / (float)m.nu * 2f - 1f, w = iw / (float)m.nw * 2f - 1f;
            var q = m.centre + ax * (u * m.halfLen) + pp * (w * m.halfWid);
            float x = q.x, z = q.y;
            // The crest wanders; summits sit on it and a lower saddle ridge links them.
            float wc = 0.16f * Mathf.Sin(u * 2.1f + m.seed) + 0.08f * Mathf.Sin(u * 5.3f + m.seed * 1.7f);
            float dw = Mathf.Abs(w - wc);
            float crestLine = saddle * (1f - u * u);
            for (int k = 0; k < ns; k++)
                crestLine = Mathf.Max(crestLine, sa[k] * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u - su[k]) / ss[k]), m.sharp));
            float env = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(u), 4f));
            // Flank width varies along the crest, with side spurs (aretes) pushing out.
            float halfW = 0.90f - 0.30f * Mathf.Abs(u) + 0.25f * (Mathf.PerlinNoise(u * 3.1f + m.seed, 0.37f) - 0.5f)
                        + Mathf.Pow(Mathf.Abs(Mathf.Sin(u * 7f + m.seed * 2.3f)), 3f) * 0.22f;
            float prof = Mathf.Pow(Mathf.Clamp01(1f - dw / Mathf.Max(0.2f, halfW)), m.concave);
            float rn = RangeRidged(x, z, 1f / 1500f, 4, m.seed);
            float shape = crestLine * env * prof * (0.60f + 0.64f * rn);
            float dist = RangeRouteDist(x, z, out _);
            shape *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(m.clear0, m.clear1, dist)) * RangeWaterAtt(x, z);
            // Rock ribs and couloirs: an additive ridged term, strongest on the upper flanks.
            float rib = RangeRidged(x, z, 1f / 520f, 3, m.seed + 5.3f);
            int idx = iw * W + iu;
            v[idx] = new Vector3(x, floorY + (crest - floorY) * (shape + 0.07f * rib * Mathf.Sqrt(shape)), z);
            uv[idx] = new Vector2(x / 100f, z / 100f);
            shp[idx] = shape;
            rid[idx] = rn;
        }
        var t = new List<int>(m.nu * m.nw * 6);
        for (int iw = 0; iw < m.nw; iw++)
        for (int iu = 0; iu < m.nu; iu++)
        {
            int a = iw * W + iu, b = a + W;
            // A quad whose corners all sit on the rim is buried under the snowfield: skip it.
            if (shp[a] < 1e-3f && shp[a + 1] < 1e-3f && shp[b] < 1e-3f && shp[b + 1] < 1e-3f) continue;
            t.Add(a); t.Add(b); t.Add(a + 1);
            t.Add(a + 1); t.Add(b); t.Add(b + 1);
        }
        tris = t.Count / 3;
        if (tris == 0) return null;
        return AlpineMesh(name, v, uv, t, rid, m.seed, 0.16f, 0.44f, 380f, cullBounds);
    }

    /// <summary>Foothills and the main range. Called from BuildAlpinePeaks (Winter.cs).</summary>
    private static void BuildAlpineMassifs(Transform group, AzoraRoute route)
    {
        PrepareRangeRoute(route);
        var mat = AlpineRangeMaterial("Azora_Alpine_Massif", farChain: false);
        var cull = new Bounds(route.Plan.center, route.Plan.size + new Vector3(0f, 4000f, 0f));
        var placed = new List<Vector4>();   // xz = centre, z = keep-apart radius, w = layer

        int Layer(string label, int layer, int seed, float step, float offLo, float offHi, float minRoute, float apart,
                  float lenLo, float lenHi, float widLo, float widHi, System.Func<System.Random, float, float> crestFn,
                  int nu, int nw, float clear0, float clear1, float sharpLo, float sharpHi, float concave, ref int triTotal)
        {
            var sub = new GameObject(label).transform;
            sub.SetParent(group, false);
            var rng = new System.Random(seed);
            int n = 0;
            for (float d = step * 0.35f; d < route.Length; d += step)
            {
                int i = route.IndexAt(d);
                var p = route.Position[i];
                var side = route.SideFlat(i);
                var tan = new Vector2(route.Tangent[i].x, route.Tangent[i].z);
                if (tan.sqrMagnitude < 1e-4f) tan = Vector2.up;
                for (int s = -1; s <= 1; s += 2)
                {
                    float turn = Mathf.Lerp(-0.6f, 0.6f, (float)rng.NextDouble());
                    float len = Mathf.Lerp(lenLo, lenHi, (float)rng.NextDouble());
                    float wid = Mathf.Lerp(widLo, widHi, (float)rng.NextDouble());
                    float sharp = Mathf.Lerp(sharpLo, sharpHi, (float)rng.NextDouble());
                    float rnd = (float)rng.NextDouble();
                    float off0 = Mathf.Lerp(offLo, offHi, (float)rng.NextDouble());
                    // The route folds back on itself, so the first offset often lands near another
                    // leg: step outward a couple of times before giving up on this station.
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        float off = off0 + attempt * (offHi - offLo) * 0.45f;
                        var c = new Vector2(p.x + side.x * s * off, p.z + side.z * s * off);
                        if (RangeRouteDist(c.x, c.y, out float ny) < minRoute) continue;
                        if (RangeWaterAtt(c.x, c.y) < 0.99f) continue;
                        bool clash = false;
                        foreach (var q in placed)
                        {
                            float keep = (int)q.w == layer ? Mathf.Max(q.z, apart) : 0.5f * Mathf.Max(q.z, apart);
                            if ((new Vector2(q.x, q.y) - c).magnitude < keep) { clash = true; break; }
                        }
                        if (clash) continue;
                        var spec = new MassifSpec
                        {
                            centre = c, angle = Mathf.Atan2(tan.y, tan.x) + turn, halfLen = len, halfWid = wid,
                            crest = crestFn(rng, ny), seed = (float)(seed % 97) + n * 3.17f + rnd * 11f,
                            clear0 = clear0, clear1 = clear1, sharp = sharp, concave = concave, nu = nu, nw = nw,
                        };
                        var mesh = MassifMesh($"AzoraH1_{label.Replace(' ', '_')}_{n}", spec, cull, out int tris);
                        if (mesh == null) continue;
                        placed.Add(new Vector4(c.x, c.y, apart, layer));
                        var go = AddMesh(sub, $"{label} {n}", mesh, mat, collider: false);
                        var mr = go.GetComponent<MeshRenderer>();
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                        triTotal += tris;
                        n++;
                        break;
                    }
                }
            }
            return n;
        }

        int triFoot = 0, triMain = 0, triSpur = 0;
        // Main range first so it claims its ground; foothills then fill in between it and the road.
        int main = Layer("Main Range", 0, 20260926, 1100f, 4200f, 6800f, 2700f, 3400f,
                         3000f, 4600f, 2200f, 3200f, (r, ny) => Mathf.Lerp(2900f, 3700f, (float)r.NextDouble()),
                         96, 64, 1900f, 3000f, 1.25f, 1.70f, 1.7f, ref triMain);
        int foot = Layer("Foothills", 1, 20260927, 650f, 1900f, 3300f, 1450f, 1800f,
                         1600f, 2600f, 1100f, 1700f,
                         (r, ny) => Mathf.Clamp(ny + Mathf.Lerp(500f, 1050f, (float)r.NextDouble()), 1600f, 3000f),
                         80, 48, 900f, 1600f, 1.00f, 1.40f, 1.8f, ref triFoot);
        // Low spurs 1-1.7 km out: the ground rolls up toward the foothills instead of lying flat.
        int spur = Layer("Spurs", 2, 20260928, 500f, 1000f, 1700f, 950f, 1100f,
                         700f, 1300f, 450f, 750f,
                         (r, ny) => ny + Mathf.Lerp(150f, 500f, (float)r.NextDouble()),
                         48, 32, 520f, 950f, 0.90f, 1.20f, 1.7f, ref triSpur);        Debug.Log($"[azora] H1 massifs: {main} main-range ({triMain} tris), {foot} foothills ({triFoot} tris), {spur} spurs ({triSpur} tris).");
    }

    // ------------------------------------------------------------------ far chain

    private const float FarChainInnerM = 11000f;
    private const float FarChainOuterM = 24000f;

    /// <summary>
    /// The far chain: an annulus of ranges 11-24 km out, authored around the route plan centre and
    /// kept centred on the camera by <see cref="AzoraRangeFollow"/>. The shader draws it in the last
    /// slice before the far clip (see AzoraAlpineRange.shader), hazed by its nominal distance, so
    /// ridge after ridge recedes into the blue. Called from BuildDistantRanges.
    /// </summary>
    private static void BuildAlpineFarChain(Transform group, AzoraRoute route)
    {
        var c = route.Plan.center;
        const int Seg = 1024, Rows = 84;
        const float Seed = 41.3f;
        int W = Seg + 1;
        var v = new Vector3[W * (Rows + 1)];
        var uv = new Vector2[v.Length];
        var rid = new float[v.Length];
        for (int j = 0; j <= Rows; j++)
        {
            float rt = j / (float)Rows;
            // Mildly denser rows near the inner rim, where the chain rises and is seen largest.
            float r = Mathf.Lerp(FarChainInnerM, FarChainOuterM, rt * rt * 0.30f + rt * 0.70f);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FarChainInnerM, FarChainInnerM + 3800f, r));
            for (int k = 0; k <= Seg; k++)
            {
                float a = k / (float)Seg * Mathf.PI * 2f;
                float lx = Mathf.Cos(a) * r, lz = Mathf.Sin(a) * r;
                // Domain warp so ranges bend and branch instead of lining up on the noise grid.
                float wx = lx + (Mathf.PerlinNoise(lx / 7000f + 3.1f, lz / 7000f + 8.7f) - 0.5f) * 5200f;
                float wz = lz + (Mathf.PerlinNoise(lx / 7000f + 19.4f, lz / 7000f + 2.2f) - 0.5f) * 5200f;
                // 4 octaves from 6.5 km: broad massifs with horns; finer octaves only aliased on
                // a ~150 m grid into vertical streaks.
                float rn = RangeRidged(wx, wz, 1f / 6500f, 4, Seed);
                // Massif-scale envelope: some sectors hold the big summits, others lower chains.
                float env = 0.55f + 0.60f * Mathf.PerlinNoise(lx / 9000f + 71.2f, lz / 9000f + 13.9f);
                float h = MassifFloorY - 100f + rise * (1000f + 3400f * env * Mathf.Pow(rn, 1.15f));
                int idx = j * W + k;
                v[idx] = new Vector3(c.x + lx, h, c.z + lz);
                uv[idx] = new Vector2(k / (float)Seg * 48f, rt);
                rid[idx] = rn;
            }
        }
        var t = new List<int>(Seg * Rows * 6);
        for (int j = 0; j < Rows; j++)
        for (int k = 0; k < Seg; k++)
        {
            int a = j * W + k, b = a + W;
            t.Add(a); t.Add(b); t.Add(a + 1);
            t.Add(a + 1); t.Add(b); t.Add(b + 1);
        }
        var mesh = AlpineMesh("AzoraH1_FarChain", v, uv, t, rid, Seed, 0.40f, 0.66f, 1500f, null);
        var go = AddMesh(group, "Far Chain", mesh, AlpineRangeMaterial("Azora_Alpine_FarChain", farChain: true), collider: false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Debug.Log($"[azora] H1 far chain: {t.Count / 3} tris, {FarChainInnerM / 1000f:0}-{FarChainOuterM / 1000f:0} km nominal.");
    }
}

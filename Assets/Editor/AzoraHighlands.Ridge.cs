using System.Collections.Generic;
using UnityEngine;

// Azora concept pass, WP-D (RIDGE + SUMMIT), winter version (copilot session 1).
//   Seg 4 Cliffside Ridge (14400-18500): rock cliff on the uphill side, galvanised W-beam
//        guardrail on the drop side, yellow/black chevrons on the outside of tight bends,
//        orange/black snow poles, a sea of cloud filling the valleys below the upper route.
//   Seg 5 Summit Plateau (18500-20400): wind-sculpted snow drifts, the summit observatory
//        (stone tower, glass drum, white dome, mast) on a knoll by the col.
//   Seg 6 Summit Descent (20400-end): timber rail fence on the drop side, viewpoint bench.
// Everything is built with the local RMesh helper below (no dependency on other packages'
// primitives) and merged per ~1 km tile. Snow comes from the weather snow term (ApplyWinter).
public static partial class AzoraHighlandsEnvironment
{
    private const float RidgeFromM = 14400f, RidgeToM = 18500f;
    private const float PlateauToM = 20400f;

    private sealed class RMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();

        /// <summary>
        /// Quad a-b-c-d (in order around its rim), emitted on BOTH sides with SEPARATE vertices:
        /// two opposite windings sharing vertices would sum their normals to zero.
        /// </summary>
        public void Quad2(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale = 1f)
        {
            float w = Vector3.Distance(a, b) * uvScale, h = Vector3.Distance(b, c) * uvScale;
            for (int side = 0; side < 2; side++)
            {
                int s = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(w, 0)); UV.Add(new Vector2(w, h)); UV.Add(new Vector2(0, h));
                if (side == 0) T.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                else T.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
        }

        /// <summary>Quad2 with explicit UVs, so a surface built from many quads tiles its texture continuously.</summary>
        public void Quad2UV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            for (int side = 0; side < 2; side++)
            {
                int s = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
                if (side == 0) T.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                else T.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
        }

        /// <summary>Oriented box, foot at <paramref name="c"/> (centre of its base), half extents.</summary>
        public void Box(Vector3 c, Vector3 right, Vector3 fwd, float h)
        {
            var up = Vector3.up * h;
            Vector3 p0 = c - right - fwd, p1 = c + right - fwd, p2 = c + right + fwd, p3 = c - right + fwd;
            Quad2(p0, p1, p1 + up, p0 + up);
            Quad2(p1, p2, p2 + up, p1 + up);
            Quad2(p2, p3, p3 + up, p2 + up);
            Quad2(p3, p0, p0 + up, p3 + up);
            Quad2(p0 + up, p1 + up, p2 + up, p3 + up);
        }

        /// <summary>Vertical N-gon prism (tower, drum, pole).</summary>
        public void Prism(Vector3 c, float r, float h, int n, float topR = -1f)
        {
            if (topR < 0f) topR = r;
            for (int k = 0; k < n; k++)
            {
                float a0 = k / (float)n * Mathf.PI * 2f, a1 = (k + 1) / (float)n * Mathf.PI * 2f;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Quad2(c + d0 * r, c + d1 * r, c + d1 * topR + Vector3.up * h, c + d0 * topR + Vector3.up * h);
                var top = c + Vector3.up * h;
                int s = V.Count;
                V.Add(top); V.Add(top + d1 * topR); V.Add(top + d0 * topR);
                UV.Add(Vector2.zero); UV.Add(Vector2.right); UV.Add(Vector2.up);
                T.AddRange(new[] { s, s + 1, s + 2 });   // faces up (single-sided cap)
            }
        }

        /// <summary>Upper hemisphere (dome / drift lump), squashable per axis, rotated by yaw.</summary>
        public void Dome(Vector3 c, Vector3 radii, float yawDeg, int lon = 14, int lat = 6)
        {
            var q = Quaternion.Euler(0f, yawDeg, 0f);
            int s = V.Count;
            for (int y = 0; y <= lat; y++)
            {
                float phi = y / (float)lat * Mathf.PI * 0.5f;
                for (int x = 0; x <= lon; x++)
                {
                    float th = x / (float)lon * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(th) * Mathf.Cos(phi) * radii.x, Mathf.Sin(phi) * radii.y,
                                        Mathf.Sin(th) * Mathf.Cos(phi) * radii.z);
                    V.Add(c + q * p);
                    UV.Add(new Vector2(x / (float)lon, y / (float)lat));
                }
            }
            for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = s + y * (lon + 1) + x, b = a + lon + 1;
                T.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });   // outward (single-sided)
            }
        }

        public bool Empty => T.Count == 0;
    }

    private static void FlushR(Transform group, string name, RMesh m, Material mat, bool shadows)
    {
        if (m.Empty) return;
        var go = AddMesh(group, name, Finish(name, m.V.ToArray(), m.UV.ToArray(), m.T), mat, collider: false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static void BuildRidgeAndSummit(Transform root, AzoraRoute route)
    {
        var group = new GameObject("Azora Ridge And Summit").transform;
        group.SetParent(root, false);

        var rockTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoastTextureDir}/Shiosai_Rock_Albedo.png");
        // WP-G: the face carries continuous UVs (Quad2UV), so it can take the rock albedo without the
        // per-quad seams that made the peaks drop their texture; the tint keeps it darker than the snow.
        var cliffMat = CelMaterial("AzoraD_Cliff_Rock", new Color(0.66f, 0.66f, 0.68f), gloss: 0.05f, spec: 0.03f, rim: 0.12f,
                                   texture: rockTex, shade: new Color(0.52f, 0.58f, 0.74f));
        cliffMat.SetTexture("_MainTex", rockTex);
        cliffMat.SetFloat("_WeatherAmount", 1f);
        cliffMat.SetFloat("_DetailScale", 0.35f);
        cliffMat.SetFloat("_DetailAmount", 1f);
        cliffMat.SetFloat("_TintVariation", 0.3f);
        cliffMat.SetFloat("_TintVarScale", 0.05f);
        cliffMat.SetFloat("_MossAmount", 0f);
        cliffMat.SetFloat("_Cull", 0f);
        var steel = CelMaterial("AzoraD_Guardrail_Steel", new Color(0.58f, 0.60f, 0.63f), gloss: 0.45f, spec: 0.35f, rim: 0.18f);
        var postMat = CelMaterial("AzoraD_Guardrail_Post", new Color(0.55f, 0.57f, 0.60f), gloss: 0.3f, spec: 0.2f, rim: 0.12f);
        var yellow = CelMaterial("AzoraD_Chevron_Paint_Yellow", new Color(0.98f, 0.78f, 0.10f), gloss: 0.2f, spec: 0.1f, rim: 0.1f);
        var black = CelMaterial("AzoraD_Chevron_Paint_Black", new Color(0.06f, 0.06f, 0.07f), gloss: 0.2f, spec: 0.1f, rim: 0.1f);
        var orange = CelMaterial("AzoraD_Pole_Paint_Orange", new Color(0.95f, 0.38f, 0.08f), gloss: 0.25f, spec: 0.1f, rim: 0.15f);
        var timber = CelMaterial("AzoraD_Fence_Timber", new Color(0.42f, 0.30f, 0.20f), gloss: 0.05f, spec: 0.03f, rim: 0.12f);
        var drift = CelMaterial("AzoraD_Snow_Drift", new Color(0.90f, 0.92f, 0.95f), gloss: 0.05f, spec: 0.02f, rim: 0.12f,
                                shade: new Color(0.60f, 0.70f, 0.88f));
        var stone = CelMaterial("AzoraD_Observatory_Stone", new Color(0.86f, 0.83f, 0.78f), gloss: 0.06f, spec: 0.04f, rim: 0.12f,
                                texture: rockTex, shade: new Color(0.52f, 0.58f, 0.74f));
        var glass = CelMaterial("AzoraD_Observatory_Glass", new Color(0.30f, 0.42f, 0.52f), gloss: 0.8f, spec: 0.6f, rim: 0.35f);
        var dome = CelMaterial("AzoraD_Observatory_Dome", new Color(0.88f, 0.89f, 0.90f), gloss: 0.5f, spec: 0.3f, rim: 0.2f);
        var cloud = CelMaterial("AzoraD_CloudSea_Sky", new Color(0.97f, 0.975f, 0.985f), gloss: 0.02f, spec: 0.0f, rim: 0.35f,
                                shade: new Color(0.70f, 0.76f, 0.90f));
        cloud.SetFloat("_ShadeStrength", 0.9f);

        int cliffs = 0, rails = 0, chev = 0, poles = 0, fence = 0;
        const float TileM = 1000f;
        float step = route.Count > 1 ? route.Distance[1] - route.Distance[0] : 4f;

        RMesh mCliff = new RMesh(), mRail = new RMesh(), mPost = new RMesh(), mYel = new RMesh(), mBlk = new RMesh(),
              mOra = new RMesh(), mFence = new RMesh(), mDrift = new RMesh();
        float tileStart = RidgeFromM;
        void FlushTile(float d)
        {
            string k = $"{tileStart:0}";
            FlushR(group, $"AzoraD_Cliff_{k}", mCliff, cliffMat, true);
            FlushR(group, $"AzoraD_Rail_{k}", mRail, steel, true);
            FlushR(group, $"AzoraD_Post_{k}", mPost, postMat, false);
            FlushR(group, $"AzoraD_ChevY_{k}", mYel, yellow, false);
            FlushR(group, $"AzoraD_ChevB_{k}", mBlk, black, false);
            FlushR(group, $"AzoraD_Pole_{k}", mOra, orange, false);
            FlushR(group, $"AzoraD_Fence_{k}", mFence, timber, true);
            FlushR(group, $"AzoraD_Drift_{k}", mDrift, drift, true);
            mCliff = new RMesh(); mRail = new RMesh(); mPost = new RMesh(); mYel = new RMesh(); mBlk = new RMesh();
            mOra = new RMesh(); mFence = new RMesh(); mDrift = new RMesh();
            tileStart = d;
        }

        int iFrom = route.IndexAt(RidgeFromM);
        int prevUphill = 0;
        float lastPost = -99f, lastChev = -99f, lastPole = -99f, lastFence = -99f, lastDrift = -99f;
        float cliffRamp = 0f;   // WP-G: 0..1 run envelope so cliff runs rise and fall over ~30 m, never pop
        int cliffProbe = 0, cliffGeo = 0, cliffWet = 0; float upSum = 0f, asymSum = 0f, upMax = -999f, asymMax = -999f;
        for (int i = iFrom; i < route.Count - 2; i++)
        {
            float d = route.Distance[i];
            if (d - tileStart >= TileM) FlushTile(d);
            var p = route.Position[i];
            var sflat = route.SideFlat(i);

            // Uphill side, decided with hysteresis so the cliff does not flip sides every station.
            float hl = Height(p.x - sflat.x * 60f, p.z - sflat.z * 60f), hr = Height(p.x + sflat.x * 60f, p.z + sflat.z * 60f);
            int uphill = hr > hl + 1.0f ? 1 : hl > hr + 1.0f ? -1 : (prevUphill != 0 ? prevUphill : (hr >= hl ? 1 : -1));
            prevUphill = uphill;

            // ---- Seg 4: cliff + guardrail + chevrons
            if (d >= RidgeFromM && d < RidgeToM && uphill != 0)
            {
                // WP-G (G2). The old test wanted the ground 60 m uphill to stand 10 m ABOVE the road.
                // The Azora massif falls away from the road in every direction (see MassifDrop*),
                // so that almost never happened and the pass placed 0 spans. A road cut into a
                // cliff is a SIDE-HILL road: one side holds up, the other drops. So test the
                // asymmetry between the two sides instead, and author the cut ourselves - a rock
                // face of its own height plus a back slope that runs down to the real terrain,
                // so it reads as a cut into the mountainside and never as a free-standing wall.
                // Probe at 60 m (the same distance that picks the uphill side): within ~25 m the
                // corridor ribbon is still road-derived and nearly level on both sides.
                float upAt = Height(p.x + sflat.x * (uphill * 60f), p.z + sflat.z * (uphill * 60f)) - p.y;
                float dnAt = Height(p.x - sflat.x * (uphill * 60f), p.z - sflat.z * (uphill * 60f)) - p.y;
                var cliffFoot = p + sflat * (uphill * 7.2f);
                bool geo = upAt - dnAt > 3f && upAt > -3f;
                bool wetFoot = Wet(cliffFoot, 6f);
                bool sideHill = geo && !wetFoot;
                cliffProbe++; if (geo) cliffGeo++; if (wetFoot) cliffWet++;
                upSum += upAt; asymSum += upAt - dnAt; upMax = Mathf.Max(upMax, upAt); asymMax = Mathf.Max(asymMax, upAt - dnAt);
                float ramp0 = cliffRamp;
                cliffRamp = Mathf.Clamp01(cliffRamp + (sideHill ? 1f : -1f) * step / 30f);
                if (cliffRamp > 0.02f)
                {
                    Foot(route, i, uphill * 7.2f, out var f0, out var o0);
                    Foot(route, i + 1, uphill * 7.2f, out var f1, out var o1);
                    float hgt0 = (6f + Mathf.PerlinNoise(d * 0.013f, 4.2f) * 11f) * Mathf.SmoothStep(0f, 1f, ramp0);
                    float hgt1 = (6f + Mathf.PerlinNoise((d + step) * 0.013f, 4.2f) * 11f) * Mathf.SmoothStep(0f, 1f, cliffRamp);
                    const int Rows = 6;
                    Vector3 C(Vector3 f, Vector3 o, float h, float t, float dd)
                    {
                        float n = Mathf.PerlinNoise(dd * 0.21f, t * 3.7f + 11f) - 0.5f;
                        // ~75 deg face (was ~45 deg, which the snow term covered completely): steep
                        // enough for the cliff material's negative snow bias to shed it, so it reads
                        // as rock with snow on the ledges.
                        return f - Vector3.up * 1.0f + Vector3.up * (h * t) + o * (t * h * 0.16f + n * 1.8f + t * t * h * 0.12f);
                    }
                    for (int rI = 0; rI < Rows; rI++)
                    {
                        float a = rI / (float)Rows, b = (rI + 1) / (float)Rows;
                        // u runs with route distance, v with height up the face: one ~9 m texture tile, seamless across spans
                        const float K = 0.11f;
                        mCliff.Quad2UV(C(f0, o0, hgt0, a, d), C(f1, o1, hgt1, a, d + step), C(f1, o1, hgt1, b, d + step), C(f0, o0, hgt0, b, d),
                                       new Vector2(d * K, hgt0 * a * K), new Vector2((d + step) * K, hgt1 * a * K),
                                       new Vector2((d + step) * K, hgt1 * b * K), new Vector2(d * K, hgt0 * b * K));
                    }
                    // Back slope: from the cliff lip down to where a ~35 deg fall line meets the
                    // terrain (<= 45 m back), in 3 rows with a little noise, so the face has a
                    // mountainside behind it from every angle including the cloud-sea views.
                    Vector3 Back(Vector3 f, Vector3 o, float h, float dd)
                    {
                        var lip = C(f, o, h, 1f, dd);
                        for (float run = 3f; run <= 45f; run += 3f)
                        {
                            var q = lip + o * run;
                            float g = Height(q.x, q.z);
                            if (g >= lip.y - run * 0.7f) { q.y = g - 0.4f; return q; }
                        }
                        var e = lip + o * 45f; e.y = Mathf.Min(lip.y - 31f, Height(e.x, e.z)) - 0.4f; return e;
                    }
                    var lip0 = C(f0, o0, hgt0, 1f, d); var lip1 = C(f1, o1, hgt1, 1f, d + step);
                    var bk0 = Back(f0, o0, hgt0, d); var bk1 = Back(f1, o1, hgt1, d + step);
                    Vector3 Mid(Vector3 l, Vector3 k, float t, float dd, Vector3 o)
                    {
                        var m = Vector3.Lerp(l, k, t);
                        m += Vector3.up * ((Mathf.PerlinNoise(dd * 0.17f, t * 5.3f + 2f) - 0.35f) * 2.2f * (1f - t));
                        return m;
                    }
                    for (int rI = 0; rI < 3; rI++)
                    {
                        float a = rI / 3f, b = (rI + 1) / 3f;
                        float r0 = Vector3.Distance(lip0, bk0) * 0.11f, r1 = Vector3.Distance(lip1, bk1) * 0.11f;
                        mCliff.Quad2UV(Mid(lip0, bk0, a, d, o0), Mid(lip1, bk1, a, d + step, o1),
                                       Mid(lip1, bk1, b, d + step, o1), Mid(lip0, bk0, b, d, o0),
                                       new Vector2(d * 0.11f, hgt0 * 0.11f + r0 * a), new Vector2((d + step) * 0.11f, hgt1 * 0.11f + r1 * a),
                                       new Vector2((d + step) * 0.11f, hgt1 * 0.11f + r1 * b), new Vector2(d * 0.11f, hgt0 * 0.11f + r0 * b));
                    }
                    cliffs++;
                }

                // guardrail on the drop side
                int drop = -uphill;
                Foot(route, i, drop * (RoadHalfWidth + ShoulderWidth + 0.35f), out var g0, out var go0);
                Foot(route, i + 1, drop * (RoadHalfWidth + ShoulderWidth + 0.35f), out var g1, out var go1);
                g0.y = RoadY(route, i, drop * (RoadHalfWidth + ShoulderWidth)); g1.y = RoadY(route, i + 1, drop * (RoadHalfWidth + ShoulderWidth));
                mRail.Quad2(g0 + Vector3.up * 0.52f, g1 + Vector3.up * 0.52f, g1 + Vector3.up * 0.84f, g0 + Vector3.up * 0.84f);
                // W-beam top lip (up-facing, holds a line of snow)
                mRail.Quad2(g0 + Vector3.up * 0.84f, g1 + Vector3.up * 0.84f, g1 + Vector3.up * 0.84f + go1 * 0.09f, g0 + Vector3.up * 0.84f + go0 * 0.09f);
                rails++;
                if (d - lastPost >= 4f)
                {
                    lastPost = d;
                    var along = route.Tangent[i]; along.y = 0f; along.Normalize();
                    mPost.Box(g0 + go0 * 0.12f - Vector3.up * 0.4f, along * 0.05f, go0 * 0.07f, 1.2f);
                }

                // chevrons on the outside of tight bends
                float turn = Vector3.SignedAngle(route.Tangent[Mathf.Max(0, i - 6)], route.Tangent[Mathf.Min(route.Count - 1, i + 6)], Vector3.up);
                if (Mathf.Abs(turn) > 14f && d - lastChev >= 14f)
                {
                    lastChev = d;
                    int outside = turn > 0f ? -1 : 1;   // turning right -> outside is left
                    Foot(route, i, outside * (RoadHalfWidth + ShoulderWidth + 0.9f), out var cf, out var co);
                    cf.y = Mathf.Max(cf.y, RoadY(route, i, outside * (RoadHalfWidth + ShoulderWidth)));
                    Chevron(mYel, mBlk, mPost, cf, -co, outside * Mathf.Sign(turn) < 0 ? 1f : -1f);
                    chev++;
                }
            }

            // ---- snow poles along the whole upper route (both sides)
            if (d >= RidgeFromM - 3000f && d - lastPole >= 25f)
            {
                lastPole = d;
                for (int s = -1; s <= 1; s += 2)
                {
                    if (d >= RidgeFromM && d < RidgeToM && s == -uphill) continue;   // guardrail side
                    Foot(route, i, s * (RoadHalfWidth + ShoulderWidth + 1.6f), out var pf, out _);
                    if (Wet(pf)) continue;
                    SnowPole(mOra, mBlk, pf);
                    poles++;
                }
            }

            // ---- Seg 5: wind-sculpted drifts on the plateau
            if (d >= RidgeToM && d < PlateauToM && d - lastDrift >= 9f)
            {
                lastDrift = d;
                for (int s = -1; s <= 1; s += 2)
                {
                    float off = s * Mathf.Lerp(12f, 90f, H01(d, s * 3.3f));
                    Foot(route, i, off, out var df, out _);
                    float len = Mathf.Lerp(4f, 13f, H01(d, 9.1f + s));
                    if (Wet(df, len)) continue;
                    float ht = Mathf.Lerp(0.5f, 1.6f, H01(d, 7.7f + s));
                    float yaw = Mathf.Atan2(WindDir.x, WindDir.z) * Mathf.Rad2Deg + (H01(d, 1.9f) - 0.5f) * 30f;
                    mDrift.Dome(df - Vector3.up * 0.25f, new Vector3(len * 0.28f, ht, len), yaw);
                }
            }

            // ---- Seg 6: timber rail fence on the drop side of the descent
            if (d >= PlateauToM + 200f && uphill != 0)
            {
                int drop = -uphill;
                Foot(route, i, drop * (RoadHalfWidth + ShoulderWidth + 1.1f), out var ff0, out var fo0);
                Foot(route, i + 1, drop * (RoadHalfWidth + ShoulderWidth + 1.1f), out var ff1, out _);
                foreach (float y in new[] { 0.55f, 1.02f })
                    mFence.Quad2(ff0 + Vector3.up * y, ff1 + Vector3.up * y, ff1 + Vector3.up * (y + 0.11f), ff0 + Vector3.up * (y + 0.11f));
                if (d - lastFence >= 3f)
                {
                    lastFence = d;
                    var along = route.Tangent[i]; along.y = 0f; along.Normalize();
                    mFence.Box(ff0 - Vector3.up * 0.3f, along * 0.07f, fo0 * 0.07f, 1.55f);
                }
                fence++;
            }
        }
        FlushTile(route.Length);

        BuildObservatory(group, route, stone, glass, dome, steel);
        BuildViewpoint(group, route, timber, steel);
        BuildCloudSea(group, route, cloud);

        if (cliffProbe > 0)
            Debug.Log($"[azora] ridge cliff probe (60 m): {cliffProbe} stations, side-hill {cliffGeo}, wet {cliffWet}, " +
                      $"uphill rise avg {upSum / cliffProbe:0.0} max {upMax:0.0} m, asymmetry avg {asymSum / cliffProbe:0.0} max {asymMax:0.0} m.");
        Debug.Log($"[azora] ridge+summit: cliff spans {cliffs}, guardrail spans {rails}, chevrons {chev}, " +
                  $"snow poles {poles}, fence spans {fence}.");
    }

    private static void Chevron(RMesh y, RMesh b, RMesh post, Vector3 foot, Vector3 facing, float pointDir)
    {
        facing.y = 0f; facing.Normalize();
        var right = Vector3.Cross(Vector3.up, facing).normalized;
        post.Box(foot - Vector3.up * 0.3f, right * 0.04f, facing * 0.04f, 1.55f);
        var c = foot + Vector3.up * 1.55f + facing * 0.06f;
        float w = 0.32f, h = 0.40f;
        y.Quad2(c - right * w - Vector3.up * h, c + right * w - Vector3.up * h, c + right * w + Vector3.up * h, c - right * w + Vector3.up * h);
        // two black chevron bars in front of the yellow board
        var f = facing * 0.02f;
        for (int k = -1; k <= 1; k += 2)
        {
            var tip = c + right * (pointDir * 0.14f + k * 0.13f) + f;
            var tail = c + right * (-pointDir * 0.14f + k * 0.13f) + f;
            b.Quad2(tail + Vector3.up * 0.32f, tip, tip + right * (-pointDir * 0.09f), tail + Vector3.up * 0.32f + right * (-pointDir * 0.09f));
            b.Quad2(tip, tail - Vector3.up * 0.32f, tail - Vector3.up * 0.32f + right * (-pointDir * 0.09f), tip + right * (-pointDir * 0.09f));
        }
    }

    /// <summary>2.2 m snow pole: alternating orange and black 25 cm bands.</summary>
    private static void SnowPole(RMesh orange, RMesh black, Vector3 foot)
    {
        for (int k = 0; k < 9; k++)
        {
            var c = foot + Vector3.up * (k * 0.25f - 0.1f);
            (k % 2 == 0 ? black : orange).Prism(c, 0.03f, 0.25f, 5);
        }
    }

    /// <summary>
    /// Sphinx-style summit observatory on a knoll beside the col: stone tower, terrace, glass
    /// drum, white dome, antenna mast.
    /// </summary>
    private static void BuildObservatory(Transform group, AzoraRoute route, Material stone, Material glass,
                                         Material domeMat, Material steel)
    {
        int ci = route.IndexAt(CpCol + 60f);
        var sflat = route.SideFlat(ci);
        float hl = Height(route.Position[ci].x - sflat.x * 50f, route.Position[ci].z - sflat.z * 50f);
        float hr = Height(route.Position[ci].x + sflat.x * 50f, route.Position[ci].z + sflat.z * 50f);
        int up = hr >= hl ? 1 : -1;
        Foot(route, ci, up * 48f, out var f, out _);
        var mS = new RMesh(); var mG = new RMesh(); var mD = new RMesh(); var mM = new RMesh();
        float g = f.y - 2f;
        var baseC = new Vector3(f.x, g, f.z);
        mS.Prism(baseC, 7.5f, 5f, 8);                                   // plinth
        mS.Prism(baseC + Vector3.up * 5f, 5.2f, 13f, 8, 4.8f);          // tower
        mS.Prism(baseC + Vector3.up * 18f, 6.6f, 0.5f, 8);              // terrace slab
        mG.Prism(baseC + Vector3.up * 18.5f, 4.4f, 3.6f, 16);           // glass drum
        mS.Prism(baseC + Vector3.up * 22.1f, 4.7f, 0.4f, 16);           // cornice
        mD.Dome(baseC + Vector3.up * 22.5f, new Vector3(4.3f, 3.8f, 4.3f), 0f, 20, 8);
        mM.Prism(baseC + Vector3.up * 26.2f, 0.08f, 7f, 6, 0.04f);      // mast
        // terrace railing posts
        for (int k = 0; k < 24; k++)
        {
            float a = k / 24f * Mathf.PI * 2f;
            mM.Prism(baseC + Vector3.up * 18.5f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 6.3f, 0.04f, 1.0f, 4);
        }
        FlushR(group, "AzoraD_Obs_Stone", mS, stone, true);
        FlushR(group, "AzoraD_Obs_Glass", mG, glass, true);
        FlushR(group, "AzoraD_Obs_Dome", mD, domeMat, true);
        FlushR(group, "AzoraD_Obs_Steel", mM, steel, false);
        Debug.Log($"[azora] summit observatory at route {CpCol + 60f:0} m, offset {up * 48f:0} m, base y {g:0}.");
    }

    /// <summary>Descent viewpoint: two benches and a railing on the drop side, facing the valley.</summary>
    private static void BuildViewpoint(Transform group, AzoraRoute route, Material timber, Material steel)
    {
        float d = Mathf.Min(route.Length - 50f, 21200f);
        int i = route.IndexAt(d);
        var s = route.SideFlat(i);
        float hl = Height(route.Position[i].x - s.x * 30f, route.Position[i].z - s.z * 30f);
        float hr = Height(route.Position[i].x + s.x * 30f, route.Position[i].z + s.z * 30f);
        int drop = hr < hl ? 1 : -1;
        var mT = new RMesh(); var mS = new RMesh();
        var along = route.Tangent[i]; along.y = 0f; along.Normalize();
        for (int k = -1; k <= 1; k += 2)
        {
            Foot(route, i + k * 2, drop * (RoadHalfWidth + ShoulderWidth + 2.6f), out var bf, out var bo);
            mT.Box(bf + Vector3.up * 0.42f, along * 0.9f, bo * 0.2f, 0.06f);              // seat
            mT.Box(bf + Vector3.up * 0.48f - bo * 0.18f, along * 0.9f, bo * 0.03f, 0.42f); // back
            mS.Box(bf - Vector3.up * 0.2f + along * 0.75f, along * 0.03f, bo * 0.18f, 0.64f);
            mS.Box(bf - Vector3.up * 0.2f - along * 0.75f, along * 0.03f, bo * 0.18f, 0.64f);
        }
        FlushR(group, "AzoraD_View_Timber", mT, timber, true);
        FlushR(group, "AzoraD_View_Steel", mS, steel, true);
    }

    /// <summary>
    /// A sea of cloud filling the valleys below the upper route. Built only where the NEAREST
    /// road is high (above 1,450 m) and the road itself is not within 350 m, so it is always seen
    /// from above - the lower route (900-1,400 m) never rides into or under it.
    /// </summary>
    private static void BuildCloudSea(Transform group, AzoraRoute route, Material cloud)
    {
        FieldBounds(route, out float x0, out float z0, out float x1, out float z1);
        const float Cell = 60f;
        const float Top = 1290f;
        int nx = Mathf.CeilToInt((x1 - x0) / Cell), nz = Mathf.CeilToInt((z1 - z0) / Cell);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        var idx = new int[nx + 1, nz + 1];
        for (int gx = 0; gx <= nx; gx++)
        for (int gz = 0; gz <= nz; gz++)
        {
            float x = x0 + gx * Cell, z = z0 + gz * Cell;
            float billow = Mathf.PerlinNoise(x * 0.0021f + 3f, z * 0.0021f + 8f) * 55f
                         + Mathf.PerlinNoise(x * 0.0073f + 1f, z * 0.0073f + 5f) * 18f;
            idx[gx, gz] = v.Count;
            v.Add(new Vector3(x, Top + billow, z));
            uv.Add(new Vector2(x, z) * 0.01f);
        }
        int quads = 0;
        for (int gx = 0; gx < nx; gx++)
        for (int gz = 0; gz < nz; gz++)
        {
            float cx = x0 + (gx + 0.5f) * Cell, cz = z0 + (gz + 0.5f) * Cell;
            if (RoadDist(cx, cz) < 350f || RoadElev(cx, cz) < 1450f) continue;
            if (Height(cx, cz) > Top + 40f) continue;   // ground pokes through: an island
            int a = idx[gx, gz], b = idx[gx, gz + 1], c = idx[gx + 1, gz], e = idx[gx + 1, gz + 1];
            t.Add(a); t.Add(b); t.Add(c); t.Add(c); t.Add(b); t.Add(e);
            quads++;
        }
        if (quads == 0) { Debug.Log("[azora] cloud sea: no qualifying valley cells."); return; }
        var go = AddMesh(group, "AzoraD_CloudSea", Finish("AzoraD_CloudSea", v.ToArray(), uv.ToArray(), t), cloud, collider: false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Debug.Log($"[azora] cloud sea: {quads} cells at y ~{Top:0} m.");
    }
}

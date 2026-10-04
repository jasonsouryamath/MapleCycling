using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Azora concept pass, work package B (WATER), winter. ChatGPT authored the hero GLBs and the first
// staging; copilot session 3 finished it and replaced the hand-placed layout with one SOLVED
// against the real landform. Height() is evaluated UNCARVED inside PrepareWater (WaterCarve is a
// no-op until _waterPrepared is set), so:
//  * each lake is a search over candidate basins that minimises excavation, with its level just
//    below the lowest rim point - a lake can never hover over a low shore;
//  * the river starts at a frozen fall at the head of a side ravine, runs under the rail viaduct,
//    then alongside route leg 4.3-4.8 km inside the fine corridor ribbon and away west into its
//    own gorge. Its level follows the natural ground (monotone downstream) and the carve is
//    min(natural, V-profile), so the terrain is only ever lowered where the water needs it;
//  * the viaduct spans the ravine ALONG THE CONTOUR, so both abutments land on the ground.
public static partial class AzoraHighlandsEnvironment
{
    private const string WaterModelDir = "Assets/Environment/AzoraHighlands/Models";

    // River layout, in route metres along the leg the river follows.
    private const float RiverConfluenceD = 4800f;  // ravine stream meets the leg here
    private const float RiverAlongToD = 4330f;     // leaves the road before the 4.25 km hairpin
    private const float RiverOffsetM = 52f;        // centreline off the road, inside the 90 m ribbon
    private const float RiverSideSign = -1f;       // the uphill (-SideFlat) side of that leg
    private const float RavineLengthM = 264f;
    private const float FallHeightM = 30f;
    private const float ViaductHalfLengthM = 62f;

    private struct WaterEllipse
    {
        public Vector2 centre;
        public Vector2 along;
        public Vector2 across;
        public float radiusAlong;
        public float radiusAcross;
        public float level;
    }

    private struct RiverKnot
    {
        public Vector3 p;          // y is the water level
        public float width;        // HALF width of the water
        public float bank;         // wall rise per metre beyond the water's edge
        public float bankNear;     // same, on the road side (gentle, so the rider sees the water)
        public Vector2 roadDir;    // unit vector towards the road; zero where the banks are symmetric
        public float routeMetres;
    }

    private static bool _waterPrepared;
    private static WaterEllipse _approachLake;
    private static WaterEllipse _island;
    private static WaterEllipse _descentLake;
    private static int _approachSide = 1;
    private static int _bridgeStation = -1;
    private static float _islandCrownY;
    private static RiverKnot[] _river = Array.Empty<RiverKnot>();
    private static int _ravineKnots;
    private static Rect _riverBounds;
    private static int _viaductKnot = -1;
    private static Vector3 _viaductAxis = Vector3.forward;
    private static Vector3 _viaductCentre;
    private static float _viaductDeckY;

    private static Vector3 WaterTangent(AzoraRoute r, int i)
    {
        var t = new Vector3(r.Tangent[i].x, 0f, r.Tangent[i].z);
        return t.sqrMagnitude < 0.001f ? Vector3.forward : t.normalized;
    }

    private static bool LakeValid(WaterEllipse e) => e.radiusAlong > 1f && e.radiusAcross > 1f;

    private static void PrepareWater(AzoraRoute route)
    {
        // Everything below samples the NATURAL ground: WaterCarve must stay a no-op until the end.
        _waterPrepared = false;

        _approachLake = SolveLake(route, "approach lake", 650f, 1400f, 50f,
            new[] { 20f, 30f, 45f, 65f },
            new[] { new Vector2(300f, 135f), new Vector2(240f, 115f), new Vector2(190f, 100f) },
            // At least 5.5 m below the road so the island bridge's arches clear the water.
            5.5f, 22f, 0.12f, 0.07f, 10f, out _approachSide);
        _island = default;
        _bridgeStation = -1;
        if (LakeValid(_approachLake))
        {
            // The bridge leaves the road where it runs lowest beside the lake (so the deck is only
            // a few metres over the water) and the island sits on that line; its crown is raised
            // to the deck, so the bridge lands on the town instead of flying over it.
            var e = _approachLake;
            float bestScore = float.MaxValue, fallbackScore = float.MaxValue;
            int fallbackStation = -1;
            for (int i = 0; i < route.Count; i += 2)
            {
                var q = route.Position[i];
                float n = EllipseN(e, q.x, q.z);
                if (n > 1.6f) continue;
                float score = q.y + 10f * Mathf.Max(0f, n - 1.2f);
                // Bridges leave a road roughly square to it: within ~35 deg of perpendicular.
                var t = WaterTangent(route, i);
                var toLake = (e.centre - new Vector2(q.x, q.z)).normalized;
                bool square = Mathf.Abs(t.x * toLake.x + t.z * toLake.y) < 0.57f;
                if (square && score < bestScore) { bestScore = score; _bridgeStation = i; }
                if (score < fallbackScore) { fallbackScore = score; fallbackStation = i; }
            }
            if (_bridgeStation < 0) _bridgeStation = fallbackStation;
            if (_bridgeStation >= 0)
            {
                var road = route.Position[_bridgeStation];
                var r2 = new Vector2(road.x, road.z);
                var c = e.centre + (r2 - e.centre) * (0.38f / Mathf.Max(0.5f, EllipseN(e, r2.x, r2.y)));
                _island = new WaterEllipse
                {
                    centre = c, along = e.along, across = e.across,
                    radiusAlong = Mathf.Min(64f, 0.24f * e.radiusAlong),
                    radiusAcross = Mathf.Min(48f, 0.36f * e.radiusAcross),
                    level = e.level,
                };
                _islandCrownY = Mathf.Clamp(road.y - 0.6f, e.level + 2.6f, e.level + 12f);
            }
        }

        _descentLake = SolveLake(route, "descent lake", 21600f, 23700f, 100f,
            new[] { 120f, 220f, 350f, 500f },
            new[] { new Vector2(420f, 230f), new Vector2(330f, 190f), new Vector2(260f, 150f) },
            // "A valley lake seen below the road": line of sight from the road dominates the score.
            20f, 220f, 0f, 0.02f, 45f, out _);

        PlanRiver(route);
        _waterPrepared = true;
    }

    // =================================================================== lake solve

    /// <summary>
    /// Searches ellipses beside the route between d0 and d1 (both sides, several radii and road
    /// gaps). Level = lowest rim sample - 1 m, so the shore is dry all round by construction.
    /// Score = mean excavation of the interior + nearBonus * gap - sizeBonus * radiusAlong
    ///         - visWeight * (fraction of road viewpoints that can see the water).
    /// </summary>
    private static WaterEllipse SolveLake(AzoraRoute route, string label, float d0, float d1, float dStep,
                                          float[] gaps, Vector2[] radii, float minBelow, float maxBelow,
                                          float nearBonus, float sizeBonus, float visWeight, out int sideSign)
    {
        var best = default(WaterEllipse);
        float bestScore = float.MaxValue;
        sideSign = 1;
        var fallback = default(WaterEllipse);
        float fallbackScore = float.MaxValue;
        int fallbackSide = 1;

        var near = new List<int>();
        for (int i = 0; i < route.Count; i += 2)
            if (route.Distance[i] > d0 - 1500f && route.Distance[i] < d1 + 1500f) near.Add(i);
        var rim = new List<Vector2>(48);
        var inner = new List<Vector2>(29);
        int[] sides = { -1, 1 };

        for (float d = d0; d <= d1 + 0.1f; d += dStep)
        {
            int i = route.IndexAt(d);
            var t = WaterTangent(route, i);
            var s = route.SideFlat(i);
            var al = new Vector2(t.x, t.z).normalized;
            var ac = new Vector2(s.x, s.z).normalized;
            var p = new Vector2(route.Position[i].x, route.Position[i].z);
            foreach (int sgn in sides)
            foreach (var r in radii)
            foreach (float gap in gaps)
            {
                var e = new WaterEllipse
                {
                    centre = p + ac * (sgn * (r.y + gap)), along = al, across = ac,
                    radiusAlong = r.x, radiusAcross = r.y,
                };
                float minN = float.MaxValue, roadY = float.MaxValue;
                foreach (int k in near)
                {
                    var q = route.Position[k];
                    float n = EllipseN(e, q.x, q.z);
                    if (n < minN) minN = n;
                    if (n < 1.8f && q.y < roadY) roadY = q.y;
                }
                if (minN < 1.10f) continue;                       // the road never enters the shore band
                if (roadY == float.MaxValue) roadY = route.Position[i].y;

                EllipseRing(e, 1.00f, 24, rim, false);
                EllipseRing(e, 1.18f, 24, rim, true);
                float rimMin = float.MaxValue;
                foreach (var q in rim) rimMin = Mathf.Min(rimMin, Height(q.x, q.y));
                e.level = rimMin - 1f;

                inner.Clear();
                inner.Add(e.centre);
                EllipseRing(e, 0.40f, 12, inner, true);
                EllipseRing(e, 0.75f, 16, inner, true);
                float exc = 0f;
                foreach (var q in inner) exc += Mathf.Max(0f, Height(q.x, q.y) - e.level);
                exc /= inner.Count;

                float score = exc + nearBonus * gap - sizeBonus * r.x;
                float below = roadY - e.level;
                bool inWindow = below >= minBelow && below <= maxBelow;
                // Line of sight is the expensive term: only evaluate it for candidates that could win.
                if (visWeight > 0f && score - visWeight < (inWindow ? bestScore : fallbackScore))
                    score -= visWeight * LakeVisibility(route, e, d0, d1);
                if (inWindow)
                {
                    if (score < bestScore) { bestScore = score; best = e; sideSign = sgn; }
                }
                else if (score < fallbackScore) { fallbackScore = score; fallback = e; fallbackSide = sgn; }
            }
        }

        if (bestScore == float.MaxValue)
        {
            Debug.LogWarning($"[azora-water] {label}: no basin met the {minBelow:0}-{maxBelow:0} m below-road window; " +
                             "using the least-excavation candidate instead.");
            best = fallback; sideSign = fallbackSide; bestScore = fallbackScore;
        }
        if (LakeValid(best))
            Debug.Log($"[azora-water] {label}: centre ({best.centre.x:0},{best.centre.y:0}), " +
                      $"{best.radiusAlong * 2f:0}x{best.radiusAcross * 2f:0} m, route side {sideSign:+0;-0}, " +
                      $"level {best.level:0.0}, score {bestScore:0.0}");
        else
            Debug.LogWarning($"[azora-water] {label}: no candidate kept the road clear - lake skipped.");
        return best;
    }

    /// <summary>
    /// Fraction of (road viewpoint, water point) pairs with a clear sightline over the natural
    /// ground. Eyes: route stations every 150 m from d0-300 to d1+300 within 1.1 km, 2.5 m up.
    /// Samples inside the future basin (n &lt; 1.3) are skipped - that ground will be carved away.
    /// </summary>
    private static float LakeVisibility(AzoraRoute route, WaterEllipse e, float d0, float d1)
    {
        int tests = 0, clear = 0;
        var targets = new[]
        {
            e.centre,
            e.centre + e.along * (0.55f * e.radiusAlong), e.centre - e.along * (0.55f * e.radiusAlong),
            e.centre + e.across * (0.55f * e.radiusAcross), e.centre - e.across * (0.55f * e.radiusAcross),
        };
        for (float d = d0 - 300f; d <= d1 + 300f; d += 150f)
        {
            if (d < 0f || d > route.Length) continue;
            var eye = route.Position[route.IndexAt(d)] + Vector3.up * 2.5f;
            if (Vector2.Distance(new Vector2(eye.x, eye.z), e.centre) > 1100f) continue;
            foreach (var t2 in targets)
            {
                tests++;
                var tgt = new Vector3(t2.x, e.level + 0.3f, t2.y);
                bool ok = true;
                for (int s = 1; s < 16 && ok; s++)
                {
                    var p = Vector3.Lerp(eye, tgt, s / 16f);
                    if (EllipseN(e, p.x, p.z) < 1.3f) continue;
                    if (Height(p.x, p.z) > p.y + 0.5f) ok = false;
                }
                if (ok) clear++;
            }
        }
        return tests == 0 ? 0f : clear / (float)tests;
    }

    private static void EllipseRing(WaterEllipse e, float n, int count, List<Vector2> into, bool append)
    {
        if (!append) into.Clear();
        for (int j = 0; j < count; j++)
        {
            float a = j * Mathf.PI * 2f / count;
            into.Add(e.centre + e.along * (Mathf.Cos(a) * e.radiusAlong * n) +
                     e.across * (Mathf.Sin(a) * e.radiusAcross * n));
        }
    }

    // =================================================================== river plan

    private static Vector2 NaturalGradient(float x, float z)
    {
        const float e = 6f;
        return new Vector2((Height(x + e, z) - Height(x - e, z)) / (2f * e),
                           (Height(x, z + e) - Height(x, z - e)) / (2f * e));
    }

    /// <summary>Plan distance to the route, ignoring stations within skipRange of skipD.</summary>
    private static float RouteClearance(AzoraRoute r, float x, float z, float skipD, float skipRange)
    {
        float best = float.MaxValue;
        for (int i = 0; i < r.Count; i += 3)
        {
            if (Mathf.Abs(r.Distance[i] - skipD) < skipRange) continue;
            float dx = r.Position[i].x - x, dz = r.Position[i].z - z;
            best = Mathf.Min(best, dx * dx + dz * dz);
        }
        return Mathf.Sqrt(best);
    }

    private static void PlanRiver(AzoraRoute route)
    {
        var pts = new List<Vector2>();
        var widths = new List<float>();
        var banks = new List<float>();
        var metres = new List<float>();
        var nearBanks = new List<float>();
        var roadDirs = new List<Vector2>();
        var roadCaps = new List<float>();   // leg knots: water at least 1.2 m under the road beside it

        // 1. The ravine: walked up the natural fall line from the confluence, stored head first.
        int ci = route.IndexAt(RiverConfluenceD);
        var cp = route.Position[ci] + route.SideFlat(ci) * (RiverSideSign * RiverOffsetM);
        var trib = new List<Vector2> { new Vector2(cp.x, cp.z) };
        var walk = trib[0];
        int steps = Mathf.RoundToInt(RavineLengthM / 24f);
        for (int s = 0; s < steps; s++)
        {
            var g = NaturalGradient(walk.x, walk.y);
            if (g.sqrMagnitude < 1e-8f) break;
            var q = walk + g.normalized * 24f;
            if (RouteClearance(route, q.x, q.y, RiverConfluenceD, 700f) < 170f) break;
            walk = q;
            trib.Add(walk);
        }
        trib.Reverse();
        int nt = trib.Count - 1;
        for (int k = 0; k < nt; k++)
        {
            pts.Add(trib[k]);
            widths.Add(5.5f + 2.5f * k / Mathf.Max(1, nt));
            banks.Add(k == 0 ? 1.8f : 0.55f);   // steep head wall behind the fall, V walls below
            nearBanks.Add(banks[banks.Count - 1]);
            roadDirs.Add(Vector2.zero);
            roadCaps.Add(float.MaxValue);
            metres.Add(RiverConfluenceD + (nt - k) * 24f);
        }
        _ravineKnots = nt;

        // 2. Alongside the leg, downstream.
        for (float d = RiverConfluenceD; d >= RiverAlongToD - 0.1f; d -= 40f)
        {
            int i = route.IndexAt(d);
            float off = RiverOffsetM + 7f * Mathf.Sin(d * 0.009f);
            var c = route.Position[i] + route.SideFlat(i) * (RiverSideSign * off);
            pts.Add(new Vector2(c.x, c.z));
            widths.Add(8f + 1.8f * Mathf.Sin(d * 0.013f));
            banks.Add(0.6f);
            // The road side is a gentle meadow down to the water: the ground rises away from the
            // road on this leg, so a symmetric channel would hide the river behind its near bank.
            nearBanks.Add(0.12f);
            var toRoad = -route.SideFlat(i) * RiverSideSign;
            roadDirs.Add(new Vector2(toRoad.x, toRoad.z).normalized);
            roadCaps.Add(route.Position[i].y - 1.2f);
            metres.Add(d);
        }

        // 3. The tail: away from the road into its own deepening gorge.
        {
            int i = route.IndexAt(RiverAlongToD);
            var t = WaterTangent(route, i);
            var s = route.SideFlat(i) * RiverSideSign;
            var dir = new Vector2(s.x - t.x, s.z - t.z).normalized;
            var last = pts[pts.Count - 1];
            for (int k = 1; k <= 11; k++)
            {
                var q = last + dir * (56f * k);
                float w = 9f + 0.4f * k;
                if (RouteClearance(route, q.x, q.y, -1e6f, 0f) < 55f + w) break;
                pts.Add(q);
                widths.Add(w);
                banks.Add(0.5f);
                nearBanks.Add(0.5f);
                roadDirs.Add(Vector2.zero);
                roadCaps.Add(float.MaxValue);
                metres.Add(RiverAlongToD - 56f * k);
            }
        }

        // Levels: 1.6 m under the lowest bank, the ravine graded from the fall's plunge pool to
        // the confluence (so the gorge deepens upslope), then monotone downstream at >= 1.5 %.
        int n = pts.Count;
        var lv = new float[n];
        for (int k = 0; k < n; k++)
        {
            var f = (k + 1 < n ? pts[k + 1] - pts[k] : pts[k] - pts[k - 1]).normalized;
            var sd = new Vector2(-f.y, f.x) * (widths[k] + 2f);
            lv[k] = Mathf.Min(Height(pts[k].x, pts[k].y),
                    Mathf.Min(Height(pts[k].x + sd.x, pts[k].y + sd.y),
                              Height(pts[k].x - sd.x, pts[k].y - sd.y))) - 1.6f;
            lv[k] = Mathf.Min(lv[k], roadCaps[k]);
        }
        if (_ravineKnots > 0)
        {
            float headLevel = Height(pts[0].x, pts[0].y) - FallHeightM;
            for (int k = 0; k < _ravineKnots; k++)
                lv[k] = Mathf.Min(lv[k], Mathf.Lerp(headLevel, lv[_ravineKnots], k / (float)_ravineKnots));
        }
        for (int k = 1; k < n; k++)
            lv[k] = Mathf.Min(lv[k], lv[k - 1] - 0.015f * Vector2.Distance(pts[k], pts[k - 1]));

        _river = new RiverKnot[n];
        var min = pts[0];
        var max = pts[0];
        for (int k = 0; k < n; k++)
        {
            _river[k] = new RiverKnot
            {
                p = new Vector3(pts[k].x, lv[k], pts[k].y), width = widths[k], bank = banks[k],
                bankNear = nearBanks[k], roadDir = roadDirs[k], routeMetres = metres[k],
            };
            min = Vector2.Min(min, pts[k]);
            max = Vector2.Max(max, pts[k]);
        }
        const float Reach = 170f;
        _riverBounds = Rect.MinMaxRect(min.x - Reach, min.y - Reach, max.x + Reach, max.y + Reach);

        // The viaduct: a ravine knot whose gorge is ~15 m deep, at least three knots (~70 m) below
        // the fall so the fall is not hidden behind it, deck along the contour.
        _viaductKnot = -1;
        float bestScore = float.MaxValue;
        for (int k = Mathf.Min(3, _ravineKnots - 1); k < _ravineKnots; k++)
        {
            if (k < 1) continue;
            float score = Mathf.Abs(Height(pts[k].x, pts[k].y) - lv[k] - 15f);
            if (score < bestScore) { bestScore = score; _viaductKnot = k; }
        }
        if (_viaductKnot >= 0)
        {
            var q = pts[_viaductKnot];
            var g = NaturalGradient(q.x, q.y);
            var axis = g.sqrMagnitude > 1e-8f ? new Vector2(-g.y, g.x).normalized : Vector2.right;
            var e0 = q - axis * ViaductHalfLengthM;
            var e1 = q + axis * ViaductHalfLengthM;
            _viaductDeckY = Mathf.Min(Height(e0.x, e0.y), Height(e1.x, e1.y)) + 1.5f;
            _viaductAxis = new Vector3(axis.x, 0f, axis.y);
            _viaductCentre = new Vector3(q.x, lv[_viaductKnot], q.y);
            Debug.Log($"[azora-water] viaduct over ravine knot {_viaductKnot}: deck {_viaductDeckY:0.0}, " +
                      $"stream {lv[_viaductKnot]:0.0}, road clearance {RouteClearance(route, e0.x, e0.y, -1e6f, 0f):0}/" +
                      $"{RouteClearance(route, e1.x, e1.y, -1e6f, 0f):0} m");
        }
        Debug.Log($"[azora-water] river: {n} knots ({_ravineKnots} ravine), level {lv[0]:0.0} -> {lv[n - 1]:0.0}, " +
                  $"fall {(_ravineKnots > 0 ? FallHeightM : 0f):0} m at ({pts[0].x:0},{pts[0].y:0}); " +
                  $"viaduct at ({_viaductCentre.x:0},{_viaductCentre.z:0})");
    }

    // =================================================================== carve + queries

    private static float EllipseN(WaterEllipse e, float x, float z)
    {
        var q = new Vector2(x, z) - e.centre;
        float a = Vector2.Dot(q, e.along) / Mathf.Max(0.01f, e.radiusAlong);
        float b = Vector2.Dot(q, e.across) / Mathf.Max(0.01f, e.radiusAcross);
        return Mathf.Sqrt(a * a + b * b);
    }

    private static float CarveLake(WaterEllipse e, float x, float z, float h, float depth)
    {
        float n = EllipseN(e, x, z);
        if (n >= 1.34f) return h;
        float influence = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.34f, 0.94f, n));
        float bed = e.level - depth + Mathf.SmoothStep(0f, depth + 1.2f,
            Mathf.InverseLerp(0.72f, 1.18f, n));
        return Mathf.Lerp(h, Mathf.Min(h, bed), influence);
    }

    /// <summary>Nearest river segment; <paramref name="bank"/> is already the road-side (gentle) or far-side slope.</summary>
    private static bool ClosestRiver(float x, float z, out float distance, out float width,
                                     out float level, out float bank)
    {
        distance = float.MaxValue;
        width = 0f;
        level = 0f;
        bank = 1f;
        if (_river == null || _river.Length < 2) return false;
        var q = new Vector2(x, z);
        bool found = false;
        for (int i = 0; i < _river.Length - 1; i++)
        {
            var a = new Vector2(_river[i].p.x, _river[i].p.z);
            var b = new Vector2(_river[i + 1].p.x, _river[i + 1].p.z);
            var ab = b - a;
            float t = ab.sqrMagnitude < 0.001f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
            var closest = a + ab * t;
            float d = Vector2.Distance(q, closest);
            if (d >= distance) continue;
            distance = d;
            width = Mathf.Lerp(_river[i].width, _river[i + 1].width, t);
            level = Mathf.Lerp(_river[i].p.y, _river[i + 1].p.y, t);
            var toRoad = Vector2.Lerp(_river[i].roadDir, _river[i + 1].roadDir, t);
            bool near = toRoad.sqrMagnitude > 0.01f && Vector2.Dot(q - closest, toRoad) > 0f;
            bank = near ? Mathf.Lerp(_river[i].bankNear, _river[i + 1].bankNear, t)
                        : Mathf.Lerp(_river[i].bank, _river[i + 1].bank, t);
            found = true;
        }
        return found;
    }

    private static float WaterCarve(float x, float z, float h)
    {
        if (!_waterPrepared) return h;
        if (LakeValid(_approachLake)) h = CarveLake(_approachLake, x, z, h, 5.8f);
        if (LakeValid(_descentLake)) h = CarveLake(_descentLake, x, z, h, 8.5f);

        if (_river.Length >= 2 && _riverBounds.Contains(new Vector2(x, z)) &&
            ClosestRiver(x, z, out float d, out float w, out float level, out float bank))
        {
            // Bed 1.8 m down mid-stream, 0.6 m at the edge, then a wall rising at 'bank' per metre.
            float prof = d < w
                ? level - 1.8f + 1.2f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(w * 0.55f, w, d))
                : level - 0.6f + (d - w) * bank;
            if (prof < h) h = prof;
        }

        // Restore a shaped island after the basin has been carved. The raised core is part of the
        // same continuous Height function, so the town can never float. Crown near road level so
        // the bridge deck lands on it.
        if (LakeValid(_island))
        {
            float islandN = EllipseN(_island, x, z);
            if (islandN < 1.16f)
            {
                float shore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 1.16f, islandN));
                float crown = Mathf.Lerp(_islandCrownY, _approachLake.level + 0.7f, shore);
                h = Mathf.Max(h, crown);
            }
        }
        return h;
    }

    /// <summary>
    /// Keep-out query for scatter passes: true inside a lake (island included - the town owns it),
    /// the river, or the viaduct's footprint, widened by <paramref name="margin"/> metres.
    /// </summary>
    private static bool InWater(float x, float z, float margin = 0f)
    {
        if (!_waterPrepared) return false;
        if (LakeValid(_approachLake) &&
            EllipseN(_approachLake, x, z) <= 1f + margin / Mathf.Min(_approachLake.radiusAlong, _approachLake.radiusAcross))
            return true;
        if (LakeValid(_descentLake) &&
            EllipseN(_descentLake, x, z) <= 1f + margin / Mathf.Min(_descentLake.radiusAlong, _descentLake.radiusAcross))
            return true;
        if (_river.Length >= 2 && _riverBounds.Contains(new Vector2(x, z)) &&
            ClosestRiver(x, z, out float d, out float w, out _, out _) && d <= w + margin)
            return true;
        if (_viaductKnot >= 0)
        {
            var q = new Vector2(x - _viaductCentre.x, z - _viaductCentre.z);
            float a = q.x * _viaductAxis.x + q.y * _viaductAxis.z;
            float b = Mathf.Abs(q.x * _viaductAxis.z - q.y * _viaductAxis.x);
            if (Mathf.Abs(a) < ViaductHalfLengthM + 4f + margin && b < 7f + margin) return true;
        }
        return false;
    }

    private static Material WaterSurfaceMaterial(string name, Color colour, float gloss, float spec)
    {
        var mat = CelMaterial(name, colour, gloss: gloss, spec: spec, rim: 0.50f,
                              shade: new Color(0.22f, 0.38f, 0.52f, 1f));
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        return mat;
    }

    private static Mesh EllipseDiscMesh(string name, WaterEllipse e, float scale, float y,
                                        int segments = 96, int rings = 5, float wave = 0f)
    {
        var v = new List<Vector3>(1 + segments * rings);
        var uv = new List<Vector2>(1 + segments * rings);
        var tri = new List<int>(segments * (rings * 6));
        v.Add(new Vector3(e.centre.x, y, e.centre.y));
        uv.Add(new Vector2(0.5f, 0.5f));
        for (int r = 1; r <= rings; r++)
        {
            float rr = scale * r / rings;
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                var q = e.along * (Mathf.Cos(a) * e.radiusAlong * rr) +
                        e.across * (Mathf.Sin(a) * e.radiusAcross * rr);
                float wy = y + wave * Mathf.Sin(a * 5f + rr * 9f) * Mathf.Sin(rr * Mathf.PI);
                v.Add(new Vector3(e.centre.x + q.x, wy, e.centre.y + q.y));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * rr * 0.5f, 0.5f + Mathf.Sin(a) * rr * 0.5f));
            }
        }
        for (int s = 0; s < segments; s++)
        {
            int n = (s + 1) % segments;
            tri.Add(0); tri.Add(1 + n); tri.Add(1 + s);
        }
        for (int r = 1; r < rings; r++)
        for (int s = 0; s < segments; s++)
        {
            int n = (s + 1) % segments;
            int i0 = 1 + (r - 1) * segments + s;
            int i1 = 1 + (r - 1) * segments + n;
            int o0 = 1 + r * segments + s;
            int o1 = 1 + r * segments + n;
            tri.Add(i0); tri.Add(i1); tri.Add(o0);
            tri.Add(i1); tri.Add(o1); tri.Add(o0);
        }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh EllipseRingMesh(string name, WaterEllipse e, float inner, float outer,
                                        float y, int segments = 112, float drift = 0f)
    {
        var v = new List<Vector3>(segments * 2);
        var uv = new List<Vector2>(segments * 2);
        var tri = new List<int>(segments * 6);
        for (int s = 0; s < segments; s++)
        {
            float a = s / (float)segments * Mathf.PI * 2f;
            float wobble = 1f + Mathf.Sin(a * 7f + e.centre.x * 0.001f) * drift;
            foreach (float r in new[] { inner, outer * wobble })
            {
                var q = e.along * (Mathf.Cos(a) * e.radiusAlong * r) +
                        e.across * (Mathf.Sin(a) * e.radiusAcross * r);
                v.Add(new Vector3(e.centre.x + q.x, y + Mathf.Sin(a * 9f) * drift * 0.22f,
                                  e.centre.y + q.y));
                uv.Add(new Vector2(s / (float)segments, r == inner ? 0f : 1f));
            }
        }
        for (int s = 0; s < segments; s++)
        {
            int n = (s + 1) % segments;
            int a = s * 2, b = n * 2;
            tri.Add(a); tri.Add(b); tri.Add(a + 1);
            tri.Add(b); tri.Add(b + 1); tri.Add(a + 1);
        }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh RiverMesh(string name, float widthScale, float yLift)
    {
        var v = new List<Vector3>(_river.Length * 2);
        var uv = new List<Vector2>(_river.Length * 2);
        var tri = new List<int>((_river.Length - 1) * 6);
        float texV = 0f;
        for (int i = 0; i < _river.Length; i++)
        {
            Vector3 flow = i == 0 ? _river[1].p - _river[0].p :
                           i == _river.Length - 1 ? _river[i].p - _river[i - 1].p :
                           _river[i + 1].p - _river[i - 1].p;
            flow.y = 0f; flow.Normalize();
            var side = Vector3.Cross(Vector3.up, flow).normalized;
            float w = Mathf.Max(0.4f, _river[i].width * widthScale);
            float ripple = Mathf.Sin(_river[i].routeMetres * 0.13f) * 0.08f;
            var c = _river[i].p + Vector3.up * (yLift + ripple);
            v.Add(c - side * w); v.Add(c + side * w);
            uv.Add(new Vector2(0f, texV / 8f)); uv.Add(new Vector2(1f, texV / 8f));
            if (i > 0) texV += Vector3.Distance(_river[i - 1].p, _river[i].p);
        }
        for (int i = 0; i < _river.Length - 1; i++)
        {
            int a = i * 2, b = a + 2;
            tri.Add(a); tri.Add(b); tri.Add(a + 1);
            tri.Add(b); tri.Add(b + 1); tri.Add(a + 1);
        }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // Non-water materials must NOT contain "water" in their names: WP-E's winter pass refuses snow
    // on any material whose name does (AzoraHighlands.Winter.cs), and a viaduct, wall or fence
    // with no snow coat would be the one bare thing in the valley.
    private static Material WaterModelRole(string role)
    {
        role = role ?? string.Empty;
        if (role.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0)
            return CelMaterial("Azora_WpB_Snow", new Color(0.94f, 0.975f, 1.0f), 0.26f, 0.24f, 0.34f,
                               shade: new Color(0.55f, 0.70f, 0.88f, 1f));
        if (role.IndexOf("Ice", StringComparison.OrdinalIgnoreCase) >= 0)
            return WaterSurfaceMaterial("Azora_Water_Ice", new Color(0.74f, 0.86f, 0.92f), 0.84f, 0.68f);
        if (role.IndexOf("Flow", StringComparison.OrdinalIgnoreCase) >= 0)   // cold water between ice runnels
            return WaterSurfaceMaterial("Azora_Water_Fall", new Color(0.25f, 0.54f, 0.63f), 0.78f, 0.57f);
        if (role.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0)
            return CelMaterial("Azora_Island_WindowWarm", new Color(1.0f, 0.49f, 0.15f), 0.50f, 0.34f, 0.55f);
        if (role.IndexOf("Timber", StringComparison.OrdinalIgnoreCase) >= 0)
            return CelMaterial("Azora_WpB_Timber", new Color(0.28f, 0.19f, 0.12f), 0.10f, 0.06f, 0.10f,
                               shade: GroundShade);
        if (role.IndexOf("Steel", StringComparison.OrdinalIgnoreCase) >= 0)
            return CelMaterial("Azora_Viaduct_Steel", new Color(0.16f, 0.19f, 0.22f), 0.42f, 0.30f, 0.16f,
                               shade: GroundShade);
        if (role.IndexOf("Roof", StringComparison.OrdinalIgnoreCase) >= 0)
            return CelMaterial("Azora_Island_Roof", new Color(0.24f, 0.19f, 0.17f), 0.14f, 0.08f, 0.12f,
                               shade: GroundShade);
        if (role.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0)
            return CelMaterial("Azora_Island_Wall", new Color(0.76f, 0.77f, 0.74f), 0.08f, 0.05f, 0.10f,
                               shade: GroundShade);
        if (role.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0)
            return CelMaterial("Azora_WpB_RockDark", new Color(0.33f, 0.35f, 0.37f), 0.10f, 0.06f, 0.10f,
                               texture: AzoraTexture("Azora_Stone_Albedo.png"), shade: GroundShade);
        return CelMaterial("Azora_WpB_Stone", new Color(0.46f, 0.48f, 0.49f), 0.09f, 0.06f, 0.10f,
                           texture: AzoraTexture("Azora_Stone_Albedo.png"), shade: GroundShade);
    }

    private static void StageWaterGlb(Transform parent, string model, Matrix4x4 place, string label)
    {
        var src = Glb(WaterModelDir, model);
        if (!src.ok) return;
        var batch = new TileBatch();
        batch.Add(src, place, WaterModelRole);
        batch.Flush(parent, label, 0);
    }

    private static void BuildLakeGeometry(Transform group, WaterEllipse lake, string stem, bool descent)
    {
        // Authored darker than it should look (see WaterMaterial): winter lake water is mostly
        // reflected sky, and a bright cyan disc reads as a swimming pool.
        var water = WaterSurfaceMaterial(stem + "_OpenWater",
            descent ? new Color(0.10f, 0.19f, 0.26f) : new Color(0.08f, 0.24f, 0.30f), 0.90f, 0.72f);
        var ice = WaterSurfaceMaterial(stem + "_IceRim", new Color(0.52f, 0.68f, 0.76f), 0.78f, 0.58f);
        var snow = WaterModelRole("Snow");
        AddMesh(group, stem + " Open Water", EllipseDiscMesh(stem + "_OpenWater", lake, 0.80f,
                    lake.level + 0.05f, 112, 6, 0.10f), water, false);
        AddMesh(group, stem + " Ice Rim", EllipseRingMesh(stem + "_IceRim", lake, 0.78f, 1.0f,
                    lake.level + 0.10f, 128, 0.018f), ice, false);
        AddMesh(group, stem + " Snow Shore", EllipseRingMesh(stem + "_SnowShore", lake, 0.985f, 1.075f,
                    lake.level + 0.19f, 128, 0.045f), snow, false);

        if (descent)
        {
            // Wind-broken floes over the sunset lake, kept separate from the continuous rim.
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.39996f;
                var floe = lake;
                var q = lake.along * (Mathf.Cos(a) * lake.radiusAlong * (0.18f + 0.07f * (i % 3))) +
                        lake.across * (Mathf.Sin(a) * lake.radiusAcross * 0.28f);
                floe.centre += q;
                floe.radiusAlong = 34f + (i % 3) * 13f;
                floe.radiusAcross = 18f + (i % 2) * 10f;
                AddMesh(group, $"{stem} Ice Floe {i}", EllipseDiscMesh($"{stem}_IceFloe_{i}", floe,
                    1f, lake.level + 0.15f, 18, 2, 0.02f), ice, false);
            }
        }
    }

    /// <summary>
    /// A coursed stone parapet on the lake side of the road, wherever the approach lake comes close
    /// (ellipse n < 1.6), with a gap where the island bridge leaves the road.
    /// </summary>
    private static void BuildLakeWall(Transform group, AzoraRoute route, int bridgeStation)
    {
        if (!LakeValid(_approachLake)) return;
        var sv = new List<Vector3>(); var suv = new List<Vector2>(); var st = new List<int>(); var sc = new List<Color>();
        var nv = new List<Vector3>(); var nuv = new List<Vector2>(); var nt = new List<int>(); var nc = new List<Color>();
        float offset = (RoadHalfWidth + ShoulderWidth + 1.45f) * _approachSide;
        float bridgeD = bridgeStation >= 0 ? route.Distance[bridgeStation] : -1e6f;
        int pieces = 0;
        for (int i = 0; i + 2 < route.Count; i += 2)
        {
            int j = i + 2;
            var a = route.Position[i]; var b = route.Position[j];
            if (EllipseN(_approachLake, a.x, a.z) > 1.6f || EllipseN(_approachLake, b.x, b.z) > 1.6f) continue;
            if (Mathf.Abs(route.Distance[i] - bridgeD) < 9f) continue;
            a += route.SideFlat(i) * offset;
            b += route.SideFlat(j) * offset;
            var along = b - a; along.y = 0f;
            if (along.sqrMagnitude < 0.01f) continue;
            var mid = (a + b) * 0.5f; mid.y = Mathf.Min(a.y, b.y) - 0.25f;
            var across = Vector3.Cross(Vector3.up, along.normalized);
            BoxO(sv, suv, st, sc, mid, across * 0.34f, along * 0.52f, 1.35f);
            BoxO(nv, nuv, nt, nc, mid + Vector3.up * 1.37f, across * 0.40f, along * 0.52f, 0.20f, true);
            pieces++;
        }
        if (pieces == 0) return;
        AddMesh(group, "Lake Road Stone Wall", Finish("Azora_WpB_LakesideWall", sv.ToArray(), suv.ToArray(), st),
                WaterModelRole("Stone"), false);
        AddMesh(group, "Lake Road Wall Snow", Finish("Azora_WpB_LakesideWallSnow", nv.ToArray(), nuv.ToArray(), nt),
                WaterModelRole("Snow"), false);
    }

    /// <summary>Timber rail fence on the river side of the road, along the riverside stretch.</summary>
    private static void BuildRiverFence(Transform group, AzoraRoute route)
    {
        var woodV = new List<Vector3>(); var woodUv = new List<Vector2>(); var woodT = new List<int>(); var woodC = new List<Color>();
        var snowV = new List<Vector3>(); var snowUv = new List<Vector2>(); var snowT = new List<int>(); var snowC = new List<Color>();
        float offset = (RoadHalfWidth + ShoulderWidth + 1.7f) * RiverSideSign;
        for (float d = RiverAlongToD - 60f; d < RiverConfluenceD + 60f; d += 10f)
        {
            int i = route.IndexAt(d); int j = route.IndexAt(Mathf.Min(d + 10f, route.Length));
            var side = route.SideFlat(i);
            var p = route.Position[i] + side * offset;
            p.y = Height(p.x, p.z);
            BoxO(woodV, woodUv, woodT, woodC, p - Vector3.up * 0.15f,
                 WaterTangent(route, i) * 0.10f, side * 0.10f, 1.65f);
            BoxO(snowV, snowUv, snowT, snowC, p + Vector3.up * 1.55f,
                 WaterTangent(route, i) * 0.15f, side * 0.15f, 0.18f, true);
            var q = route.Position[j] + route.SideFlat(j) * offset;
            q.y = Height(q.x, q.z);
            var along = q - p; along.y = 0f;
            var mid = (p + q) * 0.5f;
            foreach (float y in new[] { 0.62f, 1.20f })
                BoxO(woodV, woodUv, woodT, woodC, mid + Vector3.up * y,
                     side * 0.065f, along * 0.5f, 0.13f, true);
            BoxO(snowV, snowUv, snowT, snowC, mid + Vector3.up * 1.31f,
                 side * 0.09f, along * 0.5f, 0.10f, true);
        }
        AddMesh(group, "River Timber Fence", Finish("Azora_WpB_RiverFence", woodV.ToArray(), woodUv.ToArray(), woodT),
                WaterModelRole("Timber"), false);
        AddMesh(group, "River Fence Snow", Finish("Azora_WpB_RiverFenceSnow", snowV.ToArray(), snowUv.ToArray(), snowT),
                WaterModelRole("Snow"), false);
    }

    private static void BuildRiverHeroes(Transform group)
    {
        // Snow-capped boulders along both edges; skip the plunge pool (knot 0).
        var boulderA = Glb(WaterModelDir, "Azora_Water_Boulder_A");
        var boulderB = Glb(WaterModelDir, "Azora_Water_Boulder_B");
        var batch = new TileBatch();
        for (int i = 1; i < _river.Length; i++)
        {
            Vector3 flow = _river[i].p - _river[i - 1].p;
            flow.y = 0f; flow.Normalize();
            var side = Vector3.Cross(Vector3.up, flow).normalized;
            for (int k = 0; k < 2; k++)
            {
                float sign = k == 0 ? -1f : 1f;
                float r = 0.26f + H01(i * 19f + k, 83.1f) * 0.26f;
                var p = _river[i].p + side * sign * (_river[i].width * (0.62f + H01(i, k + 37f) * 0.40f));
                p.y = _river[i].p.y - 0.6f;
                float yaw = H01(i * 3f, k + 91f) * 360f;
                batch.Add(((i + k) & 1) == 0 ? boulderA : boulderB,
                          Matrix4x4.TRS(p, Quaternion.Euler(0f, yaw, 0f), Vector3.one * r), WaterModelRole);
            }
        }
        batch.Flush(group, "WaterBoulders", 0);

        // Crags: dark, snow-capped rock outcrops stepping down the ravine and tail-gorge walls. On
        // a snowfield a carved gorge is white on white; exposed rock on its steep walls is what
        // draws the notch (and is what a real gorge shows - snow does not hold on the rock).
        var crags = new TileBatch();
        int cragCount = 0;
        for (int i = 1; i < _river.Length; i++)
        {
            bool ravine = i < _ravineKnots;
            if (!ravine && _river[i].roadDir.sqrMagnitude > 0.01f) continue;   // not along the road
            Vector3 flow = _river[i].p - _river[i - 1].p;
            flow.y = 0f; flow.Normalize();
            var side = Vector3.Cross(Vector3.up, flow).normalized;
            for (int k = 0; k < 4; k++)
            {
                float sign = (k & 1) == 0 ? -1f : 1f;
                int row = k >> 1;
                float dist = _river[i].width + 3.5f + row * 8f + H01(i * 7f, k + 11f) * 4f;
                var p = _river[i].p + side * (sign * dist) + flow * ((H01(i, k + 5f) - 0.5f) * 16f);
                p.y = Height(p.x, p.z);
                if (p.y - _river[i].p.y < 3f) continue;                          // only on real walls
                float s = 0.9f + H01(i * 13f, k + 3f) * 1.3f;
                crags.Add((k & 2) == 0 ? boulderB : boulderA,
                          Matrix4x4.TRS(p - Vector3.up * (1.6f * s), Quaternion.Euler(0f, H01(i, k + 71f) * 360f, 0f),
                                        new Vector3(s, s * 1.35f, s)), WaterModelRole);
                cragCount++;
            }
        }
        crags.Flush(group, "RavineCrags", 0);
        Debug.Log($"[azora-water] ravine/gorge crags: {cragCount}");

        // Viaduct: along the contour over the ravine. The model is 182 m long with a PIER at z = 0,
        // so it is shifted half a span along its axis to put an arch over the stream. Uniform-ish
        // scale to the solved span; piers stretched down to the ravine floor.
        if (_viaductKnot >= 0)
        {
            const float ModelLength = 182f, ModelDrop = 43f, Arches = 6f;
            float sl = ViaductHalfLengthM * 2f / ModelLength;
            float floor = _viaductCentre.y - 2.5f;
            float sy = Mathf.Max(0.3f, (_viaductDeckY - floor) / ModelDrop);
            var pos = new Vector3(_viaductCentre.x, _viaductDeckY, _viaductCentre.z) +
                      _viaductAxis * (0.5f * ModelLength / Arches * sl);
            StageWaterGlb(group, "Azora_Viaduct_StoneRail_LOD0",
                Matrix4x4.TRS(pos, Quaternion.LookRotation(_viaductAxis, Vector3.up), new Vector3(0.78f, sy, sl)),
                "Viaduct");
        }

        // Frozen fall at the ravine head: base on the plunge pool, cliff backing into the head wall.
        if (_ravineKnots > 0 && _river.Length > 1)
        {
            const float ModelHeight = 58f;
            var head = _river[0];
            var down = _river[1].p - head.p; down.y = 0f; down.Normalize();
            float s = FallHeightM / ModelHeight;
            var pos = head.p - down * (head.width + 1.5f) + Vector3.up * -0.4f;
            StageWaterGlb(group, "Azora_Waterfall_IceCurtain",
                Matrix4x4.TRS(pos, Quaternion.LookRotation(-down, Vector3.up), new Vector3(s * 1.1f, s, s)),
                "FrozenWaterfall");
        }
    }

    private static void BuildWater(Transform root, AzoraRoute route)
    {
        if (!_waterPrepared) PrepareWater(route);
        var group = new GameObject("WP-B Water - Winter").transform;
        group.SetParent(root, false);

        if (LakeValid(_approachLake)) BuildLakeGeometry(group, _approachLake, "Azora_Water_ApproachLake", false);
        if (LakeValid(_descentLake)) BuildLakeGeometry(group, _descentLake, "Azora_Water_DescentLake", true);
        if (_river.Length >= 2)
        {
            AddMesh(group, "Dark Turquoise River", RiverMesh("Azora_Water_River", 1f, 0.08f),
                    WaterSurfaceMaterial("Azora_Water_RiverDark", new Color(0.06f, 0.24f, 0.29f), 0.86f, 0.62f), false);
            // No mid-stream foam strip: any continuous pale line on a ribbon this regular reads as
            // a road marking. Rapids are carried by the boulders and the 5-7 % grade.
            BuildRiverFence(group, route);
            BuildRiverHeroes(group);
        }

        // Island town + the stone bridge that reaches it from the road at deck = road level.
        int bridgeStation = -1;
        if (LakeValid(_island) && _bridgeStation >= 0)
        {
            var ic2 = _island.centre;
            var islandCentre = new Vector3(ic2.x, Height(ic2.x, ic2.y), ic2.y);
            float islandYaw = Mathf.Atan2(_approachLake.along.x, _approachLake.along.y) * Mathf.Rad2Deg;
            StageWaterGlb(group, "Azora_Island_Town",
                Matrix4x4.TRS(islandCentre - Vector3.up * 0.30f, Quaternion.Euler(0f, islandYaw, 0f), Vector3.one),
                "IslandTown");

            bridgeStation = _bridgeStation;
            var road = route.Position[bridgeStation];
            var dir = new Vector3(ic2.x - road.x, 0f, ic2.y - road.z).normalized;
            var start = road + dir * (RoadHalfWidth + ShoulderWidth + 2.2f);
            var end = islandCentre - dir * (Mathf.Min(_island.radiusAlong, _island.radiusAcross) * 0.55f);
            var span = end - start; span.y = 0f;
            float deckY = road.y - 0.52f;   // the model's deck surface is 0.575 m above its origin
            // Piers (9 m in the model) stretched to reach the lake bed.
            float sy = Mathf.Max(1f, (deckY - (_approachLake.level - 3f)) / 9f);
            // Long crossings are a chain of the 104 m / 4-arch model, not one stretched copy.
            int segs = Mathf.Max(1, Mathf.RoundToInt(span.magnitude / 110f));
            float segLen = span.magnitude / segs;
            for (int k = 0; k < segs; k++)
            {
                var mid = start + dir * (segLen * (k + 0.5f));
                mid.y = deckY;
                StageWaterGlb(group, "Azora_Island_Bridge",
                    Matrix4x4.TRS(mid, Quaternion.LookRotation(dir, Vector3.up), new Vector3(1f, sy, segLen / 104f)),
                    "IslandBridge" + k);
            }

            // A rocky collar round the island: dark crags at the waterline break the snow and hide
            // the coarse terrain grid's facets on the island's flanks.
            var boulderA = Glb(WaterModelDir, "Azora_Water_Boulder_A");
            var boulderB = Glb(WaterModelDir, "Azora_Water_Boulder_B");
            var collar = new TileBatch();
            for (int k = 0; k < 14; k++)
            {
                float a = k * Mathf.PI * 2f / 14f + H01(k, 5.5f) * 0.3f;
                float nr = 0.90f + H01(k, 9.1f) * 0.12f;
                var p2 = _island.centre + _island.along * (Mathf.Cos(a) * _island.radiusAlong * nr) +
                         _island.across * (Mathf.Sin(a) * _island.radiusAcross * nr);
                var p = new Vector3(p2.x, _approachLake.level - 1.2f, p2.y);
                if (Vector3.Dot(new Vector3(p.x - islandCentre.x, 0f, p.z - islandCentre.z), dir) < 0f &&
                    Mathf.Abs(Vector3.Cross(dir, new Vector3(p.x - islandCentre.x, 0f, p.z - islandCentre.z)).y) < 10f)
                    continue;                                           // keep the bridge landing clear
                float s = 0.8f + H01(k, 2.3f) * 0.8f;
                collar.Add((k & 1) == 0 ? boulderA : boulderB,
                           Matrix4x4.TRS(p, Quaternion.Euler(0f, H01(k, 7.7f) * 360f, 0f), new Vector3(s, s * 1.2f, s)),
                           WaterModelRole);
            }
            collar.Flush(group, "IslandCrags", 0);

            Debug.Log($"[azora-water] island at ({ic2.x:0},{ic2.y:0}); bridge {span.magnitude:0} m in {segs} span(s) " +
                      $"from route {route.Distance[bridgeStation]:0} m, deck {deckY:0.0}, island crown {_islandCrownY:0.0}, " +
                      $"lake {_approachLake.level:0.0}, pier scale {sy:0.00}");
        }
        BuildLakeWall(group, route, bridgeStation);

        foreach (var mr in group.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = mr.name;
            if (n.IndexOf("Open Water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Dark Turquoise River", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Foam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Ice Rim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Ice Floe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Snow Shore", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Azora_Water_Ice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Azora_Water_Fall", StringComparison.OrdinalIgnoreCase) >= 0)
                mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        Debug.Log($"[azora-water] winter water built: approach lake {_approachLake.radiusAlong * 2f:0}x{_approachLake.radiusAcross * 2f:0} m, " +
                  $"river {_river.Length} knots, descent lake {_descentLake.radiusAlong * 2f:0}x{_descentLake.radiusAcross * 2f:0} m; " +
                  $"{group.GetComponentsInChildren<MeshRenderer>(true).Length} renderers.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Read-only rideability analysis of a Shunta Metro waypoint set (ShuntaRouteBuilder's Catmull-Rom route).
/// Prints per zone: samples, min radius (as the game sees it = builder samples, and "true" = the underlying
/// spline resampled every 6 m), max curvature, hairpin count, implied lateral g at an assumed zone speed;
/// plus finish-approach straightness, ribbon/route self-overlap, self-crossing, sample spacing and 3D grade.
/// Batch entry points (run_steps.ps1 cannot forward arguments, so the path comes from the environment):
///   ShuntaMetroRouteAnalysis.Run          - analyses MR_SHUNTA_ROUTE_JSON, else the shipped course json
///   ShuntaMetroRouteAnalysis.RunProposal  - analyses docs/shunta_waypoints_proposal.json
///   ShuntaMetroRouteAnalysis.RunBoth      - current, then proposal
/// Editor code path: <see cref="Analyse(string)"/> takes any course-json path. Changes no asset or scene.
/// </summary>
public static class ShuntaMetroRouteAnalysis
{
    public const string ProposalRelPath = "docs/shunta_waypoints_proposal.json";
    const string CanonicalRoot = @"C:\Users\jason\OneDrive\Desktop\MapleRide";
    const string EnvVar = "MR_SHUNTA_ROUTE_JSON";

    /// <summary>Assumed cruise speed (m/s) per zone 1..12 for the lateral-g estimate.</summary>
    public static readonly float[] ZoneSpeedMps = { 8f, 8f, 10f, 11f, 12.5f, 15f, 9f, 9f, 12f, 12.5f, 11f, 11f };

    [MenuItem("MapleRide/Shunta Metro/Route Analysis (current)")]
    public static void Run()
    {
        string p = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrEmpty(p)) p = Path.Combine(Application.dataPath, "Resources/ShuntaMetro/shunta_metro_course.json");
        Analyse(p);
    }

    [MenuItem("MapleRide/Shunta Metro/Route Analysis (proposal)")]
    public static void RunProposal() => Analyse(ResolveDoc(ProposalRelPath));

    public static void RunBoth() { Run(); RunProposal(); }

    static string ResolveDoc(string rel)
    {
        string a = Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));
        if (File.Exists(a)) return a;
        return Path.Combine(CanonicalRoot, rel.Replace('/', Path.DirectorySeparatorChar));   // lab copies may lack docs/
    }

    // ------------------------------------------------------------------ analysis

    public static string Analyse(string jsonPath)
    {
        var sb = new StringBuilder();
        void L(string s) { sb.AppendLine(s); Debug.Log("[routeanalysis] " + s); }

        if (!File.Exists(jsonPath)) { L("MISSING FILE " + jsonPath); return sb.ToString(); }
        string text = File.ReadAllText(jsonPath);
        var course = JsonUtility.FromJson<ShuntaCourseData>(text);
        if (course == null || course.waypoints == null || course.waypoints.Length < 2 || course.zones.Length == 0)
        { L("INVALID course json " + jsonPath); return sb.ToString(); }
        L("=== ROUTE ANALYSIS: " + jsonPath + "  (" + course.waypoints.Length + " waypoints, " + course.zones.Length + " zones)");

        // 1) the game's own route (ShuntaRouteBuilder), built from this json
        var go = new GameObject("RouteAnalysisTemp");
        go.SetActive(false);
        var builder = go.AddComponent<ShuntaRouteBuilder>();
        builder.courseJson = new TextAsset(text);
        builder.buildRibbon = false; builder.buildGates = false; builder.drawGizmo = false;
        builder.Rebuild();
        var P = builder.Positions; var Km = builder.Km; var Zi = builder.ZoneIndex;
        float roadW = builder.roadWidth;
        UnityEngine.Object.DestroyImmediate(go);
        if (P.Length < 4) { L("builder produced no route"); return sb.ToString(); }
        int n = P.Length;
        var xz = new Vector2[n];
        for (int i = 0; i < n; i++) xz[i] = new Vector2(P[i].x, P[i].z);

        // 2) the underlying spline, resampled every 6 m ("true" geometry)
        var dense = DenseSpline(course.waypoints);
        float totalH = 0f; for (int i = 1; i < dense.Count; i++) totalH += Vector2.Distance(dense[i - 1], dense[i]);
        float scaleKm = course.distanceKm / Mathf.Max(totalH / 1000f, 0.001f);
        const float H = 6f;
        var fine = Resample(dense, H, out var fineS);
        var rFine = Radii(fine); var rSamp = Radii(xz);

        // ---- global numbers
        var sp = new List<float>(); for (int i = 1; i < n; i++) sp.Add(Vector2.Distance(xz[i - 1], xz[i]));
        L($"horizontal spline length {totalH / 1000f:F3} km (target {course.distanceKm:F2}); km scale {scaleKm:F5}; builder samples {n}; spacing min/mean/max " +
          $"{Min(sp):F1}/{Mean(sp):F1}/{Max(sp):F1} m (sampleSpacing {builder.sampleSpacing:F0} m requested)");
        float wpMin = float.MaxValue, wpMax = 0f, wpSum = 0f;
        for (int i = 1; i < course.waypoints.Length; i++)
        {
            float c = Vector2.Distance(new Vector2(course.waypoints[i - 1].x, course.waypoints[i - 1].z), new Vector2(course.waypoints[i].x, course.waypoints[i].z));
            wpMin = Mathf.Min(wpMin, c); wpMax = Mathf.Max(wpMax, c); wpSum += c;
        }
        L($"waypoint chord spacing min/mean/max {wpMin:F0}/{wpSum / (course.waypoints.Length - 1):F0}/{wpMax:F0} m");

        // ---- per zone
        L("zone | id | km | wps | m/wp | minR_samples | minR_true | maxCurv(1/km) | hairpins | v(m/s) | latG@minR_true | flags");
        int hairTotal = 0;
        for (int z = 0; z < course.zones.Length; z++)
        {
            var zn = course.zones[z];
            int wps = 0; foreach (var w in course.waypoints) if (w.zone == zn.index) wps++;
            float lenM = (zn.endKm - zn.startKm) * 1000f;
            float mS = float.MaxValue, mT = float.MaxValue;
            for (int i = 1; i < n - 1; i++) if (Zi[i] == zn.index) mS = Mathf.Min(mS, rSamp[i]);
            var zonePts = new List<Vector2>();
            for (int i = 1; i < fine.Count - 1; i++)
            {
                float km = fineS[i] / 1000f * scaleKm;
                if (km >= zn.startKm && km < zn.endKm) { mT = Mathf.Min(mT, rFine[i]); zonePts.Add(fine[i]); }
            }
            int hp = Hairpins(zonePts, H);
            hairTotal += hp;
            float v = zn.index >= 1 && zn.index <= ZoneSpeedMps.Length ? ZoneSpeedMps[zn.index - 1] : 10f;
            float g = v * v / mT / 9.81f;
            bool high = zn.surface == "expressway" || zn.surface == "bridge_deck";
            var flags = new List<string>();
            if (high && mT < 25f) flags.Add("R<25 expressway/bridge");
            if (high && mT < 30f) flags.Add("R<30 (design target)");
            if (!high && mT < 18f) flags.Add("R<18 city (design target)");
            if (mT < 12f) flags.Add("R<12");
            if (hp > 0) flags.Add("HAIRPIN");
            if (g > 0.35f) flags.Add("latG>0.35");
            L($"{zn.index,2} | {zn.id} | {zn.startKm:F1}-{zn.endKm:F1} | {wps} | {(wps > 0 ? lenM / wps : 0f):F0} | {mS:F0} | {mT:F0} | {1000f / mT:F1} | {hp} | {v:F1} | {g:F2} | {(flags.Count == 0 ? "ok" : string.Join("; ", flags))}");
        }
        float gMinS = float.MaxValue, gMinT = float.MaxValue;
        for (int i = 1; i < n - 1; i++) gMinS = Mathf.Min(gMinS, rSamp[i]);
        for (int i = 1; i < fine.Count - 1; i++) gMinT = Mathf.Min(gMinT, rFine[i]);
        L($"GLOBAL min radius: samples {gMinS:F0} m, true {gMinT:F0} m; hairpins {hairTotal}");

        // ---- sampling fidelity: how far apart the builder samples cut the true spline at the tightest bend
        int tight = 1; for (int i = 1; i < fine.Count - 1; i++) if (rFine[i] < rFine[tight]) tight = i;
        float sagitta = 0f;
        if (sp.Count > 0)
        {
            float chord = Max(sp);
            float rr = Mathf.Max(rFine[tight], 1f);
            sagitta = rr - Mathf.Sqrt(Mathf.Max(0f, rr * rr - chord * chord / 4f));
            L($"tightest bend R={rr:F0} m at km {fineS[tight] / 1000f * scaleKm:F2}: sample chord (max {chord:F0} m) -> sagitta {sagitta:F2} m; heading step per sample {Mathf.Rad2Deg * chord / rr:F1} deg" +
              $" | at 10 m spacing: sagitta {rr - Mathf.Sqrt(rr * rr - 25f):F3} m, {Mathf.Rad2Deg * 10f / rr:F1} deg/sample");
        }

        // ---- finish approach (true geometry, last km)
        {
            int m = fine.Count; float fh = Heading(fine[m - 2], fine[m - 1]); float straight = 0f;
            for (int i = m - 2; i >= 0; i--)
            {
                float h = Heading(fine[i], fine[i + 1]);
                if (Mathf.Abs(Mathf.DeltaAngle(h * Mathf.Rad2Deg, fh * Mathf.Rad2Deg)) <= 5f) straight += H; else break;
            }
            int kk = Mathf.Min(m - 1, Mathf.RoundToInt(1000f / H));
            var a0 = fine[m - 1 - kk]; var a1 = fine[m - 1]; var u = (a1 - a0).normalized; float dev = 0f, turn = 0f;
            for (int i = m - kk; i < m; i++)
            {
                var d = fine[i] - a0; dev = Mathf.Max(dev, Mathf.Abs(d.x * u.y - d.y * u.x));
                turn += Mathf.Abs(Mathf.DeltaAngle(Heading(fine[i - 1], fine[i]) * Mathf.Rad2Deg, Heading(fine[i - 2 < 0 ? 0 : i - 2], fine[i - 1]) * Mathf.Rad2Deg));
            }
            L($"FINISH approach: near-straight tail {straight:F0} m (heading within 5 deg of finish heading); last 1 km total heading change {turn:F1} deg, max offset from its chord {dev:F1} m" +
              (straight >= 300f ? "  -> OK (>=300 m)" : "  -> FAIL (<300 m)"));
        }

        // ---- self overlap / crossing (builder samples)
        int overlap = 0; int minGap = Mathf.CeilToInt(100f / Mathf.Max(Mean(sp), 1f)); float closest = float.MaxValue;
        for (int i = 0; i < n; i++)
            for (int j = i + minGap; j < n; j++)
            {
                float d = (xz[i] - xz[j]).magnitude;
                if (d < roadW) overlap++;
                if (Km[j] - Km[i] > 0.3f) closest = Mathf.Min(closest, d);
            }
        int cross = 0;
        for (int i = 0; i < n - 1; i++)
            for (int j = i + 2; j < n - 1; j++)
                if (SegCross(xz[i], xz[i + 1], xz[j], xz[j + 1])) cross++;
        L($"SELF-OVERLAP: {overlap} sample pairs (>=100 m apart along route) closer than road width {roadW:F0} m; segment crossings {cross}; closest approach of parts >300 m apart in km {closest:F0} m");
        // ribbon inner edge cusp: R < half road width
        int cusp = 0; for (int i = 1; i < n - 1; i++) if (rSamp[i] < roadW * 0.5f) cusp++;
        L($"RIBBON inner-edge cusps (sample R < {roadW * 0.5f:F1} m): {cusp}");

        // ---- 3D grade (RouteCourse.GradeAt style: rise over run in a rolling window)
        float[] s = new float[n]; for (int i = 1; i < n; i++) s[i] = s[i - 1] + sp[i - 1];
        foreach (float win in new[] { 8f, 40f })
        {
            float gmax = -1e9f, gmin = 1e9f, prev = 0f, maxStep = 0f; bool first = true;
            for (float c = 0f; c <= s[n - 1]; c += 1f)
            {
                float a = Mathf.Clamp(c - win * 0.5f, 0f, s[n - 1]), b = Mathf.Clamp(c + win * 0.5f, 0f, s[n - 1]);
                float run = b - a; if (run < 0.05f) continue;
                float gr = (YAt(P, s, b) - YAt(P, s, a)) / run * 100f;
                gmax = Mathf.Max(gmax, gr); gmin = Mathf.Min(gmin, gr);
                if (!first) maxStep = Mathf.Max(maxStep, Mathf.Abs(gr - prev)); first = false; prev = gr;
            }
            L($"GRADE window {win:F0} m: max up {gmax:F1}%  max down {gmin:F1}%  max change per metre {maxStep:F2} %/m   (json 100 m-window max {course.MaxGradePercent():F1}%)");
        }
        L("=== END ROUTE ANALYSIS");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ geometry helpers

    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Same spline as ShuntaRouteBuilder step 1 (24 sub-samples per waypoint segment).</summary>
    static List<Vector2> DenseSpline(ShuntaWaypoint[] w)
    {
        Vector2 P(int i) => new Vector2(w[i].x, w[i].z);
        var dense = new List<Vector2>();
        for (int i = 0; i < w.Length - 1; i++)
        {
            var p0 = P(Mathf.Max(i - 1, 0)); var p1 = P(i); var p2 = P(i + 1); var p3 = P(Mathf.Min(i + 2, w.Length - 1));
            for (int s = 0; s < 24; s++) dense.Add(CatmullRom(p0, p1, p2, p3, s / 24f));
        }
        dense.Add(P(w.Length - 1));
        return dense;
    }

    static List<Vector2> Resample(List<Vector2> poly, float h, out List<float> arc)
    {
        var res = new List<Vector2>(); arc = new List<float>();
        float acc = 0f, next = 0f;
        for (int i = 1; i < poly.Count; i++)
        {
            float seg = Vector2.Distance(poly[i - 1], poly[i]);
            while (next <= acc + seg && seg > 1e-6f)
            {
                res.Add(Vector2.Lerp(poly[i - 1], poly[i], (next - acc) / seg)); arc.Add(next); next += h;
            }
            acc += seg;
        }
        return res;
    }

    /// <summary>Circumradius through each consecutive triple (huge = straight).</summary>
    static float[] Radii(IList<Vector2> p)
    {
        var r = new float[p.Count];
        for (int i = 0; i < r.Length; i++) r[i] = 1e9f;
        for (int i = 1; i < p.Count - 1; i++)
        {
            float ab = Vector2.Distance(p[i - 1], p[i]), bc = Vector2.Distance(p[i], p[i + 1]), ca = Vector2.Distance(p[i + 1], p[i - 1]);
            float cr = Mathf.Abs((p[i].x - p[i - 1].x) * (p[i + 1].y - p[i - 1].y) - (p[i].y - p[i - 1].y) * (p[i + 1].x - p[i - 1].x));
            r[i] = cr < 1e-9f ? 1e9f : ab * bc * ca / (2f * cr);
        }
        return r;
    }

    static float Heading(Vector2 a, Vector2 b) => Mathf.Atan2(b.x - a.x, b.y - a.y);

    /// <summary>Distinct windows (150 m) in which the heading changes by >= 120 degrees.</summary>
    static int Hairpins(List<Vector2> pts, float h)
    {
        if (pts.Count < 4) return 0;
        var hd = new List<float>(); float acc = 0f, last = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            float a = Heading(pts[i - 1], pts[i]);
            if (i > 1) { float d = Mathf.DeltaAngle(last * Mathf.Rad2Deg, a * Mathf.Rad2Deg); acc += d; }
            last = a; hd.Add(acc);
        }
        int k = Mathf.RoundToInt(150f / h), cnt = 0;
        for (int i = 0; i + k < hd.Count;)
        {
            if (Mathf.Abs(hd[i + k] - hd[i]) >= 120f) { cnt++; i += k; } else i++;
        }
        return cnt;
    }

    static bool SegCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (Mathf.Min(a.x, b.x) > Mathf.Max(c.x, d.x) || Mathf.Min(c.x, d.x) > Mathf.Max(a.x, b.x)) return false;
        if (Mathf.Min(a.y, b.y) > Mathf.Max(c.y, d.y) || Mathf.Min(c.y, d.y) > Mathf.Max(a.y, b.y)) return false;
        float Ccw(Vector2 p, Vector2 q, Vector2 r) => (r.y - p.y) * (q.x - p.x) - (q.y - p.y) * (r.x - p.x);
        return Ccw(a, b, c) * Ccw(a, b, d) < 0f && Ccw(c, d, a) * Ccw(c, d, b) < 0f;
    }

    static float YAt(Vector3[] P, float[] s, float at)
    {
        int hi = Array.BinarySearch(s, at); if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, s.Length - 1);
        float t = Mathf.InverseLerp(s[hi - 1], s[hi], at);
        return Mathf.Lerp(P[hi - 1].y, P[hi].y, t);
    }

    static float Min(List<float> l) { float m = float.MaxValue; foreach (var v in l) m = Mathf.Min(m, v); return m; }
    static float Max(List<float> l) { float m = float.MinValue; foreach (var v in l) m = Mathf.Max(m, v); return m; }
    static float Mean(List<float> l) { float s = 0f; foreach (var v in l) s += v; return l.Count == 0 ? 0f : s / l.Count; }
}

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// PERMANENT engineering validation for the Minato Coast route.
///
/// This is not a throwaway probe - it is the standing "is the 19 km course actually rideable"
/// gate for Minato, and it is meant to be re-run after any change to
/// <see cref="MinatoCoastEnvironment"/>. Renders answer "does it look right"; this answers
/// "does it hold up as geometry" and every check reports a measured value against a named
/// tolerance rather than a pass/fail opinion.
///
/// It exercises six things:
///   1. ROUTE    - sample ordering, monotonic distance, spacing, gaps, chapter coverage.
///   2. SURFACE  - a downward raycast onto real colliders every 0.5 m along the centreline:
///                 missing collider (hole), step height between consecutive hits, and stacked
///                 duplicate surfaces.
///   3. JOINTS   - the same test densified at every bridge span boundary and at both bridge
///                 approaches, where a step would actually throw a rider.
///   4. ENVELOPE - the bicycle + rider + follow-camera bounding volume swept along the path,
///                 looking for anything solid intruding into it (invisible blockers, rails
///                 leaning into the lane, vegetation in the corridor).
///   5. EDGES    - guardrail collider cover on exposed edges, and confirmation that no rail
///                 collider crosses into the carriageway.
///   6. REGISTRY - Minato's presence in the project's region/route registration.
///
/// Envelope figures are read from the real KuroFollowCamera component where possible; Kuro
/// itself is never modified.
/// </summary>
public static class MinatoCoastValidation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RootName = "Minato Coast Environment";

    // ---------------------------------------------------------------- tolerances
    // All PROVISIONAL, chosen against the cycling brief rather than measured from hardware.

    /// <summary>Largest acceptable vertical step between two surface samples 0.5 m apart (m).</summary>
    private const float StepToleranceM = 0.045f;
    /// <summary>Tighter tolerance at bridge span joints and approaches (m).</summary>
    private const float JointToleranceM = 0.030f;
    /// <summary>Sample stride along the centreline for the surface sweep (m).</summary>
    private const float SurfaceStrideM = 0.5f;
    /// <summary>Stride used inside a joint window (m).</summary>
    private const float JointStrideM = 0.1f;
    /// <summary>Half-window either side of a joint that gets the dense treatment (m).</summary>
    private const float JointWindowM = 6f;
    /// <summary>Two surfaces closer than this above the road count as a duplicate/overlap (m).</summary>
    private const float DuplicateGapM = 0.60f;

    /// <summary>Handlebar half-width (m). Brief calls for a 7-9 m road; we use 8 m.</summary>
    private const float BarHalfWidthM = 0.31f;
    /// <summary>Helmet clearance above the road surface (m).</summary>
    private const float HelmetClearM = 1.95f;
    /// <summary>Pedal clearance below the rider (m) - the envelope floor sits just off the road.</summary>
    private const float PedalClearM = 0.06f;
    /// <summary>Lateral half-width of the protected ride lane (m).</summary>
    private const float LaneHalfM = 4.0f;
    /// <summary>Envelope sweep stride (m).</summary>
    private const float EnvelopeStrideM = 2.0f;

    private const float BridgeStartM = 2300f, BridgeEndM = 10300f, SpanM = 120f;

    private static readonly (string name, float from, float to)[] Chapters =
    {
        ("Chapter 1 - Port City Departure",           0f,     1800f),
        ("Chapter 2 - Bridge Approach",               1800f,  3300f),
        ("Chapter 3 - Open Ocean Midpoint",           3300f,  9900f),
        ("Chapter 4 - Far-Shore Landfall",            9900f, 13400f),
        ("Chapter 5 - Inland Mountain Continuation", 13400f, 99999f),
    };

    [MenuItem("MapleRide/Diagnostics/Validate Minato Coast Route")]
    public static void Validate()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogError($"[minato-val] '{RootName}' not in scene - run MinatoCoastEnvironment.Apply first.");
            return;
        }

        Physics.SyncTransforms();
        var route = MinatoCoastEnvironment.MinatoRoute.Load();
        var log = new StringBuilder();
        log.AppendLine("=== MINATO COAST ROUTE VALIDATION ===");

        bool ok = true;
        ok &= CheckRoute(route, log);
        ok &= CheckChapterRoots(root, route, log);
        ok &= CheckSurface(route, log);
        ok &= CheckJoints(route, log);
        ok &= CheckEnvelope(route, log);
        ok &= CheckEdges(route, log);
        ok &= CheckRegistry(log);

        log.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL - see items marked [FAIL] above");
        Debug.Log(log.ToString());
    }

    // ------------------------------------------------------------------ 1. route

    private static bool CheckRoute(MinatoCoastEnvironment.MinatoRoute r, StringBuilder log)
    {
        bool ok = true;
        int nonMono = 0, count = r.Count;
        float minGap = float.MaxValue, maxGap = 0f, worstTangent = 0f;

        for (int i = 1; i < count; i++)
        {
            float dd = r.Distance[i] - r.Distance[i - 1];
            if (dd <= 0f) nonMono++;
            float seg = Vector3.Distance(r.Position[i], r.Position[i - 1]);
            if (seg < minGap) minGap = seg;
            if (seg > maxGap) maxGap = seg;
            // The chord between samples must agree with the stored arc length, or the spline
            // has a gap the road mesh will have swept straight across.
            worstTangent = Mathf.Max(worstTangent, Mathf.Abs(seg - dd));
        }

        log.AppendLine($"[route] {count:N0} samples, {r.Length:N1} m, spacing {minGap:F2}-{maxGap:F2} m, " +
                       $"worst chord-vs-arc mismatch {worstTangent * 1000f:F1} mm");
        if (nonMono > 0) { log.AppendLine($"  [FAIL] {nonMono} samples are not in increasing distance order"); ok = false; }
        else log.AppendLine("  [ok] every sample is ordered and strictly increasing in distance");

        if (maxGap > 6f) { log.AppendLine($"  [FAIL] a {maxGap:F2} m gap exists between consecutive samples"); ok = false; }
        else log.AppendLine($"  [ok] no sample gap exceeds 6.00 m (worst {maxGap:F2} m)");

        return ok;
    }

    private static bool CheckChapterRoots(GameObject root, MinatoCoastEnvironment.MinatoRoute r,
                                          StringBuilder log)
    {
        bool ok = true;
        float covered = 0f;
        foreach (var c in Chapters)
        {
            var t = root.transform.Find(c.name);
            float to = Mathf.Min(c.to, r.Length);
            if (t == null) { log.AppendLine($"  [FAIL] missing hierarchy root '{c.name}'"); ok = false; continue; }
            int children = t.childCount;
            log.AppendLine($"[chapter] '{c.name}' {c.from:N0}-{to:N0} m, {children} content groups");
            if (children == 0) { log.AppendLine("  [FAIL] chapter root is empty"); ok = false; }
            if (!Mathf.Approximately(c.from, covered))
            { log.AppendLine($"  [FAIL] chapter boundary gap at {covered:N0} m"); ok = false; }
            covered = to;
        }
        if (Mathf.Abs(covered - r.Length) > 1f)
        { log.AppendLine($"  [FAIL] chapters cover {covered:N0} m of a {r.Length:N0} m route"); ok = false; }
        else log.AppendLine($"  [ok] the five chapters tile the whole {r.Length:N1} m route with no gap or overlap");
        return ok;
    }

    // ---------------------------------------------------------------- 2. surface

    private struct Hit { public bool any; public float y; public int stack; public string what; }

    /// <summary>Raycast straight down onto whatever solid surface carries the rider here.</summary>
    private static Hit Probe(Vector3 centre)
    {
        var from = centre + Vector3.up * 40f;
        var hits = Physics.RaycastAll(from, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
        var h = new Hit { any = false, stack = 0 };
        float best = float.MinValue;
        foreach (var x in hits)
        {
            if (x.point.y > centre.y + 6f) continue;          // ignore overhead structure
            if (x.point.y > best) { best = x.point.y; h.what = x.collider.name; }
            if (x.point.y > centre.y - DuplicateGapM && x.point.y < centre.y + DuplicateGapM) h.stack++;
        }
        if (best > float.MinValue) { h.any = true; h.y = best; }
        return h;
    }

    /// <summary>
    /// Position on the route at an arbitrary distance, linearly interpolated BETWEEN samples.
    /// Probing at r.Position[IndexAt(d)] snaps to the 3 m stations, which silently turns a
    /// 0.5 m sweep into a 3 m one and compares the surface against the very points it was
    /// generated from. Interpolating is what makes the residual meaningful: it asks whether the
    /// built mesh sags or steps between the stations the designer specified.
    /// </summary>
    private static Vector3 PointAt(MinatoCoastEnvironment.MinatoRoute r, float d)
    {
        int i = r.IndexAt(d);
        if (i >= r.Count - 1) return r.Position[i];
        float span = r.Distance[i + 1] - r.Distance[i];
        if (span <= 1e-4f) return r.Position[i];
        float t = Mathf.Clamp01((d - r.Distance[i]) / span);
        return Vector3.Lerp(r.Position[i], r.Position[i + 1], t);
    }

    private static bool CheckSurface(MinatoCoastEnvironment.MinatoRoute r, StringBuilder log)
    {
        bool ok = true;
        int holes = 0, dupes = 0, steps = 0, samples = 0;
        float worstStep = 0f, worstAt = 0f;
        float prev = float.NaN, prevRoute = float.NaN;

        for (float d = 1f; d < r.Length - 1f; d += SurfaceStrideM)
        {
            var q = PointAt(r, d);
            var h = Probe(q);
            samples++;
            if (!h.any) { holes++; prev = float.NaN; continue; }
            if (h.stack > 1) dupes++;
            if (!float.IsNaN(prev))
            {
                // Measure the RESIDUAL against the route's own gradient, not the raw height
                // delta. A legitimate 9% mountain climb moves 45 mm per 0.5 m sample, so a raw
                // delta test condemns the whole of chapter 5 for being a hill. What actually
                // throws a rider is the surface departing from the designed grade.
                float step = Mathf.Abs((h.y - prev) - (q.y - prevRoute));
                if (step > worstStep) { worstStep = step; worstAt = d; }
                if (step > StepToleranceM) steps++;
            }
            prev = h.y; prevRoute = q.y;
        }

        log.AppendLine($"[surface] {samples:N0} centreline probes at {SurfaceStrideM:F1} m " +
                       $"({r.Length:N0} m of road and deck)");
        log.AppendLine($"  worst grade residual {worstStep * 1000f:F0} mm at {worstAt:N0} m " +
                       $"(tolerance {StepToleranceM * 1000f:F0} mm)");
        if (holes > 0) { log.AppendLine($"  [FAIL] {holes} probes found NO collider - holes in the ride surface"); ok = false; }
        else log.AppendLine("  [ok] every probe landed on a collider - the surface is continuous");
        if (steps > 0) { log.AppendLine($"  [FAIL] {steps} probe pairs exceed the step tolerance"); ok = false; }
        else log.AppendLine("  [ok] no step exceeds tolerance anywhere on the route");
        if (dupes > 0) log.AppendLine($"  [warn] {dupes} probes saw stacked surfaces within {DuplicateGapM:F2} m " +
                                      "(expected where the road crosses terrain chunks)");
        else log.AppendLine("  [ok] no duplicate or overlapping ride surfaces");
        return ok;
    }

    // ----------------------------------------------------------------- 3. joints

    private static bool CheckJoints(MinatoCoastEnvironment.MinatoRoute r, StringBuilder log)
    {
        var joints = new List<float> { BridgeStartM, BridgeEndM };
        for (float d = BridgeStartM + SpanM; d < BridgeEndM; d += SpanM) joints.Add(d);

        bool ok = true;
        float worst = 0f, worstAt = 0f; int bad = 0, probes = 0;
        foreach (float j in joints)
        {
            float prev = float.NaN, prevRoute = float.NaN;
            for (float d = j - JointWindowM; d <= j + JointWindowM; d += JointStrideM)
            {
                if (d < 1f || d > r.Length - 1f) continue;
                var q = PointAt(r, d);
                var h = Probe(q);
                probes++;
                if (!h.any) { bad++; prev = float.NaN; continue; }
                if (!float.IsNaN(prev))
                {
                    // Residual against the designed grade - see CheckSurface.
                    float step = Mathf.Abs((h.y - prev) - (q.y - prevRoute));
                    if (step > worst) { worst = step; worstAt = d; }
                    if (step > JointToleranceM) bad++;
                }
                prev = h.y; prevRoute = q.y;
            }
        }
        log.AppendLine($"[joints] {joints.Count} joints (2 approaches + {joints.Count - 2} span boundaries), " +
                       $"{probes:N0} probes at {JointStrideM * 100f:F0} cm");
        log.AppendLine($"  worst joint grade residual {worst * 1000f:F0} mm at {worstAt:N0} m " +
                       $"(tolerance {JointToleranceM * 1000f:F0} mm)");
        if (bad > 0) { log.AppendLine($"  [FAIL] {bad} joint probes stepped or found no surface"); ok = false; }
        else log.AppendLine("  [ok] every span joint and both approaches are continuous and snag-free");
        return ok;
    }

    // --------------------------------------------------------------- 4. envelope

    private static bool CheckEnvelope(MinatoCoastEnvironment.MinatoRoute r, StringBuilder log)
    {
        // Read the real follow-camera geometry so the envelope matches the shipped gameplay
        // view. Kuro is only READ here, never written.
        float camBack = 3.60f, camUp = 1.45f, fov = 44f;
        var kuro = Object.FindFirstObjectByType(System.Type.GetType("KuroFollowCamera, Assembly-CSharp"));
        if (kuro != null)
        {
            var so = new SerializedObject(kuro);
            var off = so.FindProperty("gameplayOffset");
            var f = so.FindProperty("gameplayFieldOfView");
            if (off != null) { camBack = -off.vector3Value.z; camUp = off.vector3Value.y; }
            if (f != null) fov = f.floatValue;
            log.AppendLine($"[envelope] using KuroFollowCamera values read from the scene: " +
                           $"offset back {camBack:F2} m, up {camUp:F2} m, fov {fov:F0}");
        }
        else log.AppendLine($"[envelope] KuroFollowCamera not found in scene; using the documented " +
                            $"defaults back {camBack:F2} m / up {camUp:F2} m / fov {fov:F0}");

        bool ok = true;
        int intrusions = 0, camBlocked = 0, samples = 0;
        var offenders = new Dictionary<string, int>();
        var half = new Vector3(BarHalfWidthM, (HelmetClearM - PedalClearM) * 0.5f, 0.5f);

        for (float d = 2f; d < r.Length - 2f; d += EnvelopeStrideM)
        {
            int i = r.IndexAt(d);
            var p = r.Position[i];
            var fwd = r.Tangent[i]; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-5f) continue;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            var centre = p + Vector3.up * (PedalClearM + (HelmetClearM - PedalClearM) * 0.5f);
            samples++;

            foreach (var c in Physics.OverlapBox(centre, half, rot, ~0, QueryTriggerInteraction.Ignore))
            {
                // The road/deck/terrain the rider is ON is not an intrusion.
                string n = c.name;
                if (n.Contains("Road") || n.Contains("Deck") || n.Contains("Terrain") ||
                    n.Contains("Approach") || n.Contains("Bridge_Deck")) continue;
                intrusions++;
                offenders.TryGetValue(n, out int k); offenders[n] = k + 1;
            }

            // Follow camera: is the gameplay eye position inside something solid?
            var eye = p - rot * Vector3.forward * camBack + Vector3.up * camUp;
            if (Physics.CheckSphere(eye, 0.25f, ~0, QueryTriggerInteraction.Ignore)) camBlocked++;
        }

        log.AppendLine($"  swept {samples:N0} envelopes at {EnvelopeStrideM:F1} m: " +
                       $"{BarHalfWidthM * 2f:F2} m wide x {HelmetClearM:F2} m tall " +
                       "(handlebar / helmet / pedal box)");
        if (intrusions > 0)
        {
            ok = false;
            log.AppendLine($"  [FAIL] {intrusions} envelope intrusions - solid geometry inside the ride corridor:");
            foreach (var kv in offenders) log.AppendLine($"      {kv.Value,6} x {kv.Key}");
        }
        else log.AppendLine("  [ok] nothing solid intrudes on the rider envelope over the whole route");

        if (camBlocked > 0) { log.AppendLine($"  [FAIL] {camBlocked} stations have the follow camera inside geometry"); ok = false; }
        else log.AppendLine("  [ok] the gameplay follow-camera position is clear at every station");
        return ok;
    }

    // ------------------------------------------------------------------ 5. edges

    private static bool CheckEdges(MinatoCoastEnvironment.MinatoRoute r, StringBuilder log)
    {
        bool ok = true;
        int exposed = 0, unprotected = 0, laneIntrusions = 0, checkedStations = 0;
        var laneOffenders = new Dictionary<string, int>();

        for (float d = 4f; d < r.Length - 4f; d += 4f)
        {
            int i = r.IndexAt(d);
            var p = r.Position[i];
            var side = r.SideFlat(i);
            checkedStations++;

            for (int s = -1; s <= 1; s += 2)
            {
                // Is this edge exposed? Probe just beyond the shoulder; a big drop means yes.
                var lip = p + side * (s * (LaneHalfM + 1.2f));
                var h = Probe(lip);
                bool drop = !h.any || (p.y - h.y) > 1.6f;
                if (!drop) continue;
                exposed++;

                // Something solid must stand on that edge between the lane and the drop.
                var railCentre = p + side * (s * (LaneHalfM + 0.45f)) + Vector3.up * 0.6f;
                bool rail = Physics.CheckBox(railCentre, new Vector3(0.6f, 0.55f, 2f),
                                             Quaternion.LookRotation(r.Tangent[i], Vector3.up),
                                             ~0, QueryTriggerInteraction.Ignore);
                if (!rail) unprotected++;
            }

            // ...and nothing may stand INSIDE the lane.
            var laneCentre = p + Vector3.up * 1.0f;
            foreach (var c in Physics.OverlapBox(laneCentre, new Vector3(LaneHalfM - 0.35f, 0.85f, 1.5f),
                                                 Quaternion.LookRotation(r.Tangent[i], Vector3.up),
                                                 ~0, QueryTriggerInteraction.Ignore))
            {
                string n = c.name;
                if (n.Contains("Road") || n.Contains("Deck") || n.Contains("Terrain") ||
                    n.Contains("Approach")) continue;
                laneIntrusions++;
                laneOffenders.TryGetValue(n, out int lk); laneOffenders[n] = lk + 1;
            }
        }

        log.AppendLine($"[edges] {checkedStations:N0} stations at 4 m, {exposed:N0} exposed edges found " +
                       $"(drop > 1.6 m beyond the {LaneHalfM * 2f:F0} m lane)");
        if (unprotected > 0) { log.AppendLine($"  [FAIL] {unprotected} exposed edges have no rail collider"); ok = false; }
        else log.AppendLine("  [ok] every exposed edge carries a guardrail or parapet collider");
        if (laneIntrusions > 0)
        {
            ok = false;
            log.AppendLine($"  [FAIL] {laneIntrusions} rail/furniture colliders reach into the ride lane:");
            foreach (var kv in laneOffenders) log.AppendLine($"      {kv.Value,6} x {kv.Key}");
        }
        else log.AppendLine("  [ok] no rail or furniture collider intrudes into the ride lane");
        return ok;
    }

    // --------------------------------------------------------------- 6. registry

    private static bool CheckRegistry(StringBuilder log)
    {
        bool ok = true;
        const string courseId = "minato_crossing";
        const string segmentId = "minato";
        const float expectedLength = 19010.032f;
        string[] expectedCheckpoints =
        {
            "MC_PORT_CITY_DEPARTURE", "MC_BRIDGE_APPROACH", "MC_OPEN_OCEAN_MIDPOINT",
            "MC_FAR_SHORE_LANDFALL", "MC_HORIZON_METROPOLIS", "MC_METROPOLIS_FINISH",
        };

        var graph = AssetDatabase.LoadAssetAtPath<RouteGraph>(RouteGraph.AssetPath);
        if (graph == null)
        {
            log.AppendLine($"[registry] runtime graph {RouteGraph.AssetPath}: [FAIL] missing");
            return false;
        }

        var segment = graph.Segment(segmentId);
        bool segmentOk = segment != null &&
                         Mathf.Abs(segment.Length - expectedLength) < 0.25f &&
                         segment.Count == 6338;
        log.AppendLine($"[registry] runtime segment '{segmentId}': " +
                       (segmentOk
                           ? $"[ok] {segment.Count:N0} samples, authored distance {segment.Length:N1} m"
                           : $"[FAIL] samples={(segment != null ? segment.Count : 0):N0}, " +
                             $"authored distance={(segment != null ? segment.Length : 0f):N1} m"));
        ok &= segmentOk;

        var def = graph.Course(courseId);
        if (def == null)
        {
            log.AppendLine($"[registry] runtime course '{courseId}': [FAIL] not published in the baked asset");
            ok = false;
        }
        else
        {
            bool regionOk = def.regionId == RegionCatalog.MinatoCoast;
            bool legsOk = def.legs != null && def.legs.Length == 1 &&
                          def.legs[0].segment == segmentId &&
                          Mathf.Abs(def.legs[0].from) < 0.01f &&
                          segment != null &&
                          Mathf.Abs(def.legs[0].to - segment.Length) < 0.25f;
            var built = RouteCourse.Build(graph, def);
            float expectedPolylineLength = 0f;
            if (segment != null)
                for (int i = 1; i < segment.Count; i++)
                    expectedPolylineLength += Vector3.Distance(segment.position[i - 1],
                                                               segment.position[i]);
            bool lengthOk = built != null && segment != null &&
                            built.Count == segment.Count &&
                            Mathf.Abs(built.Length - expectedPolylineLength) < 0.05f;
            log.AppendLine($"[registry] runtime course '{courseId}': " +
                           (regionOk && legsOk && lengthOk
                               ? $"[ok] region={def.regionId}, one full-route leg, " +
                                 $"{segment.Length:N1} m authored / {built.Length:N1} m runtime polyline"
                               : $"[FAIL] region={def.regionId}, legs={def.legs?.Length ?? 0}, " +
                                 $"samples={(built != null ? built.Count : 0):N0}, " +
                                 $"builtLength={(built != null ? built.Length : 0f):N1} m"));
            ok &= regionOk && legsOk && lengthOk;

            var names = new HashSet<string>();
            if (built != null)
                foreach (var cp in built.Checkpoints) names.Add(cp.Name);
            bool checkpointsOk = built != null && built.Checkpoints.Length == expectedCheckpoints.Length;
            foreach (string name in expectedCheckpoints) checkpointsOk &= names.Contains(name);
            log.AppendLine($"[registry] runtime Minato checkpoints: " +
                           (checkpointsOk
                               ? $"[ok] all {expectedCheckpoints.Length} full-route checkpoints published"
                               : $"[FAIL] expected {expectedCheckpoints.Length}, found " +
                                 $"{(built != null ? built.Checkpoints.Length : 0)} or names mismatch"));
            ok &= checkpointsOk;
        }

        bool unknownOk = graph.Course("__minato_validation_unknown__") == null;
        log.AppendLine($"[registry] unknown-course lookup: " +
                       (unknownOk ? "[ok] returns null (no Sakura substitution)"
                                  : "[FAIL] resolved to another course"));
        ok &= unknownOk;

        var catalog = RegionCatalog.Find(RegionCatalog.MinatoCoast);
        bool catalogOk = catalog != null && catalog.BuiltCourseId == courseId;
        log.AppendLine($"[registry] world-map Minato pin: " +
                       (catalogOk ? $"[ok] BuiltCourseId={courseId}"
                                  : "[FAIL] catalog missing or points at the wrong course"));
        ok &= catalogOk;

        var session = Object.FindFirstObjectByType<RideSession>(FindObjectsInactive.Include);
        if (session == null)
        {
            log.AppendLine("[registry] RideSession selection: [FAIL] no session in the runtime scene");
            ok = false;
        }
        else
        {
            string before = session.courseId;
            var beforeGraph = session.Graph;
            session.Graph = graph;
            session.SelectCourse(courseId);
            bool selected = session.courseId == courseId && session.Course != null &&
                            session.Course.Id == courseId &&
                            graph.RegionOfCourse(session.courseId) == RegionCatalog.MinatoCoast;
            log.AppendLine($"[registry] RideSession/RegionDirector resolution: " +
                           (selected ? $"[ok] {courseId} -> {RegionCatalog.MinatoCoast}"
                                     : $"[FAIL] selected '{session.courseId}' / " +
                                       $"'{session.Course?.Id ?? "null"}'"));

            var selectedCourse = session.Course;
            session.SelectCourse("__minato_validation_unknown__");
            bool unknownSelectionOk = session.courseId == courseId &&
                                      ReferenceEquals(session.Course, selectedCourse);
            log.AppendLine($"[registry] RideSession unknown-course selection: " +
                           (unknownSelectionOk
                               ? "[ok] rejected and preserved the selected Minato course"
                               : $"[FAIL] changed to '{session.courseId}' / " +
                                 $"'{session.Course?.Id ?? "null"}'"));

            if (!string.IsNullOrEmpty(before) && graph.Course(before) != null)
                session.SelectCourse(before);
            session.Graph = beforeGraph ?? graph;
            ok &= selected && unknownSelectionOk;
        }

        var probeGo = new GameObject("~MinatoDefaultCourseProbe")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        var probe = probeGo.AddComponent<RideSession>();
        probe.Graph = graph;
        probe.courseId = "";
        probe.EnsureCourse();
        bool defaultOk = probe.courseId == "sakura_circuit" &&
                         probe.Course != null &&
                         probe.Course.Id == "sakura_circuit";
        log.AppendLine($"[registry] empty-course default: " +
                       (defaultOk
                           ? "[ok] resolves to the documented sakura_circuit default"
                           : $"[FAIL] resolved to '{probe.courseId}' / " +
                             $"'{probe.Course?.Id ?? "null"}'"));
        Object.DestroyImmediate(probeGo);
        ok &= defaultOk;

        string route = "Assets/Environment/MinatoCoast/MinatoRoute.json";
        bool routeOk = System.IO.File.Exists(route);
        log.AppendLine($"[registry] route asset {route}: " +
                       (routeOk ? "[ok] present" : "[FAIL] missing"));
        ok &= routeOk;
        return ok;
    }
}

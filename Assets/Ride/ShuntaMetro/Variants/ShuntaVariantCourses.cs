using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registers the Shunta course variants (Short Loop, Reverse, Sprint Cut) as selectable courses in the in-memory
/// RouteGraph, WITHOUT editing any shared file. It mirrors ShuntaRouteProvider.Augment: it only ever mutates the
/// DontSave in-memory copy that ShuntaRouteProvider produced (never the baked asset) and re-uses the zone segments
/// that provider already added (shunta_z01..shunta_z12), so no geometry is duplicated:
///   shunta_sprint_cut  legs z09..z12 (forward)                      point-to-point, ~10 km
///   shunta_reverse     legs z12..z01, each leg from=Length,to=0      point-to-point, full length (RouteCourse.Build
///                      natively supports reversed legs and maps the zone checkpoints onto them)
///   shunta_short_loop  legs z01..z06 + one new "link" segment back to the start, closed=true (3 laps)
/// Opt-in: nothing happens until <see cref="Ensure"/> / <see cref="Select"/> is called, or env MR_SHUNTA_VARIANTS=1
/// registers them at startup (so other validation that enumerates graph.courses is never surprised).
///
/// To make them world-map selectable WITHOUT touching RegionCatalog: call
/// <c>ShuntaVariantCourses.Select(session, ShuntaVariantCourses.ReverseId)</c> from any UI (it sets autoLaps/plannedLaps
/// and resets the ride). To surface them on the pin itself a one-line change in RegionDirector.FastTravel
/// (session.SelectCourse(region.BuiltCourseId)) or a Region.VariantCourseIds field would be needed (shared files, not done).
/// Known limit: ShuntaHud / ShuntaLifeDirector / ShuntaAudioHost / ShuntaRideHost only activate for course id
/// "shunta_metro"; they do not follow variants yet (see ShuntaVariantCourses.IsShuntaCourse for the 1-line change).
/// </summary>
public static class ShuntaVariantCourses
{
    public const string BaseId = ShuntaRouteProvider.CourseId;
    public const string LoopId = ShuntaCourseVariants.ShortLoopId, ReverseId = ShuntaCourseVariants.ReverseId, SprintId = ShuntaCourseVariants.SprintCutId;
    public const string LinkSegmentId = "shunta_loop_link";
    public const int LoopLaps = 3, LoopLastZone = 6, SprintFirstZone = 9;

    public static readonly string[] AllIds = { LoopId, ReverseId, SprintId };

    /// <summary>True for the base course and every variant id (hosts can use this to follow variants).</summary>
    public static bool IsShuntaCourse(string id) => id == BaseId || Array.IndexOf(AllIds, id) >= 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("MR_SHUNTA_VARIANTS") == "1") Ensure();
    }

    /// <summary>Loads the graph and registers the variants into it. Safe to call repeatedly; false if the base course is absent.</summary>
    public static bool Ensure() => Register(RouteGraph.Load());

    /// <summary>Idempotent. Mutates only a DontSave in-memory graph that already contains the base Shunta course.</summary>
    public static bool Register(RouteGraph g)
    {
        if (g == null) return false;
        var baseDef = g.Course(BaseId);
        if (baseDef == null) return false;
        if (g.Course(LoopId) != null && g.Course(ReverseId) != null && g.Course(SprintId) != null) return true;
        if ((g.hideFlags & HideFlags.DontSave) == 0)
        {
            Debug.LogError("[shunta-variants] refusing to modify the baked RouteGraph asset");
            return false;
        }
        try
        {
            var legsFwd = baseDef.legs;     // one leg per zone, in order
            var segs = new List<RouteSegmentData>(g.segments);
            var courses = new List<CourseData>(g.courses);
            var cps = new List<CheckpointData>(g.checkpoints);
            float baseMinutes = baseDef.targetMinutes > 0f ? baseDef.targetMinutes : 55f;
            float baseLen = 0f; foreach (var l in legsFwd) baseLen += Mathf.Abs(l.to - l.from);

            if (g.Course(SprintId) == null)
            {
                var legs = new List<CourseLegData>();
                float len = 0f;
                for (int i = Mathf.Clamp(SprintFirstZone - 1, 0, legsFwd.Length - 1); i < legsFwd.Length; i++)
                { legs.Add(Copy(legsFwd[i], false)); len += Mathf.Abs(legsFwd[i].to - legsFwd[i].from); }
                courses.Add(Make(SprintId, "Shunta Sprint Cut", baseDef, false, baseMinutes * len / Mathf.Max(1f, baseLen), legs));
            }
            if (g.Course(ReverseId) == null)
            {
                var legs = new List<CourseLegData>();
                for (int i = legsFwd.Length - 1; i >= 0; i--) legs.Add(Copy(legsFwd[i], true));
                courses.Add(Make(ReverseId, "Shunta Reverse", baseDef, false, baseMinutes, legs));
            }
            if (g.Course(LoopId) == null)
            {
                int n = Mathf.Clamp(LoopLastZone, 1, legsFwd.Length);
                var legs = new List<CourseLegData>();
                float len = 0f;
                for (int i = 0; i < n; i++) { legs.Add(Copy(legsFwd[i], false)); len += Mathf.Abs(legsFwd[i].to - legsFwd[i].from); }
                var first = g.Segment(legsFwd[0].segment);
                var last = g.Segment(legsFwd[n - 1].segment);
                if (first != null && last != null && first.Count >= 2 && last.Count >= 2)
                {
                    var link = MakeLink(last.position[last.Count - 1], first.position[0]);
                    segs.RemoveAll(s => s.id == LinkSegmentId);
                    segs.Add(link);
                    legs.Add(new CourseLegData { segment = LinkSegmentId, from = 0f, to = link.Length });
                    len += link.Length;
                    // lap checkpoint on the link so a lap reads like a zone
                    cps.Add(new CheckpointData { segment = LinkSegmentId, distance = link.Length, displayName = "Lap line" });
                }
                courses.Add(Make(LoopId, "Shunta Short Loop", baseDef, true, baseMinutes * len / Mathf.Max(1f, baseLen) * LoopLaps, legs));
            }
            g.segments = segs.ToArray();
            g.courses = courses.ToArray();
            g.checkpoints = cps.ToArray();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[shunta-variants] registration failed, graph left as is: " + e.Message);
            return false;
        }
    }

    static CourseLegData Copy(CourseLegData l, bool reverse)
        => new CourseLegData { segment = l.segment, from = reverse ? Mathf.Max(l.from, l.to) : Mathf.Min(l.from, l.to), to = reverse ? Mathf.Min(l.from, l.to) : Mathf.Max(l.from, l.to) };

    static CourseData Make(string id, string name, CourseData baseDef, bool closed, float minutes, List<CourseLegData> legs)
        => new CourseData { id = id, displayName = name, regionId = baseDef.regionId, closed = closed, targetMinutes = Mathf.Max(5f, minutes), legs = legs.ToArray() };

    /// <summary>Straight connector (10 m samples) from a to b, same attribute conventions as ShuntaRouteProvider.MakeSegment.</summary>
    static RouteSegmentData MakeLink(Vector3 a, Vector3 b)
    {
        float d = Vector3.Distance(a, b);
        int n = Mathf.Max(2, Mathf.CeilToInt(d / 10f) + 1);
        var s = new RouteSegmentData
        {
            id = LinkSegmentId, displayName = "Return to Shibuya",
            position = new Vector3[n], tangent = new Vector3[n], side = new Vector3[n],
            up = new Vector3[n], distance = new float[n], bank = new float[n],
        };
        var dir = (b - a); var h = new Vector3(dir.x, 0f, dir.z);
        h = h.sqrMagnitude < 1e-6f ? Vector3.forward : h.normalized;
        var tan = dir.sqrMagnitude < 1e-6f ? Vector3.forward : dir.normalized;
        for (int k = 0; k < n; k++)
        {
            float t = k / (float)(n - 1);
            s.position[k] = Vector3.Lerp(a, b, t);
            s.tangent[k] = tan;
            s.side[k] = Vector3.Cross(Vector3.up, h).normalized;
            s.up[k] = Vector3.up;
            s.distance[k] = k == 0 ? 0f : s.distance[k - 1] + Vector3.Distance(s.position[k - 1], s.position[k]);
        }
        return s;
    }

    /// <summary>
    /// Switch a session to a variant (or back to the base course): ensures the registration, sets the lap plan
    /// (loop = LoopLaps, others = 1), selects the course and resets the ride. Returns false if the id is unknown.
    /// </summary>
    public static bool Select(RideSession session, string courseId)
    {
        if (session == null || !IsShuntaCourse(courseId)) return false;
        if (!Ensure()) return false;
        session.EnsureCourse();
        session.SelectCourse(courseId);
        if (session.Course == null || session.Course.Id != courseId) return false;
        session.autoLapsFromTarget = false;
        session.plannedLaps = courseId == LoopId ? LoopLaps : 1;
        session.ResetRide();
        return true;
    }
}

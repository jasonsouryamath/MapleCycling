using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime course provider for Shunta Metro. Converts the Shunta polyline (ShuntaRouteBuilder) into
/// RouteSegmentData (one per zone, so zone names drive the HUD section), a CourseData and 12
/// zone-end checkpoints, and appends them to an in-memory COPY of the baked RouteGraph. The baked
/// asset on disk is never modified; every other course is untouched.
/// Called from RouteGraph.Load() (one additive line).
/// </summary>
public static class ShuntaRouteProvider
{
    public const string CourseId = "shunta_metro";
    public const string SegmentPrefix = "shunta_z";
    /// <summary>Course-local metres -> world. Parks Shunta in empty world space (no other region within 5 km).</summary>
    public static readonly Vector3 WorldOffset = new Vector3(0f, 0f, 30000f);

    public static RouteGraph Augment(RouteGraph src)
    {
        if (src == null || src.Course(CourseId) != null) return src;
        try
        {
            var data = ShuntaCourseData.Load();
            if (data == null || data.zones.Length == 0) return src;
            if (!Sample(data, out var pos, out var zone)) return src;

            var g = Object.Instantiate(src);
            g.name = src.name;
            g.hideFlags = HideFlags.DontSave;

            var segs = new List<RouteSegmentData>(src.segments);
            var cps = new List<CheckpointData>(src.checkpoints);
            var legs = new List<CourseLegData>();

            int start = 0;
            for (int zi = 0; zi < data.zones.Length; zi++)
            {
                int z = data.zones[zi].index;
                int end = start;
                while (end < pos.Length - 1 && zone[end] == z) end++;   // first sample of the next zone (shared vertex)
                if (zi == data.zones.Length - 1) end = pos.Length - 1;
                if (end <= start) continue;
                var seg = MakeSegment(SegmentPrefix + z.ToString("00"), data.zones[zi].name, pos, start, end);
                segs.Add(seg);
                legs.Add(new CourseLegData { segment = seg.id, from = 0f, to = seg.Length });
                cps.Add(new CheckpointData
                {
                    segment = seg.id, distance = seg.Length,
                    displayName = string.IsNullOrEmpty(data.zones[zi].landmark) ? data.zones[zi].name : data.zones[zi].landmark,
                });
                start = end;
            }
            if (legs.Count == 0) return src;

            var courses = new List<CourseData>(src.courses)
            {
                new CourseData
                {
                    id = CourseId, displayName = data.displayName, regionId = RegionCatalog.ShuntaMetro,
                    closed = false, targetMinutes = 55f, legs = legs.ToArray(),
                }
            };
            g.segments = segs.ToArray();
            g.checkpoints = cps.ToArray();
            g.courses = courses.ToArray();
            return g;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[shunta] route provider failed, graph left unchanged: " + e.Message);
            return src;
        }
    }

    static bool Sample(ShuntaCourseData data, out Vector3[] pos, out int[] zone)
    {
        pos = null; zone = null;
        var go = new GameObject("shunta_provider_tmp") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);                       // inactive: OnEnable does not run until we say so
        var b = go.AddComponent<ShuntaRouteBuilder>();
        b.buildRibbon = false; b.buildGates = false;
        b.Rebuild();                               // single build; the object is never activated, so OnEnable cannot build again
        if (b.Positions.Length >= 2)
        {
            pos = new Vector3[b.Positions.Length];
            for (int i = 0; i < pos.Length; i++) pos[i] = b.Positions[i] + WorldOffset;
            zone = (int[])b.ZoneIndex.Clone();
        }
        if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
        return pos != null;
    }

    static RouteSegmentData MakeSegment(string id, string name, Vector3[] all, int a, int b)
    {
        int n = b - a + 1;
        var s = new RouteSegmentData
        {
            id = id, displayName = name,
            position = new Vector3[n], tangent = new Vector3[n], side = new Vector3[n],
            up = new Vector3[n], distance = new float[n], bank = new float[n],
        };
        float run = 0f;
        for (int k = 0; k < n; k++)
        {
            int i = a + k;
            s.position[k] = all[i];
            var f = all[Mathf.Min(i + 1, all.Length - 1)] - all[Mathf.Max(i - 1, 0)];
            s.tangent[k] = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
            var h = new Vector3(f.x, 0f, f.z);
            h = h.sqrMagnitude < 1e-6f ? Vector3.forward : h.normalized;
            s.side[k] = Vector3.Cross(Vector3.up, h).normalized;   // rider's right
            s.up[k] = Vector3.up;
            if (k > 0) run += Vector3.Distance(all[i - 1], all[i]);
            s.distance[k] = run;
        }
        return s;
    }
}

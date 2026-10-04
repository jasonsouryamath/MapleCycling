using System.Collections.Generic;
using UnityEngine;

/// <summary>A named point on a flattened course, at an absolute course arc length.</summary>
public struct CourseCheckpoint
{
    public float Distance;
    public string Name;
    public string Segment;
}

/// <summary>
/// A course definition flattened into one ridable polyline.
///
/// This is the single geometric truth every ride system reads: the physics samples
/// <see cref="GradeAt"/>, the rider transform is derived from <see cref="PositionAt"/>, the GPS
/// window draws <see cref="Position"/> and the trainer will be sent the same
/// <see cref="GradeAt"/> the gradient badge displays - so what the rider sees and what they feel
/// cannot drift apart.
/// </summary>
public sealed class RouteCourse
{
    public string Id = "";
    public string DisplayName = "";
    public bool Closed;
    public float TargetMinutes = 30f;

    public Vector3[] Position = System.Array.Empty<Vector3>();
    public Vector3[] Tangent = System.Array.Empty<Vector3>();
    public Vector3[] Side = System.Array.Empty<Vector3>();
    public Vector3[] Up = System.Array.Empty<Vector3>();
    public float[] Distance = System.Array.Empty<float>();
    public float[] Bank = System.Array.Empty<float>();
    /// <summary>Which segment each sample came from - drives the HUD's section name.</summary>
    public string[] SegmentOf = System.Array.Empty<string>();

    public CourseCheckpoint[] Checkpoints = System.Array.Empty<CourseCheckpoint>();

    public float Length;
    public float Ascent;
    public float MinElevation;
    public float MaxElevation;
    public Vector2 BoundsMin;      // world XZ
    public Vector2 BoundsMax;

    public int Count => Position.Length;

    // ------------------------------------------------------------------ construction

    public static RouteCourse Build(RouteGraph graph, CourseData def)
    {
        var c = new RouteCourse();
        if (graph == null || def == null) return c;

        c.Id = def.id;
        c.DisplayName = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        c.Closed = def.closed;
        c.TargetMinutes = def.targetMinutes;

        var pos = new List<Vector3>();
        var tan = new List<Vector3>();
        var side = new List<Vector3>();
        var up = new List<Vector3>();
        var dist = new List<float>();
        var bank = new List<float>();
        var segOf = new List<string>();
        var cps = new List<CourseCheckpoint>();

        float running = 0f;

        foreach (var leg in def.legs)
        {
            var seg = graph.Segment(leg.segment);
            if (seg == null || seg.Count < 2) continue;

            bool reversed = leg.from > leg.to;
            float lo = Mathf.Min(leg.from, leg.to);
            float hi = Mathf.Max(leg.from, leg.to);
            lo = Mathf.Clamp(lo, 0f, seg.Length);
            hi = Mathf.Clamp(hi, 0f, seg.Length);

            int i0 = seg.IndexAt(lo);
            int i1 = Mathf.Min(seg.IndexAt(hi) + 1, seg.Count - 1);
            if (i1 <= i0) continue;

            int legStartIndex = pos.Count;
            float legStartDistance = running;

            for (int k = 0; k <= i1 - i0; k++)
            {
                int i = reversed ? i1 - k : i0 + k;
                var p = seg.position[i];

                // Drop a duplicated junction vertex so the flattened polyline never carries a
                // zero-length span (which would make the arc-length search ambiguous).
                if (pos.Count > 0 && (p - pos[pos.Count - 1]).sqrMagnitude < 1e-4f) continue;

                if (pos.Count > 0) running += Vector3.Distance(pos[pos.Count - 1], p);

                pos.Add(p);
                tan.Add(reversed ? -seg.tangent[i] : seg.tangent[i]);
                side.Add(reversed ? -seg.side[i] : seg.side[i]);
                up.Add(seg.up[i]);
                bank.Add(reversed ? -seg.bank[i] : seg.bank[i]);
                dist.Add(running);
                segOf.Add(seg.displayName);
            }

            // Map every checkpoint that falls inside this leg onto the flattened arc.
            float legSpan = running - legStartDistance;
            if (legSpan > 0.01f && graph.checkpoints != null)
            {
                foreach (var cp in graph.checkpoints)
                {
                    if (cp.segment != leg.segment) continue;
                    if (cp.distance < lo - 0.5f || cp.distance > hi + 0.5f) continue;
                    float f = Mathf.InverseLerp(lo, hi, cp.distance);
                    if (reversed) f = 1f - f;
                    cps.Add(new CourseCheckpoint
                    {
                        Distance = legStartDistance + f * legSpan,
                        Name = cp.displayName,
                        Segment = seg.displayName,
                    });
                }
            }
            if (legStartIndex < 0) { /* keeps the compiler honest about the unused local */ }
        }

        c.Position = pos.ToArray();
        c.Tangent = tan.ToArray();
        c.Side = side.ToArray();
        c.Up = up.ToArray();
        c.Distance = dist.ToArray();
        c.Bank = bank.ToArray();
        c.SegmentOf = segOf.ToArray();
        c.Length = running;

        cps.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        c.Checkpoints = cps.ToArray();

        c.Recompute();
        return c;
    }

    private void Recompute()
    {
        if (Count == 0) return;
        Ascent = 0f;
        MinElevation = float.MaxValue;
        MaxElevation = float.MinValue;
        var lo = new Vector2(float.MaxValue, float.MaxValue);
        var hi = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < Count; i++)
        {
            var p = Position[i];
            if (i > 0) Ascent += Mathf.Max(0f, p.y - Position[i - 1].y);
            MinElevation = Mathf.Min(MinElevation, p.y);
            MaxElevation = Mathf.Max(MaxElevation, p.y);
            lo = Vector2.Min(lo, new Vector2(p.x, p.z));
            hi = Vector2.Max(hi, new Vector2(p.x, p.z));
        }
        BoundsMin = lo;
        BoundsMax = hi;
    }

    // ------------------------------------------------------------------ sampling

    /// <summary>Wraps a distance onto the course. Closed courses loop; open courses clamp.</summary>
    public float Wrap(float metres)
    {
        if (Length <= 0.001f) return 0f;
        if (!Closed) return Mathf.Clamp(metres, 0f, Length);
        metres %= Length;
        if (metres < 0f) metres += Length;
        return metres;
    }

    public int IndexAt(float metres)
    {
        int n = Distance.Length;
        if (n == 0) return 0;
        if (metres <= 0f) return 0;
        if (metres >= Distance[n - 1]) return n - 1;
        int lo = 0, hi = n - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Distance[mid] <= metres) lo = mid; else hi = mid;
        }
        return lo;
    }

    private void Frame(float metres, out int i, out int j, out float t)
    {
        metres = Wrap(metres);
        i = IndexAt(metres);
        j = Mathf.Min(i + 1, Count - 1);
        float span = Distance[j] - Distance[i];
        t = span > 1e-5f ? (metres - Distance[i]) / span : 0f;
    }

    public Vector3 PositionAt(float metres)
    {
        if (Count == 0) return Vector3.zero;
        Frame(metres, out int i, out int j, out float t);
        return Vector3.Lerp(Position[i], Position[j], t);
    }

    public Vector3 TangentAt(float metres)
    {
        if (Count == 0) return Vector3.forward;
        Frame(metres, out int i, out int j, out float t);
        var v = Vector3.Slerp(Tangent[i], Tangent[j], t);
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    public Vector3 SideAt(float metres)
    {
        if (Count == 0) return Vector3.right;
        Frame(metres, out int i, out int j, out float t);
        var v = Vector3.Slerp(Side[i], Side[j], t);
        return v.sqrMagnitude < 1e-6f ? Vector3.right : v.normalized;
    }

    public Vector3 UpAt(float metres)
    {
        if (Count == 0) return Vector3.up;
        Frame(metres, out int i, out int j, out float t);
        var v = Vector3.Slerp(Up[i], Up[j], t);
        return v.sqrMagnitude < 1e-6f ? Vector3.up : v.normalized;
    }

    public float BankAt(float metres)
    {
        if (Count == 0) return 0f;
        Frame(metres, out int i, out int j, out float t);
        return Mathf.Lerp(Bank[i], Bank[j], t);
    }

    public float ElevationAt(float metres) => PositionAt(metres).y;

    public string SegmentAt(float metres)
    {
        if (SegmentOf.Length == 0) return DisplayName;
        return SegmentOf[Mathf.Clamp(IndexAt(Wrap(metres)), 0, SegmentOf.Length - 1)];
    }

    /// <summary>
    /// Rise over run across a rolling window, so hairpin sample noise never makes the badge
    /// (or the trainer's simulated resistance) flicker.
    /// </summary>
    public float GradeAt(float metres, float windowM = 8f)
    {
        if (Count < 2) return 0f;
        float half = Mathf.Max(1f, windowM * 0.5f);
        float a = metres - half;
        float b = metres + half;
        if (!Closed)
        {
            a = Mathf.Clamp(a, 0f, Length);
            b = Mathf.Clamp(b, 0f, Length);
        }
        var pa = PositionAt(a);
        var pb = PositionAt(b);
        float run = new Vector2(pb.x - pa.x, pb.z - pa.z).magnitude;
        if (run < 0.05f) return 0f;
        return (pb.y - pa.y) / run;
    }

    /// <summary>Compass heading in degrees (0 = +Z / north, clockwise).</summary>
    public float HeadingAt(float metres)
    {
        var t = TangentAt(metres);
        return Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg;
    }

    /// <summary>Next checkpoint at or after <paramref name="metres"/>, wrapping if closed.</summary>
    public bool NextCheckpoint(float metres, out CourseCheckpoint cp, out float toGo)
    {
        cp = default;
        toGo = 0f;
        if (Checkpoints.Length == 0) return false;
        for (int i = 0; i < Checkpoints.Length; i++)
        {
            if (Checkpoints[i].Distance > metres + 0.5f)
            {
                cp = Checkpoints[i];
                toGo = cp.Distance - metres;
                return true;
            }
        }
        if (!Closed)
        {
            cp = Checkpoints[Checkpoints.Length - 1];
            toGo = Mathf.Max(0f, cp.Distance - metres);
            return true;
        }
        cp = Checkpoints[0];
        toGo = Length - metres + cp.Distance;
        return true;
    }
}

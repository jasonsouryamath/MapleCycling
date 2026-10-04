using System;
using System.Collections.Generic;
using UnityEngine;

public enum ShuntaForkBranch { Main = 0, Upper = 1, Lower = 2 }

/// <summary>One branch of the zone-9 interchange fork (design numbers; real lengths are measured from the polyline).</summary>
[Serializable]
public class ShuntaForkBranchDef
{
    public ShuntaForkBranch branch;
    public string name = "";
    /// <summary>
    /// 1 = follow the main route's curvature; &lt;1 cuts toward the fork-to-merge chord (shorter), &gt;1 exaggerates
    /// the bends (longer). Drives the generated polyline length.
    /// </summary>
    public float curvatureScale = 1f;
    /// <summary>Peak vertical offset from the main profile, metres (+ = deck above, - = underpass dip).</summary>
    public float peakElevationOffsetM;
    /// <summary>Fraction of the branch over which the vertical offset ramps up and down (smaller = steeper).</summary>
    public float rampFraction = 0.25f;
    /// <summary>Peak lateral separation from the main line, metres (+ = right of travel).</summary>
    public float lateralSeparationM;
    /// <summary>Surface grip multiplier on this branch (stacks with zone grip).</summary>
    public float grip = 1f;
    /// <summary>Speed multiplier from smoother/straighter surface; use as a mild free-speed bonus.</summary>
    public float speedFactor = 1f;
}

/// <summary>
/// Multi-level interchange fork in zone 9: the main route splits at <see cref="ForkKm"/> into a shorter,
/// steeper UPPER deck and a longer, faster LOWER deck that rejoin at <see cref="MergeKm"/> at the same
/// height as the main route. Pure data + polyline generation.
/// </summary>
public sealed class ShuntaInterchangeFork
{
    public float ForkKm;
    public float MergeKm;
    public ShuntaForkBranchDef Upper;
    public ShuntaForkBranchDef Lower;

    public float MainLengthKm => MergeKm - ForkKm;

    /// <summary>Defaults sit inside zone 9 (18.4-21.0 km): fork at 18.7, merge at 20.7.</summary>
    public static ShuntaInterchangeFork Default(ShuntaCourseData course)
    {
        float zs = 18.4f, ze = 21.0f;
        if (course != null && course.zones != null)
            foreach (var z in course.zones) if (z.id == "interchange") { zs = z.startKm; ze = z.endKm; }
        return new ShuntaInterchangeFork
        {
            ForkKm = zs + 0.3f,
            MergeKm = ze - 0.3f,
            Upper = new ShuntaForkBranchDef
            {
                branch = ShuntaForkBranch.Upper, name = "Upper Deck (short, steep)",
                curvatureScale = 0.86f, peakElevationOffsetM = 5f, rampFraction = 0.3f,
                lateralSeparationM = -18f, grip = 1f, speedFactor = 1f
            },
            Lower = new ShuntaForkBranchDef
            {
                branch = ShuntaForkBranch.Lower, name = "Lower Deck (long, fast)",
                curvatureScale = 1.14f, peakElevationOffsetM = -3f, rampFraction = 0.45f,
                lateralSeparationM = 18f, grip = 1f, speedFactor = 1.05f
            }
        };
    }

    public ShuntaForkBranchDef Def(ShuntaForkBranch b)
        => b == ShuntaForkBranch.Upper ? Upper : b == ShuntaForkBranch.Lower ? Lower : null;

    public bool InFork(float km) => km >= ForkKm && km <= MergeKm;

    /// <summary>Which branch a rider takes given lateral offset at the fork (negative = left/upper, positive = right/lower).</summary>
    public ShuntaForkBranch ChooseBranch(float lateralOffsetM) => lateralOffsetM <= 0f ? ShuntaForkBranch.Upper : ShuntaForkBranch.Lower;

    /// <summary>Position along the branch 0..1 for a main-route km.</summary>
    public float U(float km) => Mathf.Clamp01(Mathf.InverseLerp(ForkKm, MergeKm, km));

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    /// <summary>Bump 0..1..0 that rises over <paramref name="ramp"/> of the span, plateaus, then falls.</summary>
    static float Bump(float u, float ramp)
    {
        ramp = Mathf.Clamp(ramp, 0.05f, 0.5f);
        return Smooth(u / ramp) * Smooth((1f - u) / ramp);
    }

    /// <summary>Vertical offset of a branch above the main profile at fork parameter <paramref name="u"/>.</summary>
    public static float ElevationOffset(ShuntaForkBranchDef d, float u) => d.peakElevationOffsetM * Bump(u, d.rampFraction);

    /// <summary>
    /// Generates a branch polyline from the main route samples between the fork and merge.
    /// Horizontal: chord + (main - chord) * curvatureScale + lateral separation bump.
    /// Vertical: main height + elevation-offset bump. Endpoints coincide with the main route exactly.
    /// Returns points in the same local space as <paramref name="positions"/>.
    /// </summary>
    public Vector3[] BuildBranch(Vector3[] positions, float[] kms, ShuntaForkBranch which)
    {
        var d = Def(which);
        var list = new List<Vector3>();
        if (d == null || positions == null || kms == null || positions.Length < 2) return list.ToArray();

        Vector3 a = LerpAtKm(positions, kms, ForkKm), b = LerpAtKm(positions, kms, MergeKm);
        list.Add(a);
        for (int i = 0; i < kms.Length; i++)
            if (kms[i] > ForkKm && kms[i] < MergeKm) list.Add(positions[i]);
        list.Add(b);

        var res = new Vector3[list.Count];
        for (int i = 0; i < res.Length; i++)
        {
            float km = i == 0 ? ForkKm : i == res.Length - 1 ? MergeKm : KmOf(positions, kms, list[i], i);
            float u = U(km);
            var p = list[i];
            var chord = Vector3.Lerp(a, b, u);
            var h = new Vector3(p.x - chord.x, 0f, p.z - chord.z) * d.curvatureScale;
            var pos = new Vector3(chord.x + h.x, p.y, chord.z + h.z);

            // lateral separation perpendicular to the chord (right of travel = +)
            var dir = new Vector3(b.x - a.x, 0f, b.z - a.z);
            dir = dir.sqrMagnitude < 1e-6f ? Vector3.forward : dir.normalized;
            var right = new Vector3(dir.z, 0f, -dir.x);
            float sep = d.lateralSeparationM * Smooth(Mathf.Sin(u * Mathf.PI));
            pos += right * sep;
            pos.y += ElevationOffset(d, u);
            res[i] = pos;
        }
        return res;
    }

    // The interior samples are exactly positions[firstIdx..], so km lookup is by index, not search.
    float KmOf(Vector3[] positions, float[] kms, Vector3 p, int listIndex)
    {
        int first = 0;
        while (first < kms.Length && kms[first] <= ForkKm) first++;
        int idx = Mathf.Clamp(first + listIndex - 1, 0, kms.Length - 1);
        return kms[idx];
    }

    static Vector3 LerpAtKm(Vector3[] pos, float[] kms, float km)
    {
        int hi = Array.BinarySearch(kms, km);
        if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, kms.Length - 1);
        float t = Mathf.InverseLerp(kms[hi - 1], kms[hi], km);
        return Vector3.Lerp(pos[hi - 1], pos[hi], t);
    }

    public static float PolylineLength(Vector3[] p)
    {
        float l = 0f;
        for (int i = 1; i < p.Length; i++) l += Vector3.Distance(p[i - 1], p[i]);
        return l;
    }

    /// <summary>Steepest grade (percent) over ~<paramref name="windowM"/> windows along a polyline.</summary>
    public static float MaxGradePercent(Vector3[] p, float windowM = 60f)
    {
        float worst = 0f;
        for (int i = 0; i < p.Length; i++)
        {
            float run = 0f; int j = i;
            while (j + 1 < p.Length && run < windowM) { run += HDist(p[j], p[j + 1]); j++; }
            if (run < windowM * 0.8f) break;
            worst = Mathf.Max(worst, Mathf.Abs(p[j].y - p[i].y) / run * 100f);
        }
        return worst;
    }

    static float HDist(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

    /// <summary>
    /// Rough steady-state time (seconds) over a polyline at constant power: per segment, solves
    /// P*eff = (drag + roll + gravity) * v by bisection, then multiplies speed by <paramref name="speedFactor"/>.
    /// Used for design balancing/tests only; the real sim is CyclingPhysics.
    /// </summary>
    public static float EstimateSeconds(Vector3[] p, float watts, float speedFactor = 1f, float massKg = 82f, float cdA = 0.32f)
    {
        float t = 0f;
        for (int i = 1; i < p.Length; i++)
        {
            float len = Vector3.Distance(p[i - 1], p[i]);
            if (len < 1e-3f) continue;
            float sinT = (p[i].y - p[i - 1].y) / len;
            float cosT = Mathf.Sqrt(Mathf.Max(0f, 1f - sinT * sinT));
            float lo = 0.5f, hi = 22f;
            for (int k = 0; k < 40; k++)
            {
                float v = 0.5f * (lo + hi);
                float need = (0.5f * 1.225f * cdA * v * v + 0.005f * massKg * 9.80665f * cosT + massKg * 9.80665f * sinT) * v / 0.975f;
                if (need > watts) hi = v; else lo = v;
            }
            float speed = Mathf.Min(0.5f * (lo + hi), 58f / 3.6f) * speedFactor;
            t += len / Mathf.Max(speed, 0.5f);
        }
        return t;
    }
}

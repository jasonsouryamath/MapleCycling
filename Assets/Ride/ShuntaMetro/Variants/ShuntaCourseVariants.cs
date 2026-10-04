using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One derived course: per-lap data in its own km space (0..lapKm).</summary>
public sealed class ShuntaVariant
{
    public string id = "", displayName = "";
    public float lapKm;                 // one lap, including any closing connector
    public int laps = 1;
    public bool closedLoop;
    public float connectorKm;           // closing connector length (loop only)
    public float gainM;                 // per lap
    public ShuntaZone[] zones = Array.Empty<ShuntaZone>();   // keep ORIGINAL index/id (weather/time lookups use them)
    public ShuntaWaypoint[] waypoints = Array.Empty<ShuntaWaypoint>();
    public ShuntaElevationKey[] elevation = Array.Empty<ShuntaElevationKey>();
    public float TotalKm => lapKm * laps;
    public float TotalGainM => gainM * laps;

    /// <summary>Elevation at a lap-local km, using the base course's monotone interpolation.</summary>
    public float ElevationAtKm(float km)
        => ShuntaCourseData.EvaluateElevation(elevation, km);

    /// <summary>Zone at lap-local km (wraps for multi-lap if km exceeds one lap).</summary>
    public ShuntaZone ZoneAtKm(float km)
    {
        if (zones.Length == 0) return null;
        if (lapKm > 0f && km > lapKm) km = Mathf.Repeat(km, lapKm);
        for (int i = 0; i < zones.Length; i++) if (km <= zones[i].endKm) return zones[i];
        return zones[zones.Length - 1];
    }

    /// <summary>Grade in percent over a 100 m window (+ uphill in the direction of travel).</summary>
    public float GradePercentAt(float km)
        => (ElevationAtKm(km + 0.05f) - ElevationAtKm(km - 0.05f)) / 100f * 100f;

    /// <summary>Wrap this variant into a plain ShuntaCourseData so existing builders can consume it.</summary>
    public ShuntaCourseData ToCourseData(ShuntaCourseData baseData)
    {
        return new ShuntaCourseData
        {
            id = id, displayName = displayName, tagline = baseData != null ? baseData.tagline : "",
            distanceKm = lapKm, targetGainM = gainM, pointToPoint = !closedLoop,
            difficultyStars = baseData != null ? baseData.difficultyStars : 2.5f,
            start = baseData != null ? baseData.start : "", finish = baseData != null ? baseData.finish : "",
            zones = zones, waypoints = waypoints, elevation = elevation,
            climbs = Array.Empty<ShuntaClimb>(), landmarks = Array.Empty<ShuntaLandmark>(),
        };
    }

    /// <summary>Sanity checks; returns human-readable failures (empty = valid).</summary>
    public List<string> Validate()
    {
        var f = new List<string>();
        string p = "[" + id + "] ";
        if (lapKm <= 0.5f) f.Add(p + "lapKm too small: " + lapKm);
        if (laps < 1) f.Add(p + "laps < 1");
        if (zones.Length == 0) f.Add(p + "no zones");
        else
        {
            if (Mathf.Abs(zones[0].startKm) > 1e-3f) f.Add(p + "first zone must start at 0");
            if (Mathf.Abs(zones[zones.Length - 1].endKm - lapKm) > 0.01f) f.Add(p + "last zone end != lapKm");
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i].endKm <= zones[i].startKm) f.Add(p + "zone " + zones[i].index + " has non-positive length");
                if (i > 0 && Mathf.Abs(zones[i].startKm - zones[i - 1].endKm) > 1e-3f) f.Add(p + "gap/overlap before zone " + zones[i].index);
            }
        }
        if (waypoints.Length < 2) f.Add(p + "need >= 2 waypoints");
        if (elevation.Length < 2) f.Add(p + "need >= 2 elevation keys");
        else
        {
            for (int i = 1; i < elevation.Length; i++)
                if (elevation[i].km <= elevation[i - 1].km) { f.Add(p + "elevation km not strictly increasing at " + i); break; }
            if (Mathf.Abs(elevation[elevation.Length - 1].km - lapKm) > 0.01f) f.Add(p + "elevation does not span lapKm");
            float g = 0f;
            for (int i = 0; i < elevation.Length - 1; i++) g += Mathf.Max(0f, elevation[i + 1].m - elevation[i].m);
            if (Mathf.Abs(g - gainM) > 0.5f) f.Add(p + "gainM " + gainM + " != keypoint gain " + g);
            if (closedLoop && Mathf.Abs(elevation[0].m - elevation[elevation.Length - 1].m) > 0.01f) f.Add(p + "closed loop elevation does not close");
        }
        for (int i = 0; i < waypoints.Length; i++)
            if (float.IsNaN(waypoints[i].x) || float.IsNaN(waypoints[i].z)) { f.Add(p + "NaN waypoint " + i); break; }
        if (closedLoop && waypoints.Length >= 2)
        {
            var a = waypoints[0]; var b = waypoints[waypoints.Length - 1];
            if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z) > 0.01f) f.Add(p + "closed loop waypoints do not close");
            if (connectorKm <= 0f) f.Add(p + "closed loop has no connector");
        }
        return f;
    }
}

/// <summary>Pure data/logic: derives variant courses from the base Shunta Metro course.</summary>
public static class ShuntaCourseVariants
{
    public const string ShortLoopId = "shunta_short_loop", ReverseId = "shunta_reverse", SprintCutId = "shunta_sprint_cut";

    public static ShuntaVariant Full(ShuntaCourseData d)
        => Slice(d, 0f, d.distanceKm, "shunta_metro", d.displayName, 1);

    /// <summary>Shibuya -> Ginza (zones 1-6) as a closed loop with a computed closing connector.</summary>
    public static ShuntaVariant ShortLoop(ShuntaCourseData d, int laps = 3, int lastZone = 6)
    {
        float endKm = EndOfZone(d, lastZone);
        var v = Slice(d, 0f, endKm, ShortLoopId, "Short Loop", Mathf.Max(1, laps));
        var wp = new List<ShuntaWaypoint>(v.waypoints);
        var first = wp[0]; var last = wp[wp.Count - 1];
        float scale = KmPerUnit(d);
        float conn = Mathf.Max(0.05f, Mathf.Sqrt(Sq(first.x - last.x) + Sq(first.z - last.z)) * scale);
        wp.Add(new ShuntaWaypoint { x = first.x, z = first.z, zone = last.zone });
        v.waypoints = wp.ToArray();

        float lapKm = endKm + conn;
        var el = new List<ShuntaElevationKey>(v.elevation);
        el.Add(new ShuntaElevationKey { km = lapKm, m = el[0].m });   // connector ramps back to the start height
        v.elevation = el.ToArray();
        var z = (ShuntaZone[])v.zones.Clone();
        z[z.Length - 1] = CloneZone(z[z.Length - 1], z[z.Length - 1].startKm, lapKm);
        v.zones = z;
        v.lapKm = lapKm; v.connectorKm = conn; v.closedLoop = true; v.gainM = Gain(v.elevation);
        return v;
    }

    /// <summary>Odaiba -> Shibuya: whole course mirrored. Grades flip sign, zone order reverses.</summary>
    public static ShuntaVariant Reverse(ShuntaCourseData d)
    {
        var f = Full(d);
        float D = f.lapKm;
        var v = new ShuntaVariant { id = ReverseId, displayName = "Reverse", lapKm = D, laps = 1 };
        int n = f.zones.Length;
        v.zones = new ShuntaZone[n];
        for (int i = 0; i < n; i++)
        {
            var z = f.zones[n - 1 - i];
            v.zones[i] = CloneZone(z, D - z.endKm, D - z.startKm);
        }
        v.waypoints = new ShuntaWaypoint[f.waypoints.Length];
        for (int i = 0; i < v.waypoints.Length; i++) v.waypoints[i] = f.waypoints[f.waypoints.Length - 1 - i];
        v.elevation = new ShuntaElevationKey[f.elevation.Length];
        for (int i = 0; i < v.elevation.Length; i++)
        {
            var k = f.elevation[f.elevation.Length - 1 - i];
            v.elevation[i] = new ShuntaElevationKey { km = D - k.km, m = k.m };
        }
        v.elevation[0].km = 0f; v.elevation[v.elevation.Length - 1].km = D;
        v.gainM = Gain(v.elevation);
        return v;
    }

    /// <summary>Zones 9-12 only (Tokyo Bay Bridge to the sakura finish).</summary>
    public static ShuntaVariant SprintCut(ShuntaCourseData d, int firstZone = 9, int lastZone = 12)
        => Slice(d, StartOfZone(d, firstZone), EndOfZone(d, lastZone), SprintCutId, "Sprint Cut", 1);

    // ---- helpers ----
    static float StartOfZone(ShuntaCourseData d, int idx) { foreach (var z in d.zones) if (z.index == idx) return z.startKm; return 0f; }
    static float EndOfZone(ShuntaCourseData d, int idx) { foreach (var z in d.zones) if (z.index == idx) return z.endKm; return d.distanceKm; }
    static ShuntaZone CloneZone(ShuntaZone z, float s, float e)
        => new ShuntaZone { index = z.index, id = z.id, name = z.name, landmark = z.landmark, startKm = s, endKm = e, color = z.color, timeOfDay = z.timeOfDay, surface = z.surface, grip = z.grip, draftMultiplier = z.draftMultiplier };
    static float Gain(ShuntaElevationKey[] e)
    {
        float g = 0f;
        for (int i = 0; i < e.Length - 1; i++) g += Mathf.Max(0f, e[i + 1].m - e[i].m);
        return g;
    }
    static float PolyLen(ShuntaWaypoint[] w)
    {
        float t = 0f;
        for (int i = 1; i < w.Length; i++) t += Mathf.Sqrt(Sq(w[i].x - w[i - 1].x) + Sq(w[i].z - w[i - 1].z));
        return t;
    }
    static float Sq(float a) => a * a;
    static float KmPerUnit(ShuntaCourseData d) { float l = PolyLen(d.waypoints); return l > 1e-3f ? d.distanceKm / l : 0.001f; }

    /// <summary>Cut [a,b] km out of the base course, shifted to start at 0. Waypoint km = polyline length scaled to the course distance.</summary>
    static ShuntaVariant Slice(ShuntaCourseData d, float a, float b, string id, string name, int laps)
    {
        var v = new ShuntaVariant { id = id, displayName = name, lapKm = b - a, laps = laps };
        var zs = new List<ShuntaZone>();
        foreach (var z in d.zones)
        {
            if (z.endKm <= a + 1e-4f || z.startKm >= b - 1e-4f) continue;
            zs.Add(CloneZone(z, Mathf.Max(z.startKm, a) - a, Mathf.Min(z.endKm, b) - a));
        }
        v.zones = zs.ToArray();

        var el = new List<ShuntaElevationKey> { new ShuntaElevationKey { km = 0f, m = d.ElevationAtKm(a) } };
        foreach (var k in d.elevation) if (k.km > a + 1e-3f && k.km < b - 1e-3f) el.Add(new ShuntaElevationKey { km = k.km - a, m = k.m });
        el.Add(new ShuntaElevationKey { km = b - a, m = d.ElevationAtKm(b) });
        v.elevation = el.ToArray();
        v.gainM = Gain(v.elevation);

        var w = d.waypoints; float scale = KmPerUnit(d);
        var km = new float[w.Length];
        for (int i = 1; i < w.Length; i++) km[i] = km[i - 1] + Mathf.Sqrt(Sq(w[i].x - w[i - 1].x) + Sq(w[i].z - w[i - 1].z)) * scale;
        var wl = new List<ShuntaWaypoint> { Interp(w, km, a) };
        for (int i = 0; i < w.Length; i++) if (km[i] > a + 1e-3f && km[i] < b - 1e-3f) wl.Add(w[i]);
        wl.Add(Interp(w, km, b));
        v.waypoints = wl.ToArray();
        return v;
    }

    static ShuntaWaypoint Interp(ShuntaWaypoint[] w, float[] km, float at)
    {
        if (w.Length == 0) return new ShuntaWaypoint();
        if (at <= km[0]) return w[0];
        for (int i = 1; i < w.Length; i++)
        {
            if (at > km[i]) continue;
            float t = Mathf.InverseLerp(km[i - 1], km[i], at);
            return new ShuntaWaypoint { x = Mathf.Lerp(w[i - 1].x, w[i].x, t), z = Mathf.Lerp(w[i - 1].z, w[i].z, t), zone = t < 0.5f ? w[i - 1].zone : w[i].zone };
        }
        return w[w.Length - 1];
    }
}

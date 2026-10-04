using System.Collections.Generic;
using UnityEngine;

/// <summary>Editor-free self-check for the Shunta variants. Run() returns failures (empty = pass).</summary>
public static class ShuntaVariantsSelfCheck
{
    public static List<string> Run()
    {
        var f = new List<string>();
        var d = ShuntaCourseData.Load();
        if (d == null) { f.Add("base course failed to load"); return f; }

        var loop = ShuntaCourseVariants.ShortLoop(d);
        var rev = ShuntaCourseVariants.Reverse(d);
        var spr = ShuntaCourseVariants.SprintCut(d);
        var all = new[] { ShuntaCourseVariants.Full(d), loop, rev, spr };
        foreach (var v in all) f.AddRange(v.Validate());

        if (!loop.closedLoop || loop.laps < 2) f.Add("short loop should be multi-lap closed");
        if (loop.connectorKm <= 0f) f.Add("short loop connector missing");
        if (loop.zones[0].index != 1) f.Add("short loop must start in zone 1");
        if (spr.zones.Length == 0 || spr.zones[0].index != 9 || spr.zones[spr.zones.Length - 1].index != 12) f.Add("sprint cut must be zones 9-12");
        if (Mathf.Abs(spr.lapKm - (d.distanceKm - 18.4f)) > 0.05f) f.Add("sprint cut distance wrong: " + spr.lapKm);
        if (rev.zones[0].index != 12 || rev.zones[rev.zones.Length - 1].index != 1) f.Add("reverse zone order wrong");
        if (Mathf.Abs(rev.lapKm - d.distanceKm) > 0.01f) f.Add("reverse distance changed");
        float D = d.distanceKm;
        for (float km = 0.3f; km < D - 0.1f; km += 1.7f)
        {
            if (Mathf.Abs(rev.ElevationAtKm(km) - d.ElevationAtKm(D - km)) > 0.01f) { f.Add("reverse elevation not mirrored at km " + km); break; }
            float gFwd = d.ElevationAtKm(D - km + 0.05f) - d.ElevationAtKm(D - km - 0.05f);
            if (Mathf.Abs(rev.GradePercentAt(km) + gFwd) > 0.05f) { f.Add("reverse grade sign not flipped at km " + km); break; }
        }

        // time of day
        var dusk = ShuntaTimeOfDay.Sample(0f, D);
        var mid = ShuntaTimeOfDay.Sample(D * 0.5f, D);
        var end = ShuntaTimeOfDay.Sample(D, D);
        if (dusk.sunElevationDeg < -2f || dusk.sunElevationDeg > 8f) f.Add("default start should be golden hour, elev=" + dusk.sunElevationDeg);
        if (mid.neonIntensity < 0.9f) f.Add("midnight neon should be ~1, got " + mid.neonIntensity);
        if (end.neonIntensity > 0.9f) f.Add("dawn neon should have faded");
        if (ShuntaTimeOfDay.Sample(0f, D, 22f).neonIntensity < 0.9f) f.Add("22:00 start should be neon night");
        float prevNeon = -1f;
        for (float km = 0f; km <= D; km += 0.1f)
        {
            var s = ShuntaTimeOfDay.Sample(km, D);
            if (float.IsNaN(s.sunElevationDeg) || s.neonIntensity < 0f || s.neonIntensity > 1f) { f.Add("time-of-day out of range at km " + km); break; }
            if (prevNeon >= 0f && Mathf.Abs(s.neonIntensity - prevNeon) > 0.12f) { f.Add("neon curve jumps at km " + km); break; }
            prevNeon = s.neonIntensity;
        }

        // weather
        var full = all[0];
        float r7 = ShuntaWeatherPlan.RainAtKm(full, 15f), r1 = ShuntaWeatherPlan.RainAtKm(full, 1f), r12 = ShuntaWeatherPlan.RainAtKm(full, 27f);
        if (r7 < 0.8f) f.Add("zone 7-8 rain should be heavy, got " + r7);
        if (r1 > 0.01f || r12 > 0.01f) f.Add("rain should be dry at start/finish");
        float prev = ShuntaWeatherPlan.RainAtKm(full, 0f);
        for (float km = 0.05f; km <= D; km += 0.05f)
        {
            float r = ShuntaWeatherPlan.RainAtKm(full, km);
            if (r < 0f || r > 1f || Mathf.Abs(r - prev) > 0.06f) { f.Add("rain curve discontinuous at km " + km); break; }
            prev = r;
        }
        var w19 = ShuntaWeatherPlan.Sample(full, 19f);
        if (w19.wetness <= w19.rain) f.Add("wetness should lag rain after zone 8");
        return f;
    }

    public static void RunAndLog()
    {
        var f = Run();
        if (f.Count == 0) Debug.Log("[shunta-variants] SELF-CHECK PASS");
        else Debug.LogError("[shunta-variants] SELF-CHECK FAIL (" + f.Count + "):\n" + string.Join("\n", f));
    }
}

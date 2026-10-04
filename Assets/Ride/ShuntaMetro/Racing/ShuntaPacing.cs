using System;

/// <summary>Situation handed to the pacing model each tick.</summary>
public struct ShuntaPacingContext
{
    public float kmDone;
    public float kmToGo;
    public float gradePercent;
    /// <summary>1-based zone index (1..12) from shunta_metro_course.json.</summary>
    public int zoneIndex;
    /// <summary>Remaining anaerobic budget in joules (0..wPrimeJ).</summary>
    public float wBalJ;
    /// <summary>True when sitting in a wheel (draft reduction above ~10%).</summary>
    public bool drafting;
}

/// <summary>
/// Energy-budget pacing: spend W' on the Elevated Ramp (zone 3), Skyline rise (5), Rainbow Bridge (10)
/// and the Odaiba finish rise (12), sit in wheels in the tunnel (4) and recover, hold back in the rain
/// (7-8), then empty the tank in the final sprint. Pure function, no engine state.
/// </summary>
public static class ShuntaPacing
{
    public const float SprintDistanceM = 450f;
    public const float BaseCornerCapMps = 13.5f;

    /// <summary>True for the zones where an aggressive rider spends anaerobic budget.</summary>
    public static bool IsSpendZone(int zone) => zone == 3 || zone == 5 || zone == 10 || zone == 12;

    /// <summary>Target power in watts for this tick (before the W' empty clamp the sim applies).</summary>
    public static float TargetWatts(in ShuntaRacerProfile p, in ShuntaPacingContext c)
    {
        float wFrac = p.wPrimeJ > 0f ? Math.Clamp(c.wBalJ / p.wPrimeJ, 0f, 1f) : 0f;
        float toGoM = c.kmToGo * 1000f;

        // Final sprint: empty the budget; wheel-suckers wait longest.
        if (toGoM <= SprintDistanceM * (0.8f + 0.2f * p.draftUsage) && wFrac > 0.05f)
            return p.sprintPeakW * (0.82f + 0.18f * wFrac);

        float m = 0.86f;                                  // steady rolling intensity (fraction of FTP)
        if (c.kmToGo < 3f) m = 0.95f + 0.05f * wFrac;     // closing 3 km: everyone lifts the baseline

        // Grade response: everybody pushes uphill a bit, lets off downhill.
        if (c.gradePercent > 1.5f) m += Math.Min(0.14f, (c.gradePercent - 1.5f) * 0.030f);
        else if (c.gradePercent < -1.5f) m -= Math.Min(0.34f, (-c.gradePercent - 1.5f) * 0.12f);

        // Spend zones.
        if (IsSpendZone(c.zoneIndex) && wFrac > 0.30f && c.kmToGo > 1.0f)
            m += 0.28f * p.spendBias * (0.4f + 0.6f * p.riskAppetite) * (c.gradePercent > 0.5f ? 1f : 0.35f);

        // Tunnel: find a wheel and recover.
        if (c.zoneIndex == 4)
            m = c.drafting ? Math.Min(m, 0.80f) : Math.Max(m, 0.90f + 0.05f * (1f - p.draftUsage));

        // Wet streets and under-railway: no spending, brake instead of power.
        if (c.zoneIndex == 7 || c.zoneIndex == 8) m = Math.Min(m, 0.84f);

        // Sitting in: save legs, the wheelsucker most of all.
        if (c.drafting && c.zoneIndex != 4) m -= 0.04f * p.draftUsage;

        // Low budget: ease back so the budget can refill.
        if (wFrac < 0.15f) m = Math.Min(m, 0.90f);

        return p.ftpW * Math.Clamp(m, 0.40f, 1.45f);
    }

    /// <summary>Corner speed cap in m/s for a zone (grip from the course json). Wet zones brake hard.</summary>
    public static float CornerSpeedCapMps(in ShuntaRacerProfile p, float zoneGrip)
    {
        float skill = 0.88f + 0.22f * p.cornering;
        float bold = 0.96f + 0.08f * p.riskAppetite;
        return BaseCornerCapMps * zoneGrip * skill * bold;
    }

    /// <summary>Chance per wet-zone pass of a slide costing about 10 s (bold + unskilled = worse).</summary>
    public static float WetMishapChance(in ShuntaRacerProfile p)
        => 0.02f + 0.06f * p.riskAppetite * (1.2f - p.cornering);
}

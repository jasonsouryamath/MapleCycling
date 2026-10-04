using System;

/// <summary>AI rider archetypes for Shunta Metro. Pure data, no engine dependencies.</summary>
public enum ShuntaArchetype { Sprinter, Puncheur, Climber, Rouleur, Breakaway, Wheelsucker }

/// <summary>
/// One AI rider's capabilities. Units follow CyclingPhysics: watts, kg, m/s. "WPrimeJ" is the
/// anaerobic work budget above FTP (W' style), "SprintPeakW" the 10 s peak.
/// </summary>
[Serializable]
public struct ShuntaRacerProfile
{
    public ShuntaArchetype archetype;
    public float ftpW;
    public float massKg;
    public float wPrimeJ;
    public float sprintPeakW;
    /// <summary>0..1: how eagerly the rider sits on wheels (scales draft benefit and the hunt for a wheel).</summary>
    public float draftUsage;
    /// <summary>0..1: cornering skill; raises the corner speed cap, most visible in wet zones 7-8.</summary>
    public float cornering;
    /// <summary>0..1: willingness to spend W' early and to take marginal lines (also mishap odds in the wet).</summary>
    public float riskAppetite;
    /// <summary>0..1: how much extra W' the rider burns on the ramp / bridge / finish-rise spend zones.</summary>
    public float spendBias;
    /// <summary>Per-rider cdA (m^2) at upright racing position.</summary>
    public float CdA => 0.30f * (float)Math.Pow(massKg / 72f, 0.66);
}

public static class ShuntaRacerArchetypes
{
    public static readonly ShuntaArchetype[] All =
    {
        ShuntaArchetype.Sprinter, ShuntaArchetype.Puncheur, ShuntaArchetype.Climber,
        ShuntaArchetype.Rouleur, ShuntaArchetype.Breakaway, ShuntaArchetype.Wheelsucker
    };

    /// <summary>Nominal profile for an archetype (tuned by ShuntaMetroRaceSim so no type dominates).</summary>
    public static ShuntaRacerProfile Nominal(ShuntaArchetype a)
    {
        var p = new ShuntaRacerProfile { archetype = a };
        switch (a)
        {
            case ShuntaArchetype.Sprinter:
                p.ftpW = 358f; p.massKg = 82f; p.wPrimeJ = 24000f; p.sprintPeakW = 1250f;
                p.draftUsage = 0.9f; p.cornering = 0.6f; p.riskAppetite = 0.6f; p.spendBias = 0.3f; break;
            case ShuntaArchetype.Puncheur:
                p.ftpW = 315f; p.massKg = 72f; p.wPrimeJ = 24000f; p.sprintPeakW = 1050f;
                p.draftUsage = 0.7f; p.cornering = 0.7f; p.riskAppetite = 0.7f; p.spendBias = 0.8f; break;
            case ShuntaArchetype.Climber:
                p.ftpW = 273f; p.massKg = 60f; p.wPrimeJ = 17000f; p.sprintPeakW = 850f;
                p.draftUsage = 0.6f; p.cornering = 0.55f; p.riskAppetite = 0.4f; p.spendBias = 0.7f; break;
            case ShuntaArchetype.Rouleur:
                p.ftpW = 335f; p.massKg = 76f; p.wPrimeJ = 20000f; p.sprintPeakW = 950f;
                p.draftUsage = 0.7f; p.cornering = 0.75f; p.riskAppetite = 0.5f; p.spendBias = 0.4f; break;
            case ShuntaArchetype.Breakaway:
                p.ftpW = 307f; p.massKg = 72f; p.wPrimeJ = 26000f; p.sprintPeakW = 900f;
                p.draftUsage = 0.3f; p.cornering = 0.7f; p.riskAppetite = 0.9f; p.spendBias = 1.0f; break;
            default: // Wheelsucker
                p.ftpW = 321f; p.massKg = 70f; p.wPrimeJ = 18000f; p.sprintPeakW = 1050f;
                p.draftUsage = 1.0f; p.cornering = 0.6f; p.riskAppetite = 0.3f; p.spendBias = 0.2f; break;
        }
        return p;
    }
}

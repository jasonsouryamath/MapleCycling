using System.Collections.Generic;

/// <summary>Snapshot of a rider's manoeuvre for the fair-play scorer. Distances in metres, rates in m/s.</summary>
public struct ShuntaFairPlayContext
{
    public int zoneIndex;
    /// <summary>Absolute lateral offset from the line the rider held a moment ago (m).</summary>
    public float lateralDriftM;
    /// <summary>Rider's current absolute lateral speed (m/s).</summary>
    public float lateralSpeedMps;
    /// <summary>Lateral gap to the nearest rider alongside (m); large number if nobody beside.</summary>
    public float sideGapM;
    /// <summary>Longitudinal gap to a rider just behind that this rider is moving in front of (m); large if none.</summary>
    public float cutInGapBehindM;
    /// <summary>True when the rider braked hard with a follower within 3 m.</summary>
    public bool hardBrakeWithFollower;
    /// <summary>Rider risk appetite 0..1 (bold riders get a little less benefit of the doubt).</summary>
    public float riskAppetite;
}

public struct ShuntaFairPlayResult
{
    /// <summary>1 = clean, 0 = dirty. Riders below <see cref="ShuntaFairPlay.AcceptThreshold"/> back off.</summary>
    public float score;
    public List<string> reasons;
    public bool Acceptable => score >= ShuntaFairPlay.AcceptThreshold;
}

/// <summary>Pure fair-play decision scorer: hold line in the tunnel, no squeezing at the interchange, no wheel-chop.</summary>
public static class ShuntaFairPlay
{
    public const float AcceptThreshold = 0.6f;
    public const int TunnelZone = 4, InterchangeZone = 9;

    public static ShuntaFairPlayResult Score(in ShuntaFairPlayContext c)
    {
        float s = 1f;
        var why = new List<string>();

        if (c.zoneIndex == TunnelZone && c.lateralDriftM > 0.5f)
        {
            s -= System.Math.Min(0.55f, 0.35f + (c.lateralDriftM - 0.5f) * 0.2f);
            why.Add("tunnel: left the held line");
        }
        if (c.zoneIndex == InterchangeZone && c.sideGapM < 0.9f)
        {
            s -= System.Math.Min(0.5f, 0.3f + (0.9f - c.sideGapM) * 0.4f);
            why.Add("interchange: squeezing a rider alongside");
        }
        if (c.cutInGapBehindM < 1.5f && c.lateralSpeedMps > 0.6f)
        {
            s -= 0.45f;
            why.Add("wheel-chop: cut across a rider's front wheel");
        }
        if (c.hardBrakeWithFollower)
        {
            s -= 0.2f;
            why.Add("hard brake in front of a close follower");
        }
        s += (0.5f - c.riskAppetite) * 0.04f;   // bold riders: marginally harsher
        if (why.Count == 0) why.Add("clean line");
        return new ShuntaFairPlayResult { score = System.Math.Clamp(s, 0f, 1f), reasons = why };
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct ShuntaChevron
{
    public int id;
    public float km;
    /// <summary>Lateral offset from the road centreline, metres (+ = right of travel).</summary>
    public float lateralM;
}

/// <summary>
/// Neon boost chevron pickups for zone 6 (High-Speed Descent). Placement is deterministic (fixed
/// seed, no UnityEngine.Random) so every rider and every run sees the same chevrons.
/// </summary>
public static class ShuntaChevronLayout
{
    public const int Count = 8;
    public const float MinSpacingKm = 0.2f;
    static readonly float[] Lanes = { -1.8f, 0f, 1.8f };

    /// <summary>Evenly spread along the zone (inset 0.2 km at both ends) with a seeded lane pattern.</summary>
    public static ShuntaChevron[] Place(ShuntaCourseData course, int seed = 6006)
    {
        float s = 11.6f, e = 13.8f;
        if (course != null && course.zones != null)
            foreach (var z in course.zones) if (z.id == "high_speed_descent") { s = z.startKm; e = z.endKm; }
        s += 0.2f; e -= 0.2f;
        var res = new ShuntaChevron[Count];
        uint state = (uint)seed * 2654435761u + 1u;
        int lastLane = 1;
        for (int i = 0; i < Count; i++)
        {
            float km = Mathf.Lerp(s, e, i / (float)(Count - 1));
            state = state * 1664525u + 1013904223u;
            int lane = (int)((state >> 16) % 3u);
            if (lane == lastLane) lane = (lane + 1) % 3; // always shift lane: weave rewards steering
            lastLane = lane;
            res[i] = new ShuntaChevron { id = i, km = km, lateralM = Lanes[lane] };
        }
        return res;
    }
}

/// <summary>
/// Rider-side boost state: collect a chevron to get a short speed boost, then a cooldown during which
/// further chevrons are ignored (they stay lit for the next lap/ghost). Drive with <see cref="Tick"/> every frame.
/// </summary>
public sealed class ShuntaBoostState
{
    public float boostSeconds = 3f;
    public float cooldownSeconds = 8f;
    /// <summary>Peak speed multiplier (1.12 = +12 % road speed). Eases out over the boost.</summary>
    public float peakSpeedMultiplier = 1.12f;
    public float pickupRadiusAlongM = 3.5f;
    public float pickupRadiusLateralM = 1.6f;

    readonly ShuntaChevron[] _chevrons;
    readonly bool[] _taken;
    float _boostLeft, _cooldownLeft;

    public int Collected { get; private set; }
    public bool Boosting => _boostLeft > 0f;
    public bool OnCooldown => _cooldownLeft > 0f;
    public float CooldownLeft => _cooldownLeft;
    public bool IsTaken(int id) => _taken[id];

    public ShuntaBoostState(ShuntaChevron[] chevrons)
    {
        _chevrons = chevrons ?? Array.Empty<ShuntaChevron>();
        _taken = new bool[_chevrons.Length];
    }

    /// <summary>Speed multiplier to apply to rider speed/power this frame (1 when idle).</summary>
    public float SpeedMultiplier
    {
        get
        {
            if (_boostLeft <= 0f) return 1f;
            float t = _boostLeft / boostSeconds; // 1 -> 0
            return 1f + (peakSpeedMultiplier - 1f) * (t * (2f - t)); // ease: strong start, soft tail
        }
    }

    /// <summary>
    /// Advance by dt. <paramref name="riderKm"/> is the rider's distance along the route, <paramref name="riderLateralM"/>
    /// the lateral offset from the centreline; pass <c>float.NaN</c> when the rider cannot steer (fixed lane): the pickup
    /// then accepts the whole chevron lane band (all three lanes) instead of a single lane. Returns the chevron id collected this tick, or -1.
    /// </summary>
    public int Tick(float riderKm, float riderLateralM, float dt)
    {
        if (_boostLeft > 0f) _boostLeft = Mathf.Max(0f, _boostLeft - dt);
        if (_cooldownLeft > 0f) _cooldownLeft = Mathf.Max(0f, _cooldownLeft - dt);
        if (_cooldownLeft > 0f) return -1;

        for (int i = 0; i < _chevrons.Length; i++)
        {
            if (_taken[i]) continue;
            var c = _chevrons[i];
            if (Mathf.Abs(riderKm - c.km) * 1000f > pickupRadiusAlongM) continue;
            if (!float.IsNaN(riderLateralM) && Mathf.Abs(riderLateralM - c.lateralM) > pickupRadiusLateralM) continue;
            _taken[i] = true; Collected++;
            _boostLeft = boostSeconds;
            _cooldownLeft = cooldownSeconds;
            return c.id;
        }
        return -1;
    }

    public void Reset()
    {
        Array.Clear(_taken, 0, _taken.Length);
        Collected = 0; _boostLeft = _cooldownLeft = 0f;
    }
}

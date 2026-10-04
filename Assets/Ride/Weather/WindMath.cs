using UnityEngine;

/// <summary>
/// Pure, deterministic wind/aero maths shared by gameplay, visuals and tests. No Unity state,
/// so it can be verified headlessly (Assets/Editor/WeatherMathValidation.cs) and gives identical
/// results on every client of a race.
/// </summary>
public static class WindMath
{
    /// <summary>Signed drag force (N) along the direction of travel for a relative air speed.
    /// relAirMps = rider speed + headwind component; negative means the air pushes the rider.</summary>
    public static float AeroDragN(float airDensity, float cdA, float relAirMps) =>
        0.5f * airDensity * cdA * relAirMps * Mathf.Abs(relAirMps);

    /// <summary>Air density from temperature and pressure (ideal gas, dry air).</summary>
    public static float AirDensity(float tempC, float pressureHPa = 1013.25f) =>
        pressureHPa * 100f / (287.05f * (tempC + 273.15f));

    /// <summary>Apparent wind resolved in the rider frame.</summary>
    public struct Apparent
    {
        public float HeadwindMps;    // + into the rider's face, - from behind (true wind only)
        public float CrosswindMps;   // + from the rider's right, - from the left
        public Vector2 RelativeAir;  // rider velocity - wind, rider frame (x right, y forward); the wind is felt coming FROM this direction
        public float SpeedMps => RelativeAir.magnitude;
        /// <summary>Apparent wind angle, degrees: 0 = dead ahead, +90 = from the right.</summary>
        public float AngleDeg => Mathf.Atan2(RelativeAir.x, RelativeAir.y) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// relativeAirVelocity = riderVelocity - windVelocity, in the rider frame.
    /// windWorld is the velocity the AIR moves with (x,z world plane).
    /// </summary>
    public static Apparent Resolve(Vector2 riderForwardXZ, float riderSpeedMps, Vector2 windWorld)
    {
        var f = riderForwardXZ.sqrMagnitude > 1e-6f ? riderForwardXZ.normalized : Vector2.up;
        var r = new Vector2(f.y, -f.x);                       // rider's right in the XZ plane
        float windAlong = Vector2.Dot(windWorld, f);          // + = blowing the way we ride
        float windRight = Vector2.Dot(windWorld, r);          // + = blowing toward our right
        return new Apparent
        {
            HeadwindMps = -windAlong,
            CrosswindMps = -windRight,                          // air moving left came FROM the right
            RelativeAir = new Vector2(-windRight, riderSpeedMps - windAlong),
        };
    }

    /// <summary>
    /// Draft multiplier on CdA from a leader's position, using the APPARENT wind: the wake trails
    /// downwind of the relative air, so in a crosswind the sheltered spot moves to the leeward
    /// side (echelon). gapM along the apparent-wind axis, lateralM across it.
    /// Returns 1 (no shelter) .. ~0.6 (on the wheel).
    /// </summary>
    public static float DraftMultiplier(float gapM, float lateralM, float maxSaving = 0.40f)
    {
        if (gapM <= 0.2f || gapM > 12f) return 1f;
        float along = Mathf.Exp(-(gapM - 0.4f) / 3.2f);
        float across = Mathf.Exp(-(lateralM * lateralM) / (2f * 0.45f * 0.45f));
        return 1f - maxSaving * Mathf.Clamp01(along) * across;
    }

    /// <summary>Smooth, bounded, deterministic gust factor (>= 0). Sum of incommensurate sines
    /// seeded per ride, so every client computes the same gust at the same race time.</summary>
    public static float Gust(double t, int seed, float strength, float frequencyHz)
    {
        if (strength <= 0f) return 0f;
        float s = (seed & 0xFFFF) * 0.0137f;
        double w = 2.0 * Mathf.PI * Mathf.Max(0.005f, frequencyHz);
        float n = (float)(0.55 * System.Math.Sin(w * t + s)
                        + 0.30 * System.Math.Sin(w * 2.31 * t + s * 1.7)
                        + 0.15 * System.Math.Sin(w * 5.07 * t + s * 2.9));
        // Only positive excursions are gusts; lulls come from the base wind itself.
        return strength * Mathf.Clamp01(n * 0.5f + 0.5f) * Mathf.Clamp01(n * 0.5f + 0.5f);
    }
}

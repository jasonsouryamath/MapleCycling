using UnityEngine;

/// <summary>Lighting sample for one point of the ride.</summary>
public struct ShuntaTimeOfDaySample
{
    public float clockHour;        // 0..24
    public float sunElevationDeg;  // negative = below horizon
    public float sunAzimuthDeg;    // 0=N, 90=E, 180=S
    public Color colorGrade;       // tint for ambient + fog
    public float neonIntensity;    // 0 (day) .. 1 (full neon night)
    public float ambient;          // 0..1 overall brightness hint
    public string phase;           // day | golden_dusk | blue_hour | neon_night | pre_dawn | sakura_dawn
}

/// <summary>
/// Golden-hour dusk -> neon night -> pre-dawn sakura arc, as a function of ride km AND the
/// user-chosen start time. The ride is a time-lapse: the whole course spans SpanHours of in-world
/// clock starting at startHour (default 17.5 = dusk, ending ~05:30). Pure maths.
/// </summary>
public static class ShuntaTimeOfDay
{
    public const float DefaultStartHour = 17.5f, DefaultSpanHours = 12f;
    const float Sunrise = 5.5f, Sunset = 17.75f;

    public static float ClockHour(float km, float totalKm, float startHour = DefaultStartHour, float spanHours = DefaultSpanHours)
    {
        float u = totalKm > 1e-4f ? Mathf.Clamp01(km / totalKm) : 0f;
        return Mathf.Repeat(startHour + u * spanHours, 24f);
    }

    public static ShuntaTimeOfDaySample Sample(float km, float totalKm, float startHour = DefaultStartHour, float spanHours = DefaultSpanHours)
        => AtHour(ClockHour(km, totalKm, startHour, spanHours));

    // colour stops keyed by sun elevation (deg), ascending
    static readonly float[] Elev = { -18f, -12f, -6f, -1f, 3f, 10f, 30f };
    static readonly Color[] Evening =
    {
        new Color(0.03f, 0.03f, 0.10f), new Color(0.07f, 0.05f, 0.20f), new Color(0.30f, 0.16f, 0.45f),
        new Color(1.00f, 0.45f, 0.20f), new Color(1.00f, 0.62f, 0.30f), new Color(1.00f, 0.80f, 0.55f), new Color(1f, 0.97f, 0.92f)
    };
    static readonly Color[] Dawn =
    {
        new Color(0.04f, 0.04f, 0.12f), new Color(0.12f, 0.08f, 0.26f), new Color(0.55f, 0.35f, 0.62f),
        new Color(1.00f, 0.62f, 0.75f), new Color(1.00f, 0.74f, 0.80f), new Color(1.00f, 0.88f, 0.86f), new Color(1f, 0.97f, 0.94f)
    };

    public static ShuntaTimeOfDaySample AtHour(float h)
    {
        h = Mathf.Repeat(h, 24f);
        float noon = (Sunrise + Sunset) * 0.5f, half = (Sunset - Sunrise) * 0.5f;
        float cosZero = Mathf.Cos(2f * Mathf.PI * half / 24f);
        const float amp = 60f;
        float elev = amp * (Mathf.Cos(2f * Mathf.PI * (h - noon) / 24f) - cosZero);
        bool rising = h < noon;
        float az = Mathf.Repeat(180f + (h - 12f) * 15f, 360f);

        var grade = Gradient(elev, rising ? Dawn : Evening);
        float neon = Smooth(3f, -9f, elev);
        float ambient = Mathf.Lerp(0.08f, 1f, Smooth(-14f, 12f, elev));

        string phase;
        if (elev > 12f) phase = "day";
        else if (!rising) phase = elev > -1f ? "golden_dusk" : elev > -8f ? "blue_hour" : "neon_night";
        else phase = elev < -14f ? "neon_night" : elev < -10f ? "pre_dawn" : "sakura_dawn";
        return new ShuntaTimeOfDaySample { clockHour = h, sunElevationDeg = elev, sunAzimuthDeg = az, colorGrade = grade, neonIntensity = neon, ambient = ambient, phase = phase };
    }

    static Color Gradient(float e, Color[] c)
    {
        if (e <= Elev[0]) return c[0];
        for (int i = 0; i < Elev.Length - 1; i++)
        {
            if (e > Elev[i + 1]) continue;
            float t = Mathf.InverseLerp(Elev[i], Elev[i + 1], e);
            t = t * t * (3f - 2f * t);
            return Color.Lerp(c[i], c[i + 1], t);
        }
        return c[c.Length - 1];
    }

    /// <summary>Smoothstep from 0 at a to 1 at b (a may be greater than b).</summary>
    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01(Mathf.InverseLerp(a, b, x));
        return t * t * (3f - 2f * t);
    }
}

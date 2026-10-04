using UnityEngine;

/// <summary>Plain weather sample (usable without the Weather system).</summary>
public struct ShuntaWeatherSample
{
    public float rain;      // 0..1 precipitation intensity
    public float wetness;   // 0..1 road wetness (lags rain)
    public float humidity, fog, cloudCoverage;
}

/// <summary>Rain intensity by km: drizzle ramps in, zones 7-8 are heavy, drizzle fades after. Pure maths.</summary>
public static class ShuntaWeatherPlan
{
    public const float BlendKm = 1.2f, DryOutKm = 2.5f;

    /// <summary>Target rain per ORIGINAL zone index (1-12).</summary>
    public static float ZoneRain(int zoneIndex)
    {
        switch (zoneIndex)
        {
            case 5: return 0.05f;
            case 6: return 0.20f;   // drizzle ramps up
            case 7: return 0.90f;   // wet zones, heavy
            case 8: return 0.85f;
            case 9: return 0.30f;   // easing off
            case 10: return 0.10f;
            default: return 0f;
        }
    }

    public static float RainAtKm(ShuntaVariant v, float km)
    {
        if (v == null || v.zones.Length == 0) return 0f;
        if (v.lapKm > 0f && km > v.lapKm) km = Mathf.Repeat(km, v.lapKm);
        km = Mathf.Clamp(km, 0f, v.lapKm);
        int zi = v.zones.Length - 1;
        for (int i = 0; i < v.zones.Length; i++) if (km <= v.zones[i].endKm) { zi = i; break; }
        var z = v.zones[zi];
        float r = ZoneRain(z.index);
        float w = Mathf.Min(BlendKm, (z.endKm - z.startKm) * 0.5f);
        if (zi > 0 && km < z.startKm + w)
        {   // blend with previous zone across the boundary (0.5 at the boundary itself)
            float t = Smooth01(0.5f + 0.5f * (km - z.startKm) / Mathf.Max(w, 1e-4f));
            r = Mathf.Lerp(ZoneRain(v.zones[zi - 1].index), r, t);
        }
        else if (zi < v.zones.Length - 1 && km > z.endKm - w)
        {
            float t = Smooth01(0.5f * (km - (z.endKm - w)) / Mathf.Max(w, 1e-4f));
            r = Mathf.Lerp(r, ZoneRain(v.zones[zi + 1].index), t);
        }
        return Mathf.Clamp01(r);
    }

    public static ShuntaWeatherSample Sample(ShuntaVariant v, float km)
    {
        float rain = RainAtKm(v, km);
        // wetness: max of recent rain decayed with distance (roads stay wet after the rain eases)
        float wet = rain;
        for (float d = 0.25f; d <= DryOutKm * 2f; d += 0.25f)
        {
            float k = km - d; if (k < 0f) break;
            wet = Mathf.Max(wet, RainAtKm(v, k) * Mathf.Exp(-d / DryOutKm));
        }
        wet = Mathf.Clamp01(wet * 1.05f);
        return new ShuntaWeatherSample { rain = rain, wetness = wet, humidity = Mathf.Lerp(0.55f, 0.95f, wet), fog = rain * 0.25f, cloudCoverage = Mathf.Lerp(0.2f, 0.95f, Mathf.Max(rain, wet * 0.6f)) };
    }

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // ---- hook into the existing Weather system (WeatherDirector / WeatherPreset) ----

    /// <summary>WeatherDirector.WeatherState with the plan's values layered on baseState.</summary>
    public static WeatherDirector.WeatherState ToWeatherState(ShuntaWeatherSample s, WeatherDirector.WeatherState baseState)
    {
        baseState.Precipitation = s.rain;
        baseState.Wetness = s.wetness;
        baseState.Humidity = Mathf.Max(baseState.Humidity, s.humidity);
        baseState.Fog = Mathf.Max(baseState.Fog, s.fog);
        baseState.Coverage = Mathf.Max(baseState.Coverage, s.cloudCoverage);
        return baseState;
    }

    /// <summary>Runtime clone of template carrying the plan; feed to WeatherDirector.SetPreset.</summary>
    public static WeatherPreset MakePreset(WeatherPreset template, ShuntaWeatherSample s)
    {
        if (template == null) return null;
        var p = Object.Instantiate(template);
        p.precipitation = s.rain; p.wetness = s.wetness;
        p.humidity = Mathf.Max(p.humidity, s.humidity); p.fog = Mathf.Max(p.fog, s.fog); p.cloudCoverage = Mathf.Max(p.cloudCoverage, s.cloudCoverage);
        return p;
    }

    /// <summary>Convenience: push the sample at km into the director with a smooth blend.</summary>
    public static void Apply(WeatherDirector director, WeatherPreset template, ShuntaVariant v, float km, float blendSeconds = 3f)
    {
        if (director == null || template == null) return;
        director.SetPreset(MakePreset(template, Sample(v, km)), blendSeconds);
    }
}

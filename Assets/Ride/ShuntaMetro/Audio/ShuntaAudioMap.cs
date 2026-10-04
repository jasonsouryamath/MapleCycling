using UnityEngine;

/// <summary>
/// Zone -> audio map for Shunta Metro (12 zones, 28.4 km, see ShuntaCourseData / shunta_metro_course.json).
/// Pure static data + pure functions (no allocation when callers pass buffers), so it can be sampled in tests.
/// Weights crossfade between neighbouring zones around each boundary with an equal-power law.
/// </summary>
public static class ShuntaAudioMap
{
    public const string CourseId = "shunta_metro";
    public const float TotalKm = 28.4f;
    /// <summary>Half-width (km) of the crossfade centred on every zone boundary.</summary>
    public const float FadeHalfKm = 0.18f;

    public static readonly float[] ZoneStartKm = { 0f, 1.6f, 3.6f, 6.2f, 8.6f, 11.6f, 13.8f, 16.2f, 18.4f, 21.0f, 24.0f, 26.6f, 28.4f };
    public const int ZoneCount = 12;

    // Ambience layers.
    public const int Crowd = 0, Murmur = 1, Tunnel = 2, Wind = 3, Cars = 4, Rain = 5, Train = 6, Bridge = 7, Waves = 8, Gulls = 9, AmbCount = 10;
    // Music layers.
    public const int Pad = 0, Bass = 1, Arp = 2, Drums = 3, MusCount = 4;

    //                        crowd murm tunl  wind  cars  rain  train brdg  wave  gull
    private static readonly float[] Amb =
    {
        .90f, .20f, 0f,   .10f, .35f, 0f,   0f,   0f,   0f,   0f,    // 1 shibuya crossing: city crowd hum
        .20f, .85f, 0f,   .08f, .15f, 0f,   0f,   0f,   0f,   0f,    // 2 narrow streets: murmur
        .10f, .20f, 0f,   .40f, .50f, 0f,   0f,   0f,   0f,   0f,    // 3 elevated ramp
        0f,   0f,   1f,   0f,   .30f, 0f,   0f,   0f,   0f,   0f,    // 4 underground tunnel: hum + echo
        0f,   0f,   0f,   .85f, .80f, 0f,   0f,   0f,   0f,   0f,    // 5 skyline expressway: wind + whooshes
        0f,   0f,   0f,   1.0f, 1.0f, 0f,   0f,   0f,   0f,   0f,    // 6 high-speed descent
        0f,   .20f, 0f,   .15f, .50f, .85f, 0f,   0f,   0f,   0f,    // 7 rain streets
        0f,   0f,   0f,   .05f, .30f, .70f, 1f,   0f,   0f,   0f,    // 8 under the railway: rain + train
        0f,   0f,   0f,   .60f, .90f, 0f,   0f,   0f,   0f,   0f,    // 9 interchange
        0f,   0f,   0f,   .50f, .35f, 0f,   0f,   1f,   0f,   0f,    // 10 rainbow bridge: wind + cables
        .05f, 0f,   0f,   .40f, .15f, 0f,   0f,   0f,   .70f, .60f,  // 11 odaiba coastline: waves + gulls
        .20f, 0f,   0f,   .25f, .05f, 0f,   0f,   0f,   1f,   1f,    // 12 finish: harbour
    };

    //                        pad   bass  arp   drums
    private static readonly float[] Mus =
    {
        .55f, .55f, .60f, .60f,   // shibuya: pulse arrives
        .50f, .60f, .45f, .55f,   // narrow
        .60f, .75f, .60f, .75f,   // ramp: building
        .85f, .40f, .30f, .20f,   // tunnel: pads, muted beat
        .55f, .90f, .75f, .95f,   // skyline: full synthwave
        .50f, 1.0f, .90f, 1.0f,   // descent: peak
        .75f, .70f, .50f, .60f,   // rain: moody
        .80f, .65f, .45f, .55f,   // railway
        .55f, .90f, .75f, .90f,   // interchange
        .70f, .80f, .90f, .75f,   // bridge: soaring arp
        .75f, .60f, .60f, .55f,   // coastline
        .95f, .50f, .55f, .35f,   // finish: open pads
    };

    /// <summary>Tunnel low-pass/reverb amount per zone (0 open air .. 1 full tunnel).</summary>
    private static readonly float[] TunnelFx = { 0f, .05f, 0f, 1f, 0f, 0f, 0f, .25f, .12f, 0f, 0f, 0f };

    // Time-of-day arc: golden dusk -> blue hour -> night -> neon night -> deep night -> pre-dawn -> sakura dawn.
    // Packed (km, BPM, brightness 0..1).
    private static readonly float[] Arc =
    {
        0f,    100f, .60f,   // golden dusk
        3.6f,  104f, .50f,   // blue hour
        6.2f,   94f, .28f,   // tunnel: slow and dark
        8.6f,  112f, .80f,   // night opens up
        11.6f, 120f, .92f,   // descent
        14.0f, 106f, .55f,   // neon rain
        18.4f, 110f, .55f,   // deep night
        21.0f, 114f, .85f,   // bridge
        24.0f, 104f, .62f,   // pre-dawn
        28.4f,  92f, .98f,   // sakura dawn
    };

    public static float Curve3(float km, out float bpm, out float bright)
    {
        int n = Arc.Length / 3;
        if (km <= Arc[0]) { bpm = Arc[1]; bright = Arc[2]; return 0f; }
        for (int i = 1; i < n; i++)
        {
            float k1 = Arc[i * 3];
            if (km <= k1)
            {
                float k0 = Arc[(i - 1) * 3];
                float t = k1 - k0 < 1e-4f ? 1f : (km - k0) / (k1 - k0);
                bpm = Mathf.Lerp(Arc[(i - 1) * 3 + 1], Arc[i * 3 + 1], t);
                bright = Mathf.Lerp(Arc[(i - 1) * 3 + 2], Arc[i * 3 + 2], t);
                return t;
            }
        }
        bpm = Arc[(n - 1) * 3 + 1]; bright = Arc[(n - 1) * 3 + 2];
        return 1f;
    }

    /// <summary>Zone index (0..11) containing <paramref name="km"/>.</summary>
    public static int ZoneAt(float km)
    {
        for (int z = 0; z < ZoneCount - 1; z++) if (km < ZoneStartKm[z + 1]) return z;
        return ZoneCount - 1;
    }

    /// <summary>
    /// Finds the zone pair and blend t (0..1, smoothstepped) at <paramref name="km"/>. Inside a zone t = 0 and b = a.
    /// </summary>
    public static void Blend(float km, out int a, out int b, out float t)
    {
        int z = ZoneAt(km);
        a = b = z; t = 0f;
        if (z > 0 && km - ZoneStartKm[z] < FadeHalfKm)
        {
            a = z - 1; b = z;
            t = 0.5f + (km - ZoneStartKm[z]) / (2f * FadeHalfKm);
        }
        else if (z < ZoneCount - 1 && ZoneStartKm[z + 1] - km < FadeHalfKm)
        {
            a = z; b = z + 1;
            t = 0.5f - (ZoneStartKm[z + 1] - km) / (2f * FadeHalfKm);
        }
        t = Mathf.Clamp01(t);
        t = t * t * (3f - 2f * t);
    }

    private static void Fill(float[] table, int stride, float km, float[] outW)
    {
        Blend(km, out int a, out int b, out float t);
        if (a == b) { for (int i = 0; i < stride; i++) outW[i] = table[a * stride + i]; return; }
        for (int i = 0; i < stride; i++)
        {
            float x = table[a * stride + i], y = table[b * stride + i];
            // equal-power: power (squared amplitude) crossfades linearly, so loudness stays steady through the join
            outW[i] = Mathf.Sqrt((1f - t) * x * x + t * y * y);
        }
    }

    public static void AmbienceAt(float km, float[] outW) => Fill(Amb, AmbCount, km, outW);
    public static void MusicAt(float km, float[] outW) => Fill(Mus, MusCount, km, outW);

    public static float TunnelAt(float km)
    {
        Blend(km, out int a, out int b, out float t);
        return Mathf.Lerp(TunnelFx[a], TunnelFx[b], t);
    }

    /// <summary>Time-of-day arc: tempo (BPM) and brightness (0..1) at km, before effort is applied.</summary>
    public static void ArcAt(float km, out float bpm, out float brightness) => Curve3(km, out bpm, out brightness);
}

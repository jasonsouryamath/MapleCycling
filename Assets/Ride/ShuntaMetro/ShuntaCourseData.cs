using System;
using UnityEngine;

/// <summary>
/// Shunta Metro (neon Tokyo, 28.4 km point-to-point): the serializable course definition that
/// <c>Assets/Resources/ShuntaMetro/shunta_metro_course.json</c> loads into, plus the elevation
/// and zone lookups the route builder and gizmo use.
/// </summary>
[Serializable]
public class ShuntaZone
{
    public int index;
    public string id = "";
    public string name = "";
    public string landmark = "";
    public float startKm;
    public float endKm;
    /// <summary>Zone palette colour as #RRGGBB.</summary>
    public string color = "#FFFFFF";
    public string timeOfDay = "night";
    public string surface = "dry_asphalt";
    /// <summary>Corner grip multiplier (1 = dry; the rain-slicked segment is lower).</summary>
    public float grip = 1f;
    /// <summary>Draft strength multiplier (tunnels pack tighter).</summary>
    public float draftMultiplier = 1f;

    public Color Color32 => ColorUtility.TryParseHtmlString(color, out var c) ? c : Color.white;
}

[Serializable]
public class ShuntaClimb { public string name = ""; public float startKm; public float endKm; public int zone; }

[Serializable]
public class ShuntaLandmark { public string name = ""; public float km; }

/// <summary>A placeholder route waypoint in course-local metres (x east, z north), traced from the map art.</summary>
[Serializable]
public class ShuntaWaypoint { public float x; public float z; public int zone; }

[Serializable]
public class ShuntaElevationKey { public float km; public float m; }

[Serializable]
public class ShuntaCourseData
{
    public const string ResourcePath = "ShuntaMetro/shunta_metro_course";

    public string id = "shunta_metro";
    public string displayName = "Shunta Metro";
    public string tagline = "";
    public float distanceKm = 28.4f;
    public float targetGainM = 612f;
    public bool pointToPoint = true;
    public float difficultyStars = 2.5f;
    public string start = "";
    public string finish = "";
    public ShuntaZone[] zones = Array.Empty<ShuntaZone>();
    public ShuntaClimb[] climbs = Array.Empty<ShuntaClimb>();
    public ShuntaLandmark[] landmarks = Array.Empty<ShuntaLandmark>();
    public ShuntaWaypoint[] waypoints = Array.Empty<ShuntaWaypoint>();
    public ShuntaElevationKey[] elevation = Array.Empty<ShuntaElevationKey>();

    public static ShuntaCourseData Load(TextAsset overrideAsset = null)
    {
        var ta = overrideAsset != null ? overrideAsset : Resources.Load<TextAsset>(ResourcePath);
        if (ta == null)
        {
            Debug.LogError($"[shunta] {ResourcePath}.json not found under a Resources folder.");
            return null;
        }
        try { return JsonUtility.FromJson<ShuntaCourseData>(ta.text); }
        catch (Exception e) { Debug.LogError($"[shunta] bad course json: {e.Message}"); return null; }
    }

    /// <summary>Zone containing <paramref name="km"/> (clamped to the first/last zone).</summary>
    public ShuntaZone ZoneAtKm(float km)
    {
        if (zones == null || zones.Length == 0) return null;
        for (int i = 0; i < zones.Length; i++)
            if (km <= zones[i].endKm) return zones[i];
        return zones[zones.Length - 1];
    }

    /// <summary>
    /// Elevation in metres at <paramref name="km"/>: monotone cubic between keypoints, so every
    /// extreme sits exactly on a keypoint and <see cref="ComputeGain"/> equals the keypoint gain.
    /// </summary>
    public float ElevationAtKm(float km)
        => EvaluateElevation(elevation, km);

    public static float EvaluateElevation(ShuntaElevationKey[] e, float km)
    {
        if (e == null || e.Length == 0) return 0f;
        if (km <= e[0].km) return e[0].m;
        for (int i = 0; i < e.Length - 1; i++)
        {
            if (km > e[i + 1].km) continue;
            float h = e[i + 1].km - e[i].km;
            if (h <= 0f) return e[i + 1].m;
            float t = Mathf.Clamp01((km - e[i].km) / h);
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * e[i].m
                + (t3 - 2f * t2 + t) * h * ElevationTangent(e, i)
                + (-2f * t3 + 3f * t2) * e[i + 1].m
                + (t3 - t2) * h * ElevationTangent(e, i + 1);
        }
        return e[e.Length - 1].m;
    }

    // PCHIP's weighted harmonic mean keeps slopes continuous without overshooting extrema.
    static float ElevationTangent(ShuntaElevationKey[] e, int i)
    {
        if (e.Length == 2) return Secant(e, 0);
        if (i == 0 || i == e.Length - 1)
        {
            bool first = i == 0;
            int a = first ? 0 : e.Length - 2, b = first ? 1 : e.Length - 3;
            float h0 = e[a + 1].km - e[a].km, h1 = e[b + 1].km - e[b].km;
            float d0 = Secant(e, a), d1 = Secant(e, b);
            if (h0 <= 0f || h1 <= 0f) return 0f;
            float m = ((2f * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
            if (m * d0 <= 0f) return 0f;
            return d0 * d1 <= 0f && Mathf.Abs(m) > 3f * Mathf.Abs(d0) ? 3f * d0 : m;
        }
        float left = Secant(e, i - 1), right = Secant(e, i);
        if (left * right <= 0f) return 0f;
        float hl = e[i].km - e[i - 1].km, hr = e[i + 1].km - e[i].km;
        float w1 = 2f * hr + hl, w2 = hr + 2f * hl;
        return (w1 + w2) / (w1 / left + w2 / right);
    }

    static float Secant(ShuntaElevationKey[] e, int i)
    {
        float h = e[i + 1].km - e[i].km;
        return h > 0f ? (e[i + 1].m - e[i].m) / h : 0f;
    }

    /// <summary>Total ascent in metres over the keypoints.</summary>
    public float ComputeGain()
    {
        float g = 0f;
        for (int i = 0; i < elevation.Length - 1; i++)
            g += Mathf.Max(0f, elevation[i + 1].m - elevation[i].m);
        return g;
    }

    /// <summary>Steepest grade (percent) the rider feels: the profile sampled over 100 m windows.</summary>
    public float MaxGradePercent()
    {
        if (elevation.Length < 2) return 0f;
        float worst = 0f, end = elevation[elevation.Length - 1].km;
        for (float km = 0f; km + 0.1f <= end; km += 0.025f)
            worst = Mathf.Max(worst, Mathf.Abs(ElevationAtKm(km + 0.1f) - ElevationAtKm(km)) / 100f * 100f);
        return worst;
    }
}

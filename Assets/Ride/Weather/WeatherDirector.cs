using UnityEngine;

/// <summary>
/// Authoritative weather state (macro scale) + the gameplay wind the rider feels. FOUNDATION of
/// the weather system (2026-09-25): sky, rain, water, audio and visuals should READ
/// <see cref="State"/> / <see cref="WindAt"/> rather than keep their own weather.
///
/// Scales: macro = preset blend over minutes; local = <see cref="WeatherWindVolume"/> shelter /
/// exposure multipliers blended in space; micro = deterministic, clamped gusts (WindMath.Gust).
/// Physics: writes CyclingPhysics.headwindMps / dragMultiplier. Trainer power is untouched.
/// Replay / multiplayer: everything derives from (preset, seed, ride time) - sync those three.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeatherDirector : MonoBehaviour
{
    public WeatherPreset preset;
    public int seed = 1234;
    [Tooltip("Freeze macro weather (debug / events).")] public bool freeze;
    [Tooltip("Accessibility: keep speed effects, but no crosswind lean/steer.")] public bool disableSteeringInfluence;
    [Tooltip("Hard safety clamp on the along-track wind the physics sees, m/s.")] public float maxAlongTrackMps = 12f;

    public RideSession session;
    /// <summary>Debug/test only (WeatherDebugOverlay): replaces the surface wind at the rider.</summary>
    [System.NonSerialized] public Vector2? debugWindOverride;
    public Transform rider;

    public struct WeatherState
    {
        public Vector2 WindToXZ;      // sustained wind velocity (air moves toward), m/s
        public float GustMps, GustHz, Turbulence;
        public float Coverage, Density, AltitudeM, Precipitation, Humidity, Fog, TempC, Wetness;
        public float SnowCover;       // lying snow 0..1 (WeatherSnowCover reads it)
        /// <summary>0 = all rain, 1 = all snow. Wet snow / sleet between +1.5 C and -0.5 C.</summary>
        public float SnowFraction => Mathf.Clamp01(Mathf.InverseLerp(1.5f, -0.5f, TempC));
        public float AirDensity;
        public float FrontDeg, FrontMps;
    }

    public WeatherState State { get; private set; }
    public WindMath.Apparent Apparent { get; private set; }
    public float Exposure { get; private set; } = 1f;
    public float TransitionProgress { get; private set; } = 1f;

    private WeatherState _from, _to;
    private float _blendT = 1f, _blendDur = 1f;

    public static WeatherDirector Instance { get; private set; }
    private static readonly int WindId = Shader.PropertyToID("_MapleWind");
    private static readonly int WeatherId = Shader.PropertyToID("_MapleWeather");
    private static readonly int SnowId = Shader.PropertyToID("_MapleSnow");

    private void OnEnable() => Instance = this;
    private void OnDisable() { if (Instance == this) Instance = null; }

    private void Start()
    {
        if (session == null) session = GetComponent<RideSession>();
        if (GetComponent<WeatherSky>() == null) gameObject.AddComponent<WeatherSky>();
        if (GetComponent<WeatherEffects>() == null) gameObject.AddComponent<WeatherEffects>();
        // Test/capture hook: MAPLERIDE_WEATHER_PRESET=SunShower forces a preset at start.
        var forced = System.Environment.GetEnvironmentVariable("MAPLERIDE_WEATHER_PRESET");
        if (!string.IsNullOrEmpty(forced)) preset = Resources.Load<WeatherPreset>("Weather/" + forced) ?? preset;
        if (GetComponent<WeatherDebugOverlay>() == null) gameObject.AddComponent<WeatherDebugOverlay>();
        // Default: a gentle coastal breeze (Assets/Resources/Weather, made by WeatherPresetAuthoring).
        if (preset == null) preset = Resources.Load<WeatherPreset>("Weather/ClearCoastalBreeze");
        _to = _from = FromPreset(preset);
        State = _to;
    }

    /// <summary>Blend to a new preset over its transition time (or an override).</summary>
    public void SetPreset(WeatherPreset p, float seconds = -1f)
    {
        preset = p;
        _from = State;
        _to = FromPreset(p);
        _blendDur = Mathf.Max(0.01f, seconds >= 0f ? seconds : (p != null ? p.transitionSeconds : 1f));
        _blendT = 0f;
    }

    public static WeatherState FromPreset(WeatherPreset p)
    {
        if (p == null) return new WeatherState { AirDensity = 1.225f, TempC = 18f, AltitudeM = 2200f };
        float a = p.windToDeg * Mathf.Deg2Rad;
        return new WeatherState
        {
            WindToXZ = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * p.sustainedMps,
            GustMps = p.gustMps, GustHz = p.gustFrequencyHz, Turbulence = p.turbulence,
            Coverage = p.cloudCoverage, Density = p.cloudDensity, AltitudeM = p.cloudAltitudeM,
            Precipitation = p.precipitation, Humidity = p.humidity, Fog = p.fog, TempC = p.airTempC,
            Wetness = p.wetness, SnowCover = p.snowCover, AirDensity = WindMath.AirDensity(p.airTempC),
            FrontDeg = p.frontTravelDeg, FrontMps = p.frontSpeedMps,
        };
    }

    public static WeatherState Lerp(WeatherState a, WeatherState b, float t)
    {
        t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        return new WeatherState
        {
            WindToXZ = Vector2.Lerp(a.WindToXZ, b.WindToXZ, t),
            GustMps = Mathf.Lerp(a.GustMps, b.GustMps, t), GustHz = Mathf.Lerp(a.GustHz, b.GustHz, t),
            Turbulence = Mathf.Lerp(a.Turbulence, b.Turbulence, t),
            Coverage = Mathf.Lerp(a.Coverage, b.Coverage, t), Density = Mathf.Lerp(a.Density, b.Density, t),
            AltitudeM = Mathf.Lerp(a.AltitudeM, b.AltitudeM, t),
            Precipitation = Mathf.Lerp(a.Precipitation, b.Precipitation, t),
            Humidity = Mathf.Lerp(a.Humidity, b.Humidity, t), Fog = Mathf.Lerp(a.Fog, b.Fog, t),
            TempC = Mathf.Lerp(a.TempC, b.TempC, t), Wetness = Mathf.Lerp(a.Wetness, b.Wetness, t),
            SnowCover = Mathf.Lerp(a.SnowCover, b.SnowCover, t),
            AirDensity = Mathf.Lerp(a.AirDensity, b.AirDensity, t),
            FrontDeg = Mathf.LerpAngle(a.FrontDeg, b.FrontDeg, t), FrontMps = Mathf.Lerp(a.FrontMps, b.FrontMps, t),
        };
    }

    /// <summary>Surface wind velocity at a world position and time: macro x local exposure + gust.</summary>
    public Vector2 WindAt(Vector3 pos, double t, out float exposure)
    {
        // Authored volumes win (Minato); elsewhere the route's own shape decides (Sakura etc.).
        exposure = WeatherWindVolume.TryExposureAt(pos, out float ve) ? ve : TerrainExposure();
        var s = State;
        float gust = WindMath.Gust(t, seed, s.GustMps, s.GustHz) * exposure;
        var dir = s.WindToXZ.sqrMagnitude > 1e-6f ? s.WindToXZ.normalized : Vector2.zero;
        return s.WindToXZ * exposure + dir * gust;
    }

    /// <summary>
    /// Route-shape exposure for regions without authored volumes: a road ABOVE its surroundings
    /// (ridge, exposed bend) catches more wind, a road BELOW them (valley) is sheltered.
    /// "Surroundings" = mean route elevation over +/-300 m. Clamped 0.6..1.35. Reusable by every
    /// region; no scene data needed.
    /// </summary>
    public float TerrainExposure()
    {
        var c = session != null ? session.Course : null;
        if (c == null || c.Length < 100f) return 1f;
        float d = session.DistanceM, y = c.ElevationAt(d), mean = 0f;
        for (int k = -3; k <= 3; k++) mean += c.ElevationAt(Mathf.Clamp(d + k * 100f, 0f, c.Length));
        mean /= 7f;
        return Mathf.Clamp(1f + (y - mean) / 60f, 0.6f, 1.35f);
    }

    private void FixedUpdate()
    {
        if (!freeze && _blendT < 1f)
        {
            _blendT = Mathf.Min(1f, _blendT + Time.fixedDeltaTime / _blendDur);
            State = Lerp(_from, _to, _blendT);
        }
        TransitionProgress = _blendT;

        // ONE wind for every visual: shaders (flags, grass, water chop, rain slant) read these
        // globals instead of animating on their own, so visuals always agree with the physics.
        // _MapleWind = (x, z, speed m/s, gust m/s); _MapleWeather = (coverage, precip, wetness, fog).
        var st = State;
        Shader.SetGlobalVector(WindId, new Vector4(st.WindToXZ.x, st.WindToXZ.y, st.WindToXZ.magnitude,
            WindMath.Gust(session != null ? session.ElapsedSeconds : Time.time, seed, st.GustMps, st.GustHz)));
        Shader.SetGlobalVector(WeatherId, new Vector4(st.Coverage, st.Precipitation, st.Wetness, st.Fog));
        // _MapleSnow = (lying snow, snowfall rate 0-1, air temp C, 0). The shaders' own cover global
        // (_MR_SnowCover) is owned by WeatherSnowCover, which folds this in.
        Shader.SetGlobalVector(SnowId, new Vector4(st.SnowCover, st.Precipitation * st.SnowFraction, st.TempC, 0f));

        if (session == null || session.physics == null) return;
        var tr = rider != null ? rider : (Camera.main != null ? Camera.main.transform : null);
        if (tr == null) return;
        double t = session.ElapsedSeconds;   // ride time, not wall time: identical on every client
        var wind = WindAt(tr.position, t, out float exp);
        if (debugWindOverride.HasValue) wind = debugWindOverride.Value;
        Exposure = exp;
        var fwd = new Vector2(tr.forward.x, tr.forward.z);
        Apparent = WindMath.Resolve(fwd, session.SpeedMps, wind);
        session.physics.headwindMps = Mathf.Clamp(Apparent.HeadwindMps, -maxAlongTrackMps, maxAlongTrackMps);
        session.physics.airDensityKgM3 = State.AirDensity > 0.5f ? State.AirDensity : 1.225f;
    }
}

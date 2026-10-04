using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Visual weather that follows <see cref="WeatherDirector"/> (added by it):
///  * RAIN - one camera-following particle system; emission = precipitation, drops fall at
///    ~9 m/s and drift with the SAME surface wind as the physics (world-space velocity), so the
///    slant the rider sees is the apparent wind.
///  * SNOW - a second camera-following system. Precipitation falls as snow when the air is cold
///    (WeatherState.SnowFraction: all snow at -0.5 C and below, sleet mix to +1.5 C). Flakes fall at
///    ~1.2 m/s, flutter on curl noise and drift with the same surface wind (they drift ~8x further
///    than rain per metre fallen, which is what makes snowfall read as snow). Shader:
///    MapleRide/HDRP/Snowflake (sun/sky-lit, near flakes defocused).
///  * WATER - MinatoOcean chop/speed/swell scale with wind (MaterialPropertyBlock, so the
///    ocean material asset is never modified in play mode).
///  * QUALITY - tiers from the active HDRP quality level control rain density and update rates.
/// Gameplay wind never depends on any of this (physics runs in WeatherDirector.FixedUpdate).
/// </summary>
[DisallowMultipleComponent]
public sealed class WeatherEffects : MonoBehaviour
{
    public enum Tier { Low, Medium, High }

    [Header("Accessibility")]
    [Tooltip("Scales rain density (0.3 = reduced precipitation).")]
    [Range(0f, 1f)] public float precipitationScale = 1f;

    public Tier tier = Tier.High;
    private int _maxDrops = 8000;
    private float _oceanInterval = 0.2f;

    private ParticleSystem _rain;
    private ParticleSystem _snow;
    private ParticleSystem.EmissionModule _snowEmission;
    private int _maxFlakes = 12000;
    public const float FlakeLifetime = 11f;
    private ParticleSystem.EmissionModule _emission;
    private ParticleSystem.VelocityOverLifetimeModule _vel;
    private Renderer[] _ocean = new Renderer[0];
    private MaterialPropertyBlock _mpb;
    private float _nextOcean, _nextScan;
    private static readonly int ChopId = Shader.PropertyToID("_ChopStrength");
    private static readonly int SpeedId = Shader.PropertyToID("_WaveSpeed");
    private static readonly int SwellId = Shader.PropertyToID("_WaveStrength");

    private void Start()
    {
        ApplyTier(TierFromQuality());
        _mpb = new MaterialPropertyBlock();
        BuildRain();
        _snow = CreateSnowfall(transform, _maxFlakes);
        _snowEmission = _snow.emission;
        _snow.Play();
    }

    /// <summary>SC_HDRP_Low/Medium/High/Ultra -> tier (by name, so reordering levels is safe).</summary>
    public static Tier TierFromQuality()
    {
        string n = QualitySettings.names[QualitySettings.GetQualityLevel()].ToLowerInvariant();
        return n.Contains("low") ? Tier.Low : n.Contains("medium") ? Tier.Medium : Tier.High;
    }

    public void ApplyTier(Tier t)
    {
        tier = t;
        _maxDrops = t == Tier.Low ? 1500 : t == Tier.Medium ? 4000 : 8000;
        _oceanInterval = t == Tier.Low ? 1f : t == Tier.Medium ? 0.5f : 0.2f;
        var sky = GetComponent<WeatherSky>();
        if (sky != null) sky.updateInterval = t == Tier.Low ? 1f : t == Tier.Medium ? 0.5f : 0.25f;
        _maxFlakes = t == Tier.Low ? 2500 : t == Tier.Medium ? 6000 : 12000;
        if (_rain != null) { var m = _rain.main; m.maxParticles = _maxDrops; }
        if (_snow != null) { var m = _snow.main; m.maxParticles = _maxFlakes; }
    }

    private void BuildRain()
    {
        var go = new GameObject("~Weather Rain");
        go.transform.SetParent(transform, false);
        _rain = go.AddComponent<ParticleSystem>();
        _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _rain.main;
        main.loop = true;
        main.startLifetime = 1.6f;
        main.startSpeed = 0f;
        main.startSize = 0.025f;
        main.startColor = new Color(0.82f, 0.86f, 0.92f, 0.45f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = _maxDrops;
        main.playOnAwake = false;
        var shape = _rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(44f, 0.5f, 44f);
        _emission = _rain.emission;
        _emission.rateOverTime = 0f;
        _vel = _rain.velocityOverLifetime;
        _vel.enabled = true;
        _vel.space = ParticleSystemSimulationSpace.World;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.045f;
        r.lengthScale = 1f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sharedMaterial = RainMaterial();
        _rain.Play();
    }

    /// <summary>
    /// The snowfall particle system (public so edit-mode captures can stage and pre-warm the same
    /// snow the game shows). Emission starts at 0; the caller drives rateOverTime.
    /// </summary>
    public static ParticleSystem CreateSnowfall(Transform parent, int maxFlakes)
    {
        var go = new GameObject("~Weather Snow");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 10f;
        main.startLifetime = FlakeLifetime;
        main.startSpeed = 0f;
        // Real flakes are 2-10 mm aggregates; the larger end reads at gameplay distance. A few big
        // wet clumps (the long tail of the size range) are what sells a heavy fall.
        main.startSize = new ParticleSystem.MinMaxCurve(0.010f, 0.034f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.90f, 0.93f, 1f, 0.75f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxFlakes;
        main.playOnAwake = false;
        main.gravityModifier = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(56f, 1f, 56f);
        var em = ps.emission;
        em.rateOverTime = 0f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(-1.5f, -0.9f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.55f);
        noise.frequency = 0.35f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.25f);
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f),
                             new GradientAlphaKey(1f, 0.88f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.minParticleSize = 0.0008f;
        r.maxParticleSize = 0.05f;
        r.sharedMaterial = SnowMaterial();
        return ps;
    }

    /// <summary>Emitter placement shared by play mode and captures: above and ahead of the camera,
    /// shifted upwind so flakes drifting across the view were born in view.</summary>
    public static void PlaceSnowfall(ParticleSystem ps, Transform cam, Vector2 windXZ)
    {
        if (ps == null || cam == null) return;
        var fwd = new Vector3(cam.forward.x, 0f, cam.forward.z);
        var upwind = Vector3.ClampMagnitude(new Vector3(-windXZ.x, 0f, -windXZ.y) * 5f, 30f);
        ps.transform.position = cam.position + Vector3.up * 12f + fwd * 14f + upwind;
        var vel = ps.velocityOverLifetime;
        vel.x = new ParticleSystem.MinMaxCurve(windXZ.x * 0.75f, windXZ.x * 1.05f);
        vel.z = new ParticleSystem.MinMaxCurve(windXZ.y * 0.75f, windXZ.y * 1.05f);
    }

    public static float SnowRate(float snowfall01, int maxFlakes) => snowfall01 * maxFlakes / FlakeLifetime;

    private static Material _snowMat;
    public static Material SnowMaterial()
    {
        if (_snowMat != null) return _snowMat;
        _snowMat = Resources.Load<Material>("Weather/Snowflake");
        if (_snowMat == null)
        {
            var sh = Shader.Find("MapleRide/HDRP/Snowflake");
            if (sh != null) _snowMat = new Material(sh) { name = "~WeatherSnow" };
        }
        return _snowMat;
    }

    private static Material RainMaterial()
    {
        var sh = Shader.Find("HDRP/Unlit");
        if (sh == null) return null;
        var m = new Material(sh) { name = "~WeatherRain" };
        m.SetFloat("_SurfaceType", 1f);
        m.SetFloat("_BlendMode", 0f);
        m.SetColor("_UnlitColor", new Color(0.80f, 0.85f, 0.92f, 0.38f));
        HDMaterial.ValidateMaterial(m);
        return m;
    }

    private void LateUpdate()
    {
        var wd = WeatherDirector.Instance;
        if (wd == null) return;
        var s = wd.State;
        var cam = Camera.main;

        // ---- rain
        if (_rain != null && cam != null)
        {
            _rain.transform.position = cam.transform.position + Vector3.up * 14f
                                       + new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z) * 10f;
            var w = s.WindToXZ * wd.Exposure;
            _vel.x = new ParticleSystem.MinMaxCurve(w.x);
            _vel.y = new ParticleSystem.MinMaxCurve(-9f);
            _vel.z = new ParticleSystem.MinMaxCurve(w.y);
            float rate = s.Precipitation * (1f - s.SnowFraction) * precipitationScale * _maxDrops / 1.6f;
            _emission.rateOverTime = rate;
        }

        // ---- snow
        if (_snow != null && cam != null)
        {
            PlaceSnowfall(_snow, cam.transform, s.WindToXZ * wd.Exposure);
            _snowEmission.rateOverTime = SnowRate(s.Precipitation * s.SnowFraction * precipitationScale, _maxFlakes);
        }

        // ---- ocean chop (rescan occasionally: regions stream in and out)
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 5f;
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var rr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (rr.sharedMaterial != null && rr.sharedMaterial.shader != null &&
                    rr.sharedMaterial.shader.name == "MapleRide/MinatoOcean") list.Add(rr);
            _ocean = list.ToArray();
        }
        if (_ocean.Length > 0 && Time.unscaledTime >= _nextOcean)
        {
            _nextOcean = Time.unscaledTime + _oceanInterval;
            float wMps = s.WindToXZ.magnitude + s.GustMps * 0.5f;
            float t = Mathf.Clamp01(wMps / 12f);
            foreach (var rr in _ocean)
            {
                if (rr == null) continue;
                rr.GetPropertyBlock(_mpb);
                _mpb.SetFloat(ChopId, Mathf.Lerp(0.08f, 0.55f, t));
                _mpb.SetFloat(SpeedId, Mathf.Lerp(0.18f, 0.75f, t));
                _mpb.SetFloat(SwellId, Mathf.Lerp(0.22f, 0.55f, t));
                rr.SetPropertyBlock(_mpb);
            }
        }
    }
}

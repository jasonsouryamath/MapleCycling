using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wet roads from <see cref="WeatherDirector.State"/>: drives <c>_WetAmount</c> on every renderer
/// whose material uses <c>MapleRide/HDRP/Road</c>, through a MaterialPropertyBlock (the material
/// assets are only READ, never written, so play mode leaves no trace in the project).
///
/// The road keeps its own surface wetness that follows the weather asymmetrically: it soaks up
/// quickly while it rains and dries out slowly afterwards (faster when warm, windy and dry air).
/// Applied value = lerp(material's authored _WetAmount, maxWetAmount, wetness), so a dry road looks
/// exactly as authored (Minato keeps its 0.55 damp-harbour patches).
///
/// Self-spawns (hidden, DontDestroyOnLoad) and idles while no WeatherDirector exists.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeatherWetness : MonoBehaviour
{
    public const string RoadShaderName = "MapleRide/HDRP/Road";

    [Tooltip("_WetAmount at full surface wetness.")]
    [Range(0f, 1f)] public float maxWetAmount = 1f;
    [Tooltip("Seconds for a soaked road to dry in mild (18 C, 3 m/s, 50 % humidity) conditions.")]
    public float dryingSeconds = 900f;
    [Tooltip("Seconds for light rain to wet a dry road (heavier rain is faster).")]
    public float wettingSeconds = 60f;
    [Tooltip("Seconds between material updates; set from the quality tier.")]
    public float updateInterval = 0.25f;

    /// <summary>Current surface wetness 0..1 (what the road shows, not the preset value).</summary>
    public float Wetness { get; private set; }
    public int RoadRendererCount => _slots.Count;

    public static WeatherWetness Instance { get; private set; }
    private static readonly int WetId = Shader.PropertyToID("_WetAmount");

    private struct Slot { public Renderer R; public int Index; public float Base; }
    private readonly List<Slot> _slots = new();
    private MaterialPropertyBlock _mpb;
    private float _nextScan, _nextApply, _lastApplied = -1f;
    private bool _initialised, _dirty;
    private int _loggedCount = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Instance != null) return;
        var go = new GameObject("~Weather Wetness") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<WeatherWetness>();
    }

    private void OnEnable() { if (Instance == null) Instance = this; }

    private void OnDisable()
    {
        RestoreAuthored();
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        var t = WeatherEffects.TierFromQuality();
        updateInterval = t == WeatherEffects.Tier.Low ? 1f : t == WeatherEffects.Tier.Medium ? 0.5f : 0.25f;
    }

    /// <summary>Wetness the weather is pushing the road toward: the preset's wetness, and any
    /// real rain soaks it further.</summary>
    public static float TargetWetness(in WeatherDirector.WeatherState s) =>
        Mathf.Clamp01(Mathf.Max(s.Wetness, s.Precipitation * 1.5f));

    /// <summary>Pure step (tests use it): quick to wet while raining, slow to dry after.</summary>
    public static float Step(float current, in WeatherDirector.WeatherState s, float dt,
                             float wettingSeconds = 60f, float dryingSeconds = 900f)
    {
        float target = TargetWetness(s);
        if (target >= current)
        {
            // Light drizzle wets in ~wettingSeconds, a downpour in a fraction of that.
            float rate = (1f + 4f * Mathf.Clamp01(s.Precipitation)) / Mathf.Max(1f, wettingSeconds);
            return Mathf.Min(target, current + rate * dt);
        }
        if (s.Precipitation > 0.05f) return current;   // still raining: puddles don't shrink
        float wind = s.WindToXZ.magnitude;
        float warm = Mathf.Clamp(0.5f + s.TempC / 36f, 0.3f, 1.8f);          // 18 C -> 1
        float air = Mathf.Clamp(1.5f - s.Humidity, 0.2f, 1.5f);             // 50 % -> 1
        float breeze = Mathf.Clamp(0.6f + wind / 7.5f, 0.6f, 2.5f);          // 3 m/s -> 1
        float dry = warm * air * breeze / Mathf.Max(1f, dryingSeconds);
        return Mathf.Max(target, current - dry * dt);
    }

    private void Update()
    {
        var wd = WeatherDirector.Instance;
        if (wd == null)
        {
            if (_initialised) { RestoreAuthored(); _initialised = false; }
            return;
        }

        var s = wd.State;
        if (!_initialised)
        {
            // The starting preset describes weather that is already established.
            Wetness = TargetWetness(s);
            _initialised = true;
            _nextScan = 0f;
        }
        else if (!wd.freeze) Wetness = Step(Wetness, s, Time.deltaTime, wettingSeconds, dryingSeconds);

        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 5f; Rescan(); }
        if (Time.unscaledTime < _nextApply) return;
        _nextApply = Time.unscaledTime + updateInterval;
        if (!_dirty && Mathf.Abs(Wetness - _lastApplied) < 0.002f) return;
        Apply(Wetness);
    }

    /// <summary>Find road renderers (regions stream in and out, so this repeats).</summary>
    public void Rescan()
    {
        _slots.Clear();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name != RoadShaderName) continue;
                _slots.Add(new Slot { R = r, Index = i, Base = m.HasProperty(WetId) ? m.GetFloat(WetId) : 0f });
            }
        }
        _dirty = true;
        if (_slots.Count != _loggedCount)
        {
            _loggedCount = _slots.Count;
            Debug.Log($"[weather-wet] {_slots.Count} road material slots, surface wetness {Wetness:0.00}");
        }
    }

    /// <summary>Write wetness (0..1) into every road renderer's property block.</summary>
    public void Apply(float wetness)
    {
        _mpb ??= new MaterialPropertyBlock();
        foreach (var sl in _slots)
        {
            if (sl.R == null) continue;
            sl.R.GetPropertyBlock(_mpb, sl.Index);
            _mpb.SetFloat(WetId, Mathf.Lerp(sl.Base, Mathf.Max(sl.Base, maxWetAmount), Mathf.Clamp01(wetness)));
            sl.R.SetPropertyBlock(_mpb, sl.Index);
        }
        _lastApplied = wetness;
        _dirty = false;
    }

    /// <summary>Put every road back to its authored value (weather gone / component disabled).</summary>
    public void RestoreAuthored()
    {
        _mpb ??= new MaterialPropertyBlock();
        foreach (var sl in _slots)
        {
            if (sl.R == null) continue;
            sl.R.GetPropertyBlock(_mpb, sl.Index);
            _mpb.SetFloat(WetId, sl.Base);
            sl.R.SetPropertyBlock(_mpb, sl.Index);
        }
        _lastApplied = -1f;
    }
}

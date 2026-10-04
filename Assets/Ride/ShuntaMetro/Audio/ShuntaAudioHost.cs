using System;
using UnityEngine;

/// <summary>
/// Starts the Shunta Metro audio when (and only when) the ride is on the "shunta_metro" course. Self-bootstrapping
/// (no scene reference); everything is created/destroyed at runtime. Set MR_SHUNTA_AUDIO=0 to disable,
/// MR_SHUNTA_AUDIO_LOG=1 for periodic state logs. Other courses are untouched.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShuntaAudioHost : MonoBehaviour
{
    private RideSession _session;
    private RideAudio _rideAudio;
    private ShuntaDirector _dir;
    private float _poll, _logAt;

    public static ShuntaAudioHost Instance { get; private set; }
    /// <summary>Diagnostics (smoke test): null while not on the Shunta course.</summary>
    public ShuntaDirector Director => _dir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        if (Environment.GetEnvironmentVariable("MR_SHUNTA_AUDIO") == "0") return;
        var go = new GameObject("~ShuntaAudioHost");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaAudioHost>();
    }

    private void Update()
    {
        _poll -= Time.unscaledDeltaTime;
        if (_poll > 0f) return;
        _poll = 0.5f;

        if (_session == null) _session = FindAnyObjectByType<RideSession>();
        if (_rideAudio == null) _rideAudio = FindAnyObjectByType<RideAudio>();

        var c = _session != null ? _session.Course : null;
        bool want = c != null && c.Count >= 2 && c.Id == ShuntaAudioMap.CourseId;
        if (want && _dir == null)
        {
            var go = new GameObject("ShuntaAudio"); go.transform.SetParent(transform, false);
            _dir = go.AddComponent<ShuntaDirector>();
            _dir.Init(_session, _rideAudio);
            Debug.Log($"[shunta-audio] started ({c.Length:F0} m)");
        }
        else if (!want && _dir != null)
        {
            Destroy(_dir.gameObject); _dir = null;
            Debug.Log("[shunta-audio] stopped (left the Shunta course)");
        }

        if (_dir != null && Time.unscaledTime > _logAt && Environment.GetEnvironmentVariable("MR_SHUNTA_AUDIO_LOG") == "1")
        {
            _logAt = Time.unscaledTime + 20f;
            Debug.Log("[shunta-audio] " + _dir.Describe());
        }
    }
}

/// <summary>
/// Main-thread side of the Shunta audio: maps route distance to km, samples ShuntaAudioMap (zone weights, tunnel
/// effect, time-of-day tempo/brightness) and feeds the two synths. Cheap: a handful of float ops per frame, no
/// allocation after Init.
/// </summary>
public sealed class ShuntaDirector : MonoBehaviour
{
    private RideSession _session;
    private RideAudio _rideAudio;
    private ShuntaAmbienceSynth _amb;
    private float _crowdDensity, _trafficDensity;
    private ShuntaMusicSynth _mus;
    private readonly float[] _aw = new float[ShuntaAudioMap.AmbCount], _mw = new float[ShuntaAudioMap.MusCount];
    private float _enter, _effort, _km, _bpm, _bright, _tunnel;

    public ShuntaAmbienceSynth Ambience => _amb;
    public ShuntaMusicSynth Music => _mus;

    public void Init(RideSession session, RideAudio rideAudio)
    {
        _session = session; _rideAudio = rideAudio;
        var a = new GameObject("Ambience"); a.transform.SetParent(transform, false);
        a.AddComponent<AudioSource>();
        _amb = a.AddComponent<ShuntaAmbienceSynth>();
        var m = new GameObject("Music"); m.transform.SetParent(transform, false);
        m.AddComponent<AudioSource>();
        _mus = m.AddComponent<ShuntaMusicSynth>();
        Sample(true);
    }

    private float Km()
    {
        var c = _session.Course;
        float len = c != null ? Mathf.Max(1f, c.Length) : ShuntaAudioMap.TotalKm * 1000f;
        return Mathf.Clamp(_session.DistanceM, 0f, len) / len * ShuntaAudioMap.TotalKm;   // scales if the baked length differs
    }

    private void Sample(bool snap)
    {
        _km = Km();
        ShuntaAudioMap.AmbienceAt(_km, _aw);
        ShuntaAudioMap.MusicAt(_km, _mw);
        _tunnel = ShuntaAudioMap.TunnelAt(_km);
        ShuntaAudioMap.ArcAt(_km, out _bpm, out _bright);
        if (snap) { _amb.SnapTo(_aw, _tunnel); _mus.SnapTo(_mw); }
    }

    private void Update()
    {
        if (_session == null || _amb == null) return;
        float dt = Time.unscaledDeltaTime;
        _enter = Mathf.MoveTowards(_enter, 1f, dt / 3f);
        Sample(false);

        float speed = Mathf.Max(0f, _session.SpeedMps);
        _effort = Mathf.Lerp(_effort, Mathf.Clamp01((speed - 4f) / 10f), 1f - Mathf.Exp(-dt / 1.5f));   // effort ~ speed above a cruise

        float gate = _rideAudio != null ? _rideAudio.MusicGate : 1f;
        float level = _rideAudio != null ? Mathf.Clamp01(_rideAudio.musicVolume * 1.4f) : 0.45f;

        for (int i = 0; i < ShuntaAudioMap.AmbCount; i++) _amb.Target[i] = _aw[i];
        // Live density modulation (2026-10-04): the zone map sets the baseline, the actual crowd / traffic / sky trains near the rider shape it.
        var crowd = ShuntaStreetCrowd.Instance; var life = ShuntaLifeDirector.Instance; var sky = ShuntaSkyTrains.Instance;
        if (crowd != null)
        {
            _crowdDensity = Mathf.Lerp(_crowdDensity, Mathf.Clamp01(crowd.VisibleCount / 60f), 1f - Mathf.Exp(-dt / 1.2f));
            float k = Mathf.Lerp(0.45f, 1.35f, _crowdDensity);
            _amb.Target[ShuntaAudioMap.Crowd] = Mathf.Clamp01(_amb.Target[ShuntaAudioMap.Crowd] * k + 0.18f * _crowdDensity);
            _amb.Target[ShuntaAudioMap.Murmur] = Mathf.Clamp01(_amb.Target[ShuntaAudioMap.Murmur] * k + 0.10f * _crowdDensity);
        }
        if (life != null)
        {
            _trafficDensity = Mathf.Lerp(_trafficDensity, Mathf.Clamp01(life.VisibleTraffic / 14f), 1f - Mathf.Exp(-dt / 1.0f));
            _amb.Target[ShuntaAudioMap.Cars] = Mathf.Clamp01(_amb.Target[ShuntaAudioMap.Cars] * Mathf.Lerp(0.7f, 1.3f, _trafficDensity));
        }
        if (sky != null && sky.VisibleCars > 0)
        {
            float swell = Mathf.Clamp01(1f - sky.NearestTrainM / 140f);        // passes close -> loud rumble, fades with distance
            _amb.Target[ShuntaAudioMap.Train] = Mathf.Max(_amb.Target[ShuntaAudioMap.Train], 0.85f * swell * swell);
        }
        _amb.TunnelFx = _tunnel;
        _amb.SpeedMps = speed;
        _amb.CarsPerMin = 6f + 14f * _effort;
        _amb.GullsPerMin = 7f;
        _amb.Master = (0.35f + 0.65f * level) * gate * _enter;

        for (int i = 0; i < ShuntaAudioMap.MusCount; i++) _mus.Target[i] = _mw[i];
        _mus.Bpm = _bpm + 8f * _effort;
        _mus.Brightness = Mathf.Clamp01(_bright + 0.15f * _effort);
        _mus.Effort = _effort;
        _mus.TunnelFx = _tunnel;
        _mus.Master = level * 0.55f * gate * _enter;
    }

    public string Describe() =>
        $"km={_km:F2} zone={ShuntaAudioMap.ZoneAt(_km) + 1} bpm={_bpm + 8f * _effort:F0} bright={_bright:F2} effort={_effort:F2} tunnel={_tunnel:F2}";
}

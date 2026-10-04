using System;
using UnityEngine;

/// <summary>
/// What the Nagisa Bay audio directors need from the ride. The real implementation is
/// <see cref="NagisaAudioHost"/>; the editor self-test supplies a scripted stub so the whole
/// positional/dynamic system can be exercised headlessly.
/// </summary>
public interface INagisaAudioContext
{
    /// <summary>The Nagisa course (positions/sides baked from NagisaRoute.json).</summary>
    RouteCourse Course { get; }
    /// <summary>Metres along the course (this lap).</summary>
    float RouteD { get; }
    float SpeedMps { get; }
    /// <summary>World position of the AudioListener (falls back to the rider).</summary>
    Vector3 ListenerPos { get; }
    /// <summary>0..1: closed while the title / World Map is up (same gate RideAudio uses for music).</summary>
    float Gate { get; }
    /// <summary>Master music level (follows RideAudio.musicVolume when present).</summary>
    float MusicLevel { get; }
}

/// <summary>Small shared helpers for the Nagisa audio (piecewise-linear curves over route distance).</summary>
public static class NagisaAudioMath
{
    /// <summary>Evaluates packed (distance, value) keyframes at <paramref name="d"/>; clamps outside the range.</summary>
    public static float Curve(float[] kv, float d)
    {
        int n = kv.Length / 2;
        if (n == 0) return 0f;
        if (d <= kv[0]) return kv[1];
        for (int i = 1; i < n; i++)
        {
            float d1 = kv[i * 2];
            if (d <= d1)
            {
                float d0 = kv[(i - 1) * 2];
                float t = d1 - d0 < 1e-3f ? 1f : (d - d0) / (d1 - d0);
                return Mathf.Lerp(kv[(i - 1) * 2 + 1], kv[i * 2 + 1], t);
            }
        }
        return kv[(n - 1) * 2 + 1];
    }

    /// <summary>1 inside <paramref name="radius"/>*0.55, easing to 0 at <paramref name="radius"/> (so culling never pops).</summary>
    public static float DistGain(float dist, float radius)
    {
        float a = radius * 0.55f;
        if (dist <= a) return 1f;
        if (dist >= radius) return 0f;
        float t = (dist - a) / (radius - a);
        return 1f - t * t * (3f - 2f * t);
    }
}

/// <summary>
/// Starts the Nagisa Bay audio when (and only when) the ride is on the Nagisa course: positional ambience
/// (<see cref="NagisaAmbienceDirector"/>) and the zone-layered music (<see cref="NagisaMusicDirector"/>).
/// Self-bootstrapping so no scene or other script has to reference it; everything is created and destroyed at
/// runtime, nothing is saved into the scene. Regions other than Nagisa Bay are untouched.
/// </summary>
[DisallowMultipleComponent]
public sealed class NagisaAudioHost : MonoBehaviour, INagisaAudioContext
{
    public const string CourseId = "nagisa_bay_loop";
    public const string RegionId = "nagisa_bay";

    private RideSession _session;
    private RegionDirector _regions;
    private RideAudio _rideAudio;
    private AudioListener _listener;
    private NagisaMusicDirector _music;
    private NagisaAmbienceDirector _ambience;
    private float _poll;
    private float _logAt;

    public static NagisaAudioHost Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        if (Environment.GetEnvironmentVariable("MR_NAGISA_AUDIO") == "0") return;
        var go = new GameObject("~NagisaAudioHost");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<NagisaAudioHost>();
    }

    // ---- INagisaAudioContext ------------------------------------------------------------------
    public RouteCourse Course => _session != null ? _session.Course : null;
    public float RouteD => _session != null ? _session.DistanceM : 0f;
    public float SpeedMps => _session != null ? Mathf.Max(0f, _session.SpeedMps) : 0f;
    public float Gate => _rideAudio != null ? _rideAudio.MusicGate : 1f;
    public float MusicLevel => _rideAudio != null ? Mathf.Clamp01(_rideAudio.musicVolume * 1.4f) : 0.45f;
    public Vector3 ListenerPos
    {
        get
        {
            if (_listener != null && _listener.isActiveAndEnabled) return _listener.transform.position;
            return _session != null ? _session.WorldPosition : Vector3.zero;
        }
    }

    private bool OnNagisa()
    {
        var c = Course;
        if (c == null || c.Count < 2) return false;
        if (c.Id == CourseId) return true;
        return _regions != null && _regions.currentRegionId == RegionId && c.Length > 10000f;
    }

    private void Update()
    {
        _poll -= Time.unscaledDeltaTime;
        if (_poll > 0f) return;
        _poll = 0.5f;

        if (_session == null) _session = FindAnyObjectByType<RideSession>();
        if (_regions == null) _regions = FindAnyObjectByType<RegionDirector>();
        if (_rideAudio == null) _rideAudio = FindAnyObjectByType<RideAudio>();
        if (_listener == null || !_listener.isActiveAndEnabled) _listener = FindAnyObjectByType<AudioListener>();

        bool want = _session != null && OnNagisa();
        if (want && _music == null)
        {
            var m = new GameObject("NagisaMusic"); m.transform.SetParent(transform, false);
            _music = m.AddComponent<NagisaMusicDirector>(); _music.Init(this);
            var a = new GameObject("NagisaAmbience"); a.transform.SetParent(transform, false);
            _ambience = a.AddComponent<NagisaAmbienceDirector>(); _ambience.Init(this);
            Debug.Log($"[nagisa-audio] started (course {Course.Id}, {Course.Length:F0} m)");
        }
        else if (!want && _music != null)
        {
            Destroy(_music.gameObject); Destroy(_ambience.gameObject);
            _music = null; _ambience = null;
            Debug.Log("[nagisa-audio] stopped (left the Nagisa course)");
        }

        if (_music != null && Time.unscaledTime > _logAt && Environment.GetEnvironmentVariable("MR_NAGISA_AUDIO_LOG") == "1")
        {
            _logAt = Time.unscaledTime + 20f;
            Debug.Log($"[nagisa-audio] d={RouteD:F0} {_music.Describe()} | {_ambience.Describe()}");
        }
    }
}

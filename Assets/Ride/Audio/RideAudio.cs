using UnityEngine;

/// <summary>
/// Region BGM plus the bike's own sounds. Every clip is original and rendered offline by
/// tools/audio/make_ride_audio.py and make_region_bgm.py into Assets/Resources/Audio, so nothing here synthesises
/// at runtime.
///
/// Bike layers, all 2D loops that always play and are only ever faded:
///  * drivetrain: chain over the rings; volume follows pedalling, pitch follows cadence (clip = 90 rpm).
///  * freewheel:  hub ratchet ticking while COASTING; pitch follows wheel speed (clip = 64 ticks/s).
///  * tyre roll + wind: speed-driven beds, so the bike never goes silent at speed.
/// </summary>
[DisallowMultipleComponent]
public sealed class RideAudio : MonoBehaviour
{
    public RideSession session;
    public DeviceManager devices;
    public RegionDirector regions;

    [Range(0f, 1f)] public float musicVolume = 0.45f;
    [Range(0f, 1f)] public float bikeVolume = 0.65f;

    // Wheel ~2.1 m circumference, 24 ratchet engagements per wheel revolution.
    private const float WheelCircumferenceM = 2.1f;
    private const float RatchetTeeth = 24f;
    private const float FreewheelClipTicksPerSec = 64f;

    private AudioSource _musicA, _musicB, _drive, _freewheel, _tyre, _wind;
    private string _musicRegion;
    private float _musicFade = 1f;
    private float _pedalBlend;
    private float _titleCheck;
    private bool _titleUp;
    private WorldMapHud _worldMap;
    /// <summary>0..1 gate on the region music: closed while the title/selection theme plays or the
    /// World Map is open, so a map's music never carries on under the map screen.</summary>
    private float _musicGate = 1f;

    /// <summary>Current region-music gate (0 = silenced by the title or an open World Map).</summary>
    public float MusicGate => _musicGate;

    /// <summary>
    /// One loop per region. Minato's comes from tools/audio/make_ride_audio.py; the other six
    /// from tools/audio/make_region_bgm.py (2026-09-25). A region whose clip has not been
    /// rendered yet simply plays no music: Resources.Load returns null and the crossfade
    /// stops the old track, exactly as before.
    /// </summary>
    private static string BgmFor(string regionId)
    {
        switch (regionId)
        {
            case RegionCatalog.MinatoCoast: return "Audio/BGM_MinatoCoast";
            case RegionCatalog.SakuraPass: return "Audio/BGM_SakuraPass";
            case RegionCatalog.ShiosaiCoast: return "Audio/BGM_ShiosaiCoast";
            case RegionCatalog.MapleCity: return "Audio/BGM_MapleCity";
            case RegionCatalog.AzoraHighlands: return "Audio/BGM_AzoraHighlands";
            case RegionCatalog.TakaMountains: return "Audio/BGM_TakaMountains";
            case RegionCatalog.FujiRidge: return "Audio/BGM_FujiRidge";
            default: return null;
        }
    }

    private void Start()
    {
        // Scenes serialize the old levels (0.32 / 0.55), so the new script defaults never reach
        // them. Lift only values still sitting on the old defaults; a hand-tuned level is kept.
        if (Mathf.Approximately(musicVolume, 0.32f)) musicVolume = 0.45f;
        if (Mathf.Approximately(bikeVolume, 0.55f)) bikeVolume = 0.65f;
        if (session == null) session = GetComponent<RideSession>();
        if (devices == null) devices = GetComponent<DeviceManager>();
        if (regions == null) regions = GetComponent<RegionDirector>();

        _musicA = NewSource("BGM A", true);
        _musicB = NewSource("BGM B", true);
        _drive = NewLoop("Audio/Bike_Drivetrain");
        _freewheel = NewLoop("Audio/Bike_Freewheel");
        _tyre = NewLoop("Audio/Bike_TyreRoll");
        _wind = NewLoop("Audio/Bike_Wind");

        if (regions != null) regions.RegionChanged += OnRegionChanged;
        OnRegionChanged(regions != null ? regions.currentRegionId : null);
    }

    private void OnDestroy()
    {
        if (regions != null) regions.RegionChanged -= OnRegionChanged;
    }

    private AudioSource NewSource(string name, bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;
        src.volume = 0f;
        return src;
    }

    private AudioSource NewLoop(string resource)
    {
        var src = NewSource(resource, true);
        src.clip = Resources.Load<AudioClip>(resource);
        if (src.clip == null) { Debug.LogWarning($"[ride-audio] missing {resource}"); return src; }
        // Random start phase so the four loops never line up into an audible repeat.
        src.timeSamples = Random.Range(0, src.clip.samples);
        src.Play();
        return src;
    }

    private void OnRegionChanged(string regionId)
    {
        if (regionId == _musicRegion) return;
        _musicRegion = regionId;
        string path = BgmFor(regionId);
        // Crossfade: the incoming track goes on the silent source, the old one fades out.
        (_musicA, _musicB) = (_musicB, _musicA);
        _musicA.clip = path != null ? Resources.Load<AudioClip>(path) : null;
        if (_musicA.clip != null) { _musicA.volume = 0f; _musicA.Play(); }
        else _musicA.Stop();
        _musicFade = 0f;
    }

    /// <summary>Cheap same-frame check for the selection theme, which the flow director adds on
    /// the frame the map opens (the 0.5 s title poll would let the ride music leak for a beat).</summary>
    private static bool FindTitleNow() =>
        MapleRideFlowDirector.Instance != null && MapleRideFlowDirector.Instance.SelectionBgmPlaying;

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // The title screen plays its own theme; stay silent underneath it.
        if ((_titleCheck -= dt) <= 0f)
        {
            _titleCheck = 0.5f;
            _titleUp = FindAnyObjectByType<MapleRideTitleBgm>() != null;
            if (_worldMap == null) _worldMap = FindAnyObjectByType<WorldMapHud>();
            // The title root owns the only fallback AudioListener; once it is gone the ride
            // would be silent, so make sure the main camera hears the world.
            if (!_titleUp && FindAnyObjectByType<AudioListener>() == null && Camera.main != null)
                Camera.main.gameObject.AddComponent<AudioListener>();
        }
        float master = _titleUp ? 0f : 1f;

        // The map music stops while the World Map is up (in-ride TAB or the opening selection)
        // and comes back when the ride resumes. Read every frame (isOpen is a plain field), so
        // it cuts on the frame the map opens instead of up to 0.5 s later.
        bool mapUp = _worldMap != null && _worldMap.isOpen;
        bool silenced = _titleUp || mapUp || FindTitleNow();
        _musicGate = Mathf.MoveTowards(_musicGate, silenced ? 0f : 1f, dt / (silenced ? 0.25f : 1.2f));
        float music = musicVolume * _musicGate;

        _musicFade = Mathf.MoveTowards(_musicFade, 1f, dt / 2.5f);
        _musicA.volume = music * _musicFade;
        _musicB.volume = music * (1f - _musicFade) * (_musicB.isPlaying ? 1f : 0f);
        if (_musicFade >= 1f && _musicB.isPlaying) _musicB.Stop();

        float speed = session != null ? Mathf.Max(0f, session.SpeedMps) : 0f;
        var tm = devices != null ? devices.Telemetry : default;
        bool pedalling = tm.CadenceRpm > 15f && tm.Watts > 8f && speed > 0.3f;
        _pedalBlend = Mathf.MoveTowards(_pedalBlend, pedalling ? 1f : 0f, dt / 0.25f);
        float moving = Mathf.Clamp01(speed / 1.5f);
        float bike = bikeVolume * master;

        // A lubed road drivetrain is nearly silent: a faint whir that grows a little with cadence.
        _drive.volume = bike * 0.16f * _pedalBlend * moving * Mathf.Clamp(tm.CadenceRpm / 90f, 0.6f, 1.2f);
        _drive.pitch = Mathf.Clamp(tm.CadenceRpm / 90f, 0.5f, 1.6f);

        float ticksPerSec = speed / WheelCircumferenceM * RatchetTeeth;
        _freewheel.volume = bike * 0.34f * (1f - _pedalBlend) * Mathf.Clamp01(speed / 0.8f);
        _freewheel.pitch = Mathf.Clamp(ticksPerSec / FreewheelClipTicksPerSec, 0.25f, 2.2f);

        float s = Mathf.Clamp01(speed / 13f);
        _tyre.volume = bike * 0.42f * Mathf.Sqrt(s);   // the main riding sound
        _tyre.pitch = 0.75f + 0.45f * s;
        // Wind noise follows the APPARENT wind from WeatherDirector (same data as the physics),
        // so a headwind roars and a matching tailwind goes quiet; falls back to road speed.
        var wd = WeatherDirector.Instance;
        float air = wd != null ? wd.Apparent.SpeedMps : speed;
        float a = Mathf.Clamp01(air / 14f);
        _wind.volume = bike * 0.40f * a * a;
        _wind.pitch = 0.85f + 0.3f * a;
    }
}

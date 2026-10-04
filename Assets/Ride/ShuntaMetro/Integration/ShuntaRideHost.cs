using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Self-bootstrapping runtime host that wires the Shunta Metro gameplay logic (zone modifiers, boost chevrons,
/// segment timer) into the live ride. Active ONLY while RideSession.courseId == "shunta_metro".
/// Uses only existing public APIs: CyclingPhysics.dragMultiplier, RideSession.SpeedCapMps/DistanceM/ElapsedSeconds.
/// Runs in LateUpdate, i.e. after RideSession.Tick and the traffic/weather directors wrote this frame's drag.
/// </summary>
public sealed class ShuntaRideHost : MonoBehaviour
{
    public static ShuntaRideHost Instance { get; private set; }

    /// <summary>Latest sampled zone modifiers (grip / draft / pack tightness). Valid while Active.</summary>
    public ShuntaModifiers Current = ShuntaModifiers.Neutral;
    public bool Active { get; private set; }
    public float SpeedMultiplierApplied { get; private set; } = 1f;
    public int ChevronsCollected => _boost != null ? _boost.Collected : 0;
    public int SegmentsFinished { get; private set; }
    public string LastSegmentLine { get; private set; } = "";

    public ShuntaBoostState Boost => _boost;
    public float DragBaseline { get; private set; } = 1f;
    public float DragApplied { get; private set; } = 1f;
    public int LastChevronId { get; private set; } = -1;
    RouteFollower _follower;
    float _dragBaseline = 1f, _lastWritten = float.NaN;
    bool _haveBaseline;

    RideSession _session;
    ShuntaZoneModifiers _zones;
    ShuntaBoostState _boost;
    ShuntaSegmentTimer _timer;
    ShuntaSegmentBoard _board;
    List<ShuntaSegmentDef> _defs;
    RaceUi _ui;
    float _nextFind;
    int _runSerial = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_SHUNTA_HOST") == "0") return;
        var go = new GameObject("~ShuntaRideHost");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaRideHost>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void BuildLogic()
    {
        var course = ShuntaCourseData.Load();
        _zones = new ShuntaZoneModifiers(course);
        _boost = new ShuntaBoostState(ShuntaChevronLayout.Place(course));
        _defs = ShuntaSegmentTable.Build(course);
        _board = ShuntaSegmentBoard.Seeded(_defs);
        NewTimer();
        SegmentsFinished = 0;
        if (ShuntaHud.Instance != null) ShuntaHud.Instance.Boost = _boost;
    }

    void NewTimer()
    {
        _timer = new ShuntaSegmentTimer(_defs);
        _timer.Finished += OnSegmentFinished;
    }

    void OnSegmentFinished(ShuntaSegmentDef d, float seconds)
    {
        SegmentsFinished++;
        int rank = _board.Submit(d.id, "You", seconds);
        int tenths = Mathf.RoundToInt(seconds * 10f);
        string line = d.name + ": " + (tenths / 600) + ":" + ((tenths % 600) / 10f).ToString("00.0") + (rank > 0 ? "  (#" + rank + ")" : "");
        LastSegmentLine = line;
        Debug.Log("[shunta-host] segment finished " + line);
        if (_ui == null) _ui = FindFirstObjectByType<RaceUi>();
        if (_ui != null) _ui.Toast(line, Color.white);
    }

    void LateUpdate()
    {
        if (_session == null)
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            _session = FindFirstObjectByType<RideSession>();
            if (_session == null) return;
        }
        bool on = _session.courseId == ShuntaRouteProvider.CourseId && _session.Course != null;
        if (!on)
        {
            if (Active)
            {
                Active = false; SpeedMultiplierApplied = 1f; Current = ShuntaModifiers.Neutral;
                if (_haveBaseline && _session.physics != null && _session.physics.dragMultiplier == _lastWritten) _session.physics.dragMultiplier = _dragBaseline;
                _haveBaseline = false;
            }
            return;
        }
        if (_zones == null) BuildLogic();
        Active = true;
        if (_session.RunSerial != _runSerial) { _runSerial = _session.RunSerial; _boost.Reset(); NewTimer(); _haveBaseline = false; }

        float dt = Time.deltaTime;
        float km = _session.DistanceM / 1000f;
        Current = _zones.Sample(km);

        // Drag is recomputed from a BASELINE every frame, never from our own previous output (that compounded
        // 1.35x per tunnel frame and divided by boost^2 repeatedly). If another system (traffic director) wrote a
        // different value since our last write, that value is the new baseline; otherwise the stored one stands.
        var ph = _session.physics;
        if (!_haveBaseline || ph.dragMultiplier != _lastWritten) { _dragBaseline = ph.dragMultiplier; _haveBaseline = true; }
        DragBaseline = _dragBaseline;
        float drag = 1f - Mathf.Clamp01((1f - _dragBaseline) * Current.draftMultiplier);

        // Chevron boost: faster road speed ~ drag reduced by the inverse square (aero-dominated).
        if (_follower == null) _follower = FindFirstObjectByType<RouteFollower>();
        float lateral = float.NaN;   // fixed lane (no steering): accept the whole chevron lane band
        if (_follower != null && _follower.laneChangeEnabled)
        {
            // Real lane offset, mapped from the rider's steer range onto the chevron lanes (+-1.8 m).
            float rel = _follower.ActiveLaneOffset - _follower.laneOffset;
            lateral = rel >= 0f ? rel / Mathf.Max(0.01f, _follower.passShiftM) * 1.8f
                                : rel / Mathf.Max(0.01f, _follower.yieldShiftM) * 1.8f;
        }
        int got = _boost.Tick(km, lateral, dt);
        if (got >= 0)
        {
            LastChevronId = got;
            var hud0 = ShuntaHud.Instance;
            if (hud0 != null) hud0.PostStatus("BOOST!", 2f);
        }
        var hud = ShuntaHud.Instance;
        if (hud != null && hud.Boost != _boost) hud.Boost = _boost;
        float mult = _boost.SpeedMultiplier;
        SpeedMultiplierApplied = mult;
        if (mult > 1f) drag /= mult * mult;
        drag = Mathf.Max(0.2f, drag);
        ph.dragMultiplier = drag;
        _lastWritten = drag;
        DragApplied = drag;
        // Wet grip: limit descent speed (cornering confidence) in low-grip zones.
        if (Current.gripMultiplier < 0.999f && _session.Grade < 0f)
        {
            float cap = ph.descentCapKph / 3.6f * Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(0.6f, 1f, Current.gripMultiplier));
            _session.SpeedCapMps = float.IsNaN(_session.SpeedCapMps) ? cap : Mathf.Min(_session.SpeedCapMps, cap);
        }

        _timer.Tick(km, _session.ElapsedSeconds);
    }
}

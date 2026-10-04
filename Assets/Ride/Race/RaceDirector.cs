using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC races, end to end. Added to the ride root by <see cref="RideBootstrap"/> in play mode.
///
/// Out of a race it owns the racers (<see cref="RaceNpc"/>: existing riders riding the run-in to
/// their start lines, pacing Kuro abeam while they challenge him), their floating markers, the
/// "E  Race" prompt within ~5 m, Kuro's rank badge and the
/// Riders screen on [R].
///
/// A race runs on the CURRENT course, from the racer's spot: fade out, "Kuro vs NPC" card,
/// 3-2-1-GO, race, results, fade back to exactly where Kuro stopped. While racing the ride
/// session is driven externally (<see cref="RideSession.ExternalControl"/>) by the brief's race
/// model (<see cref="RaceRider"/>), so the rest of the game (route follower, camera, rider rig,
/// minimap) just sees Kuro moving along the road. The ride clock and ride distance do not
/// advance during a race; checkpoints and the shop are paused.
///
/// Input: keyboard [W]/Up pedal, [Shift] sprint, [A]/[D] change line, [Esc] forfeit. With a real
/// trainer connected (<see cref="DeviceManager.ExternalTrainerActive"/>) the legs work too:
/// watts over <see cref="trainerPedalFtp"/> x FTP pedal and over <see cref="trainerSprintFtp"/>
/// x FTP sprint, while the keys keep working alongside (user decision 2026-09-26).
/// </summary>
[DefaultExecutionOrder(-90)]
public sealed class RaceDirector : MonoBehaviour
{
    public enum Phase { Idle, Intro, Countdown, Racing, Results, Outro }

    public static RaceDirector Instance { get; private set; }
    /// <summary>True from the moment a race starts until the fade back has finished.</summary>
    public static bool Busy => Instance != null && Instance.CurrentPhase != Phase.Idle;
    public static bool ScreenOpen => Instance != null && Instance._riders != null && Instance._riders.IsOpen;

    [Header("Keys")]
    public KeyCode raceKey = KeyCode.E;
    public KeyCode ridersKey = KeyCode.R;
    public KeyCode forfeitKey = KeyCode.Escape;

    [Header("Tuning (PROVISIONAL)")]
    public float promptRadiusM = 5f;
    [Tooltip("Prompt radius for a racer who is pacing Kuro (riding beside him offering a race): " +
             "wider, so the card does not flicker as the moving pair's gap breathes.")]
    public float pacingPromptRadiusM = 8f;
    [Tooltip("Prompt radius for a racer who has stopped (or is stopping) with Kuro after he braked: " +
             "they overshoot a little when he stops hard, and the prompt must stay up while they wait.")]
    public float holdPromptRadiusM = 14f;
    [Tooltip("Trainer: watts at or above this fraction of FTP count as pedalling.")]
    public float trainerPedalFtp = 0.5f;
    [Tooltip("Trainer: watts at or above this fraction of FTP count as sprinting.")]
    public float trainerSprintFtp = 1.2f;
    [Tooltip("Metres past the racer's parked bike where the start line is.")]
    public float startOffsetM = 6f;
    [Tooltip("Seconds the second rider has to finish before their time is estimated.")]
    public float finishGraceS = 20f;
    [Tooltip("After a race the rival is put back on the road this many metres ahead of Kuro, riding off.")]
    public float rideOffLeadM = 7f;

    /// <summary>The race lines (metres right of the centreline). Kuro starts on the outer.</summary>
    public static readonly float[] Lines = { -2.1f, -1.2f, -0.3f };

    public Phase CurrentPhase { get; private set; }
    public RaceRider Kuro => _kuro;
    public RaceRider Rival => _rival;
    public RacerDef CurrentDef => _def;
    public RaceResult LastResult { get; private set; }
    public IReadOnlyList<RaceNpc> Racers => _npcs;
    public RaceUi Ui => _ui;
    public RidersScreen Riders => _riders;
    public RacePromptCard Prompt => _prompt;
    public string TrackedId => _trackedId;

    // capture/test hooks (the harness cannot press keys)
    [System.NonSerialized] public float PedalOverride = float.NaN;
    [System.NonSerialized] public float SprintOverride = float.NaN;
    [System.NonSerialized] public bool AutoContinue;
    /// <summary>Test hook: Kuro's true along-road speed when a harness drives him by seeking
    /// (the session speed stays 0 then, and a distance-rate measure lags a frame, so a slow
    /// capture frame would read as Kuro stopping). NaN = measure it.</summary>
    [System.NonSerialized] public float KuroSpeedOverride = float.NaN;

    private RideBootstrap _boot;
    private RaceUi _ui;
    private RacePromptCard _prompt;
    private RaceHudView _hud;
    private RaceIntroView _intro;
    private RaceResultsView _results;
    private RidersScreen _riders;
    private PlayerRankBadge _badge;

    private readonly List<RaceNpc> _npcs = new List<RaceNpc>();
    private readonly Dictionary<RaceNpc, RaceMarker> _markers = new Dictionary<RaceNpc, RaceMarker>();
    private readonly Dictionary<string, GameObject> _sources = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, RouteCourse> _courses = new Dictionary<string, RouteCourse>();
    private string _courseId;
    private bool _coursePending;   // some racer of this course could not be parked yet (region hidden)
    private RaceNpc _near;
    private string _trackedId = "";
    private const string TrackedKey = "MapleRide.Race.Tracked";
    private bool _badgeDirty = true;

    // race state
    private RaceNpc _npc;
    private RacerDef _def;
    private RaceRider _kuro, _rival;
    private RaceRivalAi _ai;
    private Transform _rivalT;
    private GameObject _ghost, _props;
    private float _startM, _length, _raceTime, _endAt, _wobbleTimer, _wobbleTarget = 1f;
    private float _rivalLaneTarget;
    private int _kuroLine;
    private bool _forfeit, _perfectDone, _continue, _finishToastK, _finishToastR;
    private KuroBikeRig _kuroRig;
    private KuroRidePose _kuroPose;
    private bool _savedCadenceCrank;
    private readonly List<Material> _ghostMats = new List<Material>();
    private int _pendingCoins;

    // ------------------------------------------------------------------ setup

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; RouteMapHud.TrackedWorld = null; }

    private void Start()
    {
        _boot = GetComponent<RideBootstrap>();
        _ui = GetComponent<RaceUi>();
        if (_ui == null) _ui = gameObject.AddComponent<RaceUi>();
        _ui.Build();
        _prompt = new RacePromptCard(_ui.PromptLayer, () => TryStartNear());
        _hud = new RaceHudView(_ui.HudLayer);
        _intro = new RaceIntroView(_ui.ModalLayer);
        _results = new RaceResultsView(_ui.ModalLayer, () => _continue = true);
        _riders = new RidersScreen(_ui.ModalLayer, () => _trackedId, Track, Where, CloseRiders);
        _badge = new PlayerRankBadge(_ui.PromptLayer);
        _trackedId = PlayerPrefs.GetString(TrackedKey, "");
        RiderProgress.Changed += MarkBadge;
        PlayerWardrobe.Changed += MarkBadge;
        IndexSources();
        AttachBlinks();
    }

    private void OnDisable()
    {
        RiderProgress.Changed -= MarkBadge;
        PlayerWardrobe.Changed -= MarkBadge;
    }

    private void MarkBadge() { _badgeDirty = true; }

    /// <summary>Every rider rig in the scene (inactive regions too), indexed by its object name and
    /// its ancestors' names, so racers can be matched to their existing riders.</summary>
    private void IndexSources()
    {
        var wanted = new HashSet<string>();
        foreach (var d in RaceRoster.All) wanted.Add(d.SceneObject);
        foreach (var rig in FindObjectsByType<KuroBikeRig>(FindObjectsInactive.Include))
        {
            var t = rig.transform;
            for (int up = 0; up < 3 && t != null; up++, t = t.parent)
                if (wanted.Contains(t.name) && !_sources.ContainsKey(t.name)) _sources[t.name] = t.gameObject;
        }
        foreach (var d in RaceRoster.All)
            if (!_sources.ContainsKey(d.SceneObject))
                Debug.LogWarning($"[race] racer {d.Name}: scene rider '{d.SceneObject}' not found; they will not appear.");
        Debug.Log($"[race] {_sources.Count}/{RaceRoster.All.Length} racers matched to scene riders");
    }

    /// <summary>Eyelids + blinking for every rider whose face mesh has baked eyelid data.</summary>
    private void AttachBlinks()
    {
        int n = 0;
        var seen = new HashSet<GameObject>();
        foreach (var rig in FindObjectsByType<KuroBikeRig>(FindObjectsInactive.Include))
        {
            var go = rig.gameObject;
            if (!seen.Add(go)) continue;
            if (RiderBlink.Attach(go) != null || (go.transform.parent != null && RiderBlink.Attach(go.transform.parent.gameObject) != null)) n++;
        }
        Debug.Log($"[race] eyelids on {n} riders");
    }

    private RouteCourse CourseFor(string id)
    {
        if (_courses.TryGetValue(id, out var c)) return c;
        var graph = _boot != null && _boot.session != null ? _boot.session.Graph : RouteGraph.Load();
        var def = graph != null ? graph.Course(id) : null;
        c = def != null ? RouteCourse.Build(graph, def) : null;
        _courses[id] = c;
        return c;
    }

    public static float RoadHalfWidth(string courseId)
    {
        switch (courseId)
        {
            case "minato_crossing": return 4.0f;
            case "taka_high_road": return 2.6f;
            case "fuji_clouds_above": return 2.5f;
            case "azora_ascent": return 3.0f;
            default: return 3.5f;
        }
    }

    /// <summary>Takes over the racers of the course just selected (they ride it, see
    /// <see cref="RaceNpc.TickAmbient"/>); racers elsewhere keep their normal ride loop.</summary>
    private void ActivateCourse(string courseId)
    {
        _courseId = courseId;
        _coursePending = false;
        foreach (var d in RaceRoster.All)
        {
            if (d.CourseId != courseId) continue;
            bool exists = false;
            foreach (var n in _npcs) if (n.def == d) { exists = true; break; }
            if (exists) continue;
            if (!_sources.TryGetValue(d.SceneObject, out var src) || src == null) continue;
            var course = CourseFor(d.CourseId);
            if (course == null) continue;
            GameObject go = src;
            var parent = src.transform.parent;
            if (parent != null && !parent.gameObject.activeInHierarchy) { _coursePending = true; continue; }
            if (d.CloneSource)
            {
                go = Instantiate(src, src.transform.parent);
                go.name = "Race NPC " + d.Name;
                go.SetActive(true);
                foreach (var b in go.GetComponentsInChildren<RiderBlink>(true)) Destroy(b);
                foreach (Transform c in go.GetComponentsInChildren<Transform>(true))
                    if (c.name == "~Eyelid") Destroy(c.gameObject);
            }
            if (!go.activeInHierarchy) { _coursePending = true; continue; }   // region hidden; retry
            var npc = go.GetComponent<RaceNpc>();
            if (npc == null) npc = go.AddComponent<RaceNpc>();
            npc.Init(d, course, RoadHalfWidth(d.CourseId));
            if (d.CloneSource) RiderBlink.Attach(go);
            _npcs.Add(npc);
            _markers[npc] = new RaceMarker(npc, _ui.MarkerLayer);
            Debug.Log($"[race] {d.Name} (Lv {d.Level} {RaceMath.DisplayName(d.Type)}) {(npc.AmbientRiding ? "riding" : "parked")} at {d.CourseId} {d.SpotM:0} m");
        }
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        var session = _boot != null ? _boot.session : null;
        if (session == null || session.Course == null) return;
        if (session.courseId != _courseId || (_coursePending && Time.frameCount % 30 == 0)) ActivateCourse(session.courseId);

        if (_badgeDirty) { _badge.Refresh(); _badgeDirty = false; if (_riders.IsOpen) _riders.Refresh(); }
        UpdateTracking(session);
        TickRidingRacers(session, Time.deltaTime);

        switch (CurrentPhase)
        {
            case Phase.Idle: TickIdle(session); break;
            case Phase.Racing: TickRace(session, Time.deltaTime); break;
            case Phase.Results:
                _results.Tick();
                if (AutoContinue || Input.GetKeyDown(raceKey) || Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    _continue = true;
                break;
        }
    }

    private Transform KuroT => _boot.rider != null ? _boot.rider : (_boot.follower != null ? _boot.follower.rider : null);

    private bool OtherUiOwnsInput()
    {
        var hud = _boot.hud;
        if (hud != null && hud.worldMap != null && (hud.worldMap.isOpen || hud.worldMap.selectionMode)) return true;
        var shop = GetComponent<MapleRowShop>();
        if (shop != null && (shop.IsOpen || shop.Busy)) return true;
        return RideInputGate.Locked && !_riders.IsOpen;
    }

    // ------------------------------------------------------------------ riding racers

    private float _lastKuroTotal = float.NaN, _kuroSpeedEst;
    private readonly Queue<Vector2> _kuroSamples = new Queue<Vector2>();   // (time, total metres)
    /// <summary>Kuro's along-road speed as the racers see it (m/s): the session speed, or the
    /// measured ride-distance rate when that is higher (a scripted drive), teleports ignored.
    /// The rate is measured over a ~0.35 s window, not one frame: a single-frame ratio swings
    /// wildly with uneven frame times (a slow frame after a fast one read as a near-stop, which
    /// would make a pacing racer brake for no reason now that they stop with a stopped Kuro).</summary>
    public float KuroSpeedEstimate => _kuroSpeedEst;

    /// <summary>Racers ride the course out of a race (<see cref="RaceNpc.TickAmbient"/>): they
    /// pace Kuro while offering a race and only avoid him during someone else's race.</summary>
    private void TickRidingRacers(RideSession session, float dt)
    {
        if (dt <= 0f) return;
        float total = session.TotalDistanceM, now = Time.time;
        if (float.IsNaN(_lastKuroTotal) || Mathf.Abs(total - _lastKuroTotal) > 40f) _kuroSamples.Clear();   // teleport / seek
        _lastKuroTotal = total;
        _kuroSamples.Enqueue(new Vector2(now, total));
        while (_kuroSamples.Count > 2 && now - _kuroSamples.Peek().x > 0.35f) _kuroSamples.Dequeue();
        var first = _kuroSamples.Peek();
        float span = now - first.x;
        float measured = span > 0.05f ? (total - first.y) / span : 0f;
        float v = session.SpeedMps;
        if (measured >= 0f && measured <= 30f) v = Mathf.Max(v, measured);
        if (!float.IsNaN(KuroSpeedOverride)) v = Mathf.Max(0f, KuroSpeedOverride);
        _kuroSpeedEst = Mathf.Lerp(_kuroSpeedEst, v, 1f - Mathf.Exp(-10f * dt));
        GatherTraffic(dt);

        bool allowPace = CurrentPhase == Phase.Idle && !_riders.IsOpen && !OtherUiOwnsInput();
        float kuroLane = _boot.follower != null ? _boot.follower.ActiveLaneOffset : -1.7f;
        if (float.IsNaN(kuroLane)) kuroLane = _boot.follower != null ? _boot.follower.laneOffset : -1.7f;
        foreach (var npc in _npcs)
        {
            if (npc == null || !npc.AmbientRiding || npc.def.CourseId != session.courseId || !npc.gameObject.activeInHierarchy) continue;
            // hold behind another racer on the same line instead of riding through them
            float cap = float.PositiveInfinity;
            foreach (var o in _npcs)
            {
                if (o == null || o == npc || !o.AmbientRiding || o.def.CourseId != npc.def.CourseId || !o.gameObject.activeInHierarchy) continue;
                float g = npc.Gap(o.RouteM, npc.RouteM);
                if (g > 0f && g < 3.5f && Mathf.Abs(o.Lane - npc.Lane) < 0.9f) cap = Mathf.Min(cap, o.Speed);
            }
            // ... and behind traffic riders (and anything else on a bike) ahead in the same line
            cap = Mathf.Min(cap, npc.ClearanceCap(_traffic));
            npc.TickAmbient(dt, true, session.DistanceM, kuroLane, _kuroSpeedEst, allowPace, cap);
        }
    }

    // ------------------------------------------------------------------ traffic clearance

    private readonly List<KuroBikeRig> _trafficRigs = new List<KuroBikeRig>();
    private readonly Dictionary<KuroBikeRig, Vector3> _trafficLast = new Dictionary<KuroBikeRig, Vector3>();
    private readonly List<RaceNpc.Body> _traffic = new List<RaceNpc.Body>();
    private float _trafficScanAt = -1f;
    /// <summary>Test hook: when set, only riders it accepts count as traffic (a controlled
    /// clearance scenario). Setting it forces a rescan.</summary>
    public System.Func<Transform, bool> TrafficFilter
    {
        get => _trafficFilter;
        set { _trafficFilter = value; _trafficScanAt = -1f; }
    }
    private System.Func<Transform, bool> _trafficFilter;
    /// <summary>Every other rider on a bike this frame (traffic riders, racers, the TT ghost; not
    /// Kuro), for the racers' clearance check and the play test's distance log.</summary>
    public IReadOnlyList<RaceNpc.Body> Traffic => _traffic;

    private void GatherTraffic(float dt)
    {
        if (Time.time >= _trafficScanAt)
        {
            // pooled riders recycle and regions toggle, so rescan now and then (not every frame)
            _trafficScanAt = Time.time + 1.5f;
            _trafficRigs.Clear();
            _trafficRigs.AddRange(FindObjectsByType<KuroBikeRig>(FindObjectsInactive.Exclude));
            if (_trafficLast.Count > _trafficRigs.Count * 2 + 32) _trafficLast.Clear();
        }
        _traffic.Clear();
        var kuro = KuroT;
        foreach (var rig in _trafficRigs)
        {
            if (rig == null || !rig.gameObject.activeInHierarchy) continue;
            var t = rig.transform;
            if (kuro != null && (t == kuro || t.IsChildOf(kuro))) continue;
            if (_trafficFilter != null && !_trafficFilter(t)) continue;
            Vector3 p = t.position, v = Vector3.zero;
            if (_trafficLast.TryGetValue(rig, out var last) && dt > 1e-4f)
            {
                v = (p - last) / dt;
                if (v.sqrMagnitude > 25f * 25f) v = Vector3.zero;   // recycled / teleported this frame
            }
            _trafficLast[rig] = p;
            _traffic.Add(new RaceNpc.Body { Pos = p, Vel = v, Root = t });
        }
    }

    private void TickIdle(RideSession session)
    {
        // Riders screen
        if (_riders.IsOpen)
        {
            if (Input.GetKeyDown(ridersKey) || Input.GetKeyDown(KeyCode.Escape)) { CloseRiders(); return; }
            _riders.HandleKeys();
        }
        else if (Input.GetKeyDown(ridersKey) && !OtherUiOwnsInput())
        {
            OpenRiders(RidersScreen.Tab.Riders);
            return;
        }

        var cam = Camera.main;
        var kuro = KuroT;
        float scale = _ui.Canvas != null ? _ui.Canvas.scaleFactor : 1f;
        bool blocked = _riders.IsOpen || OtherUiOwnsInput();
        _badge.SetVisible(!_riders.IsOpen);
        _near = null;
        float nearD = float.MaxValue;
        foreach (var npc in _npcs)
        {
            if (npc == null) continue;
            var marker = _markers[npc];
            bool here = npc.def.CourseId == session.courseId && npc.gameObject.activeInHierarchy;
            if (!here || kuro == null) { marker.Hide(); npc.engage = 0f; continue; }
            Vector3 d3 = npc.FeetPosition - kuro.position;
            d3.y *= 0.5f;
            float d = d3.magnitude;
            if (blocked) marker.Hide();
            else marker.Tick(cam, d, session.SpeedMps, scale, Time.deltaTime);
            npc.lookTarget = kuro;
            npc.engage = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RaceMarker.ApproachM, RaceMarker.ApproachM * 0.6f, d));
            if (npc.OffersRace && d < nearD) { nearD = d; _near = npc; }
        }

        float nearRadius = _near != null && _near.AmbientRiding && _near.State == RaceNpc.RideState.Pace
            ? Mathf.Max(promptRadiusM, _near.HeldRecently ? holdPromptRadiusM : pacingPromptRadiusM) : promptRadiusM;
        bool prompt = !blocked && _near != null && nearD <= nearRadius && cam != null;
        if (prompt)
        {
            // beside the rider, never over them: the card is kept off the rider's on-screen box
            // (flipped to the other side, or above/below, when the screen edge would push it on)
            if (RiderScreenRect(cam, _near, scale, out var riderRect))
            {
                PromptAvoidRect = riderRect;
                _prompt.Show(_near.def, riderRect);
            }
            else prompt = false;
        }
        if (!prompt) { _prompt.Hide(); return; }
        if (Input.GetKeyDown(raceKey)) TryStartNear();
    }

    /// <summary>The rider's last on-screen box the prompt card was kept clear of (reference px,
    /// 1920x1080 canvas space, origin bottom-left). For the play test.</summary>
    public Rect PromptAvoidRect { get; private set; }

    /// <summary>On-screen box of a rider and their bike (canvas reference px), from the head,
    /// the feet and the wheels. False when the rider is behind the camera.</summary>
    public static bool RiderScreenRect(Camera cam, RaceNpc npc, float canvasScale, out Rect rect)
    {
        rect = default;
        var t = npc.transform;
        Vector3 head = npc.HeadPosition + Vector3.up * 0.18f, feet = t.position;
        Vector3 f = t.forward * 0.95f, r = cam.transform.right * 0.45f;
        var pts = new[] { head + r, head - r, feet + f + r, feet + f - r, feet - f + r, feet - f - r,
                          (head + feet) * 0.5f + f, (head + feet) * 0.5f - f };
        float s = Mathf.Max(0.01f, canvasScale);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var p in pts)
        {
            Vector3 sp = cam.WorldToScreenPoint(p);
            if (sp.z < 0.2f) return false;
            x0 = Mathf.Min(x0, sp.x / s); x1 = Mathf.Max(x1, sp.x / s);
            y0 = Mathf.Min(y0, sp.y / s); y1 = Mathf.Max(y1, sp.y / s);
        }
        rect = Rect.MinMaxRect(x0, y0, x1, y1);
        return true;
    }

    private void TryStartNear()
    {
        if (CurrentPhase != Phase.Idle || _near == null) return;
        StartRace(_near);
    }

    /// <summary>Starts a race against a racer (also the capture harness's entry point).</summary>
    public void StartRace(RaceNpc npc)
    {
        if (CurrentPhase != Phase.Idle || npc == null) return;
        StartCoroutine(RaceFlow(npc));
    }

    public RaceNpc FindRacer(string id)
    {
        foreach (var n in _npcs) if (n != null && n.def.Id == id) return n;
        return null;
    }

    // ------------------------------------------------------------------ Riders screen + tracking

    public void OpenRiders(RidersScreen.Tab tab)
    {
        if (!RideInputGate.Locked) RideInputGate.Lock("riders screen");
        _prompt.Hide();
        _riders.Open(tab);
        _badge.SetVisible(false);
    }

    public void CloseRiders()
    {
        _riders.Close();
        if (RideInputGate.Locked && RideInputGate.Reason == "riders screen") RideInputGate.Unlock();
        _badge.SetVisible(true);
    }

    private string Where(RacerDef d)
    {
        var graph = _boot != null && _boot.session != null ? _boot.session.Graph : null;
        string region = graph != null ? graph.RegionOfCourse(d.CourseId) : "";
        var r = RegionCatalog.Find(region);
        string place = r != null ? r.DisplayName : d.CourseId;
        return $"{place}  ·  {d.SpotM / 1000f:0.0} km";
    }

    private void Track(RacerDef d)
    {
        _trackedId = _trackedId == d.Id ? "" : d.Id;
        PlayerPrefs.SetString(TrackedKey, _trackedId);
        PlayerPrefs.Save();
        _riders.Refresh();
        if (string.IsNullOrEmpty(_trackedId)) { _ui.Toast("Tracking off", HudKit.ChalkSoft); return; }
        var session = _boot.session;
        if (session != null && session.courseId == d.CourseId)
        {
            float ahead = d.SpotM - session.DistanceM;
            if (ahead < 0f && session.Course != null && session.Course.Closed) ahead += session.Course.Length;
            _ui.Toast($"Tracking {d.Name}: {(ahead >= 0f ? $"{ahead / 1000f:0.0} km ahead" : $"{-ahead / 1000f:0.0} km behind")}  (minimap)",
                      RaceMath.TypeColor(d.Type), 3f);
        }
        else _ui.Toast($"Tracking {d.Name} in {Where(d)}. Travel there from the World Map [TAB].",
                       RaceMath.TypeColor(d.Type), 3.5f);
    }

    private void UpdateTracking(RideSession session)
    {
        RouteMapHud.TrackedWorld = null;
        if (string.IsNullOrEmpty(_trackedId)) return;
        var d = RaceRoster.Find(_trackedId);
        if (d == null || d.CourseId != session.courseId) return;
        var c = session.Course;
        var live = FindRacer(d.Id);
        RouteMapHud.TrackedWorld = live != null && live.AmbientRiding && live.gameObject.activeInHierarchy
            ? live.transform.position : c.PositionAt(d.SpotM);
        RouteMapHud.TrackedColor = RaceMath.TypeColor(d.Type);
    }

    // ------------------------------------------------------------------ the race flow

    private IEnumerator RaceFlow(RaceNpc npc)
    {
        var session = _boot.session;
        _npc = npc;
        _def = npc.def;
        CurrentPhase = Phase.Intro;
        _prompt.Hide();
        _badge.SetVisible(false);
        RideInputGate.Lock("race");
        yield return _ui.Fade(1f, 0.35f);

        SetUpRace(session);
        yield return new WaitForSecondsRealtime(0.55f);   // camera settles behind the black

        _intro.ShowIntro(_def);
        float t = 0f;
        StartCoroutine(_ui.Fade(0f, 0.35f));
        while (t < 2.3f) { t += Time.unscaledDeltaTime; _intro.AnimateIntro(t); yield return null; }

        CurrentPhase = Phase.Countdown;
        foreach (var n in new[] { "3", "2", "1" })
        {
            t = 0f;
            while (t < 1f) { t += Time.unscaledDeltaTime; _intro.ShowCount(n, HudKit.Chalk, t); yield return null; }
        }
        // GO
        RideInputGate.Unlock("race");
        _raceTime = 0f;
        _hud.SetVisible(true);
        CurrentPhase = Phase.Racing;
        t = 0f;
        var goCol = RaceMath.TypeColor(_def.Type);
        while (t < 0.8f && CurrentPhase == Phase.Racing) { t += Time.unscaledDeltaTime; _intro.ShowCount("GO!", goCol, t); yield return null; }
        _intro.Hide();

        while (CurrentPhase == Phase.Racing) yield return null;

        // results
        RideInputGate.Lock("race results");
        _hud.SetVisible(false);
        var result = Settle();
        LastResult = result;
        _results.Show(result);
        _continue = false;
        while (!_continue) yield return null;

        CurrentPhase = Phase.Outro;
        yield return _ui.Fade(1f, 0.35f);
        TearDown(session);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return _ui.Fade(0f, 0.35f);
        RideInputGate.Unlock();
        CurrentPhase = Phase.Idle;
        _prompt.Refresh();
    }

    private void SetUpRace(RideSession session)
    {
        var course = session.Course;
        _length = _def.LengthM;
        _startM = _def.SpotM + startOffsetM;
        if (!course.Closed) _startM = Mathf.Min(_startM, course.Length - _length - 60f);

        foreach (var m in _markers.Values) m.Hide();
        if (_boot.hud != null) _boot.hud.SetRideWidgetsVisible(false);
        _props = RaceCourseProps.Build(course, _startM, _startM + _length, RoadHalfWidth(session.courseId),
                                       RaceMath.TypeColor(_def.Type), _npc.transform.parent);

        _kuro = new RaceRider(RiderProgress.KuroStats) { lane = Lines[0] };
        _rival = new RaceRider(RaceMath.NpcStats(_def.Level, _def.Type, _def.ClimbSpecialist)) { lane = Lines[1] };
        _rivalLaneTarget = Lines[1];
        _kuroLine = 0;
        _ai = new RaceRivalAi(_def.Type, _length);
        _raceTime = 0f;
        _endAt = -1f;
        _forfeit = _perfectDone = _finishToastK = _finishToastR = false;
        _wobbleTarget = 1f;
        _wobbleTimer = 0f;

        if (_def.Type == RaceType.TimeTrial)
        {
            _ghost = MakeGhost(_npc);
            _rivalT = _ghost.transform;
            _npc.engage = 0f;
            // the ghost IS the rider for this one; the real one would sit on top of it at the line
            _npc.gameObject.SetActive(false);
        }
        else
        {
            _npc.SetRiding();
            _rivalT = _npc.transform;
        }

        session.BeginExternalControl();
        session.DriveExternally(_startM, 0f);
        if (_boot.follower != null) { _boot.follower.LaneOverride = Lines[0]; _boot.follower.SnapLane(); }
        PlaceRival(course);

        var kuro = KuroT;
        _kuroRig = kuro != null ? kuro.GetComponentInChildren<KuroBikeRig>() : null;
        _kuroPose = kuro != null ? kuro.GetComponentInChildren<KuroRidePose>() : null;
        if (_kuroRig != null) { _savedCadenceCrank = _kuroRig.useRideCadenceForCrank; _kuroRig.useRideCadenceForCrank = false; }
        var cam = Camera.main != null ? Camera.main.GetComponent<KuroFollowCamera>() : null;
        if (cam != null && cam.target != null) SnapCamera(cam);
        _hud.Setup(_def, _boot.devices != null && _boot.devices.ExternalTrainerActive);
    }

    private static void SnapCamera(KuroFollowCamera follow)
    {
        var t = follow.target;
        var cam = follow.transform;
        cam.position = t.position + t.TransformDirection(follow.offset);
        cam.LookAt(t.position + Vector3.up * 1.1f);
    }

    private void TearDown(RideSession session)
    {
        if (_props != null) Destroy(_props);
        if (_ghost != null) Destroy(_ghost);
        foreach (var m in _ghostMats) if (m != null) Destroy(m);
        _ghostMats.Clear();
        _ghost = null;
        session.EndExternalControl();
        if (_npc != null)
        {
            if (!_npc.gameObject.activeSelf) _npc.gameObject.SetActive(true);
            // back on the road just ahead of where Kuro stopped, riding off (no instant re-offer)
            if (_npc.rideWhileIdle)
                _npc.StartAmbientRide(session.DistanceM + rideOffLeadM, _npc.cruiseMps * _npc.rideOffSpeedFactor,
                                      RaceNpc.RideState.RideOff);
            else _npc.Park();
        }
        if (_boot.follower != null) { _boot.follower.LaneOverride = float.NaN; }
        if (_kuroRig != null) _kuroRig.useRideCadenceForCrank = _savedCadenceCrank;
        if (_kuroPose != null) _kuroPose.SprintOverride = float.NaN;
        if (_boot.devices != null) _boot.devices.ResetEffort();
        if (_boot.hud != null) _boot.hud.SetRideWidgetsVisible(true);
        _results.Hide();
        _intro.Hide();
        if (_pendingCoins > 0) { PlayerWardrobe.AddCoins(_pendingCoins); _pendingCoins = 0; }
        _badge.SetVisible(true);
        _badge.Refresh();
        var cam = Camera.main != null ? Camera.main.GetComponent<KuroFollowCamera>() : null;
        if (cam != null && cam.target != null) SnapCamera(cam);
    }

    // ------------------------------------------------------------------ racing

    private bool PedalInput(bool trainer, float watts, float ftp)
    {
        if (!float.IsNaN(PedalOverride)) return PedalOverride >= 0.5f;
        return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || (trainer && watts >= ftp * trainerPedalFtp);
    }

    private bool SprintInput(bool trainer, float watts, float ftp)
    {
        if (!float.IsNaN(SprintOverride)) return SprintOverride >= 0.5f;
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || (trainer && watts >= ftp * trainerSprintFtp);
    }

    private void TickRace(RideSession session, float dt)
    {
        var course = session.Course;
        if (dt <= 0f || course == null) return;
        _raceTime += dt;

        if (Input.GetKeyDown(forfeitKey)) { Forfeit(); return; }

        // --- Kuro's input: keyboard, and the trainer's legs when one is connected
        var dev = _boot.devices;
        bool trainer = dev != null && dev.ExternalTrainerActive;
        float watts = 0f, ftp = dev != null ? dev.ftpWatts : 220f;
        if (trainer) { dev.Tick(dt, course.GradeAt(session.DistanceM, 8f)); watts = dev.Telemetry.Watts; }
        bool pedal = !_kuro.Finished && PedalInput(trainer, watts, ftp);
        bool sprint = pedal && SprintInput(trainer, watts, ftp);

        if (!_perfectDone)
        {
            bool pressed = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow) ||
                           (!float.IsNaN(PedalOverride) && PedalOverride >= 0.5f) || (trainer && pedal);
            if (pedal || _raceTime > RaceMath.PerfectStartWindowS)
            {
                _perfectDone = true;
                if (pressed && _raceTime <= RaceMath.PerfectStartWindowS)
                {
                    _kuro.v += RaceMath.PerfectStartBonusMps;
                    _ui.Toast("Perfect start!", RaceMath.Hex(0x3ddc74), 1.6f);
                }
            }
            if (_def.Type == RaceType.Sprint && _raceTime <= dt * 1.5f)
            {
                // rolling start: both riders are already moving at GO
                _kuro.v = Mathf.Max(_kuro.v, _kuro.stats.Cruise * RaceMath.RollingStartFraction);
                _rival.v = Mathf.Max(_rival.v, _rival.stats.Cruise * RaceMath.RollingStartFraction);
            }
        }

        if (Input.GetKeyDown(KeyCode.A)) _kuroLine = Mathf.Max(0, _kuroLine - 1);
        if (Input.GetKeyDown(KeyCode.D)) _kuroLine = Mathf.Min(Lines.Length - 1, _kuroLine + 1);
        if (_boot.follower != null) _boot.follower.LaneOverride = Lines[_kuroLine];
        _kuro.lane = _boot.follower != null ? _boot.follower.ActiveLaneOffset : Lines[_kuroLine];

        // --- rival wobble (+/-2 %, eased) and line
        _wobbleTimer -= dt;
        if (_wobbleTimer <= 0f) { _wobbleTimer = Random.Range(0.9f, 1.8f); _wobbleTarget = 1f + Random.Range(-0.02f, 0.02f); }
        _rival.wobble = Mathf.MoveTowards(_rival.wobble, _wobbleTarget, dt * 0.03f);
        float gapKR = _kuro.x - _rival.x;
        bool sameLine = Mathf.Abs(_kuro.lane - _rival.lane) <= RaceMath.SameLineM;
        if (sameLine && gapKR > 0f && gapKR < 5f && _rival.v > _kuro.v + 0.2f)
            _rivalLaneTarget = _kuroLine >= 1 ? Lines[_kuroLine - 1] : Lines[1];   // go around
        _rival.lane = Mathf.MoveTowards(_rival.lane, _rivalLaneTarget, 1.9f * dt);

        // --- step both riders
        bool tt = _def.Type == RaceType.TimeTrial;
        bool draftK = RaceMath.InDraft(_rival.x - _kuro.x, _kuro.lane, _rival.lane, _def.Type);
        bool draftR = RaceMath.InDraft(_kuro.x - _rival.x, _rival.lane, _kuro.lane, _def.Type);
        _kuro.Step(dt, pedal, sprint, draftK, course.GradeAt(_startM + _kuro.x, 8f));
        bool rivalRacing = !_rival.Finished;
        bool rivalSprint = rivalRacing && _ai.WantsSprint(_rival, _kuro);
        _rival.Step(dt, rivalRacing, rivalSprint, draftR, course.GradeAt(_startM + _rival.x, 8f));
        // no riding through each other: on the same line, the rider behind is held to the one ahead
        if (!tt && sameLine)
        {
            float gap = _rival.x - _kuro.x;
            if (gap > 0f && gap < 0.9f && _kuro.v > _rival.v) _kuro.v = _rival.v;
            if (gap < 0f && gap > -0.9f && _rival.v > _kuro.v) _rival.v = _kuro.v;
        }
        Advance(_kuro, dt, ref _finishToastK, "FINISH!");
        Advance(_rival, dt, ref _finishToastR, null);

        session.DriveExternally(_startM + _kuro.x, _kuro.v);
        PlaceRival(course);
        if (_kuroPose != null) _kuroPose.SprintOverride = _kuro.sprinting ? 1f : 0f;
        _hud.Tick(_kuro, _rival, _length, _raceTime, _def.Name, tt);

        // --- end: both home, or the straggler gets an estimated time after the grace period
        if (_kuro.Finished && _rival.Finished) { CurrentPhase = Phase.Results; return; }
        if (_kuro.Finished || _rival.Finished)
        {
            if (_endAt < 0f) _endAt = _raceTime + finishGraceS;
            if (_raceTime >= _endAt)
            {
                Estimate(_kuro);
                Estimate(_rival);
                CurrentPhase = Phase.Results;
            }
        }
    }

    /// <summary>Gives the race up ([Esc]): a loss on the record, no reward.</summary>
    public void Forfeit()
    {
        if (CurrentPhase != Phase.Racing) return;
        _forfeit = true;
        CurrentPhase = Phase.Results;
    }

    private void Advance(RaceRider r, float dt, ref bool toasted, string toast)
    {
        float before = r.x;
        r.x += r.v * dt;
        if (!r.Finished && r.x >= _length)
        {
            float over = r.x - _length;
            r.finishTime = _raceTime - over / Mathf.Max(0.1f, r.v);
            if (!toasted && toast != null) { toasted = true; _ui.Toast(toast, RaceMath.TypeColor(_def.Type), 1.5f); }
        }
        if (r.Finished) r.x = Mathf.Min(r.x, _length + 45f);   // roll out past the arch, then stop
        if (r.Finished && r.x >= _length + 45f) r.v = 0f;
    }

    private void Estimate(RaceRider r)
    {
        if (r.Finished) return;
        r.finishTime = _raceTime + Mathf.Max(0f, _length - r.x) / Mathf.Max(3f, r.v);
    }

    private void PlaceRival(RouteCourse course)
    {
        if (_rivalT == null) return;
        float m = _startM + _rival.x;
        Vector3 up = course.UpAt(m);
        _rivalT.SetPositionAndRotation(course.PositionAt(m) + course.SideAt(m) * _rival.lane + up * 0.065f,
                                       Quaternion.LookRotation(course.TangentAt(m), up));
    }

    private RaceResult Settle()
    {
        if (_forfeit)
        {
            Estimate(_rival);
        }
        var r = new RaceResult
        {
            def = _def,
            forfeit = _forfeit,
            kuroTime = _forfeit ? -1f : _kuro.finishTime,
            npcTime = _rival.finishTime,
        };
        r.won = !_forfeit && _kuro.finishTime >= 0f && _kuro.finishTime < _rival.finishTime;
        r.firstWin = RiderProgress.RecordRace(_def.Id, r.won, r.kuroTime);
        int kuroLevel = RiderProgress.Level;
        r.coins = r.won ? RaceMath.WinCoins(_def.Level, _def.Type, r.firstWin) : 0;
        _pendingCoins = r.coins;   // paid when the results close, so the "+N MC" pop plays on the road
        // A forfeit is a loss on the record but earns nothing (else quitting at GO would farm XP).
        r.xp = _forfeit ? 0 : (r.won ? RaceMath.WinXp(_def.Level, kuroLevel) : RaceMath.LossXp(_def.Level));
        r.level = RiderProgress.AddXp(r.xp);
        Debug.Log($"[race] {(_forfeit ? "FORFEIT" : (r.won ? "WIN" : "LOSS"))} vs {_def.Name}: Kuro {RaceMath.FormatTime(r.kuroTime)}, " +
                  $"{_def.Name} {RaceMath.FormatTime(r.npcTime)}, +{r.coins} MC, +{r.xp} XP, Lv {r.level.levelBefore}->{r.level.levelAfter}");
        return r;
    }

    // ------------------------------------------------------------------ Time Trial ghost

    /// <summary>A see-through copy of the racer that rides the Time Trial; the real rider stays
    /// at the kerb and watches.</summary>
    private GameObject MakeGhost(RaceNpc npc)
    {
        var g = Instantiate(npc.gameObject, npc.transform.parent);
        g.name = "~Ghost " + npc.def.Name;
        foreach (var c in g.GetComponentsInChildren<RaceNpc>(true)) Destroy(c);
        foreach (var c in g.GetComponentsInChildren<RiderBlink>(true)) Destroy(c);
        foreach (var c in g.GetComponentsInChildren<NpcGreeting>(true)) Destroy(c);
        foreach (var c in g.GetComponentsInChildren<KuroDismount>(true)) Destroy(c);
        foreach (Transform c in g.GetComponentsInChildren<Transform>(true))
            if (c.name == "~Eyelid") Destroy(c.gameObject);
        foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == "KuroOutline") mb.enabled = false;
        var rig = g.GetComponentInChildren<KuroBikeRig>(true);
        if (rig != null)
        {
            rig.poseHandTargetOverrideL = rig.poseHandTargetOverrideR = null;
            rig.poseElbowPoleOverrideL = rig.poseElbowPoleOverrideR = null;
            rig.solveEveryNFrames = 1;
        }
        var tint = RaceMath.TypeColor(RaceType.TimeTrial);
        var ghostCol = new Color(Mathf.Lerp(tint.r, 1f, 0.35f), Mathf.Lerp(tint.g, 1f, 0.35f), Mathf.Lerp(tint.b, 1f, 0.35f), 0.42f);
        var cache = new Dictionary<Material, Material>();
        foreach (var r in g.GetComponentsInChildren<Renderer>(true))
        {
            var src = r.sharedMaterials;
            var dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == null) continue;
                if (!cache.TryGetValue(src[i], out var m))
                {
                    Texture tex = null;
                    foreach (var p in new[] { "_MainTex", "_BaseColorMap", "_BaseMap" })
                        if (src[i].HasProperty(p) && src[i].GetTexture(p) != null) { tex = src[i].GetTexture(p); break; }
                    m = RaceArt.Unlit("~Ghost", tex, ghostCol, false);
                    cache[src[i]] = m;
                    _ghostMats.Add(m);
                }
                dst[i] = m;
            }
            r.sharedMaterials = dst;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        g.SetActive(true);
        return g;
    }
}

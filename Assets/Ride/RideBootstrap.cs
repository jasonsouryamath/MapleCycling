using UnityEngine;

/// <summary>
/// Wires the ride systems into the Sakura Pass scene and owns the player's ride-plan input.
///
/// Sits on one scene object ("MapleRide Ride") so the whole ride foundation is a single,
/// inspectable, serialized unit. Everything it needs is found by EXACT name and re-used rather
/// than duplicated, so the staging pass converges instead of accumulating.
///
/// Controls (all rebindable fields, all provisional):
///   W / S or up / down   pedal harder / ease off   (cycling is the controller)
///   Space                brake
///   1 - 4                select course
///   [ / ]                fewer / more laps
///   T                    toggle the 30 / 60 minute ride plan
///   Backspace            restart the session (was R; R opens the Riders screen since 2026-09-26)
///   R                    Riders screen (RaceDirector): racers, ranks, garage
///   E                    race the NPC you are stopped beside (RaceDirector)
///   H                    reconnect the BLE heart-rate strap (e.g. Wahoo TICKR)
///   M                    map zoom: whole course / follow
///   F                    hand the bike back to the free-roam controller
///
/// TEMPORARY map-scout override (DeviceManager.mash.scoutHoldMode, default ON): the press-to-
/// stroke method is disabled; HOLD up arrow to accelerate up to 1000 W (fast flythrough) and
/// tap/hold down arrow (or Space) to brake. Clear scoutHoldMode to restore mash-to-ride.
/// </summary>
[DefaultExecutionOrder(-300)]
public class RideBootstrap : MonoBehaviour
{
    public const string RootName = "MapleRide Ride";

    [Header("Systems (wired by the setup pass)")]
    public RideSession session;
    public DeviceManager devices;
    public RouteDirector director;
    public RouteFollower follower;
    public RideHud hud;
    public RegionDirector regions;
    [Tooltip("The Hanakage legendary encounter. Optional - the ride runs fine without it.")]
    public HanakageEncounter encounter;
    public HanakageEncounterHud encounterHud;

    [Header("Scene links")]
    public Transform rider;
    public Camera rideCamera;

    [Header("Ride plan (provisional)")]
    [Tooltip("The two session lengths the T key toggles between, in minutes.")]
    public float shortRideMinutes = 30f;
    public float longRideMinutes = 60f;

    [Header("Keys")]
    public KeyCode restartKey = KeyCode.Backspace;
    public KeyCode zoomKey = KeyCode.M;
    public KeyCode freeRoamKey = KeyCode.F;
    public KeyCode durationKey = KeyCode.T;
    [Tooltip("Rescan / reconnect the BLE heart-rate strap (e.g. Wahoo TICKR) at run time.")]
    public KeyCode reconnectHeartRateKey = KeyCode.H;

    [Header("State")]
    public bool routeFollowing = true;

    [Header("QA / debug")]
    [Tooltip("Map-scout flythrough. When on, HOLD up arrow to accelerate to " +
             "DeviceManager.mash.scoutMaxWatts (~1000 W, up to ~200 kph) so QA can quickly scan " +
             "the map for defects and flaws; down arrow / Space brakes. This is forced on at " +
             "runtime so a scene baked with scoutHoldMode off still flies. Turn OFF for the " +
             "normal ~220 W mash-to-ride.")]
    public bool mapScoutFlythrough = true;

    private MonoBehaviour[] _freeRoamScripts;

    private void Awake()
    {
        Resolve();
        ApplyControlMode();
        // The Sakura Pass scene is baked with mash.scoutHoldMode off, which caps a held up arrow
        // at ~220 W. Re-assert the QA flythrough here so it wins over the serialized scene value.
        if (Application.isPlaying && devices != null && devices.mash != null)
            devices.mash.scoutHoldMode = mapScoutFlythrough;
        // R became the Riders screen (user 2026-09-26). The saved scene still serializes the old
        // R binding for restart, so move it here rather than letting R do both.
        if (restartKey == KeyCode.R) restartKey = KeyCode.Backspace;
    }

    private void Start()
    {
        if (hud != null && hud.Canvas == null) hud.Build();
        if (hud != null && hud.Canvas != null && rideCamera != null)
            hud.Canvas.worldCamera = rideCamera;
    }

    /// <summary>Finds/creates the sibling components. Safe to call repeatedly.</summary>
    public void Resolve()
    {
        if (devices == null) devices = GetComponent<DeviceManager>();
        if (session == null) session = GetComponent<RideSession>();
        if (director == null) director = GetComponent<RouteDirector>();
        if (hud == null) hud = GetComponent<RideHud>();
        if (regions == null) regions = GetComponent<RegionDirector>();
        // A saved scene missing this component (e.g. an editor staging pass that never ran or
        // was never saved) used to leave the World Map's every pin a silent no-op: WorldMapHud
        // already self-heals this way (see RideHud.Build()) and RideStartPad does two lines
        // below - RegionDirector must too, or "no RegionDirector wired" warnings are the ONLY
        // sign the destinations on the world map do nothing when clicked.
        if (regions == null) regions = gameObject.AddComponent<RegionDirector>();
        // Region BGM + pedalling/coasting bike sounds (clips from tools/audio/make_ride_audio.py).
        if (GetComponent<RideAudio>() == null) gameObject.AddComponent<RideAudio>();
        // Effort-to-animation: telemetry drives the rider's acting (KuroEffortActing).
        var ridePose = rider != null ? rider.GetComponentInChildren<KuroRidePose>() : FindAnyObjectByType<KuroRidePose>();
        if (ridePose != null && ridePose.GetComponent<KuroEffortActing>() == null)
            ridePose.gameObject.AddComponent<KuroEffortActing>().devices = devices;
        // Authoritative weather/wind (Assets/Ride/Weather). Calm if no preset loads.
        var weather = GetComponent<WeatherDirector>();
        if (weather == null) weather = gameObject.AddComponent<WeatherDirector>();
        if (weather.rider == null) weather.rider = rider != null ? rider
            : (follower != null ? follower.rider : null);
        // Maple Row cycling shop (Maple City only) + the gear it sells. Play mode only, so an
        // editor staging pass never serializes these into the scene.
        if (Application.isPlaying)
        {
            var shop = GetComponent<MapleRowShop>();
            if (shop == null) shop = gameObject.AddComponent<MapleRowShop>();
            shop.session = session;
            if (hud != null)
            {
                var strip = GetComponent<BikeComputerStrip>();
                if (strip == null) strip = gameObject.AddComponent<BikeComputerStrip>();
                strip.hud = hud;
            }
            var riderT = rider != null ? rider : (follower != null ? follower.rider : null);
            if (riderT != null)
            {
                var kit = riderT.GetComponent<KitAppearance>();
                if (kit == null) kit = riderT.gameObject.AddComponent<KitAppearance>();
                kit.session = session;
                kit.hud = hud;
            }
            // NPC races, rank insignias, the Riders screen and rider blinking (Assets/Ride/Race).
            if (GetComponent<RaceDirector>() == null) gameObject.AddComponent<RaceDirector>();
        }
        if (encounter == null) encounter = GetComponent<HanakageEncounter>();
        if (encounterHud == null) encounterHud = GetComponent<HanakageEncounterHud>();

        // Universal start pad: a clean tarmac launch straight + checkered line at every map's
        // start, so the rider never launches over water or onto seam markings.
        var startPad = GetComponent<RideStartPad>();
        if (startPad == null) startPad = gameObject.AddComponent<RideStartPad>();
        startPad.session = session;

        if (session != null)
        {
            session.devices = devices;
            session.EnsureCourse();
        }
        if (director != null) director.session = session;
        if (hud != null)
        {
            hud.session = session;
            hud.director = director;
            hud.devices = devices;
            if (hud.worldMap != null) hud.worldMap.regions = regions;
        }
        if (regions != null)
        {
            regions.boot = this;
            regions.session = session;
            regions.Resolve();
        }
        if (follower != null)
        {
            follower.session = session;
            if (follower.rider == null) follower.rider = follower.transform;
        }
        if (rideCamera == null) rideCamera = Camera.main;

        if (encounter != null)
        {
            encounter.session = session;
            encounter.devices = devices;
            encounter.Resolve();
        }
        if (encounterHud != null) encounterHud.encounter = encounter;
    }

    private void Update()
    {
        if (session == null) return;
        // The 3-2-1 freeze and the map-selection screen own the keyboard; a course switch or a
        // free-roam toggle mid-countdown would start the ride from somewhere else entirely.
        if (RideInputGate.Locked) return;
        // An NPC race drives the rider itself, and the Riders screen owns the keys while open.
        if (RaceDirector.Busy || RaceDirector.ScreenOpen) return;

        // 1-5 jump ALONG the current route: 1 start, 2 quarter, 3 middle, 4 three-quarters,
        // 5 just before the finish (user request 2026-09-25). They used to select courses across
        // the whole multi-region graph, which teleported the rider to random other maps.
        for (int k = 0; k < 5; k++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + k)) JumpAlongRoute(k / 4f);
        if (Input.GetKeyDown(KeyCode.LeftBracket)) session.AdjustLaps(-1);
        if (Input.GetKeyDown(KeyCode.RightBracket)) session.AdjustLaps(+1);

        if (Input.GetKeyDown(durationKey))
        {
            float target = Mathf.Abs(session.targetDurationMinutes - shortRideMinutes) < 1f
                ? longRideMinutes : shortRideMinutes;
            session.autoLapsFromTarget = true;
            session.SetTargetDuration(target);
        }

        if (Input.GetKeyDown(restartKey)) session.ResetRide();

        if (Input.GetKeyDown(reconnectHeartRateKey) && devices != null)
        {
            devices.RestartHeartRate();
            Debug.Log("[ride] Reconnecting heart-rate strap: " + devices.HeartRateLabel);
        }

        if (Input.GetKeyDown(zoomKey) && hud != null && hud.map != null)
        {
            hud.map.zoomMode = hud.map.zoomMode == RouteMapHud.ZoomMode.WholeCourse
                ? RouteMapHud.ZoomMode.Follow
                : RouteMapHud.ZoomMode.WholeCourse;
            hud.map.Invalidate();
        }

        if (Input.GetKeyDown(freeRoamKey))
        {
            routeFollowing = !routeFollowing;
            ApplyControlMode();
        }
    }

    /// <summary>Seek to a fraction of the current course (1.0 = 150 m short of the finish, so the
    /// jump never trips the finish line).</summary>
    public void JumpAlongRoute(float fraction)
    {
        if (session == null) return;
        session.EnsureCourse();
        var course = session.Course;
        if (course == null) return;
        float len = course.Length;
        float d = fraction <= 0f ? 10f : Mathf.Min(len * fraction, len - 150f);
        session.SeekTo(Mathf.Max(10f, d));
        if (follower != null) follower.Apply();
    }

    public void SelectCourseIndex(int index)
    {
        if (session == null || session.Graph == null) return;
        // Number keys pick among THIS region's courses only. Indexing the whole graph (which now
        // holds every region's courses) teleported the rider to a random other map when pressing
        // 1-5 mid-ride (user playtest 2026-09-25). The World Map is how you change region.
        var graph = session.Graph;
        string region = graph.RegionOfCourse(session.courseId);
        var local = new System.Collections.Generic.List<string>();
        foreach (var c in graph.courses)
            if (graph.RegionOfCourse(c.id) == region) local.Add(c.id);
        if (index < 0 || index >= local.Count) return;
        if (local[index] == session.courseId) return;
        session.autoLapsFromTarget = true;
        session.SelectCourse(local[index]);
        // A course can belong to another region, so the world has to follow the selection.
        if (regions != null) regions.SyncFromSession();
        if (follower != null) follower.Apply();
        if (hud != null && hud.map != null) hud.map.Invalidate();
        if (hud != null && hud.worldMap != null) hud.worldMap.Refresh();
    }

    /// <summary>
    /// Route-following and free roam are mutually exclusive owners of the rider transform.
    /// The original free-roam scripts are switched off rather than deleted, so pressing F
    /// still gives the old keyboard prototype back exactly as it was.
    /// </summary>
    public void ApplyControlMode()
    {
        if (follower != null) follower.enabled = routeFollowing;
        if (rider == null) return;

        if (_freeRoamScripts == null)
        {
            var keyboard = rider.GetComponent<KuroKeyboardController>();
            var safety = rider.GetComponent<KuroRoadSafety>();
            var grounding = rider.GetComponent<KuroRoadGrounding>();
            _freeRoamScripts = new MonoBehaviour[] { keyboard, safety, grounding };
        }
        foreach (var s in _freeRoamScripts)
            if (s != null) s.enabled = !routeFollowing;
    }
}

using UnityEngine;

/// <summary>
/// Ambient NPC cyclist traffic for SHIOSAI COAST.
///
/// DELIBERATELY NOT SAKURA'S PATTERN. Sakura Pass has an eleven-rider FIXED roster
/// (<c>SakuraNpcRoster</c>): every rider is pinned to a hard-coded <c>MeetDistance</c>, all of
/// them ride the oncoming lane, and the player meets them in the same order at the same metre
/// marks on every single ride. That is a rehearsed cast, and it is right for a showcase climb
/// where each rider is a scripted beat. It is wrong for a 5.78 km coastal loop, which should
/// read as a public road other people happen to be riding on.
///
/// So Shiosai gets a POOL + SPAWNER instead:
///
///   * ~30 riders' worth of traffic is sustained continuously inside a relevance window that
///     travels with the player, rather than 11 riders pinned to the whole route.
///   * Every rider's entry point, entry timing, direction, lane jitter and speed is drawn from
///     an RNG at spawn, so there is no schedule to memorise.
///   * Riders come from BOTH directions - some the player overtakes or is overtaken by, some
///     oncoming - with the lane offset taken from the rider's OWN heading, so keep-left traffic
///     convention puts the two streams on opposite sides of the carriageway automatically.
///   * Riders leaving the window are parked (SetActive false) and recycled, so 30 riders' worth
///     of traffic never costs more than 30 skinned bodies and their frustum culling does the
///     rest.
///
/// SEGMENT SPACE, NOT COURSE SPACE. "Shiosai Breeze" is an out-and-back: the baked course walks
/// the 2,892 m <c>shiosai</c> segment out and then straight back down it, so one course arc
/// length does NOT identify one place on the road, and "same direction as the player" is
/// meaningless in course arc. Everything here is therefore expressed on the SEGMENT - the
/// physical road - and the player's position on it is found by projection, which also means this
/// keeps working unchanged if the course is ever re-cut into laps or a point-to-point.
///
/// EVERY number in this component is PROVISIONAL tuning (see MAPLERIDE_PROJECT_CONTEXT.md: all
/// thresholds and rates in the handoff are illustrative). They are serialized so they can be
/// tuned in the inspector without a recompile.
/// </summary>
[DefaultExecutionOrder(120)]   // after RideSession/RouteFollower (50), before KuroBikeRig's LateUpdate
public class ShiosaiTrafficDirector : MonoBehaviour
{
    [Header("Road (pushed by the staging pass)")]
    [Tooltip("Id of the physical road segment in the baked RouteGraph these riders live on.")]
    public string segmentId = "shiosai";

    [Header("Pool (staged riders, in scene order)")]
    [Tooltip("The staged rider roots. Each is parked (inactive) until the director spawns it.")]
    public Transform[] pool = new Transform[0];

    // ---- density -------------------------------------------------------------------------

    [Header("Density (PROVISIONAL)")]
    [Tooltip("How far AHEAD of the player traffic is simulated, in metres. Riders further away " +
             "than this are parked and recycled; this is what keeps ~30 riders affordable.")]
    public float windowAheadM = 420f;

    [Tooltip("How far BEHIND the player traffic is simulated, in metres. Shorter than the " +
             "forward window because the player rarely looks back, but long enough that a " +
             "same-direction rider can sit on their wheel for a while before being recycled.")]
    public float windowBehindM = 340f;

    [Tooltip("No rider is ever spawned closer to the player than this, in metres - a rider " +
             "appearing inside this radius would visibly pop into existence.")]
    public float minSpawnDistanceM = 95f;

    [Tooltip("Seconds a recycled rider waits before re-entering. Randomised per rider, which " +
             "is what makes arrivals feel like traffic instead of a metronome.")]
    public float respawnDelayMinS = 0.15f;
    public float respawnDelayMaxS = 3.0f;

    [Tooltip("Metres of random jitter applied to a rider's entry point, so riders do not all " +
             "appear on the same line at the window edge.")]
    public float entryJitterM = 55f;

    [Tooltip("Chance a recycled rider is assigned the player's own direction of travel. 0.5 " +
             "gives a balanced two-way road.")]
    [Range(0f, 1f)] public float sameDirectionShare = 0.5f;

    // ---- pace ----------------------------------------------------------------------------

    [Header("Zone 2 pace (PROVISIONAL)")]
    [Tooltip("Slowest ambient rider, m/s. 6.5 m/s = 23.4 km/h.")]
    public float zone2SpeedMinMps = 6.5f;

    [Tooltip("Fastest ambient rider, m/s. 8.0 m/s = 28.8 km/h. This is steady endurance pace " +
             "on purpose: these riders are out for a long aerobic ride, NOT racing. Anything " +
             "that races the player is an ENCOUNTER (see HanakageEncounter), not traffic.")]
    public float zone2SpeedMaxMps = 8.0f;

    [Tooltip("How much a climb slows an ambient rider. speed *= 1 - grade * this, clamped. A " +
             "zone 2 rider holds effort, not speed, so they ease off uphill and roll downhill.")]
    public float gradeSpeedResponse = 4.0f;
    public float gradeSpeedMin = 0.55f;
    public float gradeSpeedMax = 1.22f;

    // ---- placement -----------------------------------------------------------------------

    [Header("Placement (PROVISIONAL)")]
    [Tooltip("The SHARED riding line, in metres on the ROAD's own side axis (+ve = the " +
             "segment's right). Every rider uses this regardless of which way they are going - " +
             "this used to be a keep-left offset applied to each rider's OWN left, which put " +
             "oncoming traffic on the far side of the road and meant no two riders ever had to " +
             "acknowledge each other. One line plus a swerve is what reads as cycling. " +
             "Must match RouteFollower.laneOffset for the player to share it.")]
    public float laneOffsetM = TrafficLine.SharedLineM;

    [Tooltip("Random +/- lateral jitter on the shared line, in metres, so a stream of riders " +
             "is not a single painted rail. Kept small: the line is shared now, so jitter eats " +
             "directly into the clearance a passing rider has to find.")]
    public float laneJitterM = TrafficLine.LineJitterM;

    [Tooltip("Lift above the carriageway surface. Matches RouteFollower.heightOffset plus a " +
             "small bias for the road crown.")]
    public float groundOffsetM = 0.03f;

    [Tooltip("Deterministic seed, so a capture run is reproducible. 0 = seed from the clock.")]
    public int seed = 20260913;

    [Tooltip("Metres of player movement in a single frame that counts as a teleport (a course " +
             "seek or a fast travel) rather than riding, and re-seeds the whole pool.")]
    public float teleportThresholdM = 55f;

    [Header("Diagnostics (read-only)")]
    public int activeRiders;
    public int oncomingRiders;
    public int sameWayRiders;
    public float playerRoadDistanceM;
    public int playerRoadDirection = 1;

    // ---- drafting, blocking and overtaking -------------------------------------------------

    [Header("Drafting / blocking / overtaking (PROVISIONAL)")]
    [Tooltip("Metres behind a same-direction rider within which their aerodynamic shadow is " +
             "worth anything at all. ~11 m is about five bike lengths, which is roughly where " +
             "a real wheel-sucker stops feeling any shelter.")]
    public float draftRangeM = 11f;

    [Tooltip("Gap at which the draft reads as PERFECT, in metres - close enough to be on their " +
             "wheel without touching it.")]
    public float draftIdealGapM = 3.0f;

    [Tooltip("Lateral offset, in metres, at which the rider in front stops sheltering you at " +
             "all. Past this you are beside them in clean air, not behind them.")]
    public float draftLateralM = 1.05f;

    [Tooltip("The closest the player may ever get to the rider in front while still in their " +
             "lane, in metres. This is the anti-ghosting gap: measured nose-to-tail between " +
             "rider ROOTS, so it has to clear a bike length (~1.05 m) plus a little air.")]
    public float minFollowGapM = 2.0f;

    [Tooltip("Lateral offset, in metres, inside which the player counts as being BEHIND the " +
             "rider (and is therefore blocked) rather than alongside them (and free to pass).")]
    public float blockLateralM = 0.85f;

    [Tooltip("Metres per second of extra closing speed allowed per metre of gap still to give " +
             "away. Low values feel like a wall; high values let the player nose through.")]
    public float blockApproachGain = 0.9f;

    [Tooltip("Metres of clear road ahead of a rider the player must reach for the pass to count " +
             "as completed.")]
    public float overtakeClearM = 4.5f;

    [Tooltip("Seconds a just-passed rider sits up rather than re-passing the player. Without " +
             "this, an ambient rider who happens to be faster immediately re-takes the place " +
             "and the whole road turns into a yo-yo.")]
    public float yieldSeconds = 14f;

    [Tooltip("Pace scale applied to a yielding rider.")]
    public float yieldPaceScale = 0.84f;

    [Tooltip("Metres a same-direction rider moves towards the centreline to go around the " +
             "player, rather than riding through them.")]
    public float npcPassShiftM = 1.1f;

    [Header("Draft readout (read-only)")]
    [Tooltip("0 = no shelter, 1 = perfectly tucked in. Drives the HUD draft bar.")]
    public float draftFactor;
    public float draftGapM;
    public bool blocked;
    public int overtakesCompleted;
    public string draftRiderName = "";

    [Header("Rider level of detail")]
    [Tooltip("Distance-based renderer / IK / shadow LOD across the pool. See ShiosaiRiderLod.")]
    public bool riderLod = true;

    // Pushed onto every ShiosaiRiderLod at arm time, so the whole pool is tuned from one place
    // rather than from thirty serialized copies of the same numbers. ALL PROVISIONAL.
    [Tooltip("Inside this, the rider gets the full moving bicycle and a per-frame IK solve.")]
    public float lodDetailM = 40f;
    [Tooltip("Inside this, the rider casts a shadow.")]
    public float lodShadowM = 40f;
    [Tooltip("Past this, the rider's IK solve drops to once every twelve frames.")]
    public float lodCoarseM = 65f;
    [Tooltip("Past this, the rider is not drawn at all. The dominant cost on this road is the " +
             "riders' own skinned bodies (~110k triangles each, thirty of them), so this is the " +
             "single most expensive number in the region - and a chibi rider is a few pixels " +
             "tall well before a hundred metres.")]
    public float lodCullM = 110f;

    // ---- runtime state -------------------------------------------------------------------

    private RouteSegmentData _seg;
    private Transform _player;
    private RideSession _session;
    private System.Random _rng;

    private float[] _arc;        // each rider's arc length along the segment, metres
    private float[] _speed;      // each rider's flat-road zone 2 speed, m/s
    private float[] _lane;       // each rider's own-left lane offset, metres
    private int[] _dir;          // +1 = with increasing segment arc, -1 = against
    private float[] _readyAt;    // Time.time a parked rider may re-enter
    private bool[] _live;
    private float[] _pace;       // this frame's actual road speed, m/s
    private float[] _laneBias;   // smoothed lateral shift while going around the player
    private float[] _yieldUntil; // Time.time a just-passed rider stops sitting up
    private bool[] _engaged;     // player is/was tucked in behind this rider
    private bool[] _passing;     // this rider is going around the player
    private ShiosaiRiderLod[] _lods;

    private RouteFollower _follower;
    private Transform _camera;

    private int _playerIndex;
    private float _lastPlayerArc = -1f;
    private bool _ready;

    private void OnEnable()
    {
        _ready = false;
        Resolve();
    }

    private void Resolve()
    {
        var graph = RouteGraph.Load();
        _seg = graph != null ? graph.Segment(segmentId) : null;
        if (_seg == null || _seg.Count < 2)
        {
            Debug.LogWarning($"[shiosai-traffic] no '{segmentId}' segment in the baked route " +
                             "graph - traffic disabled.");
            return;
        }

        _session = FindFirstObjectByType<RideSession>(FindObjectsInactive.Include);
        _player = FindPlayer();

        int n = pool != null ? pool.Length : 0;
        _arc = new float[n]; _speed = new float[n]; _lane = new float[n];
        _dir = new int[n]; _readyAt = new float[n]; _live = new bool[n];
        _pace = new float[n]; _laneBias = new float[n]; _yieldUntil = new float[n];
        _engaged = new bool[n]; _passing = new bool[n];
        _lods = new ShiosaiRiderLod[n];
        for (int i = 0; i < n; i++)
            if (pool[i] != null) _lods[i] = pool[i].GetComponent<ShiosaiRiderLod>();
        foreach (var l in _lods)
        {
            if (l == null) continue;
            l.detailM = lodDetailM; l.shadowM = lodShadowM;
            l.coarseM = lodCoarseM; l.cullM = lodCullM;
            l.Invalidate();
        }
        _rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);

        var boot = FindFirstObjectByType<RideBootstrap>(FindObjectsInactive.Include);
        _follower = boot != null ? boot.follower : FindFirstObjectByType<RouteFollower>(FindObjectsInactive.Include);
        // The pass/overtake input only exists on this coast, and only while traffic is running.
        if (_follower != null) _follower.laneChangeEnabled = true;
        _camera = Camera.main != null ? Camera.main.transform : null;

        _playerIndex = -1;
        _lastPlayerArc = -1f;
        TrackPlayer();

        // Seed the road: scatter the whole pool across the window at once, rather than letting
        // it trickle in from the edges, so the very first frame after fast travel already reads
        // as a road with people on it.
        for (int i = 0; i < n; i++)
        {
            if (pool[i] == null) continue;
            Recycle(i, initialFill: true);
        }
        _ready = true;
        Debug.Log($"[shiosai-traffic] armed: pool {n}, segment '{segmentId}' " +
                  $"{_seg.Length:0} m, window -{windowBehindM:0}/+{windowAheadM:0} m, " +
                  $"pace {zone2SpeedMinMps * 3.6f:0.0}-{zone2SpeedMaxMps * 3.6f:0.0} km/h.");
    }

    private void OnDisable()
    {
        // Leaving the region must not leave riders standing on the coast road.
        if (_follower != null) _follower.laneChangeEnabled = false;
        // ...nor strand the player with a reduced (drafting) drag.
        draftFactor = 0f;
        if (_session != null && _session.physics != null) _session.physics.dragMultiplier = 1f;
        if (pool == null) return;
        foreach (var t in pool)
            if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
    }

    private Transform FindPlayer()
    {
        var boot = FindFirstObjectByType<RideBootstrap>(FindObjectsInactive.Include);
        if (boot != null && boot.follower != null && boot.follower.rider != null)
            return boot.follower.rider;
        var controller = FindFirstObjectByType<KuroKeyboardController>(FindObjectsInactive.Include);
        if (controller != null) return controller.transform;
        return Camera.main != null ? Camera.main.transform : null;
    }

    private void Update()
    {
        if (!_ready) { Resolve(); if (!_ready) return; }
        if (_player == null) _player = FindPlayer();
        if (_player == null || _seg == null) return;

        TrackPlayer();

        // A SEEK or a fast travel moves the player hundreds of metres in one frame. Letting the
        // pool refill from the window edges after that leaves the road empty for the best part
        // of a minute (a rider entering 420 m out at 7 m/s takes ~50 s to reach the camera), so
        // a jump re-seeds the whole pool across the window instead.
        if (_lastPlayerArc >= 0f &&
            Mathf.Abs(playerRoadDistanceM - _lastPlayerArc) > teleportThresholdM)
        {
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null) Recycle(i, initialFill: true);
        }
        _lastPlayerArc = playerRoadDistanceM;

        float dt = Time.deltaTime;
        float now = Time.time;
        activeRiders = oncomingRiders = sameWayRiders = 0;

        // Player's lateral on the SAME axis the riders use: metres on the road's own side axis.
        // RouteFollower places the player at course.SideAt(d) * _lane, which is that same axis,
        // and the value MOVES while the player is steering out to pass - which is exactly the
        // signal this needs, so read the live value rather than the serialized default.
        float playerLane = _follower != null ? _follower.ActiveLaneOffset : laneOffsetM;
        float playerPaceNow = _session != null ? _session.SpeedMps : 8f;
        if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
        Vector3 camPos = _camera != null ? _camera.position : _player.position;

        int target = -1;
        float targetGap = float.MaxValue, targetLat = 0f, targetPace = 0f;

        for (int i = 0; i < pool.Length; i++)
        {
            var t = pool[i];
            if (t == null) continue;

            if (!_live[i])
            {
                if (now >= _readyAt[i]) Recycle(i, initialFill: false);
                continue;
            }

            // --- advance ------------------------------------------------------------------
            float grade = GradeAt(_arc[i], _dir[i]);
            float pace = _speed[i] * Mathf.Clamp(1f - grade * gradeSpeedResponse,
                                                 gradeSpeedMin, gradeSpeedMax);
            // A rider the player has just come past sits up for a while. Without this, any
            // ambient rider who happens to be a little quicker immediately re-takes the place
            // and the pass the player just worked for turns into a yo-yo.
            if (now < _yieldUntil[i]) pace *= yieldPaceScale;
            _pace[i] = pace;
            _arc[i] += pace * _dir[i] * dt;

            // --- relevance ----------------------------------------------------------------
            float rel = _arc[i] - playerRoadDistanceM;
            float ahead = rel * playerRoadDirection;          // +ve = in front of the player
            if (_arc[i] < 0f || _arc[i] > _seg.Length ||
                ahead > windowAheadM || ahead < -windowBehindM)
            {
                Park(i);
                continue;
            }

            bool sameWay = _dir[i] == playerRoadDirection;
            // Lateral gap to the player, on the road's side axis. Signed the player's way round
            // so the draft test below reads naturally; the avoidance rule re-signs it per rider.
            float lat = sameWay ? _lane[i] + _laneBias[i] - playerLane : 999f;

            if (sameWay)
            {
                // --- did the player just complete a pass on this rider? --------------------
                if (_engaged[i] && ahead < -overtakeClearM)
                {
                    _engaged[i] = false;
                    _yieldUntil[i] = now + yieldSeconds;
                    overtakesCompleted++;
                }
                else if (_engaged[i] && ahead > draftRangeM * 1.6f)
                {
                    _engaged[i] = false;      // they simply rode away; no pass happened
                }

                // --- the nearest rider the player is sitting behind ------------------------
                if (ahead > 0f && ahead < draftRangeM && Mathf.Abs(lat - EchelonShift(ahead)) < draftLateralM &&
                    ahead < targetGap)
                {
                    target = i; targetGap = ahead; targetLat = lat; targetPace = pace;
                }
            }

            // --- swerve to pass ------------------------------------------------------------
            // Generalised from "this rider is going around the PLAYER" to "this rider is going
            // around ANYBODY". The old version only ever considered the player, so thirty riders
            // rode through each other all day and it was only invisible because the two fixed
            // lanes kept the same-direction ones in single file.
            float wantBias = ResolveAvoidance(i, playerLane, playerPaceNow);
            _passing[i] = Mathf.Abs(wantBias) > 0.01f;
            _laneBias[i] = TrafficLine.Approach(_laneBias[i], wantBias, dt);

            Place(t, _arc[i], _dir[i], _lane[i] + _laneBias[i]);
            activeRiders++;
            if (sameWay) sameWayRiders++; else oncomingRiders++;

            if (riderLod && _lods[i] != null)
                _lods[i].Apply(Vector3.Distance(camPos, t.position));
        }

        ResolveDraft(target, targetGap, targetLat, targetPace, dt);
    }

    /// <summary>
    /// The lateral step-out rider <paramref name="i"/> wants this frame, in metres on the road's
    /// side axis, considering every other live rider AND the player.
    ///
    /// Everything is converted into the subject's OWN frame first (metres in front of me, metres
    /// to my right) so <see cref="TrafficLine.DesiredShift"/> can be the single shared rule for
    /// both regions, then converted back. The conversion is just multiplying by the subject's
    /// direction: for a rider travelling against the segment, "my right" is the road's left and
    /// "in front of me" is backwards along the arc. Getting that sign wrong is the difference
    /// between two oncoming riders opening a gap and two oncoming riders steering into each
    /// other, which is why it happens in exactly one place.
    /// </summary>
    private float ResolveAvoidance(int i, float playerLateral, float playerPace)
    {
        int d = _dir[i];
        float myLat = _lane[i] + _laneBias[i];
        bool out0 = _passing[i];
        float want = 0f;

        for (int j = 0; j < pool.Length; j++)
        {
            if (j == i || pool[j] == null || !_live[j]) continue;

            float along = (_arc[j] - _arc[i]) * d;
            float lat = (_lane[j] + _laneBias[j] - myLat) * d;
            bool oncoming = _dir[j] != d;
            float s = TrafficLine.DesiredShift(along, lat, oncoming, _pace[i], _pace[j], out0);
            if (Mathf.Abs(s) > Mathf.Abs(want)) want = s;
        }

        // The player. Never steered from here - they have their own lane change on
        // RouteFollower and moving them would fight the input - so they are purely an obstacle.
        // That asymmetry is what makes an oncoming rider reliably give way to a player who is
        // sitting in the middle of the road, rather than both of them dithering.
        {
            float along = (playerRoadDistanceM - _arc[i]) * d;
            float lat = (playerLateral - myLat) * d;
            bool oncoming = playerRoadDirection != d;
            float s = TrafficLine.DesiredShift(along, lat, oncoming, _pace[i], playerPace, out0);
            if (Mathf.Abs(s) > Mathf.Abs(want)) want = s;
        }

        return want * d;   // back onto the road's side axis
    }

    /// <summary>
    /// Turns "the nearest same-direction rider in front of me" into the three things the player
    /// actually feels: a draft bar, a wall they cannot ride through, and a pass they can make.
    ///
    /// Deliberately NOT rigid-body physics. Road cycling at close quarters is not a collision
    /// problem, it is a pacing problem: you do not bounce off the wheel in front, you simply
    /// cannot go past it while you are behind it. So the block is a one-frame SPEED CAP handed
    /// to RideSession (see RideSession.SpeedCapMps), scaled by the gap still left to give away,
    /// which converges smoothly onto the minimum following gap with no jitter and no rubber-band
    /// - and is released the instant the player steers out of the rider's line.
    /// </summary>
    [Tooltip("CdA saving at a perfect draft (real peloton data: ~30-40 % on the wheel).")]
    [Range(0f, 0.5f)] public float draftDragSaving = 0.35f;

    /// <summary>Lateral offset (player's side axis, + = leader to the player's right) of the
    /// wake pocket at a given gap: gap x tan(apparent wind angle), clamped to 1.2 m. A wind from
    /// the right pushes the wake left, so the wheel to follow sits to the player's right.</summary>
    private static float EchelonShift(float gap)
    {
        var wd = WeatherDirector.Instance;
        if (wd == null) return 0f;
        float ang = Mathf.Clamp(wd.Apparent.AngleDeg, -60f, 60f) * Mathf.Deg2Rad;
        return Mathf.Clamp(gap * Mathf.Tan(ang), -1.2f, 1.2f);
    }

    /// <summary>Drafting now changes PHYSICS, not just the HUD bar: CdA x (1 - saving x draft).</summary>
    private void ApplyDraftDrag()
    {
        if (_session != null && _session.physics != null)
            _session.physics.dragMultiplier = 1f - draftDragSaving * Mathf.Clamp01(draftFactor);
    }

    private void ResolveDraft(int i, float gap, float lat, float pace, float dt)
    {
        if (i < 0)
        {
            draftFactor = Mathf.MoveTowards(draftFactor, 0f, 2.5f * dt);
            draftGapM = 0f; blocked = false; draftRiderName = "";
            ApplyDraftDrag();
            return;
        }

        _engaged[i] = true;
        draftGapM = gap;
        draftRiderName = pool[i] != null ? pool[i].name : "";

        // Quality is BOTH axes: sitting 2 m back but half out of the lane is not a draft, and
        // being perfectly on line 10 m back is barely one either.
        float gapQ = Mathf.Clamp01((draftRangeM - gap) / Mathf.Max(0.01f, draftRangeM - draftIdealGapM));
        // The sheltered pocket trails DOWNWIND of the APPARENT wind (crosswind echelon), not
        // straight behind the wheel.
        float latQ = Mathf.Clamp01(1f - Mathf.Abs(lat - EchelonShift(gap)) / Mathf.Max(0.01f, draftLateralM));
        float want = gapQ * latQ;
        // Smoothed so the bar does not flicker while the rider in front breathes on the lane.
        draftFactor = Mathf.MoveTowards(draftFactor, want, 3f * dt);
        ApplyDraftDrag();

        // ---- blocking ---------------------------------------------------------------------
        if (Mathf.Abs(lat) < blockLateralM && _session != null)
        {
            // NOT clamped at zero spare on purpose. If a transient (a crest, a frame spike, the
            // one-frame lag between this measurement and RideSession consuming the cap) does let
            // the player nose inside the minimum gap, a cap of exactly the rider's own pace
            // would LOCK them in there. Letting the cap fall below their pace makes the gap
            // recover on its own, which is what a following rider actually does.
            float cap = Mathf.Max(0f, pace + (gap - minFollowGapM) * blockApproachGain);
            _session.SpeedCapMps = cap;
            blocked = gap - minFollowGapM < 0.45f;
        }
        else blocked = false;
    }

    // ---- pool management ------------------------------------------------------------------

    private void Park(int i)
    {
        _live[i] = false;
        _engaged[i] = false; _passing[i] = false; _laneBias[i] = 0f;
        if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        _readyAt[i] = Time.time + Lerp(respawnDelayMinS, respawnDelayMaxS);
    }

    /// <summary>
    /// Gives a parked rider a fresh direction, pace, lane and entry point.
    ///
    /// The entry EDGE is chosen from the rider's speed RELATIVE to the player, not from a fixed
    /// rule: a rider who will close on the player from behind has to enter at the back of the
    /// window, one the player will catch has to enter at the front. Picking the wrong edge makes
    /// a rider drift straight back out again and the road goes quiet.
    /// </summary>
    private void Recycle(int i, bool initialFill)
    {
        _dir[i] = (float)_rng.NextDouble() < sameDirectionShare
                  ? playerRoadDirection : -playerRoadDirection;
        _speed[i] = Lerp(zone2SpeedMinMps, zone2SpeedMaxMps);
        _lane[i] = laneOffsetM + ((float)_rng.NextDouble() - 0.5f) * 2f * laneJitterM;

        float arc;
        if (initialFill)
        {
            // Uniform over the whole window. The pop-in radius does NOT apply here: an initial
            // fill happens at arrival or after a seek, when the whole world has just changed, so
            // there is no continuity for a nearby rider to break - and holding every rider 95 m
            // away would leave the road looking empty for the first half-minute of a ride.
            float ahead = Lerp(-windowBehindM, windowAheadM);
            const float SeedClearanceM = 14f;
            if (Mathf.Abs(ahead) < SeedClearanceM)
                ahead = SeedClearanceM * (ahead < 0f ? -1f : 1f);
            arc = playerRoadDistanceM + ahead * playerRoadDirection;
        }
        else
        {
            float playerPace = _session != null ? _session.SpeedMps : 8f;
            // Closing speed along the player's own direction of travel.
            float riderAlong = _speed[i] * (_dir[i] == playerRoadDirection ? 1f : -1f);
            bool fromBehind = riderAlong > playerPace;
            float ahead = fromBehind
                ? -windowBehindM + (float)_rng.NextDouble() * entryJitterM
                : windowAheadM - (float)_rng.NextDouble() * entryJitterM;
            arc = playerRoadDistanceM + ahead * playerRoadDirection;
        }

        // The road is not a loop and does not wrap: it runs harbour -> north headland. Near
        // either end the window is truncated, so rather than popping a rider in at 20 m, wait.
        // Traffic legitimately thins at the ends of the road.
        arc = Mathf.Clamp(arc, 0f, _seg.Length);
        if (!initialFill && Mathf.Abs(arc - playerRoadDistanceM) < minSpawnDistanceM)
        {
            _live[i] = false;
            if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
            _readyAt[i] = Time.time + Lerp(respawnDelayMinS, respawnDelayMaxS);
            return;
        }

        _arc[i] = arc;
        _live[i] = true;
        _laneBias[i] = 0f; _engaged[i] = false; _passing[i] = false; _yieldUntil[i] = 0f;
        Place(pool[i], arc, _dir[i], _lane[i]);
        if (!pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(true);

        // Attach the correct LOD state SYNCHRONOUSLY, in the same call that (re)activates the
        // rider, rather than leaving it to next frame's Update() pass. A rider that respawns
        // inside the visible range (minSpawnDistanceM can be closer than lodCullM) otherwise
        // renders for one full frame with whatever renderer-enabled flags were cached from
        // BEFORE it was parked - normally "everything off" because a rider is culled long
        // before it exits the window, but that is an assumption about tuning, not a guarantee,
        // and this is cheap enough to just make it a guarantee instead. This closes the one
        // confirmed gap between "body active" and "bike attached" behind the Minato Coast
        // "rider with no bike" defect (QA finding #1).
        if (riderLod && _lods[i] != null)
        {
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            Vector3 camPos = _camera != null ? _camera.position
                            : (_player != null ? _player.position : pool[i].position);
            _lods[i].Invalidate();
            _lods[i].Apply(Vector3.Distance(camPos, pool[i].position));
        }
    }

    private float Lerp(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

    /// <summary>
    /// Puts one same-direction rider on the road a known distance in front of the player, on the
    /// player's own line, at a known pace. Exists so the capture harness can PROVE the drafting,
    /// blocking and overtaking behaviour on demand instead of waiting for the ambient RNG to
    /// happen to produce the situation - which is the difference between a repeatable verified
    /// capture and a lucky screenshot. Returns the pool index, or -1.
    /// </summary>
    public int SpawnPacerAhead(float gapM, float paceMps)
    {
        if (!_ready || pool == null) return -1;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null) continue;
            _dir[i] = playerRoadDirection;
            _speed[i] = paceMps;
            _lane[i] = laneOffsetM;
            _laneBias[i] = 0f; _engaged[i] = false; _passing[i] = false; _yieldUntil[i] = 0f;
            _arc[i] = Mathf.Clamp(playerRoadDistanceM + gapM * playerRoadDirection,
                                  0f, _seg.Length);
            _live[i] = true;
            Place(pool[i], _arc[i], _dir[i], _lane[i]);
            if (!pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(true);
            if (_lods[i] != null) { _lods[i].Invalidate(); _lods[i].Apply(0f); }
            return i;
        }
        return -1;
    }

    /// <summary>Metres the given rider is in front of the player (negative = behind).</summary>
    public float AheadOfPlayerM(int index)
    {
        if (index < 0 || _arc == null || index >= _arc.Length) return 0f;
        return (_arc[index] - playerRoadDistanceM) * playerRoadDirection;
    }

    // ---- geometry -------------------------------------------------------------------------

    /// <summary>
    /// Seats a rider on the road.
    ///
    /// <paramref name="lateral"/> is metres on the ROAD's own side axis, NOT the rider's own
    /// left. That is the whole single-line change in one line of code: the old form was
    /// <c>side * (-dir * lane)</c>, which is why two riders travelling opposite ways used to
    /// land on opposite sides of the carriageway and never interact. Now everybody is placed
    /// against the same axis, so the same nominal value puts them on the same line and any
    /// separation between them has to be EARNED by the avoidance rule.
    /// </summary>
    private void Place(Transform t, float arc, int dir, float lateral)
    {
        Frame(arc, out int a, out int b, out float f);
        Vector3 p = Vector3.Lerp(_seg.position[a], _seg.position[b], f);
        Vector3 tan = Vector3.Slerp(_seg.tangent[a], _seg.tangent[b], f).normalized * dir;
        Vector3 side = Vector3.Slerp(_seg.side[a], _seg.side[b], f).normalized;
        Vector3 up = Vector3.Slerp(_seg.up[a], _seg.up[b], f).normalized;

        t.position = p + side * lateral + up * groundOffsetM;
        if (tan.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(tan, up);
    }

    private void Frame(float metres, out int i, out int j, out float t)
    {
        metres = Mathf.Clamp(metres, 0f, _seg.Length);
        i = _seg.IndexAt(metres);
        j = Mathf.Min(i + 1, _seg.Count - 1);
        float span = _seg.distance[j] - _seg.distance[i];
        t = span > 1e-5f ? (metres - _seg.distance[i]) / span : 0f;
    }

    /// <summary>Rise over run in the rider's own direction of travel.</summary>
    private float GradeAt(float arc, int dir)
    {
        const float Window = 12f;
        float a = Mathf.Clamp(arc - Window * 0.5f, 0f, _seg.Length);
        float b = Mathf.Clamp(arc + Window * 0.5f, 0f, _seg.Length);
        Frame(a, out int i0, out int j0, out float f0);
        Frame(b, out int i1, out int j1, out float f1);
        Vector3 pa = Vector3.Lerp(_seg.position[i0], _seg.position[j0], f0);
        Vector3 pb = Vector3.Lerp(_seg.position[i1], _seg.position[j1], f1);
        float run = new Vector2(pb.x - pa.x, pb.z - pa.z).magnitude;
        if (run < 0.05f) return 0f;
        return (pb.y - pa.y) / run * dir;
    }

    /// <summary>
    /// Finds where on the physical road the player is, and which way they are pointing, by
    /// PROJECTION rather than by reading the course arc.
    ///
    /// The course is an out-and-back, so course arc 800 m and course arc 4,984 m are the same
    /// stretch of tarmac ridden in opposite directions; only projection can tell the traffic
    /// which way "the player's direction" currently points. Searches locally around the last
    /// known index, falling back to a full scan the first time (or after a fast travel).
    /// </summary>
    private void TrackPlayer()
    {
        if (_player == null || _seg == null || _seg.Count < 2) return;
        Vector3 p = _player.position;

        int best = -1;
        float bestSqr = float.MaxValue;
        int lo = 0, hi = _seg.Count - 1;
        if (_playerIndex >= 0)
        {
            lo = Mathf.Max(0, _playerIndex - 48);
            hi = Mathf.Min(_seg.Count - 1, _playerIndex + 48);
        }
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = lo; i <= hi; i++)
            {
                var d = _seg.position[i] - p;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestSqr) { bestSqr = sq; best = i; }
            }
            // A local search that lands on its own edge has lost the player (fast travel, a
            // seek, or free roam). Re-scan the whole road once rather than tracking a ghost.
            if (best > lo && best < hi) break;
            if (lo == 0 && hi == _seg.Count - 1) break;
            lo = 0; hi = _seg.Count - 1; bestSqr = float.MaxValue; best = -1;
        }
        if (best < 0) return;

        _playerIndex = best;
        playerRoadDistanceM = _seg.distance[best];
        var tan = _seg.tangent[best];
        var fwd = _player.forward;
        playerRoadDirection = (tan.x * fwd.x + tan.z * fwd.z) >= 0f ? 1 : -1;
    }
}

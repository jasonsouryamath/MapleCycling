using UnityEngine;

/// <summary>
/// The scene side of one racer (<see cref="RacerDef"/>): an EXISTING rider taken off their ride
/// loop and given a challenger's ride of their own.
///
/// RIDING (default, user direction 2026-09-26: "NPCs should not be stationary when asking to race
/// or challenge - they should be mobile and moving on their bikes"): out of a race the racer
/// keeps riding the course spline (<see cref="TickAmbient"/>), patrolling the stretch that leads
/// into their start line (<see cref="RacerDef.SpotM"/>). When Kuro rides up from behind they ease
/// off, move one line over (never through Kuro) and pace him abeam while the "E  Race" prompt is
/// up; ignored for <see cref="offerSeconds"/>, or after a race, they ride off ahead and do not
/// re-offer for <see cref="rideOffSeconds"/>. If Kuro BRAKES while they offer (below
/// <see cref="holdBelowMps"/>) they ease to a stop one bike-length ahead of him, abeam, still
/// seated, cranks still, outer foot down on the road and the bike tipped onto it
/// (<see cref="Holding"/>); the offer clock pauses and the prompt stays up; when he rides on,
/// they push off and pace him again. They wave (the hand on Kuro's side; the other stays on the
/// bars) when they start pacing him and when they stop beside him. Out of a race they also hold
/// behind traffic in their own line (<see cref="ClearanceCap"/>). Pedalling is the rig's own crank-from-distance, so
/// the legs and wheels turn because the rider really travels. The look-at still runs (head and
/// neck only while riding).
///
/// The legacy PARKED mode below (<see cref="rideWhileIdle"/> = false) is kept intact:
///
/// Standing uses the dismount overlay Kuro already has for the Maple Row stores
/// (<see cref="KuroDismount"/>; NPC riders share Kuro's KuroBikeRig solver). On top of that this
/// component layers the idle acting:
///   * a stance: arms crossed, hands on hips, or relaxed (one hand on the bars)
///   * breathing (chest pitch + shoulder rise) and a slow weight shift
///   * look-at: chest, neck and head yaw toward Kuro inside ~14 m (world-axis, so it does not
///     depend on each bone's local axis convention), and one wave when Kuro rides up
/// During a race <see cref="SetRiding"/> puts them back in the saddle and the director drives the
/// transform; the rig's own pedalling (crank from travelled distance) moves the legs.
/// </summary>
// 150: Update runs after KuroDismount.Update (0) so our hand targets win, and LateUpdate runs
// after the rig's solve (KuroBikeRig, 100) so the look-at and breathing are layered on top of it.
[DefaultExecutionOrder(150)]
public sealed class RaceNpc : MonoBehaviour
{
    public RacerDef def;
    public KuroBikeRig rig;
    public KuroDismount dismount;
    [Tooltip("Kerb side the bike is parked on: -1 = left of the road (Japan keeps left).")]
    public int kerbSide = -1;

    [Header("Acting (PROVISIONAL)")]
    public float lookRadiusM = 14f;
    public float maxLookYawDeg = 75f;
    public float breathHz = 0.26f;
    public float breathDeg = 1.6f;
    public float waveSeconds = 1.9f;
    public float waveRearmSeconds = 25f;
    [Tooltip("Metres the standing hips are raised over the shared dismount pose, which leaves the " +
             "chibi rig in a slight squat (the same on Kuro at the Maple Row stores).")]
    public float standLift = 0.09f;

    public enum RideState { Cruise, Pace, RideOff }

    [Header("Riding while idle (PROVISIONAL tuning, illustrative values)")]
    [Tooltip("Off = the legacy behaviour: parked at the kerb, standing beside the bike.")]
    public bool rideWhileIdle = true;
    [Tooltip("Relaxed patrol speed, m/s (5.5 = ~20 km/h). Varied +/-8 % per rider.")]
    public float cruiseMps = 5.5f;
    [Tooltip("Soft-pedal speed once Kuro is noticed behind, so he can catch up.")]
    public float easeOffMps = 4.3f;
    [Tooltip("Never slower than this while riding: the challenger is always rolling.")]
    public float minRollMps = 3.0f;
    public float maxPaceMps = 14f;
    public float accelMps2 = 2.0f;
    [Tooltip("Acceleration while pacing Kuro (pulling away with him after a stop, or chasing back up).")]
    public float paceAccelMps2 = 3.0f;
    public float brakeMps2 = 2.6f;
    [Tooltip("Metres behind the rider inside which Kuro is noticed.")]
    public float awareBehindM = 60f;
    [Tooltip("Longitudinal window (m) around the pacing slot in which the rider paces Kuro.")]
    public float paceWindowM = 12f;
    [Tooltip("Pacing slot while greeting: this many metres behind Kuro, abeam, so Kuro is inside the greeting's view cone.")]
    public float paceBehindM = 0.8f;
    [Tooltip("Pacing slot after the greeting: this many metres AHEAD of Kuro, so the chase camera keeps the challenger in view.")]
    public float paceAheadM = 3.5f;
    [Tooltip("Seconds in the greeting slot before moving up to the ahead slot.")]
    public float paceGreetSeconds = 3f;
    [Tooltip("How fast the pacing slot moves between the two (m/s relative to Kuro).")]
    public float paceSlotMps = 0.7f;
    [Tooltip("Pacing slot: this many metres to Kuro's road-centre side.")]
    public float paceLateralM = 1.4f;
    public float paceGain = 0.6f;
    [Tooltip("Seconds of pacing without an answer before the rider rides off.")]
    public float offerSeconds = 20f;
    [Tooltip("Seconds after riding off (or after a race) before the rider offers again.")]
    public float rideOffSeconds = 25f;
    public float rideOffSpeedFactor = 1.3f;
    [Tooltip("Patrol: the rider loops from SpotM - this ...")]
    public float patrolBehindM = 150f;
    [Tooltip("... to SpotM + this, re-spawning only while Kuro is further than hideM away.")]
    public float patrolAheadM = 25f;
    public float hideM = 120f;
    public float laneChangeMps = 1.1f;
    [Tooltip("No riding through Kuro: inside this longitudinal gap ...")]
    public float clipLongM = 2.4f;
    [Tooltip("... riders are kept at least this far apart laterally (or held back).")]
    public float clipLatM = 1.1f;
    public float maxRideLookYawDeg = 55f;

    [Header("Stopping with Kuro (QA 2026-09-26: 'Brake [Space] to stop and race' must work)")]
    [Tooltip("While offering/pacing, Kuro below this speed (m/s) = he has stopped for the race: " +
             "the rider eases to a stop beside / just ahead of him instead of rolling away.")]
    public float holdBelowMps = 1.5f;
    [Tooltip("Hysteresis: Kuro back above this speed and the rider rides on with him.")]
    public float holdResumeMps = 2.0f;
    [Tooltip("Where the rider stops: this many metres ahead of Kuro (abeam, paceLateralM to the side).")]
    public float holdAheadM = 1.0f;
    [Tooltip("Braking while stopping with Kuro (m/s^2). Kuro's own brake is ~0.55 g (5.4).")]
    public float holdBrakeMps2 = 5.0f;
    [Tooltip("Top speed when rolling up to the stop slot from behind it.")]
    public float holdCreepMps = 2.2f;
    [Tooltip("Below this speed the outer foot comes off the pedal and goes down to the road.")]
    public float footDownBelowMps = 0.5f;
    [Tooltip("Bike lean toward the planted foot, degrees (a stopped rider tips onto that foot).")]
    public float footDownLeanDeg = 7f;

    [Header("Clearance (QA 2026-09-26: do not ride into traffic riders)")]
    [Tooltip("Look this far ahead (m) for traffic in the rider's own line.")]
    public float clearLookM = 16f;
    [Tooltip("Half-width of the rider's own line (m): bodies further to the side are passed, not followed.")]
    public float clearLatM = 1.0f;
    [Tooltip("Hold this gap (m) behind a slower rider ahead in the same line.")]
    public float clearGapM = 2.6f;
    public float clearGain = 0.8f;

    /// <summary>A rider/vehicle the racer must not ride into (world position + velocity).</summary>
    public struct Body { public Vector3 Pos, Vel; public Transform Root; }

    public bool Riding { get; private set; }
    /// <summary>True while stopping / stopped with Kuro (he braked while this rider offered a race).</summary>
    public bool Holding
    {
        get => _holding;
        private set { if (_holding && !value) _heldUntil = Time.time; _holding = value; }
    }
    private bool _holding;
    private float _heldUntil = -999f;
    /// <summary>Holding, or pushed off from a stop within the last few seconds (the pair is
    /// re-forming, so the prompt keeps its wider radius instead of flickering off).</summary>
    public bool HeldRecently => _holding || Time.time - _heldUntil < 5f;
    /// <summary>0..1: the outer foot is down on the road (stopped with Kuro).</summary>
    public float FootDown => _footDown;
    /// <summary>True while the greeting wave plays (parked or riding).</summary>
    public bool Waving => _waveT >= 0f;
    public float WaveTime => _waveT;
    /// <summary>Last frame's clearance result: metres to the nearest body ahead in the rider's line
    /// (+inf = clear) and whether it capped the speed.</summary>
    public float ClearAheadM { get; private set; } = float.PositiveInfinity;
    public bool ClearBlocked { get; private set; }
    /// <summary>Riding the course out of a race (as opposed to <see cref="Riding"/> = racing).</summary>
    public bool AmbientRiding { get; private set; }
    public RideState State { get; private set; } = RideState.Cruise;
    /// <summary>Course metres (wrapped) of the rider while ambient riding.</summary>
    public float RouteM { get; private set; }
    public float Lane { get; private set; }
    public float Speed { get; private set; }
    /// <summary>False while riding off (ignored, or just raced): no prompt from this rider.</summary>
    public bool OffersRace => !Riding && (!AmbientRiding || State != RideState.RideOff);
    public Vector3 ParkPosition { get; private set; }
    public Quaternion ParkRotation { get; private set; }

    /// <summary>Kuro's transform, set by the director (null = no look-at).</summary>
    [System.NonSerialized] public Transform lookTarget;
    /// <summary>Looks here instead when set, at full engagement (capture close-ups).</summary>
    [System.NonSerialized] public Transform lookOverride;
    /// <summary>0..1 how strongly to engage (the director fades it with distance).</summary>
    [System.NonSerialized] public float engage;

    private Transform _hips, _chest, _neck, _head, _shoulderL, _shoulderR;
    private Transform _handTargetL, _handTargetR, _poleL, _poleR;
    private float _yaw, _waveT = -1f, _waveReady, _seed;
    private bool _wasNear;
    private RouteCourse _course;
    private float _halfWidth = 3.5f, _var = 1f, _stateT, _offerT, _laneVel, _slot;
    private float _footDown, _lastWaveAt = -999f;
    private bool _wasPacing, _wasStopped;
    private NpcGreeting _greet;
    private float _passLane = float.NaN, _passUntil = -1f;
    // oncoming rider head-on in our line: the line to step aside to, and the stop-short cap
    // used only when that line is taken by Kuro beside us
    private float _evadeLane = float.NaN, _evadeUntil = -1f, _evadeCap = float.PositiveInfinity;

    // ------------------------------------------------------------------ setup

    /// <summary>Takes an existing roster rider over (or a clone of a pooled traffic rider).</summary>
    public void Init(RacerDef d, RouteCourse course, float roadHalfWidth)
    {
        def = d;
        _seed = (d.Id.GetHashCode() & 1023) * 0.37f;
        // off the ride loop: the roster mover, swerving, pooled-traffic LOD and anything else
        // that would move or re-pose this rider. The rig and the greeting stay.
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb == this) continue;
            if (mb is KuroBikeRig || mb is NpcGreeting || mb is KuroDismount || mb is RiderBlink) continue;
            if (mb.GetType().Name == "KuroOutline") continue;
            mb.enabled = false;
        }
        // a racer cloned from a pooled traffic rider inherits that rider's LOD state; if the pool
        // had it culled (far from the camera) at clone time, every renderer is off and nobody
        // drives this copy's LOD again - the racer rode the course INVISIBLE (only the marker beam
        // showed). Force full detail once: there are only a few racers per course.
        var lod = GetComponentInChildren<ShiosaiRiderLod>(true);
        if (lod != null) { lod.Invalidate(); lod.Apply(0f); }
        var greet = GetComponent<NpcGreeting>() ?? GetComponentInChildren<NpcGreeting>(true);
        if (greet == null) greet = gameObject.AddComponent<NpcGreeting>();   // every challenger can say their line
        greet.riderName = d.Name;
        greet.ownPhrases = new[] { d.Challenge };
        greet.enabled = true;
        _greet = greet;
        rig = GetComponentInChildren<KuroBikeRig>(true);
        if (rig != null)
        {
            rig.solveEveryNFrames = 1;   // additive acting needs a fresh solve every frame
            dismount = rig.GetComponent<KuroDismount>();
            if (dismount == null) dismount = rig.gameObject.AddComponent<KuroDismount>();
            dismount.rig = rig;
            dismount.side = kerbSide;
            FindBones();
            // a riding challenger's legs turn from travelled distance only: a stopped rider's
            // cranks must stop too (no preview-cadence "ghost pedalling" while waiting for Kuro)
            if (rideWhileIdle) rig.previewCadenceRpm = 0f;
        }
        _handTargetL = new GameObject("~RaceHandL").transform;
        _handTargetR = new GameObject("~RaceHandR").transform;
        _poleL = new GameObject("~RacePoleL").transform;
        _poleR = new GameObject("~RacePoleR").transform;
        foreach (var t in new[] { _handTargetL, _handTargetR, _poleL, _poleR }) t.SetParent(transform, false);

        // park: bike at the kerb, pointing down the road
        Vector3 p = course.PositionAt(def.SpotM), s = course.SideAt(def.SpotM), u = course.UpAt(def.SpotM);
        Vector3 fwd = course.TangentAt(def.SpotM);
        float lateral = kerbSide * Mathf.Max(1.2f, roadHalfWidth - 0.45f);
        ParkPosition = p + s * lateral + u * 0.065f;
        ParkRotation = Quaternion.LookRotation(fwd, u);
        _course = course;
        _halfWidth = roadHalfWidth;
        _var = 0.92f + 0.16f * Mathf.Repeat(_seed * 7.31f, 1f);
        if (rideWhileIdle) StartAmbientRide(def.SpotM, cruiseMps * _var, RideState.Cruise);
        else Park();
    }

    private void FindBones()
    {
        _hips = rig.Hips;
        _chest = Find(rig.transform, "Spine02") ?? Find(rig.transform, "Spine01") ?? Find(rig.transform, "Spine");
        _neck = Find(rig.transform, "Neck");
        _head = Find(rig.transform, "Head");
        _shoulderL = rig.ElbowL != null ? rig.ElbowL.parent : null;   // upper arm
        _shoulderR = rig.ElbowR != null ? rig.ElbowR.parent : null;
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith(name, System.StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    /// <summary>Back to the kerb, standing beside the bike (legacy parked mode).</summary>
    public void Park()
    {
        transform.SetPositionAndRotation(ParkPosition, ParkRotation);
        Riding = false;
        AmbientRiding = false;
        if (dismount != null) dismount.HoldStanding();
        _yaw = 0f;
    }

    /// <summary>Back in the saddle for the race (the director moves the transform from here on).</summary>
    public void SetRiding()
    {
        Riding = true;
        AmbientRiding = false;
        InSaddle();
    }

    private void InSaddle()
    {
        if (dismount != null) dismount.SnapRiding();
        Holding = false;
        _footDown = 0f;
        _waveT = -1f;
        if (rig != null)
        {
            ClearRideOverlays();
            rig.poseHandTargetOverrideL = rig.poseHandTargetOverrideR = null;
            rig.poseElbowPoleOverrideL = rig.poseElbowPoleOverrideR = null;
        }
    }

    /// <summary>Idle state after a race or at setup: in the saddle, riding the course from
    /// <paramref name="metres"/>. Falls back to <see cref="Park"/> in legacy mode.</summary>
    public void StartAmbientRide(float metres, float speed, RideState state, float lane = float.NaN)
    {
        if (!rideWhileIdle || _course == null) { Park(); return; }
        Riding = false;
        AmbientRiding = true;
        InSaddle();
        RouteM = _course.Wrap(metres);
        Speed = Mathf.Max(minRollMps, speed);
        Lane = float.IsNaN(lane) ? CruiseLane : ClampLane(lane);
        _laneVel = 0f;
        Holding = false;
        _footDown = 0f;
        ClearRideOverlays();
        SetState(state);
        _yaw = 0f;
        PlaceOnRoad();
    }

    private float CruiseLane => ClampLane(kerbSide * Mathf.Clamp(_halfWidth * 0.4f, 0.9f, 1.6f));
    private float ClampLane(float l) { float m = Mathf.Max(0.3f, _halfWidth - 0.5f); return Mathf.Clamp(l, -m, m); }
    private void SetState(RideState s) { State = s; _stateT = 0f; if (s != RideState.Pace) _offerT = 0f; }

    /// <summary>Signed along-road metres from <paramref name="b"/> to <paramref name="a"/>
    /// (wrapped to the shorter way on a closed course).</summary>
    public float Gap(float a, float b)
    {
        float g = a - b;
        if (_course != null && _course.Closed && _course.Length > 1f)
        {
            float L = _course.Length;
            g = Mathf.Repeat(g + L * 0.5f, L) - L * 0.5f;
        }
        return g;
    }

    /// <summary>
    /// One frame of riding out of a race. <paramref name="hasKuro"/> = Kuro is on this course
    /// (kuroM = his course metres, kuroLane = his lateral offset, kuroSpeed = m/s);
    /// <paramref name="allowPace"/> = pacing/offering allowed (false during another race, when
    /// the rider only avoids him). <paramref name="speedCap"/> holds the rider behind another
    /// racer on the same line.
    /// </summary>
    public void TickAmbient(float dt, bool hasKuro, float kuroM, float kuroLane, float kuroSpeed,
                            bool allowPace, float speedCap = float.PositiveInfinity)
    {
        if (!AmbientRiding || _course == null || dt <= 0f) return;
        _stateT += dt;
        float cruise = cruiseMps * _var;
        float targetV = cruise, targetLane = CruiseLane;
        float g = hasKuro ? Gap(RouteM, kuroM) : float.PositiveInfinity;   // + = rider ahead of Kuro
        float paceLane = ClampLane(kuroLane - kerbSide * paceLateralM);

        if (State == RideState.RideOff)
        {
            targetV = cruise * rideOffSpeedFactor;
            if (hasKuro && Mathf.Abs(g) < clipLongM * 2f) targetLane = paceLane;   // pass him on his outside
            if (_stateT >= rideOffSeconds && (!hasKuro || Mathf.Abs(g) > paceWindowM * 2f)) SetState(RideState.Cruise);
        }
        else if (hasKuro && allowPace && g > -(State == RideState.Pace ? paceWindowM * 2f : paceWindowM) && g < awareBehindM)
        {
            float holdErr = holdAheadM - g;   // + = the stop slot is still ahead of the rider
            bool kuroStopped = kuroSpeed < (Holding ? holdResumeMps : holdBelowMps);
            if (kuroStopped && (State == RideState.Pace || Mathf.Abs(holdErr) < paceWindowM * 0.5f))
            {
                // Kuro braked for the race: ease to a stop one bike-length ahead of him, abeam,
                // and wait there with the prompt up (the offer clock is paused while he is stopped)
                if (State != RideState.Pace) SetState(RideState.Pace);
                Holding = true;
                // v = sqrt(2 a d) lands on the slot; past it (he stopped under us), just brake
                targetV = holdErr > 0.05f
                    ? Mathf.Min(holdCreepMps + kuroSpeed, Mathf.Sqrt(2f * holdBrakeMps2 * 0.6f * holdErr))
                    : 0f;
                targetLane = paceLane;
                _slot = Mathf.Clamp(g, -paceBehindM, paceAheadM);   // ride on from where we stopped
            }
            else
            {
                Holding = false;
                // Kuro noticed: soft-pedal until he is close, then hold the slot beside him (first just
                // behind, where the greeting can see him, then just ahead, in the chase camera's view)
                float slotWant = State == RideState.Pace && _offerT >= paceGreetSeconds ? paceAheadM : -paceBehindM;
                if (State != RideState.Pace) _slot = -paceBehindM;
                _slot = Mathf.MoveTowards(_slot, slotWant, paceSlotMps * dt);
                float err = _slot - g;
                // pacing a slow Kuro (pulling away after a stop) may go below the cruise floor
                float floor = State == RideState.Pace ? Mathf.Min(minRollMps, kuroSpeed) : minRollMps;
                float paced = Mathf.Clamp(kuroSpeed + paceGain * err, floor, maxPaceMps);
                float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(paceWindowM, paceWindowM * 0.4f, Mathf.Abs(err)));
                // already pacing and dropped behind the slot (held by traffic, or Kuro sped off):
                // chase back up to him instead of soft-pedalling away
                if (State == RideState.Pace && err > 0f) w = 1f;
                targetV = Mathf.Lerp(Mathf.Min(cruise, easeOffMps), paced, w);
                targetLane = paceLane;
                if (w > 0.5f && State != RideState.Pace) SetState(RideState.Pace);
                if (State == RideState.Pace)
                {
                    _offerT += dt;
                    if (_offerT >= offerSeconds) SetState(RideState.RideOff);
                }
            }
        }
        else
        {
            Holding = false;
            if (State == RideState.Pace) SetState(RideState.Cruise);
            if (hasKuro && Mathf.Abs(g) < clipLongM * 2f) targetLane = paceLane;
        }
        if (State == RideState.RideOff) Holding = false;
        // passing a slower rider ahead (ClearanceCap planned the line this frame)
        float passLane = PassLane;
        if (!float.IsNaN(passLane) && !Holding) targetLane = passLane;
        // an oncoming rider head-on in our line (traffic does not yield): step aside toward our
        // own side of the road - into Kuro's line when we are well ahead of/behind him - and only
        // stop short when that line is Kuro himself beside us (then drop in behind him)
        float evadeLane = EvadeLane;
        bool evading = false;
        if (!float.IsNaN(evadeLane))
        {
            bool kuroBeside = hasKuro && Mathf.Abs(evadeLane - kuroLane) < clipLatM && Mathf.Abs(g) < clipLongM + 0.5f;
            if (kuroBeside)
            {
                speedCap = Mathf.Min(speedCap, Mathf.Min(_evadeCap, Mathf.Max(0f, kuroSpeed - 1.5f)));
                ClearBlocked = true;
            }
            else { targetLane = evadeLane; evading = true; }
        }

        // no riding through Kuro: close behind him on his line, hold his speed until the line changes
        if (hasKuro && Mathf.Abs(Lane - kuroLane) < clipLatM && g < 0f && g > -(clipLongM + 1.5f))
            targetV = Mathf.Min(targetV, Mathf.Max(Holding ? 0f : 0.5f, kuroSpeed - 0.3f));
        targetV = Mathf.Min(targetV, speedCap);
        // the challenger is always rolling - except when pacing a slow Kuro or stopped with him
        bool pacingSlow = State == RideState.Pace && kuroSpeed < minRollMps;
        if (!Holding) targetV = Mathf.Max(targetV, Mathf.Min(pacingSlow ? Mathf.Max(0f, kuroSpeed) : minRollMps, speedCap));

        float accel = State == RideState.Pace ? Mathf.Max(accelMps2, paceAccelMps2) : accelMps2;
        float rate = targetV > Speed ? accel : (Holding || State == RideState.Pace ? holdBrakeMps2 : brakeMps2);
        // the last metre of a stop rolls out gently instead of snapping to zero
        if (Holding && targetV < Speed && Speed < 1.2f) rate = Mathf.Lerp(1.4f, rate, Speed / 1.2f);
        Speed = Mathf.MoveTowards(Speed, targetV, rate * dt);
        // steering needs rolling: a stopped bike does not slide sideways into its line (moving
        // out to pass a stopped rider is the exception: a slow, steep swing out of their wheel)
        bool passing = !float.IsNaN(PassLane);
        float laneRate = laneChangeMps * Mathf.Max(Mathf.Clamp01(Speed / 1.5f), passing && Speed > 0.3f ? 0.75f : 0f)
                         * (evading ? 1.6f : 1f);
        float newLane = Mathf.MoveTowards(Lane, ClampLane(targetLane), laneRate * dt);
        _laneVel = (newLane - Lane) / dt;
        Lane = newLane;
        float next = RouteM + Speed * dt;
        RouteM = _course.Wrap(next);

        // foot down once (nearly) stopped - with Kuro, or held behind a stopped rider - and up
        // again as the rider pushes off
        bool footWanted = (Holding || ClearBlocked) && Speed < footDownBelowMps;
        _footDown = Mathf.MoveTowards(_footDown, footWanted ? 1f : 0f, dt / (footWanted ? 0.45f : 0.3f));

        // patrol: loop back to the run-in of the start line, only while Kuro cannot see it
        if (State != RideState.Pace)
        {
            bool pastPatrol = Gap(RouteM, def.SpotM) > patrolAheadM || (!_course.Closed && next >= _course.Length - 1f);
            float reset = _course.Wrap(Mathf.Max(0f, def.SpotM - patrolBehindM));
            bool hidden = !hasKuro || (Mathf.Abs(g) > hideM && Mathf.Abs(Gap(reset, kuroM)) > hideM);
            if (pastPatrol && hidden)
            {
                RouteM = reset;
                Speed = cruise;
                Lane = CruiseLane;
                _laneVel = 0f;
                if (State == RideState.RideOff && _stateT >= rideOffSeconds * 0.5f) SetState(RideState.Cruise);
            }
        }
        PlaceOnRoad();
    }

    private void PlaceOnRoad()
    {
        if (_course == null) return;
        Vector3 up = _course.UpAt(RouteM), side = _course.SideAt(RouteM), fwd = _course.TangentAt(RouteM);
        // heading follows the line change a little, like a rider steering across
        float yaw = Mathf.Atan2(_laneVel, Mathf.Max(1.5f, Speed)) * Mathf.Rad2Deg;
        Vector3 dir = Quaternion.AngleAxis(yaw, up) * fwd;
        // stopped with a foot down: the bike tips a few degrees onto that foot (rolled about the
        // bike's ground point, so the wheels stay on the road)
        float roll = -OuterSide * footDownLeanDeg * Mathf.SmoothStep(0f, 1f, _footDown);
        transform.SetPositionAndRotation(_course.PositionAt(RouteM) + side * Lane + up * 0.065f,
                                         Quaternion.LookRotation(dir, up) * Quaternion.AngleAxis(roll, Vector3.forward));
    }

    /// <summary>+1 = the rider's right. The side AWAY from Kuro while pacing (he rides on the
    /// kerb side of them): the foot that goes down, and the free hand is the other one.</summary>
    private int OuterSide => kerbSide < 0 ? 1 : -1;

    /// <summary>
    /// Speed cap (m/s) so this rider does not ride into anything ahead in its own line: traffic
    /// riders (NPCCyclist rosters, the Minato/Shiosai pools), other racers, the Time Trial ghost.
    /// Same-way bodies are followed at <see cref="clearGapM"/>; a body further than
    /// <see cref="clearLatM"/> to the side is simply passed. Kuro is handled separately (pacing).
    /// </summary>
    public float ClearanceCap(System.Collections.Generic.IReadOnlyList<Body> bodies, bool planPass = true)
    {
        ClearAheadM = float.PositiveInfinity;
        ClearBlocked = false;
        if (bodies == null || _course == null) return float.PositiveInfinity;
        Vector3 fwd = _course.TangentAt(RouteM), side = _course.SideAt(RouteM), me = transform.position;
        float cap = float.PositiveInfinity, bindLane = float.NaN, bindV = 0f, bindAlong = float.PositiveInfinity;
        float evadeAlong = float.PositiveInfinity, evadeLane = float.NaN, evadeCap = float.PositiveInfinity;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b.Root == null || b.Root == transform || b.Root.IsChildOf(transform)) continue;
            Vector3 d = b.Pos - me;
            if (d.sqrMagnitude > (clearLookM + 4f) * (clearLookM + 4f)) continue;
            float along = Vector3.Dot(d, fwd), lat = Vector3.Dot(d, side);
            if (along < 0.2f || along > clearLookM || Mathf.Abs(lat) > clearLatM) continue;
            float ov = Vector3.Dot(b.Vel, fwd);
            float c;
            if (ov < -0.5f)
            {
                // oncoming in our line: traffic riders do not yield, so step aside (toward our own
                // kerb, or the other way if that does not fit) rather than stopping dead in its path
                float c0 = Mathf.Max(0f, (along - 2f) * 0.4f);
                float bl = Lane + lat, step = clearLatM + 0.35f;
                // away from the body: toward our kerb unless it is already on that side of us
                int dir = Mathf.Abs(lat) > 0.3f && Mathf.Sign(lat) == kerbSide ? -kerbSide : kerbSide;
                float ev = bl + dir * step;
                if (Mathf.Abs(ClampLane(ev) - ev) > 0.05f || !LineFree(bodies, ev, me, fwd, side)) ev = bl - dir * step;
                if (Mathf.Abs(ClampLane(ev) - ev) <= 0.05f && LineFree(bodies, ev, me, fwd, side))
                {
                    if (along < evadeAlong) { evadeAlong = along; evadeLane = ev; evadeCap = c0; }
                    ClearAheadM = Mathf.Min(ClearAheadM, along);
                    continue;
                }
                if (Mathf.Abs(lat) > 0.7f) continue;
                c = c0;
            }
            else c = Mathf.Max(0f, ov + clearGain * (along - clearGapM));
            ClearAheadM = Mathf.Min(ClearAheadM, along);
            if (c < cap) { cap = c; bindLane = ov >= -0.5f ? Lane + lat : float.NaN; bindV = ov; bindAlong = along; }
        }
        LastEvadeLane = evadeLane;
        // (planPass = false is a pure query: no pass or step-aside is recorded on the rider)
        if (planPass && !float.IsNaN(evadeLane)) { _evadeLane = evadeLane; _evadeCap = evadeCap; _evadeUntil = Time.time + 0.6f; }
        ClearBlocked = cap < Speed + 0.05f;

        // pass a slower same-way rider on the road-centre side (away from the kerb and from a
        // pacing Kuro) when that line is clear; otherwise hold behind them (the cap above)
        if (planPass && !float.IsNaN(bindLane) && !Holding && bindV < Mathf.Max(Speed, cruiseMps * _var) - 0.5f)
        {
            float want = bindLane + OuterSide * (clearLatM + 0.3f);
            bool fits = Mathf.Abs(ClampLane(want) - want) < 0.05f;
            for (int i = 0; fits && i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b.Root == null || b.Root == transform || b.Root.IsChildOf(transform)) continue;
                Vector3 d = b.Pos - me;
                float along = Vector3.Dot(d, fwd), lane = Lane + Vector3.Dot(d, side);
                if (along > -3f && along < clearLookM && Mathf.Abs(lane - want) < clearLatM) fits = false;
            }
            if (fits) { _passLane = want; _passUntil = Time.time + 2.5f; }
        }
        // while moving out to pass, creep (steering needs rolling) but keep a bike length back
        if (Time.time < _passUntil && !float.IsNaN(_passLane) && Mathf.Abs(Lane - _passLane) > 0.1f)
            cap = Mathf.Max(cap, bindAlong > 1.3f ? 0.9f : 0f);
        return cap;
    }

    /// <summary>The line this rider is moving to in order to pass a slower rider (NaN = none).</summary>
    public float PassLane => Time.time < _passUntil ? _passLane : float.NaN;

    /// <summary>The line this rider is stepping aside to for an oncoming rider (NaN = none).</summary>
    public float EvadeLane => Time.time < _evadeUntil ? _evadeLane : float.NaN;
    /// <summary>The step-aside line the last <see cref="ClearanceCap"/> call found (NaN = none needed/possible).</summary>
    public float LastEvadeLane { get; private set; } = float.NaN;

    /// <summary>No rider/body within <see cref="clearLatM"/> of <paramref name="lane"/> from a few
    /// metres back to the look-ahead (a line we can move into).</summary>
    private bool LineFree(System.Collections.Generic.IReadOnlyList<Body> bodies, float lane, Vector3 me, Vector3 fwd, Vector3 side)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b.Root == null || b.Root == transform || b.Root.IsChildOf(transform)) continue;
            Vector3 d = b.Pos - me;
            float along = Vector3.Dot(d, fwd), l = Lane + Vector3.Dot(d, side);
            if (along > -3f && along < clearLookM && Mathf.Abs(l - lane) < clearLatM) return false;
        }
        return true;
    }

    /// <summary>Drops the riding-only pose overlays (foot down, hip shift, wave hand).</summary>
    private void ClearRideOverlays()
    {
        if (rig == null) return;
        rig.poseFootGoalWeightL = rig.poseFootGoalWeightR = 0f;
        rig.poseHeelOutDegreesL = rig.poseHeelOutDegreesR = 0f;
        rig.poseHipWorldOffset = Vector3.zero;
        if (rig.poseHandTargetOverrideL == _handTargetL) rig.poseHandTargetOverrideL = null;
        if (rig.poseHandTargetOverrideR == _handTargetR) rig.poseHandTargetOverrideR = null;
        if (rig.poseElbowPoleOverrideL == _poleL) rig.poseElbowPoleOverrideL = null;
        if (rig.poseElbowPoleOverrideR == _poleR) rig.poseElbowPoleOverrideR = null;
    }

    public void PlayWave() { _waveT = 0f; _lastWaveAt = Time.time; }

    // ------------------------------------------------------------------ acting

    private void Update()
    {
        if (Riding || rig == null) return;
        if (AmbientRiding) { RideActing(); return; }
        if (dismount == null || dismount.T < 0.99f) return;
        float t = Time.time + _seed;

        bool near = engage > 0.05f;
        if (near && !_wasNear && Time.time >= _waveReady) { PlayWave(); _waveReady = Time.time + waveRearmSeconds; }
        _wasNear = near;

        // slow weight shift (added to the dismount's standing hip offset)
        Vector3 side = rig.transform.right;
        rig.poseHipWorldOffset += side * (0.012f * Mathf.Sin(t * 0.35f)) +
                                  rig.transform.up * (standLift + 0.004f * Mathf.Sin(t * breathHz * 2f * Mathf.PI));

        ApplyStance(t);
        if (_waveT >= 0f)
        {
            _waveT += Time.deltaTime;
            if (_waveT > waveSeconds) _waveT = -1f;
            else ApplyWave(_waveT);
        }
    }

    /// <summary>
    /// Acting while riding out of a race (the rig's seated solve stays in charge): a greeting
    /// wave with the hand on Kuro's side (the other hand keeps the bars) when the rider starts
    /// pacing him, again when they come to a stop beside him; and the outer foot down on the road
    /// while stopped. Every overlay is written fresh each frame, so none can stick.
    /// </summary>
    private void RideActing()
    {
        ClearRideOverlays();
        bool pacing = State == RideState.Pace;
        if (pacing && !_wasPacing && Time.time >= _waveReady)
        {
            PlayWave();
            SayChallenge();
            _waveReady = Time.time + waveRearmSeconds;
        }
        _wasPacing = pacing;
        bool stopped = Holding && Speed < 0.3f;
        if (stopped && !_wasStopped && _waveT < 0f && Time.time - _lastWaveAt > 8f) PlayWave();
        _wasStopped = stopped;

        Vector3 up = rig.transform.up, fwd = rig.transform.forward;
        float fd = Mathf.SmoothStep(0f, 1f, _footDown);
        if (fd > 0.001f && _course != null)
        {
            int o = OuterSide;
            // flat road frame (not the leaning bike's): the foot lands ON the road
            Vector3 cSide = _course.SideAt(RouteM), cUp = _course.UpAt(RouteM), cFwd = _course.TangentAt(RouteM);
            Vector3 goal = _course.PositionAt(RouteM) + cSide * (Lane + o * 0.30f) + cFwd * 0.06f + cUp * rig.ankleHeight;
            if (o > 0) { rig.poseFootGoalR = goal; rig.poseFootGoalWeightR = fd; rig.poseHeelOutDegreesR = 10f * fd; }
            else { rig.poseFootGoalL = goal; rig.poseFootGoalWeightL = fd; rig.poseHeelOutDegreesL = 10f * fd; }
            // slide a touch forward off the nose of the saddle, toward the planted foot
            rig.poseHipWorldOffset = (cSide * (o * 0.035f) + fwd * 0.03f - up * 0.03f) * fd;
        }

        if (_waveT >= 0f)
        {
            _waveT += Time.deltaTime;
            if (_waveT > waveSeconds) _waveT = -1f;
            else ApplyRideWave(_waveT);
        }
    }

    /// <summary>The challenge line on the greeting card, with the wave, as the rider starts pacing
    /// Kuro. The rider's own NpcGreeting only fires when Kuro enters its view cone, which a pacing
    /// rider coming up behind him may never give it, and in busy streets a passing traffic
    /// rider's hello can take the shared card first; the challenger should say their line.</summary>
    private void SayChallenge()
    {
        if (def == null || string.IsNullOrEmpty(def.Challenge)) return;
        var portrait = _greet != null && _greet.portrait != null
            ? _greet.portrait : Resources.Load<Texture2D>(NpcGreeting.PortraitResourceDir + def.Name);
        float secs = _greet != null ? Mathf.Max(3.5f, _greet.visibleSeconds) : 3.5f;
        NpcGreetingCard.Instance.Show(this, def.Name, def.Challenge, portrait, secs, Time.time);
    }

    /// <summary>Riding wave: the hand on Kuro's side comes off its hood to shoulder height and
    /// waves twice; the other hand stays on the bars (its target is left to the rig).</summary>
    private void ApplyRideWave(float t)
    {
        bool leftHand = OuterSide > 0;   // Kuro rides on the side opposite the planted foot
        var shoulder = leftHand ? _shoulderL : _shoulderR;
        var hood = leftHand ? rig.HoodL : rig.HoodR;
        if (shoulder == null || hood == null) return;
        Vector3 up = rig.transform.up, right = rig.transform.right, fwd = rig.transform.forward;
        float outSign = leftHand ? -1f : 1f;
        float raise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f)) *
                      Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((waveSeconds - t) / 0.35f));
        float swing = Mathf.Sin(t * 2f * Mathf.PI * 2.2f) * 0.06f * raise;
        // up and OUT beside the helmet (the chibi head is wide: a hand straight above the shoulder
        // reads as a hand over the face), palm swinging side to side
        Vector3 aloft = shoulder.position + up * 0.24f + right * outSign * (0.30f + swing) + fwd * 0.02f;
        var target = leftHand ? _handTargetL : _handTargetR;
        target.position = Vector3.Lerp(hood.position, aloft, raise);
        var pole = leftHand ? _poleL : _poleR;
        pole.position = shoulder.position + right * outSign * 0.50f - up * 0.10f - fwd * 0.05f;
        Assign(leftHand, !leftHand);
    }

    private void ApplyStance(float t)
    {
        if (_hips == null) return;
        Vector3 fwd = rig.transform.forward, up = rig.transform.up, right = rig.transform.right;
        Vector3 hip = _hips.position;
        // chest from the shoulder joints: bone NAMES differ between bodies (on some the "Spine"
        // chain ends at the waist), but the upper-arm roots are always the shoulders
        Vector3 chest = _shoulderL != null && _shoulderR != null
            ? (_shoulderL.position + _shoulderR.position) * 0.5f - up * 0.06f
            : (_chest != null ? _chest.position : hip + up * 0.3f);
        switch (def.Stance)
        {
            case RacerStance.ArmsCrossed:
            {
                // forearms folded across the chest, the right over the left
                float b = 0.004f * Mathf.Sin(t * breathHz * 2f * Mathf.PI);
                Vector3 c = chest + fwd * 0.12f - up * (0.04f - b);
                _handTargetL.position = c + right * 0.075f + up * 0.012f;
                _handTargetR.position = c - right * 0.075f + fwd * 0.025f;
                _poleL.position = chest - right * 0.25f - up * 0.12f + fwd * 0.05f;
                _poleR.position = chest + right * 0.25f - up * 0.12f + fwd * 0.05f;
                Assign(true, true);
                break;
            }
            case RacerStance.HandsOnHips:
            {
                _handTargetL.position = hip - right * 0.14f + up * 0.09f + fwd * 0.02f;
                _handTargetR.position = hip + right * 0.14f + up * 0.09f + fwd * 0.02f;
                _poleL.position = hip - right * 0.45f + up * 0.2f - fwd * 0.05f;
                _poleR.position = hip + right * 0.45f + up * 0.2f - fwd * 0.05f;
                Assign(true, true);
                break;
            }
            default:
                // relaxed: the dismount pose already has the bike-side hand on the bars and the
                // outer arm hanging, which is exactly this stance
                rig.poseElbowPoleOverrideL = rig.poseElbowPoleOverrideR = null;
                break;
        }
    }

    private void Assign(bool left, bool right)
    {
        if (left) { rig.poseHandTargetOverrideL = _handTargetL; rig.poseElbowPoleOverrideL = _poleL; }
        if (right) { rig.poseHandTargetOverrideR = _handTargetR; rig.poseElbowPoleOverrideR = _poleR; }
    }

    /// <summary>The OUTER hand (kerb side, away from the bike) comes up and waves twice.</summary>
    private void ApplyWave(float t)
    {
        if (_chest == null) return;
        bool leftHand = kerbSide < 0;
        var shoulder = leftHand ? _shoulderL : _shoulderR;
        if (shoulder == null) return;
        Vector3 up = rig.transform.up, right = rig.transform.right, fwd = rig.transform.forward;
        float outSign = leftHand ? -1f : 1f;
        float raise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f)) *
                      Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((waveSeconds - t) / 0.35f));
        float swing = Mathf.Sin(t * 2f * Mathf.PI * 2.2f) * 0.07f;
        Vector3 hang = shoulder.position - up * 0.3f + right * outSign * 0.06f;
        Vector3 aloft = shoulder.position + up * 0.24f + right * outSign * (0.12f + swing) + fwd * 0.05f;
        var target = leftHand ? _handTargetL : _handTargetR;
        target.position = Vector3.Lerp(hang, aloft, raise);
        var pole = leftHand ? _poleL : _poleR;
        pole.position = shoulder.position + right * outSign * 0.35f - up * 0.1f;
        Assign(leftHand, !leftHand);
    }

    private void LateUpdate()
    {
        // runs after the rig's solve (order 100); the rig re-poses these bones from their binds
        // every frame (solveEveryNFrames = 1), so these rotations never accumulate
        if (Riding || rig == null) return;
        bool standing = !AmbientRiding && dismount != null && dismount.T >= 0.99f;
        if (!standing && !AmbientRiding) return;
        float t = Time.time + _seed;

        // breathing (standing only): chest pitches open on the in-breath and the shoulders lift a touch
        float breath = Mathf.Sin(t * breathHz * 2f * Mathf.PI);
        if (standing && _chest != null)
            _chest.rotation = Quaternion.AngleAxis(-breathDeg * breath, rig.transform.right) * _chest.rotation;
        if (standing && _shoulderL != null) _shoulderL.position += rig.transform.up * (0.004f * breath);
        if (standing && _shoulderR != null) _shoulderR.position += rig.transform.up * (0.004f * breath);

        // look at Kuro: signed world yaw from the body's facing, eased, split chest/neck/head
        // (riding: head and neck only, so the hands stay on the bars)
        float maxYaw = AmbientRiding ? maxRideLookYawDeg : maxLookYawDeg;
        float want = 0f;
        var target = lookOverride != null ? lookOverride : lookTarget;
        float eng = lookOverride != null ? 1f : engage;
        if (target != null && eng > 0f)
        {
            Vector3 to = target.position - (_head != null ? _head.position : transform.position);
            Vector3 f = rig.transform.forward;
            to.y = 0f; f.y = 0f;
            if (to.sqrMagnitude > 0.01f && f.sqrMagnitude > 0.01f)
                want = Mathf.Clamp(Vector3.SignedAngle(f, to, Vector3.up), -maxYaw, maxYaw) * eng;
        }
        _yaw = Mathf.Lerp(_yaw, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
        if (Mathf.Abs(_yaw) < 0.05f) return;
        if (standing && _chest != null) _chest.rotation = Quaternion.AngleAxis(_yaw * 0.25f, Vector3.up) * _chest.rotation;
        float neckW = standing ? 0.3f : 0.4f, headW = standing ? 0.45f : 0.6f;
        if (_neck != null) _neck.rotation = Quaternion.AngleAxis(_yaw * neckW, Vector3.up) * _neck.rotation;
        if (_head != null) _head.rotation = Quaternion.AngleAxis(_yaw * headW, Vector3.up) * _head.rotation;
    }

    /// <summary>Head position (for the marker), falling back to a chibi rider's height.</summary>
    public Vector3 HeadPosition =>
        _head != null ? _head.position : transform.position + transform.up * 1.35f;

    /// <summary>World position of the standing rider's feet (for the ground ring).</summary>
    public Vector3 FeetPosition =>
        _hips != null ? new Vector3(_hips.position.x, transform.position.y, _hips.position.z) : transform.position;
}

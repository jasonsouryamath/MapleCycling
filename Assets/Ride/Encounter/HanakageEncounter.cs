using System;
using UnityEngine;

/// <summary>
/// The Hanakage encounter state machine.
///
/// One legendary rider, one climb, one roll per run. The whole thing is a function of two
/// numbers: the player's arc position on the course (owned by <see cref="RideSession"/>) and
/// their real watts (owned by <see cref="DeviceManager"/>). It never reads a button, never
/// teleports her, and never fakes a gradient - she is integrated through the same
/// <see cref="CyclingPhysics"/> model the player is, on the same road, so the gap between them
/// is the honest consequence of two power outputs on one hill.
///
/// Why she can gain on a strong player at all: she is a climber. 51 kg + 6.6 kg against the
/// player's 68 kg + 8.5 kg is roughly a 30 % lower gravity term, which on a 10 % pitch is worth
/// far more than it is anywhere else on the route. That is the design's "Blossom Climber" made
/// physical rather than scripted, and it is also why the encounter is placed on the steep part
/// of the pass and nowhere else.
///
/// Roll discipline (handoff section 3): the roll happens ONCE per eligible run, at the moment
/// the player first crosses <see cref="HanakageEncounterConfig.zoneEntryM"/>. Riding back and
/// forth over the trigger cannot reroll it, because the flag is cleared only by a genuine run
/// reset (course change, lap, or <see cref="RideSession.ResetRide"/>).
///
/// Deliberately NOT here, and deliberately deferred: animation (look-back, standing attack),
/// audio, camera work and any boss framing. The handoff is emphatic that the first sighting must
/// look like an ordinary distant cyclist, and the cheapest way to guarantee that is to have no
/// presentation code capable of doing anything else yet.
/// </summary>
[DefaultExecutionOrder(-50)]
public class HanakageEncounter : MonoBehaviour
{
    public enum Phase
    {
        /// <summary>Not in the zone, or the roll has not happened yet.</summary>
        Dormant = 0,
        /// <summary>The roll happened and failed. She is not on the road this run.</summary>
        Declined,
        /// <summary>She is visible ahead and soft-pedalling. No UI beyond "???".</summary>
        Observation,
        /// <summary>The player is coming across. She lifts her pace a little.</summary>
        Approach,
        /// <summary>The player is on her wheel. The clock that earns the look-back is running.</summary>
        OnWheel,
        /// <summary>She looked back, said her line, and went.</summary>
        Attack,
        /// <summary>The last pitch before the crest.</summary>
        FinalHairpin,
        /// <summary>She goes clear. This always happens; the player never beats her here.</summary>
        Escape,
        /// <summary>She is gone. The beat of nothing before the Journal.</summary>
        DiscoveryPending,
        /// <summary>Done for this run.</summary>
        Complete
    }

    /// <summary>
    /// Handoff section 14. Ordinal order matters - <see cref="RiderJournal.RecordOutcome"/>
    /// keeps the highest ever reached.
    /// </summary>
    public enum Outcome
    {
        None = 0,
        /// <summary>Saw her, never got across.</summary>
        Spotted = 1,
        /// <summary>Got onto her wheel.</summary>
        Caught = 2,
        /// <summary>Answered the attack.</summary>
        Held = 3,
        /// <summary>Was still there over the final hairpin.</summary>
        Impressed = 4
    }

    // ------------------------------------------------------------------ wiring
    [Header("Wiring (found by the setup pass)")]
    public RideSession session;
    public DeviceManager devices;
    public HanakageRider hanakage;

    [Header("Configuration")]
    public HanakageEncounterConfig config = new HanakageEncounterConfig();

    [Tooltip("Deterministic seed for the per-run roll. -1 uses UnityEngine.Random, which is " +
             "what a real ride wants; the self-test harness pins it.")]
    public int rollSeed = -1;

    // ------------------------------------------------------------------ live state
    public Phase State { get; private set; } = Phase.Dormant;
    public Outcome Result { get; private set; } = Outcome.None;

    /// <summary>Road metres between the player and her. Negative if the player is ahead.</summary>
    public float GapM { get; private set; }
    public float HerArcM { get; private set; }
    public float HerSpeedMps { get; private set; }
    public float HerWatts { get; private set; }

    /// <summary>True once the roll for this run has been made, whatever the result.</summary>
    public bool RolledThisRun => _rolled;

    /// <summary>Set while she is a visible part of the world.</summary>
    public bool Active =>
        State == Phase.Observation || State == Phase.Approach || State == Phase.OnWheel ||
        State == Phase.Attack || State == Phase.FinalHairpin || State == Phase.Escape;

    /// <summary>Objective line for the HUD. Empty means show nothing at all.</summary>
    public string ObjectiveText { get; private set; } = "";
    /// <summary>Her current spoken line, or empty. Never set before the look-back.</summary>
    public string DialogueText { get; private set; } = "";
    /// <summary>Seconds the Journal discovery card should still be shown, or 0.</summary>
    public float DiscoveryCardSeconds { get; private set; }

    /// <summary>Fired on every phase change. Audio/camera will hang off this later.</summary>
    public event Action<Phase> PhaseChanged;

    // ------------------------------------------------------------------ internals
    private readonly CyclingPhysics _herPhysics = new CyclingPhysics();
    private bool _rolled;
    private string _runCourseId = "";
    private int _runLapIndex = -1;
    private string _runToken = "";
    private float _lastElapsed;
    private float _phaseSeconds;
    private float _onWheelSeconds;
    private float _heldSeconds;
    private float _droppedSeconds;
    private float _stalledSeconds;
    private float _dialogueSeconds;
    private float _discoveryTimer;
    private float _targetFtpPercent;
    private float _blendedFtpPercent;
    private System.Random _rng;

    private void Awake()
    {
        Resolve();
    }

    private void OnEnable()
    {
        if (session != null) session.RunReset += ResetRun;
    }

    private void OnDisable()
    {
        if (session != null) session.RunReset -= ResetRun;
    }

    public void Resolve()
    {
        if (session == null) session = GetComponent<RideSession>();
        if (session != null)
        {
            // Idempotent: Resolve is called from Awake, from the setup pass and from the test
            // harness, and a double subscription would reset the run twice per restart.
            session.RunReset -= ResetRun;
            session.RunReset += ResetRun;
        }
        if (devices == null) devices = GetComponent<DeviceManager>();
        if (hanakage == null)
        {
            var go = GameObject.Find(HanakageRider.ObjectName);
            if (go != null) hanakage = go.GetComponent<HanakageRider>();
        }
        if (hanakage != null && hanakage.session == null) hanakage.session = session;
        if (rollSeed >= 0 && _rng == null) _rng = new System.Random(rollSeed);
        SyncHerPhysics();
        RehydrateRoll();
    }

    /// <summary>
    /// Re-adopts this run's roll from the ledger.
    ///
    /// The in-memory <c>_rolled</c> flag stops a player rerolling by riding back and forth over
    /// the trigger, but it dies with the component. Anything that rebuilds the encounter without
    /// ending the run - a checkpoint restore, a trigger volume reload, re-running the staging
    /// pass - would otherwise re-arm a 1-in-30 roll on a run that has already had its answer.
    /// Resolve runs on Awake and from every harness entry point, so this is where the answer
    /// comes back.
    /// </summary>
    private void RehydrateRoll()
    {
        if (session == null || _rolled) return;
        string token = session.RunToken;
        if (string.IsNullOrEmpty(token)) return;
        if (string.IsNullOrEmpty(_runToken)) _runToken = token;

        HanakageRollLedger.RollResult result;
        string courseId;
        int lapIndex;
        if (!HanakageRollLedger.TryGet(token, out result, out courseId, out lapIndex)) return;

        _rolled = true;
        _runToken = token;
        _runCourseId = courseId;
        _runLapIndex = lapIndex;

        // A rebuilt encounter cannot resurrect a rider mid-arc from a ledger entry alone, and it
        // must not try: the handoff's contract is one sighting per run, and this run has had it.
        // Declined is the honest terminal state for a run whose roll is already spent.
        if (State == Phase.Dormant) SetPhase(Phase.Declined);
        Debug.Log("[hanakage] roll rehydrated from ledger: run=" + token + " result=" + result);
    }

    private void SyncHerPhysics()
    {
        _herPhysics.riderMassKg = config.riderMassKg;
        _herPhysics.bikeMassKg = config.bikeMassKg;
        _herPhysics.cdA = config.cdA;
    }

    private void Update()
    {
        // An NPC race moves the rider over this climb and back; it must not roll or run her.
        if (RaceDirector.Busy) return;
        Tick(Time.deltaTime);
    }

    // ================================================================== tick

    /// <summary>
    /// One step. Public and dt-driven so the editor harness can run a whole encounter headlessly
    /// at a fixed timestep - the only way to assert "she always escapes" without a human on a
    /// trainer for two minutes.
    /// </summary>
    public void Tick(float dt)
    {
        if (session == null || dt <= 0f) return;
        var course = session.Course;
        if (course == null || course.Count < 2) return;

        DetectRunReset();

        float d = session.DistanceM;

        if (State == Phase.Dormant) { TickDormant(d); return; }
        if (State == Phase.Declined || State == Phase.Complete) return;

        // The dialogue timer runs BEFORE the DiscoveryPending early-out.
        //
        // It used to live below it, which meant "Good." was still on screen twenty-two seconds
        // later, underneath the Rider Journal card. Section 13 asks for the encounter to breathe
        // between her escape and the reveal, and a line frozen on screen for the whole of that
        // silence is the exact opposite - it reads as a stuck HUD, not as a pause.
        if (_dialogueSeconds > 0f)
        {
            _dialogueSeconds -= dt;
            if (_dialogueSeconds <= 0f) DialogueText = "";
        }

        if (State == Phase.DiscoveryPending) { TickDiscovery(dt); return; }

        // ---- she exists from here on -------------------------------------------------
        _phaseSeconds += dt;

        IntegrateHer(dt, course);

        // She never gets passed on her own climb. Clamping here rather than inside the physics
        // keeps the model honest - her WATTS are still the phase's watts, she is simply not
        // allowed to be overtaken - and it is also what stops the player's sculpt riding through
        // hers when the gap closes to nothing.
        float minArc = d + config.minGapM;
        if (HerArcM < minArc)
        {
            HerArcM = minArc;
            HerSpeedMps = Mathf.Max(HerSpeedMps, session.SpeedMps);
        }

        GapM = HerArcM - d;

        if (AbortIfStranded(d, dt)) return;

        switch (State)
        {
            case Phase.Observation:   TickObservation();          break;
            case Phase.Approach:      TickApproach(dt);           break;
            case Phase.OnWheel:       TickOnWheel(dt);            break;
            case Phase.Attack:        TickAttack(dt);             break;
            case Phase.FinalHairpin:  TickHairpin(dt);            break;
            case Phase.Escape:        TickEscape();               break;
        }

        PushToRider();
    }

    // ------------------------------------------------------------------ dormant / roll

    private void TickDormant(float d)
    {
        if (!config.encounterEnabled || _rolled) return;
        if (!config.IsEligibleCourse(session.courseId)) return;
        if (!config.allowRepeatAfterDiscovery && RiderJournal.IsDiscovered(RiderJournal.HanakageId))
            return;
        if (!config.allowRepeatPerLap && session.LapIndex > 0) return;

        if (d < config.zoneEntryM || d > config.zoneEntryM + config.zoneArmWindowM) return;

        // THE roll. Exactly one per run, right here, and nowhere else.
        _rolled = true;
        _runCourseId = session.courseId;
        _runLapIndex = session.LapIndex;
        _runToken = session.RunToken;

        float roll = _rng != null ? (float)_rng.NextDouble() : UnityEngine.Random.value;
        bool spawned = roll <= config.EffectiveSpawnChance;

        // Written BEFORE anything else can go wrong, so a crash or a reload between the roll and
        // the spawn still costs the player their roll rather than granting them a second one.
        HanakageRollLedger.Record(_runToken, _runCourseId, _runLapIndex,
                                  spawned ? HanakageRollLedger.RollResult.Spawned
                                          : HanakageRollLedger.RollResult.Declined);

        if (!spawned)
        {
            SetPhase(Phase.Declined);
            return;
        }

        Spawn(d);
    }

    private void Spawn(float playerDistance)
    {
        SyncHerPhysics();
        HerArcM = playerDistance + config.spawnAheadM;
        // She is already rolling at her observation pace, not starting from a standstill.
        float grade = session.Course.GradeAt(HerArcM, session.gradeWindowM);
        _blendedFtpPercent = config.observationFtpPercent;
        HerSpeedMps = _herPhysics.TerminalSpeed(FtpWatts() * _blendedFtpPercent, grade);
        GapM = config.spawnAheadM;
        Result = Outcome.Spotted;

        if (hanakage != null)
        {
            hanakage.session = session;
            hanakage.arcDistanceM = HerArcM;
            hanakage.speedMps = HerSpeedMps;
            hanakage.SetPresent(true);
            hanakage.Apply();
        }
        SetPhase(Phase.Observation);
    }

    // ------------------------------------------------------------------ her physics

    private float FtpWatts() => devices != null ? Mathf.Max(60f, devices.ftpWatts) : 220f;

    private void IntegrateHer(float dt, RouteCourse course)
    {
        _targetFtpPercent = PhaseFtpPercent();
        // Blended, so a phase change is a rider lifting their pace over a couple of seconds
        // rather than a step change in speed that reads as a teleport.
        float k = config.effortBlendSeconds <= 0.01f
            ? 1f : 1f - Mathf.Exp(-dt / config.effortBlendSeconds);
        _blendedFtpPercent = Mathf.Lerp(_blendedFtpPercent, _targetFtpPercent, k);

        HerWatts = FtpWatts() * _blendedFtpPercent;
        float grade = course.GradeAt(course.Wrap(HerArcM), session.gradeWindowM);
        HerSpeedMps = Mathf.Max(config.minSpeedMps,
                                 _herPhysics.Step(HerSpeedMps, HerWatts, grade, 0f, dt));
        HerArcM += HerSpeedMps * dt;
    }

    private float PhaseFtpPercent()
    {
        switch (State)
        {
            case Phase.Observation:  return config.observationFtpPercent;
            case Phase.Approach:     return config.approachFtpPercent;
            case Phase.OnWheel:      return config.wheelFtpPercent;
            case Phase.Attack:       return config.attackFtpPercent;
            case Phase.FinalHairpin: return config.hairpinFtpPercent;
            case Phase.Escape:       return config.escapeFtpPercent;
            default:                 return config.observationFtpPercent;
        }
    }

    private void PushToRider()
    {
        if (hanakage == null) return;
        hanakage.session = session;
        hanakage.arcDistanceM = HerArcM;
        hanakage.speedMps = HerSpeedMps;
        if (!hanakage.Present) hanakage.SetPresent(true);
    }

    // ------------------------------------------------------------------ phases

    private void TickObservation()
    {
        // The handoff is explicit: no banner, no health bar, no name. At most a placeholder.
        ObjectiveText = "???";
        if (GapM <= config.approachGapM || _phaseSeconds >= config.observationMaxSeconds)
            SetPhase(Phase.Approach);
    }

    private void TickApproach(float dt)
    {
        ObjectiveText = $"??? - {Mathf.Max(0f, GapM):0} m";
        if (GapM <= config.wheelGapM)
        {
            Result = Best(Result, Outcome.Caught);
            SetPhase(Phase.OnWheel);
        }
        else if (CommitDrop(dt)) BeginEscape();
    }

    private void TickOnWheel(float dt)
    {
        ObjectiveText = "STAY ON HER WHEEL";
        if (GapM <= config.wheelHoldGapM)
        {
            _onWheelSeconds += dt;
            _droppedSeconds = 0f;
        }
        else if (CommitDrop(dt)) { BeginEscape(); return; }

        if (_onWheelSeconds >= config.wheelHoldSeconds)
        {
            Say(config.lookBackLine);
            SetPhase(Phase.Attack);
        }
    }

    private void TickAttack(float dt)
    {
        ObjectiveText = "DON'T LET HER GO";
        if (GapM <= config.attackHoldGapM) { _heldSeconds += dt; _droppedSeconds = 0f; }
        else if (CommitDrop(dt)) { BeginEscape(); return; }

        if (_phaseSeconds >= config.attackSeconds)
        {
            if (_heldSeconds >= config.attackSeconds * config.attackHoldFraction)
                Result = Best(Result, Outcome.Held);
            SetPhase(Phase.FinalHairpin);
        }
    }

    private void TickHairpin(float dt)
    {
        ObjectiveText = "DON'T LET HER GO";
        if (GapM <= config.attackHoldGapM) { _heldSeconds += dt; _droppedSeconds = 0f; }
        else if (CommitDrop(dt)) { BeginEscape(); return; }

        if (_phaseSeconds >= config.hairpinSeconds)
        {
            if (_heldSeconds >= config.hairpinSeconds * config.hairpinHoldFraction)
            {
                Result = Best(Result, Outcome.Impressed);
                Say(config.hairpinLine);
            }
            BeginEscape();
        }
    }

    private void TickEscape()
    {
        ObjectiveText = "";
        // She always goes. The handoff's whole point: you do not beat her, you earn the right to
        // have been there. Two exits - out of sight up the road, or over the real summit crest.
        bool gone = GapM >= config.escapeDespawnGapM
                    || session.Course.Wrap(HerArcM) >= config.escapeArcM;
        if (gone) BeginDiscoveryWait();
    }

    private void BeginEscape()
    {
        _droppedSeconds = 0f;
        SetPhase(Phase.Escape);
    }

    private void BeginDiscoveryWait()
    {
        Despawn();
        _discoveryTimer = config.discoveryDelaySeconds;
        ObjectiveText = "";
        SetPhase(Phase.DiscoveryPending);
    }

    private void TickDiscovery(float dt)
    {
        if (DiscoveryCardSeconds > 0f)
        {
            DiscoveryCardSeconds -= dt;
            if (DiscoveryCardSeconds <= 0f)
            {
                DiscoveryCardSeconds = 0f;
                SetPhase(Phase.Complete);
            }
            return;
        }

        _discoveryTimer -= dt;
        if (_discoveryTimer > 0f) return;

        RiderJournal.RecordOutcome(RiderJournal.HanakageId, (int)Result);
        bool firstTime = RiderJournal.Discover(RiderJournal.HanakageId);
        // The card is the reward for the FIRST meeting. A later ride still records the outcome
        // but does not replay the discovery beat.
        DiscoveryCardSeconds = firstTime ? config.discoveryCardSeconds : 0.0001f;
    }

    // ------------------------------------------------------------------ housekeeping

    /// <summary>
    /// Drop detection with a grace window, so one bad pitch of road - or the second it takes to
    /// shift gear - cannot end the encounter.
    /// </summary>
    private bool CommitDrop(float dt)
    {
        if (GapM < config.droppedGapM) { _droppedSeconds = 0f; return false; }
        _droppedSeconds += dt;
        return _droppedSeconds >= config.droppedGraceSeconds;
    }

    private bool AbortIfStranded(float playerDistance, float dt)
    {
        if (session.SpeedMps < config.stallSpeedMps) _stalledSeconds += dt;
        else _stalledSeconds = 0f;

        bool pastZone = playerDistance > config.escapeArcM + config.abortPastZoneM;
        bool stalled = _stalledSeconds >= config.stallAbortSeconds;
        if (!pastZone && !stalled) return false;

        // Running off the end of the zone while she is already escaping is not a failure - she
        // simply went over the top first. The player has earned the reveal; give it to them.
        if (pastZone && !stalled && State == Phase.Escape)
        {
            BeginDiscoveryWait();
            return true;
        }

        // Progression is a guarantee, not a reward for winning (handoff section 1). If the
        // player got as far as SEEING her, the ride counts - even if they then blew up on the
        // 10 % wall and had to stop. Dropping the Journal entry here is precisely the "wasted
        // ride" the design forbids, and it is what the first self-test run actually did.
        if (Result >= Outcome.Spotted)
        {
            Despawn();
            ObjectiveText = "";
            DialogueText = "";
            _stalledSeconds = 0f;
            BeginDiscoveryWait();
            return true;
        }

        // Never strand her on the descent and never hold a stopped player hostage.
        Despawn();
        ObjectiveText = "";
        DialogueText = "";
        SetPhase(Phase.Complete);
        return true;
    }

    private void Despawn()
    {
        if (hanakage != null) hanakage.SetPresent(false);
    }

    private void Say(string line)
    {
        if (!config.dialogueEnabled || string.IsNullOrEmpty(line)) return;
        DialogueText = line;
        _dialogueSeconds = config.dialogueSeconds;
    }

    private void SetPhase(Phase next)
    {
        if (State == next) return;
        State = next;
        _phaseSeconds = 0f;
        PhaseChanged?.Invoke(next);
    }

    private static Outcome Best(Outcome a, Outcome b) => (Outcome)Mathf.Max((int)a, (int)b);

    /// <summary>
    /// A run ends when the ride restarts, the course changes, or a lap rolls over. Only then is
    /// the roll flag cleared - which is precisely what stops a player from farming the trigger
    /// by riding back and forth across it.
    ///
    /// Restart detection is by <see cref="RideSession.RunToken"/>, not by the elapsed clock
    /// running backwards. The clock is the wrong signal in both directions: a checkpoint restore
    /// or a seek that rewinds time is NOT a new run (and inferring one would hand out a free
    /// reroll of a 1-in-30 sighting), while a restart landing on the same clock value IS. The
    /// token changes if and only if <see cref="RideSession.ResetRide"/> ran.
    /// </summary>
    private void DetectRunReset()
    {
        string token = session.RunToken;
        bool restarted = !string.IsNullOrEmpty(_runToken) &&
                         !string.IsNullOrEmpty(token) &&
                         !string.Equals(_runToken, token, StringComparison.Ordinal);

        // Fallback for a session that predates run tokens (or a harness that never reset): keep
        // the old clock inference, but ONLY when there is no token to compare.
        if (string.IsNullOrEmpty(token))
            restarted = session.ElapsedSeconds + 0.001f < _lastElapsed;

        bool courseChanged = _rolled && !string.Equals(_runCourseId, session.courseId,
                                                       StringComparison.Ordinal);
        bool lapped = _rolled && session.LapIndex != _runLapIndex;
        _lastElapsed = session.ElapsedSeconds;

        if (!restarted && !courseChanged && !lapped) return;
        ResetRun();
    }

    /// <summary>Clears everything the encounter knows about the current run.</summary>
    public void ResetRun()
    {
        Despawn();
        _rolled = false;
        _runCourseId = "";
        _runLapIndex = -1;
        // Adopt the run we are now in, so restart detection is armed from the first tick rather
        // than only after the roll.
        _runToken = session != null ? session.RunToken : "";
        _phaseSeconds = _onWheelSeconds = _heldSeconds = 0f;
        _droppedSeconds = _stalledSeconds = _dialogueSeconds = 0f;
        _discoveryTimer = 0f;
        DiscoveryCardSeconds = 0f;
        GapM = HerArcM = HerSpeedMps = HerWatts = 0f;
        Result = Outcome.None;
        ObjectiveText = DialogueText = "";
        SetPhase(Phase.Dormant);
    }

    /// <summary>Pins the roll for a deterministic harness run. -1 restores UnityEngine.Random.</summary>
    public void SetRollSeed(int seed)
    {
        rollSeed = seed;
        _rng = seed < 0 ? null : new System.Random(seed);
    }
}

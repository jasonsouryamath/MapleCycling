using UnityEngine;

/// <summary>
/// The PLAYER's selectable road-cycling posture plus the existing terrain/sprint presentation.
///
/// This is the player-side counterpart of <c>HanakagePerformance</c>, which does the same job
/// for the rival. Like that component it only ever writes the additive pose modifiers on
/// <see cref="KuroBikeRig"/> ("Pose modifiers (runtime, driven by behaviour)") and never touches
/// a bone directly: <c>KuroBikeRig.SeatRider</c> rewrites hips/spine/neck/head from the bind pose
/// every LateUpdate, so anything that wrote those bones itself would either be stomped or would
/// have to fight the execution order. Additive modifiers consumed inside SeatRider cannot lose
/// that race.
///
/// Four authored base postures remain available to editor tooling:
///
///   F1 - Road Racer / Aggressive
///   F2 - Road Rider / Relaxed
///   F3 - Triathlon / Time Trial
///   F4 - Road Rider / Fitness / Very Relaxed
///
/// Production gameplay locks the player to Road Racer / Aggressive. This prevents stale local
/// preferences, alternate hotkeys, and the old automatic standing-climb overlay from replacing
/// the approved seated low-aero fit. KuroBikeRig still owns the final bone solve.
///
/// The sprint remains an optional held additive presentation. The standing climb data is retained
/// for authoring compatibility but is disabled while the gameplay-default lock is active:
///
///   CLIMB  - legacy automatic standing pose. It is not allowed to replace the shipped seated
///            low-aero player posture on inclines.
///   SPRINT - held, on <see cref="sprintKey"/>. Also out of the saddle, but the opposite
///            silhouette: forward and DOWN, torso deep, a fast tight rock. That is the SPRINTERS
///            (low aero) pose on the sheet. It takes priority over the climb for as long as it
///            is held, because a rider sprinting up a climb sprints - they do not sit tall.
///
/// Below the threshold, and with nothing held, every modifier eases back to zero, which is the
/// seated CRUISE pose the rig already produces on its own.
///
/// EVERY NUMBER HERE IS PROVISIONAL illustrative tuning. The 4 % climb threshold in particular
/// is a placeholder: the design handoff leaves gradient/difficulty policy unresolved, so it is a
/// serialized constant and not a magic literal.
/// </summary>
[DefaultExecutionOrder(40)]          // before the rig's LateUpdate consumes the modifiers
[DisallowMultipleComponent]
public class KuroRidePose : MonoBehaviour
{
    public enum CyclingPosture
    {
        RoadRacerAggressive = 0,
        RoadRiderRelaxed = 1,
        TriathlonTimeTrial = 2,
        RoadRiderFitness = 3,
    }

    [System.Serializable]
    public sealed class PostureData
    {
        [Tooltip("Human-readable UI/debug label.")]
        public string displayName;
        [Tooltip("PROVISIONAL authored offset added to KuroBikeRig.hipTiltDegrees.")]
        public float extraHipTiltDegrees;
        [Tooltip("PROVISIONAL authored offset added to KuroBikeRig.spineLeanDegrees.")]
        public float extraSpineLeanDegrees;
        [Tooltip("PROVISIONAL authored offset added to KuroBikeRig.neckLiftDegrees.")]
        public float extraNeckLiftDegrees;
        [Tooltip("PROVISIONAL authored offset added to KuroBikeRig.headLiftDegrees.")]
        public float extraHeadLiftDegrees;
        [Tooltip("PROVISIONAL authored offset added to KuroBikeRig.shoulderDropDegrees.")]
        public float extraShoulderDropDegrees;
        [Tooltip("PROVISIONAL shoulder-girdle protraction in degrees. Positive brings both " +
                 "shoulders forward around the leaned chest before arm IK.")]
        public float shoulderProtractionDegrees;
        [Tooltip("Legacy left/right clavicle trim in degrees. Kuro's shipped postures keep this " +
                 "at zero so both jersey shoulders use the same authored fit.")]
        public float shoulderProtractionBalanceDegrees;
        [Tooltip("Elbow guide distance outward from each upper-arm root, in metres.")]
        public float elbowPoleOutMetres;
        [Tooltip("Elbow guide distance down the anatomical torso frame, in metres.")]
        public float elbowPoleDownMetres;
        [Tooltip("Elbow guide distance behind the anatomical torso frame, in metres.")]
        public float elbowPoleBackMetres;
        [Tooltip("Steerer-local offset layered onto the rider's authored hand fit.")]
        public Vector3 handTargetOffset;
        [Tooltip("PROVISIONAL change to the player's authored symmetric grip spread.")]
        public float extraGripSpreadMetres;
        [Tooltip("Optional real cockpit socket replacing the left hood target.")]
        public Transform handTargetL;
        [Tooltip("Optional real cockpit socket replacing the right hood target.")]
        public Transform handTargetR;
        [Tooltip("Optional left elbow pole. Time-trial uses the physical arm-pad location.")]
        public Transform elbowPoleL;
        [Tooltip("Optional right elbow pole. Time-trial uses the physical arm-pad location.")]
        public Transform elbowPoleR;
        [Tooltip("Use elbowPoleL/R as actual elbow contact constraints, not merely plane hints.")]
        public bool constrainElbowsToTargets;
        [Tooltip("Authored desired left-hand bone orientation at the visible grip.")]
        public Transform handOrientationL;
        [Tooltip("Authored desired right-hand bone orientation at the visible grip.")]
        public Transform handOrientationR;
        [Range(0f, 0.35f)] public float upperArmTwistWeight;
        [Range(0f, 0.75f)] public float forearmTwistWeight;
        [Range(0f, 1f)] public float handOrientationWeight = 1f;
        [Range(0f, 80f)] public float maxGripTwistDegrees = 55f;
    }

    [Header("Wiring (resolved automatically)")]
    [Tooltip("Where the grade and the road speed come from. Resolved like every other ride " +
             "component: the sibling first, then the scene's single RideSession.")]
    public RideSession session;
    [Tooltip("The player's rig. Resolved from this object or its children.")]
    public KuroBikeRig rig;

    [Header("Selectable cycling postures")]
    [Tooltip("The posture saved with the scene and selected when no remembered runtime choice exists.")]
    public CyclingPosture selectedPosture = CyclingPosture.RoadRacerAggressive;
    [Tooltip("Keep the shipped player on the authored seated low-aero posture. This prevents " +
             "stale preferences, keyboard posture changes, or the automatic standing-climb " +
             "overlay from replacing the default gameplay fit.")]
    public bool lockGameplayLowAeroDefault = true;
    [Tooltip("Save the user's F1-F4 choice between play sessions.")]
    public bool rememberSelection = true;
    [Tooltip("Clear, non-conflicting runtime bindings. Course selection already owns Alpha1-Alpha4.")]
    public bool enableKeyboardSelection = true;
    public KeyCode aggressiveKey = KeyCode.F1;
    public KeyCode relaxedKey = KeyCode.F2;
    public KeyCode timeTrialKey = KeyCode.F3;
    public KeyCode fitnessKey = KeyCode.F4;

    [Tooltip("Named serialized pose data. Values are visual-fit defaults and remain provisional.")]
    public PostureData roadRacerAggressive = new PostureData();
    public PostureData roadRiderRelaxed = new PostureData();
    public PostureData triathlonTimeTrial = new PostureData();
    public PostureData roadRiderFitness = new PostureData();

    [Tooltip("Player-only aero extensions and pads. Visible only for the time-trial posture.")]
    public GameObject aeroCockpitVisual;

    [Header("Effort overlays")]
    [Tooltip("Legacy automatic out-of-saddle climb presentation. The gameplay-default lock " +
             "suppresses it so road grade cannot lift the player off the saddle.")]
    public bool enableClimbOverlay = true;
    [Tooltip("Preserves the existing held-S sprint presentation on top of the selected base posture.")]
    public bool enableSprintOverlay = true;

    // ------------------------------------------------------------------ climb
    [Header("Climb / out of saddle (provisional)")]
    [Tooltip("PROVISIONAL: gradient, as a FRACTION, at which the rider comes out of the saddle. " +
             "0.04 = 4 %.")]
    public float climbGradeThreshold = 0.04f;
    [Tooltip("PROVISIONAL: gradient at which the climbing pose is fully committed. Between the " +
             "threshold and this the pose fades in, so a road that hovers around 4 % does not " +
             "snap the rider up and down.")]
    public float climbGradeFull = 0.065f;
    [Tooltip("Metres the hips lift off the saddle at a full standing climb.")]
    public float climbRiseM = 0.085f;
    [Tooltip("Metres the hips slide forward over the bottom bracket.")]
    public float climbForwardM = 0.045f;
    [Tooltip("Peak hip roll of the side-to-side rock, degrees.")]
    public float climbRockDegrees = 7.5f;
    [Tooltip("Added to the rig's spineLeanDegrees. NEGATIVE opens the torso up, which is what " +
             "makes the climber read as TALL rather than as a deeper tuck.")]
    public float climbSpineOpenDegrees = -7f;
    [Tooltip("Rock cycles per second at climbReferenceSpeedMps. One cycle is one pedal rev.")]
    public float climbRockHz = 1.30f;
    [Tooltip("Road speed, m/s, at which climbRockHz applies. ~11 kph.")]
    public float climbReferenceSpeedMps = 3.1f;
    [Tooltip("Seconds to blend fully in or out of the climbing pose. Long enough not to pop.")]
    public float climbBlendSeconds = 0.45f;

    // ------------------------------------------------------------------ sprint
    [Header("Sprint / low aero (provisional)")]
    [Tooltip("PROVISIONAL binding: HELD for the sprinter pose. S no longer eases off - the " +
             "throttle is the up-arrow mash (see MashPedalDrive), so S is free to mean sprint.")]
    public KeyCode sprintKey = KeyCode.S;
    [Tooltip("Metres the hips lift. Lower than the climb: a sprinter is out of the saddle but " +
             "stays DOWN over the bars.")]
    public float sprintRiseM = 0.042f;
    [Tooltip("Metres the hips drive forward over the bottom bracket. More than the climb.")]
    public float sprintForwardM = 0.075f;
    [Tooltip("Peak hip roll, degrees. Tighter and faster than the climb's rock.")]
    public float sprintRockDegrees = 4.5f;
    [Tooltip("Added to the rig's spineLeanDegrees. POSITIVE drives the torso down and forward - " +
             "the low-aero SPRINTERS silhouette on the model sheet.")]
    public float sprintSpineLeanDegrees = 15f;
    public float sprintRockHz = 2.20f;
    [Tooltip("Road speed, m/s, at which sprintRockHz applies. ~29 kph.")]
    public float sprintReferenceSpeedMps = 8.0f;
    [Tooltip("Seconds to blend in or out. Short: a sprint is a snap decision.")]
    public float sprintBlendSeconds = 0.18f;

    [Header("Sprint aerodynamics (provisional, subtle)")]
    [Tooltip("Scales CyclingPhysics.cdA while the tuck is held, so the low-aero pose is worth " +
             "something rather than being pure decoration. Deliberately small - this is a " +
             "posture, not an ability. Set applySprintAero off to disable entirely.")]
    public bool applySprintAero = true;
    [Range(0.75f, 1f)] public float sprintCdaScale = 0.92f;

    // ------------------------------------------------------------------ scripted control
    /// <summary>
    /// Scripted sprint hold for the capture harness, which cannot press keys. NaN = read the
    /// keyboard as normal; >= 0.5 = held. Same pattern as <c>RouteFollower.SteerOverride</c>.
    /// </summary>
    [System.NonSerialized] public float SprintOverride = float.NaN;

    // ------------------------------------------------------------------ live state
    private float _climb;        // 0..1 blended weight
    private float _sprint;       // 0..1 blended weight
    private float _rockPhase;
    private float _baseCdA = float.NaN;
    // Versioned when the authored gameplay default changes so an old local F2 preference cannot
    // silently mask the new low-aero default on the first run after upgrading.
    private const string RememberedPostureKey = "MapleRide.Kuro.CyclingPosture.v2";

    /// <summary>0..1 weight of the standing climb pose, for anything that wants to key off it.</summary>
    public float ClimbWeight => _climb * (1f - _sprint);
    /// <summary>0..1 weight of the low-aero sprint pose.</summary>
    public float SprintWeight => _sprint;

    private void OnEnable()
    {
        Resolve();
        if (Application.isPlaying && lockGameplayLowAeroDefault)
        {
            selectedPosture = CyclingPosture.RoadRacerAggressive;
            _climb = 0f;
        }
        else if (Application.isPlaying && rememberSelection &&
                 PlayerPrefs.HasKey(RememberedPostureKey))
        {
            int remembered = PlayerPrefs.GetInt(RememberedPostureKey, (int)selectedPosture);
            if (System.Enum.IsDefined(typeof(CyclingPosture), remembered))
                selectedPosture = (CyclingPosture)remembered;
        }
        ApplySelectedPosture();
        // Cache the authored drag BEFORE anything scales it, and only while not already
        // tucked - otherwise a domain reload mid-sprint would bake the scaled value in as the
        // new baseline and the rider would get permanently, invisibly faster every reload.
        if (session != null && _sprint <= 0.001f) _baseCdA = session.physics.cdA;
    }

    private void OnDisable()
    {
        // Never leave the scene holding a sprint. This component writes into a SERIALIZED
        // physics block and a serialized rig, so anything left set here is saved into the scene.
        if (rig != null)
        {
            rig.poseStandRiseM = 0f;
            rig.poseStandForwardM = 0f;
            rig.poseStandRockDegrees = 0f;
            rig.poseExtraSpineLeanDegrees = 0f;
            rig.poseExtraHipTiltDegrees = 0f;
            rig.poseExtraNeckLiftDegrees = 0f;
            rig.poseExtraHeadLiftDegrees = 0f;
            rig.poseExtraShoulderDropDegrees = 0f;
            rig.poseShoulderProtractionDegrees = 0f;
            rig.poseShoulderProtractionBalanceDegrees = 0f;
            rig.poseElbowPoleOutMetres = 0f;
            rig.poseElbowPoleDownMetres = 0f;
            rig.poseElbowPoleBackMetres = 0f;
            rig.poseHandTargetOffset = Vector3.zero;
            rig.poseExtraGripSpreadMetres = 0f;
            rig.poseHandTargetOverrideL = null;
            rig.poseHandTargetOverrideR = null;
            rig.poseElbowPoleOverrideL = null;
            rig.poseElbowPoleOverrideR = null;
            rig.poseConstrainElbowsToTargets = false;
            rig.poseHandOrientationTargetL = null;
            rig.poseHandOrientationTargetR = null;
            rig.poseUpperArmTwistWeight = 0f;
            rig.poseForearmTwistWeight = 0f;
            rig.poseHandOrientationWeight = 0f;
        }
        if (aeroCockpitVisual != null) aeroCockpitVisual.SetActive(false);
        if (session != null && !float.IsNaN(_baseCdA)) session.physics.cdA = _baseCdA;
        _climb = _sprint = 0f;
    }

    /// <summary>Finds the session and the rig. Safe to call repeatedly.</summary>
    public void Resolve()
    {
        if (rig == null) rig = GetComponentInChildren<KuroBikeRig>(true);
        if (session == null) session = GetComponent<RideSession>();
        if (session == null)
        {
            // RideBootstrap.Resolve's pattern: the ride systems live on one scene object, so the
            // player rig reaches them through the scene's single session rather than a sibling.
            var boot = FindFirstObjectByType<RideBootstrap>();
            if (boot != null) session = boot.session;
        }
        if (session == null) session = FindFirstObjectByType<RideSession>();
    }

    private void Update()
    {
        if (Application.isPlaying && lockGameplayLowAeroDefault &&
            selectedPosture != CyclingPosture.RoadRacerAggressive)
        {
            selectedPosture = CyclingPosture.RoadRacerAggressive;
        }
        else if (Application.isPlaying && enableKeyboardSelection)
        {
            if (Input.GetKeyDown(aggressiveKey)) SelectPosture(CyclingPosture.RoadRacerAggressive);
            else if (Input.GetKeyDown(relaxedKey)) SelectPosture(CyclingPosture.RoadRiderRelaxed);
            else if (Input.GetKeyDown(timeTrialKey)) SelectPosture(CyclingPosture.TriathlonTimeTrial);
            else if (Input.GetKeyDown(fitnessKey)) SelectPosture(CyclingPosture.RoadRiderFitness);
        }
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Selects and immediately applies one of the four persistent postures. The rig still owns
    /// the final LateUpdate solve, so this is safe from Animator/IK ordering races.
    /// </summary>
    public void SelectPosture(CyclingPosture posture, bool persist = true)
    {
        if (Application.isPlaying && lockGameplayLowAeroDefault && persist)
            posture = CyclingPosture.RoadRacerAggressive;
        selectedPosture = posture;
        if (persist && Application.isPlaying && rememberSelection)
        {
            PlayerPrefs.SetInt(RememberedPostureKey, (int)posture);
            PlayerPrefs.Save();
        }
        ApplySelectedPosture();
    }

    public PostureData SelectedData
    {
        get
        {
            switch (selectedPosture)
            {
                case CyclingPosture.RoadRacerAggressive: return roadRacerAggressive;
                case CyclingPosture.TriathlonTimeTrial: return triathlonTimeTrial;
                case CyclingPosture.RoadRiderFitness: return roadRiderFitness;
                default: return roadRiderRelaxed;
            }
        }
    }

    /// <summary>Current user-facing posture label for HUD/menu integrations.</summary>
    public string ActiveDisplayName =>
        SelectedData != null && !string.IsNullOrEmpty(SelectedData.displayName)
            ? SelectedData.displayName
            : selectedPosture.ToString();

    /// <summary>Writes only the selected BASE posture. Tick adds any effort overlay afterwards.</summary>
    public void ApplySelectedPosture()
    {
        Resolve();
        if (rig == null) return;
        var p = SelectedData;
        if (p == null) return;

        rig.poseExtraHipTiltDegrees = p.extraHipTiltDegrees;
        rig.poseExtraSpineLeanDegrees = p.extraSpineLeanDegrees;
        rig.poseUseWorldAlignedSpineLean =
            selectedPosture == CyclingPosture.RoadRacerAggressive;
        rig.poseExtraNeckLiftDegrees = p.extraNeckLiftDegrees;
        rig.poseExtraHeadLiftDegrees = p.extraHeadLiftDegrees;
        rig.poseExtraShoulderDropDegrees = p.extraShoulderDropDegrees;
        rig.poseShoulderProtractionDegrees = p.shoulderProtractionDegrees;
        rig.poseShoulderProtractionBalanceDegrees = p.shoulderProtractionBalanceDegrees;
        rig.poseElbowPoleOutMetres = p.elbowPoleOutMetres;
        rig.poseElbowPoleDownMetres = p.elbowPoleDownMetres;
        rig.poseElbowPoleBackMetres = p.elbowPoleBackMetres;
        rig.poseHandTargetOffset = p.handTargetOffset;
        rig.poseExtraGripSpreadMetres = p.extraGripSpreadMetres;
        rig.poseHandTargetOverrideL = p.handTargetL;
        rig.poseHandTargetOverrideR = p.handTargetR;
        rig.poseElbowPoleOverrideL = p.elbowPoleL;
        rig.poseElbowPoleOverrideR = p.elbowPoleR;
        rig.poseConstrainElbowsToTargets = p.constrainElbowsToTargets;
        rig.poseHandOrientationTargetL = p.handOrientationL;
        rig.poseHandOrientationTargetR = p.handOrientationR;
        rig.poseUpperArmTwistWeight = Mathf.Clamp(p.upperArmTwistWeight, 0f, 0.35f);
        rig.poseForearmTwistWeight = Mathf.Clamp(p.forearmTwistWeight, 0f, 0.75f);
        rig.poseHandOrientationWeight = Mathf.Clamp01(p.handOrientationWeight);
        rig.poseMaxGripTwistDegrees = Mathf.Clamp(p.maxGripTwistDegrees, 0f, 80f);

        if (aeroCockpitVisual != null)
            aeroCockpitVisual.SetActive(selectedPosture == CyclingPosture.TriathlonTimeTrial);
    }

    /// <summary>
    /// Advances the pose by one step. Exposed (rather than living inside Update) for exactly the
    /// same reason <c>RideSession.Tick</c> and <c>HanakagePerformance.Tick</c> are: nothing in
    /// the editor calls Update, so a capture harness has to drive the presentation layer by hand
    /// - driver first, THEN <c>KuroBikeRig.LateUpdate</c>, or the modifiers are written and never
    /// consumed and the render shows a perfectly seated rider on a 9 % wall.
    /// </summary>
    public void Tick(float dt)
    {
        if (rig == null || session == null) { Resolve(); if (rig == null) return; }
        if (dt <= 0f) dt = 0f;

        // ---- targets -----------------------------------------------------------------------
        bool sprintHeld = enableSprintOverlay && (float.IsNaN(SprintOverride)
            ? (Application.isPlaying && Input.GetKey(sprintKey))
            : SprintOverride >= 0.5f);

        float grade = session != null ? session.Grade : 0f;
        float span = Mathf.Max(0.001f, climbGradeFull - climbGradeThreshold);
        float climbTarget = enableClimbOverlay && !lockGameplayLowAeroDefault
            ? Mathf.Clamp01((grade - climbGradeThreshold) / span)
            : 0f;

        _climb = Mathf.MoveTowards(_climb, climbTarget,
                                   dt / Mathf.Max(0.01f, climbBlendSeconds));
        _sprint = Mathf.MoveTowards(_sprint, sprintHeld ? 1f : 0f,
                                    dt / Mathf.Max(0.01f, sprintBlendSeconds));

        // The sprint OWNS the silhouette while it is held: a rider attacking a climb is in the
        // sprinter's tuck, not sitting tall. Cross-fading the two instead would average a tall
        // pose and a low one into a shrug.
        float c = _climb * (1f - _sprint);
        float s = _sprint;

        // ---- rock at pedal frequency -------------------------------------------------------
        float speed = session != null ? Mathf.Abs(session.SpeedMps) : 0f;
        float climbHz = climbRockHz * Mathf.Clamp(speed / Mathf.Max(0.01f, climbReferenceSpeedMps),
                                                  0.4f, 1.8f);
        float sprintHz = sprintRockHz * Mathf.Clamp(speed / Mathf.Max(0.01f, sprintReferenceSpeedMps),
                                                    0.5f, 1.6f);
        float hz = Mathf.Lerp(climbHz, sprintHz, s);
        _rockPhase += dt * hz * Mathf.PI * 2f;
        if (_rockPhase > Mathf.PI * 2f) _rockPhase -= Mathf.PI * 2f;

        // ---- write the additive modifiers --------------------------------------------------
        rig.poseStandRiseM = climbRiseM * c + sprintRiseM * s;
        rig.poseStandForwardM = climbForwardM * c + sprintForwardM * s;
        rig.poseStandRockDegrees = (climbRockDegrees * c + sprintRockDegrees * s)
                                   * Mathf.Sin(_rockPhase);
        // Re-apply the selected base every tick. This is deliberate: another behaviour or an
        // Animator can write the rig's serialized modifier fields, but the posture owner restores
        // its complete state before KuroBikeRig consumes it in LateUpdate.
        var selected = SelectedData;
        float baseSpine = selected != null ? selected.extraSpineLeanDegrees : 0f;
        ApplySelectedPosture();
        rig.poseExtraSpineLeanDegrees = baseSpine
                                      + climbSpineOpenDegrees * c
                                      + sprintSpineLeanDegrees * s;

        // ---- low-aero tuck -----------------------------------------------------------------
        if (session != null)
        {
            if (float.IsNaN(_baseCdA)) _baseCdA = session.physics.cdA;
            session.physics.cdA = applySprintAero
                ? _baseCdA * Mathf.Lerp(1f, sprintCdaScale, s)
                : _baseCdA;
            // Recomputed from the CACHED baseline every frame rather than scaled in place, so
            // repeated sprints cannot compound the drag down towards zero.
        }
    }
}

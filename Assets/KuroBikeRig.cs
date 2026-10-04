using UnityEngine;

/// <summary>
/// Attaches the procedural bike to Kuro and rides it.
///
/// This is the consumer half of the contract that build_kuro_bike.py publishes. The bike is
/// authored around Kuro's measured limb lengths and ships named empties at every point where he
/// touches it, so nothing here has to *search* for anything the way fit_meshy_bike.py did:
///
///     BB          crank spin axis
///     Crank_L/R   crank arms, 180 deg apart, children of BB
///     Pedal_L/R   pedal platforms, children of the cranks  -> foot IK targets
///     Hood_L/R    brake hoods                              -> hand IK targets
///     SaddleTop   where the hips sit
///     SteerPivot  parent of fork, bars and front wheel
///     Axle_F/R    wheel spin axes
///
/// Because the pedal sockets are children of the cranks, the feet are IK'd to something that is
/// already turning with the drivetrain. "Feet drifting off the pedals" is not a bug that can be
/// fixed here - it is a state this rig cannot represent.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[DefaultExecutionOrder(100)] // RouteFollower moves the bike at 50; solve Kuro on that same frame.
public class KuroBikeRig : MonoBehaviour
{
    [Header("Bike")]
    public GameObject bikePrefab;
    [Tooltip("Blender authored the wheels resting on z=0, so a zero offset already grounds them.")]
    public Vector3 bikeLocalOffset = Vector3.zero;
    public float wheelRadius = 0.175f;
    [Tooltip("Crank revolutions per wheel revolution. Lower = harder gear = slower legs.")]
    public float gearRatio = 2.8f;
    [Tooltip("Fallback crank cadence while stopped, so the riding pose is easy to preview.")]
    public float previewCadenceRpm = 0f;
    [Tooltip("Player-only. Drive the crank from the ride's cadence telemetry so pedal animation " +
             "continues at a stable angular speed between input samples. Wheels still follow " +
             "travelled road distance. Disabled by default so ambient riders are unchanged.")]
    public bool useRideCadenceForCrank = false;
    public RideSession rideSession;
    [Tooltip("Seconds used to blend cadence changes before they reach the crank. A short blend " +
             "removes visible phase jumps without making Kuro slow to respond.")]
    [Min(0.01f)] public float crankCadenceSmoothingSeconds = 0.22f;

    [Header("Riding pose")]
    public float hipTiltDegrees = 24f;
    public float spineLeanDegrees = 34f;
    [Tooltip("Neck/head counter-rotation that cancels the torso pitch so the rider looks at " +
             "the road. Hips + spine pitch the chest forward and the head inherits all of it, " +
             "so these should roughly sum to -(hipTilt + spineLean). Chibi NPCs with a large " +
             "head mass need noticeably more than the default.")]
    public float neckLiftDegrees = -7f;
    public float headLiftDegrees = -11f;
    [Tooltip("Symmetric DROP of the collarbones (LeftShoulder/RightShoulder), degrees, about the " +
             "bike's forward axis. The seated spine lean rounds the upper back up around the neck, " +
             "so on a big-headed chibi the head reads as buried between the shoulders in the chase " +
             "cam. Dropping the shoulders lowers the arm roots (the arm IK then re-reaches the " +
             "hoods) and clears the neck. DEFAULTS TO ZERO so every existing rider is unchanged; " +
             "opt in per rider (the player sets it in KuroBikeRefit).")]
    public float shoulderDropDegrees = 0f;
    [Tooltip("Sideways shift of the rider on the saddle, in metres, along the bike's +X " +
             "(the rider's right). Seating puts the Hips bone on SaddleTop, which only " +
             "centres the rider if the sculpt happens to be centred on that bone - Coral's " +
             "is not. Measured by the CENTERING check in fit_coral_to_bike.py.")]
    public float riderLateralOffset = 0f;
    [Tooltip("Ankle height above the pedal platform.")]
    public float ankleHeight = 0.045f;
    [Tooltip("Moves the ankle behind the pedal spindle so the spindle sits under the forefoot.")]
    public float footTargetRearwardOffset = 0f;
    [Tooltip("Per-side mesh seating trims applied after the common ankle height.")]
    public float footHeightTrimL = 0f;
    public float footHeightTrimR = 0f;
    [Tooltip("Cycling shoe pitch relative to the level pedal platform.")]
    public float footToeDownDegrees = 0f;
    [Tooltip("Mirrored shoe roll toward each pedal platform.")]
    public float footRollDegrees = 0f;
    [Tooltip("Outboard separation of the left/right knee pole guides.")]
    public float kneePoleLateralOffset = 0f;
    [Tooltip("Keep the pedal platforms level as the cranks turn, instead of letting them " +
             "spin round with the crank arms. Real pedals are free-turning platforms: they " +
             "orbit the bottom bracket but stay flat. Leaving them rigid also flips the " +
             "pedal's local up vector through the stroke, which makes the ankle IK target " +
             "(pedal.up * ankleHeight) swap sign and sinks the foot THROUGH the pedal at the " +
             "top of the revolution. Defaults to false so every existing rider is unchanged; " +
             "opt in per rider.")]
    public bool keepPedalsLevel = false;
    public float steerVisualDegrees = 18f;
    [System.NonSerialized]
    public float SteerOverrideDegrees = float.NaN;

    // ------------------------------------------------------------------ pose modifiers
    // Written every frame by a behaviour driver (e.g. HanakagePerformance) and consumed by
    // SeatRider. They exist as fields on the rig rather than as a separate LateUpdate component
    // because SeatRider OVERWRITES hips/spine/neck/head from the bind pose every frame - a
    // driver that wrote those bones itself would either be stomped or would have to fight the
    // execution order. Additive offsets consumed inside SeatRider cannot lose that race.
    // All default to zero, so every existing rider is bit-identical to before.

    [Header("Pose modifiers (runtime, driven by behaviour)")]
    [Tooltip("Extra head yaw about the rider's up axis, degrees. Positive looks over the LEFT " +
             "shoulder (riders keep left in Japan, so that is the shoulder a rider glances " +
             "over to see who is on their wheel). Drives the look-back beat.")]
    public float poseHeadYawDegrees = 0f;
    [Tooltip("Extra head roll about the rider's forward axis, degrees. A look-back is not a " +
             "pure yaw; the head tips as it turns, and without this it reads robotic.")]
    public float poseHeadRollDegrees = 0f;
    [Tooltip("Metres the hips rise off the saddle. This is what makes a standing climb read " +
             "as standing: out of the saddle, the rider's mass goes UP and forward.")]
    public float poseStandRiseM = 0f;
    [Tooltip("Metres the hips move forward over the bottom bracket while standing.")]
    public float poseStandForwardM = 0f;
    [Tooltip("Hip roll about the bike's forward axis, degrees. Signed, oscillated by the " +
             "driver at pedal frequency - this is the side-to-side rock of a standing effort.")]
    public float poseStandRockDegrees = 0f;
    [Tooltip("Extra spine lean added on top of spineLeanDegrees, degrees. Negative opens the " +
             "torso up, which is what a rider does when they come out of the saddle.")]
    public float poseExtraSpineLeanDegrees = 0f;
    [Tooltip("Apply this posture's spine pitch around the bicycle lateral axis so imported " +
             "bone-axis skew cannot create different left/right torso silhouettes.")]
    public bool poseUseWorldAlignedSpineLean;
    [Tooltip("Fraction of the look-back yaw/roll taken by the NECK rather than the head. " +
             "0 = head only, 1 = neck only.")]
    [Range(0f, 1f)] public float poseNeckYawShare = 0.45f;

    // ---------------------------------------------------------------- posture modifiers
    // Added for the four selectable riding POSTURES (KuroRidingPosture / KuroRidePose). They
    // follow exactly the same contract as the modifiers above: additive, written by a driver
    // that runs BEFORE this component's LateUpdate, consumed inside SeatRider/SolveLimbs, and
    // ALL DEFAULTING TO ZERO so every existing rider - player and NPC - is bit-identical until
    // something opts in. They exist because a posture is not just a spine angle: it is the
    // spine, the hip hinge, the head/neck counter-rotation that keeps the eyes on the road, the
    // shoulder line and WHERE THE HANDS ARE, and each of those previously lived only in a
    // serialized per-rider tune (spineLeanDegrees, neckLiftDegrees, handTargetLocalOffset...)
    // that a runtime driver must not overwrite - overwriting it would make the authored fit
    // unrecoverable after the first posture change.

    [Tooltip("Extra hip hinge added on top of hipTiltDegrees, degrees. POSITIVE pitches the " +
             "pelvis forward, which is the hinge a real rider makes to get low without simply " +
             "rounding their back. Kuro's rebuild freezes hipTiltDegrees at 0, so this is the " +
             "only hip rotation in the posture system and it is used sparingly.")]
    public float poseExtraHipTiltDegrees = 0f;
    [Tooltip("Added to neckLiftDegrees. NEGATIVE lifts the gaze. A deeper torso needs more " +
             "counter-rotation here or the rider ends up staring at their own front tyre.")]
    public float poseExtraNeckLiftDegrees = 0f;
    [Tooltip("Added to headLiftDegrees. See poseExtraNeckLiftDegrees.")]
    public float poseExtraHeadLiftDegrees = 0f;
    [Tooltip("Added to shoulderDropDegrees. Only meaningful on a rider that already opts in to " +
             "a non-zero shoulderDropDegrees (the player does); SeatRider skips the collarbones " +
             "entirely when the total is zero, so an NPC at 0 stays untouched.")]
    public float poseExtraShoulderDropDegrees = 0f;

    // ---- Dismount overlay (runtime only, driven by KuroDismount for the Maple Row shop). All zero
    // = the normal seated solve, so nothing else is affected.
    /// <summary>World-space shift of the hips on top of the seat position (step off to the side).</summary>
    [System.NonSerialized] public Vector3 poseHipWorldOffset;
    /// <summary>World-space foot goals and how much each replaces its pedal target (0 = on the
    /// pedal, 1 = fully on the goal).</summary>
    [System.NonSerialized] public Vector3 poseFootGoalL, poseFootGoalR;
    [System.NonSerialized] public float poseFootGoalWeightL, poseFootGoalWeightR;
    /// <summary>Heel-out twist about the bike's up axis: the clip-out motion of an SPD-SL cleat.</summary>
    [System.NonSerialized] public float poseHeelOutDegreesL, poseHeelOutDegreesR;
    /// <summary>Straight-line hip-to-ankle length at the bind pose (for standing heights).</summary>
    public float LegLength => upLegL && legL && footL
        ? Vector3.Distance(upLegL.position, legL.position) + Vector3.Distance(legL.position, footL.position) : 0.5f;
    /// <summary>0 = riding posture, 1 = standing upright (hip tilt, spine lean and neck/head
    /// lift fade out). Scales the TOTAL, so it also cancels whatever KuroRidePose adds.</summary>
    [System.NonSerialized] public float poseUprightBlend;
    public Transform Hips => hips;
    public Transform SaddleTop => saddleTop;

    [Tooltip("Added to handTargetLocalOffset, in the steerer's LOCAL space. This is how a " +
             "posture moves the hands between the tops, the hoods and the drops WITHOUT " +
             "disturbing the per-rider grip fit that handTargetLocalOffset encodes.")]
    public Vector3 poseHandTargetOffset = Vector3.zero;
    [Tooltip("Added to gripSpreadMetres (mirrored outboard). NEGATIVE narrows the hands, which " +
             "is what moving from the hoods onto the bar TOPS actually looks like.")]
    public float poseExtraGripSpreadMetres = 0f;

    [Tooltip("When set, the LEFT hand IK aims at this transform instead of Hood_L, and every " +
             "hand offset above is ignored for that hand. Used by the aero posture, where the " +
             "hands hold aero-bar extensions that are a real object in the scene rather than an " +
             "offset from the brake hood. Null on every other rider and every other posture.")]
    public Transform poseHandTargetOverrideL;
    [Tooltip("Right-hand counterpart of poseHandTargetOverrideL.")]
    public Transform poseHandTargetOverrideR;
    [Tooltip("When set, the arm elbow POLE hints aim at these transforms instead of the default " +
             "splayed hints. The aero posture uses them to drive the elbows down and inboard " +
             "onto the arm pads instead of letting them splay wide.")]
    public Transform poseElbowPoleOverrideL;
    [Tooltip("Right-hand counterpart of poseElbowPoleOverrideL.")]
    public Transform poseElbowPoleOverrideR;

    /// <summary>Solved left elbow (forearm root). Exposed so a fit pass can put an arm pad
    /// where the elbow ACTUALLY lands rather than where it was assumed to land.</summary>
    public Transform ElbowL => foreArmL;
    /// <summary>Solved right elbow (forearm root).</summary>
    public Transform ElbowR => foreArmR;
    /// <summary>Solved left wrist.</summary>
    public Transform WristL => handL;
    /// <summary>Solved right wrist.</summary>
    public Transform WristR => handR;
    /// <summary>The bike's steerer, for expressing cockpit offsets in a scale-correct frame.</summary>
    public Transform SteerPivot => steerPivot;
    /// <summary>The left brake hood socket.</summary>
    public Transform HoodL => hoodL;
    /// <summary>The right brake hood socket.</summary>
    public Transform HoodR => hoodR;
    /// <summary>Current continuous crank phase, exposed read-only for the opt-in smoothness harness.</summary>
    public float CrankAngleDegrees => crankAngle;
    /// <summary>Cadence actually integrated by the player crank after the short packet-smoothing filter.</summary>
    public float SmoothedCadenceRpm => smoothedCadenceRpm;
    /// <summary>Live pedal contacts used by the smoothness harness without hierarchy searches.</summary>
    public Transform PedalL => pedalL;
    public Transform PedalR => pedalR;
    /// <summary>Live foot bones used by the smoothness harness without hierarchy searches.</summary>
    public Transform FootL => footL;
    public Transform FootR => footR;
    public Vector3 FootTargetL => FootTarget(pedalL, true);
    public Vector3 FootTargetR => FootTarget(pedalR, false);

    /// <summary>
    /// Editor staging hook: the imported character prefab owns the trustworthy unposed
    /// clavicle rotations. Persist them relative to each chest parent so scene-saved riding
    /// poses can never be mistaken for a new bind on a later domain reload.
    /// </summary>
    public void SetShoulderParentBinds(Quaternion leftLocalBind, Quaternion rightLocalBind)
    {
        savedShoulderLParentBind = leftLocalBind;
        savedShoulderRParentBind = rightLocalBind;
        shoulderParentBindCaptured = true;
    }

    public float ShoulderParentBindDeviation(Transform shoulder)
    {
        if (shoulder == shoulderL)
            return Quaternion.Angle(savedShoulderLParentBind, shoulder.localRotation);
        if (shoulder == shoulderR)
            return Quaternion.Angle(savedShoulderRParentBind, shoulder.localRotation);
        return float.PositiveInfinity;
    }

    [Tooltip("Offset added to the Hood_L/Hood_R hand IK targets, in the steerer's LOCAL space " +
             "(so it scales with the bike). The bike only ships hood sockets, but a rider can " +
             "also be down on the drops - that is a hand position, not a different bike, so it " +
             "is expressed as an offset from the hood rather than by re-authoring the bar. " +
             "Symmetric, so no per-side mirroring is needed. DEFAULTS TO ZERO: every existing " +
             "rider keeps the hood position it was tuned at, bit-identical.")]
    public Vector3 handTargetLocalOffset = Vector3.zero;
    [Tooltip("Player-only. Pushes each hand IK target OUTBOARD along the bar axis by this many " +
             "metres (mirrored: left hand +X, right hand -X in the steerer frame). The baked glove " +
             "mesh hangs ~0.09 m INBOARD of the wrist bone, so aiming the wrist at the hood leaves " +
             "the visible glove bunched toward the stem; spreading the targets outboard lands the " +
             "GLOVE (not just the wrist) on the hood. DEFAULTS TO ZERO so every NPC is unchanged.")]
    public float gripSpreadMetres = 0f;
    [Tooltip("Player-only. Per-hand fine seating offset (steerer LOCAL space) added to the LEFT hand " +
             "IK target. Used to drop the visible GLOVE centroid onto the hood when the baked glove " +
             "hangs off the wrist in a non-axis-aligned direction; solved iteratively by the fix " +
             "harness. DEFAULTS TO ZERO so every NPC is unchanged.")]
    public Vector3 handSeatOffsetL = Vector3.zero;
    [Tooltip("Player-only. Per-hand fine seating offset (steerer LOCAL space) added to the RIGHT " +
             "hand IK target. See handSeatOffsetL. DEFAULTS TO ZERO so every NPC is unchanged.")]
    public Vector3 handSeatOffsetR = Vector3.zero;
    [Tooltip("Lateral sign of the elbow POLE hints. +1 keeps the legacy inward tuck (the left " +
             "elbow points toward the rider's right and vice-versa) which buries the elbows behind " +
             "the spine and hides the arms from directly behind. -1 SPLAYS the elbows OUTBOARD like " +
             "a real road cyclist reaching the hoods, so the upper arms/forearms read outside the " +
             "torso silhouette from behind. Does NOT move the hands - the hood targets are unchanged, " +
             "so the baked grip is preserved. DEFAULTS TO +1 so every existing NPC is bit-identical; " +
             "the player opts in to -1 via KuroBikeRefit / the fix harness.")]
    public float armElbowPoleSign = 1f;

    [Header("Anatomical arm solve (player opt-in)")]
    [Tooltip("Uses shoulder-origin, torso-frame elbow guides, chest-relative clavicle binds, " +
             "palm-aware grip rotations, and optional real elbow contacts. False preserves every NPC.")]
    public bool useAnatomicalArmSolver = false;
    [Tooltip("Posture-authored shoulder protraction. Positive rolls both clavicles forward with the chest.")]
    public float poseShoulderProtractionDegrees = 0f;
    [Tooltip("Legacy posture-specific left/right clavicle trim. Kuro's player postures keep " +
             "this at zero so the jersey shoulder line remains even.")]
    public float poseShoulderProtractionBalanceDegrees = 0f;
    [Tooltip("Posture elbow guide offset OUTWARD from the upper-arm root.")]
    public float poseElbowPoleOutMetres = 0f;
    [Tooltip("Posture elbow guide offset DOWN the anatomical torso frame.")]
    public float poseElbowPoleDownMetres = 0f;
    [Tooltip("Posture elbow guide offset BACK from the cockpit.")]
    public float poseElbowPoleBackMetres = 0f;
    [Tooltip("Treat poseElbowPoleOverrideL/R as actual elbow contact points.")]
    public bool poseConstrainElbowsToTargets = false;
    [Tooltip("Desired left-hand bone orientation at the visible grip.")]
    public Transform poseHandOrientationTargetL;
    public Transform poseHandOrientationTargetR;
    [Range(0f, 0.35f)] public float poseUpperArmTwistWeight = 0f;
    [Range(0f, 0.75f)] public float poseForearmTwistWeight = 0f;
    [Range(0f, 1f)] public float poseHandOrientationWeight = 0f;
    [Range(0f, 80f)] public float poseMaxGripTwistDegrees = 55f;

    [Header("Bone names (glTF rig)")]
    public string hipsName = "Hips";
    public string[] spineNames = { "Spine", "Spine01", "Spine02" };

    Transform bike, bbSocket, steerPivot, axleF, axleR;
    Transform pedalL, pedalR, hoodL, hoodR, saddleTop, crankL, crankR;
    Transform hips;
    Transform[] spine;
    Transform neck, head;
    Transform upLegL, legL, footL, upLegR, legR, footR;
    Transform armL, foreArmL, handL, armR, foreArmR, handR;
    Transform shoulderL, shoulderR;

    Quaternion hipsBind;
    Quaternion[] spineBind;
    Vector3 lastPos;
    float crankAngle, wheelAngle, steerAngle;
    float smoothedCadenceRpm;
    bool ready;
    Quaternion crankLBind = Quaternion.identity, crankRBind = Quaternion.identity;

    // Serialized so the bind pose is captured exactly once, from the UNPOSED rig. Re-capturing
    // it on every enable would read back a pose this component itself wrote and compound the
    // lean a little further every domain reload.
    [HideInInspector] [SerializeField] bool bindCaptured;
    [HideInInspector] [SerializeField] Quaternion savedHipsBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion[] savedSpineBind;
    [HideInInspector] [SerializeField] Quaternion savedNeckBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedHeadBind = Quaternion.identity;
    // Collarbone binds, captured rig-relative (like hips) from the UNPOSED shoulders so the drop
    // in SeatRider is an offset from the natural pose, not from this component's own output.
    [HideInInspector] [SerializeField] bool shoulderBindCaptured;
    [HideInInspector] [SerializeField] Quaternion savedShoulderLBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedShoulderRBind = Quaternion.identity;
    [HideInInspector] [SerializeField] bool shoulderParentBindCaptured;
    [HideInInspector] [SerializeField] Quaternion savedShoulderLParentBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedShoulderRParentBind = Quaternion.identity;
    [HideInInspector] [SerializeField] bool armBindCaptured;
    [HideInInspector] [SerializeField] Quaternion savedArmLLocalBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedForeArmLLocalBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedHandLLocalBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedArmRLocalBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedForeArmRLocalBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedHandRLocalBind = Quaternion.identity;
    // Pedal platform bind orientations, captured from the unposed bike for the same reason as
    // the spine binds: LevelPedals writes these transforms, so re-reading them later would
    // read back this component's own output.
    [HideInInspector] [SerializeField] bool pedalBindCaptured;
    [HideInInspector] [SerializeField] Quaternion savedPedalLBind = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedPedalRBind = Quaternion.identity;
    [HideInInspector] [SerializeField] bool footPedalBindCaptured;
    [HideInInspector] [SerializeField] Quaternion savedFootLToPedal = Quaternion.identity;
    [HideInInspector] [SerializeField] Quaternion savedFootRToPedal = Quaternion.identity;

    void OnEnable()
    {
        Setup();
    }

    void Start()
    {
        Setup();
    }

    void Setup()
    {
        if (bike == null) bike = Find(transform, "Bike");
        // Deliberately NO global GameObject.Find("Bike") fallback. Twelve objects in SakuraPass
        // are named exactly "Bike" (one per rider plus the player's), so a global lookup returns
        // whichever the scene happens to list first and can hand this rider the player's
        // drivetrain. The bike must be a child named exactly "Bike" - that is the hierarchy
        // contract every staging pass already honours.
        if (bike == null && bikePrefab != null && Application.isPlaying)
        {
            bike = Instantiate(bikePrefab, transform).transform;
            bike.name = "Bike";
            bike.localPosition = bikeLocalOffset;
            bike.localRotation = Quaternion.identity;
        }

        ready = Resolve();
        if (!ready) return;

        if (!bindCaptured)
        {
            savedHipsBind = Quaternion.Inverse(transform.rotation) * hips.rotation;
            savedSpineBind = new Quaternion[spine.Length];
            for (int i = 0; i < spine.Length; i++) savedSpineBind[i] = spine[i].localRotation;
            savedNeckBind = neck ? neck.localRotation : Quaternion.identity;
            savedHeadBind = head ? head.localRotation : Quaternion.identity;
            bindCaptured = true;
        }
        if (!pedalBindCaptured)
        {
            savedPedalLBind = pedalL ? pedalL.localRotation : Quaternion.identity;
            savedPedalRBind = pedalR ? pedalR.localRotation : Quaternion.identity;
            pedalBindCaptured = true;
        }
        if (!footPedalBindCaptured)
        {
            savedFootLToPedal = pedalL && footL
                ? Quaternion.Inverse(pedalL.rotation) * footL.rotation
                : Quaternion.identity;
            savedFootRToPedal = pedalR && footR
                ? Quaternion.Inverse(pedalR.rotation) * footR.rotation
                : Quaternion.identity;
            footPedalBindCaptured = true;
        }
        if (!shoulderBindCaptured)
        {
            savedShoulderLBind = shoulderL ? Quaternion.Inverse(transform.rotation) * shoulderL.rotation : Quaternion.identity;
            savedShoulderRBind = shoulderR ? Quaternion.Inverse(transform.rotation) * shoulderR.rotation : Quaternion.identity;
            shoulderBindCaptured = true;
        }
        if (useAnatomicalArmSolver && !shoulderParentBindCaptured)
        {
            CaptureShoulderParentBinds();
            shoulderParentBindCaptured = true;
        }
        if (useAnatomicalArmSolver && !armBindCaptured)
        {
            SetArmLocalBinds(
                armL.localRotation, foreArmL.localRotation, handL.localRotation,
                armR.localRotation, foreArmR.localRotation, handR.localRotation);
        }
        // Preserve the authored 180-degree crank phase, then drive both crank sockets from one
        // angle. This makes the alternating stroke explicit instead of relying on imported
        // local Euler values (which can collapse to the same phase after GLB conversion).
        crankLBind = Quaternion.identity;
        crankRBind = Quaternion.AngleAxis(180f, Vector3.right);
        hipsBind = savedHipsBind;
        spineBind = savedSpineBind;
        if (spineBind == null || spineBind.Length != spine.Length) { ready = false; return; }

        lastPos = transform.position;
    }

    bool Resolve()
    {
        if (bike == null) { Debug.LogError("KuroBikeRig: no bike instance - assign bikePrefab."); return false; }

        bbSocket = Socket("BB"); steerPivot = Socket("SteerPivot");
        axleF = Socket("Axle_F"); axleR = Socket("Axle_R");
        crankL = Socket("Crank_L"); crankR = Socket("Crank_R");
        pedalL = Socket("Pedal_L"); pedalR = Socket("Pedal_R");
        hoodL = Socket("Hood_L"); hoodR = Socket("Hood_R");
        saddleTop = Socket("SaddleTop");

        hips = Bone(hipsName);
        spine = new Transform[spineNames.Length];
        for (int i = 0; i < spineNames.Length; i++) spine[i] = Bone(spineNames[i]);

        upLegL = Bone("LeftUpLeg"); legL = Bone("LeftLeg"); footL = Bone("LeftFoot");
        upLegR = Bone("RightUpLeg"); legR = Bone("RightLeg"); footR = Bone("RightFoot");
        armL = Bone("LeftArm"); foreArmL = Bone("LeftForeArm"); handL = Bone("LeftHand");
        armR = Bone("RightArm"); foreArmR = Bone("RightForeArm"); handR = Bone("RightHand");
        // Optional (no warning if a rig lacks them): only consumed when shoulderDropDegrees != 0.
        shoulderL = Find(transform, "LeftShoulder");
        shoulderR = Find(transform, "RightShoulder");
        neck = Bone("neck");
        head = Bone("Head");

        bool ok = bbSocket && axleF && axleR && pedalL && pedalR && hoodL && hoodR && saddleTop
                  && hips && upLegL && legL && footL && upLegR && legR && footR
                  && armL && foreArmL && handL && armR && foreArmR && handR;
        for (int i = 0; i < spine.Length; i++) ok &= spine[i] != null;
        if (!ok) Debug.LogError("KuroBikeRig: socket or bone lookup failed - see warnings above.");
        return ok;
    }

    Transform Socket(string n)
    {
        var t = Find(bike, n);
        if (t == null) Debug.LogWarning("KuroBikeRig: missing bike socket '" + n + "'");
        return t;
    }

    Transform Bone(string n)
    {
        var t = Find(transform, n);
        if (t == null) Debug.LogWarning("KuroBikeRig: missing rider bone '" + n + "'");
        return t;
    }

    static Transform Find(Transform root, string n)
    {
        if (root == null) return null;
        if (root.name == n) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = Find(root.GetChild(i), n);
            if (r != null) return r;
        }
        return null;
    }

    // LateUpdate so this runs after KuroKeyboardController has moved the transform, and after
    // any Animator would have written the bind pose back over the bones.
    /// <summary>
    /// LOD hook, driven by ShiosaiRiderLod. 1 = solve every frame (the default, and what the
    /// player's own rig always uses). Higher values skip frames for distant ambient riders:
    /// thirty rigs solving a full limb IK pass every frame is measurable, and at a hundred
    /// metres nobody can see a pedal stroke. Never toggle the COMPONENT off instead - Setup()
    /// is a recursive name search over the whole rig and re-running it is far more expensive
    /// than the solve it would save.
    /// </summary>
    [HideInInspector] public int solveEveryNFrames = 1;
    /// <summary>Phase offset so a pool of riders does not all solve on the same frame.</summary>
    [HideInInspector] public int solvePhase = 0;

    void LateUpdate()
    {
        if (!ready) return;

        if (solveEveryNFrames > 1 &&
            (Time.frameCount + solvePhase) % solveEveryNFrames != 0) return;

        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        float signed = Vector3.Dot(delta, transform.forward);

        DriveDrivetrain(signed);
        Steer();
        SeatRider();
        SolveLimbs();
    }

    /// <summary>
    /// Runs one solve pass (seat + limb IK, no drivetrain distance) on demand, without waiting
    /// for LateUpdate. [ExecuteAlways] makes Setup() run on load even outside Play mode, but
    /// LateUpdate is NOT reliably ticked in a headless `-batchmode -quit -executeMethod` run -
    /// there is no player loop pumping frames, so a rider that is only ever
    /// SetActive(true)'d by an edit-time tool (e.g. Shiosai's pooled traffic, posed at runtime
    /// by ShiosaiTrafficDirector and otherwise left at its raw bind pose in the saved scene)
    /// never actually gets seated on the bike before something reads its bones - which is
    /// exactly what made portraits baked from that pool look wrong despite correct materials.
    /// Idempotent and safe to call every time before a one-off capture.
    /// </summary>
    public void ForceSolveOnce()
    {
        if (!ready) Setup();
        if (!ready) return;

        Steer();
        SeatRider();
        SolveLimbs();
    }

    /// <summary>
    /// Advances the drivetrain by a crank angle rather than by travelled distance, and re-solves.
    ///
    /// Exists for POSE CAPTURE. The normal input is distance, so a headless one-off capture -
    /// where nothing ever moves - always renders the same crank phase, which cannot show whether
    /// the pedal stroke actually reads. Faking it by nudging the rider forward is not equivalent:
    /// LateUpdate derives distance from transform.position, so moving the rider back afterwards
    /// would wind the crank straight back to where it started, and leaving it moved would change
    /// the camera framing between phases. This advances the phase in place and leaves position,
    /// lastPos and every other drivetrain input untouched.
    /// </summary>
    public void AdvanceCrank(float degrees)
    {
        if (!ready) Setup();
        if (!ready) return;

        crankAngle += degrees;
        // Wheels turn with the cranks through the gear, so the tyres stay consistent with the legs.
        wheelAngle += degrees * gearRatio;
        if (axleF) axleF.localRotation = Quaternion.AngleAxis(wheelAngle, Vector3.right);
        if (axleR) axleR.localRotation = Quaternion.AngleAxis(wheelAngle, Vector3.right);
        if (bbSocket) bbSocket.localRotation = Quaternion.identity;
        if (crankL) crankL.localRotation = Quaternion.AngleAxis(crankAngle, Vector3.right) * crankLBind;
        if (crankR) crankR.localRotation = Quaternion.AngleAxis(crankAngle, Vector3.right) * crankRBind;

        LevelPedals();

        Steer();
        SeatRider();
        SolveLimbs();
    }

    void DriveDrivetrain(float distance)
    {
        if (wheelRadius <= 0.0001f) return;

        float wheelDeg = distance / wheelRadius * Mathf.Rad2Deg;
        // Keep a visible, slow pedal cycle in Play mode even before keyboard distance changes.
        // Once the rider moves, travelled distance remains the authoritative drivetrain input.
        if (Application.isPlaying && previewCadenceRpm > 0f && Mathf.Abs(distance) < 0.00001f)
            wheelDeg = (previewCadenceRpm / 60f) * 360f * gearRatio * Time.deltaTime;
        wheelAngle += wheelDeg;
        if (axleF) axleF.localRotation = Quaternion.AngleAxis(wheelAngle, Vector3.right);
        if (axleR) axleR.localRotation = Quaternion.AngleAxis(wheelAngle, Vector3.right);

        float crankDeg = wheelDeg / Mathf.Max(0.01f, gearRatio);
        if (Application.isPlaying && useRideCadenceForCrank && rideSession != null &&
            rideSession.devices != null)
        {
            // Telemetry may arrive in discrete packets (and keyboard strokes are discrete by
            // definition). Integrate a continuously blended angular velocity rather than
            // converting each packet or road-position step directly into a crank jump.
            float targetCadence = Mathf.Max(0f, rideSession.devices.Telemetry.CadenceRpm);
            float dt = Mathf.Max(0f, Time.deltaTime);
            float blend = 1f - Mathf.Exp(-dt /
                Mathf.Max(0.01f, crankCadenceSmoothingSeconds));
            smoothedCadenceRpm = Mathf.Lerp(smoothedCadenceRpm, targetCadence, blend);
            if (smoothedCadenceRpm < 0.05f) smoothedCadenceRpm = 0f;
            crankDeg = smoothedCadenceRpm * 6f * dt;
        }

        crankAngle += crankDeg;
        if (bbSocket) bbSocket.localRotation = Quaternion.identity;
        if (crankL) crankL.localRotation = Quaternion.AngleAxis(crankAngle, Vector3.right) * crankLBind;
        if (crankR) crankR.localRotation = Quaternion.AngleAxis(crankAngle, Vector3.right) * crankRBind;

        // Pedals are authored as free-turning platforms under their crank arms. They therefore
        // travel on opposite sides of the revolution while retaining their physical orientation.
        LevelPedals();
    }

    /// <summary>
    /// Cancels the crank's spin out of the pedal platforms so they orbit the bottom bracket
    /// while staying flat, as real free-turning pedals do.
    ///
    /// The crank is driven as <c>AngleAxis(crankAngle, right) * crankBind</c>, and both that
    /// spin and each crankBind are rotations about the SAME local X axis, so they commute.
    /// Cancelling the spin therefore collapses to pre-multiplying the pedal's captured bind by
    /// the inverse spin - no per-parent bookkeeping required.
    ///
    /// Replaces the older Level() helper, which derived the phase from the crank's Euler angles
    /// (ambiguous once the crank passes 180 degrees) and was never actually called.
    /// </summary>
    void LevelPedals()
    {
        if (!keepPedalsLevel) return;
        Quaternion unspin = Quaternion.AngleAxis(-crankAngle, Vector3.right);
        if (pedalL) pedalL.localRotation = unspin * savedPedalLBind;
        if (pedalR) pedalR.localRotation = unspin * savedPedalRBind;
    }

    void Level(Transform pedal, Transform crank)
    {
        if (pedal == null) return;
        float phase = crank ? crank.localEulerAngles.x : 0f;
        pedal.localRotation = Quaternion.AngleAxis(-(crankAngle + phase), Vector3.right);
    }

    void Steer()
    {
        if (steerPivot == null) return;
        float want = !float.IsNaN(SteerOverrideDegrees)
            ? Mathf.Clamp(SteerOverrideDegrees, -steerVisualDegrees, steerVisualDegrees)
            : Application.isPlaying ? Input.GetAxis("Horizontal") * steerVisualDegrees : 0f;
        steerAngle = Mathf.Lerp(steerAngle, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
        steerPivot.localRotation = Quaternion.AngleAxis(steerAngle, Vector3.up);
    }

    void SeatRider()
    {
        // Standing lifts the hips off the saddle and slides them forward over the bottom
        // bracket; the rock is a roll about the direction of travel.
        Vector3 seat = saddleTop.position
                     + transform.right * riderLateralOffset
                     + transform.up * poseStandRiseM
                     + transform.forward * poseStandForwardM
                     + poseHipWorldOffset;
        hips.position = seat;
        float ride = 1f - Mathf.Clamp01(poseUprightBlend);
        hips.rotation = Quaternion.AngleAxis(poseStandRockDegrees * ride, transform.forward)
                      * Quaternion.AngleAxis((hipTiltDegrees + poseExtraHipTiltDegrees) * ride, transform.right)
                      * (transform.rotation * hipsBind);

        float per = (spineLeanDegrees + poseExtraSpineLeanDegrees) * (0.1f + 0.9f * ride) / Mathf.Max(1, spine.Length);
        for (int i = 0; i < spine.Length; i++)
        {
            if (poseUseWorldAlignedSpineLean)
            {
                Quaternion bindWorld = spine[i].parent.rotation * spineBind[i];
                spine[i].rotation =
                    Quaternion.AngleAxis(per, transform.right) * bindWorld;
            }
            else
            {
                spine[i].localRotation =
                    spineBind[i] * Quaternion.AngleAxis(per, Vector3.right);
            }
        }

        if (useAnatomicalArmSolver && shoulderL && shoulderR)
        {
            // Restore the clavicles relative to their CHEST parents after the spine lean. The old
            // rider-root-relative world bind detached the shoulder girdle from the leaned torso.
            shoulderL.localRotation = savedShoulderLParentBind;
            shoulderR.localRotation = savedShoulderRParentBind;
            GetTorsoFrame(out Vector3 torsoRight, out Vector3 torsoUp, out Vector3 torsoForward);
            ApplyShoulderOffsets(shoulderL, armL, torsoRight, torsoUp, torsoForward,
                                 shoulderDropDegrees + poseExtraShoulderDropDegrees,
                                 poseShoulderProtractionDegrees + poseShoulderProtractionBalanceDegrees);
            ApplyShoulderOffsets(shoulderR, armR, torsoRight, torsoUp, torsoForward,
                                 shoulderDropDegrees + poseExtraShoulderDropDegrees,
                                 poseShoulderProtractionDegrees - poseShoulderProtractionBalanceDegrees);
        }
        // Legacy NPC shoulder handling remains unchanged unless the anatomical solver is opted in.
        else
        // not bury the head between the shoulders. Applied in WORLD space (opposite signs per side
        // so both go DOWN) from the rig-relative bind; SolveLimbs re-solves the arm IK afterwards,
        // so the hands stay on the hoods while the arm roots (and the shoulder line) drop.
        if (shoulderDropDegrees != 0f || poseExtraShoulderDropDegrees != 0f)
        {
            float drop = shoulderDropDegrees + poseExtraShoulderDropDegrees;
            if (shoulderL)
                shoulderL.rotation = Quaternion.AngleAxis(drop, transform.forward)
                                   * transform.rotation * savedShoulderLBind;
            if (shoulderR)
                shoulderR.rotation = Quaternion.AngleAxis(-drop, transform.forward)
                                   * transform.rotation * savedShoulderRBind;
        }

        // The cycling export inherits a slightly downward-looking neutral head. Lift the neck
        // and head independently so Kuro keeps his eyes on the road instead of staring at the
        // front tyre when seated.
        // The look-back is split across the neck and the head. Partly because that is how a
        // neck works, and partly because this sculpt's Head bone carries far less of the mesh
        // than its size suggests - a head-only yaw rotated the bone (and the ribbons hanging
        // off it) while the helmet barely moved, which the render caught and the "yaw 62.0
        // deg" log line did not.
        if (neck)
            neck.localRotation = savedNeckBind
                               * Quaternion.AngleAxis((neckLiftDegrees + poseExtraNeckLiftDegrees) * ride, Vector3.right)
                               * Quaternion.AngleAxis(poseHeadYawDegrees * poseNeckYawShare, Vector3.up)
                               * Quaternion.AngleAxis(poseHeadRollDegrees * poseNeckYawShare, Vector3.forward);
        if (head)
        {
            // Yaw/roll are applied in the head bone's LOCAL frame, after the lift, so the
            // look-back turns the face rather than shearing the neck. Blender's glTF exporter
            // keeps a bone's LONG axis on local +Y, so turning the head is a rotation about
            // local Y and tipping it is a rotation about local Z.
            float share = 1f - poseNeckYawShare;
            head.localRotation = savedHeadBind
                               * Quaternion.AngleAxis((headLiftDegrees + poseExtraHeadLiftDegrees) * ride, Vector3.right)
                               * Quaternion.AngleAxis(poseHeadYawDegrees * share, Vector3.up)
                               * Quaternion.AngleAxis(poseHeadRollDegrees * share, Vector3.forward);
        }
    }

    void SolveLimbs()
    {
        Vector3 fwd = transform.forward, up = transform.up, right = transform.right;

        Vector3 leftOut = Vector3.Dot(upLegL.position - upLegR.position, right) < 0f
            ? -right : right;
        Vector3 rightOut = -leftOut;
        Vector3 footGoalL = Vector3.Lerp(FootTargetL, poseFootGoalL, Mathf.Clamp01(poseFootGoalWeightL));
        Vector3 footGoalR = Vector3.Lerp(FootTargetR, poseFootGoalR, Mathf.Clamp01(poseFootGoalWeightR));
        Vector3 kneePoleL = Vector3.Lerp(upLegL.position, footGoalL, 0.45f)
                          + fwd * 1.0f + up * 0.20f
                          + leftOut * kneePoleLateralOffset;
        Vector3 kneePoleR = Vector3.Lerp(upLegR.position, footGoalR, 0.45f)
                          + fwd * 1.0f + up * 0.20f
                          + rightOut * kneePoleLateralOffset;
        SolveTwoBone(upLegL, legL, footL, footGoalL, kneePoleL);
        SolveTwoBone(upLegR, legR, footR, footGoalR, kneePoleR);
        ApplyFootOrientation(footL, pedalL, savedFootLToPedal, footRollDegrees);
        ApplyFootOrientation(footR, pedalR, savedFootRToPedal, -footRollDegrees);
        // clip-out: heel swings OUT (away from the frame) about the bike's up axis
        if (poseHeelOutDegreesL != 0f && footL)
            footL.rotation = Quaternion.AngleAxis(poseHeelOutDegreesL * (Vector3.Dot(leftOut, right) > 0f ? -1f : 1f), up) * footL.rotation;
        if (poseHeelOutDegreesR != 0f && footR)
            footR.rotation = Quaternion.AngleAxis(poseHeelOutDegreesR * (Vector3.Dot(rightOut, right) > 0f ? -1f : 1f), up) * footR.rotation;

        Vector3 totalHandOffset = handTargetLocalOffset + poseHandTargetOffset;
        Vector3 handOff = totalHandOffset == Vector3.zero || steerPivot == null
                        ? Vector3.zero
                        : steerPivot.TransformVector(totalHandOffset);
        // Mirrored outboard spread so the skinned GLOVE (which hangs inboard of the wrist) lands
        // on the hood, not just the wrist bone. Hood_L is at +local X, Hood_R at -local X.
        float totalSpread = gripSpreadMetres + poseExtraGripSpreadMetres;
        Vector3 spread = (totalSpread != 0f && steerPivot != null)
                       ? steerPivot.TransformVector(Vector3.right).normalized * totalSpread
                       : Vector3.zero;
        Vector3 seatL = (handSeatOffsetL != Vector3.zero && steerPivot != null)
                      ? steerPivot.TransformVector(handSeatOffsetL) : Vector3.zero;
        Vector3 seatR = (handSeatOffsetR != Vector3.zero && steerPivot != null)
                      ? steerPivot.TransformVector(handSeatOffsetR) : Vector3.zero;

        // An override socket (the aero-bar extensions) REPLACES the hood target outright rather
        // than offsetting it: the extension is a real object with its own place in the cockpit,
        // and expressing it as a delta from the hood would silently drift the moment the bar,
        // the bike scale or the per-rider grip fit changed.
        Vector3 targetL = poseHandTargetOverrideL != null
                        ? poseHandTargetOverrideL.position
                        : hoodL.position + handOff - spread + seatL;
        Vector3 targetR = poseHandTargetOverrideR != null
                        ? poseHandTargetOverrideR.position
                        : hoodR.position + handOff + spread + seatR;
        if (useAnatomicalArmSolver)
        {
            RestoreArmLocalBinds();
            GetTorsoFrame(out Vector3 torsoRight, out Vector3 torsoUp, out Vector3 torsoForward);
            Vector3 outL = Vector3.Dot(armL.position - armR.position, torsoRight) < 0f
                         ? -torsoRight : torsoRight;
            Vector3 outR = -outL;
            Vector3 guideL = armL.position + outL * poseElbowPoleOutMetres
                           - torsoUp * poseElbowPoleDownMetres
                           - torsoForward * poseElbowPoleBackMetres;
            Vector3 guideR = armR.position + outR * poseElbowPoleOutMetres
                           - torsoUp * poseElbowPoleDownMetres
                           - torsoForward * poseElbowPoleBackMetres;
            // In ordinary poses an override remains a directional guide. In time trial the
            // same transform is explicitly the pad-contact goal; the geometric solver returns
            // the closest length-preserving point on the elbow's feasible circle.
            if (poseElbowPoleOverrideL != null)
                guideL = poseConstrainElbowsToTargets
                    ? poseElbowPoleOverrideL.position
                    : Vector3.Lerp(guideL, poseElbowPoleOverrideL.position, 0.35f);
            if (poseElbowPoleOverrideR != null)
                guideR = poseConstrainElbowsToTargets
                    ? poseElbowPoleOverrideR.position
                    : Vector3.Lerp(guideR, poseElbowPoleOverrideR.position, 0.35f);

            SolveTwoBoneGuided(armL, foreArmL, handL, targetL, guideL);
            SolveTwoBoneGuided(armR, foreArmR, handR, targetR, guideR);
            ApplyGripOrientation(armL, foreArmL, handL, targetL, poseHandOrientationTargetL);
            ApplyGripOrientation(armR, foreArmR, handR, targetR, poseHandOrientationTargetR);
        }

        else
        {
            Vector3 poleL = poseElbowPoleOverrideL != null
                          ? poseElbowPoleOverrideL.position
                          : transform.position + right * 1.0f * armElbowPoleSign - up * 0.6f + fwd * 0.2f;
            Vector3 poleR = poseElbowPoleOverrideR != null
                          ? poseElbowPoleOverrideR.position
                          : transform.position - right * 1.0f * armElbowPoleSign - up * 0.6f + fwd * 0.2f;
            SolveTwoBone(armL, foreArmL, handL, targetL, poleL);
            SolveTwoBone(armR, foreArmR, handR, targetR, poleR);
        }
    }

    Vector3 FootTarget(Transform pedal, bool left)
    {
        if (pedal == null) return transform.position;
        float height = ankleHeight + (left ? footHeightTrimL : footHeightTrimR);
        return pedal.position + pedal.up * height
             - transform.forward * footTargetRearwardOffset;
    }

    void ApplyFootOrientation(
        Transform foot, Transform pedal, Quaternion footToPedalBind, float rollDegrees)
    {
        if (foot == null || pedal == null) return;
        Quaternion baseRotation = pedal.rotation * footToPedalBind;
        foot.rotation = Quaternion.AngleAxis(rollDegrees, transform.forward)
                      * Quaternion.AngleAxis(footToeDownDegrees, transform.right)
                      * baseRotation;
    }

    void CaptureShoulderParentBinds()
    {
        Quaternion hipsCurrent = hips.rotation;
        var spineCurrent = new Quaternion[spine.Length];
        for (int i = 0; i < spine.Length; i++) spineCurrent[i] = spine[i].localRotation;

        hips.rotation = transform.rotation * savedHipsBind;
        for (int i = 0; i < spine.Length; i++) spine[i].localRotation = savedSpineBind[i];
        if (shoulderL)
        {
            Quaternion worldBind = shoulderBindCaptured
                ? transform.rotation * savedShoulderLBind : shoulderL.rotation;
            savedShoulderLParentBind = Quaternion.Inverse(shoulderL.parent.rotation) * worldBind;
        }
        if (shoulderR)
        {
            Quaternion worldBind = shoulderBindCaptured
                ? transform.rotation * savedShoulderRBind : shoulderR.rotation;
            savedShoulderRParentBind = Quaternion.Inverse(shoulderR.parent.rotation) * worldBind;
        }

        hips.rotation = hipsCurrent;
        for (int i = 0; i < spine.Length; i++) spine[i].localRotation = spineCurrent[i];
    }

    void GetTorsoFrame(out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        right = (armR.position - armL.position).normalized;
        if (Vector3.Dot(right, transform.right) < 0f) right = -right;
        Transform chest = spine != null && spine.Length > 0 ? spine[spine.Length - 1] : hips;
        Vector3 top = neck ? neck.position : chest.position + transform.up;
        up = (top - hips.position).normalized;
        if (up.sqrMagnitude < 0.5f) up = transform.up;
        forward = Vector3.Cross(right, up).normalized;
        if (Vector3.Dot(forward, transform.forward) < 0f) forward = -forward;
        up = Vector3.Cross(forward, right).normalized;
    }

    static void ApplyShoulderOffsets(
        Transform shoulder, Transform arm, Vector3 torsoRight, Vector3 torsoUp,
        Vector3 torsoForward, float dropDegrees, float protractionDegrees)
    {
        Vector3 clavicle = arm.position - shoulder.position;
        if (dropDegrees != 0f)
        {
            float sign = Vector3.Dot(Vector3.Cross(torsoForward, clavicle), -torsoUp) >= 0f ? 1f : -1f;
            shoulder.rotation = Quaternion.AngleAxis(sign * dropDegrees, torsoForward) * shoulder.rotation;
        }
        clavicle = arm.position - shoulder.position;
        if (protractionDegrees != 0f)
        {
            float sign = Vector3.Dot(Vector3.Cross(torsoUp, clavicle), torsoForward) >= 0f ? 1f : -1f;
            shoulder.rotation = Quaternion.AngleAxis(sign * protractionDegrees, torsoUp) * shoulder.rotation;
        }
    }

    void ApplyGripOrientation(
        Transform arm, Transform forearm, Transform hand, Vector3 handTarget,
        Transform orientationTarget)
    {
        if (orientationTarget == null || poseHandOrientationWeight <= 0f) return;

        Vector3 foreAxis = (hand.position - forearm.position).normalized;
        Vector3 havePalm = Vector3.ProjectOnPlane(hand.up, foreAxis);
        Vector3 wantPalm = Vector3.ProjectOnPlane(orientationTarget.up, foreAxis);
        float twist = 0f;
        if (havePalm.sqrMagnitude > 1e-8f && wantPalm.sqrMagnitude > 1e-8f)
            twist = Mathf.Clamp(Vector3.SignedAngle(havePalm, wantPalm, foreAxis),
                                -poseMaxGripTwistDegrees, poseMaxGripTwistDegrees);

        float upperTwist = twist * Mathf.Clamp(poseUpperArmTwistWeight, 0f, 0.35f);
        if (Mathf.Abs(upperTwist) > 0.01f)
        {
            Vector3 upperAxis = (forearm.position - arm.position).normalized;
            arm.rotation = Quaternion.AngleAxis(upperTwist, upperAxis) * arm.rotation;
            Vector3 currentFore = hand.position - forearm.position;
            Vector3 wantedFore = handTarget - forearm.position;
            if (currentFore.sqrMagnitude > 1e-8f && wantedFore.sqrMagnitude > 1e-8f)
                forearm.rotation = Quaternion.FromToRotation(currentFore, wantedFore) * forearm.rotation;
        }

        foreAxis = (hand.position - forearm.position).normalized;
        float foreTwist = twist * Mathf.Clamp(poseForearmTwistWeight, 0f, 0.75f);
        if (Mathf.Abs(foreTwist) > 0.01f)
            forearm.rotation = Quaternion.AngleAxis(foreTwist, foreAxis) * forearm.rotation;

        hand.rotation = Quaternion.Slerp(
            hand.rotation, orientationTarget.rotation, Mathf.Clamp01(poseHandOrientationWeight));
    }

    public void SetArmLocalBinds(
        Quaternion leftArm, Quaternion leftForeArm, Quaternion leftHand,
        Quaternion rightArm, Quaternion rightForeArm, Quaternion rightHand)
    {
        savedArmLLocalBind = leftArm;
        savedForeArmLLocalBind = leftForeArm;
        savedHandLLocalBind = leftHand;
        savedArmRLocalBind = rightArm;
        savedForeArmRLocalBind = rightForeArm;
        savedHandRLocalBind = rightHand;
        armBindCaptured = true;
    }

    void RestoreArmLocalBinds()
    {
        if (!armBindCaptured) return;
        armL.localRotation = savedArmLLocalBind;
        foreArmL.localRotation = savedForeArmLLocalBind;
        handL.localRotation = savedHandLLocalBind;
        armR.localRotation = savedArmRLocalBind;
        foreArmR.localRotation = savedForeArmRLocalBind;
        handR.localRotation = savedHandRLocalBind;
    }

    static void SolveTwoBoneGuided(
        Transform root, Transform mid, Transform end, Vector3 target, Vector3 elbowGuide)
    {
        if (root == null || mid == null || end == null) return;
        float a = Vector3.Distance(root.position, mid.position);
        float b = Vector3.Distance(mid.position, end.position);
        Vector3 toTarget = target - root.position;
        float c = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
        if (a < 1e-5f || b < 1e-5f || c < 1e-5f) return;

        Vector3 line = toTarget.normalized;
        float along = (a * a - b * b + c * c) / (2f * c);
        float radius = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Vector3 radial = Vector3.ProjectOnPlane(elbowGuide - root.position, line);
        if (radial.sqrMagnitude < 1e-8f)
            radial = Vector3.ProjectOnPlane(mid.position - root.position, line);
        if (radial.sqrMagnitude < 1e-8f)
            radial = Vector3.Cross(line, Vector3.up);
        radial.Normalize();
        Vector3 desiredElbow = root.position + line * along + radial * radius;

        root.rotation = Quaternion.FromToRotation(mid.position - root.position,
                                                  desiredElbow - root.position) * root.rotation;
        mid.rotation = Quaternion.FromToRotation(end.position - mid.position,
                                                 target - mid.position) * mid.rotation;
    }

    /// <summary>
    /// Analytic two-bone IK by the law of cosines. Hand-rolled deliberately: the project has no
    /// Animation Rigging package and this is ~30 lines, so pulling one in for it is not worth a
    /// new dependency in the manifest.
    /// </summary>
    static void SolveTwoBone(Transform root, Transform mid, Transform end, Vector3 target, Vector3 pole)
    {
        if (root == null || mid == null || end == null) return;

        float a = Vector3.Distance(root.position, mid.position);
        float b = Vector3.Distance(mid.position, end.position);
        if (a < 1e-5f || b < 1e-5f) return;

        Vector3 toTarget = target - root.position;
        float c = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);

        Vector3 axis = Vector3.Cross(mid.position - root.position, end.position - root.position);
        if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(toTarget, pole - root.position);
        if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(toTarget, Vector3.up);
        if (axis.sqrMagnitude < 1e-10f) return;
        axis.Normalize();

        float wantRoot = Mathf.Acos(Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f)) * Mathf.Rad2Deg;
        float haveRoot = Vector3.Angle(mid.position - root.position, end.position - root.position);
        root.rotation = Quaternion.AngleAxis(wantRoot - haveRoot, axis) * root.rotation;

        float wantMid = Mathf.Acos(Mathf.Clamp((a * a + b * b - c * c) / (2f * a * b), -1f, 1f)) * Mathf.Rad2Deg;
        float haveMid = Vector3.Angle(root.position - mid.position, end.position - mid.position);
        mid.rotation = Quaternion.AngleAxis(haveMid - wantMid, axis) * mid.rotation;

        // Land the end effector on the target...
        root.rotation = Quaternion.FromToRotation(end.position - root.position, toTarget) * root.rotation;

        // ...then twist about the root->target line, which cannot move it off the target again.
        Vector3 havePlane = Vector3.ProjectOnPlane(mid.position - root.position, toTarget);
        Vector3 wantPlane = Vector3.ProjectOnPlane(pole - root.position, toTarget);
        if (havePlane.sqrMagnitude > 1e-8f && wantPlane.sqrMagnitude > 1e-8f)
            root.rotation = Quaternion.FromToRotation(havePlane, wantPlane) * root.rotation;
    }
}

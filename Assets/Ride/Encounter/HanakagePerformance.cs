using UnityEngine;

/// <summary>
/// Turns Hanakage's ENCOUNTER PHASES into PHYSICAL PERFORMANCE (handoff section 19, P1).
///
/// The encounter state machine decides what is true; this decides what the player SEES. The
/// split matters: the design's whole emotional sequence (Notice -> Curiosity -> Catch ->
/// Recognition -> Physical response -> Respect -> Mystery -> Reveal) hangs on two moments that
/// are purely physical and carry no UI at all -
///
///   * the LOOK BACK, when she turns her head, sees the player is still there, and speaks; and
///   * the ATTACK, when she comes out of the saddle and simply rides away.
///
/// Both must read without a health bar, a boss banner or a name, so the acting has to do the
/// work. This component is that acting, and nothing else: it never writes a phase, never
/// touches her speed, never changes an outcome. Deleting it would leave the encounter fully
/// playable and completely flat.
///
/// It drives three surfaces:
///   * the rig's additive pose modifiers on <see cref="KuroBikeRig"/> (head yaw/roll for the
///     look-back, hip rise/forward/rock and spine opening for the standing climb);
///   * <see cref="HanakageRibbon.gust"/>, so the streamers sweep when she turns and whip when
///     she goes;
///   * a petal burst ParticleSystem, emitted once on the attack.
///
/// Every number below is a PROVISIONAL tuning default. Section 18 of the handoff lists the
/// encounter's configurable parameters; the presentation timings are not in that list, which
/// means they are illustrative and should be expected to move after a playtest.
/// </summary>
[DefaultExecutionOrder(80)]
public class HanakagePerformance : MonoBehaviour
{
    /// <summary>Exact child name of the burst emitter created by the staging pass.</summary>
    public const string BurstName = "HanakagePetalBurst";

    // ------------------------------------------------------------------ wiring
    [Header("Wiring (found by the setup pass)")]
    public HanakageEncounter encounter;
    public HanakageRider rider;
    [Tooltip("The rig that seats her on the bike. Its pose modifier fields are what this writes.")]
    public KuroBikeRig rig;
    public HanakageRibbon ribbon;
    [Tooltip("One-shot petal emitter parented near her shoulders.")]
    public ParticleSystem petalBurst;

    // ------------------------------------------------------------------ look back
    [Header("Look back (provisional)")]
    [Tooltip("Degrees of head yaw at the top of the glance. Positive is over her LEFT " +
             "shoulder - riders keep left in Japan, so that is the shoulder you check.")]
    public float lookBackYawDegrees = 62f;
    [Tooltip("Degrees of head roll at the top of the glance. A real look-back tips as it " +
             "turns; without this it reads like a turret.")]
    public float lookBackRollDegrees = -9f;
    [Tooltip("Seconds to turn the head.")]
    public float lookBackTurnSeconds = 0.55f;
    [Tooltip("Seconds she holds the glance. This is the beat the dialogue lands in, so it " +
             "must be at least as long as it takes to read 'Still here?'.")]
    public float lookBackHoldSeconds = 1.30f;
    [Tooltip("Seconds to return to looking up the road.")]
    public float lookBackReturnSeconds = 0.70f;

    // ------------------------------------------------------------------ standing climb
    [Header("Standing climb (provisional)")]
    [Tooltip("Metres the hips lift off the saddle when she stands.")]
    public float standRiseM = 0.085f;
    [Tooltip("Metres the hips slide forward over the bottom bracket when she stands.")]
    public float standForwardM = 0.045f;
    [Tooltip("Peak hip roll of the side-to-side rock, degrees.")]
    public float standRockDegrees = 7.5f;
    [Tooltip("Rock cycles per second at her reference climbing speed. One cycle is one full " +
             "left-right, i.e. one pedal revolution.")]
    public float standRockHz = 1.30f;
    [Tooltip("Road speed, m/s, at which standRockHz applies. ~11 kph.")]
    public float standReferenceSpeedMps = 3.1f;
    [Tooltip("Degrees the torso OPENS when she stands (negative reduces the seated lean).")]
    public float standSpineOpenDegrees = -9f;
    [Tooltip("Seconds to blend in or out of the standing pose.")]
    public float standBlendSeconds = 0.85f;
    [Tooltip("Seconds she stays out of the saddle after the attack begins. She sits back down " +
             "for the run to the hairpin, then stands again to go clear - which is what makes " +
             "the second acceleration read as a second decision rather than one long sprint.")]
    public float standAttackSeconds = 9.0f;

    // ------------------------------------------------------------------ petals
    [Header("Petal burst (provisional)")]
    [Tooltip("Particles released the instant she attacks.")]
    public int burstCount = 34;
    [Tooltip("A second, smaller release as she goes clear over the crest.")]
    public int escapeBurstCount = 18;

    // ------------------------------------------------------------------ ribbon gust
    [Header("Ribbon gust (provisional)")]
    [Tooltip("Gust level while she is looking back.")]
    [Range(0f, 1f)] public float lookBackGust = 0.55f;
    [Tooltip("Gust level while she is standing on the pedals.")]
    [Range(0f, 1f)] public float standGust = 1.0f;
    [Tooltip("Seconds for the gust to decay back to normal.")]
    public float gustDecaySeconds = 1.6f;

    // ------------------------------------------------------------------ live state
    private HanakageEncounter.Phase _last = HanakageEncounter.Phase.Dormant;
    private float _lookBackT = -1f;      // seconds into the glance; negative = not glancing
    private float _standTarget;          // 0 or 1
    private float _stand;                // blended
    private float _standHold = -1f;      // seconds left of the post-attack standing window
    private float _rockPhase;
    private float _gust;

    /// <summary>
    /// 0..1 weight of the look-back glance, for other systems to key off. The camera director
    /// uses this rather than re-deriving the curve, so the brief "focus toward Hanakage" that
    /// section 17 allows is guaranteed to land on the same frames as her head turn.
    /// </summary>
    public float LookBackWeight { get; private set; }

    /// <summary>0..1 blended weight of the out-of-the-saddle attack pose.</summary>
    public float StandWeight => _stand;

    private void OnEnable()
    {
        if (encounter != null) encounter.PhaseChanged += OnPhase;
        _last = encounter != null ? encounter.State : HanakageEncounter.Phase.Dormant;
    }

    private void OnDisable()
    {
        if (encounter != null) encounter.PhaseChanged -= OnPhase;
        ClearPose();
    }

    private void OnPhase(HanakageEncounter.Phase p)
    {
        switch (p)
        {
            case HanakageEncounter.Phase.Attack:
                // The look-back is what CAUSES the attack in the fiction: she checks, sees the
                // player has not cracked, and goes. So the glance starts on the same frame.
                _lookBackT = 0f;
                _standHold = standAttackSeconds;
                _gust = Mathf.Max(_gust, lookBackGust);
                Emit(burstCount);
                break;

            case HanakageEncounter.Phase.Escape:
                _standHold = standAttackSeconds;
                _gust = 1f;
                Emit(escapeBurstCount);
                break;

            case HanakageEncounter.Phase.Observation:
            case HanakageEncounter.Phase.Declined:
            case HanakageEncounter.Phase.Dormant:
            case HanakageEncounter.Phase.Complete:
                ClearPose();
                break;
        }
        _last = p;
    }

    private void Emit(int n)
    {
        if (petalBurst == null || n <= 0) return;
        petalBurst.Emit(n);
    }

    private void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// dt-driven so an editor capture harness can advance the performance to a chosen beat and
    /// shoot it, exactly the way <see cref="HanakageRibbon.Tick"/> can.
    /// </summary>
    public void Tick(float dt)
    {
        if (dt <= 0f) dt = 1f / 60f;
        if (rig == null) return;

        bool present = rider != null && rider.Present;
        if (!present) { ClearPose(); return; }

        // ---- look back -------------------------------------------------
        float yaw = 0f, roll = 0f;
        if (_lookBackT >= 0f)
        {
            _lookBackT += dt;
            float turn = Mathf.Max(0.01f, lookBackTurnSeconds);
            float hold = Mathf.Max(0f, lookBackHoldSeconds);
            float back = Mathf.Max(0.01f, lookBackReturnSeconds);
            float a;
            if (_lookBackT < turn) a = Mathf.SmoothStep(0f, 1f, _lookBackT / turn);
            else if (_lookBackT < turn + hold) a = 1f;
            else if (_lookBackT < turn + hold + back)
                a = Mathf.SmoothStep(1f, 0f, (_lookBackT - turn - hold) / back);
            else { a = 0f; _lookBackT = -1f; }

            yaw = lookBackYawDegrees * a;
            roll = lookBackRollDegrees * a;
            LookBackWeight = a;
            _gust = Mathf.Max(_gust, lookBackGust * a);
        }
        else LookBackWeight = 0f;

        // ---- standing climb --------------------------------------------
        if (_standHold > 0f)
        {
            _standHold -= dt;
            _standTarget = 1f;
        }
        else _standTarget = 0f;

        _stand = Mathf.MoveTowards(_stand, _standTarget,
                                   dt / Mathf.Max(0.01f, standBlendSeconds));

        float speed = rider != null ? Mathf.Abs(rider.speedMps) : standReferenceSpeedMps;
        float hz = standRockHz * Mathf.Clamp(speed / Mathf.Max(0.01f, standReferenceSpeedMps),
                                             0.4f, 1.8f);
        _rockPhase += dt * hz * Mathf.PI * 2f;

        rig.poseHeadYawDegrees = yaw;
        rig.poseHeadRollDegrees = roll;
        rig.poseStandRiseM = standRiseM * _stand;
        rig.poseStandForwardM = standForwardM * _stand;
        rig.poseStandRockDegrees = standRockDegrees * _stand * Mathf.Sin(_rockPhase);
        rig.poseExtraSpineLeanDegrees = standSpineOpenDegrees * _stand;

        // ---- ribbon gust ------------------------------------------------
        _gust = Mathf.Max(_gust, standGust * _stand);
        _gust = Mathf.MoveTowards(_gust, 0f, dt / Mathf.Max(0.01f, gustDecaySeconds));
        if (ribbon != null) ribbon.gust = _gust;
    }

    /// <summary>Returns her to the neutral seated pose. Safe to call repeatedly.</summary>
    public void ClearPose()
    {
        _lookBackT = -1f;
        LookBackWeight = 0f;
        _standHold = -1f;
        _stand = 0f;
        _standTarget = 0f;
        _gust = 0f;
        if (rig != null)
        {
            rig.poseHeadYawDegrees = 0f;
            rig.poseHeadRollDegrees = 0f;
            rig.poseStandRiseM = 0f;
            rig.poseStandForwardM = 0f;
            rig.poseStandRockDegrees = 0f;
            rig.poseExtraSpineLeanDegrees = 0f;
        }
        if (ribbon != null) ribbon.gust = 0f;
    }

    /// <summary>
    /// Forces the glance on. Used by the capture harness, which shoots a single frame and so
    /// cannot wait for the phase change to arrive.
    /// </summary>
    public void ForceLookBack(float secondsIn)
    {
        _lookBackT = Mathf.Max(0f, secondsIn);
    }

    /// <summary>Forces the standing pose on for the capture harness.</summary>
    public void ForceStanding(float seconds)
    {
        _standHold = Mathf.Max(0f, seconds);
    }
}

using UnityEngine;

/// <summary>
/// EFFORT-TO-ANIMATION: the rider's trainer telemetry becomes the character's acting.
/// Runs right after <see cref="KuroRidePose"/> (order 40) and ADDS to the rig modifiers it just
/// wrote, before <see cref="KuroBikeRig"/> consumes them in LateUpdate. Crank speed already
/// follows real cadence (KuroBikeRig.useRideCadenceForCrank), so high cadence reads as a rapid,
/// smooth spin by itself.
///
/// Intensity = watts / FTP (smoothed), so the bands scale to every rider. With FTP 250:
///   ~120 W (0.5)  RELAXED   - sits up, looks around
///   ~250 W (1.0)  TUCKED    - lower, head up to the road, visibly working
///   ~400 W (1.6)  SURGING   - shoulders rock with the stroke, stands up now and then
///   ~700 W (2.8)  SPRINT    - drives KuroRidePose's full sprint
///   low cadence + high torque -> GRINDING (deep, slow shoulder heave, head down)
///   stop pedalling after a hard climb -> RECOVERY (sits up, heavy breathing, head roll)
/// Deterministic per frame from telemetry, so every client animates a rider the same way.
/// </summary>
[DefaultExecutionOrder(45)]
[DisallowMultipleComponent]
public sealed class KuroEffortActing : MonoBehaviour
{
    public KuroRidePose pose;
    public KuroBikeRig rig;
    public RideSession session;
    public DeviceManager devices;

    [Tooltip("Accessibility / preference: scales every effort gesture (0 = off).")]
    [Range(0f, 1.5f)] public float actingStrength = 1f;

    // smoothed signals
    private float _intensity, _cadence, _fatigue, _recovery, _lookTimer, _lookTarget, _look;
    private float _standTimer, _standHold, _stand, _breathPhase, _heavePhase;
    private bool _wasPedalling;
    private System.Random _rng = new System.Random(1905);

    /// <summary>Exposed for debugging / HUD: 0 relaxed .. 1 tuck .. 2 surge .. 3 sprint.</summary>
    public float EffortLevel { get; private set; }

    private void Start()
    {
        if (pose == null) pose = GetComponent<KuroRidePose>();
        if (rig == null) rig = GetComponentInChildren<KuroBikeRig>();
        if (session == null) session = FindAnyObjectByType<RideSession>();
        if (devices == null) devices = FindAnyObjectByType<DeviceManager>();
    }

    private static float Band(float x, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

    private void Update()
    {
        if (rig == null || devices == null || actingStrength <= 0f) return;
        float dt = Mathf.Max(0f, Time.deltaTime);
        var tm = devices.Telemetry;
        float ftp = Mathf.Max(80f, devices.ftpWatts);

        // Smooth like a body, not like a power meter: surges build over ~1 s, fade over ~2 s.
        float rawI = Mathf.Max(0f, tm.Watts) / ftp;
        _intensity = Mathf.Lerp(_intensity, rawI, 1f - Mathf.Exp(-dt * (rawI > _intensity ? 1.2f : 0.5f)));
        _cadence = Mathf.Lerp(_cadence, Mathf.Max(0f, tm.CadenceRpm), 1f - Mathf.Exp(-dt * 3f));
        float I = _intensity;
        float grade = session != null ? session.Grade : 0f;
        bool pedalling = _cadence > 12f && tm.Watts > 15f;

        // ---- bands -----------------------------------------------------------------------
        float relaxed = 1f - Band(I, 0.45f, 0.80f);
        float tuck = Band(I, 0.70f, 1.00f) * (1f - Band(I, 1.40f, 1.80f));
        float surge = Band(I, 1.35f, 1.80f) * (1f - Band(I, 2.30f, 2.70f));
        float sprint = Band(I, 2.30f, 2.70f);
        // Torque ~ W / cadence: heavy gear, slow legs.
        float grind = Band(I, 0.75f, 1.2f) * Band(70f - _cadence, 0f, 20f) * (pedalling ? 1f : 0f);
        // Spinning fast smooths the upper body (a good spinner is quiet on the bike).
        float spin = Band(_cadence, 95f, 115f);
        EffortLevel = tuck + 2f * surge + 3f * sprint;

        // ---- fatigue & recovery -----------------------------------------------------------
        // Fatigue builds while working hard uphill, drains slowly otherwise.
        float work = Mathf.Clamp01((I - 0.85f) / 0.6f) * (grade > 0.03f ? 1f : 0.4f);
        _fatigue = Mathf.Clamp01(_fatigue + dt * (work > 0f ? work / 45f : -1f / 90f));
        if (_wasPedalling && !pedalling && _fatigue > 0.35f) _recovery = Mathf.Max(_recovery, _fatigue);
        if (pedalling) _recovery = Mathf.MoveTowards(_recovery, 0f, dt / 1.5f);
        else _recovery = Mathf.MoveTowards(_recovery, 0f, dt / 12f);
        _wasPedalling = pedalling;
        float rec = Band(_recovery, 0.05f, 0.5f);

        // ---- occasional standing while surging --------------------------------------------
        _standTimer -= dt;
        if (_standTimer <= 0f)
        {
            _standTimer = 6f + (float)_rng.NextDouble() * 7f;
            _standHold = surge > 0.5f && _rng.NextDouble() < 0.6 ? 2f + (float)_rng.NextDouble() * 2.5f : 0f;
        }
        _standHold = Mathf.Max(0f, _standHold - dt);
        _stand = Mathf.MoveTowards(_stand, _standHold > 0f ? 1f : 0f, dt / 0.35f);

        // ---- looking around when easy -----------------------------------------------------
        _lookTimer -= dt;
        if (_lookTimer <= 0f)
        {
            _lookTimer = 2.5f + (float)_rng.NextDouble() * 4f;
            _lookTarget = _rng.NextDouble() < 0.55 ? 0f : ((float)_rng.NextDouble() * 2f - 1f) * 32f;
        }
        float lookWeight = Mathf.Max(relaxed * (1f - sprint), rec * 0.4f);
        _look = Mathf.Lerp(_look, _lookTarget * lookWeight, 1f - Mathf.Exp(-dt * 2.2f));

        // ---- oscillators ------------------------------------------------------------------
        float strokeHz = Mathf.Max(0.3f, _cadence / 60f);          // one heave per pedal rev
        _heavePhase = Mathf.Repeat(_heavePhase + dt * strokeHz * Mathf.PI * 2f, Mathf.PI * 2f);
        float breathHz = Mathf.Lerp(0.30f, 0.62f, Mathf.Max(Band(I, 0.8f, 2.0f), rec));
        _breathPhase = Mathf.Repeat(_breathPhase + dt * breathHz * Mathf.PI * 2f, Mathf.PI * 2f);
        float breath = Mathf.Sin(_breathPhase);
        float heave = Mathf.Sin(_heavePhase);

        // ---- write (ADD to what KuroRidePose set this frame) -------------------------------
        float k = actingStrength;
        float quiet = 1f - 0.6f * spin;
        rig.poseExtraSpineLeanDegrees += k * (-5f * relaxed + 6f * tuck + 4f * surge + 3f * grind
                                              - 9f * rec + breath * (0.6f + 2.2f * rec));
        rig.poseExtraHeadLiftDegrees += k * (7f * relaxed + 4f * tuck - 3f * grind + 9f * rec);
        rig.poseExtraNeckLiftDegrees += k * (3f * tuck + 2f * surge);
        rig.poseExtraShoulderDropDegrees += k * (2f * tuck + 1.5f * surge - 2f * rec + breath * 1.2f * rec);
        rig.poseStandRockDegrees += k * quiet * heave * (3.5f * surge + 4.5f * grind + 1.2f * tuck);
        rig.poseStandRiseM += k * (0.055f * _stand * surge + 0.012f * grind * Mathf.Max(0f, heave));
        rig.poseStandForwardM += k * 0.04f * _stand * surge;
        rig.poseHeadYawDegrees = k * _look;
        rig.poseHeadRollDegrees = k * (Mathf.Sin(_breathPhase * 0.5f) * 5f * rec + heave * 1.5f * grind);

        // Full sprint: hand the silhouette to the existing sprint pose (keyboard S still works).
        if (pose != null) pose.SprintOverride = sprint > 0.5f ? 1f : float.NaN;
    }
}

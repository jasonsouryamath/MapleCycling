using System;
using UnityEngine;

/// <summary>One telemetry frame from whatever is producing the rider's effort.</summary>
public struct RideTelemetry
{
    public float Watts;
    public float CadenceRpm;
    public float HeartRateBpm;
    public bool PowerConnected;
    public bool HeartRateConnected;
    public string SourceLabel;
}

/// <summary>
/// The device layer's contract. Deliberately independent of Unity gameplay: the same
/// interface will front a real Wahoo FTMS connection and the simulator below, so nothing
/// downstream of <see cref="DeviceManager"/> can tell the difference.
/// </summary>
public interface IRideTelemetrySource
{
    string Label { get; }
    bool IsConnected { get; }
    /// <param name="grade">Road gradient at the rider, so a source can model rider behaviour.</param>
    void Tick(float dt, float grade, float effortInput, float ftpWatts);
    RideTelemetry Read();
    /// <summary>Simulated gradient sent back to the trainer (FTMS 0x11). No-op when simulated.</summary>
    void SetSimulatedGrade(float grade);
}

/// <summary>
/// Telemetry simulator standing in for the physical trainer.
///
/// NOT A REAL TRAINER. The hardware milestone (design handoff section 13, milestone 1) needs a
/// physical Wahoo on BLE FTMS, which is not available in this environment, so the ride
/// foundation is built against this mockable source behind the same interface. Everything
/// downstream - physics, HUD, GPS, checkpoints - is therefore already hardware-ready, and the
/// hardware path itself is explicitly NOT VERIFIED.
///
/// Models a rider holding an endurance power band, surging on climbs and soft-pedalling on
/// descents, with keyboard effort as the player's lever - so the pedalling input still drives
/// the game exactly the way real watts will.
/// </summary>
[Serializable]
public class SimulatedTrainerSource : IRideTelemetrySource
{
    [Tooltip("PROVISIONAL: the fraction of FTP an unforced scenic ride settles at.")]
    public float enduranceFraction = 0.72f;
    [Tooltip("PROVISIONAL: riders soft-pedal downhill.")]
    public float descentFraction = 0.25f;
    [Tooltip("PROVISIONAL: extra power a rider puts out per 1.0 of gradient on a climb.")]
    public float climbResponse = 1.15f;
    public float responseHz = 0.9f;
    public float noiseWatts = 9f;
    [Tooltip("PROVISIONAL: cadence a rider holds at endurance power.")]
    public float baseCadenceRpm = 88f;
    public float restingHrBpm = 62f;
    public float hrPerFtpFraction = 108f;
    public float hrResponseHz = 0.12f;

    [Tooltip("PROVISIONAL: how quickly the simulator tracks the player's own pedal strokes. " +
             "Faster than responseHz on purpose - a mash IS the rider's effort, not a rider " +
             "settling into a band, so it must not feel like it is being filtered.")]
    public float mashResponseHz = 3.0f;

    /// <summary>
    /// Live watts asserted by the player's own pedal strokes (see <see cref="MashPedalDrive"/>),
    /// or NaN when nothing is mashing and the endurance band should run instead.
    ///
    /// Set by <see cref="DeviceManager"/> immediately before <see cref="Tick"/>. It is a field
    /// rather than an extra <see cref="IRideTelemetrySource.Tick"/> argument so the device
    /// contract - and therefore the hardware path - is untouched: a real trainer gets its watts
    /// from the rider's legs and simply has no equivalent of this.
    ///
    /// NOT SERIALIZED: this is per-frame input, never authored state.
    /// </summary>
    [NonSerialized] public float MashWattsTarget = float.NaN;

    /// <summary>Cadence implied by the mash rate, or NaN to derive it from watts as before.</summary>
    [NonSerialized] public float MashCadenceRpm = float.NaN;

    private float _watts;
    private float _cadence;
    private float _hr = 70f;
    private const float SimMaxHrBpm = 192f;
    private float _phase;
    private float _grade;

    public string Label => "Telemetry simulator (no trainer)";
    public bool IsConnected => true;

    public void SetSimulatedGrade(float grade) { _grade = grade; }

    public void Tick(float dt, float grade, float effortInput, float ftpWatts)
    {
        _grade = grade;
        bool mashDriven = !float.IsNaN(MashWattsTarget);

        float target;
        float response;
        if (mashDriven)
        {
            // The player's pedal strokes ARE the power. Deliberately no endurance floor, no
            // climb surge and no noise wobble: every one of those would put watts into the
            // cranks that the player never produced, which is exactly the "Kuro rides off on
            // his own" behaviour this replaces. Stop mashing and this goes to zero.
            target = Mathf.Max(0f, MashWattsTarget);
            response = mashResponseHz;
        }
        else
        {
            // Legacy autonomous rider: still used by the scripted harnesses (captures,
            // benchmarks, the ride self-test) that own EffortInput themselves, and by the
            // ride-time ESTIMATE path in RideSession.EstimateLapMinutes.
            float band = grade < -0.015f ? descentFraction : enduranceFraction;
            band += Mathf.Max(0f, grade) * climbResponse;
            // effortInput is -1..+1 from the player: the cycling input is still the controller.
            band *= 1f + Mathf.Clamp(effortInput, -1f, 1f) * 0.55f;
            target = Mathf.Max(0f, ftpWatts * band);

            _phase += dt;
            float wobble = Mathf.Sin(_phase * 2.3f) * 0.55f + Mathf.Sin(_phase * 5.7f) * 0.45f;
            target += wobble * noiseWatts;
            response = responseHz;
        }

        _watts = Mathf.Lerp(_watts, Mathf.Max(0f, target), 1f - Mathf.Exp(-response * 6f * dt));
        if (mashDriven && _watts < 0.5f) _watts = 0f;   // no residual creep at a standstill

        float cadenceTarget;
        if (mashDriven)
        {
            // One press is one pedal stroke, so the mash rate IS the cadence. Deriving it from
            // watts instead would show a rider spinning 88 rpm while standing still.
            cadenceTarget = float.IsNaN(MashCadenceRpm) ? 0f : Mathf.Max(0f, MashCadenceRpm);
        }
        else
        {
            cadenceTarget = _watts < 5f ? 0f
                : baseCadenceRpm + Mathf.Clamp(_watts / Mathf.Max(1f, ftpWatts) - 1f, -0.5f, 0.6f) * 16f
                  - Mathf.Max(0f, grade) * 55f;
        }
        _cadence = Mathf.Lerp(_cadence, Mathf.Max(0f, cadenceTarget),
                              1f - Mathf.Exp(-response * 5f * dt));

        // Linear in watts was unbounded: a mashed 572 W sprint showed 521 bpm (user playtest).
        // Saturate toward a realistic max instead; below FTP it is still nearly linear.
        float linear = hrPerFtpFraction * (_watts / Mathf.Max(1f, ftpWatts));
        float headroom = SimMaxHrBpm - restingHrBpm;
        float hrTarget = restingHrBpm + headroom * (1f - Mathf.Exp(-linear / Mathf.Max(1f, headroom)));
        _hr = Mathf.Lerp(_hr, hrTarget, 1f - Mathf.Exp(-hrResponseHz * 6f * dt));
    }

    public RideTelemetry Read() => new RideTelemetry
    {
        Watts = _watts,
        CadenceRpm = _cadence,
        HeartRateBpm = _hr,
        PowerConnected = true,
        HeartRateConnected = true,
        SourceLabel = Label,
    };
}

/// <summary>
/// The interactive player's pedal strokes: MASH TO RIDE.
///
/// Kuro does not pedal on his own. Every discrete press of the stroke key is ONE pedal stroke
/// that injects a burst of power into a charge that decays continuously; the faster the player
/// mashes, the higher the charge settles, and when they stop the charge falls to zero and the
/// bike coasts down on rolling resistance and gravity (which <see cref="CyclingPhysics"/>
/// already models - nothing here brakes for the player).
///
/// Steady-state power for a mash rate f is approximately
/// <c>wattsPerStroke * f * decaySeconds</c>, so with the defaults below a lazy 2 presses/second
/// is an endurance ~119 W and a hard 5 presses/second is a strong ~297 W. One lone tap is a
/// single ~70 W spike - a nudge, not a launch.
///
/// The normal mash model remains discrete and edge-triggered. The production keyboard fallback
/// can additionally map a held Up Arrow to a named steady effort when no trainer is active; it
/// still enters through this model and the normal telemetry/physics path rather than moving the
/// rider directly.
///
/// EVERY NUMBER HERE IS PROVISIONAL illustrative tuning (design handoff section 12: FTP setup
/// and effort mapping are unresolved). Serialized so they can be tuned without a recompile.
/// </summary>
[Serializable]
public class MashPedalDrive
{
    [Header("Bindings (provisional)")]
    [Tooltip("The pedal-stroke key. One key-DOWN is one stroke; holding it does nothing.")]
    public KeyCode strokeKey = KeyCode.UpArrow;
    [Tooltip("Optional second stroke key. Deliberately never S or down-arrow: easing off is " +
             "not a throttle, and S is now the sprint-pose key.")]
    public KeyCode strokeKeyAlt = KeyCode.W;

    [Header("Keyboard hold fallback")]
    [Tooltip("Production keyboard fallback used only when no external power trainer is active. " +
             "Holding the key produces a steady cycling effort through DeviceManager and the " +
             "normal CyclingPhysics path; it never teleports the rider.")]
    public bool keyboardHoldEnabled = true;
    public KeyCode keyboardHoldKey = KeyCode.UpArrow;
    [Tooltip("PROVISIONAL steady effort while the keyboard fallback is held.")]
    public float keyboardHoldWatts = 220f;
    [Tooltip("PROVISIONAL seconds to ramp from rest to the held keyboard effort.")]
    public float keyboardHoldRampSeconds = 0.45f;
    [Tooltip("PROVISIONAL seconds for keyboard effort to fall to zero after release.")]
    public float keyboardReleaseSeconds = 0.55f;
    [Tooltip("PROVISIONAL cadence supplied to the continuous crank animation while held.")]
    public float keyboardHoldCadenceRpm = 88f;
    [NonSerialized] public float keyboardHoldOverride = float.NaN;
    [Tooltip("Holding the key BUILDS effort toward this (user 2026-09-25: gradual, max 2000 W, so " +
             "the effort-acting poses can be seen). ~1 s to 150 W, ~5 s to 400 W, ~10 s to 800 W, " +
             "~20 s to 2000 W.")]
    public float keyboardHoldMaxWatts = 2000f;
    private bool _keyboardHeld;
    private bool _keyboardHoldOwnsEffort;

    [Header("TEMPORARY — map scout / flythrough")]
    [Tooltip("TEMPORARY DEBUG MODE. When on, the one-press-per-stroke pedal method is DISABLED. " +
             "Instead, HOLD scoutHoldKey to accelerate up to scoutMaxWatts for a fast flythrough " +
             "so the map layout can be scouted and bugs found. Down arrow stays free for braking. " +
             "Turn OFF to restore mash-to-ride.")]
    public bool scoutHoldMode = true;
    [Tooltip("TEMPORARY: hold this key to accelerate in scout mode. Up arrow = go, down = brake.")]
    public KeyCode scoutHoldKey = KeyCode.UpArrow;
    [Tooltip("TEMPORARY: power ceiling while holding the scout key. 1000 W = really fast.")]
    public float scoutMaxWatts = 1000f;
    [Tooltip("TEMPORARY: seconds to ramp from 0 to scoutMaxWatts while held.")]
    public float scoutRampSeconds = 1.4f;
    private bool _scoutHeld;

    [Header("Power (provisional)")]
    [Tooltip("Watts injected by ONE pedal stroke, before decay.")]
    public float wattsPerStroke = 70f;
    [Tooltip("Seconds for un-mashed power to decay to ~37 % of its value. Longer = the effort " +
             "'hangs' between strokes and a slow mash still rides; shorter = frantic.")]
    public float decaySeconds = 0.85f;
    [Tooltip("Ceiling on mashed power, watts. Stops a key-repeat macro reaching 2 kW.")]
    public float maxWatts = 650f;
    [Tooltip("Output smoothing, seconds. Without it the HUD watts read as a sawtooth.")]
    public float smoothingSeconds = 0.12f;
    [Tooltip("Below this the rider is treated as not pedalling at all, so a standstill is a " +
             "true zero rather than a few residual watts creeping the bike forward.")]
    public float idleWattsFloor = 3f;

    [Header("Cadence (provisional)")]
    [Tooltip("Pedal strokes per crank revolution. Two: left foot, right foot.")]
    public float strokesPerRevolution = 2f;
    [Tooltip("Seconds the mash-rate estimate is averaged over, for the cadence readout.")]
    public float rateWindowSeconds = 0.9f;

    private float _charge;     // raw accumulated stroke power, watts
    private float _watts;      // smoothed output, watts
    private float _rate;       // smoothed strokes per second
    private int _pending;      // strokes banked since the last Tick

    /// <summary>Smoothed mashed power in watts. Zero means the player is not pedalling.</summary>
    public float Watts => _watts;
    /// <summary>Smoothed mash rate, presses per second.</summary>
    public float StrokesPerSecond => _rate;
    /// <summary>Crank cadence implied by the mash rate.</summary>
    public float CadenceRpm => _rate * 60f / Mathf.Max(0.5f, strokesPerRevolution);
    public bool KeyboardHeld => _keyboardHeld;

    /// <summary>
    /// Banks one pedal stroke. Public so a capture harness or a self-test can mash without a
    /// keyboard - editor tooling cannot press keys, and a mash model that can only be verified
    /// by hand is a mash model that is never verified.
    /// </summary>
    public void Stroke() { _pending++; }

    /// <summary>Edge-triggered keyboard sampling. Play mode only; call once per frame.</summary>
    public void SampleKeyboard()
    {
        if (scoutHoldMode)
        {
            // TEMPORARY scout mode: the one-press-per-stroke method is disabled; holding
            // scoutHoldKey ramps power for a fast flythrough (see ScoutTick).
            _scoutHeld = Input.GetKey(scoutHoldKey);
            return;
        }
        if (keyboardHoldEnabled)
        {
            _keyboardHeld = float.IsNaN(keyboardHoldOverride)
                ? Input.GetKey(keyboardHoldKey)
                : keyboardHoldOverride >= 0.5f;
            if (strokeKey != keyboardHoldKey && Input.GetKeyDown(strokeKey)) Stroke();
            if (strokeKeyAlt != KeyCode.None && strokeKeyAlt != keyboardHoldKey &&
                strokeKeyAlt != strokeKey && Input.GetKeyDown(strokeKeyAlt))
                Stroke();
            return;
        }
        if (Input.GetKeyDown(strokeKey)) Stroke();
        if (strokeKeyAlt != KeyCode.None && strokeKeyAlt != strokeKey &&
            Input.GetKeyDown(strokeKeyAlt)) Stroke();
    }

    public void Tick(float dt)
    {
        if (dt <= 0f) return;

        if (scoutHoldMode) { ScoutTick(dt); return; }
        if (keyboardHoldEnabled)
        {
            if (_keyboardHeld)
            {
                _keyboardHoldOwnsEffort = true;
                KeyboardHoldTick(dt);
                return;
            }
            if (_keyboardHoldOwnsEffort && _pending == 0)
            {
                KeyboardHoldTick(dt);
                if (_watts <= 0f && _rate <= 0f) _keyboardHoldOwnsEffort = false;
                return;
            }
            _keyboardHoldOwnsEffort = false;
        }

        StrokeTick(dt);
    }

    private void StrokeTick(float dt)
    {
        _charge += _pending * Mathf.Max(0f, wattsPerStroke);
        float rateSample = _pending / dt;
        _pending = 0;

        _charge *= Mathf.Exp(-dt / Mathf.Max(0.01f, decaySeconds));
        _charge = Mathf.Min(_charge, Mathf.Max(0f, maxWatts));

        _watts = Mathf.Lerp(_watts, _charge,
                            1f - Mathf.Exp(-dt / Mathf.Max(0.01f, smoothingSeconds)));
        if (_watts < idleWattsFloor) _watts = 0f;

        _rate = Mathf.Lerp(_rate, rateSample,
                           1f - Mathf.Exp(-dt / Mathf.Max(0.05f, rateWindowSeconds)));
        if (_rate < 0.05f) _rate = 0f;
    }

    public void Reset()
    {
        _charge = 0f;
        _watts = 0f;
        _rate = 0f;
        _pending = 0;
        _scoutHeld = false;
        _keyboardHeld = false;
        _keyboardHoldOwnsEffort = false;
    }

    private void KeyboardHoldTick(float dt)
    {
        if (_keyboardHeld)
        {
            // A rider winds up: quick to a cruising effort, then progressively harder to find
            // more (W/s by band), topping out at a full sprint.
            float w = _watts;
            float rate = w < 150f ? 150f : w < 400f ? 60f : w < 800f ? 80f : 120f;
            _watts = Mathf.Min(Mathf.Max(0f, keyboardHoldMaxWatts), w + rate * dt);
        }
        else
        {
            // Easing off, not a switch: ~1 s time constant.
            _watts *= Mathf.Exp(-dt / Mathf.Max(0.05f, keyboardReleaseSeconds * 1.8f));
            if (_watts < idleWattsFloor) _watts = 0f;
        }
        _charge = _watts;
        _pending = 0;

        // Cadence follows effort: ~85 rpm cruising -> ~100 at threshold -> ~118 flat-out.
        float cadence = _keyboardHeld || _watts > 0f
            ? Mathf.Lerp(keyboardHoldCadenceRpm - 5f, 118f, Mathf.Clamp01((_watts - 120f) / 1400f))
            : 0f;
        float targetRate = cadence * Mathf.Max(0.5f, strokesPerRevolution) / 60f;
        _rate = Mathf.Lerp(_rate, targetRate, 1f - Mathf.Exp(-dt / 0.4f));
        if (!_keyboardHeld && _rate < 0.05f) _rate = 0f;
    }

    /// <summary>
    /// TEMPORARY map-scout drive: HOLD scoutHoldKey to ramp power up to scoutMaxWatts, release to
    /// spin down fast. Bypasses the stroke/decay model entirely so the flythrough is smooth and
    /// predictable. Delete this and clear <see cref="scoutHoldMode"/> to restore mash-to-ride.
    /// </summary>
    private void ScoutTick(float dt)
    {
        float cap = Mathf.Max(0f, scoutMaxWatts);
        float rampUp = cap / Mathf.Max(0.05f, scoutRampSeconds);
        float target = _scoutHeld ? cap : 0f;
        float rate = _scoutHeld ? rampUp : cap / 0.4f;   // release spins down quickly
        _watts = Mathf.MoveTowards(_watts, target, rate * dt);
        if (!_scoutHeld && _watts < idleWattsFloor) _watts = 0f;
        _charge = _watts;
        // Keep cadence lively while pedalling so the pose/HUD animate.
        _rate = _watts > idleWattsFloor ? 3f : 0f;
    }
}

/// <summary>
/// Owns the active telemetry source and the resistance channel back to it.
///
/// Architecture from the design handoff section 2:
/// <c>DeviceManager -&gt; CyclingPhysics/HUD</c> and <c>TrainerController -&gt; trainer</c>.
/// The gradient pushed back to the trainer is the SAME <c>RouteCourse.GradeAt</c> the HUD badge
/// shows, so what the rider sees and what they feel cannot drift.
/// </summary>
[DefaultExecutionOrder(-200)]
public class DeviceManager : MonoBehaviour
{
    public enum SourceKind
    {
        /// <summary>Mockable stand-in. The only source available without hardware.</summary>
        Simulator = 0,
        /// <summary>Reserved for the BLE FTMS trainer. NOT IMPLEMENTED / NOT VERIFIED.</summary>
        BluetoothFtms = 1,
    }

    /// <summary>Where the heart-rate readout comes from. Independent of the power source.</summary>
    public enum HeartRateSourceKind
    {
        /// <summary>Modelled from watts by the simulator. No strap needed.</summary>
        Simulated = 0,
        /// <summary>A real BLE strap (e.g. Wahoo TICKR) over the Heart Rate Service.</summary>
        BluetoothLe = 1,
    }

    /// <summary>Where the rider's effort comes from while no real trainer is attached.</summary>
    public enum EffortSource
    {
        /// <summary>Discrete pedal strokes from the mash key. The player must pedal.</summary>
        MashPedalStrokes = 0,
        /// <summary>Legacy held axis feeding the simulator's endurance band.</summary>
        ContinuousAxis = 1,
    }

    [Header("Source")]
    public SourceKind source = SourceKind.Simulator;
    public SimulatedTrainerSource simulator = new SimulatedTrainerSource();

    [Header("Heart rate monitor")]
    [Tooltip("Simulated: heart rate is modelled from watts, no strap required. BluetoothLe: " +
             "connect a real BLE strap such as the Wahoo TICKR - its live bpm then replaces the " +
             "modelled value on the HUD. The Bluetooth transport is provided by the MapleRideBle " +
             "plugin and works on Windows (see Assets/Ride/HeartRate/README.md).")]
    public HeartRateSourceKind heartRate = HeartRateSourceKind.Simulated;

    [Tooltip("Substring matched (case-insensitive) against the strap's advertised name. 'TICKR' " +
             "picks a Wahoo TICKR; leave empty to take the first device advertising the Heart " +
             "Rate Service.")]
    public string heartRateDeviceName = "TICKR";

    [Tooltip("Seconds without a heartbeat notification before the strap is treated as dropped.")]
    public float heartRateTimeoutSeconds = 5f;

    [Header("Rider profile (provisional - FTP setup is an unresolved design decision)")]
    [Tooltip("PROVISIONAL: used for %FTP, W/kg and the ride-time estimate that picks lap counts.")]
    public float ftpWatts = 220f;
    public float riderMassKg = 68f;

    [Header("Input")]
    [Tooltip("Reads the player's pedal input. Turn OFF when a harness owns EffortInput - that " +
             "is also what puts the simulator back on its legacy autonomous endurance band.")]
    public bool acceptKeyboardEffort = true;

    [Tooltip("MashPedalStrokes: the player mashes the stroke key and Kuro only moves when they " +
             "do (the shipping input). ContinuousAxis: the old held W/S endurance-band lever, " +
             "kept for scripted rides and as a fallback. PROVISIONAL.")]
    public EffortSource effortSource = EffortSource.MashPedalStrokes;

    [Tooltip("The mash-to-ride pedal stroke model. All tuning provisional.")]
    public MashPedalDrive mash = new MashPedalDrive();

    public RideTelemetry Telemetry { get; private set; }
    public float EffortInput { get; set; }
    public float LastSimulatedGrade { get; private set; }

    /// <summary>
    /// QA fix (trainer telemetry ignores zero effort input): while <c>acceptKeyboardEffort</c>
    /// is false, the legacy path deliberately runs the simulator's autonomous endurance band
    /// regardless of <see cref="EffortInput"/> - that is load-bearing, existing behaviour
    /// (RideValidation's course self-test, the GPS/HUD capture harnesses' "Warm" helpers, and
    /// RideInputValidation's own "legacy scripted path still rides autonomously" check all
    /// depend on a harness being able to drive a course with EffortInput left at 0), so it must
    /// not change by default.
    ///
    /// But two OTHER harnesses (ShiosaiPlaymodeRunner/MinatoPlaymodeRunner's traffic-burst and
    /// -audit passes, and MinatoIdleStartRunner) set the exact same acceptKeyboardEffort=false,
    /// EffortInput=0 and documented their INTENT as "the player held stationary" - the opposite
    /// meaning. MinatoIdleStartRunner discovered the two meanings collide (zero effort kept
    /// producing ~160 W and the rider drifted) and worked around it by pinning the road
    /// position directly every frame instead of trusting EffortInput to hold it.
    ///
    /// Setting this true disambiguates the collision explicitly: it forces the SAME "coasts
    /// to a stop" physics already used and verified for the player's own mash (see
    /// MashPedalDrive / RideInputValidation's "stop mashing -> coasts to a stop" check) instead
    /// of the autonomous band, for exactly as long as it is held true. Harnesses that want the
    /// autonomous band keep working unmodified; harnesses that want the rider genuinely
    /// stationary can now ask for it directly instead of pinning position.
    /// </summary>
    public bool HoldZeroPower { get; set; }

    /// <summary>True while a real heart-rate strap is connected and streaming beats.</summary>
    public bool HeartRateConnected { get; private set; }

    /// <summary>Status of the heart-rate feed for the HUD / logs.</summary>
    public string HeartRateLabel => _hr != null ? _hr.Label : "HR: simulated";

    /// <summary>Live heart-rate diagnostics (status + devices seen), for the pairing popover.</summary>
    public string HeartRateDiagnostics => _hr != null ? _hr.Diagnostics : "";

    /// <summary>
    /// Whether a real Bluetooth heart-rate transport is available in this build. True on Windows,
    /// where the MapleRideBle plugin provides the WinRT connection; false elsewhere, so the UI can
    /// say so instead of scanning forever.
    /// </summary>
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    public const bool HeartRateTransportAvailable = true;
#else
    public const bool HeartRateTransportAvailable = false;
#endif

    /// <summary>Live mashed power in watts, or 0 when the mash model is not driving.</summary>
    public float MashWatts => MashDrivesEffort ? mash.Watts : 0f;

    /// <summary>
    /// True only for a connected non-simulator power source. The simulator reports connected
    /// because it is a valid telemetry source, but it must never suppress keyboard fallback.
    /// </summary>
    public bool ExternalTrainerActive =>
        source == SourceKind.BluetoothFtms &&
        !(Active is SimulatedTrainerSource) &&
        Active.IsConnected;

    /// <summary>
    /// True when the player's own pedal strokes are producing the watts. False whenever a
    /// harness has taken ownership of <see cref="EffortInput"/> (<c>acceptKeyboardEffort =
    /// false</c>), which is what keeps every existing capture, benchmark and self-test running
    /// the autonomous endurance band exactly as it did before.
    /// </summary>
    public bool MashDrivesEffort =>
        effortSource == EffortSource.MashPedalStrokes &&
        acceptKeyboardEffort &&
        !ExternalTrainerActive;

    /// <summary>
    /// TEMPORARY: true while the map-scout flythrough drive is active (hold-to-accelerate at up to
    /// <c>mash.scoutMaxWatts</c>). Consumed by <see cref="RideSession"/> to lift the descent speed
    /// scrub so 1000 W actually feels fast. Clear <c>mash.scoutHoldMode</c> to disable.
    /// </summary>
    public bool ScoutModeActive => MashDrivesEffort && mash.scoutHoldMode;

    private IRideTelemetrySource _active;
    private IHeartRateSource _hr;
    private bool _externalTrainerWasActive;

    public IRideTelemetrySource Active
    {
        get
        {
            if (_active == null) _active = simulator;
            return _active;
        }
    }

    public string SourceLabel =>
        source == SourceKind.BluetoothFtms ? "FTMS trainer (not connected)" : Active.Label;

    private void Awake()
    {
        _active = simulator;
        if (source == SourceKind.BluetoothFtms)
            Debug.LogWarning("[ride] DeviceManager: BluetoothFtms is a placeholder - no hardware " +
                             "layer is implemented. Falling back to the telemetry simulator.");
        RestartHeartRate();
    }

    private void OnDestroy()
    {
        if (_hr != null) { _hr.Stop(); _hr = null; }
    }

    /// <summary>
    /// (Re)builds the heart-rate source from the current settings. Tears down any existing
    /// connection first, so it doubles as a manual reconnect for live testing (bound to a key in
    /// <see cref="RideBootstrap"/>).
    /// </summary>
    public void RestartHeartRate()
    {
        if (_hr != null) { _hr.Stop(); _hr = null; }
        HeartRateConnected = false;
        if (heartRate == HeartRateSourceKind.BluetoothLe)
        {
            _hr = new BleHeartRateSource(heartRateDeviceName, heartRateTimeoutSeconds);
            _hr.Start();
        }
    }

    /// <summary>Deterministic tick so editor harnesses can drive a ride without play mode.</summary>
    public void Tick(float dt, float grade)
    {
        bool externalTrainerActive = ExternalTrainerActive;
        if (externalTrainerActive)
        {
            if (!_externalTrainerWasActive) mash.Reset();
            simulator.MashWattsTarget = float.NaN;
            simulator.MashCadenceRpm = float.NaN;
            EffortInput = 0f;
        }
        else if (MashDrivesEffort)
        {
            // Mash to ride. Key edges can only be read in play mode; Stroke() is the harness
            // route in, so the same model is what gets verified in a capture.
            // During the start-line freeze the player's strokes are not merely ignored, they are
            // never banked: otherwise a rider mashing through "3 . 2 . 1" would launch off the
            // line on stored power the instant GO lands.
            if (Application.isPlaying && !RideInputGate.Locked) mash.SampleKeyboard();
            mash.Tick(dt);
            simulator.MashWattsTarget = mash.Watts;
            simulator.MashCadenceRpm = mash.CadenceRpm;
            // Kept meaningful for anything reading effort as a 0..1 intensity (HUD, encounters).
            EffortInput = Mathf.Clamp01(mash.Watts / Mathf.Max(1f, ftpWatts));
        }
        else if (HoldZeroPower)
        {
            // QA fix: an explicit request to hold at literally zero power, using the SAME
            // decay path as the player's own mash coasting down (see MashPedalDrive) rather
            // than the autonomous endurance band below - see HoldZeroPower's doc comment.
            simulator.MashWattsTarget = 0f;
            simulator.MashCadenceRpm = 0f;
            EffortInput = 0f;
        }
        else
        {
            // Legacy path: a harness (or the ContinuousAxis fallback) owns the effort, and the
            // simulator runs its autonomous endurance band. NaN is what selects that band, so
            // it MUST be cleared here or a harness would silently inherit the last mash.
            simulator.MashWattsTarget = float.NaN;
            simulator.MashCadenceRpm = float.NaN;
            if (acceptKeyboardEffort && Application.isPlaying && !RideInputGate.Locked)
            {
                float v = Input.GetAxisRaw("Vertical");
                EffortInput = Mathf.MoveTowards(EffortInput, v, dt * 2.5f);
            }
        }

        Active.Tick(dt, grade, EffortInput, ftpWatts);
        Telemetry = Active.Read();
        _externalTrainerWasActive = externalTrainerActive;
        ApplyHeartRate(dt);
        LastSimulatedGrade = grade;
        Active.SetSimulatedGrade(grade);     // TrainerController channel; no-op while simulated
    }

    /// <summary>
    /// Overlays a real strap's heart rate onto the telemetry frame. The strap only produces HR,
    /// so nothing else in the frame is touched; while it is disconnected the simulated bpm from
    /// <see cref="Active"/> is left untouched as the fallback.
    /// </summary>
    private void ApplyHeartRate(float dt)
    {
        if (_hr == null) { HeartRateConnected = false; return; }
        _hr.Poll(dt);
        HeartRateConnected = _hr.IsConnected;
        if (!HeartRateConnected) return;

        var t = Telemetry;
        t.HeartRateBpm = _hr.BeatsPerMinute;
        t.HeartRateConnected = true;
        Telemetry = t;
    }

    /// <summary>
    /// Banks one pedal stroke. The public mash entry point for harnesses and for any future
    /// input device (a real crank sensor lands here too).
    /// </summary>
    public void PedalStroke() { mash.Stroke(); }

    /// <summary>Drops all banked effort. Called when the ride is reset, so [R] really stops him.</summary>
    public void ResetEffort()
    {
        mash.Reset();
        if (MashDrivesEffort) EffortInput = 0f;
    }
}

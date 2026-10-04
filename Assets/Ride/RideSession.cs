using System;
using UnityEngine;

/// <summary>
/// The ride session: the single source of truth for where the rider is on the course.
///
/// Everything else is derived from it. The rider transform is <c>course.PositionAt(d)</c>, the
/// GPS window reads <see cref="DistanceM"/>/<see cref="HeadingDeg"/>, the checkpoint banner reads
/// <see cref="RouteDirector"/> which reads this, and the trainer resistance is
/// <see cref="Grade"/>. Nothing raycasts, and nothing reads the rider transform back.
///
/// Ride duration is hit by choosing LAPS at run time, never by fixing a course length - a
/// stronger rider finishes a lap sooner, so the lap count is derived from the player's own FTP
/// through <see cref="EstimateLapMinutes"/>.
/// </summary>
[DefaultExecutionOrder(-100)]
public class RideSession : MonoBehaviour
{
    [Header("Course")]
    [Tooltip("Course id from the baked RouteGraph: pass_sprint / sakura_circuit / " +
             "aozora_loop / gran_fondo.")]
    public string courseId = "sakura_circuit";

    [Tooltip("PROVISIONAL: session length the ride plan is scheduled against, in minutes.")]
    public float targetDurationMinutes = 30f;

    [Tooltip("Derive the lap count from the target duration and the rider's FTP. Turn off to " +
             "ride a fixed number of laps.")]
    public bool autoLapsFromTarget = true;

    [Min(1)] public int plannedLaps = 3;

    [Header("Systems")]
    public DeviceManager devices;
    public CyclingPhysics physics = new CyclingPhysics();

    [Header("Tuning (provisional)")]
    [Tooltip("Rolling window the displayed / transmitted gradient is measured over, in metres.")]
    public float gradeWindowM = 8f;
    [Tooltip("Gradient is clamped to +/- this for DISPLAY only, so hairpin noise cannot flicker.")]
    public float gradeDisplayClampPct = 20f;
    public float startDistanceM = 0f;

    // ---------------------------------------------------------------- live state
    public RouteGraph Graph { get; set; }
    public RouteCourse Course { get; private set; }

    /// <summary>Arc metres around the current lap.</summary>
    public float DistanceM { get; private set; }
    /// <summary>Arc metres ridden since the session started, across every lap.</summary>
    public float TotalDistanceM { get; private set; }
    public float SpeedMps { get; private set; }

    /// <summary>
    /// Optional one-frame ceiling on <see cref="SpeedMps"/>, in m/s, asserted by whatever is in
    /// front of the rider (currently ShiosaiTrafficDirector's traffic blocking). NaN = no cap.
    /// Set it EVERY frame it should apply: Tick consumes and clears it, so a system that stops
    /// asserting - or is disabled, or leaves the region - can never leave the player throttled.
    /// </summary>
    [System.NonSerialized] public float SpeedCapMps = float.NaN;
    public float SpeedKph => SpeedMps * 3.6f;
    public float ElapsedSeconds { get; private set; }
    public float AscentM { get; private set; }
    public int LapIndex { get; private set; }
    public bool Finished { get; private set; }
    public float Grade { get; private set; }
    public float DisplayGradePct =>
        Mathf.Clamp(Grade * 100f, -gradeDisplayClampPct, gradeDisplayClampPct);

    public float HeadingDeg { get; private set; }
    public Vector3 WorldPosition { get; private set; }

    public int TotalLaps => Mathf.Max(1, plannedLaps);
    public float PlannedDistanceM => Course == null ? 0f : Course.Length * TotalLaps;
    public float RemainingM => Mathf.Max(0f, PlannedDistanceM - TotalDistanceM);
    public float NormalizedProgress =>
        PlannedDistanceM <= 0.01f ? 0f : Mathf.Clamp01(TotalDistanceM / PlannedDistanceM);

    public event Action<int> LapCompleted;
    public event Action SessionFinished;

    /// <summary>
    /// Raised whenever the ride is restarted or a new course is selected - i.e. whenever "this
    /// run" stops being the same run.
    ///
    /// Encounters need this as an explicit signal rather than inferring it from the elapsed
    /// clock. Inference works right up until something resets the ride at t = 0 (a scene load, a
    /// course switch during setup, a harness), at which point the encounter silently believes it
    /// is still mid-run and refuses to re-arm its once-per-run roll.
    /// </summary>
    public event Action RunReset;

    /// <summary>
    /// Identity of the CURRENT run. Minted fresh by every <see cref="ResetRide"/> and by nothing
    /// else, so "is this still the same run?" is an equality test rather than an inference.
    ///
    /// Rare encounters need this. Inferring a restart from the elapsed clock going backwards is
    /// wrong in both directions: a checkpoint restore that rewinds the clock looks like a new run
    /// (and hands the player a free reroll of a 1-in-30 sighting), while a restart that happens
    /// to land on the same clock value looks like the same run. The token has neither failure.
    ///
    /// It is deliberately NOT persisted. A process or scene reload puts the rider back at the
    /// start line with zero distance, which genuinely IS a new ride; pretending otherwise would
    /// be the bigger lie.
    /// </summary>
    public string RunToken { get; private set; } = "";

    /// <summary>Monotonic count of runs this session, for logs and the roll ledger.</summary>
    public int RunSerial { get; private set; }

    private float _brake;

    /// <summary>
    /// True while an NPC race (<see cref="RaceDirector"/>) drives the rider through
    /// <see cref="DriveExternally"/>. The ride model does not integrate, the ride clock and ride
    /// distance do not advance, and nothing banks effort; the race puts the rider back with
    /// <see cref="EndExternalControl"/>. Not serialized: a race never outlives the play session.
    /// </summary>
    [NonSerialized] public bool ExternalControl;

    /// <summary>Places the rider at a course position with a speed, as a race step decided them.</summary>
    public void DriveExternally(float courseMetres, float speedMps)
    {
        EnsureCourse();
        if (Course == null) return;
        DistanceM = Course.Wrap(Mathf.Max(0f, courseMetres));
        SpeedMps = Mathf.Max(0f, speedMps);
        Grade = Course.GradeAt(DistanceM, gradeWindowM);
        Sync();
    }

    private float _xDistance, _xTotal, _xAscent;
    private int _xLap;
    private bool _xFinished;

    /// <summary>Snapshots the ride and hands the rider to an external driver (an NPC race).</summary>
    public void BeginExternalControl()
    {
        _xDistance = DistanceM;
        _xTotal = TotalDistanceM;
        _xAscent = AscentM;
        _xLap = LapIndex;
        _xFinished = Finished;
        ExternalControl = true;
    }

    /// <summary>Puts the ride back EXACTLY as it was before <see cref="BeginExternalControl"/>
    /// (position, distance, ascent, lap), stopped.</summary>
    public void EndExternalControl()
    {
        if (!ExternalControl) return;
        ExternalControl = false;
        DistanceM = _xDistance;
        TotalDistanceM = _xTotal;
        AscentM = _xAscent;
        LapIndex = _xLap;
        Finished = _xFinished;
        SpeedMps = 0f;
        _brake = 0f;
        SpeedCapMps = float.NaN;
        if (Course != null) Grade = Course.GradeAt(DistanceM, gradeWindowM);
        if (devices != null) devices.ResetEffort();
        Sync();
    }

    private void Awake()
    {
        EnsureCourse();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    // ---------------------------------------------------------------- setup

    public void EnsureCourse()
    {
        if (Graph == null) Graph = RouteGraph.Load();
        if (Graph == null)
        {
            Debug.LogError("[ride] RouteGraph not found at Resources/" + RouteGraph.ResourceName +
                           ". Run MapleRide/Ride/Bake Route Graph.");
            return;
        }
        // A newly-created session with no explicit choice still gets the documented default.
        // A NON-empty unknown id does not: silently substituting graph.courses[0] previously
        // turned a failed Minato request into pass_sprint while every caller believed it worked.
        if (string.IsNullOrEmpty(courseId))
            courseId = Graph.Course("sakura_circuit") != null
                ? "sakura_circuit"
                : (Graph.courses.Length > 0 ? Graph.courses[0].id : "");
        if (Course == null || Course.Id != courseId)
            SelectCourse(courseId);
    }

    public void SelectCourse(string id)
    {
        if (Graph == null) Graph = RouteGraph.Load();
        if (Graph == null) return;

        var def = Graph.Course(id);
        if (def == null)
        {
            Debug.LogWarning($"[ride] course '{id}' is not present in the baked runtime graph; " +
                             $"keeping '{courseId}'.");
            return;
        }

        courseId = def.id;
        Course = RouteCourse.Build(Graph, def);
        if (autoLapsFromTarget)
        {
            targetDurationMinutes = Mathf.Max(5f, def.targetMinutes);
            plannedLaps = RecommendedLaps(targetDurationMinutes);
        }
        ResetRide();
    }

    public void CycleCourse(int direction)
    {
        if (Graph == null || Graph.courses.Length == 0) return;
        int i = Graph.CourseIndex(courseId);
        i = (i + direction + Graph.courses.Length) % Graph.courses.Length;
        SelectCourse(Graph.courses[i].id);
    }

    public void SetTargetDuration(float minutes)
    {
        targetDurationMinutes = Mathf.Clamp(minutes, 5f, 240f);
        if (autoLapsFromTarget) plannedLaps = RecommendedLaps(targetDurationMinutes);
    }

    public void AdjustLaps(int delta)
    {
        autoLapsFromTarget = false;
        plannedLaps = Mathf.Max(1, plannedLaps + delta);
    }

    public void ResetRide()
    {
        RunSerial++;
        RunToken = courseId + "#" + RunSerial + "#" + Guid.NewGuid().ToString("N").Substring(0, 8);
        DistanceM = Course != null ? Course.Wrap(startDistanceM) : 0f;
        TotalDistanceM = 0f;
        SpeedMps = 0f;
        ElapsedSeconds = 0f;
        AscentM = 0f;
        LapIndex = 0;
        Finished = false;
        // Banked pedal strokes are part of "this run" too. Without this, a restart puts the
        // rider back on the start line still carrying the power they were mashing out, and he
        // rolls away on his own - the exact behaviour the mash model exists to remove.
        if (devices != null) devices.ResetEffort();
        Sync();
        RunReset?.Invoke();
    }

    /// <summary>
    /// Jumps the ride to an arc position along the course.
    ///
    /// Exists so the capture harness and (later) Quick Ride can place the rider without faking
    /// state through reflection - every derived value is recomputed from the course, exactly as
    /// a normal tick would.
    /// </summary>
    public void SeekTo(float metres)
    {
        if (Course == null || Course.Length <= 0.01f) return;
        metres = Mathf.Max(0f, metres);
        DistanceM = Course.Wrap(metres);
        TotalDistanceM = metres;
        LapIndex = Mathf.FloorToInt(metres / Course.Length);
        AscentM = Course.Ascent * (metres / Course.Length);
        Grade = Course.GradeAt(DistanceM, gradeWindowM);
        Finished = false;
        Sync();
    }

    // ---------------------------------------------------------------- ride plan

    /// <summary>
    /// Minutes for one lap of the current course at the rider's endurance power. Integrated
    /// over the real course geometry with the real physics model, so it reflects a re-cut
    /// climb or a new segment automatically.
    /// </summary>
    public float EstimateLapMinutes() => EstimateLapMinutes(Course, physics, devices);

    public static float EstimateLapMinutes(RouteCourse course, CyclingPhysics physics,
                                           DeviceManager devices)
    {
        if (course == null || course.Count < 2) return 1f;
        float ftp = devices != null ? devices.ftpWatts : 220f;
        float endurance = devices != null ? devices.simulator.enduranceFraction : 0.72f;
        float descentFrac = devices != null ? devices.simulator.descentFraction : 0.25f;
        float cap = physics.descentCapKph / 3.6f;
        float minSpeed = 5f / 3.6f;

        double seconds = 0;
        const float stepM = 12f;
        for (float d = 0f; d < course.Length; d += stepM)
        {
            float span = Mathf.Min(stepM, course.Length - d);
            float grade = course.GradeAt(d + span * 0.5f, 12f);
            float watts = ftp * (grade < -0.015f ? descentFrac : endurance);
            float v = Mathf.Clamp(physics.TerminalSpeed(watts, grade), minSpeed, cap);
            seconds += span / v;
        }
        return (float)(seconds / 60.0);
    }

    public int RecommendedLaps(float minutes)
    {
        float lap = EstimateLapMinutes();
        if (lap <= 0.01f) return 1;
        return Mathf.Max(1, Mathf.RoundToInt(minutes / lap));
    }

    // ---------------------------------------------------------------- simulation

    /// <summary>
    /// Advances the ride. Exposed (rather than living inside Update) so an editor harness can
    /// drive a deterministic ride with no play mode, which is how the GPS window is captured
    /// and how the duration self-test runs.
    /// </summary>
    public void Tick(float dt)
    {
        EnsureCourse();
        if (Course == null || Course.Count < 2 || dt <= 0f) return;
        if (ExternalControl) { SpeedCapMps = float.NaN; Sync(); return; }
        if (Finished) { Sync(); return; }

        Grade = Course.GradeAt(DistanceM, gradeWindowM);

        // ---- the start-line freeze ---------------------------------------------------------
        // MapleRide's controller is the bicycle, so "input locked" has to mean NO WATTS REACH
        // THE MODEL - from the keyboard, the simulator, or a real trainer that is still happily
        // streaming 300 W into the app during the 3-2-1. Zeroing here, at the single point where
        // effort becomes motion, covers every source at once and cannot be bypassed by a device
        // adapter that does not know the gate exists. The ride clock does not run either: the
        // countdown is not part of the rider's time.
        if (RideInputGate.Locked)
        {
            if (devices != null) devices.ResetEffort();
            SpeedMps = 0f;
            _brake = 0f;
            SpeedCapMps = float.NaN;
            Sync();
            return;
        }

        if (devices != null) devices.Tick(dt, Grade);
        float watts = devices != null ? devices.Telemetry.Watts : 158f;

        if (Application.isPlaying)
            _brake = (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.DownArrow)) ? 1f : 0f;

        // TEMPORARY: while the map-scout flythrough is active, lift the descent speed scrub so the
        // 1000 W hold actually reaches high speed. Restored immediately after the step so nothing
        // else and no serialized asset is affected.
        bool scouting = devices != null && devices.ScoutModeActive;
        float descentCapBackup = physics.descentCapKph;
        if (scouting) physics.descentCapKph = 200f;
        SpeedMps = physics.Step(SpeedMps, watts, Grade, _brake, dt);
        if (scouting) physics.descentCapKph = descentCapBackup;

        // ---- external speed cap (traffic blocking) -----------------------------------------
        // ShiosaiTrafficDirector sets this when the player is tucked in directly behind an
        // ambient rider in the same lane: instead of teleporting the player back (which reads
        // as a stutter) or running a rigid body (which reads as a pinball), the rider simply
        // cannot pedal PAST the wheel in front. The cap is released the moment the player
        // steers out of that rider's lane, or the rider is gone.
        if (!float.IsNaN(SpeedCapMps))
        {
            float cap = Mathf.Max(0f, SpeedCapMps);
            if (SpeedMps > cap)
            {
                // Ease onto the cap rather than snapping, so a hard close reads as running out
                // of road rather than hitting a wall. 0.10 s time constant: a longer one let a
                // fast approach carry ~0.9 m INSIDE the minimum following gap before the player
                // shed the speed, which is exactly the clipping this exists to prevent.
                SpeedMps = Mathf.Max(cap, Mathf.Lerp(SpeedMps, cap,
                                                     1f - Mathf.Exp(-dt / 0.10f)));
            }
            SpeedCapMps = float.NaN;   // one frame only; the director re-asserts it each frame
        }

        float step = SpeedMps * dt;
        if (step > 0f)
        {
            float before = Course.ElevationAt(DistanceM);
            float advanced = DistanceM + step;
            TotalDistanceM += step;

            if (Course.Closed)
            {
                while (advanced >= Course.Length)
                {
                    advanced -= Course.Length;
                    LapIndex++;
                    LapCompleted?.Invoke(LapIndex);
                }
            }
            else if (advanced > Course.Length)
            {
                advanced = Course.Length;
            }
            DistanceM = advanced;

            float after = Course.ElevationAt(DistanceM);
            // Lap wrap makes the raw delta meaningless for one frame; ignore anything absurd.
            float rise = after - before;
            if (Mathf.Abs(rise) < step * 0.8f + 0.5f) AscentM += Mathf.Max(0f, rise);
        }

        ElapsedSeconds += dt;

        if (TotalDistanceM >= PlannedDistanceM - 0.01f ||
            (!Course.Closed && DistanceM >= Course.Length - 0.01f && TotalLaps == 1))
        {
            Finished = true;
            SessionFinished?.Invoke();
        }

        Sync();
    }

    private void Sync()
    {
        if (Course == null || Course.Count == 0) return;
        WorldPosition = Course.PositionAt(DistanceM);
        HeadingDeg = Course.HeadingAt(DistanceM);
    }
}

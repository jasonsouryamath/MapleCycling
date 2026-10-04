using UnityEngine;

/// <summary>
/// Places the rider on the course from <see cref="RideSession"/>.
///
/// The rider transform is *derived* from the ride's arc position, never integrated from input:
/// position is <c>course.PositionAt(d) + side * laneOffset</c>, heading is the course tangent,
/// and the lean is the course's authored banking. That is what replaces the free-roam
/// <c>KuroKeyboardController</c> while on-route, and it also makes <c>KuroRoadSafety</c>'s
/// corridor clamp redundant - you cannot leave a road you are mathematically on.
///
/// Free roam is still one key away (see <c>RideBootstrap.freeRoamKey</c>): this component
/// disables itself and hands the transform back to the original controller.
/// </summary>
[DefaultExecutionOrder(50)]
public class RouteFollower : MonoBehaviour
{
    public RideSession session;
    public Transform rider;

    [Header("Placement (provisional)")]
    [Tooltip("Metres right of the centreline - riders keep left in Japan, so this is negative.")]
    public float laneOffset = -1.7f;
    [Tooltip("Lift above the carriageway surface; the bike is authored with wheels at y=0.")]
    public float heightOffset = 0.02f;
    [Tooltip("How much of the road's authored banking the rider leans with.")]
    [Range(0f, 1f)] public float bankBlend = 0.85f;
    [Tooltip("Extra lean into corners at speed, degrees per (rad/s * m/s).")]
    public float leanGain = 2.4f;
    public float leanSmoothing = 6f;

    private float _lean;
    private float _lastHeading;

    // ---- lane changing (Shiosai overtaking) ------------------------------------------------

    [Header("Lane change (Shiosai overtaking - off by default)")]
    [Tooltip("Enables the lateral steer input. Left OFF for Sakura, whose riders are a fixed " +
             "oncoming roster with no same-lane traffic to pass.")]
    public bool laneChangeEnabled = false;

    [Tooltip("Metres the rider can move TOWARDS the centreline to pass. Japan keeps left, so " +
             "overtaking happens on the rider's right - which is +ve on laneOffset's axis.")]
    public float passShiftM = 1.15f;

    [Tooltip("How far the rider may drift the other way (towards the verge), in metres.")]
    public float yieldShiftM = 0.5f;

    [Tooltip("Lateral speed, m/s. Slow enough to read as a deliberate move across the road, " +
             "fast enough that a pass does not take a whole straight.")]
    public float laneChangeSpeedMps = 1.9f;
    [Tooltip("Legacy alternate binding. Keep false in production: Left/Right arrows orbit the " +
             "camera, while A/D own lane changes and never rotate the camera.")]
    public bool useArrowKeysForLaneChange = false;

    /// <summary>Lateral steer, -1 (towards the verge) .. +1 (towards the centreline).</summary>
    public float SteerInput { get; private set; }

    /// <summary>
    /// Scripted lateral steer for the capture harness, which cannot press keys. NaN = read the
    /// keyboard as normal.
    /// </summary>
    [System.NonSerialized] public float SteerOverride = float.NaN;

    /// <summary>
    /// The lane offset actually in use this frame, including any lane change in progress.
    /// ShiosaiTrafficDirector reads this to decide whether the player is tucked in behind a
    /// rider (drafting / blocked) or has pulled out alongside them (overtaking).
    /// </summary>
    public float ActiveLaneOffset => _lane;

    private float _lane = float.NaN;

    /// <summary>
    /// Race line set by <see cref="RaceDirector"/> (metres right of the centreline): the rider
    /// glides to it at <see cref="laneChangeSpeedMps"/> and stays there, in every region, instead
    /// of springing back to <see cref="laneOffset"/>. NaN = normal riding.
    /// </summary>
    [System.NonSerialized] public float LaneOverride = float.NaN;

    /// <summary>Jumps straight to the override line (a race set up behind a fade).</summary>
    public void SnapLane()
    {
        if (!float.IsNaN(LaneOverride)) _lane = LaneOverride;
    }

    private void UpdateLane(float dt)
    {
        if (float.IsNaN(_lane)) _lane = laneOffset;
        if (!float.IsNaN(LaneOverride))
        {
            SteerInput = 0f;
            _lane = Mathf.MoveTowards(_lane, LaneOverride, laneChangeSpeedMps * dt);
            return;
        }
        if (!laneChangeEnabled) { _lane = laneOffset; SteerInput = 0f; return; }

        float h = 0f;
        if (float.IsNaN(SteerOverride))
        {
            // No steering during the start-line freeze. The harness override is deliberately
            // still honoured: an editor capture is not the player.
            if (!RideInputGate.Locked)
            {
                if (Input.GetKey(KeyCode.D) ||
                    (useArrowKeysForLaneChange && Input.GetKey(KeyCode.RightArrow))) h += 1f;
                if (Input.GetKey(KeyCode.A) ||
                    (useArrowKeysForLaneChange && Input.GetKey(KeyCode.LeftArrow))) h -= 1f;
            }
        }
        else h = Mathf.Clamp(SteerOverride, -1f, 1f);
        SteerInput = h;

        float target = laneOffset + (h > 0f ? h * passShiftM : h * yieldShiftM);
        _lane = Mathf.MoveTowards(_lane, target, laneChangeSpeedMps * dt);
    }

    private void Reset()
    {
        rider = transform;
    }

    private void LateUpdate()
    {
        Apply();
    }

    /// <summary>Callable from an editor harness so a staged rider can be posed without play mode.</summary>
    public void Apply()
    {
        if (session == null || rider == null) return;
        var course = session.Course;
        if (course == null || course.Count < 2) return;

        float d = session.DistanceM;
        Vector3 p = course.PositionAt(d);
        Vector3 t = course.TangentAt(d);
        Vector3 s = course.SideAt(d);
        Vector3 u = course.UpAt(d);

        float dtLane = Application.isPlaying ? Time.deltaTime : 0f;
        UpdateLane(dtLane);

        rider.position = p + s * _lane + u * heightOffset;

        float heading = course.HeadingAt(d);
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        float yawRate = Mathf.DeltaAngle(_lastHeading, heading) / dt;
        _lastHeading = heading;

        float targetLean = course.BankAt(d) * bankBlend
                           - Mathf.Clamp(yawRate * Mathf.Deg2Rad * session.SpeedMps * leanGain,
                                         -14f, 14f)
                           + CrosswindLeanDeg();
        _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-leanSmoothing * dt));

        var flat = new Vector3(t.x, t.y, t.z);
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
        rider.rotation = Quaternion.LookRotation(flat.normalized, u) *
                         Quaternion.Euler(0f, 0f, -_lean);
    }

    /// <summary>
    /// Lean INTO a crosswind (+ = right), sized physically: atan(side force / weight), with
    /// side force = 0.5 rho CdA_side v_cross^2 (CdA_side ~0.6 m^2 for rider + bike side-on).
    /// ~2-3 deg on the bridge in a strong crosswind; a visual/handling cue only - the route line
    /// is never pushed sideways, so racing stays fair.
    /// </summary>
    private float CrosswindLeanDeg()
    {
        var wd = WeatherDirector.Instance;
        if (wd == null || session == null) return 0f;
        float vc = wd.Apparent.CrosswindMps;
        float side = 0.5f * session.physics.airDensityKgM3 * 0.6f * vc * Mathf.Abs(vc);
        float deg = Mathf.Atan2(side, session.physics.TotalMassKg * 9.81f) * Mathf.Rad2Deg;
        return Mathf.Clamp(deg, -4f, 4f);
    }
}

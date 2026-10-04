using UnityEngine;

/// <summary>
/// Places Hanakage on the SAME course arc the player is riding, a configurable number of metres
/// ahead of them.
///
/// Why she is not an <see cref="NPCCyclist"/> like the rest of the Sakura Pass roster:
///
/// The roster rides a resampled copy of the road centreline, in the OPPOSITE direction, at a
/// fixed speed, with no knowledge of the ride session. That is exactly right for scenery riders
/// - oncoming traffic is what makes <see cref="NpcGreeting"/>'s view cone fire at all - but it
/// is wrong for an encounter rider in three ways at once:
///
///   * the encounter is defined entirely in terms of the GAP in metres between the player's arc
///     position and hers, and the roster route is a different polyline from
///     <see cref="RouteCourse"/> with its own arc parameterisation, so a gap measured on one is
///     not a gap on the other;
///   * she travels the same way as the player, so "progress" has to run forward, not reversed;
///   * her speed is driven by the encounter phase (and therefore, indirectly, by the player's
///     real watts), not by a constant.
///
/// So she is derived from the course exactly the way the player is in
/// <see cref="RouteFollower"/> - same <c>PositionAt</c>/<c>TangentAt</c>/<c>SideAt</c>/
/// <c>BankAt</c> sampling, same lane convention - which is what guarantees that a 40 m gap on
/// the HUD is 40 m of road and not 40 m of straight-line distance across a hairpin.
///
/// This component owns placement ONLY. Every behavioural decision (when she exists, how fast she
/// rides, when she looks back) belongs to the encounter state machine; keeping them apart means
/// the state machine can be driven deterministically by a headless harness while this stays a
/// pure function of arc distance.
/// </summary>
[DefaultExecutionOrder(60)]
[ExecuteAlways]
public class HanakageRider : MonoBehaviour
{
    /// <summary>Exact scene object name. Staging and lookups match on this, never on Contains.</summary>
    public const string ObjectName = "Sakura NPC Hanakage";

    [Header("Wiring")]
    public RideSession session;
    [Tooltip("The transform that is actually moved. Defaults to this object.")]
    public Transform rider;
    [Tooltip("Renderer root toggled when she is not present in the world. Defaults to this object.")]
    public GameObject visualRoot;

    [Header("Placement")]
    [Tooltip("Arc metres along the CURRENT course. Written by the encounter state machine.")]
    public float arcDistanceM;
    [Tooltip("PROVISIONAL: metres right of the centreline. Riders keep left in Japan, so this " +
             "is negative. She sits slightly further left than the player (-1.7) so the player " +
             "can sit on her wheel without the two sculpts intersecting.")]
    public float laneOffset = -2.6f;
    [Tooltip("Lift above the carriageway. The bike is authored with its wheels at y = 0; the " +
             "asphalt carries a ~6 cm parabolic crown, so this clears it.")]
    public float heightOffset = 0.065f;
    [Range(0f, 1f)] public float bankBlend = 0.85f;
    [Tooltip("Extra lean into corners, degrees per (rad/s * m/s). Lower than the player's: her " +
             "defining trait is that she barely moves on the bike.")]
    public float leanGain = 1.5f;
    public float leanSmoothing = 6f;

    [Header("State (driven by the encounter)")]
    [Tooltip("Her current road speed, metres per second. Read by the HUD and the gap model.")]
    public float speedMps;

    private float _lean;
    private float _lastHeading;

    /// <summary>
    /// True while she is a visible part of the world. Staging always gives her a dedicated
    /// visual root child, so this is a plain active check; the renderer fallback below only
    /// exists for a hand-wired instance that never got one.
    /// </summary>
    public bool Present
    {
        get
        {
            if (visualRoot != null && visualRoot != gameObject) return visualRoot.activeSelf;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) return r.enabled;
            return false;
        }
    }

    private void Reset()
    {
        rider = transform;
        visualRoot = gameObject;
    }

    private void LateUpdate()
    {
        Apply();
    }

    /// <summary>
    /// Callable from an editor harness, so a staged Hanakage is posed on the road in the SAVED
    /// scene rather than parked at the world origin - which in this project is out over the lake.
    /// </summary>
    public void Apply()
    {
        if (rider == null) rider = transform;
        if (session == null) return;
        // While she is not present she must NOT be dragged along whatever course the session
        // happens to hold. The scene's session can be sitting on the Shiosai Coast course, and
        // the first staging run duly parked her 3 km away on the coast road because this
        // early-out was missing. Absent means absent.
        if (!Present) return;
        var course = session.Course;
        if (course == null || course.Count < 2) return;

        float d = course.Wrap(arcDistanceM);
        Vector3 p = course.PositionAt(d);
        Vector3 t = course.TangentAt(d);
        Vector3 s = course.SideAt(d);
        Vector3 u = course.UpAt(d);

        rider.position = p + s * laneOffset + u * heightOffset;

        float heading = course.HeadingAt(d);
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        float yawRate = Mathf.DeltaAngle(_lastHeading, heading) / dt;
        _lastHeading = heading;

        float targetLean = course.BankAt(d) * bankBlend
                           - Mathf.Clamp(yawRate * Mathf.Deg2Rad * speedMps * leanGain, -10f, 10f);
        _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-leanSmoothing * dt));

        var flat = t.sqrMagnitude < 1e-6f ? Vector3.forward : t.normalized;
        rider.rotation = Quaternion.LookRotation(flat, u) * Quaternion.Euler(0f, 0f, -_lean);
    }

    /// <summary>
    /// Shows or hides her. Toggling the visual root rather than this GameObject keeps the
    /// component (and therefore the encounter's reference to it) alive while she is off-road.
    /// </summary>
    public void SetPresent(bool present)
    {
        if (visualRoot == null) visualRoot = gameObject;
        if (visualRoot == gameObject)
        {
            // Would disable this component too. Fall back to renderer toggling.
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = present;
            return;
        }
        if (visualRoot.activeSelf != present) visualRoot.SetActive(present);
    }
}

using UnityEngine;

/// <summary>
/// Keeps Maple City's signature leaf-drift emitter parked just above and ahead of whichever
/// camera is currently rendering the region.
///
/// WHY THIS EXISTS AT ALL
/// ----------------------
/// Sakura Pass's petal system is a FIXED emitter box sitting at the world origin, sized roughly
/// 110 x 26 x 420 m. That works there because the pass is a single corridor of road about that
/// long, so one static box genuinely covers everywhere the player can be.
///
/// Maple City is a 5.0 km CLOSED CIRCUIT spanning roughly 1400 x 1700 m of world. Covering that
/// with a static box would need something on the order of forty times the volume - and because a
/// particle system's density is (rate x lifetime) spread over its shape, keeping the same visual
/// density would need tens of thousands of live particles, nearly all of them off-screen, on a
/// transparent unlit material. That is exactly the kind of silent overdraw cost that does not
/// show up in a screenshot and does show up in the frame time.
///
/// So instead the emitter is small (a ~46 m box, a few hundred particles) and it FOLLOWS the
/// camera. The player always rides through freshly falling leaves, the count stays restrained,
/// and nothing is ever simulated somewhere nobody is looking.
///
/// The particles themselves are simulated in WORLD space, so moving the emitter does not drag
/// already-spawned leaves along with it - they keep falling where they were born and the motion
/// reads as real wind rather than as a shell strapped to the camera.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
[DisallowMultipleComponent]
public class MapleLeafDrift : MonoBehaviour
{
    [Header("Follow (all provisional tuning)")]
    [Tooltip("Metres above the camera to park the emitter box. Leaves need enough headroom to " +
             "fall visibly through frame before their lifetime expires.")]
    public float height = 13f;

    [Tooltip("Metres AHEAD of the camera (along its flat forward) to bias the box, so leaves " +
             "have time to drift down into view rather than popping in beside the rider.")]
    public float ahead = 16f;

    [Tooltip("Metres the camera must move before the emitter re-anchors. A small dead zone stops " +
             "the box from jittering with every sub-centimetre camera correction.")]
    public float deadZoneM = 2.5f;

    [Tooltip("Seconds to ease the emitter to a new anchor. Snapping would make a whole block of " +
             "leaves appear at once on a fast corner.")]
    public float followLerp = 0.35f;

    private Transform _anchor;
    private Vector3 _target;
    private bool _primed;

    private void OnEnable()
    {
        _primed = false;
        Track(true);
    }

    private void LateUpdate()
    {
        Track(false);
    }

    /// <summary>
    /// Resolves the camera once per frame and moves the emitter toward it.
    /// </summary>
    /// <param name="snap">
    /// True on enable (including the frame fast travel switches the region on), so the emitter
    /// is already correct on the very first rendered frame instead of easing in from wherever
    /// the previous region left it.
    /// </param>
    private void Track(bool snap)
    {
        // Camera.main is null-ish in some editor contexts and can change when the ride camera
        // hands over, so re-resolve rather than caching across the object's whole lifetime.
        var cam = Camera.main;
        if (cam == null)
        {
            var any = Camera.current;
            if (any == null) return;
            cam = any;
        }
        _anchor = cam.transform;

        var flat = _anchor.forward;
        flat.y = 0f;
        flat = flat.sqrMagnitude < 1e-4f ? Vector3.forward : flat.normalized;

        var want = _anchor.position + flat * ahead + Vector3.up * height;

        if (snap || !_primed)
        {
            _target = want;
            transform.position = want;
            _primed = true;
            return;
        }

        // Dead zone: only re-anchor once the camera has actually gone somewhere.
        if ((want - _target).sqrMagnitude > deadZoneM * deadZoneM) _target = want;

        transform.position = Vector3.Lerp(transform.position, _target,
                                          1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, followLerp)));
    }
}

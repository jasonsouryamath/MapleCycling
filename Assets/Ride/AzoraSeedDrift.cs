using UnityEngine;

/// <summary>
/// Keeps Azora Highlands' signature grass-seed drift parked around whichever camera is currently
/// rendering the region.
///
/// WHY A FOLLOWING EMITTER, AGAIN
/// ------------------------------
/// Same reason <see cref="MapleLeafDrift"/> documents, only more so. Azora is 24 km of road
/// folded into a 4.5 x 7.7 km box - roughly TWENTY TIMES Maple City's footprint. A static
/// emitter large enough to cover it would need tens of thousands of live particles on a
/// transparent unlit material to hold the same on-screen density, nearly all of them behind the
/// rider or on a hillside nobody is looking at. That cost never shows up in a screenshot and
/// always shows up in the frame time.
///
/// HOW AZORA'S DRIFT DIFFERS FROM THE CITY'S, AND WHY THAT MATTERS
/// ---------------------------------------------------------------
/// Maple City's leaves FALL: they are heavy, they tumble, they have somewhere to land. Azora's
/// seed does not fall - dandelion clock, thistledown and straw seed heads are almost weightless,
/// so on an exposed fell they travel nearly HORIZONTALLY on the prevailing wind and get lifted
/// by thermals as often as they sink. That is why this component carries a <see cref="lift"/>
/// and biases the emitter UPWIND rather than simply ahead: the seed has to blow INTO frame
/// across the rider, not rain down onto them.
///
/// Keeping that distinction sharp is a hard requirement of the project, not a nicety: the three
/// regions built before this one all have an ambient drift, and the moment Azora's reads as
/// "petals, but yellow" the region stops being its own place. The colour half of that guarantee
/// lives in tools/blender/azora_surfaces.py (which asserts every seed variant has green >= blue,
/// because anything bluer than it is green blooms pink under the grade); the MOTION half lives
/// here.
///
/// The particles themselves simulate in WORLD space, so moving the emitter does not drag
/// already-spawned seed along with it - each one keeps riding the wind it was born into.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
[DisallowMultipleComponent]
public class AzoraSeedDrift : MonoBehaviour
{
    [Header("Follow (all provisional tuning)")]
    [Tooltip("Metres above the camera to park the emitter box. Lower than the city's leaf box: " +
             "seed blows past you at head height, it does not fall from a canopy.")]
    public float height = 7f;

    [Tooltip("Metres UPWIND of the camera to bias the box, so seed has room to blow across " +
             "frame instead of appearing beside the rider already halfway through its life.")]
    public float upwind = 22f;

    [Tooltip("Prevailing wind, world XZ. Must match AzoraHighlandsEnvironment.WindDir, which is " +
             "also what the marker posts lean away from and the hut's smoke streams along.")]
    public Vector3 windDir = new Vector3(0.82f, 0f, -0.57f);

    [Tooltip("Metres the camera must move before the emitter re-anchors. A small dead zone stops " +
             "the box jittering with every sub-centimetre camera correction.")]
    public float deadZoneM = 3f;

    [Tooltip("Seconds to ease the emitter to a new anchor. Snapping would make a whole cloud of " +
             "seed appear at once on a fast hairpin.")]
    public float followLerp = 0.4f;

    [Tooltip("Extra metres of headroom given to the box on the descent, where the camera is " +
             "moving fast enough to outrun a tightly-parked emitter.")]
    public float lift = 2.5f;

    private Vector3 _target;
    private bool _primed;

    private void OnEnable()
    {
        _primed = false;
        Track(true);
    }

    private void LateUpdate() => Track(false);

    /// <param name="snap">
    /// True on enable - including the frame fast travel switches this region on - so the emitter
    /// is already correct on the very first rendered frame instead of easing in from wherever the
    /// previous region left it. Without this, arriving at the Meadow Gate showed a visible slick
    /// of seed sliding in from Maple City's side of the world.
    /// </param>
    private void Track(bool snap)
    {
        // Camera.main is null-ish in some editor contexts and changes when the ride camera hands
        // over, so re-resolve every frame rather than caching across the object's lifetime.
        var cam = Camera.main;
        if (cam == null)
        {
            cam = Camera.current;
            if (cam == null) return;
        }

        var w = windDir;
        w.y = 0f;
        w = w.sqrMagnitude < 1e-4f ? Vector3.forward : w.normalized;

        // UPWIND, not "ahead". The seed's own velocity carries it back across the camera.
        var want = cam.transform.position - w * upwind + Vector3.up * (height + lift);

        if (snap || !_primed)
        {
            _target = want;
            transform.position = want;
            _primed = true;
            return;
        }

        if ((want - _target).sqrMagnitude > deadZoneM * deadZoneM) _target = want;

        transform.position = Vector3.Lerp(transform.position, _target,
                                          1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, followLerp)));
    }
}

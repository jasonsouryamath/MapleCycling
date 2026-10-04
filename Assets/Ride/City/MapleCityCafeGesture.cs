using UnityEngine;

/// <summary>
/// MAPLE CITY LIFE (claude-city, 2026-09-25). Small additive upper-body acting for the cafe
/// and street-corner figures, layered on top of <see cref="MinatoCrowdActor"/>'s idle/sit pose:
///
///  * Sip    - every few seconds the right arm lifts a coffee cup to the face and back. For a
///             seated figure the cup travels from its saucer on the table to the hand.
///  * Talk   - small head nods and a periodic open-hand gesture, as if mid-sentence.
///  * Listen - slower nods and a slight head tilt toward the speaker.
///
/// WHY LATEUPDATE. The actor rebuilds the skeleton from its neutral pose in Update (order 60)
/// every frame it animates, so rotations added here never accumulate. When the actor stops
/// animating (camera beyond its animationDistance) this component stops too, using the SAME
/// test, so a raised arm is never compounded frame over frame.
///
/// Bone rotations are applied about the FIGURE's own axes (right / forward / up), exactly the
/// way MinatoCrowdActor.WalkPose does, so the result does not depend on each donor rig's bone
/// roll. The degree values are PROVISIONAL and tuned from play-mode captures.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapleCityCafeGesture : MonoBehaviour
{
    public enum Mode { Sip, Talk, Listen }

    public Mode mode = Mode.Sip;
    public Transform rigRoot;
    /// <summary>Cup that is carried (optional). Its pose is driven here in world space.</summary>
    public Transform cup;
    /// <summary>When set, the cup rests here between sips (a seated figure's saucer).</summary>
    public Vector3 cupRestWorld;
    public bool cupRests;
    [Min(2f)] public float period = 8f;
    [Range(0f, 1f)] public float phase;

    [Header("Sip pose (degrees, about the figure's own axes)")]
    public float upperArmRaise = 62f;
    public float upperArmInward = 18f;
    public float forearmBend = 70f;
    public float headDip = 8f;
    /// <summary>Hand-to-cup offset in the figure's frame (right, up, forward), metres.</summary>
    public Vector3 cupInHand = new Vector3(-0.01f, 0.02f, 0.035f);
    public float animationDistance = 90f;
    /// <summary>
    /// True for the seated Minato donors, whose rig faces the transform's -Z (see
    /// MapleCityLife.SitterFacesMinusZ). The gesture axes are then taken from the flipped frame.
    /// </summary>
    public bool facesMinusZ;

    Transform _rArm, _rFore, _rHand, _head, _spine, _lArm, _lFore;
    bool _ready;

    void Awake() => Resolve();

    void Resolve()
    {
        if (_ready || rigRoot == null) return;
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "RightArm": _rArm = t; break;
                case "RightForeArm": _rFore = t; break;
                case "RightHand": _rHand = t; break;
                case "LeftArm": _lArm = t; break;
                case "LeftForeArm": _lFore = t; break;
                case "Head": _head = t; break;
                case "Spine02": _spine = t; break;
            }
        }
        _ready = _rArm != null && _rFore != null && _head != null;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);

    /// <summary>0..1 sip envelope: raise 0.9 s, hold 1.6 s, lower 0.9 s, once per period.</summary>
    float SipWeight(float t)
    {
        float c = Mathf.Repeat(t / period + phase, 1f) * period;
        if (c < 0.9f) return Smooth(c / 0.9f);
        if (c < 2.5f) return 1f;
        if (c < 3.4f) return Smooth(1f - (c - 2.5f) / 0.9f);
        return 0f;
    }

    static void RotateWorld(Transform bone, Vector3 axis, float degrees)
    {
        if (bone == null || Mathf.Abs(degrees) < 0.01f) return;
        bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        Resolve();
        if (!_ready) return;
        var cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude >
            animationDistance * animationDistance)
            return;

        float t = Time.time;
        Vector3 right = transform.right, fwd = transform.forward, up = transform.up;
        if (facesMinusZ) { right = -right; fwd = -fwd; }

        switch (mode)
        {
            case Mode.Sip:
            {
                float w = SipWeight(t);
                // Negative about +right swings a hanging arm FORWARD (see MinatoCrowdActor.WalkPose).
                RotateWorld(_rArm, right, -upperArmRaise * w);
                RotateWorld(_rArm, fwd, -upperArmInward * w);
                RotateWorld(_rFore, right, -forearmBend * w);
                RotateWorld(_head, right, headDip * w);
                break;
            }
            case Mode.Talk:
            {
                float g = Mathf.Max(0f, Mathf.Sin(t * 1.3f + phase * 6.28f));
                RotateWorld(_head, right, Mathf.Sin(t * 3.1f + phase * 6.28f) * 4f);
                RotateWorld(_head, up, Mathf.Sin(t * 0.9f + phase * 3.1f) * 6f);
                RotateWorld(_rArm, right, -28f * g);
                RotateWorld(_rFore, right, -48f * g);
                RotateWorld(_rFore, up, Mathf.Sin(t * 4.0f) * 10f * g);
                RotateWorld(_spine, up, Mathf.Sin(t * 0.7f + phase) * 3f);
                break;
            }
            case Mode.Listen:
            {
                float nod = Mathf.Max(0f, Mathf.Sin(t * 1.7f + phase * 6.28f));
                RotateWorld(_head, right, nod * 5f);
                RotateWorld(_head, fwd, 5f);
                if (_lArm != null && _lFore != null)
                {
                    // arms loosely folded / hand at the chin
                    RotateWorld(_lArm, right, -22f);
                    RotateWorld(_lFore, right, -55f);
                }
                break;
            }
        }

        if (cup != null && _rHand != null)
        {
            Vector3 inHand = _rHand.position + right * cupInHand.x + up * cupInHand.y + fwd * cupInHand.z;
            Vector3 p = inHand;
            if (cupRests && mode == Mode.Sip)
            {
                float w = SipWeight(t);
                // leave the saucer only once the hand is on its way up
                p = Vector3.Lerp(cupRestWorld, inHand, Smooth(Mathf.Clamp01(w * 1.6f)));
            }
            cup.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up));
        }
    }
}

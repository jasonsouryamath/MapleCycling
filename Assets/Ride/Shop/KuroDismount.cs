using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Kuro gets off (and back on) his bike at a Maple Row store. The whole motion is a pure function
/// of one value, <see cref="T"/> (0 = riding, 1 = standing on the kerb side of the bike), written
/// into KuroBikeRig's additive dismount overlay (foot goals, heel-out, hip offset, upright
/// blend, hand sockets). Remounting is the same curve played backwards, and the shop mannequin can
/// hold T = 1 without a timeline.
///
/// <see cref="side"/> is the kerb side the store is on (+1 = the bike's right, -1 = its left).
/// The foot on that side clips out and goes down first, and the other leg swings back over the
/// saddle, so Kuro always steps off TOWARD the store (and toward the camera that frames it).
///
///   0.00-0.22  clip out: the kerb-side heel swings out of the cleat
///   0.12-0.40  that foot down to the road beside the bike
///   0.30-0.60  out of the saddle, torso comes upright
///   0.52-0.90  the other leg swings back over the saddle and lands on the kerb side
///   0.60-1.00  hips settle to standing height beside the bike, the kerb foot steps out; the
///              bike-side hand keeps the bars, the outer arm hangs at his side
/// PROVISIONAL timings and distances (chibi rig, bike scale 0.9).
/// </summary>
[DisallowMultipleComponent]
public sealed class KuroDismount : MonoBehaviour
{
    public KuroBikeRig rig;
    public float dismountSeconds = 2.6f;
    public float remountSeconds = 1.9f;
    [Tooltip("Kerb side to step off to: +1 = the bike's right, -1 = its left.")]
    public int side = 1;
    [Tooltip("How far beside the bike Kuro stands (hips, metres).")]
    public float standLateral = 0.42f;

    [Range(0f, 1f)] public float T;
    public bool Busy { get; private set; }

    private Transform _handL, _handR;
    private Vector3 _pedalL0, _pedalR0;
    private bool _captured;
    private float _cadenceBackup;

    private void Awake()
    {
        if (rig == null) rig = GetComponentInChildren<KuroBikeRig>(true);
    }

    public IEnumerator Dismount(Action done = null)
    {
        Busy = true;
        Capture();
        yield return Run(T, 1f, dismountSeconds);
        Busy = false;
        done?.Invoke();
    }

    public IEnumerator Remount(Action done = null)
    {
        Busy = true;
        yield return Run(T, 0f, remountSeconds);
        Release();
        Busy = false;
        done?.Invoke();
    }

    private IEnumerator Run(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            T = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        T = to;
    }

    /// <summary>Snaps straight to standing (the shop mannequin).</summary>
    public void HoldStanding()
    {
        Capture();
        T = 1f;
        ApplyPose();
    }

    /// <summary>Straight back in the saddle with no animation (a racer mounting behind a fade).</summary>
    public void SnapRiding()
    {
        StopAllCoroutines();
        Busy = false;
        T = 0f;
        Release();
    }

    private void Capture()
    {
        if (rig == null) return;
        if (!_captured)
        {
            _cadenceBackup = rig.previewCadenceRpm;
            _pedalL0 = rig.FootTargetL;
            _pedalR0 = rig.FootTargetR;
            _captured = true;
        }
        rig.previewCadenceRpm = 0f;   // no ghost pedalling while stopped
        if (_handL == null)
        {
            _handL = new GameObject("~DismountHandL").transform;
            _handL.SetParent(rig.transform, false);
            _handR = new GameObject("~DismountHandR").transform;
            _handR.SetParent(rig.transform, false);
        }
    }

    private void Release()
    {
        if (rig == null) return;
        rig.poseHipWorldOffset = Vector3.zero;
        rig.poseFootGoalWeightL = rig.poseFootGoalWeightR = 0f;
        rig.poseHeelOutDegreesL = rig.poseHeelOutDegreesR = 0f;
        rig.poseUprightBlend = 0f;
        if (rig.poseHandTargetOverrideL == _handL) rig.poseHandTargetOverrideL = null;
        if (rig.poseHandTargetOverrideR == _handR) rig.poseHandTargetOverrideR = null;
        if (_captured) rig.previewCadenceRpm = _cadenceBackup;
        _captured = false;
    }

    private void Update()
    {
        if (rig == null || (!_captured && T <= 0f)) return;
        ApplyPose();
    }

    private static float Seg(float t, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    private void ApplyPose()
    {
        var bike = rig.transform;
        int s = side >= 0 ? 1 : -1;
        Vector3 fwd = bike.forward, up = bike.up, kerb = bike.right * s;
        float ground = GroundY();
        float t = T;

        float clip = Seg(t, 0.00f, 0.22f);
        float down = Seg(t, 0.12f, 0.40f);
        float rise = Seg(t, 0.30f, 0.60f);
        float swing = Seg(t, 0.52f, 0.90f);
        float settle = Seg(t, 0.60f, 1.00f);

        // kerb-side foot straight down beside the crank; the far foot over the saddle to the kerb
        Vector3 root = new Vector3(bike.position.x, ground, bike.position.z);
        // Final stance: the kerb-side foot is the OUTER foot and the leg that swings over lands on
        // the INSIDE, next to the bike. (First pass had them the other way round: crossed legs.)
        Vector3 nearDown = root + kerb * 0.24f + fwd * 0.02f + up * rig.ankleHeight;           // first touch
        Vector3 nearFinal = root + kerb * (standLateral + 0.09f) + fwd * 0.03f + up * rig.ankleHeight;
        Vector3 farGround = root + kerb * (standLateral - 0.09f) - fwd * 0.01f + up * rig.ankleHeight;
        Vector3 nearPedal = s > 0 ? _pedalR0 : _pedalL0, farPedal = s > 0 ? _pedalL0 : _pedalR0;

        float nearHeel = 28f * clip * (1f - settle);
        float farHeel = 22f * Seg(t, 0.45f, 0.58f) * (1f - settle);
        Vector3 nearGoal = Vector3.Lerp(Vector3.Lerp(nearPedal + kerb * 0.10f, nearDown, down), nearFinal, settle);
        float nearWeight = Mathf.Max(clip * 0.35f, down);

        Vector3 saddle = rig.SaddleTop != null ? rig.SaddleTop.position : bike.position + up * 0.5f;
        Vector3 apex = saddle + up * 0.16f - fwd * 0.22f + kerb * 0.05f;
        Vector3 a = Vector3.Lerp(farPedal, apex, swing), b = Vector3.Lerp(apex, farGround, swing);
        Vector3 farGoal = Vector3.Lerp(a, b, swing);                 // quadratic arc over the rear wheel
        float farWeight = Seg(t, 0.50f, 0.58f);

        if (s > 0)
        {
            rig.poseHeelOutDegreesR = nearHeel; rig.poseFootGoalR = nearGoal; rig.poseFootGoalWeightR = nearWeight;
            rig.poseHeelOutDegreesL = farHeel;  rig.poseFootGoalL = farGoal;  rig.poseFootGoalWeightL = farWeight;
        }
        else
        {
            rig.poseHeelOutDegreesL = nearHeel; rig.poseFootGoalL = nearGoal; rig.poseFootGoalWeightL = nearWeight;
            rig.poseHeelOutDegreesR = farHeel;  rig.poseFootGoalR = farGoal;  rig.poseFootGoalWeightR = farWeight;
        }

        // hips: up out of the saddle, then across to the kerb side and down to standing height
        float standY = ground + rig.LegLength * 0.97f;
        Vector3 lift = up * (0.07f * rise * (1f - settle));
        Vector3 across = kerb * (standLateral * swing);
        Vector3 drop = up * ((standY - saddle.y) * settle);
        rig.poseHipWorldOffset = lift + across + drop + fwd * (-0.05f * settle);
        rig.poseUprightBlend = rise;

        // hands: the bike-side hand keeps the bars (slides to the near hood); the outer arm comes
        // off the bars and hangs relaxed at Kuro's side (first pass reached back to the saddle,
        // which twisted the arm across the body)
        if (rig.HoodR != null && rig.HoodL != null)
        {
            var nearHood = s > 0 ? rig.HoodR : rig.HoodL;
            var farHand = s > 0 ? _handL : _handR;
            var nearHand = s > 0 ? _handR : _handL;
            var farHood = s > 0 ? rig.HoodL : rig.HoodR;
            Vector3 hip = rig.Hips != null ? rig.Hips.position : bike.position + up * rig.LegLength;
            Vector3 hang = hip + kerb * 0.14f + fwd * 0.03f - up * 0.04f;
            farHand.position = Vector3.Lerp(farHood.position, nearHood.position + kerb * 0.02f, settle);
            nearHand.position = Vector3.Lerp(nearHood.position, hang, Seg(t, 0.55f, 0.95f));
            rig.poseHandTargetOverrideL = t > 0.001f ? _handL : null;
            rig.poseHandTargetOverrideR = t > 0.001f ? _handR : null;
        }
    }

    private float GroundY()
    {
        var bike = rig.transform;
        var from = bike.position + Vector3.up * 1.5f;
        float best = float.MaxValue, y = bike.position.y;
        foreach (var h in Physics.RaycastAll(from, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
            if (!h.transform.IsChildOf(bike.root) && h.distance < best) { best = h.distance; y = h.point.y; }
        return y;
    }
}

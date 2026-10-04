using UnityEngine;

/// <summary>
/// Nagisa Bay sunbather (Claude, 2026-10-01). A Kuro-rig figure lying on a towel or lounger. The
/// figure sits under a "Sunbather Pose" parent that lays it on its back (head toward +Y of the
/// figure = the parent's chosen direction); this component then poses the rig from the figure's own
/// frame, in world-axis rotations so it does not depend on the bones' local axes:
///   * arms: hanging at the sides, one hand behind the head, or folded over the stomach;
///   * legs: straight, one knee up, or crossed at the ankle;
///   * life: breathing, a slow head turn, an occasional shift of an arm or a knee.
/// The MinatoCrowdActor on the figure is disabled by the builder (it would re-ground the feet).
/// Runs after the actor (order 60) and is skipped beyond <see cref="animationDistance"/>.
/// </summary>
[DefaultExecutionOrder(70)]
[DisallowMultipleComponent]
public sealed class NagisaSunbather : MonoBehaviour
{
    public Transform rigRoot;
    public Transform figure;            // the standing-rig root (child of the pose parent)
    public int pose;                    // 0 arms down, 1 hand behind head, 2 arms folded
    public int legPose;                 // 0 straight, 1 one knee up, 2 ankles crossed
    public float phase;
    public float animationDistance = 60f;

    static readonly string[] Names =
    {
        "Hips", "Spine02", "Head", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm",
        "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot"
    };

    Transform[] _b;
    Quaternion[] _bind;
    bool _ok;

    void Awake() => Resolve();

    void Resolve()
    {
        if (_ok || rigRoot == null) return;
        _b = new Transform[Names.Length];
        _bind = new Quaternion[Names.Length];
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            int i = System.Array.IndexOf(Names, t.name);
            if (i >= 0 && _b[i] == null) { _b[i] = t; _bind[i] = t.localRotation; }
        }
        _ok = _b[0] != null;
    }

    Transform B(string n) => _b[System.Array.IndexOf(Names, n)];

    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        Resolve();
        if (!_ok || figure == null) return;
        var cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > animationDistance * animationDistance) return;
        Pose(Time.time);
    }

    /// <summary>Also used by diagnostics to pose at a given time.</summary>
    public void Pose(float time)
    {
        Resolve();
        if (!_ok || figure == null) return;
        for (int i = 0; i < _b.Length; i++) if (_b[i] != null) _b[i].localRotation = _bind[i];

        Vector3 up = figure.up;           // toward the head
        Vector3 right = figure.right;
        Vector3 fwd = figure.forward;     // the figure's front = skyward while supine
        float t = time + phase * 37f;
        float breathe = Mathf.Sin(t * 1.25f);

        // breathing: the chest swells toward the sky
        Rot("Spine02", right, breathe * 1.6f);
        Rot("Head", up, Mathf.Sin(t * 0.21f + phase * 6f) * 14f);

        // arms
        Arm("Left", -1f, pose, t);
        Arm("Right", +1f, pose == 1 ? 0 : pose, t);

        // legs
        float shift = 0.5f + 0.5f * Mathf.Sin(t * 0.11f + phase * 5f);
        switch (legPose)
        {
            case 1:   // right knee up, foot flat
                Rot("RightUpLeg", right, -(52f + 6f * shift));
                Rot("RightLeg", right, 78f + 6f * shift);
                Rot("RightFoot", right, -26f);
                Rot("LeftUpLeg", Vector3.up, 0f);
                break;
            case 2:   // ankles crossed: the legs drift together
                Rot("LeftUpLeg", fwd, 7f);
                Rot("RightUpLeg", fwd, -9f);
                break;
            default:  // relaxed, toes falling outward
                Rot("LeftFoot", up, -12f);
                Rot("RightFoot", up, 12f);
                break;
        }
    }

    void Arm(string side, float sign, int p, float t)
    {
        var arm = B(side + "Arm");
        var fore = B(side + "ForeArm");
        if (arm == null || fore == null) return;
        Vector3 up = figure.up, right = figure.right, fwd = figure.forward;
        Vector3 sideDir = right * sign;           // outward from the body on this side
        Vector3 dir = fore.position - arm.position;
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();
        Vector3 goalArm, goalFore;
        switch (p)
        {
            case 1:   // hand behind the head: elbow out and up-ish, forearm folded back toward the skull
                goalArm = (sideDir * 0.85f + up * 0.45f - fwd * 0.10f).normalized;
                goalFore = (up * 0.78f - sideDir * 0.55f - fwd * 0.30f).normalized;
                break;
            case 2:   // folded over the stomach
                goalArm = (-up * 0.82f + sideDir * 0.16f + fwd * 0.10f).normalized;
                goalFore = (-up * 0.18f - sideDir * 0.88f + fwd * 0.45f).normalized;
                break;
            default:  // resting at the side, a little away from the hip
                goalArm = (-up * 0.94f + sideDir * 0.30f - fwd * 0.04f).normalized;
                goalFore = (-up * 0.96f + sideDir * 0.22f + fwd * 0.12f).normalized;
                break;
        }
        float sway = Mathf.Sin(t * 0.17f + sign) * 2.5f;
        goalArm = Quaternion.AngleAxis(sway, fwd) * goalArm;
        arm.rotation = Quaternion.FromToRotation(dir, goalArm) * arm.rotation;
        Vector3 fdir = (fore.childCount > 0 ? fore.GetChild(0).position - fore.position : dir);
        if (fdir.sqrMagnitude > 1e-8f)
            fore.rotation = Quaternion.FromToRotation(fdir.normalized, goalFore) * fore.rotation;
    }

    void Rot(string bone, Vector3 worldAxis, float degrees)
    {
        var b = B(bone);
        if (b == null) return;
        b.rotation = Quaternion.AngleAxis(degrees, worldAxis.normalized) * b.rotation;
    }
}

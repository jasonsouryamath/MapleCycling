using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lightweight procedural animation for Minato's real 24-joint crowd rigs.
///
/// This deliberately animates the imported skeleton instead of baking or swapping sprites.
/// Walkers ping-pong along an authored, corridor-safe segment; all other archetypes remain at
/// their staged anchor and receive only an appropriate idle/wave/sit/cyclist motion.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class MinatoCrowdActor : MonoBehaviour
{
    /// <summary>
    /// Correction for SkinnedMeshRenderer.BakeMesh on rigs whose glTF armature carries a unit
    /// scale (the Kuro-based bodies: armature 0.01, skin authored in centimetres). BakeMesh returns
    /// such a skin 100x too large, which made the crowd templates measure 130 m tall and broke the
    /// rendered-sole calibration. Compares the bake's height with the skeleton's real world span
    /// and snaps the ratio to a power of ten; returns 1 for ordinary scale-1 rigs (ratio ~1).
    /// </summary>
    public static float BakeUnitFix(SkinnedMeshRenderer smr, Mesh baked)
    {
        var bones = smr.bones;
        if (bones == null || bones.Length < 2 || baked == null) return 1f;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var b in bones)
        {
            if (b == null) continue;
            lo = Mathf.Min(lo, b.position.y);
            hi = Mathf.Max(hi, b.position.y);
        }
        float boneSpan = hi - lo;
        if (boneSpan < 1e-3f) return 1f;
        float ratio = baked.bounds.size.magnitude / boneSpan;
        if (ratio < 20f) return 1f;
        return Mathf.Pow(10f, -Mathf.Round(Mathf.Log10(ratio)));
    }

    /// <summary>BakeUnitFix variant for rebound drop-in models: compares baked world Y range with the bone Y span.</summary>
    public static float ReboundUnitFix(SkinnedMeshRenderer smr, Mesh baked)
    {
        var bones = smr.bones;
        if (bones == null || bones.Length < 2 || baked == null) return 1f;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var b in bones) { if (b == null) continue; lo = Mathf.Min(lo, b.position.y); hi = Mathf.Max(hi, b.position.y); }
        float span = hi - lo;
        if (span < 1e-4f) return 1f;
        var rot = smr.transform.rotation;
        float vlo = float.MaxValue, vhi = float.MinValue;
        foreach (var v in baked.vertices) { float y = (rot * v).y; vlo = Mathf.Min(vlo, y); vhi = Mathf.Max(vhi, y); }
        float ratio = (vhi - vlo) / span;
        if (ratio < 20f) return 1f;
        return Mathf.Pow(10f, -Mathf.Round(Mathf.Log10(ratio)));
    }

    public enum MotionKind { Walk, Idle, Wave, Sit, Cyclist }
    public const float NominalStridePerCycle = 0.52f;
    public const float HighDetailAnimationDistance = 90f;
    public const float RenderedSoleTargetClearance = 0.003f;
    public const float RenderedSoleTolerance = 0.006f;
    const float SwingSoleMinimumClearance = 0.012f;
    const float MaximumGroundCorrection = 0.15f;
    const float SoleBandHeight = 0.035f;
    const int SoleMarkersPerFoot = 8;
    public float GaitPeriodSeconds => externalDrive && _driveHz > 0.05f
        ? 1f / _driveHz
        : NominalStridePerCycle / Mathf.Max(0.1f, moveSpeed);

    // ---------------------------------------------------------------- external drive
    // 2026-09-26 copilot (ambient pedestrians): a PedestrianBrain (Assets/Ride/Pedestrians) may
    // take over WHERE the figure is and HOW FAST it walks. Default off, so every existing caller
    // (editor diagnostics, figures without a brain) behaves exactly as before.

    /// <summary>A brain places/rotates the root; <see cref="SamplePath"/> is skipped.</summary>
    [NonSerialized] public bool externalDrive;
    /// <summary>0 = standing still, 1 = full stride. Swing amplitudes scale by it.</summary>
    [NonSerialized] public float gaitWeight = 1f;
    /// <summary>Gait phase in radians, integrated by the brain so speed changes never jump the pose.</summary>
    [NonSerialized] public float drivePhase;
    [NonSerialized] public float _driveHz;
    /// <summary>Frame on which the skeleton was last rebuilt (overlays must only add on those frames).</summary>
    public int AnimatedFrame { get; private set; } = -1;
    public Transform Bone(string boneName)
    {
        Resolve();
        return _bones.TryGetValue(boneName, out var b) ? b : null;
    }
    public bool IsReady { get { Resolve(); return _ready; } }

    // ---------------------------------------------------------------- model swap (claude-peds 2026-09-26)
    // PedestrianModelSwap (Assets/Ride/Pedestrians) may retarget this actor onto a drop-in humanoid
    // model whose bones have other names (mixamorig:*, J_Bip_*). The alias map is only ever set by
    // Rebind; figures that are never swapped resolve exactly as before.
    [NonSerialized] Dictionary<string, Transform> _aliasBones;

    /// <summary>
    /// Re-points the procedural walk at a new skeleton. <paramref name="canonical"/> maps the names
    /// this class drives (Hips, LeftFoot, ...) to the new rig's transforms. Clears every cached pose,
    /// sole calibration and stride measurement so they are rebuilt from the new rig.
    /// </summary>
    public void Rebind(Transform newRig, Dictionary<string, Transform> canonical)
    {
        if (newRig == null) return;
        ResetBones();
        _bones.Clear(); _baseRot.Clear(); _basePos.Clear();
        _leftSoleMarkers.Clear(); _rightSoleMarkers.Clear();
        _soleCalibrationValid = false;
        _reportedInvalidCalibration = false;
        _supportFoot = null;
        _stridePerCycle = -1f;
        _hips = _leftFoot = _rightFoot = null;
        _skins = null;
        _ready = false;
        _aliasBones = canonical;
        rigRoot = newRig;
        Resolve();
    }

    float _stridePerCycle = -1f;
    /// <summary>
    /// Metres the root must travel per full gait cycle for the planted foot NOT to slide:
    /// measured once from this rig's own procedural walk pose (support-foot excursion under the
    /// hips over a cycle), in world metres at the figure's current scale.
    /// </summary>
    public float MeasuredStridePerCycle()
    {
        if (_stridePerCycle > 0f) return _stridePerCycle;
        Resolve();
        if (!_ready || _leftFoot == null || _rightFoot == null) return NominalStridePerCycle;
        var savedPos = transform.position;
        var savedRot = transform.rotation;
        float minL = float.MaxValue, maxL = float.MinValue, minR = float.MaxValue, maxR = float.MinValue;
        const int N = 24;
        for (int i = 0; i < N; i++)
        {
            ResetBones();
            float saved = gaitWeight;
            gaitWeight = 1f;
            DrivenWalkPose(i * Mathf.PI * 2f / N);
            gaitWeight = saved;
            Vector3 l = transform.InverseTransformPoint(_leftFoot.position);
            Vector3 r = transform.InverseTransformPoint(_rightFoot.position);
            minL = Mathf.Min(minL, l.z); maxL = Mathf.Max(maxL, l.z);
            minR = Mathf.Min(minR, r.z); maxR = Mathf.Max(maxR, r.z);
        }
        ResetBones();
        transform.SetPositionAndRotation(savedPos, savedRot);
        // Local z is in the figure's scaled space; one foot sweeps (max-min) backwards during its
        // half-cycle of stance, so the body covers twice that per full cycle.
        float excursion = 0.5f * ((maxL - minL) + (maxR - minR)) * transform.lossyScale.z;
        _stridePerCycle = excursion > 0.05f ? 2f * excursion : NominalStridePerCycle;
        return _stridePerCycle;
    }

    [Header("Authored motion")]
    public MotionKind motion;
    public Transform rigRoot;
    public Vector3 pathStart;
    public Vector3 pathEnd;
    [Min(0f)] public float moveSpeed = 0.78f;
    [Range(0f, 1f)] public float phaseOffset;
    [Min(0f)] public float footClearance = 0f;
    [Min(5f)] public float animationDistance = 115f;

    readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
    readonly Dictionary<string, Quaternion> _baseRot = new Dictionary<string, Quaternion>();
    readonly Dictionary<string, Vector3> _basePos = new Dictionary<string, Vector3>();
    readonly List<SoleMarker> _leftSoleMarkers = new List<SoleMarker>();
    readonly List<SoleMarker> _rightSoleMarkers = new List<SoleMarker>();
    SkinnedMeshRenderer[] _skins;
    Transform _hips, _leftFoot, _rightFoot;
    Transform _supportFoot;
    Vector3 _lastPathDirection;
    float _lastSurfaceY;
    float _leftRenderedSoleClearance;
    float _rightRenderedSoleClearance;
    float _lastGroundCorrection;
    bool _soleCalibrationValid;
    bool _reportedInvalidCalibration;
    bool _ready;

    sealed class SoleSkin
    {
        public SkinnedMeshRenderer renderer;
        public Vector3[] vertices;
        public BoneWeight[] weights;
        public Matrix4x4[] bindposes;
        public Transform[] bones;
    }

    readonly struct SoleMarker
    {
        public readonly SoleSkin skin;
        public readonly int vertexIndex;
        public readonly Vector3 calibrationLocal;

        public SoleMarker(SoleSkin source, int index, Vector3 local)
        {
            skin = source;
            vertexIndex = index;
            calibrationLocal = local;
        }
    }

    readonly struct SoleCandidate
    {
        public readonly SoleSkin skin;
        public readonly int vertexIndex;
        public readonly Vector3 calibrationLocal;

        public SoleCandidate(SoleSkin source, int index, Vector3 local)
        {
            skin = source;
            vertexIndex = index;
            calibrationLocal = local;
        }
    }

    readonly struct SoleSample
    {
        public readonly bool valid;
        public readonly float clearance;
        public readonly float surfaceY;
        public readonly Vector3 worldPoint;

        public SoleSample(bool isValid, float soleClearance, float y, Vector3 point)
        {
            valid = isValid;
            clearance = soleClearance;
            surfaceY = y;
            worldPoint = point;
        }
    }

    public void Configure(MotionKind kind, Transform liveRig, Vector3 start, Vector3 end,
                          float speed, float phase)
    {
        motion = kind;
        rigRoot = liveRig;
        pathStart = start;
        pathEnd = end;
        moveSpeed = speed;
        phaseOffset = Mathf.Repeat(phase, 1f);
        footClearance = 0f;
        animationDistance = HighDetailAnimationDistance;
        Resolve();
        // Keep the serialized skeleton in its neutral donor pose. Saving a sampled gait pose
        // makes Awake treat a different stride phase as each instance's bind pose, which in turn
        // invalidates sole calibration and can bury an otherwise-correct walker.
        ResetBones();
    }

    void Awake()
    {
        ApplyDimensionalLodPolicy();
        Resolve();
    }

    void OnEnable()
    {
        ApplyDimensionalLodPolicy();
        Resolve();
        if (Application.isPlaying) { Enabled.Add(this); RegistryVersion++; }
    }

    void OnDisable() { Enabled.Remove(this); }

    /// <summary>Enabled actors (play mode) + a version bump on every enable, so the ambient
    /// pedestrian director dresses newly activated figures the same frame, before they render.</summary>
    public static readonly HashSet<MinatoCrowdActor> Enabled = new HashSet<MinatoCrowdActor>();
    public static int RegistryVersion;

    /// <summary>
    /// Existing Minato scenes are intentionally enormous, so crowd presentation must not depend
    /// on somebody rebuilding/resaving the entire scene after this component changes.  Enforce
    /// the same policy used by the editor builder when each lightweight crowd actor wakes up.
    /// </summary>
    void ApplyDimensionalLodPolicy()
    {
        var group = GetComponent<LODGroup>();
        if (group == null) return;
        var lods = group.GetLODs();
        if (lods.Length < 2) return;
        lods[0].screenRelativeTransitionHeight = 0.013f;
        lods[1].screenRelativeTransitionHeight = 0.0045f;
        group.fadeMode = LODFadeMode.None;
        group.animateCrossFading = false;
        group.SetLODs(lods);
    }

    void Resolve()
    {
        if (_ready || rigRoot == null) return;
        if (_aliasBones != null)
            foreach (var pair in _aliasBones)
            {
                if (pair.Value == null || _bones.ContainsKey(pair.Key)) continue;
                _bones.Add(pair.Key, pair.Value);
                _baseRot.Add(pair.Key, pair.Value.localRotation);
                _basePos.Add(pair.Key, pair.Value.localPosition);
            }
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (_bones.ContainsKey(t.name)) continue;
            _bones.Add(t.name, t);
            _baseRot.Add(t.name, t.localRotation);
            _basePos.Add(t.name, t.localPosition);
        }
        _bones.TryGetValue("Hips", out _hips);
        _bones.TryGetValue("LeftFoot", out _leftFoot);
        _bones.TryGetValue("RightFoot", out _rightFoot);
        _skins = rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in _skins)
        {
            // The donor is split across several skinned material renderers. Unity's imported
            // per-renderer bounds do not cover the procedurally animated stride, so disabling
            // offscreen updates culls thighs/calves independently and leaves floating shoes.
            // Keep the live skin updated; the LODGroup/animation-distance gates are the safe
            // performance controls for this high-fidelity tier.
            smr.updateWhenOffscreen = !Application.isPlaying;
            smr.shadowCastingMode = ShadowCastingMode.On;
            smr.receiveShadows = true;
            smr.lightProbeUsage = LightProbeUsage.BlendProbes;
            smr.reflectionProbeUsage = ReflectionProbeUsage.Simple;
            smr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            smr.skinnedMotionVectors = true;
        }
        _ready = _hips != null;
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        var cam = Camera.main;
        bool nearCamera = cam == null ||
                          (cam.transform.position - transform.position).sqrMagnitude <=
                          animationDistance * animationDistance;
        SetSkinUpdates(nearCamera);
        if (!nearCamera) return;
        Sample(Time.time + phaseOffset * 4.19f, true);
    }

    /// <summary>Deterministic hook used by editor capture tooling at multiple timestamps.</summary>
    public void SampleForDiagnostics(float time)
    {
        Resolve();
        EnsureRenderedSoleMarkers();
        SetSkinUpdates(true);
        Sample(time, false);
    }

    public string DiagnosticLimbState()
    {
        Resolve();
        string[] names = { "LeftUpLeg", "LeftLeg", "LeftFoot",
                           "RightUpLeg", "RightLeg", "RightFoot" };
        var liveBones = new HashSet<Transform>();
        foreach (var smr in rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var bone in smr.bones)
                if (bone != null) liveBones.Add(bone);
        var parts = new List<string>();
        foreach (string name in names)
        {
            if (!_bones.TryGetValue(name, out var bone) || bone == null)
                parts.Add(name + "=missing");
            else
                parts.Add($"{name}=({bone.position.x:0.00},{bone.position.y:0.00}," +
                          $"{bone.position.z:0.00})/skin:{liveBones.Contains(bone)}");
        }
        return string.Join(" ", parts);
    }

    public string RuntimeContactSummary()
    {
        Resolve();
        EnsureRenderedSoleMarkers();
        return $"support={(_supportFoot != null ? _supportFoot.name : "none")} " +
               $"leftRenderedSole={_leftRenderedSoleClearance:0.000}m " +
               $"rightRenderedSole={_rightRenderedSoleClearance:0.000}m " +
               $"surface={_lastSurfaceY:0.000} correction={_lastGroundCorrection:0.000}m " +
               $"markers={_leftSoleMarkers.Count}/{_rightSoleMarkers.Count}";
    }

    public bool RuntimeRenderedContactPass(out string reason)
    {
        Resolve();
        EnsureRenderedSoleMarkers();
        if (!_soleCalibrationValid)
        {
            reason = "rendered sole calibration is unavailable";
            return false;
        }
        if (float.IsInfinity(_leftRenderedSoleClearance) ||
            float.IsInfinity(_rightRenderedSoleClearance))
        {
            reason = "one or more rendered shoe soles could not sample the intended surface";
            return false;
        }
        float support = _supportFoot == _rightFoot
            ? _rightRenderedSoleClearance
            : _leftRenderedSoleClearance;
        float lowest = Mathf.Min(_leftRenderedSoleClearance, _rightRenderedSoleClearance);
        if (lowest < -RenderedSoleTolerance)
        {
            reason = $"rendered shoe penetration {lowest:0.0000}m";
            return false;
        }
        if (Mathf.Abs(support - RenderedSoleTargetClearance) > RenderedSoleTolerance)
        {
            reason = $"support rendered sole clearance {support:0.0000}m";
            return false;
        }
        reason = "";
        return true;
    }

    void Sample(float time, bool runtimeFootPlant)
    {
        if (!_ready) return;
        ResetBones();

        AnimatedFrame = Time.frameCount;
        float gaitHz = motion == MotionKind.Walk
            ? Mathf.Clamp(moveSpeed / NominalStridePerCycle, 1.0f, 2.0f)
            : 1.55f;
        float phase = time * gaitHz * Mathf.PI * 2f;
        if (externalDrive && runtimeFootPlant) phase = drivePhase;
        switch (motion)
        {
            case MotionKind.Walk:
                if (!(externalDrive && runtimeFootPlant)) SamplePath(time);
                if (externalDrive && runtimeFootPlant) DrivenWalkPose(phase);
                else WalkPose(phase);
                if (externalDrive && runtimeFootPlant && gaitWeight < 0.999f)
                    IdleBreath(time, 1f - gaitWeight);
                break;
            case MotionKind.Wave:
                IdlePose(time, 1.0f);
                RotateWorld("RightForeArm", transform.forward, Mathf.Sin(time * 4.2f) * 13f);
                RotateWorld("RightHand", transform.up, Mathf.Sin(time * 5.1f + 0.7f) * 10f);
                break;
            case MotionKind.Sit:
                IdlePose(time, 0.55f);
                Rotate("LeftFoot", Vector3.right, Mathf.Sin(time * 1.7f) * 4f);
                Rotate("RightFoot", Vector3.right, -Mathf.Sin(time * 1.5f + 0.8f) * 4f);
                break;
            case MotionKind.Cyclist:
                IdlePose(time, 0.35f);
                Rotate("Head", Vector3.up, Mathf.Sin(time * 0.55f) * 2.2f);
                break;
            default:
                IdlePose(time, 0.7f);
                break;
        }

        Transform preferredSupport = null;
        if (motion == MotionKind.Walk)
        {
            float swing = Mathf.Sin(phase);
            preferredSupport = Mathf.Abs(swing) < 0.03f && _supportFoot != null
                ? _supportFoot
                : swing >= 0f ? _leftFoot : _rightFoot;
            if (externalDrive && runtimeFootPlant)
                preferredSupport = _drivenLeftStance ? _leftFoot : _rightFoot;
        }
        GroundAnimatedFeet(runtimeFootPlant && motion == MotionKind.Walk, preferredSupport);
    }

    void ResetBones()
    {
        foreach (var pair in _baseRot)
        {
            if (!_bones.TryGetValue(pair.Key, out var bone) || bone == null) continue;
            bone.localRotation = pair.Value;
            bone.localPosition = _basePos[pair.Key];
        }
    }

    /// <summary>
    /// Degrees to lower each upper arm from the donor's rest pose toward hanging straight down,
    /// before any swing or gesture. The donors are authored in an A-pose with the arms held out
    /// ~40 deg, which read as stiff "arms out" walkers in Maple City (2026-09-25 review).
    /// 0 = unchanged (Minato's crowd keeps its approved look); Maple City sets it.
    /// </summary>
    public float armDropDegrees = 0f;

    void DropArms()
    {
        if (armDropDegrees <= 0f) return;
        foreach (var (upper, lower) in new[] { ("LeftArm", "LeftForeArm"), ("RightArm", "RightForeArm") })
        {
            if (!_bones.TryGetValue(upper, out var arm) || !_bones.TryGetValue(lower, out var fore) ||
                arm == null || fore == null) continue;
            Vector3 dir = fore.position - arm.position;
            if (dir.sqrMagnitude < 1e-8f) continue;
            dir.Normalize();
            // stop a little short of vertical so the hand clears the hip
            Vector3 hang = Vector3.Slerp(-transform.up, Vector3.ProjectOnPlane(dir, transform.forward).normalized, 0.14f);
            Vector3 goal = Vector3.RotateTowards(dir, hang, armDropDegrees * Mathf.Deg2Rad, 0f);
            arm.rotation = Quaternion.FromToRotation(dir, goal) * arm.rotation;
        }
    }

    void WalkPose(float phase)
    {
        DropArms();
        float w = externalDrive ? Mathf.Clamp01(gaitWeight) : 1f;
        float swing = Mathf.Sin(phase) * w;
        float bob = (1f - Mathf.Abs(Mathf.Cos(phase))) * 0.018f * w;
        if (_hips != null) _hips.localPosition = _basePos["Hips"] + Vector3.up * bob;

        Vector3 lateral = transform.right;
        RotateWorld("LeftUpLeg", lateral, swing * 32f);
        RotateWorld("RightUpLeg", lateral, -swing * 32f);
        RotateWorld("LeftLeg", lateral, Mathf.Max(0f, -swing) * 42f);
        RotateWorld("RightLeg", lateral, Mathf.Max(0f, swing) * 42f);
        RotateWorld("LeftFoot", lateral, -swing * 13f);
        RotateWorld("RightFoot", lateral, swing * 13f);

        RotateWorld("LeftArm", lateral, -swing * 24f);
        RotateWorld("RightArm", lateral, swing * 24f);
        RotateWorld("LeftForeArm", lateral, 5f + Mathf.Max(0f, swing) * 12f);
        RotateWorld("RightForeArm", lateral, 5f + Mathf.Max(0f, -swing) * 12f);
        RotateWorld("Spine02", transform.forward, Mathf.Sin(phase * 0.5f) * 2.2f * w);
        RotateWorld("Head", transform.up, Mathf.Sin(phase * 0.5f + 0.4f) * 1.8f * w);
    }

    bool _drivenLeftStance = true;

    /// <summary>Harness A/B only (PedestrianPlaymodeRunner): the pre-2026-09-26 foot pitch that rocked with the thigh.</summary>
    public static bool LegacyFootPlacement;

    /// <summary>
    /// AMBIENT PEDESTRIANS (copilot 2026-09-26): gait used only while a PedestrianBrain drives
    /// this actor. Unlike the sine gait above (whose "lower" foot swings forward for half of its
    /// support time, so no phase rate can stop it skating) this is a 50% duty-cycle walk: the
    /// stance foot sweeps back LINEARLY (constant speed = body speed when the brain integrates
    /// the phase at speed / MeasuredStridePerCycle) and the swing foot lifts (knee) and returns
    /// eased. Arms counter-swing with the opposite leg; small pelvis/chest counter-rotation.
    /// gaitWeight scales stride and lift (short steps when starting/stopping).
    /// </summary>
    void DrivenWalkPose(float phase)
    {
        DropArms();
        float w = Mathf.Clamp01(gaitWeight);
        float u = Mathf.Repeat(phase / (Mathf.PI * 2f), 1f);
        LegProfile(u, out float xl, out float liftL);
        LegProfile(Mathf.Repeat(u + 0.5f, 1f), out float xr, out float liftR);
        _drivenLeftStance = u < 0.5f;
        xl *= w; xr *= w; liftL *= w; liftR *= w;
        // two bobs per cycle: lowest at double support, highest mid-stance
        float bob = (0.5f - 0.5f * Mathf.Cos(phase * 2f)) * 0.016f * w;
        if (_hips != null) _hips.localPosition = _basePos["Hips"] + Vector3.up * bob;

        Vector3 lateral = transform.right;
        // positive rotation about +right swings a hanging leg BACK; x = +1 is foot forward
        RotateWorld("LeftUpLeg", lateral, -xl * 30f - liftL * 8f);
        RotateWorld("RightUpLeg", lateral, -xr * 30f - liftR * 8f);
        RotateWorld("LeftLeg", lateral, liftL * 46f);
        RotateWorld("RightLeg", lateral, liftR * 46f);
        // FOOT PLACEMENT (QA 2026-09-26, "airborne" walkers): the foot used to inherit the whole
        // thigh swing (+-30 deg) plus a small +-9 deg of its own, so the stance foot rocked from
        // 21 deg toe-up to 21 deg toe-down and only a heel or toe tip ever touched the pavement -
        // with the swing foot up too, both big chibi shoes read as off the ground. Now the thigh +
        // knee pitch is cancelled so the sole stays FLAT on the ground through mid-stance, with a
        // short heel strike and a late heel-off (toe stays down, heel rises), then the swing foot
        // eases from toe-off back to a slight toe-up for the next contact.
        float legL = -xl * 30f - liftL * 8f + liftL * 46f * 0.7f;
        float legR = -xr * 30f - liftR * 8f + liftR * 46f * 0.7f;   // swing: the ankle follows 30% of the knee bend
        if (LegacyFootPlacement)
        {
            RotateWorld("LeftFoot", lateral, xl * 9f - liftL * 12f);
            RotateWorld("RightFoot", lateral, xr * 9f - liftR * 12f);
        }
        else
        {
            RotateWorld("LeftFoot", lateral, -legL + FootRoll(u) * w);
            RotateWorld("RightFoot", lateral, -legR + FootRoll(Mathf.Repeat(u + 0.5f, 1f)) * w);
        }

        // counter-swing: the left arm comes forward (negative) with the RIGHT leg
        RotateWorld("LeftArm", lateral, -xr * 22f);
        RotateWorld("RightArm", lateral, -xl * 22f);
        RotateWorld("LeftForeArm", lateral, 5f + Mathf.Max(0f, xr) * 12f);
        RotateWorld("RightForeArm", lateral, 5f + Mathf.Max(0f, xl) * 12f);
        float twist = (xl - xr) * 0.5f;
        RotateWorld("Spine", transform.up, twist * 3.0f);
        RotateWorld("Spine02", transform.up, -twist * 4.5f);
        RotateWorld("Spine02", transform.forward, Mathf.Sin(phase) * 1.6f * w);
        RotateWorld("Head", transform.up, twist * 3.2f);
    }

    /// <summary>
    /// World foot pitch (deg, + = toe down) at leg phase u, on top of the cancelled leg pitch:
    /// heel strike (toe up) easing to FLAT by 12% of stance, flat to 72%, heel-off to 20 deg toe
    /// down at toe-off, then eased back to a slight toe-up through swing for the next contact.
    /// </summary>
    static float FootRoll(float u)
    {
        static float S(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        if (u < 0.5f)
        {
            float p = u * 2f;
            if (p < 0.12f) return -10f * (1f - S(p / 0.12f));
            if (p < 0.72f) return 0f;
            return 20f * S((p - 0.72f) / 0.28f);
        }
        return Mathf.Lerp(20f, -10f, S((u - 0.5f) * 2f));
    }

    /// <summary>Foot forwardness x (+1 front .. -1 back) and knee lift (0..1) at leg phase u.</summary>
    static void LegProfile(float u, out float x, out float lift)
    {
        if (u < 0.5f)
        {
            x = 1f - 4f * u;          // stance: linear sweep back
            lift = 0f;
        }
        else
        {
            float k = (u - 0.5f) * 2f; // swing: eased return + knee lift
            float e = k * k * (3f - 2f * k);
            x = -1f + 2f * e;
            lift = Mathf.Sin(k * Mathf.PI);
        }
    }

    /// <summary>Additive standing breath for a driven walker that has slowed or stopped.</summary>
    void IdleBreath(float time, float weight)
    {
        float breathe = Mathf.Sin(time * 1.45f + phaseOffset * 6.28f);
        RotateWorld("Spine02", transform.right, breathe * 1.15f * weight);
        if (_hips != null) _hips.localPosition += Vector3.up * ((breathe + 1f) * 0.0035f * weight);
    }

    void IdlePose(float time, float weight)
    {
        DropArms();
        float breathe = Mathf.Sin(time * 1.45f + phaseOffset * 6.28f);
        Rotate("Spine02", Vector3.right, breathe * 1.15f * weight);
        Rotate("Head", Vector3.up, Mathf.Sin(time * 0.62f + phaseOffset * 3.1f) * 2.4f * weight);
        if (_hips != null)
            _hips.localPosition = _basePos["Hips"] + Vector3.up * ((breathe + 1f) * 0.0035f * weight);
    }

    void SamplePath(float time)
    {
        Vector3 delta = pathEnd - pathStart;
        float length = delta.magnitude;
        if (length < 0.1f || moveSpeed <= 0f) return;
        float travelled = Mathf.Max(0f, time) * moveSpeed + phaseOffset * length;
        int leg = Mathf.FloorToInt(travelled / length);
        float u = Mathf.Repeat(travelled, length) / length;
        bool reverse = (leg & 1) != 0;
        float t = reverse ? 1f - u : u;
        Vector3 direction = reverse ? -delta.normalized : delta.normalized;
        Vector3 p = Vector3.Lerp(pathStart, pathEnd, t);
        p.y = Ground(p, transform.position.y, out Vector3 normal);
        transform.position = p;
        if (direction.sqrMagnitude > 0.001f)
        {
            if (_lastPathDirection.sqrMagnitude > 0.5f &&
                Vector3.Dot(_lastPathDirection, direction) < 0f)
                _supportFoot = null;
            _lastPathDirection = direction;
            Vector3 slopeForward = Vector3.ProjectOnPlane(direction, normal).normalized;
            transform.rotation = Quaternion.LookRotation(slopeForward, normal);
        }
    }

    void GroundAnimatedFeet(bool plantSupportFoot, Transform preferredSupport)
    {
        float surface = Ground(transform.position, transform.position.y, out _);
        _lastSurfaceY = surface;
        Vector3 p = transform.position;
        p.y = surface;
        transform.position = p;

        if (motion != MotionKind.Walk)
        {
            _supportFoot = null;
            _lastGroundCorrection = 0f;
            return;
        }

        EnsureRenderedSoleMarkers();
        if (_leftFoot == null || _rightFoot == null || !_soleCalibrationValid)
        {
            _supportFoot = null;
            _lastGroundCorrection = 0f;
            if (!_reportedInvalidCalibration)
            {
                Debug.LogError($"[minato-sole] '{name}' has no valid rendered shoe-sole markers.");
                _reportedInvalidCalibration = true;
            }
            return;
        }

        SoleSample left = SampleRenderedSole(_leftSoleMarkers, surface);
        SoleSample right = SampleRenderedSole(_rightSoleMarkers, surface);
        Transform support = plantSupportFoot && preferredSupport != null
            ? preferredSupport
            : left.clearance <= right.clearance ? _leftFoot : _rightFoot;
        SoleSample supportSample = support == _rightFoot ? right : left;
        if (!left.valid || !right.valid || !supportSample.valid)
        {
            _leftRenderedSoleClearance = left.clearance;
            _rightRenderedSoleClearance = right.clearance;
            _supportFoot = support;
            return;
        }

        float correction = RenderedSoleTargetClearance + footClearance -
                           supportSample.clearance;
        if (Mathf.Abs(correction) > MaximumGroundCorrection)
        {
            _soleCalibrationValid = false;
            Debug.LogError($"[minato-sole] '{name}' rejected an implausible rendered-sole " +
                           $"correction of {correction:0.000}m.");
            return;
        }
        transform.position += Vector3.up * correction;
        _lastGroundCorrection = correction;

        left = SampleRenderedSole(_leftSoleMarkers, surface);
        right = SampleRenderedSole(_rightSoleMarkers, surface);
        if (plantSupportFoot && preferredSupport != null)
        {
            Transform swingFoot = preferredSupport == _leftFoot ? _rightFoot : _leftFoot;
            SoleSample swing = swingFoot == _rightFoot ? right : left;
            if (swing.valid && swing.clearance < SwingSoleMinimumClearance)
            {
                swingFoot.position += transform.up *
                    (SwingSoleMinimumClearance - swing.clearance);
                left = SampleRenderedSole(_leftSoleMarkers, surface);
                right = SampleRenderedSole(_rightSoleMarkers, surface);
            }
        }

        SoleSample finalSupport = support == _rightFoot ? right : left;
        if (finalSupport.valid)
        {
            float finalCorrection = RenderedSoleTargetClearance + footClearance -
                                    finalSupport.clearance;
            if (Mathf.Abs(finalCorrection) <= MaximumGroundCorrection &&
                Mathf.Abs(finalCorrection) > 0.0005f)
            {
                transform.position += Vector3.up * finalCorrection;
                _lastGroundCorrection += finalCorrection;
                left = SampleRenderedSole(_leftSoleMarkers, surface);
                right = SampleRenderedSole(_rightSoleMarkers, surface);
            }
        }

        _leftRenderedSoleClearance = left.clearance;
        _rightRenderedSoleClearance = right.clearance;
        _lastSurfaceY = support == _rightFoot ? right.surfaceY : left.surfaceY;
        if (!plantSupportFoot)
        {
            _supportFoot = null;
            return;
        }

        _supportFoot = preferredSupport;
    }

    void BuildRenderedSoleMarkers()
    {
        _leftSoleMarkers.Clear();
        _rightSoleMarkers.Clear();
        var leftCandidates = new List<SoleCandidate>();
        var rightCandidates = new List<SoleCandidate>();

        foreach (var smr in _skins)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] bones = smr.bones;
            if (vertices.Length == 0 || weights.Length != vertices.Length ||
                bindposes.Length == 0 || bones.Length == 0)
                continue;

            int leftIndex = Array.IndexOf(bones, _leftFoot);
            int rightIndex = Array.IndexOf(bones, _rightFoot);
            if (leftIndex < 0 && rightIndex < 0) continue;

            var source = new SoleSkin
            {
                renderer = smr,
                vertices = vertices,
                weights = weights,
                bindposes = bindposes,
                bones = bones
            };

            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            Vector3[] bakedVertices = baked.vertices;
            // rebound drop-in models (claude-peds): only apply the unit fix when the baked HEIGHT is
            // 20x the skeleton's height (T-pose arm span / tiny-unit exports fooled the bounds test)
            float unitFix = _aliasBones != null ? ReboundUnitFix(smr, baked) : BakeUnitFix(smr, baked);
            if (unitFix != 1f)
                for (int k = 0; k < bakedVertices.Length; k++) bakedVertices[k] *= unitFix;
            for (int i = 0; i < vertices.Length && i < bakedVertices.Length; i++)
            {
                BoneWeight weight = weights[i];
                float leftWeight = WeightForBone(weight, leftIndex);
                float rightWeight = WeightForBone(weight, rightIndex);
                if (leftWeight < 0.20f && rightWeight < 0.20f) continue;
                Vector3 local = transform.InverseTransformPoint(
                    smr.transform.TransformPoint(bakedVertices[i]));
                if (leftWeight >= 0.20f)
                    leftCandidates.Add(new SoleCandidate(source, i, local));
                if (rightWeight >= 0.20f)
                    rightCandidates.Add(new SoleCandidate(source, i, local));
            }

            Destroy(baked);
        }

        SelectSoleMarkers(leftCandidates, _leftSoleMarkers);
        SelectSoleMarkers(rightCandidates, _rightSoleMarkers);
        _soleCalibrationValid = _leftSoleMarkers.Count >= 3 && _rightSoleMarkers.Count >= 3;
        if (!_soleCalibrationValid)
            Debug.LogError($"[minato-sole] '{name}' calibrated only " +
                           $"{_leftSoleMarkers.Count}/{_rightSoleMarkers.Count} sole markers.");
    }

    void EnsureRenderedSoleMarkers()
    {
        if (_soleCalibrationValid || _leftSoleMarkers.Count > 0 ||
            _rightSoleMarkers.Count > 0)
            return;
        BuildRenderedSoleMarkers();
    }

    static float WeightForBone(BoneWeight weight, int bone)
    {
        if (bone < 0) return 0f;
        float result = 0f;
        if (weight.boneIndex0 == bone) result += weight.weight0;
        if (weight.boneIndex1 == bone) result += weight.weight1;
        if (weight.boneIndex2 == bone) result += weight.weight2;
        if (weight.boneIndex3 == bone) result += weight.weight3;
        return result;
    }

    static void SelectSoleMarkers(List<SoleCandidate> candidates, List<SoleMarker> output)
    {
        if (candidates.Count == 0) return;
        float lowest = candidates.Min(c => c.calibrationLocal.y);
        candidates.RemoveAll(c => c.calibrationLocal.y > lowest + SoleBandHeight);
        if (candidates.Count == 0) return;

        var selected = new List<SoleCandidate> { candidates.OrderBy(c => c.calibrationLocal.y).First() };
        while (selected.Count < SoleMarkersPerFoot && selected.Count < candidates.Count)
        {
            SoleCandidate best = default;
            float bestDistance = -1f;
            foreach (var candidate in candidates)
            {
                if (selected.Any(s => s.skin == candidate.skin &&
                                      s.vertexIndex == candidate.vertexIndex))
                    continue;
                Vector2 point = new Vector2(candidate.calibrationLocal.x,
                                            candidate.calibrationLocal.z);
                float nearest = selected.Min(s => (
                    point - new Vector2(s.calibrationLocal.x, s.calibrationLocal.z)).sqrMagnitude);
                if (nearest <= bestDistance) continue;
                bestDistance = nearest;
                best = candidate;
            }
            if (bestDistance < 0f) break;
            selected.Add(best);
        }

        foreach (var marker in selected)
            output.Add(new SoleMarker(marker.skin, marker.vertexIndex,
                                      marker.calibrationLocal));
    }

    SoleSample SampleRenderedSole(List<SoleMarker> markers, float fallbackSurface)
    {
        if (markers.Count == 0)
            return new SoleSample(false, float.PositiveInfinity, fallbackSurface,
                                  transform.position);

        float lowestClearance = float.PositiveInfinity;
        float sampledSurface = fallbackSurface;
        Vector3 lowestPoint = transform.position;
        foreach (var marker in markers)
        {
            Vector3 world = SkinWorldPoint(marker);
            float surface = Ground(world, fallbackSurface, out _);
            // A sole at a mesh/collider seam must not snap to a lower road, quay, or terrain
            // layer. The actor's centre ray establishes the intended rendered walking surface;
            // shoe samples may follow its local slope but not switch layers.
            if (Mathf.Abs(surface - fallbackSurface) > 0.15f) continue;
            float clearance = world.y - surface;
            if (clearance >= lowestClearance) continue;
            lowestClearance = clearance;
            sampledSurface = surface;
            lowestPoint = world;
        }
        return float.IsPositiveInfinity(lowestClearance)
            ? new SoleSample(false, float.PositiveInfinity, fallbackSurface,
                             transform.position)
            : new SoleSample(true, lowestClearance, sampledSurface, lowestPoint);
    }

    static Vector3 SkinWorldPoint(SoleMarker marker)
    {
        SoleSkin source = marker.skin;
        BoneWeight weight = source.weights[marker.vertexIndex];
        Vector3 vertex = source.vertices[marker.vertexIndex];
        Vector3 result = Vector3.zero;
        float total = 0f;
        AccumulateSkin(ref result, ref total, source, vertex,
                       weight.boneIndex0, weight.weight0);
        AccumulateSkin(ref result, ref total, source, vertex,
                       weight.boneIndex1, weight.weight1);
        AccumulateSkin(ref result, ref total, source, vertex,
                       weight.boneIndex2, weight.weight2);
        AccumulateSkin(ref result, ref total, source, vertex,
                       weight.boneIndex3, weight.weight3);
        return total > 0.0001f
            ? result / total
            : source.renderer.transform.TransformPoint(vertex);
    }

    static void AccumulateSkin(ref Vector3 result, ref float total, SoleSkin source,
                               Vector3 vertex, int boneIndex, float weight)
    {
        if (weight <= 0f || boneIndex < 0 || boneIndex >= source.bones.Length ||
            boneIndex >= source.bindposes.Length || source.bones[boneIndex] == null)
            return;
        Matrix4x4 skin = source.bones[boneIndex].localToWorldMatrix *
                         source.bindposes[boneIndex];
        result += skin.MultiplyPoint3x4(vertex) * weight;
        total += weight;
    }

    void SetSkinUpdates(bool enabled)
    {
        if (_skins == null) return;
        foreach (var smr in _skins)
            if (smr != null && smr.updateWhenOffscreen != enabled)
                smr.updateWhenOffscreen = enabled;
    }

    static float Ground(Vector3 point, float fallback, out Vector3 normal)
    {
        var ray = new Ray(new Vector3(point.x, point.y + 80f, point.z), Vector3.down);
        var hits = Physics.RaycastAll(ray, 180f, ~0, QueryTriggerInteraction.Ignore);
        int bestPriority = -1;
        float bestY = float.NegativeInfinity;
        normal = Vector3.up;
        foreach (var hit in hits)
        {
            var mf = hit.collider.GetComponent<MeshFilter>();
            var mr = hit.collider.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null || !mr.enabled) continue;
            string n = hit.collider.name;
            int priority = n.IndexOf("Beach Sand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           n.StartsWith("Connected Port", StringComparison.Ordinal) ? 2 :
                           n.StartsWith("Terrain_Chunk", StringComparison.Ordinal) ? 1 : 0;
            if (priority > bestPriority || priority == bestPriority && hit.point.y > bestY)
            {
                bestPriority = priority;
                bestY = hit.point.y;
                normal = hit.normal.sqrMagnitude > 0.5f ? hit.normal.normalized : Vector3.up;
            }
        }
        return bestPriority >= 0 ? bestY : fallback;
    }

    void Rotate(string boneName, Vector3 localAxis, float degrees)
    {
        if (!_bones.TryGetValue(boneName, out var bone) || bone == null ||
            !_baseRot.TryGetValue(boneName, out var bind)) return;
        bone.localRotation = bind * Quaternion.AngleAxis(degrees, localAxis);
    }

    void RotateWorld(string boneName, Vector3 worldAxis, float degrees)
    {
        if (!_bones.TryGetValue(boneName, out var bone) || bone == null) return;
        bone.rotation = Quaternion.AngleAxis(degrees, worldAxis.normalized) * bone.rotation;
    }
}

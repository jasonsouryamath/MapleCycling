using System;
using UnityEngine;

/// <summary>
/// AmbientSway: swings objects such as flags, tethered balloons, hanging signs, and gondola cabins.
/// Supports clean harmonic Pendulum mode or natural multi-octave Noise mode.
/// Zero physics, zero per-frame allocations, and ZERO work beyond cullDistance (~1.5 km).
/// Deterministic from seed.
/// Can drive multiple target transforms from one component or its own transform if targets is empty.
/// </summary>
public sealed class AmbientSway : MonoBehaviour
{
    public enum SwayMode
    {
        Pendulum = 0,
        Noise = 1
    }

    [Header("Mode & Targets")]
    public SwayMode mode = SwayMode.Pendulum;
    [Tooltip("Transforms to sway. If empty, sways this GameObject's transform.")]
    public Transform[] targets = new Transform[0];

    [Header("Angular Sway")]
    [Tooltip("Primary swing axis in local space.")]
    public Vector3 axis = Vector3.forward;
    [Tooltip("Secondary cross-axis in local space for 2D pendulum/flutter.")]
    public Vector3 secondaryAxis = Vector3.right;
    [Tooltip("Max primary swing angle in degrees.")]
    public float maxAngle = 8f;
    [Tooltip("Max secondary swing angle in degrees.")]
    public float secondaryAngle = 3f;
    [Tooltip("Primary swing frequency in Hz.")]
    public float frequency = 0.5f;
    [Tooltip("Phase offset spread between successive targets (radians).")]
    public float phaseOffsetPerTarget = 0.45f;

    [Header("Positional Sway (e.g. Balloons / Buoys)")]
    [Tooltip("Positional sway amplitude in metres.")]
    public Vector3 positionSway = Vector3.zero;
    [Tooltip("Positional sway frequency in Hz.")]
    public float positionFrequency = 0.3f;

    [Header("Performance & Determinism")]
    public float cullDistance = AmbientCull.DefaultCullDistance;
    public int seed = 42;

    private Quaternion[] _baseRotations;
    private Vector3[] _basePositions;
    private float[] _targetPhases;
    private float _time;
    private float _cullTimer;
    private bool _isCulled;
    private bool _baked;

    public float CurrentTime => _time;
    public bool IsCulled => _isCulled;
    public int TargetCount => (targets != null && targets.Length > 0) ? targets.Length : 1;

    private void Awake()
    {
        Bake();
    }

    private void OnEnable()
    {
        Bake();
    }

    /// <summary>
    /// Bakes initial base rotations/positions and preallocates arrays to ensure zero runtime GC allocations.
    /// </summary>
    public void Bake()
    {
        int count = (targets != null && targets.Length > 0) ? targets.Length : 1;
        if (_baseRotations == null || _baseRotations.Length != count)
        {
            _baseRotations = new Quaternion[count];
            _basePositions = new Vector3[count];
            _targetPhases = new float[count];
        }

        int s = seed;
        if (targets != null && targets.Length > 0)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    _baseRotations[i] = targets[i].localRotation;
                    _basePositions[i] = targets[i].localPosition;
                }
                else
                {
                    _baseRotations[i] = Quaternion.identity;
                    _basePositions[i] = Vector3.zero;
                }
                s = unchecked(s * 1664525 + 1013904223);
                float r = (s & 0xFFFF) / 65535f;
                _targetPhases[i] = i * phaseOffsetPerTarget + r * 1.5f;
            }
        }
        else
        {
            _baseRotations[0] = transform.localRotation;
            _basePositions[0] = transform.localPosition;
            _targetPhases[0] = 0f;
        }

        _cullTimer = seed != 0 ? (Mathf.Abs(seed * 19349663) % 100) * 0.0035f : 0f;
        _baked = true;
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    /// <summary>
    /// Explicit step advancing sway simulation by dt seconds.
    /// </summary>
    public void Step(float dt)
    {
        if (!_baked) Bake();

        // Throttled culling check (~3 times/sec)
        _cullTimer -= dt;
        if (_cullTimer <= 0f)
        {
            _cullTimer = 0.35f;
            Vector3 camPos = AmbientCull.GetReferencePosition(out bool hasRef);
            if (hasRef)
            {
                _isCulled = (transform.position - camPos).sqrMagnitude > (cullDistance * cullDistance);
            }
            else
            {
                _isCulled = false;
            }
        }

        if (_isCulled) return;

        _time += dt;

        Vector3 normAxis = axis.sqrMagnitude > 1e-5f ? axis.normalized : Vector3.forward;
        Vector3 normSec = secondaryAxis.sqrMagnitude > 1e-5f ? secondaryAxis.normalized : Vector3.right;
        bool hasPosSway = positionSway.sqrMagnitude > 1e-5f;

        int count = (targets != null && targets.Length > 0) ? targets.Length : 1;
        float w1 = frequency * 6.2831853f;
        float w2 = frequency * 7.91f; // Incommensurate frequency for natural secondary motion
        float wPos = positionFrequency * 6.2831853f;

        for (int i = 0; i < count; i++)
        {
            Transform t = (targets != null && targets.Length > 0) ? targets[i] : transform;
            if (t == null) continue;

            float ph = _targetPhases[i];
            float a1, a2;

            if (mode == SwayMode.Pendulum)
            {
                a1 = Mathf.Sin(_time * w1 + ph) * maxAngle;
                a2 = Mathf.Sin(_time * w2 + ph * 1.37f) * secondaryAngle;
            }
            else // Noise mode: multi-frequency sinusoidal superposition
            {
                float n1 = Mathf.Sin(_time * w1 + ph);
                float n2 = Mathf.Sin(_time * w1 * 1.618f + ph * 2.1f) * 0.5f;
                float n3 = Mathf.Sin(_time * w1 * 2.718f + ph * 3.7f) * 0.25f;
                a1 = ((n1 + n2 + n3) / 1.75f) * maxAngle;

                float s1 = Mathf.Sin(_time * w2 + ph * 1.4f);
                float s2 = Mathf.Sin(_time * w2 * 1.414f + ph * 2.9f) * 0.4f;
                a2 = ((s1 + s2) / 1.4f) * secondaryAngle;
            }

            Quaternion swayRot = Quaternion.AngleAxis(a1, normAxis) * Quaternion.AngleAxis(a2, normSec);
            t.localRotation = _baseRotations[i] * swayRot;

            if (hasPosSway)
            {
                float px = Mathf.Sin(_time * wPos + ph) * positionSway.x;
                float py = Mathf.Sin(_time * wPos * 1.33f + ph * 1.5f) * positionSway.y;
                float pz = Mathf.Cos(_time * wPos * 0.87f + ph * 0.8f) * positionSway.z;
                t.localPosition = _basePositions[i] + new Vector3(px, py, pz);
            }
        }
    }

    public void ForceCullCheck(Vector3 referencePos)
    {
        _isCulled = (transform.position - referencePos).sqrMagnitude > (cullDistance * cullDistance);
    }

    public void SetCulled(bool culled)
    {
        _isCulled = culled;
    }
}

using System;
using UnityEngine;

/// <summary>
/// AmbientRotor: spins objects like lift bullwheels, windmills, ventilation fans, or helicopter blades.
/// Zero physics, zero per-frame allocations, and ZERO work beyond cullDistance (~1.5 km).
/// Deterministic from seed.
/// Can drive multiple target transforms from one component or its own transform if targets is empty.
/// </summary>
public sealed class AmbientRotor : MonoBehaviour
{
    [Header("Targets & Axis")]
    [Tooltip("Transforms to rotate. If empty, rotates this GameObject's transform.")]
    public Transform[] targets = new Transform[0];
    [Tooltip("Axis of rotation in local space.")]
    public Vector3 axis = Vector3.forward;

    [Header("Speed & Dynamics")]
    [Tooltip("Rotation speed in degrees per second.")]
    public float degreesPerSecond = 180f;
    [Tooltip("Smooth acceleration in degrees/s^2. Set to 0 for instant constant speed.")]
    public float accel = 0f;
    [Tooltip("Optional wobble/precession amplitude in degrees.")]
    public float wobbleDegrees = 0f;
    [Tooltip("Wobble oscillation frequency in Hz.")]
    public float wobbleFrequency = 1f;

    [Header("Performance & Determinism")]
    public float cullDistance = AmbientCull.DefaultCullDistance;
    public int seed = 42;

    public float rpm
    {
        get => degreesPerSecond / 6f;
        set => degreesPerSecond = value * 6f;
    }

    private Quaternion[] _baseRotations;
    private float _currentSpeed;
    private float _angle;
    private float _wobbleTime;
    private float _cullTimer;
    private bool _isCulled;
    private bool _baked;

    public float CurrentSpeed => _currentSpeed;
    public float CurrentAngle => _angle;
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
    /// Bakes base rotations and preallocates arrays to ensure zero runtime GC allocations.
    /// </summary>
    public void Bake()
    {
        int count = (targets != null && targets.Length > 0) ? targets.Length : 1;
        if (_baseRotations == null || _baseRotations.Length != count)
        {
            _baseRotations = new Quaternion[count];
        }

        if (targets != null && targets.Length > 0)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                    _baseRotations[i] = targets[i].localRotation;
                else
                    _baseRotations[i] = Quaternion.identity;
            }
        }
        else
        {
            _baseRotations[0] = transform.localRotation;
        }

        _currentSpeed = accel <= 0f ? degreesPerSecond : 0f;
        _cullTimer = seed != 0 ? (Mathf.Abs(seed * 19349663) % 100) * 0.0035f : 0f;
        _baked = true;
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    /// <summary>
    /// Explicit step advancing rotor simulation by dt seconds.
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

        if (accel > 0f)
        {
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, degreesPerSecond, accel * dt);
        }
        else
        {
            _currentSpeed = degreesPerSecond;
        }

        _angle = (_angle + _currentSpeed * dt) % 360f;
        if (_angle < 0f) _angle += 360f;

        Vector3 normAxis = axis.sqrMagnitude > 1e-5f ? axis.normalized : Vector3.forward;
        Quaternion spinRot = Quaternion.AngleAxis(_angle, normAxis);

        Quaternion netRot = spinRot;
        if (wobbleDegrees > 0.001f)
        {
            _wobbleTime += dt;
            float wobble = Mathf.Sin(_wobbleTime * wobbleFrequency * 6.2831853f) * wobbleDegrees;
            Vector3 cross = Vector3.Cross(normAxis, Vector3.up);
            if (cross.sqrMagnitude < 0.01f)
                cross = Vector3.Cross(normAxis, Vector3.right);
            if (cross.sqrMagnitude > 1e-4f)
            {
                Quaternion wobbleRot = Quaternion.AngleAxis(wobble, cross.normalized);
                netRot = spinRot * wobbleRot;
            }
        }

        if (targets != null && targets.Length > 0)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].localRotation = _baseRotations[i] * netRot;
                }
            }
        }
        else
        {
            transform.localRotation = _baseRotations[0] * netRot;
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

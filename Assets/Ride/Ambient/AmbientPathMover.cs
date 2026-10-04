using System;
using UnityEngine;

/// <summary>
/// AmbientPathMover: moves a consist of N cars along a baked world-space polyline.
/// Supports Loop mode and Shuttle mode with dwell and smooth acceleration/deceleration.
/// Each car maintains authored spacing, yaw offset, tangent alignment, optional banking and bob.
/// Zero physics, zero per-frame allocations, and ZERO work beyond cullDistance (~1.5 km).
/// Deterministic from seed.
/// </summary>
public sealed class AmbientPathMover : MonoBehaviour
{
    public enum PathMode
    {
        Shuttle = 0,
        Loop = 1
    }

    [Header("Mode & Route")]
    public PathMode mode = PathMode.Shuttle;
    public Vector3[] path = new Vector3[0];

    [Header("Consist")]
    public Transform[] cars = new Transform[0];
    /// <summary>Distance of each car's centre BEHIND the consist head, in metres.</summary>
    public float[] carOffsets = new float[0];
    /// <summary>Extra yaw per car in degrees (e.g. 180 for a reversed tail car).</summary>
    public float[] carYaw = new float[0];

    [Header("Motion")]
    public float speed = 12f;
    /// <summary>Smooth acceleration / braking in m/s^2.</summary>
    public float accel = 4f;
    /// <summary>Dwell duration at each terminus in Shuttle mode (seconds).</summary>
    public float dwellSeconds = 5f;
    public float startDistance = 0f;

    [Header("Dynamics")]
    /// <summary>Bank roll angle factor into turns (degrees per turn curvature).</summary>
    public float bankFactor = 0f;
    /// <summary>Vertical heave bob amplitude in metres.</summary>
    public float bobHeave = 0f;
    /// <summary>Pitch/roll bob tilt amplitude in degrees.</summary>
    public float bobTilt = 0f;
    /// <summary>Bob oscillation frequency in Hz.</summary>
    public float bobFrequency = 0.5f;

    [Header("Performance & Determinism")]
    public float cullDistance = AmbientCull.DefaultCullDistance;
    public int seed = 42;

    private float[] _cum;
    private float _totalLength;
    private float _closingLength;
    private float _s;
    private float _dir = 1f;
    private float _currentSpeed;
    private float _dwell;
    private int _seg;
    private float _bobTime;
    private float _seedPhase;

    private float _cullTimer;
    private bool _isCulled;

    public float CurrentSpeed => _currentSpeed;
    public float CurrentHead => _s;
    public float TotalLength => _totalLength;
    public float DwellRemaining => _dwell;
    public float Direction => _dir;
    public bool IsCulled => _isCulled;

    private void Awake()
    {
        InitializeSeed();
        Bake();
        _s = startDistance;
        _currentSpeed = speed > 0f ? speed : 0f;
    }

    private void OnEnable()
    {
        InitializeSeed();
        Bake();
        _s = startDistance;
        Place();
    }

    private void InitializeSeed()
    {
        _seedPhase = seed != 0 ? (Mathf.Abs(seed * 73856093) % 1000) * 0.00628f : 0f;
        _cullTimer = seed != 0 ? (Mathf.Abs(seed * 19349663) % 100) * 0.0035f : 0f;
    }

    /// <summary>
    /// Bakes the polyline's cumulative distances. Safe to call in editor or runtime.
    /// </summary>
    public void Bake()
    {
        if (path == null || path.Length < 2)
        {
            _cum = null;
            _totalLength = 0f;
            _closingLength = 0f;
            return;
        }

        _cum = new float[path.Length];
        _cum[0] = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            _cum[i] = _cum[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        }

        float endToStart = Vector3.Distance(path[path.Length - 1], path[0]);
        if (mode == PathMode.Loop && endToStart > 0.01f)
        {
            _closingLength = endToStart;
            _totalLength = _cum[path.Length - 1] + _closingLength;
        }
        else
        {
            _closingLength = 0f;
            _totalLength = _cum[path.Length - 1];
        }

        if (_seg >= path.Length - 1) _seg = 0;

        if (_s == 0f && startDistance > 0f)
            _s = startDistance;
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    /// <summary>
    /// Explicit step advancing simulation by dt seconds. Useful for tests and deterministic stepping.
    /// </summary>
    public void Step(float dt)
    {
        if (_cum == null) Bake();
        if (_cum == null || _totalLength <= 0.01f) return;

        // Throttled culling check (~3 times/sec)
        _cullTimer -= dt;
        if (_cullTimer <= 0f)
        {
            _cullTimer = 0.35f;
            Vector3 camPos = AmbientCull.GetReferencePosition(out bool hasRef);
            if (hasRef)
            {
                Vector3 checkPos = transform.position;
                if (cars != null && cars.Length > 0 && cars[0] != null)
                    checkPos = cars[0].position;
                _isCulled = (checkPos - camPos).sqrMagnitude > (cullDistance * cullDistance);
            }
            else
            {
                _isCulled = false;
            }
        }

        if (_isCulled) return;

        _bobTime += dt;

        float effectiveAccel = Mathf.Max(accel, 0.5f);

        if (mode == PathMode.Loop)
        {
            // Smoothly reach cruise speed
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, speed, effectiveAccel * dt);
            _s = (_s + _currentSpeed * dt) % _totalLength;
            if (_s < 0f) _s += _totalLength;
        }
        else
        {
            // Shuttle mode with dwell and stopping deceleration
            float maxOffset = 0f;
            if (carOffsets != null)
            {
                for (int i = 0; i < carOffsets.Length; i++)
                    if (carOffsets[i] > maxOffset) maxOffset = carOffsets[i];
            }

            float lo = maxOffset + 1f;
            float hi = _totalLength - 1f;
            if (lo >= hi)
            {
                lo = 0f;
                hi = _totalLength;
            }

            if (_dwell > 0f)
            {
                _dwell -= dt;
                _currentSpeed = 0f;
                Place();
                return;
            }

            float targetSpeed = speed;
            if (_dir > 0f)
            {
                float distToEnd = Mathf.Max(0f, hi - _s);
                float stopDist = (_currentSpeed * _currentSpeed) / (2f * effectiveAccel);
                if (distToEnd <= stopDist)
                {
                    targetSpeed = Mathf.Sqrt(Mathf.Max(0f, 2f * effectiveAccel * distToEnd));
                }

                _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, effectiveAccel * dt);
                _s += _currentSpeed * dt;

                if (_s >= hi)
                {
                    _s = hi;
                    _currentSpeed = 0f;
                    _dir = -1f;
                    _dwell = dwellSeconds;
                }
            }
            else
            {
                float distToEnd = Mathf.Max(0f, _s - lo);
                float stopDist = (_currentSpeed * _currentSpeed) / (2f * effectiveAccel);
                if (distToEnd <= stopDist)
                {
                    targetSpeed = Mathf.Sqrt(Mathf.Max(0f, 2f * effectiveAccel * distToEnd));
                }

                _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, effectiveAccel * dt);
                _s -= _currentSpeed * dt;

                if (_s <= lo)
                {
                    _s = lo;
                    _currentSpeed = 0f;
                    _dir = 1f;
                    _dwell = dwellSeconds;
                }
            }
        }

        Place();
    }

    /// <summary>
    /// Positions and rotates each car at current head position. Also usable at edit/staging time.
    /// </summary>
    public void Place()
    {
        if (_cum == null) Bake();
        if (_cum == null || cars == null) return;

        bool hasBob = bobHeave > 0.0001f || bobTilt > 0.0001f;
        bool hasBank = bankFactor > 0.0001f;

        for (int c = 0; c < cars.Length; c++)
        {
            if (cars[c] == null) continue;

            float offset = (carOffsets != null && c < carOffsets.Length) ? carOffsets[c] : 0f;
            float carS = _s - offset;

            if (mode == PathMode.Loop)
            {
                carS = (carS % _totalLength + _totalLength) % _totalLength;
            }
            else
            {
                carS = Mathf.Clamp(carS, 0f, _totalLength);
            }

            Vector3 pos = Sample(carS);
            Vector3 fwd = GetTangent(carS);

            float rollBank = 0f;
            if (hasBank)
            {
                // Measure horizontal turn curvature between forward and backward probe samples
                Vector3 pA = Sample(carS - 1.5f);
                Vector3 pB = Sample(carS + 1.5f);
                Vector3 vA = (pos - pA);
                Vector3 vB = (pB - pos);
                vA.y = 0f;
                vB.y = 0f;
                if (vA.sqrMagnitude > 1e-4f && vB.sqrMagnitude > 1e-4f)
                {
                    float turnAngle = Vector3.SignedAngle(vA.normalized, vB.normalized, Vector3.up);
                    rollBank = Mathf.Clamp(-turnAngle * bankFactor * _dir, -30f, 30f);
                }
            }

            float heave = 0f;
            float pitchBob = 0f;
            float rollBob = 0f;
            if (hasBob)
            {
                float ph = c * 0.85f + _seedPhase;
                heave = Mathf.Sin(_bobTime * bobFrequency * 6.2831853f + ph) * bobHeave;
                pitchBob = Mathf.Sin(_bobTime * bobFrequency * 5.34f + ph) * bobTilt;
                rollBob = Mathf.Cos(_bobTime * bobFrequency * 6.91f + ph * 1.3f) * bobTilt;
            }

            float yawOffset = (carYaw != null && c < carYaw.Length) ? carYaw[c] : 0f;

            Quaternion baseRot = Quaternion.LookRotation(fwd, Vector3.up);
            Quaternion finalRot = baseRot * Quaternion.Euler(pitchBob, yawOffset, rollBank + rollBob);
            Vector3 finalPos = pos + new Vector3(0f, heave, 0f);

            cars[c].SetPositionAndRotation(finalPos, finalRot);
        }
    }

    public void SetHead(float s)
    {
        _s = s;
        Place();
    }

    public void ForceCullCheck(Vector3 referencePos)
    {
        Vector3 checkPos = transform.position;
        if (cars != null && cars.Length > 0 && cars[0] != null)
            checkPos = cars[0].position;
        _isCulled = (checkPos - referencePos).sqrMagnitude > (cullDistance * cullDistance);
    }

    public void SetCulled(bool culled)
    {
        _isCulled = culled;
    }

    public Vector3 Sample(float s)
    {
        if (_cum == null) Bake();
        if (_cum == null) return Vector3.zero;

        if (mode == PathMode.Loop)
        {
            s = (s % _totalLength + _totalLength) % _totalLength;
            if (_closingLength > 0f && s >= _cum[_cum.Length - 1])
            {
                float tClose = (s - _cum[_cum.Length - 1]) / _closingLength;
                return Vector3.Lerp(path[path.Length - 1], path[0], tClose);
            }
        }
        else
        {
            s = Mathf.Clamp(s, 0f, _totalLength);
        }

        if (_seg >= _cum.Length - 1) _seg = _cum.Length - 2;
        while (_seg > 0 && _cum[_seg] > s) _seg--;
        while (_seg < _cum.Length - 2 && _cum[_seg + 1] < s) _seg++;

        float segLen = _cum[_seg + 1] - _cum[_seg];
        float t = segLen > 1e-4f ? (s - _cum[_seg]) / segLen : 0f;
        return Vector3.Lerp(path[_seg], path[_seg + 1], t);
    }

    public Vector3 GetTangent(float s)
    {
        Vector3 p0 = Sample(s - 1.5f);
        Vector3 p1 = Sample(s + 1.5f);
        Vector3 diff = p1 - p0;
        if (diff.sqrMagnitude < 1e-6f) return Vector3.forward;
        return diff.normalized;
    }
}

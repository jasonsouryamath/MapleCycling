using System;
using UnityEngine;

/// <summary>
/// AmbientFlock: drives a flock of birds circling or drifting inside a volume.
/// Cheap: one component drives many member transforms (no per-bird MonoBehaviours).
/// Zero physics, zero per-frame allocations, and ZERO work beyond cullDistance (~1.5 km).
/// Deterministic from seed.
/// </summary>
public sealed class AmbientFlock : MonoBehaviour
{
    public enum FlockMode
    {
        Circling = 0,
        Drifting = 1
    }

    [Header("Mode & Flock")]
    public FlockMode mode = FlockMode.Circling;
    public Transform[] members = new Transform[0];

    [Header("Flight Dynamics")]
    public float speed = 7.5f;
    public float radius = 25f;
    public float radiusVariance = 8f;
    public float heightVariance = 4f;
    public Vector3 volumeSize = new Vector3(60f, 20f, 60f);

    [Header("Flutter / Bob")]
    public float flapFrequency = 2.8f;
    public float flapBobHeave = 0.15f;
    public float flapBobTilt = 3.5f;

    [Header("Performance & Determinism")]
    public float cullDistance = AmbientCull.DefaultCullDistance;
    public int seed = 42;

    private float[] _radii;
    private float[] _orbitSpeeds;
    private float[] _orbitPhases;
    private float[] _heightOffsets;
    private float[] _flapPhases;

    // Drifting parameters
    private float[] _driftOmegaX;
    private float[] _driftOmegaY;
    private float[] _driftOmegaZ;
    private float[] _driftPhaseX;
    private float[] _driftPhaseY;
    private float[] _driftPhaseZ;

    private float _time;
    private float _cullTimer;
    private bool _isCulled;

    public bool IsCulled => _isCulled;
    public int MemberCount => members != null ? members.Length : 0;

    private void Awake()
    {
        Bake();
    }

    private void OnEnable()
    {
        Bake();
        Step(0f);
    }

    /// <summary>
    /// Bakes deterministic member properties from seed. Preallocates arrays to ensure zero runtime GC.
    /// </summary>
    public void Bake()
    {
        if (members == null || members.Length == 0) return;
        int n = members.Length;

        _radii = new float[n];
        _orbitSpeeds = new float[n];
        _orbitPhases = new float[n];
        _heightOffsets = new float[n];
        _flapPhases = new float[n];

        _driftOmegaX = new float[n];
        _driftOmegaY = new float[n];
        _driftOmegaZ = new float[n];
        _driftPhaseX = new float[n];
        _driftPhaseY = new float[n];
        _driftPhaseZ = new float[n];

        int s = seed;
        for (int i = 0; i < n; i++)
        {
            s = unchecked(s * 1664525 + 1013904223);
            float r0 = (s & 0xFFFF) / 65535f;
            s = unchecked(s * 1664525 + 1013904223);
            float r1 = (s & 0xFFFF) / 65535f;
            s = unchecked(s * 1664525 + 1013904223);
            float r2 = (s & 0xFFFF) / 65535f;
            s = unchecked(s * 1664525 + 1013904223);
            float r3 = (s & 0xFFFF) / 65535f;

            float r = Mathf.Max(3f, radius + (r0 - 0.5f) * 2f * radiusVariance);
            _radii[i] = r;
            _orbitSpeeds[i] = speed / r * (0.85f + r1 * 0.3f);
            _orbitPhases[i] = r2 * 6.2831853f;
            _heightOffsets[i] = (r3 - 0.5f) * heightVariance;
            _flapPhases[i] = (r0 + r2) * 6.2831853f;

            // Drift frequencies in radians/second
            float baseW = speed / Mathf.Max(volumeSize.x, 10f);
            _driftOmegaX[i] = baseW * (0.7f + r0 * 0.6f);
            _driftOmegaY[i] = baseW * 0.5f * (0.6f + r1 * 0.8f);
            _driftOmegaZ[i] = baseW * (0.8f + r2 * 0.5f);

            _driftPhaseX[i] = r0 * 6.2831853f;
            _driftPhaseY[i] = r1 * 6.2831853f;
            _driftPhaseZ[i] = r2 * 6.2831853f;
        }

        _cullTimer = seed != 0 ? (Mathf.Abs(seed * 19349663) % 100) * 0.0035f : 0f;
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    public void Step(float dt)
    {
        if (members == null || members.Length == 0) return;
        if (_radii == null || _radii.Length != members.Length) Bake();

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
        Vector3 center = transform.position;

        if (mode == FlockMode.Circling)
        {
            for (int i = 0; i < members.Length; i++)
            {
                Transform m = members[i];
                if (m == null) continue;

                float theta = _time * _orbitSpeeds[i] + _orbitPhases[i];
                float r = _radii[i];

                float cosT = Mathf.Cos(theta);
                float sinT = Mathf.Sin(theta);

                // Subtle vertical wave
                float waveY = Mathf.Sin(theta * 0.5f + _orbitPhases[i]) * 1.2f;
                float flapCycle = _time * flapFrequency * 6.2831853f + _flapPhases[i];
                float heave = Mathf.Sin(flapCycle) * flapBobHeave;
                float pitch = Mathf.Cos(flapCycle) * flapBobTilt;

                Vector3 pos = center + new Vector3(r * cosT, _heightOffsets[i] + waveY + heave, r * sinT);

                // Forward tangent
                Vector3 fwd = new Vector3(-sinT, 0f, cosT);

                // Bank into circle: roll angle proportional to speed^2 / (g * r)
                float bankRoll = -Mathf.Clamp(Mathf.Atan2(speed * speed, 9.81f * r) * Mathf.Rad2Deg, 0f, 40f);

                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(pitch, 0f, bankRoll);
                m.SetPositionAndRotation(pos, rot);
            }
        }
        else // Drifting
        {
            float hx = volumeSize.x * 0.5f;
            float hy = volumeSize.y * 0.5f;
            float hz = volumeSize.z * 0.5f;

            for (int i = 0; i < members.Length; i++)
            {
                Transform m = members[i];
                if (m == null) continue;

                float wx = _driftOmegaX[i], wy = _driftOmegaY[i], wz = _driftOmegaZ[i];
                float px = _driftPhaseX[i], py = _driftPhaseY[i], pz = _driftPhaseZ[i];

                float sx = Mathf.Sin(wx * _time + px);
                float sy = Mathf.Sin(wy * _time + py);
                float sz = Mathf.Sin(wz * _time + pz);

                float flapCycle = _time * flapFrequency * 6.2831853f + _flapPhases[i];
                float heave = Mathf.Sin(flapCycle) * flapBobHeave;
                float pitchBob = Mathf.Cos(flapCycle) * flapBobTilt;

                Vector3 pos = center + new Vector3(hx * sx, hy * sy + _heightOffsets[i] + heave, hz * sz);

                // Velocity vector derivative
                float vx = hx * wx * Mathf.Cos(wx * _time + px);
                float vy = hy * wy * Mathf.Cos(wy * _time + py);
                float vz = hz * wz * Mathf.Cos(wz * _time + pz);

                Vector3 vel = new Vector3(vx, vy, vz);
                if (vel.sqrMagnitude < 1e-4f) vel = Vector3.forward;

                Vector3 fwd = vel.normalized;

                // Banking into lateral acceleration
                float lateralAccel = -hx * wx * wx * sx;
                float bankRoll = Mathf.Clamp(lateralAccel * 1.5f, -30f, 30f);

                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(pitchBob, 0f, bankRoll);
                m.SetPositionAndRotation(pos, rot);
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

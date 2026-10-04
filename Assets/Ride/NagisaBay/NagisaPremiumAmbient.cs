using UnityEngine;

/// <summary>
/// Lightweight ambient driver for the Nagisa premium-visuals pass. One component owns a
/// whole authored cluster, so palms, flags, banners and boats animate together and pay one
/// distance test. Renderers are disabled outside the activation radius; no GameObjects are
/// spawned or destroyed at runtime.
/// </summary>
[DefaultExecutionOrder(180)]
public sealed class NagisaPremiumAmbient : MonoBehaviour
{
    public Transform[] swayTargets = new Transform[0];
    public Transform[] bobTargets = new Transform[0];
    public Transform[] driftTargets = new Transform[0];
    public Renderer[] renderers = new Renderer[0];
    public float activationDistance = 650f;
    public float swayDegrees = 4f;
    public float bobHeight = 0.08f;
    public float driftDistance = 0.35f;
    public float speed = 1f;

    private Vector3[] _bobBase;
    private Vector3[] _driftBase;
    private Quaternion[] _swayBase;
    private float _phase;
    private float _check;
    private bool _active = true;

    private void Awake()
    {
        _phase = (GetInstanceID() & 1023) * 0.031f;
        _bobBase = CapturePositions(bobTargets);
        _driftBase = CapturePositions(driftTargets);
        _swayBase = CaptureRotations(swayTargets);
        RefreshRenderers(true);
    }

    private void OnDisable() { SetRenderers(false); }

    private void Update()
    {
        _check -= Time.deltaTime;
        if (_check <= 0f)
        {
            _check = 0.25f;
            Vector3 refPos = AmbientCull.GetReferencePosition(out bool has);
            bool next = !has || (transform.position - refPos).sqrMagnitude <= activationDistance * activationDistance;
            if (next != _active) RefreshRenderers(next);
        }
        if (!_active) return;

        float t = Time.time * speed + _phase;
        for (int i = 0; i < swayTargets.Length; i++)
        {
            Transform target = swayTargets[i];
            if (target == null) continue;
            float phase = _phase + i * 0.71f;
            target.localRotation = _swayBase[i] * Quaternion.Euler(0f, 0f, Mathf.Sin(t + phase) * swayDegrees);
        }
        for (int i = 0; i < bobTargets.Length; i++)
        {
            Transform target = bobTargets[i];
            if (target == null) continue;
            target.localPosition = _bobBase[i] + Vector3.up * (Mathf.Sin(t * 1.17f + i * 0.9f) * bobHeight);
        }
        for (int i = 0; i < driftTargets.Length; i++)
        {
            Transform target = driftTargets[i];
            if (target == null) continue;
            float wave = Mathf.Sin(t * 0.72f + i * 1.3f);
            Vector3 p = target.localPosition;
            p.x = _driftBase[i].x + wave * driftDistance;
            p.z = _driftBase[i].z;
            target.localPosition = p;
        }
    }

    private void RefreshRenderers(bool active)
    {
        _active = active;
        SetRenderers(active);
    }

    private void SetRenderers(bool enabled)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = enabled;
    }

    private static Vector3[] CapturePositions(Transform[] targets)
    {
        var result = new Vector3[targets == null ? 0 : targets.Length];
        for (int i = 0; i < result.Length; i++) result[i] = targets[i] == null ? Vector3.zero : targets[i].localPosition;
        return result;
    }

    private static Quaternion[] CaptureRotations(Transform[] targets)
    {
        var result = new Quaternion[targets == null ? 0 : targets.Length];
        for (int i = 0; i < result.Length; i++) result[i] = targets[i] == null ? Quaternion.identity : targets[i].localRotation;
        return result;
    }
}

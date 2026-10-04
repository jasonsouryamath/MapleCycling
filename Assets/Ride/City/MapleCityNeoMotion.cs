using UnityEngine;

/// <summary>
/// Moving parts of Maple City's vaporwave neo-Tokyo layer. Built by MapleCityEnvironment.NeoTokyo (editor) and driven here:
/// a maglev train that loops the elevated viaduct, and airships that drift in slow orbits over the city. Both are pure
/// functions of Time.time (no accumulated state to drift) and live under the city environment root, so the region gating
/// that hides the city when the rider leaves also hides them.
/// </summary>
public sealed class MapleCityMaglevTrain : MonoBehaviour
{
    [Tooltip("Rail path (world space) and cumulative arc length along it; the loop is closed.")]
    public Vector3[] path = new Vector3[0];
    public float[] cumulative = new float[0];
    public Transform[] cars = new Transform[0];
    public float carSpacing = 10.2f;
    public float speed = 26f;
    public float startOffset;

    void Update() { Apply(Time.time); }

    /// <summary>Pure function of time; the editor builder calls Apply(0) so the saved scene has the cars on the rail.</summary>
    public void Apply(float time)
    {
        if (path == null || path.Length < 2 || cumulative == null || cumulative.Length != path.Length || cars == null) return;
        float length = cumulative[cumulative.Length - 1];
        if (length < 10f) return;
        float head = startOffset + time * speed;
        for (int k = 0; k < cars.Length; k++)
        {
            if (cars[k] == null) continue;
            Sample(Mathf.Repeat(head - k * carSpacing, length), out var p, out var dir);
            if (dir.sqrMagnitude > 1e-6f) cars[k].SetPositionAndRotation(p, Quaternion.LookRotation(dir, Vector3.up));
        }
    }

    void Sample(float d, out Vector3 p, out Vector3 dir)
    {
        int lo = 0, hi = cumulative.Length - 1;
        while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (cumulative[mid] <= d) lo = mid; else hi = mid; }
        float span = Mathf.Max(0.001f, cumulative[hi] - cumulative[lo]);
        float t = Mathf.Clamp01((d - cumulative[lo]) / span);
        p = Vector3.Lerp(path[lo], path[hi], t);
        dir = path[hi] - path[lo];
    }
}

public sealed class MapleCityAirship : MonoBehaviour
{
    public Vector3 centre;
    public float radius = 900f;
    public float height = 170f;
    [Tooltip("Ground speed, m/s. Negative = clockwise.")]
    public float speed = 7f;
    public float phase;

    void Update() { Apply(Time.time); }

    public void Apply(float time)
    {
        float a = phase + time * speed / Mathf.Max(50f, radius);
        var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
        p.y = height + Mathf.Sin(time * 0.23f + phase * 3f) * 4f;
        var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(speed == 0f ? 1f : speed);
        transform.SetPositionAndRotation(p, Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 0.31f + phase) * 2.5f));
    }
}

/// <summary>A huge pale moon disc that rides with the camera (so it never parallaxes against a 5 km city): fixed direction, fixed distance.</summary>
public sealed class MapleCityMoon : MonoBehaviour
{
    public Vector3 direction = new Vector3(0.55f, 0.42f, 0.72f);
    public float distance = 3200f;

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.position = cam.transform.position + direction.normalized * distance;
    }
}

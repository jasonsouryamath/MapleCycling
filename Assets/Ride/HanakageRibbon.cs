using UnityEngine;

/// <summary>
/// Secondary motion for Hanakage's hair ribbons (handoff section 19, P1).
///
/// The ribbons are a chain of parented segment objects built by
/// `assets/3d/kuro/build_hanakage_look.py` and staged under the rig's `Head` bone. They are
/// deliberately NOT skinned: a skinned ribbon is rigid, and two rigid ribbons on a rider
/// climbing at 11 kph read as antennae. This component swings them instead.
///
/// The model is the cheapest thing that is still honest: each segment chases the direction its
/// parent is pointing, with a lag, plus a steady pull from the airflow. So the streamers trail
/// backwards at speed, hang when she is slow, and whip when she looks back - all of which fall
/// out of the motion she is already doing rather than from an animation that has to be cued.
///
/// Everything here is a named, serialized tunable. None of it is gameplay: if a value is wrong
/// the ribbons look wrong, and nothing else in the encounter changes.
/// </summary>
[DefaultExecutionOrder(120)]
public class HanakageRibbon : MonoBehaviour
{
    public const string RootName = "HanakageRibbonRoot";

    [Header("Wiring")]
    [Tooltip("Optional. When set, ribbon trail scales with her road speed; otherwise the " +
             "component falls back to the rig root's own movement.")]
    public HanakageRider rider;

    [Header("Airflow (provisional)")]
    [Tooltip("Degrees the chain is pulled backwards at the reference speed below.")]
    public float trailDegrees = 26f;
    [Tooltip("Road speed, m/s, at which trailDegrees is reached. ~11 kph - her climbing speed.")]
    public float referenceSpeedMps = 3.1f;
    [Tooltip("Hard cap so a descent cannot fold the ribbons through her back.")]
    public float maxTrailDegrees = 42f;

    [Header("Sway (provisional)")]
    [Tooltip("Amplitude of the idle flutter, degrees.")]
    public float swayDegrees = 5.5f;
    [Tooltip("Flutter cycles per second at the reference speed.")]
    public float swayHz = 1.35f;
    [Tooltip("Phase offset added per segment, so the wave travels down the ribbon.")]
    public float segmentPhase = 0.7f;

    [Header("Lag (provisional)")]
    [Tooltip("How fast a segment catches up to its target. Higher = stiffer ribbon.")]
    public float responsiveness = 9f;

    [Header("Gust (driven by HanakagePerformance)")]
    [Tooltip("0..1. Transient boost applied when she attacks or turns her head - the moment " +
             "the design wants the streamers to sweep across her shoulder. Written every " +
             "frame by the behaviour driver; 0 means 'behave normally'.")]
    [Range(0f, 1f)] public float gust = 0f;
    [Tooltip("Extra trail degrees at gust = 1.")]
    public float gustTrailDegrees = 16f;
    [Tooltip("Extra flutter amplitude, degrees, at gust = 1.")]
    public float gustSwayDegrees = 7f;

    private Transform[] _segments;
    private Vector3[] _rest;
    private Quaternion[] _restRot;
    private float[] _phase;
    private float _t;

    private void OnEnable() => Rebind();

    /// <summary>
    /// Collects the segment chain. Idempotent and safe to call from a staging pass; matches on
    /// the exact prefix the Blender build writes, never on Contains.
    /// </summary>
    public void Rebind()
    {
        var found = new System.Collections.Generic.List<Transform>();
        Collect(transform, found);
        _segments = found.ToArray();
        _rest = new Vector3[_segments.Length];
        _restRot = new Quaternion[_segments.Length];
        _phase = new float[_segments.Length];
        for (int i = 0; i < _segments.Length; i++)
        {
            _rest[i] = _segments[i].localPosition;
            _restRot[i] = _segments[i].localRotation;
            _phase[i] = i * segmentPhase;
        }
    }

    private static void Collect(Transform t, System.Collections.Generic.List<Transform> into)
    {
        foreach (Transform c in t)
        {
            if (c.name.StartsWith("HanakageRibbon_", System.StringComparison.Ordinal))
                into.Add(c);
            Collect(c, into);
        }
    }

    private void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>dt-driven so an editor capture can pose the ribbons before rendering a frame.</summary>
    public void Tick(float dt)
    {
        if (_segments == null || _segments.Length == 0) return;
        if (dt <= 0f) dt = 1f / 60f;
        _t += dt;

        float speed = rider != null ? Mathf.Abs(rider.speedMps) : referenceSpeedMps;
        float g = Mathf.Clamp01(gust);
        float trail = Mathf.Min(maxTrailDegrees + gustTrailDegrees,
                                trailDegrees * Mathf.Sqrt(Mathf.Max(0f, speed) /
                                                          Mathf.Max(0.01f, referenceSpeedMps))
                                + gustTrailDegrees * g);
        float sway = swayDegrees + gustSwayDegrees * g;
        float k = 1f - Mathf.Exp(-responsiveness * dt);

        for (int i = 0; i < _segments.Length; i++)
        {
            // Segment 0 of each chain carries the whole trail; later segments add a little
            // more, so the ribbon curves instead of hinging at one joint.
            float share = 1f / Mathf.Max(1, _segments.Length) * 2f;
            float flutter = sway * Mathf.Sin((_t * swayHz * (1f + g) * Mathf.PI * 2f) + _phase[i]) *
                            Mathf.Clamp01(speed / Mathf.Max(0.01f, referenceSpeedMps));
            var target = _restRot[i] * Quaternion.Euler(-trail * share, flutter * 0.6f, flutter);
            _segments[i].localRotation = Quaternion.Slerp(_segments[i].localRotation, target, k);
            _segments[i].localPosition = _rest[i];
        }
    }
}

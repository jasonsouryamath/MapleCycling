using UnityEngine;

/// <summary>
/// NB8B runtime glue (Nagisa Bay attractions): keeps hanging things upright while their parent
/// moves - Ferris-wheel cabin pins orbit with the AmbientRotor-driven rim, gondola grips follow the
/// AmbientPathMover (which pitches cars along the cable). In LateUpdate each target's WORLD
/// rotation is reset to "gravity down" with a yaw taken from <see cref="yawReference"/> (or the
/// target's parent heading, flattened). Child meshes can still swing via AmbientSway.
/// Zero allocations; skips work beyond cullDistance like the W0 kit (AmbientCull reference).
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class NagisaAttractionUpright : MonoBehaviour
{
    public Transform[] targets = new Transform[0];
    [Tooltip("Optional: every target takes this transform's flattened heading. Empty = each target's parent heading.")]
    public Transform yawReference;
    public float cullDistance = AmbientCull.DefaultCullDistance;

    private float _cullTimer;
    private bool _culled;

    private void LateUpdate()
    {
        _cullTimer -= Time.deltaTime;
        if (_cullTimer <= 0f)
        {
            _cullTimer = 0.35f;
            var cam = AmbientCull.GetReferencePosition(out bool has);
            _culled = has && (transform.position - cam).sqrMagnitude > cullDistance * cullDistance;
        }
        if (!_culled) Sync();
    }

    /// <summary>Apply now (editor captures call this after stepping the movers).</summary>
    public void Sync()
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            if (t == null) continue;
            var src = yawReference != null ? yawReference : t.parent;
            Vector3 f = src != null ? src.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            t.rotation = Quaternion.LookRotation(f, Vector3.up);
        }
    }
}

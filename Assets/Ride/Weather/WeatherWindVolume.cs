using UnityEngine;

/// <summary>
/// Local wind modifier (box trigger volume, no physics cost): shelter behind buildings, channels
/// in street canyons, exposure on seawalls and bridges. Multiplier blends in over edgeBlendM.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class WeatherWindVolume : MonoBehaviour
{
    [Tooltip("0 = full shelter, 1 = open, >1 = channelled/exposed (bridge, street canyon).")]
    [Range(0f, 2f)] public float windMultiplier = 1f;
    public float edgeBlendM = 25f;

    private static readonly System.Collections.Generic.List<WeatherWindVolume> All = new();
    private BoxCollider _box;

    private void OnEnable() { _box = GetComponent<BoxCollider>(); _box.isTrigger = true; All.Add(this); }
    private void OnDisable() => All.Remove(this);

    /// <summary>Weighted blend of the volumes containing p (each weight fades in over
    /// edgeBlendM), so overlapping boxes of one zone never compound and zone borders cross-fade.
    /// Outside every volume the exposure is 1 (open).</summary>
    public static float ExposureAt(Vector3 p) => TryExposureAt(p, out float e) ? e : 1f;

    /// <summary>False when no volume contains p (caller may fall back to terrain exposure).</summary>
    public static bool TryExposureAt(Vector3 p, out float exposure)
    {
        float sumW = 0f, sumWM = 0f, maxW = 0f;
        foreach (var v in All)
        {
            var local = v.transform.InverseTransformPoint(p) - v._box.center;
            var half = v._box.size * 0.5f;
            var sc = v.transform.lossyScale;
            float inside = Mathf.Min((half.x - Mathf.Abs(local.x)) * Mathf.Abs(sc.x),
                                     (half.z - Mathf.Abs(local.z)) * Mathf.Abs(sc.z));
            if (inside <= 0f || Mathf.Abs(local.y) > half.y) continue;
            float w = Mathf.Clamp01(inside / Mathf.Max(0.01f, v.edgeBlendM));
            sumW += w; sumWM += w * v.windMultiplier; maxW = Mathf.Max(maxW, w);
        }
        exposure = sumW <= 0f ? 1f : Mathf.Lerp(1f, sumWM / sumW, maxW);
        return sumW > 0f;
    }
}

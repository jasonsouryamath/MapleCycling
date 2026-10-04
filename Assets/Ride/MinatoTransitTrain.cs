using UnityEngine;

/// <summary>
/// Minato Coast "Glass Tide District" elevated monorail: shuttles a short train back and forth
/// along its guideway polyline. Deliberately tiny: one Update per train, a cached segment index,
/// no physics, no allocations. The path is the guideway BEAM-TOP polyline in world space, baked
/// by MinatoRedesign.Skyline.cs at build time; the cars' origins sit on the beam top.
///
/// Car 0 is the lead car (nose on its local +Z) at the high-distance end of the consist, the
/// last car is a lead car turned 180 degrees, so the train reads correctly in both directions and
/// simply reverses at each terminus after a dwell.
/// </summary>
public sealed class MinatoTransitTrain : MonoBehaviour
{
    public Vector3[] path = new Vector3[0];
    public Transform[] cars = new Transform[0];
    /// <summary>Distance of each car's centre BEHIND the consist head, in metres.</summary>
    public float[] carOffsets = new float[0];
    /// <summary>Extra yaw per car (180 for the reversed tail car).</summary>
    public float[] carYaw = new float[0];
    public float speed = 12f;
    public float dwellSeconds = 7f;
    public float startDistance;

    private float[] _cum;
    private float _s;
    private float _dir = 1f;
    private float _dwell;
    private int _seg;

    private void OnEnable()
    {
        Bake();
        _s = startDistance;
        Place();
    }

    private void Bake()
    {
        if (path == null || path.Length < 2) { _cum = null; return; }
        _cum = new float[path.Length];
        for (int i = 1; i < path.Length; i++)
            _cum[i] = _cum[i - 1] + Vector3.Distance(path[i - 1], path[i]);
    }

    private float Length => _cum == null ? 0f : _cum[_cum.Length - 1];

    private void Update()
    {
        if (_cum == null) return;
        float head = carOffsets.Length > 0 ? carOffsets[carOffsets.Length - 1] : 0f;
        float lo = head + 2f, hi = Length - 2f;
        if (_dwell > 0f)
        {
            _dwell -= Time.deltaTime;
            return;
        }
        _s += _dir * speed * Time.deltaTime;
        if (_s > hi) { _s = hi; _dir = -1f; _dwell = dwellSeconds; }
        else if (_s < lo) { _s = lo; _dir = 1f; _dwell = dwellSeconds; }
        Place();
    }

    /// <summary>Position every car for the current head distance (also used at build time).</summary>
    public void Place()
    {
        if (_cum == null) Bake();
        if (_cum == null) return;
        for (int c = 0; c < cars.Length; c++)
        {
            if (cars[c] == null) continue;
            float s = _s - (c < carOffsets.Length ? carOffsets[c] : 0f);
            Vector3 p = Sample(s);
            Vector3 f = Sample(s + 3f) - Sample(s - 3f);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            float yaw = c < carYaw.Length ? carYaw[c] : 0f;
            cars[c].SetPositionAndRotation(p, Quaternion.LookRotation(f.normalized, Vector3.up) *
                                              Quaternion.Euler(0f, yaw, 0f));
        }
    }

    public void SetHead(float s)
    {
        _s = s;
        Place();
    }

    private Vector3 Sample(float s)
    {
        s = Mathf.Clamp(s, 0f, Length);
        if (_seg >= _cum.Length - 1) _seg = _cum.Length - 2;
        while (_seg > 0 && _cum[_seg] > s) _seg--;
        while (_seg < _cum.Length - 2 && _cum[_seg + 1] < s) _seg++;
        float len = _cum[_seg + 1] - _cum[_seg];
        float t = len > 1e-4f ? (s - _cum[_seg]) / len : 0f;
        return Vector3.Lerp(path[_seg], path[_seg + 1], t);
    }
}

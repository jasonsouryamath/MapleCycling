using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Run-time "where is this along the Nagisa road" lookup (2026-10-02). Built from the baked
/// <see cref="RouteGraph"/> "nagisa" segment, with a coarse XZ hash so any world position (a placed tree,
/// a cafe table) can be mapped to a route distance and a signed lateral offset without loading the
/// editor-only NagisaRoute. Lateral is measured on the segment's own side axis (+ = the rider's right).
/// </summary>
public static class NagisaRoadIndex
{
    public const string SegmentId = "nagisa";
    const float Cell = 48f;

    static Vector3[] _pos, _side;
    static float[] _dist;
    static Dictionary<long, List<int>> _grid;
    static bool _tried;

    public static bool Ready => Load();
    public static float Length => Load() ? _dist[_dist.Length - 1] : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { _pos = null; _grid = null; _tried = false; }

    static long K(int x, int z) => ((long)x << 32) ^ (uint)z;

    static bool Load()
    {
        if (_pos != null) return true;
        if (_tried) return false;
        var graph = RouteGraph.Load();
        var seg = graph != null ? graph.Segment(SegmentId) : null;
        if (seg == null || seg.Count < 2) return false;   // retry later (graph may load after the first call)
        _tried = true;
        _pos = seg.position; _side = seg.side; _dist = seg.distance;
        _grid = new Dictionary<long, List<int>>();
        for (int i = 0; i < _pos.Length; i++)
        {
            long k = K(Mathf.FloorToInt(_pos[i].x / Cell), Mathf.FloorToInt(_pos[i].z / Cell));
            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<int>();
            l.Add(i);
        }
        return true;
    }

    /// <summary>Nearest road point within <paramref name="maxM"/> (XZ). False when the road is farther than that.</summary>
    public static bool Project(Vector3 p, out float distanceM, out float lateralM, out int index, float maxM = 160f)
    {
        distanceM = lateralM = 0f; index = -1;
        if (!Load()) return false;
        int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell), n = Mathf.CeilToInt(maxM / Cell);
        float best = maxM * maxM;
        for (int x = cx - n; x <= cx + n; x++)
            for (int z = cz - n; z <= cz + n; z++)
                if (_grid.TryGetValue(K(x, z), out var list))
                    foreach (int i in list)
                    {
                        float dx = _pos[i].x - p.x, dz = _pos[i].z - p.z, d2 = dx * dx + dz * dz;
                        if (d2 < best) { best = d2; index = i; }
                    }
        if (index < 0) return false;
        distanceM = _dist[index];
        var s = new Vector3(_side[index].x, 0f, _side[index].z).normalized;
        lateralM = Vector3.Dot(new Vector3(p.x - _pos[index].x, 0f, p.z - _pos[index].z), s);
        return true;
    }

    public static int IndexAt(float metres)
    {
        if (!Load()) return 0;
        int lo = 0, hi = _dist.Length - 1;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (_dist[mid] < metres) lo = mid + 1; else hi = mid; }
        return lo;
    }

    public static Vector3 PositionAt(float metres) => Load() ? _pos[IndexAt(metres)] : Vector3.zero;

    /// <summary>Horizontal unit side vector (+ = rider's right) at a route distance.</summary>
    public static Vector3 SideAt(float metres)
    {
        if (!Load()) return Vector3.right;
        var s = _side[IndexAt(metres)];
        return new Vector3(s.x, 0f, s.z).normalized;
    }

    /// <summary>Horizontal unit direction of travel at a route distance.</summary>
    public static Vector3 ForwardAt(float metres)
    {
        if (!Load()) return Vector3.forward;
        int i = IndexAt(metres), j = Mathf.Min(i + 1, _pos.Length - 1);
        var d = _pos[j] - _pos[Mathf.Max(0, j - 1)];
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
    }
}

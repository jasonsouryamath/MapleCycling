using System;
using UnityEngine;

/// <summary>
/// MAPLE CITY LIFE (claude-city, 2026-09-25). The closed pavement walking lanes the city's
/// pedestrians follow. Built by <c>Assets/Editor/MapleCityLife.cs</c>; one component holds every
/// lane so the ~500 walkers share the polylines instead of each serialising its own copy.
///
/// A lane is a CLOSED polyline laid along the pavement at a fixed distance from the kerb, with
/// the pavement height baked into each point (nothing in Maple City has a collider, so walkers
/// cannot raycast for the ground and must carry it).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapleCityWalkLanes : MonoBehaviour
{
    [Serializable]
    public sealed class Lane
    {
        public Vector3[] points = Array.Empty<Vector3>();
        /// <summary>Cumulative arc length at each point; the last entry closes back to point 0.</summary>
        public float[] dist = Array.Empty<float>();
        public float length;
        /// <summary>
        /// Per point: how far a figure may step off the lane line to its RIGHT (min &lt; 0 = to
        /// its left) while walking with the point order, and still be on clear pavement (kerb,
        /// shopfronts and the occupancy map's clutter excluded). Used by runners to overtake (C8).
        /// Empty on lanes built before C8 (then +-0.4 m is assumed).
        /// </summary>
        public float[] latMin = Array.Empty<float>();
        public float[] latMax = Array.Empty<float>();
    }

    public Lane[] lanes = Array.Empty<Lane>();

    /// <summary>Clear lateral range at arc length <paramref name="s"/>, right-positive for heading +1.</summary>
    public void LateralRange(int lane, float s, out float min, out float max)
    {
        min = -0.4f; max = 0.4f;
        var l = lanes[lane];
        int n = l.points.Length;
        if (l.latMin == null || l.latMin.Length != n || l.latMax == null || l.latMax.Length != n) return;
        s = Mathf.Repeat(s, l.length);
        int lo = 0, hi = n;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (l.dist[mid] <= s) lo = mid; else hi = mid;
        }
        int b = (lo + 1) % n;
        min = Mathf.Max(l.latMin[lo], l.latMin[b]);
        max = Mathf.Min(l.latMax[lo], l.latMax[b]);
    }

    public bool Valid(int lane) =>
        lanes != null && lane >= 0 && lane < lanes.Length && lanes[lane] != null &&
        lanes[lane].points.Length >= 2 && lanes[lane].length > 1f;

    /// <summary>Position and horizontal walking direction at arc length <paramref name="s"/> (wraps).</summary>
    public Vector3 Sample(int lane, float s, out Vector3 direction)
    {
        var l = lanes[lane];
        int n = l.points.Length;
        s = Mathf.Repeat(s, l.length);

        // Binary search the segment: dist[k] <= s < dist[k+1]; dist has n+1 entries (closing span).
        int lo = 0, hi = n;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (l.dist[mid] <= s) lo = mid; else hi = mid;
        }
        var a = l.points[lo];
        var b = l.points[(lo + 1) % n];
        float span = Mathf.Max(1e-4f, l.dist[lo + 1] - l.dist[lo]);
        float u = Mathf.Clamp01((s - l.dist[lo]) / span);

        direction = b - a;
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
        direction.Normalize();
        return Vector3.Lerp(a, b, u);
    }
}

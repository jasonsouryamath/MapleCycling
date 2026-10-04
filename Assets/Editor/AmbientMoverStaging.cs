using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-side staging helpers for the W0 ambient-mover kit.
/// Used by L1-L7 packages to stage trains, flocks, sways, and rotors into scene roots.
/// Guaranteed idempotent: Converge removes existing matching objects so re-running staging
/// never stacks duplicate components or geometry.
/// </summary>
public static class AmbientMoverStaging
{
    /// <summary>
    /// Converges a child GameObject under parent by exact name.
    /// Destroys any existing children with this exact name and creates a clean new GameObject.
    /// Re-running staging will never stack duplicates.
    /// </summary>
    public static GameObject Converge(Transform parent, string exactName)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));
        if (string.IsNullOrEmpty(exactName)) throw new ArgumentException("exactName cannot be null or empty", nameof(exactName));

        // Find and destroy all existing children matching exactName
        var toDestroy = new List<GameObject>();
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == exactName)
            {
                toDestroy.Add(child.gameObject);
            }
        }

        for (int i = 0; i < toDestroy.Count; i++)
        {
            UnityEngine.Object.DestroyImmediate(toDestroy[i]);
        }

        var go = new GameObject(exactName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    /// <summary>
    /// Removes all direct children with the exact name.
    /// </summary>
    public static void DestroyExact(Transform parent, string exactName)
    {
        if (parent == null || string.IsNullOrEmpty(exactName)) return;
        var toDestroy = new List<GameObject>();
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == exactName)
                toDestroy.Add(child.gameObject);
        }
        for (int i = 0; i < toDestroy.Count; i++)
        {
            UnityEngine.Object.DestroyImmediate(toDestroy[i]);
        }
    }

    /// <summary>
    /// Bakes and sanitizes a polyline: filters out duplicate or near-coincident consecutive points.
    /// </summary>
    public static Vector3[] BakePolyline(IList<Vector3> points, float minSpacing = 0.05f)
    {
        if (points == null || points.Count == 0) return Array.Empty<Vector3>();
        var result = new List<Vector3>(points.Count);
        result.Add(points[0]);

        float sqrMin = minSpacing * minSpacing;
        for (int i = 1; i < points.Count; i++)
        {
            if ((points[i] - result[result.Count - 1]).sqrMagnitude >= sqrMin)
            {
                result.Add(points[i]);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Calculates total polyline arc length in metres.
    /// </summary>
    public static float GetPolylineLength(IList<Vector3> points, bool closed = false)
    {
        if (points == null || points.Count < 2) return 0f;
        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            total += Vector3.Distance(points[i - 1], points[i]);
        }
        if (closed && points.Count > 2)
        {
            total += Vector3.Distance(points[points.Count - 1], points[0]);
        }
        return total;
    }

    /// <summary>
    /// Resamples a polyline into equidistant points separated by stepSize metres.
    /// </summary>
    public static Vector3[] ResamplePolyline(IList<Vector3> points, float stepSize, bool closed = false)
    {
        if (points == null || points.Count < 2 || stepSize <= 0.01f)
            return points != null ? BakePolyline(points) : Array.Empty<Vector3>();

        float totalLen = GetPolylineLength(points, closed);
        if (totalLen <= stepSize)
            return BakePolyline(points);

        int sampleCount = Mathf.Max(2, Mathf.RoundToInt(totalLen / stepSize) + 1);
        var resampled = new Vector3[sampleCount];

        float[] cum = new float[points.Count + (closed ? 1 : 0)];
        cum[0] = 0f;
        for (int i = 1; i < points.Count; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        if (closed)
            cum[cum.Length - 1] = cum[cum.Length - 2] + Vector3.Distance(points[points.Count - 1], points[0]);

        float actualStep = totalLen / (sampleCount - (closed ? 0 : 1));
        int seg = 0;

        for (int k = 0; k < sampleCount; k++)
        {
            float targetDist = Mathf.Min(k * actualStep, totalLen);
            while (seg < cum.Length - 2 && cum[seg + 1] < targetDist)
                seg++;

            float segLen = cum[seg + 1] - cum[seg];
            float t = segLen > 1e-4f ? (targetDist - cum[seg]) / segLen : 0f;

            Vector3 p0 = points[seg % points.Count];
            Vector3 p1 = points[(seg + 1) % points.Count];
            resampled[k] = Vector3.Lerp(p0, p1, t);
        }

        return resampled;
    }

    /// <summary>
    /// Smooths a sparse set of control points using Catmull-Rom spline interpolation.
    /// </summary>
    public static Vector3[] BakeCatmullRom(IList<Vector3> controlPoints, int subdivisions = 10, bool closed = false)
    {
        if (controlPoints == null || controlPoints.Count < 2)
            return controlPoints != null ? BakePolyline(controlPoints) : Array.Empty<Vector3>();

        int n = controlPoints.Count;
        int segments = closed ? n : n - 1;
        var result = new List<Vector3>(segments * subdivisions + 1);

        for (int i = 0; i < segments; i++)
        {
            Vector3 p0 = closed ? controlPoints[(i - 1 + n) % n] : controlPoints[Mathf.Max(0, i - 1)];
            Vector3 p1 = controlPoints[i];
            Vector3 p2 = closed ? controlPoints[(i + 1) % n] : controlPoints[Mathf.Min(n - 1, i + 1)];
            Vector3 p3 = closed ? controlPoints[(i + 2) % n] : controlPoints[Mathf.Min(n - 1, i + 2)];

            for (int s = 0; s < subdivisions; s++)
            {
                float t = (float)s / subdivisions;
                result.Add(EvaluateCatmullRom(p0, p1, p2, p3, t));
            }
        }

        if (!closed)
            result.Add(controlPoints[n - 1]);

        return BakePolyline(result);
    }

    private static Vector3 EvaluateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    /// <summary>
    /// Converges a GameObject under parent and sets up an AmbientPathMover.
    /// </summary>
    public static AmbientPathMover StagePathMover(
        Transform parent,
        string exactName,
        Vector3[] path,
        Transform[] cars,
        float[] carOffsets = null,
        float[] carYaw = null,
        float speed = 12f,
        float accel = 4f,
        AmbientPathMover.PathMode mode = AmbientPathMover.PathMode.Shuttle,
        float dwellSeconds = 5f,
        float startDistance = 0f,
        float bankFactor = 0f,
        float bobHeave = 0f,
        float bobTilt = 0f,
        int seed = 42)
    {
        var go = Converge(parent, exactName);
        var mover = go.AddComponent<AmbientPathMover>();
        mover.path = path != null ? BakePolyline(path) : Array.Empty<Vector3>();
        mover.cars = cars ?? Array.Empty<Transform>();
        mover.carOffsets = carOffsets ?? Array.Empty<float>();
        mover.carYaw = carYaw ?? Array.Empty<float>();
        mover.speed = speed;
        mover.accel = accel;
        mover.mode = mode;
        mover.dwellSeconds = dwellSeconds;
        mover.startDistance = startDistance;
        mover.bankFactor = bankFactor;
        mover.bobHeave = bobHeave;
        mover.bobTilt = bobTilt;
        mover.seed = seed;
        mover.Bake();
        if (startDistance > 0f)
            mover.SetHead(startDistance);
        else
            mover.Place();
        return mover;
    }

    /// <summary>
    /// Converges a GameObject under parent and sets up an AmbientFlock.
    /// </summary>
    public static AmbientFlock StageFlock(
        Transform parent,
        string exactName,
        Vector3 center,
        Transform[] members,
        AmbientFlock.FlockMode mode = AmbientFlock.FlockMode.Circling,
        float speed = 7.5f,
        float radius = 25f,
        float radiusVariance = 8f,
        float heightVariance = 4f,
        Vector3 volumeSize = default,
        int seed = 42)
    {
        var go = Converge(parent, exactName);
        go.transform.position = center;
        var flock = go.AddComponent<AmbientFlock>();
        flock.mode = mode;
        flock.members = members ?? Array.Empty<Transform>();
        flock.speed = speed;
        flock.radius = radius;
        flock.radiusVariance = radiusVariance;
        flock.heightVariance = heightVariance;
        flock.volumeSize = volumeSize.sqrMagnitude > 1f ? volumeSize : new Vector3(60f, 20f, 60f);
        flock.seed = seed;
        flock.Bake();
        flock.Step(0f);
        return flock;
    }

    /// <summary>
    /// Converges a GameObject under parent and sets up an AmbientSway.
    /// </summary>
    public static AmbientSway StageSway(
        Transform parent,
        string exactName,
        Transform[] targets,
        Vector3 axis,
        float maxAngle = 8f,
        float frequency = 0.5f,
        AmbientSway.SwayMode mode = AmbientSway.SwayMode.Pendulum,
        Vector3 positionSway = default,
        int seed = 42)
    {
        var go = Converge(parent, exactName);
        var sway = go.AddComponent<AmbientSway>();
        sway.targets = targets ?? Array.Empty<Transform>();
        sway.axis = axis;
        sway.maxAngle = maxAngle;
        sway.frequency = frequency;
        sway.mode = mode;
        sway.positionSway = positionSway;
        sway.seed = seed;
        sway.Bake();
        return sway;
    }

    /// <summary>
    /// Converges a GameObject under parent and sets up an AmbientRotor.
    /// </summary>
    public static AmbientRotor StageRotor(
        Transform parent,
        string exactName,
        Transform[] targets,
        Vector3 axis,
        float degreesPerSecond = 180f,
        float accel = 0f,
        float wobbleDegrees = 0f,
        int seed = 42)
    {
        var go = Converge(parent, exactName);
        var rotor = go.AddComponent<AmbientRotor>();
        rotor.targets = targets ?? Array.Empty<Transform>();
        rotor.axis = axis;
        rotor.degreesPerSecond = degreesPerSecond;
        rotor.accel = accel;
        rotor.wobbleDegrees = wobbleDegrees;
        rotor.seed = seed;
        rotor.Bake();
        return rotor;
    }
}

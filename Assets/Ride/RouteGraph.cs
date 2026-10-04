using System;
using UnityEngine;

/// <summary>
/// One drivable centreline in the Sakura Pass network, baked from
/// <c>tools/blender/sakura_route.py</c> via <c>SakuraRoute.json</c>.
///
/// Unity consumes the baked samples and never re-derives the Catmull-Rom resample or the
/// curvature-driven banking: if the two implementations drifted, the rider would ride a
/// different line from the one the asphalt was swept along.
/// </summary>
[Serializable]
public class RouteSegmentData
{
    public string id = "";
    public string displayName = "";
    public Vector3[] position = Array.Empty<Vector3>();
    public Vector3[] tangent = Array.Empty<Vector3>();
    public Vector3[] side = Array.Empty<Vector3>();     // rider's right = the valley side
    public Vector3[] up = Array.Empty<Vector3>();
    public float[] distance = Array.Empty<float>();     // arc length along this segment
    public float[] bank = Array.Empty<float>();         // roll degrees

    public int Count => position.Length;
    public float Length => distance.Length == 0 ? 0f : distance[distance.Length - 1];

    /// <summary>Sample index at or just before <paramref name="metres"/>. Binary search.</summary>
    public int IndexAt(float metres)
    {
        int n = distance.Length;
        if (n == 0) return 0;
        if (metres <= 0f) return 0;
        if (metres >= distance[n - 1]) return n - 1;
        int lo = 0, hi = n - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (distance[mid] <= metres) lo = mid; else hi = mid;
        }
        return lo;
    }
}

/// <summary>
/// One leg of a course: a segment ridden from <c>from</c> metres to <c>to</c> metres. When
/// <c>from &gt; to</c> the leg is ridden in reverse, which is what lets the Aozora spur be
/// climbed and descended, and the Maple City road be ridden out and back, at no geometry cost.
/// </summary>
[Serializable]
public class CourseLegData
{
    public string segment = "";
    public float from;
    public float to;
}

[Serializable]
public class CourseData
{
    public string id = "";
    public string displayName = "";
    /// <summary>
    /// Which world-map region this course belongs to (see <see cref="RegionCatalog"/>).
    /// Set by <c>RouteGraphBaker</c> from the route JSON the course was published in, so the
    /// World Map overlay can fast-travel to a region without a second hand-kept table.
    /// </summary>
    public string regionId = RegionCatalog.SakuraPass;
    public bool closed;
    /// <summary>PROVISIONAL: the session length this course is designed around, in minutes.</summary>
    public float targetMinutes = 30f;
    public CourseLegData[] legs = Array.Empty<CourseLegData>();
}

[Serializable]
public class CheckpointData
{
    public string segment = "";
    public float distance;
    public string displayName = "";
}

/// <summary>
/// The whole Sakura Pass road network - every segment, every course and every checkpoint -
/// in one runtime-loadable asset.
///
/// Before this existed, <c>SakuraRoute.json</c> lived outside <c>Resources/</c> and was read
/// through <c>AssetDatabase</c> in editor-only code, so nothing at play time could see the
/// centreline: not the physics, not the GPS window, not the rival AI. Baked by
/// <c>RouteGraphBaker</c> into <c>Assets/Resources/SakuraRouteGraph.asset</c>.
/// </summary>
public class RouteGraph : ScriptableObject
{
    public const string ResourceName = "SakuraRouteGraph";
    public const string AssetPath = "Assets/Resources/SakuraRouteGraph.asset";

    public float roadHalfWidth = 3.5f;
    public float shoulderWidth = 0.55f;

    /// <summary>
    /// Arc length at the summit on the <c>pass</c> segment - the boundary between the original
    /// climb and the descent. Landmark and dressing windows are anchored to this, never to
    /// total route length.
    /// </summary>
    public float climbLength = 529f;

    /// <summary>Arc length along <c>pass</c> where the Aozora spur branches off.</summary>
    public float aozoraJunction = 150f;

    public RouteSegmentData[] segments = Array.Empty<RouteSegmentData>();
    public CourseData[] courses = Array.Empty<CourseData>();
    public CheckpointData[] checkpoints = Array.Empty<CheckpointData>();

    private static RouteGraph _cached;

    /// <summary>Runtime load. Works in play mode, in edit mode and in a player build.</summary>
    public static RouteGraph Load()
    {
        if (_cached == null) _cached = ShuntaRouteProvider.Augment(Resources.Load<RouteGraph>(ResourceName)); // shunta-integration: additive in-memory copy
        return _cached;
    }

    /// <summary>Drops the cached instance so a freshly baked asset is picked up.</summary>
    public static void Invalidate() { _cached = null; }

    public RouteSegmentData Segment(string id)
    {
        for (int i = 0; i < segments.Length; i++)
            if (segments[i].id == id) return segments[i];
        return null;
    }

    public CourseData Course(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < courses.Length; i++)
            if (courses[i].id == id) return courses[i];
        return null;
    }

    /// <summary>
    /// The region a course belongs to. Unknown ids are reported and resolve to the safe default
    /// region for environment visibility only; they never resolve to a different course.
    /// </summary>
    public string RegionOfCourse(string courseId)
    {
        var c = Course(courseId);
        if (c == null)
        {
            if (!string.IsNullOrEmpty(courseId))
                Debug.LogWarning($"[ride] unknown course '{courseId}' has no registered region.");
            return RegionCatalog.SakuraPass;
        }
        return string.IsNullOrEmpty(c.regionId) ? RegionCatalog.SakuraPass : c.regionId;
    }

    /// <summary>First course published for a region, or null when the region has no road yet.</summary>
    public CourseData FirstCourseInRegion(string regionId)
    {
        for (int i = 0; i < courses.Length; i++)
            if (courses[i].regionId == regionId) return courses[i];
        return null;
    }

    public int CourseIndex(string id)
    {
        for (int i = 0; i < courses.Length; i++)
            if (courses[i].id == id) return i;
        return 0;
    }

    /// <summary>Flattens a course definition into a single ridable polyline.</summary>
    public RouteCourse BuildCourse(string id)
    {
        var def = Course(id);
        if (def == null)
        {
            Debug.LogError($"[ride] cannot build unknown course '{id}'.");
            return null;
        }
        return RouteCourse.Build(this, def);
    }
}

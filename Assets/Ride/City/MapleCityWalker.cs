using UnityEngine;

/// <summary>
/// MAPLE CITY LIFE (claude-city, 2026-09-25). Moves one pedestrian along a
/// <see cref="MapleCityWalkLanes"/> pavement lane. The figure itself is a clone of an existing
/// Minato Coast crowd walker, so its gait, feet and LODs all come from
/// <see cref="MinatoCrowdActor"/> unchanged; this component only supplies WHERE it is.
///
/// ORDER. Runs at -40, before MinatoCrowdActor (60). The actor is configured with a zero-length
/// path, so its own SamplePath does nothing and its walk cycle animates in place on top of the
/// position set here.
///
/// DETERMINISTIC. Position is a pure function of Time.time, so a walker that was skipped while
/// far from the camera is simply where it should be when it next updates - no drift, no pops.
/// </summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public sealed class MapleCityWalker : MonoBehaviour
{
    public MapleCityWalkLanes lanes;
    public int lane;
    /// <summary>Arc length on the lane at t = 0.</summary>
    public float startS;
    /// <summary>+1 walks with the lane's point order, -1 against it.</summary>
    public int heading = 1;
    [Min(0f)] public float speed = 0.85f;
    /// <summary>Metres to the walker's right of the lane line (companions, lateral jitter).</summary>
    public float lateral;
    /// <summary>Metres from the lane's pavement height to this figure's origin.</summary>
    public float lift;
    /// <summary>Beyond this camera distance the walker is not updated at all (LOD culls it anyway).</summary>
    public float updateDistance = 260f;
    /// <summary>Set at runtime when a PedestrianBrain places this figure instead (never serialized).</summary>
    [System.NonSerialized] public bool externalDrive;

    /// <summary>Every enabled pavement figure (walkers AND runners), for runner avoidance (C8).</summary>
    public static readonly System.Collections.Generic.List<MapleCityWalker> Active =
        new System.Collections.Generic.List<MapleCityWalker>();

    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    /// <summary>Current arc length on the lane.</summary>
    public float ArcAt(float time) => startS + heading * speed * time;

    void Update()
    {
        if (!Application.isPlaying || lanes == null || !lanes.Valid(lane)) return;
        // 2026-09-26 copilot: a PedestrianBrain (Assets/Ride/Pedestrians) owns this walker.
        if (externalDrive) return;
        var cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude >
            updateDistance * updateDistance)
        {
            // Still advance occasionally so a far walker is not frozen at its last spot.
            if ((Time.frameCount + GetInstanceID()) % 30 != 0) return;
        }
        Place(Time.time);
    }

    /// <summary>Also used by the editor builder to lay the saved (t = 0) pose.</summary>
    public void Place(float time)
    {
        float s = startS + heading * speed * time;
        var p = lanes.Sample(lane, s, out var dir);
        if (heading < 0) dir = -dir;
        var right = Vector3.Cross(Vector3.up, dir);
        p += right * lateral + Vector3.up * lift;
        transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir, Vector3.up));
    }
}

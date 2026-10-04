using System;
using UnityEngine;

/// <summary>
/// Owns checkpoints: fires arrival events, tracks which pin is "next" and drives the banner.
///
/// Checkpoint and lap events are the award hooks the Rider Journal and club reputation will
/// hang off, and the map's pin set doubles as the region's Journal checklist - so this is the
/// one place arrival is decided.
/// </summary>
[DefaultExecutionOrder(-50)]
public class RouteDirector : MonoBehaviour
{
    public RideSession session;

    [Tooltip("PROVISIONAL: how close counts as arriving at a checkpoint, in metres.")]
    public float arrivalRadiusM = 12f;

    [Tooltip("How long the arrival banner stays up, in seconds.")]
    public float bannerSeconds = 4f;

    public CourseCheckpoint NextCheckpoint { get; private set; }
    public float MetresToNext { get; private set; }
    public bool HasNext { get; private set; }
    public string SectionName { get; private set; } = "";
    public string BannerText { get; private set; } = "";
    public float BannerAge { get; private set; } = 999f;
    public int CheckpointsReached { get; private set; }

    public event Action<CourseCheckpoint> CheckpointReached;

    private int _lastCleared = -1;
    private string _lastSection = "";

    private void OnEnable()
    {
        if (session != null) session.LapCompleted += OnLap;
    }

    private void OnDisable()
    {
        if (session != null) session.LapCompleted -= OnLap;
    }

    private void OnLap(int lap)
    {
        _lastCleared = -1;
        Banner($"LAP {lap + 1} of {session.TotalLaps}");
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>Deterministic tick so the editor harness drives the same code path.</summary>
    public void Tick(float dt)
    {
        if (session == null) return;
        var course = session.Course;
        if (course == null || course.Count < 2) return;

        BannerAge += dt;
        // An NPC race moves the rider along the course and then puts them back: no checkpoint
        // arrivals, section banners or checkpoint coin bonuses may fire from that.
        if (RaceDirector.Busy) return;
        // Display name, not the raw graph segment: see RouteNames.Section.
        SectionName = RouteNames.Section(course, course.SegmentAt(session.DistanceM));
        if (SectionName != _lastSection && !string.IsNullOrEmpty(_lastSection))
            Banner($"ENTERING {SectionName.ToUpperInvariant()}");
        _lastSection = SectionName;

        HasNext = course.NextCheckpoint(session.DistanceM, out var cp, out float toGo);
        NextCheckpoint = cp;
        MetresToNext = toGo;

        if (!HasNext || course.Checkpoints.Length == 0) return;

        // Arrival is decided on arc length, not on a trigger volume: the rider is exactly on
        // the course, so a distance test cannot be missed by a fast frame.
        for (int i = 0; i < course.Checkpoints.Length; i++)
        {
            if (i == _lastCleared) continue;
            float delta = Mathf.Abs(course.Checkpoints[i].Distance - session.DistanceM);
            if (delta <= arrivalRadiusM)
            {
                _lastCleared = i;
                CheckpointsReached++;
                Banner($"CHECKPOINT - {RouteNames.Checkpoint(course.Checkpoints[i].Name).ToUpperInvariant()}");
                CheckpointReached?.Invoke(course.Checkpoints[i]);
                break;
            }
        }
    }

    public bool BannerVisible => BannerAge < bannerSeconds;

    private void Banner(string text)
    {
        BannerText = text;
        BannerAge = 0f;
    }
}

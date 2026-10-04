using System;
using UnityEngine;

/// <summary>Tuning for <see cref="ShuntaLifeDirector"/> (ambient life on Shunta Metro). All distances in metres.</summary>
[Serializable]
public class ShuntaLifeSettings
{
    [Header("General")]
    [Min(50f)] public float activeRadius = 300f;
    public int seed = 8201;

    [Header("Commuter train (zone 8 'Under Railway')")]
    public bool trainEnabled = true;
    public int trainZone = 8;
    [Range(2, 12)] public int trainCars = 8;
    public float carLength = 18f;
    public float carGap = 0.8f;
    public float trainSpeed = 24f;
    /// <summary>Lateral offset of the rail line from the road centre (positive = rider's right).</summary>
    public float trainLateral = 15f;
    public float trainHeight = 4.5f;
    public Color trainBody = new Color(0.78f, 0.80f, 0.84f);
    public Color trainStripe = new Color(0.1f, 0.75f, 0.45f);
    public Color trainWindow = new Color(1f, 0.92f, 0.65f);
    public float windowIntensity = 2.2f;

    [Header("Opposing traffic (expressway zones)")]
    public bool trafficEnabled = true;
    // 2026-10-04: traffic now runs on every zone (user: Shunta should be alive); street zones use slowZones/slowScale so cars crawl through town.
    public int[] trafficZones = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    public int[] slowZones = { 1, 2, 3, 4, 7, 8, 11, 12 };
    [Range(0.1f, 1f)] public float slowScale = 0.38f;
    [Range(0, 40)] public int trafficCars = 24;
    public Vector2 trafficSpeed = new Vector2(16f, 30f);
    /// <summary>Lateral offset of the opposite lane (negative = rider's left).</summary>
    public float trafficLateral = -3.2f;
    public Vector2 trafficSize = new Vector2(1.9f, 4.4f);
    public float headlightIntensity = 6f;
    public float taillightIntensity = 3f;
    public float respawnInterval = 0.4f;
    public float minSpawnAhead = 120f;

    [Header("Weather particles (camera attached)")]
    public bool rainEnabled = true;
    public int[] rainZones = { 7, 8 };
    public float rainRate = 700f;
    public float rainWind = -3f;
    public Color rainColor = new Color(0.7f, 0.85f, 1f, 0.35f);

    public bool sakuraEnabled = true;
    public int[] sakuraZones = { 11, 12 };
    public float sakuraRate = 60f;
    public Color sakuraColorA = new Color(1f, 0.72f, 0.82f, 0.95f);
    public Color sakuraColorB = new Color(1f, 0.9f, 0.94f, 0.95f);

    public bool InZones(int[] zones, int zone)
    {
        if (zones == null) return false;
        for (int i = 0; i < zones.Length; i++) if (zones[i] == zone) return true;
        return false;
    }
}

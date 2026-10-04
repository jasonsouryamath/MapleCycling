using UnityEngine;

/// <summary>
/// An editable weather preset (Create > MapleRide > Weather Preset). Presets are DATA, blended
/// by <see cref="WeatherDirector"/>; nothing branches on a preset's name.
/// </summary>
[CreateAssetMenu(menuName = "MapleRide/Weather Preset", fileName = "WeatherPreset")]
public sealed class WeatherPreset : ScriptableObject
{
    [Header("Surface wind (gameplay)")]
    [Tooltip("Direction the air moves TOWARD, degrees clockwise from world +Z.")]
    [Range(0f, 360f)] public float windToDeg = 45f;
    [Range(0f, 25f)] public float sustainedMps = 3f;
    [Range(0f, 12f)] public float gustMps = 1.5f;
    [Range(0.01f, 0.5f)] public float gustFrequencyHz = 0.06f;
    [Range(0f, 1f)] public float turbulence = 0.2f;

    [Header("Sky / atmosphere (visual)")]
    [Range(0f, 1f)] public float cloudCoverage = 0.35f;
    [Range(0f, 1f)] public float cloudDensity = 0.5f;
    public float cloudAltitudeM = 2200f;
    [Range(0f, 1f)] public float precipitation = 0f;
    [Range(0f, 1f)] public float humidity = 0.5f;
    [Range(0f, 1f)] public float fog = 0.1f;
    public float airTempC = 18f;
    [Range(0f, 1f)] public float wetness = 0f;

    [Header("Snow")]
    [Tooltip("Lying snow, 0-1. Only surfaces whose material accepts snow (_SnowAccept) show it, and only " +
             "while a WeatherSnowCover region is active. Precipitation falls as SNOW when airTempC <= ~1 C.")]
    [Range(0f, 1f)] public float snowCover = 0f;

    [Header("Front")]
    [Range(0f, 360f)] public float frontTravelDeg = 60f;
    [Range(0f, 30f)] public float frontSpeedMps = 8f;
    [Tooltip("Seconds to blend INTO this preset.")]
    public float transitionSeconds = 240f;
}

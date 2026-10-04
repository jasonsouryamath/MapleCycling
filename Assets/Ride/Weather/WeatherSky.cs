using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Drives the HDRP sky from <see cref="WeatherDirector.State"/> through ONE runtime global volume
/// (priority above the region profiles, which stay the source of their own look):
///  * VisualEnvironment wind orientation/speed = the upper-air version of the gameplay wind, so
///    CloudLayer scrolling (default "global wind" mode) travels the same way the rider feels.
///  * CloudLayer opacity = cloud coverage.
/// Only these parameters are overridden; every region's cloud map, tint and grade are untouched.
/// Runtime-created profile, so play mode never writes to the region assets.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeatherSky : MonoBehaviour
{
    [Tooltip("Upper-atmosphere wind is stronger than surface wind.")]
    public float upperWindFactor = 1.8f;
    [Tooltip("Seconds between sky updates (visual only; physics wind is unaffected).")]
    public float updateInterval = 0.25f;

    private Volume _volume;
    private VolumeProfile _profile;
    private VisualEnvironment _env;
    private CloudLayer _clouds;
    private float _next;

    private void Start()
    {
        var go = new GameObject("~Weather Sky Volume");
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 60f;
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volume.sharedProfile = _profile;
        _env = _profile.Add<VisualEnvironment>();
        _clouds = _profile.Add<CloudLayer>();
        _env.windOrientation.overrideState = true;
        _env.windSpeed.overrideState = true;
        _clouds.opacity.overrideState = true;
    }

    private void OnDestroy()
    {
        if (_profile != null) Destroy(_profile);
    }

    private void Update()
    {
        var wd = WeatherDirector.Instance;
        if (wd == null || _env == null || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + updateInterval;

        var s = wd.State;
        var w = s.WindToXZ;
        // HDRP orientation: degrees, measured like a compass from world +Z toward +X for the
        // direction the wind blows TOWARD. VISUAL CHECK PENDING - flip by 180 if clouds run
        // against the flags/rain once those read _MapleWind.
        float deg = Mathf.Atan2(w.x, w.y) * Mathf.Rad2Deg;
        if (deg < 0f) deg += 360f;
        _env.windOrientation.value = deg;
        _env.windSpeed.value = w.magnitude * upperWindFactor * 3.6f;   // km/h
        // Coverage 0.2 -> scattered cloud, 0.9 -> heavy. Floor raised 0.35 -> 0.7 (2026-10-02): at 0.35 the
        // cloud maps were invisible, so a 'clear' day had a bare sky. Never fully clear or opaque.
        _clouds.opacity.value = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(s.Coverage));
    }
}

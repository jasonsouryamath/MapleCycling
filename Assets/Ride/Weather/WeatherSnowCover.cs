using UnityEngine;

/// <summary>
/// Region snow cover (WP-E weather snow, 2026-09-26). Put ONE on a region's environment root; while
/// that root is active the region is snowbound:
///  * pushes the snow globals every MapleRide surface shader reads (_MR_SnowCover, _MR_SnowParams,
///    _MR_SnowAlbedo, _MR_SnowShadow). Only materials with _SnowAccept > 0 show it, so riders,
///    bikes and pedestrians never grow white caps;
///  * in play mode, blends the WeatherDirector to the region's snow preset (default AlpineSnowfall)
///    so the existing weather system drives the snowfall particles, fog, sky and wind, and restores
///    the previous preset when the region is left.
/// [ExecuteAlways] so edit-mode captures and the Scene view show the same snow the game does.
/// Disabling the root (RegionDirector streaming the region out) zeroes the cover.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class WeatherSnowCover : MonoBehaviour
{
    [Range(0f, 1f)] public float cover = 1f;
    [Tooltip("Altitude where cover starts / is full. Equal values = no altitude term (snow everywhere).")]
    public float snowLineStartY = 0f, snowLineFullY = 0f;
    [Range(0f, 3f)] public float sparkle = 1f;
    [Range(0f, 2f)] public float driftNoise = 1f;
    [Tooltip("Lit snow albedo (sRGB). Kept below white so sparkle and highlights still read.")]
    public Color albedo = new Color(0.90f, 0.92f, 0.95f);
    [Tooltip("Sky-lit shadow tint on snow (sRGB): open shade on snow is blue.")]
    public Color shadowTint = new Color(0.60f, 0.70f, 0.88f);
    [Range(0.3f, 1f)] public float shadowFloor = 0.80f;
    [Tooltip("Weather preset (Resources/Weather/<name>) the region blends to in play mode. Empty = leave weather alone.")]
    public string weatherPreset = "AlpineSnowfall";
    public float presetBlendSeconds = 25f;

    public static WeatherSnowCover Active { get; private set; }

    private static readonly int CoverId = Shader.PropertyToID("_MR_SnowCover");
    private static readonly int ParamsId = Shader.PropertyToID("_MR_SnowParams");
    private static readonly int AlbedoId = Shader.PropertyToID("_MR_SnowAlbedo");
    private static readonly int ShadowId = Shader.PropertyToID("_MR_SnowShadow");

    private WeatherPreset _previous;
    private bool _presetApplied;

    private void OnEnable()
    {
        Active = this;
        _presetApplied = false;
        Push();
    }

    private void OnDisable()
    {
        if (Active == this)
        {
            Active = null;
            Shader.SetGlobalFloat(CoverId, 0f);
        }
        if (Application.isPlaying && _presetApplied && WeatherDirector.Instance != null && _previous != null)
            WeatherDirector.Instance.SetPreset(_previous, presetBlendSeconds);
        _presetApplied = false;
    }

    private void OnValidate() { if (isActiveAndEnabled) Push(); }

    private void Update()
    {
        if (Active != this) return;
        if (Application.isPlaying && !_presetApplied && !string.IsNullOrEmpty(weatherPreset))
        {
            var wd = WeatherDirector.Instance;
            var p = Resources.Load<WeatherPreset>("Weather/" + weatherPreset);
            if (wd != null && p != null && wd.State.AirDensity > 0f)
            {
                _previous = wd.preset != p ? wd.preset : _previous;
                if (wd.preset != p) wd.SetPreset(p, presetBlendSeconds);
                _presetApplied = true;
            }
        }
        Push();
    }

    /// <summary>Region cover, raised (never lowered) by whatever lying snow the live weather carries.</summary>
    public float EffectiveCover()
    {
        float c = cover;
        var wd = Application.isPlaying ? WeatherDirector.Instance : null;
        if (wd != null) c = Mathf.Max(c * Mathf.Lerp(0.85f, 1f, wd.State.SnowCover), wd.State.SnowCover);
        return Mathf.Clamp01(c);
    }

    public void Push()
    {
        Shader.SetGlobalFloat(CoverId, EffectiveCover());
        Shader.SetGlobalVector(ParamsId, new Vector4(snowLineStartY, snowLineFullY, sparkle, driftNoise));
        Shader.SetGlobalVector(AlbedoId, new Vector4(albedo.r, albedo.g, albedo.b, 0f));
        Shader.SetGlobalVector(ShadowId, new Vector4(shadowTint.r, shadowTint.g, shadowTint.b, shadowFloor));
    }
}

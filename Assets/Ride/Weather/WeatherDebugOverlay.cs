using UnityEngine;

/// <summary>
/// In-game weather debugger. F9 toggles. Shows the authoritative state and what the rider
/// feels; F10 freezes weather, F11 cycles presets (instant blend), F12 cycles a forced test
/// wind (off / 20 km/h head / tail / cross from right) relative to the rider's heading.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeatherDebugOverlay : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F9;
    public bool visible;

    private WeatherPreset[] _presets;
    private int _presetIdx = -1, _forced;
    private GUIStyle _style;

    private void Update()
    {
        var wd = WeatherDirector.Instance;
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
        if (!visible || wd == null) return;
        if (Input.GetKeyDown(KeyCode.F10)) wd.freeze = !wd.freeze;
        if (Input.GetKeyDown(KeyCode.F11))
        {
            if (_presets == null) _presets = Resources.LoadAll<WeatherPreset>("Weather");
            if (_presets.Length > 0)
            {
                _presetIdx = (_presetIdx + 1) % _presets.Length;
                wd.SetPreset(_presets[_presetIdx], 2f);
            }
        }
        if (Input.GetKeyDown(KeyCode.F12)) _forced = (_forced + 1) % 4;
        ApplyForcedWind(wd);
    }

    /// <summary>Test harness: 20 km/h wind fixed relative to the rider (for head/tail/cross checks).</summary>
    private void ApplyForcedWind(WeatherDirector wd)
    {
        wd.debugWindOverride = null;
        if (_forced == 0 || wd.rider == null) return;
        var f = new Vector2(wd.rider.forward.x, wd.rider.forward.z).normalized;
        var right = new Vector2(f.y, -f.x);
        const float v = 20f / 3.6f;
        wd.debugWindOverride = _forced == 1 ? -f * v : _forced == 2 ? f * v : -right * v;
    }

    private void OnGUI()
    {
        var wd = WeatherDirector.Instance;
        if (!visible || wd == null) return;
        _style ??= new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft,
                                                richText = true, normal = { textColor = Color.white } };
        var s = wd.State;
        var a = wd.Apparent;
        var phys = wd.session != null ? wd.session.physics : null;
        float speed = wd.session != null ? wd.session.SpeedMps : 0f;
        float drag = phys != null
            ? WindMath.AeroDragN(phys.airDensityKgM3, phys.cdA * phys.dragMultiplier, speed + phys.headwindMps) : 0f;
        string forced = _forced switch { 1 => "HEAD 20", 2 => "TAIL 20", 3 => "CROSS-R 20", _ => "off" };
        string text =
            $"<b>WEATHER DEBUG</b>  (F9 hide, F10 freeze, F11 preset, F12 test wind)\n" +
            $"preset  {(wd.preset != null ? wd.preset.name : "-")}   transition {wd.TransitionProgress * 100f:0}%" +
            $"   frozen {wd.freeze}   seed {wd.seed}\n" +
            $"global wind  {s.WindToXZ.magnitude * 3.6f:0.0} km/h toward {Mathf.Repeat(Mathf.Atan2(s.WindToXZ.x, s.WindToXZ.y) * Mathf.Rad2Deg, 360f):0} deg" +
            $"   gust {s.GustMps * 3.6f:0.0} km/h @ {s.GustHz:0.00} Hz   test wind {forced}\n" +
            $"exposure  x{wd.Exposure:0.00}   draft mult x{(phys != null ? phys.dragMultiplier : 1f):0.00}" +
            $"   air density {s.AirDensity:0.000}\n" +
            $"apparent  {a.SpeedMps * 3.6f:0.0} km/h from {a.AngleDeg:0} deg   head {a.HeadwindMps * 3.6f:0.0}" +
            $"   cross {a.CrosswindMps * 3.6f:0.0} km/h\n" +
            $"aero drag {drag:0.0} N   road speed {speed * 3.6f:0.0} km/h\n" +
            $"clouds {s.Coverage:0.00}/{s.Density:0.00} @ {s.AltitudeM:0} m   precip {s.Precipitation:0.00}" +
            $"   fog {s.Fog:0.00}   wet {s.Wetness:0.00}   front {s.FrontDeg:0} deg @ {s.FrontMps:0.0} m/s";
        GUI.Box(new Rect(20, Screen.height * 0.5f - 90, 720, 168), text, _style);
    }
}

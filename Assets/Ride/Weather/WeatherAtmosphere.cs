using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Atmosphere from <see cref="WeatherDirector.State"/> (humidity, fog, precipitation):
///  * FOG - one runtime global volume (priority 61, same pattern as <see cref="WeatherSky"/>)
///    overrides ONLY HDRP Fog.meanFreePath = region base / FogScale. The region profiles stay
///    the source of their own look and are never edited. The hand-written MapleRide shaders
///    fog from RenderSettings (MapleRideSunBinder), so the same scale is applied there too,
///    relative to whatever RegionDirector last wrote (detected, so it never compounds).
///  * RAIN CURTAINS - when Precipitation > 0.3, a few huge soft vertical cards hang under the
///    storm cells 1-3 km away. They lie along the front line, on the side the front
///    (State.FrontDeg = travel direction) arrives from, drift in with State.FrontMps, and are
///    pushed off / hidden if they would stand over the route.
/// Self-spawns (hidden, DontDestroyOnLoad) and idles while no WeatherDirector exists.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeatherAtmosphere : MonoBehaviour
{
    [Header("Fog")]
    [Tooltip("Seconds between fog updates; set from the quality tier.")]
    public float updateInterval = 0.25f;
    [Tooltip("Upper clamp on how much thicker than the region's own fog weather can make it.")]
    public float maxFogScale = 10f;

    [Header("Rain curtains")]
    [Tooltip("Precipitation above which distant rain curtains appear.")]
    public float curtainThreshold = 0.3f;
    public float minDistanceM = 1000f, maxDistanceM = 3000f;
    [Tooltip("No curtain footprint may come closer than this to the route line.")]
    public float routeClearanceM = 450f;
    [Range(0f, 1f)] public float maxCurtainAlpha = 0.85f;
    public int curtainCount = 6;

    public static WeatherAtmosphere Instance { get; private set; }
    public float FogScale { get; private set; } = 1f;
    public float BaseMeanFreePath { get; private set; } = -1f;
    public int VisibleCurtains { get; private set; }
    public IReadOnlyList<Transform> Curtains => _curtainTf;
    /// <summary>Current opacity of each curtain (0 when hidden).</summary>
    public IReadOnlyList<float> CurtainAlpha => _curtainAlpha;

    private Volume _volume;
    private VolumeProfile _profile;
    private Fog _fog;
    private float _nextFog, _nextBaseScan;
    // RenderSettings fog as last authored by someone else, and what we wrote over it.
    private float _rsBaseDensity = -1f, _rsBaseStart, _rsBaseEnd, _rsWrittenDensity = -1f, _rsWrittenStart, _rsWrittenEnd;

    private readonly List<Transform> _curtainTf = new();
    private readonly List<Renderer> _curtainR = new();
    private readonly List<float> _curtainWidth = new();
    private readonly List<float> _curtainAlpha = new();
    private Material _curtainMat;
    private Mesh _curtainMesh;
    private MaterialPropertyBlock _mpb;
    private static readonly int ColorId = Shader.PropertyToID("_UnlitColor");

    private RouteCourse _routeFor;
    private readonly List<Vector2> _route = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Instance != null) return;
        var go = new GameObject("~Weather Atmosphere") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<WeatherAtmosphere>();
    }

    private void OnEnable() { if (Instance == null) Instance = this; }

    private void OnDisable()
    {
        RestoreRenderSettings();
        if (_volume != null) _volume.weight = 0f;
        SetCurtainsVisible(0);
        if (Instance == this) Instance = null;
    }

    private void OnDestroy()
    {
        if (_profile != null) Destroy(_profile);
        if (_curtainMat != null) { Destroy(_curtainMat.GetTexture("_UnlitColorMap")); Destroy(_curtainMat); }
        if (_curtainMesh != null) Destroy(_curtainMesh);
    }

    private void Start()
    {
        var t = WeatherEffects.TierFromQuality();
        updateInterval = t == WeatherEffects.Tier.Low ? 1f : t == WeatherEffects.Tier.Medium ? 0.5f : 0.25f;
        curtainCount = t == WeatherEffects.Tier.Low ? 2 : t == WeatherEffects.Tier.Medium ? 4 : 6;

        var go = new GameObject("~Weather Atmosphere Volume");
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 61f;
        _volume.weight = 0f;
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volume.sharedProfile = _profile;
        _fog = _profile.Add<Fog>();
        _mpb = new MaterialPropertyBlock();
    }

    // ------------------------------------------------------------------ pure maths (tested)

    /// <summary>Fog extinction multiplier from the weather. Neutral = the default preset
    /// (ClearCoastalBreeze: fog 0.05, humidity 0.5, dry) = 1, so the region's authored fog stays the
    /// everyday look; SunShower ~2.7x, MarineHaze hits the 10x cap.</summary>
    public static float ComputeFogScale(float fog, float humidity, float precipitation, float max = 10f)
    {
        float k = Mathf.Exp(7f * (Mathf.Clamp01(fog) - 0.05f))
                  * (1f + 0.8f * (Mathf.Clamp01(humidity) - 0.5f))
                  * (1f + 0.8f * Mathf.Clamp01(precipitation));
        return Mathf.Clamp(k, 0.6f, max);
    }

    /// <summary>0..1 curtain strength: none at or below the threshold, full 0.2 above it.</summary>
    public static float CurtainStrength(float precipitation, float threshold = 0.3f) =>
        Mathf.Clamp01((precipitation - threshold) / 0.2f);

    /// <summary>
    /// Deterministic storm-cell centre for curtain i (XZ, relative to the rider): spread along
    /// the front line, on the arrival side, distance cycling maxD -> minD as the front moves in.
    /// Returns a 0..1 fade that hides the wrap-around at both distance ends.
    /// </summary>
    public static Vector2 CellOffset(int i, int count, float frontDeg, float frontMps, double t, int seed,
                                     float minD, float maxD, out float fade)
    {
        float a = frontDeg * Mathf.Deg2Rad;
        var arrival = -new Vector2(Mathf.Sin(a), Mathf.Cos(a));    // the side the front comes from
        float h = Hash01(seed, i);
        float span = Mathf.Max(1f, maxD - minD);
        // Cells approach at the front speed; phase staggered per cell.
        double cyc = (h + t * Mathf.Max(0.5f, frontMps) / span) % 1.0;
        float u = 1f - (float)cyc;                                  // 1 = far, 0 = near
        float dist = minD + u * span;
        fade = Mathf.Clamp01(u / 0.15f) * Mathf.Clamp01((1f - u) / 0.15f);
        // Spread the cells along the front line: +/-55 deg either side of the arrival bearing.
        float spread = ((i + 0.5f) / Mathf.Max(1, count) - 0.5f) * 110f + (Hash01(seed, i + 101) - 0.5f) * 12f;
        float r = spread * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
        var dir = new Vector2(arrival.x * cs - arrival.y * sn, arrival.x * sn + arrival.y * cs);
        return dir * dist;
    }

    /// <summary>Minimum XZ distance from p to a polyline.</summary>
    public static float DistanceToPolyline(Vector2 p, IReadOnlyList<Vector2> line)
    {
        float best = float.MaxValue;
        for (int k = 0; k + 1 < line.Count; k++)
        {
            var a = line[k]; var ab = line[k + 1] - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
        }
        if (line.Count == 1) best = (p - line[0]).magnitude;
        return best;
    }

    private static float Hash01(int seed, int i)
    {
        unchecked
        {
            uint h = (uint)seed * 747796405u + (uint)i * 2891336453u + 1u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    // ------------------------------------------------------------------ runtime

    private void Update()
    {
        var wd = WeatherDirector.Instance;
        if (wd == null || _volume == null)
        {
            if (_volume != null && _volume.weight > 0f) { _volume.weight = 0f; RestoreRenderSettings(); SetCurtainsVisible(0); }
            return;
        }
        var s = wd.State;
        var cam = Camera.main;

        if (Time.unscaledTime >= _nextFog)
        {
            _nextFog = Time.unscaledTime + updateInterval;
            FogScale = ComputeFogScale(s.Fog, s.Humidity, s.Precipitation, maxFogScale);
            UpdateHdrpFog(cam);
            UpdateRenderSettingsFog();
        }
        UpdateCurtains(wd, s, cam);

        if (VisibleCurtains != _loggedCurtains || Mathf.Abs(FogScale - _loggedScale) > 0.25f)
        {
            _loggedCurtains = VisibleCurtains; _loggedScale = FogScale;
            Debug.Log($"[weather-atmos] fog x{FogScale:0.00} (HDRP base mfp {BaseMeanFreePath:0}, " +
                      $"RenderSettings density {RenderSettings.fogDensity:0.000000}), curtains {VisibleCurtains}");
        }
    }
    private int _loggedCurtains = -1;
    private float _loggedScale = -10f;

    private void UpdateHdrpFog(Camera cam)
    {
        if (Time.unscaledTime >= _nextBaseScan)
        {
            _nextBaseScan = Time.unscaledTime + 2f;   // regions stream in and out
            BaseMeanFreePath = FindBaseMeanFreePath(cam != null ? cam.transform.position : Vector3.zero);
        }
        bool have = BaseMeanFreePath > 0f;
        _fog.meanFreePath.overrideState = have;
        if (have) _fog.meanFreePath.value = Mathf.Max(1f, BaseMeanFreePath / FogScale);
        _volume.weight = have ? 1f : 0f;
    }

    /// <summary>meanFreePath of the highest-priority OTHER volume that affects the camera.</summary>
    private float FindBaseMeanFreePath(Vector3 camPos)
    {
        float best = -1f, bestPri = float.MinValue;
        foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (v == _volume || !v.isActiveAndEnabled || v.weight <= 0f) continue;
            var prof = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
            if (prof == null || !prof.TryGet<Fog>(out var f) || !f.active || !f.meanFreePath.overrideState) continue;
            if (!v.isGlobal)
            {
                bool inside = false;
                foreach (var c in v.GetComponents<Collider>())
                    if (c.enabled && c.bounds.Contains(camPos)) { inside = true; break; }
                if (!inside) continue;
            }
            if (v.priority >= bestPri) { bestPri = v.priority; best = f.meanFreePath.value; }
        }
        return best;
    }

    private void UpdateRenderSettingsFog()
    {
        // Someone else (RegionDirector.ApplyAmbience) wrote new fog: adopt it as the base.
        if (_rsBaseDensity < 0f || !Mathf.Approximately(RenderSettings.fogDensity, _rsWrittenDensity)
            || !Mathf.Approximately(RenderSettings.fogStartDistance, _rsWrittenStart)
            || !Mathf.Approximately(RenderSettings.fogEndDistance, _rsWrittenEnd))
        {
            _rsBaseDensity = RenderSettings.fogDensity;
            _rsBaseStart = RenderSettings.fogStartDistance;
            _rsBaseEnd = RenderSettings.fogEndDistance;
        }
        float k = FogScale;
        // k scales the extinction coefficient (density) in every mode, like 1/meanFreePath in HDRP.
        RenderSettings.fogDensity = _rsBaseDensity * k;
        RenderSettings.fogStartDistance = _rsBaseStart / k;
        RenderSettings.fogEndDistance = _rsBaseEnd / k;
        _rsWrittenDensity = RenderSettings.fogDensity;
        _rsWrittenStart = RenderSettings.fogStartDistance;
        _rsWrittenEnd = RenderSettings.fogEndDistance;
    }

    private void RestoreRenderSettings()
    {
        if (_rsBaseDensity < 0f) return;
        if (Mathf.Approximately(RenderSettings.fogDensity, _rsWrittenDensity))
        {
            RenderSettings.fogDensity = _rsBaseDensity;
            RenderSettings.fogStartDistance = _rsBaseStart;
            RenderSettings.fogEndDistance = _rsBaseEnd;
        }
        _rsBaseDensity = -1f;
    }

    // ------------------------------------------------------------------ rain curtains

    private void UpdateCurtains(WeatherDirector wd, in WeatherDirector.WeatherState s, Camera cam)
    {
        float strength = CurtainStrength(s.Precipitation, curtainThreshold);
        if (strength <= 0f || cam == null) { SetCurtainsVisible(0); return; }
        EnsureCurtains();

        var origin = wd.rider != null ? wd.rider.position : cam.transform.position;
        double t = wd.session != null ? wd.session.ElapsedSeconds : Time.timeAsDouble;
        RefreshRoute(wd.session != null ? wd.session.Course : null);
        float fogCol = Mathf.Clamp01(RenderSettings.fogColor.grayscale);
        var baseCol = Color.Lerp(new Color(0.20f, 0.23f, 0.28f), new Color(0.30f, 0.32f, 0.36f), fogCol);
        float cloudBase = Mathf.Clamp(s.AltitudeM, 600f, 1800f);
        var wind = s.WindToXZ;

        int shown = 0;
        for (int i = 0; i < curtainCount; i++)
        {
            var off = CellOffset(i, curtainCount, s.FrontDeg, s.FrontMps, t, wd.seed, minDistanceM, maxDistanceM, out float fade);
            var c = new Vector2(origin.x, origin.z) + off;
            float half = _curtainWidth[i] * 0.5f;
            // Keep the whole footprint clear of the route: step outward (away from the rider).
            if (_route.Count > 0)
            {
                var outward = off.sqrMagnitude > 1f ? off.normalized : Vector2.up;
                int guard = 0;
                while (DistanceToPolyline(c, _route) < routeClearanceM + half && guard++ < 8) c += outward * 200f;
                if (DistanceToPolyline(c, _route) < routeClearanceM + half || (c - new Vector2(origin.x, origin.z)).magnitude > maxDistanceM)
                { _curtainR[i].enabled = false; _curtainAlpha[i] = 0f; continue; }
            }
            float a = maxCurtainAlpha * strength * fade;
            _curtainAlpha[i] = a;
            if (a < 0.01f) { _curtainR[i].enabled = false; continue; }

            var tf = _curtainTf[i];
            float bottom = Mathf.Min(origin.y, 0f) - 150f;
            tf.position = new Vector3(c.x, bottom, c.y);
            // Billboard around Y toward the camera, then lean the streaks with the wind across it.
            var toCam = cam.transform.position - tf.position; toCam.y = 0f;
            var yaw = toCam.sqrMagnitude > 1f ? Quaternion.LookRotation(-toCam.normalized, Vector3.up) : Quaternion.identity;
            var right = yaw * Vector3.right;
            float cross = Vector2.Dot(wind, new Vector2(right.x, right.z));
            float lean = Mathf.Clamp(Mathf.Atan2(cross, 9f) * Mathf.Rad2Deg * 0.6f, -12f, 12f);
            tf.rotation = yaw * Quaternion.Euler(0f, 0f, -lean);
            tf.localScale = new Vector3(_curtainWidth[i], cloudBase - bottom, 1f);

            _curtainR[i].enabled = true;
            _curtainR[i].GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, new Color(baseCol.r, baseCol.g, baseCol.b, a));
            _curtainR[i].SetPropertyBlock(_mpb);
            shown++;
        }
        VisibleCurtains = shown;
    }

    private void RefreshRoute(RouteCourse course)
    {
        if (course == _routeFor) return;
        _routeFor = course;
        _route.Clear();
        if (course == null || course.Length < 10f) return;
        for (float m = 0f; m <= course.Length; m += 50f)
        {
            var p = course.PositionAt(m);
            _route.Add(new Vector2(p.x, p.z));
        }
        var e = course.PositionAt(course.Length);
        _route.Add(new Vector2(e.x, e.z));
    }

    private void SetCurtainsVisible(int n)
    {
        for (int i = 0; i < _curtainR.Count; i++)
        {
            if (_curtainR[i] != null) _curtainR[i].enabled = i < n;
            if (i >= n) _curtainAlpha[i] = 0f;
        }
        VisibleCurtains = Mathf.Min(n, _curtainR.Count);
    }

    private void EnsureCurtains()
    {
        if (_curtainTf.Count == curtainCount) return;
        if (_curtainMat == null) _curtainMat = CurtainMaterial();
        if (_curtainMesh == null) _curtainMesh = CurtainMesh();
        var mesh = _curtainMesh;
        while (_curtainTf.Count < curtainCount)
        {
            int i = _curtainTf.Count;
            var go = new GameObject($"~Rain Curtain {i}");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _curtainMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            _curtainTf.Add(go.transform);
            _curtainR.Add(r);
            _curtainWidth.Add(Mathf.Lerp(380f, 760f, Hash01(977, i)));
            _curtainAlpha.Add(0f);
        }
    }

    /// <summary>Unit quad, pivot at the bottom centre, facing -Z (LookRotation toward the camera).</summary>
    public static Mesh CurtainMesh()
    {
        var m = new Mesh { name = "~RainCurtain" };
        m.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        m.RecalculateNormals();
        m.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1f, 1f, 1f));
        return m;
    }

    public static Material CurtainMaterial()
    {
        var sh = Shader.Find("HDRP/Unlit");
        if (sh == null) return null;
        var m = new Material(sh) { name = "~WeatherRainCurtain" };
        m.SetFloat("_SurfaceType", 1f);
        m.SetFloat("_BlendMode", 0f);
        m.SetFloat("_DoubleSidedEnable", 1f);
        m.SetFloat("_CullMode", 0f);
        m.SetFloat("_EnableFogOnTransparent", 1f);
        m.SetTexture("_UnlitColorMap", CurtainTexture());
        m.SetColor("_UnlitColor", new Color(0.5f, 0.53f, 0.58f, 0.4f));
        HDMaterial.ValidateMaterial(m);
        return m;
    }

    /// <summary>Soft streaky alpha: feathered sides, faint top where it meets the cloud base,
    /// thicker toward the bottom where rain columns merge.</summary>
    private static Texture2D CurtainTexture()
    {
        const int W = 128, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "~RainCurtainTex", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        var cols = new float[W];
        for (int x = 0; x < W; x++) cols[x] = 0.55f + 0.45f * Mathf.PerlinNoise(x * 0.21f, 3.7f);
        for (int y = 0; y < H; y++)
        {
            float v = y / (H - 1f);
            float vert = Mathf.SmoothStep(0f, 1f, (1f - v) / 0.35f) * (0.75f + 0.25f * (1f - v));
            for (int x = 0; x < W; x++)
            {
                float u = x / (W - 1f);
                float edge = 0.5f - Mathf.Abs(u - 0.5f);
                float wob = 0.12f * Mathf.PerlinNoise(v * 3f, u * 2f + 9f);
                float side = Mathf.SmoothStep(0f, 1f, (edge - wob) / 0.3f);
                float streak = Mathf.Lerp(cols[x], 1f, 0.35f) * (0.85f + 0.15f * Mathf.PerlinNoise(x * 0.5f, v * 12f));
                byte a = (byte)(255f * Mathf.Clamp01(side * vert * streak));
                px[y * W + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }
}

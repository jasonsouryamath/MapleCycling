using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shunta Metro night sky: a camera-centred star dome (baked star + Milky Way maps, procedural moon, thin neon-lit
/// clouds, zone-coloured horizon glow, shooting stars) drawn by <c>MapleRide/Shunta/NightSky</c> on top of HDRP's
/// gradient sky. <see cref="ShuntaLookDriver"/> calls <see cref="NotifyKm"/> whenever it applies a km, so the sky
/// follows the rider (play) or the preview km (editor). Nothing generated is saved in the scene.
/// Visibility by km: golden dusk 0, dusk .08, blue hour .55, night 1, deep night 1, pre-dawn .85, sakura dawn .6.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(210)]
[DisallowMultipleComponent]
public sealed class ShuntaNightSky : MonoBehaviour
{
    [Header("Assets (assigned by ShuntaMetroSky.ApplyToPlayable)")]
    public Shader shader;
    public Texture2D starMap;
    public Texture2D glowMap;

    [Header("Stars")]
    [Range(0f, 8f)] public float starGain = 2.6f;
    [Range(0f, 1f)] public float twinkle = 0.35f;
    [Range(0f, 6f)] public float milkyWayGain = 1.6f;
    [Tooltip("Sky rotation, degrees per minute (0 = frozen).")] public float rotationDegPerMinute = 0.4f;
    [Tooltip("Force visibility 0..1 (negative = automatic from zone time of day).")] public float visibilityOverride = -1f;

    [Header("Moon")]
    [Range(0f, 1f)] public float moonPhase = 0.58f;
    [Range(0.5f, 8f)] public float moonRadiusDeg = 3.2f;
    [Range(0f, 8f)] public float moonGain = 2.4f;
    [Range(0f, 3f)] public float haloGain = 1.0f;
    [Tooltip("Place the moon relative to the route heading so it is in view; off = fixed world azimuth.")] public bool moonFollowsRoute = true;
    [Tooltip("Degrees right of the route heading (sweeps +/- over the ride).")] public float moonAzimuthOffset = 22f;
    [Range(0f, 40f)] public float moonMinElevation = 14f;

    [Header("Clouds / glow / meteors")]
    [Range(0f, 1f)] public float cloudAmount = 0.55f;
    [Range(0.2f, 4f)] public float cloudScale = 1.3f;
    [Range(0f, 1f)] public float cloudNeon = 0.16f;
    [Range(0f, 3f)] public float cityGlow = 1.0f;
    [Tooltip("Mean seconds between shooting stars per channel (0 = none).")] public float meteorPeriod = 14f;
    [Range(0f, 20f)] public float meteorGain = 6f;

    public float Visibility { get; private set; }
    public string LastReport { get; private set; } = "";
    public Vector3 MoonDirection { get; private set; } = Vector3.up;

    const string DomeName = "Shunta Night Sky Dome";
    static readonly List<ShuntaNightSky> All = new List<ShuntaNightSky>();

    GameObject dome; Material mat; Mesh mesh;
    float lastKm = 0.3f;
    ShuntaRouteBuilder lastRoute;

    /// <summary>Hook called from <see cref="ShuntaLookDriver.ApplyKm"/>.</summary>
    public static void NotifyKm(ShuntaRouteBuilder route, float km)
    {
        for (int i = 0; i < All.Count; i++) if (All[i] != null) All[i].ApplyKm(route, km);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); Build(); }
    void OnDisable() { All.Remove(this); Teardown(); }

    // ------------------------------------------------------------------ build
    void Build()
    {
        Teardown();
        if (shader == null) shader = Shader.Find("MapleRide/Shunta/NightSky");
        if (shader == null) { LastReport = "FAILED: no shader MapleRide/Shunta/NightSky"; Debug.LogError("[shunta-sky] " + LastReport); return; }
        mat = new Material(shader) { name = "ShuntaNightSky", hideFlags = HideFlags.DontSave };
        mat.SetTexture("_StarTex", starMap); mat.SetTexture("_GlowTex", glowMap);
        mat.renderQueue = 3000;
        mesh = BuildSphere(48, 32);
        dome = new GameObject(DomeName) { hideFlags = HideFlags.DontSave };
        dome.transform.SetParent(transform, false);
        var mf = dome.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
        var mr = dome.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.allowOcclusionWhenDynamic = false;
        PushParams(Color.white, 0f, 0f);
        LastReport = $"[shunta-sky] dome built (stars {(starMap ? starMap.width + "x" + starMap.height : "none")}, glow {(glowMap ? glowMap.width + "x" + glowMap.height : "none")})";
    }

    void Teardown()
    {
        if (dome != null) DestroyNow(dome);
        if (mat != null) DestroyNow(mat);
        if (mesh != null) DestroyNow(mesh);
        dome = null; mat = null; mesh = null;
        // Destroy is deferred in play mode, so a Find/Destroy loop never advances.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == DomeName) DestroyNow(child.gameObject);
        }
    }
    static void DestroyNow(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    static Mesh BuildSphere(int lon, int lat)
    {
        var m = new Mesh { name = "ShuntaSkySphere", hideFlags = HideFlags.DontSave };
        var v = new Vector3[(lon + 1) * (lat + 1)];
        for (int y = 0; y <= lat; y++)
        {
            float a = Mathf.PI * y / lat - Mathf.PI * 0.5f;
            for (int x = 0; x <= lon; x++)
            {
                float b = 2f * Mathf.PI * x / lon;
                v[y * (lon + 1) + x] = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Sin(a), Mathf.Cos(b) * Mathf.Cos(a));
            }
        }
        var tri = new int[lon * lat * 6]; int k = 0;
        for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int i0 = y * (lon + 1) + x, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                tri[k++] = i0; tri[k++] = i2; tri[k++] = i1; tri[k++] = i1; tri[k++] = i2; tri[k++] = i3;
            }
        m.vertices = v; m.triangles = tri;
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 2e6f);   // never culled
        return m;
    }

    // ------------------------------------------------------------------ per frame
    void LateUpdate()
    {
        if (dome == null) return;
        Camera cam = Camera.main;
        if (cam != null) dome.transform.position = cam.transform.position;   // keeps culling bounds centred (shader is camera-relative anyway)
        if (mat != null)
        {
            float t = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartup);
            var s1 = mat.GetVector("_Sky1"); s1.x = t; s1.y = (rotationDegPerMinute * t / 60f) * Mathf.Deg2Rad; mat.SetVector("_Sky1", s1);
        }
    }

    // ------------------------------------------------------------------ km -> look
    static float VisFor(string tod)
    {
        switch (tod)
        {
            case "golden_dusk": return 0.0f;
            case "dusk": return 0.10f;
            case "blue_hour": return 0.55f;
            case "tunnel": return 0.6f;
            case "night": case "neon_night": case "deep_night": return 1f;
            case "pre_dawn": return 0.85f;
            case "sakura_dawn": return 0.6f;
            default: return 1f;
        }
    }
    static float GlowFor(string tod)
    {
        switch (tod)
        {
            case "golden_dusk": return 0.35f;
            case "dusk": return 0.5f;
            case "blue_hour": return 0.6f;
            case "tunnel": return 0.4f;
            case "night": return 0.65f;
            case "neon_night": return 0.95f;
            case "deep_night": return 0.6f;
            case "pre_dawn": return 0.55f;
            case "sakura_dawn": return 0.45f;
            default: return 0.6f;
        }
    }

    public void ApplyKm(ShuntaRouteBuilder route, float km)
    {
        if (mat == null || route == null || route.Course == null) return;
        lastKm = km; lastRoute = route;
        var c = route.Course; var z = c.ZoneAtKm(km); if (z == null) return;
        int zi = z.index - 1; float blend = 0.45f;
        float vis = VisFor(z.timeOfDay), glow = GlowFor(z.timeOfDay);
        Color col = z.Color32;
        // crossfade across zone borders
        ShuntaZone o = null; float t = 0f;
        if (zi > 0 && km - z.startKm < blend) { o = c.zones[zi - 1]; t = Mathf.SmoothStep(0f, 1f, 0.5f + (km - z.startKm) / (2f * blend)); }
        else if (zi < c.zones.Length - 1 && z.endKm - km < blend) { o = c.zones[zi + 1]; t = Mathf.SmoothStep(0f, 1f, 0.5f + (z.endKm - km) / (2f * blend)); }
        if (o != null)
        {
            vis = Mathf.Lerp(VisFor(o.timeOfDay), vis, t); glow = Mathf.Lerp(GlowFor(o.timeOfDay), glow, t);
            col = Color.Lerp(o.Color32, col, t);
        }
        if (visibilityOverride >= 0f) vis = visibilityOverride;
        Visibility = vis;
        PushParams(col, vis, glow);

        // moon from the time-of-day arc: opposite the sun, elevation mirrors the sun's depth
        var tod = ShuntaTimeOfDay.Sample(km, c.distanceKm);
        float elev = Mathf.Clamp(-tod.sunElevationDeg * 0.9f, moonMinElevation, 66f);
        var tan = route.TangentAtKm(km); tan.y = 0f;
        float heading = tan.sqrMagnitude > 1e-6f ? Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg : 0f;
        float u = Mathf.Clamp01(km / Mathf.Max(0.1f, c.distanceKm));
        float azDeg = moonFollowsRoute ? heading + moonAzimuthOffset * Mathf.Cos(u * 5.2f) : tod.sunAzimuthDeg + 180f;
        float ar = azDeg * Mathf.Deg2Rad, er = elev * Mathf.Deg2Rad;
        var md = new Vector3(Mathf.Sin(ar) * Mathf.Cos(er), Mathf.Sin(er), Mathf.Cos(ar) * Mathf.Cos(er));
        MoonDirection = md;
        float moonFade = tod.clockHour >= 12f
            ? Mathf.Clamp01(Mathf.InverseLerp(2f, -6f, tod.sunElevationDeg))
            : Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(Mathf.InverseLerp(-1f, -12f, tod.sunElevationDeg)));
        mat.SetVector("_MoonDir", new Vector4(md.x, md.y, md.z, Mathf.Tan(moonRadiusDeg * Mathf.Deg2Rad)));
        mat.SetVector("_MoonParams", new Vector4(moonPhase, moonGain, haloGain, moonFade));
    }

    void PushParams(Color zoneCol, float vis, float glowStrength)
    {
        if (mat == null) return;
        var s1 = mat.GetVector("_Sky1");
        mat.SetVector("_Sky0", new Vector4(vis, starGain, twinkle, milkyWayGain));
        mat.SetVector("_Sky1", new Vector4(s1.x, s1.y, ShuntaLookKit.NitsPerRel, 0.985f));
        mat.SetVector("_CityColor", new Vector4(zoneCol.r, zoneCol.g, zoneCol.b, glowStrength * cityGlow));
        var pink = Color.Lerp(new Color(1f, 0.28f, 0.66f), zoneCol, 0.35f);
        var cyan = Color.Lerp(new Color(0.15f, 0.85f, 1f), zoneCol, 0.2f);
        mat.SetVector("_NeonA", pink); mat.SetVector("_NeonB", cyan);
        mat.SetVector("_CloudParams", new Vector4(cloudAmount, cloudScale, cloudNeon, 0.004f));
        mat.SetVector("_MeteorParams", new Vector4(Mathf.Max(meteorPeriod, 0.1f), meteorPeriod > 0f ? meteorGain : 0f, 0f, 0f));
        if (mat.GetVector("_MoonDir") == Vector4.zero) mat.SetVector("_MoonDir", new Vector4(0f, 0.6f, 0.8f, Mathf.Tan(moonRadiusDeg * Mathf.Deg2Rad)));
    }

    /// <summary>Re-push inspector tuning without waiting for the next km change.</summary>
    [ContextMenu("Refresh")]
    public void Refresh() { if (lastRoute != null) ApplyKm(lastRoute, lastKm); }
    void OnValidate() { if (isActiveAndEnabled && mat != null) Refresh(); }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Shunta Metro "the look" driver. Sits next to <see cref="ShuntaRouteBuilder"/>; on enable it
///  (1) swaps the road ribbon to one submesh + wet/glossy material per zone (wetter in the rain zones),
///  (2) generates neon buildings, strips, streaks, tunnel, landmarks (<see cref="ShuntaLookScenery"/>), none of it saved in the scene,
///  (3) drives a runtime global Volume (gradient sky, fixed exposure, fog, bloom, tonemap) + the sun/moon light from the
///      zone <c>timeOfDay</c> palette, blended across zone borders by km. In play it follows <see cref="follow"/>
///      (the rider); in the editor it shows <see cref="previewKm"/>.
/// The look runs at a fixed camera EV (<see cref="ShuntaLookKit.EV"/>) so all lights/emissives are display-referred.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class ShuntaLookDriver : MonoBehaviour
{
    public ShuntaRouteBuilder route;
    [Tooltip("Rider / camera rig to follow in play. Empty = use previewKm.")]
    public Transform follow;
    [Min(0f)] public float previewKm = 0.3f;
    public bool buildScenery = true;
    [Tooltip("Metres of blending either side of a zone border.")]
    public float blendMetres = 450f;

    const string GenName = "Shunta Look (generated)";

    // ---------------------------------------------------------------- palettes
    public struct Palette
    {
        public Color sky0, sky1, sky2;      // zenith, horizon, below
        public Color sun; public float sunRel; public float pitch;
        public Color fog; public float fogDist; public float neon;
        public float bloom, saturation;

        public static Palette Lerp(Palette a, Palette b, float t) => new Palette
        {
            sky0 = Color.Lerp(a.sky0, b.sky0, t), sky1 = Color.Lerp(a.sky1, b.sky1, t), sky2 = Color.Lerp(a.sky2, b.sky2, t),
            sun = Color.Lerp(a.sun, b.sun, t), sunRel = Mathf.Lerp(a.sunRel, b.sunRel, t), pitch = Mathf.Lerp(a.pitch, b.pitch, t),
            fog = Color.Lerp(a.fog, b.fog, t), fogDist = Mathf.Lerp(a.fogDist, b.fogDist, t), neon = Mathf.Lerp(a.neon, b.neon, t),
            bloom = Mathf.Lerp(a.bloom, b.bloom, t), saturation = Mathf.Lerp(a.saturation, b.saturation, t),
        };
    }

    static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

    public static Palette PaletteFor(string timeOfDay)
    {
        switch (timeOfDay)
        {
            case "golden_dusk": return new Palette { sky0 = C(.10f, .17f, .45f), sky1 = C(1f, .55f, .30f), sky2 = C(.45f, .20f, .18f), sun = C(1f, .62f, .32f), sunRel = 1.1f, pitch = 9f, fog = C(.9f, .5f, .35f), fogDist = 2500f, neon = .5f, bloom = .25f, saturation = 10f };
            case "dusk": return new Palette { sky0 = C(.06f, .09f, .30f), sky1 = C(.85f, .30f, .30f), sky2 = C(.25f, .10f, .15f), sun = C(1f, .45f, .28f), sunRel = .6f, pitch = 4f, fog = C(.5f, .22f, .25f), fogDist = 2200f, neon = .75f, bloom = .3f, saturation = 12f };
            case "blue_hour": return new Palette { sky0 = C(.02f, .04f, .20f), sky1 = C(.12f, .20f, .50f), sky2 = C(.10f, .08f, .20f), sun = C(.5f, .6f, 1f), sunRel = .34f, pitch = 25f, fog = C(.12f, .15f, .35f), fogDist = 1800f, neon = 1.1f, bloom = .35f, saturation = 12f };
            case "tunnel": return new Palette { sky0 = C(.01f, .02f, .03f), sky1 = C(.03f, .09f, .11f), sky2 = C(.03f, .08f, .10f), sun = C(.4f, .7f, .8f), sunRel = .02f, pitch = 60f, fog = C(.03f, .08f, .10f), fogDist = 500f, neon = 1.25f, bloom = .4f, saturation = 15f };
            case "night": return new Palette { sky0 = C(.005f, .01f, .05f), sky1 = C(.03f, .04f, .12f), sky2 = C(.04f, .03f, .08f), sun = C(.55f, .65f, 1f), sunRel = .24f, pitch = 45f, fog = C(.05f, .05f, .12f), fogDist = 1600f, neon = 1.4f, bloom = .4f, saturation = 15f };
            case "neon_night": return new Palette { sky0 = C(.01f, .005f, .06f), sky1 = C(.12f, .04f, .20f), sky2 = C(.15f, .04f, .15f), sun = C(.6f, .5f, 1f), sunRel = .22f, pitch = 40f, fog = C(.10f, .04f, .16f), fogDist = 1000f, neon = 1.8f, bloom = .5f, saturation = 20f };
            case "deep_night": return new Palette { sky0 = C(.002f, .004f, .025f), sky1 = C(.015f, .02f, .07f), sky2 = C(.02f, .02f, .05f), sun = C(.5f, .6f, 1f), sunRel = .18f, pitch = 50f, fog = C(.02f, .025f, .07f), fogDist = 2200f, neon = 1.6f, bloom = .45f, saturation = 15f };
            case "pre_dawn": return new Palette { sky0 = C(.03f, .06f, .25f), sky1 = C(.35f, .35f, .60f), sky2 = C(.60f, .35f, .40f), sun = C(.7f, .7f, 1f), sunRel = .2f, pitch = 3f, fog = C(.3f, .3f, .5f), fogDist = 3500f, neon = .9f, bloom = .35f, saturation = 12f };
            case "sakura_dawn": return new Palette { sky0 = C(.15f, .25f, .55f), sky1 = C(1f, .70f, .80f), sky2 = C(1f, .55f, .55f), sun = C(1f, .75f, .75f), sunRel = .9f, pitch = 5f, fog = C(1f, .70f, .80f), fogDist = 4000f, neon = .35f, bloom = .3f, saturation = 12f };
            default: return PaletteFor("night");
        }
    }

    /// <summary>Palette at a km, crossfaded over <paramref name="blendKm"/> either side of each zone border.</summary>
    public static Palette PaletteAtKm(ShuntaCourseData c, float km, float blendKm)
    {
        var z = c.ZoneAtKm(km); if (z == null) return PaletteFor("night");
        var cur = PaletteFor(z.timeOfDay);
        int zi = z.index - 1;
        if (zi > 0 && km - z.startKm < blendKm)
        {
            float t = 0.5f + (km - z.startKm) / (2f * blendKm);
            return Palette.Lerp(PaletteFor(c.zones[zi - 1].timeOfDay), cur, Mathf.SmoothStep(0f, 1f, t));
        }
        if (zi < c.zones.Length - 1 && z.endKm - km < blendKm)
        {
            float t = 0.5f + (z.endKm - km) / (2f * blendKm);
            return Palette.Lerp(PaletteFor(c.zones[zi + 1].timeOfDay), cur, Mathf.SmoothStep(0f, 1f, t));
        }
        return cur;
    }

    /// <summary>0 = dry, 1 = soaked. Drives the road smoothness range per zone.</summary>
    public static float Wetness(ShuntaZone z)
    {
        float w;
        switch (z.surface)
        {
            case "wet_asphalt": w = 1f; break;
            case "tunnel_concrete": w = .35f; break;
            case "bridge_deck": w = .5f; break;
            case "expressway": w = .45f; break;
            default: w = .3f; break;
        }
        return Mathf.Clamp01(Mathf.Max(w, (1f - z.grip) / 0.22f));
    }

    // ---------------------------------------------------------------- state
    Volume vol; VolumeProfile profile; Light sun; HDAdditionalLightData sunHd; Light fill; HDAdditionalLightData fillHd;
    GameObject gen;
    ShuntaLookScenery.Result built;
    readonly List<Material> roadMats = new List<Material>();
    Mesh roadMesh;
    Texture2D roadAlbedo, roadMask;
    float lastKm = -99f;
    public string LastReport { get; private set; } = "";

    void OnEnable() { BuildAll(); }
    void OnDisable() { Teardown(); }

    void Update()
    {
        if (route == null || route.Course == null) return;
        float km = previewKm;
        if (follow != null && Application.isPlaying) km = NearestKm(follow.position);
        if (fill != null && follow != null) fill.transform.position = follow.position + Vector3.up * 2.6f - follow.forward * 1.5f + follow.right * 1.2f;
        if (Mathf.Abs(km - lastKm) > 0.005f || !Application.isPlaying) ApplyKm(km);
    }

    float hint;
    float NearestKm(Vector3 p)
    {
        var pos = route.Positions; if (pos.Length == 0) return 0f;
        int n = pos.Length, start = 0, end = n;
        if (lastKm >= 0f) { int c = Mathf.Clamp(Mathf.RoundToInt(lastKm * 1000f / route.sampleSpacing), 0, n - 1); start = Mathf.Max(0, c - 40); end = Mathf.Min(n, c + 40); }
        float best = float.MaxValue; int bi = start;
        for (int i = start; i < end; i++) { float d = (pos[i] - p).sqrMagnitude; if (d < best) { best = d; bi = i; } }
        hint = best; return route.Km[bi];
    }

    [ContextMenu("Rebuild Look")]
    public void BuildAll()
    {
        if (route == null) route = GetComponent<ShuntaRouteBuilder>() ?? FindFirstObjectByType<ShuntaRouteBuilder>();
        if (route == null) { LastReport = "no ShuntaRouteBuilder"; return; }
        Teardown();
        var ribbon = route.transform.Find("Shunta Road Ribbon");
        if (route.Course == null || ribbon == null || ribbon.GetComponent<MeshFilter>() == null || ribbon.GetComponent<MeshFilter>().sharedMesh == null || ribbon.GetComponent<MeshFilter>().sharedMesh.name != "ShuntaRoad") route.Rebuild();
        var c = route.Course; if (c == null) return;
        ShuntaLookScenery.ResetCaches();

        BuildRoad(c);
        gen = new GameObject(GenName) { hideFlags = HideFlags.DontSave };
        gen.transform.SetParent(transform, false);
        built = new ShuntaLookScenery.Result();
        if (buildScenery) built = ShuntaLookScenery.Build(route, gen.transform);
        BuildVolumeAndLight();
        lastKm = -99f; ApplyKm(previewKm);
        LastReport = $"[shunta-look] road mats {roadMats.Count}, meshes {built.objects.Count}, buildings {built.buildings}, signs {built.signs}, strips {built.strips}, streaks {built.streaks}, props {built.props}, neon mats {built.neon.Count}";
        Debug.Log(LastReport);
    }

    void Teardown()
    {
        var resources = new HashSet<Object>();
        if (gen != null)
        {
            foreach (var filter in gen.GetComponentsInChildren<MeshFilter>(true))
                CollectGenerated(filter.sharedMesh, resources);
            foreach (var renderer in gen.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    CollectMaterial(material, resources);
        }
        if (built != null)
            foreach (var neon in built.neon) CollectMaterial(neon.mat, resources);
        foreach (var resource in resources) DestroyNow(resource);
        if (gen != null) DestroyNow(gen);
        gen = null; built = null;
        foreach (var m in roadMats) if (m != null) DestroyNow(m);
        roadMats.Clear();
        if (roadMesh != null) DestroyNow(roadMesh);
        if (roadAlbedo != null) DestroyNow(roadAlbedo);
        if (roadMask != null) DestroyNow(roadMask);
        roadMesh = null; roadAlbedo = roadMask = null;
        if (vol != null) DestroyNow(vol.gameObject);
        if (sun != null) DestroyNow(sun.gameObject);
        if (fill != null) DestroyNow(fill.gameObject);
        fill = null; fillHd = null;
        if (profile != null) DestroyNow(profile);
        vol = null; sun = null; profile = null;
        // also clear orphans after a domain reload
        // Destroy is deferred in play mode; visit each child once instead of rediscovering it.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == GenName || child.name == "Shunta Look Volume" ||
                child.name == "Shunta Rider Fill" || child.name == "Shunta Sun/Moon")
                DestroyNow(child.gameObject);
        }
    }

    static void DestroyNow(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    static void CollectGenerated(Object resource, HashSet<Object> resources)
    {
        // Procedural look resources are DontSave; imported assets and primitive meshes are shared.
        if (resource != null && (resource.hideFlags & HideFlags.DontSave) == HideFlags.DontSave)
            resources.Add(resource);
    }

    static void CollectMaterial(Material material, HashSet<Object> resources)
    {
        if (material == null || (material.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) return;
        resources.Add(material);
        foreach (var property in material.GetTexturePropertyNames())
            CollectGenerated(material.GetTexture(property), resources);
    }

    // ---------------------------------------------------------------- road
    void BuildRoad(ShuntaCourseData c)
    {
        var ribbon = route.transform.Find("Shunta Road Ribbon");
        if (ribbon == null) return;
        var mf = ribbon.GetComponent<MeshFilter>(); var mr = ribbon.GetComponent<MeshRenderer>();
        var src = mf.sharedMesh; if (src == null) return;
        ShuntaLookKit.MakeRoadTextures(out var albedo, out var mask);
        roadAlbedo = albedo; roadMask = mask;
        int zc = c.zones.Length;
        var tris = src.triangles; int quads = tris.Length / 6;
        var lists = new List<int>[zc];
        for (int i = 0; i < zc; i++) lists[i] = new List<int>();
        for (int q = 0; q < quads; q++)
        {
            int zi = Mathf.Clamp(route.ZoneIndex[Mathf.Min(q + 1, route.ZoneIndex.Length - 1)] - 1, 0, zc - 1);
            for (int k = 0; k < 6; k++) lists[zi].Add(tris[q * 6 + k]);
        }
        var mesh = new Mesh { name = "ShuntaRoadLook", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        roadMesh = mesh;
        mesh.vertices = src.vertices; mesh.normals = src.normals; mesh.uv = src.uv;
        mesh.subMeshCount = zc;
        var mats = new Material[zc];
        for (int i = 0; i < zc; i++)
        {
            mesh.SetTriangles(lists[i], i);
            var z = c.zones[i]; float wet = Wetness(z);
            var m = ShuntaLookKit.Lit("ShuntaRoad_" + z.id, Color.white, 0.5f);
            // wet asphalt is darker; dry stays lighter and rougher. Tint keeps the base colour non-black (HDRP gotcha 1).
            // PBR texture set first (per-surface variants, 9 x 18 m tile); procedural fallback if a texture is missing
            if (!ShuntaLookKit.ApplyRoadTextures(m, z, wet, route.roadWidth))
            {
                float tint = Mathf.Lerp(1.15f, 0.62f, wet);
                ShuntaLookKit.SetBaseMap(m, albedo, new Color(tint, tint, tint * 1.04f, 1f));
                ShuntaLookKit.SetMaskMap(m, mask, 0.16f, Mathf.Lerp(0.40f, 0.55f, wet));
                m.SetFloat("_NormalScale", 1f);
            }
            roadMats.Add(m); mats[i] = m;
        }
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        mf.sharedMesh = mesh; mr.sharedMaterials = mats;
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------------------------------------------------------- volume + sun
    void BuildVolumeAndLight()
    {
        var vgo = new GameObject("Shunta Look Volume") { hideFlags = HideFlags.DontSave };
        vgo.transform.SetParent(transform, false);
        vol = vgo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 50f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.hideFlags = HideFlags.DontSave;
        // components are real, in-memory instances (never a hollow profile: gotcha 2)
        var ve = profile.Add<VisualEnvironment>(true); ve.skyType.value = (int)SkyType.Gradient; ve.skyAmbientMode.value = SkyAmbientMode.Dynamic;
        var sky = profile.Add<GradientSky>(true);
        sky.skyIntensityMode.value = SkyIntensityMode.Exposure; sky.exposure.value = ShuntaLookKit.EV; sky.gradientDiffusion.value = 4f;
        var exp = profile.Add<Exposure>(true); exp.mode.value = ExposureMode.Fixed; exp.fixedExposure.value = ShuntaLookKit.EV;
        var fog = profile.Add<Fog>(true); fog.enabled.value = true; fog.colorMode.value = FogColorMode.ConstantColor; fog.enableVolumetricFog.value = true; fog.depthExtent.value = 200f; fog.anisotropy.value = 0.55f; fog.albedo.value = new Color(0.62f, 0.70f, 0.85f);   // 2026-10-04 photoreal pass: neon/streetlight shafts and halos in the night air
         fog.maxFogDistance.value = 12000f;
        var water = profile.Add<WaterRendering>(true); water.enable.value = true; water.triangleSize.value = 32f;   // required for the Shunta ocean (ShuntaOcean)
        var bloom = profile.Add<Bloom>(true); bloom.threshold.value = 0.9f; bloom.intensity.value = 0.4f; bloom.scatter.value = 0.75f;
        var tm = profile.Add<Tonemapping>(true); tm.mode.value = TonemappingMode.ACES;
        var ca = profile.Add<ColorAdjustments>(true); ca.saturation.value = 15f; ca.contrast.value = 8f; ca.postExposure.Override(0.45f);   // 2026-10-04: streets were near-black canyons
        // 2026-10-04: reflections only where the surface is really wet. The road/ground/pavement masks now keep dry smoothness <= ~.6, so a
        // .80 floor leaves screen-space reflections to the puddles alone (the user: "dont make shunta road glass reflective").
        var ssr = profile.Add<ScreenSpaceReflection>(true); ssr.enabled.value = true; ssr.reflectSky.value = true; ssr.minSmoothness = 0.9f; ssr.smoothnessFadeStart = 0.93f; ssr.reflectSky.value = false;
        // fidelity: contact shadowing under cars, riders, kerbs and awnings, and a light vignette to hold the eye on the road
        var ao = profile.Add<ScreenSpaceAmbientOcclusion>(true); ao.intensity.Override(0.75f); ao.radius.Override(1.6f);
        var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.20f); vig.smoothness.Override(0.6f);
        // 2026-10-04 photoreal pass: camera-ish imperfections + contact shadows + lens response. Kept subtle; all optional volume overrides.
        var grain = profile.Add<FilmGrain>(true); grain.type.value = FilmGrainLookup.Thin2; grain.intensity.value = 0.22f; grain.response.value = 0.8f;
        var mb = profile.Add<MotionBlur>(true); mb.intensity.value = 0.35f;
        var cab = profile.Add<ChromaticAberration>(true); cab.intensity.value = 0.07f;
        var flare = profile.Add<ScreenSpaceLensFlare>(true); flare.intensity.value = 0.5f; flare.streaksIntensity.value = 0.6f; flare.streaksThreshold.value = 0.4f;
        var cs = profile.Add<ContactShadows>(true); cs.enable.value = true; cs.length.value = 0.25f; cs.opacity.value = 0.9f; cs.maxDistance.value = 60f;
        vol.sharedProfile = profile;

        var lgo = new GameObject("Shunta Sun/Moon") { hideFlags = HideFlags.DontSave };
        lgo.transform.SetParent(transform, false);
        sun = lgo.AddComponent<Light>(); sun.type = LightType.Directional; sun.shadows = LightShadows.Soft;
        sunHd = lgo.GetComponent<HDAdditionalLightData>() ?? lgo.AddComponent<HDAdditionalLightData>();
        sunHd.EnableShadows(true);
        RenderSettings.sun = sun;

        var fgo = new GameObject("Shunta Rider Fill") { hideFlags = HideFlags.DontSave };
        fgo.transform.SetParent(transform, false);
        fill = fgo.AddComponent<Light>(); fill.type = LightType.Point; fill.range = 14f; fill.shadows = LightShadows.None;
        fillHd = fgo.GetComponent<HDAdditionalLightData>() ?? fgo.AddComponent<HDAdditionalLightData>();
        fillHd.SetIntensity(70000f, LightUnit.Lumen);
    }

    /// <summary>Apply the zone palette for <paramref name="km"/>: sky, fog, bloom, light, neon intensity.</summary>
    public void ApplyKm(float km)
    {
        if (route == null || route.Course == null || profile == null) return;
        lastKm = km;
        var p = PaletteAtKm(route.Course, km, blendMetres / 1000f);
        profile.TryGet(out GradientSky sky); profile.TryGet(out Fog fog); profile.TryGet(out Bloom bloom); profile.TryGet(out ColorAdjustments ca);
        if (sky != null) { sky.top.value = p.sky0; sky.middle.value = p.sky1; sky.bottom.value = p.sky2; }
        if (fog != null) { fog.color.value = p.fog; fog.meanFreePath.value = p.fogDist; }
        if (bloom != null) bloom.intensity.value = p.bloom;
        if (ca != null) ca.saturation.value = p.saturation;
        if (sun != null)
        {
            sun.color = p.sun; sun.intensity = Mathf.Max(p.sunRel, 0.001f) * ShuntaLookKit.LuxPerRel;
            sun.transform.rotation = Quaternion.Euler(p.pitch, route.TangentAtKm(km).x > 0 ? 250f : 70f, 0f);
        }
        if (fill != null) fill.color = Color.Lerp(p.fog, Color.white, 0.6f);
        ShuntaNightSky.NotifyKm(route, km);   // sky hook (claude, 2026-10-03): night sky follows the applied km
        if (built != null)
            foreach (var n in built.neon)
                if (n.mat != null) ShuntaLookKit.SetEmissive(n.mat, n.rgb, n.rel * p.neon);
    }
}

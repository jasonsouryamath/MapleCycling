using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>Region-scoped presentation for both authoring and additive playable scenes.
/// Copies materials at runtime; never rewrites another region's shared assets.</summary>
[DefaultExecutionOrder(900)]
public sealed class NagisaCoastalLook : MonoBehaviour
{
    public static NagisaCoastalLook Instance { get; private set; }
    public static bool CorrectionsEnabled = true;
    public int MaterialCount => _copies.Count;
    private RegionDirector _regions;
    private Volume _volume;
    private VolumeProfile _profile;
    private bool _active;
    private float _next;
    private readonly Dictionary<Material, Material> _copies = new Dictionary<Material, Material>();
    private readonly Dictionary<MeshRenderer, Material[]> _originals = new Dictionary<MeshRenderer, Material[]>();
    private Quaternion _sunRotation;
    private Color _sunColor;
    private float _sunIntensity, _fillIntensity, _shadowStrength, _shadowDimmer;
    private LightShadows _shadows;
    private Texture2D _asphalt;
    private Shader _foliageShader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; CorrectionsEnabled = true; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("~Nagisa Coastal Look");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<NagisaCoastalLook>();
    }

    private void Awake()
    {
        _volume = gameObject.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 55f; // Region=50; weather=60/61 retains cloud/fog authority.
        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volume.sharedProfile = _profile;
        _volume.enabled = false;
        var exposure = _profile.Add<Exposure>();
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(0.55f); // Protect highlights locally; preserve rider/HUD exposure.
        _profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
        var grade = _profile.Add<ColorAdjustments>();
        grade.saturation.Override(-3f); grade.contrast.Override(2f); grade.postExposure.Override(0f);
        _profile.Add<Bloom>().intensity.Override(0.025f);
        _profile.Add<WhiteBalance>().temperature.Override(0f);
        var shadows = _profile.Add<HDShadowSettings>();
        shadows.maxShadowDistance.Override(220f);
        shadows.cascadeShadowSplitCount.Override(4);
        shadows.cascadeShadowSplit0.Override(0.08f);
        shadows.cascadeShadowSplit1.Override(0.22f);
        shadows.cascadeShadowSplit2.Override(0.50f);
        var contact = _profile.Add<ContactShadows>();
        contact.enable.Override(true); contact.length.Override(0.12f);
        contact.opacity.Override(0.65f); contact.maxDistance.Override(35f);
        var ao = _profile.Add<ScreenSpaceAmbientOcclusion>();
        ao.intensity.Override(0.55f); ao.radius.Override(0.65f);
        // Custom forward shaders sample cascade shadows but not HDRP AO/contact buffers.
        // These overrides improve native HDRP surfaces only; do not advertise them as PBR conversion.
        SceneManager.sceneLoaded += SceneLoaded;
        SceneManager.sceneUnloaded += SceneUnloaded;
        RenderPipelineManager.beginCameraRendering += BindCoastalAmbient;
    }

    private void LateUpdate()
    {
        if (_regions == null && Time.unscaledTime >= _next)
        {
            _next = Time.unscaledTime + 0.5f;
            _regions = FindFirstObjectByType<RegionDirector>();
        }
        bool wanted = CorrectionsEnabled && _regions != null &&
                      _regions.currentRegionId == NagisaBayLook.RegionId;
        if (wanted != _active)
        {
            if (wanted) Activate(); else Deactivate();
        }
        if (!_active) return;
        var sun = _regions.keyLight;
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(42f, 112f, 0f);
            sun.color = new Color(1f, 0.975f, 0.92f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.86f;
        }
        if (_regions.fillLight != null) _regions.fillLight.intensity = 0.20f;
    }

    private void Activate()
    {
        _active = true;
        _volume.enabled = true;
        var sun = _regions.keyLight;
        if (sun != null)
        {
            _sunRotation = sun.transform.rotation; _sunColor = sun.color;
            _sunIntensity = sun.intensity; _shadows = sun.shadows; _shadowStrength = sun.shadowStrength;
            var hd = sun.GetComponent<HDAdditionalLightData>();
            if (hd != null) { _shadowDimmer = hd.shadowDimmer; hd.SetShadowDimmer(0.9f); }
        }
        if (_regions.fillLight != null) _fillIntensity = _regions.fillLight.intensity;
        for (int i = 0; i < SceneManager.sceneCount; i++) ApplyScene(SceneManager.GetSceneAt(i));
        Debug.Log($"[nagisa-coastal] active: sun 42deg, fixed EV0.55, {_copies.Count} material copies; weather owns cloud/fog.");
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) { if (_active) ApplyScene(scene); }
    private void BindCoastalAmbient(ScriptableRenderContext context, Camera camera)
    {
        if (!_active) return;
        MapleRideSunBinder.Bind();
        // Calibrated neutral sky bounce for the custom gamma-space shader family. The
        // saturated gradient sky probe otherwise dominates horizontal surfaces with blue.
        Shader.SetGlobalVector("_MR_AmbSky", new Vector4(0.52f, 0.59f, 0.70f, 1f));
        Shader.SetGlobalVector("_MR_AmbEquator", new Vector4(0.44f, 0.47f, 0.50f, 1f));
        Shader.SetGlobalVector("_MR_AmbGround", new Vector4(0.26f, 0.27f, 0.24f, 1f));
    }
    private void SceneUnloaded(Scene scene)
    {
        var retired = new List<MeshRenderer>();
        foreach (var renderer in _originals.Keys)
            if (renderer == null || renderer.gameObject.scene == scene) retired.Add(renderer);
        foreach (var renderer in retired) _originals.Remove(renderer);
    }

    private void ApplyScene(Scene scene)
    {
        bool cell = scene.path.Replace('\\', '/').Contains("/Cells/nagisa_bay/");
        foreach (var root in scene.GetRootGameObjects())
        {
            if (!cell && root.name != NagisaBayLook.EnvironmentRoot) continue;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Rider/people appearances are explicitly preserved, including rigid bike parts.
                if (_originals.ContainsKey(renderer) || IsActor(renderer.transform)) continue;
                var originals = renderer.sharedMaterials;
                var slots = (Material[])originals.Clone();
                bool changed = false;
                for (int j = 0; j < slots.Length; j++)
                {
                    var source = slots[j];
                    if (source == null || source.shader == null) continue;
                    string contextName = source.name + " " + renderer.name;
                    for (var t = renderer.transform.parent; t != null; t = t.parent) contextName += " " + t.name;
                    string lower = contextName.ToLowerInvariant();
                    bool tree = lower.Contains("canopytree") || lower.Contains("palm") || lower.Contains("street trees");
                    if (!source.shader.name.StartsWith("MapleRide/HDRP/")) continue;
                    if (!_copies.TryGetValue(source, out var copy))
                    {
                        copy = new Material(source) { name = source.name + " [Coastal]", enableInstancing = source.enableInstancing };
                        string tuneShader = source.shader.name;
                        if (source.shader.name == "MapleRide/HDRP/Foliage")
                        {
                            if (_foliageShader == null) _foliageShader = Resources.Load<Shader>("Rendering/NagisaCoastalFoliage");
                            if (_foliageShader != null) copy.shader = _foliageShader;
                        }
                        if (tree) Debug.Log($"[nagisa-coastal] tree material {renderer.name}/{source.name}: {source.shader.name} -> {copy.shader.name}");
                        Tune(copy, contextName, tuneShader);
                        if (tree && copy.HasProperty("_CoastalMatteFloor")) Debug.Log($"[nagisa-coastal] leaf controls {source.name}: tint={copy.GetColor("_Color")} floor={copy.GetColor("_CoastalMatteFloor")} ambient={copy.GetFloat("_CoastalAmbient")}");
                        if (source.shader.name == "MapleRide/HDRP/Road") copy.SetTexture("_MainTex", AsphaltAggregate());
                        _copies.Add(source, copy);
                    }
                    slots[j] = copy; changed = true;
                }
                if (!changed) continue;
                _originals.Add(renderer, originals);
                renderer.sharedMaterials = slots;
            }
        }
    }

    private static bool IsActor(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("people") || n.Contains("pedestrian") || n.Contains("rider") || n.Contains("cyclist") ||
                n.Contains("sunbather") || n.Contains("runner") || n.Contains("person")) return true;
            if (t.GetComponent<NPCCyclist>() != null) return true;
        }
        return false;
    }

    private static void F(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
    private static void C(Material m, string p, Color v) { if (m.HasProperty(p)) m.SetColor(p, v); }
    private Texture2D AsphaltAggregate()
    {
        if (_asphalt != null) return _asphalt;
        const int size = 256;
        _asphalt = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
        { name = "Nagisa Coastal Asphalt Aggregate", wrapMode = TextureWrapMode.Repeat,
          filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        var pixels = new Color[size * size];
        uint seed = 173u;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            seed = seed * 1664525u + 1013904223u;
            float grain = (seed >> 8) / 16777215f;
            // ~2 cm grain at the existing 4.5 m tile; low contrast coarse modulation
            // remains periodic at the tile seam, with no cloudy large-scale albedo.
            float broad = Mathf.Sin(x * Mathf.PI * 2f / size) * Mathf.Sin(y * Mathf.PI * 4f / size);
            float value = 0.31f + grain * 0.07f + broad * 0.006f;
            pixels[y * size + x] = new Color(value * 0.98f, value, value * 1.01f, 1f);
        }
        _asphalt.SetPixels(pixels); _asphalt.Apply(true, true);
        return _asphalt;
    }
    private static void Tune(Material m, string name, string shader)
    {
        if (shader.Contains("Swash")) return; // Preserve the shoreline/wet-sand owner's appearance.
        if (shader.Contains("Ocean"))
        {
            // Maldivian palette (2026-10-02, user: "mesmerizing blue ... vast ... photorealistic"): glowing
            // aqua-turquoise over the shallows grading to a deep saturated azure/sapphire offshore. The old deep
            // navy read as a dark flat band; clearer water (lower absorption) lets the sand show through, and a
            // pale hazy horizon reflection gives the sea its sense of distance.
            C(m, "_DeepColor", new Color(0.015f, 0.27f, 0.66f));
            C(m, "_ShallowTint", new Color(0.06f, 0.86f, 0.82f));
            C(m, "_SssColor", new Color(0.10f, 0.95f, 0.78f));
            C(m, "_SkyZenith", new Color(0.30f, 0.58f, 0.95f));
            C(m, "_SkyHorizon", new Color(0.80f, 0.90f, 0.97f));
            F(m, "_AbsorbScale", 0.55f);
            F(m, "_SkyGain", 0.95f); F(m, "_SpecGain", 0.85f);
            F(m, "_AmbientWeight", 0.95f); F(m, "_WhitecapAmount", 0.12f);
            F(m, "_DetailStrength", 0.9f);
            return;
        }
        bool foliage = shader.Contains("Foliage");
        C(m, "_CoastalMatteFloor", new Color(0.20f, 0.32f, 0.12f));
        F(m, "_CoastalAmbient", 0.5f);
        F(m, "_RampSteps", 6f); F(m, "_RampSmooth", 0.32f);
        F(m, "_RimStrength", foliage ? 0.08f : 0.035f);
        F(m, "_ShadowSoft", 0.20f); F(m, "_ShadowAmbient", 0.7f);
        F(m, "_AmbientStrength", 0.9f);
        C(m, "_ShadeColor", new Color(0.72f, 0.77f, 0.82f));
        F(m, "_ShadeStrength", 0.72f);
        // Foliage has no ambient term: its tinted shade floor is its sky-bounce substitute.
        if (foliage) { C(m, "_ShadeColor", new Color(0.64f, 0.72f, 0.68f)); F(m, "_ShadeStrength", 0.65f); F(m, "_Translucency", 0.25f); }
        F(m, "_HighlightRolloff", 0.7f);
        string lower = name.ToLowerInvariant();
        bool vegetation = foliage || lower.Contains("hedge") || lower.Contains("canopy") || lower.Contains("palm") || lower.Contains("tree") || lower.Contains("fern");
        if (vegetation)
        {
            bool bark = lower.Contains("trunk") || lower.Contains("bark");
            C(m, "_MatteFloor", bark ? new Color(0.26f, 0.20f, 0.14f) : new Color(0.20f, 0.38f, 0.13f));
        }
        if (shader == "MapleRide/HDRP/Road")
        {
            F(m, "_MacroAmount", 0.16f); F(m, "_PatchAmount", 0.12f);
            F(m, "_TrackDarken", 0.16f); F(m, "_TrackPolish", 0.28f);
            F(m, "_Gloss", 0.12f); F(m, "_SpecStrength", 0.08f);
            F(m, "_RoughVariation", 0.35f); F(m, "_CelAmount", 0f);
        }
        if (name.Contains("BikeLane")) C(m, "_Color", new Color(0.82f, 0.92f, 0.88f));
        if (name.Contains("Ground") || name.Contains("Grass"))
        {
            string tint = m.HasProperty("_GrassColor") ? "_GrassColor" : "_Color";
            if (m.HasProperty(tint))
            {
                Color c = m.GetColor(tint); float gray = c.grayscale;
                C(m, tint, Color.Lerp(c, new Color(gray, gray, gray, c.a), 0.22f) * new Color(0.94f, 0.94f, 0.94f, 1f));
            }
        }
    }

    private void Deactivate()
    {
        _volume.enabled = false;
        foreach (var pair in _originals) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
        _originals.Clear();
        foreach (var copy in _copies.Values) if (copy != null) Destroy(copy);
        _copies.Clear();
        if (_asphalt != null) { Destroy(_asphalt); _asphalt = null; }
        if (_regions != null && _regions.keyLight != null)
        {
            var sun = _regions.keyLight;
            var hd = sun.GetComponent<HDAdditionalLightData>();
            if (hd != null) hd.SetShadowDimmer(_shadowDimmer);
            if (_regions.currentRegionId == NagisaBayLook.RegionId)
            {
                sun.transform.rotation = _sunRotation; sun.color = _sunColor; sun.intensity = _sunIntensity;
                sun.shadows = _shadows; sun.shadowStrength = _shadowStrength;
                if (_regions.fillLight != null) _regions.fillLight.intensity = _fillIntensity;
            }
        }
        _active = false;
        MapleRideSunBinder.Bind();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        RenderPipelineManager.beginCameraRendering -= BindCoastalAmbient;
        if (_active) Deactivate();
        if (_profile != null) Destroy(_profile);
        if (Instance == this) Instance = null;
    }
}

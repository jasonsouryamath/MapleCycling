using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Pushes the scene's key directional light and trilight ambient into the globals the
/// hand-written MapleRide HDRP shaders read (see MapleRideHDRPCommon.hlsl).
///
/// HDRP does not populate the Built-in <c>_WorldSpaceLightPos0</c> / <c>_LightColor0</c>
/// globals, and its own light data is only reachable from generated HDRP passes, so a
/// hand-written pass has no way to know where the sun is. This binder closes that gap.
///
/// It deliberately touches NOTHING in the scene: no component is added, no object is
/// created, nothing is marked dirty. It hooks the render loop from a static callback in
/// both the editor and at runtime, so batchmode diagnostic captures get the same sun the
/// game does. That matters because this project has previously had capture tools write
/// the scene as a side effect of rendering it.
/// </summary>
public static class MapleRideSunBinder
{
    private static readonly int SunDirId = Shader.PropertyToID("_MR_SunDir");
    private static readonly int SunColorId = Shader.PropertyToID("_MR_SunColor");
    private static readonly int AmbSkyId = Shader.PropertyToID("_MR_AmbSky");
    private static readonly int AmbEquatorId = Shader.PropertyToID("_MR_AmbEquator");
    private static readonly int AmbGroundId = Shader.PropertyToID("_MR_AmbGround");
    private static readonly int FogColorId = Shader.PropertyToID("_MR_FogColor");
    private static readonly int FogParamsId = Shader.PropertyToID("_MR_FogParams");
    private static readonly int FogRangeId = Shader.PropertyToID("_MR_FogRange");

    private static Light _cachedSun;
    private static float _nextSearch;
    private static bool _hooked;

    // Re-scanning every camera render would be ruinous: the coast scene carries tens of
    // thousands of objects. The key light is cached and only re-found when it goes away
    // or the interval elapses, which is cheap and self-healing across scene loads.
    private const float SearchIntervalSeconds = 1.0f;

    // HDRP directional lights are authored in lux and can be in the thousands; this kit's
    // lights were authored for Built-in at intensity ~1-3. Multiplying the cel ramp by a
    // raw lux value would clip every surface to white. Anything above this threshold is
    // treated as a physical-unit light and normalised to unit intensity, because exposure
    // is applied separately (the shader multiplies by GetCurrentExposureMultiplier).
    // PROVISIONAL tuning value.
    private const float PhysicalIntensityThreshold = 8f;

    // PROVISIONAL. These lights and the trilight ambient were authored against a Gamma
    // project, where the numeric colour was used as-is. Converting them with .linear is
    // physically correct but silently re-art-directs the scene: linearisation stretches
    // the channel ratios (the sun's blue/red goes 0.675 -> 0.43 while red stays 1.0, and
    // the ambient's blue/red goes 1.5x -> 2.4x), which rendered the coast with a violently
    // blue ambient on every up-facing surface and an orange key. This milestone converts
    // shaders, not lighting, and its acceptance bar is "no regression against the pre-HDRP
    // baseline", so the authored numerics are fed through unchanged to preserve the
    // validated look. Flipping this to false is the deliberate, separately-reviewable step
    // that re-tunes the scene's lighting for Linear.
    private const bool TreatAuthoredColorsAsLinear = true;

    private static Color AuthoredColor(Color c)
        => TreatAuthoredColorsAsLinear ? c : c.linear;

    private static Color Clamp0(Color c)
        => new Color(Mathf.Max(0f, c.r), Mathf.Max(0f, c.g), Mathf.Max(0f, c.b), 1f);

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void HookEditor() => Hook();
#endif



    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookRuntime() => Hook();

    private static void Hook()
    {
        if (_hooked) return;
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        _hooked = true;
    }

    private static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) => Bind();

    /// <summary>Public so a diagnostics pass can force a bind before an off-screen capture.</summary>
    public static void Bind()
    {
        Light sun = ResolveSun();
        if (sun != null)
        {
            // Unity's directional light shines ALONG +forward, so the vector pointing
            // toward the light is -forward. Getting this backwards lights every surface
            // from behind and reads as a uniformly shaded, "flat" world.
            Vector3 toLight = -sun.transform.forward.normalized;
            float intensity = Mathf.Max(0f, sun.intensity);
            if (intensity > PhysicalIntensityThreshold) intensity = 1f;
            Color c = AuthoredColor(sun.color) * intensity;
            Shader.SetGlobalVector(SunDirId, new Vector4(toLight.x, toLight.y, toLight.z, 0f));
            Shader.SetGlobalVector(SunColorId, new Vector4(c.r, c.g, c.b, 1f));
        }

        Color sky = RenderSettings.ambientSkyColor;
        Color eq = RenderSettings.ambientEquatorColor;
        Color gnd = RenderSettings.ambientGroundColor;
        if (RenderSettings.ambientMode != AmbientMode.Trilight)
        {
            // Flat/skybox ambient modes collapse to a single colour; feeding the same
            // value to all three bands reproduces them exactly.
            sky = eq = gnd = RenderSettings.ambientLight;
        }

        // The Built-in SakuraCel this kit was lit against reads ambient through ShadeSH9,
        // i.e. Unity's spherical-harmonics ambient PROBE - not the raw authored bands.
        // The trilight -> SH projection flattens and cross-mixes the three colours heavily,
        // so an up-facing surface receives a warm-neutral average rather than the pure blue
        // sky band. Feeding the raw bands instead tinted every road, tree crown and snow cap
        // cold blue. Evaluating the real probe at up / horizon / down gives the SH-flattened
        // values, so the shader's existing three-band lerp reproduces ShadeSH9 at exactly the
        // directions that matter, with no invented tuning constants.
        var probe = RenderSettings.ambientProbe;
        var dirs = new[] { Vector3.up, Vector3.forward, Vector3.down };
        var evaluated = new Color[3];
        probe.Evaluate(dirs, evaluated);
        if (evaluated[0].maxColorComponent + evaluated[1].maxColorComponent + evaluated[2].maxColorComponent > 1e-5f)
        {
            // Evaluate() returns LINEAR radiance regardless of project colour space, while
            // everything downstream of AuthoredColor works in the kit's authored (sRGB)
            // space, so convert here rather than letting a linear value masquerade as one.
            // SH evaluation legitimately returns NEGATIVE components in some directions
            // (the L1/L2 lobes undershoot), and Color.gamma of a negative value is NaN.
            // That NaN propagates through the ambient term and wipes out whole surfaces -
            // it silently erased the entire terrain while the road still drew correctly.
            sky = Clamp0(evaluated[0]).gamma;
            eq = Clamp0(evaluated[1]).gamma;
            gnd = Clamp0(evaluated[2]).gamma;
        }

        Shader.SetGlobalVector(AmbSkyId, AuthoredColor(sky));
        Shader.SetGlobalVector(AmbEquatorId, AuthoredColor(eq));
        Shader.SetGlobalVector(AmbGroundId, AuthoredColor(gnd));

        // Distance fog. HDRP's own fog is off (see HdrpVolumeSetup), and a hand-written pass
        // receives no fog automatically, so the scene's authored RenderSettings fog is pushed
        // through to MR_ApplyFog. This is not optional polish: the warm cream fog is what
        // supplies aerial perspective and what warms the distance against a cool ambient.
        float mode = RenderSettings.fogMode == FogMode.Linear ? 1f
                   : RenderSettings.fogMode == FogMode.Exponential ? 2f : 3f;
        Shader.SetGlobalVector(FogColorId, AuthoredColor(RenderSettings.fogColor));
        Shader.SetGlobalVector(FogParamsId,
            new Vector4(RenderSettings.fog ? 1f : 0f, mode, RenderSettings.fogDensity, 0f));
        Shader.SetGlobalVector(FogRangeId,
            new Vector4(RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, 0f, 0f));
    }

    private static Light ResolveSun()
    {
        float now = Application.isPlaying ? Time.realtimeSinceStartup : (float)EditorTimeSeconds();
        if (_cachedSun != null && now < _nextSearch) return _cachedSun;
        _nextSearch = now + SearchIntervalSeconds;

        Light best = RenderSettings.sun;
        if (best == null || !best.isActiveAndEnabled || best.type != LightType.Directional)
        {
            best = null;
            float bestIntensity = -1f;
            var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                if (l.intensity > bestIntensity) { bestIntensity = l.intensity; best = l; }
            }
        }

        _cachedSun = best;
        return best;
    }

    private static double EditorTimeSeconds()
    {
#if UNITY_EDITOR
        return EditorApplication.timeSinceStartup;
#else
        return Time.realtimeSinceStartup;
#endif
    }
}

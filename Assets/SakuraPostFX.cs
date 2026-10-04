using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight bloom + filmic grade + vignette for the Built-in pipeline.
/// The Sakura Pass sunset relies on blooming the sun, the lake glitter and the lantern glows;
/// without it the scene reads as flat vector art no matter how good the geometry is.
///
/// !!! WAS DEAD UNDER HDRP - NOW RE-HOSTED, READ BEFORE TOUCHING THIS FILE OR THE HOST !!!
/// -----------------------------------------------------------------------------------
/// This component's OWN OnRenderImage body below is a BUILT-IN RENDER PIPELINE callback and
/// HDRP never invokes it - that has not changed and never will while the project stays on
/// HDRP. From the 7-map HDRP migration until 2026-09-24 that made every pass in this file
/// (exposure, saturation, contrast, lift/gain, bloom, vignette, depth of field, aerial
/// perspective, valley/cloud-sea mist) a silent no-op on all 7 maps, even though the component
/// stayed on every camera and RegionDirector.ApplyAmbience() kept pushing all 7 regions' values
/// onto it every frame - MEASURED PROOF: forcing FujiAmbience to saturation=0.00/exposure=0.25
/// (full greyscale at quarter brightness) and separately to a 4.4x denser/8x-closer mist both
/// produced byte-for-byte identical renders to the unmodified values.
///
/// THE FIX: <see cref="SakuraGradeCustomPass"/>, an HDRP CustomPass running at
/// AfterPostProcess (supportCustomPass was already 1 in all four SC_HDRP_*.asset quality tiers -
/// only the host was missing), wired into the scene as a single global CustomPassVolume by
/// SakuraPassEnvironment.EnsureSakuraGradeCustomPassVolume(). It is a line-for-line port of this
/// class's maths (see SakuraPostFXCustomPass.shader) that reads its parameters STRAIGHT OFF
/// this component - every camera that carries a SakuraPostFX (the gameplay camera permanently,
/// diagnostic rigs ad-hoc) grades again, on every region, with zero changes needed to
/// RegionDirector.ApplyAmbience() or to any Ambience value's call site.
///
/// This component's own fields remain the single source of truth (RegionDirector still writes
/// them, SakuraGradeCustomPass only reads them) and OnRenderImage below is left intact - it is
/// harmless dead code under HDRP and would matter again only if the project ever moved back to
/// the Built-in pipeline.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("MapleRide/Sakura Post FX")]
public class SakuraPostFX : MonoBehaviour
{
    [Header("Bloom")]
    [Range(0f, 4f)] public float bloomThreshold = 1.45f;
    [Range(0f, 1f)] public float bloomSoftKnee = 0.30f;
    [Range(0f, 4f)] public float bloomIntensity = 0.32f;
    [Range(1, 6)] public int bloomIterations = 4;

    [Header("Grade")]
    [Range(0f, 3f)] public float exposure = 0.82f;
    [Range(0f, 2f)] public float saturation = 1.12f;
    [Range(0f, 2f)] public float contrast = 1.06f;
    public Color lift = new Color(0.02f, 0.01f, 0.05f, 0f);
    public Color gain = new Color(1.03f, 0.99f, 0.97f, 0f);

    [Header("Vignette")]
    [Range(0f, 2f)] public float vignetteStrength = 0.38f;
    [Range(0.1f, 2f)] public float vignetteSoftness = 0.65f;

    [Header("Depth of field")]
    // Cinematic far-field defocus. Everything nearer than focusDistance is untouched, so the
    // road under the rider stays crisp; the blend then ramps in over focusRange and tops out at
    // dofStrength, which is deliberately below 1 so the volcano reads soft rather than smeared.
    // These are provisional look values, not design requirements - tune them on a render.
    [Range(0f, 600f)] public float dofFocusDistance = 160f;
    [Range(1f, 2000f)] public float dofFocusRange = 700f;
    [Range(0.25f, 4f)] public float dofFalloff = 1.55f;
    [Range(0f, 1f)] public float dofStrength = 0.62f;
    [Range(1, 4)] public int dofIterations = 2;

    // --- Section 25: atmosphere and depth --------------------------------------------------
    // All PROVISIONAL look values, not design requirements. Distance fog alone made far geometry
    // recede without reducing its saturation or contrast, so the far sakura wall competed with
    // the foreground. These add the two cues section 25 names explicitly, plus valley mist.
    [Header("Aerial perspective (section 25)")]
    /// <summary>Nothing nearer than this is touched, so the carriageway and rider stay clean.</summary>
    [Range(0f, 400f)] public float aerialStart = 110f;
    /// <summary>Distance over which the aerial cues reach full strength.</summary>
    [Range(50f, 4000f)] public float aerialRange = 950f;
    /// <summary>Saturation removed at full distance.</summary>
    [Range(0f, 1f)] public float aerialDesaturation = 0.50f;
    /// <summary>Local contrast flattened toward mid-grey at full distance.</summary>
    [Range(0f, 1f)] public float aerialFlatten = 0.40f;
    /// <summary>Horizon tint the far field drifts toward; matches RenderSettings.fogColor.</summary>
    public Color aerialTint = new Color(0.93f, 0.78f, 0.70f, 1f);
    [Range(0f, 1f)] public float aerialTintAmount = 0.22f;

    [Header("Valley mist (section 25)")]
    /// <summary>World Y of full mist (valley floor).</summary>
    public float mistBaseY = 6f;
    /// <summary>World Y at which mist has cleared (ridge line).</summary>
    public float mistTopY = 34f;
    [Range(0f, 1f)] public float mistStrength = 0.45f;
    /// <summary>No mist inside this radius - it must never fog the road under the wheels.</summary>
    [Range(10f, 600f)] public float mistStart = 140f;
    public Color mistColor = new Color(0.95f, 0.86f, 0.84f, 1f);

    private Material _material;
    private Camera _camera;
    private readonly List<RenderTexture> _pyramid = new List<RenderTexture>();

    private Material EnsureMaterial()
    {
        if (_material != null) return _material;
        var shader = Shader.Find("Hidden/MapleRide/SakuraPostFX");
        if (shader == null || !shader.isSupported) return null;
        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return _material;
    }

    // The depth-of-field pass reads _CameraDepthTexture, which the Built-in pipeline only
    // renders when a camera asks for it. Requesting it here (rather than assuming a depth
    // prepass exists) is what keeps DoF working on the diagnostic cameras as well as in game.
    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        if (_camera != null) _camera.depthTextureMode |= DepthTextureMode.Depth;
    }

    private void OnDisable()
    {
        if (_material != null) DestroyImmediate(_material);
        _material = null;
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        var mat = EnsureMaterial();
        if (mat == null) { Graphics.Blit(source, destination); return; }

        if (_camera == null) _camera = GetComponent<Camera>();
        if (_camera != null) _camera.depthTextureMode |= DepthTextureMode.Depth;

        float knee = Mathf.Max(1e-4f, bloomThreshold * bloomSoftKnee);
        mat.SetVector("_BloomParams", new Vector4(bloomThreshold, knee * 2f, 0.25f / knee, bloomIntensity));
        mat.SetVector("_GradeParams", new Vector4(exposure, saturation, contrast, 0f));
        mat.SetVector("_Lift", lift);
        mat.SetVector("_Gain", gain);
        mat.SetVector("_VignetteParams", new Vector4(vignetteStrength, vignetteSoftness, 0f, 0f));
        mat.SetVector("_DofParams", new Vector4(dofFocusDistance, dofFocusRange, dofFalloff, dofStrength));

        // Section 25. The mist pass reconstructs a world position from depth, so it needs the
        // camera basis and the frustum half-angles; pushing them every frame keeps the effect
        // correct on the chase camera, the benchmark cameras and the diagnostics cameras alike.
        mat.SetVector("_AerialParams", new Vector4(aerialStart, aerialRange, aerialDesaturation, aerialFlatten));
        mat.SetVector("_AerialTint", new Vector4(aerialTint.r, aerialTint.g, aerialTint.b, aerialTintAmount));
        mat.SetVector("_MistParams", new Vector4(mistBaseY, mistTopY, mistStrength, mistStart));
        mat.SetVector("_MistColor", mistColor);
        if (_camera != null)
        {
            float tanV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            mat.SetVector("_CamFrustum", new Vector4(tanV * _camera.aspect, tanV, 0f, 0f));
            mat.SetMatrix("_CamToWorld", _camera.cameraToWorldMatrix);
        }

        int width = Mathf.Max(1, source.width >> 1);
        int height = Mathf.Max(1, source.height >> 1);
        var format = source.format;

        // --- depth of field: one QUARTER-res blurred copy of the frame ---------------------
        // Quarter res, not half. The blur is a separable two-tap-per-pass Gaussian run
        // dofIterations times, so it is pure fill: at half res on a 1920x1080 frame, three
        // iterations is twelve full 960x540 blits every frame, and Shiosai asks for three.
        // Dropping to quarter res cuts that fill by 4x, and because the spread is expressed in
        // TEXELS the blur also gets wider for free - the effect it is used for here (soft,
        // hazed background past 150 m) is if anything slightly better, and it is only ever
        // composited over geometry that is already out of focus.
        RenderTexture dof = null;
        if (dofStrength > 0.0001f)
        {
            int dw = Mathf.Max(1, source.width >> 2);
            int dh = Mathf.Max(1, source.height >> 2);
            dof = RenderTexture.GetTemporary(dw, dh, 0, format);
            dof.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, dof);
            var scratch = RenderTexture.GetTemporary(dw, dh, 0, format);
            scratch.filterMode = FilterMode.Bilinear;
            for (int i = 0; i < Mathf.Clamp(dofIterations, 1, 4); i++)
            {
                // Halved against the half-res version: a quarter-res texel is twice as wide in
                // screen space, so the same texel spread would double the blur radius.
                float spread = 0.5f + i * 0.8f;
                mat.SetVector("_BlurDir", new Vector4(spread / dof.width, 0f, 0f, 0f));
                Graphics.Blit(dof, scratch, mat, 1);
                mat.SetVector("_BlurDir", new Vector4(0f, spread / scratch.height, 0f, 0f));
                Graphics.Blit(scratch, dof, mat, 1);
            }
            RenderTexture.ReleaseTemporary(scratch);
            mat.SetTexture("_DofTex", dof);
        }
        else
        {
            mat.SetTexture("_DofTex", Texture2D.blackTexture);
        }

        // --- bright pass -------------------------------------------------------
        var bright = RenderTexture.GetTemporary(width, height, 0, format);
        bright.filterMode = FilterMode.Bilinear;
        Graphics.Blit(source, bright, mat, 0);

        // --- downsample pyramid ------------------------------------------------
        _pyramid.Clear();
        var current = bright;
        int iterations = Mathf.Clamp(bloomIterations, 1, 6);
        for (int i = 0; i < iterations; i++)
        {
            width = Mathf.Max(1, width >> 1);
            height = Mathf.Max(1, height >> 1);
            if (width < 2 || height < 2) break;
            var down = RenderTexture.GetTemporary(width, height, 0, format);
            down.filterMode = FilterMode.Bilinear;
            mat.SetVector("_BlurDir", new Vector4(1f / current.width, 0f, 0f, 0f));
            var tmp = RenderTexture.GetTemporary(width, height, 0, format);
            tmp.filterMode = FilterMode.Bilinear;
            Graphics.Blit(current, tmp, mat, 1);
            mat.SetVector("_BlurDir", new Vector4(0f, 1f / tmp.height, 0f, 0f));
            Graphics.Blit(tmp, down, mat, 1);
            RenderTexture.ReleaseTemporary(tmp);
            _pyramid.Add(down);
            current = down;
        }

        // --- upsample and accumulate ------------------------------------------
        for (int i = _pyramid.Count - 2; i >= 0; i--)
        {
            var target = _pyramid[i];
            mat.SetTexture("_BloomTex", current);
            var accum = RenderTexture.GetTemporary(target.width, target.height, 0, format);
            accum.filterMode = FilterMode.Bilinear;
            Graphics.Blit(target, accum, mat, 2);
            current = accum;
            RenderTexture.ReleaseTemporary(target);
            _pyramid[i] = accum;
        }

        // --- composite ---------------------------------------------------------
        mat.SetTexture("_BloomTex", current);
        Graphics.Blit(source, destination, mat, 3);

        foreach (var rt in _pyramid) if (rt != null) RenderTexture.ReleaseTemporary(rt);
        _pyramid.Clear();
        RenderTexture.ReleaseTemporary(bright);
        if (dof != null) RenderTexture.ReleaseTemporary(dof);
    }
}

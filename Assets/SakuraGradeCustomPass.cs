using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// HDRP re-host of <see cref="SakuraPostFX"/>. See the "DEAD UNDER HDRP" header comment on that
/// class for the measured proof of the bug this exists to fix: <c>OnRenderImage</c> never fires
/// under HDRP, so every one of that component's passes (bloom, exposure/saturation/contrast,
/// lift/gain, vignette, depth of field, aerial perspective, valley mist) has been a silent no-op
/// on all 7 maps since the HDRP migration, even though <see cref="RegionDirector.ApplyAmbience"/>
/// keeps pushing per-region values onto it every frame.
///
/// This <see cref="CustomPass"/> is the fix: it runs at <see cref="CustomPassInjectionPoint.AfterPostProcess"/>
/// (chosen because <c>supportCustomPass</c> is already enabled in all four HDRP quality tiers for
/// this injection point, and because it needs the fully-composed frame - the same thing
/// <c>OnRenderImage</c> received under Built-in) and reproduces every SakuraPostFX pass with the
/// exact same maths (see SakuraPostFXCustomPass.shader, a line-for-line port of
/// SakuraPostFXShader), reading its parameters STRAIGHT OFF the <see cref="SakuraPostFX"/>
/// component already sitting on the camera being rendered.
///
/// This is deliberately NOT a redesign of the ambience/tuning API: RegionDirector.ApplyAmbience
/// still writes to SakuraPostFX's public fields exactly as before (zero changes to that call
/// site), and every camera that carries a SakuraPostFX component keeps grading itself the same
/// way it always was supposed to - only the host changed. A single GLOBAL CustomPassVolume is
/// enough for every camera in the project: cameras without a SakuraPostFX component (portrait/
/// matte/ungraded diagnostic rigs) are left completely untouched by the early-out below.
/// </summary>
[System.Serializable]
public class SakuraGradeCustomPass : CustomPass
{
    const int MaxBloomIterations = 6;
    const int MaxDofIterations = 4;

    const int PassBright = 0;
    const int PassBlur = 1;
    const int PassUpsample = 2;
    const int PassComposite = 3;

    static readonly int s_Source = Shader.PropertyToID("_Source");
    static readonly int s_BloomTex = Shader.PropertyToID("_BloomTex");
    static readonly int s_DofTex = Shader.PropertyToID("_DofTex");
    static readonly int s_BlurDir = Shader.PropertyToID("_BlurDir");
    static readonly int s_BloomParams = Shader.PropertyToID("_BloomParams");
    static readonly int s_GradeParams = Shader.PropertyToID("_GradeParams");
    static readonly int s_Lift = Shader.PropertyToID("_Lift");
    static readonly int s_Gain = Shader.PropertyToID("_Gain");
    static readonly int s_VignetteParams = Shader.PropertyToID("_VignetteParams");
    static readonly int s_DofParams = Shader.PropertyToID("_DofParams");
    static readonly int s_AerialParams = Shader.PropertyToID("_AerialParams");
    static readonly int s_AerialTint = Shader.PropertyToID("_AerialTint");
    static readonly int s_MistParams = Shader.PropertyToID("_MistParams");
    static readonly int s_MistColor = Shader.PropertyToID("_MistColor");
    static readonly int s_CamToWorld = Shader.PropertyToID("_CamToWorld");
    static readonly int s_CamFrustum = Shader.PropertyToID("_CamFrustum");

    Material _material;
    MaterialPropertyBlock _mpb;

    // GHOSTING FIX (per-camera RTHandle lifetime). These buffers used to be single class-level
    // fields on this one global CustomPass instance, shared by EVERY camera that ever rendered
    // through it. RTHandles allocated with useDynamicScale grow to the largest reference size
    // any camera has ever requested and never shrink back down; a smaller/differently-framed
    // camera's CustomPassUtils.Copy only overwrites the sub-rect its own viewport occupies; the
    // rest of the (larger) backing texture keeps whatever a PREVIOUS, differently-sized or
    // differently-framed camera last wrote there. Every later pass in this file samples with a
    // raw 0..1 UV via GetFullScreenTriangleTexCoord (no RTHandleScale correction), so a camera
    // that is smaller than the backing texture's high-water mark reads stale corners of a
    // previous camera's frame straight into its own composite - measured as a translucent
    // duplicate/ghosted rider-head silhouette bleeding into diagnostic renders that create and
    // destroy cameras in quick succession (AimAt()/rider camera-transition paths in the Shiosai
    // and Taka diagnostics, and any real in-game camera cut that follows the same shape).
    //
    // FIX: give every camera its OWN buffer set, keyed by the Camera object itself (Unity's
    // overridden == correctly reports "destroyed" for a stale key, which PruneDestroyedCameras
    // below relies on to release GPU memory for diagnostic cameras that are created and torn
    // down every shot). Two cameras can never again alias the same backing texture, regardless
    // of relative size or render order, so the stale-corner read is structurally impossible
    // rather than merely unlikely.
    sealed class CameraBuffers
    {
        public RTHandle sceneCopy;
        public RTHandle bright;
        public readonly RTHandle[] pyramid = new RTHandle[MaxBloomIterations];
        public readonly RTHandle[] pyramidScratch = new RTHandle[MaxBloomIterations];
        public RTHandle dofA;
        public RTHandle dofB;

        public void Release()
        {
            RTHandles.Release(sceneCopy); sceneCopy = null;
            RTHandles.Release(bright); bright = null;
            for (int i = 0; i < MaxBloomIterations; i++)
            {
                RTHandles.Release(pyramid[i]); pyramid[i] = null;
                RTHandles.Release(pyramidScratch[i]); pyramidScratch[i] = null;
            }
            RTHandles.Release(dofA); dofA = null;
            RTHandles.Release(dofB); dofB = null;
        }
    }

    readonly Dictionary<Camera, CameraBuffers> _perCamera = new Dictionary<Camera, CameraBuffers>();

    static RTHandle Alloc(float scaleFactor, string name) =>
        RTHandles.Alloc(new Vector2(scaleFactor, scaleFactor), TextureXR.slices,
            dimension: TextureXR.dimension,
            colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
            filterMode: FilterMode.Bilinear,
            wrapMode: TextureWrapMode.Clamp,
            useDynamicScale: true,
            name: name);

    CameraBuffers CreateCameraBuffers(Camera camera)
    {
        // Instance ID in the name (not just camera.name) because diagnostic rigs are frequently
        // all named e.g. "~ShiosaiDiagCam" - the ID keeps each set individually identifiable in
        // the frame debugger even when many share a name across a capture batch.
        string tag = $"{camera.name} #{camera.GetInstanceID()}";
        var buf = new CameraBuffers
        {
            sceneCopy = Alloc(1.0f, $"Sakura Grade Scene Copy [{tag}]"),
            bright = Alloc(0.5f, $"Sakura Grade Bright [{tag}]"),
        };
        for (int i = 0; i < MaxBloomIterations; i++)
        {
            // Level i sits at 1/4, 1/8, 1/16 ... of full res - one halving below the half-res
            // bright pass, exactly matching SakuraPostFX's `width = width >> 1` pyramid.
            float scale = 0.25f / (1 << i);
            buf.pyramid[i] = Alloc(scale, $"Sakura Grade Bloom {i} [{tag}]");
            buf.pyramidScratch[i] = Alloc(scale, $"Sakura Grade Bloom Scratch {i} [{tag}]");
        }
        buf.dofA = Alloc(0.25f, $"Sakura Grade DoF A [{tag}]");
        buf.dofB = Alloc(0.25f, $"Sakura Grade DoF B [{tag}]");
        return buf;
    }

    /// <summary>
    /// Releases and forgets buffer sets whose camera key has been destroyed since the last
    /// Execute - the diagnostics harnesses create-and-DestroyImmediate a fresh camera per shot,
    /// so without this the dictionary would otherwise grow one full buffer set per shot for the
    /// lifetime of the editor session.
    /// </summary>
    void PruneDestroyedCameras()
    {
        if (_perCamera.Count == 0) return;
        List<Camera> dead = null;
        foreach (var kv in _perCamera)
        {
            if (kv.Key == null) (dead ??= new List<Camera>()).Add(kv.Key);
        }
        if (dead == null) return;
        foreach (var cam in dead)
        {
            _perCamera[cam].Release();
            _perCamera.Remove(cam);
        }
    }

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        name = "Sakura Grade (HDRP)";

        var shader = Shader.Find("Hidden/MapleRide/SakuraPostFXCustomPass");
        if (shader == null)
        {
            Debug.LogError("[sakura-grade] shader 'Hidden/MapleRide/SakuraPostFXCustomPass' not found - grading will be skipped.");
            return;
        }
        _material = CoreUtils.CreateEngineMaterial(shader);
        _mpb = new MaterialPropertyBlock();

        // Buffers are no longer allocated here: each camera gets its own set, lazily, the first
        // time Execute sees it (see CreateCameraBuffers/PruneDestroyedCameras above).
    }

    protected override void Execute(CustomPassContext ctx)
    {
        if (_material == null) return;

        var camera = ctx.hdCamera.camera;
        if (camera == null) return;

        // Every camera that is meant to grade keeps its OWN SakuraPostFX component (the gameplay
        // camera permanently, diagnostic rigs ad-hoc) - RegionDirector.ApplyAmbience writes the
        // current region's values onto it every frame exactly as it always has. Cameras without
        // one (portrait rigs, matte captures, etc. - see NpcPortraitBake's "no SakuraPostFX on
        // this camera, on purpose") are left completely alone here.
        var fx = camera.GetComponent<SakuraPostFX>();
        if (fx == null || !fx.enabled) return;

        // Ghosting fix: this camera gets its OWN buffer set, never shared with any other
        // camera that has rendered (or will render) through this same global CustomPass
        // instance. See the CameraBuffers comment above for why that is what actually matters.
        PruneDestroyedCameras();
        if (!_perCamera.TryGetValue(camera, out var buf))
        {
            buf = CreateCameraBuffers(camera);
            _perCamera[camera] = buf;
        }

        var cmd = ctx.cmd;
        var mpb = _mpb;

        // Snapshot the fully composited HDRP frame so every later pass can read the ORIGINAL
        // image while we build up to writing the final grade back into the live camera buffer.
        // CustomPassUtils.Copy handles the camera buffer's RTHandle scale/history bookkeeping
        // that a raw cmd.CopyTexture would silently get wrong.
        CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, buf.sceneCopy);

        float knee = Mathf.Max(1e-4f, fx.bloomThreshold * fx.bloomSoftKnee);
        mpb.SetVector(s_BloomParams, new Vector4(fx.bloomThreshold, knee * 2f, 0.25f / knee, fx.bloomIntensity));
        mpb.SetVector(s_GradeParams, new Vector4(fx.exposure, fx.saturation, fx.contrast, 0f));
        mpb.SetVector(s_Lift, fx.lift);
        mpb.SetVector(s_Gain, fx.gain);
        mpb.SetVector(s_VignetteParams, new Vector4(fx.vignetteStrength, fx.vignetteSoftness, 0f, 0f));
        mpb.SetVector(s_DofParams, new Vector4(fx.dofFocusDistance, fx.dofFocusRange, fx.dofFalloff, fx.dofStrength));
        mpb.SetVector(s_AerialParams, new Vector4(fx.aerialStart, fx.aerialRange, fx.aerialDesaturation, fx.aerialFlatten));
        mpb.SetVector(s_AerialTint, new Vector4(fx.aerialTint.r, fx.aerialTint.g, fx.aerialTint.b, fx.aerialTintAmount));
        mpb.SetVector(s_MistParams, new Vector4(fx.mistBaseY, fx.mistTopY, fx.mistStrength, fx.mistStart));
        mpb.SetVector(s_MistColor, fx.mistColor);

        // Mist needs a world position reconstructed from depth; the camera basis/frustum is
        // pushed every frame exactly as SakuraPostFX.OnRenderImage used to under Built-in.
        float tanV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        mpb.SetVector(s_CamFrustum, new Vector4(tanV * camera.aspect, tanV, 0f, 0f));
        mpb.SetMatrix(s_CamToWorld, camera.cameraToWorldMatrix);

        // --- depth of field: one quarter-res blurred copy of the frame ------------------------
        if (fx.dofStrength > 0.0001f)
        {
            // A zero-direction "blur" degenerates to a plain resample (the 5 tap weights sum to
            // 1 and all land on the same texel), which is what seeds the quarter-res copy.
            mpb.SetTexture(s_Source, buf.sceneCopy);
            mpb.SetVector(s_BlurDir, Vector4.zero);
            CoreUtils.DrawFullScreen(cmd, _material, buf.dofA, mpb, PassBlur);

            int dofIterations = Mathf.Clamp(fx.dofIterations, 1, MaxDofIterations);
            for (int i = 0; i < dofIterations; i++)
            {
                float spread = 0.5f + i * 0.8f;
                mpb.SetTexture(s_Source, buf.dofA);
                mpb.SetVector(s_BlurDir, new Vector4(spread / Mathf.Max(1, buf.dofA.rt.width), 0f, 0f, 0f));
                CoreUtils.DrawFullScreen(cmd, _material, buf.dofB, mpb, PassBlur);
                mpb.SetTexture(s_Source, buf.dofB);
                mpb.SetVector(s_BlurDir, new Vector4(0f, spread / Mathf.Max(1, buf.dofB.rt.height), 0f, 0f));
                CoreUtils.DrawFullScreen(cmd, _material, buf.dofA, mpb, PassBlur);
            }
        }

        // --- bright pass ------------------------------------------------------------------
        mpb.SetTexture(s_Source, buf.sceneCopy);
        CoreUtils.DrawFullScreen(cmd, _material, buf.bright, mpb, PassBright);

        // --- downsample pyramid (blur-into-smaller-target IS the downsample, exactly like
        // SakuraPostFX.OnRenderImage) --------------------------------------------------------
        int iterations = Mathf.Clamp(fx.bloomIterations, 1, MaxBloomIterations);
        RTHandle current = buf.bright;
        int levelsBuilt = 0;
        for (int i = 0; i < iterations; i++)
        {
            var target = buf.pyramid[i];
            var scratch = buf.pyramidScratch[i];
            if (target.rt.width < 2 || target.rt.height < 2) break;

            mpb.SetTexture(s_Source, current);
            mpb.SetVector(s_BlurDir, new Vector4(1f / Mathf.Max(1, current.rt.width), 0f, 0f, 0f));
            CoreUtils.DrawFullScreen(cmd, _material, scratch, mpb, PassBlur);

            mpb.SetTexture(s_Source, scratch);
            mpb.SetVector(s_BlurDir, new Vector4(0f, 1f / Mathf.Max(1, scratch.rt.height), 0f, 0f));
            CoreUtils.DrawFullScreen(cmd, _material, target, mpb, PassBlur);

            current = target;
            levelsBuilt++;
        }

        // --- upsample and accumulate (writes into the scratch buffers, which are free again
        // now that the downsample pass above is done with them) ------------------------------
        for (int i = levelsBuilt - 2; i >= 0; i--)
        {
            var accumDest = buf.pyramidScratch[i];
            mpb.SetTexture(s_Source, buf.pyramid[i]);
            mpb.SetTexture(s_BloomTex, current);
            CoreUtils.DrawFullScreen(cmd, _material, accumDest, mpb, PassUpsample);
            current = accumDest;
        }

        // --- composite: grade + tonemap + vignette + DoF + aerial + mist, written straight
        // into the live camera colour buffer ------------------------------------------------
        mpb.SetTexture(s_Source, buf.sceneCopy);
        mpb.SetTexture(s_BloomTex, current);
        mpb.SetTexture(s_DofTex, fx.dofStrength > 0.0001f ? buf.dofA : (Texture)Texture2D.blackTexture);
        CoreUtils.DrawFullScreen(cmd, _material, ctx.cameraColorBuffer, mpb, PassComposite);
    }

    protected override void Cleanup()
    {
        CoreUtils.Destroy(_material);
        _material = null;

        foreach (var kv in _perCamera) kv.Value.Release();
        _perCamera.Clear();
    }
}

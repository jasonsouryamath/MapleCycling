using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Minimal, asset-only exposure sanity pass for the HDRP conversion (spec 4.2.3 / 4.4).
///
/// The first post-conversion render was unjudgeable: HDRP's stock default volume profile runs
/// Exposure in AUTOMATIC mode, so the camera re-adapts to whatever happens to be in frame and
/// no two diagnostic captures are comparable - which defeats the entire verify-by-render rule
/// this project runs on. Fixed exposure makes captures deterministic and comparable against the
/// pre-HDRP baseline.
///
/// Scope is deliberately tiny. Sky, fog and the full atmosphere/volume stack are spec sections
/// 8-16 and belong to the lighting milestone, not to shader conversion. Nothing here touches a
/// scene file: the default volume profile is a project asset.
/// </summary>
public static class HdrpVolumeSetup
{
    private const string SettingsDir = "Assets/Settings/HDRP";

    // PROVISIONAL tuning. EV100 = 0 reproduces a unit exposure multiplier, which is the value
    // the hand-written MapleRide shaders were authored against (they output LDR-range colour
    // and multiply by GetCurrentExposureMultiplier). Not benchmarked against a photographic
    // reference - it is a stable starting point, not a final grade.
    private const float FixedExposureEV100 = 0f;

    [MenuItem("MapleRide/Environment/Fix HDRP Exposure For Captures")]
    public static void Run()
    {
        if (!GraphicsSettings.TryGetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>(out var dv) ||
            dv.volumeProfile == null)
        {
            Debug.LogError("[hdrp-volume] no default volume profile found - is HDRP active?");
            return;
        }

        VolumeProfile profile = dv.volumeProfile;
        string path = AssetDatabase.GetAssetPath(profile);

        // Spec 4.2.3 wants HDRP settings assets under Assets/Settings/HDRP.
        if (!path.StartsWith(SettingsDir))
        {
            Directory.CreateDirectory(SettingsDir);
            string target = $"{SettingsDir}/{Path.GetFileName(path)}";
            string err = AssetDatabase.MoveAsset(path, target);
            if (string.IsNullOrEmpty(err)) { Debug.Log($"[hdrp-volume] moved {path} -> {target}"); path = target; }
            else Debug.LogWarning($"[hdrp-volume] could not move profile: {err}");
        }

        if (profile.TryGet<Exposure>(out var exposure))
        {
            exposure.active = true;
            exposure.mode.overrideState = true;
            exposure.mode.value = ExposureMode.Fixed;
            exposure.fixedExposure.overrideState = true;
            exposure.fixedExposure.value = FixedExposureEV100;
            Debug.Log($"[hdrp-volume] exposure -> Fixed EV100 {FixedExposureEV100} (was Automatic)");
        }
        else Debug.LogWarning("[hdrp-volume] profile has no Exposure override");

        // Motion blur makes a batchmode capture depend on the previous frame's camera pose,
        // which is exactly the kind of non-determinism that makes render comparison lie.
        if (profile.TryGet<MotionBlur>(out var mb))
        {
            mb.active = false;
            Debug.Log("[hdrp-volume] motion blur disabled for deterministic captures");
        }

        // HDRP's stock profile ships a Physically Based Sky whose aerial perspective whitens
        // everything with distance, on top of the project's OWN sky dome (MapleRide/HDRP/Sky)
        // and the kit's own authored depth haze. Two competing atmospheres is why the first
        // fixed-exposure capture still read as a white-out. HDRP's sky is switched off here so
        // the project's authored sky remains the single source of atmosphere; replacing it with
        // a deliberately art-directed HDRP sky/fog stack is spec sections 8-16.
        if (profile.TryGet<PhysicallyBasedSky>(out var pbs) && pbs.active)
        {
            pbs.active = false;
            Debug.Log("[hdrp-volume] PhysicallyBasedSky disabled (competed with the project's own sky dome)");
        }
        if (profile.TryGet<HDRISky>(out var hdri) && hdri.active)
        {
            hdri.active = false;
            Debug.Log("[hdrp-volume] HDRISky disabled");
        }
        if (profile.TryGet<VisualEnvironment>(out var ve))
        {
            // The project's sky is a Built-in *skybox material* (RenderSettings.skybox), and HDRP
            // ignores RenderSettings.skybox entirely - it only draws the sky selected here. With
            // skyType None the background rendered as a flat navy slab, which read as dusk and
            // removed the horizon line the coastal shots are composed around.
            //
            // A Gradient Sky is the right replacement for a stylised cel-shaded game: it is
            // HDRP-native, cheap, and directly art-directable, unlike PhysicallyBasedSky (which
            // previously blew the scene out) or HDRISky (no authored HDRI exists).
            ve.skyType.overrideState = true;
            ve.skyType.value = (int)SkyType.Gradient;
            Debug.Log("[hdrp-volume] VisualEnvironment skyType -> Gradient");
        }

        // Gradient bands sampled from the pre-HDRP baseline capture of the harbour road, so the
        // HDRP sky lands on the same soft blue-grey the region was art-directed against rather
        // than on an arbitrary default. Sampled values are display-space sRGB, so they are
        // converted to linear before being handed to HDRP as radiance.
        // PROVISIONAL: these are matched to the Built-in baseline, not to the SC01-SC08
        // reference set; the deliberate art-directed sky/atmosphere pass is spec sections 8-16.
        if (!profile.TryGet<GradientSky>(out var grad))
            grad = profile.Add<GradientSky>(true);

        grad.active = true;
        grad.top.overrideState = true;
        grad.top.value = new Color(0.557f, 0.604f, 0.686f).linear;
        grad.middle.overrideState = true;
        grad.middle.value = new Color(0.660f, 0.690f, 0.730f).linear;
        grad.bottom.overrideState = true;
        grad.bottom.value = new Color(0.800f, 0.800f, 0.780f).linear;
        grad.gradientDiffusion.overrideState = true;
        grad.gradientDiffusion.value = 1.0f;
        Debug.Log("[hdrp-volume] GradientSky enabled (top/middle/bottom matched to the Built-in baseline)");

        if (profile.TryGet<Fog>(out var fogOverride) && fogOverride.active)
        {
            fogOverride.active = false;
            Debug.Log("[hdrp-volume] HDRP Fog disabled (project authors its own depth haze)");
        }

        // HDRP's stock profile also ships Bloom, screen-space AO and a filmic tonemap that
        // the Built-in baseline never had. On this kit they are not a style choice, they are
        // a regression: the ocean's sun-glint specular blew into a white disc that swallowed
        // the whole harbour. This milestone's bar is "no regression against the pre-HDRP
        // baseline", so the post chain is neutralised here and re-introduced deliberately as
        // art direction later. PROVISIONAL.
        if (profile.TryGet<Bloom>(out var bloom) && bloom.active)
        {
            bloom.active = false;
            Debug.Log("[hdrp-volume] Bloom disabled (not present in the Built-in baseline)");
        }

        if (profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ssao) && ssao.active)
        {
            ssao.active = false;
            Debug.Log("[hdrp-volume] ScreenSpaceAmbientOcclusion disabled (baseline parity)");
        }

        if (profile.TryGet<Tonemapping>(out var tone))
        {
            tone.active = true;
            tone.mode.overrideState = true;
            tone.mode.value = TonemappingMode.None;
            Debug.Log("[hdrp-volume] Tonemapping set to None (baseline parity)");
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[hdrp-volume] done -> {path}");
    }
}

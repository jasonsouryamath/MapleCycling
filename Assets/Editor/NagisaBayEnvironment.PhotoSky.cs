using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using HD = UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Photoreal Nagisa Bay sky (2026-10-02, Claude): a layered, sun-lit HDRP CloudLayer driven by a
/// procedural photographic cloud map (tools/art/make_nagisa_cloudmap.py) over a hazier gradient.
///
/// The sky stays a GradientSky on purpose. Every Nagisa material is calibrated to the project's
/// unit-light convention and derives its ambient from the sky probe; a physically based sky
/// would put 10^4-nit radiance behind that rig and wreck the exposure.
///
/// Layer plan (the map packs one cloud family per channel):
///   layer A  1900 m   R = cumulus masses, B = small humilis puffs; lit, thick, soft shadowed undersides
///   layer B  7500 m   G = cirrus filaments; thin, unlit, rotated so it never lines up with layer A
/// WeatherSky (priority 60) still owns opacity + wind at runtime.
/// </summary>
public static partial class NagisaBayEnvironment
{
    public const string CloudMapPath = Dir + "/Textures/Nagisa_CloudMap.png";

    /// <summary>Re-apply only the sky to the existing Nagisa profile; no scene rebuild needed.</summary>
    public static void ApplyPhotoSkyOnly()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{Dir}/Nagisa_SkyProfile.asset");
        if (profile == null) { Debug.LogError("[nagisa-sky] Nagisa_SkyProfile.asset missing; run the full Nagisa Apply first."); return; }
        FillAtmosphereProfile(profile);   // whole atmosphere: sky, clouds, fog, exposure, grade
        AssetDatabase.SaveAssets();
    }

    private static void ApplyPhotoSky(VolumeProfile profile)
    {
        ConfigureCloudMapImport();
        var map = AssetDatabase.LoadAssetAtPath<Texture2D>(CloudMapPath);
        if (map == null) { Debug.LogWarning("[nagisa-sky] cloud map missing; keeping the previous sky."); return; }

        var ve = Ensure<HD.VisualEnvironment>(profile);
        ve.skyType.overrideState = true; ve.skyType.value = (int)HD.SkyType.Gradient;
        ve.cloudType.overrideState = true; ve.cloudType.value = (int)HD.CloudType.CloudLayer;

        // Deeper zenith, long soft fade into a pale warm horizon haze (aerial perspective).
        var sky = Ensure<HD.GradientSky>(profile);
        sky.top.overrideState = true; sky.top.value = Srgb(0x62, 0xA2, 0xE2).linear;
        sky.middle.overrideState = true; sky.middle.value = Srgb(0x9A, 0xC6, 0xEE).linear;
        sky.bottom.overrideState = true; sky.bottom.value = Srgb(0xE6, 0xEF, 0xF5).linear;
        sky.gradientDiffusion.overrideState = true; sky.gradientDiffusion.value = 3.2f;

        var c = Ensure<HD.CloudLayer>(profile);
        Set(c.opacity, 1f);
        Set(c.upperHemisphereOnly, true);
        Set(c.layers, HD.CloudMapMode.Double);
        Set(c.resolution, HD.CloudResolution.CloudResolution2048);
        Set(c.shadowMultiplier, 1f);

        var a = c.layerA;
        Set(a.cloudMap, map);
        Set(a.opacityR, 1f); Set(a.opacityG, 0f); Set(a.opacityB, 0.75f); Set(a.opacityA, 0f);
        Set(a.altitude, 1900f);
        Set(a.rotation, 0f);
        Set(a.tint, new Color(1f, 0.985f, 0.96f));
        // Lit clouds use the sun's real intensity, and this project's sun is a unit-scale 1.2, so
        // at exposure 0 they render nearly invisible against the sky (verified by capture).
        Set(a.exposure, 2.5f);
        Set(a.lighting, true);
        Set(a.steps, 10);
        Set(a.thickness, 0.62f);
        Set(a.ambientProbeDimmer, 0.85f);
        Set(a.castShadows, false);

        var b = c.layerB;
        Set(b.cloudMap, map);
        Set(b.opacityR, 0f); Set(b.opacityG, 0.8f); Set(b.opacityB, 0f); Set(b.opacityA, 0f);
        Set(b.altitude, 7500f);
        Set(b.rotation, 137f);
        Set(b.tint, new Color(0.95f, 0.975f, 1f));
        Set(b.exposure, 1.5f);
        Set(b.lighting, false);
        Set(b.castShadows, false);

        EditorUtility.SetDirty(profile);
        Debug.Log("[nagisa-sky] photoreal sky applied: 2-layer lit CloudLayer from Nagisa_CloudMap + hazier gradient.");
    }

    private static void Set<T>(VolumeParameter<T> p, T v) { p.overrideState = true; p.value = v; }

    /// <summary>Cloud map is DATA (masks): linear, high-quality, no sRGB curve, repeat in longitude.</summary>
    private static void ConfigureCloudMapImport()
    {
        var importer = AssetImporter.GetAtPath(CloudMapPath) as TextureImporter;
        if (importer == null) return;
        bool dirty = importer.sRGBTexture || importer.maxTextureSize != 4096 ||
                     importer.textureCompression != TextureImporterCompression.CompressedHQ || !importer.mipmapEnabled;
        if (!dirty) return;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.wrapModeU = TextureWrapMode.Repeat; importer.wrapModeV = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.SaveAndReimport();
    }
}

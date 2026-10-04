using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Global "anime high-quality" render pass on the project's DEFAULT volume profile, i.e. every
/// map at once (2026-09-24). Only settings the audit found missing are touched:
///   * Screen-space ambient occlusion was OFF, so props, riders and kerbs had no contact
///     darkening and read as pasted onto the ground. Kept soft (anime look, not gritty AAA).
///   * Directional shadows stopped at 150 m, so palms/buildings visibly lost their shadows
///     just beyond the rider. Extended to 300 m.
/// Grade, bloom, DOF and haze are NOT touched here - they are the per-region Ambience values
/// rendered by SakuraGradeCustomPass. ALL VALUES PROVISIONAL art tuning.
/// Not reversible from here: to undo, set SSAO inactive and maxShadowDistance back to 150.
/// Menu: MapleRide/Environment/Apply HDRP Quality Pass
/// </summary>
public static class HdrpQualityPass
{
    [MenuItem("MapleRide/Environment/Apply HDRP Quality Pass")]
    public static void Apply()
    {
        if (!GraphicsSettings.TryGetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>(out var dv) ||
            dv.volumeProfile == null)
        {
            Debug.LogError("[hdrp-quality] no default volume profile.");
            return;
        }
        var profile = dv.volumeProfile;

        if (!profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ao))
            ao = profile.Add<ScreenSpaceAmbientOcclusion>(true);
        ao.active = true;
        ao.intensity.Override(0.75f);
        ao.radius.Override(1.4f);
        ao.directLightingStrength.Override(0.25f);

        if (profile.TryGet<HDShadowSettings>(out var sh))
        {
            sh.maxShadowDistance.Override(300f);
            sh.cascadeShadowSplitCount.Override(4);
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[hdrp-quality] SSAO on (0.75 / 1.4 m), shadows to 300 m x4 cascades.");
    }
}

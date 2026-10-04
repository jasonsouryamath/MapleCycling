using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Read-only probe: reports the state that actually decides how bright the converted scene
/// looks - HDRP's default volume overrides (exposure, fog, sky) and the scene's key light.
/// Written because the first post-conversion render was washed out, and this project has a
/// standing rule that a visual fault is diagnosed from facts, not guessed at.
/// </summary>
public static class HdrpLightingProbe
{
    [MenuItem("MapleRide/Environment/Probe HDRP Lighting State")]
    public static void Run()
    {
        // RenderSettings (ambient, sun) are per-scene, so in batchmode the probe has to open
        // the scene it is reporting on or it silently describes an empty Untitled scene.
        // Opened read-only: never saved, matching the rule that a diagnostic must not write.
        const string scenePath = "Assets/Scenes/SakuraPass.unity";
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != scenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        var gs = GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>();
        Debug.Log($"[hdrp-probe] global settings asset: {(gs != null ? gs.name : "<null>")}");

        // Unity 6 moved the default volume profile out of HDRenderPipelineGlobalSettings
        // (now internal) into a graphics settings record.
        VolumeProfile profile = null;
        if (GraphicsSettings.TryGetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>(out var dv))
            profile = dv.volumeProfile;
        {
            Debug.Log($"[hdrp-probe] default volume profile: {(profile != null ? AssetDatabase.GetAssetPath(profile) : "<null>")}");
            if (profile != null)
            {
                foreach (var comp in profile.components)
                {
                    Debug.Log($"[hdrp-probe]   override: {comp.GetType().Name} active={comp.active}");
                    if (comp is Exposure ex)
                        Debug.Log($"[hdrp-probe]     exposure mode={ex.mode.value} fixedValue={ex.fixedExposure.value} comp={ex.compensation.value}");
                    if (comp is Fog fog)
                        Debug.Log($"[hdrp-probe]     fog enabled={fog.enabled.value} attenDist={fog.meanFreePath.value} maxHeight={fog.maximumHeight.value} albedo={fog.albedo.value}");
                    if (comp is VisualEnvironment ve)
                        Debug.Log($"[hdrp-probe]     visualEnv skyType={ve.skyType.value} ambientMode={ve.skyAmbientMode.value}");
                }
            }
        }

        var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[hdrp-probe] scene volumes: {volumes.Length}");
        foreach (var v in volumes.Take(10))
            Debug.Log($"[hdrp-probe]   volume '{v.name}' global={v.isGlobal} priority={v.priority} profile={(v.sharedProfile != null ? v.sharedProfile.name : "<null>")}");

        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                           .Where(l => l.type == LightType.Directional).ToArray();
        Debug.Log($"[hdrp-probe] directional lights: {lights.Length}");
        foreach (var l in lights)
        {
            var hd = l.GetComponent<HDAdditionalLightData>();
            Debug.Log($"[hdrp-probe]   '{l.name}' intensity={l.intensity} color={l.color} " +
                      $"hdIntensity={(hd != null ? hd.intensity.ToString("0.##") : "<no HD data>")} " +
                      $"unit={(hd != null ? hd.lightUnit.ToString() : "-")} shadows={l.shadows}");
        }

        Debug.Log($"[hdrp-probe] RenderSettings.fog={RenderSettings.fog} mode={RenderSettings.fogMode} " +
                  $"color={RenderSettings.fogColor} density={RenderSettings.fogDensity} " +
                  $"start={RenderSettings.fogStartDistance} end={RenderSettings.fogEndDistance} " +
                  $"ambientMode={RenderSettings.ambientMode} " +
                  $"sky={RenderSettings.ambientSkyColor} eq={RenderSettings.ambientEquatorColor} gnd={RenderSettings.ambientGroundColor}");
    }
}

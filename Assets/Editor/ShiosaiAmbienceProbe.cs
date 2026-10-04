// Diagnostic for the "tuned ambience renders flat" discrepancy at the ShiosaiCoast start line.
//
// The start renders come out shadowless and pastel even though RegionDirector.ShiosaiAmbience
// carries a warm 30 deg key with keyShadows = true and shadowStrength 0.84. Something between
// that struct and the pixels is dropping the sun. This dumps the ACTUAL post-ApplyAmbience
// state - lights, skybox, ambient, fog and the quality level's shadow settings - so the break
// is identified rather than guessed at.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShiosaiAmbienceProbe
{
    [MenuItem("MapleRide/Environment/Probe Shiosai Ambience")]
    public static void Probe()
    {
        const string scenePath = "Assets/Scenes/SakuraPass.unity";
        if (EditorSceneManager.GetActiveScene().path != scenePath)
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.Log("[amb] NO RegionDirector"); return; }
        regions.Resolve();
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        // --- the serialized sky slots: a null shiosaiSky means ApplyAmbience silently leaves
        // --- whatever skybox the previous region set (i.e. Sakura's dusk gradient).
        var sky = regions.SkyFor(RegionCatalog.ShiosaiCoast);
        Debug.Log($"[amb] SkyFor(shiosai) = {(sky == null ? "<NULL>" : sky.name + " shader=" + sky.shader.name)}");
        Debug.Log($"[amb] RenderSettings.skybox = {(RenderSettings.skybox == null ? "<NULL>" : RenderSettings.skybox.name + " shader=" + RenderSettings.skybox.shader.name)}");
        if (RenderSettings.skybox != null)
        {
            var m = RenderSettings.skybox;
            foreach (var prop in new[] { "_SkyTop", "_SkyHorizon", "_SkyZenith", "_GroundColor",
                                         "_SunDirection", "_CloudTex", "_CloudStrength", "_CloudColor" })
                if (m.HasProperty(prop))
                    Debug.Log($"[amb]   sky.{prop} = {DescribeProp(m, prop)}");
        }

        Debug.Log($"[amb] ambientMode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity}");
        Debug.Log($"[amb]   sky={RenderSettings.ambientSkyColor} eq={RenderSettings.ambientEquatorColor} gnd={RenderSettings.ambientGroundColor}");
        Debug.Log($"[amb] fog={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity} colour={RenderSettings.fogColor}");
        Debug.Log($"[amb] RenderSettings.sun = {(RenderSettings.sun == null ? "<NULL>" : RenderSettings.sun.name)}");

        // --- quality level: this is where a shadow-less render usually comes from.
        Debug.Log($"[amb] quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}' " +
                  $"(index {QualitySettings.GetQualityLevel()})");
        Debug.Log($"[amb] QualitySettings.shadows={QualitySettings.shadows} " +
                  $"res={QualitySettings.shadowResolution} proj={QualitySettings.shadowProjection} " +
                  $"cascades={QualitySettings.shadowCascades} distance={QualitySettings.shadowDistance} " +
                  $"pixelLightCount={QualitySettings.pixelLightCount}");

        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            Debug.Log($"[amb] DIR '{l.name}' active={l.gameObject.activeInHierarchy} enabled={l.enabled} " +
                      $"intensity={l.intensity} shadows={l.shadows} strength={l.shadowStrength} " +
                      $"bias={l.shadowBias} normalBias={l.shadowNormalBias} nearPlane={l.shadowNearPlane} " +
                      $"renderMode={l.renderMode} cullingMask=0x{l.cullingMask:X8} " +
                      $"colour={l.color} euler={l.transform.eulerAngles}");
        }

        // --- do the meshes at the start actually cast/receive?
        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        var p0 = route.Position[0];
        var box = new Bounds(p0 + Vector3.up * 8f, new Vector3(120f, 80f, 120f));
        int cast = 0, nocast = 0, recv = 0, norecv = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.bounds.Intersects(box)) continue;
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) nocast++; else cast++;
            if (r.receiveShadows) recv++; else norecv++;
        }
        Debug.Log($"[amb] start-box renderers: castShadows on={cast} off={nocast} | receiveShadows on={recv} off={norecv}");

        // --- the apron actually exists now?
        var apron = GameObject.Find("Shiosai Coast Environment/Coast Start Apron");
        Debug.Log($"[amb] Coast Start Apron = {(apron == null ? "<MISSING>" : apron.transform.childCount + " children")}");
    }

    private static string DescribeProp(Material m, string prop)
    {
        int id = Shader.PropertyToID(prop);
        var t = m.shader.GetPropertyType(m.shader.FindPropertyIndex(prop));
        switch (t)
        {
            case UnityEngine.Rendering.ShaderPropertyType.Color: return m.GetColor(id).ToString();
            case UnityEngine.Rendering.ShaderPropertyType.Vector: return m.GetVector(id).ToString();
            case UnityEngine.Rendering.ShaderPropertyType.Texture:
                var tex = m.GetTexture(id); return tex == null ? "<null tex>" : tex.name;
            default: return m.GetFloat(id).ToString("0.###");
        }
    }
}

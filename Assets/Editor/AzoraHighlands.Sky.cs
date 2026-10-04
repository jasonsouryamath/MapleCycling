using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// AZORA SKY + SUN (claude 2026-09-26; reworked by copilot 2026-09-26 after the first render).
/// User: "better photorealistic looking clear skies and better sun".
///
/// WHY A VOLUME, NOT THE Azora_Sky MATERIAL. HDRP ignores RenderSettings.skybox, so the
/// per-region sky materials RegionDirector swaps are never drawn: every region renders the ONE
/// global GradientSky from HdrpVolumeSetup (a flat grey-blue three-stop gradient), which is why
/// Azora's sky read as a CG backdrop with no sun in it.
///
/// This stages an "Azora Sky Volume" UNDER the Azora root (the Shiosai sky-volume pattern):
/// RegionDirector only activates the root while Azora is ridden, so nothing here can leak into
/// another region.
///
/// WHY NOT THE PHYSICALLY BASED SKY. Claude's first version used HDRP PBS. Its radiance scales
/// with the key light's illuminance, and this project lights everything with a ~1 lux sun at a
/// fixed EV100 0, so the render came back as a NIGHT sky (navy zenith, orange horizon band) with
/// a flat white disc that read as a moon (claude_az3_cap1, s4_ridge_chase 22:03). Matching PBS
/// would need ~+15 EV and its look would still depend on per-camera LUT precomputation, which
/// the single-frame batch captures do not guarantee. So:
///  * SKY: a GradientSky with PHOTOGRAPHIC stops read off clear high-altitude winter skies - a
///    deep saturated zenith (thin, dry air at 2 km), falling fast to a bright pale haze band at
///    the horizon. Deterministic, no exposure coupling. No cloud layer (clear sky).
///  * SUN: MapleRide/HDRP/AzoraSun, one self-placing additive sprite on the key-light direction
///    (see that shader): a small HDR disc that blooms, plus a painted two-lobe aureole.
/// HDRP fog is off in this volume; SakuraPostFX's aerial stage owns distance haze here.
/// ALL numbers are PROVISIONAL art tuning (logs copilot_az3_*).
/// </summary>
public static partial class AzoraHighlandsEnvironment
{
    private const string SkyProfilePath = "Assets/Environment/AzoraHighlands/Azora_SkyProfile.asset";
    private const string SkyVolumeName = "Azora Sky Volume";
    private const string SunSpriteName = "Azora Sun";
    private const string SunShaderName = "MapleRide/HDRP/AzoraSun";

    // LINEAR radiance (not sRGB). RegionDirector.AzoraAmbience's post grade multiplies the whole
    // frame by ~0.71 (exposure 0.72; measured on copilot_az3_cap1: rendered/authored = 0.70-0.73
    // on every channel from 5 to 27 deg), which turned the first stops into a dusky indigo. So
    // each stop is the wanted ON-SCREEN colour, linearised, divided by ~0.71 (then hand-tuned by
    // render, copilot_az3_cap2/cap3): a deep dry-air alpine blue zenith, a bright pale haze band
    // at the horizon, and the same haze for what shows past the 9 km far clip.
    private static readonly Color AzoraSkyZenith = new Color(0.020f, 0.150f, 0.820f);
    // Red held a little under green/blue: a linear lerp from a warm-white horizon to the blue
    // zenith passes through lavender (cap2 plateau frame); a cooler horizon passes through clean
    // pale blue instead.
    private static readonly Color AzoraSkyHorizon = new Color(0.860f, 1.060f, 1.300f);
    private static readonly Color AzoraSkyBelow = new Color(0.950f, 1.120f, 1.280f);
    // 1.45: the zenith stop is only reached at ~44 deg. The gradient CLAMPS there, and any clamp
    // inside a frame draws a visible dark arch across the sky (1.8 -> an arch at 34 deg in every
    // sun-facing shot, 2.4 -> a band along the top of level shots); 1.25 was arch-free but read
    // pale at the ride camera's 5-15 deg (flow_azora_highlands/flow_7_riding). Deep zenith compensates.
    private const float AzoraSkyDiffusion = 1.45f;

    private static void BuildAzoraSkyVolume(Transform root)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SkyProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, SkyProfilePath);
        }

        var ve = SkyOverride<VisualEnvironment>(profile);
        ve.skyType.overrideState = true;
        ve.skyType.value = (int)SkyType.Gradient;
        ve.cloudType.overrideState = true;
        ve.cloudType.value = 0;                                   // clear sky: no cloud layer

        // The first version's PBS override lives on in the profile asset; switch it off so the
        // two can never compete.
        if (profile.TryGet<PhysicallyBasedSky>(out var pbs)) { pbs.active = false; EditorUtility.SetDirty(pbs); }

        var grad = SkyOverride<GradientSky>(profile);
        grad.top.overrideState = true;               grad.top.value = AzoraSkyZenith;
        grad.middle.overrideState = true;            grad.middle.value = AzoraSkyHorizon;
        grad.bottom.overrideState = true;            grad.bottom.value = AzoraSkyBelow;
        grad.gradientDiffusion.overrideState = true; grad.gradientDiffusion.value = AzoraSkyDiffusion;

        // No HDRP fog from this volume (the global profile's fog stays whatever it is elsewhere).
        var fog = SkyOverride<Fog>(profile);
        fog.enabled.overrideState = true; fog.enabled.value = false;
        EditorUtility.SetDirty(profile);

        var old = root.Find(SkyVolumeName);
        while (old != null) { Object.DestroyImmediate(old.gameObject); old = root.Find(SkyVolumeName); }
        var go = new GameObject(SkyVolumeName);
        go.transform.SetParent(root, false);
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 20f;                 // above the scene's global default profile
        vol.sharedProfile = profile;

        BuildAzoraSun(root);
        Debug.Log($"[azora] sky: gradient zenith {AzoraSkyZenith} -> horizon {AzoraSkyHorizon}, " +
                  $"diffusion {AzoraSkyDiffusion}, clear; sun sprite '{SunSpriteName}'; volume '{SkyVolumeName}'.");
    }

    /// <summary>
    /// The sun sprite. Its quad is re-placed every frame by the shader, so the object itself just
    /// needs a mesh whose bounds can never fail frustum culling.
    /// </summary>
    private static void BuildAzoraSun(Transform root)
    {
        var old = root.Find(SunSpriteName);
        while (old != null) { Object.DestroyImmediate(old.gameObject); old = root.Find(SunSpriteName); }

        var shader = Shader.Find(SunShaderName);
        if (shader == null) { Debug.LogWarning($"[azora] sky: '{SunShaderName}' missing - no sun sprite."); return; }
        var mat = LoadOrCreate("Azora_Sun", SunShaderName);
        mat.SetColor("_Color", new Color(1.00f, 0.965f, 0.900f, 1f));
        mat.SetFloat("_HalfAngle", 22f);
        mat.SetFloat("_DiscDeg", 0.48f);
        mat.SetFloat("_DiscIntensity", 14f);
        mat.SetFloat("_GlowNear", 0.95f);
        mat.SetFloat("_GlowNearDeg", 1.5f);
        mat.SetFloat("_GlowFar", 0.26f);
        mat.SetFloat("_GlowFarDeg", 8.5f);
        mat.SetFloat("_FarFraction", 0.985f);
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);

        var mesh = new Mesh { name = "Azora_SunQuad" };
        mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200000f);

        var sun = AddMesh(root, SunSpriteName, mesh, mat, collider: false);
        var mr = sun.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        ResetKeyLightDisc();
    }

    /// <summary>
    /// Claude's first (PBS) version enlarged the shared key light's sun disc and flare
    /// (angularDiameter 0.9, flareSize 3.2, flareFalloff 5). Only a physically based sky draws
    /// those, and no region uses one now, so they are put back to HDRP's defaults rather than
    /// left as a trap for whoever next enables PBS anywhere.
    /// </summary>
    private static void ResetKeyLightDisc()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (l == null || l.type != LightType.Directional || l.name != "Sakura Sunset Key") continue;
            var hd = l.GetComponent<HDAdditionalLightData>();
            if (hd == null) continue;
            hd.angularDiameter = 0.5f;
            hd.flareSize = 2f;
            hd.flareFalloff = 4f;
            hd.flareTint = Color.white;
            hd.flareMultiplier = 1f;
            hd.surfaceTint = Color.white;
            EditorUtility.SetDirty(hd);
        }
    }

    /// <summary>
    /// Fast sky iteration: re-stage ONLY the sky volume and sun under the existing Azora root and
    /// save, without the ~13 min full Apply. Converges by exact name, same as Apply.
    /// Usage: run_steps.ps1 "AzoraHighlandsEnvironment.StageSkyOnly|copilot_az3_sky.log|1"
    /// </summary>
    [MenuItem("MapleRide/Environment/Azora: Re-stage Sky Only")]
    public static void StageSkyOnly()
    {
        if (Application.isBatchMode && EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var roots = FindRootsByExactName(RootName);
        if (roots.Count == 0) { Debug.LogError($"[azora] sky: no '{RootName}' in the scene - run Apply first."); return; }
        BuildAzoraSkyVolume(roots[0].transform);
        AssetDatabase.SaveAssets();
        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[azora] sky re-staged and saved '{active.path}'.");
    }

    private static T SkyOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var c))
        {
            c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
        }
        c.active = true;
        return c;
    }
}

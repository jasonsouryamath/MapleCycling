using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Part B section 24 working tool: sweeps the ambient / cel-shade / shadow-strength budget and
/// renders the SAME benchmark marks for each candidate, in ONE launch, so the lighting balance
/// is chosen by LOOKING at frames rather than by arithmetic.
///
/// WHY IT EXISTS: directional shadows were invisible on every SakuraCel surface. A controlled
/// A/B (shadow_repro_inscene.png) showed a Standard receiver taking a crisp shadow in this very
/// scene while a SakuraCel receiver took none, and the measured cause is CLIPPING - the cel
/// shader has no energy conservation, and key + high-key Trilight ambient pushes lit AND
/// shadowed values past 1.0, so the shadow is arithmetically present but crushed out of range.
/// Three levers move that: ambient intensity, the material shade floor (_ShadeColor *
/// _ShadeStrength) and the light's shadow strength. This sweeps them together because they only
/// mean anything together.
/// </summary>
public static class SakuraLightingSweep
{
    private const int Width = 1600, Height = 900;
    private const float Fov = 52f;
    private const float CamUpM = 1.60f, CamBackM = 4.20f, CamAheadM = 14f, CamAimUpM = 1.25f;

    private struct Config
    {
        public string Name;
        public float AmbientIntensity;
        public float ShadeStrength;     // pushed onto every SakuraCel material in the scene
        public float ShadowStrength;
        public float KeyIntensity;
        public string Why;
    }

    // PROVISIONAL candidates. Config 0 is the control - exactly what ships today.
    private static readonly Config[] Configs =
    {
        new Config { Name = "a_control", AmbientIntensity = 0.68f, ShadeStrength = 0.55f,
                     ShadowStrength = 0.62f, KeyIntensity = 0.92f,
                     Why = "current shipping balance - the one with no visible shadows" },
        new Config { Name = "b_mild",    AmbientIntensity = 0.52f, ShadeStrength = 0.44f,
                     ShadowStrength = 0.72f, KeyIntensity = 0.98f,
                     Why = "smallest change that should clear the clip ceiling" },
        new Config { Name = "c_target",  AmbientIntensity = 0.44f, ShadeStrength = 0.36f,
                     ShadowStrength = 0.80f, KeyIntensity = 1.02f,
                     Why = "section 24 target: real dapple, ambient still lifts the shade" },
        new Config { Name = "d_deep",    AmbientIntensity = 0.34f, ShadeStrength = 0.28f,
                     ShadowStrength = 0.90f, KeyIntensity = 1.06f,
                     Why = "deliberately too far, to bracket the answer from the dark side" },
    };

    // The two marks that show canopy dapple best.
    private static readonly (float D, string Name)[] Marks =
    {
        (365f, "bend"), (430f, "climb"),
    };

    public static void Run()
    {
        EditorSceneManager.OpenScene(SakuraSceneDefaults.ScenePath, OpenSceneMode.Single);
        int fixedCount = SakuraSceneDefaults.Fix();
        if (fixedCount > 0)
            Debug.LogWarning($"[sweep] region visibility corrected on {fixedCount} root(s).");

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        if (route == null || route.Count == 0) { Debug.LogError("[sweep] no route"); return; }

        var key = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(l => l.name == "Sakura Sunset Key");
        if (key == null) { Debug.LogError("[sweep] no key light"); return; }

        // Every SakuraCel material actually present in the scene, deduplicated. Collected once
        // so each config only has to push a float.
        var celMats = new HashSet<Material>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name == "MapleRide/SakuraCel"
                    && m.HasProperty("_ShadeStrength"))
                    celMats.Add(m);
        }
        // The distant backdrop rings deliberately run a very flat shade (strength 0.18) so haze
        // reads correctly; sweeping them would wreck the depth layers. Leave them alone.
        var sweepMats = celMats.Where(m => !m.name.Contains("DistantRange")).ToList();
        Debug.Log($"[sweep] {sweepMats.Count} SakuraCel materials in play " +
                  $"({celMats.Count - sweepMats.Count} backdrop material(s) excluded).");

        var original = sweepMats.ToDictionary(m => m, m => m.GetFloat("_ShadeStrength"));

        var dir = MapleRidePaths.RenderDir("benchmark/sweep");
        Directory.CreateDirectory(dir);

        // Shadows on and generous, identically for every config: the sweep is about the BUDGET,
        // not about whether shadows are switched on.
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowDistance = 150f;
        key.shadows = LightShadows.Soft;
        key.renderMode = LightRenderMode.ForcePixel;
        key.transform.rotation = Quaternion.Euler(30f, 62f, 0f);

        foreach (var c in Configs)
        {
            RenderSettings.ambientIntensity = c.AmbientIntensity;
            key.shadowStrength = c.ShadowStrength;
            key.intensity = c.KeyIntensity;
            foreach (var m in sweepMats) m.SetFloat("_ShadeStrength", c.ShadeStrength);

            Debug.Log($"[sweep] {c.Name}: ambient {c.AmbientIntensity:0.00}, shade {c.ShadeStrength:0.00}, " +
                      $"shadowStrength {c.ShadowStrength:0.00}, key {c.KeyIntensity:0.00} - {c.Why}");

            foreach (var mk in Marks)
            {
                int iBack = route.IndexAt(Mathf.Max(0f, mk.D - CamBackM));
                int iAhead = route.IndexAt(Mathf.Min(mk.D + CamAheadM, route.Length));
                var camPos = route.Position[iBack] + Vector3.up * CamUpM;
                var aim = route.Position[iAhead] + Vector3.up * CamAimUpM;
                Shot(dir, $"sweep_{c.Name}_{mk.Name}", camPos, aim);
            }
        }

        // Restore the authored values - the sweep decides, it does not commit.
        foreach (var kv in original) kv.Key.SetFloat("_ShadeStrength", kv.Value);
        Debug.Log("[sweep] restored original _ShadeStrength on every swept material.");

        // Reopen clean so nothing from the sweep is persisted.
        EditorSceneManager.OpenScene(SakuraSceneDefaults.ScenePath, OpenSceneMode.Single);
        Debug.Log("[sweep] complete -> " + dir);
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 aim)
    {
        var go = new GameObject("~SweepCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(aim, Vector3.up);
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 9000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            cam.renderingPath = RenderingPath.Forward;
            SakuraPassEnvironment.TunePostFX(go.AddComponent<SakuraPostFX>());

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            img.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
            RenderTexture.active = prev;

            cam.targetTexture = null;
            Object.DestroyImmediate(img);
            Object.DestroyImmediate(rt);
            Debug.Log($"[sweep] wrote {name}.png");
        }
        finally { Object.DestroyImmediate(go); }
    }
}

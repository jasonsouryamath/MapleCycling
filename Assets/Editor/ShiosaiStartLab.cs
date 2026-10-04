// Render-verify LAB for the ShiosaiCoast start line.
//
// The start frames come out flat and pastel even though the scene's sun, shadow quality and
// sky material all probe as correct (see ShiosaiAmbienceProbe). Reading more code cannot settle
// that - only looking at pixels can. This renders the SAME chase frame under a matrix of
// single-variable changes so the responsible stage is identified by eye in one Unity launch
// instead of one launch per guess.
//
// Everything here is diagnostic only: it renders to reference/good_graphics/lab_* and never
// mutates the saved scene (fog and quality settings are restored).
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShiosaiStartLab
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Environment/Lab Shiosai Start Variants")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogError("[lab] no RegionDirector"); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        var p = route.Position[0];
        var t = new Vector3(route.Tangent[0].x, 0f, route.Tangent[0].z).normalized;
        var s = route.SideFlat(0);
        var eye = p - t * 6.5f + Vector3.up * 2.4f - s * 0.6f;
        var look = p + t * 22f + Vector3.up * 1.4f;

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        bool fogWas = RenderSettings.fog;
        float shadowWas = QualitySettings.shadowDistance;

        // 1) The control: exactly what CaptureStart renders today.
        Shot(dir, "lab_1_base", eye, look, true, null);

        // 2) No post-FX at all. Separates "the SCENE is flat" from "the GRADE flattens it".
        Shot(dir, "lab_2_nopostfx", eye, look, false, null);

        // 3) Post-FX on, but the atmosphere stage (aerial tint + height mist) disabled. These
        //    are the two stages that can wash a saturated sky to pale cream.
        Shot(dir, "lab_3_noatmos", eye, look, true, fx =>
        {
            fx.aerialTintAmount = 0f; fx.aerialDesaturation = 0f; fx.aerialFlatten = 0f;
            fx.mistStrength = 0f;
        });

        // 4) Post-FX on, depth of field off. DOF at 150 m / 520 m blurs everything past the
        //    carriageway and can read as "flat cardboard background".
        Shot(dir, "lab_4_nodof", eye, look, true, fx => { fx.dofStrength = 0f; });

        // 5) Scene fog off (exp-squared fog is scene state, not camera state).
        RenderSettings.fog = false;
        Shot(dir, "lab_5_nofog", eye, look, true, null);
        RenderSettings.fog = fogWas;

        // 6) Raw scene, no post-FX, no fog, generous shadow volume: what the LIGHTING alone
        //    actually looks like. If there are no cast shadows in THIS frame the break is in the
        //    lighting/geometry; if there are, the grade is eating them.
        RenderSettings.fog = false;
        QualitySettings.shadowDistance = 300f;
        Shot(dir, "lab_6_rawlight", eye, look, false, null);
        QualitySettings.shadowDistance = shadowWas;
        RenderSettings.fog = fogWas;

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log("[lab] variant matrix done.");
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look,
                             bool postFx, Action<SakuraPostFX> tweak)
    {
        var go = new GameObject("~ShiosaiLabCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 30000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        if (postFx)
        {
            var a = RegionDirector.ShiosaiAmbience;
            var fx = go.AddComponent<SakuraPostFX>();
            fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
            fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
            fx.lift = a.lift; fx.gain = a.gain;
            fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
            fx.dofFocusDistance = a.dofFocusDistance; fx.dofFocusRange = a.dofFocusRange;
            fx.dofStrength = a.dofStrength;
            if (a.dofFalloff > 0.001f) fx.dofFalloff = a.dofFalloff;
            if (a.dofIterations > 0) fx.dofIterations = a.dofIterations;
            if (a.aerialRange > 0f)
            {
                fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange;
                fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
                fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
                fx.mistBaseY = a.mistBaseY; fx.mistTopY = a.mistTopY;
                fx.mistStrength = a.mistStrength; fx.mistStart = a.mistStart;
                fx.mistColor = a.mistColor;
            }
            tweak?.Invoke(fx);
        }

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());

        RenderTexture.active = prev;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(img);
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log($"[lab] wrote {name}.png");
    }
}

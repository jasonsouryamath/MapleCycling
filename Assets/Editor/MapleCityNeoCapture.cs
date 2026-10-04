using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Captures the vaporwave neo-Tokyo Maple City (both districts + the megatower backdrop) under the ACTIVE region ambience
/// (RegionDirector.AmbienceFor), unlike MapleCityDiagnostics which hard-codes the old warm grade. Output: docs/maple_neo_captures/.
/// Run: MapleCityNeoCapture.Run
/// </summary>
public static class MapleCityNeoCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/maple_neo_captures"));
        Directory.CreateDirectory(dir);
        string tag = System.Environment.GetEnvironmentVariable("MR_NEO_TAG") ?? "";
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogError("[neo-cap] no RegionDirector"); EditorApplication.Exit(2); return; }
        regions.Resolve();
        regions.currentRegionId = RegionCatalog.MapleCity;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var oldR = MapleCityEnvironment.CityRoute.Load();
        var eastR = MapleCityEnvironment.CityRoute.Load(MapleCityEnvironment.EastRoutePath);
        // street-level shots: (name, route, fraction)
        (string, MapleCityEnvironment.CityRoute, float)[] stops =
        {
            ("old_gate", oldR, 0.00f), ("old_canal", oldR, 0.16f), ("old_blvd", oldR, 0.74f),
            ("east_gate", eastR, 0.02f), ("east_harbour", eastR, 0.10f), ("east_market", eastR, 0.31f),
            ("east_hill", eastR, 0.46f), ("east_lantern", eastR, 0.72f), ("east_ferry", eastR, 0.91f),
        };
        foreach (var (name, r, f) in stops)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(f * (r.Count - 1)), 0, r.Count - 2);
            Vector3 p = r.Position[i] + Vector3.up * 2.2f;
            Vector3 t = new Vector3(r.Tangent[i].x, 0f, r.Tangent[i].z).normalized;
            Shot(dir, $"{name}{tag}", p - t * 6f + r.SideFlat(i) * -1.7f, p + t * 60f + Vector3.up * 3f, 62f);
            // look-up shot: the elevated rail, signs and holograms overhead
            Shot(dir, $"{name}_up{tag}", p, p + t * 40f + Vector3.up * 26f, 70f);
        }
        // backdrop: megatower + airships from beyond the East district
        Vector3 c0 = Vector3.zero, c1 = Vector3.zero;
        for (int i = 0; i < oldR.Count; i++) c0 += oldR.Position[i]; c0 /= oldR.Count;
        for (int i = 0; i < eastR.Count; i++) c1 += eastR.Position[i]; c1 /= eastR.Count;
        Vector3 mid = (c0 + c1) * 0.5f;
        Shot(dir, $"backdrop_tower{tag}", mid + new Vector3(760f, 70f, -340f), new Vector3(mid.x, 150f, mid.z), 55f);
        // airship close-up: ship 1 orbits at radius 650 m, height ~150 m, phase 0 -> east of the midpoint at t=0
        Shot(dir, $"airship{tag}", mid + new Vector3(330f, 95f, -140f), new Vector3(mid.x + 650f, 150f, mid.z), 50f);
        Shot(dir, $"backdrop_wide{tag}", mid + new Vector3(0f, 420f, -1500f), mid + new Vector3(0f, 110f, 0f), 62f);
        Debug.Log("[neo-cap] done");
        EditorApplication.Exit(0);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~NeoCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.2f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.AmbienceFor(RegionCatalog.MapleCity);
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain; fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofFocusDistance = a.dofFocusDistance; fx.dofFocusRange = a.dofFocusRange; fx.dofFalloff = a.dofFalloff; fx.dofStrength = a.dofStrength;
        fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange; fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
        fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
        fx.mistBaseY = a.mistBaseY; fx.mistTopY = a.mistTopY; fx.mistStrength = a.mistStrength; fx.mistStart = a.mistStart; fx.mistColor = a.mistColor;
        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        Debug.Log($"[neo-cap] wrote {name}.png");
    }
}

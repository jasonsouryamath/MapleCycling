using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// READ-ONLY QA probe for the "player Kuro renders chrome / blown-out white" complaint.
/// 1) Enumerates every renderer material on the ACTUAL player ("Kuro on Sakura Pass"):
///    shader, _SpecStrength, _Gloss, _Metallic/metallicFactor, roughnessFactor, _Color.
/// 2) Renders a tight HEAD/HELMET shot and an UPPER-BODY shot under Shiosai ambience,
///    where the complaint was filed. Never saves the scene.
/// </summary>
public static class QaKuroChromeProbe
{
    const string Scene = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/QA Kuro Chrome Probe")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != Scene)
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/qa_kuro_chrome"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[qa-chrome] player not found."); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        var sb = new StringBuilder();
        sb.AppendLine("[qa-chrome] MATERIAL INVENTORY for 'Kuro on Sakura Pass'");
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { sb.AppendLine($"  {r.name} -> <NULL MATERIAL>"); continue; }
                string spec = m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength").ToString("F3") : "-";
                string gloss = m.HasProperty("_Gloss") ? m.GetFloat("_Gloss").ToString("F3") : "-";
                string met = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString("F2")
                            : (m.HasProperty("metallicFactor") ? "mf=" + m.GetFloat("metallicFactor").ToString("F2") : "-");
                string rough = m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor").ToString("F2") : "-";
                string smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness").ToString("F2") : "-";
                Color col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.magenta;
                sb.AppendLine($"  {r.name} | mat={m.name} | shader={m.shader.name} | spec={spec} gloss={gloss} metallic={met} rough={rough} smooth={smooth} color=({col.r:F2},{col.g:F2},{col.b:F2})");
            }
        }
        Debug.Log(sb.ToString());

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) { regions.Resolve(); regions.currentRegionId = RegionCatalog.ShiosaiCoast; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }

        // Estimate head height from renderer bounds.
        Bounds b = default; bool has = false;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        float top = has ? b.max.y : player.transform.position.y + 1.6f;

        Vector3 f = player.transform.forward, up = Vector3.up, r2 = player.transform.right;
        Vector3 head = new Vector3(player.transform.position.x, top - 0.12f, player.transform.position.z);
        Vector3 chest = new Vector3(player.transform.position.x, top - 0.45f, player.transform.position.z);

        Shot(dir, "head_rear",  head + (-f + up * 0.15f).normalized * 0.75f, head, 42f);
        Shot(dir, "head_q34",   head + (-f + r2 + up * 0.2f).normalized * 0.8f, head, 42f);
        Shot(dir, "upper_rear", chest + (-f + up * 0.25f).normalized * 1.6f, chest, 45f);

        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[qa-chrome] done (scene discarded).");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var a = RegionDirector.ShiosaiAmbience;
        var go = new GameObject("~QaChromeCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 1000, h = 1000;
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
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[qa-chrome] wrote {name}.png");
    }
}

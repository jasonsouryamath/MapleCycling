using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Close-up verification renders for the player avatar (Kuro) inside the REAL SakuraPass
/// lighting rig, so material changes are judged against the same "Sakura Sunset Key" sun,
/// ambient and post-FX grade the game actually ships with.
///
/// KuroPreview.unity is deliberately NOT used: it contains a stale "Kuro MapleStory Toon"
/// object with its own single Key Light, so it cannot tell us anything about how the player
/// responds to the shipping environment lighting.
///
/// Tag the output set with -kuroTag &lt;name&gt;, e.g. "before" / "after":
///   Unity.exe -batchmode -projectPath &lt;abs&gt; -executeMethod KuroPhotorealCapture.Capture -kuroTag before
public static class KuroPhotorealCapture
{
    const string ScenePath  = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    // Application.dataPath is <project>/Assets, so the repo-root good_graphics is two up.
    const string OutDir     = "../reference/good_graphics";

    [MenuItem("MapleRide/Kuro/Capture Photoreal Closeups", priority = 40)]
    public static void CaptureMenu() => Capture();

    public static void Capture()
    {
        string tag = ArgValue("-kuroTag") ?? "now";

        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[kurophoto] no player object."); EditorApplication.Exit(1); return; }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
        Directory.CreateDirectory(dir);

        ReportLighting(player);

        // Frame off the BODY only (bounds of the whole rig include the bike wheels and would
        // push a "head" shot too far out to read pore/fabric detail).
        var body = player.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
        if (body == null) { Debug.LogError("[kurophoto] player has no SkinnedMeshRenderer."); EditorApplication.Exit(1); return; }

        var rigB = Encapsulate(player.GetComponentsInChildren<Renderer>(true));
        var bodyB = body.bounds;
        Debug.Log($"[kurophoto] body centre {bodyB.center} size {bodyB.size}");
        Debug.Log($"[kurophoto] rig  centre {rigB.center} size {rigB.size}");

        var fwd   = player.transform.forward;
        var right = player.transform.right;

        // Distances are in metres and deliberately tight: these are material-inspection shots,
        // not gameplay framing. Chibi rider is ~1.1 m tall so 1.4-2.6 m reads as a portrait.
        Shot(dir, $"kuro_photoreal_{tag}_head",
             bodyB.center + fwd * 1.30f + Vector3.up * 0.34f + right * 0.42f,
             bodyB.center + Vector3.up * 0.22f, 34f);

        Shot(dir, $"kuro_photoreal_{tag}_torso",
             bodyB.center - fwd * 1.55f + Vector3.up * 0.30f + right * 0.95f,
             bodyB.center + Vector3.up * 0.02f, 38f);

        Shot(dir, $"kuro_photoreal_{tag}_threequarter",
             rigB.center + fwd * 2.45f + Vector3.up * 0.85f + right * 1.75f,
             rigB.center + Vector3.up * 0.10f, 40f);

        Shot(dir, $"kuro_photoreal_{tag}_bike",
             rigB.center - fwd * 0.55f + Vector3.up * 0.28f + right * 2.10f,
             rigB.center - Vector3.up * 0.12f, 40f);

        Shot(dir, $"kuro_photoreal_{tag}_gameplay",
             rigB.center - fwd * 5.20f + Vector3.up * 1.95f,
             rigB.center + Vector3.up * 0.45f, 48f);

        Debug.Log("[kurophoto] done.");
        EditorApplication.Exit(0);
    }

    /// Proves (or disproves) that the player is actually lit per-pixel by the environment sun
    /// and casts/receives real-time shadows, rather than only picking up ambient/SH.
    static void ReportLighting(GameObject player)
    {
        var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        var sun = lights.FirstOrDefault(l => l.name == "Sakura Sunset Key");
        if (sun == null)
            Debug.LogWarning("[kurophoto] LIGHT: no 'Sakura Sunset Key' in scene!");
        else
            Debug.Log($"[kurophoto] LIGHT: sun type={sun.type} mode={sun.renderMode} " +
                      $"shadows={sun.shadows} intensity={sun.intensity} " +
                      $"colour={sun.color} enabled={sun.enabled} " +
                      $"cullingMask=0x{sun.cullingMask:X}");
        Debug.Log($"[kurophoto] LIGHT: ambient mode={RenderSettings.ambientMode} " +
                  $"sky={RenderSettings.ambientSkyColor} intensity={RenderSettings.ambientIntensity} " +
                  $"pixelLightCount={QualitySettings.pixelLightCount} " +
                  $"colorSpace={QualitySettings.activeColorSpace}");

        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            bool interesting = r is SkinnedMeshRenderer || r.name == "Frame" || r.name.Contains("Rim");
            if (!interesting) continue;
            int layerOk = sun == null ? -1 : ((sun.cullingMask & (1 << r.gameObject.layer)) != 0 ? 1 : 0);
            Debug.Log($"[kurophoto] REND '{r.name}' layer={LayerMask.LayerToName(r.gameObject.layer)} " +
                      $"litBySun={layerOk} cast={r.shadowCastingMode} receive={r.receiveShadows} " +
                      $"probes={r.lightProbeUsage} refl={r.reflectionProbeUsage} " +
                      $"mats=[{string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name))}]");
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                Debug.Log($"[kurophoto] MAT  '{m.name}' kw=[{string.Join(",", m.shaderKeywords)}] " +
                          $"metal={Get(m, "metallicFactor")} rough={Get(m, "roughnessFactor")} " +
                          $"normal={TexName(m, "normalTexture")} mr={TexName(m, "metallicRoughnessTexture")} " +
                          $"ao={TexName(m, "occlusionTexture")} base={TexName(m, "baseColorTexture")}");
            }
        }
    }

    static string Get(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p).ToString("0.###") : "-";
    static string TexName(Material m, string p)
    {
        if (!m.HasProperty(p)) return "-";
        var t = m.GetTexture(p);
        return t == null ? "NONE" : t.name;
    }

    static Bounds Encapsulate(Renderer[] rs)
    {
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~KuroCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        go.AddComponent<SakuraPostFX>();   // same grade the game ships with

        const int w = 1400, h = 1400;      // square: close-up material inspection
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
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
        Debug.Log($"[kurophoto] saved '{name}.png'");
    }

    static string ArgValue(string flag)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == flag) return a[i + 1];
        return null;
    }
}

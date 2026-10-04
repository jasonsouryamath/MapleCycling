using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reproduces the EXACT framing of the user's in-game complaint screenshot
/// (reference/improve/PROBLEM_kuro_brown_helmet_red_bike.png): the gameplay chase camera sitting
/// directly BEHIND the rider, so the camera only ever sees his shadow/backlit side.
///
/// This exists because the isolated studio turnarounds render the kit and helmet BLACK while the
/// in-scene chase shot renders them warm brown - a gap that only a render in the real lighting,
/// from the real camera pose, can confirm or refute. It also samples and logs the mean helmet /
/// frame colour so the fix can be judged numerically AS WELL AS by eye (the eye is the authority;
/// the numbers just stop a "looks about right" from passing).
/// </summary>
public static class AnimeKuroChaseShot
{
    private const string PocName = "Shiosai POC Realistic Rider";

    // The real chase rig (RideCameraSetup): offset (0, 1.60, -4.20) behind the rider, FOV 52.
    private static readonly Vector3 ChaseOffset = new Vector3(0f, 1.60f, -4.20f);
    private const float ChaseFov = 52f;

    [MenuItem("MapleRide/Shiosai/Capture Anime Kuro Chase Shot", priority = 46)]
    public static void Run()
    {
        string path = ShiosaiCoastEnvironment.ScenePath;
        if (EditorSceneManager.GetActiveScene().path != path)
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/shiosai_real_rider"));
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = null;
        if (regions != null)
        {
            regions.Resolve();
            previous = regions.currentRegionId;
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }

        try
        {
            Transform poc = null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == PocName) { poc = t; break; }
            if (poc == null) { Debug.LogError("[chase-shot] POC not staged."); return; }

            var rig = poc.GetComponent<KuroBikeRig>();
            if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }

            // Batchmode re-draws the FIRST solved pose unless this is forced per render. This has
            // hidden a broken render three times on this project.
            foreach (var s in poc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                s.forceMatrixRecalculationPerRender = true;
                s.updateWhenOffscreen = true;
            }

            Transform head = null;
            foreach (var t in poc.GetComponentsInChildren<Transform>(true))
                if (t.name == "Head") { head = t; break; }

            Vector3 f = poc.forward, up = Vector3.up, r = poc.right;
            Vector3 hips = poc.position;

            // Which way is the sun? Logged so "backlit" is a measured fact, not an assumption:
            // dot > 0 means the sun is shining from BEHIND the camera onto the rider's back.
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
                Debug.Log($"[chase-shot] sun fwd={sun.transform.forward} colour={sun.color} " +
                          $"intensity={sun.intensity} dot(sun,riderFwd)={Vector3.Dot(sun.transform.forward, f):F3}");

            Vector3 chasePos = hips + f * ChaseOffset.z + up * ChaseOffset.y + r * ChaseOffset.x;
            Vector3 chaseAim = hips + up * 1.05f;
            Shot(dir, "anime_kuro_chase_behind", chasePos, chaseAim, ChaseFov);

            // A tight crop on the helmet from directly behind - this is the pixel area the user
            // actually pointed at, and at chase FOV it is only ~100 px across.
            if (head != null)
            {
                Vector3 aim = head.position + f * 0.02f;
                Shot(dir, "anime_kuro_chase_helmet", aim - f * 0.85f + up * 0.10f, aim, 26f);
            }

            // Bike-only framing from behind, low: this is where the red part appeared.
            Shot(dir, "anime_kuro_chase_bike", hips - f * 2.2f + up * 0.55f, hips + up * 0.30f, 34f);

            Report(dir, "anime_kuro_chase_helmet");
            Report(dir, "anime_kuro_chase_bike");
            Debug.Log("[chase-shot] wrote 3 chase captures.");
        }
        finally
        {
            if (regions != null && previous != null)
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
    }

    /// Same behind-the-rider framing, but on the ACTUAL in-game player ("Kuro on Sakura Pass"),
    /// which is the rig the user's complaint screenshot was taken from. The Shiosai POC and the
    /// SakuraPass player are two different objects with two different material sets, so a fix
    /// verified on one proves nothing about the other - both have to be rendered.
    [MenuItem("MapleRide/Shiosai/Capture SakuraPass Player Behind", priority = 47)]
    public static void RunPlayer()
    {
        const string scene = "Assets/Scenes/SakuraPass.unity";
        if (EditorSceneManager.GetActiveScene().path != scene)
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/shiosai_real_rider"));
        Directory.CreateDirectory(dir);

        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[chase-shot] player not found."); return; }

        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            s.forceMatrixRecalculationPerRender = true;
            s.updateWhenOffscreen = true;
        }

        var sun = Object.FindFirstObjectByType<Light>();
        if (sun != null)
            Debug.Log($"[chase-shot] player sun fwd={sun.transform.forward} " +
                      $"dot(sun,riderFwd)={Vector3.Dot(sun.transform.forward, player.transform.forward):F3}");

        Vector3 f = player.transform.forward, up = Vector3.up;
        Vector3 hips = player.transform.position;
        Shot(dir, "player_kuro_behind", hips - f * 3.4f + up * 1.55f, hips + up * 1.05f, ChaseFov, RegionDirector.SakuraAmbience);

        Transform head = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
            if (t.name == "Head") { head = t; break; }
        if (head != null)
            Shot(dir, "player_kuro_helmet", head.position - f * 1.0f + up * 0.16f, head.position, 26f, RegionDirector.SakuraAmbience);

        Report(dir, "player_kuro_behind");
        Debug.Log("[chase-shot] wrote player captures.");
    }

    /// Logs the darkest-decile and mean colour of the frame so "is the black still black?" and
    /// "is anything still red?" are answerable from the log as well as from the PNG.
    private static void Report(string dir, string name)
    {
        string file = Path.Combine(dir, name + ".png");
        if (!File.Exists(file)) return;
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        tex.LoadImage(File.ReadAllBytes(file));
        var px = tex.GetPixels();
        int reddish = 0;
        float sr = 0f, sg = 0f, sb = 0f;
        foreach (var p in px)
        {
            sr += p.r; sg += p.g; sb += p.b;
            if (p.r > p.g + 0.12f && p.r > p.b + 0.12f && p.r > 0.25f) reddish++;
        }
        int n = px.Length;
        Debug.Log($"[chase-shot] {name}: mean=({sr / n:F3},{sg / n:F3},{sb / n:F3}) " +
                  $"redPixels={reddish} ({100f * reddish / n:F2}%)");
        Object.DestroyImmediate(tex);
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        Shot(dir, name, pos, look, fov, RegionDirector.ShiosaiAmbience);
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov,
                             RegionDirector.Ambience a)
    {
        var go = new GameObject("~ChaseShotCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 1100, h = 1100;
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
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[chase-shot] wrote {name}.png");
    }
}

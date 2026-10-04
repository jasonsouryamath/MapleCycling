using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Azora concept-art pass capture: shots for the six concept-sheet segments, written to
/// reference/good_graphics/azora_concept/. Nothing is saved.
///
/// EXTRA SHOTS: any worker can append lines to reference/good_graphics/azora_concept/extra_shots.txt
/// (no C# edit needed):  name d back side up lookAhead lookSide lookUp
///   d = route metres; eye = P(d) - T*back + S*side + up; look = P(d) + T*lookAhead + S*lookSide + up*lookUp
///   S = right-hand side of travel. '#' starts a comment.
/// Names containing '_sun' aim at the key light's sun instead: lookSide = azimuth offset (deg),
/// lookUp = degrees below the sun to aim.
/// Capture a subset by setting env var AZ_CONCEPT_ONLY to comma-separated name prefixes before
/// launching run_steps.ps1, e.g.  $env:AZ_CONCEPT_ONLY="s2_,x_river"  (unset = all shots).
/// </summary>
public static class AzoraConceptCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    // name, d, back, side, up, lookAhead, lookSide, lookUp
    static readonly (string, float, float, float, float, float, float, float)[] Shots =
    {
        // 1 Lakeview Approach (route 0-2000): lake + island town beside the road
        ("s1_lake_chase",    900f,  4f,   0f,  1.9f,  25f,   0f,  0.8f),
        ("s1_lake_view",     900f,  0f,   4f,  2.2f,  60f, -220f, -25f),
        ("s1_lake_view_r",   900f,  0f,  -4f,  2.2f,  60f,  220f, -25f),
        ("s1_lake_hero",    1100f, 90f,  40f, 45f,  200f, -80f, -20f),
        // 2 River Valley (route 3400-5900): turquoise river, stone viaduct, waterfall
        ("s2_river_chase",  4600f,  4f,   0f,  1.9f,  25f,   0f,  0.8f),
        ("s2_river_view",   4600f,  0f,   3f,  2.0f,  50f, -120f, -10f),
        ("s2_river_view_r", 4600f,  0f,  -3f,  2.0f,  50f,  120f, -10f),
        ("s2_river_hero",   4800f, 80f,  50f, 40f,  200f,   0f, -15f),
        // 3 Alpine Village (centred 2600 and 12600)
        ("s3_village_chase", 2560f, 4f,   0f,  1.9f,  25f,   0f,  0.8f),
        ("s3_village_hero",  2600f, 90f, -30f, 30f,  20f,   0f,  0f),
        ("s3_village2_chase",12560f, 4f,  0f,  1.9f,  25f,   0f,  0.8f),
        ("s3_village2_hero", 12600f, 90f, -30f, 30f, 20f,   0f,  0f),
        // 4 Cliffside Ridge (route 14400-18500): guardrail, chevrons, sea of clouds
        ("s4_ridge_chase",  16000f, 4f,   0f,  1.9f,  25f,   0f,  0.8f),
        ("s4_ridge_view",   16000f, 0f,   3f,  2.0f,  60f,  200f, -60f),
        ("s4_ridge_view_l", 16000f, 0f,  -3f,  2.0f,  60f, -200f, -60f),
        ("s4_ridge_hero",   16200f, 70f, 60f, 30f,  150f,   0f, -20f),
        // 5 Summit Plateau (route 18500-20400, col at 19680): lupins, summit tower
        ("s5_summit_chase", 19300f, 4f,   0f,  1.9f,  25f,   0f,  0.8f),
        ("s5_summit_view",  19550f, 0f,   3f,  1.8f,  40f,  60f,  -2f),
        ("s5_summit_view_l",19550f, 0f,  -3f,  1.8f,  40f, -60f,  -2f),
        ("s5_summit_hero",  19680f, 60f, 40f, 25f,  60f,   0f,  -5f),
        // 6 Summit Descent (route 20400-24000): valley lake at sunset, timber fence
        ("s6_descent_chase",22800f, 4f,   0f,  1.9f,  25f,   0f,  0.8f),
        ("s6_descent_view", 21200f, 0f,   3f,  2.0f,  120f, 0f, -60f),
        ("s6_descent_hero", 22000f, 60f, 40f, 40f,  300f,   0f, -80f),
    };

    [MenuItem("MapleRide/QA/Capture Azora Concept Pass")]
    public static void Capture()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/azora_concept"));
        Directory.CreateDirectory(outDir);
        string[] only = null;
        string onlyEnv = System.Environment.GetEnvironmentVariable("AZ_CONCEPT_ONLY");
        if (!string.IsNullOrWhiteSpace(onlyEnv))
        {
            only = onlyEnv.Split(',');
            for (int i = 0; i < only.Length; i++) only[i] = only[i].Trim();
        }
        bool Want(string n)
        {
            if (only == null) return true;
            foreach (var p in only) if (p.Length > 0 && !p.StartsWith("#") && n.StartsWith(p)) return true;
            return false;
        }

        int n = 0;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            var graph = RouteGraph.Load();
            if (regions == null || graph == null) { Debug.LogError("[azora-concept] no RegionDirector / route graph"); return; }
            regions.Resolve();
            foreach (var region in RegionCatalog.Regions)
            {
                if (!region.Id.ToLowerInvariant().Contains("azora")) continue;
                var course = graph.BuildCourse(region.BuiltCourseId);
                if (course == null) { Debug.LogWarning($"[azora-concept] {region.Id}: no course"); continue; }
                regions.currentRegionId = region.Id;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
                // AZ_HIDE = comma-separated child names under the Azora root to deactivate (diagnostics).
                var hideEnv = System.Environment.GetEnvironmentVariable("AZ_HIDE");
                if (!string.IsNullOrWhiteSpace(hideEnv))
                {
                    var azRoot = GameObject.Find(AzoraHighlandsEnvironment.RootName);
                    foreach (var h in hideEnv.Split(','))
                    {
                        var tr = azRoot != null ? azRoot.transform.Find(h.Trim()) : null;
                        if (tr != null) { tr.gameObject.SetActive(false); Debug.Log($"[azora-concept] hidden {h.Trim()}"); }
                    }
                }

                // Winter: the same snowfall the weather system shows in play, pre-warmed per shot.
                // AZ_SNOWFALL = 0..1 intensity (default 0.35; 0 = no falling snow).
                float fall = 0.35f;
                var fallEnv = System.Environment.GetEnvironmentVariable("AZ_SNOWFALL");
                if (!string.IsNullOrEmpty(fallEnv)) float.TryParse(fallEnv, NumberStyles.Float, CultureInfo.InvariantCulture, out fall);
                var snowHost = new GameObject("~AzoraConceptSnow");
                var snow = fall > 0f ? WeatherEffects.CreateSnowfall(snowHost.transform, 12000) : null;

                void Take(string name, float d, float back, float side, float up, float ahead, float lside, float lup)
                {
                    if (!Want(name) || d < 0f || d > course.Length) return;
                    var p = course.PositionAt(d);
                    var t = course.TangentAt(d); t.y = 0f; t.Normalize();
                    var s = new Vector3(t.z, 0f, -t.x);
                    var eye = p - t * back + s * side + Vector3.up * up;
                    var look = p + t * ahead + s * lside + Vector3.up * lup;
                    // "_sun" shots aim AT the key light's sun: lookSide = azimuth offset (deg),
                    // lookUp = how far below the sun to aim (deg), so the disc sits high in frame.
                    if (name.Contains("_sun"))
                    {
                        Light key = null;
                        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                            if (l.type == LightType.Directional && (key == null || l.name == "Sakura Sunset Key")) key = l;
                        if (key != null)
                        {
                            var sd = -key.transform.forward;
                            float el = Mathf.Asin(Mathf.Clamp(sd.y, -1f, 1f)) * Mathf.Rad2Deg - lup;
                            float az = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg + lside;
                            var dir = Quaternion.Euler(-el, az, 0f) * Vector3.forward;
                            look = eye + dir * 100f;
                        }
                    }
                    if (snow != null)
                    {
                        var probe = new GameObject("~probe").transform;
                        probe.position = eye; probe.LookAt(look);
                        WeatherEffects.PlaceSnowfall(snow, probe, new Vector2(1.6f, -1.1f));
                        Object.DestroyImmediate(probe.gameObject);
                        var em = snow.emission; em.rateOverTime = WeatherEffects.SnowRate(fall, 12000);
                        snow.Simulate(WeatherEffects.FlakeLifetime, true, true, true);
                    }
                    Shot(Path.Combine(outDir, name + ".png"), eye, look);
                    n++;
                }

                foreach (var (name, d, back, side, up, ahead, lside, lup) in Shots)
                    Take(name, d, back, side, up, ahead, lside, lup);

                string extra = Path.Combine(outDir, "extra_shots.txt");
                if (File.Exists(extra))
                {
                    foreach (var raw in File.ReadAllLines(extra))
                    {
                        var line = raw.Split('#')[0].Trim();
                        if (line.Length == 0) continue;
                        var f = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                        if (f.Length < 8) { Debug.LogWarning($"[azora-concept] bad extra shot line: {raw}"); continue; }
                        float F(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);
                        Take("x_" + f[0], F(1), F(2), F(3), F(4), F(5), F(6), F(7));
                    }
                }
                Object.DestroyImmediate(snowHost);
                Debug.Log($"[azora-concept] {region.Id}: {n} frames -> {outDir} ({course.Length:0} m course)");
            }
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }

    static void Shot(string file, Vector3 pos, Vector3 look, int w = 1280, int h = 720)
    {
        var go = new GameObject("~AzoraConceptCamera");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 9000f;
        cam.allowHDR = true;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGBA32, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(file, img.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }
}

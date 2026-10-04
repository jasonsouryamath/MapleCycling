using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification captures for the transparent-HUD pass and the greeting face card.
///
/// Renders the rider's eye view with BOTH overlay canvases composited in - the ride HUD and
/// the greeting card - at the two ends of this world's brightness range:
///
///   bright blossom (the village stretch, blown-out pink) - where a translucent pane is at the
///                   most risk of losing its text, and
///   the cliff tunnel (near-black bore)                   - where a translucent pane is at the
///                   most risk of vanishing entirely.
///
/// Both are the documented failure cases for HUD readability on this project, and both must be
/// LOOKED AT. A capture is the only valid test here; there is no metric for "the road ahead is
/// clearly visible through the panel".
///
/// The canvases are driven in SCREEN SPACE by the capture camera at the same 1920x1080 their
/// CanvasScaler references, so UI pixels land on target pixels - the same reason RideValidation
/// does it, after a world-space version turned every 12 px caption into grey mush.
/// </summary>
public static class HudReadabilityCapture
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const float Dt = 1f / 30f;

    private static string OutDir =>
        MapleRidePaths.RenderDir("hud_glass");

    [MenuItem("MapleRide/Ride/Capture HUD Readability", priority = 45)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutDir);

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[hudglass] no RideBootstrap in the scene."); return; }
        boot.Resolve();

        var session = boot.session;
        var director = boot.director;
        var devices = boot.devices;
        var hud = boot.hud;

        bool prevKeyboardEffort = devices.acceptKeyboardEffort;
        devices.acceptKeyboardEffort = false;
        devices.EffortInput = 0f;
        session.Graph = null;
        session.EnsureCourse();

        var streamer = Object.FindFirstObjectByType<RouteDressingStreamer>();
        if (streamer != null) streamer.ShowAll();

        // A real rider off the scene roster drives the card, so the capture proves the actual
        // wiring (their name, their baked portrait, one of their own lines) and not a mock-up.
        var greeter = PickGreeter();
        var card = NpcGreetingCard.Instance;
        card.EnsureBuilt();

        // Fractions of sakura_circuit, matching RideValidation's existing shot list so these
        // frames can be compared directly against the pre-change GPS captures.
        var shots = new (float at, string name)[]
        {
            (0.779f, "hud_glass_blossom"),
            (0.097f, "hud_glass_tunnel"),
        };

        foreach (var s in shots)
        {
            session.autoLapsFromTarget = true;
            session.SelectCourse("sakura_circuit");
            session.SetTargetDuration(30f);
            session.ResetRide();
            if (hud.Canvas == null) hud.Build();
            hud.map.Invalidate();

            for (int i = 0; i < 120; i++) { session.Tick(Dt); director.Tick(Dt); }
            session.SeekTo(s.at * session.Course.Length);
            for (int i = 0; i < 30; i++)
            {
                devices.Tick(Dt, session.Course.GradeAt(session.DistanceM, session.gradeWindowM));
                director.Tick(Dt);
            }
            session.SeekTo(s.at * session.Course.Length);
            director.Tick(Dt);

            if (boot.follower != null) { boot.follower.enabled = true; boot.follower.Apply(); }
            if (streamer != null) streamer.ShowAll();

            hud.Refresh();
            hud.map.Refresh(Dt);
            Canvas.ForceUpdateCanvases();
            hud.map.Refresh(Dt);

            // Hold the card fully open for the shot. Tick() is driven explicitly because
            // Time.time does not advance during a batchmode -executeMethod call.
            if (greeter != null)
            {
                greeter.ResolveIdentity();
                card.Show(greeter, greeter.riderName, GreetingLine(greeter),
                          greeter.portrait, 10f, 0f);
                card.Tick(NpcGreetingCard.FadeSeconds + 0.1f);
                Debug.Log($"[hudglass] card: '{greeter.riderName}' portrait=" +
                          (greeter.portrait != null ? greeter.portrait.name : "SILHOUETTE"));
            }
            Canvas.ForceUpdateCanvases();

            Render(boot, card, Path.Combine(OutDir, s.name + ".png"));
            if (s.name == "hud_glass_blossom")
            {
                // Greeting collision regression: same live HUD/card at 16:10 and 21:9.
                Render(boot, card, Path.Combine(OutDir, "hud_glass_blossom_16x10.png"), 1920, 1200);
                Render(boot, card, Path.Combine(OutDir, "hud_glass_blossom_21x9.png"), 2520, 1080);
                if (hud.worldMap != null)
                {
                    hud.worldMap.SetOpen(true);
                    Render(boot, card, Path.Combine(OutDir, "hud_glass_worldmap_greeting_hidden.png"));
                    hud.worldMap.SetOpen(false);
                }
            }
            Debug.Log($"[hudglass] {s.name}: {session.DistanceM:0} m of " +
                      $"{session.Course.Length:0} m, PanelAlpha {HudKit.PanelAlpha:0.00}");
        }

        // Leave the scene as we found it. The card host is a RUNTIME object - it is created on
        // the first greeting - so the capture's copy is torn down rather than serialized into
        // the scene, where it would be a stale duplicate the next staging pass has to prune.
        if (card != null) Object.DestroyImmediate(card.gameObject);
        devices.acceptKeyboardEffort = prevKeyboardEffort;
        devices.ResetEffort();
        session.autoLapsFromTarget = true;
        session.SelectCourse("sakura_circuit");
        session.ResetRide();
        if (boot.follower != null) boot.follower.Apply();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[hudglass] captures written to {OutDir}");
    }

    private static NpcGreeting PickGreeter()
    {
        var all = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include,
                                                        FindObjectsSortMode.None);
        if (all.Length == 0)
        {
            Debug.LogWarning("[hudglass] no NpcGreeting riders in the scene - the card will " +
                             "not be exercised.");
            return null;
        }
        // Prefer a rider who is actually ON this route and has a baked face: an inactive rider
        // from another region would put an empty chip on the card and make the capture prove
        // nothing.
        foreach (var g in all)
            if (g.gameObject.activeInHierarchy && g.portrait != null) return g;
        foreach (var g in all)
            if (g.portrait != null) return g;
        return all[0];
    }

    private static string GreetingLine(NpcGreeting g) =>
        (g.ownPhrases != null && g.ownPhrases.Length > 0) ? g.ownPhrases[0]
                                                          : "Morning! Perfect day for the pass.";

    private static void Render(RideBootstrap boot, NpcGreetingCard card, string path,
                               int W = 1920, int H = 1080)
    {
        var session = boot.session;
        var course = session.Course;

        float d = session.DistanceM;
        var p = course.PositionAt(d);
        var t = course.TangentAt(d);
        var s = course.SideAt(d);

        var go = new GameObject("~HudGlassCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = p + Vector3.up * 3.1f - t * 7.2f - s * 1.1f;
        cam.transform.LookAt(p + t * 9f + Vector3.up * 1.3f);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        cam.cullingMask = ~(1 << HudSprites.UiLayer);
        go.AddComponent<SakuraPostFX>();
        SakuraPassEnvironment.TunePostFX(go.GetComponent<SakuraPostFX>());

        var hudGo = new GameObject("~HudGlassUiCam");
        hudGo.transform.SetParent(go.transform, false);
        var hudCam = hudGo.AddComponent<Camera>();
        hudCam.clearFlags = CameraClearFlags.Depth;
        hudCam.cullingMask = 1 << HudSprites.UiLayer;
        hudCam.fieldOfView = cam.fieldOfView;
        hudCam.nearClipPlane = 0.05f;
        hudCam.farClipPlane = 100f;
        hudCam.depth = cam.depth + 10f;
        hudCam.allowHDR = false;

        var tex = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = tex;
        hudCam.targetTexture = tex;

        var canvases = new[] { boot.hud.Canvas, card != null ? card.Canvas : null };
        var prevMode = new RenderMode[canvases.Length];
        var prevCam = new Camera[canvases.Length];
        var prevPlane = new float[canvases.Length];

        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null) continue;
            prevMode[i] = c.renderMode;
            prevCam[i] = c.worldCamera;
            prevPlane[i] = c.planeDistance;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = hudCam;
            // The greeting card sits one hair nearer than the HUD so it composites on top
            // without depending on canvas sorting order across two separate canvases.
            c.planeDistance = i == 0 ? 1f : 0.98f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
        Canvas.ForceUpdateCanvases();
        boot.hud.map.Refresh(Dt);
        Canvas.ForceUpdateCanvases();
        if (card != null) card.Tick(NpcGreetingCard.FadeSeconds + 0.1f);
        Canvas.ForceUpdateCanvases();

        cam.Render();
        hudCam.Render();

        var prevActive = RenderTexture.active;
        RenderTexture.active = tex;
        var img = new Texture2D(W, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prevActive;

        cam.targetTexture = null;
        hudCam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);

        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null) continue;
            c.renderMode = prevMode[i];
            c.worldCamera = prevCam[i];
            c.planeDistance = prevPlane[i];
        }
    }
}

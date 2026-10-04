using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Verification captures for the World Map / fast-travel milestone.
///
/// Renders, with the REAL widgets and the REAL ride systems (never a mock-up):
///   * the World Map overlay open over the ride HUD,
///   * the rider actually on Shiosai Coast at several points along the route,
///   * a wide view of the coast so road, ocean, horizon and props can be judged together,
///   * Sakura Pass again after fast travelling back, to prove there is no regression.
///
/// Headless:
///   Unity.exe -projectPath ... -batchmode -quit -executeMethod WorldMapValidation.Capture
/// (no -nographics: renders come out empty without a graphics device).
/// </summary>
public static class WorldMapValidation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const float Dt = 1f / 30f;

    private static string OutDir =>
        MapleRidePaths.RenderDir("worldmap");

    [MenuItem("MapleRide/Ride/Capture World Map + Shiosai Coast", priority = 43)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutDir);

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[worldmap] no RideBootstrap in the scene."); return; }
        boot.Resolve();

        var regions = boot.regions;
        if (regions == null)
        {
            Debug.LogError("[worldmap] no RegionDirector - run ShiosaiCoastEnvironment.Apply.");
            return;
        }
        regions.Resolve();

        var hud = boot.hud;
        var session = boot.session;
        session.Graph = null;
        session.EnsureCourse();
        hud.Build();
        hud.worldMap.regions = regions;

        var streamer = Object.FindFirstObjectByType<RouteDressingStreamer>();

        // ---------------------------------------------------------------- 1. the overlay
        regions.FastTravel(RegionCatalog.SakuraPass);
        Warm(boot, 0.12f);
        hud.worldMap.SetOpen(true);
        hud.Refresh();
        Canvas.ForceUpdateCanvases();
        RenderRiderView(boot, Path.Combine(OutDir, "worldmap_overlay_sakura.png"));
        Debug.Log("[worldmap] captured overlay over Sakura Pass, open=" + hud.worldMap.isOpen);
        hud.worldMap.SetOpen(false);

        // ---------------------------------------------------------------- 2. fast travel
        bool travelled = regions.FastTravel(RegionCatalog.ShiosaiCoast);
        Debug.Log($"[worldmap] fast travel to Shiosai Coast -> {travelled}, " +
                  $"course '{session.Course?.DisplayName}', region '{regions.currentRegionId}'.");
        if (!travelled) { Debug.LogError("[worldmap] fast travel FAILED."); return; }
        if (streamer != null) streamer.ShowAll();

        foreach (var (f, name) in new (float, string)[]
                 {
                     (0.03f, "shiosai_start"),
                     (0.18f, "shiosai_beach"),
                     (0.40f, "shiosai_bend"),
                     (0.62f, "shiosai_bluff"),
                     (0.82f, "shiosai_lighthouse"),
                 })
        {
            Warm(boot, f);
            if (streamer != null) streamer.ShowAll();
            hud.Refresh();
            Canvas.ForceUpdateCanvases();
            RenderRiderView(boot, Path.Combine(OutDir, name + ".png"));
            Debug.Log($"[worldmap] {name}: {session.DistanceM:0} m of {session.Course.Length:0} m, " +
                      $"grade {session.DisplayGradePct:0.0} %, {session.SpeedKph:0.0} km/h, " +
                      $"rider {session.WorldPosition}");
        }

        // The overlay again, this time from the coast, so the "you are here" pin has moved.
        Warm(boot, 0.30f);
        hud.worldMap.SetOpen(true);
        hud.Refresh();
        Canvas.ForceUpdateCanvases();
        RenderRiderView(boot, Path.Combine(OutDir, "worldmap_overlay_shiosai.png"));
        hud.worldMap.SetOpen(false);

        // Wide shots: the mock has to be judged as a place, not only from 3 m off the deck.
        WideShot(session, 0.30f, 130f, 260f, "shiosai_wide_coast");
        WideShot(session, 0.82f, 90f, 190f, "shiosai_wide_headland");
        LandmarkShot("Shiosai Light", "shiosai_lighthouse_prop");

        // ---------------------------------------------------------------- 3. back home
        bool back = regions.FastTravel(RegionCatalog.SakuraPass);
        Debug.Log($"[worldmap] fast travel back -> {back}, course '{session.Course?.DisplayName}', " +
                  $"region '{regions.currentRegionId}'.");
        if (streamer != null) streamer.ShowAll();
        Warm(boot, 0.10f);
        hud.Refresh();
        Canvas.ForceUpdateCanvases();
        RenderRiderView(boot, Path.Combine(OutDir, "sakura_after_return.png"));

        // Leave the scene exactly as a player would find it: Sakura Pass, lap 1, map closed.
        session.autoLapsFromTarget = true;
        session.SelectCourse("sakura_circuit");
        session.ResetRide();
        regions.SyncFromSession();
        if (boot.follower != null) boot.follower.Apply();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[worldmap] captures written to {OutDir}");
    }

    /// <summary>Warms the telemetry + physics into a steady state, then seeks to a fraction.</summary>
    private static void Warm(RideBootstrap boot, float fraction)
    {
        var session = boot.session;
        // Borrow the scripted autonomous band: a capture harness cannot mash the pedal key, and
        // the mash model produces no watts without strokes (see DeviceManager.MashDrivesEffort).
        boot.devices.acceptKeyboardEffort = false;
        boot.devices.EffortInput = 0f;
        for (int i = 0; i < 90; i++) { session.Tick(Dt); boot.director?.Tick(Dt); }
        session.SeekTo(fraction * session.Course.Length);
        for (int i = 0; i < 20; i++)
        {
            boot.devices.Tick(Dt, session.Course.GradeAt(session.DistanceM, session.gradeWindowM));
            boot.director?.Tick(Dt);
        }
        session.SeekTo(fraction * session.Course.Length);
        boot.director?.Tick(Dt);
        // Hand the pedals back to the player. This harness saves the scene, and a serialized
        // acceptKeyboardEffort = false would ship a rider who ignores the mash key entirely.
        boot.devices.acceptKeyboardEffort = true;
        boot.devices.ResetEffort();
        if (boot.follower != null) { boot.follower.enabled = true; boot.follower.Apply(); }
        if (boot.regions != null && boot.regions.streamer != null && boot.rider != null)
            boot.regions.streamer.Apply(boot.rider.position);
    }

    /// <summary>Frames a named landmark object so the render proves it exists and is grounded.</summary>
    private static void LandmarkShot(string objectName, string fileName)
    {
        GameObject found = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None))
            if (t.name == objectName) { found = t.gameObject; break; }
        if (found == null)
        {
            Debug.LogWarning($"[worldmap] landmark '{objectName}' not found - no shot.");
            return;
        }
        var b = new Bounds(found.transform.position, Vector3.one);
        foreach (var r in found.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        float span = Mathf.Max(b.size.magnitude, 6f);
        var eye = b.center + new Vector3(0.55f, 0.42f, 0.72f).normalized * span * 1.9f;
        Shot(eye, b.center, 45f, Path.Combine(OutDir, fileName + ".png"));
        Debug.Log($"[worldmap] {fileName}: '{objectName}' bounds centre {b.center} size {b.size}");
    }

    private static void WideShot(RideSession session, float fraction, float up, float back,
                                 string name)
    {
        var course = session.Course;
        float d = fraction * course.Length;
        var p = course.PositionAt(d);
        var t = course.TangentAt(d);
        var s = course.SideAt(d);
        // Stand inland and above, looking out over the road at the sea.
        var eye = p + Vector3.up * up + s * back * 0.75f - t * back * 0.5f;
        Shot(eye, p + t * 60f, 55f, Path.Combine(OutDir, name + ".png"));
        Debug.Log($"[worldmap] {name}: eye {eye} -> {p}");
    }

    private static void Shot(Vector3 pos, Vector3 look, float fov, string path)
    {
        const int W = 1600, H = 900;
        var go = new GameObject("~WorldMapCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 12000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        SakuraPassEnvironment.TunePostFX(go.AddComponent<SakuraPostFX>());

        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(W, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }

    /// <summary>
    /// Rider's eye view with the real HUD canvas composited in - same technique as
    /// <c>RideValidation.RenderRiderView</c>: the canvas is temporarily world-space and sized to
    /// exactly fill the capture frustum, so an edit-mode render contains the real widgets.
    /// </summary>
    private static void RenderRiderView(RideBootstrap boot, string path)
    {
        const int W = 1920, H = 1080;
        var session = boot.session;
        var course = session.Course;

        float d = session.DistanceM;
        var p = course.PositionAt(d);
        var t = course.TangentAt(d);
        var s = course.SideAt(d);

        var go = new GameObject("~WorldMapRideCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = p + Vector3.up * 3.1f - t * 7.2f - s * 1.1f;
        cam.transform.LookAt(p + t * 9f + Vector3.up * 1.3f);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 12000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        SakuraPassEnvironment.TunePostFX(go.AddComponent<SakuraPostFX>());

        var canvas = boot.hud.Canvas;
        var rt = (RectTransform)canvas.transform;
        var prevMode = canvas.renderMode;
        var prevCam = canvas.worldCamera;
        var prevPos = rt.position;
        var prevRot = rt.rotation;
        var prevScale = rt.localScale;
        var prevSize = rt.sizeDelta;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        rt.sizeDelta = new Vector2(W, H);
        const float dist = 1.0f;
        float worldH = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        rt.localScale = Vector3.one * (worldH / H);
        rt.position = cam.transform.position + cam.transform.forward * dist;
        rt.rotation = cam.transform.rotation;
        Canvas.ForceUpdateCanvases();
        boot.hud.map.Refresh(Dt);
        Canvas.ForceUpdateCanvases();

        var tex = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = tex;
        cam.Render();

        var prevActive = RenderTexture.active;
        RenderTexture.active = tex;
        var img = new Texture2D(W, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prevActive;

        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);

        canvas.renderMode = prevMode;
        canvas.worldCamera = prevCam;
        rt.position = prevPos;
        rt.rotation = prevRot;
        rt.localScale = prevScale;
        rt.sizeDelta = prevSize;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ride-foundation validation and GPS capture.
///
/// Two jobs, both headless-runnable, because "it compiles" and "the log says OK" have both
/// lied on this project before:
///
///   <c>RideValidation.Run</c>     - drives a real, deterministic ride over every course with the
///                                   real physics, the real telemetry source and the real
///                                   checkpoint director, and reports the numbers that decide
///                                   whether the 30/60 minute targets are actually met.
///   <c>RideValidation.CaptureGps</c> - builds the REAL HUD widgets, drives the session to a set
///                                   of arc positions and renders the rider's eye view with the
///                                   GPS window composited in, so the map can be LOOKED AT.
/// </summary>
public static class RideValidation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const float Dt = 1f / 30f;             // fixed step: reproducible, not frame-rate bound

    private static string OutDir =>
        MapleRidePaths.RenderDir("ride");

    // ==================================================================== self-test

    [MenuItem("MapleRide/Ride/Run Ride Self-Test", priority = 41)]
    public static void Run()
    {
        OpenScene();
        var graph = RouteGraph.Load();
        if (graph == null) graph = RouteGraphBaker.BakeAsset();
        if (graph == null) { Debug.LogError("[ridetest] FAIL no route graph."); return; }

        int failures = 0;
        var log = new StringBuilder();
        log.AppendLine();
        log.AppendLine("================ MapleRide ride self-test ================");
        log.AppendLine($"segments {graph.segments.Length}  courses {graph.courses.Length}  " +
                       $"checkpoints {graph.checkpoints.Length}");

        foreach (var seg in graph.segments)
            log.AppendLine($"  segment {seg.id,-8} {seg.Count,5} samples  {seg.Length,8:0.0} m");

        // A host that is NOT the scene's ride object, so the test can never leave a mutated
        // session serialized into the scene.
        var host = new GameObject("~RideSelfTest");
        var devices = host.AddComponent<DeviceManager>();
        var session = host.AddComponent<RideSession>();
        var director = host.AddComponent<RouteDirector>();
        devices.ftpWatts = 220f;
        // This test drives an AUTONOMOUS rider to measure course times, and it cannot press
        // keys. Turning the keyboard path off is what hands the effort to the scripted
        // endurance band instead of the player's mash (see DeviceManager.MashDrivesEffort) -
        // without it every course would sit on the start line at 0 W and "fail" to finish.
        devices.acceptKeyboardEffort = false;
        devices.EffortInput = 0f;
        session.devices = devices;
        director.session = session;

        log.AppendLine();
        log.AppendLine($"{"course",-20}{"km",8}{"gain",7}{"cps",5}{"laps",6}{"lap min",9}" +
                       $"{"ride min",10}{"avg kph",9}{"max %",8}{"min %",8}  closed");

        foreach (var cd in graph.courses)
        {
            session.autoLapsFromTarget = true;
            session.SelectCourse(cd.id);
            var course = session.Course;
            if (course == null || course.Count < 2)
            {
                log.AppendLine($"  {cd.id}: FAIL - course did not build."); failures++; continue;
            }

            // --- geometry checks ---------------------------------------------------
            if (cd.closed)
            {
                float gap = Vector3.Distance(course.Position[0], course.Position[course.Count - 1]);
                if (gap > 6f)
                {
                    log.AppendLine($"  {cd.id}: FAIL - closed course has a {gap:0.0} m gap.");
                    failures++;
                }
            }

            float maxGrade = 0f, minGrade = 0f;
            for (float d = 0; d < course.Length; d += 4f)
            {
                float g = course.GradeAt(d, 8f) * 100f;
                maxGrade = Mathf.Max(maxGrade, g);
                minGrade = Mathf.Min(minGrade, g);
            }
            if (maxGrade > 14f)
            {
                log.AppendLine($"  {cd.id}: FAIL - {maxGrade:0.0} % climb wall is not rideable.");
                failures++;
            }

            // --- drive the ride ----------------------------------------------------
            session.ResetRide();
            float lapMinutes = session.EstimateLapMinutes();
            int guard = 0;
            int maxSteps = Mathf.CeilToInt(60f * 60f * 4f / Dt);     // 4 hours of wall clock
            while (!session.Finished && guard++ < maxSteps)
            {
                session.Tick(Dt);
                director.Tick(Dt);
            }

            float rideMinutes = session.ElapsedSeconds / 60f;
            float avgKph = session.ElapsedSeconds <= 0.01f ? 0f
                : session.TotalDistanceM / session.ElapsedSeconds * 3.6f;

            log.AppendLine($"{course.DisplayName,-20}{course.Length / 1000f,8:0.00}" +
                           $"{course.Ascent,7:0}{course.Checkpoints.Length,5}" +
                           $"{session.TotalLaps,6}{lapMinutes,9:0.0}{rideMinutes,10:0.0}" +
                           $"{avgKph,9:0.0}{maxGrade,8:0.0}{minGrade,8:0.0}  {cd.closed}");

            if (!session.Finished)
            {
                log.AppendLine($"  {cd.id}: FAIL - ride never finished (stalled at " +
                               $"{session.TotalDistanceM:0} m of {session.PlannedDistanceM:0} m).");
                failures++;
            }
            if (director.CheckpointsReached == 0 && course.Checkpoints.Length > 0)
            {
                log.AppendLine($"  {cd.id}: FAIL - no checkpoint fired over a whole ride.");
                failures++;
            }
        }

        // --- the headline requirement: a 30 and a 60 minute ride ---------------------
        log.AppendLine();
        foreach (var target in new[] { 30f, 60f })
        {
            foreach (var id in new[] { "sakura_circuit", "aozora_loop", "gran_fondo" })
            {
                session.autoLapsFromTarget = true;
                session.SelectCourse(id);
                session.SetTargetDuration(target);
                session.ResetRide();
                int guard = 0;
                while (!session.Finished && guard++ < Mathf.CeilToInt(60f * 60f * 4f / Dt))
                    session.Tick(Dt);
                float mins = session.ElapsedSeconds / 60f;
                float err = Mathf.Abs(mins - target) / target;
                string verdict = err <= 0.25f ? "ok" : "OUT OF BAND";
                if (err > 0.25f && id == "sakura_circuit") failures++;
                log.AppendLine($"  target {target,3:0} min  {id,-16} -> {session.TotalLaps} lap(s), " +
                               $"{session.TotalDistanceM / 1000f,6:0.00} km, {mins,5:0.0} min  [{verdict}]");
            }
        }

        // --- route follower puts the rider on the road ------------------------------
        session.SelectCourse("sakura_circuit");
        session.ResetRide();
        var dummy = new GameObject("~RideSelfTestRider");
        var follower = dummy.AddComponent<RouteFollower>();
        follower.session = session;
        follower.rider = dummy.transform;
        float worstOff = 0f;
        for (float d = 0; d < session.Course.Length; d += 17f)
        {
            session.SeekTo(d);
            follower.Apply();
            var onRoute = session.Course.PositionAt(d);
            float lateral = Vector3.Distance(
                new Vector3(dummy.transform.position.x, 0f, dummy.transform.position.z),
                new Vector3(onRoute.x, 0f, onRoute.z));
            worstOff = Mathf.Max(worstOff, lateral);
        }
        log.AppendLine();
        log.AppendLine($"  route follower: worst lateral offset from the centreline " +
                       $"{worstOff:0.00} m (carriageway half-width {graph.roadHalfWidth:0.0} m)");
        if (worstOff > graph.roadHalfWidth)
        {
            log.AppendLine("  FAIL - the rider is off the carriageway.");
            failures++;
        }

        UnityEngine.Object.DestroyImmediate(dummy);
        UnityEngine.Object.DestroyImmediate(host);

        log.AppendLine();
        log.AppendLine(failures == 0
            ? "[ridetest] PASS - all ride checks green."
            : $"[ridetest] FAIL - {failures} check(s) failed.");
        log.AppendLine("==========================================================");
        Debug.Log(log.ToString());
    }

    // ==================================================================== gps capture

    [MenuItem("MapleRide/Ride/Capture GPS Views", priority = 42)]
    public static void CaptureGps()
    {
        OpenScene();
        Directory.CreateDirectory(OutDir);

        var graph = RouteGraph.Load() ?? RouteGraphBaker.BakeAsset();
        if (graph == null) { Debug.LogError("[gps] no route graph."); return; }

        var boot = UnityEngine.Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[gps] no RideBootstrap in the scene."); return; }
        boot.Resolve();

        var session = boot.session;
        var director = boot.director;
        var devices = boot.devices;
        var hud = boot.hud;
        // Same reason as the self-test: this harness cannot mash, so it borrows the scripted
        // autonomous band to warm the telemetry into a plausible steady state for the shot.
        // Restored before the scene is saved at the end.
        bool prevKeyboardEffort = devices.acceptKeyboardEffort;
        devices.acceptKeyboardEffort = false;
        devices.EffortInput = 0f;
        session.Graph = null;
        session.EnsureCourse();

        // Dressing streaming off for captures: these are wide, static frames and a streamed
        // world renders as an empty one.
        var streamer = UnityEngine.Object.FindFirstObjectByType<RouteDressingStreamer>();
        if (streamer != null) streamer.ShowAll();

        // Fractions are of the whole course, chosen so the frame lands on the landmark the shot
        // is named after (Sakura Circuit = pass 0-1377 m then s1 1377-3279 m).
        var shots = new (string course, float laps, float atFraction, string name)[]
        {
            ("sakura_circuit", 30f, 0.097f, "gps_circuit_tunnel"),
            ("sakura_circuit", 30f, 0.161f, "gps_circuit_summit"),
            ("sakura_circuit", 30f, 0.240f, "gps_circuit_hairpins"),
            ("sakura_circuit", 30f, 0.629f, "gps_circuit_causeway"),
            ("sakura_circuit", 30f, 0.779f, "gps_circuit_village"),
            ("sakura_circuit", 30f, 0.919f, "gps_circuit_kawabe"),
            ("aozora_loop", 30f, 0.150f, "gps_aozora_switchbacks"),
            ("aozora_loop", 30f, 0.315f, "gps_aozora_shrine"),
            ("gran_fondo", 60f, 0.020f, "gps_fondo_maple_city"),
        };

        foreach (var s in shots)
        {
            session.autoLapsFromTarget = true;
            session.SelectCourse(s.course);
            session.SetTargetDuration(s.laps);
            session.ResetRide();
            if (hud.Canvas == null) hud.Build();
            hud.map.Invalidate();

            // Warm the telemetry simulator and the physics into a plausible steady state, then
            // fast-forward on arc length so the shot lands exactly where it was asked for.
            for (int i = 0; i < 120; i++) { session.Tick(Dt); director.Tick(Dt); }
            SetDistance(session, s.atFraction * session.Course.Length);
            for (int i = 0; i < 30; i++)
            {
                devices.Tick(Dt, session.Course.GradeAt(session.DistanceM, session.gradeWindowM));
                director.Tick(Dt);
            }
            // Keep the fast-forward from being undone by the warm-up ticks above.
            SetDistance(session, s.atFraction * session.Course.Length);
            director.Tick(Dt);

            if (boot.follower != null) { boot.follower.enabled = true; boot.follower.Apply(); }
            if (streamer != null) streamer.ShowAll();

            hud.Refresh();
            hud.map.Refresh(Dt);
            Canvas.ForceUpdateCanvases();
            hud.map.Refresh(Dt);
            Canvas.ForceUpdateCanvases();

            RenderRiderView(boot, Path.Combine(OutDir, s.name + ".png"));
            Debug.Log($"[gps] {s.name}: {session.Course.DisplayName} @ " +
                      $"{session.DistanceM:0} m of {session.Course.Length:0} m, " +
                      $"lap {session.LapIndex + 1}/{session.TotalLaps}, " +
                      $"grade {session.DisplayGradePct:0.0} %, " +
                      $"next '{(director.HasNext ? director.NextCheckpoint.Name : "-")}' " +
                      $"{director.MetresToNext:0} m");
        }

        // One extra frame at a lower pane opacity. The knob is the whole point of the glass
        // treatment, and an untested knob that silently does nothing is exactly the kind of
        // thing this project has shipped before - so it gets a render of its own.
        session.autoLapsFromTarget = true;
        session.SelectCourse("sakura_circuit");
        session.SetTargetDuration(30f);
        session.ResetRide();
        for (int i = 0; i < 120; i++) { session.Tick(Dt); director.Tick(Dt); }
        SetDistance(session, 0.779f * session.Course.Length);
        director.Tick(Dt);
        if (boot.follower != null) { boot.follower.enabled = true; boot.follower.Apply(); }
        if (streamer != null) streamer.ShowAll();
        float prevOpacity = hud.map.panelOpacity;
        hud.map.panelOpacity = 0.45f;
        hud.map.ApplyPanelOpacity();
        hud.Refresh();
        hud.map.Refresh(Dt);
        Canvas.ForceUpdateCanvases();
        RenderRiderView(boot, Path.Combine(OutDir, "gps_pane_opacity_045.png"));
        Debug.Log($"[gps] gps_pane_opacity_045: panelOpacity {hud.map.panelOpacity} " +
                  $"(default {prevOpacity})");
        hud.map.panelOpacity = prevOpacity;
        hud.map.ApplyPanelOpacity();

        // Leave the scene in its default state so a later capture or play session is clean.
        devices.acceptKeyboardEffort = prevKeyboardEffort;
        devices.ResetEffort();
        session.autoLapsFromTarget = true;
        session.SelectCourse("sakura_circuit");
        session.ResetRide();
        if (boot.follower != null) boot.follower.Apply();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[gps] captures written to {OutDir}");
    }

    private static void SetDistance(RideSession session, float metres)
    {
        session.SeekTo(metres);
    }

    /// <summary>
    /// Renders the rider's eye view with the real HUD canvas composited in.
    ///
    /// The canvas is temporarily driven by the capture camera in SCREEN SPACE, at the same
    /// 1920x1080 its CanvasScaler references, so UI pixels land exactly on target pixels. The
    /// first version hand-scaled a world-space canvas onto a quad one metre ahead; the maths
    /// was right to four decimal places but everything still landed on half-pixels, and 11-13 px
    /// captions came out as grey mush in every capture - a HUD defect that did not exist in the
    /// HUD. Driving the canvas from the camera makes the capture a faithful record of play mode,
    /// which is the only thing that makes "look at the render" a valid test.
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

        var go = new GameObject("~RideCam");
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

        // A second, un-graded camera for the HUD only. Rendering the canvas through the gameplay
        // camera runs it through SakuraPostFX's bloom and sunset tint, which turned the washi
        // cards pink and the ink text almost unreadable. Depth-only clear composites it straight
        // on top of the already-graded world pass in the same target.
        var hudGo = new GameObject("~RideHudCam");
        hudGo.transform.SetParent(go.transform, false);
        var hudCam = hudGo.AddComponent<Camera>();
        hudCam.clearFlags = CameraClearFlags.Depth;
        hudCam.cullingMask = 1 << HudSprites.UiLayer;
        hudCam.fieldOfView = cam.fieldOfView;
        hudCam.nearClipPlane = 0.05f;
        hudCam.farClipPlane = 100f;
        hudCam.depth = cam.depth + 10f;
        hudCam.allowHDR = false;

        // Target assigned BEFORE the canvas is switched: a screen-space-camera canvas sizes
        // itself from the camera's pixel rect, which is the render texture's, not the window's.
        var tex = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = tex;
        hudCam.targetTexture = tex;

        var canvas = boot.hud.Canvas;
        var rt = (RectTransform)canvas.transform;
        var prevMode = canvas.renderMode;
        var prevCam = canvas.worldCamera;
        var prevPlane = canvas.planeDistance;
        var prevPos = rt.position;
        var prevRot = rt.rotation;
        var prevScale = rt.localScale;
        var prevSize = rt.sizeDelta;

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = hudCam;
        canvas.planeDistance = 1f;
        HudSprites.SetLayerRecursively(canvas.gameObject, HudSprites.UiLayer);
        Canvas.ForceUpdateCanvases();
        boot.hud.map.Refresh(Dt);
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
        UnityEngine.Object.DestroyImmediate(img);
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(go);

        canvas.renderMode = prevMode;
        canvas.worldCamera = prevCam;
        canvas.planeDistance = prevPlane;
        rt.position = prevPos;
        rt.rotation = prevRot;
        rt.localScale = prevScale;
        rt.sizeDelta = prevSize;
    }

    private static void OpenScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}

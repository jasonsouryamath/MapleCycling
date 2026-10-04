using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Play-mode harness that drives and PHOTOGRAPHS the opening flow:
/// title -> world-map selection -> 3 . 2 . 1 . GO -> riding.
///
/// It exists because none of this is provable from a log line. "Input is locked" is only true if
/// the bike does not move while power is being applied, and "the map is not empty" is only true
/// if the render shows a road. So the harness applies full effort THROUGH the countdown and
/// asserts the ride did not advance, then releases and asserts it did - and writes a frame at
/// every beat so the flow can be reviewed by eye.
///
/// Driven by <c>MapleRideFlowCapture</c> (editor), which enters play mode and spawns this.
/// </summary>
public class MapleRideFlowRunner : MonoBehaviour
{
    public string outDir;

    /// <summary>Effort held down the whole time, to prove the gate rather than assume it.</summary>
    [Range(0f, 1f)] public float playerEffort = 1f;

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private MapleRideFlowDirector _flow;
    private Camera _cam;
    private Camera _uiCam;
    private readonly List<string> _report = new List<string>();

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        yield return null;                         // let the scene finish waking up

        _boot = FindAnyObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }

        // The trainer/keyboard stand-in: a constant, maximum effort held for the whole capture.
        // The gate has to hold this back, not merely the absence of a keypress.
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.EffortInput = playerEffort;

        // ---------------------------------------------------------------- 0. REAL input probe
        // Done BEFORE the capture routing below switches canvases to camera space, so the
        // raycasts are the ones a player's mouse gets in the shipped overlay setup.
        var title = FindAnyObjectByType<MapleRideTitleScreen>();
        if (title == null) { Fail("no title screen - forceInBatchMode did not take"); yield break; }
        ProbeEventSystems("title");
        var startBtn = title.GetComponentInChildren<Button>(true);
        ProbeHit("START button", startBtn != null ? (RectTransform)startBtn.transform : null, startBtn != null ? startBtn.gameObject : null);

        RouteCanvasesToCamera();

        // ---------------------------------------------------------------- 1. title
        Note($"title present, timeScale={Time.timeScale:0.##}");
        yield return Capture(0.2f, "flow_1_title");

        // ---------------------------------------------------------------- 2. ENTER -> world map
        title.StartRide();                         // exactly what ENTER / the START button does
        yield return null;
        yield return null;

        _flow = MapleRideFlowDirector.Instance;
        if (_flow == null) { Fail("ENTER did not create the flow director"); yield break; }
        RouteFlowCanvas();

        // CAPTURE PACING ONLY. Encoding a 1920x1080 PNG costs a large fraction of a second in
        // batchmode, so the shipping 1.0 s beats would have the harness chasing a count that has
        // already moved on. The beats are lengthened so each numeral can be photographed; the
        // countdown's behaviour, artwork and input gating are unchanged.
        _flow.beatSeconds = 1.8f;
        _flow.goHoldSeconds = 1.8f;
        Note("capture pacing: beatSeconds=1.8, goHoldSeconds=1.8 (shipping defaults 1.0 / 0.85)");

        var hud = _boot.hud;
        var map = hud != null ? hud.worldMap : null;
        bool mapOpen = map != null && map.isOpen;
        bool hudHidden = hud != null && hud.Canvas != null &&
                         !FindChildActive(hud.Canvas.transform, "Telemetry Card");
        Note($"after ENTER: phase={_flow.phase} worldMapOpen={mapOpen} rideWidgetsHidden={hudHidden} " +
             $"selectionBgm={_flow.SelectionBgmPlaying} timeScale={Time.timeScale:0.##} " +
             $"inputLocked={RideInputGate.Locked}");
        if (_flow.phase != MapleRideFlowDirector.Phase.MapSelection) Fail("ENTER did not open map selection");
        if (!mapOpen) Fail("the world map overlay is not open after ENTER");
        if (!_flow.SelectionBgmPlaying) Fail("no game-selection BGM during map selection");
        if (!RideInputGate.Locked) Fail("ride input is NOT locked during map selection");

        // REAL input probe on the map: raycast every unlocked pin in overlay space, as a mouse would.
        ProbeEventSystems("map");
        GameObject sakuraClick = null;
        // Optional MR_FLOW_REGION env var: ride a different map than Sakura (default unchanged).
        string pickId = System.Environment.GetEnvironmentVariable("MR_FLOW_REGION");
        pickId = string.IsNullOrWhiteSpace(pickId) ? RegionCatalog.SakuraPass : pickId.Trim();
        var restore = ToOverlay();
        yield return null;
        foreach (var r in RegionCatalog.Regions)
        {
            if (!r.Unlocked || map.Overlay == null) continue;
            var pin = map.Overlay.GetComponentsInChildren<RectTransform>(true);
            RectTransform dot = null;
            foreach (var rt in pin) if (rt.name == "Dot" && rt.parent != null && rt.parent.name == "Pin " + r.Id) dot = rt;
            var h = ProbeHit("pin " + r.Id, dot, dot != null ? dot.gameObject : null);
            if (r.Id == pickId) sakuraClick = h;
        }
        Restore(restore);
        Note($"keyboard focus on open: '{map.FocusedRegionId}'");
        if (string.IsNullOrEmpty(map.FocusedRegionId)) Fail("no pin has keyboard focus - ENTER could not pick a map");
        yield return Capture(0.2f, "flow_2_worldmap");

        float distAtSelect = _boot.session.TotalDistanceM;

        // ---------------------------------------------------------------- 3. pick a map
        // Click what the RAYCAST hit (not map.Travel directly), so a blocked pin fails here.
        if (sakuraClick != null)
        {
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(sakuraClick, ped, ExecuteEvents.pointerClickHandler);
        }
        else Fail($"the '{pickId}' pin is not clickable - nothing with a click handler under it");
        yield return null;
        Note($"after pin click: phase={_flow.phase} region={_boot.regions.currentRegionId} " +
             $"course={_boot.session.Course?.DisplayName} timeScale={Time.timeScale:0.##} " +
             $"inputLocked={RideInputGate.Locked}");
        if (_flow.phase != MapleRideFlowDirector.Phase.Countdown) Fail("selecting a map did not start the countdown");

        // ---------------------------------------------------------------- 4. the countdown
        // Beats are WAITED FOR, never timed: PNG encoding of a 1920x1080 frame costs enough real
        // time that fixed delays drift, and a drifted capture photographs the seam between two
        // numerals (or, worse, an already-finished countdown) while the log still says PASS.
        yield return WaitForBeat("3");
        // The freeze evidence is taken while the count is unambiguously mid-flight.
        float distDuringFreeze = _boot.session.TotalDistanceM;
        float speedDuringFreeze = _boot.session.SpeedMps;
        Note($"during freeze at full effort: totalDistance={distDuringFreeze:0.000} m " +
             $"speed={speedDuringFreeze:0.000} m/s inputLocked={RideInputGate.Locked}");
        if (!RideInputGate.Locked) Fail("input unlocked before GO");
        if (speedDuringFreeze > 0.001f) Fail($"the bike MOVED during the countdown ({speedDuringFreeze:0.000} m/s)");
        if (distDuringFreeze - distAtSelect > 0.001f) Fail("the ride advanced during the countdown");

        yield return Capture(0.25f, "flow_3_countdown_3");
        yield return WaitForBeat("2");
        yield return Capture(0.25f, "flow_4_countdown_2");
        yield return WaitForBeat("1");
        float distLateFreeze = _boot.session.TotalDistanceM;
        if (!RideInputGate.Locked) Fail("input unlocked during the final count");
        if (distLateFreeze - distAtSelect > 0.001f) Fail("the ride advanced during the final count");
        yield return Capture(0.25f, "flow_5_countdown_1");

        // ---------------------------------------------------------------- 5. GO
        // Waited for, not timed: PNG encoding of a 1920x1080 frame costs enough real time that a
        // fixed delay drifted right past the GO hold and photographed an empty screen.
        while (RideInputGate.Locked) yield return null;
        yield return Capture(0.12f, "flow_6_go");
        Note($"at GO: phase={_flow.phase} inputLocked={RideInputGate.Locked}");
        if (RideInputGate.Locked) Fail("input is still locked at GO");

        // ---------------------------------------------------------------- 6. riding
        yield return Capture(2.5f, "flow_7_riding");
        float distAfter = _boot.session.TotalDistanceM;
        Note($"after GO: totalDistance={distAfter:0.00} m speed={_boot.session.SpeedMps:0.00} m/s " +
             $"phase={_flow.phase}");
        if (distAfter <= 0.5f) Fail("the ride did not start after GO - the bike is still stuck");

        // ---------------------------------------------------------------- 7. in-ride map + music
        var audio = FindAnyObjectByType<RideAudio>();
        float gateRiding = audio != null ? audio.MusicGate : -1f;
        map.SetOpen(true);                          // exactly what TAB / the WORLD MAP button does
        yield return WaitReal(0.8f);
        float gateMap = audio != null ? audio.MusicGate : -1f;
        yield return Capture(0.05f, "flow_8_inride_map");
        map.SetOpen(false);
        yield return WaitReal(1.8f);
        float gateBack = audio != null ? audio.MusicGate : -1f;
        Note($"region music gate: riding={gateRiding:0.00} map-open={gateMap:0.00} after-close={gateBack:0.00}");
        if (audio == null) Fail("no RideAudio to check the music");
        else
        {
            if (gateMap > 0.05f) Fail("region music still playing under the in-ride World Map");
            if (gateBack < 0.9f) Fail("region music did not come back after closing the World Map");
        }

        foreach (var line in _report) Debug.Log("[flow-play] " + line);
        Debug.Log($"[flow-play] {(Failed ? "FAILED" : "PASS")} - frames written to {outDir}");
        Finished = true;
    }

    private void Update()
    {
        // Hold the effort down for the WHOLE capture, countdown included.
        if (_boot != null && _boot.devices != null) _boot.devices.EffortInput = playerEffort;
    }

    private void Note(string s) => _report.Add(s);

    private static IEnumerator WaitReal(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    // ================================================================= real-input probes

    private void ProbeEventSystems(string when)
    {
        var all = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var names = new List<string>();
        foreach (var e in all) names.Add($"{e.name}{(e.isActiveAndEnabled ? "" : "(off)")}");
        var cur = EventSystem.current;
        Note($"[{when}] EventSystems={all.Length} [{string.Join(", ", names)}] current={(cur != null ? cur.name : "NULL")} " +
             $"selected={(cur != null && cur.currentSelectedGameObject != null ? cur.currentSelectedGameObject.name : "-")}");
        if (all.Length != 1) Fail($"[{when}] expected exactly 1 EventSystem, found {all.Length}");
    }

    /// <summary>Raycasts the centre of <paramref name="target"/> through the live EventSystem and
    /// returns the object that would receive the click (null if nothing clickable is on top).</summary>
    private GameObject ProbeHit(string label, RectTransform target, GameObject expect)
    {
        var es = EventSystem.current;
        if (target == null || es == null) { Fail($"{label}: {(target == null ? "no target" : "no EventSystem")}"); return null; }
        Canvas.ForceUpdateCanvases();
        var canvas = target.GetComponentInParent<Canvas>();
        var cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, target.position);
        var ped = new PointerEventData(es) { position = screen };
        var hits = new List<RaycastResult>();
        es.RaycastAll(ped, hits);
        var top = hits.Count > 0 ? hits[0].gameObject : null;
        var handler = top != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(top) : null;
        var desc = new List<string>();
        for (int i = 0; i < Mathf.Min(3, hits.Count); i++)
        {
            var c = hits[i].gameObject.GetComponentInParent<Canvas>();
            desc.Add($"{hits[i].gameObject.name}@{(c != null ? c.rootCanvas.name + "/" + c.rootCanvas.sortingOrder : "?")}");
        }
        bool ok = handler != null && expect != null && (handler == expect || handler.transform.IsChildOf(expect.transform.parent));
        Note($"{label}: screen=({screen.x:0},{screen.y:0}) of {Screen.width}x{Screen.height} hits={hits.Count} " +
             $"top=[{string.Join(" | ", desc)}] clickHandler={(handler != null ? handler.name : "NONE")} {(ok ? "OK" : "BLOCKED")}");
        if (!ok) Fail($"{label} does not receive a mouse click (top hit {(top != null ? top.name : "none")})");
        return handler;
    }

    private List<(Canvas c, RenderMode m, Camera cam)> ToOverlay()
    {
        var list = new List<(Canvas, RenderMode, Camera)>();
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceCamera && c.worldCamera == _uiCam)
            { list.Add((c, c.renderMode, c.worldCamera)); c.renderMode = RenderMode.ScreenSpaceOverlay; }
        return list;
    }

    private static void Restore(List<(Canvas c, RenderMode m, Camera cam)> list)
    {
        foreach (var (c, m, cam) in list) if (c != null) { c.renderMode = m; c.worldCamera = cam; }
    }

    /// <summary>Blocks until the countdown is showing this numeral (watchdogged).</summary>
    private IEnumerator WaitForBeat(string value)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (_flow != null && _flow.CurrentBeat == value) yield break;
            yield return null;
        }
        Fail($"the countdown never showed '{value}'");
    }

    private void Fail(string why)
    {
        Debug.LogError("[flow-play] FAIL: " + why);
        _report.Add("FAIL: " + why);
        Failed = true;
    }

    private static bool FindChildActive(Transform root, string exactName)
    {
        foreach (Transform c in root)
            if (c.name == exactName) return c.gameObject.activeSelf;
        return false;
    }

    // ================================================================= capture plumbing

    /// <summary>
    /// The HUD canvases are screen-space OVERLAY, which a manual Camera.Render() never
    /// composites. They are routed to a dedicated flat UI camera sharing the render target, the
    /// same way the Hanakage play-mode capture does it.
    /// </summary>
    private void RouteCanvasesToCamera()
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);

        var uiGo = new GameObject("~FlowHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.fieldOfView = _cam.fieldOfView;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;

        Route(_boot.hud != null ? _boot.hud.Canvas : null, 1f);

        var title = FindAnyObjectByType<MapleRideTitleScreen>();
        if (title != null)
            foreach (var c in title.GetComponentsInChildren<Canvas>(true)) Route(c, 0.7f);
    }

    private void RouteFlowCanvas()
    {
        if (_flow == null) return;
        // The countdown canvas is built on demand, so it can only be routed once it exists.
        StartCoroutine(RouteFlowWhenBuilt());
    }

    private IEnumerator RouteFlowWhenBuilt()
    {
        for (int i = 0; i < 600; i++)
        {
            if (_flow != null && _flow.CountdownCanvas != null)
            {
                Route(_flow.CountdownCanvas, 0.8f);
                yield break;
            }
            yield return null;
        }
    }

    private void Route(Canvas c, float planeDistance)
    {
        if (c == null || _uiCam == null) return;
        c.renderMode = RenderMode.ScreenSpaceCamera;
        c.worldCamera = _uiCam;
        c.planeDistance = planeDistance;
        HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
    }

    private IEnumerator Capture(float delaySeconds, string name)
    {
        float end = Time.realtimeSinceStartup + delaySeconds;
        while (Time.realtimeSinceStartup < end) yield return null;
        yield return null;

        const int W = 1920, H = 1080;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        if (_uiCam != null)
        {
            var prevUi = _uiCam.targetTexture;
            _uiCam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            _uiCam.Render();
            _uiCam.targetTexture = prevUi;
        }

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);

        Debug.Log($"[flow-play] wrote {name}.png  (phase={(_flow != null ? _flow.phase.ToString() : "-")}, " +
                  $"locked={RideInputGate.Locked}, dist={(_boot != null && _boot.session != null ? _boot.session.TotalDistanceM : 0f):0.00} m)");
    }
}

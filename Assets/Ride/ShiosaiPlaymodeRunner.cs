using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Photographs SHIOSAI COAST from the REAL gameplay camera, in REAL play mode, with the HUD up.
///
/// Why this exists: every previous Shiosai verification frame was an editor diagnostic camera
/// render (ShiosaiCoastDiagnostics), which does NOT run the player loop, does not use the ride
/// camera rig, and is trivially able to look good while the thing the player actually sees is
/// a placeholder. The art pass is only allowed to be called done against frames taken here.
///
/// Driven by ShiosaiPlaymodeCapture (editor side), which enters play mode and spawns this.
/// </summary>
public class ShiosaiPlaymodeRunner : MonoBehaviour
{
    /// <summary>Where the PNGs land (absolute; set by the editor side).</summary>
    public string outDir;

    /// <summary>
    /// PROVISIONAL: distances along the Shiosai course to photograph, in metres.
    /// Spread across BOTH legs of the 5.78 km out-and-back, because the ambient traffic
    /// director works in road space and has to be verified riding the road in both directions.
    /// </summary>
    public float[] stations = { 60f, 420f, 900f, 1450f, 2050f, 2600f, 3400f, 4250f, 5100f };

    /// <summary>PROVISIONAL scripted effort so the rider actually rolls while framing.</summary>
    [Range(0f, 1f)] public float playerEffort = 0.7f;

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private Camera _cam;
    private Camera _uiCam;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        _boot.Resolve();

        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }

        yield return null;

        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.ShiosaiCoast))
            Debug.LogWarning("[shiosai-play] could not fast travel to the coast region.");

        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("shiosai_breeze");
        _boot.devices.acceptKeyboardEffort = false;

        RouteCanvasesToCamera();

        yield return null;
        Debug.Log($"[shiosai-play] course '{_boot.session.courseId}', " +
                  $"{(_boot.session.Course != null ? _boot.session.Course.Length : 0f):0} m.");

        for (int i = 0; i < stations.Length; i++)
        {
            _boot.session.SeekTo(stations[i]);
            if (_boot.follower != null) _boot.follower.Apply();
            // The chase camera LERPS at followSharpness, so after a teleport it spends about a
            // second out in the sea looking back at the coast - which is exactly the frame the
            // first baseline capture produced. Snap it onto its own desired pose first.
            SnapCamera();
            for (int f = 0; f < 24; f++)
            {
                _boot.devices.EffortInput = playerEffort;
                yield return null;
            }
            SnapCamera();
            yield return Capture($"play_shiosai_{i + 1}_{stations[i]:0000}m");
        }

        // --- ambient traffic burst -----------------------------------------------------------
        // One frame can prove riders EXIST; it cannot prove they are moving at an unhurried
        // steady pace rather than sprinting. So sit at one station and photograph the same view
        // repeatedly with a known amount of simulated time between frames: how far a rider
        // travels between consecutive frames IS the pace, measurable off the images.
        yield return TrafficBurst();
        yield return DraftSequence();
        yield return TrafficAudit();

        Debug.Log($"[shiosai-play] done, {stations.Length} frames written to {outDir}");
        Finished = true;
    }

    private void Fail(string why)
    {
        Debug.LogError("[shiosai-play] " + why);
        Failed = true;
        Finished = true;
    }

    /// <summary>PROVISIONAL: where the pace burst is shot from, and how it is spaced.</summary>
    public float burstStationM = 1450f;
    public int burstFrames = 5;
    public float burstIntervalS = 1.2f;

    /// <summary>
    /// Photographs one fixed viewpoint several times with a known interval, so ambient rider
    /// speed can be READ OFF the images instead of taken on trust from a log line. The player is
    /// held stationary (zero effort) so that everything that moves between frames is traffic.
    ///
    /// QA fix: "zero effort" here used to still free-run the simulator's autonomous endurance
    /// band (~150-165 W), which is a real hold now, via DeviceManager.HoldZeroPower rather than
    /// EffortInput alone - see MinatoIdleStartRunner's header for how that was found.
    /// </summary>
    private IEnumerator TrafficBurst()
    {
        _boot.session.SeekTo(burstStationM);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        _boot.devices.HoldZeroPower = true;
        for (int f = 0; f < 24; f++) { _boot.devices.EffortInput = 0f; yield return null; }
        SnapCamera();

        var traffic = FindFirstObjectByType<ShiosaiTrafficDirector>();
        for (int i = 0; i < burstFrames; i++)
        {
            if (i > 0)
            {
                float until = Time.time + burstIntervalS;
                while (Time.time < until) { _boot.devices.EffortInput = 0f; yield return null; }
                SnapCamera();
            }
            if (traffic != null)
                Debug.Log($"[shiosai-play] burst {i}: t={Time.time:0.00}s " +
                          $"traffic active={traffic.activeRiders} " +
                          $"sameWay={traffic.sameWayRiders} oncoming={traffic.oncomingRiders} " +
                          $"playerRoadD={traffic.playerRoadDistanceM:0} m " +
                          $"dir={traffic.playerRoadDirection}");
            yield return Capture($"play_shiosai_burst_{i}_{burstIntervalS * i:0.0}s");
        }
        _boot.devices.HoldZeroPower = false;   // DraftSequence needs the legacy autonomous band
    }

    /// <summary>PROVISIONAL: where the drafting / overtaking proof sequence is shot.</summary>
    public float draftStationM = 1150f;
    public float pacerGapM = 16f;
    public float pacerPaceMps = 7.0f;

    /// <summary>
    /// Proves the close-following mechanic ON CAMERA, in play mode, in this order:
    ///   1  approach      - closing on a same-direction rider, draft bar starting to fill
    ///   2  perfect       - tucked on their wheel, bar full, gap pinned at the minimum
    ///   3  blocked       - still flat out, still behind: the gap does NOT go to zero
    ///   4  alongside     - pulled out towards the centreline, drawing level
    ///   5  past          - through, in front, in the lead
    ///   6  settled       - back on the lane line, the passed rider sitting behind
    ///
    /// A deterministic pacer is placed in front of the player (SpawnPacerAhead) rather than
    /// waiting on the ambient RNG, so this sequence is the same every run and the numbers in
    /// the log can be checked against the images.
    /// </summary>
    private IEnumerator DraftSequence()
    {
        var traffic = FindFirstObjectByType<ShiosaiTrafficDirector>();
        var follower = _boot.follower;
        if (traffic == null || follower == null)
        {
            Debug.LogWarning("[shiosai-play] no traffic director / follower - draft capture skipped.");
            yield break;
        }

        _boot.session.SeekTo(draftStationM);
        follower.Apply();
        SnapCamera();
        for (int f = 0; f < 24; f++) { _boot.devices.EffortInput = 0.55f; yield return null; }
        SnapCamera();

        int pacer = traffic.SpawnPacerAhead(pacerGapM, pacerPaceMps);
        Debug.Log($"[shiosai-play] draft: pacer index {pacer} at {pacerGapM:0} m, " +
                  $"{pacerPaceMps * 3.6f:0.0} km/h.");

        // ---- 1: closing -------------------------------------------------------------------
        yield return Ride(() => traffic.draftFactor > 0.2f, 45f, 1f);
        LogDraft(traffic, follower, "approach");
        yield return Capture("play_shiosai_draft_1_approach");

        // ---- 2/3: tucked in and held out --------------------------------------------------
        yield return Ride(() => traffic.blocked && traffic.draftFactor > 0.9f, 30f, 1f);
        LogDraft(traffic, follower, "perfect");
        yield return Capture("play_shiosai_draft_2_perfect");

        // The chase camera sits directly behind the player, and a chibi rider's head is wide
        // enough to hide another rider two metres in front of them completely - which is
        // geometrically correct and useless as evidence. So the same moment is photographed
        // again from over the player's shoulder, where the gap can actually be READ.
        yield return Capture3Q(traffic, "play_shiosai_draft_2b_perfect_3q", 1f);

        // Flat out for three more seconds while directly behind them. If the blocking works,
        // the gap does not collapse and the player does not end up inside the rider.
        float minGap = float.MaxValue;
        float until = Time.time + 3f;
        while (Time.time < until)
        {
            _boot.devices.EffortInput = 1f;
            minGap = Mathf.Min(minGap, traffic.draftGapM);
            yield return null;
        }
        Debug.Log($"[shiosai-play] draft: flat out behind for 3 s, closest gap {minGap:0.00} m " +
                  $"(floor {traffic.minFollowGapM:0.00} m) - no clip-through.");
        LogDraft(traffic, follower, "blocked");
        yield return Capture("play_shiosai_draft_3_blocked");

        // ---- 4: pull out -------------------------------------------------------------------
        int before = traffic.overtakesCompleted;
        follower.SteerOverride = 1f;
        yield return Ride(() => traffic.AheadOfPlayerM(pacer) < 1.2f, 14f, 1f);
        LogDraft(traffic, follower, "alongside");
        yield return Capture("play_shiosai_draft_4_alongside");

        // ---- 5: through --------------------------------------------------------------------
        yield return Ride(() => traffic.overtakesCompleted > before, 16f, 1f);
        Debug.Log($"[shiosai-play] draft: overtakes {before} -> {traffic.overtakesCompleted}, " +
                  $"pacer now {traffic.AheadOfPlayerM(pacer):0.0} m (negative = behind).");
        yield return Capture("play_shiosai_draft_5_past");

        // ---- 6: settle back onto the lane line ----------------------------------------------
        follower.SteerOverride = 0f;
        yield return Ride(() => false, 5f, 0.62f);
        Debug.Log($"[shiosai-play] draft: settled, pacer {traffic.AheadOfPlayerM(pacer):0.0} m " +
                  $"behind, lane {follower.ActiveLaneOffset:0.00} m, " +
                  $"active riders {traffic.activeRiders} " +
                  $"(sameWay {traffic.sameWayRiders} / oncoming {traffic.oncomingRiders}).");
        yield return Capture("play_shiosai_draft_6_settled");

        follower.SteerOverride = float.NaN;
    }

    /// <summary>
    /// Photographs the current moment from over the rider's right shoulder, holding the ride
    /// effort so the situation does not change while the camera moves. Used to make the
    /// following gap legible, which a straight-behind chase camera cannot do.
    /// </summary>
    private IEnumerator Capture3Q(ShiosaiTrafficDirector traffic, string name, float effort)
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        var t = follow != null ? follow.target : null;
        if (t == null) yield break;

        if (follow != null) follow.enabled = false;
        _boot.devices.EffortInput = effort;
        yield return null;
        _cam.transform.position = t.position + t.right * 4.4f - t.forward * 4.0f + Vector3.up * 2.3f;
        _cam.transform.LookAt(t.position + t.forward * 2.2f + Vector3.up * 0.9f);
        yield return Capture(name);
        if (follow != null) follow.enabled = true;
        SnapCamera();
    }

    /// <summary>Rides at a fixed effort until a condition holds or a timeout expires.</summary>
    private IEnumerator Ride(System.Func<bool> until, float timeoutS, float effort)
    {
        float deadline = Time.time + timeoutS;
        while (Time.time < deadline && !until())
        {
            _boot.devices.EffortInput = effort;
            SnapCamera();
            yield return null;
        }
        SnapCamera();
    }

    private void LogDraft(ShiosaiTrafficDirector t, RouteFollower f, string tag) =>
        Debug.Log($"[shiosai-play] draft {tag}: factor {t.draftFactor:0.00} " +
                  $"gap {t.draftGapM:0.00} m blocked={t.blocked} " +
                  $"lane {f.ActiveLaneOffset:0.00} m speed {_boot.session.SpeedKph:0.0} km/h " +
                  $"target '{t.draftRiderName}'.");

    /// <summary>PROVISIONAL: elevated, HUD-free audit viewpoints used to READ traffic density.</summary>
    public float[] auditStations = { 1450f, 2600f, 4250f };    public float auditHeightM = 30f;
    public float auditBackM = 40f;
    public float auditAheadM = 150f;

    private bool _hideHud;

    /// <summary>
    /// The chase camera frames the rider, and the course minimap covers the quadrant the road
    /// recedes into - between them, a nominally busy road can photograph empty. This drone-style
    /// pass lifts the camera above the rider, drops the HUD, and looks straight down the
    /// carriageway so ambient density and lane discipline can be judged honestly.
    /// </summary>
    private IEnumerator TrafficAudit()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        _hideHud = true;
        _boot.devices.HoldZeroPower = true;    // genuinely stationary while auditing density
        foreach (float s in auditStations)
        {
            _boot.session.SeekTo(s);
            if (_boot.follower != null) _boot.follower.Apply();
            if (follow != null) follow.enabled = true;
            for (int f = 0; f < 26; f++) { _boot.devices.EffortInput = 0f; yield return null; }

            var t = follow != null ? follow.target : null;
            if (t == null) continue;
            if (follow != null) follow.enabled = false;
            _cam.transform.position = t.position - t.forward * auditBackM + Vector3.up * auditHeightM;
            _cam.transform.LookAt(t.position + t.forward * auditAheadM);
            yield return Capture($"play_shiosai_audit_{s:0000}m");
        }
        _boot.devices.HoldZeroPower = false;
        if (follow != null) follow.enabled = true;
        _hideHud = false;
    }

    /// <summary>Teleports the chase camera onto the pose it is smoothing toward.</summary>
    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    /// <summary>
    /// HUD canvases onto a dedicated depth-only UI camera sharing the ride camera's target
    /// texture - pointing them straight at a perspective ride camera skews and doubles them.
    /// </summary>
    private void RouteCanvasesToCamera()
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);

        var uiGo = new GameObject("~ShiosaiPlayHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.fieldOfView = _cam.fieldOfView;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;
        StripHdrpWorldPasses(_uiCam);

        var c = _boot.hud != null ? _boot.hud.Canvas : null;
        if (c != null)
        {
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = _uiCam;
            c.planeDistance = 1f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
    }

    /// <summary>
    /// An HDRP camera still evaluates the global volumes and custom passes, so without this the
    /// depth-cleared HUD camera re-applied the coast fog/volumetrics and the SakuraPostFX mist at
    /// far depth over the finished frame: a milky wash across the lower half of every capture,
    /// HUD included. Same fix as MinatoPlaymodeRunner; the real game's Overlay HUD never had it.
    /// </summary>
    private static void StripHdrpWorldPasses(Camera cam)
    {
        var hd = cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        if (hd == null) hd = cam.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.None;
        hd.volumeLayerMask = 0;
        hd.customRenderingSettings = true;
        var fields = new[]
        {
            UnityEngine.Rendering.HighDefinition.FrameSettingsField.AtmosphericScattering,
            UnityEngine.Rendering.HighDefinition.FrameSettingsField.Volumetrics,
            UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess,
            UnityEngine.Rendering.HighDefinition.FrameSettingsField.CustomPass,
        };
        foreach (var f in fields)
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, false);
        }
    }

    private IEnumerator Capture(string name)
    {
        yield return null;

        const int W = 1920, H = 1080;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        if (_uiCam != null && !_hideHud)
        {
            _uiCam.fieldOfView = _cam.fieldOfView;
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

        Debug.Log($"[shiosai-play] {name}: d {_boot.session.DistanceM:0} m, " +
                  $"cam {_cam.transform.position}");
        var traffic = FindFirstObjectByType<ShiosaiTrafficDirector>();
        if (traffic != null)
            Debug.Log($"[shiosai-play] {name}: traffic active={traffic.activeRiders} " +
                      $"sameWay={traffic.sameWayRiders} oncoming={traffic.oncomingRiders} " +
                      $"roadD={traffic.playerRoadDistanceM:0} m dir={traffic.playerRoadDirection}");
    }
}

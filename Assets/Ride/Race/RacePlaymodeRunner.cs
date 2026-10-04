using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Play-mode proof of the NPC race feature, in the real scene with the real HUD. Driven by
/// RacePlaymodeCapture (editor), which backs up and restores the player's saves around it.
///
///   Maple City: racers parked and standing; marker idle at 60 m, approach at 12 m (beam, ring,
///   sparkles, look-at, wave); the E prompt; face-on close-ups of three stances with eyes open
///   and closed (eyelids); the Riders screen's three tabs; a full Standard race vs Hana with a
///   level-up + new insignia (intro, countdown, mid-race HUD, results), then back exactly where
///   Kuro stopped; a Time Trial vs Taro's ghost, forfeited mid-way.
///   Sakura + Minato: their racers are parked (Minato's are clones of pooled traffic riders).
/// Frames: reference/good_graphics/race/race_*.png. "[race-play] RESULT PASS n/n".
/// </summary>
public sealed class RacePlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public static bool Finished, Failed;
    private int _pass, _fail;

    private RideBootstrap _boot;
    private RaceDirector _race;
    private Camera _cam, _uiCam;
    private RenderTexture _rt;
    private bool _hideHud;
    private const int W = 1920, H = 1080;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[race-play] PASS " : "[race-play] FAIL ") + what);
    }

    private IEnumerator Start()
    {
        Finished = Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        SetUpCameras();
        yield return null;

        yield return Travel(RegionCatalog.MapleCity);
        _race = _boot.GetComponent<RaceDirector>();
        Check(_race != null, "RaceDirector is on the ride root");
        if (_race == null) { Fail("no RaceDirector"); yield break; }
        for (int i = 0; i < 60 && _race.Racers.Count < 6; i++) yield return null;
        int standing = 0;
        foreach (var n in _race.Racers)
            if (n.def.CourseId == "maple_city_crit" && (n.AmbientRiding || (n.dismount != null && n.dismount.T > 0.99f))) standing++;
        Check(standing == 6, $"6 Maple City racers taken over (riding the course; got {standing})");
        var kuroBlink = _boot.rider != null ? _boot.rider.GetComponentInChildren<RiderBlink>() ?? _boot.rider.GetComponentInParent<RiderBlink>() : null;
        int blinkers = FindObjectsByType<RiderBlink>(FindObjectsInactive.Include).Length;
        Check(blinkers > 20, $"riders with eyelids: {blinkers}{(kuroBlink != null ? " (Kuro too)" : " (Kuro NOT)")}");

        var hana = _race.FindRacer("hana");
        if (hana == null) { Fail("Hana not parked"); yield break; }

        // --- marker: idle far, approach near, prompt within 5 m
        yield return Follow(hana, -60f, 40);
        var marker = _race.Ui.MarkerLayer.Find("Marker Hana");
        Check(marker != null && marker.gameObject.activeInHierarchy, "Hana's marker shows from 60 m");
        yield return Capture("race_marker_idle_60m");
        yield return Follow(hana, -11f, 90);
        Check(hana.engage > 0.3f, $"Hana engages as Kuro approaches (engage {hana.engage:0.00})");
        yield return Capture("race_marker_approach_11m");
        yield return Follow(hana, -1f, 40);
        Check(_race.Prompt.Visible, "the 'E  Race' prompt card shows within 5 m");
        yield return Capture("race_prompt_hana");

        // --- stances + eyelids, face on, HUD off
        _hideHud = true;
        foreach (var id in new[] { "hana", "kenji", "mei" })
        {
            var n = _race.FindRacer(id);
            if (n == null) { Check(false, id + " parked"); continue; }
            yield return Follow(n, -2f, 30);
            var blink = n.GetComponentInChildren<RiderBlink>(true);
            Check(blink != null, $"{n.def.Name} has eyelids");
            var follow = _cam.GetComponent<KuroFollowCamera>();
            if (follow != null) follow.enabled = false;
            n.lookOverride = _cam.transform;
            foreach (var shut in new[] { 0f, 1f })
            {
                if (blink != null) blink.Override = shut;
                for (int f = 0; f < 3; f++) { FaceOn(n, 1.7f); yield return null; }
                yield return Capture($"race_ride_{id}_{(shut > 0f ? "eyes_closed" : "eyes_open")}");
            }
            for (int f = 0; f < 3; f++) { ThreeQuarter(n); yield return null; }
            yield return Capture($"race_ride_{id}_full");
            if (blink != null) blink.Override = float.NaN;
            n.lookOverride = null;
            if (follow != null) follow.enabled = true;
        }
        _hideHud = false;

        // --- Riders screen
        yield return Follow(hana, -1f, 10);
        foreach (RidersScreen.Tab tab in System.Enum.GetValues(typeof(RidersScreen.Tab)))
        {
            _race.OpenRiders(tab);
            yield return null;
            yield return Capture("race_riders_" + tab.ToString().ToLowerInvariant());
        }
        Check(RaceDirector.ScreenOpen && RideInputGate.Locked, "Riders screen open with the ride paused");
        _race.CloseRiders();
        Check(!RaceDirector.ScreenOpen && !RideInputGate.Locked, "Riders screen closes and the ride resumes");

        // --- a full Standard race vs Hana, set up to level 2 -> 3 (new insignia)
        PlayerPrefs.SetString(RiderProgress.Key, "{\"level\":2,\"xp\":80}");
        RiderProgress.ReloadForTests();
        yield return Follow(hana, -1f, 20);
        float before = _boot.session.DistanceM, beforeTotal = _boot.session.TotalDistanceM;
        _race.StartRace(hana);
        yield return WaitPhase(RaceDirector.Phase.Intro, 5f);
        yield return new WaitForSecondsRealtime(1.4f);
        yield return Capture("race_intro_hana");
        yield return WaitPhase(RaceDirector.Phase.Countdown, 5f);
        yield return new WaitForSecondsRealtime(1.3f);
        yield return Capture("race_countdown");
        yield return WaitPhase(RaceDirector.Phase.Racing, 5f);
        bool shot1 = false, shot2 = false;
        while (_race.CurrentPhase == RaceDirector.Phase.Racing)
        {
            AutoPilot();
            if (!shot1 && _race.Kuro.x > 140f) { shot1 = true; yield return Capture("race_mid_hana"); }
            if (!shot2 && _race.Kuro.x > 330f) { shot2 = true; yield return Capture("race_kick_hana"); }
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.6f);
        yield return Capture("race_results_hana");
        var r = _race.LastResult;
        Check(r.won && r.coins > 0 && r.xp > 0, $"Lv 2 Kuro beats Lv 1 Hana: {RaceMath.FormatTime(r.kuroTime)} vs {RaceMath.FormatTime(r.npcTime)}, +{r.coins} MC, +{r.xp} XP");
        Check(r.level.LeveledUp && r.level.NewInsignia, $"level {r.level.levelBefore} -> {r.level.levelAfter} with a new insignia ({RaceMath.InsigniaName(r.level.levelAfter)})");
        Check(r.firstWin && r.coins == RaceMath.WinCoins(1, RaceType.Standard, true), "first win pays x1.5");
        _race.AutoContinue = true;
        yield return WaitPhase(RaceDirector.Phase.Idle, 8f);
        _race.AutoContinue = false;
        Check(Mathf.Abs(_boot.session.DistanceM - before) < 0.5f && Mathf.Abs(_boot.session.TotalDistanceM - beforeTotal) < 0.5f,
              $"back where Kuro stopped ({before:0.0} m -> {_boot.session.DistanceM:0.0} m)");
        Check(hana.AmbientRiding || (hana.dismount != null && hana.dismount.T > 0.99f), "Hana is back on the road (riding off) after the race");
        yield return new WaitForSecondsRealtime(0.5f);
        yield return Capture("race_after_hana");

        // --- Time Trial vs Taro's ghost, forfeited
        var taro = _race.FindRacer("taro");
        if (taro != null)
        {
            yield return Follow(taro, -1f, 20);
            _race.StartRace(taro);
            yield return WaitPhase(RaceDirector.Phase.Racing, 12f);
            float t0 = Time.time;
            bool ghostShot = false;
            while (_race.CurrentPhase == RaceDirector.Phase.Racing && Time.time - t0 < 12f)
            {
                AutoPilot();
                if (!ghostShot && Time.time - t0 > 6f)
                {
                    ghostShot = true;
                    var ghost = GameObject.Find("~Ghost Taro");
                    Check(ghost != null, "the Time Trial rival is a ghost copy of Taro");
                    Check(!taro.gameObject.activeSelf || (taro.dismount != null && taro.dismount.T > 0.99f), "the real Taro is off the road while his ghost rides");
                    yield return Capture("race_tt_ghost");
                }
                yield return null;
            }
            _race.Forfeit();
            yield return WaitPhase(RaceDirector.Phase.Results, 3f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("race_results_forfeit");
            var fr = _race.LastResult;
            Check(fr.forfeit && !fr.won && fr.coins == 0 && fr.xp == 0 && RiderProgress.RecordFor("taro")?.losses == 1,
                  "forfeit = a loss on the record, no coins, no XP");
            _race.AutoContinue = true;
            yield return WaitPhase(RaceDirector.Phase.Idle, 8f);
            _race.AutoContinue = false;
        }

        // --- the other regions' racers
        foreach (var (region, course, ids) in new[]
                 {
                     (RegionCatalog.SakuraPass, "sakura_circuit", new[] { "ren", "mika", "yuki" }),
                     (RegionCatalog.MinatoCoast, "minato_crossing", new[] { "marina", "kohaku" }),
                 })
        {
            yield return Travel(region);
            if (_boot.session.courseId != course) { _boot.session.SelectCourse(course); yield return null; }
            for (int i = 0; i < 30; i++) yield return null;
            foreach (var id in ids)
            {
                var n = _race.FindRacer(id);
                Check(n != null && (n.AmbientRiding || (n.dismount != null && n.dismount.T > 0.99f)), $"{id} riding in {region}{(n != null && n.def.CloneSource ? " (clone of a traffic rider)" : "")}");
                if (n == null) continue;
                yield return Follow(n, -2f, 45);
                yield return Capture($"race_prompt_{id}");
            }
        }

        Debug.Log($"[race-play] RESULT {(_fail == 0 ? "PASS" : "FAIL")} {_pass}/{_pass + _fail}");
        Failed = _fail > 0;
        Finished = true;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Where the racer is now: riding racers move, so seek to them, not to their start line.</summary>
    private static float At(RaceNpc n) => n.AmbientRiding ? n.RouteM : n.def.SpotM;

    private void AutoPilot()
    {
        var k = _race.Kuro;
        if (k == null) return;
        _race.PedalOverride = 1f;
        float toGo = _race.CurrentDef.LengthM - k.x;
        _race.SprintOverride = toGo < 130f && k.stamina > 0.5f && !k.Bonked ? 1f : 0f;
    }

    private IEnumerator Travel(string region)
    {
        if (_boot.regions != null) _boot.regions.FastTravel(region);
        var r = RegionCatalog.Find(region);
        if (r != null && _boot.session.courseId != r.BuiltCourseId) _boot.session.SelectCourse(r.BuiltCourseId);
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 3;
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.HoldZeroPower = true;
        for (int i = 0; i < 8; i++) yield return null;
    }

    /// <summary>Hold Kuro at a fixed offset from a (moving) racer every frame, like riding up to them.</summary>
    private IEnumerator Follow(RaceNpc n, float offsetM, int frames)
    {
        for (int f = 0; f < frames; f++)
        {
            _boot.session.SeekTo(At(n) + offsetM);
            if (_boot.follower != null) _boot.follower.Apply();
            SnapCamera();
            yield return null;
        }
        SnapCamera();
    }

    private IEnumerator Seek(float d, int frames)
    {
        _boot.session.SeekTo(d);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < frames; f++) yield return null;
        SnapCamera();
    }

    private IEnumerator WaitPhase(RaceDirector.Phase p, float timeout)
    {
        float t = 0f;
        while (_race.CurrentPhase != p && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
        if (_race.CurrentPhase != p) Debug.LogWarning($"[race-play] waited {timeout} s for {p}, still {_race.CurrentPhase}");
    }

    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null || !follow.enabled) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    private void FaceOn(RaceNpc n, float dist)
    {
        Vector3 head = n.HeadPosition;
        Vector3 fwd = n.transform.forward;
        // look at the face from in front and a little to the kerb side of the bike
        Vector3 from = head + fwd * dist + n.transform.right * (n.kerbSide * 0.5f) + Vector3.up * 0.05f;
        _cam.transform.position = from;
        _cam.transform.LookAt(head - Vector3.up * 0.05f);
    }

    private void ThreeQuarter(RaceNpc n)
    {
        Vector3 c = n.transform.position + Vector3.up * 0.75f;
        _cam.transform.position = c + n.transform.forward * 3.2f + n.transform.right * (n.kerbSide * 2.2f) + Vector3.up * 0.4f;
        _cam.transform.LookAt(c);
    }

    private void Fail(string why)
    {
        Debug.LogError("[race-play] " + why);
        Failed = true;
        Finished = true;
    }

    /// <summary>The scene camera and a depth-cleared UI camera both render into one 1920x1080
    /// target for the whole run, so world-projected UI (markers, prompt) lines up with the frame.</summary>
    private void SetUpCameras()
    {
        _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        _cam.targetTexture = _rt;
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);
        var uiGo = new GameObject("~RaceHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;
        _uiCam.targetTexture = _rt;
        var hd = uiGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.None;
        hd.volumeLayerMask = 0;
        hd.customRenderingSettings = true;
        foreach (var f in new[] { UnityEngine.Rendering.HighDefinition.FrameSettingsField.AtmosphericScattering,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.Volumetrics,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.CustomPass })
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, false);
        }
    }

    private void RouteOverlayCanvases()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (c == null || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            var parent = c.transform.parent;
            if (parent != null && parent.GetComponentInParent<Canvas>(true) != null) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = _uiCam;
            c.planeDistance = 1f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
    }

    private void Update()
    {
        if (_uiCam != null) RouteOverlayCanvases();
    }

    private IEnumerator Capture(string name)
    {
        yield return null;   // not WaitForEndOfFrame: it never fires in -batchmode
        RouteOverlayCanvases();
        _cam.Render();
        if (!_hideHud)
        {
            _uiCam.fieldOfView = _cam.fieldOfView;
            Canvas.ForceUpdateCanvases();
            _uiCam.Render();
        }
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log($"[race-play] {name}: {_boot.session.courseId} {_boot.session.DistanceM:0} m, phase {(_race != null ? _race.CurrentPhase.ToString() : "-")}");
    }
}

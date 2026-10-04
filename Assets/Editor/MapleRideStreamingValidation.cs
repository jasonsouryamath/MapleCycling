using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real play-mode smoke test: menu, streamed start, distant seek, region travel.</summary>
[InitializeOnLoad]
public static class MapleRideStreamingValidation
{
    private const string Key = "mapleride.streaming.validation";
    private static double _deadline, _started;
    private static int _stage;
    private static HashSet<string> _firstCells;
    private static HashSet<string> _initiallyLoaded;
    private static string _region, _target, _capturePrefix;
    private static int _initialCount;
    private static int _seekFrame;
    private static bool _expectSeekPause, _sawSeekPause;
    private static float _seekDistance;
    private static string Output => Path.GetFullPath("reference/good_graphics/streaming");

    static MapleRideStreamingValidation()
    {
        EditorApplication.playModeStateChanged += Changed;
    }

    public static void Run()
    {
        MapleRideStreamingScenes.RegisterBuildScenes();
        // A previous WorldMapClick batch can leave its global EditorPrefs flag latched after
        // an interrupted exit. Its runner expects an authoring ride and exits play mode on
        // the empty boot scene, aborting every unrelated QA run. This lab is exclusively held
        // by our runner; clear that stale diagnostic request before entering play mode.
        if (Application.isBatchMode && EditorPrefs.GetBool("mapleride.worldmap.clickplaycapture", false))
        {
            EditorPrefs.SetBool("mapleride.worldmap.clickplaycapture", false);
            Debug.Log("[stream-validation] cleared stale WorldMapClick capture request before isolated QA.");
        }
        if (EditorBuildSettings.scenes.Length == 0 || EditorBuildSettings.scenes[0].path != MapleRideBoot.ScenePath)
            throw new InvalidOperationException("Boot must be scene zero.");
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene(MapleRideBoot.ScenePath, OpenSceneMode.Single);
        SessionState.SetString(Key + ".result", "INCOMPLETE");
        SessionState.SetString(Key + ".environment", Environment.GetEnvironmentVariable("MR_BOOT_VALIDATION") ?? "");
        Environment.SetEnvironmentVariable("MR_BOOT_VALIDATION", "1");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _stage = 0;
            _region = Environment.GetEnvironmentVariable("MR_STREAM_REGION") ?? RegionCatalog.FujiRidge;
            _capturePrefix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + _region;
            _started = EditorApplication.timeSinceStartup;
            _deadline = _started + 900;
            EditorApplication.update += Poll;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Poll;
            bool passed = SessionState.GetString(Key + ".result", "INCOMPLETE") == "PASS";
            Environment.SetEnvironmentVariable("MR_BOOT_VALIDATION", SessionState.GetString(Key + ".environment", ""));
            Debug.Log("[stream-validation] " + (passed ? "PASS" : "FAIL") + ": menu -> near cells -> seek -> region travel.");
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        }
    }

    private static void Poll()
    {
        try { Tick(); }
        catch (Exception exception) { Fail(exception.ToString()); }
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup > _deadline) { Fail("Timed out at stage " + _stage); return; }
        if (_stage == 0)
        {
            if (SceneManager.GetActiveScene().path != MapleRideBoot.ScenePath ||
                Object.FindAnyObjectByType<RideBootstrap>() != null || SceneManager.sceneCount != 1)
            { Fail("Ride/environment loaded before map choice."); return; }
            if (MapleRideBoot.Active == null) { Fail("Startup did not create the boot menu."); return; }
            var title = Object.FindAnyObjectByType<MapleRideTitleScreen>();
            if (title == null) { Fail("Startup did not create the title."); return; }
            title.StartRide();
            _stage = 1;
            return;
        }
        if (_stage == 1)
        {
            var map = Object.FindAnyObjectByType<WorldMapHud>();
            if (map == null || !map.isOpen || !map.selectionMode || string.IsNullOrEmpty(map.FocusedRegionId))
            { Fail("No usable map selection after title."); return; }
            if (Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length != 1)
            { Fail("Duplicate EventSystems in menu."); return; }
            Debug.Log("[stream-validation] menu ready without ride or environment.");
            _started = EditorApplication.timeSinceStartup;
            _stage = 2;
            map.Travel(RegionCatalog.Find(_region));
            return;
        }
        var ride = Object.FindAnyObjectByType<RideBootstrap>();
        if (ride == null) return;
        var streamer = ride.GetComponent<RegionSceneStreamer>();
        if (streamer != null && !string.IsNullOrEmpty(streamer.Failure)) { Fail(streamer.Failure); return; }
        if (_stage == 2)
        {
            if (MapleRideBoot.Active != null || MapleRideFlowDirector.Instance == null ||
                MapleRideFlowDirector.Instance.phase != MapleRideFlowDirector.Phase.Riding) return;
            if (streamer == null || streamer.cells.Length < 2 || !streamer.ReadyAt(ride.session.WorldPosition))
            { Fail("Missing cells or start readiness."); return; }
            _firstCells = new HashSet<string>();
            _initiallyLoaded = new HashSet<string>();
            foreach (var cell in streamer.cells) _firstCells.Add(cell.scenePath);
            foreach (var cell in streamer.cells)
                if (SceneManager.GetSceneByPath(cell.scenePath).isLoaded) _initiallyLoaded.Add(cell.scenePath);
            _initialCount = Loaded(streamer);
            if (_initialCount >= streamer.cells.Length) { Fail("Entire region loaded at start."); return; }
            CheckRegion(_region);
            Debug.Log($"[stream-validation] {_region} start ready in {EditorApplication.timeSinceStartup - _started:0.0}s " +
                      $"(including countdown): {_initialCount}/{streamer.cells.Length} cells resident.");
            ScreenCapture.CaptureScreenshot(Output + "/" + _capturePrefix + "_start.png");
            _stage = 3;
            _deadline = EditorApplication.timeSinceStartup + 300;
            return;
        }
        if (_stage == 3)
        {
            if (!File.Exists(Output + "/" + _capturePrefix + "_start.png")) return;
            // Choose a route seek with absent nearby cells to actually exercise the safety gate.
            float bestFraction = 0.75f;
            int bestMissing = -1;
            foreach (float fraction in new[] { 0.25f, 0.5f, 0.75f, 0.98f })
            {
                var point = ride.session.Course.PositionAt(ride.session.Course.Length * fraction);
                int missing = 0;
                foreach (var cell in streamer.cells)
                    if (cell.bounds.SqrDistance(point) <= streamer.criticalDistanceM * streamer.criticalDistanceM &&
                        !SceneManager.GetSceneByPath(cell.scenePath).isLoaded) missing++;
                if (missing > bestMissing) { bestMissing = missing; bestFraction = fraction; }
            }
            _expectSeekPause = bestMissing > 0;
            _sawSeekPause = false;
            ride.JumpAlongRoute(bestFraction);
            _seekFrame = Time.frameCount;
            _seekDistance = ride.session.DistanceM;
            _started = EditorApplication.timeSinceStartup;
            _stage = 4;
            return;
        }
        if (_stage == 4)
        {
            if (RideInputGate.Locked && RideInputGate.Reason.StartsWith("region-streaming-", StringComparison.Ordinal))
            {
                _sawSeekPause = true;
                if (Mathf.Abs(ride.session.DistanceM - _seekDistance) > 0.1f)
                { Fail("Ride advanced while critical seek cells were missing."); return; }
            }
            if (_expectSeekPause && Time.frameCount > _seekFrame + 2 && !_sawSeekPause &&
                !streamer.ReadyAt(ride.session.WorldPosition))
            { Fail("Missing scenery did not pause the distant seek."); return; }
            if (EditorApplication.timeSinceStartup - _started < 1 || streamer.IsBusy ||
                !streamer.ReadyAt(ride.session.WorldPosition)) return;
            if (RideInputGate.Locked || Time.timeScale == 0) { Fail("Streaming pause did not release after seek."); return; }
            int unloaded = 0;
            foreach (var cell in streamer.cells)
                if (_initiallyLoaded.Contains(cell.scenePath) &&
                    cell.bounds.SqrDistance(ride.session.WorldPosition) > streamer.unloadDistanceM * streamer.unloadDistanceM)
                {
                    if (SceneManager.GetSceneByPath(cell.scenePath).isLoaded) { Fail("Far cell retained after seek."); return; }
                    unloaded++;
                }
            Debug.Log($"[stream-validation] distant seek ready in {EditorApplication.timeSinceStartup - _started:0.0}s; " +
                      $"{Loaded(streamer)}/{streamer.cells.Length} cells resident, {unloaded} former cells unloaded, " +
                      $"critical pause observed={_sawSeekPause}, input resumed.");
            ScreenCapture.CaptureScreenshot(Output + "/" + _capturePrefix + "_seek.png");
            _stage = 5;
            return;
        }
        if (_stage == 5)
        {
            if (!File.Exists(Output + "/" + _capturePrefix + "_seek.png")) return;
            _started = EditorApplication.timeSinceStartup;
            _deadline = _started + 600;
            _stage = 6;
            _target = _region == RegionCatalog.ShiosaiCoast ? RegionCatalog.FujiRidge : RegionCatalog.ShiosaiCoast;
            MapleRideBoot.TravelTo(RegionCatalog.Find(_target));
            return;
        }
        if (_stage == 6)
        {
            if (MapleRideBoot.Active != null || MapleRideFlowDirector.Instance == null ||
                MapleRideFlowDirector.Instance.phase != MapleRideFlowDirector.Phase.Riding) return;
            foreach (string path in _firstCells)
                if (SceneManager.GetSceneByPath(path).isLoaded) { Fail("Previous region cell remained loaded: " + path); return; }
            if (streamer == null || !streamer.ReadyAt(ride.session.WorldPosition))
            { Fail("Destination cells not ready after travel."); return; }
            if (streamer.regionId != _target || ride.regions.currentRegionId != _target)
            { Fail("Travel selected the wrong destination."); return; }
            CheckRegion(_target);
            Debug.Log($"[stream-validation] travel to {streamer.regionId} ready in " +
                      $"{EditorApplication.timeSinceStartup - _started:0.0}s; previous region cells unloaded.");
            EditorApplication.update -= Poll;
            SessionState.SetString(Key + ".result", "PASS");
            EditorApplication.ExitPlaymode();
        }
    }

    private static int Loaded(RegionSceneStreamer streamer)
    {
        int result = 0;
        foreach (var cell in streamer.cells) if (SceneManager.GetSceneByPath(cell.scenePath).isLoaded) result++;
        return result;
    }

    private static void CheckRegion(string id)
    {
        if (SceneManager.GetActiveScene().path != MapleRideBoot.RegionScenePath(id))
            throw new InvalidOperationException("Wrong active scene after selection.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                foreach (var region in RegionCatalog.Regions)
                    if (region.Id != id && !string.IsNullOrEmpty(region.EnvironmentRoot) && root.name == region.EnvironmentRoot)
                        throw new InvalidOperationException("Unselected environment loaded: " + root.name);
    }

    private static void Fail(string reason)
    {
        SessionState.SetString(Key + ".result", "FAIL");
        Debug.LogError("[stream-validation] " + reason);
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

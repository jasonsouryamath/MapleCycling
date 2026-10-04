using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Weather visual capture (play mode). Run through the shared runner WITHOUT -quit:
///   pwsh -NoProfile -File tools/unity/run_steps.ps1 "WeatherCapture.Run|copilot_wx_cap.log|0"
/// Frames: reference/copilot/weather/wx_&lt;preset&gt;_{chase,front}.png
/// </summary>
[InitializeOnLoad]
public static class WeatherCapture
{
    private const string PrefKey = "mapleride.weather.capture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/copilot/weather";
    private const double TimeoutSeconds = 600.0;
    private static double _deadline;

    static WeatherCapture() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("MapleRide/Weather/Capture Weather Visuals (Play Mode)")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[wx-cap] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~WeatherCaptureRunner").AddComponent<WeatherCaptureRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            var only = System.Environment.GetEnvironmentVariable("MAPLERIDE_WXCAP_PRESETS");   // e.g. "SunShower,MarineHaze"
            if (!string.IsNullOrEmpty(only)) runner.presets = only.Split(',');
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[wx-cap] left play mode.");
            // Exit on the next editor tick, not inside the play-mode callback.
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(WeatherCaptureRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[wx-cap] TIMED OUT");
            WeatherCaptureRunner.Failed = WeatherCaptureRunner.Finished = true;
        }
        if (!WeatherCaptureRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

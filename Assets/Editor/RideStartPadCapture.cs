using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// QA capture: editor side of <see cref="RideStartPadCaptureRunner"/>. Enters play mode and
/// photographs the start pad of each region in MR_PAD_REGIONS (default Minato, Maple City,
/// Sakura) into reference/copilot/start_pad/. Run WITHOUT -quit:
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod RideStartPadCapture.Run
/// </summary>
[InitializeOnLoad]
public static class RideStartPadCapture
{
    // SessionState, not EditorPrefs: EditorPrefs is machine-wide and leaked this flag into the
    // other lab's Unity process (COORDINATION log 2026-09-27 23:55).
    private const string PrefKey = "mapleride.startpad.capture";
    private const string PrefRegions = "mapleride.startpad.capture.regions";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/copilot/start_pad";
    private const double TimeoutSeconds = 420.0;
    private static double _deadline;

    static RideStartPadCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Diagnostics/Capture Start Pads (Play Mode)", priority = 29)]
    public static void Run()
    {
        string regions = System.Environment.GetEnvironmentVariable("MR_PAD_REGIONS");
        SessionState.SetString(PrefRegions, string.IsNullOrWhiteSpace(regions) ? "" : regions.Trim());
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PrefKey, true);
        Debug.Log("[pad-cap] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~RideStartPadCaptureRunner");
            var runner = go.AddComponent<RideStartPadCaptureRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.forcePlain = System.Environment.GetEnvironmentVariable("MR_PAD_PLAIN") == "1";
            if (runner.forcePlain) runner.outDir = Path.Combine(runner.outDir, "before");
            string list = SessionState.GetString(PrefRegions, "");
            if (!string.IsNullOrEmpty(list))
                runner.regions = list.Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[pad-cap] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(RideStartPadCaptureRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[pad-cap] TIMED OUT waiting for the runner.");
            RideStartPadCaptureRunner.Failed = true;
            RideStartPadCaptureRunner.Finished = true;
        }
        if (!RideStartPadCaptureRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Shiosai tuna-port play-mode capture (see ShiosaiTunaPortCaptureRunner).
/// Run without -quit: run_steps.ps1 "ShiosaiTunaPortCapture.Run|copilot_tunaport_cap.log|0".
/// Env: MR_TUNAPORT_TAG (file prefix, default "after"), MR_TUNAPORT_ONLY ("chase" / "free" /
/// "free:plan,stalls").
/// </summary>
[InitializeOnLoad]
public static class ShiosaiTunaPortCapture
{
    private const string PrefKey = "mapleride.shiosai.tunaportcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/shiosai_tunaport";
    private const double TimeoutSeconds = 480.0;
    private static double _deadline;

    static ShiosaiTunaPortCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PrefKey, true);   // per editor PROCESS: EditorPrefs is machine-wide and hijacks other labs' play sessions
        Debug.Log("[tunaport-cap] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~ShiosaiTunaPortCaptureRunner");
            var runner = go.AddComponent<ShiosaiTunaPortCaptureRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            string tag = System.Environment.GetEnvironmentVariable("MR_TUNAPORT_TAG");
            if (!string.IsNullOrEmpty(tag)) runner.shotTag = tag;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[tunaport-cap] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(ShiosaiTunaPortCaptureRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[tunaport-cap] TIMED OUT waiting for the runner.");
            ShiosaiTunaPortCaptureRunner.Failed = true;
            ShiosaiTunaPortCaptureRunner.Finished = true;
        }
        if (!ShiosaiTunaPortCaptureRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

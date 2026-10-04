using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Shiosai Coast PERFORMANCE measurement: enters play mode, spawns
/// <see cref="ShiosaiPerfRunner"/>, waits, leaves, and in batchmode exits.
///
/// Run headless WITHOUT -quit:
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod ShiosaiPerfCapture.Run
/// Stage label from the MR_PERF_STAGE environment variable (default "stage").
/// </summary>
[InitializeOnLoad]
public static class ShiosaiPerfCapture
{
    private const string PrefKey = "mapleride.shiosai.perf";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/shiosai_play";
    private const double TimeoutSeconds = 900.0;

    private static double _deadline;

    static ShiosaiPerfCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Environment/Measure Shiosai Performance (Play Mode)", priority = 24)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[shiosai-perf] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~ShiosaiPerfRunner");
            var runner = go.AddComponent<ShiosaiPerfRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            string stage = System.Environment.GetEnvironmentVariable("MR_PERF_STAGE");
            runner.stage = string.IsNullOrEmpty(stage) ? "stage" : stage;
            runner.sweep = System.Environment.GetEnvironmentVariable("MR_PERF_SWEEP") == "1";
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[shiosai-perf] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(ShiosaiPerfRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[shiosai-perf] TIMED OUT waiting for the runner.");
            ShiosaiPerfRunner.Failed = true;
            ShiosaiPerfRunner.Finished = true;
        }
        if (!ShiosaiPerfRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

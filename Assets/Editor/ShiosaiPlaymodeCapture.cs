using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Shiosai Coast GAMEPLAY capture: enters play mode, spawns
/// <see cref="ShiosaiPlaymodeRunner"/>, waits for it, leaves, and in batchmode exits.
///
/// Run headless WITHOUT -quit (or Unity exits before play mode starts):
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod ShiosaiPlaymodeCapture.Run
/// </summary>
[InitializeOnLoad]
public static class ShiosaiPlaymodeCapture
{
    private const string PrefKey = "mapleride.shiosai.playcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/shiosai_play";
    private const double TimeoutSeconds = 420.0;

    private static double _deadline;

    static ShiosaiPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Environment/Capture Shiosai Coast (Play Mode)", priority = 23)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[shiosai-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~ShiosaiPlaymodeRunner");
            var runner = go.AddComponent<ShiosaiPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[shiosai-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(ShiosaiPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[shiosai-play] TIMED OUT waiting for the runner.");
            ShiosaiPlaymodeRunner.Failed = true;
            ShiosaiPlaymodeRunner.Finished = true;
        }
        if (!ShiosaiPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

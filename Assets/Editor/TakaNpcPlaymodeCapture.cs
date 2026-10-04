using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Taka "moving cyclists" (claude-taka2; copy of FujiNpcPlaymodeCapture) play test: enters play mode on the shared scene,
/// spawns <see cref="TakaNpcPlaymodeRunner"/> with TakaNpcRoster's moving-cyclist names, waits
/// for it, leaves play mode, DISCARDS scene changes and (batchmode) exits with the result.
/// Read-only for the scene. Run WITHOUT -quit:
///   run_steps.ps1 "TakaNpcPlaymodeCapture.Run|claude_taka2_play.log|0"
/// Frames: reference/good_graphics/taka_fill2/moving/*.png
/// </summary>
[InitializeOnLoad]
public static class TakaNpcPlaymodeCapture
{
    private const string PrefKey = "mapleride.taka.movingcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/taka_fill2/moving";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static TakaNpcPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/QA/Play-test Taka Moving Cyclists", priority = 64)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[taka-move] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~TakaNpcPlaymodeRunner").AddComponent<TakaNpcPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.riderNames = TakaNpcRoster.MovingCyclistNames;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[taka-move] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(TakaNpcPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[taka-move] TIMED OUT waiting for the runner.");
            TakaNpcPlaymodeRunner.Failed = true;
            TakaNpcPlaymodeRunner.Finished = true;
        }
        if (!TakaNpcPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

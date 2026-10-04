using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Fuji "moving cyclists" play test: enters play mode on the shared scene,
/// spawns <see cref="FujiNpcPlaymodeRunner"/> with FujiNpcRoster's moving-cyclist names, waits
/// for it, leaves play mode, DISCARDS scene changes and (batchmode) exits with the result.
/// Read-only for the scene. Run WITHOUT -quit:
///   run_steps.ps1 "FujiNpcPlaymodeCapture.Run|copilot_fujinpc_play.log|0"
/// Frames: reference/good_graphics/fuji_npc_moving/*.png
/// </summary>
[InitializeOnLoad]
public static class FujiNpcPlaymodeCapture
{
    private const string PrefKey = "mapleride.fuji.movingcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/fuji_npc_moving";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static FujiNpcPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/QA/Play-test Fuji Moving Cyclists", priority = 64)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[fuji-move] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~FujiNpcPlaymodeRunner").AddComponent<FujiNpcPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.riderNames = FujiNpcRoster.MovingCyclistNames;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[fuji-move] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(FujiNpcPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[fuji-move] TIMED OUT waiting for the runner.");
            FujiNpcPlaymodeRunner.Failed = true;
            FujiNpcPlaymodeRunner.Finished = true;
        }
        if (!FujiNpcPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

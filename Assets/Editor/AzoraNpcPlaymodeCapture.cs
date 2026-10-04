using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Azora "moving cyclists" (copilot A2; copy of TakaNpcPlaymodeCapture) play test: enters play mode on the shared scene,
/// spawns <see cref="AzoraNpcPlaymodeRunner"/> with AzoraNpcRoster's moving-cyclist names, waits
/// for it, leaves play mode, DISCARDS scene changes and (batchmode) exits with the result.
/// Read-only for the scene. Run WITHOUT -quit:
///   run_steps.ps1 "AzoraNpcPlaymodeCapture.Run|copilot_azA2_play.log|0"
/// Frames: reference/good_graphics/azora_concept/moving/*.png
/// </summary>
[InitializeOnLoad]
public static class AzoraNpcPlaymodeCapture
{
    private const string PrefKey = "mapleride.azora.movingcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/azora_concept/moving";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static AzoraNpcPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/QA/Play-test Azora Moving Cyclists", priority = 65)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[azora-move] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~AzoraNpcPlaymodeRunner").AddComponent<AzoraNpcPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.riderNames = AzoraNpcRoster.MovingCyclistNames;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[azora-move] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(AzoraNpcPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[azora-move] TIMED OUT waiting for the runner.");
            AzoraNpcPlaymodeRunner.Failed = true;
            AzoraNpcPlaymodeRunner.Finished = true;
        }
        if (!AzoraNpcPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

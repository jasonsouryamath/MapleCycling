using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Maple City LIFE play test (claude-city): enters play mode, spawns
/// <see cref="MapleCityLifePlaymodeRunner"/>, waits, leaves, and in batchmode exits.
/// Run WITHOUT -quit: run_steps.ps1 "MapleCityLifeCapture.Run|claude_city_life_play.log|0"
/// Frames: reference/good_graphics/maple_life/.
/// </summary>
[InitializeOnLoad]
public static class MapleCityLifeCapture
{
    private const string PrefKey = "mapleride.maplelife.playcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/maple_life";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static MapleCityLifeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Diagnostics/Play-test Maple City Life", priority = 27)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[maple-life-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~MapleCityLifeRunner").AddComponent<MapleCityLifePlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[maple-life-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(MapleCityLifePlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[maple-life-play] TIMED OUT waiting for the runner.");
            MapleCityLifePlaymodeRunner.Failed = true;
            MapleCityLifePlaymodeRunner.Finished = true;
        }
        if (!MapleCityLifePlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

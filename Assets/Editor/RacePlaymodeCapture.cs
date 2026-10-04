using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the NPC race play test: enters play mode, spawns
/// <see cref="RacePlaymodeRunner"/>, waits, leaves, and in batchmode exits. The player's
/// wardrobe (MapleCoins), race progress and tracked racer are backed up first and restored
/// afterwards, so the test's races never leak into real play.
/// Run WITHOUT -quit: run_steps.ps1 "RacePlaymodeCapture.Run|claude_race_play.log|0"
/// </summary>
[InitializeOnLoad]
public static class RacePlaymodeCapture
{
    private const string PrefKey = "mapleride.race.playcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/race";
    private const double TimeoutSeconds = 1200.0;
    private static readonly string[] SavedKeys = { "MapleRide.Wardrobe.v1", RiderProgress.Key, "MapleRide.Race.Tracked" };
    private static double _deadline;

    static RacePlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Diagnostics/Play-test NPC Races", priority = 27)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var k in SavedKeys)
        {
            EditorPrefs.SetString(PrefKey + ".backup." + k, PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k) : "\u0000none");
            PlayerPrefs.DeleteKey(k);
        }
        PlayerPrefs.Save();
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[race-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~RacePlaymodeRunner").AddComponent<RacePlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            foreach (var k in SavedKeys)
            {
                string b = EditorPrefs.GetString(PrefKey + ".backup." + k, "\u0000none");
                if (b == "\u0000none") PlayerPrefs.DeleteKey(k); else PlayerPrefs.SetString(k, b);
            }
            PlayerPrefs.Save();
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[race-play] left play mode; saves restored.");
            if (Application.isBatchMode)
                EditorApplication.Exit(RacePlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[race-play] TIMED OUT waiting for the runner.");
            RacePlaymodeRunner.Failed = true;
            RacePlaymodeRunner.Finished = true;
        }
        if (!RacePlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the "racers ride while they challenge" play test: enters play mode, spawns
/// <see cref="RaceRidingPlaymodeRunner"/>, waits, leaves, and in batchmode exits. Saves
/// (wardrobe, race progress, tracked racer) are backed up and restored, and the scene is
/// discarded, exactly like <see cref="RacePlaymodeCapture"/>.
/// Run WITHOUT -quit: run_steps.ps1 "RaceRidingCapture.Run|copilot_raceride_play.log|0"
/// </summary>
[InitializeOnLoad]
public static class RaceRidingCapture
{
    private const string PrefKey = "mapleride.race.ridecapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/race_riding";
    private const double TimeoutSeconds = 900.0;
    private static readonly string[] SavedKeys = { "MapleRide.Wardrobe.v1", RiderProgress.Key, "MapleRide.Race.Tracked" };
    private static double _deadline;

    static RaceRidingCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Diagnostics/Play-test Riding Racers", priority = 28)]
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
        Debug.Log("[race-ride] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~RaceRidingPlaymodeRunner").AddComponent<RaceRidingPlaymodeRunner>();
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
            Debug.Log("[race-ride] left play mode; saves restored.");
            if (Application.isBatchMode)
                EditorApplication.Exit(RaceRidingPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[race-ride] TIMED OUT waiting for the runner.");
            RaceRidingPlaymodeRunner.Failed = true;
            RaceRidingPlaymodeRunner.Finished = true;
        }
        if (!RaceRidingPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

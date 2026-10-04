using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Maple Row play test: enters play mode, spawns
/// <see cref="MapleRowPlaymodeRunner"/>, waits, leaves, and in batchmode exits.
/// The player's wardrobe save is backed up first and restored afterwards, so the test's
/// purchases never leak into real play.
/// Run WITHOUT -quit: run_steps.ps1 "MapleRowPlaymodeCapture.Run|claude_maplerow_play.log|0"
/// </summary>
[InitializeOnLoad]
public static class MapleRowPlaymodeCapture
{
    private const string PrefKey = "mapleride.maplerow.playcapture";
    private const string BackupKey = "mapleride.maplerow.wardrobebackup";
    private const string WardrobeKey = "MapleRide.Wardrobe.v1";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/maple_row";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static MapleRowPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Diagnostics/Play-test Maple Row Shop", priority = 26)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetString(BackupKey, PlayerPrefs.HasKey(WardrobeKey) ? PlayerPrefs.GetString(WardrobeKey) : "\u0000none");
        PlayerPrefs.DeleteKey(WardrobeKey);
        PlayerPrefs.Save();
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[maplerow-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~MapleRowPlaymodeRunner").AddComponent<MapleRowPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            string backup = EditorPrefs.GetString(BackupKey, "\u0000none");
            if (backup == "\u0000none") PlayerPrefs.DeleteKey(WardrobeKey);
            else PlayerPrefs.SetString(WardrobeKey, backup);
            PlayerPrefs.Save();
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[maplerow-play] left play mode; wardrobe save restored.");
            if (Application.isBatchMode)
                EditorApplication.Exit(MapleRowPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[maplerow-play] TIMED OUT waiting for the runner.");
            MapleRowPlaymodeRunner.Failed = true;
            MapleRowPlaymodeRunner.Finished = true;
        }
        if (!MapleRowPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the WORLDMAP-CLICK play-mode proof: enters play mode, spawns
/// <see cref="WorldMapClickPlaymodeRunner"/>, waits for it, leaves, and in batchmode exits with
/// a non-zero code if the click did not actually work.
///
/// Run headless WITHOUT -quit (or Unity exits before play mode starts):
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod WorldMapClickPlaymodeCapture.Run
///
/// Temporary - delete once WORLDMAP-CLICK is fixed and verified (same lifecycle as
/// WorldMapClickProbe, its edit-mode sibling).
/// </summary>
[InitializeOnLoad]
public static class WorldMapClickPlaymodeCapture
{
    private const string PrefKey = "mapleride.worldmap.clickplaycapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const double TimeoutSeconds = 120.0;

    private static double _deadline;

    static WorldMapClickPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Diagnostics/World Map Click Probe (Play Mode)")]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[worldmap-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~WorldMapClickPlaymodeRunner");
            go.AddComponent<WorldMapClickPlaymodeRunner>();
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[worldmap-play] left play mode. Failed=" + WorldMapClickPlaymodeRunner.Failed);
            if (Application.isBatchMode)
                EditorApplication.Exit(WorldMapClickPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[worldmap-play] TIMED OUT waiting for the runner.");
            WorldMapClickPlaymodeRunner.Failed = true;
            WorldMapClickPlaymodeRunner.Finished = true;
        }
        if (!WorldMapClickPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the ride-camera A/B capture. Same shape as
/// <see cref="HanakagePlaymodeCapture"/> and for the same reasons - see its comments.
///
/// Run headless WITHOUT -quit:
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod RideCameraCapture.Run
/// </summary>
[InitializeOnLoad]
public static class RideCameraCapture
{
    private const string PrefKey = "mapleride.ridecam.playcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/ride_camera";

    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static RideCameraCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Ride/Capture Chase Camera A-B (Play Mode)", priority = 64)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[ride-cam] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~RideCameraPlaymodeRunner");
            var runner = go.AddComponent<RideCameraPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[ride-cam] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(RideCameraPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[ride-cam] TIMED OUT waiting for the runner.");
            RideCameraPlaymodeRunner.Failed = true;
            RideCameraPlaymodeRunner.Finished = true;
        }

        if (!RideCameraPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

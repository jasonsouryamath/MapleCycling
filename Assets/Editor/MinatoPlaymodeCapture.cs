using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the MINATO COAST gameplay/traffic capture: enters play mode, spawns
/// <see cref="MinatoPlaymodeRunner"/>, waits for it, leaves, and in batchmode exits.
///
/// Exists because Minato's ambient riders are parked in the saved scene and only spawned by the
/// traffic director at runtime - an editor diagnostic camera cannot photograph them.
///
/// Run headless WITHOUT -quit (or Unity exits before play mode starts):
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod MinatoPlaymodeCapture.Run
/// </summary>
[InitializeOnLoad]
public static class MinatoPlaymodeCapture
{
    private const string PrefKey = "mapleride.minato.playcapture";
    private const string CrowdOnlyPrefKey = "mapleride.minato.playcapture.crowdonly";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/minato_play";
    private const double TimeoutSeconds = 900.0;

    private static double _deadline;

    static MinatoPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Environments/Capture Minato Coast (Play Mode)", priority = 24)]
    public static void Run()
    {
        Begin(false);
    }

    [MenuItem("MapleRide/Diagnostics/Validate Minato Crowd Gait (Play Mode)", priority = 25)]
    public static void RunCrowdValidation()
    {
        Begin(true);
    }

    private static void Begin(bool crowdOnly)
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EditorPrefs.SetBool(PrefKey, true);
        EditorPrefs.SetBool(CrowdOnlyPrefKey, crowdOnly);
        Debug.Log(crowdOnly
            ? "[minato-play] entering play mode for isolated crowd validation..."
            : "[minato-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~MinatoPlaymodeRunner");
            var runner = go.AddComponent<MinatoPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            if (EditorPrefs.GetBool(CrowdOnlyPrefKey, false))
            {
                runner.stations = System.Array.Empty<float>();
                runner.burstFrames = 0;
                runner.auditStations = System.Array.Empty<float>();
            }
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorPrefs.SetBool(CrowdOnlyPrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[minato-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(MinatoPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[minato-play] TIMED OUT waiting for the runner.");
            MinatoPlaymodeRunner.Failed = true;
            MinatoPlaymodeRunner.Finished = true;
        }
        if (!MinatoPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

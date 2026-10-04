using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the play-mode capture: enters play mode, spawns
/// <see cref="HanakagePlaymodeRunner"/>, waits for it, then leaves - and in batchmode, exits the
/// process so a headless run terminates instead of sitting in play mode forever.
///
/// The awkward shape (EditorPrefs + a static hook re-registered by [InitializeOnLoad]) is forced
/// by the domain reload that happens on entering play mode: nothing in this class survives it,
/// so the intent to capture has to be written somewhere that does.
///
/// Run headless WITHOUT -quit, or Unity will exit before play mode ever starts:
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod HanakagePlaymodeCapture.Run
/// The runner calls back here and exits the process itself.
/// </summary>
[InitializeOnLoad]
public static class HanakagePlaymodeCapture
{
    private const string PrefKey = "mapleride.hanakage.playcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/hanakage";

    /// <summary>Watchdog for the whole editor-side wait, in seconds.</summary>
    private const double TimeoutSeconds = 420.0;

    private static double _deadline;

    static HanakagePlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Hanakage/Capture Encounter (Play Mode)", priority = 62)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // A dirty scene left behind by a batchmode run has previously made the editor reopen to
        // an empty Untitled scene for the user. Save nothing; discard on the way out.
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[hanakage-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~HanakagePlaymodeRunner");
            var runner = go.AddComponent<HanakagePlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[hanakage-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(HanakagePlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[hanakage-play] TIMED OUT waiting for the runner.");
            HanakagePlaymodeRunner.Failed = true;
            HanakagePlaymodeRunner.Finished = true;
        }

        if (!HanakagePlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

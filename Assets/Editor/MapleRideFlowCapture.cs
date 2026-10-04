using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the opening-flow capture: enters play mode with the title screen forced ON
/// (batchmode is normally exempt from it), spawns <see cref="MapleRideFlowRunner"/>, waits, and
/// exits the process so a headless run terminates.
///
/// The EditorPrefs + [InitializeOnLoad] shape is forced by the domain reload on entering play
/// mode - nothing in this class survives it, so the intent has to be written where it does.
///
/// Run WITHOUT -quit (Unity would exit before play mode starts) and WITHOUT -nographics
/// (the capture renders):
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod MapleRideFlowCapture.Run
/// </summary>
[InitializeOnLoad]
public static class MapleRideFlowCapture
{
    private const string PrefKey = "mapleride.flow.playcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../good_graphics/flow";
    private const double TimeoutSeconds = 600.0;

    private static double _deadline;

    static MapleRideFlowCapture()
    {
        // Runs on every domain reload, INCLUDING the one that enters play mode, and before any
        // [RuntimeInitializeOnLoadMethod]. That is the only window in which the title screen's
        // batchmode exemption can be lifted for this capture.
        MapleRideTitleScreen.forceInBatchMode = EditorPrefs.GetBool(PrefKey, false);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Flow/Capture Opening Flow (Play Mode)", priority = 40)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EditorPrefs.SetBool(PrefKey, true);
        MapleRideTitleScreen.forceInBatchMode = true;
        Debug.Log("[flow-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~MapleRideFlowRunner");
            var runner = go.AddComponent<MapleRideFlowRunner>();
            // MR_FLOW_REGION (optional, e.g. azora_highlands) picks the map pin; its frames go to
            // good_graphics/flow_<region> so the default Sakura set is never overwritten.
            string region = System.Environment.GetEnvironmentVariable("MR_FLOW_REGION");
            string dir = string.IsNullOrWhiteSpace(region) ? OutDir : OutDir + "_" + region.Trim();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, dir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            MapleRideTitleScreen.forceInBatchMode = false;
            EditorApplication.update -= Poll;

            // Play mode mutates region visibility; leaving that in the scene is exactly the
            // defect that produced the empty map in the first place.
            SakuraSceneDefaults.Fix();
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[flow-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(MapleRideFlowRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[flow-play] TIMED OUT waiting for the runner.");
            MapleRideFlowRunner.Failed = true;
            MapleRideFlowRunner.Finished = true;
        }
        if (!MapleRideFlowRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

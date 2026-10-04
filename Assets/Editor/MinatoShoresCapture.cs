using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// SHORES workstream gameplay capture. Same play-mode runner as <see cref="MinatoPlaymodeCapture"/>
/// (real chase camera, real HUD, real traffic), but with stations covering the Shores zones -
/// Seawall Sprint, Marina Ribbon, Stormglass Causeway, the far-shore landfall and the headland
/// climb up to the panoramic finish at ~19 km, which the shared capture has no station for -
/// and written to its own folder so parallel workstreams never overwrite each other's frames.
///
/// Run headless WITHOUT -quit:  -executeMethod MinatoShoresCapture.Run
/// Frames: reference/good_graphics/minato_shores/
/// </summary>
[InitializeOnLoad]
public static class MinatoShoresCapture
{
    private const string PrefKey = "mapleride.minato.shorescapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/minato_shores";
    private const double TimeoutSeconds = 1200.0;

    /// <summary>Chase-camera stations (route metres). PROVISIONAL.</summary>
    private static readonly float[] Stations =
    {
        1950f, 2250f, 2500f, 2800f, 3150f, 3450f, 4300f, 5600f, 7200f,
        10500f, 11800f, 13600f, 15300f, 17000f, 18600f, 18960f,
    };

    /// <summary>Elevated HUD-free audit + low contact frames. PROVISIONAL.</summary>
    private static readonly float[] Audits = { 2250f, 3000f, 5000f, 11000f, 16800f, 18900f };

    private static double _deadline;

    static MinatoShoresCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[shores-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~MinatoShoresRunner");
            var runner = go.AddComponent<MinatoPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.stations = Stations;
            runner.auditStations = Audits;
            runner.burstFrames = 0;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[shores-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(MinatoPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[shores-play] TIMED OUT waiting for the runner.");
            MinatoPlaymodeRunner.Failed = true;
            MinatoPlaymodeRunner.Finished = true;
        }
        if (!MinatoPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

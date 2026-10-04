using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// QA DIAGNOSTIC ONLY - editor side of <see cref="MinatoIdleStartRunner"/>. Enters play mode,
/// spawns the runner, waits, leaves, and in batchmode exits. Exists solely to reproduce the
/// user-reported "missing bike" condition (player idle at the route start for ~90s) which no
/// existing capture harness exercises.
///
/// Run headless WITHOUT -quit (or Unity exits before play mode starts):
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod MinatoIdleStartCapture.Run
/// </summary>
[InitializeOnLoad]
public static class MinatoIdleStartCapture
{
    private const string PrefKey = "mapleride.minato.idlestart";
    private const string FreeRollPrefKey = "mapleride.minato.idlestart.freeroll";
    private const string HideSmilePrefKey = "mapleride.minato.idlestart.hidesmile";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/minato_play";
    private const double TimeoutSeconds = 300.0;

    private static double _deadline;

    static MinatoIdleStartCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Diagnostics/QA - Minato Idle-At-Start Repro (Play Mode)", priority = 26)]
    public static void Run()
    {
        EditorPrefs.SetBool(FreeRollPrefKey, false);
        EditorPrefs.SetBool(HideSmilePrefKey, false);
        RunInternal();
    }

    /// <summary>
    /// QA fix verification mode for the trainer-telemetry finding: no road-position pin, just
    /// DeviceManager.HoldZeroPower - so a regression would show DistanceM/SpeedMps climbing
    /// unboundedly here exactly as originally reported, instead of being masked by the pin.
    /// </summary>
    [MenuItem("MapleRide/Diagnostics/QA - Minato Idle-At-Start Repro FREE-ROLL (Play Mode)",
              priority = 27)]
    public static void RunFreeRoll()
    {
        EditorPrefs.SetBool(FreeRollPrefKey, true);
        EditorPrefs.SetBool(HideSmilePrefKey, false);
        RunInternal();
    }

    /// <summary>
    /// FI DIAGNOSTIC ONLY - isolates whether the "helmet tear" finding is actually the
    /// SmileDecal quad showing through, by force-deactivating every "SmileDecal" GameObject in
    /// the spawned traffic right before every capture. Never used for real verification/regression
    /// captures - only to bisect root cause by render, per project doctrine (test one variable,
    /// look at the image, do not guess).
    /// </summary>
    [MenuItem("MapleRide/Diagnostics/FI - Minato Idle-At-Start Repro NO-SMILE (Play Mode)",
              priority = 28)]
    public static void RunHideSmile()
    {
        EditorPrefs.SetBool(FreeRollPrefKey, false);
        EditorPrefs.SetBool(HideSmilePrefKey, true);
        RunInternal();
    }

    private static void RunInternal()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[minato-idle] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~MinatoIdleStartRunner");
            var runner = go.AddComponent<MinatoIdleStartRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.freeRoll = EditorPrefs.GetBool(FreeRollPrefKey, false);
            runner.fiHideSmileDecals = EditorPrefs.GetBool(HideSmilePrefKey, false);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[minato-idle] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(MinatoIdleStartRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[minato-idle] TIMED OUT waiting for the runner.");
            MinatoIdleStartRunner.Failed = true;
            MinatoIdleStartRunner.Finished = true;
        }
        if (!MinatoIdleStartRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

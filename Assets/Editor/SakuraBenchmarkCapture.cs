using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Part B play-mode benchmark capture. Same shape as
/// <see cref="RideCameraCapture"/> and <see cref="HanakagePlaymodeCapture"/>.
///
/// Run headless WITHOUT -quit (the play-mode loop needs to actually run):
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod SakuraBenchmarkCapture.Run
///
/// Stage label comes from MR_BENCH_STAGE, matching the edit-mode SakuraBenchmark.
/// </summary>
[InitializeOnLoad]
public static class SakuraBenchmarkCapture
{
    private const string PrefKey = "mapleride.bench.playcapture";
    private const string OutDir = "../reference/good_graphics/benchmark";

    private const double TimeoutSeconds = 1200.0;
    private static double _deadline;

    static SakuraBenchmarkCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Environment/Capture Visual Benchmark (Play Mode)", priority = 66)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != SakuraSceneDefaults.ScenePath)
            EditorSceneManager.OpenScene(SakuraSceneDefaults.ScenePath, OpenSceneMode.Single);

        // A run that ends after a fast travel persists "Sakura Pass is hidden" into the scene,
        // and every capture after that quietly photographs open water. Guard every shoot.
        int fixedCount = SakuraSceneDefaults.Fix();
        if (fixedCount > 0)
            Debug.LogWarning($"[bench-play] region visibility was wrong in the saved scene; " +
                             $"corrected {fixedCount} root(s) before capturing.");

        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[bench-play] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~SakuraBenchmarkPlaymodeRunner");
            var runner = go.AddComponent<SakuraBenchmarkPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.stage = Stage();
            runner._shadowProof =
                Environment.GetEnvironmentVariable("MR_BENCH_SHADOWPROOF") == "1";
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[bench-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(SakuraBenchmarkPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static string Stage()
    {
        var s = Environment.GetEnvironmentVariable("MR_BENCH_STAGE");
        return string.IsNullOrWhiteSpace(s) ? "stage" : s.Trim();
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[bench-play] TIMED OUT waiting for the runner.");
            SakuraBenchmarkPlaymodeRunner.Failed = true;
            SakuraBenchmarkPlaymodeRunner.Finished = true;
        }

        if (!SakuraBenchmarkPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

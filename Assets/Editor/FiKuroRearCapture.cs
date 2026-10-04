using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of <see cref="FiKuroRearRunner"/> - a REAL PLAY-MODE, direct-rear capture of the
/// player on the Sakura road, used to reproduce and then verify the "Kuro clips white/black in
/// gameplay" fix. Faithful to shipped exposure because it rides down the real ride camera.
///
/// Run headless WITHOUT -quit (the play loop must actually run):
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod FiKuroRearCapture.Run
///
/// The output filename comes from MR_REAR_TAG (e.g. "before" / "after"), so before/after pairs
/// sit side by side in good_graphics/fi_rearfix.
/// </summary>
[InitializeOnLoad]
public static class FiKuroRearCapture
{
    private const string PrefKey = "mapleride.fi.rearcapture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/fi_rearfix";

    private const double TimeoutSeconds = 600.0;
    private static double _deadline;

    static FiKuroRearCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int fixedCount = SakuraSceneDefaults.Fix();
        if (fixedCount > 0)
            Debug.LogWarning($"[fi-rear] region visibility was wrong in the saved scene; " +
                             $"corrected {fixedCount} root(s) before capturing.");

        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[fi-rear] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~FiKuroRearRunner");
            var runner = go.AddComponent<FiKuroRearRunner>();
            var od = Environment.GetEnvironmentVariable("MR_REAR_OUTDIR");
            runner.outDir = string.IsNullOrWhiteSpace(od)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, OutDir))
                : Path.GetFullPath(od);
            var t = Environment.GetEnvironmentVariable("MR_REAR_TAG");
            runner.tag = string.IsNullOrWhiteSpace(t) ? "shot" : t.Trim();
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[fi-rear] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(FiKuroRearRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[fi-rear] TIMED OUT waiting for the runner.");
            FiKuroRearRunner.Failed = true;
            FiKuroRearRunner.Finished = true;
        }

        if (!FiKuroRearRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

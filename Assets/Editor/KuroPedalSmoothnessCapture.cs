using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Editor owner for the opt-in Kuro play-mode pedal smoothness benchmark.</summary>
[InitializeOnLoad]
public static class KuroPedalSmoothnessCapture
{
    private const string PrefKey = "mapleride.kuro.pedal-smoothness";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/kuro_jersey_motion";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static KuroPedalSmoothnessCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/Kuro/Measure Pedal Smoothness (Play Mode)", priority = 67)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[kuro-smooth] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~Kuro Pedal Smoothness Runner");
            var runner = go.AddComponent<KuroPedalSmoothnessRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[kuro-smooth] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(KuroPedalSmoothnessRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[kuro-smooth] TIMED OUT waiting for the runner.");
            KuroPedalSmoothnessRunner.Failed = true;
            KuroPedalSmoothnessRunner.Finished = true;
        }
        if (!KuroPedalSmoothnessRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

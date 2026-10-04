using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the AMBIENT PEDESTRIANS play test (copilot, 2026-09-26): enters play mode,
/// spawns <see cref="PedestrianPlaymodeRunner"/>, waits, leaves, and in batchmode exits.
///   run_steps.ps1 "PedestrianCapture.Run|copilot_peds_play.log|0"         (checks + after frames)
///   run_steps.ps1 "PedestrianCapture.RunBefore|copilot_peds_before.log|0" (director disabled)
/// Frames: reference/good_graphics/pedestrians/.
/// </summary>
[InitializeOnLoad]
public static class PedestrianCapture
{
    const string PrefKey = "mapleride.pedestrians.playcapture";
    const string BeforeKey = "mapleride.pedestrians.before";
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string OutDir = "../reference/good_graphics/pedestrians";
    const double TimeoutSeconds = 1200.0;
    static double _deadline;

    static PedestrianCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (EditorPrefs.GetBool(ResetKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorPrefs.SetBool(ResetKey, false); PlayerPrefs.SetInt(PedestrianDirector.DisabledPref, 0); PlayerPrefs.Save(); }
    }

    [MenuItem("MapleRide/Diagnostics/Play-test ambient pedestrians", priority = 28)]
    public static void Run() => Go(false);

    [MenuItem("MapleRide/Diagnostics/Play-test ambient pedestrians (BEFORE: director off)", priority = 29)]
    public static void RunBefore() => Go(true);

    /// <summary>Quick perf A/B only (no checks/captures).</summary>
    public static void RunPerf() { EditorPrefs.SetBool(PerfKey, true); Go(false); }
    const string PerfKey = "mapleride.pedestrians.perfonly";

    /// <summary>A/B perf baseline: MinatoPlaymodeCapture.RunCrowdValidation with the director off.</summary>
    public static void RunMinatoCrowdBaseline()
    {
        PlayerPrefs.SetInt(PedestrianDirector.DisabledPref, 1);
        PlayerPrefs.Save();
        EditorPrefs.SetBool(ResetKey, true);
        MinatoPlaymodeCapture.RunCrowdValidation();
    }
    const string ResetKey = "mapleride.pedestrians.resetdisable";

    static void Go(bool before)
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        EditorPrefs.SetBool(BeforeKey, before);
        PlayerPrefs.SetInt(PedestrianDirector.DisabledPref, before ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[peds-play] entering play mode ({(before ? "BEFORE: director disabled" : "after")})...");
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if ((change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode) && EditorPrefs.GetBool(ResetKey, false))
        {
            EditorPrefs.SetBool(ResetKey, false);
            PlayerPrefs.SetInt(PedestrianDirector.DisabledPref, 0);
            PlayerPrefs.Save();
        }
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~PedestrianRunner").AddComponent<PedestrianPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.beforeMode = EditorPrefs.GetBool(BeforeKey, false);
            runner.perfOnly = EditorPrefs.GetBool(PerfKey, false);
            EditorPrefs.SetBool(PerfKey, false);
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorPrefs.SetBool(BeforeKey, false);
            PlayerPrefs.SetInt(PedestrianDirector.DisabledPref, 0);
            PlayerPrefs.Save();
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[peds-play] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(PedestrianPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[peds-play] TIMED OUT waiting for the runner.");
            PedestrianPlaymodeRunner.Failed = true;
            PedestrianPlaymodeRunner.Finished = true;
        }
        if (!PedestrianPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

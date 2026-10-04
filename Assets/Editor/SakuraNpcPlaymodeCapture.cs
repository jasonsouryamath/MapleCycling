using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor side of the Sakura Pass N1 NPC play test (claude 2026-09-30; copy of
/// TakaNpcPlaymodeCapture): enters play mode on the shared scene, spawns
/// <see cref="SakuraNpcPlaymodeRunner"/> with SakuraNpcRoster's moving/stopped names, waits for
/// it, leaves play mode, DISCARDS scene changes and (batchmode) exits with the result.
/// Read-only for the scene. Run WITHOUT -quit (field 0):
///   run_steps.ps1 "SakuraNpcPlaymodeCapture.Run|claude_npc1_play.log|0"
/// Frames: reference/good_graphics/sakura_npc/*.png
/// </summary>
[InitializeOnLoad]
public static class SakuraNpcPlaymodeCapture
{
    private const string PrefKey = "mapleride.sakura.npc1capture";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string OutDir = "../reference/good_graphics/sakura_npc";
    private const double TimeoutSeconds = 900.0;
    private static double _deadline;

    static SakuraNpcPlaymodeCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("MapleRide/QA/Play-test Sakura N1 NPCs", priority = 64)]
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorPrefs.SetBool(PrefKey, true);
        Debug.Log("[sakura-npc] entering play mode...");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!EditorPrefs.GetBool(PrefKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var runner = new GameObject("~SakuraNpcPlaymodeRunner").AddComponent<SakuraNpcPlaymodeRunner>();
            runner.outDir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
            runner.riderNames = SakuraNpcRoster.MovingCyclistNames;
            runner.stoppedNames = SakuraNpcRoster.StoppedCyclistNames;
            _deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.SetBool(PrefKey, false);
            EditorApplication.update -= Poll;
            MapleRideSceneBootstrap.DiscardChanges();
            Debug.Log("[sakura-npc] left play mode.");
            if (Application.isBatchMode)
                EditorApplication.Exit(SakuraNpcPlaymodeRunner.Failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup > _deadline)
        {
            Debug.LogError("[sakura-npc] TIMED OUT waiting for the runner.");
            SakuraNpcPlaymodeRunner.Failed = true;
            SakuraNpcPlaymodeRunner.Finished = true;
        }
        if (!SakuraNpcPlaymodeRunner.Finished) return;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}

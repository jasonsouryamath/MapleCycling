using UnityEditor;
using UnityEngine;

/// <summary>Claude 2026-10-02: confirms the streamed cells are registered in build settings.
/// run_steps.ps1 "NagisaStreamingCheck.Run|claude_stream_check.log|1"</summary>
public static class NagisaStreamingCheck
{
    public static void Run()
    {
        const string cell = "Assets/Scenes/Playable/Cells/nagisa_bay/20261002_055806_653/cell_-10_-127.unity";
        Debug.Log($"[stream-check] build settings scenes: {EditorBuildSettings.scenes.Length}; " +
                  $"CanStreamedLevelBeLoaded(failing cell) = {Application.CanStreamedLevelBeLoaded(cell)}; " +
                  "nagisa base = " + Application.CanStreamedLevelBeLoaded(MapleRideBoot.RegionScenePath("nagisa_bay")));
    }
}

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// One-off harness for verifying the SakuraGradeCustomPass fix (HDRP re-host of SakuraPostFX)
/// across all 7 regions with a controlled A/B: toggle the pass off/on, capture, compare.
/// Not part of the shipped fix - safe to delete once QA has signed off.
/// </summary>
public static class FiSakuraGradeCaptureAll
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string VolumeName = "Sakura Grade Custom Pass Volume";

    [MenuItem("MapleRide/Diagnostics/FI Capture All 7 Regions")]
    public static void CaptureAll()
    {
        void Try(string label, Action a)
        {
            try { a(); Debug.Log($"[fi-grade-all] {label} capture OK"); }
            catch (Exception e) { Debug.LogError($"[fi-grade-all] {label} capture FAILED: {e}"); }
        }

        Try("Sakura", SakuraPassDiagnostics.Capture);
        Try("Shiosai", ShiosaiCoastDiagnostics.Capture);
        // MinatoCoastDiagnostics.Capture() does GameObject.Find("Minato Coast Environment")
        // BEFORE it switches region visibility, and GameObject.Find never finds an inactive
        // object - so if a different region was last active (e.g. Sakura, from the lighting
        // pass), the root is inactive and the capture silently no-ops with just a LogError, no
        // exception. Pre-existing ordering issue in that file, unrelated to the grading fix;
        // worked around here (verification harness only) by making Minato's root active first.
        Try("Minato", () =>
        {
            var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            if (regions != null)
            {
                regions.Resolve();
                regions.currentRegionId = RegionCatalog.MinatoCoast;
                regions.ApplyEnvironmentVisibility();
            }
            MinatoCoastDiagnostics.Capture();
        });
        Try("Taka", TakaMountainsDiagnostics.Capture);
        Try("Fuji", FujiRidgeDiagnostics.Capture);
        Try("MapleCity", MapleCityDiagnostics.Capture);
        Try("Azora", AzoraHighlandsDiagnostics.Capture);
    }

    private static void SetGradeEnabled(bool enabled)
    {
        int found = 0;
        foreach (var v in UnityEngine.Object.FindObjectsByType<CustomPassVolume>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (v == null || v.name != VolumeName) continue;
            foreach (var p in v.customPasses)
            {
                if (p is SakuraGradeCustomPass) { p.enabled = enabled; found++; }
            }
        }
        Debug.Log($"[fi-grade-all] SakuraGradeCustomPass.enabled = {enabled} on {found} instance(s).");
    }

    private static void OpenSceneIfNeeded()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void SaveScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("MapleRide/Diagnostics/FI Disable Sakura Grade (BEFORE state)")]
    public static void DisableGrade()
    {
        OpenSceneIfNeeded();
        SetGradeEnabled(false);
        SaveScene();
        Debug.Log("[fi-grade-all] grade DISABLED and scene saved (BEFORE state).");
    }

    [MenuItem("MapleRide/Diagnostics/FI Enable Sakura Grade (AFTER state)")]
    public static void EnableGrade()
    {
        OpenSceneIfNeeded();
        SetGradeEnabled(true);
        SaveScene();
        Debug.Log("[fi-grade-all] grade ENABLED and scene saved (AFTER / final state).");
    }
}

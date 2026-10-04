using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// NB-QOL review cameras for the Nagisa Bay wayfinding boards (NagisaBayEnvironment.Wayfinding.cs).
/// Rider-eye frames ~30 m before a few boards, same convention as NagisaBayOpeningDiagnostics:
/// region switched visible first, previous region restored in finally, scene never saved.
/// Output: reference/good_graphics/nagisa_bay/diag_nagisa_qol_*.png
/// </summary>
public static class NagisaWayfindingDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa Bay Wayfinding")]
    public static void Capture()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = NagisaBayEnvironment.RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        var r = NagisaBayEnvironment.NagisaRoute.Load();
        Directory.CreateDirectory(NagisaBayDiagnostics.OutDir);
        try
        {
            // (file, board metres, eye metres before it)
            var shots = new (string, float, float)[]
            {
                ("diag_nagisa_qol_01_welcome.png", 25f, 28f),
                ("diag_nagisa_qol_02_climb.png", Mathf.Max(80f, r.ClimbStartM - 120f), 30f),
                ("diag_nagisa_qol_03_kom500.png", r.KomM - 500f, 28f),
                ("diag_nagisa_qol_04_fin200_bridge.png", r.Length - 200f, 28f),
            };
            foreach (var (file, board, before) in shots)
            {
                int i = r.IndexAt(Mathf.Max(0f, board - before));
                int j = r.IndexAt(board);
                var sd = r.SideFlat(j) * NagisaBayEnvironment.DiagSeaSign(j);
                var eye = r.Position[i] + Vector3.up * 1.6f;
                var target = r.Position[j] + sd * 5.2f + Vector3.up * 2.0f;
                NagisaBayDiagnostics.Shot(eye, target, 50f, file);
            }
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previous))
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
        Debug.Log("[nagisa-qol] captured wayfinding frames.");
    }
}

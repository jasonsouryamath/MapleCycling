using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Nagisa Bay OPENING STRETCH review cameras (route 0 .. BeachStartM, the marina-front /
/// waterfront-town run before the resort beach). Same convention as NagisaBayDiagnostics.Capture:
/// region switched visible first, previous region restored in finally, scene never saved.
/// Also logs a seaward profile probe ("[nagisa-open] probe ...") so the shoreline offset can be
/// checked against the renders.
/// Output: reference/good_graphics/nagisa_bay/diag_nagisa_open_*.png
/// </summary>
public static class NagisaBayOpeningDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa Bay Opening Stretch")]
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
        var g = NagisaBayEnvironment.NagisaGround.Load();
        Directory.CreateDirectory(NagisaBayDiagnostics.OutDir);
        try
        {
            for (float d = 0f; d <= r.BeachStartM + 100f; d += 100f)
            {
                int i = r.IndexAt(d);
                float s = NagisaBayEnvironment.DiagSeaSign(i);
                var sd = r.SideFlat(i) * s;
                float shore = -1f;
                for (float o = 4f; o < 400f; o += 1f)
                {
                    var q = r.Position[i] + sd * o;
                    if (g.Coast(q.x, q.z) <= 0f) { shore = o; break; }
                }
                var q20 = r.Position[i] + sd * 20f;
                Debug.Log($"[nagisa-open] probe d={d:0} seaSign={s:+0;-0} shore@{shore:0.0}m " +
                          $"cls20={g.ClassAt(q20.x, q20.z)} h20={g.Height(q20.x, q20.z):0.00}");
            }

            // (file, metres, right(+ = sea side) offset, up, look-ahead, fov, look sideways toward sea)
            var shots = new (string, float, float, float, float, float, float)[]
            {
                ("diag_nagisa_open_01_start_chase.png", 40f, 0f, 1.7f, 90f, 60f, 0.25f),
                ("diag_nagisa_open_02_chase_500.png", 500f, 0f, 1.7f, 90f, 60f, 0.25f),
                ("diag_nagisa_open_03_chase_1200.png", 1200f, 0f, 1.7f, 90f, 60f, 0.25f),
                ("diag_nagisa_open_04_oblique_800.png", 700f, 6f, 14f, 60f, 60f, 0.45f),
            };
            foreach (var (file, d, right, up, ahead, fov, seaLook) in shots)
            {
                int i = r.IndexAt(d);
                var sd = r.SideFlat(i) * NagisaBayEnvironment.DiagSeaSign(i);
                var eye = r.Position[i] + sd * right + Vector3.up * up;
                int j = r.IndexAt(Mathf.Clamp(d + ahead, 0f, r.Length));
                var target = r.Position[j] + sd * (ahead * seaLook) + Vector3.up * 1.2f;
                NagisaBayDiagnostics.Shot(eye, target, fov, file);
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
        Debug.Log("[nagisa-open] captured opening-stretch frames.");
    }
}

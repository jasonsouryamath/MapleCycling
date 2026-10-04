// NB2 pass 3 diagnostics: frames of the inland resort row / frontage / hero (reads the lot list that the
// ResortRow stage writes). Run: run_steps.ps1 "NagisaBayNb2Diagnostics.Capture|claude_nb2_cap.log|0"
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NagisaBayNb2Diagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string LotFile = "reference/good_graphics/nagisa_bay/nb2_lots.txt";

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa NB2 Resort Row")]
    public static void Capture()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = NagisaBayEnvironment.RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        var lots = new List<(string stem, Vector3 p, float yaw, float d)>();
        if (File.Exists(LotFile))
            foreach (var line in File.ReadAllLines(LotFile))
            {
                var c = line.Split(',');
                if (c.Length < 6) continue;
                float F(int k) => float.Parse(c[k], System.Globalization.CultureInfo.InvariantCulture);
                lots.Add((c[0], new Vector3(F(1), F(2), F(3)), F(4), F(5)));
            }
        var r = NagisaBayEnvironment.NagisaRoute.Load();
        int shots = 0;
        try
        {
            string only = System.Environment.GetEnvironmentVariable("MR_NB2_SHOTS");
            for (int n = 0; n < lots.Count && n < 4; n++)
            {
                var lot = lots[n];
                int i = r.IndexAt(lot.d);
                var tan = r.Tangent[i]; tan.y = 0f; tan.Normalize();
                var sea = Quaternion.Euler(0f, lot.yaw, 0f) * Vector3.back;    // local -z = sea
                var inland = -sea;
                // (a) from the ride road, looking inland across the highway at the building
                var eye = r.Position[i] - tan * 90f + Vector3.up * 1.7f;
                Shot(eye, lot.p + inland * 0f + Vector3.up * 22f, 60f, $"diag_nagisa_nb2_{n}_{ShortName(lot.stem)}_road.png"); shots++;
                // (b) from the frontage sidewalk, close, facing the building front
                var f2 = r.Position[i] + inland * 53f + Vector3.up * 1.7f - tan * 38f;
                Shot(f2, lot.p + Vector3.up * 14f, 62f, $"diag_nagisa_nb2_{n}_{ShortName(lot.stem)}_front.png"); shots++;
            }
            if (lots.Count > 0)
            {
                var a = lots[0].p;
                var sea = Quaternion.Euler(0f, lots[0].yaw, 0f) * Vector3.back;
                Shot(a + sea * 260f + Vector3.up * 190f, a + Vector3.up * 10f, 55f, "diag_nagisa_nb2_strip_aerial.png"); shots++;
                if (lots.Count > 3)
                {
                    var b = lots[3].p;
                    Shot(b + sea * 190f + Vector3.up * 70f, b + Vector3.up * 14f, 55f, "diag_nagisa_nb2_beach_view.png"); shots++;
                }
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
        Debug.Log($"[nagisa-nb2] captured {shots} frames; lots read: {lots.Count}");
    }

    private static string ShortName(string stem) => stem.Replace("Nagisa_B_", "");

    private static void Shot(Vector3 eye, Vector3 target, float fov, string file) =>
        NagisaBayDiagnostics.Shot(eye, target, fov, file);
}

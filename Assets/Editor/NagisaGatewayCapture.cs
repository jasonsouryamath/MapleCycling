using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Worker E review frames: gateway, ocean-reveal screen, Skyline Terrace, signature bridge.
/// Output reference/good_graphics/nagisa_bay/overhaul/E_*.png. Run after NagisaBayEnvironment.Apply in the same batch.</summary>
public static class NagisaGatewayCapture
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    public static void Capture()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = NagisaBayEnvironment.RegionId;
            regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience();
        }
        var r = NagisaBayEnvironment.NagisaRoute.Load();
        string dir = NagisaBayDiagnostics.OutDir + "/overhaul";
        Directory.CreateDirectory(dir);
        try
        {
            string only = System.Environment.GetEnvironmentVariable("MR_E_SHOTS");
            bool want(string n) => string.IsNullOrEmpty(only) || only.Contains(n);
            Vector3 P(float m, float right, float up) { int i = r.IndexAt(m); return r.Position[i] + r.SideFlat(i) * right + Vector3.up * up; }
            void Shot(string n, Vector3 eye, Vector3 tgt, float fov)
            { if (want(n)) NagisaBayDiagnostics.Shot(eye, tgt, fov, "overhaul/E_" + n + ".png"); }
            float sea(float m) => NagisaBayEnvironment.DiagSeaSign(r.IndexAt(m));
            float bay = r.SideFlat(r.IndexAt(8200f)).z > 0f ? -1f : 1f;

            Shot("01_start_gate", P(4f, 0f, 1.7f), P(160f, 0f, 5f), 62f);
            Shot("02_gate_close", P(100f, 0f, 1.7f), P(150f, 0f, 7.5f), 62f);
            Shot("03_pine_screen", P(300f, 0f, 1.7f), P(380f, 0f, 2f) + r.SideFlat(r.IndexAt(300f)) * sea(300f) * 50f, 62f);
            Shot("04_reveal_bend", P(445f, 0f, 1.7f), P(500f, 0f, 1.5f) + r.SideFlat(r.IndexAt(445f)) * sea(445f) * 120f, 70f);
            Shot("05_gate_aerial", P(60f, 0f, 24f), P(150f, 0f, 6f), 60f);
            Shot("06_overlook_approach", P(8020f, 0f, 1.7f), P(8160f, 0f, 1.5f), 62f);
            Shot("07_terrace_road", P(8150f, 0f, 1.7f), P(8230f, 0f, 1.5f) + r.SideFlat(r.IndexAt(8150f)) * bay * 150f, 75f);
            Shot("08_deck_view", P(8200f, bay * 30f, 1.7f), P(8200f, bay * 600f, -60f), 80f);
            Shot("09_terrace_aerial", P(8200f, bay * 150f, 38f), P(8200f, bay * 30f, 3f), 60f);
            Shot("10_kiosk_close", P(8095f, 0f, 1.7f), P(8118f, bay * 12f, 1.8f), 60f);
            Shot("11_bridge_approach", P(15250f, 0f, 2.2f), P(15560f, 0f, 20f), 62f);
            Shot("12_bridge_pylon", P(15380f, 0f, 2.2f), P(15450f, 0f, 22f), 62f);
            Shot("13_bridge_aerial", P(15450f, 160f, 60f), P(15450f, 0f, 20f), 60f);
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previous))
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience();
            }
        }
    }
}

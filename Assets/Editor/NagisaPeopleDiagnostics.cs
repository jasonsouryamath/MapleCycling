using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Close-up review cameras for the Nagisa Bay people + verge grass overhaul (Claude, 2026-10-01).
/// Run: run_steps.ps1 "NagisaPeopleDiagnostics.Capture|claude_nb_people_cap.log|1"
/// Output: reference/good_graphics/nagisa_bay/people_*.png
/// </summary>
public static class NagisaPeopleDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    static void Dump(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { sb.Append(" [null]"); continue; }
                sb.Append($" [{m.name} / {m.shader.name}");
                foreach (var pn in m.GetTexturePropertyNames())
                {
                    var tx = m.GetTexture(pn);
                    if (tx != null) sb.Append($" {pn}={tx.name}");
                }
                sb.Append("]");
            }
            Debug.Log($"[nagisa-people-dump] {r.name} ({r.GetType().Name}, enabled={r.enabled}, mesh={(r is SkinnedMeshRenderer s ? (s.sharedMesh != null ? s.sharedMesh.name : "null") : "-")}){sb}");
        }
    }

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa People + Grass")]
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
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        Directory.CreateDirectory(NagisaBayDiagnostics.OutDir);
        try
        {
            var root = GameObject.Find(NagisaBayEnvironment.RootName);
            if (root == null) { Debug.LogError("[nagisa-people] environment root missing"); return; }
            var all = root.GetComponentsInChildren<Transform>(false);
            int shots = 0;
            foreach (var key in new[] { "Sunbather", "Runner", "Walker", "Chat" })
            {
                var t = all.FirstOrDefault(x => x.name.Contains(key) && x.GetComponent<LODGroup>() != null);
                if (t == null) { Debug.Log($"[nagisa-people] no '{key}' person found"); continue; }
                var c = t.position + Vector3.up * 0.9f;
                var eye = c + (t.forward * 2.4f + t.right * 0.8f) + Vector3.up * 0.3f;
                NagisaBayDiagnostics.Shot(eye, c, 38f, $"people_{key.ToLower()}_front.png"); shots++;
                eye = c - t.forward * 2.6f + t.right * 1.2f + Vector3.up * 0.4f;
                NagisaBayDiagnostics.Shot(eye, c, 38f, $"people_{key.ToLower()}_back.png"); shots++;
                Debug.Log($"[nagisa-people] {key}: {t.name} at {t.position}");
                Dump(t.gameObject);
            }
            // lying sunbather, side-on
            var sb = all.FirstOrDefault(x => x.name == "Sunbather Pose");
            if (sb != null)
            {
                var c2 = sb.position + Vector3.up * 0.2f;
                NagisaBayDiagnostics.Shot(c2 + sb.right * 3.2f + Vector3.up * 1.4f, c2, 34f, "people_sunbather_side.png"); shots++;
                NagisaBayDiagnostics.Shot(c2 - sb.up * 0f + Vector3.up * 3.2f + sb.right * 0.8f, c2, 30f, "people_sunbather_top.png"); shots++;
            }
            // verge grass at rider height, beside the road near the hotel
            var r = NagisaBayEnvironment.NagisaRoute.Load();
            foreach (var (d, tag) in new[] { (r.HotelM - 700f, "a"), (r.KomM - 1500f, "b") })
            {
                int i = r.IndexAt(d);
                var side = r.SideFlat(i);
                var eye = r.Position[i] + side * 3.0f + Vector3.up * 1.4f;
                var tgt = r.Position[r.IndexAt(d + 30f)] + side * 6.5f + Vector3.up * 0.2f;
                NagisaBayDiagnostics.Shot(eye, tgt, 55f, $"people_grass_{tag}.png"); shots++;
            }
            Debug.Log($"[nagisa-people] captured {shots} frames");
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
    }
}

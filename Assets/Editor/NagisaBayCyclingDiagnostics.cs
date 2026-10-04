// Worker F (cycling culture / boulevard / promenade / town centre) frame capture.
// Run: $env:MR_NBC_SHOTS="road_beach,service,club"; run_steps.ps1 "NagisaBayCyclingDiagnostics.Capture|claude_f_cap.log|0"
// Ids: road_beach, road_east, road_early, service, club, plaza, hotel, overlook, art, lane. (Keep to <= 3 per round.)
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NagisaBayCyclingDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    private static Transform Find(Transform root, string path) => root == null ? null : root.Find(path);

    private static Transform FirstNamed(Transform under, string name)
    {
        if (under == null) return null;
        foreach (var t in under.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa Cycling Culture")]
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
        var want = new HashSet<string>((System.Environment.GetEnvironmentVariable("MR_NBC_SHOTS") ?? "road_beach,service,club")
            .Split(new[] { ',', ';', ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
        var r = NagisaBayEnvironment.NagisaRoute.Load();
        GameObject rootGo = null;
        foreach (var go in scene.GetRootGameObjects()) if (go.name == NagisaBayEnvironment.RootName) rootGo = go;
        var ov = rootGo != null ? rootGo.transform.Find("NB Overhaul") : null;
        int shots = 0;
        try
        {
            void Road(string id, float d, float back = 0f)
            {
                if (!want.Contains(id)) return;
                int i = r.IndexAt(d);
                var tan = r.Tangent[i]; tan.y = 0f; tan.Normalize();
                var eye = r.Position[i] + Vector3.up * 1.7f - tan * back;
                Shot(eye, r.Position[r.IndexAt(d + 60f)] + Vector3.up * 1.6f, 66f, $"diag_nbc_{id}.png"); shots++;
            }
            Road("road_beach", 2700f);
            Road("road_early", 1200f);
            Road("road_east", 13000f);
            Road("lane", 3300f);

            void Look(string id, Transform t, Vector3 back, float up, float tgtUp)
            {
                if (!want.Contains(id) || t == null) { if (want.Contains(id)) Debug.LogWarning($"[nbc-diag] no target for {id}"); return; }
                var tp = t.position;
                Shot(tp + back + Vector3.up * up, tp + Vector3.up * tgtUp, 58f, $"diag_nbc_{id}.png"); shots++;
            }
            var svc = Find(ov, "NB CyclingCulture/Cycling service points");
            var corral = svc != null && svc.childCount > 0 ? svc.GetChild(0) : null;
            if (corral != null)
            {
                // from the road side, looking across the promenade
                var toRoad = -(corral.forward);
                Look("service", corral, toRoad.normalized * 12f + corral.right * 4f, 2.4f, 1.0f);
            }
            var cafe = FirstNamed(Find(ov, "NB CyclingCulture/Cycling club and cafe"), "Nagisa_NBC_CycleCafe");
            if (cafe != null) Look("club", cafe, cafe.forward * 30f + cafe.right * 6f, 4.5f, 3.0f);
            var plaza = Find(ov, "NB Promenade/Shiokaze Plaza/Shops");
            if (plaza != null && plaza.childCount > 0)
            {
                var s0 = plaza.GetChild(0);
                Look("plaza", s0, s0.forward * -30f + s0.right * 12f, 7f, 2.5f);   // shop fronts face local -z
            }
            var hot = Find(ov, "NB CyclingCulture/Hotel cycling signage");
            if (hot != null && hot.childCount > 0)
            {
                var h0 = hot.GetChild(0);
                Look("hotel", h0, h0.forward * 16f + h0.right * 5f, 3.0f, 2.0f);
            }
            var deck = FirstNamed(Find(ov, "NB Promenade/Ocean overlooks"), "Nagisa_NBC_Overlook");
            if (deck != null) Look("overlook", deck, -deck.forward * 14f + deck.right * 5f, 4.0f, 0.8f);
            var art = Find(ov, "NB Promenade/Public art");
            if (art != null && art.childCount > 0)
            {
                var a0 = art.GetChild(0);
                var pd = r.PlanDistance(a0.position.x, a0.position.z, out int ni);
                var toRoad = (r.Position[ni] - a0.position); toRoad.y = 0f;
                Look("art", a0, toRoad.normalized * 14f, 3.0f, 1.8f);
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
        Debug.Log($"[nbc-diag] captured {shots} frames ({string.Join(",", want)})");
    }

    private static void Shot(Vector3 eye, Vector3 target, float fov, string file) =>
        NagisaBayDiagnostics.Shot(eye, target, fov, file);
}

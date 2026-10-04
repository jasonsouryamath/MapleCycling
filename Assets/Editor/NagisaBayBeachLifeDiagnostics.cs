// NB6 (beach activities) verification frames - copilot (maplerider-builder).
//
// Rider-eye frames looking to the RIGHT of travel (the side the user reported as "desolate") at
// several points along the coastal sections, one matching LEFT frame for comparison, and one wide
// oblique over the NB6 beach. Output prefix is set by env var MR_NB6_TAG ("before"/"after").
//
// Output: reference/good_graphics/nagisa_bay/overhaul/nb6_<tag>_<shot>.png
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NagisaBayBeachLifeDiagnostics
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    public const string OutDir = "reference/good_graphics/nagisa_bay/overhaul";

    /// <summary>Route metres of the rider-eye frames (PROVISIONAL shot list).</summary>
    public static readonly float[] ShotMetres = { 600f, 1500f, 2500f, 3500f, 4200f, 13000f };

    [MenuItem("MapleRide/Diagnostics/Capture Nagisa Bay NB6 Beach")]
    public static void Capture()
    {
        string tag = System.Environment.GetEnvironmentVariable("MR_NB6_TAG");
        if (string.IsNullOrEmpty(tag)) tag = "after";

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
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
        Directory.CreateDirectory(OutDir);
        var r = NagisaBayEnvironment.NagisaRoute.Load();
        int shots = 0;
        try
        {
            foreach (float d in ShotMetres)
            {
                int i = r.IndexAt(d);
                var t = r.Tangent[i]; t.y = 0f; t.Normalize();
                var right = r.SideFlat(i);
                float sea = NagisaBayEnvironment.DiagSeaSign(i);
                var eye = r.Position[i] + right * 1.7f + Vector3.up * 1.55f;
                var look = (t * 0.75f + right * 0.66f).normalized;
                NagisaBayDiagnostics.Shot(eye, eye + look * 60f - Vector3.up * 3f, 62f,
                    $"overhaul/nb6_{tag}_right_{d:0000}.png");
                Debug.Log($"[nb6] shot right_{d:0000}: sea is on the {(sea > 0f ? "RIGHT" : "LEFT")} of travel.");
                shots++;
            }
            {
                // one left-looking comparison frame on the beach section
                int i = r.IndexAt(2500f);
                var t = r.Tangent[i]; t.y = 0f; t.Normalize();
                var right = r.SideFlat(i);
                var eye = r.Position[i] - right * 1.7f + Vector3.up * 1.55f;
                var look = (t * 0.75f - right * 0.66f).normalized;
                NagisaBayDiagnostics.Shot(eye, eye + look * 60f - Vector3.up * 3f, 62f, $"overhaul/nb6_{tag}_left_2500.png");
                shots++;
            }
            {
                // wide oblique: 45 m up, behind-left of the beach centre, looking over the right side
                float d = NagisaBayEnvironment.DiagBeachLifeCentreM(r);
                int i = r.IndexAt(d);
                var t = r.Tangent[i]; t.y = 0f; t.Normalize();
                var right = r.SideFlat(i);
                var eye = r.Position[i] - t * 70f - right * 25f + Vector3.up * 38f;
                var tgt = r.Position[i] + t * 30f + right * 40f;
                NagisaBayDiagnostics.Shot(eye, tgt, 55f, $"overhaul/nb6_{tag}_wide.png");
                // ground-level along the boardwalk / beach
                var eye2 = r.Position[i] + right * 22f - t * 20f + Vector3.up * 1.8f;
                var tgt2 = r.Position[i] + right * 26f + t * 40f + Vector3.up * 1.2f;
                NagisaBayDiagnostics.Shot(eye2, tgt2, 58f, $"overhaul/nb6_{tag}_beach_eye.png");
                shots += 2;
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
        Debug.Log($"[nb6] captured {shots} frames ({tag}) to {OutDir}");
    }

    /// <summary>Headless motion + keep-out check: every NB6 driver must move its items between
    /// t=1 s and t=4 s, and no animated figure may come within the road corridor. Exit 0/1.</summary>
    public static void SelfTest()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var r = NagisaBayEnvironment.NagisaRoute.Load();
        int fails = 0, drivers = 0, figures = 0;
        float minRoad = float.MaxValue;
        var counts = new Dictionary<NagisaBeachActivity.Kind, int>();
        foreach (var a in Object.FindObjectsByType<NagisaBeachActivity>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            drivers++;
            counts[a.kind] = (counts.TryGetValue(a.kind, out int c) ? c : 0) + a.items.Length;
            var saved = new Vector3[a.items.Length];
            for (int i = 0; i < a.items.Length; i++) saved[i] = a.items[i] != null ? a.items[i].position : Vector3.zero;
            Vector3 ballSaved = a.ball != null ? a.ball.position : Vector3.zero;
            a.Step(1f);
            var p1 = new Vector3[a.items.Length];
            for (int i = 0; i < a.items.Length; i++) p1[i] = a.items[i] != null ? a.items[i].position : Vector3.zero;
            Vector3 b1 = a.ball != null ? a.ball.position : Vector3.zero;
            float moved = 0f;
            for (float t = 1.5f; t <= 12f; t += 0.5f)
            {
                a.Step(t);
                for (int i = 0; i < a.items.Length; i++)
                {
                    if (a.items[i] == null) continue;
                    moved = Mathf.Max(moved, Vector3.Distance(a.items[i].position, p1[i]));
                    if (a.kind != NagisaBeachActivity.Kind.Swash)
                    {
                        var q = a.items[i].position;
                        float d = r.PlanDistance(q.x, q.z, out _);
                        minRoad = Mathf.Min(minRoad, d);
                        if (d < NagisaBayEnvironment.CorridorKeepOutM) { fails++; Debug.LogError($"[nb6] FAIL {a.name} item {i} {d:0.0} m from the road"); }
                    }
                }
                if (a.ball != null) moved = Mathf.Max(moved, Vector3.Distance(a.ball.position, b1));
            }
            if (moved < 0.3f) { fails++; Debug.LogError($"[nb6] FAIL {a.name} ({a.kind}) did not move ({moved:0.00} m)"); }
            for (int i = 0; i < a.items.Length; i++) if (a.items[i] != null) a.items[i].position = saved[i];
            if (a.ball != null) a.ball.position = ballSaved;
            figures += a.items.Length;
        }
        if (drivers == 0) { fails++; Debug.LogError("[nb6] FAIL no NagisaBeachActivity drivers in the scene"); }
        var parts = new List<string>();
        foreach (var kv in counts) parts.Add($"{kv.Key}={kv.Value}");
        Debug.Log($"[nb6] selftest: {drivers} drivers, {figures} items ({string.Join(", ", parts)}), " +
                  $"closest animated figure {minRoad:0.0} m from the centreline (keep-out {NagisaBayEnvironment.CorridorKeepOutM:0.0} m), fails={fails}");
        Debug.Log(fails == 0 ? "[nb6] SELFTEST PASS" : "[nb6] SELFTEST FAIL");
        if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}

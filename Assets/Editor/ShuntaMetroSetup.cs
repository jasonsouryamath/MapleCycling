using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Shunta Metro editor tooling. Batch entry points (use with tools/unity/run_steps.ps1):
///   ShuntaMetroSetup.BuildScene  - creates Assets/Scenes/ShuntaMetro.unity with the route builder
///   ShuntaMetroSetup.Validate    - checks the JSON against the design numbers (no scene needed)
/// </summary>
public static class ShuntaMetroSetup
{
    public const string ScenePath = "Assets/Scenes/ShuntaMetro.unity";

    [MenuItem("MapleRide/Shunta Metro/Build Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var root = new GameObject("Shunta Route");
        var builder = root.AddComponent<ShuntaRouteBuilder>();
        builder.Rebuild();

        var sun = new GameObject("Moonlight");
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional; light.color = new Color(0.55f, 0.6f, 1f); light.intensity = 0.4f;
        sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

        // overview camera framing the whole route
        var cam = new GameObject("Overview Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        if (builder.Positions.Length > 0)
        {
            var b = new Bounds(builder.Positions[0], Vector3.zero);
            foreach (var p in builder.Positions) b.Encapsulate(p);
            cam.transform.position = b.center + new Vector3(0f, b.size.magnitude * 0.55f, -b.size.z * 0.6f);
            cam.transform.LookAt(b.center);
            cam.farClipPlane = b.size.magnitude * 3f;
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[shunta] scene saved: {ScenePath}");
    }

    [MenuItem("MapleRide/Shunta Metro/Validate")]
    public static void Validate()
    {
        var c = ShuntaCourseData.Load();
        if (c == null) { Fail("course json missing"); return; }
        int fails = 0;
        void Check(bool ok, string what) { Debug.Log($"[shunta] {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) fails++; }

        Check(c.zones.Length == 12, $"12 zones ({c.zones.Length})");
        float zsum = 0f; foreach (var z in c.zones) zsum += z.endKm - z.startKm;
        Check(Mathf.Abs(zsum - c.distanceKm) < 0.02f, $"zone lengths sum to {c.distanceKm} km ({zsum:F2})");
        Check(Mathf.Abs(c.ComputeGain() - c.targetGainM) <= 3f, $"gain {c.ComputeGain():F1} m vs target {c.targetGainM} m");
        Check(c.MaxGradePercent() <= 15f, $"max grade {c.MaxGradePercent():F1}% <= 15%");

        var go = new GameObject("shunta_validate") { hideFlags = HideFlags.HideAndDontSave };
        var b = go.AddComponent<ShuntaRouteBuilder>();
        b.buildRibbon = false; b.buildGates = false;
        b.Rebuild();
        float km = b.Km.Length > 0 ? b.Km[b.Km.Length - 1] : 0f;
        Check(Mathf.Abs(km - c.distanceKm) < 0.05f, $"route length {km:F2} km vs {c.distanceKm}");
        Check(Mathf.Abs(b.Ascent - c.targetGainM) <= 25f, $"sampled gain {b.Ascent:F0} m vs {c.targetGainM}");
        Object.DestroyImmediate(go);

        if (fails > 0) Fail($"{fails} check(s) failed");
        else Debug.Log("[shunta] VALIDATE OK");
    }

    static void Fail(string msg)
    {
        Debug.LogError("[shunta] VALIDATE FAILED: " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}

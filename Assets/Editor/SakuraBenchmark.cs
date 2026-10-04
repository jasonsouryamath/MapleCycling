using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Part B visual benchmark (spec sections 38-40).
///
/// Section 38 mandates ONE polished 200-500 m stretch of the EXISTING route before any
/// full-route rollout, and section 40 mandates before/after captures "from the exact same
/// gameplay camera position" at each stage of the upgrade order (section 23).
///
/// This class is that vehicle. It is capture-only: it never edits the scene's art, so it can be
/// re-run at any stage without becoming part of the thing it is measuring.
///
/// WHY THESE CAMERAS
/// The benchmark shots are framed with the SAME geometry as the player's chase camera
/// (<see cref="RideCameraSetup.ChaseOffset"/>), because section 39's criteria are about what the
/// rider sees while riding - not about beauty angles. A shot that flatters the scene from a
/// vantage the player never occupies cannot answer section 38's question.
///
/// USAGE
///   $env:MR_BENCH_STAGE = "baseline"   # then -executeMethod SakuraBenchmark.Capture
/// Stage names follow section 40: baseline, lighting, road, vegetation, atmosphere, final.
/// </summary>
public static class SakuraBenchmark
{
    /// <summary>
    /// The benchmark stretch, in route arc-length metres. PROVISIONAL but deliberate: this
    /// 200 m window contains every element section 38 requires - the tightest switchback pair
    /// (369-392), the steepest climb (403-470), the summit shelf and its landmark framing
    /// (470-531), guardrail, rock slope, mixed flora and the Fuji backdrop - without leaving
    /// the existing route.
    ///
    /// It deliberately starts at 355 rather than 290: the Cliff Tunnel occupies roughly 298-345,
    /// and a benchmark for a LIGHTING and ATMOSPHERE pass cannot be judged from inside a tunnel
    /// (the first baseline capture at d=300 came out as a black tunnel bore, which measures
    /// nothing about canopy shadows or aerial perspective).
    /// </summary>
    public const float BenchmarkStartM = 355f;
    public const float BenchmarkEndM = 555f;

    /// <summary>Camera height above the road, metres. Matches the chase camera.</summary>
    public const float CamUpM = 1.60f;
    /// <summary>Camera distance behind the mark, metres. Matches the chase camera.</summary>
    public const float CamBackM = 4.20f;
    /// <summary>How far up the road the camera looks, metres. PROVISIONAL framing choice.</summary>
    public const float CamAheadM = 14f;
    /// <summary>Height of the aim point above the road, metres. PROVISIONAL.</summary>
    public const float CamAimUpM = 1.25f;

    public const float Fov = 52f;          // matches ConfigureCamera
    public const int Width = 1600;
    public const int Height = 900;

    private const string OutDirRel = "../reference/good_graphics/benchmark";

    /// <summary>
    /// The fixed marks. Each is (arc metres, short name, what section-39 criteria it answers).
    /// These never move - moving a benchmark camera between stages invalidates the comparison.
    /// </summary>
    private static readonly (float D, string Name, string What)[] Marks =
    {
        (365f, "bend",     "hairpin approach: canopy shadow rhythm on the road, road-edge blending"),
        (392f, "hairpin",  "tightest switchback: guardrail, rock slope, near/mid/far separation"),
        (430f, "climb",    "steepest climb: vegetation contrast, blossom-vs-green, road repetition"),
        (480f, "shelf",    "summit shelf: open bright section, landmark framing, sky detail"),
        (530f, "summit",   "summit vista: distant mountains, aerial perspective, depth layers"),
    };

    [MenuItem("MapleRide/Environment/Capture Visual Benchmark")]
    public static void CaptureMenu() => Capture();

    public static void Capture()
    {
        var stage = Environment.GetEnvironmentVariable("MR_BENCH_STAGE");
        if (string.IsNullOrEmpty(stage)) stage = "unnamed";

        // Do NOT reopen if the pass scene is already active: a caller may have staged state in
        // memory (a probe forcing a light setting, a staging pass mid-flight) and reopening
        // silently discards it, so the capture would measure the previous scene on disk.
        if (EditorSceneManager.GetActiveScene().path != SakuraSceneDefaults.ScenePath)
            EditorSceneManager.OpenScene(SakuraSceneDefaults.ScenePath, OpenSceneMode.Single);

        // Section-38 work is judged by render, so the single most dangerous failure is shooting
        // against a hidden region and grading an empty ocean. Never skip this.
        int fixedRoots = SakuraSceneDefaults.Fix();
        if (fixedRoots > 0)
            Debug.LogWarning($"[bench] region visibility was WRONG - corrected {fixedRoots} root(s) " +
                             "before capturing. Earlier captures from this scene state are suspect.");

        var route = SakuraPassEnvironment.SakuraRoute.Load();
        if (route == null || route.Count == 0) { Debug.LogError("[bench] no route."); return; }

        var dir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDirRel));
        Directory.CreateDirectory(dir);

        Debug.Log($"[bench] stage '{stage}' - benchmark {BenchmarkStartM:0} to {BenchmarkEndM:0} m " +
                  $"of a {route.Length:0.0} m route, {Marks.Length} marks.");

        foreach (var m in Marks)
        {
            int i = route.IndexAt(m.D);
            int iAhead = route.IndexAt(Mathf.Min(m.D + CamAheadM, route.Length));
            int iBack = route.IndexAt(Mathf.Max(m.D - CamBackM, 0f));

            var p = route.Position[i];
            var up = Vector3.up;                       // world up - never inherit road bank
            // Behind the mark along the road itself, so the shot sits on the carriageway rather
            // than cutting the corner through the hillside.
            var camPos = route.Position[iBack] + up * CamUpM;
            var aim = route.Position[iAhead] + up * CamAimUpM;

            Shot(dir, $"bench_{stage}_{m.Name}", camPos, aim);
            Debug.Log($"[bench] {m.Name} d={m.D:0} m pos={p} - {m.What}");
        }

        Debug.Log($"[bench] stage '{stage}' complete -> {dir}");
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 aim)
    {
        var go = new GameObject("~BenchCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(aim, Vector3.up);
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 9000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            // The whole Sakura kit is forward-only by design (every cel shader declares
            // exclude_path:deferred). A camera created in code inherits UsePlayerSettings, so a
            // capture rig can silently resolve to a different path than the gameplay camera and
            // photograph lighting the player never sees. Pin it.
            cam.renderingPath = RenderingPath.Forward;
            Debug.Log($"[bench] cam path={cam.renderingPath} actual={cam.actualRenderingPath}");

            // A freshly added SakuraPostFX carries its FIELD DEFAULTS, which are only a fallback -
            // the real grade lives in TunePostFX. Without this push the benchmark would be graded
            // differently from the game, which is precisely the comparison it exists to make.
            // MR_BENCH_NOPOSTFX=1 shoots the same frame ungraded. The probe cameras that DID resolve
        // shadows carried no post FX, so this isolates the grade (bloom over a near-clipped road,
        // exposure, contrast, DOF) as a suspect for washing the dapple out.
        if (System.Environment.GetEnvironmentVariable("MR_BENCH_NOPOSTFX") != "1")
            SakuraPassEnvironment.TunePostFX(go.AddComponent<SakuraPostFX>());
        else
            Debug.Log("[bench] MR_BENCH_NOPOSTFX=1 - shooting UNGRADED (no SakuraPostFX).");

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            img.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
            RenderTexture.active = prev;

            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(img);
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log($"[bench] wrote {name}.png");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}

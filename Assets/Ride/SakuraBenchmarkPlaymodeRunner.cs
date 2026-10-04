using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Photographs the Part B benchmark section (to_implement.md sections 38-40) in REAL PLAY MODE,
/// from the real ride chase camera, at fixed arc metres so every upgrade stage is comparable.
///
/// WHY THIS EXISTS - and why the edit-mode <c>SakuraBenchmark</c> could not be trusted:
/// an edit-mode Camera.Render() in batchmode renders this scene with NO directional shadows at
/// all. That was proven, not guessed: a stock Standard-shader plane dropped onto the road came
/// back pure ambient, receiving zero light from a 1.6-intensity directional sun with
/// cullingMask -1 on the same layer, while the SakuraCel geometry beside it rendered bright.
/// Every setting audited clean (quality shadows All / VeryHigh / StableFit / 4 cascades, light
/// Soft then forced Hard at strength 1.0 and 400 m, cast and receive flags on, Forward path
/// pinned, Realtime bake mode, fullforwardshadows + addshadow in the shaders). The shadows are
/// simply not produced on that path.
///
/// Since section 24's entire premise is "dappled canopy shadows on the road", a capture path
/// that cannot show a shadow cannot verify any of Part B. So the benchmark is shot the same way
/// the player sees it: in play mode, down the chase camera, riding through the section.
///
/// Stage name comes from the MR_BENCH_STAGE environment variable, matching SakuraBenchmark, so
/// before/after pairs sit side by side in good_graphics/benchmark.
/// </summary>
public class SakuraBenchmarkPlaymodeRunner : MonoBehaviour
{
    /// <summary>Absolute output folder.</summary>
    public string outDir;

    /// <summary>Label for this capture stage (baseline / lighting / atmosphere / road / final).</summary>
    public string stage = "stage";

    /// <summary>
    /// PROVISIONAL: the benchmark window, section 38 asks for ~200-500 m. 355-555 m is the
    /// hairpin-to-summit stretch. It deliberately starts AFTER the Cliff Tunnel (roughly
    /// 298-345 m) - an earlier window put a mark inside the tunnel and photographed the dark.
    /// </summary>
    public float startM = 355f;
    public float endM = 555f;

    /// <summary>PROVISIONAL: scripted effort so the rider is riding, not parked.</summary>
    [Range(0f, 1f)] public float playerEffort = 0.75f;

    /// <summary>PROVISIONAL: kept low; see RideCameraPlaymodeRunner for why 8x lied.</summary>
    public float timeScale = 2f;

    public float maxSeconds = 420f;

    /// <summary>
    /// The pure pass climb+descent, so arc metres here are arc metres on the pass segment.
    /// NOT "sakura_circuit": that is 3279 m and selecting it directly put the ride on open
    /// water - the environment roots are switched by RegionDirector, not by SelectCourse.
    /// </summary>
    public string courseId = "pass_sprint";

    public static bool Finished;
    public static bool Failed;

    /// <summary>Throwaway shadow-proof switch, set from MR_BENCH_SHADOWPROOF.</summary>
    public bool _shadowProof;
    private bool _provedOnce;

    private struct Mark
    {
        public float metres;
        public string name;
        public string what;
    }

    // The same five marks the edit-mode benchmark used, so the two are directly comparable.
    private static readonly Mark[] Marks =
    {
        new Mark { metres = 365f, name = "bend",    what = "hairpin approach: canopy shadow rhythm on the road, road-edge blending" },
        new Mark { metres = 392f, name = "hairpin", what = "tightest switchback: guardrail, rock slope, near/mid/far separation" },
        new Mark { metres = 430f, name = "climb",   what = "steepest climb: vegetation contrast, blossom-vs-green, road repetition" },
        new Mark { metres = 480f, name = "shelf",   what = "summit shelf: open bright section, landmark framing, sky detail" },
        new Mark { metres = 530f, name = "summit",  what = "summit vista: distant mountains, aerial perspective, depth layers" },
    };

    private RideBootstrap _boot;
    private Camera _cam;
    private RouteFollower _route;
    private RouteDressingStreamer _streamer;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        _boot.Resolve();

        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }
        _route = FindFirstObjectByType<RouteFollower>();
        _streamer = FindFirstObjectByType<RouteDressingStreamer>(FindObjectsInactive.Include);

        yield return null;

        // This is an environment-art benchmark: no encounter, no rival, just the road.
        if (_boot.encounter != null) _boot.encounter.config.encounterEnabled = false;

        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        // The scene's session can be parked on any region's course, and SelectCourse alone does
        // NOT switch the environment roots - the first run of this harness rendered five frames
        // of open ocean because of exactly that. Fast travel first; it swaps the environment
        // roots and the time-of-day grade so the frames are lit the way the pass is really lit.
        if (_boot.regions != null) _boot.regions.FastTravel(RegionCatalog.SakuraPass);
        _boot.session.SelectCourse(courseId);
        if (_boot.regions != null) _boot.regions.SyncFromSession();
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.EffortInput = playerEffort;

        var course = _boot.session.Course;
        if (course == null) { Fail("no course '" + courseId + "'"); yield break; }
        if (course.Length < endM + 10f)
            Debug.LogWarning($"[bench-play] course '{courseId}' is only {course.Length:0} m; " +
                             $"the benchmark window ends at {endM:0} m and marks past that will be missed.");

        Debug.Log($"[bench-play] stage '{stage}' - window {startM:0}-{endM:0} m of course " +
                  $"'{courseId}' ({course.Length:0} m), {Marks.Length} marks.");

        // Start a little before the window. NOT ResetRide(): the environment root and the
        // roadside dressing are streamed in around the rider's CURRENT position, and resetting
        // underneath them has photographed a rider hanging over empty sea with no road at all.
        _boot.session.SeekTo(Mathf.Max(10f, startM - 40f));
        if (_route != null) _route.Apply();
        if (_streamer != null && _boot.rider != null) _streamer.Apply(_boot.rider.position);
        yield return null;
        yield return null;

        Time.timeScale = timeScale;
        var shot = new HashSet<string>();
        float t0 = Time.realtimeSinceStartup;

        while (shot.Count < Marks.Length && Time.realtimeSinceStartup - t0 < maxSeconds)
        {
            _boot.devices.EffortInput = playerEffort;
            float d = _boot.session.DistanceM;

            foreach (var m in Marks)
            {
                if (shot.Contains(m.name)) continue;
                if (d < m.metres) continue;
                shot.Add(m.name);
                yield return Shoot($"bench_{stage}_{m.name}", m, d);
                break;
            }
            yield return null;
        }

        Time.timeScale = 1f;

        if (shot.Count < Marks.Length)
        {
            Debug.LogWarning($"[bench-play] only captured {shot.Count}/{Marks.Length} marks " +
                             $"(reached {_boot.session.DistanceM:0} m).");
            Failed = true;
        }

        Debug.Log($"[bench-play] stage '{stage}' complete -> {outDir}");
        Finished = true;
    }

    private IEnumerator Shoot(string name, Mark m, float actualD)
    {
        yield return null;

        // Opt-in, throwaway: MR_BENCH_SHADOWPROOF=1 makes the first mark an unmissable shadow
        // test - ambient crushed, fog off, sun forced Hard at full strength, and a stock-shader
        // cube hung over the road just ahead of the rider. If that casts nothing, directional
        // shadows are not being produced at all and no amount of section-24 tuning will help.
        GameObject proof = null;
        if (_shadowProof && !_provedOnce)
        {
            _provedOnce = true;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.02f, 0.02f, 0.03f, 1f);
            RenderSettings.ambientIntensity = 0.05f;
            RenderSettings.fog = false;

            var sn = RenderSettings.sun;
            if (sn != null)
            {
                sn.shadows = LightShadows.Hard;
                sn.shadowStrength = 1f;
                sn.intensity = 2f;
                sn.renderMode = LightRenderMode.ForcePixel;
            }
            QualitySettings.shadowDistance = 400f;
            QualitySettings.shadows = ShadowQuality.All;

            var rider = _boot.rider;
            proof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proof.name = "~BenchShadowProof";
            proof.transform.position = rider.position + rider.forward * 12f + Vector3.up * 6f;
            proof.transform.localScale = new Vector3(8f, 1f, 8f);
            proof.GetComponent<Renderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.On;

            Debug.Log($"[bench-play] SHADOWPROOF cube at {proof.transform.position}, " +
                      $"rider at {rider.position}, sun fwd={(sn != null ? sn.transform.forward : Vector3.zero)}, " +
                      $"SHADOWS_SCREEN={Shader.IsKeywordEnabled("SHADOWS_SCREEN")} " +
                      $"SHADOWS_DEPTH={Shader.IsKeywordEnabled("SHADOWS_DEPTH")} " +
                      $"camPath={_cam.actualRenderingPath} camMask={_cam.cullingMask}");
            yield return null;
        }

        // Kuro must be baked: a manual Camera.Render() does not line up with Unity's per-frame
        // skinning submission and the player comes back shredded. fixTransformScale is
        // mandatory - his "char1" renderer carries a leftover ~0.01 import scale.
        var bakes = new List<GameObject>();
        if (_boot.rider != null) bakes.AddRange(BeginBake(_boot.rider.gameObject, true));

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());

        Destroy(tex);
        rt.Release();
        Destroy(rt);
        EndBake(bakes);
        if (proof != null) Destroy(proof);

        var sun = RenderSettings.sun;
        Debug.Log($"[bench-play] wrote {name}.png at {actualD:0} m (target {m.metres:0}) - {m.what} " +
                  $"| sun shadows={(sun != null ? sun.shadows.ToString() : "no sun")} " +
                  $"strength={(sun != null ? sun.shadowStrength : 0f):0.00} " +
                  $"render={(sun != null ? sun.renderMode.ToString() : "-")} " +
                  $"qShadows={QualitySettings.shadows} dist={QualitySettings.shadowDistance:0}");
    }

    private static List<GameObject> BeginBake(GameObject root, bool fixTransformScale)
    {
        var temps = new List<GameObject>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var baked = new Mesh { name = smr.name + "_BakedPose" };
            smr.BakeMesh(baked, false);

            var go = new GameObject("~BenchBakedPose_" + smr.name);
            go.transform.SetParent(smr.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = smr.sharedMaterials;
            mr.shadowCastingMode = smr.shadowCastingMode;
            mr.receiveShadows = smr.receiveShadows;

            if (fixTransformScale)
            {
                Vector3 wp = go.transform.position;
                Quaternion wr = go.transform.rotation;
                go.transform.SetParent(null, true);
                go.transform.position = wp;
                go.transform.rotation = wr;
                go.transform.localScale = Vector3.one;
            }

            smr.enabled = false;
            temps.Add(go);
        }
        return temps;
    }

    private void EndBake(List<GameObject> temps)
    {
        if (_boot != null && _boot.rider != null)
            foreach (var smr in _boot.rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.enabled = true;

        foreach (var t in temps)
        {
            if (t == null) continue;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(t);
        }
    }

    private void Fail(string why)
    {
        Debug.LogError("[bench-play] " + why);
        Failed = true;
        Finished = true;
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Runs the Hanakage encounter in REAL PLAY MODE and photographs it.
///
/// Why this exists: every verification frame in this project up to now was rendered from the
/// editor with no player loop running, and that turned out to be a lie detector problem. A
/// SkinnedMeshRenderer outside play mode draws the pose it was last EVALUATED at, so three
/// separate captures came back pixel-identical while the log honestly reported a 62 degree head
/// turn. The editor harness now works around that by baking the posed mesh; this runner removes
/// the need for the workaround entirely, because in play mode Unity evaluates skinning, particle
/// systems, canvases and camera blends the same way the player will see them.
///
/// It is deliberately a coroutine over real frames rather than a fixed-step simulation: the
/// point is to catch things that only exist in a real frame.
///
/// Driven by <c>HanakagePlaymodeCapture</c> (editor), which enters play mode and spawns this.
/// </summary>
public class HanakagePlaymodeRunner : MonoBehaviour
{
    /// <summary>Where the PNGs land, relative to the repo's good_graphics folder.</summary>
    public string outDir;

    /// <summary>
    /// PROVISIONAL: the encounter is ~3 minutes of riding. At 6x it is ~30 s of wall clock,
    /// which keeps a batchmode run well inside the watchdog without making the blend timings
    /// (camera tightening, card fades) behave differently - they are all dt-driven.
    /// </summary>
    public float timeScale = 6f;

    /// <summary>Watchdog. If the encounter has not finished by here, bail and say so.</summary>
    public float maxSeconds = 240f;

    /// <summary>
    /// PROVISIONAL: the scripted player effort, 0..1, matching the editor harness. Without this
    /// the device layer supplies nothing, the simulated rider crawls, and the encounter goes
    /// Approach -> dropped -> Escape without ever reaching her wheel. That is exactly what the
    /// first play-mode run did, and it is a good reminder that "the encounter completed" is not
    /// the same as "the encounter happened".
    /// </summary>
    [Range(0f, 1f)] public float playerEffort = 0.75f;

    /// <summary>Set when the run is over, however it ended. The editor side polls this.</summary>
    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private HanakageEncounter _enc;
    private Camera _cam;
    private Camera _uiCam;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        _boot.Resolve();
        _enc = _boot.encounter;
        if (_enc == null) { Fail("encounter not staged - run Stage Encounter first"); yield break; }

        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }

        // Give the scene a frame to finish waking up before touching anything.
        yield return null;

        if (_boot.regions != null) _boot.regions.FastTravel(RegionCatalog.SakuraPass);
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("pass_sprint");
        _boot.devices.acceptKeyboardEffort = false;

        // A forced, seeded roll and a cleared journal: the capture must be repeatable and the
        // discovery card must be a genuine FIRST discovery every time.
        RiderJournal.Forget(RiderJournal.HanakageId);
        _enc.config.spawnRateMode = HanakageEncounterConfig.SpawnRateMode.Development;
        _enc.SetRollSeed(20260401);
        HanakageRollLedger.Clear();   // this capture is a fresh run, never a spent one
        _enc.ResetRun();
        _boot.session.SeekTo(_enc.config.zoneEntryM - 30f);

        RouteCanvasesToCamera();

        Time.timeScale = Mathf.Max(0.1f, timeScale);
        Debug.Log($"[hanakage-play] running at {Time.timeScale}x from " +
                  $"{_boot.session.DistanceM:0} m");

        float t0 = Time.realtimeSinceStartup;
        var shot = new System.Collections.Generic.HashSet<string>();

        while (Time.realtimeSinceStartup - t0 < maxSeconds)
        {
            var phase = _enc.State;

            // One frame per phase, taken a beat AFTER the transition so the blends have moved.
            if (phase == HanakageEncounter.Phase.OnWheel && shot.Add("onwheel"))
                yield return Capture(0.35f, "play_1_onwheel");

            if (phase == HanakageEncounter.Phase.Attack && shot.Add("attack"))
                // Inside the look-back hold: turn 0.55 s + half of the 1.30 s hold. This is the
                // frame where her head, the ribbon gust, the petal burst and the camera glance
                // are all live at once - the design's "Recognition" beat.
                yield return Capture(1.1f, "play_2_attack");

            if (phase == HanakageEncounter.Phase.FinalHairpin && shot.Add("hairpin"))
                yield return Capture(0.5f, "play_3_hairpin");

            if (_enc.DiscoveryCardSeconds > 0.05f && shot.Add("card"))
            {
                // Mid-card, so the fade is at full opacity rather than caught on its way in.
                yield return Capture(_enc.config.discoveryCardSeconds * 0.5f, "play_4_journal");
                break;
            }

            if (phase == HanakageEncounter.Phase.Complete ||
                phase == HanakageEncounter.Phase.Declined) break;

            _boot.devices.EffortInput = playerEffort;
            yield return null;
        }

        Time.timeScale = 1f;

        if (!shot.Contains("card"))
            Debug.LogWarning("[hanakage-play] the run ended without a discovery card " +
                             $"(last phase {_enc.State}, shots {shot.Count}).");

        RiderJournal.Forget(RiderJournal.HanakageId);
        Debug.Log($"[hanakage-play] done, {shot.Count} frames written to {outDir}");
        Finished = true;
    }

    private void Fail(string why)
    {
        Debug.LogError("[hanakage-play] " + why);
        Failed = true;
        Finished = true;
    }

    /// <summary>
    /// Both HUD canvases are routed to a DEDICATED UI camera that shares the ride camera's
    /// target texture.
    ///
    /// The obvious shortcut - point the canvases straight at the ride camera - renders the
    /// interface in that camera's perspective projection, and the first run produced a HUD that
    /// was visibly skewed and doubled across the frame. A second camera with a depth-only clear
    /// and its own culling mask keeps the interface flat, which is how the editor harness has
    /// always done it.
    /// </summary>
    private void RouteCanvasesToCamera()
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);

        var uiGo = new GameObject("~HanaPlayHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.fieldOfView = _cam.fieldOfView;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;

        var canvases = new[]
        {
            _boot.hud != null ? _boot.hud.Canvas : null,
            _boot.encounterHud != null ? _boot.encounterHud.Canvas : null
        };
        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = _uiCam;
            // The encounter overlay composites ON TOP of the instrument panel.
            c.planeDistance = i == 0 ? 1f : 0.9f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
    }

    private IEnumerator Capture(float delaySeconds, string name)
    {
        // Unscaled, so the delay is the same slice of the PERFORMANCE regardless of timeScale.
        float end = Time.realtimeSinceStartup + delaySeconds / Mathf.Max(0.1f, Time.timeScale);
        while (Time.realtimeSinceStartup < end)
        {
            _boot.devices.EffortInput = playerEffort;
            yield return null;
        }

        // NOT WaitForEndOfFrame: it never fires in headless batchmode, so the very first capture
        // silently parked the coroutine forever while the encounter carried on without it. One
        // more frame is enough anyway - skinning, particles and canvas layout have all been
        // evaluated by the time this resumes, and the Render() below is explicit.
        yield return null;

        // A manual Camera.Render() call does not line up with Unity's normal per-frame skinning
        // submission the way an automatic camera render does - at 6x timeScale, with several
        // simulation steps folded into one real frame, this produced the exact same "renders the
        // pose it was last evaluated at" symptom the editor harness already fixed for Hanakage
        // (see BeginBakedCapture below), on the PLAYER specifically: a shredded, dithered mesh at
        // some camera angles and not others. Baking both riders' current pose right before the
        // explicit Render() call removes the dependency on that submission order entirely.
        var bakes = new List<GameObject>();
        if (_enc.hanakage != null) bakes.AddRange(BeginBake(_enc.hanakage.gameObject, false));
        if (_boot.rider != null) bakes.AddRange(BeginBake(_boot.rider.gameObject, true));

        const int W = 1920, H = 1080;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        if (_uiCam != null)
        {
            _uiCam.fieldOfView = _cam.fieldOfView;
            var prevUi = _uiCam.targetTexture;
            _uiCam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            _uiCam.Render();
            _uiCam.targetTexture = prevUi;
        }

        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;

        Directory.CreateDirectory(outDir);
        string path = Path.Combine(outDir, name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());

        Destroy(tex);
        rt.Release();
        Destroy(rt);
        EndBake(bakes);

        var perf = _enc.hanakage != null
            ? _enc.hanakage.GetComponent<HanakagePerformance>() : null;
        var camDir = _enc.GetComponent<HanakageCameraDirector>();
        var aud = _enc.GetComponent<HanakageAudioDirector>();
        Debug.Log($"[hanakage-play] {name}: phase {_enc.State}, gap {_enc.GapM:0.0} m, " +
                  $"outcome {_enc.Result}, " +
                  $"lookback {(perf != null ? perf.LookBackWeight : 0f):0.00}, " +
                  $"stand {(perf != null ? perf.StandWeight : 0f):0.00}, " +
                  $"camFov {(camDir != null ? camDir.CurrentFovDelta : 0f):0.0}, " +
                  $"camDist {(camDir != null ? camDir.CurrentDistanceScale : 1f):0.00}, " +
                  $"camAim {(camDir != null ? camDir.CurrentAimBias : 0f):0.00}, " +
                  $"mix amb {(aud != null ? aud.AmbienceLevel : 0f):0.00} / " +
                  $"motif {(aud != null ? aud.MotifLevel : 0f):0.00}");
    }

    /// <summary>
    /// Replaces every enabled SkinnedMeshRenderer under <paramref name="root"/> with a STATIC
    /// bake of the pose it is in right now, mirroring the editor capture harness's
    /// BeginBakedCapture (see HanakageNpcSetup.cs, Editor-only and so not referenceable from
    /// this runtime script - duplicated here rather than shared).
    ///
    /// <paramref name="fixTransformScale"/> exists for Kuro specifically: his "char1" renderer's
    /// own Transform carries a leftover ~0.01 import-time scale that live GPU skinning never
    /// uses (his bones drive the actual on-screen size, independently of it) but which a naive
    /// bake-and-reparent WOULD inherit, shrinking him to a speck. Detaching to world and forcing
    /// the baked copy's scale back to identity (keeping only position/rotation) reproduces the
    /// size bones actually render him at. Hanakage's own rig scales her renderer's transform
    /// consistently with her bones, so she does not need this correction.
    /// </summary>
    private static List<GameObject> BeginBake(GameObject root, bool fixTransformScale)
    {
        var temps = new List<GameObject>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var baked = new Mesh { name = smr.name + "_BakedPose" };
            smr.BakeMesh(baked, false);

            var go = new GameObject("~PlayBakedPose_" + smr.name);
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

    /// <summary>Removes the bakes and switches the skinned renderers back on.</summary>
    private void EndBake(List<GameObject> temps)
    {
        if (_enc != null && _enc.hanakage != null)
            foreach (var smr in _enc.hanakage.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.enabled = true;
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
}


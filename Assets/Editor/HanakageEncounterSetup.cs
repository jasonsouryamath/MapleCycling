using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the Hanakage encounter into the Sakura Pass scene, and captures the encounter as the
/// player actually sees it - real world, real gradient, real HUD.
///
/// Two passes, kept apart on purpose:
///
///   <see cref="AddToScene"/>   writes to the scene. Idempotent: it adds the two components to
///                              the existing "MapleRide Ride" object if they are not already
///                              there, re-runs the bootstrap's own Resolve so the wiring is the
///                              same wiring Play mode would produce, and saves.
///
///   <see cref="CaptureEncounter"/> writes only PNGs. It drives a real ride at a fixed timestep
///                              until the encounter reaches a given phase, renders that instant
///                              with both HUD canvases composited, and then throws the whole
///                              scene away with DiscardChanges.
///
/// The composite trick (a second, un-graded UI camera on the same render target) is lifted
/// wholesale from <see cref="RideValidation"/>: rendering the canvases through the gameplay
/// camera pushes them through SakuraPostFX's sunset grade, which turns the washi cards pink and
/// the text unreadable - so what you would be reviewing would not be what the player sees.
/// </summary>
public static class HanakageEncounterSetup
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string OutDir = "../reference/good_graphics/hanakage";
    const float Dt = 1f / 30f;
    const float MaxSeconds = 400f;

    // ------------------------------------------------------------------ stage

    [MenuItem("MapleRide/Hanakage/Stage Encounter")]
    public static void AddToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null)
        {
            Debug.LogError("[hanakage] no RideBootstrap in the scene - run the ride setup first.");
            return;
        }

        var host = boot.gameObject;
        var enc = host.GetComponent<HanakageEncounter>();
        if (enc == null) enc = host.AddComponent<HanakageEncounter>();
        var hud = host.GetComponent<HanakageEncounterHud>();
        if (hud == null) hud = host.AddComponent<HanakageEncounterHud>();

        // The rider was staged absent by HanakageNpcSetup; find her by EXACT name so a second
        // Hanakage - or the player's own bike - can never be picked up by a Contains match.
        var riderGo = GameObject.Find(HanakageRider.ObjectName);
        enc.hanakage = riderGo != null ? riderGo.GetComponent<HanakageRider>() : null;
        if (enc.hanakage == null)
            Debug.LogWarning("[hanakage] '" + HanakageRider.ObjectName + "' is not in the scene. " +
                             "Run MapleRide/Hanakage/Stage Hanakage first.");

        boot.encounter = enc;
        boot.encounterHud = hud;
        boot.Resolve();

        // Push section-18 values that changed since this scene was last saved. A serialized
        // component keeps whatever was on it when the scene was written, so editing a field's
        // code default does NOTHING to an existing scene - the staging pass has to write it.
        // spawnAheadM was 60 m; the handoff (sections 4 and 18) calls for 100-150 m.
        enc.config.spawnAheadM = SpawnAheadM;

        // P3 rarity (section 18): the shipping rate is a NAMED MODE, not a float somebody has to
        // remember to edit. The scene stays on Development so QA sees her every run; flipping to
        // Production is a one-line change with the 1-in-30 value living in the config class.
        enc.config.spawnRateMode = ShipRarity;

        // The performance driver is found by exact name and re-wired every stage, so a rig
        // that was restaged (new GLB, new components) cannot leave it pointing at a dead rig.
        WirePerformance(enc);

        enc.ResetRun();

        EditorUtility.SetDirty(host);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"[hanakage] encounter staged on '{host.name}': rider " +
                  $"{(enc.hanakage != null ? "wired" : "MISSING")}, " +
                  $"zone {enc.config.zoneEntryM:0} m, spawn +{enc.config.spawnAheadM:0} m, " +
                  $"chance {enc.config.EffectiveSpawnChance:0.###} ({enc.config.spawnRateMode}), " +
                  $"crest {enc.config.escapeArcM:0} m.");
    }

    // ------------------------------------------------------------------ performance wiring

    /// <summary>
    /// PROVISIONAL: the rarity the staged scene ships at. Development = every eligible run, which
    /// is what building and QA need. Switch to
    /// <see cref="HanakageEncounterConfig.SpawnRateMode.Production"/> for the handoff's 1-in-30
    /// and re-run this pass; nothing else has to change.
    /// </summary>
    private const HanakageEncounterConfig.SpawnRateMode ShipRarity =
        HanakageEncounterConfig.SpawnRateMode.Development;

    /// <summary>
    /// PROVISIONAL: metres ahead of the player she is placed on spawn. Handoff sections 4 and
    /// 18 call for 100-150 m; 110 m keeps her inside the open sightline up to the tunnel mouth
    /// while being far enough that the first sighting reads as "someone up the road", not as
    /// something that appeared.
    /// </summary>
    private const float SpawnAheadM = 110f;

    /// <summary>
    /// Finds (or adds) <see cref="HanakagePerformance"/> on the staged rider and points it at
    /// the encounter, the rig, the ribbons and the petal burst.
    ///
    /// This lives in the ENCOUNTER setup rather than the NPC setup on purpose: the performance
    /// driver is the only thing that needs to know about both halves, and the NPC pass runs
    /// first and has no encounter to bind to yet.
    /// </summary>
    private static void WirePerformance(HanakageEncounter enc)
    {
        if (enc.hanakage == null) return;
        var npc = enc.hanakage.gameObject;

        var perf = npc.GetComponent<HanakagePerformance>();
        if (perf == null) perf = npc.AddComponent<HanakagePerformance>();

        perf.encounter = enc;
        perf.rider = enc.hanakage;
        perf.rig = npc.GetComponentInChildren<KuroBikeRig>(true);
        perf.ribbon = npc.GetComponentInChildren<HanakageRibbon>(true);
        perf.petalBurst = FindBurst(npc);

        if (perf.rig == null)
            Debug.LogWarning("[hanakage] no KuroBikeRig under her - the look-back and the " +
                             "standing attack will not play.");
        if (perf.ribbon == null)
            Debug.LogWarning("[hanakage] no HanakageRibbon under her - no streamer gust.");
        if (perf.petalBurst == null)
            Debug.LogWarning("[hanakage] no petal burst emitter - no VFX on the attack.");

        EditorUtility.SetDirty(perf);
        Debug.Log($"[hanakage] performance wired: rig {(perf.rig != null ? "ok" : "MISSING")}, " +
                  $"ribbon {(perf.ribbon != null ? "ok" : "MISSING")}, " +
                  $"petals {(perf.petalBurst != null ? "ok" : "MISSING")}.");

        WireDirectors(enc, perf);
    }

    /// <summary>
    /// Camera (section 17) and audio (section 16) directors.
    ///
    /// Both live on the ENCOUNTER object, not on Hanakage: they have to keep working for the
    /// beats where she is already gone - the camera has to relax after the escape, and the audio
    /// has to carry the 20-30 s of ordinary climbing before the Journal card. Parenting them to
    /// her would kill them the moment she despawns, which is precisely the wrong moment.
    /// </summary>
    private static void WireDirectors(HanakageEncounter enc, HanakagePerformance perf)
    {
        var host = enc.gameObject;

        var cam = host.GetComponent<HanakageCameraDirector>();
        if (cam == null) cam = host.AddComponent<HanakageCameraDirector>();
        cam.encounter = enc;
        cam.performance = perf;
        cam.rider = enc.hanakage;
        // Pushed onto the serialized component, not left to the code default: a field default
        // does nothing to a component already saved in the scene. 0.20 rather than the first
        // pass's 0.32 - at 0.32 the attack frame on the hairpin swung far enough into the cliff
        // that the road was reduced to a corner of the screen, which section 17 forbids.
        cam.lookBackAimBias = 0.20f;

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        Camera rideCam = boot != null ? boot.rideCamera : null;
        if (rideCam == null) rideCam = Camera.main;
        cam.followCamera = rideCam != null ? rideCam.GetComponent<KuroFollowCamera>() : null;
        if (cam.followCamera == null)
            Debug.LogWarning("[hanakage] no KuroFollowCamera on the ride camera - the attack " +
                             "tightening and the look-back glance will not play.");

        var aud = host.GetComponent<HanakageAudioDirector>();
        if (aud == null) aud = host.AddComponent<HanakageAudioDirector>();
        aud.encounter = enc;
        aud.rider = enc.hanakage;

        EditorUtility.SetDirty(cam);
        EditorUtility.SetDirty(aud);
        Debug.Log($"[hanakage] directors wired: camera " +
                  $"{(cam.followCamera != null ? "ok" : "NO FOLLOW CAM")}, " +
                  "audio ok (mix state only - no audio assets exist yet).");
    }

    /// <summary>Exact-name lookup for the burst emitter built by the NPC staging pass.</summary>
    private static ParticleSystem FindBurst(GameObject npc)
    {
        foreach (var ps in npc.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.gameObject.name == HanakagePerformance.BurstName) return ps;
        return null;
    }

    // ------------------------------------------------------------------ capture
    [MenuItem("MapleRide/Hanakage/Capture Encounter")]
    public static void CaptureEncounter()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[hanakage] no RideBootstrap."); return; }
        boot.Resolve();

        var enc = boot.encounter;
        if (enc == null) { Debug.LogError("[hanakage] encounter not staged."); return; }

        // The scene's session can be parked on any region's course; the encounter only exists on
        // the pass climb, so travel there properly (which also restores the right ambience).
        if (boot.regions != null) boot.regions.FastTravel(RegionCatalog.SakuraPass);
        boot.session.autoLapsFromTarget = false;
        boot.session.plannedLaps = 1;
        boot.session.SelectCourse(HanakageNpcSetup.EncounterCourseId);

        if (boot.hud != null && boot.hud.Canvas == null) boot.hud.Build();
        if (boot.encounterHud != null && boot.encounterHud.Canvas == null) boot.encounterHud.Build();

        // Force the roll and clear any real save, so the capture is repeatable and the discovery
        // card is guaranteed to be a FIRST discovery.
        bool hadJournal = RiderJournal.IsDiscovered(RiderJournal.HanakageId);
        RiderJournal.Forget(RiderJournal.HanakageId);
        enc.config.spawnRateMode = HanakageEncounterConfig.SpawnRateMode.Development;
        enc.SetRollSeed(20260401);
        HanakageRollLedger.Clear();   // a capture is a fresh run, never a spent one
        enc.ResetRun();

        boot.session.SeekTo(enc.config.zoneEntryM - 30f);
        boot.devices.acceptKeyboardEffort = false;

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir));
        Directory.CreateDirectory(dir);

        // One continuous ride; the camera fires as each phase is first reached.
        RunTo(boot, HanakageEncounter.Phase.Observation,  0.75f, dir, "enc_1_sighting");
        RunTo(boot, HanakageEncounter.Phase.Approach,     0.75f, dir, "enc_2_approach");
        RunTo(boot, HanakageEncounter.Phase.OnWheel,      0.75f, dir, "enc_3_onwheel");
        // 1.1 s into Attack, not 2 s: that is inside the look-back HOLD (turn 0.55 s + hold
        // 1.30 s), so the shot catches her head actually turned and her line on screen rather
        // than the tail of the return. This frame is the design's "Recognition" beat.
        RunTo(boot, HanakageEncounter.Phase.Attack,       0.75f, dir, "enc_4_attack", 1.1f);
        RunTo(boot, HanakageEncounter.Phase.FinalHairpin, 0.75f, dir, "enc_5_hairpin");
        RunTo(boot, HanakageEncounter.Phase.Escape,       0.75f, dir, "enc_6_escape");
        RunUntilCard(boot, 0.75f, dir, "enc_7_journal");

        if (hadJournal) RiderJournal.Discover(RiderJournal.HanakageId);
        else RiderJournal.Forget(RiderJournal.HanakageId);

        Debug.Log("[hanakage] encounter captures written to " + dir);
        MapleRideSceneBootstrap.DiscardChanges();
    }

    /// <summary>Rides until the encounter first enters <paramref name="target"/>, then shoots.</summary>
    private static void RunTo(RideBootstrap boot, HanakageEncounter.Phase target, float effort,
                              string dir, string name, float dwellSeconds = 2f)
    {
        var enc = boot.encounter;
        for (float s = 0f; s < MaxSeconds && enc.State != target; s += Dt)
        {
            boot.devices.EffortInput = effort;
            boot.session.Tick(Dt);
            enc.Tick(Dt);
            TickPerformance(boot, Dt);
        }
        if (enc.State != target)
        {
            Debug.LogWarning($"[hanakage] never reached {target}; skipping {name}.");
            return;
        }
        // A couple of seconds inside the phase, so the shot is the phase rather than its edge.
        for (float s = 0f; s < dwellSeconds; s += Dt)
        {
            boot.devices.EffortInput = effort;
            boot.session.Tick(Dt);
            enc.Tick(Dt);
            TickPerformance(boot, Dt);
        }
        Shoot(boot, Path.Combine(dir, name + ".png"), name);
    }

    /// <summary>Rides through the quiet beat until the Journal card is actually on screen.</summary>
    private static void RunUntilCard(RideBootstrap boot, float effort, string dir, string name)
    {
        var enc = boot.encounter;
        for (float s = 0f; s < MaxSeconds && enc.DiscoveryCardSeconds <= 0.05f; s += Dt)
        {
            boot.devices.EffortInput = effort;
            boot.session.Tick(Dt);
            enc.Tick(Dt);
            TickPerformance(boot, Dt);
            if (enc.State == HanakageEncounter.Phase.Complete) break;
        }
        if (enc.DiscoveryCardSeconds <= 0.05f)
        {
            Debug.LogWarning("[hanakage] discovery card never appeared; skipping " + name);
            return;
        }
        Shoot(boot, Path.Combine(dir, name + ".png"), name);
    }

    // ------------------------------------------------------------------ render

    /// <summary>
    /// Advances the PRESENTATION layer by one capture frame.
    ///
    /// The capture loop drives <c>session.Tick</c>/<c>enc.Tick</c> by hand because nothing in
    /// the editor is calling Update. The same is true of the performance layer, and it is more
    /// fragile: <see cref="KuroBikeRig"/> rewrites the hips, spine, neck and head from their
    /// bind pose every LateUpdate, so a pose modifier that is written but never consumed
    /// changes nothing at all. Order is therefore load-bearing -
    ///   1. the driver writes the modifiers,
    ///   2. the rig consumes them and re-seats her,
    ///   3. the ribbons swing from the head bone the rig just moved.
    /// Get 2 and 3 the wrong way round and the streamers lag a frame behind her head, which is
    /// exactly the frame the look-back shot is trying to capture.
    /// </summary>
    private static void TickPerformance(RideBootstrap boot, float dt)
    {
        var enc = boot.encounter;
        if (enc == null || enc.hanakage == null) return;
        var npc = enc.hanakage.gameObject;

        var perf = npc.GetComponent<HanakagePerformance>();
        if (perf != null) perf.Tick(dt);

        var rig = npc.GetComponentInChildren<KuroBikeRig>(true);
        if (rig != null) rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);

        var ribbon = npc.GetComponentInChildren<HanakageRibbon>(true);
        if (ribbon != null) ribbon.Tick(dt);

        // Directors live on the encounter object, so they are stepped even after she despawns.
        var cam = enc.GetComponent<HanakageCameraDirector>();
        if (cam != null) cam.Tick(dt);
        var aud = enc.GetComponent<HanakageAudioDirector>();
        if (aud != null) aud.Tick(dt);
    }

    private static void Shoot(RideBootstrap boot, string path, string label)
    {
        var enc = boot.encounter;
        var session = boot.session;
        float d = session.DistanceM;

        // The rider and Hanakage are both placed from the course, not from a physics step, so
        // the capture has to push the same Apply() a frame would.
        if (boot.follower != null) boot.follower.Apply();
        if (enc.hanakage != null) enc.hanakage.Apply();
        // Re-seat her AFTER Apply moved the rig root, or the standing pose would be computed
        // against last frame's position on the road.
        TickPerformance(boot, Dt);
        if (boot.hud != null) boot.hud.Refresh();
        if (boot.encounterHud != null) boot.encounterHud.Refresh();

        RenderCompositeBaked(boot, path);

        Debug.Log($"[hanakage] {label}: phase {enc.State}, player {d:0} m, her {enc.HerArcM:0} m, " +
                  $"gap {enc.GapM:0.0} m, grade {session.DisplayGradePct:0.0} %, " +
                  $"her {enc.HerWatts:0} W / {enc.HerSpeedMps * 3.6f:0.0} kph, " +
                  $"player {boot.devices.Telemetry.Watts:0} W / {session.SpeedKph:0.0} kph, " +
                  $"objective '{enc.ObjectiveText}', line '{enc.DialogueText}'");
    }

    /// <summary>
    /// Same composite, but with every posed rider baked to a static mesh first.
    ///
    /// Outside play mode a SkinnedMeshRenderer renders the pose it was last EVALUATED at, not
    /// the pose its bones are in. That is why the first run of these frames showed Hanakage
    /// riding dead straight in the very shot whose log line read "yaw 62.0 deg". Baking is the
    /// only way a batchmode frame tells the truth about the performance layer.
    ///
    /// The PLAYER is baked too, for the same reason and by the same mechanism: the "shredded,
    /// dithered, near-see-through" artifact QA reported on Kuro at several encounter angles was
    /// this exact stale-skin symptom, not a texture or mesh defect (his base-color mip chain and
    /// an errant emissive binding were fixed along the way as real but unrelated anomalies - ruled
    /// out here because fixing them produced zero visible change). An earlier attempt to bake him
    /// was abandoned because he "disappeared from the frame entirely" - that was never a baking
    /// failure, it was a scale bug: Kuro's "char1" SkinnedMeshRenderer Transform carries a
    /// leftover import-time local scale of ~0.01 that live GPU skinning never uses (his bones
    /// drive his actual on-screen size, independently of it), but which parenting a baked static
    /// mesh under that same transform silently inherits, shrinking him to a 100x-too-small speck.
    /// Detaching the bake to world and forcing its scale back to identity (keeping only the
    /// position/rotation the transform gave it) renders him at the size his bones actually put
    /// him at. Hanakage's own rig scales her renderer's transform consistently with her bones, so
    /// she does not need this correction.
    /// </summary>
    private static void RenderCompositeBaked(RideBootstrap boot, string path)
    {
        var roots = new List<GameObject>();
        if (boot.encounter != null && boot.encounter.hanakage != null)
            roots.Add(boot.encounter.hanakage.gameObject);
        if (boot.rider != null) roots.Add(boot.rider.gameObject);

        var temps = new List<KeyValuePair<GameObject, List<GameObject>>>();
        foreach (var r in roots)
        {
            var baked = HanakageNpcSetup.BeginBakedCapture(r);
            bool isPlayer = boot.rider != null && r == boot.rider.gameObject;
            if (isPlayer)
            {
                foreach (var t in baked)
                {
                    Vector3 wp = t.transform.position;
                    Quaternion wr = t.transform.rotation;
                    t.transform.SetParent(null, true);
                    t.transform.position = wp;
                    t.transform.rotation = wr;
                    t.transform.localScale = Vector3.one;
                }
            }
            temps.Add(new KeyValuePair<GameObject, List<GameObject>>(r, baked));
        }
        try { RenderComposite(boot, path); }
        finally
        {
            foreach (var kv in temps) HanakageNpcSetup.EndBakedCapture(kv.Key, kv.Value);
        }
    }

    private static void RenderComposite(RideBootstrap boot, string path)
    {
        const int W = 1920, H = 1080;
        var course = boot.session.Course;
        float d = boot.session.DistanceM;
        var p = course.PositionAt(d);
        var t = course.TangentAt(d);
        var s = course.SideAt(d);

        var go = new GameObject("~HanaEncCam");
        var cam = go.AddComponent<Camera>();

        // Mirror the in-game camera director (section 17) onto the capture camera. The harness
        // builds its own camera rather than driving the scene's, so without this the tightening
        // would be real in play mode and invisible in every verification frame - the same class
        // of lie as the stale skinned mesh.
        var dir = boot.encounter != null
            ? boot.encounter.GetComponent<HanakageCameraDirector>() : null;
        float distScale = dir != null ? dir.CurrentDistanceScale : 1f;
        float fovDelta = dir != null ? dir.CurrentFovDelta : 0f;
        float aimBias = dir != null ? dir.CurrentAimBias : 0f;

        cam.transform.position = p + Vector3.up * (3.1f * distScale)
                                   - t * (7.2f * distScale) - s * 1.1f;
        Vector3 look = p + t * 9f + Vector3.up * 1.3f;
        if (aimBias > 0.001f && boot.encounter != null && boot.encounter.hanakage != null
            && boot.encounter.hanakage.Present)
            look = Vector3.Lerp(look, boot.encounter.hanakage.transform.position + Vector3.up,
                                Mathf.Min(aimBias, KuroFollowCamera.MaxAimBias));
        cam.transform.LookAt(look);
        cam.fieldOfView = 55f + fovDelta;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        cam.cullingMask = ~(1 << HudSprites.UiLayer);
        go.AddComponent<SakuraPostFX>();
        SakuraPassEnvironment.TunePostFX(go.GetComponent<SakuraPostFX>());

        var hudGo = new GameObject("~HanaEncHudCam");
        hudGo.transform.SetParent(go.transform, false);
        var hudCam = hudGo.AddComponent<Camera>();
        hudCam.clearFlags = CameraClearFlags.Depth;
        hudCam.cullingMask = 1 << HudSprites.UiLayer;
        hudCam.fieldOfView = cam.fieldOfView;
        hudCam.nearClipPlane = 0.05f;
        hudCam.farClipPlane = 100f;
        hudCam.depth = cam.depth + 10f;
        hudCam.allowHDR = false;

        var tex = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = tex;
        hudCam.targetTexture = tex;

        var canvases = new[]
        {
            boot.hud != null ? boot.hud.Canvas : null,
            boot.encounterHud != null ? boot.encounterHud.Canvas : null
        };
        var prevMode = new RenderMode[canvases.Length];
        var prevCam = new Camera[canvases.Length];
        var prevPlane = new float[canvases.Length];

        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null) continue;
            prevMode[i] = c.renderMode;
            prevCam[i] = c.worldCamera;
            prevPlane[i] = c.planeDistance;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = hudCam;
            // The encounter overlay must composite ON TOP of the instrument panel, so it sits
            // marginally closer to the UI camera as well as higher in sortingOrder.
            c.planeDistance = i == 0 ? 1f : 0.9f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
        Canvas.ForceUpdateCanvases();
        if (boot.hud != null && boot.hud.map != null) boot.hud.map.Refresh(Dt);
        Canvas.ForceUpdateCanvases();

        cam.Render();
        hudCam.Render();

        var prevActive = RenderTexture.active;
        RenderTexture.active = tex;
        var img = new Texture2D(W, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prevActive;

        cam.targetTexture = null;
        hudCam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);

        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];
            if (c == null) continue;
            c.renderMode = prevMode[i];
            c.worldCamera = prevCam[i];
            c.planeDistance = prevPlane[i];
        }
    }
}

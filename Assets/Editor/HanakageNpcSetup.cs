using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages Hanakage - "The Blossom Climber", the Sakura Pass legendary encounter rider - into
/// the SakuraPass scene, and captures the renders that prove she reads correctly.
///
/// She is NOT staged by <see cref="SakuraNpcRoster"/>, and that is deliberate. The roster's
/// <c>Stage()</c> hard-codes three things that are all wrong for an encounter rider: an
/// <see cref="NPCCyclist"/> walking a REVERSED copy of the road centreline, an
/// <see cref="NpcGreeting"/> view cone, and a fixed speed. Hanakage rides the SAME direction as
/// the player, on the same <see cref="RouteCourse"/> arc the ride session uses, at a speed the
/// encounter state machine sets from the player's real watts - so she carries
/// <see cref="HanakageRider"/> instead. Everything else about her build is the roster's proven
/// recipe, reused verbatim rather than re-derived.
///
/// What is reused unchanged (see SakuraNpcRoster's remarks for why each number is what it is):
///   * Coral's sculpt, the only full-resolution chibi cyclist in the project (109,358 tris);
///   * the shared Colnago, at bike scale 0.9 - her baked hand grip was measured against THIS
///     frame at THIS scale, and any other value leaves her clutching thin air;
///   * her verified road-bike lean (hip 24, spine 34, neck -10, head -50);
///   * a CLONED kit material and CLONED bike materials, never the shared imported ones.
///
/// What is deliberately NOT here, and is tracked as the P1 character-behaviour milestone in
/// reference/improve/to_implement.md section 19:
///   * the petal-shaped Hanakage helmet and the flowing hair/ribbon rear silhouette - both are
///     GEOMETRY and cannot come from recolouring an atlas;
///   * the seated-climb / look-back / standing-attack animations.
/// For the P0 functional prototype her distance readability comes from colour and value alone:
/// a near-white kit and silver hair over a near-white frame, against a cast in which every
/// other rider is saturated and every other bike is dark.
///
/// Menu: MapleRide/Hanakage/Stage Hanakage
///       MapleRide/Hanakage/Capture Hanakage Verification
/// </summary>
public static class HanakageNpcSetup
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    /// <summary>
    /// HER OWN rig, not Coral's. `build_hanakage_look.py` copies Coral's rigged sculpt - grip
    /// and smile decal intact - and adds `HanakageHelm` and `HanakageHair`, both rigid-skinned
    /// 100 % to the Head bone. Writing a separate GLB is what keeps Coral and the nine roster
    /// riders who share her file completely untouched by Hanakage's silhouette work.
    /// </summary>
    const string RigPath = "Assets/Kuro/NPC/KuroNPC_Hanakage_Rigged.glb";
    const string FallbackRigPath = "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb";
    const string RibbonPath = "Assets/Kuro/NPC/Hanakage_Ribbons.glb";
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;
    const string KitPath = "Assets/Kuro/NPC/Textures/CoralKit_Hanakage.png";
    const string MaterialDir = "Assets/Kuro/NPC/Materials";

    /// <summary>Exact object name - idempotent match-and-prune depends on it.</summary>
    public const string ObjectName = HanakageRider.ObjectName;

    // ---- Coral's verified numbers, reused unchanged --------------------------------------

    const float BikeScale = NpcCanonicalConformance.BikeScale;
    /// <summary>Measured seated height of the shared sculpt at scale 1, from a BAKED mesh.</summary>
    const float SculptSeatedHeight = 1.185f;

    /// <summary>
    /// PROVISIONAL: her seated height in metres. A shade under Kuro's 1.384 m so she reads as
    /// slighter next to him without becoming a different size class - the design calls for
    /// "very compact MapleRide chibi proportions", not a smaller character.
    /// </summary>
    const float TargetHeight = 1.37f;

    /// <summary>
    /// PROVISIONAL: her lane, metres right of the centreline. The player rides at -1.70
    /// (<see cref="RouteFollower.laneOffset"/>); sitting her 0.45 m further left means "on her
    /// wheel" is directly behind and slightly inside, which keeps both sculpts separate on
    /// screen instead of intersecting.
    /// </summary>
    const float LaneOffset = -2.6f;

    /// <summary>
    /// PROVISIONAL: where she is parked in the SAVED scene, in arc metres along the course.
    /// This is the encounter's default sighting position (zone entry 140 m + 120 m ahead); at
    /// run time the encounter state machine owns her arc distance entirely.
    /// </summary>
    public const float StagedArcMetres = 260f;

    /// <summary>
    /// PROVISIONAL: the course the encounter is authored against - the pure Sakura Pass climb
    /// and descent, so an arc metre here is an arc metre on the <c>pass</c> segment. The
    /// encounter itself accepts any course whose ride begins on that segment.
    /// </summary>
    public const string EncounterCourseId = "pass_sprint";

    // ---- livery ---------------------------------------------------------------------------
    //
    // Value-lifted near-white frame with a saturated sakura accent. Two rules from the NPC
    // skill's staging reference drive this: lead with the character's SECONDARY colour (her kit
    // is already near-white, so the bike leads with white and the pink is the accent that sells
    // it), and never let a frame colour sink toward the asphalt's value - which here means the
    // opposite problem from Coral's, so the white is held at 0.93 rather than pure 1.0 so the
    // sunset grade has somewhere to go before it blows out.
    const string FrameHex = "EDE6EC";
    const string AccentHex = "EF7FB3";
    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    static float RigScale => NpcCanonicalConformance.RigScaleForAnyProductionNpc();

    // ======================================================================= staging

    [MenuItem("MapleRide/Hanakage/Stage Hanakage", priority = 60)]
    public static void AddToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var go = Stage();
        if (go == null) return;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    /// <summary>Builds (or rebuilds) Hanakage in the open scene. Idempotent.</summary>
    public static GameObject Stage()
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");
        PurgeOwnMaterials();

        var root = GameObject.Find("NPCs") ?? new GameObject("NPCs");
        Prune(root.transform, ObjectName);

        var rigAsset = LoadPrefab(RigPath);
        if (rigAsset == null)
        {
            Debug.LogWarning($"[hanakage] {RigPath} is missing - run " +
                             "'blender -b -P assets/3d/kuro/build_hanakage_look.py'. Falling " +
                             "back to the shared sculpt: she will have no helm, no hair fall " +
                             "and no ribbons, and will not read as a legendary rider.");
            rigAsset = LoadPrefab(FallbackRigPath);
        }
        var bikeAsset = LoadPrefab(BikePath);
        if (rigAsset == null) { Debug.LogError($"[hanakage] missing rig {RigPath}"); return null; }
        if (bikeAsset == null) { Debug.LogError($"[hanakage] missing bike {BikePath}"); return null; }

        var npc = new GameObject(ObjectName);
        npc.transform.SetParent(root.transform, false);

        // Dedicated visual root, so HanakageRider.SetPresent can remove her from the world
        // without disabling the component the encounter holds a reference to.
        var rider = new GameObject("HanakageRider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * RigScale;

        // MUST be named exactly "Bike", nested under the rider. KuroBikeRig.Setup looks for a
        // local child by that name and has no global fallback - twelve objects in this scene
        // are called "Bike" and a global lookup is a lottery that can hand her the player's
        // drivetrain.
        var bikeAnchor = new GameObject("Bike");
        bikeAnchor.transform.SetParent(rider.transform, false);
        bikeAnchor.transform.localScale = Vector3.one * BikeScale;
        var bikeModel = Instantiate(bikeAsset, bikeAnchor.transform);
        bikeModel.name = "BikeMesh";
        RepaintBike(bikeModel);

        var body = Instantiate(rigAsset, rider.transform);
        body.name = "HanakageArmatureAndMesh";
        ApplyKit(body);

        var rig = rider.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        // DriveDrivetrain divides WORLD distance by this, so BOTH scales have to be folded in
        // or her wheels spin at the wrong rate for the speed she is travelling.
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        // Zero, not 55: a preview cadence spins the cranks whenever travelled distance is ~0,
        // which for an encounter rider who is deliberately stationary before she spawns would
        // show her pedalling in mid-air off the side of the road.
        rig.previewCadenceRpm = 0f;
        NpcCanonicalConformance.Configure(rig);

        var follower = npc.AddComponent<HanakageRider>();
        follower.rider = npc.transform;
        follower.visualRoot = rider;
        follower.laneOffset = LaneOffset;
        follower.arcDistanceM = StagedArcMetres;
        follower.session = Object.FindFirstObjectByType<RideSession>();
        if (follower.session == null)
            Debug.LogWarning("[hanakage] no RideSession in the scene - the encounter cannot " +
                             "place her until one exists.");
        EditorUtility.SetDirty(follower);

        // After the follower exists, so the ribbons can read her speed for their airflow trail.
        AttachRibbons(body, npc);
        AttachPetalBurst(body, rider.transform);

        // No NpcGreeting: she travels the same direction as the player and therefore never has
        // him inside a view cone, and the design explicitly forbids dialogue before "Still
        // here?". Her two lines are driven by the encounter state machine instead.
        // No KuroOutline either - it is an inverted-hull pass, which produces no silhouette at
        // all on a SKINNED mesh while duplicating 109k triangles. See SakuraNpcRoster.
        SetSmileNeutral(npc);

        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        SakuraNpcRoster.FreezeSkinnedBounds(npc);

        // She is ABSENT in the saved scene, and that is the design, not a shortcut: section 3
        // of the handoff has her spawn only after an eligible run rolls her in. Leaving her
        // switched on would also put a legendary rider permanently on the roadside of every
        // ride, which is precisely the "road filled with static MMO spawn points" the project
        // context tells us to avoid.
        follower.SetPresent(false);

        Debug.Log($"[hanakage] staged absent, preview arc {StagedArcMetres:0} m, " +
                  $"lane {LaneOffset:0.00}, rig scale {RigScale:0.000}");
        return npc;
    }

    // ======================================================================= performance shots

    /// <summary>
    /// Captures the two PHYSICAL beats of the encounter as still frames.
    ///
    /// These exist because the encounter capture shoots from the player's chase camera, where
    /// Hanakage is 2-3 m away and about 90 px tall - large enough to prove the beat fired,
    /// far too small to prove the head actually turned or that she is genuinely out of the
    /// saddle. A pose driver that writes plausible numbers into fields nothing consumes would
    /// pass every log check in this project and still render a rider sitting bolt upright, so
    /// the only honest verification is a close shot of the pose itself.
    /// </summary>
    static void CapturePerformance(GameObject npc, string dir)
    {
        var perf = npc.GetComponent<HanakagePerformance>();
        if (perf == null)
        {
            Debug.LogWarning("[hanakage] no HanakagePerformance - run " +
                             "MapleRide/Hanakage/Stage Encounter, then capture again.");
            return;
        }

        var follower = npc.GetComponent<HanakageRider>();
        if (follower != null) follower.speedMps = 3.1f;   // her climbing speed, for the ribbons

        var head = FindChild(npc.transform, "Head");
        Vector3 eye = head != null ? head.position : npc.transform.position + Vector3.up * 1.1f;

        // Three-quarter rear-left: the angle the player spends the whole encounter at, and the
        // side she glances over. A head-on shot would hide the turn entirely.
        // Distance matters here: at 1.9 m the chibi head simply fills the frame and the yaw is
        // unreadable, which is how the first capture produced a "62.0 deg" log line over a
        // picture of the back of a helmet.
        Vector3 camPos = eye - npc.transform.forward * 3.4f
                             - npc.transform.right * 1.9f + Vector3.up * 0.55f;
        Vector3 aim = npc.transform.position + Vector3.up * 0.95f;

        // --- neutral control -------------------------------------------
        // Shot from the SAME camera, so the look-back frame can be judged against something
        // rather than against a memory of what her head normally does.
        perf.ClearPose();
        StepPerformance(npc, perf, 1);
        RenderPosedShot(npc, camPos, aim, Path.Combine(dir, "hanakage_pose_neutral.png"));
        var helmBake0 = BakeProbe(npc, "HanakageHelm");

        // --- look back -------------------------------------------------
        // Advanced to the middle of the HOLD, not to the moment the phase changed: the turn
        // takes lookBackTurnSeconds and a frame shot at t=0 shows a rider looking straight on.
        perf.ClearPose();
        perf.ForceLookBack(perf.lookBackTurnSeconds + perf.lookBackHoldSeconds * 0.5f);
        StepPerformance(npc, perf, 1);
        RenderPosedShot(npc, camPos, aim, Path.Combine(dir, "hanakage_lookback.png"));
        var headBone = FindChild(npc.transform, "Head");
        Debug.Log($"[hanakage] lookback shot: yaw {perf.rig.poseHeadYawDegrees:0.0} deg, " +
                  $"roll {perf.rig.poseHeadRollDegrees:0.0} deg, " +
                  $"ribbon gust {(perf.ribbon != null ? perf.ribbon.gust : 0f):0.00}, " +
                  $"HEAD BONE local euler {(headBone != null ? headBone.localEulerAngles.ToString("F1") : "NULL")}, " +
                  $"rig obj '{perf.rig.gameObject.name}'");
        var helmBake1 = BakeProbe(npc, "HanakageHelm");
        Debug.Log($"[hanakage] helm skin probe: neutral {helmBake0:F4} -> lookback {helmBake1:F4} " +
                  $"(delta {Vector3.Distance(helmBake0, helmBake1) * 1000f:0.0} mm). " +
                  "A delta near zero means the helm is NOT following the Head bone.");

        // --- standing attack -------------------------------------------
        // Stepped for enough frames that the stand blend reaches 1 and the rock is off zero;
        // a single step would shoot her mid-blend, half out of the saddle.
        perf.ClearPose();
        perf.ForceStanding(perf.standAttackSeconds);
        StepPerformance(npc, perf, Mathf.CeilToInt(perf.standBlendSeconds / PerfStepDt) + 8);
        RenderPosedShot(npc, camPos, aim, Path.Combine(dir, "hanakage_standing.png"));
        Debug.Log($"[hanakage] standing shot: rise {perf.rig.poseStandRiseM * 1000f:0} mm, " +
                  $"forward {perf.rig.poseStandForwardM * 1000f:0} mm, " +
                  $"rock {perf.rig.poseStandRockDegrees:0.0} deg, " +
                  $"spine {perf.rig.poseExtraSpineLeanDegrees:0.0} deg");

        // --- petal burst ------------------------------------------------
        // The burst is a ParticleSystem, and a ParticleSystem outside play mode has no update
        // either: Emit() alone puts particles in the buffer but nothing ages or draws them, so
        // the shot came back empty. Simulate(withChildren:true, restart:false) advances the
        // system by hand, which is the only way to photograph it from batchmode.
        var burst = FindBurstSystem(npc);
        if (burst != null)
        {
            perf.ClearPose();
            perf.ForceStanding(perf.standAttackSeconds);
            StepPerformance(npc, perf, Mathf.CeilToInt(perf.standBlendSeconds / PerfStepDt) + 8);

            burst.Clear(true);
            burst.Simulate(0f, true, true);
            burst.Emit(perf.burstCount);
            // PROVISIONAL: 0.45 s is roughly where the cone has opened but the petals are still
            // clustered near her shoulders - the frame that reads as "she just kicked".
            burst.Simulate(0.45f, true, false);

            // Pulled back and up: the burst is metres wide and the close portrait camera
            // crops every petal out of frame.
            Vector3 burstCam = eye - npc.transform.forward * 5.0f
                                   - npc.transform.right * 2.4f + Vector3.up * 1.1f;
            RenderPosedShot(npc, burstCam, aim, Path.Combine(dir, "hanakage_petal_burst.png"));
            Debug.Log($"[hanakage] petal burst shot: {burst.particleCount} live particles " +
                      $"after Emit({perf.burstCount}) + Simulate(0.45).");
            burst.Clear(true);
        }
        else
        {
            Debug.LogWarning("[hanakage] no petal burst found - skipping the burst shot.");
        }

        perf.ClearPose();
        StepPerformance(npc, perf, 1);
    }

    static ParticleSystem FindBurstSystem(GameObject npc)
    {
        foreach (var ps in npc.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.gameObject.name == HanakagePerformance.BurstName) return ps;
        return null;
    }

    /// <summary>
    /// Replaces her skinned renderers with STATIC bakes of the pose they are in right now.
    ///
    /// Outside play mode there is no player loop, and a SkinnedMeshRenderer will happily render
    /// the pose it was last evaluated at however far its bones have since moved. This was not a
    /// theory: the Head bone reported a 61 deg yaw, a BakeMesh probe measured the helm moving
    /// 102 mm, updateWhenOffscreen was forced on - and three successive captures came back with
    /// a pixel-identical helmet. Bones right, numbers right, picture wrong.
    ///
    /// BakeMesh is the same call the probe uses, so what gets rendered is provably the current
    /// pose. Returns the temporaries for <see cref="EndBakedCapture"/> to clean up; leaving one
    /// behind would put a second frozen Hanakage in the saved scene.
    /// </summary>
    internal static List<GameObject> BeginBakedCapture(GameObject npc)
    {
        var temps = new List<GameObject>();
        foreach (var smr in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var baked = new Mesh { name = smr.name + "_BakedPose" };
            smr.BakeMesh(baked, false);

            var go = new GameObject("~BakedPose_" + smr.name);
            go.transform.SetParent(smr.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = smr.sharedMaterials;
            mr.shadowCastingMode = smr.shadowCastingMode;
            mr.receiveShadows = smr.receiveShadows;

            smr.enabled = false;
            temps.Add(go);
        }
        return temps;
    }

    /// <summary>Removes the bakes and switches the skinned renderers back on.</summary>
    internal static void EndBakedCapture(GameObject npc, List<GameObject> temps)
    {
        foreach (var smr in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.enabled = true;
        foreach (var t in temps)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Object.DestroyImmediate(mf.sharedMesh);
            Object.DestroyImmediate(t);
        }
    }

    /// <summary>Poses, bakes, shoots and cleans up in one step.</summary>
    static void RenderPosedShot(GameObject npc, Vector3 camPos, Vector3 aim, string path)
    {
        var temps = BeginBakedCapture(npc);
        RenderShot(camPos, aim, 34f, path, 1100, 850);
        EndBakedCapture(npc, temps);
    }

    /// <summary>
    /// Bakes a named skinned renderer at its CURRENT pose and returns one representative vertex
    /// in world space. Used to tell "the bone moved but the render is stale" apart from "the
    /// mesh is not bound to that bone at all" - two failures that look identical on screen.
    /// </summary>
    static Vector3 BakeProbe(GameObject npc, string rendererName)
    {
        foreach (var smr in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name != rendererName) continue;
            var m = new Mesh();
            smr.BakeMesh(m, true);
            var v = m.vertexCount > 0 ? m.vertices[0] : Vector3.zero;
            Object.DestroyImmediate(m);
            return smr.transform.TransformPoint(v);
        }
        return Vector3.zero;
    }

    /// <summary>Fixed capture timestep. Matches the encounter harness so poses land the same.</summary>
    const float PerfStepDt = 1f / 60f;

    /// <summary>
    /// Drives driver -> rig -> ribbon for n frames. The order is the same load-bearing order
    /// the encounter capture uses: the rig rewrites hips/spine/neck/head from the bind pose
    /// every LateUpdate, so modifiers written after it are simply discarded.
    /// </summary>
    static void StepPerformance(GameObject npc, HanakagePerformance perf, int frames)
    {
        var rig = npc.GetComponentInChildren<KuroBikeRig>(true);
        var ribbon = npc.GetComponentInChildren<HanakageRibbon>(true);
        for (int i = 0; i < frames; i++)
        {
            perf.Tick(PerfStepDt);
            if (rig != null) rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
            if (ribbon != null) ribbon.Tick(PerfStepDt);
        }
        RefreshSkins(npc);
    }

    /// <summary>
    /// Forces every skinned renderer under her to re-skin before the next manual Camera.Render.
    ///
    /// Outside play mode there is no player loop, so a SkinnedMeshRenderer can happily render
    /// the pose it was last evaluated at even though its bones have since moved. That is
    /// exactly what happened here: the Head bone reported a 61 deg yaw, a BakeMesh probe
    /// measured the helm moving 102 mm, and three successive renders came back pixel-identical
    /// across the helmet. The bones were right, the numbers were right, and the picture was
    /// wrong - which is the whole reason this project verifies by looking.
    ///
    /// Toggling updateWhenOffscreen forces the bounds (and therefore the skin) to be recomputed
    /// from the current bone matrices. SakuraNpcRoster.FreezeSkinnedBounds deliberately leaves
    /// it off for runtime cost, so this restores it afterwards.
    /// </summary>
    static void RefreshSkins(GameObject npc)
    {
        foreach (var smr in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            bool was = smr.updateWhenOffscreen;
            smr.updateWhenOffscreen = true;
            smr.updateWhenOffscreen = was;
            smr.updateWhenOffscreen = true;   // left on for the duration of the capture
        }
    }

    // ======================================================================= petal burst
    /// <summary>PROVISIONAL: burst origin in unscaled rig-local metres, relative to the rider.</summary>
    static readonly Vector3 BurstOffset = new Vector3(0f, 1.05f, -0.10f);

    /// <summary>
    /// Builds the one-shot petal emitter that fires when she attacks (handoff section 19, P1).
    ///
    /// It reuses the SAME petal mesh and material as the ambient `Sakura Petal Drift` system
    /// the environment pass builds, found by EXACT name in the scene. That is not just thrift:
    /// a burst authored with its own quad and its own pink would read as a different substance
    /// from the petals already in the air, and the beat only works if what she kicks up is
    /// visibly the blossom the pass is full of.
    ///
    /// Emission rate is zero. Every particle it ever shows comes from an explicit
    /// <c>Emit(n)</c> in <see cref="HanakagePerformance"/>, so an idle Hanakage cannot trail
    /// petals around the mountain like a boss aura.
    /// </summary>
    static void AttachPetalBurst(GameObject body, Transform rider)
    {
        Prune(rider, HanakagePerformance.BurstName);

        var go = new GameObject(HanakagePerformance.BurstName);
        go.transform.SetParent(rider, false);
        go.transform.localPosition = BurstOffset;
        go.transform.localRotation = Quaternion.identity;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.84f, 0.90f, 1f), new Color(1f, 0.60f, 0.75f, 1f));
        main.gravityModifier = 0.06f;
        main.maxParticles = 120;
        // World space, or the whole burst would be dragged along behind her at 11 kph and would
        // never actually fall away - the one thing a wake has to do.
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;
        shape.radius = 0.12f;
        // Thrown BACKWARDS out of her wake: the rider's -Z.
        shape.rotation = new Vector3(0f, 180f, 0f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        // All three axes MUST be in the same curve mode. Setting only y left x and z as single
        // constants, and Unity threw "Particle Velocity curves must all be in the same mode" on
        // every single Emit at runtime - an error that never appeared in the editor capture
        // because nothing was simulating. A small drift on x/z also stops the burst reading as
        // a flat vertical curtain.
        vel.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.9f, 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.30f, 0.30f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-3.0f, 3.0f);
        rot.y = new ParticleSystem.MinMaxCurve(-2.0f, 2.0f);   // same-mode rule as above
        rot.z = new ParticleSystem.MinMaxCurve(-4.0f, 4.0f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.alignment = ParticleSystemRenderSpace.World;

        // Include INACTIVE: the ambient drift system is switched off in the saved scene (the
        // region director turns it on), and GameObject.Find silently skips inactive objects -
        // which is exactly how the first run of this pass produced a burst with a null mesh
        // while still logging success.
        ParticleSystemRenderer src = null;
        foreach (var psr in Object.FindObjectsByType<ParticleSystemRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (psr.gameObject.name == "Sakura Petal Drift") { src = psr; break; }
        }
        if (src != null)
        {
            r.mesh = src.mesh;
            r.sharedMaterial = src.sharedMaterial;
        }
        else
        {
            Debug.LogWarning("[hanakage] no 'Sakura Petal Drift' in the scene - the burst has " +
                             "no petal mesh/material and will render as nothing. Run the " +
                             "environment pass, then restage her.");
        }

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        EditorUtility.SetDirty(go);
        Debug.Log($"[hanakage] petal burst attached at local {BurstOffset}, " +
                  $"mesh {(r.mesh != null ? r.mesh.name : "MISSING")}.");
    }

    // ======================================================================= ribbons

    /// <summary>
    /// PROVISIONAL: where the ribbons tie in, in unscaled rig-local metres relative to the Head
    /// bone. Behind and a little above the nape, under the helm's rear petal, per the character
    /// sheet's HAIR REFERENCE panel.    /// </summary>
    static readonly Vector3 RibbonOffset = new Vector3(0f, 0.045f, -0.075f);

    /// <summary>
    /// PROVISIONAL: initial droop of the ribbon chain, degrees.
    /// NOTE: an earlier guess put 180 deg on Y to "compensate" for the Blender +Y -> Unity -Z
    /// length-axis flip. The render proved that wrong - the streamers flew FORWARD out of her
    /// chest. The glTF importer already resolves the axis, so no yaw compensation is correct.
    /// QA fix: raised from 18 deg to 34 deg. With the wider/longer hair mass now covering most
    /// of the rear arc, a shallow droop kept the whole ribbon chain running parallel to, and
    /// therefore hidden inside, the hair volume - the streamers rendered as fully invisible
    /// (not merely mis-aimed). A steeper downward droop, combined with lengthening the chain
    /// itself in build_hanakage_look.py, lets the ribbons fall clear of the hair and read as a
    /// distinct trailing element below it, matching the reference sheet.
    /// </summary>
    static readonly Vector3 RibbonTilt = new Vector3(34f, 0f, 0f);

    /// <summary>
    /// Hangs the ribbon chain off the Head bone and gives it its secondary motion.
    ///
    /// The chain is a separate GLB and is parented in the SCENE rather than skinned, because
    /// `HanakageRibbon` has to be able to rotate each segment - a skinned ribbon cannot move
    /// independently of the bone it is bound to, which is the whole point of the ribbons.
    ///
    /// Attached under the Head BONE, not under the rig root: her look-back is a head rotation,
    /// and the design's most important single frame is the moment she turns and the ribbons
    /// sweep across her shoulder.
    /// </summary>
    static void AttachRibbons(GameObject body, GameObject npc)
    {
        var ribbonAsset = LoadPrefab(RibbonPath);
        if (ribbonAsset == null)
        {
            Debug.LogWarning($"[hanakage] no ribbons at {RibbonPath} - run " +
                             "'blender -b -P assets/3d/kuro/build_hanakage_look.py'.");
            return;
        }

        var head = FindChild(body.transform, "Head");
        if (head == null)
        {
            Debug.LogWarning("[hanakage] no 'Head' bone under the rig - ribbons not attached.");
            return;
        }

        // Idempotent: a re-stage must not leave two sets of streamers on her head.
        Prune(head, HanakageRibbon.RootName);

        var root = Instantiate(ribbonAsset, head);
        root.name = HanakageRibbon.RootName;

        var rider = npc.transform.childCount > 0 ? npc.transform.GetChild(0) : npc.transform;
        root.transform.position = head.position + rider.TransformVector(RibbonOffset);
        root.transform.rotation = rider.rotation * Quaternion.Euler(RibbonTilt);

        var motion = root.GetComponent<HanakageRibbon>() ?? root.AddComponent<HanakageRibbon>();
        motion.rider = npc.GetComponent<HanakageRider>();
        motion.Rebind();
        EditorUtility.SetDirty(motion);

        int segments = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) segments++;
        Debug.Log($"[hanakage] ribbons attached to the Head bone: {segments} segments, " +
                  $"offset {RibbonOffset}, tilt {RibbonTilt}.");
    }

    // ======================================================================= verification

    /// <summary>
    /// Renders Hanakage from the exact viewpoints the design's emotional sequence depends on:
    /// the 120 m sighting, the approach, the wheel, and a close three-quarter.
    ///
    /// The sighting frame is the one that matters most and is the one a metric cannot judge:
    /// section 4 of the handoff asks for "a tiny white cyclist" that an experienced player will
    /// eventually recognise by silhouette before any UI confirms it. Either that reads at 120 m
    /// through the sunset grade or it does not, and only looking can tell.
    /// </summary>
    [MenuItem("MapleRide/Hanakage/Capture Hanakage Verification", priority = 61)]
    public static void CaptureVerification()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var npc = GameObject.Find(ObjectName);
        if (npc == null) { Debug.LogError("[hanakage] not staged - run Stage Hanakage first."); return; }
        var follower = npc.GetComponent<HanakageRider>();
        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[hanakage] no RideBootstrap in the scene."); return; }
        boot.Resolve();
        var session = boot.session;
        session.Graph = null;
        session.EnsureCourse();
        if (follower.session == null) follower.session = session;

        // The encounter is a SAKURA PASS event, and the scene's session can be parked on any
        // region's course (it was on Shiosai Coast the first time this ran, which put her 3 km
        // out on the coast road). Fast travel rather than SelectCourse: it also switches the
        // environment roots and the whole time-of-day grade, so these frames are lit the way
        // the pass is actually lit rather than by the last region visited.
        if (boot.regions != null) boot.regions.FastTravel(RegionCatalog.SakuraPass);
        // The pure pass climb+descent, so arc metres here are arc metres on the pass segment.
        session.autoLapsFromTarget = true;
        session.SelectCourse(EncounterCourseId);
        if (boot.regions != null) boot.regions.SyncFromSession();
        session.ResetRide();
        follower.SetPresent(true);

        Debug.Log($"[hanakage] capturing on '{session.Course.DisplayName}' " +
                  $"({session.Course.Length:0} m), region " +
                  $"{(boot.regions != null ? boot.regions.currentRegionId : "?")}");

        // Wide static frames: a streamed world renders as an empty one.
        var streamer = Object.FindFirstObjectByType<RouteDressingStreamer>();
        if (streamer != null) streamer.ShowAll();

        string dir = MapleRidePaths.RenderDir("hanakage");
        Directory.CreateDirectory(dir);

        // (label, player arc m, gap m). Gaps are the design's own progression from section 4.
        var shots = new (string name, float playerArc, float gap)[]
        {
            ("sighting_120m", 140f, 120f),
            ("approach_40m",  220f,  40f),
            ("onwheel_7m",    300f,   7f),
            ("attack_11m",    360f,  11f),
        };

        foreach (var s in shots)
        {
            session.SeekTo(s.playerArc);
            if (boot.follower != null) { boot.follower.enabled = true; boot.follower.Apply(); }
            follower.arcDistanceM = s.playerArc + s.gap;
            follower.speedMps = 4.4f;
            follower.Apply();
            PoseRig(npc);
            if (streamer != null) streamer.ShowAll();

            RenderFromRider(session, Path.Combine(dir, "hanakage_" + s.name + ".png"));
            Debug.Log($"[hanakage] {s.name}: player {s.playerArc:0} m, her {follower.arcDistanceM:0} m, " +
                      $"gap {s.gap:0} m, grade {session.Course.GradeAt(s.playerArc, 8f) * 100f:0.0} %, " +
                      $"her world pos {npc.transform.position:F1}");
        }

        // Close three-quarter rear, the angle the player actually spends the encounter looking
        // at, plus a head-on so her face and kit can be judged.
        var head = FindChild(npc.transform, "Head");
        Vector3 eye = head != null ? head.position : npc.transform.position + Vector3.up * 1.1f;
        var portraits = new (string name, Vector3 pos, Vector3 look, float fov)[]
        {
            ("portrait_rear",  eye - npc.transform.forward * 2.2f + npc.transform.right * 0.9f
                                   + Vector3.up * 0.45f, npc.transform.position + Vector3.up * 0.7f, 38f),
            ("portrait_front", eye + npc.transform.forward * 2.4f + Vector3.up * 0.25f, eye, 38f),
            ("portrait_side",  npc.transform.position + npc.transform.right * 2.6f + Vector3.up * 0.85f,
                               npc.transform.position + Vector3.up * 0.65f, 38f),
        };
        foreach (var p in portraits)
            RenderShot(p.pos, p.look, p.fov, Path.Combine(dir, "hanakage_" + p.name + ".png"), 1100, 850);

        CapturePerformance(npc, dir);

        // Side by side with Yuki, the only other pale rider in the cast. If those two are not
        // instantly separable the palette has failed, and no number in the recolour script
        // would have said so.
        var yuki = GameObject.Find("Sakura NPC Yuki");
        if (yuki != null)
        {
            // Yuki lives 930 m up the pass on the oncoming side, so she has to be brought here
            // for the shot. This pass ends in DiscardChanges, which reopens the scene from
            // disk, so moving her is temporary by construction.
            var hp = npc.transform.position;
            var hf = npc.transform.forward;
            var hr = npc.transform.right;
            // Offset ALONG the axis they face, for a profile pair. Offsetting ACROSS it lines
            // them up one behind the other - exactly how the first Coral-vs-Kuro comparison in
            // this project came out useless.
            yuki.transform.SetPositionAndRotation(hp + hf * 3.0f, npc.transform.rotation);
            var yrig = yuki.GetComponentInChildren<CoralBikeRig>();
            if (yrig != null)
            {
                yrig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
                yrig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
            }
            RenderShot(hp + hf * 1.5f + hr * 7.5f + Vector3.up * 1.2f,
                       hp + hf * 1.5f + Vector3.up * 0.75f, 40f,
                       Path.Combine(dir, "hanakage_vs_yuki.png"), 1400, 800);
        }
        else Debug.LogWarning("[hanakage] Yuki not staged - skipping the pale-rider pair shot.");

        // Leave the scene exactly as it was found: this pass is a renderer, not an edit.
        Debug.Log("[hanakage] captures written to " + dir);
        MapleRideSceneBootstrap.DiscardChanges();
    }

    /// <summary>Re-seats the rider so a staged pose is current before a frame is rendered.</summary>
    static void PoseRig(GameObject npc)
    {
        var rig = npc.GetComponentInChildren<CoralBikeRig>();
        if (rig == null) return;
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
    }

    /// <summary>The player's actual eye line: behind and slightly inside, same as the GPS capture.</summary>
    static void RenderFromRider(RideSession session, string path)
    {
        var course = session.Course;
        float d = session.DistanceM;
        var p = course.PositionAt(d);
        var t = course.TangentAt(d);
        var s = course.SideAt(d);
        RenderShot(p + Vector3.up * 3.1f - t * 7.2f - s * 1.1f,
                   p + t * 9f + Vector3.up * 1.3f, 55f, path, 1920, 1080);
    }

    /// <summary>
    /// HDR + the scene's sunset grade, always. A bare camera renders these characters blown out
    /// to near-white, which on a rider whose whole identity IS white would hide the exact thing
    /// the capture exists to check.
    /// </summary>
    static void RenderShot(Vector3 pos, Vector3 look, float fov, string path, int w, int h)
    {
        var go = new GameObject("~HanakageCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        go.AddComponent<SakuraPostFX>();
        SakuraPassEnvironment.TunePostFX(go.GetComponent<SakuraPostFX>());

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }

    // ======================================================================= materials

    /// <summary>
    /// Deletes only Hanakage's previously generated materials.
    ///
    /// SaveMaterial uses GenerateUniqueAssetPath, so without this a second staging run writes
    /// "Hanakage_Body 1.mat", a third "Hanakage_Body 2.mat", and the folder grows without bound
    /// while the scene silently references whichever copy was newest.
    /// </summary>
    static void PurgeOwnMaterials()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var file = Path.GetFileNameWithoutExtension(path);
            if (file.StartsWith("Hanakage_")) AssetDatabase.DeleteAsset(path);
        }
    }

    /// <summary>
    /// Swaps in her recoloured kit atlas on a CLONED material. The sculpt is shared by the whole
    /// cast and its material is shared BY REFERENCE - retexturing in place would repaint every
    /// rider in the scene, Coral included.
    /// </summary>
    static void ApplyKit(GameObject body)
    {
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(KitPath);
        if (kit == null)
        {
            Debug.LogError($"[hanakage] no kit texture at {KitPath} - run " +
                           "'python assets/3d/kuro/npc_palette_variants.py Hanakage'. She will " +
                           "wear Coral's colours and will not read as a legendary rider.");
            return;
        }

        Material clone = null;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                // Body atlas only - the smile decal keeps its own material, or her mouth would
                // be painted with a full-body texture.
                if (!mats[i].name.StartsWith("Material_")) continue;
                if (clone == null)
                {
                    clone = new Material(mats[i]) { name = "Hanakage_Body" };
                    SetTexture(clone, kit);
                    SaveMaterial(clone);
                }
                mats[i] = clone;
                touched = true;
            }
            if (touched) { renderer.sharedMaterials = mats; EditorUtility.SetDirty(renderer); }
        }
        if (clone == null)
            Debug.LogWarning("[hanakage] no 'Material_*' slot found on the sculpt - kit not applied.");
    }

    /// <summary>
    /// One clone per source material, cached by name. The Colnago is ~78 parts sharing two
    /// materials; cloning per renderer produced 34 redundant assets per rider on the roster.
    /// Brushed_Metal and Road_Tyre are left alone - spokes, chain and tyres stay metal and
    /// rubber on anyone's bicycle.
    /// </summary>
    static void RepaintBike(GameObject bike)
    {
        var frame = Hex(FrameHex);
        var accent = Hex(AccentHex);
        var clones = new Dictionary<string, Material>();

        foreach (var renderer in bike.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string src = mats[i].name.Replace(" (Instance)", "");
                Color? tint = FrameMaterials.Contains(src) ? frame
                            : AccentMaterials.Contains(src) ? accent
                            : (Color?)null;
                if (tint == null) continue;

                if (!clones.TryGetValue(src, out var clone))
                {
                    clone = new Material(mats[i]) { name = $"Hanakage_{src}" };
                    SetColor(clone, tint.Value);
                    SaveMaterial(clone);
                    clones[src] = clone;
                }
                mats[i] = clone;
                touched = true;
            }
            if (touched) { renderer.sharedMaterials = mats; EditorUtility.SetDirty(renderer); }
        }
    }

    // glTFast materials do not use the built-in _Color/_MainTex names, and which properties
    // exist depends on the import path, so set every variant that is actually present.
    static readonly string[] TextureProps = { "baseColorTexture", "_BaseMap", "_MainTex", "_BaseColorMap" };
    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    static void SetTexture(Material m, Texture2D tex)
    {
        bool any = false;
        foreach (var p in TextureProps)
            if (m.HasProperty(p)) { m.SetTexture(p, tex); any = true; }
        if (!any)
            Debug.LogWarning($"[hanakage] '{m.shader.name}' exposes no known base-colour texture slot.");
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, Color.white);
    }

    static void SetColor(Material m, Color c)
    {
        bool any = false;
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) { m.SetColor(p, p == "baseColorFactor" ? c.linear : c); any = true; }
        if (!any) Debug.LogWarning($"[hanakage] '{m.shader.name}' exposes no base-colour slot.");
    }

    static void SaveMaterial(Material m) =>
        AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath($"{MaterialDir}/{m.name}.mat"));

    // ======================================================================= small helpers
    //
    // Duplicated from SakuraNpcRoster rather than shared. They are four lines each, and the
    // alternative - widening the roster's private surface - would put a legendary encounter
    // rider's build on the same code path as the scenery cast, which is precisely the
    // "two staging paths" arrangement that once let a rebuild quietly replace good riders with
    // poor ones. FreezeSkinnedBounds IS shared, because it is neither small nor obvious.

    static void SetSmileNeutral(GameObject npc)
    {
        var smile = FindChild(npc.transform, "SmileDecal");
        if (smile == null)
        {
            Debug.LogWarning("[hanakage] no 'SmileDecal' under the rig - her 'Good.' smile at " +
                             "the final hairpin will have nothing to show.");
            return;
        }
        var r = smile.GetComponent<Renderer>();
        if (r != null) { r.enabled = false; EditorUtility.SetDirty(r); }
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    /// <summary>EXACT name match. Contains()-style matching has leaked duplicates in this project.</summary>
    static void Prune(Transform parent, string objName)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).name == objName)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject LoadPrefab(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path) ??
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().FirstOrDefault();

    static GameObject Instantiate(GameObject prefab, Transform parent)
    {
        var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject ?? Object.Instantiate(prefab);
        if (PrefabUtility.IsPartOfPrefabInstance(go))
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }
}

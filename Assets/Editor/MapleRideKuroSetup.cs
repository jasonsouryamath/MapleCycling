using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MapleRideKuroSetup
{
    // The player must match reference/improve/TARGET_kuro_anime_YESYESYES.png (the user's
    // definitive Kuro sheet - see design_assets/3d/kuro/mrb3_generate_anime_kuro.py) and the
    // approved preview/RB_*.png turnaround, which superseded the old photoreal pipeline.
    // The canonical player is KuroNPC_KuroAnime_Rigged.glb, which now carries the bytes of the
    // literal user-approved RB (Rebound) anime chibi - KuroNPC_KuroApose_Rebound.glb. It stands
    // 1.3021 m on a single skinned mesh (Mesh_0, ~39k verts, one Image_0 atlas covering helmet +
    // hair + kit + skin) over a 24-joint Kuro skeleton (Hips/Spine/Spine01/Spine02/neck/Head with
    // LeftArm/RightArm/LeftUpLeg/RightUpLeg chains), so the kuro_cycle_fixed.glb clip retargets
    // onto it unchanged. Verified in-engine seated on the bike (hands on the hoods, feet on the
    // pedals, glossy black aero helmet, tidy anime hair, matte no-chrome body): see
    // reference/good_graphics/fi_player_posecheck/*.
    //
    // HISTORY / why this path changed: the player previously loaded the misleadingly named
    // KuroNPC_KuroReal_Rigged.glb, which by 2026-09-18 held a raw Meshy PHOTOREAL sculpt (blobby
    // melted helmet/hair), NOT the anime build - so the approved anime lineage was shipping unused.
    // The refined P7 promotion (KuroNPC_KuroApose_P7.glb) is a valid alternative source, but the
    // in-engine render proved the earlier RB rebind already seats correctly on the bike, so we ship
    // the exact asset the user pointed at. NOTE: the sibling KuroNPC_KuroApose_P7_unitscale.glb
    // (the ORIGINAL "KuroAnime_Rigged" content) is BROKEN: its unit-scale bake applied the
    // armature's 0.01 scale to the bone rest transforms without regenerating the inverse-bind
    // matrices, so it EXPLODES ~100x when skinned; a dated *_BROKEN_unitscale_BACKUP_*.glb keeps it.
    // No NPC roster references this file, so using it as the player source conflicts with nothing.
    private const string ModelPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
    // Player-only original MapleRide compact aero-road bike, authored around Kuro's measured
    // ~0.267 m arm chain. NPCs keep the SHARED kuro_bike_colnago.glb via their own rosters.
    private const string BikePath = KuroCyclingPostureSetup.PlayerBikeAssetPath;
    private const string CyclePath = "Assets/Kuro/kuro_cycle_fixed.glb";

    /// <summary>
    /// Kuro's per-character bike fit. PROVISIONAL tuning, not a design requirement: it is the
    /// largest scale at which the chibi-v6 rider still reaches the brake hoods in the Blender
    /// on-bike harness (kuro_onbike_views.py). The NPC rosters use 0.9 for their own riders;
    /// Kuro sits slightly above that because his arm chain is a touch longer.
    /// Re-sweep this whenever kuro_chibi_proportions.py changes his limb lengths.
    /// </summary>
    // PROVISIONAL (A-pose rebuild promotion): 1.26, up from 1.07.
    // 1.07 was derived for the OLD 1.1333 m rider via the reference-sheet rule that the wheel
    // diameter is 0.33x standing height (0.33 * 1.1333 = 0.374 m / 0.350 m at scale 1 = 1.069).
    // The promoted A-pose Kuro (KuroNPC_KuroApose_P7 lineage) stands 1.3021 m, so the same rule
    // now gives 0.33 * 1.3021 = 0.4297 m / 0.350 = 1.228. The value actually shipped is 1.26,
    // which is the bike scale carried through the whole accepted Blender fit and signed off on
    // the GATE_leftside_vs_reference composite ("bike-ratio matches the reference"). It sits
    // ~2.6% above the arithmetic estimate, which is inside the brief's own instruction to tune
    // against the reference render rather than against the formula. Keep the two in sync with
    // zz_bike_fit.py --bike-scale.
    private const float BikeScale = 1.26f;

    /// <summary>Wheel radius of kuro_bike_colnago.glb at scale 1. Multiply by BikeScale.</summary>
    private const float UnscaledWheelRadius = 0.175f;
    // Coral is the game's first real NPC and is staged by CoralNpcSetup, which applies her
    // Coral-specific bike scale and road-bike lean. The generic loop below assumes Kuro's arm
    // length, so it is left empty rather than reintroducing the old face-test stand-in.
    private static readonly string[] NpcPaths = new string[0];
    private static readonly string[] NpcBikePaths = {
        "Assets/Kuro/NPCBikes/NPCBike_Cannondale.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Cannondale.glb", "Assets/Kuro/NPCBikes/NPCBike_Pinarello.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Trek.glb", "Assets/Kuro/NPCBikes/NPCBike_Bianchi.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Canyon.glb", "Assets/Kuro/NPCBikes/NPCBike_Cannondale.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Pinarello.glb", "Assets/Kuro/NPCBikes/NPCBike_Bianchi.glb",
        "Assets/Kuro/NPCBikes/NPCBike_Trek.glb"
    };
    private const string ControllerPath = "Assets/Kuro/KuroCycle.controller";
    private const string ScenePath = "Assets/Scenes/KuroPreview.unity";

    /// <summary>
    /// Re-bakes the authored route into whatever KuroRoadSafety components already exist in the
    /// playable scene, without tearing the scene down and rebuilding it. Use this after the route
    /// changes: a stale safety polyline reads in-game as an invisible wall across the road.
    /// </summary>
    [MenuItem("MapleRide/Repair Road Safety")]
    public static void RepairRoadSafety()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity");

        var found = Object.FindObjectsByType<KuroRoadSafety>(FindObjectsInactive.Include,
                                                             FindObjectsSortMode.None);
        if (found.Length == 0)
        {
            Debug.LogWarning("[kuro] no KuroRoadSafety in the scene - nothing to repair.");
            return;
        }

        foreach (var s in found)
        {
            ConfigureRoadSafety(s);
            EditorUtility.SetDirty(s);
            Debug.Log($"[kuro] rebaked safety route on '{s.name}': {s.route.Length} samples, " +
                      $"x [{s.minX:F0}, {s.maxX:F0}]  z [{s.minZ:F0}, {s.maxZ:F0}]  " +
                      $"corridor {s.roadCorridor:F1} m");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[kuro] saved 'Assets/Scenes/SakuraPass.unity'.");
    }

    [MenuItem("MapleRide/Build Kuro Preview")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new FileNotFoundException("Kuro GLB was not imported by glTFast", ModelPath);

        var cycleClip = AssetDatabase.LoadAllAssetsAtPath(CyclePath)
            .OfType<AnimationClip>()
            .OrderByDescending(c => c.length)
            .FirstOrDefault();
        if (cycleClip == null) throw new FileNotFoundException("Cycling animation clip was not imported", CyclePath);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var layer = controller.layers[0];
        var state = layer.stateMachine.states.FirstOrDefault(s => s.state.name == "Cycle").state;
        if (state == null) state = layer.stateMachine.AddState("Cycle");
        state.motion = cycleClip;
        state.speed = 1f;
        layer.stateMachine.defaultState = state;
        controller.layers = new[] { layer };
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var rider = (GameObject)PrefabUtility.InstantiatePrefab(model);
        rider.name = "Kuro_Rigged_Cycling";
        var animator = rider.GetComponent<Animator>() ?? rider.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.enabled = false;

        // MapleStory-style presentation: preserve the painted textures, remove
        // metallic/PBR washout, and render them with a clean bright unlit pass.
        var toonShader = Shader.Find("Unlit/Texture");
        foreach (var renderer in rider.GetComponentsInChildren<Renderer>(true))
        {
            var sourceMaterials = renderer.sharedMaterials;
            var toonMaterials = new Material[sourceMaterials.Length];
            for (var i = 0; i < sourceMaterials.Length; i++)
            {
                var source = sourceMaterials[i];
                var material = new Material(toonShader) { name = "Kuro MapleStory Toon" };
                if (source != null && source.mainTexture != null) material.mainTexture = source.mainTexture;
                // Lift the dark source albedo and add a subtle warm/red graphic grade.
                material.color = new Color(1.75f, 1.42f, 1.38f, 1f);
                toonMaterials[i] = material;
            }
            renderer.sharedMaterials = toonMaterials;
        }
        // No KuroOutline: the preview rider is skinned, and the hull pass now refuses skinned
        // renderers because it renders coincident with the body instead of behind it.

        var lightObject = new GameObject("Key Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2.2f;
        lightObject.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

        var cameraObject = new GameObject("Preview Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0.75f, 2.6f);
        camera.transform.LookAt(new Vector3(0f, 0.7f, 0f));
        camera.fieldOfView = 35f;
        scene.name = "KuroPreview";
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = rider;
        AssetDatabase.SaveAssets();
        Debug.Log($"Kuro preview built: {ScenePath}; cycle clip: {cycleClip.name} ({cycleClip.length:0.00}s)");
    }

    // ------------------------------------------------------------ Sakura Pass

    /// <summary>
    /// Builds the Sakura Pass scene: route collision, the rider, camera, then the authored
    /// environment.
    ///
    /// Everything visible now comes from the Blender pipeline in tools/blender. The previous
    /// version of this method built the entire world out of GameObject.CreatePrimitive cubes
    /// and spheres shaded with Unlit/Color, which is exactly why the game rendered as flat
    /// vector art floating in a blue void - an unlit material cannot respond to the sunset key
    /// light, and RenderSettings.skybox = null gave the hard rectangular horizon.
    /// </summary>
    [MenuItem("MapleRide/Build Sakura Pass")]
    public static void BuildSakuraPass()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new FileNotFoundException("Kuro model missing", ModelPath);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "SakuraPass";

        var zones = new GameObject("Zones").transform;
        string[] zoneNames = { "SummitApproach", "Forest", "ShrineGate", "Cliffside", "Lakeside", "Hairpin", "Summit" };
        for (int i = 0; i < zoneNames.Length; i++)
        {
            var z = new GameObject(zoneNames[i]);
            z.transform.SetParent(zones);
            z.transform.position = new Vector3(0f, 0f, -30f + i * 10f);
        }

        BuildRouteBounds();

        var rider = (GameObject)PrefabUtility.InstantiatePrefab(model);
        rider.name = "Kuro on Sakura Pass";
        // Unpack the scene instance so Unity can serialize the bicycle and rig components as
        // ordinary children. The source GLB/prefab in Assets/Kuro is not modified.
        if (PrefabUtility.IsPartOfPrefabInstance(rider))
            PrefabUtility.UnpackPrefabInstance(rider, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        // Preserve the authored Kuro texture.  Earlier passes multiplied the single combined
        // material toward near-black, which erased the red graphics, helmet vents and glove
        // shapes.  Keep the outfit black through lighting/roughness instead of crushing albedo.
        foreach (var renderer in rider.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                var c = mats[i].color;
                if (mats[i].HasProperty("_Metallic")) mats[i].SetFloat("_Metallic", 0.05f);
                if (mats[i].HasProperty("_Glossiness")) mats[i].SetFloat("_Glossiness", 0.28f);
                if (mats[i].HasProperty("_Smoothness")) mats[i].SetFloat("_Smoothness", 0.28f);
            }
        }
        // NO KuroOutline on the player. The inverted hull was destroying him: his body is a
        // SKINNED mesh, so the hull's 1 + thickness localScale is overridden by the bone
        // transforms and the black shell rendered exactly coincident with him, z-fighting his
        // face away into a mass of black facets (good_graphics/qa/player_outline_ON.png vs
        // player_outline_OFF.png - 13% of the frame differed, max channel delta 249). He reads
        // better with no outline at all; KuroOutline now refuses skinned renderers outright.
        rider.transform.position = new Vector3(-2f, 0.12f, -170f);
        rider.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        rider.AddComponent<KuroKeyboardController>();
        rider.AddComponent<KuroSpeedGauge>();
        rider.AddComponent<KuroRoadGrounding>();

        // Give him the bike. Values are pushed onto the serialized component rather than left to
        // the field initialisers, because a MonoBehaviour already saved in a scene never picks
        // up changed defaults.
        // glTFast can expose a .glb as a sub-asset while its importer is finishing. Try the
        // normal typed load first, then the main/sub-asset paths so the bike is never silently
        // omitted from the saved scene.
        var bikeModel = AssetDatabase.LoadAssetAtPath<GameObject>(BikePath);
        if (bikeModel == null) bikeModel = AssetDatabase.LoadMainAssetAtPath(BikePath) as GameObject;
        if (bikeModel == null)
        {
            bikeModel = AssetDatabase.LoadAllAssetsAtPath(BikePath)
                .OfType<GameObject>().FirstOrDefault();
        }
        if (bikeModel == null)
        {
            Debug.LogWarning("Bike model missing at " + BikePath +
                             " - run assets/3d/kuro/build_kuro_bike.py. Riding without it.");
        }
        else
        {
            Debug.Log("Bike asset resolved: " + bikeModel.name + " (" + bikeModel.GetInstanceID() + ")");
            // Instantiate the bike here rather than at runtime so it is saved into the scene and
            // the editor capture shows the real seated pose, not a rider standing in mid-air.
            // Keep an explicit scene anchor. Unity may hide the imported GLB root when it is
            // nested directly under another prefab instance; a plain GameObject anchor makes
            // the bicycle visible in the Hierarchy and gives the rig a stable search root.
            // It must be a CHILD of the rider: the bike has to inherit his movement, and
            // KuroBikeRig resolves its sockets by searching down from its own transform.
            var bikeAnchor = new GameObject("Bike");
            bikeAnchor.transform.SetParent(rider.transform, false);
            bikeAnchor.transform.localPosition = Vector3.zero;
            bikeAnchor.transform.localRotation = Quaternion.identity;
            // The imported chibi rider is taller than the first bike blockout. Enlarge the
            // authored bicycle as one unit so saddle, crank, bars and wheels stay proportional
            // instead of leaving Kuro with feet almost on the asphalt.
            // Kuro's chibi proportion rework (design_assets/3d/kuro/kuro_chibi_proportions.py,
            // preset v6) cut his rest height from 1.704 m to 1.186 m -- a ratio of 0.696. The
            // bike must shrink with him or he sits on a machine two sizes too big, so the old
            // hard-coded 1.35 is now BikeScale below. 0.95 / 1.35 = 0.704, i.e. it tracks the
            // height change almost exactly, and it is the largest scale at which the on-bike
            // harness still reaches the hoods (see the sweep in the milestone report).
            // PROVISIONAL: a per-character fit constant, not a design requirement.
            bikeAnchor.transform.localScale = Vector3.one * BikeScale;

            var bikeGo = (GameObject)PrefabUtility.InstantiatePrefab(bikeModel);
            if (bikeGo == null) bikeGo = UnityEngine.Object.Instantiate(bikeModel);
            if (bikeGo == null)
            {
                Debug.LogError("Bike asset loaded but could not be instantiated: " + BikePath);
                return;
            }
            bikeGo.name = "BikeModel";
            bikeGo.transform.SetParent(bikeAnchor.transform, false);
            bikeGo.transform.localPosition = Vector3.zero;
            bikeGo.transform.localRotation = Quaternion.identity;

            var bikeRig = rider.AddComponent<KuroBikeRig>();
            Debug.Log("Bike rig component attached: " + (bikeRig != null) + "; anchor=" + bikeAnchor.name + "; children=" + bikeAnchor.transform.childCount);
            bikeRig.bikePrefab = bikeModel;
            bikeRig.bikeLocalOffset = Vector3.zero;
            // Fold the rig scale into the wheel radius, exactly as every NPC roster does, so
            // the wheels roll at the right rate for the distance travelled instead of spinning
            // at the old 1.35-sized rate. Previously the literal 0.23625f (= 0.175 * 1.35).
            bikeRig.wheelRadius = UnscaledWheelRadius * BikeScale;
            bikeRig.gearRatio = 2.8f;
            // ---- Promoted A-pose Kuro riding pose (lockstep with zz_bike_fit.py) ----------
            // These reproduce the accepted Blender fit that passed the reference gate:
            //   --bike-scale=1.26 --lean=47 --neck=0.95 --headpitch=-10 --crank=-90 --footsolve
            //
            // hipTilt is deliberately ZERO. The rebuilt spine (zz_p3_rig.py) exists precisely
            // so the lean can live entirely in Spine/Spine01/Spine02, and the frozen rule from
            // the rebuild is "never rotate Hips" - rotating the pelvis is what used to drag the
            // jersey hem over the top tube. The old 8 deg / 10 deg split was tuned against the
            // DEGENERATE spine (a 6 mm + 48 mm + 48 mm chain) and is meaningless on this rig.
            bikeRig.hipTiltDegrees = 0f;
            // PROVISIONAL: 47 is the REQUESTED lean. The rebuilt spine delivers ~34.5 deg of
            // visible torso pitch from this request (measured by zz_leanprobe.py), which is
            // inside the authorised 30-45 deg window. Acceptance is the DELIVERED angle in the
            // render, never this number.
            bikeRig.spineLeanDegrees = 11f;
            // Counter-rotation so his gaze stays on the road. These should roughly cancel the
            // DELIVERED torso pitch (~34.5 deg), not the requested 47, and then carry a little
            // extra downward pitch for the road-ahead look (Blender --headpitch=-10).
            bikeRig.neckLiftDegrees = -22f;
            bikeRig.headLiftDegrees = -28f;
            // MEASURED off the promoted mesh, not guessed: the sole sits 0.128 m below the
            // ankle joint (zz_unitscale.py / the inverse-bind-matrix probe; left 0.1280,
            // right 0.1279). ankleHeight is the ankle's height above the pedal platform, so
            // anything smaller drives the shoe THROUGH the pedal. The previous 0.045 was too
            // small even for the OLD rider, whose shoe measured 0.0802 - this is the same
            // floating/sinking foot defect the Blender --footsolve pass fixed, and it was
            // present in the shipped Unity config all along. At 0.130 the in-engine mesh-level
            // probe still read the shoes +0.0155 / +0.0237 m PROUD of the pedal platform, so
            // the ankle is dropped to 0.113 to centre the sole on it. PROVISIONAL: this last
            // step was tuned against the render, not derived from the mesh.
            bikeRig.ankleHeight = 0.113f;
            bikeRig.footTargetRearwardOffset = 0.062f;
            bikeRig.footHeightTrimL = 0.0005f;
            bikeRig.footHeightTrimR = 0.0025f;
            bikeRig.footToeDownDegrees = 5f;
            bikeRig.footRollDegrees = 5f;
            bikeRig.kneePoleLateralOffset = 0.060f;
            // Real pedals orbit the bottom bracket but stay flat. Leaving them rigid flips the
            // pedal's local up vector through the stroke, which makes the (pedal.up * ankleHeight)
            // IK target swap sign and sinks the foot through the platform at the top of the
            // revolution - the same failure that stood the pedal platforms on edge in Blender
            // (CONTACT_P11_6up.png) until the crank pass was changed to TRANSLATE the pedals.
            bikeRig.keepPedalsLevel = true;
            // Player-only seated-hand fit. The collarbone drop lowers the arm roots; a clean
            // SYMMETRIC grip (like the NPCs) then seats both gloves on their OWN hoods: a mirrored
            // outboard spread (gripSpreadMetres) pushes each wrist ~0.06 m outboard so the baked
            // glove - which hangs ~0.09 m inboard of the wrist - lands on the hood, plus a small
            // symmetric Y lift keeps the wrists at hood height. Verified by frontal render on
            // Sakura (fi_symsweep/sym_p06y5): arms uncrossed, forearms parallel, each glove on its
            // same-side hood. This REPLACES the earlier asymmetric per-hand handSeatOffsetL/R that
            // a glove-gap=0 seat-fit solver derived - those had a large crossing -Z back-pull that
            // yanked each wrist up/inboard/back until the forearms crossed into an X ("pretzel
            // arms"). Seat offsets are now ZERO, same as every NPC on the shared bike.
            bikeRig.shoulderDropDegrees = 7f;
            bikeRig.handTargetLocalOffset = new Vector3(0f, 0.01f, 0f);
            bikeRig.gripSpreadMetres = 0f;
            bikeRig.handSeatOffsetL = Vector3.zero;
            bikeRig.handSeatOffsetR = Vector3.zero;
            bikeRig.useAnatomicalArmSolver = true;
            EditorUtility.SetDirty(bikeRig);
        }

        // The safety net has to be told what the road *is*. Baking the authored route in here
        // is what stops it drifting out of sync with the pass the way its old private copy did.
        ConfigureRoadSafety(rider.AddComponent<KuroRoadSafety>());

        var camObj = new GameObject("Sakura Camera");
        var cam = camObj.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.transform.position = new Vector3(1.35f, 1.35f, -174.8f);
        cam.transform.LookAt(new Vector3(-1.8f, 0.92f, -170f));
        cam.fieldOfView = 34f;
        var follow = camObj.AddComponent<KuroFollowCamera>();
        follow.target = rider.transform;
        // Straight rear-and-above chase. This used to be (2.55, 1.18, -3.35) - a beauty-shot
        // three-quarter angle that quietly became the ride camera and had the player reporting
        // they were "looking at my character from the side". The framing now lives in ONE place:
        // RideCameraSetup, which also pushes it onto an already-saved scene.
        follow.offset = RideCameraSetup.ChaseOffset;
        follow.followSharpness = RideCameraSetup.FollowSharpness;
        follow.yawOnlyFrame = true;
        follow.bankFollowFraction = 0f;
        follow.pitchFollowFraction = RideCameraSetup.PitchFollowFraction;
        follow.maxHeadingLagDegrees = RideCameraSetup.MaxHeadingLagDegrees;

        var rimObject = new GameObject("Kuro Rim Light");
        var rim = rimObject.AddComponent<Light>();
        rim.type = LightType.Point;
        rim.color = new Color(1.0f, 0.45f, 0.38f);
        rim.intensity = 1.45f;
        rim.range = 5.5f;
        rim.shadows = LightShadows.None;
        rimObject.transform.position = rider.transform.position + new Vector3(-1.8f, 2.3f, 1.6f);

        // Save BEFORE the environment pass. Apply() reopens ScenePath in batch mode whenever the
        // active scene's path differs, and a NewScene has no path yet - so calling it first threw
        // away everything built above and then re-saved the *previous* scene. That is why newly
        // added objects silently failed to appear while pre-existing ones looked fine.
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/SakuraPass.unity");

        // Lighting, sky, fog, the staged landscape and all scattered dressing are owned by the
        // environment pass so there is one place that decides how the pass looks.
        SakuraPassEnvironment.Apply();

        StageNpcVariants();

        // THE BOOT SCENE IS SHARED BY EVERY REGION. RegionDirector shows/hides one root per region
        // inside this ONE scene. This build regenerated the scene from an EMPTY scene above, which
        // drops every non-Sakura region root, so the five other world-map pins would drop the rider
        // onto the empty checkerboard start line. Re-stage all sibling regions back in and leave
        // Sakura the active default before saving. SaveWithSakuraDefault also runs the region-root
        // guardrail assertion, so this build now FAILS loudly if any unlocked region is missing
        // rather than silently shipping a black checkerboard.
        MapleRideRegionStaging.StageSiblingRegions();
        MapleRideRegionStaging.SaveWithSakuraDefault("build-sakura-pass");

        Debug.Log("Sakura Pass built: Assets/Scenes/SakuraPass.unity");
    }

    /// <summary>
    /// Stages the rider cast. The per-NPC work now lives in SakuraNpcRoster, which builds every
    /// rider the way Coral is built.
    ///
    /// The loop that used to live here staged NPCs with an 8 deg hip tilt on a NON-reversed
    /// route. Both are wrong and were fixed on Coral first: that pose leaves the hands short of
    /// the brake hoods, and an NPC travelling the player's way keeps them permanently behind it
    /// and outside the view cone, so it could never greet anyone. Keeping two staging paths
    /// alive also meant a full scene rebuild quietly replaced the good riders with poor ones.
    /// </summary>
    static void StageNpcVariants()
    {
        var root = new GameObject("NPCs").transform;
        var route = ResampleRoute(GetRoadPoints(), 3.0f);
        SakuraNpcRoster.StageAll(root, route);
        Debug.Log("Staged the Sakura Pass rider roster (including Coral).");
    }

    [MenuItem("MapleRide/Capture Sakura Pass Verification")]
    public static void CaptureSakuraPassVerification()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/SakuraPass.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);

        var camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                           .FirstOrDefault(c => c.name == "Sakura Camera");
        if (camera == null) throw new FileNotFoundException("Sakura Camera was not found in SakuraPass.");

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();

        var previousActive = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(w, h, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        image.Apply();

        var dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);
        var output = Path.Combine(dir, "SakuraPass_After.png");
        File.WriteAllBytes(output, image.EncodeToPNG());

        Object.DestroyImmediate(image);
        RenderTexture.active = previousActive;
        camera.targetTexture = previous;
        Object.DestroyImmediate(rt);
        Debug.Log("Sakura Pass verification captured: " + output);
    }

    /// <summary>
    /// Close-up verification of the rider on the bike. The follow-camera shot is far too distant
    /// to tell whether the feet are actually on the pedals, and "verify with renders" only counts
    /// if the render can show the defect.
    /// </summary>
    [MenuItem("MapleRide/Capture Kuro Ride Verification")]
    public static void CaptureKuroRideVerification()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/SakuraPass.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);

        var rider = Object.FindObjectsByType<KuroBikeRig>(FindObjectsSortMode.None).FirstOrDefault();
        if (rider == null) throw new FileNotFoundException("No KuroBikeRig in SakuraPass.");

        var pivot = rider.transform.position + Vector3.up * 0.55f;
        var camObj = new GameObject("RideVerifyCam");
        var cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.05f;

        var dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var shots = new (string name, Vector3 dir)[]
        {
            ("side",    rider.transform.right),
            ("front34", (rider.transform.right + rider.transform.forward * 1.2f).normalized),
            ("rear34",  (rider.transform.right - rider.transform.forward * 1.2f).normalized),
        };

        foreach (var shot in shots)
        {
            cam.transform.position = pivot + shot.dir * 2.1f + Vector3.up * 0.35f;
            cam.transform.LookAt(pivot);

            const int w = 1200, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(w, h, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            image.Apply();
            var output = Path.Combine(dir, "kuro_ride_" + shot.name + ".png");
            File.WriteAllBytes(output, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            RenderTexture.active = prevActive;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Debug.Log("Kuro ride verification captured: " + output);
        }

        Object.DestroyImmediate(camObj);
    }

    /// <summary>
    /// The authored route, in Unity world space, read straight from the file the Blender
    /// pipeline generates.
    ///
    /// This used to be a hand-maintained copy of sakura_route.py's CONTROL_POINTS. Hand-mirrored
    /// route data has now drifted twice - most recently leaving the rider's safety net clamping
    /// to a summit that no longer existed, which played as an invisible wall across the road -
    /// so the copy is gone. sakura_route.write_route_json() is the single source of truth.
    /// </summary>
    public static Vector3[] GetRoadPoints()
    {
        const string path = "Assets/Environment/SakuraPass/SakuraRoute.json";
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "SakuraRoute.json is missing. Run: blender -b -P build_all.py -- route", path);

        var dto = JsonUtility.FromJson<RouteFile>(File.ReadAllText(path));
        if (dto == null || dto.controlPoints == null || dto.controlPoints.Length < 2)
            throw new InvalidDataException("SakuraRoute.json has no usable controlPoints: " + path);

        return dto.controlPoints.Select(p => new Vector3(p.x, p.y, p.z)).ToArray();
    }

    [System.Serializable] private class RoutePoint { public float x, y, z; }
    [System.Serializable] private class RouteFile { public RoutePoint[] controlPoints; }

    /// <summary>
    /// Bakes the authored route into the rider's safety net and sizes the recovery bounds to it.
    ///
    /// The corridor test measures distance to straight segments, so it is fed a Catmull-Rom
    /// resample matching tools/blender/sakura_route.py's sample_centerline(). Feeding it the raw
    /// control points would let the chord cut corners the real asphalt bulges around, and the
    /// clamp would shove the rider off the outside of every bend.
    /// </summary>
    private static void ConfigureRoadSafety(KuroRoadSafety safety)
    {
        var pts = ResampleRoute(GetRoadPoints(), 3.0f);
        safety.route = pts;

        Vector3 lo = pts[0], hi = pts[0];
        foreach (var p in pts) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }

        // Generous margin: these bounds are a "you have fallen out of the world" backstop, not
        // the corridor clamp. Anything tighter risks teleporting a rider who is still on tarmac.
        const float margin = 90f;
        safety.minX = lo.x - margin;
        safety.maxX = hi.x + margin;
        safety.minZ = lo.z - margin;
        safety.maxZ = hi.z + margin;
        safety.minY = lo.y - 12f;

        safety.roadCorridor = 3.9f;   // 3.5 m half-width plus a little slack
        safety.recoveryPosition = pts[0] + Vector3.up * 0.2f;
    }

    /// <summary>Catmull-Rom resample, mirroring sakura_route.sample_centerline().</summary>
    public static Vector3[] ResampleRoute(Vector3[] cp, float spacing)
    {
        var outPts = new List<Vector3>();
        int n = cp.Length;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = cp[Mathf.Max(0, i - 1)], p1 = cp[i];
            Vector3 p2 = cp[i + 1], p3 = cp[Mathf.Min(n - 1, i + 2)];
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / spacing));
            for (int s = 0; s < steps; s++)
            {
                float t = (float)s / steps, t2 = t * t, t3 = t2 * t;
                outPts.Add(0.5f * ((2f * p1) + (-p0 + p2) * t
                                   + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                                   + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        outPts.Add(cp[n - 1]);
        return outPts.ToArray();
    }

    /// <summary>
    /// Invisible containment for the playable corridor.
    ///
    /// These are bare BoxColliders on empty GameObjects rather than scaled primitive cubes:
    /// a primitive would drag a MeshRenderer and an Unlit material into the scene, and the
    /// visible world is authored in Blender now. The riding surface itself gets its collision
    /// from a MeshCollider on the authored asphalt, which the environment pass sets up.
    /// </summary>
    private static void BuildRouteBounds()
    {
        var root = new GameObject("Route Collision").transform;

        // Derived from the authored route rather than hardcoded, so extending the pass can never
        // leave a wall cutting across the road the way the old x = -120 west wall did.
        var pts = GetRoadPoints();
        Vector3 lo = pts[0], hi = pts[0];
        foreach (var p in pts) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }

        const float pad = 48f;     // clear of the road, still inside the authored terrain
        float cx = (lo.x + hi.x) * 0.5f, cz = (lo.z + hi.z) * 0.5f;
        float spanX = (hi.x - lo.x) + pad * 2f, spanZ = (hi.z - lo.z) + pad * 2f;
        float wallY = hi.y + 20f, wallH = (hi.y - lo.y) + 220f;

        // A deep catch volume well below the valley floor, so a rider who leaves the road
        // still lands on something instead of falling forever.
        AddBounds(root, "Valley Catch Floor", new Vector3(cx, lo.y - 58f, cz),
                  new Vector3(spanX, 4f, spanZ));

        AddBounds(root, "Boundary West", new Vector3(lo.x - pad, wallY, cz), new Vector3(1f, wallH, spanZ));
        AddBounds(root, "Boundary East", new Vector3(hi.x + pad, wallY, cz), new Vector3(1f, wallH, spanZ));
        AddBounds(root, "Boundary South", new Vector3(cx, wallY, lo.z - pad), new Vector3(spanX, wallH, 1f));
        AddBounds(root, "Boundary North", new Vector3(cx, wallY, hi.z + pad), new Vector3(spanX, wallH, 1f));
    }

    private static void AddBounds(Transform parent, string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = centre;
        go.AddComponent<BoxCollider>().size = size;
    }
}

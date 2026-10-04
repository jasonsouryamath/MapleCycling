using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Idempotent staging, self-test and visual verification for Kuro's four selectable cycling
/// postures. The rider remains one skinned rig; posture data feeds KuroBikeRig's existing
/// post-Animator procedural solve. This owner also installs Kuro's player-only original
/// MapleRide aero-road bicycle and its small time-trial cockpit. No shared NPC bike or imported
/// material is mutated.
/// </summary>
public static class KuroCyclingPostureSetup
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PlayerName = "Kuro on Sakura Pass";
    public const string PlayerBikeAssetPath =
        "Assets/Kuro/Player/kuro_bike_mapleride_aero.glb";
    private const string PostureRootName = "Kuro Posture Rig";
    private const string AeroVisualName = "Kuro Aero Cockpit";
    private const string MaterialFolder = "Assets/Kuro/Player/Materials";
    private const string AeroMaterialPath = MaterialFolder + "/KuroAeroCockpit.mat";
    private const string PlayerCharacterAssetPath =
        "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
    // PROVISIONAL visual-diagnostic thresholds calibrated against Assets/Kuro/Reference.png.
    // These supplement render inspection; they are not substitutes for it.
    private const float MinimumUpperArmSectionMetres = 0.065f;
    private const float MinimumForearmSectionMetres = 0.050f;
    // PROVISIONAL: baked slice orientation changes across the four very different poses.
    // 1.50 retains sensitivity to real collapse while admitting the visually healthy 1.44
    // forearm ratio produced by the final fuller mesh.
    private const float MaximumArmSectionPoseRatio = 1.50f;
    private const float MinimumArmLegAxisClearanceMetres = 0.008f;
    // PROVISIONAL verification resolution. Five degrees is dense enough to catch the brief
    // knee-rise interval that a 30-degree sweep can step over, while remaining cheap in batchmode.
    private const float ArmLegCrankSweepStepDegrees = 5f;

    // Player bike scale remains unchanged: the compact fit comes from authored frame/cockpit
    // geometry, not from independently shrinking the bicycle around the rider.
    public const float PlayerBikeScale = 1.26f;
    public const float UnscaledWheelRadius = 0.175f;
    public const float AuthoredWheelbase = 0.540f;
    public const float AuthoredSaddleToHood = 0.213f;
    // PROVISIONAL shoulder-matched cockpit widths, authored before PlayerBikeScale.
    // Kuro's measured arm-head breadth is 0.1974 m; the hood width becomes 0.252 m in-world.
    public const float AuthoredTopHalfWidth = 0.082f;
    public const float AuthoredHoodHalfWidth = 0.100f;
    public const float AuthoredDropHalfWidth = 0.108f;
    // PROVISIONAL inner-top socket. The physical tops remain 164 mm wide authored / 207 mm
    // in-world; Fitness grips 2 mm inboard per side, which preserves its upright wrist without
    // collapsing the forearm skin. Hoods and drops use the full shoulder-matched cockpit width.
    private const float FitnessInnerTopGripHalfWidth = 0.080f;
    private const float MinimumWorldHoodWidthMetres = 0.245f;
    private const float MaximumWorldHoodWidthMetres = 0.270f;
    // Kuro's original compact drops leave only 67% of his arm chain between shoulder and grip
    // in the low-aero posture, forcing an 84-degree elbow fold. Add connected forward drop
    // grips for that posture only; the normal hoods, fitness tops, fork and wheel stay untouched.
    private const float AggressiveReachExtensionLeftMetres = 0.090f;
    // The imported right arm chain is slightly longer and its shoulder bind sits farther
    // forward, so the right grip needs 10 mm more authored reach for a symmetric elbow arc.
    private const float AggressiveReachExtensionRightMetres = 0.100f;
    private const string AggressiveGripExtensionName = "Kuro Forward Drop Grips";

    // Baseline rider-fit values. All are PROVISIONAL named tunables and are pushed onto the
    // serialized KuroBikeRig every stage so a full Sakura rebuild cannot restore the old fit.
    private const float BaseHipTiltDegrees = 0f;
    private const float BaseSpineLeanDegrees = 11f;
    private const float BaseNeckLiftDegrees = -22f;
    private const float BaseHeadLiftDegrees = -28f;
    private const float BaseShoulderDropDegrees = 7f;
    private const float BaseShoulderProtractionDegrees = 3f;
    // PROVISIONAL posture trims measured against the solved left/right elbow arcs. These retain
    // each side's imported chest-relative bind while compensating for the source's unequal arm
    // chain geometry; they do not hide the jersey defect, which is repaired in the mesh itself.
    private const float AggressiveShoulderBalanceDegrees = 2f;
    private const float RelaxedShoulderBalanceDegrees = 8f;
    private const float TimeTrialShoulderBalanceDegrees = 17f;
    private const float FitnessShoulderBalanceDegrees = 3f;
    private const float MaximumShoulderRootHemMismatchMetres = 0.030f;
    private const float MaximumShoulderRootDepthMismatchMetres = 0.020f;
    private const float MaximumShoulderRootWidthMismatchMetres = 0.018f;
    // Palm-aware authored grip sockets replace the old 20 mm lateral wrist-spread workaround.
    private const float BaseGripSpreadMetres = 0f;
    private const float BaseAnkleHeightMetres = 0.113f;
    private const float FootTargetRearwardMetres = 0.062f;
    private const float FootHeightTrimLeftMetres = 0.0005f;
    private const float FootHeightTrimRightMetres = 0.0025f;
    private const float FootToeDownDegrees = 5f;
    private const float FootRollDegrees = 5f;
    private const float KneePoleLateralMetres = 0.060f;
    private static readonly Vector3 SaddleTopLocalPosition =
        new Vector3(0f, 0.498f, -0.026f);
    private static readonly Vector3 BaseHandTargetLocalOffset =
        new Vector3(0f, 0.010f, 0f);

    // SteerPivot-local contacts track the actual shoulder-width visible bar. Fitness stays on
    // the inner tops; relaxed uses the hoods; aggressive uses the subtly flared lower drops.
    private static readonly Vector3 FitnessTopL =
        new Vector3(-FitnessInnerTopGripHalfWidth, 0.010f, 0.006f);
    private static readonly Vector3 FitnessTopR =
        new Vector3( FitnessInnerTopGripHalfWidth, 0.010f, 0.006f);
    private static readonly Vector3 RelaxedHoodL =
        new Vector3(-AuthoredHoodHalfWidth, -0.008f, 0.032f);
    private static readonly Vector3 RelaxedHoodR =
        new Vector3( AuthoredHoodHalfWidth, -0.008f, 0.032f);
    private static readonly Vector3 AggressiveGripContactL =
        new Vector3(-0.159f, -0.066f, 0.124f);
    private static readonly Vector3 AggressiveGripContactR =
        new Vector3( 0.159f, -0.066f, 0.121f);
    private static readonly Vector3 AggressiveGripStartL =
        new Vector3(-AuthoredDropHalfWidth, -0.090f, 0.030f);
    private static readonly Vector3 AggressiveGripStartR =
        new Vector3( AuthoredDropHalfWidth, -0.090f, 0.030f);
    // Keep the anatomically validated hand-bone reach separate from the physical contact point.
    // The imported glove meshes are substantially offset from their Hand bones.
    private static readonly Vector3 AggressiveDropL =
        new Vector3(-0.078f, -0.050f, 0.092f);
    private static readonly Vector3 AggressiveDropR =
        new Vector3( 0.078f, -0.052f, 0.083f);
    private static readonly Vector3 AeroGripL = new Vector3(-0.040f, 0.005f, 0.090f);
    private static readonly Vector3 AeroGripR = new Vector3( 0.040f, 0.005f, 0.090f);
    private static readonly Vector3 AeroPadCenterL = new Vector3(-0.050f, -0.048f, 0.006f);
    private static readonly Vector3 AeroPadCenterR = new Vector3( 0.050f, -0.063f, 0.006f);
    private static readonly Vector3 AeroElbowContactL = new Vector3(-0.052f, -0.039f, 0.006f);
    private static readonly Vector3 AeroElbowContactR = new Vector3( 0.045f, -0.054f, 0.006f);

    // Desired HAND-BONE rotations in SteerPivot space. These preserve Kuro's baked closed glove
    // while making palm orientation an authored part of each real grip rather than an IK accident.
    // The glove's long baked axis is local -X. Aim it along the forward drop instead of leaving
    // the hand as a vertical paddle, while keeping each palm normal facing inward toward the stem.
    private static readonly Quaternion AggressiveGripRotationL =
        Quaternion.LookRotation(
            new Vector3(-0.517f, -0.579f, -0.631f),
            new Vector3(-0.012f, -0.732f, 0.681f)) *
        Quaternion.AngleAxis(
            -29.9f, new Vector3(0.4473f, 0.7999f, -0.4002f).normalized);
    private static readonly Quaternion AggressiveGripRotationR =
        Quaternion.LookRotation(
            new Vector3(0.579f, -0.563f, -0.590f),
            new Vector3(-0.061f, -0.751f, 0.657f)) *
        Quaternion.AngleAxis(
            -18.4f, new Vector3(0.7123f, -0.3860f, 0.5863f).normalized);
    private static readonly Vector3 RelaxedGripEulerL = new Vector3(34f, 122f, 94f);
    private static readonly Vector3 RelaxedGripEulerR = new Vector3(42f, 236f, 262f);
    private static readonly Vector3 AeroGripEulerL = new Vector3(326f, 108f, 40f);
    private static readonly Vector3 AeroGripEulerR = new Vector3(359f, 266f, 320f);
    private static readonly Vector3 FitnessGripEulerL = new Vector3(22f, 158f, 119f);
    private static readonly Vector3 FitnessGripEulerR = new Vector3(29f, 195f, 237f);

    private static string OutputDirectory =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/kuro_four_poses"));

    [MenuItem("MapleRide/Kuro/Stage Four Cycling Postures", priority = 62)]
    public static void Stage()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = FindRoot(scene, PlayerName);
        if (player == null) throw new InvalidOperationException("Kuro player root is missing.");

        // The supplied gameplay references establish the seated low-aero road-racer posture as
        // the locked production default. The other authored poses remain editor-test fixtures.
        ConfigurePlayer(player, false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[kuro-posture] staged the locked seated low-aero gameplay default; " +
                  "alternate authored poses remain editor-only fixtures. Scene saved clean.");
    }

    /// <summary>
    /// Public so a future full Sakura rebuild can call the same owner instead of duplicating
    /// cockpit geometry or pose values.
    /// </summary>
    public static KuroRidePose ConfigurePlayer(GameObject player, bool preserveSelection)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig == null) throw new InvalidOperationException("Player KuroBikeRig is missing.");

        EnsurePlayerBike(player, rig);

        var poses = player.GetComponents<KuroRidePose>();
        for (int i = 1; i < poses.Length; i++) UnityEngine.Object.DestroyImmediate(poses[i]);
        bool added = poses.Length == 0;
        var pose = added ? player.AddComponent<KuroRidePose>() : poses[0];
        var previous = pose.selectedPosture;

        var steer = ExactSingle(player, "SteerPivot");
        PruneExactChildren(steer, PostureRootName);
        var root = NewChild(steer, PostureRootName);
        var aeroMaterial = GetOrCreateAeroMaterial(player);
        RestoreImportedCockpitPositions(steer);
        ConfigureSaddle(player);
        BuildAggressiveReachGrips(root, aeroMaterial);

        var fitnessL = Socket(root, "FitnessTop_L", FitnessTopL, FitnessGripEulerL);
        var fitnessR = Socket(root, "FitnessTop_R", FitnessTopR, FitnessGripEulerR);
        var relaxedL = Socket(root, "RelaxedHood_L", RelaxedHoodL, RelaxedGripEulerL);
        var relaxedR = Socket(root, "RelaxedHood_R", RelaxedHoodR, RelaxedGripEulerR);
        Socket(root, "AggressiveGripContact_L", AggressiveGripContactL, Vector3.zero);
        Socket(root, "AggressiveGripContact_R", AggressiveGripContactR, Vector3.zero);
        var dropL = Socket(root, "AggressiveDrop_L", AggressiveDropL, AggressiveGripRotationL);
        var dropR = Socket(root, "AggressiveDrop_R", AggressiveDropR, AggressiveGripRotationR);

        var aero = NewChild(root, AeroVisualName);
        BuildAeroCockpit(aero, aeroMaterial);
        var aeroGripL = Socket(aero, "AeroGrip_L", AeroGripL, AeroGripEulerL);
        var aeroGripR = Socket(aero, "AeroGrip_R", AeroGripR, AeroGripEulerR);
        var aeroPoleL = Socket(aero, "AeroElbowContact_L", AeroElbowContactL);
        var aeroPoleR = Socket(aero, "AeroElbowContact_R", AeroElbowContactR);

        pose.rig = rig;
        if (pose.session == null) pose.session = UnityEngine.Object.FindFirstObjectByType<RideSession>();
        pose.lockGameplayLowAeroDefault = true;
        pose.enableKeyboardSelection = false;
        pose.rememberSelection = false;
        pose.aggressiveKey = KeyCode.F1;
        pose.relaxedKey = KeyCode.F2;
        pose.timeTrialKey = KeyCode.F3;
        pose.fitnessKey = KeyCode.F4;
        pose.enableClimbOverlay = false;
        pose.enableSprintOverlay = true;
        pose.aeroCockpitVisual = aero.gameObject;

        // Values below are named, serialized visual-fit defaults -- not final bike-fit policy.
        pose.roadRacerAggressive = Data(
            "Road Racer / Aggressive",
            hip: 29f, spine: 52f, neck: -10f, head: -12f, shoulder: 0f,
            handOffset: Vector3.zero, spread: 0f,
            handL: dropL, handR: dropR, poleL: null, poleR: null,
            protraction: BaseShoulderProtractionDegrees,
            protractionBalance: AggressiveShoulderBalanceDegrees,
            elbowOut: 0.006f, elbowDown: 0.082f, elbowBack: 0.010f,
            constrainElbows: false, upperTwist: 0.09f, foreTwist: 0.28f,
            handOrient: 0.80f, maxTwist: 42f);

        pose.roadRiderRelaxed = Data(
            "Road Rider / Relaxed",
            // BaseSpineLeanDegrees is already applied by KuroBikeRig. The former +26 degree
            // additive trim over-curled the three-bone spine chain; +12 preserves a natural
            // road-cycling lean without collapsing the chest into the hips.
            hip: 4f, spine: 48f, neck: -12f, head: -12f, shoulder: 0f,
            handOffset: Vector3.zero, spread: 0f,
            handL: relaxedL, handR: relaxedR, poleL: null, poleR: null,
            protraction: BaseShoulderProtractionDegrees,
            protractionBalance: RelaxedShoulderBalanceDegrees,
            elbowOut: 0.004f, elbowDown: 0.078f, elbowBack: 0.006f,
            constrainElbows: false, upperTwist: 0.12f, foreTwist: 0.38f,
            handOrient: 1f, maxTwist: 46f);

        pose.triathlonTimeTrial = Data(
            "Triathlon / Time Trial / Aerodynamic",
            hip: 28f, spine: 129f, neck: -40f, head: -44f, shoulder: 0f,
            handOffset: Vector3.zero, spread: 0f,
            handL: aeroGripL, handR: aeroGripR, poleL: aeroPoleL, poleR: aeroPoleR,
            protraction: BaseShoulderProtractionDegrees,
            protractionBalance: TimeTrialShoulderBalanceDegrees,
            elbowOut: 0f, elbowDown: 0f, elbowBack: 0f,
            constrainElbows: true, upperTwist: 0.16f, foreTwist: 0.52f,
            handOrient: 1f, maxTwist: 65f);

        pose.roadRiderFitness = Data(
            "Road Rider / Fitness / Very Relaxed",
            hip: 2f, spine: 24f, neck: 5f, head: 7f, shoulder: 0f,
            handOffset: Vector3.zero, spread: 0f,
            handL: fitnessL, handR: fitnessR, poleL: null, poleR: null,
            protraction: BaseShoulderProtractionDegrees,
            protractionBalance: FitnessShoulderBalanceDegrees,
            elbowOut: 0.008f, elbowDown: 0.074f, elbowBack: 0.004f,
            constrainElbows: false, upperTwist: 0.10f, foreTwist: 0.34f,
            handOrient: 1f, maxTwist: 42f);

        pose.selectedPosture = preserveSelection && !added
            ? previous
            : KuroRidePose.CyclingPosture.RoadRacerAggressive;
        PushImportedArmBinds(player, rig);
        ConfigureProductionRuntime(rig);
        pose.ApplySelectedPosture();
        rig.ForceSolveOnce();
        KuroGogglesSetup.ConfigurePlayer(player);

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(pose);
        EditorUtility.SetDirty(rig);
        return pose;
    }

    private static void PushImportedArmBinds(GameObject player, KuroBikeRig rig)
    {
        var left = ExactSingle(player, "LeftShoulder");
        var right = ExactSingle(player, "RightShoulder");
        var leftSource = PrefabUtility.GetCorrespondingObjectFromSource(left);
        var rightSource = PrefabUtility.GetCorrespondingObjectFromSource(right);
        if (leftSource != null && rightSource != null)
        {
            // Preserve each imported chest-parent-relative bind. The independent Blender probe
            // measures only 1.4 degrees of clavicle mirror error; replacing the left bind with a
            // synthetic mirror caused 14-23 degree elbow asymmetry while failing to fix the real
            // 28 mm sleeve-geometry defect.
            rig.SetShoulderParentBinds(leftSource.localRotation, rightSource.localRotation);
        }

        var leftArm = ExactSingle(player, "LeftArm");
        var leftForeArm = ExactSingle(player, "LeftForeArm");
        var leftHand = ExactSingle(player, "LeftHand");
        var rightArm = ExactSingle(player, "RightArm");
        var rightForeArm = ExactSingle(player, "RightForeArm");
        var rightHand = ExactSingle(player, "RightHand");
        var leftArmSource = PrefabUtility.GetCorrespondingObjectFromSource(leftArm);
        var leftForeArmSource = PrefabUtility.GetCorrespondingObjectFromSource(leftForeArm);
        var leftHandSource = PrefabUtility.GetCorrespondingObjectFromSource(leftHand);
        var rightArmSource = PrefabUtility.GetCorrespondingObjectFromSource(rightArm);
        var rightForeArmSource = PrefabUtility.GetCorrespondingObjectFromSource(rightForeArm);
        var rightHandSource = PrefabUtility.GetCorrespondingObjectFromSource(rightHand);
        if (leftArmSource != null && leftForeArmSource != null && leftHandSource != null &&
            rightArmSource != null && rightForeArmSource != null && rightHandSource != null)
        {
            rig.SetArmLocalBinds(
                leftArmSource.localRotation, leftForeArmSource.localRotation,
                leftHandSource.localRotation, rightArmSource.localRotation,
                rightForeArmSource.localRotation, rightHandSource.localRotation);
        }
    }

    private static void ConfigureProductionRuntime(KuroBikeRig rig)
    {
        if (rig.rideSession != null && rig.rideSession.devices != null)
        {
            // Up Arrow is the production no-trainer fallback. It stays inside DeviceManager so
            // physics, telemetry and cadence all see one source; the old 1000 W scout remains off.
            rig.rideSession.devices.mash.scoutHoldMode = false;
            rig.rideSession.devices.mash.keyboardHoldEnabled = true;
            rig.rideSession.devices.mash.keyboardHoldKey = KeyCode.UpArrow;
            rig.rideSession.devices.mash.keyboardHoldWatts = 220f;
            rig.rideSession.devices.mash.keyboardHoldRampSeconds = 0.45f;
            rig.rideSession.devices.mash.keyboardReleaseSeconds = 0.55f;
            rig.rideSession.devices.mash.keyboardHoldCadenceRpm = 88f;
            EditorUtility.SetDirty(rig.rideSession.devices);
        }

        var follow = UnityEngine.Object.FindFirstObjectByType<KuroFollowCamera>();
        if (follow != null)
        {
            follow.enableDebugOrbit = true;
            follow.debugOrbitSpeed = 90f;
            follow.keyboardOrbitResponse = 12f;
            EditorUtility.SetDirty(follow);
        }
    }

    private static Transform EnsurePlayerBike(GameObject player, KuroBikeRig rig)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBikeAssetPath);
        if (prefab == null)
            throw new FileNotFoundException("Kuro player aero bike was not imported", PlayerBikeAssetPath);

        var bikes = player.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "Bike").ToList();
        Transform bike = bikes.FirstOrDefault(t => t.parent == player.transform)
                      ?? bikes.FirstOrDefault();
        foreach (var duplicate in bikes)
            if (duplicate != bike) UnityEngine.Object.DestroyImmediate(duplicate.gameObject);

        string sourcePath = "";
        if (bike != null)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(bike.gameObject);
            if (source != null) sourcePath = AssetDatabase.GetAssetPath(source);
        }
        bool needsReplacement = bike == null ||
            !string.Equals(sourcePath, PlayerBikeAssetPath, StringComparison.OrdinalIgnoreCase);
        if (needsReplacement)
        {
            if (bike != null) UnityEngine.Object.DestroyImmediate(bike.gameObject);
            var fresh = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
            if (fresh == null) fresh = UnityEngine.Object.Instantiate(prefab, player.transform);
            if (fresh == null) throw new InvalidOperationException("Could not instantiate Kuro aero bike.");
            fresh.name = "Bike";
            bike = fresh.transform;
        }

        bike.SetParent(player.transform, false);
        bike.localPosition = Vector3.zero;
        bike.localRotation = Quaternion.identity;
        bike.localScale = Vector3.one * PlayerBikeScale;
        ApplyPlayerBikeMaterials(bike);

        rig.bikePrefab = prefab;
        rig.bikeLocalOffset = Vector3.zero;
        rig.wheelRadius = UnscaledWheelRadius * PlayerBikeScale;
        rig.hipTiltDegrees = BaseHipTiltDegrees;
        rig.spineLeanDegrees = BaseSpineLeanDegrees;
        rig.neckLiftDegrees = BaseNeckLiftDegrees;
        rig.headLiftDegrees = BaseHeadLiftDegrees;
        rig.shoulderDropDegrees = BaseShoulderDropDegrees;
        rig.handTargetLocalOffset = BaseHandTargetLocalOffset;
        rig.gripSpreadMetres = BaseGripSpreadMetres;
        rig.handSeatOffsetL = Vector3.zero;
        rig.handSeatOffsetR = Vector3.zero;
        rig.ankleHeight = BaseAnkleHeightMetres;
        rig.footTargetRearwardOffset = FootTargetRearwardMetres;
        rig.footHeightTrimL = FootHeightTrimLeftMetres;
        rig.footHeightTrimR = FootHeightTrimRightMetres;
        rig.footToeDownDegrees = FootToeDownDegrees;
        rig.footRollDegrees = FootRollDegrees;
        rig.kneePoleLateralOffset = KneePoleLateralMetres;
        rig.keepPedalsLevel = true;
        rig.armElbowPoleSign = -1f;
        rig.useAnatomicalArmSolver = true;
        rig.useRideCadenceForCrank = true;
        rig.rideSession = UnityEngine.Object.FindFirstObjectByType<RideSession>();
        rig.crankCadenceSmoothingSeconds = 0.22f;
        // The bike may have been replaced while KuroBikeRig still cached sockets from the old
        // object. Setup is intentionally re-entrant and refreshes those exact-name references.
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        EditorUtility.SetDirty(bike);
        EditorUtility.SetDirty(rig);
        return bike;
    }

    private static void ConfigureSaddle(GameObject player)
    {
        Transform saddleTop = ExactSingle(player, "SaddleTop");
        Vector3 delta = SaddleTopLocalPosition - saddleTop.localPosition;
        saddleTop.localPosition = SaddleTopLocalPosition;
        foreach (string name in new[] { "RaceSaddle", "SaddleRails" })
        {
            Transform part = ExactSingle(player, name);
            part.localPosition += delta;
            EditorUtility.SetDirty(part);
        }
        EditorUtility.SetDirty(saddleTop);
    }

    private static void ApplyPlayerBikeMaterials(Transform bike)
    {
        EnsureFolder(MaterialFolder);
        var canonicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AeroMaterialPath
        };
        foreach (var renderer in bike.GetComponentsInChildren<Renderer>(true))
        {
            // The prior posture root is still present when an idempotent restage begins. It owns
            // the Kuro-only aero extension material and must not be recursively cloned as if it
            // came from the imported bicycle.
            if (HasNamedAncestor(renderer.transform, PostureRootName)) continue;

            var materials = renderer.sharedMaterials;
            var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
            var sourceMaterials = sourceRenderer != null
                ? sourceRenderer.sharedMaterials
                : null;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                // Always clone from the imported bicycle, never from a prior scene override.
                // Otherwise an accidental body-material conversion on the bike becomes the next
                // staging pass's "source" and permanently nests PlayerBody/KuroBike clones.
                var source = sourceMaterials != null && i < sourceMaterials.Length
                    ? sourceMaterials[i]
                    : materials[i];
                if (source == null) continue;
                string baseName = source.name.Replace(" (Instance)", "");
                while (baseName.StartsWith("KuroBike_", StringComparison.OrdinalIgnoreCase))
                    baseName = baseName.Substring("KuroBike_".Length);
                string safeName = new string(baseName.Select(c =>
                    char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
                string path = MaterialFolder + "/KuroBike_" + safeName + ".mat";
                canonicalPaths.Add(path);
                var clone = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (clone == null)
                {
                    clone = new Material(source);
                    clone.CopyPropertiesFromMaterial(source);
                    clone.name = "KuroBike_" + safeName;
                    AssetDatabase.CreateAsset(clone, path);
                }
                else if (source != clone && AssetDatabase.GetAssetPath(source) != path)
                {
                    clone.shader = source.shader;
                    clone.CopyPropertiesFromMaterial(source);
                    clone.name = "KuroBike_" + safeName;
                }
                TunePlayerBikeMaterial(clone, baseName);
                EditorUtility.SetDirty(clone);
                if (materials[i] != clone)
                {
                    materials[i] = clone;
                    changed = true;
                }
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }
        }

        // Remove only obsolete bike-clone generations from this dedicated folder. Earlier
        // staging appended another "KuroBike_" prefix on every run; pruning them makes repeated
        // staging converge to the same seven player-owned materials.
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.StartsWith("KuroBike_", StringComparison.OrdinalIgnoreCase) &&
                !canonicalPaths.Contains(path))
                AssetDatabase.DeleteAsset(path);
        }
    }

    private static bool HasNamedAncestor(Transform transform, string name)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name == name) return true;
        return false;
    }

    private static void TunePlayerBikeMaterial(Material material, string sourceName)
    {
        Color color;
        float metallic;
        float roughness;
        if (sourceName.IndexOf("Aero_Frame", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = new Color32(31, 122, 114, 255); metallic = 0.28f; roughness = 0.58f;
        }
        else if (sourceName.IndexOf("Aero_Highlight", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = new Color32(48, 179, 187, 255); metallic = 0.20f; roughness = 0.50f;
        }
        else if (sourceName.IndexOf("Maple_Accent", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = new Color32(181, 45, 58, 255); metallic = 0.12f; roughness = 0.58f;
        }
        else if (sourceName.IndexOf("Drivetrain", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = new Color32(166, 173, 181, 255); metallic = 0.86f; roughness = 0.25f;
        }
        else if (sourceName.IndexOf("Tyre", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = new Color32(17, 18, 22, 255); metallic = 0f; roughness = 0.82f;
        }
        else if (sourceName.IndexOf("Brake", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            color = new Color32(37, 42, 48, 255); metallic = 0.32f; roughness = 0.62f;
        }
        else
        {
            color = new Color32(22, 26, 32, 255); metallic = 0.08f; roughness = 0.68f;
        }
        SetMaterialColor(material, color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("metallicFactor")) material.SetFloat("metallicFactor", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f - roughness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 1f - roughness);
        if (material.HasProperty("roughnessFactor")) material.SetFloat("roughnessFactor", roughness);
    }

    private static KuroRidePose.PostureData Data(
        string label, float hip, float spine, float neck, float head, float shoulder,
        Vector3 handOffset, float spread, Transform handL, Transform handR,
        Transform poleL, Transform poleR, float protraction, float elbowOut,
        float protractionBalance,
        float elbowDown, float elbowBack, bool constrainElbows, float upperTwist,
        float foreTwist, float handOrient, float maxTwist)
    {
        return new KuroRidePose.PostureData
        {
            displayName = label,
            extraHipTiltDegrees = hip,
            extraSpineLeanDegrees = spine,
            extraNeckLiftDegrees = neck,
            extraHeadLiftDegrees = head,
            extraShoulderDropDegrees = shoulder,
            shoulderProtractionDegrees = protraction,
            shoulderProtractionBalanceDegrees = protractionBalance,
            elbowPoleOutMetres = elbowOut,
            elbowPoleDownMetres = elbowDown,
            elbowPoleBackMetres = elbowBack,
            handTargetOffset = handOffset,
            extraGripSpreadMetres = spread,
            handTargetL = handL,
            handTargetR = handR,
            elbowPoleL = poleL,
            elbowPoleR = poleR,
            constrainElbowsToTargets = constrainElbows,
            handOrientationL = handL,
            handOrientationR = handR,
            upperArmTwistWeight = upperTwist,
            forearmTwistWeight = foreTwist,
            handOrientationWeight = handOrient,
            maxGripTwistDegrees = maxTwist,
        };
    }

    private static void BuildAeroCockpit(Transform root, Material material)
    {
        // Two compact extensions, a low integrated bridge and real elbow pads. Dimensions are
        // deliberately minimal for Kuro's chibi reach; PlayerBikeScale applies through SteerPivot.
        CylinderBetween(root, "AeroExtension_L",
            new Vector3(-0.040f, -0.012f, -0.012f),
            new Vector3(-0.040f,  0.005f,  0.102f), 0.0065f, material);
        CylinderBetween(root, "AeroExtension_R",
            new Vector3( 0.040f, -0.012f, -0.012f),
            new Vector3( 0.040f,  0.005f,  0.102f), 0.0065f, material);
        CylinderBetween(root, "AeroMountBridge",
            new Vector3(-0.058f, -0.014f, -0.010f),
            new Vector3( 0.058f, -0.014f, -0.010f), 0.006f, material);
        CylinderBetween(root, "AeroRiser_L",
            AeroPadCenterL,
            new Vector3(-0.040f, -0.012f,  0.002f), 0.0055f, material);
        CylinderBetween(root, "AeroRiser_R",
            AeroPadCenterR,
            new Vector3( 0.040f, -0.012f,  0.002f), 0.0055f, material);

        Box(root, "AeroArmPad_L", AeroPadCenterL, new Vector3(0.050f, 0.012f, 0.052f), material);
        Box(root, "AeroArmPad_R", AeroPadCenterR, new Vector3(0.050f, 0.012f, 0.052f), material);
    }

    private static void BuildAggressiveReachGrips(Transform root, Material material)
    {
        var extension = NewChild(root, AggressiveGripExtensionName);
        Vector3 startL = AggressiveGripStartL;
        Vector3 startR = AggressiveGripStartR;
        Vector3 endL = AggressiveGripContactL +
                       (AggressiveGripContactL - startL).normalized * 0.020f;
        Vector3 endR = AggressiveGripContactR +
                       (AggressiveGripContactR - startR).normalized * 0.020f;
        CylinderBetween(
            extension, "ForwardDrop_L", startL, endL, 0.011f, material);
        CylinderBetween(
            extension, "ForwardDrop_R", startR, endR, 0.011f, material);
    }

    private static void RestoreImportedCockpitPositions(Transform steer)
    {
        foreach (var child in steer.Cast<Transform>())
        {
            if (!IsImportedCockpitPart(child.name)) continue;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(child);
            if (source == null) continue;
            child.localPosition = source.localPosition;
            EditorUtility.SetDirty(child);
        }
    }

    private static bool IsImportedCockpitPart(string name)
    {
        return name == "AeroBarTops" ||
               name == "BrakeLever_L" ||
               name == "BrakeLever_R" ||
               name == "ErgoHood_L" ||
               name == "ErgoHood_R" ||
               name == "Hood_L" ||
               name == "Hood_R" ||
               name.StartsWith("CompactDrop_L_", StringComparison.Ordinal) ||
               name.StartsWith("CompactDrop_R_", StringComparison.Ordinal);
    }

    private static void CylinderBetween(
        Transform parent, string name, Vector3 a, Vector3 b, float radius, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 delta = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        go.transform.localScale = new Vector3(radius, delta.magnitude * 0.5f, radius);
        var collider = go.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void Box(
        Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        var collider = go.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material GetOrCreateAeroMaterial(GameObject player)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(AeroMaterialPath);
        if (existing != null) return existing;

        EnsureFolder(MaterialFolder);
        var bike = ExactSingle(player, "Bike");
        var source = bike.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials)
            .FirstOrDefault(m => m != null && m.name.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? bike.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null);

        Material material;
        if (source != null)
        {
            material = new Material(source);
            material.CopyPropertiesFromMaterial(source);
        }
        else
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No compatible shader for aero cockpit.");
            material = new Material(shader);
        }
        material.name = "Kuro Aero Cockpit";
        SetMaterialColor(material, new Color32(34, 39, 48, 255));
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.18f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.32f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.32f);
        AssetDatabase.CreateAsset(material, AeroMaterialPath);
        return material;
    }

    private static void SetMaterialColor(Material material, Color srgb)
    {
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", srgb.linear);
        else if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", srgb);
        else if (material.HasProperty("_Color")) material.SetColor("_Color", srgb);
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    // ======================================================================== self-test

    [MenuItem("MapleRide/Kuro/Run Four Posture Self-Test", priority = 63)]
    public static void RunSelfTest()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var log = new StringBuilder();
        int failures = 0;
        try
        {
            var player = FindRoot(scene, PlayerName);
            Check(player != null, "player root exists", ref failures, log);
            if (player == null) throw new InvalidOperationException("Cannot continue without player.");

            var rig = player.GetComponent<KuroBikeRig>();
            var pose = player.GetComponent<KuroRidePose>();
            Check(rig != null, "KuroBikeRig exists", ref failures, log);
            Check(pose != null, "KuroRidePose exists", ref failures, log);
            if (rig == null || pose == null) throw new InvalidOperationException("Posture components missing.");

            Check(rig.useRideCadenceForCrank && rig.rideSession != null &&
                  rig.rideSession.devices != null && rig.crankCadenceSmoothingSeconds >= 0.1f,
                  "player crank uses continuously blended ride cadence",
                  ref failures, log);
            int routeOrder = ((DefaultExecutionOrder)typeof(RouteFollower)
                .GetCustomAttributes(typeof(DefaultExecutionOrder), false).Single()).order;
            int rigOrder = ((DefaultExecutionOrder)typeof(KuroBikeRig)
                .GetCustomAttributes(typeof(DefaultExecutionOrder), false).Single()).order;
            int cameraOrder = ((DefaultExecutionOrder)typeof(KuroFollowCamera)
                .GetCustomAttributes(typeof(DefaultExecutionOrder), false).Single()).order;
            Check(rigOrder > routeOrder,
                  "route placement completes before Kuro's same-frame body/drivetrain solve",
                  ref failures, log);
            Check(cameraOrder > rigOrder,
                  "chase camera follows the current-frame route and pedal solve",
                  ref failures, log);
            var follow = UnityEngine.Object.FindFirstObjectByType<KuroFollowCamera>();
            Check(follow != null && follow.enableDebugOrbit,
                  "Left/Right gameplay camera orbit is enabled in production",
                  ref failures, log);
            Check(rig.rideSession != null && rig.rideSession.devices != null &&
                  !rig.rideSession.devices.mash.scoutHoldMode &&
                  rig.rideSession.devices.mash.keyboardHoldEnabled &&
                  rig.rideSession.devices.mash.keyboardHoldKey == KeyCode.UpArrow,
                  "Up Arrow no-trainer hold fallback is enabled without scout mode",
                  ref failures, log);
            var playerFollower = player.GetComponent<RouteFollower>();
            Check(playerFollower != null && !playerFollower.useArrowKeysForLaneChange,
                  "Left/Right arrows are reserved for camera orbit, not bicycle steering",
                  ref failures, log);
            Check(new[] {
                    pose.roadRacerAggressive, pose.roadRiderRelaxed,
                    pose.triathlonTimeTrial, pose.roadRiderFitness
                  }.All(p =>
                      Mathf.Abs(p.shoulderProtractionDegrees -
                                BaseShoulderProtractionDegrees) < 0.001f &&
                      Mathf.Abs(p.shoulderProtractionBalanceDegrees) <=
                                TimeTrialShoulderBalanceDegrees),
                  "all four postures use bounded named shoulder-girdle tunings",
                  ref failures, log);
            Check(pose.selectedPosture == KuroRidePose.CyclingPosture.RoadRacerAggressive,
                  "low-aero road-racer posture is the saved gameplay default",
                  ref failures, log);

            Check(CountNamed(player, "Bike") == 1, "exactly one Bike child under Kuro", ref failures, log);
            Check(CountNamed(player, "SteerPivot") == 1, "exactly one SteerPivot", ref failures, log);
            Check(CountNamed(player, PostureRootName) == 1, "no duplicate posture staging root", ref failures, log);
            KuroGogglesSetup.AppendSelfTest(player, ref failures, log);
            foreach (var name in new[] { "Pedal_L", "Pedal_R", "Axle_F", "Axle_R", "Crank_L", "Crank_R" })
                Check(CountNamed(player, name) == 1, "exactly one " + name, ref failures, log);
            var steeringPivot = ExactSingle(player, "SteerPivot");
            var steeringAxle = ExactSingle(player, "Axle_F");
            var fixedRearAxle = ExactSingle(player, "Axle_R");
            Quaternion steeringBind = steeringPivot.localRotation;
            Vector3 frontBeforeSteer = steeringAxle.position;
            Vector3 rearBeforeSteer = fixedRearAxle.position;
            steeringPivot.localRotation = Quaternion.AngleAxis(8f, Vector3.up) * steeringBind;
            bool frontRespondsToSteer =
                Vector3.Distance(frontBeforeSteer, steeringAxle.position) > 0.005f;
            bool rearStaysFixed =
                Vector3.Distance(rearBeforeSteer, fixedRearAxle.position) < 0.0001f;
            steeringPivot.localRotation = steeringBind;
            Check(frontRespondsToSteer && rearStaysFixed &&
                  steeringAxle.IsChildOf(steeringPivot) && !fixedRearAxle.IsChildOf(steeringPivot),
                  "steering rotates the front assembly without moving the rear wheel",
                  ref failures, log);

            var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Check(skins.Length > 0 && skins.Any(s => s.bones != null && s.bones.Length >= 20),
                  "Kuro remains a multi-bone skinned mesh", ref failures, log);
            Check(rig.bikePrefab != null &&
                  AssetDatabase.GetAssetPath(rig.bikePrefab) == PlayerBikeAssetPath,
                  "player uses the MapleRide aero bike asset", ref failures, log);
            var bikeRoot = ExactSingle(player, "Bike");
            var bikeRenderers = bikeRoot.GetComponentsInChildren<Renderer>(true);
            Check(bikeRenderers.Length > 30,
                  "aero bike is detailed geometry, not the old tube blockout", ref failures, log);
            Check(bikeRenderers.SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .All(m => AssetDatabase.GetAssetPath(m).StartsWith(MaterialFolder + "/")),
                  "all player bike materials are player-owned clones", ref failures, log);
            var bikeMaterialAssets = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p)
                    .StartsWith("KuroBike_", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Check(bikeMaterialAssets.Length == 7 &&
                  bikeMaterialAssets.All(p => !Path.GetFileNameWithoutExtension(p)
                      .StartsWith("KuroBike_KuroBike_", StringComparison.OrdinalIgnoreCase)),
                  "player bike material staging is idempotent (7 canonical clones)",
                  ref failures, log);
            float wheelbase = Vector3.Distance(
                ExactSingle(player, "Axle_F").position, ExactSingle(player, "Axle_R").position);
            float saddleToHood = Vector3.Distance(
                ExactSingle(player, "SaddleTop").position,
                (ExactSingle(player, "Hood_L").position + ExactSingle(player, "Hood_R").position) * 0.5f);
            float hoodWidth = Vector3.Distance(
                ExactSingle(player, "Hood_L").position, ExactSingle(player, "Hood_R").position);
            float visibleHoodWidth = Vector3.Distance(
                ExactSingle(player, "ErgoHood_L").GetComponent<Renderer>().bounds.center,
                ExactSingle(player, "ErgoHood_R").GetComponent<Renderer>().bounds.center);
            float visibleTopSpan = MeshSpanInFrame(
                ExactSingle(player, "AeroBarTops"), steeringPivot);
            Check(wheelbase > 0.66f && wheelbase < 0.70f,
                  "compact wheelbase preserved at " + wheelbase.ToString("F3") + " m",
                  ref failures, log);
            Check(saddleToHood > 0.235f && saddleToHood < 0.255f,
                  "compact saddle-to-hood reach is " + saddleToHood.ToString("F3") + " m",
                  ref failures, log);
            Check(hoodWidth >= MinimumWorldHoodWidthMetres &&
                  hoodWidth <= MaximumWorldHoodWidthMetres,
                  "visible hood width matches Kuro's shoulders (" +
                  (hoodWidth * 1000f).ToString("F0") + " mm)",
                  ref failures, log);
            Check(Mathf.Abs(visibleHoodWidth - hoodWidth) < 0.012f &&
                  visibleTopSpan >= AuthoredTopHalfWidth * 1.9f,
                  "actual handlebar/hood geometry matches the widened contact sockets",
                  ref failures, log);
            Check(new[] {
                    "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
                    "RightShoulder", "RightArm", "RightForeArm", "RightHand"
                  }.All(name => CountNamed(player, name) == 1),
                  "both complete shoulder-arm-hand bone chains are present",
                  ref failures, log);
            var aeroRenderers = ExactSingle(player, AeroVisualName)
                .GetComponentsInChildren<Renderer>(true);
            Check(aeroRenderers.Length > 0 &&
                  aeroRenderers.All(r => r.sharedMaterial != null &&
                      AssetDatabase.GetAssetPath(r.sharedMaterial) == AeroMaterialPath),
                  "aero cockpit uses its own cloned material asset", ref failures, log);

            var prior = pose.selectedPosture;
            bool priorClimb = pose.enableClimbOverlay, priorSprint = pose.enableSprintOverlay;
            pose.enableClimbOverlay = false;
            pose.enableSprintOverlay = false;
            pose.SprintOverride = 0f;

            var angles = new Dictionary<KuroRidePose.CyclingPosture, float>();
            float referenceLeftArm = ArmChainLength(player, "Left");
            float referenceRightArm = ArmChainLength(player, "Right");
            var diagnosticSkin = skins.OrderByDescending(
                s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).First();
            var bakedArmMesh = new Mesh();
            var upperArmSections = new List<float>();
            var forearmSections = new List<float>();
            foreach (KuroRidePose.CyclingPosture posture in Enum.GetValues(typeof(KuroRidePose.CyclingPosture)))
            {
                pose.SelectPosture(posture, false);
                pose.Tick(10f);
                rig.ForceSolveOnce();

                var data = pose.SelectedData;
                var handL = ExactSingle(player, "LeftHand");
                var handR = ExactSingle(player, "RightHand");
                var footL = ExactSingle(player, "LeftFoot");
                var footR = ExactSingle(player, "RightFoot");
                var pedalL = ExactSingle(player, "Pedal_L");
                var pedalR = ExactSingle(player, "Pedal_R");
                var hoodL = ExactSingle(player, "Hood_L");
                var hoodR = ExactSingle(player, "Hood_R");
                Vector3 handTargetL, handTargetR;
                if (data.handTargetL != null && data.handTargetR != null)
                {
                    handTargetL = data.handTargetL.position;
                    handTargetR = data.handTargetR.position;
                }
                else
                {
                    var steer = ExactSingle(player, "SteerPivot");
                    Vector3 handOffset = steer.TransformVector(
                        rig.handTargetLocalOffset + rig.poseHandTargetOffset);
                    Vector3 spread = steer.TransformVector(Vector3.right).normalized *
                                     (rig.gripSpreadMetres + rig.poseExtraGripSpreadMetres);
                    Vector3 seatL = steer.TransformVector(rig.handSeatOffsetL);
                    Vector3 seatR = steer.TransformVector(rig.handSeatOffsetR);
                    handTargetL = hoodL.position + handOffset - spread + seatL;
                    handTargetR = hoodR.position + handOffset + spread + seatR;
                }
                float handResidual = Mathf.Max(
                    Vector3.Distance(handL.position, handTargetL),
                    Vector3.Distance(handR.position, handTargetR));
                float footResidual = Mathf.Max(
                    Vector3.Distance(footL.position, rig.FootTargetL),
                    Vector3.Distance(footR.position, rig.FootTargetR));
                float angle = TorsoAngle(player);
                float leftReach = Vector3.Distance(
                    ExactSingle(player, "LeftArm").position, handTargetL) /
                    Mathf.Max(0.001f, ArmChainLength(player, "Left"));
                float rightReach = Vector3.Distance(
                    ExactSingle(player, "RightArm").position, handTargetR) /
                    Mathf.Max(0.001f, ArmChainLength(player, "Right"));
                float elbowL = ElbowAngle(player, "Left");
                float elbowR = ElbowAngle(player, "Right");
                var elbowBoneL = ExactSingle(player, "LeftForeArm");
                var elbowBoneR = ExactSingle(player, "RightForeArm");
                var shoulderL = ExactSingle(player, "LeftShoulder");
                var shoulderR = ExactSingle(player, "RightShoulder");
                var steerFrame = ExactSingle(player, "SteerPivot");
                float elbowOutL = Mathf.Abs(steerFrame.InverseTransformPoint(elbowBoneL.position).x);
                float elbowOutR = Mathf.Abs(steerFrame.InverseTransformPoint(elbowBoneR.position).x);
                float handWidthL = Mathf.Abs(steerFrame.InverseTransformPoint(handTargetL).x);
                float handWidthR = Mathf.Abs(steerFrame.InverseTransformPoint(handTargetR).x);
                float elbowSideL = steerFrame.InverseTransformPoint(elbowBoneL.position).x;
                float elbowSideR = steerFrame.InverseTransformPoint(elbowBoneR.position).x;
                float gripAngleL = data.handOrientationL != null
                    ? Quaternion.Angle(handL.rotation, data.handOrientationL.rotation) : 0f;
                float gripAngleR = data.handOrientationR != null
                    ? Quaternion.Angle(handR.rotation, data.handOrientationR.rotation) : 0f;
                if (posture == KuroRidePose.CyclingPosture.RoadRacerAggressive)
                {
                    Quaternion deltaL =
                        Quaternion.Inverse(data.handOrientationL.rotation) * handL.rotation;
                    Quaternion deltaR =
                        Quaternion.Inverse(data.handOrientationR.rotation) * handR.rotation;
                    deltaL.ToAngleAxis(out float deltaAngleL, out Vector3 deltaAxisL);
                    deltaR.ToAngleAxis(out float deltaAngleR, out Vector3 deltaAxisR);
                    Debug.Log("[kuro-posture] aggressive grip residual L=" +
                              deltaAngleL.ToString("F2") + "deg@" +
                              deltaAxisL.ToString("F4") + " R=" +
                              deltaAngleR.ToString("F2") + "deg@" +
                              deltaAxisR.ToString("F4"));
                }
                float shoulderBindL = rig.ShoulderParentBindDeviation(shoulderL);
                float shoulderBindR = rig.ShoulderParentBindDeviation(shoulderR);
                Vector3 armRootLocalL = steerFrame.InverseTransformPoint(
                    ExactSingle(player, "LeftArm").position);
                Vector3 armRootLocalR = steerFrame.InverseTransformPoint(
                    ExactSingle(player, "RightArm").position);
                float shoulderWidthMismatch =
                    Mathf.Abs(Mathf.Abs(armRootLocalL.x) - Mathf.Abs(armRootLocalR.x));
                float shoulderHemMismatch =
                    Mathf.Abs(armRootLocalL.y - armRootLocalR.y);
                float shoulderDepthMismatch =
                    Mathf.Abs(armRootLocalL.z - armRootLocalR.z);
                ElbowExpectations(posture, out float elbowMin, out float elbowMax,
                                  out float symmetryLimit);
                angles[posture] = angle;
                diagnosticSkin.BakeMesh(bakedArmMesh);
                float upperArmSection = MeasureArmSection(
                    diagnosticSkin, bakedArmMesh, "RightArm",
                    "RightArm", "RightForeArm", 0.65f);
                float forearmSection = MeasureArmSection(
                    diagnosticSkin, bakedArmMesh, "RightForeArm",
                    "RightForeArm", "RightHand", 0.65f);
                upperArmSections.Add(upperArmSection);
                forearmSections.Add(forearmSection);
                log.AppendLine("      baked arm sections upper/fore=" +
                    (upperArmSection * 1000f).ToString("F1") + "/" +
                    (forearmSection * 1000f).ToString("F1") + " mm");

                Check(handResidual < 0.002f,
                      posture + " palm anchors reach the visible grip sockets (" +
                      handResidual * 1000f + " mm)",
                      ref failures, log);
                Check(footResidual < 0.018f,
                      posture + " feet remain on pedals (" + footResidual * 1000f + " mm)",
                      ref failures, log);
                Check(Mathf.Max(leftReach, rightReach) < 0.975f,
                      posture + " arm targets stay inside anatomical reach (" +
                      Mathf.Max(leftReach, rightReach).ToString("P1") + ")",
                      ref failures, log);
                Check(elbowL > elbowMin && elbowL < elbowMax &&
                      elbowR > elbowMin && elbowR < elbowMax,
                      posture + " elbows meet the posture-specific natural arc (" +
                      elbowL.ToString("F1") + "/" + elbowR.ToString("F1") + " deg)",
                      ref failures, log);
                Check(Mathf.Abs(elbowL - elbowR) < symmetryLimit,
                      posture + " left/right elbow bend is visually balanced (delta " +
                      Mathf.Abs(elbowL - elbowR).ToString("F1") + " deg)",
                      ref failures, log);
                Check(elbowOutL <= handWidthL + 0.045f &&
                      elbowOutR <= handWidthR + 0.045f,
                      posture + " elbows stay down near the ribcage rather than flaring " +
                      "(out L/R " + (elbowOutL * 1000f).ToString("F0") + "/" +
                      (elbowOutR * 1000f).ToString("F0") + " mm)",
                      ref failures, log);
                float minimumArmSideSeparation =
                    posture == KuroRidePose.CyclingPosture.TriathlonTimeTrial
                        ? 0.080f
                        : 0.090f;
                Check(Mathf.Sign(elbowSideL) == Mathf.Sign(
                          steerFrame.InverseTransformPoint(handTargetL).x) &&
                      Mathf.Sign(elbowSideR) == Mathf.Sign(
                          steerFrame.InverseTransformPoint(handTargetR).x) &&
                      Mathf.Abs(elbowSideL - elbowSideR) >= minimumArmSideSeparation,
                      posture + " left/right arms remain on distinct visible sides of the torso",
                      ref failures, log);
                Check(Mathf.Max(gripAngleL, gripAngleR) < 2.5f,
                      posture + " authored palm axes align to the visible grip (" +
                      Mathf.Max(gripAngleL, gripAngleR).ToString("F1") + " deg)",
                      ref failures, log);
                if (posture == KuroRidePose.CyclingPosture.RoadRacerAggressive)
                {
                    float visibleGripL = VisiblePadResidual(
                        player, "ForwardDrop_L",
                        ExactSingle(player, "AggressiveGripContact_L").position);
                    float visibleGripR = VisiblePadResidual(
                        player, "ForwardDrop_R",
                        ExactSingle(player, "AggressiveGripContact_R").position);
                    Check(Mathf.Max(visibleGripL, visibleGripR) < 0.004f,
                          "low-aero hand sockets terminate on visible forward-drop geometry (" +
                          (visibleGripL * 1000f).ToString("F1") + "/" +
                          (visibleGripR * 1000f).ToString("F1") + " mm)",
                          ref failures, log);
                }
                Check(Mathf.Max(shoulderBindL, shoulderBindR) < 24f &&
                      Mathf.Abs(shoulderBindL - shoulderBindR) < 15f,
                      posture + " clavicles remain chest-parent-relative and balanced (" +
                      shoulderBindL.ToString("F1") + "/" + shoulderBindR.ToString("F1") + " deg)",
                      ref failures, log);
                Check(shoulderHemMismatch < MaximumShoulderRootHemMismatchMetres &&
                      shoulderDepthMismatch < MaximumShoulderRootDepthMismatchMetres &&
                      shoulderWidthMismatch < MaximumShoulderRootWidthMismatchMetres,
                      posture + " shoulder roots stay inside the skinned source envelope (hem " +
                      (shoulderHemMismatch * 1000f).ToString("F1") + " mm, depth " +
                      (shoulderDepthMismatch * 1000f).ToString("F1") + " mm, width " +
                      (shoulderWidthMismatch * 1000f).ToString("F1") + " mm)",
                      ref failures, log);
                if (posture == KuroRidePose.CyclingPosture.TriathlonTimeTrial)
                {
                    Vector3 elbowLocalL = steerFrame.InverseTransformPoint(elbowBoneL.position);
                    Vector3 elbowLocalR = steerFrame.InverseTransformPoint(elbowBoneR.position);
                    float padResidualL = VisiblePadResidual(
                        player, "AeroArmPad_L", elbowBoneL.position);
                    float padResidualR = VisiblePadResidual(
                        player, "AeroArmPad_R", elbowBoneR.position);
                    Check(Mathf.Max(padResidualL, padResidualR) < 0.020f,
                          "time-trial elbows reach the visible pad surfaces (" +
                          (padResidualL * 1000f).ToString("F1") + "/" +
                          (padResidualR * 1000f).ToString("F1") + " mm)",
                          ref failures, log);
                    log.AppendLine("      time-trial elbow local L/R=" +
                        elbowLocalL.ToString("F4") + "/" + elbowLocalR.ToString("F4"));
                }
                Check(Mathf.Abs(ArmChainLength(player, "Left") - referenceLeftArm) < 0.0005f &&
                      Mathf.Abs(ArmChainLength(player, "Right") - referenceRightArm) < 0.0005f,
                      posture + " arm bone lengths remain rigid (no stretch deformation)",
                      ref failures, log);
                bool aeroVisible = pose.aeroCockpitVisual != null && pose.aeroCockpitVisual.activeSelf;
                Check(aeroVisible == (posture == KuroRidePose.CyclingPosture.TriathlonTimeTrial),
                      posture + " aero cockpit visibility", ref failures, log);

                Vector3 headBefore = ExactSingle(player, "Head").position;
                pose.Tick(1f / 60f);
                rig.ForceSolveOnce();
                float resetDrift = Vector3.Distance(headBefore, ExactSingle(player, "Head").position);
                Check(pose.selectedPosture == posture && resetDrift < 0.003f,
                      posture + " survives another gameplay/IK tick", ref failures, log);

                log.AppendLine(string.Format(
                    "      {0}: torso={1:F1} deg hand={2:F1} mm foot={3:F1} mm " +
                    "reach L/R={4:P1}/{5:P1} elbow={6:F1}/{7:F1} deg " +
                    "gripAngle={8:F1}/{9:F1} shoulderBind={10:F1}/{11:F1}",
                    posture, angle, handResidual * 1000f, footResidual * 1000f,
                    leftReach, rightReach, elbowL, elbowR,
                    gripAngleL, gripAngleR, shoulderBindL, shoulderBindR));
            }

            float minAngle = angles.Values.Min();
            float maxAngle = angles.Values.Max();
            Check(maxAngle - minAngle >= 18f,
                  "four postures produce meaningfully distinct torso silhouettes",
                  ref failures, log);
            Check(angles[KuroRidePose.CyclingPosture.RoadRiderFitness] <
                  angles[KuroRidePose.CyclingPosture.RoadRiderRelaxed] &&
                  angles[KuroRidePose.CyclingPosture.RoadRiderRelaxed] <
                  angles[KuroRidePose.CyclingPosture.RoadRacerAggressive] &&
                  angles[KuroRidePose.CyclingPosture.RoadRacerAggressive] <
                  angles[KuroRidePose.CyclingPosture.TriathlonTimeTrial],
                  "silhouette order is fitness < relaxed < aggressive < time trial",
                  ref failures, log);

            float upperSectionRatio = upperArmSections.Max() /
                                      Mathf.Max(0.001f, upperArmSections.Min());
            float foreSectionRatio = forearmSections.Max() /
                                     Mathf.Max(0.001f, forearmSections.Min());
            Check(upperArmSections.Min() >= MinimumUpperArmSectionMetres &&
                  forearmSections.Min() >= MinimumForearmSectionMetres,
                  "fuller arm cross-sections survive every pose (upper/fore min " +
                  (upperArmSections.Min() * 1000f).ToString("F1") + "/" +
                  (forearmSections.Min() * 1000f).ToString("F1") + " mm)",
                  ref failures, log);
            Check(upperSectionRatio <= MaximumArmSectionPoseRatio &&
                  foreSectionRatio <= MaximumArmSectionPoseRatio,
                  "arm sections do not collapse under bending (upper/fore ratios " +
                  upperSectionRatio.ToString("F2") + "/" +
                  foreSectionRatio.ToString("F2") + ")",
                  ref failures, log);
            UnityEngine.Object.DestroyImmediate(bakedArmMesh);

            // Representative full crank sweep. The centreline threshold is intentionally modest:
            // final acceptance comes from the dedicated left-side renders, while this catches a
            // solver regression where the arm and leg chains actually cross in 3D.
            foreach (KuroRidePose.CyclingPosture posture in
                     Enum.GetValues(typeof(KuroRidePose.CyclingPosture)))
            {
                pose.SelectPosture(posture, false);
                pose.Tick(1f);
                rig.ForceSolveOnce();
                FindWorstLeftArmLegPhase(
                    player, rig, out float worstAxisClearance, out float worstPhase);
                Check(worstAxisClearance >= MinimumArmLegAxisClearanceMetres,
                      posture + " left arm/leg chains stay separated through crank sweep (" +
                      (worstAxisClearance * 1000f).ToString("F1") + " mm at " +
                      worstPhase.ToString("F0") + " deg)",
                      ref failures, log);
            }

            // Drivetrain regression: a pose switch must not break the opposite pedal phase,
            // wheel rotation, or the feet following their moving targets.
            pose.SelectPosture(KuroRidePose.CyclingPosture.RoadRiderRelaxed, false);
            pose.Tick(1f);
            rig.ForceSolveOnce();
            var drivePedalL = ExactSingle(player, "Pedal_L");
            var drivePedalR = ExactSingle(player, "Pedal_R");
            var driveFootL = ExactSingle(player, "LeftFoot");
            var driveFootR = ExactSingle(player, "RightFoot");
            var axleF = ExactSingle(player, "Axle_F");
            Vector3 pedalBefore = drivePedalL.position;
            Quaternion wheelBefore = axleF.localRotation;
            rig.AdvanceCrank(90f);
            float pedalTravel = Vector3.Distance(pedalBefore, drivePedalL.position);
            float movingFootResidual = Mathf.Max(
                Vector3.Distance(driveFootL.position, rig.FootTargetL),
                Vector3.Distance(driveFootR.position, rig.FootTargetR));
            Check(pedalTravel > 0.05f, "crank advance moves the pedal through its orbit",
                  ref failures, log);
            Check(Quaternion.Angle(wheelBefore, axleF.localRotation) > 1f,
                  "crank advance still drives wheel rotation", ref failures, log);
            Check(movingFootResidual < 0.018f,
                  "feet remain planted after a moving pedal stroke",
                  ref failures, log);
            rig.AdvanceCrank(-90f);

            pose.enableClimbOverlay = priorClimb;
            pose.enableSprintOverlay = priorSprint;
            pose.SprintOverride = float.NaN;
            pose.SelectPosture(prior, false);
            rig.ForceSolveOnce();
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }

        log.Insert(0, "\n============ Kuro four-posture self-test ============\n");
        log.AppendLine(failures == 0
            ? "[kuro-posture] PASS - all checks green."
            : "[kuro-posture] FAIL - " + failures + " check(s) failed.");
        log.AppendLine("====================================================");
        Debug.Log(log.ToString());
        if (failures != 0) throw new Exception("Kuro four-posture self-test failed.");
    }

    private static void Check(bool ok, string label, ref int failures, StringBuilder log)
    {
        log.AppendLine("  [" + (ok ? "ok" : "FAIL") + "] " + label);
        if (!ok) failures++;
    }

    private static void ElbowExpectations(
        KuroRidePose.CyclingPosture posture,
        out float minimum, out float maximum, out float symmetryLimit)
    {
        switch (posture)
        {
            case KuroRidePose.CyclingPosture.RoadRacerAggressive:
                minimum = 135f; maximum = 145f; symmetryLimit = 6f; return;
            case KuroRidePose.CyclingPosture.RoadRiderRelaxed:
                minimum = 116f; maximum = 133f; symmetryLimit = 10f; return;
            case KuroRidePose.CyclingPosture.TriathlonTimeTrial:
                minimum = 94f; maximum = 124f; symmetryLimit = 14f; return;
            default:
                minimum = 118f; maximum = 134f; symmetryLimit = 8f; return;
        }
    }

    [MenuItem("MapleRide/Kuro/Diagnose Shoulder Balance", priority = 63)]
    public static void DiagnoseShoulderBalance()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        try
        {
            var player = FindRoot(scene, PlayerName);
            var pose = player.GetComponent<KuroRidePose>();
            var rig = player.GetComponent<KuroBikeRig>();
            bool priorClimb = pose.enableClimbOverlay, priorSprint = pose.enableSprintOverlay;
            pose.enableClimbOverlay = pose.enableSprintOverlay = false;
            pose.SprintOverride = 0f;
            foreach (KuroRidePose.CyclingPosture posture in
                     Enum.GetValues(typeof(KuroRidePose.CyclingPosture)))
            {
                pose.SelectPosture(posture, false);
                var data = pose.SelectedData;
                float original = data.shoulderProtractionBalanceDegrees;
                float bestBalance = 0f, bestScore = float.PositiveInfinity;
                float bestPad = 0f, bestBind = 0f;
                for (float balance = -20f; balance <= 20f; balance += 1f)
                {
                    data.shoulderProtractionBalanceDegrees = balance;
                    pose.ApplySelectedPosture();
                    rig.ForceSolveOnce();
                    float left = ElbowAngle(player, "Left");
                    float right = ElbowAngle(player, "Right");
                    ElbowExpectations(posture, out float elbowMin, out float elbowMax,
                                      out float symmetryLimit);
                    float rangePenalty =
                        Mathf.Max(0f, elbowMin - left) + Mathf.Max(0f, left - elbowMax) +
                        Mathf.Max(0f, elbowMin - right) + Mathf.Max(0f, right - elbowMax);
                    float pad = 0f;
                    if (posture == KuroRidePose.CyclingPosture.TriathlonTimeTrial)
                        pad = Mathf.Max(
                            VisiblePadResidual(player, "AeroArmPad_L", rig.ElbowL.position),
                            VisiblePadResidual(player, "AeroArmPad_R", rig.ElbowR.position));
                    float bind = Mathf.Max(
                        rig.ShoulderParentBindDeviation(ExactSingle(player, "LeftShoulder")),
                        rig.ShoulderParentBindDeviation(ExactSingle(player, "RightShoulder")));
                    float score = Mathf.Abs(left - right) + rangePenalty * 2f +
                                  Mathf.Max(0f, pad - 0.019f) * 1000f +
                                  Mathf.Max(0f, bind - 23.5f) * 2f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestBalance = balance;
                        bestPad = pad;
                        bestBind = bind;
                    }
                }
                data.shoulderProtractionBalanceDegrees = bestBalance;
                pose.ApplySelectedPosture();
                rig.ForceSolveOnce();
                Debug.Log("[kuro-posture] BALANCE " + posture + " = " +
                          bestBalance.ToString("F1") + " deg; elbows " +
                          ElbowAngle(player, "Left").ToString("F1") + "/" +
                          ElbowAngle(player, "Right").ToString("F1") + " deg; score " +
                          bestScore.ToString("F1") + "; pad " +
                          (bestPad * 1000f).ToString("F1") + " mm; bind " +
                          bestBind.ToString("F1") + " deg (was " + original.ToString("F1") + ")");
                data.shoulderProtractionBalanceDegrees = original;
            }
            pose.enableClimbOverlay = priorClimb;
            pose.enableSprintOverlay = priorSprint;
            pose.SprintOverride = float.NaN;
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    private static float VisiblePadResidual(GameObject player, string padName, Vector3 elbowWorld)
    {
        var pad = ExactSingle(player, padName);
        var renderer = pad.GetComponent<Renderer>();
        return renderer != null
            ? Mathf.Sqrt(renderer.bounds.SqrDistance(elbowWorld))
            : float.PositiveInfinity;
    }

    private static float MeasureArmSection(
        SkinnedMeshRenderer skin, Mesh baked, string weightedBone,
        string startBone, string endBone, float tCenter)
    {
        int weightedIndex = Array.FindIndex(
            skin.bones, bone => bone != null && bone.name == weightedBone);
        Transform start = skin.bones.First(bone => bone != null && bone.name == startBone);
        Transform end = skin.bones.First(bone => bone != null && bone.name == endBone);
        Vector3 a = ToBakedLocal(skin, start.position);
        Vector3 b = ToBakedLocal(skin, end.position);
        Vector3 axis = b - a;
        float length = axis.magnitude;
        axis /= Mathf.Max(length, 0.000001f);
        Vector3 center = Vector3.Lerp(a, b, tCenter);
        float[] weights = BoneWeightsFor(skin.sharedMesh, weightedIndex);
        var points = new List<Vector3>();
        Vector3[] vertices = baked.vertices;
        for (int index = 0; index < vertices.Length && index < weights.Length; index++)
        {
            if (weights[index] < 0.30f) continue;
            Vector3 delta = vertices[index] - center;
            float along = Vector3.Dot(delta, axis);
            if (Mathf.Abs(along) > length * 0.10f) continue;
            Vector3 radial = delta - axis * along;
            if (radial.magnitude < 0.14f) points.Add(radial);
        }
        if (points.Count < 8) return 0f;
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 point in points) centroid += point;
        centroid /= points.Count;
        var radii = points.Select(point => (point - centroid).magnitude).ToList();
        radii.Sort();
        return 2f * radii[Mathf.Clamp(
            Mathf.RoundToInt((radii.Count - 1) * 0.90f), 0, radii.Count - 1)];
    }

    private static float[] BoneWeightsFor(Mesh mesh, int boneIndex)
    {
        var result = new float[mesh.vertexCount];
        var perVertex = mesh.GetBonesPerVertex();
        var allWeights = mesh.GetAllBoneWeights();
        int cursor = 0;
        for (int vertex = 0; vertex < perVertex.Length; vertex++)
        {
            int count = perVertex[vertex];
            for (int membership = 0; membership < count; membership++, cursor++)
                if (allWeights[cursor].boneIndex == boneIndex)
                    result[vertex] += allWeights[cursor].weight;
        }
        return result;
    }

    private static Vector3 ToBakedLocal(SkinnedMeshRenderer skin, Vector3 world)
    {
        return Quaternion.Inverse(skin.transform.rotation) * (world - skin.transform.position);
    }

    private static float LeftArmLegAxisClearance(GameObject player)
    {
        Transform upperArm = ExactSingle(player, "LeftArm");
        Transform forearm = ExactSingle(player, "LeftForeArm");
        Transform hand = ExactSingle(player, "LeftHand");
        Transform thigh = ExactSingle(player, "LeftUpLeg");
        Transform shin = ExactSingle(player, "LeftLeg");
        Transform foot = ExactSingle(player, "LeftFoot");
        float minimum = float.PositiveInfinity;
        foreach (var arm in new[]
        {
            Tuple.Create(upperArm.position, forearm.position),
            Tuple.Create(forearm.position, hand.position)
        })
        foreach (var leg in new[]
        {
            Tuple.Create(thigh.position, shin.position),
            Tuple.Create(shin.position, foot.position)
        })
            minimum = Mathf.Min(minimum,
                SampledSegmentDistance(arm.Item1, arm.Item2, leg.Item1, leg.Item2));
        return minimum;
    }

    private static float SampledSegmentDistance(
        Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
    {
        float minimum = float.PositiveInfinity;
        for (int a = 0; a <= 20; a++)
        for (int b = 0; b <= 20; b++)
            minimum = Mathf.Min(minimum, Vector3.Distance(
                Vector3.Lerp(a0, a1, a / 20f),
                Vector3.Lerp(b0, b1, b / 20f)));
        return minimum;
    }

    // ======================================================================== capture

    [MenuItem("MapleRide/Kuro/Capture Four Cycling Postures", priority = 64)]
    public static void CaptureAll()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutputDirectory);
        try
        {
            var player = FindRoot(scene, PlayerName);
            if (player == null) throw new InvalidOperationException("Kuro player root is missing.");
            var pose = player.GetComponent<KuroRidePose>();
            var rig = player.GetComponent<KuroBikeRig>();
            if (pose == null || rig == null)
                throw new InvalidOperationException("Run KuroCyclingPostureSetup.Stage first.");

            var streamer = UnityEngine.Object.FindFirstObjectByType<RouteDressingStreamer>();
            if (streamer != null) streamer.ShowAll();

            var prior = pose.selectedPosture;
            bool priorClimb = pose.enableClimbOverlay, priorSprint = pose.enableSprintOverlay;
            pose.enableClimbOverlay = false;
            pose.enableSprintOverlay = false;
            pose.SprintOverride = 0f;

            CapturePosture(player, pose, rig,
                KuroRidePose.CyclingPosture.RoadRacerAggressive, "01_aggressive");
            CapturePosture(player, pose, rig,
                KuroRidePose.CyclingPosture.RoadRiderRelaxed, "02_relaxed");
            CapturePosture(player, pose, rig,
                KuroRidePose.CyclingPosture.TriathlonTimeTrial, "03_time_trial");
            CapturePosture(player, pose, rig,
                KuroRidePose.CyclingPosture.RoadRiderFitness, "04_fitness");

            pose.enableClimbOverlay = priorClimb;
            pose.enableSprintOverlay = priorSprint;
            pose.SprintOverride = float.NaN;
            pose.SelectPosture(prior, false);
            rig.ForceSolveOnce();
            Debug.Log("[kuro-posture] captures written to " + OutputDirectory);
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    [MenuItem("MapleRide/Kuro/Capture Neutral Jersey Symmetry", priority = 65)]
    public static void CaptureNeutralJersey()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutputDirectory);
        GameObject neutral = null;
        GameObject player = null;
        try
        {
            player = FindRoot(scene, PlayerName);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCharacterAssetPath);
            if (player == null || prefab == null)
                throw new InvalidOperationException("Kuro player or neutral character asset is missing.");

            neutral = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (neutral == null) neutral = UnityEngine.Object.Instantiate(prefab);
            neutral.name = "~Kuro Neutral Jersey";
            neutral.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            neutral.transform.localScale = player.transform.localScale;
            player.SetActive(false);

            foreach (var skin in neutral.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.forceMatrixRecalculationPerRender = true;
                skin.updateWhenOffscreen = true;
            }

            Bounds bounds = RendererBounds(neutral);
            Vector3 aim = bounds.center + Vector3.up * bounds.extents.y * 0.08f;
            Vector3 forward = neutral.transform.forward;
            Vector3 right = neutral.transform.right;
            float distance = Mathf.Max(1.35f, bounds.size.y * 1.20f);
            float lift = bounds.size.y * 0.04f;

            Shot("00_neutral_front", aim + forward * distance + Vector3.up * lift, aim, 31f);
            Shot("00_neutral_rear", aim - forward * distance + Vector3.up * lift, aim, 31f);
            Shot("00_neutral_front_left34",
                 aim + forward * (distance * 0.72f) - right * (distance * 0.72f)
                     + Vector3.up * lift,
                 aim, 31f);
            Shot("00_neutral_front_right34",
                 aim + forward * (distance * 0.72f) + right * (distance * 0.72f)
                     + Vector3.up * lift,
                 aim, 31f);
            Debug.Log("[kuro-posture] neutral jersey captures written to " + OutputDirectory);
        }
        finally
        {
            if (player != null) player.SetActive(true);
            if (neutral != null) UnityEngine.Object.DestroyImmediate(neutral);
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    [MenuItem("MapleRide/Kuro/Capture Left Arm Clearance Sweep", priority = 66)]
    public static void CaptureLeftArmClearanceSweep()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutputDirectory);
        try
        {
            var player = FindRoot(scene, PlayerName);
            var pose = player.GetComponent<KuroRidePose>();
            var rig = player.GetComponent<KuroBikeRig>();
            var names = new Dictionary<KuroRidePose.CyclingPosture, string>
            {
                { KuroRidePose.CyclingPosture.RoadRacerAggressive, "01_aggressive" },
                { KuroRidePose.CyclingPosture.RoadRiderRelaxed, "02_relaxed" },
                { KuroRidePose.CyclingPosture.TriathlonTimeTrial, "03_time_trial" },
                { KuroRidePose.CyclingPosture.RoadRiderFitness, "04_fitness" },
            };
            bool priorClimb = pose.enableClimbOverlay, priorSprint = pose.enableSprintOverlay;
            pose.enableClimbOverlay = pose.enableSprintOverlay = false;
            pose.SprintOverride = 0f;
            foreach (var pair in names)
            {
                pose.SelectPosture(pair.Key, false);
                pose.Tick(1f);
                rig.ForceSolveOnce();
                CaptureLeftSide(player, pair.Value + "_left_phase000");
                FindWorstLeftArmLegPhase(
                    player, rig, out float worstClearance, out float worstPhase);
                rig.AdvanceCrank(worstPhase);
                CaptureLeftSide(player, pair.Value + "_left_worst" +
                    worstPhase.ToString("000"));
                rig.AdvanceCrank(-worstPhase);
                Debug.Log("[kuro-posture] " + pair.Key + " captured worst left clearance " +
                          (worstClearance * 1000f).ToString("F1") + " mm at " +
                          worstPhase.ToString("F0") + " deg");
            }
            pose.enableClimbOverlay = priorClimb;
            pose.enableSprintOverlay = priorSprint;
            pose.SprintOverride = float.NaN;
            Debug.Log("[kuro-posture] left-arm clearance captures written to " + OutputDirectory);
        }
        finally
        {
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    private static void CaptureLeftSide(GameObject player, string name)
    {
        var hips = ExactSingle(player, "Hips");
        var head = ExactSingle(player, "Head");
        Vector3 aim = Vector3.Lerp(hips.position, head.position, 0.43f)
                    + player.transform.forward * 0.05f;
        Shot(name, aim - player.transform.right * 1.55f + Vector3.up * 0.08f,
             aim, 31f);
    }

    private static void FindWorstLeftArmLegPhase(
        GameObject player, KuroBikeRig rig, out float worstClearance, out float worstPhase)
    {
        worstClearance = float.PositiveInfinity;
        worstPhase = 0f;
        int steps = Mathf.RoundToInt(360f / ArmLegCrankSweepStepDegrees);
        for (int step = 0; step < steps; step++)
        {
            if (step != 0) rig.AdvanceCrank(ArmLegCrankSweepStepDegrees);
            float phase = step * ArmLegCrankSweepStepDegrees;
            float clearance = LeftArmLegAxisClearance(player);
            if (clearance < worstClearance)
            {
                worstClearance = clearance;
                worstPhase = phase;
            }
        }
        if (steps > 1)
            rig.AdvanceCrank(-ArmLegCrankSweepStepDegrees * (steps - 1));
    }

    private static void CapturePosture(
        GameObject player, KuroRidePose pose, KuroBikeRig rig,
        KuroRidePose.CyclingPosture posture, string prefix)
    {
        pose.SelectPosture(posture, false);
        pose.Tick(1f);
        rig.ForceSolveOnce();
        foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            skin.forceMatrixRecalculationPerRender = true;
            skin.updateWhenOffscreen = true;
        }

        var hips = ExactSingle(player, "Hips");
        var head = ExactSingle(player, "Head");
        Vector3 aim = Vector3.Lerp(hips.position, head.position, 0.48f);
        Vector3 forward = player.transform.forward;
        Vector3 right = player.transform.right;
        const float distance = 2.05f;

        Shot(prefix + "_side", aim + right * distance + Vector3.up * 0.14f, aim, 35f);
        Shot(prefix + "_front", aim + forward * distance + Vector3.up * 0.14f, aim, 35f);
        Shot(prefix + "_front_left34",
             aim + forward * (distance * 0.72f) - right * (distance * 0.72f)
                 + Vector3.up * 0.14f,
             aim, 35f);
        Shot(prefix + "_rear", aim - forward * distance + Vector3.up * 0.14f, aim, 35f);

        Debug.Log(string.Format(
            "[kuro-posture] {0}: torso={1:F1} deg, elbows L/R={2:F1}/{3:F1} deg",
            posture, TorsoAngle(player), ElbowAngle(player, "Left"), ElbowAngle(player, "Right")));
    }

    private static void Shot(string name, Vector3 position, Vector3 lookAt, float fov)
    {
        var ambience = RegionDirector.SakuraAmbience;
        var go = new GameObject("~KuroPostureCamera");
        var camera = go.AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.LookAt(lookAt, Vector3.up);
        camera.fieldOfView = fov;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 14000f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        fx.bloomThreshold = ambience.bloomThreshold;
        fx.bloomIntensity = ambience.bloomIntensity;
        fx.exposure = ambience.exposure;
        fx.saturation = ambience.saturation;
        fx.contrast = ambience.contrast;
        fx.lift = ambience.lift;
        fx.gain = ambience.gain;
        fx.vignetteStrength = ambience.vignette;
        fx.vignetteSoftness = ambience.vignetteSoftness;
        fx.dofStrength = 0f;

        const int width = 1200, height = 1000;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4
        };
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("[kuro-posture] wrote " + name + ".png");
    }

    // ======================================================================== shared helpers

    private static float MeshSpanInFrame(Transform meshObject, Transform frame)
    {
        var filter = meshObject != null ? meshObject.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null || frame == null) return 0f;
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (Vector3 vertex in filter.sharedMesh.vertices)
        {
            float x = frame.InverseTransformPoint(meshObject.TransformPoint(vertex)).x;
            min = Mathf.Min(min, x);
            max = Mathf.Max(max, x);
        }
        return max > min ? max - min : 0f;
    }

    private static Bounds RendererBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException(root.name + " has no renderers.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static GameObject FindRoot(Scene scene, string exactName)
    {
        return scene.GetRootGameObjects().SingleOrDefault(root => root.name == exactName);
    }

    private static Transform ExactSingle(GameObject root, string exactName)
    {
        var matches = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == exactName).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Expected exactly one '" + exactName + "' under " + root.name + ", found " + matches.Length + ".");
        return matches[0];
    }

    private static int CountNamed(GameObject root, string exactName)
    {
        return root.GetComponentsInChildren<Transform>(true).Count(t => t.name == exactName);
    }

    private static void PruneExactChildren(Transform parent, string exactName)
    {
        var matches = new List<Transform>();
        foreach (Transform child in parent)
            if (child.name == exactName) matches.Add(child);
        foreach (var child in matches) UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static Transform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private static Transform Socket(Transform parent, string name, Vector3 localPosition)
    {
        var socket = NewChild(parent, name);
        socket.localPosition = localPosition;
        return socket;
    }

    private static Transform Socket(
        Transform parent, string name, Vector3 localPosition, Vector3 localEuler)
    {
        var socket = Socket(parent, name, localPosition);
        socket.localRotation = Quaternion.Euler(localEuler);
        return socket;
    }

    private static Transform Socket(
        Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
    {
        var socket = Socket(parent, name, localPosition);
        socket.localRotation = localRotation;
        return socket;
    }

    private static float TorsoAngle(GameObject player)
    {
        var hips = ExactSingle(player, "Hips");
        var head = ExactSingle(player, "Head");
        Vector3 vector = head.position - hips.position;
        float forward = Vector3.Dot(vector, player.transform.forward);
        float up = Vector3.Dot(vector, player.transform.up);
        return Mathf.Atan2(forward, up) * Mathf.Rad2Deg;
    }

    private static float ArmChainLength(GameObject player, string side)
    {
        var arm = ExactSingle(player, side + "Arm");
        var forearm = ExactSingle(player, side + "ForeArm");
        var hand = ExactSingle(player, side + "Hand");
        return Vector3.Distance(arm.position, forearm.position) +
               Vector3.Distance(forearm.position, hand.position);
    }

    private static float ElbowAngle(GameObject player, string side)
    {
        var arm = ExactSingle(player, side + "Arm");
        var forearm = ExactSingle(player, side + "ForeArm");
        var hand = ExactSingle(player, side + "Hand");
        Vector3 upper = forearm.position - arm.position;
        Vector3 lower = hand.position - forearm.position;
        return 180f - Vector3.Angle(upper, lower);
    }
}

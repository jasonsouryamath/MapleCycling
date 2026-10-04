using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Single staging authority for every production NPC's Kuro-matched body scale, bicycle and
/// RoadRacerAggressive fit.  Roster files own identity, route, pace, greeting and livery only.
/// </summary>
public static class NpcCanonicalConformance
{
    public const string BikeAssetPath =
        "Assets/Kuro/Player/kuro_bike_mapleride_aero.glb";
    public const float RiderScale = 1f; // production GLBs are normalized to Kuro's envelope
    public const float BikeScale = KuroCyclingPostureSetup.PlayerBikeScale;
    public const float WheelRadius = KuroCyclingPostureSetup.UnscaledWheelRadius * BikeScale;
    public const float BubbleHeight = 1.35f;
    public const string PostureRootName = "NPC Canonical RoadRacerAggressive";
    const float CanonicalBikeTopY = 0.6734f;

    public static readonly string[] FrameMaterialNames =
        { "Colnago_Racing_Red", "MR_Aero_Frame" };
    public static readonly string[] AccentMaterialNames =
        { "Colnago_Carbon_Black", "MR_Maple_Accent", "MR_Aero_Highlight" };

    static readonly Vector3 SaddleTopLocalPosition = new Vector3(0f, 0.498f, -0.026f);
    static readonly Vector3 AggressiveDropL = new Vector3(-0.078f, -0.050f, 0.092f);
    static readonly Vector3 AggressiveDropR = new Vector3( 0.078f, -0.052f, 0.083f);
    static readonly Vector3 AggressiveGripStartL = new Vector3(-0.108f, -0.090f, 0.030f);
    static readonly Vector3 AggressiveGripStartR = new Vector3( 0.108f, -0.090f, 0.030f);
    static readonly Vector3 AggressiveGripContactL = new Vector3(-0.159f, -0.066f, 0.124f);
    static readonly Vector3 AggressiveGripContactR = new Vector3( 0.159f, -0.066f, 0.121f);
    // The production donor skins have a consistent bind-space glove offset relative to their
    // solved Hand bones. Keep the canonical sockets unchanged and move only the small, generated
    // contact sleeves so the visible gloves terminate on geometry instead of floating beside it.
    static readonly Vector3 NpcGripVisualOffsetL = new Vector3(0.0325f, 0.0029f, 0.0060f);
    static readonly Vector3 NpcGripVisualOffsetR = new Vector3(-0.0059f, 0.0314f, 0.0148f);
    static readonly Quaternion AggressiveGripRotationL =
        Quaternion.LookRotation(
            new Vector3(-0.517f, -0.579f, -0.631f),
            new Vector3(-0.012f, -0.732f, 0.681f)) *
        Quaternion.AngleAxis(
            -29.9f, new Vector3(0.4473f, 0.7999f, -0.4002f).normalized);
    static readonly Quaternion AggressiveGripRotationR =
        Quaternion.LookRotation(
            new Vector3(0.579f, -0.563f, -0.590f),
            new Vector3(-0.061f, -0.751f, 0.657f)) *
        Quaternion.AngleAxis(
            -18.4f, new Vector3(0.7123f, -0.3860f, 0.5863f).normalized);

    public static float RigScaleForAnyProductionNpc() => RiderScale;

    /// <summary>Pushes the complete canonical fit onto a newly-created, still-unposed rig.</summary>
    public static void Configure(CoralBikeRig rig)
    {
        if (rig == null) throw new ArgumentNullException(nameof(rig));
        Transform rider = rig.transform;
        rider.localScale = Vector3.one * RiderScale;
        Transform bike = ExactSingle(rider, "Bike");
        bike.localPosition = Vector3.zero;
        bike.localRotation = Quaternion.identity;
        bike.localScale = Vector3.one * BikeScale;

        rig.bikeLocalOffset = Vector3.zero;
        rig.wheelRadius = WheelRadius;
        rig.gearRatio = 2.8f;
        rig.hipTiltDegrees = 0f;
        rig.spineLeanDegrees = 11f;
        rig.neckLiftDegrees = -22f;
        rig.headLiftDegrees = -28f;
        rig.shoulderDropDegrees = 7f;
        rig.riderLateralOffset = 0f;
        rig.handTargetLocalOffset = new Vector3(0f, 0.010f, 0f);
        rig.gripSpreadMetres = 0f;
        rig.handSeatOffsetL = Vector3.zero;
        rig.handSeatOffsetR = Vector3.zero;
        rig.ankleHeight = 0.101f;
        rig.footTargetRearwardOffset = 0.062f;
        rig.footHeightTrimL = 0.0005f;
        rig.footHeightTrimR = 0.0025f;
        rig.footToeDownDegrees = 5f;
        rig.footRollDegrees = 5f;
        rig.kneePoleLateralOffset = 0.060f;
        rig.keepPedalsLevel = true;
        rig.armElbowPoleSign = -1f;
        rig.useAnatomicalArmSolver = true;
        rig.useRideCadenceForCrank = false;

        rig.poseExtraHipTiltDegrees = 29f;
        rig.poseExtraSpineLeanDegrees = 52f;
        rig.poseExtraNeckLiftDegrees = -10f;
        // Matches the player's own shipped "Road Racer / Aggressive" posture exactly
        // (KuroRidePose.roadRacerAggressive.extraHeadLiftDegrees = -12, the one production
        // players actually ride with). This field had drifted to -18.5 while every other field
        // in this pose - hip/spine/neck lift, shoulder drop/protraction, elbow pole offsets,
        // twist/grip weights - was kept byte-for-byte identical to the player's authored value.
        // The extra 6.5 degrees of HEAD-ONLY rotation is enough to balloon the donor sculpt's
        // helmet/hair mass into an oversized, ill-defined blob: SeatRider's own comment notes
        // this sculpt's Head bone "carries far less of the mesh than its size suggests," so a
        // small additional head-only tilt swings a disproportionately large hanging mass. This
        // was the systemic "bobblehead" defect across every NPC using the canonical fit
        // (road peloton and boulevard crowd alike) - restoring parity with the player's verified
        // number removes it without touching anything else about the pose.
        rig.poseExtraHeadLiftDegrees = -12f;
        rig.poseExtraShoulderDropDegrees = 0f;
        rig.poseUseWorldAlignedSpineLean = true;
        rig.poseShoulderProtractionDegrees = 3f;
        rig.poseShoulderProtractionBalanceDegrees = 2f;
        rig.poseElbowPoleOutMetres = 0.006f;
        rig.poseElbowPoleDownMetres = 0.082f;
        rig.poseElbowPoleBackMetres = 0.010f;
        rig.poseConstrainElbowsToTargets = false;
        rig.poseUpperArmTwistWeight = 0.09f;
        rig.poseForearmTwistWeight = 0.28f;
        rig.poseHandOrientationWeight = 0.80f;
        rig.poseMaxGripTwistDegrees = 42f;

        ConfigureSaddle(rider);
        MatchCanonicalBikeTop(rider);
        rig.ForceSolveOnce();
        Transform steer = rig.SteerPivot;
        if (steer == null)
        {
            var matches = rider.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "SteerPivot").ToArray();
            if (matches.Length == 1) steer = matches[0];
        }
        if (steer == null)
            throw new InvalidOperationException($"{rider.name}: canonical bike has no SteerPivot.");
        PruneExactChildren(steer, PostureRootName);
        var postureRoot = NewChild(steer, PostureRootName);
        BuildAggressiveReachGrips(postureRoot, BikeMaterial(rider));
        OffsetGripVisual(rider, postureRoot, "ForwardDrop_L", NpcGripVisualOffsetL);
        OffsetGripVisual(rider, postureRoot, "ForwardDrop_R", NpcGripVisualOffsetR);
        var left = Socket(postureRoot, "AggressiveDrop_L", AggressiveDropL,
                          AggressiveGripRotationL);
        var right = Socket(postureRoot, "AggressiveDrop_R", AggressiveDropR,
                           AggressiveGripRotationR);
        rig.poseHandTargetOverrideL = left;
        rig.poseHandTargetOverrideR = right;
        rig.poseHandOrientationTargetL = left;
        rig.poseHandOrientationTargetR = right;
        rig.ForceSolveOnce();
        EditorUtility.SetDirty(bike);
        EditorUtility.SetDirty(rig);
    }

    /// <summary>
    /// Re-solves after the route root has received its final pitch/yaw, then moves only the
    /// generated contact sleeves onto the visible donor gloves. The canonical hand sockets and
    /// imported bike stay untouched.
    /// </summary>
    public static void FinalizeStagedPose(CoralBikeRig rig)
    {
        if (rig == null) return;
        rig.ForceSolveOnce();
        AlignGripVisual(rig.transform, "LeftHand", "ForwardDrop_L");
        AlignGripVisual(rig.transform, "RightHand", "ForwardDrop_R");
        EditorUtility.SetDirty(rig);
    }

    static void ConfigureSaddle(Transform rider)
    {
        Transform saddleTop = ExactSingle(rider, "SaddleTop");
        Vector3 delta = SaddleTopLocalPosition - saddleTop.localPosition;
        saddleTop.localPosition = SaddleTopLocalPosition;
        foreach (string name in new[] { "RaceSaddle", "SaddleRails" })
        {
            var matches = rider.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == name).ToArray();
            if (matches.Length == 1) matches[0].localPosition += delta;
        }
        EditorUtility.SetDirty(saddleTop);
    }

    static void MatchCanonicalBikeTop(Transform rider)
    {
        Transform bike = ExactSingle(rider, "Bike");
        float top = float.NegativeInfinity;
        foreach (var mf in bike.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || HasNamedAncestor(mf.transform, PostureRootName)) continue;
            foreach (var vertex in mf.sharedMesh.vertices)
                top = Mathf.Max(top, rider.InverseTransformPoint(
                    mf.transform.TransformPoint(vertex)).y);
        }
        if (!float.IsFinite(top)) return;
        float delta = CanonicalBikeTopY - top;
        if (Mathf.Abs(delta) < 0.0001f) return;
        // Match the player's visible saddle shell without moving the anatomical SaddleTop
        // socket; moving that socket would raise the rider away from canonical hood/pedal targets.
        foreach (string name in new[] { "RaceSaddle", "SaddleRails" })
        {
            Transform part = ExactSingle(rider, name);
            part.position += rider.up * delta;
            EditorUtility.SetDirty(part);
        }
    }

    static void AlignGripVisual(Transform rider, string handBone, string visualName)
    {
        Transform visual = ExactSingle(rider, visualName);
        var targets = visual.GetComponentsInChildren<Renderer>(true)
            .Select(r => r.bounds).ToArray();
        if (targets.Length == 0) return;
        float best = float.PositiveInfinity;
        Vector3 correction = Vector3.zero;
        var names = new HashSet<string> { handBone };
        foreach (var smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var weights = smr.sharedMesh.boneWeights;
            for (int i = 0; i < verts.Length; i++)
            {
                if (!HasBoneWeight(smr, weights[i], names)) continue;
                Vector3 point = smr.transform.TransformPoint(verts[i]);
                foreach (var bounds in targets)
                {
                    Vector3 delta = point - bounds.ClosestPoint(point);
                    if (delta.sqrMagnitude < best)
                    {
                        best = delta.sqrMagnitude;
                        correction = delta;
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        if (best > 0f && best < 0.01f)
        {
            visual.position += correction;
            EditorUtility.SetDirty(visual);
        }
    }

    static bool HasBoneWeight(
        SkinnedMeshRenderer smr, BoneWeight weight, HashSet<string> names)
    {
        int[] indices =
            { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
        float[] values = { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
        float total = 0f;
        for (int i = 0; i < indices.Length; i++)
            if (indices[i] >= 0 && indices[i] < smr.bones.Length &&
                smr.bones[indices[i]] != null && names.Contains(smr.bones[indices[i]].name))
                total += values[i];
        return total > 0.5f;
    }

    static bool HasNamedAncestor(Transform transform, string exactName)
    {
        for (Transform t = transform; t != null; t = t.parent)
            if (t.name == exactName) return true;
        return false;
    }

    static Transform Socket(Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        return go.transform;
    }

    static void BuildAggressiveReachGrips(Transform root, Material material)
    {
        Vector3 endL = AggressiveGripContactL +
                       (AggressiveGripContactL - AggressiveGripStartL).normalized * 0.020f;
        Vector3 endR = AggressiveGripContactR +
                       (AggressiveGripContactR - AggressiveGripStartR).normalized * 0.020f;
        CylinderBetween(root, "ForwardDrop_L", AggressiveGripStartL, endL, 0.011f, material);
        CylinderBetween(root, "ForwardDrop_R", AggressiveGripStartR, endR, 0.011f, material);
    }

    static void OffsetGripVisual(
        Transform rider, Transform postureRoot, string exactName, Vector3 riderLocalOffset)
    {
        Transform visual = ExactSingle(postureRoot, exactName);
        visual.position += rider.TransformDirection(riderLocalOffset);
    }

    static void CylinderBetween(
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

    static Material BikeMaterial(Transform rider)
    {
        foreach (var renderer in rider.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null &&
                    (material.name.Contains("MR_Brake_Black") ||
                     material.name.Contains("MR_Carbon")))
                    return material;
        return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
    }

    static Transform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform ExactSingle(Transform root, string name)
    {
        var matches = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == name).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"{root.name}: expected one exact '{name}', found {matches.Length}.");
        return matches[0];
    }

    static void PruneExactChildren(Transform parent, string name)
    {
        foreach (Transform child in parent.Cast<Transform>().Where(t => t.name == name).ToArray())
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }
}

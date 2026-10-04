using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Play-mode-only renderer for Kuro's face, profiles, and contact invariants.</summary>
public sealed class KuroTorsoRuntimeRunner : MonoBehaviour
{
    public static bool Finished;
    public static bool Failed;

    private const string PlayerName = "Kuro on Sakura Pass";
    private GameObject _player;
    private KuroBikeRig _rig;
    private KuroRidePose _pose;

    public void CaptureNow()
    {
        Finished = false;
        Failed = false;
        _player = GameObject.Find(PlayerName);
        _rig = _player != null ? _player.GetComponent<KuroBikeRig>() : null;
        _pose = _player != null ? _player.GetComponent<KuroRidePose>() : null;
        if (_player == null || _rig == null || _pose == null)
            throw new InvalidOperationException(
                "Runtime validation could not resolve the active SakuraPass player.");

        var skin = _player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .OrderByDescending(renderer =>
                renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0)
            .FirstOrDefault();
        if (skin == null || skin.sharedMesh == null)
            throw new InvalidOperationException("Runtime player has no skinned body mesh.");
        if (skin.sharedMesh.vertexCount != 41496 || skin.bones.Length != 24)
            throw new InvalidOperationException(
                "Runtime player skin does not match the canonical mesh: vertices=" +
                skin.sharedMesh.vertexCount + ", bones=" + skin.bones.Length + ".");

        _pose.enableClimbOverlay = false;
        _pose.enableSprintOverlay = false;
        _pose.SprintOverride = 0f;
        if (_pose.selectedPosture != KuroRidePose.CyclingPosture.RoadRacerAggressive)
            throw new InvalidOperationException(
                "Runtime player did not enter gameplay in the low-aero default posture.");
        _pose.ApplySelectedPosture();
        _pose.Tick(1f);
        _rig.ForceSolveOnce();
        RunCadenceAndSteeringStability();

        string rootDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../reference/good_graphics/kuro_torso_repair"));
        string outputDirectory = Path.Combine(rootDirectory, "runtime_verified");
        Directory.CreateDirectory(outputDirectory);

        ReportRuntimeState();

        Transform hips = Find("Hips");
        Transform neck = Find("neck");
        Transform head = Find("Head");
        Vector3 faceAim = Vector3.Lerp(neck.position, head.position, 0.58f) +
                          Vector3.up * 0.10f;
        Vector3 bodyAim = Vector3.Lerp(hips.position, head.position, 0.43f) +
                          Vector3.up * 0.03f;

        Capture(Path.Combine(outputDirectory, "KuroFaceRuntime_front.png"),
            faceAim, _player.transform.forward, 1.35f, 34f, 1000, 1000);
        Capture(Path.Combine(outputDirectory, "KuroFaceRuntime_left.png"),
            faceAim, -_player.transform.right, 1.35f, 34f, 1000, 1000);
        Capture(Path.Combine(outputDirectory, "KuroFaceRuntime_right.png"),
            faceAim, _player.transform.right, 1.35f, 34f, 1000, 1000);
        Capture(Path.Combine(outputDirectory, "KuroFaceRuntime_front_left34.png"),
            faceAim,
            (_player.transform.forward - _player.transform.right).normalized,
            1.35f, 34f, 1000, 1000);

        Transform handL = Find("LeftHand");
        Transform handR = Find("RightHand");
        Transform armL = Find("LeftArm");
        Transform armR = Find("RightArm");
        Vector3 gripAim = Vector3.Lerp(
            (handL.position + handR.position) * 0.5f,
            (armL.position + armR.position) * 0.5f, 0.45f);
        Capture(Path.Combine(outputDirectory, "KuroGripRuntime_front.png"),
            gripAim, _player.transform.forward, 1.25f, 31f, 1200, 1000, 0.08f);
        Capture(Path.Combine(outputDirectory, "KuroGripRuntime_left.png"),
            gripAim, -_player.transform.right, 1.15f, 31f, 1200, 1000, 0.08f);
        Capture(Path.Combine(outputDirectory, "KuroGripRuntime_right.png"),
            gripAim, _player.transform.right, 1.15f, 31f, 1200, 1000, 0.08f);
        Capture(Path.Combine(outputDirectory, "KuroGripRuntime_top_front_left34.png"),
            gripAim,
            (_player.transform.forward - _player.transform.right).normalized,
            1.35f, 33f, 1200, 1000, 0.62f);

        string fullFront = Path.Combine(outputDirectory, "KuroFullBodyRuntime_front.png");
        string fullLeft = Path.Combine(outputDirectory, "KuroFullBodyRuntime_left.png");
        string fullRight = Path.Combine(outputDirectory, "KuroFullBodyRuntime_right.png");
        string fullThreeQuarter =
            Path.Combine(outputDirectory, "KuroFullBodyRuntime_front_left34.png");
        Capture(fullFront, bodyAim, _player.transform.forward, 2.25f, 36f, 1200, 1000);
        Capture(fullLeft, bodyAim, -_player.transform.right, 2.25f, 36f, 1200, 1000);
        Capture(fullRight, bodyAim, _player.transform.right, 2.25f, 36f, 1200, 1000);
        Capture(fullThreeQuarter, bodyAim,
            (_player.transform.forward - _player.transform.right).normalized,
            2.25f, 36f, 1200, 1000);

        File.Copy(fullFront, Path.Combine(rootDirectory, "KuroTorsoRepair_front.png"), true);
        File.Copy(fullLeft, Path.Combine(rootDirectory, "KuroTorsoRepair_left.png"), true);
        File.Copy(fullRight, Path.Combine(rootDirectory, "KuroTorsoRepair_right.png"), true);

        Debug.Log("[kuro-runtime-face] PASS - runtime face and full-body captures written to " +
                  outputDirectory);
        Finished = true;
    }

    private void ReportRuntimeState()
    {
        Transform hips = Find("Hips");
        Transform neck = Find("neck");
        Transform handL = Find("LeftHand");
        Transform handR = Find("RightHand");
        Transform targetL = _pose.SelectedData.handTargetL;
        Transform targetR = _pose.SelectedData.handTargetR;
        Transform gripContactL = Find("AggressiveGripContact_L");
        Transform gripContactR = Find("AggressiveGripContact_R");
        Transform footL = Find("LeftFoot");
        Transform footR = Find("RightFoot");
        Transform pedalL = Find("Pedal_L");
        Transform pedalR = Find("Pedal_R");
        Transform saddle = Find("SaddleTop");

        float torsoAngle = Vector3.Angle(neck.position - hips.position, Vector3.up);
        float handResidualL = Vector3.Distance(handL.position, targetL.position);
        float handResidualR = Vector3.Distance(handR.position, targetR.position);
        float footResidualL = Vector3.Distance(
            footL.position, _rig.FootTargetL);
        float footResidualR = Vector3.Distance(
            footR.position, _rig.FootTargetR);
        float saddleResidual = Vector3.Distance(hips.position, saddle.position);
        float elbowL = ElbowAngle("Left");
        float elbowR = ElbowAngle("Right");
        float reachL = Vector3.Distance(Find("LeftArm").position, targetL.position) /
                       ArmChainLength("Left");
        float reachR = Vector3.Distance(Find("RightArm").position, targetR.position) /
                       ArmChainLength("Right");
        float gripAngleL = Quaternion.Angle(
            handL.rotation, _pose.SelectedData.handOrientationL.rotation);
        float gripAngleR = Quaternion.Angle(
            handR.rotation, _pose.SelectedData.handOrientationR.rotation);
        float handSurfaceL =
            HandSurfaceDistance(handL, gripContactL.position, out Vector3 handPointL);
        float handSurfaceR =
            HandSurfaceDistance(handR, gripContactR.position, out Vector3 handPointR);
        float visibleGripL = RendererResidual("ForwardDrop_L", gripContactL.position);
        float visibleGripR = RendererResidual("ForwardDrop_R", gripContactR.position);
        Transform steer = Find("SteerPivot");
        Vector3 handOffsetL =
            steer.InverseTransformVector(handPointL - gripContactL.position);
        Vector3 handOffsetR =
            steer.InverseTransformVector(handPointR - gripContactR.position);
        LogHandBounds(handL, steer, "L");
        LogHandBounds(handR, steer, "R");

        Debug.Log(string.Format(
            "[kuro-runtime-face] posture={0}, torso={1:F1} deg, hands={2:F4}/{3:F4} m, " +
            "feet={4:F4}/{5:F4} m, saddle={6:F4} m, elbows={7:F1}/{8:F1} deg, " +
            "reach={9:P1}/{10:P1}, gripAxis={11:F1}/{12:F1} deg, " +
            "handSurface={13:F1}/{14:F1} mm, visibleGrip={15:F1}/{16:F1} mm",
            _pose.selectedPosture, torsoAngle, handResidualL, handResidualR,
            footResidualL, footResidualR, saddleResidual, elbowL, elbowR,
            reachL, reachR, gripAngleL, gripAngleR,
            handSurfaceL * 1000f, handSurfaceR * 1000f,
            visibleGripL * 1000f, visibleGripR * 1000f));
        Debug.Log("[kuro-runtime-face] closest glove surface offset from grip L/R=" +
                  handOffsetL.ToString("F4") + "/" + handOffsetR.ToString("F4") +
                  " in SteerPivot space");
        Debug.Log("[kuro-runtime-face] hand axes in SteerPivot space L(up/right/fwd)=" +
                  steer.InverseTransformDirection(handL.up).ToString("F3") + "/" +
                  steer.InverseTransformDirection(handL.right).ToString("F3") + "/" +
                  steer.InverseTransformDirection(handL.forward).ToString("F3") +
                  " R(up/right/fwd)=" +
                  steer.InverseTransformDirection(handR.up).ToString("F3") + "/" +
                  steer.InverseTransformDirection(handR.right).ToString("F3") + "/" +
                  steer.InverseTransformDirection(handR.forward).ToString("F3"));

        if (torsoAngle < 38f || torsoAngle > 45f ||
            handResidualL > 0.012f || handResidualR > 0.012f ||
            footResidualL > 0.018f || footResidualR > 0.018f ||
            saddleResidual > 0.012f ||
            elbowL < 135f || elbowL > 145f ||
            elbowR < 135f || elbowR > 145f ||
            reachL < 0.88f || reachL > 0.93f ||
            reachR < 0.88f || reachR > 0.93f ||
            Mathf.Abs(elbowL - elbowR) > 6f ||
            (_rig.poseHandOrientationWeight > 0f &&
             (             gripAngleL > 5f || gripAngleR > 5f)) ||
             handSurfaceL > 0.020f || handSurfaceR > 0.020f ||
            visibleGripL > 0.004f || visibleGripR > 0.004f)
            throw new InvalidOperationException(
                "Runtime posture or contact invariants are outside their accepted limits.");
    }

    private void RunCadenceAndSteeringStability()
    {
        float maxHand = 0f;
        float maxFoot = 0f;
        var initialData = _pose.SelectedData;
        float initialGripAngleL = Quaternion.Angle(
            Find("LeftHand").rotation, initialData.handOrientationL.rotation);
        float initialGripAngleR = Quaternion.Angle(
            Find("RightHand").rotation, initialData.handOrientationR.rotation);
        float maxGripDrift = 0f;
        const int ticks = 300;
        for (int i = 0; i < ticks; i++)
        {
            _rig.SteerOverrideDegrees = i < 100 ? 12f : i < 200 ? -12f : 0f;
            _pose.Tick(1f / 60f);
            _rig.AdvanceCrank(9f);

            var data = _pose.SelectedData;
            maxHand = Mathf.Max(maxHand,
                Vector3.Distance(Find("LeftHand").position, data.handTargetL.position),
                Vector3.Distance(Find("RightHand").position, data.handTargetR.position));
            maxFoot = Mathf.Max(maxFoot,
                Vector3.Distance(Find("LeftFoot").position,
                    _rig.FootTargetL),
                Vector3.Distance(Find("RightFoot").position,
                    _rig.FootTargetR));
            maxGripDrift = Mathf.Max(maxGripDrift,
                Mathf.Abs(Quaternion.Angle(
                    Find("LeftHand").rotation, data.handOrientationL.rotation) -
                    initialGripAngleL),
                Mathf.Abs(Quaternion.Angle(
                    Find("RightHand").rotation, data.handOrientationR.rotation) -
                    initialGripAngleR));
        }
        _rig.SteerOverrideDegrees = float.NaN;

        Debug.Log(string.Format(
            "[kuro-runtime-face] 5.0 s cadence/steering stability: hand={0:F2} mm, " +
            "foot={1:F2} mm, gripOrientationDrift={2:F2} deg",
            maxHand * 1000f, maxFoot * 1000f, maxGripDrift));
        if (maxHand > 0.012f || maxFoot > 0.018f || maxGripDrift > 3.5f)
            throw new InvalidOperationException(
                "Cadence/steering stability pulled a contact away from its authored target.");
    }

    private float ElbowAngle(string side)
    {
        Transform arm = Find(side + "Arm");
        Transform forearm = Find(side + "ForeArm");
        Transform hand = Find(side + "Hand");
        return Vector3.Angle(arm.position - forearm.position, hand.position - forearm.position);
    }

    private float ArmChainLength(string side)
    {
        Transform arm = Find(side + "Arm");
        Transform forearm = Find(side + "ForeArm");
        Transform hand = Find(side + "Hand");
        return Mathf.Max(0.001f,
            Vector3.Distance(arm.position, forearm.position) +
            Vector3.Distance(forearm.position, hand.position));
    }

    private float RendererResidual(string rendererName, Vector3 point)
    {
        var renderer = Find(rendererName).GetComponent<Renderer>();
        if (renderer == null) return float.PositiveInfinity;
        return Vector3.Distance(point, renderer.bounds.ClosestPoint(point));
    }

    private float HandSurfaceDistance(
        Transform handBone, Vector3 point, out Vector3 closestPoint)
    {
        closestPoint = point;
        var skin = _player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .OrderByDescending(renderer =>
                renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0)
            .First();
        int boneIndex = Array.IndexOf(skin.bones, handBone);
        if (boneIndex < 0) return float.PositiveInfinity;

        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        var vertices = baked.vertices;
        var weights = skin.sharedMesh.boneWeights;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length && i < weights.Length; i++)
        {
            BoneWeight weight = weights[i];
            float handWeight = 0f;
            if (weight.boneIndex0 == boneIndex) handWeight += weight.weight0;
            if (weight.boneIndex1 == boneIndex) handWeight += weight.weight1;
            if (weight.boneIndex2 == boneIndex) handWeight += weight.weight2;
            if (weight.boneIndex3 == boneIndex) handWeight += weight.weight3;
            if (handWeight < 0.25f) continue;
            Vector3 vertex = skin.transform.TransformPoint(vertices[i]);
            float distance = Vector3.Distance(vertex, point);
            if (distance < closest)
            {
                closest = distance;
                closestPoint = vertex;
            }
        }
        Destroy(baked);
        return closest;
    }

    private void LogHandBounds(Transform handBone, Transform frame, string label)
    {
        var skin = _player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .OrderByDescending(renderer =>
                renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0)
            .First();
        int boneIndex = Array.IndexOf(skin.bones, handBone);
        if (boneIndex < 0) return;

        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        var vertices = baked.vertices;
        var weights = skin.sharedMesh.boneWeights;
        var bounds = new Bounds();
        bool hasPoint = false;
        int count = 0;
        for (int i = 0; i < vertices.Length && i < weights.Length; i++)
        {
            BoneWeight weight = weights[i];
            float influence = 0f;
            if (weight.boneIndex0 == boneIndex) influence += weight.weight0;
            if (weight.boneIndex1 == boneIndex) influence += weight.weight1;
            if (weight.boneIndex2 == boneIndex) influence += weight.weight2;
            if (weight.boneIndex3 == boneIndex) influence += weight.weight3;
            if (influence < 0.5f) continue;
            Vector3 point = frame.InverseTransformPoint(
                skin.transform.TransformPoint(vertices[i]));
            if (!hasPoint)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasPoint = true;
            }
            else bounds.Encapsulate(point);
            count++;
        }
        Destroy(baked);
        if (hasPoint)
            Debug.Log("[kuro-runtime-face] glove " + label + " SteerPivot bounds center=" +
                      bounds.center.ToString("F4") + " size=" + bounds.size.ToString("F4") +
                      " vertices=" + count);
    }

    private Transform Find(string name)
    {
        return _player.GetComponentsInChildren<Transform>(true)
            .Single(transformItem => transformItem.name == name);
    }

    private static void Capture(
        string outputPath,
        Vector3 aim,
        Vector3 viewDirection,
        float distance,
        float fieldOfView,
        int width,
        int height,
        float cameraLift = 0.04f)
    {
        var cameraObject = new GameObject("~Kuro Runtime Face Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position =
            aim + viewDirection.normalized * distance + Vector3.up * cameraLift;
        camera.transform.LookAt(aim, Vector3.up);
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 14000f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = true;

        var ambience = RegionDirector.SakuraAmbience;
        var postFx = cameraObject.AddComponent<SakuraPostFX>();
        postFx.bloomThreshold = ambience.bloomThreshold;
        postFx.bloomIntensity = ambience.bloomIntensity;
        postFx.exposure = ambience.exposure;
        postFx.saturation = ambience.saturation;
        postFx.contrast = ambience.contrast;
        postFx.lift = ambience.lift;
        postFx.gain = ambience.gain;
        postFx.vignetteStrength = ambience.vignette;
        postFx.vignetteSoftness = ambience.vignetteSoftness;
        postFx.dofStrength = 0f;

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
        };
        camera.targetTexture = target;
        camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        RenderTexture.active = previous;

        camera.targetTexture = null;
        target.Release();
        Destroy(target);
        Destroy(image);
        Destroy(cameraObject);
        Debug.Log("[kuro-runtime-face] wrote " + Path.GetFullPath(outputPath));
    }
}

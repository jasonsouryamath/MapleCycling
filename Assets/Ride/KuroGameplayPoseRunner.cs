using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Exercises the shipped SakuraPass rider lifecycle and captures the real chase camera after
/// sustained riding on flat and inclined road. It never selects or rewrites a posture.
/// </summary>
public sealed class KuroGameplayPoseRunner : MonoBehaviour
{
    public static bool Finished;
    public static bool Failed;

    public string outDir;
    public float settleSeconds = 10.5f;
    public float playerEffort = 0.78f;

    private const string PlayerName = "Kuro on Sakura Pass";
    private RideBootstrap _boot;
    private RouteFollower _route;
    private RouteDressingStreamer _streamer;
    private GameObject _player;
    private KuroRidePose _pose;
    private KuroBikeRig _rig;
    private Camera _camera;
    private KuroFollowCamera _follow;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        try
        {
            Resolve();
            Directory.CreateDirectory(outDir);
        }
        catch (Exception exception)
        {
            Failed = true;
            Debug.LogException(exception);
            Debug.LogError("[kuro-gameplay-pose] FAIL - " + exception.Message);
            Finished = true;
            yield break;
        }

        if (_boot.encounter != null) _boot.encounter.config.encounterEnabled = false;
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("pass_sprint");
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.EffortInput = playerEffort;
        _follow.enableDebugOrbit = true;
        _follow.yawOnlyFrame = true;
        _follow.ClearModifiers();
        _follow.ResetKeyboardOrbit();

        RouteCourse course = _boot.session.Course;
        if (course == null)
        {
            Fail("SakuraPass gameplay did not resolve pass_sprint.");
            yield break;
        }

        FindTestStarts(course, out float flatStart, out float inclineStart);
        yield return ExerciseLocation("flat", flatStart, false);
        yield return ExerciseLocation("incline", inclineStart, true);

        Time.timeScale = 1f;
        _follow.modDistanceScale = 1f;
        _follow.modFovDelta = 0f;
        _follow.modHeightDelta = 0f;
        _follow.ResetKeyboardOrbit();
        if (!Failed)
            Debug.Log("[kuro-gameplay-pose] PASS - actual gameplay captures written to " + outDir);
        Finished = true;
    }

    private void Resolve()
    {
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) throw new InvalidOperationException("No RideBootstrap is active.");
        _boot.Resolve();

        _player = GameObject.Find(PlayerName);
        if (_player == null) throw new InvalidOperationException("The active SakuraPass player is missing.");
        _pose = _player.GetComponent<KuroRidePose>();
        _rig = _player.GetComponent<KuroBikeRig>();
        if (_pose == null || _rig == null)
            throw new InvalidOperationException("The active player is missing KuroRidePose/KuroBikeRig.");

        _route = _player.GetComponent<RouteFollower>();
        _streamer = FindFirstObjectByType<RouteDressingStreamer>(FindObjectsInactive.Include);
        _camera = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        _follow = _camera != null ? _camera.GetComponent<KuroFollowCamera>() : null;
        if (_route == null || _camera == null || _follow == null)
            throw new InvalidOperationException("The normal gameplay route/camera path is incomplete.");

        foreach (var npc in FindObjectsByType<NPCCyclist>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (npc != null && !npc.transform.IsChildOf(_player.transform))
                npc.gameObject.SetActive(false);
        }

        var skin = _player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .OrderByDescending(renderer =>
                renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0)
            .FirstOrDefault();
        if (skin == null || skin.sharedMesh == null ||
            skin.sharedMesh.vertexCount != 41496 || skin.bones.Length != 24)
            throw new InvalidOperationException("The active player is not using the canonical skin.");
    }

    private static void FindTestStarts(
        RouteCourse course, out float flatStart, out float inclineStart)
    {
        float bestFlat = float.PositiveInfinity;
        float bestIncline = float.NegativeInfinity;
        flatStart = 20f;
        inclineStart = 20f;

        for (float d = 20f; d < course.Length - 80f; d += 4f)
        {
            float g0 = course.GradeAt(d);
            float g1 = course.GradeAt(d + 20f);
            float g2 = course.GradeAt(d + 40f);
            float flatScore = Mathf.Max(Mathf.Abs(g0), Mathf.Abs(g1), Mathf.Abs(g2));
            if (flatScore < bestFlat)
            {
                bestFlat = flatScore;
                flatStart = d;
            }

            float inclineScore = Mathf.Min(g0, Mathf.Min(g1, g2));
            if (inclineScore > bestIncline)
            {
                bestIncline = inclineScore;
                inclineStart = d;
            }
        }

        Debug.Log(string.Format(
            "[kuro-gameplay-pose] selected starts flat={0:F1} m (worst |grade| {1:P1}), " +
            "incline={2:F1} m (minimum grade {3:P1})",
            flatStart, bestFlat, inclineStart, bestIncline));
    }

    private IEnumerator ExerciseLocation(string tag, float startDistance, bool requireIncline)
    {
        _follow.ResetKeyboardOrbit();
        _follow.modDistanceScale = 1f;
        _follow.modFovDelta = 0f;
        _follow.modHeightDelta = 0f;

        _boot.session.SeekTo(startDistance);
        _route.Apply();
        if (_streamer != null && _boot.rider != null)
            _streamer.Apply(_boot.rider.position);

        float startTime = Time.time;
        while (Time.time - startTime < settleSeconds)
        {
            _boot.devices.EffortInput = playerEffort;
            yield return null;
        }

        float grade = _boot.session.Grade;
        if (requireIncline && grade < 0.04f)
        {
            Fail(
                "Incline validation did not remain on representative climbing road: grade=" +
                grade.ToString("P1") + ".");
        }
        if (!requireIncline && Mathf.Abs(grade) > 0.035f)
        {
            Fail(
                "Flat validation drifted onto a significant grade: grade=" +
                grade.ToString("P1") + ".");
        }

        bool valid = ReportAndValidate(tag, grade);
        yield return CaptureCurrentCamera(tag + "_gameplay_chase");

        Time.timeScale = 0f;
        yield return CaptureOrbit(tag + "_front", 180f);
        yield return CaptureOrbit(tag + "_left", 90f);
        yield return CaptureOrbit(tag + "_right", -90f);
        yield return CaptureOrbit(tag + "_front_threequarter", 135f);

        _follow.SetDebugOrbitYawForValidation(90f);
        for (int phase = 0; phase < 4; phase++)
        {
            if (phase > 0) _rig.AdvanceCrank(90f);
            _follow.TickForValidation(0f);
            yield return CaptureCurrentCamera(
                tag + "_pedal_phase_" + (phase * 90).ToString("000"));
        }
        _rig.AdvanceCrank(90f);
        Time.timeScale = 1f;
        _follow.ResetKeyboardOrbit();
        _follow.modDistanceScale = 1f;
        _follow.modFovDelta = 0f;
        _follow.modHeightDelta = 0f;
        if (!valid)
            Fail(tag + " actual-gameplay posture or contact invariants are outside limits.");
        yield return null;
    }

    private bool ReportAndValidate(string tag, float grade)
    {
        Transform hips = Find("Hips");
        Transform neck = Find("neck");
        Transform head = Find("Head");
        Transform shoulderL = Find("LeftShoulder");
        Transform shoulderR = Find("RightShoulder");
        Transform handL = Find("LeftHand");
        Transform handR = Find("RightHand");
        Transform footL = Find("LeftFoot");
        Transform footR = Find("RightFoot");
        Transform saddle = Find("SaddleTop");

        Vector3 courseUp = _player.transform.up.normalized;
        Vector3 courseForward = _player.transform.forward.normalized;
        Vector3 courseRight = _player.transform.right.normalized;
        float torsoSigned = SignedSagittalAngle(
            neck.position - hips.position, courseUp, courseRight);
        float pelvisSigned = SignedSagittalAngle(hips.up, courseUp, courseRight);
        float headSigned = SignedSagittalAngle(head.up, courseUp, courseRight);
        float shoulderForward = Vector3.Dot(
            (handL.position + handR.position - shoulderL.position - shoulderR.position) * 0.5f,
            courseForward);
        float shoulderDrop = Vector3.Dot(
            (handL.position + handR.position - shoulderL.position - shoulderR.position) * 0.5f,
            courseUp);
        float saddleBone = Vector3.Distance(hips.position, saddle.position);
        float saddleMesh = PelvisSurfaceDistance(saddle.position);
        float handLResidual = Vector3.Distance(handL.position, _pose.SelectedData.handTargetL.position);
        float handRResidual = Vector3.Distance(handR.position, _pose.SelectedData.handTargetR.position);
        float footLResidual = Vector3.Distance(footL.position, _rig.FootTargetL);
        float footRResidual = Vector3.Distance(footR.position, _rig.FootTargetR);
        float elbowL = ElbowAngle("Left");
        float elbowR = ElbowAngle("Right");

        Debug.Log(string.Format(
            "[kuro-gameplay-pose] {0} after {1:F1}s: distance={2:F1} m grade={3:P1}, " +
            "posture={4}, climb={5:F3}, stand={6:F3}/{7:F3} m, " +
            "signed torso/pelvis/head={8:F1}/{9:F1}/{10:F1} deg, " +
            "shoulder-to-grip forward/down={11:F3}/{12:F3} m, " +
            "saddle bone/mesh={13:F1}/{14:F1} mm, elbows={15:F1}/{16:F1} deg, " +
            "hands={17:F1}/{18:F1} mm, feet={19:F1}/{20:F1} mm",
            tag, settleSeconds, _boot.session.DistanceM, grade,
            _pose.selectedPosture, _pose.ClimbWeight,
            _rig.poseStandRiseM, _rig.poseStandForwardM,
            torsoSigned, pelvisSigned, headSigned,
            shoulderForward, shoulderDrop,
            saddleBone * 1000f, saddleMesh * 1000f,
            elbowL, elbowR,
            handLResidual * 1000f, handRResidual * 1000f,
            footLResidual * 1000f, footRResidual * 1000f));

        if (!_pose.lockGameplayLowAeroDefault ||
            _pose.selectedPosture != KuroRidePose.CyclingPosture.RoadRacerAggressive ||
            _pose.ClimbWeight > 0.001f ||
            Mathf.Abs(_rig.poseStandRiseM) > 0.001f ||
            Mathf.Abs(_rig.poseStandForwardM) > 0.001f ||
            torsoSigned < 50f || torsoSigned > 60f ||
            pelvisSigned < 25f || pelvisSigned > 38f ||
            headSigned < -8f || headSigned > 12f ||
            shoulderForward < 0.12f ||
            shoulderDrop < -0.26f || shoulderDrop > -0.14f ||
            saddleBone > 0.012f ||
            saddleMesh > 0.020f ||
            elbowL < 134f || elbowL > 147f ||
            elbowR < 134f || elbowR > 147f ||
            Mathf.Abs(elbowL - elbowR) > 6f ||
            handLResidual > 0.012f || handRResidual > 0.012f ||
            footLResidual > 0.018f || footRResidual > 0.018f)
            return false;
        return true;
    }

    private static float SignedSagittalAngle(
        Vector3 direction, Vector3 courseUp, Vector3 courseRight)
    {
        Vector3 projected = Vector3.ProjectOnPlane(direction, courseRight);
        if (projected.sqrMagnitude < 1e-6f) return 0f;
        return Vector3.SignedAngle(courseUp, projected.normalized, courseRight);
    }

    private float PelvisSurfaceDistance(Vector3 saddlePoint)
    {
        var skin = _player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .OrderByDescending(renderer =>
                renderer.sharedMesh != null ? renderer.sharedMesh.vertexCount : 0)
            .First();
        Transform hips = Find("Hips");
        int boneIndex = Array.IndexOf(skin.bones, hips);
        if (boneIndex < 0) return float.PositiveInfinity;

        Renderer saddleRenderer = _player.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.transform.name.IndexOf(
                "saddle", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(renderer => Vector3.Distance(
                renderer.bounds.ClosestPoint(saddlePoint), saddlePoint))
            .FirstOrDefault();
        if (saddleRenderer == null) return float.PositiveInfinity;

        var baked = new Mesh();
        skin.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        BoneWeight[] weights = skin.sharedMesh.boneWeights;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length && i < weights.Length; i++)
        {
            BoneWeight weight = weights[i];
            float influence = 0f;
            if (weight.boneIndex0 == boneIndex) influence += weight.weight0;
            if (weight.boneIndex1 == boneIndex) influence += weight.weight1;
            if (weight.boneIndex2 == boneIndex) influence += weight.weight2;
            if (weight.boneIndex3 == boneIndex) influence += weight.weight3;
            if (influence < 0.20f) continue;

            Vector3 point = skin.transform.TransformPoint(vertices[i]);
            if (Vector3.ProjectOnPlane(point - saddlePoint, _player.transform.up).magnitude > 0.18f)
                continue;
            closest = Mathf.Min(
                closest, Vector3.Distance(point, saddleRenderer.bounds.ClosestPoint(point)));
        }
        Destroy(baked);
        return closest;
    }

    private float ElbowAngle(string side)
    {
        Transform arm = Find(side + "Arm");
        Transform forearm = Find(side + "ForeArm");
        Transform hand = Find(side + "Hand");
        return Vector3.Angle(arm.position - forearm.position, hand.position - forearm.position);
    }

    private IEnumerator CaptureOrbit(string name, float yaw)
    {
        _follow.modDistanceScale = 0.72f;
        _follow.modFovDelta = -3f;
        _follow.modHeightDelta = -0.08f;
        _follow.SetDebugOrbitYawForValidation(yaw);
        _follow.TickForValidation(0f);
        yield return CaptureCurrentCamera(name);
    }

    private IEnumerator CaptureCurrentCamera(string name)
    {
        yield return null;
        _follow.TickForValidation(0f);

        const int Width = 1200;
        const int Height = 1000;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = _camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        _camera.targetTexture = target;
        _camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), texture.EncodeToPNG());
        _camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        Destroy(texture);
        target.Release();
        Destroy(target);
    }

    private Transform Find(string name)
    {
        return _player.GetComponentsInChildren<Transform>(true)
            .Single(item => item.name == name);
    }

    private void Fail(string reason)
    {
        Failed = true;
        Debug.LogError("[kuro-gameplay-pose] FAIL - " + reason);
        Time.timeScale = 1f;
    }
}

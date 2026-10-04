using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Focused regression harness for Up-to-ride and Left/Right camera orbit.</summary>
public static class KeyboardControlRegressionValidation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const float Dt = 1f / 60f;

    [MenuItem("MapleRide/Ride/Run Keyboard Control Regression Test", priority = 46)]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var log = new StringBuilder();
        int failures = 0;
        GameObject host = null;
        try
        {
            var boot = UnityEngine.Object.FindFirstObjectByType<RideBootstrap>();
            if (boot == null) throw new InvalidOperationException("RideBootstrap is missing.");
            boot.Resolve();

            var player = scene.GetRootGameObjects()
                .SingleOrDefault(root => root.name == "Kuro on Sakura Pass");
            var follow = UnityEngine.Object.FindFirstObjectByType<KuroFollowCamera>();
            var pose = player != null ? player.GetComponent<KuroRidePose>() : null;
            var follower = player != null ? player.GetComponent<RouteFollower>() : null;
            var rig = player != null ? player.GetComponent<KuroBikeRig>() : null;
            Check(player != null && follow != null && pose != null &&
                  follower != null && rig != null,
                  "production keyboard owners resolve", ref failures, log);
            if (player == null || follow == null || pose == null ||
                follower == null || rig == null)
                throw new InvalidOperationException("Production keyboard owners are incomplete.");

            host = new GameObject("~KeyboardControlRegression");
            var devices = host.AddComponent<DeviceManager>();
            var session = host.AddComponent<RideSession>();
            devices.acceptKeyboardEffort = true;
            devices.effortSource = DeviceManager.EffortSource.MashPedalStrokes;
            devices.mash.keyboardHoldEnabled = true;
            devices.mash.keyboardHoldKey = KeyCode.UpArrow;
            devices.mash.keyboardHoldWatts = 220f;
            devices.mash.keyboardHoldRampSeconds = 0.45f;
            devices.mash.keyboardReleaseSeconds = 0.55f;
            devices.mash.keyboardHoldCadenceRpm = 88f;
            devices.mash.scoutHoldMode = false;
            session.devices = devices;
            session.courseId = "sakura_circuit";
            session.EnsureCourse();
            float start = FindFlatDistance(session);
            session.SeekTo(start);

            devices.mash.keyboardHoldOverride = 1f;
            devices.mash.SampleKeyboard();
            session.Tick(Dt);
            Check(devices.mash.KeyboardHeld && devices.mash.Watts > 0f,
                  "Up key-down/held starts keyboard effort on the first sampled frame",
                  ref failures, log);

            float startDistance = session.DistanceM;
            float maxCrankStep = 0f;
            float priorCadence = devices.Telemetry.CadenceRpm;
            for (int frame = 0; frame < 4 * 60; frame++)
            {
                devices.mash.SampleKeyboard();
                session.Tick(Dt);
                maxCrankStep = Mathf.Max(
                    maxCrankStep,
                    Mathf.Abs(devices.Telemetry.CadenceRpm - priorCadence));
                priorCadence = devices.Telemetry.CadenceRpm;
            }
            float heldDistance = session.DistanceM - startDistance;
            Check(heldDistance > 4f && session.SpeedMps > 1f &&
                  devices.Telemetry.Watts > 150f &&
                  devices.Telemetry.CadenceRpm > 60f,
                  "held Up advances the normal ride model (" +
                  heldDistance.ToString("F1") + " m, " +
                  session.SpeedKph.ToString("F1") + " kph, " +
                  devices.Telemetry.Watts.ToString("F0") + " W, " +
                  devices.Telemetry.CadenceRpm.ToString("F0") + " rpm)",
                  ref failures, log);
            Check(maxCrankStep < 5f,
                  "keyboard cadence changes continuously without packet-sized jumps",
                  ref failures, log);

            devices.mash.keyboardHoldOverride = 0f;
            devices.mash.SampleKeyboard();
            float releaseSpeed = session.SpeedMps;
            float releaseSeconds = 0f;
            while (releaseSeconds < 180f &&
                   (devices.Telemetry.Watts > 0.5f || session.SpeedMps > 0.15f))
            {
                devices.mash.SampleKeyboard();
                session.Tick(Dt);
                session.SeekTo(start);
                releaseSeconds += Dt;
            }
            Check(!devices.mash.KeyboardHeld &&
                  devices.Telemetry.Watts <= 0.5f &&
                  session.SpeedMps <= 0.15f,
                  "releasing Up removes effort and coasts to rest (" +
                  releaseSpeed.ToString("F2") + " -> " +
                  session.SpeedMps.ToString("F2") + " m/s in " +
                  releaseSeconds.ToString("F1") + " s)",
                  ref failures, log);

            devices.ResetEffort();
            devices.mash.keyboardHoldOverride = 0f;
            for (int frame = 0; frame < 180; frame++)
            {
                if (frame % 12 == 0) devices.PedalStroke();
                devices.Tick(Dt, 0f);
            }
            Check(devices.Telemetry.Watts > 100f &&
                  devices.Telemetry.CadenceRpm > 50f,
                  "scripted/discrete pedal strokes remain valid beside held-key fallback",
                  ref failures, log);

            var trainer = new FakeTrainerSource(286f, 94f);
            SetActiveSource(devices, trainer);
            devices.source = DeviceManager.SourceKind.BluetoothFtms;
            devices.mash.keyboardHoldOverride = 1f;
            devices.mash.SampleKeyboard();
            devices.Tick(Dt, 0f);
            Check(devices.ExternalTrainerActive &&
                  Mathf.Abs(devices.Telemetry.Watts - 286f) < 0.01f &&
                  Mathf.Abs(trainer.LastEffortInput) < 0.0001f &&
                  devices.MashWatts <= 0.01f,
                  "connected trainer telemetry takes priority without doubled keyboard effort",
                  ref failures, log);

            follow.enableDebugOrbit = true;
            follow.target = player.transform;
            follow.ResetKeyboardOrbit();
            follow.OrbitInputOverride = 1f;
            float maxRightStep = 0f;
            float previousYaw = follow.KeyboardOrbitYawDegrees;
            for (int frame = 0; frame < 60; frame++)
            {
                follow.TickForValidation(Dt);
                maxRightStep = Mathf.Max(maxRightStep,
                    Mathf.Abs(Mathf.DeltaAngle(previousYaw, follow.KeyboardOrbitYawDegrees)));
                previousYaw = follow.KeyboardOrbitYawDegrees;
            }
            float rightYaw = follow.KeyboardOrbitYawDegrees;
            float rightDistance = Vector3.Distance(follow.transform.position, follow.target.position);

            follow.ResetKeyboardOrbit();
            follow.OrbitInputOverride = -1f;
            float maxLeftStep = 0f;
            previousYaw = follow.KeyboardOrbitYawDegrees;
            for (int frame = 0; frame < 60; frame++)
            {
                follow.TickForValidation(Dt);
                maxLeftStep = Mathf.Max(maxLeftStep,
                    Mathf.Abs(Mathf.DeltaAngle(previousYaw, follow.KeyboardOrbitYawDegrees)));
                previousYaw = follow.KeyboardOrbitYawDegrees;
            }
            float leftYaw = follow.KeyboardOrbitYawDegrees;
            float leftDistance = Vector3.Distance(follow.transform.position, follow.target.position);

            Check(rightYaw > 45f && leftYaw < -45f &&
                  Mathf.Abs(Mathf.Abs(rightYaw) - Mathf.Abs(leftYaw)) < 1f,
                  "held Right/Left produce smooth opposite-sign camera yaw (" +
                  rightYaw.ToString("F1") + "/" + leftYaw.ToString("F1") + " deg)",
                  ref failures, log);
            float maximumExpectedStep = follow.debugOrbitSpeed * Dt + 0.01f;
            Check(maxRightStep <= maximumExpectedStep &&
                  maxLeftStep <= maximumExpectedStep,
                  "camera orbit has no discontinuous yaw steps",
                  ref failures, log);
            Check(Mathf.Abs(rightDistance - leftDistance) < 0.01f &&
                  Vector3.Dot(
                      follow.transform.forward,
                      (follow.target.position + Vector3.up * 1.1f -
                       follow.transform.position).normalized) > 0.999f,
                  "orbit preserves follow radius and stable camera target",
                  ref failures, log);

            follow.OrbitInputOverride = 0f;
            for (int frame = 0; frame < 120; frame++) follow.TickForValidation(Dt);
            float settledYaw = follow.KeyboardOrbitYawDegrees;
            for (int frame = 0; frame < 120; frame++) follow.TickForValidation(Dt);
            Check(Mathf.Abs(Mathf.DeltaAngle(
                      settledYaw, follow.KeyboardOrbitYawDegrees)) < 0.01f,
                  "releasing Left/Right settles without drift",
                  ref failures, log);

            Check(!follower.useArrowKeysForLaneChange,
                  "Left/Right arrows are not consumed by bicycle lane steering",
                  ref failures, log);
            var postureKeys = new HashSet<KeyCode>
            {
                pose.aggressiveKey,
                pose.relaxedKey,
                pose.timeTrialKey,
                pose.fitnessKey,
            };
            Check(postureKeys.Count == 4 &&
                  !postureKeys.Contains(KeyCode.UpArrow) &&
                  !postureKeys.Contains(KeyCode.LeftArrow) &&
                  !postureKeys.Contains(KeyCode.RightArrow),
                  "F1-F4 posture selection does not conflict with movement or orbit keys",
                  ref failures, log);
            Check(rig.useRideCadenceForCrank &&
                  rig.crankCadenceSmoothingSeconds >= 0.1f &&
                  follow.GetType().GetCustomAttribute<DefaultExecutionOrder>().order >
                  rig.GetType().GetCustomAttribute<DefaultExecutionOrder>().order,
                  "continuous cadence crank and late camera execution remain enabled",
                  ref failures, log);

            SetActiveSource(devices, devices.simulator);
            devices.source = DeviceManager.SourceKind.Simulator;
            devices.ResetEffort();
            devices.mash.keyboardHoldOverride = 1f;
            for (int frame = 0; frame < 30; frame++)
            {
                devices.mash.SampleKeyboard();
                devices.Tick(Dt, 0f);
                follow.OrbitInputOverride = (frame & 1) == 0 ? 1f : -1f;
                follow.TickForValidation(Dt);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 600; frame++)
            {
                devices.mash.SampleKeyboard();
                devices.Tick(Dt, 0f);
                follow.OrbitInputOverride = (frame & 1) == 0 ? 1f : -1f;
                follow.TickForValidation(Dt);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0,
                  "keyboard and camera hot paths allocate 0 B across 600 frames",
                  ref failures, log);

            log.AppendLine(failures == 0
                ? "[keyboard-regression] PASS - all checks green."
                : "[keyboard-regression] FAIL - " + failures + " check(s) failed.");
            Debug.Log(log.ToString());
        }
        finally
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            MapleRideSceneBootstrap.DiscardChanges();
        }
    }

    private static float FindFlatDistance(RideSession session)
    {
        float bestDistance = 20f;
        float bestGrade = float.PositiveInfinity;
        for (float distance = 20f; distance < session.Course.Length - 20f; distance += 5f)
        {
            float grade = Mathf.Abs(session.Course.GradeAt(distance, session.gradeWindowM));
            if (grade < bestGrade)
            {
                bestGrade = grade;
                bestDistance = distance;
            }
        }
        return bestDistance;
    }

    private static void SetActiveSource(
        DeviceManager devices, IRideTelemetrySource source)
    {
        typeof(DeviceManager)
            .GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(devices, source);
    }

    private static void Check(
        bool condition, string label, ref int failures, StringBuilder log)
    {
        if (condition) log.AppendLine("  [ok] " + label);
        else
        {
            failures++;
            log.AppendLine("  [FAIL] " + label);
        }
    }

    private sealed class FakeTrainerSource : IRideTelemetrySource
    {
        private readonly float _watts;
        private readonly float _cadence;

        public FakeTrainerSource(float watts, float cadence)
        {
            _watts = watts;
            _cadence = cadence;
        }

        public string Label => "Fake connected trainer";
        public bool IsConnected => true;
        public float LastEffortInput { get; private set; }

        public void Tick(float dt, float grade, float effortInput, float ftpWatts)
        {
            LastEffortInput = effortInput;
        }

        public RideTelemetry Read()
        {
            return new RideTelemetry
            {
                Watts = _watts,
                CadenceRpm = _cadence,
                PowerConnected = true,
                SourceLabel = Label,
            };
        }

        public void SetSimulatedGrade(float grade) { }
    }
}

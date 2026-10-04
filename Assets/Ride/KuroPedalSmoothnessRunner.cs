using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Opt-in play-mode benchmark for Kuro's pedal, foot, and chase-camera continuity.
/// It is spawned only by KuroPedalSmoothnessCapture and never exists in the production scene.
/// </summary>
public sealed class KuroPedalSmoothnessRunner : MonoBehaviour
{
    public string outDir;
    public static bool Finished;
    public static bool Failed;

    private const string PlayerName = "Kuro on Sakura Pass";
    private const int WarmupFrames = 120;
    private const int SampleFrames = 360;
    private const float SimulationDeltaSeconds = 1f / 60f;
    private static readonly float[] Cadences = { 60f, 88f, 110f };

    private RideSession _session;
    private DeviceManager _devices;
    private KuroBikeRig _rig;
    private KuroFollowCamera _follow;
    private GameObject _player;
    private float _strokeClock;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;
        yield return Guarded(Run());
        Finished = true;
    }

    private IEnumerator Guarded(IEnumerator routine)
    {
        while (true)
        {
            bool moved;
            object current;
            try
            {
                moved = routine.MoveNext();
                current = moved ? routine.Current : null;
            }
            catch (Exception ex)
            {
                Failed = true;
                Debug.LogException(ex);
                Debug.LogError("[kuro-smooth] FAIL - " + ex.Message);
                yield break;
            }
            if (!moved) yield break;
            yield return current;
        }
    }

    private IEnumerator Run()
    {
        Directory.CreateDirectory(outDir);
        Debug.Log("[kuro-smooth] runner started");
        _player = GameObject.Find(PlayerName);
        _rig = _player != null ? _player.GetComponent<KuroBikeRig>() : null;
        _session = FindFirstObjectByType<RideSession>();
        _devices = _session != null ? _session.devices : null;
        _follow = FindFirstObjectByType<KuroFollowCamera>();
        if (_player == null || _rig == null || _session == null || _devices == null ||
            _follow == null)
            throw new InvalidOperationException("Kuro smoothness harness could not resolve runtime owners.");

        RideInputGate.Unlock("Kuro smoothness harness");
        IsolatePedalPath();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Time.captureDeltaTime = SimulationDeltaSeconds;
        _devices.acceptKeyboardEffort = true;
        _devices.effortSource = DeviceManager.EffortSource.MashPedalStrokes;
        _devices.mash.scoutHoldMode = false;
        _follow.enableDebugOrbit = false;

        var results = new List<Result>(Cadences.Length * 2);
        foreach (bool continuous in new[] { false, true })
        foreach (float cadence in Cadences)
            yield return Measure(continuous, cadence, results);

        WriteResults(results);
        yield return CaptureMotionStrip(88f);
        Debug.Log("[kuro-smooth] PASS - metrics and motion strip written to " + outDir);
    }

    private void IsolatePedalPath()
    {
        var pose = _player.GetComponent<KuroRidePose>();
        var follower = FindObjectsByType<RouteFollower>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.rider == _player.transform);
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == this || behaviour == _session || behaviour == _devices ||
                behaviour == _rig || behaviour == pose || behaviour == follower ||
                behaviour == _follow)
                continue;
            behaviour.enabled = false;
        }

        foreach (Renderer renderer in FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!renderer.transform.IsChildOf(_player.transform))
                renderer.enabled = false;

        foreach (Animator animator in FindObjectsByType<Animator>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!animator.transform.IsChildOf(_player.transform))
                animator.enabled = false;

        Debug.Log("[kuro-smooth] isolated rider/session/route/rig/camera path for measurement");
    }

    private IEnumerator Measure(bool continuous, float cadence, List<Result> results)
    {
        _rig.useRideCadenceForCrank = continuous;
        _devices.ResetEffort();
        _session.ResetRide();
        _strokeClock = 0f;

        for (int frame = 0; frame < WarmupFrames; frame++)
        {
            BankCadence(cadence, SimulationDeltaSeconds);
            yield return null;
        }

        var frameMs = new float[SampleFrames];
        var crankRpm = new float[SampleFrames];
        var pedalStepMm = new float[SampleFrames];
        var footStepMm = new float[SampleFrames];
        var allocations = new long[SampleFrames];
        int duplicateSteps = 0;
        float maxFootResidualMm = 0f;
        float maxCameraResidualMm = 0f;
        Vector3 priorPedal = _rig.PedalL.position;
        Vector3 priorFoot = _rig.FootL.position;
        float priorCrank = _rig.CrankAngleDegrees;
        long priorAllocated = GC.GetAllocatedBytesForCurrentThread();
        int gcStart = GC.CollectionCount(0);
        double priorRealtime = Time.realtimeSinceStartupAsDouble;

        for (int frame = 0; frame < SampleFrames; frame++)
        {
            BankCadence(cadence, SimulationDeltaSeconds);
            yield return null;

            double realtime = Time.realtimeSinceStartupAsDouble;
            frameMs[frame] = (float)((realtime - priorRealtime) * 1000.0);
            priorRealtime = realtime;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float crank = _rig.CrankAngleDegrees;
            float crankDelta = crank - priorCrank;
            priorCrank = crank;
            crankRpm[frame] = crankDelta / (6f * dt);
            if (_devices.Telemetry.CadenceRpm > 20f && crankDelta <= 0.05f) duplicateSteps++;

            Vector3 pedal = _rig.PedalL.position;
            Vector3 foot = _rig.FootL.position;
            pedalStepMm[frame] = Vector3.Distance(priorPedal, pedal) * 1000f;
            footStepMm[frame] = Vector3.Distance(priorFoot, foot) * 1000f;
            priorPedal = pedal;
            priorFoot = foot;
            maxFootResidualMm = Mathf.Max(
                maxFootResidualMm,
                Vector3.Distance(
                    _rig.FootL.position,
                    _rig.FootTargetL) * 1000f,
                Vector3.Distance(
                    _rig.FootR.position,
                    _rig.FootTargetR) * 1000f);

            Vector3 scaled = new Vector3(
                _follow.offset.x,
                _follow.offset.y * _follow.modDistanceScale + _follow.modHeightDelta,
                _follow.offset.z * _follow.modDistanceScale);
            Vector3 expectedCamera = _follow.target.position + _follow.ChaseFrame() * scaled;
            maxCameraResidualMm = Mathf.Max(
                maxCameraResidualMm,
                Vector3.Distance(_follow.transform.position, expectedCamera) * 1000f);

            long allocated = GC.GetAllocatedBytesForCurrentThread();
            allocations[frame] = Math.Max(0L, allocated - priorAllocated);
            priorAllocated = allocated;
        }

        var result = new Result
        {
            mode = continuous ? "cadence_integrated" : "legacy_distance",
            cadence = cadence,
            frameMedianMs = Percentile(frameMs, 50f),
            frameP95Ms = Percentile(frameMs, 95f),
            frameP99Ms = Percentile(frameMs, 99f),
            frameMaxMs = frameMs.Max(),
            allocationMedianBytes = Percentile(allocations, 50f),
            allocationP95Bytes = Percentile(allocations, 95f),
            gcCollections = GC.CollectionCount(0) - gcStart,
            crankMeanRpm = crankRpm.Average(),
            crankStdDevRpm = StdDev(crankRpm),
            duplicateCrankSteps = duplicateSteps,
            pedalP99StepMm = Percentile(pedalStepMm, 99f),
            footP99StepMm = Percentile(footStepMm, 99f),
            maxFootResidualMm = maxFootResidualMm,
            maxCameraResidualMm = maxCameraResidualMm,
        };
        results.Add(result);
        Debug.Log(result.LogLine());
    }

    private void BankCadence(float cadenceRpm, float dt)
    {
        float strokeInterval = 60f /
            (Mathf.Max(1f, cadenceRpm) * Mathf.Max(0.5f, _devices.mash.strokesPerRevolution));
        _strokeClock += Mathf.Max(0f, dt);
        while (_strokeClock >= strokeInterval)
        {
            _strokeClock -= strokeInterval;
            _devices.PedalStroke();
        }
    }

    private IEnumerator CaptureMotionStrip(float cadence)
    {
        _rig.useRideCadenceForCrank = true;
        _devices.ResetEffort();
        _session.ResetRide();
        _strokeClock = 0f;
        for (int frame = 0; frame < WarmupFrames; frame++)
        {
            BankCadence(cadence, SimulationDeltaSeconds);
            yield return null;
        }

        const int tileWidth = 480;
        const int tileHeight = 400;
        const int columns = 4;
        const int rows = 3;
        var strip = new Texture2D(tileWidth * columns, tileHeight * rows, TextureFormat.RGB24, false);
        var cameraGo = new GameObject("~Kuro Pedal Motion Camera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.fieldOfView = 32f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 14000f;
        camera.allowHDR = true;
        camera.clearFlags = CameraClearFlags.Skybox;
        var target = new RenderTexture(tileWidth, tileHeight, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4,
        };
        camera.targetTexture = target;

        for (int tile = 0; tile < columns * rows; tile++)
        {
            if (tile != 0)
            {
                // Four real presentation frames at 88 rpm advance about 35 degrees at 60 Hz.
                // Capture the actual runtime sequence rather than teleporting the crank to
                // idealised phases; duplicate or skipped visual steps remain visible.
                for (int tick = 0; tick < 4; tick++)
                {
                    BankCadence(cadence, SimulationDeltaSeconds);
                    yield return null;
                }
            }

            Transform hips = Find(_player.transform, "Hips");
            Transform head = Find(_player.transform, "Head");
            Vector3 aim = Vector3.Lerp(hips.position, head.position, 0.36f);
            camera.transform.position = aim + _player.transform.right * 1.55f + Vector3.up * 0.06f;
            camera.transform.LookAt(aim, Vector3.up);
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var frame = new Texture2D(tileWidth, tileHeight, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0, 0, tileWidth, tileHeight), 0, 0);
            frame.Apply();
            int x = (tile % columns) * tileWidth;
            int y = (rows - 1 - tile / columns) * tileHeight;
            strip.SetPixels32(x, y, tileWidth, tileHeight, frame.GetPixels32());
            Destroy(frame);
            RenderTexture.active = previous;
        }

        strip.Apply();
        File.WriteAllBytes(
            Path.Combine(outDir, "kuro_pedal_motion_strip.png"),
            strip.EncodeToPNG());
        Destroy(strip);
        camera.targetTexture = null;
        target.Release();
        Destroy(target);
        Destroy(cameraGo);
    }

    private void WriteResults(List<Result> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("mode,cadence_rpm,frame_median_ms,frame_p95_ms,frame_p99_ms,frame_max_ms," +
                       "alloc_median_bytes,alloc_p95_bytes,gc_collections,crank_mean_rpm," +
                       "crank_stddev_rpm,duplicate_crank_steps,pedal_p99_step_mm," +
                       "foot_p99_step_mm,max_foot_residual_mm,max_camera_residual_mm");
        foreach (Result result in results) csv.AppendLine(result.CsvLine());
        File.WriteAllText(Path.Combine(outDir, "kuro_pedal_smoothness.csv"), csv.ToString());

        var summary = new StringBuilder();
        foreach (Result result in results) summary.AppendLine(result.LogLine());
        File.WriteAllText(Path.Combine(outDir, "kuro_pedal_smoothness.txt"), summary.ToString());
    }

    private static float Percentile(float[] values, float percentile)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        float index = (sorted.Length - 1) * percentile / 100f;
        int lower = Mathf.FloorToInt(index);
        int upper = Mathf.CeilToInt(index);
        return Mathf.Lerp(sorted[lower], sorted[upper], index - lower);
    }

    private static long Percentile(long[] values, float percentile)
    {
        var sorted = (long[])values.Clone();
        Array.Sort(sorted);
        float index = (sorted.Length - 1) * percentile / 100f;
        int lower = Mathf.FloorToInt(index);
        int upper = Mathf.CeilToInt(index);
        return (long)Math.Round(Mathf.Lerp(sorted[lower], sorted[upper], index - lower));
    }

    private static float StdDev(float[] values)
    {
        float mean = values.Average();
        double sum = 0.0;
        foreach (float value in values) sum += (value - mean) * (value - mean);
        return (float)Math.Sqrt(sum / Math.Max(1, values.Length));
    }

    private static Transform Find(Transform root, string exactName)
    {
        if (root.name == exactName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), exactName);
            if (found != null) return found;
        }
        return null;
    }

    [Serializable]
    private struct Result
    {
        public string mode;
        public float cadence;
        public float frameMedianMs;
        public float frameP95Ms;
        public float frameP99Ms;
        public float frameMaxMs;
        public long allocationMedianBytes;
        public long allocationP95Bytes;
        public int gcCollections;
        public float crankMeanRpm;
        public float crankStdDevRpm;
        public int duplicateCrankSteps;
        public float pedalP99StepMm;
        public float footP99StepMm;
        public float maxFootResidualMm;
        public float maxCameraResidualMm;

        public string LogLine()
        {
            return string.Format(
                "[kuro-smooth] {0} {1:F0} rpm | frame med/p95/p99/max " +
                "{2:F2}/{3:F2}/{4:F2}/{5:F2} ms | alloc med/p95 {6}/{7} B, GC {8} | " +
                "crank mean/std {9:F1}/{10:F1} rpm, duplicates {11} | " +
                "pedal/foot p99 {12:F2}/{13:F2} mm | foot residual {14:F2} mm | camera residual {15:F2} mm",
                mode, cadence, frameMedianMs, frameP95Ms, frameP99Ms, frameMaxMs,
                allocationMedianBytes, allocationP95Bytes, gcCollections, crankMeanRpm,
                crankStdDevRpm, duplicateCrankSteps, pedalP99StepMm, footP99StepMm,
                maxFootResidualMm, maxCameraResidualMm);
        }

        public string CsvLine()
        {
            return string.Join(",", new[]
            {
                mode,
                cadence.ToString("F1"),
                frameMedianMs.ToString("F3"),
                frameP95Ms.ToString("F3"),
                frameP99Ms.ToString("F3"),
                frameMaxMs.ToString("F3"),
                allocationMedianBytes.ToString(),
                allocationP95Bytes.ToString(),
                gcCollections.ToString(),
                crankMeanRpm.ToString("F3"),
                crankStdDevRpm.ToString("F3"),
                duplicateCrankSteps.ToString(),
                pedalP99StepMm.ToString("F3"),
                footP99StepMm.ToString("F3"),
                maxFootResidualMm.ToString("F3"),
                maxCameraResidualMm.ToString("F3"),
            });
        }
    }
}

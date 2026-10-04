using System;
using UnityEngine;

/// <summary>
/// Headless self-test for the W0 ambient-mover kit.
/// Run via: Unity -batchmode -quit -executeMethod AmbientMoverSelfTest.Run -logFile gemini_w0_selftest.log
/// Covers: path sampling, loop/shuttle end behaviour, consist spacing & yaw, distance cutoff (1.5 km), and zero-GC runtime execution.
/// Exits with code 0 on PASS or 1 on FAIL.
/// </summary>
public static class AmbientMoverSelfTest
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
        {
            _pass++;
        }
        else
        {
            _fail++;
            Debug.LogError($"[ambient-tests] FAIL: {name} - {detail}");
        }
    }

    public static void Run()
    {
        _pass = 0;
        _fail = 0;
        Debug.Log("[ambient-tests] Starting W0 Ambient Mover self-tests...");

        TestPathSampling();
        TestLoopAndShuttle();
        TestConsistSpacingAndYaw();
        TestDistanceCutoff();
        TestZeroGC();
        TestFlockDynamics();
        TestRotorAndSway();
        TestStagingHelpers();

        Debug.Log($"[ambient-tests] {(_fail == 0 ? "PASS" : "FAIL")} {_pass}/{_pass + _fail}");
        if (Application.isBatchMode)
        {
            UnityEditor.EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }
    }

    private static void TestPathSampling()
    {
        var testGo = new GameObject("TestPathSampling");
        try
        {
            var mover = testGo.AddComponent<AmbientPathMover>();
            // Straight 2-segment path: (0,0,0) -> (50,0,0) -> (50,0,50)
            mover.path = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(50f, 0f, 0f),
                new Vector3(50f, 0f, 50f)
            };
            mover.mode = AmbientPathMover.PathMode.Shuttle;
            mover.Bake();

            Check("polyline total length", Mathf.Abs(mover.TotalLength - 100f) < 1e-3f, $"Length: {mover.TotalLength}");

            // Sampling checks
            Vector3 pStart = mover.Sample(0f);
            Check("sample at 0", Vector3.Distance(pStart, new Vector3(0f, 0f, 0f)) < 1e-3f, $"pStart: {pStart}");

            Vector3 pMid1 = mover.Sample(25f);
            Check("sample midway seg1", Vector3.Distance(pMid1, new Vector3(25f, 0f, 0f)) < 1e-3f, $"pMid1: {pMid1}");

            Vector3 pCorner = mover.Sample(50f);
            Check("sample corner", Vector3.Distance(pCorner, new Vector3(50f, 0f, 0f)) < 1e-3f, $"pCorner: {pCorner}");

            Vector3 pMid2 = mover.Sample(75f);
            Check("sample midway seg2", Vector3.Distance(pMid2, new Vector3(50f, 0f, 25f)) < 1e-3f, $"pMid2: {pMid2}");

            Vector3 pEnd = mover.Sample(100f);
            Check("sample end", Vector3.Distance(pEnd, new Vector3(50f, 0f, 50f)) < 1e-3f, $"pEnd: {pEnd}");

            // Clamp checks in Shuttle mode
            Vector3 pUnder = mover.Sample(-10f);
            Check("shuttle clamp underflow", Vector3.Distance(pUnder, new Vector3(0f, 0f, 0f)) < 1e-3f, $"pUnder: {pUnder}");

            Vector3 pOver = mover.Sample(120f);
            Check("shuttle clamp overflow", Vector3.Distance(pOver, new Vector3(50f, 0f, 50f)) < 1e-3f, $"pOver: {pOver}");

            // Tangents
            Vector3 tan1 = mover.GetTangent(25f);
            Check("tangent seg1", Vector3.Dot(tan1, Vector3.right) > 0.99f, $"tan1: {tan1}");

            Vector3 tan2 = mover.GetTangent(75f);
            Check("tangent seg2", Vector3.Dot(tan2, Vector3.forward) > 0.99f, $"tan2: {tan2}");

            // Loop mode closing length
            mover.mode = AmbientPathMover.PathMode.Loop;
            mover.Bake();
            float expectedLoopLen = 100f + Vector3.Distance(new Vector3(50f, 0f, 50f), Vector3.zero);
            Check("loop includes closing segment", Mathf.Abs(mover.TotalLength - expectedLoopLen) < 1e-2f, $"LoopLen: {mover.TotalLength}");

            Vector3 pWrap = mover.Sample(mover.TotalLength + 25f);
            Check("loop wraps correctly", Vector3.Distance(pWrap, new Vector3(25f, 0f, 0f)) < 1e-2f, $"pWrap: {pWrap}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(testGo);
        }
    }

    private static void TestLoopAndShuttle()
    {
        var testGo = new GameObject("TestLoopAndShuttle");
        try
        {
            var mover = testGo.AddComponent<AmbientPathMover>();
            mover.path = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(100f, 0f, 0f)
            };
            mover.speed = 20f;
            mover.accel = 50f;
            mover.mode = AmbientPathMover.PathMode.Loop;
            mover.startDistance = 0f;
            mover.Bake();

            // Loop stepping
            for (int i = 0; i < 50; i++)
            {
                mover.Step(0.1f);
            }
            Check("loop advances distance", mover.CurrentHead > 0f, $"Head: {mover.CurrentHead}");

            // Shuttle mode: test deceleration, dwell, and direction reversal
            mover.mode = AmbientPathMover.PathMode.Shuttle;
            mover.dwellSeconds = 2f;
            mover.speed = 10f;
            mover.accel = 10f;
            mover.startDistance = 90f; // close to terminus (hi = 99f)
            mover.Bake();
            mover.SetHead(90f);

            bool reachedEnd = false;
            for (int i = 0; i < 60; i++)
            {
                mover.Step(0.1f);
                if (mover.DwellRemaining > 0f && mover.Direction < 0f)
                {
                    reachedEnd = true;
                    break;
                }
            }
            Check("shuttle hits terminus and dwells", reachedEnd, $"Dwell: {mover.DwellRemaining}, Dir: {mover.Direction}");
            Check("shuttle speed 0 during dwell", Mathf.Approximately(mover.CurrentSpeed, 0f), $"Speed: {mover.CurrentSpeed}");

            // Advance through dwell
            for (int i = 0; i < 25; i++)
            {
                mover.Step(0.1f);
            }
            Check("shuttle resumes after dwell in reverse", mover.DwellRemaining <= 0f && mover.Direction < 0f,
                $"Dwell: {mover.DwellRemaining}, Dir: {mover.Direction}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(testGo);
        }
    }

    private static void TestConsistSpacingAndYaw()
    {
        var testGo = new GameObject("TestConsist");
        var car0 = new GameObject("Car0");
        var car1 = new GameObject("Car1");
        var car2 = new GameObject("Car2");
        try
        {
            var mover = testGo.AddComponent<AmbientPathMover>();
            mover.path = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(200f, 0f, 0f)
            };
            mover.cars = new[] { car0.transform, car1.transform, car2.transform };
            mover.carOffsets = new[] { 0f, 15f, 30f };
            mover.carYaw = new[] { 0f, 0f, 180f };
            mover.mode = AmbientPathMover.PathMode.Shuttle;
            mover.startDistance = 60f;
            mover.Bake();
            mover.SetHead(60f);

            float dist01 = Vector3.Distance(car0.transform.position, car1.transform.position);
            float dist12 = Vector3.Distance(car1.transform.position, car2.transform.position);

            Check("car 0 to car 1 spacing is 15m", Mathf.Abs(dist01 - 15f) < 0.1f, $"dist01: {dist01}");
            Check("car 1 to car 2 spacing is 15m", Mathf.Abs(dist12 - 15f) < 0.1f, $"dist12: {dist12}");

            // Check car 0 facing tangent (+X)
            float car0Dot = Vector3.Dot(car0.transform.forward, Vector3.right);
            Check("car 0 faces forward (+X)", car0Dot > 0.99f, $"car0Dot: {car0Dot}");

            // Check car 2 tail car yaw (180 deg -> faces -X)
            float car2Dot = Vector3.Dot(car2.transform.forward, -Vector3.right);
            Check("car 2 reversed tail car faces backward (-X)", car2Dot > 0.99f, $"car2Dot: {car2Dot}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car0);
            UnityEngine.Object.DestroyImmediate(car1);
            UnityEngine.Object.DestroyImmediate(car2);
            UnityEngine.Object.DestroyImmediate(testGo);
        }
    }

    private static void TestDistanceCutoff()
    {
        var testGo = new GameObject("TestDistanceCutoff");
        var birdGo = new GameObject("TestFlockBird");
        try
        {
            testGo.transform.position = new Vector3(0f, 0f, 0f);

            var mover = testGo.AddComponent<AmbientPathMover>();
            mover.path = new[] { new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f) };
            mover.cullDistance = 1500f;
            mover.Bake();

            var flock = testGo.AddComponent<AmbientFlock>();
            flock.members = new[] { birdGo.transform };
            flock.cullDistance = 1500f;
            flock.Bake();

            var rotor = testGo.AddComponent<AmbientRotor>();
            rotor.degreesPerSecond = 180f;
            rotor.cullDistance = 1500f;
            rotor.Bake();

            var sway = testGo.AddComponent<AmbientSway>();
            sway.maxAngle = 10f;
            sway.cullDistance = 1500f;
            sway.Bake();

            // Set camera position within 1000m (inside 1500m cutoff)
            AmbientCull.TargetPositionOverride = () => new Vector3(500f, 0f, 0f);

            mover.Step(0.4f);
            flock.Step(0.4f);
            rotor.Step(0.4f);
            sway.Step(0.4f);

            Check("mover active within 1500m", !mover.IsCulled, "Mover unexpectedly culled");
            Check("flock active within 1500m", !flock.IsCulled, "Flock unexpectedly culled");
            Check("rotor active within 1500m", !rotor.IsCulled, "Rotor unexpectedly culled");
            Check("sway active within 1500m", !sway.IsCulled, "Sway unexpectedly culled");

            // Move reference position beyond 1500m (e.g. 2000m away)
            AmbientCull.TargetPositionOverride = () => new Vector3(2000f, 0f, 0f);

            mover.Step(0.4f);
            flock.Step(0.4f);
            rotor.Step(0.4f);
            sway.Step(0.4f);

            Check("mover culled beyond 1500m", mover.IsCulled, "Mover failed to cull");
            Check("flock culled beyond 1500m", flock.IsCulled, "Flock failed to cull");
            Check("rotor culled beyond 1500m", rotor.IsCulled, "Rotor failed to cull");
            Check("sway culled beyond 1500m", sway.IsCulled, "Sway failed to cull");

            // Reset override
            AmbientCull.TargetPositionOverride = null;
        }
        finally
        {
            AmbientCull.TargetPositionOverride = null;
            UnityEngine.Object.DestroyImmediate(birdGo);
            UnityEngine.Object.DestroyImmediate(testGo);
        }
    }

    private static void TestZeroGC()
    {
        var testGo = new GameObject("TestZeroGC");
        var birdGo = new GameObject("Bird");
        var carGo = new GameObject("Car");
        try
        {
            var mover = testGo.AddComponent<AmbientPathMover>();
            mover.path = new[] { new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f) };
            mover.cars = new[] { carGo.transform };
            mover.Bake();

            var flock = testGo.AddComponent<AmbientFlock>();
            flock.members = new[] { birdGo.transform };
            flock.Bake();

            var rotor = testGo.AddComponent<AmbientRotor>();
            rotor.Bake();

            var sway = testGo.AddComponent<AmbientSway>();
            sway.Bake();

            // Warm up execution (JIT compilation)
            for (int i = 0; i < 10; i++)
            {
                mover.Step(0.016f);
                flock.Step(0.016f);
                rotor.Step(0.016f);
                sway.Step(0.016f);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocBefore = GC.GetTotalMemory(false);

            // Execute 1,000 frames
            for (int i = 0; i < 1000; i++)
            {
                mover.Step(0.016f);
                flock.Step(0.016f);
                rotor.Step(0.016f);
                sway.Step(0.016f);
            }

            long allocAfter = GC.GetTotalMemory(false);
            long delta = allocAfter - allocBefore;

            Check("zero GC allocations during 1000 simulation steps", delta <= 0, $"GC delta bytes: {delta}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(carGo);
            UnityEngine.Object.DestroyImmediate(birdGo);
            UnityEngine.Object.DestroyImmediate(testGo);
        }
    }

    private static void TestFlockDynamics()
    {
        var testGo1 = new GameObject("TestFlock1");
        var testGo2 = new GameObject("TestFlock2");
        var bird1 = new GameObject("Bird1");
        var bird2 = new GameObject("Bird2");
        try
        {
            var flock1 = testGo1.AddComponent<AmbientFlock>();
            flock1.members = new[] { bird1.transform };
            flock1.seed = 12345;
            flock1.Bake();

            var flock2 = testGo2.AddComponent<AmbientFlock>();
            flock2.members = new[] { bird2.transform };
            flock2.seed = 12345;
            flock2.Bake();

            // Both identical seeds should produce identical positions
            for (int i = 0; i < 50; i++)
            {
                flock1.Step(0.05f);
                flock2.Step(0.05f);
            }

            float diff = Vector3.Distance(bird1.transform.position, bird2.transform.position);
            Check("flock determinism from seed", diff < 1e-4f, $"Seed position difference: {diff}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(bird1);
            UnityEngine.Object.DestroyImmediate(bird2);
            UnityEngine.Object.DestroyImmediate(testGo1);
            UnityEngine.Object.DestroyImmediate(testGo2);
        }
    }

    private static void TestRotorAndSway()
    {
        var rotorGo = new GameObject("TestRotor");
        var swayGo = new GameObject("TestSway");
        try
        {
            var rotor = rotorGo.AddComponent<AmbientRotor>();
            rotor.degreesPerSecond = 360f;
            rotor.accel = 0f;
            rotor.Bake();

            rotor.Step(0.5f); // 0.5 sec at 360 deg/sec = 180 degrees
            Check("rotor spins at authored speed", Mathf.Abs(rotor.CurrentAngle - 180f) < 0.1f, $"Angle: {rotor.CurrentAngle}");

            var sway = swayGo.AddComponent<AmbientSway>();
            sway.maxAngle = 15f;
            sway.secondaryAngle = 5f;
            sway.frequency = 1f;
            sway.mode = AmbientSway.SwayMode.Pendulum;
            sway.Bake();

            for (int i = 0; i < 100; i++)
            {
                sway.Step(0.02f);
                float angle = Quaternion.Angle(swayGo.transform.localRotation, Quaternion.identity);
                if (angle > 22f)
                {
                    Check("sway angle remains bounded", false, $"Exceeded expected bounds: {angle}");
                    break;
                }
            }
            Check("sway angle remains bounded", true);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rotorGo);
            UnityEngine.Object.DestroyImmediate(swayGo);
        }
    }

    private static void TestStagingHelpers()
    {
        var parentGo = new GameObject("TestParent");
        try
        {
            // Test polyline baking and resampling
            var rawPoints = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0.001f, 0f, 0f), // duplicate to filter
                new Vector3(10f, 0f, 0f),
                new Vector3(20f, 0f, 0f)
            };
            var baked = AmbientMoverStaging.BakePolyline(rawPoints, 0.05f);
            Check("bake polyline filters duplicates", baked.Length == 3, $"Length: {baked.Length}");

            var resampled = AmbientMoverStaging.ResamplePolyline(baked, 5f);
            Check("resample polyline step count", resampled.Length == 5, $"Resampled length: {resampled.Length}");

            // Test Converge idempotence (no stacking duplicates)
            var c1 = AmbientMoverStaging.Converge(parentGo.transform, "UniqueTrainMover");
            var c2 = AmbientMoverStaging.Converge(parentGo.transform, "UniqueTrainMover");

            int matchCount = 0;
            for (int i = 0; i < parentGo.transform.childCount; i++)
            {
                if (parentGo.transform.GetChild(i).name == "UniqueTrainMover") matchCount++;
            }
            Check("converge never stacks duplicates", matchCount == 1, $"matchCount: {matchCount}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parentGo);
        }
    }
}

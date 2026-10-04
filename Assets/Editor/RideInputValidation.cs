using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Staging and verification for the player's INPUT and POSTURE:
///
///   <c>StageRideInput</c>  - bakes the mash-to-ride effort settings and the
///                            <see cref="KuroRidePose"/> driver into the saved SakuraPass scene.
///                            A surgical alternative to re-running the whole environment pass.
///   <c>CaptureRideInput</c> - drives a deterministic ride with the REAL device manager, the REAL
///                            physics and the REAL rig, and renders what it looks like, because
///                            on this project "the log says 0.0 m" has been true of a bike that
///                            was visibly rolling.
///
/// Every capture ticks the presentation layer in the load-bearing order the encounter harness
/// documents: session -> pose driver -> <c>KuroBikeRig.LateUpdate</c>. The rig rewrites
/// hips/spine from the bind pose every LateUpdate, so a modifier written after it is a modifier
/// that does nothing at all.
/// </summary>
public static class RideInputValidation
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const float Dt = 1f / 60f;

    private static string OutDir =>
        MapleRidePaths.RenderDir("ride_input");

    // ==================================================================== staging

    [MenuItem("MapleRide/Ride/Stage Ride Input And Pose", priority = 43)]
    public static void StageRideInput()
    {
        OpenScene();

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[rideinput] no RideBootstrap in the scene."); return; }
        boot.Resolve();

        var devices = boot.devices;
        devices.acceptKeyboardEffort = true;
        devices.effortSource = DeviceManager.EffortSource.MashPedalStrokes;
        devices.mash.strokeKey = KeyCode.UpArrow;
        devices.mash.strokeKeyAlt = KeyCode.W;
        devices.mash.keyboardHoldEnabled = true;
        devices.mash.keyboardHoldKey = KeyCode.UpArrow;
        devices.mash.keyboardHoldWatts = 220f;
        devices.mash.keyboardHoldRampSeconds = 0.45f;
        devices.mash.keyboardReleaseSeconds = 0.55f;
        devices.mash.keyboardHoldCadenceRpm = 88f;
        devices.mash.scoutHoldMode = false;
        devices.mash.wattsPerStroke = 70f;      // PROVISIONAL
        devices.mash.decaySeconds = 0.85f;      // PROVISIONAL
        devices.mash.maxWatts = 650f;           // PROVISIONAL
        devices.mash.smoothingSeconds = 0.12f;  // PROVISIONAL
        EditorUtility.SetDirty(devices);

        var player = boot.rider != null ? boot.rider.gameObject
                                        : GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[rideinput] no player rider in the scene."); return; }

        // Idempotent: reuse the component if it is already there, and prune any duplicate. A
        // second driver would write the modifiers twice and the last one to run would win.
        var poses = player.GetComponents<KuroRidePose>();
        for (int i = 1; i < poses.Length; i++) Object.DestroyImmediate(poses[i]);
        var pose = poses.Length > 0 ? poses[0] : player.AddComponent<KuroRidePose>();

        pose.session = boot.session;
        pose.rig = player.GetComponentInChildren<KuroBikeRig>(true);
        pose.climbGradeThreshold = 0.04f;    // PROVISIONAL: out of the saddle at 4 %
        pose.climbGradeFull = 0.065f;        // PROVISIONAL
        pose.sprintKey = KeyCode.S;
        pose.applySprintAero = true;
        pose.sprintCdaScale = 0.92f;         // PROVISIONAL
        EditorUtility.SetDirty(pose);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[rideinput] staged: effort={devices.effortSource}, " +
                  $"mash key {devices.mash.strokeKey}, KuroRidePose on '{player.name}' " +
                  $"(rig {(pose.rig != null ? pose.rig.name : "MISSING")}), " +
                  $"climb >= {pose.climbGradeThreshold * 100f:0.#} %, sprint on {pose.sprintKey}.");
    }

    // ==================================================================== self-test

    [MenuItem("MapleRide/Ride/Run Ride Input Self-Test", priority = 44)]
    public static void RunSelfTest()
    {
        OpenScene();
        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[rideinput] no RideBootstrap."); return; }
        boot.Resolve();

        var log = new StringBuilder();
        int failures = 0;
        log.AppendLine();
        log.AppendLine("============ MapleRide ride-input self-test ============");

        // A throwaway host, so the test can never leave a mutated session in the scene.
        var host = new GameObject("~RideInputSelfTest");
        var devices = host.AddComponent<DeviceManager>();
        var session = host.AddComponent<RideSession>();
        devices.ftpWatts = boot.devices.ftpWatts;
        devices.acceptKeyboardEffort = true;
        devices.effortSource = DeviceManager.EffortSource.MashPedalStrokes;
        devices.mash.keyboardHoldEnabled = false;
        // TEMPORARY scout-mode debug flag defaults true and disables Stroke()-driven mashing
        // entirely (ScoutTick ignores banked strokes) - without turning it off here every mash
        // check below (2, 3, 4b) silently no-ops at 0 W/0 kph instead of testing anything.
        devices.mash.scoutHoldMode = false;
        session.devices = devices;
        session.courseId = "sakura_circuit";
        session.EnsureCourse();

        // ---- 1. no input, no movement ------------------------------------------------------
        float flat = FindFlatDistance(session, 0.01f);
        session.SeekTo(flat);
        for (int i = 0; i < 60 * 15; i++) session.Tick(Dt);
        float idleSpeed = session.SpeedMps;
        float idleTravel = Mathf.Abs(session.DistanceM - flat);
        bool idleOk = idleSpeed < 0.05f && idleTravel < 0.5f;
        if (!idleOk) failures++;
        log.AppendLine($"  [{(idleOk ? "ok" : "FAIL")}] 15 s with no input at {flat:0} m " +
                       $"(grade {session.Grade * 100f:0.0} %): speed {idleSpeed:0.000} m/s, " +
                       $"moved {idleTravel:0.00} m, {devices.Telemetry.Watts:0} W");

        // ---- 2. mash rate -> speed ---------------------------------------------------------
        // Pinned to the flat sample: left free, the rider covers hundreds of metres in 45 s and
        // the number at the end is the gradient he happens to be on, not the mash. The first
        // version of this test reported 47 kph for a 40 W lazy mash for exactly that reason.
        var results = new List<(float hz, float watts, float kph)>();
        foreach (float hz in new[] { 1f, 2f, 3.5f, 5f, 7f })
        {
            session.ResetRide();
            session.SeekTo(flat);
            devices.ResetEffort();
            var r = MashFor(session, devices, hz, 45f, flat);
            results.Add((hz, r.watts, r.kph));
            log.AppendLine($"  mash {hz,4:0.0} presses/s -> {r.watts,4:0} W, {r.kph,5:0.0} kph " +
                           $"(cadence {devices.Telemetry.CadenceRpm:0} rpm) [flat, pinned]");
        }
        for (int i = 1; i < results.Count; i++)
        {
            if (results[i].kph <= results[i - 1].kph + 0.2f)
            {
                log.AppendLine($"  FAIL - mashing faster ({results[i].hz}/s) is not faster than " +
                               $"{results[i - 1].hz}/s.");
                failures++;
            }
        }
        if (results[0].kph < 1f) { log.AppendLine("  FAIL - a slow mash does not move him."); failures++; }
        if (results.Last().kph < 20f)
        {
            log.AppendLine("  FAIL - sustained mashing never reaches a strong riding speed.");
            failures++;
        }

        // ---- 3. stop mashing -> coasts to a stop -------------------------------------------
        session.ResetRide();
        session.SeekTo(flat);
        devices.ResetEffort();
        MashFor(session, devices, 5f, 30f, flat);
        float fromKph = session.SpeedKph;
        float coastSeconds = 0f;
        for (int i = 0; i < 60 * 600 && session.SpeedMps > 0.15f; i++)
        {
            session.Tick(Dt);
            session.SeekTo(flat);      // still on the flat: this is coasting, not a descent
            coastSeconds += Dt;
        }
        bool coastOk = session.SpeedMps <= 0.15f;
        if (!coastOk) failures++;
        log.AppendLine($"  [{(coastOk ? "ok" : "FAIL")}] stopped mashing at {fromKph:0.0} kph -> " +
                       $"{session.SpeedKph:0.00} kph after {coastSeconds:0.0} s of coasting " +
                       $"({devices.Telemetry.Watts:0} W)");

        // ---- 4. scripted harnesses are untouched -------------------------------------------
        devices.acceptKeyboardEffort = false;          // what every capture/benchmark sets
        devices.EffortInput = 0f;
        session.ResetRide();
        session.SeekTo(flat);
        for (int i = 0; i < 60 * 20; i++) { session.Tick(Dt); session.SeekTo(flat); }
        bool legacyOk = session.SpeedKph > 15f;
        if (!legacyOk) failures++;
        log.AppendLine($"  [{(legacyOk ? "ok" : "FAIL")}] legacy scripted path " +
                       $"(acceptKeyboardEffort=false) still rides autonomously: " +
                       $"{session.SpeedKph:0.0} kph, {devices.Telemetry.Watts:0} W");

        // ---- 4b. QA fix: HoldZeroPower actually holds, unlike EffortInput=0 alone -----------
        // Regression guard for the "Trainer telemetry ignores zero effort input" finding: two
        // Minato/Shiosai capture harnesses set exactly the settings check 4 just proved ride
        // autonomously, but DOCUMENTED their intent as "player held stationary" - the opposite
        // meaning. DeviceManager.HoldZeroPower is the fix; this proves it actually holds, using
        // a mash-then-release run (mash while it can still drive effort) so a residual mash
        // charge cannot be mistaken for a hold. Same coast-to-a-stop shape as check 3 (real
        // rolling-resistance coastdown takes tens of seconds, not a fixed short window) - the
        // thing that actually distinguishes this from the legacy band is that watts hit ~0
        // essentially at once, rather than settling on a nonzero cruising equilibrium.
        devices.acceptKeyboardEffort = true;   // so MashFor's strokes actually drive effort
        session.ResetRide();
        session.SeekTo(flat);
        devices.ResetEffort();
        MashFor(session, devices, 5f, 10f, flat);
        float fromKphHold = session.SpeedKph;
        devices.acceptKeyboardEffort = false;  // now hand it to the legacy/HoldZeroPower path
        devices.HoldZeroPower = true;
        // A short settle window, not literally one tick: the simulator exponentially smooths
        // watts toward its target (same mechanism the mash-release coast already uses), so it
        // converges within a second rather than instantaneously. The point being proven is
        // "converges toward zero" vs. the bug's "settles on a nonzero ~150-165 W cruising
        // band forever" - one second cleanly tells those two apart.
        for (int i = 0; i < 60; i++) { session.Tick(Dt); session.SeekTo(flat); }
        float wattsRightAfterHold = devices.Telemetry.Watts;
        float holdCoastSeconds = 0f;
        for (int i = 0; i < 60 * 600 && session.SpeedMps > 0.15f; i++)
        {
            session.Tick(Dt);
            session.SeekTo(flat);
            holdCoastSeconds += Dt;
        }
        bool holdZeroOk = wattsRightAfterHold < 5f && session.SpeedMps <= 0.15f;
        if (!holdZeroOk) failures++;
        log.AppendLine($"  [{(holdZeroOk ? "ok" : "FAIL")}] HoldZeroPower after mashing to " +
                       $"{fromKphHold:0.0} kph: watts {wattsRightAfterHold:0} W after 1 s hold, " +
                       $"coasted to {session.SpeedKph:0.00} kph after {holdCoastSeconds:0.0} s " +
                       $"of coasting ({devices.Telemetry.Watts:0} W)");
        devices.HoldZeroPower = false;

        // ---- 5. the pose driver -------------------------------------------------------------
        var poseHost = new GameObject("~RidePoseSelfTest");
        // Inactive BEFORE the rig is added: KuroBikeRig.OnEnable runs a socket/bone search and
        // logs a hard error when there is no bike, which in a headless run is indistinguishable
        // from a real failure. Only its pose-modifier FIELDS are under test here.
        poseHost.SetActive(false);
        var rig = poseHost.AddComponent<KuroBikeRig>();
        var pose = poseHost.AddComponent<KuroRidePose>();
        pose.session = session;
        pose.rig = rig;

        // A point at or past climbGradeFull, so "fully committed" is actually what is asserted;
        // 5 % would legitimately blend to a PARTIAL stand and read as a failure that is not one.
        session.SeekTo(FindGradeDistance(session, 0.075f, true));
        float steep = session.Grade;
        for (int i = 0; i < 90; i++) pose.Tick(Dt);
        bool climbOk = pose.ClimbWeight > 0.9f && rig.poseStandRiseM > 0.05f &&
                       rig.poseExtraSpineLeanDegrees < 0f;
        if (!climbOk) failures++;
        log.AppendLine($"  [{(climbOk ? "ok" : "FAIL")}] at {steep * 100f:0.0} % grade: climb " +
                       $"weight {pose.ClimbWeight:0.00}, rise {rig.poseStandRiseM:0.000} m, " +
                       $"spine {rig.poseExtraSpineLeanDegrees:0.0} deg (negative = opened up)");

        session.SeekTo(flat);
        for (int i = 0; i < 90; i++) pose.Tick(Dt);
        bool seatedOk = pose.ClimbWeight < 0.02f && rig.poseStandRiseM < 0.005f;
        if (!seatedOk) failures++;
        log.AppendLine($"  [{(seatedOk ? "ok" : "FAIL")}] back on the flat " +
                       $"({session.Grade * 100f:0.0} %): climb weight {pose.ClimbWeight:0.00}, " +
                       $"rise {rig.poseStandRiseM:0.000} m");

        float baseCda = session.physics.cdA;
        pose.SprintOverride = 1f;
        session.SeekTo(FindGradeDistance(session, 0.075f, true));
        for (int i = 0; i < 90; i++) pose.Tick(Dt);
        bool sprintOk = pose.SprintWeight > 0.95f && pose.ClimbWeight < 0.05f &&
                        rig.poseExtraSpineLeanDegrees > 0f && session.physics.cdA < baseCda;
        if (!sprintOk) failures++;
        log.AppendLine($"  [{(sprintOk ? "ok" : "FAIL")}] S held on the same climb: sprint " +
                       $"{pose.SprintWeight:0.00} / climb {pose.ClimbWeight:0.00} (sprint wins), " +
                       $"spine +{rig.poseExtraSpineLeanDegrees:0.0} deg, " +
                       $"cdA {baseCda:0.000} -> {session.physics.cdA:0.000}");
        pose.SprintOverride = float.NaN;
        for (int i = 0; i < 120; i++) pose.Tick(Dt);
        bool cdaRestored = Mathf.Abs(session.physics.cdA - baseCda) < 1e-4f;
        if (!cdaRestored) failures++;
        log.AppendLine($"  [{(cdaRestored ? "ok" : "FAIL")}] cdA restored on release: " +
                       $"{session.physics.cdA:0.0000} (base {baseCda:0.0000})");

        // ---- 6. the NPC roster pace ---------------------------------------------------------
        var cyclists = Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None);
        if (cyclists.Length == 0) { log.AppendLine("  FAIL - no staged NPCCyclist."); failures++; }
        else
        {
            float fastest = cyclists.Max(c => c.speed);
            float slowest = cyclists.Min(c => c.speed);
            bool paceOk = fastest <= 3.4f && slowest >= 1.6f && fastest > slowest;
            if (!paceOk) failures++;
            log.AppendLine($"  [{(paceOk ? "ok" : "FAIL")}] {cyclists.Length} staged riders: " +
                           $"{slowest:0.00} - {fastest:0.00} m/s " +
                           $"({slowest * 3.6f:0.0} - {fastest * 3.6f:0.0} kph), variation kept");
        }

        Object.DestroyImmediate(poseHost);
        Object.DestroyImmediate(host);

        log.AppendLine();
        log.AppendLine(failures == 0 ? "[rideinput] PASS - all checks green."
                                     : $"[rideinput] FAIL - {failures} check(s) failed.");
        log.AppendLine("=======================================================");
        Debug.Log(log.ToString());
    }

    // ==================================================================== capture

    [MenuItem("MapleRide/Ride/Capture Ride Input Verification", priority = 45)]
    public static void CaptureRideInput()
    {
        OpenScene();
        Directory.CreateDirectory(OutDir);

        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Debug.LogError("[rideinput] no RideBootstrap."); return; }
        boot.Resolve();

        var session = boot.session;
        var devices = boot.devices;
        var pose = boot.rider != null ? boot.rider.GetComponent<KuroRidePose>() : null;
        if (pose == null) { Debug.LogError("[rideinput] no KuroRidePose - run the staging pass."); return; }
        pose.Resolve();

        devices.acceptKeyboardEffort = true;
        devices.effortSource = DeviceManager.EffortSource.MashPedalStrokes;

        var streamer = Object.FindFirstObjectByType<RouteDressingStreamer>();
        if (streamer != null) streamer.ShowAll();

        session.Graph = null;
        session.EnsureCourse();

        float flat = FindFlatDistance(session, 0.01f);
        float climb = FindGradeDistance(session, 0.075f, true);

        // ---- (a) at rest, no input ---------------------------------------------------------
        Reset(boot, flat);
        _pinM = flat;                      // hold the flat sample: this is about input, not terrain
        for (int i = 0; i < 60 * 8; i++) Step(boot, pose, 0f);
        Shoot(boot, pose, "a_idle_no_input",
              $"no input for 8 s on a {session.DisplayGradePct:0.0} % road: {session.SpeedKph:0.00} kph, " +
              $"{devices.Telemetry.Watts:0} W, {devices.Telemetry.CadenceRpm:0} rpm");

        // ---- (b) mash: slow, fast, then coasting down --------------------------------------
        // Free rides from the same start line, so the DISTANCE covered in the same 20 s is the
        // proof that mashing moves him and that mashing faster moves him further.
        _pinM = float.NaN;
        Reset(boot, flat);
        for (int i = 0; i < 60 * 20; i++) Step(boot, pose, 2f);
        float slowRun = session.TotalDistanceM;
        Shoot(boot, pose, "b1_mash_slow_2hz",
              $"2 presses/s for 20 s: {slowRun:0} m covered, {session.SpeedKph:0.0} kph, " +
              $"{devices.Telemetry.Watts:0} W, {devices.Telemetry.CadenceRpm:0} rpm");

        Reset(boot, flat);
        for (int i = 0; i < 60 * 20; i++) Step(boot, pose, 5f);
        float fastRun = session.TotalDistanceM;
        Shoot(boot, pose, "b2_mash_fast_5hz",
              $"5 presses/s for 20 s: {fastRun:0} m covered (vs {slowRun:0} m at 2/s), " +
              $"{session.SpeedKph:0.0} kph, {devices.Telemetry.Watts:0} W, " +
              $"{devices.Telemetry.CadenceRpm:0} rpm");

        // Coasting is pinned to the flat, or it becomes a descent and never stops - which would
        // be an honest physics result and a completely dishonest test of "he coasts to a stop".
        float fromKph = session.SpeedKph;
        float coast = 0f;
        _pinM = flat;
        session.SeekTo(flat);
        for (int i = 0; i < 60 * 600 && session.SpeedMps > 0.15f; i++) { Step(boot, pose, 0f); coast += Dt; }
        Shoot(boot, pose, "b3_stopped_mashing_coasted_to_stop",
              $"stopped mashing at {fromKph:0.0} kph -> {session.SpeedKph:0.00} kph after " +
              $"{coast:0.0} s of flat-road coasting, {devices.Telemetry.Watts:0} W");

        // ---- (c) the climb pose ------------------------------------------------------------
        Reset(boot, flat);
        _pinM = flat;
        for (int i = 0; i < 60 * 12; i++) Step(boot, pose, 4f);
        ShootRider(boot, pose, "c1_seated_cruise_on_the_flat",
                   $"grade {session.DisplayGradePct:0.0} %, climb weight {pose.ClimbWeight:0.00}, " +
                   $"rise {pose.rig.poseStandRiseM:0.000} m");

        Reset(boot, climb);
        _pinM = climb;
        for (int i = 0; i < 60 * 12; i++) Step(boot, pose, 4f);
        ShootRider(boot, pose, "c2_climber_out_of_saddle",
                   $"grade {session.DisplayGradePct:0.0} %, climb weight {pose.ClimbWeight:0.00}, " +
                   $"rise {pose.rig.poseStandRiseM:0.000} m, spine {pose.rig.poseExtraSpineLeanDegrees:0.0}");

        // ---- (d) the sprint pose -----------------------------------------------------------
        pose.SprintOverride = 1f;
        for (int i = 0; i < 60 * 4; i++) Step(boot, pose, 6f);
        ShootRider(boot, pose, "d_sprinter_low_aero",
                   $"S held: sprint {pose.SprintWeight:0.00}, climb {pose.ClimbWeight:0.00}, " +
                   $"spine +{pose.rig.poseExtraSpineLeanDegrees:0.0}, cdA {session.physics.cdA:0.000}");
        pose.SprintOverride = float.NaN;
        for (int i = 0; i < 60 * 3; i++) Step(boot, pose, 4f);
        _pinM = float.NaN;

        // ---- (e) NPC pace -------------------------------------------------------------------
        CaptureNpcPace(boot, pose);

        // Leave the scene as a fresh ride, exactly as the other harnesses do.
        Reset(boot, 0f);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[rideinput] captures written to {OutDir}");
    }

    /// <summary>
    /// Renders a roster rider from a FIXED camera before and after 4 s of travel, so the pace is
    /// something that can be LOOKED at rather than read off a field. A still frame cannot show
    /// speed; two frames a known time apart can.
    /// </summary>
    private static void CaptureNpcPace(RideBootstrap boot, KuroRidePose pose)
    {
        var cyc = Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                       FindObjectsSortMode.None)
                        .FirstOrDefault(c => c.gameObject.name == "Sakura NPC Coral");
        if (cyc == null) { Debug.LogWarning("[rideinput] no Coral to pace-check."); return; }

        float progress0 = cyc.progress;
        Vector3 p0 = cyc.transform.position;
        var cam = p0 + Vector3.up * 4.5f - cyc.transform.forward * 16f + cyc.transform.right * 6f;

        ShootFrom(cam, p0 + Vector3.up * 0.8f, "e1_npc_pace_t0",
                  $"Coral at {cyc.speed:0.00} m/s ({cyc.speed * 3.6f:0.0} kph), t = 0 s");

        // Drive the rider by hand: nothing in the editor calls Update.
        const float seconds = 4f;
        AdvanceCyclist(cyc, seconds, Dt);
        var rig = cyc.GetComponentInChildren<KuroBikeRig>(true);
        if (rig != null) rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);

        float travelled = Vector3.Distance(p0, cyc.transform.position);
        ShootFrom(cam, p0 + Vector3.up * 0.8f, "e2_npc_pace_t4",
                  $"same camera, t = {seconds:0} s: travelled {travelled:0.0} m " +
                  $"({travelled / seconds:0.00} m/s)");

        // Put her back exactly where the scene had her.
        cyc.progress = progress0;
        cyc.ApplyPose();
        if (rig != null) rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
    }

    private static void AdvanceCyclist(NPCCyclist cyc, float seconds, float dt)
    {
        for (float t = 0f; t < seconds; t += dt)
        {
            var route = cyc.route;
            if (route == null || route.Length < 2) return;
            float distance = cyc.speed * dt;
            while (distance > 0f)
            {
                int i = Mathf.Clamp(Mathf.FloorToInt(cyc.progress), 0, route.Length - 2);
                float len = Mathf.Max(0.01f, Vector3.Distance(route[i], route[i + 1]));
                float step = Mathf.Min(distance, len * (1f - (cyc.progress - i)));
                cyc.progress += step / len;
                distance -= step;
                if (cyc.progress >= route.Length - 1) cyc.progress = 0f;
            }
        }
        cyc.ApplyPose();
    }

    // ==================================================================== drive helpers

    private static void Reset(RideBootstrap boot, float metres)
    {
        boot.devices.ResetEffort();
        // ResetRide (not SeekTo alone) because SeekTo is an arc JUMP and deliberately keeps the
        // rider's momentum - a capture that inherits the previous shot's speed proves nothing.
        boot.session.ResetRide();
        boot.session.SeekTo(metres);
        _strokeClock = 0f;
        if (boot.follower != null) boot.follower.Apply();
    }

    /// <summary>One capture frame: mash at <paramref name="hz"/>, then step the whole chain.</summary>
    private static void Step(RideBootstrap boot, KuroRidePose pose, float hz)
    {
        _strokeClock += Dt;
        if (hz > 0f)
        {
            float period = 1f / hz;
            while (_strokeClock >= period) { boot.devices.PedalStroke(); _strokeClock -= period; }
        }
        else _strokeClock = 0f;

        boot.session.Tick(Dt);
        // Optional treadmill pin: holds the arc position (and therefore the gradient) while the
        // physics keep integrating, so a shot is of the INPUT rather than of the terrain the
        // rider happened to drift onto. NaN = ride the road normally.
        if (!float.IsNaN(_pinM)) boot.session.SeekTo(_pinM);
        if (boot.follower != null) boot.follower.Apply();
        pose.Tick(Dt);                                  // 1. driver writes the modifiers
        if (pose.rig != null)                           // 2. the rig consumes them
            pose.rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
    }

    private static float _strokeClock;
    private static float _pinM = float.NaN;

    private static (float watts, float kph) MashFor(RideSession session, DeviceManager devices,
                                                    float hz, float seconds, float pinAtM)
    {
        float clock = 0f, period = 1f / Mathf.Max(0.01f, hz);
        for (float t = 0f; t < seconds; t += Dt)
        {
            clock += Dt;
            while (clock >= period) { devices.PedalStroke(); clock -= period; }
            session.Tick(Dt);
            // Treadmill: hold the arc position so the gradient stays the one being tested.
            // SeekTo is an arc jump and deliberately preserves SpeedMps, which is the whole
            // point - the physics keep integrating, only the road under him stops changing.
            if (!float.IsNaN(pinAtM)) session.SeekTo(pinAtM);
        }
        return (devices.Telemetry.Watts, session.SpeedKph);
    }

    /// <summary>Arc position whose gradient is closest to flat.</summary>
    private static float FindFlatDistance(RideSession session, float tolerance)
    {
        var course = session.Course;
        float best = 0f, bestGrade = float.MaxValue;
        for (float d = 20f; d < course.Length - 20f; d += 6f)
        {
            float g = Mathf.Abs(course.GradeAt(d, session.gradeWindowM));
            if (g < bestGrade) { bestGrade = g; best = d; }
            if (g < tolerance) return d;
        }
        return best;
    }

    /// <summary>Arc position at or beyond a gradient, so the climb shot is a real climb.</summary>
    private static float FindGradeDistance(RideSession session, float grade, bool uphill)
    {
        var course = session.Course;
        float best = 0f, bestG = uphill ? float.MinValue : float.MaxValue;
        for (float d = 20f; d < course.Length - 20f; d += 4f)
        {
            float g = course.GradeAt(d, session.gradeWindowM);
            if (uphill ? g > bestG : g < bestG) { bestG = g; best = d; }
        }
        // Prefer the FIRST place that clears the bar over the single steepest metre: the steepest
        // point is usually a one-sample spike the rider is through in half a second.
        for (float d = 20f; d < course.Length - 20f; d += 4f)
        {
            float g = course.GradeAt(d, session.gradeWindowM);
            if (uphill ? g >= grade : g <= -grade) return d;
        }
        return best;
    }

    // ==================================================================== render

    /// <summary>Chase view with the real HUD, so the watts/cadence/speed can be read off.</summary>
    private static void Shoot(RideBootstrap boot, KuroRidePose pose, string name, string note)
    {
        if (boot.hud != null)
        {
            if (boot.hud.Canvas == null) boot.hud.Build();
            boot.hud.Refresh();
            Canvas.ForceUpdateCanvases();
        }
        var session = boot.session;
        var course = session.Course;
        float d = session.DistanceM;
        var p = course.PositionAt(d);
        var t = course.TangentAt(d);
        var s = course.SideAt(d);
        RenderView(boot, p + Vector3.up * 2.6f - t * 6.4f - s * 0.9f,
                   p + t * 8f + Vector3.up * 1.1f, 55f, true, name);
        Debug.Log($"[rideinput] {name}: {note}");
    }

    /// <summary>
    /// Close rear-quarter view of the rider himself, framed like the kuro_pose model sheet
    /// (which is drawn from BEHIND) so the silhouette can be compared against it directly.
    /// </summary>
    private static void ShootRider(RideBootstrap boot, KuroRidePose pose, string name, string note)
    {
        var rider = boot.rider;
        var p = rider.position + Vector3.up * 0.75f;
        var eye = p - rider.forward * 3.0f + rider.right * 1.15f + Vector3.up * 0.55f;
        RenderView(boot, eye, p, 34f, false, name);
        Debug.Log($"[rideinput] {name}: {note}");
    }

    private static void ShootFrom(Vector3 eye, Vector3 look, string name, string note)
    {
        RenderView(null, eye, look, 42f, false, name);
        Debug.Log($"[rideinput] {name}: {note}");
    }

    /// <summary>
    /// Renders one frame, optionally compositing the real HUD canvas on a second, un-graded
    /// camera - the same arrangement RideValidation.RenderRiderView uses, and for the same
    /// reason: running the UI through SakuraPostFX turns the washi cards pink.
    /// </summary>
    private static void RenderView(RideBootstrap boot, Vector3 eye, Vector3 look, float fov,
                                   bool withHud, string name)
    {
        const int W = 1600, H = 900;

        var go = new GameObject("~RideInputCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = eye;
        cam.transform.LookAt(look);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        cam.cullingMask = ~(1 << HudSprites.UiLayer);
        go.AddComponent<SakuraPostFX>();
        SakuraPassEnvironment.TunePostFX(go.GetComponent<SakuraPostFX>());

        var tex = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = tex;

        Camera hudCam = null;
        Canvas canvas = null;
        RenderMode prevMode = RenderMode.ScreenSpaceOverlay;
        Camera prevCam = null;
        float prevPlane = 0f;

        if (withHud && boot != null && boot.hud != null && boot.hud.Canvas != null)
        {
            var hudGo = new GameObject("~RideInputHudCam");
            hudGo.transform.SetParent(go.transform, false);
            hudCam = hudGo.AddComponent<Camera>();
            hudCam.clearFlags = CameraClearFlags.Depth;
            hudCam.cullingMask = 1 << HudSprites.UiLayer;
            hudCam.fieldOfView = fov;
            hudCam.nearClipPlane = 0.05f;
            hudCam.farClipPlane = 100f;
            hudCam.depth = cam.depth + 10f;
            hudCam.allowHDR = false;
            hudCam.targetTexture = tex;

            canvas = boot.hud.Canvas;
            prevMode = canvas.renderMode;
            prevCam = canvas.worldCamera;
            prevPlane = canvas.planeDistance;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = hudCam;
            canvas.planeDistance = 1f;
            HudSprites.SetLayerRecursively(canvas.gameObject, HudSprites.UiLayer);
            Canvas.ForceUpdateCanvases();
        }

        cam.Render();
        if (hudCam != null) hudCam.Render();

        var prevActive = RenderTexture.active;
        RenderTexture.active = tex;
        var img = new Texture2D(W, H, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prevActive;

        cam.targetTexture = null;
        if (hudCam != null) hudCam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);

        if (canvas != null)
        {
            canvas.renderMode = prevMode;
            canvas.worldCamera = prevCam;
            canvas.planeDistance = prevPlane;
        }
    }

    private static void OpenScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Play-mode proof that racers RIDE while they challenge Kuro (user direction 2026-09-26: "NPCs
/// should not be stationary when asking to race or challenge"). Driven by RaceRidingCapture
/// (editor), which backs up and restores the player's saves around it.
///
/// Kuro is driven along the road at a scripted speed (the harness cannot pedal), and for each
/// racer checked: they are in the saddle and moving; they pace Kuro abeam while the "E  Race"
/// prompt is up (position and crank angle change across frames, never inside Kuro's line);
/// ignored, they ride off; after an accepted race they are back on the road, riding off.
/// Frames: reference/good_graphics/race_riding/*.png. "[race-ride] RESULT PASS n/n".
/// </summary>
public sealed class RaceRidingPlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public static bool Finished, Failed;
    private int _pass, _fail;

    private RideBootstrap _boot;
    private RaceDirector _race;
    private Camera _cam, _uiCam;
    private RenderTexture _rt;
    private const int W = 1600, H = 900;
    private float _kuroM;
    private float _minLateral = float.MaxValue;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[race-ride] PASS " : "[race-ride] FAIL ") + what);
    }

    private IEnumerator Start()
    {
        Finished = Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        SetUpCameras();
        yield return null;

        yield return Travel(RegionCatalog.MapleCity);
        _race = _boot.GetComponent<RaceDirector>();
        if (_race == null) { Fail("no RaceDirector"); yield break; }
        for (int i = 0; i < 60 && _race.Racers.Count < 6; i++) yield return null;

        // --- every racer on the course is in the saddle and really riding (none stationary).
        // Per-frame speed from the transform, not net displacement: the patrol loop teleports a
        // rider ~150 m back to their run-in, which the old "moved > 0.5 m" check counted as riding.
        yield return Seek(20f, 30);
        var prevP = new Dictionary<RaceNpc, Vector3>();
        var prevM = new Dictionary<RaceNpc, float>();
        var st = new Dictionary<RaceNpc, int[]>();      // valid frames, in-band frames, resets
        var fwdM = new Dictionary<RaceNpc, float>();
        var fwdT = new Dictionary<RaceNpc, float>();
        var vMin = new Dictionary<RaceNpc, float>();
        var vMax = new Dictionary<RaceNpc, float>();
        foreach (var n in _race.Racers)
            if (n != null && n.def.CourseId == "maple_city_crit")
            {
                prevP[n] = n.transform.position; prevM[n] = n.RouteM; st[n] = new int[3];
                fwdM[n] = fwdT[n] = 0f; vMin[n] = float.MaxValue; vMax[n] = 0f;
            }
        for (int f = 0; f < 60; f++)
        {
            yield return null;
            float dt = Time.deltaTime;
            foreach (var n in new List<RaceNpc>(prevP.Keys))
            {
                Vector3 p = n.transform.position;
                float dm = n.Gap(n.RouteM, prevM[n]), dp = Vector3.Distance(p, prevP[n]);
                prevP[n] = p; prevM[n] = n.RouteM;
                if (Mathf.Abs(dm) > 20f || dp > 20f) { st[n][2]++; continue; }   // patrol reset frame: skipped
                if (dt <= 1e-4f) continue;
                float v = dp / dt;
                st[n][0]++;
                if (v >= 2f && v <= 12f) st[n][1]++;
                vMin[n] = Mathf.Min(vMin[n], v); vMax[n] = Mathf.Max(vMax[n], v);
                fwdM[n] += dm; fwdT[n] += dt;
            }
        }
        foreach (var n in prevP.Keys)
        {
            bool saddle = n.dismount == null || n.dismount.T < 0.01f;
            int valid = st[n][0], band = st[n][1], resets = st[n][2];
            float avgFwd = fwdT[n] > 0f ? fwdM[n] / fwdT[n] : 0f;
            Check(n.AmbientRiding && saddle && valid >= 40 && band >= Mathf.CeilToInt(valid * 0.95f) && avgFwd >= 2f,
                  $"{n.def.Name} rides the course: in saddle {saddle}, {band}/{valid} frames at 2-12 m/s " +
                  $"(per-frame {vMin[n]:0.0}-{vMax[n]:0.0} m/s), forward {fwdM[n]:0.0} m in {fwdT[n]:0.0} s = {avgFwd:0.0} m/s, " +
                  $"{resets} patrol-reset frame(s) skipped, state {n.State}");
        }

        // --- clearance maths (synthetic bodies): hold behind a body ahead in the rider's line,
        // follow a slower same-way rider at the gap, ignore one beside the line or behind
        {
            var probe = _race.FindRacer("kenji") ?? _race.FindRacer("hana");
            var c = _boot.session.Course;
            var dummy = new GameObject("~ClearanceProbe").transform;
            Vector3 f = c.TangentAt(probe.RouteM), s = c.SideAt(probe.RouteM), me = probe.transform.position;
            var list = new List<RaceNpc.Body> { new RaceNpc.Body { Pos = me + f * 6f, Vel = Vector3.zero, Root = dummy } };
            float capStill = probe.ClearanceCap(list, false);
            list[0] = new RaceNpc.Body { Pos = me + f * 6f, Vel = f * 5f, Root = dummy };
            float capFollow = probe.ClearanceCap(list, false);
            list[0] = new RaceNpc.Body { Pos = me + f * 6f + s * 1.6f, Vel = Vector3.zero, Root = dummy };
            float capBeside = probe.ClearanceCap(list, false);
            list[0] = new RaceNpc.Body { Pos = me - f * 4f, Vel = Vector3.zero, Root = dummy };
            float capBehind = probe.ClearanceCap(list, false);
            // oncoming head-on in the rider's line: no dead stop - it steps aside instead
            list[0] = new RaceNpc.Body { Pos = me + f * 8f, Vel = -f * 5f, Root = dummy };
            float capOncoming = probe.ClearanceCap(list, false);
            float evade = probe.LastEvadeLane;
            Destroy(dummy.gameObject);
            Check(capStill < 3.5f && capFollow > 6f && capFollow < 9f && float.IsInfinity(capBeside) && float.IsInfinity(capBehind),
                  $"clearance cap: still body 6 m ahead {capStill:0.0} m/s, 5 m/s rider 6 m ahead {capFollow:0.0} m/s, " +
                  $"beside the line {capBeside}, behind {capBehind}");
            Check(float.IsInfinity(capOncoming) && !float.IsNaN(evade) && Mathf.Abs(evade - probe.Lane) > 1.2f,
                  $"oncoming rider head-on 8 m ahead: steps aside (lane {probe.Lane:0.00} -> {evade:0.00}) instead of stopping (cap {capOncoming})");
        }
        yield return LiveClearance();

        // --- Hana: Kuro rides up from behind at 8 m/s; she paces him while the prompt is up
        var hana = _race.FindRacer("hana");
        if (hana == null) { Fail("Hana not taken over"); yield break; }
        float hanaOffer = hana.offerSeconds;
        hana.offerSeconds = 120f;   // test only: this section paces her for longer than the real 20 s offer
        _kuroM = hana.RouteM - 45f;
        yield return Seek(_kuroM, 5);
        bool prompted = false, greeted = false, waved = false, waveShot = false;
        float waveLift = 0f;
        _minLateral = float.MaxValue;
        for (float t = 0f; t < 25f && !(prompted && greeted && waveShot); t += Time.deltaTime)
        {
            yield return Drive(8f);
            prompted |= _race.Prompt.Visible && hana.State == RaceNpc.RideState.Pace;
            greeted |= GreetingShowing(hana);
            if (hana.Waving)
            {
                waved = true;
                waveLift = Mathf.Max(waveLift, WaveLift(hana));
                if (!waveShot && hana.WaveTime > 0.75f)
                {
                    waveShot = true;
                    Debug.Log($"[race-ride] wave: hand {WaveLift(hana):0.00} m above its hood, greeting {GreetingShowing(hana)}, prompt {_race.Prompt.Visible}");
                    yield return Capture("ride_wave_hana", true, hana);
                    yield return CloseUp(hana, "ride_wave_hana_close", true, 8f);
                }
            }
        }
        Check(prompted, $"Hana paces Kuro and the prompt shows (state {hana.State}, gap {hana.Gap(hana.RouteM, _boot.session.DistanceM):0.0} m)");
        Check(greeted, $"Hana's greeting card shows her challenge line while she rides (\"{hana.def.Challenge}\")");
        Check(waved && waveLift > 0.12f, $"Hana waves while riding: the hand on Kuro's side leaves the bars ({waveLift:0.00} m above its hood at the top; other hand stays on)");
        for (int f = 0; f < 30; f++) yield return Drive(8f);
        yield return Capture("ride_prompt_hana_greet", true, hana);
        // she greets from just behind, then moves up alongside/ahead of Kuro
        for (float t = 0f; t < 8.5f; t += Time.deltaTime) yield return Drive(8f);
        Check(hana.State == RaceNpc.RideState.Pace && hana.Gap(hana.RouteM, _boot.session.DistanceM) > 2f,
              $"after greeting Hana rides just ahead of Kuro (gap {hana.Gap(hana.RouteM, _boot.session.DistanceM):0.0} m)");
        // hold the pace for a few seconds: frames from the chase camera, HUD on
        var pos = new List<Vector3>();
        var crank = new List<float>();
        var hRig = hana.rig;
        for (int k = 0; k < 3; k++)
        {
            for (int f = 0; f < 20; f++) yield return Drive(8f);
            pos.Add(hana.transform.position);
            crank.Add(hRig != null ? hRig.CrankAngleDegrees : 0f);
            yield return Capture($"ride_prompt_hana_{k + 1}", true, hana);
            Debug.Log($"[race-ride] hana frame {k + 1}: pos {hana.transform.position}, route {hana.RouteM:0.0} m, lane {hana.Lane:0.00}, " +
                      $"speed {hana.Speed:0.0} m/s, crank {crank[k]:0} deg, kuro {_boot.session.DistanceM:0.0} m @ {_race.KuroSpeedEstimate:0.0} m/s, prompt {_race.Prompt.Visible}");
        }
        float d01 = Vector3.Distance(pos[0], pos[1]), d12 = Vector3.Distance(pos[1], pos[2]);
        Check(d01 > 2f && d12 > 2f, $"Hana's position changes across prompt frames ({d01:0.0} m, {d12:0.0} m)");
        Check(Mathf.Abs(Mathf.DeltaAngle(crank[0], crank[1])) > 1f || Mathf.Abs(Mathf.DeltaAngle(crank[1], crank[2])) > 1f,
              $"Hana's cranks turn ({crank[0]:0} -> {crank[1]:0} -> {crank[2]:0} deg)");
        Check(_race.Prompt.Visible, $"the prompt stays up while she paces (state {hana.State})");
        Check(Mathf.Abs(hana.Speed - 8f) < 1.5f, $"Hana matches Kuro's speed ({hana.Speed:0.0} vs 8.0 m/s)");
        Check(_minLateral > 0.9f, $"Hana never rides through Kuro (min lateral separation within 2.4 m: {_minLateral:0.00} m)");

        // side-on frames with the camera fixed at the roadside: the pair crosses the frame
        yield return RoadsideSequence(hana, "ride_roadside_hana");

        // close three-quarter of the pedalling pose (camera travels with her), HUD off
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;
        for (int k = 0; k < 2; k++)
        {
            for (int f = 0; f < 6; f++) { yield return Drive(8f); ThreeQuarter(hana); }
            yield return Capture($"ride_pedal_hana_{k + 1}", false);
        }
        if (follow != null) follow.enabled = true;

        // --- Kuro stops (QA HIGH): she eases to a stop beside / just ahead of him, seated, with the
        // cranks still and a foot down; the prompt stays up the whole time; she rides on with him.
        // (a) QA's literal step: pacing at 8 m/s, Kuro set to 0 at once, held for 4 s.
        for (float t = 0f; t < 1.5f; t += Time.deltaTime) yield return Drive(8f);
        yield return StopAndHold(hana, 0f, "instant", "ride_stopped_hana");
        yield return RideOnAfterStop(hana, 8f, "instant");
        // (b) a real stop: Kuro brakes at the physics' own ~0.55 g (CyclingPhysics brake) to 0
        for (float t = 0f; t < 3f; t += Time.deltaTime) yield return Drive(8f);
        yield return StopAndHold(hana, 5.4f, "braked", "ride_stopped_braked_hana");
        yield return RideOnAfterStop(hana, 6f, "braked");
        hana.offerSeconds = hanaOffer;

        // --- accept: race, forfeit, and she is back on the road riding off (no instant re-offer)
        _race.StartRace(hana);
        yield return WaitPhase(RaceDirector.Phase.Racing, 12f);
        Check(_race.CurrentPhase == RaceDirector.Phase.Racing, "accepting starts the race");
        for (int f = 0; f < 90; f++) { _race.PedalOverride = 1f; yield return null; }
        _race.Forfeit();
        _race.PedalOverride = float.NaN;
        yield return WaitPhase(RaceDirector.Phase.Results, 3f);
        _race.AutoContinue = true;
        yield return WaitPhase(RaceDirector.Phase.Idle, 8f);
        _race.AutoContinue = false;
        _kuroM = _boot.session.TotalDistanceM;
        Vector3 p0 = hana.transform.position;
        for (int f = 0; f < 30; f++) yield return Drive(5f);
        Check(hana.AmbientRiding && hana.State == RaceNpc.RideState.RideOff && Vector3.Distance(p0, hana.transform.position) > 2f,
              $"after the race Hana rides off ({hana.State}, {hana.Speed:0.0} m/s)");
        Check(!hana.OffersRace, "no instant re-offer from Hana while she rides off");
        yield return Capture("ride_after_race_hana", true);

        // --- ignored: Mei offers, then rides off when Kuro doesn't answer
        var mei = _race.FindRacer("mei");
        if (mei != null)
        {
            mei.offerSeconds = 3f;   // test only: the real wait is PROVISIONAL 20 s
            _kuroM = mei.RouteM - 40f;
            yield return Seek(_kuroM, 5);
            bool paced = false;
            for (float t = 0f; t < 25f && !paced; t += Time.deltaTime) { yield return Drive(8f); paced = mei.State == RaceNpc.RideState.Pace; }
            Check(paced, "Mei paces Kuro");
            yield return Capture("ride_prompt_mei", true, mei);
            bool off = false;
            for (float t = 0f; t < 8f && !off; t += Time.deltaTime) { yield return Drive(6f); off = mei.State == RaceNpc.RideState.RideOff; }
            float g0 = mei.Gap(mei.RouteM, _boot.session.DistanceM);
            for (int f = 0; f < 90; f++) yield return Drive(6f);
            float g1 = mei.Gap(mei.RouteM, _boot.session.DistanceM);
            Check(off && g1 > g0 + 2f, $"ignored, Mei rides off ahead (gap {g0:0.0} -> {g1:0.0} m)");
            yield return Capture("ride_rideoff_mei", true);
        }

        // --- every other racer, in every region, rides up and paces Kuro with the prompt up
        LogTraffic("maple_city (hana, mei)");
        // (greeting card + riding wave checked for each; closest traffic logged per region)
        foreach (var (region, course, ids) in new[]
                 {
                     (RegionCatalog.MapleCity, "maple_city_crit", new[] { "kenji", "taro", "sota", "hiro" }),
                     (RegionCatalog.SakuraPass, "sakura_circuit", new[] { "ren", "mika", "yuki" }),
                     (RegionCatalog.MinatoCoast, "minato_crossing", new[] { "marina", "kohaku" }),
                     (RegionCatalog.TakaMountains, "taka_high_road", new[] { "kazan" }),
                     (RegionCatalog.ShiosaiCoast, "shiosai_breeze", new[] { "nami" }),
                     ("nagisa_bay", "nagisa_bay_loop", new[] { "kaimana" }),
                 })
        {
            if (_boot.session.courseId != course) yield return Travel(region);
            if (_boot.session.courseId != course) { _boot.session.SelectCourse(course); yield return null; }
            for (int i = 0; i < 30; i++) yield return null;
            ResetTrafficLog();
            foreach (var id in ids)
            {
                var n = _race.FindRacer(id);
                if (n == null) { Check(false, $"{id} taken over in {region}"); continue; }
                _kuroM = n.RouteM - 30f;
                yield return Seek(_kuroM, 5);
                bool p = false, greet = false, wave = false;
                Vector3 a = n.transform.position;
                for (float t = 0f; t < 20f && !p; t += Time.deltaTime)
                {
                    yield return Drive(8f);
                    p = _race.Prompt.Visible && n.State == RaceNpc.RideState.Pace;
                    greet |= GreetingShowing(n); wave |= n.Waving;
                }
                // then ride together; a racer briefly held behind traffic (or tucked in behind Kuro
                // for an oncoming rider) gets up to 10 s to come back up alongside with the prompt
                // (a trace is logged every ~0.5 s)
                float settleT = 0f, traceT = 0f;
                int lostFrames = 0;
                for (int f = 0; f < 60 || (settleT < 10f && (Mathf.Abs(n.Speed - 8f) > 1.2f ||
                         n.State != RaceNpc.RideState.Pace || !_race.Prompt.Visible)); f++)
                {
                    yield return Drive(8f);
                    settleT += Time.deltaTime; traceT += Time.deltaTime;
                    greet |= GreetingShowing(n); wave |= n.Waving;
                    if (p && (n.State != RaceNpc.RideState.Pace || !_race.Prompt.Visible)) lostFrames++;
                    if (traceT > 0.5f)
                    {
                        traceT = 0f;
                        Debug.Log($"[race-ride] trace {id}: {n.Speed:0.0} m/s, gap {n.Gap(n.RouteM, _boot.session.DistanceM):0.0} m, lane {n.Lane:0.00}, " +
                                  $"state {n.State}, holding {n.Holding}, blocked {n.ClearBlocked} ({n.ClearAheadM:0.0} m), pass {n.PassLane:0.00}, evade {n.EvadeLane:0.00}, prompt {_race.Prompt.Visible}");
                    }
                }
                // still pacing WITH the prompt up at the end (the capture frame) - QA re-check: a
                // racer that was stopped by traffic and dropped back must not pass on its early peak
                float gapEnd = n.Gap(n.RouteM, _boot.session.DistanceM);
                Check(p && n.AmbientRiding && Vector3.Distance(a, n.transform.position) > 3f && n.Speed > 5f &&
                      n.State == RaceNpc.RideState.Pace && _race.Prompt.Visible && gapEnd > -3f && gapEnd < 6f,
                      $"{id} ({region}) rides up and paces Kuro with the prompt up ({n.Speed:0.0} m/s, state {n.State}, prompt {_race.Prompt.Visible}, " +
                      $"gap {gapEnd:0.0} m, lost pace/prompt {lostFrames} frames, holding {n.Holding}, " +
                      $"clearance blocked {n.ClearBlocked} ahead {n.ClearAheadM:0.0} m, pass lane {n.PassLane:0.00}, Kuro est {_race.KuroSpeedEstimate:0.0})");
                Check(greet && wave, $"{id} greets while riding (card shows their line: {greet}, wave: {wave})");
                yield return Capture($"ride_prompt_{id}", true, n);
                if (id == "marina") yield return MarinaTimeTrial(n);
            }
            LogTraffic(region);
        }
        Check(_promptChecks > 0 && _promptOverlaps == 0,
              $"prompt card never covers the rider's on-screen box ({_promptOverlaps} overlaps in {_promptChecks} prompt frames)");

        _race.KuroSpeedOverride = float.NaN;
        Debug.Log($"[race-ride] RESULT {(_fail == 0 ? "PASS" : "FAIL")} {_pass}/{_pass + _fail}");
        Failed = _fail > 0;
        Finished = true;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>One frame of Kuro riding at <paramref name="v"/> m/s along the road.</summary>
    private IEnumerator Drive(float v)
    {
        _race.KuroSpeedOverride = v;
        _kuroM += v * Mathf.Max(0.001f, Time.deltaTime);
        _boot.session.SeekTo(_kuroM);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        TrackSeparation();
        yield return null;
    }

    private void TrackSeparation()
    {
        var s = _boot.session;
        float kl = _boot.follower != null ? _boot.follower.ActiveLaneOffset : -1.7f;
        foreach (var n in _race.Racers)
        {
            if (n == null || !n.AmbientRiding || n.def.CourseId != s.courseId) continue;
            if (Mathf.Abs(n.Gap(n.RouteM, s.DistanceM)) < 2.4f) _minLateral = Mathf.Min(_minLateral, Mathf.Abs(n.Lane - kl));
        }
        TrackTraffic();
    }

    // ------------------------------------------------------------------ traffic log (QA: log the closest distance)

    private float _minTraffic = float.MaxValue, _minAheadInLine = float.MaxValue;
    private string _minTrafficWho = "-", _minAheadWho = "-";
    private int _blockedFrames;

    private void ResetTrafficLog()
    {
        _minTraffic = _minAheadInLine = float.MaxValue;
        _minTrafficWho = _minAheadWho = "-";
        _blockedFrames = 0;
    }

    private void TrackTraffic()
    {
        var s = _boot.session;
        var bodies = _race.Traffic;
        foreach (var n in _race.Racers)
        {
            if (n == null || !n.AmbientRiding || n.def.CourseId != s.courseId || !n.gameObject.activeInHierarchy) continue;
            Vector3 me = n.transform.position;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b.Root == null || b.Root == n.transform || b.Root.IsChildOf(n.transform)) continue;
                float d = Vector3.Distance(b.Pos, me);
                if (d < _minTraffic) { _minTraffic = d; _minTrafficWho = $"{n.def.Name} <-> {b.Root.name}"; }
            }
            if (n.ClearAheadM < _minAheadInLine) { _minAheadInLine = n.ClearAheadM; _minAheadWho = n.def.Name; }
            if (n.ClearBlocked) _blockedFrames++;
        }
    }

    private void LogTraffic(string where)
    {
        Debug.Log($"[race-ride] traffic {where}: closest rider/traffic to any riding racer {(_minTraffic < 1e6f ? _minTraffic.ToString("0.00") : "none")} m " +
                  $"({_minTrafficWho}); closest body AHEAD in a racer's own line {(_minAheadInLine < 1e6f ? _minAheadInLine.ToString("0.00") : "none")} m " +
                  $"({_minAheadWho}); {_blockedFrames} racer-frames slowed/held by clearance; {_race.Traffic.Count} bodies tracked now");
        ResetTrafficLog();
    }

    // ------------------------------------------------------------------ greeting / wave probes

    /// <summary>The shared greeting card is up (alpha) and shows this racer's own line.</summary>
    private static bool GreetingShowing(RaceNpc n)
    {
        var card = NpcGreetingCard.Instance;
        if (card == null) return false;
        var cg = card.GetComponentInChildren<CanvasGroup>(true);
        if (cg == null || cg.alpha < 0.5f) return false;
        foreach (var t in card.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            if (t.text == n.def.Challenge) return true;
        return false;
    }

    /// <summary>How far (m) the waving hand (Kuro's side) is above its brake hood.</summary>
    private static float WaveLift(RaceNpc n)
    {
        var r = n.rig;
        if (r == null) return 0f;
        bool left = n.kerbSide < 0;
        var wrist = left ? r.WristL : r.WristR;
        var hood = left ? r.HoodL : r.HoodR;
        return wrist != null && hood != null ? Vector3.Dot(wrist.position - hood.position, r.transform.up) : 0f;
    }

    /// <summary>Free hand still on its hood (m from wrist to hood).</summary>
    private static float BarHandGap(RaceNpc n)
    {
        var r = n.rig;
        if (r == null) return 99f;
        bool left = n.kerbSide < 0;
        var wrist = left ? r.WristR : r.WristL;
        var hood = left ? r.HoodR : r.HoodL;
        return wrist != null && hood != null ? Vector3.Distance(wrist.position, hood.position) : 99f;
    }

    // ------------------------------------------------------------------ stopping with Kuro

    /// <summary>Kuro stops (instantly when <paramref name="brakeMps2"/> = 0, else braking at that
    /// rate), then stays stopped for 4 s. Asserts the prompt never drops, the rider stops beside /
    /// just ahead of him at the side gap, seated, cranks still, foot down.</summary>
    private IEnumerator StopAndHold(RaceNpc n, float brakeMps2, string tag, string shot)
    {
        var s = _boot.session;
        float v = 8f;
        int promptLost = 0, frames = 0;
        float maxGap = float.MinValue, peakSpeedAfter1s = 0f;
        while (v > 0f && brakeMps2 > 0f)
        {
            v = Mathf.Max(0f, v - brakeMps2 * Mathf.Max(0.001f, Time.deltaTime));
            yield return Drive(v);
            frames++;
            if (!_race.Prompt.Visible) promptLost++;
        }
        Debug.Log($"[race-ride] stop ({tag}): Kuro stopped at {s.DistanceM:0.0} m; {n.def.Name} {n.Speed:0.0} m/s, gap {n.Gap(n.RouteM, s.DistanceM):0.0} m, holding {n.Holding}");
        float crankAt3 = float.NaN;
        bool waveShot = false;
        for (float t = 0f; t < 4f; t += Time.deltaTime)
        {
            yield return Drive(0f);
            frames++;
            if (!_race.Prompt.Visible) promptLost++;
            maxGap = Mathf.Max(maxGap, n.Gap(n.RouteM, s.DistanceM));
            if (t > 2.5f) peakSpeedAfter1s = Mathf.Max(peakSpeedAfter1s, n.Speed);
            if (float.IsNaN(crankAt3) && t > 3f && n.rig != null) crankAt3 = n.rig.CrankAngleDegrees;
            if (tag == "instant" && !waveShot && n.Waving && n.WaveTime > 0.75f)
            {
                waveShot = true;
                Debug.Log($"[race-ride] stopped wave: hand {WaveLift(n):0.00} m above hood, bar hand {BarHandGap(n):0.00} m from its hood, foot down {n.FootDown:0.00}");
                yield return CloseUp(n, "ride_stopped_wave_hana_close", true);
            }
        }
        float gap = n.Gap(n.RouteM, s.DistanceM);
        float kl = _boot.follower != null ? _boot.follower.ActiveLaneOffset : -1.7f;
        float side = Mathf.Abs(n.Lane - kl);
        float crankNow = n.rig != null ? n.rig.CrankAngleDegrees : 0f;
        bool saddle = n.dismount == null || n.dismount.T < 0.01f;
        Debug.Log($"[race-ride] stop ({tag}) +4 s: {n.def.Name} speed {n.Speed:0.00} m/s, holding {n.Holding}, state {n.State}, " +
                  $"gap {gap:0.0} m (max {maxGap:0.0}), side gap {side:0.00} m, foot down {n.FootDown:0.00}, crank {crankAt3:0.0} -> {crankNow:0.0} deg, " +
                  $"prompt lost {promptLost}/{frames} frames, Kuro est {_race.KuroSpeedEstimate:0.00} m/s");
        Check(_race.Prompt.Visible && promptLost == 0,
              $"[{tag}] prompt stays up while Kuro is stopped ({promptLost} of {frames} frames without it)");
        Check(n.Speed <= 0.1f && peakSpeedAfter1s <= 0.5f && n.Holding,
              $"[{tag}] {n.def.Name} stops with Kuro (speed {n.Speed:0.00} m/s, max {peakSpeedAfter1s:0.00} after 2.5 s, holding {n.Holding})");
        float maxAhead = brakeMps2 > 0f ? 8f : 13f;
        Check(gap > -1.5f && gap < maxAhead && Mathf.Abs(side - 1.4f) < 0.3f,
              $"[{tag}] she waits beside / just ahead of Kuro ({gap:0.0} m ahead, limit {maxAhead:0}; side gap {side:0.00} m, want 1.4)");
        Check(saddle && n.AmbientRiding && Mathf.Abs(Mathf.DeltaAngle(crankAt3, crankNow)) < 0.5f && n.FootDown > 0.95f,
              $"[{tag}] seated on the bike, cranks still, foot down (saddle {saddle}, crank moved {Mathf.DeltaAngle(crankAt3, crankNow):0.0} deg in the last second, foot {n.FootDown:0.00})");
        yield return Capture(shot, true, n);
        if (tag == "instant") yield return CloseUp(n, shot + "_close");
    }

    /// <summary>Kuro rides on: the rider pushes off (foot up) and paces him again.</summary>
    private IEnumerator RideOnAfterStop(RaceNpc n, float v, string tag)
    {
        float foot0 = n.FootDown;
        bool promptAll = true;
        for (float t = 0f; t < 4f; t += Time.deltaTime) { yield return Drive(Mathf.Min(v, 1f + 3f * t)); promptAll &= _race.Prompt.Visible; }
        float gap = n.Gap(n.RouteM, _boot.session.DistanceM);
        Check(n.State == RaceNpc.RideState.Pace && !n.Holding && n.Speed > v - 2f && n.FootDown < 0.05f && gap > -3f && gap < 8f && promptAll,
              $"[{tag}] Kuro rides on and {n.def.Name} rides on with him (state {n.State}, {n.Speed:0.0} m/s vs {v:0}, gap {gap:0.0} m, foot {foot0:0.00} -> {n.FootDown:0.00}, prompt all along {promptAll})");
        if (tag == "instant") yield return Capture("ride_rides_on_hana", true, n);
    }

    private IEnumerator CloseUp(RaceNpc n, string name, bool kuroSide = false, float driveV = 0f)
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;
        // Kuro keeps riding at driveV through the close-up (a pause here reads as Kuro stopping,
        // and a pacing racer now stops with a stopped Kuro)
        for (int f = 0; f < 2; f++) { Frame(n, kuroSide); yield return DriveNoCam(driveV); }
        Frame(n, kuroSide);
        _kuroM += driveV * Mathf.Max(0.001f, Time.deltaTime);
        yield return Capture(name, false);
        if (follow != null) follow.enabled = true;
        SnapCamera();
    }

    /// <summary>Close three-quarter: the road-centre side (planted foot) or Kuro's side (the waving hand).</summary>
    private void Frame(RaceNpc n, bool kuroSide)
    {
        if (!kuroSide) { ThreeQuarter(n, true); return; }
        Vector3 c = n.transform.position + n.transform.up * 0.75f;
        _cam.transform.position = c + n.transform.forward * 2.9f + n.transform.right * (n.kerbSide * 1.25f) + Vector3.up * 0.45f;
        _cam.transform.LookAt(c);
    }

    // ------------------------------------------------------------------ live clearance

    /// <summary>Real riders stopped in the road ahead of a racer (clones of another racer, movers
    /// off). (1) One in its line and one in the passing line: the racer must slow and hold behind.
    /// (2) The passing line clears: the racer must move out and pass without touching the rider.
    /// (3) All clear: it rides on. Distances are bike-to-bike (rig positions).</summary>
    private IEnumerator LiveClearance()
    {
        var n = _race.FindRacer("kenji");
        var donor = _race.FindRacer("mei");
        if (n == null || donor == null) { Check(false, "kenji + mei for the live clearance check"); yield break; }
        // Kuro far behind (outside the rider's awareness, so no pacing), rider on its patrol
        _kuroM = Mathf.Max(0f, n.RouteM - 100f);
        yield return Seek(_kuroM, 3);
        var c = _boot.session.Course;
        float m = c.Wrap(n.RouteM + 16f);
        float passSide = n.kerbSide < 0 ? 1f : -1f;
        GameObject MakeBlock(string name, float lane)
        {
            var go = Instantiate(donor.gameObject, c.PositionAt(m) + c.SideAt(m) * lane + c.UpAt(m) * 0.065f,
                                 Quaternion.LookRotation(c.TangentAt(m), c.UpAt(m)), donor.transform.parent);
            go.name = name;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(mb is KuroBikeRig) && mb.GetType().Name != "KuroOutline") mb.enabled = false;
            return go;
        }
        var blockA = MakeBlock("~Clearance Block A", n.Lane);
        var blockB = MakeBlock("~Clearance Block B", n.Lane + passSide * 1.3f);
        // a controlled scene: only the two blocks count as traffic here (a live oncoming rider
        // would make Kenji step aside and change the scenario under test)
        Transform tA = blockA.transform, tB = blockB.transform;
        _race.TrafficFilter = t => t != null && ((tA != null && t.IsChildOf(tA)) || (tB != null && t.IsChildOf(tB)));
        Transform RigOf(GameObject g) { var r = g.GetComponentInChildren<KuroBikeRig>(true); return r != null ? r.transform : g.transform; }
        Transform kRig = n.rig != null ? n.rig.transform : n.transform, aRig = RigOf(blockA);
        Debug.Log($"[race-ride] live clearance setup: Kenji root {n.transform.position} rig {kRig.position}, lane {n.Lane:0.00}; " +
                  $"block A rig {aRig.position}, block B rig {RigOf(blockB).position}");
        var follow = _cam.GetComponent<KuroFollowCamera>();
        float minAlong = float.MaxValue, minSpeed = float.MaxValue;
        for (float t = 0f; t < 7f; t += Time.deltaTime)
        {
            yield return null;
            minAlong = Mathf.Min(minAlong, Vector3.Dot(aRig.position - kRig.position, c.TangentAt(n.RouteM)));
            if (t > 3f) minSpeed = Mathf.Min(minSpeed, n.Speed);
        }
        if (follow != null) follow.enabled = false;
        SideShot(c, m, n.transform.position, aRig.position);
        yield return Capture("ride_clearance_hold_kenji", false);
        Debug.Log($"[race-ride] live clearance (hold): Kenji {minAlong:0.00} m behind block A, min speed {minSpeed:0.00} m/s, blocked {n.ClearBlocked}, lane {n.Lane:0.00}");
        Check(minAlong > 1.5f && minSpeed < 0.6f,
              $"Kenji slows and holds behind a stopped rider in his line when the passing line is taken (closest {minAlong:0.00} m bike-to-bike, min speed {minSpeed:0.00} m/s)");

        Destroy(blockB);
        float minDist = float.MaxValue, laneMax = n.Lane;
        bool shot = false;
        for (float t = 0f; t < 8f; t += Time.deltaTime)
        {
            yield return null;
            minDist = Mathf.Min(minDist, Vector3.Distance(aRig.position, kRig.position));
            laneMax = passSide > 0 ? Mathf.Max(laneMax, n.Lane) : Mathf.Min(laneMax, n.Lane);
            float along = Vector3.Dot(aRig.position - kRig.position, c.TangentAt(n.RouteM));
            if (!shot && along < 0.6f)
            {
                shot = true;
                SideShot(c, m, n.transform.position, aRig.position);
                yield return Capture("ride_clearance_pass_kenji", false);
            }
        }
        float alongEnd = Vector3.Dot(aRig.position - kRig.position, c.TangentAt(n.RouteM));
        Debug.Log($"[race-ride] live clearance (pass): closest {minDist:0.00} m bike-to-bike, widest lane {laneMax:0.00}, block A now {alongEnd:0.0} m ahead (negative = passed), {n.Speed:0.0} m/s");
        Check(minDist > 0.9f && alongEnd < -2f && n.Speed > 3f,
              $"with the passing line clear Kenji moves out and passes the stopped rider without touching (closest {minDist:0.00} m, now {-alongEnd:0.0} m past, {n.Speed:0.0} m/s)");
        if (follow != null) follow.enabled = true;
        Destroy(blockA);
        _race.TrafficFilter = null;
        yield return null;
    }

    private void SideShot(RouteCourse c, float m, Vector3 a, Vector3 b)
    {
        Vector3 mid = (a + b) * 0.5f;
        _cam.transform.position = mid + c.SideAt(m) * 6.5f + Vector3.up * 2.4f;
        _cam.transform.LookAt(mid + Vector3.up * 0.6f);
    }
    // ------------------------------------------------------------------ Marina Time Trial

    private static int ShownRenderers(RaceNpc n)
    {
        int k = 0;
        foreach (var r in n.GetComponentsInChildren<Renderer>()) if (r.enabled) k++;
        return k;
    }

    /// <summary>Accept Marina's Time Trial: her ghost rides the race, the real Marina is hidden,
    /// and after the race she is back on the road (visible, riding).</summary>
    private IEnumerator MarinaTimeTrial(RaceNpc n)
    {
        int shownBefore = ShownRenderers(n);
        _race.StartRace(n);
        yield return WaitPhase(RaceDirector.Phase.Racing, 12f);
        Check(_race.CurrentPhase == RaceDirector.Phase.Racing, "Marina's Time Trial starts");
        GameObject ghost = null;
        for (int f = 0; f < 150; f++)
        {
            _race.PedalOverride = 1f;
            yield return null;
            if (ghost == null) ghost = GameObject.Find("~Ghost " + n.def.Name);
        }
        bool hidden = !n.gameObject.activeInHierarchy;
        float ghostLead = ghost != null ? _race.Rival.x - _race.Kuro.x : float.NaN;
        Debug.Log($"[race-ride] TT: ghost {(ghost != null ? ghost.name : "none")} at race x {_race.Rival.x:0.0} m (Kuro {_race.Kuro.x:0.0} m), real Marina active {n.gameObject.activeInHierarchy}");
        Check(ghost != null && ghost.activeInHierarchy && hidden && _race.Rival.x > 5f,
              $"TT: the ghost rides the race ({_race.Rival.x:0.0} m in, lead {ghostLead:0.0} m) and the real Marina is hidden ({hidden})");
        yield return Capture("ride_tt_marina_ghost", true);
        _race.Forfeit();
        _race.PedalOverride = float.NaN;
        yield return WaitPhase(RaceDirector.Phase.Results, 3f);
        _race.AutoContinue = true;
        yield return WaitPhase(RaceDirector.Phase.Idle, 8f);
        _race.AutoContinue = false;
        _kuroM = _boot.session.TotalDistanceM;
        for (int f = 0; f < 40; f++) yield return Drive(5f);
        int shownAfter = ShownRenderers(n);
        bool visible = n.gameObject.activeInHierarchy && shownAfter >= Mathf.Max(1, shownBefore - 1);
        Vector3 sp = _cam.WorldToViewportPoint(n.transform.position + Vector3.up * 0.7f);
        bool inView = sp.z > 0f && sp.x > 0f && sp.x < 1f && sp.y > 0f && sp.y < 1f;
        Check(GameObject.Find("~Ghost " + n.def.Name) == null && visible && n.AmbientRiding && n.Speed > 3f && inView,
              $"after the TT the ghost is gone and the real Marina reappears riding ({n.State}, {n.Speed:0.0} m/s, in view {inView}, renderers shown {shownAfter} vs {shownBefore} before)");
        yield return Capture("ride_tt_marina_after", true);
    }

    private IEnumerator RoadsideSequence(RaceNpc n, string name)
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;
        var c = _boot.session.Course;
        float m = n.RouteM + 24f;
        Vector3 side = c.SideAt(m), road = c.PositionAt(m);
        // fixed, a little ahead and up at the road-centre-side kerb: the pair rides toward it
        float hw = RaceDirector.RoadHalfWidth(_boot.session.courseId);
        _cam.transform.position = road + side * (hw + 0.2f) + Vector3.up * 2.3f;
        _cam.transform.LookAt(c.PositionAt(m - 12f) + Vector3.up * 0.8f);
        for (int k = 0; k < 3; k++)
        {
            for (int f = 0; f < 12; f++) yield return DriveNoCam(8f);
            yield return Capture($"{name}_{k + 1}", true);
            Debug.Log($"[race-ride] {name} {k + 1}: {n.def.Name} at {n.transform.position} ({n.Speed:0.0} m/s), prompt {_race.Prompt.Visible}");
        }
        if (follow != null) follow.enabled = true;
    }

    private IEnumerator DriveNoCam(float v)
    {
        _race.KuroSpeedOverride = v;
        _kuroM += v * Mathf.Max(0.001f, Time.deltaTime);
        _boot.session.SeekTo(_kuroM);
        if (_boot.follower != null) _boot.follower.Apply();
        TrackSeparation();
        yield return null;
    }

    private IEnumerator Travel(string region)
    {
        if (_boot.regions != null) _boot.regions.FastTravel(region);
        var r = RegionCatalog.Find(region);
        if (r != null && _boot.session.courseId != r.BuiltCourseId) _boot.session.SelectCourse(r.BuiltCourseId);
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 3;
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.HoldZeroPower = true;
        for (int i = 0; i < 8; i++) yield return null;
    }

    private IEnumerator Seek(float d, int frames)
    {
        if (_race != null) _race.KuroSpeedOverride = 0f;   // teleported, then standing
        _kuroM = Mathf.Max(0f, d);
        _boot.session.SeekTo(_kuroM);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < frames; f++) yield return null;
        SnapCamera();
    }

    private IEnumerator WaitPhase(RaceDirector.Phase p, float timeout)
    {
        float t = 0f;
        while (_race.CurrentPhase != p && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
        if (_race.CurrentPhase != p) Debug.LogWarning($"[race-ride] waited {timeout} s for {p}, still {_race.CurrentPhase}");
    }

    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null || !follow.enabled) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    private void ThreeQuarter(RaceNpc n, bool wide = false)
    {
        Vector3 c = n.transform.position + n.transform.up * (wide ? 0.6f : 0.7f);
        // from ahead and to the road-centre side of the rider (Kuro is on the other side)
        float ahead = wide ? 3.3f : 3.0f, side = wide ? 2.6f : 2.0f, up = wide ? 0.55f : 0.35f;
        _cam.transform.position = c + n.transform.forward * ahead - n.transform.right * (n.kerbSide * side) + Vector3.up * up;
        _cam.transform.LookAt(c);
    }

    private void Fail(string why)
    {
        Debug.LogError("[race-ride] " + why);
        Failed = true;
        Finished = true;
    }

    private void SetUpCameras()
    {
        _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        _cam.targetTexture = _rt;
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);
        var uiGo = new GameObject("~RaceRideHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;
        _uiCam.targetTexture = _rt;
        var hd = uiGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.None;
        hd.volumeLayerMask = 0;
        hd.customRenderingSettings = true;
        foreach (var f in new[] { UnityEngine.Rendering.HighDefinition.FrameSettingsField.AtmosphericScattering,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.Volumetrics,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.CustomPass })
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, false);
        }
    }

    private void RouteOverlayCanvases()
    {
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (c == null || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            var parent = c.transform.parent;
            if (parent != null && parent.GetComponentInParent<Canvas>(true) != null) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = _uiCam;
            c.planeDistance = 1f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
    }

    private void Update()
    {
        if (_uiCam != null) RouteOverlayCanvases();
    }

    private int _promptChecks, _promptOverlaps;

    private IEnumerator Capture(string name, bool hud, RaceNpc subject = null)
    {
        yield return null;   // not WaitForEndOfFrame: it never fires in -batchmode
        RouteOverlayCanvases();
        // QA LOW: the prompt card must never sit on the rider it belongs to
        if (hud && subject != null && _race != null && _race.Prompt.Visible)
        {
            float scale = _race.Ui != null && _race.Ui.Canvas != null ? _race.Ui.Canvas.scaleFactor : 1f;
            if (RaceDirector.RiderScreenRect(_cam, subject, scale, out var rr))
            {
                var card = _race.Prompt.ScreenRect;
                bool over = card.Overlaps(rr);
                _promptChecks++;
                if (over) _promptOverlaps++;
                Debug.Log($"[race-ride] {name}: prompt card {card} vs {subject.def.Name}'s box {rr}{(over ? "  OVERLAP" : "  clear")}");
            }
        }
        _cam.Render();
        if (hud)
        {
            _uiCam.fieldOfView = _cam.fieldOfView;
            Canvas.ForceUpdateCanvases();
            _uiCam.Render();
        }
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log($"[race-ride] {name}: {_boot.session.courseId} {_boot.session.DistanceM:0} m, phase {(_race != null ? _race.CurrentPhase.ToString() : "-")}");
    }
}

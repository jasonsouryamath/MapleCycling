using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless, deterministic proof that the Hanakage encounter actually works.
///
/// Why this exists rather than "it compiled and the log said OK": the encounter is a two-minute
/// interaction between a state machine, a physics model and a player's watts. Nothing about that
/// can be judged from a render, and nothing about it can be judged by riding it once - the
/// question is not "did it run" but "does the gap respond to effort, does she ALWAYS escape, and
/// can the roll ever happen twice". So the harness builds a throwaway ride, drives it at a fixed
/// timestep with scripted rider effort, and asserts the properties the design actually promises.
///
/// It runs three rides:
///   A. STRONG   - the player pushes hard the whole way. Must catch her, must still lose her.
///   B. WEAK     - the player soft-pedals. Must never catch her, must still end up in the Journal
///                 as SPOTTED, because the handoff says no ride is wasted.
///   C. TRIGGER  - the player crosses the spawn trigger, reverses, and crosses it again. Must
///                 roll EXACTLY once.
///   D. PERSIST  - P3 rarity persistence. A rewound clock, a backwards seek and a destroyed-and-
///                 rebuilt encounter component must all fail to produce a second roll; only a
///                 genuine restart (a new RideSession.RunToken) re-arms it.
///
/// Nothing here touches the real scene or the player's real save: the host object is destroyed
/// and the Journal keys are restored on the way out.
/// </summary>
public static class HanakageEncounterSelfTest
{
    private const float Dt = 1f / 30f;
    private const float MaxSeconds = 600f;
    private const string HostName = "~HanakageSelfTest";

    [MenuItem("MapleRide/Hanakage/Run Encounter Self-Test")]
    public static void Run()
    {
        var log = new StringBuilder();
        bool pass = true;

        bool hadJournal = RiderJournal.IsDiscovered(RiderJournal.HanakageId);
        int hadBest = RiderJournal.BestOutcome(RiderJournal.HanakageId);
        RiderJournal.Forget(RiderJournal.HanakageId);

        try
        {
            pass &= RideStrong(log);
            RiderJournal.Forget(RiderJournal.HanakageId);
            pass &= RideWeak(log);
            RiderJournal.Forget(RiderJournal.HanakageId);
            pass &= RollOnce(log);
            RiderJournal.Forget(RiderJournal.HanakageId);
            pass &= RollPersistence(log);
        }
        finally
        {
            RiderJournal.Forget(RiderJournal.HanakageId);
            if (hadJournal) RiderJournal.Discover(RiderJournal.HanakageId);
            if (hadBest > 0) RiderJournal.RecordOutcome(RiderJournal.HanakageId, hadBest);
        }

        Debug.Log("[hanakage-test] " + (pass ? "ALL CHECKS PASSED" : "FAILURES PRESENT") +
                  "\n" + log);
    }

    // ------------------------------------------------------------------ rides

    private static bool RideStrong(StringBuilder log)
    {
        log.AppendLine("--- A. STRONG RIDER (effort +0.75) ---");
        var rig = Build(out var host);
        bool ok = true;
        try
        {
            var trace = Drive(rig, 0.75f, log);
            ok &= Check(log, "reached the Journal",
                        rig.enc.State == HanakageEncounter.Phase.Complete ||
                        rig.enc.State == HanakageEncounter.Phase.DiscoveryPending);
            ok &= Check(log, "got onto her wheel",
                        (int)rig.enc.Result >= (int)HanakageEncounter.Outcome.Caught,
                        "outcome " + rig.enc.Result);
            ok &= Check(log, "she finished ahead - the player never beats her",
                        rig.enc.GapM > 0f, $"final gap {rig.enc.GapM:0.0} m, " +
                        $"closest approach {trace.minGap:0.0} m");
            ok &= Check(log, "recorded in the Rider Journal",
                        RiderJournal.IsDiscovered(RiderJournal.HanakageId));
            ok &= Check(log, "encounter fitted inside the climb",
                        trace.escapeArc <= 700f, $"she went clear at arc {trace.escapeArc:0} m");
            log.AppendLine($"    outcome {rig.enc.Result}, closest gap {trace.minGap:0.0} m, " +
                           $"caught at arc {trace.caughtArc:0} m, clear at {trace.escapeArc:0} m, " +
                           $"encounter lasted {trace.encounterSeconds:0} s");
        }
        finally { Object.DestroyImmediate(host); }
        return ok;
    }

    private static bool RideWeak(StringBuilder log)
    {
        log.AppendLine("--- B. WEAK RIDER (effort -0.30: blows up on the 10 % wall) ---");
        var rig = Build(out var host);
        bool ok = true;
        try
        {
            var trace = Drive(rig, -0.30f, log);
            ok &= Check(log, "never got onto her wheel",
                        (int)rig.enc.Result < (int)HanakageEncounter.Outcome.Caught,
                        "outcome " + rig.enc.Result);
            ok &= Check(log, "still SPOTTED her - no ride is wasted",
                        rig.enc.Result == HanakageEncounter.Outcome.Spotted);
            ok &= Check(log, "still recorded in the Rider Journal - no ride is wasted",
                        RiderJournal.IsDiscovered(RiderJournal.HanakageId));
            ok &= Check(log, "she never stalled on the climb",
                        trace.minHerSpeed > 1.0f,
                        $"her slowest {trace.minHerSpeed * 3.6f:0.0} kph");
            log.AppendLine($"    outcome {rig.enc.Result}, closest gap {trace.minGap:0.0} m");
        }
        finally { Object.DestroyImmediate(host); }
        return ok;
    }

    private static bool RollOnce(StringBuilder log)
    {
        log.AppendLine("--- C. TRIGGER RE-CROSSING ---");
        var rig = Build(out var host);
        bool ok = true;
        try
        {
            rig.enc.config.spawnRateMode = HanakageEncounterConfig.SpawnRateMode.ManualOverride;
            rig.enc.config.spawnChance = 0f;             // roll must FAIL, then never repeat
            rig.enc.SetRollSeed(1234);
            float entry = rig.enc.config.zoneEntryM;

            rig.session.SeekTo(entry - 20f);
            rig.enc.Tick(Dt);
            ok &= Check(log, "dormant before the trigger", !rig.enc.RolledThisRun);

            rig.session.SeekTo(entry + 5f);
            rig.enc.Tick(Dt);
            ok &= Check(log, "rolled on the first crossing", rig.enc.RolledThisRun);
            ok &= Check(log, "roll declined at spawnChance 0",
                        rig.enc.State == HanakageEncounter.Phase.Declined);

            // Reverse back down the hill and come over the trigger again.
            for (int i = 0; i < 5; i++)
            {
                rig.session.SeekTo(entry - 20f); rig.enc.Tick(Dt);
                rig.session.SeekTo(entry + 5f);  rig.enc.Tick(Dt);
            }
            ok &= Check(log, "re-crossing cannot reroll",
                        rig.enc.State == HanakageEncounter.Phase.Declined);

            // A genuine restart, however, must re-arm.
            rig.session.ResetRide();
            rig.enc.Tick(Dt);
            ok &= Check(log, "a ride restart re-arms the roll", !rig.enc.RolledThisRun);
        }
        finally { Object.DestroyImmediate(host); }
        return ok;
    }

    /// <summary>
    /// P3 rarity persistence (handoff sections 3 / 18 / 19). At 1-in-30 the roll is the most
    /// valuable event in the encounter, so every way of asking for a second one must fail:
    /// rewinding the ride clock, seeking backwards, and - the one the in-memory flag could not
    /// cover - destroying and rebuilding the encounter component mid-run.
    /// </summary>
    private static bool RollPersistence(StringBuilder log)
    {
        log.AppendLine("--- D. ROLL PERSISTENCE (P3) ---");
        bool ok = true;
        HanakageRollLedger.Clear();

        // -- production rate is the handoff's 1-in-30, reachable by a named mode, not a literal.
        var probe = new HanakageEncounterConfig();
        probe.spawnRateMode = HanakageEncounterConfig.SpawnRateMode.Production;
        ok &= Check(log, "production mode yields 1/30",
                    Mathf.Abs(probe.EffectiveSpawnChance - 1f / 30f) < 1e-5f);
        probe.spawnRateMode = HanakageEncounterConfig.SpawnRateMode.Development;
        ok &= Check(log, "development mode yields 1.0",
                    Mathf.Abs(probe.EffectiveSpawnChance - 1f) < 1e-5f);

        var rig = Build(out var host);
        try
        {
            rig.enc.config.spawnRateMode = HanakageEncounterConfig.SpawnRateMode.ManualOverride;
            rig.enc.config.spawnChance = 0f;
            rig.enc.SetRollSeed(4242);
            float entry = rig.enc.config.zoneEntryM;

            rig.session.ResetRide();
            string token = rig.session.RunToken;
            ok &= Check(log, "a run has a token", !string.IsNullOrEmpty(token));

            rig.session.SeekTo(entry + 5f);
            rig.enc.Tick(Dt);
            ok &= Check(log, "rolled, and the ledger recorded it",
                        rig.enc.RolledThisRun && HanakageRollLedger.Count == 1);

            // A checkpoint restore rewinds the clock AND the distance. Under the old
            // clock-inference this read as a brand new run and handed out a free reroll.
            rig.session.SeekTo(entry - 40f);
            rig.enc.Tick(Dt);
            rig.session.SeekTo(entry + 5f);
            rig.enc.Tick(Dt);
            ok &= Check(log, "a backwards seek cannot reroll",
                        rig.enc.State == HanakageEncounter.Phase.Declined &&
                        string.Equals(rig.session.RunToken, token));

            // The case the in-memory flag could never cover: rebuild the component mid-run.
            Object.DestroyImmediate(rig.enc);
            var rebuilt = host.AddComponent<HanakageEncounter>();
            rebuilt.session = rig.session;
            rebuilt.devices = rig.devices;
            rebuilt.config.spawnRateMode = HanakageEncounterConfig.SpawnRateMode.ManualOverride;
            rebuilt.config.spawnChance = 1f;   // would spawn, if it were allowed to roll at all
            rebuilt.Resolve();
            rig.session.SeekTo(entry + 5f);
            rebuilt.Tick(Dt);
            ok &= Check(log, "a rebuilt encounter rehydrates as already rolled",
                        rebuilt.RolledThisRun);
            ok &= Check(log, "a rebuilt encounter does not spawn her on a spent run",
                        rebuilt.State == HanakageEncounter.Phase.Declined);
            ok &= Check(log, "the ledger did not grow", HanakageRollLedger.Count == 1);

            // A real restart mints a new token, which is the ONLY thing that re-arms the roll.
            rig.session.ResetRide();
            rebuilt.Tick(Dt);
            ok &= Check(log, "a restart mints a new token",
                        !string.Equals(rig.session.RunToken, token));
            ok &= Check(log, "a restart re-arms the roll", !rebuilt.RolledThisRun);

            rig.session.SeekTo(entry + 5f);
            rebuilt.Tick(Dt);
            ok &= Check(log, "the new run rolls once and is ledgered separately",
                        HanakageRollLedger.Count == 2);
        }
        finally
        {
            Object.DestroyImmediate(host);
            HanakageRollLedger.Clear();
        }
        return ok;
    }

    // ------------------------------------------------------------------ driver

    private struct Trace
    {
        public float minGap, caughtArc, escapeArc, encounterSeconds, minHerSpeed;
    }

    /// <summary>
    /// Rides from just below the trigger until the encounter completes, holding a fixed effort.
    /// Logs every phase change with the arc metre and gradient it happened at, which is the only
    /// way to see whether the encounter actually fits the hill it was tuned for.
    /// </summary>
    private static Trace Drive(Rig rig, float effort, StringBuilder log)
    {
        var t = new Trace { minGap = float.MaxValue, caughtArc = -1f, escapeArc = -1f, minHerSpeed = float.MaxValue };
        rig.session.SeekTo(rig.enc.config.zoneEntryM - 30f);
        rig.devices.EffortInput = effort;

        var last = rig.enc.State;
        float elapsed = 0f, started = -1f;

        for (float s = 0f; s < MaxSeconds; s += Dt)
        {
            rig.devices.EffortInput = effort;
            rig.session.Tick(Dt);
            rig.enc.Tick(Dt);
            elapsed += Dt;

            if (rig.enc.Active)
            {
                if (started < 0f) started = elapsed;
                t.minGap = Mathf.Min(t.minGap, rig.enc.GapM);
                t.minHerSpeed = Mathf.Min(t.minHerSpeed, rig.enc.HerSpeedMps);
            }

            if (rig.enc.State != last)
            {
                float arc = rig.session.DistanceM;
                float grade = rig.session.Course.GradeAt(arc, 8f) * 100f;
                log.AppendLine($"    {elapsed,6:0.0} s  {last,-16} -> {rig.enc.State,-16} " +
                               $"player {arc,5:0} m  {grade,5:0.0} %  gap {rig.enc.GapM,6:0.0} m  " +
                               $"her {rig.enc.HerWatts,3:0} W / {rig.enc.HerSpeedMps * 3.6f,4:0.0} kph  " +
                               $"player {rig.devices.Telemetry.Watts,3:0} W / {rig.session.SpeedKph,4:0.0} kph");
                if (rig.enc.State == HanakageEncounter.Phase.OnWheel && t.caughtArc < 0f)
                    t.caughtArc = arc;
                if (rig.enc.State == HanakageEncounter.Phase.DiscoveryPending)
                    t.escapeArc = rig.enc.HerArcM;
                last = rig.enc.State;
            }

            if (rig.enc.State == HanakageEncounter.Phase.Complete && started > 0f) break;
            if (rig.enc.State == HanakageEncounter.Phase.Declined) break;
        }
        t.encounterSeconds = started < 0f ? 0f : elapsed - started;
        return t;
    }

    // ------------------------------------------------------------------ rig

    private class Rig
    {
        public RideSession session;
        public DeviceManager devices;
        public HanakageEncounter enc;
    }

    /// <summary>
    /// A ride on the real baked course with no scene, no rider transform and no HUD. The
    /// encounter is given no <see cref="HanakageRider"/> at all - it must be able to run its
    /// whole state machine without one, which is also what proves the placement component holds
    /// no logic.
    /// </summary>
    private static Rig Build(out GameObject host)
    {
        host = new GameObject(HostName) { hideFlags = HideFlags.HideAndDontSave };
        var devices = host.AddComponent<DeviceManager>();
        devices.acceptKeyboardEffort = false;

        var session = host.AddComponent<RideSession>();
        session.devices = devices;
        session.autoLapsFromTarget = false;
        session.plannedLaps = 1;
        session.courseId = "pass_sprint";
        session.EnsureCourse();

        var enc = host.AddComponent<HanakageEncounter>();
        enc.session = session;
        enc.devices = devices;
        enc.hanakage = null;
        enc.Resolve();
        enc.config.spawnChance = 1f;
        enc.SetRollSeed(20260401);
        enc.ResetRun();

        return new Rig { session = session, devices = devices, enc = enc };
    }

    private static bool Check(StringBuilder log, string what, bool ok, string detail = null)
    {
        log.AppendLine($"    [{(ok ? "PASS" : "FAIL")}] {what}" +
                       (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")"));
        return ok;
    }
}

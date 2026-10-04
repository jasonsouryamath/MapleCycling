#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline Shunta Metro race balance check. Run via
/// <c>run_steps.ps1 "ShuntaMetroRaceSim.Run|claude_shuntarace.log|1"</c>.
/// <c>Tune</c> does a coordinate descent on archetype FTP so win rates even out and prints the values.
/// </summary>
public static class ShuntaMetroRaceSim
{
    const int Races = 200;
    const string JsonPath = "Assets/Resources/ShuntaMetro/shunta_metro_course.json";

    static ShuntaSimCourse BuildCourse(out ShuntaCourseData data)
    {
        data = JsonUtility.FromJson<ShuntaCourseData>(File.ReadAllText(JsonPath));
        var c = new ShuntaSimCourse { lengthM = data.distanceKm * 1000f };
        int n = (int)(c.lengthM / ShuntaSimCourse.StepM) + 2;
        c.gradePct = new float[n]; c.zone = new byte[n];
        for (int i = 0; i < n; i++)
        {
            float km = i * ShuntaSimCourse.StepM / 1000f;
            float dKm = 0.05f; // 50 m window
            c.gradePct[i] = (data.ElevationAtKm(km + dKm) - data.ElevationAtKm(km)) / (dKm * 1000f) * 100f;
            var z = data.ZoneAtKm(km);
            c.zone[i] = (byte)(z != null ? z.index : 1);
        }
        foreach (var z in data.zones)
            if (z.index >= 1 && z.index <= 12) { c.zoneGrip[z.index] = z.grip; c.zoneDraft[z.index] = z.draftMultiplier; }
        return c;
    }

    struct Stats { public int[] wins; public int[] podiums; public float[] winnerTimes; public float minT, maxT; public float meanWinner; }

    static Stats Simulate(ShuntaSimCourse course, Func<ShuntaArchetype, ShuntaRacerProfile> profileOf, int races, StringBuilder log)
    {
        var s = new Stats { wins = new int[6], podiums = new int[6], winnerTimes = new float[races], minT = 1e9f, maxT = 0f };
        for (int i = 0; i < races; i++)
        {
            int seed = 1000 + i;
            var field = ShuntaRaceSimCore.BuildField(seed, 4, profileOf);
            var res = ShuntaRaceSimCore.Run(course, field, seed);
            s.wins[(int)res[0].profile.archetype]++;
            for (int k = 0; k < 3; k++) s.podiums[(int)res[k].profile.archetype]++;
            s.winnerTimes[i] = res[0].finishS;
            s.minT = Mathf.Min(s.minT, res[0].finishS);
            s.maxT = Mathf.Max(s.maxT, res[res.Count - 1].finishS);
            s.meanWinner += res[0].finishS / races;
            if (log != null && i == 0)
            {
                log.AppendLine("Race 0 finish order (top 10 of 24):");
                for (int k = 0; k < 10; k++)
                    log.AppendLine($"  {k + 1,2}. {res[k].name,-12} {Fmt(res[k].finishS)}  (+{res[k].finishS - res[0].finishS:F1}s)");
                log.AppendLine($"  last: {res[res.Count - 1].name} {Fmt(res[res.Count - 1].finishS)}");
            }
        }
        return s;
    }

    static string Fmt(float sec) => $"{(int)(sec / 60)}:{sec % 60:00.0}";

    public static void Run()
    {
        var course = BuildCourse(out var data);
        var sb = new StringBuilder();
        sb.AppendLine($"[shunta-race] course {data.displayName} {data.distanceKm:F1} km, {data.ComputeGain():F0} m gain, {Races} races x 24 riders");

        // determinism: same seed, same order and times
        {
            var f1 = ShuntaRaceSimCore.BuildField(1000, 4, ShuntaRacerArchetypes.Nominal);
            var f2 = ShuntaRaceSimCore.BuildField(1000, 4, ShuntaRacerArchetypes.Nominal);
            var a = ShuntaRaceSimCore.Run(course, f1, 1000);
            var b = ShuntaRaceSimCore.Run(course, f2, 1000);
            for (int i = 0; i < a.Count; i++)
                if (a[i].name != b[i].name || Mathf.Abs(a[i].finishS - b[i].finishS) > 1e-3f)
                    Fail(sb, "simulation is not deterministic");
        }

        var st = Simulate(course, ShuntaRacerArchetypes.Nominal, Races, sb);
        sb.AppendLine("Archetype      wins  win%   podium-slots");
        bool ok = true;
        for (int a = 0; a < 6; a++)
        {
            float pct = 100f * st.wins[a] / Races;
            sb.AppendLine($"{(ShuntaArchetype)a,-13} {st.wins[a],4} {pct,5:F1}%  {st.podiums[a],4}");
            if (pct > 40f || pct < 2f) { ok = false; sb.AppendLine($"  FAIL: {(ShuntaArchetype)a} win rate {pct:F1}% outside 2..40%"); }
        }
        sb.AppendLine($"Winner time mean {Fmt(st.meanWinner)}, fastest winner {Fmt(st.minT)}, slowest finisher {Fmt(st.maxT)}");
        if (st.meanWinner < 50f * 60f || st.meanWinner > 60f * 60f || st.minT < 45f * 60f || st.maxT > 70f * 60f)
        { ok = false; sb.AppendLine("  FAIL: finish times outside the plausible 50-60 min band"); }

        // fair-play spot checks
        var tunnel = ShuntaFairPlay.Score(new ShuntaFairPlayContext { zoneIndex = 4, lateralDriftM = 1.2f, sideGapM = 9f, cutInGapBehindM = 9f, riskAppetite = 0.5f });
        var squeeze = ShuntaFairPlay.Score(new ShuntaFairPlayContext { zoneIndex = 9, lateralDriftM = 0f, sideGapM = 0.4f, cutInGapBehindM = 9f, riskAppetite = 0.5f });
        var chop = ShuntaFairPlay.Score(new ShuntaFairPlayContext { zoneIndex = 5, lateralSpeedMps = 1.1f, sideGapM = 9f, cutInGapBehindM = 0.8f, riskAppetite = 0.5f });
        var clean = ShuntaFairPlay.Score(new ShuntaFairPlayContext { zoneIndex = 4, lateralDriftM = 0.1f, sideGapM = 9f, cutInGapBehindM = 9f, riskAppetite = 0.5f });
        sb.AppendLine($"FairPlay tunnel-drift {tunnel.score:F2} [{string.Join("; ", tunnel.reasons)}], interchange-squeeze {squeeze.score:F2}, wheel-chop {chop.score:F2}, clean {clean.score:F2}");
        if (tunnel.Acceptable || squeeze.Acceptable || chop.Acceptable || !clean.Acceptable) { ok = false; sb.AppendLine("  FAIL: fair-play scorer thresholds"); }

        sb.AppendLine(ok ? "[shunta-race] RESULT PASS" : "[shunta-race] RESULT FAIL");
        Debug.Log(sb.ToString());
        if (!ok) throw new Exception("[shunta-race] balance/plausibility assertions failed");
    }

    /// <summary>Coordinate descent on FTP scale per archetype toward equal win share. Prints the scale factors.</summary>
    public static void Tune()
    {
        var course = BuildCourse(out _);
        var scale = new float[] { 1, 1, 1, 1, 1, 1 };
        var sb = new StringBuilder("[shunta-race] tune\n");
        for (int round = 0; round < 30; round++)
        {
            var captured = (float[])scale.Clone();
            Func<ShuntaArchetype, ShuntaRacerProfile> po = a =>
            {
                var p = ShuntaRacerArchetypes.Nominal(a);
                p.ftpW *= captured[(int)a];
                return p;
            };
            var st = Simulate(course, po, 100, null);
            sb.Append($"round {round}: ");
            for (int a = 0; a < 6; a++)
            {
                float share = st.wins[a] / 100f;
                sb.Append($"{(ShuntaArchetype)a}={st.wins[a]} ");
                scale[a] *= 1f + 0.10f * (1f / 6f - share);
            }
            sb.AppendLine($"| mean winner {Fmt(st.meanWinner)}");
        }
        for (int a = 0; a < 6; a++) sb.AppendLine($"scale {(ShuntaArchetype)a} = {scale[a]:F3}  -> ftp {ShuntaRacerArchetypes.Nominal((ShuntaArchetype)a).ftpW * scale[a]:F1}");
        Debug.Log(sb.ToString());
    }

    static void Fail(StringBuilder sb, string why)
    {
        sb.AppendLine("FAIL: " + why);
        Debug.Log(sb.ToString());
        throw new Exception("[shunta-race] " + why);
    }
}
#endif

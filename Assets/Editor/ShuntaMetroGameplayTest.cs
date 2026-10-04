using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Offline checks for the Shunta Metro Phase 3 gameplay data (modifiers, interchange fork, boost chevrons,
/// segment table/timer/board). Run via: run_steps.ps1 "ShuntaMetroGameplayTest.Run|claude_shunta_gameplay.log|1"
/// </summary>
public static class ShuntaMetroGameplayTest
{
    const float MaxStepKm = 0.005f;      // sample every 5 m
    const float MaxGripJumpPer5m = 0.02f;
    const float MaxDraftJumpPer5m = 0.02f;

    [MenuItem("MapleRide/Shunta Metro/Gameplay Test")]
    public static void Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Debug.Log($"[shunta] {(ok ? "PASS" : "FAIL")} gameplay: {what}"); if (!ok) fails++; }

        var c = ShuntaCourseData.Load();
        if (c == null) { Finish(1); return; }

        // ---- (1) zone modifiers ----
        var mods = new ShuntaZoneModifiers(c);
        float maxG = 0f, maxD = 0f, maxP = 0f, minGrip = 9f, maxDraft = 0f;
        var prev = mods.Sample(0f);
        for (float km = MaxStepKm; km <= c.distanceKm + 0.001f; km += MaxStepKm)
        {
            var m = mods.Sample(km);
            maxG = Mathf.Max(maxG, Mathf.Abs(m.gripMultiplier - prev.gripMultiplier));
            maxD = Mathf.Max(maxD, Mathf.Abs(m.draftMultiplier - prev.draftMultiplier));
            maxP = Mathf.Max(maxP, Mathf.Abs(m.packTightness - prev.packTightness));
            minGrip = Mathf.Min(minGrip, m.gripMultiplier); maxDraft = Mathf.Max(maxDraft, m.draftMultiplier);
            prev = m;
        }
        Check(maxG <= MaxGripJumpPer5m, $"max grip step per 5 m {maxG:F4} <= {MaxGripJumpPer5m}");
        Check(maxD <= MaxDraftJumpPer5m, $"max draft step per 5 m {maxD:F4} <= {MaxDraftJumpPer5m}");
        Check(maxP <= 0.04f, $"max pack-tightness step per 5 m {maxP:F4} <= 0.04");
        Check(Mathf.Abs(mods.Sample(7.4f).draftMultiplier - 1.35f) < 1e-4f, $"tunnel mid draft {mods.Sample(7.4f).draftMultiplier:F3} == 1.35");
        Check(Mathf.Abs(mods.Sample(14.9f).gripMultiplier - 0.78f) < 1e-4f, $"zone 7 mid grip {mods.Sample(14.9f).gripMultiplier:F3} == 0.78");
        Check(Mathf.Abs(mods.Sample(17.3f).gripMultiplier - 0.85f) < 1e-4f, $"zone 8 mid grip {mods.Sample(17.3f).gripMultiplier:F3} == 0.85");
        Check(minGrip >= 0.78f - 1e-4f && maxDraft <= 1.35f + 1e-4f, "no overshoot beyond zone extremes");
        float bnd = mods.Sample(13.8f).gripMultiplier;
        Check(bnd > 0.88f && bnd < 0.92f, $"grip at zone 6/7 boundary is the midpoint {bnd:F3}");

        // ---- route (headless) ----
        var go = new GameObject("shunta_gameplay_test") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        var rb = go.AddComponent<ShuntaRouteBuilder>();
        rb.buildRibbon = false; rb.buildGates = false;
        rb.Rebuild();

        // ---- (2) interchange fork ----
        var fork = ShuntaInterchangeFork.Default(c);
        Check(fork.ForkKm >= 18.4f && fork.MergeKm <= 21.0f && fork.MergeKm > fork.ForkKm, $"fork {fork.ForkKm}-{fork.MergeKm} km inside zone 9");
        var main = new List<Vector3>();
        for (int i = 0; i < rb.Km.Length; i++) if (rb.Km[i] > fork.ForkKm && rb.Km[i] < fork.MergeKm) main.Add(rb.Positions[i]);
        var a = fork.BuildBranch(rb.Positions, rb.Km, ShuntaForkBranch.Main); // Main => empty (not a branch)
        Check(a.Length == 0, "Main branch yields no generated polyline");
        var up = fork.BuildBranch(rb.Positions, rb.Km, ShuntaForkBranch.Upper);
        var lo = fork.BuildBranch(rb.Positions, rb.Km, ShuntaForkBranch.Lower);
        // main reference with identical endpoints
        var mainRef = new List<Vector3>(); mainRef.Add(up[0]); mainRef.AddRange(main); mainRef.Add(up[up.Length - 1]);
        var mainArr = mainRef.ToArray();
        float lm = ShuntaInterchangeFork.PolylineLength(mainArr), lu = ShuntaInterchangeFork.PolylineLength(up), ll = ShuntaInterchangeFork.PolylineLength(lo);
        Debug.Log($"[shunta] fork lengths: upper {lu:F0} m, main {lm:F0} m, lower {ll:F0} m");
        Check(up.Length == lo.Length && up.Length == mainArr.Length, $"branch sample counts match ({up.Length}/{lo.Length}/{mainArr.Length})");
        Check(lu < lm * 0.97f && ll > lm * 1.03f, "upper deck shorter and lower deck longer than main (>=3%)");
        Check(lu > lm * 0.75f && ll < lm * 1.35f, "length deltas stay sensible (upper >75%, lower <135% of main)");
        Check((up[0] - lo[0]).sqrMagnitude < 1e-4f && (up[up.Length - 1] - lo[lo.Length - 1]).sqrMagnitude < 1e-4f, "branches share fork and merge points");
        Check(Mathf.Abs(up[up.Length - 1].y - mainArr[mainArr.Length - 1].y) < 0.01f, "branches rejoin at main elevation");
        float gu = ShuntaInterchangeFork.MaxGradePercent(up), gl = ShuntaInterchangeFork.MaxGradePercent(lo), gm = ShuntaInterchangeFork.MaxGradePercent(mainArr);
        Debug.Log($"[shunta] fork max grade: upper {gu:F1}%, main {gm:F1}%, lower {gl:F1}%");
        Check(gu > gl && gu <= gm * 1.4f && gl <= gm, $"upper steeper than lower; upper <= 1.4x main grade, lower <= main");
        float asc(Vector3[] p) { float s = 0; for (int i = 1; i < p.Length; i++) s += Mathf.Max(0f, p[i].y - p[i - 1].y); return s; }
        Debug.Log($"[shunta] fork ascent: upper {asc(up):F1} m, main {asc(mainArr):F1} m, lower {asc(lo):F1} m");
        foreach (float w in new[] { 200f, 280f, 360f })
        {
            float tu = ShuntaInterchangeFork.EstimateSeconds(up, w), tl = ShuntaInterchangeFork.EstimateSeconds(lo, w, fork.Lower.speedFactor), tm = ShuntaInterchangeFork.EstimateSeconds(mainArr, w);
            Debug.Log($"[shunta] fork time @{w:F0} W: upper {tu:F1} s, main {tm:F1} s, lower {tl:F1} s");
            Check(Mathf.Abs(tu - tl) / Mathf.Max(tu, tl) <= 0.10f, $"@{w:F0} W branch times within 10% so the choice matters ({tu:F1}/{tl:F1})");
        }

        // ---- (3) chevrons ----
        var chev = ShuntaChevronLayout.Place(c);
        var chev2 = ShuntaChevronLayout.Place(c);
        bool same = chev.Length == chev2.Length;
        for (int i = 0; same && i < chev.Length; i++) same = chev[i].km == chev2[i].km && chev[i].lateralM == chev2[i].lateralM;
        Check(chev.Length == ShuntaChevronLayout.Count && same, $"{chev.Length} chevrons, deterministic");
        bool inZone = true, spaced = true, laneShift = true;
        for (int i = 0; i < chev.Length; i++)
        {
            inZone &= chev[i].km >= 11.6f && chev[i].km <= 13.8f && Mathf.Abs(chev[i].lateralM) < 2.5f;
            if (i > 0) { spaced &= chev[i].km - chev[i - 1].km >= ShuntaChevronLayout.MinSpacingKm; laneShift &= chev[i].lateralM != chev[i - 1].lateralM; }
        }
        Check(inZone, "all chevrons inside zone 6 and on the road");
        Check(spaced, $"chevron spacing >= {ShuntaChevronLayout.MinSpacingKm * 1000f:F0} m");
        Check(laneShift, "consecutive chevrons change lane");
        // ride straight through every chevron at 14 m/s steering onto each: cooldown (8 s) gates pickups
        var boost = new ShuntaBoostState(chev);
        float riderKm = 11.6f, tNow = 0f; int got = 0; float lastGot = -99f; bool cdOk = true, boostSeen = false;
        while (riderKm < 13.8f)
        {
            // steer to the nearest upcoming chevron's lane
            float lat = 0f; foreach (var ch in chev) if (ch.km >= riderKm - 0.005f) { lat = ch.lateralM; break; }
            int id = boost.Tick(riderKm, lat, 0.05f);
            if (id >= 0) { got++; cdOk &= tNow - lastGot >= boost.cooldownSeconds - 0.06f; lastGot = tNow; }
            if (boost.Boosting && boost.SpeedMultiplier > 1f) boostSeen = true;
            riderKm += 14f * 0.05f / 1000f; tNow += 0.05f;
        }
        Debug.Log($"[shunta] chevrons collected at 14 m/s with {boost.cooldownSeconds:F0} s cooldown: {got}/{chev.Length}");
        Check(got == chev.Length && cdOk && boostSeen, "all chevrons collectable at 14 m/s; boost raises speed multiplier");
        boost.Reset();
        int g0 = boost.Tick(chev[0].km, chev[0].lateralM, 0.05f);
        int g1 = boost.Tick(chev[1].km, chev[1].lateralM, 1f);          // 1 s later: still cooling down
        int g2 = boost.Tick(chev[1].km, chev[1].lateralM, 8f);          // cooldown over
        Check(g0 == 0 && g1 == -1 && g2 == 1 && boost.Boosting, "cooldown blocks a second pickup, then allows it");
        boost.Reset();
        Check(!boost.Boosting && boost.SpeedMultiplier == 1f && boost.Collected == 0, "reset clears boost state");

        // ---- (4) segments ----
        var segs = ShuntaSegmentTable.Build(c);
        Debug.Log("[shunta] segments: " + string.Join(", ", segs.ConvertAll(s => $"{s.id}[{s.startKm:F1}-{s.endKm:F1}]")));
        int climbs = segs.FindAll(s => s.kind == ShuntaSegmentKind.Climb).Count;
        Check(segs.Count == 6 && climbs == 4 && segs.FindAll(s => s.kind == ShuntaSegmentKind.Tunnel).Count == 1 && segs.FindAll(s => s.kind == ShuntaSegmentKind.Bridge).Count == 1,
              $"4 climbs + tunnel + bridge ({segs.Count} segments)");
        bool order = true, valid = true;
        for (int i = 0; i < segs.Count; i++)
        {
            valid &= segs[i].endKm > segs[i].startKm && segs[i].startKm >= 0f && segs[i].endKm <= c.distanceKm + 0.001f;
            if (i > 0) order &= segs[i].startKm >= segs[i - 1].startKm;
        }
        Check(order && valid, "segments ordered by start km, within the course");
        var ids = new HashSet<string>(); foreach (var s in segs) ids.Add(s.id);
        Check(ids.Count == segs.Count, "segment ids unique");

        var board = ShuntaSegmentBoard.Seeded(segs);
        var timer = new ShuntaSegmentTimer(segs);
        var results = new List<string>();
        timer.Finished += (d, sec) => { int rank = board.Submit(d.id, "You", sec); results.Add($"{d.id}:{sec:F0}s#{rank}"); };
        float km2 = 0f, t2 = 0f;
        while (km2 < c.distanceKm) { km2 += 9f * 0.1f / 1000f; t2 += 0.1f; timer.Tick(km2, t2); } // 32.4 kph, 10 Hz
        timer.Tick(c.distanceKm + 0.01f, t2 + 1f);
        Debug.Log("[shunta] segment results: " + string.Join(" ", results));
        Check(results.Count == segs.Count, $"timer finished all {segs.Count} segments ({results.Count})");
        var tunnel = segs.Find(s => s.kind == ShuntaSegmentKind.Tunnel);
        float expect = tunnel.LengthKm * 1000f / 9f;
        string tr = results.Find(r => r.StartsWith(tunnel.id));
        float pb = tr != null ? float.Parse(tr.Split(':')[1].Split('s')[0]) : -1f;
        Check(Mathf.Abs(pb - expect) < 1.5f, $"tunnel time {pb:F1}s ~ {expect:F1}s");
        board.Submit(tunnel.id, "You", 100f);
        Check(board.PersonalBest(tunnel.id) == 100f, "personal best tracked");
        var rec = board.Get(tunnel.id);
        bool sorted = true; for (int i = 1; i < rec.entries.Count; i++) sorted &= rec.entries[i].seconds >= rec.entries[i - 1].seconds;
        Check(sorted && rec.entries.Count <= ShuntaSegmentBoard.MaxEntries, "leaderboard sorted and capped");
        var round = ShuntaSegmentBoard.FromJson(board.ToJson());
        Check(round.records.Count == board.records.Count && round.PersonalBest(tunnel.id).HasValue, "board JSON round-trip");

        Object.DestroyImmediate(go);
        Finish(fails);
    }

    static void Finish(int fails)
    {
        if (fails > 0) { Debug.LogError($"[shunta] GAMEPLAY TEST FAILED: {fails} check(s)"); if (Application.isBatchMode) EditorApplication.Exit(2); }
        else Debug.Log("[shunta] GAMEPLAY TEST OK");
    }
}

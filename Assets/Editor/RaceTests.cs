using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Headless tests for the NPC race feature: the brief's formulas, the stamina/draft/bonk model,
/// save backward-compatibility, the roster's placement on REAL road (grades from the baked route
/// graph), the sprites, and a balance simulation of the brief's targets:
///   "A Lv 1 Kuro should be able to beat Lv 1-7, find Lv 12 a close fight, and need levels and
///    upgrades for the Elite riders."
/// The simulated Kuro is a GOOD player: for each match-up the best of 112 pacing policies
/// (sprint thresholds, kick distance, sit in the draft or not) over three seeds.
/// Run: run_steps.ps1 "RaceTests.Run|claude_race_tests.log|1". Prints "[race-tests] PASS n/n".
/// </summary>
public static class RaceTests
{
    private static int _pass, _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Debug.Log($"[race-tests] ok   {name} {detail}"); }
        else { _fail++; Debug.LogError($"[race-tests] FAIL {name} {detail}"); }
    }

    private static bool Near(float a, float b, float eps = 1e-3f) => Mathf.Abs(a - b) <= eps;

    public static void Run()
    {
        _pass = _fail = 0;
        Formulas();
        Model();
        Save();
        Roster();
        Art();
        Balance();
        Debug.Log($"[race-tests] {(_fail == 0 ? "PASS" : "FAIL")} {_pass}/{_pass + _fail}");
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(_fail == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------------ formulas

    private static void Formulas()
    {
        var k1 = RaceMath.KuroStats(1, 0, 0, 0);
        Check("Kuro Lv1 stats", Near(k1.Cruise, 11f) && Near(k1.Boost, 4f) && Near(k1.Stamina, 100f));
        var k = RaceMath.KuroStats(11, 2, 3, 4);
        Check("Kuro stat formula", Near(k.Cruise, 11f + 0.45f + 0.44f) && Near(k.Boost, 4f + 0.2f + 0.9f) && Near(k.Stamina, 100f + 12f + 48f),
              $"{k.Cruise:0.000}/{k.Boost:0.000}/{k.Stamina:0.0}");
        var n = RaceMath.NpcStats(31, RaceType.Standard, false);
        Check("NPC stat formula", Near(n.Cruise, 10f + 0.068f * 30f) && Near(n.Boost, 3.6f + 0.9f) && Near(n.Stamina, 136f));
        var e = RaceMath.NpcStats(31, RaceType.Elite, false);
        Check("Elite bonus", Near(e.Cruise - n.Cruise, 0.25f) && Near(e.Boost - n.Boost, 0.4f));
        Check("KOM specialist grade K", Near(RaceMath.NpcStats(18, RaceType.Climb, true).GradeK, 2.5f) && Near(n.GradeK, 3f));
        Check("grade factor clamps", Near(RaceMath.GradeFactor(0.5f, 3f), 0.42f) && Near(RaceMath.GradeFactor(-0.5f, 3f), 1.18f) &&
              Near(RaceMath.GradeFactor(0.05f, 3f), 0.85f));

        // coins: round5((40 + 14L + 0.45L^2) x typeMult), first win x1.5
        Check("coins Lv1 standard", RaceMath.WinCoins(1, RaceType.Standard, false) == 55, RaceMath.WinCoins(1, RaceType.Standard, false).ToString());
        Check("coins Lv1 first win", RaceMath.WinCoins(1, RaceType.Standard, true) == 80);
        Check("coins Lv60 elite", RaceMath.WinCoins(60, RaceType.Elite, false) == 5000, RaceMath.WinCoins(60, RaceType.Elite, false).ToString());
        Check("coins Lv12 TT", RaceMath.WinCoins(12, RaceType.TimeTrial, false) == 300, RaceMath.WinCoins(12, RaceType.TimeTrial, false).ToString());
        Check("coins are multiples of 5", RaceMath.WinCoins(7, RaceType.Sprint, true) % 5 == 0);

        // XP
        Check("win XP no bonus", RaceMath.WinXp(5, 10) == 90);
        Check("win XP +5%/level", RaceMath.WinXp(12, 2) == Mathf.RoundToInt(174f * 1.5f));
        Check("win XP bonus capped at +100%", RaceMath.WinXp(60, 1) == 750 * 2);
        Check("loss XP", RaceMath.LossXp(12) == 44);
        Check("XP to next", RaceMath.XpToNext(1) == 61 && RaceMath.XpToNext(10) == 360 && RaceMath.XpToNext(60) == 0,
              $"{RaceMath.XpToNext(1)}/{RaceMath.XpToNext(10)}");

        // insignias (17, boundaries from the brief)
        Check("17 insignias", RaceMath.InsigniaCount == 17 && RaceMath.InsigniaFromLevel.Length == 17);
        Check("insignia boundaries", RaceMath.InsigniaName(1) == "Sprout" && RaceMath.InsigniaName(2) == "Sprout" &&
              RaceMath.InsigniaName(3) == "Wooden Wheel" && RaceMath.InsigniaName(21) == "Twin Silver Cranks" &&
              RaceMath.InsigniaName(22) == "Gold Crank" && RaceMath.InsigniaName(54) == "Sakura Crown" &&
              RaceMath.InsigniaName(55) == "Azure Dragon" && RaceMath.InsigniaName(59) == "Azure Dragon" &&
              RaceMath.InsigniaName(60) == "Rising Sun");
        Check("insignia ranges", RaceMath.InsigniaRange(0) == "Lv 1-2" && RaceMath.InsigniaRange(16) == "Lv 60");

        // garage prices: round5(base x 1.65^level)
        Check("upgrade prices", RaceMath.UpgradeCost(150, 0) == 150 && RaceMath.UpgradeCost(150, 1) == 250 &&
              RaceMath.UpgradeCost(120, 7) == RaceMath.Round5(120f * Mathf.Pow(1.65f, 7)),
              $"{RaceMath.UpgradeCost(150, 1)} {RaceMath.UpgradeCost(120, 7)}");

        Check("race lengths", RaceMath.LengthM(RaceType.Standard) == 400f && RaceMath.LengthM(RaceType.Sprint) == 200f &&
              RaceMath.LengthM(RaceType.Climb) == 300f && RaceMath.LengthM(RaceType.TimeTrial) == 500f && RaceMath.LengthM(RaceType.Elite) == 600f);
        Check("kick distances", RaceMath.KickM(RaceType.Sprint) == 105f && RaceMath.KickM(RaceType.Climb) == 95f &&
              RaceMath.KickM(RaceType.Elite) == 150f && RaceMath.KickM(RaceType.Standard) == 120f);
        Check("odds bands", RaceMath.OddsLabel(0f) == "Easy win" && RaceMath.OddsLabel(10f) == "Even match" &&
              RaceMath.OddsLabel(18f) == "Tough" && RaceMath.OddsLabel(40f) == "Out of your league");
    }

    // ------------------------------------------------------------------ model

    private static void Model()
    {
        const float dt = 0.01f;
        var r = new RaceRider(RaceMath.KuroStats(1, 0, 0, 0)) { v = 11f };
        for (int i = 0; i < 100; i++) r.Step(dt, true, true, false, 0f);
        Check("sprint drains 24/s", Near(r.stamina, 76f, 0.05f), $"{r.stamina:0.00}");
        r.stamina = 50f;
        for (int i = 0; i < 100; i++) r.Step(dt, true, false, false, 0f);
        Check("pedal regen 9/s", Near(r.stamina, 59f, 0.05f), $"{r.stamina:0.00}");
        r.stamina = 50f;
        for (int i = 0; i < 100; i++) r.Step(dt, true, false, true, 0f);
        Check("draft regen 15/s", Near(r.stamina, 65f, 0.05f), $"{r.stamina:0.00}");
        r.stamina = 50f;
        for (int i = 0; i < 100; i++) r.Step(dt, false, false, false, 0f);
        Check("coast regen 16/s", Near(r.stamina, 66f, 0.05f), $"{r.stamina:0.00}");

        // bonk: 1.6 s, x0.8, no sprint
        r = new RaceRider(RaceMath.KuroStats(1, 0, 0, 0)) { v = 15f, stamina = 1f };
        r.Step(0.1f, true, true, false, 0f);
        Check("hitting 0 while sprinting bonks", r.Bonked && Near(r.bonkTimer, 1.6f));
        r.v = 11f * 0.8f;
        for (int i = 0; i < 100; i++) r.Step(dt, true, true, false, 0f);   // sprint held through the bonk
        Check("bonk: speed x0.8 and no sprint", !r.sprinting && Near(r.v, 11f * 0.8f, 0.05f), $"{r.v:0.00}");
        for (int i = 0; i < 65; i++) r.Step(dt, true, false, false, 0f);
        Check("bonk ends after 1.6 s", !r.Bonked);

        // acceleration rates
        float up = RaceMath.Accelerate(0f, 10f, true, 0.1f), coast = RaceMath.Accelerate(10f, 0f, false, 0.1f),
              down = RaceMath.Accelerate(12f, 10f, true, 0.1f);
        Check("accel rates 1.25 / 0.06 / 1.0", Near(up, 1.25f) && Near(coast, 10f - 0.06f) && Near(down, 11.8f));

        // draft window + TT exclusion
        Check("draft window", RaceMath.InDraft(3f, -1.2f, -1.1f, RaceType.Standard) && !RaceMath.InDraft(0.5f, -1.2f, -1.2f, RaceType.Standard) &&
              !RaceMath.InDraft(7.5f, -1.2f, -1.2f, RaceType.Standard) && !RaceMath.InDraft(3f, -2.1f, -1.2f, RaceType.Standard) &&
              !RaceMath.InDraft(3f, -1.2f, -1.2f, RaceType.TimeTrial));
        r = new RaceRider(RaceMath.KuroStats(1, 0, 0, 0));
        for (int i = 0; i < 3000; i++) r.Step(dt, true, false, true, 0f);
        Check("draft = +6% speed", Near(r.v, 11f * 1.06f, 0.02f), $"{r.v:0.000}");

        // AI rules
        var ai = new RaceRivalAi(RaceType.Standard, 400f);
        var me = new RaceRider(RaceMath.NpcStats(7, RaceType.Standard, false)) { x = 100f, stamina = 60f };
        var ku = new RaceRider(RaceMath.KuroStats(1, 0, 0, 0)) { x = 112f };
        Check("AI chases when Kuro > 9 m ahead and stamina > 45", ai.WantsSprint(me, ku));
        me.stamina = 21f;
        Check("AI stops sprinting under 22", !ai.WantsSprint(me, ku));
        me.stamina = 30f; ku.x = 100f;
        me.x = 290f;
        Check("AI kicks inside 120 m", ai.WantsSprint(me, ku));
        var elite = new RaceRivalAi(RaceType.Elite, 600f);
        var em = new RaceRider(RaceMath.NpcStats(52, RaceType.Elite, false)) { x = 50f };
        Check("Elite attacks above 92% stamina", elite.WantsSprint(em, new RaceRider(k1()) { x = 40f }));
        var tt = new RaceRivalAi(RaceType.TimeTrial, 500f);
        var gm = new RaceRider(RaceMath.NpcStats(12, RaceType.TimeTrial, false)) { x = 10f, stamina = 41f };
        bool s1 = tt.WantsSprint(gm, null);
        gm.stamina = 20f; bool s2 = tt.WantsSprint(gm, null);
        gm.stamina = 7f; bool s3 = tt.WantsSprint(gm, null);
        gm.stamina = 20f; bool s4 = tt.WantsSprint(gm, null);
        gm.x = 380f; bool s5 = tt.WantsSprint(gm, null);
        Check("TT ghost: >40 on, <8 off, empties tank in last 130 m", s1 && s2 && !s3 && !s4 && s5);
    }

    private static RaceMath.Stats k1() => RaceMath.KuroStats(1, 0, 0, 0);

    // ------------------------------------------------------------------ save

    private static void Save()
    {
        string progBackup = PlayerPrefs.HasKey(RiderProgress.Key) ? PlayerPrefs.GetString(RiderProgress.Key) : null;
        const string wardKey = "MapleRide.Wardrobe.v1";
        string wardBackup = PlayerPrefs.HasKey(wardKey) ? PlayerPrefs.GetString(wardKey) : null;
        try
        {
            PlayerPrefs.DeleteKey(RiderProgress.Key);
            RiderProgress.ReloadForTests();
            Check("no save = Lv 1, 0 XP, no upgrades", RiderProgress.Level == 1 && RiderProgress.Xp == 0 &&
                  RiderProgress.UpgradeLevel(BikeUpgrade.AeroWheels) == 0 && RiderProgress.RecordFor("hana") == null);

            // an older/partial JSON: missing fields default, short upgrade arrays are padded
            PlayerPrefs.SetString(RiderProgress.Key, "{\"level\":7,\"upgrades\":[2]}");
            RiderProgress.ReloadForTests();
            Check("partial JSON loads", RiderProgress.Level == 7 && RiderProgress.UpgradeLevel(BikeUpgrade.AeroWheels) == 2 &&
                  RiderProgress.UpgradeLevel(BikeUpgrade.OnigiriRations) == 0);
            PlayerPrefs.SetString(RiderProgress.Key, "not json {");
            RiderProgress.ReloadForTests();
            Check("corrupt JSON falls back to a fresh save", RiderProgress.Level == 1);

            PlayerPrefs.DeleteKey(RiderProgress.Key);
            RiderProgress.ReloadForTests();
            var res = RiderProgress.AddXp(61 + 80);   // Lv1 -> 2 needs 61, Lv2 -> 3 needs 85
            Check("level up across one level", res.levelBefore == 1 && res.levelAfter == 2 && RiderProgress.Xp == 80, $"xp {RiderProgress.Xp}");
            res = RiderProgress.AddXp(10);
            Check("level 3 = new insignia (Wooden Wheel)", res.LeveledUp && res.NewInsignia && RaceMath.InsigniaName(RiderProgress.Level) == "Wooden Wheel");
            res = RiderProgress.AddXp(10_000_000);
            Check("level caps at 60", RiderProgress.Level == 60 && RiderProgress.Xp == 0);

            bool first = RiderProgress.RecordRace("hana", true, 40f);
            bool second = RiderProgress.RecordRace("hana", true, 38.5f);
            RiderProgress.RecordRace("hana", false, 45f);
            var rec = RiderProgress.RecordFor("hana");
            Check("record: first win bonus once, W-L, best time", first && !second && rec.wins == 2 && rec.losses == 1 && Near(rec.bestTime, 38.5f));

            // the wardrobe save is untouched by progress; coins still work
            PlayerPrefs.SetString(wardKey, "{\"coins\":500}");
            PlayerWardrobe.ReloadForTests();
            int before = PlayerWardrobe.Coins;
            bool bought = RiderProgress.BuyUpgrade(BikeUpgrade.AeroWheels);
            int paid = before - PlayerWardrobe.Coins;
            Check("garage upgrade (QA free => pays 0)", bought && RiderProgress.UpgradeLevel(BikeUpgrade.AeroWheels) == 1 &&
                  paid == (PlayerWardrobe.QaFreePurchases ? 0 : 150), $"paid {paid}");
            for (int i = 0; i < 10; i++) RiderProgress.BuyUpgrade(BikeUpgrade.AeroWheels);
            Check("upgrades cap at 8", RiderProgress.UpgradeLevel(BikeUpgrade.AeroWheels) == 8 && RiderProgress.NextUpgradePrice(BikeUpgrade.AeroWheels) < 0);
            Check("TrySpend refuses an overdraft", !PlayerWardrobe.TrySpend(PlayerWardrobe.Coins + 1));
        }
        finally
        {
            if (progBackup == null) PlayerPrefs.DeleteKey(RiderProgress.Key); else PlayerPrefs.SetString(RiderProgress.Key, progBackup);
            if (wardBackup == null) PlayerPrefs.DeleteKey(wardKey); else PlayerPrefs.SetString(wardKey, wardBackup);
            PlayerPrefs.Save();
            RiderProgress.ReloadForTests();
            PlayerWardrobe.ReloadForTests();
        }
    }

    // ------------------------------------------------------------------ roster on real road

    private static void Roster()
    {
        var all = RaceRoster.All;
        var levels = new List<int>();
        var extras = new HashSet<string>(RaceRoster.RegionalExtras);
        int extrasFound = 0;
        foreach (var d in all)
        {
            if (extras.Contains(d.Id)) { extrasFound++; continue; }
            levels.Add(d.Level);
        }
        levels.Sort();
        Check("13 racers at the brief's level spread", string.Join(",", levels) == "1,3,7,12,18,24,31,38,45,48,52,56,60", string.Join(",", levels));
        Check($"{extras.Count} regional extra racer(s) in the roster ({string.Join(", ", RaceRoster.RegionalExtras)})",
              extrasFound == extras.Count, $"found {extrasFound}");
        var ids = new HashSet<string>();
        bool unique = true, eliteRule = true, colours = true;
        foreach (var d in all)
        {
            unique &= ids.Add(d.Id);
            eliteRule &= (d.Level >= 52) == (d.Type == RaceType.Elite);
            colours &= !string.IsNullOrEmpty(d.WinQuip) && !string.IsNullOrEmpty(d.LossQuip) && !string.IsNullOrEmpty(d.Challenge);
        }
        Check("unique ids, Lv 52+ exactly the Elites, quips present", unique && eliteRule && colours);

        var graph = RouteGraph.Load();
        if (graph == null) { Check("route graph loads", false); return; }
        foreach (var d in all)
        {
            var cd = graph.Course(d.CourseId);
            if (cd == null) { Check($"{d.Name}: course {d.CourseId} exists", false); continue; }
            var c = RouteCourse.Build(graph, cd);
            float start = d.SpotM + 6f, len = d.LengthM;
            bool fits = c.Closed || start + len + 60f <= c.Length;
            // mean grade over the race and over its halves
            float Mean(float a, float b)
            {
                float s = 0f; int n = 0;
                for (float m = a; m <= b; m += 10f) { s += c.GradeAt(m, 8f); n++; }
                return s / Mathf.Max(1, n);
            }
            float g = Mean(start, start + len), g1 = Mean(start, start + len * 0.5f), g2 = Mean(start + len * 0.5f, start + len);
            bool terrain;
            string want;
            switch (d.Type)
            {
                case RaceType.Climb: terrain = g > 0.04f; want = "uphill > 4 %"; break;
                case RaceType.Elite: terrain = g1 < 0.02f && g2 > 0.025f; want = "flat then climb"; break;
                default: terrain = Mathf.Abs(g) < 0.02f; want = "flat (|g| < 2 %)"; break;
            }
            Check($"{d.Name} Lv {d.Level} {RaceMath.DisplayName(d.Type)} on real road ({d.CourseId} {d.SpotM:0} m)",
                  fits && terrain, $"{want}: mean {g * 100f:0.0} % (halves {g1 * 100f:0.0} / {g2 * 100f:0.0} %), fits {fits}");
        }
    }

    // ------------------------------------------------------------------ sprites + eyelid data

    private static void Art()
    {
        bool ok = true;
        var missing = new List<string>();
        foreach (RaceType t in System.Enum.GetValues(typeof(RaceType)))
            if (Resources.Load<Texture2D>(RaceArt.Dir + "race_icon_" + RaceMath.IconName(t)) == null) missing.Add(t.ToString());
        for (int i = 0; i < 17; i++)
            if (Resources.Load<Texture2D>($"{RaceArt.Dir}insignia_{i:00}") == null) missing.Add("insignia " + i);
        foreach (var n in new[] { "race_glow", "race_ring", "race_beam", "race_sparkle", "race_banner_start",
                                  "race_banner_finish", "race_board_200", "race_board_100" })
            if (Resources.Load<Texture2D>(RaceArt.Dir + n) == null) missing.Add(n);
        ok = missing.Count == 0;
        Check("race sprites present in Resources/Race", ok, string.Join(", ", missing));
        var eyes = Resources.Load<TextAsset>(RiderBlink.DataResource);
        Check("eyelid data baked (EyelidBake.Run)", eyes != null && eyes.text.Contains("\"eyes\""),
              eyes == null ? "missing" : $"{eyes.text.Length} chars");
        foreach (var d in RaceRoster.All)
            if (Resources.Load<Texture2D>(NpcGreeting.PortraitResourceDir + d.Name) == null)
                Check($"{d.Name} has a portrait", false);
    }

    // ------------------------------------------------------------------ balance

    private struct Policy { public float hi, lo, kick; public bool sit; }

    private static readonly List<Policy> Policies = BuildPolicies();

    private static List<Policy> BuildPolicies()
    {
        var l = new List<Policy>();
        foreach (float hi in new[] { 101f, 95f, 70f, 45f })
            foreach (float lo in new[] { 5f, 22f })
                foreach (float kick in new[] { 0f, 60f, 100f, 140f, 180f, 250f, 400f })
                    foreach (bool sit in new[] { false, true })
                        l.Add(new Policy { hi = hi, lo = lo, kick = kick, sit = sit });
        return l;
    }

    /// <summary>Margin in seconds (positive = Kuro wins) for the best policy.</summary>
    private static float BestMargin(RacerDef d, RouteCourse c, int level, int up)
    {
        float best = float.MinValue;
        foreach (var p in Policies)
        {
            float sum = 0f;
            for (int seed = 0; seed < 3; seed++) sum += Simulate(d, c, level, up, p, seed);
            best = Mathf.Max(best, sum / 3f);
        }
        return best;
    }

    private static float Simulate(RacerDef d, RouteCourse c, int level, int up, Policy p, int seed)
    {
        var rnd = new System.Random(seed * 7919 + d.Level);
        float L = d.LengthM, start = d.SpotM + 6f;
        var k = new RaceRider(RaceMath.KuroStats(level, up, up, up)) { lane = RaceDirector.Lines[1] };
        var n = new RaceRider(RaceMath.NpcStats(d.Level, d.Type, d.ClimbSpecialist)) { lane = RaceDirector.Lines[1] };
        var ai = new RaceRivalAi(d.Type, L);
        if (d.Type == RaceType.Sprint)
        {
            k.v = k.stats.Cruise * RaceMath.RollingStartFraction;
            n.v = n.stats.Cruise * RaceMath.RollingStartFraction;
        }
        k.v += RaceMath.PerfectStartBonusMps;
        const float dt = 1f / 60f;
        float t = 0f, wobT = 0f;
        bool spr = false;
        while ((!k.Finished || !n.Finished) && t < 300f)
        {
            wobT -= dt;
            if (wobT <= 0f) { wobT = 1.4f; n.wobble = 1f + ((float)rnd.NextDouble() * 0.04f - 0.02f); }
            float gap = k.x - n.x;
            float toGo = L - k.x;
            bool ks;
            if (toGo <= p.kick) ks = k.stamina > 0.5f;
            else if (p.sit && d.Type != RaceType.TimeTrial && gap < 0f && gap > -7f) ks = false;
            else if (!spr && k.stamina > p.hi) ks = true;
            else if (spr && k.stamina < p.lo) ks = false;
            else ks = spr;
            spr = ks;
            bool dk = RaceMath.InDraft(n.x - k.x, k.lane, n.lane, d.Type);
            bool dn = RaceMath.InDraft(k.x - n.x, n.lane, k.lane, d.Type);
            if (!k.Finished) k.Step(dt, true, ks, dk, c.GradeAt(start + k.x, 8f));
            if (!n.Finished) n.Step(dt, true, ai.WantsSprint(n, k), dn, c.GradeAt(start + n.x, 8f));
            t += dt;
            if (!k.Finished) { k.x += k.v * dt; if (k.x >= L) k.finishTime = t - (k.x - L) / Mathf.Max(0.1f, k.v); }
            if (!n.Finished) { n.x += n.v * dt; if (n.x >= L) n.finishTime = t - (n.x - L) / Mathf.Max(0.1f, n.v); }
        }
        return n.finishTime - k.finishTime;
    }

    private static void Balance()
    {
        var graph = RouteGraph.Load();
        if (graph == null) return;
        var courses = new Dictionary<string, RouteCourse>();
        RouteCourse C(RacerDef d)
        {
            if (!courses.TryGetValue(d.CourseId, out var c)) courses[d.CourseId] = c = RouteCourse.Build(graph, graph.Course(d.CourseId));
            return c;
        }
        var lines = new List<string>();
        float M(string id, int lv, int up)
        {
            var d = RaceRoster.Find(id);
            float m = BestMargin(d, C(d), lv, up);
            lines.Add($"Kuro Lv{lv}+{up} vs {d.Name} Lv{d.Level}: {m:+0.00;-0.00} s");
            return m;
        }
        float hana = M("hana", 1, 0), kenji = M("kenji", 1, 0), mei = M("mei", 1, 0), taro = M("taro", 1, 0), hiro = M("hiro", 1, 0);
        Check("Lv 1 Kuro beats Lv 1, 3 and 7", hana > 0f && kenji > 0f && mei > 0f, $"{hana:0.00} / {kenji:0.00} / {mei:0.00} s");
        Check("Lv 12 is a close fight for Lv 1 Kuro (within 2.5 s)", Mathf.Abs(taro) < 2.5f, $"{taro:+0.00;-0.00} s");
        Check("Lv 1 Kuro cannot beat Lv 18", hiro < 0f, $"{hiro:0.00} s");
        float sota = M("sota", 1, 0), yuki = M("yuki", 20, 3);
        Check("Elites are out of reach for a Lv 1 (and Lv 20 +3) Kuro", sota < -5f && yuki < 0f, $"{sota:0.00} / {yuki:0.00} s");
        float maxed = M("kohaku", 60, 8), levelsOnly = M("kohaku", 60, 0), upgradesOnly = M("kohaku", 1, 8);
        M("kohaku", 40, 4);
        Check("the Lv 60 Elite needs levels AND upgrades", maxed > 0f && levelsOnly < 0f && upgradesOnly < 0f,
              $"Lv60+8 {maxed:+0.00;-0.00}, Lv60+0 {levelsOnly:+0.00;-0.00}, Lv1+8 {upgradesOnly:+0.00;-0.00} s");
        foreach (var l in lines) Debug.Log("[race-tests] balance " + l);
    }
}

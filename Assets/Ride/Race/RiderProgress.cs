using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bike upgrades sold in the Riders screen's Garage tab.</summary>
public enum BikeUpgrade { AeroWheels = 0, CeramicDrivetrain = 1, OnigiriRations = 2 }

/// <summary>
/// Kuro's racing progression: level, XP, bike upgrades and the per-rival record (wins, losses,
/// best time, first-win bonus claimed). Saved as JSON in PlayerPrefs under its OWN key, beside
/// the wardrobe save; coins are NOT duplicated here, they live in the existing MapleCoins
/// wallet (<see cref="PlayerWardrobe"/>).
///
/// Backward compatible by construction: a save with no progress key (every save made before
/// this feature) loads as Lv 1 / 0 XP / no upgrades / no records, and JsonUtility leaves any
/// field missing from an older JSON at its default, so later fields can be added freely.
/// </summary>
public static class RiderProgress
{
    public const string Key = "MapleRide.Progress.v1";

    [Serializable]
    public class Record
    {
        public string id = "";
        public int wins;
        public int losses;
        /// <summary>Best finishing time in seconds, -1 = never finished.</summary>
        public float bestTime = -1f;
        public bool firstWinClaimed;
    }

    [Serializable]
    private class Save
    {
        public int level = 1;
        public int xp;
        public int[] upgrades = new int[3];
        public List<Record> records = new List<Record>();
    }

    private static Save _s;
    public static event Action Changed;

    private static Save S
    {
        get
        {
            if (_s != null) return _s;
            try { _s = JsonUtility.FromJson<Save>(PlayerPrefs.GetString(Key, "")); }
            catch { _s = null; }
            if (_s == null) _s = new Save();
            _s.level = Mathf.Clamp(_s.level, 1, RaceMath.MaxLevel);
            if (_s.upgrades == null || _s.upgrades.Length != 3)
            {
                var up = new int[3];
                if (_s.upgrades != null) Array.Copy(_s.upgrades, up, Mathf.Min(3, _s.upgrades.Length));
                _s.upgrades = up;
            }
            if (_s.records == null) _s.records = new List<Record>();
            return _s;
        }
    }

    public static int Level => S.level;
    public static int Xp => S.xp;
    public static int XpToNext => RaceMath.XpToNext(S.level);
    public static int UpgradeLevel(BikeUpgrade u) => S.upgrades[(int)u];

    public static RaceMath.Stats KuroStats =>
        RaceMath.KuroStats(S.level, S.upgrades[0], S.upgrades[1], S.upgrades[2]);

    public static float EffectiveLevel =>
        RaceMath.EffectiveKuroLevel(S.level, S.upgrades[0], S.upgrades[1], S.upgrades[2]);

    public static Record RecordFor(string racerId)
    {
        foreach (var r in S.records) if (r.id == racerId) return r;
        return null;
    }

    /// <summary>Result of <see cref="AddXp"/>: how far Kuro went and whether the insignia changed.</summary>
    public struct XpResult
    {
        public int gained, levelBefore, levelAfter;
        public bool LeveledUp => levelAfter > levelBefore;
        public bool NewInsignia =>
            RaceMath.InsigniaIndex(levelAfter) != RaceMath.InsigniaIndex(levelBefore);
    }

    public static XpResult AddXp(int amount)
    {
        var res = new XpResult { gained = Mathf.Max(0, amount), levelBefore = S.level };
        if (S.level < RaceMath.MaxLevel) S.xp += res.gained;
        while (S.level < RaceMath.MaxLevel && S.xp >= RaceMath.XpToNext(S.level))
        {
            S.xp -= RaceMath.XpToNext(S.level);
            S.level++;
        }
        if (S.level >= RaceMath.MaxLevel) S.xp = 0;
        res.levelAfter = S.level;
        Persist();
        return res;
    }

    /// <summary>Records a race. Returns true when this win is the FIRST against that rider (the
    /// x1.5 coin bonus); the flag is claimed as part of the same call.</summary>
    public static bool RecordRace(string racerId, bool won, float timeSeconds)
    {
        var r = RecordFor(racerId);
        if (r == null) { r = new Record { id = racerId }; S.records.Add(r); }
        bool first = false;
        if (won)
        {
            r.wins++;
            if (!r.firstWinClaimed) { r.firstWinClaimed = true; first = true; }
        }
        else r.losses++;
        if (timeSeconds > 0f && (r.bestTime < 0f || timeSeconds < r.bestTime)) r.bestTime = timeSeconds;
        Persist();
        return first;
    }

    // ------------------------------------------------------------------ garage

    public static int UpgradeBaseCost(BikeUpgrade u) => u == BikeUpgrade.OnigiriRations ? 120 : 150;

    public static string UpgradeName(BikeUpgrade u)
    {
        switch (u)
        {
            case BikeUpgrade.CeramicDrivetrain: return "Ceramic Drivetrain";
            case BikeUpgrade.OnigiriRations: return "Onigiri Rations";
            default: return "Aero Wheels";
        }
    }

    public static string UpgradeEffect(BikeUpgrade u)
    {
        switch (u)
        {
            case BikeUpgrade.CeramicDrivetrain: return "+0.3 m/s sprint boost per level";
            case BikeUpgrade.OnigiriRations: return "+12 stamina per level";
            default: return "+0.22 m/s cruise speed per level";
        }
    }

    /// <summary>List price of the next level, or -1 at max.</summary>
    public static int NextUpgradePrice(BikeUpgrade u)
    {
        int lv = UpgradeLevel(u);
        return lv >= RaceMath.MaxUpgradeLevel ? -1 : RaceMath.UpgradeCost(UpgradeBaseCost(u), lv);
    }

    /// <summary>What the player actually pays (0 while the wardrobe's QA free-purchase switch is on,
    /// the user's call on 2026-09-26: "still in QA, everything free").</summary>
    public static int UpgradePriceToPay(BikeUpgrade u)
    {
        int list = NextUpgradePrice(u);
        return list < 0 ? -1 : (PlayerWardrobe.QaFreePurchases ? 0 : list);
    }

    public static bool BuyUpgrade(BikeUpgrade u)
    {
        int pay = UpgradePriceToPay(u);
        if (pay < 0) return false;
        if (!PlayerWardrobe.TrySpend(pay)) return false;
        S.upgrades[(int)u]++;
        Persist();
        return true;
    }

    // ------------------------------------------------------------------ persistence

    private static void Persist()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(S));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>Test-only: drop the cached save so the next read reloads PlayerPrefs.</summary>
    public static void ReloadForTests() { _s = null; }
}

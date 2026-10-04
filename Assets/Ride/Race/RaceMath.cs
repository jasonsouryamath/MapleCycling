using UnityEngine;

/// <summary>The five NPC race formats. Ordinal order is the display order.</summary>
public enum RaceType { Standard = 0, Sprint = 1, Climb = 2, TimeTrial = 3, Elite = 4 }

/// <summary>How a parked racer stands beside their bike while waiting for a challenger.</summary>
public enum RacerStance { Relaxed = 0, ArmsCrossed = 1, HandsOnHips = 2 }

/// <summary>
/// Every number of the NPC race feature, as pure functions (no scene, no clock), so the
/// headless tests (RaceTests.Run) exercise exactly what the game runs.
///
/// Source: the user's race brief (2026-09-25). The stat, stamina, grade, acceleration, reward
/// and XP formulas are the brief's own and are reproduced verbatim; the odds-label thresholds
/// are PROVISIONAL tuning of mine, calibrated against the balance simulation described on
/// <see cref="OddsLabel"/>.
/// </summary>
public static class RaceMath
{
    public const int MaxLevel = 60;
    public const int MaxUpgradeLevel = 8;

    // ------------------------------------------------------------------ race types

    public static float LengthM(RaceType t)
    {
        switch (t)
        {
            case RaceType.Sprint: return 200f;
            case RaceType.Climb: return 300f;
            case RaceType.TimeTrial: return 500f;
            case RaceType.Elite: return 600f;
            default: return 400f;
        }
    }

    /// <summary>Distance to go at which the rival launches the finishing sprint.</summary>
    public static float KickM(RaceType t)
    {
        switch (t)
        {
            case RaceType.Sprint: return 105f;
            case RaceType.Climb: return 95f;
            case RaceType.Elite: return 150f;
            default: return 120f;
        }
    }

    public static float TypeMultiplier(RaceType t)
    {
        switch (t)
        {
            case RaceType.Sprint: return 0.85f;
            case RaceType.Climb: return 1.25f;
            case RaceType.TimeTrial: return 1.1f;
            case RaceType.Elite: return 2.0f;
            default: return 1.0f;
        }
    }

    /// <summary>Text in the marker's pill.</summary>
    public static string Label(RaceType t)
    {
        switch (t)
        {
            case RaceType.Sprint: return "SPRINT";
            case RaceType.Climb: return "KOM";
            case RaceType.TimeTrial: return "TIME TRIAL";
            case RaceType.Elite: return "ELITE";
            default: return "RACE";
        }
    }

    public static string DisplayName(RaceType t)
    {
        switch (t)
        {
            case RaceType.Sprint: return "Sprint Challenge";
            case RaceType.Climb: return "Climb Challenge";
            case RaceType.TimeTrial: return "Time Trial";
            case RaceType.Elite: return "Elite Race";
            default: return "Standard Race";
        }
    }

    /// <summary>Resources/Race/race_icon_&lt;name&gt;.png (tools/ui/make_race_icons.py).</summary>
    public static string IconName(RaceType t)
    {
        switch (t)
        {
            case RaceType.Sprint: return "sprint";
            case RaceType.Climb: return "kom";
            case RaceType.TimeTrial: return "tt";
            case RaceType.Elite: return "elite";
            default: return "race";
        }
    }

    public static Color TypeColor(RaceType t)
    {
        switch (t)
        {
            case RaceType.Sprint: return Hex(0xff4757);
            case RaceType.Climb: return Hex(0x3ddc74);
            case RaceType.TimeTrial: return Hex(0xa95cff);
            case RaceType.Elite: return Hex(0xffb42e);
            default: return Hex(0x3fa2ff);
        }
    }

    public static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

    // ------------------------------------------------------------------ rider stats (m/s)

    public struct Stats
    {
        public float Cruise;
        public float Boost;
        public float Stamina;
        /// <summary>Grade sensitivity: target x clamp(1 - grade x K, 0.42, 1.18).</summary>
        public float GradeK;
    }

    public static Stats KuroStats(int level, int wheels, int drivetrain, int nutrition)
    {
        level = Mathf.Clamp(level, 1, MaxLevel);
        return new Stats
        {
            Cruise = 11.0f + 0.045f * (level - 1) + 0.22f * wheels,
            Boost = 4.0f + 0.02f * (level - 1) + 0.3f * drivetrain,
            Stamina = 100f + 1.2f * (level - 1) + 12f * nutrition,
            GradeK = 3.0f,
        };
    }

    public static Stats NpcStats(int level, RaceType type, bool climbSpecialist)
    {
        level = Mathf.Clamp(level, 1, MaxLevel);
        var s = new Stats
        {
            Cruise = 10.0f + 0.068f * (level - 1),
            Boost = 3.6f + 0.03f * (level - 1),
            Stamina = 100f + 1.2f * (level - 1),
            GradeK = climbSpecialist ? 2.5f : 3.0f,
        };
        if (type == RaceType.Elite) { s.Cruise += 0.25f; s.Boost += 0.4f; }
        return s;
    }

    public static float GradeFactor(float grade, float k) => Mathf.Clamp(1f - grade * k, 0.42f, 1.18f);

    // ------------------------------------------------------------------ stamina + motion

    public const float SprintDrainPerS = 24f;
    public const float RegenPedalPerS = 9f;
    public const float RegenDraftPerS = 15f;
    public const float RegenCoastPerS = 16f;
    public const float BonkSeconds = 1.6f;
    public const float BonkSpeedScale = 0.8f;
    public const float DraftMinM = 0.8f;
    public const float DraftMaxM = 7f;
    public const float DraftBonus = 1.06f;
    /// <summary>Riders are on the same line when their lateral offsets are this close.</summary>
    public const float SameLineM = 0.45f;
    public const float PerfectStartWindowS = 0.3f;
    public const float PerfectStartBonusMps = 3f;
    public const float AccelRateUp = 1.25f;
    public const float AccelRatePedalDown = 1.0f;
    public const float AccelRateCoast = 0.06f;
    /// <summary>PROVISIONAL: the Sprint Challenge's rolling start speed, as a fraction of each
    /// rider's own cruise. The brief asks for a rolling start without a number.</summary>
    public const float RollingStartFraction = 0.75f;

    /// <summary>v += (target - v) x min(1, rate x dt).</summary>
    public static float Accelerate(float v, float target, bool pedaling, float dt)
    {
        float rate = !pedaling ? AccelRateCoast : (target > v ? AccelRateUp : AccelRatePedalDown);
        return v + (target - v) * Mathf.Min(1f, rate * dt);
    }

    public static bool InDraft(float gapBehindM, float laneA, float laneB, RaceType type) =>
        type != RaceType.TimeTrial &&
        gapBehindM >= DraftMinM && gapBehindM <= DraftMaxM &&
        Mathf.Abs(laneA - laneB) <= SameLineM;

    // ------------------------------------------------------------------ rewards

    public static int Round5(float x) => Mathf.RoundToInt(x / 5f) * 5;

    public static int WinCoins(int npcLevel, RaceType type, bool firstWin)
    {
        float L = npcLevel;
        float c = (40f + 14f * L + 0.45f * L * L) * TypeMultiplier(type);
        return Round5(firstWin ? c * 1.5f : c);
    }

    public static int WinXp(int npcLevel, int kuroLevel)
    {
        float bonus = Mathf.Min(1f, 0.05f * Mathf.Max(0, npcLevel - kuroLevel));
        return Mathf.RoundToInt((30f + 12f * npcLevel) * (1f + bonus));
    }

    public static int LossXp(int npcLevel) => 8 + 3 * npcLevel;

    /// <summary>XP needed to go from level K to K + 1. 0 at the level cap.</summary>
    public static int XpToNext(int level) =>
        level >= MaxLevel ? 0 : Mathf.RoundToInt(40f + 20f * level + 1.2f * level * level);

    public static int UpgradeCost(int baseCost, int currentLevel) =>
        Round5(baseCost * Mathf.Pow(1.65f, currentLevel));

    // ------------------------------------------------------------------ odds

    /// <summary>
    /// "Easy win" / "Even match" / "Tough" / "Out of your league", from the level gap.
    ///
    /// PROVISIONAL thresholds. A balance simulation of these exact formulas (Kuro played well:
    /// the best of 112 pacing policies per match-up) found each upgrade level is worth about
    /// 2.5 rider levels, and an Elite's +0.25 cruise / +0.4 boost about 12. So the gap is taken
    /// between EFFECTIVE levels. On that scale a Lv 1 Kuro wins by ~2 s at a gap of 6, by ~1 s
    /// at 11 and just loses at 17, which is where the bands below break.
    /// </summary>
    public static string OddsLabel(float effectiveGap)
    {
        if (effectiveGap <= 6f) return "Easy win";
        if (effectiveGap <= 14f) return "Even match";
        if (effectiveGap <= 22f) return "Tough";
        return "Out of your league";
    }

    public static Color OddsColor(float effectiveGap)
    {
        if (effectiveGap <= 6f) return Hex(0x3ddc74);
        if (effectiveGap <= 14f) return Hex(0x3fa2ff);
        if (effectiveGap <= 22f) return Hex(0xffb42e);
        return Hex(0xff4757);
    }

    public static float EffectiveKuroLevel(int level, int wheels, int drivetrain, int nutrition) =>
        level + 2.5f * (wheels + drivetrain + nutrition);

    public static float EffectiveNpcLevel(int level, RaceType type) =>
        level + (type == RaceType.Elite ? 12f : 0f);

    // ------------------------------------------------------------------ rank insignias

    public static readonly string[] InsigniaNames =
    {
        "Sprout", "Wooden Wheel", "Twin Wooden Wheels", "Bronze Wheel", "Twin Bronze Wheels",
        "Silver Crank", "Twin Silver Cranks", "Gold Crank", "Twin Gold Cranks", "Jade Wing",
        "Twin Jade Wings", "Violet Wing", "Twin Violet Wings", "Crimson Maple", "Sakura Crown",
        "Azure Dragon", "Rising Sun",
    };

    /// <summary>First level of each insignia (same order as <see cref="InsigniaNames"/>).</summary>
    public static readonly int[] InsigniaFromLevel =
        { 1, 3, 6, 9, 12, 15, 18, 22, 26, 30, 34, 38, 42, 46, 50, 55, 60 };

    public static int InsigniaCount => InsigniaNames.Length;

    public static int InsigniaIndex(int level)
    {
        int idx = 0;
        for (int i = 0; i < InsigniaFromLevel.Length; i++)
            if (level >= InsigniaFromLevel[i]) idx = i;
        return idx;
    }

    public static string InsigniaName(int level) => InsigniaNames[InsigniaIndex(level)];

    /// <summary>"Lv 22-25" / "Lv 60".</summary>
    public static string InsigniaRange(int index)
    {
        int from = InsigniaFromLevel[index];
        int to = index + 1 < InsigniaFromLevel.Length ? InsigniaFromLevel[index + 1] - 1 : MaxLevel;
        return from == to ? $"Lv {from}" : $"Lv {from}-{to}";
    }

    public static string FormatTime(float seconds)
    {
        if (seconds < 0f || float.IsNaN(seconds)) return "--:--.--";
        int m = Mathf.FloorToInt(seconds / 60f);
        float s = seconds - m * 60f;
        return $"{m}:{s:00.00}";
    }
}

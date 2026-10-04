using System;
using UnityEngine;

/// <summary>
/// The Rider Journal: the permanent record of who the player has met.
///
/// Design handoff section 6 makes the Journal the thing an encounter is ultimately FOR - the
/// ride is the loop, the Journal is the keepsake. It is deliberately the smallest possible
/// persistence layer right now (one PlayerPrefs key per rider) because the shape of the full
/// Journal UI, its rarity tiers and its relationship track are still open (section 12); what
/// matters for this milestone is that a discovery SURVIVES the session, so the "first sighting"
/// framing can never be shown twice.
///
/// Everything here is keyed by a stable rider id, never by display name, so renaming a rider
/// later cannot silently wipe a player's record.
/// </summary>
public static class RiderJournal
{
    /// <summary>PROVISIONAL: PlayerPrefs namespace. A real save file replaces this.</summary>
    public const string KeyPrefix = "mapleride.journal.";

    public const string HanakageId = "hanakage";

    /// <summary>
    /// PROVISIONAL / ILLUSTRATIVE. Section 13 shows the card reading "Riders Discovered 23 / 30",
    /// which is example copy, not a requirement - there is no agreed Sakura Pass roster yet
    /// (section 12 leaves it open). The denominator therefore lives here as a named constant so
    /// the card can show a real fraction today without anyone mistaking 30 for a design decision.
    /// </summary>
    public const int SakuraPassRosterTarget = 30;

    /// <summary>
    /// The riders that actually exist on Sakura Pass right now. Grows as NPCs are built; the
    /// card's numerator counts only these, so it can never claim a discovery that has no rider
    /// behind it.
    /// </summary>
    public static readonly string[] SakuraPassRoster = { "coral", "yuki", HanakageId };

    /// <summary>How many Sakura Pass riders the player has met.</summary>
    public static int SakuraPassDiscoveredCount()
    {
        int n = 0;
        foreach (var id in SakuraPassRoster) if (IsDiscovered(id)) n++;
        return n;
    }

    /// <summary>Raised when a rider is recorded for the FIRST time. The HUD card listens.</summary>
    public static event Action<string> Discovered;

    public static bool IsDiscovered(string riderId) =>
        !string.IsNullOrEmpty(riderId) && PlayerPrefs.GetInt(KeyPrefix + riderId, 0) == 1;

    /// <summary>Records a rider. Returns true only on the transition from unknown to known.</summary>
    public static bool Discover(string riderId)
    {
        if (string.IsNullOrEmpty(riderId)) return false;
        if (IsDiscovered(riderId)) return false;
        PlayerPrefs.SetInt(KeyPrefix + riderId, 1);
        PlayerPrefs.Save();
        Discovered?.Invoke(riderId);
        return true;
    }

    /// <summary>
    /// Best result the player has ever posted against this rider, stored as the integer value of
    /// <see cref="HanakageEncounter.Outcome"/>. Kept separate from discovery so a later ride can
    /// improve the record without re-triggering the discovery card.
    /// </summary>
    public static int BestOutcome(string riderId) =>
        PlayerPrefs.GetInt(KeyPrefix + riderId + ".best", 0);

    public static void RecordOutcome(string riderId, int outcome)
    {
        if (string.IsNullOrEmpty(riderId)) return;
        if (outcome <= BestOutcome(riderId)) return;
        PlayerPrefs.SetInt(KeyPrefix + riderId + ".best", outcome);
        PlayerPrefs.Save();
    }

    /// <summary>Test-only. The self-test harness must not leave a real discovery behind.</summary>
    public static void Forget(string riderId)
    {
        if (string.IsNullOrEmpty(riderId)) return;
        PlayerPrefs.DeleteKey(KeyPrefix + riderId);
        PlayerPrefs.DeleteKey(KeyPrefix + riderId + ".best");
        PlayerPrefs.Save();
    }
}

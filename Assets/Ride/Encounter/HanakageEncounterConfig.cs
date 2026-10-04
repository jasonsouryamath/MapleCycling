using UnityEngine;

/// <summary>
/// Every number the Hanakage encounter runs on, in one serialized place.
///
/// EVERYTHING HERE IS PROVISIONAL illustrative tuning. The design handoff is explicit that its
/// thresholds, distances, drop rates and durations are placeholders, so none of them appear as a
/// literal inside the state machine - the state machine reads this object and nothing else. The
/// handoff's own required-parameter list (section 18) is reproduced field for field below so a
/// designer can find each one by the name the doc used.
///
/// The one number that is NOT free: the arc window. Sakura Pass's climb is 529 m long and the
/// road descends after that, and the handoff protects the gradient profile ("never fake a
/// gradient"). So the phases are laid out against the REAL gradient ramp - 5.9 % at 150 m,
/// 8 % at 200 m, 10.4 % at 275-400 m, easing to 4 % at the crest - and the encounter is
/// compressed to fit inside it rather than the road being bent to fit the encounter. See
/// <see cref="zoneEntryM"/>.
/// </summary>
[System.Serializable]
public class HanakageEncounterConfig
{
    // ------------------------------------------------------------------ rarity
    /// <summary>
    /// Which rarity the build ships at. A named mode rather than a raw float, because the
    /// handoff's 1-in-30 is the PRODUCTION target and 1.0 is a development convenience - and the
    /// difference between the two must never come down to somebody remembering to edit a number
    /// in a serialized scene before a build.
    /// </summary>
    public enum SpawnRateMode
    {
        /// <summary>She appears every eligible run. For building and QA only.</summary>
        Development,
        /// <summary>The handoff's shipping rarity.</summary>
        Production,
        /// <summary>Use <see cref="spawnChance"/> verbatim. For tuning experiments.</summary>
        ManualOverride,
    }

    /// <summary>PROVISIONAL (handoff section 18): the production sighting rate, 1 run in 30.</summary>
    public const float ProductionSpawnChance = 1f / 30f;
    /// <summary>Development rate: always.</summary>
    public const float DevelopmentSpawnChance = 1f;

    [Header("Rarity")]
    [Tooltip("Development = she always appears (current default). Production = the handoff's " +
             "1-in-30. ManualOverride = use spawnChance below verbatim.")]
    public SpawnRateMode spawnRateMode = SpawnRateMode.Development;

    /// <summary>The chance the single per-run roll actually uses. The state machine reads THIS.</summary>
    public float EffectiveSpawnChance
    {
        get
        {
            switch (spawnRateMode)
            {
                case SpawnRateMode.Production: return ProductionSpawnChance;
                case SpawnRateMode.ManualOverride: return Mathf.Clamp01(spawnChance);
                default: return DevelopmentSpawnChance;
            }
        }
    }

    // ------------------------------------------------------------------ availability
    [Header("Availability")]
    [Tooltip("Master switch. Off = she can never roll.")]
    public bool encounterEnabled = true;

    [Tooltip("PROVISIONAL: chance the single per-run roll succeeds. Only consulted when " +
             "spawnRateMode is ManualOverride; the shipping value lives in ProductionSpawnChance.")]
    [Range(0f, 1f)] public float spawnChance = 1f;

    [Tooltip("Course ids she can appear on. Sakura Pass climb courses only - the arc metres " +
             "below are measured on the 'pass' segment, which both of these start with.")]
    public string[] eligibleCourseIds = { "pass_sprint", "sakura_circuit" };

    [Tooltip("Let her roll again on later laps of a closed course. Off by default: the handoff " +
             "wants a rare sighting, not a lap-timer.")]
    public bool allowRepeatPerLap = false;

    [Tooltip("Allow her to roll again once she is already in the Journal. On, because the " +
             "handoff treats the challenge as replayable - only the DISCOVERY card is one-shot.")]
    public bool allowRepeatAfterDiscovery = true;

    // ------------------------------------------------------------------ where
    [Header("Encounter window (arc metres on the pass climb)")]
    [Tooltip("PROVISIONAL: arc metre at which the roll is made. 150 m is ~5.9 % - the foot of " +
             "the steep section, which is the gradient the handoff opens the encounter on.")]
    public float zoneEntryM = 150f;

    [Tooltip("Metres past zoneEntryM in which the roll can still happen, for a player who " +
             "loaded in mid-climb. Outside this the run is simply not eligible.")]
    public float zoneArmWindowM = 40f;

    [Tooltip("PROVISIONAL: how far ahead of the player she is placed when she spawns. The " +
             "handoff says 100-150 m; 110 m keeps her inside the open sightline that runs up " +
             "to the tunnel mouth.")]
    public float spawnAheadM = 110f;

    [Tooltip("PROVISIONAL: arc metre of the summit crest, where she is out of sight for good. " +
             "This is the REAL crest of the baked route (RouteGraph.climbLength = 529 m), which " +
             "is why the escape needs no invented occluder.")]
    public float escapeArcM = 529f;

    [Tooltip("Metres past escapeArcM after which a still-running encounter gives up and cleans " +
             "itself away, so a stalled state can never strand her on the descent.")]
    public float abortPastZoneM = 120f;

    // ------------------------------------------------------------------ her effort
    [Header("Her effort, as a fraction of the PLAYER's FTP (provisional)")]
    [Tooltip("Soft-pedalling while the player first sees her. Below any plausible FTP so the " +
             "gap always closes if the player simply keeps riding.")]
    public float observationFtpPercent = 0.60f;

    [Tooltip("She picks up as the player comes across, so the catch costs something.")]
    public float approachFtpPercent = 0.71f;

    [Tooltip("Phase 1 of the challenge: just under threshold. Holdable on a good day.")]
    public float wheelFtpPercent = 0.86f;

    [Tooltip("Phase 2, the attack: over threshold. Costs the player matches to answer.")]
    public float attackFtpPercent = 1.00f;

    [Tooltip("The final hairpin. The last thing the player is asked for.")]
    public float hairpinFtpPercent = 1.08f;

    [Tooltip("The escape. She is a legendary climber: this is not meant to be answerable.")]
    public float escapeFtpPercent = 1.30f;

    [Tooltip("Seconds her power takes to blend between phases. Instant steps read as teleporting.")]
    public float effortBlendSeconds = 2.5f;

    [Tooltip("Floor on her road speed, m/s. The physics model has a genuine stall point - below " +
             "a certain power a rider on a 10 % wall cannot keep moving at all - and the first " +
             "self-test run duly parked her at 0.0 kph halfway up the climb. A legendary climber " +
             "does not stall; a soft-pedalling one just goes slowly. 1.7 m/s is ~6 kph.")]
    public float minSpeedMps = 1.7f;

    [Header("Her bike + body (provisional)")]
    [Tooltip("A climber's build. Lighter than the player's 68 kg, which is WHY she gains on a " +
             "10 % gradient at similar watts - the advantage is physical, not scripted.")]
    public float riderMassKg = 56f;
    public float bikeMassKg = 7.0f;
    [Tooltip("Small, tucked, on the hoods.")]
    public float cdA = 0.295f;

    // ------------------------------------------------------------------ phase shape
    [Header("Phase shape (provisional)")]
    [Tooltip("Gap at which the observation phase hands over to the approach.")]
    public float approachGapM = 30f;
    [Tooltip("Observation cannot outlast this even if the player is crawling.")]
    public float observationMaxSeconds = 150f;

    [Tooltip("Gap she will never let fall below, metres. Two things at once: a legendary rider " +
             "does not get passed on her own climb, and - visible in the first capture - a gap " +
             "under a bike length puts two sculpts through each other. 2.4 m is a wheel plus a " +
             "little air.")]
    public float minGapM = 2.4f;

    [Tooltip("Gap that counts as being ON HER WHEEL. The handoff's 'draft' distance.")]
    public float wheelGapM = 8f;
    [Tooltip("Slack before an on-wheel player is considered to have come off it.")]
    public float wheelHoldGapM = 13f;
    [Tooltip("Seconds the player must hold the wheel before she looks back.")]
    public float wheelHoldSeconds = 12f;

    [Tooltip("Seconds the attack lasts.")]
    public float attackSeconds = 22f;
    [Tooltip("Gap inside which the player is still 'answering' the attack.")]
    public float attackHoldGapM = 16f;
    [Tooltip("Fraction of the attack the player must stay inside attackHoldGapM to count as HELD.")]
    [Range(0f, 1f)] public float attackHoldFraction = 0.6f;

    [Tooltip("Seconds the final hairpin lasts.")]
    public float hairpinSeconds = 12f;
    [Tooltip("Fraction of the hairpin the player must hold to count as IMPRESSED.")]
    [Range(0f, 1f)] public float hairpinHoldFraction = 0.55f;

    [Tooltip("Gap at which a dropped player loses her for good and she starts escaping.")]
    public float droppedGapM = 55f;
    [Tooltip("Seconds beyond droppedGapM before the drop is committed - one bad pitch of road " +
             "must not end the encounter.")]
    public float droppedGraceSeconds = 6f;

    [Tooltip("Gap at which she is gone from the world during the escape.")]
    public float escapeDespawnGapM = 85f;

    // ------------------------------------------------------------------ reveal
    [Header("Reveal (provisional)")]
    [Tooltip("Allow her two spoken lines. The handoff forbids ALL dialogue before the look-back.")]
    public bool dialogueEnabled = true;
    [Tooltip("Her line when she looks back and finds the player still there.")]
    public string lookBackLine = "Still here?";
    [Tooltip("Her line over the top of the final hairpin.")]
    public string hairpinLine = "Good.";
    [Tooltip("Seconds after she is out of sight before the Journal card appears. The handoff " +
             "wants a beat of nothing first, so the player feels the loss before the reward.")]
    public float discoveryDelaySeconds = 22f;
    [Tooltip("Seconds the Journal card stays up.")]
    public float discoveryCardSeconds = 9f;
    [Tooltip("Seconds a spoken line stays up.")]
    public float dialogueSeconds = 4f;

    // ------------------------------------------------------------------ safety
    [Header("Safety")]
    [Tooltip("Seconds the player may be stopped before the encounter releases them. Nobody " +
             "should be held hostage by a rival because they had to answer the door.")]
    public float stallAbortSeconds = 25f;
    [Tooltip("Speed below which the player counts as stopped, m/s.")]
    public float stallSpeedMps = 0.6f;

    /// <summary>True if this course id can host the encounter.</summary>
    public bool IsEligibleCourse(string courseId)
    {
        if (string.IsNullOrEmpty(courseId) || eligibleCourseIds == null) return false;
        foreach (var id in eligibleCourseIds)
            if (string.Equals(id, courseId, System.StringComparison.Ordinal)) return true;
        return false;
    }
}

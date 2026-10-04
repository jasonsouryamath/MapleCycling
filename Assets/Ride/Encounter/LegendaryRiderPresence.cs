using UnityEngine;

/// <summary>
/// The REGION-AGNOSTIC half of a legendary rider: presence, first sighting, and escape.
///
/// WHY THIS EXISTS RATHER THAN A SECOND HANAKAGE PORT.
/// <see cref="HanakageEncounter"/> is a 25 KB scripted four-phase duel - approach, tug-of-war,
/// decisive pitch, resolution - wired to its own HUD, camera director, audio director, roll
/// ledger and performance sampler. That whole stack is Sakura's, and porting it wholesale for
/// Taka's Hyoga would mean duplicating eight files before a single new idea was expressed.
///
/// But a legendary is really two separable things:
///
///   1. PRESENCE - the rider exists on the road, is unmistakably bigger and faster than the
///      cast, speaks in their own voice, is recorded in the Journal the first time you see
///      them, and REFUSES TO BE CAUGHT: they lift and vanish up the road. This is what makes a
///      legendary feel legendary even before any duel logic runs.
///
///   2. THE DUEL - the scripted, phased, HUD-driven challenge. Content, not identity.
///
/// This component is (1), written once so every region's legendary can have it: Hyoga on the
/// North Wall today, and whoever rules the next eight regions after. (2) remains Hanakage-only
/// and is explicitly deferred - see the class remarks on <see cref="escapeSpeedMultiplier"/> for
/// the hook a later duel port would attach to.
///
/// Everything below is PROVISIONAL tuning. None of these numbers come from the design docs as
/// requirements; they are the smallest set that makes an escape read as an escape.
/// </summary>
[DisallowMultipleComponent]
public class LegendaryRiderPresence : MonoBehaviour
{
    [Header("Identity")]

    /// <summary>
    /// Stable Journal id, lowercase ASCII. NEVER the display name: <see cref="RiderJournal"/> is
    /// keyed by id precisely so renaming a rider cannot wipe a player's record, and Hyoga's
    /// display name carries a macron that has no business in a PlayerPrefs key.
    /// </summary>
    public string riderId = "hyoga";

    /// <summary>Shown in the discovery log line. May carry diacritics; the id may not.</summary>
    public string displayName = "Hyoga";

    /// <summary>Flavour for the discovery line, e.g. "The North Wall".</summary>
    public string title = "The North Wall";

    /// <summary>
    /// Region id from <see cref="RegionCatalog"/>. Presence only arms while the player is
    /// actually riding this region: all regions share ONE scene, so without this gate a
    /// legendary parked at Taka's 21.6 km mark would happily discover himself the moment a
    /// Sakura ride happened to put the player within 40 m of his world-space position.
    /// </summary>
    public string regionId = RegionCatalog.TakaMountains;

    [Header("First sighting")]

    /// <summary>
    /// PROVISIONAL, metres. How close the player must come before the sighting is recorded.
    /// Deliberately generous: a legendary you had to brush shoulders with to "meet" would be
    /// missed by anyone riding a clean line, and a missed first sighting cannot be retried
    /// (<see cref="RiderJournal.Discover"/> only ever fires on the unknown -> known transition).
    /// </summary>
    public float sightingRadiusM = 42f;

    [Header("Escape")]

    /// <summary>
    /// PROVISIONAL. Peak multiplier on the rider's authored speed while escaping.
    ///
    /// THIS IS THE DUEL HOOK. A later port of the Hanakage stack would suppress the escape while
    /// a challenge is live and let the duel drive the pace instead; until then the escape IS the
    /// encounter, and 1.9x is tuned so that a player who is already at threshold watches him go
    /// rather than reeling him back - which is the whole characterisation of "no one holds my
    /// wheel on the North Wall".
    /// </summary>
    public float escapeSpeedMultiplier = 1.9f;

    /// <summary>PROVISIONAL, seconds. Ramp in - an instant teleport to 1.9x reads as a bug.</summary>
    public float escapeRampSeconds = 2.5f;

    /// <summary>PROVISIONAL, seconds. How long he holds the lift before easing back.</summary>
    public float escapeHoldSeconds = 14f;

    /// <summary>PROVISIONAL, seconds. Ease back down, so he does not snap to cruising pace.</summary>
    public float escapeReleaseSeconds = 6f;

    // ---------------------------------------------------------------- runtime

    NPCCyclist _cyclist;
    RideSession _session;
    Transform _player;
    float _baseSpeed;
    bool _discovered;
    float _escapeT = -1f;   // seconds since the escape began; negative = not escaping

    /// <summary>True once this rider has been seen in THIS play session (not the save).</summary>
    public bool Sighted => _discovered;

    void Awake()
    {
        _cyclist = GetComponent<NPCCyclist>();
        // Captured in Awake, BEFORE any escape can touch it. Reading it lazily at escape time
        // would capture an already-boosted value if the component were ever re-enabled mid-lift,
        // and the rider would ratchet faster on every sighting.
        if (_cyclist != null) _baseSpeed = _cyclist.speed;
    }

    void Start()
    {
        _session = FindFirstObjectByType<RideSession>();
        var pose = FindFirstObjectByType<KuroRidePose>();
        if (pose != null) _player = pose.transform;
        else if (_session != null) _player = _session.transform;
    }

    void Update()
    {
        if (_cyclist == null) return;

        // Region gate. See regionId: one scene, many regions, many kilometres apart.
        if (_session != null && !string.IsNullOrEmpty(regionId))
        {
            var region = RegionCatalog.Find(regionId);
            if (region != null && !string.IsNullOrEmpty(region.BuiltCourseId) &&
                _session.courseId != region.BuiltCourseId)
            {
                // Not this region's ride. Make sure we are not leaving him boosted.
                if (_escapeT >= 0f) { _escapeT = -1f; _cyclist.speed = _baseSpeed; }
                return;
            }
        }

        if (_player != null && _escapeT < 0f && !_discovered)
        {
            var d = Vector3.Distance(_player.position, transform.position);
            if (d <= sightingRadiusM)
            {
                _discovered = true;
                _escapeT = 0f;

                // Discover() is idempotent across sessions and returns true only on the first
                // ever sighting, so the "first sighting" framing can never be shown twice. The
                // escape runs either way - he does not become catchable once you have met him.
                bool firstEver = RiderJournal.Discover(riderId);
                Debug.Log($"[legendary] {displayName} \"{title}\" sighted at {d:0.0} m " +
                          $"(first ever: {firstEver}) - journal id '{riderId}'.");
            }
        }

        if (_escapeT >= 0f)
        {
            _escapeT += Time.deltaTime;
            float mul;
            if (_escapeT < escapeRampSeconds)
                mul = Mathf.Lerp(1f, escapeSpeedMultiplier,
                                 Mathf.SmoothStep(0f, 1f, _escapeT / Mathf.Max(0.01f, escapeRampSeconds)));
            else if (_escapeT < escapeRampSeconds + escapeHoldSeconds)
                mul = escapeSpeedMultiplier;
            else
            {
                float t = (_escapeT - escapeRampSeconds - escapeHoldSeconds) /
                          Mathf.Max(0.01f, escapeReleaseSeconds);
                mul = Mathf.Lerp(escapeSpeedMultiplier, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
                if (t >= 1f) { _escapeT = -1f; mul = 1f; }
            }
            _cyclist.speed = _baseSpeed * mul;
        }
    }

    void OnDisable()
    {
        // Leaving him boosted in the serialized scene would quietly bake 1.9x into the asset the
        // next time anything saved it - exactly the class of bug that PaceScale re-application
        // exists to undo.
        if (_cyclist != null) _cyclist.speed = _baseSpeed;
        _escapeT = -1f;
    }
}

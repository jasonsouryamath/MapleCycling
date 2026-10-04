using UnityEngine;

/// <summary>
/// Handoff section 16 - audio direction for the Hanakage encounter.
///
/// STATUS: this is the MIX STATE MACHINE only. The project currently ships no music or ambience
/// assets, so there is nothing for it to fade. It is written and wired anyway because the state
/// machine is the part that encodes the design - the part that is easy to get wrong later, when
/// somebody is holding a finished motif and reaches for the obvious "play boss music on the
/// attack" - and because the levels it computes can be asserted by the self-test today.
///
/// Assign the four <see cref="AudioSource"/> slots when audio exists and it starts working with
/// no further changes. Until then <see cref="AmbienceLevel"/> and friends are the deliverable,
/// and anything about how it SOUNDS is Not Verified.
///
/// The design, restated, because it is unusually specific:
///   - before sighting: normal Sakura Pass ambience, music untouched;
///   - approaching: do NOT change the music. Lift the mechanical layer instead - drivetrain,
///     tyres, wind, breathing. The player should notice the effort, not a cue;
///   - on her wheel: music pulls back to near silence. The tension is the absence;
///   - "Still here?": a brief hole around the line, so it lands in space;
///   - attack: HER motif enters. Elegant, fast, controlled, climbing. Explicitly not villainous
///     and explicitly not generic boss music - she is not a boss and the encounter must never
///     frame her as one;
///   - escape: the motif recedes rather than stops;
///   - journal reveal: a short blossom/discovery sting, separate from the motif.
/// </summary>
[DefaultExecutionOrder(85)]
public class HanakageAudioDirector : MonoBehaviour
{
    [Header("Wiring")]
    public HanakageEncounter encounter;
    public HanakageRider rider;

    [Header("Optional sources - leave empty until audio content exists")]
    /// <summary>Route music / ambience bed that is already playing before she appears.</summary>
    public AudioSource ambience;
    /// <summary>Drivetrain + tyres + wind + breathing layer.</summary>
    public AudioSource mechanical;
    /// <summary>Hanakage's motif. Should be a loop that can be entered and receded from.</summary>
    public AudioSource motif;
    /// <summary>One-shot blossom discovery sting for the Rider Journal card.</summary>
    public AudioSource discoverySting;

    // ---------------------------------------------------------------- tuning (ALL PROVISIONAL)
    [Header("Target levels (PROVISIONAL)")]
    public float ambienceNormal = 1.00f;
    /// <summary>Approach: music is NOT cut, only eased, per the design's explicit instruction.</summary>
    public float ambienceApproach = 0.75f;
    /// <summary>On the wheel: "minimal music. The silence should create tension."</summary>
    public float ambienceOnWheel = 0.18f;
    public float ambienceAttack = 0.10f;
    public float ambienceAfter = 0.85f;

    public float mechanicalNormal = 0.55f;
    /// <summary>The approach cue is carried entirely by this layer.</summary>
    public float mechanicalApproach = 0.95f;
    public float mechanicalOnWheel = 1.00f;
    public float mechanicalAttack = 1.00f;

    public float motifOff = 0.0f;
    public float motifAttack = 0.85f;
    public float motifHairpin = 0.95f;
    /// <summary>Escape recedes; it does not stop.</summary>
    public float motifEscape = 0.35f;

    [Header("Dialogue space (PROVISIONAL)")]
    /// <summary>
    /// Section 16: "brief pause/space around dialogue". Everything ducks by this factor while a
    /// line is on screen, so "Still here?" and "Good." land in a hole rather than over a motif.
    /// </summary>
    [Range(0f, 1f)] public float dialogueDuck = 0.45f;
    public float dialogueDuckSeconds = 0.35f;

    [Header("Fades (PROVISIONAL)")]
    /// <summary>Slow enough that no transition reads as a cue firing.</summary>
    public float fadeSeconds = 3.0f;
    /// <summary>The motif is the one thing allowed to arrive with intent.</summary>
    public float motifInSeconds = 1.2f;

    // ---------------------------------------------------------------- live state
    public float AmbienceLevel { get; private set; }
    public float MechanicalLevel { get; private set; }
    public float MotifLevel { get; private set; }
    public bool StingFired { get; private set; }

    private float _duck = 1f;
    private bool _sawCard;

    private void OnEnable()
    {
        AmbienceLevel = ambienceNormal;
        MechanicalLevel = mechanicalNormal;
        MotifLevel = motifOff;
        StingFired = false;
        _sawCard = false;
        if (encounter != null) encounter.PhaseChanged += OnPhase;
    }

    private void OnDisable()
    {
        if (encounter != null) encounter.PhaseChanged -= OnPhase;
    }

    private void OnPhase(HanakageEncounter.Phase p)
    {
        // Nothing to do per-phase beyond logging: the levels are derived in Tick so that a
        // harness can seek straight to a phase and still get the right mix.
        if (p == HanakageEncounter.Phase.Attack)
            Debug.Log("[hanakage-audio] motif enters (elegant/climbing, NOT boss music)");
    }

    private void Update() { Tick(Time.deltaTime); }

    /// <summary>dt-driven so the editor harness can step it alongside everything else.</summary>
    public void Tick(float dt)
    {
        if (dt <= 0f) dt = 1f / 60f;
        var phase = encounter != null ? encounter.State : HanakageEncounter.Phase.Dormant;

        float amb, mech, mot;
        switch (phase)
        {
            case HanakageEncounter.Phase.Observation:
            case HanakageEncounter.Phase.Approach:
                amb = ambienceApproach; mech = mechanicalApproach; mot = motifOff; break;
            case HanakageEncounter.Phase.OnWheel:
                amb = ambienceOnWheel; mech = mechanicalOnWheel; mot = motifOff; break;
            case HanakageEncounter.Phase.Attack:
                amb = ambienceAttack; mech = mechanicalAttack; mot = motifAttack; break;
            case HanakageEncounter.Phase.FinalHairpin:
                amb = ambienceAttack; mech = mechanicalAttack; mot = motifHairpin; break;
            case HanakageEncounter.Phase.Escape:
                amb = ambienceOnWheel; mech = mechanicalOnWheel; mot = motifEscape; break;
            default:
                amb = phase == HanakageEncounter.Phase.DiscoveryPending
                          ? ambienceAfter : ambienceNormal;
                mech = mechanicalNormal; mot = motifOff; break;
        }

        // Ducking for dialogue space.
        bool speaking = encounter != null && !string.IsNullOrEmpty(encounter.DialogueText);
        float duckTarget = speaking ? dialogueDuck : 1f;
        _duck = Mathf.MoveTowards(_duck, duckTarget,
                                  dt / Mathf.Max(0.01f, dialogueDuckSeconds));

        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, fadeSeconds / 3f));
        float kMot = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, motifInSeconds / 3f));

        AmbienceLevel = Mathf.Lerp(AmbienceLevel, amb, k);
        MechanicalLevel = Mathf.Lerp(MechanicalLevel, mech, k);
        MotifLevel = Mathf.Lerp(MotifLevel, mot, kMot);

        // The discovery sting is tied to the CARD, not to the escape - section 13 wants 20-30 s
        // of ordinary climbing in between, and firing it on the escape would collapse that gap.
        bool card = encounter != null && encounter.DiscoveryCardSeconds > 0f;
        if (card && !_sawCard) { _sawCard = true; StingFired = true; PlaySting(); }
        if (!card) _sawCard = false;

        Apply(ambience, AmbienceLevel * _duck);
        Apply(mechanical, MechanicalLevel);      // never ducked: it is the ride itself
        Apply(motif, MotifLevel * _duck);
    }

    private static void Apply(AudioSource s, float level)
    {
        if (s == null) return;
        s.volume = Mathf.Clamp01(level);
        if (level > 0.001f && !s.isPlaying && s.clip != null) s.Play();
    }

    private void PlaySting()
    {
        Debug.Log("[hanakage-audio] discovery sting (short blossom/legendary motif)");
        if (discoverySting != null && discoverySting.clip != null) discoverySting.Play();
    }
}

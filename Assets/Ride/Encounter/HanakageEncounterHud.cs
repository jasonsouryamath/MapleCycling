using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The encounter's entire on-screen presence: one objective line, one dialogue line, one
/// discovery card. Nothing else.
///
/// This is a separate canvas from <see cref="RideHud"/> on purpose. The ride HUD is the player's
/// instrument panel and is always on; the encounter overlay is a guest that must be able to
/// appear and vanish without touching a single field of the panel that shows their watts. Wiring
/// it into RideHud would have meant editing the class that owns the numbers the player is
/// actually pedalling to, for the sake of three labels.
///
/// The handoff's hardest presentation rule (section 4) is enforced here by omission: there is no
/// boss banner, no health bar, no name plate and no spawn flourish anywhere in this file. During
/// the first sighting the only thing that can appear is the "???" the state machine puts in
/// <see cref="HanakageEncounter.ObjectiveText"/>. If a future milestone wants a reveal, it has
/// to be added deliberately - it cannot happen by accident.
///
/// Styling is <see cref="HudKit"/>'s dark smoked-washi glass, which is the only scheme in this
/// project that survives both the blown-out blossom and the near-black cliff tunnel that the
/// encounter runs straight through.
/// </summary>
[DefaultExecutionOrder(300)]
public class HanakageEncounterHud : MonoBehaviour
{
    public const string CanvasName = "MapleRide Encounter HUD";

    public HanakageEncounter encounter;
    public Vector2 referenceResolution = new Vector2(1920f, 1080f);

    private Canvas _canvas;
    private RectTransform _objectiveCard, _dialogueCard, _discoveryCard;
    private Text _objective, _dialogue;
    private Text _discoveryKicker, _discoveryName, _discoveryEpithet, _discoveryTier;
    private Text _discoveryGlyph, _discoveryLore, _discoveryOutcome, _discoveryCount;
    private CanvasGroup _discoveryGroup;

    /// <summary>
    /// PROVISIONAL. Section 13's Journal entry copy, verbatim. Lives here rather than in the
    /// state machine because it is presentation, and because a real Journal screen will want it
    /// from a rider database instead.
    /// </summary>
    public string loreLine =
        "A mysterious Kage climber who appears without warning on the upper slopes.";

    /// <summary>
    /// Section 14's performance states, as the one line of the card that changes.
    ///
    /// The design is explicit that this is NOT win/lose: every state discovers her, and a weaker
    /// ride is still a real ride. So these read as a record of what happened, never as a grade -
    /// there is no "FAILED", no score, and no comparison to what the player could have done.
    /// ALL PROVISIONAL copy.
    /// </summary>
    public string spottedLine  = "You saw her.";
    public string caughtLine   = "You reached her wheel.";
    public string heldLine     = "You held her wheel.";
    public string impressedLine = "You answered her attack.";

    /// <summary>Seconds of fade at each end of the card. Restraint, per section 13.</summary>
    public float discoveryFadeSeconds = 0.9f;

    public Canvas Canvas => _canvas;

    private void Awake()
    {
        if (encounter == null) encounter = GetComponent<HanakageEncounter>();
        if (_canvas == null) Build();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    // ------------------------------------------------------------------ build

    /// <summary>
    /// Builds the overlay in code, idempotently. A scene that lost the canvas rebuilds an
    /// identical one; a scene that already has it is left alone.
    /// </summary>
    public void Build()
    {
        var existing = GameObject.Find(CanvasName);
        if (existing != null)
        {
            _canvas = existing.GetComponent<Canvas>();
            if (_canvas != null && existing.transform.childCount > 0)
            {
                Rebind(existing.transform);
                if (_objective != null) return;
            }
            DestroyImmediate(existing);
        }

        var go = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler),
                                typeof(GraphicRaycaster));
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 120;                       // above the ride HUD
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight = 0.5f;
        HudSprites.SetLayerRecursively(go, HudSprites.UiLayer);

        var root = (RectTransform)go.transform;

        // ---- objective: a single quiet line, top centre, under the checkpoint banner --------
        _objectiveCard = HudKit.SoftPanel(root, "Objective", HudKit.Glass);
        HudKit.Corner(_objectiveCard, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -186f), new Vector2(420f, 46f));
        _objective = HudKit.Label(_objectiveCard, "Text", "", 20, HudKit.Chalk,
                                  TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.Place((RectTransform)_objective.transform, Vector2.zero, Vector2.one,
                     Vector2.zero, Vector2.zero);
        HudKit.AddShadow(_objective);

        // ---- dialogue: her two lines, low centre, out of the road's way --------------------
        _dialogueCard = HudKit.SoftPanel(root, "Dialogue", HudKit.Glass);
        HudKit.Corner(_dialogueCard, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                      new Vector2(0f, 210f), new Vector2(360f, 58f));
        _dialogue = HudKit.Label(_dialogueCard, "Text", "", 24, HudKit.Chalk,
                                 TextAnchor.MiddleCenter, FontStyle.Italic);
        HudKit.Place((RectTransform)_dialogue.transform, Vector2.zero, Vector2.one,
                     Vector2.zero, Vector2.zero);
        HudKit.AddShadow(_dialogue);

        // ---- discovery card: the Journal reward -------------------------------------------
        // LEFT of centre, not right: the first capture put it on the right and it landed square
        // on top of the route map, hiding the elevation profile at exactly the moment the player
        // is being handed a reward. The mid-left band is the only region of this HUD that is
        // permanently empty (telemetry is top-left, the ride plan is bottom-left).
        _discoveryCard = HudKit.SoftPanel(root, "Discovery", HudKit.Glass);
        HudKit.Corner(_discoveryCard, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                      new Vector2(48f, 30f), new Vector2(430f, 268f));
        _discoveryGroup = _discoveryCard.gameObject.AddComponent<CanvasGroup>();
        var rule = HudKit.Panel(_discoveryCard, "Rule", HudKit.Ember);
        HudKit.Corner(rule, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(22f, -20f), new Vector2(72f, 3f));

        _discoveryKicker = HudKit.Label(_discoveryCard, "Kicker", "NEW RIDER DISCOVERED", 14,
                                        HudKit.ChalkSoft, TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)_discoveryKicker.transform, new Vector2(0f, 1f),
                      new Vector2(0f, 1f), new Vector2(22f, -32f), new Vector2(380f, 20f));

        // Section 13 puts a blossom above the name. The emoji in the doc is not in any font this
        // HUD can rely on, so this is U+273F (BLACK FLORETTE) in her ember pink - the same read,
        // from a glyph that is actually present. If it ever renders as a box, replace it with a
        // petal sprite rather than reaching for the emoji.
        _discoveryGlyph = HudKit.Label(_discoveryCard, "Glyph", "\u273F", 26, HudKit.Ember,
                                       TextAnchor.UpperRight, FontStyle.Normal);
        HudKit.Corner((RectTransform)_discoveryGlyph.transform, new Vector2(1f, 1f),
                      new Vector2(1f, 1f), new Vector2(-30f, -36f), new Vector2(40f, 34f));

        _discoveryName = HudKit.Label(_discoveryCard, "Name", "HANAKAGE", 38, HudKit.Chalk,
                                      TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)_discoveryName.transform, new Vector2(0f, 1f),
                      new Vector2(0f, 1f), new Vector2(22f, -56f), new Vector2(380f, 44f));
        _discoveryEpithet = HudKit.Label(_discoveryCard, "Epithet", "The Blossom Climber", 20,
                                         HudKit.ChalkSoft, TextAnchor.UpperLeft, FontStyle.Italic);
        HudKit.Corner((RectTransform)_discoveryEpithet.transform, new Vector2(0f, 1f),
                      new Vector2(0f, 1f), new Vector2(22f, -102f), new Vector2(380f, 26f));
        _discoveryTier = HudKit.Label(_discoveryCard, "Tier", "LEGENDARY  .  SAKURA PASS", 14,
                                      HudKit.Ember, TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)_discoveryTier.transform, new Vector2(0f, 1f),
                      new Vector2(0f, 1f), new Vector2(22f, -134f), new Vector2(380f, 20f));

        // Lore, then what the player actually did, then the Journal tally: the three things
        // section 13 says should "retroactively explain what just happened".
        _discoveryLore = HudKit.Label(_discoveryCard, "Lore", "", 15, HudKit.ChalkSoft,
                                      TextAnchor.UpperLeft, FontStyle.Italic);
        HudKit.Corner((RectTransform)_discoveryLore.transform, new Vector2(0f, 1f),
                      new Vector2(0f, 1f), new Vector2(22f, -162f), new Vector2(386f, 46f));
        _discoveryLore.horizontalOverflow = HorizontalWrapMode.Wrap;

        _discoveryOutcome = HudKit.Label(_discoveryCard, "Outcome", "", 16, HudKit.Chalk,
                                         TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)_discoveryOutcome.transform, new Vector2(0f, 1f),
                      new Vector2(0f, 1f), new Vector2(22f, -214f), new Vector2(386f, 22f));

        _discoveryCount = HudKit.Label(_discoveryCard, "Count", "", 13, HudKit.ChalkSoft,
                                       TextAnchor.UpperLeft, FontStyle.Normal);
        HudKit.Corner((RectTransform)_discoveryCount.transform, new Vector2(0f, 1f),
                      new Vector2(0f, 1f), new Vector2(22f, -240f), new Vector2(386f, 20f));

        foreach (var t in new[] { _discoveryKicker, _discoveryName, _discoveryEpithet,
                                  _discoveryTier, _discoveryGlyph, _discoveryLore,
                                  _discoveryOutcome, _discoveryCount })
            HudKit.AddShadow(t);

        HudSprites.SetLayerRecursively(go, HudSprites.UiLayer);
        Hide();
    }

    private void Rebind(Transform root)
    {
        _objectiveCard = root.Find("Objective") as RectTransform;
        _dialogueCard = root.Find("Dialogue") as RectTransform;
        _discoveryCard = root.Find("Discovery") as RectTransform;
        _objective = _objectiveCard != null ? _objectiveCard.GetComponentInChildren<Text>(true) : null;
        _dialogue = _dialogueCard != null ? _dialogueCard.GetComponentInChildren<Text>(true) : null;

        // Rebind the discovery card too. Before this existed, a scene that already had the
        // canvas took the early-out in Build() with every discovery label left null - the card
        // showed, but its variable half (lore, outcome, tally) was silently empty.
        if (_discoveryCard != null)
        {
            _discoveryGroup = _discoveryCard.GetComponent<CanvasGroup>();
            _discoveryGlyph = Find(_discoveryCard, "Glyph");
            _discoveryLore = Find(_discoveryCard, "Lore");
            _discoveryOutcome = Find(_discoveryCard, "Outcome");
            _discoveryCount = Find(_discoveryCard, "Count");
            _discoveryKicker = Find(_discoveryCard, "Kicker");
            _discoveryName = Find(_discoveryCard, "Name");
            _discoveryEpithet = Find(_discoveryCard, "Epithet");
            _discoveryTier = Find(_discoveryCard, "Tier");
        }

        // An older canvas without the new labels must be rebuilt, not patched.
        if (_discoveryGroup == null || _discoveryLore == null || _discoveryCount == null)
            _objective = null;
    }

    private static Text Find(Transform card, string name)
    {
        var t = card.Find(name);
        return t != null ? t.GetComponent<Text>() : null;
    }

    private void Hide()
    {
        if (_objectiveCard != null) _objectiveCard.gameObject.SetActive(false);
        if (_dialogueCard != null) _dialogueCard.gameObject.SetActive(false);
        if (_discoveryCard != null) _discoveryCard.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ refresh

    public void Refresh()
    {
        if (_objectiveCard == null) return;
        if (encounter == null) { Hide(); return; }

        string obj = encounter.ObjectiveText;
        bool showObjective = !string.IsNullOrEmpty(obj);
        if (_objectiveCard.gameObject.activeSelf != showObjective)
            _objectiveCard.gameObject.SetActive(showObjective);
        if (showObjective) _objective.text = obj;

        string line = encounter.DialogueText;
        bool showLine = !string.IsNullOrEmpty(line);
        if (_dialogueCard.gameObject.activeSelf != showLine)
            _dialogueCard.gameObject.SetActive(showLine);
        if (showLine) _dialogue.text = "\u201c" + line + "\u201d";

        // The card only exists for the full duration on a genuine first discovery; a later ride
        // gets a near-zero timer and therefore no card at all.
        bool showCard = encounter.DiscoveryCardSeconds > 0.05f;
        if (_discoveryCard.gameObject.activeSelf != showCard)
        {
            _discoveryCard.gameObject.SetActive(showCard);
            if (showCard) FillDiscoveryCard();
        }
        if (showCard && _discoveryGroup != null)
        {
            // Fade at both ends. Section 13 asks for restraint and for the encounter to
            // "breathe"; a card that pops in at full opacity reads as an achievement toast.
            float f = Mathf.Max(0.01f, discoveryFadeSeconds);
            float total = encounter.config != null ? encounter.config.discoveryCardSeconds : 9f;
            float left = encounter.DiscoveryCardSeconds;
            float shown = total - left;
            _discoveryGroup.alpha = Mathf.Clamp01(Mathf.Min(shown / f, left / f));
        }
    }

    /// <summary>
    /// Fills the variable half of the card once, when it appears. Section 14's performance
    /// states land here: the same discovery, described by what the player actually managed.
    /// </summary>
    private void FillDiscoveryCard()
    {
        if (_discoveryLore != null) _discoveryLore.text = loreLine;

        if (_discoveryOutcome != null)
        {
            switch (encounter.Result)
            {
                case HanakageEncounter.Outcome.Impressed: _discoveryOutcome.text = impressedLine; break;
                case HanakageEncounter.Outcome.Held:      _discoveryOutcome.text = heldLine; break;
                case HanakageEncounter.Outcome.Caught:    _discoveryOutcome.text = caughtLine; break;
                case HanakageEncounter.Outcome.Spotted:   _discoveryOutcome.text = spottedLine; break;
                default:                                  _discoveryOutcome.text = ""; break;
            }
        }

        if (_discoveryCount != null)
            _discoveryCount.text = "RIDERS DISCOVERED   " +
                                   RiderJournal.SakuraPassDiscoveredCount() + " / " +
                                   RiderJournal.SakuraPassRosterTarget;
    }
}

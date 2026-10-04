using UnityEngine;

/// <summary>
/// Makes an NPC greet the player when it sees them: a speech bubble with a phrase drawn from
/// a 100-entry pool, and a smile for exactly as long as the bubble is up.
///
/// "Sees" is a real field-of-view test, not just proximity: the player must be inside
/// <see cref="viewAngle"/> of the NPC's own forward direction and within
/// <see cref="triggerDistance"/>. The NPC deliberately does NOT turn to greet - it keeps
/// riding its route and simply greets whoever comes into view.
/// </summary>
public class NpcGreeting : MonoBehaviour
{
    [Header("Current line (read-only at runtime)")]
    public string phrase;

    [Header("Seeing the player")]
    [Tooltip("How far away the NPC can notice the player, in metres.")]
    public float triggerDistance = 13f;
    [Tooltip("Total cone width in degrees, centred on the NPC's forward axis. The player must " +
             "be inside this cone for the NPC to 'see' them; 180 would be everything in front.")]
    public float viewAngle = 150f;
    [Tooltip("Seconds before this NPC will greet the player again after losing sight of them.")]
    public float rearmSeconds = 6f;

    [Header("Greeting")]
    public float visibleSeconds = 3.5f;
    public Vector3 bubbleOffset = new Vector3(0f, 1.35f, 0f);

    [Header("Face card")]
    [Tooltip("Shown on the greeting card. Riders are met from behind, so the card is the only " +
             "place the player ever actually sees this rider's face and their smile. Baked by " +
             "NpcPortraitBake; left empty it is loaded from Resources/NpcPortraits/<riderName> " +
             "at runtime, and failing that a generic silhouette is used.")]
    public Texture2D portrait;
    [Tooltip("Display name on the card. Empty = derived from the GameObject name, so a rider " +
             "staged as 'Sakura NPC Aoi' introduces herself as 'Aoi'.")]
    public string riderName = "";
    [Tooltip("Off = keep the old floating speech bubble instead of the portrait card.")]
    public bool useFaceCard = true;

    /// <summary>Where a baked headshot lives, relative to a Resources folder.</summary>
    public const string PortraitResourceDir = "NpcPortraits/";

    [Header("Smile")]
    [Tooltip("Child renderer switched on only while the greeting bubble is visible. Built by " +
             "assets/3d/kuro/smile_coral.py as a mouth-shaped decal conformed to the face.")]
    public string smileRendererName = "SmileDecal";

    [Header("Voice")]
    [Tooltip("Lines unique to this rider. Leave empty to draw from the shared 100-phrase pool. " +
             "With a roster of NPCs the shared pool makes every rider sound like the same " +
             "person, so each character on the pass gets their own short set.")]
    public string[] ownPhrases = new string[0];

    /// <summary>This NPC's lines if they have any, otherwise the shared pool.</summary>
    string[] Pool => (ownPhrases != null && ownPhrases.Length > 0) ? ownPhrases : Phrases;

    Transform target;
    Renderer smile;
    float visibleUntil;
    float nextAllowed;
    bool wasSeen;
    GUIStyle style;
    Texture2D bubbleTexture;

    // Injectable clock. Editor tooling drives the greeting cycle deterministically without
    // entering Play mode, and Time.time does not advance during a batchmode -executeMethod
    // call, so the test would otherwise see every tick happen at the same instant. Left null
    // in the game, where this is simply Time.time.
    public static System.Func<float> Clock;
    static float Now => Clock != null ? Clock() : Time.time;

    // Shuffled bag: every phrase is used once before any repeats, so the player never hears
    // the same line twice running and the pool is heard evenly.
    int[] bag;
    int bagIndex;

    void Awake()
    {
        smile = FindSmile();
        SetSmile(false);
        ResolveIdentity();
    }

    /// <summary>
    /// Fills in the card's name and portrait if staging did not.
    ///
    /// Riders already saved in the scene were staged before the face card existed, so their
    /// NpcGreeting has neither field serialized. Deriving the name from the object name and
    /// loading the headshot out of Resources means those riders get a correct card without a
    /// re-stage - and a re-stage that DOES assign them simply wins, because both fields are
    /// only touched when empty.
    /// </summary>
    public void ResolveIdentity()
    {
        if (string.IsNullOrEmpty(riderName))
        {
            var n = gameObject.name;
            int cut = n.LastIndexOf(' ');
            riderName = (cut >= 0 && cut < n.Length - 1) ? n.Substring(cut + 1) : n;
        }
        if (portrait == null && !string.IsNullOrEmpty(riderName))
            portrait = Resources.Load<Texture2D>(PortraitResourceDir + riderName);
    }

    Renderer FindSmile()
    {
        if (string.IsNullOrEmpty(smileRendererName)) return null;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == smileRendererName) return r;
        return null;
    }

    void SetSmile(bool on)
    {
        if (smile != null && smile.enabled != on) smile.enabled = on;
    }

    Transform FindPlayer()
    {
        var byName = GameObject.Find("Kuro on Sakura Pass");
        if (byName != null) return byName.transform;
        // Other regions rename the rider; the bootstrap's route follower always points at the real one.
        var boot = Object.FindFirstObjectByType<RideBootstrap>();
        if (boot != null && boot.follower != null && boot.follower.rider != null) return boot.follower.rider;
        // The rider object is rebuilt and renamed often enough that the name alone is fragile;
        // fall back to whatever actually carries the player controller, then to the camera.
        var controller = Object.FindFirstObjectByType<KuroKeyboardController>();
        if (controller != null) return controller.transform;
        return Camera.main != null ? Camera.main.transform : null;
    }

    /// <summary>True when the player is close enough AND inside this NPC's view cone.</summary>
    bool CanSee(Transform player)
    {
        if (player == null) return false;
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance > triggerDistance || distance < 0.01f) return false;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(forward.normalized, toPlayer / distance);
        return angle <= viewAngle * 0.5f;
    }

    void Update()
    {
        if (target == null) target = FindPlayer();
        if (smile == null) smile = FindSmile();

        bool seen = CanSee(target);
        if (seen && !wasSeen && Now >= nextAllowed)
        {
            phrase = NextPhrase();
            visibleUntil = Now + visibleSeconds;
            ShowCard();
        }
        if (!seen && wasSeen) nextAllowed = Now + rearmSeconds;
        wasSeen = seen;

        // She smiles only while she is actually greeting, and returns to neutral after.
        SetSmile(Now <= visibleUntil && !string.IsNullOrEmpty(phrase));
    }

    /// <summary>Pushes this rider's face, name and line onto the shared greeting card.</summary>
    void ShowCard()
    {
        if (!useFaceCard) return;
        ResolveIdentity();
        NpcGreetingCard.Instance.Show(this, riderName, phrase, portrait, visibleSeconds, Now);
    }

    void OnDisable()
    {
        SetSmile(false);
        if (useFaceCard && NpcGreetingCard.Instance != null)
            NpcGreetingCard.Instance.Dismiss(this, Now);
    }

    string NextPhrase()
    {
        var pool = Pool;
        if (bag == null || bag.Length != pool.Length)
        {
            bag = new int[pool.Length];
            for (int i = 0; i < bag.Length; i++) bag[i] = i;
            bagIndex = bag.Length;
        }
        if (bagIndex >= bag.Length)
        {
            for (int i = bag.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int swap = bag[i]; bag[i] = bag[j]; bag[j] = swap;
            }
            bagIndex = 0;
        }
        return pool[bag[bagIndex++]];
    }

    void OnGUI()
    {
        // The face card replaces the floating bubble. The bubble is kept behind a flag rather
        // than deleted: it is the only greeting visual that needs no baked portrait, so it
        // stays available for a rider staged in a scene with no portraits at all.
        if (useFaceCard) return;
        if (Now > visibleUntil || string.IsNullOrEmpty(phrase)) return;
        var cam = Camera.main; if (cam == null) return;
        Vector3 screen = cam.WorldToScreenPoint(transform.position + bubbleOffset);
        if (screen.z <= 0f) return;
        if (style == null)
        {
            bubbleTexture = new Texture2D(1, 1);
            bubbleTexture.SetPixel(0, 0, Color.white);
            bubbleTexture.Apply();
            style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(22, 22, 16, 16),
                normal = { textColor = new Color(0.04f, 0.02f, 0.04f), background = bubbleTexture }
            };
        }
        var rect = new Rect(screen.x - 220f, Screen.height - screen.y - 62f, 440f, 124f);
        GUI.Box(rect, phrase, style);
    }

    /// <summary>
    /// 100 individually written greetings. These are deliberately whole authored lines rather
    /// than a 10x10 opener/closer cross-product: the combinatorial version produced grammatical
    /// but obviously templated chatter ("Nice pace! the summit is waiting.") and the seams
    /// showed within a couple of encounters.
    /// </summary>
    static readonly string[] Phrases =
    {
        "Morning! Perfect day for the pass.",
        "Hey, nice line through that corner!",
        "You're climbing well today!",
        "Sakura season never gets old, huh?",
        "Careful, there's gravel after the bend.",
        "That's a gorgeous bike you've got.",
        "Keep that cadence smooth!",
        "Almost at the summit, hang in there!",
        "Petals in your spokes again?",
        "I've ridden this road for years. Still love it.",
        "Left side, coming through!",
        "Great day to be out here, isn't it?",
        "Your form's looking sharp.",
        "Watch the hairpin, it tightens up.",
        "The view from the top is worth every metre.",
        "Nice kit! Very sharp.",
        "Tailwind on the descent today. Enjoy it.",
        "Don't burn all your matches on this climb.",
        "First time up Sakura Pass?",
        "Mind the damp patch under the trees.",
        "You make that look easy!",
        "I'm just spinning the legs out today.",
        "Cherry blossom's at its best this week.",
        "Say hi to the torii gate for me.",
        "Steady now, the gradient kicks up soon.",
        "Lovely morning for it!",
        "You passed me like I was parked!",
        "Fresh tarmac around the next corner.",
        "Keep your shoulders relaxed.",
        "Beautiful cadence, very smooth.",
        "The lake comes into view just up there.",
        "Chain's sounding healthy!",
        "Coffee at the summit? Worth the stop.",
        "Nearly through the steep bit now.",
        "I love this stretch of road.",
        "Nice and steady, that's the way.",
        "Wind's picking up near the ridge.",
        "You're flying today!",
        "Save something for the last kilometre.",
        "The tunnel's cooler than you'd expect.",
        "Good line! Right through the apex.",
        "Petals everywhere this morning.",
        "Give me a wave on the way back down!",
        "This climb rewards patience.",
        "Careful, the descent is faster than it looks.",
        "Perfect weather, perfect road.",
        "Don't forget to drink!",
        "You've got a strong rhythm going.",
        "Best road on the whole mountain.",
        "Watch for the cattle grid ahead.",
        "That gearing suits you well.",
        "The switchbacks start in about a kilometre.",
        "Fantastic effort! Keep going.",
        "Quiet out here today. Lovely, isn't it?",
        "Sun's just hitting the blossoms. Magic.",
        "Easy does it through the village.",
        "Your bike sounds beautifully tuned.",
        "See you at the top!",
        "The gradient eases after the bridge.",
        "Now that's how you ride a corner.",
        "Fresh legs? You look it.",
        "Nothing beats a morning like this.",
        "I'll catch you on the descent!",
        "Careful of the drain covers.",
        "That's a proper climbing pace.",
        "The old shrine's just through the trees.",
        "Blossom's falling like snow up here.",
        "Keep it smooth over the crest.",
        "Every ride up here is different.",
        "Looking comfortable! Nice.",
        "It flattens out by the lake, promise.",
        "Careful, riders coming down fast.",
        "Great to see someone else out early.",
        "You're making good time.",
        "This is my favourite corner on the pass.",
        "Mind the roots pushing through the tarmac.",
        "Beautiful day to suffer a little!",
        "Keep breathing, you're nearly there.",
        "Your descending looks confident.",
        "Watch the shade, it stays slippery.",
        "Not long to the summit marker now.",
        "The air smells incredible up here.",
        "Strong ride! Really strong.",
        "Take the inside line, it's cleaner.",
        "You picked the perfect day for this.",
        "The guardrail section is exposed. Careful.",
        "I never tire of this view.",
        "Nice spin! Very efficient.",
        "There's a water fountain by the shrine.",
        "Hold that pace, it's a good one.",
        "The last ramp is the hard one.",
        "Sakura Pass is treating us well today.",
        "You're stronger than you were last time!",
        "Enjoy the run down, you've earned it.",
        "Careful, tight left after the trees.",
        "That's a lovely rhythm you've found.",
        "Wave if you need anything!",
        "The summit cafe opens at nine.",
        "Ride safe out there, friend!",
        "See you on the mountain again soon!"
    };
}

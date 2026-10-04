using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The rider "face card": a small character portrait that pops up with a passing cyclist's
/// hello.
///
/// WHY THIS EXISTS. Greetings used to be a bare OnGUI speech box floating over the NPC. The
/// player only ever meets oncoming riders from behind - they are past the camera by the time
/// the bubble is up - so the greeter's face, and the smile the expression pipeline bakes onto
/// it, were never actually seen. The card puts the face on screen: a baked smiling headshot
/// (see <c>NpcPortraitBake</c>), the rider's name, and their line.
///
/// Self-contained on purpose: it owns its own canvas rather than living inside
/// <see cref="RideHud"/>, so it works in any region, in scenes that have no ride HUD at all,
/// and so nothing in the ride HUD had to be touched to add it. It is built from
/// <see cref="HudKit"/>, so it inherits the same smoked-glass art direction and the same
/// <see cref="HudKit.PanelAlpha"/> transparency as every other panel.
///
/// Placement follows the actual lower HUD rectangles. The elevation strip spans the apparent
/// gap between plan and GPS, so a fixed bottom-centre anchor obscures its route data.
/// </summary>
[DefaultExecutionOrder(120)]
public class NpcGreetingCard : MonoBehaviour
{
    public const string CanvasName = "MapleRide Greeting Card";

    // ---- provisional tuning ----------------------------------------------------------
    /// <summary>PROVISIONAL: card size at the 1920x1080 reference resolution.</summary>
    public static readonly Vector2 CardSize = new Vector2(560f, 168f);
    /// <summary>PROVISIONAL: pixels up from the bottom edge.</summary>
    public const float BottomMargin = 26f;
    /// <summary>PROVISIONAL: square portrait edge in card pixels.</summary>
    public const float PortraitPx = 128f;
    /// <summary>PROVISIONAL: seconds of fade at each end of the card's life.</summary>
    public const float FadeSeconds = 0.35f;

    /// <summary>
    /// PROVISIONAL: the card's own glass, much heavier than <see cref="HudKit.PanelAlpha"/>
    /// (0.18). At PanelAlpha the road and riders showed straight through the card, so in
    /// screenshots the greeting read as sitting BEHIND the 3D scene. A speech card is
    /// read, not glanced at, so it gets a solid bed (same reasoning as RideHud.PillGlass).
    /// </summary>
    public static readonly Color CardGlass = new Color(0.035f, 0.045f, 0.065f, 0.78f);
    /// <summary>PROVISIONAL: the spoken line shrinks to this size before it would clip.</summary>
    public const int LineMinPx = 14;

    private static NpcGreetingCard _instance;

    /// <summary>The live card, created on first greeting. Never null at the call site.</summary>
    public static NpcGreetingCard Instance
    {
        get
        {
            if (_instance != null) return _instance;
            _instance = FindFirstObjectByType<NpcGreetingCard>(FindObjectsInactive.Include);
            if (_instance == null)
            {
                var go = new GameObject(CanvasName + " Host");
                _instance = go.AddComponent<NpcGreetingCard>();
            }
            return _instance;
        }
    }

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _card;
    private RawImage _portrait;
    private Text _name, _line;
    private Image _insignia;
    private float _nameX;
    private float _shownAt, _hideAt;
    private object _owner;
    private RideHud _rideHud;
    private readonly Vector3[] _corners = new Vector3[4];
    private static readonly string[] LowerPanels =
        { "Ride Plan Card", "Wind Card", "Elevation Strip", "GPS Window" };

    /// <summary>The canvas, built on demand. Exposed so a capture harness can composite it the
    /// same way it composites the ride HUD.</summary>
    public Canvas Canvas { get { EnsureBuilt(); return _canvas; } }

    private void Awake() { if (_instance == null) _instance = this; }

    // ================================================================= construction

    /// <summary>Idempotent: destroys any canvas of the exact same name under this host first,
    /// so a rebuild can never leave two cards stacked on each other.</summary>
    public void EnsureBuilt()
    {
        if (_canvas != null) return;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name != CanvasName) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        var go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas),
                                typeof(CanvasScaler), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        // Overlay for the same reason RideHud is: composited after SakuraPostFX, so the sunset
        // grade and bloom can never wash the portrait or the text out.
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 110;          // above the ride HUD (100), below nothing

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _group = go.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        var root = (RectTransform)go.transform;

        _card = HudKit.SoftPanel(root, "Greeting Card", CardGlass);
        HudKit.Corner(_card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                      new Vector2(0f, BottomMargin), CardSize);
        var frame = HudKit.SoftPanel(_card, "Edge", HudKit.GlassEdge, frame: true);
        HudKit.Place(frame, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Portrait sits on its own darker bed. The headshot is baked against a dark backdrop,
        // and at PanelAlpha the pane alone is far too transparent to read a face against.
        var bed = HudKit.SoftPanel(_card, "Portrait Bed",
                                   new Color(0.016f, 0.024f, 0.035f, 0.80f));
        HudKit.Corner(bed, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                      new Vector2(20f, 0f), new Vector2(PortraitPx, PortraitPx));

        var pgo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer),
                                 typeof(RawImage));
        pgo.transform.SetParent(bed, false);
        _portrait = pgo.GetComponent<RawImage>();
        _portrait.raycastTarget = false;
        _portrait.texture = Silhouette;
        HudKit.Place((RectTransform)pgo.transform, Vector2.zero, Vector2.one,
                     new Vector2(2f, 2f), new Vector2(-2f, -2f));

        var pframe = HudKit.SoftPanel(bed, "Portrait Edge", HudKit.Ember, frame: true);
        HudKit.Place(pframe, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        float textX = 20f + PortraitPx + 18f;

        // The spoken line is the one piece of small type on this card, and it lands wherever
        // the road happens to be. It gets the same local text bed the ride HUD uses.
        var lineBed = HudKit.Panel(_card, "Line Bed", new Color(0.016f, 0.024f, 0.035f, 0.55f));
        HudKit.Place(lineBed, new Vector2(0f, 0f), new Vector2(1f, 1f),
                     new Vector2(textX - 8f, 14f), new Vector2(-12f, -58f));

        _name = HudKit.Label(_card, "Name", "", 24, HudKit.Chalk,
                             TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.AddOutline(_name, 0.88f, 1.3f);
        HudKit.Place((RectTransform)_name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(textX, -46f), new Vector2(-18f, -16f));
        _nameX = textX;
        // Rank insignia beside a racer's name (RaceDirector racers only; hidden otherwise).
        _insignia = HudKit.Panel(_card, "Insignia", Color.white).GetComponent<Image>();
        _insignia.preserveAspect = true;
        HudKit.Corner(_insignia.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(textX, -12f), new Vector2(32f, 32f));
        _insignia.gameObject.SetActive(false);

        var rule = HudKit.Panel(_card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(textX, -51f), new Vector2(-18f, -48f));

        _line = HudKit.Label(_card, "Line", "", 19, HudKit.Chalk, TextAnchor.UpperLeft);
        _line.horizontalOverflow = HorizontalWrapMode.Wrap;
        _line.verticalOverflow = VerticalWrapMode.Truncate;
        // Long lines used to be cut mid-sentence by Truncate. Best-fit shrinks the type
        // (19 -> 14 px) until the whole line fits the bed; Truncate stays only as the last
        // resort for a line too long even at 14 px (~6 lines, far past any authored greeting).
        _line.resizeTextForBestFit = true;
        _line.resizeTextMinSize = LineMinPx;
        _line.resizeTextMaxSize = 19;
        HudKit.AddShadow(_line, 0.85f, 1.1f);
        HudKit.Place((RectTransform)_line.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                     new Vector2(textX, 16f), new Vector2(-18f, -60f));

        HudSprites.SetLayerRecursively(go, HudSprites.UiLayer);
    }

    // ================================================================= api

    /// <summary>
    /// Show a rider's greeting. <paramref name="owner"/> is whoever is greeting, so a second
    /// rider entering the cone replaces the card instead of two NPCs fighting over it, and so
    /// a rider can only ever clear its OWN card.
    /// </summary>
    public void Show(object owner, string riderName, string line, Texture portrait,
                     float seconds, float now)
    {
        EnsureBuilt();
        _owner = owner;
        _name.text = string.IsNullOrEmpty(riderName) ? "Rider" : riderName;
        var racer = owner is Component comp ? comp.GetComponent<RaceNpc>() : null;
        bool ranked = racer != null && racer.def != null;
        if (_insignia != null)
        {
            _insignia.gameObject.SetActive(ranked);
            var nrt = (RectTransform)_name.transform;
            nrt.offsetMin = new Vector2(_nameX + (ranked ? 38f : 0f), nrt.offsetMin.y);
            if (ranked)
            {
                _insignia.sprite = RaceArt.Insignia(racer.def.Level);
                _name.text += $"  <size=17><color=#c9d3e6>Lv {racer.def.Level} · {RaceMath.InsigniaName(racer.def.Level)}</color></size>";
            }
        }
        _line.text = line ?? "";
        _portrait.texture = portrait != null ? portrait : Silhouette;
        _shownAt = now;
        _hideAt = now + seconds;
        PlaceOutsideRideHud();
    }

    /// <summary>Clears the card if <paramref name="owner"/> still owns it.</summary>
    public void Dismiss(object owner, float now)
    {
        if (_owner != owner) return;
        if (_hideAt > now) _hideAt = now;
    }

    /// <summary>Drives the fade. Exposed so an editor harness can advance it without Play mode
    /// (Time.time does not move during a batchmode -executeMethod call).</summary>
    public void Tick(float now)
    {
        if (_group == null) return;
        // An in-world greeting belongs to the ride, not the full-screen fast-travel map.
        // The map shares the HUD canvas but the card has its own higher-sorting canvas.
        if (_rideHud == null) _rideHud = FindFirstObjectByType<RideHud>(FindObjectsInactive.Include);
        if (_rideHud != null && _rideHud.worldMap != null && _rideHud.worldMap.isOpen)
        {
            _group.alpha = 0f;
            return;
        }
        if (now < _hideAt + FadeSeconds) PlaceOutsideRideHud();
        float a;
        if (now >= _hideAt) a = 1f - Mathf.Clamp01((now - _hideAt) / FadeSeconds);
        else a = Mathf.Clamp01((now - _shownAt) / FadeSeconds);
        _group.alpha = Mathf.Clamp01(a);
    }

    /// <summary>
    /// In the greeting canvas's local coordinates, raise the card above whichever LIVE lower
    /// HUD pane intersects its horizontal span. This follows CanvasScaler at 16:9, 16:10 and
    /// ultrawide instead of assuming that the plan/GPS gap stays empty at every resolution.
    /// </summary>
    private void PlaceOutsideRideHud()
    {
        if (_card == null) return;
        if (_rideHud == null) _rideHud = FindFirstObjectByType<RideHud>(FindObjectsInactive.Include);
        var hud = _rideHud != null ? _rideHud.Canvas : null;
        if (hud == null) return;
        var cardRoot = _card.parent as RectTransform;
        var hudRoot = hud.transform as RectTransform;
        if (cardRoot == null || hudRoot == null) return;

        float bottom = BottomMargin;
        float halfWidth = _card.rect.width * 0.5f;
        for (int i = 0; i < LowerPanels.Length; i++)
        {
            var pane = hudRoot.Find(LowerPanels[i]) as RectTransform;
            if (pane == null || !pane.gameObject.activeInHierarchy) continue;
            pane.GetWorldCorners(_corners);
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            for (int j = 0; j < 4; j++)
            {
                var p = cardRoot.InverseTransformPoint(_corners[j]);
                left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x);
                low = Mathf.Min(low, p.y); high = Mathf.Max(high, p.y);
            }
            if (left >= halfWidth + 12f || right <= -halfWidth - 12f) continue;
            // Only bottom-docked panes govern this position; the top telemetry and progress
            // controls are handled by leaving a separate 110-unit top margin below.
            if (low > cardRoot.rect.yMin + cardRoot.rect.height * 0.42f) continue;
            bottom = Mathf.Max(bottom, high - cardRoot.rect.yMin + 16f);
        }
        _card.anchoredPosition = new Vector2(0f,
            Mathf.Min(bottom, cardRoot.rect.height - _card.rect.height - 110f));
    }

    private void Update()
    {
        Tick(NpcGreeting.Clock != null ? NpcGreeting.Clock() : Time.time);
    }

    // ================================================================= fallback art

    private static Texture2D _silhouette;

    /// <summary>
    /// Generic head-and-shoulders silhouette, generated once. A rider staged before the
    /// portrait bake has run still gets a card with a readable placeholder rather than a
    /// magenta "no texture" square or a null-reference.
    /// </summary>
    public static Texture2D Silhouette
    {
        get
        {
            if (_silhouette != null) return _silhouette;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "NpcSilhouette" };
            var bg = new Color(0.075f, 0.106f, 0.129f, 1f);
            var fg = new Color(0.62f, 0.66f, 0.70f, 1f);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                // head disc
                float hd = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.66f) * (v - 0.66f));
                bool head = hd < 0.20f;
                // shoulders: a wide ellipse rising from the bottom edge
                float sx = (u - 0.5f) / 0.42f, sy = (v - 0.02f) / 0.42f;
                bool body = (sx * sx + sy * sy) < 1f && v < 0.44f;
                px[y * S + x] = (head || body) ? fg : bg;
            }
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false, false);
            _silhouette = tex;
            return _silhouette;
        }
    }
}

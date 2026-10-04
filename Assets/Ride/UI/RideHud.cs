using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ride HUD: telemetry card, next-checkpoint banner, ride-plan card and the GPS window.
///
/// Two layouts share this class. The default (<see cref="compactHud"/>) is the compact
/// "cycling app" look: a telemetry pill top-left, a route progress line top-centre and a
/// circular minimap bottom-right. The original cards are kept behind the flag rather than
/// deleted so the change is one tick-box to revert while the new look is still being judged.
///
/// Built entirely in code into a Canvas named "MapleRide Ride HUD", so the setup pass is
/// idempotent (it destroys any existing canvas of that exact name first) and so an editor
/// capture harness can construct, drive and render the *real* widgets rather than a mock-up.
/// </summary>
[DefaultExecutionOrder(100)]
public class RideHud : MonoBehaviour
{
    public const string CanvasName = "MapleRide Ride HUD";

    [Header("Wiring")]
    public RideSession session;
    public RouteDirector director;
    public DeviceManager devices;
    public RouteMapHud map;
    public ElevationStripHud elevationStrip;
    [Tooltip("The World Map fast-travel overlay; built into this same canvas.")]
    public WorldMapHud worldMap;

    [Header("Reference resolution")]
    public Vector2 referenceResolution = new Vector2(1920f, 1080f);

    [Header("Layout")]
    [Tooltip("Compact HUD (default): a small power / cadence / heart pill top-left, a letter-" +
             "spaced route title over a checkpoint progress line top-centre, and a circular " +
             "follow minimap bottom-right. Untick (before Build) for the original telemetry " +
             "card, checkpoint banner and full GPS window.")]
    public bool compactHud = true;
    [Tooltip("Bike computer bought on Maple Row (0 none, 1 APEX Pro, 2 APEX Elite). Set at run time " +
             "by KitAppearance; BikeComputerStrip draws the extra readouts.")]
    [System.NonSerialized] public int computerTier;
    [Tooltip("Accessibility: yellow-on-black wind indicator.")]
    public bool highContrastWind;

    private Canvas _canvas;
    private CanvasScaler _scaler;
    private Text _power, _cadence, _speed, _heart, _effort, _source;
    private Image _effortBar;
    private Text _bannerSection, _bannerMain;
    private RectTransform _bannerCard;
    private Text _planTitle, _planElapsed, _planCourse, _planLaps, _planHint;
    private RectTransform _planCard, _planHintBed;
    private int _planHintPage = -1;
    private Image _planBar;
    private RectTransform _windPivot;
    private Image _windShaft, _windHead;
    private Text _windValue;
    private Text[] _planStatValue, _planStatCaption;
    private RectTransform _draftCard;
    private Image _draftBar;
    private Text _draftValue, _draftHint;
    private ShiosaiTrafficDirector _traffic;
    private float _draftSeekAt;
    private float _draftSeenAt = -999f;

    /// <summary>
    /// PROVISIONAL: seconds the draft card lingers in clean air after the last shelter. The
    /// empty card used to stay up the whole ride; at PanelAlpha its bare bar bed framed
    /// whatever was behind it (on Minato, the orange lattice of a harbour crane), which read
    /// as a stray wireframe graphic. The card now only appears while it has something to say.
    /// </summary>
    private const float CleanAirLingerS = 2.5f;

    // Compact-HUD route bar (top-centre). Only exists when compactHud is on; every refresh path
    // that touches it null-guards, exactly as the classic widgets are null in compact mode.
    private Text _routeTitle, _routeStatus;
    private RectTransform _routeStatusBed;
    private RectTransform _routeTrack, _routeMarker, _routeDotLayer;
    private Image _routeFill;
    private readonly System.Collections.Generic.List<Image> _routeDots =
        new System.Collections.Generic.List<Image>();
    private readonly System.Collections.Generic.List<float> _routeDotFractions =
        new System.Collections.Generic.List<float>();
    private string _routeBuiltFor = "";
    private string _routeTitleSource;
    // Heart-rate pairing button + popover (the "add a HR monitor" affordance).
    private Button _hrButton;
    private Image _hrHeart;
    private Text _hrTileLabel;
    private RectTransform _hrPanel;
    private Text _hrPanelStatus, _hrPanelHint;
    private bool _hrPanelOpen;
    private float _hrPulse, _beatPhase;

    public Canvas Canvas => _canvas;

    // Records what was on before the ride widgets were hidden for map selection, so restoring
    // cannot resurrect a card that was deliberately off (the draft card, for instance).
    private readonly System.Collections.Generic.List<(GameObject go, bool wasActive)> _stashed =
        new System.Collections.Generic.List<(GameObject, bool)>();

    /// <summary>
    /// Hides every ride widget except the World Map overlay, so the map-selection screen is not
    /// presented over a live-looking instrument panel for a ride that has not started.
    ///
    /// Idempotent in both directions: hiding twice does not overwrite the stash, and showing
    /// restores exactly the states that were captured rather than switching everything on.
    /// </summary>
    public void SetRideWidgetsVisible(bool visible)
    {
        if (_canvas == null) return;

        if (visible)
        {
            foreach (var (go, wasActive) in _stashed)
                if (go != null) go.SetActive(wasActive);
            _stashed.Clear();
            return;
        }

        if (_stashed.Count > 0) return;                 // already hidden - keep the first stash
        foreach (Transform child in _canvas.transform)
        {
            if (child.name == WorldMapHud.OverlayName) continue;
            _stashed.Add((child.gameObject, child.gameObject.activeSelf));
            child.gameObject.SetActive(false);
        }
    }

    // ================================================================= construction

    public void Build()
    {
        _stashed.Clear();
        foreach (Transform child in transform)
        {
            if (child.name != CanvasName) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        var go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas),
                                typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        // Screen-space OVERLAY, deliberately: an overlay canvas is composited after every camera
        // and after SakuraPostFX, so the sunset grade and bloom cannot wash the HUD out. The
        // first build rendered the HUD through the gameplay camera and the whole washi card came
        // back pink and nearly illegible. The capture harness temporarily switches this to world
        // space and renders it with its own un-graded UI camera for exactly the same reason.
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.worldCamera = Camera.main;
        _canvas.planeDistance = 1.2f;
        _canvas.sortingOrder = 100;

        _scaler = go.GetComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = referenceResolution;
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = 0.5f;

        var root = (RectTransform)go.transform;

        // Every widget reference is reset first: a rebuild that switches style must not leave
        // the other style's (now destroyed) widgets looking alive to the null-guarded refresh.
        _power = _cadence = _speed = _heart = _effort = _source = null;
        _effortBar = null;
        _bannerSection = _bannerMain = null;
        _bannerCard = null;
        _routeTitle = _routeStatus = null;
        _routeStatusBed = null;
        _planCard = null;
        _planHintBed = null;
        _routeTrack = _routeMarker = _routeDotLayer = null;
        _routeFill = null;
        _routeDots.Clear();
        _routeDotFractions.Clear();
        _routeBuiltFor = "";
        _routeTitleSource = null;
        System.Array.Clear(_pillIcons, 0, _pillIcons.Length);

        if (compactHud)
        {
            BuildTelemetryPill(root);
            BuildDraft(root);
            BuildRouteBar(root);
        }
        else
        {
            BuildTelemetry(root);
            BuildDraft(root);
            BuildBanner(root);
        }
        BuildPlan(root);
        BuildWindIndicator(root);
        BuildHeartRateButton(root);

        if (map == null) map = gameObject.GetComponent<RouteMapHud>();
        if (map == null) map = gameObject.AddComponent<RouteMapHud>();
        map.session = session;
        map.director = director;
        map.compact = compactHud;
        map.Build(root);
        map.Invalidate();

        // Bottom-centre elevation / progress strip (compact HUD only: the classic GPS window
        // still carries its own profile).
        if (compactHud)
        {
            if (elevationStrip == null) elevationStrip = gameObject.GetComponent<ElevationStripHud>();
            if (elevationStrip == null) elevationStrip = gameObject.AddComponent<ElevationStripHud>();
            elevationStrip.session = session;
            elevationStrip.director = director;
            elevationStrip.Build(root);
        }

        // The World Map lives in the same canvas so it shares the scaler, the raycaster and the
        // capture harness. Built last so its overlay is the top sibling.
        if (worldMap == null) worldMap = gameObject.GetComponent<WorldMapHud>();
        if (worldMap == null) worldMap = gameObject.AddComponent<WorldMapHud>();
        worldMap.session = session;
        if (worldMap.regions == null) worldMap.regions = gameObject.GetComponent<RegionDirector>();
        worldMap.Build(root);
        worldMap.Refresh();

        // Everything in the HUD goes on the UI layer. The capture harness renders that layer
        // with its own camera, after the graded world pass, so the HUD is never run through
        // SakuraPostFX's bloom and sunset tint.
        HudSprites.SetLayerRecursively(go, HudSprites.UiLayer);
    }

    /// <summary>
    /// The shared chrome every ride card now gets: a rounded smoked-glass pane at
    /// <see cref="HudKit.PanelAlpha"/> plus the one paper hairline that keeps it reading as a
    /// window. At ~18 % a pane has almost no silhouette of its own over dark forest, so the
    /// frame is what stops it dissolving into the scenery.
    /// </summary>
    private static RectTransform GlassCard(Transform parent, string name)
    {
        var card = HudKit.SoftPanel(parent, name, HudKit.Pane);
        var frame = HudKit.SoftPanel(card, "Edge", HudKit.GlassEdge, frame: true);
        HudKit.Place(frame, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return card;
    }

    private void BuildTelemetry(RectTransform root)
    {
        var card = GlassCard(root, "Telemetry Card");
        HudKit.Corner(card, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(26f, -26f), new Vector2(430f, 224f));

        var head = HudKit.Panel(card, "Head", HudKit.PaneTextBed);
        HudKit.Place(head, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -38f), Vector2.zero);
        var t = HudKit.Label(head, "Title", "TELEMETRY", 15, HudKit.Chalk,
                             TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(t);
        HudKit.Place((RectTransform)t.transform, Vector2.zero, Vector2.one,
                     new Vector2(16f, 0f), new Vector2(-150f, 0f));
        _source = HudKit.Label(head, "Source", "SIM", 13, SignalGreen,
                               TextAnchor.MiddleRight, FontStyle.Bold);
        HudKit.AddShadow(_source);
        HudKit.Place((RectTransform)_source.transform, Vector2.zero, Vector2.one,
                     new Vector2(-260f, 0f), new Vector2(-16f, 0f));

        var rule = HudKit.Panel(card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -41f), new Vector2(0f, -38f));

        // Text beds, sized to the rows that carry small type. The card itself stays at
        // PanelAlpha; only the caption strip and the effort line get extra contrast.
        var statBed = HudKit.Panel(card, "Stat Bed", HudKit.PaneTextBed);
        HudKit.Corner(statBed, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(10f, -46f), new Vector2(410f, 25f));

        MakeStat(card, "Power", "POWER", 16f, out _power, 46, HudKit.Ember);
        MakeStat(card, "Cadence", "CADENCE", 132f, out _cadence, 30, HudKit.Chalk);
        MakeStat(card, "Speed", "SPEED", 230f, out _speed, 30, HudKit.Chalk);
        MakeStat(card, "Heart", "HEART", 330f, out _heart, 30, HudKit.Ember);

        var effortBed = HudKit.Panel(card, "Effort Bed", HudKit.PaneTextBed);
        HudKit.Corner(effortBed, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(10f, 38f), new Vector2(410f, 22f));

        _effort = HudKit.Label(card, "EffortText", "0 % FTP  -  0.0 W/kg", 14, HudKit.Chalk);
        HudKit.AddShadow(_effort);
        HudKit.Corner((RectTransform)_effort.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(16f, 40f), new Vector2(400f, 18f));

        var bar = HudKit.Panel(card, "EffortBarBed", HudKit.PaneBarBed);
        HudKit.Corner(bar, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(16f, 20f), new Vector2(398f, 12f));
        var fill = HudKit.Panel(bar, "Fill", new Color(0.937f, 0.545f, 0.643f, 1f));
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        fill.sizeDelta = new Vector2(0f, 0f);
        _effortBar = fill.GetComponent<Image>();
        _effortBar.sprite = HudSprites.White;
        _effortBar.type = Image.Type.Filled;
        _effortBar.fillMethod = Image.FillMethod.Horizontal;
        _effortBar.fillAmount = 0f;
        HudKit.Place(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    // ================================================================= compact HUD

    // PROVISIONAL TUNING - compact-HUD geometry, in 1920x1080 reference pixels.
    private const float PillMargin = 26f;
    private static readonly Vector2 PillSize = new Vector2(340f, 52f);
    private const float RouteBarWidth = 560f;
    // Widest the centred title may be. Half of it plus the plan card's right edge (26 + 470 px
    // from the left, where the ELAPSED chip lives) must fit in the narrowest supported canvas
    // (4:3 -> ~1663 px wide at match 0.5): 831 - 280 = 551 > 496, so 560 keeps the chip clear.
    private const float RouteTitleMaxWidth = RouteBarWidth;

    /// <summary>
    /// The pill's own smoked glass. Deliberately heavier than <see cref="HudKit.PanelAlpha"/>:
    /// the pill is small and carries no text beds, so its single pane has to supply the whole
    /// darkness floor under 20 px white type, and at 52 px tall it hides almost no road.
    /// </summary>
    private static readonly Color PillGlass = new Color(0.035f, 0.045f, 0.065f, 0.60f);

    /// <summary>"You are here" blue shared by the route bar and the minimap, so the rider's
    /// progress reads as the same thing in both places.</summary>
    public static readonly Color RideBlue = new Color(0.231f, 0.557f, 0.965f, 1f);

    private static readonly Color BoltGold = new Color(1f, 0.804f, 0.263f, 1f);
    private static readonly Color DotHollow = new Color(0.035f, 0.045f, 0.065f, 0.55f);

    /// <summary>
    /// Compact telemetry: one translucent pill, three glyph + number pairs, nothing else.
    ///
    /// Named "Telemetry Card" on purpose - MapleRideFlowRunner checks for a child of exactly
    /// that name to decide the ride HUD is up. Captions are replaced by icons (bolt / dial /
    /// heart) and the unit rides with the number, which is what lets three readings fit in a
    /// 340 px strip without any text smaller than 20 px.
    /// </summary>
    private void BuildTelemetryPill(RectTransform root)
    {
        var card = HudKit.Panel(root, "Telemetry Card", PillGlass);
        var img = card.GetComponent<Image>();
        img.sprite = HudSprites.Pill;
        img.type = Image.Type.Sliced;
        // The pill sprite's 9-slice border is 32 texels; scale it so the caps are exactly half
        // the pill's height and the ends come out as true semicircles.
        img.pixelsPerUnitMultiplier = 64f / PillSize.y;
        HudKit.Corner(card, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(PillMargin, -PillMargin), PillSize);

        var edge = HudKit.Panel(card, "Edge", HudKit.WithAlpha(HudKit.GlassEdge, 0.22f));
        var ei = edge.GetComponent<Image>();
        ei.sprite = HudSprites.PillFrame;
        ei.type = Image.Type.Sliced;
        ei.pixelsPerUnitMultiplier = 64f / PillSize.y;
        HudKit.Place(edge, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Fixed columns sized for the widest plausible readings ("1200 W", "120 RPM", "199"),
        // so the numbers never shuffle sideways as they change.
        _pillIcons[0] = PillItem(card, "Power", HudSprites.Bolt, BoltGold, 20f, new Vector2(20f, 22f), out _power);
        _pillIcons[1] = PillItem(card, "Cadence", HudSprites.Gauge, HudKit.Chalk, 128f, new Vector2(22f, 22f), out _cadence);
        _pillIcons[2] = PillItem(card, "Heart", HudSprites.Heart, HeartLive, 256f, new Vector2(22f, 20f), out _heart);
        _pillLayoutKey = -1;
    }

    private readonly RectTransform[] _pillIcons = new RectTransform[3];
    private int _pillLayoutKey = -1;

    /// <summary>
    /// Packs the three icon + number pairs with equal gaps and centres the row in the pill.
    ///
    /// Fixed columns left the gaps visibly uneven ("227 W" is much shorter than "88 RPM") and
    /// the heart group hard against the right cap. Arial's digits are tabular, so widths only
    /// change when a reading gains or loses a digit - the layout is keyed on the three string
    /// lengths and re-runs only then, so the row does not jitter as the numbers tick.
    /// </summary>
    private void LayoutPill()
    {
        if (_power == null || _cadence == null || _heart == null || _pillIcons[0] == null) return;
        int key = _power.text.Length | (_cadence.text.Length << 8) | (_heart.text.Length << 16);
        if (key == _pillLayoutKey) return;
        _pillLayoutKey = key;

        const float iconGap = 7f, groupGap = 22f;
        var texts = new[] { _power, _cadence, _heart };
        float total = 0f;
        for (int i = 0; i < 3; i++)
            total += _pillIcons[i].sizeDelta.x + iconGap + texts[i].preferredWidth + (i < 2 ? groupGap : 0f);

        float x = Mathf.Max(16f, (PillSize.x - total) * 0.5f);
        for (int i = 0; i < 3; i++)
        {
            _pillIcons[i].anchoredPosition = new Vector2(x, 0f);
            x += _pillIcons[i].sizeDelta.x + iconGap;
            var trt = texts[i].rectTransform;
            trt.anchoredPosition = new Vector2(x, 0f);
            trt.sizeDelta = new Vector2(texts[i].preferredWidth + 4f, trt.sizeDelta.y);
            x += texts[i].preferredWidth + groupGap;
        }
    }

    private static RectTransform PillItem(RectTransform card, string name, Sprite glyph, Color tint,
                                          float x, Vector2 iconSize, out Text value)
    {
        var icon = HudKit.Panel(card, name + "Icon", tint);
        icon.GetComponent<Image>().sprite = glyph;
        icon.GetComponent<Image>().preserveAspect = true;
        HudKit.Corner(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                      new Vector2(x, 0f), iconSize);

        value = HudKit.Label(card, name + "Value", "0", 20, HudKit.Chalk,
                             TextAnchor.MiddleLeft, FontStyle.Bold);
        // Single offset shadow: the pill already guarantees the contrast, the shadow only has
        // to lift the glyph edges when the pill sits over bright sky.
        HudKit.AddShadow(value, 0.7f, 1.2f);
        HudKit.Corner((RectTransform)value.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                      new Vector2(x + 29f, 0f), new Vector2(96f, 30f));
        return icon;
    }

    /// <summary>
    /// Compact route progress: a letter-spaced route title over one thin line with the
    /// checkpoints as hollow dots, the ridden part in <see cref="RideBlue"/>, a solid blue rider
    /// marker and a finish flag at the end. Replaces the checkpoint banner card - the banner's
    /// transient message becomes one small line under the bar, and there is no box at all, so
    /// the top-centre of the screen (where the road vanishes) stays open.
    /// </summary>
    private void BuildRouteBar(RectTransform root)
    {
        var bar = HudKit.Rect(root, "Route Progress");
        HudKit.Corner(bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -22f), new Vector2(RouteBarWidth + 80f, 92f));

        _routeTitle = HudKit.Label(bar, "Title", "", 19, HudKit.Chalk,
                                   TextAnchor.MiddleCenter, FontStyle.Bold);
        // A soft, low-alpha outline plus shadow: enough to hold white caps over bright sky
        // without the hard "stamped" edge a full-strength outline gives spaced capitals.
        HudKit.AddOutline(_routeTitle, 0.40f, 1.0f);
        HudKit.AddShadow(_routeTitle, 0.55f, 1.4f);
        HudKit.Corner((RectTransform)_routeTitle.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      Vector2.zero, new Vector2(RouteTitleMaxWidth, 26f));

        // Track: a local frame whose x runs -W/2 .. +W/2 along the line.
        _routeTrack = HudKit.Rect(bar, "Track");
        HudKit.Corner(_routeTrack, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                      new Vector2(0f, -44f), new Vector2(RouteBarWidth, 20f));

        // Dark casing one px either side of the line - the only "shadow" the bar gets.
        var casing = HudKit.Panel(_routeTrack, "Casing", new Color(0.016f, 0.020f, 0.027f, 0.40f));
        HudKit.Corner(casing, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, new Vector2(RouteBarWidth + 2f, 5f));
        var line = HudKit.Panel(_routeTrack, "Line", HudKit.WithAlpha(HudKit.Chalk, 0.80f));
        HudKit.Corner(line, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, new Vector2(RouteBarWidth, 2f));

        var fill = HudKit.Panel(_routeTrack, "Fill", RideBlue);
        HudKit.Corner(fill, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, new Vector2(RouteBarWidth, 4f));
        _routeFill = fill.GetComponent<Image>();
        _routeFill.sprite = HudSprites.White;             // Filled needs a sprite - see White
        _routeFill.type = Image.Type.Filled;
        _routeFill.fillMethod = Image.FillMethod.Horizontal;
        _routeFill.fillAmount = 0f;

        _routeDotLayer = HudKit.Rect(_routeTrack, "Checkpoints");
        HudKit.Place(_routeDotLayer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var flag = HudKit.Panel(_routeTrack, "Finish Flag", Color.white);
        flag.GetComponent<Image>().sprite = HudSprites.FinishFlag;
        // Pole foot sits on the line's end; the flag flies above it.
        HudKit.Corner(flag, new Vector2(0.5f, 0.5f), new Vector2(0.18f, 0.12f),
                      new Vector2(RouteBarWidth * 0.5f + 2f, -2f), new Vector2(22f, 22f));

        _routeMarker = HudKit.Rect(_routeTrack, "Rider");
        HudKit.Corner(_routeMarker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      new Vector2(-RouteBarWidth * 0.5f, 0f), new Vector2(18f, 18f));
        var halo = HudKit.Panel(_routeMarker, "Halo", HudKit.Chalk);
        HudKit.Place(halo, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        halo.GetComponent<Image>().sprite = HudSprites.Disc;
        var core = HudKit.Panel(_routeMarker, "Core", RideBlue);
        HudKit.Place(core, Vector2.zero, Vector2.one, new Vector2(2.5f, 2.5f), new Vector2(-2.5f, -2.5f));
        core.GetComponent<Image>().sprite = HudSprites.Disc;

        // 18 px BOLD over its own dark bed, with an outline (QA #4): the old 16 px regular line
        // with only a shadow vanished over bright sky. The bed is sized to the text each frame
        // and hidden when there is no message, so the top-centre stays open between banners.
        _routeStatusBed = HudKit.SoftPanel(bar, "Status Bed", HudKit.TopRowPane);
        HudKit.Corner(_routeStatusBed, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -58f), new Vector2(200f, 28f));
        _routeStatusBed.gameObject.SetActive(false);
        _routeStatus = HudKit.Label(bar, "Status", "", 18, HudKit.Chalk,
                                    TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_routeStatus, 0.85f, 1.5f);
        HudKit.Corner((RectTransform)_routeStatus.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -60f), new Vector2(RouteBarWidth + 80f, 24f));
    }

    /// <summary>Rebuilds the checkpoint dots when the course changes (not per frame).</summary>
    private void RebuildRouteDots(RouteCourse course)
    {
        foreach (Transform child in _routeDotLayer)
        {
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
        _routeDots.Clear();
        _routeDotFractions.Clear();

        float len = Mathf.Max(1f, course.Length);
        for (int i = 0; i < course.Checkpoints.Length; i++)
        {
            float f = course.Checkpoints[i].Distance / len;
            // The start is the rider marker's own home and the finish has the flag; a dot at
            // either end would only sit underneath one of them.
            if (f <= 0.004f || f >= 0.992f) continue;

            var dot = HudKit.Rect(_routeDotLayer, "Checkpoint_" + i);
            HudKit.Corner(dot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                          new Vector2((f - 0.5f) * RouteBarWidth, 0f), new Vector2(11f, 11f));
            var fill = HudKit.Panel(dot, "Fill", DotHollow);
            HudKit.Place(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fi = fill.GetComponent<Image>();
            fi.sprite = HudSprites.Disc;
            var rim = HudKit.Panel(dot, "Rim", HudKit.Chalk);
            HudKit.Place(rim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rim.GetComponent<Image>().sprite = HudSprites.Ring;

            _routeDots.Add(fi);
            _routeDotFractions.Add(f);
        }
        HudSprites.SetLayerRecursively(_routeDotLayer.gameObject, HudSprites.UiLayer);
        _routeMarker.SetAsLastSibling();
        _routeBuiltFor = course.Id + "|" + course.Length.ToString("0");
    }

    private void RefreshRouteBar(RouteCourse course)
    {
        if (_routeTrack == null) return;

        string key = course.Id + "|" + course.Length.ToString("0");
        if (key != _routeBuiltFor) RebuildRouteDots(course);

        if (!ReferenceEquals(_routeTitleSource, course.DisplayName))
        {
            _routeTitleSource = course.DisplayName;
            _routeTitle.text = FitWidth(_routeTitle, SpacedCaps(RouteTitle(course.DisplayName)), RouteTitleMaxWidth);
        }

        float f = course.Length <= 0.01f ? 0f : Mathf.Clamp01(session.DistanceM / course.Length);
        _routeFill.fillAmount = f;
        _routeMarker.anchoredPosition = new Vector2((f - 0.5f) * RouteBarWidth, 0f);
        for (int i = 0; i < _routeDots.Count; i++)
            _routeDots[i].color = _routeDotFractions[i] <= f ? RideBlue : DotHollow;

        if (_routeStatus != null)
        {
            _routeStatus.text = director != null && director.BannerVisible ? director.BannerText : "";
            if (_routeStatusBed != null)
            {
                bool any = !string.IsNullOrEmpty(_routeStatus.text);
                if (_routeStatusBed.gameObject.activeSelf != any)
                    _routeStatusBed.gameObject.SetActive(any);
                if (any)
                    _routeStatusBed.sizeDelta = new Vector2(
                        Mathf.Min(_routeStatus.preferredWidth + 36f, RouteBarWidth + 80f), 28f);
            }
        }
    }

    /// <summary>
    /// Trim <paramref name="s"/> with an ellipsis until it measures within <paramref name="maxWidth"/>
    /// at the label's own font and size. The Text overflows horizontally (HudKit.Label), so an
    /// over-long title would otherwise spill past its rect instead of clipping.
    /// </summary>
    private static string FitWidth(Text label, string s, float maxWidth)
    {
        var gen = label.cachedTextGeneratorForLayout;
        var settings = label.GetGenerationSettings(Vector2.zero);
        if (gen.GetPreferredWidth(s, settings) / label.pixelsPerUnit <= maxWidth) return s;
        for (int n = s.Length - 1; n > 0; n--)
        {
            string t = s.Substring(0, n).TrimEnd() + "…";
            if (gen.GetPreferredWidth(t, settings) / label.pixelsPerUnit <= maxWidth) return t;
        }
        return "…";
    }

    /// <summary>"Minato Coast - Beyond the Horizon" -> "MINATO COAST": the place, not the tagline.</summary>
    private static string RouteTitle(string displayName) =>
        RouteNames.Place(displayName).ToUpperInvariant();

    /// <summary>
    /// Wide tracking for the legacy Text component, which has no letter-spacing property:
    /// one space between letters, so a word gap becomes three and still reads as a gap.
    /// </summary>
    private static string SpacedCaps(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length * 2);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The DRAFT card: how well the player is sitting in the wheel in front.
    ///
    /// Built in the telemetry card's own idiom (washi panel, vermilion rule, small-caps caption,
    /// a horizontally-filled bar on a sand bed) and parked directly beneath it, because it is
    /// telemetry - it is a reading off the road, not a notification. It is hidden entirely on
    /// routes with no same-direction ambient traffic, so Sakura's HUD is untouched.
    /// </summary>
    private void BuildDraft(RectTransform root)
    {
        var card = GlassCard(root, "Draft Card");
        // Parked 10 px under whichever telemetry widget is above it: the 52 px compact pill
        // (26 + 52 + 10 = 88) or the classic 224 px card (26 + 224 + 12 = 262).
        float top = compactHud ? -(PillMargin + PillSize.y + 10f) : -262f;
        HudKit.Corner(card, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(26f, top), new Vector2(430f, 96f));
        _draftCard = card;

        var head = HudKit.Panel(card, "Head", HudKit.PaneTextBed);
        HudKit.Place(head, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -34f), Vector2.zero);
        var t = HudKit.Label(head, "Title", "DRAFT", 15, HudKit.Chalk,
                             TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(t);
        HudKit.Place((RectTransform)t.transform, Vector2.zero, Vector2.one,
                     new Vector2(16f, 0f), new Vector2(-150f, 0f));
        _draftValue = HudKit.Label(head, "Value", "-", 13, SignalGreen,
                                   TextAnchor.MiddleRight, FontStyle.Bold);
        HudKit.AddShadow(_draftValue);
        HudKit.Place((RectTransform)_draftValue.transform, Vector2.zero, Vector2.one,
                     new Vector2(-260f, 0f), new Vector2(-16f, 0f));

        var rule = HudKit.Panel(card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -37f), new Vector2(0f, -34f));

        var bed = HudKit.Panel(card, "DraftBarBed", HudKit.PaneBarBed);
        HudKit.Corner(bed, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(16f, -48f), new Vector2(398f, 16f));
        var fill = HudKit.Panel(bed, "Fill", DraftWeak);
        _draftBar = fill.GetComponent<Image>();
        _draftBar.sprite = HudSprites.White;
        _draftBar.type = Image.Type.Filled;
        _draftBar.fillMethod = Image.FillMethod.Horizontal;
        _draftBar.fillAmount = 0f;
        HudKit.Place(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        _draftHint = HudKit.Label(card, "Hint", "", 13, HudKit.Chalk, TextAnchor.UpperLeft);
        HudKit.AddShadow(_draftHint);
        HudKit.Corner((RectTransform)_draftHint.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(16f, -70f), new Vector2(400f, 20f));

        card.gameObject.SetActive(false);
    }

    /// <summary>"Signal good" green, lifted off the paper-era dark green so it reads on glass.</summary>
    private static readonly Color SignalGreen = new Color(0.545f, 0.859f, 0.588f, 1f);

    // Heart-rate pairing states, mirroring the pairing-screen convention shared by Zwift / Rouvy /
    // Wahoo SYSTM: a dim heart when nothing is attached, a warm pulse while it searches, and a
    // live red heart showing bpm once a strap is talking.
    private static readonly Color HeartOff = new Color(0.66f, 0.64f, 0.61f, 1f);
    private static readonly Color HeartSearch = new Color(0.965f, 0.71f, 0.24f, 1f);
    private static readonly Color HeartLive = new Color(0.949f, 0.29f, 0.30f, 1f);

    // Sand -> pink -> gold, matching the telemetry card's effort bar family.
    private static readonly Color DraftWeak = new Color(0.937f, 0.545f, 0.643f, 1f);
    private static readonly Color DraftPerfect = new Color(0.95f, 0.78f, 0.35f, 1f);

    private void RefreshDraft()
    {
        if (_draftCard == null) return;

        if (_traffic == null && Time.unscaledTime > _draftSeekAt)
        {
            // Cheap and occasional: the director only exists inside the Shiosai region, and it
            // is created/destroyed by RegionDirector, so this cannot be resolved once at Build.
            _draftSeekAt = Time.unscaledTime + 1f;
            _traffic = FindFirstObjectByType<ShiosaiTrafficDirector>(FindObjectsInactive.Exclude);
        }

        bool inTraffic = _traffic != null && _traffic.isActiveAndEnabled;
        float f = inTraffic ? Mathf.Clamp01(_traffic.draftFactor) : 0f;
        if (f > 0.02f) _draftSeenAt = Time.unscaledTime;
        bool show = inTraffic && Time.unscaledTime - _draftSeenAt < CleanAirLingerS;
        if (_draftCard.gameObject.activeSelf != show) _draftCard.gameObject.SetActive(show);
        if (!show) return;

        _draftBar.fillAmount = f;
        _draftBar.color = Color.Lerp(DraftWeak, DraftPerfect, Mathf.InverseLerp(0.7f, 1f, f));

        if (f <= 0.02f)
        {
            _draftValue.text = "CLEAN AIR";
            _draftHint.text = "Close on a rider ahead to pick up their shelter.";
        }
        else
        {
            _draftValue.text = f >= 0.92f ? "PERFECT" : $"{f * 100f:0} %";
            _draftHint.text = _traffic.blocked
                ? $"On the wheel - {_traffic.draftGapM:0.0} m.  D / right to pull out and pass."
                : $"{_traffic.draftGapM:0.0} m back.  Tuck in tighter.";
        }
    }

    private static void MakeStat(RectTransform card, string name, string caption, float x,
                                 out Text value, int size, Color color)
    {
        // 14 px bold, not the original 12 plain: at PanelAlpha a 12 px caption averages into
        // its own bed under anti-aliasing and reads as grey mush over bright blossom.
        var cap = HudKit.Label(card, name + "Caption", caption, 14, HudKit.Chalk,
                               TextAnchor.UpperLeft, FontStyle.Bold);
        // 12 px type never gets an outline - a 1 px 4-copy outline fills its counters. A single
        // offset shadow is the documented treatment for small text on glass.
        HudKit.AddShadow(cap, 0.85f, 1.0f);
        HudKit.Corner((RectTransform)cap.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(x, -52f), new Vector2(110f, 16f));
        value = HudKit.Label(card, name + "Value", "0", size, color,
                             TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.AddOutline(value, 0.85f, 1.3f);
        HudKit.Corner((RectTransform)value.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(x, -70f), new Vector2(120f, size + 12f));
    }

    private void BuildBanner(RectTransform root)
    {
        _bannerCard = GlassCard(root, "Checkpoint Banner");
        HudKit.Corner(_bannerCard, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -30f), new Vector2(660f, 72f));
        var edge = HudKit.Panel(_bannerCard, "Edge Rule", HudKit.Ember);
        HudKit.Place(edge, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(0f, 0f), new Vector2(0f, 3f));

        var sectionBed = HudKit.Panel(_bannerCard, "Section Bed", HudKit.PaneTextBed);
        HudKit.Place(sectionBed, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(6f, -28f), new Vector2(-6f, -4f));

        _bannerSection = HudKit.Label(_bannerCard, "Section", "", 15,
                                      HudKit.Chalk, TextAnchor.UpperCenter);
        HudKit.AddShadow(_bannerSection, 0.85f, 1.0f);
        HudKit.Place((RectTransform)_bannerSection.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -26f), new Vector2(0f, -6f));
        _bannerMain = HudKit.Label(_bannerCard, "Main", "", 24, HudKit.Gold,
                                   TextAnchor.UpperCenter, FontStyle.Bold);
        HudKit.AddOutline(_bannerMain, 0.88f, 1.3f);
        HudKit.Place((RectTransform)_bannerMain.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -62f), new Vector2(0f, -26f));
    }

    private void BuildPlan(RectTransform root)
    {
        var card = GlassCard(root, "Ride Plan Card");
        HudKit.Corner(card, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(26f, 26f), new Vector2(470f, PlanCardHeight));
        _planCard = card;

        var head = HudKit.Panel(card, "Head", HudKit.PaneTextBed);
        HudKit.Place(head, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -38f), Vector2.zero);
        _planTitle = HudKit.Label(head, "Title", "RIDE PLAN", 15, HudKit.Chalk,
                                  TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(_planTitle);
        HudKit.Place((RectTransform)_planTitle.transform, Vector2.zero, Vector2.one,
                     new Vector2(16f, 0f), new Vector2(-150f, 0f));
        _planElapsed = HudKit.Label(head, "Elapsed", "ELAPSED 00:00", 14, HudKit.Chalk,
                                    TextAnchor.MiddleRight, FontStyle.Bold);
        HudKit.AddShadow(_planElapsed);
        HudKit.Place((RectTransform)_planElapsed.transform, Vector2.zero, Vector2.one,
                     new Vector2(-230f, 0f), new Vector2(-16f, 0f));

        var rule = HudKit.Panel(card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -41f), new Vector2(0f, -38f));

        // The laps readout is 16 px light type sitting straight on the pane, over whatever the
        // verge happens to be; it gets the same bed as the rest of the small type.
        var courseBed = HudKit.Panel(card, "Course Bed", HudKit.PaneTextBed);
        HudKit.Corner(courseBed, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(10f, -50f), new Vector2(450f, 30f));

        _planCourse = HudKit.Label(card, "Course", "Sakura Circuit", 20, HudKit.Chalk,
                                   TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.AddOutline(_planCourse, 0.85f, 1.2f);
        HudKit.Corner((RectTransform)_planCourse.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(16f, -52f), new Vector2(280f, 26f));
        _planLaps = HudKit.Label(card, "Laps", "3 laps @ 8.9 min", 16, HudKit.Chalk,
                                 TextAnchor.UpperRight);
        HudKit.AddShadow(_planLaps);
        HudKit.Corner((RectTransform)_planLaps.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-16f, -54f), new Vector2(210f, 22f));

        var bed = HudKit.Panel(card, "ProgressBed", HudKit.PaneBarBed);
        HudKit.Corner(bed, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(16f, -86f), new Vector2(438f, 12f));
        var fill = HudKit.Panel(bed, "Fill", HudKit.Ember);
        HudKit.Place(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _planBar = fill.GetComponent<Image>();
        _planBar.sprite = HudSprites.White;
        _planBar.type = Image.Type.Filled;
        _planBar.fillMethod = Image.FillMethod.Horizontal;
        _planBar.fillAmount = 0f;

        // Live metrics row (user playtest: the card "didn't look useful"). Four columns between
        // the progress bar and the key hints: speed, gradient, distance, next checkpoint.
        _planStatValue = new Text[4];
        _planStatCaption = new Text[4];
        string[] caps = { "SPEED", "GRADE", "DISTANCE", "NEXT CP" };
        // Per-column x / width (QA #2): a fixed 110 px pitch let "13.5/19.0 km" run into NEXT CP.
        // DISTANCE gets the room for "188.8/199.9 km"; GRADE's caption carries "+1234 m".
        float[] colX = { 16f, 122f, 214f, 372f };
        float[] colW = { 100f, 86f, 152f, 82f };
        for (int k = 0; k < 4; k++)
        {
            float x = colX[k];
            var v = HudKit.Label(card, "Stat " + caps[k], "-", 19, HudKit.Chalk,
                                 TextAnchor.UpperLeft, FontStyle.Bold);
            HudKit.AddOutline(v, 0.85f, 1.1f);
            // Safety net for anything still longer than its column: shrink, never overflow.
            v.horizontalOverflow = HorizontalWrapMode.Wrap;
            v.verticalOverflow = VerticalWrapMode.Truncate;
            v.resizeTextForBestFit = true;
            v.resizeTextMinSize = 14;
            v.resizeTextMaxSize = 19;
            HudKit.Corner((RectTransform)v.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                          new Vector2(x, -102f), new Vector2(colW[k], 24f));
            var c = HudKit.Label(card, "Cap " + caps[k], caps[k], 11, HudKit.ChalkSoft,
                                 TextAnchor.UpperLeft, FontStyle.Bold);
            HudKit.AddShadow(c, 0.85f, 1.0f);
            HudKit.Corner((RectTransform)c.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                          new Vector2(x, -126f), new Vector2(colW[k], 14f));
            _planStatValue[k] = v;
            _planStatCaption[k] = c;
        }

        // One 16 px line (QA #5; was two 13 px lines of microtext). It pages from the ride keys
        // to the course keys, then fades out and the card folds up - see RefreshPlanHint.
        _planHintBed = HudKit.Panel(card, "Hint Bed", HudKit.PaneTextBed);
        HudKit.Corner(_planHintBed, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(10f, 14f), new Vector2(450f, 30f));

        _planHint = HudKit.Label(card, "Hint", PlanHintPages[0], 16, HudKit.Chalk,
                                 TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(_planHint, 0.9f, 1.2f);
        _planHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        _planHint.verticalOverflow = VerticalWrapMode.Truncate;
        _planHint.resizeTextForBestFit = true;
        _planHint.resizeTextMinSize = 14;
        _planHint.resizeTextMaxSize = 16;
        HudKit.Corner((RectTransform)_planHint.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(18f, 14f), new Vector2(436f, 30f));
        _planHintPage = 0;
    }

    private const float PlanCardHeight = 200f;
    /// <summary>Height the plan card folds to once the key hint has faded (hint row removed).</summary>
    private const float PlanCardFoldedHeight = 158f;
    private const float PlanHintPageS = 10f;
    private const float PlanHintFadeS = 1.5f;
    private static readonly string[] PlanHintPages =
    {
        "[W] pedal   [S] sprint   [A/D] lane   [E] race   [R] riders   [TAB] map",
        "[1-5] jump  [T] 30/60  [ / ] laps  [M] zoom  [Bksp] restart",
    };

    /// <summary>
    /// Key hint lifecycle, driven off ride time so a restart brings it back: ride keys for the
    /// first page, course keys for the second, then a short fade after which the hint row is
    /// removed and the card folds to its stats.
    /// </summary>
    private void RefreshPlanHint()
    {
        if (_planHint == null) return;
        float t = Mathf.Max(0f, session.ElapsedSeconds);
        float shown = PlanHintPageS * PlanHintPages.Length;
        float a = 1f - Mathf.Clamp01((t - shown) / PlanHintFadeS);
        int page = Mathf.Min(PlanHintPages.Length - 1, Mathf.FloorToInt(t / PlanHintPageS));
        if (page != _planHintPage)
        {
            _planHintPage = page;
            _planHint.text = PlanHintPages[page];
        }
        bool on = a > 0.001f;
        if (_planHint.gameObject.activeSelf != on) _planHint.gameObject.SetActive(on);
        if (_planHintBed.gameObject.activeSelf != on) _planHintBed.gameObject.SetActive(on);
        if (on)
        {
            _planHint.color = HudKit.WithAlpha(HudKit.Chalk, a);
            _planHintBed.GetComponent<Image>().color =
                HudKit.WithAlpha(HudKit.PaneTextBed, HudKit.PaneTextBed.a * a);
        }
        float h = Mathf.Lerp(PlanCardFoldedHeight, PlanCardHeight, a);
        if (!Mathf.Approximately(_planCard.sizeDelta.y, h))
            _planCard.sizeDelta = new Vector2(_planCard.sizeDelta.x, h);
    }

    /// <summary>
    /// Wind indicator (weather system): an arrow that points where the air is going RELATIVE to
    /// the rider (screen up = riding direction; pointing down = headwind), over the true wind
    /// speed at the rider. Sits right of the ride-plan card.
    /// </summary>
    private void BuildWindIndicator(RectTransform root)
    {
        var card = GlassCard(root, "Wind Card");
        HudKit.Corner(card, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(506f, 26f), new Vector2(96f, 110f));
        var pivotGo = new GameObject("Wind Pivot", typeof(RectTransform));
        _windPivot = (RectTransform)pivotGo.transform;
        _windPivot.SetParent(card, false);
        _windPivot.anchorMin = _windPivot.anchorMax = new Vector2(0.5f, 1f);
        _windPivot.anchoredPosition = new Vector2(0f, -42f);
        _windPivot.sizeDelta = new Vector2(60f, 60f);
        _windShaft = HudKit.Panel(_windPivot, "Shaft", HudKit.Chalk).GetComponent<Image>();
        var shaft = (RectTransform)_windShaft.transform;
        shaft.anchorMin = shaft.anchorMax = new Vector2(0.5f, 0.5f);
        shaft.sizeDelta = new Vector2(6f, 40f);
        shaft.anchoredPosition = new Vector2(0f, -4f);
        _windHead = HudKit.Panel(_windPivot, "Head", HudKit.Ember).GetComponent<Image>();
        var head = (RectTransform)_windHead.transform;
        head.anchorMin = head.anchorMax = new Vector2(0.5f, 0.5f);
        head.sizeDelta = new Vector2(18f, 18f);
        head.anchoredPosition = new Vector2(0f, 16f);
        head.localRotation = Quaternion.Euler(0f, 0f, 45f);
        _windValue = HudKit.Label(card, "Wind Value", "-", 15, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_windValue, 0.85f, 1.1f);
        HudKit.Corner((RectTransform)_windValue.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(4f, 6f), new Vector2(88f, 22f));
    }

    private void RefreshWindIndicator()
    {
        if (_windPivot == null) return;
        var wd = WeatherDirector.Instance;
        bool on = wd != null;
        if (_windPivot.parent.gameObject.activeSelf != on) _windPivot.parent.gameObject.SetActive(on);
        if (!on) return;
        var ap = wd.Apparent;
        // True wind in the rider frame (x right, y forward) = -(cross, head).
        var v = new Vector2(-ap.CrosswindMps, -ap.HeadwindMps);
        float kph = v.magnitude * 3.6f;
        float target = kph < 1f ? 0f : -Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
        float cur = _windPivot.localEulerAngles.z;
        _windPivot.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.LerpAngle(cur, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime)));
        float gust = wd.State.GustMps * 3.6f * wd.Exposure;
        _windValue.text = gust >= 2f ? $"{kph:0} +{gust:0}" : $"{kph:0} km/h";
        _windShaft.color = highContrastWind ? Color.yellow : HudKit.Chalk;
        _windHead.color = highContrastWind ? Color.yellow : HudKit.Ember;
    }

    // ================================================================= per frame

    private void Update()
    {
        // While the ride widgets are stashed for map selection, the HUD must not repaint: the
        // per-frame refresh re-activates the checkpoint banner and the draft card by design, and
        // they would otherwise bleed through the selection screen for a ride that has not begun.
        if (_stashed.Count > 0) return;
        Refresh();
    }

    /// <summary>Deterministic refresh so the editor capture harness drives the same code.</summary>
    public void Refresh()
    {
        if (session == null || _canvas == null) return;
        var course = session.Course;
        if (course == null) return;

        RefreshDraft();

        if (devices != null)
        {
            var tm = devices.Telemetry;
            // The compact pill carries the unit with the number (it has no captions); the
            // classic card keeps bare numbers under its POWER / CADENCE captions.
            if (_power != null)
                _power.text = compactHud ? $"{tm.Watts:0} W" : tm.Watts.ToString("0");
            if (_cadence != null)
                _cadence.text = compactHud ? $"{tm.CadenceRpm:0} RPM" : tm.CadenceRpm.ToString("0");
            if (_speed != null) _speed.text = session.SpeedKph.ToString("0.0");
            // A configured-but-unconnected strap shows "--" rather than a plausible simulated
            // number, so live testing gives honest feedback about whether the TICKR is talking.
            bool bleHr = devices.heartRate == DeviceManager.HeartRateSourceKind.BluetoothLe;
            if (_heart != null)
                _heart.text = (bleHr && !devices.HeartRateConnected)
                    ? "--"
                    : tm.HeartRateBpm.ToString("0");
            float ftpPct = devices.ftpWatts > 1f ? tm.Watts / devices.ftpWatts : 0f;
            float wkg = devices.riderMassKg > 1f ? tm.Watts / devices.riderMassKg : 0f;
            if (_effort != null)
                _effort.text = $"{ftpPct * 100f:0} % FTP   -   {wkg:0.0} W/kg   -   {devices.SourceLabel}";
            if (_effortBar != null) _effortBar.fillAmount = Mathf.Clamp01(ftpPct / 1.5f);
            string hrTag = bleHr ? (devices.HeartRateConnected ? "HR OK" : "HR --") : "HR .";
            if (_source != null)
                _source.text = devices.Active.IsConnected ? $"TRAINER . {hrTag}" : "NO SIGNAL";
        }

        LayoutPill();
        RefreshRouteBar(course);

        if (director != null && _bannerCard != null)
        {
            bool show = director.BannerVisible;
            _bannerCard.gameObject.SetActive(show || director.HasNext);
            _bannerSection.text =
                $"{director.SectionName.ToUpperInvariant()}   .   KM {session.DistanceM / 1000f:0.00} / " +
                $"{course.Length / 1000f:0.00}";
            _bannerMain.text = show
                ? director.BannerText
                : director.HasNext
                    ? $"NEXT CHECKPOINT - {RouteNames.Checkpoint(director.NextCheckpoint.Name)}   .   {director.MetresToNext:0} m"
                    : "";
        }

        // Compact wind readout (same data as the physics): sustained+gust, and where it hits you.
        var wd = WeatherDirector.Instance;
        if (wd != null)
        {
            var ap = wd.Apparent;
            float trueKph = new Vector2(ap.HeadwindMps, ap.CrosswindMps).magnitude * 3.6f;
            string kind = trueKph < 3f ? "CALM"
                : Mathf.Abs(ap.CrosswindMps) > Mathf.Abs(ap.HeadwindMps)
                    ? (ap.CrosswindMps > 0f ? "CROSS R" : "CROSS L")
                    : (ap.HeadwindMps > 0f ? "HEAD" : "TAIL");
            float gustKph = wd.State.GustMps * 3.6f * wd.Exposure;
            _planTitle.text = $"WIND {trueKph:0} km/h {kind}" + (gustKph >= 2f ? $"  +{gustKph:0}" : "");
        }
        else _planTitle.text = $"RIDE PLAN  .  {session.targetDurationMinutes:0} MIN";
        int mins = Mathf.FloorToInt(session.ElapsedSeconds / 60f);
        int secs = Mathf.FloorToInt(session.ElapsedSeconds % 60f);
        _planElapsed.text = $"ELAPSED {mins:00}:{secs:00}";
        _planCourse.text = RouteNames.Place(course.DisplayName);
        _planLaps.text = $"{session.TotalLaps} lap{(session.TotalLaps == 1 ? "" : "s")} @ " +
                         $"{session.EstimateLapMinutes():0.0} min";
        _planBar.fillAmount = session.NormalizedProgress;
        RefreshPlanHint();
        if (_planStatValue != null)
        {
            _planStatValue[0].text = $"{session.SpeedKph:0.0} km/h";
            float g = session.DisplayGradePct;
            _planStatValue[1].text = $"{(g > 0.05f ? "+" : "")}{g:0.0}%";
            _planStatValue[2].text = $"{session.DistanceM / 1000f:0.0}/{course.Length / 1000f:0.0} km";
            _planStatValue[3].text = director != null && director.HasNext
                ? (director.MetresToNext >= 1000f ? $"{director.MetresToNext / 1000f:0.0} km"
                                                  : $"{director.MetresToNext:0} m")
                : "FINISH";
            _planStatCaption[1].text = $"GRADE  .  +{session.AscentM:0} m";
        }

        if (map != null) map.session = session;
        if (worldMap != null) worldMap.session = session;

        RefreshWindIndicator();
        RefreshHeartRate();
    }

    // ================================================================= heart-rate pairing

    /// <summary>
    /// The "add a heart-rate monitor" control: a compact glass tile with a heart logo, pinned
    /// top-right under the World Map button. Tapping it starts a Bluetooth scan and opens a small
    /// pairing popover - the same one-tap-to-search flow Zwift / Rouvy use for their HR tile.
    /// </summary>
    private void BuildHeartRateButton(RectTransform root)
    {
        var card = HudKit.SoftPanel(root, "Heart Rate Button", HudKit.TopRowPane);
        HudKit.Corner(card, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-26f, -100f), new Vector2(168f, 60f));
        var img = card.GetComponent<Image>();
        img.raycastTarget = true;
        var frame = HudKit.SoftPanel(card, "Edge", HudKit.GlassEdge, frame: true);
        HudKit.Place(frame, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var rule = HudKit.Panel(card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(0f, 0f), new Vector2(0f, 3f));

        var heartGo = HudKit.Rect(card, "Heart");
        _hrHeart = heartGo.gameObject.AddComponent<Image>();
        _hrHeart.sprite = HudSprites.Heart;
        _hrHeart.color = HeartOff;
        _hrHeart.raycastTarget = false;
        HudKit.Corner(heartGo, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                      new Vector2(16f, 2f), new Vector2(34f, 32f));

        _hrTileLabel = HudKit.Label(card, "Label", "ADD HR", 18, HudKit.Chalk,
                                    TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddOutline(_hrTileLabel, 0.85f, 1.2f);
        HudKit.Place((RectTransform)_hrTileLabel.transform, Vector2.zero, Vector2.one,
                     new Vector2(60f, 0f), new Vector2(-10f, 0f));

        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(OnHeartRateClicked);
        _hrButton = button;

        BuildHeartRatePanel(root);
    }

    /// <summary>The pairing popover: live status while searching / once connected, plus guidance.</summary>
    private void BuildHeartRatePanel(RectTransform root)
    {
        var card = GlassCard(root, "Heart Rate Panel");
        HudKit.Corner(card, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-26f, -168f), new Vector2(360f, 224f));
        _hrPanel = card;

        var head = HudKit.Panel(card, "Head", HudKit.PaneTextBed);
        HudKit.Place(head, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -34f), Vector2.zero);
        var title = HudKit.Label(head, "Title", "HEART RATE MONITOR", 15, HudKit.Chalk,
                                 TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(title);
        HudKit.Place((RectTransform)title.transform, Vector2.zero, Vector2.one,
                     new Vector2(16f, 0f), new Vector2(-44f, 0f));
        var rule = HudKit.Panel(card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -37f), new Vector2(0f, -34f));

        // A small close affordance in the header corner.
        var close = HudKit.Panel(head, "Close", HudKit.PaneTextBed);
        HudKit.Corner(close, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                      new Vector2(-6f, 0f), new Vector2(26f, 22f));
        var closeLabel = HudKit.Label(close, "X", "X", 15, HudKit.Chalk,
                                      TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.Place((RectTransform)closeLabel.transform, Vector2.zero, Vector2.one,
                     Vector2.zero, Vector2.zero);
        var closeBtn = close.gameObject.AddComponent<Button>();
        var closeImg = close.GetComponent<Image>();
        closeImg.raycastTarget = true;
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(() => SetHeartRatePanelOpen(false));

        _hrPanelStatus = HudKit.Label(card, "Status", "", 16, HudKit.Gold,
                                      TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.AddShadow(_hrPanelStatus, 0.85f, 1.0f);
        HudKit.Corner((RectTransform)_hrPanelStatus.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(16f, -46f), new Vector2(330f, 24f));

        _hrPanelHint = HudKit.Label(card, "Hint", "", 13, HudKit.ChalkSoft, TextAnchor.UpperLeft);
        HudKit.AddShadow(_hrPanelHint, 0.85f, 1.0f);
        HudKit.Corner((RectTransform)_hrPanelHint.transform,
                      new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(16f, -76f), new Vector2(330f, 136f));
        _hrPanelHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        _hrPanelHint.verticalOverflow = VerticalWrapMode.Truncate;

        card.gameObject.SetActive(false);
    }

    private void OnHeartRateClicked()
    {
        if (devices == null) return;

        // One tap arms the BLE strap and (re)scans - the pairing-screen idiom. Switching the
        // source here means the player never has to touch the inspector to add a monitor.
        if (devices.heartRate != DeviceManager.HeartRateSourceKind.BluetoothLe)
            devices.heartRate = DeviceManager.HeartRateSourceKind.BluetoothLe;
        devices.RestartHeartRate();
        SetHeartRatePanelOpen(true);
    }

    private void SetHeartRatePanelOpen(bool open)
    {
        _hrPanelOpen = open;
        if (_hrPanel != null) _hrPanel.gameObject.SetActive(open);
    }

    private void RefreshHeartRate()
    {
        if (_hrButton == null || devices == null) return;

        bool ble = devices.heartRate == DeviceManager.HeartRateSourceKind.BluetoothLe;
        bool connected = devices.HeartRateConnected;
        int bpm = Mathf.RoundToInt(devices.Telemetry.HeartRateBpm);
        _hrPulse += Time.unscaledDeltaTime;

        string status, hint;
        if (!ble)
        {
            _hrHeart.color = HeartOff;
            _hrHeart.rectTransform.localScale = Vector3.one;
            _hrTileLabel.text = "ADD HR";
            _hrTileLabel.color = HudKit.ChalkSoft;
            status = "Not connected";
            hint = "Tap to search for a Bluetooth heart-rate monitor such as the Wahoo TICKR. " +
                   "Wake the strap and make sure the PC's Bluetooth is on.";
        }
        else if (connected)
        {
            // A subtle lub-dub beat at the measured rate - the heart quite literally beats.
            _beatPhase += Time.unscaledDeltaTime * Mathf.Max(40f, bpm) / 60f;
            float t = _beatPhase - Mathf.Floor(_beatPhase);
            float beat = Mathf.Exp(-26f * t * t) + 0.55f * Mathf.Exp(-26f * (t - 0.20f) * (t - 0.20f));
            float s = 1f + 0.16f * Mathf.Clamp01(beat);
            _hrHeart.color = HeartLive;
            _hrHeart.rectTransform.localScale = new Vector3(s, s, 1f);
            _hrTileLabel.text = $"{bpm} BPM";
            _hrTileLabel.color = HudKit.Chalk;
            status = $"Connected  .  {bpm} bpm";
            hint = devices.HeartRateLabel + "\nTap to reconnect if the signal drops.";
        }
        else
        {
            // Warm pulsing heart while scanning.
            float p = 0.55f + 0.45f * Mathf.Sin(_hrPulse * 6f);
            _hrHeart.color = Color.Lerp(HeartOff, HeartSearch, p);
            _hrHeart.rectTransform.localScale = Vector3.one;
            if (!DeviceManager.HeartRateTransportAvailable)
            {
                // Honest state for a default build: the strap can't connect until the transport
                // is compiled in, so don't imply we're still looking.
                _hrHeart.color = HeartOff;
                _hrTileLabel.text = "NO BLE";
                _hrTileLabel.color = HudKit.ChalkSoft;
                status = "Bluetooth not available";
                hint = "The Bluetooth heart-rate transport is Windows-only, and this build is " +
                       "not on Windows. The HUD shows simulated heart rate here.";
            }
            else
            {
                _hrTileLabel.text = "SEARCHING";
                _hrTileLabel.color = HeartSearch;
                status = "Searching\u2026";
                string diag = devices.HeartRateDiagnostics;
                hint = "Looking for '" + (string.IsNullOrEmpty(devices.heartRateDeviceName)
                            ? "any HR strap" : devices.heartRateDeviceName) +
                       "'. Wake the strap and keep it close; if it's paired to your phone/Wahoo " +
                       "app or another app, close that so it can connect here.";
                if (!string.IsNullOrEmpty(diag)) hint += "\n" + diag;
            }
        }

        if (_hrPanelOpen && _hrPanel != null)
        {
            _hrPanelStatus.text = status;
            _hrPanelStatus.color = connected ? SignalGreen : (ble ? HeartSearch : HudKit.ChalkSoft);
            _hrPanelHint.text = hint;
        }
    }
}

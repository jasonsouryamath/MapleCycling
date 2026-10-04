using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The World Map: a ride-HUD button that opens a full-screen fast-travel overlay drawn on the
/// shipped world-map art, with one clickable pin per region.
///
/// Built in code into the SAME canvas as the rest of the ride HUD (see <see cref="RideHud"/>),
/// so the setup pass stays idempotent and the editor capture harness renders the REAL widgets
/// rather than a mock-up. Clicking an unlocked pin calls <see cref="RegionDirector.FastTravel"/>;
/// locked pins are drawn greyed and do nothing.
/// </summary>
[DefaultExecutionOrder(101)]
public class WorldMapHud : MonoBehaviour
{
    public const string OverlayName = "World Map Overlay";
    public const string ButtonName = "World Map Button";

    [Header("Wiring")]
    public RideSession session;
    public RegionDirector regions;

    [Header("Keys (provisional)")]
    public KeyCode openKey = KeyCode.Tab;
    public KeyCode closeKey = KeyCode.Escape;

    [Header("State")]
    public bool isOpen;

    [Tooltip("SELECTION MODE: the overlay is the opening map-choice screen rather than an " +
             "in-ride fast-travel panel. Set by MapleRideFlowDirector - it retitles the panel, " +
             "hides CLOSE (there is nothing to go back to) and surrenders TAB/ESC to the flow.")]
    public bool selectionMode;

    // Boot uses the same map widgets before any ride systems or environment exist.
    public System.Func<RegionCatalog.Region, bool> destinationAvailable;
    public System.Action<RegionCatalog.Region> destinationSelected;

    private RectTransform _overlay;
    private RectTransform _mapFrame;
    private Text _status;
    private Text _subtitle;
    private RectTransform _close;
    private readonly List<PinView> _pins = new List<PinView>();

    private sealed class PinView
    {
        public RegionCatalog.Region region;
        public Vector2 uv;            // pin position on the art, 0..1 (bottom-left origin)
        public RectTransform holder;
        public RectTransform card;
        public Text name;
        public Text sub;
        public Image dot;
        public Image ring;
        public bool flipped;
    }

    // ---- PAN / ZOOM (World redraw M3). The v2 relief map is a vast 380 x 250 km world, so the
    // overlay is a camera over it: the art is laid out by (_center, _zoom) and every pin is
    // re-anchored from its art uv, so pins and live labels stay screen-sized at every zoom.
    public const float MinZoom = 1f;
    public const float MaxZoom = 5f;
    private RectTransform _art;
    private float _zoom = 1f, _zoomGoal = 1f;
    private Vector2 _center = new Vector2(0.5f, 0.5f), _centerGoal = new Vector2(0.5f, 0.5f);

    /// <summary>Current zoom (1 = the whole world covers the screen).</summary>
    public float Zoom => _zoom;
    /// <summary>Art uv (0..1, bottom-left origin) at the centre of the view.</summary>
    public Vector2 ViewCenter => _center;

    // KEYBOARD SELECTION (user 2026-09-26: "stuck / unresponsive"). The title says PRESS ENTER,
    // but the map was mouse-only, so a keyboard/trainer player could not pick a map at all.
    // Arrows / WASD move a focus between UNLOCKED pins, ENTER / SPACE confirms.
    private int _focus = -1;
    private int _openedFrame = -1;
    private readonly List<WorldMapPinHover> _hovers = new List<WorldMapPinHover>();

    public RectTransform Overlay => _overlay;

    /// <summary>Region id of the pin holding keyboard focus ("" when none), for the flow harness.</summary>
    public string FocusedRegionId => _focus >= 0 && _focus < _pins.Count ? _pins[_focus].region.Id : "";

    // ================================================================= construction

    /// <summary>Builds the button and the (closed) overlay under the ride HUD canvas root.</summary>
    public void Build(RectTransform hudRoot)
    {
        // Converge, never accumulate: drop any previous instance of either widget by exact name.
        foreach (var n in new[] { OverlayName, ButtonName })
        {
            var old = hudRoot.Find(n);
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }
        }
        _pins.Clear();
        _hovers.Clear();
        _focus = -1;
        _zoom = _zoomGoal = 1f;
        _center = _centerGoal = new Vector2(0.5f, 0.5f);

        BuildButton(hudRoot);
        BuildOverlay(hudRoot);
        SetOpen(false);
    }

    private void BuildButton(RectTransform hudRoot)
    {
        // Smoked glass, but at the heavier top-row alpha: this button sits top-right over open
        // sky and at PanelAlpha its caption all but vanished there (QA #4).
        var card = HudKit.SoftPanel(hudRoot, ButtonName, HudKit.TopRowPane);
        HudKit.Corner(card, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-26f, -26f), new Vector2(250f, 64f));
        var img = card.GetComponent<Image>();
        img.raycastTarget = true;
        var frame = HudKit.SoftPanel(card, "Edge", HudKit.GlassEdge, frame: true);
        HudKit.Place(frame, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var rule = HudKit.Panel(card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(0f, 0f), new Vector2(0f, 3f));

        var label = HudKit.Label(card, "Label", "WORLD MAP", 20, HudKit.Chalk,
                                 TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(label, 0.85f, 1.2f);
        HudKit.Place((RectTransform)label.transform, Vector2.zero, Vector2.one,
                     new Vector2(0f, 26f), new Vector2(0f, 0f));
        var hintBed = HudKit.Panel(card, "Hint Bed", HudKit.PaneTextBed);
        HudKit.Place(hintBed, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(6f, 4f), new Vector2(-6f, 26f));
        // 16 px bold with an outline (was 13 px regular + shadow, unreadable over sky).
        var hint = HudKit.Label(card, "Hint", "FAST TRAVEL  .  [TAB]", 16, HudKit.Chalk,
                                TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(hint, 0.85f, 1.3f);
        HudKit.Place((RectTransform)hint.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(0f, 4f), new Vector2(0f, 26f));

        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(() => SetOpen(!isOpen));
    }

    private void BuildOverlay(RectTransform hudRoot)
    {
        // OPAQUE: this is the map-selection screen, so nothing of the frozen 3D ride behind it
        // (the Sakura scene, the speed gauge) should read through it.
        _overlay = HudKit.Panel(hudRoot, OverlayName, new Color(0.05f, 0.06f, 0.09f, 1f));
        HudKit.Place(_overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _overlay.GetComponent<Image>().raycastTarget = true;   // eats clicks behind the map
        _overlay.SetAsLastSibling();

        var plate = HudKit.Panel(_overlay, "Title Plate", new Color(0.03f, 0.04f, 0.06f, 0.96f));
        HudKit.Corner(plate, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(36f, -22f), new Vector2(800f, 82f));
        plate.GetComponent<Image>().raycastTarget = false;

        var title = HudKit.Label(_overlay, "Title", "WORLD MAP", 34, HudKit.PaperSolid,
                                 TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.AddOutline(title, 0.95f, 2f);
        HudKit.Corner((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(52f, -30f), new Vector2(520f, 42f));
        var sub = HudKit.Label(_overlay, "Subtitle",
                               "SELECT A REGION TO FAST TRAVEL   .   [ESC] CLOSE", 16,
                               new Color(0.80f, 0.78f, 0.74f, 1f));
        HudKit.AddShadow(sub, 0.9f, 1.4f);
        HudKit.Corner((RectTransform)sub.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                      new Vector2(54f, -72f), new Vector2(720f, 22f));
        _subtitle = sub;

        // No bottom status line (user 2026-09-26: the gold "YOU ARE HERE ... KM LAP" text sat on
        // the map legend and told the player nothing the highlighted pin does not). _status stays
        // null and every write to it is null-guarded.
        _status = null;

        // close button
        var close = HudKit.Panel(_overlay, "Close", HudKit.Paper);
        _close = close;
        HudKit.Corner(close, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-52f, -30f), new Vector2(150f, 46f));
        var closeLabel = HudKit.Label(close, "Label", "CLOSE  [ESC]", 16, HudKit.Ink,
                                      TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.Place((RectTransform)closeLabel.transform, Vector2.zero, Vector2.one,
                     Vector2.zero, Vector2.zero);
        var closeButton = close.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = close.GetComponent<Image>();
        closeButton.onClick.AddListener(() => SetOpen(false));

        // ---- the map art -----------------------------------------------------------
        _mapFrame = HudKit.Rect(_overlay, "Map");
        var tex = Resources.Load<Texture2D>(RegionCatalog.MapTextureResource);
        float aspect = tex != null ? (float)tex.width / tex.height : 1.5f;

        // The map IS the selection screen, so it fills the whole overlay edge-to-edge instead of
        // sitting in a centred card with the frozen world showing around it. An AspectRatioFitter
        // in EnvelopeParent mode covers the screen while preserving the art's aspect (a "cover"
        // fit), so the map never distorts and never letterboxes at any window shape.
        _mapFrame.anchorMin = _mapFrame.anchorMax = new Vector2(0.5f, 0.5f);
        _mapFrame.pivot = new Vector2(0.5f, 0.5f);
        _mapFrame.anchoredPosition = Vector2.zero;
        var fitter = _mapFrame.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = aspect;

        var mapImage = HudKit.Panel(_mapFrame, "Art", Color.white);
        HudKit.Place(mapImage, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _art = mapImage;
        var mi = mapImage.GetComponent<Image>();
        mi.raycastTarget = false;   // drags/scrolls land on the overlay's WorldMapPanZoom
        if (tex != null)
        {
            mi.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                      new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            mi.color = Color.white;
        }
        else
        {
            // The overlay must still be usable (and reviewable) without the art.
            mi.color = HudKit.MapBed;
            Debug.LogWarning($"[worldmap] Resources/{RegionCatalog.MapTextureResource} not found - " +
                             "drawing pins on a blank bed.");
        }

        foreach (var region in RegionCatalog.Regions) BuildPin(region);

        var panZoom = _overlay.gameObject.AddComponent<WorldMapPanZoom>();
        panZoom.map = this;
        ApplyView();

        // The full-screen map is built last but must sit BEHIND the title band, the status line
        // and the CLOSE affordance, which stay legible on top of the art.
        _mapFrame.SetAsFirstSibling();
    }

    private void BuildPin(RegionCatalog.Region region)
    {
        // Catalog unlocks are only a presentation hint.  The route graph is the source of
        // truth for whether a destination can actually be loaded; keeping a pin clickable
        // when the graph is stale makes travel look like a dead button.
        bool unlocked = IsTravelable(region);

        var holder = HudKit.Rect(_mapFrame, "Pin " + region.Id);
        Vector2 uv = RegionCatalog.PinFor(region);
        holder.anchorMin = holder.anchorMax = uv;
        holder.pivot = new Vector2(0.5f, 0.5f);
        holder.anchoredPosition = Vector2.zero;
        holder.sizeDelta = unlocked ? new Vector2(34f, 34f) : new Vector2(24f, 24f);

        var ring = HudKit.Panel(holder, "Ring", HudKit.Gold);
        HudKit.Place(ring, Vector2.zero, Vector2.one, new Vector2(-9f, -9f), new Vector2(9f, 9f));
        var ringImg = ring.GetComponent<Image>();
        ringImg.sprite = HudSprites.Ring;
        ringImg.raycastTarget = false;
        ring.gameObject.SetActive(false);

        var dot = HudKit.Panel(holder, "Dot",
                               unlocked ? HudKit.Vermilion : new Color(0.45f, 0.45f, 0.48f, 0.92f));
        HudKit.Place(dot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var dotImg = dot.GetComponent<Image>();
        dotImg.sprite = HudSprites.Disc;
        dotImg.raycastTarget = true;

        var inner = HudKit.Panel(dot, "Inner", HudKit.PaperSolid);
        float inset = unlocked ? 11f : 8f;
        HudKit.Place(inner, Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset));
        var innerImg = inner.GetComponent<Image>();
        innerImg.sprite = HudSprites.Disc;
        innerImg.raycastTarget = false;
        innerImg.color = unlocked ? HudKit.PaperSolid : new Color(0.72f, 0.72f, 0.74f, 0.9f);

        // LIVE LABEL CARD. The v2 relief art carries NO painted text (v1's baked labels are what
        // made the old live labels read as doubled text), so the name, tagline and elevation are
        // drawn here - one copy, on a smoked bed so they stay legible over busy terrain. The
        // card is also the click target for the region, like the name was on v1.
        var card = HudKit.Panel(holder, "Card", unlocked ? new Color(0.02f, 0.03f, 0.045f, 0.80f)
                                                         : new Color(0.02f, 0.03f, 0.045f, 0.68f));
        card.GetComponent<Image>().raycastTarget = true;
        var nameText = HudKit.Label(card, "Name", region.DisplayName.ToUpperInvariant(),
                                    unlocked ? 21 : 17,
                                    unlocked ? HudKit.Chalk : new Color(0.80f, 0.80f, 0.82f, 1f),
                                    TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.AddOutline(nameText, 0.8f, 1.1f);
        HudKit.Place((RectTransform)nameText.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(10f, unlocked ? -29f : -24f), new Vector2(-8f, -3f));
        string subLine = (region.Tagline ?? "").ToUpperInvariant();
        if (unlocked && !string.IsNullOrEmpty(region.Subtitle)) subLine += "   " + region.Subtitle.ToUpperInvariant();
        if (!unlocked) subLine += "   .   LOCKED";
        var subText = HudKit.Label(card, "Sub", subLine, unlocked ? 14 : 13,
                                   unlocked ? new Color(0.95f, 0.80f, 0.62f, 1f) : new Color(0.66f, 0.67f, 0.70f, 1f),
                                   TextAnchor.LowerLeft, FontStyle.Bold);
        HudKit.Place((RectTransform)subText.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(10f, 4f), new Vector2(-8f, 22f));
        foreach (var t in new[] { nameText, subText }) t.raycastTarget = false;
        float cardW = Mathf.Max(nameText.preferredWidth, subText.preferredWidth) + 22f;
        cardW = Mathf.Clamp(cardW, 120f, 340f);
        var pv = new PinView
        {
            region = region, uv = uv, holder = holder, card = card, name = nameText, sub = subText,
            dot = dotImg, ring = ringImg,
        };
        card.sizeDelta = new Vector2(cardW, unlocked ? 56f : 48f);
        LayoutCard(pv, false);

        if (unlocked)
        {
            var pinButton = dot.gameObject.AddComponent<Button>();
            pinButton.targetGraphic = dotImg;
            pinButton.onClick.AddListener(() => Travel(region));
            var cardButton = card.gameObject.AddComponent<Button>();
            cardButton.targetGraphic = card.GetComponent<Image>();
            cardButton.transition = Selectable.Transition.None;   // the pin's hover glow is the feedback
            cardButton.onClick.AddListener(() => Travel(region));
        }

        _pins.Add(pv);
        _hovers.Add(null);

        // Hover feedback: swelling point + glowing ring while the pointer is over the pin. Lives
        // on the holder so hovering either the point or its name card lights the same pin.
        var hover = holder.gameObject.AddComponent<WorldMapPinHover>();
        hover.target = holder;
        hover.glow = ringImg;
        _hovers[_hovers.Count - 1] = hover;
    }

    /// <summary>Puts the label card right of the pin, or left of it near the right screen edge.</summary>
    private static void LayoutCard(PinView p, bool left)
    {
        p.flipped = left;
        var size = p.card.sizeDelta;
        float gap = p.region.Unlocked ? 24f : 18f;
        HudKit.Corner(p.card, new Vector2(0.5f, 0.5f), new Vector2(left ? 1f : 0f, 0.5f),
                      new Vector2(left ? -gap : gap, 0f), size);
        var a = left ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
        p.name.alignment = a;
        p.sub.alignment = left ? TextAnchor.LowerRight : TextAnchor.LowerLeft;
    }

    // ================================================================= pan / zoom

    /// <summary>Jumps (or eases, when <paramref name="instant"/> is false) the view to a centre and zoom.</summary>
    public void SetView(Vector2 center, float zoom, bool instant = true)
    {
        _zoomGoal = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        _centerGoal = ClampCenter(center, _zoomGoal);
        if (instant) { _zoom = _zoomGoal; _center = _centerGoal; }
        ApplyView();
    }

    /// <summary>Zooms by <paramref name="factor"/> keeping the art point under <paramref name="frameFrac"/> fixed.</summary>
    public void ZoomAbout(Vector2 frameFrac, float factor, bool instant = false)
    {
        Vector2 uv = _centerGoal + (frameFrac - new Vector2(0.5f, 0.5f)) / _zoomGoal;
        float z = Mathf.Clamp(_zoomGoal * factor, MinZoom, MaxZoom);
        SetView(uv - (frameFrac - new Vector2(0.5f, 0.5f)) / z, z, instant);
    }

    /// <summary>Pans by a delta measured in map-frame fractions (drag).</summary>
    public void PanByFrame(Vector2 frameDelta)
    {
        _centerGoal = ClampCenter(_centerGoal - frameDelta / _zoomGoal, _zoomGoal);
        _center = ClampCenter(_center - frameDelta / _zoom, _zoom);
        ApplyView();
    }

    /// <summary>Screen point -> fraction of the map frame (0..1), for the pan/zoom input.</summary>
    public bool ScreenToFrame(Vector2 screen, Camera cam, out Vector2 frac)
    {
        frac = new Vector2(0.5f, 0.5f);
        if (_mapFrame == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapFrame, screen, cam, out var local))
            return false;
        var r = _mapFrame.rect;
        if (r.width < 1f || r.height < 1f) return false;
        frac = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        return true;
    }

    private static Vector2 ClampCenter(Vector2 c, float zoom)
    {
        float h = 0.5f / zoom;
        return new Vector2(Mathf.Clamp(c.x, h, 1f - h), Mathf.Clamp(c.y, h, 1f - h));
    }

    /// <summary>Lays the art and every pin out for the current (_center, _zoom).</summary>
    private void ApplyView()
    {
        if (_art == null) return;
        var half = new Vector2(0.5f, 0.5f);
        _art.anchorMin = half - _center * _zoom;
        _art.anchorMax = half + (Vector2.one - _center) * _zoom;
        _art.offsetMin = _art.offsetMax = Vector2.zero;
        foreach (var p in _pins)
        {
            if (p.holder == null) continue;
            Vector2 a = half + (p.uv - _center) * _zoom;
            p.holder.anchorMin = p.holder.anchorMax = a;
            p.holder.anchoredPosition = Vector2.zero;
            bool visible = a.x > -0.01f && a.x < 1.01f && a.y > -0.01f && a.y < 1.01f;
            if (p.holder.gameObject.activeSelf != visible) p.holder.gameObject.SetActive(visible);
            bool left = a.x > 0.80f;
            if (left != p.flipped) LayoutCard(p, left);
        }
    }

    /// <summary>Eases the view so the pin is comfortably on screen (keyboard focus).</summary>
    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _pins.Count) return;
        Vector2 a = new Vector2(0.5f, 0.5f) + (_pins[index].uv - _centerGoal) * _zoomGoal;
        // The frame is an envelope ("cover") fit, so ~5 % of it is off-screen top and bottom.
        if (a.x < 0.12f || a.x > 0.88f || a.y < 0.16f || a.y > 0.84f)
            SetView(_pins[index].uv, _zoomGoal, instant: false);
    }

    private void SetFocus(int index)
    {
        _focus = index;
        EnsureVisible(index);
        for (int i = 0; i < _hovers.Count; i++)
        {
            if (_hovers[i] == null) continue;
            bool f = i == _focus;
            if (_hovers[i].focused && !f && _pins[i].ring != null)
                _pins[i].ring.gameObject.SetActive(regions != null && _pins[i].region.Id == regions.currentRegionId);
            _hovers[i].focused = f;
        }
    }

    /// <summary>Moves the focus to the unlocked pin nearest along the pressed direction.</summary>
    private void MoveFocus(Vector2 dir)
    {
        if (_pins.Count == 0) return;
        if (_focus < 0) { SetFocus(DefaultFocus()); return; }
        Vector2 from = _pins[_focus].uv;
        int best = -1; float bestScore = float.MaxValue;
        for (int i = 0; i < _pins.Count; i++)
        {
            if (i == _focus || !IsTravelable(_pins[i].region)) continue;
            Vector2 d = _pins[i].uv - from;
            if (Vector2.Dot(d, dir) <= 0.01f) continue;
            float score = d.magnitude + Mathf.Abs(d.x * dir.y - d.y * dir.x) * 2f;   // favour straight ahead
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) SetFocus(best);
    }

    private int DefaultFocus()
    {
        string current = regions != null ? regions.currentRegionId : RegionCatalog.SakuraPass;
        int first = -1;
        for (int i = 0; i < _pins.Count; i++)
        {
            if (!IsTravelable(_pins[i].region)) continue;
            if (_pins[i].region.Id == current) return i;
            if (first < 0) first = i;
        }
        return first;
    }

    // ================================================================= behaviour

    public void Travel(RegionCatalog.Region region)
    {
        if (!IsTravelable(region)) return;
        if (destinationSelected != null)
        {
            destinationSelected(region);
            return;
        }
        if (UsesSceneTravel)
        {
            SetOpen(false);
            MapleRideBoot.TravelTo(region);
            return;
        }
        if (regions == null)
        {
            Debug.LogWarning("[worldmap] no RegionDirector wired.");
            return;
        }
        if (regions.FastTravel(region.Id))
        {
            SetOpen(false);
            Refresh();
        }
        else if (_status != null)
        {
            _status.text = $"{region.DisplayName.ToUpperInvariant()} IS NOT OPEN YET.";
        }
        else
        {
            Debug.LogWarning($"[worldmap] travel to '{region.DisplayName}' was rejected by the route graph.");
        }
    }

    // Streamed payload validation must exercise the player travel path even in batch mode.
    // Only nonstreamed authoring diagnostics retain direct RegionDirector.FastTravel.
    private static bool UsesSceneTravel =>
        MapleRideTitleScreen.PresentationEnabled ||
        (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path ?? "").StartsWith(
            "Assets/Scenes/Playable/", System.StringComparison.Ordinal) ||
        FindAnyObjectByType<RegionSceneStreamer>() != null;

    private bool IsTravelable(RegionCatalog.Region region)
    {
        return region != null && region.Unlocked &&
            (destinationAvailable != null ? destinationAvailable(region) :
             regions != null && regions.CanTravelTo(region.Id) &&
             (!UsesSceneTravel ||
              Application.CanStreamedLevelBeLoaded(MapleRideBoot.RegionScenePath(region.Id))));
    }

    public void SetOpen(bool open)
    {
        isOpen = open;
        if (_overlay != null)
        {
            _overlay.gameObject.SetActive(open);
            if (open) _overlay.SetAsLastSibling();
        }
        ApplyPresentation();
        if (open)
        {
            SetView(new Vector2(0.5f, 0.5f), MinZoom);   // always open on the whole world
            Refresh();
            // The ENTER that closed the title is still "down" this frame: never let it confirm.
            _openedFrame = Time.frameCount;
            SetFocus(DefaultFocus());
        }
        else SetFocus(-1);
    }

    /// <summary>
    /// Retitles the overlay for whichever job it is doing. Idempotent and driven off
    /// <see cref="selectionMode"/>, so the same widgets serve the opening map choice and the
    /// in-ride fast-travel panel instead of a second map being built for one of them.
    /// </summary>
    public void ApplyPresentation()
    {
        if (_subtitle != null)
            _subtitle.text = selectionMode
                ? "CHOOSE YOUR MAP  .  ARROWS MOVE  .  ENTER RIDE  .  SCROLL / + - ZOOM  .  DRAG PAN"
                : "ARROWS MOVE  .  ENTER TRAVEL  .  SCROLL / + - ZOOM  .  DRAG PAN  .  ESC CLOSE";
        // There is nothing behind the selection screen to close back to - the ride has not
        // started yet - so the CLOSE affordance would be a dead end.
        if (_close != null) _close.gameObject.SetActive(!selectionMode);
    }

    /// <summary>Repaints the current-region highlight and the status line.</summary>
    public void Refresh()
    {
        string current = regions != null ? regions.currentRegionId : "";
        foreach (var p in _pins)
        {
            if (p.ring != null) p.ring.gameObject.SetActive(p.region.Id == current);
            if (p.dot != null && IsTravelable(p.region))
                p.dot.color = p.region.Id == current ? HudKit.Gold : HudKit.Vermilion;
        }
        ApplyView();
        if (_status != null)
        {
            var r = RegionCatalog.Find(current);
            string course = session != null && session.Course != null
                ? $"{session.Course.DisplayName}  .  {session.Course.Length / 1000f:0.00} KM LAP"
                : "";
            _status.text = r == null
                ? ""
                : $"YOU ARE HERE:  {r.DisplayName.ToUpperInvariant()}  .  {r.Tagline.ToUpperInvariant()}" +
                  (string.IsNullOrEmpty(course) ? "" : $"   >   {course.ToUpperInvariant()}");
        }
    }

    private void Update()
    {
        if (isOpen)
        {
            // Ease towards the pan/zoom goal on UNSCALED time: selection runs at timeScale 0.
            if (Mathf.Abs(_zoom - _zoomGoal) > 1e-4f || (_center - _centerGoal).sqrMagnitude > 1e-9f)
            {
                float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
                _zoom = Mathf.Lerp(_zoom, _zoomGoal, k);
                _center = ClampCenter(Vector2.Lerp(_center, _centerGoal, k), _zoom);
                if (Mathf.Abs(_zoom - _zoomGoal) < 1e-3f && (_center - _centerGoal).sqrMagnitude < 1e-8f)
                { _zoom = _zoomGoal; _center = _centerGoal; }
                ApplyView();
            }

            // Keyboard zoom about the focused pin (or the view centre).
            Vector2 about = _focus >= 0 && _focus < _pins.Count
                ? new Vector2(0.5f, 0.5f) + (_pins[_focus].uv - _centerGoal) * _zoomGoal
                : new Vector2(0.5f, 0.5f);
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) ||
                Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.PageUp))
                ZoomAbout(about, 1.6f);
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) ||
                     Input.GetKeyDown(KeyCode.PageDown))
                ZoomAbout(about, 1f / 1.6f);
            else if (Input.GetKeyDown(KeyCode.Home) || Input.GetKeyDown(KeyCode.Alpha0))
                SetView(new Vector2(0.5f, 0.5f), MinZoom, instant: false);
        }

        // Keyboard pin selection whenever the map is up (the opening selection AND in-ride TAB).
        if (isOpen && Time.frameCount > _openedFrame)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) MoveFocus(Vector2.right);
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) MoveFocus(Vector2.left);
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) MoveFocus(Vector2.up);
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) MoveFocus(Vector2.down);
            else if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                      Input.GetKeyDown(KeyCode.Space)) && _focus >= 0 && _focus < _pins.Count)
            {
                Travel(_pins[_focus].region);
                return;
            }
        }

        // The World Map is the map-SELECTION screen during the opening flow; while it is being
        // presented (or during the 3-2-1) its own keys must not fight the flow director.
        if (selectionMode || RideInputGate.Locked || RaceDirector.Busy) return;
        if (Input.GetKeyDown(openKey)) SetOpen(!isOpen);
        if (isOpen && Input.GetKeyDown(closeKey)) SetOpen(false);
    }
}

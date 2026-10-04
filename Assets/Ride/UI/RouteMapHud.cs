using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-game GPS / map window.
///
/// Two layers, exactly as designed:
///   * STATIC - baked once whenever the course changes: the lake body, the rest of the road
///     network in pale ink, the course casing and line, the checkpoint pins and the elevation
///     profile. All real UI geometry (<see cref="RouteLineGraphic"/>), so it scales crisply and
///     costs nothing per frame.
///   * DYNAMIC - three things move: the rider chevron, the truncated "ridden so far" line and
///     the elevation cursor plus gradient badge. Update is a handful of anchoredPosition
///     writes: no camera render, no per-frame RenderTexture, no GC churn.
///
/// COMPACT mode (<see cref="compact"/>, the default) swaps the whole window for a circular,
/// north-up minimap that scrolls under a centred rider - see <see cref="BuildCompact"/>. The
/// classic window above is untouched and comes back with compact = false before Build.
///
/// It subscribes to <see cref="RideSession"/> only. It never raycasts and never reads the rider
/// transform, so it behaves identically against a telemetry simulator and a real trainer.
/// </summary>
public class RouteMapHud : MonoBehaviour
{
    public enum ZoomMode { WholeCourse = 0, Follow = 1 }

    [Header("Wiring")]
    public RideSession session;
    public RouteDirector director;

    [Header("Map (provisional tuning)")]
    public ZoomMode zoomMode = ZoomMode.WholeCourse;
    [Tooltip("Metres of course visible across the window in Follow mode.")]
    public float followWindowM = 900f;
    [Tooltip("Rider chevron size in map pixels.")]
    public float riderMarkerPx = 26f;
    [Tooltip("How many times a second the ridden-portion line is rebuilt.")]
    public float progressRebuildHz = 6f;
    [Tooltip("Metres of water inset from the circuit centreline - the modelled shore is the " +
             "valley batter running out to the lake surface at VALLEY_RANGE.")]
    public float shoreInsetM = 105f;

    [Header("Compact circular minimap (provisional tuning)")]
    [Tooltip("Circular north-up minimap that scrolls under a centred rider (default). Untick " +
             "(before Build) for the original rectangular GPS window with readouts and profile. " +
             "RideHud copies its own compactHud flag into this on Build.")]
    public bool compact = true;
    [Tooltip("Minimap diameter in reference pixels.")]
    public float compactDiameterPx = 260f;
    [Tooltip("Metres across the minimap in WholeCourse zoom (the default). The M key toggles " +
             "to compactCloseM.")]
    public float compactWideM = 2400f;
    [Tooltip("Metres across the minimap in Follow zoom.")]
    public float compactCloseM = 1000f;

    [Header("Glass backing (provisional tuning)")]
    [Tooltip("Master multiplier on every backing alpha in the window. 0 = backing fully " +
             "see-through (content floats on the scenery), 1 = the tuned smoked-washi pane, " +
             ">1 = heavier scrim for very bright scenery. Content alpha is never touched.")]
    [Range(0f, 1.6f)] public float panelOpacity = 1f;
    [Tooltip("Vertical lit-edge sheen across the top of the pane. Purely cosmetic 'glass' cue.")]
    public bool showSheen = true;

    // --- built widgets -------------------------------------------------------------
    private RectTransform _card, _mapBed, _mapLayer, _pinLayer;
    private Text _title, _lapPill, _scaleText, _gradeText;
    private Text _distValue, _distUnit, _ascValue, _ascUnit, _toGoValue, _toGoUnit, _cpValue, _cpUnit;
    private Text _nextPinLabel;
    private PolygonGraphic _water;
    private RouteLineGraphic _casing, _courseLine, _progressLine;
    private readonly List<RouteLineGraphic> _networkLines = new List<RouteLineGraphic>();
    private RouteProfileGraphic _profile;
    private RectTransform _rider, _profileCursor, _gradeBadge, _scaleBar, _nextPinChip, _scaleChip;
    private readonly List<RectTransform> _pins = new List<RectTransform>();

    private string _builtCourse = "";
    private Vector2 _center, _origin;
    private float _pxPerMetre = 0.1f;
    // Compact-map detail (user: "minimap is not useful"): checkpoint pins, nearby riders, heading.
    private RectTransform _cpLayer, _riderDotLayer, _heading;

    /// <summary>World position of the racer tracked from the Riders screen (RaceDirector), or null.
    /// Drawn as a pulsing pin, clamped to the disc edge when the racer is out of view.</summary>
    public static Vector3? TrackedWorld;
    public static Color TrackedColor = Color.white;
    private RectTransform _trackPin;
    private RawImage _terrainImage;
    private readonly List<RectTransform> _riderDots = new List<RectTransform>();
    private ShiosaiTrafficDirector _traffic;
    private float _dotsTimer, _trafficSeek;
    private Vector2[] _mapPoints = System.Array.Empty<Vector2>();
    private float _progressTimer;
    private int _lastProgressIndex = -1;
    private float _appliedOpacity = -1f;

    /// <summary>An Image that is part of the pane's BACKING, plus the alpha it was tuned at.
    /// Only these are scaled by <see cref="panelOpacity"/> - never the map content.</summary>
    private struct Backing { public Image img; public Color tuned; }
    private readonly List<Backing> _backings = new List<Backing>();

    public RectTransform Card => _card;

    // ================================================================= construction

    public void Build(Transform parent)
    {
        _backings.Clear();
        // A rebuild destroys the old canvas; lists still holding its (now dead) children would
        // otherwise be reused by the next bake.
        _networkLines.Clear();
        _pins.Clear();
        _builtCourse = "";
        _lastProgressIndex = -1;
        _mapPoints = System.Array.Empty<Vector2>();

        if (compact)
        {
            BuildCompact(parent);
            return;
        }

        // The pane is smoked glass, not paper: a dark scrim carrying light content. See the
        // note on HudKit.Glass for why the cream card could not simply be made translucent.
        _card = HudKit.SoftPanel(parent, "GPS Window", HudKit.Pane);
        HudKit.Corner(_card, new Vector2(1f, 0f), new Vector2(1f, 0f),
                      new Vector2(-26f, 26f), new Vector2(620f, 520f));
        RegisterBacking(_card);

        if (showSheen)
        {
            var sheen = HudKit.Panel(_card, "Sheen", HudKit.GlassSheen);
            HudKit.Place(sheen, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var si = sheen.GetComponent<Image>();
            si.sprite = HudSprites.Sheen;
            si.type = Image.Type.Simple;
            RegisterBacking(sheen);
        }

        // One hairline of paper at the pane edge. Without it a low-opacity scrim has no
        // silhouette at all over dark forest and the window stops reading as a window.
        var frame = HudKit.SoftPanel(_card, "Edge", HudKit.GlassEdge, frame: true);
        HudKit.Place(frame, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        RegisterBacking(frame);

        var header = HudKit.Panel(_card, "Header", HudKit.PaneHeader);
        HudKit.Place(header, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -46f), new Vector2(0f, 0f));
        RegisterBacking(header);

        _title = HudKit.Label(header, "Title", "SAKURA CIRCUIT", 22, HudKit.Chalk,
                              TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(_title);
        HudKit.Place((RectTransform)_title.transform, Vector2.zero, Vector2.one,
                     new Vector2(18f, 0f), new Vector2(-160f, 0f));

        _lapPill = HudKit.Label(header, "Lap", "LAP 1/3", 17, HudKit.Ember,
                                TextAnchor.MiddleRight, FontStyle.Bold);
        HudKit.AddShadow(_lapPill);
        HudKit.Place((RectTransform)_lapPill.transform, Vector2.zero, Vector2.one,
                     new Vector2(-180f, 0f), new Vector2(-18f, 0f));

        var rule = HudKit.Panel(_card, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -49f), new Vector2(0f, -46f));

        // ---- map bed --------------------------------------------------------------
        _mapBed = HudKit.Panel(_card, "Map Bed", HudKit.PaneBed);
        HudKit.Place(_mapBed, new Vector2(0f, 0f), new Vector2(1f, 1f),
                     new Vector2(14f, 152f), new Vector2(-14f, -58f));
        RegisterBacking(_mapBed);
        var mask = _mapBed.gameObject.AddComponent<RectMask2D>();
        mask.padding = Vector4.zero;

        _mapLayer = HudKit.Rect(_mapBed, "Layer");
        HudKit.Place(_mapLayer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        _water = NewGraphic<PolygonGraphic>(_mapLayer, "Water", HudKit.GlassWater);
        // Dark casing UNDER a chalk course line: the halo is what keeps the route readable when
        // blown-out blossom shows through the pane behind it.
        _casing = HudKit.Line(_mapLayer, "Course Casing", HudKit.GlassCasing, 7.4f);
        _courseLine = HudKit.Line(_mapLayer, "Course Line", HudKit.Chalk, 3.4f);
        _progressLine = HudKit.Line(_mapLayer, "Progress", HudKit.Ember, 4.6f);
        _pinLayer = HudKit.Rect(_mapLayer, "Pins");
        HudKit.Place(_pinLayer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // The next-checkpoint callout gets its own chip. It has to stay readable where it lands
        // on top of the route line, and over a translucent pane it cannot borrow the bed's
        // contrast the way it used to.
        _nextPinChip = HudKit.SoftPanel(_pinLayer, "Next Pin Chip",
                                        new Color(0.031f, 0.043f, 0.063f, 0.72f));
        _nextPinChip.GetComponent<Image>().sprite = HudSprites.RoundedSmall;
        HudKit.Corner(_nextPinChip, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                      Vector2.zero, new Vector2(150f, 22f));
        _nextPinLabel = HudKit.Label(_nextPinChip, "Next Pin Label", "", 15, HudKit.Chalk,
                                     TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(_nextPinLabel, 0.8f, 1.1f);
        HudKit.Place((RectTransform)_nextPinLabel.transform, Vector2.zero, Vector2.one,
                     new Vector2(7f, 0f), new Vector2(-6f, 0f));

        // rider marker: a bright core inside a dark halo, so it pops on blossom and on rock
        _rider = HudKit.Rect(_mapLayer, "Rider");
        HudKit.Corner(_rider, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, Vector2.one * riderMarkerPx);
        var halo = HudKit.Panel(_rider, "Halo", new Color(0.016f, 0.020f, 0.027f, 0.55f));
        HudKit.Corner(halo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, Vector2.one * (riderMarkerPx + 5f));
        halo.GetComponent<Image>().sprite = HudSprites.Disc;
        var ring = HudKit.Panel(_rider, "Ring", HudKit.Chalk);
        HudKit.Place(ring, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ring.GetComponent<Image>().sprite = HudSprites.Disc;
        var ringEdge = HudKit.Panel(_rider, "Edge", HudKit.Ember);
        HudKit.Place(ringEdge, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ringEdge.GetComponent<Image>().sprite = HudSprites.Ring;
        var chev = HudKit.Panel(_rider, "Chevron", HudKit.Ember);
        HudKit.Corner(chev, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, Vector2.one * (riderMarkerPx * 0.62f));
        chev.GetComponent<Image>().sprite = HudSprites.Chevron;

        // compass + scale bar
        var compass = HudKit.Panel(_mapBed, "Compass", new Color(0.031f, 0.043f, 0.063f, 0.62f));
        HudKit.Corner(compass, new Vector2(1f, 1f), new Vector2(1f, 1f),
                      new Vector2(-10f, -10f), new Vector2(30f, 30f));
        compass.GetComponent<Image>().sprite = HudSprites.Disc;
        RegisterBacking(compass);
        var needle = HudKit.Panel(compass, "N", HudKit.Ember);
        HudKit.Corner(needle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      new Vector2(0f, 2f), new Vector2(13f, 16f));
        needle.GetComponent<Image>().sprite = HudSprites.Chevron;
        var nLabel = HudKit.Label(compass, "Label", "N", 10, HudKit.Chalk, TextAnchor.LowerCenter);
        HudKit.AddShadow(nLabel, 0.8f, 1f);
        HudKit.Place((RectTransform)nLabel.transform, Vector2.zero, Vector2.one,
                     Vector2.zero, new Vector2(0f, -1f));

        // Scale bar on its own chip. It sits directly on the map, where the backdrop can be
        // bright grass or dark rock within the same 120 px, and 12 px type cannot carry a
        // per-glyph effect strong enough to cover that without turning to mush.
        _scaleChip = HudKit.SoftPanel(_mapBed, "Scale Chip",
                                      new Color(0.031f, 0.043f, 0.063f, 0.62f));
        _scaleChip.GetComponent<Image>().sprite = HudSprites.RoundedSmall;
        HudKit.Corner(_scaleChip, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(10f, 10f), new Vector2(126f, 36f));
        RegisterBacking(_scaleChip);

        _scaleBar = HudKit.Panel(_scaleChip, "Scale Bar", HudKit.Chalk);
        HudKit.Corner(_scaleBar, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(8f, 8f), new Vector2(100f, 3f));
        _scaleText = HudKit.Label(_scaleChip, "Scale Text", "200 m", 14, HudKit.Chalk,
                                  TextAnchor.LowerLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)_scaleText.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                      new Vector2(8f, 13f), new Vector2(110f, 18f));

        // ---- readouts --------------------------------------------------------------
        // The readouts get their own bed. Without one the big numbers sit straight on whatever
        // the map bed lets through - at Kawabe that is a bright road diagonal running right
        // under "3.01" - and the 12-13 px captions and units lose the fight even outlined.
        var readoutBed = HudKit.SoftPanel(_card, "Readout Bed", HudKit.PaneBand);
        HudKit.Place(readoutBed, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(14f, 76f), new Vector2(-14f, 150f));
        readoutBed.anchorMax = new Vector2(1f, 0f);
        readoutBed.offsetMin = new Vector2(14f, 76f);
        readoutBed.offsetMax = new Vector2(-14f, 150f);
        RegisterBacking(readoutBed);

        var row = HudKit.Rect(_card, "Readouts");
        HudKit.Place(row, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(14f, 76f), new Vector2(-14f, 150f));
        row.anchorMax = new Vector2(1f, 0f);
        row.offsetMin = new Vector2(14f, 76f);
        row.offsetMax = new Vector2(-14f, 150f);

        float w = 592f / 4f;
        HudKit.Readout(row, "Distance", "DISTANCE", 0f, w, out _distValue, out _distUnit,
                       HudKit.Chalk, HudKit.ChalkSoft, legible: true);
        HudKit.Readout(row, "Ascent", "ASCENT", w, w, out _ascValue, out _ascUnit,
                       HudKit.Chalk, HudKit.ChalkSoft, legible: true);
        HudKit.Readout(row, "ToGo", "TO GO", w * 2f, w, out _toGoValue, out _toGoUnit,
                       HudKit.Chalk, HudKit.ChalkSoft, legible: true);
        HudKit.Readout(row, "NextCp", "NEXT CP", w * 3f, w, out _cpValue, out _cpUnit,
                       HudKit.Chalk, HudKit.ChalkSoft, legible: true);

        // ---- elevation band ---------------------------------------------------------
        var band = HudKit.SoftPanel(_card, "Elevation", HudKit.PaneBand);
        HudKit.Place(band, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(14f, 12f), new Vector2(-14f, 74f));
        band.anchorMax = new Vector2(1f, 0f);
        band.offsetMin = new Vector2(14f, 12f);
        band.offsetMax = new Vector2(-14f, 74f);
        RegisterBacking(band);

        _profile = NewGraphic<RouteProfileGraphic>(band, "Profile",
                                                   new Color(0.784f, 0.808f, 0.824f, 0.34f));
        _profile.color = new Color(0.784f, 0.808f, 0.824f, 0.34f);
        _profile.ridden = new Color(0.976f, 0.416f, 0.318f, 0.80f);

        _profileCursor = HudKit.Panel(band, "Cursor", HudKit.Chalk);
        HudKit.Corner(_profileCursor, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, new Vector2(2f, 62f));

        // Elevation axis chip: the +/- labels used to sit straight on the ember "ridden" fill,
        // where dark outlined 11 px type was unreadable. A narrow axis strip is both legible and
        // more chart-like than two floating numbers.
        var axis = HudKit.SoftPanel(band, "Axis", new Color(0.031f, 0.043f, 0.063f, 0.58f));
        axis.GetComponent<Image>().sprite = HudSprites.RoundedSmall;
        axis.anchorMin = new Vector2(0f, 0f);
        axis.anchorMax = new Vector2(0f, 1f);
        axis.pivot = new Vector2(0f, 0.5f);
        axis.sizeDelta = new Vector2(54f, -8f);       // stretched in y, fixed 54 px wide
        axis.anchoredPosition = new Vector2(4f, 0f);
        RegisterBacking(axis);

        var hi = HudKit.Label(axis, "Hi", "", 13, HudKit.Chalk, TextAnchor.UpperCenter,
                              FontStyle.Bold);
        HudKit.Place((RectTransform)hi.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -18f), new Vector2(0f, -2f));
        var lo = HudKit.Label(axis, "Lo", "", 13, HudKit.Chalk, TextAnchor.LowerCenter,
                              FontStyle.Bold);
        HudKit.Place((RectTransform)lo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                     new Vector2(0f, 2f), new Vector2(0f, 18f));
        _elevHi = hi;
        _elevLo = lo;

        _gradeBadge = HudKit.Chip(band, "Grade", HudKit.Ember, out _gradeText, 20, Color.white);
        _gradeBadge.GetComponent<Image>().sprite = HudSprites.RoundedSmall;
        _gradeBadge.GetComponent<Image>().type = Image.Type.Sliced;
        HudKit.AddShadow(_gradeText, 0.7f, 1.2f);
        HudKit.Corner(_gradeBadge, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                      new Vector2(-8f, 0f), new Vector2(98f, 34f));

        ApplyPanelOpacity();
    }

    /// <summary>Records a backing Image so <see cref="panelOpacity"/> can scale it later.</summary>
    private void RegisterBacking(RectTransform rt)
    {
        var img = rt != null ? rt.GetComponent<Image>() : null;
        if (img == null) return;
        _backings.Add(new Backing { img = img, tuned = img.color });
    }

    /// <summary>
    /// Re-applies the master opacity to every backing element. Content - route lines, pins, text,
    /// the rider marker, the profile - is deliberately excluded, so turning the pane down makes
    /// the world show through without ever making the readouts fainter.
    /// </summary>
    public void ApplyPanelOpacity()
    {
        float k = Mathf.Max(0f, panelOpacity);
        for (int i = 0; i < _backings.Count; i++)
        {
            var b = _backings[i];
            if (b.img == null) continue;
            b.img.color = HudKit.WithAlpha(b.tuned, Mathf.Clamp01(b.tuned.a * k));
        }
        _appliedOpacity = k;
    }

    private Text _elevHi, _elevLo;

    // ================================================================= compact minimap

    private RectTransform _networkLayer;

    /// <summary>Dark navy disc. Heavier than the classic pane because it is small, round and
    /// carries thin white lines that must hold against bright water and sky.</summary>
    // PROVISIONAL: 0.66 at (0.035, 0.063, 0.125) came back brown over Minato's sand - the
    // backdrop won the hue. A bluer, slightly heavier tint keeps it reading as navy.
    private static readonly Color MiniGlass = new Color(0.024f, 0.071f, 0.180f, 0.76f);

    /// <summary>
    /// The compact minimap: a circular, north-up window that scrolls under a centred rider.
    ///
    /// Layers, bottom to top: navy backing disc; a <see cref="Mask"/> disc (stencil - a
    /// RectMask2D could only clip to the bounding square) holding the map layer with the
    /// network, course casing, course line and ridden line; the rider dot, which never moves;
    /// a hairline rim; and the "N" badge sitting on the rim at the top-right.
    ///
    /// The map layer is baked ONCE per course/zoom at a fixed px-per-metre, and a frame is a
    /// single anchoredPosition write that slides it so the rider lands on the centre. The
    /// classic Follow zoom re-decimated the whole course every frame; at Minato's 19 km that is
    /// thousands of points per frame for a window that only ever shows ~2 km of it.
    ///
    /// The mask graphic is a separate, always-opaque disc (showMaskGraphic off) so that dialling
    /// <see cref="panelOpacity"/> down to 0 fades the backing without also clipping the map away.
    /// </summary>
    private void BuildCompact(Transform parent)
    {
        // Classic-only widgets do not exist in this style; clear any left over from a previous
        // classic build so nothing below mistakes a destroyed widget for a live one.
        _mapBed = _pinLayer = _rider = _profileCursor = _gradeBadge = _scaleBar = null;
        _nextPinChip = _scaleChip = null;
        _title = _lapPill = _scaleText = _gradeText = _nextPinLabel = _elevHi = _elevLo = null;
        _distValue = _distUnit = _ascValue = _ascUnit = _toGoValue = _toGoUnit = _cpValue = _cpUnit = null;
        _water = null;
        _profile = null;

        float d = compactDiameterPx;
        _card = HudKit.Rect(parent, "GPS Window");
        HudKit.Corner(_card, new Vector2(1f, 0f), new Vector2(1f, 0f),
                      new Vector2(-26f, 26f), new Vector2(d, d));

        // A faint, slightly larger dark halo gives the disc an edge against dark scenery
        // without a hard drop shadow.
        var halo = HudKit.Panel(_card, "Halo", new Color(0.016f, 0.020f, 0.027f, 0.22f));
        halo.GetComponent<Image>().sprite = HudSprites.DiscLarge;
        HudKit.Place(halo, Vector2.zero, Vector2.one, new Vector2(-4f, -4f), new Vector2(4f, 4f));
        RegisterBacking(halo);

        var disc = HudKit.Panel(_card, "Disc", MiniGlass);
        disc.GetComponent<Image>().sprite = HudSprites.DiscLarge;
        HudKit.Place(disc, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        RegisterBacking(disc);

        var clip = HudKit.Panel(_card, "Clip", Color.white);
        clip.GetComponent<Image>().sprite = HudSprites.DiscLarge;
        HudKit.Place(clip, Vector2.zero, Vector2.one, new Vector2(1.5f, 1.5f), new Vector2(-1.5f, -1.5f));
        clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        // Zero-size layer pivoted on the disc centre: map coordinates are pixels from the
        // course centre, and sliding this layer by -rider puts the rider on the centre.
        _mapLayer = HudKit.Rect(clip, "Layer");
        HudKit.Corner(_mapLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, Vector2.zero);
        // Baked top-down picture of the region (MinimapBake): land, sea, city - under everything.
        var terrainGo = new GameObject("Terrain Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        terrainGo.transform.SetParent(_mapLayer, false);
        _terrainImage = terrainGo.GetComponent<RawImage>();
        _terrainImage.raycastTarget = false;
        _terrainImage.color = new Color(0.85f, 0.88f, 0.95f, 0.92f);
        _terrainImage.enabled = false;
        var trt = (RectTransform)terrainGo.transform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        _networkLayer = HudKit.Rect(_mapLayer, "Network");
        HudKit.Place(_networkLayer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        _casing = HudKit.Line(_mapLayer, "Course Casing", new Color(0.016f, 0.024f, 0.035f, 0.78f), 7.4f);
        _courseLine = HudKit.Line(_mapLayer, "Course Line", HudKit.Chalk, 3.4f);
        _progressLine = HudKit.Line(_mapLayer, "Progress", RideHud.RideBlue, 4.6f);
        _cpLayer = HudKit.Rect(_mapLayer, "Checkpoints");
        HudKit.Place(_cpLayer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _riderDotLayer = HudKit.Rect(_mapLayer, "Riders");
        HudKit.Place(_riderDotLayer, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _riderDots.Clear();

        // Heading cone: a soft wedge ahead of the rider showing the direction of travel.
        _heading = HudKit.Rect(_card, "Heading");
        HudKit.Corner(_heading, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 2f);
        var cone = HudKit.Panel(_heading, "Cone", HudKit.WithAlpha(RideHud.RideBlue, 0.35f));
        cone.anchorMin = cone.anchorMax = new Vector2(0.5f, 0.5f);
        cone.sizeDelta = new Vector2(22f, 22f);
        cone.anchoredPosition = new Vector2(0f, 16f);
        cone.localRotation = Quaternion.Euler(0f, 0f, 45f);

        // Rider: blue dot in a white ring, in a soft dark halo - fixed at the centre.
        var rider = HudKit.Rect(_card, "Rider");
        HudKit.Corner(rider, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                      Vector2.zero, Vector2.one * 18f);
        var rHalo = HudKit.Panel(rider, "Halo", new Color(0.016f, 0.020f, 0.027f, 0.45f));
        HudKit.Place(rHalo, Vector2.zero, Vector2.one, new Vector2(-4f, -4f), new Vector2(4f, 4f));
        rHalo.GetComponent<Image>().sprite = HudSprites.Disc;
        var rRing = HudKit.Panel(rider, "Ring", HudKit.Chalk);
        HudKit.Place(rRing, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        rRing.GetComponent<Image>().sprite = HudSprites.Disc;
        var rCore = HudKit.Panel(rider, "Core", RideHud.RideBlue);
        HudKit.Place(rCore, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        rCore.GetComponent<Image>().sprite = HudSprites.Disc;

        var rim = HudKit.Panel(_card, "Rim", HudKit.WithAlpha(HudKit.Chalk, 0.55f));
        rim.GetComponent<Image>().sprite = HudSprites.ThinRing;
        HudKit.Place(rim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // North badge centred ON the rim at 45 degrees (top-right), so it reads as part of the
        // dial rather than a floating chip.
        float inset = d * 0.5f * (1f - 0.70711f);
        var badge = HudKit.Panel(_card, "North Badge", new Color(0.035f, 0.063f, 0.125f, 0.90f));
        badge.GetComponent<Image>().sprite = HudSprites.Disc;
        HudKit.Corner(badge, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                      new Vector2(-inset, -inset), new Vector2(30f, 30f));
        RegisterBacking(badge);
        var badgeRim = HudKit.Panel(badge, "Rim", HudKit.WithAlpha(HudKit.Chalk, 0.60f));
        badgeRim.GetComponent<Image>().sprite = HudSprites.HairRing;
        HudKit.Place(badgeRim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var n = HudKit.Label(badge, "N", "N", 15, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.Place((RectTransform)n.transform, Vector2.zero, Vector2.one,
                     new Vector2(0f, 0f), new Vector2(0f, 1f));

        ApplyPanelOpacity();
    }

    /// <summary>Metres across the minimap for the current zoom (the M key toggles it).</summary>
    private float CompactWindowM =>
        Mathf.Max(100f, zoomMode == ZoomMode.Follow ? compactCloseM : compactWideM);

    /// <summary>The compact bake: fixed scale, whole course + network, once per course/zoom.
    /// No lake (BakeWater is Sakura-only), no pins, no profile.</summary>
    private void RebakeCompact(RouteCourse course)
    {
        _pxPerMetre = compactDiameterPx / CompactWindowM;
        _center = (course.BoundsMin + course.BoundsMax) * 0.5f;
        _origin = Vector2.zero;

        _mapPoints = Decimate(course.Position, 1.5f);
        _casing.SetPoints(_mapPoints);
        _courseLine.SetPoints(_mapPoints);
        _progressLine.SetPoints(_mapPoints);
        _casing.ClosedLoop = course.Closed;
        _courseLine.ClosedLoop = course.Closed;

        BakeNetwork(session.Graph, _networkLayer, false);
        BakeCompactCheckpoints(course);
        PlaceTerrainImage(course);

        _lastProgressIndex = -1;
        _builtCourse = CompactKey(course);
    }

    private string CompactKey(RouteCourse course) =>
        course.Id + "|" + zoomMode + "|compact|" + compactDiameterPx.ToString("0");

    private void RefreshCompact(RouteCourse course, float dt)
    {
        if (_mapLayer == null || _casing == null) return;
        if (CompactKey(course) != _builtCourse) RebakeCompact(course);

        if (!Mathf.Approximately(_appliedOpacity, Mathf.Max(0f, panelOpacity))) ApplyPanelOpacity();

        // The whole per-frame cost of the minimap: slide the baked layer under the rider.
        _mapLayer.anchoredPosition = -WorldToMap(session.WorldPosition);

        RefreshCompactDetail(course, dt);

        _progressTimer += dt;
        float f = course.Length <= 0.01f ? 0f : Mathf.Clamp01(session.DistanceM / course.Length);
        int idx = Mathf.Clamp(Mathf.RoundToInt(f * _mapPoints.Length), 0, _mapPoints.Length);
        if (idx != _lastProgressIndex)
        {
            _lastProgressIndex = idx;
            _progressLine.SetVisibleCount(idx);
        }
    }

    /// <summary>Positions the baked region picture with the same world->map transform.</summary>
    private void PlaceTerrainImage(RouteCourse course)
    {
        if (_terrainImage == null) return;
        string region = session.Graph != null ? session.Graph.RegionOfCourse(course.Id) : null;
        var tex = region != null ? Resources.Load<Texture2D>("Minimap/" + region) : null;
        var json = region != null ? Resources.Load<TextAsset>("Minimap/" + region + "_bounds") : null;
        if (tex == null || json == null) { _terrainImage.enabled = false; return; }
        var b = JsonUtility.FromJson<MinimapBounds>(json.text);
        _terrainImage.texture = tex;
        var rt = _terrainImage.rectTransform;
        rt.anchoredPosition = WorldToMap(new Vector3(b.cx, 0f, b.cz));
        rt.sizeDelta = Vector2.one * b.size * _pxPerMetre;
        _terrainImage.enabled = true;
    }

    /// <summary>Checkpoint pins (ember discs) and a white finish pin, baked with the course.</summary>
    private void BakeCompactCheckpoints(RouteCourse course)
    {
        if (_cpLayer == null) return;
        for (int i = _cpLayer.childCount - 1; i >= 0; i--) Destroy(_cpLayer.GetChild(i).gameObject);
        if (course.Checkpoints != null)
            for (int i = 0; i < course.Checkpoints.Length; i++)
                Pin(_cpLayer, WorldToMap(course.PositionAt(course.Checkpoints[i].Distance)), HudKit.Ember, 11f);
        if (!course.Closed)
            Pin(_cpLayer, WorldToMap(course.PositionAt(course.Length)), HudKit.Chalk, 13f);
    }

    private static RectTransform Pin(RectTransform layer, Vector2 at, Color color, float size)
    {
        var ring = HudKit.Panel(layer, "Pin", new Color(0.016f, 0.020f, 0.027f, 0.85f));
        ring.GetComponent<Image>().sprite = HudSprites.Disc;
        ring.anchorMin = ring.anchorMax = new Vector2(0.5f, 0.5f);
        ring.sizeDelta = Vector2.one * size;
        ring.anchoredPosition = at;
        var core = HudKit.Panel(ring, "Core", color);
        core.GetComponent<Image>().sprite = HudSprites.Disc;
        HudKit.Place(core, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
        return ring;
    }

    /// <summary>Nearby riders as small dots (5 Hz) and the heading cone (every frame).</summary>
    private void RefreshCompactDetail(RouteCourse course, float dt)
    {
        if (_heading != null)
        {
            float d = session.DistanceM;
            var a = WorldToMap(course.PositionAt(d));
            var b = WorldToMap(course.PositionAt(Mathf.Min(course.Length, d + 8f)));
            var dir = b - a;
            if (dir.sqrMagnitude > 1e-4f)
                _heading.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
        }

        UpdateTrackPin();

        _dotsTimer -= dt;
        if (_dotsTimer > 0f || _riderDotLayer == null) return;
        _dotsTimer = 0.2f;
        if (_traffic == null && Time.unscaledTime >= _trafficSeek)
        {
            _trafficSeek = Time.unscaledTime + 2f;
            _traffic = FindFirstObjectByType<ShiosaiTrafficDirector>(FindObjectsInactive.Exclude);
        }
        int used = 0;
        if (_traffic != null && _traffic.isActiveAndEnabled && _traffic.pool != null)
            foreach (var t in _traffic.pool)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (used == _riderDots.Count)
                {
                    var dot = HudKit.Panel(_riderDotLayer, "Rider Dot", new Color(1f, 0.82f, 0.30f, 0.95f));
                    dot.GetComponent<Image>().sprite = HudSprites.Disc;
                    dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
                    dot.sizeDelta = Vector2.one * 7f;
                    _riderDots.Add(dot);
                }
                var r = _riderDots[used++];
                r.anchoredPosition = WorldToMap(t.position);
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            }
        for (int i = used; i < _riderDots.Count; i++)
            if (_riderDots[i].gameObject.activeSelf) _riderDots[i].gameObject.SetActive(false);
    }

    private void UpdateTrackPin()
    {
        if (_riderDotLayer == null || session == null) return;
        if (TrackedWorld == null)
        {
            if (_trackPin != null && _trackPin.gameObject.activeSelf) _trackPin.gameObject.SetActive(false);
            return;
        }
        if (_trackPin == null)
        {
            _trackPin = HudKit.Panel(_riderDotLayer, "Tracked Racer", new Color(0.02f, 0.02f, 0.04f, 0.95f));
            _trackPin.GetComponent<Image>().sprite = HudSprites.Disc;
            _trackPin.anchorMin = _trackPin.anchorMax = new Vector2(0.5f, 0.5f);
            _trackPin.sizeDelta = Vector2.one * 20f;
            var fill = HudKit.Panel(_trackPin, "Fill", Color.white);
            fill.GetComponent<Image>().sprite = HudSprites.Disc;
            fill.anchorMin = fill.anchorMax = new Vector2(0.5f, 0.5f);
            fill.sizeDelta = Vector2.one * 14f;
        }
        if (!_trackPin.gameObject.activeSelf) _trackPin.gameObject.SetActive(true);
        _trackPin.SetAsLastSibling();
        var me = WorldToMap(session.WorldPosition);
        var v = WorldToMap(TrackedWorld.Value) - me;
        float r = compactDiameterPx * 0.5f - 14f;
        if (v.magnitude > r) v = v.normalized * r;
        _trackPin.anchoredPosition = me + v;
        _trackPin.localScale = Vector3.one * (1f + 0.18f * Mathf.Sin(Time.unscaledTime * 5f));
        _trackPin.GetChild(0).GetComponent<Image>().color = TrackedColor;
    }

    private static T NewGraphic<T>(Transform parent, string name, Color color)
        where T : MaskableGraphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.transform.SetParent(parent, false);
        var g = go.GetComponent<T>();
        g.color = color;
        g.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return g;
    }

    // ================================================================= static bake

    private Vector2 WorldToMap(Vector3 world)
    {
        var p = new Vector2(world.x, world.z) - _center;
        return _origin + p * _pxPerMetre;      // +Z is north = up
    }

    private void Rebake()
    {
        var course = session != null ? session.Course : null;
        if (course == null || course.Count < 2 || _mapBed == null) return;

        var rect = _mapBed.rect;
        if (rect.width < 10f || rect.height < 10f) return;

        Vector2 lo, hi;
        if (zoomMode == ZoomMode.Follow)
        {
            var c = new Vector2(session.WorldPosition.x, session.WorldPosition.z);
            var half = new Vector2(followWindowM, followWindowM) * 0.5f;
            lo = c - half;
            hi = c + half;
        }
        else
        {
            lo = course.BoundsMin;
            hi = course.BoundsMax;
        }

        var span = Vector2.Max(hi - lo, Vector2.one * 10f);
        _center = (lo + hi) * 0.5f;
        _origin = Vector2.zero;
        _pxPerMetre = Mathf.Min((rect.width - 34f) / span.x, (rect.height - 34f) / span.y);

        // course polyline, decimated to ~1.5 px so the vertex buffer stays small
        _mapPoints = Decimate(course.Position, 1.5f);
        _casing.SetPoints(_mapPoints);
        _courseLine.SetPoints(_mapPoints);
        _progressLine.SetPoints(_mapPoints);
        _casing.ClosedLoop = course.Closed;
        _courseLine.ClosedLoop = course.Closed;

        var graph = session.Graph;
        BakeNetwork(graph, _mapLayer, true);

        BakeWater(graph);
        BakePins(course);
        BakeProfile(course);
        BakeScaleBar();

        _lastProgressIndex = -1;
        _builtCourse = course.Id + "|" + zoomMode;
    }

    /// <summary>
    /// The rest of the network in pale ink, so a course reads as part of a place.
    /// One graphic PER SEGMENT: concatenating them into a single polyline draws a phantom
    /// road from the end of each segment to the start of the next, right across the lake.
    /// </summary>
    /// <param name="afterWater">Classic window only: slot the lines just above the lake body
    /// (sibling 0) so they stay under the course casing. The compact map gives them a layer.</param>
    private void BakeNetwork(RouteGraph graph, RectTransform parent, bool afterWater)
    {
        if (graph == null || parent == null) return;
        while (_networkLines.Count < graph.segments.Length)
        {
            var g = HudKit.Line(parent, "Network_" + _networkLines.Count,
                                HudKit.GlassNetwork, 2f);
            if (afterWater) g.transform.SetSiblingIndex(1 + _networkLines.Count);
            HudSprites.SetLayerRecursively(g.gameObject, HudSprites.UiLayer);
            _networkLines.Add(g);
        }
        for (int i = 0; i < _networkLines.Count; i++)
        {
            if (i < graph.segments.Length)
            {
                _networkLines[i].SetPoints(Decimate(graph.segments[i].position, 2.5f));
                _networkLines[i].enabled = true;
            }
            else
            {
                _networkLines[i].SetPoints(System.Array.Empty<Vector2>());
            }
        }
    }

    private Vector2[] Decimate(Vector3[] world, float minPx)
    {
        var outPts = new List<Vector2>(world.Length);
        Vector2 last = Vector2.positiveInfinity;
        for (int i = 0; i < world.Length; i++)
        {
            var p = WorldToMap(world[i]);
            if (i == 0 || i == world.Length - 1 || (p - last).sqrMagnitude >= minPx * minPx)
            {
                outPts.Add(p);
                last = p;
            }
        }
        return outPts.ToArray();
    }

    /// <summary>
    /// The lake body, derived rather than authored: the terrain builder batters the valley side
    /// from the carriageway down to the lake surface over VALLEY_RANGE metres, so offsetting the
    /// circuit's centreline by that distance to the rider's right traces the actual waterline.
    /// </summary>
    private void BakeWater(RouteGraph graph)
    {
        if (graph == null || _water == null) { return; }
        var circuit = graph.Course("sakura_circuit");
        if (circuit == null) { _water.SetPoints(System.Array.Empty<Vector2>()); return; }
        var ring = RouteCourse.Build(graph, circuit);
        if (!ring.Closed || ring.Count < 8) { _water.SetPoints(System.Array.Empty<Vector2>()); return; }

        var pts = new List<Vector2>();
        int stride = Mathf.Max(1, ring.Count / 160);
        for (int i = 0; i < ring.Count; i += stride)
        {
            var p = ring.Position[i] + ring.Side[i] * shoreInsetM;
            pts.Add(WorldToMap(p));
        }
        _water.SetPoints(pts.ToArray());
    }

    private void BakePins(RouteCourse course)
    {
        foreach (var p in _pins)
        {
            if (p == null) continue;
            if (Application.isPlaying) Destroy(p.gameObject);
            else DestroyImmediate(p.gameObject);
        }
        _pins.Clear();

        for (int i = 0; i < course.Checkpoints.Length; i++)
        {
            var pin = HudKit.Panel(_pinLayer, "Pin_" + i, HudKit.Chalk);
            HudKit.Corner(pin, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                          WorldToMap(course.PositionAt(course.Checkpoints[i].Distance)),
                          Vector2.one * 11f);
            pin.GetComponent<Image>().sprite = HudSprites.Disc;
            var edge = HudKit.Panel(pin, "Edge", new Color(0.016f, 0.020f, 0.027f, 0.9f));
            HudKit.Place(edge, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            edge.GetComponent<Image>().sprite = HudSprites.Ring;
            _pins.Add(pin);
        }
        if (_nextPinChip != null) _nextPinChip.transform.SetAsLastSibling();
    }

    private void BakeProfile(RouteCourse course)
    {
        int n = 220;
        var pts = new Vector2[n];
        float range = Mathf.Max(1f, course.MaxElevation - course.MinElevation);
        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            float y = course.ElevationAt(f * course.Length);
            pts[i] = new Vector2(f, Mathf.Clamp01((y - course.MinElevation) / range) * 0.86f + 0.06f);
        }
        _profile.SetProfile(pts);
        if (_elevHi != null) _elevHi.text = $"+{course.MaxElevation:0} m";
        if (_elevLo != null) _elevLo.text = $"{course.MinElevation:0} m";
    }

    private void BakeScaleBar()
    {
        float[] nice = { 50f, 100f, 200f, 500f, 1000f, 2000f };
        float best = nice[0];
        foreach (var m in nice)
        {
            if (m * _pxPerMetre <= 130f) best = m;
        }
        _scaleBar.sizeDelta = new Vector2(Mathf.Max(12f, best * _pxPerMetre), 3f);
        _scaleText.text = best >= 1000f ? $"{best / 1000f:0.#} km" : $"{best:0} m";
        // Chip tracks the bar, but never narrower than the "1.2 km" label it also carries.
        if (_scaleChip != null)
            _scaleChip.sizeDelta = new Vector2(Mathf.Max(_scaleBar.sizeDelta.x + 16f, 78f), 38f);
    }

    // ================================================================= per frame

    private void Update()
    {
        Refresh(Time.deltaTime);
    }

    /// <summary>Deterministic refresh so the editor capture harness drives the same code.</summary>
    public void Refresh(float dt)
    {
        if (session == null || _card == null) return;
        var course = session.Course;
        if (course == null || course.Count < 2) return;

        if (_mapBed == null)
        {
            // Compact minimap (or a style switch that has not been rebuilt yet): none of the
            // classic widgets below exist.
            RefreshCompact(course, dt);
            return;
        }

        string key = course.Id + "|" + zoomMode;
        if (key != _builtCourse) Rebake();
        if (zoomMode == ZoomMode.Follow) Rebake();

        // Live opacity: the pane can be dialled in the inspector during a ride without a rebuild.
        if (!Mathf.Approximately(_appliedOpacity, Mathf.Max(0f, panelOpacity))) ApplyPanelOpacity();

        // --- header ---
        _title.text = course.DisplayName.ToUpperInvariant();
        _lapPill.text = $"LAP {Mathf.Min(session.LapIndex + 1, session.TotalLaps)}/{session.TotalLaps}";

        // --- rider marker ---
        var mapPos = WorldToMap(session.WorldPosition);
        _rider.anchoredPosition = mapPos;
        _rider.localRotation = Quaternion.Euler(0f, 0f, -session.HeadingDeg);

        // --- ridden portion: only rebuilt when the sample index actually changes ---
        // --- ridden portion: the vertex buffer is only rebuilt when the sample index actually
        // changes, which is a few times a second at ride speed. It is deliberately NOT also
        // gated on a timer: a capture harness that calls Refresh a handful of times would then
        // never truncate the line at all, and the whole course would draw as "already ridden".
        _progressTimer += dt;
        {
            float f = course.Length <= 0.01f ? 0f : Mathf.Clamp01(session.DistanceM / course.Length);
            int idx = Mathf.Clamp(Mathf.RoundToInt(f * _mapPoints.Length), 0, _mapPoints.Length);
            if (idx != _lastProgressIndex)
            {
                _lastProgressIndex = idx;
                _progressLine.SetVisibleCount(idx);
            }
            _profile.SetFill(f);
        }

        // --- readouts ---
        _distValue.text = (session.TotalDistanceM / 1000f).ToString("0.00");
        _distUnit.text = $"of {session.PlannedDistanceM / 1000f:0.00} km";
        _ascValue.text = session.AscentM.ToString("0");
        _ascUnit.text = "m climbed";
        _toGoValue.text = (session.RemainingM / 1000f).ToString("0.00");
        _toGoUnit.text = "km";

        if (director != null && director.HasNext)
        {
            _cpValue.text = director.MetresToNext >= 1000f
                ? (director.MetresToNext / 1000f).ToString("0.0")
                : director.MetresToNext.ToString("0");
            _cpUnit.text = director.MetresToNext >= 1000f ? "km" : "m";
            _nextPinLabel.text = RouteNames.Checkpoint(director.NextCheckpoint.Name);
            var pinPos = WorldToMap(course.PositionAt(director.NextCheckpoint.Distance));

            // Flip the callout to the left of the pin when it would run off the map bed, and
            // size the chip to the text so it never reads as a floating bar.
            float chipW = Mathf.Clamp(_nextPinLabel.preferredWidth + 16f, 60f, 210f);
            bool flip = pinPos.x + 11f + chipW > _mapBed.rect.width * 0.5f - 6f;
            _nextPinChip.pivot = new Vector2(flip ? 1f : 0f, 0.5f);
            _nextPinChip.sizeDelta = new Vector2(chipW, 22f);
            _nextPinChip.anchoredPosition = pinPos + new Vector2(flip ? -11f : 11f, 0f);
            _nextPinChip.gameObject.SetActive(true);
            _nextPinLabel.enabled = true;
            HighlightNextPin(course, director.NextCheckpoint);
        }
        else
        {
            _cpValue.text = "-";
            _cpUnit.text = "";
            _nextPinLabel.enabled = false;
            if (_nextPinChip != null) _nextPinChip.gameObject.SetActive(false);
        }

        // --- elevation cursor + gradient badge ---
        float pf = course.Length <= 0.01f ? 0f : Mathf.Clamp01(session.DistanceM / course.Length);
        var band = (RectTransform)_profile.transform.parent;
        _profileCursor.anchoredPosition = new Vector2(pf * band.rect.width, 0f);
        float pct = session.DisplayGradePct;
        _gradeText.text = (pct >= 0f ? "+" : "") + pct.ToString("0.0") + "%";
        _gradeBadge.GetComponent<Image>().color =
            pct >= 0.5f ? HudKit.Ember                                   // climbing
            : pct <= -0.5f ? new Color(0.294f, 0.592f, 0.749f, 1f)       // descending
            : new Color(0.396f, 0.416f, 0.447f, 1f);                     // flat
    }

    private void HighlightNextPin(RouteCourse course, CourseCheckpoint next)
    {
        for (int i = 0; i < _pins.Count && i < course.Checkpoints.Length; i++)
        {
            bool isNext = Mathf.Abs(course.Checkpoints[i].Distance - next.Distance) < 0.01f;
            var img = _pins[i].GetComponent<Image>();
            img.color = isNext ? HudKit.Gold : HudKit.Chalk;
            var edge = _pins[i].GetChild(0).GetComponent<Image>();
            edge.color = isNext ? HudKit.Ember : new Color(0.016f, 0.020f, 0.027f, 0.9f);
            _pins[i].sizeDelta = Vector2.one * (isNext ? 15f : 11f);
        }
    }

    /// <summary>Forces a static rebake - used after a course change or a layout resize.</summary>
    public void Invalidate() { _builtCourse = ""; }
}

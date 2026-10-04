using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Washi-card styling and the handful of uGUI builders the ride HUD needs.
///
/// Palette is the torii/lantern one already in <c>diag_gate.png</c> - cream paper, vermilion
/// rule, ink text - so the HUD sits in the same world as the road rather than on top of it.
/// The HUD is built in code, not from a prefab, so the setup pass is idempotent and a scene
/// that lost its canvas can always rebuild an identical one.
/// </summary>
public static class HudKit
{
    public static readonly Color Paper = new Color(0.953f, 0.925f, 0.878f, 0.94f);
    public static readonly Color PaperSolid = new Color(0.953f, 0.925f, 0.878f, 1f);
    public static readonly Color Header = new Color(0.898f, 0.851f, 0.788f, 0.96f);
    public static readonly Color MapBed = new Color(0.886f, 0.925f, 0.914f, 1f);
    public static readonly Color Water = new Color(0.529f, 0.678f, 0.745f, 1f);
    public static readonly Color Vermilion = new Color(0.769f, 0.267f, 0.180f, 1f);
    public static readonly Color Ink = new Color(0.169f, 0.149f, 0.133f, 1f);
    public static readonly Color InkSoft = new Color(0.365f, 0.337f, 0.310f, 1f);
    public static readonly Color Gold = new Color(0.847f, 0.647f, 0.180f, 1f);
    public static readonly Color Casing = new Color(1f, 1f, 1f, 0.85f);
    public static readonly Color Shade = new Color(0.114f, 0.098f, 0.090f, 0.82f);

    // ---- Smoked-washi glass ---------------------------------------------------------------
    // Backing for panes that must stay readable while letting the world through - currently the
    // GPS window.
    //
    // A DARK scrim carrying LIGHT content is the only scheme that survives both ends of this
    // world. A translucent CREAM pane goes muddy brown inside the cliff tunnel and the dark ink
    // text on it loses almost all contrast; a dark pane can only ever ADD contrast underneath
    // light text, and over bright blossom it guarantees a darkness floor no matter how blown out
    // the backdrop is. It is also already in the art direction: the checkpoint banner
    // (<see cref="Shade"/>) is a dark pane with gold text and reads cleanly in both places.
    public static readonly Color Glass = new Color(0.043f, 0.055f, 0.075f, 0.52f);
    public static readonly Color GlassHeader = new Color(0.031f, 0.043f, 0.063f, 0.34f);
    public static readonly Color GlassBed = new Color(0.075f, 0.106f, 0.129f, 0.26f);
    public static readonly Color GlassBand = new Color(0.031f, 0.043f, 0.063f, 0.38f);
    public static readonly Color GlassEdge = new Color(0.965f, 0.945f, 0.902f, 0.32f);
    public static readonly Color GlassSheen = new Color(0.867f, 0.918f, 1f, 0.10f);

    /// <summary>Paper-white foreground for glass panes - the ink/inkSoft pair, inverted.</summary>
    public static readonly Color Chalk = new Color(0.976f, 0.965f, 0.937f, 1f);
    public static readonly Color ChalkSoft = new Color(0.898f, 0.894f, 0.875f, 1f);

    /// <summary>Vermilion lifted for dark glass - the road-sign vermilion reads muddy on it.</summary>
    public static readonly Color Ember = new Color(0.976f, 0.416f, 0.318f, 1f);
    public static readonly Color GlassWater = new Color(0.325f, 0.553f, 0.686f, 0.44f);
    public static readonly Color GlassCasing = new Color(0.016f, 0.024f, 0.035f, 0.66f);
    public static readonly Color GlassNetwork = new Color(0.867f, 0.882f, 0.894f, 0.24f);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // ---- Master pane transparency --------------------------------------------------------
    /// <summary>
    /// PROVISIONAL TUNING - the single knob for how opaque every HUD panel background is.
    ///
    /// The player's note was that the boxes, though already translucent, still block the road
    /// ahead, so the panes are dialled down to ~18 % opaque (~82 % see-through). Everything
    /// that is a panel BACKGROUND derives its alpha from this; nothing that is CONTENT (text,
    /// route line, pins, bars, the rider marker, the elevation profile) ever does - content
    /// keeps full alpha and carries its own contrast via <see cref="AddShadow"/> /
    /// <see cref="AddOutline"/>.
    ///
    /// Raise towards 0.35 if a future region's scenery is brighter than Sakura's blossom;
    /// drop towards 0.10 for a "clean screen" photo mode. The whole HUD follows automatically.
    /// </summary>
    public const float PanelAlpha = 0.18f;

    /// <summary>PanelAlpha expressed as a multiplier on the originally tuned glass alphas, so
    /// the art-directed relationship between pane / header / bed / band is preserved.</summary>
    public static float PaneScale => PanelAlpha / Glass.a;

    /// <summary>The main pane background at <see cref="PanelAlpha"/>.</summary>
    public static Color Pane => WithAlpha(Glass, PanelAlpha);
    /// <summary>Title-bar band, one step lighter than the pane.</summary>
    public static Color PaneHeader => WithAlpha(GlassHeader, GlassHeader.a * PaneScale);
    /// <summary>Recessed bed (map bed, bar beds) - the faintest backing.</summary>
    public static Color PaneBed => WithAlpha(GlassBed, GlassBed.a * PaneScale);
    /// <summary>Readout / elevation band backing.</summary>
    public static Color PaneBand => WithAlpha(GlassBand, GlassBand.a * PaneScale);
    /// <summary>Bar bed on a glass pane - replaces the old opaque sand bed.</summary>
    public static Color PaneBarBed => new Color(0.016f, 0.020f, 0.027f, PanelAlpha * 1.6f);

    /// <summary>
    /// A local scrim that sits behind SMALL TYPE ONLY - captions, units, hints, title bars.
    ///
    /// This is a legibility element, not a panel background, and it is sized to the text row
    /// rather than to the card. At <see cref="PanelAlpha"/> a pane is so transparent that
    /// 12-15 px light type over blown-out blossom loses almost all contrast, and the documented
    /// fix on this project is a bed, not a heavier outline: a 1 px 4-copy outline is ~10 % of a
    /// 12 px glyph and fills its counters ("500 m" came back as three solid blocks). The GPS
    /// window's readout bed and scale chip are the same idea, at the same sort of alpha.
    ///
    /// Big numbers never need this - they carry an outline and read fine on the bare pane.
    /// </summary>
    public static Color PaneTextBed => new Color(0.016f, 0.024f, 0.035f, PanelAlpha * 2.4f);

    /// <summary>
    /// Heavier pane for the small top-row widgets (WORLD MAP, ADD HR) and the route status bed.
    /// They sit over open sky, the brightest backdrop in every region, where a pane at
    /// <see cref="PanelAlpha"/> left their captions near-invisible (QA #4). They are small, so
    /// the extra opacity costs almost no view of the road; the big cards keep PanelAlpha.
    /// </summary>
    public const float TopRowPaneAlpha = 0.55f;
    public static Color TopRowPane => WithAlpha(Glass, TopRowPaneAlpha);

    /// <summary>
    /// Drop shadow behind a label. On a translucent pane the backdrop can be anything, so every
    /// glyph carries its own contrast rather than relying on the pane alone.
    /// </summary>
    public static Text AddShadow(Text t, float alpha = 0.78f, float dist = 1.4f)
    {
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0.016f, 0.020f, 0.027f, alpha);
        sh.effectDistance = new Vector2(dist, -dist);
        return t;
    }

    /// <summary>Full outline - for the big numbers and anything that may sit over the map.</summary>
    public static Text AddOutline(Text t, float alpha = 0.85f, float dist = 1.3f)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.016f, 0.020f, 0.027f, alpha);
        o.effectDistance = new Vector2(dist, dist);
        return t;
    }

    /// <summary>Gives a panel soft rounded corners so a translucent pane reads as glass rather
    /// than as a hard rectangular hole punched in the scene.</summary>
    public static RectTransform SoftPanel(Transform parent, string name, Color color,
                                          bool frame = false)
    {
        var rt = Panel(parent, name, color);
        var img = rt.GetComponent<Image>();
        img.sprite = frame ? HudSprites.RoundedFrame : HudSprites.RoundedRect;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
        return rt;
    }

    private static Font _font;

    public static Font Font
    {
        get
        {
            if (_font == null)
            {
                // Unity 6 renamed the built-in Arial to LegacyRuntime.ttf.
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            }
            return _font;
        }
    }

    public static RectTransform Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return (RectTransform)go.transform;
    }

    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Text Label(Transform parent, string name, string text, int size,
                             Color color, TextAnchor anchor = TextAnchor.UpperLeft,
                             FontStyle style = FontStyle.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Font;
        t.fontSize = size;
        t.text = text;
        t.color = color;
        t.alignment = anchor;
        t.fontStyle = style;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = true;
        return t;
    }

    /// <summary>Anchor a rect by explicit min/max fractions plus a pixel offset box.</summary>
    public static RectTransform Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
                                      Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    /// <summary>Anchor a fixed-size rect to a corner.</summary>
    public static RectTransform Corner(RectTransform rt, Vector2 anchor, Vector2 pivot,
                                       Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    public static RouteLineGraphic Line(Transform parent, string name, Color color, float thickness)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                                typeof(RouteLineGraphic));
        go.transform.SetParent(parent, false);
        var g = go.GetComponent<RouteLineGraphic>();
        g.color = color;
        g.Thickness = thickness;
        g.raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return g;
    }

    /// <summary>A square-cornered chip used for the gradient badge and the lap pill.</summary>
    public static RectTransform Chip(Transform parent, string name, Color bg, out Text label,
                                     int fontSize, Color fg)
    {
        var rt = Panel(parent, name, bg);
        label = Label(rt, "Text", "", fontSize, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place((RectTransform)label.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return rt;
    }

    /// <summary>Readout block: a small caption over a big value over a unit.</summary>
    /// <param name="valueColor">Foreground for the big number; defaults to ink on paper.</param>
    /// <param name="captionColor">Foreground for the caption/unit; defaults to soft ink.</param>
    /// <param name="legible">Adds a shadow/outline so the block survives a translucent pane.</param>
    public static void Readout(Transform parent, string name, string caption, float x, float width,
                               out Text value, out Text unit,
                               Color? valueColor = null, Color? captionColor = null,
                               bool legible = false)
    {
        var col = Rect(parent, name);
        Place(col, new Vector2(0f, 0f), new Vector2(0f, 1f),
              new Vector2(x, 0f), new Vector2(x + width, 0f));
        col.anchorMax = new Vector2(0f, 1f);
        col.sizeDelta = new Vector2(width, 0f);
        col.anchoredPosition = new Vector2(x + width * 0.5f, 0f);
        col.pivot = new Vector2(0.5f, 0.5f);

        var cap = Label(col, "Caption", caption, 15, captionColor ?? InkSoft, TextAnchor.UpperCenter,
                        FontStyle.Bold);
        Corner((RectTransform)cap.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
               new Vector2(0f, -1f), new Vector2(width, 18f));

        value = Label(col, "Value", "0", 30, valueColor ?? Ink, TextAnchor.UpperCenter, FontStyle.Bold);
        Corner((RectTransform)value.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
               new Vector2(0f, -19f), new Vector2(width, 34f));

        unit = Label(col, "Unit", "", 14, captionColor ?? InkSoft, TextAnchor.UpperCenter);
        Corner((RectTransform)unit.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
               new Vector2(0f, -53f), new Vector2(width, 18f));

        if (legible)
        {
            // Effect must suit the point size. A 4-copy outline at ~1 px is ~10 % of an 11-13 px
            // glyph and fills its counters - "500 m" came back as three solid blocks. Outline the
            // 30 px value only; small text gets a single-offset shadow, which never closes a
            // counter, and sits on a bed of its own for the rest of its contrast.
            AddShadow(cap, 0.85f, 1.0f);
            AddOutline(value, 0.85f, 1.3f);
            AddShadow(unit, 0.85f, 1.0f);
        }
    }
}

// ---------------------------------------------------------------------------------------
// Procedural sprites. The project has no UI atlas, and a checkpoint pin drawn with the
// default white quad reads as a square blob, so the two shapes the map needs are generated
// once at runtime and cached.
// ---------------------------------------------------------------------------------------
public static partial class HudSprites
{
    private static Sprite _disc, _ring, _chevron, _heart;

    public static Sprite Disc => _disc != null ? _disc : (_disc = MakeDisc(64, 0f));
    public static Sprite Ring => _ring != null ? _ring : (_ring = MakeDisc(64, 0.62f));
    public static Sprite Chevron => _chevron != null ? _chevron : (_chevron = MakeChevron(64));
    public static Sprite Heart => _heart != null ? _heart : (_heart = MakeHeart(96));

    private static Sprite Finish(Texture2D tex)
    {
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                             new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    private static Sprite MakeDisc(int size, float innerFraction)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HudDisc" };
        float r = size * 0.5f - 1f;
        float inner = r * innerFraction;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - size * 0.5f;
            float dy = y + 0.5f - size * 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(r - d);
            if (innerFraction > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - inner));
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        return Finish(tex);
    }

    private static Sprite MakeChevron(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HudChevron" };
        var px = new Color[size * size];
        // An upward arrowhead: apex at the top centre, base at 22 % height, notched.
        Vector2 apex = new Vector2(0.5f, 0.94f);
        Vector2 bl = new Vector2(0.16f, 0.10f);
        Vector2 br = new Vector2(0.84f, 0.10f);
        Vector2 notch = new Vector2(0.5f, 0.38f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
            bool inside = InTri(p, apex, bl, notch) || InTri(p, apex, notch, br);
            px[y * size + x] = new Color(1f, 1f, 1f, inside ? 1f : 0f);
        }
        tex.SetPixels(px);
        return Finish(tex);
    }

    private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0;
        bool pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    // A filled heart, drawn from the classic implicit curve
    // (x^2 + y^2 - 1)^3 - x^2 y^3 <= 0 (lobes up, point down), 3x3 supersampled for clean edges.
    private static Sprite MakeHeart(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HudHeart" };
        var px = new Color[size * size];
        const int ss = 3;
        const float half = 1.6f;    // fits the ~2.4 wide / ~2.7 tall heart with a little padding
        const float yShift = 0.05f; // keep the bottom tip off the edge
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int inside = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                float fx = (x + (sx + 0.5f) / ss) / size;
                float fy = (y + (sy + 0.5f) / ss) / size;
                float nx = (fx * 2f - 1f) * half;
                float ny = (fy * 2f - 1f) * half + yShift;
                float a = nx * nx + ny * ny - 1f;
                if (a * a * a - nx * nx * ny * ny * ny <= 0f) inside++;
            }
            px[y * size + x] = new Color(1f, 1f, 1f, inside / (float)(ss * ss));
        }
        tex.SetPixels(px);
        return Finish(tex);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
        (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
}

public static partial class HudSprites
{
    private static Sprite _rounded, _roundedFrame, _roundedSmall, _sheen;

    /// <summary>Soft-cornered pane, 9-sliced. Border &gt; corner radius so corners never stretch.</summary>
    public static Sprite RoundedRect =>
        _rounded != null ? _rounded : (_rounded = MakeRounded(64, 17f, 0f));

    /// <summary>Matching hairline border - the only hard edge a glass pane gets.</summary>
    public static Sprite RoundedFrame =>
        _roundedFrame != null ? _roundedFrame : (_roundedFrame = MakeRounded(64, 17f, 1.6f));

    /// <summary>
    /// Small-radius variant for chips and badges. <see cref="RoundedRect"/>'s 21 px 9-slice
    /// border is taller than a 20 px chip, and Unity does not clamp oversized borders - the
    /// top and bottom slices overlap and the chip renders as a pinched blob.
    /// </summary>
    public static Sprite RoundedSmall =>
        _roundedSmall != null ? _roundedSmall : (_roundedSmall = MakeRounded(32, 7f, 0f, 9f));

    /// <summary>Top-down white ramp. Fakes the lit edge of frosted glass without a blur pass.</summary>
    public static Sprite Sheen => _sheen != null ? _sheen : (_sheen = MakeSheen(8, 64));

    private static Sprite MakeRounded(int size, float radius, float frameThickness,
                                      float border = -1f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = frameThickness > 0f ? "HudRoundedFrame" : "HudRounded" };
        var px = new Color[size * size];
        float h = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Signed distance to a rounded box: negative inside, zero on the edge.
            float qx = Mathf.Abs(x + 0.5f - h) - (h - radius);
            float qy = Mathf.Abs(y + 0.5f - h) - (h - radius);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
            float a = Mathf.Clamp01(0.5f - d);                       // 1 px of analytic AA
            if (frameThickness > 0f) a -= Mathf.Clamp01(0.5f - (d + frameThickness));
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, false);
        float b = border > 0f ? border : radius + 4f;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }

    private static Sprite MakeSheen(int w, int hgt)
    {
        var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false) { name = "HudSheen" };
        var px = new Color[w * hgt];
        for (int y = 0; y < hgt; y++)
        {
            float f = y / (float)(hgt - 1);                          // 0 = bottom, 1 = top
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1f, f));
            for (int x = 0; x < w; x++) px[y * w + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, false);
        return Sprite.Create(tex, new Rect(0, 0, w, hgt), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect);
    }
}

public static partial class HudSprites
{
    private static Sprite _white;

    /// <summary>
    /// A plain white sprite.
    ///
    /// <c>Image.Type.Filled</c> silently does nothing when the Image has no sprite - the bar
    /// then renders at full width no matter what fillAmount says, which is exactly how the
    /// %FTP and ride-progress bars first shipped reading 100 % permanently.
    /// </summary>
    public static Sprite White
    {
        get
        {
            if (_white == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "HudWhite" };
                var px = new Color[16];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                tex.SetPixels(px);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply(false, false);
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f),
                                       100f, 0, SpriteMeshType.FullRect);
            }
            return _white;
        }
    }

    /// <summary>Unity's built-in UI layer. HUD geometry lives here so the gameplay camera - and
    /// therefore the sunset post-FX grade - never touches it.</summary>
    public const int UiLayer = 5;

    public static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursively(t.gameObject, layer);
    }
}

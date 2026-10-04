using UnityEngine;

// ---------------------------------------------------------------------------------------
// Procedural icons for the compact ride HUD (telemetry pill, route progress bar, circular
// minimap). Same reasoning as the rest of HudSprites: the project has no UI atlas, and a
// handful of flat glyphs is cheaper to generate once at runtime than to author, import and
// keep in sync as textures. Every glyph is WHITE (except the two-tone finish flag) so the
// Image tint decides its colour.
// ---------------------------------------------------------------------------------------
public static partial class HudSprites
{
    private static Sprite _bolt, _gauge, _finishFlag, _discLarge, _thinRing;

    /// <summary>Lightning bolt - the power (watts) glyph in the telemetry pill.</summary>
    public static Sprite Bolt => _bolt != null ? _bolt : (_bolt = MakeBolt(96));

    /// <summary>A small speedometer dial (open-bottom arc + needle) - the cadence glyph.</summary>
    public static Sprite Gauge => _gauge != null ? _gauge : (_gauge = MakeGauge(96));

    /// <summary>Chequered flag on a pole - marks the finish end of the route progress bar.
    /// Two-tone (white / near-black) in the texture itself, so tint it white.</summary>
    public static Sprite FinishFlag =>
        _finishFlag != null ? _finishFlag : (_finishFlag = MakeFinishFlag(96));

    /// <summary>
    /// A 256 px disc. The 64 px <see cref="Disc"/> is fine for pins and dots, but scaled up to a
    /// 260 px minimap its one-texel anti-aliased edge becomes a 4 px smear, and as a Mask
    /// graphic that smear is exactly where the route lines get clipped.
    /// </summary>
    public static Sprite DiscLarge => _discLarge != null ? _discLarge : (_discLarge = MakeDisc(256, 0f));

    /// <summary>A hairline ring (~2 px at 256 px) - the minimap's light rim.</summary>
    public static Sprite ThinRing => _thinRing != null ? _thinRing : (_thinRing = MakeDisc(256, 0.984f));

    private static Sprite _pill, _pillFrame, _hairRing;

    /// <summary>A ~2 px ring at badge size (28-32 px). <see cref="Ring"/>'s 0.62 inner radius
    /// is a thick donut at that size and <see cref="ThinRing"/> vanishes below a pixel.</summary>
    public static Sprite HairRing => _hairRing != null ? _hairRing : (_hairRing = MakeDisc(64, 0.88f));

    /// <summary>
    /// Fully-rounded capsule, 9-sliced with a 32 texel border on a 64 texel texture - i.e. the
    /// corner radius IS half the texture. Set <c>Image.pixelsPerUnitMultiplier</c> to
    /// 64 / height so the caps render as exact semicircles at whatever height the pill is;
    /// <see cref="RoundedRect"/>'s 17 px radius on a 52 px strip reads as a box, not a pill.
    /// </summary>
    public static Sprite Pill => _pill != null ? _pill : (_pill = MakeRounded(64, 31.5f, 0f, 32f));

    /// <summary>Hairline outline matching <see cref="Pill"/>.</summary>
    public static Sprite PillFrame =>
        _pillFrame != null ? _pillFrame : (_pillFrame = MakeRounded(64, 31.5f, 1.8f, 32f));

    // Supersampling factor for the icon rasterisers: 4x4 keeps 20-24 px glyphs clean-edged.
    private const int IconSS = 4;

    private delegate bool IconShape(Vector2 p);

    /// <summary>Rasterises a boolean shape (in 0..1 texture space, y up) with box supersampling.</summary>
    private static Sprite RasterIcon(int size, string name, IconShape inside)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int hit = 0;
            for (int sy = 0; sy < IconSS; sy++)
            for (int sx = 0; sx < IconSS; sx++)
            {
                var p = new Vector2((x + (sx + 0.5f) / IconSS) / size, (y + (sy + 0.5f) / IconSS) / size);
                if (inside(p)) hit++;
            }
            px[y * size + x] = new Color(1f, 1f, 1f, hit / (float)(IconSS * IconSS));
        }
        tex.SetPixels(px);
        return Finish(tex);
    }

    /// <summary>Even-odd point-in-polygon; the bolt is concave so a triangle fan will not do.</summary>
    private static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    private static Sprite MakeBolt(int size)
    {
        // A classic two-stroke bolt, leaning right, filling most of the square so it reads at
        // the same optical weight as the heart beside it.
        var poly = new[]
        {
            new Vector2(0.64f, 0.97f), new Vector2(0.20f, 0.44f), new Vector2(0.47f, 0.44f),
            new Vector2(0.36f, 0.03f), new Vector2(0.82f, 0.58f), new Vector2(0.54f, 0.58f),
        };
        return RasterIcon(size, "HudBolt", p => InPolygon(p, poly));
    }

    private static Sprite MakeGauge(int size)
    {
        var c = new Vector2(0.5f, 0.46f);
        const float outer = 0.44f, inner = 0.33f;
        // Needle pointing up-right (~40 degrees right of vertical): "spinning well".
        var tip = c + new Vector2(Mathf.Sin(40f * Mathf.Deg2Rad), Mathf.Cos(40f * Mathf.Deg2Rad)) * 0.30f;
        return RasterIcon(size, "HudGauge", p =>
        {
            var d = p - c;
            float r = d.magnitude;
            // Arc: everything but a 100-degree wedge centred straight down (the dial opening).
            if (r <= outer && r >= inner)
            {
                float ang = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;    // 0 = straight down
                if (Mathf.Abs(ang) > 50f) return true;
            }
            if (r <= 0.085f) return true;                                // hub
            // Needle: distance from p to the hub->tip segment.
            var seg = tip - c;
            float t = Mathf.Clamp01(Vector2.Dot(d, seg) / seg.sqrMagnitude);
            return (d - seg * t).magnitude <= 0.045f;
        });
    }

    private static Sprite MakeFinishFlag(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HudFinishFlag" };
        var px = new Color[size * size];
        var dark = new Color(0.07f, 0.08f, 0.10f, 1f);
        const float poleL = 0.14f, poleR = 0.23f, flagL = 0.23f, flagR = 0.92f;
        const float flagB = 0.46f, flagT = 0.94f;
        const int cols = 4, rows = 3;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float r = 0f, g = 0f, b = 0f, a = 0f;
            for (int sy = 0; sy < IconSS; sy++)
            for (int sx = 0; sx < IconSS; sx++)
            {
                float fx = (x + (sx + 0.5f) / IconSS) / size;
                float fy = (y + (sy + 0.5f) / IconSS) / size;
                Color s;
                if (fx >= poleL && fx < poleR && fy >= 0.03f && fy <= 0.97f) s = Color.white;
                else if (fx >= flagL && fx <= flagR && fy >= flagB && fy <= flagT)
                {
                    int cx = Mathf.Min(cols - 1, (int)((fx - flagL) / (flagR - flagL) * cols));
                    int cy = Mathf.Min(rows - 1, (int)((fy - flagB) / (flagT - flagB) * rows));
                    s = ((cx + cy) & 1) == 0 ? Color.white : dark;
                }
                else continue;
                r += s.r; g += s.g; b += s.b; a += 1f;
            }
            // Un-premultiplied average over the covered samples, alpha = coverage.
            px[y * size + x] = a > 0f
                ? new Color(r / a, g / a, b / a, a / (IconSS * IconSS))
                : new Color(1f, 1f, 1f, 0f);
        }
        tex.SetPixels(px);
        return Finish(tex);
    }
}

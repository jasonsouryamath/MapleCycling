using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen transition shown while a region scene loads from the world map. A rendered
/// picture of the destination (Resources/LoadingScreens/&lt;regionId&gt;, falls back to a plain
/// dusk gradient when a region has none yet), the region's name, and a silhouette of Kuro
/// pedalling along the progress bar - the bar IS the road, and Kuro arrives at the end of it.
///
/// Owns its own DontDestroyOnLoad root so it survives the scene swap, and runs on unscaled time
/// because the world clock is frozen (timeScale 0) for the whole load.
/// </summary>
public sealed class RegionLoadingScreen : MonoBehaviour
{
    public const string RootName = "MapleRide Region Loading";
    private const string ArtFolder = "LoadingScreens/";

    // Silhouette colour and cadence. Provisional tuning.
    private static readonly Color Silhouette = new Color(0.97f, 0.95f, 0.90f, 1f);
    private const float CadenceRadPerSec = 8.6f;
    private const float WheelRadPerSec = 15f;
    private const float RiderScale = 0.55f;

    private CanvasGroup _group;
    private RawImage _art;
    private RectTransform _artRect;
    private Texture2D _artTexture;
    private Text _status;
    private Text _percent;
    private RectTransform _fill;
    private RectTransform _rider;
    private RectTransform[] _speedLines;
    private Rider _kuro;

    private float _target;
    private float _shown;
    private float _fade;
    private float _born;
    private bool _finishing;
    private float _phase;
    private float _wheel;

    public static RegionLoadingScreen Show(RegionCatalog.Region region)
    {
        var root = new GameObject(RootName);
        DontDestroyOnLoad(root);
        var screen = root.AddComponent<RegionLoadingScreen>();
        screen.Build(region);
        return screen;
    }

    public void SetProgress(float t) => _target = Mathf.Clamp01(t);

    public void SetStatus(string text) { if (_status != null) _status.text = text; }

    /// <summary>Fill the bar, hold a beat so Kuro reaches the end, then fade away.</summary>
    public void Finish()
    {
        _target = 1f;
        _finishing = true;
    }

    /// <summary>Drop the screen at once (a failed load hands control back to the map).</summary>
    public void Dismiss() => Destroy(gameObject);

    private void OnDestroy()
    {
        if (_artTexture != null && !Application.isEditor) Resources.UnloadAsset(_artTexture);
    }

    // ----------------------------------------------------------------- layout

    private void Build(RegionCatalog.Region region)
    {
        _born = Time.unscaledTime;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100;   // above the boot menu (1000) and the streamer card (900)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = true;   // nothing underneath may be clicked mid-load
        var rootRect = (RectTransform)transform;

        // Backdrop, clipped so the slow push-in never spills out of the screen.
        var clip = HudKit.Rect(rootRect, "Backdrop Clip");
        HudKit.Place(clip, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        clip.gameObject.AddComponent<RectMask2D>();
        _artRect = HudKit.Rect(clip, "Art");
        HudKit.Place(_artRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _art = _artRect.gameObject.AddComponent<RawImage>();
        _art.raycastTarget = false;
        _artTexture = region != null ? Resources.Load<Texture2D>(ArtFolder + region.Id) : null;
        if (_artTexture != null) { _art.texture = _artTexture; _art.color = Color.white; }
        else _art.color = new Color(0.09f, 0.14f, 0.22f, 1f);

        // Dark foot so the name, bar and silhouette read over any picture.
        var foot = new GameObject("Foot Shade", typeof(RectTransform), typeof(RawImage));
        foot.transform.SetParent(rootRect, false);
        HudKit.Place((RectTransform)foot.transform, Vector2.zero, new Vector2(1, 0.62f), Vector2.zero, Vector2.zero);
        var footImage = foot.GetComponent<RawImage>();
        footImage.texture = VerticalFade(0.88f);
        footImage.raycastTarget = false;

        // Region title block, lower left.
        string name = region != null ? region.DisplayName.ToUpperInvariant() : "MAPLERIDE";
        var caption = HudKit.Label(rootRect, "Caption", "NOW ENTERING", 26, HudKit.WithAlpha(HudKit.Chalk, 0.75f),
                                   TextAnchor.LowerLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)caption.transform, new Vector2(0, 0), new Vector2(0, 0),
                      new Vector2(120, 360), new Vector2(900, 36));
        var title = HudKit.Label(rootRect, "Region Name", name, 112, HudKit.Chalk, TextAnchor.LowerLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)title.transform, new Vector2(0, 0), new Vector2(0, 0),
                      new Vector2(116, 238), new Vector2(1500, 130));
        HudKit.AddShadow(title, 0.7f, 3f);
        if (region != null && !string.IsNullOrEmpty(region.Tagline))
        {
            var tag = HudKit.Label(rootRect, "Tagline", region.Tagline, 36, HudKit.WithAlpha(HudKit.Chalk, 0.9f),
                                   TextAnchor.LowerLeft, FontStyle.Italic);
            HudKit.Corner((RectTransform)tag.transform, new Vector2(0, 0), new Vector2(0, 0),
                          new Vector2(122, 190), new Vector2(1200, 44));
            HudKit.AddShadow(tag, 0.7f, 2f);
        }

        // The road: a track along the bottom with Kuro riding its fill edge.
        var track = HudKit.Panel(rootRect, "Track", new Color(1f, 1f, 1f, 0.28f));
        HudKit.Place(track, new Vector2(0, 0), new Vector2(1, 0), new Vector2(120, 118), new Vector2(-120, 124));
        var fill = HudKit.Panel(track, "Fill", HudKit.Ember);
        _fill = fill;
        HudKit.Place(_fill, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);

        _rider = HudKit.Rect(track, "Kuro");
        _rider.anchorMin = _rider.anchorMax = new Vector2(0, 1f);   // top edge of the track = the road surface
        _rider.sizeDelta = Vector2.zero;
        _rider.localScale = Vector3.one * RiderScale;
        _speedLines = new RectTransform[3];
        for (int i = 0; i < _speedLines.Length; i++)
        {
            _speedLines[i] = HudKit.Panel(_rider, "Speed " + i, new Color(1f, 1f, 1f, 0.4f));
            _speedLines[i].anchorMin = _speedLines[i].anchorMax = new Vector2(0.5f, 0.5f);
        }
        _kuro = new Rider(_rider);

        _status = HudKit.Label(rootRect, "Status", "LOADING…", 26, HudKit.WithAlpha(HudKit.Chalk, 0.85f),
                               TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.Corner((RectTransform)_status.transform, new Vector2(0, 0), new Vector2(0, 0),
                      new Vector2(120, 76), new Vector2(1300, 34));
        _percent = HudKit.Label(rootRect, "Percent", "0%", 26, HudKit.Chalk, TextAnchor.UpperRight, FontStyle.Bold);
        HudKit.Corner((RectTransform)_percent.transform, new Vector2(1, 0), new Vector2(1, 0),
                      new Vector2(-120, 76), new Vector2(240, 34));

        HudSprites.SetLayerRecursively(gameObject, HudSprites.UiLayer);
        Tick(0f);
    }

    // ----------------------------------------------------------------- per frame

    private void Update() => Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));

    private void Tick(float dt)
    {
        float age = Time.unscaledTime - _born;

        // Bar eases toward the real progress so scene-load jumps read as riding, not teleporting.
        _shown = Mathf.MoveTowards(_shown, _target, dt * (_finishing ? 0.9f : 0.55f));
        if (_finishing && _shown >= 0.999f) _fade = Mathf.MoveTowards(_fade, 1f, dt / 0.45f);
        float fadeIn = Mathf.Clamp01(age / 0.25f);
        if (_group != null) _group.alpha = fadeIn * (1f - _fade);
        if (_finishing && _fade >= 1f) { Destroy(gameObject); return; }

        if (_percent != null) _percent.text = Mathf.RoundToInt(_shown * 100f) + "%";
        if (_fill != null) _fill.anchorMax = new Vector2(_shown, 1f);
        if (_rider != null) { _rider.anchorMin = _rider.anchorMax = new Vector2(_shown, 1f); _rider.anchoredPosition = Vector2.zero; }

        // Slow push-in on the picture.
        if (_artRect != null)
        {
            float s = 1f + Mathf.Min(age, 12f) * 0.006f;
            _artRect.localScale = new Vector3(s, s, 1f);
            if (_artTexture != null) CoverUv(_art, _artTexture.width / (float)_artTexture.height);
        }

        _phase += dt * CadenceRadPerSec;
        _wheel -= dt * WheelRadPerSec;
        _kuro?.Pose(_phase, _wheel);
        if (_speedLines != null)
            for (int i = 0; i < _speedLines.Length; i++)
            {
                float flicker = 0.5f + 0.5f * Mathf.Sin(age * 9f + i * 2.1f);
                float length = 90f + i * 30f + flicker * 40f;
                _speedLines[i].sizeDelta = new Vector2(length, 4f);
                _speedLines[i].anchoredPosition = new Vector2(-230f - length * 0.5f - i * 12f, 70f + i * 62f);
                _speedLines[i].GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f + 0.26f * flicker);
            }
    }

    /// <summary>Aspect-fill: crop the picture's UVs so it covers the screen without stretching.</summary>
    private static void CoverUv(RawImage image, float textureAspect)
    {
        float screenAspect = Screen.width / (float)Mathf.Max(1, Screen.height);
        var rect = new Rect(0, 0, 1, 1);
        if (screenAspect > textureAspect)
        {
            rect.height = textureAspect / screenAspect;
            rect.y = (1f - rect.height) * 0.5f;
        }
        else
        {
            rect.width = screenAspect / textureAspect;
            rect.x = (1f - rect.width) * 0.5f;
        }
        image.uvRect = rect;
    }

    private static Texture2D VerticalFade(float bottomAlpha)
    {
        const int h = 64;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { name = "LoadingFootFade" };
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
        {
            float t = 1f - y / (h - 1f);   // 1 at the bottom row
            float a = bottomAlpha * t * t * (3f - 2f * t);
            tex.SetPixel(0, y, new Color(0.02f, 0.04f, 0.08f, a));
        }
        tex.Apply(false, true);
        return tex;
    }

    // ----------------------------------------------------------------- Kuro silhouette

    /// <summary>
    /// Side-on cyclist built from UI primitives, riding to the right. Local units: ground at y = 0,
    /// wheel centres at y = wheelRadius. The legs are a real two-bone solve against the crank, so
    /// the pedalling is mechanically right rather than a canned flipbook.
    /// </summary>
    private sealed class Rider
    {
        private const float WheelR = 62f;
        private static readonly Vector2 Rear = new Vector2(-105, WheelR);
        private static readonly Vector2 Front = new Vector2(105, WheelR);
        private static readonly Vector2 Bb = new Vector2(-5, 67);
        private static readonly Vector2 Seat = new Vector2(-40, 158);
        private static readonly Vector2 Head = new Vector2(70, 150);
        private static readonly Vector2 Bars = new Vector2(88, 166);
        private const float Crank = 34f;

        private readonly RectTransform _root;
        private readonly RectTransform[] _rearSpokes = new RectTransform[3];
        private readonly RectTransform[] _frontSpokes = new RectTransform[3];
        private readonly RectTransform _rearRing, _frontRing;
        private readonly Bone _thighA, _shinA, _thighB, _shinB, _torso, _upper, _fore, _neck;
        private readonly RectTransform _helmet, _helmetTail, _headDisc;
        private readonly RectTransform _crankDisc;

        public Rider(RectTransform root)
        {
            _root = root;

            // Frame and fork: static bones, built once.
            new Bone(root, Silhouette, 9f).Set(Rear, Bb);
            new Bone(root, Silhouette, 9f).Set(Bb, Seat);
            new Bone(root, Silhouette, 7f).Set(Rear, Seat);
            new Bone(root, Silhouette, 8f).Set(Seat, Head);
            new Bone(root, Silhouette, 10f).Set(Bb, Head);
            new Bone(root, Silhouette, 8f).Set(Head, Front);
            new Bone(root, Silhouette, 8f).Set(Head + new Vector2(0, 8), Bars);
            new Bone(root, Silhouette, 12f).Set(Seat + new Vector2(-14, 2), Seat + new Vector2(14, 2));

            _rearRing = Ring(root, Rear);
            _frontRing = Ring(root, Front);
            for (int i = 0; i < 3; i++)
            {
                _rearSpokes[i] = Spoke(root);
                _frontSpokes[i] = Spoke(root);
            }
            _crankDisc = Disc(root, Bb, 16f);

            _thighB = new Bone(root, new Color(Silhouette.r * 0.82f, Silhouette.g * 0.82f, Silhouette.b * 0.82f, 1f), 22f);
            _shinB = new Bone(root, new Color(Silhouette.r * 0.82f, Silhouette.g * 0.82f, Silhouette.b * 0.82f, 1f), 17f);
            _thighA = new Bone(root, Silhouette, 24f);
            _shinA = new Bone(root, Silhouette, 18f);
            _torso = new Bone(root, Silhouette, 38f);
            _neck = new Bone(root, Silhouette, 16f);
            _upper = new Bone(root, Silhouette, 17f);
            _fore = new Bone(root, Silhouette, 14f);
            _headDisc = Disc(root, Vector2.zero, 32f);
            _helmet = Disc(root, Vector2.zero, 40f);
            _helmetTail = Disc(root, Vector2.zero, 30f);
        }

        public void Pose(float cadence, float wheel)
        {
            Vector2 bob = new Vector2(0f, Mathf.Sin(cadence * 2f) * 2.5f);
            Vector2 hip = new Vector2(-42, 166) + bob;
            Vector2 shoulder = new Vector2(24, 226) + bob * 1.2f;

            SpinWheel(_rearSpokes, Rear, wheel);
            SpinWheel(_frontSpokes, Front, wheel);

            // Legs: pedal A on the crank, pedal B opposite.
            Vector2 pedalA = Bb + Crank * new Vector2(Mathf.Cos(cadence), Mathf.Sin(cadence));
            Vector2 pedalB = Bb - Crank * new Vector2(Mathf.Cos(cadence), Mathf.Sin(cadence));
            Vector2 kneeB = Solve(hip, pedalB, 68f, 70f, 1f);
            _thighB.Set(hip, kneeB);
            _shinB.Set(kneeB, pedalB);
            Vector2 kneeA = Solve(hip, pedalA, 68f, 70f, 1f);
            _thighA.Set(hip, kneeA);
            _shinA.Set(kneeA, pedalA);

            // Torso leaning into the bars, head forward, arm reaching down to them.
            _torso.Set(hip, shoulder);
            Vector2 headPos = shoulder + new Vector2(20, 24);
            _neck.Set(shoulder, headPos - new Vector2(4, 8));
            Place(_headDisc, headPos, 32f, 32f, 0f);
            Place(_helmet, headPos + new Vector2(2, 9), 46f, 32f, -12f);
            Place(_helmetTail, headPos + new Vector2(-18, 9), 40f, 20f, 14f);
            Vector2 elbow = Solve(shoulder, Bars, 55f, 55f, -1f);
            _upper.Set(shoulder, elbow);
            _fore.Set(elbow, Bars);
        }

        // ---- two-bone solve: joint point between a and b, bent toward `side` of the a->b line.
        private static Vector2 Solve(Vector2 a, Vector2 b, float l1, float l2, float side)
        {
            Vector2 d = b - a;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.01f, l1 + l2 - 0.01f);
            Vector2 dir = d.normalized;
            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            Vector2 perp = new Vector2(-dir.y, dir.x) * side;
            return a + dir * along + perp * h;
        }

        private void SpinWheel(RectTransform[] spokes, Vector2 centre, float angle)
        {
            for (int i = 0; i < spokes.Length; i++)
            {
                spokes[i].anchoredPosition = centre;
                spokes[i].localRotation = Quaternion.Euler(0, 0, (angle * Mathf.Rad2Deg) + i * 60f);
            }
        }

        private static RectTransform Spoke(RectTransform parent)
        {
            var r = HudKit.Panel(parent, "Spoke", new Color(Silhouette.r, Silhouette.g, Silhouette.b, 0.75f));
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(WheelR * 2f - 8f, 3f);
            return r;
        }

        private static RectTransform Ring(RectTransform parent, Vector2 centre)
        {
            var go = new GameObject("Wheel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = HudSprites.Ring;
            image.color = Silhouette;
            image.raycastTarget = false;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(WheelR * 2f, WheelR * 2f);
            r.anchoredPosition = centre;
            return r;
        }

        private static RectTransform Disc(RectTransform parent, Vector2 centre, float size)
        {
            var go = new GameObject("Disc", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = HudSprites.Disc;
            image.color = Silhouette;
            image.raycastTarget = false;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            r.anchoredPosition = centre;
            return r;
        }

        private static void Place(RectTransform r, Vector2 pos, float w, float h, float degrees)
        {
            r.anchoredPosition = pos;
            r.sizeDelta = new Vector2(w, h);
            r.localRotation = Quaternion.Euler(0, 0, degrees);
        }
    }

    /// <summary>A thick line with round ends, repositioned by its two end points.</summary>
    private sealed class Bone
    {
        private readonly RectTransform _bar;
        private readonly RectTransform _capA, _capB;
        private readonly float _thickness;

        public Bone(RectTransform parent, Color color, float thickness)
        {
            _thickness = thickness;
            _bar = HudKit.Panel(parent, "Bone", color);
            _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0.5f);
            _capA = Cap(parent, color);
            _capB = Cap(parent, color);
        }

        private RectTransform Cap(RectTransform parent, Color color)
        {
            var go = new GameObject("Cap", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = HudSprites.Disc;
            image.color = color;
            image.raycastTarget = false;
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(_thickness, _thickness);
            return r;
        }

        public void Set(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            _bar.anchoredPosition = (a + b) * 0.5f;
            _bar.sizeDelta = new Vector2(d.magnitude, _thickness);
            _bar.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            _capA.anchoredPosition = a;
            _capB.anchoredPosition = b;
        }
    }
}

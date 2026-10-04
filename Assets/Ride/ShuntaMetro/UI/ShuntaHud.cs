using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shunta Metro HUD overlay (self-bootstrapping, active only when the session course id is "shunta_metro"):
///  1. zone banner toast (name + landmark, zone colour, fade) on entering each of the 12 zones,
///  2. live elevation-profile strip along the bottom (zone-coloured fill, rider marker, labelled climbs),
///  3. status text hooks: active segment / last segment result, chevron boost, plus <see cref="PostStatus"/>.
/// Pure uGUI built procedurally; font via the same fallback as HudKit. No prefabs, no imports.
/// Gameplay hooks: assign <see cref="Boost"/> (a ShuntaBoostState the ride loop ticks) to show boost/cooldown.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShuntaHud : MonoBehaviour
{
    public const string CourseId = "shunta_metro";

    public static ShuntaHud Instance { get; private set; }

    /// <summary>Optional: the live boost state ticked by the ride loop. Null = boost line hidden.</summary>
    public ShuntaBoostState Boost;
    /// <summary>Optional: chevrons to show as ticks on the profile strip.</summary>
    public ShuntaChevron[] Chevrons;
    /// <summary>Diagnostics (smoke test): zone banners shown since activation, and the last zone index shown.</summary>
    public int BannersShown { get; private set; }
    public int LastBannerZone => _lastZone;
    public bool IsActive => _active;

    RideSession _session;
    ShuntaCourseData _data;
    ShuntaSegmentTimer _timer;
    List<ShuntaSegmentDef> _segs;
    ShuntaSegmentBoard _board;
    float _poll;
    bool _active;
    int _lastZone = -1;
    float _kmScale = 1f;

    Canvas _canvas;
    CanvasGroup _root;
    CanvasGroup _bannerGroup;
    Image _bannerBar;
    Text _bannerTitle, _bannerSub;
    float _bannerT = 99f;
    const float BannerHold = 2.6f, BannerFade = 0.9f;

    ProfileGraphic _profile;
    RectTransform _marker, _stripRect;
    Text _statusText;
    string _toast = ""; float _toastLeft;
    string _activeSeg = "";
    readonly List<RectTransform> _chevTicks = new List<RectTransform>();
    float _minM, _maxM;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        if (Environment.GetEnvironmentVariable("MR_SHUNTA_HUD") == "0") return;
        var go = new GameObject("~ShuntaHud");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaHud>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Show a one-line status message (segment result, boost pickup, ...) for a few seconds.</summary>
    public void PostStatus(string text, float seconds = 3f) { _toast = text ?? ""; _toastLeft = seconds; }

    /// <summary>Course km for the rider (session metres scaled so the full route maps to the course's distanceKm).</summary>
    public float RiderKm => _session == null ? 0f : _session.DistanceM * 0.001f * _kmScale;

    void Update()
    {
        _poll -= Time.unscaledDeltaTime;
        if (_poll <= 0f)
        {
            _poll = 0.5f;
            if (_session == null) _session = FindAnyObjectByType<RideSession>();
            bool want = _session != null && _session.Course != null && _session.Course.Id == CourseId;
            if (want && !_active) Activate();
            else if (!want && _active) Deactivate();
        }
        if (!_active) return;

        float dt = Time.unscaledDeltaTime;
        float km = RiderKm;

        // 1. zone banner
        var z = _data.ZoneAtKm(km);
        if (z != null && z.index != _lastZone)
        {
            _lastZone = z.index;
            ShowBanner(z);
        }
        UpdateBanner(dt);

        // 2. profile marker
        float total = Mathf.Max(0.1f, _data.distanceKm);
        float u = Mathf.Clamp01(km / total);
        float w = _stripRect.rect.width;
        _marker.anchoredPosition = new Vector2(u * w, 0f);

        // 3. status hooks
        _timer?.Tick(km, _session.ElapsedSeconds);
        _activeSeg = "";
        if (_segs != null && _timer != null)
            for (int i = 0; i < _segs.Count; i++)
                if (_timer.IsActive(i)) { _activeSeg = _segs[i].name; break; }
        if (_toastLeft > 0f) _toastLeft -= dt;
        // build the string only when there is something to show (no per-frame List/string garbage otherwise)
        _statusText.text = (_toastLeft > 0f || _activeSeg.Length > 0 || Boost != null) ? BuildStatus() : "";
    }

    string BuildStatus()
    {
        var parts = new List<string>(3);
        if (_toastLeft > 0f && _toast.Length > 0) parts.Add(_toast);
        if (_activeSeg.Length > 0) parts.Add("SEGMENT  " + _activeSeg.ToUpperInvariant());
        if (Boost != null)
        {
            if (Boost.Boosting) parts.Add("BOOST x" + Boost.SpeedMultiplier.ToString("0.00"));
            else if (Boost.OnCooldown) parts.Add("BOOST READY IN " + Mathf.CeilToInt(Boost.CooldownLeft) + "s");
            if (Boost.Collected > 0) parts.Add("CHEVRONS " + Boost.Collected + "/" + ShuntaChevronLayout.Count);
        }
        return string.Join("    |    ", parts);
    }

    // ---- activation -----------------------------------------------------------------------------
    void Activate()
    {
        _data = ShuntaCourseData.Load();
        if (_data == null || _data.zones == null || _data.zones.Length == 0) { _data = null; return; }
        _active = true;
        _lastZone = -1;
        float lenM = _session.Course.Length;
        _kmScale = lenM > 1f ? _data.distanceKm * 1000f / lenM : 1f;
        _segs = ShuntaSegmentTable.Build(_data);
        _board = ShuntaSegmentBoard.Seeded(_segs);
        _timer = new ShuntaSegmentTimer(_segs);
        _timer.Finished += OnSegmentFinished;
        if (Chevrons == null) Chevrons = ShuntaChevronLayout.Place(_data);
        BuildUi();
    }

    void Deactivate()
    {
        _active = false;
        if (_timer != null) _timer.Finished -= OnSegmentFinished;
        _timer = null;
        if (_canvas != null) Destroy(_canvas.gameObject);
        _canvas = null; _chevTicks.Clear();
    }

    void OnSegmentFinished(ShuntaSegmentDef def, float seconds)
    {
        int rank = _board != null ? _board.Submit(def.id, "You", seconds) : 0;
        int m = (int)(seconds / 60f); float s = seconds - m * 60f;
        PostStatus($"{def.name.ToUpperInvariant()}  {m}:{s:00.0}" + (rank > 0 ? $"  #{rank}" : ""), 5f);
    }

    // ---- banner ---------------------------------------------------------------------------------
    void ShowBanner(ShuntaZone z)
    {
        var c = z.Color32;
        _bannerTitle.text = z.name.ToUpperInvariant();
        _bannerSub.text = string.IsNullOrEmpty(z.landmark) ? $"ZONE {z.index} OF {_data.zones.Length}" : z.landmark;
        _bannerTitle.color = Color.white;
        _bannerSub.color = Color.Lerp(c, Color.white, 0.35f);
        _bannerBar.color = c;
        _bannerT = 0f;
        BannersShown++;
    }

    void UpdateBanner(float dt)
    {
        _bannerT += dt;
        float a = _bannerT < 0.25f ? _bannerT / 0.25f
                : _bannerT < BannerHold ? 1f
                : 1f - (_bannerT - BannerHold) / BannerFade;
        _bannerGroup.alpha = Mathf.Clamp01(a);
    }

    // ---- UI construction ------------------------------------------------------------------------
    static Font _font;
    static Font GetFont()
    {
        if (_font != null) return _font;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return _font;
    }

    static RectTransform Rt(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Text MakeText(string name, Transform parent, int size, Color col, TextAnchor anchor, FontStyle style)
    {
        var rt = Rt(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = GetFont(); t.fontSize = size; t.color = col; t.alignment = anchor; t.fontStyle = style;
        t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    void BuildUi()
    {
        var cgo = new GameObject("ShuntaHudCanvas", typeof(Canvas), typeof(CanvasScaler));
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 40;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        var rootRt = Rt("Root", cgo.transform); Stretch(rootRt);
        _root = rootRt.gameObject.AddComponent<CanvasGroup>();
        _root.interactable = false; _root.blocksRaycasts = false;

        // Banner (top centre)
        var banner = Rt("ZoneBanner", rootRt);
        banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 1f);
        banner.pivot = new Vector2(0.5f, 1f);
        banner.anchoredPosition = new Vector2(0f, -120f);
        banner.sizeDelta = new Vector2(760f, 120f);
        _bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
        _bannerGroup.alpha = 0f;
        var bg = banner.gameObject.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.02f, 0.08f, 0.72f); bg.raycastTarget = false;
        var bar = Rt("Bar", banner);
        bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 0.5f); bar.sizeDelta = new Vector2(10f, 0f);
        _bannerBar = bar.gameObject.AddComponent<Image>(); _bannerBar.raycastTarget = false;
        _bannerTitle = MakeText("Title", banner, 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        var tr = _bannerTitle.rectTransform; tr.anchorMin = new Vector2(0f, 0.45f); tr.anchorMax = new Vector2(1f, 1f); tr.offsetMin = new Vector2(24f, 0f); tr.offsetMax = new Vector2(-12f, -6f);
        _bannerSub = MakeText("Sub", banner, 24, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
        var sr = _bannerSub.rectTransform; sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0.48f); sr.offsetMin = new Vector2(24f, 6f); sr.offsetMax = new Vector2(-12f, 0f);

        // Status line (above the strip)
        _statusText = MakeText("Status", rootRt, 22, new Color(0.75f, 1f, 0.95f), TextAnchor.LowerCenter, FontStyle.Bold);
        var st = _statusText.rectTransform;
        st.anchorMin = new Vector2(0.1f, 0f); st.anchorMax = new Vector2(0.9f, 0f);
        st.pivot = new Vector2(0.5f, 0f); st.anchoredPosition = new Vector2(0f, 170f); st.sizeDelta = new Vector2(0f, 34f);

        // Elevation strip (bottom)
        var panel = Rt("ProfilePanel", rootRt);
        panel.anchorMin = new Vector2(0.06f, 0f); panel.anchorMax = new Vector2(0.94f, 0f);
        panel.pivot = new Vector2(0.5f, 0f); panel.anchoredPosition = new Vector2(0f, 24f); panel.sizeDelta = new Vector2(0f, 136f);
        var pbg = panel.gameObject.AddComponent<Image>();
        pbg.color = new Color(0.02f, 0.015f, 0.06f, 0.62f); pbg.raycastTarget = false;

        _stripRect = Rt("Strip", panel);
        _stripRect.anchorMin = Vector2.zero; _stripRect.anchorMax = Vector2.one;
        _stripRect.offsetMin = new Vector2(10f, 8f); _stripRect.offsetMax = new Vector2(-10f, -30f);

        float minM = float.MaxValue, maxM = float.MinValue;
        int N = 240;
        var elev = new float[N + 1]; var cols = new Color[N + 1];
        for (int i = 0; i <= N; i++)
        {
            float km = _data.distanceKm * i / N;
            elev[i] = _data.ElevationAtKm(km);
            minM = Mathf.Min(minM, elev[i]); maxM = Mathf.Max(maxM, elev[i]);
            var zz = _data.ZoneAtKm(km);
            cols[i] = zz != null ? zz.Color32 : Color.white;
        }
        if (maxM - minM < 10f) maxM = minM + 10f;
        _minM = minM; _maxM = maxM;
        var pg = Rt("Profile", _stripRect); Stretch(pg);
        _profile = pg.gameObject.AddComponent<ProfileGraphic>();
        _profile.raycastTarget = false;
        _profile.Set(elev, cols, minM, maxM);

        // Title + range labels
        var head = MakeText("Head", panel, 18, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperLeft, FontStyle.Bold);
        var hr = head.rectTransform; hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(0.6f, 1f); hr.pivot = new Vector2(0f, 1f);
        hr.anchoredPosition = new Vector2(12f, -4f); hr.sizeDelta = new Vector2(0f, 24f);
        head.text = $"{_data.displayName.ToUpperInvariant()}   {_data.distanceKm:0.0} KM   +{_data.ComputeGain():0} M";
        var rng = MakeText("Range", panel, 16, new Color(1f, 1f, 1f, 0.6f), TextAnchor.UpperRight, FontStyle.Normal);
        var rr = rng.rectTransform; rr.anchorMin = new Vector2(0.4f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(1f, 1f);
        rr.anchoredPosition = new Vector2(-12f, -5f); rr.sizeDelta = new Vector2(0f, 22f);
        rng.text = $"{minM:0}-{maxM:0} m";

        // Climb labels
        if (_data.climbs != null)
            foreach (var c in _data.climbs)
            {
                float mid = (c.startKm + c.endKm) * 0.5f;
                float u = mid / _data.distanceKm;
                float y = Mathf.InverseLerp(minM, maxM, _data.ElevationAtKm(mid));
                var lbl = MakeText("Climb " + c.name, _stripRect, 14, new Color(1f, 0.92f, 0.5f), TextAnchor.LowerCenter, FontStyle.Bold);
                lbl.text = c.name.ToUpperInvariant();
                var lr = lbl.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(u, Mathf.Clamp(y + 0.04f, 0f, 0.9f));
                lr.pivot = new Vector2(0.5f, 0f); lr.sizeDelta = new Vector2(160f, 20f); lr.anchoredPosition = Vector2.zero;
            }

        // Chevron ticks
        if (Chevrons != null)
            foreach (var ch in Chevrons)
            {
                var t = Rt("Chevron", _stripRect);
                t.anchorMin = t.anchorMax = new Vector2(Mathf.Clamp01(ch.km / _data.distanceKm), 0f);
                t.pivot = new Vector2(0.5f, 0f); t.sizeDelta = new Vector2(4f, 10f);
                var im = t.gameObject.AddComponent<Image>(); im.color = new Color(0.3f, 1f, 0.9f, 0.95f); im.raycastTarget = false;
                _chevTicks.Add(t);
            }

        // Rider marker: vertical line spanning the strip + dot
        _marker = Rt("Marker", _stripRect);
        _marker.anchorMin = new Vector2(0f, 0f); _marker.anchorMax = new Vector2(0f, 1f);
        _marker.pivot = new Vector2(0.5f, 0.5f); _marker.sizeDelta = new Vector2(3f, 0f);
        var line = _marker.gameObject.AddComponent<Image>(); line.color = new Color(1f, 1f, 1f, 0.9f); line.raycastTarget = false;
        var dot = Rt("Dot", _marker);
        dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 1f); dot.pivot = new Vector2(0.5f, 0.5f); dot.sizeDelta = new Vector2(14f, 14f);
        var di = dot.gameObject.AddComponent<Image>(); di.color = new Color(1f, 0.25f, 0.6f); di.raycastTarget = false;
    }

    // ---- elevation graphic ----------------------------------------------------------------------
    sealed class ProfileGraphic : MaskableGraphic
    {
        float[] _e; Color[] _c; float _min, _max;

        public void Set(float[] elev, Color[] cols, float min, float max)
        {
            _e = elev; _c = cols; _min = min; _max = max; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_e == null || _e.Length < 2) return;
            var r = GetPixelAdjustedRect();
            int n = _e.Length;
            float line = 3f;
            for (int i = 0; i < n - 1; i++)
            {
                float x0 = r.xMin + r.width * i / (n - 1), x1 = r.xMin + r.width * (i + 1) / (n - 1);
                float y0 = r.yMin + r.height * (0.06f + 0.86f * Mathf.InverseLerp(_min, _max, _e[i]));
                float y1 = r.yMin + r.height * (0.06f + 0.86f * Mathf.InverseLerp(_min, _max, _e[i + 1]));
                var col = _c[i];
                var fill = new Color(col.r, col.g, col.b, 0.38f);
                var top = new Color(col.r, col.g, col.b, 1f);
                int b = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, r.yMin), fill, Vector2.zero);
                vh.AddVert(new Vector3(x0, y0), fill, Vector2.zero);
                vh.AddVert(new Vector3(x1, y1), fill, Vector2.zero);
                vh.AddVert(new Vector3(x1, r.yMin), fill, Vector2.zero);
                vh.AddTriangle(b, b + 1, b + 2); vh.AddTriangle(b, b + 2, b + 3);
                b = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, y0 - line * 0.5f), top, Vector2.zero);
                vh.AddVert(new Vector3(x0, y0 + line * 0.5f), top, Vector2.zero);
                vh.AddVert(new Vector3(x1, y1 + line * 0.5f), top, Vector2.zero);
                vh.AddVert(new Vector3(x1, y1 - line * 0.5f), top, Vector2.zero);
                vh.AddTriangle(b, b + 1, b + 2); vh.AddTriangle(b, b + 2, b + 3);
            }
        }
    }
}

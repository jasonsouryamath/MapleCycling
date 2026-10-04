using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The elevation / progress strip along the bottom of the ride screen.
///
/// WHY IT EXISTS AGAIN. The course's elevation profile used to live inside the rectangular GPS
/// window (<see cref="RouteMapHud"/>). When the compact circular minimap became the default HUD,
/// the profile went with the old window and the ride lost its "where am I on the climb" read
/// (user 2026-09-26: "reimplement the elevation/progress map at the bottom of the screen"). This
/// brings it back as its own widget in the free band between the ride card (bottom-left) and
/// the minimap (bottom-right): the WHOLE course profile, the ridden part in ember, a rider
/// cursor with a live grade chip, checkpoint ticks with the next one named, and the numbers a
/// rider actually glances at (elevation now, lap, distance to go).
///
/// Built in code into the ride HUD canvas by <see cref="RideHud.Build"/>, converge-by-name like
/// every other HUD widget, and refreshed in its own Update (a few anchoredPosition writes a
/// frame; the profile mesh is rebaked only when the course changes).
/// </summary>
public sealed class ElevationStripHud : MonoBehaviour
{
    public const string StripName = "Elevation Strip";

    public RideSession session;
    public RouteDirector director;

    [Header("Layout (1920x1080 reference, provisional)")]
    [Tooltip("Left edge of the strip, px from the screen's left. Clears the bottom-left ride card.")]
    public float left = 628f;
    [Tooltip("Right edge of the strip, px from the screen's right. Clears the circular minimap.")]
    public float right = 318f;
    public float bottom = 26f;
    public float height = 112f;

    private RectTransform _strip, _band, _cursor, _riderDot, _gradeChip, _nextTick;
    private Text _elevNow, _lap, _toGo, _gradeText, _hiText, _loText, _nextName;
    private RouteProfileGraphic _profile;
    private readonly List<RectTransform> _ticks = new List<RectTransform>();
    private string _bakedCourse = "";
    private float _bakedWidth = -1f;

    public RectTransform Strip => _strip;

    // ================================================================= construction

    public void Build(RectTransform hudRoot)
    {
        var old = hudRoot.Find(StripName);
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }
        _ticks.Clear();
        _bakedCourse = "";

        // Darker than the ride cards' PanelAlpha glass: the strip sits over the road, and at the
        // standard alpha the centre line and the rider's own shadow read straight through it.
        // 0.66 still let the lane line show through the profile band (copilot_strip2/3 frames).
        _strip = HudKit.SoftPanel(hudRoot, StripName, new Color(0.020f, 0.028f, 0.040f, 0.84f));
        _strip.anchorMin = new Vector2(0f, 0f);
        _strip.anchorMax = new Vector2(1f, 0f);
        _strip.pivot = new Vector2(0.5f, 0f);
        _strip.offsetMin = new Vector2(left, bottom);
        _strip.offsetMax = new Vector2(-right, bottom + height);
        var edge = HudKit.SoftPanel(_strip, "Edge", HudKit.GlassEdge, frame: true);
        HudKit.Place(edge, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ---- header row: ELEVATION <now>  ...  LAP n/N  .  x.x KM TO GO
        var head = HudKit.Panel(_strip, "Head", HudKit.PaneTextBed);
        HudKit.Place(head, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), Vector2.zero);
        var cap = HudKit.Label(head, "Caption", "ELEVATION", 13, HudKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(cap);
        HudKit.Place((RectTransform)cap.transform, Vector2.zero, new Vector2(0f, 1f), new Vector2(14f, 0f), new Vector2(110f, 0f));
        _elevNow = HudKit.Label(head, "Now", "", 17, HudKit.Chalk, TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(_elevNow);
        HudKit.Place((RectTransform)_elevNow.transform, Vector2.zero, new Vector2(0f, 1f), new Vector2(104f, 0f), new Vector2(260f, 0f));
        _toGo = HudKit.Label(head, "ToGo", "", 15, HudKit.Chalk, TextAnchor.MiddleRight, FontStyle.Bold);
        HudKit.AddShadow(_toGo);
        HudKit.Place((RectTransform)_toGo.transform, new Vector2(1f, 0f), Vector2.one, new Vector2(-300f, 0f), new Vector2(-14f, 0f));
        _lap = HudKit.Label(head, "Lap", "", 15, HudKit.ChalkSoft, TextAnchor.MiddleRight, FontStyle.Bold);
        HudKit.AddShadow(_lap);
        HudKit.Place((RectTransform)_lap.transform, new Vector2(1f, 0f), Vector2.one, new Vector2(-460f, 0f), new Vector2(-300f, 0f));

        var rule = HudKit.Panel(_strip, "Rule", HudKit.Ember);
        HudKit.Place(rule, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -30f), new Vector2(0f, -28f));

        // ---- profile band (leaves room on the right for the hi/lo axis labels)
        _band = HudKit.Rect(_strip, "Band");
        HudKit.Place(_band, Vector2.zero, Vector2.one, new Vector2(14f, 12f), new Vector2(-70f, -38f));

        var go = new GameObject("Profile", typeof(RectTransform), typeof(CanvasRenderer), typeof(RouteProfileGraphic));
        go.transform.SetParent(_band, false);
        _profile = go.GetComponent<RouteProfileGraphic>();
        _profile.raycastTarget = false;
        _profile.color = new Color(0.784f, 0.808f, 0.824f, 0.38f);
        _profile.ridden = new Color(0.976f, 0.416f, 0.318f, 0.85f);
        HudKit.Place((RectTransform)go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        _hiText = HudKit.Label(_strip, "Hi", "", 12, HudKit.ChalkSoft, TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.AddShadow(_hiText);
        HudKit.Place((RectTransform)_hiText.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-64f, 12f), new Vector2(-4f, -38f));
        _loText = HudKit.Label(_strip, "Lo", "", 12, HudKit.ChalkSoft, TextAnchor.LowerLeft, FontStyle.Bold);
        HudKit.AddShadow(_loText);
        HudKit.Place((RectTransform)_loText.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-64f, 12f), new Vector2(-4f, -38f));

        // ---- rider cursor: a hairline through the band plus a dot riding the profile
        _cursor = HudKit.Panel(_band, "Cursor", HudKit.Chalk);
        _cursor.anchorMin = new Vector2(0f, 0f);
        _cursor.anchorMax = new Vector2(0f, 1f);
        _cursor.pivot = new Vector2(0.5f, 0f);
        _cursor.sizeDelta = new Vector2(2f, 0f);

        _riderDot = HudKit.Panel(_band, "Rider", HudKit.Chalk);
        _riderDot.anchorMin = _riderDot.anchorMax = Vector2.zero;
        _riderDot.pivot = new Vector2(0.5f, 0.5f);
        _riderDot.sizeDelta = new Vector2(14f, 14f);
        _riderDot.GetComponent<Image>().sprite = HudSprites.Disc;
        var ring = HudKit.Panel(_riderDot, "Ring", HudKit.Ember);
        HudKit.Place(ring, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        ring.GetComponent<Image>().sprite = HudSprites.Disc;

        _gradeChip = HudKit.Panel(_band, "Grade", HudKit.Ember);
        _gradeChip.anchorMin = _gradeChip.anchorMax = new Vector2(0f, 1f);
        _gradeChip.pivot = new Vector2(0.5f, 1f);          // hangs INSIDE the band, under the header rule
        _gradeChip.sizeDelta = new Vector2(62f, 20f);
        _gradeText = HudKit.Label(_gradeChip, "Label", "", 13, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_gradeText, 0.7f, 1f);
        HudKit.Place((RectTransform)_gradeText.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        _nextName = HudKit.Label(_band, "Next", "", 12, HudKit.Gold, TextAnchor.LowerCenter, FontStyle.Bold);
        HudKit.AddShadow(_nextName, 0.9f, 1.3f);
        var nn = (RectTransform)_nextName.transform;
        nn.anchorMin = nn.anchorMax = Vector2.zero;
        nn.pivot = new Vector2(0.5f, 0f);
        nn.sizeDelta = new Vector2(200f, 16f);
    }

    // ================================================================= baking

    private void Bake(RouteCourse course)
    {
        _bakedCourse = course.Id;
        _bakedWidth = _band.rect.width;

        const int N = 240;
        var pts = new Vector2[N + 1];
        float range = Mathf.Max(1f, course.MaxElevation - course.MinElevation);
        for (int i = 0; i <= N; i++)
        {
            float f = i / (float)N;
            float y = course.ElevationAt(f * course.Length);
            pts[i] = new Vector2(f, ProfileY(y, course, range));
        }
        _profile.SetProfile(pts);
        _hiText.text = $"{course.MaxElevation:0} m";
        _loText.text = $"{course.MinElevation:0} m";

        foreach (var t in _ticks) if (t != null) Destroy(t.gameObject);
        _ticks.Clear();
        foreach (var cp in course.Checkpoints)
        {
            var tick = HudKit.Panel(_band, "Tick", new Color(0.976f, 0.965f, 0.937f, 0.55f));
            tick.anchorMin = tick.anchorMax = Vector2.zero;
            tick.pivot = new Vector2(0.5f, 0f);
            tick.sizeDelta = new Vector2(2f, 9f);
            tick.anchoredPosition = new Vector2(Frac(cp.Distance, course) * _band.rect.width, 0f);
            _ticks.Add(tick);
        }
        _cursor.SetAsLastSibling();
        _riderDot.SetAsLastSibling();
        _gradeChip.SetAsLastSibling();
        _nextName.transform.SetAsLastSibling();
    }

    private static float ProfileY(float y, RouteCourse course, float range) =>
        Mathf.Clamp01((y - course.MinElevation) / range) * 0.86f + 0.06f;

    private static float Frac(float d, RouteCourse course) =>
        course.Length <= 0.01f ? 0f : Mathf.Clamp01(d / course.Length);

    // ================================================================= live update

    private void Update()
    {
        if (_strip == null || session == null) return;
        var course = session.Course;
        if (course == null || course.Length <= 0.01f) return;
        if (course.Id != _bakedCourse || !Mathf.Approximately(_band.rect.width, _bakedWidth)) Bake(course);
        Refresh(course);
    }

    private void Refresh(RouteCourse course)
    {
        float w = _band.rect.width, h = _band.rect.height;
        float range = Mathf.Max(1f, course.MaxElevation - course.MinElevation);
        float f = Frac(session.DistanceM, course);
        float elev = course.ElevationAt(session.DistanceM);
        float x = f * w;

        _profile.SetFill(f);
        _cursor.anchoredPosition = new Vector2(x, 0f);
        _riderDot.anchoredPosition = new Vector2(x, ProfileY(elev, course, range) * h);

        float pct = session.DisplayGradePct;
        _gradeText.text = (pct >= 0f ? "+" : "") + pct.ToString("0.0") + "%";
        _gradeChip.GetComponent<Image>().color =
            pct >= 0.5f ? HudKit.Ember
            : pct <= -0.5f ? new Color(0.294f, 0.592f, 0.749f, 1f)
            : new Color(0.396f, 0.416f, 0.447f, 1f);
        // keep the chip inside the band at both ends of the course
        _gradeChip.anchoredPosition = new Vector2(Mathf.Clamp(x, 31f, w - 31f), -2f);

        _elevNow.text = $"{elev:0} m";
        _lap.text = session.TotalLaps > 1 ? $"LAP {Mathf.Min(session.LapIndex + 1, session.TotalLaps)}/{session.TotalLaps}" : "";
        _toGo.text = $"{session.RemainingM / 1000f:0.0} KM TO GO";

        // next checkpoint: brighten its tick and name it just above
        CourseCheckpoint? next = null;
        if (director != null && director.HasNext) next = director.NextCheckpoint;
        for (int i = 0; i < _ticks.Count && i < course.Checkpoints.Length; i++)
        {
            bool isNext = next.HasValue && Mathf.Abs(course.Checkpoints[i].Distance - next.Value.Distance) < 0.01f;
            _ticks[i].GetComponent<Image>().color = isNext ? HudKit.Gold : new Color(0.976f, 0.965f, 0.937f, 0.55f);
            _ticks[i].sizeDelta = new Vector2(isNext ? 3f : 2f, isNext ? 14f : 9f);
        }
        if (next.HasValue)
        {
            float nx = Frac(next.Value.Distance, course) * w;
            _nextName.text = RouteNames.Checkpoint(next.Value.Name).ToUpperInvariant();
            var nn = (RectTransform)_nextName.transform;
            nn.anchoredPosition = new Vector2(Mathf.Clamp(nx, 100f, w - 100f), 15f);
            // hide the name when it would sit on the rider's own grade chip
            _nextName.enabled = Mathf.Abs(nx - x) > 70f;
        }
        else _nextName.enabled = false;
    }
}

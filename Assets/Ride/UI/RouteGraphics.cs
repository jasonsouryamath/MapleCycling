using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A polyline drawn as real UI geometry.
///
/// The course line is baked once per course into a vertex buffer rather than re-rasterised into
/// a RenderTexture every frame: it scales crisply, costs nothing per frame, and the "ridden so
/// far" overlay is the same buffer truncated at the rider's sample index.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RouteLineGraphic : MaskableGraphic
{
    [SerializeField] private float thickness = 3f;
    [SerializeField] private bool closedLoop;

    private Vector2[] _points = System.Array.Empty<Vector2>();
    private int _visible = -1;

    public float Thickness
    {
        get => thickness;
        set { thickness = value; SetVerticesDirty(); }
    }

    public bool ClosedLoop
    {
        get => closedLoop;
        set { closedLoop = value; SetVerticesDirty(); }
    }

    /// <summary>Points in this RectTransform's local space.</summary>
    public void SetPoints(Vector2[] pts)
    {
        _points = pts ?? System.Array.Empty<Vector2>();
        _visible = -1;
        SetVerticesDirty();
    }

    /// <summary>Draw only the first <paramref name="count"/> points (the ridden portion).</summary>
    public void SetVisibleCount(int count)
    {
        count = Mathf.Clamp(count, 0, _points.Length);
        if (count == _visible) return;
        _visible = count;
        SetVerticesDirty();
    }

    public int PointCount => _points.Length;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int n = _visible >= 0 ? _visible : _points.Length;
        if (n < 2 || thickness <= 0f) return;

        float half = thickness * 0.5f;
        var c = color;

        int spans = closedLoop && _visible < 0 ? n : n - 1;
        for (int i = 0; i < spans; i++)
        {
            Vector2 a = _points[i];
            Vector2 b = _points[(i + 1) % _points.Length];
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) continue;
            d /= len;
            var nrm = new Vector2(-d.y, d.x) * half;

            int v = vh.currentVertCount;
            vh.AddVert(a - nrm, c, Vector2.zero);
            vh.AddVert(a + nrm, c, Vector2.up);
            vh.AddVert(b + nrm, c, Vector2.one);
            vh.AddVert(b - nrm, c, Vector2.right);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);

            // Square joint patch: cheaper and steadier than a mitre, and at 3 px wide on a map
            // line it is visually identical to a round join.
            if (i > 0)
            {
                int j = vh.currentVertCount;
                vh.AddVert(a + new Vector2(-half, -half), c, Vector2.zero);
                vh.AddVert(a + new Vector2(-half, half), c, Vector2.up);
                vh.AddVert(a + new Vector2(half, half), c, Vector2.one);
                vh.AddVert(a + new Vector2(half, -half), c, Vector2.right);
                vh.AddTriangle(j, j + 1, j + 2);
                vh.AddTriangle(j, j + 2, j + 3);
            }
        }
    }
}

/// <summary>
/// The elevation band: a filled profile polygon baked once per course.
///
/// x is arc length normalised over the course, y is elevation normalised over the course's own
/// min/max, so a 37 m pass and a 165 m mountain both fill the band.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RouteProfileGraphic : MaskableGraphic
{
    private Vector2[] _profile = System.Array.Empty<Vector2>();   // normalised 0..1
    private float _fill = 1f;

    public Color ridden = new Color(0.77f, 0.27f, 0.18f, 0.85f);

    /// <summary>Normalised (arc, elevation) pairs, both 0..1.</summary>
    public void SetProfile(Vector2[] normalised)
    {
        _profile = normalised ?? System.Array.Empty<Vector2>();
        SetVerticesDirty();
    }

    /// <summary>Fraction of the course ridden - tints the profile behind the rider.</summary>
    public void SetFill(float f)
    {
        f = Mathf.Clamp01(f);
        if (Mathf.Abs(f - _fill) < 0.002f) return;
        _fill = f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_profile.Length < 2) return;

        var r = rectTransform.rect;
        for (int i = 0; i < _profile.Length - 1; i++)
        {
            var a = _profile[i];
            var b = _profile[i + 1];
            float ax = r.xMin + a.x * r.width;
            float bx = r.xMin + b.x * r.width;
            float ay = r.yMin + a.y * r.height;
            float by = r.yMin + b.y * r.height;
            var c = a.x <= _fill ? ridden : color;

            int v = vh.currentVertCount;
            vh.AddVert(new Vector3(ax, r.yMin), c, Vector2.zero);
            vh.AddVert(new Vector3(ax, ay), c, Vector2.up);
            vh.AddVert(new Vector3(bx, by), c, Vector2.one);
            vh.AddVert(new Vector3(bx, r.yMin), c, Vector2.right);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }
    }
}

/// <summary>
/// A flat filled polygon - the lake body under the course line on the GPS map.
///
/// Triangulated as a fan about the centroid. The lake outline is the circuit's own valley-side
/// offset, which is a ring that is star-shaped about its centre, so a fan is both correct and
/// an order of magnitude cheaper than a general ear-clip.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class PolygonGraphic : MaskableGraphic
{
    private Vector2[] _points = System.Array.Empty<Vector2>();

    public void SetPoints(Vector2[] pts)
    {
        _points = pts ?? System.Array.Empty<Vector2>();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_points.Length < 3) return;

        Vector2 centre = Vector2.zero;
        for (int i = 0; i < _points.Length; i++) centre += _points[i];
        centre /= _points.Length;

        var c = color;
        vh.AddVert(centre, c, Vector2.one * 0.5f);
        for (int i = 0; i < _points.Length; i++)
            vh.AddVert(_points[i], c, Vector2.zero);
        for (int i = 0; i < _points.Length; i++)
            vh.AddTriangle(0, 1 + i, 1 + (i + 1) % _points.Length);
    }
}

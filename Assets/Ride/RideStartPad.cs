using UnityEngine;

/// <summary>
/// A universal START PAD, laid at the course's start line on EVERY map at run time.
///
/// The problem it solves: different regions begin the road on different ground - Shiosai launches
/// at the water's edge (open sea fills the foreground), other routes begin on messy seam markings.
/// Rather than author a bespoke start in each region's builder (six different route types), this
/// drops a consistent, clean launch straight - a plain tarmac apron running back from the line so
/// the rider always sets off from road, plus a crisp checkered start/finish line - driven purely
/// by the runtime <see cref="RouteCourse"/>, so it works the same on any map and rebuilds when the
/// player fast-travels to a new one.
///
/// Deliberately neutral tarmac (not the region's own asphalt material): a launch pad reading a
/// touch different from the road is fine and keeps this independent of every region's shader/UV
/// conventions.
/// </summary>
[DefaultExecutionOrder(-90)]
public partial class RideStartPad : MonoBehaviour
{
    public RideSession session;

    [Header("Apron (metres)")]
    [Tooltip("Length of tarmac laid BEHIND the start line, so the rider launches from road.")]
    public float back = 22f;
    [Tooltip("Tarmac carried a little AHEAD of the line, to blend onto the real road.")]
    public float forward = 4f;
    [Tooltip("Half width - a touch wider than the 3.5 m carriageway half so edges are covered.")]
    public float halfWidth = 4.2f;
    [Tooltip("How far the apron sits above the sampled road surface, to beat z-fighting.")]
    public float lift = 0.02f;

    [Header("Placement")]
    [Tooltip("Layers the downward probe may hit to find the road surface height.")]
    public LayerMask surfaceMask = ~0;

    private string _builtCourse = "";
    private GameObject _pad;
    private Material _tarmacMat;
    private Material _checkerMat;

    private void Awake()
    {
        if (session == null) session = GetComponent<RideSession>();
    }

    private void OnEnable()
    {
        if (session != null) session.RunReset += OnRunReset;
    }

    private void OnDisable()
    {
        if (session != null) session.RunReset -= OnRunReset;
    }

    private void OnRunReset() { _builtCourse = ""; }   // force a rebuild on the next tick

    private void Start() { TryBuild(); }

    private void Update()
    {
        if (session != null && session.Course != null && session.Course.Id != _builtCourse)
            TryBuild();
    }

    public void TryBuild()
    {
        if (session == null || session.Course == null) return;
        var course = session.Course;
        if (course.Length <= 1f) return;
        _builtCourse = course.Id;

        if (_pad != null) Destroy(_pad);

        float d0 = session.startDistanceM;
        Vector3 p0 = course.PositionAt(d0);
        // Forward = the way the rider actually rides (toward increasing course distance), taken
        // from two samples so it never depends on a heading sign convention. The apron is laid
        // OPPOSITE this, behind the line, where a route like Shiosai has only water.
        Vector3 pAhead = course.PositionAt(Mathf.Min(d0 + 4f, d0 + course.Length * 0.5f));
        Vector3 H = pAhead - p0; H.y = 0f;
        if (H.sqrMagnitude < 1e-4f) H = Vector3.forward;
        H.Normalize();
        Vector3 R = Vector3.Cross(Vector3.up, H).normalized;

        // Sample the road surface beside the start (offset onto the carriageway so the probe hits
        // the road, NOT the rider standing on the centre line). Fall back to the course height -
        // the rider path already sits on the road - if nothing is hit (e.g. the start juts over
        // water).
        float y = p0.y;
        Vector3 probe = p0 + R * 1.6f + Vector3.up * 6f;
        if (Physics.Raycast(probe, Vector3.down, out var hit, 30f, surfaceMask,
                            QueryTriggerInteraction.Ignore))
            y = hit.point.y;

        _pad = new GameObject("Ride Start Pad");
        _pad.transform.SetParent(transform, false);

        Vector3 c = new Vector3(p0.x, y + lift, p0.z);
        bool finished = UsesFinishedStart(course.Id);
        if (finished)
        {
            BuildFinishedStart(course, d0, p0, y, H, R);
        }
        else
        {
            BuildApron(c, H, R);
            BuildCheckerLine(new Vector3(p0.x, y + lift + 0.015f, p0.z), H, R);
        }

        Debug.Log($"[ride] start pad built for '{course.Id}' at {c} (back {back:0} m, " +
                  $"{(finished ? "finished" : "plain")} style).");
    }

    private void BuildApron(Vector3 c, Vector3 H, Vector3 R)
    {
        var go = new GameObject("Tarmac Apron");
        go.transform.SetParent(_pad.transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = TarmacMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 bl = c - H * back - R * halfWidth;
        Vector3 br = c - H * back + R * halfWidth;
        Vector3 fr = c + H * forward + R * halfWidth;
        Vector3 fl = c + H * forward - R * halfWidth;

        float tile = 4f;   // ~4 m asphalt tile, in case the shader reads mesh UVs
        var mesh = new Mesh { name = "StartApron" };
        mesh.vertices = new[] { bl, br, fr, fl };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[]
        {
            new Vector2(-halfWidth / tile, -back / tile),
            new Vector2( halfWidth / tile, -back / tile),
            new Vector2( halfWidth / tile,  forward / tile),
            new Vector2(-halfWidth / tile,  forward / tile),
        };
        // Double-sided so a flipped winding can never make the pad invisible from above.
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        mf.sharedMesh = mesh;
    }

    private void BuildCheckerLine(Vector3 c, Vector3 H, Vector3 R)
    {
        var go = new GameObject("Checkered Start Line");
        go.transform.SetParent(_pad.transform, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = CheckerMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        const float depth = 1.3f;      // ~1.3 m deep line
        Vector3 bl = c - H * (depth * 0.5f) - R * halfWidth;
        Vector3 br = c - H * (depth * 0.5f) + R * halfWidth;
        Vector3 fr = c + H * (depth * 0.5f) + R * halfWidth;
        Vector3 fl = c + H * (depth * 0.5f) - R * halfWidth;

        var mesh = new Mesh { name = "StartCheckerLine" };
        mesh.vertices = new[] { bl, br, fr, fl };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        mf.sharedMesh = mesh;
    }

    private Material TarmacMat()
    {
        if (_tarmacMat != null) return _tarmacMat;
        var sh = Shader.Find("HDRP/Lit");
        _tarmacMat = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
        if (_tarmacMat.HasProperty("_BaseColor")) _tarmacMat.SetColor("_BaseColor", new Color(0.17f, 0.17f, 0.18f));
        if (_tarmacMat.HasProperty("_Color")) _tarmacMat.SetColor("_Color", new Color(0.17f, 0.17f, 0.18f));
        if (_tarmacMat.HasProperty("_Smoothness")) _tarmacMat.SetFloat("_Smoothness", 0.12f);
        if (_tarmacMat.HasProperty("_Metallic")) _tarmacMat.SetFloat("_Metallic", 0f);
        return _tarmacMat;
    }

    private Material CheckerMat()
    {
        if (_checkerMat != null) return _checkerMat;
        var tex = MakeChecker(16, 2, 16);
        var sh = Shader.Find("HDRP/Unlit");
        _checkerMat = sh != null ? new Material(sh) : new Material(Shader.Find("Unlit/Texture"));
        if (_checkerMat.HasProperty("_UnlitColorMap")) _checkerMat.SetTexture("_UnlitColorMap", tex);
        if (_checkerMat.HasProperty("_UnlitColor")) _checkerMat.SetColor("_UnlitColor", Color.white);
        if (_checkerMat.HasProperty("_BaseColorMap")) _checkerMat.SetTexture("_BaseColorMap", tex);
        if (_checkerMat.HasProperty("_MainTex")) _checkerMat.SetTexture("_MainTex", tex);
        return _checkerMat;
    }

    private static Texture2D MakeChecker(int cellsX, int cellsY, int px)
    {
        int w = cellsX * px, h = cellsY * px;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { name = "StartChecker", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var black = new Color(0.05f, 0.05f, 0.06f, 1f);
        var cols = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            cols[y * w + x] = (((x / px) + (y / px)) & 1) == 0 ? Color.white : black;
        tex.SetPixels(cols);
        tex.Apply(false, false);
        return tex;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the Shunta Metro route from <see cref="ShuntaCourseData"/>: a Catmull-Rom spline through
/// the waypoints, lifted by the elevation profile, resampled at a fixed spacing, with a road
/// ribbon mesh, start/finish gates, and a zone-coloured scene gizmo.
/// Rebuild from the context menu or via <c>ShuntaMetroSetup</c>; the result lives in <see cref="Positions"/>.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ShuntaRouteBuilder : MonoBehaviour
{
    [Tooltip("Optional override; defaults to Resources/ShuntaMetro/shunta_metro_course.json")]
    public TextAsset courseJson;
    [Min(2f)] public float sampleSpacing = 10f;
    [Min(2f)] public float roadWidth = 9f;
    public float verticalExaggeration = 1f;
    [Tooltip("Added to every sample (world-space route in a builder that sits at the origin, e.g. the playable scene's z+30000).")]
    public Vector3 positionBias;
    public bool buildRibbon = true;
    public bool buildGates = true;
    [Header("Gizmo")]
    public bool drawGizmo = true;
    public bool drawZoneLabels = true;
    public bool drawWaypoints = true;

    public ShuntaCourseData Course { get; private set; }
    public Vector3[] Positions { get; private set; } = System.Array.Empty<Vector3>();
    public float[] Km { get; private set; } = System.Array.Empty<float>();
    public int[] ZoneIndex { get; private set; } = System.Array.Empty<int>();
    public float Length3D { get; private set; }
    public float Ascent { get; private set; }

    const string RibbonName = "Shunta Road Ribbon";
    const string StartGateName = "Shunta Start Gate";
    const string FinishGateName = "Shunta Finish Gate";

    void OnEnable() { if (Positions.Length == 0) Rebuild(); }

    [ContextMenu("Rebuild Route")]
    public void Rebuild()
    {
        Course = ShuntaCourseData.Load(courseJson);
        if (Course == null || Course.waypoints.Length < 2) { Positions = System.Array.Empty<Vector3>(); return; }

        // 1) spline through waypoints, flattened to a dense XZ polyline
        var wp = Course.waypoints;
        var dense = new List<Vector2>();
        for (int i = 0; i < wp.Length - 1; i++)
        {
            var p0 = P(wp, Mathf.Max(i - 1, 0)); var p1 = P(wp, i);
            var p2 = P(wp, i + 1); var p3 = P(wp, Mathf.Min(i + 2, wp.Length - 1));
            int steps = Mathf.Max(24, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 2f));
            for (int s = 0; s < steps; s++) dense.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
        }
        dense.Add(P(wp, wp.Length - 1));

        // 2) resample at even horizontal spacing; km follows horizontal distance
        var pts = new List<Vector3>(); var kms = new List<float>(); var zones = new List<int>();
        float acc = 0f, next = Mathf.Max(2f, sampleSpacing);
        float totalH = 0f;
        for (int i = 1; i < dense.Count; i++) totalH += Vector2.Distance(dense[i - 1], dense[i]);
        float scaleKm = Course.distanceKm / Mathf.Max(totalH / 1000f, 0.001f); // keep km faithful to the JSON
        AddSample(dense[0], 0f, pts, kms, zones);
        for (int i = 1; i < dense.Count; i++)
        {
            float length = Vector2.Distance(dense[i - 1], dense[i]);
            while (length > 0f && next <= acc + length)
            {
                var p = Vector2.Lerp(dense[i - 1], dense[i], (next - acc) / length);
                AddSample(p, next / 1000f * scaleKm, pts, kms, zones);
                next += Mathf.Max(2f, sampleSpacing);
            }
            acc += length;
        }
        if (Course.distanceKm - kms[kms.Count - 1] > 0.000001f)
            AddSample(dense[dense.Count - 1], Course.distanceKm, pts, kms, zones);
        Positions = pts.ToArray(); Km = kms.ToArray(); ZoneIndex = zones.ToArray();

        Length3D = 0f; Ascent = 0f;
        for (int i = 1; i < Positions.Length; i++)
        {
            Length3D += Vector3.Distance(Positions[i - 1], Positions[i]);
            Ascent += Mathf.Max(0f, Positions[i].y - Positions[i - 1].y);
        }
        Ascent /= Mathf.Max(verticalExaggeration, 0.0001f);

        ClearChild(RibbonName); ClearChild(StartGateName); ClearChild(FinishGateName);
        if (buildRibbon) BuildRibbonMesh();
        if (buildGates) { BuildGate(StartGateName, 0f, new Color(0.3f, 1f, 0.6f)); BuildGate(FinishGateName, Km[Km.Length - 1], new Color(1f, 0.25f, 0.65f)); }

        Debug.Log($"[shunta] route built: {Positions.Length} samples, {Km[Km.Length - 1]:F2} km (3D {Length3D / 1000f:F2} km), " +
                  $"gain sampled {Ascent:F0} m / keypoints {Course.ComputeGain():F0} m (target {Course.targetGainM:F0}), max grade {Course.MaxGradePercent():F1}%");
    }

    void AddSample(Vector2 p, float km, List<Vector3> pts, List<float> kms, List<int> zones)
    {
        var z = Course.ZoneAtKm(km);
        pts.Add(new Vector3(p.x, Course.ElevationAtKm(km) * verticalExaggeration, p.y) + positionBias);
        kms.Add(km);
        zones.Add(z != null ? z.index : 0);
    }

    static Vector2 P(ShuntaWaypoint[] w, int i) => new Vector2(w[i].x, w[i].z);

    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    public Vector3 PositionAtKm(float km)
    {
        if (Positions.Length == 0) return transform.position;
        int hi = System.Array.BinarySearch(Km, km);
        if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, Km.Length - 1);
        float t = Mathf.InverseLerp(Km[hi - 1], Km[hi], km);
        return transform.TransformPoint(Vector3.Lerp(Positions[hi - 1], Positions[hi], t));
    }

    public Vector3 TangentAtKm(float km)
    {
        var a = PositionAtKm(Mathf.Max(0f, km - 0.01f)); var b = PositionAtKm(km + 0.01f);
        var d = b - a; return d.sqrMagnitude < 1e-6f ? transform.forward : d.normalized;
    }

    // ---------------------------------------------------------------- road ribbon

    void BuildRibbonMesh()
    {
        int n = Positions.Length;
        var v = new Vector3[n * 2]; var uv = new Vector2[n * 2]; var tris = new int[(n - 1) * 6];
        float run = 0f;
        for (int i = 0; i < n; i++)
        {
            var fwd = (Positions[Mathf.Min(i + 1, n - 1)] - Positions[Mathf.Max(i - 1, 0)]); fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized;
            var right = new Vector3(fwd.z, 0f, -fwd.x);
            v[i * 2] = Positions[i] - right * roadWidth * 0.5f; v[i * 2 + 1] = Positions[i] + right * roadWidth * 0.5f;
            if (i > 0) run += Vector3.Distance(Positions[i - 1], Positions[i]);
            uv[i * 2] = new Vector2(0f, run / roadWidth); uv[i * 2 + 1] = new Vector2(1f, run / roadWidth);
        }
        for (int i = 0; i < n - 1; i++)
        {
            int a = i * 2, t = i * 6;
            tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
            tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
        }
        var mesh = new Mesh { name = "ShuntaRoad", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = v; mesh.uv = uv; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();

        var go = new GameObject(RibbonName); go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.05f, 0.05f, 0.07f), Color.black, 0.9f);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // ---------------------------------------------------------------- gates

    void BuildGate(string gateName, float km, Color glow)
    {
        var pos = PositionAtKm(km); var fwd = TangentAtKm(km); fwd.y = 0f; fwd.Normalize();
        var root = new GameObject(gateName); root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
        float half = roadWidth * 0.5f + 1.2f, h = 6.5f;

        Part(root, "Pillar L", new Vector3(-half, h * 0.5f, 0f), new Vector3(0.9f, h, 0.9f), new Color(0.08f, 0.08f, 0.1f), Color.black);
        Part(root, "Pillar R", new Vector3(half, h * 0.5f, 0f), new Vector3(0.9f, h, 0.9f), new Color(0.08f, 0.08f, 0.1f), Color.black);
        Part(root, "Beam", new Vector3(0f, h + 0.6f, 0f), new Vector3(half * 2f + 1.8f, 1.2f, 0.9f), new Color(0.08f, 0.08f, 0.1f), glow * 2.5f);
        Part(root, "Banner", new Vector3(0f, h + 0.6f, -0.5f), new Vector3(half * 2f - 0.6f, 0.7f, 0.1f), new Color(0.9f, 0.1f, 0.15f), new Color(1f, 0.1f, 0.2f) * 3f);

        // chequered line on the tarmac: alternating cells across the road
        int cells = Mathf.RoundToInt(roadWidth / 0.75f);
        float cw = roadWidth / cells;
        for (int c = 0; c < cells; c++)
            for (int r = 0; r < 2; r++)
                Part(root, "Line", new Vector3(-roadWidth * 0.5f + cw * (c + 0.5f), 0.04f, (r - 0.5f) * 0.75f), new Vector3(cw, 0.04f, 0.75f),
                     (c + r) % 2 == 0 ? Color.white : new Color(0.03f, 0.03f, 0.03f), Color.black);
    }

    void Part(GameObject parent, string partName, Vector3 localPos, Vector3 scale, Color albedo, Color emission)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = partName;
        DestroyImmediateSafe(go.GetComponent<Collider>());
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos; go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(albedo, emission, 0.5f);
    }

    static Material MakeMaterial(Color albedo, Color emission, float smoothness)
    {
        var sh = Shader.Find("HDRP/Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh) { name = "ShuntaMat", hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", albedo); m.SetColor("_Color", albedo);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissiveColor", emission); m.SetColor("_EmissionColor", emission);
        }
        return m;
    }

    void ClearChild(string childName)
    {
        var t = transform.Find(childName);
        if (t != null) DestroyImmediateSafe(t.gameObject);
    }

    static void DestroyImmediateSafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    // ---------------------------------------------------------------- gizmo

    void OnDrawGizmos()
    {
        if (!drawGizmo || Positions.Length < 2 || Course == null) return;
        var m = transform.localToWorldMatrix;
        for (int i = 1; i < Positions.Length; i++)
        {
            var z = Course.zones != null && ZoneIndex[i] > 0 && ZoneIndex[i] <= Course.zones.Length ? Course.zones[ZoneIndex[i] - 1] : null;
            Gizmos.color = z != null ? z.Color32 : Color.white;
            Gizmos.DrawLine(m.MultiplyPoint3x4(Positions[i - 1] + Vector3.up * 0.5f), m.MultiplyPoint3x4(Positions[i] + Vector3.up * 0.5f));
        }
        if (drawWaypoints)
        {
            Gizmos.color = Color.white;
            foreach (var w in Course.waypoints)
            {
                float km = KmNearest(new Vector2(w.x, w.z));
                Gizmos.DrawWireSphere(m.MultiplyPoint3x4(new Vector3(w.x, Course.ElevationAtKm(km) * verticalExaggeration + 0.5f, w.z)), 12f);
            }
        }
#if UNITY_EDITOR
        if (!drawZoneLabels) return;
        foreach (var z in Course.zones)
        {
            float mid = (z.startKm + z.endKm) * 0.5f;
            UnityEditor.Handles.color = z.Color32;
            var style = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold };
            style.normal.textColor = z.Color32;
            UnityEditor.Handles.Label(PositionAtKm(mid) + Vector3.up * 60f, $"{z.index}. {z.name}\n{z.startKm:F1}-{z.endKm:F1} km", style);
        }
        UnityEditor.Handles.Label(PositionAtKm(0f) + Vector3.up * 80f, "START  Shibuya");
        UnityEditor.Handles.Label(PositionAtKm(Km[Km.Length - 1]) + Vector3.up * 80f, "FINISH  Odaiba");
#endif
    }

    float KmNearest(Vector2 xz)
    {
        float best = float.MaxValue, km = 0f;
        for (int i = 0; i < Positions.Length; i++)
        {
            float d = (new Vector2(Positions[i].x, Positions[i].z) - xz).sqrMagnitude;
            if (d < best) { best = d; km = Km[i]; }
        }
        return km;
    }
}

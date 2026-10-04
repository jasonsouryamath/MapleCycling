using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Minato Coast redesign workstream: Bridge. See MINATO_VISUAL_DESIGN.md at the project root.
/// Owned by one workstream only; called from MinatoCoastEnvironment.Apply().
///
/// Replaces the coral through-arch crossing with (reference 8 + StormglassCauseway concept):
///   * a WHITE CABLE-STAYED signature span - tall white H-pylons straddling the deck, semi-fan
///     stays in pairs to the deck edges - that the rider sees grow from the approach ramp and
///     then rides between;
///   * a sleek white box-girder viaduct on twin-column portal piers for the rest of the 8 km;
///   * a footway + white parapet + brushed-metal handrails and lamp standards with blue wave
///     banners along the whole crossing.
///
/// Gameplay: the legacy BuildBridge carried NO colliders (MinatoCoastEnvironment.BuildRideBoundary
/// in BuildRoad owns the ride boundary and BuildRoad owns the ride surface), so nothing
/// gameplay-relevant is lost when the legacy builder is skipped. Nothing here adds collision.
///
/// Discrete modules (pylon, pier head / shaft / footing) come from
/// tools/blender/build_minato_cablestay.py. The girder, handrails and stay cables are generated
/// here because they must follow the route's real plan + profile curve; a straight tiled module
/// cannot be seam-free on the ramp and the long causeway curve.
/// </summary>
public static partial class MinatoCoastEnvironment
{
    /// <summary>Flip to true once this workstream's replacement is verified in gameplay captures;
    /// Apply() then skips the legacy builders it replaces.</summary>
    private const bool BridgeReplacesLegacy = true;

    // ============================================================== placement (route metres)
    // DATA-DRIVEN: the lead may move the climax later in the crossing. Move/add pylons HERE
    // only; the back-span anchor piers, pier gaps, stays and lamp gaps all derive from this.
    // Keep each value on the pier grid (CsPierGridOriginM + n * CsPierPitchM) so the ocean
    // pass's pier-foam patches (every 270 m from 2380 m) keep landing under real supports.

    /// <summary>Pylon stations inside the signature zone (UPDATE 2: 8900-9700 m, crest ~50 m).
    /// Two towers, 607.5 m main span. The rider sees them grow along the whole causeway and
    /// the 3 % ramp; capture station 9000 m sits ~60 m before the first tower, so the legs
    /// straddle the frame and the fan converges overhead.</summary>
    private static readonly float[] CsPylonM = { 9062.5f, 9670f };
    /// <summary>Viaduct pier grid, phase-locked to the ocean pass's pier-foam grid
    /// (SeaCrossStartM + 80 m + k*270 m): every 4th pier stands in a foam patch.</summary>
    private const float CsPierGridOriginM = SeaCrossStartM + 80f;
    private const float CsPierPitchM = 67.5f;
    /// <summary>Back spans: auxiliary pier and anchor pier distance outside the outer pylons.</summary>
    private const float CsBackSpanAuxM = 202.5f;
    private const float CsBackSpanEndM = 337.5f;

    // ============================================================== stays
    private const int CsStaysPerFan = 13;          // per plane, per direction, per pylon
    private const float CsStayFirstM = 24f;        // first deck anchor from the pylon
    private const float CsStayPitchM = 21.5f;      // deck anchor pitch -> last at 282 m
    // Mirror of build_minato_cablestay.py LEG_BASE / LEG_TOP / STAY_TOP_*: leg centreline
    // x at deck-local height y, and the anchorage band the semi-fan spreads over.
    private const float CsLegXBase = 10.8f, CsLegYBase = -5.35f;
    private const float CsLegXTop = 8.6f, CsLegYTop = 100f;
    private const float CsStayTopLow = 70f, CsStayTopHigh = 98f;
    private const float CsDeckAnchorX = 6.95f, CsDeckAnchorY = -0.30f;

    // ============================================================== deck section
    /// <summary>Mirror of build_minato_cablestay.py SOFFIT_Y and PIER_HEAD_DEPTH.</summary>
    private const float CsSoffitY = -2.85f;
    private const float CsPierHeadDepthM = 2.3f;
    private const float CsFootingTopM = 2.5f;
    /// <summary>Footway top above the carriageway centreline (kerb upstand).</summary>
    private const float CsFootwayY = 0.12f;
    private const float CsParapetTopY = 0.62f;
    private const float CsRailX = 6.2f;

    /// <summary>
    /// Right half of the box-girder cross-section, (lateral, up) from the centreline at road
    /// level, walked bottom -> kerb. Kerb face at 4.50 m sits just outside the 4.45 m ride
    /// boundary; footway 4.5-6.0 m; white parapet 6.0-6.4 m; wind-fairing nose at 7.05 m that
    /// gives the thin sharp white deck edge of the references. The full loop is this mirrored.
    /// </summary>
    private static readonly Vector2[] CsGirderRight =
    {
        new Vector2(3.40f, -2.85f), new Vector2(5.90f, -1.45f), new Vector2(7.05f, -0.50f),
        new Vector2(6.55f, -0.20f), new Vector2(6.40f, CsParapetTopY), new Vector2(6.00f, CsParapetTopY),
        new Vector2(6.00f, CsFootwayY), new Vector2(4.50f, CsFootwayY), new Vector2(4.50f, -0.30f),
    };

    // ============================================================== furniture
    private const float CsLampPitchM = 50f;
    private const float CsLampX = 5.45f;
    /// <summary>Streaming cell length for the furniture (lamps, banners, rails).</summary>
    private const float CsCellM = 480f;
    private const float CsRailChunkM = 96f;
    private const float CsPostPitchM = 2.4f;
    /// <summary>Girder/rail extents: overlap the land carriageway's own furniture, which stops
    /// at BridgeStartM - 30 / resumes at BridgeEndM + 30 (BuildRoadFurniture).</summary>
    private const float CsDeckFromM = BridgeStartM - 15f, CsDeckToM = BridgeEndM + 20f;
    private const float CsFurnFromM = BridgeStartM - 30f, CsFurnToM = BridgeEndM + 30f;

    // ============================================================== entry

    private static void RedesignBridge(MinatoRoute route, Transform[] chapters)
    {
        if (!BridgeReplacesLegacy) return;   // never double-build over the legacy arches

        // Exact name + parent so ConfigureStreaming's "Chapter 2 - Bridge Approach/Bridge" and
        // its Span_* cell convention pick up the furniture cells without any change there.
        var root = new GameObject("Bridge").transform;
        root.SetParent(chapters[1], false);

        var white = CsWhiteMat();
        int deckTris = CsBuildGirder(route, root, white);
        int piers = CsBuildPiers(route, root, white);
        int stayTris = CsBuildSignature(route, root, white);
        int railTris = CsBuildFurniture(route, root, out int lamps);

        Debug.Log($"[minato-bridge] cable-stayed crossing: {CsPylonM.Length} pylons, " +
                  $"{CsPylonM.Length * 4 * CsStaysPerFan} stays ({stayTris:N0} LOD0 tris), " +
                  $"girder {deckTris:N0} tris, {piers} viaduct piers, rails {railTris:N0} LOD0 tris, " +
                  $"{lamps} lamps+banners");
    }

    // ============================================================== materials

    private static Material CsWhiteMat()
    {
        // Clean white paint over concrete. Shade leans cool-blue (not the lavender default) so
        // the shadow side reads as white-in-shade, like reference 8's pylons.
        var m = CelMaterial("Minato_Bridge_WhitePaint", new Color(0.93f, 0.94f, 0.95f),
                            gloss: 0.30f, spec: 0.16f, rim: 0.40f,
                            shade: new Color(0.62f, 0.72f, 0.86f));
        m.SetFloat("_WeatherAmount", 0.35f);
        m.SetFloat("_GrimeAmount", 0.28f);                           // soffit / underside grime
        m.SetFloat("_WearAmount", 0.10f);
        m.SetColor("_WearColor", new Color(1f, 0.98f, 0.94f));
        m.SetColor("_MossColor", new Color(0.60f, 0.63f, 0.60f));     // salt/tide band, not moss
        m.SetFloat("_MossAmount", 0.32f);
        m.SetFloat("_MossBase", SeaLevelY);
        m.SetFloat("_MossHeight", 7f);
        m.SetFloat("_DetailScale", 3.5f);
        m.SetFloat("_DetailAmount", 0.14f);
        m.SetFloat("_TintVariation", 0.05f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material CsFootwayMat()
    {
        var m = CelMaterial("Minato_Bridge_Footway", new Color(0.78f, 0.79f, 0.80f),
                            gloss: 0.10f, spec: 0.05f, rim: 0.22f,
                            texture: Tex(TakaTex, "Taka_Concrete_Albedo.png"), shade: GroundShade);
        m.SetFloat("_WeatherAmount", 0.40f);
        m.SetFloat("_DetailScale", 5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material CsRailMat()
    {
        // Brushed stainless: high gloss + spec gives the long sun streak along the handrail
        // that references 7/8 and the SeawallSprint target all carry.
        var m = CelMaterial("Minato_Bridge_BrushedRail", new Color(0.78f, 0.80f, 0.83f),
                            gloss: 0.62f, spec: 0.55f, rim: 0.55f,
                            shade: new Color(0.60f, 0.68f, 0.80f));
        m.SetFloat("_WeatherAmount", 0.15f);
        m.SetFloat("_DetailAmount", 0.10f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material CsStayMat() =>
        CelMaterial("Minato_Bridge_Stay", new Color(0.88f, 0.90f, 0.92f),
                    gloss: 0.55f, spec: 0.40f, rim: 0.50f, shade: new Color(0.62f, 0.70f, 0.84f));

    private static Material CsLampMat() =>
        CelMaterial("Minato_Bridge_LampSilver", new Color(0.80f, 0.82f, 0.85f),
                    gloss: 0.45f, spec: 0.35f, rim: 0.45f, shade: new Color(0.60f, 0.68f, 0.80f));

    /// <summary>Slot-name retint for the Blender modules (bridge_white / concrete / beacon).</summary>
    private static void CsRetint(GameObject go, Material white)
    {
        var footing = ConcreteMaterial();
        var beacon = CelMaterial("Minato_Bridge_Beacon", new Color(0.95f, 0.32f, 0.24f),
                                 gloss: 0.3f, spec: 0.2f, rim: 0.5f);
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = mr.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                mats[i] = n.Contains("concrete") ? footing : n.Contains("beacon") ? beacon : white;
            }
            mr.sharedMaterials = mats;
        }
    }

    // ============================================================== route frames

    /// <summary>Interpolated route frame at a distance: centreline point, flat forward, flat
    /// side (same convention as MinatoRoute.SideFlat, +side = rider's right).</summary>
    private static void CsFrame(MinatoRoute r, float d, out Vector3 pos, out Vector3 fwd,
                                out Vector3 side)
    {
        int i = r.IndexAt(d);
        int i0 = Mathf.Max(0, i - 1);
        float span = r.Distance[i] - r.Distance[i0];
        float t = span > 1e-3f ? Mathf.Clamp01((d - r.Distance[i0]) / span) : 0f;
        pos = Vector3.Lerp(r.Position[i0], r.Position[i], t);
        var tan = Vector3.Lerp(r.Tangent[i0], r.Tangent[i], t);
        fwd = new Vector3(tan.x, 0f, tan.z);
        fwd = fwd.sqrMagnitude < 1e-6f ? Vector3.forward : fwd.normalized;
        side = Vector3.Cross(Vector3.up, fwd).normalized;
    }

    private static float CsLegX(float y) =>
        CsLegXBase + (CsLegXTop - CsLegXBase) * (y - CsLegYBase) / (CsLegYTop - CsLegYBase);

    // ============================================================== mesh builder

    /// <summary>Tiny multi-submesh builder. Every triangle is wound against the analytic
    /// normal, so no generator below has to get CW/CCW right by hand.</summary>
    private sealed class CsMesh
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> U = new List<Vector2>();
        public readonly List<int>[] T;
        public CsMesh(int subs)
        {
            T = new List<int>[subs];
            for (int i = 0; i < subs; i++) T[i] = new List<int>();
        }
        public int Tris { get { int n = 0; foreach (var t in T) n += t.Count / 3; return n; } }
        public int Add(Vector3 p, Vector3 n, Vector2 uv) { V.Add(p); N.Add(n); U.Add(uv); return V.Count - 1; }
        public void Tri(int sub, int a, int b, int c)
        {
            var cr = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(cr, N[a] + N[b] + N[c]) < 0f) { T[sub].Add(a); T[sub].Add(c); T[sub].Add(b); }
            else { T[sub].Add(a); T[sub].Add(b); T[sub].Add(c); }
        }
        public void Quad(int sub, int a, int b, int c, int d) { Tri(sub, a, b, c); Tri(sub, a, c, d); }
        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, U);
            m.subMeshCount = T.Length;
            for (int i = 0; i < T.Length; i++) m.SetTriangles(T[i], i);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Closed box with face normals (bottom optional), axes need not be world-aligned.</summary>
    private static void CsBox(CsMesh m, int sub, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az,
                              float hx, float hy, float hz, bool bottom = false)
    {
        void Face(Vector3 n, Vector3 u, Vector3 v, float hu, float hv, float hn)
        {
            var o = c + n * hn;
            int a = m.Add(o - u * hu - v * hv, n, new Vector2(0f, 0f));
            int b = m.Add(o + u * hu - v * hv, n, new Vector2(hu, 0f));
            int cc = m.Add(o + u * hu + v * hv, n, new Vector2(hu, hv));
            int d = m.Add(o - u * hu + v * hv, n, new Vector2(0f, hv));
            m.Quad(sub, a, b, cc, d);
        }
        Face(ax, az, ay, hz, hy, hx);
        Face(-ax, az, ay, hz, hy, hx);
        Face(az, ax, ay, hx, hy, hz);
        Face(-az, ax, ay, hx, hy, hz);
        Face(ay, ax, az, hx, hz, hy);
        if (bottom) Face(-ay, ax, az, hx, hz, hy);
    }

    /// <summary>Straight tube a->b (no caps: both ends are buried in a leg or the deck).</summary>
    private static void CsTube(CsMesh m, int sub, Vector3 a, Vector3 b, float r, int n)
    {
        var w = (b - a).normalized;
        var u = Vector3.Cross(w, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(w, Vector3.right);
        u.Normalize();
        var v = Vector3.Cross(w, u);
        float len = (b - a).magnitude;
        int s = m.V.Count;
        for (int j = 0; j < n; j++)
        {
            float ang = j * Mathf.PI * 2f / n;
            var dir = u * Mathf.Cos(ang) + v * Mathf.Sin(ang);
            m.Add(a + dir * r, dir, new Vector2(j / (float)n, 0f));
            m.Add(b + dir * r, dir, new Vector2(j / (float)n, len * 0.1f));
        }
        for (int j = 0; j < n; j++)
        {
            int j1 = (j + 1) % n;
            m.Quad(sub, s + j * 2, s + j1 * 2, s + j1 * 2 + 1, s + j * 2 + 1);
        }
    }

    /// <summary>Tube swept through route-lateral offsets (x, y) over a list of distances.</summary>
    private static void CsRailTube(CsMesh m, int sub, MinatoRoute r, List<float> ds, float x,
                                   float y, float rad, int n)
    {
        int s = m.V.Count;
        foreach (float d in ds)
        {
            CsFrame(r, d, out var p, out _, out var side);
            var c = p + side * x + Vector3.up * y;
            for (int j = 0; j < n; j++)
            {
                float ang = (j + 0.5f) * Mathf.PI * 2f / n;
                var dir = side * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang);
                m.Add(c + dir * rad, dir, new Vector2(j / (float)n, d * 0.1f));
            }
        }
        for (int k = 0; k < ds.Count - 1; k++)
            for (int j = 0; j < n; j++)
            {
                int j1 = (j + 1) % n;
                int a = s + k * n + j, b = s + k * n + j1;
                m.Quad(sub, a, b, b + n, a + n);
            }
    }

    private static GameObject CsRenderer(Transform parent, string name, Mesh mesh, Material[] mats,
                                         bool shadows)
    {
        var go = AddMesh(parent, name, mesh, mats[0], false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        go.isStatic = true;
        return go;
    }

    /// <summary>LODGroup whose switches happen at chosen camera DISTANCES (60 deg vFOV), because
    /// a long thin rail or cable fan must thin/cull by distance, not by its large bounds.</summary>
    private static void CsLods(GameObject group, GameObject[] levels, float[] switchDistM)
    {
        var lg = group.AddComponent<LODGroup>();
        var lods = new LOD[levels.Length];
        lg.SetLODs(new[] { new LOD(0.5f, new Renderer[] { levels[0].GetComponent<Renderer>() }) });
        lg.RecalculateBounds();
        float size = Mathf.Max(lg.size, 1f);
        float prev = 1f;
        for (int i = 0; i < levels.Length; i++)
        {
            float h = Mathf.Clamp(size / (1.1547f * switchDistM[i]), 0.0005f, prev - 0.0005f);
            lods[i] = new LOD(h, new Renderer[] { levels[i].GetComponent<Renderer>() });
            prev = h;
        }
        lg.SetLODs(lods);
        lg.RecalculateBounds();
    }

    // ============================================================== girder

    private static int CsBuildGirder(MinatoRoute route, Transform root, Material white)
    {
        var parent = new GameObject("Deck Girder").transform;
        parent.SetParent(root, false);
        var mats = new[] { white, CsFootwayMat() };

        // Full CCW loop (seen looking along +route): left half mirrored + right half.
        var loop = new List<Vector2>();
        for (int k = CsGirderRight.Length - 1; k >= 0; k--)
            loop.Add(new Vector2(-CsGirderRight[k].x, CsGirderRight[k].y));
        loop.AddRange(CsGirderRight);
        var along = new float[loop.Count];
        for (int k = 1; k < loop.Count; k++) along[k] = along[k - 1] + (loop[k] - loop[k - 1]).magnitude;

        int i0 = route.IndexAt(CsDeckFromM), i1 = route.IndexAt(CsDeckToM);
        const int Step = 2;                          // 6 m stations; 9 km radius -> <1 mm chord error
        int perChunk = Mathf.RoundToInt(ChunkM / 3f / Step) * Step;
        int tris = 0, chunk = 0;
        for (int start = i0; start < i1; start += perChunk, chunk++)
        {
            int end = Mathf.Min(start + perChunk, i1);
            var m = new CsMesh(2);
            var stations = new List<int>();
            for (int i = start; i < end; i += Step) stations.Add(i);
            stations.Add(end);
            for (int e = 0; e < loop.Count - 1; e++)
            {
                var a2 = loop[e]; var b2 = loop[e + 1];
                var d2 = b2 - a2;
                if (d2.sqrMagnitude < 1e-6f) continue;
                // Outward normal of a CCW loop edge is (dy, -dx).
                var n2 = new Vector2(d2.y, -d2.x).normalized;
                int sub = Mathf.Abs(a2.y - CsFootwayY) < 1e-3f && Mathf.Abs(b2.y - CsFootwayY) < 1e-3f ? 1 : 0;
                int first = m.V.Count;
                foreach (int i in stations)
                {
                    var p = route.Position[i];
                    var side = route.SideFlat(i);
                    var n = (side * n2.x + Vector3.up * n2.y).normalized;
                    float v = route.Distance[i] * 0.25f;
                    m.Add(p + side * a2.x + Vector3.up * a2.y, n, new Vector2(along[e] * 0.25f, v));
                    m.Add(p + side * b2.x + Vector3.up * b2.y, n, new Vector2(along[e + 1] * 0.25f, v));
                }
                for (int k = 0; k < stations.Count - 1; k++)
                {
                    int a = first + k * 2;
                    m.Quad(sub, a, a + 1, a + 3, a + 2);
                }
            }
            // Abutment blocks close the open girder ends at both landfalls.
            if (start == i0) CsAbutment(route, m, CsDeckFromM + 1.2f);
            if (end == i1) CsAbutment(route, m, CsDeckToM - 1.2f);

            var mesh = m.Build($"Minato_Bridge_Deck_{chunk:D3}");
            CsRenderer(parent, $"Deck_{chunk:D3}", mesh, mats, true);
            tris += m.Tris;
        }
        return tris;
    }

    private static void CsAbutment(MinatoRoute route, CsMesh m, float d)
    {
        CsFrame(route, d, out var p, out var fwd, out var side);
        float ground = Mathf.Max(SeaLevelY, GroundAt(route, p.x, p.z));
        float top = p.y - 0.40f, bot = Mathf.Min(ground, p.y + CsSoffitY) - 1.5f;
        var c = new Vector3(p.x, (top + bot) * 0.5f, p.z);
        CsBox(m, 0, c, side, Vector3.up, fwd, 7.4f, (top - bot) * 0.5f, 1.5f, false);
    }

    // ============================================================== piers

    private static int CsBuildPiers(MinatoRoute route, Transform root, Material white)
    {
        var parent = new GameObject("Viaduct Piers").transform;
        parent.SetParent(root, false);
        var head = Model("Minato_Bridge_ViaductPierHead");
        var shaft = Model("Minato_Bridge_ViaductPierShaft");
        var foot = Model("Minato_Bridge_ViaductPierFooting");
        if (head == null || shaft == null || foot == null) return 0;

        float firstP = CsPylonM[0], lastP = CsPylonM[CsPylonM.Length - 1];
        var stations = new List<float> { BridgeStartM + 4f };
        for (int k = 0; ; k++)
        {
            float d = CsPierGridOriginM + k * CsPierPitchM;
            if (d > BridgeEndM - 10f) break;
            if (d < BridgeStartM + 10f) continue;
            // Signature span: only the auxiliary + anchor piers of the back spans stand here.
            bool inSig = d > firstP - CsBackSpanEndM + 1f && d < lastP + CsBackSpanEndM - 1f;
            bool aux = Mathf.Abs(d - (firstP - CsBackSpanAuxM)) < 1f ||
                       Mathf.Abs(d - (lastP + CsBackSpanAuxM)) < 1f;
            if (inSig && !aux) continue;
            stations.Add(d);
        }
        stations.Add(BridgeEndM - 6f);

        int placed = 0;
        foreach (float d in stations)
        {
            CsFrame(route, d, out var p, out var fwd, out _);
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            float baseY = Mathf.Max(SeaLevelY, GroundAt(route, p.x, p.z));
            float headBot = p.y + CsSoffitY - CsPierHeadDepthM;
            float footTop = baseY + CsFootingTopM;
            var pier = new GameObject($"Pier_{d:F0}").transform;
            pier.SetParent(parent, false);
            pier.SetPositionAndRotation(p, rot);
            pier.gameObject.isStatic = true;

            var h = Inst(head, pier, Vector3.zero, Quaternion.identity, null);
            CsRetint(h, white);
            float len = headBot - footTop;
            if (len > 0.4f)
            {
                var s = Inst(shaft, pier, new Vector3(0f, CsSoffitY - CsPierHeadDepthM, 0f),
                             Quaternion.identity, null);
                s.transform.localScale = new Vector3(1f, len / 10f, 1f);
                CsRetint(s, white);
            }
            if (headBot > baseY + 0.5f)
            {
                var f = Inst(foot, pier, new Vector3(0f, baseY - p.y, 0f), Quaternion.identity, null);
                CsRetint(f, white);
            }
            foreach (var t in pier.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            placed++;
        }
        return placed;
    }

    // ============================================================== signature span

    private static int CsBuildSignature(MinatoRoute route, Transform root, Material white)
    {
        // Pylons + stays stay OUTSIDE the streamed Span_* cells on purpose: the towers are the
        // route's visual anchor from zones 1-2 (3+ km away). Their own LOD ladders handle cost.
        var parent = new GameObject("Signature Span").transform;
        parent.SetParent(root, false);
        var pylon = Model("Minato_Bridge_Pylon");
        var stayMat = CsStayMat();
        int tris = 0;

        for (int p = 0; p < CsPylonM.Length; p++)
        {
            float dP = CsPylonM[p];
            CsFrame(route, dP, out var pos, out var fwd, out var side);
            if (pylon != null)
            {
                var go = Inst(pylon, parent, Vector3.zero, Quaternion.identity, null);
                go.name = $"Pylon_{p}";
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
                CsRetint(go, white);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            }

            for (int dir = -1; dir <= 1; dir += 2)
            {
                // LOD0 8-sided crisp tubes near; thicker, fewer-sided tubes further out. The
                // growing radius is deliberate: a 0.3 m cable is sub-pixel past ~1 km and would
                // shimmer; a fatter, cheaper far LOD keeps the fan as a stable set of lines.
                var levels = new (float r, int n, bool socket)[] { (0.15f, 8, true), (0.21f, 4, false), (0.34f, 3, false) };
                var lodGos = new GameObject[levels.Length];
                var group = new GameObject($"Stays_P{p}_{(dir > 0 ? "Fore" : "Back")}");
                group.transform.SetParent(parent, false);
                group.isStatic = true;
                for (int l = 0; l < levels.Length; l++)
                {
                    var m = new CsMesh(1);
                    for (int s = -1; s <= 1; s += 2)
                        for (int k = 0; k < CsStaysPerFan; k++)
                        {
                            float h = CsStayTopLow + (CsStayTopHigh - CsStayTopLow) * k / (CsStaysPerFan - 1f);
                            var top = pos + side * (s * CsLegX(h)) + Vector3.up * h + fwd * (dir * 0.9f);
                            CsFrame(route, dP + dir * (CsStayFirstM + CsStayPitchM * k),
                                    out var q, out _, out var qs);
                            var bot = q + qs * (s * CsDeckAnchorX) + Vector3.up * CsDeckAnchorY;
                            CsTube(m, 0, bot, top, levels[l].r, levels[l].n);
                            if (levels[l].socket)
                                CsTube(m, 0, bot, bot + (top - bot).normalized * 2.6f, 0.27f, 8);
                        }
                    var mesh = m.Build($"Minato_Bridge_Stays_P{p}_{(dir > 0 ? "F" : "B")}_L{l}");
                    lodGos[l] = CsRenderer(group.transform, $"LOD{l}", mesh, new[] { stayMat }, l == 0);
                    if (l == 0) tris += m.Tris;
                }
                CsLods(group, lodGos, new[] { 700f, 1900f, 60000f });
            }
        }
        return tris;
    }

    // ============================================================== furniture

    private static int CsBuildFurniture(MinatoRoute route, Transform root, out int lampCount)
    {
        var cells = new Dictionary<int, Transform>();
        Transform Cell(float d)
        {
            int c = Mathf.FloorToInt((d - CsFurnFromM) / CsCellM);
            if (!cells.TryGetValue(c, out var t))
            {
                t = new GameObject($"Span_{c:D2}").transform;
                t.SetParent(root, false);
                t.gameObject.isStatic = true;
                cells[c] = t;
            }
            return t;
        }

        var railMat = new[] { CsRailMat() };
        int tris = 0, chunk = 0;
        for (float a = CsFurnFromM; a < CsFurnToM - 0.5f; a += CsRailChunkM, chunk++)
        {
            float b = Mathf.Min(a + CsRailChunkM, CsFurnToM);
            var ds = new List<float>();
            for (float d = a; d < b; d += 4f) ds.Add(d);
            ds.Add(b);

            var l0 = new CsMesh(1);
            var l1 = new CsMesh(1);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * CsRailX;
                CsRailTube(l0, 0, route, ds, x, 1.32f, 0.060f, 8);
                CsRailTube(l0, 0, route, ds, x, 1.04f, 0.034f, 6);
                CsRailTube(l0, 0, route, ds, x, 0.82f, 0.034f, 6);
                CsRailTube(l1, 0, route, ds, x, 1.32f, 0.075f, 4);
                CsRailTube(l1, 0, route, ds, x, 0.95f, 0.050f, 3);
                int post = 0;
                for (float d = a + 0.6f; d < b; d += CsPostPitchM, post++)
                {
                    CsFrame(route, d, out var p, out var fwd, out var side);
                    var c = p + side * x + Vector3.up * ((CsParapetTopY + 1.30f) * 0.5f);
                    float hy = (1.30f - CsParapetTopY) * 0.5f;
                    CsBox(l0, 0, c, side, Vector3.up, fwd, 0.05f, hy, 0.065f);
                    if ((post & 1) == 0) CsBox(l1, 0, c, side, Vector3.up, fwd, 0.06f, hy, 0.07f);
                }
            }
            var group = new GameObject($"Rail_{chunk:D3}");
            group.transform.SetParent(Cell(a + 1f), false);
            group.isStatic = true;
            var g0 = CsRenderer(group.transform, "LOD0", l0.Build($"Minato_Bridge_Rail_{chunk:D3}_L0"), railMat, true);
            var g1 = CsRenderer(group.transform, "LOD1", l1.Build($"Minato_Bridge_Rail_{chunk:D3}_L1"), railMat, false);
            // Posts and rails are a few centimetres thick: thin them at 320 m, drop them at 1.3 km
            // (the white parapet in the girder mesh keeps the deck edge readable beyond that).
            CsLods(group, new[] { g0, g1 }, new[] { 320f, 1300f });
            tris += l0.Tris;
        }

        // Lamp standards with blue wave banners, both sides, opposite pairs (reference 8).
        var lamp = Model("Minato_Bridge_Lamp");
        var bannerA = Model("Minato_City_BannerFlag");
        var bannerB = Model("Minato_City_BannerFlagB");
        var lampMat = CsLampMat();
        lampCount = 0;
        if (lamp != null)
        {
            int n = 0;
            for (float d = CsFurnFromM + 12f; d < CsFurnToM - 5f; d += CsLampPitchM, n++)
            {
                bool nearPylon = false;
                foreach (float pm in CsPylonM) nearPylon |= Mathf.Abs(d - pm) < 10f;
                if (nearPylon) continue;
                CsFrame(route, d, out var p, out var fwd, out var side);
                var cell = Cell(d);
                for (int s = -1; s <= 1; s += 2)
                {
                    // The lamp's gallows arm reaches along local -X, i.e. over the carriageway
                    // for a right-side standard; the left one is spun 180 deg to mirror it.
                    var rot = Quaternion.LookRotation(fwd, Vector3.up) *
                              (s < 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);
                    var pos = p + side * (s * CsLampX) + Vector3.up * CsFootwayY;
                    var l = Inst(lamp, cell, Vector3.zero, Quaternion.identity, lampMat);
                    l.transform.SetPositionAndRotation(pos, rot);
                    if (bannerA != null)
                    {
                        // Banner bracket is authored on local +X about the lamp base: with the
                        // lamp's own rotation that puts the cloth OUTBOARD (over the parapet,
                        // framing the sea) and keeps it clear of the ride corridor.
                        var src = (n % 3 == 2 && bannerB != null) ? bannerB : bannerA;
                        var bn = Inst(src, cell, Vector3.zero, Quaternion.identity, null);
                        bn.transform.SetPositionAndRotation(pos, rot);
                        RetintBoulevard(bn);
                        foreach (var t in bn.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                    }
                    foreach (var t in l.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                    lampCount++;
                }
            }
        }
        return tris;
    }
}

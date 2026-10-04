using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// SHIOSAI GRAND COAST TUNA PORT (user brief 2026-09-27): the Fishing Village chapter
/// (4.95-5.5 km) as a bustling Japanese port town whose hottest commodity is tuna.
///
///   * S1 straight (5,056-5,200 m, the rider's first view out of the tunnel): a road-level
///     market apron with the MAGURO ICHIBA hall (open roadside face, rows of frozen and fresh
///     tuna, auctioneers, buyers, turret trucks, crates), a retaining sea wall down to a
///     working quay with bollards, fenders, piers, a breakwater with tetrapods and the harbour
///     light, and moored tuna boats flying tairyo-bata.
///   * Inland of S1 and S2 (5,281-5,460 m): a gable-to-gable row of fishmongers, a tuna
///     wholesaler and seafood diners (noren, fascia signs, ice trays, lanterns, nobori).
///   * S2 seaward verge: a morning-market tent row above the sea wall.
///   * The hillside town: procedural machiya/minka (kawara gables, cedar, plaster) on the
///     legacy lot layout, replacing the western hipped-roof harbour-house GLBs.
///   * Pass 2 (TunaPort.Detail.cs / TunaPort.Outfit*.cs): street life and fitted work clothes.
///
/// Everything is static geometry merged per material per 60 m chunk. All numbers PROVISIONAL.
/// Fast re-stage without a full Apply: ShiosaiCoastEnvironment.StageTunaPortOnly.
/// OWNER: copilot (COORDINATION.md 12:05 / 13:40 / 20:40). A 20:46 overwrite by another session
/// was reverted; that version is kept at reference/conflicts/codex_*TunaPort*.cs.txt.
/// </summary>
public static partial class ShiosaiCoastEnvironment
{
    private const bool TunaPortEnabled = true;

    private const float PortS1AnchorM = 5130f;   // origin of the S1 frame (straight 5,056-5,200)
    private const float PortS2AnchorM = 5370f;   // origin of the S2 frame (straight 5,281-5,461)
    private const float PortQuayY = 1.9f;         // working-quay deck height above the sea
    private const float PortApronNear = 8.2f;     // seaward kerb of the market apron (from centreline)
    private const float PortApronFar = 38f;       // seaward retaining wall of the apron
    private const float PortApronA0 = -66f, PortApronA1 = 66f;
    private const float PortQuayFar = 74f;        // quay face (water edge)
    private const float PortQuayA1 = 100f;
    private const float PortShopFront = 10.4f;    // inland shop-front line (from centreline)
    private const float PortShopDepth = 8.4f;
    private const string PortTexDir = "Assets/Environment/ShiosaiCoast/Textures/TunaPort";
    private const string PortGroupName = "Shiosai Tuna Port";
    // Distance culling (camera metres at 60 deg vfov): small dressing and the pass-2 micro detail.
    private const float PortSmallCullM = 320f, PortFineCullM = 120f;

    // ------------------------------------------------------------------ frames

    /// <summary>A straight-road frame: a along the road, n SEAWARD (the rider's left).</summary>
    private sealed class PFrame
    {
        public Vector3 O, T, N;
        public float anchorM;
        public CoastRoute route;
        public float RoadY(float a)
        {
            int i = route.IndexAt(anchorM + a);
            return route.Position[Mathf.Clamp(i, 0, route.Count - 1)].y;
        }
        public Vector3 W(float a, float n, float y) =>
            new Vector3(O.x + T.x * a + N.x * n, y, O.z + T.z * a + N.z * n);
        /// <summary>Point at (a, n) at road height + dy.</summary>
        public Vector3 R(float a, float n, float dy = 0f) => W(a, n, RoadY(a) + dy);
        public Vector2 Local(Vector3 w)
        {
            var d = new Vector3(w.x - O.x, 0f, w.z - O.z);
            return new Vector2(Vector3.Dot(d, T), Vector3.Dot(d, N));
        }
    }

    private static PFrame PortFrame(CoastRoute route, float anchorM)
    {
        int i = route.IndexAt(anchorM);
        var t = route.Tangent[i]; t.y = 0f; t.Normalize();
        return new PFrame
        {
            O = route.Position[i], T = t, N = -route.SideFlat(i), anchorM = route.Distance[i], route = route,
        };
    }

    // ------------------------------------------------------------------ footprints
    // Rectangles (frame, a0, a1, n0, n1) where the verge scatter and the guardrail must yield.
    private static List<(PFrame f, float a0, float a1, float n0, float n1)> _portFoot;
    private static CoastRoute _portFootRoute;

    private static List<(PFrame f, float a0, float a1, float n0, float n1)> PortFootprints(CoastRoute r)
    {
        if (_portFoot != null && _portFootRoute == r) return _portFoot;
        var s1 = PortFrame(r, PortS1AnchorM);
        var s2 = PortFrame(r, PortS2AnchorM);
        _portFoot = new List<(PFrame, float, float, float, float)>
        {
            (s1, PortApronA0 - 2f, PortApronA1 + 2f, 4.2f, PortApronFar + 2f),    // market apron
            (s1, PortApronA0 - 6f, PortQuayA1 + 4f, PortApronFar - 4f, PortQuayFar + 50f), // quay
            (s1, -74f, 66f, -(PortShopFront + PortShopDepth + 1f), -4.2f),          // S1 shop row
            (s2, -82f, 82f, -(PortShopFront + PortShopDepth + 1f), -4.2f),          // S2 shop row
            (s2, -82f, 82f, 4.2f, 10.6f),                                           // S2 tent row
        };
        _portFootRoute = r;
        return _portFoot;
    }

    /// <summary>True when (station i, offset) lies on built port ground. Offset is the file's
    /// usual convention (positive = inland), so n = -offset.</summary>
    private static bool InTunaPortFootprint(CoastRoute r, int i, float offset)
    {
        if (!TunaPortEnabled) return false;
        var p = r.Position[i];
        var s = r.SideFlat(i);
        var w = new Vector3(p.x + s.x * offset, 0f, p.z + s.z * offset);
        foreach (var (f, a0, a1, n0, n1) in PortFootprints(r))
        {
            var l = f.Local(w);
            if (l.x >= a0 && l.x <= a1 && l.y >= n0 && l.y <= n1) return true;
        }
        return false;
    }

    /// <summary>Guardrail yields where the market apron and the S2 tent row own the seaward verge.</summary>
    private static bool TunaPortOwnsSeawardVerge(CoastRoute r, float d)
    {
        if (!TunaPortEnabled) return false;
        return (d > PortS1AnchorM + PortApronA0 - 1f && d < PortS1AnchorM + PortApronA1 + 1f) ||
               (d > PortS2AnchorM - 84f && d < PortS2AnchorM + 84f);
    }

    // ------------------------------------------------------------------ merged mesh bins

    private sealed class PB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
    }

    private sealed class PortBins
    {
        private readonly Dictionary<(Material m, int chunk, bool shadow), PB> _map =
            new Dictionary<(Material, int, bool), PB>();
        public int chunk;
        public bool shadow = true;
        /// <summary>Walkable floors: flushed as "Connected Port Walk ..." with MeshColliders, which
        /// MinatoCrowdActor.Ground() ranks above terrain - without them every townsperson on the
        /// apron / hall floor / pavements snaps down to the terrain underneath and disappears.</summary>
        public bool walk;
        /// <summary>Follow another bin set's chunk so callers only SetChunk the main bins.</summary>
        public PortBins chunkFrom;
        /// <summary>&gt; 0: each chunk's renderers get a single-LOD LODGroup culled beyond this distance.</summary>
        public float cullM;
        public PB this[Material m]
        {
            get
            {
                var k = (m, chunkFrom != null ? chunkFrom.chunk : chunk, shadow);
                if (!_map.TryGetValue(k, out var b)) { b = new PB(); _map[k] = b; }
                return b;
            }
        }
        public void SetChunk(float a) => chunk = Mathf.FloorToInt(a / 60f);
        public int Tris
        {
            get { int n = 0; foreach (var b in _map.Values) n += b.t.Count / 3; return n; }
        }

        public int Flush(Transform parent, string label)
        {
            int n = 0;
            var perChunk = new Dictionary<int, List<Renderer>>();
            foreach (var kv in _map)
            {
                if (kv.Value.t.Count == 0) continue;
                var (m, c, sh) = kv.Key;
                string nm = $"Shiosai_TP_{label}_{c + 50}_{(sh ? "S" : "N")}_{m.name.Replace("Shiosai_TP_", "")}";
                var mesh = Finish(nm, kv.Value.v.ToArray(), kv.Value.uv.ToArray(), kv.Value.t);
                var go = AddMesh(parent, $"{label} {c + 50} {m.name.Replace("Shiosai_TP_", "")}", mesh, m, collider: walk);
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = sh ? UnityEngine.Rendering.ShadowCastingMode.On
                                          : UnityEngine.Rendering.ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic |
                                                           StaticEditorFlags.OccluderStatic |
                                                           StaticEditorFlags.OccludeeStatic);
                if (cullM > 0f)
                {
                    if (!perChunk.TryGetValue(c, out var l)) perChunk[c] = l = new List<Renderer>();
                    l.Add(mr);
                }
                n++;
            }
            _map.Clear();
            foreach (var kv in perChunk)
            {
                var rs = kv.Value.ToArray();
                var bounds = rs[0].bounds;
                foreach (var r in rs) bounds.Encapsulate(r.bounds);
                var g = new GameObject($"{label} {kv.Key + 50} Cull", typeof(LODGroup));
                g.transform.SetParent(parent, false);
                g.transform.position = bounds.center;
                foreach (var r in rs) r.transform.SetParent(g.transform, true);
                var lg = g.GetComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.5f, rs) });
                lg.RecalculateBounds();
                float h = lg.size / (2f * cullM * Mathf.Tan(30f * Mathf.Deg2Rad));
                lg.SetLODs(new[] { new LOD(Mathf.Clamp(h, 0.002f, 0.95f), rs) });
                lg.fadeMode = LODFadeMode.None;
            }
            return n;
        }
    }

    // ------------------------------------------------------------------ materials

    private static readonly Dictionary<string, Texture2D> _portTex = new Dictionary<string, Texture2D>();

    private static Texture2D PortTex(string file)
    {
        if (_portTex.TryGetValue(file, out var t) && t != null) return t;
        t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortTexDir}/{file}.png");
        if (t == null) Debug.LogWarning($"[tunaport] missing texture {file}.png - run tools/blender/make_shiosai_tunaport_textures.py");
        _portTex[file] = t;
        return t;
    }

    /// <summary>
    /// Port surfaces are HDRP/Lit (real specular + normal/mask maps), not CelLit - the brief is
    /// "high quality, not Roblox". The coast's light rig is non-physical and its sky ambient is
    /// strongly blue, so every base colour is multiplied by PortGain: the same warm skew that
    /// lands the (HDRP/Lit) asphalt neutral (CoastAsphaltTint, measured), eased for walls that
    /// see more of the warm key than the road does. PROVISIONAL - tuned against captures.
    /// </summary>
    private static readonly Color PortGain = new Color(1.0f, 0.90f, 0.76f);

    private static Material PortLitBase(string full, Color c, bool twoSided, float smooth, float metal)
    {
        var m = LoadOrCreate(full, "HDRP/Lit");
        m.SetColor("_BaseColor", new Color(c.r * PortGain.r, c.g * PortGain.g, c.b * PortGain.b, 1f));
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_SmoothnessRemapMin", 0f);
        m.SetFloat("_SmoothnessRemapMax", smooth);
        m.SetFloat("_DoubleSidedEnable", twoSided ? 1f : 0f);
        m.SetFloat("_DoubleSidedNormalMode", 0f);
        m.SetFloat("_CullMode", twoSided ? 0f : 2f);
        m.SetFloat("_CullModeForward", twoSided ? 0f : 2f);
        m.SetTexture("_BaseColorMap", null);
        m.SetTexture("_NormalMap", null);
        m.SetTexture("_MaskMap", null);
        m.mainTextureScale = Vector2.one;
        return m;
    }

    /// <summary>Self-lit surface (fluorescent tubes, lit windows). Same emission recipe as
    /// TunnelLampMaterial: LDR colour x intensity in nits, which survives a material re-save.</summary>
    private static Material PMGlow(string name, Color c, float nits)
    {
        string full = "Shiosai_TP_" + name;
        if (MaterialCache.TryGetValue(full, out var cached) && cached != null) return cached;
        var m = PortLitBase(full, c, false, 0.6f, 0f);
        m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_MASKMAP");
        if (m.HasProperty("_UseEmissiveIntensity")) m.SetFloat("_UseEmissiveIntensity", 1f);
        if (m.HasProperty("_EmissiveIntensityUnit")) m.SetFloat("_EmissiveIntensityUnit", 0f);
        if (m.HasProperty("_EmissiveIntensity")) m.SetFloat("_EmissiveIntensity", nits);
        if (m.HasProperty("_EmissiveColorLDR")) m.SetColor("_EmissiveColorLDR", c);
        if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", c * nits);
        if (m.HasProperty("_EmissiveExposureWeight")) m.SetFloat("_EmissiveExposureWeight", 0f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        MaterialCache[full] = m;
        return m;
    }

    /// <summary>Plain or card-textured port material (signs, cloth, paint, plastics).</summary>
    private static Material PM(string name, Color c, string tex = null, bool twoSided = false,
                               float rim = 0.16f, float gloss = 0.12f, float spec = 0.06f)
    {
        string full = "Shiosai_TP_" + name;
        if (MaterialCache.TryGetValue(full, out var cached) && cached != null) return cached;
        var m = PortLitBase(full, c, twoSided, Mathf.Clamp01(gloss), 0f);
        if (tex != null) m.SetTexture("_BaseColorMap", PortTex(tex));
        m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_MASKMAP");
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        MaterialCache[full] = m;
        return m;
    }

    /// <summary>PBR surface from a TP_&lt;stem&gt;_{Albedo,Normal,Mask} set. UVs are authored in
    /// METRES, so the tiling is 1 / tileM (the real size one texture repeat represents).</summary>
    private static Material PMP(string name, string stem, Color tint, float tileM, float normalScale = 1f,
                                bool twoSided = false)
    {
        string full = "Shiosai_TP_" + name;
        if (MaterialCache.TryGetValue(full, out var cached) && cached != null) return cached;
        var m = PortLitBase(full, tint, twoSided, 1f, 0f);
        var alb = PortTex($"TP_{stem}_Albedo");
        var nrm = PortTex($"TP_{stem}_Normal");
        var msk = PortTex($"TP_{stem}_Mask");
        m.SetTexture("_BaseColorMap", alb);
        m.SetTexture("_NormalMap", nrm);
        m.SetTexture("_MaskMap", msk);
        m.SetFloat("_NormalScale", normalScale);
        m.SetFloat("_SmoothnessRemapMax", 1f);
        m.SetFloat("_AORemapMin", 0f);
        m.SetFloat("_AORemapMax", 1f);
        m.SetFloat("_Metallic", 1f);   // scaled by mask R
        if (nrm != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
        if (msk != null) m.EnableKeyword("_MASKMAP"); else m.DisableKeyword("_MASKMAP");
        m.mainTextureScale = new Vector2(1f / tileM, 1f / tileM);
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        MaterialCache[full] = m;
        return m;
    }

    // ------------------------------------------------------------------ primitives
    // Every face is wound from an explicit outward normal, so no primitive can render inside out.

    private static void PQ(PB b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                           Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3, Vector3 n)
    {
        int i = b.v.Count;
        b.v.Add(p0); b.v.Add(p1); b.v.Add(p2); b.v.Add(p3);
        b.uv.Add(u0); b.uv.Add(u1); b.uv.Add(u2); b.uv.Add(u3);
        bool ok = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) >= 0f;
        if (ok) { b.t.Add(i); b.t.Add(i + 1); b.t.Add(i + 2); b.t.Add(i); b.t.Add(i + 2); b.t.Add(i + 3); }
        else { b.t.Add(i); b.t.Add(i + 2); b.t.Add(i + 1); b.t.Add(i); b.t.Add(i + 3); b.t.Add(i + 2); }
    }

    /// <summary>Quad from a centre, two half-extent axes and a normal; uv 0..1 (u along ax, v along ay).
    /// ax is flipped when needed so u always runs to the viewer's right: text never reads mirrored.</summary>
    private static void PCard(PB b, Vector3 c, Vector3 ax, Vector3 ay, Vector3 n, bool twoFaced = false,
                              float u0 = 0f, float u1 = 1f, float v0 = 0f, float v1 = 1f)
    {
        if (Vector3.Dot(ax, Vector3.Cross(ay, -n)) < 0f) ax = -ax;
        PQ(b, c - ax - ay, c + ax - ay, c + ax + ay, c - ax + ay,
           new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), n);
        if (twoFaced)
        {
            // 3 mm behind the front face: with a double-sided material two coplanar faces z-fight
            // and the mirrored one can win (the kaitai banner read backwards in v7)
            var off = n.normalized * 0.003f;
            PQ(b, c + ax - ay - off, c - ax - ay - off, c - ax + ay - off, c + ax + ay - off,
               new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), -n);
        }
    }

    private static void PTri(PB b, Vector3 p0, Vector3 p1, Vector3 p2, Vector2 u0, Vector2 u1, Vector2 u2, Vector3 n)
    {
        int i = b.v.Count;
        b.v.Add(p0); b.v.Add(p1); b.v.Add(p2);
        b.uv.Add(u0); b.uv.Add(u1); b.uv.Add(u2);
        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) >= 0f) { b.t.Add(i); b.t.Add(i + 1); b.t.Add(i + 2); }
        else { b.t.Add(i); b.t.Add(i + 2); b.t.Add(i + 1); }
    }

    /// <summary>Oriented box: centre and three HALF-extent axis vectors. UVs are in METRES
    /// scaled by 1/tile (tile 1 = metres, the PBR convention).</summary>
    private static void PBox(PB b, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float tile = 1f, bool bottom = false)
    {
        void Face(Vector3 fc, Vector3 u, Vector3 v, Vector3 n)
        {
            float su = u.magnitude * 2f / tile, sv = v.magnitude * 2f / tile;
            PQ(b, fc - u - v, fc + u - v, fc + u + v, fc - u + v,
               new Vector2(0, 0), new Vector2(su, 0), new Vector2(su, sv), new Vector2(0, sv), n);
        }
        Face(c + ax, az, ay, ax); Face(c - ax, az, ay, -ax);
        Face(c + az, ax, ay, az); Face(c - az, ax, ay, -az);
        Face(c + ay, ax, az, ay);
        if (bottom) Face(c - ay, ax, az, -ay);
    }

    /// <summary>Axis-aligned-in-frame box: along-road T, up, seaward N half extents.</summary>
    private static void PBoxF(PB b, PFrame f, Vector3 c, float ha, float hy, float hn, float tile = 1f, bool bottom = false) =>
        PBox(b, c, f.T * ha, Vector3.up * hy, f.N * hn, tile, bottom);

    /// <summary>Box from a ground-level base centre (height h), in the frame.</summary>
    private static void PBlock(PB b, PFrame f, Vector3 baseC, float ha, float h, float hn, float tile = 1f) =>
        PBoxF(b, f, baseC + Vector3.up * (h * 0.5f), ha, h * 0.5f, hn, tile);

    /// <summary>
    /// SMOOTH-SHADED vertex grid (shared vertices, so RecalculateNormals averages across quads -
    /// the difference between a faceted "Roblox" cylinder and a turned one). P(i, j) for
    /// i = 0..nu, j = 0..nv. outwardRef(i, j) is any point inside the surface, used once to pick
    /// the winding so the faces always point out.
    /// </summary>
    private static void PGrid(PB b, int nu, int nv, System.Func<int, int, Vector3> P,
                              System.Func<int, int, Vector2> UV, System.Func<int, int, Vector3> inside)
    {
        int baseIdx = b.v.Count;
        for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++) { b.v.Add(P(i, j)); b.uv.Add(UV(i, j)); }
        int mi = nu / 2, mj = nv / 2;
        var p0 = P(mi, mj); var p1 = P(mi + 1, mj); var p2 = P(mi + 1, mj + 1);
        bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p0 - inside(mi, mj)) < 0f;
        for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int a = baseIdx + j * (nu + 1) + i, bb = a + 1, cc = a + nu + 2, d = a + nu + 1;
                if (!flip) { b.t.Add(a); b.t.Add(bb); b.t.Add(cc); b.t.Add(a); b.t.Add(cc); b.t.Add(d); }
                else { b.t.Add(a); b.t.Add(cc); b.t.Add(bb); b.t.Add(a); b.t.Add(d); b.t.Add(cc); }
            }
    }

    private static void PCyl(PB b, Vector3 baseC, Vector3 axis, float r0, float r1, float h, int seg,
                             bool capTop = true, float vTile = 1f)
    {
        axis.Normalize();
        seg = Mathf.Max(seg, 6);
        var u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        var w = Vector3.Cross(axis, u);
        var top = baseC + axis * h;
        PGrid(b, seg, 1,
              (i, j) =>
              {
                  float a = i * Mathf.PI * 2f / seg;
                  return (j == 0 ? baseC : top) + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * (j == 0 ? r0 : r1);
              },
              (i, j) => new Vector2((float)i / seg, j * vTile),
              (i, j) => j == 0 ? baseC : top);
        if (capTop && r1 > 0.001f)
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                PTri(b, top, top + (u * Mathf.Cos(a0) + w * Mathf.Sin(a0)) * r1, top + (u * Mathf.Cos(a1) + w * Mathf.Sin(a1)) * r1,
                     new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.right, axis);
            }
    }

    /// <summary>Smooth ellipsoid (lanterns, buoys, net heaps): u around, v bottom->top.</summary>
    private static void PEllipsoid(PB b, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, int seg = 16, int rings = 10)
    {
        seg = Mathf.Max(seg, 14); rings = Mathf.Max(rings, 8);
        PGrid(b, seg, rings,
              (k, j) =>
              {
                  float th = k * Mathf.PI * 2f / seg;
                  float ph = -Mathf.PI * 0.5f + j * Mathf.PI / rings;
                  return c + (ax * Mathf.Cos(th) + az * Mathf.Sin(th)) * Mathf.Cos(ph) + ay * Mathf.Sin(ph);
              },
              (k, j) => new Vector2((float)k / seg, (float)j / rings),
              (k, j) => c);
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>Walkable-floor bins for the current BuildTunaPort pass (see PortBins.walk).</summary>
    private static PortBins _walk;
    /// <summary>Pass-2 micro detail (fish on trays, cats, cables, clutter): culled at PortFineCullM.</summary>
    private static PortBins _fine;

    /// <summary>Called from BuildHarbour in place of the legacy house/mole/boat layout.</summary>
    private static void BuildTunaPort(Transform town, CoastRoute route)
    {
        _portFoot = null;
        _portTex.Clear();
        _portPeople = 0;
        _gear.Clear(); _gearPieces = 0; _outfitted = 0; _scanNearest = 0; _scanFailed = 0; _scanFailKeys.Clear();
        PurgeLegacyKitMeshes();
        var root = new GameObject(PortGroupName).transform;
        root.SetParent(town, false);
        var s1 = PortFrame(route, PortS1AnchorM);
        var s2 = PortFrame(route, PortS2AnchorM);
        var cast = PortCrowdCast();
        var people = new GameObject("Tuna Port People").transform;
        people.SetParent(root, false);

        var big = new PortBins { shadow = true };
        var small = new PortBins { shadow = false, cullM = PortSmallCullM };
        _walk = new PortBins { shadow = false, walk = true, chunkFrom = big };
        _fine = new PortBins { shadow = false, cullM = PortFineCullM, chunkFrom = big };
        var rng = new System.Random(ScatterSeed + 27);

        BuildPortApronAndQuay(big, small, s1);
        BuildMarketHall(big, small, s1, rng, cast, people);
        BuildQuayside(big, small, s1, rng, cast, people, root);
        BuildPortBoats(big, small, s1, rng);
        BuildShopRow(big, small, s1, -72f, 64f, rng, cast, people, 0);
        BuildShopRow(big, small, s2, -80f, 80f, rng, cast, people, 7);
        BuildTentRow(big, small, s2, rng, cast, people);
        int houses = BuildPortHouses(big, small, route, s1, s2, rng);
        // pass 2 has its own RNG so the pass-1 layout above is unchanged
        BuildPortDetail(big, small, s1, s2, new System.Random(ScatterSeed + 41), cast, people, root);

        int tris = big.Tris + small.Tris + _walk.Tris + _fine.Tris;
        int objs = big.Flush(root, "Port") + small.Flush(root, "PortSmall") + _walk.Flush(root, "Connected Port Walk") +
                   _fine.Flush(root, "PortFine");
        _walk = null; _fine = null;
        Debug.Log($"[tunaport] built: {objs} merged meshes, {tris} tris, {houses} machiya/minka, " +
                  $"{_portPeople} townsfolk (crowd donors {cast.walk.Count}w/{cast.stand.Count}s/{cast.sit.Count}sit), " +
                  $"{_outfitted} outfitted, {_gearPieces} gear pieces from {_gear.Count} fitted meshes " +
                  $"({EmptyGear()} empty; scans: {_scanNearest} by nearest bone, {_scanFailed} failed [{string.Join(",", _scanFailKeys)}]).");
    }

    private static int EmptyGear() { int n = 0; foreach (var r in _gear.Values) if (r.mesh == null) n++; return n; }

    /// <summary>Pass 1 persisted one headband/apron mesh PER PERSON; gear is now per donor mesh.</summary>
    private static void PurgeLegacyKitMeshes()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            string f = System.IO.Path.GetFileName(p);
            if (f.StartsWith("Shiosai_TP_Hachimaki_") || f.StartsWith("Shiosai_TP_Apron_"))
            {
                AssetDatabase.DeleteAsset(p);
                n++;
            }
        }
        if (n > 0) Debug.Log($"[tunaport] purged {n} legacy per-person kit meshes.");
    }

    /// <summary>
    /// Re-stages ONLY what the tuna port touches (harbour group, guardrail, verge dressing) on
    /// the saved scene, without the full Apply (which also re-stages traffic, graph and sky).
    /// run_steps.ps1 "ShiosaiCoastEnvironment.StageTunaPortOnly|copilot_tunaport_stage.log|1"
    /// </summary>
    public static void StageTunaPortOnly()
    {
        bool headless = Application.isBatchMode;
        if (headless && EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.Refresh();
        SakuraTextureImportSettings.ApplyAll();
        MaterialCache.Clear();
        var route = CoastRoute.Load();
        var roots = FindRootsByExactName(RootName);
        if (roots.Count == 0) { Debug.LogError("[tunaport] no coast root in scene - run Apply."); return; }
        var root = roots[0].transform;

        var landmarks = root.Find("Coast Landmarks");
        if (landmarks == null) { Debug.LogError("[tunaport] no 'Coast Landmarks' group."); return; }
        for (int k = landmarks.childCount - 1; k >= 0; k--)
            if (landmarks.GetChild(k).name == "Shiosai Harbour")
                Object.DestroyImmediate(landmarks.GetChild(k).gameObject);
        BuildHarbour(landmarks, route);

        for (int k = root.childCount - 1; k >= 0; k--)
        {
            string n = root.GetChild(k).name;
            if (n == "Coast Guardrail" || n == "Coast Dressing")
                Object.DestroyImmediate(root.GetChild(k).gameObject);
        }
        BuildGuardrail(root, route);
        ScatterCoast(root, route);

        if (headless)
        {
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            AssetDatabase.SaveAssets();
            Debug.Log($"[tunaport] staged and saved '{active.path}'.");
        }
    }
}

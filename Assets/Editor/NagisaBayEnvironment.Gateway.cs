// Worker E (premier-resort brief sections 1 + 2): NAGISA BAY grand entrance.
//   * Stage 89 "Gateway": a portal gateway over the start straight (route d = GwGateM) with two white-concrete / teak
//     pylons just OUTSIDE the shoulder (+/-5.6 m), a 7 m clearance beam, a NAGISA BAY wordmark sign, welcome plaques,
//     sway flags; a landscaped sea-side entrance boulevard (royal-palm avenue, lamps, planters, green cycle-lane strip);
//     and the OCEAN-REVEAL screen: a Japanese black-pine dune grove on the sea side that hides the bay except for
//     glimpses until route d = GwScreenToM, where the grove ends at the marina bend and the bay opens up.
// Also hosts the helpers shared with NagisaBayEnvironment.Overlook.cs (GwBag meshes, signs, pines, blockers).
// Nothing here is a collider; nothing is inside the ride road + shoulder (4.1 m); the land-side pylon sits in the
// kerb band (-4.1..-8 m, "nobody" band), clear of the highway deck edge (-8 m).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    // ---- PROVISIONAL art tunables --------------------------------------------------------------------
    private const string GwDir = NagisaTex + "/Gateway";
    private const float GwGateM = 130f;          // portal station (after the welcome board at 25 m and checkpoint board at 70 m)
    private const float GwPylonO = 5.6f;         // pylon centre from the centreline
    private const float GwBeamClearM = 6.9f;     // underside of the beam above the road
    private const float GwAvenueFromM = 40f, GwScreenFromM = 150f, GwScreenToM = 430f;

    private sealed class GwBag
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
    }

    private static GwBag Gb(Dictionary<string, GwBag> d, string k)
    {
        if (!d.TryGetValue(k, out var b)) d[k] = b = new GwBag();
        return b;
    }

    // ---- geometry helpers ----------------------------------------------------------------------------
    /// <summary>Emit a triangle whose Unity front face (numeric cross) points along <paramref name="outward"/>.</summary>
    private static void GwTri(GwBag b, int i0, int i1, int i2, Vector3 outward)
    {
        var n = Vector3.Cross(b.v[i1] - b.v[i0], b.v[i2] - b.v[i0]);
        if (Vector3.Dot(n, outward) >= 0f) { b.t.Add(i0); b.t.Add(i1); b.t.Add(i2); }
        else { b.t.Add(i0); b.t.Add(i2); b.t.Add(i1); }
    }

    /// <summary>Oriented box with explicit axes (ax/ay/az orthonormal), sizes w/h/d along them. UV = metres / 4.</summary>
    private static void GwOBox(GwBag b, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, float w, float h, float d)
    {
        Vector3 hx = ax * (w * 0.5f), hy = ay * (h * 0.5f), hz = az * (d * 0.5f);
        void Face(Vector3 n, Vector3 a, Vector3 b2, float sa, float sb)
        {
            int k = b.v.Count; var o = c + n;
            b.v.Add(o - a - b2); b.v.Add(o + a - b2); b.v.Add(o + a + b2); b.v.Add(o - a + b2);
            b.uv.Add(new Vector2(0, 0)); b.uv.Add(new Vector2(sa / 4f, 0));
            b.uv.Add(new Vector2(sa / 4f, sb / 4f)); b.uv.Add(new Vector2(0, sb / 4f));
            GwTri(b, k, k + 1, k + 2, n); GwTri(b, k, k + 2, k + 3, n);
        }
        Face(hx, hz, hy, d, h); Face(-hx, hz, hy, d, h);
        Face(hy, hx, hz, w, d); Face(-hy, hx, hz, w, d);
        Face(hz, hx, hy, w, h); Face(-hz, hx, hy, w, h);
    }

    /// <summary>World-axis-aligned-to-frame box: side = lateral axis, fwd = along-road axis, up = world up.</summary>
    private static void GwFBox(GwBag b, Vector3 c, Vector3 side, Vector3 fwd, float w, float h, float d) =>
        GwOBox(b, c, side, Vector3.up, fwd, w, h, d);

    /// <summary>Box between two points (a -> c), cross-section w x h.</summary>
    private static void GwBeam(GwBag b, Vector3 a, Vector3 c, float w, float h)
    {
        var dir = (c - a); float len = dir.magnitude; if (len < 1e-3f) return; dir /= len;
        var hint = Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up;
        var side = Vector3.Cross(hint, dir).normalized;
        var up = Vector3.Cross(dir, side).normalized;
        GwOBox(b, (a + c) * 0.5f, side, up, dir, w, h, len);
    }

    private static void GwCyl(GwBag b, Vector3 a, Vector3 c, float r0, float r1, int seg = 8)
    {
        var dir = c - a; float len = dir.magnitude; if (len < 1e-3f) return; dir /= len;
        var hint = Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up;
        var u = Vector3.Cross(hint, dir).normalized;
        var w = Vector3.Cross(dir, u).normalized;
        int k = b.v.Count;
        for (int i = 0; i < seg; i++)
        {
            float ang = i * Mathf.PI * 2f / seg; var rad = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
            b.v.Add(a + rad * r0); b.v.Add(c + rad * r1);
            b.uv.Add(new Vector2(i / (float)seg, 0f)); b.uv.Add(new Vector2(i / (float)seg, len / 4f));
        }
        for (int i = 0; i < seg; i++)
        {
            int i0 = k + i * 2, i1 = k + ((i + 1) % seg) * 2;
            var rad = ((b.v[i0] - a) + (b.v[i1] - a)).normalized;
            GwTri(b, i0, i1, i1 + 1, rad); GwTri(b, i0, i1 + 1, i0 + 1, rad);
        }
    }

    /// <summary>Flat sign quad facing <paramref name="n"/> (front face), with a UV sub-rect (u0,v0,u1,v1).</summary>
    private static void GwSign(GwBag b, Vector3 c, Vector3 n, Vector3 upHint, float w, float h,
                               float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
    {
        n.Normalize();
        var r = Vector3.Cross(n, upHint).normalized;
        var u = Vector3.Cross(r, n).normalized;
        int k = b.v.Count;
        b.v.Add(c - r * (w * 0.5f) - u * (h * 0.5f)); b.v.Add(c + r * (w * 0.5f) - u * (h * 0.5f));
        b.v.Add(c + r * (w * 0.5f) + u * (h * 0.5f)); b.v.Add(c - r * (w * 0.5f) + u * (h * 0.5f));
        b.uv.Add(new Vector2(u0, v0)); b.uv.Add(new Vector2(u1, v0)); b.uv.Add(new Vector2(u1, v1)); b.uv.Add(new Vector2(u0, v1));
        GwTri(b, k, k + 1, k + 2, n); GwTri(b, k, k + 2, k + 3, n);
    }

    /// <summary>Strip following the route between metres, lateral offsets oA..oB (sgn applied), at route y + dy.</summary>
    private static void GwStrip(GwBag b, float m0, float m1, float oA, float oB, float sgn, float dy,
                                float uScale, float vScale, Vector3 outward)
    {
        int i0 = _route.IndexAt(m0), i1 = _route.IndexAt(m1);
        int prevA = -1, prevB = -1;
        for (int i = i0; i <= i1; i++)
        {
            var p = _route.Position[i]; var s = _route.SideFlat(i) * sgn;
            int a = b.v.Count;
            b.v.Add(new Vector3(p.x + s.x * oA, p.y + dy, p.z + s.z * oA));
            b.v.Add(new Vector3(p.x + s.x * oB, p.y + dy, p.z + s.z * oB));
            float vv = _route.Distance[i] / vScale;
            b.uv.Add(new Vector2(oA / uScale, vv)); b.uv.Add(new Vector2(oB / uScale, vv));
            if (prevA >= 0)
            {
                GwTri(b, prevA, prevB, a + 1, outward); GwTri(b, prevA, a + 1, a, outward);
            }
            prevA = a; prevB = a + 1;
        }
    }

    private static Vector3 GwW(float m, float o, float h, float sgn)
    {
        int i = _route.IndexAt(m);
        var p = _route.Position[i];
        var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
        var s = _route.SideFlat(i) * sgn;
        return p + s * o + t * (m - _route.Distance[i]) + Vector3.up * h;
    }

    private static void GwFrame(float m, float sgn, out Vector3 t, out Vector3 s)
    {
        int i = _route.IndexAt(m);
        t = _route.Tangent[i]; t.y = 0f; t.Normalize();
        s = _route.SideFlat(i) * sgn;
    }

    private static float GwYaw(Vector3 facing) => Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg;

    private static void GwFlush(Transform parent, string prefix, Dictionary<string, GwBag> bags,
                                Dictionary<string, Material> mats)
    {
        foreach (var kv in bags)
        {
            if (kv.Value.t.Count == 0) continue;
            if (!mats.TryGetValue(kv.Key, out var mat)) { Debug.LogWarning($"[gw] no material '{kv.Key}'"); continue; }
            var mesh = Finish("Nagisa_GW_" + prefix.Replace(' ', '_') + "_" + kv.Key, kv.Value.v, kv.Value.uv, kv.Value.t);
            var go = AddMesh(parent, prefix + " " + kv.Key, mesh, mat, false);
            go.isStatic = true;
        }
    }

    // ---- materials -------------------------------------------------------------------------------------
    private static Texture2D GwTex(string file)
    {
        string p = $"{GwDir}/{file}";
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    private static Material GwSignMat(string slot, string file, float cull = 2f)
    {
        var m = Cel("Nagisa_GW_" + slot, Color.white, 0.3f, 0.1f, 0.04f, GwTex(file), null, 0.8f, cull);
        m.SetFloat("_ShadowAmbient", 0.85f);
        return m;
    }

    private static Dictionary<string, Material> GwMats()
    {
        var d = new Dictionary<string, Material>();
        d["white"] = Cel("Nagisa_GW_White", new Color(0.95f, 0.94f, 0.90f), 0.15f, 0.10f, 0.12f,
                         NbTex("NB_Plaster", "Albedo"), NbTex("NB_Plaster", "Normal"), 0.4f);
        d["stone"] = Cel("Nagisa_GW_Stone", new Color(0.88f, 0.84f, 0.76f), 0.2f, 0.10f, 0.10f,
                         NbTex("NB_Stone", "Albedo"), NbTex("NB_Stone", "Normal"), 0.8f);
        d["teak"] = Cel("Nagisa_GW_Teak", Color.white, 0.3f, 0.14f, 0.10f, NbTex("NB_Teak", "Albedo"), NbTex("NB_Teak", "Normal"), 0.8f);
        d["paving"] = Cel("Nagisa_GW_Paving", new Color(0.62f, 0.58f, 0.52f), 0.15f, 0.08f, 0.05f,
                          NbTex("NB_PlazaStone", "Albedo"), NbTex("NB_PlazaStone", "Normal"), 0.6f);
        d["steel"] = Cel("Nagisa_GW_Steel", Srgb(74, 84, 92), 0.6f, 0.5f, 0.2f);
        d["teal"] = Cel("Nagisa_GW_Teal", Srgb(14, 108, 122), 0.4f, 0.25f, 0.12f);
        d["coral"] = Cel("Nagisa_GW_Coral", Srgb(240, 108, 80), 0.4f, 0.2f, 0.12f);
        d["lane"] = Cel("Nagisa_GW_Lane", new Color(0.62f, 0.66f, 0.62f), 0.2f, 0.08f, 0.04f, GwTex("NB_GW_Lane.png"), null, 0.8f);
        d["flagCoral"] = Cel("Nagisa_GW_FlagCoral", Srgb(240, 108, 80), 0.1f, 0.05f, 0.1f, null, null, 0.8f, 0f);
        d["flagTeal"] = Cel("Nagisa_GW_FlagTeal", Srgb(14, 108, 122), 0.1f, 0.05f, 0.1f, null, null, 0.8f, 0f);
        d["awning"] = Cel("Nagisa_GW_Awning", Color.white, 0.1f, 0.05f, 0.1f, NbTex("NB_Awning", "Albedo"), null, 0.8f, 0f);
        d["wordmark"] = GwSignMat("Wordmark", "NB_GW_Wordmark.png");
        d["welcome"] = GwSignMat("Welcome", "NB_GW_Welcome.png");
        d["skyline"] = GwSignMat("Skyline", "NB_GW_Skyline.png");
        d["panorama"] = GwSignMat("Panorama", "NB_GW_Panorama.png");
        d["kiosk"] = GwSignMat("Kiosk", "NB_GW_Kiosk.png");
        d["direction"] = GwSignMat("Direction", "NB_GW_Direction.png");
        d["bridge"] = GwSignMat("Bridge", "NB_GW_Bridge.png");
        // Tint must stay near white: the bark/hedge albedos are already dark, and a grey tint crushed the whole pine to pure black (2026-10-02).
        d["pineTrunk"] = Cel("Nagisa_GW_PineTrunk", Srgb(255, 255, 255), 0.05f, 0.03f, 0.1f,
                             NbTex("NB_PalmBark", "Albedo"), NbTex("NB_PalmBark", "Normal"), 0.9f);
        d["pineLeaf"] = Cel("Nagisa_GW_PineLeaf", Srgb(230, 255, 230), 0.08f, 0.05f, 0.18f,
                            NbTex("NB_Hedge", "Albedo"), NbTex("NB_Hedge", "Normal"), 0.9f);
        d["glass"] = _nb["NB_BalconyGlass"];
        d["lamp"] = _nb["NB_Lamp"];
        return d;
    }

    // ---- blockers (existing non-batched props) ---------------------------------------------------------
    private static List<Bounds> GwBlockers(Transform group, params (float m0, float m1, float pad)[] windows)
    {
        var boxes = new List<Bounds>();
        foreach (var w in windows)
        {
            int i0 = _route.IndexAt(w.m0), i1 = _route.IndexAt(w.m1);
            var bb = new Bounds(_route.Position[i0], Vector3.zero);
            for (int i = i0; i <= i1; i++) bb.Encapsulate(_route.Position[i]);
            bb.Expand(new Vector3(w.pad * 2f, 400f, w.pad * 2f));
            boxes.Add(bb);
        }
        var res = new List<Bounds>();
        foreach (var r in group.root.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || r.transform.IsChildOf(group)) continue;
            var b = r.bounds;
            if (b.extents.x > 25f || b.extents.z > 25f) continue;   // terrain chunks / batched meshes / sea
            if (b.size.y < 0.7f) continue;                          // grass tufts, decals, paint: not obstacles
            foreach (var bx in boxes) if (bx.Intersects(b)) { res.Add(b); break; }
        }
        return res;
    }

    private static bool GwFree(List<Bounds> bl, Vector3 p, float r)
    {
        float r2 = r * r;
        foreach (var b in bl)
        {
            float dx = Mathf.Max(b.min.x - p.x, 0f, p.x - b.max.x);
            float dz = Mathf.Max(b.min.z - p.z, 0f, p.z - b.max.z);
            if (dx * dx + dz * dz < r2) return false;
        }
        return true;
    }

    /// <summary>Dry land with a real ground height; no corridor test (verge props sit inside CanPlace's keep-out).</summary>
    private static bool GwOk(float x, float z, out float y)
    {
        y = _ground.Height(x, z);
        return _ground.Coast(x, z) > 1f && y > 0.25f;
    }

    /// <summary>Deactivate foreign (other-stage) props standing in a route-space zone (m0..m1, lateral o0..o1 toward sgn).
    /// Rebuilt deterministically each Apply, so this never leaves permanent damage.</summary>
    private static int GwClearZone(Transform group, float m0, float m1, float o0, float o1, float sgn)
    {
        int n = 0;
        foreach (var r in group.root.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || r.transform.IsChildOf(group)) continue;
            var b = r.bounds;
            if (b.extents.x > 25f || b.extents.z > 25f) continue;
            var pos = b.center;
            _route.PlanDistance(pos.x, pos.z, out int i);
            var p = _route.Position[i]; var s = _route.SideFlat(i) * sgn;
            var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
            float o = (pos.x - p.x) * s.x + (pos.z - p.z) * s.z;
            float m = _route.Distance[i] + (pos.x - p.x) * t.x + (pos.z - p.z) * t.z;
            if (m < m0 || m > m1 || o < o0 || o > o1) continue;
            var lg = r.GetComponentInParent<LODGroup>();
            var go = lg != null ? lg.gameObject : r.gameObject;
            if (go.activeSelf) { go.SetActive(false); n++; }
        }
        return n;
    }

    // ---- black pine ------------------------------------------------------------------------------------
    private static Mesh[] _gwPines;

    private static Mesh GwPineMesh(int variant)
    {
        var rng = new System.Random(4100 + variant * 17);
        float F() => (float)rng.NextDouble();
        var v = new List<Vector3>(); var uv = new List<Vector2>();
        var tt = new List<int>(); var tl = new List<int>();
        float H = 7.2f + variant * 0.9f;
        float ba = F() * Mathf.PI * 2f, bend = 1.1f + F() * 1.2f;
        var bendV = new Vector3(Mathf.Cos(ba), 0f, Mathf.Sin(ba)) * bend;
        Vector3 Trunk(float u) => new Vector3(0f, u * H, 0f) + bendV * (u * u) +
            new Vector3(Mathf.Sin(u * 7f + variant) * 0.25f, 0f, Mathf.Cos(u * 6f) * 0.2f) * u;
        // trunk: 8 rings x 7 sides
        int rings = 8, sides = 7, k0 = v.Count;
        for (int r = 0; r < rings; r++)
        {
            float u = r / (float)(rings - 1) * 0.97f; float rad = Mathf.Lerp(0.42f, 0.11f, u);
            var c = Trunk(u);
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                v.Add(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad);
                uv.Add(new Vector2(s / (float)sides, u * H / 3f));
            }
        }
        for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = k0 + r * sides + s, b = k0 + r * sides + (s + 1) % sides, c2 = a + sides, d = b + sides;
                tt.Add(a); tt.Add(c2); tt.Add(b); tt.Add(b); tt.Add(c2); tt.Add(d);   // outward (clockwise from outside)
            }
        // cloud pads
        void Pad(Vector3 c, float rx, float ry)
        {
            int latN = 5, lonN = 9, k = v.Count;
            float ph = F() * 6f;
            for (int la = 0; la <= latN; la++)
            {
                float pl = Mathf.PI * la / latN - Mathf.PI * 0.5f;
                float ys = Mathf.Sin(pl); float xs = Mathf.Cos(pl);
                for (int lo = 0; lo < lonN; lo++)
                {
                    float a = lo * Mathf.PI * 2f / lonN;
                    float lump = 1f + 0.16f * Mathf.Sin(a * 3f + ph) + 0.08f * Mathf.Sin(a * 5f - ph);
                    float yy = ys * ry * (ys < 0f ? 0.55f : 1f);
                    v.Add(c + new Vector3(Mathf.Cos(a) * xs * rx * lump, yy, Mathf.Sin(a) * xs * rx * lump));
                    uv.Add(new Vector2(Mathf.Cos(a) * xs * 0.5f + 0.5f, Mathf.Sin(a) * xs * 0.5f + 0.5f));
                }
            }
            for (int la = 0; la < latN; la++)
                for (int lo = 0; lo < lonN; lo++)
                {
                    int a = k + la * lonN + lo, b = k + la * lonN + (lo + 1) % lonN, c2 = a + lonN, d = b + lonN;
                    tl.Add(a); tl.Add(b); tl.Add(c2); tl.Add(b); tl.Add(d); tl.Add(c2);
                }
        }
        var top = Trunk(0.97f);
        Pad(top + Vector3.up * 0.2f, 2.7f + F() * 0.5f, 1.05f);
        int nSide = 4;
        for (int i = 0; i < nSide; i++)
        {
            float u = 0.5f + 0.4f * i / (nSide - 1) + F() * 0.06f;
            float a = F() * Mathf.PI * 2f; float off = 1.9f + F() * 0.9f;
            var tc = Trunk(u);
            var pc = tc + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * off;
            Pad(pc, 1.6f + F() * 0.7f, 0.8f);
            // branch (tapered 4-sided stub)
            int k = v.Count;
            var dir = (pc - tc).normalized; var sd = Vector3.Cross(Vector3.up, dir).normalized; var up2 = Vector3.Cross(dir, sd);
            for (int e = 0; e < 2; e++)
                for (int s = 0; s < 4; s++)
                {
                    float an = s * Mathf.PI / 2f; var o = (sd * Mathf.Cos(an) + up2 * Mathf.Sin(an)) * (e == 0 ? 0.13f : 0.06f);
                    v.Add((e == 0 ? tc : pc - dir * 0.3f) + o); uv.Add(new Vector2(s * 0.25f, e));
                }
            for (int s = 0; s < 4; s++)
            {
                int a0 = k + s, b0 = k + (s + 1) % 4, c0 = a0 + 4, d0 = b0 + 4;
                tt.Add(a0); tt.Add(c0); tt.Add(b0); tt.Add(b0); tt.Add(c0); tt.Add(d0);
            }
        }
        var m = new Mesh { name = "Nagisa_GW_Pine" + variant };
        m.SetVertices(v); m.SetUVs(0, uv);
        m.subMeshCount = 2;
        m.SetTriangles(tt, 0); m.SetTriangles(tl, 1);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        string path = $"{MeshDir}/{m.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static GameObject GwPine(Transform parent, Dictionary<string, Material> mats, Vector3 pos, float yaw, float scale, int variant)
    {
        if (_gwPines == null || _gwPines.Length != 3 || _gwPines[0] == null)
            _gwPines = new[] { GwPineMesh(0), GwPineMesh(1), GwPineMesh(2) };
        var go = new GameObject("Black Pine", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        go.GetComponent<MeshFilter>().sharedMesh = _gwPines[variant % 3];
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { mats["pineTrunk"], mats["pineLeaf"] };
        mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
        var lg = go.AddComponent<LODGroup>();
        lg.SetLODs(new[] { new LOD(0.010f, new Renderer[] { mr }) });
        lg.RecalculateBounds();
        return go;
    }

    private static void GwSaveMesh(Mesh m)
    {
        string path = $"{MeshDir}/{m.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }

    private static Mesh GwFlagMesh(string name, float w, float h)
    {
        var v = new List<Vector3> { new Vector3(0, -h * 0.5f, 0), new Vector3(w, -h * 0.5f, 0), new Vector3(w, h * 0.5f, 0), new Vector3(0, h * 0.5f, 0) };
        var uv = new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        var t = new List<int> { 0, 3, 2, 0, 2, 1 };
        return Finish(name, v, uv, t);
    }

    /// <summary>Pole + flag hung from it (flag flutters via AmbientSway). Returns the flag transform.</summary>
    private static Transform GwFlag(Transform parent, Dictionary<string, Material> mats, Dictionary<string, GwBag> bags,
                                    Mesh flagMesh, string matKey, Vector3 baseP, float poleH, Vector3 along)
    {
        GwCyl(Gb(bags, "steel"), baseP, baseP + Vector3.up * poleH, 0.07f, 0.04f, 6);
        var go = new GameObject("Flag", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = baseP + Vector3.up * (poleH - 0.7f);
        go.transform.rotation = Quaternion.LookRotation(Vector3.Cross(along, Vector3.up), Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
        go.GetComponent<MeshFilter>().sharedMesh = flagMesh;
        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mats[matKey]; mr.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    // ====================================================================================== stage
    [NagisaStage(99, "Gateway")]
    private static void BuildGatewayStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();
        if (_nb.Count == 0) { Debug.LogError("[gw] Run NagisaBayEnvironment.Apply first."); return; }
        var mats = GwMats();
        var blockers = GwBlockers(group, (0f, 600f, 60f));
        int ig = _route.IndexAt(GwGateM);
        float sg = SeaSign(ig);
        var portalT = new GameObject("NAGISA BAY Gateway Portal").transform; portalT.SetParent(group, false);
        int portalTris = BuildPortal(portalT, mats, sg);
        int pines = BuildAvenueAndScreen(group, mats, sg, blockers, out int palms, out int lamps, out int beds);
        Debug.Log($"[gw] Gateway: portal {portalTris} tris, sea side sg={sg:+0;-0}, avenue palms {palms}, lamps {lamps}, planter beds {beds}, " +
                  $"black pines {pines}, blockers {blockers.Count}, screen d {GwScreenFromM:0}-{GwScreenToM:0} m.");
    }

    private static int BuildPortal(Transform parent, Dictionary<string, Material> mats, float sg)
    {
        var bags = new Dictionary<string, GwBag>();
        GwFrame(GwGateM, sg, out var t, out var s);       // s = seaward
        var c = GwW(GwGateM, 0f, 0f, sg);
        Vector3 W(float lat, float h, float al) => c + s * lat + Vector3.up * h + t * al;
        foreach (float L in new[] { GwPylonO, -GwPylonO })
        {
            float inw = -Mathf.Sign(L);     // direction toward the road from this pylon
            GwFBox(Gb(bags, "stone"), W(L, 0.45f, 0f), s, t, 2.8f, 1.2f, 3.2f);
            GwFBox(Gb(bags, "stone"), W(L, -0.9f, 0f), s, t, 2.4f, 2.4f, 2.8f);        // footing below grade
            GwFBox(Gb(bags, "white"), W(L, 5.4f, 0f), s, t, 1.7f, 9.6f, 2.4f);
            GwFBox(Gb(bags, "teak"), W(L + inw * 0.92f, 5.2f, 0f), s, t, 0.14f, 8.4f, 2.0f);   // road-facing timber slats
            GwFBox(Gb(bags, "teak"), W(L - inw * 0.92f, 5.2f, 0f), s, t, 0.14f, 8.4f, 2.0f);
            GwFBox(Gb(bags, "stone"), W(L, 10.35f, 0f), s, t, 2.3f, 0.35f, 3.0f);
            GwSign(Gb(bags, "welcome"), W(L, 2.5f, -1.24f), -t, Vector3.up, 1.35f, 1.35f);
        }
        // beam + sign slab
        GwFBox(Gb(bags, "white"), W(0f, GwBeamClearM + 0.6f, 0f), s, t, GwPylonO * 2f + 1.7f, 1.2f, 2.0f);
        GwFBox(Gb(bags, "teak"), W(0f, GwBeamClearM - 0.02f, 0f), s, t, GwPylonO * 2f - 0.6f, 0.08f, 1.7f);
        GwFBox(Gb(bags, "coral"), W(0f, GwBeamClearM + 1.25f, 0f), s, t, GwPylonO * 2f + 1.7f, 0.12f, 2.0f);
        GwFBox(Gb(bags, "teal"), W(0f, 9.1f, 0f), s, t, 9.4f, 2.5f, 1.9f);
        GwSign(Gb(bags, "wordmark"), W(0f, 9.1f, -0.96f), -t, Vector3.up, 9.0f, 2.25f);
        GwSign(Gb(bags, "wordmark"), W(0f, 9.1f, 0.96f), t, Vector3.up, 9.0f, 2.25f);
        // flags on the pylon tops
        var flagMeshC = GwFlagMesh("Nagisa_GW_FlagCoral", 1.9f, 1.15f);
        var flagMeshT = GwFlagMesh("Nagisa_GW_FlagTeal", 1.9f, 1.15f);
        var flags = new List<Transform>();
        var fl = new GameObject("Flags").transform; fl.SetParent(parent, false);
        // AddMesh persists a mesh on first use only: flags use two shared asset meshes.
        GwSaveMesh(flagMeshC); GwSaveMesh(flagMeshT);
        int fi = 0;
        foreach (float L in new[] { GwPylonO, -GwPylonO })
            foreach (float al in new[] { -0.8f, 0.8f })
            {
                var f = GwFlag(fl, mats, bags, fi % 2 == 0 ? flagMeshC : flagMeshT, fi % 2 == 0 ? "flagCoral" : "flagTeal",
                               W(L, 10.5f, al), 6.0f, t);
                flags.Add(f); fi++;
            }
        AmbientMoverStaging.StageSway(parent, "Gateway Flag Sway", flags.ToArray(), Vector3.up, 16f, 0.7f,
                                      AmbientSway.SwayMode.Noise, default, 89);
        GwFlush(parent, "Portal", bags, mats);
        int tris = 0; foreach (var b in bags.Values) tris += b.t.Count / 3;
        return tris;
    }

    private static int BuildAvenueAndScreen(Transform group, Dictionary<string, Material> mats, float sg,
                                            List<Bounds> blockers, out int palms, out int lamps, out int beds)
    {
        palms = lamps = beds = 0;
        var avT = new GameObject("Entrance Boulevard").transform; avT.SetParent(group, false);
        var pineT = new GameObject("Black Pine Dune Screen").transform; pineT.SetParent(group, false);
        var bags = new Dictionary<string, GwBag>();
        // green cycle-lane strip on the sea-side verge (flush with the shoulder), with the pictogram every 20 m
        GwStrip(Gb(bags, "lane"), GwAvenueFromM, GwScreenToM + 20f, 4.25f, 5.45f, sg, 0.01f, 1.2f, 20f, Vector3.up);
        GwFlush(avT, "Cycle Lane", bags, mats);

        var rng = new System.Random(8901);
        for (float m = GwAvenueFromM; m < GwScreenToM + 20f; m += 16f)
        {
            if (Mathf.Abs(m - GwGateM) < 12f) continue;
            int i = _route.IndexAt(m); GwFrame(m, sg, out var t, out var s);
            var p = _route.Position[i];
            // royal palm avenue (verge)
            var pp = GwW(m, 5.9f, 0f, sg);
            if (GwOk(pp.x, pp.z, out float y) && GwFree(blockers, pp, 0.8f))
            {
                var go = PlaceWorld("Nagisa_NB9_RoyalPalm", avT, new Vector3(pp.x, y, pp.z), (float)rng.NextDouble() * 360f, 1.0f, 0.002f);
                if (go != null) palms++;
            }
            // premium street light between palms
            var lp = GwW(m + 8f, 6.9f, 0f, sg);
            if (GwOk(lp.x, lp.z, out y) && GwFree(blockers, lp, 0.7f))
            {
                var go = PlaceWorld("Nagisa_PromenadeLamp", avT, new Vector3(lp.x, y, lp.z), GwYaw(-s), 1f, 0.003f);
                if (go != null) lamps++;
            }
            // planter bed with flowering shrubs
            if (((int)((m - GwAvenueFromM) / 16f)) % 2 == 0)
            {
                var bp = GwW(m + 4f, 8.6f, 0f, sg);
                if (GwOk(bp.x, bp.z, out y) && GwFree(blockers, bp, 1.5f))
                {
                    PlaceWorld("Nagisa_S_Planter", avT, new Vector3(bp.x, y, bp.z), GwYaw(-s), 1f, 0.003f);
                    string[] fl = { "Nagisa_Hibiscus", "Nagisa_Bougainvillea", "Nagisa_NB9_Frangipani" };
                    PlaceWorld(fl[beds % 3], avT, new Vector3(bp.x, y, bp.z), (float)rng.NextDouble() * 360f, 1.0f, 0.002f);
                    beds++;
                }
            }
        }

        // black-pine dune grove: sea side, tight enough to hide the sea at road level; two deliberate view slots
        int pines = 0;
        float[] gapAt = { 235f, 335f };
        for (float m = 56f; m < GwScreenToM; m += 5f + (float)rng.NextDouble() * 2.5f)
        {
            bool framing = m < GwScreenFromM;
            bool inGap = false;
            foreach (float g in gapAt) if (Mathf.Abs(m - g) < 7f) inGap = true;
            if (inGap) continue;
            int rows = framing ? 1 : 2;
            for (int row = 0; row < rows; row++)
            {
                float lat = (framing ? 10.5f : 12.8f) + row * 5.5f + (float)rng.NextDouble() * 2.4f;
                var pp = GwW(m + row * 2.3f, lat, 0f, sg);
                if (!CanPlace(pp.x, pp.z, 1.5f, out float y, 3f)) continue;
                if (!GwFree(blockers, pp, 2.2f)) continue;
                GwPine(pineT, mats, new Vector3(pp.x, y - 0.1f, pp.z), (float)rng.NextDouble() * 360f,
                       0.85f + (float)rng.NextDouble() * 0.45f, rng.Next(3));
                pines++;
            }
            // low understory (fan-palm clusters) so the screen is solid below eye level too
            if (!framing && rng.NextDouble() < 0.5)
            {
                var up = GwW(m, 10.6f, 0f, sg);
                if (CanPlace(up.x, up.z, 1f, out float y2, 3f) && GwFree(blockers, up, 1.4f))
                    PlaceWorld("Nagisa_NB9_FanCluster", pineT, new Vector3(up.x, y2, up.z), (float)rng.NextDouble() * 360f, 1f, 0.002f);
            }
        }
        // two monumental trees frame the reveal at the end of the screen
        foreach (float m in new[] { GwScreenToM + 6f, GwScreenToM + 12f })
        {
            var pp = GwW(m, 11.5f, 0f, sg);
            if (CanPlace(pp.x, pp.z, 1.5f, out float y, 3f) && GwFree(blockers, pp, 2.2f))
            { GwPine(pineT, mats, new Vector3(pp.x, y - 0.1f, pp.z), 40f, 1.5f, 1); pines++; }
        }
        return pines;
    }
}

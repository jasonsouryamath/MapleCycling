// NAGISA BAY destination brief section 7 - NAGISA BAY MARINA (worker G, 2026-10-01).
//
// Turns the lone Marina Club on a bare lawn + five far-out pontoons into a premium resort marina with a small
// working fishing harbour at its south end:
//   * waterfront plaza + timber boardwalk + quay wall following the shoreline (route m 280..690)
//   * marina village on the plaza: boutique hotel, restaurants/cafes with terraces, shops, gelato, kiosks,
//     flagpoles (AmbientSway), palms, planters, lamps, bollards, benches, bike racks
//   * two extra piers from the quay to the existing pontoon head-walkway, parallel berths on every pier and
//     spine (runabouts, sloops, catamarans, motor yachts, superyachts), tenders, a mooring field; every moored
//     boat rocks (AmbientSway, one component per cluster, 1.5 km cull)
//   * rubble-mound breakwater arms closing the basin (entrance gap for slow in/out boats), red/green beacons
//   * boats slowly entering/leaving: AmbientPathMover closed loops through the entrance
//   * fishing harbour (route m ~735..860): concrete apron, two fish sheds, drying-net racks, crates, a boat
//     hoist, a concrete mole with working boats alongside, a rubble arm, beached dinghies, working boats on loops
// Nothing is placed inside CorridorKeepOutM (+margin), on the highway side, or on top of existing props:
// every footprint is tested against an occupancy map of everything already in the scene. Props of
// NB OpeningBeach (umbrellas / people) that sit on the new plaza are pruned (only that group).
// Stage "Marina2" (order 99) -> "Nagisa Bay Environment/NB Overhaul/NB Marina2".  Markers for WorldLife2 are
// created as "M2Spot_*" empties under "NB Marina2/Spots" so that stage can run on its own.
// Models: tools/blender/build_nagisa_marina_kit.py (Nagisa_MR_*). ALL numbers are PROVISIONAL art tuning.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- tunables (PROVISIONAL)
    private const float M2M0 = 150f, M2M1 = 960f, M2Step = 4f;
    private const float M2PlazaM0 = 292f, M2PlazaM1 = 655f;       // marina village along the route
    private const float M2PlazaV0 = 14.5f;                         // plaza starts this far seaward of the centreline
    private const float M2QuayBackM = 9f, M2QuayFrontM = 3f;       // boardwalk: vShore-9 .. vShore+3
    private const float M2PaveLift = 0.16f;
    private const float M2RoadMargin = 3.5f;
    private const float M2BoatCull = 0.0016f;
    private const float M2HarbM0 = 738f, M2HarbM1 = 858f;          // fishing harbour apron along the route

    // ---------------------------------------------------------------- frame along the route (shore profile)
    private sealed class M2Frame
    {
        public int n;
        public float[] m, vs;
        public Vector3[] p, sd, t;
    }

    private static M2Frame _m2f;

    private static M2Frame M2BuildFrame()
    {
        int n = Mathf.FloorToInt((M2M1 - M2M0) / M2Step) + 1;
        var f = new M2Frame { n = n, m = new float[n], vs = new float[n], p = new Vector3[n], sd = new Vector3[n], t = new Vector3[n] };
        for (int k = 0; k < n; k++)
        {
            int i = _route.IndexAt(M2M0 + k * M2Step);
            f.m[k] = _route.Distance[i];
            f.p[k] = _route.Position[i];
            var tt = _route.Tangent[i]; tt.y = 0f; f.t[k] = tt.normalized;
            f.sd[k] = _route.SideFlat(i) * SeaSign(i);
            float v = 80f;
            for (float o = 8f; o < 400f; o += 1f)
            {
                var q = f.p[k] + f.sd[k] * o;
                if (_ground.Coast(q.x, q.z) <= 0f) { v = o; break; }
            }
            f.vs[k] = v;
        }
        // smooth the 6 m terrain stair-steps out of the shoreline (+-6 samples = +-24 m)
        var sm = new float[n];
        for (int k = 0; k < n; k++)
        {
            float s = 0f; int c = 0;
            for (int j = -6; j <= 6; j++) { int q = Mathf.Clamp(k + j, 0, n - 1); s += f.vs[q]; c++; }
            sm[k] = s / c;
        }
        f.vs = sm;
        return f;
    }

    private static void M2Locate(float m, out int k, out float t)
    {
        float x = Mathf.Clamp((m - M2M0) / M2Step, 0f, _m2f.n - 1.001f);
        k = Mathf.FloorToInt(x); t = x - k;
    }

    /// <summary>World point at route metre m and v metres seaward of the centreline (y = ground height).</summary>
    private static Vector3 M2W(float m, float v)
    {
        M2Locate(m, out int k, out float t);
        var p = Vector3.Lerp(_m2f.p[k], _m2f.p[k + 1], t);
        var sd = Vector3.Lerp(_m2f.sd[k], _m2f.sd[k + 1], t).normalized;
        var w = p + sd * v;
        w.y = _ground.Height(w.x, w.z);
        return w;
    }

    private static float M2Vs(float m)
    {
        M2Locate(m, out int k, out float t);
        return Mathf.Lerp(_m2f.vs[k], _m2f.vs[k + 1], t);
    }

    private static Vector3 M2Sd(float m)
    {
        M2Locate(m, out int k, out float t);
        return Vector3.Lerp(_m2f.sd[k], _m2f.sd[k + 1], t).normalized;
    }

    private static Vector3 M2Tan(float m)
    {
        M2Locate(m, out int k, out float t);
        return Vector3.Lerp(_m2f.t[k], _m2f.t[k + 1], t).normalized;
    }

    /// <summary>Yaw that turns a model's local -z (building front) toward seaward direction sd.</summary>
    private static float M2FrontYaw(Vector3 sd) => Mathf.Atan2(-sd.x, -sd.z) * Mathf.Rad2Deg;

    /// <summary>Route metre and seaward offset of a world point (nearest centreline sample).</summary>
    private static void M2MV(Vector3 w, out float m, out float v)
    {
        _route.PlanDistance(w.x, w.z, out int idx);
        m = _route.Distance[idx];
        var sd = _route.SideFlat(idx) * SeaSign(idx);
        var d = w - _route.Position[idx];
        v = d.x * sd.x + d.z * sd.z;
    }

    // ---------------------------------------------------------------- occupancy of everything already staged
    private sealed class M2Occ
    {
        private const float C = 16f;
        private readonly Dictionary<long, List<Vector4>> _cells = new Dictionary<long, List<Vector4>>();
        private static long K(int x, int z) => ((long)x << 32) ^ (uint)z;
        public int Count;

        public void Add(float minx, float minz, float maxx, float maxz)
        {
            var r = new Vector4(minx, minz, maxx, maxz);
            for (int x = Mathf.FloorToInt(minx / C); x <= Mathf.FloorToInt(maxx / C); x++)
                for (int z = Mathf.FloorToInt(minz / C); z <= Mathf.FloorToInt(maxz / C); z++)
                {
                    if (!_cells.TryGetValue(K(x, z), out var l)) _cells[K(x, z)] = l = new List<Vector4>();
                    l.Add(r);
                }
            Count++;
        }

        public bool Hit(float minx, float minz, float maxx, float maxz) => HitSlow(minx, minz, maxx, maxz);

        private bool HitSlow(float minx, float minz, float maxx, float maxz)
        {
            for (int x = Mathf.FloorToInt(minx / C); x <= Mathf.FloorToInt(maxx / C); x++)
                for (int z = Mathf.FloorToInt(minz / C); z <= Mathf.FloorToInt(maxz / C); z++)
                    if (_cells.TryGetValue(K(x, z), out var l))
                        foreach (var r in l)
                            if (minx < r.z && maxx > r.x && minz < r.w && maxz > r.y) return true;
            return false;
        }
    }

    private static M2Occ M2SeedOcc(Transform root, Transform mine)
    {
        var occ = new M2Occ();
        // region of interest: a box around the marina + harbour stretch of the route
        float minx = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxz = float.MinValue;
        for (int k = 0; k < _m2f.n; k++)
        {
            minx = Mathf.Min(minx, _m2f.p[k].x); maxx = Mathf.Max(maxx, _m2f.p[k].x);
            minz = Mathf.Min(minz, _m2f.p[k].z); maxz = Mathf.Max(maxz, _m2f.p[k].z);
        }
        minx -= 330f; minz -= 330f; maxx += 330f; maxz += 330f;
        int seen = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.transform.IsChildOf(mine)) continue;
            var b = r.bounds;
            if (b.max.x < minx || b.min.x > maxx || b.max.z < minz || b.min.z > maxz) continue;
            if (b.size.x > 45f || b.size.z > 45f) continue;           // terrain / sand / road / sea ribbons
            if (b.size.y < 0.25f && (b.size.x > 8f || b.size.z > 8f)) continue;   // flat decals
            if (b.size.y < 1.1f && Mathf.Max(b.size.x, b.size.z) < 3f) continue;   // grass clumps / tufts / small litter
            occ.Add(b.min.x, b.min.z, b.max.x, b.max.z);
            seen++;
        }
        Debug.Log($"[g-marina2] occupancy: {seen} renderers indexed ({occ.Count} rects).");
        return occ;
    }

    /// <summary>Oriented footprint test: dry land, clear of the road corridor, flat enough, nothing already there.</summary>
    private static string _m2Why = "";

    private static bool M2FootOk(M2Occ occ, Vector3 c, float yaw, float hx, float hz, float margin, out float y, float maxRelief = 2.6f, float minCoast = 5f)
    {
        y = 0f; _m2Why = "";
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float lo = float.MaxValue, hi = float.MinValue;
        float minx = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxz = float.MinValue;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                var w = c + rot * new Vector3(i * (hx + margin), 0f, j * (hz + margin));
                if (_ground.Coast(w.x, w.z) < minCoast) { _m2Why = "water"; return false; }
                if (_route.PlanDistance(w.x, w.z, out _) < CorridorKeepOutM + M2RoadMargin) { _m2Why = "road"; return false; }
                float h = _ground.Height(w.x, w.z);
                lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                if (Mathf.Abs(i) == 1 && Mathf.Abs(j) == 1)
                {
                    minx = Mathf.Min(minx, w.x); maxx = Mathf.Max(maxx, w.x);
                    minz = Mathf.Min(minz, w.z); maxz = Mathf.Max(maxz, w.z);
                }
            }
        if (hi - lo > maxRelief) { _m2Why = "relief"; return false; }
        if (occ != null && occ.Hit(minx, minz, maxx, maxz)) { _m2Why = "occupied"; return false; }
        y = hi;
        return true;
    }

    private static Transform _m2Root;

    /// <summary>Debug: names of renderers whose bounds overlap an oriented footprint's AABB (first few).</summary>
    private static string M2Who(Vector3 c, float yaw, float hx, float hz, float margin)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float minx = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxz = float.MinValue;
        foreach (var (i, j) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var w = c + rot * new Vector3(i * (hx + margin), 0f, j * (hz + margin));
            minx = Mathf.Min(minx, w.x); maxx = Mathf.Max(maxx, w.x); minz = Mathf.Min(minz, w.z); maxz = Mathf.Max(maxz, w.z);
        }
        var names = new List<string>();
        if (_m2Root == null) return "";
        foreach (var r in _m2Root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r.transform.name.StartsWith("M2 ")) continue;
            var b = r.bounds;
            if (b.max.x < minx || b.min.x > maxx || b.max.z < minz || b.min.z > maxz) continue;
            if (b.size.x > 45f || b.size.z > 45f) continue;
            if (b.size.y < 1.1f && Mathf.Max(b.size.x, b.size.z) < 3f) continue;
            var t = r.transform; string path = t.name;
            for (int k = 0; k < 3 && t.parent != null; k++) { t = t.parent; path = t.name + "/" + path; }
            names.Add(path);
            if (names.Count >= 4) break;
        }
        return string.Join(" | ", names);
    }

    private static void M2Reserve(M2Occ occ, Vector3 c, float yaw, float hx, float hz)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        float minx = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxz = float.MinValue;
        foreach (var (i, j) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var w = c + rot * new Vector3(i * hx, 0f, j * hz);
            minx = Mathf.Min(minx, w.x); maxx = Mathf.Max(maxx, w.x);
            minz = Mathf.Min(minz, w.z); maxz = Mathf.Max(maxz, w.z);
        }
        occ.Add(minx, minz, maxx, maxz);
    }

    // ---------------------------------------------------------------- materials
    private static Dictionary<string, Material>[] _m2Hulls;
    private static Material _m2Flag0, _m2Flag1, _m2Flag2, _m2Beacon;

    private static void M2Materials()
    {
        var colours = new[]
        {
            new Color(0.95f, 0.95f, 0.93f), new Color(0.93f, 0.94f, 0.95f), new Color(0.08f, 0.15f, 0.31f),
            new Color(0.82f, 0.76f, 0.64f), new Color(0.12f, 0.50f, 0.52f), new Color(0.74f, 0.15f, 0.12f),
            new Color(0.07f, 0.07f, 0.08f), new Color(0.95f, 0.95f, 0.93f),
        };
        _m2Hulls = new Dictionary<string, Material>[colours.Length];
        for (int i = 0; i < colours.Length; i++)
            _m2Hulls[i] = new Dictionary<string, Material> { ["NB_MR_Hull"] = Cel("Nagisa_MR_Hull_" + i, colours[i], 0.7f, 0.5f, 0.2f, null, null, 0.5f) };
        _nb["NB_MR_Hull"] = _m2Hulls[0]["NB_MR_Hull"];
        _nb["NB_M2Paving"] = NbCel("NB_M2Paving", "NB_PlazaStone", new Color(0.78f, 0.74f, 0.68f), 0.12f, 0.06f, 0.06f, 0.9f);
        _nb["NB_MR_Sail"] = Cel("Nagisa_MR_Sail", new Color(0.96f, 0.95f, 0.90f), 0.08f, 0.04f, 0.12f, null, null, 0f, 0f);
        _nb["NB_MR_Net"] = Cel("Nagisa_MR_Net", new Color(0.22f, 0.30f, 0.26f), 0.03f, 0.02f, 0.05f, null, null, 0f, 0f);
        _m2Flag0 = Cel("Nagisa_M2_FlagNavy", Srgb(18, 54, 110), 0.1f, 0.05f, 0.1f, null, null, 0f, 0f);
        _m2Flag1 = Cel("Nagisa_M2_FlagWhite", Srgb(244, 244, 240), 0.1f, 0.05f, 0.1f, null, null, 0f, 0f);
        _m2Flag2 = Cel("Nagisa_M2_FlagCoral", Srgb(236, 98, 78), 0.1f, 0.05f, 0.1f, null, null, 0f, 0f);
        _m2Beacon = NbGlow("M2_Beacon", new Color(1f, 0.9f, 0.7f, 1f));
    }

    private static Dictionary<string, Material> M2Hull(System.Random rng)
    {
        double r = rng.NextDouble();
        int i = r < 0.50 ? 0 : r < 0.62 ? 1 : r < 0.74 ? 2 : r < 0.82 ? 3 : r < 0.89 ? 4 : r < 0.95 ? 5 : 6;
        return _m2Hulls[i];
    }

    private static Transform M2Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
    }

    /// <summary>Flush a merged mesh. Walkable surfaces (collider) get the " - Beach Sand walk layer" suffix on the object so
    /// MinatoCrowdActor.Ground() (priority 2) and Nb4Surface() let people stand on them instead of on the terrain below.</summary>
    private static GameObject M2Flush(HwBuf b, Transform parent, string name, Material mat, bool collider = false, bool shadow = true)
    {
        if (b.v.Count == 0) return null;
        var go = AddMesh(parent, name, Finish(name, b.v, b.uv, b.t), mat, collider);
        if (collider) go.name = name + " - Beach Sand walk layer";
        if (!shadow) go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // ---------------------------------------------------------------- marina frame in the pontoon axis (Q frame)
    private sealed class M2Q
    {
        public Vector3 q, dir, perp; public float best, shoreS;
        public Vector3 At(float u, float s) => q + dir * s + perp * u;
    }

    private static M2Q M2QFrame()
    {
        if (!_route.Pads.TryGetValue("marina_quay", out var quay)) return null;
        Vector3 dir = Vector3.zero; float best = float.MaxValue;
        for (int a = 0; a < 36; a++)
        {
            var d = Quaternion.Euler(0, a * 10f, 0) * Vector3.forward;
            for (float r = 20f; r < 400f; r += 10f)
            {
                var q = quay.c + d * r;
                if (_ground.Height(q.x, q.z) < -2.5f && _ground.Coast(q.x, q.z) < -15f) { if (r < best) { best = r; dir = d; } break; }
            }
        }
        if (dir == Vector3.zero) return null;
        var f = new M2Q { q = quay.c, dir = dir, perp = Vector3.Cross(Vector3.up, dir), best = best };
        f.shoreS = M2ShoreS(f, 0f);
        return f;
    }

    private static float M2ShoreS(M2Q f, float u)
    {
        for (float s = -40f; s < 400f; s += 1f)
        {
            var w = f.At(u, s);
            if (_ground.Coast(w.x, w.z) <= 0f) return s;
        }
        return 60f;
    }

    // ================================================================= stage
    [NagisaStage(99, "Marina2")]
    private static void BuildMarina2Stage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0 || _nbColourRemaps[0] == null) { PrepareNagisaTextures(); BuildNbMaterials(); }
        if (_nb.Count == 0) { Debug.LogError("[g-marina2] Run NagisaBayEnvironment.Apply first."); return; }
        if (!_nb.ContainsKey("NB9_CoralRock")) Nb9Materials();   // standalone stage run: Flora slots (coral, frangipani) for the shared GLBs
        M2Materials();
        _m2f = M2BuildFrame();
        var root = group.root;
        _m2Root = root;
        var rng = new System.Random(9901);

        int pruned = M2Prune(group);
        var occ = M2SeedOcc(root, group);

        var plazaT = M2Child(group, "Plaza and boardwalk");
        var villageT = M2Child(group, "Marina village");
        var propsT = M2Child(group, "Furniture");
        var pierT = M2Child(group, "Piers");
        var boatsT = M2Child(group, "Boats");
        var wallT = M2Child(group, "Breakwater");
        var harbT = M2Child(group, "Fishing harbour");
        var spotsT = M2Child(group, "Spots");

        M2Plaza(plazaT, occ);
        int bld = M2Village(villageT, spotsT, occ, rng);
        int furn = M2Furniture(propsT, spotsT, occ, rng);
        M2Signs(propsT, occ);
        var q = M2QFrame();
        int boats = 0, moored = 0;
        if (q != null)
        {
            var piers = M2Piers(pierT, q, spotsT);
            boats = M2Berths(boatsT, q, piers, rng, out moored);
            M2Breakwater(wallT, q, rng);
            M2MarinaMovers(boatsT, q, rng);
        }
        else Debug.LogWarning("[g-marina2] no marina quay frame: piers/boats/breakwater skipped.");
        string harb = M2Harbour(harbT, spotsT, occ, rng);
        M2Gulls(M2Child(group, "Gulls"), q);
        int coral = M2Coral(M2Child(group, "Coral shallows"), occ, rng);
        Debug.Log($"[g-marina2] done: pruned {pruned} beach props, {bld} village buildings, {furn} furniture pieces, " +
                  $"{boats} berthed + {moored} moored boats; harbour: {harb}.");
    }

    // ---------------------------------------------------------------- prune NB OpeningBeach props on the new plaza
    private static int M2Prune(Transform group)
    {
        int n = 0;
        foreach (var gname in new[] { "NB OpeningBeach/NB Open Beach Sets", "NB OpeningBeach/NB Open Beach People" })
        {
            var g = group.parent != null ? group.parent.Find(gname) : null;
            if (g == null) continue;
            for (int i = g.childCount - 1; i >= 0; i--)
            {
                var c = g.GetChild(i);
                if (c.name.Contains("Patches")) continue;
                M2MV(c.position, out float m, out float v);
                bool plaza = m >= M2PlazaM0 - 12f && m <= M2PlazaM1 + 12f && v >= 11f && v <= M2Vs(Mathf.Clamp(m, M2M0, M2M1 - 8f)) + 8f;
                bool harb = m >= M2HarbM0 - 10f && m <= M2HarbM1 + 10f && v >= 11f && v <= M2Vs(Mathf.Clamp(m, M2M0, M2M1 - 8f)) + 6f;
                if (!plaza && !harb) continue;
                Object.DestroyImmediate(c.gameObject);
                n++;
            }
        }
        return n;
    }

    // ---------------------------------------------------------------- plaza, boardwalk, quay wall, harbour apron
    private static void M2Plaza(Transform parent, M2Occ occ)
    {
        var plaza = new HwBuf(); var deck = new HwBuf(); var wall = new HwBuf(); var rail = new HwBuf(); var kerb = new HwBuf();
        for (float m = M2PlazaM0; m < M2PlazaM1 - 0.01f; m += M2Step)
        {
            float m2 = Mathf.Min(m + M2Step, M2PlazaM1);
            float vsA = M2Vs(m), vsB = M2Vs(m2);
            // plaza paving: v0 .. vs-9 in ~6 m columns
            int cols = Mathf.Max(2, Mathf.CeilToInt((Mathf.Min(vsA, vsB) - M2QuayBackM - M2PlazaV0) / 6f));
            for (int c = 0; c < cols; c++)
            {
                float fa = c / (float)cols, fb = (c + 1) / (float)cols;
                float v0a = Mathf.Lerp(M2PlazaV0, vsA - M2QuayBackM, fa), v1a = Mathf.Lerp(M2PlazaV0, vsA - M2QuayBackM, fb);
                float v0b = Mathf.Lerp(M2PlazaV0, vsB - M2QuayBackM, fa), v1b = Mathf.Lerp(M2PlazaV0, vsB - M2QuayBackM, fb);
                var a = M2Lift(M2W(m, v0a)); var b = M2Lift(M2W(m, v1a));
                var cc = M2Lift(M2W(m2, v1b)); var d = M2Lift(M2W(m2, v0b));
                plaza.Quad(a, b, cc, d, Vector3.up, m / 4f, m2 / 4f, v0a / 4f, v1a / 4f);
            }
            // boardwalk deck: flat at the landward edge's level, rising with the ground where it is higher; wall face to the water
            float yEndA = M2DeckY(m);
            float yEndB = M2DeckY(m2);
            const int dc = 3;
            for (int c = 0; c < dc; c++)
            {
                float fa = c / (float)dc, fb = (c + 1) / (float)dc;
                float v0a = Mathf.Lerp(vsA - M2QuayBackM, vsA + M2QuayFrontM, fa), v1a = Mathf.Lerp(vsA - M2QuayBackM, vsA + M2QuayFrontM, fb);
                float v0b = Mathf.Lerp(vsB - M2QuayBackM, vsB + M2QuayFrontM, fa), v1b = Mathf.Lerp(vsB - M2QuayBackM, vsB + M2QuayFrontM, fb);
                Vector3 P(float mm, float vv, float yE)
                {
                    var w = M2W(mm, vv);
                    w.y = Mathf.Max(w.y + M2PaveLift + 0.02f, yE);
                    return w;
                }
                deck.Quad(P(m, v0a, yEndA), P(m, v1a, yEndA), P(m2, v1b, yEndB), P(m2, v0b, yEndB), Vector3.up,
                          m / 3f, m2 / 3f, v0a / 3f, v1a / 3f);
            }
            // quay face (outward = seaward), top at the deck, down to -1.2
            {
                var sdA = M2Sd(m);
                var tA = M2W(m, vsA + M2QuayFrontM); var tB = M2W(m2, vsB + M2QuayFrontM);
                var a = new Vector3(tA.x, yEndA, tA.z); var b = new Vector3(tB.x, yEndB, tB.z);
                var a2 = new Vector3(tA.x, -1.2f, tA.z); var b2 = new Vector3(tB.x, -1.2f, tB.z);
                wall.Quad(a2, a, b, b2, sdA, m / 3f, m2 / 3f, 0f, (yEndA + 1.2f) / 3f);
                // kerb / coping stone along the top edge
                var ctr = (a + b) * 0.5f;
                kerb.Box(ctr + new Vector3(0f, 0.04f, 0f) - sdA * 0.1f, Vector3.Cross(Vector3.up, M2Tan(m)), M2Tan(m), 0.5f, 0.12f, M2Step + 0.05f);
            }
        }
        // handrail along the front edge (gaps at the pier roots)
        var pierRoots = M2PierRootMs();
        for (float m = M2PlazaM0 + 2f; m < M2PlazaM1 - 2f; m += 3f)
        {
            bool gap = false;
            foreach (float pr in pierRoots) if (Mathf.Abs(m - pr) < 3.6f) gap = true;
            if (gap) continue;
            float vs = M2Vs(m) + M2QuayFrontM - 0.25f;
            var w = M2W(m, vs);
            float yE = M2DeckY(m);
            var tan = M2Tan(m); var side = Vector3.Cross(Vector3.up, tan);
            rail.Box(new Vector3(w.x, yE + 0.5f, w.z), side, tan, 0.07f, 1.0f, 0.07f);
            rail.Box(new Vector3(w.x, yE + 1.0f, w.z) + tan * 1.5f, side, tan, 0.06f, 0.06f, 3.0f);
            rail.Box(new Vector3(w.x, yE + 0.55f, w.z) + tan * 1.5f, side, tan, 0.03f, 0.03f, 3.0f);
        }
        M2Flush(plaza, parent, "Nagisa_M2_PlazaPaving", _nb["NB_M2Paving"], true, false);
        M2Flush(deck, parent, "Nagisa_M2_Boardwalk", _nb.TryGetValue("NB_Boardwalk", out var bw) && bw != null ? bw : _nb["NB_PontoonWood"], true);
        M2Flush(wall, parent, "Nagisa_M2_QuayWall", _nb["NB_Stone"], false);
        M2Flush(kerb, parent, "Nagisa_M2_QuayCoping", _nb.TryGetValue("NB_Kerb", out var kb) && kb != null ? kb : _nb["NB_Concrete"], false, false);
        M2Flush(rail, parent, "Nagisa_M2_Handrail", _nb["NB_Metal"], false, false);
    }

    /// <summary>Boardwalk deck level at route metre m: the plaza level at the quay's landward edge, never below 0.6 m (timber deck on piles).</summary>
    private static float M2DeckY(float m)
    {
        m = Mathf.Clamp(m, M2M0, M2M1 - 8f);
        var back = M2W(m, M2Vs(m) - M2QuayBackM);
        return Mathf.Max(0.6f, back.y + M2PaveLift);
    }

    private static Vector3 M2Lift(Vector3 w) { w.y += M2PaveLift; return w; }

    /// <summary>Pier axis distance s where the pier meets the boardwalk's seaward edge, and the boardwalk deck level there.</summary>
    private static float M2QuayFrontS(M2Q q, float u, out float yDeck)
    {
        float s = q.shoreS - 6f;
        for (; s < q.shoreS + 14f; s += 0.5f)
        {
            var w = q.At(u, s);
            M2MV(w, out float m, out float v);
            if (v >= M2Vs(Mathf.Clamp(m, M2M0, M2M1 - 8f)) + M2QuayFrontM - 0.3f) break;
        }
        var wq = q.At(u, s);
        M2MV(wq, out float mm, out float vv);
        mm = Mathf.Clamp(mm, M2M0, M2M1 - 8f);
        yDeck = M2DeckY(mm);
        return s;
    }

    private static List<float> M2PierRootMs()
    {
        var l = new List<float>();
        var q = M2QFrame();
        if (q == null) return l;
        foreach (float u in new[] { -34f, 0f, 34f })
        {
            var w = q.At(u, q.shoreS - 1f);
            M2MV(w, out float m, out _);
            l.Add(m);
        }
        return l;
    }

    // ---------------------------------------------------------------- the marina village
    private struct M2Lot { public string stem; public float m, vFront; public bool seaFront; public int colour; public float scale; }

    private static int M2Village(Transform parent, Transform spots, M2Occ occ, System.Random rng)
    {
        // (stem, route metre of the lot centre, v of the seaward FACE, remap colourway)
        var lots = new List<M2Lot>
        {
            new M2Lot { stem = "Nagisa_B_Boutique1", m = 352f, vFront = 40f, colour = 2 },        // marina boutique hotel
            new M2Lot { stem = "Nagisa_B_Boutique2", m = 392f, vFront = 38f, colour = 4 },
            new M2Lot { stem = "Nagisa_B_ShopB_5", m = 420f, vFront = 33f, colour = 6 },
            new M2Lot { stem = "Nagisa_B_ShopA_7", m = 431f, vFront = 33f, colour = 1 },
            new M2Lot { stem = "Nagisa_B_ShopC_10", m = 442f, vFront = 33f, colour = 5 },
            new M2Lot { stem = "Nagisa_B_BeachBar", m = 405f, vFront = -26f, colour = 3, seaFront = true },    // vFront relative to shore
            new M2Lot { stem = "Nagisa_B_BeachBar", m = 520f, vFront = -26f, colour = 7, seaFront = true },
            new M2Lot { stem = "Nagisa_B_Gelato", m = 563f, vFront = -24f, colour = 2, seaFront = true },
            new M2Lot { stem = "Nagisa_B_ShaveIce", m = 585f, vFront = -23f, colour = 4, seaFront = true },
            new M2Lot { stem = "Nagisa_B_ShopA_4", m = 505f, vFront = 33f, colour = 3 },
            new M2Lot { stem = "Nagisa_B_ShopB_6", m = 516f, vFront = 33f, colour = 0 },
            new M2Lot { stem = "Nagisa_B_ShopC_11", m = 527f, vFront = 33f, colour = 7 },
            new M2Lot { stem = "Nagisa_B_Konbini", m = 557f, vFront = 42f, colour = 1 },
            new M2Lot { stem = "Nagisa_B_Boutique1", m = 612f, vFront = 38f, colour = 6 },        // yacht-club residence
            new M2Lot { stem = "Nagisa_B_Boutique2", m = 640f, vFront = 36f, colour = 0 },
        };
        int placed = 0, rejected = 0;
        foreach (var lot in lots)
        {
            var f = FootOf(lot.stem);
            float vs = M2Vs(lot.m);
            float vFront = lot.seaFront ? vs + lot.vFront : lot.vFront;
            var sd = M2Sd(lot.m);
            float yaw = M2FrontYaw(sd);
            var c = M2W(lot.m, vFront - f.hz);
            bool ok = false; float y = 0f;
            // nudge along the route until the lot is free (+- 18 m)
            for (int k = 0; k < 9 && !ok; k++)
            {
                float dm = ((k + 1) / 2) * 4.5f * (k % 2 == 0 ? 1f : -1f);
                c = M2W(lot.m + dm, vFront - f.hz);
                ok = M2FootOk(occ, c, M2FrontYaw(M2Sd(lot.m + dm)), f.hx, f.hz, 1.5f, out y, 2.6f, 6f);
                if (ok) { yaw = M2FrontYaw(M2Sd(lot.m + dm)); }
            }
            if (!ok)
            {
                rejected++;
                Debug.Log($"[g-marina2] lot {lot.stem} m{lot.m:0} rejected: {_m2Why} (hx {f.hx:0.0} hz {f.hz:0.0}) " + (_m2Why == "occupied" ? "by " + M2Who(c, yaw, f.hx, f.hz, 1.5f) : ""));
                continue;
            }
            var go = PlaceWorld(lot.stem, parent, new Vector3(c.x, y - 0.05f, c.z), yaw, 1f, 0.0025f, NbColourway(lot.colour), hero: f.h > 12f);
            if (go == null) continue;
            go.name = $"M2 {lot.stem.Replace("Nagisa_B_", "")} m{lot.m:0}";
            M2Reserve(occ, c, yaw, f.hx, f.hz);
            placed++;
            // a terrace + seating spots for the waterfront food places
            if (lot.stem.Contains("BeachBar") || lot.stem.Contains("Gelato") || lot.stem.Contains("ShaveIce"))
            {
                var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;      // local -z
                var tc = c + front * (f.hz + 3.4f);
                if (lot.stem.Contains("BeachBar") && M2FootOk(occ, tc, yaw, 5f, 3f, 0f, out float ty, 3f, 1f))
                {
                    var tr = PlaceWorld("Nagisa_MR_CafeTerrace", parent, new Vector3(tc.x, ty - 0.05f, tc.z), yaw, 1f, 0.003f);
                    if (tr != null)
                    {
                        tr.name = "M2 Cafe Terrace m" + lot.m.ToString("0"); M2Reserve(occ, tc, yaw, 5f, 3f);
                        M2WalkSlab(tr.transform, 5f, 3f, 0.395f);
                    }
                    for (int s = 0; s < 4; s++)
                    {
                        var sp = tc + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-3.4f + s * 2.3f, 0.4f, (s % 2 == 0 ? 0.2f : -0.5f) + 0.9f);
                        M2Spot(spots, "Sit", sp, front);
                    }
                }
                else if (!lot.stem.Contains("BeachBar"))
                    M2Spot(spots, "Queue", c + front * (f.hz + 1.4f), front);   // gelato / shave-ice queue
                M2Spot(spots, "Stand", c + front * (f.hz + 2.4f), front);
            }
        }
        // flagpoles in front of the village (AmbientSway pennants) and sign poles
        Debug.Log($"[g-marina2] village: {placed}/{lots.Count} lots placed, {rejected} rejected (occupied / road / relief).");
        return placed;
    }

    private static Mesh _m2Slab;

    /// <summary>Invisible-under-the-deck walk surface: a slab (top 'top' m above the parent origin) with the walk-layer name.</summary>
    private static void M2WalkSlab(Transform parent, float hx, float hz, float top)
    {
        if (_m2Slab == null)
        {
            var b = new HwBuf();
            b.Box(new Vector3(0f, top - 0.2f, 0f), Vector3.right, Vector3.forward, 2f * hx, 0.4f, 2f * hz);
            _m2Slab = Finish("Nagisa_M2_TerraceSlab", b.v, b.uv, b.t);
            string path = $"{MeshDir}/Nagisa_M2_TerraceSlab.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(_m2Slab, path);
        }
        var go = new GameObject("M2 Terrace Slab - Beach Sand walk layer", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = _m2Slab;
        go.GetComponent<MeshCollider>().sharedMesh = _m2Slab;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = _nb["NB_Teak"]; r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void M2Spot(Transform spots, string kind, Vector3 p, Vector3 face)
    {
        var t = new GameObject("M2Spot_" + kind).transform;
        t.SetParent(spots, false);
        face.y = 0f;
        t.SetPositionAndRotation(p, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face.normalized, Vector3.up) : Quaternion.identity);
    }

    // ---------------------------------------------------------------- furniture along the plaza
    private static int M2Furniture(Transform parent, Transform spots, M2Occ occ, System.Random rng)
    {
        int n = 0;
        var palms = new[] { "Nagisa_CoconutPalm_A", "Nagisa_CoconutPalm_B", "Nagisa_FanPalm" };
        // road-side planting rhythm: palms + planters just outside the corridor, low enough to keep the sea in view
        for (float m = M2PlazaM0 + 6f; m < M2PlazaM1 - 6f; m += 15f)
        {
            var sd = M2Sd(m);
            float yaw = Mathf.Atan2(M2Tan(m).x, M2Tan(m).z) * Mathf.Rad2Deg;
            var w = M2W(m, 16.5f);
            if (CanPlace(w.x, w.z, 1.2f, out float y, 2f) && occ.Count >= 0 && !M2Taken(occ, w, 1.8f))
            {
                if (PlaceWorld(palms[rng.Next(palms.Length)], parent, new Vector3(w.x, y + M2PaveLift - 0.05f, w.z), (float)rng.NextDouble() * 360f, 0.85f + (float)rng.NextDouble() * 0.3f, 0.004f) != null) n++;
            }
            var p2 = M2W(m + 7.5f, 14.8f);
            if (CanPlace(p2.x, p2.z, 1f, out float y2, 2f) && !M2Taken(occ, p2, 1.5f))
            {
                if (PlaceWorld("Nagisa_S_Planter", parent, new Vector3(p2.x, y2 + M2PaveLift - 0.05f, p2.z), yaw, 1f, 0.004f) != null) n++;
            }
        }
        // waterfront: lamps + bollards + benches along the boardwalk, bike racks near the village
        var pierRoots = M2PierRootMs();
        for (float m = M2PlazaM0 + 10f; m < M2PlazaM1 - 10f; m += 20f)
        {
            bool nearPier = false; foreach (float pr in pierRoots) if (Mathf.Abs(m - pr) < 5f) nearPier = true;
            if (nearPier) continue;
            float vs = M2Vs(m);
            var sd = M2Sd(m); var tan = M2Tan(m);
            float yE = M2DeckY(m);
            var wl = M2W(m, vs - 2.2f); wl.y = yE;
            if (!M2Taken(occ, wl, 0.8f) && PlaceWorld("Nagisa_PromenadeLamp", parent, wl, M2FrontYaw(sd), 1f, 0.004f) != null) n++;
            var wb = M2W(m + 10f, vs + 1.8f); wb.y = yE;
            if (PlaceWorld("Nagisa_S_Bollard", parent, wb, 0f, 1f, 0.004f) != null) n++;
            if (((int)(m / 20f)) % 2 == 0)
            {
                var wbn = M2W(m + 6f, vs - 5.2f); wbn.y = yE;
                if (!M2Taken(occ, wbn, 1.4f) && PlaceWorld("Nagisa_Bench", parent, wbn, M2FrontYaw(sd), 1f, 0.004f) != null)
                {
                    n++;
                    M2Spot(spots, "Sit", wbn, sd);
                }
            }
        }
        for (float m = 360f; m < 640f; m += 70f)
        {
            var w = M2W(m, 21f);
            if (!CanPlace(w.x, w.z, 1.5f, out float y, 2f) || M2Taken(occ, w, 2.2f)) continue;
            var tan = M2Tan(m);
            for (int k = 0; k < 3; k++)
            {
                var rp = w + tan * (k * 1.7f);
                if (PlaceWorld("Nagisa_S_BikeRack", parent, new Vector3(rp.x, y + M2PaveLift - 0.05f, rp.z), Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg + 90f, 1f, 0.004f) != null) n++;
            }
            M2Spot(spots, "Stand", w + M2Sd(m) * 2f + tan * 1.7f, M2Sd(m));
        }
        // flagpoles with pennants along the quay (one per ~60 m)
        int flags = 0;
        for (float m = M2PlazaM0 + 24f; m < M2PlazaM1 - 12f; m += 58f)
        {
            float vs = M2Vs(m);
            float yE = M2DeckY(m);
            var w = M2W(m, vs - 3.6f); w.y = yE;
            if (M2Taken(occ, w, 1f)) continue;
            M2Flagpole(parent, w, flags++);
        }
        Debug.Log($"[g-marina2] furniture: {n} props + {flags} flagpoles.");
        return n + flags;
    }

    // ---------------------------------------------------------------- NB9 coral outcrops in the shallows / rocky waterline
    // The flora scan only starts beyond the 100 m sea reserve and ChatGPT's dedicated scan looked 2.5-11 m from the road
    // centre, while the shore is 50-90 m out: neither ever reached the water. This scan walks seaward from the route to the
    // real shoreline (Coast <= 0) and puts the outcrops in the first 30 m of shallows (sea bed -1.8 .. -0.15 m), where the
    // turquoise water shows them, plus a few on the wet rocky edge. Marina and harbour frontage are left alone.
    private static int M2Coral(Transform parent, M2Occ occ, System.Random rng)
    {
        const string stem = "Nagisa_NB9_CoralOutcrop";
        if (!GlbExists(stem)) return 0;
        int placed = 0, tried = 0;
        foreach (var (m0, m1) in new[] { (60f, 4650f), (12230f, 14900f) })
        {
            for (float m = m0; m < m1 && placed < 150; m += 38f + (float)rng.NextDouble() * 34f)
            {
                if ((m > 270f && m < 700f) || (m > 725f && m < 880f)) continue;
                int i = _route.IndexAt(m);
                if (_route.OnBridge(i)) continue;
                var p = _route.Position[i];
                var sd = _route.SideFlat(i) * SeaSign(i);
                var tn = _route.Tangent[i]; tn.y = 0f; tn.Normalize();
                float vs = -1f;
                for (float o = 8f; o < 500f; o += 1f)
                {
                    var q = p + sd * o;
                    if (_ground.Coast(q.x, q.z) <= 0f) { vs = o; break; }
                }
                if (vs < 0f) continue;
                int n = 1 + rng.Next(2);
                for (int k = 0; k < n; k++)
                {
                    tried++;
                    float v = vs + 2f + (float)rng.NextDouble() * 28f;
                    var w = p + sd * v + tn * ((float)(rng.NextDouble() - 0.5) * 16f);
                    float h = _ground.Height(w.x, w.z);
                    if (h > -0.15f || h < -1.8f) continue;
                    if (_route.PlanDistance(w.x, w.z, out _) < 40f) continue;
                    if (M2Taken(occ, w, 2.5f)) continue;
                    var go = PlaceWorld(stem, parent, new Vector3(w.x, h - 0.1f, w.z), (float)rng.NextDouble() * 360f, 0.9f + (float)rng.NextDouble() * 1.1f, 0.004f);
                    if (go != null) placed++;
                }
            }
        }
        Debug.Log($"[g-marina2] NB9 coral fix: {placed} outcrops in the shallows ({tried} candidates).");
        return placed;
    }

    // ---------------------------------------------------------------- gulls (one AmbientFlock per cluster, 1.5 km cull)
    private static void M2Gulls(Transform parent, M2Q q)
    {
        if (!GlbExists("Nagisa_MR_Gull")) return;
        var centres = new List<(string name, Vector3 c, float radius)>();
        if (q != null) centres.Add(("Marina basin gulls", q.At(0f, q.best + 40f) + Vector3.up * 16f, 34f));
        var v1 = M2W(470f, M2Vs(470f) - 4f); centres.Add(("Boardwalk gulls", v1 + Vector3.up * 11f, 24f));
        var v2 = M2W(796f, M2Vs(796f) + 40f); centres.Add(("Harbour gulls", v2 + Vector3.up * 14f, 30f));
        int k = 0, total = 0;
        foreach (var (name, c, radius) in centres)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false); root.position = c;
            int count = 6;
            var members = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var go = PlaceWorld("Nagisa_MR_Gull", root, c + new Vector3(Mathf.Cos(i) * radius, i * 0.7f, Mathf.Sin(i) * radius), i * 55f, 1.7f, 0.002f);
                if (go == null) continue;
                go.name = "Gull_" + i; members[i] = go.transform; total++;
            }
            var flock = root.gameObject.AddComponent<AmbientFlock>();
            flock.members = members;
            flock.mode = AmbientFlock.FlockMode.Circling;
            flock.radius = radius; flock.radiusVariance = radius * 0.35f; flock.heightVariance = 4f;
            flock.speed = 6.5f + k; flock.flapFrequency = 2.2f; flock.flapBobHeave = 0.12f; flock.flapBobTilt = 4f;
            flock.cullDistance = 1500f; flock.seed = 4400 + k * 11;
            flock.Bake();
            EditorUtility.SetDirty(flock);
            k++;
        }
        Debug.Log($"[g-marina2] gulls: {total} birds in {k} circling flocks.");
    }

    // ---------------------------------------------------------------- signage (tools/textures/make_nagisa_marina_signs.py)
    private static void M2Signs(Transform parent, M2Occ occ)
    {
        int n = 0;
        var roots = M2PierRootMs();
        var list = new List<(string name, float m, float v, float w, float h, float swing)>
        {
            ("Marina", 306f, 20f, 6.4f, 1.6f, 38f),
            ("Harbour", 736f, 20f, 6.0f, 1.5f, 38f),
        };
        if (roots.Count > 1) list.Add(("Cruise", roots[1] - 16f, M2Vs(roots[1]) - 13f, 4.2f, 1.05f, 62f));
        foreach (var (name, m, v, w, h, swing) in list)
        {
            var tex = Tex(NagisaTex + "/Marina", $"M2_Sign_{name}.png");
            if (tex == null) continue;
            var p0 = M2W(m, v);
            if (M2Taken(occ, p0, 1.2f)) { Debug.Log($"[g-marina2] sign {name}: spot taken."); continue; }
            var sd = M2Sd(m); var tan = M2Tan(m);
            float a = swing * Mathf.Deg2Rad;
            var nrm = (-sd * Mathf.Cos(a) - tan * Mathf.Sin(a)).normalized;
            var r = Vector3.Cross(nrm, Vector3.up);
            float bottom = 1.3f, top = bottom + h;
            var b = new HwBuf();
            var c = p0;
            b.Quad(c - r * (w * 0.5f) + Vector3.up * bottom, c - r * (w * 0.5f) + Vector3.up * top,
                   c + r * (w * 0.5f) + Vector3.up * top, c + r * (w * 0.5f) + Vector3.up * bottom, nrm, 0f, 1f, 0f, 1f);
            var mat = Cel("Nagisa_M2_Sign_" + name, Color.white, 0.2f, 0.08f, 0.04f, tex, null, 0f, 2f);
            var holder = new GameObject("M2 Sign " + name).transform;
            holder.SetParent(parent, false);
            var face = AddMesh(holder, "Nagisa_M2_SignFace_" + name, Finish("Nagisa_M2_SignFace_" + name, b.v, b.uv, b.t), mat, false);
            face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var fr = new HwBuf();
            Vector3 flat = new Vector3(nrm.x, 0f, nrm.z).normalized;
            fr.Box(c + Vector3.up * ((bottom + top) * 0.5f) - nrm * 0.07f, r, nrm, w + 0.25f, h + 0.25f, 0.1f);
            foreach (float sg in new[] { -1f, 1f })
                fr.Box(c + r * (sg * (w * 0.5f - 0.35f)) + Vector3.up * ((top + 0.1f) * 0.5f) - nrm * 0.12f, r, nrm, 0.16f, top + 0.1f, 0.16f);
            M2Flush(fr, holder, "Nagisa_M2_SignFrame_" + name, _nb["NB_Metal"], false);
            occ.Add(p0.x - 3.2f, p0.z - 3.2f, p0.x + 3.2f, p0.z + 3.2f);
            n++;
        }
        Debug.Log($"[g-marina2] signage: {n} boards.");
    }

    private static bool M2Taken(M2Occ occ, Vector3 p, float r) => occ.Hit(p.x - r, p.z - r, p.x + r, p.z + r);

    private static void M2Flagpole(Transform parent, Vector3 foot, int k)
    {
        var holder = new GameObject("M2 Flagpole " + k).transform;
        holder.SetParent(parent, false);
        holder.position = foot;
        var pole = new HwBuf();
        pole.Box(new Vector3(0f, 4.4f, 0f), Vector3.right, Vector3.forward, 0.14f, 8.8f, 0.14f);
        pole.Box(new Vector3(0f, 8.9f, 0f), Vector3.right, Vector3.forward, 0.3f, 0.3f, 0.3f);
        string pn = "Nagisa_M2_FlagPole";
        var pm = Finish(pn, pole.v, pole.uv, pole.t);
        if (AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/{pn}.asset") == null) { if (_meshNames.Add(pn)) AssetDatabase.CreateAsset(pm, $"{MeshDir}/{pn}.asset"); }
        else pm = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/{pn}.asset");
        var pgo = new GameObject("Pole", typeof(MeshFilter), typeof(MeshRenderer));
        pgo.transform.SetParent(holder, false);
        pgo.GetComponent<MeshFilter>().sharedMesh = pm;
        var pr = pgo.GetComponent<MeshRenderer>();
        pr.sharedMaterial = _nb["NB_Metal"]; pr.shadowCastingMode = ShadowCastingMode.Off;
        // pennant: a double-sided quad pivoting at the pole, flutter via AmbientSway
        var pivot = new GameObject("Flag pivot").transform;
        pivot.SetParent(holder, false);
        pivot.localPosition = new Vector3(0f, 8.1f, 0f);
        pivot.localRotation = Quaternion.Euler(0f, k * 67f, 0f);
        var fb = new HwBuf();
        fb.Quad(new Vector3(0.07f, 0f, 0f), new Vector3(0.07f, 1.1f, 0f), new Vector3(1.9f, 0.95f, 0f), new Vector3(1.9f, 0.15f, 0f), Vector3.back, 0f, 1f, 0f, 1f);
        fb.Quad(new Vector3(0.07f, 0f, 0f), new Vector3(0.07f, 1.1f, 0f), new Vector3(1.9f, 0.95f, 0f), new Vector3(1.9f, 0.15f, 0f), Vector3.forward, 0f, 1f, 0f, 1f);
        string fn = "Nagisa_M2_Pennant";
        var fm = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/{fn}.asset");
        if (fm == null) { fm = Finish(fn, fb.v, fb.uv, fb.t); if (_meshNames.Add(fn)) AssetDatabase.CreateAsset(fm, $"{MeshDir}/{fn}.asset"); }
        var fgo = new GameObject("Flag", typeof(MeshFilter), typeof(MeshRenderer));
        fgo.transform.SetParent(pivot, false);
        fgo.GetComponent<MeshFilter>().sharedMesh = fm;
        var fr = fgo.GetComponent<MeshRenderer>();
        fr.sharedMaterial = (k % 3) == 0 ? _m2Flag0 : (k % 3) == 1 ? _m2Flag1 : _m2Flag2;
        fr.shadowCastingMode = ShadowCastingMode.Off;
        var sway = fgo.AddComponent<AmbientSway>();
        sway.mode = AmbientSway.SwayMode.Noise;
        sway.axis = Vector3.up; sway.secondaryAxis = Vector3.forward;
        sway.maxAngle = 16f; sway.secondaryAngle = 5f; sway.frequency = 0.55f;
        sway.cullDistance = 700f; sway.seed = 7100 + k;
        sway.Bake();
        EditorUtility.SetDirty(sway);
    }

    // ---------------------------------------------------------------- piers (quay -> existing pontoon head walkway)
    private sealed class M2PierInfo { public float u; public float s0, s1; public Vector3 a, b; }

    private static List<M2PierInfo> M2Piers(Transform parent, M2Q q, Transform spots)
    {
        var list = new List<M2PierInfo>();
        var deck = new HwBuf(); var piles = new HwBuf(); var kerb = new HwBuf();
        const float w = 3.2f, thick = 0.28f, deckTop = 0.55f;
        float s1 = q.best + 2.5f;                 // the existing head walkway
        foreach (float u in new[] { -34f, 34f })
        {
            float s0 = M2QuayFrontS(q, u, out float yStart);
            var a0 = q.At(u, s0); var b0 = q.At(u, s1);
            // piers must stay clear of the road (they never get near it: ~50 m) - sanity only
            float prev = 0f;
            int steps = Mathf.CeilToInt((s1 - s0) / 4f);
            // slope from the quay level to the pontoon level over the first ~60 % of the length, flat after
            for (int k = 0; k < steps; k++)
            {
                float sa = s0 + (s1 - s0) * k / steps, sb = s0 + (s1 - s0) * (k + 1) / steps;
                float ta = Mathf.Clamp01((sa - s0) / ((s1 - s0) * 0.6f)), tb = Mathf.Clamp01((sb - s0) / ((s1 - s0) * 0.6f));
                float ya = Mathf.Lerp(yStart, deckTop, ta), yb = Mathf.Lerp(yStart, deckTop, tb);
                var pa = q.At(u, sa); var pb = q.At(u, sb);
                var l0 = pa - q.perp * (w * 0.5f); var r0 = pa + q.perp * (w * 0.5f);
                var l1 = pb - q.perp * (w * 0.5f); var r1 = pb + q.perp * (w * 0.5f);
                l0.y = ya; r0.y = ya; l1.y = yb; r1.y = yb;
                deck.Quad(l0, r0, r1, l1, Vector3.up, sa / 3f, sb / 3f, 0f, 1f);
                // fascia sides (thickness)
                var d = Vector3.down * thick;
                deck.Quad(l0 + d, l0, l1, l1 + d, -q.perp, sa / 3f, sb / 3f, 0f, 0.2f);
                deck.Quad(r0, r0 + d, r1 + d, r1, q.perp, sa / 3f, sb / 3f, 0f, 0.2f);
                prev = ya;
                // piles every 6 m (only over water)
                if (k % 2 == 1 && _ground.Height(pa.x, pa.z) < ya - 0.9f)
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        var pp = pa + q.perp * (sg * (w * 0.5f - 0.2f));
                        piles.Box(new Vector3(pp.x, (ya - thick - 3f) * 0.5f, pp.z), q.perp, q.dir, 0.28f, ya - thick + 3f, 0.28f);
                    }
                // low kerb rails
                foreach (float sg in new[] { -1f, 1f })
                {
                    var rc = (pa + pb) * 0.5f + q.perp * (sg * (w * 0.5f - 0.08f)); rc.y = (ya + yb) * 0.5f + 0.1f;
                    kerb.Box(rc, q.perp, q.dir, 0.14f, 0.16f, 4.1f);
                }
            }
            list.Add(new M2PierInfo { u = u, s0 = s0, s1 = s1, a = a0, b = b0 });
            M2Spot(spots, "PierEnd", q.At(u, s1 - 6f), q.dir);
            M2Spot(spots, "PierMid", q.At(u, (s0 + s1) * 0.5f), q.perp);
        }
        M2Flush(deck, parent, "Nagisa_M2_PierDecks", _nb["NB_PontoonWood"], true);
        M2Flush(piles, parent, "Nagisa_M2_PierPiles", _nb["NB_Concrete"], false, false);
        M2Flush(kerb, parent, "Nagisa_M2_PierKerbs", _nb["NB_PontoonWood"], false, false);
        // the existing main pier (u = 0) and the pontoon spines are also berthing lines
        list.Add(new M2PierInfo { u = 0f, s0 = q.shoreS, s1 = s1 });
        return list;
    }

    // ---------------------------------------------------------------- berthed + moored boats (all rock)
    private struct M2BoatKind { public string stem; public float len, beam, scale, w; }

    private static readonly M2BoatKind[] M2Kinds =
    {
        new M2BoatKind { stem = "Nagisa_MotorYacht", len = 16f, beam = 4.6f, scale = 0.78f, w = 9 },
        new M2BoatKind { stem = "Nagisa_MR_Sloop", len = 14f, beam = 4.1f, scale = 1f, w = 30 },
        new M2BoatKind { stem = "Nagisa_MR_Catamaran", len = 13f, beam = 6.6f, scale = 1f, w = 9 },
        new M2BoatKind { stem = "Nagisa_Sailboat", len = 11f, beam = 3.6f, scale = 0.95f, w = 12 },
        new M2BoatKind { stem = "Nagisa_MR_Runabout", len = 6.5f, beam = 2.3f, scale = 1f, w = 28 },
        new M2BoatKind { stem = "Nagisa_MR_Dinghy", len = 3.4f, beam = 1.5f, scale = 1f, w = 12 },
    };

    private static M2BoatKind M2PickBoat(System.Random rng, bool small = false)
    {
        float sum = 0f;
        foreach (var k in M2Kinds) if (!small || k.len < 8f) sum += k.w;
        float r = (float)rng.NextDouble() * sum;
        foreach (var k in M2Kinds)
        {
            if (small && k.len >= 8f) continue;
            r -= k.w; if (r <= 0f) return k;
        }
        return M2Kinds[1];
    }

    private static Transform _m2SwayHolder;
    private static readonly List<Transform> _m2Rockers = new List<Transform>();

    private static GameObject M2Boat(Transform parent, M2BoatKind k, Vector3 w, float yaw, System.Random rng)
    {
        if (!GlbExists(k.stem)) return null;
        w.y = 0.02f;
        var go = PlaceWorld(k.stem, parent, w, yaw, k.scale, M2BoatCull, M2Hull(rng));
        if (go == null) return null;
        _m2Rockers.Add(go.transform);
        return go;
    }

    private static void M2FlushRockers(Transform parent, string name, Vector3 centre)
    {
        if (_m2Rockers.Count == 0) return;
        var holder = new GameObject(name).transform;
        holder.SetParent(parent, false);
        holder.position = centre;
        var sway = holder.gameObject.AddComponent<AmbientSway>();
        sway.mode = AmbientSway.SwayMode.Noise;
        sway.targets = _m2Rockers.ToArray();
        sway.axis = Vector3.forward; sway.secondaryAxis = Vector3.right;          // roll about the bow axis, pitch about the beam
        sway.maxAngle = 2.0f; sway.secondaryAngle = 1.1f; sway.frequency = 0.26f; sway.phaseOffsetPerTarget = 0.9f;
        sway.positionSway = new Vector3(0.03f, 0.07f, 0.04f); sway.positionFrequency = 0.21f;
        sway.cullDistance = 1500f; sway.seed = 8200 + parent.childCount;
        sway.Bake();
        EditorUtility.SetDirty(sway);
        _m2Rockers.Clear();
    }

    private static bool M2Deep(Vector3 w, float minDepth = 0.7f) => _ground.Height(w.x, w.z) < -minDepth;

    private static int M2Berths(Transform parent, M2Q q, List<M2PierInfo> piers, System.Random rng, out int moored)
    {
        int n = 0; moored = 0;
        float yawSea = Mathf.Atan2(q.dir.x, q.dir.z) * Mathf.Rad2Deg;
        _m2Rockers.Clear();
        // 1) boats along both sides of each pier (parallel berths, bow out or in)
        foreach (var p in piers)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                float s = Mathf.Max(p.s0 + 12f, M2FirstDeepS(q, p.u + side * 6f) + 1f);
                int guard = 0;
                while (s < p.s1 - 3f && guard++ < 8)
                {
                    var k = M2PickBoat(rng, small: (p.s1 - s) < 20f);
                    if (s + k.len > p.s1 + 6f) { s += 6f; continue; }
                    float off = side * (1.7f + k.beam * k.scale * 0.5f + 0.35f);
                    var w = q.At(p.u + off, s + k.len * 0.5f * k.scale);
                    if (!M2Deep(w, 0.5f)) { s += 8f; continue; }
                    float yaw = yawSea + (rng.NextDouble() < 0.5 ? 0f : 180f);
                    if (M2Boat(parent, k, w, yaw + (float)(rng.NextDouble() * 2 - 1), rng) != null) n++;
                    s += k.len * k.scale + 2.4f;
                }
            }
        }
        M2FlushRockers(parent, "Rocking boats (piers)", q.At(0f, q.best * 0.7f));
        // 2) alongside the existing pontoon spines (the -x / -perp face; their fingers are on +perp)
        int sp = 0;
        for (int kx = -2; kx <= 2; kx++)
        {
            float u = kx * MarinaPontoonGapM;
            var kind = (kx == -2) ? new M2BoatKind { stem = "Nagisa_MR_Superyacht", len = 34f, beam = 7.6f, scale = 1f, w = 1 } : M2PickBoat(rng);
            if (kx == -2 && !GlbExists(kind.stem)) kind = M2PickBoat(rng);
            float off = -(1.8f + kind.beam * kind.scale * 0.5f + 0.4f);
            var w = q.At(u + off, q.best + 22f + (kx == -2 ? 0f : (float)(rng.NextDouble() * 4 - 2)));
            if (!M2Deep(w, 1.5f)) continue;
            if (M2Boat(parent, kind, w, yawSea + (kx % 2 == 0 ? 0f : 180f), rng) != null) { n++; sp++; }
            // a second boat on the shoreward half of the spine for the shorter types
            if (kind.len < 20f)
            {
                var k2 = M2PickBoat(rng, small: true);
                var w2 = q.At(u - (1.7f + k2.beam * 0.5f + 0.3f), q.best + 12f);
                if (M2Deep(w2, 1.2f) && M2Boat(parent, k2, w2, yawSea, rng) != null) { n++; sp++; }
            }
        }
        M2FlushRockers(parent, "Rocking boats (spines)", q.At(0f, q.best + 22f));
        // 3) mooring field inside the basin: swinging to the wind (all bows roughly one way)
        float windYaw = yawSea + 205f;
        var placedM = new List<Vector3>();
        for (int tries = 0; tries < 160 && moored < 14; tries++)
        {
            float u = Mathf.Lerp(-150f, 150f, (float)rng.NextDouble());
            float s = Mathf.Lerp(q.best + 62f, q.best + 150f, (float)rng.NextDouble());
            var w = q.At(u, s);
            if (!M2Deep(w, 2.2f)) continue;
            if ((u > 52f && u < 128f) || u < -100f) continue;      // keep the entrance fairway open and the south basin free for paddleboards
            bool clear = true;
            foreach (var o in placedM) if ((o - w).sqrMagnitude < 28f * 28f) { clear = false; break; }
            if (!clear) continue;
            var k = M2PickBoat(rng);
            if (k.len < 9f && rng.NextDouble() < 0.5) continue;
            if (M2Boat(parent, k, w, windYaw + (float)(rng.NextDouble() * 50 - 25), rng) != null) { moored++; placedM.Add(w); }
        }
        M2FlushRockers(parent, "Rocking boats (moorings)", q.At(0f, q.best + 100f));
        Debug.Log($"[g-marina2] boats: {n} berthed ({sp} on spines), {moored} moored; hull colours via 7 remaps; all rock via AmbientSway.");
        return n;
    }

    private static float M2FirstDeepS(M2Q q, float u)
    {
        for (float s = 0f; s < 260f; s += 2f)
        {
            var w = q.At(u, s);
            if (_ground.Height(w.x, w.z) < -0.7f) return s;
        }
        return q.best;
    }

    // ---------------------------------------------------------------- breakwater arms + beacons
    private static void M2Breakwater(Transform parent, M2Q q, System.Random rng)
    {
        float sOut = q.best + 105f;
        var arms = new List<List<Vector2>>();
        float uN = 175f, uGapA = 115f, uGapB = 65f, uS = -200f;
        float shoreN = M2ShoreS(q, uN);
        arms.Add(M2Line(uN, shoreN + 8f, uN, sOut));            // north arm, shore -> out
        arms.Add(M2Line(uN, sOut, uGapA, sOut));                // outer arm, north part
        arms.Add(M2Line(uGapB, sOut, uS, sOut));                // outer arm, south part (joins the existing lighthouse jetty)
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        float row = 0f;
        foreach (var arm in arms)
        {
            Vector3 prevL = default, prevC1 = default, prevC2 = default, prevR = default; bool have = false;
            for (int i = 0; i < arm.Count; i++)
            {
                var c = q.At(arm[i].x, arm[i].y);
                var dirA = i + 1 < arm.Count ? q.At(arm[i + 1].x, arm[i + 1].y) - c : c - q.At(arm[i - 1].x, arm[i - 1].y);
                dirA.y = 0f; dirA.Normalize();
                var side = Vector3.Cross(Vector3.up, dirA);
                float ground = _ground.Height(c.x, c.z);
                float crown = Mathf.Max(1.7f, ground + 0.4f);
                float jit() => (float)(rng.NextDouble() - 0.5) * 0.5f;
                var L = c - side * 9.2f; L.y = -4f;
                var C1 = c - side * 2.4f; C1.y = crown + jit();
                var C2 = c + side * 2.4f; C2.y = crown + jit();
                var R = c + side * 9.2f; R.y = -4f;
                if (have)
                {
                    int b = v.Count;
                    v.AddRange(new[] { prevL, prevC1, C1, L, prevC1, prevC2, C2, C1, prevC2, prevR, R, C2 });
                    float u0 = row / 5f, u1 = (row + 6f) / 5f;
                    for (int s = 0; s < 3; s++)
                    {
                        uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u0, 1.3f)); uv.Add(new Vector2(u1, 1.3f)); uv.Add(new Vector2(u1, 0));
                    }
                    for (int s = 0; s < 3; s++)
                    {
                        int o = b + s * 4;
                        var n = Vector3.Cross(v[o + 1] - v[o], v[o + 2] - v[o]);
                        if (n.y >= 0f) { tri.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 }); }
                        else { tri.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 }); }
                    }
                    row += 6f;
                }
                prevL = L; prevC1 = C1; prevC2 = C2; prevR = R; have = true;
            }
        }
        var mesh = Finish("Nagisa_M2_Breakwater", v, uv, tri);
        var go = AddMesh(parent, "Nagisa_M2_Breakwater", mesh, _nb["NB_Stone"], false);
        // beacons on the two entrance heads (green = south head, red = north head)
        foreach (var (u, col) in new[] { (uGapA, 0), (uGapB, 1) })
        {
            var c = q.At(u, sOut); c.y = 1.7f;
            var hb = new HwBuf();
            hb.Box(new Vector3(0f, 3.0f, 0f), Vector3.right, Vector3.forward, 1.5f, 6.0f, 1.5f);
            hb.Box(new Vector3(0f, 6.6f, 0f), Vector3.right, Vector3.forward, 2.1f, 0.4f, 2.1f);
            var t = new GameObject("M2 Beacon " + (col == 0 ? "red" : "green")).transform;
            t.SetParent(parent, false); t.position = c;
            string name = "Nagisa_M2_Beacon" + col;
            var bm = Finish(name, hb.v, hb.uv, hb.t);
            var bgo = AddMesh(t, name, bm, _nb["NB_Concrete"], false);
            bgo.transform.localPosition = Vector3.zero;
            var lamp = new HwBuf();
            lamp.Box(new Vector3(0f, 7.3f, 0f), Vector3.right, Vector3.forward, 1.0f, 0.9f, 1.0f);
            var lm = Finish(name + "_Lamp", lamp.v, lamp.uv, lamp.t);
            var lgo = AddMesh(t, name + "_Lamp", lm, col == 0 ? NbGlow("M2_BeaconRed", new Color(1f, 0.15f, 0.1f, 1f)) : NbGlow("M2_BeaconGreen", new Color(0.1f, 1f, 0.35f, 1f)), false);
            lgo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        Debug.Log($"[g-marina2] breakwater: 3 rubble-mound arms at {sOut:0} m out (entrance u {uGapB:0}..{uGapA:0}), {tri.Count / 3} tris, 2 beacons.");
    }

    private static List<Vector2> M2Line(float u0, float s0, float u1, float s1)
    {
        var l = new List<Vector2>();
        float len = Mathf.Max(Mathf.Abs(u1 - u0), Mathf.Abs(s1 - s0));
        int n = Mathf.Max(2, Mathf.CeilToInt(len / 6f));
        for (int i = 0; i <= n; i++) l.Add(Vector2.Lerp(new Vector2(u0, s0), new Vector2(u1, s1), i / (float)n));
        return l;
    }

    // ---------------------------------------------------------------- boats slowly entering / leaving the marina
    private static List<Vector2> _m2Hazard;

    private static bool M2WaterOk(Vector3 p)
    {
        if (_ground.Height(p.x, p.z) > -1.3f) return false;
        return _route.PlanDistance(p.x, p.z, out _) > 60f;
    }

    private static void M2MarinaMovers(Transform parent, M2Q q, System.Random rng)
    {
        float sOut = q.best + 105f;
        int built = 0;
        // (stem, in-lane u, out-lane u, speed, phase)  lanes are 12 m either side of the middle of the 50 m entrance gap (u 65..115)
        var specs = new[]
        {
            ("Nagisa_MotorYacht", 0.78f, 3.6f, 0.05f), ("Nagisa_MR_Sloop", 0.78f, 2.7f, 0.40f),
            ("Nagisa_MR_Catamaran", 0.78f, 2.4f, 0.68f), ("Nagisa_MR_Superyacht", 0.85f, 2.0f, 0.86f),
        };
        foreach (var (stem, scale, speed, phase) in specs)
        {
            if (!GlbExists(stem)) continue;
            var path = new List<Vector3>();
            // out lane (u 100) seaward through the entrance, wide turn offshore, in lane (u 80) back, a U-turn inside the basin
            var pts = new List<Vector2>
            {
                new Vector2(78f, q.best + 80f), new Vector2(78f, sOut - 20f), new Vector2(78f, sOut + 50f),
                new Vector2(84f, sOut + 130f), new Vector2(104f, sOut + 200f), new Vector2(140f, sOut + 245f),
                new Vector2(185f, sOut + 235f), new Vector2(195f, sOut + 185f), new Vector2(170f, sOut + 120f),
                new Vector2(125f, sOut + 70f), new Vector2(104f, sOut + 20f), new Vector2(102f, sOut - 30f),
                new Vector2(102f, q.best + 72f), new Vector2(90f, q.best + 62f),
            };
            foreach (var p in pts) path.Add(q.At(p.x, p.y));
            var smooth = Smooth(path, 3);
            bool ok = true;
            foreach (var p in smooth) if (!M2WaterOk(p)) { ok = false; break; }
            if (!ok) { Debug.LogWarning($"[g-marina2] mover {stem}: path crosses shallows/road, dropped."); continue; }
            var holder = AmbientMoverStaging.Converge(parent, $"M2 {stem} (mover {built})");
            var boat = PlaceWorld(stem, holder.transform, smooth[0], 0f, scale, M2BoatCull, M2Hull(rng));
            if (boat == null) { Object.DestroyImmediate(holder); continue; }
            var mover = holder.AddComponent<AmbientPathMover>();
            mover.mode = AmbientPathMover.PathMode.Loop;
            mover.path = AmbientMoverStaging.BakePolyline(smooth);
            mover.cars = new[] { boat.transform };
            mover.carOffsets = new[] { 0f };
            mover.carYaw = new[] { 0f };
            mover.speed = speed; mover.accel = 0.8f;
            mover.bankFactor = 0.15f; mover.bobHeave = 0.07f; mover.bobTilt = 0.7f; mover.bobFrequency = 0.3f;
            mover.cullDistance = AmbientCull.DefaultCullDistance;
            mover.seed = 8300 + built;
            mover.Bake();
            mover.startDistance = phase * mover.TotalLength;
            mover.SetHead(mover.startDistance);
            mover.Place();
            EditorUtility.SetDirty(mover);
            built++;
            Debug.Log($"[g-marina2] mover {stem}: {mover.TotalLength:0} m loop through the entrance at {speed:0.0} m/s ({speed * 1.944f:0.0} kn).");
        }
    }

    // ================================================================= fishing harbour
    private static string M2Harbour(Transform parent, Transform spots, M2Occ occ, System.Random rng)
    {
        var apron = new HwBuf(); var mole = new HwBuf(); var piles = new HwBuf(); var wall = new HwBuf();
        int sheds = 0, racks = 0, dock = 0, hoists = 0, boats = 0;
        // ---- concrete apron
        for (float m = M2HarbM0; m < M2HarbM1 - 0.01f; m += M2Step)
        {
            float m2 = m + M2Step;
            float vsA = M2Vs(m), vsB = M2Vs(m2);
            int cols = Mathf.Max(2, Mathf.CeilToInt((Mathf.Min(vsA, vsB) - 4f - 16f) / 6f));
            for (int c = 0; c < cols; c++)
            {
                float fa = c / (float)cols, fb = (c + 1) / (float)cols;
                float v0a = Mathf.Lerp(16f, vsA - 4f, fa), v1a = Mathf.Lerp(16f, vsA - 4f, fb);
                float v0b = Mathf.Lerp(16f, vsB - 4f, fa), v1b = Mathf.Lerp(16f, vsB - 4f, fb);
                apron.Quad(M2Lift(M2W(m, v0a)), M2Lift(M2W(m, v1a)), M2Lift(M2W(m2, v1b)), M2Lift(M2W(m2, v0b)), Vector3.up, m / 4f, m2 / 4f, v0a / 4f, v1a / 4f);
            }
            // quay lip at the water: low concrete face
            var sd = M2Sd(m);
            var ta = M2W(m, vsA - 4f); var tb = M2W(m2, vsB - 4f);
            var a = new Vector3(ta.x, ta.y + M2PaveLift, ta.z); var b = new Vector3(tb.x, tb.y + M2PaveLift, tb.z);
            wall.Quad(a - Vector3.up * 0.9f, a, b, b - Vector3.up * 0.9f, sd, m / 3f, m2 / 3f, 0f, 0.3f);
        }
        M2Flush(apron, parent, "Nagisa_M2_HarbourApron", _nb["NB_Concrete"], true, false);
        M2Flush(wall, parent, "Nagisa_M2_HarbourLip", _nb["NB_Concrete"], false, false);

        // ---- buildings and props on the apron
        foreach (var (stem, m, vf, sc) in new[] { ("Nagisa_MR_FishShed", 764f, 44f, 1f), ("Nagisa_MR_FishShed", 806f, 44f, 1f) })
        {
            if (!GlbExists(stem)) continue;
            var sd = M2Sd(m); float yaw = M2FrontYaw(sd);
            var c = M2W(m, vf - 4f);
            if (!M2FootOk(occ, c, yaw, 8f, 5f, 1f, out float y, 2.4f, 6f)) continue;
            var go = PlaceWorld(stem, parent, new Vector3(c.x, y + M2PaveLift - 0.05f, c.z), yaw, sc, 0.003f);
            if (go == null) continue;
            go.name = "M2 Fish Shed m" + m.ToString("0");
            M2Reserve(occ, c, yaw, 8f, 5f); sheds++;
            var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            M2Spot(spots, "Work", c + front * 6.5f, front);
            M2Spot(spots, "Work", c + front * 7.5f + Vector3.Cross(Vector3.up, front) * 3f, front);
        }
        for (float m = 745f; m < 855f; m += 14f)
        {
            var sd = M2Sd(m); float yaw = M2FrontYaw(sd);
            float vs = M2Vs(m);
            var c = M2W(m, vs - 14f);
            if (rng.NextDouble() < 0.4 && GlbExists("Nagisa_MR_NetRack") && M2FootOk(occ, c, yaw, 4f, 1.5f, 0.5f, out float y, 2.2f, 5f))
            {
                var go = PlaceWorld("Nagisa_MR_NetRack", parent, new Vector3(c.x, y + M2PaveLift - 0.05f, c.z), yaw + 90f * ((int)(m / 14f) % 2 == 0 ? 0 : 0), 1f, 0.004f);
                if (go != null) { go.name = "M2 Net Rack m" + m.ToString("0"); M2Reserve(occ, c, yaw, 4f, 1.5f); racks++; M2Spot(spots, "Work", c - Vector3.Cross(Vector3.up, M2Tan(m)) * 3f - sd * 2f, sd); }
            }
            else
            {
                var cd = M2W(m + 3f, vs - 8f);
                if (GlbExists("Nagisa_MR_FishDock") && M2FootOk(occ, cd, yaw, 3f, 1.6f, 0.2f, out float y2, 2.2f, 3f))
                {
                    var go = PlaceWorld("Nagisa_MR_FishDock", parent, new Vector3(cd.x, y2 + M2PaveLift - 0.05f, cd.z), yaw, 1f, 0.004f);
                    if (go != null) { M2Reserve(occ, cd, yaw, 3f, 1.6f); dock++; }
                }
            }
        }
        // boat hoist near the north end of the apron (a slip straddled by the hoist)
        if (GlbExists("Nagisa_MR_BoatHoist"))
        {
            float m = 846f; var sd = M2Sd(m);
            var c = M2W(m, M2Vs(m) - 11f);
            if (M2FootOk(occ, c, M2FrontYaw(sd), 4f, 4f, 0.4f, out float y, 2.4f, 4f))
            {
                var go = PlaceWorld("Nagisa_MR_BoatHoist", parent, new Vector3(c.x, y + M2PaveLift - 0.05f, c.z), M2FrontYaw(sd) + 90f, 1f, 0.004f);
                if (go != null)
                {
                    go.name = "M2 Boat Hoist"; hoists++; M2Reserve(occ, c, M2FrontYaw(sd), 4f, 4f);
                    // a boat on the hoist's slings
                    if (GlbExists("Nagisa_MR_FishingBoat"))
                    {
                        var bt = PlaceWorld("Nagisa_MR_FishingBoat", parent, new Vector3(c.x, y + M2PaveLift + 1.6f, c.z), M2FrontYaw(sd) + 90f, 0.62f, 0.003f, M2Hull(rng));
                        if (bt != null) bt.name = "M2 Hoisted Boat";
                    }
                }
            }
        }
        // beached dinghies on the sand below the apron
        for (float m = 742f; m < 856f; m += 17f)
        {
            if (!GlbExists("Nagisa_MR_Dinghy")) break;
            var c = M2W(m, M2Vs(m) + 0.8f);
            if (_ground.Height(c.x, c.z) < 0.15f) c = M2W(m, M2Vs(m) - 1.5f);
            if (M2Taken(occ, c, 2f)) continue;
            var g = PlaceWorld("Nagisa_MR_Dinghy", parent, new Vector3(c.x, Mathf.Max(0.15f, c.y) + 0.35f, c.z), M2FrontYaw(M2Sd(m)) + (float)(rng.NextDouble() * 40 - 20), 1f, 0.004f, M2Hull(rng));
            if (g != null) { g.transform.rotation *= Quaternion.Euler(-4f, 0f, 3f); boats++; }
        }

        // ---- the mole: concrete pier straight out from the apron with working boats on both flanks
        float mm = 796f;
        var sdM = M2Sd(mm); var tanM = M2Tan(mm);
        float vS = M2Vs(mm);
        float vEnd = vS + 92f;
        const float moleW = 7f, deckY = 1.35f;
        int steps = Mathf.CeilToInt((vEnd - (vS - 6f)) / 4f);
        for (int k = 0; k < steps; k++)
        {
            float va = Mathf.Lerp(vS - 6f, vEnd, k / (float)steps), vb = Mathf.Lerp(vS - 6f, vEnd, (k + 1) / (float)steps);
            var side = Vector3.Cross(Vector3.up, tanM);
            Vector3 P(float vv, float so) { var w = M2W(mm, vv) + side * so; w.y = Mathf.Max(deckY, _ground.Height(w.x, w.z) + M2PaveLift); return w; }
            mole.Quad(P(va, -moleW * 0.5f), P(va, moleW * 0.5f), P(vb, moleW * 0.5f), P(vb, -moleW * 0.5f), Vector3.up, va / 4f, vb / 4f, 0f, 1.7f);
            var d = Vector3.down * 1.1f;
            mole.Quad(P(va, -moleW * 0.5f) + d, P(va, -moleW * 0.5f), P(vb, -moleW * 0.5f), P(vb, -moleW * 0.5f) + d, -side, va / 4f, vb / 4f, 0f, 0.3f);
            mole.Quad(P(va, moleW * 0.5f), P(va, moleW * 0.5f) + d, P(vb, moleW * 0.5f) + d, P(vb, moleW * 0.5f), side, va / 4f, vb / 4f, 0f, 0.3f);
            if (k % 2 == 1)
                foreach (float so in new[] { -moleW * 0.5f + 0.4f, moleW * 0.5f - 0.4f })
                {
                    var pp = P(va, so);
                    if (_ground.Height(pp.x, pp.z) < deckY - 0.9f) piles.Box(new Vector3(pp.x, (deckY - 1.2f - 3f) * 0.5f, pp.z), side, tanM, 0.34f, deckY - 1.2f + 3f, 0.34f);
                }
        }
        M2Flush(mole, parent, "Nagisa_M2_HarbourMole", _nb["NB_Concrete"], true);
        M2Flush(piles, parent, "Nagisa_M2_HarbourPiles", _nb["NB_Concrete"], false, false);
        // bollards + lamps on the mole, boats alongside
        var sideV = Vector3.Cross(Vector3.up, tanM);
        for (float vv = vS + 6f; vv < vEnd - 2f; vv += 10f)
        {
            foreach (float so in new[] { -moleW * 0.5f + 0.5f, moleW * 0.5f - 0.5f })
            {
                var w = M2W(mm, vv) + sideV * so; w.y = deckY;
                PlaceWorld("Nagisa_S_Bollard", parent, w, 0f, 1f, 0.004f);
            }
        }
        for (float vv = vS + 14f; vv < vEnd - 6f; vv += 30f)
        {
            var w = M2W(mm, vv) + sideV * (moleW * 0.5f - 0.6f); w.y = deckY;
            PlaceWorld("Nagisa_PromenadeLamp", parent, w, M2FrontYaw(sdM), 0.9f, 0.004f);
        }
        _m2Rockers.Clear();
        foreach (float side in new[] { -1f, 1f })
        {
            float vv = vS + 10f;
            while (vv < vEnd - 12f)
            {
                var k = rng.NextDouble() < 0.7 ? new M2BoatKind { stem = "Nagisa_MR_FishingBoat", len = 13f, beam = 4.2f, scale = 1f } : M2PickBoat(rng, true);
                if (!GlbExists(k.stem)) { vv += 12f; continue; }
                var w = M2W(mm, vv + k.len * 0.5f * k.scale) + sideV * (side * (moleW * 0.5f + 0.4f + k.beam * k.scale * 0.5f));
                if (_ground.Height(w.x, w.z) < -0.4f)
                {
                    var hull = k.stem == "Nagisa_MR_FishingBoat" ? M2FishHull(rng) : M2Hull(rng);
                    w.y = 0.02f;
                    var go = PlaceWorld(k.stem, parent, w, Mathf.Atan2(tanM.x * 0f + sdM.x, sdM.z) * Mathf.Rad2Deg + (rng.NextDouble() < 0.5 ? 0f : 180f), k.scale, M2BoatCull, hull);
                    if (go != null) { _m2Rockers.Add(go.transform); boats++; }
                }
                vv += k.len * k.scale + 2.2f;
            }
        }
        M2FlushRockers(parent, "Rocking boats (harbour)", M2W(mm, vS + 40f));
        M2Spot(spots, "Work", M2W(mm, vEnd - 8f), sdM);
        M2Spot(spots, "Work", M2W(mm, vS + 30f) + sideV * 2f, sdM);
        // rubble arm south of the mole, closing the pocket
        {
            float ma = 832f; var sda = M2Sd(ma);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3 pl = default, pc1 = default, pc2 = default, pr = default; bool have = false; float row = 0f;
            for (float vv = M2Vs(ma) + 2f; vv < M2Vs(ma) + 78f; vv += 5f)
            {
                float mmm = ma + (vv - M2Vs(ma)) * 0.28f;        // the arm leans south (toward larger m) as it goes out
                var c = M2W(mmm, vv); var tn = M2Tan(mmm); var side = Vector3.Cross(Vector3.up, tn);
                var dirA = (M2W(mmm + 1.4f, vv + 5f) - c); dirA.y = 0f; dirA.Normalize();
                side = Vector3.Cross(Vector3.up, dirA);
                float crown = Mathf.Max(1.6f, c.y + 0.4f);
                var L = c - side * 8f; L.y = -3.5f; var C1 = c - side * 2.2f; C1.y = crown; var C2 = c + side * 2.2f; C2.y = crown; var R = c + side * 8f; R.y = -3.5f;
                if (have)
                {
                    int b = v.Count;
                    v.AddRange(new[] { pl, pc1, C1, L, pc1, pc2, C2, C1, pc2, pr, R, C2 });
                    for (int s = 0; s < 3; s++) { uv.Add(new Vector2(row / 5f, 0)); uv.Add(new Vector2(row / 5f, 1.2f)); uv.Add(new Vector2((row + 5f) / 5f, 1.2f)); uv.Add(new Vector2((row + 5f) / 5f, 0)); }
                    for (int s = 0; s < 3; s++)
                    {
                        int o = b + s * 4;
                        var nrm = Vector3.Cross(v[o + 1] - v[o], v[o + 2] - v[o]);
                        if (nrm.y >= 0f) tri.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 }); else tri.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
                    }
                    row += 5f;
                }
                pl = L; pc1 = C1; pc2 = C2; pr = R; have = true;
            }
            if (tri.Count > 0) AddMesh(parent, "Nagisa_M2_HarbourArm", Finish("Nagisa_M2_HarbourArm", v, uv, tri), _nb["NB_Stone"], false);
        }
        // working boats leaving / returning on slow loops (fishing boats; open water well clear of the road)
        int movers = M2HarbourMovers(parent, mm, vEnd, rng);
        return $"{sheds} sheds, {racks} net racks, {dock} dock clutter, {hoists} hoist, {boats} boats (mole + beached), {movers} working-boat loops";
    }

    private static Dictionary<string, Material> _m2FishHull;
    private static Dictionary<string, Material> M2FishHull(System.Random rng)
    {
        if (_m2FishHull == null || true)
        {
            var cols = new[] { Srgb(196, 48, 40), Srgb(30, 84, 150), Srgb(238, 238, 232), Srgb(28, 128, 118) };
            int i = rng.Next(cols.Length);
            _m2FishHull = new Dictionary<string, Material> { ["NB_MR_Hull"] = Cel("Nagisa_MR_FishHull_" + i, cols[i], 0.5f, 0.3f, 0.12f, null, null, 0.5f) };
        }
        return _m2FishHull;
    }

    private static int M2HarbourMovers(Transform parent, float moleM, float moleVEnd, System.Random rng)
    {
        int built = 0;
        var sd = M2Sd(moleM); var tan = M2Tan(moleM);
        var origin = M2W(moleM, 0f);
        Vector3 At(float along, float v) { var w = origin + tan * along + sd * v; w.y = 0.02f; return w; }
        // loops start beside the mole tip, run seaward, swing wide and return; two lanes 22 m apart
        for (int b = 0; b < 3; b++)
        {
            float lane = 28f + b * 22f;
            var pts = new List<Vector3>
            {
                At(lane, moleVEnd + 8f), At(lane + 5f, moleVEnd + 70f), At(lane + 30f, moleVEnd + 160f),
                At(lane + 80f, moleVEnd + 240f), At(lane + 150f, moleVEnd + 270f), At(lane + 210f, moleVEnd + 215f),
                At(lane + 190f, moleVEnd + 130f), At(lane + 130f, moleVEnd + 70f), At(lane + 80f, moleVEnd + 25f),
                At(lane + 40f, moleVEnd + 10f),
            };
            var smooth = Smooth(pts, 3);
            bool ok = true;
            foreach (var p in smooth) if (!M2WaterOk(p)) { ok = false; break; }
            if (!ok) { Debug.LogWarning($"[g-marina2] harbour mover {b}: path in shallows/near road, dropped."); continue; }
            string stem = b == 1 ? "Nagisa_MR_Runabout" : "Nagisa_MR_FishingBoat";
            if (!GlbExists(stem)) continue;
            var holder = AmbientMoverStaging.Converge(parent, $"M2 Harbour boat {b}");
            var boat = PlaceWorld(stem, holder.transform, smooth[0], 0f, 1f, M2BoatCull, stem.Contains("Fishing") ? M2FishHull(rng) : M2Hull(rng));
            if (boat == null) { Object.DestroyImmediate(holder); continue; }
            var mover = holder.AddComponent<AmbientPathMover>();
            mover.mode = AmbientPathMover.PathMode.Loop;
            mover.path = AmbientMoverStaging.BakePolyline(smooth);
            mover.cars = new[] { boat.transform }; mover.carOffsets = new[] { 0f }; mover.carYaw = new[] { 0f };
            mover.speed = b == 1 ? 3.4f : 2.4f; mover.accel = 0.8f;
            mover.bankFactor = 0.12f; mover.bobHeave = 0.1f; mover.bobTilt = 0.9f; mover.bobFrequency = 0.33f;
            mover.cullDistance = AmbientCull.DefaultCullDistance;
            mover.seed = 8400 + b;
            mover.Bake();
            mover.startDistance = (0.2f + b * 0.33f) * mover.TotalLength;
            mover.SetHead(mover.startDistance);
            mover.Place();
            EditorUtility.SetDirty(mover);
            built++;
        }
        return built;
    }
}

// NB3 (COORDINATION.md): the car-only coastal highway + traffic, built to the SIDE RULE.
//
// Offsets are metres INLAND of the ride centreline (the side rule's -8..-42 m bands):
//   7.6 guardrail | 8.0-17.5 carriageway A | 17.5-22.5 median (barrier, anti-glare, oleander,
//   twin lights) | 22.5-32.0 carriageway B | 31.8 guardrail | 34.6 sound wall + vines | ~38.5 palms.
// Nothing of NB3 is ever placed seaward of +7.6 m inland, and nothing motorised crosses the ride road.
//
// Conflicts: where another package's building/prop (or sea / steep ground) occupies the band, the
// highway drops into a cut-and-cover tunnel (portal headwall + dark tube, the traffic path runs
// underground), so no other package's file or object is edited. The conflict list is logged
// ("[nb3] conflict ...") for the NB2 owner.
//
// Traffic: the W0 kit. One AmbientPathMover per vehicle (so the 1.5 km AmbientCull works per car),
// on 2 closed loops per coastal section (fast lanes / slow lanes). Loops U-turn underground behind
// the end portals. Left-hand traffic (Japan-inspired setting). Emissive head/tail lamps are added
// per vehicle; the LOD group culls each vehicle at ~1.5 km.
//
// Every number below is PROVISIONAL tuning (illustrative; see MAPLERIDE_PROJECT_CONTEXT.md).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    public const string HighwayGroupName = "NB Highway";

    // ---- cross-section (inland metres) - PROVISIONAL
    private const float HwStepM = 6f;
    private const float HwDeck0 = 8.0f, HwDeck1 = 32.0f;
    private const float HwMedian0 = 17.5f, HwMedian1 = 22.5f, HwBarrierO = 20f;
    private static readonly float[] HwLineSolid = { 9.5f, 16.7f, 23.3f, 30.5f };
    private static readonly float[] HwLineDash = { 13.1f, 26.9f };
    private const float HwLaneAslow = 11.3f, HwLaneAfast = 14.9f, HwLaneBfast = 25.1f, HwLaneBslow = 28.7f;
    private const float HwRailNear = 7.6f, HwRailFar = 31.8f;
    private const float HwWallO = 34.6f, HwWallH = 4.6f, HwPalmO = 38.5f;
    private const float HwLift = 0.14f;

    // ---- conflict / tunnel handling - PROVISIONAL
    private const float HwMaxGrade = 0.10f;
    private const float HwPortalPadM = 30f, HwMinOpenM = 180f, HwEndTunnelM = 40f;
    private const float HwTunnelDepthM = 10f, HwTubeM = 26f, HwPortalClearH = 5.4f, HwPortalH = 7.6f;

    // ---- furniture spacing - PROVISIONAL
    private const float HwLightSpacingM = 48f, HwLightH = 11f, HwLightArm = 4.6f;
    private const float HwGantrySpacingM = 850f;
    private const float HwOleanderM = 12f, HwPalmSpacingM = 30f;
    private const int HwFootbridges = 2;

    // ---- traffic - PROVISIONAL (brief: 50-80 km/h, >=120 vehicles, cull 1.5 km)
    private const float HwFastKmh = 76f, HwSlowKmh = 56f;
    private const float HwMinClearM = 16f, HwMeanExtraGapM = 80f;
    private const float HwCullM = 1500f;

    private struct HwSt
    {
        public float d; public Vector3 p, t, inl; public float y, gmax; public bool blocked; public string why;
    }

    private sealed class HwBuf
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want, float u0, float u1, float v0, float v1)
        {
            int n = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u1, v0));
            var nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, want) >= 0f) t.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            else t.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
        }

        public void Box(Vector3 c, Vector3 side, Vector3 fwd, float w, float h, float d) =>
            NagisaBayEnvironment.Box(v, uv, t, c, side, fwd, w, h, d);
    }

    private static readonly Dictionary<string, HwBuf> _hwBufs = new Dictionary<string, HwBuf>();
    private static readonly Dictionary<string, Material> _hwMats = new Dictionary<string, Material>();
    private static HwBuf B(string kind) { if (!_hwBufs.TryGetValue(kind, out var b)) _hwBufs[kind] = b = new HwBuf(); return b; }

    private static int _hwChunk;
    private static void HwFlush(Transform parent, string tag)
    {
        foreach (var kv in _hwBufs)
        {
            if (kv.Value.v.Count == 0) continue;
            string name = $"NB3_{tag}_{kv.Key}_{_hwChunk}";
            var go = AddMesh(parent, name, Finish(name, kv.Value.v, kv.Value.uv, kv.Value.t), _hwMats[kv.Key], false);
            var mr = go.GetComponent<MeshRenderer>();
            if (kv.Key == "glow" || kv.Key == "lines" || kv.Key == "vine") mr.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
        _hwBufs.Clear();
        _hwChunk++;
    }

    private static Material Nb(string slot, Material fallback) =>
        _nb.TryGetValue(slot, out var m) && m != null ? m : fallback;

    private static void HwMaterials()
    {
        _hwMats.Clear();
        _hwMats["asphalt"] = Cel("Nagisa_NB3_Asphalt", Srgb(74, 77, 82), 0.22f, 0.1f, 0.08f);
        _hwMats["concrete"] = Nb("NB_Concrete", Cel("Nagisa_NB3_Concrete", Srgb(190, 188, 180), 0.12f, 0.06f));
        _hwMats["metal"] = Nb("NB_Metal", Cel("Nagisa_NB3_Metal", Srgb(160, 164, 168), 0.6f, 0.45f));
        _hwMats["median"] = Nb("NB_Groundcover", Cel("Nagisa_NB3_Median", Srgb(84, 128, 60), 0.05f, 0.02f));
        _hwMats["lines"] = Cel("Nagisa_NB3_LineWhite", Srgb(238, 238, 230), 0.25f, 0.12f, 0.1f);
        _hwMats["antiglare"] = Cel("Nagisa_NB3_AntiGlare", Srgb(52, 104, 78), 0.3f, 0.15f);
        _hwMats["wall"] = Cel("Nagisa_NB3_SoundWall", Srgb(196, 204, 198), 0.22f, 0.12f);
        _hwMats["vine"] = Nb("NB_Hedge", Cel("Nagisa_NB3_Vine", Srgb(60, 112, 52), 0.05f, 0.02f));
        _hwMats["bloom"] = Nb("NB_Bougainvillea", Cel("Nagisa_NB3_Bloom", Srgb(220, 80, 150), 0.05f, 0.02f));
        _hwMats["dark"] = Cel("Nagisa_NB3_TunnelDark", Srgb(16, 17, 20), 0f, 0f, 0f, null, null, 0.8f, 0f);
        _hwMats["glow"] = NbGlow("NB3_StreetLamp", new Color(1f, 0.92f, 0.74f, 1f));
        _hwMats["rail"] = Cel("Nagisa_NB3_Railing", Srgb(214, 218, 220), 0.55f, 0.4f);
        for (int k = 0; k < 6; k++)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/Highway/NB3_Sign_{k}.png");
            _hwMats["sign" + k] = Cel($"Nagisa_NB3_Sign_{k}", Color.white, 0.3f, 0.1f, 0.05f, tex);
        }
    }

    // ============================================================== stage

    [NagisaStage(90, "Highway")]
    private static void BuildHighwayStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();   // standalone MR_NB_STAGES run: reload the shared NB_* assets
        _hwBufs.Clear(); _hwChunk = 0;
        HwMaterials();
        var root = group.root;
        var obstacles = HwObstacles(root, group);
        Debug.Log($"[nb3] {obstacles.Count} foreign obstacle renderers indexed.");

        var sections = new[] { ("West", 0f, _route.ClimbStartM), ("East", _route.CoastStartM, _route.BridgeStartM) };
        int totalVehicles = 0, aboveGround = 0, openStations = 0, allStations = 0;
        var conflictLog = new Dictionary<string, (int n, float d0, float d1)>();
        var rng = new System.Random(3303);
        int footbridges = 0;
        foreach (var (sname, d0, d1) in sections)
        {
            var sg = new GameObject($"NB3 {sname} Coast Highway").transform; sg.SetParent(group, false);
            var st = HwStations(d0, d1);
            if (st.Count < 20) { Debug.LogWarning($"[nb3] section {sname}: too short ({st.Count})."); continue; }
            HwClassify(st, obstacles, conflictLog, d0, d1);
            allStations += st.Count;
            var runs = HwRuns(st);
            int cleared = HwClearVegetation(st, obstacles);
            if (cleared > 0) Debug.Log($"[nb3] section {sname}: deactivated {cleared} base-scene plants standing in the highway band (side rule).");
            foreach (var (a, b) in runs) openStations += b - a + 1;
            Debug.Log($"[nb3] section {sname} {d0:0}-{d1:0} m: {st.Count} stations, open runs: " +
                      string.Join(", ", runs.Select(r => $"{st[r.Item1].d:0}-{st[r.Item2].d:0}")));

            var roadT = new GameObject("Road").transform; roadT.SetParent(sg, false);
            foreach (var (a, b) in runs)
            {
                for (int c0 = a; c0 < b; c0 += 50)
                {
                    int c1 = Mathf.Min(b, c0 + 50);
                    HwRoadChunk(st, c0, c1);
                    HwFlush(roadT, sname);
                }
                HwPortal(st, a, -1); HwPortal(st, b, +1);
                HwFlush(roadT, sname + "Portal");
                HwGantries(st, a, b, roadT, sname);
            }
            var plantT = new GameObject("Oleander + Palms").transform; plantT.SetParent(sg, false);
            foreach (var (a, b) in runs) HwPlants(st, a, b, plantT, obstacles, rng);
            if (footbridges < HwFootbridges)
                footbridges += HwFootbridgesIn(st, runs, obstacles, roadT, HwFootbridges - footbridges);

            var trafT = new GameObject("Traffic").transform; trafT.SetParent(sg, false);
            var (n, up) = HwTraffic(st, runs, trafT, sname, rng);
            totalVehicles += n; aboveGround += up;
        }

        foreach (var kv in conflictLog.OrderByDescending(k => k.Value.n).Take(12))
            Debug.Log($"[nb3] conflict: {kv.Value.n} station hits by '{kv.Key}' at {kv.Value.d0:0}-{kv.Value.d1:0} m " +
                      "(highway runs in a cut-and-cover tunnel there).");
        Debug.Log($"[nb3] highway: {openStations}/{allStations} stations at grade ({openStations * HwStepM / 1000f:0.00} km), " +
                  $"{footbridges} footbridges, {totalVehicles} vehicles on W0 movers ({aboveGround} starting above ground), " +
                  $"{HwSlowKmh:0}/{HwFastKmh:0} km/h, cull {HwCullM:0} m.");
    }

    // ============================================================== stations

    private static void HwAt(float d, out Vector3 p)
    {
        int lo = 0, hi = _route.Count - 1;
        d = Mathf.Clamp(d, 0f, _route.Length);
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (_route.Distance[m] <= d) lo = m; else hi = m; }
        float span = _route.Distance[hi] - _route.Distance[lo];
        float f = span > 1e-4f ? Mathf.Clamp01((d - _route.Distance[lo]) / span) : 0f;
        p = Vector3.Lerp(_route.Position[lo], _route.Position[hi], f);
    }

    private static List<HwSt> HwStations(float d0, float d1)
    {
        var list = new List<HwSt>();
        int votes = 0;
        for (float d = d0; d <= d1; d += HwStepM)
        {
            HwAt(d, out var p); HwAt(d - 8f, out var pa); HwAt(d + 8f, out var pb);
            var t = pb - pa; t.y = 0f; t = t.sqrMagnitude > 1e-6f ? t.normalized : Vector3.forward;
            list.Add(new HwSt { d = d, p = p, t = t });
            int i = _route.IndexAt(d);
            var right = new Vector3(t.z, 0f, -t.x);
            votes += Vector3.Dot(_route.SideFlat(i) * SeaSign(i), right) > 0f ? 1 : -1;
        }
        float seaRight = votes >= 0 ? 1f : -1f;
        for (int k = 0; k < list.Count; k++)
        {
            var s = list[k];
            s.inl = -new Vector3(s.t.z, 0f, -s.t.x) * seaRight;
            float gmax = float.MinValue;
            for (float o = HwDeck0; o <= HwDeck1 + 0.01f; o += 4f)
            {
                var q = s.p + s.inl * o;
                gmax = Mathf.Max(gmax, _ground.Height(q.x, q.z));
            }
            s.gmax = gmax;
            list[k] = s;
        }
        // deck height: never under the ground across the band; max-filter then smooth
        var mx = new float[list.Count];
        for (int k = 0; k < list.Count; k++)
        {
            float m = float.MinValue;
            for (int j = Mathf.Max(0, k - 3); j <= Mathf.Min(list.Count - 1, k + 3); j++) m = Mathf.Max(m, list[j].gmax);
            mx[k] = m;
        }
        for (int k = 0; k < list.Count; k++)
        {
            float sum = 0f; int n = 0;
            for (int j = Mathf.Max(0, k - 4); j <= Mathf.Min(list.Count - 1, k + 4); j++) { sum += mx[j]; n++; }
            var s = list[k];
            s.y = Mathf.Max(sum / n, s.gmax) + HwLift;
            list[k] = s;
        }
        return list;
    }

    private struct HwOb { public Bounds b; public string owner; public bool veg; public Transform inst; }
    private static readonly HashSet<Transform> _hwVegHit = new HashSet<Transform>();
    private static readonly string[] HwVegWords = { "Palm", "Tree", "Canopy", "Shrub", "Bush", "Fern", "Grass", "Flower", "Hedge" };

    private static Dictionary<(int, int), List<HwOb>> HwObstacles(Transform root, Transform self)
    {
        var grid = new Dictionary<(int, int), List<HwOb>>();
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.transform.IsChildOf(self) || !HwActiveBelow(r.transform, root)) continue;
            var b = r.bounds;
            if (b.size.x > 70f || b.size.z > 70f || b.size.y < 1.8f) continue;
            string owner = HwOwner(r.transform, root);
            var inst = HwInstance(r.transform, root);
            var ob = new HwOb { b = b, owner = owner, inst = inst, veg = HwVegWords.Any(w => owner.Contains(w)) };
            for (int gx = Mathf.FloorToInt(b.min.x / 16f); gx <= Mathf.FloorToInt(b.max.x / 16f); gx++)
                for (int gz = Mathf.FloorToInt(b.min.z / 16f); gz <= Mathf.FloorToInt(b.max.z / 16f); gz++)
                {
                    if (!grid.TryGetValue((gx, gz), out var l)) grid[(gx, gz)] = l = new List<HwOb>();
                    l.Add(ob);
                }
            n++;
        }
        return grid;
    }

    private static bool HwActiveBelow(Transform t, Transform root)
    {
        for (var c = t; c != null && c != root; c = c.parent) if (!c.gameObject.activeSelf) return false;
        return true;
    }

    /// <summary>The placed instance: the ancestor three levels under the environment root.</summary>
    private static Transform HwInstance(Transform t, Transform root)
    {
        var chain = new List<Transform>();
        for (var c = t; c != null && c != root; c = c.parent) chain.Add(c);
        chain.Reverse();
        return chain.Count >= 3 ? chain[2] : t;
    }

    private static string HwOwner(Transform t, Transform root)
    {
        var names = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) names.Add(c.name);
        names.Reverse();
        return string.Join("/", names.Take(Mathf.Min(3, names.Count)));
    }

    private static bool HwHit(Dictionary<(int, int), List<HwOb>> grid, Vector3 q, float y0, float y1, out string owner, bool collectVeg = false)
    {
        owner = null;
        bool hit = false;
        if (!grid.TryGetValue((Mathf.FloorToInt(q.x / 16f), Mathf.FloorToInt(q.z / 16f)), out var l)) return false;
        foreach (var o in l)
        {
            var b = o.b;
            if (q.x < b.min.x - 0.4f || q.x > b.max.x + 0.4f || q.z < b.min.z - 0.4f || q.z > b.max.z + 0.4f) continue;
            if (b.max.y < y0 || b.min.y > y1) continue;
            if (collectVeg) { if (o.veg) _hwVegHit.Add(o.inst); else { owner = o.owner; hit = true; } continue; }
            owner = o.owner; return true;
        }
        return hit;
    }

    private static void HwClassify(List<HwSt> st, Dictionary<(int, int), List<HwOb>> grid,
                                   Dictionary<string, (int n, float d0, float d1)> log, float d0, float d1)
    {
        for (int k = 0; k < st.Count; k++)
        {
            var s = st[k];
            string why = null;
            if (s.d < d0 + HwEndTunnelM || s.d > d1 - HwEndTunnelM) why = "end";
            if (why == null && s.y - s.p.y > 12f) why = "cliff";
            if (why == null && k > 0 && Mathf.Abs(s.y - st[k - 1].y) / HwStepM > HwMaxGrade) why = "steep";
            for (float o = HwRailNear; why == null && o <= HwWallO + 0.6f; o += 2f)
            {
                var q = s.p + s.inl * o;
                if (_ground.Coast(q.x, q.z) < 3f) { why = "sea"; break; }
                if (HwHit(grid, q, s.y - 2f, s.y + 9f, out var owner, collectVeg: true))
                {
                    why = "obstacle";
                    log.TryGetValue(owner, out var e);
                    log[owner] = (e.n + 1, e.n == 0 ? s.d : Mathf.Min(e.d0, s.d), Mathf.Max(e.d1, s.d));
                }
            }
            s.blocked = why != null; s.why = why;
            st[k] = s;
        }
        // dilate blocked by the portal pad, then close short open runs
        int pad = Mathf.CeilToInt(HwPortalPadM / HwStepM);
        var src = st.Select(x => x.blocked).ToArray();
        for (int k = 0; k < st.Count; k++)
        {
            if (src[k]) continue;
            for (int j = Mathf.Max(0, k - pad); j <= Mathf.Min(st.Count - 1, k + pad); j++)
                if (src[j]) { var s = st[k]; s.blocked = true; s.why = "pad"; st[k] = s; break; }
        }
        int minOpen = Mathf.CeilToInt(HwMinOpenM / HwStepM);
        for (int k = 0; k < st.Count;)
        {
            if (st[k].blocked) { k++; continue; }
            int e = k; while (e + 1 < st.Count && !st[e + 1].blocked) e++;
            if (e - k + 1 < minOpen)
                for (int j = k; j <= e; j++) { var s = st[j]; s.blocked = true; s.why = "short"; st[j] = s; }
            k = e + 1;
        }
    }

    /// <summary>Side rule: the -8..-35 m band belongs to the highway, so base-scene vegetation standing on
    /// the open deck/wall line is deactivated (scene object only; no other package's file is edited).</summary>
    private static int HwClearVegetation(List<HwSt> st, Dictionary<(int, int), List<HwOb>> grid)
    {
        int n = 0;
        foreach (var s in st)
        {
            if (s.blocked) continue;
            _hwVegHit.Clear();
            for (float o = HwRailNear - 1f; o <= HwWallO + 1.2f; o += 1.5f)
                HwHit(grid, s.p + s.inl * o, s.y - 2f, s.y + 9f, out _, collectVeg: true);
            foreach (var t in _hwVegHit) if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); n++; }
        }
        _hwVegHit.Clear();
        return n;
    }

    private static List<(int, int)> HwRuns(List<HwSt> st)
    {
        var runs = new List<(int, int)>();
        for (int k = 0; k < st.Count;)
        {
            if (st[k].blocked) { k++; continue; }
            int e = k; while (e + 1 < st.Count && !st[e + 1].blocked) e++;
            runs.Add((k, e)); k = e + 1;
        }
        return runs;
    }

    private static Vector3 W(HwSt s, float o, float dy) { var q = s.p + s.inl * o; q.y = s.y + dy; return q; }

    // ============================================================== road geometry

    private static void HwRibbon(string kind, HwSt a, HwSt b, float o0, float o1, float dy, float uScale = 4f)
    {
        B(kind).Quad(W(a, o0, dy), W(a, o1, dy), W(b, o1, dy), W(b, o0, dy), Vector3.up,
                     a.d / uScale, b.d / uScale, o0 / uScale, o1 / uScale);
    }

    private static void HwWall(string kind, HwSt a, HwSt b, float o, float y0a, float y1a, float y0b, float y1b, Vector3 face)
    {
        var pa = a.p + a.inl * o; var pb = b.p + b.inl * o;
        B(kind).Quad(new Vector3(pa.x, y0a, pa.z), new Vector3(pa.x, y1a, pa.z), new Vector3(pb.x, y1b, pb.z),
                     new Vector3(pb.x, y0b, pb.z), face, a.d / 4f, b.d / 4f, y0a / 4f, y1a / 4f);
    }

    private static void HwRoadChunk(List<HwSt> st, int c0, int c1)
    {
        for (int k = c0; k < c1; k++)
        {
            var a = st[k]; var b = st[k + 1];
            var dir = (b.p - a.p); dir.y = 0f; float len = dir.magnitude; if (len < 0.1f) continue; dir /= len;
            HwRibbon("asphalt", a, b, HwDeck0, HwMedian0, 0f);
            HwRibbon("asphalt", a, b, HwMedian1, HwDeck1, 0f);
            HwRibbon("median", a, b, HwMedian0, HwMedian1, 0.18f, 3f);
            HwWall("concrete", a, b, HwMedian0, a.y, a.y + 0.18f, b.y, b.y + 0.18f, -a.inl);
            HwWall("concrete", a, b, HwMedian1, a.y, a.y + 0.18f, b.y, b.y + 0.18f, a.inl);
            // embankment / retaining skirts down to the ground on both deck edges
            HwSkirt(a, b, HwDeck0, -a.inl);
            HwSkirt(a, b, HwDeck1, a.inl);

            // markings: solid edge lines, 6 m dash / 12 m gap lane lines (Japan-style)
            foreach (var o in HwLineSolid) HwRibbon("lines", a, b, o - 0.08f, o + 0.08f, 0.02f);
            if (k % 3 == 0) foreach (var o in HwLineDash) HwRibbon("lines", a, b, o - 0.075f, o + 0.075f, 0.02f);

            // guardrails (W-beam + posts) on both outer edges
            foreach (var o in new[] { HwRailNear, HwRailFar })
            {
                var ma = W(a, o, 0.72f); var mb = W(b, o, 0.72f);
                B("metal").Box((ma + mb) * 0.5f, a.inl, dir, 0.1f, 0.32f, len + 0.04f);
                var pp = W(a, o + (o < 10f ? 0.12f : -0.12f), 0.46f);
                if (o < 10f) pp.y = Mathf.Min(pp.y, _ground.Height(pp.x, pp.z) + 0.46f);
                B("metal").Box(pp, a.inl, dir, 0.12f, 0.92f, 0.12f);
            }

            // median barrier: sloped concrete profile + anti-glare slats
            {
                float bw = 0.3f, tw = 0.11f, h = 0.18f + 0.86f;
                HwRibbon("concrete", a, b, HwBarrierO - tw, HwBarrierO + tw, h);
                B("concrete").Quad(W(a, HwBarrierO - bw, 0.18f), W(a, HwBarrierO - tw, h), W(b, HwBarrierO - tw, h),
                                   W(b, HwBarrierO - bw, 0.18f), -a.inl + Vector3.up * 0.2f, a.d / 4f, b.d / 4f, 0f, 0.3f);
                B("concrete").Quad(W(a, HwBarrierO + bw, 0.18f), W(a, HwBarrierO + tw, h), W(b, HwBarrierO + tw, h),
                                   W(b, HwBarrierO + bw, 0.18f), a.inl + Vector3.up * 0.2f, a.d / 4f, b.d / 4f, 0f, 0.3f);
                var rot = Quaternion.AngleAxis(32f, Vector3.up);
                for (int s = 0; s < 5; s++)
                {
                    var c = Vector3.Lerp(W(a, HwBarrierO, h + 0.42f), W(b, HwBarrierO, h + 0.42f), (s + 0.5f) / 5f);
                    B("antiglare").Box(c, rot * a.inl, rot * dir, 0.26f, 0.84f, 0.035f);
                }
            }

            // median twin street lights
            if (Mathf.Repeat(a.d, HwLightSpacingM) < HwStepM)
            {
                var foot = W(a, HwBarrierO, 1.04f);
                B("metal").Box(foot + Vector3.up * (HwLightH * 0.5f), a.inl, dir, 0.24f, HwLightH, 0.24f);
                foreach (var sgn in new[] { -1f, 1f })
                {
                    var arm = W(a, HwBarrierO + sgn * HwLightArm * 0.5f, 1.04f + HwLightH - 0.1f);
                    B("metal").Box(arm, a.inl, dir, HwLightArm, 0.14f, 0.14f);
                    var head = W(a, HwBarrierO + sgn * (HwLightArm - 0.3f), 1.04f + HwLightH - 0.28f);
                    B("metal").Box(head + Vector3.up * 0.1f, a.inl, dir, 0.9f, 0.16f, 0.42f);
                    B("glow").Box(head - Vector3.up * 0.02f, a.inl, dir, 0.78f, 0.06f, 0.32f);
                }
            }

            // sound wall on its own ground line + posts + vines
            {
                var pa = a.p + a.inl * HwWallO; var pb = b.p + b.inl * HwWallO;
                float ga = Mathf.Min(_ground.Height(pa.x, pa.z), a.y) - 0.3f, gb = Mathf.Min(_ground.Height(pb.x, pb.z), b.y) - 0.3f;
                float ta = Mathf.Max(a.y, ga + 0.3f) + HwWallH, tb = Mathf.Max(b.y, gb + 0.3f) + HwWallH;
                HwWall("concrete", a, b, HwWallO - 0.22f, ga, ga + 1.0f, gb, gb + 1.0f, -a.inl);
                HwWall("wall", a, b, HwWallO - 0.1f, ga + 1.0f, ta, gb + 1.0f, tb, -a.inl);
                HwWall("wall", a, b, HwWallO + 0.1f, ga, ta, gb, tb, a.inl);
                B("wall").Quad(new Vector3(pa.x, ta, pa.z) - a.inl * 0.1f, new Vector3(pa.x, ta, pa.z) + a.inl * 0.1f,
                               new Vector3(pb.x, tb, pb.z) + b.inl * 0.1f, new Vector3(pb.x, tb, pb.z) - b.inl * 0.1f,
                               Vector3.up, 0f, 1f, 0f, 0.05f);
                if (k % 2 == 0)
                    B("metal").Box(new Vector3(pa.x, (ga + ta) * 0.5f, pa.z) - a.inl * 0.18f, a.inl, dir, 0.22f, ta - ga + 0.2f, 0.3f);
                // vines: deterministic patches on the highway face (hash of the station distance)
                float hsh = Mathf.Repeat(Mathf.Sin(a.d * 12.9898f) * 43758.55f, 1f);
                if (hsh < 0.62f)
                {
                    float vh = 1.4f + hsh * 4.2f;
                    HwWall(hsh < 0.18f ? "bloom" : "vine", a, b, HwWallO - 0.3f, ga + 0.6f, Mathf.Min(ta, ga + 0.6f + vh),
                           gb + 0.6f, Mathf.Min(tb, gb + 0.6f + vh * 0.8f), -a.inl);
                }
            }
        }
    }

    private static void HwSkirt(HwSt a, HwSt b, float o, Vector3 face)
    {
        var pa = a.p + a.inl * o; var pb = b.p + b.inl * o;
        float ga = Mathf.Min(_ground.Height(pa.x, pa.z), a.y) - 0.6f, gb = Mathf.Min(_ground.Height(pb.x, pb.z), b.y) - 0.6f;
        HwWall("concrete", a, b, o, ga, a.y, gb, b.y, face);
    }

    /// <summary>Cut-and-cover tunnel portal at station k; side = -1 tube extends backwards (run start), +1 forwards.</summary>
    private static void HwPortal(List<HwSt> st, int k, int side)
    {
        var s = st[k];
        var into = s.t * side;
        float half = 0.5f;
        Vector3 At(float o, float dy, float along) => W(s, o, dy) + into * along;
        // headwall: 3 piers + 2 lintels
        foreach (var (o0, o1) in new[] { (7.3f, 8.3f), (17.2f, 22.8f), (31.7f, 32.7f) })
            B("concrete").Box(At((o0 + o1) * 0.5f, HwPortalH * 0.5f - 0.3f, half), s.inl, s.t, o1 - o0, HwPortalH + 0.6f, 1.0f);
        foreach (var (o0, o1) in new[] { (8.3f, 17.2f), (22.8f, 31.7f) })
        {
            B("concrete").Box(At((o0 + o1) * 0.5f, (HwPortalClearH + HwPortalH) * 0.5f, half), s.inl, s.t, o1 - o0, HwPortalH - HwPortalClearH, 1.0f);
            // dark tube: walls, ceiling, floor, end cap
            foreach (var o in new[] { o0, o1 })
                B("dark").Quad(At(o, 0f, 0f), At(o, HwPortalClearH, 0f), At(o, HwPortalClearH, HwTubeM), At(o, 0f, HwTubeM),
                               o == o0 ? s.inl : -s.inl, 0f, 1f, 0f, 1f);
            B("dark").Quad(At(o0, HwPortalClearH, 0f), At(o1, HwPortalClearH, 0f), At(o1, HwPortalClearH, HwTubeM),
                           At(o0, HwPortalClearH, HwTubeM), -Vector3.up, 0f, 1f, 0f, 1f);
            B("dark").Quad(At(o0, 0.01f, 0f), At(o1, 0.01f, 0f), At(o1, 0.01f, HwTubeM), At(o0, 0.01f, HwTubeM),
                           Vector3.up, 0f, 1f, 0f, 1f);
            B("dark").Quad(At(o0, 0f, HwTubeM), At(o1, 0f, HwTubeM), At(o1, HwPortalClearH, HwTubeM),
                           At(o0, HwPortalClearH, HwTubeM), -into, 0f, 1f, 0f, 1f);
            // tunnel lights just inside the mouth
            for (float a2 = 3f; a2 < HwTubeM; a2 += 7f)
                B("glow").Box(At((o0 + o1) * 0.5f, HwPortalClearH - 0.08f, a2), s.inl, s.t, 1.4f, 0.06f, 0.3f);
        }
        // cut-and-cover lid (concrete box) topped with planting, plus side walls
        B("concrete").Box(At(20f, (HwPortalClearH + HwPortalH) * 0.5f, half + HwTubeM * 0.5f), s.inl, s.t, 25.4f, HwPortalH - HwPortalClearH, HwTubeM);
        B("median").Quad(At(7.3f, HwPortalH + 0.02f, 0f), At(32.7f, HwPortalH + 0.02f, 0f), At(32.7f, HwPortalH + 0.02f, HwTubeM + 1f),
                         At(7.3f, HwPortalH + 0.02f, HwTubeM + 1f), Vector3.up, 0f, 6f, 0f, 6f);
        foreach (var o in new[] { 7.3f, 32.7f })
            B("concrete").Quad(At(o, -1f, 1f), At(o, HwPortalH, 1f), At(o, HwPortalH, HwTubeM + 1f), At(o, -1f, HwTubeM + 1f),
                               o < 20f ? -s.inl : s.inl, 0f, 6f, 0f, 2f);
    }

    // ============================================================== gantries, plants, footbridges

    private static int _hwSignIdx;

    private static void HwGantries(List<HwSt> st, int a, int b, Transform parent, string sname)
    {
        float runLen = st[b].d - st[a].d;
        if (runLen < 260f) return;
        for (float g = st[a].d + 130f; g < st[b].d - 120f; g += HwGantrySpacingM)
        {
            int k = a + Mathf.RoundToInt((g - st[a].d) / HwStepM);
            var s = st[k];
            foreach (var o in new[] { HwDeck0 + 0.5f, HwDeck1 - 0.5f })
                B("metal").Box(W(s, o, 4.0f), s.inl, s.t, 0.5f, 8.0f, 0.5f);
            foreach (var h in new[] { 7.05f, 7.85f })
                B("metal").Box(W(s, 20f, h), s.inl, s.t, HwDeck1 - HwDeck0 - 1f, 0.24f, 0.3f);
            for (float o = HwDeck0 + 1.5f; o < HwDeck1 - 1f; o += 1.5f)
                B("metal").Box(W(s, o, 7.45f), s.inl, s.t, 0.08f, 0.8f, 0.08f);
            HwFlush(parent, sname + "Gantry");
            // one sign per carriageway, facing the traffic that approaches it
            bool fwdOnB = HwForwardOnB(s);
            foreach (var (o, onB) in new[] { (13.1f, false), (26.9f, true) })
            {
                var travel = (onB == fwdOnB) ? s.t : -s.t;
                var face = -travel;
                var c = W(s, o, 6.25f) + face * 0.28f;
                B("metal").Box(c - face * 0.05f, s.inl, s.t, 7.0f, 2.7f, 0.1f);
                string kind = "sign" + (_hwSignIdx++ % 6);
                var right = Vector3.Cross(Vector3.up, face).normalized;   // viewer's right when looking at the face
                var p0 = c - right * 3.4f - Vector3.up * 1.28f + face * 0.02f;
                var p1 = c + right * 3.4f - Vector3.up * 1.28f + face * 0.02f;
                // uv: u along viewer's right, v up
                B(kind).Quad(p0, p0 + Vector3.up * 2.56f, p1 + Vector3.up * 2.56f, p1, face, 0f, 1f, 0f, 1f);
                FixSignUv(B(kind));
            }
            HwFlush(parent, sname + "Sign");
        }
    }

    /// <summary>HwBuf.Quad writes uv (u0,v0),(u0,v1),(u1,v1),(u1,v0) for a,b,c,d; for signs a=bottom-left,
    /// b=top-left, c=top-right, d=bottom-right, which is already correct. Kept explicit for clarity.</summary>
    private static void FixSignUv(HwBuf b) { }

    private static bool HwForwardOnB(HwSt s)
    {
        // left-hand traffic: route-forward traffic keeps LEFT, i.e. on the carriageway on its left.
        var left = -new Vector3(s.t.z, 0f, -s.t.x);
        return Vector3.Dot(left, s.inl) > 0f;   // inland is on the left -> the far (B) carriageway
    }

    private static void HwPlants(List<HwSt> st, int a, int b, Transform parent,
                                 Dictionary<(int, int), List<HwOb>> grid, System.Random rng)
    {
        var pink = new Dictionary<string, Material> { { "NB_Hedge", Nb("NB_Bougainvillea", null) } };
        float lastO = -999f, lastP = -999f; int n = 0;
        for (int k = a; k <= b; k++)
        {
            var s = st[k];
            if (s.d - lastO >= HwOleanderM)
            {
                lastO = s.d;
                float o = (n % 2 == 0) ? HwBarrierO - 1.45f : HwBarrierO + 1.45f;
                var go = PlaceWorld("Nagisa_S_HedgeSeg", parent, W(s, o, 0.18f),
                                    Mathf.Atan2(s.t.x, s.t.z) * Mathf.Rad2Deg + 90f, 1f, SmallPropCull,
                                    (n % 3 != 2 && pink["NB_Hedge"] != null) ? pink : null);
                if (go != null) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                n++;
            }
            if (s.d - lastP >= HwPalmSpacingM)
            {
                lastP = s.d;
                var q = s.p + s.inl * (HwPalmO + (float)rng.NextDouble() * 2.5f);
                if (_ground.Coast(q.x, q.z) < 4f || InAnyPad(q.x, q.z, 2f) || HwHit(grid, q, -999f, 999f, out _)) continue;
                q.y = _ground.Height(q.x, q.z) - 0.05f;
                string stem = rng.NextDouble() < 0.5 ? "Nagisa_CoconutPalm_A" : (rng.NextDouble() < 0.6 ? "Nagisa_CoconutPalm_B" : "Nagisa_FanPalm");
                PlaceWorld(stem, parent, q, (float)rng.NextDouble() * 360f, 0.9f + (float)rng.NextDouble() * 0.25f);
            }
        }
    }

    private static int HwFootbridgesIn(List<HwSt> st, List<(int, int)> runs, Dictionary<(int, int), List<HwOb>> grid,
                                       Transform parent, int want)
    {
        // candidates: the middle of the longest open runs, spaced >= 600 m
        var cands = new List<int>();
        foreach (var (a, b) in runs.OrderByDescending(r => r.Item2 - r.Item1))
            if (st[b].d - st[a].d > 400f) { cands.Add((a + b) / 2); cands.Add(a + (b - a) / 4); cands.Add(a + 3 * (b - a) / 4); }
        int built = 0; var used = new List<float>();
        foreach (var k in cands)
        {
            if (built >= want) break;
            var s = st[k];
            if (used.Any(u => Mathf.Abs(u - s.d) < 600f)) continue;
            // stair footprints: sea side on the beach edge beyond the promenade, land side beyond the palm buffer
            float seaO = -13.7f, landO = 44.5f;
            bool clear = true;
            for (float along = -1f; along <= 14f && clear; along += 3f)
                foreach (var o in new[] { seaO, landO })
                {
                    var q = s.p + s.inl * o + s.t * along;
                    if (HwHit(grid, q, -999f, 999f, out _) || _ground.Coast(q.x, q.z) < 2f) { clear = false; break; }
                }
            if (!clear) continue;
            HwFootbridge(s, seaO, landO, parent, built);
            used.Add(s.d); built++;
        }
        return built;
    }

    private static void HwFootbridge(HwSt s, float seaO, float landO, Transform parent, int idx)
    {
        float deckY = Mathf.Max(s.y, s.p.y) + 6.6f;
        Vector3 P(float o, float along, float y) { var q = s.p + s.inl * o + s.t * along; q.y = y; return q; }
        float span = landO - seaO;
        // deck slab + glass-ish railings + roof canopy
        B("concrete").Box(P((seaO + landO) * 0.5f, 0f, deckY - 0.25f), s.inl, s.t, span + 2f, 0.5f, 3.2f);
        foreach (var sgn in new[] { -1f, 1f })
        {
            B("rail").Box(P((seaO + landO) * 0.5f, sgn * 1.55f, deckY + 0.55f), s.inl, s.t, span + 2f, 1.1f, 0.06f);
            B("metal").Box(P((seaO + landO) * 0.5f, sgn * 1.55f, deckY + 1.12f), s.inl, s.t, span + 2f, 0.08f, 0.1f);
        }
        B("rail").Box(P((seaO + landO) * 0.5f, 0f, deckY + 2.9f), s.inl, s.t, span + 2f, 0.12f, 3.6f);
        for (float o = seaO; o <= landO + 0.1f; o += span / 6f)
            foreach (var sgn in new[] { -1f, 1f })
                B("metal").Box(P(o, sgn * 1.6f, deckY + 1.45f), s.inl, s.t, 0.12f, 2.9f, 0.12f);
        // piers: sea tower, median, land tower (never inside the ride corridor)
        foreach (var o in new[] { seaO, HwBarrierO, landO })
        {
            var q = P(o, 0f, 0f);
            float g = o == HwBarrierO ? s.y + 1.04f : _ground.Height(q.x, q.z) - 0.2f;
            B("concrete").Box(P(o, 0f, (g + deckY - 0.5f) * 0.5f), s.inl, s.t, 1.0f, deckY - 0.5f - g, 1.0f);
        }
        // straight stairs running along the road, down from each end of the deck
        foreach (var (o, sgn) in new[] { (seaO, -1f), (landO, 1f) })
        {
            var qg = P(o, 0f, 0f);
            float g = _ground.Height(qg.x, qg.z);
            float rise = deckY - g; int steps = Mathf.Max(8, Mathf.RoundToInt(rise / 0.17f));
            float run = steps * 0.29f;
            for (int i = 0; i < steps; i++)
            {
                float along = 2.0f + i * 0.29f;
                float y = deckY - (i + 1) * rise / steps;
                B("concrete").Box(P(o + sgn * 0.0f, along, y + 0.09f), s.inl, s.t, 1.8f, 0.18f, 0.3f);
            }
            B("concrete").Box(P(o, 1.0f, deckY - 0.25f), s.inl, s.t, 2.2f, 0.5f, 2.2f);                 // landing
            foreach (var e in new[] { -1f, 1f })
            {
                var top = P(o + e * 0.95f, 1.8f, deckY + 0.9f); var bot = P(o + e * 0.95f, 2.0f + run, g + 0.9f);
                var mid = (top + bot) * 0.5f; var dir = bot - top;
                B("metal").Box(mid, s.inl, dir.normalized, 0.06f, 0.08f, dir.magnitude);
            }
        }
        HwFlush(parent, "Footbridge" + idx);
    }

    // ============================================================== traffic

    private static readonly Dictionary<string, (float L, float W, float lampY)> HwDims = new Dictionary<string, (float, float, float)>
    {
        { "Nagisa_S_CarKei", (3.4f, 1.47f, 0.74f) }, { "Nagisa_S_CarSedan", (4.6f, 1.78f, 0.70f) },
        { "Nagisa_S_CarTaxi", (4.6f, 1.78f, 0.70f) }, { "Nagisa_S_CarVan", (4.7f, 1.8f, 0.80f) },
        { "Nagisa_NB3_Coach", (12.0f, 2.5f, 0.95f) }, { "Nagisa_NB3_CityBus", (10.5f, 2.5f, 0.90f) },
        { "Nagisa_NB3_BoxTruck", (7.4f, 2.3f, 0.85f) }, { "Nagisa_NB3_SurfSUV", (4.8f, 1.9f, 0.86f) },
        { "Nagisa_NB3_Convertible", (4.5f, 1.8f, 0.68f) }, { "Nagisa_NB3_Camper", (5.6f, 2.05f, 0.90f) }, { "Nagisa_NB3_Motorbike", (2.1f, 0.6f, 0.78f) },
        // G traffic polish (brief 12): luxury saloon, resort shuttle, delivery van, scooter (tools/blender/build_nagisa_marina_traffic.py)
        { "Nagisa_MR_LuxurySedan", (5.1f, 1.9f, 0.70f) }, { "Nagisa_MR_Shuttle", (6.4f, 2.05f, 0.95f) },
        { "Nagisa_MR_DeliveryVan", (4.6f, 1.7f, 0.86f) }, { "Nagisa_MR_Scooter", (1.85f, 0.7f, 0.80f) },
    };

    // Moderate, mixed density (brief 12): mostly kei / compact / sedans, some luxury cars, resort shuttles, taxis, scooters,
    // delivery vans, SUVs and convertibles on the fast lanes; the slow lanes carry shuttles, delivery vans and the occasional bus.
    private static readonly (string stem, float w)[] HwFastMix =
    {
        ("Nagisa_S_CarSedan", 18), ("Nagisa_S_CarKei", 18), ("Nagisa_S_CarTaxi", 10), ("Nagisa_NB3_SurfSUV", 12),
        ("Nagisa_NB3_Convertible", 9), ("Nagisa_S_CarVan", 7), ("Nagisa_NB3_Coach", 2), ("Nagisa_NB3_Motorbike", 6),
        ("Nagisa_MR_LuxurySedan", 12), ("Nagisa_MR_Scooter", 8), ("Nagisa_MR_Shuttle", 6), ("Nagisa_MR_DeliveryVan", 5),
    };
    private static readonly (string stem, float w)[] HwSlowMix =
    {
        ("Nagisa_S_CarVan", 10), ("Nagisa_S_CarKei", 16), ("Nagisa_S_CarSedan", 10), ("Nagisa_NB3_CityBus", 5),
        ("Nagisa_NB3_Coach", 3), ("Nagisa_NB3_BoxTruck", 8), ("Nagisa_NB3_Camper", 6), ("Nagisa_NB3_SurfSUV", 6),
        ("Nagisa_S_CarTaxi", 8), ("Nagisa_NB3_Motorbike", 4), ("Nagisa_MR_Shuttle", 14), ("Nagisa_MR_DeliveryVan", 14),
        ("Nagisa_MR_LuxurySedan", 6), ("Nagisa_MR_Scooter", 8),
    };
    private static readonly int[] HwLuxuryPaints = { 0, 1, 2, 2, 3 };
    private static readonly int[] HwShuttlePaints = { 0, 0, 5, 8 };
    private static readonly int[] HwDeliveryPaints = { 0, 0, 6, 5, 3 };

    private static readonly Color[] HwPaints =
    {
        new Color(0.93f, 0.93f, 0.91f), new Color(0.70f, 0.72f, 0.74f), new Color(0.10f, 0.11f, 0.13f),
        new Color(0.12f, 0.26f, 0.55f), new Color(0.72f, 0.12f, 0.10f), new Color(0.14f, 0.55f, 0.56f),
        new Color(0.93f, 0.78f, 0.22f), new Color(0.80f, 0.74f, 0.62f), new Color(0.56f, 0.80f, 0.70f),
        new Color(0.95f, 0.55f, 0.35f),
    };

    private static bool GlbExists(string stem) => File.Exists($"{ModelDir}/{stem}.glb");

    private static string HwPick((string stem, float w)[] mix, System.Random rng)
    {
        var ok = mix.Where(m => GlbExists(m.stem)).ToArray();
        float sum = ok.Sum(m => m.w), r = (float)rng.NextDouble() * sum;
        foreach (var m in ok) { r -= m.w; if (r <= 0f) return m.stem; }
        return ok.Length > 0 ? ok[ok.Length - 1].stem : "Nagisa_S_CarSedan";
    }

    private static (int n, int up) HwTraffic(List<HwSt> st, List<(int, int)> runs, Transform parent, string sname, System.Random rng)
    {
        if (runs.Count == 0) { Debug.LogWarning($"[nb3] {sname}: no open highway - no traffic."); return (0, 0); }
        // path height per station: deck where open; underground (ramped) where tunnelled
        var open = st.Select(x => !x.blocked).ToArray();
        var distOpen = new float[st.Count]; var yOpen = new float[st.Count];
        for (int k = 0; k < st.Count; k++) { distOpen[k] = open[k] ? 0f : 1e9f; yOpen[k] = st[k].y; }
        for (int k = 1; k < st.Count; k++) if (!open[k] && distOpen[k - 1] + HwStepM < distOpen[k]) { distOpen[k] = distOpen[k - 1] + HwStepM; yOpen[k] = yOpen[k - 1]; }
        for (int k = st.Count - 2; k >= 0; k--) if (!open[k] && distOpen[k + 1] + HwStepM < distOpen[k]) { distOpen[k] = distOpen[k + 1] + HwStepM; yOpen[k] = yOpen[k + 1]; }
        float PathY(int k)
        {
            if (open[k]) return st[k].y + 0.02f;
            float ramp = Mathf.Clamp01((distOpen[k] - 14f) / 36f);
            float deep = Mathf.Min(st[k].gmax, yOpen[k]) - HwTunnelDepthM;
            return Mathf.Lerp(yOpen[k] + 0.02f, deep, ramp);
        }
        List<Vector3> Lane(float o, bool reverse)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < st.Count; i++)
            {
                int k = reverse ? st.Count - 1 - i : i;
                var q = st[k].p + st[k].inl * o; q.y = PathY(k);
                if (pts.Count > 0)
                {
                    var dirTravel = reverse ? -st[k].t : st[k].t;
                    if (Vector3.Dot(q - pts[pts.Count - 1], dirTravel) < 0.8f) continue;   // fold filter on tight curves
                }
                pts.Add(q);
            }
            return pts;
        }

        bool fwdOnB = HwForwardOnB(st[st.Count / 2]);
        var loops = new[]
        {
            ("Fast", fwdOnB ? HwLaneBfast : HwLaneAfast, fwdOnB ? HwLaneAfast : HwLaneBfast, HwFastKmh, HwFastMix),
            ("Slow", fwdOnB ? HwLaneBslow : HwLaneAslow, fwdOnB ? HwLaneAslow : HwLaneBslow, HwSlowKmh, HwSlowMix),
        };
        var paints = new List<Material>();
        for (int c = 0; c < HwPaints.Length; c++) paints.Add(Cel($"Nagisa_NB3_CarPaint_{c}", HwPaints[c], 0.62f, 0.55f, 0.22f));
        var head = NbGlow("NB3_HeadLamp", new Color(1f, 0.96f, 0.86f, 1f));
        var tail = NbGlow("NB3_TailLamp", new Color(0.95f, 0.07f, 0.04f, 1f));
        int n = 0, up = 0;
        foreach (var (lname, oFwd, oBack, kmh, mix) in loops)
        {
            var pts = Lane(oFwd, false); pts.AddRange(Lane(oBack, true));
            var arr = AmbientMoverStaging.BakePolyline(pts);
            float len = AmbientMoverStaging.GetPolylineLength(arr, closed: true);
            var lg = AmbientMoverStaging.Converge(parent, $"NB3 {sname} {lname} Loop").transform;
            float s = (float)rng.NextDouble() * 40f, prevHalf = 0f;
            while (true)
            {
                string stem = HwPick(mix, rng);
                var dims = HwDims.TryGetValue(stem, out var dd) ? dd : (4.6f, 1.8f, 0.7f);
                float gap = prevHalf + dims.Item1 * 0.5f + HwMinClearM - Mathf.Log(1f - (float)rng.NextDouble() * 0.999f) * HwMeanExtraGapM;
                s += n == 0 && prevHalf == 0f ? 0f : gap;
                if (s > len - 30f) break;
                prevHalf = dims.Item1 * 0.5f;
                var holder = AmbientMoverStaging.Converge(lg, $"NB3 {lname} {lg.childCount:000} {stem}");
                var remap = new Dictionary<string, Material> { { "NB_CarPaint", paints[rng.Next(paints.Count)] } };
                if (stem == "Nagisa_S_CarTaxi") remap = null;   // taxis keep their fleet paint
                else if (stem == "Nagisa_MR_LuxurySedan") remap["NB_CarPaint"] = paints[HwLuxuryPaints[rng.Next(HwLuxuryPaints.Length)]];
                else if (stem == "Nagisa_MR_Shuttle") remap["NB_CarPaint"] = paints[HwShuttlePaints[rng.Next(HwShuttlePaints.Length)]];
                else if (stem == "Nagisa_MR_DeliveryVan") remap["NB_CarPaint"] = paints[HwDeliveryPaints[rng.Next(HwDeliveryPaints.Length)]];
                var go = PlaceWorld(stem, holder.transform, arr[0], 0f, 1f, 0.0015f, remap);
                if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }
                var lamps = HwLamps(go.transform, stem, dims, head, tail);
                HwFixLod(go, lamps);
                var tag = holder.AddComponent<NagisaTrafficVehicle>();
                tag.kind = stem; tag.kmh = kmh; tag.section = sname; tag.loop = lname;
                var mover = holder.AddComponent<AmbientPathMover>();
                mover.mode = AmbientPathMover.PathMode.Loop;
                mover.path = arr;
                mover.cars = new[] { go.transform };
                mover.carOffsets = new[] { 0f };
                mover.carYaw = new[] { 180f };   // Nagisa GLB vehicles face -z
                mover.speed = kmh / 3.6f;
                mover.accel = 3f;
                mover.cullDistance = HwCullM;
                mover.seed = 3300 + n;
                mover.startDistance = s;
                mover.Bake();
                mover.SetHead(s);
                if (go.transform.position.y > _ground.Height(go.transform.position.x, go.transform.position.z) - 1.5f) up++;
                n++;
            }
            Debug.Log($"[nb3] {sname} {lname} loop: {lg.childCount} vehicles on {len / 1000f:0.00} km at {kmh:0} km/h.");
        }
        return (n, up);
    }

    private static readonly Dictionary<string, (Mesh h, Mesh t)> _hwLampMeshes = new Dictionary<string, (Mesh, Mesh)>();

    private static Renderer[] HwLamps(Transform car, string stem, (float L, float W, float lampY) d, Material head, Material tail)
    {
        if (!_hwLampMeshes.TryGetValue(stem, out var m) || m.h == null)
        {
            Mesh Make(bool front)
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                float z = front ? -d.L * 0.5f - 0.035f : d.L * 0.5f + 0.035f;
                float hw = d.W * 0.5f - 0.3f, w = front ? 0.36f : 0.3f, h = front ? 0.15f : 0.16f;
                foreach (var x in new[] { -hw, hw })
                {
                    int n = v.Count;
                    v.Add(new Vector3(x - w * 0.5f, d.lampY - h * 0.5f, z)); v.Add(new Vector3(x - w * 0.5f, d.lampY + h * 0.5f, z));
                    v.Add(new Vector3(x + w * 0.5f, d.lampY + h * 0.5f, z)); v.Add(new Vector3(x + w * 0.5f, d.lampY - h * 0.5f, z));
                    uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one); uv.Add(Vector2.right);
                    // front faces -z, rear faces +z
                    if (front) t.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
                    else t.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
                }
                string name = $"NB3_Lamps_{(front ? "Head" : "Tail")}_{stem}";
                var mesh = Finish(name, v, uv, t);
                string path = $"{MeshDir}/{name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            m = (Make(true), Make(false));
            _hwLampMeshes[stem] = m;
        }
        var list = new List<Renderer>();
        foreach (var (mesh, mat, nm) in new[] { (m.h, head, "Headlights"), (m.t, tail, "Taillights") })
        {
            var go = new GameObject(nm, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(car, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
            list.Add(r);
        }
        return list.ToArray();
    }

    private static void HwFixLod(GameObject go, Renderer[] extra)
    {
        var g = go.GetComponent<LODGroup>();
        if (g == null) { g = go.AddComponent<LODGroup>(); g.SetLODs(new[] { new LOD(0.001f, go.GetComponentsInChildren<Renderer>()) }); }
        var lods = g.GetLODs();
        for (int l = 0; l < Mathf.Min(2, lods.Length); l++)
        {
            var rs = new List<Renderer>(lods[l].renderers); rs.AddRange(extra); lods[l].renderers = rs.ToArray();
        }
        // cull each vehicle at ~1.5 km (60 deg vertical fov): relative height = size / (2 d tan 30)
        g.RecalculateBounds();
        float cut = g.size / (2f * HwCullM * Mathf.Tan(30f * Mathf.Deg2Rad));
        int last = lods.Length - 1;
        float prev = last > 0 ? lods[last - 1].screenRelativeTransitionHeight : 1f;
        lods[last].screenRelativeTransitionHeight = Mathf.Min(cut, prev * 0.9f);
        g.SetLODs(lods);
    }

    // ============================================================== self-test + capture

    /// <summary>Batch self-test: vehicle count, speeds, side-rule band check on every above-ground path
    /// point, and real motion (movers stepped 3 s near a reference, and frozen beyond 1.5 km).</summary>
    public static void HighwaySelfTest()
    {
        HwOpenScene();
        EnsureRouteGround();
        var hw = HwGroup();
        if (hw == null) { Debug.LogError("[nb3] selftest FAIL: no 'NB Highway' group - run NagisaBayEnvironment.Apply."); return; }
        var movers = hw.GetComponentsInChildren<AmbientPathMover>(true);
        int bad = 0, pts = 0, seaSide = 0;
        float minK = 999f, maxK = 0f;
        foreach (var m in movers)
        {
            minK = Mathf.Min(minK, m.speed * 3.6f); maxK = Mathf.Max(maxK, m.speed * 3.6f);
        }
        foreach (var lg in hw.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith(" Loop")))
        {
            var m = lg.GetComponentInChildren<AmbientPathMover>(true);
            if (m == null) continue;
            for (int i = 0; i < m.path.Length; i += 3)
            {
                var q = m.path[i];
                if (q.y < _ground.Height(q.x, q.z) - 1.5f) continue;   // underground (tunnel) section
                pts++;
                float dist = _route.PlanDistance(q.x, q.z, out int nr);
                var sd = _route.SideFlat(nr) * SeaSign(nr);
                var rel = q - _route.Position[nr]; rel.y = 0f;
                bool seaward = Vector3.Dot(rel, sd) > 0f;
                if (seaward) seaSide++;
                if (seaward || dist < 8f || dist > 32f) bad++;
            }
        }
        // motion: reference near the first West open-run vehicle
        var cars = movers.Where(m => m.cars.Length > 0 && m.cars[0] != null).ToArray();
        var refPos = cars.Length > 0 ? cars[cars.Length / 3].cars[0].position : Vector3.zero;
        AmbientCull.TargetPositionOverride = () => refPos;
        var before = cars.Select(m => m.cars[0].position).ToArray();
        foreach (var m in cars) m.ForceCullCheck(refPos);
        for (int f = 0; f < 150; f++) foreach (var m in cars) m.Step(0.1f);   // edit mode: Awake never ran, so cars ramp from 0 at accel 3 m/s^2; 15 s covers the ramp
        int movedNear = 0, near = 0, farMoved = 0, far = 0;
        for (int i = 0; i < cars.Length; i++)
        {
            float moved = Vector3.Distance(before[i], cars[i].cars[0].position);
            bool isNear = (before[i] - refPos).magnitude < HwCullM - 100f;
            if (isNear) { near++; if (moved > 60f) movedNear++; }
            else if ((before[i] - refPos).magnitude > HwCullM + 100f) { far++; if (moved > 0.01f) farMoved++; }
        }
        AmbientCull.TargetPositionOverride = null;
        int people = HwPeopleInBand();
        bool pass = movers.Length >= 120 && bad == 0 && minK >= 50f && maxK <= 80f && movedNear >= near * 0.95f && farMoved == 0;
        Debug.Log($"[nb3] selftest {(pass ? "PASS" : "FAIL")}: {movers.Length} vehicle movers, {minK:0}-{maxK:0} km/h; " +
                  $"band check {pts} above-ground path pts, {bad} outside 8-32 m inland ({seaSide} seaward); " +
                  $"motion 15 s: {movedNear}/{near} near vehicles moved >60 m, {farMoved}/{far} beyond 1.5 km moved; " +
                  $"people/riders standing in the -4.1..-42 m band (other packages): {people}.");
    }

    private static int HwPeopleInBand()
    {
        var root = GameObject.Find(RootName);
        if (root == null) return -1;
        int n = 0;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var q = smr.transform.position;
            float dist = _route.PlanDistance(q.x, q.z, out int near);
            float d = _route.Distance[near];
            bool coastal = d < _route.ClimbStartM || (d > _route.CoastStartM && d < _route.BridgeStartM);
            if (!coastal || dist < 4.1f || dist > 42f) continue;
            var sd = _route.SideFlat(near) * SeaSign(near);
            var rel = q - _route.Position[near]; rel.y = 0f;
            if (Vector3.Dot(rel, sd) < 0f && q.y < _ground.Height(q.x, q.z) + 3f) n++;
        }
        return n;
    }

    /// <summary>Captures NB3 frames (reference/good_graphics/nagisa_bay/overhaul/nb3_*.png): the side view
    /// from the ride road twice 3 s apart (motion), the east coast, and an oblique aerial.</summary>
    public static void CaptureHighway()
    {
        HwOpenScene();
        EnsureRouteGround();
        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        var hw = HwGroup();
        if (hw == null) { Debug.LogError("[nb3] capture: no 'NB Highway' group."); return; }
        Directory.CreateDirectory(Path.Combine(NagisaBayDiagnostics.OutDir, "overhaul"));
        var movers = hw.GetComponentsInChildren<AmbientPathMover>(true);
        var roads = hw.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Road").ToArray();

        foreach (var (sname, dWant, tag) in new[] { ("West", _route.HotelM + 500f, "nb3_highway_side"), ("East", _route.CoastStartM + 900f, "nb3_eastcoast_highway") })
        {
            var st = HwStations(sname == "West" ? 0f : _route.CoastStartM, sname == "West" ? _route.ClimbStartM : _route.BridgeStartM);
            // pick the station nearest dWant that has built deck geometry nearby
            HwSt best = st[0]; float bestErr = float.MaxValue;
            var deckMeshes = hw.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.Contains("_asphalt_")).Select(r => r.bounds).ToArray();
            foreach (var s in st)
            {
                var q = W(s, 20f, 0f);
                if (!deckMeshes.Any(b => b.Contains(new Vector3(q.x, b.center.y, q.z)))) continue;
                float e = Mathf.Abs(s.d - dWant);
                if (e < bestErr) { bestErr = e; best = s; }
            }
            var eye = best.p - best.inl * 2.2f + Vector3.up * 1.7f - best.t * 25f;
            var look = W(best, 18f, 2.5f) + best.t * 45f;
            AmbientCull.TargetPositionOverride = () => eye;
            foreach (var m in movers) m.ForceCullCheck(eye);
            NagisaBayDiagnostics.Shot(eye, look, 55f, $"overhaul/{tag}_a.png");
            for (int f = 0; f < 30; f++) foreach (var m in movers) m.Step(0.1f);
            NagisaBayDiagnostics.Shot(eye, look, 55f, $"overhaul/{tag}_b.png");
            if (sname == "West")
            {
                var aEye = best.p - best.inl * 90f + Vector3.up * 55f - best.t * 140f;
                NagisaBayDiagnostics.Shot(aEye, W(best, 22f, 0f) + best.t * 60f, 50f, "overhaul/nb3_highway_aerial.png");
                var gEye = W(best, HwLaneBslow, 1.6f) - best.t * 1.0f;   // lane-level (camera only; no person stands here)
                NagisaBayDiagnostics.Shot(gEye + Vector3.up * 3f, gEye + best.t * 120f + Vector3.up * 2f, 55f, "overhaul/nb3_highway_deck.png");
            }
            Debug.Log($"[nb3] captured {tag} at d={best.d:0} m.");
        }
        AmbientCull.TargetPositionOverride = null;
    }

    private static Transform HwGroup()
    {
        var root = GameObject.Find(RootName);
        if (root == null) return null;
        var ov = root.transform.Find(OverhaulGroupName);
        return ov != null ? ov.Find(HighwayGroupName) : null;
    }

    private static void HwOpenScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
    }
}

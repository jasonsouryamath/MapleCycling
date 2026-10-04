// Nagisa Bay (B5) PASS 2 - the dense SW-shore resort town (copilot CLI Opus, builder).
//
// Replaces pass 1's "fill buildings" (three extruded midrise boxes scattered on flat lawn pads).
// Everything is laid out in a ROUTE-RELATIVE frame: d = metres along the centreline, off = metres
// INLAND of it (seaward is negative), so the street grid follows the coast road like a real
// waterfront town. Cross-section (inland offsets, PROVISIONAL illustrative tuning):
//
//     4.1  road edge (shoulder)      7.1  CorridorKeepOutM - nothing solid inside this
//     7.4 - 10.8   coast-road sidewalk (raised kerb, street lights + clipped street trees)
//    10.8 - 35     ROW A: sea-view condos / boutique hotels / resort hotel with forecourts + gardens
//    35 - 38.3     main-street sea-side sidewalk (trees, lights, benches)
//    38.3 - 49.7   MAIN STREET: 2.5 m parking lane (parked cars) + 8.9 m carriageway, centre dashes
//    49.7 - 54.2   shop-side sidewalk (planters, bike racks, scooters, bus stop, cafe seating)
//    54.2 - 72     ROW B: shophouse main street (3 styles x 3 variants x 8 colourways, never the same
//                  silhouette side by side), konbini + car park, shrine, kiosks, alleys
//    72 - 92 | 92-102 street R1 | 102 - 124    ROWS C / D: pastel houses facing residential street R1
//   124 - 146 | 146-156 street R2 | 156 - 178  ROWS E / F (only up to TownFarRowD1)
//   seaward (d 950-2080): ROW S beach houses facing the sea behind a hedge line.
// Cross streets every TownBlockM + TownCrossW, with kerbs, corner sidewalks and zebra crossings.
//
// Every building is a Blender GLB from tools/blender/build_nagisa_buildings.py (LOD0/1/2, trim sheets,
// PBR); every prop from build_nagisa_street.py. Ground surfaces are generated here (paving,
// asphalt, granite kerbs, lawn, tropical groundcover, gravel) and merged per material per
// TownChunkM chunk. Walkable raised surfaces carry a MeshCollider and "Beach Sand walk layer" in
// their name: the shared MinatoCrowdActor ground probe ranks colliders whose name contains
// "Beach Sand" above the terrain, so walkers stand ON the sidewalk instead of the terrain 18 cm below.
//
// Idempotent: everything lives under "Nagisa Town Grid" inside the region root, rebuilt from
// scratch by Apply(). Nothing is placed inside the ride corridor (CanPlace / the offsets above).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- provisional layout
    private const float TownD0 = 140f, TownD1 = 2600f;
    private const float TownFarRowD1 = 2000f;          // second residential street ends here
    private const float TownBlockM = 86f, TownCrossW = 10f, TownCrossLane = 3.5f;
    private const float TownChunkM = 320f;
    private const float RowSD0 = 950f, RowSD1 = 950f;   // seaward beach-house row: DISABLED (sat ~15-25 m inland of the ride road, inside the NB3 highway band -> tunnels)
    private const float TownBandClearM = 40f;           // no building footprint corner nearer than this (plan metres) to the ride line
    private const float KerbH = 0.15f;
    private const float WalkLift = 0.18f, StreetLift = 0.045f, LotLift = 0.05f, PaveLift = 0.06f;

    private const float O_Coast0 = 7.4f, O_Coast1 = 10.8f;
    private const float O_RowAFront = 13f, O_RowA1 = 35f;
    private const float O_MainWalkA0 = 35f, O_Main0 = 38.3f, O_Park1 = 40.8f, O_Main1 = 49.7f, O_MainWalkB1 = 54.2f;
    private const float O_RowB1 = 72f;
    private const float O_CrossRoadStart = 4.4f;
    private static readonly (float walk0, float road0, float road1, float walk1)[] ResStreets =
    {
        (92f, 94f, 100f, 102f),
        (146f, 148f, 154f, 156f),
    };

    private const float TownLightSpacingM = 32f, TownTreeSpacingM = 16f, ParkBayM = 5.6f;
    private const float ParkFill = 0.62f, DriveCarFill = 0.55f, ScooterFill = 0.35f;

    // ---------------------------------------------------------------- frame
    private const float TfStep = 2f;
    private static int _townBandSkipped;
    private static float _tfD0;
    private static Vector3[] _tfP, _tfN;

    private static void BuildTownFrame()
    {
        _townBandSkipped = 0;
        float d0 = TownD0 - 80f, d1 = TownD1 + 80f;
        int n = Mathf.CeilToInt((d1 - d0) / TfStep) + 1;
        _tfD0 = d0; _tfP = new Vector3[n]; _tfN = new Vector3[n];
        var raw = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            float d = d0 + k * TfStep;
            int i = Mathf.Clamp(_route.IndexAt(d), 1, _route.Count - 1);
            float span = _route.Distance[i] - _route.Distance[i - 1];
            float t = span > 1e-4f ? Mathf.Clamp01((d - _route.Distance[i - 1]) / span) : 0f;
            _tfP[k] = Vector3.Lerp(_route.Position[i - 1], _route.Position[i], t);
            raw[k] = -_route.SideFlat(i) * SeaSign(i);
        }
        for (int k = 0; k < n; k++)
        {
            var s = Vector3.zero;
            for (int j = -8; j <= 8; j++) s += raw[Mathf.Clamp(k + j, 0, n - 1)];
            s.y = 0f;
            _tfN[k] = s.normalized;
        }
        // NB2: the whole town frame now starts across the NB3 highway (side rule: -8..-32 highway,
        // -32..-42 wall/palms, buildings beyond -42 m). Every layout offset below stays "metres inland
        // of the town baseline"; the baseline itself is TownShiftM inland of the ride centreline.
        for (int k = 0; k < n; k++) _tfP[k] += _tfN[k] * TownShiftM;
    }
    private const float TownShiftM = 36f;

    private static void TfAt(float d, out Vector3 p, out Vector3 n)
    {
        float f = Mathf.Clamp((d - _tfD0) / TfStep, 0f, _tfP.Length - 1.001f);
        int k = Mathf.FloorToInt(f); float t = f - k;
        p = Vector3.Lerp(_tfP[k], _tfP[k + 1], t);
        n = Vector3.Lerp(_tfN[k], _tfN[k + 1], t).normalized;
    }

    /// <summary>World point (y = ground height) at route distance d, inland offset off.</summary>
    private static Vector3 TP(float d, float off)
    {
        TfAt(d, out var p, out var n);
        var q = p + n * off;
        q.y = _ground.Height(q.x, q.z);
        return q;
    }

    private static Vector3 TN(float d) { TfAt(d, out _, out var n); return n; }

    private static Vector3 TT(float d)
    {
        TfAt(d - 1f, out var a, out _); TfAt(d + 1f, out var b, out _);
        var t = b - a; t.y = 0f;
        return t.sqrMagnitude > 1e-6f ? t.normalized : Vector3.forward;
    }

    private static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    /// <summary>Diagnostics: world point in the town frame (d along the route, off metres inland;
    /// negative = seaward), y = terrain. Loads the route/ground if Apply has not run this domain.</summary>
    public static Vector3 DiagTownPoint(float d, float off)
    {
        if (_route == null) _route = NagisaRoute.Load();
        if (_ground == null) _ground = NagisaGround.Load();
        if (_tfP == null || _tfP.Length == 0) BuildTownFrame();
        return TP(d, off);
    }

    public static Vector3 DiagInland(float d)
    {
        DiagTownPoint(d, 0f);
        return TN(d);
    }

    // ---------------------------------------------------------------- town mask (forest skips it)
    private static HashSet<long> _townMask = new HashSet<long>();
    private const float TownMaskCell = 6f;

    private static long MaskKey(float x, float z) =>
        ((long)Mathf.FloorToInt(x / TownMaskCell) << 32) ^ (uint)Mathf.FloorToInt(z / TownMaskCell);

    private static void MarkTown(float d0, float d1, float o0, float o1)
    {
        for (float d = d0; d <= d1; d += 3f)
            for (float o = o0; o <= o1; o += 3f)
            {
                TfAt(d, out var p, out var n);
                var q = p + n * o;
                _townMask.Add(MaskKey(q.x, q.z));
            }
    }

    private static bool InTown(float x, float z) => _townMask.Contains(MaskKey(x, z));

    // ---------------------------------------------------------------- ground batches
    private sealed class GBatch
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public bool vertical;
    }

    private static readonly Dictionary<(string mat, int chunk, bool col, bool vert), GBatch> _gb =
        new Dictionary<(string, int, bool, bool), GBatch>();

    private static GBatch GB(string mat, float d, bool col, bool vert = false)
    {
        var key = (mat, Mathf.FloorToInt(d / TownChunkM), col, vert);
        if (!_gb.TryGetValue(key, out var b)) { _gb[key] = b = new GBatch { vertical = vert }; }
        return b;
    }

    private static float GTile(string mat) =>
        mat == "NB_Asphalt2" ? 6f : mat == "NB_Lawn" || mat == "NB_Groundcover" ? 4f :
        mat == "NB_Paving" ? 2.4f : mat == "NB_PlazaStone" ? 3f : mat == "NB_Kerb" ? 1.2f : 3f;

    /// <summary>A ground-hugging rectangle in (d, off) space, subdivided so it follows the terrain.</summary>
    private static void GRect(string mat, float d0, float d1, float o0, float o1, float lift, bool col = false)
    {
        if (d1 - d0 < 0.05f || o1 - o0 < 0.05f) return;
        int nd = Mathf.Max(1, Mathf.CeilToInt((d1 - d0) / 4f)), no = Mathf.Max(1, Mathf.CeilToInt((o1 - o0) / 5f));
        var b = GB(mat, (d0 + d1) * 0.5f, col);
        int b0 = b.v.Count;
        float tile = GTile(mat);
        for (int j = 0; j <= no; j++)
            for (int i = 0; i <= nd; i++)
            {
                float d = Mathf.Lerp(d0, d1, i / (float)nd), o = Mathf.Lerp(o0, o1, j / (float)no);
                var p = TP(d, o); p.y += lift;
                b.v.Add(p); b.uv.Add(new Vector2(d / tile, o / tile));
            }
        for (int j = 0; j < no; j++)
            for (int i = 0; i < nd; i++)
            {
                int a = b0 + j * (nd + 1) + i, c = a + nd + 1;
                b.t.Add(a); b.t.Add(c); b.t.Add(a + 1);
                b.t.Add(a + 1); b.t.Add(c); b.t.Add(c + 1);
            }
    }

    /// <summary>Vertical strip between ground+lo and ground+hi along a polyline of (d, off) points,
    /// facing <paramref name="outward"/> side (+1 = toward increasing off / d depending on axis).</summary>
    private static void GVert(string mat, IList<Vector2> pts, float lo, float hi, Vector3 outwardHint, float dKey)
    {
        var b = GB(mat, dKey, false, true);
        float along = 0f;
        for (int k = 0; k + 1 < pts.Count; k++)
        {
            var a = TP(pts[k].x, pts[k].y); var c = TP(pts[k + 1].x, pts[k + 1].y);
            float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(c.x, 0, c.z));
            int i0 = b.v.Count;
            b.v.Add(new Vector3(a.x, a.y + lo, a.z)); b.v.Add(new Vector3(c.x, c.y + lo, c.z));
            b.v.Add(new Vector3(c.x, c.y + hi, c.z)); b.v.Add(new Vector3(a.x, a.y + hi, a.z));
            b.uv.Add(new Vector2(along, 0)); b.uv.Add(new Vector2(along + len / 1.2f, 0));
            b.uv.Add(new Vector2(along + len / 1.2f, (hi - lo) / 1.2f)); b.uv.Add(new Vector2(along, (hi - lo) / 1.2f));
            along += len / 1.2f;
            var nrm = Vector3.Cross(b.v[i0 + 1] - b.v[i0], b.v[i0 + 3] - b.v[i0]);
            if (Vector3.Dot(nrm, outwardHint) >= 0f) { b.t.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 }); }
            else { b.t.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 }); }
        }
    }

    /// <summary>Kerb face running along d at inland offset o (lower surface on the side of
    /// <paramref name="faceInland"/> ? +off : -off).</summary>
    private static void KerbAlongD(float d0, float d1, float o, bool faceInland, float lo, float hi)
    {
        if (d1 - d0 < 0.1f) return;
        var pts = new List<Vector2>();
        int n = Mathf.Max(1, Mathf.CeilToInt((d1 - d0) / 4f));
        for (int i = 0; i <= n; i++) pts.Add(new Vector2(Mathf.Lerp(d0, d1, i / (float)n), o));
        GVert("NB_Kerb", pts, lo, hi, TN((d0 + d1) * 0.5f) * (faceInland ? 1f : -1f), (d0 + d1) * 0.5f);
    }

    private static void KerbAlongOff(float d, float o0, float o1, bool faceForward, float lo, float hi)
    {
        if (o1 - o0 < 0.1f) return;
        var pts = new List<Vector2>();
        int n = Mathf.Max(1, Mathf.CeilToInt((o1 - o0) / 4f));
        for (int i = 0; i <= n; i++) pts.Add(new Vector2(d, Mathf.Lerp(o0, o1, i / (float)n)));
        GVert("NB_Kerb", pts, lo, hi, TT(d) * (faceForward ? 1f : -1f), d);
    }

    /// <summary>Raised sidewalk rectangle: paving top, granite kerb top band on the street edge(s),
    /// kerb faces on every edge that borders a lower surface.</summary>
    private static void Sidewalk(float d0, float d1, float o0, float o1, bool kerbLowO, bool kerbHighO,
                                 bool faceD0 = false, bool faceD1 = false, string mat = "NB_Paving")
    {
        const float band = 0.28f;
        float a0 = kerbLowO ? o0 + band : o0, a1 = kerbHighO ? o1 - band : o1;
        GRect(mat, d0, d1, a0, a1, WalkLift, col: true);
        if (kerbLowO) GRect("NB_Kerb", d0, d1, o0, o0 + band, WalkLift + 0.004f, col: true);
        if (kerbHighO) GRect("NB_Kerb", d0, d1, o1 - band, o1, WalkLift + 0.004f, col: true);
        KerbAlongD(d0, d1, o0, false, kerbLowO ? StreetLift : LotLift, WalkLift);
        KerbAlongD(d0, d1, o1, true, kerbHighO ? StreetLift : LotLift, WalkLift);
        if (faceD0) KerbAlongOff(d0, o0, o1, false, StreetLift, WalkLift);
        if (faceD1) KerbAlongOff(d1, o0, o1, true, StreetLift, WalkLift);
    }

    private static int FlushGround(Transform parent)
    {
        int n = 0; long tris = 0;
        foreach (var kv in _gb)
        {
            var (mat, chunk, col, vert) = kv.Key;
            var b = kv.Value;
            if (b.t.Count == 0) continue;
            if (!vert) FixWinding(b.v, b.t);
            string label = mat.Replace("NB_", "");
            string meshName = $"Nagisa_Town_{label}_{chunk}{(col ? "_w" : "")}{(vert ? "_v" : "")}";
            string goName = $"Town Ground {label} {chunk}{(vert ? " face" : "")}{(col ? " - Beach Sand walk layer" : "")}";
            var go = AddMesh(parent, goName, Finish(meshName, b.v, b.uv, b.t), _nb[mat], col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
            n++; tris += b.t.Count / 3;
        }
        _gb.Clear();
        Debug.Log($"[nagisa] town ground: {n} merged surface meshes, {tris:N0} tris");
        return n;
    }

    // ---------------------------------------------------------------- walls (lot boundaries)
    private static readonly List<Vector3> _wallV = new List<Vector3>();
    private static readonly List<Vector2> _wallUv = new List<Vector2>();
    private static readonly List<int> _wallT = new List<int>();
    private static readonly List<Vector3> _capV = new List<Vector3>();
    private static readonly List<Vector2> _capUv = new List<Vector2>();
    private static readonly List<int> _capT = new List<int>();

    /// <summary>Low rendered garden wall (0.2 m, h tall) with a 0.3 m coping, from (d0,o0) to (d1,o1).</summary>
    private static void LotWall(float d0, float o0, float d1, float o1, float h)
    {
        var a = TP(d0, o0); var c = TP(d1, o1);
        var dir = c - a; dir.y = 0f;
        float len = dir.magnitude;
        if (len < 0.3f) return;
        dir /= len;
        var side = Vector3.Cross(Vector3.up, dir);
        float y = Mathf.Min(a.y, c.y) - 0.1f;
        var mid = (a + c) * 0.5f;
        Box(_wallV, _wallUv, _wallT, new Vector3(mid.x, y + (h + 0.1f) * 0.5f, mid.z), side, dir, 0.2f, h + 0.1f, len);
        Box(_capV, _capUv, _capT, new Vector3(mid.x, y + h + 0.1f + 0.04f, mid.z), side, dir, 0.3f, 0.08f, len + 0.1f);
    }

    private static void GatePier(float d, float o)
    {
        var a = TP(d, o); var t = TT(d); var s = Vector3.Cross(Vector3.up, t);
        Box(_wallV, _wallUv, _wallT, new Vector3(a.x, a.y - 0.1f + 0.7f, a.z), s, t, 0.38f, 1.4f, 0.38f);
        Box(_capV, _capUv, _capT, new Vector3(a.x, a.y + 1.34f, a.z), s, t, 0.48f, 0.1f, 0.48f);
    }

    // ---------------------------------------------------------------- footprints
    private sealed class Foot { public float hx, hz, h; }
    private static Dictionary<string, Foot> _feet;

    private static Foot FootOf(string stem)
    {
        if (_feet == null)
        {
            _feet = new Dictionary<string, Foot>();
            string path = $"{ModelDir}/Nagisa_B_Footprints.json";
            if (System.IO.File.Exists(path))
            {
                string json = System.IO.File.ReadAllText(path);
                // tiny parser: "Nagisa_B_X": {"hx": 1, "hz": 2, "h": 3, ...}
                int i = 0;
                while ((i = json.IndexOf("\"Nagisa_", i, System.StringComparison.Ordinal)) >= 0)
                {
                    int e = json.IndexOf('"', i + 1);
                    string name = json.Substring(i + 1, e - i - 1);
                    int close = json.IndexOf('}', e);
                    string body = json.Substring(e, close - e);
                    _feet[name] = new Foot { hx = JNum(body, "\"hx\""), hz = JNum(body, "\"hz\""), h = JNum(body, "\"h\"") };
                    i = close;
                }
            }
            Debug.Log($"[nagisa] footprints: {_feet.Count} building types");
        }
        return _feet.TryGetValue(stem, out var f) ? f : new Foot { hx = 5f, hz = 5f, h = 8f };
    }

    private static float JNum(string body, string key)
    {
        int k = body.IndexOf(key, System.StringComparison.Ordinal);
        if (k < 0) return 0f;
        k = body.IndexOf(':', k) + 1;
        int e = k;
        while (e < body.Length && (char.IsDigit(body[e]) || body[e] == '.' || body[e] == '-' || body[e] == ' ' || body[e] == 'e')) e++;
        float.TryParse(body.Substring(k, e - k).Trim(), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out float v);
        return v;
    }

    // ---------------------------------------------------------------- placement
    private static readonly float[] BigCuts = { 0.22f, 0.05f, 0.0012f };
    private static readonly float[] MidCuts = { 0.12f, 0.035f, 0.0020f };
    private static readonly float[] HouseCuts = { 0.08f, 0.025f, 0.0030f };

    /// <summary>Place a building / prop in the town frame. faceSea: its front (local -z) faces the
    /// sea (-N); otherwise it faces inland (+N).</summary>
    private static GameObject PlaceT(string stem, Transform parent, float d, float off, bool faceSea,
                                     Dictionary<string, Material> remap = null, float[] cuts = null,
                                     float cull = 0.0015f, float extraYaw = 0f, float sink = 0.12f,
                                     float hx = 0f, float hz = 0f, bool hero = false, float scale = 1f)
    {
        var p = TP(d, off);
        var n = TN(d);
        var fwd = faceSea ? n : -n;
        float yaw = YawOf(fwd) + extraYaw;
        float y = p.y;
        if (hx > 0f || hz > 0f)
        {
            var r = Vector3.Cross(Vector3.up, fwd);
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var q = p + r * (sx * hx) + fwd * (sz * hz);
                if (_route.PlanDistance(q.x, q.z, out _) < TownBandClearM) { _townBandSkipped++; return null; }
                y = Mathf.Min(y, _ground.Height(q.x, q.z));
            }
        }
        var go = PlaceWorld(stem, parent, new Vector3(p.x, y - sink, p.z), yaw, scale, cull, remap, hero);
        if (go != null && cuts != null) ApplyCuts(go, cuts, hero);
        return go;
    }

    private static void ApplyCuts(GameObject go, float[] cuts, bool hero)
    {
        var g = go.GetComponent<LODGroup>();
        if (g == null) return;
        var lods = g.GetLODs();
        for (int i = 0; i < lods.Length && i < cuts.Length; i++) lods[i].screenRelativeTransitionHeight = cuts[i];
        g.SetLODs(lods);
        // big buildings: LOD1 also casts (their shadows fall on the street at 100-300 m)
        if (hero && lods.Length > 1)
            foreach (var r in lods[1].renderers) if (r != null) r.shadowCastingMode = ShadowCastingMode.On;
    }

    // ---------------------------------------------------------------- people spots (Life2 reads them)
    private struct WalkSpot { public Vector3 a, b; public string role; }
    private struct StandSpot { public Vector3 p, face; public string role; public bool sit; }
    private static readonly List<WalkSpot> _walkSpots = new List<WalkSpot>();
    private static readonly List<StandSpot> _standSpots = new List<StandSpot>();

    private static void AddWalk(float d0, float d1, float off, float lift, string role)
    {
        var a = TP(d0, off); var b = TP(d1, off);
        a.y += lift; b.y += lift;
        _walkSpots.Add(new WalkSpot { a = a, b = b, role = role });
    }

    private static void AddStand(float d, float off, float lift, Vector3 face, string role, bool sit = false)
    {
        var p = TP(d, off); p.y += lift;
        _standSpots.Add(new StandSpot { p = p, face = face, role = role, sit = sit });
    }

    // ================================================================= build

    private static readonly string[][] ShopStyles =
    {
        new[] { "Nagisa_B_ShopA_4", "Nagisa_B_ShopA_15", "Nagisa_B_ShopA_7", "Nagisa_B_ShopA_24", "Nagisa_B_ShopA_25" },
        new[] { "Nagisa_B_ShopB_5", "Nagisa_B_ShopB_6", "Nagisa_B_ShopB_1", "Nagisa_B_ShopB_26", "Nagisa_B_ShopB_27" },
        new[] { "Nagisa_B_ShopC_11", "Nagisa_B_ShopC_2", "Nagisa_B_ShopC_10", "Nagisa_B_ShopC_28", "Nagisa_B_ShopC_29", "Nagisa_B_ShopC_30", "Nagisa_B_ShopC_31" },
    };
    private static readonly string[] Houses = { "Nagisa_B_HouseA", "Nagisa_B_HouseB", "Nagisa_B_HouseC" };
    private static readonly string[] RowAKinds =
        { "Nagisa_B_Condo1", "Nagisa_B_Boutique2", "Nagisa_B_Condo2", "Nagisa_B_Boutique1" };
    private static readonly string[] Cars = { "Nagisa_S_CarKei", "Nagisa_S_CarSedan", "Nagisa_S_CarVan", "Nagisa_S_CarKei", "Nagisa_S_CarSedan" };

    private static int _townBuildings, _townHouses, _townShops, _townCars, _townProps;

    private static void BuildTownGrid(Transform group)
    {
        var townT = new GameObject("Nagisa Town Grid").transform;
        townT.SetParent(group, false);
        var bldT = new GameObject("Buildings").transform; bldT.SetParent(townT, false);
        var lotT = new GameObject("Lots + Gardens").transform; lotT.SetParent(townT, false);
        var furnT = new GameObject("Street Furniture").transform; furnT.SetParent(townT, false);
        var carT = new GameObject("Parked Vehicles").transform; carT.SetParent(townT, false);
        var groundT = new GameObject("Streets + Ground").transform; groundT.SetParent(townT, false);

        _gb.Clear(); _townMask.Clear(); _walkSpots.Clear(); _standSpots.Clear();
        _wallV.Clear(); _wallUv.Clear(); _wallT.Clear(); _capV.Clear(); _capUv.Clear(); _capT.Clear();
        _townBuildings = _townHouses = _townShops = _townCars = _townProps = 0;
        BuildTownFrame();
        var rng = new System.Random(52020);
        float R() => (float)rng.NextDouble();

        // ---- cross-street centres
        var cross = new List<float>();
        for (float c = TownD0; c <= TownD1 + 0.1f; c += TownBlockM + TownCrossW) cross.Add(c);
        float lastD = cross[cross.Count - 1];
        float Depth(float d) => d <= TownFarRowD1 ? ResStreets[1].walk1 + 22f : ResStreets[0].walk1 + 22f;

        MarkTown(TownD0 - 8f, lastD + 8f, -2f, 180f);
        if (RowSD1 > RowSD0) MarkTown(RowSD0, RowSD1, -48f, 0f);

        // ---- continuous carriageways (main street + residential streets) across the whole town
        GRect("NB_Asphalt2", TownD0 - TownCrossLane, lastD + TownCrossLane, O_Main0, O_Main1, StreetLift);
        // centre dashes + parking-bay lines
        float cL = (O_Park1 + O_Main1) * 0.5f;
        for (float d = TownD0 + 6f; d < lastD - 6f; d += 9f)
            GRect("NB_Kerb", d, d + 3f, cL - 0.07f, cL + 0.07f, StreetLift + 0.012f);
        GRect("NB_Kerb", TownD0, lastD, O_Park1 - 0.06f, O_Park1 + 0.06f, StreetLift + 0.012f);
        foreach (var (w0, r0, r1, w1) in ResStreets)
        {
            float e = w0 == ResStreets[1].walk0 ? Mathf.Min(lastD, TownFarRowD1) : lastD;
            GRect("NB_Asphalt2", TownD0 - TownCrossLane, e + TownCrossLane, r0, r1, StreetLift);
        }

        int block = 0;
        for (int k = 0; k + 1 < cross.Count; k++, block++)
        {
            float c0 = cross[k], c1 = cross[k + 1];
            float b0 = c0 + TownCrossW * 0.5f, b1 = c1 - TownCrossW * 0.5f;
            float depth = Depth((b0 + b1) * 0.5f);

            // ---- cross street at c0 (and the final one after the loop)
            CrossStreet(c0, Mathf.Max(depth, k > 0 ? Depth(c0 - 20f) : depth), furnT);

            // ---- sidewalks of this block
            Sidewalk(b0, b1, O_Coast0, O_Coast1, true, false, true, true);
            Sidewalk(b0, b1, O_MainWalkA0, O_Main0, false, true, true, true);
            Sidewalk(b0, b1, O_Main1, O_MainWalkB1, true, false, true, true);
            AddWalk(b0 + 3f, b1 - 3f, 9.1f, WalkLift, "Pedestrian");
            AddWalk(b0 + 3f, b1 - 3f, 36.6f, WalkLift, "Shopper");
            AddWalk(b0 + 3f, b1 - 3f, 51.6f, WalkLift, "Shopper");
            foreach (var (w0, r0, r1, w1) in ResStreets)
            {
                if (w0 == ResStreets[1].walk0 && b1 > TownFarRowD1) continue;
                Sidewalk(b0, b1, w0, r0, false, true, true, true);
                Sidewalk(b0, b1, r1, w1, true, false, true, true);
                AddWalk(b0 + 3f, b1 - 3f, (w0 + r0) * 0.5f, WalkLift, "Resident");
                AddWalk(b0 + 3f, b1 - 3f, (r1 + w1) * 0.5f, WalkLift, "Resident");
            }

            BuildRowA(block, b0, b1, bldT, lotT, furnT, rng);
            BuildMainStreetFurniture(b0, b1, furnT, carT, rng);
            var skipC = BuildRowB(block, b0, b1, bldT, lotT, furnT, carT, rng);
            // residential rows
            BuildHouseRow(b0, b1, O_RowB1, ResStreets[0].walk0, faceSea: false, bldT, lotT, carT, rng, skipC);
            BuildHouseRow(b0, b1, ResStreets[0].walk1, ResStreets[0].walk1 + 22f, faceSea: true, bldT, lotT, carT, rng, null);
            if (b1 <= TownFarRowD1)
            {
                BuildHouseRow(b0, b1, ResStreets[0].walk1 + 22f, ResStreets[1].walk0, faceSea: false, bldT, lotT, carT, rng, null);
                BuildHouseRow(b0, b1, ResStreets[1].walk1, ResStreets[1].walk1 + 22f, faceSea: true, bldT, lotT, carT, rng, null);
            }
            BuildResidentialFurniture(b0, b1, furnT, rng);
            // the back edge of the town: a tall hedge line against the forest
            for (float d = b0 + 2f; d < b1 - 2f; d += 4.1f)
                if (R() < 0.85f) { PlaceT("Nagisa_S_HedgeSeg", lotT, d + 2f, depth + 0.6f, true, cull: SmallPropCull, extraYaw: 0f, sink: 0.05f, scale: 1.35f); _townProps++; }
        }
        CrossStreet(lastD, Depth(lastD - 20f), furnT);

        BuildRowS(bldT, lotT, rng);

        // ---- flush merged geometry
        FlushGround(groundT);
        if (_wallT.Count > 0)
        {
            var w = AddMesh(lotT, "Town Garden Walls", Finish("Nagisa_Town_Walls", _wallV, _wallUv, _wallT), _nb["NB_Wall"], false);
            w.isStatic = true;
            var c = AddMesh(lotT, "Town Garden Wall Coping", Finish("Nagisa_Town_WallCoping", _capV, _capUv, _capT), _nb["NB_Trim"], false);
            c.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            c.isStatic = true;
        }
        Debug.Log($"[nagisa] town grid: {cross.Count} cross streets, {block} blocks, {_townBuildings} row-A/landmark buildings, " +
                  $"{_townShops} shophouses, {_townHouses} houses, {_townCars} parked vehicles, {_townProps} street props; " +
                  $"{_walkSpots.Count} walk legs + {_standSpots.Count} stand spots for people.");
    }

    // ---------------------------------------------------------------- cross street
    private static void CrossStreet(float c, float depth, Transform furnT)
    {
        float l0 = c - TownCrossLane, l1 = c + TownCrossLane, e0 = c - TownCrossW * 0.5f, e1 = c + TownCrossW * 0.5f;
        // carriageway, excluding the continuous main / residential carriageways
        var cuts = new List<(float, float)> { (O_Main0, O_Main1) };
        foreach (var s in ResStreets) if (depth > s.road1) cuts.Add((s.road0, s.road1));
        float o = O_CrossRoadStart;
        foreach (var (a, b) in cuts) { GRect("NB_Asphalt2", l0, l1, o, a, StreetLift); o = b; }
        GRect("NB_Asphalt2", l0, l1, o, depth, StreetLift);
        // corner sidewalks both sides of the cross street, between the carriageways
        var walks = new List<(float, float)> { (O_Coast0, O_Main0), (O_Main1, ResStreets[0].road0) };
        if (depth > ResStreets[1].road1) { walks.Add((ResStreets[0].road1, ResStreets[1].road0)); walks.Add((ResStreets[1].road1, depth)); }
        else walks.Add((ResStreets[0].road1, depth));
        foreach (var (a, b) in walks)
        {
            GRect("NB_Paving", e0, l0 - 0.28f, a, b, WalkLift, col: true);
            GRect("NB_Kerb", l0 - 0.28f, l0, a, b, WalkLift + 0.004f, col: true);
            GRect("NB_Paving", l1 + 0.28f, e1, a, b, WalkLift, col: true);
            GRect("NB_Kerb", l1, l1 + 0.28f, a, b, WalkLift + 0.004f, col: true);
            KerbAlongOff(l0, a, b, true, StreetLift, WalkLift);
            KerbAlongOff(l1, a, b, false, StreetLift, WalkLift);
            KerbAlongD(e0, l0, a, false, StreetLift, WalkLift);
            KerbAlongD(e0, l0, b, true, StreetLift, WalkLift);
            KerbAlongD(l1, e1, a, false, StreetLift, WalkLift);
            KerbAlongD(l1, e1, b, true, StreetLift, WalkLift);
        }
        // zebra crossings over the main street, both sides of the junction
        foreach (float zc in new[] { e0 - 2.2f, e1 + 2.2f })
            for (float s = O_Main0 + 0.6f; s < O_Main1 - 0.8f; s += 1.1f)
                GRect("NB_Kerb", zc - 1.5f, zc + 1.5f, s, s + 0.55f, StreetLift + 0.014f);
        // bollards at the corners of the coast-road mouth (keeps cars off the corner sidewalks)
        foreach (float d in new[] { e0 - 0.5f, e1 + 0.5f })
        {
            PlaceT("Nagisa_S_Bollard", furnT, d, O_Coast0 + 0.7f, true, cull: SmallPropCull, sink: -WalkLift + 0.02f);
            _townProps++;
        }
        // a street light at the coast-road corner, arm over the cross street
        PlaceT("Nagisa_S_StreetLight", furnT, e1 + 0.6f, O_Coast1 - 0.6f, true, cull: 0.003f, sink: -WalkLift + 0.02f);
        _townProps++;
    }

    // ---------------------------------------------------------------- row A (sea-view)
    private static int _rowAPick;

    private static void BuildRowA(int block, float b0, float b1, Transform bldT, Transform lotT, Transform furnT, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        float cursor = b0 + 1.5f;
        string prev = null;
        while (true)
        {
            string stem = null; Foot f = null;
            for (int tries = 0; tries < RowAKinds.Length; tries++)
            {
                string cand = RowAKinds[(_rowAPick + tries) % RowAKinds.Length];
                var cf = FootOf(cand);
                if (cand == prev) continue;
                if (cursor + 2f * cf.hx > b1 - 1.5f) continue;
                stem = cand; f = cf; _rowAPick += tries + 1; break;
            }
            if (stem == null) break;
            float dC = cursor + f.hx;
            float offC = O_RowAFront + f.hz;
            if (offC + f.hz > O_RowA1 - 0.8f) offC = O_RowA1 - 0.8f - f.hz;
            // forecourt (plaza stone) from the coast sidewalk to the building + a paved entry
            GRect("NB_PlazaStone", cursor, cursor + 2f * f.hx, O_Coast1, offC - f.hz + 0.2f, PaveLift);
            GRect("NB_Lawn", cursor, cursor + 2f * f.hx, offC + f.hz - 0.2f, O_MainWalkA0, LotLift);
            GRect("NB_Groundcover", cursor, cursor + 2f * f.hx, offC - f.hz + 0.2f, offC + f.hz - 0.2f, LotLift);
            var go = PlaceT(stem, bldT, dC, offC, true, NbColourway(block * 3 + _rowAPick), f.h > 30f ? BigCuts : MidCuts,
                            hx: f.hx, hz: f.hz, hero: true);
            if (go != null) _townBuildings++;
            // forecourt planters + a palm pair either side of the entrance
            foreach (float s in new[] { -1f, 1f })
            {
                PlaceT("Nagisa_S_Planter", furnT, dC + s * 4.2f, O_Coast1 + 1.3f, true, cull: SmallPropCull, sink: -PaveLift);
                PlaceT(R() < 0.5f ? "Nagisa_CoconutPalm_A" : "Nagisa_FanPalm", lotT, dC + s * (f.hx - 2.2f), O_Coast1 + 1.4f, true,
                       extraYaw: R() * 360f, sink: 0.1f, scale: 0.8f + R() * 0.2f);
                _townProps += 2;
            }
            for (int q = 0; q < 3; q++) AddStand(dC + (R() - 0.5f) * 2f * (f.hx - 3f), O_Coast1 + 0.9f + R() * 1.2f, PaveLift,
                                                 -TN(dC), "CondoGuest");
            prev = stem;
            float gap = 5f + R() * 4f;
            float g0 = cursor + 2f * f.hx, g1 = Mathf.Min(g0 + gap, b1);
            // garden between buildings: groundcover + a paved path through + a palm
            GardenStrip(g0, g1, O_Coast1, O_MainWalkA0, lotT, rng);
            cursor = g0 + gap;
        }
        if (cursor < b1) GardenStrip(cursor, b1, O_Coast1, O_MainWalkA0, lotT, rng);
    }

    private static void GardenStrip(float g0, float g1, float o0, float o1, Transform lotT, System.Random rng)
    {
        if (g1 - g0 < 0.5f) return;
        float R() => (float)rng.NextDouble();
        float mid = (g0 + g1) * 0.5f;
        if (g1 - g0 > 3.2f)
        {
            GRect("NB_Groundcover", g0, mid - 0.8f, o0, o1, LotLift);
            GRect("NB_PlazaStone", mid - 0.8f, mid + 0.8f, o0, o1, PaveLift);
            GRect("NB_Groundcover", mid + 0.8f, g1, o0, o1, LotLift);
            PlaceT(R() < 0.6f ? "Nagisa_CoconutPalm_B" : "Nagisa_FanPalm", lotT, g0 + 0.9f, (o0 + o1) * 0.5f + (R() - 0.5f) * 8f, true,
                   extraYaw: R() * 360f, sink: 0.1f, scale: 0.75f + R() * 0.25f);
            PlaceT(R() < 0.5f ? "Nagisa_Hibiscus" : "Nagisa_Bougainvillea", lotT, g1 - 0.8f, o0 + 3f + R() * 10f, true,
                   extraYaw: R() * 360f, cull: SmallPropCull, sink: 0.05f);
            _townProps += 2;
        }
        else GRect("NB_Groundcover", g0, g1, o0, o1, LotLift);
    }

    // ---------------------------------------------------------------- main street furniture + parking
    private static void BuildMainStreetFurniture(float b0, float b1, Transform furnT, Transform carT, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        // coast sidewalk: lights (arm over the road), clipped street trees between
        for (float d = b0 + 6f; d < b1 - 3f; d += TownLightSpacingM)
        {
            PlaceT("Nagisa_S_StreetLight", furnT, d, O_Coast0 + 0.55f, true, cull: 0.003f, sink: -WalkLift + 0.02f);
            _townProps++;
        }
        for (float d = b0 + 14f; d < b1 - 4f; d += TownTreeSpacingM * 2f)
        {
            PlaceT("Nagisa_S_StreetTreeA", furnT, d, O_Coast1 - 1.0f, true, extraYaw: R() * 360f, cull: SmallPropCull, sink: -WalkLift + 0.02f);
            _townProps++;
        }
        // main street, sea side: trees + lights (arm over the carriageway = +N) + benches
        for (float d = b0 + 5f; d < b1 - 3f; d += TownTreeSpacingM)
        {
            PlaceT(((int)(d / TownTreeSpacingM) & 1) == 0 ? "Nagisa_S_StreetTreeB" : "Nagisa_S_StreetTreeA", furnT, d,
                   O_MainWalkA0 + 1.0f, true, extraYaw: R() * 360f, cull: SmallPropCull, sink: -WalkLift + 0.02f);
            _townProps++;
        }
        for (float d = b0 + 13f; d < b1 - 3f; d += TownLightSpacingM)
        {
            PlaceT("Nagisa_S_StreetLight", furnT, d, O_Main0 - 0.5f, false, cull: 0.003f, sink: -WalkLift + 0.02f);
            // half the benches are taken (the seated donor brings its own bench model), half empty
            if (R() < 0.5f) PlaceT("Nagisa_Bench", furnT, d + 8f, O_MainWalkA0 + 1.1f, false, cull: SmallPropCull, sink: -WalkLift + 0.02f);
            else AddStand(d + 8f, O_MainWalkA0 + 1.1f, WalkLift, TN(d), "BenchSitter", sit: true);
            _townProps += 2;
        }
        // shop side: lights, planters and bike racks at the kerb, scooters parked nose-in
        for (float d = b0 + 21f; d < b1 - 3f; d += TownLightSpacingM)
        {
            PlaceT("Nagisa_S_StreetLight", furnT, d, O_Main1 + 0.5f, true, cull: 0.003f, sink: -WalkLift + 0.02f);
            _townProps++;
        }
        for (float d = b0 + 9f; d < b1 - 4f; d += 19f)
        {
            float pick = R();
            if (pick < 0.4f) PlaceT("Nagisa_S_Planter", furnT, d, O_Main1 + 0.9f, true, extraYaw: 90f, cull: SmallPropCull, sink: -WalkLift + 0.02f);
            else if (pick < 0.7f) PlaceT("Nagisa_S_BikeRack", furnT, d, O_Main1 + 0.9f, true, cull: SmallPropCull, sink: -WalkLift + 0.02f);
            else
            {
                for (int s = 0; s < 3; s++)
                    if (R() < 0.8f)
                    {
                        PlaceT("Nagisa_S_Scooter", carT, d + s * 1.1f, O_Main1 + 1.2f, true, NbCarColour(rng.Next(8)), extraYaw: 90f + (R() - 0.5f) * 12f,
                               cull: SmallPropCull, sink: -WalkLift + 0.02f);
                        _townCars++;
                    }
            }
            _townProps++;
        }
        // parking lane: cars nose-to-tail along the sea-side kerb
        for (float d = b0 + 2f + ParkBayM * 0.5f; d < b1 - ParkBayM * 0.5f; d += ParkBayM)
        {
            if (R() > ParkFill) continue;
            string car = Cars[rng.Next(Cars.Length)];
            PlaceT(car, carT, d, (O_Main0 + O_Park1) * 0.5f, true, NbCarColour(rng.Next(8)), cull: 0.004f,
                   extraYaw: 90f + (R() < 0.85f ? 0f : 180f), sink: -StreetLift + 0.01f);
            // NB: extraYaw 90 turns local -z (the car's nose) from -N onto the tangent
            _townCars++;
        }
    }

    // ---------------------------------------------------------------- row B (shophouses)
    private static int _shopColour;

    /// <summary>Returns the d-ranges where row C must stay empty (deep landmarks: konbini car park, shrine).</summary>
    private static List<(float, float)> BuildRowB(int block, float b0, float b1, Transform bldT, Transform lotT,
                                                  Transform furnT, Transform carT, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        var skip = new List<(float, float)>();
        float cursor = b0 + 0.6f;
        // landmark per block (cycled): 0 konbini, 1 plain, 2 shrine, 3 kiosk corner + bus stop, 4 plain
        int landmark = block % 5;
        if (landmark == 0)
        {
            var f = FootOf("Nagisa_B_Konbini");
            const float lot = 11f;
            float dC = cursor + f.hx + 1f, front = O_MainWalkB1 + lot;
            GRect("NB_Asphalt2", cursor, dC + f.hx + 1f, O_MainWalkB1, front, StreetLift + 0.01f);
            for (float x = cursor + 1.2f; x < dC + f.hx; x += 2.6f)
                GRect("NB_Kerb", x, x + 0.1f, front - 5.2f, front - 0.4f, StreetLift + 0.022f);
            KerbAlongD(cursor, dC + f.hx + 1f, O_MainWalkB1, true, StreetLift, WalkLift);
            PlaceT("Nagisa_B_Konbini", bldT, dC, front + f.hz, true, null, MidCuts, hx: f.hx, hz: f.hz, hero: true);
            GRect("NB_Gravel", cursor, dC + f.hx + 1f, front + 2f * f.hz, O_RowB1 + 20f, LotLift);
            for (int c = 0; c < 5; c++)
                if (R() < 0.75f)
                {
                    PlaceT(Cars[rng.Next(Cars.Length)], carT, cursor + 2.5f + c * 2.6f, front - 2.8f, false, NbCarColour(rng.Next(8)),
                           cull: 0.004f, sink: -StreetLift);
                    _townCars++;
                }
            for (int q = 0; q < 3; q++) AddStand(dC + (R() - 0.5f) * 8f, front - 0.8f, StreetLift, TN(dC), "Shopper");
            skip.Add((cursor - 1f, dC + f.hx + 2f));
            _townBuildings++;
            cursor = dC + f.hx + 1.8f;
        }
        else if (landmark == 2)
        {
            var f = FootOf("Nagisa_B_Shrine");
            float dC = (b0 + b1) * 0.5f;
            // the shrine sits back in a grove, the row resumes either side
            FillShops(block, cursor, dC - f.hx - 4f, bldT, lotT, furnT, rng);
            GRect("NB_Gravel", dC - f.hx - 4f, dC + f.hx + 4f, O_MainWalkB1, O_RowB1 + 18f, LotLift);
            PlaceT("Nagisa_B_Shrine", bldT, dC, O_MainWalkB1 + 3f + f.hz, true, null, MidCuts, hx: f.hx, hz: f.hz, hero: true);
            foreach (float s in new[] { -1f, 1f })
                for (int t = 0; t < 3; t++)
                    PlaceT(t == 1 ? "Nagisa_CanopyTree_B" : "Nagisa_CanopyTree_A", lotT, dC + s * (f.hx + 2.6f),
                           O_MainWalkB1 + 6f + t * 9f, true, extraYaw: R() * 360f, sink: 0.1f, scale: 0.55f + R() * 0.15f);
            AddStand(dC + 1.5f, O_MainWalkB1 + 1.6f, LotLift, TN(dC), "Visitor");
            AddStand(dC - 1.2f, O_MainWalkB1 + 2.2f, LotLift, TN(dC), "Visitor");
            skip.Add((dC - f.hx - 4f, dC + f.hx + 4f));
            _townBuildings++;
            FillShops(block, dC + f.hx + 4f, b1 - 0.6f, bldT, lotT, furnT, rng);
            return skip;
        }
        else if (landmark == 3)
        {
            // corner kiosks + bus stop at the start of the block
            var g = FootOf("Nagisa_B_Gelato"); var s = FootOf("Nagisa_B_ShaveIce");
            GRect("NB_PlazaStone", cursor, cursor + 22f, O_MainWalkB1, O_RowB1, PaveLift + 0.12f);
            KerbAlongD(cursor, cursor + 22f, O_RowB1, true, LotLift, PaveLift + 0.12f);
            PlaceT("Nagisa_B_Gelato", bldT, cursor + g.hx + 1.5f, O_MainWalkB1 + 2f + g.hz, true, null, MidCuts, hx: g.hx, hz: g.hz, sink: -0.1f);
            PlaceT("Nagisa_B_ShaveIce", bldT, cursor + 2f * g.hx + 5f + s.hx, O_MainWalkB1 + 2f + s.hz, true, null, MidCuts, hx: s.hx, hz: s.hz, sink: -0.1f);
            PlaceT("Nagisa_S_BusStop", furnT, cursor + 17.5f, O_Main1 + 1.5f, true, cull: 0.003f, sink: -WalkLift + 0.02f);
            for (int q = 0; q < 4; q++) AddStand(cursor + g.hx + 1.5f + (R() - 0.5f) * 1.2f, O_MainWalkB1 + 0.7f - q * 0.75f, WalkLift, TN(cursor), "IceCreamQueue");
            for (int q = 0; q < 3; q++) AddStand(cursor + 16.5f + q * 0.9f, O_Main1 + 2.5f, WalkLift, -TN(cursor), "BusWait");
            _townBuildings += 2;
            cursor += 22.6f;
        }
        FillShops(block, cursor, b1 - 0.6f, bldT, lotT, furnT, rng);
        return skip;
    }

    private static int _prevStyle = -1;

    private static void FillShops(int block, float d0, float d1, Transform bldT, Transform lotT, Transform furnT, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        float cursor = d0;
        int inTerrace = 0, terraceLen = 4 + rng.Next(4);
        int prevColour = -1;
        while (true)
        {
            int style = rng.Next(3);
            if (style == _prevStyle) style = (style + 1 + rng.Next(2)) % 3;
            string stem = ShopStyles[style][rng.Next(ShopStyles[style].Length)];
            var f = FootOf(stem);
            if (cursor + 2f * f.hx > d1) { style = 1; stem = ShopStyles[1][rng.Next(ShopStyles[1].Length)]; f = FootOf(stem); if (cursor + 2f * f.hx > d1) break; }
            int colour = rng.Next(NbColourways.Length);
            if (colour == prevColour) colour = (colour + 3) % NbColourways.Length;
            float dC = cursor + f.hx;
            var go = PlaceT(stem, bldT, dC, O_MainWalkB1 + f.hz, true, NbColourway(colour), HouseCuts, cull: 0.002f, hx: f.hx, hz: f.hz, sink: 0.1f);
            if (go != null) _townShops++;
            GRect("NB_Gravel", cursor, cursor + 2f * f.hx, O_MainWalkB1 + 2f * f.hz - 0.3f, O_RowB1, LotLift);
            // window shoppers + cafe diners on the pavement in front
            float roll = R();
            if (roll < 0.35f) AddStand(dC + (R() - 0.5f) * 3f, O_MainWalkB1 - 0.9f, WalkLift, TN(dC), "WindowShopper");
            else if (roll < 0.55f) AddStand(dC, O_MainWalkB1 - 1.25f, WalkLift, -TN(dC), "CafeDiner", sit: true);
            _prevStyle = style; prevColour = colour;
            cursor += 2f * f.hx;
            if (++inTerrace >= terraceLen && cursor + 3.4f < d1)
            {
                // alley: paving + a potted palm, then a new terrace
                GRect("NB_Paving", cursor, cursor + 3.2f, O_MainWalkB1, O_RowB1, PaveLift);
                PlaceT("Nagisa_FanPalm", lotT, cursor + 1.6f, O_MainWalkB1 + 1.5f, true, extraYaw: R() * 360f, sink: 0.1f, scale: 0.55f);
                cursor += 3.2f; inTerrace = 0; terraceLen = 4 + rng.Next(4);
                _townProps++;
            }
        }
        if (cursor < d1) GRect("NB_Groundcover", cursor, d1, O_MainWalkB1, O_RowB1, LotLift);
    }

    // ---------------------------------------------------------------- houses
    private static int _houseColour;

    private static void BuildHouseRow(float b0, float b1, float o0, float o1, bool faceSea, Transform bldT, Transform lotT,
                                      Transform carT, System.Random rng, List<(float, float)> skip)
    {
        float R() => (float)rng.NextDouble();
        // faceSea: the street is at o0 (lower off), house fronts face it; else the street is at o1.
        float streetEdge = faceSea ? o0 : o1;
        float backEdge = faceSea ? o1 : o0;
        float sgn = faceSea ? 1f : -1f;             // +off moves away from the street
        float cursor = b0 + 0.8f;
        int prevKind = -1;
        while (true)
        {
            int kind = rng.Next(Houses.Length);
            if (kind == prevKind) kind = (kind + 1) % Houses.Length;
            string stem = Houses[kind];
            var f = FootOf(stem);
            float lotW = 2f * f.hx + 3.0f + R() * 2f;
            if (cursor + lotW > b1 - 0.5f) break;
            float l0 = cursor, l1 = cursor + lotW, dC = (l0 + l1) * 0.5f;
            cursor = l1;
            bool blocked = false;
            if (skip != null) foreach (var (s0, s1) in skip) if (l1 > s0 && l0 < s1) blocked = true;
            if (blocked) continue;
            float setback = 3.4f + R() * 1.4f;
            float offC = streetEdge + sgn * (setback + f.hz);
            float lo = Mathf.Min(streetEdge, backEdge), hi = Mathf.Max(streetEdge, backEdge);
            if (Mathf.Abs(offC - streetEdge) + f.hz > hi - lo - 0.5f) offC = streetEdge + sgn * (hi - lo - 0.5f - f.hz);
            // ground: lawn lot + a paved path / drive to the street
            bool hasDrive = kind == 0;
            float driveD = dC + (hasDrive ? 5.5f * DriveSign(dC, faceSea) : 0f);
            GRect("NB_Lawn", l0, l1, lo, hi, LotLift);
            float pathW = hasDrive ? 1.5f : 0.6f;
            float frontO = offC - sgn * f.hz;
            GRect(hasDrive ? "NB_PlazaStone" : "NB_Paving", driveD - pathW, driveD + pathW,
                  Mathf.Min(streetEdge, frontO), Mathf.Max(streetEdge, frontO), PaveLift);
            int colour = (_houseColour++ * 5 + rng.Next(2)) % NbColourways.Length;
            var go = PlaceT(stem, bldT, dC, offC, faceSea, NbColourway(colour), HouseCuts, cull: 0.003f, hx: f.hx, hz: f.hz, sink: 0.08f);
            if (go != null) _townHouses++;
            // boundary: front wall / hedge with a gap at the path, side walls
            float fo = streetEdge + sgn * 0.35f;
            bool hedge = R() < 0.5f;
            if (hedge)
            {
                for (float d = l0 + 0.2f; d + 4f < l1; d += 4.05f)
                    if (d + 4f < driveD - pathW - 0.2f || d > driveD + pathW + 0.2f)
                    { PlaceT("Nagisa_S_HedgeSeg", lotT, d + 2f, fo, faceSea, cull: SmallPropCull, sink: 0.05f); _townProps++; }
            }
            else
            {
                LotWall(l0, fo, driveD - pathW - 0.1f, fo, 0.9f);
                LotWall(driveD + pathW + 0.1f, fo, l1, fo, 0.9f);
                GatePier(driveD - pathW - 0.1f, fo); GatePier(driveD + pathW + 0.1f, fo);
            }
            LotWall(l1, streetEdge + sgn * 0.4f, l1, backEdge - sgn * 0.3f, R() < 0.5f ? 1.1f : 0.8f);
            // garden planting: a palm or small tree + flowering shrubs
            PlaceT(R() < 0.5f ? "Nagisa_FanPalm" : "Nagisa_CoconutPalm_B", lotT, l0 + 1.4f, backEdge - sgn * 2.5f, true,
                   extraYaw: R() * 360f, sink: 0.1f, scale: 0.6f + R() * 0.2f);
            PlaceT(R() < 0.5f ? "Nagisa_Hibiscus" : "Nagisa_Bougainvillea", lotT, l1 - 1.0f, streetEdge + sgn * 1.4f, true,
                   extraYaw: R() * 360f, cull: SmallPropCull, sink: 0.05f);
            _townProps += 2;
            if (hasDrive && R() < DriveCarFill)
            {
                PlaceT(Cars[rng.Next(Cars.Length)], carT, driveD, streetEdge + sgn * 3.6f, faceSea, NbCarColour(rng.Next(8)), cull: 0.004f,
                       extraYaw: 180f, sink: -PaveLift + 0.01f);
                _townCars++;
            }
            else if (R() < ScooterFill)
            {
                PlaceT("Nagisa_S_Scooter", carT, driveD + 0.9f, streetEdge + sgn * 1.6f, faceSea, NbCarColour(rng.Next(8)),
                       extraYaw: 180f + (R() - 0.5f) * 30f, cull: SmallPropCull, sink: -PaveLift + 0.01f);
                _townCars++;
            }
            // residents in the front garden
            if (R() < 0.45f) AddStand(l0 + 1.5f + R() * (lotW - 3f), streetEdge + sgn * (1.2f + R() * 1.4f), LotLift,
                                       sgn * TN(dC) * -1f + TT(dC) * (R() - 0.5f), "Resident");
        }
    }

    /// <summary>Which way (+1/-1 along d) a house's local +x points for its facing.</summary>
    private static float DriveSign(float d, bool faceSea)
    {
        var fwd = faceSea ? TN(d) : -TN(d);
        var right = Vector3.Cross(Vector3.up, fwd);
        return Vector3.Dot(right, TT(d)) >= 0f ? 1f : -1f;
    }

    private static void BuildResidentialFurniture(float b0, float b1, Transform furnT, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        foreach (var (w0, r0, r1, w1) in ResStreets)
        {
            if (w0 == ResStreets[1].walk0 && b1 > TownFarRowD1) continue;
            for (float d = b0 + 8f; d < b1 - 3f; d += 38f)
            {
                PlaceT("Nagisa_S_StreetLight", furnT, d, r0 - 0.4f, false, cull: 0.003f, sink: -WalkLift + 0.02f);
                PlaceT("Nagisa_S_StreetLight", furnT, d + 19f, r1 + 0.4f, true, cull: 0.003f, sink: -WalkLift + 0.02f);
                _townProps += 2;
            }
            for (float d = b0 + 16f; d < b1 - 3f; d += 26f)
            {
                PlaceT("Nagisa_S_StreetTreeA", furnT, d, w0 + 0.7f, true, extraYaw: R() * 360f, cull: SmallPropCull, sink: -WalkLift + 0.02f, scale: 0.85f);
                _townProps++;
            }
            // a couple of cars parked at the kerb of each residential street
            for (int c = 0; c < 2; c++)
            {
                float d = b0 + 10f + R() * (b1 - b0 - 20f);
                PlaceT(Cars[rng.Next(Cars.Length)], furnT, d, r1 - 1.2f, true, NbCarColour(rng.Next(8)), cull: 0.004f,
                       extraYaw: 90f, sink: -StreetLift + 0.01f);
                _townCars++;
            }
        }
    }

    // ---------------------------------------------------------------- row S (seaward beach houses)
    private static void BuildRowS(Transform bldT, Transform lotT, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        float cursor = RowSD0;
        int made = 0, prevKind = -1;
        // seaside path + hedge line on the seaward verge, then houses facing the sea
        while (cursor < RowSD1)
        {
            int kind = 1 + rng.Next(2);
            if (kind == prevKind) kind = kind == 1 ? 2 : 1;
            string stem = Houses[kind];
            var f = FootOf(stem);
            float lotW = 2f * f.hx + 6f + R() * 8f;
            float dC = cursor + lotW * 0.5f;
            float offC = -(O_Coast0 + 7f + f.hz);
            var c = TP(dC, offC - f.hz - 6f);
            bool ok = _ground.Coast(c.x, c.z) > 6f && CanPlace(TP(dC, offC).x, TP(dC, offC).z, f.hz, out _, 10f);
            if (ok)
            {
                GRect("NB_Lawn", cursor, cursor + lotW, offC - f.hz - 8f, -O_Coast0 - 3.2f, LotLift);
                GRect("NB_Paving", cursor, cursor + lotW, -O_Coast0 - 3.2f, -O_Coast0 - 0.2f, PaveLift);
                PlaceT(stem, bldT, dC, offC, true, NbColourway(5 + made), HouseCuts, cull: 0.003f, hx: f.hx, hz: f.hz, sink: 0.08f);
                for (float d = cursor + 0.2f; d + 4f < cursor + lotW; d += 4.05f)
                    if (Mathf.Abs(d + 2f - dC) > 2.4f)
                    { PlaceT("Nagisa_S_HedgeSeg", lotT, d + 2f, -O_Coast0 - 3.6f, true, cull: SmallPropCull, sink: 0.05f); _townProps++; }
                PlaceT("Nagisa_CoconutPalm_B", lotT, cursor + 1.5f, offC - f.hz - 4f, true, extraYaw: R() * 360f, sink: 0.1f, scale: 0.8f + R() * 0.2f);
                if (R() < 0.5f) AddStand(dC + (R() - 0.5f) * 6f, offC - f.hz - 3f, LotLift, -TN(dC), "Resident");
                AddWalk(cursor + 1f, cursor + lotW - 1f, -O_Coast0 - 1.7f, PaveLift, "Pedestrian");
                made++; _townHouses++;
            }
            prevKind = kind;
            cursor += lotW;
        }
        Debug.Log($"[nagisa] row S: {made} seaward beach houses; {_townBandSkipped} town buildings skipped for the highway band");
    }

    // ---------------------------------------------------------------- highway-band trim (NB2 <-> NB3)
    /// <summary>NB3's classifier tunnels the highway wherever a foreign renderer's bounds touch the band
    /// 7.6..35.2 m either side of the ride line. Footprint tests at placement time missed some Town buildings
    /// (curves, rotated AABBs), so this stage mirrors that test on the finished Town buildings and removes
    /// any instance that still intrudes. Runs before ResortRow/NB3 (order 50 &lt; 90); idempotent.</summary>
    [NagisaStage(50, "TownBandTrim")]
    private static void TownBandTrim(Transform group)
    {
        var root = group.parent != null ? group.parent.parent : null;
        if (root == null || _route == null) return;
        var bt = root.Find("Nagisa Town/Nagisa Town Grid/Buildings");
        if (bt == null) { Debug.Log("[nagisa-townband] no Town buildings found."); return; }
        var kids = new List<Transform>();
        for (int i = 0; i < bt.childCount; i++) kids.Add(bt.GetChild(i));
        var boundsOf = new Dictionary<Transform, Bounds>();
        foreach (var k in kids)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in k.GetComponentsInChildren<MeshRenderer>(true))
            { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any) boundsOf[k] = b;
        }
        var doomed = new HashSet<Transform>();
        for (float d = 0f; d < _route.Distance[_route.Count - 1]; d += 3f)
        {
            int i = _route.IndexAt(d);
            if (_route.OnBridge(i)) continue;
            var t = _route.Position[Mathf.Min(i + 1, _route.Count - 1)] - _route.Position[Mathf.Max(i - 1, 0)]; t.y = 0f;
            if (t.sqrMagnitude < 1e-4f) continue;
            t.Normalize();
            var perp = new Vector3(t.z, 0f, -t.x);
            var pos = _route.Position[i];
            foreach (var kv in boundsOf)
            {
                if (doomed.Contains(kv.Key)) continue;
                var b = kv.Value;
                if (b.max.y < pos.y - 2f || b.min.y > pos.y + 9f) continue;
                if (pos.x < b.min.x - 37f || pos.x > b.max.x + 37f || pos.z < b.min.z - 37f || pos.z > b.max.z + 37f) continue;
                if (b.size.x > 70f || b.size.z > 70f) continue;      // NB3 ignores >70 m blobs
                for (float o = 7.6f; o <= 35.8f; o += 2f)
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        var q = pos + perp * (sgn * o);
                        if (q.x < b.min.x - 0.4f || q.x > b.max.x + 0.4f || q.z < b.min.z - 0.4f || q.z > b.max.z + 0.4f) continue;
                        doomed.Add(kv.Key); goto next;
                    }
                next:;
            }
        }
        foreach (var k in doomed) Object.DestroyImmediate(k.gameObject);
        Debug.Log($"[nagisa-townband] removed {doomed.Count} of {kids.Count} Town buildings intruding on the highway band.");
    }
}

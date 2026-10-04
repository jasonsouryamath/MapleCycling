// NAGISA BAY premier resort cycling destination, brief sections 4 + 17 (Claude worker F, 2026-10-01).
//
// Stage "CyclingCulture" (order 92, before Wayfinding 95 / Flora 96 so they steer around it):
//   * cycling SERVICE POINTS on the oceanfront promenade every ~420 m: bike corral (5 premium road bikes
//     on stainless hoops), repair station, water-refill fountain, sign board, every other one a route map;
//   * the NAGISA CYCLING CLUB / CAFE landmark pavilion (rooftop sign, glazed terrace, bike corrals, cyclists
//     with coffee) on the best free flat site found by a site search, plus a second one on the east coast;
//   * hotel signage on the inland resort row: CYCLIST WELCOME / BIKE STORAGE / BIKE WASH / ROUTE INFORMATION
//     boards and a bike corral at every lot's forecourt (read from NB ResortRow's building list).
// All props are instanced GLB kits (tools/blender/build_nagisa_cycling_kit.py) with authored LODs and shared
// materials; text/map/vending faces are per-instance material remaps of textures from
// tools/textures/make_nagisa_cycling_signs.py. Nothing is placed inside CorridorKeepOutM of the ride road,
// on bridges, in the highway band, or on top of anything an earlier stage placed (occupancy hash).
// Shared helpers (Nbc*) are also used by .Boulevard.cs and .Promenade.cs.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    private const string NbcTexDir = NagisaTex + "/Cycling";
    private const float NbcPaveLift = 0.09f;
    /// <summary>Promenade-side stretches (metres along the route) that get cycling dressing; the marina front
    /// (0-900 m) belongs to worker G, the climb and ridge have no promenade.</summary>
    private static readonly Vector2[] NbcSpans = { new Vector2(930f, 4350f), new Vector2(12230f, 14930f) };

    private static readonly Color[] NbcBikeColours =
    {
        new Color(0.07f, 0.20f, 0.52f), new Color(0.86f, 0.12f, 0.14f), new Color(0.93f, 0.93f, 0.90f),
        new Color(0.10f, 0.10f, 0.11f), new Color(0.05f, 0.58f, 0.60f), new Color(0.98f, 0.78f, 0.10f),
        new Color(0.96f, 0.46f, 0.12f), new Color(0.88f, 0.30f, 0.56f), new Color(0.30f, 0.62f, 0.20f),
    };
    private static readonly Dictionary<string, Material> _nbcSignMats = new Dictionary<string, Material>();
    private static readonly Dictionary<string, Dictionary<string, Material>> _nbcRemaps = new Dictionary<string, Dictionary<string, Material>>();
    private static readonly List<string> _nbcLog = new List<string>();
    private static int _nbcPlaced, _nbcSkipped, _nbcPeople;

    // ------------------------------------------------------------------ shared setup
    private static void NbcInit()
    {
        EnsureRouteGround();
        if (_nb.Count == 0) { PrepareNagisaTextures(); BuildNbMaterials(); }
        _nbcSignMats.Clear(); _nbcRemaps.Clear();
        for (int k = 0; k < NbcBikeColours.Length; k++)
            _nb["NBC_Bike" + k] = Cel("Nagisa_NBC_Bike" + k, NbcBikeColours[k], 0.78f, 0.6f, 0.2f);
        _nb["NBC_BikeFrame"] = _nb["NBC_Bike0"];
        _nb["NBC_BikeFrameB"] = _nb["NBC_Bike1"];
        _nb["NBC_BikeFrameC"] = _nb["NBC_Bike2"];
        _nb["NBC_Water"] = Cel("Nagisa_NBC_Water", new Color(0.35f, 0.75f, 0.95f), 0.9f, 0.5f, 0.25f);
        _nb["NBC_SignFace"] = NbcFace("Nagisa_NBC_Sign_default", "NBC_Sign_resort.png");
        _nb["NBC_MapFace"] = NbcFace("Nagisa_NBC_Map", "NBC_Map.png");
        _nb["NBC_VendFace"] = NbcFace("Nagisa_NBC_Vend_drinks", "NBC_Vend_drinks.png");
    }

    private static Material NbcFace(string matName, string png)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{NbcTexDir}/{png}");
        if (tex == null) Debug.LogWarning($"[nbc] texture missing: {NbcTexDir}/{png}");
        var m = Cel(matName, Color.white, 0.3f, 0.1f, 0.05f, tex);
        SetF(m, "_ShadowAmbient", 0.8f);
        return m;
    }

    /// <summary>Remap that swaps the shared NBC_SignFace slot for the texture of sign `key` ("Sign"/"Blade"/"Aframe").</summary>
    private static Dictionary<string, Material> NbcSign(string kind, string key)
    {
        string id = kind + "_" + key;
        if (_nbcRemaps.TryGetValue(id, out var r)) return r;
        var mat = NbcFace("Nagisa_NBC_" + id, $"NBC_{id}.png");
        return _nbcRemaps[id] = new Dictionary<string, Material> { ["NBC_SignFace"] = mat };
    }

    private static Dictionary<string, Material> NbcVend(string key)
    {
        string id = "Vend_" + key;
        if (_nbcRemaps.TryGetValue(id, out var r)) return r;
        return _nbcRemaps[id] = new Dictionary<string, Material> { ["NBC_VendFace"] = NbcFace("Nagisa_NBC_" + id, $"NBC_{id}.png") };
    }

    private static Dictionary<string, Material> NbcBikeColourway(int k)
    {
        string id = "Bikes_" + (k % NbcBikeColours.Length);
        if (_nbcRemaps.TryGetValue(id, out var r)) return r;
        int n = NbcBikeColours.Length;
        return _nbcRemaps[id] = new Dictionary<string, Material>
        {
            ["NBC_BikeFrame"] = _nb["NBC_Bike" + (k % n)],
            ["NBC_BikeFrameB"] = _nb["NBC_Bike" + ((k + 3) % n)],
            ["NBC_BikeFrameC"] = _nb["NBC_Bike" + ((k + 6) % n)],
        };
    }

    // ------------------------------------------------------------------ occupancy (what earlier stages already placed)
    private sealed class NbcOcc
    {
        private readonly List<Bounds> _b = new List<Bounds>();
        private readonly Dictionary<long, List<int>> _h = new Dictionary<long, List<int>>();
        private const float Cell = 16f;
        private static long K(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(Bounds b)
        {
            int id = _b.Count; _b.Add(b);
            for (int x = Mathf.FloorToInt(b.min.x / Cell); x <= Mathf.FloorToInt(b.max.x / Cell); x++)
                for (int z = Mathf.FloorToInt(b.min.z / Cell); z <= Mathf.FloorToInt(b.max.z / Cell); z++)
                {
                    long k = K(x, z);
                    if (!_h.TryGetValue(k, out var l)) _h[k] = l = new List<int>();
                    l.Add(id);
                }
        }

        public void AddRect(Vector3 c, float rx, float rz) => Add(new Bounds(c, new Vector3(rx * 2f, 4f, rz * 2f)));

        /// <summary>True when circle (p, r) overlaps something already placed.</summary>
        public bool Hit(Vector3 p, float r)
        {
            long k = K(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
            // a bound may span cells; it is registered in every cell it covers, so one lookup suffices
            if (!_h.TryGetValue(k, out var l)) return false;
            foreach (int id in l)
            {
                var b = _b[id];
                if (p.x > b.min.x - r && p.x < b.max.x + r && p.z > b.min.z - r && p.z < b.max.z + r) return true;
            }
            return false;
        }

        public bool Free(Vector3 p, float r) => !Hit(p, r);

        public static NbcOcc FromScene(Transform stageGroup)
        {
            var o = new NbcOcc();
            int n = 0;
            foreach (var g in stageGroup.root.GetComponentsInChildren<LODGroup>(true))
            {
                if (g == null || g.transform.IsChildOf(stageGroup)) continue;
                if (g.GetComponent<MinatoCrowdActor>() != null) continue;
                var lods = g.GetLODs();
                if (lods.Length == 0) continue;
                bool any = false; Bounds b = default;
                foreach (var r in lods[0].renderers)
                {
                    if (r == null) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) continue;
                o.Add(b); n++;
            }
            Debug.Log($"[nbc] occupancy: {n} placed objects hashed.");
            return o;
        }
    }

    // ------------------------------------------------------------------ placement helpers
    /// <summary>Frame at route distance d, `off` metres to the SEA side of the ride centreline.</summary>
    private static bool NbcAt(float d, float off, out Vector3 pos, out Vector3 sd, out Vector3 tan)
    {
        int i = _route.IndexAt(d);
        sd = _route.SideFlat(i) * SeaSign(i);
        tan = _route.Tangent[i]; tan.y = 0f; tan.Normalize();
        pos = _route.Position[i] + sd * off;
        pos.y = _ground.Height(pos.x, pos.z) + NbcPaveLift;
        return !_route.OnBridge(i) && _ground.Coast(pos.x, pos.z) > 1.2f && !InAnyPad(pos.x, pos.z, 0f);
    }

    private static float NbcDistOf(Vector3 p) { _route.PlanDistance(p.x, p.z, out int ni); return _route.Distance[ni]; }

    private static float NbcYaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    private static GameObject NbcPut(NbcOcc occ, string stem, Transform parent, Vector3 p, float yaw, float radius,
                                     Dictionary<string, Material> remap = null, float scale = 1f,
                                     float cull = SmallPropCull, bool hero = false, bool force = false)
    {
        if (_route.PlanDistance(p.x, p.z, out _) < CorridorKeepOutM + 0.2f && !force) { _nbcSkipped++; return null; }
        if (occ != null && occ.Hit(p, radius)) { _nbcSkipped++; return null; }
        var go = PlaceWorld(stem, parent, p, yaw, scale, cull, remap, hero);
        if (go == null) return null;
        occ?.AddRect(p, radius, radius);
        _nbcPlaced++;
        return go;
    }

    private static Transform NbcChild(Transform parent, string name)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
    }

    /// <summary>Sample a tangent-aligned rectangle (centre c, x along `tan`, z along `sd`); fills ground min/max.
    /// Ok only when it is dry, flat enough, off the pads/road and free of earlier objects.</summary>
    private static bool NbcRectOk(NbcOcc occ, Vector3 c, Vector3 tan, Vector3 sd, float hw, float hd, float minCoast,
                                  float maxRange, out float hMin, out float hMax)
    {
        hMin = float.MaxValue; hMax = float.MinValue;
        for (int a = 0; a <= 4; a++)
            for (int b = 0; b <= 4; b++)
            {
                var q = c + tan * (hw * (a / 2f - 1f)) + sd * (hd * (b / 2f - 1f));
                if (_ground.Coast(q.x, q.z) < minCoast || InAnyPad(q.x, q.z, 3f)) return false;
                if (_route.PlanDistance(q.x, q.z, out int ni) < CorridorKeepOutM + 1.5f || _route.OnBridge(ni)) return false;
                float h = _ground.Height(q.x, q.z);
                if (h < 0.4f) return false;
                hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
                if (hMax - hMin > maxRange) return false;
                if (occ != null && (occ.Hit(q, 1.2f))) return false;
            }
        // mid-edge samples catch big buildings the 5x5 grid steps over
        if (occ != null)
            for (float fx = -1f; fx <= 1.01f; fx += 0.25f)
                for (float fz = -1f; fz <= 1.01f; fz += 0.25f)
                    if (occ.Hit(c + tan * (hw * fx) + sd * (hd * fz), 0.6f)) return false;
        return true;
    }

    /// <summary>First flat free site for a hw x hd rectangle scanning d then seaward offset. The rectangle's
    /// x axis follows the road tangent, z points to the sea.</summary>
    private static bool NbcFindSite(NbcOcc occ, float d0, float d1, float off0, float off1, float hw, float hd,
                                    float minCoast, float maxRange, out Vector3 c, out Vector3 tan, out Vector3 sd,
                                    out float hMin, out float hMax, float dStep = 5f)
    {
        c = tan = sd = Vector3.zero; hMin = hMax = 0f;
        for (float d = d0; d <= d1; d += dStep)
            for (float off = off0; off <= off1; off += 3f)
            {
                int i = _route.IndexAt(d);
                if (_route.OnBridge(i)) continue;
                if (!NbcAt(d, off, out var p, out var s, out var t)) continue;
                if (!NbcRectOk(occ, p, t, s, hw, hd, minCoast, maxRange, out hMin, out hMax)) continue;
                c = p; tan = t; sd = s;
                return true;
            }
        return false;
    }

    /// <summary>Flat stone plinth (top quad + skirts down to the ground) for pavilions and plazas.</summary>
    private static GameObject NbcPlinth(Transform parent, string name, Vector3 c, Vector3 tan, Vector3 sd, float hw, float hd,
                                        float topY, Material mat)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        var cs = new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) };
        var top = new Vector3[4]; var bot = new Vector3[4];
        for (int k = 0; k < 4; k++)
        {
            var p = c + tan * (cs[k].Item1 * hw) + sd * (cs[k].Item2 * hd);
            top[k] = new Vector3(p.x, topY, p.z);
            bot[k] = new Vector3(p.x, Mathf.Min(_ground.Height(p.x, p.z), topY - 0.15f) - 0.4f, p.z);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 dd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int n = v.Count; v.Add(a); v.Add(b); v.Add(cc); v.Add(dd);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            t.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
        Quad(top[0], top[1], top[2], top[3], new Vector2(0, 0), new Vector2(hw * 2 / 2.4f, 0),
             new Vector2(hw * 2 / 2.4f, hd * 2 / 2.4f), new Vector2(0, hd * 2 / 2.4f));
        for (int k = 0; k < 4; k++)
        {
            int j = (k + 1) % 4;
            Quad(top[k], bot[k], bot[j], top[j], new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1));
            Quad(top[j], bot[j], bot[k], top[k], new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1));
        }
        // make the top face up regardless of the corner winding
        if (Vector3.Dot(Vector3.Cross(top[1] - top[0], top[2] - top[0]), Vector3.up) < 0f)
            for (int i = 0; i < 6; i += 3) { (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]); }
        string mname = "Nagisa_NBC_" + name.Replace(' ', '_');
        var go = AddMesh(parent, name, Finish(mname, v, uv, t), mat, true);
        go.name = name + " - Beach Sand walk layer";   // crowd ground probe ranks this above terrain
        return go;
    }

    /// <summary>Mount the slowly spinning steel wheel (kinetic public art) on a placed Nagisa_NBC_SculptureWheel.
    /// AmbientRotor (W0 kit) only works within its cull distance, so it costs nothing when far away.</summary>
    private static void NbcMountWheelRing(Transform plinth)
    {
        var ring = Place("Nagisa_NBC_SculptureWheelRing", plinth, new Vector3(0f, 1.95f, 0f), 0f, 1f, false, 0.003f, null);
        if (ring == null) return;
        var rot = ring.AddComponent<AmbientRotor>();
        rot.axis = Vector3.right;
        rot.degreesPerSecond = 7f;
        rot.cullDistance = 700f;
    }

    private static NbCast NbcCast(Transform group) => _lastCast ?? NbCrowdCast(group.root);

    private static GameObject NbcPerson(NbCast cast, Transform parent, string role, Vector3 pos, Vector3 face, MK kind,
                                        float seed, Vector3? end = null)
    {
        var pool = kind == MK.Walk ? cast.walk : kind == MK.Sit ? cast.sit : cast.stand;
        var go = NbPerson(pool, parent, role, pos, face, kind, seed, end);
        if (go == null) return null;
        FinishPerson(go);
        _nbcPeople++;
        return go;
    }

    // ================================================================== STAGE
    [NagisaStage(92, "CyclingCulture")]
    private static void BuildCyclingCulture(Transform group)
    {
        NbcInit();
        _nbcLog.Clear(); _nbcPlaced = _nbcSkipped = _nbcPeople = 0;
        var occ = NbcOcc.FromScene(group);
        var cast = NbcCast(group);
        var rng = new System.Random(9201);
        float R() => (float)rng.NextDouble();

        var service = NbcChild(group, "Cycling service points");
        var club = NbcChild(group, "Cycling club and cafe");
        var hotels = NbcChild(group, "Hotel cycling signage");
        var folk = NbcChild(group, "Cyclists at rest");

        int sp = 0, maps = 0, rentals = 0;
        string[] boardCycle = { "water", "repair", "route_info", "bike_storage", "rental" };
        foreach (var span in NbcSpans)
            for (float d = span.x + 120f; d < span.y - 60f; d += 420f)
            {
                if (!NbcAt(d, 9.4f, out var mid, out var sd, out var tan)) continue;
                var toRoad = -sd;
                float cyaw = NbcYaw(sd);
                // bike corral across the walk, repair + water at the road edge of the paving
                var corralP = mid + sd * 1.7f;
                var cp = NbcPut(occ, "Nagisa_NBC_BikeCorral", service, corralP, cyaw, 3.2f, NbcBikeColourway(sp * 2 + 1));
                if (cp == null) continue;
                sp++;
                if (NbcAt(d + 5.4f, 8.3f, out var rp, out _, out _))
                    NbcPut(occ, "Nagisa_NBC_RepairStation", service, rp, NbcYaw(toRoad), 0.9f);
                if (NbcAt(d + 7.4f, 8.3f, out var hp, out _, out _))
                    NbcPut(occ, "Nagisa_NBC_Hydration", service, hp, NbcYaw(toRoad), 0.9f);
                // approach sign (faces the oncoming rider)
                if (NbcAt(d - 14f, 8.0f, out var bp, out _, out var bt))
                    NbcPut(occ, "Nagisa_NBC_SignBoard", service, bp, NbcYaw(-bt) + 8f, 1.5f, NbcSign("Sign", boardCycle[sp % boardCycle.Length]), 1.0f);
                if (sp % 2 == 0 && NbcAt(d + 12f, 12.0f, out var mp, out _, out _))
                    if (NbcPut(occ, "Nagisa_NBC_MapKiosk", service, mp, NbcYaw(sd), 1.6f) != null) maps++;
                // bike rental point further along: kiosk + a row of identical hire bikes + board
                if (sp % 2 == 1 && NbcAt(d + 200f, 10.6f, out var rn, out var rsd, out var rtan))
                {
                    var stand = NbcPut(occ, "Nagisa_S_RentalStand", service, rn, NbcYaw(rsd) + 180f, 2.4f);
                    if (stand != null)
                    {
                        var hire = NbcBikeColourway(4);
                        NbcPut(occ, "Nagisa_NBC_BikeCorral", service, rn - rtan * 5.2f, NbcYaw(rsd), 3.2f, hire);
                        NbcPut(occ, "Nagisa_NBC_BikeCorral", service, rn + rtan * 5.2f, NbcYaw(rsd), 3.2f, hire);
                        if (NbcAt(d + 186f, 8.0f, out var rb, out _, out var rbt))
                            NbcPut(occ, "Nagisa_NBC_SignBoard", service, rb, NbcYaw(-rbt) + 8f, 1.5f, NbcSign("Sign", "rental"));
                        rentals++;
                    }
                }
                // two resting cyclists + one rider on foot leading a bike's worth of conversation
                var chatP = mid + sd * 3.4f + tan * -2.0f;
                var g1 = NbcPerson(cast, folk, "CyclistCoffee", chatP, tan * 0.4f - sd * 0.6f, MK.Idle, R());
                var g2 = NbcPerson(cast, folk, "CyclistCoffee", chatP + tan * 1.0f, -tan * 0.4f - sd * 0.6f, MK.Idle, R());
                if (R() < 0.5f && NbcAt(d + 3f, 10.8f, out var wa, out _, out _) && NbcAt(d + 36f, 10.8f, out var wb, out _, out _))
                    NbcPerson(cast, folk, "ServiceJogger", wa, wb - wa, MK.Walk, R(), wb);
            }
        _nbcLog.Add($"service points {sp}, route maps {maps}, rental points {rentals}");

        // ---- the NAGISA CYCLING CLUB / CAFE landmark pavilions
        int clubs = 0;
        var clubWindows = new[] { new Vector4(2240f, 2800f, 15f, 60f), new Vector4(12400f, 14500f, 15f, 60f), new Vector4(1150f, 2100f, 14f, 70f) };
        string[] clubSign = { "club", "cafe", "cafe" };
        foreach (var w in clubWindows)
        {
            if (!NbcFindSite(occ, w.x, w.y, w.z, w.w, 8.8f, 9.4f, 6f, 0.9f, out var c, out var tan, out var sd, out var h0, out var h1))
            { _nbcLog.Add($"club window {w.x:0}-{w.y:0}: NO SITE"); continue; }
            // building front (+z) faces the road; the terrace and bike rows open toward the rider
            var toRoad = -sd;
            float baseY = h1 + 0.05f;
            var pc = new Vector3(c.x, 0f, c.z);
            NbcPlinth(club, "Club plinth " + clubs, c, tan, sd, 9.6f, 11.6f, baseY, _nb["NB_PlazaStone"]);
            var pos = new Vector3(c.x, baseY, c.z) + sd * 2.0f;         // building sits seaward of the terrace
            var go = PlaceWorld("Nagisa_NBC_CycleCafe", club, pos, NbcYaw(toRoad), 1f, 0.0015f,
                                new Dictionary<string, Material> { ["NBC_SignFace"] = NbcSign("Sign", clubSign[clubs])["NBC_SignFace"] }, true);
            occ.AddRect(c, 10.5f, 11.5f);
            if (go == null) continue;
            clubs++;
            // bike rows + stands on the terrace, guests with coffee
            var front = c + toRoad * 5.9f;
            NbcPut(null, "Nagisa_NBC_BikeCorral", club, new Vector3(front.x - tan.x * 4.0f, baseY + 0.02f, front.z - tan.z * 4.0f), NbcYaw(tan) + 90f, 1f, NbcBikeColourway(clubs * 3), force: true);
            NbcPut(null, "Nagisa_NBC_BikeCorral", club, new Vector3(front.x + tan.x * 4.2f, baseY + 0.02f, front.z + tan.z * 4.2f), NbcYaw(tan) + 90f, 1f, NbcBikeColourway(clubs * 3 + 2), force: true);
            for (int k = 0; k < 4; k++)
            {
                var sp2 = front + tan * (-6f + k * 4f) + toRoad * 1.6f;
                NbcPut(null, "Nagisa_NBC_BikeStand", club, new Vector3(sp2.x, baseY + 0.02f, sp2.z), NbcYaw(tan) + 90f, 1f, NbcBikeColourway(k + clubs * 5), force: true);
            }
            for (int k = 0; k < 6; k++)
            {
                var gp = front + tan * (-5f + k * 2.1f) + toRoad * (2.7f + (k % 2) * 0.8f);
                NbcPerson(cast, folk, "ClubCyclist", new Vector3(gp.x, baseY + 0.02f, gp.z), (k % 2 == 0 ? tan : -tan) * 0.5f + sd * 0.4f, MK.Idle, R());
            }
            for (int k = 0; k < 3; k++)
            {
                var pp = c + tan * (-7f + k * 7f) + toRoad * 8.3f;
                NbcPut(null, "Nagisa_S_Parasol", club, new Vector3(pp.x, baseY + 0.02f, pp.z), R() * 360f, 1f, force: true);
            }
            _nbcLog.Add($"CLUB[{clubSign[clubs - 1]}] at ({c.x:0},{c.z:0}) d~{NbcDistOf(c):0}");
        }

        // ---- hotels that cater to cyclists
        int hotelSigns = 0, hotelCorrals = 0;
        var rr = group.root.Find(OverhaulGroupName + "/NB ResortRow/Resort buildings");
        string[] hotelBoards = { "cyclist_welcome", "bike_storage", "bike_wash", "route_info" };
        if (rr != null)
        {
            int hi = 0;
            for (int ci = 0; ci < rr.childCount; ci++)
            {
                var b = rr.GetChild(ci);
                string stem = b.name;
                if (!(stem.Contains("Resort") || stem.Contains("Condo") || stem.Contains("Boutique"))) continue;
                var f = RrFootOf(stem);
                float fw = Mathf.Min(12f, f.hw * 0.5f);
                float yaw = b.eulerAngles.y;
                var q = Quaternion.Euler(0f, yaw, 0f);
                float z0 = f.front - RrForecourt;
                int side = (hi & 1) == 0 ? 1 : -1;
                var signP = b.position + q * new Vector3(side * (fw + 3.2f), 0f, z0 + 2.0f);
                signP.y = _ground.Height(signP.x, signP.z);
                var sgo = NbcPut(occ, "Nagisa_NBC_SignBoard", hotels, signP, yaw + 180f, 2.4f,
                                 NbcSign("Sign", hotelBoards[hi % hotelBoards.Length]), 1.5f, 0.0035f, force: true);
                if (sgo != null) hotelSigns++;
                var cpos = b.position + q * new Vector3(-side * (fw + 4.5f), 0f, z0 + 3.2f);
                cpos.y = _ground.Height(cpos.x, cpos.z) + 0.04f;
                if (NbcPut(occ, "Nagisa_NBC_BikeCorral", hotels, cpos, yaw + 90f, 3.2f, NbcBikeColourway(hi * 4 + 1), 1f, 0.004f, force: true) != null)
                    hotelCorrals++;
                if (hi % 2 == 0)
                {   // a bike-wash / repair post beside the corral: the CYCLIST WELCOME promise
                    var wp = b.position + q * new Vector3(-side * (fw + 8.6f), 0f, z0 + 3.0f);
                    wp.y = _ground.Height(wp.x, wp.z) + 0.04f;
                    NbcPut(occ, "Nagisa_NBC_RepairStation", hotels, wp, yaw + 180f, 1.0f, null, 1.2f, 0.004f, force: true);
                }
                hi++;
            }
        }
        _nbcLog.Add($"hotel signs {hotelSigns}, hotel bike corrals {hotelCorrals} (resort row {(rr != null ? rr.childCount : -1)} children)");

        foreach (var r in service.GetComponentsInChildren<MeshRenderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        NbcFlushLog("cycling", group);
    }

    private static void NbcFlushLog(string tag, Transform group)
    {
        string text = string.Join("\n", _nbcLog);
        Debug.Log($"[nbc] {tag}: placed {_nbcPlaced}, skipped {_nbcSkipped}, people {_nbcPeople}\n{text}");
        try
        {
            Directory.CreateDirectory("reference/good_graphics/nagisa_bay");
            File.AppendAllText($"reference/good_graphics/nagisa_bay/nbc_sites.txt", $"== {tag} {System.DateTime.Now:HH:mm}\n{text}\n");
        }
        catch { }
    }
}

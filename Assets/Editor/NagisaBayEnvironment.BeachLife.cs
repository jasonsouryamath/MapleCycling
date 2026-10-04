// NB6 - Beach activities: fill the sand (copilot, maplerider-builder). COORDINATION.md NB6 row.
//
// User 2026-09-28: "the left side is bustling with people, cars and stuff, BUT the right side is
// desolate". On 0..4,720 m the sea (and the beach) is on the rider's RIGHT (SeaSign > 0), and the
// NB2 pass-2 beach dressing only puts a thin line of sets ~40-60 m out, so from the saddle the
// sand reads empty. This stage ADDS (never edits NB2's Beach2/Life2 objects):
//   1. dense umbrella / lounger / towel rows from the boardwalk railing out to the wet sand,
//      varied parasol colours, sunbathers on some loungers;
//   2. live beach-volleyball courts: net, rope lines, a ball rallying between 4 players who hop
//      and shuffle (NagisaBeachActivity.Volley), spectators;
//   3. shoreline walkers on the wet sand + animated swash foam and a breaker line
//      (NagisaBeachActivity.Swash; a stand-in until NB1's NagisaSurf lands);
//   4. a beach bar (Nagisa_B_BeachBar + timber deck, counter, stools, sign, bartender, patrons)
//      and a beach shack (Nagisa_SurfShop + sign, rental rack, customers);
//   5. a planked BEACH BOARDWALK out on the sand, and skateboarders, rollerbladers and runners
//      looping it AND NB2's promenade boardwalk (NagisaBeachActivity.Glide).
// Everything sits at >= CorridorKeepOutM + margin from the ride road (only the sea side), props
// use the NB2 LOD ladder (Place/PlaceWorld) and small-prop cull, and nothing moves on the road.
// ALL numbers are PROVISIONAL art tuning.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    private static void EnsureRouteGround()
    {
        if (_route != null && _ground != null) return;
        var route = NagisaRoute.Load();
        var ground = NagisaGround.Load();
        HealGround(ground, route);
        BenchUnderRoad(ground, route);
        _route = route; _ground = ground;
    }

    /// <summary>Diagnostics: +1 when the sea is on the rider's RIGHT (SideFlat) at sample i.</summary>
    public static float DiagSeaSign(int i) { EnsureRouteGround(); return SeaSign(i); }

    /// <summary>Diagnostics: route metres of the NB6 hero beach block (bar + shack).</summary>
    public static float DiagBeachLifeCentreM(NagisaRoute r)
    {
        var p = UnityEngine.GameObject.Find(RootName + "/" + OverhaulGroupName + "/NB BeachLife/NB6 Beach Bar");
        if (p != null) { r.PlanDistance(p.transform.position.x, p.transform.position.z, out int i); return r.Distance[i]; }
        return r.BeachStartM + Nb6HeroCandidatesM[0];
    }

    // ---------------------------------------------------------------- PROVISIONAL tuning
    private static readonly float[] Nb6HeroCandidatesM = { 420f, 560f, 300f, 700f, 1500f, 1650f };
    private const float Nb6RailOffM = 15.6f;              // NB2 boardwalk outer edge (PromenadeOuterM + BoardwalkW)
    private const float Nb6UmbStepM = 8f;                 // along-shore spacing of umbrella sets
    private const float Nb6UmbRowM = 9f;                  // row pitch across the sand
    private const float Nb6UmbFirstRowM = 19.5f;          // first row, from the centreline
    private const float Nb6UmbFill = 0.75f;
    private const float Nb6WetKeepM = 11f;                // keep umbrellas this far above the waterline
    private const int Nb6UmbMaxRows = 5;                  // provisional perf cap: rows of umbrellas seaward of the promenade
    private const float Nb6SunbatherChance = 0.24f;
    private const float Nb6BwGapM = 17f;                  // beach boardwalk starts this far beyond the railing
    private const float Nb6BwW = 3.6f, Nb6BwLift = 0.32f;
    private const float Nb6BwMinSeaM = 22f;               // need this much sand beyond the boardwalk
    private const int Nb6Courts = 4;
    private const float Nb6CourtEveryM = 420f;
    private const float Nb6ShoreWalkEveryM = 45f;
    private const float Nb6FoamSegM = 28f;
    private const float Nb6GlideEveryM = 85f;              // one skater / blader / runner per this much loop
    private const float Nb6CullM = 450f;

    private static readonly List<Vector3> _nb6Occ = new List<Vector3>();
    private static readonly Dictionary<long, List<Vector3>> _nb6Hash = new Dictionary<long, List<Vector3>>();
    private static readonly (List<Vector3> v, List<Vector2> uv, List<int> t) _nb6Patch =
        (new List<Vector3>(), new List<Vector2>(), new List<int>());

    private static long Nb6Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    private static void Nb6Occupy(Vector3 p)
    {
        long k = Nb6Key(Mathf.FloorToInt(p.x / 4f), Mathf.FloorToInt(p.z / 4f));
        if (!_nb6Hash.TryGetValue(k, out var l)) _nb6Hash[k] = l = new List<Vector3>();
        l.Add(p);
    }
    private static bool Nb6Free(Vector3 p, float r)
    {
        int cx = Mathf.FloorToInt(p.x / 4f), cz = Mathf.FloorToInt(p.z / 4f);
        int span = Mathf.CeilToInt(r / 4f);
        for (int dx = -span; dx <= span; dx++)
            for (int dz = -span; dz <= span; dz++)
                if (_nb6Hash.TryGetValue(Nb6Key(cx + dx, cz + dz), out var l))
                    foreach (var q in l)
                        if ((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) < r * r) return false;
        return true;
    }

    [NagisaStage(60, "BeachLife")]
    private static void BuildBeachLifeStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0 || _nbColourRemaps[0] == null)
        {
            Debug.LogError("[nb6] NB materials not prepared - run the full NagisaBayEnvironment.Apply (it runs this stage at the end).");
            return;
        }
        var root = group.root;
        var cast = _lastCast ?? NbCrowdCast(root);
        _nb6Hash.Clear(); _nb6BoardMesh = null; _nb6WheelMesh = null;
        _nb6Patch.v.Clear(); _nb6Patch.uv.Clear(); _nb6Patch.t.Clear();

        // existing props + people (NB2 and earlier stages) are obstacles
        int occ = 0;
        foreach (var lg in root.GetComponentsInChildren<LODGroup>(true)) { Nb6Occupy(lg.transform.position); occ++; }
        foreach (var a in root.GetComponentsInChildren<MinatoCrowdActor>(true)) { Nb6Occupy(a.transform.position); occ++; }

        var propsT = Nb6Group(group, "NB6 Beach Sets");
        var peopleT = Nb6Group(group, "NB6 Beach People");
        var rng = new System.Random(6606);
        int people0 = _nbPeople;

        // --- per-sample beach profile on the beach section (sea on either side, via SeaSign)
        int i0 = _route.IndexAt(_route.BeachStartM + 25f), i1 = _route.IndexAt(_route.BeachEndM - 25f);
        int n = i1 - i0 + 1;
        var shore = new float[n];          // offset of the waterline (coast = 0) from the centreline
        var okRow = new bool[n];
        for (int k = 0; k < n; k++)
        {
            int i = i0 + k;
            var p = _route.Position[i]; var sd = _route.SideFlat(i) * SeaSign(i);
            shore[k] = -1f;
            if (_route.OnBridge(i)) continue;
            for (float o = Nb6RailOffM; o < 320f; o += 0.5f)
            {
                var q = p + sd * o;
                if (_ground.Coast(q.x, q.z) <= 0f) { shore[k] = o; break; }
            }
            okRow[k] = shore[k] > Nb6RailOffM + 12f && !NearDrive(p.x);
        }

        // hero sites first, then courts, then umbrellas fill what is left (umbrellas used to eat every hero site)
        var bw = BuildNb6Boardwalk(group, i0, n, shore, okRow, out var bwSegs);
        string hero = BuildNb6BarAndShack(group, peopleT, cast, rng, i0, n, shore, okRow, bw);
        int courts = BuildNb6Volleyball(group, peopleT, cast, rng, i0, n, shore, okRow);
        int sets = BuildNb6Umbrellas(propsT, peopleT, cast, rng, i0, n, shore, okRow);
        int shoreWalkers = BuildNb6Shoreline(group, peopleT, cast, rng, i0, n, shore);
        int gliders = BuildNb6Gliders(group, peopleT, cast, rng, bwSegs);

        // walk-layer patches under lounger sitters (hidden by the lounger)
        if (_nb6Patch.t.Count > 0)
        {
            FixWinding(_nb6Patch.v, _nb6Patch.t);
            var go = AddMesh(peopleT, "NB6 Lounger Patches - Beach Sand walk layer",
                             Finish("Nagisa_NB6_LoungerPatch", _nb6Patch.v, _nb6Patch.uv, _nb6Patch.t), _nb["NB_Canvas"], true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        // props never move: let Unity static-batch them
        foreach (var r in propsT.GetComponentsInChildren<MeshRenderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);

        Debug.Log($"[nb6] beach life: {occ} existing obstacles; {sets} umbrella sets, {courts} live volleyball courts, " +
                  $"{bwSegs.Count} beach-boardwalk runs ({bw:0} m), hero block {hero}, {shoreWalkers} shoreline walkers, " +
                  $"{gliders} skaters/bladers/runners; {_nbPeople - people0} people added.");
    }

    private static Transform Nb6Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static Vector3 Nb6Sd(int i) => _route.SideFlat(i) * SeaSign(i);
    private static Vector3 Nb6Tan(int i) { var t = _route.Tangent[i]; t.y = 0f; return t.normalized; }
    private static Vector3 Nb6At(int i, float off)
    {
        var q = _route.Position[i] + Nb6Sd(i) * off;
        q.y = _ground.Height(q.x, q.z);
        return q;
    }

    // ================================================================ 1. umbrella rows
    private static int BuildNb6Umbrellas(Transform propsT, Transform peopleT, NbCast cast, System.Random rng,
                                         int i0, int n, float[] shore, bool[] okRow)
    {
        float R() => (float)rng.NextDouble();
        int sets = 0; float last = -999f;
        for (int k = 0; k < n; k++)
        {
            int i = i0 + k;
            float d = _route.Distance[i];
            if (!okRow[k] || d - last < Nb6UmbStepM) continue;
            last = d;
            var sd = Nb6Sd(i); var tan = Nb6Tan(i);
            float seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            float bw0 = Nb6RailOffM + Nb6BwGapM - 2.5f, bw1 = Nb6RailOffM + Nb6BwGapM + Nb6BwW + 2.5f;
            int row = 0;
            for (float o = Nb6UmbFirstRowM; o < shore[k] - Nb6WetKeepM && row < Nb6UmbMaxRows; o += Nb6UmbRowM, row++)
            {
                if (o > bw0 && o < bw1) continue;                       // keep the beach boardwalk clear
                if (R() > Nb6UmbFill) continue;
                var u = _route.Position[i] + sd * (o + (R() - 0.5f) * 1.6f) + tan * ((row & 1) * Nb6UmbStepM * 0.5f + (R() - 0.5f) * 1.2f);
                if (InAnyPad(u.x, u.z, 3f) || !CanPlace(u.x, u.z, 3f, out float uy, 6f) || !Nb6Free(u, 2.6f)) continue;
                u.y = uy;
                Nb6Occupy(u);
                bool parasol = R() < 0.72f;
                if (parasol)
                    PlaceWorld("Nagisa_S_Parasol", propsT, new Vector3(u.x, uy - 0.1f, u.z), R() * 360f, 1f, SmallPropCull, NbColourway(rng.Next(8)));
                else
                    PlaceWorld("Nagisa_BeachUmbrella", propsT, new Vector3(u.x, uy - 0.15f, u.z), R() * 360f, 1f, SmallPropCull);
                // loungers: backrest at local +z (landward), sitter faces the sea
                float yaw = seaYaw + 180f + (R() - 0.5f) * 18f;
                var side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                bool towels = R() < 0.35f;
                int count = R() < 0.75f ? 2 : 1;
                for (int c = 0; c < count; c++)
                {
                    var lp = u + side * (count == 1 ? 0f : (c == 0 ? -0.85f : 0.85f)) + sd * 0.45f;
                    lp.y = _ground.Height(lp.x, lp.z) + 0.01f;
                    if (towels)
                    {
                        PlaceWorld("Nagisa_S_Towel", propsT, lp + sd * 0.4f, yaw, 1f, SmallPropCull, NbColourway(rng.Next(8)));
                        if (R() < Nb6SunbatherChance)
                            Nb6Spawn(cast, peopleT, lp + sd * 1.7f + side * 0.2f, -sd + side * (R() - 0.5f), "Sunbather",
                                     R() < 0.3f ? MK.Wave : MK.Idle, R());
                    }
                    else
                    {
                        PlaceWorld("Nagisa_Lounger", propsT, lp, yaw, 1f, SmallPropCull);
                        if (R() < Nb6SunbatherChance) Nb6Lounger(cast, peopleT, lp, sd, R());
                    }
                }
                sets++;
            }
        }
        return sets;
    }

    private static GameObject Nb6Spawn(NbCast cast, Transform parent, Vector3 p, Vector3 face, string role, MK kind, float seed,
                                       float scale = 1f)
    {
        var pool = kind == MK.Walk ? cast.walk : cast.stand;
        var go = NbPerson(pool, parent, role, p, face, kind, seed);
        if (go == null) return null;
        if (scale != 1f) go.transform.localScale *= scale;
        FinishPerson(go);
        Nb6Occupy(p);
        return go;
    }

    private static void Nb6Lounger(NbCast cast, Transform parent, Vector3 lp, Vector3 sd, float seed)
    {
        if (cast.sit.Count == 0) return;
        var pos = lp + sd * 0.18f + Vector3.up * LoungerSitLift;
        var go = NbPerson(cast.sit, parent, "Sunbather", pos, sd, MK.Sit, seed);
        if (go == null) return;
        var bench = go.transform.Find("Timber Waterfront Bench");
        if (bench != null) Object.DestroyImmediate(bench.gameObject);
        FinishPerson(go);
        // tiny walk-layer patch under the root so the ground probe keeps the seat on the cushion
        var b = _nb6Patch; int v0 = b.v.Count; float h = 0.2f, y = pos.y + 0.006f;
        b.v.Add(new Vector3(pos.x - h, y, pos.z - h)); b.v.Add(new Vector3(pos.x + h, y, pos.z - h));
        b.v.Add(new Vector3(pos.x + h, y, pos.z + h)); b.v.Add(new Vector3(pos.x - h, y, pos.z + h));
        b.uv.Add(Vector2.zero); b.uv.Add(Vector2.right); b.uv.Add(Vector2.one); b.uv.Add(Vector2.up);
        b.t.AddRange(new[] { v0, v0 + 2, v0 + 1, v0, v0 + 3, v0 + 2 });
    }

    // ================================================================ 5a. beach boardwalk geometry
    /// <summary>Planked boardwalk on low posts, Nb6BwGapM beyond NB2's railing. Returns metres built;
    /// bwSegs = (lane A, lane B) world polylines per continuous run, for the gliders.</summary>
    private static float BuildNb6Boardwalk(Transform group, int i0, int n, float[] shore, bool[] okRow,
                                           out List<List<Vector3>> bwSegs)
    {
        var segs = new List<List<Vector3>>();
        var t = Nb6Group(group, "NB6 Beach Boardwalk");
        var dv = new List<Vector3>(); var duv = new List<Vector2>(); var dt = new List<int>();
        var fv = new List<Vector3>(); var fuv = new List<Vector2>(); var ft = new List<int>();
        float o0 = Nb6RailOffM + Nb6BwGapM, o1 = o0 + Nb6BwW;
        float built = 0f, along = 0f, lastPost = -999f;
        List<Vector3> laneA = null, laneB = null;
        bool open = false; Vector3 prevA = Vector3.zero, prevB = Vector3.zero; float prevY = 0f;
        void Close()
        {
            if (laneA != null && laneA.Count >= 12)
            {
                laneB.Reverse();
                var loop = new List<Vector3>(laneA); loop.AddRange(laneB);
                segs.Add(loop);
            }
            laneA = null; laneB = null; open = false;
        }
        for (int k = 0; k < n; k += 1)
        {
            int i = i0 + k;
            var p = _route.Position[i]; var sd = Nb6Sd(i);
            var a = p + sd * o0; var b = p + sd * o1;
            bool ok = okRow[k] && shore[k] - o1 > Nb6BwMinSeaM && !InAnyPad(a.x, a.z, 2f) && !InAnyPad(b.x, b.z, 2f)
                      && _ground.Coast(b.x, b.z) > 8f;
            if (!ok) { Close(); continue; }
            float ga = _ground.Height(a.x, a.z), gb = _ground.Height(b.x, b.z);
            float y = Mathf.Max(ga, gb, _ground.Height((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f)) + Nb6BwLift;
            if (open) { y = Mathf.Lerp(prevY, y, 0.5f); y = Mathf.Max(y, Mathf.Max(ga, gb) + 0.12f); }
            a.y = y; b.y = y;
            if (open)
            {
                float seg = Vector3.Distance((a + b) * 0.5f, (prevA + prevB) * 0.5f);
                // deck (planks run ACROSS the walk: v along)
                int v0 = dv.Count;
                dv.Add(prevA); dv.Add(prevB); dv.Add(a); dv.Add(b);
                duv.Add(new Vector2(0f, along / 1.2f)); duv.Add(new Vector2(Nb6BwW / 1.2f, along / 1.2f));
                duv.Add(new Vector2(0f, (along + seg) / 1.2f)); duv.Add(new Vector2(Nb6BwW / 1.2f, (along + seg) / 1.2f));
                dt.AddRange(new[] { v0, v0 + 1, v0 + 2, v0 + 1, v0 + 3, v0 + 2 });
                // fascia on both edges, 0.3 m deep (outward faces)
                foreach (var (e0, e1, outward) in new[] { (prevA, a, -sd), (prevB, b, sd) })
                {
                    int f0 = fv.Count;
                    fv.Add(e0); fv.Add(e1); fv.Add(e0 - Vector3.up * 0.3f); fv.Add(e1 - Vector3.up * 0.3f);
                    fuv.Add(new Vector2(0f, 0f)); fuv.Add(new Vector2(seg / 2f, 0f)); fuv.Add(new Vector2(0f, 0.15f)); fuv.Add(new Vector2(seg / 2f, 0.15f));
                    var nrm = Vector3.Cross(e1 - e0, (e0 - Vector3.up * 0.3f) - e0);
                    if (Vector3.Dot(nrm, outward) > 0f) ft.AddRange(new[] { f0, f0 + 1, f0 + 2, f0 + 1, f0 + 3, f0 + 2 });
                    else ft.AddRange(new[] { f0, f0 + 2, f0 + 1, f0 + 1, f0 + 2, f0 + 3 });
                }
                along += seg; built += seg;
                if (along - lastPost > 2.4f)
                {
                    lastPost = along;
                    var tan = Nb6Tan(i);
                    foreach (var e in new[] { a + sd * 0.12f, b - sd * 0.12f })
                    {
                        float g = _ground.Height(e.x, e.z) - 0.25f;
                        float h = e.y - 0.3f - g;
                        if (h > 0.05f) Box(fv, fuv, ft, new Vector3(e.x, g + h * 0.5f, e.z), sd, tan, 0.16f, h, 0.16f);
                    }
                }
            }
            else { laneA = new List<Vector3>(); laneB = new List<Vector3>(); open = true; }
            laneA.Add(Vector3.Lerp(a, b, 0.28f)); laneB.Add(Vector3.Lerp(a, b, 0.72f));
            prevA = a; prevB = b; prevY = y;
        }
        Close();
        if (dt.Count > 0)
        {
            FixWinding(dv, dt);
            AddMesh(t, "NB6 Beach Boardwalk Deck - Beach Sand walk layer", Finish("Nagisa_NB6_BeachBoardwalk", dv, duv, dt), _nb["NB_Boardwalk"], true);
            var f = AddMesh(t, "NB6 Beach Boardwalk Frame", Finish("Nagisa_NB6_BeachBoardwalkFrame", fv, fuv, ft), _nb["NB_Teak"], false);
            f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        }
        foreach (var s in segs) foreach (var q in s) Nb6Occupy(q);
        bwSegs = segs;
        return built;
    }

    // ================================================================ 2. volleyball
    private static int BuildNb6Volleyball(Transform group, Transform peopleT, NbCast cast, System.Random rng,
                                          int i0, int n, float[] shore, bool[] okRow)
    {
        float R() => (float)rng.NextDouble();
        var t = Nb6Group(group, "NB6 Volleyball");
        var ballMat = Cel("NB6_Volleyball", Srgb(250, 236, 120), 0.4f, 0.3f, 0.3f);
        int courts = 0; float last = -Nb6CourtEveryM * 0.5f;
        for (int k = 0; k < n && courts < Nb6Courts; k++)
        {
            int i = i0 + k;
            float d = _route.Distance[i];
            if (!okRow[k] || d - last < Nb6CourtEveryM) continue;
            var sd = Nb6Sd(i); var tan = Nb6Tan(i);
            // court centre: beyond the beach boardwalk, clear of the wet sand
            float o = Nb6RailOffM + Nb6BwGapM + Nb6BwW + 10f;
            if (shore[k] - o < 12f) o = Mathf.Max(Nb6UmbFirstRowM + 4f, shore[k] - 12f);
            var cc = _route.Position[i] + sd * o;
            if (InAnyPad(cc.x, cc.z, 10f) || !CanPlace(cc.x, cc.z, 9f, out float cy, 7f)) continue;
            bool clear = true;
            foreach (var q in new[] { cc, cc + tan * 6f, cc - tan * 6f, cc + sd * 3f, cc - sd * 3f })
                if (!Nb6Free(q, 3.2f)) { clear = false; break; }
            if (!clear) continue;
            last = d;
            cc.y = cy;
            var ct = Nb6Group(t, $"NB6 Court {courts}");
            ct.position = cc;
            float seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            // net across the court (local x = sd), same orientation as NB2's courts
            PlaceWorld("Nagisa_S_VolleyNet", ct, new Vector3(cc.x, cy - 0.05f, cc.z), seaYaw - 90f, 1f, SmallPropCull);
            // rope boundary 16 x 8 m (4 m pieces follow the sand)
            var lv = new List<Vector3>(); var luv = new List<Vector2>(); var lt = new List<int>();
            void Rope(Vector3 a, Vector3 b)
            {
                for (int s = 0; s < 4; s++)
                {
                    var c = Vector3.Lerp(a, b, (s + 0.5f) / 4f);
                    c.y = _ground.Height(c.x, c.z) + 0.015f;
                    var dir = (b - a); dir.y = 0f;
                    Box(lv, luv, lt, c - cc, Vector3.Cross(Vector3.up, dir.normalized), dir.normalized, 0.06f, 0.03f, dir.magnitude / 4f);
                }
            }
            Vector3 C(float x, float z) => cc + tan * x + sd * z;
            Rope(C(-8, -4), C(8, -4)); Rope(C(-8, 4), C(8, 4)); Rope(C(-8, -4), C(-8, 4)); Rope(C(8, -4), C(8, 4));
            var rope = AddMesh(ct, $"NB6 Court {courts} Lines", Finish($"Nagisa_NB6_CourtLines_{courts}", lv, luv, lt), _nb["NB_Canvas"], false);
            rope.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            foreach (var q in new[] { cc, C(-6, 0), C(6, 0), C(-3, 3), C(3, -3), C(-3, -3), C(3, 3) }) Nb6Occupy(q);

            // 2 v 2: team A on -tan, team B on +tan
            var pos = new[] { C(-3.4f, -1.8f), C(-5.6f, 1.9f), C(3.6f, 1.7f), C(5.4f, -2.0f) };
            var items = new List<Transform>(); var bp = new List<Vector3>(); var bd = new List<Vector3>();
            for (int p = 0; p < 4; p++)
            {
                var q = pos[p]; q.y = _ground.Height(q.x, q.z);
                var face = p < 2 ? tan : -tan;
                var go = Nb6Spawn(cast, peopleT, q, face, "Volleyball", p % 2 == 0 ? MK.Wave : MK.Idle, R());
                if (go == null) continue;
                items.Add(go.transform); bp.Add(q); bd.Add(face);
            }
            if (items.Count == 4)
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(ball.GetComponent<Collider>());
                ball.name = "NB6 Volleyball Ball";
                ball.transform.SetParent(ct, true);
                ball.transform.localScale = Vector3.one * 0.23f;
                var mr = ball.GetComponent<MeshRenderer>(); mr.sharedMaterial = ballMat; mr.shadowCastingMode = ShadowCastingMode.On;
                var act = ct.gameObject.AddComponent<NagisaBeachActivity>();
                act.kind = NagisaBeachActivity.Kind.Volley;
                act.cullDistance = Nb6CullM;
                act.items = items.ToArray(); act.basePos = bp.ToArray(); act.baseDir = bd.ToArray();
                act.ball = ball.transform;
                // serve A0 -> B2 bump -> B3 set -> B2 spike -> A1 dig -> A0 set -> A1 spike -> B3 dig -> B2 set -> B3 spike
                act.rally = new[] { 0, 2, 3, 2, 1, 0, 1, 3, 2, 3 };
                act.flightSeconds = 1.05f + R() * 0.2f;
                act.hitHeight = 1.9f * items[0].lossyScale.y;
                act.Step(0.37f * act.flightSeconds);      // bake a mid-flight pose into the saved scene
            }
            // spectators on the landward side (a few on towels)
            for (int s = 0; s < 4; s++)
            {
                var q = C(-6f + s * 3.6f + R(), -7.2f - R() * 1.5f);
                if (!CanPlace(q.x, q.z, 2f, out float qy, 5f) || !Nb6Free(q, 0.9f)) continue;
                q.y = qy;
                Nb6Spawn(cast, peopleT, q, sd, "Spectator", s == 1 ? MK.Wave : MK.Idle, R());
            }
            courts++;
        }
        return courts;
    }

    // ================================================================ 4. beach bar + shack
    private static string BuildNb6BarAndShack(Transform group, Transform peopleT, NbCast cast, System.Random rng,
                                              int i0, int n, float[] shore, bool[] okRow, float bwBuilt)
    {
        float R() => (float)rng.NextDouble();
        foreach (float cand in Nb6HeroCandidatesM)
        {
            int i = _route.IndexAt(_route.BeachStartM + cand);
            int k = i - i0;
            if (k < 0 || k >= n || !okRow[k]) continue;
            var sd = Nb6Sd(i); var tan = Nb6Tan(i);
            float o = Nb6RailOffM + Nb6BwGapM + Nb6BwW + 9f;            // bar centre, seaward of the beach boardwalk
            if (shore[k] - o < 14f) continue;
            var bar = _route.Position[i] + sd * o;
            var shack = bar + tan * 34f;
            bool ok = true;
            foreach (var q in new[] { bar, bar + tan * 8f, bar - tan * 8f, shack, shack + tan * 5f })
                if (InAnyPad(q.x, q.z, 6f) || !CanPlace(q.x, q.z, 8f, out _, 9f) || !Nb6Free(q, 4.5f)) { ok = false; break; }
            if (!ok) continue;

            float seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            // ---- BAR: GLB front (local -z) faces the boardwalk (landward)
            var bt = Nb6Group(group, "NB6 Beach Bar");
            float by = _ground.Height(bar.x, bar.z);
            bt.position = new Vector3(bar.x, by, bar.z);
            PlaceWorld("Nagisa_B_BeachBar", bt, new Vector3(bar.x, by - 0.1f, bar.z), seaYaw, 1f, 0.002f, NbColourway(5), hero: true);
            var front = -sd;
            // timber deck between the building and the boardwalk, with the counter on it
            var dv = new List<Vector3>(); var duv = new List<Vector2>(); var dtri = new List<int>();
            var deckC = bar + front * 9.2f;
            float deckTop = Mathf.Max(_ground.Height(deckC.x, deckC.z), by) + 0.28f;
            Box(dv, duv, dtri, new Vector3(deckC.x, deckTop - 0.2f, deckC.z) - bt.position, tan, front, 15f, 0.4f, 6.5f);
            var deck = AddMesh(bt, "NB6 Bar Deck - Beach Sand walk layer", Finish("Nagisa_NB6_BarDeck", dv, duv, dtri), _nb["NB_Boardwalk"], true);
            // counter (teak body + terrazzo top) and 7 stools on the boardwalk side
            var cv = new List<Vector3>(); var cuv = new List<Vector2>(); var ctri = new List<int>();
            var tv = new List<Vector3>(); var tuv = new List<Vector2>(); var ttri = new List<int>();
            var mv = new List<Vector3>(); var muv = new List<Vector2>(); var mtri = new List<int>();
            var counterC = bar + front * 8.2f;
            Vector3 L(Vector3 w, float up) => new Vector3(w.x, deckTop + up, w.z) - bt.position;
            Box(cv, cuv, ctri, L(counterC, 0.52f), tan, front, 8f, 1.04f, 0.62f);
            Box(tv, tuv, ttri, L(counterC + front * 0.06f, 1.08f), tan, front, 8.3f, 0.07f, 0.8f);
            for (int s = 0; s < 7; s++)
            {
                var sp = counterC + front * 0.95f + tan * (-3.3f + s * 1.1f);
                Cyl(mv, muv, mtri, L(sp, 0.37f), 0.035f, 0.74f, 8);        // pole
                Cyl(mv, muv, mtri, L(sp, 0.03f), 0.2f, 0.05f, 12);         // foot
                Cyl(cv, cuv, ctri, L(sp, 0.77f), 0.2f, 0.07f, 14);         // seat
            }
            AddMesh(bt, "NB6 Bar Counter", Finish("Nagisa_NB6_BarCounter", cv, cuv, ctri), _nb["NB_Teak"], false);
            AddMesh(bt, "NB6 Bar Counter Top", Finish("Nagisa_NB6_BarCounterTop", tv, tuv, ttri), _nb["NB_Terrazzo"], false);
            AddMesh(bt, "NB6 Bar Stools", Finish("Nagisa_NB6_BarStools", mv, muv, mtri), _nb["NB_Chrome"], false);
            Nb6Sign(bt, "NB6_Sign_BeachBar", counterC + front * 2.6f + tan * 5.6f, front, deckTop, 3.4f, 1.06f);
            // bartender behind the counter, patrons at the counter and on the deck
            Nb6Spawn(cast, peopleT, new Vector3(counterC.x, deckTop, counterC.z) - front * 0.9f, front, "Bartender", MK.Wave, R());
            Nb6Spawn(cast, peopleT, new Vector3(counterC.x, deckTop, counterC.z) - front * 0.9f + tan * 2.4f, front, "Bartender", MK.Idle, R());
            for (int s = 0; s < 7; s++)
            {
                if (s == 3) continue;
                var sp = counterC + front * (1.45f + R() * 0.3f) + tan * (-3.3f + s * 1.1f + 0.5f);
                Nb6Spawn(cast, peopleT, new Vector3(sp.x, deckTop, sp.z), -front + tan * (R() - 0.5f) * 0.6f, "BarGuest",
                         R() < 0.4f ? MK.Wave : MK.Idle, R());
            }
            for (int s = 0; s < 4; s++)
            {
                var sp = deckC + front * (1.8f + R()) + tan * (-6f + s * 3.8f);
                var q = new Vector3(sp.x, deckTop, sp.z);
                Nb6Spawn(cast, peopleT, q, tan * (s % 2 == 0 ? 1f : -1f), "BarGuest", MK.Idle, R());
                Nb6Spawn(cast, peopleT, q + tan * (s % 2 == 0 ? 1.1f : -1.1f), tan * (s % 2 == 0 ? -1f : 1f), "BarGuest", MK.Wave, R());
            }
            _ = deck;

            // ---- SHACK: pass-1 surf shack GLB + sign + rental rack + customers
            var st = Nb6Group(group, "NB6 Beach Shack");
            float sy = _ground.Height(shack.x, shack.z);
            st.position = new Vector3(shack.x, sy, shack.z);
            PlaceWorld("Nagisa_SurfShop", st, new Vector3(shack.x, sy - 0.08f, shack.z), seaYaw + 180f, 1f, 0.002f, null, hero: true);
            var sfront = shack + front * 5.2f;
            Nb6Sign(st, "NB6_Sign_Shack", sfront - tan * 3.2f, front, _ground.Height(sfront.x, sfront.z), 2.8f, 0.88f);
            var rack = shack + front * 4.5f + tan * 4.2f;
            if (CanPlace(rack.x, rack.z, 2f, out float ry, 5f))
            {
                PlaceWorld("Nagisa_S_RentalStand", st, new Vector3(rack.x, ry - 0.05f, rack.z), seaYaw + 180f, 1f, SmallPropCull);
                for (int b = 0; b < 4; b++)
                {
                    var bpos = rack + tan * (1.6f + b * 0.55f); bpos.y = _ground.Height(bpos.x, bpos.z);
                    PlaceWorld(b == 2 ? "Nagisa_S_SUP" : "Nagisa_S_Surfboard", st, bpos, seaYaw + 90f, 1f, SmallPropCull);
                }
            }
            for (int s = 0; s < 5; s++)
            {
                var q = shack + front * (5.6f + R() * 2.5f) + tan * (-3f + s * 1.5f);
                q.y = _ground.Height(q.x, q.z);
                Nb6Spawn(cast, peopleT, q, sd + tan * (R() - 0.5f), s == 0 ? "ShackAttendant" : "ShackCustomer", s == 2 ? MK.Wave : MK.Idle, R());
            }
            foreach (var q in new[] { bar, deckC, shack, rack }) Nb6Occupy(q);
            return $"at {cand + _route.BeachStartM:0} m";
        }
        Debug.LogWarning("[nb6] no clear site for the beach bar + shack.");
        return "NOT PLACED";
    }

    /// <summary>Post-mounted sign: a textured front quad (0..1 UV) on a teak backing + two posts.</summary>
    private static void Nb6Sign(Transform parent, string tex, Vector3 at, Vector3 face, float groundY, float w, float h)
    {
        var side = Vector3.Cross(Vector3.up, face).normalized;
        float bottom = groundY + 1.35f;
        var c = new Vector3(at.x, bottom + h * 0.5f, at.z) - parent.position;
        var fv = new List<Vector3>(); var fuv = new List<Vector2>(); var ft = new List<int>();
        var o = c + face * 0.061f;
        // front quad seen from +face: left = -side from the viewer's right-handed frame
        var l = side; // viewer looking along -face sees +side on their LEFT? compute below
        Vector3 p00 = o - l * (w * 0.5f) - Vector3.up * (h * 0.5f), p10 = o + l * (w * 0.5f) - Vector3.up * (h * 0.5f);
        Vector3 p11 = o + l * (w * 0.5f) + Vector3.up * (h * 0.5f), p01 = o - l * (w * 0.5f) + Vector3.up * (h * 0.5f);
        // text must read left->right for a viewer at +face looking back (-face): their right = Cross(up, -face) = -side
        bool mirror = Vector3.Dot(Vector3.Cross(Vector3.up, -face), l) < 0f;
        fv.Add(p00); fv.Add(p10); fv.Add(p11); fv.Add(p01);
        if (mirror) { fuv.Add(new Vector2(1, 0)); fuv.Add(new Vector2(0, 0)); fuv.Add(new Vector2(0, 1)); fuv.Add(new Vector2(1, 1)); }
        else { fuv.Add(new Vector2(0, 0)); fuv.Add(new Vector2(1, 0)); fuv.Add(new Vector2(1, 1)); fuv.Add(new Vector2(0, 1)); }
        var nrm = Vector3.Cross(p10 - p00, p11 - p00);
        if (Vector3.Dot(nrm, face) > 0f) ft.AddRange(new[] { 0, 1, 2, 0, 2, 3 }); else ft.AddRange(new[] { 0, 2, 1, 0, 3, 2 });
        var m = Cel("NB6_" + tex, Color.white, 0.2f, 0.1f, 0.1f, Tex(NagisaTex + "/NB6", tex + ".png"));
        var sgo = AddMesh(parent, $"NB6 Sign {tex}", Finish($"Nagisa_{tex}_Face", fv, fuv, ft), m, false);
        sgo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bt = new List<int>();
        Box(bv, buv, bt, c, side, face, w + 0.16f, h + 0.16f, 0.1f);
        foreach (float s in new[] { -1f, 1f })
            Box(bv, buv, bt, new Vector3(c.x, groundY - 0.3f + (bottom + 0.2f - groundY + 0.3f) * 0.5f, c.z) + side * (s * w * 0.42f) - face * 0.1f,
                side, face, 0.12f, bottom + 0.2f - groundY + 0.3f, 0.12f);
        AddMesh(parent, $"NB6 Sign {tex} Frame", Finish($"Nagisa_{tex}_Frame", bv, buv, bt), _nb["NB_Teak"], false);
    }

    /// <summary>Closed cylinder (y-up) centred at c.</summary>
    private static void Cyl(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 c, float r, float h, int seg)
    {
        int b0 = v.Count;
        for (int s = 0; s <= seg; s++)
        {
            float a = s / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            v.Add(c + d - Vector3.up * h * 0.5f); v.Add(c + d + Vector3.up * h * 0.5f);
            uv.Add(new Vector2(s / (float)seg, 0f)); uv.Add(new Vector2(s / (float)seg, h));
        }
        for (int s = 0; s < seg; s++)
        {
            int a = b0 + s * 2;
            // outward: (a, a+1, a+2) winding checked against the radial direction
            var n = Vector3.Cross(v[a + 1] - v[a], v[a + 2] - v[a]);
            var radial = (v[a] + v[a + 2]) * 0.5f - c; radial.y = 0f;
            if (Vector3.Dot(n, radial) > 0f) t.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            else t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        foreach (float sgn in new[] { 1f, -1f })
        {
            int ci = v.Count; v.Add(c + Vector3.up * (h * 0.5f * sgn)); uv.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s < seg; s++)
            {
                int a = b0 + s * 2 + (sgn > 0f ? 1 : 0), b = b0 + (s + 1) * 2 + (sgn > 0f ? 1 : 0);
                var n = Vector3.Cross(v[a] - v[ci], v[b] - v[ci]);
                if (n.y * sgn > 0f) t.AddRange(new[] { ci, a, b }); else t.AddRange(new[] { ci, b, a });
            }
        }
    }

    // ================================================================ 3. shoreline walkers + swash foam
    private static int BuildNb6Shoreline(Transform group, Transform peopleT, NbCast cast, System.Random rng,
                                         int i0, int n, float[] shore, string meshTag = "NB6")
    {
        float R() => (float)rng.NextDouble();
        int walkers = 0; float last = -999f;
        for (int k = 0; k < n; k++)
        {
            int i = i0 + k;
            float d = _route.Distance[i];
            if (shore[k] < 0f || d - last < Nb6ShoreWalkEveryM) continue;
            int j = _route.IndexAt(d + 22f + R() * 10f) - i0;
            if (j >= n || shore[j] < 0f) continue;
            last = d;
            float wetA = shore[k] - (2.2f + R() * 2.5f), wetB = shore[j] - (2.2f + R() * 2.5f);
            var a = Nb6At(i, wetA); var b = Nb6At(i0 + j, wetB);
            if (InAnyPad(a.x, a.z, 2f) || InAnyPad(b.x, b.z, 2f)) continue;
            bool fwd = R() < 0.5f;
            var s0 = fwd ? a : b; var s1 = fwd ? b : a;
            var p = Walker(cast, peopleT, "ShoreWalker", s0, s1, R(), 0.62f + R() * 0.3f);
            if (p != null) walkers++;
            if (R() < 0.35f)
            {
                var lat = Vector3.Cross(Vector3.up, (s1 - s0).normalized) * 0.7f;
                if (Walker(cast, peopleT, "ShoreWalker", s0 + lat, s1 + lat, R(), 0.7f) != null) walkers++;
            }
        }

        // foam: one mesh per ~28 m of shoreline for the swash and one for the breaker line
        var t = Nb6Group(group, "NB6 Shore Foam");
        var foam = Cel("NB6_Foam", new Color(0.97f, 0.99f, 1f, 1f), 0.5f, 0.4f, 0.35f, null, null, 0.8f, 0f);
        foam.SetColor("_ShadeColor", new Color(0.72f, 0.86f, 0.93f, 1f));
        var items = new List<Transform>(); var bp = new List<Vector3>(); var bd = new List<Vector3>();
        var reach = new List<float>(); var phase = new List<float>(); var lo = new List<float>();
        int segN = 0;
        int kk = 0;
        while (kk < n)
        {
            if (shore[kk] < 0f) { kk++; continue; }
            int ks = kk; float d0 = _route.Distance[i0 + kk];
            while (kk < n && shore[kk] >= 0f && _route.Distance[i0 + kk] - d0 < Nb6FoamSegM) kk++;
            int ke = Mathf.Min(n - 1, kk);
            if (ke - ks < 2) continue;
            int im = i0 + (ks + ke) / 2;
            var sdm = Nb6Sd(im);
            foreach (var (inner, width, rch, depth, ph, name) in new[]
                     { (-1.3f, 2.1f, 3.8f, -0.14f, 0.0f, "Swash"), (-11f, 1.5f, 6.5f, -0.45f, 0.62f, "Breaker") })
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                var origin = _route.Position[im] + sdm * (shore[(ks + ke) / 2] + inner); origin.y = SeaLevelY + 0.035f;
                for (int q = ks; q <= ke; q++)
                {
                    if (shore[q] < 0f) continue;
                    var sd = Nb6Sd(i0 + q);
                    float jag = Mathf.PerlinNoise(q * 0.61f, segN * 1.7f + inner) - 0.5f;
                    var a = _route.Position[i0 + q] + sd * (shore[q] + inner - width * 0.5f + jag * 0.5f);
                    var b = _route.Position[i0 + q] + sd * (shore[q] + inner + width * 0.5f + jag * 0.9f);
                    a.y = b.y = SeaLevelY + 0.035f;
                    v.Add(a - origin); v.Add(b - origin);
                    float u = (q - ks) * 0.37f; uv.Add(new Vector2(u, 0f)); uv.Add(new Vector2(u, 1f));
                    int c = v.Count;
                    if (c >= 4) tri.AddRange(new[] { c - 4, c - 3, c - 2, c - 3, c - 1, c - 2 });
                }
                if (tri.Count == 0) continue;
                FixWinding(v, tri);
                var go = AddMesh(t, $"NB6 {name} Foam {segN:D3}", Finish($"Nagisa_{meshTag}_{name}Foam_{segN:D3}", v, uv, tri), foam, false);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                go.transform.position = origin;
                items.Add(go.transform); bp.Add(origin); bd.Add(sdm);
                reach.Add(rch); lo.Add(depth);
                phase.Add(Mathf.Repeat(ph + segN * 0.035f + (float)rng.NextDouble() * 0.04f, 1f));
            }
            segN++;
        }
        if (items.Count > 0)
        {
            var act = t.gameObject.AddComponent<NagisaBeachActivity>();
            act.kind = NagisaBeachActivity.Kind.Swash;
            act.cullDistance = 5000f;       // one driver for the whole beach: cheap transforms only
            act.items = items.ToArray(); act.basePos = bp.ToArray(); act.baseDir = bd.ToArray();
            act.reach = reach.ToArray(); act.phase = phase.ToArray(); act.depthLo = lo.ToArray();
            act.swashPeriod = 7.5f;
            act.Step(2.2f);
        }
        Debug.Log($"[nb6] shoreline: {walkers} walkers, {segN} foam segments ({items.Count} strips).");
        return walkers;
    }

    // ================================================================ 5b. skaters / rollerbladers / runners
    private static int BuildNb6Gliders(Transform group, Transform peopleT, NbCast cast, System.Random rng,
                                       List<List<Vector3>> bwLoops)
    {
        float R() => (float)rng.NextDouble();
        var t = Nb6Group(group, "NB6 Boardwalk Gliders");
        var boardMat = Cel("NB6_Skateboard", Srgb(226, 88, 60), 0.3f, 0.2f, 0.2f);
        var wheelMat = _nb["NB_Rubber"];

        // NB2's promenade boardwalk deck (12.4 .. 15.6 m, top = paving edge + BoardwalkLift):
        // two lanes, out on 12.9 m and back on 14.0 m, per continuous run
        var loops = new List<List<Vector3>>(bwLoops);
        {
            int a0 = _route.IndexAt(_route.BeachStartM), a1 = _route.IndexAt(_route.BeachEndM);
            List<Vector3> la = null, lb = null;
            void Close() { if (la != null && la.Count >= 12) { lb.Reverse(); la.AddRange(lb); loops.Add(la); } la = null; lb = null; }
            for (int i = a0; i <= a1; i++)
            {
                var p = _route.Position[i]; var sd = Nb6Sd(i);
                var b = p + sd * PromenadeOuterM; var c = p + sd * (PromenadeOuterM + BoardwalkW);
                var mid = (p + sd * PromenadeInnerM + b) * 0.5f;
                bool ok = _ground.Coast(c.x, c.z) > 2f && !NearDrive(mid.x) && !_route.OnBridge(i);
                if (!ok) { Close(); continue; }
                float deckY = _ground.Height(b.x, b.z) + 0.07f + BoardwalkLift - 0.07f;
                if (la == null) { la = new List<Vector3>(); lb = new List<Vector3>(); }
                var qa = p + sd * (PromenadeOuterM + 0.5f); qa.y = deckY;
                var qb = p + sd * (PromenadeOuterM + 1.6f); qb.y = deckY;
                la.Add(qa); lb.Add(qb);
            }
            Close();
        }

        int count = 0, li = 0;
        foreach (var loop in loops)
        {
            var lt = Nb6Group(t, $"NB6 Glide Loop {li++}");
            var act = lt.gameObject.AddComponent<NagisaBeachActivity>();
            act.kind = NagisaBeachActivity.Kind.Glide;
            act.cullDistance = Nb6CullM;
            act.path = loop.ToArray();
            float len = act.PathLength;
            int m = Mathf.Max(1, Mathf.RoundToInt(len / Nb6GlideEveryM));
            var items = new List<Transform>(); var sp = new List<float>(); var s0 = new List<float>();
            var sw = new List<float>(); var ly = new List<float>();
            for (int q = 0; q < m; q++)
            {
                float roll = R();
                int kind = roll < 0.34f ? 0 : roll < 0.67f ? 1 : 2;   // skater / rollerblader / runner
                float start = (q + R() * 0.6f) / m * len;
                var pos = act.SamplePath(start, out var fwd);
                GameObject go;
                if (kind == 2)
                {
                    go = NbPerson(cast.walk, peopleT, "Runner", pos, fwd, MK.Walk, R());
                    if (go != null) { var a = go.GetComponent<MinatoCrowdActor>(); if (a != null) a.moveSpeed = 2.4f; }
                }
                else go = NbPerson(cast.stand, peopleT, kind == 0 ? "Skater" : "Rollerblader", pos, fwd, MK.Idle, R());
                if (go == null) continue;
                FinishPerson(go);
                go.transform.SetParent(lt, true);
                if (kind == 0) Nb6Skateboard(go.transform, boardMat, wheelMat);
                items.Add(go.transform);
                sp.Add(kind == 0 ? 3.6f + R() * 1.2f : kind == 1 ? 3.0f + R() * 1.1f : 2.5f + R() * 0.7f);
                s0.Add(start);
                sw.Add(kind == 1 ? 0.22f : kind == 0 ? 0.05f : 0f);
                ly.Add(kind == 0 ? 0.115f : kind == 1 ? 0.07f : 0f);
            }
            act.items = items.ToArray(); act.speeds = sp.ToArray(); act.startS = s0.ToArray();
            act.sway = sw.ToArray(); act.liftY = ly.ToArray();
            act.Step(0f);
            count += items.Count;
        }
        return count;
    }

    private static void Nb6Skateboard(Transform person, Material deck, Material wheel)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Box(v, uv, t, new Vector3(0f, 0.085f, 0f), Vector3.right, Vector3.forward, 0.22f, 0.025f, 0.80f);
        var w = new List<Vector3>(); var wuv = new List<Vector2>(); var wt = new List<int>();
        foreach (float z in new[] { -0.27f, 0.27f })
            foreach (float x in new[] { -0.09f, 0.09f })
                Box(w, wuv, wt, new Vector3(x, 0.03f, z), Vector3.right, Vector3.forward, 0.045f, 0.06f, 0.06f);
        var g = new GameObject("NB6 Skateboard");
        g.transform.SetParent(person, false);
        float s = person.lossyScale.x > 1e-3f ? 1f / person.lossyScale.x : 1f;
        g.transform.localScale = Vector3.one * s;
        g.transform.localPosition = new Vector3(0f, -0.115f * s, 0f);
        g.transform.localRotation = Quaternion.identity;
        var d = AddMeshShared(g.transform, "Deck", _nb6BoardMesh ??= Finish("Nagisa_NB6_SkateDeck", v, uv, t), deck);
        var wh = AddMeshShared(g.transform, "Wheels", _nb6WheelMesh ??= Finish("Nagisa_NB6_SkateWheels", w, wuv, wt), wheel);
        d.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        _ = wh;
    }
    private static Mesh _nb6BoardMesh, _nb6WheelMesh;

    /// <summary>AddMesh for a mesh that is already an asset (shared by many instances).</summary>
    private static GameObject AddMeshShared(Transform parent, string name, Mesh mesh, Material mat)
    {
        if (!AssetDatabase.Contains(mesh)) return AddMesh(parent, name, mesh, mat, false);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }
}

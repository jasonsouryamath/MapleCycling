// NB4 (COORDINATION.md "PRIORITY 1: Nagisa Bay overhaul"): people on foot + runners, SEA SIDE ONLY,
// on the two coastal sections (the same spans NB13 paves). copilot CLI, 2026-09-29.
//
// Donors (N rules 2/3/8): clones of the Minato "Crowd_*" donors already in the scene (NbCrowdCast),
// never MinatoCrowdPopulation.Prepare, dressed with MapleCityLife.DressForRegion using the NEW look
// sets from tools/blender/looks/nagisa_looks2.py: "NR" resort (rash guards, bikinis + sarongs, sun
// hats, sunglasses...) and "NJ" running kit. No helmets (the looks cap the head with hair / sun hat).
//
// What this stage ADDS (NB2 Life/Life2, NB6 BeachLife and NB13 ResortInfill people are counted, never
// duplicated or touched):
//   * runners with the real run gait (MapleCityRunner + the donors' baked run cycles) on closed
//     promenade loops (out on the road-side edge, back on the sea-side edge = both directions);
//   * skateboarders gliding on the same loops;
//   * vignettes along the promenade: shoppers with bags, couples, families with strollers + kids,
//     dog walkers, beach-goers carrying boards down to the sand, chatting groups, bench sitters;
//   * queues at the existing sea-side kiosks / food trucks / shave-ice / rental stands;
//   * street musicians with a small crowd.
// Every spot: sea side (SeaSign), ClearOfRoad (corridor + margin), dry land, off the pads, and a
// clearance test against every existing prop (LODGroup) and person in the region.
//
// Perf: LOD0 skinned -> LOD1 "Low 3D" -> culled (the donor LOD policy, which is the far "impostor"
// tier), LOD1 shadowless; MinatoCrowdActor animates only within its 115 m gate, MapleCityRunner within
// 90 m, lane movers update fully within 260 m and every 30th frame beyond (all well inside the
// 800 m brief). Carried props are added to LOD0 so they cull with the person.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- PROVISIONAL tuning (NB4)
    private static readonly Vector2[] Nb4Spans = { new Vector2(0f, 4720f), new Vector2(12167f, 14991f) };
    private const float Nb4LaneOutM = 9.0f, Nb4LaneBackM = 12.05f;   // promenade band edges (NB13 paves 7.45-12.35)
    private const float Nb4LaneLatM = 0.25f;                           // runner overtaking room each side
    private const float Nb4LaneStepM = 4f, Nb4LaneMinM = 120f;
    private const int Nb4RunnerTarget = 120, Nb4SkaterTarget = 26;
    private const float Nb4RunSpeedMin = 2.6f, Nb4RunSpeedMax = 3.4f;
    private const float Nb4SkateSpeedMin = 2.6f, Nb4SkateSpeedMax = 3.6f, Nb4DeckTopM = 0.11f;
    private const float Nb4SlotStepM = 24f;                            // along-shore pitch of vignette slots
    private const float Nb4PersonGapM = 0.85f, Nb4PropGapM = 0.8f;
    private const int Nb4MusicianSites = 4, Nb4QueueMin = 3, Nb4QueueMax = 6;
    private const string Nb4Look = "NR", Nb4RunLook = "NJ";
    private const string Nb4AnimDir = "Assets/Environment/MapleCity/Life/Anim";
    private const string Nb4GroupName = "NB Crowd";

    private static int _nb4N;
    private static MinatoCrowdActor _nb4LastSrc;
    private static readonly Dictionary<string, int> _nb4Roles = new Dictionary<string, int>();

    // ---------------------------------------------------------------- occupancy
    private sealed class Nb4Occ
    {
        private const float Cell = 4f;
        private readonly Dictionary<long, List<Vector4>> _props = new Dictionary<long, List<Vector4>>();
        private readonly Dictionary<long, List<Vector4>> _people = new Dictionary<long, List<Vector4>>();
        private static long K(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(bool person, Vector3 p, float r)
        {
            var map = person ? _people : _props;
            int x0 = Mathf.FloorToInt((p.x - r) / Cell), x1 = Mathf.FloorToInt((p.x + r) / Cell);
            int z0 = Mathf.FloorToInt((p.z - r) / Cell), z1 = Mathf.FloorToInt((p.z + r) / Cell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!map.TryGetValue(K(x, z), out var l)) map[K(x, z)] = l = new List<Vector4>();
                    l.Add(new Vector4(p.x, p.y, p.z, r));
                }
        }

        public bool Free(Vector3 p, float personGap, float propGap)
        {
            long k = K(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
            if (_people.TryGetValue(k, out var a))
                foreach (var e in a) if (Sq(p, e) < (e.w + personGap) * (e.w + personGap)) return false;
            if (_props.TryGetValue(k, out var b))
                foreach (var e in b) if (Sq(p, e) < (e.w + propGap) * (e.w + propGap)) return false;
            // neighbours (entries are registered in every cell their radius touches, so one ring is enough)
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    long n = K(Mathf.FloorToInt(p.x / Cell) + dx, Mathf.FloorToInt(p.z / Cell) + dz);
                    if (_people.TryGetValue(n, out var c))
                        foreach (var e in c) if (Sq(p, e) < (e.w + personGap) * (e.w + personGap)) return false;
                    if (_props.TryGetValue(n, out var d))
                        foreach (var e in d) if (Sq(p, e) < (e.w + propGap) * (e.w + propGap)) return false;
                }
            return true;
        }

        public bool FreePath(Vector3 a, Vector3 b, float gap)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 1f));
            for (int k = 0; k <= n; k++)
                if (!Free(Vector3.Lerp(a, b, k / (float)n), 0f, gap)) return false;
            return true;
        }

        private static float Sq(Vector3 p, Vector4 e) { float dx = p.x - e.x, dz = p.z - e.z; return dx * dx + dz * dz; }
    }

    private static int _nb4Existing, _nb4ExistingRunners, _nb4ExistingNb13;

    private static Nb4Occ Nb4SeedOcc(Transform root, Transform mine)
    {
        var occ = new Nb4Occ();
        _nb4Existing = 0; _nb4ExistingRunners = 0; _nb4ExistingNb13 = 0;
        foreach (var a in root.GetComponentsInChildren<MinatoCrowdActor>(true))
        {
            if (a.transform.IsChildOf(mine)) continue;
            _nb4Existing++;
            string n = a.name;
            if (n.Contains("Runner") || n.Contains("Jogger")) _nb4ExistingRunners++;
            if (n.Contains("ResortGuest") || n.Contains("ResortRunner") || n.Contains("CabanaGuest")) _nb4ExistingNb13++;
            occ.Add(true, a.transform.position, 0.35f);
        }
        foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
        {
            if (g.transform.IsChildOf(mine) || g.GetComponent<MinatoCrowdActor>() != null) continue;
            if (g.GetComponentInParent<MinatoCrowdActor>() != null) continue;     // carried props
            float r = Mathf.Clamp(g.size * 0.5f * g.transform.lossyScale.x * 0.8f, 0.25f, 6f);
            // big buildings are far inland; only the footprint near the promenade matters
            occ.Add(false, g.transform.TransformPoint(g.localReferencePoint), r);
        }
        return occ;
    }

    // ---------------------------------------------------------------- surfaces
    /// <summary>Walkable height at p: the highest upward "walk layer" collider near the terrain, else the terrain.</summary>
    private static float Nb4Surface(Vector3 p)
    {
        float gy = _ground.Height(p.x, p.z), best = gy;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, gy + 2.5f, p.z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.normal.y < 0.9f || !h.collider.name.Contains("walk layer")) continue;
            if (h.point.y > best && h.point.y < gy + 0.6f) best = h.point.y;
        }
        return best;
    }

    private static Vector3 Nb4At(int i, float offset)
    {
        var p = _route.Position[i] + _route.SideFlat(i) * SeaSign(i) * offset;
        p.y = Nb4Surface(p);
        return p;
    }

    private static bool Nb4Dry(Vector3 p, float coast = 2f) =>
        _ground.Coast(p.x, p.z) >= coast && !InAnyPad(p.x, p.z, 3f) && ClearOfRoad(p) && _ground.Height(p.x, p.z) > 0.25f;

    private static bool Nb4InSpans(float d)
    {
        foreach (var s in Nb4Spans) if (d >= s.x && d <= s.y) return true;
        return false;
    }

    // ---------------------------------------------------------------- cloning
    private static GameObject Nb4Clone(List<MinatoCrowdActor> pool, Transform parent, string role, string look, string dressAs,
                                       Vector3 pos, Vector3 face, MK kind, float seed, Nb4Occ occ,
                                       Vector3? end = null, float speed = 0f, float scale = 1f)
    {
        if (pool == null || pool.Count == 0) return null;
        if (!ClearOfRoad(pos) || (end.HasValue && !ClearOfRoad(end.Value))) return null;
        seed = Mathf.Repeat(seed, 1f);
        var src = pool[Mathf.Abs((int)(seed * 9973f) + _nb4N * 7919) % pool.Count];
        _nb4LastSrc = src;
        var go = Object.Instantiate(src.gameObject, parent);
        go.hideFlags = HideFlags.None;
        go.SetActive(true);
        bool seated = kind == MK.Sit;
        var bench = go.transform.Find("Timber Waterfront Bench");
        if (bench != null && !seated) Object.DestroyImmediate(bench.gameObject);
        face.y = 0f;
        if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
        if (seated) face = -face;   // seated donors face local -Z
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face.normalized, Vector3.up));
        go.transform.localScale = src.transform.localScale * Mathf.Lerp(0.95f, 1.05f, seed) * scale;
        var high = go.transform.Find("LOD0 High Skinned");
        var rig = high != null ? high.Find("Rigged Character") : null;
        var actor = go.GetComponent<MinatoCrowdActor>();
        if (actor != null && rig != null)
        {
            float v = kind == MK.Walk ? (speed > 0f ? speed : Mathf.Lerp(0.6f, 0.95f, seed)) : 0f;
            actor.Configure(kind, rig, pos, end ?? pos, v, seed);
            if (kind == MK.Walk) actor.moveSpeed = v;
            actor.armDropDegrees = seated ? 0f : 32f;
        }
        // the name decides MapleCityLook's role; the look prefix decides the wardrobe
        go.name = dressAs + "NB4_" + role;
        try { MapleCityLife.DressForRegion(go, src.name, look, seated); }
        catch (System.Exception ex) { Debug.LogWarning($"[nb4] dress failed for {role}: {ex.Message}"); }
        go.name = $"Nagisa NB4 {role} {_nb4N:D4}";
        FinishPerson(go);
        _nb4N++;
        _nb4Roles[role] = _nb4Roles.TryGetValue(role, out int n) ? n + 1 : 1;
        occ?.Add(true, pos, 0.35f);
        if (end.HasValue) occ?.Add(true, end.Value, 0.35f);
        return go;
    }

    private static GameObject Nb4Walker(NbCast cast, Transform parent, string role, Vector3 a, Vector3 b, float seed,
                                        Nb4Occ occ, float speed = 0f, float scale = 1f) =>
        Nb4Clone(cast.walk, parent, role, Nb4Look, "Walker_", a, b - a, MK.Walk, seed, occ, b, speed, scale);

    private static GameObject Nb4Stander(NbCast cast, Transform parent, string role, Vector3 p, Vector3 face, float seed,
                                         Nb4Occ occ, bool wave = false) =>
        Nb4Clone(cast.stand, parent, role, Nb4Look, "Chat_", p, face, wave ? MK.Wave : MK.Idle, seed, occ);

    /// <summary>Parents a prop to a bone so it follows the arm swing, and culls it with LOD0.</summary>
    private static void Nb4Carry(GameObject person, string bone, GameObject prop, Vector3 worldOffset)
    {
        if (person == null || prop == null) return;
        Transform b = null;
        foreach (var t in person.GetComponentsInChildren<Transform>(true)) if (t.name == bone) { b = t; break; }
        var at = b != null ? b : person.transform;
        prop.transform.SetPositionAndRotation(at.position + worldOffset, person.transform.rotation);
        prop.transform.SetParent(at, true);
        Nb4AddToLod0(person, prop);
    }

    private static void Nb4AddToLod0(GameObject person, GameObject prop)
    {
        var g = person.GetComponent<LODGroup>();
        var rs = prop.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rs) r.shadowCastingMode = ShadowCastingMode.Off;
        if (g == null || rs.Length == 0) return;
        var lods = g.GetLODs();
        if (lods.Length == 0) return;
        var list = new List<Renderer>(lods[0].renderers); list.AddRange(rs);
        lods[0].renderers = list.ToArray();
        g.SetLODs(lods);
    }

    // ---------------------------------------------------------------- small carried props (procedural, bevelled)
    private static readonly Dictionary<string, Mesh> _nb4Mesh = new Dictionary<string, Mesh>();

    private static Mesh Nb4MeshAsset(string name, System.Action<List<Vector3>, List<Vector2>, List<int>> build)
    {
        if (_nb4Mesh.TryGetValue(name, out var m) && m != null) return m;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        build(v, uv, t);
        m = Finish(name, v, uv, t);
        string path = $"{MeshDir}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        _nb4Mesh[name] = m;
        return m;
    }

    /// <summary>Rounded box (chamfered corners) centred at c, half extents h, bevel b.</summary>
    private static void Nb4RBox(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 c, Vector3 h, float b)
    {
        // a chamfered box = 8-sided prism in XZ, capped top and bottom with a bevel ring
        var ring = new List<Vector2>();
        for (int k = 0; k < 8; k++)
        {
            float sx = (k == 0 || k == 7) ? 1 : (k == 3 || k == 4) ? -1 : 0;
            float sz = (k == 1 || k == 2) ? 1 : (k == 5 || k == 6) ? -1 : 0;
            // corners chamfered: alternate x-edge / z-edge points
            Vector2 p;
            switch (k)
            {
                case 0: p = new Vector2(h.x, h.z - b); break;
                case 1: p = new Vector2(h.x - b, h.z); break;
                case 2: p = new Vector2(-h.x + b, h.z); break;
                case 3: p = new Vector2(-h.x, h.z - b); break;
                case 4: p = new Vector2(-h.x, -h.z + b); break;
                case 5: p = new Vector2(-h.x + b, -h.z); break;
                case 6: p = new Vector2(h.x - b, -h.z); break;
                default: p = new Vector2(h.x, -h.z + b); break;
            }
            ring.Add(p);
        }
        float[] ys = { -h.y, -h.y + b, h.y - b, h.y };
        float[] sc = { 0.82f, 1f, 1f, 0.82f };
        int baseI = v.Count;
        for (int r = 0; r < 4; r++)
            for (int k = 0; k < 8; k++)
            {
                v.Add(c + new Vector3(ring[k].x * sc[r], ys[r], ring[k].y * sc[r]));
                uv.Add(new Vector2(k / 8f, r / 3f));
            }
        for (int r = 0; r < 3; r++)
            for (int k = 0; k < 8; k++)
            {
                int a = baseI + r * 8 + k, bb = baseI + r * 8 + (k + 1) % 8, cc = a + 8, d = bb + 8;
                t.AddRange(new[] { a, cc, bb, bb, cc, d });
            }
        int top = v.Count; v.Add(c + new Vector3(0, h.y, 0)); uv.Add(new Vector2(0.5f, 1));
        int bot = v.Count; v.Add(c + new Vector3(0, -h.y, 0)); uv.Add(new Vector2(0.5f, 0));
        for (int k = 0; k < 8; k++)
        {
            t.AddRange(new[] { baseI + 24 + k, top, baseI + 24 + (k + 1) % 8 });
            t.AddRange(new[] { baseI + (k + 1) % 8, bot, baseI + k });
        }
    }

    private static void Nb4Cyl(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 c, float r, float halfW, int seg = 10)
    {
        int b0 = v.Count;
        for (int s = 0; s < 2; s++)
            for (int k = 0; k < seg; k++)
            {
                float a = k * Mathf.PI * 2f / seg;
                v.Add(c + new Vector3(s == 0 ? -halfW : halfW, Mathf.Sin(a) * r, Mathf.Cos(a) * r));
                uv.Add(new Vector2(k / (float)seg, s));
            }
        for (int k = 0; k < seg; k++)
        {
            int a = b0 + k, b = b0 + (k + 1) % seg, cc = a + seg, d = b + seg;
            t.AddRange(new[] { a, b, cc, b, d, cc });
        }
        for (int s = 0; s < 2; s++)
        {
            int ci = v.Count; v.Add(c + new Vector3(s == 0 ? -halfW : halfW, 0, 0)); uv.Add(new Vector2(0.5f, 0.5f));
            for (int k = 0; k < seg; k++)
            {
                int a = b0 + s * seg + k, b = b0 + s * seg + (k + 1) % seg;
                if (s == 0) t.AddRange(new[] { ci, b, a }); else t.AddRange(new[] { ci, a, b });
            }
        }
    }

    private static GameObject Nb4Prop(string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    private static GameObject Nb4Skateboard(Transform parent)
    {
        var deck = Nb4MeshAsset("Nagisa_NB4_SkateDeck", (v, uv, t) =>
        {
            Nb4RBox(v, uv, t, new Vector3(0, Nb4DeckTopM - 0.008f, 0), new Vector3(0.105f, 0.008f, 0.40f), 0.07f);
        });
        var wheels = Nb4MeshAsset("Nagisa_NB4_SkateWheels", (v, uv, t) =>
        {
            foreach (float z in new[] { -0.28f, 0.28f })
            {
                Nb4RBox(v, uv, t, new Vector3(0, 0.075f, z), new Vector3(0.09f, 0.012f, 0.02f), 0.008f);   // truck
                foreach (float x in new[] { -0.085f, 0.085f }) Nb4Cyl(v, uv, t, new Vector3(x, 0.03f, z), 0.03f, 0.018f);
            }
        });
        var go = new GameObject("Skateboard");
        go.transform.SetParent(parent, false);
        var colours = new[] { new Color(0.95f, 0.35f, 0.25f), new Color(0.15f, 0.65f, 0.75f), new Color(0.98f, 0.82f, 0.25f), new Color(0.2f, 0.2f, 0.22f) };
        var d = Nb4Prop("Deck", deck, Cel($"NB4_SkateDeck{_nb4N % 4}", colours[_nb4N % 4], 0.35f, 0.2f, 0.15f));
        d.transform.SetParent(go.transform, false);
        var w = Nb4Prop("Wheels", wheels, Cel("NB4_SkateWheels", new Color(0.92f, 0.9f, 0.84f), 0.4f, 0.3f, 0.1f));
        w.transform.SetParent(go.transform, false);
        return go;
    }

    private static GameObject Nb4Bag(int k)
    {
        var mesh = Nb4MeshAsset("Nagisa_NB4_ShopBag", (v, uv, t) =>
        {
            Nb4RBox(v, uv, t, new Vector3(0, -0.13f, 0), new Vector3(0.045f, 0.11f, 0.13f), 0.012f);
            // rope handle loop
            for (int s = 0; s < 6; s++)
            {
                float a0 = Mathf.PI * s / 6f, a1 = Mathf.PI * (s + 1) / 6f;
                var p0 = new Vector3(0, -0.02f + Mathf.Sin(a0) * 0.05f, Mathf.Cos(a0) * 0.06f);
                var p1 = new Vector3(0, -0.02f + Mathf.Sin(a1) * 0.05f, Mathf.Cos(a1) * 0.06f);
                Nb4RBox(v, uv, t, (p0 + p1) * 0.5f, new Vector3(0.006f, 0.006f, 0.006f) + new Vector3(0, Mathf.Abs(p1.y - p0.y) * 0.5f, Mathf.Abs(p1.z - p0.z) * 0.5f), 0.003f);
            }
        });
        var cols = new[] { new Color(0.93f, 0.87f, 0.74f), new Color(0.9f, 0.3f, 0.45f), new Color(0.2f, 0.62f, 0.72f), new Color(0.98f, 0.97f, 0.94f), new Color(0.35f, 0.3f, 0.26f) };
        return Nb4Prop("Shopping Bag", mesh, Cel($"NB4_Bag{k % cols.Length}", cols[k % cols.Length], 0.15f, 0.1f, 0.12f));
    }

    private static GameObject Nb4Guitar()
    {
        var body = Nb4MeshAsset("Nagisa_NB4_Ukulele", (v, uv, t) =>
        {
            Nb4RBox(v, uv, t, new Vector3(0, 0, -0.07f), new Vector3(0.12f, 0.035f, 0.11f), 0.06f);   // lower bout
            Nb4RBox(v, uv, t, new Vector3(0, 0, 0.07f), new Vector3(0.095f, 0.035f, 0.08f), 0.045f);  // upper bout
            Nb4RBox(v, uv, t, new Vector3(0, 0.01f, 0.32f), new Vector3(0.022f, 0.012f, 0.18f), 0.006f); // neck
            Nb4RBox(v, uv, t, new Vector3(0, 0.012f, 0.53f), new Vector3(0.035f, 0.012f, 0.04f), 0.01f); // head
        });
        return Nb4Prop("Ukulele", body, Cel("NB4_Ukulele", new Color(0.62f, 0.38f, 0.2f), 0.45f, 0.35f, 0.12f));
    }

    // ================================================================= the stage

    [NagisaStage(90, "Crowd")]
    private static void BuildNb4Crowd(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();   // standalone MR_NB_STAGES run
        var root = group.parent != null && group.parent.parent != null ? group.parent.parent : group.root;
        var cast = NbCrowdCast(root);
        if (cast.walk.Count == 0 || cast.stand.Count == 0)
        {
            Debug.LogError("[nb4] no Minato Crowd_ donors in the scene - nothing staged.");
            return;
        }
        _nb4N = 0; _nb4Roles.Clear(); _nb4Mesh.Clear();
        Physics.SyncTransforms();
        var occ = Nb4SeedOcc(root, group);
        var rng = new System.Random(4404);

        var runT = Nb4Child(group, "Runners and skaters");
        var promT = Nb4Child(group, "Promenade strollers");
        var beachT = Nb4Child(group, "Beach-goers and groups");
        var queueT = Nb4Child(group, "Queues and buskers");

        int lanes = Nb4Runners(cast, runT, occ, rng, out int runners, out int skaters);
        Nb4Vignettes(cast, promT, beachT, occ, rng);
        int queues = Nb4Queues(cast, queueT, root, group, occ, rng);
        int buskers = Nb4Buskers(cast, queueT, occ, rng);

        // runtime budget: shadows off >120 m, low-poly impostor level >200 m, asleep >800 m (NagisaCrowdBudget.cs)
        if (group.GetComponent<NagisaCrowdBudget>() == null) group.gameObject.AddComponent<NagisaCrowdBudget>();

        var roles = new List<string>();
        foreach (var kv in _nb4Roles) roles.Add($"{kv.Value} {kv.Key}");
        int total = _nb4Existing + _nb4N;
        int allRunners = _nb4ExistingRunners + runners;
        Debug.Log($"[nb4] crowd: +{_nb4N} people on foot (sea side) = {string.Join(", ", roles)}; " +
                  $"{lanes} promenade loops, {runners} runners (run gait, both ways), {skaters} skaters, {queues} queues, {buskers} buskers.");
        Debug.Log($"[nb4] totals: existing {_nb4Existing} (incl. {_nb4ExistingNb13} NB13 ResortInfill, {_nb4ExistingRunners} existing joggers/runners) " +
                  $"+ NB4 {_nb4N} = {total} people; runners {allRunners}. Target >=900 / >=80: " +
                  (total >= 900 && runners >= 80 ? "MET" : "NOT MET"));
    }

    private static Transform Nb4Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
    }

    // ---------------------------------------------------------------- runners + skaters on promenade loops
    private static int Nb4Runners(NbCast cast, Transform parent, Nb4Occ occ, System.Random rng, out int runners, out int skaters)
    {
        runners = 0; skaters = 0;
        var lanesGo = new GameObject("NB4 Promenade Loops");
        lanesGo.transform.SetParent(parent, false);
        var comp = lanesGo.AddComponent<MapleCityWalkLanes>();
        var built = new List<MapleCityWalkLanes.Lane>();
        int fRoute = 0, fDry = 0, fProp = 0, fStep = 0, nOk = 0;
        for (int pass = 0; pass < 2 && built.Count == 0; pass++)
        {
            bool useOcc = pass == 0;   // pass 1 (fallback): ignore props, only dry/height/road checks
            foreach (var span in Nb4Spans)
            {
                var outP = new List<Vector3>(); var backP = new List<Vector3>();
                for (float d = span.x + 10f; d <= span.y - 10f; d += Nb4LaneStepM)
                {
                    int i = _route.IndexAt(d);
                    bool ok = !_route.OnBridge(i);
                    if (!ok) fRoute++;
                    Vector3 a = default, b = default;
                    if (ok)
                    {
                        a = Nb4At(i, Nb4LaneOutM); b = Nb4At(i, Nb4LaneBackM);
                        if (!(Nb4Dry(a) && Nb4Dry(b))) { ok = false; fDry++; }
                        else if (Mathf.Abs(a.y - b.y) >= 1.2f) { ok = false; fStep++; }
                        else if (useOcc && !(occ.Free(a, 0f, 0.3f) && occ.Free(b, 0f, 0.3f))) { ok = false; fProp++; }
                        else if (outP.Count > 0 && (Mathf.Abs(a.y - outP[outP.Count - 1].y) >= 0.9f || Mathf.Abs(b.y - backP[backP.Count - 1].y) >= 0.9f)) { ok = false; fStep++; }
                    }
                    if (ok) { outP.Add(a); backP.Add(b); nOk++; }
                    if ((!ok || d + Nb4LaneStepM > span.y - 10f) && outP.Count > 0)
                    {
                        if (outP.Count * Nb4LaneStepM >= 60f) built.Add(Nb4Loop(outP, backP));
                        outP = new List<Vector3>(); backP = new List<Vector3>();
                    }
                }
            }
            Debug.Log($"[nb4] runner loops pass {pass} (props {(useOcc ? "checked" : "ignored")}): {built.Count} loops; stations ok {nOk}, bridge {fRoute}, wet/road {fDry}, height {fStep}, prop-blocked {fProp}.");
        }
        comp.lanes = built.ToArray();
        if (built.Count == 0) { Debug.LogWarning("[nb4] no promenade loop long enough for runners."); return 0; }

        var cycles = new Dictionary<MinatoCrowdActor, TextAsset>();
        foreach (var w in cast.walk)
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Nb4AnimDir}/MapleLife_RunCycle_{w.name.Substring(6)}.json");
            if (ta != null) cycles[w] = ta;
        }
        var runPool = new List<MinatoCrowdActor>(cycles.Keys);
        runPool.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        float total = 0f; foreach (var l in built) total += l.length;

        // runners
        if (runPool.Count > 0)
        {
            float spacing = total / Nb4RunnerTarget;
            for (int l = 0; l < built.Count; l++)
            {
                float s = (float)rng.NextDouble() * spacing;
                while (s < built[l].length)
                {
                    float seed = (float)rng.NextDouble();
                    float speed = Mathf.Lerp(Nb4RunSpeedMin, Nb4RunSpeedMax, (float)rng.NextDouble());
                    int heading = rng.NextDouble() < 0.5 ? 1 : -1;
                    var pos = comp.Sample(l, s, out var dir);
                    if (heading < 0) dir = -dir;
                    var go = Nb4Clone(runPool, parent, "Runner", Nb4RunLook, "Runner_", pos, dir, MK.Idle, seed, null);
                    if (go != null)
                    {
                        var src = runPool[0];
                        foreach (var a in runPool) if (go.GetComponent<MapleCityLook>() != null && go.GetComponent<MapleCityLook>().donor == a.name.Substring(6)) src = a;
                        var w = go.AddComponent<MapleCityWalker>();
                        w.lanes = comp; w.lane = l; w.startS = s; w.heading = heading; w.speed = speed;
                        w.lateral = 0f; w.lift = 0f; w.Place(0f);
                        var run = go.AddComponent<MapleCityRunner>();
                        run.cycle = cycles[src];
                        var high = go.transform.Find("LOD0 High Skinned");
                        run.rigRoot = high != null ? high.Find("Rigged Character") : null;
                        run.walker = w;
                        run.phase = (float)rng.NextDouble();
                        run.baseLateral = 0f;
                        float natural = 2.82f;
                        try { natural = Mathf.Max(0.5f, JsonUtility.FromJson<Nb4RunHeader>(run.cycle.text).naturalSpeed); }
                        catch (System.Exception) { }
                        run.playback = Mathf.Clamp(speed / (natural * go.transform.localScale.x), 0.8f, 1.4f);
                        runners++;
                    }
                    s += spacing * Mathf.Lerp(0.5f, 1.5f, (float)rng.NextDouble());
                }
            }
        }
        else Debug.LogWarning("[nb4] no MapleLife_RunCycle_*.json for the walk donors - no runners.");

        // skaters: standing donors gliding on a deck
        {
            float spacing = total / Nb4SkaterTarget;
            for (int l = 0; l < built.Count; l++)
            {
                float s = (float)rng.NextDouble() * spacing;
                while (s < built[l].length)
                {
                    float seed = (float)rng.NextDouble();
                    int heading = rng.NextDouble() < 0.5 ? 1 : -1;
                    var pos = comp.Sample(l, s, out var dir);
                    if (heading < 0) dir = -dir;
                    var go = Nb4Clone(cast.stand, parent, "Skater", Nb4Look, "Chat_", pos, dir, MK.Idle, seed, null);
                    if (go != null)
                    {
                        var board = Nb4Skateboard(go.transform);
                        board.transform.localPosition = Vector3.zero; board.transform.localRotation = Quaternion.identity;
                        float ls = go.transform.lossyScale.x;
                        if (ls > 1e-3f) board.transform.localScale = Vector3.one / ls;
                        Nb4AddToLod0(go, board);
                        var w = go.AddComponent<MapleCityWalker>();
                        w.lanes = comp; w.lane = l; w.startS = s; w.heading = heading;
                        w.speed = Mathf.Lerp(Nb4SkateSpeedMin, Nb4SkateSpeedMax, (float)rng.NextDouble());
                        w.lateral = (float)rng.NextDouble() * 0.3f - 0.15f; w.lift = Nb4DeckTopM; w.Place(0f);
                        skaters++;
                    }
                    s += spacing * Mathf.Lerp(0.5f, 1.5f, (float)rng.NextDouble());
                }
            }
        }
        return built.Count;
    }

    /// <summary>A loop vertex at the given offset, nudged up to 0.9 m across the band around lamps / palms / benches.</summary>
    private static Vector3 Nb4LanePoint(int i, float off, Nb4Occ occ)
    {
        foreach (float d in new[] { 0f, 0.45f, -0.45f, 0.9f, -0.9f })
        {
            var p = Nb4At(i, off + d);
            if (occ.Free(p, 0f, 0.45f)) return p;
        }
        return Nb4At(i, off);
    }

    [System.Serializable] private sealed class Nb4RunHeader { public float naturalSpeed = 2.82f; }

    private static MapleCityWalkLanes.Lane Nb4Loop(List<Vector3> outP, List<Vector3> backP)
    {
        var pts = new List<Vector3>(outP);
        for (int k = backP.Count - 1; k >= 0; k--) pts.Add(backP[k]);
        var lane = new MapleCityWalkLanes.Lane { points = pts.ToArray() };
        int n = pts.Count;
        lane.dist = new float[n + 1];
        for (int k = 1; k <= n; k++)
        {
            var a = pts[k - 1]; var b = pts[k % n];
            lane.dist[k] = lane.dist[k - 1] + new Vector2(b.x - a.x, b.z - a.z).magnitude;
        }
        lane.length = lane.dist[n];
        lane.latMin = new float[n]; lane.latMax = new float[n];
        for (int k = 0; k < n; k++) { lane.latMin[k] = -Nb4LaneLatM; lane.latMax[k] = Nb4LaneLatM; }
        return lane;
    }

    // ---------------------------------------------------------------- promenade + beach vignettes
    private static void Nb4Vignettes(NbCast cast, Transform promT, Transform beachT, Nb4Occ occ, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        foreach (var span in Nb4Spans)
        {
            for (float d = span.x + 20f; d < span.y - 30f; d += Nb4SlotStepM * Mathf.Lerp(0.7f, 1.3f, R()))
            {
                int i = _route.IndexAt(d);
                if (_route.OnBridge(i)) continue;
                var sd = _route.SideFlat(i) * SeaSign(i);
                var tan = _route.Tangent[i]; tan.y = 0f; tan.Normalize();

                // --- a promenade walker group in the middle of the band (between the runner loops)
                {
                    float leg = Mathf.Lerp(14f, 26f, R());
                    int j = _route.IndexAt(Mathf.Min(d + leg, span.y - 10f));
                    if (j > i && !_route.OnBridge(j))
                    {
                        float off = Mathf.Lerp(9.0f, 10.8f, R());
                        var a = Nb4At(i, off); var b = Nb4At(j, off);
                        bool fwd = R() < 0.5f;
                        var s0 = fwd ? a : b; var s1 = fwd ? b : a;
                        if (Nb4Dry(s0) && Nb4Dry(s1) && occ.Free(s0, Nb4PersonGapM, Nb4PropGapM) && occ.FreePath(s0, s1, 0.5f))
                        {
                            var lat = Vector3.Cross(Vector3.up, (s1 - s0).normalized);
                            float roll = R(), seed = R();
                            if (roll < 0.30f)
                            {
                                var p = Nb4Walker(cast, promT, "Shopper", s0, s1, seed, occ);
                                if (p != null)
                                {
                                    Nb4Carry(p, "RightHand", Nb4Bag(rng.Next(5)), Vector3.down * 0.03f + lat * 0.03f);
                                    if (R() < 0.5f) Nb4Carry(p, "LeftHand", Nb4Bag(rng.Next(5)), Vector3.down * 0.03f - lat * 0.03f);
                                }
                            }
                            else if (roll < 0.55f)
                            {
                                Nb4Walker(cast, promT, "Couple", s0 - lat * 0.36f, s1 - lat * 0.36f, seed, occ);
                                Nb4Walker(cast, promT, "Couple", s0 + lat * 0.36f, s1 + lat * 0.36f, seed, occ);
                            }
                            else if (roll < 0.72f)
                            {
                                var p = Nb4Walker(cast, promT, "Family", s0 - lat * 0.4f, s1 - lat * 0.4f, seed, occ);
                                if (p != null && R() < 0.6f)
                                {
                                    Attach(p, "Nagisa_S_Stroller", new Vector3(0f, 0f, 0.95f), 180f, NbCarColour(rng.Next(8)));
                                    var st = p.transform.Find("Nagisa_S_Stroller") ?? p.transform.GetChild(p.transform.childCount - 1);
                                    if (st != null) Nb4AddToLod0(p, st.gameObject);
                                }
                                Nb4Walker(cast, promT, "FamilyKid", s0 + lat * 0.45f, s1 + lat * 0.45f, seed, occ, 0f, 0.64f);
                                if (R() < 0.4f) Nb4Walker(cast, promT, "FamilyKid", s0 + lat * 0.45f - (s1 - s0).normalized * 0.9f,
                                                          s1 + lat * 0.45f - (s1 - s0).normalized * 0.9f, seed, occ, 0f, 0.6f);
                            }
                            else if (roll < 0.84f)
                            {
                                var p = Nb4Walker(cast, promT, "DogWalker", s0, s1, seed, occ);
                                Attach(p, "Nagisa_S_Dog", new Vector3(0.28f, 0f, 1.25f), 180f);
                            }
                            else Nb4Walker(cast, promT, "Tourist", s0, s1, seed, occ);
                        }
                    }
                }

                // --- the sand side: beach-goers heading down with boards / towels, chatting groups, benches
                {
                    var edge = Nb4At(i, 13.0f);
                    if (!Nb4Dry(edge)) continue;
                    float roll = R();
                    if (roll < 0.40f)
                    {
                        // walk from the promenade edge down to the dry sand (and back)
                        Vector3 sand = default; bool found = false;
                        for (float o = 22f; o < 34f; o += 2f)
                        {
                            var q = Nb4At(i, o);
                            float c = _ground.Coast(q.x, q.z);
                            if (c < 9f) break;
                            if (c > 14f) continue;
                            sand = q; found = true; break;
                        }
                        if (!found) { var q = Nb4At(i, 20f); if (Nb4Dry(q, 9f)) { sand = q; found = true; } }
                        if (found && occ.Free(edge, Nb4PersonGapM, Nb4PropGapM) && occ.FreePath(edge, sand, 0.7f))
                        {
                            bool down = R() < 0.5f;
                            var p = Nb4Walker(cast, beachT, "BeachGoer", down ? edge : sand, down ? sand : edge, R(), occ);
                            if (p != null && R() < 0.45f) Attach(p, "Nagisa_S_Surfboard", new Vector3(0.34f, 0.12f, 0.05f), 0f);
                        }
                    }
                    else if (roll < 0.78f)
                    {
                        // chatting group of 3-4 on the sand-side edge
                        var c = Nb4At(i, Mathf.Lerp(13.5f, 16f, R())) + tan * Mathf.Lerp(-4f, 4f, R());
                        c.y = Nb4Surface(c);
                        int n = 3 + (R() < 0.4f ? 1 : 0);
                        if (!Nb4Dry(c) || !occ.Free(c, 1.4f, 1.6f)) continue;
                        float a0 = R() * Mathf.PI * 2f;
                        for (int k = 0; k < n; k++)
                        {
                            float a = a0 + k * Mathf.PI * 2f / n;
                            var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.62f;
                            p.y = Nb4Surface(p);
                            Nb4Stander(cast, beachT, "ChatGroup", p, c - p, R(), null, k == 0 && R() < 0.5f);
                        }
                        occ.Add(true, c, 0.9f);
                    }
                    else if (cast.sit.Count > 0)
                    {
                        // a bench sitter looking out to sea (the sit donor brings its own timber bench)
                        var p = Nb4At(i, 13.2f) + tan * Mathf.Lerp(-5f, 5f, R());
                        p.y = Nb4Surface(p);
                        if (!Nb4Dry(p) || !occ.Free(p, 1.2f, 1.2f)) continue;
                        var go = Nb4Clone(cast.sit, beachT, "BenchSitter", Nb4Look, "Customer_", p, sd, MK.Sit, R(), occ);
                        if (go != null && R() < 0.5f)
                        {
                            var q = p + tan * 1.1f + sd * 0.25f; q.y = Nb4Surface(q);
                            Nb4Stander(cast, beachT, "BenchChat", q, p - q, R(), occ);
                        }
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- kiosk / truck queues
    private static int Nb4Queues(NbCast cast, Transform parent, Transform root, Transform mine, Nb4Occ occ, System.Random rng)
    {
        int queues = 0;
        var seen = new List<Vector3>();
        foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
        {
            if (g.transform.IsChildOf(mine) || g.GetComponentInParent<MinatoCrowdActor>() != null) continue;
            string n = g.name;
            if (!(n.Contains("ShaveIce") || n.Contains("FoodTruck") || n.Contains("RentalStand") || n.Contains("Kiosk") || n.Contains("IceCream")))
                continue;
            var c = g.transform.position;
            bool dup = false; foreach (var s in seen) if ((s - c).sqrMagnitude < 16f) dup = true;
            if (dup) continue;
            seen.Add(c);
            float dist = _route.PlanDistance(c.x, c.z, out int ni);
            if (!Nb4InSpans(_route.Distance[ni])) continue;
            var sd = _route.SideFlat(ni) * SeaSign(ni);
            if (Vector3.Dot(c - _route.Position[ni], sd) < CorridorKeepOutM) continue;      // sea side only
            var tan = _route.Tangent[ni]; tan.y = 0f; tan.Normalize();
            float r = Mathf.Clamp(g.size * 0.5f * g.transform.lossyScale.x, 0.8f, 4f);
            // customers face the counter from the promenade side, the line runs along the shore
            var toProm = -sd;
            var head = c + toProm * (r * 0.75f + 0.9f);
            float dir = rng.NextDouble() < 0.5 ? 1f : -1f;
            int want = rng.Next(Nb4QueueMin, Nb4QueueMax + 1), got = 0;
            var prev = c;
            for (int k = 0; k < want; k++)
            {
                var p = head + tan * dir * (0.85f * k) + toProm * (0.12f * (k % 2));
                p.y = Nb4Surface(p);
                if (!Nb4Dry(p) || !occ.Free(p, 0.55f, 0.3f)) break;
                var face = k == 0 ? c - p : prev - p;
                if (Nb4Stander(cast, parent, "QueueGuest", p, face, (float)rng.NextDouble(), occ) != null) got++;
                prev = p;
            }
            if (got > 0) queues++;
        }
        return queues;
    }

    // ---------------------------------------------------------------- street musicians
    private static int Nb4Buskers(NbCast cast, Transform parent, Nb4Occ occ, System.Random rng)
    {
        int sites = 0;
        var picks = new List<float>();
        foreach (var span in Nb4Spans)
            for (float d = span.x + 300f; d < span.y - 200f; d += 173f) picks.Add(d);
        for (int k = picks.Count - 1; k > 0; k--) { int j = rng.Next(k + 1); (picks[k], picks[j]) = (picks[j], picks[k]); }
        foreach (float d in picks)
        {
            if (sites >= Nb4MusicianSites) break;
            int i = _route.IndexAt(d);
            if (_route.OnBridge(i)) continue;
            var sd = _route.SideFlat(i) * SeaSign(i);
            var m = Nb4At(i, 16.5f);
            if (!Nb4Dry(m, 8f) || !occ.Free(m, 3.2f, 3.2f)) continue;
            var busker = Nb4Stander(cast, parent, "Busker", m, -sd, (float)rng.NextDouble(), occ, true);
            if (busker == null) continue;
            var uke = Nb4Guitar();
            // across the chest, neck up to the left
            Transform spine = null;
            foreach (var t in busker.GetComponentsInChildren<Transform>(true)) if (t.name == "Spine02") { spine = t; break; }
            var at = spine != null ? spine : busker.transform;
            var fwd = busker.transform.forward; var right = busker.transform.right;
            uke.transform.position = at.position + fwd * 0.14f + (spine == null ? Vector3.up * 0.7f : Vector3.zero);
            uke.transform.rotation = Quaternion.LookRotation((-right + Vector3.up * 0.55f).normalized, -fwd);
            uke.transform.SetParent(at, true);
            Nb4AddToLod0(busker, uke);
            int aud = 5 + rng.Next(3);
            for (int k = 0; k < aud; k++)
            {
                float a = Mathf.Lerp(-70f, 70f, aud == 1 ? 0.5f : k / (float)(aud - 1)) * Mathf.Deg2Rad;
                var dir = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * (-sd);
                var p = m + dir * Mathf.Lerp(2.2f, 2.8f, (float)rng.NextDouble()); p.y = Nb4Surface(p);
                if (!Nb4Dry(p) || !occ.Free(p, 0.5f, 0.5f)) continue;
                Nb4Stander(cast, parent, "Audience", p, m - p, (float)rng.NextDouble(), occ, rng.NextDouble() < 0.25);
            }
            occ.Add(true, m, 3f);
            sites++;
        }
        return sites;
    }

    // ================================================================= verification

    /// <summary>Headless NB4 check: counts, sea side, road keep-out, runner gait + motion. Logs PASS/FAIL.</summary>
    public static void Nb4SelfTest()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnsureRouteGround();
        var roots = FindRootsByExactName(RootName);
        if (roots.Count == 0) { Debug.LogError("[nb4] SELFTEST FAIL: no Nagisa root"); return; }
        var root = roots[0].transform;
        Transform grp = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == Nb4GroupName) { grp = t; break; }
        if (grp == null) { Debug.LogError("[nb4] SELFTEST FAIL: no 'NB Crowd' group"); return; }
        int all = root.GetComponentsInChildren<MinatoCrowdActor>(true).Length;
        var mine = grp.GetComponentsInChildren<MinatoCrowdActor>(true);
        int bad = 0, land = 0, helmets = 0, runnersOk = 0, runners = 0, moved = 0, dupNames = 0;
        var names = new HashSet<string>();
        foreach (var a in mine)
        {
            if (!names.Add(a.name)) dupNames++;
            var p = a.transform.position;
            float dist = _route.PlanDistance(p.x, p.z, out int ni);
            var sd = _route.SideFlat(ni) * SeaSign(ni);
            if (dist < CorridorKeepOutM + 0.5f) bad++;
            if (Vector3.Dot(p - _route.Position[ni], sd) < 0f) land++;
            foreach (var r in a.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.Contains("Helmet") && r.enabled && r.gameObject.activeInHierarchy) { helmets++; goto nextActor; }
            nextActor:
            var run = a.GetComponent<MapleCityRunner>();
            var w = a.GetComponent<MapleCityWalker>();
            if (run != null)
            {
                runners++;
                run.Pose(0.37f);
                if (run.Ready) runnersOk++;
            }
            if (w != null)
            {
                w.Place(0f); var p0 = a.transform.position;
                w.Place(3f); var p1 = a.transform.position;
                w.Place(0f);
                if ((p1 - p0).magnitude > 5f) moved++;
                if (_route.PlanDistance(p1.x, p1.z, out _) < CorridorKeepOutM + 0.5f) bad++;
            }
        }
        bool pass = bad == 0 && land == 0 && helmets == 0 && all >= 900 && runners >= 80 && runnersOk == runners && dupNames == 0;
        Debug.Log($"[nb4] SELFTEST {(pass ? "PASS" : "FAIL")}: region people {all} (NB4 {mine.Length}), runners {runners} " +
                  $"(gait ready {runnersOk}), lane movers moved {moved}, in-corridor {bad}, land-side {land}, helmets {helmets}, dup names {dupNames}.");
    }

    /// <summary>Three verification frames (runner gait posed at t = 4 s): reference/good_graphics/nagisa_bay/overhaul/nb4_*.png</summary>
    public static void Nb4Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnsureRouteGround();
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        const float T = 4f;
        foreach (var w in Object.FindObjectsByType<MapleCityWalker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (w.name.StartsWith("Nagisa NB4")) w.Place(T);
        foreach (var r in Object.FindObjectsByType<MapleCityRunner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (r.name.StartsWith("Nagisa NB4")) r.Pose(T);
        foreach (var a in Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (a.name.StartsWith("Nagisa") && a.GetComponent<MapleCityRunner>() == null && a.GetComponent<MapleCityWalker>() == null)
                a.SampleForDiagnostics(T);
        try
        {
            foreach (var (d, tag) in new[] { (1900f, "prom_beach"), (13300f, "prom_east") })
            {
                int i = _route.IndexAt(d);
                var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
                var sd = _route.SideFlat(i) * SeaSign(i);
                var eye = _route.Position[i] + sd * 6.2f + Vector3.up * 1.7f;
                var tgt = _route.Position[i] + sd * 11f + t * 28f + Vector3.up * 0.6f;
                NagisaBayDiagnostics.Shot(eye, tgt, 60f, $"overhaul/nb4_{tag}.png");
            }
            {
                int i = _route.IndexAt(2600f);
                var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
                var sd = _route.SideFlat(i) * SeaSign(i);
                var eye = _route.Position[i] - sd * 6f - t * 30f + Vector3.up * 22f;
                var tgt = _route.Position[i] + sd * 16f + t * 25f;
                NagisaBayDiagnostics.Shot(eye, tgt, 55f, "overhaul/nb4_wide.png");
            }
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previous))
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
        Debug.Log("[nb4] captured 3 frames to reference/good_graphics/nagisa_bay/overhaul/nb4_*.png");
    }
}



// N1 Sakura Pass NPCs (claude, 2026-09-30): PEOPLE ON FOOT for the pass and the Kawabe lake village.
//
// Donors (N rules 2/3/8): clones of the Minato "Crowd_*" donors already saved in the scene,
// dressed with MapleCityLife.DressForRegion using the NEW Sakura look sets from
// tools/blender/looks/sakura_looks.py:
//   look prefix "SK"  yukata (pale blue / sakura pink / dark), kimono with obi, spring windbreakers
//                     and pastel cardigans, geta / zori / trainers
//   look prefix "SM"  shrine staff (white top + vermilion or indigo hakama)
// Never MinatoCrowdPopulation.Prepare. Nobody helmeted (the looks cap the head with hair).
//
// What it stages, all under ONE root "Sakura Townsfolk" (converges by exact name: re-running
// destroys and rebuilds only this root, never another package's objects):
//   * shrine visitors, two shrine staff, photographers and short stroll legs at the great torii
//     and its lantern avenue; visitors at the descent overlook's footpath torii;
//   * visitors and a photographer at the lakeside overlook and the descent overlook (inland side);
//   * photographers and bench pairs under the sakura trees beside the road;
//   * villagers (chats, benches, strollers) around the Kawabe lake village;
//   * strolling pairs along the verges of the pass and the lakeshore.
// Photographers hold a small modelled camera to the eye (a head-bone child, culled with LOD0).
//
// Placement: every spot is on terrain (a temporary mesh-collider host, torn down afterwards),
// slope-limited, above the lake, >= RoadClearM from EVERY road centreline (the ~6 m ride corridor
// plus margin), outside every prop bound, clear of every tree trunk and of every other person.
// Walkers pace short straight legs whose ends and midpoint are all re-tested.
//
// Perf: the donors' own LODGroup (LOD0 skinned -> LOD1 Low 3D -> culled), LOD1 shadowless;
// MinatoCrowdActor animates only within its own gate (<= 115 m, well inside the 800 m brief).
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MK = MinatoCrowdActor.MotionKind;

public static class SakuraPassTownsfolk
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    public const string RootName = "Sakura Townsfolk";
    public const string LookPrefix = "SK";
    public const string StaffPrefix = "SM";
    const string MatDir = "Assets/Environment/SakuraPass/Townsfolk/Materials";

    // ---------------------------------------------------------------- PROVISIONAL tuning
    const float RoadClearM = 7.5f;      // metres from any road centreline (half width 3.5 + shoulder + 3.4)
    const float PersonGapM = 0.9f;
    const float MaxSlope = 0.42f;       // rise/run (~23 deg)
    const float TrunkM = 1.15f, SaplingM = 0.5f;
    const int Seed = 90031;

    sealed class Cast
    {
        public readonly List<MinatoCrowdActor> walk = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> idle = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> stand = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> sit = new List<MinatoCrowdActor>();
    }

    sealed class Road { public string id; public Vector3[] p, tan, side; public float[] d; public float len; }

    // ---- scene state for one Stage() run
    static Cast _cast;
    static readonly List<Road> _roads = new List<Road>();
    static GameObject _groundHost;
    static float _waterY;
    static readonly RaycastHit[] _hits = new RaycastHit[32];
    static readonly List<Vector4> _people = new List<Vector4>();            // x,y,z,radius
    static readonly Dictionary<long, List<int>> _roadGrid = new Dictionary<long, List<int>>();   // 50 m cells -> road index*100000+pt
    static readonly List<Vector3> _roadPts = new List<Vector3>();
    static readonly Dictionary<long, List<Bounds>> _obst = new Dictionary<long, List<Bounds>>();
    static readonly List<Vector3> _trunks = new List<Vector3>();            // x,z,radius in (x,y=radius,z)
    static readonly List<Transform> _sakuraTrees = new List<Transform>();
    static System.Random _rng;
    static int _n;
    static readonly Dictionary<string, int> _roles = new Dictionary<string, int>();
    static readonly Dictionary<string, int> _lookTally = new Dictionary<string, int>();
    static long _tris0, _tris1;
    static int _cameras;

    static float R() => (float)_rng.NextDouble();
    static float R(float a, float b) => Mathf.Lerp(a, b, R());
    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    // =================================================================== entry
    [MenuItem("MapleRide/NPCs/Stage Sakura Townsfolk")]
    public static void Stage()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // exact-name converge: only OUR root goes
        var old = GameObject.Find(RootName);
        while (old != null) { Object.DestroyImmediate(old); old = GameObject.Find(RootName); }

        _rng = new System.Random(Seed);
        _n = 0; _people.Clear(); _roles.Clear(); _lookTally.Clear(); _tris0 = _tris1 = 0; _cameras = 0;
        _roadPts.Clear(); _roadGrid.Clear(); _obst.Clear(); _trunks.Clear(); _sakuraTrees.Clear(); _roads.Clear();
        if (!AssetDatabase.IsValidFolder("Assets/Environment/SakuraPass/Townsfolk"))
            AssetDatabase.CreateFolder("Assets/Environment/SakuraPass", "Townsfolk");
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/Environment/SakuraPass/Townsfolk", "Materials");

        if (!LoadRoads()) return;
        _cast = BuildCast();
        if (_cast.walk.Count == 0 || _cast.idle.Count == 0)
        { Debug.LogError("[sakura-folk] no Minato Crowd_ donors in the scene - nothing staged."); return; }

        _groundHost = MakeGroundHost();
        if (_groundHost == null) { Debug.LogError("[sakura-folk] no 'Valley Terrain' - nothing staged."); return; }
        try
        {
            var lake = GameObject.Find("Valley Lake");
            var lr = lake != null ? lake.GetComponentsInChildren<Renderer>(true) : null;
            _waterY = lr != null && lr.Length > 0 ? lr.Max(r => r.bounds.max.y) : -44f;
            IndexObstacles();

            var root = new GameObject(RootName).transform;
            var torii = Group(root, "Shrine Torii");
            var overlook = Group(root, "Overlooks");
            var trees = Group(root, "Sakura Trees");
            var village = Group(root, "Lake Village");
            var verge = Group(root, "Road Strollers");

            int a = _n; BuildToriiShrine(torii); int nTorii = _n - a;
            a = _n; BuildOverlooks(overlook); int nOver = _n - a;
            a = _n; BuildUnderTrees(trees); int nTrees = _n - a;
            a = _n; BuildVillage(village); int nVillage = _n - a;
            a = _n; BuildStrollers(verge); int nStroll = _n - a;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log($"[sakura-folk] STAGED {_n} people: torii {nTorii}, overlooks {nOver}, sakura trees {nTrees}, " +
                      $"lake village {nVillage}, road strollers {nStroll}; photographers with cameras {_cameras}.");
            Debug.Log("[sakura-folk] roles: " + string.Join(", ", _roles.Select(k => $"{k.Key} {k.Value}")));
            Debug.Log($"[sakura-folk] look prefixes: {LookPrefix} (yukata / kimono / spring jackets), {StaffPrefix} (shrine staff); " +
                      "looks drawn: " + string.Join(", ", _lookTally.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
            Debug.Log($"[sakura-folk] TRIS if every person were drawn: LOD0 {_tris0 / 1000f:F0}k ({_tris0 / Mathf.Max(1, _n):F0} avg), " +
                      $"LOD1 {_tris1 / 1000f:F0}k. Real cost is the nearest few at LOD0 (budget +1.5M).");
        }
        finally
        {
            if (_groundHost != null) Object.DestroyImmediate(_groundHost);
            _groundHost = null;
        }
    }

    static Transform Group(Transform root, string name)
    {
        var g = new GameObject(name).transform;
        g.SetParent(root, false);
        return g;
    }

    // =================================================================== roads / ground / obstacles
    static bool LoadRoads()
    {
        var graph = RouteGraph.Load();
        if (graph == null) { Debug.LogError("[sakura-folk] no route graph."); return false; }
        foreach (var s in graph.segments)
        {
            if (s.Count < 2) continue;
            _roads.Add(new Road { id = s.id, p = s.position, tan = s.tangent, side = s.side, d = s.distance, len = s.Length });
            int ri = _roads.Count - 1;
            for (int i = 0; i < s.Count; i++)
            {
                _roadPts.Add(s.position[i]);
                long k = Key(Mathf.FloorToInt(s.position[i].x / 50f), Mathf.FloorToInt(s.position[i].z / 50f));
                if (!_roadGrid.TryGetValue(k, out var l)) _roadGrid[k] = l = new List<int>();
                l.Add(_roadPts.Count - 1);
            }
        }
        return _roads.Count > 0;
    }

    static Road RoadOf(string id) => _roads.FirstOrDefault(r => r.id == id);

    static void FrameAt(Road r, float m, out Vector3 p, out Vector3 tan, out Vector3 side)
    {
        m = Mathf.Clamp(m, 0f, r.len);
        int lo = 0, hi = r.d.Length - 1;
        while (lo + 1 < hi) { int mid = (lo + hi) >> 1; if (r.d[mid] <= m) lo = mid; else hi = mid; }
        int b = Mathf.Min(lo + 1, r.p.Length - 1);
        float span = r.d[b] - r.d[lo];
        float f = span > 1e-4f ? (m - r.d[lo]) / span : 0f;
        p = Vector3.Lerp(r.p[lo], r.p[b], f);
        tan = Vector3.Slerp(r.tan[lo], r.tan[b], f).normalized;
        side = Vector3.Slerp(r.side[lo], r.side[b], f).normalized;
    }

    /// <summary>Horizontal distance to the nearest road sample of ANY segment (3 m spacing + polyline test).</summary>
    static float RoadDist(float x, float z)
    {
        float best = float.MaxValue;
        int cx = Mathf.FloorToInt(x / 50f), cz = Mathf.FloorToInt(z / 50f);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!_roadGrid.TryGetValue(Key(cx + dx, cz + dz), out var l)) continue;
                foreach (int i in l)
                {
                    var q = _roadPts[i];
                    float ddx = q.x - x, ddz = q.z - z;
                    float d = ddx * ddx + ddz * ddz;
                    if (d < best) best = d;
                }
            }
        // samples are ~3 m apart: the true polyline distance can be up to 1.5 m less, so deduct it
        return best == float.MaxValue ? float.MaxValue : Mathf.Max(0f, Mathf.Sqrt(best) - 1.5f);
    }

    static GameObject MakeGroundHost()
    {
        var terrain = GameObject.Find("Valley Terrain");
        if (terrain == null) return null;
        var host = new GameObject("~SakuraTownsfolkGround") { hideFlags = HideFlags.HideAndDontSave };
        foreach (var mf in terrain.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var proxy = new GameObject(mf.name);
            proxy.transform.SetParent(host.transform, false);
            proxy.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
            proxy.transform.localScale = mf.transform.lossyScale;
            proxy.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }
        Physics.SyncTransforms();
        return host;
    }

    static bool Ground(float x, float z, out float y)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(x, 500f, z), Vector3.down, _hits, 1500f);
        float best = float.MaxValue; y = 0f; bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = _hits[i];
            if (!h.collider.transform.IsChildOf(_groundHost.transform)) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; found = true; }
        }
        return found;
    }

    static readonly string[] SoftNames =
    { "tree", "pine", "broadleaf", "sapling", "ground", "cover", "moss", "grass", "petal", "decal", "verge",
      "fern", "shrub", "foliage", "bush", "flower", "leaf", "reed", "road", "shoulder", "wind volume" };

    static void IndexObstacles()
    {
        int kept = 0, scanned = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r is SkinnedMeshRenderer || r is ParticleSystemRenderer || r is LineRenderer) continue;
            var b = r.bounds;
            if (b.size.x > 30f || b.size.z > 30f || b.size.y < 0.25f) continue;
            if (RoadDist(b.center.x, b.center.z) > 60f) continue;
            scanned++;
            bool soft = false;
            for (var t = r.transform; t != null && !soft; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                foreach (var s in SoftNames) if (n.Contains(s)) { soft = true; break; }
                if (t.name == "Sakura Pass Environment") break;
            }
            if (soft) continue;
            b.Expand(new Vector3(0.5f, 0f, 0.5f));
            int x0 = Mathf.FloorToInt(b.min.x / 6f), x1 = Mathf.FloorToInt(b.max.x / 6f);
            int z0 = Mathf.FloorToInt(b.min.z / 6f), z1 = Mathf.FloorToInt(b.max.z / 6f);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!_obst.TryGetValue(Key(x, z), out var l)) _obst[Key(x, z)] = l = new List<Bounds>();
                    l.Add(b);
                }
            kept++;
        }
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = t.name;
            bool tree = n.StartsWith("Sakura Tree ", System.StringComparison.Ordinal) ||
                        n.StartsWith("Pine ", System.StringComparison.Ordinal) ||
                        n.StartsWith("Broadleaf ", System.StringComparison.Ordinal) && !n.Contains("Sapling");
            bool sapling = n.Contains("Sapling");
            if (!tree && !sapling) continue;
            if (RoadDist(t.position.x, t.position.z) > 60f) continue;
            _trunks.Add(new Vector3(t.position.x, sapling ? SaplingM : TrunkM, t.position.z));
            if (n.StartsWith("Sakura Tree ", System.StringComparison.Ordinal)) _sakuraTrees.Add(t);
        }
        Debug.Log($"[sakura-folk] obstacles: {kept} prop bounds (of {scanned} near the roads), {_trunks.Count} trunks, " +
                  $"{_sakuraTrees.Count} sakura trees near the road; water level {_waterY:F1}.");
    }

    static bool PropFree(Vector3 p)
    {
        if (_obst.TryGetValue(Key(Mathf.FloorToInt(p.x / 6f), Mathf.FloorToInt(p.z / 6f)), out var l))
            foreach (var b in l)
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z && p.y < b.max.y + 0.3f && p.y > b.min.y - 2f)
                    return false;
        foreach (var t in _trunks)
        {
            float dx = p.x - t.x, dz = p.z - t.z;
            if (dx * dx + dz * dz < t.y * t.y) return false;
        }
        return true;
    }

    static bool PeopleFree(Vector3 p, float gap)
    {
        foreach (var e in _people)
        {
            float dx = p.x - e.x, dz = p.z - e.z, r = e.w + gap;
            if (dx * dx + dz * dz < r * r) return false;
        }
        return true;
    }

    /// <summary>Terrain point valid for a person: on land, gentle, off every road, off every prop.</summary>
    static bool Valid(ref Vector3 p, float gap = PersonGapM)
    {
        if (!Ground(p.x, p.z, out float y)) return false;
        if (y < _waterY + 0.7f) return false;
        if (RoadDist(p.x, p.z) < RoadClearM) return false;
        // slope: four neighbours at 0.7 m
        float hmax = 0f;
        foreach (var o in new[] { new Vector2(0.7f, 0f), new Vector2(-0.7f, 0f), new Vector2(0f, 0.7f), new Vector2(0f, -0.7f) })
        {
            if (!Ground(p.x + o.x, p.z + o.y, out float yy)) return false;
            hmax = Mathf.Max(hmax, Mathf.Abs(yy - y));
        }
        if (hmax / 0.7f > MaxSlope) return false;
        p.y = y;
        return PropFree(p) && PeopleFree(p, gap);
    }

    static bool FindSpot(Vector3 anchor, float rMin, float rMax, out Vector3 p, int tries = 60)
    {
        for (int i = 0; i < tries; i++)
        {
            float a = R() * Mathf.PI * 2f, d = R(rMin, rMax);
            p = new Vector3(anchor.x + Mathf.Cos(a) * d, 0f, anchor.z + Mathf.Sin(a) * d);
            if (Valid(ref p)) return true;
        }
        p = anchor;
        return false;
    }

    static bool LegValid(Vector3 a, Vector3 b)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / 1.5f));
        for (int k = 0; k <= n; k++)
        {
            var q = Vector3.Lerp(a, b, k / (float)n);
            if (!Ground(q.x, q.z, out float y) || y < _waterY + 0.7f || RoadDist(q.x, q.z) < RoadClearM) return false;
            q.y = y;
            if (!PropFree(q)) return false;
        }
        return true;
    }

    // =================================================================== cast / cloning
    static Cast BuildCast()
    {
        var cast = new Cast();
        foreach (var a in Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a == null || !a.name.StartsWith("Crowd_", System.StringComparison.Ordinal)) continue;
            if (a.transform.Find("LOD0 High Skinned/Rigged Character") == null) continue;
            if (a.motion == MK.Walk) cast.walk.Add(a);
            else if (a.motion == MK.Idle) { cast.idle.Add(a); cast.stand.Add(a); }
            else if (a.motion == MK.Wave) cast.stand.Add(a);
            else if (a.motion == MK.Sit && a.transform.Find("Timber Waterfront Bench") != null) cast.sit.Add(a);
        }
        if (cast.idle.Count == 0) cast.idle.AddRange(cast.stand);
        if (cast.walk.Count == 0) cast.walk.AddRange(cast.idle);
        foreach (var l in new[] { cast.walk, cast.idle, cast.stand, cast.sit }) l.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        Debug.Log($"[sakura-folk] crowd donors: {cast.walk.Count} walk, {cast.idle.Count} idle, {cast.stand.Count} stand+wave, {cast.sit.Count} sit.");
        return cast;
    }

    static GameObject Person(Transform parent, string role, Vector3 pos, Vector3 face, MK kind, float seed,
                             string prefix = LookPrefix, Vector3? end = null, float speed = 0f)
    {
        var pool = kind == MK.Sit ? _cast.sit : kind == MK.Walk ? _cast.walk : _cast.idle;
        if (pool.Count == 0) pool = _cast.idle;
        var src = pool[Mathf.Abs((int)(seed * 9973f) + _n * 7919) % pool.Count];
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
        go.transform.localScale = src.transform.localScale * Mathf.Lerp(0.95f, 1.05f, seed);
        var high = go.transform.Find("LOD0 High Skinned");
        var rig = high != null ? high.Find("Rigged Character") : null;
        var actor = go.GetComponent<MinatoCrowdActor>();
        if (actor != null && rig != null)
        {
            float v = kind == MK.Walk ? (speed > 0f ? speed : Mathf.Lerp(0.55f, 0.9f, seed)) : 0f;
            actor.Configure(kind, rig, pos, end ?? pos, v, seed);
            if (kind == MK.Walk) actor.moveSpeed = v;
            actor.armDropDegrees = seated ? 0f : 32f;
        }
        // the name decides MapleCityLook's role; the look prefix decides the wardrobe
        go.name = (kind == MK.Walk ? "Walker_" : seated ? "Customer_" : "Chat_") + "SakuraFolk_" + role;
        try { MapleCityLife.DressForRegion(go, src.name, prefix, seated); }
        catch (System.Exception ex) { Debug.LogWarning($"[sakura-folk] dress failed for {role}: {ex.Message}"); }
        var tag = go.GetComponent<MapleCityLook>();
        string look = tag != null ? tag.look : "";
        _lookTally[string.IsNullOrEmpty(look) ? "(kit)" : look] = _lookTally.TryGetValue(string.IsNullOrEmpty(look) ? "(kit)" : look, out int c) ? c + 1 : 1;
        go.name = $"Sakura {role} {_n:D3}";
        var lg = go.GetComponent<LODGroup>();
        if (lg != null)
        {
            var lods = lg.GetLODs();
            for (int l = 1; l < lods.Length; l++)
                foreach (var r in lods[l].renderers) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
            if (lods.Length > 0) _tris0 += Tris(lods[0].renderers);
            if (lods.Length > 1) _tris1 += Tris(lods[1].renderers);
        }
        _people.Add(new Vector4(pos.x, pos.y, pos.z, seated ? 0.55f : 0.35f));
        _roles[role] = _roles.TryGetValue(role, out int n) ? n + 1 : 1;
        _n++;
        return go;
    }

    static long Tris(Renderer[] rs)
    {
        long t = 0;
        foreach (var r in rs)
        {
            if (r == null) continue;
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (m != null) for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3;
        }
        return t;
    }

    // =================================================================== photographer camera
    static void AddCamera(GameObject person)
    {
        var smr = person.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == "Mesh_0");
        if (smr == null) return;
        Transform head = null;
        foreach (var b in smr.bones) if (b != null && b.name == "Head") { head = b; break; }
        if (head == null) return;
        float H = smr.bounds.size.y;
        if (H < 0.3f) return;
        var mat = CameraMaterial();
        var cam = new GameObject("Photographer Camera");
        cam.transform.SetParent(head, true);
        var fwd = person.transform.forward;
        cam.transform.position = head.position + fwd * (0.085f * H) + Vector3.up * (0.03f * H);
        cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        var ls = head.lossyScale;
        Vector3 world(float w, float h, float d) => new Vector3(w / Mathf.Max(ls.x, 1e-4f), h / Mathf.Max(ls.y, 1e-4f), d / Mathf.Max(ls.z, 1e-4f));
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        var body = Part(cam.transform, "Body", cube, mat);
        body.localScale = world(0.095f * H, 0.060f * H, 0.040f * H);
        var lens = Part(cam.transform, "Lens", cyl, mat);
        lens.localRotation = Quaternion.Euler(90f, 0f, 0f);
        lens.localPosition = new Vector3(0f, 0f, 0f);
        lens.position = cam.transform.position + fwd * (0.040f * H);
        lens.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
        lens.localScale = world(0.036f * H, 0.022f * H, 0.036f * H);
        // cull with the figure's LOD0
        var lod = person.GetComponent<LODGroup>();
        if (lod != null)
        {
            var lods = lod.GetLODs();
            if (lods.Length > 0)
            {
                var list = new List<Renderer>(lods[0].renderers);
                list.AddRange(cam.GetComponentsInChildren<Renderer>(true));
                lods[0].renderers = list.ToArray();
                lod.SetLODs(lods);
            }
        }
        _cameras++;
    }

    static Transform Part(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    static Material _camMat;
    static Material CameraMaterial()
    {
        if (_camMat != null) return _camMat;
        string path = MatDir + "/SakuraTown_Camera.mat";
        _camMat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (_camMat == null)
        {
            var shader = Shader.Find("HDRP/Lit");
            _camMat = new Material(shader) { name = "SakuraTown_Camera" };
            AssetDatabase.CreateAsset(_camMat, path);
        }
        if (_camMat.HasProperty("_BaseColor")) _camMat.SetColor("_BaseColor", new Color(0.09f, 0.09f, 0.10f, 1f));
        if (_camMat.HasProperty("_Smoothness")) _camMat.SetFloat("_Smoothness", 0.35f);
        if (_camMat.HasProperty("_Metallic")) _camMat.SetFloat("_Metallic", 0.2f);
        EditorUtility.SetDirty(_camMat);
        return _camMat;
    }

    // =================================================================== building blocks
    static GameObject Standing(Transform g, string role, Vector3 p, Vector3 target, string prefix = LookPrefix, MK kind = MK.Idle)
    {
        var go = Person(g, role, p, target - p, kind, R(), prefix);
        return go;
    }

    /// <summary>A tight cluster (1-4 people) facing <paramref name="target"/>. Returns the number placed.</summary>
    static int Cluster(Transform g, string role, Vector3 anchor, float rMin, float rMax, int count, Vector3 target,
                       string prefix = LookPrefix)
    {
        if (!FindSpot(anchor, rMin, rMax, out var c)) return 0;
        int made = 0;
        Standing(g, role, c, target, prefix); made++;
        for (int k = 1; k < count; k++)
            for (int t = 0; t < 12; t++)
            {
                float a = R() * Mathf.PI * 2f, d = R(0.95f, 1.5f);
                var q = new Vector3(c.x + Mathf.Cos(a) * d, 0f, c.z + Mathf.Sin(a) * d);
                if (!Valid(ref q, 0.8f)) continue;
                // chatters turn toward the middle of the group, the rest toward the target
                var tgt = R() < 0.4f ? c : target;
                Standing(g, role, q, tgt, prefix); made++;
                break;
            }
        return made;
    }

    static bool Photographer(Transform g, Vector3 anchor, float rMin, float rMax, Vector3 target)
    {
        if (!FindSpot(anchor, rMin, rMax, out var p)) return false;
        var go = Person(g, "Photographer", p, target - p, MK.Idle, R());
        AddCamera(go);
        return true;
    }

    static bool Stroller(Transform g, string role, Vector3 a, Vector3 b, float speed = 0f, string prefix = LookPrefix)
    {
        if (!Valid(ref a) || !LegValid(a, b)) return false;
        var bb = b;
        if (!Ground(bb.x, bb.z, out float by)) return false;
        bb.y = by;
        Person(g, role, a, bb - a, MK.Walk, R(), prefix, bb, speed);
        return true;
    }

    // =================================================================== A. shrine torii
    static void BuildToriiShrine(Transform g)
    {
        var gate = GameObject.Find("Torii Gate");
        var pass = RoadOf("pass");
        if (pass == null) return;
        Vector3 T;
        if (gate != null) T = gate.transform.position;
        else { FrameAt(pass, 88f, out T, out _, out _); }
        // visitor groups round the lantern avenue and the gate
        for (int k = 0; k < 3; k++) Cluster(g, "Shrine Visitor", T, 8.5f, 20f, 3, T);
        for (int k = 0; k < 2; k++) Cluster(g, "Shrine Visitor", T, 9f, 24f, 2, T);
        // shrine staff (miko / priest) flanking the gate, facing it
        for (int k = 0; k < 2; k++)
            if (FindSpot(T, 8f, 15f, out var sp)) Standing(g, "Shrine Staff", sp, T, StaffPrefix);
        // photographers framing the torii
        for (int k = 0; k < 2; k++) Photographer(g, T, 11f, 20f, T);
        // short stroll legs between the lanterns and the trees
        int legs = 0;
        for (int tries = 0; tries < 40 && legs < 4; tries++)
        {
            if (!FindSpot(T, 9f, 28f, out var a)) continue;
            float ang = R() * Mathf.PI * 2f;
            var b = a + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * R(10f, 18f);
            if (Stroller(g, "Shrine Stroller", a, b)) legs++;
        }
    }

    // =================================================================== B. overlooks
    static void BuildOverlooks(Transform g)
    {
        var pass = RoadOf("pass");
        if (pass == null) return;
        // lakeside overlook (checkpoint 211.6 m) and descent overlook apex (~676 m): INLAND side
        // (-side), visitors facing across the road toward the valley.
        foreach (float m in new[] { 211.6f, 676f })
        {
            FrameAt(pass, m, out var p, out _, out var side);
            var inland = p - side * 10.5f;
            var view = p + side * 14f;
            for (int k = 0; k < 2; k++) Cluster(g, "Overlook Visitor", inland, 0f, 5f, 2, view);
            Photographer(g, inland, 0f, 6f, view);
        }
        // descent overlook footpath torii
        var ft = GameObject.Find("Descent Overlook Torii");
        if (ft != null)
        {
            var T = ft.transform.position;
            Cluster(g, "Shrine Visitor", T, 6f, 12f, 2, T);
            if (FindSpot(T, 5f, 10f, out var sp)) Standing(g, "Shrine Staff", sp, T, StaffPrefix);
            Photographer(g, T, 8f, 13f, T);
        }
        else Debug.LogWarning("[sakura-folk] no 'Descent Overlook Torii' in the scene - footpath visitors skipped.");
    }

    // =================================================================== C. under the sakura
    static void BuildUnderTrees(Transform g)
    {
        // candidate trees: 9-30 m from a road, spaced >= 45 m apart, nearest-the-road first
        var cands = _sakuraTrees
            .Select(t => (t, d: RoadDist(t.position.x, t.position.z)))
            .Where(x => x.d >= 9f && x.d <= 30f)
            .OrderBy(x => Mathf.Abs(x.d - 14f)).ToList();
        var chosen = new List<Transform>();
        foreach (var (t, d) in cands)
        {
            if (chosen.Any(c => Vector3.Distance(c.position, t.position) < 45f)) continue;
            chosen.Add(t);
            if (chosen.Count >= 8) break;
        }
        int i = 0;
        foreach (var t in chosen)
        {
            var T = t.position;
            // look toward the nearest road point: the "view" for a hanami bench
            Vector3 toward = Vector3.zero; float best = float.MaxValue;
            foreach (var q in _roadPts) { float dd = (new Vector3(q.x, 0, q.z) - new Vector3(T.x, 0, T.z)).sqrMagnitude; if (dd < best) { best = dd; toward = q; } }
            if (i % 2 == 0)
            {
                // photographer shooting up into the blossom, a friend beside
                if (!Photographer(g, T, 2.2f, 3.8f, T + Vector3.up * 0f)) { }
                Cluster(g, "Blossom Visitor", T, 2.2f, 4f, 2, T);
            }
            else
            {
                // bench pair facing the road/valley, and a standing admirer
                if (FindSpot(T, 2.2f, 3.6f, out var bp))
                {
                    var dir = toward - bp; dir.y = 0f;
                    var right = Vector3.Cross(Vector3.up, dir.normalized);
                    Person(g, "Blossom Bencher", bp, dir, MK.Sit, R());
                    var q2 = bp + right * 0.95f;
                    if (Valid(ref q2, 0.5f)) Person(g, "Blossom Bencher", q2, dir, MK.Sit, R());
                }
                Cluster(g, "Blossom Visitor", T, 2.4f, 4.2f, 1, T);
            }
            i++;
        }
        Debug.Log($"[sakura-folk] sakura trees used: {chosen.Count} of {_sakuraTrees.Count} near the road.");
    }

    // =================================================================== D. lake village
    static void BuildVillage(Transform g)
    {
        var vgo = GameObject.Find("Lake Village");
        Vector3 V;
        if (vgo != null)
        {
            var rs = vgo.GetComponentsInChildren<Renderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(vgo.transform.position, Vector3.one);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            V = b.center;
            Debug.Log($"[sakura-folk] lake village bounds centre {b.center:F1} size {b.size:F1}.");
        }
        else
        {
            var s1 = RoadOf("s1");
            if (s1 == null) return;
            FrameAt(s1, 1179f, out V, out _, out _);
            Debug.LogWarning("[sakura-folk] no 'Lake Village' object - using the s1 checkpoint position.");
        }
        // The village sits on the lakeshore road: people stand in the open ground round it
        for (int k = 0; k < 6; k++) Cluster(g, "Villager", V, 8f, 34f, 2, V);
        for (int k = 0; k < 3; k++) Cluster(g, "Villager", V, 10f, 38f, 3, V);
        // a couple of photographers capturing the lake / the shrine gate in the water
        for (int k = 0; k < 2; k++) Photographer(g, V, 12f, 36f, V + Vector3.right * 40f);
        // benches
        for (int k = 0; k < 3; k++)
            if (FindSpot(V, 10f, 36f, out var bp))
            {
                var dir = V - bp; dir.y = 0f;
                Person(g, "Village Bencher", bp, dir, MK.Sit, R());
            }
        // strolling villagers on short legs
        int legs = 0;
        for (int tries = 0; tries < 80 && legs < 8; tries++)
        {
            if (!FindSpot(V, 9f, 38f, out var a)) continue;
            float ang = R() * Mathf.PI * 2f;
            var b2 = a + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * R(9f, 17f);
            if (Stroller(g, "Villager Walker", a, b2)) legs++;
        }
    }

    // =================================================================== E. strolling pairs
    static void BuildStrollers(Transform g)
    {
        int made = 0;
        foreach (var (id, lo, hi, want) in new[] { ("pass", 40f, 1340f, 6), ("s1", 40f, 1860f, 6) })
        {
            var road = RoadOf(id);
            if (road == null) continue;
            int got = 0;
            for (int tries = 0; tries < 160 && got < want; tries++)
            {
                float m = R(lo, hi);
                if (id == "pass" && m > 295f && m < 350f) continue;      // the cliff tunnel
                FrameAt(road, m, out var p, out var tan, out var side);
                float off = (R() < 0.5f ? -1f : 1f) * R(8.8f, 12f);
                var a = p + side * off;
                var b = a + tan * (R() < 0.5f ? 1f : -1f) * R(12f, 20f);
                if (Stroller(g, "Verge Stroller", a, b)) { got++; made++; }
            }
        }
        Debug.Log($"[sakura-folk] verge strollers: {made}.");
    }
}

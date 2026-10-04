using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AMBIENT PEDESTRIANS (copilot, 2026-09-26). One runtime director for every map's ambient
/// walking/standing crowd. It is created automatically after a scene loads, discovers the
/// ambient <see cref="MinatoCrowdActor"/> figures (Maple City Life pedestrians/chats/cafes and
/// the Minato waterfront/market/beach crowd), dresses them from the shared
/// <see cref="PedestrianWardrobe"/> with a variety solver (no identical neighbours) and attaches
/// a <see cref="PedestrianBrain"/> to each.
///
/// NOT touched: cyclists (MotionKind.Cyclist / "Boulevard Cyclists"), Maple City runners
/// (MapleCityRunner), race NPCs, Hanakage, greeting-card riders, shop mannequins, the player.
///
/// BEHAVIOUR LOD (PROVISIONAL): full brain (awareness + neighbour avoidance + acting) inside
/// <see cref="FullRadius"/>; path/speed only inside <see cref="MidRadius"/>; a round-robin tick
/// beyond. Figures are never enabled/disabled by this system, so nothing pops in view; the
/// existing LODGroups only cull at a sub-5-pixel screen height.
///
/// Kill switch: PlayerPrefs "MapleRide.Pedestrians.Disabled" = 1 (used for before/after captures).
/// </summary>
[DefaultExecutionOrder(-30)]
public sealed class PedestrianDirector : MonoBehaviour
{
    public const string DisabledPref = "MapleRide.Pedestrians.Disabled";
    public static float FullRadius = 70f;       // PROVISIONAL
    public static float MidRadius = 260f;       // matches MapleCityWalker.updateDistance
    public static float VarietyRadius = 9f;     // PROVISIONAL: no identical looks within this
    public static int FarTickDivisor = 8;

    public static PedestrianDirector Instance { get; private set; }
    public readonly List<PedestrianBrain> Brains = new List<PedestrianBrain>();
    public int Dressed { get; private set; }
    public int LaneCount, SegmentCount, SpotCount, SegmentsClipped, SegmentsBlocked;
    public float SetupMs;

    // cyclist
    public bool HasCyclist { get; private set; }
    public Vector3 CyclistPosition { get; private set; }
    public Vector3 CyclistVelocity { get; private set; }
    public bool overrideCyclist;
    public Vector3 overridePosition, overrideVelocity;
    Transform _rider;
    Vector3 _lastRider;
    bool _haveLast;
    float _nextRiderSearch;

    // neighbour grid (full-tier only)
    const float Cell = 4f;
    readonly Dictionary<long, List<PedestrianBrain>> _grid = new Dictionary<long, List<PedestrianBrain>>();
    readonly List<List<PedestrianBrain>> _pool = new List<List<PedestrianBrain>>();
    readonly List<PedestrianBrain> _near = new List<PedestrianBrain>();
    readonly List<PedestrianBrain> _full = new List<PedestrianBrain>();
    readonly HashSet<MinatoCrowdActor> _known = new HashSet<MinatoCrowdActor>();
    float[] _farDt = Array.Empty<float>();
    float _nextScan;
    int _seenVersion = -1;
    double _msDress, _msBlockers, _msSegments;
    PedestrianWardrobe _wardrobe;
    PedestrianModelLibrary _modelLib;   // claude-peds: null unless the model set pref is on

    static readonly string[] MinatoContainers = { "Waterfront Pedestrians", "Market Shoppers", "City Beach" };
    static readonly string[] MapleContainers = { "Pedestrians", "Street Chats", "Cafes", "Lived In" };
    static readonly string[] Excluded = { "Runners", "Race", "Hanakage", "Greeting", "Mannequin", "Boulevard Cyclists", "Kuro" };

    // ---- Nagisa Bay (Claude, 2026-10-02) ------------------------------------------------------
    // Nagisa's people are clones of the same Minato crowd donors, but nothing ever dressed them at
    // run time: in streamed play they showed the donor's raw camouflage atlas AND its baked cycling
    // helmet. The Minato/Maple wardrobe path (helmetless body + hair cap + clothing look) fixes both,
    // so Nagisa figures now go through exactly the same dressing.
    const string NagisaRoot = "Nagisa Bay Environment";
    // Containers whose name mentions "runners" also hold ordinary guests, so Nagisa is excluded by
    // component (MapleCityRunner / sunbather / beach activity) rather than by container name.
    static readonly string[] ExcludedNagisa = { "Race", "Hanakage", "Greeting", "Mannequin", "Boulevard Cyclists", "Cyclists" };
    const int NagisaPerScan = 90;
    readonly HashSet<MinatoCrowdActor> _nagisa = new HashSet<MinatoCrowdActor>();
    PedestrianModelLibrary _nagisaLib;               // loaded even when the opt-in model-set pref is off
    Texture2D[] _promenadeKits, _beachKits;          // PedKit lists (bikinis/trunks only on the beach)

    /// <summary>Kuro-kit look for Nagisa figures: street-style kits on the promenade, swimwear only on the sand.</summary>
    void LoadNagisaKits()
    {
        _nagisaLib = PedestrianModelLibrary.Load();
        var set = _nagisaLib != null ? _nagisaLib.kuroRider : null;
        if (set == null || !set.enabled || set.beachKits == null || set.beachKits.Length == 0) { _nagisaLib = null; return; }
        var promenade = new List<Texture2D>();
        foreach (var k in set.beachKits)
            if (k != null && k.name.IndexOf("Bikini", StringComparison.OrdinalIgnoreCase) < 0 &&
                k.name.IndexOf("Trunks", StringComparison.OrdinalIgnoreCase) < 0) promenade.Add(k);
        _promenadeKits = promenade.ToArray();
        _beachKits = set.beachKits;
    }

    static bool OnBeach(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name.IndexOf("Beach", StringComparison.OrdinalIgnoreCase) >= 0 || p.name.IndexOf("Sand", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static bool IsNagisaFigure(MinatoCrowdActor a)
    {
        if (a.GetComponent<NagisaSunbather>() != null || a.GetComponent<NagisaBeachActivity>() != null) return false;
        bool underNagisa = false;
        for (var p = a.transform.parent; p != null; p = p.parent)
        {
            if (p.name == NagisaRoot) underNagisa = true;
            for (int i = 0; i < ExcludedNagisa.Length; i++)
                if (p.name.IndexOf(ExcludedNagisa[i], StringComparison.OrdinalIgnoreCase) >= 0) return false;
        }
        return underNagisa;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();

    public static PedestrianDirector Ensure()
    {
        if (!Application.isPlaying) return null;
        if (PlayerPrefs.GetInt(DisabledPref, 0) == 1) return null;
        if (Instance != null) return Instance;
        var go = new GameObject("Pedestrian Director");
        DontDestroyOnLoad(go);
        return Instance = go.AddComponent<PedestrianDirector>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        _wardrobe = PedestrianWardrobe.Load();
        if (_wardrobe == null) Debug.LogWarning("[peds] no PedestrianWardrobe in Resources - looks unchanged");
        if (PedestrianModelLibrary.ActiveSet() != null)
        {
            _modelLib = PedestrianModelLibrary.Load();
            Debug.Log(_modelLib != null
                ? $"[peds-model] model set '{PedestrianModelLibrary.ActiveSet()}' on: library {(_modelLib.enabledGlobally ? "enabled" : "DISABLED")}, {_modelLib.models.Count} models"
                : "[peds-model] model set requested but no PedestrianModelLibrary in Resources/Pedestrians");
        }
        LoadNagisaKits();
        Scan();
    }

    // ================================================================== discovery

    static bool HasAncestor(Transform t, string[] names, out string hit)
    {
        for (var p = t.parent; p != null; p = p.parent)
            for (int i = 0; i < names.Length; i++)
                if (p.name == names[i]) { hit = names[i]; return true; }
        hit = null;
        return false;
    }

    static bool IsExcluded(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            for (int i = 0; i < Excluded.Length; i++)
                if (p.name.IndexOf(Excluded[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>Finds ambient figures not yet managed. Cheap enough to repeat after region loads.</summary>
    public void Scan()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _seenVersion = MinatoCrowdActor.RegistryVersion;
        var actors = MinatoCrowdActor.Enabled.Count > 0
            ? new List<MinatoCrowdActor>(MinatoCrowdActor.Enabled).ToArray()
            : FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var fresh = new List<(MinatoCrowdActor a, bool minato)>();
        foreach (var a in actors)
        {
            if (a == null || _known.Contains(a) || !a.isActiveAndEnabled) continue;
            _known.Add(a);
            if (a.motion == MinatoCrowdActor.MotionKind.Cyclist) continue;
            if (a.GetComponent<MapleCityRunner>() != null) continue;
            if (IsExcluded(a.transform) && !IsNagisaFigure(a)) continue;
            bool nagisa = false;
            bool minato = HasAncestor(a.transform, MinatoContainers, out _);
            bool maple = !minato && HasAncestor(a.transform, MapleContainers, out _) &&
                         a.GetComponentInParent<Transform>() != null && UnderMapleLife(a.transform);
            if (!minato && !maple) { nagisa = IsNagisaFigure(a); if (nagisa) _nagisa.Add(a); }
            if (!minato && !maple && !nagisa) continue;
            if (!a.IsReady) continue;
            fresh.Add((a, minato || nagisa));   // Nagisa is dressed like Minato (region lookups use _nagisa)
        }
        if (fresh.Count == 0) return;
        // deterministic order, so the variety solver gives the same town every run
        fresh.Sort((x, y) => string.CompareOrdinal(Path(x.a.transform), Path(y.a.transform)));

        var assigned = new List<PedestrianAppearance>();
        foreach (var b in Brains) if (b != null && b.look != null) assigned.Add(b.look);
        var lookGrid = new Dictionary<long, List<PedestrianAppearance>>();
        foreach (var l in assigned) GridAdd(lookGrid, l.transform.position, l);

        var blockers = new List<Bounds>();
        bool blockersBuilt = false;

        // Nagisa Bay streams in thousands of figures; dressing them all in one frame hitched ~3 s. Dress a
        // budget per scan and defer the rest to the next frame (Minato/Maple behaviour is unchanged).
        int nagisaDressed = 0; bool deferred = false;
        foreach (var (a, minato) in fresh)
        {
            if (_nagisa.Contains(a) && nagisaDressed >= NagisaPerScan) { _known.Remove(a); deferred = true; continue; }
            if (_nagisa.Contains(a)) nagisaDressed++;
            var go = a.gameObject;
            var look = go.GetComponent<PedestrianAppearance>() ?? go.AddComponent<PedestrianAppearance>();
            look.donorKey = PedestrianAppearance.DonorKeyOf(go);
            var t0 = sw.Elapsed.TotalMilliseconds;
            // claude-peds 2026-09-26: opt-in drop-in model swap (PedestrianModelLibrary). _modelLib is
            // only loaded when PlayerPrefs "MapleRide.Pedestrians.ModelSet" is set; otherwise this is skipped.
            PedestrianModelSwap swap = null;
            bool isNagisa = _nagisa.Contains(a);
            string region = isNagisa ? NagisaBayLook.RegionId : minato ? RegionCatalog.MinatoCoast : RegionCatalog.MapleCity;
            bool kuroPath = _modelLib != null && _modelLib.kuroRider != null && _modelLib.kuroRider.enabled &&
                            PedestrianModelLibrary.ActiveSet() != PedestrianModelLibrary.FbxTestSet &&
                            _modelLib.enabledGlobally && Array.IndexOf(_modelLib.regions, region) >= 0;
            if (_modelLib != null && !kuroPath && a.motion != MinatoCrowdActor.MotionKind.Sit &&
                go.GetComponent<MapleCityCafeGesture>() == null)
            {
                var eligible = _modelLib.Eligible(region);
                if (eligible.Count > 0) swap = PedestrianModelSwap.TrySwap(a, _modelLib, eligible);
            }
            ChooseLook(look, a, lookGrid, minato);
            if (swap != null) look.ApplyModel(a, swap.modelId, swap.variantId, swap.bodyMaterial, swap.skins);
            else
            {
                look.Apply(_wardrobe, _lookIndex, a);
                if (kuroPath && look.applied)
                    PedestrianKuroLook.Apply(look, a, _modelLib.kuroRider, PedestrianModelSwap.Hash(go.transform));
                else if (isNagisa && look.applied && _nagisaLib != null)
                {
                    var tag = PedestrianKuroLook.Apply(look, a, _nagisaLib.kuroRider, PedestrianModelSwap.Hash(go.transform),
                                                       OnBeach(go.transform) ? _beachKits : _promenadeKits);
                    // Nagisa clones also carry the donor's own raw "NpcHair" mesh (black material); with the authored
                    // hair now on the head it only showed as a black dome underneath.
                    if (tag != null)
                        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            if (smr.name == "NpcHair") smr.forceRenderingOff = true;
                }
            }
            _msDress += sw.Elapsed.TotalMilliseconds - t0;
            if (look.applied) Dressed++;
            GridAdd(lookGrid, go.transform.position, look);

            // Plain Nagisa walkers (no lane) keep MinatoCrowdActor's own walk along their path; a brain
            // would stand them still. They are dressed above but get no brain.
            if (isNagisa && a.motion == MinatoCrowdActor.MotionKind.Walk && go.GetComponent<MapleCityWalker>() == null) continue;
            var brain = go.GetComponent<PedestrianBrain>() ?? go.AddComponent<PedestrianBrain>();
            brain.actor = a;
            brain.look = look;
            brain.hasGesture = go.GetComponent<MapleCityCafeGesture>() != null;
            brain.seated = a.motion == MinatoCrowdActor.MotionKind.Sit;
            var w = go.GetComponent<MapleCityWalker>();
            if (w != null && w.lanes != null && w.lanes.Valid(w.lane))
            {
                brain.InitLane(w);
                LaneCount++;
            }
            else if (minato && a.motion == MinatoCrowdActor.MotionKind.Walk &&
                     Vector3.Distance(a.pathStart, a.pathEnd) > 1.5f)
            {
                var t1 = sw.Elapsed.TotalMilliseconds;
                if (!blockersBuilt) { CollectBlockers(blockers); AddFigureBlockers(blockers, fresh); blockersBuilt = true; _msBlockers += sw.Elapsed.TotalMilliseconds - t1; t1 = sw.Elapsed.TotalMilliseconds; }
                bool okSeg = SetupSegment(brain, a, blockers);
                _msSegments += sw.Elapsed.TotalMilliseconds - t1;
                if (okSeg) SegmentCount++;
                else { brain.InitSpot(); SpotCount++; SegmentsBlocked++; }
            }
            else
            {
                brain.InitSpot();
                SpotCount++;
            }
            Brains.Add(brain);
        }
        if (deferred) _seenVersion = -1;   // more Nagisa figures waiting: scan again next frame
        LinkCompanions();
        if (_modelLib != null)
            Debug.Log($"[peds-model] kuro-rider look {PedestrianKuroLook.Applied} (kits {PedestrianKuroLook.Kitted}, hair {PedestrianKuroLook.Haired}, blinking {PedestrianKuroLook.Blinking}); " +
                      $"fbx swapped {PedestrianModelSwap.Swapped}, failed {PedestrianModelSwap.Failed} ({PedestrianModelSwap.UsageSummary()})");
        _farDt = new float[Brains.Count];
        sw.Stop();
        SetupMs += (float)sw.Elapsed.TotalMilliseconds;
        Debug.Log($"[peds] director: {Brains.Count} ambient pedestrians ({LaneCount} lane walkers, " +
                  $"{SegmentCount} corridor walkers, {SpotCount} standing/sitting); dressed {Dressed}; " +
                  $"corridors clipped {SegmentsClipped}, blocked {SegmentsBlocked}; setup {SetupMs:0} ms " +
                  $"(dress {_msDress:0}, prop scan {_msBlockers:0}, corridors {_msSegments:0}); corridor lengths " +
                  $"<4m {CorridorHistogram[0]}, 4-8m {CorridorHistogram[1]}, 8-12m {CorridorHistogram[2]}, 12m+ {CorridorHistogram[3]}");
    }

    static bool UnderMapleLife(Transform t)
    {
        for (var p = t.parent; p != null; p = p.parent)
            if (p.name == "Maple City Life") return true;
        return false;
    }

    static string Path(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    // ================================================================== variety

    int _lookIndex;

    static long Key(Vector3 p) => ((long)Mathf.FloorToInt(p.x / Cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / Cell);

    static void GridAdd(Dictionary<long, List<PedestrianAppearance>> g, Vector3 p, PedestrianAppearance l)
    {
        long k = Key(p);
        if (!g.TryGetValue(k, out var list)) g[k] = list = new List<PedestrianAppearance>();
        list.Add(l);
    }

    static IEnumerable<PedestrianAppearance> LooksNear(Dictionary<long, List<PedestrianAppearance>> g, Vector3 p, float r)
    {
        int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell), n = Mathf.CeilToInt(r / Cell);
        for (int x = cx - n; x <= cx + n; x++)
            for (int z = cz - n; z <= cz + n; z++)
                if (g.TryGetValue(((long)x << 32) ^ (uint)z, out var list))
                    foreach (var l in list)
                        if (l != null && (l.transform.position - p).sqrMagnitude <= r * r) yield return l;
    }

    /// <summary>
    /// Greedy per-figure choice of look/cap/hat/bag/persona that minimises matches with figures
    /// already dressed within VarietyRadius. Donor (body mesh) is fixed; everything else varies.
    /// </summary>
    void ChooseLook(PedestrianAppearance look, MinatoCrowdActor a, Dictionary<long, List<PedestrianAppearance>> grid, bool minato)
    {
        var rng = new System.Random(Path(a.transform).GetHashCode());
        var donor = _wardrobe != null ? _wardrobe.Find(look.donorKey) : null;
        int nLooks = donor != null ? Mathf.Max(1, donor.looks.Length) : 1;
        int nCaps = donor != null ? Mathf.Max(1, donor.caps.Length) : 1;
        // Minato corridor walkers roam up to ~CorridorExtend m past their spawn, so look further
        float radius = minato ? VarietyRadius + 2f * CorridorExtend : VarietyRadius;
        var near = new List<PedestrianAppearance>(LooksNear(grid, a.transform.position, radius));
        bool walking = a.motion == MinatoCrowdActor.MotionKind.Walk;
        bool shoppingStreet = minato && HasAncestor(a.transform, new[] { "Market Shoppers" }, out _) ||
                              a.transform.position.x > float.MaxValue;   // (Maple Row handled by bag odds below)

        float best = float.MaxValue;
        int bLook = 0, bCap = 0, bHatC = 0, bBagC = 0;
        PedestrianAppearance.Hat bHat = 0; PedestrianAppearance.Bag bBag = 0; PedestrianAppearance.Persona bPer = 0;
        for (int attempt = 0; attempt < 40 && best > 0.06f; attempt++)
        {
            int li = rng.Next(nLooks), ci = rng.Next(nCaps);
            var hat = rng.NextDouble() < 0.22 ? (PedestrianAppearance.Hat)(1 + rng.Next(3)) : PedestrianAppearance.Hat.None;
            double bagRoll = rng.NextDouble();
            var bag = !walking && a.motion == MinatoCrowdActor.MotionKind.Sit ? PedestrianAppearance.Bag.None
                    : bagRoll < 0.18 ? PedestrianAppearance.Bag.Backpack
                    : bagRoll < 0.34 ? PedestrianAppearance.Bag.Shoulder
                    : bagRoll < (shoppingStreet ? 0.62 : 0.44) && walking ? PedestrianAppearance.Bag.Shopping
                    : PedestrianAppearance.Bag.None;
            double pr = rng.NextDouble();
            var per = pr < 0.13 ? PedestrianAppearance.Persona.Senior : pr < 0.25 ? PedestrianAppearance.Persona.Young : PedestrianAppearance.Persona.Adult;
            int hc = rng.Next(PedestrianAppearance.Palette.Length), bc = rng.Next(PedestrianAppearance.Palette.Length);
            string lookId = donor != null && donor.looks.Length > 0 ? donor.looks[li].id : "";
            string baseSig = $"{look.donorKey}|{lookId}|{ci}";
            float cost = 0f;
            foreach (var o in near)
            {
                if (o == look) continue;
                float d = Vector3.Distance(o.transform.position, a.transform.position);
                float w = 1f - d / (radius * 1.2f);
                if (o.BaseSignature == baseSig) cost += 10f * w;
                else if (o.donorKey == look.donorKey && o.lookId == lookId) cost += 3f * w;
                else if (o.donorKey == look.donorKey) cost += 0.6f * w;
                if (o.hat == hat && hat != 0) cost += 0.8f * w;
                if (o.bag == bag && bag != 0) cost += 0.5f * w;
            }
            cost += (float)rng.NextDouble() * 0.05f;
            if (cost < best)
            {
                best = cost; bLook = li; bCap = ci; bHat = hat; bBag = bag; bPer = per; bHatC = hc; bBagC = bc;
            }
        }
        _lookIndex = bLook;
        look.capStyle = bCap;
        look.hat = bHat; look.hatColour = bHatC;
        look.bag = bBag; look.bagColour = bBagC;
        look.persona = bPer;
        if (donor != null && donor.looks.Length > 0) look.lookId = donor.looks[bLook].id;
        // body-silhouette / age presentation: small uniform scale shifts only (rig stays one unit)
        float sc = bPer == PedestrianAppearance.Persona.Young ? 0.93f : bPer == PedestrianAppearance.Persona.Senior ? 0.97f : 1f;
        if (sc != 1f) a.transform.localScale *= sc;
    }

    // ================================================================== companions

    void LinkCompanions()
    {
        var byLane = new Dictionary<(MapleCityWalkLanes, int), List<PedestrianBrain>>();
        foreach (var b in Brains)
        {
            if (b == null || b.kind != PedestrianBrain.PathKind.Lane || b.walker == null || b.leader != null || b.follower != null) continue;   // already paired (incremental re-scans)
            var k = (b.walker.lanes, b.walker.lane);
            if (!byLane.TryGetValue(k, out var l)) byLane[k] = l = new List<PedestrianBrain>();
            l.Add(b);
        }
        foreach (var l in byLane.Values)
        {
            l.Sort((x, y) => x.walker.startS.CompareTo(y.walker.startS));
            for (int i = 1; i < l.Count; i++)
            {
                var a = l[i - 1]; var b = l[i];
                if (a.leader != null || a.follower != null) continue;
                if (Mathf.Abs(a.walker.speed - b.walker.speed) < 1e-4f && Mathf.Abs(a.walker.startS - b.walker.startS) < 0.5f &&
                    Mathf.Abs(Mathf.Abs(a.walker.lateral) - 0.28f) < 0.01f)
                {
                    b.MakeFollower(a);
                    i++;
                }
            }
        }
    }

    // ================================================================== corridor clean-up (Minato)

    static readonly string[] Walkable = { "Connected Port", "Beach Sand", "Terrain_Chunk", "Pavement", "Plaza", "Promenade", "Boardwalk", "Pier", "Road" };

    static bool IsWalkable(string n)
    {
        for (int i = 0; i < Walkable.Length; i++) if (n.IndexOf(Walkable[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>Standing/sitting Minato figures (and short-path figures that become spots) block
    /// corridors too, so no walker's path runs through someone chatting or waiting.</summary>
    static void AddFigureBlockers(List<Bounds> into, List<(MinatoCrowdActor a, bool minato)> fresh)
    {
        foreach (var (a, minato) in fresh)
        {
            if (!minato || a == null) continue;
            bool walker = a.motion == MinatoCrowdActor.MotionKind.Walk && Vector3.Distance(a.pathStart, a.pathEnd) > 1.5f;
            if (walker) continue;
            var p = a.transform.position;
            into.Add(new Bounds(p + Vector3.up * 0.75f, new Vector3(0.45f, 1.5f, 0.45f)));
        }
    }

    /// <summary>Static prop boxes (Minato has few colliders): used to keep corridors off props.</summary>
    void CollectBlockers(List<Bounds> into)
    {
        GameObject root = null;
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name.IndexOf("Minato", StringComparison.OrdinalIgnoreCase) >= 0) { root = go; break; }
        var t = root != null ? root.transform : null;
        if (t == null)
        {
            var f = GameObject.Find("Minato Coast Environment");
            if (f != null) t = f.transform;
        }
        if (t == null) return;
        foreach (var r in t.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (r == null || !r.enabled) continue;
            if (r.GetComponentInParent<MinatoCrowdActor>() != null) continue;
            var n = r.name;
            if (IsWalkable(n) || n.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Ocean", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Sky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Line", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Marking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Canopy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Awning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Lamp Glow", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            var b = r.bounds;
            // merged/huge meshes (terrain, city blocks batched together) are not usable as boxes
            // ...except long, narrow, low runs (axis-aligned planter rows, hedges, benches, railings):
            // their box is still a good fit and walkers brushed their foliage before
            bool longRun = Mathf.Min(b.size.x, b.size.z) < 3f && Mathf.Max(b.size.x, b.size.z) < 80f && b.size.y < 3.5f;
            if ((b.size.x > 14f || b.size.z > 14f) && !longRun) continue;
            if (b.size.y < 0.25f) continue;
            into.Add(b);
        }
    }

    readonly Dictionary<long, List<int>> _blockGrid = new Dictionary<long, List<int>>();
    List<Bounds> _blockList;
    const float BlockCell = 8f;

    void IndexBlockers(List<Bounds> blockers)
    {
        _blockList = blockers;
        _blockGrid.Clear();
        for (int i = 0; i < blockers.Count; i++)
        {
            var b = blockers[i];
            int x0 = Mathf.FloorToInt((b.min.x - 0.4f) / BlockCell), x1 = Mathf.FloorToInt((b.max.x + 0.4f) / BlockCell);
            int z0 = Mathf.FloorToInt((b.min.z - 0.4f) / BlockCell), z1 = Mathf.FloorToInt((b.max.z + 0.4f) / BlockCell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long k = ((long)x << 32) ^ (uint)z;
                    if (!_blockGrid.TryGetValue(k, out var l)) _blockGrid[k] = l = new List<int>();
                    l.Add(i);
                }
        }
    }

    bool Blocked(Vector3 p, float groundY, List<Bounds> blockers, float radius)
    {
        if (_blockList != blockers) IndexBlockers(blockers);
        long key = ((long)Mathf.FloorToInt(p.x / BlockCell) << 32) ^ (uint)Mathf.FloorToInt(p.z / BlockCell);
        if (!_blockGrid.TryGetValue(key, out var cell)) return false;
        for (int ci = 0; ci < cell.Count; ci++)
        {
            var b = blockers[cell[ci]];
            if (b.max.y < groundY + 0.12f || b.min.y > groundY + 1.5f) continue;
            if (p.x < b.min.x - radius || p.x > b.max.x + radius || p.z < b.min.z - radius || p.z > b.max.z + radius) continue;
            return true;
        }
        return false;
    }

    static bool GroundAt(Vector3 p, out float y, out string surface)
    {
        y = p.y; surface = "";
        var hits = Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 7f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.GetComponentInParent<MinatoCrowdActor>() != null) continue;
            if (h.point.y > best) { best = h.point.y; surface = h.collider.name; }
        }
        if (float.IsNegativeInfinity(best)) return false;
        y = best;
        return true;
    }

    /// <summary>
    /// Samples the Minato corridor every 0.5 m: keeps the contiguous stretch around the figure that
    /// stays on the same walkable surface family, has no step &gt; 0.12 m (curbs) and is clear of prop
    /// boxes, and records how far each sample may drift sideways for avoidance.
    /// </summary>
    bool SetupSegment(PedestrianBrain brain, MinatoCrowdActor a, List<Bounds> blockers)
    {
        var A = a.pathStart; var B = a.pathEnd;
        var flat = B - A; flat.y = 0f;
        float len = flat.magnitude;
        var fwd = flat / len;
        // The stager's Minato corridors are mostly 2-6 m, so walkers spent most of their life
        // turning round. Probe up to CorridorExtend m further along the same line each way; the
        // contiguous-clear test below keeps only what stays on the same surface, curb-free and
        // clear of props, so the extension never crosses into a road, the sea or a stall.
        float ext = Mathf.Clamp((CorridorTargetLength - len) * 0.5f, 0f, CorridorExtend);
        if (ext > 0f) { A -= fwd * ext; B += fwd * ext; len += 2f * ext; }
        var right = Vector3.Cross(Vector3.up, fwd);
        int n = Mathf.Max(2, Mathf.FloorToInt(len / 0.5f) + 1);
        var ok = new bool[n];
        var lo = new float[n]; var hi = new float[n];
        float cur = Mathf.Clamp(Vector3.Dot(a.transform.position - A, fwd), 0f, len);
        GroundAt(a.transform.position, out float y0, out string homeSurface);
        string family = SurfaceFamily(homeSurface);
        var ys = new float[n];
        for (int i = 0; i < n; i++)
        {
            var p = A + fwd * Mathf.Min(len, i * 0.5f);
            bool g = GroundAt(p, out float y, out string surf);
            ys[i] = g ? y : float.NaN;
            ok[i] = g && (family == "" || SurfaceFamily(surf) == family) && !Blocked(p, y, blockers, PathPropClearance);
            lo[i] = hi[i] = 0f;
            if (ok[i])
            {
                for (float off = 0.3f; off <= 0.61f; off += 0.3f)
                {
                    if (hi[i] >= off - 0.31f && Side(p + right * off, y, family, blockers)) hi[i] = off;
                    if (lo[i] <= -off + 0.31f && Side(p - right * off, y, family, blockers)) lo[i] = -off;
                }
            }
        }
        // step check between neighbours (curb / stairs lip)
        for (int i = 1; i < n; i++)
            if (!float.IsNaN(ys[i]) && !float.IsNaN(ys[i - 1]) && Mathf.Abs(ys[i] - ys[i - 1]) > 0.12f) ok[i] = false;
        int c = Mathf.Clamp(Mathf.RoundToInt(cur / 0.5f), 0, n - 1);
        if (!ok[c])
        {
            // figure itself stands somewhere odd: find the nearest ok sample
            int bestI = -1;
            for (int d = 1; d < n && bestI < 0; d++)
            {
                if (c - d >= 0 && ok[c - d]) bestI = c - d;
                else if (c + d < n && ok[c + d]) bestI = c + d;
            }
            if (bestI < 0) return false;
            c = bestI;
        }
        int s0 = c, s1 = c;
        while (s0 > 0 && ok[s0 - 1]) s0--;
        while (s1 < n - 1 && ok[s1 + 1]) s1++;
        float from = s0 * 0.5f, to = Mathf.Min(len, s1 * 0.5f);
        if (to - from < 2.5f) return false;
        float kept = to - from;
        CorridorHistogram[kept < 4f ? 0 : kept < 8f ? 1 : kept < 12f ? 2 : 3]++;
        if (s0 > 0 || s1 < n - 1) SegmentsClipped++;
        int m = s1 - s0 + 1;
        var clo = new float[m]; var chi = new float[m];
        Array.Copy(lo, s0, clo, 0, m); Array.Copy(hi, s0, chi, 0, m);
        brain.SetSegmentClearance(clo, chi);
        var a2 = A + fwd * from; var b2 = A + fwd * to;
        a2.y = A.y; b2.y = B.y;
        float startS = Mathf.Clamp(cur - from, 0f, to - from);
        int dir = Vector3.Dot(a.transform.forward, fwd) >= 0f ? 1 : -1;
        brain.InitSegment(a2, b2, startS, dir);
        return true;
    }

    public readonly int[] CorridorHistogram = new int[4];   // kept corridor lengths <4 / 4-8 / 8-12 / 12+ m
    public static float CorridorTargetLength = 14f;   // PROVISIONAL: desired Minato corridor length
    public static float CorridorExtend = 5f;           // max probe beyond each stager end
    public static float PathPropClearance = 0.42f;     // PROVISIONAL: min centre-to-prop-box gap (0.3 hugged planter walls)

    bool Side(Vector3 p, float y, string family, List<Bounds> blockers)
    {
        if (!GroundAt(p, out float gy, out string surf)) return false;
        if (Mathf.Abs(gy - y) > 0.1f) return false;
        if (family != "" && SurfaceFamily(surf) != family) return false;
        return !Blocked(p, gy, blockers, PathPropClearance - 0.05f);
    }

    static string SurfaceFamily(string n)
    {
        if (string.IsNullOrEmpty(n)) return "";
        if (n.StartsWith("Connected Port", StringComparison.Ordinal)) return "pave";
        if (n.IndexOf("Beach Sand", StringComparison.OrdinalIgnoreCase) >= 0) return "sand";
        if (n.StartsWith("Terrain_Chunk", StringComparison.Ordinal)) return "terrain";
        return n;
    }

    // ================================================================== tick

    void Update()
    {
        // newly enabled crowd (a region streamed/activated): dress + brain it THIS frame, before
        // it renders, so no look ever changes in view. A slow fallback poll covers anything else.
        if (MinatoCrowdActor.RegistryVersion != _seenVersion || Time.time >= _nextScan)
        {
            _nextScan = Time.time + 20f;
            Scan();
        }
        UpdateCyclist();
        var cam = Camera.main;
        var cp = cam != null ? cam.transform.position : CyclistPosition;
        float dt = Time.deltaTime;
        if (_farDt.Length != Brains.Count) Array.Resize(ref _farDt, Brains.Count);

        // pass 1: tier + grid
        foreach (var l in _grid.Values) { l.Clear(); _pool.Add(l); }
        _grid.Clear();
        _full.Clear();
        float full2 = FullRadius * FullRadius, mid2 = MidRadius * MidRadius;
        int frame = Time.frameCount;
        for (int i = 0; i < Brains.Count; i++)
        {
            var b = Brains[i];
            if (b == null || !b.isActiveAndEnabled) continue;
            float d2 = (b.transform.position - cp).sqrMagnitude;
            if (HasCyclist) d2 = Mathf.Min(d2, (b.transform.position - CyclistPosition).sqrMagnitude);
            if (d2 <= full2)
            {
                _full.Add(b);
                long k = Key(b.transform.position);
                if (!_grid.TryGetValue(k, out var list))
                {
                    if (_pool.Count > 0) { list = _pool[_pool.Count - 1]; _pool.RemoveAt(_pool.Count - 1); }
                    else list = new List<PedestrianBrain>();
                    _grid[k] = list;
                }
                list.Add(b);
                b.tier = 0;
            }
            else if (d2 <= mid2)
            {
                b.tier = 1;
                b.Tick(dt, false, this);
            }
            else
            {
                b.tier = 2;
                _farDt[i] += dt;
                if ((i + frame) % FarTickDivisor == 0) { b.Tick(_farDt[i], false, this); _farDt[i] = 0f; }
            }
        }
        // pass 2: full brains (leaders before followers so pairs stay in step)
        for (int i = 0; i < _full.Count; i++) if (_full[i].leader == null) _full[i].Tick(dt, true, this);
        for (int i = 0; i < _full.Count; i++) if (_full[i].leader != null) _full[i].Tick(dt, true, this);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _full.Count; i++) if (_full[i] != null) _full[i].LateTick(dt);
    }

    public List<PedestrianBrain> Neighbours(Vector3 p, float r)
    {
        _near.Clear();
        int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell), n = Mathf.CeilToInt(r / Cell);
        float r2 = r * r;
        for (int x = cx - n; x <= cx + n; x++)
            for (int z = cz - n; z <= cz + n; z++)
                if (_grid.TryGetValue(((long)x << 32) ^ (uint)z, out var list))
                    for (int i = 0; i < list.Count; i++)
                        if ((list[i].transform.position - p).sqrMagnitude <= r2) _near.Add(list[i]);
        return _near;
    }

    void UpdateCyclist()
    {
        if (overrideCyclist)
        {
            HasCyclist = true;
            CyclistPosition = overridePosition;
            CyclistVelocity = overrideVelocity;
            return;
        }
        if (_rider == null && Time.time >= _nextRiderSearch)
        {
            _nextRiderSearch = Time.time + 2f;
            var boot = FindFirstObjectByType<RideBootstrap>();
            if (boot != null) _rider = boot.rider;
            _haveLast = false;
        }
        if (_rider == null || !_rider.gameObject.activeInHierarchy) { HasCyclist = false; return; }
        var p = _rider.position;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        if (_haveLast)
        {
            var v = (p - _lastRider) / dt;
            if (v.sqrMagnitude > 40f * 40f) v = CyclistVelocity;          // teleport / fast travel
            CyclistVelocity = Vector3.Lerp(CyclistVelocity, v, 1f - Mathf.Exp(-dt * 6f));
        }
        _lastRider = p;
        _haveLast = true;
        CyclistPosition = p;
        HasCyclist = true;
    }
}

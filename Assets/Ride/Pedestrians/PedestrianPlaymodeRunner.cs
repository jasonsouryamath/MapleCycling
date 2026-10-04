using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// AMBIENT PEDESTRIANS play-mode proof (copilot, 2026-09-26). Driven by the editor entry
/// <c>PedestrianCapture.Run</c> (after) / <c>PedestrianCapture.RunBefore</c> (director disabled,
/// same camera placements, captures only). Frames: reference/good_graphics/pedestrians/.
///
/// Per map (Maple City, Minato Coast): director discovered the crowd; identical-look neighbours;
/// companions apart; nobody overlapping; lane walkers inside their clear pavement range (never
/// in the road); corridor walkers stay on their surface; no stuck agents; no teleport / pop-in
/// inside the camera frustum near the player; planted-foot slide; a synthetic bicycle ridden
/// straight at a pedestrian makes it yield + step aside + look, then resume; a very close fast
/// pass startles once; idles appear.
/// </summary>
[DefaultExecutionOrder(1000)]
public class PedestrianPlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public bool beforeMode;
    public bool perfOnly;
    public static bool Finished, Failed;
    int _pass, _fail;
    RideBootstrap _boot;
    Camera _cam;
    KuroFollowCamera _follow;
    string _map = "";

    void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[peds-play] PASS " : "[peds-play] FAIL ") + _map + ": " + what);
    }

    void Info(string what) => Debug.Log("[peds-play] INFO " + _map + ": " + what);

    IEnumerator Start()
    {
        Finished = Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Debug.LogError("[peds-play] no RideBootstrap"); Failed = Finished = true; yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        _follow = _cam != null ? _cam.GetComponent<KuroFollowCamera>() : null;
        _boot.devices.acceptKeyboardEffort = false;
        yield return null;

        yield return Map("maple", RegionCatalog.MapleCity, RegionCatalog.Find(RegionCatalog.MapleCity).BuiltCourseId,
                         new[] { 900f, 1600f, 2500f });
        yield return Map("minato", RegionCatalog.MinatoCoast, "minato_crossing", new[] { 420f, 900f, 1500f });

        Debug.Log($"[peds-play] RESULT {_pass}/{_pass + _fail} checks passed{(beforeMode ? " (BEFORE mode: captures only)" : "")}");
        Failed = _fail > 0;
        Finished = true;
    }

    IEnumerator Map(string label, string region, string course, float[] stations)
    {
        _map = label;
        if (_boot.regions == null || !_boot.regions.FastTravel(region))
            Debug.LogWarning($"[peds-play] could not fast travel to {region}");
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse(course);
        yield return Seek(stations[0], 0f, 30);
        var dir = PedestrianDirector.Instance;
        if (!beforeMode)
        {
            if (dir == null) { Check(false, "PedestrianDirector exists"); yield break; }
            dir.Scan();
        }
        var cityRoot = RegionRoot(label);
        List<PedestrianBrain> brains = !beforeMode
            ? dir.Brains.Where(b => b != null && b.gameObject.activeInHierarchy && (cityRoot == null || b.transform.IsChildOf(cityRoot))).ToList()
            : new List<PedestrianBrain>();
        if (!beforeMode)
        {
            int walkers = brains.Count(b => b.kind != PedestrianBrain.PathKind.Spot);
            Check(brains.Count > (label == "maple" ? 200 : 20) && walkers > 10,
                  $"{brains.Count} ambient pedestrians managed ({walkers} walking, " +
                  $"{brains.Count(b => b.kind == PedestrianBrain.PathKind.Spot)} standing/sitting); " +
                  $"director setup {dir.SetupMs:0} ms, clipped corridors {dir.SegmentsClipped}, blocked {dir.SegmentsBlocked}");
            Check(brains.All(b => b.look != null && b.look.applied), $"{brains.Count(b => b.look != null && b.look.applied)}/{brains.Count} dressed");
            Variety(brains);
        }

        if (!beforeMode) yield return Perf(brains, dir);
        if (perfOnly) yield break;
        // ---- captures at gameplay distance (chase cam) + fixed street views
        for (int i = 0; i < stations.Length; i++)
        {
            yield return Seek(stations[i], 0.55f, 40);
            yield return Capture($"{Tag()}{label}_chase_{stations[i]:0000}m");
            yield return StreetView(label, i);
        }
        if (beforeMode) { yield return FootSlide(null); yield break; }

        // ---- simulation checks around the player
        yield return Seek(stations[1], 0.55f, 10);
        yield return Simulate(brains, 8f);
        Companions(brains);

        // ---- foot slide
        yield return FootSlide(brains);

        // ---- foot contact: the lowest rendered sole must be on the rendered walking surface
        yield return FootContact(brains);

        // ---- the bicycle: yield / step aside / look / resume, then a startle
        yield return YieldTest(brains, dir);

        // ---- idles (forced on a few nearby figures so the frames show them)
        yield return IdleShowcase(brains);

        // ---- wardrobe line-up: one close frame per accessory / persona category
        yield return Lineup(brains);
    }

    IEnumerator Perf(List<PedestrianBrain> brains, PedestrianDirector dir)
    {
        IEnumerator Measure(string what)
        {
            for (int k = 0; k < 10; k++) yield return null;
            float t0 = Time.realtimeSinceStartup;
            for (int k = 0; k < 40; k++) yield return null;
            Info($"perf {what}: {(Time.realtimeSinceStartup - t0) * 1000f / 40f:0.0} ms/frame");
        }
        var all = dir.Brains.Where(b => b != null).ToList();
        yield return Measure("all on");
        dir.enabled = false;
        yield return Measure("director ticks off");
        dir.enabled = true;
        foreach (var b in all) if (b.look != null) b.look.ProbeOffscreen(false);
        yield return Measure("fresh skins updateWhenOffscreen=false");
        foreach (var b in all) if (b.look != null) b.look.ProbeExtras(false);
        yield return Measure("+ accessories/caps hidden");
        foreach (var b in all) if (b.look != null) { b.look.ProbeExtras(true); b.look.ProbeOffscreen(true); }
    }

    string Tag() => beforeMode ? "before_" : "after_";

    Transform RegionRoot(string label)
    {
        if (label == "maple")
        {
            var life = GameObject.Find("Maple City Life");
            return life != null ? life.transform : null;
        }
        var go = GameObject.Find("Minato Coast Environment");
        return go != null ? go.transform : null;
    }

    // ================================================================== static checks

    void Variety(List<PedestrianBrain> brains)
    {
        int ident = 0, baseIdent = 0, pairs = 0;
        var looks = brains.Select(b => b.look).Where(l => l != null).ToList();
        for (int i = 0; i < looks.Count; i++)
            for (int j = i + 1; j < looks.Count; j++)
            {
                if ((looks[i].transform.position - looks[j].transform.position).sqrMagnitude > 4f * 4f) continue;
                pairs++;
                if (looks[i].Signature == looks[j].Signature) ident++;
                if (looks[i].BaseSignature == looks[j].BaseSignature) baseIdent++;
            }
        int distinct = looks.Select(l => l.Signature).Distinct().Count();
        Check(ident == 0, $"identical-look neighbours within 4 m = {ident} of {pairs} close pairs " +
                          $"(same body+outfit+hair, ignoring accessories: {baseIdent}); {distinct} distinct looks");
        Info($"hats {looks.Count(l => l.hat != 0)}, bags {looks.Count(l => l.bag != 0)} " +
             $"(shopping {looks.Count(l => l.bag == PedestrianAppearance.Bag.Shopping)}), seniors " +
             $"{looks.Count(l => l.persona == PedestrianAppearance.Persona.Senior)}, young {looks.Count(l => l.persona == PedestrianAppearance.Persona.Young)}; " +
             $"accessory tris/figure max {looks.Max(l => l.ExtraTriangles)}");
    }

    void Companions(List<PedestrianBrain> brains)
    {
        // measured after the simulation (the builder's raw spawn has some co-located pairs that
        // the brains separate within a second), on pairs the full brain tier is running near the camera
        var cam = _cam.transform.position;
        var pairs = brains.Where(b => b.leader != null && b.tier == 0 && b.leader.tier == 0 &&
                                      (b.transform.position - cam).sqrMagnitude < 60f * 60f).ToList();
        if (pairs.Count == 0) { Info("no companion pairs on this map"); return; }
        float minGap = pairs.Min(b => Vector3.Distance(b.transform.position, b.leader.transform.position));
        Check(minGap > 0.55f, $"{pairs.Count} companion pairs walk side by side, closest pair {minGap:0.00} m apart (was 0.56 m centre-to-centre with overlapping shoulders)");
    }

    // ================================================================== simulation

    IEnumerator Simulate(List<PedestrianBrain> brains, float seconds)
    {
        var cam = _cam.transform;
        var near = brains.Where(b => (b.transform.position - cam.position).sqrMagnitude < 70f * 70f).ToList();
        Info($"{near.Count} pedestrians within 70 m of the camera for the {seconds:0} s simulation");
        var start = near.ToDictionary(b => b, b => b.transform.position);
        var lastPos = near.ToDictionary(b => b, b => b.transform.position);
        var walkTime = near.ToDictionary(b => b, b => 0f);
        var visible = new Dictionary<PedestrianBrain, bool>();
        int overlapSamples = 0, samples = 0, offRange = 0, offSurface = 0, teleports = 0, popIns = 0, jerks = 0;
        var lastFacing = near.ToDictionary(b => b, b => b.Facing);
        var surface0 = near.Where(b => b.kind == PedestrianBrain.PathKind.Segment).ToDictionary(b => b, b => Surface(b.transform.position));
        float t = 0f, nextSample = 0f;
        while (t < seconds)
        {
            _boot.devices.EffortInput = 0.55f;
            yield return null;
            float dt = Time.deltaTime;
            t += dt;
            var planes = GeometryUtility.CalculateFrustumPlanes(_cam);
            foreach (var b in near)
            {
                if (b == null) continue;
                var p = b.transform.position;
                float step = Vector3.Distance(p, lastPos[b]);
                bool inView = (p - cam.position).sqrMagnitude < 45f * 45f &&
                              GeometryUtility.TestPlanesAABB(planes, new Bounds(p + Vector3.up * 0.6f, new Vector3(0.6f, 1.2f, 0.6f)));
                if (inView && step > Mathf.Max(0.35f, 3f * dt)) teleports++;
                bool shown = AnyRendererOn(b);
                if (visible.TryGetValue(b, out bool was) && inView && !was && shown && (p - cam.position).sqrMagnitude < 40f * 40f) popIns++;
                visible[b] = shown;
                if (b.state == PedestrianBrain.State.Walk && b.speed > 0.3f)
                {
                    walkTime[b] += dt;
                    float turn = Vector3.Angle(lastFacing[b], b.Facing) / Mathf.Max(1e-4f, dt);
                    if (turn > 400f) jerks++;
                }
                lastFacing[b] = b.Facing;
                lastPos[b] = p;
            }
            if (t >= nextSample)
            {
                nextSample += 0.5f;
                samples++;
                for (int i = 0; i < near.Count; i++)
                {
                    var a = near[i];
                    if (a.kind == PedestrianBrain.PathKind.Spot) continue;
                    for (int j = 0; j < near.Count; j++)
                    {
                        if (i == j) continue;
                        var o = near[j];
                        if (o.kind != PedestrianBrain.PathKind.Spot && j < i) continue;
                        var d = a.transform.position - o.transform.position; d.y = 0f;
                        if (d.sqrMagnitude < 0.38f * 0.38f)
                        {
                            overlapSamples++;
                            if (overlapSamples <= 6)
                                Info($"overlap {d.magnitude:0.00} m: {a.name}[{a.kind}/{a.state}/lat {a.lateral:0.00}/lead {(a.leader != null)}/fol {(a.follower != null)}] vs " +
                                     $"{o.name}[{o.kind}/{o.state}/lat {o.lateral:0.00}/lead {(o.leader != null)}/fol {(o.follower != null)}] same-lane {(a.walker != null && o.walker != null && a.walker.lanes == o.walker.lanes && a.walker.lane == o.walker.lane)}");
                        }
                    }
                    if (a.kind == PedestrianBrain.PathKind.Lane && a.walker != null)
                    {
                        a.walker.lanes.LateralRange(a.walker.lane, a.s, out float lo, out float hi);
                        float lat = a.dirSign >= 0 ? a.lateral : -a.lateral;
                        if (lat < lo - 0.12f || lat > hi + 0.12f) offRange++;
                    }
                    else if (a.kind == PedestrianBrain.PathKind.Segment && surface0.TryGetValue(a, out var s0) && s0 != "")
                    {
                        var s1 = Surface(a.transform.position);
                        if (Family(s1) != Family(s0)) offSurface++;
                    }
                }
            }
        }
        foreach (var b in near.Where(x => x.kind == PedestrianBrain.PathKind.Segment).Take(8))
            Info($"dump {b.name}: state {b.state} tier {b.tier} s {b.s:0.00}/{b.PathLength:0.00} dir {b.dirSign} speed {b.speed:0.00} pref {b.prefSpeed:0.00} " +
                 $"turns {b.turnarounds} walkT {walkTime[b]:0.0} moved {Vector3.Distance(start[b], b.transform.position):0.00}");
        int stuck = 0;
        foreach (var b in near)
            if (b.kind != PedestrianBrain.PathKind.Spot && walkTime[b] > seconds * 0.6f &&
                Vector3.Distance(start[b], b.transform.position) < 0.5f)
                stuck++;
        Check(overlapSamples <= Mathf.Max(1, near.Count * samples / 200),
              $"pedestrian overlaps (< 0.38 m apart) = {overlapSamples} over {samples} samples x {near.Count} figures");
        if (_map == "maple") Check(offRange == 0, $"lane walkers outside their clear pavement range (i.e. toward the road/clutter) = {offRange}");
        else Check(offSurface == 0, $"corridor walkers that left their surface (pavement/sand) = {offSurface}");
        Check(stuck == 0, $"stuck walkers = {stuck}");
        Check(teleports == 0, $"teleports inside the camera frustum = {teleports}");
        Check(popIns == 0, $"pop-ins inside the frustum within 40 m = {popIns}");
        Check(jerks == 0, $"sudden (>400 deg/s) facing snaps while walking = {jerks}");
        int yields = near.Sum(b => b.yields), startles = near.Sum(b => b.startles), stops = near.Sum(b => b.stops);
        Info($"during the ride: {stops} idle stops, {yields} yields, {startles} startles, " +
             $"{near.Sum(b => b.turnarounds)} smooth turnarounds (nobody should be flinching constantly)");
        Check(startles <= Mathf.Max(2, near.Count / 20), $"startles while simply riding the road = {startles}");
    }

    static bool AnyRendererOn(PedestrianBrain b)
    {
        foreach (var r in b.GetComponentsInChildren<Renderer>(false))
            if (r.enabled && r.isVisible) return true;
        return false;
    }

    static string Surface(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity; string n = "";
        foreach (var h in hits)
            if (h.collider != null && h.collider.GetComponentInParent<MinatoCrowdActor>() == null && h.point.y > best)
            { best = h.point.y; n = h.collider.name; }
        return n;
    }

    static string Family(string n)
    {
        if (n.StartsWith("Connected Port")) return "pave";
        if (n.IndexOf("Beach Sand", System.StringComparison.OrdinalIgnoreCase) >= 0) return "sand";
        if (n.StartsWith("Terrain_Chunk")) return "terrain";
        return n;
    }

    // ================================================================== foot slide

    IEnumerator FootSlide(List<PedestrianBrain> brains)
    {
        var cam = _cam.transform.position;
        // AFTER: brain-driven walkers that are actually walking. BEFORE: every walking crowd actor.
        var actors = brains != null
            ? brains.Where(b => b.kind != PedestrianBrain.PathKind.Spot && b.state == PedestrianBrain.State.Walk &&
                                b.speed > 0.4f && (b.transform.position - cam).sqrMagnitude < 60f * 60f)
                    .Select(b => b.actor).Take(20).ToList()
            : FindObjectsByType<MinatoCrowdActor>(FindObjectsSortMode.None)
                    .Where(a => a.motion == MinatoCrowdActor.MotionKind.Walk && a.GetComponent<MapleCityRunner>() == null &&
                                (a.transform.position - cam).sqrMagnitude < 60f * 60f)
                    .Take(20).ToList();
        if (actors.Count == 0) { Info("no walkers near the camera for the foot-slide metric"); yield break; }
        var feet = actors.Select(a => (a, l: a.Bone("LeftFoot"), r: a.Bone("RightFoot"))).Where(f => f.l != null && f.r != null).ToList();
        var subjects = feet.Select(f => f.a).ToList();
        var prevL = feet.Select(f => f.l.position).ToArray();
        var prevR = feet.Select(f => f.r.position).ToArray();
        var prevRoot = subjects.Select(b => b.transform.position).ToArray();
        double slide = 0, travel = 0;
        for (int f = 0; f < 90; f++)
        {
            yield return null;
            for (int k = 0; k < feet.Count; k++)
            {
                var (a, l, r) = feet[k];
                var b = a.GetComponent<PedestrianBrain>();
                bool walking = b == null || (b.state == PedestrianBrain.State.Walk && b.speed >= 0.4f);
                if (!walking || a.AnimatedFrame != Time.frameCount) { prevL[k] = l.position; prevR[k] = r.position; prevRoot[k] = a.transform.position; continue; }
                var planted = l.position.y < r.position.y ? l : r;
                var prev = planted == l ? prevL[k] : prevR[k];
                var d = planted.position - prev; d.y = 0f;
                var dr = a.transform.position - prevRoot[k]; dr.y = 0f;
                slide += d.magnitude; travel += dr.magnitude;
                prevL[k] = l.position; prevR[k] = r.position; prevRoot[k] = a.transform.position;
            }
        }
        float ratio = travel > 1e-3 ? (float)(slide / travel) : 0f;
        if (beforeMode) { Info($"BASELINE planted-foot slide = {ratio:0.00} x body travel over {subjects.Count} walkers"); yield break; }
        Check(ratio < 0.4f, $"planted-foot slide = {ratio:0.00} x body travel over {subjects.Count} walkers " +
                            $"(1.0 = foot skating with the body; ideal 0)");
    }

    // ================================================================== foot contact

    /// <summary>
    /// QA 2026-09-26 (airborne walker, after_maple_idle_phone): the lowest RENDERED sole of each
    /// sampled walker, measured every frame over more than one gait cycle, against the highest
    /// RENDERED walking surface under it. Independent of the actor's own sole markers/ground ray:
    /// the soles are skinned here from the enabled body skin (bones x bindposes, exactly what the
    /// GPU does) and the surface comes from the rendered pavement meshes (Maple's pavement has no
    /// collider, so a temporary probe collider is laid on it - renderer-less, so the actors' own
    /// ground ray ignores it; collisions with it are disabled).
    /// </summary>
    public const float ContactTolerance = 0.03f;
    const int ProbeLayer = 31;

    sealed class FootSkin
    {
        public int[] idx; public bool[] left; public Vector3[] v; public BoneWeight[] w; public Matrix4x4[] bind; public Transform[] bones;
    }

    static List<FootSkin> FootSkins(MinatoCrowdActor a)
    {
        var list = new List<FootSkin>();
        var lf = a.Bone("LeftFoot"); var rf = a.Bone("RightFoot");
        if (lf == null || rf == null) return list;
        foreach (var smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (!smr.enabled || smr.forceRenderingOff || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
            var bones = smr.bones;
            var foot = new bool[bones.Length];
            var isLeft = new bool[bones.Length];
            bool any = false;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && (bones[i] == lf || bones[i] == rf || bones[i].IsChildOf(lf) || bones[i].IsChildOf(rf)))
                { foot[i] = true; any = true; isLeft[i] = bones[i] == lf || bones[i].IsChildOf(lf); }
            if (!any) continue;
            var mesh = smr.sharedMesh;
            var w = mesh.boneWeights;
            var v = mesh.vertices;
            if (w.Length != v.Length) continue;
            var idx = new List<int>(); var side = new List<bool>();
            float W(int bi, float wt) => bi >= 0 && bi < foot.Length && foot[bi] ? wt : 0f;
            float L(int bi, float wt) => bi >= 0 && bi < foot.Length && foot[bi] && isLeft[bi] ? wt : 0f;
            for (int i = 0; i < w.Length; i++)
            {
                var b = w[i];
                float all = W(b.boneIndex0, b.weight0) + W(b.boneIndex1, b.weight1) + W(b.boneIndex2, b.weight2) + W(b.boneIndex3, b.weight3);
                if (all < 0.2f) continue;
                idx.Add(i);
                side.Add(L(b.boneIndex0, b.weight0) + L(b.boneIndex1, b.weight1) + L(b.boneIndex2, b.weight2) + L(b.boneIndex3, b.weight3) >= all * 0.5f);
            }
            if (idx.Count > 0) list.Add(new FootSkin { idx = idx.ToArray(), left = side.ToArray(), v = v, w = w, bind = mesh.bindposes, bones = bones });
        }
        return list;
    }

    static Vector3 LowestSole(List<FootSkin> skins) => LowestSole(skins, Vector3.forward, out _, out _, out _);

    /// <summary>Sole pitch (deg, + = toe down) of the lower foot from its heel / toe heights.</summary>
    static float SolePitch(float heelY, float toeY, float len) => len > 1e-3f ? Mathf.Atan2(heelY - toeY, len * 0.75f) * Mathf.Rad2Deg : 0f;
    public const float FlatPitchDeg = 8f;

    /// <summary>
    /// Lowest skinned sole point, plus the lowest heel and toe heights (rear / front quarter of
    /// that foot's vertices along <paramref name="fwd"/>) of the foot that holds it.
    /// </summary>
    static Vector3 LowestSole(List<FootSkin> skins, Vector3 fwd, out float heelY, out float toeY, out float footLen)
    {
        var best = new Vector3(0f, float.PositiveInfinity, 0f);
        var pts = new List<(Vector3 p, bool left)>();
        foreach (var s in skins)
        {
            var m = new Matrix4x4[s.bones.Length];
            for (int i = 0; i < m.Length; i++) m[i] = s.bones[i] != null && i < s.bind.Length ? s.bones[i].localToWorldMatrix * s.bind[i] : Matrix4x4.zero;
            for (int j = 0; j < s.idx.Length; j++)
            {
                int k = s.idx[j];
                var b = s.w[k]; var v = s.v[k];
                Vector3 p = m[b.boneIndex0].MultiplyPoint3x4(v) * b.weight0 + m[b.boneIndex1].MultiplyPoint3x4(v) * b.weight1 +
                            m[b.boneIndex2].MultiplyPoint3x4(v) * b.weight2 + m[b.boneIndex3].MultiplyPoint3x4(v) * b.weight3;
                float tot = b.weight0 + b.weight1 + b.weight2 + b.weight3;
                if (tot > 1e-4f) p /= tot;
                pts.Add((p, s.left[j]));
            }
        }
        bool lowLeft = true;
        foreach (var (p, l) in pts) if (p.y < best.y) { best = p; lowLeft = l; }
        heelY = toeY = best.y; footLen = 0f;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var (p, l) in pts) if (l == lowLeft) { float f = Vector3.Dot(p, fwd); lo = Mathf.Min(lo, f); hi = Mathf.Max(hi, f); }
        if (hi > lo)
        {
            float q = (hi - lo) * 0.25f;
            footLen = hi - lo;
            heelY = toeY = float.PositiveInfinity;
            foreach (var (p, l) in pts)
            {
                if (l != lowLeft) continue;
                float f = Vector3.Dot(p, fwd);
                if (f <= lo + q) heelY = Mathf.Min(heelY, p.y);
                if (f >= hi - q) toeY = Mathf.Min(toeY, p.y);
            }
        }
        return best;
    }

    /// <summary>Highest rendered surface (enabled MeshRenderer collider, or a pavement probe) under p.</summary>
    static float RenderedGround(Vector3 p, float fromY, out string what)
    {
        // back faces too: several Maple walking meshes are wound downward (drawn double-sided)
        bool bf = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        var hits = Physics.RaycastAll(new Vector3(p.x, fromY, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = bf;
        float best = float.NegativeInfinity; what = "";
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.GetComponentInParent<MinatoCrowdActor>() != null) continue;
            bool probe = h.collider.gameObject.layer == ProbeLayer && h.collider.name.StartsWith("~ContactProbe");
            if (!probe)
            {
                var mr = h.collider.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
            }
            if (h.point.y > best) { best = h.point.y; what = probe ? h.collider.name.Substring(14) : h.collider.name; }
        }
        return best;
    }

    /// <summary>
    /// Renderer-less probe colliders on every collider-less static mesh around the subjects
    /// (Maple's pavement/ground meshes have no colliders), whatever the mesh is called.
    /// </summary>
    List<GameObject> AddPavementProbes(List<PedestrianBrain> subjects, List<Transform> movers)
    {
        var made = new List<GameObject>();
        var boxes = subjects.Select(b => new Bounds(b.transform.position, new Vector3(14f, 2.4f, 14f)))
                            .Concat(movers.Select(m => new Bounds(m.position, new Vector3(20f, 2.4f, 20f)))).ToList();
        var seen = new HashSet<string>();
        foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!mr.enabled || mr.forceRenderingOff || mr.GetComponent<Collider>() != null) continue;
            var bb = mr.bounds;
            if (!boxes.Any(x => x.Intersects(bb))) continue;
            if (mr.GetComponentInParent<MinatoCrowdActor>() != null || mr.GetComponentInParent<PedestrianBrain>() != null) continue;
            if (mr.GetComponentInParent<Camera>() != null) continue;
            if (!OnLod0(mr)) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var go = new GameObject("~ContactProbe " + mr.name) { layer = ProbeLayer };
            go.transform.SetPositionAndRotation(mr.transform.position, mr.transform.rotation);
            go.transform.localScale = mr.transform.lossyScale;
            go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            made.Add(go);
            seen.Add(mr.name);
        }
        for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(ProbeLayer, i, true);
        Physics.SyncTransforms();
        Info($"foot-contact probes on {made.Count} collider-less meshes near the subjects: {string.Join(", ", seen.Take(24))}");
        return made;
    }

    /// <summary>False for a renderer that belongs to a LODGroup level other than LOD0 (not what is drawn up close).</summary>
    static bool OnLod0(Renderer r)
    {
        var g = r.GetComponentInParent<LODGroup>();
        if (g == null) return true;
        var lods = g.GetLODs();
        for (int i = 1; i < lods.Length; i++)
            if (lods[i].renderers != null && System.Array.IndexOf(lods[i].renderers, r) >= 0) return false;
        return true;
    }

    IEnumerator FootContact(List<PedestrianBrain> brains)
    {
        var cam = _cam.transform.position;
        var subjects = brains.Where(b => b.kind != PedestrianBrain.PathKind.Spot && b.state == PedestrianBrain.State.Walk &&
                                         b.speed > 0.4f && b.actor != null && (b.transform.position - cam).sqrMagnitude < 50f * 50f &&
                                         (b.kind != PedestrianBrain.PathKind.Segment || b.RemainingOnPath > 2.5f))
                             .OrderBy(b => (b.transform.position - cam).sqrMagnitude).Take(16).ToList();
        // joggers (MapleCityRunner, Maple City Life - not driven by the pedestrian brain): measured for the
        // report only; a run has a flight phase, so they are not held to the walking tolerance
        var runners = FindObjectsByType<MapleCityRunner>(FindObjectsSortMode.None)
                          .Where(r => r.Ready && (r.transform.position - cam).sqrMagnitude < 40f * 40f && r.GetComponent<MinatoCrowdActor>() != null)
                          .OrderBy(r => (r.transform.position - cam).sqrMagnitude).Take(6).ToList();
        var probes = AddPavementProbes(subjects, runners.Select(r => r.transform).ToList());
        var skins = subjects.Select(b => FootSkins(b.actor)).ToList();
        var rSkins = runners.Select(r => FootSkins(r.GetComponent<MinatoCrowdActor>())).ToList();
        var rLo = runners.Select(_ => float.MaxValue).ToArray(); var rHi = runners.Select(_ => float.MinValue).ToArray();
        var rIn = new int[runners.Count]; var rCnt = new int[runners.Count];
        int n = subjects.Count;
        var sum = new double[n]; var cnt = new int[n]; var lo = new float[n]; var hi = new float[n]; var inTol = new int[n];
        var src = new string[n]; var flat = new int[n];
        float pitchSum = 0f;
        for (int k = 0; k < n; k++) { lo[k] = float.MaxValue; hi[k] = float.MinValue; src[k] = ""; }

        // A/B: the same walkers with the previous foot placement (feet rocking with the thigh swing)
        MinatoCrowdActor.LegacyFootPlacement = true;
        const float LegacySeconds = 1.8f;
        int legacyFrames = 0, legacyFlat = 0, legacyIn = 0;
        float legacyPitch = 0f;
        for (float t = 0f; t < LegacySeconds; t += Time.deltaTime)
        {
            _boot.devices.EffortInput = 0.55f;
            yield return null;
            for (int k = 0; k < n; k++)
            {
                var b = subjects[k];
                if (b == null || skins[k].Count == 0 || b.actor.AnimatedFrame != Time.frameCount) continue;
                if (b.state != PedestrianBrain.State.Walk || b.speed < 0.3f) continue;
                var sole = LowestSole(skins[k], b.transform.forward, out float heelY, out float toeY, out float len);
                if (float.IsInfinity(sole.y)) continue;
                float g = RenderedGround(sole, sole.y + 0.25f, out _);
                if (float.IsNegativeInfinity(g)) continue;
                legacyFrames++;
                legacyPitch += Mathf.Abs(SolePitch(heelY, toeY, len));
                if (Mathf.Abs(sole.y - g) <= ContactTolerance) legacyIn++;
                if (Mathf.Abs(sole.y - g) <= ContactTolerance && Mathf.Abs(SolePitch(heelY, toeY, len)) < FlatPitchDeg) legacyFlat++;
            }
        }
        MinatoCrowdActor.LegacyFootPlacement = false;
        Info($"BEFORE (legacy foot placement): lowest sole within {ContactTolerance * 100f:0} cm on {legacyIn}/{legacyFrames} frames, planted foot flat (sole pitch < {FlatPitchDeg:0} deg) on {legacyFlat}/{legacyFrames}, mean |pitch| {(legacyFrames > 0 ? legacyPitch / legacyFrames : 0f):0.0} deg " +
             $"({(legacyFrames > 0 ? 100f * legacyFlat / legacyFrames : 0f):0}%) - only a heel or toe tip touched the rest of the time");
        for (float t = 0f; t < 2.6f; t += Time.deltaTime)
        {
            _boot.devices.EffortInput = 0.55f;
            yield return null;
            for (int k = 0; k < n; k++)
            {
                var b = subjects[k];
                if (b == null || skins[k].Count == 0 || b.actor.AnimatedFrame != Time.frameCount) continue;
                if (b.state != PedestrianBrain.State.Walk || b.speed < 0.3f) continue;
                var sole = LowestSole(skins[k], b.transform.forward, out float heelY, out float toeY, out float len);
                if (float.IsInfinity(sole.y)) continue;
                float g = RenderedGround(sole, sole.y + 0.25f, out string what);
                if (float.IsNegativeInfinity(g)) continue;
                float c = sole.y - g;
                sum[k] += c; cnt[k]++;
                float pitch = Mathf.Abs(SolePitch(heelY, toeY, len));
                pitchSum += pitch;
                if (Mathf.Abs(c) <= ContactTolerance && pitch < FlatPitchDeg) flat[k]++;
                lo[k] = Mathf.Min(lo[k], c); hi[k] = Mathf.Max(hi[k], c);
                if (Mathf.Abs(c) <= ContactTolerance) inTol[k]++;
                if (src[k] == "") src[k] = what;
            }
            for (int k = 0; k < runners.Count; k++)
            {
                if (runners[k] == null || rSkins[k].Count == 0) continue;
                var sole = LowestSole(rSkins[k]);
                if (float.IsInfinity(sole.y)) continue;
                float g = RenderedGround(sole, sole.y + 0.25f, out _);
                if (float.IsNegativeInfinity(g)) continue;
                float c = sole.y - g;
                rCnt[k]++; rLo[k] = Mathf.Min(rLo[k], c); rHi[k] = Mathf.Max(rHi[k], c);
                if (Mathf.Abs(c) <= ContactTolerance) rIn[k]++;
            }
        }
        for (int k = 0; k < runners.Count; k++)
            if (rCnt[k] > 0)
                Info($"contact jogger {runners[k].name}: lowest sole min {rLo[k] * 100f:0.0} cm, max {rHi[k] * 100f:0.0} cm, " +
                     $"on the ground (<= {ContactTolerance * 100f:0} cm) {rIn[k]}/{rCnt[k]} frames");
        foreach (var p in probes) Destroy(p);
        for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(ProbeLayer, i, false);

        int measured = 0, frames = 0, framesIn = 0, framesFlat = 0;
        float worstMean = 0f;
        for (int k = 0; k < n; k++)
        {
            if (cnt[k] == 0) continue;
            measured++; frames += cnt[k]; framesIn += inTol[k]; framesFlat += flat[k];
            float mean = (float)(sum[k] / cnt[k]);
            if (Mathf.Abs(mean) > Mathf.Abs(worstMean)) worstMean = mean;
            var b = subjects[k];
            Info($"contact {b.name} [{b.kind}{(b.walker != null ? $" lift {b.walker.lift:0.000}" : "")}] lowest sole vs '{src[k]}': " +
                 $"mean {mean * 100f:0.0} cm, min {lo[k] * 100f:0.0}, max {hi[k] * 100f:0.0} over {cnt[k]} frames, planted foot flat {flat[k]}/{cnt[k]}; actor {b.actor.RuntimeContactSummary()}");
        }
        if (measured < 3) { Check(false, $"foot contact: only {measured} walkers could be measured"); yield break; }
        Check(Mathf.Abs(worstMean) <= ContactTolerance,
              $"lowest rendered sole on the pavement: worst per-walker mean {worstMean * 100f:0.0} cm over {measured} walkers (tolerance {ContactTolerance * 100f:0} cm)");
        Check(framesIn >= frames * 0.97f,
              $"lowest rendered sole within {ContactTolerance * 100f:0} cm of the pavement on {framesIn}/{frames} sampled frames " +
              $"(worst frame {MaxAbs(lo, hi, cnt) * 100f:0.0} cm)");
        // planted = the lower shoe is on the pavement AND flat (sole pitch under FlatPitchDeg), not
        // balanced on a heel or toe tip; a natural walk has the stance foot flat for most of stance
        Check(framesFlat >= frames * 0.7f,
              $"planted foot flat on the pavement (on it, sole pitch < {FlatPitchDeg:0} deg) on {framesFlat}/{frames} sampled frames " +
              $"({(frames > 0 ? 100f * framesFlat / frames : 0f):0}%, need 70%), mean |pitch| {(frames > 0 ? pitchSum / frames : 0f):0.0} deg");

        // a close, low side view of one walker mid-stride (visual proof of the planted foot)
        var subject = subjects.FirstOrDefault(b => b != null && b.state == PedestrianBrain.State.Walk && b.speed > 0.4f);
        if (subject != null)
        {
            if (_follow != null) _follow.enabled = false;
            for (float t = 0f; t < 0.7f; t += Time.deltaTime)
            {
                var p = subject.transform.position;
                _cam.transform.position = p + subject.transform.right * 2.3f + subject.transform.forward * 0.4f + Vector3.up * 0.55f;
                _cam.transform.LookAt(p + Vector3.up * 0.4f);
                yield return null;
            }
            yield return CaptureNowCo($"after_{_map}_walk_contact");
            if (_follow != null) _follow.enabled = true;
        }
    }

    static float MaxAbs(float[] lo, float[] hi, int[] cnt)
    {
        float m = 0f;
        for (int k = 0; k < lo.Length; k++)
            if (cnt[k] > 0) { if (Mathf.Abs(lo[k]) > Mathf.Abs(m)) m = lo[k]; if (Mathf.Abs(hi[k]) > Mathf.Abs(m)) m = hi[k]; }
        return m;
    }

    // ================================================================== bicycle

    IEnumerator YieldTest(List<PedestrianBrain> brains, PedestrianDirector dir)
    {
        var cam = _cam.transform.position;
        PedestrianBrain subject = null;
        for (float w = 0f; w < 8f && subject == null; w += Time.deltaTime)
        {
            subject = brains.Where(b => b.kind != PedestrianBrain.PathKind.Spot && b.leader == null && b.follower == null &&
                                        b.tier == 0 && b.state == PedestrianBrain.State.Walk && b.speed > 0.4f &&
                                        (b.kind != PedestrianBrain.PathKind.Segment || b.RemainingOnPath > 3f) &&
                                        (b.transform.position - cam).sqrMagnitude < 80f * 80f)
                            .OrderBy(b => (b.transform.position - cam).sqrMagnitude).FirstOrDefault();
            if (subject == null) yield return null;
        }
        if (subject == null) { Check(false, "a walking subject for the yield test"); yield break; }
        if (_follow != null) _follow.enabled = false;
        int y0 = subject.yields, s0 = subject.startles;
        float lat0 = subject.lateral;
        var fwd = subject.Facing;
        var side = Vector3.Cross(Vector3.up, fwd);
        // start the bike ~2.4 s out (shorter on a short corridor so the walker is still walking when it arrives)
        float lead = subject.kind == PedestrianBrain.PathKind.Segment ? Mathf.Clamp(subject.RemainingOnPath * 3.2f, 7f, 11f) : 11f;
        var bike = subject.transform.position + fwd * lead + side * 0.15f;
        var vel = -fwd * 4.5f;
        dir.overrideCyclist = true;
        float maxYaw = 0f, minSpeed = subject.speed, maxLat = 0f;
        int frame = 0;
        for (float t = 0f; t < 4.2f; t += Time.deltaTime)
        {
            bike += vel * Time.deltaTime;
            dir.overridePosition = bike; dir.overrideVelocity = vel;
            _cam.transform.position = subject.transform.position + side * 3.6f + fwd * 1.2f + Vector3.up * 1.5f;
            _cam.transform.LookAt(subject.transform.position + fwd * 1.5f + Vector3.up * 0.7f);
            yield return null;
            maxYaw = Mathf.Max(maxYaw, Mathf.Abs(subject.HeadYawTowardCyclist));
            minSpeed = Mathf.Min(minSpeed, subject.speed);
            maxLat = Mathf.Max(maxLat, Mathf.Abs(subject.lateral - lat0));
            if (frame++ % 22 == 11) yield return CaptureNowCo($"after_{_map}_yield_{frame / 22:0}");
        }
        dir.overrideCyclist = false;
        bool yielded = subject.yields > y0;
        Check(yielded && minSpeed < 0.15f, $"pedestrian yields to a bicycle on its line (yields +{subject.yields - y0}, slowed to {minSpeed:0.00} m/s)");
        Check(maxLat > 0.2f, $"pedestrian steps aside {maxLat:0.00} m");
        Check(maxYaw > 12f, $"pedestrian turns its head toward the bicycle (peak {maxYaw:0} deg)");
        for (float t = 0f; t < 5f && subject.state != PedestrianBrain.State.Walk; t += Time.deltaTime) yield return null;
        // resumed = walking at > 0.2 m/s at some point in the next 2 s (a legitimate random idle stop
        // may follow the resume inside that window; it used to fail the check intermittently)
        float resumedAt = -1f;
        for (float t = 0f; t < 2f; t += Time.deltaTime)
        {
            if (resumedAt < 0f && subject.state == PedestrianBrain.State.Walk && subject.speed > 0.2f) resumedAt = t;
            yield return null;
        }
        Check(resumedAt >= 0f, $"pedestrian resumes walking after the bike passes (walking at {resumedAt:0.0} s; now {subject.state}, {subject.speed:0.00} m/s)");

        // close fast pass from behind -> one brief startle, not a jump
        var sub2 = brains.Where(b => b != subject && b.kind != PedestrianBrain.PathKind.Spot && b.tier == 0 && b.state == PedestrianBrain.State.Walk && b.speed > 0.4f &&
                                     b.leader == null && b.follower == null && (b.kind != PedestrianBrain.PathKind.Segment || b.RemainingOnPath > 4f) &&
                                     Time.time - b.lastStartleTime > 10f && Time.time - b.lastYieldTime > 4f &&
                                     Vector3.Distance(b.transform.position, subject.transform.position) > 6f)
                         .OrderBy(b => (b.transform.position - cam).sqrMagnitude).FirstOrDefault();
        if (sub2 != null)
        {
            int st0 = sub2.startles;
            var f2 = sub2.Facing; var s2 = Vector3.Cross(Vector3.up, f2);
            var bk = sub2.transform.position - f2 * 9f + s2 * 0.6f;
            var v2 = f2 * 8f;
            dir.overrideCyclist = true;
            // settle the camera on the subject first (its tier follows the camera)
            for (int k = 0; k < 5; k++)
            {
                _cam.transform.position = sub2.transform.position - s2 * 3.8f + f2 * 1.6f + Vector3.up * 1.4f;
                yield return null;
            }
            bk = sub2.transform.position - f2 * 9f + s2 * 0.45f;
            float maxMove = 0f; var prev = sub2.transform.position; float win = 0f;
            for (float t = 0f; t < 2.6f; t += Time.deltaTime)
            {
                bk += v2 * Time.deltaTime;
                dir.overridePosition = bk; dir.overrideVelocity = v2;
                _cam.transform.position = sub2.transform.position - s2 * 3.8f + f2 * 1.6f + Vector3.up * 1.4f;
                _cam.transform.LookAt(sub2.transform.position + Vector3.up * 0.7f);
                yield return null;
                // horizontal ground speed over 0.2 s windows (a jump would still show; per-frame
                // jitter from sub-millisecond batchmode frames would not)
                win += Time.deltaTime;
                if (win >= 0.2f)
                {
                    var dd = sub2.transform.position - prev; dd.y = 0f;
                    maxMove = Mathf.Max(maxMove, dd.magnitude / win);
                    prev = sub2.transform.position; win = 0f;
                }
                if (Time.time - sub2.lastStartleTime < 0.3f && Time.time - sub2.lastStartleTime > 0.2f)
                    yield return CaptureNowCo($"after_{_map}_startle");
            }
            dir.overrideCyclist = false;
            Check(sub2.startles - st0 == 1, $"a close fast pass startles once (startles +{sub2.startles - st0})");
            Check(maxMove < 1.6f, $"startle is a small step, not a jump (peak {maxMove:0.00} m/s)");
        }
        if (_follow != null) _follow.enabled = true;
    }

    // ================================================================== idles

    IEnumerator IdleShowcase(List<PedestrianBrain> brains)
    {
        var cam = _cam.transform.position;
        var idles = new[] { PedestrianBrain.Idle.Phone, PedestrianBrain.Idle.Photo, PedestrianBrain.Idle.Map, PedestrianBrain.Idle.Stretch, PedestrianBrain.Idle.LookAround };
        var pool = brains.Where(b => b.kind != PedestrianBrain.PathKind.Spot && b.leader == null && b.follower == null)
                         .OrderBy(b => (b.transform.position - cam).sqrMagnitude).Take(idles.Length * 3).ToList();
        if (_follow != null) _follow.enabled = false;
        int shown = 0;
        var propSubjects = new List<PedestrianBrain>();
        for (int i = 0; i < idles.Length && i * 3 < pool.Count; i++)
        {
            var b = pool[i * 3];
            b.ForceIdle(idles[i], 6f);
            // frame the idle from whichever front-quarter side is not blocked by street furniture
            var side = ClearSide(b, null);
            var occluders = Occluders(b);
            for (float t = 0f; t < 2.2f; t += Time.deltaTime)
            {
                side = ClearSide(b, occluders, side);
                var f = b.transform.forward;
                _cam.transform.position = b.transform.position + f * 2.4f + b.transform.right * (1.3f * side) + Vector3.up * 1.1f;
                _cam.transform.LookAt(b.transform.position + Vector3.up * 0.65f);
                yield return null;
            }
            if (b.idle == idles[i]) shown++;
            var wantProp = idles[i] == PedestrianBrain.Idle.Phone ? PedestrianAppearance.Prop.Phone
                         : idles[i] == PedestrianBrain.Idle.Photo ? PedestrianAppearance.Prop.Camera
                         : idles[i] == PedestrianBrain.Idle.Map ? PedestrianAppearance.Prop.Map : PedestrianAppearance.Prop.None;
            if (wantProp != PedestrianAppearance.Prop.None && b.look != null)
            {
                propSubjects.Add(b);
                var hand = b.actor != null ? b.actor.Bone("RightHand") : null;
                float reach = hand != null && b.look.PropTransform != null ? Vector3.Distance(hand.position, b.look.PropTransform.position) : 99f;
                float S = b.transform.lossyScale.y;
                Check(b.look.PropKind == wantProp && reach < 0.30f * S,
                      $"{idles[i]} idle shows a handheld {wantProp} (visible {b.look.PropKind}, {reach * 100f:0} cm from the right hand)");
            }
            yield return CaptureNowCo($"after_{_map}_idle_{idles[i].ToString().ToLowerInvariant()}");
        }
        Check(shown == Mathf.Min(idles.Length, (pool.Count + 2) / 3), $"{shown} idle actions play on demand");
        // the props go away with the idle
        for (float t = 0f; t < 1.5f; t += Time.deltaTime) yield return null;
        int still = propSubjects.Count(b => b != null && b.look.PropVisible && b.idle == PedestrianBrain.Idle.None);
        Check(propSubjects.Count > 0 && still == 0, $"handheld props hidden once their idle ends ({still} of {propSubjects.Count} still showing)");
        if (_follow != null) _follow.enabled = true;
    }

    /// <summary>Small static renderers within 6 m of a showcase subject (street furniture that can block the view).</summary>
    static List<Bounds> Occluders(PedestrianBrain b)
    {
        var p = b.transform.position;
        var list = new List<Bounds>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r.transform.IsChildOf(b.transform)) continue;
            var bb = r.bounds;
            if (bb.size.magnitude > 8f || bb.size.y < 0.08f || (bb.center - p).sqrMagnitude > 36f) continue;
            list.Add(bb);
        }
        return list;
    }

    /// <summary>+1 = camera on the subject's front-right, -1 = front-left; keeps the current side while it is clear.</summary>
    float ClearSide(PedestrianBrain b, List<Bounds> occ, float current = 1f)
    {
        if (occ == null || occ.Count == 0) return current;
        bool Clear(float s)
        {
            var eye = b.transform.position + b.transform.forward * 2.4f + b.transform.right * (1.3f * s) + Vector3.up * 1.1f;
            var target = b.transform.position + Vector3.up * 0.65f;
            var ray = new Ray(eye, (target - eye).normalized);
            float len = Vector3.Distance(eye, target) - 0.45f;
            foreach (var bb in occ)
                if (bb.IntersectRay(ray, out float d) && d < len) return false;
            return true;
        }
        if (Clear(current)) return current;
        if (Clear(-current)) return -current;
        return current;
    }

    IEnumerator Lineup(List<PedestrianBrain> brains)
    {
        var cam = _cam.transform.position;
        var cats = new (string name, System.Func<PedestrianAppearance, bool> pick)[]
        {
            ("cap", l => l.hat == PedestrianAppearance.Hat.Cap),
            ("bucket", l => l.hat == PedestrianAppearance.Hat.Bucket),
            ("beanie", l => l.hat == PedestrianAppearance.Hat.Beanie),
            ("backpack", l => l.bag == PedestrianAppearance.Bag.Backpack),
            ("shoulderbag", l => l.bag == PedestrianAppearance.Bag.Shoulder),
            ("shopping", l => l.bag == PedestrianAppearance.Bag.Shopping),
            ("senior", l => l.persona == PedestrianAppearance.Persona.Senior),
            ("plain", l => l.hat == 0 && l.bag == 0 && l.persona == PedestrianAppearance.Persona.Adult),
        };
        if (_follow != null) _follow.enabled = false;
        var used = new HashSet<PedestrianBrain>();
        foreach (var (name, pick) in cats)
        {
            var b = brains.Where(x => !used.Contains(x) && x.look != null && pick(x.look) && x.kind != PedestrianBrain.PathKind.Spot &&
                                      x.leader == null && x.follower == null)
                          .OrderBy(x => (x.transform.position - cam).sqrMagnitude).FirstOrDefault();
            if (b == null) continue;
            used.Add(b);
            b.ForceIdle(PedestrianBrain.Idle.Wait, 5f);
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                var f = b.transform.forward;
                _cam.transform.position = b.transform.position + f * 1.9f + b.transform.right * 0.9f + Vector3.up * 1.0f;
                _cam.transform.LookAt(b.transform.position + Vector3.up * 0.62f);
                yield return null;
            }
            yield return CaptureNowCo($"after_{_map}_look_{name}_front");
            for (int k = 0; k < 3; k++)
            {
                var f = b.transform.forward;
                _cam.transform.position = b.transform.position - f * 1.8f - b.transform.right * 1.0f + Vector3.up * 1.05f;
                _cam.transform.LookAt(b.transform.position + Vector3.up * 0.62f);
                yield return null;
            }
            yield return CaptureNowCo($"after_{_map}_look_{name}_back");
        }
        if (_follow != null) _follow.enabled = true;
    }

    // ================================================================== views

    IEnumerator StreetView(string label, int i)
    {
        if (_follow != null) _follow.enabled = false;
        var rider = _follow != null && _follow.target != null ? _follow.target.position : _cam.transform.position;
        Vector3 eye, look;
        if (label == "maple")
        {
            var lanes = FindObjectsByType<MapleCityWalkLanes>(FindObjectsSortMode.None).FirstOrDefault();
            if (lanes == null) { if (_follow != null) _follow.enabled = true; yield break; }
            int lane = i % 2 == 0 ? 1 : 3;
            if (!lanes.Valid(lane)) lane = 0;
            float bestS = 0f, best = float.MaxValue;
            var L = lanes.lanes[lane];
            for (int k = 0; k < L.points.Length; k++)
            {
                float d = (L.points[k] - rider).sqrMagnitude;
                if (d < best) { best = d; bestS = L.dist[k]; }
            }
            var p = lanes.Sample(lane, bestS, out var dir);
            eye = p - dir * 4.2f + Vector3.up * 1.35f;
            look = p + dir * 5f + Vector3.up * 0.75f;
        }
        else
        {
            var root = RegionRoot(label);
            var crowd = root != null
                ? root.GetComponentsInChildren<MinatoCrowdActor>(false).Where(a => a.motion != MinatoCrowdActor.MotionKind.Cyclist &&
                      a.transform.parent != null && (a.transform.parent.name == "Market Shoppers" || a.transform.parent.name == "Waterfront Pedestrians" || a.transform.parent.name == "City Beach"))
                      .OrderBy(a => a.name).ToList()
                : new List<MinatoCrowdActor>();
            if (crowd.Count == 0) { if (_follow != null) _follow.enabled = true; yield break; }
            var a0 = crowd[(i * 7919) % crowd.Count];
            var p = a0.pathStart;
            var fwd = (a0.pathEnd - a0.pathStart); fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, fwd);
            eye = p + side * 4f - fwd * 2.5f + Vector3.up * 1.45f;
            look = p + fwd * 2f + Vector3.up * 0.6f;
        }
        for (int f = 0; f < 6; f++)
        {
            _cam.transform.position = eye;
            _cam.transform.LookAt(look);
            yield return null;
        }
        yield return CaptureNowCo($"{Tag()}{label}_street_{i}");
        if (_follow != null) _follow.enabled = true;
    }

    IEnumerator Seek(float d, float effort, int frames)
    {
        _boot.session.SeekTo(d);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < frames; f++) { _boot.devices.EffortInput = effort; yield return null; }
        SnapCamera();
    }

    void SnapCamera()
    {
        if (_follow == null || _follow.target == null || !_follow.enabled) return;
        var t = _follow.target;
        _cam.transform.position = t.position + t.TransformDirection(_follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    // Render at END of frame: the brains' acting/head-look overlays are applied in LateUpdate,
    // after coroutines run, so an immediate render would miss them.
    // WaitForEndOfFrame never resumes in batchmode editor play, so captures are queued and taken
    // in this runner's LateUpdate (execution order 1000, after the director's LateUpdate at -30).
    IEnumerator Capture(string name) { yield return null; yield return CaptureNowCo(name); }
    IEnumerator CaptureNowCo(string name)
    {
        _pendingCapture = name;
        while (_pendingCapture != null) yield return null;
    }
    string _pendingCapture;
    void LateUpdate()
    {
        if (_pendingCapture == null) return;
        var n = _pendingCapture;
        try { CaptureNow(n); } finally { _pendingCapture = null; }
    }

    void CaptureNow(string name)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);
        Debug.Log($"[peds-play] frame {name}");
    }
}

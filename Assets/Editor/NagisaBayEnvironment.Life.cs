using System.Collections.Generic;
using UnityEngine;

// B5 Nagisa Bay, part 4: people on foot + the ambient boats (copilot, 2026-09-27).
//
// PEOPLE follow the Azora / Tuna-port donor approach (N rule 2): clone the Minato "Crowd_*"
// donors already saved in the scene, then civilian-dress each clone with
// MapleCityLife.DressForRegion(go, donor, "NB", seated) - the resort wardrobe written by
// tools/blender/looks/nagisa_looks.py (aloha shirts, linen, sundresses, board shorts, straw sun
// hats). Never MinatoCrowdPopulation.Prepare. Nobody helmeted (the NB looks turn the helmet into
// hair / a sun-hat cap). Nobody on the road: every spot is tested against CorridorKeepOutM
// (+ a margin) and walkers only pace short straight legs on the promenade, both ends and the
// midpoint re-tested.
//
// BOATS: the W0 shared ambient-mover kit (Assets/Ride/Ambient/AmbientPathMover, Loop mode, AmbientCull
// 1.5 km cutoff), paths baked here and validated against the ground field (open water only, far
// from the road and the marina berths).
public static partial class NagisaBayEnvironment
{
    private const float PromWalkOffsetM = 9.9f;       // centre of the promenade paving
    private const float PromWalkLegM = 22f;
    private const float PeopleSpacingM = 34f;
    private const float PersonRoadMarginM = 1.6f;     // beyond CorridorKeepOutM
    private const float TaxiInnerCoastM = -90f;       // water-taxi lane, metres offshore
    private const float TaxiOuterCoastM = -165f;
    private const float BoatRoadClearM = 60f;
    private const float WaterTaxiSpeed = 7.5f;        // m/s (~15 kn) PROVISIONAL
    private const float YachtSpeed = 4.2f;

    private sealed class NbCast
    {
        public readonly List<MinatoCrowdActor> walk = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> stand = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> sit = new List<MinatoCrowdActor>();
    }

    private static int _nbPeople;
    private static readonly Dictionary<string, int> _nbRoleCount = new Dictionary<string, int>();

    private static NbCast NbCrowdCast(Transform root)
    {
        var cast = new NbCast();
        foreach (var a in Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a == null || a.transform.IsChildOf(root)) continue;
            if (!a.name.StartsWith("Crowd_", System.StringComparison.Ordinal)) continue;
            if (a.transform.Find("LOD0 High Skinned/Rigged Character") == null) continue;
            if (a.motion == MinatoCrowdActor.MotionKind.Walk) cast.walk.Add(a);
            else if (a.motion == MinatoCrowdActor.MotionKind.Idle || a.motion == MinatoCrowdActor.MotionKind.Wave) cast.stand.Add(a);
            else if (a.motion == MinatoCrowdActor.MotionKind.Sit && a.transform.Find("Timber Waterfront Bench") != null) cast.sit.Add(a);
        }
        if (cast.stand.Count == 0) cast.stand.AddRange(cast.walk);
        if (cast.walk.Count == 0) cast.walk.AddRange(cast.stand);
        cast.walk.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        cast.stand.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        cast.sit.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        Debug.Log($"[nagisa] crowd donors: {cast.walk.Count} walk, {cast.stand.Count} stand, {cast.sit.Count} sit.");
        return cast;
    }

    private static bool ClearOfRoad(Vector3 p) =>
        _route.PlanDistance(p.x, p.z, out _) > CorridorKeepOutM + PersonRoadMarginM;

    private static GameObject NbPerson(List<MinatoCrowdActor> pool, Transform parent, string role, Vector3 pos,
                                       Vector3 face, MinatoCrowdActor.MotionKind kind, float seed, Vector3? end = null)
    {
        if (pool == null || pool.Count == 0) return null;
        if (!ClearOfRoad(pos) || (end.HasValue && !ClearOfRoad(end.Value))) return null;
        seed = Mathf.Repeat(seed, 1f);
        var src = pool[Mathf.Abs((int)(seed * 9973f) + _nbPeople * 7919) % pool.Count];
        var go = Object.Instantiate(src.gameObject, parent);
        go.name = $"Nagisa {role} {_nbPeople:D3}";
        go.hideFlags = HideFlags.None;
        go.SetActive(true);
        bool seated = kind == MinatoCrowdActor.MotionKind.Sit;
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
            actor.Configure(kind, rig, pos, end ?? pos,
                            kind == MinatoCrowdActor.MotionKind.Walk ? Mathf.Lerp(0.55f, 0.9f, seed) : 0f, seed);
            actor.armDropDegrees = seated ? 0f : 32f;
        }
        string baseName = go.name;
        go.name = (kind == MinatoCrowdActor.MotionKind.Walk ? "Walker_" : seated ? "Customer_" : "Chat_") + baseName;
        try { MapleCityLife.DressForRegion(go, src.name, "NB", seated); }
        catch (System.Exception ex) { Debug.LogWarning($"[nagisa] dress failed for {baseName}: {ex.Message}"); }
        go.name = baseName;
        _nbPeople++;
        _nbRoleCount[role] = _nbRoleCount.TryGetValue(role, out int n) ? n + 1 : 1;
        return go;
    }

    static partial void BuildPeople(Transform root)
    {
        _nbPeople = 0;
        _lastCast = null; _peopleT = null;
        _nbRoleCount.Clear();
        var cast = NbCrowdCast(root);
        if (cast.walk.Count == 0 && cast.stand.Count == 0)
        {
            Debug.LogWarning("[nagisa] no Minato Crowd_ donors in the scene - no people staged.");
            return;
        }
        var group = new GameObject("Nagisa People").transform;
        group.SetParent(root, false);
        var rng = new System.Random(7707);
        float R() => (float)rng.NextDouble();

        // --- promenade people: pass 2 walk legs + bench / food-truck / rental spots (Life2.cs)
        int i0 = _route.IndexAt(_route.BeachStartM + 40f), i1 = _route.IndexAt(_route.BeachEndM - 40f);
        float last = -999f;

        // --- beach-goers on the dry sand (by the umbrellas), looking out to sea or chatting
        last = -999f;
        for (int i = i0; i <= i1; i++)
        {
            float d = _route.Distance[i];
            if (d - last < PeopleSpacingM * 0.8f) continue;
            last = d;
            var p = _route.Position[i];
            var sd = _route.SideFlat(i) * SeaSign(i);
            for (float o = PromenadeOuterM + 8f; o < 170f; o += 2f)
            {
                var q = p + sd * o;
                float c = _ground.Coast(q.x, q.z);
                if (c < 5f) break;
                if (c > 20f) continue;
                if (InAnyPad(q.x, q.z, 3f) || !CanPlace(q.x, q.z, PersonRoadMarginM, out float qy, 4f)) break;
                var side = Vector3.Cross(Vector3.up, sd);
                var at = new Vector3(q.x, qy, q.z) + side * (R() * 6f - 3f);
                at.y = _ground.Height(at.x, at.z);
                if (R() < 0.6f)
                    NbPerson(cast.stand, group, "Beachgoer", at, sd + side * (R() - 0.5f), MinatoCrowdActor.MotionKind.Idle, R());
                else
                {
                    NbPerson(cast.stand, group, "Beachgoer", at, side, MinatoCrowdActor.MotionKind.Idle, R());
                    var at2 = at + side * 1.1f; at2.y = _ground.Height(at2.x, at2.z);
                    NbPerson(cast.stand, group, "Beachgoer", at2, -side, MinatoCrowdActor.MotionKind.Wave, R());
                }
                break;
            }
        }

        // --- marina crew on the quay, and resort guests at the hotel porte-cochere plaza
        StandGroup(cast, group, "marina_quay", "MarinaCrew", 9, rng);
        StandGroup(cast, group, "hotel", "HotelGuest", 6, rng, landwardOnly: true);
        BuildPeople2(cast, group, rng);
        _lastCast = cast; _peopleT = group;

        var summary = new List<string>();
        foreach (var kv in _nbRoleCount) summary.Add($"{kv.Value} {kv.Key}");
        Debug.Log($"[nagisa] people: {_nbPeople} townsfolk in NB looks ({string.Join(", ", summary)}).");
    }

    /// <summary>A few standers on the open part of a pad, clear of buildings' footprints (outer ring).</summary>
    private static void StandGroup(NbCast cast, Transform group, string pad, string role, int count,
                                   System.Random rng, bool landwardOnly = false)
    {
        if (!_route.Pads.TryGetValue(pad, out var pd)) return;
        int made = 0;
        for (int tries = 0; tries < count * 12 && made < count; tries++)
        {
            // outer ring of the pad: 80-100 % of the half extents, where the buildings are not
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float k = 0.85f + (float)rng.NextDouble() * 0.2f;
            var q = pd.c + new Vector3(Mathf.Cos(ang) * pd.h.x * k, 0f, Mathf.Sin(ang) * pd.h.y * k);
            if (landwardOnly && _route.PlanDistance(q.x, q.z, out _) > _route.PlanDistance(pd.c.x, pd.c.z, out _)) continue;
            if (!CanPlace(q.x, q.z, PersonRoadMarginM, out float y, 1.5f)) continue;
            var face = pd.c - q;
            var go = NbPerson(cast.stand, group, role, new Vector3(q.x, y, q.z), face,
                              rng.NextDouble() < 0.3 ? MinatoCrowdActor.MotionKind.Wave : MinatoCrowdActor.MotionKind.Idle,
                              (float)rng.NextDouble());
            if (go != null) made++;
        }
    }

    // ================================================================= boats (the mover)

    static partial void BuildMover(Transform root)
    {
        // W0 shared ambient-mover kit (gemini, 2026-09-27 23:41): one AmbientPathMover per boat in
        // Loop mode; the kit's AmbientCull freezes it beyond 1.5 km. Converged by exact name.
        var group = AmbientMoverStaging.Converge(root, "Nagisa Bay Boats").transform;
        var list = new List<AmbientPathMover>();

        // 1) resort water taxi: marina -> along the beach to the Grand Shiokaze and back
        var outLeg = new List<Vector3>(); var back = new List<Vector3>();
        int a0 = _route.IndexAt(_route.MarinaEndM + 150f), a1 = _route.IndexAt(_route.HotelM + 350f);
        for (int i = a0; i <= a1; i += 12)
        {
            // pairs only where both lanes are open water (the marina end is skipped, so the taxi
            // runs a clean loop off the resort beach instead of threading the berths)
            if (Offshore(i, TaxiInnerCoastM, out var p1) && Offshore(i, TaxiOuterCoastM, out var p2) &&
                OpenWater(p1) && OpenWater(p2)) { outLeg.Add(p1); back.Add(p2); }
        }
        back.Reverse();
        var taxi = new List<Vector3>(outLeg); taxi.AddRange(back);
        var smooth = Smooth(taxi, 3);
        smooth.RemoveAll(p => !OpenWater(p));   // corner-cutting can pull a point inshore; drop it (mids re-checked)
        AddBoat(list, group, "Nagisa_WaterTaxi", smooth, WaterTaxiSpeed, 0.15f, 0.06f, 0.9f);

        // 2) two yachts cruising slow ellipses off the resort beach
        foreach (var (m, off, rx, rz, ph) in new[] { (_route.HotelM - 300f, -520f, 380f, 110f, 0.0f),
                                                    (_route.BeachStartM, -760f, 450f, 140f, 0.55f) })
        {
            int i = _route.IndexAt(m);
            if (!Offshore(i, off, out var c)) continue;
            var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
            var s = Vector3.Cross(Vector3.up, t);
            var loop = new List<Vector3>();
            for (int k = 0; k < 48; k++)
            {
                float a = k / 48f * Mathf.PI * 2f;
                loop.Add(c + t * Mathf.Cos(a) * rx + s * Mathf.Sin(a) * rz);
            }
            AddBoat(list, group, list.Count % 2 == 0 ? "Nagisa_MotorYacht" : "Nagisa_Sailboat", loop,
                    YachtSpeed, ph, 0.10f, 0.7f);
        }
        if (_lastCast != null && _peopleT != null) BuildWaterToys(list, group, _lastCast, _peopleT);
        Debug.Log($"[nagisa] mover: {list.Count} W0 AmbientPathMover boats staged under '{group.name}'.");
    }

    /// <summary>Seaward point at the given (negative) coast distance from route sample i.</summary>
    private static bool Offshore(int i, float coastM, out Vector3 p)
    {
        var o = _route.Position[i]; var sd = _route.SideFlat(i) * SeaSign(i);
        for (float d = 10f; d < 2500f; d += 5f)
        {
            var q = o + sd * d;
            if (_ground.Coast(q.x, q.z) <= coastM) { p = new Vector3(q.x, SeaLevelY, q.z); return true; }
        }
        p = Vector3.zero;
        return false;
    }

    private static List<Vector3> Smooth(List<Vector3> pts, int iterations)
    {
        var cur = pts;
        for (int it = 0; it < iterations && cur.Count >= 3; it++)
        {
            var nx = new List<Vector3>(cur.Count * 2);
            for (int i = 0; i < cur.Count; i++)
            {
                var a = cur[i]; var b = cur[(i + 1) % cur.Count];
                nx.Add(Vector3.Lerp(a, b, 0.25f)); nx.Add(Vector3.Lerp(a, b, 0.75f));
            }
            cur = nx;
        }
        return cur;
    }

    private static bool OpenWater(Vector3 p) =>
        _ground.Height(p.x, p.z) < -2f && _ground.Coast(p.x, p.z) < -60f &&
        _route.PlanDistance(p.x, p.z, out _) > BoatRoadClearM &&
        (!_marinaBerthsSet || new Vector2(p.x - _marinaBerths.x, p.z - _marinaBerths.z).magnitude > MarinaBoatClearM);

    private static void AddBoat(List<AmbientPathMover> list, Transform group, string stem, List<Vector3> path,
                                float speed, float phase, float heave, float tilt)
    {
        if (path == null || path.Count < 8) { Debug.LogWarning($"[nagisa] boat {stem}: path too short ({path?.Count ?? 0})."); return; }
        for (int i = 0; i < path.Count; i++)
        {
            var mid = Vector3.Lerp(path[i], path[(i + 1) % path.Count], 0.5f);
            if (!OpenWater(path[i]) || !OpenWater(mid))
            {
                Debug.LogWarning($"[nagisa] boat {stem}: path point {i} {path[i]:F0} not in open water / too near the road - boat dropped.");
                return;
            }
        }
        var arr = AmbientMoverStaging.BakePolyline(path);
        var holder = AmbientMoverStaging.Converge(group, $"{stem} (mover {list.Count})");
        var go = PlaceWorld(stem, holder.transform, arr[0], 0f);
        if (go == null) return;
        var mover = holder.AddComponent<AmbientPathMover>();
        mover.mode = AmbientPathMover.PathMode.Loop;
        mover.path = arr;
        mover.cars = new[] { go.transform };
        mover.carOffsets = new[] { 0f };
        mover.carYaw = new[] { 0f };
        mover.speed = speed;
        mover.accel = 1.5f;
        mover.bankFactor = 0.2f;
        mover.bobHeave = heave;
        mover.bobTilt = tilt;
        mover.bobFrequency = 0.35f;
        mover.cullDistance = AmbientCull.DefaultCullDistance;   // 1.5 km
        mover.seed = 5900 + list.Count;
        mover.Bake();
        float len = AmbientMoverStaging.GetPolylineLength(arr, closed: true);
        mover.startDistance = phase * len;
        mover.SetHead(mover.startDistance);
        mover.Place();
        UnityEditor.EditorUtility.SetDirty(mover);
        list.Add(mover);
        Debug.Log($"[nagisa] mover {holder.name}: {arr.Length}-point closed loop, {len:F0} m, {speed:F1} m/s, " +
                  $"all points open water and >{BoatRoadClearM:F0} m from the road.");
    }
}

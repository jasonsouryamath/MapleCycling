// Nagisa Bay (B5) PASS 2 - "lively with tourists, vacations, residences": several hundred people.
//
// Same donor rule as pass 1 (N rule 2): clone the Minato Crowd_* donors, dress each with
// MapleCityLife.DressForRegion(go, donor, "NB", seated). Never MinatoCrowdPopulation.Prepare; nobody
// helmeted; nobody inside the corridor (NbPerson re-tests every spot + both walk-leg ends).
//
// Spots come from the builders: Town2 (sidewalk legs, shop fronts, gardens, bus stop, kiosks),
// Beach2 (promenade + boardwalk legs, benches, food trucks, rentals, towels, volleyball, swimmers,
// surfers), Resort (pool deck loungers, cabanas, porte-cochere bellhops + taxi guests). This file adds
// balcony / roof-terrace residents and marina crew by PROBING the building / boat GLBs with temporary
// mesh colliders, and the boat movers (jet skis, paddleboarders, a banana boat) with riders.
//
// Anything a person stands on above the terrain gets a small "Beach Sand walk layer" patch (collider +
// matching material): the shared MinatoCrowdActor ground probe ranks "Beach Sand" colliders above the
// terrain, so at runtime balcony / deck / boat people stay where they were staged.
//
// Attachments (dog, stroller, carried surfboard) are parented to the actor so they follow its walk.
// Distance shadows: every person's LOD1+ renderers are shadowless (LOD0 is the actor's own policy).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- PROVISIONAL tuning
    private const float TownWalkFill = 0.62f, PromWalkFill = 0.9f, SubLegMinM = 16f, SubLegMaxM = 26f;
    private const float JogChance = 0.12f, CoupleChance = 0.2f, FamilyChance = 0.1f, DogChance = 0.1f;
    private const float JogSpeed = 2.3f;
    private const float LoungerSitLift = 0.03f;          // lounger cushion 0.41 vs donor bench seat 0.38
    private const float JetSkiSeatRoot = 0.35f, BananaSeatRoot = 0.30f, SupDeckRoot = 0.12f;
    private const int MaxBalconyPerBuilding = 7;
    private const float BalconyChance = 0.55f;

    private struct PSpot
    {
        public Vector3 p, face; public MK kind; public string role; public float scale;
        public string patchMat; public bool lounger;
    }

    private static readonly List<PSpot> _pSpots = new List<PSpot>();
    private static readonly List<(Vector3 a, Vector3 b, string role)> _pWalks = new List<(Vector3, Vector3, string)>();
    private static NbCast _lastCast;
    private static Transform _peopleT;

    private static PSpot PS(Vector3 p, Vector3 face, string role, MK kind = MK.Idle, float scale = 1f) =>
        new PSpot { p = p, face = face, role = role, kind = kind, scale = scale };

    /// <summary>Pool-deck spot (patch in NB_DeckStone so the ground probe keeps them on the deck).</summary>
    private static PSpot PSd(Vector3 p, Vector3 face, string role, MK kind = MK.Idle, bool lounger = false) =>
        new PSpot { p = p, face = face, role = role, kind = kind, scale = 1f, patchMat = "NB_DeckStone", lounger = lounger };

    // ---------------------------------------------------------------- walk-layer patches
    private static readonly Dictionary<string, (List<Vector3> v, List<Vector2> uv, List<int> t)> _patch =
        new Dictionary<string, (List<Vector3>, List<Vector2>, List<int>)>();

    private static void Patch(string mat, Vector3 at, float half = 0.32f)
    {
        if (!_patch.TryGetValue(mat, out var b)) _patch[mat] = b = (new List<Vector3>(), new List<Vector2>(), new List<int>());
        int i0 = b.v.Count;
        float y = at.y + 0.006f;
        b.v.Add(new Vector3(at.x - half, y, at.z - half)); b.v.Add(new Vector3(at.x + half, y, at.z - half));
        b.v.Add(new Vector3(at.x + half, y, at.z + half)); b.v.Add(new Vector3(at.x - half, y, at.z + half));
        foreach (var q in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) })
            b.uv.Add(new Vector2(at.x / 2.4f, at.z / 2.4f) + q * (2f * half / 2.4f));
        b.t.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
    }

    private static void FlushPatches(Transform parent)
    {
        foreach (var kv in _patch)
        {
            if (kv.Value.t.Count == 0) continue;
            FixWinding(kv.Value.v, kv.Value.t);
            var go = AddMesh(parent, $"Deck Patches {kv.Key.Replace("NB_", "")} - Beach Sand walk layer",
                             Finish($"Nagisa_DeckPatch_{kv.Key}", kv.Value.v, kv.Value.uv, kv.Value.t), _nb[kv.Key], true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        _patch.Clear();
    }

    /// <summary>A small collider child that travels with a vehicle (boat riders).</summary>
    private static void RiderPad(Transform vehicle, float localY, Material mat)
    {
        var v = new List<Vector3> { new Vector3(-0.3f, localY, -0.5f), new Vector3(0.3f, localY, -0.5f),
                                    new Vector3(0.3f, localY, 0.5f), new Vector3(-0.3f, localY, 0.5f) };
        var uv = new List<Vector2> { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        var t = new List<int> { 0, 2, 1, 0, 3, 2 };
        var go = AddMesh(vehicle, "Rider Pad - Beach Sand walk layer", Finish($"Nagisa_RiderPad_{_riderPads++}", v, uv, t), mat, true);
        go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }
    private static int _riderPads;

    // ---------------------------------------------------------------- spawn helpers
    private static GameObject Spawn(NbCast cast, Transform parent, PSpot s, System.Random rng)
    {
        var kind = s.kind;
        if (kind == MK.Sit && cast.sit.Count == 0) kind = MK.Idle;
        if (kind == MK.Sit && s.role == "Sunbather" && !s.lounger) kind = MK.Idle;   // no lying pose: stand by the towel
        var pool = kind == MK.Sit ? cast.sit : kind == MK.Walk ? cast.walk : cast.stand;
        var pos = s.p;
        if (s.lounger) pos += s.face.normalized * 0.18f + Vector3.up * LoungerSitLift;
        var go = NbPerson(pool, parent, s.role, pos, s.face, kind, (float)rng.NextDouble());
        if (go == null) return null;
        if (s.lounger)
        {
            var bench = go.transform.Find("Timber Waterfront Bench");
            if (bench != null) Object.DestroyImmediate(bench.gameObject);
        }
        if (s.scale != 1f) go.transform.localScale *= s.scale;
        if (s.patchMat != null) Patch(s.patchMat, pos);
        FinishPerson(go);
        return go;
    }

    private static GameObject Walker(NbCast cast, Transform parent, string role, Vector3 a, Vector3 b, float seed, float speed = 0f, float scale = 1f)
    {
        var go = NbPerson(cast.walk, parent, role, a, b - a, MK.Walk, seed, b);
        if (go == null) return null;
        if (speed > 0f) { var act = go.GetComponent<MinatoCrowdActor>(); if (act != null) act.moveSpeed = speed; }
        if (scale != 1f) go.transform.localScale *= scale;
        FinishPerson(go);
        return go;
    }

    private static void FinishPerson(GameObject go)
    {
        var g = go.GetComponent<LODGroup>();
        if (g == null) return;
        var lods = g.GetLODs();
        for (int l = 1; l < lods.Length; l++)
            foreach (var r in lods[l].renderers) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void Attach(GameObject person, string stem, Vector3 local, float yaw, Dictionary<string, Material> remap = null)
    {
        if (person == null) return;
        var go = Place(stem, person.transform, local, yaw, 1f, false, SmallPropCull, remap);
        if (go == null) return;
        // the person is scaled (0.95..1.05, kids 0.66): keep the prop at world size
        var ls = person.transform.lossyScale.x;
        if (ls > 1e-3f) go.transform.localScale = Vector3.one / ls;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ================================================================= build

    private static void BuildPeople2(NbCast cast, Transform group, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        _patch.Clear(); _riderPads = 0;
        int before = _nbPeople;

        // ---- recorded stand / sit spots (resort, promenade, beach, town)
        foreach (var s in _pSpots) Spawn(cast, group, s, rng);
        foreach (var s in _standSpots)
            Spawn(cast, group, PS(s.p, s.face, s.role, s.sit ? MK.Sit : (R() < 0.3f ? MK.Wave : MK.Idle)), rng);

        // ---- walk legs: promenade / boardwalk / shoreline / town sidewalks, split into sub-legs
        var legs = new List<(Vector3 a, Vector3 b, string role, float fill)>();
        foreach (var (a, b, role) in _pWalks) legs.Add((a, b, role, role == "Surfer" || role == "Shoreline" ? 1f : PromWalkFill));
        foreach (var w in _walkSpots) legs.Add((w.a, w.b, w.role, TownWalkFill));
        int couples = 0, families = 0, dogs = 0, joggers = 0, surfers = 0;
        foreach (var (a, b, role, fill) in legs)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / Mathf.Lerp(SubLegMinM, SubLegMaxM, R())));
            for (int k = 0; k < n; k++)
            {
                if (R() > fill) continue;
                var p0 = Vector3.Lerp(a, b, k / (float)n); var p1 = Vector3.Lerp(a, b, (k + 1) / (float)n);
                if (Vector3.Distance(p0, p1) < 6f) continue;
                bool fwd = R() < 0.5f;
                var s0 = fwd ? p0 : p1; var s1 = fwd ? p1 : p0;
                var lat = Vector3.Cross(Vector3.up, (s1 - s0).normalized);
                float seed = R();
                if (role == "Surfer")
                {
                    var p = Walker(cast, group, "Surfer", s0, s1, seed);
                    Attach(p, "Nagisa_S_Surfboard", new Vector3(0.34f, 0.12f, 0.05f), 0f);
                    if (p != null) surfers++;
                    continue;
                }
                bool prom = role == "Promenade" || role == "Boardwalk";
                bool res = role == "Resident";
                float roll = R();
                if (prom && roll < JogChance)
                {
                    if (Walker(cast, group, "Jogger", s0, s1, seed, JogSpeed) != null) joggers++;
                }
                else if ((prom || role == "Shopper") && roll < JogChance + CoupleChance)
                {
                    var p = Walker(cast, group, "Couple", s0 - lat * 0.36f, s1 - lat * 0.36f, seed);
                    var q = Walker(cast, group, "Couple", s0 + lat * 0.36f, s1 + lat * 0.36f, seed);
                    if (p != null && q != null) couples++;
                }
                else if ((prom || res) && roll < JogChance + CoupleChance + FamilyChance)
                {
                    var p = Walker(cast, group, "Family", s0 - lat * 0.4f, s1 - lat * 0.4f, seed);
                    Attach(p, "Nagisa_S_Stroller", new Vector3(0f, 0f, 0.95f), 180f, NbCarColour(rng.Next(8)));
                    Walker(cast, group, "FamilyKid", s0 + lat * 0.45f, s1 + lat * 0.45f, seed, 0f, 0.64f);
                    if (p != null) families++;
                }
                else if ((prom || res) && roll < JogChance + CoupleChance + FamilyChance + DogChance)
                {
                    var p = Walker(cast, group, "DogWalker", s0, s1, seed);
                    Attach(p, "Nagisa_S_Dog", new Vector3(0.28f, 0f, 1.25f), 180f);
                    if (p != null) dogs++;
                }
                else Walker(cast, group, prom ? "Stroller" : role, s0, s1, seed);
            }
        }

        int balcony = BalconyResidents(cast, group, rng);
        int crew = MarinaCrew(cast, group, rng);
        var patchT = new GameObject("Walk Layer Patches").transform; patchT.SetParent(group, false);
        FlushPatches(patchT);
        Debug.Log($"[nagisa] people pass 2: {_nbPeople - before} added ({couples} couples, {families} families, {dogs} dog walkers, " +
                  $"{joggers} joggers, {surfers} surfers, {balcony} balcony/roof residents, {crew} marina crew; " +
                  $"{_pSpots.Count} resort/beach spots, {_standSpots.Count} town spots, {legs.Count} walk legs).");
    }

    // ---------------------------------------------------------------- probing GLB surfaces
    private static List<MeshCollider> TempColliders(GameObject go)
    {
        var list = new List<MeshCollider>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || LodLevel(mf.transform, go.transform.parent) != 0) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            list.Add(mc);
        }
        Physics.SyncTransforms();
        return list;
    }

    private static void DropColliders(List<MeshCollider> list)
    {
        foreach (var c in list) if (c != null) Object.DestroyImmediate(c);
    }

    /// <summary>All upward-facing surfaces of <paramref name="cols"/> under the vertical line at p,
    /// highest first, each with >= 0.35 m of flat floor around it.</summary>
    private static List<float> FloorsAt(Vector3 p, float top, HashSet<Collider> cols)
    {
        var ys = new List<float>();
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, top + 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!cols.Contains(h.collider) || h.normal.y < 0.92f) continue;
            bool flat = true;
            foreach (var o in new[] { new Vector3(0.35f, 0, 0), new Vector3(-0.35f, 0, 0), new Vector3(0, 0, 0.35f), new Vector3(0, 0, -0.35f) })
            {
                if (!Physics.Raycast(new Vector3(p.x + o.x, h.point.y + 0.6f, p.z + o.z), Vector3.down, out var h2, 0.7f) ||
                    !cols.Contains(h2.collider) || Mathf.Abs(h2.point.y - h.point.y) > 0.04f) { flat = false; break; }
            }
            // and head room: nothing of the building within 1.9 m above (not under a slab edge / inside a wall)
            if (flat && Physics.Raycast(new Vector3(p.x, h.point.y + 0.05f, p.z), Vector3.up, out var up, 1.9f) && cols.Contains(up.collider)
                && up.distance < 1.85f && !IsNextFloor(up.distance)) flat = false;
            if (flat) ys.Add(h.point.y);
        }
        ys.Sort((x, y) => y.CompareTo(x));
        return ys;
    }

    private static bool IsNextFloor(float d) => false;   // any building geometry < 1.85 m above = no head room

    private static int BalconyResidents(NbCast cast, Transform group, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        var root = group.parent;
        var targets = new List<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n == "Nagisa_B_HeroTower" || n == "Nagisa_B_ResortHotel2" || n.StartsWith("Nagisa_B_Condo") || n.StartsWith("Nagisa_B_Boutique"))
                targets.Add(t.gameObject);
        }
        int made = 0;
        foreach (var b in targets)
        {
            var f = FootOf(b.name);
            var cols = TempColliders(b);
            var set = new HashSet<Collider>(cols);
            try
            {
                int here = 0;
                float top = b.transform.position.y + f.h + 12f;
                for (int tries = 0; tries < 26 && here < MaxBalconyPerBuilding; tries++)
                {
                    if (R() > BalconyChance) continue;
                    // front (-z) band: balconies project here; roof terraces are anywhere
                    float lx = (R() * 2f - 1f) * (f.hx - 1.2f);
                    float lz = tries % 5 == 4 ? (R() * 2f - 1f) * (f.hz - 2f) : -f.hz + 0.6f + R() * 1.1f;
                    var w = b.transform.TransformPoint(new Vector3(lx, 0f, lz));
                    var floors = FloorsAt(w, top, set);
                    floors.RemoveAll(y => y < b.transform.position.y + 2.8f);
                    if (floors.Count == 0) continue;
                    float fy = floors[rng.Next(floors.Count)];
                    var at = new Vector3(w.x, fy, w.z);
                    var face = b.transform.rotation * Vector3.back;
                    var go = Spawn(cast, group, new PSpot { p = at, face = face + b.transform.right * (R() - 0.5f), role = "BalconyResident",
                                                            kind = R() < 0.3f ? MK.Wave : MK.Idle, scale = 1f, patchMat = "NB_Terrazzo" }, rng);
                    if (go != null) { here++; made++; }
                }
            }
            finally { DropColliders(cols); }
        }
        return made;
    }

    private static int MarinaCrew(NbCast cast, Transform group, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        var root = group.parent;
        int made = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.parent == null || t.parent.name != "Nagisa Marina") continue;
            bool yacht = t.name == "Nagisa_MotorYacht" || t.name == "Nagisa_Sailboat";
            if (!yacht || R() > 0.7f) continue;
            var cols = TempColliders(t.gameObject);
            var set = new HashSet<Collider>(cols);
            try
            {
                int n = R() < 0.5f ? 1 : 2;
                for (int k = 0; k < 6 && n > 0; k++)
                {
                    var w = t.TransformPoint(new Vector3((R() - 0.5f) * 1.2f, 0f, (R() - 0.5f) * 5f) / Mathf.Max(0.01f, t.lossyScale.x));
                    var floors = FloorsAt(w, t.position.y + 12f, set);
                    if (floors.Count == 0) continue;
                    var at = new Vector3(w.x, floors[floors.Count - 1 > 0 && R() < 0.4f ? 0 : floors.Count - 1], w.z);
                    var go = Spawn(cast, group, new PSpot { p = at, face = Quaternion.Euler(0f, R() * 360f, 0f) * Vector3.forward, role = "YachtCrew",
                                                            kind = R() < 0.35f ? MK.Wave : MK.Idle, scale = 1f, patchMat = "NB_Teak" }, rng);
                    if (go != null) { made++; n--; }
                }
            }
            finally { DropColliders(cols); }
        }
        return made;
    }

    // ================================================================= extra boats (W0 movers)

    private static void BuildWaterToys(List<AmbientPathMover> list, Transform group, NbCast cast, Transform peopleT)
    {
        var rng = new System.Random(5950);
        float R() => (float)rng.NextDouble();
        int toys = 0, riders = 0;
        // (stem, route metres, coast distance of the loop centre, rx, rz, speed, rider root y, rider kind)
        var plan = new (string stem, float m, float coast, float rx, float rz, float speed, float riderY, MK kind)[]
        {
            ("Nagisa_S_JetSki", _route.BeachStartM + 350f, -120f, 90f, 35f, 9.5f, JetSkiSeatRoot, MK.Sit),
            ("Nagisa_S_JetSki", _route.HotelM - 120f, -130f, 110f, 40f, 11f, JetSkiSeatRoot, MK.Sit),
            ("Nagisa_S_JetSki", _route.HotelM + 420f, -115f, 70f, 30f, 8.5f, JetSkiSeatRoot, MK.Sit),
            ("Nagisa_S_SUP", _route.BeachStartM + 700f, -85f, 40f, 12f, 0.9f, SupDeckRoot, MK.Idle),
            ("Nagisa_S_SUP", _route.HotelM - 420f, -82f, 36f, 10f, 0.8f, SupDeckRoot, MK.Idle),
            ("Nagisa_S_SUP", _route.HotelM + 200f, -90f, 45f, 12f, 0.85f, SupDeckRoot, MK.Idle),
            ("Nagisa_S_SUP", _route.BeachEndM - 300f, -86f, 38f, 11f, 0.9f, SupDeckRoot, MK.Idle),
        };
        foreach (var (stem, m, coast, rx, rz, speed, riderY, kind) in plan)
        {
            var go = WaterLoop(list, group, stem, m, coast, rx, rz, speed, R(), stem.Contains("SUP") ? 0.03f : 0.08f);
            if (go == null) continue;
            toys++;
            RiderPad(go.transform, riderY, _nb["NB_Gelcoat"]);
            var p = NbPerson(kind == MK.Sit ? cast.sit : cast.stand, peopleT, stem.Contains("SUP") ? "Paddleboarder" : "JetSkiRider",
                             go.transform.TransformPoint(new Vector3(0f, riderY, stem.Contains("SUP") ? 0f : 0.35f)),
                             go.transform.forward * -1f, kind, R());
            if (p == null) continue;
            var bench = p.transform.Find("Timber Waterfront Bench");
            if (bench != null) Object.DestroyImmediate(bench.gameObject);
            p.transform.SetParent(go.transform, true);
            FinishPerson(p);
            riders++;
        }
        // banana boat towed by a ski boat (the water taxi hull), three riders
        {
            var tow = WaterLoop(list, group, "Nagisa_S_JetSki", _route.HotelM - 700f, -150f, 160f, 55f, 7f, 0.3f, 0.07f,
                                trailer: "Nagisa_S_BananaBoat", trailerGap: 11f);
            if (tow != null)
            {
                toys++;
                var mover = tow.GetComponentInParent<AmbientPathMover>();
                var banana = mover != null && mover.cars.Length > 1 ? mover.cars[1] : null;
                RiderPad(tow.transform, JetSkiSeatRoot, _nb["NB_Gelcoat"]);
                var driver = NbPerson(cast.sit.Count > 0 ? cast.sit : cast.stand, peopleT, "JetSkiRider",
                                      tow.transform.TransformPoint(new Vector3(0f, JetSkiSeatRoot, 0.35f)), -tow.transform.forward,
                                      cast.sit.Count > 0 ? MK.Sit : MK.Idle, R());
                if (driver != null)
                {
                    var bb = driver.transform.Find("Timber Waterfront Bench"); if (bb != null) Object.DestroyImmediate(bb.gameObject);
                    driver.transform.SetParent(tow.transform, true); FinishPerson(driver); riders++;
                }
                if (banana != null)
                {
                    RiderPad(banana, BananaSeatRoot, _nb["NB_Lamp"]);
                    for (int k = 0; k < 3; k++)
                    {
                        var r = NbPerson(cast.sit.Count > 0 ? cast.sit : cast.stand, peopleT, "BananaRider",
                                         banana.TransformPoint(new Vector3(0f, BananaSeatRoot, -1.4f + k * 1.3f)), -banana.forward,
                                         cast.sit.Count > 0 ? MK.Sit : MK.Idle, R());
                        if (r == null) continue;
                        var bb = r.transform.Find("Timber Waterfront Bench"); if (bb != null) Object.DestroyImmediate(bb.gameObject);
                        r.transform.SetParent(banana, true); FinishPerson(r); riders++;
                    }
                }
            }
        }
        Debug.Log($"[nagisa] water toys: {toys} movers (jet skis, paddleboards, banana tow), {riders} riders.");
    }

    /// <summary>An elliptical W0 loop off the beach, validated like every other boat path.</summary>
    private static GameObject WaterLoop(List<AmbientPathMover> list, Transform group, string stem, float m, float coast,
                                        float rx, float rz, float speed, float phase, float heave,
                                        string trailer = null, float trailerGap = 0f)
    {
        int i = _route.IndexAt(m);
        if (!Offshore(i, coast, out var c)) return null;
        var t = _route.Tangent[i]; t.y = 0f; t.Normalize();
        var s = Vector3.Cross(Vector3.up, t);
        var loop = new List<Vector3>();
        for (int k = 0; k < 40; k++)
        {
            float a = k / 40f * Mathf.PI * 2f;
            loop.Add(c + t * Mathf.Cos(a) * rx + s * Mathf.Sin(a) * rz);
        }
        int n0 = list.Count;
        AddBoat(list, group, stem, loop, speed, phase, heave, 0.8f);
        if (list.Count == n0) return null;
        var mover = list[list.Count - 1];
        if (trailer != null)
        {
            var tr = PlaceWorld(trailer, mover.transform, mover.cars[0].position, 0f);
            if (tr != null)
            {
                mover.cars = new[] { mover.cars[0], tr.transform };
                mover.carOffsets = new[] { 0f, -trailerGap };
                mover.carYaw = new[] { 0f, 0f };
                mover.Bake();
                mover.SetHead(mover.startDistance);
                mover.Place();
                UnityEditor.EditorUtility.SetDirty(mover);
            }
        }
        return mover.cars[0].gameObject;
    }
}

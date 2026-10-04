// NAGISA BAY destination brief section 13 - WORLD LIFE for the marina + working harbour (worker G).
//
// People with a PURPOSE, placed on the authored activity spots that NagisaBayEnvironment.Marina2.cs leaves under
// "NB Marina2/Spots" (so this stage can run on its own):
//   Sit    -> diners / coffee drinkers on cafe terraces and benches (seated donors)
//   Queue  -> 3-4 people lining up at a gelato / shave-ice kiosk, facing it
//   Stand  -> friends chatting by the kiosks and the bike racks (some wave to the boats)
//   PierEnd/PierMid -> couples admiring the yachts, owners walking out to their boats (walk legs along the pier)
//   Work   -> fishermen at the fish sheds / on the mole mending nets, and crate-carrying walkers between shed and mole
// Plus boardwalk strollers: pairs and trios walking a real destination (cafe -> pier root, hotel -> yacht club).
// Efficiency: the group carries NagisaCrowdBudget (shadows off > 120 m, impostor LOD > 200 m, asleep > 800 m), every
// person is a Minato donor clone (no new rigs), nobody is within the road corridor, nothing spawns at runtime, and the
// total stays in the low hundreds on purpose (brief 18: composition and placed activity, not NPC spam).
// Stage "WorldLife2" (order 100) -> "NB Overhaul/NB WorldLife2".  ALL numbers PROVISIONAL.
using System.Collections.Generic;
using UnityEngine;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    private const string WlLook = "NR";
    private static readonly List<Vector3> _wlPeople = new List<Vector3>();

    private static bool WlFree(Vector3 p, float gap = 0.8f)
    {
        foreach (var q in _wlPeople)
        {
            float dx = p.x - q.x, dz = p.z - q.z;
            if (dx * dx + dz * dz < gap * gap) return false;
        }
        return true;
    }

    private static GameObject WlPerson(NbCast cast, Transform parent, string role, MK kind, Vector3 pos, Vector3 face, float seed,
                                       Vector3? end = null, float speed = 0f)
    {
        var pool = kind == MK.Sit ? cast.sit : kind == MK.Walk ? cast.walk : cast.stand;
        if (pool == null || pool.Count == 0) return null;
        // authored markers already carry the deck height (terrace / lay-by / boardwalk); keep it when it is above the terrain
        float sy = Nb4Surface(pos);
        if (!(pos.y > sy + 0.05f && pos.y < sy + 8f)) pos.y = sy;
        if (!WlFree(pos)) return null;
        if (end.HasValue) { var e = end.Value; float ey = Nb4Surface(e); if (!(e.y > ey + 0.05f && e.y < ey + 8f)) e.y = ey; end = e; }
        string dress = kind == MK.Walk ? "Walker_" : kind == MK.Sit ? "Customer_" : "Chat_";
        var go = Nb4Clone(pool, parent, role, WlLook, dress, pos, face, kind, seed, null, end, speed);
        if (go == null) return null;
        _wlPeople.Add(pos);
        if (end.HasValue) _wlPeople.Add(end.Value);
        return go;
    }

    /// <summary>Paddleboarders gliding slow loops in the sheltered south half of the marina basin (W0 AmbientPathMover, 1.5 km cull).</summary>
    private static int WlPaddlers(Transform group, NbCast cast, System.Random rng)
    {
        var q = M2QFrame();
        if (q == null || !GlbExists("Nagisa_S_SUP")) return 0;
        var moverT = AmbientMoverStaging.Converge(group, "Marina paddleboards").transform;
        int made = 0;
        for (int k = 0; k < 3; k++)
        {
            var loop = new List<Vector3>();
            float cu = -150f + k * 26f, cs = q.best + 38f + k * 16f, rx = 26f, rz = 8f + k * 2f;
            for (int i = 0; i < 28; i++)
            {
                float a = i / 28f * Mathf.PI * 2f;
                var p = q.At(cu + Mathf.Cos(a) * rx, cs + Mathf.Sin(a) * rz); p.y = 0.03f;
                loop.Add(p);
            }
            bool ok = true;
            foreach (var p in loop) if (!M2Deep(p, 0.9f) || _route.PlanDistance(p.x, p.z, out _) < 60f) { ok = false; break; }
            if (!ok) { Debug.LogWarning($"[g-life2] paddleboard loop {k} leaves the deep marina water, dropped."); continue; }
            var holder = AmbientMoverStaging.Converge(moverT, $"Marina SUP {k}");
            var sup = PlaceWorld("Nagisa_S_SUP", holder.transform, loop[0], 0f, 1f, 0.004f);
            if (sup == null) { Object.DestroyImmediate(holder); continue; }
            var mover = holder.AddComponent<AmbientPathMover>();
            mover.mode = AmbientPathMover.PathMode.Loop;
            mover.path = AmbientMoverStaging.BakePolyline(loop);
            mover.cars = new[] { sup.transform }; mover.carOffsets = new[] { 0f }; mover.carYaw = new[] { 0f };
            mover.speed = 0.85f + 0.1f * k; mover.accel = 0.5f;
            mover.bankFactor = 0.03f; mover.bobHeave = 0.03f; mover.bobTilt = 0.8f; mover.bobFrequency = 0.3f;
            mover.cullDistance = AmbientCull.DefaultCullDistance; mover.seed = 8500 + k;
            mover.Bake();
            mover.startDistance = (k * 0.31f) * mover.TotalLength;
            mover.SetHead(mover.startDistance); mover.Place();
            UnityEditor.EditorUtility.SetDirty(mover);
            RiderPad(sup.transform, SupDeckRoot, _nb["NB_Gelcoat"]);
            var person = NbPerson(cast.stand, group, "Paddleboarder", sup.transform.TransformPoint(new Vector3(0f, SupDeckRoot, 0f)), -sup.transform.forward, MK.Idle, (float)rng.NextDouble());
            if (person != null)
            {
                var bench = person.transform.Find("Timber Waterfront Bench"); if (bench != null) Object.DestroyImmediate(bench.gameObject);
                person.transform.SetParent(sup.transform, true);
                FinishPerson(person);
                made++;
            }
        }
        return made;
    }

    [NagisaStage(100, "WorldLife2")]
    private static void BuildWorldLife2Stage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0 || _nbColourRemaps[0] == null) { PrepareNagisaTextures(); BuildNbMaterials(); }
        var root = group.root;
        var cast = NbCrowdCast(root);
        if (cast.walk.Count == 0 || cast.stand.Count == 0)
        {
            Debug.LogError("[g-life2] no Minato Crowd_ donors in the scene - nothing staged.");
            return;
        }
        Physics.SyncTransforms();
        _nb4N = 6000; _nb4Roles.Clear(); _wlPeople.Clear();
        var spotsT = group.parent != null ? group.parent.Find("NB Marina2/Spots") : null;
        if (spotsT == null) { Debug.LogWarning("[g-life2] no 'NB Marina2/Spots' - run the Marina2 stage first."); return; }
        var rng = new System.Random(1301);
        float R() => (float)rng.NextDouble();

        var sitT = Nb4Child(group, "Diners and visitors");
        var walkT = Nb4Child(group, "Strollers and owners");
        var workT = Nb4Child(group, "Harbour workers");

        int diners = 0, queued = 0, chat = 0, couples = 0, owners = 0, workers = 0, carriers = 0, strollers = 0;
        for (int i = 0; i < spotsT.childCount; i++)
        {
            var sp = spotsT.GetChild(i);
            string kind = sp.name.Substring("M2Spot_".Length);
            var p = sp.position; var f = sp.forward; var side = Vector3.Cross(Vector3.up, f);
            switch (kind)
            {
                case "Sit":
                    if (R() < 0.82f && WlPerson(cast, sitT, "MarinaDiner", MK.Sit, p, f, R()) != null) diners++;
                    break;
                case "Queue":
                    for (int k = 0; k < 2 + rng.Next(3); k++)
                        if (WlPerson(cast, sitT, "KioskQueue", MK.Idle, p - f * (0.9f * k) + side * ((R() - 0.5f) * 0.3f), f, R()) != null) queued++;
                    break;
                case "Stand":
                    for (int k = 0; k < 1 + rng.Next(2); k++)
                    {
                        var q = p + side * (k * 0.9f);
                        if (WlPerson(cast, sitT, "MarinaChat", R() < 0.3f ? MK.Wave : MK.Idle, q, (f + side * (k == 0 ? 0.35f : -0.35f)).normalized, R()) != null) chat++;
                    }
                    break;
                case "PierEnd":
                    if (WlPerson(cast, sitT, "YachtWatcher", R() < 0.4f ? MK.Wave : MK.Idle, p, f, R()) != null) couples++;
                    if (WlPerson(cast, sitT, "YachtWatcher", MK.Idle, p + side * 0.85f, f, R()) != null) couples++;
                    break;
                case "PierMid":
                    // an owner walking out to the boat and back along the pier (purposeful 20 m leg)
                    {
                        var a = p - side * 8f; var b = p + side * 8f;
                        if (WlPerson(cast, walkT, "BoatOwner", MK.Walk, a, b - a, R(), b, 0.9f + R() * 0.3f) != null) owners++;
                    }
                    break;
                case "Work":
                    if (WlPerson(cast, workT, "Fisherman", R() < 0.5f ? MK.Idle : MK.Wave, p, f, R()) != null) workers++;
                    // crate carrier: shed -> mole, a 24 m leg
                    if (R() < 0.7f)
                    {
                        var a = p; var b = p + f * 24f + side * (R() * 4f - 2f);
                        if (WlPerson(cast, workT, "CrateCarrier", MK.Walk, a + side * 1.4f, b - a, R(), b + side * 1.4f, 0.8f + R() * 0.25f) != null) carriers++;
                    }
                    break;
            }
        }

        // lookouts on the climb / descent: riders and hikers pausing for the view (photographers wave at the camera-people)
        var clSpots = group.parent != null ? group.parent.Find("NB ClimbDressing/Spots") : null;
        int viewers = 0, photographers = 0;
        if (clSpots != null)
        {
            var viewT = Nb4Child(group, "Lookout visitors");
            for (int i = 0; i < clSpots.childCount; i++)
            {
                var sp = clSpots.GetChild(i);
                var p = sp.position; var f = sp.forward;
                if (sp.name == "CL_Spot_Sit" && R() < 0.75f && WlPerson(cast, viewT, "LookoutSitter", MK.Sit, p, f, R()) != null) viewers++;
                else if (sp.name == "CL_Spot_Photo" && R() < 0.8f && WlPerson(cast, viewT, "LookoutPhotographer", R() < 0.35f ? MK.Wave : MK.Idle, p, f, R()) != null) photographers++;
            }
        }

        // boardwalk strollers: pairs / trios walking a 30-60 m leg along the quay, each leg between two real places
        var legs = new[] { (330f, 420f), (420f, 520f), (470f, 560f), (520f, 610f), (560f, 640f), (300f, 380f) };
        foreach (var (m0, m1) in legs)
        {
            float vs = M2Vs(m0);
            var a = M2W(m0, vs - 5.5f); var b = M2W(m1, M2Vs(m1) - 5.5f);
            float yE = M2DeckY(m0);
            int party = 2 + rng.Next(2);
            var across = Vector3.Cross(Vector3.up, (b - a).normalized);
            for (int k = 0; k < party; k++)
            {
                var off = across * (k * 0.8f) + (b - a).normalized * (k * 0.35f);
                if (WlPerson(cast, walkT, "MarinaStroller", MK.Walk, a + off, b - a, R(), b + off, 0.75f + R() * 0.2f) != null) strollers++;
            }
        }

        int sups = WlPaddlers(group, cast, rng);

        if (group.GetComponent<NagisaCrowdBudget>() == null) group.gameObject.AddComponent<NagisaCrowdBudget>();
        Debug.Log($"[g-life2] marina + harbour life: {diners} diners, {queued} queueing, {chat} chatting, {couples} yacht watchers, " +
                  $"{owners} boat owners walking piers, {workers} fishermen, {carriers} crate carriers, {strollers} boardwalk strollers, " +
                  $"{viewers + photographers} lookout visitors, {sups} paddleboarders = " +
                  $"{diners + queued + chat + couples + owners + workers + carriers + strollers + viewers + photographers} people (NagisaCrowdBudget attached: shadows off >120 m, asleep >800 m).");
    }
}

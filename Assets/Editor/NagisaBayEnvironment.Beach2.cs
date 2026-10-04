// Nagisa Bay (B5) PASS 2 - boardwalk promenade, beach life dressing, beach shops (copilot CLI Opus).
//
// Replaces pass 1's bare white strip ("one bench and ONE person", palms scattered on grass):
//   * the paving strip gains a collider + "Beach Sand walk layer" so walkers stand ON it,
//   * a raised teak BOARDWALK runs seaward of the paving with a fascia down to the sand,
//     a railing on the sea edge (gaps at the beach-access steps every PromAccessEveryM),
//   * ORDERLY palm rows: one straight row on the sand at a fixed offset + fixed spacing,
//     all the same species per run, small alternating lean,
//   * lamps on the railing line, benches between them, rental stands at each access gap,
//     food-truck lay-bys with a paved apron and a queue,
//   * beach: towels + parasols between the umbrella sets, two volleyball courts,
//     cabanas on the hotel deck, the new beach bars / kiosks / surf shop on the cafes pad.
// People are only RECORDED here as spots (the P* lists in Life2.cs spawn them).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const float BoardwalkW = 3.2f, BoardwalkLift = 0.19f;   // deck top above the paving-edge ground
    private const float PromAccessEveryM = 48f, PromAccessGapM = 5f;
    private const float PromPalmRowOffM = 4.6f;                     // beyond the boardwalk's sea edge
    private const float PromPalmStepM = 12f;
    private const float FoodTruckEveryM = 420f, RentalEveryM = 190f;
    private const float VolleyCourtEveryM = 900f;

    private static void BuildPromenade2(Transform group)
    {
        var promT = new GameObject("Beach Promenade").transform;
        promT.SetParent(group, false);
        int i0 = _route.IndexAt(_route.BeachStartM), i1 = _route.IndexAt(_route.BeachEndM);

        var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pt = new List<int>();
        var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bt = new List<int>();
        var fv = new List<Vector3>(); var fuv = new List<Vector2>(); var ft = new List<int>();
        bool open = false; float along = 0f; Vector3 prevMid = Vector3.zero;
        float lastLamp = -999f, lastPalm = -999f, lastBench = -999f, lastRail = -999f, lastTruck = -FoodTruckEveryM * 0.6f,
              lastRental = -RentalEveryM * 0.3f, lastHedge = -999f;
        int palmRun = 0, lamps = 0, palms = 0, rails = 0, benches = 0, trucks = 0, rentals = 0;
        var rng = new System.Random(5502);
        float R() => (float)rng.NextDouble();
        float outer = PromenadeOuterM, bwOuter = PromenadeOuterM + BoardwalkW;
        for (int i = i0; i <= i1; i++)
        {
            var p = _route.Position[i];
            var sd = _route.SideFlat(i) * SeaSign(i);
            var a = p + sd * PromenadeInnerM; var b = p + sd * outer; var c = p + sd * bwOuter;
            var mid = (a + b) * 0.5f;
            bool ok = _ground.Coast(c.x, c.z) > 2f && !NearDrive(mid.x) && !_route.OnBridge(i);
            if (open) along += Vector3.Distance(mid, prevMid);
            prevMid = mid;
            if (!ok) { open = false; continue; }
            a.y = _ground.Height(a.x, a.z) + 0.07f;
            b.y = _ground.Height(b.x, b.z) + 0.07f;
            float deckY = b.y + BoardwalkLift - 0.07f;
            var b2 = new Vector3(b.x, deckY, b.z); var c2 = new Vector3(c.x, deckY, c.z);
            var cg = new Vector3(c.x, Mathf.Min(_ground.Height(c.x, c.z), deckY) - 0.25f, c.z);
            var bg = new Vector3(b.x, b.y - 0.02f, b.z);
            pv.Add(a); puv.Add(new Vector2(0f, along / 2.4f));
            pv.Add(b); puv.Add(new Vector2((outer - PromenadeInnerM) / 2.4f, along / 2.4f));
            // boardwalk: planks run across the walk (u along the route)
            bv.Add(b2); buv.Add(new Vector2(along / 1.8f, 0f));
            bv.Add(c2); buv.Add(new Vector2(along / 1.8f, BoardwalkW / 1.8f));
            // fascias: inner step face (paving -> deck) and the sea-side face down into the sand
            fv.Add(bg); fuv.Add(new Vector2(along / 1.2f, 0f));
            fv.Add(b2); fuv.Add(new Vector2(along / 1.2f, (deckY - bg.y) / 1.2f));
            fv.Add(c2); fuv.Add(new Vector2(along / 1.2f, 0f));
            fv.Add(cg); fuv.Add(new Vector2(along / 1.2f, (deckY - cg.y) / 1.2f));
            if (open)
            {
                int k = pv.Count - 4;
                pt.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                k = bv.Count - 4;
                bt.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                int f = fv.Count - 8;
                // inner face (faces landward / up the step) and outer face (faces the sea)
                Quad(ft, f + 0, f + 4, f + 5, f + 1, fv, -sd);
                Quad(ft, f + 2, f + 6, f + 7, f + 3, fv, sd);
            }
            open = true;

            float dist = _route.Distance[i];
            var seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            var tan = _route.Tangent[i]; tan.y = 0f; tan.Normalize();
            float railYaw = Mathf.Atan2(tan.x, tan.z) * Mathf.Rad2Deg - 90f;
            bool access = Mathf.Repeat(dist, PromAccessEveryM) < PromAccessGapM;

            // railing on the boardwalk's sea edge, gaps at the access steps
            if (dist - lastRail >= 4f)
            {
                lastRail = dist;
                if (!access)
                {
                    var rp = p + sd * (bwOuter - 0.12f); rp.y = deckY;
                    PlaceWorld("Nagisa_Railing", promT, rp, railYaw, 1f, SmallPropCull);
                    rails++;
                }
                else if (dist - lastRental >= RentalEveryM)
                {
                    // rental stand on the sand beside the access, with an attendant + a customer
                    lastRental = dist;
                    var sp = p + sd * (bwOuter + 3.2f) + tan * 3.5f;
                    if (CanPlace(sp.x, sp.z, 1.5f, out float sy, 2f) && !InAnyPad(sp.x, sp.z, 2f))
                    {
                        PlaceWorld("Nagisa_S_RentalStand", promT, new Vector3(sp.x, sy - 0.05f, sp.z), seaYaw + 180f, 1f, SmallPropCull);
                        _pSpots.Add(PS(sp + sd * 0.9f, -sd, "RentalAttendant"));
                        _pSpots.Add(PS(sp - sd * 1.2f + tan * 0.4f, sd, "RentalCustomer"));
                        // boards leaning on the stand
                        for (int s = 0; s < 3; s++)
                        {
                            var bp = sp + tan * (1.6f + s * 0.45f) + sd * 0.2f; bp.y = _ground.Height(bp.x, bp.z);
                            var brd = PlaceWorld(s == 1 ? "Nagisa_S_SUP" : "Nagisa_S_Surfboard", promT, bp, seaYaw + 90f, 1f, SmallPropCull);
                            if (brd != null && s == 1) { brd.transform.position = bp + tan * 1.2f - sd * 1.6f; brd.transform.rotation = Quaternion.Euler(0f, seaYaw, 0f); }
                            if (brd != null && s != 1) brd.transform.rotation = Quaternion.Euler(0f, seaYaw + 90f, 0f) * Quaternion.Euler(-12f, 0f, 0f);
                        }
                        rentals++;
                    }
                }
            }
            if (dist - lastLamp >= PromLampSpacingM)
            {
                lastLamp = dist;
                var lp = p + sd * (bwOuter - 0.45f); lp.y = deckY;
                PlaceWorld("Nagisa_PromenadeLamp", promT, lp, seaYaw);
                lamps++;
            }
            if (dist - lastBench >= PromLampSpacingM && dist - lastLamp > 10f && !access)
            {
                lastBench = dist;
                var bp = p + sd * (bwOuter - 1.1f); bp.y = deckY;
                // every other bench seat is taken: the seated donor brings its own bench model
                if (R() < 0.5f) PlaceWorld("Nagisa_Bench", promT, bp, seaYaw + 180f);
                else _pSpots.Add(PS(bp, sd, "BenchSitter", MinatoCrowdActor.MotionKind.Sit));
                benches++;
            }
            // one orderly row of palms on the sand, fixed spacing, species in runs of 6
            if (dist - lastPalm >= PromPalmStepM)
            {
                lastPalm = dist;
                var pp = p + sd * (bwOuter + PromPalmRowOffM);
                if (CanPlace(pp.x, pp.z, 1f, out float py, 1.5f) && !InAnyPad(pp.x, pp.z, 2f))
                {
                    bool runB = (palmRun / 6) % 3 == 2;
                    float lean = ((palmRun & 1) == 0 ? -6f : 6f);
                    PlaceWorld(runB ? "Nagisa_CoconutPalm_B" : "Nagisa_CoconutPalm_A", promT,
                               new Vector3(pp.x, py - 0.1f, pp.z), seaYaw + lean, 0.95f + 0.05f * Mathf.Sin(palmRun * 1.7f));
                    palms++;
                }
                palmRun++;
            }
            // food-truck lay-by: a paved apron on the sand side, truck parallel, hatch (+x) toward the walk
            if (dist - lastTruck >= FoodTruckEveryM && !access)
            {
                var tp = p + sd * (bwOuter + 8.5f);
                if (CanPlace(tp.x, tp.z, 5f, out float ty, 4f) && !InAnyPad(tp.x, tp.z, 6f))
                {
                    lastTruck = dist;
                    var apron = new List<Vector3>(); var auv = new List<Vector2>(); var at = new List<int>();
                    float ay = Mathf.Max(ty, _ground.Height((p + sd * bwOuter).x, (p + sd * bwOuter).z)) + 0.08f;
                    Box(apron, auv, at, new Vector3(tp.x, ay - 0.2f, tp.z) - sd * 2.4f, tan, sd, 12f, 0.4f, 9.5f);
                    AddMesh(promT, $"Promenade Paving Food Truck Apron {trucks} - Beach Sand walk layer",
                            Finish($"Nagisa_TruckApron_{trucks}", apron, auv, at), _nb["NB_PlazaStone"], true);
                    // truck local +x (hatch) must point landward (-sd): yaw so local -z runs along the tangent
                    float yaw = Mathf.Atan2(-tan.x, -tan.z) * Mathf.Rad2Deg;
                    var q = Quaternion.Euler(0f, yaw, 0f);
                    if (Vector3.Dot(q * Vector3.right, -sd) < 0f) yaw += 180f;
                    PlaceWorld("Nagisa_S_FoodTruck", promT, new Vector3(tp.x, ay, tp.z), yaw,
                               1f, 0.003f, NbCarColour(trucks * 3 + 1));
                    for (int k = 0; k < 4; k++)
                        _pSpots.Add(PS(new Vector3(tp.x, ay, tp.z) - sd * (2.4f + 0.8f * k) + tan * (0.6f - 0.35f * k), sd, "FoodTruckQueue"));
                    for (int k = 0; k < 3; k++)
                        _pSpots.Add(PS(new Vector3(tp.x, ay, tp.z) - sd * 3.4f + tan * (4f + k * 1.6f), (k & 1) == 0 ? tan : -tan,
                                       "TruckDiner", MinatoCrowdActor.MotionKind.Sit));
                    trucks++;
                }
            }
            // low clipped hedge line + flowering shrubs on the landward verge (never inside the corridor)
            if (dist - lastHedge >= 4.05f)
            {
                lastHedge = dist;
                var hp = p - sd * (CorridorKeepOutM + 1.2f);
                if (dist > TownD0 - 10f && dist < TownD1 + 110f) { }   // town frontage: the coast sidewalk owns this verge
                else if (CanPlace(hp.x, hp.z, 0.4f, out float hy) && !InAnyPad(hp.x, hp.z, 1f))
                    PlaceWorld("Nagisa_S_HedgeSeg", promT, new Vector3(hp.x, hy - 0.05f, hp.z), railYaw, 1f, SmallPropCull);
            }
            // promenade walk legs (Life2 splits them) on the paving centre + the boardwalk
            if (i % 6 == 0)
            {
                int j = Mathf.Min(i1, i + 6);
                var pj = _route.Position[j]; var sj = _route.SideFlat(j) * SeaSign(j);
                var w0 = p + sd * PromWalkOffsetM; w0.y = _ground.Height(w0.x, w0.z) + 0.07f;
                var w1 = pj + sj * PromWalkOffsetM; w1.y = _ground.Height(w1.x, w1.z) + 0.07f;
                if (_ground.Coast(w1.x, w1.z) > 3f && !NearDrive(w1.x)) _pWalks.Add((w0, w1, "Promenade"));
                var x0 = p + sd * (outer + 1.2f); x0.y = deckY;
                var x1 = pj + sj * (outer + 1.2f); x1.y = _ground.Height(x1.x, x1.z) + BoardwalkLift;
                if (_ground.Coast(x1.x, x1.z) > 3f && !NearDrive(x1.x)) _pWalks.Add((x0, x1, "Boardwalk"));
            }
        }
        FixWinding(pv, pt); FixWinding(bv, bt);
        if (pt.Count > 0)
        {
            var go = AddMesh(promT, "Promenade Paving - Beach Sand walk layer", Finish("Nagisa_Promenade", pv, puv, pt), _nb["NB_Paving"], true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        if (bt.Count > 0)
        {
            var go = AddMesh(promT, "Boardwalk Deck - Beach Sand walk layer", Finish("Nagisa_Boardwalk", bv, buv, bt), _nb["NB_Boardwalk"], true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            AddMesh(promT, "Boardwalk Fascia", Finish("Nagisa_BoardwalkFascia", fv, fuv, ft), _nb["NB_Teak"], false);
        }
        Debug.Log($"[nagisa] promenade: paving {pt.Count / 3:N0} + boardwalk {bt.Count / 3:N0} + fascia {ft.Count / 3:N0} tris; " +
                  $"{rails} rail sections, {lamps} lamps, {benches} benches, {palms} row palms, {trucks} food trucks, {rentals} rental stands");
    }

    private static void Quad(List<int> t, int a, int b, int c, int d, List<Vector3> v, Vector3 outward)
    {
        var n = Vector3.Cross(v[b] - v[a], v[d] - v[a]);
        if (Vector3.Dot(n, outward) >= 0f) t.AddRange(new[] { a, b, c, a, c, d });
        else t.AddRange(new[] { a, c, b, a, d, c });
    }

    // ---------------------------------------------------------------- beach extras
    private static void BuildBeach2(Transform group)
    {
        var beachT = group.Find("Beach Dressing");
        if (beachT == null) { beachT = new GameObject("Beach Dressing").transform; beachT.SetParent(group, false); }
        int i0 = _route.IndexAt(_route.BeachStartM + 30f), i1 = _route.IndexAt(_route.BeachEndM - 30f);
        var rng = new System.Random(5504);
        float R() => (float)rng.NextDouble();
        float lastSet = -999f, lastCourt = -VolleyCourtEveryM * 0.35f;
        int towels = 0, courts = 0, swimmers = 0, surf = 0;
        for (int i = i0; i <= i1; i++)
        {
            float dist = _route.Distance[i];
            var p = _route.Position[i];
            var sd = _route.SideFlat(i) * SeaSign(i);
            var tan = _route.Tangent[i]; tan.y = 0f; tan.Normalize();
            float seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            // coast-distance probes: dry sand (10..24 m), wet sand (2..6 m), shallows (-3..-12 m)
            Vector3 dry = Vector3.zero, wet = Vector3.zero, shallow = Vector3.zero;
            bool hasDry = false, hasWet = false, hasShallow = false;
            for (float o = PromenadeOuterM + BoardwalkW + 7f; o < 220f; o += 1.5f)
            {
                var q = p + sd * o;
                float c = _ground.Coast(q.x, q.z);
                if (!hasDry && c <= 24f && c >= 10f) { dry = q; hasDry = true; }
                if (!hasWet && c <= 6f && c >= 2f) { wet = q; hasWet = true; }
                if (!hasShallow && c <= -4f) { shallow = q; hasShallow = _ground.Height(q.x, q.z) > -1.6f; break; }
            }
            if (!hasDry || InAnyPad(dry.x, dry.z, 4f)) continue;

            // volleyball court on the dry sand: net across the court, 2 v 2 + a spectator
            if (dist - lastCourt >= VolleyCourtEveryM)
            {
                var cc = dry + sd * 2f;
                if (CanPlace(cc.x, cc.z, 9f, out float cy, 6f))
                {
                    lastCourt = dist; lastSet = dist;
                    PlaceWorld("Nagisa_S_VolleyNet", beachT, new Vector3(cc.x, cy - 0.05f, cc.z), Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg - 90f, 1f, SmallPropCull);
                    // net along sd (local x = sd direction) -> the two halves are +-tan
                    foreach (float s in new[] { -1f, 1f })
                        foreach (float l in new[] { -1.8f, 1.8f })
                            _pSpots.Add(PS(cc + tan * (s * (3.2f + R() * 1.5f)) + sd * l, -tan * s, "Volleyball",
                                           R() < 0.5f ? MinatoCrowdActor.MotionKind.Wave : MinatoCrowdActor.MotionKind.Idle));
                    _pSpots.Add(PS(cc + sd * -6.5f + tan * 2f, sd, "Spectator"));
                    courts++;
                    continue;
                }
            }
            // towel + parasol sets between the pass-1 umbrella/lounger sets
            if (dist - lastSet >= 7f)
            {
                lastSet = dist;
                if (R() < 0.8f)
                {
                    var u = dry + sd * (R() * 6f - 1f) + tan * (R() * 3f - 1.5f);
                    if (CanPlace(u.x, u.z, 2f, out float uy, 5f))
                    {
                        float yaw = seaYaw + 180f + (R() - 0.5f) * 30f;
                        PlaceWorld("Nagisa_S_Parasol", beachT, new Vector3(u.x, uy - 0.1f, u.z), R() * 360f, 1f, SmallPropCull,
                                   NbColourway(rng.Next(8)));
                        int n = R() < 0.6f ? 2 : 1;
                        var side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                        for (int k = 0; k < n; k++)
                        {
                            var tp = u + side * (k == 0 ? -0.6f : 0.6f) + sd * 0.8f; tp.y = _ground.Height(tp.x, tp.z) + 0.01f;
                            PlaceWorld("Nagisa_S_Towel", beachT, tp, yaw, 1f, SmallPropCull, NbColourway(rng.Next(8)));
                            towels++;
                            // a sunbather on (sitting) or beside (standing) each towel
                            if (R() < 0.75f) _pSpots.Add(PS(tp, sd, "Sunbather", R() < 0.6f ? MinatoCrowdActor.MotionKind.Sit : MinatoCrowdActor.MotionKind.Idle));
                        }
                        // kids near the family sets
                        if (R() < 0.3f) _pSpots.Add(PS(u + sd * 3f + tan * 1.5f, -sd, "Kid", MinatoCrowdActor.MotionKind.Wave, scale: 0.66f));
                    }
                }
            }
            // wet sand: shoreline walkers + surfers carrying boards
            if (hasWet && i % 9 == 0)
            {
                int j = Mathf.Min(i1, i + 7);
                var pj = _route.Position[j]; var sj = _route.SideFlat(j) * SeaSign(j);
                Vector3 wetB = Vector3.zero; bool okB = false;
                for (float o = PromenadeOuterM + BoardwalkW + 7f; o < 220f; o += 1.5f)
                {
                    var q = pj + sj * o;
                    float c = _ground.Coast(q.x, q.z);
                    if (c <= 6f && c >= 2f) { wetB = q; okB = true; break; }
                }
                if (okB)
                {
                    wet.y = _ground.Height(wet.x, wet.z); wetB.y = _ground.Height(wetB.x, wetB.z);
                    bool surfer = R() < 0.4f;
                    _pWalks.Add((wet, wetB, surfer ? "Surfer" : "Shoreline"));
                    if (surfer) surf++;
                }
            }
            // swimmers standing waist-deep in the shallows, some in pairs
            if (hasShallow && i % 5 == 0 && R() < 0.7f)
            {
                var s = shallow + sd * (R() * 8f) + tan * (R() * 6f - 3f);
                float h = _ground.Height(s.x, s.z);
                if (h > -1.3f && h < -0.35f)
                {
                    _pSpots.Add(PS(new Vector3(s.x, h, s.z), R() < 0.5f ? -sd : tan, "Swimmer",
                                   R() < 0.4f ? MinatoCrowdActor.MotionKind.Wave : MinatoCrowdActor.MotionKind.Idle));
                    swimmers++;
                    if (R() < 0.4f)
                    {
                        var s2 = s + tan * 1.2f; float h2 = _ground.Height(s2.x, s2.z);
                        if (h2 > -1.3f && h2 < -0.3f) { _pSpots.Add(PS(new Vector3(s2.x, h2, s2.z), -tan, "Swimmer")); swimmers++; }
                    }
                }
            }
        }
        Debug.Log($"[nagisa] beach extras: {towels} towels, {courts} volleyball courts, {swimmers} swimmer spots, {surf} surfer legs");
    }

    // ---------------------------------------------------------------- beach shops (cafes pad)
    private static void BuildBeachShops2(Transform group)
    {
        var shopT = new GameObject("Beach Shops").transform;
        shopT.SetParent(group, false);
        if (!_route.Pads.TryGetValue("cafes", out var P)) return;
        var stems = new[] { "Nagisa_B_SurfShop", "Nagisa_B_BeachBar", "Nagisa_B_Gelato", "Nagisa_B_ShaveIce", "Nagisa_B_BeachBar" };
        var halfW = new[] { 7.3f, 7f, 3.5f, 2.6f, 7f };
        float span = 0f; foreach (var h in halfW) span += 2f * h + 6f;
        _route.PlanDistance(P.c.x, P.c.z, out int ci);
        var tan = _route.Tangent[ci]; tan.y = 0f; tan.Normalize();
        float cur = -span * 0.5f;
        int placed = 0;
        var rng = new System.Random(5602);
        for (int k = 0; k < stems.Length; k++)
        {
            cur += halfW[k] + 3f;
            var q = P.c + tan * cur;
            cur += halfW[k] + 3f;
            if (!CanPlace(q.x, q.z, 6f, out float y, 3f)) continue;
            var pos = new Vector3(q.x, y - 0.1f, q.z);
            var go = PlaceWorld(stems[k], shopT, pos, FaceRoadYaw(pos), 1f, 0.002f, NbColourway(k * 3 + 2), hero: true);
            if (go == null) continue;
            placed++;
            var toRoad = -(go.transform.rotation * Vector3.forward) * -1f;   // local -z = front, toward the road
            var front = go.transform.rotation * Vector3.back;
            // customers at the counter + diners at the bar's deck
            for (int n = 0; n < (stems[k].Contains("BeachBar") ? 5 : 3); n++)
            {
                var sp = pos + front * (halfW[k] * 0.4f + 2.2f + n * 0.75f) + tan * ((float)rng.NextDouble() - 0.5f) * 3f;
                _pSpots.Add(PS(sp, -front, stems[k].Contains("Gelato") || stems[k].Contains("ShaveIce") ? "IceCreamQueue" : "BarGuest",
                               stems[k].Contains("BeachBar") && (n & 1) == 1 ? MinatoCrowdActor.MotionKind.Sit : MinatoCrowdActor.MotionKind.Idle));
            }
            _ = toRoad;
        }
        Debug.Log($"[nagisa] beach shops (pass 2 kit): {placed}");
    }
}

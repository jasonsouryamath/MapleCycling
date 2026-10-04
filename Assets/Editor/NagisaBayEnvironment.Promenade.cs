// NAGISA BAY premier resort cycling destination, brief sections 6 + 8 (Claude worker F, 2026-10-01).
// Stage "Promenade", order 93.
//
//   * the oceanfront CYCLING PROMENADE (the sea-side walk next to the ride road): public cycling sculptures
//     (steel wheel / breaking-wave ribbons / peloton blades) on stone plinths, sea-view benches on the close-
//     to-water stretches, timber OCEAN OVERLOOK decks with glass rails and coin telescopes, joggers and strolling
//     pairs on short distance-activated walk legs (the existing MinatoCrowdActor walkers: they sleep when far);
//   * the SHIOKAZE PLAZA resort town centre (found on the best free flat sea-side site between the marina and
//     the beach): a paved plaza with a row of low shops (gelato, cafe, bakery, bike shop) carrying Japanese
//     blade signs, vending machines, sandwich boards, bike corrals, parasol tables, benches, palms, a sculpture
//     and a crowd with something to do (queueing, sitting, strolling).
// Nothing stands in the ride line: the walk is >= 7.4 m from the centreline and every spot is occupancy-checked.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    [NagisaStage(93, "Promenade")]
    private static void BuildPromenadeCulture(Transform group)
    {
        NbcInit();
        _nbcLog.Clear(); _nbcPlaced = _nbcSkipped = _nbcPeople = 0;
        var occ = NbcOcc.FromScene(group);
        var cast = NbcCast(group);
        var rng = new System.Random(9301);
        float R() => (float)rng.NextDouble();

        var art = NbcChild(group, "Public art");
        var seats = NbcChild(group, "Sea-view seating");
        var decks = NbcChild(group, "Ocean overlooks");
        var folk = NbcChild(group, "Promenade people");

        // ---- cycling sculptures (a different one every ~560 m)
        string[] sculpt = { "Nagisa_NBC_SculptureWheel", "Nagisa_NBC_SculptureWave", "Nagisa_NBC_SculpturePeloton" };
        int sc = 0;
        foreach (var span in NbcSpans)
            for (float d = span.x + 300f; d < span.y - 100f; d += 560f)
            {
                if (!NbcAt(d, 12.7f, out var p, out var sd, out var tan)) continue;
                // sculptures read best from the road: broad face toward the rider
                var sgo = NbcPut(occ, sculpt[sc % 3], art, p, NbcYaw(tan), 2.8f, null, 1f, 0.002f);
                if (sgo == null) continue;
                if (sc % 3 == 0) NbcMountWheelRing(sgo.transform);
                sc++;
            }
        _nbcLog.Add($"sculptures {sc}");

        // ---- benches facing the sea on the close-to-water stretches, joggers + strollers
        int benches = 0, jog = 0, stroll = 0;
        foreach (var span in NbcSpans)
            for (float d = span.x + 20f; d < span.y - 20f; d += 8f)
            {
                if (!NbcAt(d, 12.0f, out var p, out var sd, out var tan)) continue;
                // distance from the paving's sea edge to the water
                var edge = p + sd * 3.4f;
                float toWater = _ground.Coast(edge.x, edge.z);
                bool close = toWater < 12f;
                if (close && d % 50f < 8f && NbcPut(occ, "Nagisa_Bench", seats, p, NbcYaw(sd) + 180f, 1.4f, null, 1f, SmallPropCull) != null)
                    benches++;
            }
        foreach (var span in NbcSpans)
            for (float d = span.x + 60f; d < span.y - 120f; d += 140f)
            {
                if (!NbcAt(d, 9.2f + R() * 2f, out var a, out _, out _)) continue;
                if (!NbcAt(d + 55f, 9.6f + R() * 1.6f, out var b, out _, out _)) continue;
                if (R() < 0.6f)
                {   // a group of two or three joggers, staggered across the walk
                    int n = 2 + (R() < 0.4f ? 1 : 0);
                    for (int k = 0; k < n; k++)
                    {
                        var off = (b - a).normalized * (-1.1f * k);
                        var side = Vector3.Cross(Vector3.up, (b - a).normalized) * ((k % 2 == 0 ? -0.5f : 0.5f));
                        if (NbcPerson(cast, folk, "PromenadeJogger", a + off + side, b - a, MK.Walk, R(), b + off + side) != null) jog++;
                    }
                }
                else if (NbcPerson(cast, folk, "StrollingPair", a, b - a, MK.Walk, R(), b) != null)
                {
                    stroll++;
                    var pp = Vector3.Cross(Vector3.up, (b - a).normalized) * 0.7f;
                    if (NbcPerson(cast, folk, "StrollingPair", a + pp, b - a, MK.Walk, R(), b + pp) != null) stroll++;
                }
            }
        // overrun the walkers' speed a little so joggers actually jog
        foreach (var act in folk.GetComponentsInChildren<MinatoCrowdActor>(true))
            if (act.name.Contains("Jogger")) act.moveSpeed = 2.55f;
        _nbcLog.Add($"sea-view benches {benches}, joggers {jog}, strolling {stroll}");

        // ---- ocean overlook decks: scan seaward for a stretch where the shore is 4-9 m from a flat edge
        int dk = 0;
        foreach (var span in NbcSpans)
            for (float d = span.x + 150f; d < span.y - 150f && dk < 8; d += 31f)
            {
                if (d % 4f > 31f) continue;
                if (!NbcAt(d, 15.8f, out var p0, out var sd, out var tan)) continue;
                for (float off = 16f; off < 60f; off += 2f)
                {
                    if (!NbcAt(d, off, out var p, out _, out _)) break;
                    float coast = _ground.Coast(p.x, p.z);
                    if (coast < 3.8f) break;
                    if (coast > 10f) continue;
                    // footprint test: 6 x 3.4 deck, sea toward +z
                    if (!NbcRectOk(occ, p + sd * 1.4f, tan, sd, 3.1f, 1.8f, 1.5f, 1.3f, out _, out float hMax)) continue;
                    var pos = new Vector3(p.x, hMax + 0.35f, p.z);
                    if (PlaceWorld("Nagisa_NBC_Overlook", decks, pos, NbcYaw(sd), 1f, 0.003f) == null) break;
                    occ.AddRect(p + sd * 1.2f, 3.4f, 2.2f);
                    dk++; d += 420f;
                    break;
                }
            }
        _nbcLog.Add($"ocean overlook decks {dk}");

        PlazaCentre(group, occ, cast, rng);
        NbcFlushLog("promenade", group);
    }

    // ------------------------------------------------------------------ Shiokaze plaza (resort town centre)
    private static void PlazaCentre(Transform group, NbcOcc occ, NbCast cast, System.Random rng)
    {
        float R() => (float)rng.NextDouble();
        var plazaT = NbcChild(group, "Shiokaze Plaza");
        var shopsT = NbcChild(plazaT, "Shops");
        var propsT = NbcChild(plazaT, "Furniture and signage");
        var folk = NbcChild(plazaT, "Plaza people");

        var sizes = new[] { (30f, 15f), (24f, 13f), (18f, 11f) };
        Vector3 c = default, tan = default, sd = default; float h0 = 0, h1 = 0, hw = 0, hd = 0; bool found = false;
        foreach (var (w, dpt) in sizes)
        {
            foreach (var win in new[] { new Vector2(960f, 2100f), new Vector2(12300f, 14800f), new Vector2(2150f, 4300f) })
                if (NbcFindSite(occ, win.x, win.y, 14f, 90f, w, dpt, 6f, 1.0f, out c, out tan, out sd, out h0, out h1, 4f)) { found = true; break; }
            if (found) { hw = w; hd = dpt; break; }
        }
        if (!found) { _nbcLog.Add("PLAZA: NO SITE FOUND"); return; }

        float topY = h1 + 0.12f;
        NbcPlinth(plazaT, "Plaza paving", c, tan, sd, hw, hd, topY, _nb["NB_PlazaStone"]);
        occ.AddRect(c, hw + 1f, hd + 1f);
        Vector3 W(float u, float z) { var p = c + tan * u + sd * z; return new Vector3(p.x, topY, p.z); }
        float yawSea = NbcYaw(sd), yawRoad = NbcYaw(-sd);

        // row of low shops along the SEAWARD edge, fronts (local -z) toward the road
        var shops = new (string stem, string blade, float hx, float hz)[]
        {
            ("Nagisa_B_Gelato", "icecream", 3.5f, 5.5f), ("Nagisa_B_BeachBar", "cafe", 7.0f, 4.5f),
            ("Nagisa_B_ShaveIce", "bakery", 2.6f, 3.8f), ("Nagisa_B_SurfShop", "bike", 7.3f, 5.5f),
        };
        float cursor = -hw + 1.5f; int placed = 0;
        var shopCentres = new List<(float u, float hz, string blade, float hx)>();
        foreach (var s in shops)
        {
            if (cursor + s.hx * 2f > hw - 1f) continue;
            float u = cursor + s.hx;
            var pos = W(u, hd - s.hz - 0.8f);
            var go = PlaceWorld(s.stem, shopsT, new Vector3(pos.x, topY - 0.04f, pos.z), yawSea, 1f, 0.002f, NbColourway(placed * 3 + 1), true);
            if (go != null) { placed++; shopCentres.Add((u, s.hz, s.blade, s.hx)); }
            cursor += s.hx * 2f + 2.2f;
        }
        // signage + dressing at each shop front
        int vend = 0, boards = 0, bikes = 0;
        for (int k = 0; k < shopCentres.Count; k++)
        {
            var (u, hz, blade, hx) = shopCentres[k];
            float frontZ = hd - 2f * hz - 0.8f;                         // shop front plane (seaward row, front toward -z)
            // projecting blade sign at the shop corner, along the street
            var bp = W(u + hx * 0.55f, frontZ + 0.05f);
            PlaceWorld("Nagisa_NBC_SignBlade", propsT, new Vector3(bp.x, topY, bp.z), NbcYaw(-sd) , 1f, 0.004f, NbcSign("Blade", blade));
            // sandwich board in front
            var ap = W(u - hx * 0.35f, frontZ - 1.1f);
            if (NbcPut(null, "Nagisa_NBC_SignAFrame", propsT, ap, NbcYaw(tan) + R() * 20f - 10f, 0.6f, NbcSign("Aframe", new[] { "coffee", "today", "repair" }[k % 3]), 1f, SmallPropCull, force: true) != null) boards++;
            // vending machine against the wall beside the door
            if (k % 2 == 0)
            {
                var vp = W(u + hx * 0.8f, frontZ - 0.55f);
                if (NbcPut(null, "Nagisa_NBC_Vending", propsT, vp, yawRoad, 0.8f, NbcVend(k % 4 == 0 ? "drinks" : "blue"), 1f, SmallPropCull, force: true) != null) vend++;
            }
            // parked bikes by the door (premium road bikes)
            var sp = W(u - hx * 0.6f, frontZ - 0.9f);
            if (NbcPut(null, "Nagisa_NBC_BikeStand", propsT, sp, NbcYaw(tan), 0.8f, NbcBikeColourway(k * 2), 1f, SmallPropCull, force: true) != null) bikes++;
        }
        // bike corrals + tables + benches + planters on the open plaza
        for (int k = 0; k < 2; k++)
            if (NbcPut(null, "Nagisa_NBC_BikeCorral", propsT, W(-hw * 0.5f + k * hw * 0.9f, -hd * 0.55f), NbcYaw(tan) , 1f, NbcBikeColourway(k * 4 + 3), 1f, 0.004f, force: true) != null) bikes += 5;
        for (int k = 0; k < 5; k++)
        {
            var tp = W(-hw * 0.6f + k * hw * 0.3f, -hd * 0.05f + (k % 2) * 2.4f);
            NbcPut(null, "Nagisa_S_Parasol", propsT, tp, R() * 360f, 1f, null, 1f, SmallPropCull, force: true);
        }
        for (int k = 0; k < 4; k++)
        {
            var bp2 = W(-hw * 0.7f + k * hw * 0.46f, hd * 0.05f);
            NbcPut(null, "Nagisa_Bench", propsT, bp2, yawRoad + (k % 2 == 0 ? 0f : 180f), 1f, null, 1f, SmallPropCull, force: true);
        }
        for (int k = 0; k < 6; k++)
        {
            float u = -hw + 1.2f + k * (hw * 2f - 2.4f) / 5f;
            NbcPut(null, "Nagisa_S_Planter", propsT, W(u, -hd + 0.9f), yawRoad, 1f, null, 1f, SmallPropCull, force: true);
            if (k % 2 == 0) PlaceWorld("Nagisa_CoconutPalm_A", propsT, W(u, -hd + 2.6f), R() * 360f, 0.9f + R() * 0.2f, 0.003f);
        }
        var plazaWheel = PlaceWorld("Nagisa_NBC_SculptureWheel", propsT, W(hw * 0.35f, -hd * 0.62f), NbcYaw(tan), 1f, 0.002f);
        if (plazaWheel != null) NbcMountWheelRing(plazaWheel.transform);
        PlaceWorld("Nagisa_NBC_MapKiosk", propsT, W(-hw * 0.12f, -hd + 1.8f), NbcYaw(sd), 1f, 0.003f);

        // people with a destination: queues at the gelato window, cafe sitters, strollers crossing
        int ppl = 0;
        foreach (var sh in shopCentres)
        {
            float frontZ = hd - 2f * sh.hz - 0.8f;
            for (int k = 0; k < 3; k++)
            {
                var qp = W(sh.u - 0.8f + k * 0.8f, frontZ - 1.8f - k * 0.2f);
                if (NbcPerson(cast, folk, "PlazaGuest", qp, sd * (k % 2 == 0 ? 1f : 0.6f) + tan * 0.3f, MK.Idle, R()) != null) ppl++;
            }
        }
        for (int k = 0; k < 6; k++)
        {
            var a = W(-hw * 0.8f, -hd * 0.3f + k * 1.6f); var b = W(hw * 0.8f, -hd * 0.1f + k * 1.0f);
            if (k % 2 == 1) (a, b) = (b, a);
            if (NbcPerson(cast, folk, "PlazaStroller", a, b - a, MK.Walk, R(), b) != null) ppl++;
        }
        for (int k = 0; k < 5; k++)
        {
            var sp = W(-hw * 0.6f + k * hw * 0.3f, -hd * 0.05f + (k % 2) * 2.4f + 1.1f);
            if (NbcPerson(cast, folk, "CafeSitter", sp, -sd + tan * 0.2f, MK.Idle, R()) != null) ppl++;
        }
        _nbcLog.Add($"PLAZA at ({c.x:0},{c.z:0}) d~{NbcDistOf(c):0} {hw * 2:0}x{hd * 2:0} m: {placed} shops, {boards} boards, {vend} vending, ~{bikes} bikes, {ppl} people");

        // ---- konbini / bakery dressing in the inland town: blade + vending beside each Nagisa_B_Konbini
        int kb = 0;
        var townT = NbcChild(group, "Town konbini dressing");
        foreach (var lg in group.root.GetComponentsInChildren<LODGroup>(true))
        {
            if (lg.name != "Nagisa_B_Konbini" || lg.transform.IsChildOf(group)) continue;
            var q = Quaternion.Euler(0f, lg.transform.eulerAngles.y, 0f);
            var f = q * Vector3.back;                                 // building front (local -z)
            var basePos = lg.transform.position;
            float gy = _ground.Height(basePos.x, basePos.z);
            // blade at the front-left corner, vending machines against the front wall
            var cp = basePos + q * new Vector3(-7.4f, 0f, -11.6f);
            PlaceWorld("Nagisa_NBC_SignBlade", townT, new Vector3(cp.x, gy + 0.2f, cp.z), lg.transform.eulerAngles.y + 90f, 1f, 0.004f, NbcSign("Blade", "konbini"));
            var vpos = basePos + q * new Vector3(5.2f, 0f, -12.4f);
            PlaceWorld("Nagisa_NBC_Vending", townT, new Vector3(vpos.x, gy + 0.2f, vpos.z), lg.transform.eulerAngles.y + 180f, 1f, SmallPropCull, NbcVend("drinks"));
            kb++;
        }
        _nbcLog.Add($"konbini dressed {kb}");
    }
}

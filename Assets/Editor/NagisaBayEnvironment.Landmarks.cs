// NB8C - Nagisa Bay landmarks (stage 98 "Landmarks" via the NB0 hook in NagisaBayEnvironment.Overhaul.cs).
// Breakwater lighthouse (rotating beam, AmbientRotor), ridge golf course (draped fairways/greens/bunkers,
// flags on AmbientSway, golfers, carts on an AmbientPathMover loop), a water park with slides, an east-coast
// fishing pier with anglers, and a hilltop shrine (hall, torii approach, toro + swaying chochin).
// Models: tools/blender/build_nagisa_landmarks_models.py -> Assets/Environment/NagisaBay/Models/Nagisa_NB8C_*.
// The NB0 hook hands us a freshly rebuilt "NB Landmarks" group; every child below is created by EXACT name
// (Converge) so re-runs never duplicate. No colliders, route/road/CorridorKeepOutM untouched.
// Sites are PROVISIONAL candidates validated at runtime (coast, pads, route distance, existing LODGroups).
using System.Collections.Generic;
using UnityEngine;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    // ---- tunables (provisional art/layout values) -------------------------------------------------------
    private const float LmLampY = 21.55f;          // beam pivot above lighthouse origin (matches Blender LH_LAMP_Y)
    private const float LmBeamDegPerSec = 36f;     // one sweep every 10 s
    private const float LmBeamCullM = 3200f;
    private const float LmBeamEmissive = 0.22f;   // keep low: a daylight haze, not a solid cone
    private const float LmBreakwaterDeckY = 2.0f;  // deck above sea level
    private const float LmBreakwaterModuleM = 12f;
    private const float LmPierDeckY = 2.4f;
    private const float LmPierBayM = 8f;
    private const int LmPierBays = 11;
    private const float LmMinRouteM = 110f;        // landmarks (except coastal ones) keep this far from the road
    private const float LmFlagSwayDeg = 14f;
    private const float LmCartSpeed = 4.5f;
    private const float LmShrineSearchM = 150f;  // search radius around a candidate summit
    private const float LmPierMaxShoreM = 560f;  // route -> shoreline march limit (east coast has a wide headland)

    private static readonly Vector2 LmBreakwaterShore = new Vector2(-2230f, -33060f);
    private static readonly Vector2 LmBreakwaterHead = new Vector2(-2500f, -33240f);
    private static readonly Vector2[] LmShrineSites = { new Vector2(0f, -31090f), new Vector2(-600f, -31300f) };
    private static readonly Vector2[] LmGolfSites = { new Vector2(1300f, -31300f), new Vector2(1600f, -31650f) };
    private static readonly Vector2[] LmWaterParkSites =
        { new Vector2(1900f, -33300f), new Vector2(2000f, -32700f), new Vector2(700f, -33400f) };
    private static readonly float[] LmPierRouteM = { 13300f, 13000f, 13600f, 12700f, 13900f, 12500f, 14200f, 12300f, 14500f, 2200f, 3000f };

    private static Dictionary<string, Material> _lmMats;
    private static int _lmPeople;

    [NagisaStage(98, "Landmarks")]
    private static void BuildLandmarksStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0) BuildNbMaterials();   // standalone MR_NB_STAGES run: reload the shared NB_* assets
        if (_nb.Count == 0) { Debug.LogError("[nb8c] Run NagisaBayEnvironment.Apply before Landmarks."); return; }
        _lmMats = LmMaterials();
        _lmPeople = 0;
        var cast = _lastCast ?? NbCrowdCast(group.root);
        string bw = LmBreakwater(LmChild(group, "NB8C Breakwater Lighthouse"));
        string sh = LmShrine(LmChild(group, "NB8C Hilltop Shrine"), cast);
        string gf = LmGolf(LmChild(group, "NB8C Ridge Golf"), cast);
        string wp = LmWaterPark(LmChild(group, "NB8C Water Park"), cast);
        string pr = LmPier(LmChild(group, "NB8C Fishing Pier"), cast);
        Debug.Log($"[nb8c] Landmarks: {bw} | {sh} | {gf} | {wp} | {pr} | people {_lmPeople}");
    }

    private static Transform LmChild(Transform parent, string name) => AmbientMoverStaging.Converge(parent, name).transform;

    private static Dictionary<string, Material> LmMaterials()
    {
        var d = new Dictionary<string, Material>
        {
            ["NB8C_LhWhite"] = NbCel("NB8C_LhWhite", "NB_Wall", new Color(0.97f, 0.96f, 0.93f), 0.3f, 0.2f),
            ["NB8C_LhRed"] = NbCel("NB8C_LhRed", "NB_Wall", new Color(0.74f, 0.13f, 0.10f), 0.35f, 0.22f),
            ["NB8C_LampGlass"] = NbLit("NB8C_LampGlass", new Color(0.82f, 0.92f, 0.96f, 0.35f), 0.95f, 0f, true),
            ["NB8C_Armour"] = NbCel("NB8C_Armour", "NB_Stone", new Color(0.80f, 0.78f, 0.74f), 0.12f, 0.08f),
            ["NB8C_Black"] = Cel("Nagisa_NB8C_Black", new Color(0.07f, 0.07f, 0.075f), 0.35f, 0.2f),
            ["NB8C_Vermilion"] = NbCel("NB8C_Vermilion", null, new Color(0.86f, 0.25f, 0.11f), 0.35f, 0.2f),
            ["NB8C_Copper"] = NbCel("NB8C_Copper", "NB_MetalRoof", new Color(0.40f, 0.64f, 0.54f), 0.3f, 0.25f),
            ["NB8C_Gold"] = NbCel("NB8C_Gold", null, new Color(0.86f, 0.67f, 0.26f), 0.6f, 0.5f),
            ["NB8C_Paper"] = NbCel("NB8C_Paper", null, new Color(0.96f, 0.93f, 0.85f), 0.15f, 0.08f),
            ["NB8C_LanternGlow"] = NbGlow("NB8C_LanternGlow", new Color(1f, 0.74f, 0.42f)),
            ["NB8C_Chochin"] = NbCel("NB8C_Chochin", null, new Color(0.90f, 0.20f, 0.12f), 0.3f, 0.15f),
            ["NB8C_Flag"] = NbCel("NB8C_Flag", null, new Color(0.96f, 0.84f, 0.12f), 0.2f, 0.1f, 0.14f, 0.8f, 0f),
            ["NB8C_CartBody"] = NbCel("NB8C_CartBody", null, new Color(0.95f, 0.95f, 0.92f), 0.5f, 0.35f),
            ["NB8C_SlideSteel"] = NbCel("NB8C_SlideSteel", null, new Color(0.74f, 0.76f, 0.79f), 0.5f, 0.45f),
            ["NB8C_SlideYellow"] = NbCel("NB8C_SlideYellow", null, new Color(0.98f, 0.78f, 0.12f), 0.6f, 0.4f, 0.14f, 0.8f, 0f),
            ["NB8C_SlideBlue"] = NbCel("NB8C_SlideBlue", null, new Color(0.12f, 0.45f, 0.86f), 0.6f, 0.4f, 0.14f, 0.8f, 0f),
            ["NB8C_SlideRed"] = NbCel("NB8C_SlideRed", null, new Color(0.88f, 0.16f, 0.14f), 0.6f, 0.4f, 0.14f, 0.8f, 0f),
            ["NB8C_Canopy"] = NbCel("NB8C_Canopy", null, new Color(0.10f, 0.56f, 0.74f), 0.25f, 0.12f, 0.14f, 0.8f, 0f),
            ["NB8C_Fairway"] = NbCel("NB8C_Fairway", "NB_Lawn", new Color(0.60f, 0.80f, 0.44f), 0.16f, 0.06f),
            ["NB8C_Green"] = NbCel("NB8C_Green", "NB_Lawn", new Color(0.56f, 0.86f, 0.42f), 0.22f, 0.08f),
            ["NB8C_Bunker"] = Cel("Nagisa_NB8C_Bunker", new Color(0.93f, 0.86f, 0.68f), 0.1f, 0.05f),
            ["NB8C_PathStone"] = NbCel("NB8C_PathStone", "NB_PlazaStone", new Color(0.92f, 0.90f, 0.86f), 0.15f, 0.1f),
        };
        // Beam: transparent HDRP/Lit with a warm emissive so it reads at dusk without an Unlit shader.
        var beam = NbLit("NB8C_Beam", new Color(1f, 0.95f, 0.8f, 0.07f), 0f, 0f, true);
        beam.SetColor("_EmissiveColor", new Color(1f, 0.9f, 0.65f) * LmBeamEmissive);   // HDRP adds emission regardless of alpha
        beam.SetFloat("_UseEmissiveIntensity", 0f);
        UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(beam);
        d["NB8C_Beam"] = beam;
        foreach (var kv in _nb) if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
        return d;
    }

    private static GameObject LmPlace(string stem, Transform parent, Vector3 world, float yaw, float cull = 0.0015f, bool hero = false)
        => PlaceWorld(stem, parent, world, yaw, 1f, cull, _lmMats, hero);

    private static Vector3 LmToRoad(Vector3 w)
    {
        _route.PlanDistance(w.x, w.z, out int i); var d = _route.Position[i] - w; d.y = 0f; return d.normalized;
    }

    private static float LmYaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    private static bool LmFree(Transform root, Vector3 p, float r, float padMargin = 8f)
        => !InAnyPad(p.x, p.z, padMargin) && Nb13Unoccupied(root, p, r);

    // ================================================================= breakwater + lighthouse
    private static string LmBreakwater(Transform g)
    {
        var a = new Vector3(LmBreakwaterShore.x, 0f, LmBreakwaterShore.y);
        var b = new Vector3(LmBreakwaterHead.x, 0f, LmBreakwaterHead.y);
        var dir = (b - a).normalized; float len = (b - a).magnitude, yaw = LmYaw(dir);
        // start where the ground drops below the deck, so the root is never buried in the beach
        float s0 = 0f;
        while (s0 < len * 0.5f && _ground.Height(a.x + dir.x * s0, a.z + dir.z * s0) > LmBreakwaterDeckY - 0.6f) s0 += 2f;
        int n = 0; float s = s0 + LmBreakwaterModuleM * 0.5f;
        for (; s < len; s += LmBreakwaterModuleM, n++)
        {
            var p = a + dir * s; p.y = LmBreakwaterDeckY;
            LmPlace("Nagisa_NB8C_Breakwater", g, p, yaw).name = $"NB8C Breakwater {n:00}";
        }
        var head = a + dir * (s - LmBreakwaterModuleM * 0.5f + 5f); head.y = LmBreakwaterDeckY;
        LmPlace("Nagisa_NB8C_BreakwaterHead", g, head, yaw).name = "NB8C Breakwater Head";
        var lh = LmPlace("Nagisa_NB8C_Lighthouse", g, head, yaw + 180f, 0.0008f, true);
        if (lh == null) return "breakwater: lighthouse GLB missing";
        lh.name = "NB8C Lighthouse";
        var beam = LmPlace("Nagisa_NB8C_LighthouseBeam", lh.transform, head + Vector3.up * LmLampY, yaw, 0.0004f);
        beam.name = "NB8C Lighthouse Beam";
        foreach (var r in beam.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var rotor = AmbientMoverStaging.StageRotor(g, "NB8C Lighthouse Beam Rotor", new[] { beam.transform },
                                                   Vector3.up, LmBeamDegPerSec, 0f, 0f, 81);
        rotor.cullDistance = LmBeamCullM;
        _route.PlanDistance(head.x, head.z, out int hi);
        return $"breakwater {n} modules {len - s0:0} m, lighthouse at ({head.x:0},{head.z:0}) route {_route.Distance[hi]:0} m";
    }

    // ================================================================= hilltop shrine
    private static string LmShrine(Transform g, NbCast cast)
    {
        foreach (var site in LmShrineSites)
        {
            // highest FREE spot (backdrop villas may already crown the summit) within LmShrineSearchM
            Vector3 P = new Vector3(site.x, -999f, site.y);
            for (float dx = -LmShrineSearchM; dx <= LmShrineSearchM; dx += 6f)
                for (float dz = -LmShrineSearchM; dz <= LmShrineSearchM; dz += 6f)
                {
                    float x = site.x + dx, z = site.y + dz, h = _ground.Height(x, z);
                    if (h <= P.y || _ground.Coast(x, z) < 40f || _route.PlanDistance(x, z, out _) < LmMinRouteM) continue;
                    var w = new Vector3(x, h, z);
                    if (LmFree(g.root, w, 20f) && LmFree(g.root, w + LmToRoad(w) * 30f, 14f)) P = w;
                }
            if (P.y < -900f) { Debug.Log($"[nb8c] shrine reject site ({site.x:0},{site.y:0}): no free spot"); continue; }
            float rd = _route.PlanDistance(P.x, P.z, out int ri);
            var toRoad = _route.Position[ri] - P; toRoad.y = 0f; toRoad.Normalize();
            float yaw = LmYaw(toRoad);
            var hall = LmPlace("Nagisa_NB8C_ShrineHall", g, P + Vector3.down * 0.3f, yaw, 0.0008f, true);
            if (hall == null) return "shrine: GLB missing";
            hall.name = "NB8C Shrine Hall";
            var side = Vector3.Cross(Vector3.up, toRoad);
            var path = new List<Vector2>();
            var hangers = new List<Transform>();
            int torii = 0, toro = 0;
            for (float t = 10f; t <= 58f; t += 2f)
            {
                var q = P + toRoad * t; path.Add(new Vector2(q.x, q.z));
                if (_route.PlanDistance(q.x, q.z, out _) < 60f) break;
                float gy = _ground.Height(q.x, q.z);
                if (Mathf.Abs(t % 12f - 10f) < 0.01f || t == 10f)
                {
                    var tg = LmPlace("Nagisa_NB8C_Torii", g, new Vector3(q.x, gy - 0.15f, q.z), yaw);
                    if (tg == null) continue;
                    tg.name = $"NB8C Torii {torii:00}";
                    var ch = LmPlace("Nagisa_NB8C_Chochin", tg.transform, new Vector3(q.x, gy - 0.15f + 5.12f, q.z), yaw);
                    if (ch != null) { ch.name = $"NB8C Chochin {torii:00}"; hangers.Add(ch.transform); }
                    torii++;
                }
                else if (Mathf.Abs(t % 12f - 4f) < 0.01f)
                {
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var l = q + side * (sgn * 3.6f);
                        var tl = LmPlace("Nagisa_NB8C_Toro", g, new Vector3(l.x, _ground.Height(l.x, l.z) - 0.1f, l.z), yaw);
                        if (tl != null) { tl.name = $"NB8C Toro {toro:00}"; toro++; }
                    }
                }
            }
            if (path.Count > 1) AddMesh(g, "NB8C Shrine Approach", LmRibbon("NB8C_ShrineApproach", path, 1.7f, 0.22f, 3f), _lmMats["NB8C_PathStone"], false);
            if (hangers.Count > 0)
                AmbientMoverStaging.StageSway(g, "NB8C Chochin Sway", hangers.ToArray(), Vector3.right, 5f, 0.45f,
                                              AmbientSway.SwayMode.Noise, default, 82);
            int ppl = 0;
            for (int k = 0; k < 4; k++)
            {
                var q = P + toRoad * (12f + k * 9f) + side * ((k % 2 == 0 ? 1f : -1f) * 1.1f);
                q.y = _ground.Height(q.x, q.z);
                var face = k % 2 == 0 ? -toRoad : side;
                if (LmPerson(cast.stand, g, "Shrine visitor", q, face, 830 + k) != null) ppl++;
            }
            return $"shrine at ({P.x:0},{P.y:0},{P.z:0}) {rd:0} m from road: {torii} torii, {toro} toro, {ppl} visitors";
        }
        return "shrine: no free site";
    }

    // ================================================================= ridge golf course
    private static string LmGolf(Transform g, NbCast cast)
    {
        var holes = new[] { (new Vector2(-150f, -80f), new Vector2(60f, -130f)),
                            (new Vector2(90f, -60f), new Vector2(170f, 120f)),
                            (new Vector2(110f, 170f), new Vector2(-120f, 70f)) };
        foreach (var site in LmGolfSites)
        {
            var ok = new List<(Vector2 tee, Vector2 grn)>();
            foreach (var h in holes)
            {
                Vector2 tee = site + h.Item1, grn = site + h.Item2; bool good = true;
                for (float t = 0f; t <= 1.001f && good; t += 0.05f)
                {
                    var q = Vector2.Lerp(tee, grn, t);
                    var w = new Vector3(q.x, _ground.Height(q.x, q.y), q.y);
                    good = _route.PlanDistance(q.x, q.y, out _) > LmMinRouteM && _ground.Coast(q.x, q.y) > 30f
                           && LmFree(g.root, w, 20f, 12f);
                }
                if (good) ok.Add((tee, grn));
            }
            if (ok.Count < 2) continue;
            var flags = new List<Transform>();
            var cartLoop = new List<Vector2>();
            int golfers = 0;
            for (int k = 0; k < ok.Count; k++)
            {
                var (tee, grn) = ok[k];
                var fw = (grn - tee).normalized; var pr = new Vector2(fw.y, -fw.x);
                var hg = LmChild(g, $"NB8C Hole {k + 1}");
                var fair = new List<Vector2>();
                for (float t = 0.12f; t <= 0.9f; t += 0.03f) fair.Add(Vector2.Lerp(tee, grn, t) + pr * Mathf.Sin(t * 3.1f) * 8f);
                AddMesh(hg, $"NB8C Fairway {k + 1}", LmRibbon($"NB8C_Fairway{k + 1}", fair, 13f, 0.22f, 8f), _lmMats["NB8C_Fairway"], false);
                AddMesh(hg, $"NB8C Tee {k + 1}", LmBlob($"NB8C_Tee{k + 1}", tee, 5f, 7f, fw, 0.26f), _lmMats["NB8C_Green"], false);
                AddMesh(hg, $"NB8C Green {k + 1}", LmBlob($"NB8C_Green{k + 1}", grn, 13f, 11f, fw, 0.28f), _lmMats["NB8C_Green"], false);
                AddMesh(hg, $"NB8C Bunker {k + 1}a", LmBlob($"NB8C_Bunker{k + 1}a", grn + pr * 16f - fw * 3f, 4f, 7f, fw, 0.32f), _lmMats["NB8C_Bunker"], false);
                AddMesh(hg, $"NB8C Bunker {k + 1}b", LmBlob($"NB8C_Bunker{k + 1}b", grn - pr * 15f + fw * 4f, 3.5f, 6f, fw, 0.32f), _lmMats["NB8C_Bunker"], false);
                var fb = Vector2.Lerp(tee, grn, 0.58f) + pr * 17f;
                AddMesh(hg, $"NB8C Bunker {k + 1}c", LmBlob($"NB8C_Bunker{k + 1}c", fb, 5f, 9f, fw, 0.3f), _lmMats["NB8C_Bunker"], false);
                var pin = grn + fw * 3f + pr * 2f;
                var pinW = new Vector3(pin.x, _ground.Height(pin.x, pin.y) + 0.2f, pin.y);
                var pg = LmPlace("Nagisa_NB8C_GolfPin", hg, pinW, LmYaw(new Vector3(fw.x, 0, fw.y)));
                if (pg != null)
                {
                    pg.name = $"NB8C Pin {k + 1}";
                    var fl = LmPlace("Nagisa_NB8C_GolfFlag", pg.transform, pinW + Vector3.up * 2.3f, 60f + k * 20f);
                    if (fl != null) { fl.name = $"NB8C Flag {k + 1}"; flags.Add(fl.transform); }
                }
                // golfers: two on the tee, one on the green
                var tw = new Vector3(tee.x, 0f, tee.y); var fw3 = new Vector3(fw.x, 0f, fw.y); var pr3 = new Vector3(pr.x, 0f, pr.y);
                foreach (var (p, face, seed) in new[] { (tw + pr3 * 1.2f, fw3, 840 + k), (tw - pr3 * 3.5f - fw3 * 2f, pr3, 850 + k),
                                                        (new Vector3(grn.x, 0f, grn.y) - fw3 * 6f, fw3, 860 + k) })
                {
                    var q = p; q.y = _ground.Height(q.x, q.z) + 0.25f;
                    if (LmPerson(cast.stand, hg, "Golfer", q, face, seed) != null) golfers++;
                }
                // cart path: along the hole's side from tee to green
                for (float t = 0f; t <= 1.001f; t += 0.1f) cartLoop.Add(Vector2.Lerp(tee, grn, t) + pr * -24f);
            }
            if (flags.Count > 0)
                AmbientMoverStaging.StageSway(g, "NB8C Flag Sway", flags.ToArray(), Vector3.up, LmFlagSwayDeg, 0.9f,
                                              AmbientSway.SwayMode.Noise, default, 84);
            // carts: paved cart path loop + two moving carts + one parked at hole 1
            var path = LmResample(cartLoop, 5f, true);
            AddMesh(g, "NB8C Cart Path", LmRibbon("NB8C_CartPath", path, 1.3f, 0.24f, 3f), _lmMats["NB8C_PathStone"], false);
            var carts = new List<Transform>();
            for (int c = 0; c < 2; c++)
            {
                var cg = LmPlace("Nagisa_NB8C_GolfCart", g, new Vector3(path[0].x, _ground.Height(path[0].x, path[0].y) + 0.26f, path[0].y), 0f);
                if (cg != null) { cg.name = $"NB8C Cart {c + 1}"; carts.Add(cg.transform); }
            }
            var pts = new Vector3[path.Count + 1];
            for (int k = 0; k < path.Count; k++) pts[k] = new Vector3(path[k].x, _ground.Height(path[k].x, path[k].y) + 0.26f, path[k].y);
            pts[path.Count] = pts[0];
            if (carts.Count > 0)
            {
                float total = 0f; for (int k = 1; k < pts.Length; k++) total += Vector3.Distance(pts[k - 1], pts[k]);
                var mover = AmbientMoverStaging.StagePathMover(g, "NB8C Cart Loop", pts, carts.ToArray(),
                    new[] { 0f, total * 0.5f }, null, LmCartSpeed, 1.5f, AmbientPathMover.PathMode.Loop, 0f, 0f, 0f, 0f, 0f, 85);
                mover.cullDistance = 1200f;
            }
            var t0 = ok[0].tee; var d0 = (ok[0].grn - t0).normalized; var p0 = t0 - new Vector2(d0.y, -d0.x) * 8f - d0 * 4f;
            var park = LmPlace("Nagisa_NB8C_GolfCart", g, new Vector3(p0.x, _ground.Height(p0.x, p0.y) + 0.26f, p0.y), LmYaw(new Vector3(d0.x, 0, d0.y)) + 90f);
            if (park != null) park.name = "NB8C Cart Parked";
            return $"golf at ({site.x:0},{site.y:0}): {ok.Count} holes, {flags.Count} flags, {carts.Count}+1 carts, {golfers} golfers";
        }
        return "golf: no free site";
    }

    // ================================================================= water park
    private static readonly Vector2[] LmWpStand =
        { new Vector2(-30f, -3f), new Vector2(-14f, -2f), new Vector2(-2f, 6f), new Vector2(24f, 9f), new Vector2(30f, -10f),
          new Vector2(-22f, 33f), new Vector2(12f, -28f), new Vector2(40f, 20f) };

    private static string LmWaterPark(Transform g, NbCast cast)
    {
        foreach (var site in LmWaterParkSites)
        {
            float rd = _route.PlanDistance(site.x, site.y, out int ri);
            if (rd < LmMinRouteM) continue;
            var tan = _route.Position[Mathf.Min(ri + 1, _route.Position.Length - 1)] - _route.Position[Mathf.Max(ri - 1, 0)];
            tan.y = 0f; float yaw = LmYaw(tan.normalized) + 90f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float lo = float.MaxValue, hi = float.MinValue, minCoast = float.MaxValue;
            for (float x = -48f; x <= 48.1f; x += 8f)
                for (float z = -36f; z <= 36.1f; z += 8f)
                {
                    var w = new Vector3(site.x, 0f, site.y) + rot * new Vector3(x, 0f, z);
                    float h = _ground.Height(w.x, w.z); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                    minCoast = Mathf.Min(minCoast, _ground.Coast(w.x, w.z));
                }
            var c = new Vector3(site.x, hi + 0.3f, site.y);
            if (hi - lo > 5.5f || minCoast < 25f || !LmFree(g.root, c, 70f, 50f)) continue;
            var wpMats = new Dictionary<string, Material>(_lmMats)
            {   // warm sun-deck tone: the shared NB_DeckStone reads blown-out white over a 96 x 72 m slab
                ["NB_DeckStone"] = NbCel("NB8C_ParkDeck", "NB_DeckStone", new Color(0.80f, 0.72f, 0.60f), 0.14f, 0.08f)
            };
            var wp = PlaceWorld("Nagisa_NB8C_WaterPark", g, c, yaw, 1f, 0.0008f, wpMats, true);
            if (wp == null) return "water park: GLB missing";
            wp.name = "NB8C Water Park Deck";
            int ppl = 0;
            for (int k = 0; k < LmWpStand.Length; k++)
            {
                var q = c + rot * new Vector3(LmWpStand[k].x, 0f, LmWpStand[k].y);
                var face = rot * Quaternion.Euler(0f, k * 67f, 0f) * Vector3.forward;
                if (LmPerson(cast.stand, g, "Water park guest", q, face, 870 + k) != null) ppl++;
            }
            for (int k = 0; k < 3; k++)
            {
                var a = c + rot * new Vector3(-44f + k * 3f, 0f, -33f + k * 2f);
                var b = c + rot * new Vector3(44f - k * 4f, 0f, -33f + k * 2f);
                if (cast.walk.Count > 0 && Walker(cast, g, "Water park walker", a, b, 0.31f + k * 0.17f, 1.1f) != null) ppl++;
            }
            return $"water park at ({site.x:0},{c.y:0},{site.y:0}) {rd:0} m from road, relief {hi - lo:0.0} m, {ppl} guests";
        }
        return "water park: no free site";
    }

    // ================================================================= east-coast fishing pier
    private static string LmPier(Transform g, NbCast cast)
    {
        foreach (float m in LmPierRouteM)
        {
            int i = _route.IndexAt(m);
            var p = _route.Position[i]; var sd = _route.SideFlat(i) * SeaSign(i); sd.y = 0f; sd.Normalize();
            float shore = -1f;
            for (float t = 10f; t < LmPierMaxShoreM; t += 3f)
                if (_ground.Coast(p.x + sd.x * t, p.z + sd.z * t) < 0f) { shore = t; break; }
            if (shore < 45f) { Debug.Log($"[nb8c] pier reject {m:0}: shore {shore:0}"); continue; }
            // deck starts where the ground falls below it; pier then runs straight out to sea
            float s = shore - 12f;
            while (s < shore + 10f && _ground.Height(p.x + sd.x * s, p.z + sd.z * s) > LmPierDeckY - 0.7f) s += 1f;
            var start = p + sd * s; start.y = 0f;
            var end = start + sd * (LmPierBays * LmPierBayM + 5f);
            if (_ground.Height(end.x, end.z) > -2.5f || !LmFree(g.root, start + sd * 40f, 30f, 10f)) { Debug.Log($"[nb8c] pier reject {m:0}: end h {_ground.Height(end.x, end.z):0.0} free {LmFree(g.root, start + sd * 40f, 30f, 10f)} p ({p.x:0},{p.z:0}) sd ({sd.x:0.00},{sd.z:0.00})"); continue; }
            float yaw = LmYaw(sd);
            var bays = new List<Vector3>();
            for (int b = 0; b < LmPierBays; b++)
            {
                var q = start + sd * (LmPierBayM * (b + 0.5f)); q.y = LmPierDeckY;
                var go = LmPlace("Nagisa_NB8C_PierBay", g, q, yaw); if (go == null) return "pier: GLB missing";
                go.name = $"NB8C Pier Bay {b:00}"; bays.Add(q);
            }
            var headC = start + sd * (LmPierBays * LmPierBayM + 5f); headC.y = LmPierDeckY;
            LmPlace("Nagisa_NB8C_PierHead", g, headC, yaw).name = "NB8C Pier Head";
            var side = Vector3.Cross(Vector3.up, sd);
            int anglers = 0;
            // anglers along both rails of the outer bays and on the T-head's seaward rail
            var spots = new List<(Vector3 pos, Vector3 outDir)>();
            for (int b = 4; b < LmPierBays; b += 2) spots.Add((bays[b] + side * ((b / 2) % 2 == 0 ? 1.55f : -1.55f), (b / 2) % 2 == 0 ? side : -side));
            foreach (float x in new[] { -5.5f, -1.5f, 3.5f, 6.5f }) spots.Add((headC + side * x + sd * 4.4f, sd));
            for (int k = 0; k < spots.Count; k++)
            {
                var (pos, outDir) = spots[k];
                var a = LmPerson(cast.stand, g, "Angler", pos, outDir, 890 + k);
                if (a == null) continue;
                anglers++;
                var rod = LmPlace("Nagisa_NB8C_Rod", g, pos + outDir * 0.35f + Vector3.up * 0.95f, LmYaw(outDir));
                if (rod != null) rod.name = $"NB8C Rod {k:00}";
                var cool = LmPlace("Nagisa_NB8C_Cooler", g, pos - outDir * 0.6f + sd * 0.9f, LmYaw(sd) + k * 23f);
                if (cool != null) cool.name = $"NB8C Cooler {k:00}";
            }
            return $"fishing pier at route {m:0} m, shore {shore:0} m seaward, {LmPierBays} bays + head, {anglers} anglers";
        }
        return "fishing pier: no valid east-coast site";
    }

    // ================================================================= diagnostics capture
    /// <summary>Batch capture: one frame per landmark into NagisaBayDiagnostics.OutDir (diag_nagisa_nb8c_*.png).</summary>
    public static void CaptureLandmarks()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = regions != null ? regions.currentRegionId : null;
        if (regions != null)
        {
            regions.Resolve(); regions.currentRegionId = RegionId;
            regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience();
        }
        var r = NagisaRoute.Load(); var gr = NagisaGround.Load();
        System.IO.Directory.CreateDirectory(NagisaBayDiagnostics.OutDir);
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        try
        {
            var shots = new (string obj, string file, float dist, float h, float aimY)[]
            {
                ("NB8C Lighthouse", "diag_nagisa_nb8c_1_lighthouse.png", 95f, 18f, 12f),
                ("NB8C Shrine Hall", "diag_nagisa_nb8c_2_shrine.png", 50f, 10f, 4f),
                ("NB8C Pin 1", "diag_nagisa_nb8c_3_golf.png", 120f, 45f, 0f),
                ("NB8C Water Park Deck", "diag_nagisa_nb8c_4_waterpark.png", 105f, 45f, 2f),
                ("NB8C Pier Head", "diag_nagisa_nb8c_5_pier.png", 70f, 16f, 1f),
            };
            foreach (var s in shots)
            {
                Transform t = null;
                foreach (var x in all) if (x.name == s.obj) { t = x; break; }
                if (t == null) { Debug.LogWarning($"[nb8c] capture: '{s.obj}' not found"); continue; }
                var T = t.position;
                r.PlanDistance(T.x, T.z, out int i);
                var toRoad = r.Position[i] - T; toRoad.y = 0f; toRoad.Normalize();
                var side = Vector3.Cross(Vector3.up, toRoad);
                var eye = T + (toRoad + side * 0.45f).normalized * s.dist + Vector3.up * s.h;
                eye.y = Mathf.Max(eye.y, gr.Height(eye.x, eye.z) + s.h * 0.6f);
                NagisaBayDiagnostics.Shot(eye, T + Vector3.up * s.aimY, 50f, s.file);
            }
        }
        finally
        {
            if (regions != null && !string.IsNullOrEmpty(previous))
            { regions.currentRegionId = previous; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
        }
        Debug.Log("[nb8c] captured landmark frames.");
    }

    // ================================================================= helpers
    private static GameObject LmPerson(List<MinatoCrowdActor> pool, Transform parent, string role, Vector3 pos, Vector3 face, int seed)
    {
        var go = NbPerson(pool, parent, role, pos, face, MK.Idle, seed * 0.6180339f);
        if (go == null) return null;
        go.name = $"NB8C {role} {_lmPeople:00}";
        FinishPerson(go); _lmPeople++;
        return go;
    }

    private static List<Vector2> LmResample(List<Vector2> pts, float step, bool closed)
    {
        var src = new List<Vector2>(pts); if (closed) src.Add(pts[0]);
        var o = new List<Vector2> { src[0] };
        for (int k = 1; k < src.Count; k++)
        {
            float L = Vector2.Distance(src[k - 1], src[k]); int n = Mathf.Max(1, Mathf.CeilToInt(L / step));
            for (int j = 1; j <= n; j++) o.Add(Vector2.Lerp(src[k - 1], src[k], j / (float)n));
        }
        return o;
    }

    /// <summary>Terrain-draped ribbon along a plan polyline (resampled every ~3 m, across every ~3 m).</summary>
    private static Mesh LmRibbon(string name, List<Vector2> line, float halfW, float lift, float tile)
    {
        var pts = LmResample(line, 3f, false);
        int nx = Mathf.Max(1, Mathf.CeilToInt(halfW * 2f / 3f));
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        float along = 0f;
        for (int k = 0; k < pts.Count; k++)
        {
            var dir = (pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized;
            var pr = new Vector2(dir.y, -dir.x);
            if (k > 0) along += Vector2.Distance(pts[k - 1], pts[k]);
            for (int j = 0; j <= nx; j++)
            {
                float u = -halfW + 2f * halfW * j / nx;
                var q = pts[k] + pr * u;
                v.Add(new Vector3(q.x, _ground.Height(q.x, q.y) + lift, q.y));
                uv.Add(new Vector2(q.x / tile, q.y / tile));
            }
        }
        for (int k = 0; k < pts.Count - 1; k++)
            for (int j = 0; j < nx; j++)
            {
                int a = k * (nx + 1) + j, b = a + 1, c = a + nx + 1, d = c + 1;
                LmQuad(v, tri, a, c, d, b);
            }
        return Finish(name, v, uv, tri);
    }

    /// <summary>Terrain-draped ellipse (rx across, rz along the forward vector).</summary>
    private static Mesh LmBlob(string name, Vector2 c, float rx, float rz, Vector2 fwd, float lift)
    {
        var pr = new Vector2(fwd.y, -fwd.x);
        const int seg = 24, rings = 4;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        v.Add(new Vector3(c.x, _ground.Height(c.x, c.y) + lift, c.y)); uv.Add(c / 4f);
        for (int r = 1; r <= rings; r++)
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg, f = r / (float)rings;
                float wob = 1f + 0.08f * Mathf.Sin(a * 3f + c.x * 0.1f);
                var q = c + pr * (Mathf.Cos(a) * rx * f * wob) + fwd * (Mathf.Sin(a) * rz * f * wob);
                v.Add(new Vector3(q.x, _ground.Height(q.x, q.y) + lift, q.y)); uv.Add(q / 4f);
            }
        for (int s = 0; s < seg; s++) LmTri(v, tri, 0, 1 + s, 1 + (s + 1) % seg);
        for (int r = 1; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = 1 + (r - 1) * seg + s, b = 1 + (r - 1) * seg + (s + 1) % seg;
                LmQuad(v, tri, a, a + seg, b + seg, b);
            }
        return Finish(name, v, uv, tri);
    }

    private static void LmQuad(List<Vector3> v, List<int> tri, int a, int b, int c, int d)
    { LmTri(v, tri, a, b, c); LmTri(v, tri, a, c, d); }

    /// <summary>Adds an up-facing triangle (Unity front face = normal toward viewer).</summary>
    private static void LmTri(List<Vector3> v, List<int> tri, int a, int b, int c)
    {
        if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y >= 0f) { tri.Add(a); tri.Add(b); tri.Add(c); }
        else { tri.Add(a); tri.Add(c); tri.Add(b); }
    }
}

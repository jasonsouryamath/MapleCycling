// NB-Open - Opening-stretch beach (copilot, maplerider-builder).
//
// User: "on the Nagisa Bay route, the beginning stretch of the ride looks like just desolate green".
// On 0 .. BeachStartM (marina front + waterfront town) the sea is on the rider's RIGHT, 41-100 m out,
// but the baked ground classes are grass there (build_nagisa_route.py only paints sand for gx > -1150)
// and NB2/NB6 beach dressing only covers BeachStartM..BeachEndM. This stage ADDS, sea side only:
//   1. a terrain-conforming SAND ribbon (Shiosai sand texture, same approach as Minato's
//      Minato_City_BeachSand) from just past the road keep-out - or behind Town2's ROW S beach-house
//      lawns on d 950-2080 - down to the waterline, plus a darker WET-SAND band that dips under the sea;
//      both are "Beach Sand walk layer" colliders so crowd actors stand on them;
//   2. swash + breaker foam and shoreline walkers (reuses NB6's BuildNb6Shoreline / NagisaBeachActivity.Swash);
//   3. umbrella / parasol / lounger / towel sets with sunbathers and standing beachgoers, a few palms and
//      lifeguard towers (NB2 props via PlaceWorld, NB6 people helpers);
//   4. runners/joggers looping the wet sand and the dry sand by the road (NagisaBeachActivity.Glide).
// Nothing is placed inside CorridorKeepOutM, on marina pads, or on top of existing props/people.
// ALL numbers are PROVISIONAL art tuning.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    // ---------------------------------------------------------------- PROVISIONAL tuning
    private const float NbOpenD0 = 20f;                 // opening beach starts here (route m)
    private const float NbOpenInnerM = RoadHalfWidth + ShoulderWidth + 0.5f; // sand starts just past the road shoulder (props still honour CorridorKeepOutM)
    private const float NbOpenRowSGapM = 1.5f;          // behind ROW S lawns
    private const float NbOpenStepM = 4f;               // ribbon row spacing along the route
    private const int NbOpenDryLanes = 28, NbOpenWetLanes = 6;
    private const float NbOpenWetM = 5.5f;              // wet band width landward of the waterline
    private const float NbOpenUnderM = 4f;              // wet band continues this far under the sea
    private const float NbOpenLift = 0.08f;             // dry sand above the terrain
    private const float NbOpenChunkM = 160f;            // mesh chunk length (culling)
    private const float NbOpenMinBeachM = 10f;          // need this much sand between inner edge and waterline
    private const float NbOpenUmbStepM = 10f, NbOpenUmbRowM = 8.5f, NbOpenUmbFill = 0.62f;
    private const int NbOpenUmbMaxRows = 4;
    private const float NbOpenUmbFirstM = 5f;           // first umbrella row beyond the sand's inner edge
    private const float NbOpenUmbWetKeepM = 9f;
    private const float NbOpenBeachgoerChance = 0.22f;
    private const float NbOpenPalmEveryM = 26f, NbOpenPalmChance = 0.55f;
    private const float NbOpenTowerEveryM = 520f;
    private const float NbOpenRunnerEveryM = 55f;       // one runner per this much glide loop

    [NagisaStage(65, "OpeningBeach")]
    private static void BuildOpeningBeachStage(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0 || _nbColourRemaps[0] == null)
        {
            Debug.LogError("[nb-open] NB materials not prepared - run the full NagisaBayEnvironment.Apply.");
            return;
        }
        var root = group.root;
        var cast = _lastCast ?? NbCrowdCast(root);
        _nb6Hash.Clear(); _nb6BoardMesh = null; _nb6WheelMesh = null;
        _nb6Patch.v.Clear(); _nb6Patch.uv.Clear(); _nb6Patch.t.Clear();
        foreach (var lg in root.GetComponentsInChildren<LODGroup>(true)) Nb6Occupy(lg.transform.position);
        foreach (var a in root.GetComponentsInChildren<MinatoCrowdActor>(true)) Nb6Occupy(a.transform.position);

        var rng = new System.Random(6565);
        float R() => (float)rng.NextDouble();
        int people0 = _nbPeople;

        // ROW S (Town2) back edge: houses + lawns reach O_Coast0 + 7 + 2*hz + 8 seaward
        float rowSHz = 0f;
        foreach (var h in Houses) { var f = FootOf(h); if (f != null) rowSHz = Mathf.Max(rowSHz, f.hz); }
        if (rowSHz <= 0f) rowSHz = 8f;
        float rowSBack = O_Coast0 + 7f + 2f * rowSHz + 8f + NbOpenRowSGapM;

        // --- per-sample profile: inner edge + waterline
        int i0 = _route.IndexAt(NbOpenD0), i1 = _route.IndexAt(_route.BeachStartM);
        int n = i1 - i0 + 1;
        var shore = new float[n]; var inner = new float[n]; var ok = new bool[n];
        for (int k = 0; k < n; k++)
        {
            int i = i0 + k; float d = _route.Distance[i];
            shore[k] = -1f;
            inner[k] = (d > RowSD0 - 6f && d < RowSD1 + 6f) ? rowSBack : NbOpenInnerM;
            if (_route.OnBridge(i)) continue;
            var p = _route.Position[i]; var sd = Nb6Sd(i);
            for (float o = NbOpenInnerM; o < 320f; o += 0.5f)
            {
                var q = p + sd * o;
                if (_ground.Coast(q.x, q.z) <= 0f) { shore[k] = o; break; }
            }
            var c = p + sd * inner[k];
            ok[k] = shore[k] > inner[k] + NbOpenMinBeachM && !InAnyPad(c.x, c.z, 2f);
        }

        // --- 1. sand ribbon (dry + wet), chunked
        var sandT = Nb6Group(group, "NB Open Sand");
        var dryMat = Cel("NB_OpenSand", new Color(1.00f, 0.86f, 0.62f), 0.06f, 0.04f, 0.18f,
                         Tex(CoastTex, "Shiosai_Sand_Albedo.png"), Tex(CoastTex, "Shiosai_Sand_Normal.png"), 0.6f);
        dryMat.SetColor("_ShadeColor", new Color(0.80f, 0.84f, 0.92f, 1f));
        var wetMat = Cel("NB_OpenWetSand", new Color(0.74f, 0.63f, 0.48f), 0.30f, 0.22f, 0.12f,
                         Tex(CoastTex, "Shiosai_Sand_Albedo.png"), Tex(CoastTex, "Shiosai_Sand_Normal.png"), 0.6f);
        wetMat.SetColor("_ShadeColor", new Color(0.70f, 0.76f, 0.86f, 1f));
        var rows = new List<int>();                      // sample indices k used as ribbon rows
        { float last = -999f; for (int k = 0; k < n; k++) { float d = _route.Distance[i0 + k]; if (d - last >= NbOpenStepM || k == n - 1) { rows.Add(k); last = d; } } }
        int chunks = 0; float area = 0f;
        foreach (bool wet in new[] { false, true })
        {
            int lanes = wet ? NbOpenWetLanes : NbOpenDryLanes;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float chunkStart = _route.Distance[i0 + rows[0]];
            int prevBase = -1;
            void Flush()
            {
                if (t.Count > 0)
                {
                    FixWinding(v, t);
                    string nm = $"NB Open {(wet ? "Wet" : "Dry")} Sand {chunks:D2} - Beach Sand walk layer";
                    var go = AddMesh(sandT, nm, Finish($"Nagisa_Open{(wet ? "Wet" : "Dry")}Sand_{chunks:D2}", v, uv, t), wet ? wetMat : dryMat, true);
                    var mr = go.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    chunks++;
                }
                v = new List<Vector3>(); uv = new List<Vector2>(); t = new List<int>(); prevBase = -1;
            }
            foreach (int k in rows)
            {
                int i = i0 + k; float d = _route.Distance[i];
                if (!ok[k]) { Flush(); continue; }
                var p = _route.Position[i]; var sd = Nb6Sd(i);
                float a = wet ? shore[k] - NbOpenWetM : inner[k];
                float b = wet ? shore[k] + NbOpenUnderM : shore[k] - NbOpenWetM;
                int baseV = v.Count;
                bool padHit = false;
                for (int l = 0; l <= lanes; l++)
                {
                    float o = Mathf.Lerp(a, b, l / (float)lanes);
                    var q = p + sd * o;
                    if (InAnyPad(q.x, q.z, 1f)) padHit = true;
                    q.y = OpenSandY(q, o, shore[k]);
                    v.Add(q); uv.Add(new Vector2(q.x / 6f, q.z / 6f));
                }
                if (padHit) { v.RemoveRange(baseV, lanes + 1); uv.RemoveRange(baseV, lanes + 1); Flush(); continue; }
                if (prevBase >= 0)
                    for (int l = 0; l < lanes; l++)
                    {
                        int a0 = prevBase + l, a1 = prevBase + l + 1, b0 = baseV + l, b1 = baseV + l + 1;
                        t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                        if (!wet) area += NbOpenStepM * (b - a) / lanes;
                    }
                prevBase = baseV;
                if (d - chunkStart > NbOpenChunkM)
                {
                    // start the next chunk on this row so there is no seam gap
                    var keepV = v.GetRange(baseV, lanes + 1); var keepUv = uv.GetRange(baseV, lanes + 1);
                    Flush();
                    v.AddRange(keepV); uv.AddRange(keepUv); prevBase = 0; chunkStart = d;
                }
            }
            Flush();
        }

        // --- 2. foam + shoreline walkers (NB6 helper; its okRow-free profile = shore[])
        var shoreForFoam = new float[n];
        for (int k = 0; k < n; k++) shoreForFoam[k] = ok[k] ? shore[k] : -1f;
        var peopleT = Nb6Group(group, "NB Open Beach People");
        int walkers = BuildNb6Shoreline(group, peopleT, cast, rng, i0, n, shoreForFoam, "NBOpen");

        // --- 3. umbrella sets, beachgoers, palms, lifeguard towers
        var propsT = Nb6Group(group, "NB Open Beach Sets");
        int sets = 0, palms = 0, towers = 0, goers = 0;
        float lastU = -999f, lastP = -999f, lastT = -NbOpenTowerEveryM * 0.6f;
        for (int k = 0; k < n; k++)
        {
            if (!ok[k]) continue;
            int i = i0 + k; float d = _route.Distance[i];
            var sd = Nb6Sd(i); var tan = Nb6Tan(i);
            float seaYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
            var p0 = _route.Position[i];

            if (d - lastP >= NbOpenPalmEveryM)
            {
                lastP = d;
                if (R() < NbOpenPalmChance)
                {
                    var q = p0 + sd * (Mathf.Max(inner[k], CorridorKeepOutM) + 1.8f + R() * 1.5f);
                    if (!InAnyPad(q.x, q.z, 3f) && CanPlace(q.x, q.z, 2f, out float qy, 5f) && Nb6Free(q, 3f))
                    {
                        PlaceWorld(R() < 0.5f ? "Nagisa_CoconutPalm_A" : "Nagisa_CoconutPalm_B", propsT,
                                   new Vector3(q.x, OpenSandY(q, inner[k] + 2f, shore[k]) - 0.1f, q.z), R() * 360f, 0.8f + R() * 0.3f);
                        Nb6Occupy(q); palms++;
                    }
                }
            }
            if (d - lastT >= NbOpenTowerEveryM && shore[k] - inner[k] > 22f)
            {
                var q = p0 + sd * (shore[k] - NbOpenUmbWetKeepM - 3f);
                if (!InAnyPad(q.x, q.z, 3f) && CanPlace(q.x, q.z, 2f, out _, 3f) && Nb6Free(q, 4f))
                {
                    lastT = d;
                    PlaceWorld("Nagisa_LifeguardTower", propsT, new Vector3(q.x, OpenSandY(q, shore[k] - 12f, shore[k]) - 0.1f, q.z), seaYaw + 180f, 1f, SmallPropCull);
                    Nb6Occupy(q); towers++;
                    var g = q - sd * 2.2f; g.y = OpenSandY(g, shore[k] - 14f, shore[k]);
                    if (Nb6Spawn(cast, peopleT, g, sd, "Lifeguard", MK.Idle, R()) != null) goers++;
                }
            }
            if (d - lastU < NbOpenUmbStepM) continue;
            lastU = d;
            int row = 0;
            for (float o = Mathf.Max(inner[k], CorridorKeepOutM) + NbOpenUmbFirstM; o < shore[k] - NbOpenUmbWetKeepM && row < NbOpenUmbMaxRows; o += NbOpenUmbRowM, row++)
            {
                if (R() > NbOpenUmbFill) continue;
                var u = p0 + sd * (o + (R() - 0.5f) * 1.6f) + tan * ((row & 1) * NbOpenUmbStepM * 0.5f + (R() - 0.5f) * 1.2f);
                if (InAnyPad(u.x, u.z, 3f) || !CanPlace(u.x, u.z, 3f, out _, 6f) || !Nb6Free(u, 2.6f)) continue;
                float uo = Vector3.Dot(u - p0, sd);
                u.y = OpenSandY(u, uo, shore[k]);
                Nb6Occupy(u);
                if (R() < 0.72f)
                    PlaceWorld("Nagisa_S_Parasol", propsT, new Vector3(u.x, u.y - 0.1f, u.z), R() * 360f, 1f, SmallPropCull, NbColourway(rng.Next(8)));
                else
                    PlaceWorld("Nagisa_BeachUmbrella", propsT, new Vector3(u.x, u.y - 0.15f, u.z), R() * 360f, 1f, SmallPropCull);
                float yaw = seaYaw + 180f + (R() - 0.5f) * 18f;
                var side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                bool towels = R() < 0.45f;
                int count = R() < 0.75f ? 2 : 1;
                for (int c = 0; c < count; c++)
                {
                    var lp = u + side * (count == 1 ? 0f : (c == 0 ? -0.85f : 0.85f)) + sd * 0.45f;
                    lp.y = OpenSandY(lp, uo + 0.45f, shore[k]) + 0.01f;
                    if (towels)
                    {
                        PlaceWorld("Nagisa_S_Towel", propsT, lp + sd * 0.4f, yaw, 1f, SmallPropCull, NbColourway(rng.Next(8)));
                        if (R() < Nb6SunbatherChance)
                            if (Nb6Spawn(cast, peopleT, lp + sd * 1.7f + side * 0.2f, -sd + side * (R() - 0.5f), "Sunbather",
                                         R() < 0.3f ? MK.Wave : MK.Idle, R()) != null) goers++;
                    }
                    else
                    {
                        PlaceWorld("Nagisa_Lounger", propsT, lp, yaw, 1f, SmallPropCull);
                        if (R() < Nb6SunbatherChance) { Nb6Lounger(cast, peopleT, lp, sd, R()); goers++; }
                    }
                }
                if (R() < NbOpenBeachgoerChance)
                {
                    var g = u + sd * (2.4f + R()) + tan * ((R() - 0.5f) * 3f);
                    g.y = OpenSandY(g, uo + 2.8f, shore[k]);
                    if (Nb6Free(g, 0.8f) && Nb6Spawn(cast, peopleT, g, -sd + tan * (R() - 0.5f), "Beachgoer",
                                                     R() < 0.35f ? MK.Wave : MK.Idle, R()) != null) goers++;
                }
                sets++;
            }
        }
        if (_nb6Patch.t.Count > 0)
        {
            FixWinding(_nb6Patch.v, _nb6Patch.t);
            var go = AddMesh(peopleT, "NB Open Lounger Patches - Beach Sand walk layer",
                             Finish("Nagisa_NBOpen_LoungerPatch", _nb6Patch.v, _nb6Patch.uv, _nb6Patch.t), _nb["NB_Canvas"], true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        foreach (var r in propsT.GetComponentsInChildren<MeshRenderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);

        // --- 4. runners / joggers: glide loops on the wet sand and on the dry sand by the road
        var glideT = Nb6Group(group, "NB Open Beach Runners");
        int runners = 0, loopsN = 0;
        foreach (var (offOut, offBack, wetLane) in new[] { (3.2f, 4.6f, true), (2.2f, 3.4f, false) })
        {
            List<Vector3> la = null, lb = null;
            var loops = new List<List<Vector3>>();
            void Close() { if (la != null && la.Count >= 12) { lb.Reverse(); la.AddRange(lb); loops.Add(la); } la = null; lb = null; }
            foreach (int k in rows)
            {
                if (!ok[k]) { Close(); continue; }
                int i = i0 + k; var p = _route.Position[i]; var sd = Nb6Sd(i);
                float oa = wetLane ? shore[k] - offOut : inner[k] + offOut;
                float ob = wetLane ? shore[k] - offBack : inner[k] + offBack;
                var qa = p + sd * oa; var qb = p + sd * ob;
                if (InAnyPad(qa.x, qa.z, 2f) || InAnyPad(qb.x, qb.z, 2f)) { Close(); continue; }
                qa.y = OpenSandY(qa, oa, shore[k]); qb.y = OpenSandY(qb, ob, shore[k]);
                if (la == null) { la = new List<Vector3>(); lb = new List<Vector3>(); }
                la.Add(qa); lb.Add(qb);
            }
            Close();
            foreach (var loop in loops)
            {
                var lt = Nb6Group(glideT, $"NB Open Run Loop {loopsN++}");
                var act = lt.gameObject.AddComponent<NagisaBeachActivity>();
                act.kind = NagisaBeachActivity.Kind.Glide;
                act.cullDistance = Nb6CullM;
                act.path = loop.ToArray();
                float len = act.PathLength;
                int m = Mathf.Max(1, Mathf.RoundToInt(len / NbOpenRunnerEveryM));
                var items = new List<Transform>(); var sp = new List<float>(); var s0 = new List<float>();
                var sw = new List<float>(); var ly = new List<float>();
                for (int q = 0; q < m; q++)
                {
                    float start = (q + R() * 0.6f) / m * len;
                    var pos = act.SamplePath(start, out var fwd);
                    var go = NbPerson(cast.walk, peopleT, "Runner", pos, fwd, MK.Walk, R());
                    if (go == null) continue;
                    var ca = go.GetComponent<MinatoCrowdActor>(); if (ca != null) ca.moveSpeed = 2.4f;
                    FinishPerson(go);
                    go.transform.SetParent(lt, true);
                    items.Add(go.transform);
                    sp.Add(2.3f + R() * 0.9f); s0.Add(start); sw.Add(0f); ly.Add(0f);
                }
                act.items = items.ToArray(); act.speeds = sp.ToArray(); act.startS = s0.ToArray();
                act.sway = sw.ToArray(); act.liftY = ly.ToArray();
                act.Step(0f);
                runners += items.Count;
            }
        }

        Debug.Log($"[nb-open] opening beach d {NbOpenD0:0}..{_route.BeachStartM:0}: {chunks} sand chunks (~{area:0} m2 dry), " +
                  $"{sets} umbrella sets, {palms} palms, {towers} lifeguard towers, {goers} beachgoers, {walkers} shoreline walkers, " +
                  $"{runners} runners on {loopsN} loops; {_nbPeople - people0} people added.");
    }

    /// <summary>Opening-beach surface height: terrain + lift on dry sand, tapering under the sea
    /// through the wet band so NB6 swash foam (SeaLevelY + 0.035) stays visible.</summary>
    private static float OpenSandY(Vector3 q, float off, float shore)
    {
        float g = _ground.Height(q.x, q.z);
        float wet0 = shore - NbOpenWetM;
        if (off <= wet0) return g + NbOpenLift;
        float t = Mathf.Clamp01((off - wet0) / NbOpenWetM);
        float y = g + Mathf.Lerp(NbOpenLift, -0.03f, t);
        if (off > shore) y = Mathf.Min(y, SeaLevelY - 0.06f * (off - shore));
        return y;
    }
}

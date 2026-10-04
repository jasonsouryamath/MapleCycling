using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Taka Mountains - LIVING ROUTE + ARRIVAL (COORDINATION N6 + L5 + finish fix; copilot maplerider-builder).
///
/// Two jobs:
///  1. The final stretch used to end in a bare asphalt edge still descending at ~10% - "a road to
///     nowhere". It now finishes in the hamlet of Val-Taka: a flamme-rouge banner at 1 km to go,
///     barrier-lined crowd for the last few hundred metres, a finish gantry with a painted line,
///     then a paved arrival plaza beyond the last station, closed on its far side by an arc of
///     townhouses and an auberge around a fountain. The road now visibly ARRIVES somewhere.
///  2. Liveliness on the climb: roadside spectator clusters with camper vans and swaying flags on
///     the exposed upper mountain, photographers at the verge, hikers walking the shoulder,
///     parked team cars, and W0 ambient movers overhead - circling eagles, paragliders and a TV
///     helicopter with a spinning rotor.
///
/// Everything lives under one child "Taka Living" of the region root, which Apply() rebuilds by
/// exact name, so this pass is idempotent. EVERY number below is PROVISIONAL art tuning.
/// </summary>
public static partial class TakaMountainsEnvironment
{
    // ---------------------------------------------------------------- provisional tunables
    private const float LivFinishBeforeEndM = 14f;        // finish line this far before the last station
    private const float LivBarrierLengthM = 260f;         // barriers + crowd over the last N metres
    private const float LivBarrierCrowdNear = 0.55f;      // spectator chance per 2.5 m bay at the line ...
    private const float LivBarrierCrowdFar = 0.22f;       // ... and at the start of the barriers (crowd budget)
    private const float LivVillageLengthM = 420f;         // arrival-hamlet houses over the last N metres
    private const float LivFlammeRougeM = 1000f;          // "1 KM" banner distance to go
    private const float LivPlazaRadiusM = 13f;            // arrival plaza radius (kept inside the 18 m road cut)
    private const float LivPlazaAheadM = 7f;              // plaza centre this far beyond the last station
    private const float LivSpectatorFromM = 10800f;       // spectator clusters along the upper climb ...
    private const float LivSpectatorToM = 15300f;         // ... up to the summit
    private const float LivSpectatorStepM = 300f;
    private const float LivHikerFromM = 13000f;
    private const float LivHikerToM = 17600f;
    private const float LivHikerStepM = 420f;
    private const float LivParagliderAltM = 140f;
    private const float LivHeliAltM = 95f;
    private const float LivHeliOrbitM = 260f;
    private const int LivSeed = 7719;

    private static readonly Color[] LivFlagColours =
    {
        new Color(0.85f, 0.16f, 0.14f), new Color(0.98f, 0.82f, 0.12f),
        new Color(0.16f, 0.38f, 0.80f), new Color(0.95f, 0.95f, 0.92f), new Color(0.20f, 0.62f, 0.34f),
    };

    private static void BuildLiving(Transform root, TakaRoute route)
    {
        AssetDatabase.ImportAsset($"{TextureDir}/Taka_Sign_TrimSheet.png");

        var group = new GameObject("Taka Living").transform;
        group.SetParent(root, false);
        var rng = new System.Random(LivSeed);
        float kerb = RoadHalfWidth + ShoulderWidth;

        // ---------------------------------------------------------------- buckets
        var stucco = new Bucket[4];
        for (int k = 0; k < 4; k++) stucco[k] = new Bucket($"LStucco{k}", Stucco(k));
        var tile = new Bucket("LRoofTile", Plain("TakaV_Terracotta", new Color(0.70f, 0.34f, 0.20f)));
        var shutter = new Bucket("LShutter", Plain("TakaV_Shutter0", new Color(0.35f, 0.55f, 0.66f)));
        var door = new Bucket("LDoor", Plain("TakaV_Door", new Color(0.30f, 0.20f, 0.13f)));
        var bark = new Bucket("LPlaneBark", Plain("TakaV_PlaneBark", new Color(0.62f, 0.60f, 0.50f), 0.1f));
        var leaf = new Bucket("LPlaneLeaf", Plain("TakaV_PlaneLeaf", new Color(0.30f, 0.46f, 0.20f), 0.1f));
        var awnRed = new Bucket("LAwnRed", Plain("TakaV_AwnRed", new Color(0.72f, 0.16f, 0.14f)));
        var awnWhite = new Bucket("LAwnWhite", Plain("TakaV_AwnWhite", new Color(0.93f, 0.92f, 0.88f)));
        var steel = new Bucket("LSteel", Plain("TakaL_Steel", new Color(0.62f, 0.63f, 0.66f), 0.5f));
        var barrierBlue = new Bucket("LBarrierBlue", Plain("TakaL_BarrierBlue", new Color(0.18f, 0.36f, 0.72f)));
        var barrierWhite = new Bucket("LBarrierWhite", Plain("TakaL_BarrierWhite", new Color(0.94f, 0.94f, 0.92f)));
        var paintW = new Bucket("LLinePaint", Plain("TakaL_LineWhite", new Color(0.96f, 0.96f, 0.94f), 0.1f));
        var paintK = new Bucket("LLineBlack", Plain("TakaL_LineBlack", new Color(0.10f, 0.10f, 0.11f), 0.1f));
        var vanBody = new Bucket("LVanBody", Plain("TakaL_VanBody", new Color(0.93f, 0.92f, 0.88f), 0.4f));
        var vanStripe = new Bucket("LVanStripe", Plain("TakaL_VanStripe", new Color(0.30f, 0.52f, 0.66f), 0.3f));
        var glass = new Bucket("LGlass", Plain("TakaL_Glass", new Color(0.16f, 0.20f, 0.26f), 0.8f));
        var tyre = new Bucket("LTyre", Plain("TakaL_Tyre", new Color(0.08f, 0.08f, 0.09f), 0.1f));
        var carCols = new[] { new Color(0.80f, 0.14f, 0.16f), new Color(0.12f, 0.30f, 0.62f), new Color(0.95f, 0.78f, 0.10f), new Color(0.18f, 0.55f, 0.32f) };
        var carBody = new Bucket[carCols.Length];
        for (int k = 0; k < carCols.Length; k++) carBody[k] = new Bucket($"LCar{k}", Plain($"TakaL_Car{k}", carCols[k], 0.55f));
        var bike = new Bucket("LBikes", Plain("TakaV_BikeFrame", new Color(0.12f, 0.12f, 0.14f), 0.5f));
        var bikeCol = new Bucket("LBikeColour", Plain("TakaV_BikeColour", new Color(0.10f, 0.45f, 0.75f), 0.6f));
        var stone = new Bucket("LFountainStone", CelMaterial("TakaL_FountainStone", new Color(0.82f, 0.78f, 0.70f), gloss: 0.08f,
                                spec: 0.05f, rim: 0.1f, texture: TakaTexture("Taka_Limestone_Albedo.png"), shade: LimeShade));
        var water = new Bucket("LFountainWater", Plain("TakaL_Water", new Color(0.40f, 0.66f, 0.80f), 0.9f));
        var paving = new Bucket("LPlaza", CelMaterial("TakaL_Plaza", new Color(0.76f, 0.72f, 0.66f), gloss: 0.06f, spec: 0.04f,
                                rim: 0.08f, texture: TakaTexture("Taka_Limestone_Albedo.png"), shade: LimeShade));
        foreach (var m in new[] { awnRed.Mat, awnWhite.Mat, barrierBlue.Mat, barrierWhite.Mat })
            if (m.HasProperty("_Cull")) { m.SetFloat("_Cull", 0f); EditorUtility.SetDirty(m); }
        var all = new List<Bucket> { tile, shutter, door, bark, leaf, awnRed, awnWhite, steel, barrierBlue, barrierWhite, paintW, paintK,
                                     vanBody, vanStripe, glass, tyre, bike, bikeCol, stone, water, paving };
        all.AddRange(stucco); all.AddRange(carBody);

        var spots = new List<(Vector3 pos, Vector3 face, MinatoCrowdActor.MotionKind kind, Vector3 walkTo)>();
        var flagPoles = new List<(Vector3 top, Vector3 along)>();
        int last = route.Count - 1;
        float L = route.Length;

        // ================================================================ 1. ARRIVAL
        // --- flamme rouge: a thin red gantry with a "1 KM" board
        Gantry(route, route.IndexAt(L - LivFlammeRougeM), steel, group, "Taka_Sign_KOM.png", "Flamme Rouge", 5.2f, 0.5f, kerb);

        // --- the finish gantry + painted chequered line
        int iFin = route.IndexAt(L - LivFinishBeforeEndM);
        Gantry(route, iFin, steel, group, "Taka_Sign_Finish.png", "Finish Banner", 6.2f, 0.1875f, kerb);
        {
            var side = route.SideFlat(iFin); var tan = route.Tangent[iFin]; tan.y = 0f; tan.Normalize();
            const float Cell = 0.5f;
            for (int row = 0; row < 2; row++)
                for (float o = -RoadHalfWidth + Cell * 0.5f; o < RoadHalfWidth; o += Cell)
                {
                    int col = Mathf.RoundToInt((o + RoadHalfWidth) / Cell) + row;
                    var p = route.Position[iFin] + side * o + tan * ((row - 0.5f) * Cell);
                    p.y = RoadY(route, iFin, o) + 0.012f;
                    var b = col % 2 == 0 ? paintW : paintK;
                    Box(b.V, b.UV, b.T, null, p, side * (Cell * 0.5f), tan * (Cell * 0.5f), 0.012f, Color.white);
                }
        }

        // --- barrier-lined run-in with a deep crowd behind it (both sides)
        for (float d = L - LivBarrierLengthM; d < L - 2f; d += 2.5f)
        {
            int i = route.IndexAt(d), j = route.IndexAt(d + 2.4f);
            for (int s = -1; s <= 1; s += 2)
            {
                float o = s * (kerb + 0.35f);
                var a = route.Position[i] + route.SideFlat(i) * o; a.y = RoadY(route, i, o);
                var c = route.Position[j] + route.SideFlat(j) * o; c.y = RoadY(route, j, o);
                var pb = ((int)(d / 2.5f)) % 2 == 0 ? barrierBlue : barrierWhite;
                Band(pb.V, pb.UV, pb.T, a + Vector3.up * 0.2f, c + Vector3.up * 0.2f,
                     a + Vector3.up * 0.8f, c + Vector3.up * 0.8f, 0f);
                Post(steel.V, steel.UV, steel.T, null, a, a + Vector3.up * 0.85f, 0.03f, Color.white);
                Post(steel.V, steel.UV, steel.T, null, a + Vector3.up * 0.83f, c + Vector3.up * 0.83f, 0.025f, Color.white);
                // spectators: dense near the line, thinning back up the road
                float density = Mathf.Lerp(LivBarrierCrowdNear, LivBarrierCrowdFar, (L - d) / LivBarrierLengthM);
                if (rng.NextDouble() < density)
                {
                    float back = kerb + 0.9f + (float)rng.NextDouble() * 0.5f;
                    Foot(route, i, s * back, out var f, out var outward);
                    f.y = Mathf.Max(f.y, RoadY(route, i, 0f));
                    if (NearestDist(f) > kerb + 0.6f)
                        spots.Add((f, -outward + route.Tangent[i] * 0.4f * (float)(rng.NextDouble() - 0.5), MinatoCrowdActor.MotionKind.Wave, f));
                }
                if (rng.NextDouble() < density * 0.45)
                {
                    Foot(route, i, s * (kerb + 2.1f), out var f2, out var out2);
                    f2.y = Mathf.Max(f2.y, RoadY(route, i, 0f));
                    if (NearestDist(f2) > kerb + 1.4f) spots.Add((f2, -out2, MinatoCrowdActor.MotionKind.Idle, f2));
                }
                if (((int)(d / 2.5f)) % 9 == 3 && s == (((int)(d / 22.5f)) % 2 == 0 ? 1 : -1))
                {
                    Foot(route, i, s * (kerb + 1.4f), out var ff, out _);
                    ff.y = Mathf.Max(ff.y, RoadY(route, i, 0f));
                    flagPoles.Add((ff, route.Tangent[i]));
                }
            }
        }

        // --- the hamlet: houses on both sides of the last few hundred metres
        int houses = 0;
        for (float d = L - LivVillageLengthM; d < L - 24f; d += 13f)
        {
            int i = route.IndexAt(d);
            for (int s = -1; s <= 1; s += 2)
            {
                if (rng.NextDouble() < 0.15) continue;
                float o = s * (kerb + 8.5f + (float)rng.NextDouble() * 2f);
                Foot(route, i, o, out var f, out var outward);
                if (NearestDist(f) < 9.5f) continue;
                f.y = Mathf.Max(f.y, RoadY(route, i, 0f) - 0.4f);
                float W = 8f + (float)rng.NextDouble() * 2.5f, D = 7f, H = 6f + (float)rng.NextDouble() * 3.5f;
                Townhouse(stucco[rng.Next(4)], tile, shutter, door, f, outward, W, D, H, rng);
                houses++;
                if (((int)(d / 13f)) % 2 == 0)
                {
                    Foot(route, i, s * (kerb + 3.4f), out var tf, out _);
                    tf.y = Mathf.Max(tf.y, RoadY(route, i, 0f) - 0.2f);
                    if (NearestDist(tf) > kerb + 2.5f) PlaneTree(bark, leaf, tf, 9f + (float)rng.NextDouble() * 3f, rng);
                }
            }
        }
        // village name board just before the barriers start
        {
            int i = route.IndexAt(L - LivBarrierLengthM - 25f);
            Foot(route, i, kerb + 1.4f, out var f, out _);
            f.y = Mathf.Max(f.y, RoadY(route, i, 0f));
            var tan = route.Tangent[i]; tan.y = 0; tan.Normalize();
            var side = route.SideFlat(i);
            Post(steel.V, steel.UV, steel.T, null, f + tan * 0.9f, f + tan * 0.9f + Vector3.up * 2.4f, 0.04f, Color.white);
            Post(steel.V, steel.UV, steel.T, null, f - tan * 0.9f, f - tan * 0.9f + Vector3.up * 2.4f, 0.04f, Color.white);
            SignBoard(group, "Village Sign", "Taka_Sign_Village.png", f + Vector3.up * 2.05f - tan * 0.05f, side, 2.6f, 0.49f);
        }

        // --- arrival plaza beyond the last station
        var endP = route.Position[last];
        var endT = route.Tangent[last]; endT.y = 0f; endT.Normalize();
        var endS = Vector3.Cross(Vector3.up, endT).normalized;
        var plazaC = endP + endT * LivPlazaAheadM;
        plazaC.y = Height(plazaC.x, plazaC.z);
        {
            // grid in the end frame; cells ahead of the road's end edge only, so the paving never
            // z-fights the asphalt ribbon; each vertex on Height() so it sits flush on the cut.
            const float Cell = 1.5f;
            float R = LivPlazaRadiusM;
            Vector3 Pv(float a, float c)
            {
                var q = endP + endT * a + endS * c;
                q.y = Height(q.x, q.z) + 0.07f;
                return q;
            }
            Vector2 U(Vector3 q) => new Vector2(q.x * 0.3f, q.z * 0.3f);
            for (float a = 0f; a < LivPlazaAheadM + R; a += Cell)
                for (float c = -R - 2f; c < R + 2f; c += Cell)
                {
                    var mid = endP + endT * (a + Cell * 0.5f) + endS * (c + Cell * 0.5f);
                    bool inDisc = Vector2.Distance(new Vector2(mid.x, mid.z), new Vector2(plazaC.x, plazaC.z)) < R + 0.8f;
                    bool neck = a < LivPlazaAheadM && Mathf.Abs(c + Cell * 0.5f) < RoadHalfWidth + 3f;
                    if (!inDisc && !neck) continue;
                    var p00 = Pv(a, c); var p10 = Pv(a + Cell, c); var p01 = Pv(a, c + Cell); var p11 = Pv(a + Cell, c + Cell);
                    void T(Vector3 x, Vector3 y, Vector3 z)
                    {
                        if (Vector3.Cross(y - x, z - x).y < 0f) { var t = y; y = z; z = t; }
                        paving.Tri(x, y, z, U(x), U(y), U(z));
                    }
                    T(p00, p10, p01); T(p10, p11, p01);
                }
        }
        // fountain
        Drum(stone, plazaC - Vector3.up * 0.1f, 2.4f, 0.75f, 16);
        Drum(water, plazaC, 2.05f, 0.62f, 16);
        Post(stone.V, stone.UV, stone.T, null, plazaC, plazaC + Vector3.up * 2.0f, 0.25f, Color.white);
        Drum(stone, plazaC + Vector3.up * 1.6f, 0.8f, 0.18f, 12);
        // plane trees ringing the fountain
        for (int k = 0; k < 4; k++)
        {
            float ang = 45f + k * 90f;
            var dir = Quaternion.AngleAxis(ang, Vector3.up) * endT;
            var tp = plazaC + dir * 7.5f; tp.y = Height(tp.x, tp.z);
            if (Vector3.Dot(tp - endP, endT) < 1f) continue;   // keep off the road
            PlaneTree(bark, leaf, tp, 10f + (float)rng.NextDouble() * 2f, rng);
        }
        // arc of houses closing the far side, the auberge dead ahead on the road's axis
        for (int k = -3; k <= 3; k++)
        {
            float ang = k * 26f;
            var dir = Quaternion.AngleAxis(ang, Vector3.up) * endT;
            var f = plazaC + dir * (LivPlazaRadiusM + 0.6f);
            float g = Height(f.x, f.z);
            f.y = plazaC.y;
            if (g < f.y - 1.2f)   // plinth down to real ground if the field falls away
                Box(stone.V, stone.UV, stone.T, null, new Vector3(f.x, g - 0.5f, f.z) + dir * 4f, Vector3.Cross(Vector3.up, dir) * 5.4f,
                    dir * 4.2f, f.y - g + 0.5f, Color.white);
            bool auberge = k == 0;
            float W = auberge ? 12f : 9f, H = auberge ? 8.5f : 6.5f + (float)rng.NextDouble() * 2.5f;
            Townhouse(stucco[auberge ? 0 : rng.Next(4)], tile, shutter, door, f, dir, W, 8f, H, rng);
            if (auberge)
            {
                var along = Vector3.Cross(Vector3.up, dir).normalized;
                SignBoard(group, "Auberge Sign", "Taka_Sign_Auberge.png", f - dir * 0.14f + Vector3.up * 3.9f, along, 5f, 0.94f);
                int strips = 12;
                for (int st = 0; st < strips; st++)
                {
                    float x0 = -W * 0.45f + st * W * 0.9f / strips, x1 = x0 + W * 0.9f / strips;
                    var b = st % 2 == 0 ? awnRed : awnWhite;
                    Band(b.V, b.UV, b.T, f + along * x0 + Vector3.up * 3.3f, f + along * x1 + Vector3.up * 3.3f,
                         f + along * x0 - dir * 2.4f + Vector3.up * 2.7f, f + along * x1 - dir * 2.4f + Vector3.up * 2.7f, 0f);
                }
                for (int t = 0; t < 4; t++)   // terrace tables + diners
                {
                    var tc = f - dir * 3.4f + along * ((t - 1.5f) * 2.4f);
                    Post(steel.V, steel.UV, steel.T, null, tc, tc + Vector3.up * 0.74f, 0.04f, Color.white);
                    Drum(steel, tc + Vector3.up * 0.72f, 0.4f, 0.04f, 8);
                    spots.Add((tc + along * 0.65f, -along, MinatoCrowdActor.MotionKind.Idle, tc));
                    spots.Add((tc - along * 0.65f, along, MinatoCrowdActor.MotionKind.Idle, tc));
                }
            }
        }
        // team cars + a support van parked at the plaza's edges (roof racks with bikes)
        for (int k = 0; k < 4; k++)
        {
            int side = k % 2 == 0 ? 1 : -1;
            var c = plazaC + endS * side * (LivPlazaRadiusM - 3.2f) + endT * (k < 2 ? -3f : 2.5f);
            c.y = Height(c.x, c.z) + 0.07f;
            TeamCar(carBody[k], glass, tyre, bike, c, endT, rng);
        }
        // plaza people: chatting riders by the fountain, walkers crossing, a welcome committee
        for (int k = 0; k < 14; k++)
        {
            float ang = k * 360f / 14f + (float)rng.NextDouble() * 10f;
            var dir = Quaternion.AngleAxis(ang, Vector3.up) * endT;
            var p = plazaC + dir * (3.3f + (float)rng.NextDouble() * 1.4f); p.y = Height(p.x, p.z) + 0.07f;
            if (Vector3.Dot(p - endP, endT) < 1.5f) continue;
            spots.Add((p, plazaC - p, rng.NextDouble() < 0.3 ? MinatoCrowdActor.MotionKind.Wave : MinatoCrowdActor.MotionKind.Idle, p));
        }
        for (int k = 0; k < 6; k++)
        {
            float a0 = (float)rng.NextDouble() * 360f;
            var d0 = Quaternion.AngleAxis(a0, Vector3.up) * endT; var d1 = Quaternion.AngleAxis(a0 + 150f, Vector3.up) * endT;
            var p0 = plazaC + d0 * 9.5f; var p1 = plazaC + d1 * 9.5f;
            if (Vector3.Dot(p0 - endP, endT) < 1.5f || Vector3.Dot(p1 - endP, endT) < 1.5f) continue;
            p0.y = Height(p0.x, p0.z) + 0.07f; p1.y = Height(p1.x, p1.z) + 0.07f;
            spots.Add((p0, p1 - p0, MinatoCrowdActor.MotionKind.Walk, p1));
        }
        ParkedBikes(route, route.IndexAt(L - 40f), 1, 10, bike, bikeCol, spots, rng);
        ParkedBikes(route, route.IndexAt(L - 56f), -1, 8, bike, bikeCol, spots, rng);
        ShotMarker(group, "TakaShot_Arrival", plazaC + Vector3.up * 1.5f, endT);

        // ================================================================ 2. LIVING CLIMB
        int vans = 0;
        for (float d = LivSpectatorFromM; d < LivSpectatorToM; d += LivSpectatorStepM)
        {
            float dd = d + (float)(rng.NextDouble() - 0.5) * 90f;
            int i = route.IndexAt(dd);
            int s = rng.NextDouble() < 0.5 ? 1 : -1;
            Foot(route, i, s * (kerb + 6.2f), out var vf, out var vout);
            if (NearestDist(vf) < kerb + 5f) { s = -s; Foot(route, i, s * (kerb + 6.2f), out vf, out vout); }
            if (NearestDist(vf) < kerb + 5f) continue;
            vf.y = Mathf.Max(vf.y, RoadY(route, i, 0f) - 0.05f);
            if (rng.NextDouble() < 0.75)
            {
                CamperVan(vanBody, vanStripe, glass, tyre, vf, route.Tangent[i], vout); vans++;
                if (dd > 13000f && group.Find("TakaShot_Fans") == null)
                    ShotMarker(group, "TakaShot_Fans", vf, vout);
            }
            // a knot of fans between van and road, plus a photographer crouched at the kerb
            int fans = 3 + rng.Next(5);
            for (int k = 0; k < fans; k++)
            {
                int ik = route.IndexAt(dd + (k - fans * 0.5f) * 1.3f);
                Foot(route, ik, s * (kerb + 1.1f + (float)rng.NextDouble() * 1.8f), out var p, out var po);
                p.y = Mathf.Max(p.y, RoadY(route, ik, 0f));
                if (NearestDist(p) < kerb + 0.7f) continue;
                spots.Add((p, -po, rng.NextDouble() < 0.5 ? MinatoCrowdActor.MotionKind.Wave : MinatoCrowdActor.MotionKind.Idle, p));
            }
            {
                int ip = route.IndexAt(dd + 18f);
                Foot(route, ip, s * (kerb + 0.9f), out var p, out var po);
                p.y = Mathf.Max(p.y, RoadY(route, ip, 0f));
                if (NearestDist(p) > kerb + 0.5f)
                {
                    spots.Add((p, -po - route.Tangent[ip] * 0.8f, MinatoCrowdActor.MotionKind.Idle, p));
                    Tripod(steel, p + route.Tangent[ip] * 0.6f);
                }
            }
            Foot(route, i, s * (kerb + 3.2f), out var fp, out _);
            fp.y = Mathf.Max(fp.y, RoadY(route, i, 0f));
            flagPoles.Add((fp, route.Tangent[i]));
            if (rng.NextDouble() < 0.5)
            {
                Foot(route, route.IndexAt(dd + 9f), s * (kerb + 2.4f), out var fp2, out _);
                fp2.y = Mathf.Max(fp2.y, RoadY(route, route.IndexAt(dd + 9f), 0f));
                flagPoles.Add((fp2, route.Tangent[i]));
            }
        }
        // parked team cars in the summit lay-bys
        foreach (var d in new[] { 11650f, 13150f, 14620f })
        {
            int i = route.IndexAt(d);
            for (int s = -1; s <= 1; s += 2)
            {
                Foot(route, i, s * (kerb + 4.2f), out var c, out _);
                if (NearestDist(c) < kerb + 3.2f) continue;
                c.y = Mathf.Max(c.y, RoadY(route, i, 0f) - 0.05f);
                var tan = route.Tangent[i]; tan.y = 0; tan.Normalize();
                TeamCar(carBody[rng.Next(carBody.Length)], glass, tyre, bike, c, tan, rng);
                break;
            }
        }
        // hikers walking the shoulder near the top
        for (float d = LivHikerFromM; d < LivHikerToM; d += LivHikerStepM)
        {
            int n = 1 + rng.Next(3);
            int s = rng.NextDouble() < 0.5 ? 1 : -1;
            for (int k = 0; k < n; k++)
            {
                int i = route.IndexAt(d + k * 2.2f), j = route.IndexAt(d + k * 2.2f + 14f);
                float o = s * (kerb + 1.6f + k * 0.2f);
                Foot(route, i, o, out var a, out _); Foot(route, j, o, out var b, out _);
                a.y = Mathf.Max(a.y, RoadY(route, i, 0f)); b.y = Mathf.Max(b.y, RoadY(route, j, 0f));
                if (NearestDist(a) < kerb + 0.8f || NearestDist(b) < kerb + 0.8f) continue;
                spots.Add((a, b - a, MinatoCrowdActor.MotionKind.Walk, b));
            }
        }

        foreach (var b in all) b.Flush(group);

        // ---------------------------------------------------------------- flags (swaying)
        var flagRoot = new GameObject("Taka Living Flags").transform;
        flagRoot.SetParent(group, false);
        var flagTargets = new List<Transform>();
        var flagMats = new Material[LivFlagColours.Length];
        for (int k = 0; k < flagMats.Length; k++)
        {
            flagMats[k] = Plain($"TakaL_Flag{k}", LivFlagColours[k], 0.2f);
            if (flagMats[k].HasProperty("_Cull")) { flagMats[k].SetFloat("_Cull", 0f); EditorUtility.SetDirty(flagMats[k]); }
        }
        var poleBucket = new Bucket("LFlagPoles", Plain("TakaL_Steel", new Color(0.62f, 0.63f, 0.66f), 0.5f));
        Mesh flagMesh = null; GameObject flagDonor = null;
        for (int k = 0; k < flagPoles.Count; k++)
        {
            var (foot, along) = flagPoles[k];
            const float PoleH = 3.8f;
            Post(poleBucket.V, poleBucket.UV, poleBucket.T, null, foot, foot + Vector3.up * PoleH, 0.025f, Color.white);
            var pivot = new GameObject($"Flag {k:000}").transform;
            pivot.SetParent(flagRoot, false);
            pivot.position = foot + Vector3.up * (PoleH - 0.45f);
            along.y = 0f; if (along.sqrMagnitude < 1e-4f) along = Vector3.forward;
            pivot.rotation = Quaternion.LookRotation(along.normalized, Vector3.up) * Quaternion.Euler(0f, rng.Next(-40, 40), 0f);
            GameObject flag;
            if (flagMesh == null)
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                Quad(v, uv, t, new Vector3(0f, 0f, -0.7f), new Vector3(0f, 0f, 0.7f), new Vector3(0f, 0.42f, 0f));
                flagMesh = Finish("TakaL_FlagCloth", v.ToArray(), uv.ToArray(), t);
                flag = AddMesh(pivot, "Cloth", flagMesh, flagMats[0], collider: false);
                flagDonor = flag;
            }
            else
            {
                flag = Object.Instantiate(flagDonor, pivot, false);
                flag.name = "Cloth";
                flag.GetComponent<MeshRenderer>().sharedMaterial = flagMats[k % flagMats.Length];
            }
            flag.transform.localPosition = Vector3.zero; flag.transform.localRotation = Quaternion.identity;
            flagTargets.Add(pivot);
        }
        poleBucket.Flush(flagRoot);
        if (flagTargets.Count > 0)
            AmbientMoverStaging.StageSway(flagRoot, "Flag Flutter", flagTargets.ToArray(), Vector3.up, 22f, 0.9f,
                                          AmbientSway.SwayMode.Noise, default, LivSeed);

        // ---------------------------------------------------------------- sky movers
        var sky = new GameObject("Taka Living Sky").transform;
        sky.SetParent(group, false);
        int iTop = route.IndexAt(SummitM);
        var top = route.Position[iTop];
        var topSide = route.SideFlat(iTop);

        // eagles: three small flocks circling over the upper mountain
        var birdMat = Plain("TakaL_Eagle", new Color(0.26f, 0.19f, 0.13f), 0.1f);
        if (birdMat.HasProperty("_Cull")) { birdMat.SetFloat("_Cull", 0f); EditorUtility.SetDirty(birdMat); }
        GameObject birdDonor;
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            void W(Vector3 a, Vector3 b, Vector3 c)
            {
                int n0 = v.Count; v.Add(a); v.Add(b); v.Add(c); uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
                t.Add(n0); t.Add(n0 + 1); t.Add(n0 + 2);
                n0 = v.Count; v.Add(a); v.Add(c); v.Add(b); uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.right);
                t.Add(n0); t.Add(n0 + 1); t.Add(n0 + 2);
            }
            // body + two slightly raised wings (span ~2.1 m) + tail
            W(new Vector3(0, 0, 0.55f), new Vector3(1.05f, 0.18f, -0.05f), new Vector3(0, 0.02f, -0.25f));
            W(new Vector3(0, 0, 0.55f), new Vector3(0, 0.02f, -0.25f), new Vector3(-1.05f, 0.18f, -0.05f));
            W(new Vector3(0, 0, -0.1f), new Vector3(0.22f, 0, -0.7f), new Vector3(-0.22f, 0, -0.7f));
            var birdMesh = Finish("TakaL_Eagle", v.ToArray(), uv.ToArray(), t);
            birdDonor = AddMesh(sky, "Eagle Donor", birdMesh, birdMat, collider: false);
            birdDonor.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var eagleSites = new[] { (12400f, 1, 70f), (14200f, -1, 90f), (16400f, 1, 80f) };
        for (int e = 0; e < eagleSites.Length; e++)
        {
            var (d, s, alt) = eagleSites[e];
            int i = route.IndexAt(d);
            var c = route.Position[i] + route.SideFlat(i) * (s * 90f) + Vector3.up * alt;
            var members = new Transform[e == 1 ? 3 : 2];
            for (int m = 0; m < members.Length; m++)
            {
                var b = e == 0 && m == 0 ? birdDonor : Object.Instantiate(birdDonor, sky, false);
                b.name = $"Eagle {e}-{m}";
                b.transform.localScale = Vector3.one * (1.6f + 0.3f * m);
                members[m] = b.transform;
            }
            AmbientMoverStaging.StageFlock(sky, $"Eagles {e}", c, members, AmbientFlock.FlockMode.Circling,
                                           speed: 9f, radius: 45f, radiusVariance: 12f, heightVariance: 6f, seed: LivSeed + e);
        }

        // paragliders: three canopies riding the ridge lift beside the summit
        var canopyCols = new[] { new Color(0.95f, 0.45f, 0.10f), new Color(0.90f, 0.20f, 0.45f), new Color(0.20f, 0.70f, 0.85f) };
        for (int g = 0; g < 3; g++)
        {
            var glider = BuildParaglider(sky, g, canopyCols[g]);
            int s = g == 1 ? -1 : 1;
            int i = route.IndexAt(SummitM - 600f + g * 700f);
            var centre = route.Position[i] + route.SideFlat(i) * (s * (230f + g * 40f)) + Vector3.up * (LivParagliderAltM + g * 25f);
            var ctrl = new List<Vector3>();
            float rad = 150f + g * 30f;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 2f / 8f;
                ctrl.Add(centre + new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a * 2f + g) * 12f, Mathf.Sin(a) * rad * 0.7f));
            }
            var path = AmbientMoverStaging.BakeCatmullRom(ctrl, 10, true);
            AmbientMoverStaging.StagePathMover(sky, $"Paraglider Path {g}", path, new[] { glider }, speed: 9f + g, accel: 1f,
                                               mode: AmbientPathMover.PathMode.Loop, dwellSeconds: 0f, startDistance: 60f + g * 170f,
                                               bankFactor: 0.6f, bobHeave: 0.8f, bobTilt: 2f, seed: LivSeed + 10 + g);
        }

        // TV helicopter orbiting the upper climb
        {
            var heli = BuildHelicopter(sky, out var rotor, out var tailRotor);
            int i = route.IndexAt(13600f);
            var centre = route.Position[i] + Vector3.up * LivHeliAltM;
            var ctrl = new List<Vector3>();
            for (int k = 0; k < 10; k++)
            {
                float a = k * Mathf.PI * 2f / 10f;
                ctrl.Add(centre + new Vector3(Mathf.Cos(a) * LivHeliOrbitM, Mathf.Sin(a * 3f) * 8f, Mathf.Sin(a) * LivHeliOrbitM * 0.8f));
            }
            var path = AmbientMoverStaging.BakeCatmullRom(ctrl, 10, true);
            AmbientMoverStaging.StagePathMover(sky, "TV Helicopter Path", path, new[] { heli }, speed: 22f, accel: 3f,
                                               mode: AmbientPathMover.PathMode.Loop, dwellSeconds: 0f, startDistance: 400f,
                                               bankFactor: 0.8f, bobHeave: 0.6f, bobTilt: 1.5f, seed: LivSeed + 20);
            AmbientMoverStaging.StageRotor(sky, "TV Helicopter Rotor", new[] { rotor }, Vector3.up, 1100f, 0f, 0f, LivSeed + 21);
            AmbientMoverStaging.StageRotor(sky, "TV Helicopter Tail Rotor", new[] { tailRotor }, Vector3.right, 1600f, 0f, 0f, LivSeed + 22);
        }

        int people = SpawnCrowd(group, root, spots, rng);
        Debug.Log($"[taka] living: arrival plaza at ({plazaC.x:0},{plazaC.y:0.0},{plazaC.z:0}), {houses} hamlet houses, " +
                  $"{vans} camper vans, {flagTargets.Count} flags, {people} people, 8 eagles, 3 paragliders, 1 helicopter");
    }

    // ------------------------------------------------------------------ props

    /// <summary>Road-spanning gantry: two posts, a crossbeam and a two-faced banner.</summary>
    private static void Gantry(TakaRoute route, int i, Bucket steel, Transform group, string sign, string name,
                               float clear, float aspect, float kerb)
    {
        var side = route.SideFlat(i);
        float half = kerb + 0.9f;
        var a = route.Position[i] + side * half; a.y = RoadY(route, i, half) - 0.1f;
        var b = route.Position[i] - side * half; b.y = RoadY(route, i, -half) - 0.1f;
        float top = route.Position[i].y + clear;
        Post(steel.V, steel.UV, steel.T, null, a, new Vector3(a.x, top + 0.6f, a.z), 0.14f, Color.white);
        Post(steel.V, steel.UV, steel.T, null, b, new Vector3(b.x, top + 0.6f, b.z), 0.14f, Color.white);
        Post(steel.V, steel.UV, steel.T, null, new Vector3(a.x, top + 0.55f, a.z), new Vector3(b.x, top + 0.55f, b.z), 0.08f, Color.white);
        float w = half * 2f * 0.96f, h = w * aspect;
        if (h > 1.8f) { h = 1.8f; w = h / aspect; }
        var c = route.Position[i]; c.y = top + 0.5f - h * 0.5f;
        SignBoard(group, name, sign, c, side, w, h);
    }

    private static void CamperVan(Bucket body, Bucket stripe, Bucket glass, Bucket tyre, Vector3 f, Vector3 along, Vector3 outward)
    {
        along.y = 0; along.Normalize(); outward.y = 0; outward.Normalize();
        var lift = Vector3.up * 0.42f;
        Box(body.V, body.UV, body.T, null, f + lift, along * 3.0f, outward * 1.05f, 2.35f, Color.white);          // living box
        Box(body.V, body.UV, body.T, null, f + lift + along * 3.6f, along * 0.7f, outward * 1.0f, 1.45f, Color.white); // cab nose
        Box(glass.V, glass.UV, glass.T, null, f + lift + along * 3.2f + Vector3.up * 1.05f, along * 0.45f, outward * 1.06f, 0.55f, Color.white);
        Box(stripe.V, stripe.UV, stripe.T, null, f + lift + Vector3.up * 0.95f, along * 3.02f, outward * 1.07f, 0.22f, Color.white);
        Box(glass.V, glass.UV, glass.T, null, f + lift - along * 0.9f + Vector3.up * 1.35f, along * 0.8f, outward * 1.07f, 0.5f, Color.white);
        foreach (var x in new[] { -2.1f, 2.7f })
            foreach (var z in new[] { -0.95f, 0.95f })
                Box(tyre.V, tyre.UV, tyre.T, null, f + along * x + outward * z, along * 0.36f, outward * 0.14f, 0.72f, Color.white);
        // roll-out awning on the road side
        var ro = -outward;
        stripe.Tri2(f + lift + ro * 1.05f + Vector3.up * 2.2f - along * 2.4f, f + lift + ro * 1.05f + Vector3.up * 2.2f + along * 1.4f,
                    f + lift + ro * 3.2f + Vector3.up * 1.9f - along * 2.4f);
        stripe.Tri2(f + lift + ro * 1.05f + Vector3.up * 2.2f + along * 1.4f, f + lift + ro * 3.2f + Vector3.up * 1.9f + along * 1.4f,
                    f + lift + ro * 3.2f + Vector3.up * 1.9f - along * 2.4f);
    }

    private static void TeamCar(Bucket body, Bucket glass, Bucket tyre, Bucket bikes, Vector3 c, Vector3 along, System.Random rng)
    {
        along.y = 0; along.Normalize();
        var side = Vector3.Cross(Vector3.up, along).normalized;
        var lift = Vector3.up * 0.3f;
        Box(body.V, body.UV, body.T, null, c + lift, along * 2.3f, side * 0.92f, 0.75f, Color.white);
        Box(glass.V, glass.UV, glass.T, null, c + lift + Vector3.up * 0.75f - along * 0.2f, along * 1.35f, side * 0.86f, 0.55f, Color.white);
        foreach (var x in new[] { -1.5f, 1.5f })
            foreach (var z in new[] { -0.85f, 0.85f })
                Box(tyre.V, tyre.UV, tyre.T, null, c + along * x + side * z, along * 0.32f, side * 0.12f, 0.62f, Color.white);
        // roof rack with three bikes standing along the car
        var roof = c + lift + Vector3.up * 1.32f;
        Post(bikes.V, bikes.UV, bikes.T, null, roof - along * 1.2f, roof + along * 1.2f, 0.03f, Color.white);
        for (int k = -1; k <= 1; k++)
        {
            var bc = roof + side * (k * 0.42f);
            Wheel(bikes, bc - along * 0.5f + Vector3.up * 0.34f, side, 0.33f);
            Wheel(bikes, bc + along * 0.5f + Vector3.up * 0.34f, side, 0.33f);
            Post(bikes.V, bikes.UV, bikes.T, null, bc - along * 0.4f + Vector3.up * 0.4f, bc + along * 0.35f + Vector3.up * 0.75f, 0.025f, Color.white);
        }
    }

    private static void Tripod(Bucket b, Vector3 foot)
    {
        var head = foot + Vector3.up * 1.45f;
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.PI * 2f / 3f;
            Post(b.V, b.UV, b.T, null, foot + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.35f, head, 0.015f, Color.white);
        }
        Box(b.V, b.UV, b.T, null, head, new Vector3(0.1f, 0, 0), new Vector3(0, 0, 0.16f), 0.12f, Color.white);
    }

    private static Transform BuildParaglider(Transform parent, int g, Color colour)
    {
        var mat = Plain($"TakaL_Canopy{g}", colour, 0.3f);
        if (mat.HasProperty("_Cull")) { mat.SetFloat("_Cull", 0f); EditorUtility.SetDirty(mat); }
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        const int Seg = 8; const float Span = 10f, Chord = 2.6f, Arc = 2.2f;
        for (int k = 0; k < Seg; k++)
        {
            float u0 = k / (float)Seg - 0.5f, u1 = (k + 1) / (float)Seg - 0.5f;
            Vector3 P(float u, float z) => new Vector3(u * Span, -Arc * 4f * u * u, z);
            Band(v, uv, t, P(u0, -Chord * 0.5f), P(u1, -Chord * 0.5f), P(u0, Chord * 0.5f), P(u1, Chord * 0.5f), 0f);
        }
        // pilot (a small seated box) hanging 6 m below, and the riser lines
        var pilot = new Vector3(0f, -6.5f, 0f);
        Box(v, uv, t, null, pilot, new Vector3(0.28f, 0, 0), new Vector3(0, 0, 0.35f), 0.9f, Color.white);
        foreach (var u in new[] { -0.45f, -0.2f, 0.2f, 0.45f })
            Post(v, uv, t, null, new Vector3(u * Span, -Arc * 4f * u * u, 0f), pilot + Vector3.up * 0.9f, 0.012f, Color.white);
        var mesh = Finish($"TakaL_Paraglider{g}", v.ToArray(), uv.ToArray(), t);
        var go = AddMesh(parent, $"Paraglider {g}", mesh, mat, collider: false);
        return go.transform;
    }

    private static Transform BuildHelicopter(Transform parent, out Transform rotor, out Transform tail)
    {
        var bodyMat = Plain("TakaL_HeliBody", new Color(0.92f, 0.92f, 0.90f), 0.6f);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Box(v, uv, t, null, new Vector3(0, 0, 0.4f), new Vector3(0.8f, 0, 0), new Vector3(0, 0, 1.7f), 1.7f, Color.white);   // cabin
        Box(v, uv, t, null, new Vector3(0, 0.9f, -3.6f), new Vector3(0.18f, 0, 0), new Vector3(0, 0, 2.2f), 0.35f, Color.white); // boom
        Box(v, uv, t, null, new Vector3(0, 0.9f, -5.6f), new Vector3(0.06f, 0, 0), new Vector3(0, 0, 0.35f), 1.2f, Color.white); // fin
        foreach (var x in new[] { -0.75f, 0.75f })
            Post(v, uv, t, null, new Vector3(x, -0.25f, -1.1f), new Vector3(x, -0.25f, 1.9f), 0.05f, Color.white);             // skids
        var body = AddMesh(parent, "TV Helicopter", Finish("TakaL_HeliBody", v.ToArray(), uv.ToArray(), t), bodyMat, collider: false);

        var bladeMat = Plain("TakaL_HeliBlade", new Color(0.12f, 0.12f, 0.13f), 0.3f);
        if (bladeMat.HasProperty("_Cull")) { bladeMat.SetFloat("_Cull", 0f); EditorUtility.SetDirty(bladeMat); }
        var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bt = new List<int>();
        Quad(bv, buv, bt, Vector3.zero, new Vector3(5.2f, 0, 0), new Vector3(0, 0, 0.14f));
        Quad(bv, buv, bt, Vector3.zero, new Vector3(0, 0, 5.2f), new Vector3(0.14f, 0, 0));
        var r = AddMesh(body.transform, "Main Rotor", Finish("TakaL_HeliRotor", bv.ToArray(), buv.ToArray(), bt), bladeMat, collider: false);
        r.transform.localPosition = new Vector3(0, 1.95f, 0.3f);
        rotor = r.transform;
        var tv = new List<Vector3>(); var tuv = new List<Vector2>(); var tt = new List<int>();
        Quad(tv, tuv, tt, Vector3.zero, new Vector3(0, 0.8f, 0), new Vector3(0, 0, 0.08f));
        var tr = AddMesh(body.transform, "Tail Rotor", Finish("TakaL_HeliTail", tv.ToArray(), tuv.ToArray(), tt), bladeMat, collider: false);
        tr.transform.localPosition = new Vector3(0.12f, 1.3f, -5.6f);
        tail = tr.transform;
        return body.transform;
    }

    // ------------------------------------------------------------------ capture

    /// <summary>Three review frames for the living route + arrival: the run-in to the finish, an
    /// elevated view of the arrival plaza, and a spectator stretch on the upper climb.
    /// Writes reference/good_graphics/taka_living/*.png. Nothing is saved.</summary>
    [MenuItem("MapleRide/QA/Capture Taka Living Review")]
    public static void CaptureLiving()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/taka_living"));
        Directory.CreateDirectory(outDir);
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            if (regions != null)
            {
                regions.Resolve();
                regions.currentRegionId = RegionCatalog.TakaMountains;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
            var route = TakaRoute.Load();
            int last = route.Count - 1;
            var endP = route.Position[last];
            var endT = route.Tangent[last]; endT.y = 0; endT.Normalize();
            var endS = Vector3.Cross(Vector3.up, endT).normalized;

            // 1. the run-in: rider's eye 110 m out, looking down the road at gantry + plaza
            {
                int i = route.IndexAt(route.Length - 110f);
                var p = route.Position[i]; var t = route.Tangent[i]; t.y = 0; t.Normalize();
                FillShot(Path.Combine(outDir, "taka_living_finish_runin.png"), p - t * 3f + Vector3.up * 2.0f, endP + endT * 12f + Vector3.up * 2.5f);
            }
            // 2. elevated view back over the finish line into the plaza
            FillShot(Path.Combine(outDir, "taka_living_arrival_plaza.png"),
                     endP - endT * 40f - endS * 2f + Vector3.up * 24f, endP + endT * 8f);
            // 3. spectator cluster on the upper climb (camper van, flags, fans), from across the road
            var fans = GameObject.Find("TakaShot_Fans");
            if (fans != null)
            {
                var f = fans.transform.position; var o = fans.transform.forward;   // points away from the road
                var along = Vector3.Cross(Vector3.up, o).normalized;
                FillShot(Path.Combine(outDir, "taka_living_climb_fans.png"),
                         f - o * 16f - along * 14f + Vector3.up * 3.2f, f - o * 3f + Vector3.up * 1.4f);
            }
            else Debug.LogWarning("[taka-living] no TakaShot_Fans marker");
            Debug.Log($"[taka-living] 3 frames -> {outDir}");
        }
        finally { MapleRideSceneBootstrap.DiscardChanges(); }
    }
}

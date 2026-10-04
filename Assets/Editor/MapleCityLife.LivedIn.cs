using System.Collections.Generic;
using UnityEngine;
using CityRoute = MapleCityEnvironment.CityRoute;

/// <summary>
/// LIVED-IN MAPLE CITY (claude-cowork, 2026-09-25). User brief: "it should look deliberate and
/// lived-in and bustling", with "water fountains and urban landscape".
///
/// Adds, on top of the cafes / chats / walkers:
///   * POCKET PARKS with a two-tier FOUNTAIN, lawn, hedges, flowerbeds, benches and round
///     shade trees, set into every other cross-street gap (the gaps were bare ground).
///   * ZEBRA CROSSINGS across the main road at every cross-street on asphalt.
///   * STREET CLUTTER on the building side of the pavement: lit vending-machine pairs, parked
///     bicycles, A-frame menu boards, potted plants by the doors, red post boxes.
///   * FACADE LIFE found by raycasting the real street wall ("City Facades"): air-con units,
///     balcony laundry, window planters on the upper floors, and lit vertical BLADE SIGNS on the
///     first floor - the layered signage that makes a Japanese street read as busy.
///
/// Everything is merged per material (~25 renderers total), placed through the same Occupancy
/// map the walkers and cafes use (and marked into it), kept out of Maple Row, deterministic
/// from its own rng so it never shifts the existing crowd, and rebuilt with the Life group.
/// </summary>
public static partial class MapleCityLife
{
    private const int LivedSeed = 20260926;

    // ---- PROVISIONAL tuning -------------------------------------------------------------------
    private const float ParkEveryNthCross = 2f;         // a pocket park in every 2nd cross-street gap
    private const float ParkDepthM = 12f, ParkWidthM = 9.4f;
    private const float VendingEveryM = 120f, BikesEveryM = 70f, BoardEveryM = 42f,
                        PotsEveryM = 34f, PostBoxEveryM = 420f;
    private const float FacadeStepM = 5f, BladeSignEveryM = 26f;
    private const float BuildingBandFromKerbM = 4.45f; // hard against the building line (pavement is 5 m)

    private static readonly System.Reflection.MethodInfo IsCobbleFn = typeof(MapleCityEnvironment).GetMethod(
        "IsCobble", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    private static bool Cobbled(float frac) =>
        IsCobbleFn != null ? (bool)IsCobbleFn.Invoke(null, new object[] { frac }) : (frac > 0.262f && frac < 0.53f);

    private sealed class Kit
    {
        public readonly Dictionary<string, (MeshBuilder mb, Material mat)> Parts =
            new Dictionary<string, (MeshBuilder, Material)>();

        public MeshBuilder Get(string key, Material mat)
        {
            if (!Parts.TryGetValue(key, out var p)) Parts[key] = p = (new MeshBuilder(), mat);
            return p.mb;
        }
    }

    private static int BuildLivedIn(Transform group, Transform cityRoot, CityRoute route, Occupancy occ)
    {
        var holder = new GameObject("Lived In").transform;
        holder.SetParent(group, false);
        var rng = new System.Random(LivedSeed);
        var kit = new Kit();

        int parks = BuildPocketParks(kit, route, occ, rng);
        int zebras = BuildCrossings(kit, route);
        int clutter = BuildClutter(kit, route, occ, rng);
        int facade = BuildFacadeLife(kit, cityRoot, route, rng);

        int renderers = 0;
        foreach (var kv in kit.Parts)
            if (Emit(holder, kv.Key, kv.Value.mb, kv.Value.mat) != null) renderers++;
        Debug.Log($"[maple-life] lived-in: {parks} pocket parks with fountains, {zebras} zebra crossings, " +
                  $"{clutter} street props, {facade} facade details; {renderers} renderers.");
        return parks + zebras + clutter + facade;
    }

    // =================================================================== shared materials

    private static Material MStone => CelMat("LI_Stone", new Color(0.62f, 0.60f, 0.56f), 0.10f, 0.08f);
    private static Material MLawn => CelMat("LI_Lawn", new Color(0.30f, 0.55f, 0.22f), 0.05f, 0.04f);
    private static Material MHedge => CelMat("LI_Hedge", new Color(0.17f, 0.40f, 0.16f), 0.06f, 0.05f);
    private static Material MWater => CelMat("LI_Water", new Color(0.30f, 0.62f, 0.78f), 0.85f, 0.60f);
    private static Material MSpray => CelMat("LI_Spray", new Color(0.86f, 0.94f, 0.98f), 0.60f, 0.40f);
    private static Material MWood => CelMat("LI_Wood", new Color(0.50f, 0.34f, 0.20f), 0.12f, 0.08f);
    private static Material MIron => CelMat("LI_Iron", new Color(0.14f, 0.15f, 0.16f), 0.30f, 0.20f);
    private static Material MWhite => CelMat("LI_Paint", new Color(0.90f, 0.89f, 0.85f), 0.05f, 0.04f);

    // =================================================================== pocket parks

    private static List<float> CrossStations(CityRoute route)
    {
        // Same formula as MapleCityEnvironment.SideStreetStations (private there).
        var list = new List<float>();
        for (float d = 150f; d < route.Length - 90f; d += 260f)
        {
            if (d > MapleRowBoutiques.StreetStartM - 60f && d < MapleRowBoutiques.StreetEndM + 60f) continue;
            list.Add(d);
        }
        return list;
    }

    private static int BuildPocketParks(Kit kit, CityRoute route, Occupancy occ, System.Random rng)
    {
        int n = 0, idx = 0;
        foreach (float ss in CrossStations(route))
        {
            idx++;
            if (idx % (int)ParkEveryNthCross != 0) continue;
            int i = route.IndexAt(ss);
            float frac = route.Frac(i);
            for (int side = -1; side <= 1; side += 2)
            {
                // the canal runs outside the left pavement across the opening kilometre
                if (side < 0 && frac > CanalFromFrac - 0.01f && frac < CanalToFrac + 0.01f) continue;
                var edge = PavementPoint(route, i, side, PavementWidthM + 0.6f);   // park's street edge
                var t = Flat(route.Tangent[i]);
                var inw = Flat(route.SideFlat(i) * side);
                var centre = edge + inw * (ParkDepthM * 0.5f);
                if (!occ.Free(centre, 3.5f)) continue;
                Park(kit, edge, t, inw, rng);
                occ.MarkRect(centre - new Vector3(6f, 0f, 6f), centre + new Vector3(6f, 0f, 6f));
                n++;
            }
        }
        return n;
    }

    /// <summary>One pocket park. <paramref name="o"/> is the middle of its street edge at pavement
    /// height, <paramref name="t"/> runs along the main road, <paramref name="inw"/> away from it.</summary>
    private static void Park(Kit kit, Vector3 o, Vector3 t, Vector3 inw, System.Random rng)
    {
        var rot = Quaternion.LookRotation(inw, Vector3.up);
        float W = ParkWidthM, D = ParkDepthM;
        var stone = kit.Get("Park Stone", MStone);
        var lawn = kit.Get("Park Lawn", MLawn);
        var hedge = kit.Get("Park Hedges", MHedge);

        // paved forecourt + lawn bed with a raised stone kerb
        stone.Box(o + inw * (D * 0.5f) + Vector3.up * 0.04f, new Vector3(W, 0.08f, D), rot);
        lawn.Box(o + inw * (D * 0.62f) + Vector3.up * 0.14f, new Vector3(W - 1.6f, 0.12f, D * 0.62f), rot);
        foreach (int s in new[] { -1, 1 })
            stone.Box(o + t * (s * (W * 0.5f - 0.15f)) + inw * (D * 0.5f) + Vector3.up * 0.22f, new Vector3(0.3f, 0.44f, D), rot);
        stone.Box(o + inw * (D - 0.15f) + Vector3.up * 0.22f, new Vector3(W, 0.44f, 0.3f), rot);

        // hedges down both long sides and across the back (B1: clipped to ~0.8 m, was >1 m)
        foreach (int s in new[] { -1, 1 })
            for (float z = 2.0f; z < D - 0.8f; z += 1.6f)
                hedge.Blob(o + t * (s * (W * 0.5f - 0.75f)) + inw * z + Vector3.up * 0.40f, new Vector3(0.9f, 0.8f, 1.6f), rng);

        // flowerbeds either side of the fountain
        var bloomA = kit.Get("Park Flowers Pink", CelMat("LI_FlowerPink", new Color(0.93f, 0.46f, 0.62f), 0.1f, 0.06f));
        var bloomB = kit.Get("Park Flowers Gold", CelMat("LI_FlowerGold", new Color(0.97f, 0.80f, 0.30f), 0.1f, 0.06f));
        foreach (int s in new[] { -1, 1 })
            for (int k = 0; k < 4; k++)
            {
                var p = o + t * (s * (2.9f + 0.45f * (k % 2))) + inw * (D * 0.46f + (k - 1.5f) * 0.9f) + Vector3.up * 0.34f;
                (k % 2 == 0 ? bloomA : bloomB).Blob(p, new Vector3(0.55f, 0.4f, 0.55f), rng);
            }

        // THE FOUNTAIN: stone basin, water surface, pedestal, upper bowl, spout and falling sheets
        var fc = o + inw * (D * 0.46f);
        stone.Cylinder(fc + Vector3.up * 0.30f, 2.2f, 0.60f, 28);
        kit.Get("Fountain Water", MWater).Cylinder(fc + Vector3.up * 0.52f, 1.95f, 0.08f, 28);
        stone.Cylinder(fc + Vector3.up * 0.95f, 0.32f, 1.30f, 14);
        stone.Cylinder(fc + Vector3.up * 1.62f, 0.95f, 0.18f, 20);
        kit.Get("Fountain Water", MWater).Cylinder(fc + Vector3.up * 1.70f, 0.82f, 0.04f, 20);
        stone.Cylinder(fc + Vector3.up * 1.98f, 0.14f, 0.55f, 10);
        var spray = kit.Get("Fountain Spray", MSpray);
        spray.Cylinder(fc + Vector3.up * 2.55f, 0.07f, 0.6f, 8);                     // central jet
        for (int k = 0; k < 10; k++)                                               // curtain from the upper bowl
        {
            float a = k * Mathf.PI * 2f / 10f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var right = Vector3.Cross(Vector3.up, d) * 0.28f;
            spray.QuadTwoSided(fc + d * 0.98f - right * 0.5f + Vector3.up * 0.62f, right, Vector3.up * 1.0f,
                               Vector2.zero, Vector2.one);
        }
        for (int k = 0; k < 6; k++)                                                // arcing jets into the basin
        {
            float a = k * Mathf.PI / 3f + 0.3f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int seg = 0; seg < 4; seg++)
            {
                float u0 = seg / 4f, u1 = (seg + 1) / 4f;
                Vector3 P(float u) => fc + d * (0.4f + 1.3f * u) + Vector3.up * (2.15f + 0.6f * u - 2.1f * u * u);
                var a0 = P(u0); var a1 = P(u1);
                spray.Box((a0 + a1) * 0.5f, new Vector3(0.05f, 0.05f, (a1 - a0).magnitude),
                          Quaternion.LookRotation(a1 - a0, Vector3.up));
            }
        }

        // two benches facing the fountain from the street side, a round shade tree at the back corners
        var wood = kit.Get("Park Benches", MWood);
        var iron = kit.Get("Park Iron", MIron);
        foreach (int s in new[] { -1, 1 })
        {
            var b = o + t * (s * 2.4f) + inw * 1.6f;
            var br = Quaternion.LookRotation(inw, Vector3.up);
            wood.Box(b + Vector3.up * 0.45f, new Vector3(1.7f, 0.06f, 0.45f), br);
            wood.Box(b + Vector3.up * 0.75f - inw * 0.22f, new Vector3(1.7f, 0.45f, 0.05f), br);
            iron.Box(b + t * 0.75f + Vector3.up * 0.22f, new Vector3(0.06f, 0.44f, 0.45f), br);
            iron.Box(b - t * 0.75f + Vector3.up * 0.22f, new Vector3(0.06f, 0.44f, 0.45f), br);

            var tree = o + t * (s * (W * 0.5f - 1.4f)) + inw * (D - 1.6f);
            // B4 (2026-09-27): pocket-park trees are sakura too - pink crown, dark cherry bark.
            kit.Get("Park Tree Trunks", CelMat("LI_Bark", new Color(0.235f, 0.185f, 0.180f), 0.08f, 0.05f))
               .Cylinder(tree + Vector3.up * 1.4f, 0.14f, 2.8f, 8);
            kit.Get("Park Tree Crowns", CelMat("LI_Crown", new Color(0.93f, 0.72f, 0.90f), 0.06f, 0.05f))
               .Blob(tree + Vector3.up * 3.5f, new Vector3(2.8f, 2.0f, 2.8f), rng);
        }
    }

    // =================================================================== zebra crossings

    private static int BuildCrossings(Kit kit, CityRoute route)
    {
        var paint = kit.Get("Zebra Crossings", MWhite);
        int n = 0;
        foreach (float ss in CrossStations(route))
        {
            int i = route.IndexAt(ss);
            float frac = route.Frac(i);
            if (Cobbled(frac)) continue;                         // the Old Town setts stay unmarked
            float half = HalfWidth(frac);
            var p = route.Position[i];
            var t = Flat(route.Tangent[i]);
            var s = Flat(route.SideFlat(i));
            // Japanese style: 0.45 m bars running WITH the traffic, 0.45 m gaps, 4 m long.
            for (float o = -half + 0.5f; o <= half - 0.5f; o += 0.9f)
            {
                float y = RoadY(route, i, o) + 0.025f;
                var c = new Vector3(p.x + s.x * o, y, p.z + s.z * o);
                paint.QuadTwoSided(c - s * 0.225f - t * 2f, s * 0.45f, t * 4f, Vector2.zero, Vector2.one);
            }
            // stop lines 1.5 m before the crossing on both approaches
            foreach (int dir in new[] { -1, 1 })
            {
                var c0 = new Vector3(p.x, RoadY(route, i, 0f) + 0.025f, p.z) + t * (dir * 3.6f);
                paint.QuadTwoSided(c0 - s * half - t * 0.2f, s * (2f * half), t * 0.4f, Vector2.zero, Vector2.one);
            }
            n++;
        }
        return n;
    }

    // =================================================================== street clutter

    private static int BuildClutter(Kit kit, CityRoute route, Occupancy occ, System.Random rng)
    {
        int n = 0;
        var vendColours = new[] { new Color(0.86f, 0.12f, 0.14f), new Color(0.10f, 0.35f, 0.75f), new Color(0.92f, 0.92f, 0.90f) };
        for (int side = -1; side <= 1; side += 2)
        {
            float vend = 60f + (float)rng.NextDouble() * 60f, bikes = 30f, board = 12f, pots = 20f,
                  post = 200f + (float)rng.NextDouble() * 100f;
            for (float d = 5f; d < route.Length - 5f; d += 1f)
            {
                if (InRow(d)) continue;
                int i = route.IndexAt(d);
                float frac = route.Frac(i);
                bool canalSide = side < 0 && frac > CanalFromFrac - 0.01f && frac < CanalToFrac + 0.01f;
                var t = Flat(route.Tangent[i]);
                var inw = Flat(route.SideFlat(i) * side);
                var face = Quaternion.LookRotation(-inw, Vector3.up);        // facing the road

                if (d >= vend && !canalSide)
                {
                    vend = d + VendingEveryM * Mathf.Lerp(0.6f, 1.4f, (float)rng.NextDouble());
                    var c = PavementPoint(route, i, side, BuildingBandFromKerbM);
                    if (occ.Free(c, 1.2f))
                    {
                        int machines = 2 + rng.Next(2);
                        for (int m = 0; m < machines; m++)
                        {
                            var mc = c + t * ((m - (machines - 1) * 0.5f) * 1.05f);
                            int col = rng.Next(vendColours.Length);
                            kit.Get($"Vending Body {col}", CelMat($"LI_Vend{col}", vendColours[col], 0.35f, 0.25f))
                               .Box(mc + Vector3.up * 0.92f, new Vector3(1.0f, 1.84f, 0.72f), face);
                            // lit display window (drinks), then the dark pick-up slot below it
                            kit.Get("Vending Display", Unlit("LI_VendDisplay", null, new Color(1.0f, 0.97f, 0.88f)))
                               .QuadTwoSided(mc - inw * 0.365f - t * 0.42f + Vector3.up * 0.95f, t * 0.84f, Vector3.up * 0.72f, Vector2.zero, Vector2.one);
                            kit.Get("Park Iron", MIron)
                               .QuadTwoSided(mc - inw * 0.365f - t * 0.35f + Vector3.up * 0.22f, t * 0.7f, Vector3.up * 0.22f, Vector2.zero, Vector2.one);
                        }
                        occ.MarkDisc(c, 1.3f);
                        n += machines;
                    }
                }
                if (d >= bikes && !canalSide)
                {
                    bikes = d + BikesEveryM * Mathf.Lerp(0.6f, 1.4f, (float)rng.NextDouble());
                    var c = PavementPoint(route, i, side, BuildingBandFromKerbM - 0.1f);
                    if (occ.Free(c, 1.6f))
                    {
                        int count = 3 + rng.Next(3);
                        for (int b = 0; b < count; b++)
                            ParkedBike(kit, c + t * ((b - (count - 1) * 0.5f) * 0.62f), t, inw, rng);
                        occ.MarkDisc(c, 1.8f);
                        n += count;
                    }
                }
                if (d >= board)
                {
                    board = d + BoardEveryM * Mathf.Lerp(0.5f, 1.5f, (float)rng.NextDouble());
                    var c = PavementPoint(route, i, side, 3.5f);
                    if (occ.Free(c, 0.5f))
                    {
                        var chalk = kit.Get("A-Frame Boards", CelMat("LI_Chalk", new Color(0.12f, 0.13f, 0.12f), 0.1f, 0.05f));
                        foreach (int f in new[] { -1, 1 })
                            chalk.Box(c + inw * (f * 0.14f) + Vector3.up * 0.48f, new Vector3(0.6f, 0.95f, 0.04f),
                                      Quaternion.LookRotation(inw, Vector3.up) * Quaternion.Euler(f * 10f, 0f, 0f));
                        occ.MarkDisc(c, 0.5f);
                        n++;
                    }
                }
                if (d >= pots && !canalSide)
                {
                    pots = d + PotsEveryM * Mathf.Lerp(0.5f, 1.5f, (float)rng.NextDouble());
                    var c = PavementPoint(route, i, side, BuildingBandFromKerbM + 0.1f);
                    if (occ.Free(c, 0.5f))
                    {
                        kit.Get("Plant Pots", CelMat("LI_Terracotta", new Color(0.66f, 0.36f, 0.24f), 0.08f, 0.05f))
                           .Cylinder(c + Vector3.up * 0.225f, 0.3f, 0.45f, 12);
                        kit.Get("Park Hedges", MHedge).Blob(c + Vector3.up * 0.68f, new Vector3(0.62f, 0.42f, 0.62f), rng);   // B1: top ~0.89 m, was ~1.35 m
                        occ.MarkDisc(c, 0.45f);
                        n++;
                    }
                }
                if (d >= post)
                {
                    post = d + PostBoxEveryM * Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble());
                    var c = PavementPoint(route, i, side, 3.3f);
                    if (occ.Free(c, 0.4f))
                    {
                        var red = kit.Get("Post Boxes", CelMat("LI_PostRed", new Color(0.80f, 0.10f, 0.10f), 0.35f, 0.25f));
                        red.Cylinder(c + Vector3.up * 0.65f, 0.26f, 1.3f, 14);
                        red.Cylinder(c + Vector3.up * 1.34f, 0.3f, 0.08f, 14);
                        occ.MarkDisc(c, 0.35f);
                        n++;
                    }
                }
            }
        }
        return n;
    }

    /// <summary>A bicycle parked parallel to the building line: two wheels, diamond frame, bars, saddle.</summary>
    private static void ParkedBike(Kit kit, Vector3 foot, Vector3 t, Vector3 inw, System.Random rng)
    {
        var frameCols = new[] { new Color(0.72f, 0.12f, 0.14f), new Color(0.12f, 0.30f, 0.62f),
                                new Color(0.90f, 0.88f, 0.84f), new Color(0.16f, 0.45f, 0.30f), new Color(0.12f, 0.12f, 0.13f) };
        int ci = rng.Next(frameCols.Length);
        var frame = kit.Get($"Bike Frames {ci}", CelMat($"LI_BikeFrame{ci}", frameCols[ci], 0.45f, 0.35f));
        var tyre = kit.Get("Bike Tyres", MIron);
        // wheels in the vertical plane containing inw (bikes stand nose-in to the wall)
        var fwd = inw;
        const float R = 0.34f;
        Vector3 rear = foot - fwd * 0.52f + Vector3.up * R, front = foot + fwd * 0.52f + Vector3.up * R;
        foreach (var hub in new[] { rear, front })
            for (int k = 0; k < 12; k++)
            {
                float a0 = k * Mathf.PI * 2f / 12f, a1 = (k + 1) * Mathf.PI * 2f / 12f;
                var p0 = hub + (fwd * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0)) * R;
                var p1 = hub + (fwd * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1)) * R;
                tyre.Box((p0 + p1) * 0.5f, new Vector3(0.04f, 0.04f, (p1 - p0).magnitude), Quaternion.LookRotation(p1 - p0, t));
            }
        var seat = foot - fwd * 0.12f + Vector3.up * 0.86f;
        var head = foot + fwd * 0.42f + Vector3.up * 0.86f;
        var bb = foot + Vector3.up * 0.30f;
        foreach (var (a, b) in new[] { (rear, seat), (seat, bb), (bb, rear), (seat, head), (head, bb), (head, front) })
            frame.Box((a + b) * 0.5f, new Vector3(0.045f, 0.045f, (b - a).magnitude), Quaternion.LookRotation(b - a, t));
        tyre.Box(seat + Vector3.up * 0.06f, new Vector3(0.14f, 0.05f, 0.26f), Quaternion.LookRotation(fwd, Vector3.up));
        tyre.Box(head + Vector3.up * 0.12f, new Vector3(0.46f, 0.035f, 0.035f), Quaternion.LookRotation(fwd, Vector3.up));
    }

    // =================================================================== facade life

    private static int BuildFacadeLife(Kit kit, Transform cityRoot, CityRoute route, System.Random rng)
    {
        var fac = FindChild(cityRoot, "City Facades");
        var mf = fac != null ? fac.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) { Debug.LogWarning("[maple-life] no 'City Facades' mesh - facade life skipped."); return 0; }

        // Temporary collider so the details sit exactly on the real, modelled street wall.
        var col = fac.gameObject.AddComponent<MeshCollider>();
        col.sharedMesh = mf.sharedMesh;
        Physics.SyncTransforms();
        int n = 0;
        try
        {
            var signCols = new[] { new Color(1.0f, 0.36f, 0.30f), new Color(1.0f, 0.78f, 0.32f),
                                   new Color(0.40f, 0.78f, 1.0f), new Color(0.50f, 0.95f, 0.60f), new Color(1.0f, 0.97f, 0.92f) };
            var clothCols = new[] { new Color(0.92f, 0.92f, 0.90f), new Color(0.45f, 0.62f, 0.85f),
                                    new Color(0.93f, 0.62f, 0.66f), new Color(0.95f, 0.85f, 0.45f) };
            float[] storeys = { 7.9f, 11.3f, 14.7f, 18.1f, 21.5f, 24.9f };
            for (int side = -1; side <= 1; side += 2)
            {
                float nextSign = 10f + (float)rng.NextDouble() * 20f;
                for (float d = 3f; d < route.Length - 3f; d += FacadeStepM)
                {
                    if (InRow(d)) continue;
                    int i = route.IndexAt(d);
                    var inw = Flat(route.SideFlat(i) * side);
                    var t = Flat(route.Tangent[i]);
                    var basePt = PavementPoint(route, i, side, 2.5f);

                    // lit vertical blade sign on the first floor
                    if (d >= nextSign && Hit(basePt + Vector3.up * 5.6f, inw, out var hs))
                    {
                        nextSign = d + BladeSignEveryM * Mathf.Lerp(0.6f, 1.4f, (float)rng.NextDouble());
                        var root = hs - inw * 0.05f;
                        int sc = rng.Next(signCols.Length);
                        float h = Mathf.Lerp(1.8f, 3.2f, (float)rng.NextDouble());
                        kit.Get("Blade Sign Frames", MIron).Box(root - inw * 0.55f + Vector3.up * (h * 0.5f),
                            new Vector3(0.14f, h + 0.1f, 0.95f), Quaternion.LookRotation(inw, Vector3.up));
                        var glow = kit.Get($"Blade Sign Glow {sc}", Unlit($"LI_Blade{sc}", null, signCols[sc]));
                        foreach (int f in new[] { -1, 1 })       // both broad faces, seen up and down the street
                            glow.QuadTwoSided(root - inw * 0.10f + t * (f * 0.076f) + Vector3.up * 0.05f,
                                              -inw * 0.85f, Vector3.up * (h - 0.1f), Vector2.zero, Vector2.one);
                        n++;
                    }

                    foreach (float y in storeys)
                    {
                        var from = basePt + Vector3.up * y;
                        if (!Hit(from, inw, out var hp)) break;          // above the roof: stop climbing
                        double roll = rng.NextDouble();
                        if (roll < 0.10)
                        {   // air-con condenser on a bracket, below the window
                            var c = hp - inw * 0.2f + Vector3.up * -1.1f + t * ((float)rng.NextDouble() - 0.5f);
                            var r = Quaternion.LookRotation(-inw, Vector3.up);
                            kit.Get("AC Units", CelMat("LI_ACUnit", new Color(0.84f, 0.84f, 0.80f), 0.2f, 0.12f))
                               .Box(c, new Vector3(0.8f, 0.56f, 0.3f), r);
                            kit.Get("Park Iron", MIron).QuadTwoSided(c - inw * 0.151f - t * 0.2f - Vector3.up * 0.17f, t * 0.4f, Vector3.up * 0.34f, Vector2.zero, Vector2.one);
                            n++;
                        }
                        else if (roll < 0.15)
                        {   // balcony laundry on a pole
                            var c = hp - inw * 0.55f + Vector3.up * 0.4f;
                            kit.Get("Park Iron", MIron).Box(c, new Vector3(0.03f, 0.03f, 1.8f), Quaternion.LookRotation(t, Vector3.up));
                            int items = 2 + rng.Next(3);
                            for (int k = 0; k < items; k++)
                            {
                                int cc = rng.Next(clothCols.Length);
                                float w = Mathf.Lerp(0.35f, 0.6f, (float)rng.NextDouble());
                                float hh = Mathf.Lerp(0.4f, 0.75f, (float)rng.NextDouble());
                                var o = c + t * (-0.8f + k * 0.5f) - Vector3.up * hh;
                                kit.Get($"Laundry {cc}", CelMat($"LI_Cloth{cc}", clothCols[cc], 0.05f, 0.03f))
                                   .QuadTwoSided(o, t * w, Vector3.up * hh, Vector2.zero, Vector2.one);
                            }
                            n++;
                        }
                        else if (roll < 0.21)
                        {   // window flower box
                            var c = hp - inw * 0.14f + Vector3.up * -0.75f;
                            var r = Quaternion.LookRotation(-inw, Vector3.up);
                            kit.Get("Plant Pots", CelMat("LI_Terracotta", new Color(0.66f, 0.36f, 0.24f), 0.08f, 0.05f))
                               .Box(c, new Vector3(1.0f, 0.22f, 0.26f), r);
                            bool pink = rng.NextDouble() < 0.5;
                            kit.Get(pink ? "Park Flowers Pink" : "Park Flowers Gold",
                                    pink ? CelMat("LI_FlowerPink", new Color(0.93f, 0.46f, 0.62f), 0.1f, 0.06f)
                                         : CelMat("LI_FlowerGold", new Color(0.97f, 0.80f, 0.30f), 0.1f, 0.06f))
                               .Blob(c + Vector3.up * 0.22f, new Vector3(0.3f, 0.28f, 0.9f), rng);
                            n++;
                        }
                    }
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(col);
        }
        return n;
    }

    /// <summary>Horizontal ray from the pavement toward the street wall (max 24 m).</summary>
    private static bool Hit(Vector3 from, Vector3 dir, out Vector3 point)
    {
        if (Physics.Raycast(from, dir, out var hit, 24f)) { point = hit.point; return true; }
        point = Vector3.zero;
        return false;
    }
}

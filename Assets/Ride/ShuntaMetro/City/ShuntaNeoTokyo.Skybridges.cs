using UnityEngine;

public sealed partial class ShuntaNeoTokyo
{
    // Glazed pedestrian skybridges that cross the street and run into the buildings on both sides.
    // Deterministic per chunk, built from the same mesh bags as the rest of the city, and never
    // touching the carriageway: the deck underside sits above the 10.3 m utility poles and the
    // 7 m streetlight arms, and far below the elevated sky-train lines (62 m and up).
    public const float SkybridgeDeckHeight = 12f;     // underside of the deck above the road surface
    public const float SkybridgeLegOffset = 3.4f;     // leg centre from the carriageway edge (beyond the 2.4 m utility poles)
    public const float SkybridgeChance = .55f;        // share of chunks that get a bridge
    public const float SkybridgeEdgeMarginKm = .03f;  // keeps bridges in neighbouring chunks at least 60 m apart
    public int SkybridgeCount { get; private set; }

    /// <summary>Km of this chunk's bridge, or -1 when the chunk has none. Pure and deterministic.</summary>
    public static float SkybridgeKmInChunk(int id, float start, float end)
    {
        var random = new System.Random(48611 + id * 7919);
        double roll = random.NextDouble(), where = random.NextDouble();
        float lo = start + SkybridgeEdgeMarginKm, hi = end - SkybridgeEdgeMarginKm;
        if (roll >= SkybridgeChance || hi <= lo) return -1f;
        return lo + (float)where * (hi - lo);
    }

    void BuildSkybridges(int id, float start, float end, ShuntaLookKit.MeshBag coarse, ShuntaLookKit.MeshBag fine, Block block)
    {
        float km = SkybridgeKmInChunk(id, start, end);
        if (km < 0f) return;
        // Dressed city on both sides only: no tunnel, coast, interchange or Rainbow Bridge zones.
        foreach (float k in new[] { km - .012f, km, km + .012f })
        {
            var z = route.Course.ZoneAtKm(Mathf.Clamp(k, 0f, route.Course.distanceKm));
            if (z == null || !(z.index == 1 || z.index == 2 || z.index == 7 || z.index == 8)) return;
        }
        Vector3 p = route.PositionAtKm(km), f = route.TangentAtKm(km);
        f.y = 0f;
        if (f.sqrMagnitude < .0001f) return;
        f.Normalize();
        Vector3 right = new Vector3(f.z, 0f, -f.x);
        float edge = route.roadWidth * .5f, leg = edge + SkybridgeLegOffset, half = leg + 2.2f;
        for (int side = -1; side <= 1; side += 2)
            if (!ClearOfRoad(p + right * side * leg, km, edge + 1.2f)) return;

        var q = Quaternion.LookRotation(f);
        float deck = SkybridgeDeckHeight;
        Vector3 C(float y) => p + Vector3.up * y;
        Material trim = (id & 1) == 0 ? neonPink : neonBlue;

        // Corridor: slab, glazing, roof and a lit window band.
        coarse.Box(concrete, C(deck + .35f), q, new Vector3(half * 2f, .7f, 3.4f));
        coarse.Box(glass, C(deck + 1.8f), q, new Vector3(half * 2f - .4f, 2.2f, 3.0f));
        coarse.Box(metal, C(deck + 3.05f), q, new Vector3(half * 2f, .25f, 3.4f));
        fine.Box(warm, C(deck + 2.1f), q, new Vector3(half * 2f - .6f, .22f, 3.06f));
        fine.Box(trim, C(deck - .02f), q, new Vector3(half * 2f - .4f, .08f, .4f));

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 foot = p + right * side * leg;
            // Support leg on the pavement edge, a diagonal brace, and the housing that enters the facade.
            coarse.Box(concrete, foot + Vector3.up * ((deck - .6f) * .5f), q, new Vector3(.6f, deck - .6f, .8f));
            coarse.Beam(metal, foot + Vector3.up * 4.5f, p + right * side * (leg - 3f) + Vector3.up * (deck - .1f), .22f);
            coarse.Box(concrete, p + right * side * (half - .3f) + Vector3.up * (deck + 1.4f), q, new Vector3(1.2f, 3.4f, 3.8f));
        }
        block.bridges++; block.details += 8;
    }
}

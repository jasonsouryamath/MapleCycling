using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FUJI RIDGE - fuji2 (2026-09-26) performance + far-field pass.
///
/// DETAIL TILES. Small props (cafe furniture, parked bikes, stalls, laundry, lanterns, small scree
/// clasts and shrubs) no longer ride in the per-km pilgrimage chunks. They go into 160 m world-grid
/// tiles that cast NO shadows and sit in a one-level LODGroup culled beyond ~DetailCullM, so the
/// GPU stops drawing (and shadow-rendering) thousands of tiny triangles a kilometre away.
///
/// FAR FIELD. The upper volcanic slopes were bare beyond ~40 m from the road. BuildVolcanicFarField
/// drapes ground-conforming ash/scree bands and snow patches (Height()-sampled grids, so nothing
/// floats) and scatters low lava-rock fields 45-230 m out. All of it is merged into per-km "far"
/// meshes with shadows off (low relief; its shadows were never readable at that range).
/// </summary>
public static partial class FujiRidgeEnvironment
{
    private const float DetailTileM = 160f;
    private const float DetailCullM = 280f;

    private static readonly Dictionary<long, PMesh> _detailTiles = new Dictionary<long, PMesh>();
    private static readonly Dictionary<int, PMesh> _farChunks = new Dictionary<int, PMesh>();

    private static PMesh DetailAt(Vector3 p)
    {
        long key = ((long)Mathf.FloorToInt(p.x / DetailTileM) << 32) ^ (uint)Mathf.FloorToInt(p.z / DetailTileM);
        if (!_detailTiles.TryGetValue(key, out var m)) _detailTiles[key] = m = new PMesh();
        return m;
    }

    private static PMesh FarAt(FujiRoute route, int i)
    {
        int key = Mathf.FloorToInt(route.Distance[i] / 1000f);
        if (!_farChunks.TryGetValue(key, out var m)) _farChunks[key] = m = new PMesh();
        return m;
    }

    private static void ResetDetailTiles() { _detailTiles.Clear(); _farChunks.Clear(); }

    /// <summary>Writes the detail tiles and far chunks. Returns (detail tris, far tris).</summary>
    private static (int detail, int far) FlushDetailTiles(Transform group)
    {
        int dTris = 0, fTris = 0, n = 0;
        var dRoot = new GameObject("Fuji Detail (culled, no shadows)").transform;
        dRoot.SetParent(group, false);
        foreach (var kv in _detailTiles)
        {
            var m = kv.Value;
            if (m.T.Count == 0) continue;
            int kx = (int)(kv.Key >> 32), kz = (int)(kv.Key & 0xffffffff);
            var mesh = Finish($"Fuji_Detail_{kx}_{kz}", m.V.ToArray(), m.UV.ToArray(), m.T);
            var go = AddMesh(dRoot, $"Detail {kx},{kz}", mesh, _pilgrimMat, collider: false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lod = go.AddComponent<LODGroup>();
            var b = mesh.bounds;
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            // Unity: relative height = size / (distance * 2 tan(fov/2)); fov 60 -> 1.155.
            float h = Mathf.Clamp(size / (DetailCullM * 1.155f), 0.005f, 0.95f);
            lod.SetLODs(new[] { new LOD(h, new Renderer[] { mr }) });
            lod.fadeMode = LODFadeMode.None;
            lod.RecalculateBounds();
            dTris += m.T.Count / 3; n++;
        }
        var fRoot = new GameObject("Fuji Far Field (no shadows)").transform;
        fRoot.SetParent(group, false);
        foreach (var kv in _farChunks)
        {
            var m = kv.Value;
            if (m.T.Count == 0) continue;
            var mesh = Finish($"Fuji_Far_{kv.Key}", m.V.ToArray(), m.UV.ToArray(), m.T);
            var go = AddMesh(fRoot, $"Far km {kv.Key}", mesh, _pilgrimMat, collider: false);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fTris += m.T.Count / 3;
        }
        Debug.Log($"[fuji] fuji2 detail tiles: {n} tiles, {dTris} tris (no shadows, culled > {DetailCullM:0} m); " +
                  $"far field: {_farChunks.Count} chunks, {fTris} tris (no shadows).");
        return (dTris, fTris);
    }

    /// <summary>A ground-draped irregular patch (grid sampled through Height, lifted 6 cm).</summary>
    private static void Drape(PMesh m, Vector3 c, float rx, float rz, float yaw, int row, float bright,
                              System.Random rnd, int grid = 4, float lift = 0.06f)
    {
        var ax = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
        var az = new Vector3(-ax.z, 0f, ax.x);
        int start = m.V.Count;
        float[] edge = new float[8];
        for (int k = 0; k < 8; k++) edge[k] = 0.75f + (float)rnd.NextDouble() * 0.35f;
        for (int j = 0; j <= grid; j++)
        for (int i = 0; i <= grid; i++)
        {
            float u = i / (float)grid * 2f - 1f, v = j / (float)grid * 2f - 1f;
            // Pull the square grid into a lumpy ellipse.
            float ang = Mathf.Atan2(v, u);
            float e = edge[Mathf.Clamp(Mathf.RoundToInt((ang + Mathf.PI) / (Mathf.PI * 2f) * 8f) % 8, 0, 7)];
            float len = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
            float rr = len * e;
            float nu = len > 1e-4f ? Mathf.Cos(ang) * rr : 0f, nv = len > 1e-4f ? Mathf.Sin(ang) * rr : 0f;
            var p = c + ax * (nu * rx) + az * (nv * rz);
            p.y = Height(p.x, p.z) + lift;
            float br = bright * (len > 0.8f ? 0.85f : 1f) * (0.9f + (float)rnd.NextDouble() * 0.2f);
            m.Vert(p, row, br);
        }
        int w = grid + 1;
        for (int j = 0; j < grid; j++)
        for (int i = 0; i < grid; i++)
        {
            int a = start + j * w + i;
            m.Tri(a, a + 1, a + w + 1, Vector3.up);
            m.Tri(a, a + w + 1, a + w, Vector3.up);
        }
    }

    // ---- copilot F6 (2026-09-26): far-field contrast. All PROVISIONAL art tuning. -------------
    /// <summary>Ash/pumice band half-length and half-width ranges, metres (was 6-16 x 2.5-5).</summary>
    private const float AshBandLenMinM = 14f, AshBandLenMaxM = 42f, AshBandWideMinM = 4f, AshBandWideMaxM = 11f;
    /// <summary>Share of bands drawn in the warm pale ash row (the rest: red scoria, then grey scree).</summary>
    private const float AshWarmShare = 0.55f, ScoriaShare = 0.25f;
    /// <summary>Lava-field dark base patch radius range and rock count range.</summary>
    private const float LavaPatchMinM = 9f, LavaPatchMaxM = 22f;
    private const int LavaRocksMin = 7, LavaRocksMax = 15;
    /// <summary>Drape lift for the (bigger) far patches, so coarse terrain never pokes through.</summary>
    private const float FarDrapeLiftM = 0.14f;
    /// <summary>Nearest lateral offset of ash bands / lava fields from the centreline (was 45 / 42 m).</summary>
    private const float AshBandNearM = 16f, LavaNearM = 24f;

    /// <summary>Upper volcanic slopes: scree bands, lava-rock fields and snow patches 45-230 m out.</summary>
    private static void BuildVolcanicFarField(FujiRoute route, System.Random rnd)
    {
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        int bands = 0, fields = 0, snow = 0, rocks = 0;
        float snowFrom = route.MaxY - 320f;
        for (int i = 4; i < route.Count - 4; i += 7)
        {
            float y = route.Position[i].y;
            if (y < TreeLineY - 40f) continue;
            float high = Mathf.InverseLerp(TreeLineY - 40f, TreeLineY + 250f, y);
            var m = FarAt(route, i);
            for (int s = -1; s <= 1; s += 2)
            {
                // Ash / pumice / scoria band: long ellipse running roughly down the fall line.
                // F6: bigger, and in rows that contrast with the mauve-grey ground (warm pale
                // pumice-ash, red scoria), not the old ash row that matched the ground.
                if (rnd.NextDouble() < 0.8 * Mathf.Max(0.35f, high))
                {
                    // F6: the bands now lie roughly along the contour (parallel to the road, +-25 deg)
                    // and start AshBandNearM out, so they read from the saddle instead of only as
                    // slivers on the horizon.
                    float off = s * R(AshBandNearM, 210f);
                    Foot(route, i, off, out var f, out _);
                    float rx = R(AshBandLenMinM, AshBandLenMaxM), rz = R(AshBandWideMinM, AshBandWideMaxM);
                    var tg = route.Tangent[i];
                    float yaw = Mathf.Atan2(tg.z, tg.x) + R(-0.44f, 0.44f);
                    NearestStation(f.x, f.z, out float dr);
                    if (dr > CorridorClearM + rz * 1.35f + 3f)
                    {
                        double pick = rnd.NextDouble();
                        int row = pick < AshWarmShare ? PAshWarm : pick < AshWarmShare + ScoriaShare ? PScoria : PAsh;
                        float br = row == PAsh ? R(0.8f, 0.98f) : R(0.62f, 0.95f);
                        Drape(m, f, rx, rz, yaw, row, br, rnd, 7, FarDrapeLiftM + R(0f, 0.05f));
                        bands++;
                    }
                }
                // Lava-rock field: a near-black basalt apron with a cluster of dark blocky rocks on
                // it, sunk so downhill edges don't float. F6: larger and much darker.
                if (rnd.NextDouble() < 0.8 * Mathf.Max(0.35f, high))
                {
                    float off = s * R(LavaNearM, 230f);
                    Foot(route, i, off, out var f, out _);
                    NearestStation(f.x, f.z, out float dr);
                    float pr = R(LavaPatchMinM, LavaPatchMaxM);
                    if (dr > CorridorClearM + pr + 4f)
                    {
                        Drape(m, f, pr, pr * R(0.55f, 0.9f), R(0f, Mathf.PI), PLava, R(0.35f, 0.6f), rnd, 7, FarDrapeLiftM - 0.04f + R(0f, 0.02f));
                        int n = rnd.Next(LavaRocksMin, LavaRocksMax + 1);
                        for (int k = 0; k < n; k++)
                        {
                            var p = f + new Vector3(R(-pr, pr), 0f, R(-pr, pr)) * 0.7f;
                            p.y = Height(p.x, p.z);
                            float r = R(0.9f, 3.4f);
                            m.Blob(p - Vector3.up * r * 0.35f, r, R(0.4f, 0.7f), PLava, R(0.45f, 0.95f), rnd, 0.35f);
                            rocks++;
                        }
                        fields++;
                    }
                }
                // Snow patches near the summit.
                if (y > snowFrom && rnd.NextDouble() < 0.7)
                {
                    float off = s * R(30f, 180f);
                    Foot(route, i, off, out var f, out _);
                    NearestStation(f.x, f.z, out float dr);
                    float rx = R(4f, 11f);
                    if (dr > CorridorClearM + rx + 2f)
                    {
                        Drape(m, f, rx, R(2f, 6f), R(0f, Mathf.PI), PSnow, R(0.8f, 1f), rnd);
                        snow++;
                    }
                }
            }
        }
        Debug.Log($"[fuji] fuji2 far field: {bands} scree bands, {fields} lava fields ({rocks} rocks), {snow} snow patches " +
                  $"(snow above y {snowFrom:0}).");
    }
}

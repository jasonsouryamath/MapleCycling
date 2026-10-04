using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using HD = UnityEngine.Rendering.HighDefinition;

/// <summary>
/// NAGISA BAY (board task B5) - "Where the City Meets the Sea". A tropical resort beach town on
/// the harbour peninsula between Maple City and Minato Port (reference/bad_graphics/new_map.png,
/// red circle).
///
/// The course (tools/blender/build_nagisa_route.py -> NagisaRoute.json, ~16.2 km, open):
///   marina front -> pastel waterfront -> resort beach past the hero hotel -> hill road ->
///   Palm Ridge KOM (~207 m) -> ridge loop -> east coast road -> SE bridge approach.
///
/// This file is the REGION CORE: route + ground loader, terrain, sea, road, bridge approach and
/// the HDRP atmosphere. Dressing lives in partial files (NagisaBayEnvironment.*.cs) that plug in
/// through the partial hooks below, so each milestone is a reviewable, separate diff.
///
/// IDEMPOTENCY: <see cref="Apply"/> destroys every scene root named EXACTLY
/// "Nagisa Bay Environment" and rebuilds it, so re-running converges. Nothing outside that root
/// is created except through ShiosaiCoastEnvironment.SetupRegionSystems, the single shared
/// authority for the RegionDirector (same as Fuji/Minato). Every generated mesh is persisted
/// under Assets/Environment/NagisaBay/Meshes, and materials are flushed with SaveAssets.
///
/// ALL numbers here are PROVISIONAL art/tuning values unless stated otherwise.
/// </summary>
public static partial class NagisaBayEnvironment
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    public const string Dir = "Assets/Environment/NagisaBay";
    public const string RoutePath = Dir + "/NagisaRoute.json";
    private const string GroundPath = Dir + "/NagisaGround.bytes";
    private const string MaterialDir = Dir + "/Materials";
    private const string MeshDir = Dir + "/Meshes";
    public const string ModelDir = Dir + "/Models";
    private const string TextureDir = Dir + "/Textures";
    private const string CoastTex = "Assets/Environment/ShiosaiCoast/Textures";
    private const string SakuraTex = "Assets/Environment/SakuraPass/Textures";
    public const string NagisaTex = Dir + "/Textures";

    public const string RootName = "Nagisa Bay Environment";
    public const string RegionId = "nagisa_bay";
    public const string CourseId = "nagisa_bay_loop";

    private const string CelShader = "MapleRide/HDRP/CelLit";
    private const string TerrainShader = "MapleRide/HDRP/Terrain";
    private const string RoadShader = "MapleRide/HDRP/Road";
    private const string OceanShader = "MapleRide/HDRP/Ocean";
    private const string ShallowsShader = "MapleRide/HDRP/Shallows";
    private const string FoliageShader = "MapleRide/HDRP/Foliage";

    // ---- road cross-section (MUST match NagisaRoute.json / RouteFollower) --------------------
    public const float RoadHalfWidth = 3.5f;
    private const float ShoulderWidth = 0.6f;
    private const float RoadCrown = 0.06f;
    private const float VergeDropM = 0.05f;
    private const float LaneLineOffsetM = 1.7f;
    private const float RiderLiftM = 0.02f;
    private static float CrownAt(float o) =>
        RoadCrown * (1f - Mathf.Pow(Mathf.Min(Mathf.Abs(o), RoadHalfWidth + ShoulderWidth) /
                                     (RoadHalfWidth + ShoulderWidth), 2f));
    private static readonly float RoadSurfaceLiftM = RiderLiftM + VergeDropM - CrownAt(LaneLineOffsetM);
    private const float MarkingLiftM = 0.015f;
    /// <summary>Road keep-out half width for ALL scatter/props (road + ~6 m corridor rule).</summary>
    public const float CorridorKeepOutM = RoadHalfWidth + ShoulderWidth + 3.0f;

    // ---- terrain (PROVISIONAL) -----------------------------------------------------------
    private const int ChunkCells = 64;
    private const float FineRadiusM = 450f;      // native 6 m cells within this of the road
    private const float MidRadiusM = 1500f;      // 12 m cells, else 24 m
    private const float SkipBelowY = -1.2f;      // quads fully under this are hidden by the opaque sea

    // ---- sea (PROVISIONAL) ---------------------------------------------------------------
    public const float SeaLevelY = 0f;
    private const float ShallowsReachM = 420f;
    private const float ShallowsDepthScale = 20f;

    // ---- bridge approach -----------------------------------------------------------------
    private const float BridgeDeckHalfW = 5.4f;
    private const float PierSpacingM = 42f;

    // =================================================================== entry point

    [MenuItem("MapleRide/Environment/Build Nagisa Bay")]
    public static void Apply()
    {
        bool headless = Application.isBatchMode;
        if (headless && EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(MeshDir);
        Directory.CreateDirectory(TextureDir);
        Directory.CreateDirectory(ModelDir);
        MaterialCache.Clear();
        _meshNames.Clear();

        var route = NagisaRoute.Load();
        var ground = NagisaGround.Load();
        int healed = HealGround(ground, route);
        int benched = BenchUnderRoad(ground, route);
        Debug.Log($"[nagisa] ground heal: {healed} nodes (seam step + pale scar reclass)");
        _route = route; _ground = ground;
        Debug.Log($"[nagisa] route {route.Count} samples, {route.Length / 1000f:0.00} km, " +
                  $"y {route.MinY:0.0}..{route.MaxY:0.0} m, bridge from {route.BridgeStartM:0} m; " +
                  $"ground {ground.Nx}x{ground.Nz} @ {ground.Cell} m; {benched} nodes benched under the road");

        foreach (var stale in FindRootsByExactName(RootName))
            UnityEngine.Object.DestroyImmediate(stale);
        var root = new GameObject(RootName).transform;

        BuildTerrain(root);
        BuildSea(root);
        BuildRoad(root);
        BuildBridgeApproach(root);
        ConfigureAtmosphere(root);

        // Milestone dressing (partial files). Each is a no-op until its file exists.
        BuildResort(root);
        BuildTown(root);
        BuildPeople(root);
        BuildMover(root);
        RunOverhaulStages(root, null);   // NB0 hook: every [NagisaStage] package rebuilds with a full Apply

        var graph = RouteGraphBaker.BakeAsset();
        ShiosaiCoastEnvironment.SetupRegionSystems(graph);

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.ApplyEnvironmentVisibility();
        }
        else Debug.LogWarning("[nagisa] no RegionDirector in scene.");

        int renderers = root.GetComponentsInChildren<MeshRenderer>(true).Length;
        long tris = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.LongLength / 3;
        Debug.Log($"[nagisa] built '{RootName}': {renderers} renderers, {tris:N0} unique-mesh tris " +
                  $"(generated geometry; instanced GLBs logged separately).");

        AssetDatabase.SaveAssets();
        if (headless)
        {
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[nagisa] saved '{active.path}'.");
        }
    }

    static partial void BuildResort(Transform root);
    static partial void BuildTown(Transform root);
    static partial void BuildPeople(Transform root);
    static partial void BuildMover(Transform root);

    private static NagisaRoute _route;
    private static NagisaGround _ground;

    // =================================================================== data

    [Serializable] private class SampleDto { public float[] p, t, s, u; public float bank, d; }
    [Serializable] private class ZonesDto
    {
        public float marinaEnd, beachStart, hotelM, beachEnd, climbStart, komM, ridgeEnd,
                     coastStart, bridgeStart, finish;
    }
    [Serializable] private class PadDto { public string name; public float[] c; public float[] h; }
    [Serializable] private class RouteDto
    {
        public float roadHalfWidth, shoulderWidth;
        public SampleDto[] samples;
        public ZonesDto zones;
        public PadDto[] pads;
    }

    /// <summary>The published Nagisa centreline in Unity world space (open course).</summary>
    public class NagisaRoute
    {
        public Vector3[] Position = Array.Empty<Vector3>();
        public Vector3[] Tangent = Array.Empty<Vector3>();
        public float[] Distance = Array.Empty<float>();
        public int Count => Position.Length;
        public float Length => Distance.Length == 0 ? 0f : Distance[Distance.Length - 1];
        public float MinY, MaxY;
        public Bounds Plan;
        public float MarinaEndM, BeachStartM, HotelM, BeachEndM, ClimbStartM, KomM, RidgeEndM,
                     CoastStartM, BridgeStartM;
        public readonly Dictionary<string, (Vector3 c, Vector2 h)> Pads =
            new Dictionary<string, (Vector3, Vector2)>();

        public Vector3 SideFlat(int i)
        {
            var t = new Vector3(Tangent[i].x, 0f, Tangent[i].z);
            if (t.sqrMagnitude < 1e-6f) t = Vector3.forward;
            return Vector3.Cross(Vector3.up, t.normalized).normalized;
        }

        public int IndexAt(float metres)
        {
            int lo = 0, hi = Count - 1;
            if (metres <= 0f) return 0;
            if (metres >= Length) return hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Distance[mid] < metres) lo = mid; else hi = mid;
            }
            return hi;
        }

        /// <summary>True where the road runs on the viaduct (no ground under the carriageway).</summary>
        public bool OnBridge(int i) => Distance[i] > BridgeStartM + 40f;

        /// <summary>Plan distance to the nearest centreline sample (coarse, stride 2).</summary>
        public float PlanDistance(float x, float z, out int nearest)
        {
            float best = float.MaxValue; nearest = 0;
            for (int i = 0; i < Count; i += 2)
            {
                float dx = Position[i].x - x, dz = Position[i].z - z;
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; nearest = i; }
            }
            return Mathf.Sqrt(best);
        }

        public static NagisaRoute Load()
        {
            var text = File.ReadAllText(RoutePath);
            var dto = JsonUtility.FromJson<RouteDto>(text);
            int n = dto.samples.Length;
            var r = new NagisaRoute
            {
                Position = new Vector3[n], Tangent = new Vector3[n], Distance = new float[n],
                MinY = float.MaxValue, MaxY = float.MinValue,
            };
            for (int i = 0; i < n; i++)
            {
                var s = dto.samples[i];
                r.Position[i] = new Vector3(s.p[0], s.p[1], s.p[2]);
                r.Tangent[i] = new Vector3(s.t[0], s.t[1], s.t[2]).normalized;
                r.Distance[i] = s.d;
                r.MinY = Mathf.Min(r.MinY, s.p[1]);
                r.MaxY = Mathf.Max(r.MaxY, s.p[1]);
            }
            r.Plan = new Bounds(r.Position[0], Vector3.zero);
            for (int i = 0; i < n; i++) r.Plan.Encapsulate(r.Position[i]);
            var z = dto.zones;
            r.MarinaEndM = z.marinaEnd; r.BeachStartM = z.beachStart; r.HotelM = z.hotelM;
            r.BeachEndM = z.beachEnd; r.ClimbStartM = z.climbStart; r.KomM = z.komM;
            r.RidgeEndM = z.ridgeEnd; r.CoastStartM = z.coastStart; r.BridgeStartM = z.bridgeStart;
            if (dto.pads != null)
                foreach (var p in dto.pads)
                    r.Pads[p.name] = (new Vector3(p.c[0], p.c[1], p.c[2]), new Vector2(p.h[0], p.h[1]));
            return r;
        }
    }

    /// <summary>The published ground height field (NagisaGround.bytes, NBG1).</summary>
    public class NagisaGround
    {
        public int Nx, Nz; public float X0, Z0, Cell;
        public float[] H; public byte[] Cls; public float[] Sd;

        public static NagisaGround Load()
        {
            var b = File.ReadAllBytes(GroundPath);
            if (b.Length < 24 || b[0] != 'N' || b[1] != 'B' || b[2] != 'G' || b[3] != '1')
                throw new InvalidDataException($"{GroundPath} is not NBG1 - rerun build_nagisa_route.py");
            var g = new NagisaGround
            {
                Nx = BitConverter.ToInt32(b, 4), Nz = BitConverter.ToInt32(b, 8),
                X0 = BitConverter.ToSingle(b, 12), Z0 = BitConverter.ToSingle(b, 16),
                Cell = BitConverter.ToSingle(b, 20),
            };
            int n = g.Nx * g.Nz, o = 24;
            g.H = new float[n]; Buffer.BlockCopy(b, o, g.H, 0, n * 4); o += n * 4;
            g.Cls = new byte[n]; Buffer.BlockCopy(b, o, g.Cls, 0, n); o += n;
            g.Sd = new float[n]; Buffer.BlockCopy(b, o, g.Sd, 0, n * 4);
            return g;
        }

        public float Node(int ix, int iz) =>
            H[Mathf.Clamp(iz, 0, Nz - 1) * Nx + Mathf.Clamp(ix, 0, Nx - 1)];

        private float Bilinear(float[] a, float x, float z)
        {
            float fx = (x - X0) / Cell, fz = (z - Z0) / Cell;
            int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, Nx - 2);
            int iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, Nz - 2);
            float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
            float a00 = a[iz * Nx + ix], a10 = a[iz * Nx + ix + 1];
            float a01 = a[(iz + 1) * Nx + ix], a11 = a[(iz + 1) * Nx + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(a00, a10, tx), Mathf.Lerp(a01, a11, tx), tz);
        }

        /// <summary>Ground height at a world point (the value every prop must sit on).</summary>
        public float Height(float x, float z)
        {
            // Match the rendered triangle surface on the fine chunks (same split as the mesh).
            float fx = (x - X0) / Cell, fz = (z - Z0) / Cell;
            int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, Nx - 2);
            int iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, Nz - 2);
            float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
            float a = H[iz * Nx + ix], bx = H[iz * Nx + ix + 1];
            float c = H[(iz + 1) * Nx + ix], d = H[(iz + 1) * Nx + ix + 1];
            // triangles (a, c, d) and (a, d, bx): diagonal a->d
            return tz > tx ? a + (d - c) * tx + (c - a) * tz
                           : a + (bx - a) * tx + (d - bx) * tz;
        }

        /// <summary>Signed distance to the coastline, metres (+ inland).</summary>
        public float Coast(float x, float z) => Bilinear(Sd, x, z);

        public int ClassAt(float x, float z)
        {
            int ix = Mathf.Clamp(Mathf.RoundToInt((x - X0) / Cell), 0, Nx - 1);
            int iz = Mathf.Clamp(Mathf.RoundToInt((z - Z0) / Cell), 0, Nz - 1);
            return Cls[iz * Nx + ix];
        }

        public int ClassNode(int ix, int iz) =>
            Cls[Mathf.Clamp(iz, 0, Nz - 1) * Nx + Mathf.Clamp(ix, 0, Nx - 1)];
    }

    /// <summary>
    /// Shared keep-out used by every scatter / prop placer in the region: dry land, a real ground
    /// height, clear of the road corridor and never on the viaduct.
    /// </summary>
    public static bool CanPlace(float x, float z, float clearanceM, out float y, float minCoastM = 2f)
    {
        y = 0f;
        if (_ground == null || _route == null) return false;
        if (_ground.Coast(x, z) < minCoastM) return false;
        y = _ground.Height(x, z);
        if (y < 0.25f) return false;
        float d = _route.PlanDistance(x, z, out _);
        return d > CorridorKeepOutM + clearanceM;
    }

    // =================================================================== terrain

    /// <summary>Half-width of the ground bench kept below the road: any terrain triangle that can
    /// reach the carriageway + shoulder (4.1 m) has all its nodes within 4.1 m + one cell
    /// diagonal (8.5 m), so every node that close sits under the road; beyond it the ground may
    /// rise again at a 1:1 cut batter. Fixes grass poking through the road on the 9.5 % wall,
    /// where the hill's cross-slope is steep (NagisaBayValidation [surface]). PROVISIONAL.</summary>
    private const float RoadBenchHalfM = 12.6f;
    private const float RoadBenchDropM = 0.12f;

    /// <summary>Lower the in-memory ground field under the road (props, colliders and the rendered
    /// mesh all read the same benched field). Returns the number of nodes lowered.</summary>
    private static int BenchUnderRoad(NagisaGround g, NagisaRoute r)
    {
        int lowered = 0;
        float reach = RoadBenchHalfM + 8f;
        int rc = Mathf.CeilToInt(reach / g.Cell);
        for (int i = 0; i < r.Count; i++)
        {
            if (r.OnBridge(i)) continue;
            var p = r.Position[i];
            int cx = Mathf.RoundToInt((p.x - g.X0) / g.Cell), cz = Mathf.RoundToInt((p.z - g.Z0) / g.Cell);
            for (int iz = cz - rc; iz <= cz + rc; iz++)
            for (int ix = cx - rc; ix <= cx + rc; ix++)
            {
                if (ix < 0 || iz < 0 || ix >= g.Nx || iz >= g.Nz) continue;
                float x = g.X0 + ix * g.Cell, z = g.Z0 + iz * g.Cell;
                float d = new Vector2(x - p.x, z - p.z).magnitude;
                if (d > reach) continue;
                float cap = p.y - RoadBenchDropM + Mathf.Max(0f, d - RoadBenchHalfM);
                int k = iz * g.Nx + ix;
                if (g.H[k] > cap) { g.H[k] = cap; lowered++; }
            }
        }
        lowered += OverlookShapeTerrain(g, r);   // worker E: Skyline Terrace cut (Overlook.cs)
        return lowered;
    }

    // Pass 2: the baked ground has a 10-35 m vertical step between two grid columns near x = 0
    // (the straight N-S line in the aerial), which the classifier then painted as a rock strip,
    // plus pale 'rock' class on gentle inland slopes (the bare scars). Heal both on load:
    // cross-fade the step over SeamBlendCols columns either side, faded out near the route so the
    // road bench / road height / colliders are untouched, then re-class gentle inland rock to forest.
    private const int SeamBlendCols = 45;          // provisional: ~270 m cross-fade each side
    private const float SeamRouteKeepM = 18f;      // no height edits within this of the centreline
    private const float SeamRouteFadeM = 70f;
    private const float ScarMaxSlopeDeg = 30f;     // rock gentler than this, inland, is a scar
    private const float ScarMinCoastM = 25f;

    private static int HealGround(NagisaGround g, NagisaRoute route)
    {
        int nx = g.Nx, nz = g.Nz, healed = 0;
        // 1) locate the seam column pair near x = 0 by the largest mean step.
        int c0 = Mathf.RoundToInt((0f - g.X0) / g.Cell), seam = -1; float bestJump = 0f;
        for (int c = c0 - 6; c <= c0 + 6; c++)
        {
            if (c < 2 || c > nx - 3) continue;
            double sum = 0; int n = 0;
            for (int iz = 0; iz < nz; iz++)
            {
                int k = iz * nx + c;
                if (g.H[k] < 0.5f && g.H[k + 1] < 0.5f) continue;
                float slopeIn = 0.5f * ((g.H[k] - g.H[k - 1]) + (g.H[k + 2] - g.H[k + 1]));
                sum += Mathf.Abs(g.H[k + 1] - g.H[k] - slopeIn); n++;
            }
            float mean = n > 0 ? (float)(sum / n) : 0f;
            if (mean > bestJump) { bestJump = mean; seam = c; }
        }
        var probe = new List<Vector2>();
        for (int i = 0; i < route.Count; i += 4) probe.Add(new Vector2(route.Position[i].x, route.Position[i].z));
        float RouteWeight(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var p in probe)
            {
                float dx = p.x - x, dz = p.y - z;
                if (Mathf.Abs(dx) > SeamRouteFadeM || Mathf.Abs(dz) > SeamRouteFadeM) continue;
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            best = Mathf.Sqrt(best);
            return Mathf.Clamp01((best - SeamRouteKeepM) / (SeamRouteFadeM - SeamRouteKeepM));
        }
        if (seam > 0 && bestJump > 1.5f)
        {
            for (int iz = 0; iz < nz; iz++)
            {
                int k = iz * nx + seam;
                if (g.H[k] < 0.5f && g.H[k + 1] < 0.5f) continue;
                float slopeIn = 0.5f * ((g.H[k] - g.H[k - 1]) + (g.H[k + 2] - g.H[k + 1]));
                float d = g.H[k + 1] - g.H[k] - slopeIn;
                if (Mathf.Abs(d) < 1f) continue;
                float z = g.Z0 + iz * g.Cell;
                for (int j = 0; j < SeamBlendCols; j++)
                {
                    float w = 0.5f * d * (1f - j / (float)SeamBlendCols);
                    int kl = iz * nx + seam - j, kr = iz * nx + seam + 1 + j;
                    if (seam - j >= 0)
                    {
                        float rw = RouteWeight(g.X0 + (seam - j) * g.Cell, z);
                        if (g.H[kl] > 0.3f && rw > 0f) { g.H[kl] += w * rw; healed++; }
                    }
                    if (seam + 1 + j < nx)
                    {
                        float rw = RouteWeight(g.X0 + (seam + 1 + j) * g.Cell, z);
                        if (g.H[kr] > 0.3f && rw > 0f) { g.H[kr] -= w * rw; healed++; }
                    }
                }
            }
            Debug.Log($"[nagisa] seam at column {seam} (x {g.X0 + seam * g.Cell:0}), mean step {bestJump:0.0} m - blended");
        }
        // 2) re-class gentle inland rock (scars) now that the step is gone.
        var cls = (byte[])g.Cls.Clone();
        for (int iz = 1; iz < nz - 1; iz++)
            for (int ix = 1; ix < nx - 1; ix++)
            {
                int k = iz * nx + ix;
                if (cls[k] != 5 || g.H[k] < 0.5f || g.Sd[k] < ScarMinCoastM) continue;
                float gx = (g.H[k + 1] - g.H[k - 1]) / (2f * g.Cell), gz = (g.H[k + nx] - g.H[k - nx]) / (2f * g.Cell);
                float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                if (slope < ScarMaxSlopeDeg) { g.Cls[k] = 3; healed++; }
            }
        return healed;
    }

    private static void BuildTerrain(Transform root)
    {
        var group = new GameObject("Terrain").transform;
        group.SetParent(root, false);
        var g = _ground;
        var mat = GroundMaterial();

        // Route stations thinned for chunk distance tests.
        var probe = new List<Vector2>();
        for (int i = 0; i < _route.Count; i += 10)
            probe.Add(new Vector2(_route.Position[i].x, _route.Position[i].z));

        int chunks = 0; long tris = 0;
        for (int cz = 0; cz < g.Nz - 1; cz += ChunkCells)
        for (int cx = 0; cx < g.Nx - 1; cx += ChunkCells)
        {
            int ex = Mathf.Min(cx + ChunkCells, g.Nx - 1), ez = Mathf.Min(cz + ChunkCells, g.Nz - 1);
            float x0 = g.X0 + cx * g.Cell, z0 = g.Z0 + cz * g.Cell;
            float x1 = g.X0 + ex * g.Cell, z1 = g.Z0 + ez * g.Cell;

            // Any dry-ish node at all?
            float maxH = float.MinValue;
            for (int iz = cz; iz <= ez; iz += 2)
                for (int ix = cx; ix <= ex; ix += 2)
                    maxH = Mathf.Max(maxH, g.Node(ix, iz));
            if (maxH < SkipBelowY) continue;

            float best = float.MaxValue;
            foreach (var p in probe)
            {
                float dx = Mathf.Max(Mathf.Max(x0 - p.x, 0f), p.x - x1);
                float dz = Mathf.Max(Mathf.Max(z0 - p.y, 0f), p.y - z1);
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            best = Mathf.Sqrt(best);
            int step = best < FineRadiusM ? 1 : best < MidRadiusM ? 2 : 4;

            var mesh = TerrainChunk(cx, cz, ex, ez, step, $"Nagisa_Terrain_{cx}_{cz}");
            if (mesh == null) continue;
            var go = AddMesh(group, $"Terrain_Chunk_{cx}_{cz}", mesh, mat, collider: step == 1);
            var mr = go.GetComponent<MeshRenderer>();
            // Terrain receives shadows; only near chunks cast (shadows only within ~150 m anyway).
            mr.shadowCastingMode = step == 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.isStatic = true;
            chunks++; tris += mesh.triangles.LongLength / 3;
        }
        Debug.Log($"[nagisa] terrain: {chunks} chunks, {tris:N0} tris");
    }

    private static Mesh TerrainChunk(int cx, int cz, int ex, int ez, int step, string name)
    {
        var g = _ground;
        var xs = new List<int>(); for (int ix = cx; ix < ex; ix += step) xs.Add(ix); xs.Add(ex);
        var zs = new List<int>(); for (int iz = cz; iz < ez; iz += step) zs.Add(iz); zs.Add(ez);
        int nx = xs.Count, nz = zs.Count;

        var verts = new List<Vector3>(nx * nz + 4 * (nx + nz));
        var normals = new List<Vector3>(verts.Capacity);
        var uv0 = new List<Vector2>(verts.Capacity);
        var uv1 = new List<Vector2>(verts.Capacity);
        var uv2 = new List<Vector2>(verts.Capacity);

        Vector3 NormalAt(int ix, int iz)
        {
            float hl = g.Node(ix - step, iz), hr = g.Node(ix + step, iz);
            float hd = g.Node(ix, iz - step), hu = g.Node(ix, iz + step);
            return new Vector3(hl - hr, 2f * step * g.Cell, hd - hu).normalized;
        }

        void Splat(int ix, int iz, out Vector2 a, out Vector2 b)
        {
            float rock = 0, sand = 0, forest = 0, n = 0;
            int r = Mathf.Max(1, step);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int c = g.ClassNode(ix + dx, iz + dz);
                    if (c == 5) rock++; else if (c == 1 || c == 0) sand++; else if (c == 3) forest++;
                    n++;
                }
            a = new Vector2(rock / n, sand / n);
            b = new Vector2(forest / n, 0f);
        }

        int Add(int ix, int iz, float drop)
        {
            float x = g.X0 + ix * g.Cell, z = g.Z0 + iz * g.Cell;
            verts.Add(new Vector3(x, g.Node(ix, iz) - drop, z));
            normals.Add(NormalAt(ix, iz));
            uv0.Add(new Vector2(x / 8f, z / 8f));
            Splat(ix, iz, out var a, out var b);
            uv1.Add(a); uv2.Add(b);
            return verts.Count - 1;
        }

        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
                Add(xs[i], zs[j], 0f);

        var tris = new List<int>();
        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                float ha = verts[a].y, hb = verts[b].y, hc = verts[c].y, hd = verts[d].y;
                if (ha < SkipBelowY && hb < SkipBelowY && hc < SkipBelowY && hd < SkipBelowY) continue;
                // Same diagonal as NagisaGround.Height (a->d); CCW seen from +Y.
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(b);
            }
        if (tris.Count == 0) return null;

        // Skirts on all four borders hide T-junction cracks between chunks of different step.
        float skirt = 1.5f + 2.5f * step;
        void Skirt(IList<int> edge, bool flip)
        {
            int first = -1;
            for (int k = 0; k < edge.Count; k++)
            {
                var v = verts[edge[k]];
                verts.Add(v + Vector3.down * skirt); normals.Add(normals[edge[k]]);
                uv0.Add(uv0[edge[k]]); uv1.Add(uv1[edge[k]]); uv2.Add(uv2[edge[k]]);
                if (k == 0) first = verts.Count - 1;
            }
            for (int k = 0; k < edge.Count - 1; k++)
            {
                int t0 = edge[k], t1 = edge[k + 1], b0 = first + k, b1 = first + k + 1;
                if (!flip) { tris.Add(t0); tris.Add(b0); tris.Add(t1); tris.Add(t1); tris.Add(b0); tris.Add(b1); }
                else { tris.Add(t0); tris.Add(t1); tris.Add(b0); tris.Add(t1); tris.Add(b1); tris.Add(b0); }
            }
        }
        var south = new List<int>(); var north = new List<int>();
        var west = new List<int>(); var east = new List<int>();
        for (int i = 0; i < nx; i++) { south.Add(i); north.Add((nz - 1) * nx + i); }
        for (int j = 0; j < nz; j++) { west.Add(j * nx); east.Add(j * nx + nx - 1); }
        Skirt(south, true); Skirt(north, false); Skirt(west, false); Skirt(east, true);

        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetNormals(normals);
        mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    // =================================================================== sea

    private static void BuildSea(Transform root)
    {
        var group = new GameObject("Sea").transform;
        group.SetParent(root, false);

        var ocean = OceanMaterial();
        var c = _route.Plan.center;
        float half = 20000f;   // plate edge must never show in the aerial
        var plate = Grid(c.x - half, c.z - half, half * 2f, 160, SeaLevelY, "Nagisa_OceanPlate");
        var og = AddMesh(group, "Ocean Surface", plate, ocean, collider: false);
        og.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        og.isStatic = true;

        // Turquoise shelf over the real sea floor, depth baked into vertex colour.
        var g = _ground;
        var mat = ShallowsMaterial();
        const int step = 2, tile = 96;
        int tiles = 0;
        for (int cz = 0; cz < g.Nz - 1; cz += tile)
        for (int cx = 0; cx < g.Nx - 1; cx += tile)
        {
            int ex = Mathf.Min(cx + tile, g.Nx - 1), ez = Mathf.Min(cz + tile, g.Nz - 1);
            var verts = new List<Vector3>(); var cols = new List<Color>(); var uvs = new List<Vector2>();
            var tris = new List<int>();
            var index = new Dictionary<int, int>();
            int V(int ix, int iz)
            {
                int key = iz * g.Nx + ix;
                if (index.TryGetValue(key, out int v)) return v;
                float x = g.X0 + ix * g.Cell, z = g.Z0 + iz * g.Cell;
                float sd = g.Sd[key], h = g.H[key];
                float depth = Mathf.Clamp(-h, 0f, ShallowsDepthScale);
                float present = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShallowsReachM * 0.7f,
                                                                                 ShallowsReachM, -sd));
                verts.Add(new Vector3(x, SeaLevelY + 0.03f, z));
                cols.Add(new Color(depth / ShallowsDepthScale, 0f, 0f, present));
                uvs.Add(new Vector2(x / 20f, z / 20f));
                index[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            for (int iz = cz; iz < ez; iz += step)
                for (int ix = cx; ix < ex; ix += step)
                {
                    int jx = Mathf.Min(ix + step, g.Nx - 1), jz = Mathf.Min(iz + step, g.Nz - 1);
                    float s0 = g.Sd[iz * g.Nx + ix], s1 = g.Sd[iz * g.Nx + jx];
                    float s2 = g.Sd[jz * g.Nx + ix], s3 = g.Sd[jz * g.Nx + jx];
                    float mn = Mathf.Min(Mathf.Min(s0, s1), Mathf.Min(s2, s3));
                    float mx = Mathf.Max(Mathf.Max(s0, s1), Mathf.Max(s2, s3));
                    if (mx < -ShallowsReachM || mn > 30f) continue;
                    int a = V(ix, iz), b = V(jx, iz), cc = V(ix, jz), d = V(jx, jz);
                    tris.Add(a); tris.Add(cc); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b);
                }
            if (tris.Count == 0) continue;
            var mesh = new Mesh { name = $"Nagisa_Shallows_{cx}_{cz}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            var n = new Vector3[verts.Count]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            mesh.normals = n;
            mesh.RecalculateBounds();
            var go = AddMesh(group, $"Shallows_{cx}_{cz}", mesh, mat, collider: false);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
            tiles++;
        }
        Debug.Log($"[nagisa] sea: ocean plate {half * 2f:N0} m, {tiles} shallows tiles");
    }

    private static Mesh Grid(float ox, float oz, float size, int n, float y, string name)
    {
        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[verts.Length];
        float stepM = size / n;
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                verts[i * (n + 1) + j] = new Vector3(ox + i * stepM, y, oz + j * stepM);
                uvs[i * (n + 1) + j] = new Vector2(i * stepM / 40f, j * stepM / 40f);
            }
        var tris = new List<int>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                int a = i * (n + 1) + j, b = a + 1, cc = a + (n + 1), d = cc + 1;
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(cc);
            }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.uv = uvs; mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // =================================================================== road

    private static float RoadY(int i, float offset) =>
        _route.Position[i].y + RoadSurfaceLiftM + CrownAt(offset);

    private static void BuildRoad(Transform root)
    {
        var group = new GameObject("Road").transform;
        group.SetParent(root, false);
        var r = _route;

        // Cross-section: skirt | shoulder+carriageway (crowned) | skirt. The skirt only exists on
        // land; on the viaduct the parapet takes over.
        float half = RoadHalfWidth + ShoulderWidth;
        float[] offs = { -half - 1.1f, -half, -2.6f, -1.3f, 0f, 1.3f, 2.6f, half, half + 1.1f };
        const float UvPerMetre = 0.22f;
        const int SpansPerTile = 400;
        int tiles = 0;
        for (int t0 = 0; t0 < r.Count - 1; t0 += SpansPerTile)
        {
            int t1 = Mathf.Min(t0 + SpansPerTile, r.Count - 1);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int cols = offs.Length;
            for (int i = t0; i <= t1; i++)
            {
                var p = r.Position[i]; var s = r.SideFlat(i);
                bool bridge = r.OnBridge(i);
                for (int c = 0; c < cols; c++)
                {
                    float o = offs[c];
                    float y = Mathf.Abs(o) > half + 0.01f
                        ? (bridge ? RoadY(i, half) - 0.04f : RoadY(i, half) - 0.55f)
                        : RoadY(i, o);
                    verts.Add(new Vector3(p.x + s.x * o, y, p.z + s.z * o));
                    uvs.Add(new Vector2((o + half + 1.1f) * UvPerMetre, r.Distance[i] * UvPerMetre));
                }
            }
            for (int i = 0; i < t1 - t0; i++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = i * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    tris.Add(a); tris.Add(d); tris.Add(b);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                }
            var mesh = Finish($"Nagisa_Road_{t0}", verts, uvs, tris);
            var go = AddMesh(group, $"Road_{t0}", mesh, RoadMaterial(half + 1.1f, UvPerMetre), collider: true);
            go.isStatic = true;
            tiles++;
        }
        BuildMarkings(group);
        Debug.Log($"[nagisa] road: {tiles} tiles, {r.Length / 1000f:0.00} km");
    }

    private static void BuildMarkings(Transform group)
    {
        var r = _route;
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();

        void Strip(int i, float o, float w)
        {
            var p0 = r.Position[i]; var s0 = r.SideFlat(i);
            var p1 = r.Position[i + 1]; var s1 = r.SideFlat(i + 1);
            int b = verts.Count;
            verts.Add(new Vector3(p0.x + s0.x * (o - w), RoadY(i, o) + MarkingLiftM, p0.z + s0.z * (o - w)));
            verts.Add(new Vector3(p0.x + s0.x * (o + w), RoadY(i, o) + MarkingLiftM, p0.z + s0.z * (o + w)));
            verts.Add(new Vector3(p1.x + s1.x * (o - w), RoadY(i + 1, o) + MarkingLiftM, p1.z + s1.z * (o - w)));
            verts.Add(new Vector3(p1.x + s1.x * (o + w), RoadY(i + 1, o) + MarkingLiftM, p1.z + s1.z * (o + w)));
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.up); uvs.Add(Vector2.one);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
        }

        const float DashM = 5f, GapM = 7f;
        for (int i = 0; i < r.Count - 1; i++)
        {
            // Solid white edge lines; dashed white centre line (Japanese rural road convention).
            Strip(i, -RoadHalfWidth + 0.18f, 0.075f);
            Strip(i, RoadHalfWidth - 0.18f, 0.075f);
            if (Mathf.Repeat(r.Distance[i], DashM + GapM) < DashM) Strip(i, 0f, 0.07f);
        }
        var mesh = Finish("Nagisa_Markings", verts, uvs, tris);
        var go = AddMesh(group, "Road Markings", mesh,
                         Cel("Nagisa_LinePaint", new Color(0.93f, 0.93f, 0.90f), gloss: 0.1f, spec: 0.05f,
                             rim: 0.05f),
                         collider: false);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // =================================================================== bridge approach

    private static void BuildBridgeApproach(Transform root)
    {
        var group = new GameObject("Bridge Approach").transform;
        group.SetParent(root, false);
        var r = _route;
        int start = r.IndexAt(r.BridgeStartM + 20f);
        if (start >= r.Count - 2) return;

        var concrete = Cel("Nagisa_BridgeConcrete", new Color(0.80f, 0.80f, 0.78f), gloss: 0.12f,
                           spec: 0.08f, rim: 0.12f, texture: Tex(NagisaTex, "NB_Plaster_Albedo.png"),
                           normal: Tex(NagisaTex, "NB_Plaster_Normal.png"), normalStrength: 0.5f);
        concrete.SetFloat("_DetailAmount", 0.12f);
        var rail = Cel("Nagisa_BridgeRail", new Color(0.30f, 0.56f, 0.62f), gloss: 0.5f, spec: 0.4f, rim: 0.3f);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        var rv = new List<Vector3>(); var ruv = new List<Vector2>(); var rtri = new List<int>();

        // Deck girder: the carriageway itself is the road mesh; this adds the edge beam + soffit
        // + a 0.9 m parapet wall with a teal handrail on top.
        for (int i = start; i < r.Count - 1; i++)
        {
            var p0 = r.Position[i]; var s0 = r.SideFlat(i);
            var p1 = r.Position[i + 1]; var s1 = r.SideFlat(i + 1);
            float y0 = RoadY(i, BridgeDeckHalfW), y1 = RoadY(i + 1, BridgeDeckHalfW);
            foreach (float side in new[] { -1f, 1f })
            {
                float oIn = side * (RoadHalfWidth + ShoulderWidth), oOut = side * BridgeDeckHalfW;
                // raised walkway (15 cm kerb) between carriageway edge and parapet
                QuadWall(v, uv, tri, p0, s0, p1, s1, oIn, y0 - 0.03f, y1 - 0.03f, 0.18f, side < 0);
                QuadStrip(v, uv, tri, p0, s0, p1, s1, oIn, oOut, y0 + 0.15f, y1 + 0.15f, y0 + 0.15f, y1 + 0.15f, side < 0);
                // parapet inner face (faces the road), top, outer face (faces the sea)
                QuadWall(v, uv, tri, p0, s0, p1, s1, oOut - side * 0.25f, y0 + 0.15f, y1 + 0.15f, 0.8f, side < 0);
                QuadStrip(v, uv, tri, p0, s0, p1, s1, oOut - side * 0.25f, oOut, y0 + 0.95f, y1 + 0.95f,
                          y0 + 0.95f, y1 + 0.95f, side < 0);
                QuadWall(v, uv, tri, p0, s0, p1, s1, oOut, y0 - 1.6f, y1 - 1.6f, 2.55f, side > 0);
                // handrail
                QuadWall(rv, ruv, rtri, p0, s0, p1, s1, oOut - side * 0.12f, y0 + 0.95f, y1 + 0.95f, 0.18f, side > 0);
                QuadWall(rv, ruv, rtri, p0, s0, p1, s1, oOut - side * 0.12f, y0 + 0.95f, y1 + 0.95f, 0.18f, side < 0);
            }
            // soffit
            QuadStrip(v, uv, tri, p0, s0, p1, s1, -BridgeDeckHalfW, BridgeDeckHalfW, y0 - 1.6f, y1 - 1.6f,
                      y0 - 1.6f, y1 - 1.6f, true);
        }

        // Piers down to the sea floor / ground.
        float next = r.Distance[start] + 12f;
        for (int i = start; i < r.Count; i++)
        {
            if (r.Distance[i] < next) continue;
            next += PierSpacingM;
            var p = r.Position[i]; var s = r.SideFlat(i);
            var t = new Vector3(r.Tangent[i].x, 0f, r.Tangent[i].z).normalized;
            float top = RoadY(i, 0f) - 1.6f;
            float bottom = Mathf.Min(_ground.Height(p.x, p.z), SeaLevelY) - 2f;
            if (top - bottom < 1.5f) continue;
            Box(v, uv, tri, new Vector3(p.x, (top + bottom) * 0.5f, p.z), s, t, 3.4f, top - bottom, 1.8f);
            // pier cap
            Box(v, uv, tri, new Vector3(p.x, top - 0.45f, p.z), s, t, BridgeDeckHalfW * 2f - 0.6f, 0.9f, 2.2f);
        }

        var deck = AddMesh(group, "Viaduct Structure", Finish("Nagisa_Viaduct", v, uv, tri), concrete, collider: false);
        deck.isStatic = true;
        var rails = AddMesh(group, "Viaduct Handrail", Finish("Nagisa_ViaductRail", rv, ruv, rtri), rail, collider: false);
        rails.isStatic = true;
        Debug.Log($"[nagisa] bridge approach from {r.Distance[start]:0} m: {tri.Count / 3:N0} tris");
    }

    private static void QuadStrip(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3 p0, Vector3 s0,
                                  Vector3 p1, Vector3 s1, float oA, float oB, float yA0, float yA1,
                                  float yB0, float yB1, bool flip)
    {
        int b = v.Count;
        v.Add(new Vector3(p0.x + s0.x * oA, yA0, p0.z + s0.z * oA));
        v.Add(new Vector3(p0.x + s0.x * oB, yB0, p0.z + s0.z * oB));
        v.Add(new Vector3(p1.x + s1.x * oA, yA1, p1.z + s1.z * oA));
        v.Add(new Vector3(p1.x + s1.x * oB, yB1, p1.z + s1.z * oB));
        float len = Vector3.Distance(p0, p1);
        uv.Add(new Vector2(oA / 4f, 0f)); uv.Add(new Vector2(oB / 4f, 0f));
        uv.Add(new Vector2(oA / 4f, len / 4f)); uv.Add(new Vector2(oB / 4f, len / 4f));
        // Faces UP when (oB > oA) != flip, else faces down.
        bool up = (oB > oA) != flip;
        if (up) { tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3); }
        else { tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b + 3); tri.Add(b + 2); }
    }

    private static void QuadWall(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3 p0, Vector3 s0,
                                 Vector3 p1, Vector3 s1, float o, float y0, float y1, float h, bool facePlusSide)
    {
        int b = v.Count;
        v.Add(new Vector3(p0.x + s0.x * o, y0, p0.z + s0.z * o));
        v.Add(new Vector3(p0.x + s0.x * o, y0 + h, p0.z + s0.z * o));
        v.Add(new Vector3(p1.x + s1.x * o, y1, p1.z + s1.z * o));
        v.Add(new Vector3(p1.x + s1.x * o, y1 + h, p1.z + s1.z * o));
        float len = Vector3.Distance(p0, p1);
        uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, h / 4f));
        uv.Add(new Vector2(len / 4f, 0f)); uv.Add(new Vector2(len / 4f, h / 4f));
        if (facePlusSide) { tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b + 3); }
        else { tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3); tri.Add(b + 1); }
    }

    /// <summary>Oriented box (side = local X, up = Y, fwd = local Z), outward-facing.</summary>
    public static void Box(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3 c, Vector3 side,
                           Vector3 fwd, float w, float h, float d)
    {
        var up = Vector3.up;
        Vector3 hx = side * (w * 0.5f), hy = up * (h * 0.5f), hz = fwd * (d * 0.5f);
        void Face(Vector3 n, Vector3 a, Vector3 b2, float sa, float sb)
        {
            int b = v.Count;
            var o = c + n;
            v.Add(o - a - b2); v.Add(o + a - b2); v.Add(o + a + b2); v.Add(o - a + b2);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(sa / 4f, 0)); uv.Add(new Vector2(sa / 4f, sb / 4f));
            uv.Add(new Vector2(0, sb / 4f));
            // orient CCW seen from outside (normal n)
            var nrm = Vector3.Cross(a, b2);
            if (Vector3.Dot(nrm, n) > 0f) { tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b); tri.Add(b + 2); tri.Add(b + 3); }
            else { tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b); tri.Add(b + 3); tri.Add(b + 2); }
        }
        Face(hx, hz, hy, d, h); Face(-hx, hz, hy, d, h);
        Face(hy, hx, hz, w, d); Face(-hy, hx, hz, w, d);
        Face(hz, hx, hy, w, h); Face(-hz, hx, hy, w, h);
    }

    // =================================================================== atmosphere

    /// <summary>Everything the Nagisa sky volume sets. Callable on the saved profile alone (PhotoSky.cs).</summary>
    public static void FillAtmosphereProfile(VolumeProfile profile)
    {
        profile.components.RemoveAll(c => c == null);   // stale `fileID: 0` entries from the old unsaved build
        var ve = Ensure<HD.VisualEnvironment>(profile);
        ve.skyType.overrideState = true; ve.skyType.value = (int)HD.SkyType.Gradient;
        ve.cloudType.overrideState = true; ve.cloudType.value = (int)HD.CloudType.CloudLayer;

        var clouds = Ensure<HD.CloudLayer>(profile);
        var cloudMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoastTex}/Shiosai_CloudMap.png");
        if (cloudMap != null)
        {
            clouds.layerA.cloudMap.overrideState = true; clouds.layerA.cloudMap.value = cloudMap;
            clouds.layerB.cloudMap.overrideState = true; clouds.layerB.cloudMap.value = cloudMap;
        }
        clouds.opacity.overrideState = true; clouds.opacity.value = 0.72f;
        clouds.upperHemisphereOnly.overrideState = true; clouds.upperHemisphereOnly.value = true;
        clouds.layers.overrideState = true; clouds.layers.value = HD.CloudMapMode.Double;
        clouds.layerA.altitude.overrideState = true; clouds.layerA.altitude.value = 1800f;
        clouds.layerA.tint.overrideState = true; clouds.layerA.tint.value = new Color(1f, 0.99f, 0.97f);
        clouds.layerA.opacityR.overrideState = true; clouds.layerA.opacityR.value = 1f;
        clouds.layerA.lighting.overrideState = true; clouds.layerA.lighting.value = true;
        clouds.layerA.thickness.overrideState = true; clouds.layerA.thickness.value = 0.55f;
        clouds.layerB.rotation.overrideState = true; clouds.layerB.rotation.value = 0.41f;
        clouds.layerB.altitude.overrideState = true; clouds.layerB.altitude.value = 3600f;
        clouds.layerB.tint.overrideState = true; clouds.layerB.tint.value = new Color(0.97f, 0.98f, 1f);
        clouds.layerB.opacityR.overrideState = true; clouds.layerB.opacityR.value = 0.7f;
        clouds.layerB.lighting.overrideState = true; clouds.layerB.lighting.value = true;

        // FIX (copilot, fix-implementer pass): top/middle/bottom were gained x1.45 into HDR with
        // an extremely saturated royal-blue top (0x1E6ED8). HDRP derives the realtime ambient GI
        // probe from this GradientSky (NOT from RenderSettings.ambientSkyColor, which this custom
        // lighting rig only uses as a documentation/fallback value), and the custom MR_Ambient()
        // "sky" lobe is what every near-horizontal upward normal samples - i.e. the road and
        // ground. That saturated, over-gained top colour is exactly what was washing the whole
        // road/terrain into a flat pale blue (CelLit buildings/trees stayed readable because
        // their own saturated local albedo dominates the same ambient add). Gain removed, top
        // desaturated/lightened to a believable tropical sky blue.
        var sky = Ensure<HD.GradientSky>(profile);
        sky.top.overrideState = true; sky.top.value = Srgb(0x3E, 0x86, 0xD2).linear;
        sky.middle.overrideState = true; sky.middle.value = Srgb(0x8C, 0xC4, 0xF0).linear;
        sky.bottom.overrideState = true; sky.bottom.value = Srgb(0xE6, 0xF2, 0xF6).linear;
        sky.gradientDiffusion.overrideState = true; sky.gradientDiffusion.value = 2.4f;

        ApplyPhotoSky(profile);   // photoreal clouds + haze (NagisaBayEnvironment.PhotoSky.cs) override the block above

        var fog = Ensure<HD.Fog>(profile);
        fog.enabled.overrideState = true; fog.enabled.value = true;
        fog.meanFreePath.overrideState = true; fog.meanFreePath.value = 5200f;
        fog.baseHeight.overrideState = true; fog.baseHeight.value = 0f;
        fog.maximumHeight.overrideState = true; fog.maximumHeight.value = 420f;
        fog.albedo.overrideState = true; fog.albedo.value = Srgb(0xDC, 0xEA, 0xF4);
        fog.mipFogNear.overrideState = true; fog.mipFogNear.value = 0f;
        fog.mipFogFar.overrideState = true; fog.mipFogFar.value = 5000f;
        fog.mipFogMaxMip.overrideState = true; fog.mipFogMaxMip.value = 0.5f;
        fog.enableVolumetricFog.overrideState = true; fog.enableVolumetricFog.value = false;

        var tm = Ensure<HD.Tonemapping>(profile);
        tm.mode.overrideState = true; tm.mode.value = HD.TonemappingMode.ACES;

        var ex = Ensure<HD.Exposure>(profile);
        ex.mode.overrideState = true; ex.mode.value = HD.ExposureMode.Fixed;
        ex.fixedExposure.overrideState = true; ex.fixedExposure.value = NagisaFixedEv;

        var bloom = Ensure<HD.Bloom>(profile);
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.18f;
        bloom.scatter.overrideState = true; bloom.scatter.value = 0.70f;

        var ca = Ensure<HD.ColorAdjustments>(profile);
        ca.saturation.overrideState = true; ca.saturation.value = 14f;
        ca.contrast.overrideState = true; ca.contrast.value = 8f;
        ca.postExposure.overrideState = true; ca.postExposure.value = 0f;

        var wb = Ensure<HD.WhiteBalance>(profile);
        wb.temperature.overrideState = true; wb.temperature.value = 2f;
        wb.tint.overrideState = true; wb.tint.value = 0f;

        EditorUtility.SetDirty(profile);
    }

    private static void ConfigureAtmosphere(Transform parent)
    {
        string profilePath = $"{Dir}/Nagisa_SkyProfile.asset";
        const string volumeName = "Nagisa Sky Volume";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        FillAtmosphereProfile(profile);
        EditorUtility.SetDirty(profile);

        var go = new GameObject(volumeName);
        go.transform.SetParent(parent, false);
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 50f;
        vol.sharedProfile = profile;
        Debug.Log($"[nagisa] atmosphere: tropical afternoon GradientSky + CloudLayer, fog mfp 5200, EV {NagisaFixedEv}");
    }

    /// <summary>HDRP fixed exposure (lower = brighter). PROVISIONAL; tuned by render.</summary>
    private const float NagisaFixedEv = 0.55f;

    private static T Ensure<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet<T>(out var c) || c == null) c = p.Add<T>(true);
        // VolumeProfile.Add only creates the component in MEMORY; without AddObjectToAsset the saved
        // profile holds NULL component references (Nagisa_SkyProfile.asset did: three `fileID: 0`),
        // so the intended Nagisa sky/clouds/fog/exposure never loaded and HDRP used its default sky.
        // Same fix as Shiosai/Minato. (2026-10-02, Claude)
        if (!AssetDatabase.Contains(c) && AssetDatabase.Contains(p))
        {
            c.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
        }
        c.active = true;
        return c;
    }

    // =================================================================== materials

    private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

    public static Material LoadOrCreate(string name, string shaderName)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null && cached.shader != null &&
            cached.shader.name == shaderName) return cached;
        Directory.CreateDirectory(MaterialDir);
        string path = $"{MaterialDir}/{name}.mat";
        var shader = Shader.Find(shaderName);
        if (shader == null) Debug.LogError($"[nagisa] shader missing: {shaderName}");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (shader != null) mat.shader = shader;
        mat.name = name;
        mat.enableInstancing = true;
        MaterialCache[name] = mat;
        return mat;
    }

    private static readonly Color NeutralShade = new Color(0.40f, 0.52f, 0.66f, 1f);

    public static Material Cel(string name, Color albedo, float gloss = 0.2f, float spec = 0.15f,
                               float rim = 0.2f, Texture texture = null, Texture normal = null,
                               float normalStrength = 0.8f, float cull = 2f)
    {
        var mat = LoadOrCreate(name, CelShader);
        mat.SetColor("_Color", albedo);
        mat.SetColor("_ShadeColor", NeutralShade);
        mat.SetFloat("_ShadeStrength", 0.52f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.10f);
        mat.SetColor("_RimColor", new Color(1f, 0.95f, 0.86f, 1f));
        mat.SetFloat("_RimStrength", rim);
        mat.SetFloat("_Gloss", gloss);
        mat.SetFloat("_SpecStrength", spec);
        mat.SetFloat("_ShadowAmbient", 0.55f);
        mat.SetFloat("_ShadowSoft", 0.16f);
        mat.SetFloat("_Cull", cull);
        if (texture != null) mat.SetTexture("_MainTex", texture);
        if (normal != null)
        {
            mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_NormalStrength", normalStrength);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material GroundMaterial()
    {
        var mat = LoadOrCreate("Nagisa_Ground", TerrainShader);
        // grass = remainder: tropical lawn / hill meadow
        mat.SetTexture("_GrassTex", Tex(CoastTex, "Shiosai_Grass_Albedo.png"));
        mat.SetTexture("_GrassNormal", Tex(CoastTex, "Shiosai_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", Tex(CoastTex, "Shiosai_Grass_Rough.png"));
        mat.SetColor("_GrassColor", new Color(0.25f, 0.37f, 0.17f, 1f));   // matches the map mean (x tex) - not lime
        mat.SetFloat("_GrassScale", 6.0f);
        // scree slot (uv1.y) = coral-white beach sand
        mat.SetTexture("_ScreeTex", Tex(CoastTex, "Shiosai_Sand_Albedo.png"));
        mat.SetTexture("_ScreeNormal", Tex(CoastTex, "Shiosai_Sand_Normal.png"));
        mat.SetTexture("_ScreeRough", Tex(CoastTex, "Shiosai_Sand_Rough.png"));
        mat.SetColor("_ScreeColor", new Color(0.66f, 0.62f, 0.54f, 1f));   // warm coral sand (map is already pale)
        mat.SetFloat("_ScreeScale", 3.2f);
        // rock slot (uv1.x) = warm limestone / coral rock
        mat.SetTexture("_RockTex", Tex(CoastTex, "Shiosai_Rock_Albedo.png"));
        mat.SetTexture("_RockNormal", Tex(CoastTex, "Shiosai_Rock_Normal.png"));
        mat.SetTexture("_RockRough", Tex(CoastTex, "Shiosai_Rock_Rough.png"));
        mat.SetColor("_RockColor", new Color(0.66f, 0.62f, 0.54f, 1f));
        mat.SetFloat("_RockScale", 7.0f);
        // soil slot (uv2.x) = shaded rainforest floor under the canopy
        mat.SetTexture("_SoilTex", Tex(SakuraTex, "Sakura_Grass_Albedo.png"));
        mat.SetTexture("_SoilNormal", Tex(SakuraTex, "Sakura_Grass_Normal.png"));
        mat.SetTexture("_SoilRough", Tex(SakuraTex, "Sakura_Grass_Rough.png"));
        mat.SetColor("_SoilColor", new Color(0.30f, 0.42f, 0.22f, 1f));
        mat.SetFloat("_SoilScale", 4.0f);
        mat.SetFloat("_PetalStrength", 0f);
        mat.SetFloat("_MossStrength", 0.12f);
        mat.SetColor("_MossColor", new Color(0.36f, 0.52f, 0.26f, 1f));
        mat.SetFloat("_SlopeRockStart", 30f);
        mat.SetFloat("_SlopeRockEnd", 50f);
        mat.SetFloat("_MacroVariation", 0.40f);
        mat.SetFloat("_MesoVariation", 0.25f);
        mat.SetFloat("_DetileAmount", 0.95f);
        mat.SetFloat("_NormalStrength", 0.9f);
        mat.SetColor("_ShadeColor", NeutralShade);
        mat.SetFloat("_ShadeStrength", 0.42f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.14f);
        mat.SetFloat("_ShadowAmbient", 0.62f);
        mat.SetColor("_RimColor", new Color(0.95f, 0.97f, 1f, 1f));
        mat.SetFloat("_RimStrength", 0.04f);
        mat.SetFloat("_SpecStrength", 0.08f);
        mat.SetVector("_HeightRange", new Vector4(4000f, 4600f, 0f, 0f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material RoadMaterial(float uvCentreM, float uvPerMetre)
    {
        var m = LoadOrCreate("Nagisa_Asphalt", RoadShader);
        m.SetTexture("_MainTex", Tex(CoastTex, "Shiosai_Asphalt_Albedo.png"));
        m.SetTexture("_DetailNormal", Tex(CoastTex, "Shiosai_Asphalt_Normal.png"));
        m.SetColor("_Color", new Color(0.80f, 0.79f, 0.77f, 1f));
        m.SetColor("_ShadeColor", new Color(0.62f, 0.66f, 0.74f, 1f));
        m.SetFloat("_ShadeStrength", 0.55f);
        m.SetFloat("_CelAmount", 0.2f);
        m.SetFloat("_UvPerMetre", uvPerMetre);
        m.SetFloat("_UvCentreM", uvCentreM);
        m.SetFloat("_RoadWidthM", RoadHalfWidth * 2f);
        m.SetFloat("_ShoulderWidthM", ShoulderWidth);
        m.SetColor("_ShoulderColor", new Color(0.74f, 0.70f, 0.62f, 1f));
        m.SetFloat("_ShoulderAmount", 0.35f);
        m.SetFloat("_VergeWidthM", 1.1f);
        m.SetColor("_VergeGravelColor", new Color(0.80f, 0.76f, 0.66f, 1f));
        m.SetColor("_VergeSoilColor", new Color(0.44f, 0.50f, 0.30f, 1f));
        m.SetFloat("_VergeAmount", 0.8f);
        m.SetFloat("_Gloss", 0.22f);
        m.SetFloat("_SpecStrength", 0.18f);
        m.SetFloat("_RimStrength", 0.08f);
        m.SetFloat("_AmbientStrength", 0.9f);
        m.SetFloat("_ShadowAmbient", 0.5f);
        m.SetFloat("_Cull", 2f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material OceanMaterial()
    {
        var mat = LoadOrCreate("Nagisa_Ocean", OceanShader);
        SetIf(mat, "_ShallowColor", Srgb(0x1A, 0x9C, 0xB4));
        SetIf(mat, "_MidColor", Srgb(0x13, 0x6A, 0xA0));
        SetIf(mat, "_DeepColor", Srgb(0x0C, 0x3E, 0x78));
        SetIf(mat, "_SkyTint", Srgb(0x9C, 0xC6, 0xEA));
        SetIf(mat, "_Color", Srgb(0x1C, 0x84, 0xB0));
        SetIf(mat, "_SunColor", Srgb(0xFF, 0xF2, 0xD8));
        SetF(mat, "_DepthBlend", 1f);
        SetF(mat, "_ShoreFadeStart", 120f);
        SetF(mat, "_ShoreFadeEnd", 3200f);
        SetF(mat, "_FresnelBoost", 0.34f);
        SetF(mat, "_FresnelPower", 4.4f);
        SetF(mat, "_GlitterBoost", 0.6f);
        SetF(mat, "_GlitterPower", 110f);
        SetF(mat, "_GlitterFalloff", 1f);
        SetF(mat, "_GlitterGrazeEnd", 0.5f);
        SetIf(mat, "_FoamColor", Srgb(0xF6, 0xFC, 0xFC));
        SetF(mat, "_FoamAmount", 0.14f);
        SetF(mat, "_WaveScale", 0.12f);
        SetF(mat, "_WaveStrength", 0.45f);
        SetF(mat, "_DetailFadeStart", 220f);
        SetF(mat, "_DetailFadeEnd", 3000f);
        SetF(mat, "_AmbientWeight", 0.9f);
        SetF(mat, "_LightGain", 1.08f);
        SetF(mat, "_ChopWeight", 1f);
        SetF(mat, "_ChopScale", 6.0f);
        SetF(mat, "_ChopStrength", 0.32f);
        SetF(mat, "_MacroScale", 0.0005f);
        SetF(mat, "_MacroStrength", 0.45f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material ShallowsMaterial()
    {
        var m = LoadOrCreate("Nagisa_Shallows", ShallowsShader);
        m.SetFloat("_DepthDriven", 1f);
        m.SetFloat("_DepthScale", ShallowsDepthScale);
        m.SetColor("_SandColor", Srgb(0x86, 0xBA, 0xAE));
        m.SetColor("_ShoreColor", Srgb(0x2E, 0x98, 0xA0));
        m.SetColor("_ShelfColor", Srgb(0x14, 0x74, 0x98));
        m.SetColor("_FoamColor", Color.white);
        m.SetFloat("_TurquoiseDepth", 1.8f);
        m.SetFloat("_ShelfDepth", 7.5f);
        m.SetFloat("_FadeDepth", 14f);
        m.SetFloat("_SurfDepth", 0.9f);
        m.SetFloat("_SurfAmount", 0.7f);
        m.SetFloat("_Opacity", 0.80f);
        m.SetFloat("_WaveStrength", 0.2f);
        m.SetFloat("_GlitterBoost", 0.7f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static void SetIf(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
    private static void SetF(Material m, string p, float f) { if (m.HasProperty(p)) m.SetFloat(p, f); }

    public static Color Srgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

    public static Texture2D Tex(string dir, string file)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{file}");
        if (t == null) Debug.LogWarning($"[nagisa] texture missing: {dir}/{file}");
        return t;
    }

    // =================================================================== mesh helpers

    private static readonly HashSet<string> _meshNames = new HashSet<string>();

    public static Mesh Finish(string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                              List<Color> colors = null)
    {
        var mesh = new Mesh { name = name };
        mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        if (colors != null && colors.Count == verts.Count) mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static GameObject AddMesh(Transform parent, string name, Mesh mesh, Material mat, bool collider)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;

        if (!_meshNames.Add(mesh.name))
            Debug.LogWarning($"[nagisa] duplicate generated mesh name '{mesh.name}' - asset overwritten");
        string path = $"{MeshDir}/{mesh.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return go;
    }

    private static List<GameObject> FindRootsByExactName(string exactName)
    {
        var hits = new List<GameObject>();
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == exactName) hits.Add(go);
        return hits;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TAKA HIGHLANDS - "Earn the View". The world's first true MOUNTAIN region: a 24 km
/// point-to-point highlands road that climbs 1,080 m from a meadow gate at 900 m to the Taka
/// Col at 1,980 m, then spills 420 m down the far side to a highland descent finish.
/// Authored against <c>reference/improve/region_designs/taka_highlands.md</c>.
///
/// WHAT MAKES THIS REGION DIFFERENT FROM THE THREE THAT CAME BEFORE
/// ----------------------------------------------------------------
/// * It is OPEN, not a loop. Sakura, Shiosai and Maple City all publish a closing sample so a
///   course leg wraps; Taka does not, and <c>RouteCourse.Wrap()</c> clamps instead. Every
///   sweep in this file therefore iterates spans 0..Count-2 with no wrap special-casing and
///   simply STOPS at the finish - there is no seam to close.
/// * It is 24 km across a 4.5 x 7.7 km plan. That is five times Maple City's footprint, so the
///   terrain cannot be a corridor ribbon plus a flat basin plate: from the col you look back
///   down over the entire 7 km climbing stem, 1,080 m below, and a basin plate would read as a
///   sheet of card under a toy road. The ground here is a real continuous height field - see
///   <see cref="Height"/>.
/// * Its premise is VISIBILITY. Where the other three lean on fog for depth, Taka runs the
///   lowest fog density in the game (RegionDirector.TakaAmbience) and buys its depth cue from
///   aerial perspective and layered ridge silhouettes instead.
///
/// SIGNATURE AMBIENT VFX. Sakura has cherry petals; Maple City has drifting maple leaves; Taka
/// has WIND-BLOWN GRASS SEED - dandelion clocks, straw seed heads, thistledown and pale upland
/// florets on a prevailing crosswind (design doc section 3). Keeping that distinct is a hard
/// requirement: the drift sprite atlas in tools/blender/taka_surfaces.py carries an assertion
/// that every variant has green >= blue, because anything that goes blue-of-green blooms pink
/// under the grade and the region instantly reads as Sakura Pass with different hills.
///
/// WHY THIS IS C# AND NOT BLENDER. Same reasoning <see cref="ShiosaiCoastEnvironment"/> and
/// <see cref="MapleCityEnvironment"/> document: every piece of the region is measured off the
/// published centreline, so authoring it in a DCC tool would mean re-publishing that centreline
/// into a second tool for no gain and adding a way for the scenery to desynchronise from the
/// road the rider actually rides. The route and the surfaces ARE published through the
/// pipeline:
///   tools/blender/taka_route.py    -> Assets/Environment/TakaMountains/TakaRoute.json
///   tools/blender/taka_surfaces.py -> Assets/Environment/TakaMountains/Textures/*.png
///
/// IDEMPOTENCY. <see cref="Apply"/> destroys every root matching the EXACT name
/// "Taka Mountains Environment" and rebuilds from scratch, so re-running converges instead of
/// accumulating. Every generated mesh is written to disk under Meshes/ so the saved scene never
/// carries a dangling reference to a runtime-only Mesh.
/// </summary>
public static partial class TakaMountainsEnvironment
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RoutePath = "Assets/Environment/TakaMountains/TakaRoute.json";
    private const string MaterialDir = "Assets/Environment/TakaMountains/Materials";
    private const string MeshDir = "Assets/Environment/TakaMountains/Meshes";
    private const string TextureDir = "Assets/Environment/TakaMountains/Textures";
    private const string SakuraTextureDir = "Assets/Environment/SakuraPass/Textures";
    private const string CoastTextureDir = "Assets/Environment/ShiosaiCoast/Textures";

    public const string RootName = "Taka Mountains Environment";

    private const string CelShaderName = "MapleRide/HDRP/CelLit";
    private const string FoliageShaderName = "MapleRide/HDRP/Foliage";
    private const string TerrainShaderName = "MapleRide/HDRP/Terrain";

    // =================================================================== provisional tuning
    // EVERY number below is PROVISIONAL. The design handoff is explicit that thresholds,
    // distances and art tuning are illustrative; these values exist so the region is rideable
    // and reviewable, not because any of them is a final requirement.

    // ---- shared road cross-section --------------------------------------------------------
    // Taka publishes a NARROWER road than the other three regions (3.0 m half width instead of
    // 3.5 m) because a single-track upland road between dry-stone walls is the whole point of
    // the place. The LANE OFFSET is unchanged, because RouteFollower's is a global constant.
    private const float RoadHalfWidth = 2.6f;   // narrower than the highlands: an alpine pass road
    private const float ShoulderWidth = 0.45f;  // barely a shoulder - guardrail then air
    /// <summary>Parabolic carriageway crown, metres. Matched to Sakura Pass's authored 6 cm.</summary>
    private const float RoadCrown = 0.06f;
    /// <summary>Metres the ground's road-corridor nodes sit below the published centreline.</summary>
    private const float VergeDropM = 0.05f;
    /// <summary>Lane line the player and ambient riders ride. MUST match RouteFollower.laneOffset.</summary>
    private const float LaneLineOffsetM = 1.7f;
    /// <summary>Metres the rider transform is lifted above the centreline (RouteFollower.heightOffset).</summary>
    private const float RiderLiftM = 0.02f;

    private static float CrownAt(float offset) =>
        RoadCrown * (1f - Mathf.Pow(Mathf.Abs(offset) / (RoadHalfWidth + ShoulderWidth), 2f));

    /// <summary>
    /// Vertical bias applied to the swept carriageway, SOLVED rather than guessed, so the road
    /// surface at the lane line lands exactly on the height the rider is placed at. Identical
    /// solve to the coast and the city - getting it wrong buries half the wheel in the asphalt.
    /// </summary>
    private static readonly float RoadSurfaceLiftM =
        RiderLiftM + VergeDropM - CrownAt(LaneLineOffsetM);

    /// <summary>Paint sits this far proud of the road so it never z-fights.</summary>
    private const float MarkingLiftM = 0.015f;

    // ---- terrain ---------------------------------------------------------------------------

    /// <summary>
    /// Half width of the finely-tessellated corridor ribbon, metres. Beyond this the background
    /// height field takes over; see <see cref="BuildCorridor"/> for how the two are stitched
    /// without either a crack or a z-fight.
    /// </summary>
    private const float CorridorHalfWidthM = 90f;

    /// <summary>Route stations between corridor rows. 3 x 4.0 m spacing = a row every 12 m.</summary>
    private const int CorridorStride = 3;

    /// <summary>Background height-field cell size, metres.</summary>
    private const float BackgroundCellM = 32f;

    /// <summary>Metres of empty ground authored beyond the route's plan bounds.</summary>
    private const float BackgroundPadM = 1600f;

    /// <summary>
    /// Quads of the background field whose four corners are ALL inside this radius of the route
    /// are dropped, because the corridor ribbon already covers them at a much finer tessellation.
    /// Comfortably inside <see cref="CorridorHalfWidthM"/> so the ribbon overlaps the hole it
    /// leaves rather than merely abutting it - an abutted seam cracks, an overlapped one cannot.
    /// </summary>
    private const float BackgroundSkipRadiusM = 78f;

    /// <summary>Resolution of the coarse inverse-distance-weighted landform grid, metres.</summary>
    private const float LandformCellM = 96f;

    /// <summary>Every Nth route station feeds the landform solve. 10 x 4 m = a sample per 40 m.</summary>
    private const int LandformRouteStride = 10;

    // ---- signature identity ---------------------------------------------------------------

    /// <summary>
    /// Prevailing wind, as a world-space XZ direction. The whole region is combed by it: the
    /// seed drift blows along it, the marker posts lean away from it, and the hut's smoke
    /// streams with it. One constant so those never disagree. PROVISIONAL.
    /// </summary>
    private static readonly Vector3 WindDir = new Vector3(0.63f, 0f, 0.78f).normalized;

    /// <summary>
    /// THE SNOW LINE. This is the single most identity-defining number in the region and it
    /// replaces the highlands' turf-to-dry-grass band wholesale: Taka is above the treeline
    /// everywhere, so the ground blend is not green-to-gold, it is bare granite to snowfield.
    ///
    /// The band is placed so the rider CROSSES it while climbing and can see that they have.
    /// The route runs 1,400 m to 2,842 m; putting first snow at 2,300 m and full cover at
    /// 2,680 m means the lower two thirds of the climb are honest bare rock, the North Wall
    /// checkpoint (2,570 m) sits mid-transition with snow in the gullies and rock on the ribs,
    /// and the summit is unambiguously white. A band that completed below the North Wall would
    /// throw away the whole middle of the climb; one that started above it would make the
    /// summit arrive without warning. PROVISIONAL.
    /// </summary>
    private const float DryGrassStartY = 2300f;   // first snow in the shaded gullies
    private const float DryGrassFullY = 2680f;    // continuous snowfield

    // ---- the massif -----------------------------------------------------------------------
    // WHY THIS EXISTS. The first build derived the whole landform from the road by inverse
    // distance weighting, which guarantees the ground agrees with the carriageway but has one
    // fatal consequence: far from the road the weights even out and the surface relaxes to the
    // MEAN route elevation. The result rendered as a 24 km plateau - the col panorama, the shot
    // the region is named for, looked out over flat ground at 1,980 m in every direction. A pass
    // is only a pass if the world falls away from it.
    //
    // The second attempt was a Gaussian dome crowned on the col. That produced a real mountain,
    // and a worse bug: a dome centred on the pass makes the pass its own summit, so the payoff
    // view looked straight into a hillside 200 m ABOVE the road. A col is a place you look OUT
    // from, not up at.
    //
    // So the model here is the one a real pass road follows: the land falls away from the ROAD,
    // in every direction, by an amount that grows with distance from it. Near the road the
    // inverse-distance field still rules (the hillside stacked between switchback legs has to
    // come from the road or it disagrees with it); far out the ground is the nearest road
    // elevation minus a deepening drop, floored at a valley height. Between two switchback legs
    // the distance-to-route is small, so no trench opens up between them - that falls out of the
    // model rather than needing a special case. All PROVISIONAL.
    private const float MassifDropMaxM = 1250f;   // how far below the road the far ground sinks
    private const float MassifDropStartM = 220f;  // no drop at all inside this distance
    private const float MassifDropEndM = 3000f;   // full drop beyond this distance
    private const float MassifValleyY = 1080f;    // hard floor - the valley below the trailhead
    private const float MassifBlendStartM = 170f; // road-derived below this distance
    private const float MassifBlendEndM = 1500f;  // pure massif beyond this distance

    // ---- the summit tarn's basin ----------------------------------------------------------
    // The tarn is a flat disc, so the ground under it must actually be a hollow or the water
    // plane is buried and only a sliver shows. The hollow is carved HERE, in the height
    // function, so every surface that resolves through Height - fell, corridor, flora, sheep -
    // agrees about it. The water is then filled to a fraction of the bowl depth, which is what
    // puts the shoreline somewhere believable instead of at a hand-guessed radius.
    private const float TarnBasinInnerM = 95f;
    private const float TarnBasinOuterM = 240f;

    // DEPTH AND FILL RAISED 16 -> 42 m and 12.5 -> 31 m after the summit capture came back as a
    // white dome with a dark smudge in it and no readable water at all.
    //
    // The arithmetic is the whole story. Relief amplitude near the summit is ~96 m and Relief()
    // itself swings about +/-1.5 in normalised units, so even damped to 15% of amplitude the
    // noise inside the basin still moved the ground by roughly +/-22 m - against 12.5 m of
    // water. The lake was not "hard to see": the bed was genuinely poking through it across most
    // of its area, and the few pixels that were water were a sliver in a trough.
    //
    // Two changes, and BOTH are needed. Deepening the bowl alone would only have made the
    // islands taller relative to a lower shoreline; damping the relief alone would have left a
    // pond too shallow to hold a shoreline at this scale. A tarn 42 m deep with 31 m of ice in
    // it, over ground that is genuinely smooth inside the basin, is a lake.
    private const float TarnBasinDepthM = 42f;
    private const float TarnFillM = 31f;

    /// <summary>Plan position of the tarn, solved once in <see cref="BuildLandform"/>.</summary>
    private static Vector2 _tarnCentre;

    /// <summary>
    /// The solved plan position of Kori Lake, for the diagnostics harness to aim at. Exposed
    /// because a hero-prop shot framed from a GUESSED offset photographs empty ground the moment
    /// the landform solve moves the prop - which is how the first Kori Lake capture came back as
    /// a blank snowfield that every log line called a success.
    /// </summary>
    public static Vector2 TarnCentreWorld => _tarnCentre;

    /// <summary>Plan position of the col - kept for reference shots. Solved in <see cref="BuildLandform"/>.</summary>
    private static Vector2 _massifCentre;

    /// <summary>
    /// Elevation of the nearest route sample, on the landform grid. Paired with
    /// <see cref="_roadDist"/> so the far field can fall away FROM THE ROAD rather than from the
    /// mean of the whole route (which is a plateau) or from a fixed summit (which buries the col).
    /// </summary>
    private static float[,] _roadElev;

    /// <summary>
    /// Coarse, CONTINUOUS distance-to-route field, metres, on the landform grid.
    ///
    /// WHY THIS IS NOT NearestStation. NearestStation searches a 3x3 block of 128 m buckets, so
    /// it answers accurately out to ~380 m and then returns -1 / +infinity. That was harmless
    /// while the only thing distance drove was a relief amplitude that had already saturated -
    /// but the moment the massif blend started reading it, the field jumped from "road-derived"
    /// to "pure massif" IN ONE CELL at the search boundary, and the render came back with the
    /// road standing on a 150 m causeway with vertical sides, stepped exactly like the bucket
    /// grid. A blend factor must come from a field that is continuous at every scale it is used
    /// at, so this one is solved by brute force over the strided route (a few million ops, once)
    /// and sampled bilinearly. NearestStation is still used for the road cut, where it is exact
    /// and the range is short.
    /// </summary>
    private static float[,] _roadDist;

    // =================================================================== entry points

    [MenuItem("MapleRide/Environment/Build Taka Mountains")]
    public static void Apply()
    {
        bool headless = Application.isBatchMode;
        if (headless && EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(MeshDir);
        MaterialCache.Clear();
        SakuraTextureImportSettings.ApplyAll();

        var route = TakaRoute.Load();
        Debug.Log($"[taka] route: {route.Count} samples, {route.Length:0.0} m point-to-point, " +
                  $"y {route.MinY:0.0} - {route.MaxY:0.0} m.");

        BuildLandform(route);

        // Converge, never accumulate.
        foreach (var stale in FindRootsByExactName(RootName))
            UnityEngine.Object.DestroyImmediate(stale);

        var root = new GameObject(RootName).transform;

        Kit2Reset();                        // TakaMountains.Kit2.cs (E2 GLB kit placements accumulate until Kit2Flush)
        BuildBackgroundTerrain(root, route);
        BuildApron(root, route);
        BuildCorridor(root, route);
        BuildRoad(root, route);
        BuildDryStoneWalls(root, route);
        BuildGatesAndGrids(root, route);
        BuildCairnsAndPosts(root, route);
        BuildColMarker(root, route);
        BuildTarn(root, route);
        BuildHut(root, route);
        // NO BuildSheep(). The highlands' grazing flock is the one prop that cannot survive
        // the move: nothing pastures above the treeline, and sheep on a 2,842 m snowfield
        // would undo the isolation the whole region is built to deliver. The method is left
        // in the file unused rather than deleted, so the diff against the highlands pass
        // stays readable.
        PlanProvence(route);                // TakaMountains.Ventoux.cs: lavender/olive plots the scatter keeps out of
        BuildFloraScatter(root, route);
        BuildAlpineDressing(root, route);   // TakaMountains.Alpine.cs (2026-09-26 fill-in)
        BuildVentoux(root, route);          // TakaMountains.Ventoux.cs (Mt. Ventoux theme)
        BuildLiving(root, route);           // TakaMountains.Living.cs (N6+L5: crowds, movers, arrival plaza)
        try
        {
            BuildKit2(root, route);         // TakaMountains.Kit2.cs (E2: bornes, summit hall, memorial, fountain)
            Kit2Flush(root);                // merge every kit placement into LOD chunks
        }
        catch (System.Exception e) { Debug.LogError("[taka] kit2 stage failed (region still builds): " + e); }
        BuildDistantRanges(root, route);
        BuildSeedVfx(root, route);

        // Re-bake so the highlands segment/course/checkpoints reach the runtime graph, then
        // stage the region systems the World Map drives. SetupRegionSystems is reused from the
        // coast pass ON PURPOSE: there must be exactly ONE authority staging the RegionDirector,
        // or two regions' build passes fight over the same component every time either is re-run.
        var graph = RouteGraphBaker.BakeAsset();
        ShiosaiCoastEnvironment.SetupRegionSystems(graph);

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
            FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.takaSky = BuildTakaSky();
            EditorUtility.SetDirty(regions);
        }
        else
        {
            Debug.LogWarning("[taka] no RegionDirector in scene - highlands sky not wired.");
        }

        int renderers = root.GetComponentsInChildren<MeshRenderer>(true).Length;
        Debug.Log($"[taka] highlands built: {renderers} renderers under '{RootName}'.");

        // MATERIALS MUST BE FLUSHED TO DISK HERE. SetDirty only MARKS an asset; in batchmode the
        // process quits immediately afterwards and every property edit is thrown away, and then
        // the NEXT batchmode process (the diagnostics capture) loads the STALE .mat from disk and
        // photographs the old colours. That failure is completely silent - the build logs its
        // renderer count, exits 0, and the render simply does not reflect the change. It cost
        // several full build+capture cycles on Maple City before it was found.
        AssetDatabase.SaveAssets();

        if (headless)
        {
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[taka] saved '{active.path}'.");
        }
    }

    // =================================================================== route

    [Serializable] private class SampleDto { public float[] p, t, s, u; public float bank, d; }
    [Serializable] private class RouteDto { public float roadHalfWidth, shoulderWidth; public SampleDto[] samples; }

    /// <summary>
    /// The published highlands centreline, in Unity world space.
    ///
    /// UNLIKE the other three regions this route carries NO closing sample: station Count-1 is
    /// the finish, 24 km from the start and nowhere near it. Geometry iterates spans
    /// 0..Count-2 and simply ends.
    /// </summary>
    public class TakaRoute
    {
        public Vector3[] Position = Array.Empty<Vector3>();
        public Vector3[] Tangent = Array.Empty<Vector3>();
        public float[] Distance = Array.Empty<float>();

        public int Count => Position.Length;
        public float Length => Distance.Length == 0 ? 0f : Distance[Distance.Length - 1];
        public float MinY, MaxY;
        public Bounds Plan;

        /// <summary>Arc fraction of a station - the parameter every profile in this file uses.</summary>
        public float Frac(int i) => Length <= 0.001f ? 0f : Distance[i] / Length;

        /// <summary>Horizontal right-hand side vector - ground and walls are never banked.</summary>
        public Vector3 SideFlat(int i)
        {
            var t = new Vector3(Tangent[i].x, 0f, Tangent[i].z);
            if (t.sqrMagnitude < 1e-6f) t = Vector3.forward;
            return Vector3.Cross(Vector3.up, t.normalized).normalized;
        }

        public int IndexAt(float metres)
        {
            for (int i = 0; i < Count; i++)
                if (Distance[i] >= metres) return i;
            return Count - 1;
        }

        public static TakaRoute Load()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(RoutePath);
            if (text == null)
                throw new FileNotFoundException(
                    $"{RoutePath} is missing. Run: python tools/blender/taka_route.py");

            var dto = JsonUtility.FromJson<RouteDto>(text.text);
            int n = dto.samples.Length;
            var r = new TakaRoute
            {
                Position = new Vector3[n],
                Tangent = new Vector3[n],
                Distance = new float[n],
                MinY = float.MaxValue,
                MaxY = float.MinValue,
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
            return r;
        }
    }

    // =================================================================== landform

    // THE HARDEST PROBLEM IN THIS REGION, and worth the explanation.
    //
    // Sakura, Shiosai and Maple City are all small enough (<= 5 km, <= 1.4 km wide) that their
    // ground can be a ribbon swept along the road plus a flat plate underneath to stop you
    // seeing sky through the gaps. Taka cannot use that: it is 24 km of road folded into a
    // 4.5 x 7.7 km box, so the road passes WITHIN SIGHT OF ITSELF constantly - from any
    // switchback you can see four other switchbacks - and from the col you look back down over
    // the whole 1,080 m climb. A ribbon would self-overlap and z-fight on every hairpin, and a
    // flat plate under a mountain reads as a sheet of card.
    //
    // So Taka has ONE continuous height function H(x, z) that the whole region is built from,
    // and it is derived FROM THE ROAD rather than invented alongside it:
    //
    //   1. LANDFORM. A coarse grid (LandformCellM) where every cell is an inverse-distance
    //      weighted blend of nearby route-sample elevations. This is the trick that makes the
    //      mountain believable for free: where seven switchback legs stack up a hillside, the
    //      legs' own elevations - 40 m apart vertically, 200 m apart horizontally - are what
    //      the blend averages, so the ground between them IS the hillside at the right angle.
    //      No hand-modelled slope can disagree with the road, because the road is its source.
    //
    //   2. RELIEF. Three octaves of Perlin on top, with an amplitude that GROWS with distance
    //      from the road: ~1.5 m of roll at the verge (so the shoulder is not a billiard table)
    //      rising to tens of metres out in the open fell (so the skyline has shape).
    //
    //   3. ROAD CUT. Within 18 m the ground is pinned to the carriageway's own height, easing
    //      out to pure landform by 46 m, which gives every switchback a real cut on the uphill
    //      side and a real fill on the downhill side without either being modelled.
    //
    // Distance-to-road is answered by a uniform bucket grid over the route stations, because
    // 82,000 background vertices x 6,001 stations would be half a billion distance tests.

    private static float[,] _landform;          // coarse IDW elevation grid
    private static float _landformOriginX, _landformOriginZ;
    private static int _landformNx, _landformNz;

    private const float BucketM = 128f;
    private static List<int>[,] _buckets;
    private static float _bucketOriginX, _bucketOriginZ;
    private static int _bucketNx, _bucketNz;
    private static TakaRoute _route;

    /// <summary>Padded plan bounds the background field and the landform grid both cover.</summary>
    private static void FieldBounds(TakaRoute r, out float x0, out float z0, out float x1, out float z1)
    {
        x0 = r.Plan.min.x - BackgroundPadM;
        z0 = r.Plan.min.z - BackgroundPadM;
        x1 = r.Plan.max.x + BackgroundPadM;
        z1 = r.Plan.max.z + BackgroundPadM;
    }

    private static void BuildLandform(TakaRoute r)
    {
        _route = r;

        // Solve the tarn's plan position ONCE, here, because the height function has to carve
        // its basin and the prop builder has to sit the water in it - and if those two ever
        // disagreed the tarn would hover or vanish. No y term: Height is not available yet.
        {
            int ti = r.IndexAt(CpCol - 1250f);
            var side = r.SideFlat(ti);
            _tarnCentre = new Vector2(r.Position[ti].x + side.x * -155f,
                                      r.Position[ti].z + side.z * -155f);
            int ci = r.IndexAt(CpCol);
            _massifCentre = new Vector2(r.Position[ci].x, r.Position[ci].z);
        }

        FieldBounds(r, out float x0, out float z0, out float x1, out float z1);

        // ---- station buckets, for distance-to-road queries ---------------------------------
        _bucketOriginX = x0; _bucketOriginZ = z0;
        _bucketNx = Mathf.CeilToInt((x1 - x0) / BucketM) + 1;
        _bucketNz = Mathf.CeilToInt((z1 - z0) / BucketM) + 1;
        _buckets = new List<int>[_bucketNx, _bucketNz];
        for (int i = 0; i < r.Count; i++)
        {
            int bx = Mathf.Clamp((int)((r.Position[i].x - x0) / BucketM), 0, _bucketNx - 1);
            int bz = Mathf.Clamp((int)((r.Position[i].z - z0) / BucketM), 0, _bucketNz - 1);
            (_buckets[bx, bz] ??= new List<int>(32)).Add(i);
        }

        // ---- inverse-distance landform grid -------------------------------------------------
        _landformOriginX = x0; _landformOriginZ = z0;
        _landformNx = Mathf.CeilToInt((x1 - x0) / LandformCellM) + 1;
        _landformNz = Mathf.CeilToInt((z1 - z0) / LandformCellM) + 1;
        _landform = new float[_landformNx, _landformNz];
        _roadDist = new float[_landformNx, _landformNz];
        _roadElev = new float[_landformNx, _landformNz];

        var src = new List<int>(r.Count / LandformRouteStride + 2);
        for (int i = 0; i < r.Count; i += LandformRouteStride) src.Add(i);
        src.Add(r.Count - 1);

        // Softening radius. Squared-distance-plus-epsilon keeps the weight finite ON a station
        // (otherwise the cell containing a sample snaps to exactly that sample's height and the
        // grid gets a pockmark every 40 m), and the 1.6 exponent makes the falloff steep enough
        // that a hillside 200 m away does not drag the valley floor up with it.
        const float Eps = 130f * 130f;
        const float Power = 1.6f;

        for (int gx = 0; gx < _landformNx; gx++)
        {
            float px = x0 + gx * LandformCellM;
            for (int gz = 0; gz < _landformNz; gz++)
            {
                float pz = z0 + gz * LandformCellM;
                double sw = 0.0, sy = 0.0;
                float nearSq = float.MaxValue, nearY = 0f;
                for (int k = 0; k < src.Count; k++)
                {
                    var q = r.Position[src[k]];
                    float dx = q.x - px, dz = q.z - pz;
                    float sq = dx * dx + dz * dz;
                    if (sq < nearSq) { nearSq = sq; nearY = q.y; }
                    double w = 1.0 / Math.Pow(sq + Eps, Power);
                    sw += w; sy += w * q.y;
                }
                _landform[gx, gz] = (float)(sy / sw);
                _roadDist[gx, gz] = Mathf.Sqrt(nearSq);
                _roadElev[gx, gz] = nearY;
            }
        }

        Debug.Log($"[taka] landform: {_landformNx}x{_landformNz} cells @ {LandformCellM} m " +
                  $"from {src.Count} route samples; buckets {_bucketNx}x{_bucketNz}.");
    }

    /// <summary>Bilinear sample of the coarse landform grid.</summary>
    private static float Landform(float x, float z)
    {
        float fx = Mathf.Clamp((x - _landformOriginX) / LandformCellM, 0f, _landformNx - 1.001f);
        float fz = Mathf.Clamp((z - _landformOriginZ) / LandformCellM, 0f, _landformNz - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float a = Mathf.Lerp(_landform[ix, iz], _landform[ix + 1, iz], tx);
        float b = Mathf.Lerp(_landform[ix, iz + 1], _landform[ix + 1, iz + 1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    /// <summary>Bilinear sample of the continuous distance-to-route field. See <see cref="_roadDist"/>.</summary>
    private static float RoadDist(float x, float z)
    {
        float fx = Mathf.Clamp((x - _landformOriginX) / LandformCellM, 0f, _landformNx - 1.001f);
        float fz = Mathf.Clamp((z - _landformOriginZ) / LandformCellM, 0f, _landformNz - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float a = Mathf.Lerp(_roadDist[ix, iz], _roadDist[ix + 1, iz], tx);
        float b = Mathf.Lerp(_roadDist[ix, iz + 1], _roadDist[ix + 1, iz + 1], tx);
        // Outside the solved grid the field is clamped, which is correct: everything out there
        // is far beyond MassifBlendEndM and already fully handed over to the massif.
        return Mathf.Lerp(a, b, tz);
    }

    /// <summary>Bilinear sample of the nearest-route-elevation field. See <see cref="_roadElev"/>.</summary>
    private static float RoadElev(float x, float z)
    {
        float fx = Mathf.Clamp((x - _landformOriginX) / LandformCellM, 0f, _landformNx - 1.001f);
        float fz = Mathf.Clamp((z - _landformOriginZ) / LandformCellM, 0f, _landformNz - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float a = Mathf.Lerp(_roadElev[ix, iz], _roadElev[ix + 1, iz], tx);
        float b = Mathf.Lerp(_roadElev[ix, iz + 1], _roadElev[ix + 1, iz + 1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    /// <summary>
    /// The far field: the land as it is once it is too far from the road for the inverse-distance
    /// solve to mean anything. It falls away from the nearest carriageway elevation by a drop
    /// that deepens with distance, floored at the valley height. See the MassifDrop constants for
    /// why it is neither a plateau nor a dome.
    /// </summary>
    private static float Massif(float x, float z, float dq)
    {
        float drop = MassifDropMaxM * Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(MassifDropStartM, MassifDropEndM, dq));
        return Mathf.Max(MassifValleyY, RoadElev(x, z) - drop);
    }

    /// <summary>
    /// Nearest route station to a world XZ position, searched through the bucket grid.
    /// Returns -1 (and <paramref name="dist"/> = +inf) when nothing is within ~380 m, which is
    /// far enough out that the road term is zero anyway.
    /// </summary>
    private static int NearestStation(float x, float z, out float dist)
    {
        int bx = Mathf.Clamp((int)((x - _bucketOriginX) / BucketM), 0, _bucketNx - 1);
        int bz = Mathf.Clamp((int)((z - _bucketOriginZ) / BucketM), 0, _bucketNz - 1);
        int best = -1;
        float bestSq = float.MaxValue;
        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
        {
            int cx = bx + ox, cz = bz + oz;
            if (cx < 0 || cz < 0 || cx >= _bucketNx || cz >= _bucketNz) continue;
            var list = _buckets[cx, cz];
            if (list == null) continue;
            for (int k = 0; k < list.Count; k++)
            {
                var q = _route.Position[list[k]];
                float dx = q.x - x, dz = q.z - z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = list[k]; }
            }
        }
        dist = best < 0 ? float.PositiveInfinity : Mathf.Sqrt(bestSq);
        return best;
    }

    /// <summary>Three octaves of fell relief, in normalised units (roughly -1..+1).</summary>
    private static float Relief(float x, float z)
    {
        float a = Mathf.PerlinNoise(x * 0.00035f + 31.7f, z * 0.00035f + 12.4f) - 0.5f;
        float b = Mathf.PerlinNoise(x * 0.00110f + 7.9f, z * 0.00110f + 55.1f) - 0.5f;
        float c = Mathf.PerlinNoise(x * 0.00340f + 64.2f, z * 0.00340f + 3.3f) - 0.5f;
        return (a * 2.0f + b * 0.84f + c * 0.32f);
    }

    /// <summary>
    /// THE height function. Everything that touches the ground in this region - the background
    /// field, the corridor ribbon, every scattered tuft, every cairn, every sheep - resolves its
    /// y through here, which is what guarantees nothing floats and nothing sinks.
    /// </summary>
    private static float Height(float x, float z)
    {
        int s = NearestStation(x, z, out float d);
        // The SMOOTH distance field drives everything that has to be continuous at kilometre
        // scale. The exact bucket distance 'd' is used only for the road cut below, where it is
        // accurate and the range is under 50 m.
        float dq = RoadDist(x, z);

        // The tarn's hollow, carved in the height function itself so every surface agrees on it.
        float bowl = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(TarnBasinInnerM, TarnBasinOuterM,
                              Vector2.Distance(new Vector2(x, z), _tarnCentre)));

        // Relief amplitude grows with distance from the road: a quiet verge, a shaped fell. It
        // is damped inside the tarn's basin, or the noise would push islands up through a lake
        // that is only ten metres deep.
        // AMPLITUDE RAISED AND ITS ONSET PULLED IN after the first render pass. At 1.5 m inside
        // 50 m growing to 78 m only past 900 m, every surface within half a kilometre of the
        // road was effectively flat - and because the summit is a 290 m plateau loop, the whole
        // payoff view rendered as a featureless white TABLE with a crisp rim. Rock does not do
        // that. Starting the ramp at 40 m and reaching 96 m by 700 m puts real ribs and gullies
        // on the ground the rider can actually see, which is also what finally lets the snow
        // line read: snow needs gullies to collect in.
        // DAMPING RAISED 0.85 -> 1.0: relief is now suppressed ENTIRELY at the basin centre, not
        // merely reduced. See TarnBasinDepthM for why 15% of a 96 m amplitude was still enough
        // to push the bed straight through the ice.
        float amp = Mathf.Lerp(3f, 96f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(40f, 700f, dq)))
                    * (1f - bowl);

        // Road-derived field near the road; the broad massif far from it. See MassifPeakY.
        float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MassifBlendStartM, MassifBlendEndM, dq));
        float bed = Mathf.Lerp(Landform(x, z), Massif(x, z, dq), far);
        float open = bed + Relief(x, z) * amp - TarnBasinDepthM * bowl;

        if (s < 0) return open;

        float roadCut = _route.Position[s].y - VergeDropM;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 46f, d));
        return Mathf.Lerp(roadCut, open, t);
    }

    /// <summary>
    /// Splat weights for the terrain shader's UV1 channel: x = rock, y = dry grass.
    ///
    /// The altitude ramp is the region's palette made mechanical - emerald turf in the meadows,
    /// golden dry grass on the exposed tops - and it is finished BELOW the col on purpose so the
    /// payoff view is delivered over gold, not over valley green.
    /// </summary>
    private static Vector2 GroundSplat(float x, float y, float z)
    {
        float dry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DryGrassStartY, DryGrassFullY, y));
        // Wind-scoured patches: the tops are not uniformly gold, they are blotched where the
        // wind strips the turf back. Cheap, and it stops the upper half reading as one flat wash.
        dry = Mathf.Clamp01(dry + (Mathf.PerlinNoise(x * 0.0019f + 2.2f, z * 0.0019f + 9.6f) - 0.5f) * 0.42f);
        return new Vector2(0f, dry);
    }

    // =================================================================== terrain meshes

    /// <summary>
    /// The open fell: one regular grid over the padded plan bounds, evaluating
    /// <see cref="Height"/>, with the quads nearest the road dropped because the corridor ribbon
    /// covers them at four times the resolution.
    ///
    /// Split into tiles rather than built as one mesh, for two reasons: a single 24 km field
    /// would be ~82,000 verts in one renderer with a bounding box the size of the region, so it
    /// could never be frustum-culled and every shot would pay for all of it; and Unity's mesh
    /// asset serialisation gets unhappy well before that.
    /// </summary>
    private static void BuildBackgroundTerrain(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Fell").transform;
        group.SetParent(root, false);

        FieldBounds(route, out float x0, out float z0, out float x1, out float z1);
        int nx = Mathf.CeilToInt((x1 - x0) / BackgroundCellM);
        int nz = Mathf.CeilToInt((z1 - z0) / BackgroundCellM);

        const int Tile = 48;                      // cells per tile edge -> ~1.5 km tiles
        int tx = Mathf.CeilToInt(nx / (float)Tile);
        int tz = Mathf.CeilToInt(nz / (float)Tile);
        var mat = GroundMaterial();
        int built = 0, dropped = 0;

        for (int ti = 0; ti < tx; ti++)
        for (int tj = 0; tj < tz; tj++)
        {
            int cx0 = ti * Tile, cz0 = tj * Tile;
            int cx1 = Mathf.Min(cx0 + Tile, nx), cz1 = Mathf.Min(cz0 + Tile, nz);
            int cols = cx1 - cx0 + 1, rows = cz1 - cz0 + 1;

            var verts = new Vector3[cols * rows];
            var uvs = new Vector2[verts.Length];
            var uv2 = new Vector2[verts.Length];
            var near = new bool[verts.Length];

            for (int a = 0; a < cols; a++)
            for (int b = 0; b < rows; b++)
            {
                float px = x0 + (cx0 + a) * BackgroundCellM;
                float pz = z0 + (cz0 + b) * BackgroundCellM;
                float py = Height(px, pz);
                int k = a * rows + b;
                verts[k] = new Vector3(px, py, pz);
                uvs[k] = new Vector2(px / 8f, pz / 8f);
                uv2[k] = GroundSplat(px, py, pz);
                NearestStation(px, pz, out float d);
                near[k] = d < BackgroundSkipRadiusM;
            }

            var tris = new List<int>();
            for (int a = 0; a < cols - 1; a++)
            for (int b = 0; b < rows - 1; b++)
            {
                int p0 = a * rows + b, p1 = p0 + 1;
                int q0 = (a + 1) * rows + b, q1 = q0 + 1;
                if (near[p0] && near[p1] && near[q0] && near[q1]) { dropped++; continue; }
                tris.Add(p0); tris.Add(p1); tris.Add(q0);
                tris.Add(p1); tris.Add(q1); tris.Add(q0);
            }
            if (tris.Count == 0) continue;

            AddMesh(group, $"Fell {ti}_{tj}",
                    Finish($"Taka_Fell_{ti}_{tj}", verts, uvs, tris, uv2), mat, collider: false);
            built++;
        }

        Debug.Log($"[taka] fell: {built} tiles ({nx}x{nz} cells @ {BackgroundCellM} m), " +
                  $"{dropped} quads yielded to the corridor.");
    }

    /// <summary>
    /// The apron: coarse outland that carries the ground from the edge of the detailed fell out
    /// past the distant ranges.
    ///
    /// WHY. The fell plate stops 1.6 km beyond the route's plan bounds, which along the short
    /// axis is only ~3.9 km from the centre - INSIDE the near ridge ring at 5.5 km. Every wide
    /// shot therefore showed a hard horizontal cut where the ground simply ended and the ridge
    /// wall started, which read as a white band across the horizon and killed the depth the
    /// ranges exist to create.
    ///
    /// It matches the fell's outer edge BY CONSTRUCTION rather than by tuning: it calls the very
    /// same <see cref="Height"/> function, which out here is already fully handed over to the
    /// massif. The cells are 256 m because nothing at this range is ever closer than four
    /// kilometres to the camera.
    /// </summary>
    private static void BuildApron(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Apron").transform;
        group.SetParent(root, false);

        FieldBounds(route, out float fx0, out float fz0, out float fx1, out float fz1);
        var c = route.Plan.center;
        const float Reach = 19000f;       // comfortably past the 14 km far range ring
        const float Cell = 256f;
        const int Tile = 24;

        // PROVISIONAL. No apron quad may be built within this distance of the road: at 256 m per
        // cell the apron cannot represent the ridden corridor, it can only bury it.
        const float ApronRouteClearM = 1400f;

        float ax0 = c.x - Reach, az0 = c.z - Reach;
        int nx = Mathf.CeilToInt(Reach * 2f / Cell);
        int nz = Mathf.CeilToInt(Reach * 2f / Cell);
        var mat = GroundMaterial();
        int built = 0;

        for (int ti = 0; ti * Tile < nx; ti++)
        for (int tj = 0; tj * Tile < nz; tj++)
        {
            int cx0 = ti * Tile, cz0 = tj * Tile;
            int cx1 = Mathf.Min(cx0 + Tile, nx), cz1 = Mathf.Min(cz0 + Tile, nz);
            int cols = cx1 - cx0 + 1, rows = cz1 - cz0 + 1;

            var verts = new Vector3[cols * rows];
            var uvs = new Vector2[verts.Length];
            var uv2 = new Vector2[verts.Length];
            var inside = new bool[verts.Length];

            for (int a = 0; a < cols; a++)
            for (int b = 0; b < rows; b++)
            {
                float px = ax0 + (cx0 + a) * Cell;
                float pz = az0 + (cz0 + b) * Cell;
                float py = Height(px, pz);
                int k = a * rows + b;
                verts[k] = new Vector3(px, py, pz);
                uvs[k] = new Vector2(px / 8f, pz / 8f);
                uv2[k] = GroundSplat(px, py, pz);
                // A vertex the detailed fell already owns. Quads with all four corners inside
                // are dropped; the one-cell overlap band is deliberate, so the two surfaces
                // interpenetrate instead of abutting (an abutted seam cracks - see BuildCorridor).
                //
                // The ROUTE-DISTANCE term is the second half of that test and is not optional.
                // The field bounds are the bounding box of the route PLAN, so the meadow gate at
                // station 0 sits exactly ON the boundary: every apron quad around it failed the
                // box test, got built, and covered the road, the walls and the start line with a
                // flat 256 m slab lifted metres above them - because a 256 m sample of a hillside
                // is nowhere near the road's own elevation. The apron exists only to hide the
                // fell plate's edge ~4 km out, so it has no business within a kilometre of
                // anything the player rides past.
                inside[k] = (px > fx0 + Cell && px < fx1 - Cell && pz > fz0 + Cell && pz < fz1 - Cell)
                            || RoadDist(px, pz) < ApronRouteClearM;
            }

            var tris = new List<int>();
            for (int a = 0; a < cols - 1; a++)
            for (int b = 0; b < rows - 1; b++)
            {
                int p0 = a * rows + b, p1 = p0 + 1;
                int q0 = (a + 1) * rows + b, q1 = q0 + 1;
                if (inside[p0] && inside[p1] && inside[q0] && inside[q1]) continue;
                tris.Add(p0); tris.Add(p1); tris.Add(q0);
                tris.Add(p1); tris.Add(q1); tris.Add(q0);
            }
            if (tris.Count == 0) continue;

            var go = AddMesh(group, $"Apron {ti}_{tj}",
                             Finish($"Taka_Apron_{ti}_{tj}", verts, uvs, tris, uv2), mat,
                             collider: false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            built++;
        }

        Debug.Log($"[taka] apron: {built} tiles @ {Cell} m out to {Reach:0} m.");
    }

    /// <summary>
    /// The finely-tessellated ground the rider actually sees: a ribbon swept along the
    /// centreline out to <see cref="CorridorHalfWidthM"/>, evaluating the SAME
    /// <see cref="Height"/> function as the fell.
    ///
    /// THE STITCH. Because both surfaces come from one function they agree in VALUE everywhere,
    /// but they disagree slightly in INTERPOLATION (12 m rows here against 32 m cells out there),
    /// so butting them edge to edge would open hairline cracks you could see sky through at
    /// grazing angles. Instead the ribbon reaches 90 m while the fell only yields quads inside
    /// 78 m, and the ribbon's outer half is lifted a few centimetres: the overlap band is a
    /// dozen metres wide, the ribbon reliably wins the depth test in it, and there is no seam to
    /// crack. The lift is 5 cm, seen only at 80+ m and at a grazing angle - comfortably
    /// sub-pixel. This is the standard terrain-LOD skirt trick, minus the skirt.
    /// </summary>
    private static void BuildCorridor(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Verge").transform;
        group.SetParent(root, false);

        float[] nodes =
        {
            -90f, -70f, -55f, -46f, -34f, -24f, -16f, -10f, -5.2f,
            0f,
            5.2f, 10f, 16f, 24f, 34f, 46f, 55f, 70f, 90f,
        };

        var rows = new List<int>();
        for (int i = 0; i < route.Count; i += CorridorStride) rows.Add(i);
        if (rows[rows.Count - 1] != route.Count - 1) rows.Add(route.Count - 1);

        int cols = nodes.Length;
        var mat = GroundMaterial();
        const int RowsPerTile = 260;              // ~3.1 km of road per renderer

        for (int t0 = 0; t0 < rows.Count - 1; t0 += RowsPerTile)
        {
            int t1 = Mathf.Min(t0 + RowsPerTile, rows.Count - 1);
            int n = t1 - t0 + 1;
            var verts = new Vector3[n * cols];
            var uvs = new Vector2[verts.Length];
            var uv2 = new Vector2[verts.Length];

            for (int ri = 0; ri < n; ri++)
            {
                int i = rows[t0 + ri];
                var p = route.Position[i];
                var s = route.SideFlat(i);
                for (int c = 0; c < cols; c++)
                {
                    float o = nodes[c];
                    float px = p.x + s.x * o, pz = p.z + s.z * o;
                    float py = Height(px, pz)
                             + 0.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 55f, Mathf.Abs(o)));
                    int k = ri * cols + c;
                    verts[k] = new Vector3(px, py, pz);
                    uvs[k] = new Vector2(px / 8f, pz / 8f);
                    uv2[k] = GroundSplat(px, py, pz);
                }
            }

            var tris = new List<int>();
            for (int ri = 0; ri < n - 1; ri++)
            for (int c = 0; c < cols - 1; c++)
            {
                int a = ri * cols + c, b = a + 1;
                int d = (ri + 1) * cols + c, e = d + 1;
                tris.Add(a); tris.Add(d); tris.Add(b);
                tris.Add(b); tris.Add(d); tris.Add(e);
            }

            AddMesh(group, $"Verge {t0}",
                    Finish($"Taka_Verge_{t0}", verts, uvs, tris, uv2), mat, collider: false);
        }
    }

    // =================================================================== road

    /// <summary>Carriageway surface height at a station and offset, including crown and bias.</summary>
    private static float RoadY(TakaRoute r, int i, float offset) =>
        r.Position[i].y + RoadSurfaceLiftM + CrownAt(offset);

    private static void BuildRoad(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Roadway").transform;
        group.SetParent(root, false);

        const int Cols = 9;
        float half = RoadHalfWidth + ShoulderWidth;
        const int SpansPerTile = 800;             // ~3.2 km of carriageway per renderer

        for (int t0 = 0; t0 < route.Count - 1; t0 += SpansPerTile)
        {
            int t1 = Mathf.Min(t0 + SpansPerTile, route.Count - 1);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = t0; i < t1; i++)
            {
                int b0 = verts.Count;
                for (int k = 0; k < 2; k++)
                {
                    int idx = i + k;
                    var p = route.Position[idx];
                    var s = route.SideFlat(idx);
                    for (int c = 0; c < Cols; c++)
                    {
                        float o = Mathf.Lerp(-half, half, c / (float)(Cols - 1));
                        verts.Add(new Vector3(p.x + s.x * o, RoadY(route, idx, o), p.z + s.z * o));
                        // Isotropic metre-scale UVs. Mapping the tile across "the whole
                        // carriageway into 0..1" is what turned the coast's road into a flat
                        // grey plane; never do it.
                        uvs.Add(new Vector2(o / 4f, route.Distance[idx] / 4f));
                    }
                }
                for (int c = 0; c < Cols - 1; c++)
                {
                    int p0 = b0 + c, p1 = p0 + 1;
                    int q0 = b0 + Cols + c, q1 = q0 + 1;
                    tris.Add(p0); tris.Add(q0); tris.Add(p1);
                    tris.Add(p1); tris.Add(q0); tris.Add(q1);
                }
            }

            AddMesh(group, $"Taka Asphalt {t0}",
                    Finish($"Taka_Asphalt_{t0}", verts.ToArray(), uvs.ToArray(), tris),
                    AsphaltMaterial(), collider: true);
        }

        BuildMarkings(group, route);
    }

    /// <summary>
    /// Road paint. A HIGHLAND road is not a boulevard: the design's reference is a single-track
    /// pass road, so there is no cycle lane and no continuous edge line. What it does carry is a
    /// worn dashed centre line on the lower, wider, busier third, FADING OUT as the road climbs
    /// past the pasture and the county stops bothering to repaint it. That fade is free
    /// storytelling and it is also the cheapest possible altitude cue.
    /// </summary>
    private static void BuildMarkings(Transform group, TakaRoute route)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        const float DashM = 4f, GapM = 6f;
        for (int i = 0; i < route.Count - 1; i++)
        {
            float frac = route.Frac(i);
            // PROVISIONAL: paint on the first 42% of the route, thinning over the last tenth of
            // that so the line does not simply stop dead at an arbitrary station.
            if (frac > 0.42f) break;
            float keep = 1f - Mathf.InverseLerp(0.34f, 0.42f, frac);
            if (keep < 0.18f) continue;
            float cycle = Mathf.Repeat(route.Distance[i], DashM + GapM);
            if (cycle > DashM * keep) continue;

            var p0 = route.Position[i]; var s0 = route.SideFlat(i);
            var p1 = route.Position[i + 1]; var s1 = route.SideFlat(i + 1);
            const float w = 0.09f;
            var a0 = new Vector3(p0.x - s0.x * w, RoadY(route, i, 0f) + MarkingLiftM, p0.z - s0.z * w);
            var a1 = new Vector3(p0.x + s0.x * w, RoadY(route, i, 0f) + MarkingLiftM, p0.z + s0.z * w);
            var b0 = new Vector3(p1.x - s1.x * w, RoadY(route, i + 1, 0f) + MarkingLiftM, p1.z - s1.z * w);
            var b1 = new Vector3(p1.x + s1.x * w, RoadY(route, i + 1, 0f) + MarkingLiftM, p1.z + s1.z * w);

            int b = verts.Count;
            verts.Add(a0); verts.Add(a1); verts.Add(b0); verts.Add(b1);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right);
            uvs.Add(Vector2.up); uvs.Add(Vector2.one);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
        }

        if (tris.Count == 0) return;
        AddMesh(group, "Taka Markings",
                Finish("Taka_Markings", verts.ToArray(), uvs.ToArray(), tris),
                CelMaterial("Taka_LinePaint", new Color(0.80f, 0.78f, 0.70f),
                            gloss: 0.04f, spec: 0.04f, rim: 0.10f, shade: GroundShade),
                collider: false);
    }

    // =================================================================== dry-stone walls

    /// <summary>
    /// How much of the road is walled at a given arc fraction. The design calls dry-stone walls
    /// the region's STRUCTURAL SIGNATURE, but a wall that runs unbroken for 24 km is a corridor,
    /// not a landscape - and it would also hide the very view the region is named for.
    ///
    /// So the walls tell the altitude story: continuous through the enclosed lower pastures,
    /// intermittent across the false-flat grazing, and gone entirely above the windward ramp
    /// where there is nothing left to enclose and the fell is open to the sky. Above that line
    /// the road is edged by wind-frayed marker posts instead (see <see cref="BuildCairnsAndPosts"/>),
    /// which is exactly the transition a real upland pass makes. PROVISIONAL.
    /// </summary>
    private static float WallPresence(float frac, float distance)
    {
        // TAKA INVERTS THE HIGHLANDS RULE. On the fell the wall is a field boundary, so it
        // stops where the enclosed land stops. Here it is a PARAPET over a drop, so it does the
        // opposite: it is absent on the valley approach where there is nothing to fall off, and
        // becomes near-continuous once the road is cut into the wall above 0.25 arc. Deleting
        // the parapet from the exposed half would be the one thing a rider would actually
        // notice as wrong.
        float band = frac < 0.14f ? 0f : Mathf.Clamp01(Mathf.InverseLerp(0.14f, 0.30f, frac));
        // Field boundaries, gateways and collapsed sections - a wall with no gaps reads as a
        // extruded curve, which is precisely what the first pass looked like.
        if (band <= 0f) return 0f;
        // 2026-09-26 copilot session 3 (T4): open in front of the Chalet stop, so its forecourt
        // meets the road instead of sitting behind a parapet (which read as a bank).
        if (Mathf.Abs(distance - ChaletM) < 60f) return 0f;
        float gap = Mathf.PerlinNoise(distance * 0.0055f + 17.3f, 0.5f);
        // NOTE THE DIRECTION. The first build lerped the gap threshold 0.30 -> 0.62 with band,
        // which is backwards: band = 1 means "enclosed lower pasture, wall should be CONTINUOUS",
        // and a 0.62 threshold deletes nearly two thirds of it. The render showed no wall at all
        // at 3.4 km. Low ground keeps 18 % gaps (gateways, collapses); the thinning band above
        // loses most of it before the posts take over.
        // Gaps are for gallery mouths, lay-bys and washed-out sections: 12% at most. A parapet
        // with highland-sized gaps would read as a ruin rather than as engineered roadworks.
        return gap < Mathf.Lerp(0.30f, 0.12f, band) ? 0f : band;
    }

    private static void BuildDryStoneWalls(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Parapets").transform;
        group.SetParent(root, false);

        // PER-STONE GEOMETRY IS NOT AN OPTION. A 1.1 m wall along both sides of 24 km of road is
        // roughly 110,000 stones; at even 20 triangles each that is 2.2 million triangles for
        // scenery you ride past at 30 km/h. All of this wall's character therefore lives in
        // Taka_Concrete_Albedo/Normal (tools/blender/taka_surfaces.py), which is authored as
        // COURSED dry stone with dark joints and lichen precisely so a swept ribbon reads as
        // masonry. The only geometry that varies is the coping height, which wanders, because a
        // perfectly level wall top is the single biggest tell.
        const float Inset = RoadHalfWidth + ShoulderWidth + 1.15f;
        const float Thick = 0.34f;
        var mat = StoneMaterial();
        const int SpansPerTile = 900;

        for (int t0 = 0; t0 < route.Count - 1; t0 += SpansPerTile)
        {
            int t1 = Mathf.Min(t0 + SpansPerTile, route.Count - 1);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int sign = -1; sign <= 1; sign += 2)
            for (int i = t0; i < t1; i++)
            {
                float pa = WallPresence(route.Frac(i), route.Distance[i]);
                float pb = WallPresence(route.Frac(i + 1), route.Distance[i + 1]);
                if (pa <= 0f || pb <= 0f) continue;

                Foot(route, i, sign * Inset, out var f0, out var n0);
                Foot(route, i + 1, sign * Inset, out var f1, out var n1);
                float h0 = Coping(route.Distance[i]) * pa;
                float h1 = Coping(route.Distance[i + 1]) * pb;

                var o0 = n0 * (Thick * 0.5f);
                var o1 = n1 * (Thick * 0.5f);
                var up0 = Vector3.up * h0;
                var up1 = Vector3.up * h1;

                // Outer face, inner face, coping. Feet are sunk 12 cm so the wall never shows a
                // gap where the ground rolls between two stations.
                var sink = Vector3.up * 0.12f;
                Band(verts, uvs, tris, f0 - o0 - sink, f1 - o1 - sink, f0 - o0 + up0, f1 - o1 + up1, route.Distance[i]);
                Band(verts, uvs, tris, f0 + o0 - sink, f1 + o1 - sink, f0 + o0 + up0, f1 + o1 + up1, route.Distance[i]);
                Band(verts, uvs, tris, f0 - o0 + up0, f1 - o1 + up1, f0 + o0 + up0, f1 + o1 + up1, route.Distance[i]);
            }

            if (tris.Count == 0) continue;
            AddMesh(group, $"Wall {t0}",
                    Finish($"Taka_Wall_{t0}", verts.ToArray(), uvs.ToArray(), tris), mat,
                    collider: false);
        }
    }

    /// <summary>Wandering coping height, metres. A level wall top is the biggest tell there is.</summary>
    private static float Coping(float distance) =>
        // Lower and far more even than a field wall: this is a poured-and-faced roadworks
        // parapet at bar height, not something a shepherd stacked. 0.78 m puts the coping just
        // under a seated rider's hip, which is what makes the exposure read.
        0.78f + (Mathf.PerlinNoise(distance * 0.075f, 3.1f) - 0.5f) * 0.10f
              + (Mathf.PerlinNoise(distance * 0.011f, 8.4f) - 0.5f) * 0.12f;

    /// <summary>
    /// Where a roadside object's foot sits: on the GROUND at that offset (never on the road
    /// plane), with the horizontal normal pointing away from the centreline.
    /// </summary>
    private static void Foot(TakaRoute r, int i, float offset, out Vector3 foot, out Vector3 outward)
    {
        var p = r.Position[i];
        var s = r.SideFlat(i);
        float x = p.x + s.x * offset, z = p.z + s.z * offset;
        foot = new Vector3(x, Height(x, z), z);
        outward = new Vector3(s.x, 0f, s.z) * Mathf.Sign(offset);
    }

    // =================================================================== hero props

    /// <summary>
    /// The route's six named checkpoints, in metres, mirrored from tools/blender/taka_route.py.
    /// Duplicated here rather than read back out of the JSON because these are ANCHORS FOR ART:
    /// the gate, the hut and the col marker all have to land on the checkpoint the HUD announces,
    /// and a silent drift between the two lists would put the region's hero prop 200 m from the
    /// sign that names it.
    /// </summary>
    /// The identifier names are inherited from the highlands pass this file was grown from and
    /// are kept deliberately: every prop builder below is keyed to them, and renaming six
    /// constants across a 2,400-line file to gain nothing but nicer spelling is exactly the kind
    /// of churn that silently moves a hero prop. Read them through this map instead:
    ///
    ///     CpMeadowGate   ->  0.00  "Trailhead Galleries"    1,400 m
    ///     CpSwitchbacks  ->  0.20  "Avalanche Gallery"      1,729 m
    ///     CpFalseFlat    ->  0.40  "The Stacked Hairpins"   2,205 m
    ///     CpWindwardRamp ->  0.55  "North Wall"             2,570 m
    ///     CpCol          ->  0.70  "Kori Lake Summit"       2,842 m
    ///     CpDescent      ->  0.88  "Ice-Blue Descent"       2,507 m
    private const float CpMeadowGate = 0f;
    private const float CpSwitchbacks = 4400f;
    private const float CpFalseFlat = 8800f;
    private const float CpWindwardRamp = 12100f;
    private const float CpCol = 15400f;
    private const float CpDescent = 19360f;

    /// <summary>
    /// AVALANCHE GALLERIES AND THE ROCK TUNNEL - the region's signature built structures, and
    /// the thing that says "high alpine pass" faster than any amount of snow does.
    ///
    /// A gallery is a concrete shed built OVER the carriageway on the exposed side of a slope,
    /// so a slide passes over the road instead of across it: a roof slab, a row of stout pillars
    /// on the valley side, and the mountain itself as the back wall. Riding under one is a
    /// three-second flicker of shadow and daylight through the pillar gaps, which is the most
    /// interesting thing that happens to the light anywhere on this route.
    ///
    /// THE INVARIANT THAT MAKES OR BREAKS THIS: every surface the rider passes INSIDE must be
    /// double-sided. The roof slab and the back wall are authored from outside the volume, so
    /// with default back-face culling the interior is simply not drawn - the rider enters the
    /// gallery and the roof vanishes, leaving pillars holding up nothing and open sky above.
    /// The Sakura tunnel shipped exactly that bug once. <see cref="InteriorMaterial"/> sets
    /// _Cull = 0 and is used for every piece of this group for that reason.
    ///
    /// Placements are PROVISIONAL, chosen so one gallery brackets the Avalanche Gallery
    /// checkpoint it is named for and the others break up the long ramps.
    /// </summary>
    private static void BuildGatesAndGrids(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Galleries").transform;
        group.SetParent(root, false);

        var cv = new List<Vector3>(); var cuv = new List<Vector2>(); var ctri = new List<int>();

        // (start metre, length metre). The checkpoint gallery is the long one.
        (float at, float len)[] galleries =
        {
            // 2026-09-26 claude-taka2: was CpMeadowGate + 620 m, which stood the pillars inside
            // the Provencal start village (route 40-760 m). Moved clear of it (see the skip below).
            (CpMeadowGate + 900f, 90f),
            (CpSwitchbacks - 55f, 150f),
            (6900f, 70f),
            (CpFalseFlat + 1450f, 80f),
            (CpWindwardRamp + 900f, 110f),
            (CpDescent + 780f, 95f),
        };

        const float Clear = 4.6f;        // headroom under the slab - a loaded snowplough fits
        const float Slab = 0.85f;        // roof thickness
        const float PillarStep = 7.0f;   // bay spacing; also the shadow-flicker frequency
        float half = RoadHalfWidth + ShoulderWidth + 0.55f;

        int bays = 0;
        foreach (var (at, len) in galleries)
        {
            // Which side is the drop? Measure, never assume - the route hairpins, so the valley
            // swaps sides repeatedly and a hard-coded sign builds half the galleries into the
            // mountain.
            int mid = route.IndexAt(at + len * 0.5f);
            Foot(route, mid, +55f, out var probeR, out _);
            Foot(route, mid, -55f, out var probeL, out _);
            int openSide = probeR.y < probeL.y ? +1 : -1;   // pillars go on the low side
            int backSide = -openSide;                       // back wall against the mountain

            // Never build a gallery over the start village (TakaMountains.Ventoux.cs band).
            if (at < VillageToM + 60f && at + len > VillageFromM) continue;
            int i0 = route.IndexAt(at), i1 = route.IndexAt(at + len);
            if (i1 <= i0 + 2) continue;

            // ---- roof slab: one long ribbon, both faces, following the road's bank ----------
            for (int i = i0; i < i1; i++)
            {
                var a = route.Position[i]; var b = route.Position[i + 1];
                var sa = route.SideFlat(i); var sb = route.SideFlat(i + 1);
                float ya = Height(a.x, a.z) + Clear, yb = Height(b.x, b.z) + Clear;
                var a0 = new Vector3(a.x + sa.x * half * backSide, ya, a.z + sa.z * half * backSide);
                var a1 = new Vector3(a.x + sa.x * half * openSide, ya, a.z + sa.z * half * openSide);
                var b0 = new Vector3(b.x + sb.x * half * backSide, yb, b.z + sb.z * half * backSide);
                var b1 = new Vector3(b.x + sb.x * half * openSide, yb, b.z + sb.z * half * openSide);
                // Soffit (what the rider sees) and the top of the slab.
                Band(cv, cuv, ctri, a0, b0, a1, b1, route.Distance[i]);
                Band(cv, cuv, ctri, a0 + Vector3.up * Slab, b0 + Vector3.up * Slab,
                                    a1 + Vector3.up * Slab, b1 + Vector3.up * Slab, route.Distance[i]);
                // Outer fascia, so the slab has thickness from the road below.
                Band(cv, cuv, ctri, a1, b1, a1 + Vector3.up * Slab, b1 + Vector3.up * Slab,
                     route.Distance[i]);

                // ---- back wall against the mountain -----------------------------------------
                Band(cv, cuv, ctri,
                     new Vector3(a0.x, Height(a0.x, a0.z) - 0.4f, a0.z),
                     new Vector3(b0.x, Height(b0.x, b0.z) - 0.4f, b0.z),
                     a0 + Vector3.up * Slab, b0 + Vector3.up * Slab, route.Distance[i]);
            }

            // ---- pillars on the open side ---------------------------------------------------
            for (float d = at + PillarStep * 0.5f; d < at + len; d += PillarStep)
            {
                int i = route.IndexAt(d);
                var pp = route.Position[i];
                var sp = route.SideFlat(i);
                var tp = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
                var foot = new Vector3(pp.x + sp.x * half * openSide, 0f, pp.z + sp.z * half * openSide);
                foot.y = Height(foot.x, foot.z) - 0.5f;
                float h = (Height(pp.x, pp.z) + Clear) - foot.y;
                Box(cv, cuv, ctri, null, foot,
                    new Vector3(sp.x, 0f, sp.z) * 0.42f, tp * 0.52f, h, Color.white);
                bays++;
            }
        }

        if (ctri.Count > 0)
            AddMesh(group, "Avalanche Galleries",
                    Finish("Taka_Galleries", cv.ToArray(), cuv.ToArray(), ctri),
                    InteriorMaterial(), collider: false);

        BuildSnowSheds(group, route);
        Debug.Log($"[taka] {galleries.Length} avalanche galleries, {bays} pillar bays.");
    }

    /// <summary>
    /// Snow-depth marker poles: the tall red-and-white banded rods planted beside an alpine road
    /// so the plough driver can still find the carriageway edge under two metres of snow.
    ///
    /// They are here for a reason beyond authenticity. Taka has no vegetation by design, which
    /// removes every vertical the eye normally uses to read distance and speed along a road. A
    /// regular file of 3.2 m poles restores that parallax cue completely, and its banding gives
    /// the cel shader something high-contrast to work with against snow.
    /// </summary>
    private static void BuildSnowSheds(Transform group, TakaRoute route)
    {
        // TWO MESHES, NOT VERTEX COLOURS. Box()/Post() collect a per-vertex tint list, but
        // Finish() does not consume it - the highlands pass gathered those colours and threw
        // them away, which is why its gate rails all came out one flat stain. Splitting the red
        // and white bands into two meshes with two materials is the honest way to get banding
        // out of this toolchain, and costs one extra renderer for the whole region.
        var rv = new List<Vector3>(); var ruv = new List<Vector2>(); var rtri = new List<int>();
        var wv = new List<Vector3>(); var wuv = new List<Vector2>(); var wtri = new List<int>();

        int poles = 0;
        // 32 m spacing, both sides, the whole route. Dense on purpose - see the summary.
        for (float d = 40f; d < route.Length - 40f; d += 32f)
        {
            int i = route.IndexAt(d);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Foot(route, i, sign * (RoadHalfWidth + ShoulderWidth + 0.55f), out var f, out _);
                // A slight consistent downwind lean, as on the highlands posts.
                var tip = f + Vector3.up * 3.2f + WindDir * 0.14f;
                // Four bands, alternating, drawn as four stacked posts rather than a texture so
                // the banding survives at any distance and needs no second material.
                for (int b = 0; b < 4; b++)
                {
                    var a0 = Vector3.Lerp(f, tip, b / 4f);
                    var a1 = Vector3.Lerp(f, tip, (b + 1) / 4f);
                    if (b % 2 == 0) Post(rv, ruv, rtri, null, a0, a1, 0.042f, Color.white);
                    else            Post(wv, wuv, wtri, null, a0, a1, 0.042f, Color.white);
                }
                poles++;
            }
        }

        if (rtri.Count > 0)
            AddMesh(group, "Snow Poles Red",
                    Finish("Taka_SnowPoles_R", rv.ToArray(), ruv.ToArray(), rtri),
                    CelMaterial("Taka_PoleRed", new Color(0.74f, 0.20f, 0.17f), gloss: 0.30f,
                                spec: 0.22f, rim: 0.34f), collider: false);
        if (wtri.Count > 0)
            AddMesh(group, "Snow Poles White",
                    Finish("Taka_SnowPoles_W", wv.ToArray(), wuv.ToArray(), wtri),
                    CelMaterial("Taka_PoleWhite", new Color(0.90f, 0.91f, 0.92f), gloss: 0.30f,
                                spec: 0.22f, rim: 0.34f), collider: false);
        Debug.Log($"[taka] {poles} snow-depth marker poles.");
    }

    /// <summary>
    /// Shepherd's cairns and wind-frayed marker posts - the two things that edge an upland road
    /// once the walls stop. The posts lean AWAY from <see cref="WindDir"/>, all of them, by the
    /// same sign: a field of posts leaning consistently is the cheapest and most convincing way
    /// to put wind into a still image, and it is the reason the wind is one shared constant.
    /// </summary>
    private static void BuildCairnsAndPosts(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Cairns and Posts").transform;
        group.SetParent(root, false);

        var sv = new List<Vector3>(); var suv = new List<Vector2>(); var stri = new List<int>();
        var pv = new List<Vector3>(); var puv = new List<Vector2>(); var ptri = new List<int>();
        var pcol = new List<Color>();

        // ---- marker posts, above the wall line ------------------------------------------
        // 60 m spacing, alternating sides, from where the walls thin out to the finish.
        int posts = 0;
        for (float d = 0.56f * route.Length; d < route.Length - 40f; d += 60f)
        {
            int i = route.IndexAt(d);
            int sign = (posts % 2 == 0) ? 1 : -1;
            Foot(route, i, sign * (RoadHalfWidth + ShoulderWidth + 0.8f), out var f, out _);
            // Lean: 7 degrees off vertical, downwind, plus a little scatter.
            float lean = 0.12f + Mathf.PerlinNoise(d * 0.03f, 5.5f) * 0.06f;
            var tip = f + Vector3.up * 1.25f + WindDir * lean * 1.25f;
            Post(pv, puv, ptri, pcol, f, tip, 0.055f, new Color(0.47f, 0.42f, 0.34f));
            // Weathered white cap - the reflective band every upland marker post carries.
            Post(pv, puv, ptri, pcol, Vector3.Lerp(f, tip, 0.80f), tip, 0.058f,
                 new Color(0.90f, 0.89f, 0.84f));
            posts++;
        }

        // ---- cairns ----------------------------------------------------------------------
        // On the high ground only, set well back so they read as landmarks rather than bollards.
        float[] cairnAt = { 13200f, 15600f, 17100f, 18600f, CpCol - 420f, 20900f, 22100f };
        int c = 0;
        foreach (float d in cairnAt)
        {
            int i = route.IndexAt(d);
            int sign = (c % 2 == 0) ? 1 : -1;
            float off = sign * Mathf.Lerp(26f, 48f, Mathf.PerlinNoise(d * 0.004f, 2.2f));
            Foot(route, i, off, out var f, out _);
            float cairnH = 1.55f + Mathf.PerlinNoise(d * 0.02f, 9f) * 0.75f;
            if (!Kit2Cairn(f, cairnH)) Cairn(sv, suv, stri, f, cairnH);
            c++;
        }

        if (stri.Count > 0)
            AddMesh(group, "Cairns", Finish("Taka_Cairns", sv.ToArray(), suv.ToArray(), stri),
                    StoneMaterial(), collider: false);
        if (ptri.Count > 0)
            AddMesh(group, "Marker Posts",
                    Finish("Taka_Posts", pv.ToArray(), puv.ToArray(), ptri),
                    TimberMaterial(), collider: false);

        Debug.Log($"[taka] {posts} marker posts, {cairnAt.Length} cairns.");
    }

    /// <summary>A tapering stack of rough stone - eight courses, each smaller and rotated.</summary>
    private static void Cairn(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3 foot, float height)
    {
        const int Courses = 8;
        for (int k = 0; k < Courses; k++)
        {
            float t = k / (float)(Courses - 1);
            float r = Mathf.Lerp(0.62f, 0.10f, t);
            float y = t * height;
            float a = k * 0.9f + foot.x * 0.01f;
            var right = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            var back = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * r * 0.85f;
            Box(v, uv, tri, null, foot + Vector3.up * y, right, back,
                height / Courses * 1.25f, Color.white);
        }
    }

    /// <summary>A tapered square post from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void Post(List<Vector3> v, List<Vector2> uv, List<int> tri, List<Color> col,
                             Vector3 a, Vector3 b, float r, Color tint)
    {
        var dir = (b - a).normalized;
        var right = Vector3.Cross(dir, Vector3.up);
        if (right.sqrMagnitude < 1e-5f) right = Vector3.right;
        right = right.normalized * r;
        var back = Vector3.Cross(dir, right).normalized * r;

        int bse = v.Count;
        var lo = new[] { a - right - back, a + right - back, a + right + back, a - right + back };
        var hi = new[] { b - right - back, b + right - back, b + right + back, b - right + back };
        for (int f = 0; f < 4; f++)
        {
            int n = (f + 1) & 3;
            int k = v.Count;
            v.Add(lo[f]); v.Add(lo[n]); v.Add(hi[n]); v.Add(hi[f]);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(r * 2f, 0));
            uv.Add(new Vector2(r * 2f, (b - a).magnitude)); uv.Add(new Vector2(0, (b - a).magnitude));
            for (int q = 0; q < 4; q++) col?.Add(tint);
            tri.Add(k); tri.Add(k + 2); tri.Add(k + 1);
            tri.Add(k); tri.Add(k + 3); tri.Add(k + 2);
        }
        int top = v.Count;
        for (int q = 0; q < 4; q++) { v.Add(hi[q]); uv.Add(new Vector2(q & 1, (q >> 1) & 1)); col?.Add(tint); }
        tri.Add(top); tri.Add(top + 2); tri.Add(top + 1);
        tri.Add(top); tri.Add(top + 3); tri.Add(top + 2);
        _ = bse;
    }

    /// <summary>
    /// THE COL VIEW-MARKER: the stone slab at 1,980 m that the entire region is built to deliver
    /// you to. It sits on the outside of the road at the col checkpoint, facing back down the
    /// climb, so the moment the rider crests they are looking at the marker AND past it at the
    /// 7 km of switchbacks they just rode, 1,080 m below. That framing is the region's payoff,
    /// and it is the reason the col marker is placed by hand rather than scattered.
    /// </summary>
    private static void BuildColMarker(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Col Marker").transform;
        group.SetParent(root, false);

        int i = route.IndexAt(CpCol);
        var s = route.SideFlat(i);
        var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;

        // Outboard side = the side the view is on. The route descends northward off the col, so
        // the drop is on the side whose ground is LOWER 60 m out; measure it rather than guess.
        Foot(route, i, +60f, out var probeR, out _);
        Foot(route, i, -60f, out var probeL, out _);
        int sign = probeR.y < probeL.y ? +1 : -1;

        Foot(route, i, sign * 7.5f, out var f, out var outward);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        // Plinth, slab and a low apron of set stones - a summit marker, not a milestone.
        Box(v, uv, tri, null, f - Vector3.up * 0.1f, t * 1.30f, outward * 0.55f, 0.42f, Color.white);
        Box(v, uv, tri, null, f + Vector3.up * 0.30f, t * 0.95f, outward * 0.20f, 1.70f, Color.white);
        for (int k = 0; k < 9; k++)
        {
            float a = k / 9f * Mathf.PI * 2f;
            var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.9f;
            var g = f + o;
            g.y = Height(g.x, g.z);
            Box(v, uv, tri, null, g, t * 0.34f, outward * 0.28f, 0.30f, Color.white);
        }
        AddMesh(group, "Col View Marker",
                Finish("Taka_ColMarker", v.ToArray(), uv.ToArray(), tri),
                StoneMaterial(), collider: false);

        // A brass-ish plate on the face, warm enough to catch the key and read from the road.
        var pv = new List<Vector3>(); var puv = new List<Vector2>(); var ptri = new List<int>();
        Quad(pv, puv, ptri, f + Vector3.up * 1.32f + outward * 0.21f, t * 0.62f, Vector3.up * 0.34f);
        AddMesh(group, "Col Plate", Finish("Taka_ColPlate", pv.ToArray(), puv.ToArray(), ptri),
                CelMaterial("Taka_Brass", new Color(0.86f, 0.70f, 0.36f), gloss: 0.62f,
                            spec: 0.45f, rim: 0.40f),
                collider: false);
    }

    /// <summary>
    /// The summit tarn. A still upland lake just below the col, set back from the road so it
    /// arrives as a reward for looking around rather than as a puddle beside the kerb.
    ///
    /// It is authored as a flat disc at a SOLVED water level: the terrain is sampled across the
    /// footprint first and the surface is placed a little below the minimum, so the tarn always
    /// sits IN a hollow instead of hovering over a slope. That solve is why it is a hand-placed
    /// prop and not a scatter.
    /// </summary>
    private static void BuildTarn(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Tarn").transform;
        group.SetParent(root, false);

        int i = route.IndexAt(CpCol - 1250f);
        Foot(route, i, -155f, out var centre, out _);
        // Use the SOLVED plan centre, not the foot's, so the disc sits in the hollow that
        // Height() actually carved. (Foot resolves y through Height, which is exactly what we
        // want for the bed reading below.)
        centre.x = _tarnCentre.x; centre.z = _tarnCentre.y;

        // Fill the carved basin to a fraction of its depth. The first build solved the level as
        // "minimum terrain over the disc, minus 0.9 m", which on a relief-heavy slope put the
        // water plane BELOW the deepest ground in frame and rendered an invisible lake. Filling
        // a bowl of known depth cannot fail that way.
        const float R = 150f;
        const int Ring = 34;
        float level = Height(centre.x, centre.z) + TarnFillM;

        var v = new List<Vector3> { new Vector3(centre.x, level, centre.z) };
        var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        var tri = new List<int>();
        for (int k = 0; k <= Ring; k++)
        {
            float a = k / (float)Ring * Mathf.PI * 2f;
            // Irregular shoreline - a perfect circle of water in a mountain reads as a crater.
            float r = R * (0.72f + Mathf.PerlinNoise(Mathf.Cos(a) * 1.7f + 4f, Mathf.Sin(a) * 1.7f + 9f) * 0.55f);
            v.Add(new Vector3(centre.x + Mathf.Cos(a) * r, level, centre.z + Mathf.Sin(a) * r));
            uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            if (k > 0) { tri.Add(0); tri.Add(k); tri.Add(k + 1); }
        }
        AddMesh(group, "Summit Tarn", Finish("Taka_Tarn", v.ToArray(), uv.ToArray(), tri),
                WaterMaterial(), collider: false);
        Debug.Log($"[taka] tarn at ({centre.x:0},{centre.z:0}) level {level:0.0} m.");
    }

    /// <summary>
    /// The timber alpine hut on the pasture false-flat, with its chimney smoke. It is the only
    /// building in the region and it is doing a specific job: on a 24 km road with no settlement,
    /// ONE human structure at the halfway point is what stops the fell reading as uninhabited
    /// terrain-generator output, and its smoke is the only vertical motion in the whole scene.
    /// </summary>
    private static void BuildHut(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Refuge").transform;
        group.SetParent(root, false);

        int i = route.IndexAt(CpFalseFlat - 140f);
        Foot(route, i, -34f, out var f, out var outward);
        var along = Vector3.Cross(Vector3.up, outward).normalized;

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        var cv = new List<Vector3>(); var cuv = new List<Vector2>(); var ctri = new List<int>();
        var rv = new List<Vector3>(); var ruv = new List<Vector2>(); var rtri = new List<int>();

        const float W = 4.2f, D = 3.1f, Wall = 2.35f;
        // Stone footing, timber walls, dark slate roof: the local vernacular, and it means the
        // hut shares the wall material so it reads as belonging to the same valley.
        Box(cv, cuv, ctri, null, f - Vector3.up * 0.15f, along * (W * 0.55f), outward * (D * 0.55f), 0.65f, Color.white);
        Box(v, uv, tri, null, f + Vector3.up * 0.45f, along * (W * 0.5f), outward * (D * 0.5f), Wall, Color.white);

        // Gabled roof: two pitched quads plus the gable triangles, overhanging on all sides.
        float eave = f.y + 0.45f + Wall;
        float ridge = eave + 1.25f;
        var a0 = f + along * (W * 0.62f) + outward * (D * 0.62f); a0.y = eave;
        var a1 = f - along * (W * 0.62f) + outward * (D * 0.62f); a1.y = eave;
        var b0 = f + along * (W * 0.62f) - outward * (D * 0.62f); b0.y = eave;
        var b1 = f - along * (W * 0.62f) - outward * (D * 0.62f); b1.y = eave;
        var r0 = f + along * (W * 0.62f); r0.y = ridge;
        var r1 = f - along * (W * 0.62f); r1.y = ridge;
        Band(rv, ruv, rtri, a0, a1, r0, r1, 0f);
        Band(rv, ruv, rtri, b0, b1, r0, r1, 0f);

        // Chimney, on the ridge, with smoke streaming downwind.
        var chim = f + along * (W * 0.30f); chim.y = eave + 0.45f;
        Box(cv, cuv, ctri, null, chim, along * 0.22f, outward * 0.22f, 1.15f, Color.white);

        AddMesh(group, "Hut Walls", Finish("Taka_HutWalls", v.ToArray(), uv.ToArray(), tri),
                TimberMaterial(), collider: false);
        AddMesh(group, "Hut Stone", Finish("Taka_HutStone", cv.ToArray(), cuv.ToArray(), ctri),
                StoneMaterial(), collider: false);
        AddMesh(group, "Hut Roof", Finish("Taka_HutRoof", rv.ToArray(), ruv.ToArray(), rtri),
                CelMaterial("Taka_Slate", new Color(0.30f, 0.32f, 0.34f), gloss: 0.30f,
                            spec: 0.20f, rim: 0.16f, shade: GroundShade),
                collider: false);

        BuildSmoke(group, chim + Vector3.up * 1.2f);
    }

    /// <summary>
    /// Chimney smoke. Deliberately thin and slow: a fat plume on a 4 m hut reads as a fire, and
    /// the whole point is a single quiet vertical thread in an otherwise horizontal landscape.
    /// Streams along <see cref="WindDir"/> like everything else in the region.
    /// </summary>
    private static void BuildSmoke(Transform parent, Vector3 at)
    {
        var go = new GameObject("Chimney Smoke");
        go.transform.SetParent(parent, false);
        go.transform.position = at;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 7.5f;
        main.startSpeed = 0.75f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.16f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;
        main.gravityModifier = -0.02f;
        var em = ps.emission; em.rateOverTime = 5f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.16f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(WindDir.x * 1.9f);
        vel.z = new ParticleSystem.MinMaxCurve(WindDir.z * 1.9f);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f),
                                new GradientAlphaKey(0f, 1f) },
        });

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = SoftParticleMaterial("Taka_Smoke", null);
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// Grazing sheep - the design's one piece of visible life, and the region's scale reference.
    /// Nothing else in Taka has a known size: a fell has no windows, no doors and no street
    /// furniture, so without sheep a 40 m hillside and a 400 m hillside look identical. They are
    /// clustered, never evenly spread, because evenly-spread livestock reads as a decal pattern.
    /// </summary>
    private static void BuildSheep(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Sheep").transform;
        group.SetParent(root, false);

        var wv = new List<Vector3>(); var wuv = new List<Vector2>(); var wtri = new List<int>();
        var hv = new List<Vector3>(); var huv = new List<Vector2>(); var htri = new List<int>();
        int count = 0;

        // Flocks live on the enclosed and false-flat ground only: nothing grazes the scree above
        // the windward ramp, and a sheep standing at 1,950 m in frame would be a bug report.
        for (float d = 900f; d < 0.60f * route.Length; d += 640f)
        {
            if (Mathf.PerlinNoise(d * 0.002f, 41.3f) < 0.42f) continue;
            int i = route.IndexAt(d);
            int sign = (Mathf.PerlinNoise(d * 0.007f, 6.1f) > 0.5f) ? 1 : -1;
            float baseOff = sign * Mathf.Lerp(16f, 62f, Mathf.PerlinNoise(d * 0.003f, 14f));
            int flock = 4 + Mathf.RoundToInt(Mathf.PerlinNoise(d * 0.009f, 22f) * 7f);

            for (int k = 0; k < flock; k++)
            {
                float jx = (Mathf.PerlinNoise(d * 0.05f + k * 3.7f, 1.1f) - 0.5f) * 44f;
                float jz = (Mathf.PerlinNoise(d * 0.05f + k * 3.7f, 7.9f) - 0.5f) * 44f;
                Foot(route, i, baseOff + jx, out var f, out var outward);
                var along = Vector3.Cross(Vector3.up, outward).normalized;
                var at = f + along * jz;
                at.y = Height(at.x, at.z);

                float yaw = Mathf.PerlinNoise(at.x * 0.1f, at.z * 0.1f) * Mathf.PI * 2f;
                var fwd = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var rgt = Vector3.Cross(Vector3.up, fwd);

                // Body, head and four legs. ~0.75 m long: a real sheep, which is the whole point.
                Box(wv, wuv, wtri, null, at + Vector3.up * 0.30f, fwd * 0.36f, rgt * 0.17f, 0.34f, Color.white);
                Box(hv, huv, htri, null, at + fwd * 0.40f + Vector3.up * 0.42f,
                    fwd * 0.11f, rgt * 0.08f, 0.17f, Color.white);
                for (int lx = -1; lx <= 1; lx += 2)
                for (int lz = -1; lz <= 1; lz += 2)
                    Box(hv, huv, htri, null, at + fwd * (0.22f * lz) + rgt * (0.12f * lx),
                        fwd * 0.035f, rgt * 0.035f, 0.30f, Color.white);
                count++;
            }
        }

        if (wtri.Count == 0) return;
        AddMesh(group, "Sheep Fleece", Finish("Taka_Fleece", wv.ToArray(), wuv.ToArray(), wtri),
                CelMaterial("Taka_Fleece", new Color(0.88f, 0.86f, 0.80f), gloss: 0.06f,
                            spec: 0.05f, rim: 0.34f, shade: GroundShade),
                collider: false);
        AddMesh(group, "Sheep Faces", Finish("Taka_SheepDark", hv.ToArray(), huv.ToArray(), htri),
                CelMaterial("Taka_SheepDark", new Color(0.24f, 0.22f, 0.21f), gloss: 0.10f,
                            spec: 0.08f, rim: 0.22f, shade: GroundShade),
                collider: false);
        Debug.Log($"[taka] {count} sheep in {group.childCount} meshes.");
    }

    // =================================================================== flora

    /// <summary>
    /// Verge flora: grass tussocks, purple wildflower clumps, thistles and rusty bracken, as
    /// crossed billboards off the 2x2 Taka_Lichen_Sprite atlas.
    ///
    /// HIGHLANDS HAVE (ALMOST) NO TREES, and resisting the urge to plant some is the single
    /// biggest art decision in this region. Sakura is defined by its cherry canopy and Maple
    /// City by its street maples, so the reflex is to give Taka an equivalent - but a treeline
    /// is exactly what an exposed 1,980 m pass does NOT have, and the emptiness above it is the
    /// point. What replaces the canopy is DENSITY OF GROUND COVER and its change with altitude:
    /// lush tussock and flowers in the meadows, thinning to wind-scoured bracken and bare stone
    /// on the tops. So this scatter thins as the road climbs, and stops almost entirely above
    /// the windward ramp.
    ///
    /// Every tuft resolves its foot through <see cref="Height"/>, which is why none of them
    /// floats and none sinks even where the corridor cuts into the hillside.
    /// </summary>
    private static void BuildFloraScatter(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Flora").transform;
        group.SetParent(root, false);

        var mat = TuftMaterial();
        const float StepM = 7f;              // along-route spacing of scatter attempts
        const int TuftsPerAttempt = 3;
        const float TilesEveryM = 1400f;     // one renderer per ~1.4 km, so frustum culling works

        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        int tile = 0, placed = 0;
        float nextTile = TilesEveryM;

        for (float d = 20f; d < route.Length - 20f; d += StepM)
        {
            if (d >= nextTile)
            {
                FlushTuftTile(group, mat, verts, uvs, tris, tile++);
                verts.Clear(); uvs.Clear(); tris.Clear();
                nextTile += TilesEveryM;
            }

            int i = route.IndexAt(d);
            float y = route.Position[i].y;
            // Altitude thinning. 1.0 in the meadows, ~0.18 by the windward ramp, ~0 on the tops.
            float lush = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1180f, 1820f, y));
            lush = Mathf.Max(lush, 0.06f);

            for (int k = 0; k < TuftsPerAttempt; k++)
            {
                float r1 = Mathf.PerlinNoise(d * 0.37f + k * 11.3f, 2.1f);
                if (r1 > lush) continue;
                float r2 = Mathf.PerlinNoise(d * 0.29f + k * 5.7f, 8.8f);
                float r3 = Mathf.PerlinNoise(d * 0.53f + k * 3.1f, 15.2f);
                float r4 = Mathf.PerlinNoise(d * 0.71f + k * 7.9f, 21.7f);

                int sign = r2 > 0.5f ? 1 : -1;
                // Dense at the verge, sparse out to the corridor edge - exactly the falloff a
                // real roadside has, because the verge is where the water runs off.
                float off = sign * Mathf.Lerp(4.6f, 62f, r3 * r3);
                Foot(route, i, off, out var f, out _);
                if (InProvence(f, 0.5f)) continue;   // not inside the lavender rows / olive groves

                // Variant choice from the 2x2 atlas. Wildflowers and bracken are altitude-keyed:
                // flowers belong to the meadows, bracken to the scoured upper slopes.
                int cell;
                if (r4 < 0.44f) cell = 0;                       // grass tussock, everywhere
                else if (r4 < 0.72f) cell = lush > 0.55f ? 1 : 3; // flowers low / bracken high
                else if (r4 < 0.88f) cell = 2;                   // thistle
                else cell = 3;                                   // bracken

                float scale = Mathf.Lerp(0.55f, 1.15f, r1 / Mathf.Max(lush, 0.001f))
                            * Mathf.Lerp(0.72f, 1f, lush);
                float yaw = r2 * Mathf.PI * 2f + r3 * 1.7f;
                TuftBillboard(verts, uvs, tris, f, scale, yaw, cell);
                placed++;
            }
        }
        FlushTuftTile(group, mat, verts, uvs, tris, tile++);

        Debug.Log($"[taka] flora: {placed} tufts across {tile} tiles.");
    }

    private static void FlushTuftTile(Transform group, Material mat, List<Vector3> v,
                                      List<Vector2> uv, List<int> tri, int index)
    {
        if (tri.Count == 0) return;
        var go = AddMesh(group, $"Tufts {index}",
                         Finish($"Taka_Tufts_{index}", v.ToArray(), uv.ToArray(), tri), mat,
                         collider: false);
        // Ground cover casting full shadow maps over 24 km is pure cost for no read at this
        // scale; the terrain's own shading already carries the form.
        go.GetComponent<MeshRenderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>
    /// Two crossed quads carrying one cell of the 2x2 tuft atlas.
    ///
    /// The UV rect is INSET by half a texel's worth of margin. Without it, bilinear filtering and
    /// the mip chain pull neighbouring cells across the seam and every grass tussock grows a
    /// purple fringe from the wildflower cell next door - a failure that only becomes visible at
    /// distance, once the mips kick in, which is exactly when nobody is looking for it.
    /// </summary>
    private static void TuftBillboard(List<Vector3> v, List<Vector2> uv, List<int> tri,
                                      Vector3 foot, float scale, float yaw, int cell)
    {
        const float Inset = 0.012f;
        float u0 = (cell & 1) * 0.5f + Inset, u1 = (cell & 1) * 0.5f + 0.5f - Inset;
        float v0 = (cell >> 1) * 0.5f + Inset, v1 = (cell >> 1) * 0.5f + 0.5f - Inset;

        float w = 0.62f * scale, h = 0.74f * scale;
        for (int k = 0; k < 2; k++)
        {
            float a = yaw + k * Mathf.PI * 0.5f;
            var right = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * w;
            // Sunk 4 cm so the billboard's lower edge is never a visible floating hairline on a
            // slope steeper than the quad's own footprint.
            var lo = foot - Vector3.up * 0.04f;
            int b = v.Count;
            v.Add(lo - right); v.Add(lo + right);
            v.Add(lo - right + Vector3.up * h); v.Add(lo + right + Vector3.up * h);
            uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u1, v0));
            uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u1, v1));
            tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
            tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
            tri.Add(b); tri.Add(b + 1); tri.Add(b + 2);
            tri.Add(b + 1); tri.Add(b + 3); tri.Add(b + 2);
        }
    }

    // =================================================================== horizon

    /// <summary>
    /// Layered distant ranges - the region's depth cue, and the thing the col actually looks at.
    ///
    /// Taka deliberately runs the LOWEST fog density in the game (RegionDirector.TakaAmbience:
    /// 0.00009 against Maple City's 0.00055) because its whole premise is that you can see. That
    /// removes the cheap depth cue the other three regions lean on, so the depth has to come from
    /// somewhere real: three rings of ridge silhouette at 5.5, 9 and 14 km, each one paler,
    /// bluer and lower-contrast than the last. That IS aerial perspective, modelled explicitly
    /// rather than hoped for from a fog term - which also means it survives even if the
    /// post-process aerial stage is not running in a given capture path.
    ///
    /// They are unlit-ish flat colour on purpose. A distant range that responds to the key light
    /// picks up the same shading as the near hillside and immediately collapses into it; real
    /// distant ranges are a flat wash, and flat is what sells the kilometres.
    /// </summary>
    private static void BuildDistantRanges(Transform root, TakaRoute route)
    {
        var group = new GameObject("Taka Distant Ranges").transform;
        group.SetParent(root, false);

        var c = route.Plan.center;
        // Ring radius, ridge base height, ridge amplitude, colour, name.
        // DARKENED AFTER THE FIRST RENDER. These were authored at the value real distant ranges
        // read at against a bright sky - and came back as three near-white domes that vanished
        // into the haze band. Unlit/Color is unaffected by the key light, so the authored value
        // IS the screen value: aerial perspective has to be modelled by desaturating and lifting
        // TOWARD the horizon colour, not by starting there. PROVISIONAL.
        var layers = new[]
        {
            (r: 5500f, base_: 1180f, amp: 470f, tint: new Color(0.300f, 0.404f, 0.446f), n: "Near Range"),
            (r: 9000f, base_: 1280f, amp: 620f, tint: new Color(0.424f, 0.520f, 0.578f), n: "Mid Range"),
            (r: 14000f, base_: 1360f, amp: 780f, tint: new Color(0.556f, 0.630f, 0.688f), n: "Far Range"),
        };

        int li = 0;
        foreach (var L in layers)
        {
            const int Seg = 240;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float floor = 640f;               // well below anything the player can stand on

            for (int k = 0; k <= Seg; k++)
            {
                float a = k / (float)Seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                // Three octaves around the ring, phase-offset per layer so the three silhouettes
                // never line their peaks up (which instantly reads as one repeated cut-out).
                float s = k / (float)Seg * 8f + li * 37f;
                float n = (Mathf.PerlinNoise(s, 0.5f) - 0.5f) * 2f
                        + (Mathf.PerlinNoise(s * 2.7f, 3.5f) - 0.5f) * 0.9f
                        + (Mathf.PerlinNoise(s * 6.3f, 7.5f) - 0.5f) * 0.36f;
                float top = L.base_ + n * L.amp;

                var p = new Vector3(c.x + dir.x * L.r, 0f, c.z + dir.z * L.r);
                v.Add(new Vector3(p.x, floor, p.z));
                v.Add(new Vector3(p.x, top, p.z));
                uv.Add(new Vector2(k / (float)Seg * 24f, 0f));
                uv.Add(new Vector2(k / (float)Seg * 24f, 1f));
                if (k > 0)
                {
                    int b = (k - 1) * 2;
                    tri.Add(b); tri.Add(b + 1); tri.Add(b + 2);
                    tri.Add(b + 1); tri.Add(b + 3); tri.Add(b + 2);
                    // Wound both ways: the ring surrounds the player, so it is always seen from
                    // the inside, but a capture camera placed outside the ring must not see holes.
                    tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
                    tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
                }
            }

            var go = AddMesh(group, L.n, Finish($"Taka_Range_{li}", v.ToArray(), uv.ToArray(), tri),
                             RangeMaterial($"Taka_Range_{li}", L.tint, L.r), collider: false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            li++;
        }
    }

    // =================================================================== signature VFX

    /// <summary>
    /// THE SIGNATURE: wind-blown grass seed. Sakura has petals, Maple City has maple leaves,
    /// Taka has dandelion clocks, thistledown, straw seed heads and pale upland florets carried
    /// on the prevailing crosswind (design doc section 3).
    ///
    /// The three ways this is deliberately NOT a petal system:
    ///  * MOTION. Petals and leaves fall; seed FLIES. Gravity is near zero (0.006 against the
    ///    city's 0.055), the dominant velocity is horizontal along <see cref="WindDir"/>, and the
    ///    vertical band straddles zero so roughly a third of the seed is RISING on a thermal at
    ///    any moment. Nothing else in the game moves like that.
    ///  * FORM. Billboard, not a cupped mesh. A dandelion clock has no face to catch the light;
    ///    it is a soft radial puff and it should look the same from every angle.
    ///  * COLOUR. Cream, straw and gold, enforced upstream - tools/blender/taka_surfaces.py
    ///    asserts every variant has green >= blue, because a variant bluer than it is green
    ///    blooms PINK past the grade's bloom threshold and the region instantly reads as cherry
    ///    blossom. That assertion has already caught one: the doc's knapweed-purple wildflower,
    ///    which was moved to the static ground scatter where nothing blooms it.
    /// </summary>
    private static void BuildSeedVfx(Transform root, TakaRoute route)
    {
        var tex = TakaTexture("Taka_Spindrift_Sprite.png");

        var shader = Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply")
                     ?? Shader.Find("Sprites/Default");
        var mat = LoadOrCreate("Taka_Seed_VFX", shader.name);
        mat.SetTexture("_MainTex", tex);
        // _Mode is read ONLY by the inspector ShaderGUI; the blend state must be pushed
        // explicitly or every seed renders as an opaque cream square.
        if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 2f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_BlendOp")) mat.SetFloat("_BlendOp", (float)UnityEngine.Rendering.BlendOp.Add);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        if (mat.HasProperty("_LightingEnabled")) mat.SetFloat("_LightingEnabled", 0f);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetColor("_Color", Color.white);
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);

        var go = new GameObject("Taka Spindrift");
        go.transform.SetParent(root, false);
        go.transform.position = route.Position[0] + Vector3.up * 8f;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 14f;
        main.loop = true;
        // SPINDRIFT IS FAST AND SHORT-LIVED. Seed floats for fifteen seconds; wind-driven snow
        // rips past in three or four. Shortening the lifetime is also what keeps the particle
        // COUNT honest at a high emission rate - the population is rate x lifetime, and a
        // spindrift emitter tuned with seed lifetimes would hold 1,400 live particles and eat
        // the frame in overdraw for a region that is mostly pale sky and pale snow already.
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 5.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 8.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.17f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // WHITE. The four seed colours live in the SPRITE ATLAS; tinting here would flatten the
        // cream/straw/gold variety the texture was authored to provide.
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        // Spindrift DOES fall, just not much: it is real snow being carried, not thistledown.
        main.gravityModifier = 0.055f;
        main.maxParticles = 420;               // restrained: the emitter follows the camera
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 110f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        // Wide ACROSS the wind and shallow along it: seed should stream through frame, so the
        // box is a curtain the camera flies through rather than a cube it sits inside.
        shape.scale = new Vector3(70f, 18f, 70f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        // Three to four times the highlands' drift speed. This is the single number that
        // separates "snow falling" from "snow being BLOWN", and it is the whole identity of the
        // effect: Taka's signature must never be mistaken for Sakura's petals or Maple City's
        // leaves, both of which settle.
        vel.x = new ParticleSystem.MinMaxCurve(WindDir.x * 9f, WindDir.x * 16f);
        vel.z = new ParticleSystem.MinMaxCurve(WindDir.z * 9f, WindDir.z * 16f);
        // Mostly sinking, with gusts lifting off the snowfield. Unlike seed this does NOT
        // straddle zero evenly - spindrift is scoured up off a surface and settles again.
        vel.y = new ParticleSystem.MinMaxCurve(-1.4f, 0.55f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(2.4f);
        noise.frequency = 0.34f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(1.1f);
        noise.damping = true;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(1f, 0.80f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        // STRETCHED BILLBOARDS, not flat ones. The sprite atlas is already four elongated
        // streaks (tools/blender/taka_surfaces.py asserts they are not discs), and stretching
        // them along velocity as well means every particle points the way the wind is going
        // whatever angle the camera is at. A round, unstretched, white particle is a cherry
        // blossom; this cannot be mistaken for one.
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.055f;
        renderer.lengthScale = 2.6f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var follow = go.AddComponent<TakaSpindrift>();
        follow.height = 9f;
        follow.upwind = 34f;
        follow.windDir = WindDir;
        EditorUtility.SetDirty(follow);

        Debug.Log("[taka] spindrift VFX: camera-following stretched snow streaks, 4-variant " +
                  $"atlas, {main.maxParticles} max particles.");
    }

    // =================================================================== primitives

    /// <summary>
    /// A double-sided quad band between two cross-sections: (a0,b0) at one station and (a1,b1)
    /// at the next. The general ribbon primitive - wall faces, coping, roof pitches.
    /// UVs are metre-scale on both axes so a texture on a band never stretches.
    /// </summary>
    private static void Band(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, float distance)
    {
        int b = v.Count;
        float w = Vector3.Distance(a0, b0);
        float len = Vector3.Distance(a0, a1);

        // FRONT face: its own four vertices.
        v.Add(a0); v.Add(b0); v.Add(a1); v.Add(b1);
        uv.Add(new Vector2(0f, distance)); uv.Add(new Vector2(w, distance));
        uv.Add(new Vector2(0f, distance + len)); uv.Add(new Vector2(w, distance + len));
        tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
        tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);

        // BACK face: FOUR MORE vertices at the same positions, never the same four again.
        //
        // This duplication is the whole point of the function and cost a region to find. The
        // original version wound the back face over the SAME four vertices as the front, which
        // is the obvious way to make a ribbon two-sided and is silently fatal: Mesh.
        // RecalculateNormals averages the face normals of every triangle that shares a vertex,
        // and a front face and its exact mirror have opposite normals, so the average is the
        // ZERO VECTOR at every vertex on the ribbon. Unity stores that as (0,0,0), N.L is then
        // 0 everywhere, and the cel shader drops the entire surface into full shade colour - a
        // black-blue slab in bright sunlight. It affected everything built from Band(): the
        // dry-stone walls (which read as black plastic), the road, the lane markings and the
        // corridor apron, and NOTHING in any log or self test mentioned it, because the meshes
        // were structurally perfect. Only the render showed it.
        //
        // With separate vertices each side gets its own honest outward normal, the ribbon is
        // still two-sided, and the only cost is 4 extra vertices per span.
        int k = v.Count;
        v.Add(a0); v.Add(b0); v.Add(a1); v.Add(b1);
        uv.Add(new Vector2(0f, distance)); uv.Add(new Vector2(w, distance));
        uv.Add(new Vector2(0f, distance + len)); uv.Add(new Vector2(w, distance + len));
        tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
        tri.Add(k + 1); tri.Add(k + 3); tri.Add(k + 2);
    }

    private static void Quad(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 centre, Vector3 right, Vector3 up)
    {
        int b = v.Count;
        v.Add(centre - right - up); v.Add(centre + right - up);
        v.Add(centre - right + up); v.Add(centre + right + up);
        uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
        uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
        tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
        tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);

        // Second set of vertices for the back face - see the long note in Band(). Winding both
        // facings over ONE set of vertices makes RecalculateNormals average them to zero and
        // drops the quad into permanent cel shade.
        int k = v.Count;
        v.Add(centre - right - up); v.Add(centre + right - up);
        v.Add(centre - right + up); v.Add(centre + right + up);
        uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
        uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
        tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
        tri.Add(k + 1); tri.Add(k + 3); tri.Add(k + 2);
    }

    /// <summary>
    /// An axis-free box: <paramref name="centre"/> is the FOOT centre (unless
    /// <paramref name="centredY"/>), <paramref name="right"/> and <paramref name="back"/> are
    /// half-extent vectors, and the box rises <paramref name="height"/> in world up.
    /// </summary>
    private static void Box(List<Vector3> v, List<Vector2> uv, List<int> tri, List<Color> colors,
                            Vector3 centre, Vector3 right, Vector3 back, float height, Color tint,
                            bool centredY = false)
    {
        var up = Vector3.up * height;
        var lo = centredY ? centre - up * 0.5f : centre;
        var hi = lo + up;

        // HANDEDNESS GUARD. The five faces below are wound for a RIGHT-handed (right, back, up)
        // basis, and callers build both sides of the road from (tangent, side * sign) whose
        // handedness FLIPS with the sign - so without this, every gate post on one side of the
        // road renders inside-out. Negating `right` fixes the winding and, because these are
        // symmetric half-extent vectors, changes the box's shape not at all.
        if (Vector3.Dot(Vector3.Cross(right, back), Vector3.up) < 0f) right = -right;

        var c = new[]
        {
            lo - right - back, lo + right - back, lo + right + back, lo - right + back,
            hi - right - back, hi + right - back, hi + right + back, hi - right + back,
        };
        // Per-face quads (not a shared-vertex cube) so RecalculateNormals gives hard edges - a
        // smoothed cairn corner reads as an inflated balloon under a cel ramp.
        int[,] faces =
        {
            { 0, 1, 5, 4 }, { 1, 2, 6, 5 }, { 2, 3, 7, 6 }, { 3, 0, 4, 7 }, { 4, 5, 6, 7 },
        };
        float wide = right.magnitude * 2f, deep = back.magnitude * 2f;
        var spans = new[] { wide, deep, wide, deep, wide };

        for (int f = 0; f < 5; f++)
        {
            int b = v.Count;
            for (int k = 0; k < 4; k++) v.Add(c[faces[f, k]]);
            float w = spans[f] / 3f, h = (f == 4 ? deep : height) / 3f;
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(w, 0));
            uv.Add(new Vector2(w, h)); uv.Add(new Vector2(0, h));
            if (colors != null) for (int k = 0; k < 4; k++) colors.Add(tint);
            tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
            tri.Add(b); tri.Add(b + 3); tri.Add(b + 2);
        }
    }

    private static Mesh Finish(string name, Vector3[] verts, Vector2[] uvs, List<int> tris,
                               Vector2[] uv2 = null)
    {
        var mesh = new Mesh { name = name };
        mesh.indexFormat = verts.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.uv = uvs;
        if (uv2 != null)
        {
            mesh.uv2 = uv2;
            // Explicit zero TEXCOORD2. An unwritten UV channel does not read as zero in the
            // vertex shader - it aliases whatever stream was bound last, which is how the
            // dry-grass splat ended up driving SakuraTerrain's Sakura-petal overlay and turned
            // the col pink. Sixteen bytes a vertex to make a guarantee explicit.
            mesh.uv3 = new Vector2[verts.Length];
        }
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static GameObject AddMesh(Transform parent, string name, Mesh mesh, Material mat,
                                      bool collider)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // Persist the mesh so the saved scene does not carry a dangling reference to a runtime
        // Mesh object (which comes back as an empty renderer next editor session).
        string path = $"{MeshDir}/{mesh.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return go;
    }

    // =================================================================== materials

    private static readonly Dictionary<string, Material> MaterialCache =
        new Dictionary<string, Material>();

    private static Material LoadOrCreate(string name, string shaderName)
    {
        string path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find(shaderName);
        mat.name = name;
        mat.enableInstancing = true;
        return mat;
    }

    /// <summary>
    /// Shadow tint for GREY / NEUTRAL surfaces - stone, asphalt, slate, fleece, terrain.
    ///
    /// Sakura's cel <c>_ShadeColor</c> is a blue-VIOLET whose green channel sits ~0.06 below the
    /// red/blue midpoint. On green grass that is invisible, because the surface's own albedo
    /// fills the green back in. On anything NEUTRAL it is not: the violet multiplies against a
    /// warm key and the surface lands on dusty rose. Maple City lost several build+capture cycles
    /// to exactly this, with its entire pavement reading mauve.
    ///
    /// Taka is the region most exposed to that failure in the whole game - it is built almost
    /// entirely from grey dry stone, grey asphalt and pale fleece - so EVERY neutral material in
    /// this file passes this shade explicitly. The diagnostic is the G-gap, G - (R+B)/2: negative
    /// means a rose cast, and this colour is authored at +0.00.
    /// </summary>
    private static readonly Color GroundShade = new Color(0.380f, 0.505f, 0.640f, 1f);

    private static Material CelMaterial(string name, Color albedo, float gloss = 0.2f,
                                        float spec = 0.18f, float rim = 0.5f, Texture texture = null,
                                        Color? shade = null)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, CelShaderName);
        mat.SetColor("_Color", albedo);
        mat.SetColor("_ShadeColor", shade ?? new Color(0.44f, 0.50f, 0.68f, 1f));
        mat.SetFloat("_ShadeStrength", 0.58f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.08f);
        // COOLER AND WEAKER than the city's warm gold rim. Maple City is lit by a low golden sun
        // where a hot rim is the point; Taka is clean late-morning daylight at 1,980 m, and a
        // gold rim on every stone would drag the region back towards golden hour - which the
        // design doc explicitly rules out ("NOT golden hour, not cold cobalt").
        mat.SetColor("_RimColor", new Color(1f, 0.94f, 0.82f, 1f));
        mat.SetFloat("_RimStrength", rim);
        mat.SetFloat("_Gloss", gloss);
        mat.SetFloat("_SpecStrength", spec);
        mat.SetFloat("_ShadowAmbient", 0.50f);
        mat.SetFloat("_ShadowSoft", 0.15f);
        if (texture != null) mat.SetTexture("_MainTex", texture);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Exposed upland asphalt (#5c5b52 in the design palette). Borrows the coast's asphalt
    /// albedo - a road surface is a road surface - but tinted cooler and darker than the city's
    /// boulevard, because a pass road is patched, weathered and never resurfaced.
    /// </summary>
    private static Material AsphaltMaterial() =>
        CelMaterial("Taka_Asphalt", new Color(0.795f, 0.805f, 0.775f), gloss: 0.18f,
                    spec: 0.11f, rim: 0.06f,
                    texture: CoastTexture("Shiosai_Asphalt_Albedo.png"), shade: GroundShade);

    /// <summary>
    /// Dry stone - walls, cairns, the col marker, the hut's footing. The region's own map,
    /// authored by tools/blender/taka_surfaces.py as COURSED masonry with dark joints and
    /// lichen, which is what lets a swept ribbon read as a wall built stone by stone.
    /// </summary>
    private static Material StoneMaterial() =>
        // Cold granite, NOT the warm limestone the highlands used: G-gap is held at +0.000 so
        // this neutral grey cannot pick up the cel shade's rose bias on a flat coping.
        CelMaterial("Taka_Granite", new Color(0.500f, 0.512f, 0.524f), gloss: 0.10f,
                    spec: 0.08f, rim: 0.12f,
                    texture: TakaTexture("Taka_Granite_Albedo.png"), shade: GroundShade);

    /// <summary>
    /// GALLERY / TUNNEL INTERIOR CONCRETE. Identical to <see cref="StoneMaterial"/> in every
    /// respect but one: _Cull = 0.
    ///
    /// That single float is the difference between a gallery and an open-air pillar colonnade.
    /// The roof slab and the back wall are wound from OUTSIDE the volume, so with the default
    /// back-face culling the rider entering the gallery sees straight through the roof to the
    /// sky. It is a documented invariant of this project ("set _Cull = 0 on camera-interior
    /// geometry") and it has already cost one rebuild on the Sakura tunnel.
    ///
    /// _ShadowAmbient is lifted to 0.62 as well: a cel-shaded soffit with no bounce term is a
    /// black ceiling, and the whole point of the gallery is the flicker of light through the
    /// pillar gaps, which needs somewhere to land.
    /// </summary>
    private static Material InteriorMaterial()
    {
        const string name = "Taka_Interior";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, CelShaderName);
        mat.SetColor("_Color", new Color(0.565f, 0.568f, 0.552f));
        mat.SetColor("_ShadeColor", GroundShade);
        mat.SetFloat("_ShadeStrength", 0.52f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.10f);
        mat.SetColor("_RimColor", new Color(1f, 0.96f, 0.90f, 1f));
        mat.SetFloat("_RimStrength", 0.10f);
        mat.SetFloat("_Gloss", 0.08f);
        mat.SetFloat("_SpecStrength", 0.05f);
        mat.SetFloat("_ShadowAmbient", 0.62f);
        mat.SetFloat("_ShadowSoft", 0.18f);
        mat.SetTexture("_MainTex", TakaTexture("Taka_Concrete_Albedo.png"));
        mat.SetFloat("_Cull", 0f);   // <-- the whole reason this material exists
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>Weathered upland timber: gate rails, marker posts, the hut's walls.</summary>
    private static Material TimberMaterial() =>
        // Creosoted alpine timber - darker and colder than upland fence timber, because up here
        // it is refuge cladding and plough-marker stock, not field gates.
        CelMaterial("Taka_Timber", new Color(0.318f, 0.282f, 0.244f), gloss: 0.12f,
                    spec: 0.08f, rim: 0.16f, shade: GroundShade);

    /// <summary>
    /// The summit tarn. Still, cold and mirror-dark: at 1,900 m a tarn is mostly reflected sky
    /// with almost no scattering of its own, so it is authored DARKER than it looks, with the
    /// gloss and specular doing the work. A pale-blue tarn reads as a swimming pool.
    ///
    /// _CULL = 0 IS LOAD-BEARING, EXACTLY AS IN AzoraHighlandsEnvironment.WaterMaterial().
    /// BuildTarn() emits this slab as a fan-triangulated horizontal disc - a centre vertex plus
    /// Ring rim vertices, triangles (0, k, k+1) - and that winding makes the disc's FRONT face
    /// point straight DOWN. Measured, not assumed: TakaWaterCullProbe reported the first fan
    /// triangle's world front normal as (0, -1, 0) with the material's _Cull sitting at
    /// MapleRideCelLit's default 2 (Back). Kori Lake was therefore entirely backface-culled for
    /// every camera that looked down into the basin, which is every camera a rider on the col
    /// above actually has; only a near-level grazing shot caught it edge-on. Water sits in a
    /// bowl precisely so it is seen from above, so a single-sided shell is never the right answer
    /// here - this is the same invariant already applied to petals, moss, soil and every
    /// ring/skirt/bore interior in this project.
    /// </summary>
    private static Material WaterMaterial()
    {
        // KORI LAKE IS FROZEN, so this is not water at all. Ice is the opposite material to a
        // tarn: instead of a dark mirror it is a pale, high-albedo, LOW-gloss slab whose
        // character comes from the crevasse map rather than from reflection. Keeping the
        // highlands' 0.92 gloss here would have produced a black mirror at the summit, which is
        // exactly what a still tarn should look like and exactly what lake ice should not.
        var mat = CelMaterial("Taka_Ice", new Color(0.745f, 0.836f, 0.872f), gloss: 0.34f,
                              spec: 0.30f, rim: 0.42f,
                              texture: TakaTexture("Taka_Ice_Albedo.png"), shade: GroundShade);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        return mat;
    }

    /// <summary>
    /// Verge flora billboards. Cutout foliage with a strong wind term, because everything in
    /// this region is being blown - the posts lean, the smoke streams, the seed flies, and
    /// still grass in the middle of that would be the one thing that gives it away.
    /// </summary>
    private static Material TuftMaterial()
    {
        const string name = "Taka_Tuft";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FoliageShaderName);
        mat.SetColor("_Color", new Color(0.78f, 0.78f, 0.72f, 1f));
        mat.SetTexture("_MainTex", TakaTexture("Taka_Lichen_Sprite.png"));
        mat.SetFloat("_Cutoff", 0.42f);
        mat.SetColor("_ShadeColor", new Color(0.44f, 0.52f, 0.60f, 1f));
        mat.SetFloat("_ShadeStrength", 0.55f);
        mat.SetFloat("_Translucency", 0.70f);
        mat.SetColor("_TransColor", new Color(0.78f, 0.82f, 0.50f, 1f));
        mat.SetColor("_RimColor", new Color(0.92f, 0.93f, 0.82f, 1f));
        mat.SetFloat("_RimStrength", 0.26f);
        mat.SetFloat("_WindStrength", 0.22f);
        mat.SetFloat("_WindSpeed", 2.1f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    // ------------------------------------------------- distant-range atmospheric perspective
    //
    // THE RAMP BELOW WAS SOLVED, NOT GUESSED. The three bands used to be flat Unlit/Color fills
    // at the tints in BuildDistantRanges, and those tints turn out to sit almost exactly on one
    // straight line from a single honest landform colour toward a single horizon colour - i.e.
    // the original author had hand-baked aerial perspective into the fill because a flat unlit
    // shader gave them nowhere else to put it:
    //
    //      band 0 @  5,500 m   (0.300, 0.404, 0.446)  = lerp(ridge, horizon, 0.298)
    //      band 1 @  9,000 m   (0.424, 0.520, 0.578)  = lerp(ridge, horizon, 0.477)
    //      band 2 @ 14,000 m   (0.556, 0.630, 0.688)  = lerp(ridge, horizon, 0.646)
    //      ridge = (0.100, 0.210, 0.240),  horizon = RidgeHazeColour
    //
    // This ramp reproduces 0.295 / 0.466 / 0.665 at those same three radii - within 0.01 of the
    // baked values - so moving the bands onto MapleRide/HDRP/RidgeHaze leaves each one's SCREEN
    // colour exactly where QA signed it off. Nothing washes out and no hue moves. What is gained
    // is that the haze is now computed per pixel from REAL camera distance instead of frozen into
    // the fill (the player's distance to a point on a 5.5-14 km ring swings by the whole 22 km
    // length of the route), plus the valley-fill, form-shading and macro-noise terms a flat fill
    // could never have had. ALL PROVISIONAL ART TUNING.
    private const string RidgeShaderName = "MapleRide/HDRP/RidgeHaze";
    /// <summary>Half-width of each band's OWN haze ramp, in metres. The ramp is centred on the
    /// band's ring radius, so a band is slightly clearer where the route runs close to it and
    /// slightly hazier where the route runs away from it - real parallax haze across a 22 km
    /// course - while never straying far from the value it was signed off at.</summary>
    private const float  RidgeRampHalfM  = 5500f;
    /// <summary>How far below / above its anchor a band's haze is allowed to travel. Bounded on
    /// purpose: an unbounded ramp is what darkened the near band to a faceted teal slab on the
    /// first attempt at this port.</summary>
    private const float  RidgeHazeBelow  = 0.10f;
    private const float  RidgeHazeAbove  = 0.12f;
    /// <summary>The honest landform colour the three flat tints were hand-baked away from. Used
    /// only to RECOVER each band's baked haze amount; the colour actually pushed to the material
    /// is solved per band from its own tint, so each band keeps its exact hue.</summary>
    private static readonly Color RidgeBaseColour = new Color(0.100f, 0.210f, 0.240f, 1f);
    /// <summary>Ring floor Y, mirrored from <see cref="BuildDistantRanges"/> so the shader's
    /// _BaseY tracks the geometry rather than drifting away from it.</summary>
    private const float  RidgeFloorY     = 640f;
    /// <summary>Height above the ring floor over which haze pools and dissolves a range's feet.
    /// 560 m puts the dissolve just under the lowest band crest base (1,180 m).</summary>
    private const float  RidgeValleyM    = 560f;
    /// <summary>Crest gradient span - floor 640 m to the tallest band crest (~2,140 m).</summary>
    private const float  RidgeCrestM     = 1500f;
    private static readonly Color RidgeHazeColour = new Color(0.78f, 0.86f, 0.93f, 1f);

    /// <summary>
    /// Distant ridge silhouettes, rendered through MapleRide/HDRP/RidgeHaze - the same shader
    /// Shiosai Coast already uses for its headlands, and the shader that exists precisely for
    /// this geometry (it reconstructs its normal from screen-space derivatives because these
    /// rings emit BOTH windings, which makes averaged vertex normals sum to zero).
    ///
    /// The bands are still not key-lit in any meaningful way - _FormShading is held at 0.28, so a
    /// range cannot pick up the near hillside's shading and collapse into it, which is the whole
    /// reason they were flat in the first place.
    /// </summary>
    /// <param name="nominalDistanceM">The band's ring radius, used to invert the haze ramp and
    /// recover the honest landform colour that <paramref name="tint"/> had haze baked into.</param>
    private static Material RangeMaterial(string name, Color tint, float nominalDistanceM)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var shader = Shader.Find(RidgeShaderName);
        if (shader == null || !shader.isSupported)
        {
            // FALL BACK TO EXACTLY THE OLD BEHAVIOUR - not to a lit shader, and not to null. If
            // the HDRP shader ever goes missing (a rollback to the Built-in checkpoint) the
            // horizon must still be the colour it has always been, rather than magenta or three
            // ranges that suddenly respond to the key light.
            Debug.LogWarning($"[taka] '{RidgeShaderName}' unavailable - '{name}' falls back to " +
                             "flat Unlit/Color at its baked tint.");
            var flat = LoadOrCreate(name, "Unlit/Color");
            flat.SetColor("_Color", tint);
            EditorUtility.SetDirty(flat);
            MaterialCache[name] = flat;
            return flat;
        }

        var mat = LoadOrCreate(name, RidgeShaderName);

        // STEP 1 - RECOVER THE BAKED HAZE. Read off the green channel how far this band's flat
        // tint had already been lifted from the honest landform colour toward the horizon. Green
        // is used because it is the channel with the largest separation between the two ends, so
        // it is the least noisy to invert; the recovered amount then reproduces red and blue to
        // within 0.02, which is what makes this a solve rather than a guess.
        float h = Mathf.Clamp01((tint.g - RidgeBaseColour.g) /
                                Mathf.Max(RidgeHazeColour.g - RidgeBaseColour.g, 1e-4f));

        // STEP 2 - DE-BAKE THE FILL.  tint = lerp(base, horizon, h)  ->
        //                             base = (tint - h*horizon) / (1 - h)
        // Solved per channel from THIS band's tint, not from RidgeBaseColour, so each band keeps
        // its own hue exactly rather than being flattened onto one shared colour.
        //
        // The blend the shader performs happens in AUTHORED (sRGB) space - MR_AuthoredCol brings
        // the linear-uploaded Color property back to authored space before lerping - so this
        // solve is done in authored space too. Solving it in linear space instead would have
        // landed every band a few percent dark, which is exactly the class of "each value is
        // individually correct but the two halves are in different spaces" bug that washed the
        // coast out once already.
        float k = Mathf.Max(1f - h, 0.05f);
        var baseCol = new Color(Mathf.Clamp01((tint.r - h * RidgeHazeColour.r) / k),
                                Mathf.Clamp01((tint.g - h * RidgeHazeColour.g) / k),
                                Mathf.Clamp01((tint.b - h * RidgeHazeColour.b) / k), 1f);

        // STEP 3 - ANCHOR A NARROW RAMP ON THE BAND'S OWN RADIUS.
        //
        // THE MISTAKE THIS REPLACES, BECAUSE IT COST A WHOLE RENDER CYCLE. The first version of
        // this port gave all three bands ONE wide ramp (1.5 -> 15.5 km, haze 0.20 -> 0.68) and
        // de-baked each band at its ring RADIUS. But the shader ramps on length(positionRWS) -
        // the true camera-to-pixel distance - and the camera is never at the plan centre: from
        // the col it sits ~2 km off, so the near ring's close side is only ~3.3 km away and got
        // far less haze than it had been authored with. The near band came back as a dark,
        // hard-faceted teal slab and the three layers separated WORSE than the flat fills did.
        //
        // Centring each band's own ramp on its own radius makes r the fixed point: at the
        // nominal distance the band renders exactly the colour QA signed off, and it can only
        // drift by RidgeHazeBelow/RidgeHazeAbove either side of that however the route moves.
        float rampStart = Mathf.Max(nominalDistanceM - RidgeRampHalfM, 200f);
        float rampFull  = nominalDistanceM + RidgeRampHalfM;
        float hazeMin   = Mathf.Clamp01(h - RidgeHazeBelow);
        float hazeMax   = Mathf.Clamp01(h + RidgeHazeAbove);

        mat.SetColor("_Color", baseCol);
        // Crest lift: a small step toward the horizon so the top of a ridge separates from its
        // flanks. Deliberately only 0.12 - a fully modelled distant mountain stops reading as
        // distance and starts reading as a near hill.
        mat.SetColor("_CrestColor", Color.Lerp(baseCol, RidgeHazeColour, 0.12f));
        mat.SetColor("_HazeColor", RidgeHazeColour);
        mat.SetFloat("_HazeStart", rampStart);
        mat.SetFloat("_HazeFull", rampFull);
        mat.SetFloat("_HazeMin", hazeMin);
        mat.SetFloat("_HazeMax", hazeMax);
        mat.SetFloat("_BaseY", RidgeFloorY);
        mat.SetFloat("_ValleyHeight", RidgeValleyM);
        mat.SetFloat("_ValleyFill", 0.30f);
        // FORM SHADING OFF, AND IT HAS TO BE. BuildDistantRanges emits every quad with BOTH
        // windings, so the shader reconstructs a per-triangle geometric normal from screen-space
        // derivatives - and on a 240-segment ring 5-14 km across each of those triangles is
        // hundreds of metres wide. Any diffuse term therefore paints the ring's own facets, and
        // at 0.28 it did exactly that: the first render came back with hard polygonal patches
        // across the horizon. Shiosai can afford 0.34 because its headlands are modelled
        // landforms; these rings are silhouettes and must stay flat, which was the original
        // Unlit/Color decision and is still right.
        mat.SetFloat("_FormShading", 0f);
        mat.SetFloat("_CrestHeight", RidgeCrestM);
        // Macro noise for the same reason, kept to a whisper at a scale far larger than a facet
        // so it breaks the fill without ever outlining a triangle.
        mat.SetFloat("_MacroNoise", 0.05f);
        mat.SetFloat("_MacroScale", 2600f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        Debug.Log($"[taka] range '{name}' @ {nominalDistanceM:0} m -> RidgeHaze; recovered baked " +
                  $"haze {h:0.000} (ramp {hazeMin:0.00}-{hazeMax:0.00} over {rampStart:0}-{rampFull:0} m), " +
                  $"landform base {baseCol}.");
        return mat;
    }

    /// <summary>
    /// Soft additive-ish puff for the hut's chimney smoke.
    ///
    /// It reuses the SEED atlas's first cell - the dandelion clock - rather than authoring a
    /// separate smoke sprite, because a dandelion clock already IS a soft radial falloff with no
    /// hard edge, which is precisely what a smoke billboard needs. The tiling/offset picks that
    /// one cell out of the 2x2 sheet.
    /// </summary>
    private static Material SoftParticleMaterial(string name, Texture _)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var shader = Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply")
                     ?? Shader.Find("Sprites/Default");
        var mat = LoadOrCreate(name, shader.name);
        // The highlands reused its own seed atlas's dandelion cell here because that cell was
        // already a soft radial falloff. Taka's atlas has no radial cell at all - every one of
        // its four is an elongated streak, by assertion - so a stretched smoke puff would read
        // as more spindrift. The LICHEN atlas's pale saxifrage cell is the soft blob this needs.
        mat.SetTexture("_MainTex", TakaTexture("Taka_Lichen_Sprite.png"));
        mat.SetTextureScale("_MainTex", new Vector2(0.5f, 0.5f));
        mat.SetTextureOffset("_MainTex", new Vector2(0f, 0f));
        if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 2f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_LightingEnabled")) mat.SetFloat("_LightingEnabled", 0f);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetColor("_Color", new Color(1f, 1f, 1f, 1f));
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// The fell surface. The shared triplanar terrain shader with all three slots given over to
    /// the region's own maps: "grass" is emerald upland turf, "scree" is the golden dry grass of
    /// the exposed tops (driven by the UV1 altitude ramp in <see cref="GroundSplat"/>), and
    /// "rock" is the same dry limestone the walls are built from, auto-blended onto steep faces.
    ///
    /// The slope thresholds are LOW here (32-52 degrees against the city's 42-62), which is the
    /// opposite of the city's problem: a city floor is flat everywhere and the auto-rock blend
    /// had to be pushed away, whereas a mountain that stays grassy up a 45-degree face looks like
    /// a golf course. Rock wants to appear early.
    /// </summary>
    private static Material GroundMaterial()
    {
        const string name = "Taka_Ground";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.SetTexture("_GrassTex", TakaTexture("Taka_Granite_Albedo.png"));
        mat.SetTexture("_GrassNormal", TakaTexture("Taka_Granite_Normal.png"));
        mat.SetTexture("_GrassRough", SakuraTexture("Sakura_Grass_Rough.png"));
        // 2026-09-26 claude-taka2 (Mt. Ventoux): the altitude "scree" layer is the bare pale
        // limestone moonscape above the treeline, not a snowfield.
        mat.SetTexture("_ScreeTex", TakaTexture("Taka_Limestone_Albedo.png"));
        mat.SetTexture("_ScreeNormal", TakaTexture("Taka_Limestone_Normal.png"));
        mat.SetTexture("_ScreeRough", SakuraTexture("Sakura_Grass_Rough.png"));
        // 2026-09-26: steep faces were the smooth CONCRETE map (item 6: "smooth stretched grey
        // shapes"). They are granite now, at a larger scale and darker than the gentle ground.
        mat.SetTexture("_RockTex", TakaTexture("Taka_Limestone_Albedo.png"));
        mat.SetTexture("_RockNormal", TakaTexture("Taka_Limestone_Normal.png"));
        mat.SetTexture("_RockRough", SakuraTexture("Sakura_Rock_Rough.png"));

        // THE PINK BUG, AND WHY THESE FOUR LINES EXIST.
        //
        // SakuraTerrain carries two overlays this region never asked for: a moss film and a
        // SAKURA PETAL ACCUMULATION tint (_PetalColor 0.97/0.80/0.86 at _PetalStrength 0.55 by
        // default). Their weights arrive in TEXCOORD2. Taka's meshes write UV0 and UV1 only,
        // and an unwritten TEXCOORD2 does NOT read as zero - it aliases the previous stream, so
        // petal came through equal to the dry-grass splat. The consequence was invisible in the
        // meadows (dry = 0) and total at the col (dry = 1): the payoff view, the one shot the
        // region is named for, rendered as a PINK DESERT, and it rendered that way while every
        // log line said PASS.
        //
        // Belt and braces: the overlays are switched off here, the soil layer is pointed at the
        // turf maps so a leaked soil weight is turf rather than untextured white, AND Finish()
        // now writes an explicit zero TEXCOORD2 on every splatted mesh.
        mat.SetFloat("_PetalStrength", 0f);
        // Moss is nearly off: above the treeline the only living colour is the ochre lichen
        // already painted into the granite albedo, and a green film over a snow region is the
        // fastest way to make it read as the highlands again.
        mat.SetFloat("_MossStrength", 0.05f);
        mat.SetColor("_MossColor", new Color(0.40f, 0.42f, 0.36f, 1f));
        mat.SetTexture("_SoilTex", TakaTexture("Taka_Granite_Albedo.png"));
        mat.SetTexture("_SoilNormal", TakaTexture("Taka_Granite_Normal.png"));
        mat.SetTexture("_SoilRough", SakuraTexture("Sakura_Grass_Rough.png"));
        mat.SetColor("_SoilColor", new Color(0.424f, 0.437f, 0.450f, 1f));
        mat.SetFloat("_SoilScale", 1.5f);

        // AUTHORED BELOW THE PALETTE VALUE, ON PURPOSE. The palette's #3f8f4a / #d9b24a are what
        // these surfaces should READ AS ON SCREEN after the key light and the cel tint have been
        // applied; authoring the albedo at the target value double-counts the light and the fell
        // comes back as a snooker table. The turf texture itself is already authored at ~0.6x for
        // the same reason, so these multipliers stay close to - but under - neutral.
        // THE THREE SLOTS ARE REPURPOSED. "Grass" is bare cold granite, "scree" is the
        // snowfield the UV1 altitude ramp fades in above 2,300 m, and "rock" is the steep-face
        // auto-blend. All three are neutral-to-cool greys, so every one of them needs the G-gap
        // guard below or the cel _ShadeColor turns the whole mountain mauve in shadow.
        //
        // Values are AUTHORED UNDER the palette (#6a6f77 granite, #f2f6fa snow): the palette is
        // what the surface should READ AS once lit, and the textures are already authored low
        // for the same reason. Snow in particular clips instantly if this is pushed up.
        // 2026-09-26 claude-taka2: warmed toward Provencal garrigue (was 0.424/0.437/0.450 grey).
        mat.SetColor("_GrassColor", new Color(0.500f, 0.465f, 0.370f, 1f));
        // 2026-09-26: snow pulled down from 0.84 and cooled - with _AmbientStrength 1.8 it was
        // clipping to flat white (item 5), so the sastrugi in the snow map never showed.
        // 2026-09-26 claude-taka2: pale limestone scree (off-white, faint warm cast), not snow.
        // 2026-09-26 copilot session 3 (T3): still read as snow. The albedo map is now warm buff
        // broken stone at a mean of ~0.52 (was off-white ~0.72, near-neutral), so this
        // multiplier went UP slightly while the product went down ~25% and warmer.
        mat.SetColor("_ScreeColor", new Color(0.680f, 0.640f, 0.575f, 1f));
        // G-gap +0.001: a neutral grey that will NOT go mauve under the key. See GroundShade.
        mat.SetColor("_RockColor", new Color(0.600f, 0.570f, 0.515f, 1f));   // limestone faces

        // SCALES CUT BY ~3x after the first render pass. At 4.2 the granite map repeated about
        // every metre and a 400 m mountainside read as BRICKWORK - the strata and joint lines
        // authored into the texture to make it legible as cut rock became a visible masonry grid
        // instead. A mountain face needs a several-metre repeat; a wall needs a one-metre one,
        // and the wall has its own material.
        // 2026-09-26: the shader's scale is METRES PER TILE (MRT_TriSample divides by it), so the
        // 1.5 / 1.6 / 3.0 values here tiled the granite every 1.5 m - the "grid" on every slope
        // in the region review. Rock faces want a multi-metre strata repeat.
        mat.SetFloat("_GrassScale", 5.5f);
        mat.SetFloat("_ScreeScale", 4.5f);
        mat.SetFloat("_RockScale", 9.0f);
        if (mat.HasProperty("_DetileAmount")) mat.SetFloat("_DetileAmount", 0.6f);
        // Rock appears EARLIER still than the highlands (26-44 against 32-52): on a 2,842 m
        // face anything steeper than a scree slope is bare rock, and snow that clings to a
        // 40-degree wall is the tell that a mountain is fake.
        mat.SetFloat("_SlopeRockStart", 26f);
        mat.SetFloat("_SlopeRockEnd", 44f);
        // HIGH macro variation. This is a 24 km region seen from a col 1,080 m up; without a
        // strong low-frequency break-up the whole fell tiles visibly from the viewpoint the
        // region is named after.
        mat.SetFloat("_MacroVariation", 0.58f);
        mat.SetFloat("_NormalStrength", 1.35f);
        // 2026-09-26 copilot session 3 (T3): the fell's shade was GroundShade (0.38/0.51/0.64), a
        // strong blue, which is the "moonscape looks bluish" half of the review: every slope
        // facing away from the sun went sky-blue and the pale stone read as shaded snow. A
        // near-neutral, slightly cool grey keeps the sky-lit feel without the blue cast (G-gap
        // 0.00, so it cannot go mauve either).
        mat.SetColor("_ShadeColor", new Color(0.500f, 0.530f, 0.560f, 1f));
        // SOFTENED. Combined with the old dark ambient this was what turned every slope facing
        // the camera into a blue-black wall. Snow and granite both want a shallow ramp: the form
        // is described by the normal map and the relief, not by a hard terminator.
        mat.SetFloat("_ShadeStrength", 0.55f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.14f);
        // Cast shadows on open upland ground must stay READABLE, not black: at altitude the
        // shadow side of everything is lit almost purely by a bright sky dome. The first render
        // came back with the cattle-grid gate casting bars of pure black across gold turf.
        mat.SetFloat("_ShadowAmbient", 0.68f);
        // AMBIENT STRENGTH RAISED (default 1.0 -> 1.8), diagnosed via a temporary per-term
        // shader readback (TerrainShadeDiag): even fully sunlit, unoccluded fell (ndl ~0.8,
        // sunShadow ~1.0 - NOT the shadow ramp, genuinely lit) was compositing to an authored
        // colour of only ~0.5, which the shared MR_ToRender authored->linear bridge (necessary
        // and correct - see MapleRideHDRPCommon.hlsl) compresses hard in that range, so the
        // whole granite/rock fell displayed as a near-uniform dark blue-grey slab regardless of
        // which side of the mountain faced the sun. _ShadowAmbient (already the highest in the
        // game at 0.68) cannot fix this: its lerp only engages when sunShadow < 1, and these
        // slopes were not in cast shadow at all. Raising _AmbientStrength lifts the ambient
        // term itself, which is active on BOTH the lit and shadowed side, so the whole fell
        // clears the compression floor and reads as lit rock with the shadow side still
        // legibly darker relative to it - matching Azora's contrast behaviour instead of
        // Taka's own too-dark trilight values getting crushed flat.
        mat.SetFloat("_AmbientStrength", 1.8f);
        // COOL rim, not the highlands' warm one: a warm rim across a grazing snowfield is the
        // other half of where the khaki tan was coming from.
        mat.SetColor("_RimColor", new Color(0.90f, 0.95f, 1f, 1f));
        // Flat ground at a grazing angle fires the rim at full strength across the whole frame;
        // the pass learned this the expensive way. Ground rim is 0.03-0.06, never 0.2.
        mat.SetFloat("_RimStrength", 0.05f);
        mat.SetFloat("_SpecStrength", 0.07f);
        // The shader's own height tint is left PUSHED OUT OF RANGE on purpose, even though this
        // is the snow region. Taka's snow line is authored in GroundSplat() as a real splat
        // weight against the SNOW TEXTURE, which gives sastrugi, blue troughs and sparkle; the
        // shader's _HeightRange is only a white TINT over whatever is underneath, and stacking
        // the two would wash the sastrugi straight back out.
        mat.SetVector("_HeightRange", new Vector4(4000f, 4600f, 0f, 0f));
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    private static Texture SakuraTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture>($"{SakuraTextureDir}/{file}");

    private static Texture CoastTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture>($"{CoastTextureDir}/{file}");

    /// <summary>The region's own maps, authored by tools/blender/taka_surfaces.py.</summary>
    private static Texture TakaTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture>($"{TextureDir}/{file}");

    /// <summary>
    /// The highlands sky: CLEAN HIGH-ALTITUDE LATE MORNING, which the design doc calls out
    /// explicitly as neither golden hour nor cold cobalt.
    ///
    /// It has to be its own material for the reason the coast and city passes document: all four
    /// regions share one scene and one skybox slot, so anything a region does not own it
    /// inherits - and inheriting Sakura's sunset would leave Taka reading as "the pass at dusk,
    /// but higher up". The specific differences from every other sky in the game:
    ///  * A DEEP, CLEAN zenith. At 1,980 m there is a third less atmosphere overhead and the sky
    ///    really does go this blue; it is the single strongest altitude cue available.
    ///  * A TIGHT, PALE horizon band (HorizonSharp 5.4 against the city's 2.6). Haze means damp
    ///    low air; a broad warm band here would undo the altitude the zenith just bought.
    ///  * A SMALL, HARD, NEARLY WHITE sun with very little glow. A big soft orange lobe is a low
    ///    sun by definition.
    ///  * Bright cumulus with real structure, sitting LOW in the dome - because from the col you
    ///    are at cloud height and should be looking ACROSS at them, not up.
    /// </summary>
    private static Material BuildTakaSky()
    {
        var sky = LoadOrCreate("Taka_Sky", "MapleRide/HDRP/Sky");
        // DEEPER AND HARDER THAN THE HIGHLANDS. #1f4e8c is the design's named cobalt and it is
        // the single strongest altitude cue the region has: at 2,842 m there is a third less
        // atmosphere overhead than at the highlands col, and the zenith really does go this dark.
        sky.SetColor("_ZenithColor", new Color(0.078f, 0.239f, 0.502f, 1f));   // #143f80, past #1f4e8c
        sky.SetColor("_MidColor", new Color(0.310f, 0.545f, 0.796f, 1f));
        sky.SetColor("_HorizonColor", new Color(0.824f, 0.890f, 0.933f, 1f));
        sky.SetColor("_GroundColor", new Color(0.322f, 0.338f, 0.356f, 1f));   // rock, not turf
        // Tighter still than the highlands' 5.4: thin dry air has almost no horizon band at all.
        sky.SetFloat("_HorizonSharp", 7.2f);
        sky.SetFloat("_MidPoint", 0.30f);
        sky.SetColor("_SunColor", new Color(1f, 0.973f, 0.918f, 1f));
        sky.SetFloat("_SunSize", 0.021f);
        sky.SetFloat("_SunSoftness", 0.007f);
        sky.SetFloat("_SunGlow", 0.46f);
        sky.SetFloat("_SunGlowPower", 26f);
        sky.SetColor("_CloudColor", new Color(1f, 0.988f, 0.965f, 1f));
        sky.SetFloat("_CloudStrength", 0.46f);
        sky.SetFloat("_CloudScale", 3.3f);
        sky.SetFloat("_CloudHeight", 0.26f);        // low in the dome: you are AT cloud height
        sky.SetFloat("_CloudSpread", 0.30f);
        sky.SetFloat("_Exposure", 1.04f);
        EditorUtility.SetDirty(sky);
        return sky;
    }

    private static List<GameObject> FindRootsByExactName(string exactName)
    {
        var hits = new List<GameObject>();
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == exactName) hits.Add(go);
        return hits;
    }
}

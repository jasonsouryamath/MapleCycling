using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Minato Coast - "Beyond the Horizon". A 19.01 km point-to-point in five chapters:
/// port city departure -> bridge approach -> open-ocean crossing -> far-shore landfall ->
/// inland mountain continuation, anchored by a coral-vermilion steel arch crossing.
///
/// Conventions followed (deliberately, not reinvented)
/// ---------------------------------------------------
/// * Every shader name goes through <see cref="MapleRideShaderNames.Resolve"/>, so a region
///   rebuild cannot silently drop a surface back onto a pipeline-incompatible shader.
/// * Neutral surfaces (concrete, asphalt, steel, rock) pass <see cref="GroundShade"/>, whose
///   G-gap is authored at +0.00. Maple City lost build cycles to mauve pavement because the
///   default cel shade is blue-violet and neutral albedo cannot fill the green back in.
/// * Geometry is authored in Blender in Unity coordinates through `u2b`, exported with
///   `bake_space_transform`, so every module imports on an identity root - no rotation or
///   scale fix-up happens here.
/// * The pass is idempotent: the root is matched by EXACT name and destroyed before rebuild.
///   A `Contains` match once renamed a fill light into a second sun in this project.
///
/// Asset sourcing
/// --------------
/// Vegetation, rock/cliff, harbour-house and marine-hardware geometry is REUSED from Sakura
/// Pass and Shiosai Coast rather than re-authored. Those are production assets (Sakura's trees
/// are 4-6 MB of branching geometry with leaf-card crowns) and reusing them is what holds
/// Minato to the established art bar. Minato authors only what genuinely does not exist:
/// the modular vermilion arch-bridge kit, and the port skyline.
/// </summary>
public static partial class MinatoCoastEnvironment
{
    // =================================================================== paths

    private const string RoutePath = "Assets/Environment/MinatoCoast/MinatoRoute.json";
    private const string SpecPath = "Assets/Environment/MinatoCoast/MinatoBridgeSpec.json";
    private const string MaterialDir = "Assets/Environment/MinatoCoast/Materials";
    private const string MeshDir = "Assets/Environment/MinatoCoast/Meshes";
    private const string ModelDir = "Assets/Environment/MinatoCoast/Models";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    private const string RootName = "Minato Coast Environment";
    /// <summary>Asphalt texture repeats per metre, matched to the production road shader.</summary>
    private const float RoadUvPerMetre = 0.22f;

    private const string CelShaderName = "MapleRide/SakuraCel";
    private const string TerrainShaderName = "MapleRide/SakuraTerrain";
    private const string OceanShaderName = "MapleRide/MinatoOcean";
    private const string FoliageShaderName = "MapleRide/SakuraFoliage";
    private const string RoadShaderName = "MapleRide/SakuraRoad";

    // Reused production libraries (read-only - these regions are never written to).
    private const string SakuraGlb = "Assets/Environment/SakuraPass/BlenderAssets";
    private const string ShiosaiGlb = "Assets/Environment/ShiosaiCoast/BlenderAssets";
    /// <summary>Minato-owned derived assets (LOD ladders built from the reused production GLBs).</summary>
    private const string MinatoGlb = "Assets/Environment/MinatoCoast/BlenderAssets";
    private const string SakuraTex = "Assets/Environment/SakuraPass/Textures";
    private const string ShiosaiTex = "Assets/Environment/ShiosaiCoast/Textures";
    private const string MapleTex = "Assets/Environment/MapleCity/Textures";
    private const string MinatoTex = "Assets/Environment/MinatoCoast/Textures";
    private const string TakaTex = "Assets/Environment/TakaMountains/Textures";

    // =================================================================== tuning
    // ALL provisional - illustrative values behind named constants, never magic literals.

    /// <summary>Where the crossing leaves land and where it makes landfall (route metres).</summary>
    // REDESIGN 2026-09-25: the over-water crossing (2.3-10.3 km) is now a sea-level CAUSEWAY
    // (2.3-7.6 km, road ~4 m above the sea) followed by the elevated cable-stayed BRIDGE
    // (7.6-10.3 km, deck 48 m) - see tools/blender/minato_route.py and MINATO_VISUAL_DESIGN.md.
    // SeaCrossStartM..BridgeEndM = "the road is over water" (terrain, shoreline, verge rules);
    // BridgeStartM..BridgeEndM = "elevated deck structure exists" (spans, piers, deck rails).
    private const float SeaCrossStartM = 2300f;
    private const float BridgeStartM = 7600f;
    private const float BridgeEndM = 10300f;

    private const float SeaLevelY = 0f;
    private const float SeabedY = -38f;

    /// <summary>Land influence falls off over this distance seaward of the last land station.</summary>
    private const float ShoreFalloffM = 260f;
    /// <summary>How far land reaches SEAWARD of a coastal carriageway before the shelf. PROVISIONAL.</summary>
    /// <summary>Lowest the landward terrain is allowed to sit, in metres. Provisional.</summary>
    private const float LandwardFloorY = 2.5f;
    private const float ShoreWidthSeaM = 300f;
    /// <summary>How far land reaches INLAND. Beyond the terrain plan, so there is no drowned
    /// plateau or visible shelf edge on the inland skyline. PROVISIONAL.</summary>
    private const float ShoreWidthLandM = 7000f;

    private const float TerrainCellM = 12f;      // background grid resolution (was 24 - too faceted)
    private const float ChunkM = 512f;           // culling / streaming chunk size
    private const float LandformCellM = 110f;    // coarse IDW grid
    private const float RoadPinM = 16f;          // ground pinned to carriageway inside this
    private const float RoadEaseM = 52f;         // ...easing to pure landform by here

    private const float RoadHalfWidth = 4.0f;    // 8 m carriageway (spec 7-9 m)
    private const float ShoulderW = 0.55f;
    /// <summary>Max rise (m per m) of land away from the road on the low coastal route. PROVISIONAL.</summary>
    private const float CoastBankSlope = 0.22f;

    private const int SeaHalfWidthM = 5200;      // (legacy) ocean plate half-extent
    /// <summary>Ocean plate margin beyond the terrain plan, so the sea runs to the horizon.</summary>
    private const float SeaHorizonMarginM = 9000f;

    /// <summary>
    /// Neutral-surface shade colour. Re-graded for the BRIGHT MIDDAY pass: the old
    /// (0.380, 0.505, 0.640) was a dark blue-grey that crushed every concrete/asphalt/steel
    /// shadow in the port. The G-gap is still authored at +0.00 (R 0.58, B 0.80 -> mid 0.69),
    /// which is what keeps pavement from going mauve. PROVISIONAL.
    /// </summary>
    private static readonly Color GroundShade = new Color(0.580f, 0.690f, 0.800f, 1f);

    // --------------------------------------------------------------- landform tuning
    /// <summary>Rolling coastal relief amplitude at far field. PROVISIONAL.</summary>
    private const float CoastReliefAmpM = 52f;
    /// <summary>Land elevation at which the inland massif starts to appear. PROVISIONAL.</summary>
    private const float MassifOnsetY = 45f;
    /// <summary>Elevation span over which the massif reaches full strength. PROVISIONAL.</summary>
    private const float MassifRangeY = 175f;
    /// <summary>Peak-to-valley amplitude of the inland massif at far field. PROVISIONAL.</summary>
    private const float MassifAmpM = 720f;
    /// <summary>Exponent applied to the normalised ridge field. >1 deepens valleys and keeps
    /// crests, giving mountain relief without high-frequency noise. Provisional.</summary>
    private const float MassifRidgePower = 1.35f;
    /// <summary>Ridge value that maps to "no change". Below it the massif carves valleys. Provisional.</summary>
    private const float MassifValleyBias = 0.22f;
    /// <summary>Elevation where grass gives way to scree and rock. PROVISIONAL.</summary>
    private const float AlpineStartY = 380f;
    private const float AlpineFullY = 640f;
    /// <summary>Terrain plan margin around the route, per side. PROVISIONAL. The old value put a
    /// hard terrain edge 1,200 m out, clearly visible on the skyline of the mountain chapter.</summary>
    private const float TerrainMarginM = 2600f;

    // =================================================================== entry

    [MenuItem("MapleRide/Environments/Build Minato Coast")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(MeshDir);
        MaterialCache.Clear();
        _crowdTops = null; _crowdLegs = null; _crowdHair = null; _crowdBikes = null;
        _crowdSkin = null;
        // Force a fresh bake of the crowd templates for this build; the previous run's baked
        // mesh assets are regenerated from scratch by Prepare().
        MinatoCrowdPopulation.Dispose();
        _landform = null;
        _buckets = null;

        var route = MinatoRoute.Load();
        Debug.Log($"[minato] route {route.Count} stations, {route.Length:N1} m, " +
                  $"Y {route.MinY:N1} -> {route.MaxY:N1}");

        // Idempotency: EXACT-name match, and prune every duplicate, not just the first.
        for (int i = SceneRoots().Count - 1; i >= 0; i--)
        {
            var go = SceneRoots()[i];
            if (go.name == RootName) UnityEngine.Object.DestroyImmediate(go);
        }

        var root = new GameObject(RootName).transform;

        BuildLandform(route);

        var chapters = new Transform[5];
        string[] chapterNames =
        {
            "Chapter 1 - Port City Departure",
            "Chapter 2 - Bridge Approach",
            "Chapter 3 - Open Ocean Midpoint",
            "Chapter 4 - Far-Shore Landfall",
            "Chapter 5 - Inland Mountain Continuation",
        };
        for (int i = 0; i < 5; i++)
        {
            chapters[i] = new GameObject(chapterNames[i]).transform;
            chapters[i].SetParent(root, false);
        }

        BuildTerrain(route, root);
        BuildHeroMountains(route, root, chapters[4]);
        BuildOcean(route, root);
        BuildRoad(route, root);
        // REDESIGN (MINATO_VISUAL_DESIGN.md): each workstream owns a MinatoRedesign.*.cs partial
        // and flips its own *ReplacesLegacy flag once its replacement is verified.
        if (!BridgeReplacesLegacy) BuildBridge(route, chapters[1]);
        ScatterDressing(route, chapters);
        RedesignHarbor(route, chapters);
        RedesignSkyline(route, chapters);
        RedesignBridge(route, chapters);
        RedesignShores(route, chapters);
        RedesignRedBridge(route, chapters);   // title-screen red suspension landfall (MinatoRedesign.RedBridge.cs)
        SnapBollardsToGround(root);
        BuildWindVolumes(route, root);
        ConfigureAtmosphere(root);
        ConfigureStreaming(root);

        // Ambient NPC cyclist traffic lives UNDER this region root, so RegionDirector's existing
        // exact-name visibility model parks thirty riders the moment the player travels anywhere
        // else - no extra region bookkeeping, and no Minato riders on the Sakura or Shiosai
        // roads. It is staged HERE because Apply() destroys and rebuilds the root from scratch
        // every run, which would otherwise silently strip the pool. Same contract as
        // ShiosaiCoastEnvironment.Apply -> ShiosaiNpcTraffic.Stage.
        MinatoNpcTraffic.Stage(root);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[minato] saved '{scene.path}'");
    }

    private static List<GameObject> SceneRoots()
    {
        var list = new List<GameObject>();
        EditorSceneManager.GetActiveScene().GetRootGameObjects(list);
        return list;
    }

    // =================================================================== route

    [Serializable] private class SampleDto { public float[] p; public float[] t; public float d; }
    [Serializable] private class RouteDto { public SampleDto[] samples; public float roadHalfWidth; }

    public class MinatoRoute
    {
        public Vector3[] Position = Array.Empty<Vector3>();
        public Vector3[] Tangent = Array.Empty<Vector3>();
        public float[] Distance = Array.Empty<float>();
        public int Count => Position.Length;
        public float Length => Distance.Length == 0 ? 0f : Distance[Distance.Length - 1];
        public float MinY, MaxY;

        public Vector3 SideFlat(int i)
        {
            var t = new Vector3(Tangent[i].x, 0f, Tangent[i].z);
            if (t.sqrMagnitude < 1e-6f) t = Vector3.forward;
            return Vector3.Cross(Vector3.up, t.normalized).normalized;
        }

        public int IndexAt(float metres)
        {
            for (int i = 0; i < Count; i++) if (Distance[i] >= metres) return i;
            return Count - 1;
        }

        public static MinatoRoute Load()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(RoutePath);
            if (text == null)
                throw new FileNotFoundException(
                    $"{RoutePath} is missing. Run: python tools/blender/minato_route.py");
            var dto = JsonUtility.FromJson<RouteDto>(text.text);
            int n = dto.samples.Length;
            var r = new MinatoRoute
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
            return r;
        }
    }

    // =================================================================== landform
    //
    // One continuous height function H(x,z), derived FROM the road exactly as Taka's is, because
    // Minato has the same problem: 19 km of road folded into an 11 x 7.8 km box, where chapter 5
    // looks back over the whole crossing. A ribbon of ground would self-overlap on the
    // switchbacks and a flat plate under a mountain reads as card.
    //
    // Minato adds one thing Taka does not need: the middle 8 km of the route is OVER WATER. So
    // the IDW is fed only by LAND stations, and land influence decays seaward, which produces
    // the shoreline for free instead of hand-modelling it.

    private static float[,] _landform;
    private static float _lfOriginX, _lfOriginZ;
    private static int _lfNx, _lfNz;

    private const float BucketM = 256f;
    private static List<int>[,] _buckets;
    private static float _bkOriginX, _bkOriginZ;
    private static int _bkNx, _bkNz;
    private static Bounds _worldPlan;

    private static bool IsLandStation(MinatoRoute r, int i) =>
        r.Distance[i] < SeaCrossStartM || r.Distance[i] > BridgeEndM;

    private static void BuildLandform(MinatoRoute route)
    {
        _seaSide = null;
        _lodHits = _lodMisses = 0;
        _worldPlan = new Bounds(route.Position[0], Vector3.zero);
        for (int i = 0; i < route.Count; i++) _worldPlan.Encapsulate(route.Position[i]);
        _worldPlan.Expand(new Vector3(TerrainMarginM * 2f, 0f, TerrainMarginM * 2f));

        // bucket grid over LAND stations only
        _bkOriginX = _worldPlan.min.x; _bkOriginZ = _worldPlan.min.z;
        _bkNx = Mathf.CeilToInt(_worldPlan.size.x / BucketM) + 1;
        _bkNz = Mathf.CeilToInt(_worldPlan.size.z / BucketM) + 1;
        _buckets = new List<int>[_bkNx, _bkNz];
        for (int i = 0; i < route.Count; i++)
        {
            if (!IsLandStation(route, i)) continue;
            int bx = Mathf.Clamp((int)((route.Position[i].x - _bkOriginX) / BucketM), 0, _bkNx - 1);
            int bz = Mathf.Clamp((int)((route.Position[i].z - _bkOriginZ) / BucketM), 0, _bkNz - 1);
            (_buckets[bx, bz] ??= new List<int>()).Add(i);
        }

        _lfOriginX = _worldPlan.min.x; _lfOriginZ = _worldPlan.min.z;
        _lfNx = Mathf.CeilToInt(_worldPlan.size.x / LandformCellM) + 1;
        _lfNz = Mathf.CeilToInt(_worldPlan.size.z / LandformCellM) + 1;
        _landform = new float[_lfNx, _lfNz];

        for (int ix = 0; ix < _lfNx; ix++)
            for (int iz = 0; iz < _lfNz; iz++)
            {
                float x = _lfOriginX + ix * LandformCellM;
                float z = _lfOriginZ + iz * LandformCellM;
                float wsum = 0f, vsum = 0f;
                for (int i = 0; i < route.Count; i += 3)
                {
                    if (!IsLandStation(route, i)) continue;
                    var p = route.Position[i];
                    float dx = p.x - x, dz = p.z - z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > 4600f * 4600f) continue;
                    float w = 1f / (d2 + 900f);
                    wsum += w; vsum += w * p.y;
                }
                _landform[ix, iz] = wsum > 0f ? vsum / wsum : SeabedY;
            }
        Debug.Log($"[minato] landform {_lfNx}x{_lfNz} cells over {_worldPlan.size.x:N0} x {_worldPlan.size.z:N0} m");
        BuildFarNearest(route);
    }

    private static float LandformAt(float x, float z)
    {
        float fx = Mathf.Clamp((x - _lfOriginX) / LandformCellM, 0, _lfNx - 1.001f);
        float fz = Mathf.Clamp((z - _lfOriginZ) / LandformCellM, 0, _lfNz - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float a = Mathf.Lerp(_landform[ix, iz], _landform[ix + 1, iz], tx);
        float b = Mathf.Lerp(_landform[ix, iz + 1], _landform[ix + 1, iz + 1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    /// <summary>Distance to the nearest LAND route station, and that station's index.</summary>
    private static float NearestLand(MinatoRoute r, float x, float z, out int best)
    {
        best = -1;
        float bd = float.MaxValue;
        int bx = Mathf.Clamp((int)((x - _bkOriginX) / BucketM), 0, _bkNx - 1);
        int bz = Mathf.Clamp((int)((z - _bkOriginZ) / BucketM), 0, _bkNz - 1);
        for (int ring = 0; ring <= 6 && best < 0 || ring <= 2; ring++)
        {
            for (int ox = -ring; ox <= ring; ox++)
                for (int oz = -ring; oz <= ring; oz++)
                {
                    if (Mathf.Max(Mathf.Abs(ox), Mathf.Abs(oz)) != ring) continue;
                    int cx = bx + ox, cz = bz + oz;
                    if (cx < 0 || cz < 0 || cx >= _bkNx || cz >= _bkNz) continue;
                    var list = _buckets[cx, cz];
                    if (list == null) continue;
                    foreach (int i in list)
                    {
                        var p = r.Position[i];
                        float dx = p.x - x, dz = p.z - z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d < bd) { bd = d; best = i; }
                    }
                }
        }
        if (best < 0 && _farIdx != null)
        {
            int fx = Mathf.Clamp(Mathf.RoundToInt((x - _lfOriginX) / LandformCellM), 0, _lfNx - 1);
            int fz = Mathf.Clamp(Mathf.RoundToInt((z - _lfOriginZ) / LandformCellM), 0, _lfNz - 1);
            best = _farIdx[fx, fz];
            var fp = r.Position[best];
            bd = Mathf.Sqrt((fp.x - x) * (fp.x - x) + (fp.z - z) * (fp.z - z));
        }
        return bd;
    }

    /// <summary>
    /// Fallback table for points beyond the bucket search radius. The ring search stops at ring 6
    /// (about 1.5 km at BucketM = 256), and every point past that returned "no land station" -
    /// which made GroundAt hand back the seabed and drew several square kilometres of flat
    /// tableland at -38 m across the horizon of chapters 01, 04 and 05. This coarse per-cell
    /// nearest-station table is built once and is exact enough at those distances.
    /// </summary>
    private static void BuildFarNearest(MinatoRoute r)
    {
        _farIdx = new int[_lfNx, _lfNz];
        for (int ix = 0; ix < _lfNx; ix++)
            for (int iz = 0; iz < _lfNz; iz++)
            {
                float x = _lfOriginX + ix * LandformCellM;
                float z = _lfOriginZ + iz * LandformCellM;
                float bd = float.MaxValue; int best = 0;
                for (int i = 0; i < r.Count; i += 8)
                {
                    if (!IsLandStation(r, i)) continue;
                    var p = r.Position[i];
                    float dx = p.x - x, dz = p.z - z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < bd) { bd = d2; best = i; }
                }
                _farIdx[ix, iz] = best;
            }
    }

    private static int[,] _farIdx;

    /// <summary>
    /// Terrain relief added on top of the coarse IDW landform.
    ///
    /// Two regimes. Near the coast the landscape is low and rolling. INLAND, where the route
    /// climbs towards 400 m, a ridged-noise massif takes over so chapter 05 reads as real
    /// mountains with peaks and valleys instead of the smooth tilted ramp it was. Both
    /// amplitudes grow with distance from the carriageway, so the ride line keeps its cut and
    /// fill while the skyline gets shape. ALL PROVISIONAL.
    /// </summary>
    private static float Relief(float x, float z, float distToRoad, float baseY)
    {
        float far = Mathf.Clamp01((distToRoad - 40f) / 900f);
        float amp = Mathf.Lerp(1.2f, CoastReliefAmpM, far);
        // The 54 m octave was being sampled by 18-24 m distant terrain grids. That is below a
        // safe reconstruction rate once the slope is projected into a chapter-wide view, so it
        // aliased into the regular diamond/terrace pattern seen in 04/05. Keep that small-scale
        // breakup close to the ride line and let the broad/mid octaves form distant mountains.
        float nearDetail = 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01((distToRoad - 70f) / 520f));
        float n = 0f;
        n += (Mathf.PerlinNoise(x * 0.0021f + 11.3f, z * 0.0021f + 4.7f) - 0.5f) * 1.0f;
        n += (Mathf.PerlinNoise(x * 0.0067f + 31.1f, z * 0.0067f + 9.2f) - 0.5f)
             * Mathf.Lerp(0.20f, 0.45f, nearDetail);
        n += (Mathf.PerlinNoise(x * 0.0185f + 57.9f, z * 0.0185f + 22.4f) - 0.5f)
             * (0.12f * nearDetail);
        float relief = n * amp;

        // Inland massif. Keyed to how high the surrounding land already is, so it fades in
        // exactly where the route leaves the coastal plain and never disturbs the port, the
        // crossing or the landfall valley floor.
        float alp = Mathf.Clamp01((baseY - MassifOnsetY) / MassifRangeY);
        if (alp > 0.002f)
        {
            float ridge = 0f;
            ridge += Ridged(x * 0.00046f + 3.1f, z * 0.00046f + 8.9f) * 1.00f;
            ridge += Ridged(x * 0.00075f + 19.7f, z * 0.00075f + 27.3f) * 0.70f;
            ridge += Ridged(x * 0.00155f + 41.2f, z * 0.00155f + 12.8f) * 0.22f;
            ridge /= 1.92f;
            // SHAPE, not sharpness. Raising the normalised ridged field to a power is the
            // standard way to turn rolling hills into mountains: it pulls the common mid values
            // down into broad smooth valleys while leaving the rare crests near full height, so
            // the silhouette gains relief WITHOUT adding high-frequency noise or faceting. The
            // earlier linear field averaged around 0.5 everywhere, which is exactly why the
            // inland chapter read as high rolling country. PROVISIONAL.
            ridge = Mathf.Pow(Mathf.Clamp01(ridge), MassifRidgePower);
            float mAmp = Mathf.Lerp(8f, MassifAmpM, Mathf.Clamp01((distToRoad - 55f) / 700f));
            relief += (ridge - MassifValleyBias) * mAmp * alp;
        }
        return relief;
    }


    /// <summary>
    /// Floating bollards (user playtest 2026-09-25): bollards are placed from analytic heights
    /// (deck drop, or GroundAt + 0.25 m) that disagree with the real surface wherever a bank,
    /// quay deck or embankment mesh sits a little higher or lower. Drop every bollard onto the
    /// first collider beneath it; leave it where it is if nothing is hit within 4 m.
    /// </summary>
    private static void SnapBollardsToGround(Transform root)
    {
        Physics.SyncTransforms();
        int snapped = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.IndexOf("Bollard", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (t.parent != null && t.parent.name.IndexOf("Bollard", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            var own = t.GetComponentsInChildren<Collider>(true);
            foreach (var c in own) c.enabled = false;
            var from = t.position + Vector3.up * 2.0f;
            if (Physics.Raycast(from, Vector3.down, out var hit, 6.0f, ~0, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(hit.point.y - t.position.y) > 0.03f)
            {
                t.position = new Vector3(t.position.x, hit.point.y - 0.02f, t.position.z);
                snapped++;
            }
            foreach (var c in own) c.enabled = true;
        }
        Debug.Log($"[minato] bollards snapped to the surface below: {snapped}");
    }


    /// <summary>
    /// Local wind exposure along the route (WeatherWindVolume boxes, read by WeatherDirector):
    /// sheltered harbor quay, channelled glass-district streets, exposed seawall/causeway and a
    /// very exposed bridge. One box per 100 m segment, oriented along the road, 240 m wide.
    /// </summary>
    private static void BuildWindVolumes(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Wind Volumes").transform;
        parent.SetParent(root, false);
        (float from, float to, float mult)[] zones =
        {
            (0f, 1000f, 0.6f),                  // harbor: warehouses + stacks shelter the quay
            (1000f, 1800f, 1.3f),               // glass district: street-canyon channelling
            (1800f, SeaCrossStartM, 1.15f),     // seawall sprint: open to the sea
            (SeaCrossStartM, BridgeStartM, 1.25f), // causeway: exposed both sides
            (BridgeStartM, BridgeEndM, 1.5f),   // bridge deck: the crosswind challenge
            (BridgeEndM, 13000f, 0.8f),         // far-shore settlement + trees
        };
        int n = 0;
        foreach (var z in zones)
            for (float d = z.from; d < z.to; d += 100f)
            {
                int i = route.IndexAt(d + 50f);
                var p = route.Position[i];
                var fwd = route.Tangent[i]; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) continue;
                var go = new GameObject($"Wind {d:0} x{z.mult:0.00}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd.normalized, Vector3.up));
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                // Overlapping 20 m so neighbouring boxes of the same zone never gap.
                box.size = new Vector3(240f, 120f, 120f);
                var v = go.AddComponent<WeatherWindVolume>();
                v.windMultiplier = z.mult;
                v.edgeBlendM = 30f;
                n++;
            }
        Debug.Log($"[minato] wind volumes: {n}");
    }

    /// <summary>
    /// The authored mountain was sculpted for the ORIGINAL route profile. After the 2026-09-25
    /// re-profile it floated ~100 m above the road at 13.1-13.6 km (a ceiling over the rider) and
    /// 7-12 m above it to 14.6 km, while the generated ground under it was hidden - the "hole"
    /// with the sea showing at road level (measured by MinatoShellProbe). Conform a COPY of each
    /// LOD mesh: vertices near the route blend onto GroundAt (full within 60 m, fading out by
    /// 400 m), so near slopes meet the road and the far hillside keeps its sculpted shape.
    /// Saved as assets so the scene stays small. Idempotent (always starts from the GLB mesh).
    /// </summary>
    private static void ConformShellToRoute(MinatoRoute route, GameObject mountain)
    {
        const string dir = "Assets/Environment/MinatoCoast/Generated";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/Environment/MinatoCoast", "Generated");
        int moved = 0, total = 0, meshes = 0;
        foreach (var mf in mountain.GetComponentsInChildren<MeshFilter>(true))
        {
            var src = mf.sharedMesh;
            if (src == null) continue;
            if (!src.isReadable) { Debug.LogWarning($"[minato] shell mesh {src.name} not readable - cannot conform"); continue; }
            var mesh = UnityEngine.Object.Instantiate(src);
            mesh.name = src.name + "_Conformed";
            var v = mesh.vertices;
            var tr = mf.transform;
            for (int i = 0; i < v.Length; i++)
            {
                var w = tr.TransformPoint(v[i]);
                float dist = NearestLand(route, w.x, w.z, out int idx);
                if (idx < 0 || dist > 400f) continue;
                float dAlong = route.Distance[idx];
                if (dAlong < 12500f || dAlong > 15600f) continue;   // the shell fits beyond ~15 km
                float target = GroundAt(route, w.x, w.z);
                float wgt = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 400f, dist));
                // ease the correction in/out along the route too, so there is no seam
                wgt *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12500f, 12900f, dAlong))
                     * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15000f, 15600f, dAlong)));
                if (wgt <= 0f || Mathf.Abs(w.y - target) < 0.3f) continue;
                w.y = Mathf.Lerp(w.y, target, wgt);
                v[i] = tr.InverseTransformPoint(w);
                moved++;
            }
            total += v.Length;
            mesh.vertices = v;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string path = $"{dir}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            mf.sharedMesh = mesh;
            meshes++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[minato] conformed authored mountain to the route: {moved:N0}/{total:N0} vertices in {meshes} meshes");
    }

    /// <summary>Ridged (absolute-valued) Perlin - gives crests rather than blobs.</summary>
    private static float Ridged(float u, float v)
    {
        float p = Mathf.PerlinNoise(u, v);
        return 1f - Mathf.Abs(p * 2f - 1f);
    }

    /// <summary>THE height function. Everything that is not road or bridge sits on this.</summary>
    private static float GroundAt(MinatoRoute r, float x, float z)
    {
        float d = NearestLand(r, x, z, out int idx);
        if (idx < 0) return SeabedY;

        float land = LandformAt(x, z);
        land += Relief(x, z, d, land);

        // Road cut/fill: pin to the carriageway near it, ease out to pure landform.
        if (d < RoadEaseM)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - RoadPinM) / (RoadEaseM - RoadPinM)));
            land = Mathf.Lerp(r.Position[idx].y - 0.12f, land, t);
        }
        // Coastal bank limit. The 2026-09-25 re-profile dropped the seawall/marina road to the
        // waterside, but the landform behind it kept the old headland height, so the ease above
        // produced a 30 m+ vertical cutting wall beside the marina (user playtest). On the low
        // coastal route the land may only rise at a gentle bank from the road; the mountain
        // chapter (road above ~60 m) keeps its real cuttings.
        float roadY = r.Position[idx].y;
        float lowCoast = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(40f, 80f, roadY));
        if (lowCoast > 0f)
        {
            float bank = roadY + Mathf.Max(0f, d - RoadPinM) * CoastBankSlope;
            if (land > bank) land = Mathf.Lerp(land, bank, lowCoast);
        }
        // Far shore up to the authored mountain shell (13.6 km): no trenches either. At 13.5 km
        // the land rose on BOTH sides and the retaining-wall kit walled the road into a ~10 m
        // concrete canyon (user: "very low quality"). A steeper bank is allowed here, but the
        // road always sits on a shelf with a view instead of in a slot.
        else if (r.Distance[idx] < 13200f)
        {
            // Fades out before the authored mountain shell (13.6 km+), which is separate geometry
            // fitted to the ORIGINAL terrain - lowering the land under it left it overhanging.
            float fade = 1f - Mathf.InverseLerp(12900f, 13200f, r.Distance[idx]);
            float bank = roadY + Mathf.Max(0f, d - RoadPinM) * 0.45f;
            if (land > bank) land = Mathf.Lerp(land, bank, fade);
        }

        // ------------------------------------------------------------------ shoreline
        // The old rule decayed the land to seabed with distance from the road in EVERY
        // direction, so everything beyond ~400 m of the carriageway drowned - which is exactly
        // the flat drowned "tan plateau" that filled the horizon of the mountain chapter. The
        // sea only lies on the SEAWARD side, and only where the route is actually coastal.
        var sp = r.Position[idx];
        var sside = r.SideFlat(idx);
        float lateral = (x - sp.x) * sside.x + (z - sp.z) * sside.z;
        bool seaward = lateral * SeaSideSign(r, idx) > 0f;

        // Inland the land simply continues; seaward it runs out at a coastal shelf whose width
        // closes to nothing at each end of the crossing, so the bridge genuinely leaves land at
        // a shoreline instead of spanning several hundred metres of beach.
        float gap = Mathf.Min(Mathf.Abs(r.Distance[idx] - SeaCrossStartM),
                              Mathf.Abs(r.Distance[idx] - BridgeEndM));
        // ...and only where the route is genuinely coastal. Without this test the shelf put open
        // ocean 300 m from the carriageway 8 km inland, in the middle of the mountain chapter.
        float coastal = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(90f, 220f, sp.y));
        float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(gap / 2500f));
        float seaW = Mathf.Lerp(6f, ShoreWidthSeaM, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(gap / 700f)));
        // The LANDWARD shelf needs the same ramp. At full width (7 km) the land behind the two
        // shore stations reached more than half way across the 7.9 km crossing and put a tan
        // coastline right beside the bridge at the ocean midpoint. PROVISIONAL.
        float landW = Mathf.Lerp(900f, ShoreWidthLandM, ramp);
        float width = seaward ? Mathf.Lerp(landW, seaW, coastal) : landW;

        float infl = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(width, width + ShoreFalloffM, d));
        float g = Mathf.Lerp(SeabedY, land, infl);
        // The landward side must never dip under the waterline: relief noise at the low-lying
        // port end was cutting lagoons into the city behind the quays.
        if (!seaward && infl > 0.5f) g = Mathf.Max(g, LandwardFloorY);
        return g;
    }

    /// <summary>
    /// Which lateral side of a land station the open sea is on: the side that faces the
    /// crossing. Derived from geometry rather than hard-coded, so it stays correct for the
    /// port, the landfall and the inland run alike.
    /// </summary>
    /// <summary>Public accessor for diagnostics framing (which side the open sea is on).</summary>
    internal static float SeaSideSignFor(MinatoRoute r, int idx) => SeaSideSign(r, idx);

    private static float SeaSideSign(MinatoRoute r, int idx)    {
        if (_seaSide == null)
        {
            _seaSide = new float[r.Count];
            var mid = r.Position[r.IndexAt((SeaCrossStartM + BridgeEndM) * 0.5f)];
            float last = 1f;
            for (int i = 0; i < r.Count; i++)
            {
                var p = r.Position[i];
                var s = r.SideFlat(i);
                float dx = mid.x - p.x, dz = mid.z - p.z;
                float len = Mathf.Sqrt(dx * dx + dz * dz) + 1e-3f;
                float cos = (dx * s.x + dz * s.z) / len;
                // Where the crossing lies dead ahead the lateral test is degenerate; inherit
                // the previous station's side so the shoreline never flips mid-coast.
                if (Mathf.Abs(cos) < 0.15f) _seaSide[i] = last;
                else _seaSide[i] = last = (cos >= 0f ? 1f : -1f);
            }
        }
        return _seaSide[idx];
    }

    private static float[] _seaSide;

    // =================================================================== terrain

    private static void BuildTerrain(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Terrain").transform;
        parent.SetParent(root, false);

        var mat = TerrainMaterial();

        int nx = Mathf.CeilToInt(_worldPlan.size.x / ChunkM);
        int nz = Mathf.CeilToInt(_worldPlan.size.z / ChunkM);
        int made = 0, near = 0;
        for (int cx = 0; cx < nx; cx++)
            for (int cz = 0; cz < nz; cz++)
            {
                float ox = _worldPlan.min.x + cx * ChunkM;
                float oz = _worldPlan.min.z + cz * ChunkM;
                // Terrain mesh LOD: keep the inland silhouette denser than the old 36 m far
                // mesh. TerrainChunk also snaps the requested spacing to an integer number of
                // subdivisions so every visual chunk ends on the shared 512 m boundary.
                float dc = NearestLand(route, ox + ChunkM * 0.5f, oz + ChunkM * 0.5f, out _);
                float cell = dc < 900f ? TerrainCellM
                           : dc < 2600f ? TerrainCellM * 1.5f
                           : TerrainCellM * 2f;
                var mesh = TerrainChunk(route, ox, oz, cell, out bool anyLand);
                if (!anyLand) continue;   // pure seabed chunks are the ocean's job
                var go = AddMesh(parent, $"Terrain_Chunk_{cx}_{cz}", mesh, mat,
                                 collider: cell <= TerrainCellM + 0.01f);
                if (cell <= TerrainCellM + 0.01f)
                {
                    var mc = go.GetComponent<MeshCollider>();
                    if (mc != null) mc.sharedMesh = TerrainCollisionChunk(route, mesh);
                }
                go.isStatic = true;
                made++;
                if (cell <= TerrainCellM + 0.01f) near++;
            }
        Debug.Log($"[minato] terrain {made} chunks of {ChunkM} m ({near} near-field at " +
                  $"{TerrainCellM} m cells with colliders, rest coarsened for distance)");
    }

    /// <summary>
    /// Stages the Blender-authored chapter-05 landscape over the generated ride terrain. The
    /// generated MeshColliders stay active as the stable ride surface; only covered visual
    /// renderers are disabled, avoiding duplicate/z-fighting terrain without touching physics.
    /// </summary>
    private static void BuildHeroMountains(MinatoRoute route, Transform root, Transform chapter)
    {
        const float anchorDistanceM = 16000f;
        var prefab = Glb(MinatoGlb, "Minato_Mountain_HeroValley");
        if (prefab == null)
        {
            Debug.LogError("[minato] authored mountain GLB missing");
            return;
        }

        var anchor = route.Position[route.IndexAt(anchorDistanceM)];
        var material = HeroMountainMaterial();
        var mountain = Inst(prefab, chapter, anchor, Quaternion.identity, material);
        mountain.name = "Authored Mountain Landscape";
        ConformShellToRoute(route, mountain);
        var mountainLod = mountain.GetComponent<LODGroup>();
        if (mountainLod != null)
        {
            var levels = mountainLod.GetLODs();
            float[] cuts = { 0.020f, 0.0045f, 0.0003f };
            for (int l = 0; l < levels.Length && l < cuts.Length; l++)
                levels[l].screenRelativeTransitionHeight = cuts[l];
            mountainLod.SetLODs(levels);
            mountainLod.RecalculateBounds();
        }

        bool any = false;
        Bounds authoredBounds = default;
        foreach (var renderer in mountain.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { authoredBounds = renderer.bounds; any = true; }
            else authoredBounds.Encapsulate(renderer.bounds);
        }

        int hiddenRenderers = 0, keptRenderers = 0;
        var terrain = root.Find("Terrain");
        // Coverage test (2026-09-25): the authored shell does NOT fill its rectangular XZ bounds.
        // Hiding every generated chunk inside the rectangle opened holes near 13.5 km (sea plane
        // at road level, the shell's edge overhanging the road) that the old retaining walls
        // happened to hide. Hide a chunk only where a ray actually hits the shell above/below it.
        var probeCols = new List<MeshCollider>();
        foreach (var mf in mountain.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                probeCols.Add(mc);
            }
        Physics.SyncTransforms();
        bool Covered(Bounds b)
        {
            int hits = 0;
            for (int k = 0; k < 5; k++)
            {
                var o = b.center + (k == 0 ? Vector3.zero : new Vector3(
                    (k % 2 == 0 ? 0.35f : -0.35f) * b.size.x, 0f, (k < 3 ? 0.35f : -0.35f) * b.size.z));
                foreach (var h in Physics.RaycastAll(new Vector3(o.x, 3000f, o.z), Vector3.down, 6000f))
                    if (h.collider is MeshCollider m && probeCols.Contains(m)) { hits++; break; }
            }
            return hits >= 4;   // essentially fully under the shell
        }
        if (any && terrain != null)
        {
            foreach (var renderer in terrain.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Replace every generated chunk whose centre lies under the authored shell.
                // Test X/Z explicitly: a chunk's vertical bounds can sit outside the authored
                // renderer bounds even when the two visible surfaces overlap. The old inset
                // left a ring of coarse generated terrain as floating ribbons in 05/05V.
                var c = renderer.bounds.center;
                if (c.x < authoredBounds.min.x || c.x > authoredBounds.max.x ||
                    c.z < authoredBounds.min.z || c.z > authoredBounds.max.z) continue;
                // Keep an uncovered chunk only right beside the route (a real hole under the road);
                // far uncovered chunks are the coarse edge ribbons that must stay hidden.
                if (!Covered(renderer.bounds) &&
                    NearestLand(route, c.x, c.z, out _) < 120f) { keptRenderers++; continue; }
                renderer.enabled = false;
                hiddenRenderers++;
            }
        }
        foreach (var mc in probeCols) UnityEngine.Object.DestroyImmediate(mc);
        Debug.Log($"[minato] mountain shell coverage: kept {keptRenderers} generated chunks the shell does not cover");

        DressHeroMountains(route, chapter, mountain, authoredBounds);

        Debug.Log($"[minato] authored mountain bounds center={authoredBounds.center} " +
                  $"size={authoredBounds.size}; hid {hiddenRenderers} generated terrain renderers " +
                  "(colliders preserved)");
    }

    private static void DressHeroMountains(MinatoRoute route, Transform chapter,
                                           GameObject mountain, Bounds bounds)
    {
        MeshFilter samplerMesh = null;
        foreach (var mf in mountain.GetComponentsInChildren<MeshFilter>(true))
            if (mf.name.EndsWith("_LOD1", StringComparison.Ordinal))
            {
                samplerMesh = mf;
                break;
            }
        if (samplerMesh == null)
            foreach (var mf in mountain.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.EndsWith("_LOD0", StringComparison.Ordinal))
                {
                    samplerMesh = mf;
                    break;
                }
        if (samplerMesh == null || samplerMesh.sharedMesh == null) return;

        // LOD0 now exceeds PhysX's reliable fast-midphase triangle count. The LOD1 shell has
        // the identical silhouette/height field at half spacing and is ample for deterministic
        // vegetation seating without collision misses.
        var sampler = samplerMesh.gameObject.AddComponent<MeshCollider>();
        sampler.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning |
                                 MeshColliderCookingOptions.WeldColocatedVertices |
                                 MeshColliderCookingOptions.CookForFasterSimulation;
        sampler.sharedMesh = samplerMesh.sharedMesh;

        var pine = new[]
        {
            Glb(MinatoGlb, "Minato_Shiosai_Pine"),
            Glb(MinatoGlb, "Minato_Shiosai_Pine_B"),
            Glb(MinatoGlb, "Minato_SakuraPass_Pine_A"),
            Glb(MinatoGlb, "Minato_SakuraPass_Pine_B"),
        };
        var broadleaf = new[]
        {
            Glb(MinatoGlb, "Minato_Shiosai_Broadleaf"),
            Glb(MinatoGlb, "Minato_Shiosai_Broadleaf_B"),
            Glb(MinatoGlb, "Minato_SakuraPass_Broadleaf_A"),
            Glb(MinatoGlb, "Minato_SakuraPass_Broadleaf_B"),
        };
        var rocks = new[]
        {
            Glb(MinatoGlb, "Minato_SakuraPass_Rock_Cluster_A"),
            Glb(MinatoGlb, "Minato_SakuraPass_Rock_Cluster_B"),
            Glb(MinatoGlb, "Minato_SakuraPass_Cliff_Ledge"),
        };
        var bark = CelMaterial("Minato_HeroMountain_Bark", Color.white, 0.10f, 0.05f, 0.38f,
                               Tex(ShiosaiTex, "Shiosai_Bark_Albedo.png"));
        var needle = FoliageMaterial("Minato_HeroMountain_Needle",
                                     new Color(0.82f, 0.91f, 0.78f),
                                     Tex(ShiosaiTex, "Shiosai_Needle_Albedo.png"), 0.20f);
        var leaf = FoliageMaterial("Minato_HeroMountain_Leaf",
                                   new Color(0.91f, 0.97f, 0.84f),
                                   Tex(ShiosaiTex, "Shiosai_Leaf_Albedo_HQ.png"), 0.20f);
        var rock = CelMaterial("Minato_HeroMountain_Rock", new Color(0.66f, 0.64f, 0.59f),
                               0.08f, 0.05f, 0.28f,
                               Tex(ShiosaiTex, "Shiosai_Rock_Albedo.png"), GroundShade);

        var rng = new System.Random(51905);
        int trees = 0, stones = 0;
        for (int attempt = 0; attempt < 4600 && trees < 900; attempt++)
        {
            float x = Mathf.Lerp(bounds.min.x + 80f, bounds.max.x - 80f, (float)rng.NextDouble());
            float z = Mathf.Lerp(bounds.min.z + 80f, bounds.max.z - 80f, (float)rng.NextDouble());
            float routeDistance = NearestLand(route, x, z, out _);
            if (routeDistance < 38f || routeDistance > 1500f) continue;
            var ray = new Ray(new Vector3(x, bounds.max.y + 100f, z), Vector3.down);
            if (!sampler.Raycast(ray, out var hit, bounds.size.y + 300f)) continue;
            if (hit.normal.y < 0.66f || hit.point.y < SeaLevelY + 30f) continue;

            bool usePine = rng.NextDouble() < 0.58;
            var family = usePine ? pine : broadleaf;
            var src = family[rng.Next(family.Length)];
            if (src == null) continue;
            float scale = Mathf.Lerp(1.25f, 2.55f, (float)rng.NextDouble());
            var placed = Inst(src, chapter, chapter.InverseTransformPoint(hit.point),
                              Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                              usePine ? needle : leaf, bark);
            placed.name = $"Hero Mountain Tree {trees:D3}";
            placed.transform.localScale = Vector3.one * scale;
            trees++;
        }

        for (int attempt = 0; attempt < 1500 && stones < 220; attempt++)
        {
            float x = Mathf.Lerp(bounds.min.x + 50f, bounds.max.x - 50f, (float)rng.NextDouble());
            float z = Mathf.Lerp(bounds.min.z + 50f, bounds.max.z - 50f, (float)rng.NextDouble());
            float routeDistance = NearestLand(route, x, z, out _);
            if (routeDistance < 42f || routeDistance > 1150f) continue;
            var ray = new Ray(new Vector3(x, bounds.max.y + 100f, z), Vector3.down);
            if (!sampler.Raycast(ray, out var hit, bounds.size.y + 300f)) continue;
            if (hit.normal.y < 0.24f || hit.point.y < SeaLevelY + 22f) continue;

            var src = rocks[rng.Next(rocks.Length)];
            if (src == null) continue;
            float scale = Mathf.Lerp(1.8f, 5.2f, (float)rng.NextDouble());
            var placed = Inst(src, chapter, chapter.InverseTransformPoint(hit.point - hit.normal * 0.35f),
                              Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), rock);
            placed.name = $"Hero Mountain Rock {stones:D3}";
            placed.transform.localScale = new Vector3(scale, Mathf.Lerp(0.75f, 1.35f,
                                                     (float)rng.NextDouble()), scale);
            stones++;
        }

        UnityEngine.Object.DestroyImmediate(sampler);
        Debug.Log($"[minato] authored mountain dressing {trees} production trees, {stones} rock clusters");
    }

    /// <summary>
    /// The production four-layer triplanar terrain material, same shader and property convention
    /// as Sakura/Shiosai/Taka. Minato previously used a flat cel material with a single grass
    /// albedo, which is exactly why the ground read as a smooth tinted lime mesh.
    ///
    /// Layer weights are baked into the mesh's extra UV channels by <see cref="TerrainChunk"/>
    /// (uv1.x = rock, uv1.y = scree, uv2.x = soil, grass is the remainder). Shiosai warns that a
    /// mesh with no UV2 feeds all-zero weights and renders pure grass.
    ///
    /// Minato reads the SCREE slot as coastal SAND, which is what makes the shoreline and
    /// beaches resolve without a fifth layer.
    /// </summary>
    private static Material TerrainMaterial()
    {
        if (MaterialCache.TryGetValue("Terrain", out var cached) && cached != null) return cached;

        var mat = LoadOrCreate("Minato_Terrain", TerrainShaderName);

        // The original Shiosai grass map has a bright centre and dark corners. Across a large
        // triplanar mountain that tile boundary becomes a literal checkerboard. Shiosai's
        // production Hokkaido ground family is stochastic and directionless, so it keeps close
        // vegetation breakup without drawing a regular grid over the chapter-05 slopes.
        mat.SetTexture("_GrassTex", Tex(ShiosaiTex, "Shiosai_HokkaidoGround_Albedo.png"));
        mat.SetTexture("_GrassNormal", Tex(ShiosaiTex, "Shiosai_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", Tex(ShiosaiTex, "Shiosai_Grass_Rough.png"));
        // Layer tints matched to the SakuraPass_Terrain art bar (grass 0.68/0.82/0.47,
        // scree 0.78/0.72/0.63, soil 0.86/0.78/0.66), nudged cooler for a maritime headland.
        // My earlier pale-sage grass tint desaturated the albedo's own green and compounded
        // the flat look.
        // Pulled down from (0.70, 0.82, 0.50): Minato shows far more open ground than Sakura
        // does, and at that value the big coastal fields read as a flat lime lawn. PROVISIONAL.
        mat.SetColor("_GrassColor", new Color(0.52f, 0.62f, 0.38f));
        // Minato is the first region authored far from the world origin (X 9,000-20,090,
        // Z 4,146-11,944). MapleRideTerrain projects every layer triplanar from ABSOLUTE world
        // position, so those coordinates divide into tiling UVs in the thousands, where float32
        // derivatives quantise and the sampler collapses to the top mip - the whole surface then
        // renders as one flat average colour. Centring the tiling origin on the route keeps the
        // sampled coordinates within +/-5.6 km of zero. Opt-in: defaults to 0 elsewhere.
        mat.SetVector("_TileOrigin", new Vector4(14500f, 0f, 8000f, 0f));
        mat.SetFloat("_GrassScale", 8.0f);

        // Shiosai_Rock_Albedo is deliberately stratified for sea cliffs. On Minato's enormous
        // inland slopes its broad horizontal waves repeated as literal wood grain / contour
        // stripes. Taka's production granite has isotropic mineral breakup and is the correct
        // mountain material; the Shiosai roughness map remains a safe neutral fallback.
        mat.SetTexture("_RockTex", Tex(TakaTex, "Taka_Granite_Albedo.png"));
        mat.SetTexture("_RockNormal", Tex(TakaTex, "Taka_Granite_Normal.png"));
        mat.SetTexture("_RockRough", Tex(ShiosaiTex, "Shiosai_Rock_Rough.png"));
        mat.SetColor("_RockColor", new Color(0.72f, 0.74f, 0.76f));
        mat.SetFloat("_RockScale", 3.2f);

        // SCREE slot = coastal sand.
        mat.SetTexture("_ScreeTex", Tex(ShiosaiTex, "Shiosai_Sand_Albedo.png"));
        mat.SetTexture("_ScreeNormal", Tex(ShiosaiTex, "Shiosai_Sand_Normal.png"));
        mat.SetTexture("_ScreeRough", Tex(ShiosaiTex, "Shiosai_Sand_Rough.png"));
        mat.SetColor("_ScreeColor", new Color(0.80f, 0.75f, 0.66f));
        mat.SetFloat("_ScreeScale", 3.0f);

        if (mat.HasProperty("_SoilTex"))
        {
            mat.SetTexture("_SoilTex", Tex(SakuraTex, "Sakura_Soil_Albedo.png"));
            mat.SetTexture("_SoilNormal", Tex(SakuraTex, "Sakura_Soil_Normal.png"));
            mat.SetTexture("_SoilRough", Tex(SakuraTex, "Sakura_Soil_Rough.png"));
            mat.SetColor("_SoilColor", new Color(0.78f, 0.70f, 0.58f));
            mat.SetFloat("_SoilScale", 3.4f);
        }

        // Slope-driven rock, so cut faces and cliffs expose stone without any baked weight.
        mat.SetFloat("_SlopeRockStart", 26f);
        mat.SetFloat("_SlopeRockEnd", 50f);
        // Taka's production mountain values use several-metre triplanar repeats plus controlled
        // macro/meso breakup. The old 22 m granite projection magnified mineral bands into
        // contour-following wood grain across the massif.
        mat.SetFloat("_MacroVariation", 0.56f);
        mat.SetFloat("_NormalStrength", 0.42f);
        if (mat.HasProperty("_DetileAmount")) mat.SetFloat("_DetileAmount", 0.86f);
        if (mat.HasProperty("_DetileBlendM")) mat.SetFloat("_DetileBlendM", 42f);
        if (mat.HasProperty("_MesoVariation")) mat.SetFloat("_MesoVariation", 0.30f);
        if (mat.HasProperty("_MesoHue")) mat.SetFloat("_MesoHue", 0.38f);
        if (mat.HasProperty("_MesoScaleM")) mat.SetFloat("_MesoScaleM", 31f);
        // Suppress only unresolved tile detail in the long chapter-04/05 views; macro colour
        // and the authored silhouette remain intact.
        if (mat.HasProperty("_DetailFadeAmt")) mat.SetFloat("_DetailFadeAmt", 0.96f);
        if (mat.HasProperty("_DetailFadeStart")) mat.SetFloat("_DetailFadeStart", 110f);
        if (mat.HasProperty("_DetailFadeRange")) mat.SetFloat("_DetailFadeRange", 920f);

        // No cherry drifts on a working port coast.
        if (mat.HasProperty("_PetalStrength")) mat.SetFloat("_PetalStrength", 0f);
        mat.SetFloat("_MossStrength", 0.30f);
        mat.SetColor("_MossColor", new Color(0.40f, 0.52f, 0.34f));

        // Neutral-safe shade: the G-gap rule (see the class header).
        mat.SetColor("_ShadeColor", GroundShade);
        mat.SetFloat("_ShadeStrength", 0.32f);
        if (mat.HasProperty("_RampSmooth")) mat.SetFloat("_RampSmooth", 0.16f);
        if (mat.HasProperty("_ShadowAmbient")) mat.SetFloat("_ShadowAmbient", 0.78f);
        if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", 0.06f);
        // The coast is low; keep the altitude tint off the ride corridor and only on the peaks.
        mat.SetVector("_HeightRange", new Vector4(240f, 430f, 0.35f, 0f));

        EditorUtility.SetDirty(mat);
        MaterialCache["Terrain"] = mat;
        return mat;
    }

    private static Mesh TerrainChunk(MinatoRoute route, float ox, float oz, float cellM, out bool anyLand)
    {
        int n = Mathf.Max(2, Mathf.RoundToInt(ChunkM / cellM));
        float step = ChunkM / n;
        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[verts.Length];
        var uv1 = new Vector2[verts.Length];   // TEXCOORD1: x = rock, y = scree(sand)
        var uv2 = new Vector2[verts.Length];   // TEXCOORD2: x = soil,  y = petal (unused)
        var normals = new Vector3[verts.Length];
        anyLand = false;
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                float x = ox + i * step;
                float z = oz + j * step;
                float y = GroundAt(route, x, z);
                if (y > SeaLevelY - 2f) anyLand = true;
                int k = i * (n + 1) + j;
                verts[k] = new Vector3(x, y, z);
                uvs[k] = new Vector2(x / 18f, z / 18f);

                // Local slope from finite differences, in degrees.
                float h = Mathf.Min(step, TerrainCellM) * 0.5f;
                float dydx = (GroundAt(route, x + h, z) - GroundAt(route, x - h, z)) / (2f * h);
                float dydz = (GroundAt(route, x, z + h) - GroundAt(route, x, z - h)) / (2f * h);
                // Continuous height-field normals are shared across chunk boundaries. The old
                // per-chunk RecalculateNormals pass could only see triangles within one chunk,
                // producing both hard 512 m seams and broad planar lighting facets.
                normals[k] = new Vector3(-dydx, 1f, -dydz).normalized;
                float slopeDeg = Mathf.Atan(Mathf.Sqrt(dydx * dydx + dydz * dydz)) * Mathf.Rad2Deg;

                // ROCK: wave-cut stone on steep ground and on everything near the waterline,
                // which is what makes the coastal cliffs and embankments read as layered rock
                // rather than as a tinted smooth mesh.
                float rockSlope = Mathf.InverseLerp(24f, 46f, slopeDeg);
                float rockShore = 1f - Mathf.InverseLerp(1.0f, 7.0f, y - SeaLevelY);
                float rock = Mathf.Clamp01(Mathf.Max(rockSlope, rockShore * 0.75f));

                // ALPINE: above the treeline the massif must read as rock and scree, not as the
                // same lime pasture as the coast. Without this the mountain chapter renders a
                // flat green tableland however much relief the landform carries. PROVISIONAL.
                float alpine = Mathf.InverseLerp(AlpineStartY, AlpineFullY, y);
                // Alpine ground is ROCK, with soil below the bare crests. It must NOT use the
                // scree slot: in Minato that slot is bound to Shiosai coastal SAND (see
                // TerrainMaterial), so driving it by elevation painted cream beach dunes over
                // every mountain in the rear viewpoint. Sand stays where sand belongs - the
                // waterline. PROVISIONAL.
                rock = Mathf.Clamp01(Mathf.Max(rock, alpine * 0.92f));
                float sandBand = 1f - Mathf.InverseLerp(0.4f, 3.2f, Mathf.Abs(y - (SeaLevelY + 1.1f)));
                float sand = Mathf.Clamp01(sandBand * (1f - Mathf.InverseLerp(6f, 18f, slopeDeg)));

                // SOIL: exposed earth on the mid slopes and along the road cut/fill shoulder.
                // The shoulder term MUST stay narrow. It was previously 0.55 over a 26 m band,
                // which painted an ochre soil apron down both sides of all 19 km - blended with
                // the grass tint that is exactly the flat khaki field the verge was rendering
                // as. A real road shows scuffed earth for a couple of metres past the shoulder
                // and then grass. PROVISIONAL: 3.5 m / 0.30.
                float soilSlope = Mathf.InverseLerp(12f, 30f, slopeDeg)
                                  * (1f - Mathf.InverseLerp(34f, 52f, slopeDeg));
                float nearRoad = 1f - Mathf.InverseLerp(RoadPinM, RoadPinM + 3.5f,
                                                        NearestLand(route, x, z, out _));
                float soil = Mathf.Clamp01(Mathf.Max(soilSlope * 0.8f, nearRoad * 0.30f));
                // A band of exposed earth between the treeline and the bare crests, so the
                // massif transitions grass -> soil -> rock instead of switching in one step.
                soil = Mathf.Clamp01(Mathf.Max(soil, alpine * (1f - alpine) * 1.7f));

                // Rock wins over sand where both want the same vertex.
                sand *= 1f - rock;
                soil *= 1f - Mathf.Max(rock, sand);

                uv1[k] = new Vector2(rock, sand);
                uv2[k] = new Vector2(soil, 0f);
            }
        var tris = new List<int>(n * n * 6);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                int a = i * (n + 1) + j, b = a + 1, c = a + (n + 1), d = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }
        var mesh = new Mesh { name = $"Minato_Terrain_{ox:F0}_{oz:F0}" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.uv = uvs;
        mesh.uv2 = uv1;   // TEXCOORD1 - rock / scree(sand)
        mesh.uv3 = uv2;   // TEXCOORD2 - soil / petal
        mesh.SetTriangles(tris, 0);
        mesh.normals = normals;
        // Without tangents the terrain shader's grass/rock/scree/soil NORMAL maps have no basis
        // to apply against, so the surface shades as a flat tinted mesh no matter how good the
        // layer textures are. Sakura's terrain is Blender-authored and ships tangents; this mesh
        // is generated in C# and must build them explicitly.
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Separate physics mesh with a shallow service trench under the paved corridor. The visible
    /// terrain still tucks 12 cm under the asphalt, but its collider used to sit within 0.60 m of
    /// the road for the entire land route, producing 22,159 stacked-surface hits. Lowering only
    /// the hidden collider removes the redundant ride surface without opening an off-road hole.
    /// </summary>
    private static Mesh TerrainCollisionChunk(MinatoRoute route, Mesh renderMesh)
    {
        var verts = renderMesh.vertices;
        float clear = RoadHalfWidth + ShoulderW + 2.2f;
        for (int i = 0; i < verts.Length; i++)
        {
            float d = NearestLand(route, verts[i].x, verts[i].z, out int idx);
            if (idx < 0 || d >= clear) continue;
            float t = 1f - Mathf.SmoothStep(0f, 1f, d / clear);
            float safeY = route.Position[idx].y - Mathf.Lerp(0f, 1.15f, t);
            verts[i].y = Mathf.Min(verts[i].y, safeY);
        }
        var mesh = new Mesh { name = renderMesh.name + "_Collision" };
        mesh.indexFormat = renderMesh.indexFormat;
        mesh.vertices = verts;
        mesh.triangles = renderMesh.triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // =================================================================== ocean

    // PASS 4 sea colour (PROVISIONAL art tuning, named so a future pass can move them in one
    // place). Pass 3 left the NEAR field reading as a flat bright cyan sheet: at a rider's eye
    // the sea beside the causeway is viewed at a grazing angle, so the Fresnel term handed most
    // of it to _SkyTint rather than to the water body. Two levers, not one:
    //   * deepen the near stop, and
    //   * stop the grazing Fresnel from washing the whole near field to sky.
    // Target: MinatoCoast_StormglassCauseway_Target_v01.png (steel/navy sea, cyan only in the
    // sun path and the wave faces).
    private static readonly Color SeaShallowColor = Srgb(0x14, 0x53, 0x6B);  // pass 3: 0x1C6A80 still read bright cyan near-field
    private static readonly Color SeaSkyTintColor = Srgb(0x8A, 0xA6, 0xC6);  // pass 3: 0x9CB8D8
    private const float SeaFresnelBoost = 0.30f;                             // pass 3: 0.42

    private static void BuildOcean(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Ocean").transform;
        parent.SetParent(root, false);

        var mat = LoadOrCreate("Minato_Ocean", OceanShaderName);
        // Use the coast shader's three-stop distance model. The previous view-angle model
        // resolved most of this 8 km crossing to one saturated cobalt/cyan mass.
        // Desaturated from the previous pure turquoise sheet: the old _ShallowColor 0x1F86A2 /
        // _MidColor 0x125C8C read as a flat over-saturated plastic cyan under the +sat grade.
        // Pulled toward a greyer sea-green/steel-blue with more value spread between the stops so
        // the water reads as a real surface rather than one uniform colour.
        // REDESIGN (golden hour, palette: ocean cyan/turquoise -> deep navy). Water is a main
        // character: brighter turquoise shelf, navy open sea, and a WARM gold sun path with
        // much stronger glitter toward the low sun (reference images 6-8). PROVISIONAL.
        SetIf(mat, "_ShallowColor", SeaShallowColor);          // near shelf: deep teal (see SeaShallowColor)
        SetIf(mat, "_MidColor", Srgb(0x12, 0x42, 0x70));       // middle distance: ocean blue
        SetIf(mat, "_DeepColor", Srgb(0x0E, 0x2C, 0x52));      // open strait: deep navy
        SetIf(mat, "_SkyTint", SeaSkyTintColor);               // sky reflection
        SetIf(mat, "_Color", Srgb(0x1F, 0x62, 0x8E));
        SetIf(mat, "_SunColor", Srgb(0xFF, 0xD2, 0x8C));       // golden sun path
        if (mat.HasProperty("_DepthBlend")) mat.SetFloat("_DepthBlend", 1f);
        if (mat.HasProperty("_ShoreFadeStart")) mat.SetFloat("_ShoreFadeStart", 90f);
        if (mat.HasProperty("_ShoreFadeEnd")) mat.SetFloat("_ShoreFadeEnd", 2600f);
        if (mat.HasProperty("_FresnelBoost")) mat.SetFloat("_FresnelBoost", SeaFresnelBoost);
        if (mat.HasProperty("_FresnelPower")) mat.SetFloat("_FresnelPower", 4.6f);
        if (mat.HasProperty("_GlitterBoost")) mat.SetFloat("_GlitterBoost", 0.55f);
        if (mat.HasProperty("_GlitterPower")) mat.SetFloat("_GlitterPower", 112f);
        if (mat.HasProperty("_SunPathWidth")) mat.SetFloat("_SunPathWidth", 0.26f);
        if (mat.HasProperty("_SunPathStrength")) mat.SetFloat("_SunPathStrength", 0.45f);
        if (mat.HasProperty("_GlitterFalloff")) mat.SetFloat("_GlitterFalloff", 1f);
        if (mat.HasProperty("_GlitterGrazeEnd")) mat.SetFloat("_GlitterGrazeEnd", 0.55f);
        SetIf(mat, "_FoamColor", Srgb(0xF4, 0xFC, 0xFB));
        if (mat.HasProperty("_FoamAmount")) mat.SetFloat("_FoamAmount", 0.20f);
        if (mat.HasProperty("_WaveScale")) mat.SetFloat("_WaveScale", 0.10f);
        if (mat.HasProperty("_WaveStrength")) mat.SetFloat("_WaveStrength", 0.52f);
        if (mat.HasProperty("_DetailFadeStart")) mat.SetFloat("_DetailFadeStart", 240f);
        if (mat.HasProperty("_DetailFadeEnd")) mat.SetFloat("_DetailFadeEnd", 3400f);
        if (mat.HasProperty("_AmbientWeight")) mat.SetFloat("_AmbientWeight", 0.86f);
        if (mat.HasProperty("_LightGain")) mat.SetFloat("_LightGain", 1.08f);
        if (mat.HasProperty("_ChopWeight")) mat.SetFloat("_ChopWeight", 1f);
        if (mat.HasProperty("_ChopScale")) mat.SetFloat("_ChopScale", 6.5f);
        if (mat.HasProperty("_ChopStrength")) mat.SetFloat("_ChopStrength", 0.36f);
        if (mat.HasProperty("_MacroScale")) mat.SetFloat("_MacroScale", 0.00048f);
        if (mat.HasProperty("_MacroStrength")) mat.SetFloat("_MacroStrength", 0.50f);
        EditorUtility.SetDirty(mat);

        var c = _worldPlan.center;
        // The plate MUST cover the whole terrain plan plus a horizon margin. It was a fixed
        // 10.4 km square on a 16.3 x 13.0 km plan, so the corners left several square kilometres
        // of bare seabed at -38 m exposed - rendered by the terrain material as a flat lime
        // tableland, which is exactly what filled the horizon of chapters 01, 04 and 05.
        float half = Mathf.Max(_worldPlan.size.x, _worldPlan.size.z) * 0.5f + SeaHorizonMarginM;
        var mesh = Grid(c.x - half, c.z - half, half * 2f, 96, SeaLevelY,
                        "Minato_OceanPlate");
        var go = AddMesh(parent, "Ocean Surface", mesh, mat, collider: false);
        go.isStatic = true;
        BuildOceanAccents(route, parent);
        Debug.Log($"[minato] ocean plate {half * 2f:N0} m square centred on the route plan " +
                  $"({_worldPlan.size.x:N0} x {_worldPlan.size.z:N0} m)");
    }

    private static Mesh Grid(float ox, float oz, float size, int n, float y, string name)
    {
        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[verts.Length];
        float step = size / n;
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++)
            {
                verts[i * (n + 1) + j] = new Vector3(ox + i * step, y, oz + j * step);
                uvs[i * (n + 1) + j] = new Vector2(i * step / 40f, j * step / 40f);
            }
        var tris = new List<int>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                int a = i * (n + 1) + j, b = a + 1, cc = a + (n + 1), d = cc + 1;
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(cc);
            }
        var mesh = new Mesh { name = name };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.uv = uvs; mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// One combined foam/wake mesh: shoreline interaction, pier turbulence and vessel wakes at
    /// a fraction of the object cost of spawning hundreds of decals. The ocean shader supplies
    /// the small wave field; these broad pale streaks establish kilometre scale.
    /// </summary>
    private static void BuildOceanAccents(MinatoRoute route, Transform parent)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        void Patch(Vector3 centre, Vector3 along, float length, float width)
        {
            along.y = 0f;
            if (along.sqrMagnitude < 1e-4f) along = Vector3.forward;
            along.Normalize();
            var side = Vector3.Cross(Vector3.up, along);
            int b = verts.Count;
            var a = centre - along * (length * 0.5f);
            var z = centre + along * (length * 0.5f);
            verts.Add(a - side * (width * 0.5f));
            verts.Add(a + side * (width * 0.5f));
            verts.Add(z + side * (width * 0.5f));
            verts.Add(z - side * (width * 0.5f));
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, length * 0.2f)); uvs.Add(new Vector2(0f, length * 0.2f));
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        var rng = new System.Random(9022);
        for (float d = SeaCrossStartM + 80f; d < BridgeEndM - 80f; d += 135f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            var fwd = route.Tangent[i];
            for (int q = 0; q < 2; q++)
            {
                float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                float off = 90f + (float)rng.NextDouble() * 820f;
                var c = p + side * (sgn * off);
                c.y = SeaLevelY + 0.035f;
                Patch(c, fwd + side * ((float)rng.NextDouble() - 0.5f) * 0.8f,
                      18f + (float)rng.NextDouble() * 75f,
                      0.7f + (float)rng.NextDouble() * 2.8f);
            }
            // Turbulence around every second pier is broad enough to read from the deck.
            if (((int)((d - SeaCrossStartM) / 135f) & 1) == 0)
            {
                p.y = SeaLevelY + 0.04f;
                Patch(p, side, 14f, 5.5f);
            }
        }

        // Shore-break ribbons on both landfalls.
        foreach (var range in new[] { (from: 80f, to: SeaCrossStartM - 40f),
                                      (from: BridgeEndM + 40f, to: BridgeEndM + 2500f) })
            for (float d = range.from; d < Mathf.Min(range.to, route.Length - 10f); d += 95f)
            {
                int i = route.IndexAt(d);
                var p = route.Position[i];
                var sea = route.SideFlat(i) * SeaSideSign(route, i);
                float edge = 20f;
                for (; edge < 650f; edge += 8f)
                {
                    var q = p + sea * edge;
                    if (GroundAt(route, q.x, q.z) < SeaLevelY + 0.7f) break;
                }
                if (edge >= 650f) continue;
                var c = p + sea * (edge + 2f);
                c.y = SeaLevelY + 0.045f;
                Patch(c, route.Tangent[i], 28f + (float)rng.NextDouble() * 34f, 2.2f);
            }

        var mesh = new Mesh { name = "Minato_Ocean_FoamAndWakes" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var mat = CelMaterial("Minato_OceanFoam", new Color(0.82f, 0.92f, 0.95f),
                              gloss: 0.18f, spec: 0.10f, rim: 0.30f, shade: GroundShade);
        var go = AddMesh(parent, "Foam, Wakes and Pier Turbulence", mesh, mat, false);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    // =================================================================== road
    //
    // PASS 4 wet-patch tuning (PROVISIONAL). Root cause of the "maroon-brown asphalt when facing
    // the low sun" (contact_06400m) turned out to be TWO leftover warm tints on Minato_Asphalt,
    // not the wet term. The road shader adds a grazing-angle rim on top of the lit base:
    //     col += tintRim * rim * ... * sun,   rim = pow(1 - dot(n,v), _RimPower) * _RimStrength
    // On an 8 m ribbon seen from a 1.5 m eye, `1 - dot(n,v)` is ~1 for everything beyond a few
    // metres, so the rim is applied to MOST of the visible road. _RimColor was a Sakura-era
    // orange (1.00, 0.72, 0.52) and _ShoulderColor a warm maroon-grey (0.62, 0.50, 0.50); neither
    // was ever set in code, so LoadOrCreate kept re-loading them off the material asset and every
    // previous pass (which only moved _Color and the wet term) could not shift the result. Under
    // the profile's saturation +20 that orange rim x warm key resolved to maroon.
    // The warm golden glints now come only from _SpecTint (near-white) and the mirror-sun glint.
    private static readonly Color MinatoRoadRimColor = new Color(0.72f, 0.82f, 0.95f);      // was 1.00/0.72/0.52 orange
    private const float MinatoRoadRimStrength = 0.16f;                                      // was 0.25
    private static readonly Color MinatoRoadShoulderColor = new Color(0.60f, 0.62f, 0.66f); // was 0.62/0.50/0.50 maroon-grey
    private static readonly Color MinatoRoadShadeColor = new Color(0.62f, 0.70f, 0.82f);    // was 0.72/0.66/0.74 mauve
    // The wet term itself: the concepts show damp asphalt reflecting the COOL upper sky, with the
    // warm glint supplied separately by the shader's mirror-sun term (untouched). Cooling this
    // also answers "the wet patches are barely visible": a cool reflection has hue contrast
    // against warm-lit tarmac, where the old cream (0.86, 0.80, 0.70) had none. _WetAmount stays
    // at 0.55 for the same reason - the patches need to be MORE readable, not fainter.
    private static readonly Color MinatoRoadWetSkyColor = new Color(0.62f, 0.74f, 0.92f);  // pass 3: 0.86/0.80/0.70 warm cream -> maroon
    private const float MinatoRoadWetAmount = 0.55f;

    // ONE continuous swept surface for the whole 19.01 km, chunked only for culling - the chunks
    // share vertices at their seams by construction (each starts at the previous one's last
    // station), so there is no collider discontinuity for a rider to catch on.

    private static void BuildRoad(MinatoRoute route, Transform root)
    {
        var parent = new GameObject("Road").transform;
        parent.SetParent(root, false);

        var road = LoadOrCreate("Minato_Asphalt", RoadShaderName);
        road.SetTexture("_MainTex", Tex(ShiosaiTex, "Shiosai_Asphalt_Albedo.png"));
        road.SetTexture("_DetailNormal", Tex(ShiosaiTex, "Shiosai_Asphalt_Normal.png"));
        // Slightly cool base: under the warm golden-hour key the neutral albedo read as
        // reddish-brown; the concepts keep asphalt blue-grey with warm glints on top.
        road.SetColor("_Color", new Color(0.74f, 0.85f, 0.95f));   // pass 3: 0.86/0.93/1.06 still read maroon-purple
        road.SetFloat("_UvPerMetre", RoadUvPerMetre);
        road.SetFloat("_UvCentreM", RoadHalfWidth + ShoulderW);
        road.SetFloat("_RoadWidthM", RoadHalfWidth * 2f);
        road.SetFloat("_ShoulderWidthM", ShoulderW);
        road.SetFloat("_TrackOffsetM", 1.05f);
        road.SetFloat("_TrackWidthM", 0.75f);
        road.SetFloat("_TrackDarken", 0.24f);
        // The former 28x micro-normal dominated the chase view and aliased into white speckle,
        // making a 2K asphalt source look lower resolution than it is. Broader, gentler aggregate
        // retains surface depth while TAA/aniso filtering can keep it stable in motion.
        road.SetFloat("_DetailTile", 11f);
        road.SetFloat("_DetailStrength", 0.34f);
        road.SetFloat("_MacroAmount", 0.20f);
        road.SetFloat("_PatchAmount", 0.17f);
        road.SetColor("_PatchColor", new Color(0.78f, 0.80f, 0.82f));
        road.SetFloat("_JointOffsetM", 1.35f);
        road.SetFloat("_JointWidthM", 0.05f);
        road.SetFloat("_JointDarken", 0.34f);
        road.SetFloat("_EdgeRavelM", 0.22f);
        // PASS 4: pin the grazing-angle tints in CODE. These were never set here, so LoadOrCreate
        // kept the material asset's Sakura-era warm values alive across every rebuild. See the
        // MinatoRoad* constants above for the reasoning.
        road.SetColor("_RimColor", MinatoRoadRimColor);
        road.SetFloat("_RimStrength", MinatoRoadRimStrength);
        road.SetColor("_ShoulderColor", MinatoRoadShoulderColor);
        road.SetColor("_ShadeColor", MinatoRoadShadeColor);
        // REDESIGN: wet reflective patches (every Minato concept shows damp asphalt catching
        // the golden sky). Uses the road shader's opt-in _Wet* feature; other regions keep 0.
        road.SetFloat("_WetAmount", MinatoRoadWetAmount);
        road.SetFloat("_WetScale", 0.045f);
        road.SetColor("_WetSkyColor", MinatoRoadWetSkyColor);
        EditorUtility.SetDirty(road);

        int perChunk = Mathf.Max(8, Mathf.RoundToInt(ChunkM / 3f));   // 3 m station spacing
        int chunks = 0;
        for (int start = 0; start < route.Count - 1; start += perChunk)
        {
            int end = Mathf.Min(start + perChunk, route.Count - 1);
            var mesh = RoadRibbon(route, start, end);
            var go = AddMesh(parent, $"Road_{start / perChunk:D3}", mesh, road, collider: true);
            go.isStatic = true;
            chunks++;
        }
        Debug.Log($"[minato] road {chunks} chunks, continuous over {route.Length:N0} m");
        BuildRoadMarkings(route, parent);
        BuildRoadFurniture(route, parent);
    }

    /// <summary>
    /// Guardrails and streetlights along the LAND carriageway. The bridge deck carries its own
    /// furniture inside BuildBridge; without this the approach and the mainland/mountain road
    /// rendered as a bare asphalt ribbon, which is the single most obvious miss against
    /// 02_city_side_bridge_approach.png. Module pitch is measured from the imported prefab rather
    /// than assumed, so a rebuilt module of a different length cannot silently leave gaps.
    /// </summary>
    private static void BuildRoadFurniture(MinatoRoute route, Transform parent)
    {
        var rail = Model("Minato_Bridge_Guardrail");
        var lamp = Model("Minato_Bridge_Lamp");
        if (rail == null) return;

        var steel = SteelMaterial();
        var railMat = ConcreteMaterial();

        float pitch = 6f;
        var mf = rail.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            float len = mf.sharedMesh.bounds.size.z;
            if (len > 0.5f) pitch = len;
        }

        var railRoot = new GameObject("Guardrails").transform; railRoot.SetParent(parent, false);
        var lampRoot = new GameObject("Streetlights").transform; lampRoot.SetParent(parent, false);

        // PROVISIONAL: 42 m lamp spacing, the same order as the bridge deck's.
        const float LampSpacingM = 42f;
        float nextLamp = 0f;
        int rails = 0, lamps = 0;
        float travelled = 0f;

        for (float d = 0f; d < route.Length - pitch; d += pitch)
        {
            // The elevated bridge carries its own rails and lamps; the causeway uses these.
            if (d > BridgeStartM - 30f && d < BridgeEndM + 30f) continue;

            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + pitch, route.Length - 1f));
            var fwd = (route.Position[j] - p);
            if (fwd.sqrMagnitude < 1e-4f) continue;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            for (int s = -1; s <= 1; s += 2)
            {
                var pos = p + side * (s * (RoadHalfWidth + ShoulderW + 0.18f));
                var g = Inst(rail, railRoot, Vector3.zero, rot, railMat);
                g.transform.position = pos;
                g.transform.rotation = rot;
                g.isStatic = true;
                rails++;
            }

            travelled = d;
            if (lamp != null && d >= nextLamp)
            {
                nextLamp = d + LampSpacingM;
                var pos = p + side * (RoadHalfWidth + ShoulderW + 0.30f);
                var l = Inst(lamp, lampRoot, Vector3.zero, rot, steel);
                l.transform.position = pos;
                l.transform.rotation = rot;
                l.isStatic = true;
                lamps++;
            }
        }
        Debug.Log($"[minato] road furniture {rails} guardrail bays (pitch {pitch:F2} m), " +
                  $"{lamps} streetlights on the land carriageway");
        BuildRideBoundary(route, parent);
    }

    /// <summary>
    /// THE ride boundary. The guardrail and parapet modules are visual only - they carry no
    /// colliders, so MinatoCoastValidation found 3,975 exposed edges with nothing to stop a
    /// rider leaving the road down a fill slope or off the deck.
    ///
    /// Rather than give every one of the 7,302 rail bays a mesh collider (expensive, and the
    /// brief explicitly asks for no unnecessary decorative collision), this lays one continuous
    /// invisible barrier down each shoulder in long segments. It sits OUTSIDE the 8 m
    /// carriageway - inner face at 4.30 m from the centreline against a 4.00 m lane half-width -
    /// so it protects the edge without ever intruding on the ride lane, and its 1.10 m height
    /// matches the guardrail spec.
    /// </summary>
    private static void BuildRideBoundary(MinatoRoute route, Transform parent)
    {
        var root = new GameObject("Ride Boundary").transform;
        root.SetParent(parent, false);

        const float SegmentM = 20f;      // one collider per 20 m of shoulder. PROVISIONAL.
        const float LateralM = 4.45f;    // centre of the barrier from the centreline
        const float ThickM = 0.30f;
        const float HeightM = 1.10f;     // guardrail height from the brief
        const float MaxSagittaM = 0.22f; // curve bow a straight segment may carry. PROVISIONAL.

        int bays = 0;
        float worstSag = 0f, shortest = SegmentM;
        float d = 0f;
        while (d < route.Length - 2f)
        {
            // Adaptive segment length. Sagitta compensation alone still left nine segments
            // clipping the lane on the tightest switchbacks, because a long straight box also
            // swings its ENDS inward. Where the centreline bows more than the allowance, split
            // the segment down until a straight box genuinely fits the curve.
            float seg = Mathf.Min(SegmentM, route.Length - d - 0.5f);
            float sag = Sagitta(route, d, seg);
            for (int attempt = 0; attempt < 4 && sag > MaxSagittaM && seg > 1.4f; attempt++)
            {
                seg *= 0.5f;
                sag = Sagitta(route, d, seg);
            }
            shortest = Mathf.Min(shortest, seg);
            worstSag = Mathf.Max(worstSag, sag);

            int i = route.IndexAt(d);
            int j = route.IndexAt(Mathf.Min(d + seg, route.Length - 1f));
            var a = route.Position[i];
            var b = route.Position[j];
            var fwd = b - a;
            if (fwd.sqrMagnitude < 1e-4f) { d += seg; continue; }
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            int mid = route.IndexAt(d + seg * 0.5f);
            var c = route.Position[mid];
            var side = route.SideFlat(mid);

            for (int s = -1; s <= 1; s += 2)
            {
                var go = new GameObject(s < 0 ? "RideBoundary_Rail_L" : "RideBoundary_Rail_R");
                go.transform.SetParent(root, false);
                go.transform.position = c + side * (s * (LateralM + sag))
                                        + Vector3.up * (HeightM * 0.5f);
                go.transform.rotation = rot;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(ThickM, HeightM, fwd.magnitude + 0.15f);
                go.isStatic = true;
                bays++;
            }
            d += seg;
        }
        Debug.Log($"[minato] ride boundary {bays} barrier colliders " +
                  $"(segments {shortest:F2}-{SegmentM:F0} m, inner face {LateralM - ThickM * 0.5f:F2} m " +
                  $"from the centreline against a {RoadHalfWidth:F2} m lane half-width, " +
                  $"worst residual sagitta {worstSag * 1000f:F0} mm)");
    }

    /// <summary>How far the centreline bows away from the straight chord over a segment (m).</summary>
    private static float Sagitta(MinatoRoute route, float d, float seg)
    {
        var a = route.Position[route.IndexAt(d)];
        var b = route.Position[route.IndexAt(Mathf.Min(d + seg, route.Length - 1f))];
        var side = route.SideFlat(route.IndexAt(d + seg * 0.5f));
        float sag = 0f;
        for (float t = 0.05f; t < 0.98f; t += 0.05f)
        {
            var s = route.Position[route.IndexAt(d + seg * t)];
            var chord = Vector3.Lerp(a, b, t);
            sag = Mathf.Max(sag, Mathf.Abs((s.x - chord.x) * side.x + (s.z - chord.z) * side.z));
        }
        return sag;
    }

    private static Mesh RoadRibbon(MinatoRoute r, int start, int end)
    {
        int n = end - start + 1;
        var verts = new Vector3[n * 4];
        var uvs = new Vector2[n * 4];
        float w = RoadHalfWidth, sw = RoadHalfWidth + ShoulderW;
        for (int k = 0; k < n; k++)
        {
            int i = start + k;
            var p = r.Position[i];
            var s = r.SideFlat(i);
            float d = r.Distance[i];
            verts[k * 4 + 0] = p - s * sw + Vector3.down * 0.16f;
            verts[k * 4 + 1] = p - s * w;
            verts[k * 4 + 2] = p + s * w;
            verts[k * 4 + 3] = p + s * sw + Vector3.down * 0.16f;
            for (int q = 0; q < 4; q++)
                // V was raw metres, giving one full texture tile per metre - the heavy horizontal
                // banding in the first renders. The period is matched to the carriageway width so
                // asphalt texels stay square. PROVISIONAL.
            {
                float lateral = q == 0 ? -sw : q == 1 ? -w : q == 2 ? w : sw;
                uvs[k * 4 + q] = new Vector2((lateral + sw) * RoadUvPerMetre,
                                             d * RoadUvPerMetre);
            }
        }
        var tris = new List<int>(n * 18);
        for (int k = 0; k < n - 1; k++)
            for (int q = 0; q < 3; q++)
            {
                int a = k * 4 + q, b = a + 1, c = a + 4, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(b);
            }
        var mesh = new Mesh { name = $"Minato_Road_{start:D5}" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.uv = uvs; mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static void BuildRoadMarkings(MinatoRoute route, Transform parent)
    {
        var root = new GameObject("Road Markings").transform;
        root.SetParent(parent, false);

        Material Paint(string name, Color color, Color worn)
        {
            var m = LoadOrCreate(name, "MapleRide/HDRP/RoadPaint");
            m.SetColor("_Color", color);
            m.SetColor("_WornColor", worn);
            m.SetColor("_GrimeColor", new Color(0.34f, 0.35f, 0.35f));
            m.SetColor("_ShadeColor", new Color(0.62f, 0.66f, 0.72f));
            m.SetFloat("_WearAmount", 0.30f);
            m.SetFloat("_WearScale", 0.34f);
            m.SetFloat("_EdgeGrime", 0.32f);
            m.SetFloat("_MetresPerV", 1f);
            m.SetFloat("_BeadSparkle", 0.18f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        var white = Paint("Minato_RoadPaint_White", new Color(0.86f, 0.88f, 0.87f),
                          new Color(0.42f, 0.44f, 0.45f));
        // REDESIGN (every Minato concept target): WHITE dashed centre line instead of the yellow
        // one, and a painted BLUE BIKE LANE along the rider's right edge, bounded by a second
        // solid white line - the lane language of a modern Japanese waterfront road.
        var blue = Paint("Minato_RoadPaint_BikeLane", new Color(0.17f, 0.44f, 0.80f),
                         new Color(0.24f, 0.36f, 0.52f));

        int perChunk = Mathf.Max(8, Mathf.RoundToInt(ChunkM / 3f));
        int chunk = 0;
        for (int start = 0; start < route.Count - 1; start += perChunk, chunk++)
        {
            int end = Mathf.Min(start + perChunk, route.Count - 1);
            AddMesh(root, $"Marking_EdgeL_{chunk:D3}",
                    StripeMesh(route, start, end, -RoadHalfWidth + 0.17f, 0.10f, false),
                    white, false);
            AddMesh(root, $"Marking_EdgeR_{chunk:D3}",
                    StripeMesh(route, start, end, RoadHalfWidth - 0.17f, 0.10f, false),
                    white, false);
            AddMesh(root, $"Marking_Centre_{chunk:D3}",
                    StripeMesh(route, start, end, 0f, 0.12f, true),
                    white, false);
            AddMesh(root, $"Marking_BikeLane_{chunk:D3}",
                    StripeMesh(route, start, end, RoadHalfWidth - 0.85f, 1.18f, false),
                    blue, false);
            AddMesh(root, $"Marking_BikeLaneLine_{chunk:D3}",
                    StripeMesh(route, start, end, RoadHalfWidth - 1.52f, 0.10f, false),
                    white, false);
        }
    }

    private static Mesh StripeMesh(MinatoRoute route, int start, int end, float lateral,
                                   float width, bool dashed)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        for (int i = start; i < end; i++)
        {
            float dm = (route.Distance[i] + route.Distance[i + 1]) * 0.5f;
            if (dashed && dm % 12f > 6f) continue;
            var a = route.Position[i] + route.SideFlat(i) * lateral + Vector3.up * 0.018f;
            var b = route.Position[i + 1] + route.SideFlat(i + 1) * lateral + Vector3.up * 0.018f;
            var sa = route.SideFlat(i) * (width * 0.5f);
            var sb = route.SideFlat(i + 1) * (width * 0.5f);
            int q = verts.Count;
            verts.Add(a - sa); verts.Add(a + sa); verts.Add(b + sb); verts.Add(b - sb);
            float v0 = route.Distance[i], v1 = route.Distance[i + 1];
            uvs.Add(new Vector2(0f, v0)); uvs.Add(new Vector2(1f, v0));
            uvs.Add(new Vector2(1f, v1)); uvs.Add(new Vector2(0f, v1));
            tris.Add(q); tris.Add(q + 2); tris.Add(q + 1);
            tris.Add(q); tris.Add(q + 3); tris.Add(q + 2);
        }
        var mesh = new Mesh { name = $"Minato_Stripe_{start:D5}_{lateral:F2}" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // =================================================================== bridge

    [Serializable]
    private class BridgeSpec
    {
        public float spanLength, archRise, ribOffsetSpring, ribOffsetCrown;
        public float ribWidthSpring, ribWidthCrown, hangerSpacing, deckHalfWidth;
        public float deckSlabDepth, girderDepth, deckBayLength, railPostSpacing, lampSpacing;
        public float pierCapDepth, pierShaftLength, pierBaseDepth, parapetHeight, railHeight;
    }

    private static void BuildBridge(MinatoRoute route, Transform chapter)
    {
        var specText = AssetDatabase.LoadAssetAtPath<TextAsset>(SpecPath);
        if (specText == null) { Debug.LogError($"[minato] {SpecPath} missing"); return; }
        var K = JsonUtility.FromJson<BridgeSpec>(specText.text);

        var parent = new GameObject("Bridge").transform;
        parent.SetParent(chapter, false);

        var rib = Model("Minato_Bridge_ArchRibMain");
        var hanger = Model("Minato_Bridge_Hanger");
        var brace = Model("Minato_Bridge_CrossBrace");
        var diag = Model("Minato_Bridge_BraceDiagonal");
        var pedestal = Model("Minato_Bridge_ArchPedestal");
        var cap = Model("Minato_Bridge_PierCap");
        var shaft = Model("Minato_Bridge_PierShaft");
        var pbase = Model("Minato_Bridge_PierBase");
        var guard = Model("Minato_Bridge_Guardrail");
        var lamp = Model("Minato_Bridge_Lamp");
        var joint = Model("Minato_Bridge_Joint");
        var walk = Model("Minato_Bridge_Walkway");
        var drain = Model("Minato_Bridge_Drainage");
        var plate = Model("Minato_Bridge_ConnectionPlate");

        var steel = SteelMaterial();
        var steelDark = CelMaterial("Minato_SteelDark", new Color(0.44f, 0.46f, 0.49f),
                                    gloss: 0.30f, spec: 0.22f, rim: 0.35f, shade: GroundShade);
        var concrete = ConcreteMaterial();

        float spanLen = K.spanLength;
        int spanCount = Mathf.FloorToInt((BridgeEndM - BridgeStartM) / spanLen);
        var rng = new System.Random(20260922);
        int placed = 0;

        // Culling chunks: one GameObject per span keeps the 66-span crossing occlusion-friendly
        // and gives the renderer a natural per-span bounds to frustum-cull.
        for (int s = 0; s < spanCount; s++)
        {
            float d0 = BridgeStartM + s * spanLen;
            float dc = d0 + spanLen * 0.5f;
            int ic = route.IndexAt(dc);
            var pc = route.Position[ic];
            var fwd = route.Tangent[ic];
            var side = route.SideFlat(ic);
            var rot = Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z).normalized, Vector3.up);

            var span = new GameObject($"Span_{s:D2}").transform;
            span.SetParent(parent, false);
            span.position = pc;
            span.rotation = rot;

            // Subtle per-span variation so 66 identical spans do not read as a photocopy.
            float tint = 0.91f + (float)rng.NextDouble() * 0.17f;
            // A repeating 66-copy arch was readable from kilometres away. A slow 8-span rhythm
            // gives the crossing hero spans, secondary spans and approach spans without changing
            // the 120 m engineering module or any deck joint.
            float archScale = 0.88f + 0.22f * Mathf.SmoothStep(
                0f, 1f, 0.5f + 0.5f * Mathf.Sin((s % 8) / 8f * Mathf.PI * 2f));

            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var r = Inst(rib, span, new Vector3(0f, 0f, 0f), Quaternion.identity, steel);
                r.transform.localScale = new Vector3(sgn, archScale, 1f);
                r.name = $"ArchRib_{(sgn < 0 ? "L" : "R")}";
                Vary(r, tint);
                placed++;

                for (float z = -spanLen * 0.5f + K.hangerSpacing;
                         z < spanLen * 0.5f - K.hangerSpacing * 0.5f; z += K.hangerSpacing)
                {
                    float f = (z + spanLen * 0.5f) / spanLen;
                    float yA = K.archRise * (1f - Mathf.Pow(2f * f - 1f, 2f)) - 2f;
                    if (yA <= 2f) continue;
                    float lean = Mathf.Abs(2f * f - 1f);
                    float xA = sgn * (K.ribOffsetCrown + (K.ribOffsetSpring - K.ribOffsetCrown) * lean);
                    var h = Inst(hanger, span, new Vector3(xA, 0f, z), Quaternion.identity, steelDark);
                    h.transform.localScale = new Vector3(1f, (yA * archScale - 1.1f) / 10f, 1f);
                    h.name = $"Hanger_{(sgn < 0 ? "L" : "R")}_{z:F0}";
                    placed++;
                }
            }

            // crown bracing + diagonals that follow the rib curve
            float eCrown = K.ribOffsetCrown - K.ribWidthCrown * 0.25f;
            float step = spanLen / 12f;
            int bi = 0;
            for (float z = -spanLen * 0.5f + spanLen * 0.14f;
                     z <= spanLen * 0.5f - spanLen * 0.14f + 0.01f; z += step, bi++)
            {
                float f = (z + spanLen * 0.5f) / spanLen;
                float y = K.archRise * (1f - Mathf.Pow(2f * f - 1f, 2f)) - 2f;
                float lean = Mathf.Abs(2f * f - 1f);
                float xr = K.ribOffsetCrown + (K.ribOffsetSpring - K.ribOffsetCrown) * lean;
                float wr = K.ribWidthCrown + (K.ribWidthSpring - K.ribWidthCrown) * lean;
                float e = xr - wr * 0.25f;

                y *= archScale;
                var br = Inst(brace, span, new Vector3(0f, y, z), Quaternion.identity, steel);
                br.transform.localScale = new Vector3(e / eCrown, 1f, 1f);
                br.name = $"CrossBrace_{bi}";
                Vary(br, tint);
                placed++;

                float z2 = z + step;
                if (z2 > spanLen * 0.5f - spanLen * 0.14f + 0.01f) continue;
                float f2 = (z2 + spanLen * 0.5f) / spanLen;
                float y2 = (K.archRise * (1f - Mathf.Pow(2f * f2 - 1f, 2f)) - 2f) * archScale;
                float lean2 = Mathf.Abs(2f * f2 - 1f);
                float xr2 = K.ribOffsetCrown + (K.ribOffsetSpring - K.ribOffsetCrown) * lean2;
                float wr2 = K.ribWidthCrown + (K.ribWidthSpring - K.ribWidthCrown) * lean2;
                float e2 = xr2 - wr2 * 0.25f;
                PlaceMember(diag, span, new Vector3(-e, y, z), new Vector3(e2, y2, z2), steel, $"Diag_{bi}a");
                PlaceMember(diag, span, new Vector3(e, y, z), new Vector3(-e2, y2, z2), steel, $"Diag_{bi}b");
                placed += 2;
            }

            // springing pedestals
            for (int sgn = -1; sgn <= 1; sgn += 2)
                foreach (float zs in new[] { -spanLen * 0.5f, spanLen * 0.5f })
                {
                    var pd = Inst(pedestal, span, new Vector3(sgn * K.ribOffsetSpring, 0f, zs),
                                  Quaternion.identity, concrete);
                    pd.transform.localScale = new Vector3(1f, 1f, zs > 0 ? -1f : 1f);
                    pd.name = $"Pedestal_{(sgn < 0 ? "L" : "R")}_{(zs > 0 ? "N" : "S")}";
                    placed++;
                }

            // pier under the span joint
            float soffit = -(K.deckSlabDepth + K.girderDepth);
            var pier = new GameObject($"Pier_{s:D2}").transform;
            pier.SetParent(span, false);
            pier.localPosition = new Vector3(0f, soffit, -spanLen * 0.5f);
            Inst(cap, pier, Vector3.zero, Quaternion.identity, concrete).name = "PierCap";
            float top = pc.y + soffit - K.pierCapDepth - 0.55f;
            float baseTop = SeaLevelY + K.pierBaseDepth - 2f;
            int nShaft = Mathf.Max(1, Mathf.CeilToInt((top - baseTop) / K.pierShaftLength));
            for (int q = 0; q < nShaft; q++)
            {
                var sh = Inst(shaft, pier, new Vector3(0f, -K.pierCapDepth - q * K.pierShaftLength, 0f),
                              Quaternion.identity, concrete);
                sh.name = $"PierShaft_{q}";
            }
            Inst(pbase, pier, new Vector3(0f, baseTop - pc.y - soffit, 0f), Quaternion.identity,
                 concrete).name = "PierBase";
            Inst(joint, span, new Vector3(0f, 0f, -spanLen * 0.5f), Quaternion.identity,
                 steelDark).name = "ExpansionJoint";
            placed += nShaft + 3;

            // deck furniture along the span
            float px = K.deckHalfWidth - 0.8f;
            for (float z = -spanLen * 0.5f; z < spanLen * 0.5f; z += K.railPostSpacing)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var g = Inst(guard, span, new Vector3(sgn * px, K.parapetHeight, z),
                                 Quaternion.identity, steel);
                    g.name = $"Guardrail_{(sgn < 0 ? "L" : "R")}_{z:F0}";
                }
            for (float z = -spanLen * 0.5f; z < spanLen * 0.5f; z += K.deckBayLength)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var wk = Inst(walk, span, new Vector3(sgn * (K.deckHalfWidth), 0f, z),
                                  Quaternion.identity, steelDark);
                    wk.transform.localScale = new Vector3(sgn, 1f, 1f);
                    wk.name = $"Walkway_{(sgn < 0 ? "L" : "R")}_{z:F0}";
                }
            for (float z = -spanLen * 0.5f; z < spanLen * 0.5f; z += K.deckBayLength * 2f)
                Inst(drain, span, new Vector3(K.deckHalfWidth - 0.4f, 0f, z), Quaternion.identity,
                     steelDark).name = $"Drain_{z:F0}";
            for (float z = -spanLen * 0.5f; z < spanLen * 0.5f; z += K.lampSpacing)
                Inst(lamp, span, new Vector3(px, K.parapetHeight, z), Quaternion.identity,
                     steelDark).name = $"Lamp_{z:F0}";
            for (int q = 0; q < 4; q++)
                Inst(plate, span, new Vector3(K.ribOffsetSpring * 0.8f, 3f + q * 4f,
                                              -spanLen * 0.35f + q * 6f),
                     Quaternion.identity, steelDark).name = $"Plate_{q}";
        }
        Debug.Log($"[minato] bridge {spanCount} spans of {spanLen:N0} m " +
                  $"({spanCount * spanLen / 1000f:N1} km), {placed} structural instances");
    }

    private static void PlaceMember(GameObject src, Transform parent, Vector3 a, Vector3 b,
                                    Material mat, string name, float nominal = 10f)
    {
        var d = b - a;
        var go = Inst(src, parent, a, Quaternion.FromToRotation(Vector3.forward, d.normalized), mat);
        go.transform.localScale = new Vector3(1f, 1f, d.magnitude / nominal);
        go.name = name;
    }

    // =================================================================== scatter
    //
    // Reuses Sakura Pass / Shiosai Coast production geometry. Density follows Sakura's own
    // ScatterDressing shape: multiple slots per station in depth bands, species picked per
    // instance, with rejection on slope and on the carriageway.

    private sealed class InstancedBuild
    {
        public Mesh mesh;
        public int subMesh;
        public Material material;
        public int cell;
        public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        public Bounds bounds;
        public bool hasBounds;
    }

    private static readonly Dictionary<string, InstancedBuild> InstancedBuilds =
        new Dictionary<string, InstancedBuild>();

    private static void QueueInstanced(GameObject src, Vector3 position, Quaternion rotation,
                                       Vector3 scale, Material primary, Material secondary,
                                       int cell)
    {
        if (src == null) return;
        var renderers = src.GetComponentsInChildren<MeshRenderer>(true);
        bool hasLod0 = false;
        foreach (var r in renderers) hasLod0 |= r.name.EndsWith("_LOD0", StringComparison.Ordinal);

        Matrix4x4 root = Matrix4x4.TRS(position, rotation, scale);
        foreach (var mr in renderers)
        {
            if (hasLod0 && !mr.name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
            if (mr.name.EndsWith("_LOD1", StringComparison.Ordinal) ||
                mr.name.EndsWith("_LOD2", StringComparison.Ordinal) ||
                mr.name.EndsWith("_LOD3", StringComparison.Ordinal))
                continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            Matrix4x4 relative = src.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;
            Matrix4x4 matrix = root * relative;
            int subCount = Mathf.Max(1, mf.sharedMesh.subMeshCount);
            for (int sub = 0; sub < subCount; sub++)
            {
                var imported = mr.sharedMaterials.Length == 0 ? null :
                               mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)];
                string n = imported != null ? imported.name.ToLowerInvariant() : "";
                bool alt = n.Contains("bark") || n.Contains("trunk") || n.Contains("wood") ||
                           n.Contains("branch") || n.Contains("stem");
                var material = alt && secondary != null ? secondary : primary;
                if (material == null) material = imported;
                if (material == null) continue;
                material.enableInstancing = true;

                string key = $"{cell}|{mf.sharedMesh.GetInstanceID()}|{sub}|{material.GetInstanceID()}";
                if (!InstancedBuilds.TryGetValue(key, out var batch))
                {
                    batch = new InstancedBuild
                    {
                        mesh = mf.sharedMesh,
                        subMesh = sub,
                        material = material,
                        cell = cell,
                    };
                    InstancedBuilds.Add(key, batch);
                }
                batch.matrices.Add(matrix);

                var b = TransformBounds(mf.sharedMesh.bounds, matrix);
                if (batch.hasBounds) batch.bounds.Encapsulate(b);
                else { batch.bounds = b; batch.hasBounds = true; }
            }
        }
    }

    private static Bounds TransformBounds(Bounds local, Matrix4x4 m)
    {
        var c = m.MultiplyPoint3x4(local.center);
        var ex = m.MultiplyVector(new Vector3(local.extents.x, 0f, 0f));
        var ey = m.MultiplyVector(new Vector3(0f, local.extents.y, 0f));
        var ez = m.MultiplyVector(new Vector3(0f, 0f, local.extents.z));
        var e = new Vector3(Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x),
                            Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                            Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z));
        return new Bounds(c, e * 2f);
    }

    private static int FlushInstanced(Transform parent)
    {
        int batches = 0, instances = 0;
        foreach (var kv in InstancedBuilds)
        {
            var b = kv.Value;
            if (b.matrices.Count == 0) continue;
            var go = new GameObject($"GroundCover_Cell_{b.cell:D3}_{batches:D3}");
            go.transform.SetParent(parent, false);
            var draw = go.AddComponent<MinatoInstancedBatch>();
            draw.mesh = b.mesh;
            draw.subMeshIndex = b.subMesh;
            draw.material = b.material;
            draw.matrices = b.matrices.ToArray();
            draw.worldBounds = b.bounds;
            draw.maxDistanceM = 720f;
            draw.castShadows = false;
            go.isStatic = true;
            instances += b.matrices.Count;
            batches++;
        }
        InstancedBuilds.Clear();
        Debug.Log($"[minato] ground-cover architecture: {instances:N0} transforms in {batches:N0} " +
                  "GPU-instanced route-cell batches (no per-tuft GameObjects/LODGroups)");
        return batches;
    }

    // Shiosai's production villages never paint every imported house with one white material.
    // These muted coastal families keep the authored facade texture, but vary value, temperature
    // and roof silhouette per lot so repeated geometry does not read as a row of white blocks.
    private static Material[] SettlementFacades()
    {
        var tex = Tex(ShiosaiTex, "Shiosai_HarbourFacade_Albedo_v2.png");
        return new[]
        {
            CelMaterial("Minato_Facade_Ivory",  new Color(0.92f, 0.89f, 0.80f), 0.14f, 0.08f, 0.30f, tex),
            CelMaterial("Minato_Facade_Sage",   new Color(0.70f, 0.78f, 0.67f), 0.14f, 0.08f, 0.30f, tex),
            CelMaterial("Minato_Facade_Slate",  new Color(0.63f, 0.72f, 0.78f), 0.14f, 0.08f, 0.30f, tex),
            CelMaterial("Minato_Facade_Ochre",  new Color(0.74f, 0.72f, 0.64f), 0.14f, 0.08f, 0.30f, tex),
            CelMaterial("Minato_Facade_Coral",  new Color(0.82f, 0.62f, 0.57f), 0.14f, 0.08f, 0.30f, tex),
            CelMaterial("Minato_Facade_Indigo", new Color(0.50f, 0.61f, 0.72f), 0.14f, 0.08f, 0.30f, tex),
        };
    }

    private static Material[] SettlementRoofs()
    {
        var tex = Tex(ShiosaiTex, "Shiosai_HarbourRoof_Albedo_v2.png");
        return new[]
        {
            CelMaterial("Minato_Roof_Slate", new Color(0.27f, 0.31f, 0.35f), 0.18f, 0.10f, 0.28f, tex),
            CelMaterial("Minato_Roof_Navy",  new Color(0.20f, 0.28f, 0.36f), 0.18f, 0.10f, 0.28f, tex),
            CelMaterial("Minato_Roof_Red",   new Color(0.45f, 0.22f, 0.18f), 0.18f, 0.10f, 0.28f, tex),
            CelMaterial("Minato_Roof_Green", new Color(0.24f, 0.36f, 0.31f), 0.18f, 0.10f, 0.28f, tex),
            CelMaterial("Minato_Roof_Metal", new Color(0.43f, 0.47f, 0.49f), 0.22f, 0.12f, 0.25f, tex),
        };
    }

    private static void ScatterDressing(MinatoRoute route, Transform[] chapters)
    {
        var rng = new System.Random(20260922);

        var bark = CelMaterial("Minato_Bark", Color.white, gloss: 0.10f, spec: 0.05f, rim: 0.40f,
                               texture: Tex(ShiosaiTex, "Shiosai_Bark_Albedo.png"));
        // Tints are near-white multipliers so the production albedo supplies the colour; the
        // earlier dark tints (0.30,0.44,0.34) multiplied an already mid-dark leaf texture and
        // crushed the canopy to black. PROVISIONAL.
        var needle = FoliageMaterial("Minato_Needle", new Color(0.78f, 0.88f, 0.76f),
                                     Tex(ShiosaiTex, "Shiosai_Needle_Albedo.png"), 0.22f);
        var leaf = FoliageMaterial("Minato_Leaf", new Color(0.88f, 0.95f, 0.80f),
                                   Tex(ShiosaiTex, "Shiosai_Leaf_Albedo_HQ.png"), 0.24f);
        var rock = CelMaterial("Minato_Rock", new Color(0.62f, 0.60f, 0.57f), gloss: 0.10f,
                               spec: 0.06f, rim: 0.35f,
                               texture: Tex(ShiosaiTex, "Shiosai_Rock_Albedo.png"), shade: GroundShade);
        var facades = SettlementFacades();
        var roofs = SettlementRoofs();

        // (glb, material, minScale, maxScale)
        var pines = new[] { Glb(ShiosaiGlb, "Shiosai_Pine"), Glb(ShiosaiGlb, "Shiosai_Pine_B"),
                            Glb(ShiosaiGlb, "Shiosai_Pine"), Glb(ShiosaiGlb, "Shiosai_Pine_B") };
        var broad = new[] { Glb(ShiosaiGlb, "Shiosai_Broadleaf"), Glb(ShiosaiGlb, "Shiosai_Broadleaf_B"),
                            Glb(ShiosaiGlb, "Shiosai_Broadleaf"), Glb(ShiosaiGlb, "Shiosai_Broadleaf_B") };
        var shrubs = new[] { Glb(ShiosaiGlb, "Shiosai_Hydrangea_C"), Glb(ShiosaiGlb, "Shiosai_Hydrangea"),
                             Glb(ShiosaiGlb, "Shiosai_Hydrangea"), Glb(ShiosaiGlb, "Shiosai_Hydrangea_B") };
        var tufts = new[] { Glb(SakuraGlb, "SakuraPass_Grass_Tuft"), Glb(SakuraGlb, "SakuraPass_Fern_Clump") };
        var rocks = new[] { Glb(SakuraGlb, "SakuraPass_Rock_Cluster_A"), Glb(SakuraGlb, "SakuraPass_Rock_Cluster_B"),
                            Glb(SakuraGlb, "SakuraPass_Stone_Scatter_A"), Glb(SakuraGlb, "SakuraPass_Stone_Scatter_B") };
        var houses = new[] { Glb(ShiosaiGlb, "Shiosai_HarbourHouse_A"), Glb(ShiosaiGlb, "Shiosai_HarbourHouse_B"),
                             Glb(ShiosaiGlb, "Shiosai_HarbourHouse_C"), Glb(ShiosaiGlb, "Shiosai_House_A"),
                             Glb(ShiosaiGlb, "Shiosai_House_B"), Glb(ShiosaiGlb, "Shiosai_House_C") };

        var flora = new GameObject("Flora").transform; flora.SetParent(chapters[4], false);
        var props = new GameObject("Settlement").transform; props.SetParent(chapters[3], false);
        var coast = new GameObject("Coast").transform; coast.SetParent(chapters[0], false);

        int trees = 0, built = 0, stones = 0;
        for (int i = 4; i < route.Count - 4; i++)
        {
            float d = route.Distance[i];
            // Scatter used to be suppressed across the whole d in (BridgeStart-120, BridgeEnd+120)
            // band. That band starts only 30 m in front of the chapter-02 quality-gate camera, so
            // the entire forward view of the hero approach shot was deliberately bare - which is
            // what read as "invisible scatter" in the first renders even though 9,279 instances
            // were placed and enabled. The correct rule is a PER-POSITION water test below, so the
            // land-side approach embankments get dressed and only actual water stays clear.

            var p = route.Position[i];
            var side = route.SideFlat(i);
            int ch = ChapterOf(d);
            Transform bucket = ch == 0 ? coast : ch == 3 ? props : flora;

            // 6 slots per station in 3 depth bands, Sakura's banding shape.
            for (int slot = 0; slot < 6; slot++)
            {
                float chance = ch == 0 ? 0.20f : ch == 3 ? 0.42f : 0.62f;
                if (rng.NextDouble() > chance) continue;
                float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                float band = slot < 2 ? 1f : slot < 4 ? 2f : 3f;
                float off = (9f + band * 17f) + (float)rng.NextDouble() * 13f;
                var q = p + side * sgn * off;
                float y = GroundAt(route, q.x, q.z);
                if (y < SeaLevelY + 1.2f) continue;                      // in the sea
                if (Mathf.Abs(y - p.y) > 44f) continue;                  // cliff face
                var pos = new Vector3(q.x, y, q.z);
                var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                GameObject src; Material mat; Material alt; float sc; bool isHouse = false;
                double pick = rng.NextDouble();
                if (ch == 3 && pick < 0.34)
                {
                    src = houses[rng.Next(houses.Length)];
                    mat = facades[rng.Next(facades.Length)];
                    alt = roofs[rng.Next(roofs.Length)];
                    sc = 0.9f + (float)rng.NextDouble() * 0.4f;
                    isHouse = true;
                }
                else if (pick < 0.44)
                { src = pines[rng.Next(pines.Length)]; mat = needle; alt = bark; sc = 0.85f + (float)rng.NextDouble() * 0.7f; }
                else if (pick < 0.70)
                { src = broad[rng.Next(broad.Length)]; mat = leaf; alt = bark; sc = 0.85f + (float)rng.NextDouble() * 0.6f; }
                else if (pick < 0.86)
                { src = shrubs[rng.Next(shrubs.Length)]; mat = leaf; alt = bark; sc = 0.9f + (float)rng.NextDouble() * 0.6f; }
                else if (pick < 0.95)
                { src = tufts[rng.Next(tufts.Length)]; mat = leaf; alt = bark; sc = 1.0f + (float)rng.NextDouble() * 0.8f; }
                else
                { src = rocks[rng.Next(rocks.Length)]; mat = rock; alt = bark; sc = 0.8f + (float)rng.NextDouble() * 1.1f; }
                if (src == null) continue;

                var go = Inst(src, bucket, Vector3.zero, rot, mat, alt);
                go.transform.position = pos;
                go.transform.localScale = isHouse
                    ? new Vector3(sc * (0.84f + (float)rng.NextDouble() * 0.34f),
                                  sc * (0.82f + (float)rng.NextDouble() * 0.55f),
                                  sc * (0.82f + (float)rng.NextDouble() * 0.38f))
                    : Vector3.one * sc;
                go.isStatic = true;
                if (isHouse) built++; else if (mat == rock) stones++; else trees++;
            }
        }
        Debug.Log($"[minato] scatter {trees} flora, {built} buildings, {stones} rock clusters");
        ClearPortScatter(route, coast);

        if (!ShoresReplacesLegacy) BuildLighthousePromontory(route, chapters[3], rock);
        if (!SkylineReplacesLegacy) BuildPortCity(route, chapters, houses, rock, bark, leaf);
        if (!SkylineReplacesLegacy) BuildAuthoredPortDistrict(route, chapters[0]);
        // The port APRON (ground surface) is built inside BuildPortInfrastructure too; the
        // harbor workstream must keep providing it (and its colliders) if it replaces this.
        if (!HarborReplacesLegacy) BuildPortInfrastructure(route, chapters[0]);
        if (!ShoresReplacesLegacy) BuildFarShoreSettlement(route, chapters[3], houses, rock, bark, leaf);
        if (!HarborReplacesLegacy) BuildOceanTraffic(route, chapters[2]);
        BuildRoadEngineering(route, chapters, rock);
        VergeDressing(route, chapters, leaf, bark, rock);
        // --- bright city pass. Order matters: the beach must be laid before the crowds so the
        // sand surface exists, and both come after the port so they are not cleared by
        // ClearPortScatter or buried by the apron.
        if (!SkylineReplacesLegacy) BuildModernSkyline(route, chapters[0]);
        BuildCityBeach(route, chapters[0]);
        BuildCityLife(route, chapters[0]);
        // Boulevard dressing LAST in chapter 1: it mounts banners onto the lamp instances that
        // BuildRoadFurniture and BuildPortCity have already placed, and grounds its planting on
        // the port apron colliders built by BuildPortInfrastructure.
        BuildBoulevardDressing(route, chapters[0], chapters[0].parent);
        // Market district AFTER the boulevard dressing: it rejects candidates against the real
        // renderer bounds of the waterfront blocks, street trees and the M1 median planting, so
        // all of those must already exist when it runs.
        BuildMarketDistrict(route, chapters[0], chapters[0].parent);
        Debug.Log($"[minato] LOD sourcing: {_lodHits} loads from Minato-derived LOD assets, " +
                  $"{_lodMisses} fell back to a single-LOD source GLB");
    }

    /// <summary>
    /// Working-port infrastructure for chapter 01: container cranes, warehouses, crate stacks,
    /// bollards on the quay, and the two distant skyline landmarks. These are Minato's own
    /// authored prop family, which is BACKGROUND-tier geometry - so everything here is placed
    /// at 90 m or further from the carriageway, beyond the near/mid band that the Sakura art
    /// bar governs, where the silhouette is what reads. Nothing from this family is placed in
    /// the cycling corridor.
    /// </summary>
    private static void BuildPortInfrastructure(MinatoRoute route, Transform chapter)
    {
        var parent = new GameObject("Port Infrastructure").transform;
        parent.SetParent(chapter, false);

        var steel = SteelMaterial();
        var concrete = ConcreteMaterial();
        var facades = SettlementFacades();
        var roofs = SettlementRoofs();
        var apronMat = CelMaterial("Minato_PortApron", new Color(0.70f, 0.71f, 0.68f),
                                   gloss: 0.10f, spec: 0.06f, rim: 0.20f,
                                   texture: Tex(MapleTex, "MapleCity_Pavement_Albedo.png"),
                                   shade: GroundShade);
        // The seaward apron is SPLIT around the public city beach window: the working dock
        // pavement stops at BeachStartM and resumes at BeachEndM, so the bay opens onto sand
        // instead of being walled off by a concrete quay.
        var seaApronA = AddMesh(parent, "Connected Port Seaward Apron",
                                PortApronMesh(route, 100f, BeachStartM, 6.2f, 420f),
                                apronMat, false);
        seaApronA.isStatic = true;
        var seaApronB = AddMesh(parent, "Connected Port Seaward Apron East",
                                PortApronMesh(route, BeachEndM, 2295f, 6.2f, 420f),
                                apronMat, false);
        seaApronB.isStatic = true;
        // Landward apron EXTENDED from 520 m to 1360 m so the city plaza reaches out UNDER the
        // skyline district (towers stand at 340-1320 m). The old 520 m edge met the green terrain
        // humps beneath the towers in a hard seam that left the tower bases looking disconnected;
        // the plaza now grounds them and the seam retreats behind the skyline.
        var landApron = AddMesh(parent, "Connected Port Landward Apron",
                                PortApronMesh(route, 100f, 2295f, -600f, -6.2f),   // condensed (was -1360)
                                apronMat, false);
        landApron.isStatic = true;

        // Give the aprons mesh colliders so BuildCityLife can raycast the TRUE walkable surface
        // per figure (apron pavement where it is the top surface, terrain grass where the ground
        // arches above it). Without this a blind +0.38 m apron lift floated the sea-side cyclists
        // over the grass verge with detached shadows.
        foreach (var ap in new[] { seaApronA, seaApronB, landApron })
        {
            var mc = ap.GetComponent<MeshCollider>();
            if (mc == null) mc = ap.AddComponent<MeshCollider>();
            mc.sharedMesh = ap.GetComponent<MeshFilter>().sharedMesh;
        }

        var crane = Model("Minato_Port_ContainerCrane");
        var shed = Model("Minato_Port_Warehouse");
        var crates = Model("Minato_Port_CrateStack");
        var bollard = Model("Minato_Port_Bollard");
        var wheel = Model("Minato_Port_FerrisWheel");
        var ferry = Model("Minato_Sea_Ferry");
        var cargo = Model("Minato_Sea_CargoShip");

        var rng = new System.Random(5150);
        int cranes = 0, sheds = 0, stacks = 0, bollards = 0, vessels = 0;

        // The seaward quay line: march out until the ground reaches the water, then work back.
        for (float d = 150f; d < 2200f; d += 42f)
        {
            if (InBeach(d)) continue;              // the beach window is public sand, not dock
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * SeaSideSign(route, i);
            int j = route.IndexAt(Mathf.Min(d + 42f, route.Length - 1f));
            var fwd = (route.Position[j] - p); fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) continue;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            float quay = -1f;
            for (float off = 90f; off < 660f; off += 8f)
            {
                var q = p + side * off;
                if (GroundAt(route, q.x, q.z) < SeaLevelY + 1.0f) { quay = off; break; }
            }

            if (quay < 0f) continue;

            // Cranes stand on the quay edge, facing the water.
            if (crane != null && rng.NextDouble() < 0.60)
            {
                var q = p + side * (quay - 14f);
                var go = Inst(crane, parent, Vector3.zero, rot, steel);
                go.transform.position = new Vector3(q.x, GroundAt(route, q.x, q.z) - 0.3f, q.z);
                go.transform.localScale = Vector3.one * (1.0f + (float)rng.NextDouble() * 0.35f);
                go.isStatic = true; cranes++;
            }
            // Warehouses and container stacks fill the apron behind the cranes.
            for (int k = 0; k < 4; k++)
            {
                if (rng.NextDouble() > 0.68) continue;
                float off = quay - 34f - (float)rng.NextDouble() * 70f;
                if (off < 88f) continue;
                var q = p + side * off + fwd.normalized * ((float)rng.NextDouble() - 0.5f) * 40f;
                float y = GroundAt(route, q.x, q.z);
                if (y < SeaLevelY + 1.2f) continue;
                bool warehouse = rng.NextDouble() < 0.55;
                var src = warehouse ? shed : crates;
                if (src == null) continue;
                var go = Inst(src, parent, Vector3.zero,
                              rot * Quaternion.Euler(0f, rng.Next(4) * 90f, 0f),
                              warehouse ? facades[rng.Next(facades.Length)] : concrete,
                              warehouse ? roofs[rng.Next(roofs.Length)] : concrete);
                go.transform.position = new Vector3(q.x, y - 0.25f, q.z);
                float sc = 0.9f + (float)rng.NextDouble() * 0.6f;
                go.transform.localScale = warehouse
                    ? new Vector3(sc * (0.80f + (float)rng.NextDouble() * 0.65f),
                                  sc * (0.78f + (float)rng.NextDouble() * 0.45f),
                                  sc * (0.85f + (float)rng.NextDouble() * 0.55f))
                    : Vector3.one * sc;
                go.isStatic = true;
                if (warehouse) sheds++; else stacks++;
            }
            // Bollards along the quay edge.
            if (bollard != null)
                for (int k = 0; k < 2; k++)
                {
                    var q = p + side * (quay - 2.5f) + fwd.normalized * (k * 26f);
                    float y = GroundAt(route, q.x, q.z);
                    if (y < SeaLevelY + 0.6f) continue;
                    var go = Inst(bollard, parent, Vector3.zero, rot, concrete);
                    go.transform.position = new Vector3(q.x, y, q.z);
                    go.isStatic = true; bollards++;
                }
            // Ferries and coasters berthed off the quay.
            if (rng.NextDouble() < 0.42)
            {
                var src = rng.NextDouble() < 0.5 ? ferry : cargo;
                if (src != null)
                {
                    var q = p + side * (quay + 45f + (float)rng.NextDouble() * 120f);
                    var go = Inst(src, parent, Vector3.zero,
                                  rot * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 24f, 0f),
                                  null);
                    go.transform.position = new Vector3(q.x, SeaLevelY + 0.4f, q.z);
                    go.transform.localScale = Vector3.one * (1.0f + (float)rng.NextDouble() * 0.6f);
                    go.isStatic = true; vessels++;
                }
            }
        }

        // Populate the broad connected apron as working service yards rather than one blank
        // pavement field. These are existing production warehouse/container modules, kept well
        // outside the 8 m cycling road and alternated on both sides so the bridge sightline stays
        // open while the target-port frame reads as an active coastal departure.
        for (float d = 220f; d < 2050f; d += 92f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            int j = route.IndexAt(Mathf.Min(d + 24f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) continue;
            var side = route.SideFlat(i);
            var rot = Quaternion.LookRotation(fwd, Vector3.up);

            for (int s = -1; s <= 1; s += 2)
            {
                // Keep the working service yards off the public beach's seaward side.
                if (InBeach(d) && s * SeaSideSign(route, i) > 0f) continue;
                // CONDENSED: yards are SEA-side only now - the landward side is the street wall
                // (26-87 m) and the skyline right behind it (110-480 m), and a shed there would
                // stand inside a tower.
                if (s * SeaSideSign(route, i) < 0f) continue;
                int count = 1 + (rng.NextDouble() < 0.55 ? 1 : 0);
                for (int k = 0; k < count; k++)
                {
                    float offset = 70f + (float)rng.NextDouble() * 120f;   // condensed (was 105-355)
                    var q = p + side * (s * offset) +
                            fwd * (((float)rng.NextDouble() - 0.5f) * 48f);
                    float y = GroundAt(route, q.x, q.z);
                    if (y < SeaLevelY + 1.2f) continue;

                    bool warehouse = rng.NextDouble() < 0.52;
                    var src = warehouse ? shed : crates;
                    if (src == null) continue;
                    var go = Inst(src, parent, Vector3.zero,
                                  rot * Quaternion.Euler(0f, rng.Next(4) * 90f, 0f),
                                  warehouse ? facades[rng.Next(facades.Length)] : concrete,
                                  warehouse ? roofs[rng.Next(roofs.Length)] : concrete);
                    go.transform.position = new Vector3(q.x, y - 0.22f, q.z);
                    float sc = Mathf.Lerp(0.85f, 1.35f, (float)rng.NextDouble());
                    go.transform.localScale = warehouse
                        ? new Vector3(sc * Mathf.Lerp(0.85f, 1.45f, (float)rng.NextDouble()),
                                      sc * Mathf.Lerp(0.78f, 1.18f, (float)rng.NextDouble()),
                                      sc * Mathf.Lerp(0.85f, 1.35f, (float)rng.NextDouble()))
                        : Vector3.one * sc;
                    go.isStatic = true;
                    if (warehouse) sheds++; else stacks++;
                }
            }
        }

        // The FERRIS WHEEL is the signature waterfront landmark of Minato on the world map, so
        // it is staged close enough to the ride to be unmistakable - but still off the
        // carriageway at background tier. The model is authored in the XY plane (disc normal
        // along local +Z), so local +Z must be aimed at the road for the wheel to read as a
        // wheel instead of a thin edge-on sliver.
        void FerrisLandmark(GameObject src, float d, float off, float scale, string name)
        {
            if (src == null)
            {
                Debug.LogError("[minato] ferris wheel model missing");
                return;
            }
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var sea = route.SideFlat(i) * SeaSideSign(route, i);

            // Prefer the seaward pier side (that is where the world map puts it); fall back
            // landward if the sea side is open water at this offset.
            var q = p + sea * off;
            float y = GroundAt(route, q.x, q.z);
            if (y < SeaLevelY + 1.2f)
            {
                q = p - sea * off;
                y = GroundAt(route, q.x, q.z);
            }
            y = Mathf.Max(y, SeaLevelY + 0.8f);

            var pos = new Vector3(q.x, y - 0.5f, q.z);
            var toRoad = p - pos; toRoad.y = 0f;
            var rot = toRoad.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toRoad.normalized, Vector3.up)
                : Quaternion.identity;

            var go = Inst(src, parent, Vector3.zero, rot, null);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            go.name = name; go.isStatic = true;

            // Bright, cheerful fairground livery: a clean white-silver frame with vivid
            // gondolas, so it pops against the bay instead of reading as grey plant.
            var ferrisFrame = CelMaterial("Minato_Ferris_Frame", new Color(0.98f, 0.98f, 0.99f),
                                          gloss: 0.52f, spec: 0.40f, rim: 0.55f);
            var gondola = CelMaterial("Minato_Ferris_Gondola", new Color(1.00f, 0.42f, 0.32f),
                                      gloss: 0.42f, spec: 0.34f, rim: 0.60f);
            var hub = CelMaterial("Minato_Ferris_Hub", new Color(1.00f, 0.80f, 0.24f),
                                  gloss: 0.46f, spec: 0.38f, rim: 0.58f);
            RetintBySlot(go, new[]
            {
                ("vermilion", gondola),      // gondolas are painted steel_vermilion in Blender
                ("gondola", gondola),
                ("hub", hub),
                ("white", ferrisFrame),
                ("hull", ferrisFrame),
                ("steel", ferrisFrame),
            }, ferrisFrame);

            Debug.Log($"[minato] ferris wheel landmark at d={d:N0} m, {off:N0} m off the road, " +
                      $"scale {scale:N2} (~{58.5f * scale:N0} m tall)");
        }
        FerrisLandmark(wheel, FerrisRouteM, FerrisOffsetM, FerrisScale,
                       "Minato_Landmark_FerrisWheel");

        Debug.Log($"[minato] port infrastructure {cranes} container cranes, {sheds} warehouses, " +
                  $"{stacks} crate stacks, {bollards} bollards, {vessels} berthed vessels, " +
                  $"1 skyline landmark (all >= 88 m from the carriageway - background tier)");
    }

    // =================================================================== bright city pass
    //
    // Everything below was added to turn Minato from a drab industrial corridor into the
    // BRIGHT, LIVELY, BUSTLING modern port city it reads as on the world map: a modern glass
    // skyline, a prominent colourful waterfront ferris wheel, a public beach, and crowds of
    // cyclists and pedestrians. ALL tuning here is PROVISIONAL illustrative tuning.

    /// <summary>Route-metre window of the public city beach (replaces the apron there).</summary>
    private const float BeachStartM = 1180f;
    private const float BeachEndM = 1980f;
    /// <summary>Nearest / furthest a modern high-rise may stand from the carriageway.</summary>
    // CONDENSED 2026-09-24: the city used to sit 340-1320 m back with empty ground in front of it; brought in so the ride runs THROUGH the city.
    private const float SkylineNearM = 110f;
    private const float SkylineFarM = 480f;
    /// <summary>Ferris wheel: route metre, lateral offset and scale (wheel is ~58 m at 1.0).</summary>
    private const float FerrisRouteM = 1430f;
    private const float FerrisOffsetM = 95f;   // condensed (was 165)
    private const float FerrisScale = 1.75f;
    /// <summary>
    /// Crowd density knobs. Left at the original 34 m / 26 m after the untextured box people
    /// were replaced by the approved MinatoNPC archetypes: a crowd figure is ~40k triangles
    /// instead of a few hundred, but every instance carries a hard distance cull
    /// (<see cref="MinatoCrowdPopulation.CullScreenHeight"/>, ~110 m), so what is actually
    /// submitted is bounded by view distance rather than by population. Thinning the boulevard
    /// instead just made the waterfront read as deserted. PROVISIONAL - tune against a real
    /// frame-time capture, not a triangle count.
    /// </summary>
    private const float CyclistSpacingM = 34f;
    private const float PedestrianSpacingM = 26f;

    private static bool InBeach(float d) => d >= BeachStartM && d <= BeachEndM;

    /// <summary>
    /// Height of the beach sand at a lateral offset: the terrain surface plus 12 cm, easing
    /// down into the shallows once past the waterline. Both the sand ribbon and everything
    /// standing on it sample THIS, so a parasol can never float over its own beach.
    /// </summary>
    private static float BeachSandY(MinatoRoute route, Vector3 q, float off, float waterline)
    {
        float ground = GroundAt(route, q.x, q.z) + 0.32f;
        if (off <= waterline) return Mathf.Max(ground, SeaLevelY + 0.10f);
        float t = Mathf.Clamp01((off - waterline) / 40f);
        return Mathf.Lerp(Mathf.Max(ground, SeaLevelY + 0.10f), SeaLevelY - 1.2f, t);
    }

    /// <summary>
    /// The height of the WALKABLE port surface at a lateral offset: the apron pavement where
    /// the apron exists, bare ground everywhere else. Placing crowds on GroundAt alone sinks
    /// them 0.38 m into the pavement, which reads as people buried to the shin.
    /// </summary>
    private static float PortSurfaceY(MinatoRoute route, Vector3 q, float seaOffset, float d)
    {
        float ground = GroundAt(route, q.x, q.z);
        bool onApron = d >= 100f && d <= 2295f && !InBeach(d) &&
                       ((seaOffset >= 6.2f && seaOffset <= 420f) ||
                        (seaOffset <= -6.2f && seaOffset >= -1360f));
        return onApron ? Mathf.Max(SeaLevelY + 0.65f, ground + 0.38f) : ground;
    }

    /// <summary>
    /// Ground a figure on the TRUE rendered surface by raycasting a set of mesh colliders (the
    /// terrain chunks and the port aprons) and taking the highest hit. Where the apron pavement
    /// is the top surface the figure lands on pavement; where the terrain grass arches above the
    /// apron the figure lands on grass. Either way there is no blind lift, so nothing floats.
    /// Returns false if no collider is under the point (caller falls back to the analytic height).
    /// </summary>
    private static bool GroundByRenderedSurface(List<MeshCollider> aprons,
                                                List<MeshCollider> terrain,
                                                Vector3 xz, out float y,
                                                out string surface)
    {
        var ray = new Ray(new Vector3(xz.x, SeaLevelY + 400f, xz.z), Vector3.down);
        bool Highest(List<MeshCollider> candidates, out float hitY)
        {
            hitY = float.NegativeInfinity;
            bool found = false;
            foreach (var mc in candidates)
            {
                if (mc == null || !mc.enabled) continue;
                var mf = mc.GetComponent<MeshFilter>();
                var mr = mc.GetComponent<MeshRenderer>();
                // Only a mesh which is actually rendered may ground the crowd. This avoids
                // stale/hidden collision proxies disagreeing with the visible plaza.
                if (mf == null || mf.sharedMesh == null || mr == null || !mr.enabled) continue;
                if (mc.Raycast(ray, out var hit, 900f) && hit.point.y > hitY)
                {
                    hitY = hit.point.y;
                    found = true;
                }
            }
            return found;
        }
        // The apron/plaza is the visible walkable finish and intentionally covers the rolling
        // terrain. Never let the mismatched underlying terrain collider win merely because one
        // of its triangles arches higher.
        if (Highest(aprons, out y)) { surface = "plaza"; return true; }
        if (Highest(terrain, out y)) { surface = "terrain"; return true; }
        surface = "analytic";
        return false;
    }

    /// <summary>
    /// Sink an object so the LOWEST point of its combined renderer bounds sits at
    /// <paramref name="baseY"/> (below the waterline for sea islands/stacks). This seats a
    /// scaled islet in the water instead of leaving it as a textured box floating at the horizon.
    /// </summary>
    private static void SeatInWater(GameObject go, float baseY)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        go.transform.position += new Vector3(0f, baseY - b.min.y, 0f);
    }

    /// <summary>
    /// Re-materialise an imported family BY MATERIAL SLOT NAME. The generic <see cref="Retint"/>
    /// only knows bark/roof tokens, so a tower painted through it becomes one flat colour and a
    /// person loses skin, hair and clothing in a single slab.
    /// </summary>
    private static void RetintBySlot(GameObject go, (string token, Material mat)[] map,
                                     Material fallback)
    {
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = mr.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                mats[i] = fallback;
                foreach (var (token, mat) in map)
                    if (n.Contains(token)) { mats[i] = mat; break; }
            }
            mr.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// A DENSE CLUSTER OF MODERN GLASS HIGH-RISES on the city side of the port, so the region
    /// reads as a modern port CITY rather than a container yard. Deliberately background tier
    /// (>= <see cref="SkylineNearM"/> m from the carriageway): these hold the skyline, they are
    /// not roadside geometry, and the Sakura near/mid art bar does not govern them.
    /// </summary>
    private static void BuildModernSkyline(MinatoRoute route, Transform chapter)
    {
        var parent = new GameObject("Modern Skyline").transform;
        parent.SetParent(chapter, false);

        var towers = new[]
        {
            Model("Minato_City_SkyTowerA"), Model("Minato_City_SkyTowerB"),
            Model("Minato_City_SkyTowerC"), Model("Minato_City_SkyTowerD"),
            Model("Minato_City_SkyTowerE"),
        };
        if (towers[0] == null)
        {
            Debug.LogError("[minato] modern skyline skipped: Minato_City_SkyTower*.fbx missing " +
                           "(run tools/blender/build_minato_city.py -- towers)");
            return;
        }

        // Solid daylit curtain wall. The old high gloss/spec (0.74-0.80 / 0.54-0.62) + near-white
        // rim 0.55 + lifted _ShadowAmbient 0.72 turned every tower into a pale translucent ghost
        // that dissolved into the sky under the bright ACES grade (fixedEV +0.35, bloom, +sat).
        // Deep, saturated glass with REAL value contrast, low spec and a cool (not white) rim now
        // reads as a solid glazed volume. The shade colour is a deep blue-teal so the shadowed
        // faces darken instead of washing to sky, and _ShadowAmbient is dropped so the lit/shaded
        // facades separate.
        var glassCool = CelMaterial("Minato_Tower_GlassCool", new Color(0.12f, 0.28f, 0.46f),
                                    gloss: 0.34f, spec: 0.20f, rim: 0.20f,
                                    shade: new Color(0.19f, 0.33f, 0.50f, 1f));
        var glassTeal = CelMaterial("Minato_Tower_GlassTeal", new Color(0.08f, 0.32f, 0.35f),
                                    gloss: 0.32f, spec: 0.18f, rim: 0.20f,
                                    shade: new Color(0.13f, 0.31f, 0.35f, 1f));
        var glassWarm = CelMaterial("Minato_Tower_GlassWarm", new Color(0.20f, 0.30f, 0.44f),
                                    gloss: 0.30f, spec: 0.18f, rim: 0.18f,
                                    shade: new Color(0.19f, 0.29f, 0.45f, 1f));
        foreach (var g in new[] { glassCool, glassTeal, glassWarm })
        {
            // CelMaterial caches by name and forces a near-white _RimColor and _ShadowAmbient
            // 0.72; override AFTER the call (per the skill's "set extra props on the returned
            // material") so the rim no longer paints the silhouette sky-white and the shaded
            // side keeps genuine value contrast.
            g.SetColor("_RimColor", new Color(0.50f, 0.66f, 0.78f, 1f));
            g.SetFloat("_ShadowAmbient", 0.40f);
            g.SetFloat("_ShadeStrength", 0.58f);
            EditorUtility.SetDirty(g);
        }
        // Mullions/spandrels stay pale so they read as a window grid against the deep glass -
        // facade contrast that makes the towers read as solid volumes rather than sheets of sky.
        var frame = CelMaterial("Minato_Tower_Frame", new Color(0.72f, 0.75f, 0.79f),
                                gloss: 0.30f, spec: 0.18f, rim: 0.22f, shade: GroundShade);
        var crown = CelMaterial("Minato_Tower_Crown", new Color(0.58f, 0.62f, 0.68f),
                                gloss: 0.28f, spec: 0.18f, rim: 0.24f, shade: GroundShade);
        var glassSets = new[] { glassCool, glassTeal, glassWarm };

        var rng = new System.Random(48221);
        int placed = 0;
        float tallest = 0f;

        for (float d = 60f; d < 2700f; d += 58f)
        {
            int i = route.IndexAt(Mathf.Min(d, route.Length - 1f));
            var p = route.Position[i];
            int j = route.IndexAt(Mathf.Min(d + 30f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) continue;
            fwd.Normalize();
            // City side = LANDWARD. The sea side is the working quay and the bay.
            var city = route.SideFlat(i) * -SeaSideSign(route, i);

            int count = rng.NextDouble() < 0.55 ? 2 : 1;
            for (int k = 0; k < count; k++)
            {
                float off = Mathf.Lerp(SkylineNearM, SkylineFarM,
                                       Mathf.Pow((float)rng.NextDouble(), 0.75f));
                var q = p + city * off + fwd * (((float)rng.NextDouble() - 0.5f) * 52f);
                float y = GroundAt(route, q.x, q.z);
                if (y < SeaLevelY + 1.5f) continue;

                // Taller towers cluster in the middle of the district; shorter ones at its edge.
                float coreness = 1f - Mathf.InverseLerp(SkylineNearM, SkylineFarM, off);
                int pick = rng.NextDouble() < 0.30 + 0.45 * coreness
                    ? (rng.NextDouble() < 0.5 ? 3 : 2)          // C (142 m) / D (186 m)
                    : rng.Next(0, 5);
                var src = towers[pick];
                if (src == null) continue;

                var rot = Quaternion.LookRotation(fwd, Vector3.up) *
                          Quaternion.Euler(0f, rng.Next(4) * 90f +
                                               (float)(rng.NextDouble() - 0.5) * 12f, 0f);
                var go = Inst(src, parent, Vector3.zero, rot, null);
                go.transform.position = new Vector3(q.x, y - 0.4f, q.z);
                float sc = 0.72f + (float)rng.NextDouble() * 0.86f;
                go.transform.localScale = new Vector3(sc * (0.88f + (float)rng.NextDouble() * 0.30f),
                                                      sc, sc * (0.88f + (float)rng.NextDouble() * 0.30f));
                RetintBySlot(go, new[]
                {
                    ("glass", glassSets[rng.Next(glassSets.Length)]),
                    ("tower_frame", frame),
                    ("tower_crown", crown),
                }, frame);
                go.isStatic = true;
                placed++;
                tallest = Mathf.Max(tallest, go.GetComponentInChildren<Renderer>() != null
                                             ? go.transform.localScale.y : 0f);
            }
        }

        Debug.Log($"[minato] modern skyline {placed} glass high-rises on the city side, " +
                  $"{SkylineNearM:N0}-{SkylineFarM:N0} m from the carriageway (background tier)");
    }

    // ---- crowds ---------------------------------------------------------------------------

    private static Material[] _crowdTops, _crowdLegs, _crowdHair, _crowdBikes;
    private static Material _crowdSkin;

    /// <summary>
    /// Bright holiday-town clothing palettes. Variety comes from swapping MATERIALS per
    /// instance rather than from more meshes, which keeps a 400-strong crowd at six FBX files.
    /// </summary>
    private static void EnsureCrowdMaterials()
    {
        if (_crowdTops != null) return;
        Material Flat(string n, Color c, float gloss = 0.14f) =>
            CelMaterial(n, c, gloss: gloss, spec: 0.10f, rim: 0.30f);

        _crowdSkin = Flat("Minato_Crowd_Skin", new Color(0.99f, 0.84f, 0.73f));
        _crowdTops = new[]
        {
            Flat("Minato_Crowd_Top_Red",    new Color(0.95f, 0.26f, 0.24f)),
            Flat("Minato_Crowd_Top_Sun",    new Color(1.00f, 0.78f, 0.16f)),
            Flat("Minato_Crowd_Top_Cyan",   new Color(0.20f, 0.80f, 0.90f)),
            Flat("Minato_Crowd_Top_Mint",   new Color(0.40f, 0.90f, 0.62f)),
            Flat("Minato_Crowd_Top_Pink",   new Color(1.00f, 0.52f, 0.72f)),
            Flat("Minato_Crowd_Top_White",  new Color(0.97f, 0.97f, 0.96f)),
            Flat("Minato_Crowd_Top_Violet", new Color(0.66f, 0.46f, 0.94f)),
            Flat("Minato_Crowd_Top_Lime",   new Color(0.76f, 0.92f, 0.26f)),
        };
        // Cyclist tights/shorts and pedestrian trousers. BIASED AWAY FROM WHITE: the old palette
        // included a near-white leg (0.92,0.92,0.90) that vanished against the white bike frame,
        // so a rider read as a torso stuck to the frame. All entries are now saturated or dark so
        // legs separate from both the white frame and the pale plaza.
        _crowdLegs = new[]
        {
            Flat("Minato_Crowd_Leg_Denim",  new Color(0.22f, 0.32f, 0.60f)),
            Flat("Minato_Crowd_Leg_Slate",  new Color(0.30f, 0.34f, 0.42f)),
            Flat("Minato_Crowd_Leg_Coral",  new Color(0.92f, 0.42f, 0.32f)),
            Flat("Minato_Crowd_Leg_Navy",   new Color(0.13f, 0.18f, 0.42f)),
            Flat("Minato_Crowd_Leg_Teal",   new Color(0.10f, 0.44f, 0.46f)),
            Flat("Minato_Crowd_Leg_Plum",   new Color(0.42f, 0.16f, 0.44f)),
        };
        _crowdHair = new[]
        {
            Flat("Minato_Crowd_Hair_Black", new Color(0.16f, 0.15f, 0.17f)),
            Flat("Minato_Crowd_Hair_Brown", new Color(0.36f, 0.24f, 0.17f)),
            Flat("Minato_Crowd_Hair_Sand",  new Color(0.78f, 0.66f, 0.42f)),
            Flat("Minato_Crowd_Hair_Teal",  new Color(0.22f, 0.56f, 0.58f)),
        };
        _crowdBikes = new[]
        {
            Flat("Minato_Crowd_Bike_Yellow", new Color(0.96f, 0.84f, 0.18f), 0.45f),
            Flat("Minato_Crowd_Bike_Red",    new Color(0.88f, 0.22f, 0.22f), 0.45f),
            Flat("Minato_Crowd_Bike_Teal",   new Color(0.16f, 0.72f, 0.72f), 0.45f),
            Flat("Minato_Crowd_Bike_White",  new Color(0.94f, 0.94f, 0.94f), 0.45f),
        };
    }

    private static GameObject PlaceFigure(GameObject src, Transform parent, Vector3 pos,
                                          Quaternion rot, float scale, System.Random rng)
    {
        if (src == null) return null;
        EnsureCrowdMaterials();
        var go = Inst(src, parent, Vector3.zero, rot, null);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        RetintBySlot(go, new[]
        {
            ("skin", _crowdSkin),
            ("hair", _crowdHair[rng.Next(_crowdHair.Length)]),
            ("cloth_top", _crowdTops[rng.Next(_crowdTops.Length)]),
            ("cloth_leg", _crowdLegs[rng.Next(_crowdLegs.Length)]),
            ("bike_frame", _crowdBikes[rng.Next(_crowdBikes.Length)]),
        }, _crowdTops[0]);
        go.isStatic = true;
        return go;
    }

    /// <summary>
    /// The BUSTLE: background cyclists riding the waterfront boulevard and pedestrians on the
    /// quay, the promenade and the city pavement.
    ///
    /// These are ENVIRONMENT crowd props, NOT the rigged gameplay NPCs - a rival rider is a
    /// Kuro-skeleton character staged through the NPC pipeline, and nothing here touches that.
    /// Everything stays clear of the 8 m carriageway: cyclists ride the separated path outside
    /// the guardrail line, pedestrians start at 10 m.
    /// </summary>
    private static void BuildCityLife(MinatoRoute route, Transform chapter)
    {
        var parent = new GameObject("City Life").transform;
        parent.SetParent(chapter, false);
        var riders = new GameObject("Boulevard Cyclists").transform; riders.SetParent(parent, false);
        var walkers = new GameObject("Waterfront Pedestrians").transform; walkers.SetParent(parent, false);

        var rng = new System.Random(90211);
        int cyclists = 0, pedestrians = 0;

        // The approved MinatoNPC crowd. Near/mid figures retain full-resolution live skins and
        // procedural skeleton motion; only the distant 3D LOD is baked. They remain AI-free
        // environment decor and replace the old Minato_People_* box props.
        MinatoCrowdPopulation.Prepare();
        if (!MinatoCrowdPopulation.Ready)
        {
            Debug.LogError("[minato] city life skipped: crowd templates unavailable " +
                           "(run design_assets/3d/kuro/minato_crowd_export.py)");
            return;
        }
        var cyclistKeys = MinatoCrowdPopulation.CyclistKeys;
        var pedKeys = MinatoCrowdPopulation.PedestrianKeys;

        // Collect the terrain + apron mesh colliders so every figure is grounded on the SURFACE
        // THAT ACTUALLY RENDERS beneath it (see GroundByCollider). This is what fixes the
        // sea-side cyclists that used to float ~0.35 m over the grass verge on a blind apron lift.
        var aprons = new List<MeshCollider>();
        var terrainGround = new List<MeshCollider>();
        var rootT = chapter.parent != null ? chapter.parent : chapter;
        foreach (var mc in rootT.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.name.StartsWith("Connected Port")) aprons.Add(mc);
            else if (mc.name.StartsWith("Terrain_Chunk")) terrainGround.Add(mc);
        }
        Physics.SyncTransforms();
        int plazaGrounded = 0, terrainGrounded = 0, analyticGrounded = 0;

        // --- cyclists on the separated waterfront path, both directions.
        for (float d = 70f; d < 2240f; d += CyclistSpacingM)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            int j = route.IndexAt(Mathf.Min(d + 20f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) continue;
            fwd.Normalize();
            var side = route.SideFlat(i);

            for (int s = -1; s <= 1; s += 2)
            {
                if (rng.NextDouble() > 0.62) continue;
                int pack = 1 + (rng.NextDouble() < 0.35 ? 1 : 0) + (rng.NextDouble() < 0.14 ? 1 : 0);
                for (int k = 0; k < pack; k++)
                {
                    float off = s * (7.5f + (float)rng.NextDouble() * 3.2f);
                    var q = p + side * off + fwd * (k * 3.4f);
                    float seaOff = off * SeaSideSign(route, i);
                    // Sit on the real rendered surface (apron pavement or grass), NOT a blind
                    // +0.38 apron plane; fall back to the analytic surface only if no collider is
                    // under the wheel.
                    if (!GroundByRenderedSurface(aprons, terrainGround, q, out float y,
                                                 out string surface))
                        y = PortSurfaceY(route, q, seaOff, d);
                    if (surface == "plaza") plazaGrounded++;
                    else if (surface == "terrain") terrainGrounded++;
                    else analyticGrounded++;
                    if (y < SeaLevelY + 0.5f) continue;
                    // Riders on the seaward path travel with the route, the far side against it.
                    var heading = s < 0 ? fwd : -fwd;
                    var go = MinatoCrowdPopulation.Spawn(
                        cyclistKeys[rng.Next(cyclistKeys.Length)], riders,
                        new Vector3(q.x, y, q.z),
                        Quaternion.LookRotation(heading, Vector3.up),
                        0.96f + (float)rng.NextDouble() * 0.12f);
                    if (go != null) cyclists++;
                }
            }
        }

        // --- pedestrians on the promenade / quay / city pavement.
        for (float d = 70f; d < 2260f; d += PedestrianSpacingM)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            int j = route.IndexAt(Mathf.Min(d + 20f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) continue;
            fwd.Normalize();
            var side = route.SideFlat(i);

            for (int s = -1; s <= 1; s += 2)
            {
                if (rng.NextDouble() > 0.72) continue;
                int group = 1 + rng.Next(3);
                float baseOff = 10f + (float)rng.NextDouble() * 48f;
                for (int k = 0; k < group; k++)
                {
                    float off = s * (baseOff + (float)rng.NextDouble() * 5f);
                    var q = p + side * off + fwd * (((float)rng.NextDouble() - 0.5f) * 14f);
                    float seaOff = off * SeaSideSign(route, i);
                    if (!GroundByRenderedSurface(aprons, terrainGround, q, out float y,
                                                 out string surface))
                        y = PortSurfaceY(route, q, seaOff, d);
                    if (surface == "plaza") plazaGrounded++;
                    else if (surface == "terrain") terrainGrounded++;
                    else analyticGrounded++;
                    if (y < SeaLevelY + 0.5f) continue;
                    var src = pedKeys[rng.Next(pedKeys.Length)];
                    bool moving = src.Contains("_Walk_");
                    var walkAxis = rng.NextDouble() < 0.5 ? fwd : -fwd;
                    var go = MinatoCrowdPopulation.Spawn(
                        src, walkers, new Vector3(q.x, y, q.z),
                        moving ? Quaternion.LookRotation(walkAxis, Vector3.up) :
                                 Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                        0.94f + (float)rng.NextDouble() * 0.16f, null,
                        moving ? walkAxis : Vector3.zero,
                        moving ? 6f + (float)rng.NextDouble() * 8f : 0f,
                        moving ? 0.62f + (float)rng.NextDouble() * 0.34f : 0f);
                    if (go != null) pedestrians++;
                }
            }
        }

        Debug.Log($"[minato] city life {cyclists} boulevard cyclists, {pedestrians} pedestrians " +
                  $"(all outside the {RoadHalfWidth + ShoulderW:N1} m carriageway edge); " +
                  $"grounded plaza={plazaGrounded}, terrain={terrainGrounded}, " +
                  $"analytic={analyticGrounded}");
    }

    // ==================================================== boulevard dressing (After board 01/02)
    //
    // Recreates the two things the "After" concept boards carry along the waterfront boulevard
    // that the staged scene did not:
    //
    //   * BANNER FLAGS - a blue ocean-motif vertical banner on EVERY lamp standard. They are
    //     not scattered on their own poles: each one is placed on the world transform of an
    //     EXISTING `Minato_Bridge_Lamp` instance, because the banner asset is authored about the
    //     lamp base in the same local axes. That makes the mount exact by construction instead
    //     of by a per-instance offset table that drifts the moment the lamp module is rebuilt.
    //   * PLANTED MEDIAN - palms, flowering beds and magenta shrubs in the verge band, plus
    //     timber kerb planters along both kerbs, replacing the bare grass strip.
    //
    // Refs: Minamo_01_PortDeparture_After.png, Minamo_02_BridgeApproach_After.png.
    // ALL VALUES PROVISIONAL illustrative tuning - named constants, never magic literals.
    private const float BoulevardStartM = 60f;
    private const float BoulevardEndM = 2290f;      // stops short of SeaCrossStartM (2300 m)
    private const float BannerLampSearchM = 45f;    // max off-route distance for a boulevard lamp
    private const float MedianPalmSpacingM = 11f;
    private const float MedianPalmOffsetM = 13.0f;
    private const float MedianBedSpacingM = 8f;
    private const float MedianShrubSpacingM = 9f;
    // The planted band sits OUTSIDE the 7.5-10.7 m lane the crowd cyclists use (see
    // BuildCityLife) so nothing is planted through a rider.
    private const float MedianInnerOffsetM = 11.5f;
    private const float MedianOuterOffsetM = 17.5f;
    // Between the 4.73 m guardrail line and the 7.5 m cycle band.
    private const float KerbPlanterOffsetM = 6.1f;
    private const float KerbPlanterSpacingM = 13f;

    /// <summary>
    /// Slot-aware retint for the boulevard families. The banner, its bracket steel, timber,
    /// soil, foliage and the four blossom colours all arrive in one FBX, so a single-material
    /// Retint would paint the bracket blue and the flowers green. Keyed on the imported slot
    /// NAME, the same contract the Blender palette slots were authored against.
    /// </summary>
    /// <summary>
    /// Boulevard / market bush foliage. ANIME QUALITY PASS (2026-09-24): was a flat lime
    /// (0.33, 0.58, 0.25) with the default lavender shade, which read as plastic. Now a white
    /// tint over Minato_Foliage_Gradient.png, which the Blender builders map per CLUMP
    /// (build_minato_boulevard.py blob(): v = underside -> crown), so every clump is deep
    /// teal-green below and sunlit yellow-green on top, with a cool teal shade tint. ONE helper
    /// because two call sites share this material asset by name and used to overwrite each other.
    /// </summary>
    private static Material MedianFoliageMaterial()
    {
        const string gradient = MinatoTex + "/Minato_Foliage_Gradient.png";
        if (AssetImporter.GetAtPath(gradient) is TextureImporter ti &&
            ti.wrapMode != TextureWrapMode.Clamp)
        {
            ti.wrapMode = TextureWrapMode.Clamp;   // the crown row must never wrap to the base
            ti.SaveAndReimport();
        }
        return CelMaterial("Minato_MedianFoliage", Color.white, gloss: 0.06f, spec: 0.04f,
                           rim: 0.30f, texture: Tex(MinatoTex, "Minato_Foliage_Gradient.png"),
                           shade: new Color(0.44f, 0.64f, 0.68f));
    }

    private static void RetintBoulevard(GameObject go)
    {
        // Banner cloth flutters with the gameplay wind (MapleRide/HDRP/Flag reads _MapleWind;
        // pinned at the bracket, u = 0). Was a static CelMaterial.
        var banner = FoliageMaterial("Minato_BannerFlag", Color.white,
                                     Tex(MinatoTex, "Minato_Banner_Albedo.png"), 0.22f);
        banner.shader = Shader.Find("MapleRide/HDRP/Flag");
        // The foliage lighting model shades much darker than CelLit (first flag pass rendered the
        // blue/white banner navy): lift the tint and let the low sun glow through the cloth.
        banner.SetColor("_Color", new Color(1.45f, 1.45f, 1.45f, 1f));
        if (banner.HasProperty("_Translucency")) banner.SetFloat("_Translucency", 0.9f);
        if (banner.HasProperty("_Cutoff")) banner.SetFloat("_Cutoff", 0.02f);
        var steel = CelMaterial("Minato_BannerSteel", new Color(0.32f, 0.34f, 0.36f),
                                gloss: 0.28f, spec: 0.22f, rim: 0.38f);
        var timber = CelMaterial("Minato_PlanterTimber", new Color(0.50f, 0.33f, 0.21f),
                                 gloss: 0.10f, spec: 0.05f, rim: 0.28f);
        var soil = CelMaterial("Minato_BedSoil", new Color(0.24f, 0.17f, 0.13f),
                               gloss: 0.04f, spec: 0.02f, rim: 0.16f);
        var kerb = CelMaterial("Minato_BedKerb", new Color(0.78f, 0.76f, 0.72f),
                               gloss: 0.08f, spec: 0.05f, rim: 0.22f);
        var leaf = MedianFoliageMaterial();
        var pink = CelMaterial("Minato_BlossomPink", new Color(0.97f, 0.42f, 0.62f),
                               gloss: 0.10f, spec: 0.06f, rim: 0.42f);
        var red = CelMaterial("Minato_BlossomRed", new Color(0.88f, 0.17f, 0.22f),
                              gloss: 0.10f, spec: 0.06f, rim: 0.42f);
        var magenta = CelMaterial("Minato_BlossomMagenta", new Color(0.82f, 0.14f, 0.56f),
                                  gloss: 0.10f, spec: 0.06f, rim: 0.42f);
        var white = CelMaterial("Minato_BlossomWhite", new Color(0.98f, 0.97f, 0.94f),
                                gloss: 0.10f, spec: 0.06f, rim: 0.42f);

        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = mr.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                mats[i] =
                    n.Contains("banner") ? banner
                  : n.Contains("blossom_pink") ? pink
                  : n.Contains("blossom_red") ? red
                  : n.Contains("blossom_magenta") ? magenta
                  : n.Contains("blossom_white") ? white
                  : n.Contains("soil") ? soil
                  : n.Contains("concrete") ? kerb
                  : n.Contains("timber") ? timber
                  : n.Contains("foliage") ? leaf
                  : steel;
            }
            mr.sharedMaterials = mats;
        }
    }

    private static void BuildBoulevardDressing(MinatoRoute route, Transform chapter, Transform root)
    {
        var parent = new GameObject("Boulevard Dressing").transform;
        parent.SetParent(chapter, false);
        var bannerRoot = new GameObject("Lamp Banners").transform; bannerRoot.SetParent(parent, false);
        var medianRoot = new GameObject("Planted Median").transform; medianRoot.SetParent(parent, false);
        var kerbRoot = new GameObject("Kerb Planters").transform; kerbRoot.SetParent(parent, false);

        var bannerA = Model("Minato_City_BannerFlag");
        var bannerB = Model("Minato_City_BannerFlagB");
        var palmTall = Model("Minato_Flora_PalmTall");
        var palmShort = Model("Minato_Flora_PalmShort");
        var bed = Model("Minato_Flora_FlowerBed");
        var magentaShrub = Model("Minato_Flora_ShrubMagenta");
        var planter = Model("Minato_City_KerbPlanter");

        // The same rendered-surface grounding the crowds use: apron pavement where the plaza is
        // the top surface, terrain grass where it is not. A blind analytic lift floats props.
        var aprons = new List<MeshCollider>();
        var terrainGround = new List<MeshCollider>();
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.name.StartsWith("Connected Port")) aprons.Add(mc);
            else if (mc.name.StartsWith("Terrain_Chunk")) terrainGround.Add(mc);
        }
        Physics.SyncTransforms();

        float SurfaceY(Vector3 q, float seaOff, float d)
        {
            if (GroundByRenderedSurface(aprons, terrainGround, q, out float y, out _)) return y;
            return PortSurfaceY(route, q, seaOff, d);
        }

        var rng = new System.Random(430711);

        // ---------------------------------------------------------------- banners on every post
        int banners = 0, skipped = 0;
        if (bannerA != null)
        {
            int lastIdx = route.IndexAt(BoulevardEndM + 80f);
            var lamps = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Minato_Bridge_Lamp") lamps.Add(t);   // EXACT name, never Contains

            foreach (var lamp in lamps)
            {
                // Nearest route station inside the boulevard window - the lamps were placed by
                // three different sub-builders, so their route metre is recovered here rather
                // than assumed.
                int best = -1;
                float bestSq = float.MaxValue;
                for (int i = 0; i <= lastIdx; i++)
                {
                    var dp = route.Position[i] - lamp.position;
                    float sq = dp.x * dp.x + dp.z * dp.z;
                    if (sq < bestSq) { bestSq = sq; best = i; }
                }
                if (best < 0 || bestSq > BannerLampSearchM * BannerLampSearchM) { skipped++; continue; }
                float d = route.Distance[best];
                if (d < BoulevardStartM - 40f || d > BoulevardEndM) { skipped++; continue; }

                // Mount the bracket on the face that looks at the carriageway. The asset carries
                // its bracket on local +X, so a lamp standing on the +side is spun 180 degrees.
                float lateral = Vector3.Dot(lamp.position - route.Position[best],
                                            route.SideFlat(best));
                var src = (banners % 3 == 2 && bannerB != null) ? bannerB : bannerA;
                var go = Inst(src, bannerRoot, Vector3.zero, Quaternion.identity, null);
                go.transform.SetPositionAndRotation(
                    lamp.position,
                    lamp.rotation * (lateral > 0f ? Quaternion.Euler(0f, 180f, 0f)
                                                  : Quaternion.identity));
                RetintBoulevard(go);
                go.isStatic = true;
                banners++;
            }
        }

        // ---------------------------------------------------------------- palm rows
        int palms = 0, beds = 0, shrubs = 0, planters = 0;
        int step = 0;
        for (float d = BoulevardStartM; d < BoulevardEndM; d += MedianPalmSpacingM, step++)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + 12f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) continue;

            for (int s = -1; s <= 1; s += 2)
            {
                // Dense row on the seaward median, a looser alternating row on the city side -
                // which is how the boards read.
                bool seaward = s * SeaSideSign(route, i) > 0f;
                if (!seaward && step % 2 == 1) continue;
                float off = s * (MedianPalmOffsetM + (float)rng.NextDouble() * 2.2f - 1.1f);
                var q = p + side * off;
                float y = SurfaceY(q, off * SeaSideSign(route, i), d);
                if (y < SeaLevelY + 0.4f) continue;
                var src = (rng.NextDouble() < 0.62 ? palmTall : palmShort) ?? palmTall;
                if (src == null) continue;
                var go = Inst(src, medianRoot, Vector3.zero,
                              Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), null);
                go.transform.position = new Vector3(q.x, y - 0.05f, q.z);
                go.transform.localScale = Vector3.one * (0.92f + (float)rng.NextDouble() * 0.26f);
                RetintBoulevard(go);
                go.isStatic = true;
                palms++;
            }
        }

        // ---------------------------------------------------------------- flowering beds
        for (float d = BoulevardStartM + 5f; d < BoulevardEndM; d += MedianBedSpacingM)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + 12f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) continue;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            for (int s = -1; s <= 1; s += 2)
            {
                if (rng.NextDouble() > 0.80) continue;
                float off = s * Mathf.Lerp(MedianInnerOffsetM + 0.4f, MedianOuterOffsetM - 1.0f,
                                           (float)rng.NextDouble());
                var q = p + side * off;
                float y = SurfaceY(q, off * SeaSideSign(route, i), d);
                if (y < SeaLevelY + 0.4f || bed == null) continue;
                var go = Inst(bed, medianRoot, Vector3.zero,
                              rot * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 12f, 0f),
                              null);
                go.transform.position = new Vector3(q.x, y - 0.06f, q.z);
                go.transform.localScale = new Vector3(0.9f + (float)rng.NextDouble() * 0.35f,
                                                      0.9f + (float)rng.NextDouble() * 0.3f,
                                                      0.9f + (float)rng.NextDouble() * 0.5f);
                RetintBoulevard(go);
                go.isStatic = true;
                beds++;
            }
        }

        // ---------------------------------------------------------------- magenta shrubs
        for (float d = BoulevardStartM + 2f; d < BoulevardEndM; d += MedianShrubSpacingM)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            for (int s = -1; s <= 1; s += 2)
            {
                int cluster = 1 + (rng.NextDouble() < 0.45 ? 1 : 0) + (rng.NextDouble() < 0.2 ? 1 : 0);
                for (int k = 0; k < cluster; k++)
                {
                    if (rng.NextDouble() > 0.72) continue;
                    float off = s * Mathf.Lerp(MedianInnerOffsetM, MedianOuterOffsetM,
                                               (float)rng.NextDouble());
                    var q = p + side * off + Vector3.zero;
                    float y = SurfaceY(q, off * SeaSideSign(route, i), d);
                    if (y < SeaLevelY + 0.4f || magentaShrub == null) continue;
                    var go = Inst(magentaShrub, medianRoot, Vector3.zero,
                                  Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), null);
                    go.transform.position = new Vector3(q.x, y - 0.05f, q.z);
                    go.transform.localScale = Vector3.one * (0.85f + (float)rng.NextDouble() * 0.6f);
                    RetintBoulevard(go);
                    go.isStatic = true;
                    shrubs++;
                }
            }
        }

        // ---------------------------------------------------------------- kerb planters
        for (float d = BoulevardStartM + 7f; d < BoulevardEndM; d += KerbPlanterSpacingM)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + 10f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f || planter == null) continue;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            for (int s = -1; s <= 1; s += 2)
            {
                if (rng.NextDouble() > 0.86) continue;
                float off = s * KerbPlanterOffsetM;
                var q = p + side * off;
                float y = SurfaceY(q, off * SeaSideSign(route, i), d);
                if (y < SeaLevelY + 0.4f) continue;
                var go = Inst(planter, kerbRoot, Vector3.zero, rot, null);
                go.transform.position = new Vector3(q.x, y - 0.04f, q.z);
                RetintBoulevard(go);
                go.isStatic = true;
                planters++;
            }
        }

        Debug.Log($"[minato] boulevard dressing {banners} lamp banners ({skipped} lamps outside " +
                  $"the boulevard window), {palms} median palms, {beds} flowering beds, " +
                  $"{shrubs} magenta shrubs, {planters} kerb planters");
    }

    // ==================================================== cafe / market district (After 01 / 04)
    //
    // Minamo_01_PortDeparture_After.png and Minamo_04_FarShore_After.png both show the CITY-SIDE
    // plaza as a Mediterranean market/cafe promenade: striped-awning stalls, blue-and-white
    // pop-up tents, red and cream parasols over round tables, seated drinkers and browsing
    // crowds. The staged scene rendered that same band as several hundred metres of bare white
    // tile (diag_minato_target_crowd.png / diag_minato_target_skyline.png). This pass fills it.
    //
    // DESIGN RULES THAT MATTER
    // * CITY SIDE ONLY. The seaward side is the working port, the quay and the public beach; a
    //   market there would wall the water off, which is the mistake BuildPortCity already had to
    //   back out of.
    // * ORGANIC, NOT A GRID. Clusters of three kinds (a stall row, a cafe terrace, a tent
    //   group) at jittered spacing and jittered band depth. A regular lattice of stalls reads as
    //   a car park, not a market.
    // * NOTHING IN THE CYCLING CORRIDOR. The band starts outside the 17.5 m planted median and
    //   therefore well outside the 7.5-10.7 m lane the crowd cyclists use.
    // * PLACED AGAINST THE SCENE THAT EXISTS. Every candidate is rejected against the real
    //   renderer bounds of the already-placed waterfront blocks, street trees and median
    //   planting, and grounded on the real rendered plaza surface - not on an analytic plane.
    //
    // ALL VALUES PROVISIONAL illustrative tuning - named constants, never magic literals.
    private const float MarketStartM = 300f;
    private const float MarketEndM = 2000f;
    // Inner edge sits just outside MedianOuterOffsetM (17.5 m) so the market never grows into
    // the planted median or the rider lane; the outer edge stops short of the 26 m street wall
    // that BuildPortCity's CorridorClearM establishes, with overspill handled by the obstacle
    // rejection rather than by a hard limit.
    private const float MarketInnerOffsetM = 18.5f;
    private const float MarketOuterOffsetM = 30.0f;
    private const float MarketObstacleClearM = 3.2f;
    private const float MarketClusterSpacingM = 15f;
    /// <summary>
    /// Obstacle discs larger than this are a GROUP bound (a whole district's encapsulated
    /// renderer bounds or a GPU-instanced scatter batch), not a prop. Collecting those rejected
    /// every single market candidate on the first run - the "Port City" group alone is a ~2.5 km
    /// disc. Props are collected per-renderer and anything bigger than this is discarded.
    /// </summary>
    private const float MarketMaxObstacleR = 18f;
    /// <summary>Obstacles further than this from the market corridor centreline can never block
    /// a candidate, so they are culled before the O(n) rejection loop ever sees them.</summary>
    private const float MarketCorridorCullM = 80f;
    /// <summary>Outward offset of the corridor sample line used for that cull - centred so the
    /// cull disc covers both the promenade band and the deep plaza band.</summary>
    private const float MarketCorridorSampleM = 80f;
    // THE DEEP PLAZA. Beyond BuildPortCity's 26 m street wall the city-side tile apron runs out
    // to the tower district and reads as bare white in the target_skyline framing. A sparser
    // second tier of cafe terraces and tent groups fills the pockets between the blocks; the
    // obstacle rejection keeps it out of the buildings themselves.
    private const float MarketDeepInnerM = 34f;
    private const float MarketDeepOuterM = 140f;
    private const float MarketDeepSpacingM = 13f;
    // Rejection footprints. The cafe footprint must exceed the parasol CANOPY radius, not the
    // table radius - at 1.55 m the canopies visibly interpenetrated on the contact frame.
    private const float MarketCafeFootprintM = 2.25f;
    private const float MarketStallFootprintM = 1.85f;
    private const float MarketTentFootprintM = 2.10f;
    private const float StallRowPitchM = 3.45f;
    private const float CafeSetPitchM = 3.60f;
    // Cafe chair ring, mirrored from build_minato_market.py (CHAIR_RING_R / CAFE_CHAIRS /
    // the 0.52 rad phase) so a seated figure lands ON a chair rather than beside it.
    private const float CafeChairRingM = 0.80f;
    private const int CafeChairCount = 3;
    private const float CafeChairPhaseRad = 0.52f;
    /// <summary>
    /// The seated crowd archetype's bench places its backrest at local +Z and its seat/legs at
    /// local -Z (see MinatoCrowdPopulation.BenchMesh), so the figure LOOKS ALONG ITS LOCAL -Z.
    /// A cafe sitter therefore takes LookRotation(outward-from-table), not LookRotation(toward).
    /// </summary>
    private const bool SitterFacesLocalMinusZ = true;

    /// <summary>Exact-name descendant lookup. Never a Contains match - that has grabbed the
    /// wrong object in this project before.</summary>
    private static Transform FirstNamed(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    /// <summary>
    /// Slot-aware retint for the market family. Stripes are GEOMETRY in this family (alternating
    /// awning panels / parasol wedges carrying two different Blender slots), so the stripe
    /// depends entirely on this mapping keeping the two slots apart - a single-material Retint
    /// would flatten every awning to one colour and delete the whole read.
    /// </summary>
    private static void RetintMarket(GameObject go)
    {
        var awnBlue = CelMaterial("Minato_AwningBlue", new Color(0.16f, 0.46f, 0.82f),
                                  gloss: 0.08f, spec: 0.05f, rim: 0.30f);
        var awnRed = CelMaterial("Minato_AwningRed", new Color(0.86f, 0.22f, 0.21f),
                                 gloss: 0.08f, spec: 0.05f, rim: 0.30f);
        // Cream is deliberately NOT paper white: the bright-midday ACES grade (fixedEV +0.35,
        // sat +20) clips anything above ~0.93 to featureless white, which made both the cream
        // wedges of the red parasol and the whole plain parasol read as a flat disc.
        var awnCream = CelMaterial("Minato_AwningCream", new Color(0.93f, 0.90f, 0.84f),
                                   gloss: 0.08f, spec: 0.05f, rim: 0.26f);
        // Warm canvas ochre for the alternating wedges of the PLAIN parasol - see
        // build_minato_market cafe_set_cream(). Must be clearly darker than cream to survive
        // the grade, or the dome cel-shades back down to a flat white disc.
        var awnSand = CelMaterial("Minato_AwningSand", new Color(0.78f, 0.68f, 0.52f),
                                  gloss: 0.08f, spec: 0.05f, rim: 0.26f);
        var produceRed = CelMaterial("Minato_ProduceRed", new Color(0.88f, 0.21f, 0.18f),
                                     gloss: 0.12f, spec: 0.08f, rim: 0.40f);
        var produceGreen = CelMaterial("Minato_ProduceGreen", new Color(0.44f, 0.72f, 0.26f),
                                       gloss: 0.12f, spec: 0.08f, rim: 0.40f);
        var produceOrange = CelMaterial("Minato_ProduceOrange", new Color(0.97f, 0.62f, 0.15f),
                                        gloss: 0.12f, spec: 0.08f, rim: 0.40f);
        var timber = CelMaterial("Minato_PlanterTimber", new Color(0.50f, 0.33f, 0.21f),
                                 gloss: 0.10f, spec: 0.05f, rim: 0.28f);
        var tableTop = CelMaterial("Minato_CafeTableTop", new Color(0.94f, 0.93f, 0.90f),
                                   gloss: 0.18f, spec: 0.12f, rim: 0.28f);
        var steel = CelMaterial("Minato_MarketSteel", new Color(0.30f, 0.33f, 0.36f),
                                gloss: 0.26f, spec: 0.20f, rim: 0.34f);

        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = mr.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                mats[i] =
                    n.Contains("awning_blue") ? awnBlue
                  : n.Contains("awning_red") ? awnRed
                  : n.Contains("awning_cream") ? awnCream
                  : n.Contains("awning_sand") ? awnSand
                  : n.Contains("produce_red") ? produceRed
                  : n.Contains("produce_green") ? produceGreen
                  : n.Contains("produce_orange") ? produceOrange
                  : n.Contains("white_render") ? tableTop
                  : n.Contains("timber") ? timber
                  : n.Contains("soil") ? timber
                  : n.Contains("foliage") ? MedianFoliageMaterial()
                  : n.Contains("blossom_pink") ? CelMaterial("Minato_BlossomPink",
                        new Color(0.97f, 0.42f, 0.62f), gloss: 0.10f, spec: 0.06f, rim: 0.42f)
                  : n.Contains("blossom_white") ? CelMaterial("Minato_BlossomWhite",
                        new Color(0.98f, 0.97f, 0.94f), gloss: 0.10f, spec: 0.06f, rim: 0.42f)
                  : steel;
            }
            mr.sharedMaterials = mats;
        }
    }

    private static void BuildMarketDistrict(MinatoRoute route, Transform chapter, Transform root)
    {
        var parent = new GameObject("Market District").transform;
        parent.SetParent(chapter, false);
        var stallRoot = new GameObject("Market Stalls").transform; stallRoot.SetParent(parent, false);
        var cafeRoot = new GameObject("Cafe Terraces").transform; cafeRoot.SetParent(parent, false);
        var fillRoot = new GameObject("Market Dressing").transform; fillRoot.SetParent(parent, false);
        var shopperRoot = new GameObject("Market Shoppers").transform; shopperRoot.SetParent(parent, false);

        var stallModels = new[] { Model("Minato_City_MarketStall"), Model("Minato_City_MarketStallB") };
        var cafeModels = new[] { Model("Minato_City_CafeSet"), Model("Minato_City_CafeSetB") };
        var tentModel = Model("Minato_City_MarketTent");
        var crateModel = Model("Minato_City_CratePile");
        var planterModel = Model("Minato_City_KerbPlanter");
        if (stallModels[0] == null || cafeModels[0] == null)
        {
            Debug.LogError("[minato] market district skipped: Minato_City_MarketStall/CafeSet.fbx " +
                           "missing (run tools/blender/build_minato_market.py)");
            return;
        }

        // Same rendered-surface grounding the crowds and the boulevard planting use.
        var aprons = new List<MeshCollider>();
        var terrainGround = new List<MeshCollider>();
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.name.StartsWith("Connected Port")) aprons.Add(mc);
            else if (mc.name.StartsWith("Terrain_Chunk")) terrainGround.Add(mc);
        }
        Physics.SyncTransforms();

        // --- obstacle discs from what is ALREADY in the scene, so the market never grows
        //     through a shopfront, a street tree or a flower bed.
        var blocked = new List<Vector3>();       // (x, radius, z)

        // Only obstacles that sit in the market corridor can ever reject a candidate; culling
        // the rest up front keeps the rejection loop small.
        var corridor = new List<Vector3>();
        for (float dd = MarketStartM - 40f; dd <= MarketEndM + 40f; dd += 20f)
        {
            int ii = route.IndexAt(Mathf.Clamp(dd, 0f, route.Length - 1f));
            var pp = route.Position[ii];
            var oo = route.SideFlat(ii) * -SeaSideSign(route, ii);
            corridor.Add(pp + oo * MarketCorridorSampleM);
        }
        bool NearCorridor(Vector3 c)
        {
            for (int k = 0; k < corridor.Count; k++)
            {
                float dx = c.x - corridor[k].x, dz = c.z - corridor[k].z;
                if (dx * dx + dz * dz < MarketCorridorCullM * MarketCorridorCullM) return true;
            }
            return false;
        }

        void CollectFrom(string groupName)
        {
            var g = FirstNamed(root, groupName);
            if (g == null) return;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                float rad = Mathf.Max(b.extents.x, b.extents.z);
                // Discard group-sized and instanced-batch bounds (see MarketMaxObstacleR) and
                // sub-decimetre slivers that would only add loop cost.
                if (rad > MarketMaxObstacleR || rad < 0.15f) continue;
                if (!NearCorridor(b.center)) continue;
                blocked.Add(new Vector3(b.center.x, rad, b.center.z));
            }
        }
        CollectFrom("Waterfront Blocks");
        CollectFrom("Port City");
        CollectFrom("Planted Median");
        CollectFrom("Kerb Planters");
        CollectFrom("Modern Skyline");
        int sceneObstacles = blocked.Count;

        bool Free(Vector3 q, float radius)
        {
            for (int k = 0; k < blocked.Count; k++)
            {
                float dx = q.x - blocked[k].x, dz = q.z - blocked[k].z;
                float rr = blocked[k].y + radius;
                if (dx * dx + dz * dz < rr * rr) return false;
            }
            return true;
        }

        var rng = new System.Random(5140922);
        int stalls = 0, cafeSets = 0, tents = 0, crates = 0, planters = 0;
        int sitters = 0, browsers = 0, strollers = 0;
        bool crowdReady = MinatoCrowdPopulation.Ready;
        if (!crowdReady)
        {
            MinatoCrowdPopulation.Prepare();
            crowdReady = MinatoCrowdPopulation.Ready;
        }
        var pedKeys = crowdReady ? MinatoCrowdPopulation.PedestrianKeys : null;
        const string SitKey = "06_Sit_Coral";

        // Place one prop. Returns the placed GameObject, or null if the spot was taken, in the
        // sea, or off the walkable surface.
        GameObject Place(GameObject src, Transform bucket, Vector3 q, Quaternion rot,
                         float footprint, float scale, out float groundY)
        {
            groundY = 0f;
            if (src == null) return null;
            if (!Free(q, footprint)) return null;
            if (!GroundByRenderedSurface(aprons, terrainGround, q, out float y, out _))
                y = float.NegativeInfinity;
            if (y < SeaLevelY + 0.4f) return null;
            var go = Inst(src, bucket, Vector3.zero, rot, null);
            go.transform.position = new Vector3(q.x, y - 0.03f, q.z);
            go.transform.localScale = Vector3.one * scale;
            RetintMarket(go);
            go.isStatic = true;
            blocked.Add(new Vector3(q.x, footprint, q.z));
            groundY = y;
            return go;
        }

        void Figure(string key, Vector3 q, Quaternion rot, float scale, bool bench)
        {
            if (!crowdReady) return;
            if (!GroundByRenderedSurface(aprons, terrainGround, q, out float y, out _)) return;
            if (y < SeaLevelY + 0.4f) return;
            MinatoCrowdPopulation.Spawn(key, shopperRoot, new Vector3(q.x, y, q.z), rot, scale,
                                        null, Vector3.zero, 0f, 0f, bench);
        }

        for (float d = MarketStartM; d < MarketEndM;
             d += MarketClusterSpacingM + (float)rng.NextDouble() * 15f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + 14f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) continue;
            fwd.Normalize();

            // The market lives on the CITY side of the boulevard, never the working seaward quay.
            var outward = side * -SeaSideSign(route, i);
            // Prop +Z is its open front, which must look back at the road.
            var faceRoad = Quaternion.LookRotation(-outward, Vector3.up);

            double kind = rng.NextDouble();
            float band = Mathf.Lerp(MarketInnerOffsetM, MarketOuterOffsetM - 5f,
                                    (float)rng.NextDouble());

            if (kind < 0.42)
            {
                // --- STALL ROW: a run of booths shoulder to shoulder facing the promenade.
                int n = 3 + rng.Next(4);
                float lead = ((float)rng.NextDouble() - 0.5f) * 8f;
                for (int k = 0; k < n; k++)
                {
                    float lat = band + ((float)rng.NextDouble() - 0.5f) * 1.4f;
                    var q = p + outward * lat + fwd * (lead + k * StallRowPitchM);
                    var rot = faceRoad * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 14f, 0f);
                    if (Place(stallModels[rng.Next(stallModels.Length)], stallRoot, q, rot, MarketStallFootprintM,
                              0.98f + (float)rng.NextDouble() * 0.08f, out float y) == null) continue;
                    stalls++;
                    // Browsers at the counter, facing the stall.
                    if (crowdReady)
                    {
                        int b = rng.NextDouble() < 0.72 ? 1 + rng.Next(2) : 0;
                        for (int m = 0; m < b; m++)
                        {
                            var bq = q - outward * (2.1f + (float)rng.NextDouble() * 1.1f)
                                       + fwd * (((float)rng.NextDouble() - 0.5f) * 2.4f);
                            Figure(pedKeys[rng.Next(pedKeys.Length)], bq,
                                   Quaternion.LookRotation(outward, Vector3.up),
                                   0.94f + (float)rng.NextDouble() * 0.14f, true);
                            browsers++;
                        }
                    }
                }
                // Goods spilling out at the ends of the row.
                for (int k = 0; k < 2; k++)
                {
                    var q = p + outward * (band - 2.4f - (float)rng.NextDouble() * 1.4f)
                              + fwd * (lead + (k == 0 ? -2.4f : n * StallRowPitchM + 1.2f));
                    if (Place(crateModel, fillRoot, q, Quaternion.Euler(
                            0f, (float)rng.NextDouble() * 360f, 0f), 1.15f, 1f, out _) != null)
                        crates++;
                }
            }
            else if (kind < 0.78)
            {
                // --- CAFE TERRACE: a loose scatter of parasol tables with drinkers.
                int n = 4 + rng.Next(5);
                for (int k = 0; k < n; k++)
                {
                    float lat = band + ((k % 2) * CafeSetPitchM)
                                     + ((float)rng.NextDouble() - 0.5f) * 1.6f;
                    var q = p + outward * lat
                              + fwd * ((k / 2) * CafeSetPitchM - n * 0.35f
                                       + ((float)rng.NextDouble() - 0.5f) * 1.5f);
                    var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    var set = Place(cafeModels[rng.Next(cafeModels.Length)], cafeRoot, q, rot,
                                    MarketCafeFootprintM, 0.97f + (float)rng.NextDouble() * 0.08f, out float y);
                    if (set == null) continue;
                    cafeSets++;
                    if (!crowdReady) continue;
                    // Seat 1-3 of the three chairs. The chair ring is mirrored from the asset,
                    // so the sitter lands on a chair rather than on the paving beside it.
                    for (int c = 0; c < CafeChairCount; c++)
                    {
                        if (rng.NextDouble() > 0.62) continue;
                        float a = (c / (float)CafeChairCount) * Mathf.PI * 2f + CafeChairPhaseRad;
                        var localOut = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var worldOut = rot * localOut;
                        var cq = q + worldOut * CafeChairRingM;
                        var look = SitterFacesLocalMinusZ ? worldOut : -worldOut;
                        Figure(SitKey, cq, Quaternion.LookRotation(look, Vector3.up), 0.97f, false);
                        sitters++;
                    }
                }
            }
            else
            {
                // --- TENT GROUP: peaked pop-up canopies with produce piles under them.
                int n = 1 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    var q = p + outward * (band + ((float)rng.NextDouble() - 0.5f) * 3.0f)
                              + fwd * (k * 4.4f + ((float)rng.NextDouble() - 0.5f) * 2.0f);
                    var rot = faceRoad * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 30f, 0f);
                    if (Place(tentModel, stallRoot, q, rot, MarketTentFootprintM,
                              0.98f + (float)rng.NextDouble() * 0.10f, out _) == null) continue;
                    tents++;
                    if (Place(crateModel, fillRoot, q - outward * 1.15f, Quaternion.Euler(
                            0f, (float)rng.NextDouble() * 360f, 0f), 0.95f, 1f, out _) != null)
                        crates++;
                    if (crowdReady && rng.NextDouble() < 0.8)
                    {
                        var bq = q - outward * (2.6f + (float)rng.NextDouble() * 1.0f);
                        Figure(pedKeys[rng.Next(pedKeys.Length)], bq,
                               Quaternion.LookRotation(outward, Vector3.up), 0.96f, true);
                        browsers++;
                    }
                }
                // A planter or two to soften the group edge - reuses the M1 kerb planter.
                if (Place(planterModel, fillRoot,
                          p + outward * (band - 2.8f) + fwd * (n * 4.4f + 2.2f),
                          Quaternion.LookRotation(fwd, Vector3.up), 1.4f, 1f, out _) != null)
                    planters++;
            }

            // --- strollers moving through the promenade between the clusters.
            if (crowdReady)
            {
                int walkers = 2 + rng.Next(4);
                for (int k = 0; k < walkers; k++)
                {
                    float lat = Mathf.Lerp(MarketInnerOffsetM - 3.0f, MarketOuterOffsetM,
                                           (float)rng.NextDouble());
                    var q = p + outward * lat + fwd * (((float)rng.NextDouble() - 0.5f) * 26f);
                    var key = pedKeys[rng.Next(pedKeys.Length)];
                    bool moving = key.Contains("_Walk_");
                    var axis = rng.NextDouble() < 0.5 ? fwd : -fwd;
                    if (!GroundByRenderedSurface(aprons, terrainGround, q, out float y, out _)) continue;
                    if (y < SeaLevelY + 0.4f) continue;
                    MinatoCrowdPopulation.Spawn(
                        key, shopperRoot, new Vector3(q.x, y, q.z),
                        moving ? Quaternion.LookRotation(axis, Vector3.up)
                               : Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                        0.94f + (float)rng.NextDouble() * 0.16f, null,
                        moving ? axis : Vector3.zero,
                        moving ? 5f + (float)rng.NextDouble() * 7f : 0f,
                        moving ? 0.58f + (float)rng.NextDouble() * 0.32f : 0f);
                    strollers++;
                }
            }
        }

        // ---------------------------------------------------------------------------------
        // DEEP PLAZA TIER. Same vocabulary, looser spacing, pushed out into the tile apron
        // between the street wall and the tower district (the target_skyline framing). Every
        // candidate still goes through the same Free()/GroundByRenderedSurface gate, so it
        // simply finds nothing wherever a block, quay or skyline tower already stands.
        // ---------------------------------------------------------------------------------
        int deepSets = 0, deepTents = 0, deepStalls = 0;
        for (float d = MarketStartM; d < MarketEndM;
             d += MarketDeepSpacingM + (float)rng.NextDouble() * 20f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            int j = route.IndexAt(Mathf.Min(d + 14f, route.Length - 1f));
            var fwd = Vector3.ProjectOnPlane(route.Position[j] - p, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) continue;
            fwd.Normalize();
            var outward = route.SideFlat(i) * -SeaSideSign(route, i);
            var faceRoad = Quaternion.LookRotation(-outward, Vector3.up);

            float deepBand = Mathf.Lerp(MarketDeepInnerM, MarketDeepOuterM,
                                        (float)rng.NextDouble());
            double kind = rng.NextDouble();

            if (kind < 0.55)
            {
                int n = 3 + rng.Next(4);
                for (int k = 0; k < n; k++)
                {
                    var q = p + outward * (deepBand + ((k % 2) * CafeSetPitchM)
                                           + ((float)rng.NextDouble() - 0.5f) * 2.2f)
                              + fwd * ((k / 2) * CafeSetPitchM + ((float)rng.NextDouble() - 0.5f) * 3f);
                    var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    if (Place(cafeModels[rng.Next(cafeModels.Length)], cafeRoot, q, rot, MarketCafeFootprintM,
                              0.97f + (float)rng.NextDouble() * 0.08f, out _) == null) continue;
                    cafeSets++; deepSets++;
                    if (!crowdReady) continue;
                    for (int c = 0; c < CafeChairCount; c++)
                    {
                        if (rng.NextDouble() > 0.55) continue;
                        float a = (c / (float)CafeChairCount) * Mathf.PI * 2f + CafeChairPhaseRad;
                        var worldOut = rot * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var look = SitterFacesLocalMinusZ ? worldOut : -worldOut;
                        Figure(SitKey, q + worldOut * CafeChairRingM,
                               Quaternion.LookRotation(look, Vector3.up), 0.97f, false);
                        sitters++;
                    }
                }
            }
            else if (kind < 0.82)
            {
                int n = 2 + rng.Next(4);
                float lead = ((float)rng.NextDouble() - 0.5f) * 8f;
                for (int k = 0; k < n; k++)
                {
                    var q = p + outward * (deepBand + ((float)rng.NextDouble() - 0.5f) * 1.6f)
                              + fwd * (lead + k * StallRowPitchM);
                    var rot = faceRoad * Quaternion.Euler(
                        0f, (float)(rng.NextDouble() - 0.5) * 24f, 0f);
                    if (Place(stallModels[rng.Next(stallModels.Length)], stallRoot, q, rot, MarketStallFootprintM,
                              0.98f + (float)rng.NextDouble() * 0.08f, out _) == null) continue;
                    stalls++; deepStalls++;
                    if (crowdReady && rng.NextDouble() < 0.7)
                    {
                        Figure(pedKeys[rng.Next(pedKeys.Length)],
                               q - outward * (2.1f + (float)rng.NextDouble() * 1.2f),
                               Quaternion.LookRotation(outward, Vector3.up), 0.95f, true);
                        browsers++;
                    }
                }
            }
            else
            {
                int n = 1 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    var q = p + outward * (deepBand + ((float)rng.NextDouble() - 0.5f) * 4f)
                              + fwd * (k * 4.6f + ((float)rng.NextDouble() - 0.5f) * 2.5f);
                    var rot = faceRoad * Quaternion.Euler(
                        0f, (float)(rng.NextDouble() - 0.5) * 40f, 0f);
                    if (Place(tentModel, stallRoot, q, rot, MarketTentFootprintM,
                              0.98f + (float)rng.NextDouble() * 0.10f, out _) == null) continue;
                    tents++; deepTents++;
                    if (Place(crateModel, fillRoot, q - outward * 1.15f,
                              Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                              0.95f, 1f, out _) != null) crates++;
                }
            }

            // Plaza-crossing pedestrians so the deep tile never reads as an empty car park.
            if (crowdReady)
            {
                int walkers = 3 + rng.Next(5);
                for (int k = 0; k < walkers; k++)
                {
                    float lat = Mathf.Lerp(MarketDeepInnerM - 4f, MarketDeepOuterM + 14f,
                                           (float)rng.NextDouble());
                    var q = p + outward * lat + fwd * (((float)rng.NextDouble() - 0.5f) * 30f);
                    var key = pedKeys[rng.Next(pedKeys.Length)];
                    bool moving = key.Contains("_Walk_");
                    var axis = (rng.NextDouble() < 0.5 ? fwd : -fwd);
                    if (rng.NextDouble() < 0.4) axis = (rng.NextDouble() < 0.5 ? outward : -outward);
                    if (!GroundByRenderedSurface(aprons, terrainGround, q, out float y, out _)) continue;
                    if (y < SeaLevelY + 0.4f) continue;
                    if (!Free(q, 0.6f)) continue;
                    MinatoCrowdPopulation.Spawn(
                        key, shopperRoot, new Vector3(q.x, y, q.z),
                        moving ? Quaternion.LookRotation(axis, Vector3.up)
                               : Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                        0.94f + (float)rng.NextDouble() * 0.16f, null,
                        moving ? axis : Vector3.zero,
                        moving ? 5f + (float)rng.NextDouble() * 7f : 0f,
                        moving ? 0.58f + (float)rng.NextDouble() * 0.32f : 0f);
                    strollers++;
                }
            }
        }

        Debug.Log($"[minato] market district {stalls} stalls, {tents} tents, {cafeSets} cafe sets, " +
                  $"{crates} crate piles, {planters} planters on the city-side plaza " +
                  $"({MarketStartM:N0}-{MarketEndM:N0} m, {MarketInnerOffsetM:N1}-" +
                  $"{MarketOuterOffsetM:N1} m out, rejected against {sceneObstacles:N0} existing " +
                  $"scene obstacles); crowd {sitters} seated, {browsers} browsing, " +
                  $"{strollers} strolling; deep plaza tier {deepStalls} stalls, {deepTents} tents, " +
                  $"{deepSets} cafe sets ({MarketDeepInnerM:N0}-{MarketDeepOuterM:N0} m out)");
    }

    /// <summary>
    /// THE CITY BEACH. A bright sand shelf on the seaward side between
    /// <see cref="BeachStartM"/> and <see cref="BeachEndM"/>, with parasols and beachgoers. The
    /// working apron, quay wall and crane line are suppressed over this window (see
    /// <see cref="InBeach"/>) so the bay opens up instead of being walled off by a dock.
    /// </summary>
    private static void BuildCityBeach(MinatoRoute route, Transform chapter)
    {
        var parent = new GameObject("City Beach").transform;
        parent.SetParent(chapter, false);

        var sand = CelMaterial("Minato_BeachSand", new Color(1.00f, 0.82f, 0.52f),
                               gloss: 0.06f, spec: 0.04f, rim: 0.18f,
                               texture: Tex(ShiosaiTex, "Shiosai_Sand_Albedo.png"),
                               shade: new Color(0.80f, 0.84f, 0.92f, 1f));

        // Per-station: find the waterline by marching seaward, then lay a sand ribbon from the
        // backshore down into the shallows.
        int n = Mathf.CeilToInt((BeachEndM - BeachStartM) / 20f);
        const int lanes = 12;
        int stride = lanes + 1;
        var verts = new Vector3[(n + 1) * stride];
        var uvs = new Vector2[verts.Length];
        int found = 0;
        for (int k = 0; k <= n; k++)
        {
            float d = Mathf.Lerp(BeachStartM, BeachEndM, k / (float)n);
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var sea = route.SideFlat(i) * SeaSideSign(route, i);

            float waterline = -1f;
            for (float off = 18f; off < 620f; off += 6f)
            {
                var probe = p + sea * off;
                if (GroundAt(route, probe.x, probe.z) < SeaLevelY + 0.9f) { waterline = off; break; }
            }
            if (waterline > 0f) found++;
            else waterline = 150f;                                   // fallback so the strip closes

            float inner = Mathf.Max(9f, waterline - 195f);           // wider dry-sand backshore
            float outer = waterline + 50f;

            for (int lane = 0; lane <= lanes; lane++)
            {
                float t = lane / (float)lanes;
                float off = Mathf.Lerp(inner, outer, t);
                var q = p + sea * off;
                // The sand CONFORMS to the terrain (+12 cm) instead of following a synthetic
                // profile. A synthetic slope buried most of the ribbon under the generated
                // ground and only a lens of sand poked through - the frame read as a lawn
                // running into the sea. Past the waterline it drops into the shallows.
                float y = BeachSandY(route, q, off, waterline);
                int v = k * stride + lane;
                verts[v] = new Vector3(q.x, y, q.z);
                uvs[v] = new Vector2((off - inner) * 0.10f, d * 0.10f);
            }
        }
        var tris = new int[n * lanes * 6];
        for (int k = 0; k < n; k++)
            for (int lane = 0; lane < lanes; lane++)
            {
                int v = k * stride + lane;
                int t = (k * lanes + lane) * 6;
                // Same winding as PortApronMesh: route-forward x route-side points DOWN for this
                // convention, so the triangles are reversed to face the sky.
                tris[t] = v; tris[t + 1] = v + stride + 1; tris[t + 2] = v + stride;
                tris[t + 3] = v; tris[t + 4] = v + 1; tris[t + 5] = v + stride + 1;
            }
        var mesh = new Mesh { name = "Minato_City_BeachSand" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        // Runtime walkers ground against the visible sand itself. Using the terrain collider
        // below it recreates the ankle sinking that prompted the crowd rebuild.
        var beach = AddMesh(parent, "Beach Sand", mesh, sand, collider: true);
        beach.isStatic = true;

        // --- parasols and beachgoers on the dry sand.
        var parasolSrc = Model("Minato_Beach_Parasol");
        // Beachgoers use the same approved MinatoNPC archetypes as the boulevard crowd (no
        // cyclists on the sand). Prepare() is idempotent, so calling it again after BuildCityLife
        // simply reuses the already-baked templates.
        MinatoCrowdPopulation.Prepare();
        var sitterKeys = MinatoCrowdPopulation.BeachKeys;
        var canopy = CelMaterial("Minato_Beach_Canopy", new Color(0.98f, 0.42f, 0.32f),
                                 gloss: 0.16f, spec: 0.10f, rim: 0.34f);
        var pole = CelMaterial("Minato_Beach_Pole", new Color(0.90f, 0.90f, 0.90f),
                               gloss: 0.30f, spec: 0.20f, rim: 0.30f);
        var towel = CelMaterial("Minato_Beach_Towel", new Color(0.30f, 0.80f, 0.88f),
                                gloss: 0.10f, spec: 0.06f, rim: 0.24f);

        var rng = new System.Random(31337);
        int parasols = 0, bathers = 0;
        for (float d = BeachStartM + 25f; d < BeachEndM - 25f; d += 17f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var sea = route.SideFlat(i) * SeaSideSign(route, i);

            float waterline = -1f;
            for (float off = 18f; off < 620f; off += 6f)
            {
                var probe = p + sea * off;
                if (GroundAt(route, probe.x, probe.z) < SeaLevelY + 0.9f) { waterline = off; break; }
            }
            if (waterline < 0f) waterline = 150f;
            float inner = Mathf.Max(9f, waterline - 195f);
            float outer = waterline + 50f;

            // Sample the SAME conforming profile the sand ribbon uses, so nothing floats.
            float SandY(Vector3 at, float off) => BeachSandY(route, at, off, waterline);

            if (parasolSrc != null && rng.NextDouble() < 0.58)
            {
                float off = inner + 12f + (float)rng.NextDouble() * 52f;
                var q = p + sea * off;
                float y = SandY(q, off);
                if (y > SeaLevelY + 0.15f)
                {
                    var go = Inst(parasolSrc, parent, Vector3.zero,
                                  Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), null);
                    go.transform.position = new Vector3(q.x, y, q.z);
                    go.transform.localScale = Vector3.one * (0.95f + (float)rng.NextDouble() * 0.25f);
                    RetintBySlot(go, new[] { ("parasol", canopy), ("cloth_top", towel),
                                             ("lamp_grey", pole) }, canopy);
                    go.isStatic = true;
                    parasols++;
                }
            }

            int group = 1 + rng.Next(4);
            for (int k = 0; k < group; k++)
            {
                float off = inner + 6f + (float)rng.NextDouble() * 96f;
                var q = p + sea * off + route.Tangent[i].normalized *
                        (((float)rng.NextDouble() - 0.5f) * 12f);
                float y = SandY(q, off);
                if (y < SeaLevelY + 0.05f) continue;                 // in the water
                var src = sitterKeys[rng.Next(sitterKeys.Length)];
                bool moving = src.Contains("_Walk_");
                var beachAxis = route.Tangent[i].normalized;
                if (rng.NextDouble() < 0.5) beachAxis = -beachAxis;
                var go = MinatoCrowdPopulation.Spawn(
                    src, parent, new Vector3(q.x, y, q.z),
                    moving ? Quaternion.LookRotation(beachAxis, Vector3.up) :
                             Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                    0.92f + (float)rng.NextDouble() * 0.18f, null,
                    moving ? beachAxis : Vector3.zero,
                    moving ? 5f + (float)rng.NextDouble() * 7f : 0f,
                    moving ? 0.58f + (float)rng.NextDouble() * 0.30f : 0f);
                if (go != null) bathers++;
            }
        }

        Debug.Log($"[minato] city beach {BeachStartM:N0}-{BeachEndM:N0} m " +
                  $"({found}/{n + 1} stations found a waterline), {parasols} parasols, " +
                  $"{bathers} beachgoers");
    }

    private static void BuildAuthoredPortDistrict(MinatoRoute route, Transform chapter)
    {
        var oldBlocks = chapter.Find("Port City/Waterfront Blocks");
        if (oldBlocks != null)
            UnityEngine.Object.DestroyImmediate(oldBlocks.gameObject);

        var prefab = Glb(MinatoGlb, "Minato_Port_HeroDistrict");
        if (prefab == null)
        {
            Debug.LogError("[minato] authored port district GLB missing");
            return;
        }

        int i = route.IndexAt(900f);
        var tangent = Vector3.ProjectOnPlane(route.Tangent[i], Vector3.up).normalized;
        var side = route.SideFlat(i);

        // BUG FIX (QA #1, critical): this hero GLB was instanced with ZERO lateral offset,
        // directly ON the route centreline, unlike every other building placer in this file
        // (see BuildPortCity's `side`/`CorridorClearM` pattern). Its own local-space bounds
        // (measured from the authored GLB, pivot-relative) span X [-275.6, +218.0] m - i.e. up
        // to 275.6 m of solid facade/roof mesh on ONE side of its own pivot alone - so at zero
        // offset its footprint blankets the corridor for its entire ~1950 m along-route length,
        // which is exactly what filled the view near the route opening (d~140-240 m, long
        // before the d=903 m anchor). A small nudge cannot fix this: the pivot must move by
        // more than the FARTHEST local-X extent (218.0 m) plus the corridor half-width
        // (RoadHalfWidth + ShoulderW = 4.55 m) so the ENTIRE footprint clears, not just the
        // pivot. CorridorClearHeroM below clears the worse-case direction (280.15 m) with
        // margin. Moved to the LANDWARD ("city") side, matching this file's `-SeaSideSign`
        // convention (see ScatterDressing's `city = side * -SeaSideSign`), so the district
        // lands on solid ground rather than out over the bay.
        const float CorridorClearHeroM = 300f;
        var cityDir = side * -SeaSideSign(route, i);
        var anchor = route.Position[i] + cityDir * CorridorClearHeroM;
        anchor.y = GroundAt(route, anchor.x, anchor.z) + 0.03f;
        var district = Inst(prefab, chapter, anchor,
                            Quaternion.LookRotation(tangent, Vector3.up), null);
        district.name = "Authored Port District";
        RetintAuthoredPort(district);

        var approachPrefab = Glb(MinatoGlb, "Minato_Port_BridgeApproach");
        GameObject approach = null;
        if (approachPrefab != null)
        {
            int ai = route.IndexAt(2070f);
            var at = Vector3.ProjectOnPlane(route.Tangent[ai], Vector3.up).normalized;
            var ap = route.Position[ai];
            ap.y = GroundAt(route, ap.x, ap.z) + 0.03f;
            approach = Inst(approachPrefab, chapter, ap,
                            Quaternion.LookRotation(at, Vector3.up), null);
            approach.name = "Authored Bridge Approach";
            RetintAuthoredPort(approach);
        }

        bool any = false;
        Bounds bounds = default;
        foreach (var renderer in district.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        // Corridor-clearance proof (QA #1 fix verification): project every renderer's world
        // bounds onto the LOCAL route-lateral axis at the anchor station and report the closest
        // approach to the centreline across the whole district, not just the pivot-to-centreline
        // distance (a small pivot nudge would still leave the far edge of a ~500 m-wide mesh
        // sitting on the corridor).
        float corridorHalfWidth = RoadHalfWidth + ShoulderW;
        float minAbsLateral = float.MaxValue;
        foreach (var renderer in district.GetComponentsInChildren<Renderer>(true))
        {
            var b = renderer.bounds;
            for (int cx = 0; cx < 2; cx++)
            for (int cz = 0; cz < 2; cz++)
            {
                var corner = new Vector3(cx == 0 ? b.min.x : b.max.x, b.center.y, cz == 0 ? b.min.z : b.max.z);
                float lateral = (corner.x - route.Position[i].x) * side.x + (corner.z - route.Position[i].z) * side.z;
                minAbsLateral = Mathf.Min(minAbsLateral, Mathf.Abs(lateral));
            }
        }
        bool clear = minAbsLateral > corridorHalfWidth;
        Debug.Log($"[minato] authored port district bounds center={bounds.center} size={bounds.size}; " +
                  $"closest footprint approach to centreline={minAbsLateral:N1} m " +
                  $"(corridor half-width={corridorHalfWidth:N2} m) -> CLEAR={clear}; " +
                  "replaced repeated waterfront blocks, preserved quay/marina/industrial systems");
        if (!clear)
            Debug.LogError("[minato] authored port district STILL intrudes on the rideable corridor " +
                            $"after relocation - closest approach {minAbsLateral:N1} m <= half-width " +
                            $"{corridorHalfWidth:N2} m.");

        if (approach != null)
            foreach (var renderer in approach.GetComponentsInChildren<Renderer>(true))
                bounds.Encapsulate(renderer.bounds);
    }

    private static void ClearPortScatter(MinatoRoute route, Transform coast)
    {
        int removed = 0;
        for (int c = coast.childCount - 1; c >= 0; c--)
        {
            var child = coast.GetChild(c);
            float lateral = NearestLand(route, child.position.x, child.position.z, out int index);
            if (index < 0 || route.Distance[index] > 2320f || lateral > 360f) continue;
            UnityEngine.Object.DestroyImmediate(child.gameObject);
            removed++;
        }
        Debug.Log($"[minato] cleared {removed} generic coast scatter objects from the authored port footprint");
    }

    private static void RetintAuthoredPort(GameObject root)
    {
        var facades = SettlementFacades();
        var roofs = SettlementRoofs();
        var metal = SteelMaterial();
        var concrete = ConcreteMaterial();
        var accent = CelMaterial("Minato_PortHero_Accent", new Color(0.82f, 0.30f, 0.20f),
                                 0.22f, 0.14f, 0.32f,
                                 Tex(ShiosaiTex, "Shiosai_HarbourRoof_Albedo_v2.png"), GroundShade);
        var glass = CelMaterial("Minato_PortHero_Glass", new Color(0.22f, 0.43f, 0.54f),
                                0.62f, 0.48f, 0.42f, null, GroundShade);
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = renderer.name;
            Material material =
                n.Contains("FacadeLight") ? facades[0] :
                n.Contains("FacadeWarm") ? facades[4] :
                n.Contains("FacadeSlate") ? facades[5] :
                n.Contains("FacadeColor") ? facades[2] :
                n.Contains("Roof") ? roofs[1] :
                n.Contains("Glass") ? glass :
                n.Contains("Concrete") ? concrete :
                n.Contains("Accent") ? accent :
                metal;
            var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
            for (int m = 0; m < materials.Length; m++) materials[m] = material;
            renderer.sharedMaterials = materials;
        }
    }

    /// <summary>
    /// One continuous, conforming industrial apron under the port kit. This is deliberately a
    /// single authored ribbon rather than dozens of primitive pads, and it uses Maple City's
    /// production pavement family so the cranes/warehouses read as a connected working dock
    /// instead of props scattered over grass.
    /// </summary>
    private static Mesh PortApronMesh(MinatoRoute route, float startM, float endM,
                                      float innerOffset, float outerOffset)
    {
        int n = Mathf.CeilToInt((endM - startM) / 24f);
        // The old two-edge ribbon only conformed at its boundaries. Across a 300 m apron the
        // generated terrain could arch above that plane, leaving broad grassy islands through
        // the middle of the port. Sample a lateral grid so the pavement follows the ground.
        int lanes = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(outerOffset - innerOffset) / 18f));
        int stride = lanes + 1;
        var verts = new Vector3[(n + 1) * stride];
        var uvs = new Vector2[verts.Length];
        for (int k = 0; k <= n; k++)
        {
            float d = Mathf.Lerp(startM, endM, k / (float)n);
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var sea = route.SideFlat(i) * SeaSideSign(route, i);
            for (int lane = 0; lane <= lanes; lane++)
            {
                float t = lane / (float)lanes;
                float offset = Mathf.Lerp(innerOffset, outerOffset, t);
                var q = p + sea * offset;
                q.y = Mathf.Max(SeaLevelY + 0.65f, GroundAt(route, q.x, q.z) + 0.38f);
                int v = k * stride + lane;
                verts[v] = q;
                uvs[v] = new Vector2((offset - innerOffset) * 0.08f, d * 0.08f);
            }
        }
        var tris = new int[n * lanes * 6];
        for (int k = 0; k < n; k++)
            for (int lane = 0; lane < lanes; lane++)
        {
            int v = k * stride + lane;
            int t = (k * lanes + lane) * 6;
            // Route-forward x route-side points down for this mesh convention. Reverse the
            // winding so the production pavement faces the route cameras instead of being
            // backface-culled and exposing the generated grass beneath it.
            tris[t] = v; tris[t + 1] = v + stride + 1; tris[t + 2] = v + stride;
            tris[t + 3] = v; tris[t + 4] = v + 1; tris[t + 5] = v + stride + 1;
        }
        // SELF-FOLD GUARD (the "grey wall filling the sky" at KM ~0.2). A fixed 1360 m lateral
        // reach cannot follow a bend tighter than that radius: on the inside of the curve the far
        // rows cross back over each other, and the triangles bridging them - between points whose
        // sampled ground heights differ by up to ~29 m - stand up as a near-vertical striped wall
        // right in the chase camera's sky (see ASSET_CATALOG "Known gotchas"). A pavement is never
        // steeper than ~45 deg and never faces down, so any such triangle is fold debris: drop it.
        // The grass terrain underneath still covers the ground there.
        // ROAD GUARD (the pale "white shader on the road" at route ~256-266 m): on the inside of
        // the opening bend the apron's near edge (6.2 m) swings across the carriageway itself.
        // Drop any triangle whose centre lies within the road corridor of ANY nearby station.
        var corridor = new List<Vector3>();
        for (float s = Mathf.Max(0f, startM - 150f); s <= Mathf.Min(route.Length - 1f, endM + 150f); s += 3f)
            corridor.Add(route.Position[route.IndexAt(s)]);
        float clearSq = (RoadHalfWidth + ShoulderW + 0.6f) * (RoadHalfWidth + ShoulderW + 0.6f);
        bool OnRoad(Vector3 c)
        {
            foreach (var q in corridor)
            {
                float dx = c.x - q.x, dz = c.z - q.z;
                if (dx * dx + dz * dz < clearSq) return true;
            }
            return false;
        }

        var kept = new List<int>(tris.Length);
        int culled = 0, onRoad = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            var n0 = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]);
            if (n0.sqrMagnitude < 1e-8f || n0.normalized.y < 0.7f) { culled++; continue; }
            if (OnRoad((verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]) / 3f) ||
                OnRoad(verts[tris[t]]) || OnRoad(verts[tris[t + 1]]) || OnRoad(verts[tris[t + 2]]))
            { onRoad++; continue; }
            kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]);
        }
        if (culled > 0 || onRoad > 0)
            Debug.Log($"[minato] port apron {startM:0}-{endM:0} m: dropped {culled} folded/steep " +
                      $"and {onRoad} on-road triangles of {tris.Length / 3}.");
        tris = kept.ToArray();

        var mesh = new Mesh { name = "Minato_Port_ServiceApron" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Sea traffic and islands across the crossing, so chapter 03 reads as a real strait with
    /// depth cues rather than empty water. Everything is 300 m or further from the deck, i.e.
    /// silhouette-tier, and fades through the maritime haze.
    /// </summary>
    private static void BuildOceanTraffic(MinatoRoute route, Transform chapter)
    {
        var parent = new GameObject("Sea Traffic and Islands").transform;
        parent.SetParent(chapter, false);

        var steel = SteelMaterial();
        var rock = CelMaterial("Minato_Rock", new Color(0.62f, 0.60f, 0.57f), gloss: 0.10f,
                               spec: 0.06f, rim: 0.35f,
                               texture: Tex(ShiosaiTex, "Shiosai_Rock_Albedo.png"), shade: GroundShade);

        var isletRock = CelMaterial("Minato_IsletRock", new Color(0.50f, 0.53f, 0.52f), gloss: 0.08f,
                                    spec: 0.05f, rim: 0.30f,
                                    texture: Tex(TakaTex, "Taka_Granite_Albedo.png"), shade: GroundShade);
        // build_minato_islets.py gives every canopy clump clump-height UVs for the same painted
        // dark-below / sunlit-crown gradient as the boulevard bushes.
        var isletCanopy = MedianFoliageMaterial();
        var vessels = new[] { Model("Minato_Sea_CargoShip"), Model("Minato_Sea_Ferry"),
                              Model("Minato_Sea_Sailboat"), Model("Minato_Sea_FishingBoat") };
        var islets = new[] { Model("Minato_Sea_IsletA"), Model("Minato_Sea_IsletB"),
                             Glb(ShiosaiGlb, "Shiosai_SeaStack_A"), Glb(ShiosaiGlb, "Shiosai_SeaStack_B") };

        var rng = new System.Random(9031);
        int ships = 0, isles = 0;

        for (float d = SeaCrossStartM; d < BridgeEndM; d += 130f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * SeaSideSign(route, i);
            var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            if (rng.NextDouble() < 0.55)
            {
                var src = vessels[rng.Next(vessels.Length)];
                if (src != null)
                {
                    float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float off = 110f + (float)rng.NextDouble() * 650f;   // CONDENSED 2026-09-24 (was 320-2620)
                    var q = p + side * (sgn * off);
                    var go = Inst(src, parent, Vector3.zero, rot, null);
                    go.transform.position = new Vector3(q.x, SeaLevelY + 0.4f, q.z);
                    // Bigger with distance so the silhouette still reads through the haze.
                    go.transform.localScale = Vector3.one * (1.0f + off / 1400f);
                    go.isStatic = true; ships++;
                }
            }
            if (rng.NextDouble() < 0.22)
            {
                var src = islets[rng.Next(islets.Length)];
                if (src != null)
                {
                    float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float off = 300f + (float)rng.NextDouble() * 1100f;  // CONDENSED 2026-09-24 (was 900-4100)
                    var q = p + side * (sgn * off);
                    // The old islets were stacked boxes + cone trees painted with ONE stratified rock
                    // texture at 3.5-12.5x (up to ~1 km wide): brown wood-grain "sandcastles".
                    // Now sculpted rock + canopy clumps (build_minato_islets.py), granite and
                    // foliage by palette slot, at a size that reads as a wooded islet.
                    bool authoredIslet = src.name.StartsWith("Minato_Sea_Islet");
                    var go = Inst(src, parent, Vector3.zero, rot, null);
                    RetintBySlot(go, new[] { ("foliage", isletCanopy), ("rock", isletRock) }, isletRock);
                    go.transform.localScale = Vector3.one * (authoredIslet
                        ? 0.9f + (float)rng.NextDouble() * 1.1f
                        : 2.5f + (float)rng.NextDouble() * 4f);
                    go.transform.position = new Vector3(q.x, SeaLevelY, q.z);
                    // Seat the base ~2.5 m below the waterline (after scaling) so the island rises
                    // out of the sea instead of hovering as a floating textured box.
                    SeatInWater(go, SeaLevelY - 2.5f);
                    go.isStatic = true; isles++;
                }
            }
        }
        Debug.Log($"[minato] sea traffic {ships} vessels and {isles} islands across the crossing " +
                  $"(all >= 320 m from the deck)");
    }

    /// <summary>
    /// Road engineering for the land chapters: masonry retaining walls on the cut side and rock
    /// outcrops in the cut face. This is what stops the far-shore valley and the mountain
    /// switchbacks reading as a road laid on bare ground.
    /// </summary>
    private static void BuildRoadEngineering(MinatoRoute route, Transform[] chapters, Material rock)
    {
        var concrete = ConcreteMaterial();
        var rng = new System.Random(66140);
        int walls = 0, cuts = 0;

        var wall = Model("Minato_Road_MasonryWall");
        var outcrop = Model("Minato_Road_RockOutcrop");

        Transform Root(int ch, string name)
        {
            var t = chapters[ch].Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(chapters[ch], false); }
            return t;
        }
        var shoreRoot = Root(3, "Road Engineering");
        var mtnRoot = Root(4, "Road Engineering");

        for (float d = BridgeEndM + 60f; d < route.Length - 40f; d += 6.4f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + 6.4f, route.Length - 1f));
            var fwd = (route.Position[j] - p); fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) continue;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            var root = d < 13400f ? shoreRoot : mtnRoot;
            // Chapter 05 now has a dedicated high-resolution visual shell that blends directly
            // into the road shoulder and carries authored talus/vegetation. The generic wall
            // kit is still appropriate on the far shore, but even its shortest bays read as a
            // dark continuous slab from the elevated 05V camera, so do not layer it over the
            // authored mountain.
            if (d >= 13600f) continue;
            // Far shore: GroundAt now limits the land to a bank beside the road (no trench), so
            // retaining walls only walled the road into a concrete slot (user: "very low
            // quality"). Rock-cut outcrops below still dress the uphill verge.
            bool noWalls = true;

            // Work out which side is the CUT and build there. The rise MUST be probed outside
            // RoadPinM: inside that radius GroundAt is pinned to the carriageway itself, so a
            // probe at the shoulder always reports zero rise and no wall is ever built.
            for (int s = -1; s <= 1; s += 2)
            {
                var probe = p + side * (s * (RoadEaseM + 4f));
                float rise = GroundAt(route, probe.x, probe.z) - p.y;
                if (rise < 6.0f) continue;      // fill side or gentle: nothing to retain, no wall
                var q = p + side * (s * (RoadHalfWidth + ShoulderW + 2.6f));
                float y = GroundAt(route, q.x, q.z);

                // Measured from the FBX: the module runs 6.58 m along its local +Z, is 2.68 m
                // thick in X and 5.54 m tall. LookRotation already maps local +Z to the road
                // tangent, so NO yaw correction belongs here - the 90 deg correction tried
                // earlier is exactly what laid the bays across the carriageway as tombstones.
                var src = wall;
                if (src == null || noWalls) goto Outcrop;
                var go = Inst(src, root, Vector3.zero, rot, concrete);
                go.transform.position = new Vector3(q.x, p.y - 0.4f, q.z);
                // Height clamp is modest: the old 0.8-2.6 range produced 14 m walls that stood
                // proud of the hillside instead of holding it back. PROVISIONAL.
                go.transform.localScale =
                    new Vector3(1f, Mathf.Clamp(rise / 5.0f, 0.55f, 1.70f), 1f);
                go.isStatic = true; walls++;
            Outcrop:

                // Exposed rock in the cut face just above the wall. Kept small and half buried:
                // the earlier version used the big cliff-ledge props at up to 2.7x, which read
                // as tan pancakes floating on the hillside.
                if (rise > 6f && rng.NextDouble() < 0.30)
                {
                    if (outcrop == null) continue;
                    var c = p + side * (s * (RoadHalfWidth + 10f + (float)rng.NextDouble() * 10f));
                    float cy = GroundAt(route, c.x, c.z);
                    var g2 = Inst(outcrop, root, Vector3.zero,
                                  Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), rock);
                    g2.transform.position = new Vector3(c.x, cy - 2.4f, c.z);
                    g2.transform.localScale = Vector3.one * (0.7f + (float)rng.NextDouble() * 0.7f);
                    g2.isStatic = true; cuts++;
                }
            }
        }
        Debug.Log($"[minato] road engineering {walls} retaining wall bays and {cuts} rock-cut " +
                  $"features on the uphill side of the far-shore and mountain chapters");
    }

    /// <summary>
    /// Dense waterfront port for chapters 1-2, so the road visibly DEPARTS a working port and
    /// aims at the crossing instead of starting in open grass.
    ///
    /// Everything here is reused Shiosai production geometry - six distinct harbour/house
    /// silhouettes, the breakwater as quay/seawall, fishing boats at the marina - plus the
    /// Minato lamp module and urban vegetation. No new boxes are authored, which is what keeps
    /// the near and midground clear of crude low-poly filler.
    /// </summary>
    private static void BuildPortCity(MinatoRoute route, Transform[] chapters, GameObject[] houses,
                                      Material rock, Material bark, Material leaf)
    {
        var parent = new GameObject("Port City").transform;
        parent.SetParent(chapters[0], false);
        var blockRoot = new GameObject("Waterfront Blocks").transform; blockRoot.SetParent(parent, false);
        var quayRoot = new GameObject("Quay and Seawall").transform; quayRoot.SetParent(parent, false);
        var marinaRoot = new GameObject("Marina").transform; marinaRoot.SetParent(parent, false);

        var facades = SettlementFacades();
        var roofs = SettlementRoofs();
        var concrete = ConcreteMaterial();
        var lampMat = SteelMaterial();

        var breakwater = Glb(ShiosaiGlb, "Shiosai_Breakwater");
        var boat = Glb(ShiosaiGlb, "Shiosai_FishingBoat");
        var lamp = Model("Minato_Bridge_Lamp");
        var broad = new[] { Glb(ShiosaiGlb, "Shiosai_Broadleaf"), Glb(ShiosaiGlb, "Shiosai_Broadleaf_B") };
        var shopHouse = Model("Minato_City_ShopHouse");

        var rng = new System.Random(77120);
        int blocks = 0, quays = 0, boats = 0, lamps = 0, street = 0;

        // PROVISIONAL extent. CorridorClearM was 13 m, which walled the carriageway in and hid
        // the harbour and ocean completely - the approach read as an inland urban canyon rather
        // than a coastal port. Pushed back so the water stays visible between the blocks.
        const float PortEndM = 2250f;
        const float CorridorClearM = 26f;   // never encroach on the cycling corridor

        for (float d = 40f; d < PortEndM; d += 16f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int j = route.IndexAt(Mathf.Min(d + 16f, route.Length - 1f));
            var fwd = (route.Position[j] - p);
            if (fwd.sqrMagnitude < 1e-4f) continue;

            // Two to three setback rows per side: a dense street wall, not a sparse sprinkle.
            for (int s = -1; s <= 1; s += 2)
                for (int row = 0; row < 3; row++)
                {
                    // The public beach window is kept open on the seaward side - a street wall
                    // there hides the sand, the bay and the ferris wheel from the ride.
                    if (InBeach(d) && s * SeaSideSign(route, i) > 0f) continue;
                    // Row 0 is deliberately gappy so the harbour reads THROUGH the street wall.
                    if (rng.NextDouble() > (row == 0 ? 0.70 : row == 1 ? 0.74 : 0.55)) continue;
                    float off = CorridorClearM + row * 26f + (float)rng.NextDouble() * 9f;
                    var q = p + side * (s * off);
                    float y = GroundAt(route, q.x, q.z);
                    if (y < SeaLevelY + 1.6f) continue;            // in the water
                    if (Mathf.Abs(y - p.y) > 26f) continue;

                    // Shiosai's production low-rise families hold the town silhouette. The
                    // authored Minato shop house is used sparingly in the deeper rows; the
                    // generic slab/tower blocks are deliberately excluded because they read as
                    // crude repeated boxes in the hero frame.
                    var src = row > 0 && shopHouse != null && rng.NextDouble() < 0.18
                        ? shopHouse
                        : houses[rng.Next(houses.Length)];
                    if (src == null) continue;
                    // Face the street.
                    var rot = Quaternion.LookRotation((-side * s).normalized, Vector3.up)
                              * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 16f, 0f);
                    var go = Inst(src, blockRoot, Vector3.zero, rot,
                                  facades[rng.Next(facades.Length)],
                                  roofs[rng.Next(roofs.Length)]);
                    go.transform.position = new Vector3(q.x, y - 0.35f, q.z);
                    float sc = 0.85f + (float)rng.NextDouble() * 0.45f;
                    go.transform.localScale =
                        new Vector3(sc * (0.82f + (float)rng.NextDouble() * 0.42f),
                                    sc * (0.78f + (float)rng.NextDouble() * 0.62f),
                                    sc * (0.82f + (float)rng.NextDouble() * 0.42f));
                    go.isStatic = true;
                    blocks++;
                }

            // Street lamps and urban trees on the pavement edge.
            if (lamp != null && d % 48f < 16f)
            {
                var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                var pos = p + side * -(RoadHalfWidth + ShoulderW + 0.30f);
                var l = Inst(lamp, parent, Vector3.zero, rot, lampMat);
                l.transform.position = pos; l.transform.rotation = rot; l.isStatic = true;
                lamps++;
            }
            if (rng.NextDouble() < 0.30)
            {
                var src = broad[rng.Next(broad.Length)];
                if (src != null)
                {
                    float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                    var q = p + side * (sgn * (CorridorClearM - 3.5f));
                    float y = GroundAt(route, q.x, q.z);
                    if (y > SeaLevelY + 1.4f)
                    {
                        var t = Inst(src, parent, Vector3.zero,
                                     Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), leaf, bark);
                        t.transform.position = new Vector3(q.x, y, q.z);
                        t.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.4f);
                        t.isStatic = true;
                        street++;
                    }
                }
            }
        }

        // Quay wall + moored boats along the waterline on the seaward side of the port.
        for (float d = 120f; d < PortEndM; d += 26f)
        {
            if (InBeach(d)) continue;              // the beach window opens onto sand, not a wall
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i) * SeaSideSign(route, i);

            // March seaward until the ground drops to the waterline - that is the quay edge.
            float found = -1f;
            for (float off = 26f; off < 620f; off += 6f)
            {
                var q = p + side * off;
                if (GroundAt(route, q.x, q.z) < SeaLevelY + 1.0f) { found = off; break; }
            }
            if (found < 0f) continue;

            var edge = p + side * found;
            int j = route.IndexAt(Mathf.Min(d + 26f, route.Length - 1f));
            var rot2 = Quaternion.LookRotation((route.Position[j] - p).normalized, Vector3.up);

            if (breakwater != null)
            {
                var w = Inst(breakwater, quayRoot, Vector3.zero, rot2, concrete);
                w.transform.position = new Vector3(edge.x, SeaLevelY + 0.6f, edge.z);
                w.transform.localScale = new Vector3(1.1f, 1.3f, 1.4f);
                w.isStatic = true;
                quays++;
            }
            if (boat != null && rng.NextDouble() < 0.55)
            {
                var b = Inst(boat, marinaRoot, Vector3.zero,
                             rot2 * Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 30f, 0f),
                             null);
                var bp = p + side * (found + 12f + (float)rng.NextDouble() * 22f);
                b.transform.position = new Vector3(bp.x, SeaLevelY + 0.35f, bp.z);
                b.transform.localScale = Vector3.one * (0.9f + (float)rng.NextDouble() * 0.5f);
                b.isStatic = true;
                boats++;
            }
        }

        Debug.Log($"[minato] port city {blocks} waterfront buildings, {quays} quay/seawall sections, " +
                  $"{boats} moored boats, {lamps} street lamps, {street} street trees");
    }

    /// <summary>
    /// Populated far-shore arrival using only Shiosai's production houses, trees and rock. The
    /// previous generic scatter placed one house at a time in parallel rows, leaving empty lawn
    /// between cloned silhouettes. This builds irregular hamlets around shared courtyards and
    /// tree masses, then leaves deliberate gaps for the road, coast and lighthouse composition.
    /// </summary>
    private static void BuildFarShoreSettlement(MinatoRoute route, Transform chapter,
                                                GameObject[] houses, Material rock,
                                                Material bark, Material leaf)
    {
        var parent = new GameObject("Coastal Arrival Settlement").transform;
        parent.SetParent(chapter, false);
        var homes = new GameObject("Hamlets").transform; homes.SetParent(parent, false);
        var canopy = new GameObject("Canopy Masses").transform; canopy.SetParent(parent, false);
        var details = new GameObject("Coastal Road Details").transform; details.SetParent(parent, false);

        var facades = SettlementFacades();
        var roofs = SettlementRoofs();
        var trees = new[] { Glb(ShiosaiGlb, "Shiosai_Broadleaf"),
                            Glb(ShiosaiGlb, "Shiosai_Broadleaf_B"),
                            Glb(ShiosaiGlb, "Shiosai_Pine"),
                            Glb(ShiosaiGlb, "Shiosai_Broadleaf_B") };
        var villas = new[] { Model("Minato_Shore_VillaA"), Model("Minato_Shore_VillaB"),
                             Model("Minato_Shore_VillaC") };
        var wall = Glb(ShiosaiGlb, "Shiosai_Breakwater");
        var lamp = Model("Minato_Bridge_Lamp");
        var concrete = ConcreteMaterial();
        var rng = new System.Random(41004);
        int buildings = 0, treesPlaced = 0, walls = 0, lamps = 0;

        for (float d = BridgeEndM + 140f; d < 13200f; d += 54f)
        {
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            var fwd = route.Tangent[i]; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) continue;

            // One side carries the denser settlement, but the side flips every few clusters so
            // the valley never becomes two mechanical rows.
            int primary = (((int)(d / 360f)) & 1) == 0 ? -1 : 1;
            for (int s = -1; s <= 1; s += 2)
            {
                int count = s == primary ? 4 + rng.Next(4) : 2 + rng.Next(3);
                // CONDENSED 2026-09-24: was 42/72 + up to 75 m - the town now lines the road instead of sitting back.
                float baseOff = s == primary ? 28f : 44f;
                var courtyard = p + side * (s * (baseOff + (float)rng.NextDouble() * 40f))
                                + fwd.normalized * ((float)rng.NextDouble() - 0.5f) * 46f;
                for (int q = 0; q < count; q++)
                {
                    var src = rng.NextDouble() < 0.42
                        ? villas[rng.Next(villas.Length)]
                        : houses[rng.Next(houses.Length)];
                    if (src == null) continue;
                    float a = q / Mathf.Max(1f, count) * Mathf.PI * 2f +
                              (float)rng.NextDouble() * 0.45f;
                    float rr = 12f + (float)rng.NextDouble() * 28f;
                    var xz = courtyard + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    float y = GroundAt(route, xz.x, xz.z);
                    if (y < SeaLevelY + 1.3f || Mathf.Abs(y - p.y) > 34f) continue;
                    // Road guard: with the town pulled in, a cluster's ring can swing over the road.
                    if (NearestLand(route, xz.x, xz.z, out _) < 16f) continue;
                    var face = Quaternion.LookRotation((courtyard - xz).normalized, Vector3.up);
                    var go = Inst(src, homes, Vector3.zero, face,
                                  facades[rng.Next(facades.Length)],
                                  roofs[rng.Next(roofs.Length)]);
                    go.transform.position = new Vector3(xz.x, y - 0.30f, xz.z);
                    float sc = 0.82f + (float)rng.NextDouble() * 0.62f;
                    go.transform.localScale =
                        new Vector3(sc * (0.84f + (float)rng.NextDouble() * 0.36f),
                                    sc * (0.80f + (float)rng.NextDouble() * 0.58f),
                                    sc * (0.84f + (float)rng.NextDouble() * 0.36f));
                    go.isStatic = true; buildings++;
                }

                int grove = 7 + rng.Next(11);
                var treeSrc = trees[rng.Next(trees.Length)];
                if (treeSrc != null)
                    for (int q = 0; q < grove; q++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float rr = 18f + (float)rng.NextDouble() * 62f;
                        var xz = courtyard + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                        float y = GroundAt(route, xz.x, xz.z);
                        if (y < SeaLevelY + 1.1f || Mathf.Abs(y - p.y) > 55f) continue;
                        if (NearestLand(route, xz.x, xz.z, out _) < 10f) continue;   // road guard
                        var t = Inst(treeSrc, canopy, Vector3.zero,
                                     Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f),
                                     leaf, bark);
                        t.transform.position = new Vector3(xz.x, y - 0.08f, xz.z);
                        t.transform.localScale = Vector3.one *
                                                 (0.85f + (float)rng.NextDouble() * 0.75f);
                        t.isStatic = true; treesPlaced++;
                    }
            }

            if (wall != null && rng.NextDouble() < 0.55)
            {
                int uphill = GroundAt(route, (p + side * 28f).x, (p + side * 28f).z) >
                             GroundAt(route, (p - side * 28f).x, (p - side * 28f).z) ? 1 : -1;
                var q = p + side * (uphill * (RoadHalfWidth + 5.5f));
                var w = Inst(wall, details, Vector3.zero,
                             Quaternion.LookRotation(fwd.normalized, Vector3.up), concrete);
                w.transform.position = new Vector3(q.x, p.y - 0.55f, q.z);
                w.transform.localScale = new Vector3(0.9f, 0.9f, 1.5f);
                w.isStatic = true; walls++;
            }
            if (lamp != null && rng.NextDouble() < 0.40)
            {
                var q = p + side * (RoadHalfWidth + ShoulderW + 0.35f);
                var l = Inst(lamp, details, Vector3.zero,
                             Quaternion.LookRotation(fwd.normalized, Vector3.up), SteelMaterial());
                l.transform.position = q; l.isStatic = true; lamps++;
            }
        }

        Debug.Log($"[minato] far-shore settlement {buildings} varied homes, {treesPlaced} trees " +
                  $"in canopy masses, {walls} seawall/retaining sections and {lamps} lamps");
    }

    /// <summary>
    /// Layered verge dressing at the road/terrain boundary: tufts, ferns and low shrubs with a
    /// gravel/soil transition and the occasional stone. Without this the carriageway meets the
    /// ground on a hard generated seam. Density is controlled and everything is pushed clear of
    /// the cycling corridor so nothing blocks the ride line.
    /// </summary>
    private static void VergeDressing(MinatoRoute route, Transform[] chapters,
                                      Material leaf, Material bark, Material rock)
    {
        var parent = new GameObject("Verge Dressing").transform;
        parent.SetParent(chapters[1], false);
        // Depth bands get their own roots so culling/streaming can treat the cheap near cover
        // and the expensive far groves independently.
        var nearRoot = new GameObject("Band A - Ground Cover").transform; nearRoot.SetParent(parent, false);
        var midRoot  = new GameObject("Band B - Shrub and Rock").transform; midRoot.SetParent(parent, false);
        var farRoot  = new GameObject("Band C - Groves").transform; farRoot.SetParent(parent, false);

        // ---------------------------------------------------------------- species families
        // Several families per band, so no band is a cloned row of one asset. All are existing
        // production Sakura/Shiosai meshes - no new filler geometry.
        var ground = new[] { Glb(SakuraGlb, "SakuraPass_Grass_Tuft"), Glb(SakuraGlb, "SakuraPass_Fern_Clump"),
                             Glb(SakuraGlb, "SakuraPass_Grass_Tuft") };
        var shrubs = new[] { Glb(ShiosaiGlb, "Shiosai_Hydrangea_C"), Glb(ShiosaiGlb, "Shiosai_Hydrangea"),
                             Glb(ShiosaiGlb, "Shiosai_Hydrangea"), Glb(ShiosaiGlb, "Shiosai_Hydrangea_B"),
                             Glb(ShiosaiGlb, "Shiosai_Hydrangea_C") };
        var stones = new[] { Glb(SakuraGlb, "SakuraPass_Stone_Scatter_A"), Glb(SakuraGlb, "SakuraPass_Stone_Scatter_B"),
                             Glb(SakuraGlb, "SakuraPass_Rock_Cluster_A"), Glb(SakuraGlb, "SakuraPass_Rock_Cluster_B") };
        var trees  = new[] { Glb(ShiosaiGlb, "Shiosai_Broadleaf"), Glb(ShiosaiGlb, "Shiosai_Broadleaf_B"),
                             Glb(ShiosaiGlb, "Shiosai_Pine"), Glb(ShiosaiGlb, "Shiosai_Pine_B"),
                             Glb(ShiosaiGlb, "Shiosai_Pine"), Glb(ShiosaiGlb, "Shiosai_Pine_B") };

        // Three leaf tints so a grove is not one flat green. Cheap: three shared materials, so
        // SRP batching and static batching are unaffected. PROVISIONAL tints.
        var leafTex = Tex(ShiosaiTex, "Shiosai_Leaf_Albedo_HQ.png");
        var leaves = new[]
        {
            leaf,
            FoliageMaterial("Minato_Leaf_Warm", new Color(0.94f, 0.93f, 0.70f), leafTex, 0.24f),
            FoliageMaterial("Minato_Leaf_Deep", new Color(0.72f, 0.86f, 0.72f), leafTex, 0.24f),
        };

        // ------------------------------------------------------- keep-out: built footprints
        // Ground cover must not grow through waterfront buildings, the quay, the marina or the
        // moored boats. Collect what the port pass already placed and treat each as a disc.
        var keepOut = new List<(Vector3 p, float r)>();
        var portCity = GameObject.Find("Minato Coast Environment/Chapter 01 - Port City Departure/Port City")
                    ?? GameObject.Find("Port City");
        if (portCity != null)
            foreach (Transform grp in portCity.transform)
            {
                float r = grp.name.Contains("Block") ? 15f : grp.name.Contains("Quay") ? 9f : 7f;
                foreach (Transform t in grp) keepOut.Add((t.position, r));
            }

        bool Blocked(Vector3 q)
        {
            for (int n = 0; n < keepOut.Count; n++)
            {
                float dx = q.x - keepOut[n].p.x, dz = q.z - keepOut[n].p.z;
                if (dx * dx + dz * dz < keepOut[n].r * keepOut[n].r) return true;
            }
            return false;
        }

        var rng = new System.Random(4410);
        InstancedBuilds.Clear();
        // Dressing starts just outside the shoulder+guardrail and never enters the corridor.
        float inner = RoadHalfWidth + ShoulderW + 0.55f;
        int placed = 0;

        // Cluster-based placement. Scattering per-station in a fixed lateral slot is what made
        // the old verge read as two thin parallel ribbons with bare field behind them; real
        // ground cover arrives in patches of one species with a scale spread.
        void Cluster(Transform root, Vector3 centre, GameObject[] pal, Material[] mats,
                     int count, float radius, float sMin, float sMax, float yDrop, float slopeTol,
                     Vector3 railP, bool instanced = false, int cell = 0)
        {
            var src = pal[rng.Next(pal.Length)];
            if (src == null) return;
            for (int k = 0; k < count; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rr = radius * Mathf.Sqrt((float)rng.NextDouble());
                var q = centre + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);

                // Never inside the carriageway + shoulder safety zone.
                float lat = Vector3.ProjectOnPlane(q - railP, Vector3.up).magnitude;
                if (lat < inner) continue;

                float y = GroundAt(route, q.x, q.z);
                if (y < SeaLevelY + 0.8f) continue;                 // no planting in water
                if (Mathf.Abs(y - railP.y) > slopeTol) continue;    // don't hang off the cut face
                if (Blocked(q)) continue;                           // buildings / quay / docks

                var mat = mats[rng.Next(mats.Length)];
                var pos = new Vector3(q.x, y - yDrop, q.z);
                var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                var scale = Vector3.one * (sMin + (float)rng.NextDouble() * (sMax - sMin));
                if (instanced) QueueInstanced(src, pos, rot, scale, mat, bark, cell);
                else
                {
                    var go = Inst(src, root, Vector3.zero, rot, mat, bark);
                    go.transform.position = pos;
                    go.transform.localScale = scale;
                    go.isStatic = true;
                }
                placed++;
            }
        }

        for (float d = 6f; d < route.Length - 6f; d += (d < 3600f ? 2.4f : 4.8f))
        {
            // The bridge deck has no verge.
            if (d > SeaCrossStartM - 20f && d < BridgeEndM + 20f) continue;
            // Chapters 01/02 now sit on connected production pavement and authored yards.
            // Ground-cover scatter here was poking rocks and grass through the port apron.
            if (d < SeaCrossStartM) continue;

            int i = route.IndexAt(d);
            var p = route.Position[i];
            var side = route.SideFlat(i);
            int ch = ChapterOf(d);
            int cell = Mathf.FloorToInt(d / 256f);
            // COST TIERING, revisited now that every reused family has a Minato-derived LOD
            // ladder (see build_minato_lods.py): distance culling is real, so the remaining
            // chapters can carry near the gate density instead of the 0.45 stopgap. The bridge
            // chapter has no verge at all. PROVISIONAL.
            float dens = d < 3600f ? 1.00f
                       : d < BridgeEndM ? 0.55f
                       : 0.85f;

            for (int s = -1; s <= 1; s += 2)
            {
                // BAND A - continuous ground cover hard against the rail, 5.1-13 m.
                _simpleLod = true;
                for (int c = 0; c < 2; c++)
                {
                    if (rng.NextDouble() > 0.80 * dens) continue;
                    var centre = p + side * (s * (inner + 0.8f + (float)rng.NextDouble() * 7.0f));
                    Cluster(nearRoot, centre, ground, leaves, 3 + rng.Next(4), 1.9f,
                            0.75f, 1.55f, 0.05f, 7f, p, true, cell);
                }

                // BAND A2 - the 13-26 m zone. Without this the mid field read as mown lawn
                // between the near cover and the shrub patches. Half the density of band A and
                // a wider cluster radius, so it fills rather than repeats.
                if (rng.NextDouble() < 0.72 * dens)
                {
                    var centre = p + side * (s * (13f + (float)rng.NextDouble() * 13f));
                    Cluster(nearRoot, centre, ground, leaves, 3 + rng.Next(3), 3.0f,
                            0.85f, 1.70f, 0.05f, 16f, p, true, cell);
                }
                _simpleLod = false;

                // BAND B - shrub masses and rock patches, 13-36 m. Sparser stride so the
                // clusters read as patches with grass between, not a wall.
                if (rng.NextDouble() < 0.50)
                    for (int c = 0; c < 2; c++)
                    {
                        if (rng.NextDouble() > 0.85 * dens) continue;
                        var centre = p + side * (s * (13f + (float)rng.NextDouble() * 23f));
                        bool rocky = rng.NextDouble() < 0.28;
                        if (rocky) Cluster(midRoot, centre, stones, new[] { rock }, 2 + rng.Next(4),
                                           3.2f, 0.55f, 1.45f, 0.10f, 26f, p);
                        else Cluster(midRoot, centre, shrubs, leaves, 3 + rng.Next(5),
                                     3.6f, 0.70f, 1.35f, 0.05f, 26f, p);
                    }

                // BAND D - HILLSIDE FOREST, 120-480 m. The mid-distance slopes were reading as a
                // bare lime tableland behind the roadside groves; real coastal hills carry
                // continuous woodland at that depth. Wide stride, big clusters, and every
                // instance now carries a Minato-derived LOD ladder so the cost is distance
                // bounded. PROVISIONAL.
                if (rng.NextDouble() < 0.085 && ch >= 3)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        if (rng.NextDouble() > 0.70 * dens) continue;
                        float off = 120f + (float)rng.NextDouble() * 360f;
                        var centre = p + side * (s * off);
                        Cluster(farRoot, centre, trees, leaves, 5 + rng.Next(9), 34f,
                                0.95f, 1.9f, 0.10f, 220f, p);
                        if (rng.NextDouble() < 0.5)
                            Cluster(farRoot, centre, stones, new[] { rock }, 2 + rng.Next(4),
                                    26f, 0.9f, 2.2f, 0.10f, 220f, p);
                    }
                }
                // the far field gains a silhouette without becoming an overdraw wall. Tall
                // species stay well outside the corridor, so bridge visibility is preserved.
                if (rng.NextDouble() < 0.25)
                {
                    if (rng.NextDouble() > 0.62 * dens) continue;
                    float off = 36f + (float)rng.NextDouble() * 69f;
                    // Composition-aware thinning: density falls off with lateral distance.
                    if (rng.NextDouble() > Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(36f, 105f, off))) continue;
                    var centre = p + side * (s * off);
                    Cluster(farRoot, centre, trees, leaves, 2 + rng.Next(4), 7.5f,
                            0.95f, 1.75f, 0.10f, 60f, p);
                    if (rng.NextDouble() < 0.6)
                        Cluster(farRoot, centre, shrubs, leaves, 2 + rng.Next(3), 6.0f,
                                0.75f, 1.30f, 0.05f, 60f, p);
                }
            }
        }
        int instancedBatches = FlushInstanced(nearRoot);
        Debug.Log($"[minato] verge dressing {placed} instances in 3 depth bands " +
                  $"(A ground cover, B shrub/rock, C groves), inner edge {inner:F2} m from " +
                  $"centreline, {keepOut.Count} built footprints excluded " +
                  $"(cycling corridor kept clear), {instancedBatches} instanced batch objects");
    }

    private static int ChapterOf(float d) =>
        d < 1800f ? 0 : d < 3300f ? 1 : d < 9900f ? 2 : d < 13400f ? 3 : 4;

    /// <summary>
    /// The lighthouse sits on its OWN rocky promontory, offset well clear of the carriageway and
    /// of the crossing's landfall. The brief is explicit that the bridge must never terminate at
    /// the lighthouse, and concept 04 shows them as separate headlands.
    /// </summary>
    private static void BuildLighthousePromontory(MinatoRoute route, Transform chapter, Material rock)
    {
        var parent = new GameObject("Lighthouse Promontory").transform;
        parent.SetParent(chapter, false);

        int i = route.IndexAt(BridgeEndM + 1250f);
        var p = route.Position[i];
        var side = route.SideFlat(i);
        // 620 m out to seaward - far enough that no camera reads it as the bridge's abutment.
        var centre = p + side * -320f;   // CONDENSED 2026-09-24 (was -620)
        centre.y = SeaLevelY;
        var promontoryRock = CelMaterial("Minato_PromontoryRock",
                                         new Color(0.36f, 0.40f, 0.41f),
                                         0.08f, 0.05f, 0.24f,
                                         Tex(TakaTex, "Taka_Granite_Albedo.png"), GroundShade);

        var hero = Glb(MinatoGlb, "Minato_Lighthouse_Hero");
        if (hero != null)
        {
            var body = CelMaterial("Minato_Lighthouse_Body", new Color(0.96f, 0.93f, 0.84f),
                                   0.16f, 0.10f, 0.34f,
                                   Tex(ShiosaiTex, "Shiosai_HarbourFacade_Albedo_v2.png"));
            var roof = CelMaterial("Minato_Lighthouse_Roof", new Color(0.52f, 0.16f, 0.12f),
                                   0.20f, 0.13f, 0.30f,
                                   Tex(ShiosaiTex, "Shiosai_HarbourRoof_Albedo_v2.png"),
                                   GroundShade);
            var lantern = CelMaterial("Minato_Lighthouse_Lantern",
                                      new Color(1.00f, 0.86f, 0.56f),
                                      0.42f, 0.46f, 0.40f, null, GroundShade);
            var band = CelMaterial("Minato_Lighthouse_Band", new Color(0.39f, 0.075f, 0.06f),
                                   0.18f, 0.11f, 0.32f,
                                   Tex(ShiosaiTex, "Shiosai_HarbourRoof_Albedo_v2.png"), GroundShade);
            var metal = CelMaterial("Minato_Lighthouse_Metal", new Color(0.13f, 0.15f, 0.16f),
                                    0.32f, 0.26f, 0.24f, null, GroundShade);
            var glass = CelMaterial("Minato_Lighthouse_Glass", new Color(0.31f, 0.56f, 0.64f),
                                    0.72f, 0.62f, 0.46f, null, GroundShade);
            var plant = FoliageMaterial("Minato_Lighthouse_Scrub",
                                        new Color(0.70f, 0.82f, 0.62f),
                                        Tex(ShiosaiTex, "Shiosai_Leaf_Albedo_HQ.png"), 0.18f);

            var go = Inst(hero, parent, centre, Quaternion.Euler(0f, 18f, 0f), null);
            go.transform.localScale = Vector3.one * 1.34f;
            go.name = "Minato_Lighthouse_Hero";
            go.isStatic = true;
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = renderer.name;
                Material material =
                    n.Contains("_Rock_") ? promontoryRock :
                    n.Contains("_Body_") ? body :
                    n.Contains("_Band_") ? band :
                    n.Contains("_Roof_") ? roof :
                    n.Contains("_Glass_") ? glass :
                    n.Contains("_Light_") ? lantern :
                    n.Contains("_Plant_") ? plant :
                    metal;
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int m = 0; m < materials.Length; m++) materials[m] = material;
                renderer.sharedMaterials = materials;
            }
        }
        else
        {
            Debug.LogError("[minato] authored lighthouse GLB missing");
        }
        Debug.Log($"[minato] lighthouse promontory at {centre}, {Vector3.Distance(centre, p):N0} m " +
                  $"from the carriageway and {Vector3.Distance(centre, route.Position[route.IndexAt(BridgeEndM)]):N0} m from landfall");
    }

    // =================================================================== helpers

    private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

    private static Material LoadOrCreate(string name, string shaderName)
    {
        string resolved = MapleRideShaderNames.Resolve(shaderName);
        string path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(resolved));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = Shader.Find(resolved);
        mat.name = name;
        mat.enableInstancing = true;
        return mat;
    }

    private static Material CelMaterial(string name, Color albedo, float gloss = 0.2f,
                                        float spec = 0.18f, float rim = 0.5f, Texture texture = null,
                                        Color? shade = null)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, CelShaderName);
        mat.SetColor("_Color", albedo);
        mat.SetColor("_ShadeColor", shade ?? new Color(0.60f, 0.68f, 0.84f, 1f));
        mat.SetFloat("_ShadeStrength", 0.44f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.10f);
        // BRIGHT MIDDAY COASTAL re-grade. The shade colour/strength and _ShadowAmbient below
        // were authored for a late-afternoon low sun and are the single biggest reason the
        // region read "dark and murky": every shadowed facade sat at 48% of its albedo under a
        // blue-violet shade. Lifted so shadows stay coloured but never muddy. PROVISIONAL.
        mat.SetColor("_RimColor", new Color(1f, 0.97f, 0.90f, 1f));
        mat.SetFloat("_RimStrength", rim);
        mat.SetFloat("_Gloss", gloss);
        mat.SetFloat("_SpecStrength", spec);
        mat.SetFloat("_ShadowAmbient", 0.72f);
        mat.SetFloat("_ShadowSoft", 0.18f);
        if (texture != null) mat.SetTexture("_MainTex", texture);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Foliage per the Sakura convention. The first Minato renders showed near-black crowns
    /// because only _Color and _MainTex were set: without _Translucency the leaf canopy gets no
    /// subsurface transmission and reads as a dark silhouette, and _Cutoff left at the shader
    /// default chunks the alpha cards. Sakura sets all three, so Minato does too.
    /// </summary>
    private static Material FoliageMaterial(string name, Color tint, Texture tex, float wind)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FoliageShaderName);
        mat.SetColor("_Color", tint);
        if (tex != null) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.35f);
        if (mat.HasProperty("_Translucency")) mat.SetFloat("_Translucency", 1.0f);
        // Bright midday light bleeding through the canopy (was a warm late-afternoon 1,0.72,0.52).
        if (mat.HasProperty("_TransColor"))
            mat.SetColor("_TransColor", new Color(1f, 0.94f, 0.76f, 1f));
        if (mat.HasProperty("_WindStrength")) mat.SetFloat("_WindStrength", wind);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Coral-vermilion maintained coastal paint. The weathering the brief asks for - faded
    /// upper surfaces, grime at connections, salt on the lower sections, chipped edges - is
    /// driven by the cel shader's PROCEDURAL weathering block rather than by mask maps, because
    /// that is what this project actually has and it varies per instance for free.
    /// </summary>
    private static Material SteelMaterial()
    {
        var mat = CelMaterial("Minato_SteelVermilion", new Color(0.86f, 0.36f, 0.26f),
                              gloss: 0.34f, spec: 0.26f, rim: 0.45f);
        mat.SetFloat("_WeatherAmount", 0.55f);
        mat.SetFloat("_WearAmount", 0.34f);                                  // chipped edges
        mat.SetColor("_WearColor", new Color(1f, 0.93f, 0.84f));             // sun-bleached upper faces
        mat.SetFloat("_GrimeAmount", 0.40f);                                 // grime at connections
        mat.SetColor("_MossColor", new Color(0.62f, 0.60f, 0.55f));          // salt bloom, not moss
        mat.SetFloat("_MossAmount", 0.30f);
        mat.SetFloat("_MossBase", 0f);
        mat.SetFloat("_MossHeight", 26f);                                    // salt band on lower steel
        mat.SetFloat("_DetailScale", 11f);
        mat.SetFloat("_DetailAmount", 0.34f);
        mat.SetFloat("_TintVariation", 0.16f);                               // restrained paint variation
        mat.SetFloat("_TintVarScale", 0.010f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material HeroMountainMaterial()
    {
        var mat = LoadOrCreate("Minato_HeroMountain", TerrainShaderName);
        mat.SetTexture("_GrassTex", Tex(ShiosaiTex, "Shiosai_HokkaidoGround_Albedo.png"));
        mat.SetTexture("_GrassNormal", Tex(ShiosaiTex, "Shiosai_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", Tex(ShiosaiTex, "Shiosai_Grass_Rough.png"));
        mat.SetColor("_GrassColor", new Color(0.61f, 0.72f, 0.43f));
        mat.SetFloat("_GrassScale", 7.5f);
        mat.SetTexture("_RockTex", Tex(TakaTex, "Taka_Granite_Albedo.png"));
        mat.SetTexture("_RockNormal", Tex(TakaTex, "Taka_Granite_Normal.png"));
        mat.SetTexture("_RockRough", Tex(ShiosaiTex, "Shiosai_Rock_Rough.png"));
        mat.SetColor("_RockColor", new Color(0.63f, 0.64f, 0.62f));
        mat.SetFloat("_RockScale", 2.7f);
        mat.SetTexture("_ScreeTex", Tex(ShiosaiTex, "Shiosai_Scree_Albedo.png"));
        mat.SetTexture("_ScreeNormal", Tex(ShiosaiTex, "Shiosai_Scree_Normal.png"));
        mat.SetTexture("_ScreeRough", Tex(ShiosaiTex, "Shiosai_Scree_Rough.png"));
        mat.SetColor("_ScreeColor", new Color(0.61f, 0.57f, 0.50f));
        mat.SetFloat("_ScreeScale", 2.5f);
        if (mat.HasProperty("_SoilTex"))
        {
            mat.SetTexture("_SoilTex", Tex(SakuraTex, "Sakura_Soil_Albedo.png"));
            mat.SetTexture("_SoilNormal", Tex(SakuraTex, "Sakura_Soil_Normal.png"));
            mat.SetTexture("_SoilRough", Tex(SakuraTex, "Sakura_Soil_Rough.png"));
            mat.SetColor("_SoilColor", new Color(0.66f, 0.57f, 0.45f));
            mat.SetFloat("_SoilScale", 3.0f);
        }
        mat.SetVector("_TileOrigin", new Vector4(19615f, 0f, 10154f, 0f));
        mat.SetFloat("_SlopeRockStart", 38f);
        mat.SetFloat("_SlopeRockEnd", 62f);
        mat.SetFloat("_MacroVariation", 0.25f);
        mat.SetFloat("_NormalStrength", 0.58f);
        if (mat.HasProperty("_DetileAmount")) mat.SetFloat("_DetileAmount", 0.95f);
        if (mat.HasProperty("_DetileBlendM")) mat.SetFloat("_DetileBlendM", 29f);
        if (mat.HasProperty("_MesoVariation")) mat.SetFloat("_MesoVariation", 0.12f);
        if (mat.HasProperty("_MesoHue")) mat.SetFloat("_MesoHue", 0.06f);
        if (mat.HasProperty("_MesoScaleM")) mat.SetFloat("_MesoScaleM", 61f);
        if (mat.HasProperty("_DetailFadeAmt")) mat.SetFloat("_DetailFadeAmt", 0.94f);
        if (mat.HasProperty("_DetailFadeStart")) mat.SetFloat("_DetailFadeStart", 180f);
        if (mat.HasProperty("_DetailFadeRange")) mat.SetFloat("_DetailFadeRange", 980f);
        if (mat.HasProperty("_PetalStrength")) mat.SetFloat("_PetalStrength", 0f);
        mat.SetFloat("_MossStrength", 0.28f);
        mat.SetColor("_MossColor", new Color(0.33f, 0.43f, 0.29f));
        mat.SetColor("_ShadeColor", GroundShade);
        mat.SetFloat("_ShadeStrength", 0.34f);
        if (mat.HasProperty("_RampSmooth")) mat.SetFloat("_RampSmooth", 0.20f);
        if (mat.HasProperty("_ShadowAmbient")) mat.SetFloat("_ShadowAmbient", 0.78f);
        if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", 0.05f);
        mat.SetVector("_HeightRange", new Vector4(245f, 610f, 0.42f, 0f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material ConcreteMaterial()
    {
        var mat = CelMaterial("Minato_Concrete", new Color(0.82f, 0.82f, 0.80f),
                              gloss: 0.12f, spec: 0.06f, rim: 0.30f,
                              texture: Tex(TakaTex, "Taka_Concrete_Albedo.png"), shade: GroundShade);
        mat.SetFloat("_WeatherAmount", 0.60f);
        mat.SetFloat("_GrimeAmount", 0.46f);                                 // waterline grime
        mat.SetFloat("_WearAmount", 0.22f);                                  // edge wear on formwork
        mat.SetColor("_MossColor", new Color(0.46f, 0.50f, 0.44f));
        mat.SetFloat("_MossAmount", 0.34f);
        mat.SetFloat("_MossBase", SeaLevelY);
        mat.SetFloat("_MossHeight", 9f);                                     // tide / salt streak band
        mat.SetFloat("_DetailScale", 5.5f);
        mat.SetFloat("_TintVariation", 0.12f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void Vary(GameObject go, float tint)
    {
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", new Color(0.86f * tint, 0.36f * tint, 0.26f * tint));
            mr.SetPropertyBlock(mpb);
        }
    }

    /// <summary>
    /// Replace the GLB's imported materials (glTF/Lit, which render black under this project's
    /// low-intensity HDRP lights) with project cel materials. Submesh-aware: a single foliage
    /// material painted over EVERY slot puts an alpha-clipped leaf material on the trunk, which
    /// erases it. Slots whose imported material name reads as bark/trunk/wood keep a solid
    /// companion material instead.
    /// </summary>
    private static void Retint(GameObject go, Material mat, Material secondary = null)
    {
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            var src = mr.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                // The "secondary" slot is the one that is NOT the main surface: a tree's woody
                // parts, or a building's roof. Painting a single material over every slot puts
                // an alpha-clipped leaf material on trunks (which erases them) and gives every
                // house a facade-textured roof.
                bool alt = n.Contains("bark") || n.Contains("trunk") || n.Contains("wood")
                           || n.Contains("branch") || n.Contains("stem")
                           || n.Contains("roof") || n.Contains("tile") || n.Contains("shingle");
                mats[i] = (alt && secondary != null) ? secondary : mat;
            }
            mr.sharedMaterials = mats;
        }
    }

    private static void RetintLighthouse(GameObject go, Material plinth, Material body,
                                         Material accent, Material lantern)
    {
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            var src = mr.sharedMaterials;
            var mats = new Material[src.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                string n = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                mats[i] = n.Contains("rock") || n.Contains("plinth") ? plinth
                        : n.Contains("red") || n.Contains("band") ? accent
                        : n.Contains("glass") || n.Contains("lantern") ? lantern
                        : body;
            }
            mr.sharedMaterials = mats;
        }
    }

    /// <summary>sRGB byte triplet -> linear Color, matching the coast's colour authoring.</summary>
    private static Color Srgb(int r, int g, int b) =>
        new Color32((byte)r, (byte)g, (byte)b, 255);

    /// <summary>Sets a colour only when the shader actually has the property.</summary>
    private static void SetIf(Material m, string prop, Color c)
    {
        if (m != null && m.HasProperty(prop)) m.SetColor(prop, c);
    }

    /// <summary>
    /// VolumeProfile.Add&lt;T&gt;() only creates the component in MEMORY; without an explicit
    /// AddObjectToAsset the profile serialises NULL references and the sky reverts on reload.
    /// </summary>
    private static T EnsureOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var comp) || comp == null) comp = profile.Add<T>(true);
        if (!AssetDatabase.Contains(comp))
        {
            comp.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
        comp.active = true;
        return comp;
    }

    // BRIGHT MIDDAY MARITIME grade. Re-authored from the old late-afternoon low-warm-sun look,
    // which was the root cause of "drab / dark / bleak": a 6 km mean free path of warm
    // volumetric fog over a navy GradientSky, a warm +12 white balance and -2 saturation.
    // Minato is a lively modern port city at midday. ALL PROVISIONAL.
    private const float MinatoFogMeanFreePath = 14000f;  // light golden-hour haze (was 19000 midday; 9000 too thick)
    private const float MinatoFogBaseHeight = -20f;
    private const float MinatoFogMaxHeight = 1400f;
    private const float MinatoFogAnisotropy = 0.12f;     // near-isotropic: high sun, no forward glare
    private const float MinatoFogDepthExtent = 120f;

    /// <summary>
    /// The late-afternoon maritime grade: warm key highlights, cool blue ocean haze, filmic
    /// roll-off and deterministic exposure. Without this the region renders with no aerial
    /// perspective at all, which is why Minato's 8 km of receding arch spans read flat.
    ///
    /// CRITICAL: the volume is parented UNDER the Minato region root. Sakura Pass, Shiosai and
    /// Minato all share Assets/Scenes/SakuraPass.unity and RegionDirector shows/hides the region
    /// roots, so a scene-root global volume would impose Minato's sky on Sakura's sunset.
    /// </summary>
    private static void ConfigureAtmosphere(Transform parent)
    {
        const string profilePath =
            "Assets/Environment/MinatoCoast/Minato_SkyProfile.asset";
        const string volumeName = "Minato Sky Volume";

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        var ve = EnsureOverride<UnityEngine.Rendering.HighDefinition.VisualEnvironment>(profile);
        ve.skyType.overrideState = true;
        ve.skyType.value = (int)UnityEngine.Rendering.HighDefinition.SkyType.Gradient;
        // REDESIGN: golden-hour CLOUD LAYER (reference skies are full of sun-lit cumulus). Same
        // proven path as ShiosaiCoastEnvironment: HDRP CloudLayer driven by the coast's own cloud
        // map (HDRP's default map resolves to null in batchmode). Warm tint + lighting so the
        // cloud undersides go gold toward the low sun. PROVISIONAL art tuning.
        ve.cloudType.overrideState = true;
        ve.cloudType.value = (int)UnityEngine.Rendering.HighDefinition.CloudType.CloudLayer;
        var clouds = EnsureOverride<UnityEngine.Rendering.HighDefinition.CloudLayer>(profile);
        var cloudMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Environment/ShiosaiCoast/Textures/Shiosai_CloudMap.png");
        if (cloudMap != null)
        {
            clouds.layerA.cloudMap.overrideState = true;
            clouds.layerA.cloudMap.value = cloudMap;
        }
        else Debug.LogWarning("[minato] Shiosai_CloudMap.png missing - no clouds");
        clouds.opacity.overrideState = true;             clouds.opacity.value = 0.80f;
        clouds.upperHemisphereOnly.overrideState = true; clouds.upperHemisphereOnly.value = true;
        clouds.layers.overrideState = true;
        // Two layers of the same coverage map, rotated against each other: pass 1 with a single
        // layer left the golden-hour sky almost empty (one cloud per frame).
        clouds.layers.value = UnityEngine.Rendering.HighDefinition.CloudMapMode.Double;
        if (cloudMap != null)
        {
            clouds.layerB.cloudMap.overrideState = true;
            clouds.layerB.cloudMap.value = cloudMap;
        }
        clouds.layerB.rotation.overrideState = true;     clouds.layerB.rotation.value = 0.37f;
        clouds.layerB.altitude.overrideState = true;     clouds.layerB.altitude.value = 3400f;
        clouds.layerB.tint.overrideState = true;         clouds.layerB.tint.value = new Color(1f, 0.93f, 0.86f);
        clouds.layerB.opacityR.overrideState = true;     clouds.layerB.opacityR.value = 1f;
        clouds.layerB.lighting.overrideState = true;     clouds.layerB.lighting.value = true;
        clouds.layerB.thickness.overrideState = true;    clouds.layerB.thickness.value = 0.5f;
        clouds.layerA.altitude.overrideState = true;     clouds.layerA.altitude.value = 2200f;
        clouds.layerA.tint.overrideState = true;         clouds.layerA.tint.value = new Color(1f, 0.90f, 0.78f);
        clouds.layerA.exposure.overrideState = true;     clouds.layerA.exposure.value = 0f;
        clouds.layerA.opacityR.overrideState = true;     clouds.layerA.opacityR.value = 1f;
        clouds.layerA.lighting.overrideState = true;     clouds.layerA.lighting.value = true;
        clouds.layerA.thickness.overrideState = true;    clouds.layerA.thickness.value = 0.6f;
        clouds.layerA.steps.overrideState = true;        clouds.layerA.steps.value = 8;

        // BRIGHT midday sky: a clean saturated blue overhead that stays luminous all the way
        // down to a pale, slightly warm horizon. The old top (0x2E62A8) read almost navy in the
        // renders and dragged the whole frame down.
        var sky = EnsureOverride<UnityEngine.Rendering.HighDefinition.GradientSky>(profile);
        // GOLDEN HOUR sky (2026-09-25): deep evening blue overhead, a warm peach band and a
        // glowing amber horizon. Was bright midday (0x5FAAEE / 0xB4DEFA / 0xECF6FD).
        // Pass 2: blue must own the upper sky (pass 1 was peach from horizon to zenith).
        sky.top.overrideState = true;     sky.top.value = Srgb(0x2E, 0x6C, 0xCC).linear * 1.40f;
        sky.middle.overrideState = true;  sky.middle.value = Srgb(0xC8, 0xD2, 0xE4).linear * 1.40f;
        sky.bottom.overrideState = true;  sky.bottom.value = Srgb(0xFF, 0xC8, 0x86).linear * 1.50f;
        sky.gradientDiffusion.overrideState = true; sky.gradientDiffusion.value = 2.2f;

        var fog = EnsureOverride<UnityEngine.Rendering.HighDefinition.Fog>(profile);
        fog.enabled.overrideState = true;          fog.enabled.value = true;
        fog.meanFreePath.overrideState = true;     fog.meanFreePath.value = MinatoFogMeanFreePath;
        fog.baseHeight.overrideState = true;       fog.baseHeight.value = MinatoFogBaseHeight;
        fog.maximumHeight.overrideState = true;    fog.maximumHeight.value = MinatoFogMaxHeight;
        fog.albedo.overrideState = true;           fog.albedo.value = Srgb(0xF0, 0xE0, 0xCC);   // soft warm haze (pass 2: less saturated)
        fog.mipFogNear.overrideState = true;       fog.mipFogNear.value = 0f;
        fog.mipFogFar.overrideState = true;        fog.mipFogFar.value = 5000f;
        fog.mipFogMaxMip.overrideState = true;     fog.mipFogMaxMip.value = 0.5f;
        fog.enableVolumetricFog.overrideState = true; fog.enableVolumetricFog.value = true;
        // Strong forward scatter: the haze GLOWS toward the low sun (golden hour), instead of
        // the near-isotropic midday value.
        fog.anisotropy.overrideState = true;       fog.anisotropy.value = 0.40f;   // pass 2 (0.62 flooded the sun side)
        fog.depthExtent.overrideState = true;      fog.depthExtent.value = MinatoFogDepthExtent;

        var tm = EnsureOverride<UnityEngine.Rendering.HighDefinition.Tonemapping>(profile);
        tm.mode.overrideState = true;
        tm.mode.value = UnityEngine.Rendering.HighDefinition.TonemappingMode.ACES;

        var ex = EnsureOverride<UnityEngine.Rendering.HighDefinition.Exposure>(profile);
        ex.mode.overrideState = true;
        ex.mode.value = UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed;
        // Fixed EV is inverted in HDRP: a LOWER fixed exposure = a BRIGHTER image. -0.85 EV is
        // the single biggest lift in this pass. PROVISIONAL.
        ex.fixedExposure.overrideState = true; ex.fixedExposure.value = 0.35f;

        var bloom = EnsureOverride<UnityEngine.Rendering.HighDefinition.Bloom>(profile);
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.30f;   // golden hour glow (was 0.14)
        bloom.scatter.overrideState = true;   bloom.scatter.value = 0.74f;

        var ca = EnsureOverride<UnityEngine.Rendering.HighDefinition.ColorAdjustments>(profile);
        // Was saturation -2 / contrast +10 / postExposure +0.05, which is a flat, desaturated,
        // contrasty grade - exactly the "drab" the brief is complaining about. A lively city
        // wants MORE colour and a lifted midtone, not more contrast. PROVISIONAL.
        ca.saturation.overrideState = true;   ca.saturation.value = 20f;
        ca.contrast.overrideState = true;     ca.contrast.value = 10f;   // golden hour (was 8 midday; 14 crushed shadows)
        ca.postExposure.overrideState = true; ca.postExposure.value = 0.05f;

        var wb = EnsureOverride<UnityEngine.Rendering.HighDefinition.WhiteBalance>(profile);
        // Was +12 (heavily warm, "late afternoon"). Clean bright daylight sits near neutral,
        // pulled a touch cool so the sky and sea read blue rather than amber.
        wb.temperature.overrideState = true;  wb.temperature.value = 6f;    // gentle warmth (was -6 midday; 16 = sepia)
        wb.tint.overrideState = true;         wb.tint.value = 2f;

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Exact-name match + prune duplicates (idempotency invariant).
        Transform keep = null;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var c = parent.GetChild(i);
            if (!string.Equals(c.name, volumeName, StringComparison.Ordinal)) continue;
            if (keep == null) keep = c; else UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        if (keep == null)
        {
            var go = new GameObject(volumeName);
            go.transform.SetParent(parent, false);
            keep = go.transform;
        }
        var vol = keep.GetComponent<Volume>();
        if (vol == null) vol = keep.gameObject.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 50f;
        vol.sharedProfile = profile;

        Debug.Log($"[minato] atmosphere: GOLDEN HOUR GradientSky, thin volumetric fog " +
                  $"mfp={MinatoFogMeanFreePath}m aniso=0.40, ACES, " +
                  $"fixedEV=+0.35, bloom=0.30, sat=+20, WB +6");
    }

    /// <summary>
    /// Registers the already-sectioned terrain, road and bridge roots with a lightweight runtime
    /// distance streamer. This complements renderer frustum culling: Unity no longer walks and
    /// submits terrain chunks or bridge spans several chapters away from the rider.
    /// </summary>
    private static void ConfigureStreaming(Transform root)
    {
        var cells = new List<MinatoRouteStreamer.Cell>();

        void Add(GameObject go, float distance)
        {
            if (go == null) return;
            bool any = false;
            Bounds b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!any) return;
            cells.Add(new MinatoRouteStreamer.Cell
            {
                root = go,
                bounds = b,
                activeDistanceM = distance,
            });
        }

        var terrain = root.Find("Terrain");
        if (terrain != null)
            foreach (Transform c in terrain)
                if (c.name.StartsWith("Terrain_Chunk_", StringComparison.Ordinal))
                    Add(c.gameObject, 2400f);

        var road = root.Find("Road");
        if (road != null)
        {
            foreach (Transform c in road)
                if (c.name.StartsWith("Road_", StringComparison.Ordinal))
                    Add(c.gameObject, 1800f);
            var markings = road.Find("Road Markings");
            if (markings != null)
                foreach (Transform c in markings) Add(c.gameObject, 1800f);
        }

        var bridge = root.Find("Chapter 2 - Bridge Approach/Bridge");
        if (bridge != null)
            foreach (Transform span in bridge)
                if (span.name.StartsWith("Span_", StringComparison.Ordinal))
                    Add(span.gameObject, 2800f);

        var authoredMountains = root.Find(
            "Chapter 5 - Inland Mountain Continuation/Authored Mountain Landscape");
        if (authoredMountains != null)
            Add(authoredMountains.gameObject, 7000f);

        var authoredPort = root.Find("Chapter 1 - Port City Departure/Authored Port District");
        if (authoredPort != null)
            Add(authoredPort.gameObject, 4200f);
        var authoredApproach = root.Find("Chapter 1 - Port City Departure/Authored Bridge Approach");
        if (authoredApproach != null)
            Add(authoredApproach.gameObject, 3400f);

        var streamer = root.GetComponent<MinatoRouteStreamer>();
        if (streamer == null) streamer = root.gameObject.AddComponent<MinatoRouteStreamer>();
        streamer.updateInterval = 0.25f;
        streamer.cells = cells.ToArray();
        streamer.ShowAll();

        int objects = root.GetComponentsInChildren<Transform>(true).Length;
        int renderers = root.GetComponentsInChildren<Renderer>(true).Length;
        int lodGroups = root.GetComponentsInChildren<LODGroup>(true).Length;
        int instanced = root.GetComponentsInChildren<MinatoInstancedBatch>(true).Length;
        Debug.Log($"[minato] architecture {cells.Count:N0} streamed terrain/road/bridge cells, " +
                  $"{instanced:N0} GPU-instanced scatter batches; hierarchy {objects:N0} objects, " +
                  $"{renderers:N0} renderers, {lodGroups:N0} LODGroups before scene serialization");
    }

    private static Texture2D Tex(string dir, string file)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{file}");
        if (t == null) Debug.LogWarning($"[minato] texture missing: {dir}/{file}");
        return t;
    }

    /// <summary>
    /// Load a reused production model, preferring the MINATO-OWNED LOD derivative.
    ///
    /// The Sakura/Shiosai source GLBs ship a single mesh each and therefore never receive an
    /// LODGroup from <see cref="Inst"/>. tools/blender/build_minato_lods.py imports each source,
    /// renames its meshes to _LOD0 and adds decimated _LOD1/_LOD2/_LOD3 siblings (55/25/8 %),
    /// writing the result to Minato's own BlenderAssets folder. The source environments are
    /// never modified. Falling back to the original keeps the pass working if a family has not
    /// been derived yet.
    /// </summary>
    private static GameObject Glb(string dir, string name)
    {
        var lod = AssetDatabase.LoadAssetAtPath<GameObject>($"{MinatoGlb}/Minato_{name}.glb");
        if (lod != null) { _lodHits++; return lod; }
        var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}.glb");
        if (g == null) Debug.LogWarning($"[minato] glb missing: {dir}/{name}.glb");
        else _lodMisses++;
        return g;
    }

    private static int _lodHits, _lodMisses;

    private static GameObject Model(string name)
    {
        var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{name}.fbx");
        if (g == null) Debug.LogWarning($"[minato] fbx missing: {ModelDir}/{name}.fbx");
        return g;
    }

    /// <summary>
    /// Instantiate a source model, wire an LODGroup from its LOD0..LOD3 children if the FBX
    /// carries them, and assign the material. Spec LOD ladder: LOD0 full, LOD1 ~55%,
    /// LOD2 ~25%, LOD3 ~8%.
    /// </summary>
    /// <summary>Set while placing tiny ground-cover families, to skip their pointless LOD ladder.</summary>
    private static bool _simpleLod;

    private static GameObject Inst(GameObject src, Transform parent, Vector3 localPos,
                                   Quaternion localRot, Material mat, Material secondary = null)
    {
        if (src == null) return new GameObject("MISSING");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        if (mat != null) Retint(go, mat, secondary);

        var lods = new List<Renderer>[4];
        bool any = false;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
            for (int l = 0; l < 4; l++)
                if (mr.name.EndsWith($"_LOD{l}"))
                { (lods[l] ??= new List<Renderer>()).Add(mr); if (l > 0) any = true; }

        // GROUND-COVER FAST PATH. A grass tuft is ~156 tris, so its LOD ladder saves no
        // triangles at all while costing 3 extra renderers per instance. Across ~100k verge
        // instances that alone drove the scene to 3.7M loaded objects / 4.3 GB and crashed the
        // batch build. For these families keep LOD0 only and drop the LODGroup.
        if (_simpleLod && any)
        {
            for (int l = 1; l < 4; l++)
                if (lods[l] != null)
                    foreach (var r in lods[l]) UnityEngine.Object.DestroyImmediate(r.gameObject);
            any = false;
        }

        if (any)
        {
            // NOT `GetComponent<T>() ?? AddComponent<T>()`: the null-coalescing operator bypasses
            // UnityEngine.Object's overloaded ==, so a fake-null component reference is treated
            // as a live one and every later call throws MissingComponentException.
            var group = go.GetComponent<LODGroup>();
            if (group == null) group = go.AddComponent<LODGroup>();
            // Screen-relative HEIGHT thresholds, not detail percentages. A 6 m tree at 80 m in a
            // 60 deg view occupies ~0.065 of screen height, so the old 0.42/0.16/0.055/0.012
            // ladder culled every tree past ~32 m and emptied the whole landscape while the
            // placement log still reported thousands of instances. The final entry must sit very
            // near zero or distant scatter disappears. PROVISIONAL.
            float[] cuts = { 0.060f, 0.022f, 0.008f, 0.0015f };
            var list = new List<LOD>();
            for (int l = 0; l < 4; l++)
                if (lods[l] != null) list.Add(new LOD(cuts[l], lods[l].ToArray()));
            if (list.Count > 1)
            {
                // Whatever the coarsest present level is, it must fade out at ~0, never at the
                // threshold of the NEXT level that this asset does not actually ship.
                var last = list[list.Count - 1];
                list[list.Count - 1] = new LOD(0.0015f, last.renderers);
                group.SetLODs(list.ToArray());
                group.RecalculateBounds();
            }
            else
            {
                // One level only: an LODGroup here can only cull. Remove it.
                UnityEngine.Object.DestroyImmediate(group);
            }
        }
        return go;
    }

    private static GameObject AddMesh(Transform parent, string name, Mesh mesh, Material mat,
                                      bool collider)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;

        string path = $"{MeshDir}/{mesh.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return go;
    }
}

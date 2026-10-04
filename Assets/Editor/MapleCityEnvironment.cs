using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// MAPLE CITY - "The Heart". The world's only URBAN region, and the first region authored from
/// scratch against a written 2D design document
/// (<c>reference/improve/region_designs/maple_city.md</c>).
///
/// MILESTONE SCOPE (M1 - foundation slice)
/// ---------------------------------------
/// This pass delivers a rideable, fast-travellable, Sakura-lit city: the 5.0 km closed crit
/// loop as real swept geometry, its two road surfaces (asphalt boulevard + cobbled Old Town
/// setts), kerbs and pavements, the terraced city ground the loop sits on, a blockout skyline
/// of machiya/apartment/boulevard buildings with lit windows, street maples and ginkgo, the
/// vermilion Maple Gate at the start/finish, and the region's own golden-hour sky.
///
/// Deliberately NOT in this slice, and named so the next milestone is unambiguous: the canal
/// water and its barges, the tram and its overhead wires, strung paper lanterns, vending
/// machines, cafe terraces, the pocket shrine, the arched river bridge, rooftop signage, and
/// the SIGNATURE drifting maple/ginkgo leaf VFX (the city's answer to Sakura's petals).
///
/// WHY THIS IS C# AND NOT BLENDER
/// ------------------------------
/// Same reasoning <see cref="ShiosaiCoastEnvironment"/> documents, and for the same reasons:
/// every piece of this region is measured off the published centreline, so authoring it in
/// Blender would mean re-publishing the same centreline into a second tool for no gain, and it
/// cannot desynchronise from the road the rider actually rides. When Maple City gets its final
/// art pass the builders move to tools/blender/ and this file shrinks to a staging pass,
/// exactly like SakuraPassEnvironment.
///
/// The route itself IS published through the pipeline:
/// tools/blender/maple_city_route.py -> Assets/Environment/MapleCity/MapleRoute.json ->
/// RouteGraphBaker -> RouteGraph.
///
/// IDEMPOTENCY. Apply() destroys every root matching the EXACT name "Maple City Environment"
/// and rebuilds from scratch, so re-running converges instead of accumulating. Every mesh it
/// generates is written to disk under Meshes/ so the saved scene never carries a dangling
/// reference to a runtime-only Mesh.
/// </summary>
public static partial class MapleCityEnvironment
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RoutePath = "Assets/Environment/MapleCity/MapleRoute.json";
    /// <summary>Second district (Harbour &amp; Market, tools/blender/maple_city_east_route.py). Built as a CHILD of the one
    /// "Maple City Environment" root so region gating and every exactly-one-root test keep working.</summary>
    public const string EastRoutePath = "Assets/Environment/MapleCity/MapleRouteEast.json";
    public const string EastGroupName = "Maple City East";
    /// <summary>Prefix on persisted mesh assets so the East district can never overwrite the Old Town's.</summary>
    private static string MeshPrefix = "";
    private const string MaterialDir = "Assets/Environment/MapleCity/Materials";
    private const string MeshDir = "Assets/Environment/MapleCity/Meshes";
    private const string SakuraTextureDir = "Assets/Environment/SakuraPass/Textures";
    private const string CoastTextureDir = "Assets/Environment/ShiosaiCoast/Textures";

    public const string RootName = "Maple City Environment";

    private const string CelShaderName = "MapleRide/HDRP/CelLit";
    private const string FoliageShaderName = "MapleRide/HDRP/Foliage";
    private const string FacadeShaderName = "MapleRide/HDRP/CityFacade";
    private const string TerrainShaderName = "MapleRide/HDRP/Terrain";

    // =================================================================== provisional tuning
    // EVERY number in this region is PROVISIONAL. Route metrics, economy names and art tuning
    // are all explicitly unresolved in the design handoff; these exist so the city is rideable
    // and reviewable, not because any of them is a final requirement.

    // ---- shared road cross-section (identical in all three regions, on purpose) -----------
    private const float RoadHalfWidth = 3.5f;
    private const float ShoulderWidth = 0.55f;
    /// <summary>Parabolic carriageway crown, metres. Matched to Sakura Pass's authored 6 cm.</summary>
    private const float RoadCrown = 0.06f;
    /// <summary>Metres the ground's road-corridor nodes sit below the published centreline.</summary>
    private const float VergeDropM = 0.05f;
    /// <summary>
    /// The lane line the player and the ambient riders both ride, in metres from the
    /// centreline. MUST match <c>RouteFollower.laneOffset</c> (which is -1.7).
    /// </summary>
    private const float LaneLineOffsetM = 1.7f;
    /// <summary>Metres the rider transform is lifted above the centreline
    /// (<c>RouteFollower.heightOffset</c>). The bike is authored with wheels at y = 0.</summary>
    private const float RiderLiftM = 0.02f;

    private static float CrownAt(float offset) =>
        RoadCrown * (1f - Mathf.Pow(Mathf.Abs(offset) / (RoadHalfWidth + ShoulderWidth), 2f));

    /// <summary>
    /// Vertical bias applied to the swept carriageway, SOLVED rather than guessed, so that the
    /// road surface at the lane line lands exactly on the height the rider is placed at. Copied
    /// from ShiosaiCoastEnvironment because it is the same solve against the same follower -
    /// getting this wrong is what buried half the wheel in the asphalt on the coast mock.
    /// </summary>
    private static readonly float RoadSurfaceLiftM =
        RiderLiftM + VergeDropM - CrownAt(LaneLineOffsetM);

    /// <summary>Paint sits this far proud of the road, so it never z-fights.</summary>
    private const float MarkingLiftM = 0.015f;

    // ---- the city's own numbers -----------------------------------------------------------

    /// <summary>
    /// Arc fractions of the Old Town cobbled section. Design doc section 2 puts the ramps at
    /// 1.9-2.6 km of a 5.0 km lap (0.38-0.52); the setts are laid slightly WIDER than that, from
    /// the corner that enters the old town (measured at frac 0.264 by the route build report) to
    /// just past the crest, because a surface change that begins exactly where the gradient
    /// does reads as a gameplay decal rather than as a neighbourhood.
    /// </summary>
    private const float CobbleStartFrac = 0.262f;
    private const float CobbleEndFrac = 0.530f;

    /// <summary>
    /// Visual carriageway half width, metres, as a function of arc fraction. The design asks for
    /// road width to VARY (wide boulevard <-> tight 4 m cobbled street) in deliberate contrast
    /// to Sakura's constant 7 m.
    ///
    /// IMPORTANT: this is a RENDERING property only. The published route keeps roadHalfWidth at
    /// 3.5 m in every region so the rider's lane offset, the NPC lane offsets and the physics
    /// cross-section are identical everywhere - and the profile below never returns less than
    /// the shared 3.5 m, so the rider can never be placed off the asphalt.
    /// </summary>
    private static float CarriagewayHalfWidth(float frac)
    {
        // Old Town: narrow cobbled lane between machiya. Boulevard: wide tram-tracked avenue.
        float cobble = Mathf.Clamp01(SmoothBand(frac, CobbleStartFrac, CobbleEndFrac, 0.035f));
        float boulevard = Mathf.Clamp01(SmoothBand(frac, 0.60f, 0.88f, 0.05f));
        return Mathf.Lerp(Mathf.Lerp(4.6f, 3.6f, cobble), 6.4f, boulevard);
    }

    /// <summary>1 inside [lo, hi] with a smoothstep shoulder of <paramref name="soft"/>, wrapping.</summary>
    private static float SmoothBand(float f, float lo, float hi, float soft)
    {
        float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo - soft, lo + soft, f));
        float b = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hi + soft, hi - soft, f));
        return Mathf.Min(a, b);
    }

    /// <summary>Kerb face height, metres. Raised pavements are half of what makes a road URBAN.</summary>
    private const float KerbHeightM = 0.14f;
    /// <summary>Pavement width outside the kerb, metres.</summary>
    private const float PavementWidthM = 5.0f;

    /// <summary>PROVISIONAL: fraction of tall-district buildings that carry a rooftop sign.</summary>
    private const float SignChance = 0.22f;

    /// <summary>PROVISIONAL: metres between strung paper lanterns along a lantern run.</summary>
    private const float LanternSpacingM = 7.5f;

    /// <summary>PROVISIONAL: height of the tram's overhead contact wire above the road, metres.</summary>
    private const float TramWireHeightM = 5.6f;

    /// <summary>
    /// PROVISIONAL: arc fraction band carrying the canal, and the fraction the river bridge sits
    /// at. Both are taken from the design doc's named checkpoints (Canal Sprint 0.16, River
    /// Bridge Finish 0.92) rather than invented.
    /// </summary>
    private const float CanalFromFrac = 0.06f, CanalToFrac = 0.25f, BridgeFrac = 0.92f;

    /// <summary>PROVISIONAL: canal channel width and coping height, metres.</summary>
    private const float ChannelWidthM = 14f, CopingHeightM = 0.5f;

    /// <summary>
    /// Half width of the modelled city ground corridor, metres.
    ///
    /// SOLVED, not chosen: the route build report measures the minimum separation between
    /// non-adjacent parts of the loop at 168.9 m, so any corridor wider than ~84 m per side
    /// would have the Old Town terrace (y ~ 90 m) and the canal flat (y ~ 40 m) writing
    /// conflicting heights into the same patch of ground and tearing through each other. 68 m
    /// leaves a comfortable margin, is deeper than the deepest building row, and is far beyond
    /// what a rider at road level can see past the buildings anyway.
    /// </summary>
    private const float CorridorHalfWidthM = 68f;
    /// <summary>The corridor skirts down to this absolute world Y at its outer edge, where it
    /// meets the flat basin plate that closes the city off. Below every part of the loop.</summary>
    private const float BasinY = 30f;
    private const int GroundStride = 2;            // use every Nth route sample for the ground

    private const int ScatterSeed = 20260914;

    // =================================================================== menu / entry points

    [MenuItem("MapleRide/Environment/Build Maple City", priority = 23)]
    public static void BuildCityPass()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Apply();

        // A city-specific build command should leave the city visible and playable.
        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
            FindObjectsInactive.Include);
        if (regions == null || !regions.FastTravel(RegionCatalog.MapleCity))
            Debug.LogWarning("[maple] city built, but it could not be selected for preview.");

        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[maple] saved '{active.path}'.");
    }

    /// <summary>
    /// Builds Maple City into the active scene and stages the fast-travel systems.
    /// Safe to run repeatedly: the region root is rebuilt from scratch and every scene object it
    /// touches is matched by EXACT name.
    /// </summary>
    public static void Apply()
    {
        bool headless = Application.isBatchMode;
        if (headless && EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(MeshDir);
        MaterialCache.Clear();
        SakuraTextureImportSettings.ApplyAll();

        var route = CityRoute.Load();
        Debug.Log($"[maple] route: {route.Count} samples, {route.Length:0.0} m lap, " +
                  $"y {route.MinY:0.0} - {route.MaxY:0.0} m.");

        // Converge, never accumulate.
        foreach (var stale in FindRootsByExactName(RootName))
            UnityEngine.Object.DestroyImmediate(stale);

        var root = new GameObject(RootName).transform;

        BuildBasin(root, route);
        BuildGround(root, route);
        BuildRoad(root, route);
        BuildKerbsAndPavements(root, route);
        BuildBuildings(root, route);
        BuildStreetTrees(root, route);
        BuildMapleGate(root, route);
        BuildCanalAndBridge(root, route);
        BuildTramLine(root, route);
        BuildStreetFurniture(root, route);
        BuildLeafVfx(root, route);
        MapleRowBoutiques.Build(root, route);   // C6 Maple Row boutique street, route m 450-700 (copilot, Assets/Editor/MapleRowBoutiques.cs)
        MapleCitySkyline.Build(root, route);    // downtown towers from the Minato skyline kit (claude-city, Assets/Editor/MapleCitySkyline.cs)
        MapleCityLife.Build(root, route);       // pedestrians, cafes, street chats - reuses the Minato crowd (claude-city, Assets/Editor/MapleCityLife.cs)

        BuildEastDistrict(root);                // second district (child of the same root)
        BuildNeoLayer(root, route, "Old");      // vaporwave neo-Tokyo layer for the Old Town (same code as East)

        // Re-bake so the city's segment/course/checkpoints reach the runtime graph, then stage
        // the region systems the World Map drives. SetupRegionSystems is reused from the coast
        // pass on purpose: there must be exactly ONE authority for staging the RegionDirector,
        // or two regions' build passes would fight over the same component every time either
        // one is re-run.
        var graph = RouteGraphBaker.BakeAsset();
        ShiosaiCoastEnvironment.SetupRegionSystems(graph);

        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
            FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.mapleCitySky = BuildCitySky();
            EditorUtility.SetDirty(regions);
        }
        else
        {
            Debug.LogWarning("[maple] no RegionDirector in scene - city sky not wired.");
        }

        int renderers = root.GetComponentsInChildren<MeshRenderer>(true).Length;
        Debug.Log($"[maple] city built: {renderers} renderers under '{RootName}'.");

        // MATERIALS MUST BE FLUSHED TO DISK HERE.
        //
        // Every material this pass authors goes through LoadOrCreate -> SetColor/SetFloat ->
        // EditorUtility.SetDirty. SetDirty only MARKS the asset; it does not write it. In the
        // editor you never notice, because the asset database saves on its own eventually. In
        // BATCHMODE the process quits immediately afterwards and every property edit is thrown
        // away - and then the NEXT batchmode process (the diagnostics capture) loads the STALE
        // .mat from disk and photographs the old colours.
        //
        // That failure is completely silent: the build logs its renderer count, exits 0, the
        // capture writes fresh PNGs, and the render simply does not reflect the change. It cost
        // several full build+capture cycles here - paving albedo was cut by 36% with the
        // rendered pixels moving 2%, which is what finally gave it away.
        AssetDatabase.SaveAssets();

        if (headless)
        {
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[maple] saved '{active.path}'.");
        }
    }

    // =================================================================== second district

    /// <summary>
    /// Maple City East - the "Harbour &amp; Market" district that doubles the city. Same builders, same materials, its own route
    /// (MapleRouteEast.json), parented under <see cref="EastGroupName"/> inside the existing city root. Phase 1 scope: ground,
    /// road + markings, kerbs/pavements, buildings, street trees, gate, canal + bridge, tram, furniture and leaf VFX.
    /// NOT yet built for East (their builders persist assets under shared names): Maple Row boutiques (route-m specific),
    /// downtown skyline kit, pedestrian/cafe life.
    /// </summary>
    /// <summary>Builds ONLY the East district into the saved scene and re-bakes the route graph. The Old Town is not touched.</summary>
    [MenuItem("MapleRide/Environment/Build Maple City East (district only)", priority = 24)]
    public static void BuildEastOnly()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(MaterialDir);
        Directory.CreateDirectory(MeshDir);
        MaterialCache.Clear();
        GameObject root = null;
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == RootName) { root = go; break; }
        if (root == null) { Debug.LogError($"[maple-east] no '{RootName}' in the scene - run Build Maple City first."); return; }
        BuildEastDistrict(root.transform);
        BuildNeoLayer(root.transform, CityRoute.Load(), "Old");
        RouteGraphBaker.BakeAsset();
        var regionsForSky = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regionsForSky != null) { regionsForSky.mapleCitySky = BuildCitySky(); EditorUtility.SetDirty(regionsForSky); }
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[maple-east] saved '{scene.path}'.");
    }

    public static void BuildEastDistrict(Transform cityRoot)
    {
        if (cityRoot == null) return;
        for (int k = cityRoot.childCount - 1; k >= 0; k--)
            if (cityRoot.GetChild(k).name == EastGroupName) UnityEngine.Object.DestroyImmediate(cityRoot.GetChild(k).gameObject);
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(EastRoutePath) == null)
        {
            Debug.Log("[maple-east] no MapleRouteEast.json - skipping the second district.");
            return;
        }
        var route = CityRoute.Load(EastRoutePath);
        Debug.Log($"[maple-east] route: {route.Count} samples, {route.Length:0.0} m lap, y {route.MinY:0.0} - {route.MaxY:0.0} m.");
        var east = new GameObject(EastGroupName).transform;
        east.SetParent(cityRoot, false);
        MeshPrefix = "East_";
        PrepareEastKeepOut();
        try
        {
            BuildBasin(east, route);
            BuildGround(east, route);
            BuildRoad(east, route);
            BuildKerbsAndPavements(east, route);
            BuildBuildings(east, route);
            BuildStreetTrees(east, route);
            BuildMapleGate(east, route);
            BuildCanalAndBridge(east, route);
            BuildTramLine(east, route);
            BuildStreetFurniture(east, route);
            BuildLeafVfx(east, route);
            BuildEastLandmarks(east, route);
            BuildNeoLayer(east, route, "East");
        }
        finally { MeshPrefix = ""; EastKeepOut.Clear(); }
        Debug.Log($"[maple-east] district built: {east.GetComponentsInChildren<MeshRenderer>(true).Length} renderers.");
    }

    // =================================================================== route

    [Serializable] private class SampleDto { public float[] p, t, s, u; public float bank, d; }
    [Serializable] private class RouteDto { public float roadHalfWidth, shoulderWidth; public SampleDto[] samples; }

    /// <summary>
    /// The published city centreline, in Unity world space.
    ///
    /// The JSON carries a CLOSING sample: station N is a copy of station 0 at d = lap length, so
    /// a course leg of 0..length has a real closing span. Geometry here therefore iterates spans
    /// 0..Count-2 and gets a fully closed loop with no wrap special-casing anywhere.
    /// </summary>
    public class CityRoute
    {
        public Vector3[] Position = Array.Empty<Vector3>();
        public Vector3[] Tangent = Array.Empty<Vector3>();
        public float[] Distance = Array.Empty<float>();

        public int Count => Position.Length;
        public float Length => Distance.Length == 0 ? 0f : Distance[Distance.Length - 1];
        public float MinY, MaxY;

        /// <summary>Arc fraction of a station - the parameter every profile in this file uses.</summary>
        public float Frac(int i) => Length <= 0.001f ? 0f : Distance[i] / Length;

        /// <summary>Horizontal right-hand side vector - ground and kerbs are never banked.</summary>
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

        public static CityRoute Load(string path = null)
        {
            path = path ?? RoutePath;
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (text == null)
                throw new FileNotFoundException(
                    $"{path} is missing. Run: python tools/blender/maple_city_route.py (or maple_city_east_route.py)");

            var dto = JsonUtility.FromJson<RouteDto>(text.text);
            if (dto?.samples == null || dto.samples.Length < 2)
                throw new InvalidDataException($"{path} has no samples.");

            int n = dto.samples.Length;
            var r = new CityRoute
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
                r.MinY = Mathf.Min(r.MinY, r.Position[i].y);
                r.MaxY = Mathf.Max(r.MaxY, r.Position[i].y);
            }
            return r;
        }
    }

    // =================================================================== ground

    /// <summary>
    /// Ground height at a station and lateral offset.
    ///
    /// A city is not a hillside: within a block of the road the ground is essentially FLAT at
    /// street level, which is what lets buildings sit on it without a plinth. Further out it
    /// terraces away and finally skirts down to the basin plate. The three bands are:
    ///
    ///   |o| &lt;  block   : street level (road height minus the verge drop) - dead flat
    ///   block..skirt   : gentle terrace fall, plus low-frequency relief so the districts are
    ///                    not a perfect plateau
    ///   skirt..corridor: hard fall to <see cref="BasinY"/>, hidden behind the building rows
    /// </summary>
    private static float GroundHeight(CityRoute r, int i, float offset)
    {
        const float BlockM = 26f;      // flat street-level apron: kerb, pavement, building feet
        const float SkirtM = 52f;      // where the terrace starts falling to the basin

        float a = Mathf.Abs(offset);
        float street = r.Position[i].y - VergeDropM;

        if (a <= BlockM) return street;

        if (a <= SkirtM)
        {
            float t = Mathf.InverseLerp(BlockM, SkirtM, a);
            // PROVISIONAL relief: +-1.6 m of low-frequency roll so back-lots and courtyards are
            // not a billiard table. Keyed on world XZ so it is stable under re-sampling.
            var p = r.Position[i] + r.SideFlat(i) * offset;
            float relief = (Mathf.PerlinNoise(p.x * 0.0085f + 11.3f, p.z * 0.0085f + 4.7f) - 0.5f) * 3.2f;
            return street - t * 2.4f + relief * t;
        }

        float s = Mathf.InverseLerp(SkirtM, CorridorHalfWidthM, a);
        float outerTop = street - 2.4f;
        return Mathf.Lerp(outerTop, BasinY, Mathf.SmoothStep(0f, 1f, s));
    }

    /// <summary>
    /// The flat plate the whole city sits in, at <see cref="BasinY"/>.
    ///
    /// Without it the corridor ribbon would end in mid-air and the rider would see sky through
    /// the gap between the loop's inner edge and its far side. It is a single quad: it is only
    /// ever glimpsed between buildings, and the region's fog swallows it long before its edge.
    /// </summary>
    private static void BuildBasin(Transform root, CityRoute route)
    {
        var group = new GameObject("City Basin").transform;
        group.SetParent(root, false);

        var b = new Bounds(route.Position[0], Vector3.zero);
        for (int i = 0; i < route.Count; i++) b.Encapsulate(route.Position[i]);
        float pad = 7000f;   // wide enough that its edge is never seen from the ground or from the megatower view

        var v = new Vector3[]
        {
            new Vector3(b.min.x - pad, BasinY, b.min.z - pad),
            new Vector3(b.max.x + pad, BasinY, b.min.z - pad),
            new Vector3(b.min.x - pad, BasinY, b.max.z + pad),
            new Vector3(b.max.x + pad, BasinY, b.max.z + pad),
        };
        float tile = (b.size.x + 2 * pad) / 24f;
        var uv = new Vector2[]
        {
            new Vector2(0, 0), new Vector2(tile, 0), new Vector2(0, tile), new Vector2(tile, tile),
        };
        var tris = new List<int> { 0, 2, 1, 1, 2, 3 };
        AddMesh(group, "City Basin Plate", Finish("MapleCity_Basin", v, uv, tris),
                CityGroundMaterial(), collider: false);
    }

    private static void BuildGround(Transform root, CityRoute route)
    {
        var group = new GameObject("City Ground").transform;
        group.SetParent(root, false);

        // Lateral nodes, metres from the centreline. Dense near the road (where the kerb,
        // pavement and building feet all have to sit on a believable surface), coarse outside.
        float[] nodes = { -CorridorHalfWidthM, -52f, -38f, -26f, -14f, -7.5f, 0f,
                          7.5f, 14f, 26f, 38f, 52f, CorridorHalfWidthM };

        var rows = new List<int>();
        for (int i = 0; i < route.Count - 1; i += GroundStride) rows.Add(i);
        rows.Add(route.Count - 1);          // the closing station, so the ribbon shuts cleanly

        int cols = nodes.Length;
        var verts = new Vector3[rows.Count * cols];
        var uvs = new Vector2[verts.Length];
        var uv2 = new Vector2[verts.Length];

        for (int ri = 0; ri < rows.Count; ri++)
        {
            int i = rows[ri];
            var p = route.Position[i];
            var s = route.SideFlat(i);
            for (int c = 0; c < cols; c++)
            {
                float o = nodes[c];
                float y = GroundHeight(route, i, o);
                int k = ri * cols + c;
                verts[k] = new Vector3(p.x + s.x * o, y, p.z + s.z * o);
                // METRE-SCALE UVs on both axes, so the ground texture is the size it was
                // authored for. The terrain shader projects triplanar anyway, but UV0 still
                // drives its macro-variation lookup.
                uvs[k] = new Vector2(o / 8f, route.Distance[i] / 8f);
                // UV1 carries the terrain shader's (rock, scree) splat weights. It MUST be
                // written even when it is nearly all zero: on a mesh with no UV1 Unity falls
                // back to UV0, whose v runs to thousands of metres, saturates to 1, and paints
                // the entire city beige. That exact failure is documented on the coast pass.
                // The city is paved, so it rides almost entirely on the "scree" slot (which is
                // retinted to warm stone below) with a little grass in the far back-lots.
                float paved = 1f - Mathf.Clamp01((Mathf.Abs(o) - 30f) / 26f);
                uv2[k] = new Vector2(0f, Mathf.Clamp01(paved));
            }
        }

        var tris = new List<int>();
        for (int ri = 0; ri < rows.Count - 1; ri++)
        for (int c = 0; c < cols - 1; c++)
        {
            int a = ri * cols + c, bb = a + 1;
            int d = (ri + 1) * cols + c, e = d + 1;
            tris.Add(a); tris.Add(d); tris.Add(bb);
            tris.Add(bb); tris.Add(d); tris.Add(e);
        }

        AddMesh(group, "City Ground", Finish("MapleCity_Ground", verts, uvs, tris, uv2),
                CityGroundMaterial(), collider: false);
    }

    // =================================================================== road

    /// <summary>Carriageway surface height at a station and offset, including crown and bias.</summary>
    private static float RoadY(CityRoute r, int i, float offset) =>
        r.Position[i].y + RoadSurfaceLiftM + CrownAt(offset);

    private static void BuildRoad(Transform root, CityRoute route)
    {
        var group = new GameObject("City Roadway").transform;
        group.SetParent(root, false);

        // TWO SURFACES, ONE SWEEP. The carriageway is built twice over the whole loop, once per
        // surface material, and each copy is masked to its own arc range. That is cheaper to
        // reason about than a single mesh with two submeshes and - crucially - it lets the two
        // ranges OVERLAP by one span at each boundary, so the setts and the asphalt meet with
        // no hairline of basin showing through.
        SweptCarriageway(group, route, "City Asphalt", "MapleCity_Asphalt",
                         AsphaltMaterial(), inCobble: false);
        SweptCarriageway(group, route, "City Setts", "MapleCity_Setts",
                         SettsMaterial(), inCobble: true);

        BuildMarkings(group, route);
    }

    /// <summary>
    /// Sweeps the carriageway across its (varying) width for the spans that belong to one
    /// surface. <paramref name="inCobble"/> selects the Old Town range or its complement.
    /// </summary>
    private static void SweptCarriageway(Transform group, CityRoute route, string name,
                                         string meshName, Material mat, bool inCobble)
    {
        const int Cols = 9;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        for (int i = 0; i < route.Count - 1; i++)
        {
            // One span of overlap on each side of the boundary, so the two surfaces butt.
            bool a = IsCobble(route.Frac(i));
            bool b = IsCobble(route.Frac(i + 1));
            if (inCobble ? (!a && !b) : (a && b)) continue;

            int b0 = verts.Count;
            for (int k = 0; k < 2; k++)
            {
                int idx = i + k;
                var p = route.Position[idx];
                var s = route.SideFlat(idx);
                float half = CarriagewayHalfWidth(route.Frac(idx));
                for (int c = 0; c < Cols; c++)
                {
                    float o = Mathf.Lerp(-half, half, c / (float)(Cols - 1));
                    float y = RoadY(route, idx, o);
                    verts.Add(new Vector3(p.x + s.x * o, y, p.z + s.z * o));
                    // Isotropic metre-scale UVs. The coast pass documents at length why: map
                    // the tile across "the whole carriageway into 0..1" and the aggregate is
                    // stretched into a flat grey plane.
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

        if (tris.Count == 0) return;
        // Only the asphalt carries the collider: two overlapping colliders on the same span
        // would be a physics coin-flip, and nothing in the ride raycasts the setts.
        AddMesh(group, name, Finish(meshName, verts.ToArray(), uvs.ToArray(), tris), mat,
                collider: !inCobble);
    }

    private static bool IsCobble(float frac) => frac >= CobbleStartFrac && frac <= CobbleEndFrac;

    private static void BuildMarkings(Transform group, CityRoute route)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // Dashed centre line only where the road is ASPHALT: Old Town setts are unmarked, which
        // is both true to a Japanese old town and a free legibility cue that the surface (and
        // the gradient) has changed. 5 m dash / 5 m gap, PROVISIONAL.
        Strip(route, 0f, 0.15f, 5f, 5f, verts, uvs, tris, asphaltOnly: true);
        // Solid edge lines, inset from the varying kerb by a constant 0.3 m.
        Strip(route, 0f, 0.12f, 0f, 1f, verts, uvs, tris, asphaltOnly: true, edge: -1);
        Strip(route, 0f, 0.12f, 0f, 1f, verts, uvs, tris, asphaltOnly: true, edge: +1);

        if (tris.Count > 0)
            AddMesh(group, "City Markings",
                    Finish("MapleCity_Markings", verts.ToArray(), uvs.ToArray(), tris),
                    // No rim: a rim-lit 30 cm stripe glows along its edges and reads as a
                    // raised slab rather than paint. Shade matches the road.
                    RoadSurface(CelMaterial("MapleCity_LinePaint", new Color(0.88f, 0.87f, 0.82f),
                                gloss: 0.04f, spec: 0.03f, rim: 0f, shade: RoadShade)),
                    collider: false);

        BuildCycleLane(group, route);
    }

    /// <summary>
    /// The blue 自転車ナビライン, the same on-brand cycling lane the coast carries. Painted on the
    /// boulevard asphalt only - the Old Town's cobbled lane is too narrow and too old for it.
    /// </summary>
    private static void BuildCycleLane(Transform group, CityRoute route)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        Strip(route, 0f, 0.20f, 0f, 1f, verts, uvs, tris, asphaltOnly: true, edge: -1, inset: 0.95f);
        Strip(route, 0f, 0.20f, 0f, 1f, verts, uvs, tris, asphaltOnly: true, edge: +1, inset: 0.95f);
        if (tris.Count == 0) return;
        AddMesh(group, "City Cycle Lane",
                Finish("MapleCity_CycleLane", verts.ToArray(), uvs.ToArray(), tris),
                // Muted, matte nav-lane blue (was 0.11/0.42/0.86 with rim 0.28, which the city
                // grade pushed to a glowing neon stripe). Same family as Shiosai's lane paint.
                RoadSurface(CelMaterial("MapleCity_CycleLanePaint", new Color(0.22f, 0.32f, 0.46f),
                            gloss: 0.04f, spec: 0.03f, rim: 0f, shade: RoadShade)),
                collider: false);
    }

    /// <summary>
    /// Lays a painted strip along the route. <paramref name="edge"/> of 0 means "at
    /// <paramref name="offset"/> from the centreline"; -1 / +1 mean "tracked to the left / right
    /// carriageway edge", which is what keeps the edge lines and the cycle lane glued to a road
    /// whose width changes along the lap.
    /// </summary>
    private static void Strip(CityRoute route, float offset, float width, float dash, float gap,
                              List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                              bool asphaltOnly = false, int edge = 0, float inset = 0.35f)
    {
        for (int i = 0; i < route.Count - 1; i++)
        {
            if (asphaltOnly && (IsCobble(route.Frac(i)) || IsCobble(route.Frac(i + 1)))) continue;
            if (dash > 0f && route.Distance[i] % (dash + gap) > dash) continue;

            int b = verts.Count;
            for (int k = 0; k < 2; k++)
            {
                int idx = i + k;
                var p = route.Position[idx];
                var s = route.SideFlat(idx);
                float o = offset;
                if (edge != 0) o = edge * (CarriagewayHalfWidth(route.Frac(idx)) - inset);
                float y = RoadY(route, idx, o) + MarkingLiftM;
                verts.Add(new Vector3(p.x + s.x * (o - width), y, p.z + s.z * (o - width)));
                verts.Add(new Vector3(p.x + s.x * (o + width), y, p.z + s.z * (o + width)));
                uvs.Add(new Vector2(0f, route.Distance[idx]));
                uvs.Add(new Vector2(1f, route.Distance[idx]));
            }
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
        }
    }

    // =================================================================== kerbs and pavements

    /// <summary>
    /// The raised kerb face and the pavement behind it, both sides, all the way round.
    ///
    /// This is the single cheapest piece of geometry in the region and the one that does the
    /// most work: a carriageway that meets the ground flush reads as a country lane no matter
    /// what is built beside it, and a 14 cm kerb line running away down the street is what the
    /// eye uses to read "city". It also gives every building row a consistent, believable
    /// foot to stand on.
    /// </summary>
    private static void BuildKerbsAndPavements(Transform root, CityRoute route)
    {
        var group = new GameObject("City Kerbs").transform;
        group.SetParent(root, false);

        var kv = new List<Vector3>(); var ku = new List<Vector2>(); var kt = new List<int>();
        var pv = new List<Vector3>(); var pu = new List<Vector2>(); var pt = new List<int>();

        for (int i = 0; i < route.Count - 1; i++)
        for (int side = -1; side <= 1; side += 2)
        {
            int kb = kv.Count, pb = pv.Count;
            for (int k = 0; k < 2; k++)
            {
                int idx = i + k;
                var p = route.Position[idx];
                var s = route.SideFlat(idx);
                float half = CarriagewayHalfWidth(route.Frac(idx));
                float o = side * half;
                float roadY = RoadY(route, idx, o);
                float topY = roadY + KerbHeightM;
                float d = route.Distance[idx];

                // kerb face: road edge up to pavement top
                kv.Add(new Vector3(p.x + s.x * o, roadY, p.z + s.z * o));
                kv.Add(new Vector3(p.x + s.x * o, topY, p.z + s.z * o));
                ku.Add(new Vector2(0f, d * 0.5f)); ku.Add(new Vector2(1f, d * 0.5f));

                // pavement top: kerb line out to the building line
                float o2 = side * (half + PavementWidthM);
                pv.Add(new Vector3(p.x + s.x * o, topY, p.z + s.z * o));
                pv.Add(new Vector3(p.x + s.x * o2, topY, p.z + s.z * o2));
                pu.Add(new Vector2(0f, d / 2.5f)); pu.Add(new Vector2(1f, d / 2.5f));   // C7: 5 x 2.5 m paver tile
            }
            // Winding order flips with the side so both faces point outward/up.
            if (side < 0)
            {
                kt.Add(kb); kt.Add(kb + 2); kt.Add(kb + 1);
                kt.Add(kb + 1); kt.Add(kb + 2); kt.Add(kb + 3);
                pt.Add(pb); pt.Add(pb + 2); pt.Add(pb + 1);
                pt.Add(pb + 1); pt.Add(pb + 2); pt.Add(pb + 3);
            }
            else
            {
                kt.Add(kb); kt.Add(kb + 1); kt.Add(kb + 2);
                kt.Add(kb + 1); kt.Add(kb + 3); kt.Add(kb + 2);
                pt.Add(pb); pt.Add(pb + 1); pt.Add(pb + 2);
                pt.Add(pb + 1); pt.Add(pb + 3); pt.Add(pb + 2);
            }
        }

        // Kerb and pavement both use the city's own cast-concrete paving map.
        //
        // The blockout reused Sakura_Asphalt_Albedo here and the footpaths came back MAUVE: that
        // map is a warm terrain albedo, and under the city's golden-hour grade a warm grey tips
        // straight into pink. The replacement is authored cool and neutral, and the base colour
        // is pushed near white so the texture's own value - not a tint - is what shows.
        // COLOUR NOTE (measured, not guessed): the first fidelity pass authored these cool
        // (blue above red) to kill the mauve, and the paving STILL read dusty rose. Sampling the
        // render showed why - the lit pavement came back R 0.747 / G 0.613 / B 0.553, i.e. green
        // sitting 0.04 BELOW the red/blue midpoint. That green deficit is the pink, not a blue
        // excess: the warm key lifts red, the cool fill and fog lift blue, and nothing lifts
        // green. So the base tints carry green ABOVE both neighbours to land neutral under this
        // light rather than being authored neutral and arriving pink.
        AddMesh(group, "City Kerb Face",
                Finish("MapleCity_KerbFace", kv.ToArray(), ku.ToArray(), kt),
                StreetKerbMaterial(),
                collider: false);
        // VALUE, measured: with the base at 0.455 the SUNLIT footpath came back at 0.83/0.68/0.61
        // - luminance 0.71, which is blown-out chalk, and the warm key's own red bias then reads
        // as rose on anything that bright. Real dry concrete in low sun sits nearer 0.60-0.65
        // luminance, so the base is scaled ~0.82 and given a further green lift to cancel the
        // key's red bias in DIRECT light (GroundShade only corrects the shadow term).
        AddMesh(group, "City Pavement",
                Finish("MapleCity_Pavement", pv.ToArray(), pu.ToArray(), pt),
                StreetPavementMaterial(),
                collider: false);
    }

    // =================================================================== buildings

    /// <summary>
    /// A district's building rules. The city has three, and they are what make one lap feel
    /// like it passes through different neighbourhoods rather than down one infinite street.
    /// All PROVISIONAL.
    /// </summary>
    private struct District
    {
        public string Name;
        public float FromFrac, ToFrac;
        public Vector2 Height;       // storey-height range, metres
        public Vector2 Frontage;     // metres of street frontage per building
        public Vector2 Depth;        // metres back from the building line
        public float Setback;        // metres from the pavement edge to the facade
        public float Gap;            // metres of alley between neighbours
        public Color[] Plaster;      // facade palette
        public Color Roof;
        public float WindowChance;   // fraction of facades that get lit windows
    }

    /// <summary>
    /// The three districts, laid onto the arc fractions the route build report measured.
    ///
    ///   Canal district  0.00-0.26  mid-rise cream plaster over the canal flat
    ///   Old Town        0.26-0.53  low, tight, timber-and-plaster machiya on the cobbled climb
    ///   Boulevard       0.53-1.00  the tallest facades on the widest street, deepest setback
    /// </summary>
    private static readonly District[] Districts =
    {
        new District
        {
            Name = "Canal District", FromFrac = 0.00f, ToFrac = 0.26f,
            Height = new Vector2(9f, 19f), Frontage = new Vector2(11f, 19f),
            Depth = new Vector2(12f, 22f), Setback = 1.5f, Gap = 1.6f,
            Plaster = new[]
            {
                new Color(0.937f, 0.894f, 0.824f), // cream machiya plaster #efe4d2
                new Color(0.847f, 0.816f, 0.776f),
                new Color(0.612f, 0.663f, 0.702f), // canal blue-grey
                new Color(0.878f, 0.804f, 0.706f),
                new Color(0.729f, 0.604f, 0.529f), // weathered terracotta
                new Color(0.545f, 0.596f, 0.588f), // verdigris-stained stucco
            },
            Roof = new Color(0.286f, 0.290f, 0.330f), WindowChance = 0.72f,
        },
        new District
        {
            Name = "Old Town", FromFrac = 0.26f, ToFrac = 0.53f,
            Height = new Vector2(5.5f, 9.5f), Frontage = new Vector2(6f, 11f),
            Depth = new Vector2(9f, 15f), Setback = 0.6f, Gap = 0.7f,
            Plaster = new[]
            {
                new Color(0.902f, 0.859f, 0.780f),
                new Color(0.435f, 0.337f, 0.251f), // dark stained timber
                new Color(0.831f, 0.784f, 0.706f),
                new Color(0.337f, 0.271f, 0.220f), // near-black charred cedar (yakisugi)
                new Color(0.298f, 0.353f, 0.435f), // indigo noren blue
                new Color(0.588f, 0.451f, 0.325f), // warm ochre earth plaster
            },
            Roof = new Color(0.231f, 0.243f, 0.271f), WindowChance = 0.60f,
        },
        new District
        {
            Name = "Ginkgo Boulevard", FromFrac = 0.53f, ToFrac = 1.00f,
            Height = new Vector2(13f, 27f), Frontage = new Vector2(14f, 26f),
            Depth = new Vector2(16f, 28f), Setback = 2.6f, Gap = 2.2f,
            Plaster = new[]
            {
                new Color(0.898f, 0.867f, 0.827f),
                new Color(0.741f, 0.718f, 0.729f),
                new Color(0.831f, 0.773f, 0.706f),
                new Color(0.478f, 0.494f, 0.545f), // slate office curtain wall
                new Color(0.878f, 0.831f, 0.784f),
                new Color(0.639f, 0.478f, 0.427f), // brick-red commercial block
                new Color(0.373f, 0.404f, 0.451f), // dark glass tower
            },
            Roof = new Color(0.259f, 0.263f, 0.302f), WindowChance = 0.80f,
        },
    };

    private static District DistrictAt(float frac)
    {
        foreach (var d in Districts)
            if (frac >= d.FromFrac && frac < d.ToFrac) return d;
        return Districts[Districts.Length - 1];
    }

    // =================================================================== street wall (C7)

    // C7 street-wall tunables. ALL PROVISIONAL illustrative tuning, judged from chase captures.
    private const string StreetTextureDir = "Assets/Environment/MapleCity/Street/Textures";
    private const float StoreyM = 3.4f;             // texture-locked: facade tile = 4 storeys = 13.6 m
    private const float BayM = 3.0f;                // texture-locked: facade tile = 4 bays = 12.0 m
    private const float FacadeTileWM = 12f, FacadeTileHM = 13.6f;
    private const float ShopFloorM = 4.4f;          // ground-floor shop storey, floor to fascia top
    private const float OldTownShopFloorM = 3.3f;
    private const float PodiumChance = 0.40f;       // 2-storey base in a contrasting cladding
    private const float ShopRecessM = 0.45f;        // shop glazing set back behind the piers
    private const float ShopPierHalfM = 0.24f;
    private const float StallRiserM = 0.40f;
    private const float FasciaM = 0.75f;
    private const float ShopUnitM = 7.0f;
    private const float AwningChance = 0.55f;
    private const float AwningProjectM = 1.5f;
    private const float AwningDropM = 0.6f;
    private const float CanopyProjectM = 1.9f;      // flat steel canopy on modern blocks
    private const float CorniceProudM = 0.35f, CorniceM = 0.55f;
    private const float PilasterProudM = 0.18f, PilasterHalfM = 0.22f;
    private const float ParapetM = 0.9f;
    private const float BalconyProudM = 0.95f;
    private const float TallBuildingChance = 0.22f, TallScale = 1.6f;
    private const float SetbackChance = 0.32f, PlantChance = 0.45f, TankChance = 0.22f;
    private const float RowGuardM = 1.5f;           // whole buildings stay off the Maple Row ends
    private const float SideStreetEveryM = 260f, SideStreetWidthM = 11f;
    private const float ShopGlowK = 0.95f;          // HDRP/Unlit shop interiors (Row windows use 1.0)

    private enum Clad { Brick = 0, Limestone = 1, Metal = 2, Glass = 3, Plaster = 4, Timber = 5 }
    private static readonly string[] CladNames = { "Brick", "Limestone", "Metal", "Glass", "Plaster", "Timber" };
    private const int SubTrim = 6, SubShop = 7, FacadeSubs = 8;

    /// <summary>Shopfront pier / fascia paint. All at or below 0.70 so nothing chalks out.</summary>
    private static readonly Color[] ShopTints =
    {
        new Color(0.24f, 0.19f, 0.14f), new Color(0.16f, 0.17f, 0.18f), new Color(0.12f, 0.26f, 0.21f),
        new Color(0.36f, 0.12f, 0.12f), new Color(0.12f, 0.17f, 0.30f), new Color(0.62f, 0.58f, 0.52f),
        new Color(0.10f, 0.30f, 0.32f), new Color(0.70f, 0.66f, 0.58f),
    };

    /// <summary>Tree feet recorded by BuildStreetTrees so the furniture pass can lay grates.</summary>
    private static readonly List<(Vector3 foot, Vector3 t, float d)> TreeFeet =
        new List<(Vector3, Vector3, float)>();

    private static Clad PickClad(string district, System.Random rng)
    {
        double r = rng.NextDouble();
        switch (district)
        {
            case "Old Town": return r < 0.5 ? Clad.Timber : r < 0.8 ? Clad.Plaster : Clad.Brick;
            case "Canal District":
                return r < 0.35 ? Clad.Brick : r < 0.6 ? Clad.Plaster : r < 0.85 ? Clad.Limestone : Clad.Metal;
            default:
                return r < 0.28 ? Clad.Glass : r < 0.5 ? Clad.Metal : r < 0.78 ? Clad.Limestone : Clad.Brick;
        }
    }

    private static Clad PodiumClad(Clad main, System.Random rng)
    {
        var opts = new[] { Clad.Limestone, Clad.Metal, Clad.Brick };
        for (int n = 0; n < 8; n++) { var c = opts[rng.Next(opts.Length)]; if (c != main) return c; }
        return main == Clad.Limestone ? Clad.Metal : Clad.Limestone;
    }

    /// <summary>Per-building vertex tint over an already-coloured cladding map: never above 1.</summary>
    private static Color CladTint(System.Random rng)
    {
        float v = Mathf.Lerp(0.80f, 1.0f, (float)rng.NextDouble());
        float w = ((float)rng.NextDouble() - 0.5f) * 0.10f;
        return new Color(Mathf.Min(1f, v * (1f + w)), v, Mathf.Min(1f, v * (1f - w)), 1f);
    }

    /// <summary>Cornice / pilaster / parapet colour per cladding (x MapleStreet_Plain ~0.78).</summary>
    private static Color TrimTint(Clad k, System.Random rng)
    {
        Color c;
        switch (k)
        {
            case Clad.Brick: c = new Color(0.72f, 0.68f, 0.60f); break;
            case Clad.Limestone: c = new Color(0.80f, 0.77f, 0.69f); break;
            case Clad.Plaster: c = new Color(0.78f, 0.76f, 0.71f); break;
            case Clad.Metal: c = new Color(0.24f, 0.25f, 0.27f); break;
            case Clad.Glass: c = new Color(0.30f, 0.32f, 0.35f); break;
            default: c = new Color(0.26f, 0.20f, 0.15f); break;
        }
        float j = Mathf.Lerp(0.90f, 1.02f, (float)rng.NextDouble());
        return new Color(c.r * j, c.g * j, c.b * j, 1f);
    }

    // Atlas cells (see tools/blender/build_maple_city_street_textures.py). Unity v = 0 is the
    // image BOTTOM, so rows counted from the image top are flipped here. Inset against bleed.
    private static Rect GlazeCell(int k)
    {
        int c = k % 4, r = (k / 4) % 4; const float e = 0.004f;
        return new Rect(c * 0.25f + e, 1f - (r + 1) * 0.25f + e, 0.25f - 2 * e, 0.25f - 2 * e);
    }
    private static Rect FasciaCell(int k)
    {
        int c = k % 2, r = (k / 2) % 8;
        return new Rect(c * 0.5f + 0.003f, 1f - (r + 1) * 0.0625f + 0.002f, 0.494f, 0.0585f);
    }
    private static Rect AwningCell(int k)
    {
        int c = k % 2, r = (k / 2) % 4;
        return new Rect(c * 0.5f + 0.003f, 0.5f - (r + 1) * 0.125f + 0.002f, 0.494f, 0.121f);
    }

    /// <summary>
    /// A building's local frame: x runs along the facade to the STREET VIEWER'S RIGHT (so fascia
    /// lettering is never mirrored), z runs INTO the block from the facade plane, y is absolute.
    /// </summary>
    private struct Frame
    {
        public Vector3 L, R, In;
        public Vector3 P(float x, float y, float z)
        {
            var q = L + R * x + In * z; q.y = y; return q;
        }
    }

    /// <summary>
    /// Multi-submesh combined-mesh builder. Street-wall materials render with _Cull 0 and the
    /// facade shader flips the normal on back faces, so winding never hides a face.
    /// </summary>
    private sealed class MeshBuf
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> U = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int>[] T;
        public MeshBuf(int subs)
        {
            T = new List<int>[subs];
            for (int k = 0; k < subs; k++) T[k] = new List<int>();
        }
        public int Tris { get { int n = 0; foreach (var t in T) n += t.Count / 3; return n; } }

        // p0 bottom-left, p1 bottom-right, p2 top-right, p3 top-left as seen from the front.
        public void Quad(int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                         Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3, Color c)
        {
            int b = V.Count;
            V.Add(p0); V.Add(p1); V.Add(p2); V.Add(p3);
            U.Add(u0); U.Add(u1); U.Add(u2); U.Add(u3);
            C.Add(c); C.Add(c); C.Add(c); C.Add(c);
            var t = T[sub];
            t.Add(b); t.Add(b + 3); t.Add(b + 1);
            t.Add(b + 1); t.Add(b + 3); t.Add(b + 2);
        }

        public void QuadM(int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color c, float tile = 3f)
        {
            float w = Vector3.Distance(p0, p1) / tile, h = Vector3.Distance(p0, p3) / tile;
            Quad(sub, p0, p1, p2, p3, new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h),
                 new Vector2(0, h), c);
        }

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            U.Add(Vector2.zero); U.Add(Vector2.right); U.Add(Vector2.up);
            C.Add(col); C.Add(col); C.Add(col);
            T[sub].Add(i); T[sub].Add(i + 1); T[sub].Add(i + 2);
        }

        /// <summary>Foot-centred box; a / b are half-extent vectors; five faces (no bottom).</summary>
        public void Box(int sub, Vector3 foot, Vector3 a, Vector3 b, float h, Color c)
        {
            var up = Vector3.up * h;
            Vector3 c0 = foot - a - b, c1 = foot + a - b, c2 = foot + a + b, c3 = foot - a + b;
            QuadM(sub, c0, c1, c1 + up, c0 + up, c);
            QuadM(sub, c1, c2, c2 + up, c1 + up, c);
            QuadM(sub, c2, c3, c3 + up, c2 + up, c);
            QuadM(sub, c3, c0, c0 + up, c3 + up, c);
            QuadM(sub, c0 + up, c1 + up, c2 + up, c3 + up, c);
        }

        public void Cylinder(int sub, Vector3 foot, float r, float h, Color c, int seg = 8)
        {
            var up = Vector3.up * h;
            for (int k = 0; k < seg; k++)
            {
                float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r;
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                QuadM(sub, foot + d0, foot + d1, foot + d1 + up, foot + d0 + up, c);
                Tri(sub, foot + up, foot + d1 + up, foot + d0 + up, c);
            }
        }

        /// <summary>
        /// Low-poly dome (the top <paramref name="cover"/> of an ellipsoid) sitting on
        /// <paramref name="foot"/>: clipped shrubs, hedge balls. Faceted on purpose (cel look).
        /// </summary>
        public void Blob(int sub, Vector3 foot, float r, float h, Color c, int seg = 8, int rings = 3,
                         float cover = 0.85f)
        {
            // Latitude runs from phi0 (below the equator, so the dome bulges past its base) to the pole.
            float phi0 = Mathf.Asin(Mathf.Clamp(1f - 2f * cover, -1f, 1f));
            float yBase = Mathf.Sin(phi0);
            Vector3 P(int k, int j)
            {
                float phi = Mathf.Lerp(phi0, Mathf.PI * 0.5f, j / (float)rings);
                float a = k * Mathf.PI * 2f / seg;
                float cr = Mathf.Cos(phi) * r;
                return foot + new Vector3(Mathf.Cos(a) * cr, (Mathf.Sin(phi) - yBase) / (1f - yBase) * h,
                                          Mathf.Sin(a) * cr);
            }
            for (int j = 0; j < rings; j++)
            for (int k = 0; k < seg; k++)
            {
                // Slightly lighter toward the top: sun-catching crown, shaded skirt.
                var cc = Color.Lerp(c, c * 1.25f, j / (float)rings);
                if (j == rings - 1)
                    Tri(sub, P(k, j), P(k, j + 1), P(k + 1, j), cc);
                else
                    Quad(sub, P(k, j), P(k + 1, j), P(k + 1, j + 1), P(k, j + 1),
                         Vector2.zero, Vector2.right, Vector2.one, Vector2.up, cc);
            }
        }

        public void Append(List<Vector3> v, List<Vector2> u, List<int> t, List<Color> c, int sub)
        {
            int b = V.Count;
            V.AddRange(v); U.AddRange(u);
            for (int k = 0; k < v.Count; k++) C.Add(c != null && k < c.Count ? c[k] : Color.white);
            foreach (int i in t) T[sub].Add(b + i);
        }
    }

    private static void WallX(MeshBuf b, int sub, Frame f, float x0, float x1, float y0, float y1,
                              float z, float vOrigin, Color c, float uOrigin = 0f,
                              float tileW = FacadeTileWM, float tileH = FacadeTileHM)
    {
        b.Quad(sub, f.P(x0, y0, z), f.P(x1, y0, z), f.P(x1, y1, z), f.P(x0, y1, z),
               new Vector2((x0 - uOrigin) / tileW, (y0 - vOrigin) / tileH),
               new Vector2((x1 - uOrigin) / tileW, (y0 - vOrigin) / tileH),
               new Vector2((x1 - uOrigin) / tileW, (y1 - vOrigin) / tileH),
               new Vector2((x0 - uOrigin) / tileW, (y1 - vOrigin) / tileH), c);
    }

    private static void WallZ(MeshBuf b, int sub, Frame f, float z0, float z1, float y0, float y1,
                              float x, float vOrigin, Color c)
    {
        b.Quad(sub, f.P(x, y0, z0), f.P(x, y0, z1), f.P(x, y1, z1), f.P(x, y1, z0),
               new Vector2(z0 / FacadeTileWM, (y0 - vOrigin) / FacadeTileHM),
               new Vector2(z1 / FacadeTileWM, (y0 - vOrigin) / FacadeTileHM),
               new Vector2(z1 / FacadeTileWM, (y1 - vOrigin) / FacadeTileHM),
               new Vector2(z0 / FacadeTileWM, (y1 - vOrigin) / FacadeTileHM), c);
    }

    private static void BoxF(MeshBuf b, int sub, Frame f, float x0, float x1, float y0, float y1,
                             float z0, float z1, Color c)
    {
        b.Box(sub, f.P((x0 + x1) * 0.5f, y0, (z0 + z1) * 0.5f), f.R * ((x1 - x0) * 0.5f),
              f.In * ((z1 - z0) * 0.5f), y1 - y0, c);
    }

    private static void FlatF(MeshBuf b, int sub, Frame f, float x0, float x1, float z0, float z1,
                              float y, Color c)
    {
        b.QuadM(sub, f.P(x0, y, z0), f.P(x1, y, z0), f.P(x1, y, z1), f.P(x0, y, z1), c);
    }

    private static void RectQuad(MeshBuf b, int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                 Rect r, Color c)
    {
        b.Quad(sub, p0, p1, p2, p3, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
               new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), c);
    }

    private static GameObject AddBuf(Transform parent, string name, string meshName, MeshBuf b,
                                     Material[] mats)
    {
        var mesh = new Mesh { name = meshName };
        mesh.indexFormat = b.V.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(b.V);
        mesh.SetUVs(0, b.U);
        mesh.SetColors(b.C);
        mesh.subMeshCount = b.T.Length;
        for (int k = 0; k < b.T.Length; k++) mesh.SetTriangles(b.T[k], k, false);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = AddMesh(parent, name, mesh, mats[0], collider: false);
        go.GetComponent<MeshRenderer>().sharedMaterials = mats;
        return go;
    }

    /// <summary>Cross-street stations: gaps in the street wall with signals at the corners.</summary>
    private static List<float> SideStreetStations(CityRoute route)
    {
        var list = new List<float>();
        for (float d = 150f; d < route.Length - 90f; d += SideStreetEveryM)
        {
            if (d > MapleRowBoutiques.StreetStartM - 60f && d < MapleRowBoutiques.StreetEndM + 60f) continue;
            list.Add(d);
        }
        return list;
    }


    /// <summary>
    /// C7 STREET WALL. Walks both sides of the loop by arc length laying a continuous street
    /// wall, and bakes it into four combined renderers whose NAMES the Maple Row carve and the
    /// Life occupancy map key on: "City Facades" (8 submeshes: six claddings, trim, shop atlas),
    /// "City Roofs", "City Windows" (lit shop interiors) and "City Rooftop Signs".
    ///
    /// Every building is snapped to whole 3 m bays and whole 3.4 m storeys, because the
    /// cladding maps are authored on exactly that grid - so the painted windows, the geometric
    /// pilasters, the cornice and the floor lines all agree.
    ///
    /// Buildings never straddle Maple Row (450-700 m) or a cross-street: the carve drops
    /// connected pieces by their centre, so a straddling block would be cut in half.
    /// </summary>
    private static void BuildBuildings(Transform root, CityRoute route)
    {
        var group = new GameObject("City Buildings").transform;
        group.SetParent(root, false);

        var fac = new MeshBuf(FacadeSubs);
        var roof = new MeshBuf(1);
        var glaze = new MeshBuf(1);
        var gv = new List<Vector3>(); var gu = new List<Vector2>(); var gt = new List<int>();
        var gc = new List<Color>();
        var sv = new List<Vector3>(); var su = new List<Vector2>(); var st = new List<int>();

        var keepOut = new List<Vector2>
        {
            new Vector2(MapleRowBoutiques.StreetStartM - RowGuardM, MapleRowBoutiques.StreetEndM + RowGuardM),
        };
        foreach (var z in EastKeepOut) keepOut.Add(z);        // East landmark zones (empty while building the Old Town)
        foreach (float ss in SideStreetStations(route))
            keepOut.Add(new Vector2(ss - SideStreetWidthM * 0.5f, ss + SideStreetWidthM * 0.5f));

        var rng = new System.Random(ScatterSeed);
        int placed = 0, shops = 0, awnings = 0, signs = 0;
        var cladCount = new int[6];

        for (int side = -1; side <= 1; side += 2)
        {
            // Offset the two sides' phase so facades never line up across the street.
            float d = side < 0 ? 0f : 9f;
            while (d < route.Length)
            {
                int i = route.IndexAt(d);
                float frac = route.Frac(i);
                var dis = DistrictAt(frac);
                bool oldTown = dis.Name == "Old Town";

                // The canal runs outside the LEFT pavement across the opening kilometre: the
                // left-hand row steps back past the far bank rather than walling off the water.
                float canalPush = 0f;
                if (side < 0 && frac > CanalFromFrac - 0.01f && frac < CanalToFrac + 0.01f)
                    canalPush = ChannelWidthM + 4.0f;

                float frontage = Mathf.Lerp(dis.Frontage.x, dis.Frontage.y, (float)rng.NextDouble());
                frontage = Mathf.Max(2f, Mathf.Round(frontage / BayM)) * BayM;
                float depth = Mathf.Lerp(dis.Depth.x, dis.Depth.y, (float)rng.NextDouble());
                float height = Mathf.Lerp(dis.Height.x, dis.Height.y, (float)rng.NextDouble());
                if (!oldTown && rng.NextDouble() < TallBuildingChance) height *= TallScale;

                bool skip = false;
                foreach (var ko in keepOut)
                {
                    if (d + frontage <= ko.x || d >= ko.y) continue;
                    float room = Mathf.Floor((ko.x - d) / BayM) * BayM;
                    if (room >= 2f * BayM) frontage = room;
                    else { d = ko.y + 0.5f; skip = true; }
                    break;
                }
                if (skip) continue;

                int iMid = route.IndexAt(d + frontage * 0.5f);
                var p = route.Position[iMid];
                var s = route.SideFlat(iMid);

                float half = CarriagewayHalfWidth(route.Frac(iMid));
                float front = half + PavementWidthM + dis.Setback + canalPush;

                // FOOT: anchor to whichever of (ground at the frontage, road) is LOWER and sink a
                // further 1.2 m, so a mismatch is buried rather than floating. The shop FLOOR is
                // the higher of the two so the glazing never starts below the pavement.
                float groundY = GroundHeight(route, iMid, side * front);
                float roadTopY = RoadY(route, iMid, side * half);
                float y0 = Mathf.Min(groundY, roadTopY) - 1.2f;
                float yF = Mathf.Max(groundY, roadTopY + KerbHeightM);

                var outward = s * -side;                                   // toward the street
                var R = Vector3.Cross(Vector3.up, -outward).normalized;    // street viewer's right
                var f = new Frame { L = p + s * (side * front) - R * (frontage * 0.5f), R = R, In = -outward };

                float top = StreetBuilding(fac, roof, glaze, f, frontage, depth, height, y0, yF,
                                           dis.Name, oldTown, rng, ref shops, ref awnings, cladCount);

                // Rooftop signage: the city's artificial-light layer. Original lettering only.
                if (!oldTown && rng.NextDouble() < SignChance)
                {
                    Sign(gv, gu, gt, gc, sv, su, st, f.P(frontage * 0.5f, top, depth * 0.5f),
                         R * (frontage * 0.5f), f.In * (depth * 0.5f), side, s, rng);
                    signs++;
                }

                placed++;
                d += frontage + dis.Gap;
            }
        }

        var facMats = new Material[FacadeSubs];
        for (int k = 0; k < 6; k++) facMats[k] = StreetFacadeMaterial((Clad)k);
        facMats[SubTrim] = TrimMaterial();
        facMats[SubShop] = ShopAtlasMaterial();
        AddBuf(group, "City Facades", "MapleCity_Facades", fac, facMats);
        AddBuf(group, "City Roofs", "MapleCity_Roofs", roof, new[] { TrimMaterial() });
        AddBuf(group, "City Windows", "MapleCity_Windows", glaze, new[] { ShopGlazingMaterial() });

        if (gt.Count > 0)
        {
            var sb = new MeshBuf(2);
            sb.Append(gv, gu, gt, gc, 0);
            sb.Append(sv, su, st, null, 1);
            AddBuf(group, "City Rooftop Signs", "MapleCity_Signs", sb,
                   new[] { SignMaterial(), WindowMaterial() });
        }

        Debug.Log($"[maple] street wall: {placed} buildings, {shops} shop units, {awnings} awnings, " +
                  $"{signs} rooftop signs; clad brick/lime/metal/glass/plaster/timber = " +
                  $"{string.Join("/", cladCount)}; tris facades {fac.Tris}, roofs {roof.Tris}, " +
                  $"shop glazing {glaze.Tris}; 4 renderers, {FacadeSubs + 3} material slots.");
    }

    /// <summary>
    /// One street-wall building. Returns the height of its highest roof edge (for signage).
    ///   ground floor : recessed lit shop glazing between piers, stall riser, soffit, fascia band
    ///                  with a lettered sign per unit, and a striped awning / steel canopy /
    ///                  machiya pent roof
    ///   podium       : optional storey in a contrasting cladding (the "2-storey base")
    ///   upper floors : tiled cladding (painted recessed windows + normal map) with geometric
    ///                  relief per kind: pilasters (brick, limestone), fins (glass), sunshade
    ///                  louvres (metal), balconies (plaster)
    ///   roofline     : cornice + parapet, setback top storey, plant boxes, water tanks - or a
    ///                  pitched tile roof in the Old Town
    /// </summary>
    private static float StreetBuilding(MeshBuf fac, MeshBuf roof, MeshBuf glaze, Frame f,
                                        float W, float D, float H, float y0, float yF,
                                        string district, bool oldTown, System.Random rng,
                                        ref int shops, ref int awnings, int[] cladCount)
    {
        Clad main = PickClad(district, rng);
        cladCount[(int)main]++;
        Color tint = CladTint(rng);
        Color trim = TrimTint(main, rng);
        float shopH = oldTown ? OldTownShopFloorM : ShopFloorM;
        int nUp = Mathf.Max(1, Mathf.RoundToInt((H - shopH) / StoreyM));
        bool podium = !oldTown && nUp >= 3 && rng.NextDouble() < PodiumChance;
        Clad baseClad = podium ? PodiumClad(main, rng) : main;
        Color baseTint = podium ? CladTint(rng) : tint;
        float yS = yF + shopH, yP = podium ? yS + StoreyM : yS, yT = yS + nUp * StoreyM;
        int sm = (int)main;

        // ---- body. The street face is modelled (real recessed openings, reveals, sills,
        // lintels) wherever the cladding has punched windows; see PunchedFacade.
        PunchedFacade(fac, main, f, W, yP, yT, yP, tint, trim, oldTown);
        if (podium) PunchedFacade(fac, baseClad, f, W, yS, yP, yS, baseTint, TrimTint(baseClad, new System.Random(1)), false);
        WallZ(fac, sm, f, 0f, D, y0, yT, 0f, yP, tint);
        WallZ(fac, sm, f, 0f, D, y0, yT, W, yP, tint);
        WallX(fac, sm, f, 0f, W, y0, yT, D, yP, tint);

        // ---- ground-floor shops
        Color pier = ShopTints[rng.Next(ShopTints.Length)];
        Color dark = new Color(pier.r * 0.55f, pier.g * 0.55f, pier.b * 0.55f, 1f);
        int units = Mathf.Max(1, Mathf.RoundToInt(W / ShopUnitM));
        float uw = W / units;
        float glazeTop = yS - FasciaM;
        float zr = ShopRecessM;
        WallX(fac, SubTrim, f, 0f, W, y0, yF + StallRiserM, zr, 0f, dark, 0f, 3f, 3f);
        FlatF(fac, SubTrim, f, 0f, W, -0.02f, zr, glazeTop, dark);
        for (int u = 0; u <= units; u++)
        {
            float xc = u * uw;
            BoxF(fac, SubTrim, f, Mathf.Max(0f, xc - ShopPierHalfM), Mathf.Min(W, xc + ShopPierHalfM),
                 y0, glazeTop, -0.06f, zr, pier);
        }
        bool canopy = !oldTown && (main == Clad.Glass || main == Clad.Metal) && rng.NextDouble() < 0.6;
        bool awningRun = !canopy && main != Clad.Timber && rng.NextDouble() < AwningChance;
        int awningCell = rng.Next(8);
        for (int u = 0; u < units; u++)
        {
            float a = u * uw + ShopPierHalfM, b = (u + 1) * uw - ShopPierHalfM;
            // The window now shows the SAME trade as the sign above it (ShopGlazing cell k is the
            // interior of fascia k - see tools/blender/maple_city_shop_interiors.py). Both draws
            // are kept, in the same order, so the shared scatter sequence is unchanged.
            _ = rng.Next(16);
            int trade = rng.Next(16);
            RectQuad(glaze, 0, f.P(a, yF + StallRiserM, zr), f.P(b, yF + StallRiserM, zr),
                     f.P(b, glazeTop, zr), f.P(a, glazeTop, zr), GlazeCell(trade), Color.white);
            RectQuad(fac, SubShop, f.P(a + 0.15f, glazeTop + 0.08f, -0.16f),
                     f.P(b - 0.15f, glazeTop + 0.08f, -0.16f), f.P(b - 0.15f, yS - 0.08f, -0.16f),
                     f.P(a + 0.15f, yS - 0.08f, -0.16f), FasciaCell(trade), Color.white);
            shops++;
            if (awningRun && rng.NextDouble() < 0.85)
            {
                Awning(fac, f, a + 0.08f, b - 0.08f, glazeTop - 0.04f,
                       rng.NextDouble() < 0.7 ? awningCell : rng.Next(8));
                awnings++;
            }
        }
        BoxF(fac, SubTrim, f, -0.05f, W + 0.05f, glazeTop, yS, -0.15f, 0.02f, dark);
        if (canopy)
            BoxF(fac, SubTrim, f, 0.3f, W - 0.3f, glazeTop - 0.22f, glazeTop - 0.04f,
                 -CanopyProjectM, 0f, new Color(0.16f, 0.17f, 0.18f));
        if (oldTown)
        {
            // Machiya pent roof (hisashi) over the shop storey, tiled like the main roof.
            var tile = new Color(0.20f, 0.21f, 0.25f);
            roof.QuadM(0, f.P(-0.3f, yS + 0.10f, -0.95f), f.P(W + 0.3f, yS + 0.10f, -0.95f),
                       f.P(W + 0.3f, yS + 0.62f, 0.02f), f.P(-0.3f, yS + 0.62f, 0.02f), tile);
        }

        // ---- facade relief above the shops
        int nAbove = Mathf.RoundToInt((yT - yP) / StoreyM);
        switch (main)
        {
            case Clad.Brick:
            case Clad.Limestone:
                BoxF(fac, SubTrim, f, -0.08f, W + 0.08f, yS, yS + 0.24f, -0.20f, 0.02f, trim);
                if (podium) BoxF(fac, SubTrim, f, -0.08f, W + 0.08f, yP - 0.10f, yP + 0.16f, -0.24f, 0.02f, trim);
                for (float x = 0f; x <= W + 0.01f; x += 2f * BayM)
                    BoxF(fac, SubTrim, f, Mathf.Max(-0.04f, x - PilasterHalfM),
                         Mathf.Min(W + 0.04f, x + PilasterHalfM), yP + 0.16f, yT, -PilasterProudM, 0.02f, trim);
                break;
            case Clad.Glass:
                for (float x = 0f; x <= W + 0.01f; x += BayM)
                    BoxF(fac, SubTrim, f, x - 0.05f, x + 0.05f, yP, yT, -0.32f, 0f, trim);
                break;
            case Clad.Metal:
                for (int k = 0; k < nAbove; k++)
                {
                    float y = yP + k * StoreyM + 2.2f;
                    BoxF(fac, SubTrim, f, 0f, W, y, y + 0.08f, -0.55f, 0f, trim);
                }
                for (float x = 0f; x <= W + 0.01f; x += 2f * BayM)
                    BoxF(fac, SubTrim, f, x - 0.08f, x + 0.08f, yP, yT, -0.22f, 0f, trim);
                break;
            case Clad.Plaster:
                if (!oldTown)
                {
                    var rail = new Color(0.30f, 0.31f, 0.32f);
                    for (int k = podium ? 0 : 1; k < nAbove; k++)
                    {
                        float y = yP + k * StoreyM;
                        BoxF(fac, SubTrim, f, 0.1f, W - 0.1f, y, y + 0.14f, -BalconyProudM, 0f, trim);
                        WallX(fac, SubTrim, f, 0.1f, W - 0.1f, y + 0.14f, y + 1.05f, -BalconyProudM, 0f,
                              rail, 0f, 3f, 3f);
                    }
                }
                break;
        }

        // ---- roofline
        if (oldTown)
            return PitchedRoof(fac, roof, f, W, D, yT, sm, tint, yP);

        bool glass = main == Clad.Glass;
        float proud = glass ? 0.12f : CorniceProudM;
        float ch = glass ? 0.35f : CorniceM;
        Color capCol = glass ? new Color(0.22f, 0.23f, 0.25f) : trim;
        BoxF(fac, SubTrim, f, -proud, W + proud, yT - 0.12f, yT - 0.12f + ch, -proud, D + proud, capCol);
        float yC = yT - 0.12f + ch;
        float pt = 0.25f, ph = glass ? 1.1f : ParapetM;
        BoxF(fac, SubTrim, f, 0f, W, yC, yC + ph, 0f, pt, capCol);
        BoxF(fac, SubTrim, f, 0f, W, yC, yC + ph, D - pt, D, capCol);
        BoxF(fac, SubTrim, f, 0f, pt, yC, yC + ph, pt, D - pt, capCol);
        BoxF(fac, SubTrim, f, W - pt, W, yC, yC + ph, pt, D - pt, capCol);
        FlatF(roof, 0, f, pt, W - pt, pt, D - pt, yC + 0.05f, new Color(0.30f, 0.31f, 0.33f));
        float topY = yC + ph;

        // Setback top storey: the block stepping in as it rises.
        if (nUp >= 3 && W >= 9f && D >= 10f && rng.NextDouble() < SetbackChance)
        {
            float x0 = 1.5f, x1 = W - 1.5f, z0 = 2.4f, z1 = D - 1.5f, h1 = yC + StoreyM;
            WallX(fac, sm, f, x0, x1, yC, h1, z0, yC, tint, x0);
            WallX(fac, sm, f, x0, x1, yC, h1, z1, yC, tint, x0);
            WallZ(fac, sm, f, z0, z1, yC, h1, x0, yC, tint);
            WallZ(fac, sm, f, z0, z1, yC, h1, x1, yC, tint);
            BoxF(fac, SubTrim, f, x0 - 0.2f, x1 + 0.2f, h1, h1 + 0.35f, z0 - 0.2f, z1 + 0.2f, capCol);
            topY = Mathf.Max(topY, h1 + 0.35f);
        }
        // Roof kit only on an open slab: a setback storey already fills the middle of the roof.
        if (topY <= yC + ph + 0.01f) RooftopKit(roof, fac, f, W, D, yC, capCol, pt);

        // Rooftop plant and water tanks, in the back half so the street silhouette stays clean.
        if (rng.NextDouble() < PlantChance)
        {
            int n = 1 + rng.Next(3);
            for (int k = 0; k < n; k++)
            {
                float bw = Mathf.Lerp(0.8f, 1.6f, (float)rng.NextDouble());
                float bd = Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble());
                float bx = Mathf.Lerp(bw + 0.5f, W - bw - 0.5f, (float)rng.NextDouble());
                float bz = Mathf.Lerp(D * 0.55f, D - bd - 0.5f, (float)rng.NextDouble());
                float bh = Mathf.Lerp(1.1f, 2.3f, (float)rng.NextDouble());
                BoxF(roof, 0, f, bx - bw, bx + bw, yC, yC + bh, bz - bd, bz + bd,
                     new Color(0.42f, 0.43f, 0.44f));
            }
        }
        if (rng.NextDouble() < TankChance && W > 6f)
        {
            float r = Mathf.Lerp(0.9f, 1.3f, (float)rng.NextDouble());
            var foot = f.P(Mathf.Lerp(r + 0.6f, W - r - 0.6f, (float)rng.NextDouble()), yC, D - r - 0.8f);
            roof.Box(0, foot, f.R * (r * 0.9f), f.In * (r * 0.9f), 0.6f, new Color(0.24f, 0.24f, 0.25f));
            roof.Cylinder(0, foot + Vector3.up * 0.6f, r, Mathf.Lerp(1.8f, 2.5f, (float)rng.NextDouble()),
                          rng.NextDouble() < 0.5 ? new Color(0.55f, 0.53f, 0.48f) : new Color(0.36f, 0.45f, 0.54f), 10);
        }
        return topY;
    }

    // =================================================================== C7 fidelity pass

    /// <summary>
    /// Where each cladding's PAINTED window sits inside one 3.0 m bay x 3.4 m storey cell, in
    /// metres (x from the bay's left edge, y from the storey floor). Measured from the box
    /// coordinates in tools/blender/build_maple_city_street_textures.py (facade tile 1024 px =
    /// 4 bays x 4 storeys, 256 px per cell; x m = px * 3/256, y m = 3.4 - px * 3.4/256).
    /// Keep in step with that script: if a box moves there, it must move here.
    /// </summary>
    private struct Opening { public float X0, X1, Y0, Y1, Depth; }

    private static bool PunchedOpening(Clad k, out Opening o)
    {
        switch (k)
        {
            case Clad.Brick:     o = new Opening { X0 = 0.820f, X1 = 2.180f, Y0 = 0.664f, Y1 = 2.630f, Depth = 0.24f }; return true;
            case Clad.Limestone: o = new Opening { X0 = 0.727f, X1 = 2.273f, Y0 = 0.584f, Y1 = 2.869f, Depth = 0.26f }; return true;
            case Clad.Plaster:   o = new Opening { X0 = 0.422f, X1 = 2.578f, Y0 = 0.930f, Y1 = 2.922f, Depth = 0.16f }; return true;
            case Clad.Timber:    o = new Opening { X0 = 0.703f, X1 = 2.297f, Y0 = 0.797f, Y1 = 2.603f, Depth = 0.12f }; return true;
            default:             o = default; return false;
        }
    }

    /// <summary>Metal cladding's ribbon glazing band inside a storey (image rows 94..238 px).</summary>
    private const float ArchM = 0.10f, ArchProudM = 0.05f;      // limestone architrave
    private static readonly Color KoshiSlat = new Color(0.26f, 0.19f, 0.14f);
    private const float RibbonY0 = 0.239f, RibbonY1 = 2.152f, RibbonDepthM = 0.18f, RibbonMullionM = 1.5f;

    /// <summary>Wall-thickness colour seen inside a reveal, per cladding, before the building tint.</summary>
    private static Color RevealTint(Clad k)
    {
        switch (k)
        {
            case Clad.Brick: return new Color(0.46f, 0.30f, 0.24f);
            case Clad.Limestone: return new Color(0.56f, 0.53f, 0.46f);
            case Clad.Plaster: return new Color(0.58f, 0.56f, 0.52f);
            case Clad.Timber: return new Color(0.22f, 0.16f, 0.12f);
            default: return new Color(0.20f, 0.21f, 0.23f);
        }
    }

    private static Color Mul(Color a, Color b, float k = 1f) =>
        new Color(Mathf.Min(1f, a.r * b.r * k), Mathf.Min(1f, a.g * b.g * k), Mathf.Min(1f, a.b * b.b * k), 1f);

    /// <summary>
    /// C7 FIDELITY. The street face of one cladding run (y0..y1, whole storeys) as real
    /// geometry instead of one painted quad.
    ///
    /// WHY. From the chase cam and the aerial captures, every street-wall block read as a
    /// flat box with windows printed on it: there was no depth, so no shadow line, so no
    /// sense of scale. This keeps the SAME cladding textures and UV mapping (so pilasters,
    /// string courses and the painted frames/glass all still line up) but cuts each painted
    /// window out of the wall and pushes its glass back by the reveal depth, lining the cut
    /// with reveals and adding a proud stone sill, a brick soldier lintel, a limestone
    /// architrave or a machiya koshi lattice.
    ///
    /// Only the STREET face (z = 0) is modelled; sides and backs stay painted, they are only
    /// ever seen at a glance. Glass and Metal claddings are curtain walls: Glass stays flush
    /// (its fins already give relief); Metal gets its ribbon band recessed with real mullions.
    /// </summary>
    private static void PunchedFacade(MeshBuf fac, Clad k, Frame f, float W, float y0, float y1,
                                      float vOrigin, Color tint, Color trim, bool oldTown)
    {
        int sub = (int)k;
        int storeys = Mathf.Max(0, Mathf.RoundToInt((y1 - y0) / StoreyM));
        int bays = Mathf.FloorToInt(W / BayM + 0.01f);
        Color reveal = Mul(RevealTint(k), tint);

        if (k == Clad.Metal && storeys > 0)
        {
            for (int s = 0; s < storeys; s++)
            {
                float ys = y0 + s * StoreyM, a = ys + RibbonY0, b = ys + RibbonY1;
                WallX(fac, sub, f, 0f, W, ys, a, 0f, vOrigin, tint);
                WallX(fac, sub, f, 0f, W, b, ys + StoreyM, 0f, vOrigin, tint);
                WallX(fac, sub, f, 0f, W, a, b, RibbonDepthM, vOrigin, tint);          // recessed glass
                fac.QuadM(SubTrim, f.P(0f, a, 0f), f.P(W, a, 0f), f.P(W, a, RibbonDepthM), f.P(0f, a, RibbonDepthM), reveal);
                fac.QuadM(SubTrim, f.P(0f, b, RibbonDepthM), f.P(W, b, RibbonDepthM), f.P(W, b, 0f), f.P(0f, b, 0f), reveal);
                fac.QuadM(SubTrim, f.P(0f, a, 0f), f.P(0f, a, RibbonDepthM), f.P(0f, b, RibbonDepthM), f.P(0f, b, 0f), reveal);
                fac.QuadM(SubTrim, f.P(W, a, RibbonDepthM), f.P(W, a, 0f), f.P(W, b, 0f), f.P(W, b, RibbonDepthM), reveal);
                var mull = new Color(0.16f, 0.17f, 0.19f);
                for (float x = RibbonMullionM; x < W - 0.2f; x += RibbonMullionM)
                    BoxF(fac, SubTrim, f, x - 0.035f, x + 0.035f, a, b, 0.02f, RibbonDepthM, mull);
            }
            return;
        }

        if (storeys == 0 || bays == 0 || !PunchedOpening(k, out var o))
        {
            WallX(fac, sub, f, 0f, W, y0, y1, 0f, vOrigin, tint);                       // flush (Glass)
            return;
        }

        float d = o.Depth;
        Color sill = Mul(trim, Color.white, 1.02f);
        for (int s = 0; s < storeys; s++)
        {
            float ys = y0 + s * StoreyM, wy0 = ys + o.Y0, wy1 = ys + o.Y1;
            // spandrel below / above the window row, full width
            WallX(fac, sub, f, 0f, W, ys, wy0, 0f, vOrigin, tint);
            WallX(fac, sub, f, 0f, W, wy1, ys + StoreyM, 0f, vOrigin, tint);
            float xPrev = 0f;
            for (int b = 0; b < bays; b++)
            {
                float wx0 = b * BayM + o.X0, wx1 = b * BayM + o.X1;
                WallX(fac, sub, f, xPrev, wx0, wy0, wy1, 0f, vOrigin, tint);           // pier
                xPrev = wx1;

                // the painted frame + glass, pushed back into the wall
                WallX(fac, sub, f, wx0, wx1, wy0, wy1, d, vOrigin, tint);
                // reveals: left, right, sill board, head
                fac.QuadM(SubTrim, f.P(wx0, wy0, 0f), f.P(wx0, wy0, d), f.P(wx0, wy1, d), f.P(wx0, wy1, 0f), reveal);
                fac.QuadM(SubTrim, f.P(wx1, wy0, d), f.P(wx1, wy0, 0f), f.P(wx1, wy1, 0f), f.P(wx1, wy1, d), reveal);
                fac.QuadM(SubTrim, f.P(wx0, wy0, 0f), f.P(wx1, wy0, 0f), f.P(wx1, wy0, d), f.P(wx0, wy0, d), Mul(reveal, Color.white, 1.12f));
                fac.QuadM(SubTrim, f.P(wx0, wy1, d), f.P(wx1, wy1, d), f.P(wx1, wy1, 0f), f.P(wx0, wy1, 0f), Mul(reveal, Color.white, 0.62f));

                switch (k)
                {
                    case Clad.Brick:
                        BoxF(fac, SubTrim, f, wx0 - 0.09f, wx1 + 0.09f, wy0 - 0.08f, wy0, -0.07f, 0.02f, sill);
                        BoxF(fac, SubTrim, f, wx0 - 0.07f, wx1 + 0.07f, wy1, wy1 + 0.21f, -0.035f, 0.02f,
                             Mul(RevealTint(Clad.Brick), tint, 1.18f));                  // soldier-course lintel
                        break;
                    case Clad.Limestone:
                        BoxF(fac, SubTrim, f, wx0 - ArchM, wx0, wy0, wy1 + ArchM, -ArchProudM, 0.02f, sill);
                        BoxF(fac, SubTrim, f, wx1, wx1 + ArchM, wy0, wy1 + ArchM, -ArchProudM, 0.02f, sill);
                        BoxF(fac, SubTrim, f, wx0, wx1, wy1, wy1 + ArchM, -ArchProudM, 0.02f, sill);
                        BoxF(fac, SubTrim, f, wx0 - ArchM - 0.04f, wx1 + ArchM + 0.04f, wy0 - 0.09f, wy0, -0.08f, 0.02f, sill);
                        break;
                    case Clad.Plaster:
                        BoxF(fac, SubTrim, f, wx0 - 0.03f, wx1 + 0.03f, wy0 - 0.05f, wy0, -0.04f, 0.02f, sill);
                        break;
                    case Clad.Timber:
                        // koshi lattice standing in the recess, plus head and sill beams
                        for (float x = wx0 + 0.12f; x < wx1 - 0.06f; x += 0.16f)
                            BoxF(fac, SubTrim, f, x - 0.022f, x + 0.022f, wy0, wy1, 0.01f, 0.05f, KoshiSlat);
                        BoxF(fac, SubTrim, f, wx0 - 0.08f, wx1 + 0.08f, wy1, wy1 + 0.08f, -0.05f, 0.02f, KoshiSlat);
                        BoxF(fac, SubTrim, f, wx0 - 0.08f, wx1 + 0.08f, wy0 - 0.08f, wy0, -0.05f, 0.02f, KoshiSlat);
                        break;
                }
            }
            WallX(fac, sub, f, xPrev, W, wy0, wy1, 0f, vOrigin, tint);                   // last pier
        }
    }

    // Rooftop kit tunables (PROVISIONAL). Drawn from a LOCAL rng seeded by the building's
    // position so adding them never shifts the shared scatter sequence (and so never moves a
    // building, shop, sign or Life placement that the shared rng drives).
    private const float OverrunChance = 0.70f, RoofHvacChance = 0.85f, RoofRailChance = 0.35f;

    private static int PosSeed(Vector3 p) =>
        unchecked(Mathf.RoundToInt(p.x * 10f) * 73856093 ^ Mathf.RoundToInt(p.z * 10f) * 19349663 ^ 0x5bd1e995);

    /// <summary>
    /// C7 FIDELITY. What makes a flat roof read as a real roof from the aerial and hillside
    /// views: a lift/stair overrun with its own coping, rows of HVAC condensers with fan
    /// discs, a membrane walkway and (sometimes) a steel safety rail. All in the back half or
    /// middle of the slab so the street silhouette stays the cornice line.
    /// </summary>
    private static void RooftopKit(MeshBuf roof, MeshBuf fac, Frame f, float W, float D, float yC,
                                   Color cap, float parapet)
    {
        if (W < 6f || D < 8f) return;
        var lr = new System.Random(PosSeed(f.L));
        var steel = new Color(0.34f, 0.35f, 0.36f);
        var unit = new Color(0.58f, 0.58f, 0.56f);
        var dark = new Color(0.12f, 0.12f, 0.13f);

        // membrane walkway pads from the overrun towards the front parapet
        FlatF(roof, 0, f, W * 0.5f - 0.5f, W * 0.5f + 0.5f, parapet + 0.4f, D * 0.6f, yC + 0.08f,
              new Color(0.40f, 0.40f, 0.41f));

        if (lr.NextDouble() < OverrunChance)
        {
            float ow = Mathf.Min(4.2f, W * 0.35f), od = Mathf.Min(3.6f, D * 0.30f), oh = 2.9f;
            float ox = Mathf.Lerp(parapet + ow * 0.5f + 0.6f, W - parapet - ow * 0.5f - 0.6f, (float)lr.NextDouble());
            float oz = D * 0.62f;
            BoxF(fac, SubTrim, f, ox - ow * 0.5f, ox + ow * 0.5f, yC, yC + oh, oz - od * 0.5f, oz + od * 0.5f,
                 Mul(cap, Color.white, 0.92f));
            BoxF(fac, SubTrim, f, ox - ow * 0.5f - 0.12f, ox + ow * 0.5f + 0.12f, yC + oh, yC + oh + 0.22f,
                 oz - od * 0.5f - 0.12f, oz + od * 0.5f + 0.12f, cap);                    // coping
            BoxF(fac, SubTrim, f, ox - 0.45f, ox + 0.45f, yC, yC + 2.1f, oz - od * 0.5f - 0.03f, oz - od * 0.5f, dark); // door
        }

        if (lr.NextDouble() < RoofHvacChance)
        {
            int n = 2 + lr.Next(4);
            float z = D * 0.80f, x = parapet + 1.0f;
            for (int k = 0; k < n && x < W - parapet - 1.2f; k++)
            {
                BoxF(roof, 0, f, x, x + 1.1f, yC, yC + 0.9f, z - 0.45f, z + 0.45f, unit);
                roof.Cylinder(0, f.P(x + 0.55f, yC + 0.9f, z), 0.38f, 0.04f, dark, 10);  // fan disc
                x += 1.5f;
            }
            // duct run back to the overrun
            BoxF(roof, 0, f, parapet + 1.0f, Mathf.Min(W - parapet - 1.0f, x), yC, yC + 0.35f, z - 0.95f, z - 0.65f, steel);
        }

        if (lr.NextDouble() < RoofRailChance)
        {
            float yr = yC + 1.1f;
            // top rail set back 0.4 m from the front parapet, posts every 2 m
            BoxF(fac, SubTrim, f, parapet + 0.4f, W - parapet - 0.4f, yr, yr + 0.05f, parapet + 0.4f, parapet + 0.45f, steel);
            for (float x = parapet + 0.4f; x <= W - parapet - 0.39f; x += 2f)
                BoxF(fac, SubTrim, f, x - 0.025f, x + 0.025f, yC, yr, parapet + 0.4f, parapet + 0.45f, steel);
        }
    }

    /// <summary>Striped canvas awning over one shop unit: sloped canvas plus hanging valance.</summary>
    private static void Awning(MeshBuf fac, Frame f, float a, float b, float yTop, int cell)
    {
        float yTip = yTop - AwningDropM, z = -AwningProjectM;
        var r = AwningCell(cell);
        float vVal = r.yMin + r.height * 0.20f;   // valance = the bottom 20 % of the cell
        fac.Quad(SubShop, f.P(a, yTip, z), f.P(b, yTip, z), f.P(b, yTop, -0.02f), f.P(a, yTop, -0.02f),
                 new Vector2(r.xMin, vVal + 0.003f), new Vector2(r.xMax, vVal + 0.003f),
                 new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), Color.white);
        fac.Quad(SubShop, f.P(a, yTip - 0.28f, z), f.P(b, yTip - 0.28f, z), f.P(b, yTip, z), f.P(a, yTip, z),
                 new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
                 new Vector2(r.xMax, vVal - 0.003f), new Vector2(r.xMin, vVal - 0.003f), Color.white);
        var cheek = new Color(0.30f, 0.28f, 0.26f);
        fac.Tri(SubTrim, f.P(a, yTop, -0.02f), f.P(a, yTip, z), f.P(a, yTip - 0.28f, z), cheek);
        fac.Tri(SubTrim, f.P(b, yTop, -0.02f), f.P(b, yTip, z), f.P(b, yTip - 0.28f, z), cheek);
    }

    /// <summary>Old Town machiya roof: dark kawara tile, ridge along the street, deep eaves.</summary>
    private static float PitchedRoof(MeshBuf fac, MeshBuf roof, Frame f, float W, float D, float yT,
                                     int cladSub, Color tint, float vOrigin)
    {
        const float E = 0.7f;
        float rh = Mathf.Min(3.2f, D * 0.28f);
        float yE = yT - 0.25f, yR = yT + rh, zm = D * 0.5f;
        var tile = new Color(0.20f, 0.21f, 0.25f);
        roof.QuadM(0, f.P(-E, yE, -E), f.P(W + E, yE, -E), f.P(W + E, yR, zm), f.P(-E, yR, zm), tile);
        roof.QuadM(0, f.P(W + E, yE, D + E), f.P(-E, yE, D + E), f.P(-E, yR, zm), f.P(W + E, yR, zm), tile);
        // gables, in the building's own cladding
        fac.Tri(cladSub, f.P(0f, yT, 0f), f.P(0f, yT, D), f.P(0f, yR - 0.05f, zm), tint);
        fac.Tri(cladSub, f.P(W, yT, 0f), f.P(W, yT, D), f.P(W, yR - 0.05f, zm), tint);
        // ridge cap
        BoxF(roof, 0, f, -E, W + E, yR - 0.12f, yR + 0.22f, zm - 0.22f, zm + 0.22f, new Color(0.16f, 0.16f, 0.19f));
        return yR + 0.22f;
    }

    /// <summary>
    /// A rooftop billboard: a dark support frame plus a LIT sign panel.
    ///
    /// The panel goes into the same mesh as the lit windows so it shares their unlit
    /// above-bloom-threshold material - it is part of the city's artificial-light layer, which
    /// is the one thing the design says no other MapleRide region has. The frame carries a
    /// vertex colour because signs are not all the same brand colour; the panel does not,
    /// because a light source reads as its own colour, not a tinted one.
    ///
    /// Original in-world signage only: these are abstract lit panels, never real brands.
    /// </summary>
    private static void Sign(List<Vector3> fv, List<Vector2> fu, List<int> ft, List<Color> fc,
                             List<Vector3> pv, List<Vector2> pu, List<int> pt,
                             Vector3 top, Vector3 right, Vector3 back, int side,
                             Vector3 sideVec, System.Random rng)
    {
        float w = right.magnitude * Mathf.Lerp(0.55f, 0.95f, (float)rng.NextDouble());
        float h = Mathf.Lerp(2.2f, 4.6f, (float)rng.NextDouble());
        var rightN = right.normalized;
        var facadeNormal = sideVec * -side;          // face the street, never the back alley

        // Two legs standing the sign off the roof slab.
        for (int k = -1; k <= 1; k += 2)
        {
            var leg = top + rightN * (k * w * 0.8f);
            Box(fv, fu, ft, fc, leg, rightN * 0.10f, facadeNormal * 0.10f, h * 0.45f,
                new Color(0.16f, 0.16f, 0.18f));
        }

        // The lit panel, standing proud of the legs and facing the street.
        var centre = top + Vector3.up * (h * 0.45f + h * 0.5f) + facadeNormal * 0.12f;
        Quad(pv, pu, pt, centre, rightN * w, Vector3.up * (h * 0.5f));
    }

    /// <summary>
    /// A row of lit window quads on the street-facing facade only.
    ///
    /// These are UNLIT warm quads, not emissive surfaces: the built-in forward path this project
    /// renders in has no emission-to-bloom pathway on the cel shader, so the only reliable way
    /// to make a window read as a LIGHT SOURCE is to draw it above the post-FX bloom threshold
    /// (1.42 in the city grade) and let bloom find it. That artificial-light layer is the one
    /// thing no other MapleRide region has, per the design doc.
    /// </summary>
    private static void Windows(List<Vector3> v, List<Vector2> uv, List<int> tri,
                                Vector3 centre, Vector3 right, Vector3 back, float height,
                                int side, Vector3 sideVec, System.Random rng)
    {
        var facadeNormal = sideVec * -side;             // points back at the street
        var face = centre - back + facadeNormal * 0.06f; // 6 cm proud, never z-fights
        float halfW = right.magnitude;

        int storeys = Mathf.Max(1, Mathf.FloorToInt(height / 3.2f));
        int bays = Mathf.Max(1, Mathf.FloorToInt(halfW * 2f / 3.0f));
        var rightN = right.normalized;

        for (int s = 0; s < storeys; s++)
        for (int b = 0; b < bays; b++)
        {
            if (rng.NextDouble() < 0.38) continue;      // dark flats: a fully lit block reads fake
            float y = 1.6f + s * 3.2f;
            if (y + 1.2f > height) continue;
            float x = Mathf.Lerp(-halfW + 1.2f, halfW - 1.2f, bays == 1 ? 0.5f : b / (float)(bays - 1));
            var c = face + rightN * x + Vector3.up * y;
            // Smaller and cooler than the first pass. At 1.3 x 1.7 m and 1.95 nits these read as
            // blown-out white SLABS rather than windows once the bloom hit them - the single
            // biggest thing making the blockout look like a blockout. A domestic window is about
            // 1.0 x 1.3 m, and it only needs to sit just over the 1.42 bloom threshold to glow.
            Quad(v, uv, tri, c, rightN * 0.50f, Vector3.up * 0.65f);
        }
    }

    // =================================================================== street trees

    /// <summary>
    /// Street SAKURA down both sides, planted on the PAVEMENT, not in the road.
    ///
    /// 2026-09-27 B4 (user): the street maples/ginkgo became cherry trees in bloom. They are
    /// crossed billboard canopies (Sakura Pass's blossom atlas) over a short tapered trunk with
    /// a few dark limbs, shaped as a broad umbrella crown; the canal kilometre gets weeping
    /// cherries so the whole lap is not one silhouette. Fallen petals lie on the pavement round
    /// each tree (<see cref="BuildPetalLitter"/>).
    ///
    /// ONE CANOPY MESH PER TINT, not one mesh with vertex colours. MapleRide/SakuraFoliage's
    /// Input struct is { float2 uv_MainTex; } - it does not read vertex colour, so painting the
    /// tints into a single combined canopy would have silently produced five hundred identical
    /// trees in the atlas's own pink. The city's facades solve the same problem with a purpose
    /// -built shader; the canopies do not, because the Sakura foliage shader's translucency and
    /// wind tuning are worth keeping exactly as they are, and five renderers is a fair price.
    /// </summary>
    private static void BuildStreetTrees(Transform root, CityRoute route)
    {
        var group = new GameObject("City Street Trees").transform;
        group.SetParent(root, false);

        // 2026-09-27 B4 (user, reference/bad_graphics/replace_with_sakura.png): every street tree
        // is now a SAKURA IN BLOOM. The canopy cards use Sakura Pass's blossom atlas (read-only,
        // shared texture) through the city's own LeafMaterial copies. Slot ORDER and COUNT are
        // unchanged, so the shared rng draws below land exactly as before: every tree, and so
        // every tree grate, stays where it was. Tints multiply the already-pink atlas, so they
        // stay near white and lean blue (the atlas and the warm city grade otherwise push them salmon); slot 0 is the deeper double-flowered Kanzan accent (~1 tree in 9)
        // and the canal kilometre's slot 4 is a weeping cherry (shidare-zakura).
        var palette = new (string name, Color tint)[]
        {
            ("SakuraKanzan",  new Color(0.930f, 0.620f, 0.900f)),   // deep pink accent
            ("SakuraYoshino", new Color(0.960f, 0.880f, 1.000f)),   // pale Somei-Yoshino
            ("SakuraPink",    new Color(0.950f, 0.760f, 0.960f)),
            ("SakuraBlush",   new Color(0.970f, 0.940f, 1.000f)),   // nearly white
            ("SakuraWeeping", new Color(0.960f, 0.800f, 0.980f)),   // canal weeping cherry
        };
        const int WillowSlot = 4;
        // Canopy detail draws come from their OWN rng, so adding leaf clusters never moves a tree.
        var leafRng = new System.Random(ScatterSeed + 23);
        // Branch limbs get a third rng, so they never disturb the canopy or the planting.
        var limbRng = new System.Random(ScatterSeed + 29);

        var tv = new List<Vector3>(); var tu = new List<Vector2>(); var tt = new List<int>();
        var cv = new List<Vector3>[palette.Length];
        var cu = new List<Vector2>[palette.Length];
        var ct = new List<int>[palette.Length];
        for (int k = 0; k < palette.Length; k++)
        {
            cv[k] = new List<Vector3>(); cu[k] = new List<Vector2>(); ct[k] = new List<int>();
        }

        var rng = new System.Random(ScatterSeed + 17);
        int planted = 0;
        TreeFeet.Clear();

        for (int side = -1; side <= 1; side += 2)
        {
            float d = side < 0 ? 4f : 13f;              // stagger the two sides
            while (d < route.Length)
            {
                int i = route.IndexAt(d);
                float frac = route.Frac(i);
                // Spacing: dense avenue planting on the boulevard, sparse in the tight old town
                // where there is no pavement to spare.
                bool oldTown = IsCobble(frac);
                float spacing = oldTown ? 26f : Mathf.Lerp(11f, 15f, (float)rng.NextDouble());
                d += spacing;
                if (oldTown && rng.NextDouble() < 0.55) continue;

                var p = route.Position[i];
                var s = route.SideFlat(i);
                float half = CarriagewayHalfWidth(frac);
                float o = side * (half + PavementWidthM * 0.55f);
                float baseY = RoadY(route, i, side * half) + KerbHeightM;

                var foot = new Vector3(p.x + s.x * o, baseY, p.z + s.z * o);
                float h = Mathf.Lerp(5.0f, 8.5f, (float)rng.NextDouble());
                float r = Mathf.Lerp(2.1f, 3.4f, (float)rng.NextDouble());

                // SAKURA FORM: a short, stout trunk that forks low into a few dark limbs under a
                // broad, flat-topped umbrella crown that is wider than it is tall (a Somei-Yoshino
                // avenue tree), instead of the old tall lollipop.
                float trunkH = Mathf.Max(2.3f, h * 0.40f);
                Trunk(tv, tu, tt, foot, trunkH, 0.20f, 0.14f);

                // The canal kilometre gets weeping cherries; everywhere else is avenue sakura.
                int slot = (frac < 0.22f && rng.NextDouble() < 0.35)
                    ? WillowSlot
                    : rng.Next(palette.Length - 1);
                // Kanzan only as an accent: most slot-0 picks become the pale avenue tints.
                if (slot == 0 && leafRng.NextDouble() < 0.66) slot = 1 + leafRng.Next(3);
                bool weeping = slot == WillowSlot;
                var crown = foot + Vector3.up * trunkH;
                float rs = r * 1.18f;
                var centre = crown + Vector3.up * (h * 0.27f);
                // Nothing in the crown hangs below head clearance over the pavement.
                float clearY = foot.y + 2.4f;

                // Dark limbs, visible through the gaps between blossom clusters.
                int limbs = 3 + limbRng.Next(3);
                for (int k = 0; k < limbs; k++)
                {
                    float a = (k / (float)limbs) * Mathf.PI * 2f + (float)limbRng.NextDouble() * 0.9f;
                    var outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float reach = rs * Mathf.Lerp(0.50f, 0.78f, (float)limbRng.NextDouble());
                    float rise = h * Mathf.Lerp(0.14f, 0.30f, (float)limbRng.NextDouble());
                    Limb(tv, tu, tt, crown - Vector3.up * 0.30f, crown + outDir * reach + Vector3.up * rise,
                         0.12f, 0.035f);
                }

                // Core crossed cards (same rng draws as before) plus a CLOUD of 7-9 smaller
                // crossed clusters on a FLATTENED dome, so the crown reads full from every side.
                for (int k = 0; k < 3; k++)
                {
                    float a = (float)(rng.NextDouble() * Math.PI) + k * (Mathf.PI / 3f);
                    var axis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Quad(cv[slot], cu[slot], ct[slot], centre, axis * rs, Vector3.up * (h * 0.25f));
                }
                int clusters = 7 + leafRng.Next(3);
                for (int c = 0; c < clusters; c++)
                {
                    float az = (c / (float)clusters) * Mathf.PI * 2f + (float)leafRng.NextDouble() * 0.6f;
                    float el = c == clusters - 1 ? 1.2f : Mathf.Lerp(-0.25f, 0.75f, (float)leafRng.NextDouble());
                    var dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el) * 0.55f, Mathf.Sin(az) * Mathf.Cos(el));
                    var cc = centre + dir * (rs * 0.66f);
                    float cr = r * Mathf.Lerp(0.48f, 0.66f, (float)leafRng.NextDouble());
                    // Weeping cherry: taller cards hanging from lower down, like falling curtains.
                    float halfUp = cr * (weeping ? 1.05f : 0.70f);
                    if (weeping) cc -= Vector3.up * (cr * 0.35f);
                    halfUp = Mathf.Max(0.3f, Mathf.Min(halfUp, cc.y - clearY));
                    for (int k = 0; k < 2; k++)
                    {
                        float a = az + k * (Mathf.PI * 0.5f) + (float)leafRng.NextDouble() * 0.4f;
                        var axis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        Quad(cv[slot], cu[slot], ct[slot], cc, axis * cr, Vector3.up * halfUp);
                    }
                }
                TreeFeet.Add((foot, new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z), route.Distance[i]));
                planted++;
            }
        }

        AddMesh(group, "City Tree Trunks",
                Finish("MapleCity_TreeTrunks", tv.ToArray(), tu.ToArray(), tt),
                // Cherry bark is dark purplish-brown, much darker than the old maple trunk.
                CelMaterial("MapleCity_Bark", new Color(0.235f, 0.185f, 0.180f), gloss: 0.10f,
                            spec: 0.06f, rim: 0.28f,
                            texture: SakuraTexture("Sakura_Bark_Albedo.png")),
                collider: false);

        for (int k = 0; k < palette.Length; k++)
        {
            if (ct[k].Count == 0) continue;
            AddMesh(group, $"City Canopy {palette[k].name}",
                    Finish($"MapleCity_Canopy_{palette[k].name}",
                           cv[k].ToArray(), cu[k].ToArray(), ct[k]),
                    LeafMaterial(palette[k].name, palette[k].tint), collider: false);
        }

        int litter = BuildPetalLitter(group, route);

        Debug.Log($"[maple] street trees: {planted} sakura planted along the loop, " +
                  $"{palette.Length} blossom tints, {litter} fallen-petal patches.");
    }

    /// <summary>
    /// Fallen petals on the PAVEMENT round each tree foot: a few flat blossom-atlas cards lying
    /// on the paving, kept within 1.9 m of the tree across the pavement (the kerb is 2.75 m
    /// away), so none land on the road. Skips Maple Row, like the tree grates.
    /// </summary>
    private static int BuildPetalLitter(Transform group, CityRoute route)
    {
        var rng = new System.Random(ScatterSeed + 31);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        int n = 0;
        foreach (var tf in TreeFeet)
        {
            if (!FurnitureAllowed(route, tf.d)) continue;
            var along = tf.t.normalized;
            var across = Vector3.Cross(Vector3.up, along).normalized;
            int patches = 2 + rng.Next(3);
            for (int k = 0; k < patches; k++)
            {
                float size = Mathf.Lerp(0.35f, 0.60f, (float)rng.NextDouble());
                float a = Mathf.Lerp(-3.0f, 3.0f, (float)rng.NextDouble());
                float c = Mathf.Lerp(-1.3f, 1.3f, (float)rng.NextDouble());
                var p = tf.foot + along * a + across * c + Vector3.up * (0.016f + k * 0.002f);
                float yaw = (float)(rng.NextDouble() * Math.PI * 2.0);
                var right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * size;
                var fwd = Vector3.Cross(right, Vector3.up).normalized * size;
                Quad(v, uv, tri, p, right, fwd);
                n++;
            }
        }
        if (n == 0) return 0;
        var mesh = Finish("MapleCity_PetalLitter", v.ToArray(), uv.ToArray(), tri);
        // Double-sided cards on shared vertices average to a zero normal; the litter lies flat.
        var up = new Vector3[mesh.vertexCount];
        for (int k = 0; k < up.Length; k++) up[k] = Vector3.up;
        mesh.normals = up;
        var go = AddMesh(group, "City Petal Litter", mesh, PetalLitterMaterial(), collider: false);
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return n;
    }

    // =================================================================== landmark

    /// <summary>
    /// The vermilion MAPLE GATE at the start/finish, straddling the road in Maple Gate Plaza.
    ///
    /// One landmark in this slice, on purpose: the start/finish line is the one place the player
    /// is guaranteed to look at on lap one and every lap after, and an unlandmarked crit start
    /// is indistinguishable from any other stretch of road. The remaining five named landmarks
    /// (Canal Sprint banner, Sky Terrace, Ginkgo avenue, river bridge, pocket shrine) belong to
    /// the M2 fidelity pass.
    ///
    /// Clearance is measured, not assumed: the crossbeam sits 7.2 m over the crown, well clear
    /// of any rider or camera boom.
    /// </summary>
    private static void BuildMapleGate(Transform root, CityRoute route)
    {
        var group = new GameObject("Maple Gate Plaza").transform;
        group.SetParent(root, false);

        int i = 0;                                        // arc 0 IS the plaza (see START_CORNER)
        var p = route.Position[i];
        var s = route.SideFlat(i);
        var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
        float half = CarriagewayHalfWidth(route.Frac(i));
        float span = half + PavementWidthM * 0.8f;
        float baseY = RoadY(route, i, 0f);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();

        const float PostR = 0.42f, Height = 7.2f;
        for (int k = -1; k <= 1; k += 2)
        {
            var foot = p + s * (k * span);
            foot.y = baseY;
            Box(v, uv, tri, null, foot + Vector3.up * (Height * 0.5f),
                t * PostR, s * PostR, Height, default, centredY: true);
        }
        // Main lintel and the second, shorter beam beneath it - the two-beam silhouette is what
        // makes a gate read as a gate rather than as scaffolding.
        Box(v, uv, tri, null, p + Vector3.up * (baseY - p.y + Height + 0.45f),
            t * 0.55f, s * (span + 1.5f), 0.9f, default, centredY: true);
        Box(v, uv, tri, null, p + Vector3.up * (baseY - p.y + Height - 0.9f),
            t * 0.38f, s * (span + 0.3f), 0.55f, default, centredY: true);

        AddMesh(group, "Maple Gate", Finish("MapleCity_Gate", v.ToArray(), uv.ToArray(), tri),
                CelMaterial("MapleCity_Vermilion", new Color(0.784f, 0.192f, 0.157f), gloss: 0.26f,
                            spec: 0.22f, rim: 0.45f),
                collider: false);
    }

    // =================================================================== dressing

    /// <summary>
    /// The canal along the flat opening kilometre, plus the arched stone bridge at the finish.
    ///
    /// Both are design-doc named landmarks (Canal Sprint 0.16, River Bridge Finish 0.92). The
    /// canal runs OUTSIDE the pavement on the left-hand side only, so it never intrudes on the
    /// racing line - the rider sprints ALONGSIDE water, which is the shot the doc asks for,
    /// rather than over it.
    /// </summary>
    private static void BuildCanalAndBridge(Transform root, CityRoute route)
    {
        var group = new GameObject("City Canal").transform;
        group.SetParent(root, false);

        var wv = new List<Vector3>(); var wu = new List<Vector2>(); var wt = new List<int>();
        var sv = new List<Vector3>(); var su = new List<Vector2>(); var st = new List<int>();
        var bv = new List<Vector3>(); var bu = new List<Vector2>(); var bt = new List<int>();
        var bc = new List<Color>();

        // ---- canal channel.
        //
        // HEIGHT IS THE WHOLE PROBLEM HERE. The first attempt sank the water 1.5 m below the
        // kerb in a 2.4 m trough, which is what a real canal looks like - and it was completely
        // invisible, because the CITY GROUND mesh is a continuous field at street level and it
        // simply drew over the top of the trough. Nothing raycasts or cuts holes in that field.
        //
        // So the canal is authored as a BRIMMING channel instead: the water sits marginally
        // ABOVE the surrounding ground, held in by a low stone coping wall on each bank. It
        // still reads unmistakably as a canal (and brim-full urban canals are entirely normal),
        // and it cannot be occluded by the ground it sits on.
        const float ChannelWidth = ChannelWidthM;
        const float WaterDrop = 0.06f;      // just below the kerb top, just above the ground
        int first = -1, last = -1;

        for (int i = 0; i < route.Count - 1; i++)
        {
            float f = route.Frac(i);
            if (f < CanalFromFrac || f > CanalToFrac) continue;
            if (first < 0) first = i;
            last = i;
        }

        if (first >= 0)
        {
            for (int i = first; i < last; i++)
            {
                float half = CarriagewayHalfWidth(route.Frac(i));
                float half2 = CarriagewayHalfWidth(route.Frac(i + 1));
                // Left side (negative), just beyond the pavement.
                float inner = half + PavementWidthM + 1.0f;
                float inner2 = half2 + PavementWidthM + 1.0f;

                var p0 = route.Position[i]; var p1 = route.Position[i + 1];
                var s0 = route.SideFlat(i); var s1 = route.SideFlat(i + 1);
                float y0 = RoadY(route, i, -half) + KerbHeightM;
                float y1 = RoadY(route, i + 1, -half2) + KerbHeightM;

                Vector3 A(float o, float dy, int k) => k == 0
                    ? new Vector3(p0.x - s0.x * o, y0 + dy, p0.z - s0.z * o)
                    : new Vector3(p1.x - s1.x * o, y1 + dy, p1.z - s1.z * o);

                // Near-bank coping wall: the low kerb that tells the eye "this edge is a drop
                // into water", seen from the racing line.
                Band(sv, su, st, A(inner, CopingHeightM, 0), A(inner, CopingHeightM, 1),
                      A(inner, -0.35f, 0), A(inner, -0.35f, 1), route.Distance[i]);
                // Top of the near coping.
                Band(sv, su, st, A(inner - 0.45f, CopingHeightM, 0), A(inner - 0.45f, CopingHeightM, 1),
                      A(inner, CopingHeightM, 0), A(inner, CopingHeightM, 1), route.Distance[i]);
                // Far bank coping, same treatment mirrored.
                Band(sv, su, st, A(inner + ChannelWidth, -0.35f, 0),
                      A(inner + ChannelWidth, -0.35f, 1),
                      A(inner + ChannelWidth, CopingHeightM, 0),
                      A(inner + ChannelWidth, CopingHeightM, 1), route.Distance[i]);
                Band(sv, su, st, A(inner + ChannelWidth, CopingHeightM, 0),
                      A(inner + ChannelWidth, CopingHeightM, 1),
                      A(inner + ChannelWidth + 0.45f, CopingHeightM, 0),
                      A(inner + ChannelWidth + 0.45f, CopingHeightM, 1), route.Distance[i]);
                // water plane
                Band(wv, wu, wt, A(inner, -WaterDrop, 0), A(inner, -WaterDrop, 1),
                      A(inner + ChannelWidth, -WaterDrop, 0),
                      A(inner + ChannelWidth, -WaterDrop, 1), route.Distance[i]);
            }

            AddMesh(group, "Canal Water", Finish("MapleCity_CanalWater",
                    wv.ToArray(), wu.ToArray(), wt), WaterMaterial(), collider: false);
            AddMesh(group, "Canal Embankment", Finish("MapleCity_CanalStone",
                    sv.ToArray(), su.ToArray(), st),
                    CelMaterial("MapleCity_CanalStone", new Color(0.495f, 0.535f, 0.520f),
                                gloss: 0.14f, spec: 0.09f, rim: 0.05f,
                                texture: CityTexture("MapleCity_Pavement_Albedo.png"),
                                shade: GroundShade),
                    collider: false);
        }

        // ---- arched river bridge at the finish: balustrades both sides plus arch rings below.
        {
            int mid = route.IndexAt(route.Length * BridgeFrac);
            int span = Mathf.Max(6, Mathf.RoundToInt(34f / 3f));   // ~34 m of bridge
            for (int i = mid - span; i <= mid + span; i++)
            {
                int j = ((i % route.Count) + route.Count) % route.Count;
                float half = CarriagewayHalfWidth(route.Frac(j));
                var p = route.Position[j]; var s = route.SideFlat(j);
                float y = RoadY(route, j, half) + KerbHeightM;
                var t = new Vector3(route.Tangent[j].x, 0f, route.Tangent[j].z).normalized;

                // Balustrade posts every other station, both sides.
                if (((i % 2) + 2) % 2 != 0) continue;
                for (int k = -1; k <= 1; k += 2)
                {
                    var o = s * (k * (half + PavementWidthM * 0.9f));
                    Box(bv, bu, bt, bc, p + o + Vector3.up * (y - route.Position[j].y),
                        t * 0.22f, s * 0.22f, 1.05f, new Color(0.88f, 0.86f, 0.83f));
                    // hand rail
                    Box(bv, bu, bt, bc,
                        p + o + Vector3.up * (y - route.Position[j].y + 1.05f),
                        t * 3.2f, s * 0.14f, 0.16f, new Color(0.80f, 0.78f, 0.75f));
                }
            }
            if (bt.Count > 0)
            {
                var bridge = Finish("MapleCity_Bridge", bv.ToArray(), bu.ToArray(), bt);
                bridge.colors = bc.ToArray();
                AddMesh(group, "River Bridge", bridge, FacadeMaterial(), collider: false);
            }
        }

        Debug.Log($"[maple] canal + bridge: channel stations {first}..{last}, " +
                  $"bridge at {BridgeFrac:0.00} arc.");
    }

    /// <summary>
    /// Tram rails set into the boulevard, and the overhead contact wire on its poles.
    ///
    /// The design calls tram rails a HAZARD (line choice) as well as dressing, so the rails are
    /// drawn as thin proud ribbons on the road surface rather than as a decal: the player should
    /// be able to see exactly where they are from a distance and choose a line around them.
    /// </summary>
    private static void BuildTramLine(Transform root, CityRoute route)
    {
        var group = new GameObject("City Tram Line").transform;
        group.SetParent(root, false);

        var rv = new List<Vector3>(); var ru = new List<Vector2>(); var rt = new List<int>();
        var pv = new List<Vector3>(); var pu = new List<Vector2>(); var pt = new List<int>();
        var pc = new List<Color>();
        var wv = new List<Vector3>(); var wu = new List<Vector2>(); var wt = new List<int>();
        var wc = new List<Color>();

        // Boulevard only - the design puts the tram on Ginkgo Boulevard, not up the Old Town
        // cobbles. Two rails, offset either side of the crown on the right-hand carriageway.
        const float GaugeHalf = 0.72f;
        const float RailCentre = 2.4f;
        const float RailWidth = 0.075f;
        int poles = 0;

        for (int i = 0; i < route.Count - 1; i++)
        {
            float f = route.Frac(i);
            if (f < 0.55f || f > 0.98f) continue;

            var p0 = route.Position[i]; var p1 = route.Position[i + 1];
            var s0 = route.SideFlat(i); var s1 = route.SideFlat(i + 1);

            for (int k = -1; k <= 1; k += 2)
            {
                float o = RailCentre + k * GaugeHalf;
                var a0 = new Vector3(p0.x + s0.x * (o - RailWidth), RoadY(route, i, o) + 0.012f,
                                     p0.z + s0.z * (o - RailWidth));
                var a1 = new Vector3(p1.x + s1.x * (o - RailWidth), RoadY(route, i + 1, o) + 0.012f,
                                     p1.z + s1.z * (o - RailWidth));
                var b0 = new Vector3(p0.x + s0.x * (o + RailWidth), RoadY(route, i, o) + 0.012f,
                                     p0.z + s0.z * (o + RailWidth));
                var b1 = new Vector3(p1.x + s1.x * (o + RailWidth), RoadY(route, i + 1, o) + 0.012f,
                                     p1.z + s1.z * (o + RailWidth));
                Band(rv, ru, rt, a0, a1, b0, b1, route.Distance[i]);
            }

            // Catenary poles every ~28 m on the right-hand pavement, with a wire span between.
            if (i % 10 != 0) continue;
            float half = CarriagewayHalfWidth(f);
            var s = route.SideFlat(i);
            var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            float baseY = RoadY(route, i, half) + KerbHeightM;
            var foot = new Vector3(p0.x + s.x * (half + PavementWidthM * 0.75f), baseY,
                                   p0.z + s.z * (half + PavementWidthM * 0.75f));
            Box(pv, pu, pt, pc, foot, t * 0.10f, s * 0.10f, TramWireHeightM + 1.4f,
                new Color(0.22f, 0.24f, 0.25f));
            // Cantilever arm reaching out over the rails.
            Box(pv, pu, pt, pc, foot + Vector3.up * (TramWireHeightM + 1.2f),
                t * 0.06f, s * ((half - RailCentre) * 0.5f + 0.4f), 0.10f,
                new Color(0.22f, 0.24f, 0.25f));

            // The contact wire itself - a long thin ribbon to the NEXT pole station.
            int n = Mathf.Min(route.Count - 1, i + 10);
            var q = route.Position[n];
            var sq = route.SideFlat(n);
            var wireA = new Vector3(p0.x + s.x * RailCentre,
                                    RoadY(route, i, RailCentre) + TramWireHeightM,
                                    p0.z + s.z * RailCentre);
            var wireB = new Vector3(q.x + sq.x * RailCentre,
                                    RoadY(route, n, RailCentre) + TramWireHeightM,
                                    q.z + sq.z * RailCentre);
            var dir = (wireB - wireA);
            var sideV = Vector3.Cross(dir.normalized, Vector3.up) * 0.035f;
            Box(wv, wu, wt, wc, (wireA + wireB) * 0.5f, dir * 0.5f, sideV, 0.05f,
                new Color(0.18f, 0.18f, 0.19f), centredY: true);
            poles++;
        }

        if (rt.Count > 0)
            AddMesh(group, "Tram Rails", Finish("MapleCity_TramRails",
                    rv.ToArray(), ru.ToArray(), rt),
                    CelMaterial("MapleCity_TramRail", new Color(0.72f, 0.72f, 0.74f),
                                gloss: 0.80f, spec: 0.65f, rim: 0.40f),
                    collider: false);
        if (pt.Count > 0)
        {
            var poleMesh = Finish("MapleCity_TramPoles", pv.ToArray(), pu.ToArray(), pt);
            poleMesh.colors = pc.ToArray();
            AddMesh(group, "Tram Poles", poleMesh, SignMaterial(), collider: false);
        }
        if (wt.Count > 0)
        {
            var wire = Finish("MapleCity_TramWire", wv.ToArray(), wu.ToArray(), wt);
            wire.colors = wc.ToArray();
            AddMesh(group, "Tram Wire", wire, SignMaterial(), collider: false);
        }

        Debug.Log($"[maple] tram line: 2 rails on the boulevard, {poles} catenary poles.");
    }

    /// <summary>
    /// Street-level detail: strung paper lanterns, cafe terraces, vending machines and a pocket
    /// shrine with its torii - the design doc's hero prop list, at blockout-plus fidelity.
    ///
    /// All of it sits on the PAVEMENT (kerb height), never in the carriageway, so none of it can
    /// change the racing line the route JSON defines. Everything is combined into a handful of
    /// meshes for the same reason the buildings are.
    /// </summary>
    private static void BuildStreetFurniture(Transform root, CityRoute route)
    {
        var group = new GameObject("City Street Furniture").transform;
        group.SetParent(root, false);

        var bv = new List<Vector3>(); var bu = new List<Vector2>(); var bt = new List<int>();
        var bc = new List<Color>();                       // painted props (vertex tinted)
        var lv = new List<Vector3>(); var lu = new List<Vector2>(); var lt = new List<int>();
        var rng = new System.Random(ScatterSeed + 91);
        int lanterns = 0, tables = 0, machines = 0;

        for (float d = 6f; d < route.Length; d += 4.5f)
        {
            int i = route.IndexAt(d);
            float f = route.Frac(i);
            float half = CarriagewayHalfWidth(f);
            var p = route.Position[i];
            var s = route.SideFlat(i);
            var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            float baseY = RoadY(route, i, half) + KerbHeightM;

            // ---- strung paper lanterns: Old Town lanes and the canal, per the design.
            bool lanternRun = IsCobble(f) || (f > CanalFromFrac && f < CanalToFrac);
            if (lanternRun && d % LanternSpacingM < 4.5f)
            {
                for (int k = -1; k <= 1; k += 2)
                {
                    var o = s * (k * (half + PavementWidthM * 0.5f));
                    var hang = p + o + Vector3.up * (baseY - p.y + 3.6f);
                    // A lantern is a short glowing cylinder; cheap and unmistakable.
                    Trunk(lv, lu, lt, hang, 0.42f, 0.17f, 0.17f);
                    lanterns++;
                }
            }

            // ---- cafe terraces on the boulevard: a parasol slab over two stools.
            if (f > 0.60f && f < 0.95f && rng.NextDouble() < 0.16)
            {
                int k = rng.NextDouble() < 0.5 ? -1 : 1;
                var o = s * (k * (half + PavementWidthM * 0.72f));
                var foot = p + o; foot.y = baseY;
                // table
                Box(bv, bu, bt, bc, foot, t * 0.36f, s * 0.36f, 0.74f,
                    new Color(0.36f, 0.27f, 0.20f));
                // parasol canopy
                Box(bv, bu, bt, bc, foot + Vector3.up * 2.05f, t * 1.15f, s * 1.15f, 0.12f,
                    rng.NextDouble() < 0.5 ? new Color(0.78f, 0.24f, 0.26f)
                                           : new Color(0.93f, 0.89f, 0.80f));
                for (int q = -1; q <= 1; q += 2)
                    Box(bv, bu, bt, bc, foot + t * (q * 0.85f), t * 0.20f, s * 0.20f, 0.46f,
                        new Color(0.30f, 0.24f, 0.20f));
                tables++;
            }

            // ---- vending machines: the design's most characteristically Japanese street prop,
            // and a useful warm light source against a shaded facade.
            if (rng.NextDouble() < 0.07)
            {
                int k = rng.NextDouble() < 0.5 ? -1 : 1;
                var o = s * (k * (half + PavementWidthM * 0.88f));
                var foot = p + o; foot.y = baseY;
                Box(bv, bu, bt, bc, foot, t * 0.55f, s * 0.36f, 1.85f,
                    rng.NextDouble() < 0.5 ? new Color(0.80f, 0.16f, 0.18f)
                                           : new Color(0.16f, 0.42f, 0.75f));
                // lit display front
                var face = foot + s * (k * -0.38f) + Vector3.up * 1.15f;
                Quad(lv, lu, lt, face, t * 0.40f, Vector3.up * 0.48f);
                machines++;
            }
        }

        // ---- pocket shrine + torii, tucked between two Old Town buildings (design doc).
        {
            int i = route.IndexAt(route.Length * 0.40f);
            float half = CarriagewayHalfWidth(route.Frac(i));
            var p = route.Position[i];
            var s = route.SideFlat(i);
            var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            float baseY = RoadY(route, i, half) + KerbHeightM;
            var mouth = p - s * (half + PavementWidthM + 2.0f); mouth.y = baseY;

            var vermilion = new Color(0.847f, 0.235f, 0.180f);
            const float PostR = 0.16f, Span = 1.5f, PostH = 2.9f;
            for (int k = -1; k <= 1; k += 2)
                Box(bv, bu, bt, bc, mouth + t * (k * Span), t * PostR, s * PostR, PostH, vermilion);
            Box(bv, bu, bt, bc, mouth + Vector3.up * PostH, t * (Span + 0.5f), s * 0.20f, 0.20f,
                vermilion);
            Box(bv, bu, bt, bc, mouth + Vector3.up * (PostH + 0.34f), t * (Span + 0.8f),
                s * 0.26f, 0.22f, vermilion);
            // the shrine box itself, behind the torii
            Box(bv, bu, bt, bc, mouth - s * 2.6f, t * 0.9f, s * 0.7f, 1.7f,
                new Color(0.28f, 0.22f, 0.19f));
        }

        // C7 downtown kit, in its own submesh on the plain trim material; the same
        // "City Street Props" object, so Maple Row's carve and the walkers' clutter map see it.
        var kitBuf = new MeshBuf(1);
        string kit = BuildStreetKit(route, kitBuf, lv, lu, lt);
        int grates = BuildTreeGrates(group, route);
        if (bt.Count > 0 || kitBuf.V.Count > 0)
        {
            var props = new MeshBuf(2);
            props.Append(bv, bu, bt, bc, 0);
            props.Append(kitBuf.V, kitBuf.U, kitBuf.T[0], kitBuf.C, 1);
            AddBuf(group, "City Street Props", "MapleCity_StreetProps", props,
                   new[] { FacadeMaterial(), TrimMaterial() });
        }
        if (lt.Count > 0)
            AddMesh(group, "City Lantern Glow",
                    Finish("MapleCity_Lanterns", lv.ToArray(), lu.ToArray(), lt),
                    LanternMaterial(), collider: false);

        Debug.Log($"[maple] street furniture: {lanterns} lanterns, {tables} cafe terraces, " +
                  $"{machines} vending machines, 1 pocket shrine; C7 kit: {kit}, {grates} tree grates.");
    }

    // ------------------------------------------------------------ C7 street furniture

    // PROVISIONAL spacing, judged from chase captures. Offsets are metres FROM THE KERB LINE.
    // Everything sits in the pavement's 0.30-1.40 m sett "furniture strip" and mostly inside
    // 0.15-0.65 m, clear of MapleCityLife's 1.0 m / 1.9 m walk lanes (which nudge round clutter).
    private const float LampEveryM = 32f, BenchEveryM = 58f, RackEveryM = 97f, PlanterEveryM = 71f;
    private const float FurnitureKerbM = 0.42f;
    private const float FurnitureGuardM = 30f;       // stay off Maple Gate Plaza at the lap seam

    private static readonly Color StreetSteel = new Color(0.17f, 0.18f, 0.19f);
    private static readonly Color StreetGreen = new Color(0.13f, 0.22f, 0.19f);

    /// <summary>Kerb-strip station on one side: foot point, along-street tangent, toward-building.</summary>
    private static void KerbStation(CityRoute route, float d, int side, float fromKerb,
                                    out Vector3 foot, out Vector3 t, out Vector3 inward)
    {
        int i = route.IndexAt(d);
        float half = CarriagewayHalfWidth(route.Frac(i));
        var p = route.Position[i];
        var s = route.SideFlat(i);
        t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
        inward = s * side;
        foot = p + s * (side * (half + fromKerb));
        foot.y = RoadY(route, i, side * half) + KerbHeightM;
    }

    private static bool FurnitureAllowed(CityRoute route, float d)
    {
        if (d < FurnitureGuardM || d > route.Length - FurnitureGuardM) return false;
        return d < MapleRowBoutiques.StreetStartM - 20f || d > MapleRowBoutiques.StreetEndM + 20f;
    }

    /// <summary>
    /// C7: the modern-downtown kit along the whole loop (Maple Row 430-720 m has its own):
    /// lamp posts, benches + bins, bike racks, planters, bus shelters, signals and bollards at
    /// every cross-street. Painted steel into <paramref name="nb"/> (plain trim material),
    /// lamp heads into the shared lantern glow.
    /// </summary>
    private static string BuildStreetKit(CityRoute route, MeshBuf nb,
                                         List<Vector3> lv, List<Vector2> lu, List<int> lt)
    {
        var rng = new System.Random(ScatterSeed + 131);
        int lamps = 0, benches = 0, bins = 0, racks = 0, planters = 0, shelters = 0, signals = 0, bollards = 0;
        var stations = SideStreetStations(route);
        bool NearCross(float d, float r)
        {
            foreach (float ss in stations) if (Mathf.Abs(d - ss) < r) return true;
            return false;
        }

        for (int side = -1; side <= 1; side += 2)
        {
            // ---- lamp posts (the Old Town keeps its strung paper lanterns instead)
            for (float d = side < 0 ? 12f : 28f; d < route.Length; d += LampEveryM)
            {
                if (!FurnitureAllowed(route, d) || IsCobble(route.Frac(route.IndexAt(d)))) continue;
                KerbStation(route, d, side, 0.32f, out var foot, out var t, out var inw);
                // Round cast plinth + round shaft (were square boxes: from the chase cam the
                // nearest post read as a black square pillar).
                nb.Cylinder(0, foot, 0.17f, 0.5f, StreetSteel, 12);                   // plinth
                nb.Cylinder(0, foot + Vector3.up * 0.5f, 0.12f, 0.08f, StreetSteel, 12); // collar
                nb.Cylinder(0, foot, 0.065f, 6.6f, StreetSteel, 10);                  // shaft
                var armTip = foot - inw * 1.3f + Vector3.up * 6.5f;
                nb.Box(0, foot - inw * 0.65f + Vector3.up * 6.45f, t * 0.05f, inw * 0.70f, 0.1f, StreetSteel);
                nb.Box(0, armTip + Vector3.up * -0.08f, t * 0.22f, inw * 0.34f, 0.16f, StreetSteel);
                Quad(lv, lu, lt, armTip + Vector3.up * -0.10f, t * 0.18f, inw * 0.28f);
                lamps++;
            }

            // ---- bench facing the shops, with a bin at its end
            for (float d = side < 0 ? 40f : 66f; d < route.Length; d += BenchEveryM * Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble()))
            {
                if (!FurnitureAllowed(route, d) || NearCross(d, 12f)) continue;
                KerbStation(route, d, side, FurnitureKerbM, out var foot, out var t, out var inw);
                var wood = new Color(0.40f, 0.29f, 0.19f);
                for (int k = -1; k <= 1; k += 2)
                    nb.Box(0, foot + t * (k * 0.72f), t * 0.04f, inw * 0.20f, 0.44f, StreetSteel);
                nb.Box(0, foot + Vector3.up * 0.40f, t * 0.85f, inw * 0.21f, 0.06f, wood);                 // seat
                nb.Box(0, foot - inw * 0.19f + Vector3.up * 0.46f, t * 0.85f, inw * 0.03f, 0.42f, wood);   // back
                benches++;
                KerbStation(route, d + 1.5f, side, FurnitureKerbM, out var bf, out var bt2, out var bi);
                nb.Box(0, bf, bt2 * 0.22f, bi * 0.22f, 0.86f, rng.NextDouble() < 0.5 ? StreetGreen : StreetSteel);
                nb.Box(0, bf + Vector3.up * 0.86f, bt2 * 0.25f, bi * 0.25f, 0.06f, StreetSteel);
                bins++;
            }

            // ---- bike racks: three hoops in line with the kerb
            for (float d = side < 0 ? 83f : 120f; d < route.Length; d += RackEveryM * Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble()))
            {
                if (!FurnitureAllowed(route, d) || NearCross(d, 12f)) continue;
                for (int h = 0; h < 3; h++)
                {
                    KerbStation(route, d + h * 1.0f, side, 0.40f, out var foot, out var t, out var inw);
                    for (int k = -1; k <= 1; k += 2)
                        nb.Box(0, foot + t * (k * 0.34f), t * 0.03f, inw * 0.03f, 0.82f, StreetSteel);
                    nb.Box(0, foot + Vector3.up * 0.80f, t * 0.37f, inw * 0.03f, 0.06f, StreetSteel);
                }
                racks++;
            }

            // ---- planters: low concrete trough with clipped shrubs
            for (float d = side < 0 ? 55f : 97f; d < route.Length; d += PlanterEveryM * Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble()))
            {
                if (!FurnitureAllowed(route, d) || NearCross(d, 10f)) continue;
                KerbStation(route, d, side, FurnitureKerbM, out var foot, out var t, out var inw);
                // Stone trough with a proud coping, a soil bed and a row of rounded clipped
                // shrubs (was a flat green box on a grey box). Shrub sizes come from the
                // station, not the shared rng, so the scatter sequence is unchanged.
                // B1: trough 0.50 -> 0.40 m and shrub domes 0.36-0.48 -> 0.28-0.38 m, so the
                // tallest planting tops out ~0.81 m instead of ~1.0 m; smoother 12x4 domes.
                var stone = new Color(0.52f, 0.50f, 0.46f);
                nb.Box(0, foot, t * 0.95f, inw * 0.24f, 0.40f, stone);
                nb.Box(0, foot + Vector3.up * 0.40f, t * 1.00f, inw * 0.29f, 0.07f, new Color(0.60f, 0.58f, 0.53f));
                nb.Box(0, foot + Vector3.up * 0.40f, t * 0.90f, inw * 0.19f, 0.09f, new Color(0.22f, 0.17f, 0.13f));
                for (int k = -2; k <= 2; k++)
                {
                    float h01 = Mathf.Repeat(Mathf.Sin((d + k * 3.1f) * 12.9898f) * 43758.55f, 1f);
                    float r = Mathf.Lerp(0.20f, 0.27f, h01);
                    var sf = foot + t * (k * 0.36f) + Vector3.up * 0.47f;
                    var leaf = Color.Lerp(new Color(0.16f, 0.22f, 0.12f), new Color(0.23f, 0.28f, 0.15f), h01);   // olive, not lime
                    // Rounded clipped shrubs (2026-09-25 claude: stacked drums read as blocky
                    // green octagon slabs in life_sidewalk_1200m).
                    nb.Blob(0, sf - Vector3.up * 0.04f, r * 1.12f, 0.28f + 0.10f * h01, leaf, 12, 4);
                }
                planters++;
            }
        }

        // ---- bus shelters: one per ~1.2 km per side on the modern districts
        for (int side = -1; side <= 1; side += 2)
        for (float d = side < 0 ? 300f : 900f; d < route.Length; d += 1200f)
        {
            if (!FurnitureAllowed(route, d) || NearCross(d, 15f) || IsCobble(route.Frac(route.IndexAt(d)))) continue;
            KerbStation(route, d, side, 0.85f, out var foot, out var t, out var inw);
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
                nb.Box(0, foot + t * (a * 1.9f) + inw * (b * 0.55f), t * 0.05f, inw * 0.05f, 2.5f, StreetSteel);
            // roof in two halves so each piece stays small enough for the walkers' clutter map
            for (int a = -1; a <= 1; a += 2)
                nb.Box(0, foot + t * (a * 1.0f) + Vector3.up * 2.5f, t * 1.02f, inw * 0.72f, 0.12f,
                       new Color(0.25f, 0.27f, 0.29f));
            for (int a = -1; a <= 1; a += 2)   // back screen (building side), smoked glass tone
                nb.Box(0, foot + t * (a * 1.0f) + inw * 0.55f + Vector3.up * 0.3f, t * 0.95f, inw * 0.02f, 1.9f,
                       new Color(0.30f, 0.38f, 0.42f));
            nb.Box(0, foot + inw * 0.30f + Vector3.up * 0.42f, t * 1.3f, inw * 0.18f, 0.06f,
                   new Color(0.40f, 0.29f, 0.19f));
            // lit timetable panel at the upstream end
            Quad(lv, lu, lt, foot + t * (-1.95f) + Vector3.up * 1.4f, inw * 0.45f, Vector3.up * 0.8f);
            shelters++;
        }

        // ---- cross-streets: a signal pole and a bollard row at each corner, both sides
        foreach (float ss in stations)
        for (int side = -1; side <= 1; side += 2)
        for (int c = -1; c <= 1; c += 2)
        {
            float d = ss + c * (SideStreetWidthM * 0.5f + 1.2f);
            if (!FurnitureAllowed(route, d)) continue;
            KerbStation(route, d, side, 0.35f, out var foot, out var t, out var inw);
            nb.Cylinder(0, foot, 0.075f, 3.6f, StreetSteel, 10);               // round signal pole
            var head = foot + Vector3.up * 2.55f - inw * 0.18f;
            nb.Box(0, head, t * 0.17f, inw * 0.12f, 0.95f, new Color(0.12f, 0.12f, 0.13f));
            // one lit lens on the face looking away from the junction (toward approaching riders)
            Quad(lv, lu, lt, head + t * (-c * 0.18f) + Vector3.up * (c < 0 ? 0.25f : 0.70f),
                 inw * 0.09f, Vector3.up * 0.09f);
            signals++;
            for (int b = 1; b <= 4; b++)
            {
                KerbStation(route, d - c * (b * 1.3f), side, 0.30f, out var bf, out var bt2, out var bi);
                nb.Cylinder(0, bf, 0.075f, 0.9f, StreetSteel, 10);                   // round bollard
                nb.Cylinder(0, bf + Vector3.up * 0.72f, 0.08f, 0.06f, new Color(0.62f, 0.52f, 0.20f), 10);
                bollards++;
            }
        }

        return $"{lamps} lamp posts, {benches} benches, {bins} bins, {racks} bike racks, {planters} planters, " +
               $"{shelters} bus shelters, {signals} signal poles, {bollards} bollards";
    }

    /// <summary>Cast-iron tree grates under every street tree outside Maple Row (flat, walkable).</summary>
    private static int BuildTreeGrates(Transform group, CityRoute route)
    {
        var g = new MeshBuf(1);
        int n = 0;
        foreach (var tf in TreeFeet)
        {
            if (!FurnitureAllowed(route, tf.d)) continue;
            var a = Vector3.Cross(Vector3.up, tf.t).normalized * 0.55f;
            var b = tf.t.normalized * 0.55f;
            var c = tf.foot + Vector3.up * 0.012f;
            g.Quad(0, c - a - b, c + a - b, c + a + b, c - a + b,
                   new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                   new Color(0.16f, 0.16f, 0.17f));
            n++;
        }
        if (n > 0) AddBuf(group, "City Tree Grates", "MapleCity_TreeGrates", g, new[] { TrimMaterial() });
        return n;
    }

    /// <summary>
    /// THE SIGNATURE AMBIENT VFX: drifting red-and-gold maple and ginkgo leaves - Maple City's
    /// answer to Sakura Pass's petals.
    ///
    /// Built on the pass's petal system deliberately (same material blend-state dance, same mesh
    /// particle renderer), with one structural difference that the region forces:
    ///
    ///   Sakura's emitter is a FIXED box at the world origin, which is fine for a 1.4 km road.
    ///   Maple City is a 5 km closed loop spanning ~1400 x 1700 m, so a fixed box would either
    ///   have to cover the whole city - an absurd particle count for a handful of visible leaves
    ///   - or leave most of the lap completely bare. The emitter therefore FOLLOWS THE CAMERA
    ///   (see MapleLeafDrift), so a small, cheap box of particles is always exactly where the
    ///   player is looking. That is what keeps the overdraw restrained.
    /// </summary>
    private static void BuildLeafVfx(Transform root, CityRoute route)
    {
        // B4: the street trees are sakura now, so the drift is cherry PETALS (Sakura Pass's own
        // petal sprite, read-only), not the red-and-gold maple leaf atlas.
        var tex = SakuraTexture("SakuraPetal_Sprite.png") as Texture2D;

        var shader = Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply")
                     ?? Shader.Find("Sprites/Default");
        var mat = LoadOrCreate("MapleCity_Leaf_VFX", shader.name);
        mat.SetTexture("_MainTex", tex);
        // _Mode is read ONLY by the inspector ShaderGUI; the blend state has to be pushed
        // explicitly or the leaves render as opaque squares. (Learned on the pass's petals.)
        if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 2f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_BlendOp")) mat.SetFloat("_BlendOp", (float)UnityEngine.Rendering.BlendOp.Add);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);      // leaves tumble, both faces show
        if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f);
        if (mat.HasProperty("_LightingEnabled")) mat.SetFloat("_LightingEnabled", 0f);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetColor("_Color", Color.white);
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);

        var go = new GameObject("Maple Leaf Drift");
        go.transform.SetParent(root, false);
        go.transform.position = route.Position[0] + Vector3.up * 14f;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 12f;
        main.loop = true;
        // Petals are small and light: they fall slowly and flutter.
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // White: the four leaf colours live in the SPRITE ATLAS, so tinting here would flatten
        // the crimson/orange/gold variety the texture was authored to provide.
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, 0.86f, 0.90f, 1f));
        main.gravityModifier = 0.030f;
        // Restrained on purpose - the emitter follows the camera, so these are all near the
        // player and a high count would be pure overdraw.
        main.maxParticles = 520;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 60f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(46f, 12f, 46f);

        // Prevailing wind down the boulevard, so leaves drift with a direction rather than
        // falling straight down like snow.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-1.9f, 0.6f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.9f, -0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(-1.3f, 1.3f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(1.1f);
        noise.frequency = 0.28f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.3f);
        noise.damping = true;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);
        rot.z = new ParticleSystem.MinMaxCurve(-2.6f, 2.6f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(1f, 0.82f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // The petal sprite is a single cell (the old leaf atlas was 2x2), so no sheet animation.
        var tsa = ps.textureSheetAnimation;
        tsa.enabled = false;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = BuildLeafMesh();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = ParticleSystemRenderSpace.World;

        // The follow component. Without it this is a 46 m box of leaves sitting at the start
        // line and the other 4.95 km of the lap has none.
        var follow = go.AddComponent<MapleLeafDrift>();
        follow.height = 13f;
        follow.ahead = 16f;
        EditorUtility.SetDirty(follow);

        Debug.Log("[maple] petal VFX: camera-following sakura petal drift, " +
                  $"{main.maxParticles} max particles.");
    }

    /// <summary>A cupped quad so a leaf catches the light differently as it tumbles.</summary>
    private static Mesh BuildLeafMesh()
    {
        var mesh = new Mesh { name = "MapleCity Leaf" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0.18f, 0.5f), new Vector3(0.5f, 0.18f, 0.5f)
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // =================================================================== primitives

    /// <summary>
    /// A single double-sided quad band between two cross-sections: (a0,b0) at one station and
    /// (a1,b1) at the next. This is the general ribbon primitive the canal walls, water surface
    /// and tram rails are built from - distinct from <see cref="Strip"/>, which is specifically
    /// the ROAD-MARKING helper that tracks the carriageway edge.
    ///
    /// UVs are metre-scale on both axes so a texture on a band never stretches, and the band is
    /// wound both ways because a canal wall is seen from inside the channel as well as out.
    /// </summary>
    private static void Band(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, float distance)
    {
        int b = v.Count;
        float w = Vector3.Distance(a0, b0);
        v.Add(a0); v.Add(b0); v.Add(a1); v.Add(b1);
        uv.Add(new Vector2(0f, distance)); uv.Add(new Vector2(w, distance));
        uv.Add(new Vector2(0f, distance + Vector3.Distance(a0, a1)));
        uv.Add(new Vector2(w, distance + Vector3.Distance(a0, a1)));
        tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
        tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
        tri.Add(b); tri.Add(b + 1); tri.Add(b + 2);
        tri.Add(b + 1); tri.Add(b + 3); tri.Add(b + 2);
    }

    private static void Quad(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 centre, Vector3 right, Vector3 up,
                             List<Color> colors = null, Color tint = default)
    {
        int b = v.Count;
        v.Add(centre - right - up); v.Add(centre + right - up);
        v.Add(centre - right + up); v.Add(centre + right + up);
        uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
        uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
        if (colors != null) for (int k = 0; k < 4; k++) colors.Add(tint);
        tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
        tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
        // Backface: these are single-sided quads in a cutout/cel shader with no two-sided pass,
        // and a canopy billboard seen from behind must not vanish.
        tri.Add(b); tri.Add(b + 1); tri.Add(b + 2);
        tri.Add(b + 1); tri.Add(b + 3); tri.Add(b + 2);
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
        // basis. Callers build the two sides of a street from (tangent, side * sideSign), whose
        // handedness FLIPS with sideSign - so without this, every building on one side of every
        // street would render inside-out (backface-culled into an open shell). Negating `right`
        // fixes the winding and, because these are symmetric half-extent vectors, changes the
        // box's shape not at all.
        if (Vector3.Dot(Vector3.Cross(right, back), Vector3.up) < 0f) right = -right;

        var c = new[]
        {
            lo - right - back, lo + right - back, lo + right + back, lo - right + back,
            hi - right - back, hi + right - back, hi + right + back, hi - right + back,
        };
        // Per-face quads (not a shared-vertex cube) so RecalculateNormals gives hard edges -
        // a smoothed building corner reads as an inflated balloon under a cel ramp.
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

    private static void Trunk(List<Vector3> v, List<Vector2> uv, List<int> tri,
                              Vector3 foot, float height, float rLo, float rHi)
    {
        const int Seg = 6;
        int b = v.Count;
        for (int s = 0; s <= Seg; s++)
        {
            float a = s / (float)Seg * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(foot + dir * rLo);
            v.Add(foot + Vector3.up * height + dir * rHi);
            uv.Add(new Vector2(s / (float)Seg * 2f, 0f));
            uv.Add(new Vector2(s / (float)Seg * 2f, height / 2f));
        }
        for (int s = 0; s < Seg; s++)
        {
            int a = b + s * 2, c = a + 1, d = a + 2, e = a + 3;
            tri.Add(a); tri.Add(c); tri.Add(d);
            tri.Add(c); tri.Add(e); tri.Add(d);
        }
    }

    /// <summary>A tapered 5-sided limb from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static void Limb(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 from, Vector3 to, float rLo, float rHi)
    {
        const int Seg = 5;
        var axis = to - from;
        float len = axis.magnitude;
        if (len < 1e-3f) return;
        axis /= len;
        var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
        var w = Vector3.Cross(u, axis);   // same winding sense as Trunk()
        int b = v.Count;
        for (int s = 0; s <= Seg; s++)
        {
            float a = s / (float)Seg * Mathf.PI * 2f;
            var dir = u * Mathf.Cos(a) + w * Mathf.Sin(a);
            v.Add(from + dir * rLo);
            v.Add(to + dir * rHi);
            uv.Add(new Vector2(s / (float)Seg, 0f));
            uv.Add(new Vector2(s / (float)Seg, len / 2f));
        }
        for (int s = 0; s < Seg; s++)
        {
            int a = b + s * 2, c = a + 1, d = a + 2, e = a + 3;
            tri.Add(a); tri.Add(c); tri.Add(d);
            tri.Add(c); tri.Add(e); tri.Add(d);
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
        if (uv2 != null) mesh.uv2 = uv2;
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
        // Mesh object (which would come back as an empty renderer next editor session).
        string path = $"{MeshDir}/{MeshPrefix}{mesh.name}.asset";
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
    /// The city's shared cel tuning. The shade tint and rim colour are the region's identity as
    /// much as any texture: Sakura shades into mauve, Shiosai into sea-blue, and Maple City
    /// shades into a COOL BLUE ALLEY SHADOW under a WARM GOLD rim - which is precisely the
    /// warm/cool split the design doc's palette section asks for.
    /// </summary>
    /// <summary>
    /// Shadow tint for GREY GROUND PLANES (pavement, kerb, setts, canal coping, terrain).
    ///
    /// Sakura's shade tint is a blue-violet whose green channel sits well below the red/blue
    /// midpoint. On green grass that is invisible; on a grey city floor it is the mauve. This
    /// variant keeps the same coolness and value but puts green back level, so shade reads
    /// blue-teal instead of lilac. PROVISIONAL, like every colour here.
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
        mat.SetFloat("_ShadeStrength", 0.62f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.07f);
        mat.SetColor("_RimColor", new Color(1f, 0.78f, 0.52f, 1f));
        mat.SetFloat("_RimStrength", rim);
        mat.SetFloat("_Gloss", gloss);
        mat.SetFloat("_SpecStrength", spec);
        // Long building shadows striping the road is the design's headline lighting note, so the
        // shadow step has to stay READABLE: keep less ambient in shadow than the pass does.
        mat.SetFloat("_ShadowAmbient", 0.34f);
        mat.SetFloat("_ShadowSoft", 0.14f);
        if (texture != null) mat.SetTexture("_MainTex", texture);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    // 2026-09-25 22:30 (claude): the city's OWN asphalt (tools/blender/maple_city_asphalt.py),
    // replacing the borrowed Shiosai map. That map was dark (~0.165) and blue-biased, needed a
    // 1.7x tint, repeated its grunge blotches every 4 m, and the sunlit road read lavender under
    // the blue sky ambient (life_chase_4300m averaged RGB 85/79/109). The new map is warm-neutral
    // at ~0.26 with fine aggregate and wheel-track wear, so the tint is ~1. Sky ambient is cut
    // on the road surfaces (RoadAmbient) because a flat up-facing plane takes the full blue SH
    // term; the dapple noise is reduced so shadowed stretches stop looking stained.
    private static Material AsphaltMaterial() =>
        // MATTE (user, 2026-10-04: "it looks like im riding on glass"): no specular lobe, no rim, minimal gloss on every road surface.
        RoadSurface(CelMaterial("MapleCity_Asphalt", new Color(1.04f, 1.03f, 1.02f), gloss: 0.04f,
                    spec: 0.0f, rim: 0.0f, texture: CityTexture("MapleCity_Asphalt_Albedo.png"),
                    shade: RoadShade));

    private const float RoadAmbient = 0.55f;

    private static Material RoadSurface(Material mat)
    {
        mat.SetFloat("_AmbientStrength", RoadAmbient);
        // More of that (reduced) sky fill survives in shadow than on other city materials, so
        // building shadows read as cool shade instead of the golden sun x shade tint = brown.
        mat.SetFloat("_ShadowAmbient", 0.75f);
        // Shade band lighter than the city default (0.62): a dark flat road in shadow went to a
        // featureless navy/black slab under the cool grade. Walls keep the stronger split.
        mat.SetFloat("_ShadeStrength", 0.80f);
        mat.SetFloat("_DappleStrength", 0.12f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 2026-09-25 (claude-cowork): the road's shadow tint. CelMaterial's default shade is
    /// Sakura's blue-VIOLET (0.44, 0.50, 0.68) - the same thing CityGroundMaterial documents as
    /// the source of its mauve floor. Asphalt is grey, so in every building shadow it went
    /// purple (worst at ~2500 m, a long shaded stretch). A cool-neutral shade keeps shadow
    /// reading as shadow without the violet; the albedo tint loses its blue bias for the same
    /// reason. Also used by the markings and cycle lane so the paint shades like the road.
    /// </summary>
    private static readonly Color RoadShade = new Color(0.50f, 0.54f, 0.59f, 1f);   // green >= red: cool, never violet

    /// <summary>
    /// The Old Town's cobbled setts - the region's OWN purpose-built granite paving, generated
    /// by tools/blender/maple_city_setts.py.
    ///
    /// This originally borrowed Sakura_Rock_Albedo.png, on the theory that a rock albedo at a
    /// small tile would read as stones underwheel. It did not: that map is a MOSSY CLIFF face,
    /// and the render came back with the historic quarter's high street paved in green moss and
    /// dirt. A city's oldest street is a specific, man-made surface and it needed a real one.
    ///
    /// Sits cooler and darker than the boulevard asphalt so the surface change is obvious from a
    /// hundred metres out - which is the point: the player should see the climb coming.
    /// </summary>
    private static Material SettsMaterial() =>
        // 2026-09-25 (claude): neutral tint + RoadShade. The old green-grey tint and the
        // blue-teal GroundShade made the Old Town read as cold blue brick; the regenerated
        // albedo carries its own warm/cool per-sett hues and baked dome relief.
        RoadSurface(CelMaterial("MapleCity_Setts", new Color(0.96f, 0.96f, 0.95f), gloss: 0.04f,
                    spec: 0.0f, rim: 0.0f, texture: CityTexture("MapleCity_Setts_Albedo.png"),
                    shade: RoadShade));

    /// <summary>
    /// Facades. Uses the city's OWN facade shader rather than the coast's, for one specific
    /// reason: a building wall wants continuous lighting (a cel ramp collapses a flat wall to
    /// one solid colour and the skyline reads as cardboard), but the coast's architecture
    /// shader has no COLOR input - and vertex colour is the only channel left to carry
    /// per-building tint once several hundred buildings are baked into one combined mesh.
    /// See Assets/Environment/MapleCity/Shaders/MapleCityFacade.shader.
    /// </summary>
    private static Material FacadeMaterial()
    {
        const string name = "MapleCity_Facade";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FacadeShaderName);
        mat.SetColor("_Color", Color.white);
        // The city's OWN facade map, not the coast's harbour planks.
        //
        // The blockout borrowed Shiosai_HarbourFacade_Albedo_v2.png, and the render showed
        // exactly why that failed: it is a strongly-patterned CREAM map, so multiplying it by a
        // cream district tint gave cream and multiplying it by dark-stained timber gave slightly
        // darker cream. The texture was dominating and all three districts collapsed into one.
        // MapleCity_Facade_Albedo is deliberately NEUTRAL MID-GREY and carries only STRUCTURE
        // (floor bands, window bays, ground-floor shopfront), so the per-building vertex tint is
        // what the eye actually reads. Authored by tools/blender/maple_city_surfaces.py.
        mat.SetTexture("_MainTex", CityTexture("MapleCity_Facade_Albedo.png"));
        mat.SetTexture("_BumpMap", CityTexture("MapleCity_Facade_Normal.png"));
        mat.SetFloat("_Glossiness", 0.16f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_BumpStrength", 0.55f);
        mat.SetFloat("_Occlusion", 0.90f);
        mat.SetFloat("_VertexTint", 1f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Lit windows. UNLIT on purpose - see <see cref="Windows"/>. The colour is deliberately
    /// pushed past 1.0 on red and green so it clears the city grade's 1.42 bloom threshold and
    /// blooms as a warm tungsten point rather than sitting there as a beige rectangle.
    /// </summary>
    private static Material WindowMaterial()
    {
        const string name = "MapleCity_WindowGlow";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, "Unlit/Color");
        // Just over the city grade's 1.42 bloom threshold, not far over it: the first pass sat at
        // 1.95 and every window in the district bloomed into a solid white rectangle.
        mat.SetColor("_Color", new Color(1.62f, 1.28f, 0.74f, 1f));
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Street-tree canopy, one material per blossom tint (B4: sakura, was autumn maple). Cutout
    /// foliage over Sakura Pass's blossom atlas. The tint rides on <c>_Color</c> because
    /// MapleRide/SakuraFoliage does not read vertex colour - see <see cref="BuildStreetTrees"/>.
    /// </summary>
    private static Material LeafMaterial(string variant, Color tint)
    {
        string name = $"MapleCity_Leaf_{variant}";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FoliageShaderName);
        mat.SetColor("_Color", tint);
        mat.SetTexture("_MainTex", SakuraTexture("Sakura_Blossom_Atlas.png"));
        mat.SetFloat("_Cutoff", 0.36f);
        mat.SetColor("_ShadeColor", new Color(0.58f, 0.52f, 0.76f, 1f));
        mat.SetFloat("_ShadeStrength", 0.50f);
        // Backlight and rim are derived from the blossom's OWN tint so they reinforce it rather
        // than overwrite it (the old fixed-orange translucency made every canopy the same gold).
        // Both stay under 1.0: near-white petals times a >1 backlight bloomed past the city
        // grade's 1.42 threshold into white blobs.
        mat.SetFloat("_Translucency", 0.70f);
        mat.SetColor("_TransColor", tint * 0.85f);
        mat.SetColor("_RimColor", Color.Lerp(tint, Color.white, 0.25f));
        mat.SetFloat("_RimStrength", 0.26f);
        mat.SetFloat("_WindStrength", 0.10f);
        mat.SetFloat("_WindSpeed", 1.3f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>Fallen sakura petals lying on the pavement (no wind, no backlight).</summary>
    private static Material PetalLitterMaterial()
    {
        const string name = "MapleCity_PetalLitter";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FoliageShaderName);
        mat.SetColor("_Color", new Color(0.95f, 0.80f, 0.96f, 1f));
        mat.SetTexture("_MainTex", SakuraTexture("Sakura_Blossom_Atlas.png"));
        mat.SetFloat("_Cutoff", 0.45f);
        mat.SetColor("_ShadeColor", new Color(0.58f, 0.52f, 0.76f, 1f));
        mat.SetFloat("_ShadeStrength", 0.45f);
        mat.SetFloat("_Translucency", 0f);
        mat.SetFloat("_RimStrength", 0f);
        mat.SetFloat("_WindStrength", 0f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// City ground. The shared triplanar terrain shader, with its three slots repurposed:
    /// "grass" stays grass for the back-lots and park strips, "scree" is retinted to warm PAVING
    /// STONE and carries the paved street level, and "rock" fills the terrace cuts. Slope
    /// thresholds are pushed high because a city floor is flat almost everywhere and the auto
    /// rock blend would otherwise paint the whole thing as cliff.
    /// </summary>
    private static readonly Color CityFloorShade = new Color(0.36f, 0.52f, 0.46f, 1f);

    private static Material CityGroundMaterial()
    {
        const string name = "MapleCity_Ground";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.SetTexture("_GrassTex", SakuraTexture("Sakura_Grass_Albedo.png"));
        mat.SetTexture("_GrassNormal", SakuraTexture("Sakura_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", SakuraTexture("Sakura_Grass_Rough.png"));
        mat.SetTexture("_RockTex", SakuraTexture("Sakura_Rock_Albedo.png"));
        mat.SetTexture("_RockNormal", SakuraTexture("Sakura_Rock_Normal.png"));
        mat.SetTexture("_RockRough", SakuraTexture("Sakura_Rock_Rough.png"));
        mat.SetTexture("_ScreeTex", CityTexture("MapleCity_Pavement_Albedo.png"));
        mat.SetTexture("_ScreeNormal", CityTexture("MapleCity_Pavement_Normal.png"));
        mat.SetTexture("_ScreeRough", SakuraTexture("Sakura_Asphalt_Rough.png"));
        mat.SetColor("_GrassColor", new Color(0.310f, 0.373f, 0.235f, 1f));
        mat.SetColor("_RockColor", new Color(0.452f, 0.462f, 0.478f, 1f));
        // COOL NEUTRAL, deliberately. The first fidelity pass used a warm 0.545/0.529/0.518
        // "paving stone" here, and under the golden-hour key with the cel shader's blue shade
        // tint it multiplied out to a flat MAUVE that covered every street-level shot. A city
        // floor has to sit slightly COOLER than neutral so the warm sun is the only warmth in
        // frame; anything warm in the albedo compounds with the grade and goes pink.
        // SHADOW TINT - the actual source of the mauve floor.
        //
        // Sakura's cel shade tint is a blue-VIOLET (0.44, 0.50, 0.68): its green sits ~0.06 below
        // the red/blue midpoint. On Sakura that is invisible, because its ground is green grass
        // whose own albedo fills the green back in. A city floor is grey, so nothing masks it -
        // the violet shade multiplies against the golden-hour key and every paved surface in
        // frame lands on dusty rose. Measured from the render: R 0.75 / G 0.62 / B 0.55.
        //
        // The fix is a shadow that is COOL without being violet: green raised level with the
        // red/blue midpoint, so shade reads blue-teal and the only warmth in frame is the sun.
        // VALUE: this is the city FLOOR, the largest surface in almost every street shot, and at
        // 0.398 it blew out to luminance 0.71 in direct sun (measured off the Old Town ramps) -
        // chalk-white, and bright enough that the warm key's red bias read as rose. Scaled to
        // real dry-paving values with the same green lift the footpaths get.
        // 2026-09-25 (claude): MEASURED again off life_sidewalk_2000m: sunlit city-floor tiles
        // rendered (173, 153, 187) = lavender (orange key + blue sky fill on a cool grey). Target
        // is a warm stone grey ~(160, 155, 145), so blue -22 %, red -8 %, and the shade moves from
        // violet-blue toward neutral (CityFloorShade).
        // Pass 2: (0.296, 0.376, 0.270) only moved the render to (167, 150, 176) - the region grade
        // (saturation 1.26 + tinted lift) amplifies any green deficit, so green now leads clearly.
        mat.SetColor("_ScreeColor", new Color(0.285f, 0.445f, 0.225f, 1f));
        mat.SetFloat("_GrassScale", 5.0f);
        mat.SetFloat("_RockScale", 9.0f);
        mat.SetFloat("_ScreeScale", 3.0f);
        mat.SetFloat("_SlopeRockStart", 42f);
        mat.SetFloat("_SlopeRockEnd", 62f);
        mat.SetFloat("_MacroVariation", 0.30f);
        mat.SetFloat("_NormalStrength", 0.85f);
        mat.SetColor("_ShadeColor", CityFloorShade);
        mat.SetFloat("_ShadeStrength", 0.62f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.07f);
        mat.SetColor("_RimColor", new Color(1f, 0.78f, 0.52f, 1f));
        mat.SetFloat("_RimStrength", 0.04f);
        mat.SetFloat("_SpecStrength", 0.08f);
        mat.SetVector("_HeightRange", new Vector4(2000f, 3000f, 0f, 0f));  // no snow tint downtown
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    private static Texture SakuraTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture>($"{SakuraTextureDir}/{file}");

    private static Texture CoastTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture>($"{CoastTextureDir}/{file}");

    /// <summary>Maple City's own textures, authored by tools/blender/maple_city_setts.py.</summary>
    private static Texture CityTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture>(
            $"Assets/Environment/MapleCity/Textures/{file}");

    // ------------------------------------------------------------ C7 street-wall materials

    /// <summary>C7 street textures, authored by tools/blender/build_maple_city_street_textures.py.</summary>
    private static Texture StreetTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture>($"{StreetTextureDir}/{file}");

    /// <summary>
    /// One cladding: brick, limestone, dark metal panel, glass-and-steel curtain wall, plaster,
    /// stained timber. Each map is an already-coloured 4-bay x 4-storey tile (painted recessed
    /// windows + frames + mullions) with a matching normal map. The per-building vertex tint is
    /// never above 1, and every map averages well under 0.75, so nothing chalks out.
    /// _Cull 0: the street wall is double-sided by construction (the shader flips back faces).
    /// </summary>
    private static Material StreetFacadeMaterial(Clad k)
    {
        string name = "MapleCity_Street_" + CladNames[(int)k];
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FacadeShaderName);
        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex", StreetTexture($"MapleStreet_Facade_{CladNames[(int)k]}_Albedo.png"));
        mat.SetTexture("_BumpMap", StreetTexture($"MapleStreet_Facade_{CladNames[(int)k]}_Normal.png"));
        bool shiny = k == Clad.Glass || k == Clad.Metal;
        mat.SetFloat("_Glossiness", shiny ? 0.42f : 0.14f);
        mat.SetFloat("_Metallic", k == Clad.Metal ? 0.20f : 0f);
        mat.SetFloat("_BumpStrength", 0.60f);
        mat.SetFloat("_Occlusion", 0.90f);
        mat.SetFloat("_VertexTint", 1f);
        mat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>Cornices, pilasters, piers, parapets, roof slabs: plain vertex-tinted trim.</summary>
    private static Material TrimMaterial()
    {
        const string name = "MapleCity_Street_Trim";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FacadeShaderName);
        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex", StreetTexture("MapleStreet_Plain.png"));
        mat.SetTexture("_BumpMap", null);
        mat.SetFloat("_BumpStrength", 0f);
        mat.SetFloat("_Glossiness", 0.20f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Occlusion", 1f);
        mat.SetFloat("_VertexTint", 1f);
        mat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>Fascia signs and striped awnings (lit, not glowing - they are paint, not light).</summary>
    private static Material ShopAtlasMaterial()
    {
        const string name = "MapleCity_Street_ShopAtlas";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FacadeShaderName);
        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex", StreetTexture("MapleStreet_ShopAtlas.png"));
        mat.SetTexture("_BumpMap", null);
        mat.SetFloat("_BumpStrength", 0f);
        mat.SetFloat("_Glossiness", 0.18f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Occlusion", 1f);
        mat.SetFloat("_VertexTint", 1f);
        mat.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Shop interiors seen through the glazing: HDRP/Unlit so a warm-lit interior reads through
    /// the glass in shade as well as sun (the same pattern the Maple Row windows use). The atlas
    /// itself is authored dim-to-warm with a sky reflection band, so ShopGlowK sits at ~1 and
    /// only the brightest lamp pixels reach the bloom threshold.
    /// </summary>
    private static Material ShopGlazingMaterial()
    {
        const string name = "MapleCity_Street_ShopGlazing";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var tex = StreetTexture("MapleStreet_ShopGlazing.png");
        var hd = Shader.Find("HDRP/Unlit");
        var mat = LoadOrCreate(name, hd != null ? "HDRP/Unlit" : "Unlit/Texture");
        var k = new Color(ShopGlowK, ShopGlowK, ShopGlowK, 1f);
        if (hd != null)
        {
            mat.SetTexture("_UnlitColorMap", tex);
            mat.SetColor("_UnlitColor", k);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
        }
        else
        {
            mat.SetTexture("_MainTex", tex);
            mat.SetColor("_Color", k);
        }
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>Street pavers (running bond of 60 x 30 cm slabs) and granite kerb stone.</summary>
    private static Material StreetPavementMaterial() =>
        // Warm concrete with a NEUTRAL shade. The old cool grey-teal (0.45, 0.53, 0.53) under the
        // blue GroundShade read LAVENDER in the pink golden-hour sun (2026-09-25 review); even a
        // warm albedo stayed lilac, so the albedo leans yellow to cancel the sun's magenta.
        CelMaterial("MapleCity_StreetPavement", new Color(0.62f, 0.55f, 0.42f), gloss: 0.20f,
                    spec: 0.10f, rim: 0.10f, texture: StreetTexture("MapleStreet_Pavement.png"),
                    shade: PavementShade);

    private static readonly Color PavementShade = new Color(0.47f, 0.47f, 0.53f, 1f);

    private static Material StreetKerbMaterial() =>
        CelMaterial("MapleCity_StreetKerb", new Color(0.58f, 0.54f, 0.45f), gloss: 0.24f,
                    spec: 0.12f, rim: 0.12f, texture: StreetTexture("MapleStreet_Kerb.png"),
                    shade: PavementShade);

    /// <summary>
    /// Rooftop sign FRAMES. Dark painted steel, vertex-tinted, continuously lit rather than cel
    /// ramped so a thin leg does not flip between two flat bands as it turns.
    /// </summary>
    private static Material SignMaterial()
    {
        const string name = "MapleCity_SignFrame";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, FacadeShaderName);
        mat.SetColor("_Color", Color.white);
        mat.SetFloat("_Glossiness", 0.34f);
        mat.SetFloat("_Metallic", 0.25f);
        mat.SetFloat("_Occlusion", 1f);
        mat.SetFloat("_VertexTint", 1f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Canal / river water. Flat, teal-grey (#7fb7cf per the design palette), glossy enough to
    /// catch the low sun. Deliberately NOT the coast's animated sea shader: this is still water
    /// in a stone channel, and a rolling swell in a city canal would read as a mistake.
    /// </summary>
    private static Material WaterMaterial() =>
        CelMaterial("MapleCity_Water", new Color(0.498f, 0.718f, 0.812f), gloss: 0.86f,
                    spec: 0.60f, rim: 0.45f);

    /// <summary>Paper-lantern glow. Same above-threshold unlit trick as the windows.</summary>
    private static Material LanternMaterial()
    {
        const string name = "MapleCity_LanternGlow";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var mat = LoadOrCreate(name, "Unlit/Color");
        // Warmer and a touch hotter than the windows so a lantern run reads as its own light.
        mat.SetColor("_Color", new Color(2.10f, 1.32f, 0.62f, 1f));
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// The city's own skybox: warm autumn GOLDEN HOUR drifting toward blue dusk, with a broad
    /// low sun lobe and high thin cirrus (design doc section 3).
    ///
    /// It has to be its own material, not the pass's, for the reason the coast pass documents:
    /// the three regions share one scene and one skybox slot, so anything the city does not own
    /// it inherits - and inheriting Sakura's sunset would leave Maple City reading as "the pass
    /// at dusk with buildings", which is exactly what an urban region must not look like. The
    /// difference from Sakura is deliberate and specific: a deeper, bluer zenith (dusk is
    /// arriving overhead), a warmer and much brighter horizon band (city haze lit from behind),
    /// and thin high cirrus instead of the pass's soft warm cumulus.
    /// </summary>
    private static Material BuildCitySky()
    {
        var sky = LoadOrCreate("MapleCity_Sky", "MapleRide/HDRP/Sky");
        // VAPORWAVE NEO-TOKYO NIGHT (2026-10-03): indigo zenith -> magenta-violet mid -> hot-pink horizon, a big pale-lavender
        // moon in place of the sun, neon-pink cloud bands. Warm golden-hour values are in git-less history: see RegionDirector
        // MapleCityAmbience (rollback flag RegionDirector.MapleCityVaporwave).
        sky.SetColor("_ZenithColor", new Color(0.035f, 0.030f, 0.170f, 1f));
        sky.SetColor("_MidColor", new Color(0.330f, 0.130f, 0.520f, 1f));
        sky.SetColor("_HorizonColor", new Color(1.000f, 0.380f, 0.700f, 1f));
        sky.SetColor("_GroundColor", new Color(0.090f, 0.060f, 0.190f, 1f));
        sky.SetFloat("_HorizonSharp", 2.2f);
        sky.SetFloat("_MidPoint", 0.30f);
        sky.SetColor("_SunColor", new Color(0.88f, 0.82f, 1.0f, 1f));            // the moon
        sky.SetFloat("_SunSize", 0.095f);
        sky.SetFloat("_SunSoftness", 0.020f);
        sky.SetFloat("_SunGlow", 1.35f);
        sky.SetFloat("_SunGlowPower", 22f);
        sky.SetColor("_CloudColor", new Color(0.98f, 0.46f, 0.86f, 1f));
        sky.SetFloat("_CloudStrength", 0.52f);
        sky.SetFloat("_CloudScale", 4.6f);
        sky.SetFloat("_CloudHeight", 0.30f);
        sky.SetFloat("_CloudSpread", 0.26f);
        sky.SetFloat("_Exposure", 1.0f);
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

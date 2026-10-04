using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// SHIOSAI COAST - playable mock of the scenic ocean route ("Ride the Breeze").
///
/// Scope, deliberately: this is the reviewable *mock* pass asked for by the World Map
/// milestone - a rideable coastal road, an ocean, a coast-reading horizon and a few coastal
/// props, all correct and grounded, none of it a finished art pass.
///
/// DEVIATION FROM THE SAKURA PIPELINE, ON PURPOSE
/// ----------------------------------------------
/// Sakura Pass geometry is authored in Blender and staged from GLB, and that remains the rule
/// for anything that gets an art pass. The coast mock is generated here in C# instead, swept
/// directly along the published centreline, because:
///   * the whole mock is ribbon geometry measured off ShiosaiRoute.json - authoring it in
///     Blender would mean re-publishing the same centreline into a second tool for no gain,
///   * it keeps the milestone to one reviewable slice with no new GLB inventory, and
///   * it cannot desynchronise from the route the rider actually rides: the road, the land it
///     sits on, the guardrail and every prop are all placed from the same samples.
/// When Shiosai Coast gets its real art pass, the builders move to tools/blender/ and this file
/// shrinks to a staging pass, exactly like SakuraPassEnvironment.
///
/// The route itself IS published through the pipeline: tools/blender/shiosai_route.py ->
/// Assets/Environment/ShiosaiCoast/ShiosaiRoute.json -> RouteGraphBaker -> RouteGraph.
/// </summary>
public static partial class ShiosaiCoastEnvironment
{
    /// <summary>
    /// Scene the coast builds into: the SHARED, PLAYABLE SakuraPass scene.
    ///
    /// DO NOT restore the old `File.Exists(ShiosaiSceneBuilder.ShiosaiScenePath) ? ... : ...`
    /// auto-select. That silently redirected BOTH this pass and ShiosaiCoastDiagnostics into
    /// Assets/Scenes/SC_Persistent.unity the moment that file existed - and nothing in
    /// Assets/Ride/ ever loads SC_Persistent (grep: zero references). MapleRideSceneBootstrap
    /// plays SakuraPass.unity, and RegionDirector just shows/hides the per-region roots inside
    /// it, which is why every other region's pass (Azora, Taka, Maple City, Coral NPC, ...)
    /// hard-codes this same path.
    ///
    /// The cost of that one ternary was an entire milestone's art - rock textures, ocean
    /// gradient, 34k hydrangea scatter, sea stacks, sky volume - built and "verified by render"
    /// against a scene the game never loads, while the player saw no change whatsoever.
    /// SC_Persistent is retired to reference/retired/. If it is ever re-derived, it must NOT
    /// become the build target again.
    /// </summary>
    public static string ScenePath = DefaultScenePath;

    public const string DefaultScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string RoutePath = "Assets/Environment/ShiosaiCoast/ShiosaiRoute.json";
    private const string RegionDir = "Assets/Environment/ShiosaiCoast";
    private const string MaterialDir = "Assets/Environment/ShiosaiCoast/Materials";
    private const string MeshDir = "Assets/Environment/ShiosaiCoast/Meshes";
    private const string SakuraTextureDir = "Assets/Environment/SakuraPass/Textures";
    private const string CoastTextureDir = "Assets/Environment/ShiosaiCoast/Textures";

    private const string RootName = "Shiosai Coast Environment";
    private const string CelShaderName = "MapleRide/SakuraCel";
    private const string ArchitectureShaderName = "MapleRide/ShiosaiArchitecture";
    private const string WaterShaderName = "MapleRide/SakuraWater";
    private const string TerrainShaderName = "MapleRide/SakuraTerrain";
    /// <summary>
    /// Background-range shader. HDRP-only by design (see RidgeMaterial): it exists to replace
    /// the flat CelLit silhouettes that read as cardboard cones at the start line, and there is
    /// no Built-in surface that reproduces its self-computed aerial perspective.
    /// </summary>
    private const string RidgeShaderName = "MapleRide/HDRP/RidgeHaze";

    /// <summary>
    /// The colour a range dissolves into. PROVISIONAL: matched to Shiosai_Sky's _HorizonColor
    /// (0.80, 0.92, 0.98) pulled very slightly down and blue, so a fully hazed ridge foot sits
    /// just under the sky it is silhouetted against instead of glowing out of it.
    /// </summary>
    private static readonly Color RidgeHazeColour = new Color(0.71f, 0.83f, 0.93f, 1f);

    // --------------------------------------------------------------- provisional tuning
    // Every number here is PROVISIONAL mock tuning. Route metrics are an unresolved design
    // decision (context 12); these exist so the coast is rideable and reviewable, not final.

    private const float SeaLevelY = 0f;
    private const float RoadHalfWidth = 3.5f;      // shared 7 m carriageway
    private const float ShoulderWidth = 0.55f;
    /// <summary>
    /// Parabolic carriageway crown, metres. Matched to Sakura Pass's authored 6 cm crown (see
    /// ASSET_CATALOG.md: the riders' GroundOffset of 0.065 m exists to clear exactly that).
    /// The coast mock originally swept a 12 cm crown, which alone put the asphalt 10 cm above
    /// the line the rider is mathematically placed on.
    /// </summary>
    private const float RoadCrown = 0.06f;

    /// <summary>Metres the terrain's road-corridor nodes sit below the published centreline.</summary>
    private const float VergeDropM = 0.05f;

    /// <summary>
    /// The lane line the player and the ambient traffic both ride, in metres from the centreline.
    /// MUST match <c>RouteFollower.laneOffset</c> and <c>ShiosaiTrafficDirector.laneOffsetM</c>.
    /// </summary>
    private const float LaneLineOffsetM = 1.72f;

    /// <summary>Metres the rider transform is lifted above the centreline
    /// (<c>RouteFollower.heightOffset</c>). The bike is authored with its wheels at y = 0.</summary>
    private const float RiderLiftM = 0.02f;

    /// <summary>Crown height at a given offset across the carriageway.</summary>
    private static float CrownAt(float offset) =>
        RoadCrown * (1f - Mathf.Pow(Mathf.Abs(offset) / (RoadHalfWidth + ShoulderWidth), 2f));

    /// <summary>
    /// Vertical bias applied to the swept asphalt, SOLVED rather than guessed, so that the
    /// carriageway surface at the lane line lands exactly on the height the rider is placed at.
    ///
    /// The mock originally used a flat +0.06 m bias on top of a 12 cm crown, which put the
    /// asphalt roughly 9 cm ABOVE the rider's own placement line - i.e. the player (and every
    /// ambient rider, placed by the same arithmetic) rode with more than half the wheel buried
    /// in the road surface. Nothing raycasts here, so no amount of "grounding" could correct it;
    /// the two authorities simply had to be made to agree.
    /// </summary>
    private static readonly float RoadSurfaceLiftM =
        RiderLiftM + VergeDropM - CrownAt(LaneLineOffsetM);

    /// <summary>Paint sits this far proud of the asphalt, so it never z-fights.</summary>
    private const float MarkingLiftM = 0.015f;

    /// <summary>
    /// The checkered start/finish band sits just ABOVE the lane paint (MarkingLiftM), so at
    /// arc 0 the start line draws over the edge lines and cycle-lane paint that used to make the
    /// blunt road end read as an "out of bounds" strip, rather than z-fighting with them.
    /// </summary>
    private const float StartLineLiftM = 0.024f;

    private const float GuardrailOffset = -5.2f;   // seaward verge (rider's left heading north)

    // --- road surface / paint (all PROVISIONAL, read off the concept renders) ---------------
    /// <summary>Metres per tile of the coast asphalt map, on BOTH axes (isotropic grain).</summary>
    private const float AsphaltTileM = 4f;
    /// <summary>Centre of the blue cycle lane, measured in from the carriageway edge.</summary>
    private const float CycleLaneInsetM = 0.75f;
    /// <summary>HALF width of the blue cycle lane (Strip() takes a half width).</summary>
    private const float CycleLaneWidthM = 0.22f;
    

    private const float TerrainStride = 2f;        // use every Nth route sample for the land mesh
    private const int OceanGrid = 64;
    /// <summary>How far the sea is carried past the route's own bounding box, so the horizon is
    /// still water from any clifftop in the region. PROVISIONAL.
    ///
    /// THE GOLD HORIZON BAND WAS THIS NUMBER. At 9000 m the sheet's seaward edge stood roughly
    /// 14 km from a coastal camera, but the TRUE geometric horizon from a 60 m clifftop is
    /// sqrt(2*R*h) ~= 28 km. The sea therefore ran out well SHORT of the skyline and left a
    /// wedge of bare below-horizon sky between the water's edge and the true horizon - which
    /// the sun-lit cloud layer painted a solid warm tan, reading as a gold bar across the whole
    /// horizon in diag_shiosai_ch8_highway.png. It was never the glitter term and never the far
    /// clip plane: raising the camera's far plane to 60 km changed the render by nothing,
    /// because the geometry itself stopped there.
    ///
    /// 40 km clears that 28 km horizon with margin from any clifftop in the region. It costs
    /// nothing - the sheet is a fixed OceanGrid x OceanGrid mesh at any size, and ripple detail
    /// has already faded out by _DetailFadeEnd (3.2 km) long before the new edge.
    /// </summary>
    private const float OceanHorizonMarginM = 40000f;

    // ------------------------------------------------ PHOTOREAL SEA (HDRP Water System)
    //
    // MILESTONE: replace the cartoon-blue custom-shader sea plane with HDRP 17.4's Water
    // System (an Ocean surface, Infinite geometry, at SeaLevelY).
    //
    // WHY THE OLD SEA COULD NOT GET THERE BY TUNING. MapleRide/SakuraWater is a Built-in-era
    // surface shader bridged into HDRP: it has no depth-driven absorption, no screen-space
    // refraction, no real sky reflection and no simulation. Its "depth gradient" was a
    // VIEW-ANGLE proxy and its turquoise was a second translucent mesh (Coast Shallows)
    // painting a fixed band along the waterline. The target plate's sea is a *physical*
    // result - deep blue where the water column is deep, turquoise where the sea floor rises
    // into it, sun glint from a simulated surface, and sky in the reflection. All four are
    // built into the HDRP water surface and none of them can be faked by the old shader.
    //
    // The legacy mesh + the shallows sheet are therefore retired together (UseHdrpWaterSystem),
    // not layered on top - two water surfaces at y = 0 would z-fight and double-shade.
    // Set UseHdrpWaterSystem = false to fall back to the old sheet in one edit.
    private const bool UseHdrpWaterSystem = true;

    /// <summary>Exact name of the HDRP water surface object. Matched exactly, never Contains.</summary>
    private const string OceanWaterName = "Shiosai Ocean Water";
    /// <summary>Exact name of the retired custom-shader sea sheet.</summary>
    private const string LegacyOceanMeshName = "Shiosai Ocean";
    /// <summary>Exact name of the retired turquoise shelf sheet.</summary>
    private const string LegacyShallowsName = "Coast Shallows";

    // --- water look tunables. ALL PROVISIONAL (illustrative tuning, not design requirement) ---

    /// <summary>Distant ("swell") wind speed, km/h. The Ocean preset ships 30 = open-ocean
    /// weather; the brief is a CALM COASTAL sea, so this is pulled well down. PROVISIONAL.</summary>
    private const float OceanWindSpeedKmh = 17f;
    /// <summary>Swell directionality. 1 = fully chaotic, 0 = one clean direction. PROVISIONAL.</summary>
    private const float OceanChaos = 0.72f;
    /// <summary>Swell patch repetition, metres. Smaller = shorter wavelength. PROVISIONAL.</summary>
    private const float OceanRepetitionSizeM = 360f;
    /// <summary>Surface-ripple wind speed, km/h - the micro chop that carries the sun glint.
    /// PROVISIONAL.</summary>
    private const float OceanRipplesWindKmh = 6.5f;

    /// <summary>
    /// Metres of water the light survives before it is fully absorbed. THIS is the knob that
    /// makes the turquoise shallows band: where the sea floor is closer to the surface than
    /// this, the sand shows through and the water reads turquoise; past it the column resolves
    /// to pure scattering colour and reads deep blue.
    ///
    /// FIRST PASS USED 7.5 m AND IT WAS THE MILESTONE'S ONE REAL DEFECT. The coast's sea floor
    /// and beach are authored a very pale pink-white sand, and at 7.5 m of absorption the water
    /// column barely tinted it: the whole harbour basin, every sea-stack apron and the entire
    /// inshore band rendered as a flat pale sheet (diag_shiosai_harbour_overlook, first pass) -
    /// sand showing through clear water, not turquoise sea. HDRP's own Ocean preset ships 1.5 m
    /// for exactly this reason. 2.6 m keeps a readable turquoise over the 1-2 m shelf while
    /// still resolving to deep blue by the time the floor reaches ~5 m. PROVISIONAL.
    /// </summary>
    private const float OceanAbsorptionDistanceM = 2.6f;

    /// <summary>Colour of the light that survives the shallow column (the turquoise). Authored
    /// sRGB, converted to linear on assignment. PROVISIONAL.</summary>
    private static readonly Color OceanRefractionColour = new Color(0.11f, 0.66f, 0.62f);
    /// <summary>Colour of the deep, un-lit water column - the offshore blue. PROVISIONAL.</summary>
    private static readonly Color OceanScatteringColour = new Color(0.03f, 0.30f, 0.52f);

    /// <summary>Whitecap coverage on the simulated crests. PROVISIONAL.</summary>
    private const float OceanFoamAmount = 0.42f;

    private const int ScatterSeed = 20260913;

    // --------------------------------------------------------- chapter landmark chainages
    //
    // Every hero landmark is anchored to a NAMED ROUTE ANCHOR published in ShiosaiRoute.json
    // (spec 4.4), never to a fraction of route length and never to a world-space heuristic.
    // The metres beside each name are the FALLBACK used only if the route predates that anchor.
    // The small +/- offsets are PROVISIONAL framing taste: they put the landmark a couple of
    // hundred metres into the rider's view rather than exactly on the anchor post.

    private const string AnchorGatewaySummit = "SC_KM_060_GatewaySummit";
    private const string AnchorTunnelEntry = "SC_KM_155_TunnelEntry";
    private const string AnchorTunnelExit = "SC_KM_160_TunnelExit";
    private const string AnchorShrineOverlook = "SC_KM_140_ShrineOverlook";
    private const string AnchorVillageCentre = "SC_KM_180_VillageCenter";
    private const string AnchorBridgeApproach = "SC_KM_200_BridgeApproach";
    private const string AnchorBridgeMidpoint = "SC_KM_215_BridgeMidpoint";
    private const string AnchorBridgeExit = "SC_KM_230_BridgeExit";
    private const string AnchorLighthouseSummit = "SC_KM_275_LighthouseSummit";
    private const string AnchorSeaArch = "SC_KM_310_SeaArch";
    private const string AnchorIslandReveal = "SC_KM_400_IslandReveal";

    // ROUTE LENGTH REVISION (PROVISIONAL, user decision): the course was compressed from 42 km
    // to ~20 km so the coastal chapters are actually reachable. The anchor NAMES above are
    // stable contract IDs and did NOT change - the SC_KM_ prefix is now a label, not a
    // chainage. These fallbacks are the republished 20 km positions.
    private const float AnchorGatewaySummitM = 1801f;
    private const float AnchorTunnelEntryM = 4654f;
    private const float AnchorTunnelExitM = 4804f;
    private const float AnchorShrineOverlookM = 4203f;
    private const float AnchorVillageCentreM = 5204f;
    private const float AnchorBridgeApproachM = 6906f;
    private const float AnchorBridgeMidpointM = 7706f;
    private const float AnchorBridgeExitM = 8507f;
    private const float AnchorLighthouseSummitM = 10939f;
    private const float AnchorSeaArchM = 12811f;
    private const float AnchorIslandRevealM = 18615f;

    /// <summary>
    /// The gateway arch stands in the first chapter, not at its summit: the rider should ride
    /// THROUGH it early and then earn the panorama. PROVISIONAL (was 3,100 m at 42 km).
    /// </summary>
    private const float GatewayArchM = 930f;

    // --------------------------------------------------------------- menu / entry points

    [MenuItem("MapleRide/Environment/Build Shiosai Coast Mock", priority = 22)]
    public static void BuildCoastPass()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Apply();

        // A coast-specific build command should leave the coast visible and playable. Apply()
        // deliberately preserves the project's normal Sakura start state when called as part of
        // another setup pass, so explicitly travel here before saving the editor scene.
        var regions = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
            FindObjectsInactive.Include);
        if (regions == null || !regions.FastTravel(RegionCatalog.ShiosaiCoast))
            Debug.LogWarning("[shiosai] coast built, but it could not be selected for preview.");

        // THE SHARED SCENE MUST NEVER BE SAVED WITH THE COAST SHOWING.
        //
        // Every region lives in this ONE scene and RegionDirector just shows/hides roots, so
        // whatever is active when SaveScene runs becomes the state the GAME BOOTS INTO. Saving
        // here right after the coast preview shipped SakuraPass.unity with 'Shiosai Coast
        // Environment' active, 'Sakura Pass Environment' inactive and currentRegionId serialized
        // as shiosai_coast - so a player starting a Sakura Pass ride was handed the coast's
        // 91 km ocean plane and inland ranges over the top of their course.
        //
        // RegionDirector.Awake -> SyncFromSession is supposed to correct this, but it can early
        // return before ApplyEnvironmentVisibility if the session graph is not resolved yet, and
        // in any case the scene has to LOAD correct on its own rather than rely on a runtime
        // repair. The preview above is still useful (the diagnostics capture drives the region
        // itself, so it is not what the renders depend on), but the LAST thing that happens
        // before the save must always be a return to the default region.
        if (regions == null || !regions.FastTravel(RegionCatalog.SakuraPass))
            Debug.LogWarning("[shiosai] could not restore the default region before saving.");
        LogRegionRootStates("pre-save");

        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
        Debug.Log($"[shiosai] saved '{active.path}'.");
    }

    /// <summary>
    /// Logs the active state of every region root. This is the state that gets SERIALIZED, and
    /// therefore the state the game boots into - worth printing next to every save.
    /// </summary>
    public static void LogRegionRootStates(string when)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var present = new HashSet<string>();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (!go.name.EndsWith(" Environment")) continue;
            present.Add(go.name);
            Debug.Log($"[shiosai] region-root ({when}): '{go.name}' active={go.activeSelf}");
        }
        var rd = UnityEngine.Object.FindFirstObjectByType<RegionDirector>(
            FindObjectsInactive.Include);
        Debug.Log($"[shiosai] currentRegionId ({when}) = {(rd != null ? rd.currentRegionId : "<no RegionDirector>")}");

        // GUARDRAIL. Every region lives in this ONE boot scene and RegionDirector merely shows/
        // hides one root per region, so a root that is not in the SAVED scene means that region's
        // map pin drops the rider onto the empty checkerboard start line (RideStartPad's universal
        // launch, the only geometry RegionDirector cannot hide). A pass that regenerates the scene
        // from an empty scene silently drops every sibling root; this is where that MUST be caught,
        // before the save, rather than shipped as a black checkerboard for five of the six regions.
        var missing = new List<string>();
        foreach (var region in RegionCatalog.Regions)
        {
            if (!region.Unlocked || string.IsNullOrEmpty(region.EnvironmentRoot)) continue;
            if (!present.Contains(region.EnvironmentRoot)) missing.Add(region.EnvironmentRoot);
        }
        if (missing.Count > 0)
        {
            string msg = $"[shiosai] REGION ROOT ASSERTION FAILED ({when}): the boot scene is " +
                         $"missing {missing.Count} unlocked region root(s): " +
                         $"{string.Join(", ", missing)}. Saving now would ship an empty " +
                         "checkerboard for those regions. Re-stage every region via " +
                         "MapleRideRegionStaging.RestageAllRegions before saving the boot scene.";
            Debug.LogError(msg);
            // In batch mode (a build) this must FAIL the run - a non-zero exit - rather than let
            // a clobbered boot scene be captured and shipped as if it were fine.
            if (Application.isBatchMode)
                throw new InvalidOperationException(msg);
        }
    }

    /// <summary>
    /// Builds the coast into the active scene and stages the world-map / fast-travel systems.
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

        // The coast authors its own texture set; without correct importer settings its normal
        // and roughness maps come in as sRGB colour and the surfaces light wrongly.
        SakuraTextureImportSettings.ApplyAll();

        ImportWorldMapArt();

        var route = CoastRoute.Load();
        Debug.Log($"[shiosai] route: {route.Count} samples, {route.Length:0.0} m one way, " +
                  $"y {route.MinY:0.0} - {route.MaxY:0.0} m.");

        // Converge, never accumulate.
        foreach (var stale in FindRootsByExactName(RootName))
            UnityEngine.Object.DestroyImmediate(stale);

        var root = new GameObject(RootName).transform;

        // An INFINITE ocean draws a sea plane at y = 0 for every camera that can see it, so a
        // water surface that ends up outside the coast region root would flood Sakura Pass and
        // Maple City. Prune scene-wide by exact name BEFORE the coast builds its own, and
        // verify the pipeline actually supports water at all.
        PruneStrayWaterSurfaces();
        VerifyWaterSupport();

        BuildOcean(root);
        BuildLand(root, route);
        BuildRoad(root, route);
        // The landform and the carriageway are both swept from route sample 0, so NOTHING exists
        // behind the start line. See BuildStartApron for why that showed up as a sheet of sea
        // water over the start-line renders.
        BuildStartApron(root, route);
        BuildGuardrail(root, route);
        BuildLandmarks(root, route);
        // Start line/arch is now the universal runtime RideStartPad (a clean tarmac launch +
        // checkered line on every map), so the Shiosai-only environment version is retired to
        // avoid a double start line at arc 0.
        // BuildStartLine(root, route);
        ScatterCoast(root, route);
        BuildDistantDepth(root, route);
        BuildKm209VisualCorrections(root, route);
        BuildHeroBackdrop(root, route);   // high-poly hero landforms (ShiosaiCoastEnvironment.HeroBackdrop.cs)

        // Ambient NPC traffic lives UNDER the region root, so RegionDirector's existing
        // exact-name visibility model hides thirty riders the moment the player travels to
        // Sakura Pass - no extra region bookkeeping, and no riders on the wrong road. It is
        // staged here because Apply() rebuilds the root from scratch every time.
        ShiosaiNpcTraffic.Stage(root);

        // Re-bake so the coast's segment/course/checkpoints reach the runtime graph, then stage
        // the region systems that the World Map drives.
        var graph = RouteGraphBaker.BakeAsset();
        SetupRegionSystems(graph);

        // M6. Two fixes that live OUTSIDE the region root and so must run after it is rebuilt:
        //   * the sky the camera actually draws (HDRP volume, not RenderSettings.skybox), and
        //   * the foreign Sakura Pass backdrop that bleeds onto the coast horizon.
        ConfigureCoastSkyVolume(root);
        HideForeignBackdrop();

        int renderers = root.GetComponentsInChildren<MeshRenderer>(true).Length;
        Debug.Log($"[shiosai] coast built: {renderers} renderers under '{RootName}'.");

        if (headless)
        {
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"[shiosai] saved '{active.path}'.");
        }
    }

    // ------------------------------------------------------------- M6 sky

    /// <summary>
    /// BLOCKER A ROOT CAUSE. <c>BuildCoastSky()</c> authors a perfectly good
    /// <c>MapleRide/HDRP/Sky</c> material with cloud parameters, and <see cref="RegionDirector"/>
    /// assigns it with <c>RenderSettings.skybox = sky</c> - which under HDRP is a NO-OP. HDRP
    /// ignores <c>RenderSettings.skybox</c> entirely and draws whatever sky the active Volume
    /// stack selects (see <c>HdrpVolumeSetup.cs</c>, which forces a flat three-stop GradientSky
    /// into the DEFAULT settings profile). That is why raising _CloudStrength 0.52 -> 0.95 on
    /// disk changed nothing on screen: the material was never drawn.
    ///
    /// The fix drives the asset the camera really uses. A SCENE-LOCAL global Volume carrying a
    /// coast-own profile overrides the project default, so it is region-scoped by construction -
    /// Sakura Pass lives in a different scene file and is untouched - and it can carry HDRP's
    /// native CloudLayer, which the GradientSky alone cannot do.
    ///
    /// Idempotent: the Volume is matched by EXACT name and every duplicate is pruned.
    /// </summary>
    private const string CoastSkyProfilePath =
        "Assets/Environment/ShiosaiCoast/Materials/Shiosai_SkyVolume.asset";
    private const string CoastSkyVolumeName = "Shiosai Sky Volume";

    // PROVISIONAL tuning - the coast sky palette, read off references 03/10/14/18: a deep
    // cobalt zenith falling to a bright pale horizon haze. Values are authored in sRGB and
    // converted, because HDRP gradient stops are linear.
    // GOLDEN-HOUR SKY (fix #1). Zenith stays deep cobalt - golden hour keeps a blue zenith -
    // but the middle and horizon stops are warmed toward a late-afternoon glow so the sky above
    // the sea reads warm (matching SC01/03/08) instead of the old pale-cyan midday haze. The
    // warmth is in the SKY only, not the fog, so the far ocean stays cobalt (no gold bar).
    // GOLDEN-HOUR SKY COMPLETION (fix #1, second pass). The first pass warmed only the BOTTOM
    // (horizon) stop from a cold pale blue to cream, but left the MIDDLE stop pale BLUE and the
    // diffusion at 1.7 - which lifts the blue FAR down the dome, so the warm cream survived only
    // as a thin band right at the horizon (usually below/behind terrain in the chapter cams).
    // Net result: every chapter still rendered a bright midday-BLUE sky (verified by render),
    // undercutting the golden-hour sun+ambient that ARE landing. This pass warms the MIDDLE stop
    // to a late-afternoon peach-grey (the mid-sky is what the chapter cams actually frame) and
    // eases the diffusion so that warmth rises visibly up the dome, while the ZENITH stays blue
    // (golden hour keeps a blue zenith - SC01 has blue up top over a warm lower sky). The warmth
    // is still in the SKY only, NOT the fog, so the far ocean stays cobalt (no gold horizon bar).
    // SUNNY-COAST SKY (start-line milestone, gap #3). Supersedes the "golden hour" warm-middle
    // pass above. The reference plate the coast is now being judged against
    // (Assets/Environment/ShiosaiCoast/ShiosaiCoast_02.png) is bright sunny Ghibli realism: a
    // deep saturated blue that STAYS blue most of the way down the dome and only opens out to a
    // pale marine haze right on the sea horizon, with big white cumulus reading against it. The
    // warm peach middle stop rendered as the grey-lavender/cream overcast wash that was the
    // single largest contributor to the "Roblox" read at the start line (verified by render:
    // reference/good_graphics/diag_shiosai_start_chase.png before this change). Authored in sRGB
    // and converted to linear at the point of use. ALL PROVISIONAL.
    private static readonly Color CoastSkyTop    = new Color(0.07f, 0.29f, 0.68f);  // deep cobalt zenith
    private static readonly Color CoastSkyMiddle = new Color(0.26f, 0.55f, 0.86f);  // still blue at mid-dome
    private static readonly Color CoastSkyBottom = new Color(0.74f, 0.87f, 0.94f);  // pale marine haze at the sea line
    private const float CoastSkyDiffusion = 2.2f;   // higher = the blue reaches further down the dome
    private const float CoastCloudOpacity = 0.85f;  // puffy cumulus, not overcast
    private const float CoastCloudAltitude = 2600f;
    private const float CoastCloudThickness = 0.55f;

    // ================================================================= FIDELITY POC
    // PHOTOREAL FIDELITY POC (reference/improve/TARGET_shiosai_photoreal_goal.png).
    // The target frame's "photo" quality comes overwhelmingly from three things the coast
    // profile did NOT have at all, in this order of impact-per-cost:
    //   1. AERIAL PERSPECTIVE. VisualEnvironment.fogType was 0 (None) - verified in the saved
    //      .asset - so the far coastline, the sea stacks and the inland ranges all rendered at
    //      full saturation right to the horizon. That single fact is what makes the baseline
    //      read as a toy diorama: there is no depth cue at all beyond perspective.
    //   2. NO TONEMAPPER. HDRP with no Tonemapping override clips linear radiance straight to
    //      display, so every lit surface races to a hard, plasticky primary. The target is
    //      filmic: highlights roll off, saturation falls away as things get bright.
    //   3. NO EXPOSURE override, so exposure was whatever the default profile happened to say.
    // Everything below is PROVISIONAL art tuning for the POC.

    /// <summary>
    /// Fog mean free path in metres - the distance over which the air removes 1/e of the
    /// contrast. Tuned against the coast's real depth budget, NOT guessed: the route is ~22 km
    /// long but the framed depth in the hero shots is the far headland and the inland ranges at
    /// roughly 1.5-4 km. At 1800 m a ridge at 1.5 km keeps ~43% of its own colour and one at
    /// 4 km keeps ~11%, so the backdrop separates into receding layers the way the target does
    /// instead of either staying vivid (the baseline) or dissolving into a flat wash.
    /// </summary>
    private const float CoastFogMeanFreePath = 1800f;
    /// <summary>Fog is a sea-haze layer, so it thins with altitude rather than filling the sky.</summary>
    private const float CoastFogBaseHeight = -20f;
    private const float CoastFogMaxHeight = 900f;
    /// <summary>Haze colour: the pale marine horizon, matched to <see cref="CoastSkyBottom"/>
    /// so distant land dissolves INTO the sky instead of cutting out against it.</summary>
    private static readonly Color CoastFogAlbedo = new Color(0.78f, 0.86f, 0.92f);
    /// <summary>Volumetric fog gives real light shafts and depth-graded scattering. It is the
    /// expensive half of the win; the distance fog above is the cheap half.</summary>
    private const bool CoastFogVolumetric = true;
    private const float CoastFogAnisotropy = 0.35f;     // forward-scatter, sun-facing glow
    private const float CoastFogVolumetricDistance = 900f;
    private const float CoastFogDepthExtent = 90f;

    /// <summary>
    /// Fixed EV100, and it MUST stay at the project's authored 0.
    ///
    /// VERIFIED THE HARD WAY: the first POC pass set this to a photographically correct 13.6 EV
    /// for bright daylight over water, and every start-line render came back COMPLETELY BLACK
    /// while the build logged clean and exited 0. The reason is that this project's lighting rig
    /// is NOT physical - RegionDirector.ShiosaiAmbience drives the key light at
    /// keyIntensity = 1.16 (a Built-in-era multiplier carried through the HDRP conversion), not
    /// at a daylight ~100,000 lux, and Assets/Settings/HDRP/DefaultSettingsVolumeProfile.asset
    /// pins fixedExposure to 0. A real-world EV is therefore ~13.6 stops - about 12,000x - below
    /// what this scene actually emits.
    ///
    /// The override is kept (rather than deleted) precisely so the region pins its own exposure
    /// instead of silently inheriting the shared default profile, but the VALUE is matched to
    /// the rig. Moving the coast to physical light units is a real follow-up, not a POC step.
    /// </summary>
    private const float CoastFixedExposure = 0f;

    /// <summary>Filmic highlight roll-off. ACES is the strongest "this is a photograph" cue
    /// available for free, and it is what pulls the baseline's electric cobalt sea and neon
    /// grass back to plausible.</summary>
    private const TonemappingMode CoastTonemapping = TonemappingMode.ACES;

    private const float CoastBloomIntensity = 0.18f;
    private const float CoastBloomScatter = 0.72f;

    /// <summary>
    /// Post-exposure compensation, in EV, applied on top of the fixed exposure.
    ///
    /// ACES compresses midtones on its way to filmic highlight roll-off, so switching the
    /// tonemapper on costs roughly half a stop of apparent midtone brightness. MEASURED, not
    /// guessed: mean frame luma over diag_shiosai_start_3q.png fell 0.328 -> 0.223 when ACES
    /// came in. This buys that back without touching the lighting rig, which other regions share.
    /// PROVISIONAL.
    /// </summary>
    private const float CoastPostExposure = 0.6f;

    /// <summary>
    /// Base-colour multiplier for the PBR asphalt. This is NOT an arbitrary brightener, and it
    /// is deliberately WARM. Two separate corrections are folded into it:
    ///
    /// 1. BRIGHTNESS. Shiosai_Asphalt_Albedo.png has a mean of 0.18 in sRGB, which decodes to
    ///    0.027 LINEAR. Real asphalt sits around 0.05-0.08 linear reflectance, so the map is
    ///    roughly a stop and a half too dark to be used as a physical albedo - it was authored
    ///    to be read by MapleRide/HDRP/CelLit, whose large unconditional ambient term was
    ///    silently compensating. Feeding it to HDRP/Lit unmodified took the road from luma
    ///    0.223 to 0.039 (a 5.7x drop, MEASURED on the render, not inferred).
    /// 2. COLOUR CAST. RegionDirector.ShiosaiAmbience runs a strongly blue ambient dome
    ///    (0.44, 0.60, 0.86). CelLit's flat shading hid it; a real PBR diffuse surface picks it
    ///    up honestly, and a neutral tint rendered the road as denim-lavender - measured
    ///    (0.257, 0.317, 0.485) against the target frame's near-neutral (0.283, 0.269, 0.306).
    ///    The warm skew here cancels that dome so the asphalt lands neutral in sun AND stays
    ///    neutral (rather than going brown) in shadow, where the blue ambient dominates.
    ///
    /// The proper fixes are re-authoring the map at true linear albedo and moving the region to
    /// a physically-scaled light rig; both belong in the asset-pipeline follow-up, not this POC.
    /// Solved against the target frame by measurement, in two iterations: a neutral 2.6x tint
    /// rendered denim-lavender (0.257, 0.317, 0.485); an over-warm (2.95, 1.95, 1.15) rendered
    /// brown (0.284, 0.230, 0.221). This value lands the road on the target's measured
    /// (0.283, 0.269, 0.306).
    /// </summary>
    private static readonly Color CoastAsphaltTint = new Color(2.93f, 2.53f, 1.96f);

    // GRADE (gap #7). SakuraPostFX is an OnRenderImage component and HDRP never calls
    // OnRenderImage, so RegionDirector.ShiosaiAmbience's saturation/contrast grade has been
    // inert since the HDRP conversion (proved by render: lab_1_base == lab_2_nopostfx). Rather
    // than leave the region ungraded, the same intent is expressed through HDRP's own
    // ColorAdjustments on THIS region-scoped profile - not the shared default profile, so no
    // other region's reference renders move. HDRP takes both as -100..100 offsets around 0,
    // i.e. the struct's 1.22/1.17 multipliers. PROVISIONAL.
    //
    // FIDELITY POC: +22 saturation / +17 contrast were authored to fight a FLAT, untonemapped,
    // fog-free image - they were compensation for the three missing pieces above, and they are
    // the direct cause of the "toy" primaries in reference/good_graphics/BEFORE_*.png. With
    // ACES + fog now doing that work honestly, the same numbers double-cook the frame. Pulled
    // to a slight DESATURATION, which is what photographic coastal haze actually does.
    private const float CoastSaturation = -6f;
    private const float CoastContrast   = 8f;

    /// <summary>
    /// <c>VolumeProfile.Add&lt;T&gt;()</c> only creates the component in MEMORY. Without an
    /// explicit <c>AddObjectToAsset</c> the profile serialises three NULL component references
    /// (verified: the saved .asset listed <c>- {fileID: 0}</c> three times), so the sky worked
    /// for exactly the editor session that built it and reverted on reload.
    /// </summary>
    private static T EnsureOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var comp) || comp == null)
        {
            comp = profile.Add<T>(true);
        }
        if (!AssetDatabase.Contains(comp))
        {
            comp.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
        comp.active = true;
        return comp;
    }

    private static void ConfigureCoastSkyVolume(Transform parent)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(CoastSkyProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, CoastSkyProfilePath);
        }

        var ve = EnsureOverride<VisualEnvironment>(profile);
        ve.skyType.overrideState = true;
        ve.skyType.value = (int)SkyType.Gradient;
        ve.cloudType.overrideState = true;
        ve.cloudType.value = (int)CloudType.CloudLayer;

        var sky = EnsureOverride<GradientSky>(profile);
        sky.top.overrideState = true;                sky.top.value = CoastSkyTop.linear;
        sky.middle.overrideState = true;             sky.middle.value = CoastSkyMiddle.linear;
        sky.bottom.overrideState = true;             sky.bottom.value = CoastSkyBottom.linear;
        sky.gradientDiffusion.overrideState = true;  sky.gradientDiffusion.value = CoastSkyDiffusion;

        var clouds = EnsureOverride<CloudLayer>(profile);
        // CloudLayer's built-in default cloud map lives in the HDRP package's resource asset and
        // resolves to NULL in batchmode (proved by the Apply log: "cloudMapA=<null>"), which is
        // why the first CloudLayer pass rendered a completely cloudless sky at opacity 1.0. The
        // coast ships its own map (tools/blender/build_shiosai_cloudmap.py), coverage in RED.
        const string cloudMapPath =
            "Assets/Environment/ShiosaiCoast/Textures/Shiosai_CloudMap.png";
        var importer = AssetImporter.GetAtPath(cloudMapPath) as TextureImporter;
        if (importer != null && (importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Repeat))
        {
            importer.sRGBTexture = false;          // coverage mask, not colour
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }
        var cloudMap = AssetDatabase.LoadAssetAtPath<Texture2D>(cloudMapPath);
        if (cloudMap == null)
            Debug.LogWarning("[shiosai] cloud map missing - run " +
                             "python tools/blender/build_shiosai_cloudmap.py");
        else
        {
            clouds.layerA.cloudMap.overrideState = true;
            clouds.layerA.cloudMap.value = cloudMap;
        }
        clouds.opacity.overrideState = true;             clouds.opacity.value = CoastCloudOpacity;
        clouds.upperHemisphereOnly.overrideState = true; clouds.upperHemisphereOnly.value = true;
        clouds.layers.overrideState = true;
        clouds.layers.value = UnityEngine.Rendering.HighDefinition.CloudMapMode.Single;
        clouds.layerA.altitude.overrideState = true;     clouds.layerA.altitude.value = CoastCloudAltitude;
        clouds.layerA.tint.overrideState = true;         clouds.layerA.tint.value = Color.white;
        clouds.layerA.exposure.overrideState = true;     clouds.layerA.exposure.value = 0f;
        clouds.layerA.opacityR.overrideState = true;     clouds.layerA.opacityR.value = 1f;
        clouds.layerA.lighting.overrideState = true;     clouds.layerA.lighting.value = true;
        clouds.layerA.thickness.overrideState = true;    clouds.layerA.thickness.value = CoastCloudThickness;
        clouds.layerA.steps.overrideState = true;        clouds.layerA.steps.value = 8;

        // GRADE, region-scoped (see CoastSaturation).
        var ca = EnsureOverride<ColorAdjustments>(profile);
        ca.saturation.overrideState = true; ca.saturation.value = CoastSaturation;
        ca.contrast.overrideState = true;   ca.contrast.value = CoastContrast;
        ca.postExposure.overrideState = true; ca.postExposure.value = CoastPostExposure;

        // ---------------------------------------------------- FIDELITY POC: atmosphere
        // 1. AERIAL PERSPECTIVE. The saved profile had NO Fog override at all and
        //    VisualEnvironment.fogType serialised as 0, so the far coastline rendered at full
        //    saturation to the horizon. In HDRP 17 `VisualEnvironment.fogType` is internal and
        //    inert - the Fog override's own `enabled` flag is the real switch (confirmed by
        //    CS0122/CS1061 when driving fogType from user code), so that is what is written.
        var fog = EnsureOverride<Fog>(profile);
        fog.enabled.overrideState = true;            fog.enabled.value = true;
        fog.meanFreePath.overrideState = true;       fog.meanFreePath.value = CoastFogMeanFreePath;
        fog.baseHeight.overrideState = true;         fog.baseHeight.value = CoastFogBaseHeight;
        fog.maximumHeight.overrideState = true;      fog.maximumHeight.value = CoastFogMaxHeight;
        fog.albedo.overrideState = true;             fog.albedo.value = CoastFogAlbedo;
        fog.tint.overrideState = true;               fog.tint.value = Color.white;
        // Let the fog inherit the sky's own colour at distance. This is what turns a flat grey
        // veil into true aerial perspective: the far coastline takes the horizon's pale marine
        // blue, exactly as in the target frame.
        fog.mipFogNear.overrideState = true;         fog.mipFogNear.value = 0f;
        fog.mipFogFar.overrideState = true;          fog.mipFogFar.value = 5000f;
        fog.mipFogMaxMip.overrideState = true;       fog.mipFogMaxMip.value = 0.5f;
        fog.enableVolumetricFog.overrideState = true;
        fog.enableVolumetricFog.value = CoastFogVolumetric;
        fog.anisotropy.overrideState = true;         fog.anisotropy.value = CoastFogAnisotropy;
        fog.depthExtent.overrideState = true;        fog.depthExtent.value = CoastFogDepthExtent;
        fog.globalLightProbeDimmer.overrideState = true;
        fog.globalLightProbeDimmer.value = 1f;

        // 2. FILMIC ROLL-OFF.
        var tm = EnsureOverride<Tonemapping>(profile);
        tm.mode.overrideState = true;                tm.mode.value = CoastTonemapping;

        // 3. DETERMINISTIC EXPOSURE, so the grade above means the same thing in every shot
        //    instead of riding on whatever auto-exposure the default profile supplies.
        var ex = EnsureOverride<Exposure>(profile);
        ex.mode.overrideState = true;                ex.mode.value = ExposureMode.Fixed;
        ex.fixedExposure.overrideState = true;       ex.fixedExposure.value = CoastFixedExposure;

        var bloom = EnsureOverride<Bloom>(profile);
        bloom.intensity.overrideState = true;        bloom.intensity.value = CoastBloomIntensity;
        bloom.scatter.overrideState = true;          bloom.scatter.value = CoastBloomScatter;

        Debug.Log($"[shiosai] FIDELITY POC atmosphere: fog=Volumetric mfp={CoastFogMeanFreePath}m " +
                  $"height={CoastFogBaseHeight}..{CoastFogMaxHeight} albedo={CoastFogAlbedo} " +
                  $"volumetric={CoastFogVolumetric} aniso={CoastFogAnisotropy} | " +
                  $"tonemap={CoastTonemapping} exposureEV={CoastFixedExposure} " +
                  $"bloom={CoastBloomIntensity} | sat={CoastSaturation} con={CoastContrast}");

        Debug.Log($"[shiosai] sky volume: gradient top={CoastSkyTop} mid={CoastSkyMiddle} " +

                  $"bot={CoastSkyBottom} diff={CoastSkyDiffusion} sat={CoastSaturation} con={CoastContrast}");

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Exact-name match + prune, per the pipeline's idempotency invariant. CRITICAL: the
        // volume is parented UNDER THE COAST REGION ROOT, not left as a scene root. Sakura Pass
        // and Shiosai Coast share Assets/Scenes/SakuraPass.unity and RegionDirector shows/hides
        // the region roots, so a global scene-root volume would impose the coast's blue daylight
        // sky on Sakura's sunset. Under the root it switches off with the region, for free.
        Transform keep = null;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var c = parent.GetChild(i);
            if (!string.Equals(c.name, CoastSkyVolumeName, StringComparison.Ordinal)) continue;
            if (keep == null) keep = c;
            else UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
        if (keep == null)
        {
            var go = new GameObject(CoastSkyVolumeName);
            go.transform.SetParent(parent, false);
            keep = go.transform;
        }

        var vol = keep.GetComponent<Volume>() ?? keep.gameObject.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 50f;          // above the project default settings profile
        vol.weight = 1f;
        vol.sharedProfile = profile;
        Debug.Log($"[shiosai] sky: scene volume '{CoastSkyVolumeName}' -> GradientSky + CloudLayer " +
                  $"(opacity {CoastCloudOpacity}). RenderSettings.skybox is ignored by HDRP. " +
                  $"profile components={profile.components.Count}, cloudMapA=" +
                  $"{(clouds.layerA.cloudMap.value != null ? clouds.layerA.cloudMap.value.name : "<null>")}");
    }

    // --------------------------------------------------------- M6 region bleed

    /// <summary>
    /// BLOCKER B, REVISED. The original version DEACTIVATED these objects, which was safe only
    /// while the coast lived in its own forked scene. It does not: Sakura Pass and Shiosai Coast
    /// SHARE Assets/Scenes/SakuraPass.unity and RegionDirector simply shows/hides the region
    /// roots. Deactivating Sakura's backdrop here would therefore delete Sakura's own horizon
    /// permanently - breaking a region this milestone must not touch.
    ///
    /// So this is now REPORT-ONLY: it logs whether each backdrop object exists and whether it
    /// already sits under a region-managed root (in which case RegionDirector hides it for us
    /// and there is no bleed to fix). Any real cull has to be region-conditional at runtime,
    /// not a one-shot SetActive in an editor pass.
    private static readonly string[] ForeignBackdropNames =
    {
        "Distant Ranges",
        "Hero Volcano",
        "Fuji Vista",
        "Mountain Backdrop",
    };

    private static void HideForeignBackdrop()
    {
        var all = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int found = 0;
        foreach (var t in all)
        {
            if (t == null) continue;
            bool match = false;
            foreach (var n in ForeignBackdropNames)
                if (string.Equals(t.name, n, StringComparison.Ordinal)) { match = true; break; }
            if (!match) continue;
            found++;
            var top = t.root;
            Debug.Log($"[shiosai] backdrop audit: '{t.name}' under root '{top.name}' " +
                      $"(root active={top.gameObject.activeSelf}, self active={t.gameObject.activeSelf}).");
        }
        Debug.Log($"[shiosai] backdrop audit: {found} backdrop object(s) present. REPORT ONLY - " +
                  "the coast shares SakuraPass.unity, so nothing is deactivated here.");
    }

    // --------------------------------------------------------------- route

    [Serializable] private class SampleDto { public float[] p, t, s, u; public float bank, d; }
    [Serializable] private class CheckpointDto { public string segment, name; public float distance; }
    [Serializable] private class ChapterDto { public string id, name; public float from, to; }
    [Serializable] private class RouteDto
    {
        public float roadHalfWidth, shoulderWidth, seaLevel;
        public SampleDto[] samples;
        public CheckpointDto[] checkpoints;
        public ChapterDto[] chapters;
    }

    /// <summary>The published coastal centreline, in Unity world space.</summary>
    public class CoastRoute
    {
        public Vector3[] Position = Array.Empty<Vector3>();
        public Vector3[] Tangent = Array.Empty<Vector3>();
        public float[] Distance = Array.Empty<float>();

        public int Count => Position.Length;
        public float Length => Distance.Length == 0 ? 0f : Distance[Distance.Length - 1];
        public float MinY, MaxY;

        /// <summary>
        /// Named route anchors published by tools/blender/shiosai_route.py (spec 4.4), keyed by
        /// name -> ABSOLUTE chainage in metres.
        ///
        /// ROOT CAUSE OF THE "HERO LANDMARK NEVER APPEARS AT ITS CHAPTER" DEFECT. Every landmark
        /// in this file used to be placed either by a FRACTION of route length (0.34, 0.55, ...)
        /// or by a world-space heuristic ("the first sample with z >= -940"). Both were chosen
        /// against the 2.892 km mock. On the 42 km Grand Coast the harbour heuristic resolved to
        /// 22.7 km - i.e. the lighthouse and the whole fishing village were built 4.7 km PAST the
        /// Fishing Village chapter (16.0-20.0 km), inside the Lighthouse Climb. The chapters are
        /// a CHAINAGE contract published in the route JSON, so landmarks must be anchored to it.
        /// </summary>
        public readonly Dictionary<string, float> Anchors = new Dictionary<string, float>();

        /// <summary>Chapter id -> (from, to) chainage, straight off the published route.</summary>
        public readonly Dictionary<string, Vector2> Chapters = new Dictionary<string, Vector2>();

        /// <summary>
        /// Chainage of a published anchor. <paramref name="fallbackM"/> is used (with a warning)
        /// when the route predates the anchor, so a stale route degrades to "landmark in roughly
        /// the right place" rather than to an exception mid-build.
        /// </summary>
        public float Anchor(string name, float fallbackM)
        {
            if (Anchors.TryGetValue(name, out float d)) return d;
            Debug.LogWarning($"[shiosai] route has no anchor '{name}'; using {fallbackM:0} m.");
            return fallbackM;
        }

        /// <summary>Sample index at a published anchor.</summary>
        public int AnchorIndex(string name, float fallbackM) => IndexAt(Anchor(name, fallbackM));

        /// <summary>Horizontal right-hand side vector - terrain is never banked.</summary>
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

        public static CoastRoute Load()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(RoutePath);
            if (text == null)
                throw new FileNotFoundException(
                    $"{RoutePath} is missing. Run: blender -b -P tools/blender/shiosai_route.py");

            var dto = JsonUtility.FromJson<RouteDto>(text.text);
            if (dto?.samples == null || dto.samples.Length < 2)
                throw new InvalidDataException($"{RoutePath} has no samples.");

            int n = dto.samples.Length;
            var r = new CoastRoute
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

            if (dto.checkpoints != null)
                foreach (var c in dto.checkpoints)
                    if (!string.IsNullOrEmpty(c.name)) r.Anchors[c.name] = c.distance;
            if (dto.chapters != null)
                foreach (var c in dto.chapters)
                    if (!string.IsNullOrEmpty(c.id)) r.Chapters[c.id] = new Vector2(c.from, c.to);

            return r;
        }
    }

    // --------------------------------------------------------------- land profile
    //
    // THE CROSS-SECTION IS PER-STATION, NOT CONSTANT.
    //
    // The 20 concept renders are emphatic about one thing: this is a CLIFF road. The land does
    // not shelve gently into the water at a fixed angle - it alternates between sheer headlands
    // that fall straight into deep blue water and soft coves with a sand beach and a shallow
    // turquoise shelf. A single fixed profile swept along the route produced exactly the
    // "extruded ribbon" read the mock had. So every station computes its own section from two
    // scalars: how cove-like it is, and a ragged coastline jitter.
    //
    // All heights below are PROVISIONAL art tuning.

    private const int NodeCount = 18;

    // Node index -> material band. Kept as named constants because the ribbons are cut on them.
    private const int NodeShoreline = 4;   // waterline
    private const int NodeBeachBack = 6;   // back of the beach == foot of the cliff
    private const int NodeCliffTop = 9;    // top of the cliff == seaward verge
    private const int NodeLast = NodeCount - 1;

    private const float CliffTopOffset = -11.0f;   // seaward verge, just outside the guardrail

    /// <summary>
    /// Half-width of the ROAD CORRIDOR that the switchback curvature clamp is forbidden to
    /// shrink, in metres.
    ///
    /// ROOT CAUSE OF THE "PLAYER IS OFF-CENTRE / OFF-ROAD ON SHIOSAI" DEFECT. The hairpin-fold
    /// fix scaled EVERY cross-section node on the concave side by one factor measured against
    /// the OUTERMOST node (210 m inland, 300 m out to sea). At the ~34 m switchback that factor
    /// is ~0.11, so it did not just pull the distant hillside in - it dragged the nodes that
    /// define the carriageway itself in with it: the inland verge at +8.0/+9.8 m collapsed to
    /// ~+0.9/+1.1 m and the seaward clifftop at -11 m to ~-1.2 m. Because BuildRoad/BuildMarkings
    /// take their surface Y from this same profile, the asphalt then ramped up by ~0.9 m across
    /// its own width at every bend while the baked RouteGraph samples the player and the traffic
    /// director are placed from did not move at all. The rider rode the (unchanged) centreline
    /// through a road surface that had tilted and climbed out from under them, which reads
    /// exactly as "the player is not on the road any more".
    ///
    /// The clamp now scales only the part of an offset OUTSIDE this corridor, so the road, both
    /// verges, the guardrail line and the clifftop are bit-for-bit identical on a hairpin and on
    /// a straight, while the distant hillside still shrinks enough not to fold.
    /// PROVISIONAL: 12 m, i.e. the widest node the corridor owns (+9.8 m) and the clifftop
    /// (-11.0 m) plus a small margin.
    /// </summary>
    private const float CorridorProtectM = 12f;

    /// <summary>Clearance the clamp always leaves OUTSIDE the corridor, so a bend still has a
    /// few metres of land beyond the verge to stand a guardrail post and a tree on.</summary>
    private const float CorridorBandM = 12f;

    /// <summary>Chainage where the corridor starts turning coastal. PROVISIONAL (20 km route).</summary>
    private const float SeanessRampStartM = 3780f;

    // ------------------------------------------------------------------ cliff tunnel (spec 5.3)
    // ALL PROVISIONAL. Sized so the third-person chase camera rides through without clipping.
    /// <summary>Clear half-span of the bore, metres (13 m carriageway envelope).</summary>
    private const float TunnelBoreHalfW = 6.5f;
    /// <summary>Wall height to the springline; the crown is a semicircle of the half-span.</summary>
    private const float TunnelSpringH = 2.9f;
    /// <summary>Crown segments, springing to springing.</summary>
    private const int TunnelCrownSegs = 14;
    /// <summary>Rock cover at the two portals - thin, so the mouth reads as an arch ring.</summary>
    private const float TunnelPortalCoverM = 3.2f;
    /// <summary>Rock cover at mid-bore - thick, so the road burrows through a real headland.</summary>
    private const float TunnelMidCoverM = 26f;
    /// <summary>How far the massif skirt is buried below the verge.</summary>
    private const float TunnelSkirtDropM = 55f;
    /// <summary>Spacing of the interior lamp panels, metres.</summary>
    private const float TunnelLampSpacingM = 18f;
    /// <summary>
    /// Crown lamp geometry. ALL PROVISIONAL - tuned by eye against diag_shiosai_tunnel_interior
    /// .png, not derived from anything. Lamps hang in twin rows either side of the crown
    /// centreline (the usual Japanese bore layout) rather than sitting on the springline walls,
    /// where they were edge-on to the camera and therefore invisible.
    /// </summary>
    private const float TunnelLampOffsetM = 2.3f;    // lateral offset of each row from centreline
    private const float TunnelLampDropM = 0.55f;     // how far the strip hangs below the crown
    private const float TunnelLampLengthM = 2.4f;    // along the bore
    private const float TunnelLampWidthM = 0.44f;    // across the bore
    /// <summary>
    /// Sodium-amber emissive intensity in nits. Low ON PURPOSE: the brief is a tunnel that
    /// reads as a real road tunnel WITHOUT washing out the dark mood, so the lamps should be
    /// the brightest thing in frame while still leaving the bore walls in near-darkness.
    /// PROVISIONAL. Tuned DOWN from 1400: at 1400 nits the strips rendered pure white after
    /// tonemapping, so the tunnel read as strip-lit with fluorescent tubes instead of the warm
    /// sodium the brief asks for. 280 keeps them the brightest thing in the bore while the
    /// amber hue still survives exposure.
    /// </summary>
    private const float TunnelLampNits = 280f;
    /// <summary>Warm sodium-amber lamp tint. PROVISIONAL.</summary>
    private static readonly Color TunnelLampColour = new Color(1.00f, 0.74f, 0.38f, 1f);
    /// <summary>Chainage where the corridor is fully coastal. PROVISIONAL (20 km route).</summary>
    private const float SeanessRampEndM = 4680f;

    /// <summary>
    /// How coastal the corridor is at chainage <paramref name="s"/>: 0 = an inland mountain
    /// valley (no beach, no sea floor), 1 = the cliff-and-cove profile.
    ///
    /// WHY THIS EXISTS. The cross-section was authored when Shiosai was a 2.892 km coast road and
    /// every station was on the water, so nodes 0-6 unconditionally lay a beach and a sea floor
    /// (down to y = -30) on the seaward side. The 42 km Grand Coast opens with 11 km of MOUNTAIN
    /// - the Gateway climbs to 420 m and the Upper Switchbacks stack above the treeline - and
    /// sweeping a beach off the side of a 420 m alpine hairpin produces a 450 m vertical wall of
    /// sand and sea floor at every station. Nothing in the alignment contract or the shader work
    /// would have caught that; it is purely a question of which chapter you are in.
    ///
    /// Chapter boundaries are the spec section 2 table. PROVISIONAL: the ramp spans are authoring
    /// taste, not a stated requirement.
    /// </summary>
    private static float Seaness(float s)
    {        // Ch1 Mountain Gateway (0-1.8 km) and Ch2 Upper Switchbacks (1.8-3.3 km) are inland.
        // Ch3 Hydrangea Descent (3.3-4.8 km) is the transition that brings the road down to the
        // water; everything from the Fishing Village on is coastal.
        //
        // PROVISIONAL, rescaled with the 42 km -> 20 km route revision (was 12,600 / 15,600).
        // The ramp still closes just before the tunnel, so the rider emerges already coastal.
        return Smooth01(Mathf.InverseLerp(SeanessRampStartM, SeanessRampEndM, s));
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// 0 = sheer headland falling into deep water, 1 = soft cove with a wide beach.
    /// Driven by named coves plus a slow wobble so the coastline is never uniform.
    ///
    /// Parameterised by CHAINAGE, not world z. The mock's cove centres were world-z values inside
    /// a 2.9 km span that happened to run along +z; a 42 km route that winds, doubles back through
    /// twelve hairpins and covers 25 km of z would have aliased those four gaussians into a
    /// coastline that repeats wherever the road happens to re-cross z = 100 m. Chainage is
    /// monotonic by contract, so shaping keyed on it is single-valued everywhere by construction
    /// and lands the beaches where the CHAPTER wants them.
    /// </summary>
    private static float Coveness(float s)
    {
        float cv = 0f;
        // (chainage centre, sigma, weight) - PROVISIONAL, placed against the spec section 2
        // chapter table so the sand appears where the story says the road meets the water.
        // Centres and sigmas were rescaled with the 42 km -> 20 km route revision: each centre
        // is remapped through its chapter and each sigma scaled by that chapter's compression,
        // so a cove still covers the same FRACTION of its chapter as it did at 42 km.
        cv = Mathf.Max(cv, 1.00f * Mathf.Exp(-Sqr((s - 4620f) / 270f)));    // Hydrangea Cove
        cv = Mathf.Max(cv, 0.95f * Mathf.Exp(-Sqr((s - 5250f) / 578f)));    // fishing harbour bay
        cv = Mathf.Max(cv, 0.62f * Mathf.Exp(-Sqr((s - 7706f) / 373f)));    // red bridge estuary
        cv = Mathf.Max(cv, 0.45f * Mathf.Exp(-Sqr((s - 12811f) / 480f)));   // sea-arch strand
        cv = Mathf.Max(cv, 0.70f * Mathf.Exp(-Sqr((s - 16850f) / 980f)));   // coastal highway bays
        // The slow wobble frequencies are scaled with the route so the coastline keeps the same
        // number of undulations end to end instead of flattening to two.
        cv += 0.10f * Mathf.Sin(s * 0.0038f) + 0.06f * Mathf.Sin(s * 0.00993f + 1.7f);
        return Mathf.Clamp01(cv);
    }

    private static float Sqr(float v) => v * v;

    /// <summary>
    /// Seaward lateral offset for cross-section node <paramref name="n"/> when the corridor is
    /// inland (see <see cref="Seaness"/>). Mirrors the reach of the coastal profile's nodes so
    /// the two can be blended without the ribbon shearing, but spaces them for a hillside
    /// falling to a valley floor rather than a cliff, a beach and a sea bed.
    /// </summary>
    private static float MountainOffset(int n)
    {
        switch (n)
        {
            case 8: return -15f;
            case 7: return -21f;
            case 6: return -30f;
            case 5: return -44f;
            case 4: return -66f;
            case 3: return -70f;
            // SEAWARD REACH IS DELIBERATELY SHORT INLAND.
            //
            // These nodes used to run out to -340 m. The cross-section is swept perpendicular to
            // the centreline, so on the Gateway climb's switchbacks (turn radii of tens of
            // metres) a 340 m arm crosses its own neighbours and the ribbon folds back over
            // itself; the fold's underside is unlit, which is what rendered as the flat grey
            // "floating island" card with roadside trees apparently hovering in front of it.
            // An ISOLATION CAPTURE (Isolate Shiosai Ch1 Artifact) pinned the card on the SEA
            // FLOOR band specifically. Keeping the arm inside the local turn radius is the only
            // reliable cure - a wider mountain flank simply cannot be swept from a hairpin.
            case 2: return -105f;
            case 1: return -145f;
            default: return -190f;
        }
    }

    /// <summary>Ragged coastline jitter (metres added to the cliff's horizontal run).</summary>
    private static float ShoreJitter(float s) =>
        9f * Mathf.Sin(s * 0.0113f) + 5f * Mathf.Sin(s * 0.0317f + 2.3f) +
        3f * Mathf.Sin(s * 0.0907f + 0.6f);

    /// <summary>Half-width (in chainage) of the flat-bottomed part of the Red Bridge estuary.
    /// Must stay comfortably inside RedBridgeSpanM/2 (85 m) or the span lands in the water.
    /// PROVISIONAL.</summary>
    private const float EstuaryHalfWidthM = 46f;
    /// <summary>Chainage over which the estuary banks climb back to the natural ground.
    /// PROVISIONAL.</summary>
    private const float EstuaryBankM = 30f;
    /// <summary>Mid-channel depth below sea level. PROVISIONAL.</summary>
    private const float EstuaryDepthM = 8f;

    private static CoastRoute _estuaryRoute;
    private static float _estuaryS;

    /// <summary>Chainage of the Red Bridge, cached: CrossSection runs ~14,000 times per ribbon
    /// and the anchor lookup is a dictionary probe plus a scan.</summary>
    private static float EstuaryChainage(CoastRoute r)
    {
        if (!ReferenceEquals(_estuaryRoute, r))
        {
            _estuaryRoute = r;
            _estuaryS = r.Anchor(AnchorBridgeMidpoint, AnchorBridgeMidpointM);
        }
        return _estuaryS;
    }

    /// <summary>Inland relief, so the coastal hills are not a featureless wash of green.</summary>
    private static float InlandRelief(float s, float offset)
    {
        float w = Mathf.Clamp01((Mathf.Abs(offset) - 20f) / 90f);   // nothing near the road cut
        return w * (7.5f * Mathf.Sin(s * 0.0071f + offset * 0.011f) +
                    4.0f * Mathf.Sin(s * 0.0229f - offset * 0.023f + 1.4f) +
                    2.0f * Mathf.Sin(s * 0.0513f + 0.9f));
    }

    /// <summary>
    /// Builds one cross-section: 18 (offset, height) pairs from the sea floor (seaward, negative
    /// offset) to the inland hilltop. Heights are absolute world Y.
    /// </summary>
    private static void CrossSection(CoastRoute r, int i, float[] off, float[] hgt)
    {
        var p = r.Position[i];
        float s = r.Distance[i];
        float roadY = p.y;
        float sea = Seaness(s);
        float cv = Coveness(s);
        float jit = ShoreJitter(s);

        // The cliff: sheer (10 m of run for 30 m of drop) on a headland, a long grassy slope in
        // a cove. Clamped so a tall roller cannot produce a negative-run overhang.
        float cliffDrop = Mathf.Max(roadY - 2.0f, 3f);
        float cliffRun = Mathf.Max(7f, Mathf.Lerp(0.28f, 1.35f, cv) * cliffDrop + jit * 0.6f);
        float beachWidth = Mathf.Lerp(1.5f, 34f, cv * cv);
        float footY = Mathf.Lerp(2.6f, 0.9f, cv);

        float oCliffTop = CliffTopOffset;
        float oFoot = oCliffTop - cliffRun;
        float oWater = oFoot - beachWidth;

        // --- inland (positive offsets) -------------------------------------------------------
        off[NodeCliffTop] = oCliffTop; hgt[NodeCliffTop] = roadY - 1.6f;
        off[10] = -9.6f; hgt[10] = roadY - 0.25f;
        off[11] = -8.0f; hgt[11] = roadY - 0.05f;
        off[12] = 8.0f; hgt[12] = roadY - 0.05f;
        off[13] = 9.8f; hgt[13] = roadY - 0.18f;
        off[14] = 17f; hgt[14] = roadY + 2.6f + InlandRelief(s, 17f);
        off[15] = 40f; hgt[15] = roadY + 11f + InlandRelief(s, 40f);
        off[16] = 95f; hgt[16] = roadY + 30f + InlandRelief(s, 95f);
        off[17] = 210f; hgt[17] = roadY + 74f + InlandRelief(s, 210f);

        // --- the cliff face (three rows so it can bulge, not just be a flat plane) ------------
        off[8] = Mathf.Lerp(oCliffTop, oFoot, 0.30f);
        hgt[8] = Mathf.Lerp(roadY - 1.6f, footY, 0.20f);
        off[7] = Mathf.Lerp(oCliffTop, oFoot, 0.68f);
        hgt[7] = Mathf.Lerp(roadY - 1.6f, footY, 0.62f);
        off[NodeBeachBack] = oFoot; hgt[NodeBeachBack] = footY;

        // --- beach / shore -------------------------------------------------------------------
        off[5] = Mathf.Lerp(oFoot, oWater, 0.55f);
        hgt[5] = Mathf.Lerp(footY, 0.35f, 0.62f);
        off[NodeShoreline] = oWater; hgt[NodeShoreline] = 0.18f;

        // --- sea floor (absolute, never follows the road) ------------------------------------
        off[3] = oWater - 14f - 22f * cv; hgt[3] = -1.6f;
        off[2] = oWater - 48f - 40f * cv; hgt[2] = -6.5f;
        off[1] = oWater - 130f; hgt[1] = -15f;
        off[0] = oWater - 300f; hgt[0] = -30f;

        // --- inland chapters: blend the seaward half into a mountain valley wall --------------
        // See Seaness(). Nodes 0-8 are the coastal story (cliff, beach, sea floor); on the
        // Mountain Gateway and Upper Switchbacks that story is simply not true, so the same
        // nodes are re-aimed at a hillside falling away to a valley floor that stays above sea
        // level. Blending (rather than switching) keeps the Hydrangea Descent's arrival at the
        // water continuous - a hard switch would tear the ribbon at one station.
        if (sea < 0.999f)
        {
            // A valley floor that tracks the road's own height, so the drop stays plausible at
            // 30 m and at 420 m instead of opening a 400 m chasm under an alpine hairpin.
            float valleyFloorY = Mathf.Max(SeaLevelY + 5f, roadY - Mathf.Lerp(24f, 120f, Mathf.Clamp01(roadY / 420f)));
            for (int n = 0; n <= 8; n++)
            {
                float run = Mathf.Abs(MountainOffset(n)) - Mathf.Abs(oCliffTop);
                float mOff = MountainOffset(n);
                // Near the road the hillside is a VALLEY WALL and is floored, so an alpine
                // hairpin does not open a chasm beside the carriageway. Past ValleyReachM it is
                // the mountain's FLANK, which on this route genuinely runs down to the sea - so
                // the floor is released and the profile is allowed to pass below the ocean
                // plane. Interpolating between the two laws (rather than switching) keeps the
                // profile continuous, which matters because a step here reads as a terrace.
                const float ValleyReachM = 40f;     // PROVISIONAL: end of the "valley wall" zone
                const float FlankReachM = 175f;     // PROVISIONAL: fully "mountain flank" by here
                const float SubmergedY = -14f;      // PROVISIONAL: ribbon edge depth
                float walled = Mathf.Max(valleyFloorY, roadY - 1.6f - 0.46f * Mathf.Max(0f, run));
                float flank = Mathf.Min(walled, SeaLevelY + SubmergedY);
                float t = Mathf.Clamp01((run - ValleyReachM) / (FlankReachM - ValleyReachM));
                // CRAG NOISE. Between the road (150 m up on the Gateway climb) and the water
                // there is only ~190 m of reach, so this flank IS a cliff - that is correct for
                // a "Mountain Gateway" chapter and cannot be flattened away. What made it read
                // as a grey CARD rather than rock was that it was perfectly planar and its top
                // edge was a dead-straight cut. The relief term is therefore no longer faded
                // out on the flank (the old "* (1 - t)"), and the outer nodes get an extra
                // chainage-keyed wobble in BOTH axes so the silhouette breaks up into buttresses.
                float crag = (1f - Mathf.Abs(1f - 2f * t)) * 16f *
                             Mathf.Sin(s * 0.0087f + n * 1.7f) *
                             (0.6f + 0.4f * Mathf.Sin(s * 0.0231f + n * 0.9f));
                float mHgt = Mathf.Lerp(walled, flank, t)
                             + InlandRelief(s, mOff) * Mathf.Clamp01(run / 90f) * (1f - 0.45f * t)
                             + crag;
                mOff *= 1f + 0.17f * Mathf.Sin(s * 0.0041f + n * 2.1f) * Mathf.Clamp01(run / 60f);
                off[n] = Mathf.Lerp(mOff, off[n], sea);
                hgt[n] = Mathf.Lerp(mHgt, hgt[n], sea);
            }
        }

        // --- the Red Bridge estuary ----------------------------------------------------------
        // A bridge over grass is not a bridge. The Kawaguchi-style coral span at SC_KM_215 needs
        // a tidal inlet under it, so the WHOLE cross-section (verge nodes included - the road
        // deck is its own mesh and stays up on the bridge) is cut down below sea level inside a
        // chainage window narrower than the span. The global ocean plane at y = 0 then shows
        // through the cut, which is the water. Cutting by CHAINAGE gives a channel that crosses
        // the road at right angles - exactly a river mouth - for free.
        {
            float es = EstuaryChainage(r);
            float du = Mathf.Abs(s - es);
            if (du < EstuaryHalfWidthM + EstuaryBankM)
            {
                float w = Smooth01(1f - Mathf.Clamp01((du - EstuaryHalfWidthM) / EstuaryBankM));
                for (int n = 0; n < NodeCount; n++)
                {
                    // Banks shelve: the channel is deepest mid-stream and shallower up the
                    // inland arm, so the inlet reads as a river mouth rather than a trench.
                    float floorY = SeaLevelY - EstuaryDepthM *
                                   Mathf.Clamp01(1f - Mathf.Max(0f, off[n] - 20f) / 150f);
                    hgt[n] = Mathf.Lerp(hgt[n], Mathf.Min(hgt[n], floorY), w);
                }
            }
        }

        // --- curvature clamp -------------------------------------------------------------
        // Every station's cross-section is swept perpendicular to the centreline as if the
        // road were straight. At the Torii Overlook switchback (~1.18 km, ~34 m turn radius)
        // the inland nodes reach out to 210 m - almost 5x that radius - so on the CONCAVE side
        // of the bend consecutive stations' far offsets cross past each other and the ribbon
        // folds back over itself. The fold's underside faces away from the sun, which is why
        // it rendered as a dark, un-lit wedge floating over the correct green hillside instead
        // of an obvious hole.
        //
        // IMPORTANT: this must SCALE every node on the concave side by the same factor, not
        // clamp each one independently to the same ceiling. An earlier version of this fix did
        // clamp per-node, which fixed the self-crossing fold but on a second, gentler switchback
        // it instead collapsed nodes 15/16/17 (offsets 40/95/210 m, but roadY+11/+30/+74 m
        // APART in height) down onto nearly the same clamped X position - trading a folded
        // ribbon for a near-vertical cliff wall of the same height in a couple of metres of run,
        // which read as exactly the same kind of dark floating shard. Scaling preserves the
        // nodes' relative spacing (and therefore the profile's slope), just shrunk to fit inside
        // the local turn radius.
        // Snapshot the UNCLAMPED profile. When the clamp below shrinks a side's run, the new
        // heights are resampled from this curve at the new offsets (see ResampleProfile) rather
        // than scaled toward roadY. Scaling the rise toward the road was what lifted the sea
        // floor (y = -30) up to road height on the Gateway's switchbacks, producing the grey
        // terraced "floating island" hanging over the water: the band kept its full width in
        // triangles but was dragged 150 m into the air. Resampling instead TRUNCATES the
        // profile - the far nodes bunch up near the shoreline at shoreline height and go
        // degenerate (invisible), which is the correct reading: on a tight inland switchback
        // there simply is no room for 300 m of sea floor.
        var srcOff = new float[NodeCount]; var srcHgt = new float[NodeCount];
        System.Array.Copy(off, srcOff, NodeCount); System.Array.Copy(hgt, srcHgt, NodeCount);

        CurvatureLimits(r, i, out float posLimit, out float negLimit);
        // The proximity limit applies to BOTH sides - a hairpin's neighbour can be on either
        // hand - so it is folded in here rather than inside CurvatureLimits, which is one-sided
        // by construction.
        float prox = ProximityLimit(r, i);
        posLimit = Mathf.Min(posLimit, prox);
        negLimit = Mathf.Min(negLimit, prox);
        if (posLimit < float.MaxValue)
        {
            float maxPos = 0f;
            for (int n = 0; n < NodeCount; n++) if (off[n] > maxPos) maxPos = off[n];
            if (maxPos > posLimit)
            {
                // Scale only the part of each offset OUTSIDE the protected road corridor. See
                // CorridorProtectM: scaling from zero collapsed the verge onto the carriageway.
                float scale = (posLimit - CorridorProtectM) / (maxPos - CorridorProtectM);
                for (int n = 0; n < NodeCount; n++)
                {
                    if (off[n] <= CorridorProtectM) continue;
                    off[n] = CorridorProtectM + (off[n] - CorridorProtectM) * scale;
                    // Scaling ONLY the run (offset) while leaving the rise (height above the
                    // road) unchanged turns a gentle inland hillside into a near-vertical wall
                    // once the run is shrunk to fit inside a tight turn radius - e.g. node 17
                    // (210 m out, roadY+74 m up) scaled to ~28 m of run still carries its full
                    // 74 m of rise, a ~70 degree face. That face's normal points away from the
                    // sun and it renders as the exact same dark floating wedge this clamp is
                    // meant to remove, just relocated to the clamped nodes instead of a folded
                    // seam. Scaling the rise above the road by the same factor keeps the
                    // profile's slope ANGLE intact - it becomes a smaller hill, not a steeper
                    // wall - which is what actually fixes the switchback's silhouette.
                    hgt[n] = ResampleProfile(srcOff, srcHgt, off[n]);
                }
                // EDGE SKIRT. See the seaward branch below for the full reasoning; the inland
                // lip is shallower because a neighbouring stretch's ribbon usually covers it.
                hgt[NodeCount - 1] = Mathf.Min(hgt[NodeCount - 1],
                                               hgt[NodeCount - 2] - EdgeSkirtInlandM);
            }
        }
        if (negLimit < float.MaxValue)
        {
            float maxNeg = 0f;
            for (int n = 0; n < NodeCount; n++) if (-off[n] > maxNeg) maxNeg = -off[n];
            if (maxNeg > negLimit)
            {
                float scale = (negLimit - CorridorProtectM) / (maxNeg - CorridorProtectM);
                for (int n = 0; n < NodeCount; n++)
                {
                    if (-off[n] <= CorridorProtectM) continue;
                    off[n] = -(CorridorProtectM + (-off[n] - CorridorProtectM) * scale);
                    // switchback ladder produced once the proximity clamp began pulling this
                    // side in to ~20 m. Heights come from the unclamped profile resampled at the
                    // new offset - see the srcOff/srcHgt snapshot above.
                    hgt[n] = ResampleProfile(srcOff, srcHgt, off[n]);
                }
                // EDGE SKIRT - the fix for the floating tree ribbon on the ch1 Gateway render.
                //
                // Unclamped, the seaward profile runs all the way down to SubmergedY (-14 m) or
                // to the sea floor, so the ribbon's outer edge is BELOW the ocean plane and is
                // never seen. Clamping truncates the run, and resampling then leaves that outer
                // edge at whatever height the profile had at the new, much smaller offset -
                // typically valley-floor height, i.e. 100+ m in the air on the Gateway climb.
                // Swept along the route that becomes a thin, edge-on shelf hanging over the sea
                // with the roadside scatter apparently floating on it. Dropping ONLY the
                // outermost node turns that cut edge into a vertical skirt that falls out of
                // frame (and, wherever the ribbon is over water, below the ocean plane), so the
                // silhouette ends in rock or in sea instead of in a cut card. The offset is left
                // alone on purpose: a skirt must not widen the ribbon back out past the limit
                // the clamp just imposed.
                // EDGE APRON - the fix for the floating switchback ladder on the ch1 Gateway
                // render (a zoom of that frame shows seven parallel grass-and-tree shelves
                // cantilevered out over the sea, which is the Upper Switchbacks' own terrain
                // ribbon seen from below).
                //
                // Unclamped, the seaward profile runs down to SubmergedY / the sea floor, so the
                // ribbon's outer edge is never seen. The proximity clamp shrinks that run to
                // ~20 m on a hairpin, and resampling then leaves the edge FLAT at valley-floor
                // height - a lit horizontal shelf with nothing under it. Since the legs of a
                // hairpin stack are only tens of metres apart horizontally but ~30-60 m apart
                // vertically, the only honest ground between them is a near-vertical rock face,
                // so that is what is built here: the three outermost nodes are fanned downward
                // into an apron that falls past the leg below.
                //
                // It is deliberately built from THREE nodes with per-node lateral wobble and
                // chainage-keyed crag noise rather than as a single drop of node 0. A one-quad
                // skirt is planar and untextured-looking, and rendered as a flat grey slab
                // standing in silhouette against the sea - visually worse than the sliver it
                // replaced. Three broken facets read as the continuation of the cliff.
                if (sea < 0.5f)
                {
                    // SEAWARD CLIFF COLLAPSE (inland switchbacks) - the robust fix for the ch1
                    // Gateway "floating island" shelves.
                    //
                    // On a mountain hairpin the seaward clamp shrinks this side's run to ~20 m,
                    // and resampling the truncated profile leaves nodes 0-8 bunched at valley-
                    // floor height as a STACK OF FLAT, LIT HORIZONTAL SHELVES - the "floating
                    // island" that was still visible on the left of the ch1 Gateway render.
                    // Dropping only the outer three nodes (the old fanned apron) left those inner
                    // shelves intact, which is why the artifact survived every apron tune.
                    //
                    // Rebuild the WHOLE seaward flank as one continuous rock face that falls from
                    // just under the clifftop straight down to the waterline, so no horizontal
                    // shelf survives to catch the light. The FULL drop is carried by the CLIFF-FACE
                    // band (nodes 6-9, rock material); the beach and sea-floor bands (nodes 0-5)
                    // are pushed BELOW the ocean plane so they are hidden by the water instead of
                    // being hoisted up the cliff and painted their sand/teal colours - that
                    // hoisted sea-floor band was the residual "flat teal triangular facet" under
                    // the ch1 cliff. Heights are SET (not min'd) to obliterate the resampled
                    // shelf; the face steepens with depth (u*u) so it reads as a cliff rather than
                    // a ramp, and carries two-octave crag noise (faded in from the clifftop so the
                    // join stays tight) so it is broken rock, not a flat grey slab. Offsets are
                    // left on their clamped, monotone-seaward positions - a face this steep needs
                    // no extra width, and re-ordering them would tear the ribbon.
                    float topH = hgt[NodeCliffTop];
                    float footH = SeaLevelY + 1.5f;   // foot of the rock face, just above the water
                    // Cliff-face band (nodes 6..8) drops from the clifftop to the waterline.
                    for (int n = NodeCliffTop - 1; n >= NodeBeachBack; n--)   // 8,7,6
                    {
                        float u = (float)(NodeCliffTop - n) / (NodeCliffTop - NodeBeachBack); // 0 top .. 1 foot
                        float crag = (10f * Mathf.Sin(s * 0.0093f + n * 2.3f) +
                                      5f * Mathf.Sin(s * 0.0331f + n * 1.1f)) * u;
                        hgt[n] = Mathf.Lerp(topH, footH, u * u) + crag;
                    }
                    // Beach + sea-floor bands (nodes 5..0) sink monotonically below the ocean
                    // plane so the sand/teal terrain is submerged and never seen inland.
                    for (int n = NodeBeachBack - 1; n >= 0; n--)             // 5,4,3,2,1,0
                    {
                        hgt[n] = SeaLevelY - 4f - (NodeBeachBack - 1 - n) * 3f;
                    }
                }
                else
                {
                    float edgeY = hgt[2];
                    float edgeOff = off[2];
                    for (int n = 0; n <= 2; n++)
                    {
                        float f = (2 - n) * 0.5f;                 // 0 at node 2, 1 at node 0
                        float crag = 11f * Mathf.Sin(s * 0.0093f + n * 2.3f) * f;
                        hgt[n] = Mathf.Min(hgt[n], edgeY - f * EdgeApronDropM + crag);
                        off[n] = edgeOff - f * (3f + 5f * Mathf.Sin(s * 0.0147f + n * 1.1f));
                    }
                }
            }
        }
    }

    /// <summary>
    /// How far the outermost cross-section node is dropped below its neighbour when the
    /// curvature/proximity clamp has truncated that side's run. Big enough that the resulting
    /// face is effectively vertical and reads as the continuation of the cliff rather than as a
    /// cut edge. PROVISIONAL.
    ///
    /// 60 m was too much: it turned the truncated ribbon edge into a 60 m flat grey slab that
    /// stood in silhouette against the sea on the ch1 Gateway render - a worse artifact than the
    /// sliver it replaced, because a big untextured face reads as a wall. With the scatter cull
    /// (see OnRibbon) removing the floating trees, the skirt only has to roll the cut edge over
    /// so it is not a lit horizontal shelf; 9 m does that and stays under the treeline.
    /// </summary>
    private const float EdgeSkirtSeawardM = 9f;

    /// <summary>Inland counterpart of <see cref="EdgeSkirtSeawardM"/>. PROVISIONAL.</summary>
    private const float EdgeSkirtInlandM = 7f;

    /// <summary>
    /// Total fall of the three-facet seaward apron built at a clamped ribbon edge. Has to clear
    /// the vertical spacing of a hairpin's stacked legs (~30-60 m on the Gateway climb) with
    /// margin, or the shelves still read as separate floating terraces. PROVISIONAL.
    /// </summary>
    private const float EdgeApronDropM = 110f;

    /// <summary>
    /// Samples an (offset -> height) cross-section polyline at an arbitrary offset. The node
    /// arrays are monotonic in offset (node 0 is the far seaward edge, node 17 the far inland
    /// one), so this is a plain piecewise-linear lookup with clamped ends. Used by the curvature
    /// clamp so shrinking a side's run truncates the profile instead of steepening it.
    /// </summary>
    private static float ResampleProfile(float[] o, float[] h, float x)
    {
        if (x <= o[0]) return h[0];
        if (x >= o[NodeCount - 1]) return h[NodeCount - 1];
        for (int k = 1; k < NodeCount; k++)
        {
            if (x > o[k]) continue;
            float span = o[k] - o[k - 1];
            if (span <= 1e-5f) return h[k];
            return Mathf.Lerp(h[k - 1], h[k], (x - o[k - 1]) / span);
        }
        return h[NodeCount - 1];
    }

    /// <summary>
        /// How far a cross-section may safely reach on its concave side before the swept ribbon
    /// folds over itself at this station's local turn radius. Returns <see cref="float.MaxValue"/>
    /// for the convex side (and for both sides on straight or gently curved ground), so this is a
    /// no-op everywhere except tight switchbacks.
    /// </summary>
    private static void CurvatureLimits(CoastRoute r, int i, out float posLimit, out float negLimit)
    {
        posLimit = float.MaxValue; negLimit = float.MaxValue;
        // A NARROW window (immediate neighbours, not a several-station smoothing span) is
        // required here: the ribbon actually folds between ADJACENT swept rows (TerrainStride
        // apart, a couple of metres), so a wide window averages away exactly the sharp, local
        // kink that causes it - that under-estimate of curvature was why the first, wider-window
        // version of this clamp still let a second, gentler-looking switchback (~1.9-2.0 km, R
        // ~100-120 m smoothed) fold on its concave side even though the ~34 m hairpin it was
        // written for was fixed.
        int a = Mathf.Max(0, i - 1), b = Mathf.Min(r.Count - 1, i + 1);
        if (b <= a) return;

        var ta = r.Tangent[a]; var tb = r.Tangent[b];
        float angA = Mathf.Atan2(ta.x, ta.z) * Mathf.Rad2Deg;
        float angB = Mathf.Atan2(tb.x, tb.z) * Mathf.Rad2Deg;
        float dtheta = Mathf.Abs(Mathf.DeltaAngle(angA, angB)) * Mathf.Deg2Rad;
        float ds = Mathf.Max(0.5f, r.Distance[b] - r.Distance[a]);
        if (dtheta < 1e-4f) return;   // effectively straight - no clamp needed

        float radius = ds / dtheta;
        // 0.6x margin (tightened from an initial 0.85x, which still let a second, gentler
        // switchback fold): a generous safety gap is cheap here (it just narrows the green
        // band a little further inland on a tight bend) whereas being too permissive
        // reproduces the exact floating-wedge defect this clamp exists to prevent.
        //
        // FLOORED at the protected road corridor plus a band: the clamp may shrink the distant
        // hillside as hard as the turn radius demands, but it may never reach in past the
        // carriageway's own verges (see CorridorProtectM for what that cost).
        float safe = Mathf.Max(CorridorProtectM + CorridorBandM, radius * 0.6f);

        // (tb - ta) points toward the turning centre, i.e. into the concave side. Whichever
        // signed offset direction that lines up with is the side that needs clamping.
        var curveDir = new Vector3(tb.x - ta.x, 0f, tb.z - ta.z);
        if (curveDir.sqrMagnitude < 1e-8f) return;
        if (Vector3.Dot(curveDir.normalized, r.SideFlat(i)) > 0f) posLimit = safe;
        else negLimit = safe;
    }

    /// <summary>
    /// Cache of "how far is the nearest OTHER part of the road, in plan", per route sample.
    /// </summary>
    private static CoastRoute _proxRoute;
    private static float[] _proximity;

    /// <summary>
    /// Half the plan distance to the nearest part of the route that is far away ALONG the road -
    /// i.e. how far this station's cross-section may reach before it starts burying a different
    /// stretch of the same road.
    ///
    /// ROOT CAUSE OF THE "CH2 SWITCHBACKS ARE A WALL OF GRASS" DEFECT. Every station sweeps its
    /// cross-section out to 210 m inland and 320 m seaward. That was harmless while Shiosai was
    /// a 2.892 km coast road whose tightest feature was a ~34 m bend, because no two parts of the
    /// road were ever within 400 m of each other. The Grand Coast's Upper Switchbacks are a
    /// ladder of twelve 21 m hairpins whose legs sit 2R = 42 m apart, so each leg's hillside was
    /// swept straight over the leg above and the leg below - the road was still exactly where the
    /// RouteGraph said it was, but it was underneath a couple of hundred metres of someone else's
    /// terrain, which is why a rider's-eye capture at 8.5 km showed nothing but a grass bank.
    ///
    /// <see cref="CurvatureLimits"/> cannot catch this: it reasons about a single station's local
    /// turn radius, and on a switchback LEG the road is nearly straight, so it correctly reports
    /// "no clamp needed" while the leg 42 m away is about to be buried.
    ///
    /// Returns <see cref="float.MaxValue"/> wherever the road is comfortably on its own.
    /// </summary>
    private static float ProximityLimit(CoastRoute r, int i)
    {
        if (!ReferenceEquals(_proxRoute, r) || _proximity == null || _proximity.Length != r.Count)
        {
            _proxRoute = r;
            _proximity = new float[r.Count];

            // Uniform bucket grid: the naive all-pairs scan is 14,000^2 and this runs per build.
            const float Cell = 64f;
            const float IgnoreSpanM = 90f;   // same stretch of road, not a different one
            var grid = new Dictionary<long, List<int>>();
            for (int k = 0; k < r.Count; k++)
            {
                var q = r.Position[k];
                long key = ((long)Mathf.FloorToInt(q.x / Cell) << 32) ^
                           (uint)Mathf.FloorToInt(q.z / Cell);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(k);
            }

            int span = Mathf.CeilToInt(MaxReachM / Cell);
            for (int k = 0; k < r.Count; k++)
            {
                var q = r.Position[k];
                int cx = Mathf.FloorToInt(q.x / Cell), cz = Mathf.FloorToInt(q.z / Cell);
                float best = float.MaxValue;
                for (int ax = -span; ax <= span; ax++)
                for (int az = -span; az <= span; az++)
                {
                    long key = ((long)(cx + ax) << 32) ^ (uint)(cz + az);
                    if (!grid.TryGetValue(key, out var list)) continue;
                    foreach (int j in list)
                    {
                        float along = Mathf.Abs(r.Distance[j] - r.Distance[k]);
                        if (along < IgnoreSpanM) continue;
                        var o = r.Position[j];
                        float d2 = (o.x - q.x) * (o.x - q.x) + (o.z - q.z) * (o.z - q.z);
                        if (d2 >= best) continue;
                        // DETOUR RATIO, not raw separation. A fixed "more than 90 m along the
                        // road" test is satisfied by the road AHEAD: on a straight, the sample
                        // 90 m further on is also 90 m away in plan, so the nearest "other"
                        // stretch is always ~90 m and the clamp fired at ~43 m EVERYWHERE. That
                        // silently starved the distant treeline (which needs ~120 m of hillside)
                        // to zero silhouettes across the whole 42 km while looking like a
                        // switchback fix. Requiring the road to travel several times the plan
                        // distance to get there is what actually distinguishes a hairpin's
                        // neighbouring leg from the road just ahead.
                        float plan = Mathf.Sqrt(d2);
                        if (along < plan * DetourRatio) continue;
                        best = d2;
                    }
                }
                _proximity[k] = best == float.MaxValue ? float.MaxValue : Mathf.Sqrt(best);
            }

            // SMOOTH THE LIMIT ALONG THE ROAD.
            //
            // The raw per-station nearest-other-leg distance changes abruptly: one station is
            // fouled by a neighbouring switchback and shrinks to ~20 m of reach while the one
            // 3 m behind it still reaches 190 m. Swept into a ribbon that produces a razor-thin
            // SLIVER - a near-vertical, edge-on triangle fan poking out over the sea, with the
            // roadside scatter that was seated on it apparently floating in mid-air. That is
            // exactly the artifact still visible on the left of the ch1 Gateway render.
            // Taking a running MINIMUM over a window makes neighbouring stations shrink
            // together, so the ribbon narrows as a smooth taper instead of a spike.
            const int ProxSmoothStations = 64;   // PROVISIONAL: ~190 m of road at 3 m sampling
            var smoothed = new float[r.Count];
            for (int k = 0; k < r.Count; k++)
            {
                float m = float.MaxValue;
                int lo = Mathf.Max(0, k - ProxSmoothStations);
                int hi2 = Mathf.Min(r.Count - 1, k + ProxSmoothStations);
                for (int j = lo; j <= hi2; j++) if (_proximity[j] < m) m = _proximity[j];
                smoothed[k] = m;
            }
            _proximity = smoothed;
        }

        float near = _proximity[i];
        if (near == float.MaxValue || near >= MaxReachM * 2f) return float.MaxValue;
        // Half the gap, so the two stretches' ribbons meet in the middle rather than overlapping,
        // with a small margin so they do not z-fight along the seam.
        return Mathf.Max(CorridorProtectM + CorridorBandM, near * 0.47f);
    }

    /// <summary>Widest lateral reach any cross-section node asks for (node 0, seaward).</summary>
    private const float MaxReachM = 340f;

    /// <summary>
    /// How much further the ROAD must travel than the straight-line gap before two stretches count
    /// as separate stretches rather than "the road just ahead". 3.0 comfortably excludes a normal
    /// bend (ratio ~1.0-1.4) and comfortably includes a hairpin's neighbouring leg (ratio ~14).
    /// PROVISIONAL.
    /// </summary>
    private const float DetourRatio = 3.0f;

    private static readonly float[] _off = new float[NodeCount];
    private static readonly float[] _hgt = new float[NodeCount];
    private static readonly float[] _offB = new float[NodeCount];
    private static readonly float[] _hgtB = new float[NodeCount];

    /// <summary>Terrain height at an arbitrary cross-section offset - used to seat props.</summary>
    private static float SurfaceHeight(CoastRoute r, int i, float offset)
    {
        CrossSection(r, i, _off, _hgt);
        if (offset <= _off[0]) return _hgt[0];
        for (int n = 1; n < NodeCount; n++)
        {
            if (offset > _off[n]) continue;
            float t = Mathf.InverseLerp(_off[n - 1], _off[n], offset);
            return Mathf.Lerp(_hgt[n - 1], _hgt[n], t);
        }
        return _hgt[NodeLast];
    }

    /// <summary>The land ribbon's row stride - <see cref="Ribbon"/> sweeps every Nth station.</summary>
    private static int RibbonStride => Mathf.Max(1, (int)TerrainStride);

    /// <summary>
    /// Terrain height AS THE LAND MESH ACTUALLY RENDERS IT at station <paramref name="i"/>.
    ///
    /// FLOATING-PROP ROOT CAUSE #2. <see cref="SurfaceHeight"/> evaluates the exact per-station
    /// cross-section, but <see cref="Ribbon"/> only sweeps every <see cref="TerrainStride"/>th
    /// station and linearly interpolates between those rows. On a station the ribbon skipped -
    /// half of them - the analytic profile and the rendered triangle disagree, which on the
    /// coast's shaped dips and rollers is centimetres on a straight and a good deal more at a
    /// bend. Every prop seated with the analytic value therefore hovered or sank by that
    /// difference. Props are now seated on the SAME interpolation the mesh is built from.
    /// </summary>
    private static float MeshSurfaceHeight(CoastRoute r, int i, float offset)
    {
        int stride = RibbonStride;
        int last = r.Count - 1;
        int r0 = Mathf.Min((i / stride) * stride, last);
        int r1 = Mathf.Min(r0 + stride, last);
        if (r1 == r0) return SurfaceHeight(r, r0, offset);
        float f = Mathf.Clamp01((float)(i - r0) / (r1 - r0));
        return Mathf.Lerp(SurfaceHeight(r, r0, offset), SurfaceHeight(r, r1, offset), f);
    }

    /// <summary>
    /// The offsets the land ribbon ACTUALLY reaches at this station: seaward (negative) and
    /// inland (positive). Outside this span there is no terrain at all.
    ///
    /// FLOATING-PROP ROOT CAUSE #1. <see cref="SurfaceHeight"/> extrapolates by returning the
    /// outermost node's height for any offset past the profile, so it answers a height for
    /// ground that does not exist. The distant treeline asked for 210-390 m inland while node 17
    /// - the ribbon's outer edge - sits at 210 m, so most of a whole extra treeline was planted
    /// in mid-air at ridge height; and on a clamped switchback the roadside scatter's 16-94 m
    /// band could likewise reach past the shortened profile. Callers clamp against this.
    /// </summary>
    private static void TerrainSpan(CoastRoute r, int i, out float seaward, out float inland)
    {
        CrossSection(r, i, _offB, _hgtB);
        seaward = _offB[0];
        inland = _offB[NodeLast];
    }

    /// <summary>Clamps a cross-section offset into ground that really exists, with a margin.</summary>
    private static float ClampToTerrain(CoastRoute r, int i, float offset, float marginM = 4f)
    {
        TerrainSpan(r, i, out float seaward, out float inland);
        return Mathf.Clamp(offset, seaward + marginM, inland - marginM);
    }

    private static Vector3 PointAt(CoastRoute r, int i, float offset)
    {
        var p = r.Position[i];
        var s = r.SideFlat(i);
        return new Vector3(p.x + s.x * offset, MeshSurfaceHeight(r, i, offset), p.z + s.z * offset);
    }

    /// <summary>Seats a prop on real ground: the offset is clamped into the ribbon's own span
    /// first, then the height is read off the mesh interpolation rather than the analytic
    /// profile. Everything scattered on the hillside goes through this.</summary>
    private static Vector3 GroundedPoint(CoastRoute r, int i, float offset, float marginM = 4f)
    {
        return PointAt(r, i, ClampToTerrain(r, i, offset, marginM));
    }

    // --------------------------------------------------------------- land / ocean

    private static void BuildLand(Transform root, CoastRoute route)
    {
        var group = new GameObject("Coast Landform").transform;
        group.SetParent(root, false);

        // Four bands so the coast reads as sea floor -> beach -> rock cliff -> green headland
        // without needing the splat terrain shader's second UV channel.
        var seafloor = Ribbon(route, 0, NodeShoreline, "Shiosai_SeaFloor");
        var beach = Ribbon(route, NodeShoreline, NodeBeachBack, "Shiosai_Beach");
        var cliff = Ribbon(route, NodeBeachBack, NodeCliffTop, "Shiosai_CliffFace");
        var green = Ribbon(route, NodeCliffTop, NodeLast, "Shiosai_Headland");

        AddMesh(group, "Coast Sea Floor", seafloor,
                ShoreTerrainMaterial("Shiosai_SeaFloor"),
                collider: false);
        AddMesh(group, "Coast Beach", beach,
                ShoreTerrainMaterial("Shiosai_Sand"),
                collider: true);
        AddMesh(group, "Coast Cliff Face", cliff,
                CliffRockMaterial(),
                collider: true);
        // GREEN. The single most important colour note separating Shiosai from Sakura Pass.
        AddMesh(group, "Coast Headland", green, HeadlandMaterial(), collider: true);
        // The turquoise shelf sheet is RETIRED under the HDRP Water System: the near-shore
        // turquoise is now a real absorption result computed against this very sea floor
        // (see OceanAbsorptionDistanceM), and keeping the sheet would put a second translucent
        // water surface 7 cm above the first - the exact double-water/z-fight the milestone
        // set out to remove.
        if (!UseHdrpWaterSystem) BuildShallows(group, route);
    }

    // ------------------------------------------------------- start apron (opening 0-200 m)

    // How far back behind route sample 0 the land + tarmac are carried. PROVISIONAL: sized off
    // the start-line diagnostic cameras, whose furthest eye stands 10 m behind arc 0 and whose
    // 55 deg lens still sees ground ~60 m back. 90 m leaves margin at the frame edges.
    private const float StartApronReachM = 90f;
    private const int StartApronRows = 14;   // PROVISIONAL: mesh density behind the line

    /// <summary>
    /// Land (and tarmac) BEHIND the start line.
    ///
    /// ROOT CAUSE OF THE "FLAT TEAL WATER PLANE OVER THE START LINE" DEFECT. Every landform
    /// ribbon and the carriageway are swept with <c>for (i = 0; i &lt; route.Count; ...)</c>, so
    /// their first row is EXACTLY route sample 0 - the start line. There is no geometry at all
    /// behind it. The start-line diagnostic cameras, however, stand 6.5 m and 10 m BEHIND arc 0,
    /// which puts the whole lower half of those frames over a hole in the world.
    ///
    /// What shows through that hole is <see cref="BuildOcean"/>'s sheet: a 29 x 43 km plane at
    /// y = 0 that is drawn everywhere in the region. Arc 0 sits at y = 62 m, so the sea is 62 m
    /// BELOW the road - but seen through a hole directly under the lens it fills the frame as a
    /// flat teal field with a specular blob in it, and reads as water floating above the road.
    ///
    /// It was never a mis-placed water mesh: Shiosai_Ocean is correct at sea level, and
    /// Shiosai_Shallows is collapsed to zero width here because <see cref="Seaness"/>(0) == 0 in
    /// the inland Mountain Gateway chapter. The CONTROL is the third start render: the reverse
    /// camera stands 26 m AHEAD of arc 0, over real ground, and is the one shot with no teal
    /// foreground.
    ///
    /// The fix is to continue the ground backwards rather than to move any water. The arc-0
    /// cross-section is swept back along -tangent, so the seam at the start line is vertex-exact
    /// with the main ribbons' first row (same offsets, same heights, same splat weights) and no
    /// crack or material change is visible.
    /// </summary>
    private static void BuildStartApron(Transform root, CoastRoute route)
    {
        if (route.Count < 2) return;

        var group = new GameObject("Coast Start Apron").transform;
        group.SetParent(root, false);

        var p0 = route.Position[0];
        var t0 = new Vector3(route.Tangent[0].x, 0f, route.Tangent[0].z).normalized;
        var s0 = route.SideFlat(0);

        // Follow the road's own local grade backwards so the apron is a continuation of the
        // hillside rather than a flat table welded onto it. The Gateway climbs away from the
        // start, so the ground behind it must fall.
        float grade = (route.Position[1].y - p0.y) /
                      Mathf.Max(0.001f, route.Distance[1] - route.Distance[0]);

        var off = new float[NodeCount];
        var hgt = new float[NodeCount];
        CrossSection(route, 0, off, hgt);

        // --- land: the same four material bands as BuildLand, so the seam is invisible ---
        ApronBand(group, "Coast Start Apron Sea Floor", "Shiosai_StartApron_SeaFloor",
                  0, NodeShoreline, p0, t0, s0, off, hgt, grade,
                  ShoreTerrainMaterial("Shiosai_SeaFloor"), collider: false);
        ApronBand(group, "Coast Start Apron Beach", "Shiosai_StartApron_Beach",
                  NodeShoreline, NodeBeachBack, p0, t0, s0, off, hgt, grade,
                  ShoreTerrainMaterial("Shiosai_Sand"), collider: true);
        ApronBand(group, "Coast Start Apron Cliff", "Shiosai_StartApron_Cliff",
                  NodeBeachBack, NodeCliffTop, p0, t0, s0, off, hgt, grade,
                  CliffRockMaterial(), collider: true);
        ApronBand(group, "Coast Start Apron Headland", "Shiosai_StartApron_Headland",
                  NodeCliffTop, NodeLast, p0, t0, s0, off, hgt, grade,
                  HeadlandMaterial(), collider: true);

        // --- tarmac: the road the rider is standing on has to exist behind the line too ---
        float edge = RoadHalfWidth + ShoulderWidth;
        var lanes = new[] { -edge, -RoadHalfWidth, -RoadHalfWidth * 0.5f, 0f,
                            RoadHalfWidth * 0.5f, RoadHalfWidth, edge };
        var verts = new Vector3[StartApronRows * lanes.Length];
        var uvs = new Vector2[verts.Length];
        for (int r = 0; r < StartApronRows; r++)
        {
            float back = StartApronReachM * r / (StartApronRows - 1f);   // 0 at the line
            for (int c = 0; c < lanes.Length; c++)
            {
                float o = lanes[c];
                float y = SurfaceHeight(route, 0, o) + RoadSurfaceLiftM + CrownAt(o) - grade * back;
                var at = p0 - t0 * back;
                verts[r * lanes.Length + c] = new Vector3(at.x + s0.x * o, y, at.z + s0.z * o);
                // Negative v so the asphalt grain runs continuously through the start line.
                uvs[r * lanes.Length + c] = new Vector2(o / AsphaltTileM, -back / AsphaltTileM);
            }
        }
        var tris = new List<int>();
        for (int r = 0; r < StartApronRows - 1; r++)
        for (int c = 0; c < lanes.Length - 1; c++)
        {
            // Wound the opposite way to BuildRoad's sweep: the apron marches along -tangent, so
            // keeping BuildRoad's order here would face every triangle at the ground.
            int a = r * lanes.Length + c, b = a + 1;
            int d = (r + 1) * lanes.Length + c, e = d + 1;
            tris.Add(a); tris.Add(b); tris.Add(d);
            tris.Add(b); tris.Add(e); tris.Add(d);
        }
        AddMesh(group, "Coast Start Apron Carriageway",
                Finish("Shiosai_StartApron_Carriageway", verts, uvs, tris),
                // FIDELITY POC: same PBR asphalt as the carriageway. These two meshes MEET at
                // arc 0, so they must resolve to the identical material or the start line gets
                // a visible shading seam.
                PbrMaterial("Shiosai_Asphalt", "Shiosai_Asphalt", AsphaltTileM,
                            smoothness: 0.28f, normalScale: 1.15f,
                            tint: CoastAsphaltTint),
                collider: true);

        Debug.Log($"[shiosai] start apron: {StartApronReachM:0} m of land + tarmac carried behind " +
                  $"arc 0 at {p0}, grade {grade:0.000}.");
    }

    /// <summary>One material band of the start apron, swept backwards from the arc-0 section.</summary>
    private static void ApronBand(Transform group, string objectName, string meshName,
                                  int firstNode, int lastNode, Vector3 p0, Vector3 t0, Vector3 s0,
                                  float[] off, float[] hgt, float grade, Material mat, bool collider)
    {
        int cols = lastNode - firstNode + 1;
        var verts = new Vector3[StartApronRows * cols];
        var uvs = new Vector2[verts.Length];
        var uv2 = new Vector2[verts.Length];

        for (int r = 0; r < StartApronRows; r++)
        {
            float back = StartApronReachM * r / (StartApronRows - 1f);
            var at = p0 - t0 * back;
            for (int c = 0; c < cols; c++)
            {
                int n = firstNode + c;
                int v = r * cols + c;
                verts[v] = new Vector3(at.x + s0.x * off[n], hgt[n] - grade * back,
                                       at.z + s0.z * off[n]);
                uvs[v] = new Vector2(off[n] / 8f, -back / 8f);
                // Seaness(0) == 0: the start is the inland Mountain Gateway, so the bands take
                // their MOUNTAIN splat mix, exactly as the adjoining ribbon row does.
                uv2[v] = SplatWeights(n, Seaness(0f));
            }
        }

        var tris = new List<int>((StartApronRows - 1) * (cols - 1) * 6);
        for (int r = 0; r < StartApronRows - 1; r++)
        for (int c = 0; c < cols - 1; c++)
        {
            int a = r * cols + c, b = a + 1, d = (r + 1) * cols + c, e = d + 1;
            tris.Add(a); tris.Add(b); tris.Add(d);
            tris.Add(b); tris.Add(e); tris.Add(d);
        }

        AddMesh(group, objectName, Finish(meshName, verts, uvs, tris, uv2), mat, collider);
    }

    // Seaward extent of the turquoise shelf, measured out from the waterline. Provisional: the
    // concept renders show the band roughly as wide as the beach is deep, and 34 m reads right
    // from the clifftop road at ride speed.
    private const float ShallowsReachM = 85f;
    private const float ShallowsLiftM = 0.07f;   // above the ocean plane, to beat z-fighting

    /// <summary>
    /// The turquoise inshore shelf. A flat translucent sheet swept from the waterline out to
    /// <see cref="ShallowsReachM"/>, with the shore->sea gradient baked into VERTEX ALPHA and
    /// drawn by MapleRide/ShiosaiShallows (Shiosai-only; the shared water shader is untouched).
    ///
    /// Built here rather than in Blender because it has to follow the same live centreline the
    /// rest of the landform is swept along - an exported GLB would go stale the moment the route
    /// changed.
    /// </summary>
    /// <summary>
    /// Metres of water depth encoded by a full vertex-colour red channel on the shelf. Must match
    /// MapleRideShallows.shader's _DepthScale. PROVISIONAL.
    /// </summary>
    private const float ShallowsDepthScaleM = 20f;

    /// <summary>
    /// Depth at which the shelf has completely handed over to the ocean plane, metres. The shelf
    /// is swept out to a little beyond this so the fade always completes on geometry rather than
    /// being cut off at the mesh edge. PROVISIONAL - must be >= the shader's _FadeDepth.
    /// </summary>
    private const float ShallowsHandoverDepthM = 9f;

    private static void BuildShallows(Transform group, CoastRoute route)
    {
        // THE SHELF NOW FOLLOWS THE SEA FLOOR, NOT A FIXED WIDTH.
        //
        // It used to be a constant 85 m-wide strip whose gradient was a geometric ramp in vertex
        // alpha. That paints a stripe of the same width along 42 km of coast whatever the water
        // under it is doing, which is why the harbour overlook rendered a soft turquoise blob
        // with no relationship to the beach. The cross-section already publishes the real sea
        // floor (node 4 = waterline at +0.18 m, node 3 = -1.6 m, node 2 = -6.5 m, node 1 = -15 m,
        // node 0 = -30 m), so the shelf is swept over THOSE nodes and each vertex carries the
        // TRUE DEPTH of the water above the floor. The shader then does the whole gradient,
        // surf line and handover as a function of depth - so the turquoise pools where there is
        // a shelf and pinches out where a headland drops away, exactly as the reference shows.
        //
        // Columns are placed per sea-floor SEGMENT so the near-shore band (where all the colour
        // change happens) gets most of the resolution.
        int[] segNodes = { NodeShoreline, 3, 2, 1 };     // waterline -> -1.6 -> -6.5 -> -15 m
        int[] segSubdiv = { 5, 4, 3 };                    // columns added inside each segment
        int Cols = 1; foreach (var n in segSubdiv) Cols += n;

        var verts = new Vector3[route.Count * Cols];
        var uvs = new Vector2[verts.Length];
        var cols = new Color[verts.Length];
        var tris = new List<int>(route.Count * Cols * 6);
        var off = new float[NodeCount];
        var hgt = new float[NodeCount];

        for (int i = 0; i < route.Count; i++)
        {
            CrossSection(route, i, off, hgt);
            // Fade the inshore shelf out where the chapter is not coastal. In the Mountain
            // Gateway / Upper Switchbacks (Seaness ~ 0) there is no beach, so the flat turquoise
            // sheet laid at sea level under a 400 m cliff read as the "flat teal triangular
            // facet" under the ch1 Gateway cliff. Scaling the shelf width and its alpha by
            // Seaness collapses it to a zero-area, transparent strip inland.
            float seaF = Seaness(route.Distance[i]);
            var p = route.Position[i];
            var s = route.SideFlat(i);
            float waterline = off[NodeShoreline];

            int c = 0;
            for (int seg = 0; seg < segSubdiv.Length; seg++)
            {
                int nA = segNodes[seg], nB = segNodes[seg + 1];
                int steps = segSubdiv[seg];
                for (int k = 0; k < steps; k++, c++)
                {
                    float f = k / (float)steps;
                    float o = Mathf.Lerp(off[nA], off[nB], f);
                    float floorY = Mathf.Lerp(hgt[nA], hgt[nB], f);
                    WriteShallowVert(verts, uvs, cols, i * Cols + c, p, s, o, floorY, waterline,
                                     seaF, route.Distance[i]);
                }
            }
            // Closing column on the outermost node.
            {
                int nB = segNodes[segNodes.Length - 1];
                WriteShallowVert(verts, uvs, cols, i * Cols + c, p, s, off[nB], hgt[nB], waterline,
                                 seaF, route.Distance[i]);
            }
        }
        for (int i = 0; i < route.Count - 1; i++)
        for (int c = 0; c < Cols - 1; c++)
        {
            int a = i * Cols + c, b = a + 1, d = (i + 1) * Cols + c, e = d + 1;
            tris.Add(a); tris.Add(d); tris.Add(b);
            tris.Add(b); tris.Add(d); tris.Add(e);
        }

        var mesh = Finish("Shiosai_Shallows", verts, uvs, tris);
        mesh.colors = cols;
        var mat = LoadOrCreate("Shiosai_Shallows", "MapleRide/ShiosaiShallows");
        // OCEAN-REALISM MILESTONE. All PROVISIONAL look values.
        //
        // The palette is now a three-stop DEPTH gradient, read off ShiosaiCoast_06.png: pale
        // aqua over bare sand at the waterline, saturated turquoise over the 1-3 m shelf, and
        // the open sea's teal by ~7 m, where the shelf dissolves into the ocean plane.
        //
        // Values are authored LOW for the same reason the village walls are (see
        // VillageWallColours): the shelf multiplies its colour by the sky ambient x1.6 and adds
        // 0.35 of itself, so anything authored near 1.0 clips to white.
        if (mat.HasProperty("_DepthDriven")) mat.SetFloat("_DepthDriven", 1f);
        if (mat.HasProperty("_DepthScale")) mat.SetFloat("_DepthScale", ShallowsDepthScaleM);
        // FIRST RENDER CAME BACK NEON. The shelf pass multiplies albedo by ambient*1.6 (+0.35
        // self), so these authored values land roughly 2x brighter on screen; the first pass
        // clipped into an electric-cyan glow nothing like the reference's sunlit turquoise.
        // Roughly 0.65x across the board, and the depth stops pulled in so the turquoise band
        // hugs the sand instead of reaching 200 m offshore. All PROVISIONAL.
        if (mat.HasProperty("_SandColor")) mat.SetColor("_SandColor", new Color(0.30f, 0.56f, 0.55f, 1f));
        if (mat.HasProperty("_ShoreColor")) mat.SetColor("_ShoreColor", new Color(0.12f, 0.44f, 0.50f, 1f));
        if (mat.HasProperty("_ShelfColor")) mat.SetColor("_ShelfColor", new Color(0.04f, 0.24f, 0.42f, 1f));
        if (mat.HasProperty("_TurquoiseDepth")) mat.SetFloat("_TurquoiseDepth", 1.2f);
        if (mat.HasProperty("_ShelfDepth")) mat.SetFloat("_ShelfDepth", 4.5f);
        if (mat.HasProperty("_FadeDepth")) mat.SetFloat("_FadeDepth", ShallowsHandoverDepthM);
        if (mat.HasProperty("_SurfDepth")) mat.SetFloat("_SurfDepth", 1.15f);
        if (mat.HasProperty("_SurfAmount")) mat.SetFloat("_SurfAmount", 0.80f);
        if (mat.HasProperty("_GlitterBoost")) mat.SetFloat("_GlitterBoost", 0.55f);
        if (mat.HasProperty("_WaveStrength")) mat.SetFloat("_WaveStrength", 0.20f);
        if (mat.HasProperty("_FoamWidth")) mat.SetFloat("_FoamWidth", 0.10f);
        if (mat.HasProperty("_Opacity")) mat.SetFloat("_Opacity", 0.90f);
        EditorUtility.SetDirty(mat);
        var go = AddMesh(group, "Coast Shallows", mesh, mat, collider: false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Debug.Log($"[shiosai] shallows: depth-driven shelf, {Cols} columns, {verts.Length} verts.");
    }

    /// <summary>
    /// One shelf vertex. Lays it on the ocean plane (the shelf is a surface layer, not the sea
    /// bed) and bakes the TRUE water depth over the sea floor into vertex colour RED, with the
    /// chapter's coastalness in ALPHA.
    /// </summary>
    private static void WriteShallowVert(Vector3[] verts, Vector2[] uvs, Color[] cols, int v,
                                         Vector3 p, Vector3 s, float o, float floorY,
                                         float waterline, float seaF, float chainage)
    {
        verts[v] = new Vector3(p.x + s.x * o, SeaLevelY + ShallowsLiftM, p.z + s.z * o);
        uvs[v] = new Vector2(o / 12f, chainage / 12f);
        float depth = Mathf.Max(0f, SeaLevelY - floorY);
        cols[v] = new Color(Mathf.Clamp01(depth / ShallowsDepthScaleM), 1f, 1f, seaF);
    }

    /// <summary>Sweeps a slice of the (per-station) cross-section along the whole centreline.</summary>
    private static Mesh Ribbon(CoastRoute route, int firstNode, int lastNode, string name)
    {
        var rows = new List<int>();
        for (int i = 0; i < route.Count; i += Mathf.Max(1, (int)TerrainStride)) rows.Add(i);
        if (rows[rows.Count - 1] != route.Count - 1) rows.Add(route.Count - 1);

        int cols = lastNode - firstNode + 1;
        var verts = new Vector3[rows.Count * cols];
        var uvs = new Vector2[verts.Length];
        var uv2 = new Vector2[verts.Length];
        var off = new float[NodeCount];
        var hgt = new float[NodeCount];

        for (int r = 0; r < rows.Count; r++)
        {
            int i = rows[r];
            CrossSection(route, i, off, hgt);
            var p = route.Position[i];
            var s = route.SideFlat(i);
            for (int c = 0; c < cols; c++)
            {
                int n = firstNode + c;
                verts[r * cols + c] = new Vector3(p.x + s.x * off[n], hgt[n], p.z + s.z * off[n]);
                uvs[r * cols + c] = new Vector2(off[n] / 8f, route.Distance[i] / 8f);
                uv2[r * cols + c] = SplatWeights(n, Seaness(route.Distance[i]));
            }
        }

        var tris = new List<int>((rows.Count - 1) * (cols - 1) * 6);
        for (int r = 0; r < rows.Count - 1; r++)
        for (int c = 0; c < cols - 1; c++)
        {
            int a = r * cols + c, b = a + 1, d = (r + 1) * cols + c, e = d + 1;
            tris.Add(a); tris.Add(d); tris.Add(b);
            tris.Add(b); tris.Add(d); tris.Add(e);
        }
        return Finish(name, verts, uvs, tris, uv2);
    }

    /// <summary>
    /// (rock, scree) splat weight for one cross-section node, for MapleRide/SakuraTerrain's UV1.
    /// The shader adds its own slope-driven rock on top, so these only have to state the
    /// MATERIAL intent of each band, not re-derive the geometry.
    /// </summary>
    private static Vector2 SplatWeights(int node, float sea)
    {
        Vector2 coastal;
        if (node <= NodeShoreline) coastal = new Vector2(0.15f, 0.85f);   // sea floor: sand/shingle
        else if (node < NodeBeachBack) coastal = new Vector2(0.05f, 0.95f);   // beach
        else if (node <= NodeCliffTop) coastal = new Vector2(1f, 0f);         // cliff face: solid rock
        // Headland: grass, with a thin rocky lip where it breaks over the cliff edge.
        else return node == NodeCliffTop + 1 ? new Vector2(0.35f, 0.10f) : Vector2.zero;

        // The four landform bands are whole-route meshes with one material each, so a station in
        // an inland chapter still draws its seaward nodes with the SAND and SEA-FLOOR materials.
        // Geometry alone therefore is not enough to move the Mountain Gateway off the beach - the
        // splat weights have to carry it too.
        //
        // Inland the bands used to be forced to SOLID ROCK (1, 0) for every node above the
        // shoreline, which is why the Gateway's seaward flank rendered as a dark grey wall
        // dropping to the water instead of the green, pine-covered mountainside the concept
        // render (01-mountain-gateway.png) shows. An ISOLATION CAPTURE (Isolate Shiosai Ch1
        // Artifact) proved the offending surface was specifically the SEA FLOOR band, which
        // inland is not a sea floor at all but the lower mountain flank - so it gets grass too,
        // with a little more rock and scree low down than the flank above it.
        // PROVISIONAL mix.
        var mountain = node <= NodeShoreline ? new Vector2(0.34f, 0.12f)   // lower flank
                                             : new Vector2(0.28f, 0.04f); // upper flank
        return Vector2.Lerp(mountain, coastal, sea);
    }

    private static void BuildOcean(Transform root)
    {
        if (UseHdrpWaterSystem) { BuildOceanWaterSurface(root); return; }
        BuildLegacyOceanSheet(root);
    }

    /// <summary>
    /// Destroys every <see cref="WaterSurface"/> in the scene that this pass owns, matched by
    /// EXACT object name, wherever it sits in the hierarchy. Run before the coast builds its
    /// own so a re-run converges instead of accumulating, and so a surface that was dragged (or
    /// migrated) out of the region root cannot leave an infinite sea plane switched on in every
    /// other region.
    ///
    /// Anything NOT named <see cref="OceanWaterName"/> is left alone and merely reported - the
    /// scene may legitimately gain other regions' water later, and silently deleting a stranger's
    /// surface is exactly the Contains()-style over-match that leaked a second sun.
    /// </summary>
    private static void PruneStrayWaterSurfaces()
    {
        int pruned = 0, foreign = 0;
        var all = UnityEngine.Object.FindObjectsByType<WaterSurface>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var w in all)
        {
            if (w == null) continue;
            if (w.gameObject.name == OceanWaterName)
            {
                UnityEngine.Object.DestroyImmediate(w.gameObject);
                pruned++;
            }
            else
            {
                foreign++;
                Debug.Log($"[shiosai] water: leaving foreign WaterSurface '{w.gameObject.name}' " +
                          $"(parent '{(w.transform.parent != null ? w.transform.parent.name : "<root>")}') alone.");
            }
        }
        Debug.Log($"[shiosai] water: pruned {pruned} stale '{OceanWaterName}', {foreign} foreign surface(s) left.");
    }

    /// <summary>
    /// Reports whether the ACTIVE HDRP asset supports the Water System. Read-only on purpose:
    /// supportWater is a per-quality-level pipeline setting, and flipping it from a region's
    /// build pass would silently change what every other region renders (and, per HDRP's own
    /// sanitisation, force MSAA off project-wide). If it is off, the milestone is blocked and
    /// the log says so rather than the render quietly coming back with no sea.
    /// </summary>
    private static void VerifyWaterSupport()
    {
        var hdrp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
        if (hdrp == null)
        {
            Debug.LogWarning("[shiosai] water: active render pipeline is not HDRP - the water surface will not draw.");
            return;
        }
        var s = hdrp.currentPlatformRenderPipelineSettings;
        Debug.Log($"[shiosai] water: HDRP asset '{hdrp.name}' (quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}') " +
                  $"supportWater={s.supportWater} simRes={s.waterSimulationResolution} decals={s.supportWaterDecals}.");
        if (!s.supportWater)
            Debug.LogError($"[shiosai] water: supportWater is OFF on '{hdrp.name}'. Enable it in " +
                           "Project Settings > Quality > HDRP > Rendering > Water, or the coast renders with no sea.");
    }

    /// <summary>
    /// The HDRP Water System ocean. One <see cref="WaterSurface"/>, Infinite geometry, sitting
    /// exactly at <see cref="SeaLevelY"/> so it meets the swept beach at the same waterline the
    /// cross-section publishes (node 4, +0.18 m) - no gap, and no z-fight, because there is now
    /// only ONE water surface.
    ///
    /// IDEMPOTENCE. Apply() destroys and rebuilds the region root every run, so a child here
    /// cannot accumulate; what CAN accumulate is a stray surface outside the root (a hand-added
    /// one, or a leftover from an earlier layout), and an infinite ocean outside the region root
    /// would draw a sea plane across Sakura Pass and Maple City. PruneStrayWaterSurfaces handles
    /// that by exact name, scene-wide.
    /// </summary>
    private static void BuildOceanWaterSurface(Transform root)
    {
        var go = new GameObject(OceanWaterName);
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(0f, SeaLevelY, 0f);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var w = go.AddComponent<WaterSurface>();

        // -- geometry ---------------------------------------------------------------------
        // Infinite, because the coast's hero views look 6+ km out to a true horizon ~28 km
        // away from the clifftop road. That is the same reason the legacy sheet had to be
        // 40 km wide (see OceanHorizonMarginM); Infinite gets it for free and, unlike the
        // sheet, it keeps tessellating detail near the camera.
        w.surfaceType = WaterSurfaceType.OceanSeaLake;
        w.geometryType = WaterGeometryType.Infinite;
        w.timeMultiplier = 1f;
        // No CPU-side height queries yet (nothing floats on the coast water), and each one
        // costs a readback. Turn it on when a boat or a buoy needs to ride the swell.
        w.scriptInteractions = false;
        w.cpuEvaluateRipples = false;
        w.customMaterial = null;

        // -- simulation: CALM COASTAL, not open-ocean weather ------------------------------
        w.repetitionSize = OceanRepetitionSizeM;
        w.largeWindSpeed = OceanWindSpeedKmh;
        w.largeChaos = OceanChaos;
        w.largeOrientationValue = 0f;
        w.largeCurrentSpeedValue = 0f;
        w.largeBand0Multiplier = 1f;
        w.largeBand1Multiplier = 1f;
        w.largeBand0FadeMode = WaterSurface.FadeMode.Custom;
        w.largeBand0FadeStart = 2500f;
        w.largeBand0FadeDistance = 5000f;
        w.largeBand1FadeMode = WaterSurface.FadeMode.Custom;
        w.largeBand1FadeStart = 600f;
        w.largeBand1FadeDistance = 1600f;
        // Ripples carry the sun sparkle. Automatic fade dropped them by ~250 m, which on a
        // region whose hero shots are 300 m - 6 km of open water flattened most of the frame.
        w.ripples = true;
        w.ripplesMotionMode = WaterPropertyOverrideMode.Inherit;
        w.ripplesWindSpeed = OceanRipplesWindKmh;
        w.ripplesChaos = 0.8f;
        w.ripplesFadeMode = WaterSurface.FadeMode.Custom;
        w.ripplesFadeStart = 300f;
        w.ripplesFadeDistance = 1400f;
        w.maximumHeightOverride = 0f;

        // -- tessellation / smoothness (the glint) ----------------------------------------
        w.tessellation = true;
        w.maxTessellationFactor = 3f;
        w.tessellationFactorFadeStart = 150f;
        w.tessellationFactorFadeRange = 1850f;
        // High smoothness = a near-mirror surface, which is what turns the sky and the sun into
        // a specular sheet and a glint path. Held high a long way out so the horizon still
        // reflects sky rather than going matte.
        w.startSmoothness = 0.96f;
        w.endSmoothness = 0.90f;
        w.smoothnessFadeStart = 400f;
        w.smoothnessFadeDistance = 3000f;

        // -- colour: refraction (shallows) + scattering (deep) ----------------------------
        // Both authored sRGB and converted, matching how HDRP's own Ocean preset assigns them.
        w.refractionColor = OceanRefractionColour.linear;
        w.maxRefractionDistance = 0.5f;
        w.absorptionDistance = OceanAbsorptionDistanceM;
        w.scatteringColor = OceanScatteringColour.linear;
        w.ambientScattering = 0.32f;
        w.heightScattering = 0.2f;
        w.displacementScattering = 0.12f;
        w.directLightTipScattering = 0.6f;
        w.directLightBodyScattering = 0.5f;

        // -- caustics ---------------------------------------------------------------------
        // Off, as in HDRP's Ocean preset: caustics are projected onto a virtual plane a few
        // metres down, which is meaningful in a pool and meaningless over a 30 m sea floor.
        // (The old sea's "caustic band" was exactly this mistake, painted by hand.)
        w.caustics = false;

        // -- foam -------------------------------------------------------------------------
        w.foam = true;
        w.foamResolution = WaterSurface.WaterDecalRegionResolution.Resolution512;
        w.foamPersistenceMultiplier = 0.55f;
        w.foamCurrentInfluence = 0.6f;
        w.foamColor = Color.white;
        w.foamTextureTiling = 0.18f;
        w.foamSmoothness = 1f;
        w.simulationFoamAmount = OceanFoamAmount;
        w.supportSimulationFoamMask = false;
        w.simulationFoamMask = null;
        w.simulationFoamWindCurve =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 0f),
                               new Keyframe(0.30f, 1f), new Keyframe(1f, 1f));
        // The foam/decal region follows the camera-adjacent water; 500 m covers the beach,
        // the breakwater and the jetty in every hero framing.
        w.decalRegionSize = new Vector2(500f, 500f);
        w.decalRegionAnchor = null;

        // -- underwater -------------------------------------------------------------------
        // Off: the rider never goes below the surface, and an underwater volume on an Infinite
        // ocean would tint every camera that dips under y = 0.
        w.underWater = false;

        Debug.Log($"[shiosai] ocean: HDRP WaterSurface '{OceanWaterName}' (Ocean/Infinite) at " +
                  $"y={SeaLevelY:0.00}, wind {OceanWindSpeedKmh:0.0} km/h, chaos {OceanChaos:0.00}, " +
                  $"absorption {OceanAbsorptionDistanceM:0.0} m, foam {OceanFoamAmount:0.00}.");
    }

    /// <summary>
    /// RETIRED custom-shader sea sheet. Kept compiled (not deleted) so UseHdrpWaterSystem is a
    /// genuine one-line fallback if the Water System ever has to be backed out.
    /// </summary>
    private static void BuildLegacyOceanSheet(Transform root)
    {
        var route = CoastRoute.Load();

        // Size and place the plane from the ROUTE'S OWN FOOTPRINT. The mock centred a fixed
        // 14 km plane 3.2 km seaward of the midpoint, which works only while "seaward" is a
        // single fixed direction and the road is shorter than the plane. The Grand Coast spans
        // roughly 11 km east-west and 25 km north-south and changes which way the sea lies
        // several times, so the plane is now the route's bounding box plus a horizon margin.
        // A flat sheet at y = 0 needs no seaward offset: inland chapters sit 200-420 m above it
        // and simply occlude it, exactly as real ground does.
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < route.Count; i++)
        {
            var q = route.Position[i];
            if (q.x < minX) minX = q.x; if (q.x > maxX) maxX = q.x;
            if (q.z < minZ) minZ = q.z; if (q.z > maxZ) maxZ = q.z;
        }
        var centre = new Vector3((minX + maxX) * 0.5f, SeaLevelY, (minZ + maxZ) * 0.5f);
        float sizeX = (maxX - minX) + 2f * OceanHorizonMarginM;
        float sizeZ = (maxZ - minZ) + 2f * OceanHorizonMarginM;

        int n = OceanGrid + 1;
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float fx = (float)x / OceanGrid - 0.5f;
            float fz = (float)z / OceanGrid - 0.5f;
            verts[z * n + x] = new Vector3(centre.x + fx * sizeX, SeaLevelY,
                                           centre.z + fz * sizeZ);
            uvs[z * n + x] = new Vector2(fx * sizeX / 24f, fz * sizeZ / 24f);
        }
        var tris = new List<int>(OceanGrid * OceanGrid * 6);
        for (int z = 0; z < OceanGrid; z++)
        for (int x = 0; x < OceanGrid; x++)
        {
            int a = z * n + x, b = a + 1, c = (z + 1) * n + x, d = c + 1;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }

        var mesh = Finish("Shiosai_Ocean", verts, uvs, tris);
        var go = AddMesh(root, "Shiosai Ocean", mesh, WaterMaterial(), collider: false);
        // The ocean is the horizon: it must draw behind everything and cast nothing.
        go.GetComponent<MeshRenderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        Debug.Log($"[shiosai] ocean: {sizeX:0} x {sizeZ:0} m at ({centre.x:0}, {centre.z:0}).");
    }

    // --------------------------------------------------------------- road

    private static void BuildRoad(Transform root, CoastRoute route)
    {
        var group = new GameObject("Coast Roadway").transform;
        group.SetParent(root, false);

        float edge = RoadHalfWidth + ShoulderWidth;
        var lanes = new[] { -edge, -RoadHalfWidth, -RoadHalfWidth * 0.5f, 0f,
                            RoadHalfWidth * 0.5f, RoadHalfWidth, edge };

        var verts = new Vector3[route.Count * lanes.Length];
        var uvs = new Vector2[verts.Length];
        for (int i = 0; i < route.Count; i++)
        for (int c = 0; c < lanes.Length; c++)
        {
            float o = lanes[c];
            var p = route.Position[i];
            var s = route.SideFlat(i);
            // Crown: the carriageway sheds water to its edges, like the pass. The bias is
            // solved (see RoadSurfaceLiftM) so the lane line lands on the rider's own height.
            float y = SurfaceHeight(route, i, o) + RoadSurfaceLiftM + CrownAt(o);
            verts[i * lanes.Length + c] = new Vector3(p.x + s.x * o, y, p.z + s.z * o);
            // METRE-SCALE UVs. The first pass mapped u across the whole 8.1 m carriageway into
            // 0..1 and v in 6 m steps, so the asphalt map was stretched ~8x across the road and
            // squashed along it - which is most of why the surface read as a flat grey plane
            // with no aggregate. Both axes now run at AsphaltTileM metres per tile, so the
            // texture is the size it was authored for and the grain is isotropic.
            uvs[i * lanes.Length + c] = new Vector2(o / AsphaltTileM,
                                                    route.Distance[i] / AsphaltTileM);
        }
        var tris = new List<int>();
        for (int i = 0; i < route.Count - 1; i++)
        for (int c = 0; c < lanes.Length - 1; c++)
        {
            int a = i * lanes.Length + c, b = a + 1;
            int d = (i + 1) * lanes.Length + c, e = d + 1;
            tris.Add(a); tris.Add(d); tris.Add(b);
            tris.Add(b); tris.Add(d); tris.Add(e);
        }
        AddMesh(group, "Coast Carriageway", Finish("Shiosai_Carriageway", verts, uvs, tris),
                // FIDELITY POC: the carriageway is the hero surface. PBR, not cel - see PbrMaterial.
                PbrMaterial("Shiosai_Asphalt", "Shiosai_Asphalt", AsphaltTileM,
                            smoothness: 0.28f, normalScale: 1.15f,
                            tint: CoastAsphaltTint),
                collider: true);

        BuildMarkings(group, route);
    }

    private static void BuildMarkings(Transform group, CoastRoute route)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // Solid edge lines both sides, dashed centre line: 6 m dash / 6 m gap (PROVISIONAL).
        Strip(route, -(RoadHalfWidth - 0.22f), 0.14f, 0f, 1f, verts, uvs, tris);
        Strip(route, RoadHalfWidth - 0.22f, 0.14f, 0f, 1f, verts, uvs, tris);
        Strip(route, 0f, 0.16f, 6f, 6f, verts, uvs, tris);

        AddMesh(group, "Coast Markings",
                Finish("Shiosai_Markings", verts.ToArray(), uvs.ToArray(), tris),
                CelMaterial("Shiosai_LinePaint", new Color(0.93f, 0.92f, 0.87f), gloss: 0.05f,
                            spec: 0.05f, rim: 0.2f),
                collider: false);

        // The 自転車ナビライン, kept but toned DOWN to blend with the carriageway (owner request):
        // no longer a saturated cobalt stripe.
        BuildCycleLane(group, route);
    }

    /// <summary>
    /// The 自転車ナビライン - Japan's painted BLUE cycling lane, laid just inside each edge line.
    ///
    /// It is in nearly every reference render (01, 15) and it is the single most on-brand piece
    /// of road paint this project could have: MapleRide is a cycling game, and the blue line is
    /// what tells the player at a glance that this carriageway is theirs. Separate mesh and
    /// material from the white paint so the two never share a colour by accident.
    /// </summary>
    private static void BuildCycleLane(Transform group, CoastRoute route)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        Strip(route, -(RoadHalfWidth - CycleLaneInsetM), CycleLaneWidthM, 0f, 1f, verts, uvs, tris);
        Strip(route, RoadHalfWidth - CycleLaneInsetM, CycleLaneWidthM, 0f, 1f, verts, uvs, tris);

        AddMesh(group, "Coast Cycle Lane",
                Finish("Shiosai_CycleLane", verts.ToArray(), uvs.ToArray(), tris),
                // Muted blue-grey, roughly midway from the old cobalt toward the asphalt tone, so
                // the nav lane still reads as "your lane" but blends into the road instead of
                // shouting. Low spec/rim so it does not catch highlights and pop.
                CelMaterial("Shiosai_CycleLanePaint", new Color(0.42f, 0.52f, 0.64f), gloss: 0.05f,
                            spec: 0.04f, rim: 0.12f),
                collider: false);
    }

    private static void Strip(CoastRoute route, float offset, float width, float dash, float gap,
                              List<Vector3> verts, List<Vector2> uvs, List<int> tris)
    {
        for (int i = 0; i < route.Count - 1; i++)
        {
            if (dash > 0f)
            {
                float phase = route.Distance[i] % (dash + gap);
                if (phase > dash) continue;
            }
            for (int k = 0; k < 2; k++)
            {
                int idx = i + k;
                var p = route.Position[idx];
                var s = route.SideFlat(idx);
                float y = SurfaceHeight(route, idx, offset) + RoadSurfaceLiftM + MarkingLiftM +
                          CrownAt(offset);
                verts.Add(new Vector3(p.x + s.x * (offset - width), y, p.z + s.z * (offset - width)));
                verts.Add(new Vector3(p.x + s.x * (offset + width), y, p.z + s.z * (offset + width)));
                uvs.Add(new Vector2(0f, route.Distance[idx]));
                uvs.Add(new Vector2(1f, route.Distance[idx]));
            }
            int b = verts.Count - 4;
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
        }
    }

    // --------------------------------------------------------------- start line

    /// <summary>
    /// The ride START at course arc 0 - what the player looks at during the 3-2-1 countdown.
    ///
    /// The coast carriageway is authored one-way and simply BEGINS at arc 0 with a blunt
    /// transverse cut; with only the through edge-lines, blue cycle lanes and centre dashes
    /// running up to that cut, the road end read as a painted hazard / "out of bounds" strip
    /// rather than as a race start. This lays a proper motorsport start/finish over it:
    ///
    ///   * a black-and-white CHECKERED band across the full carriageway, flat on the road
    ///     (StartLineLiftM proud of the paint so it draws over it, no z-fighting), and
    ///   * a START GANTRY straddling the road ~13 m ahead (two posts + top beam + a
    ///     vermilion-trimmed checkered banner), so it reads as an arch the rider approaches and
    ///     rides under - the way Zwift/Rouvy make the overhead banner the hero - with clearance
    ///     well above the rider/camera, plus
    ///   * a checkered marshal FLAG on a pole on the inland verge beside the gantry.
    ///
    /// The on-road line is placed from the route's arc-0 frame; the gantry from the route node
    /// ~13 m ahead (route.IndexAt), each using its own position / tangent / side and the shared
    /// carriageway half-width, so both sit square across the road even on a curve. Called from
    /// Apply(), which rebuilds the region root from scratch, so it is idempotent.
    /// </summary>
    private static void BuildStartLine(Transform root, CoastRoute route)
    {
        var group = new GameObject("Coast Start Line").transform;
        group.SetParent(root, false);

        int i0 = 0;
        var p = route.Position[i0];
        var t = new Vector3(route.Tangent[i0].x, 0f, route.Tangent[i0].z).normalized;
        var s = route.SideFlat(i0);

        var whiteMat = CelMaterial("Shiosai_StartWhite", new Color(0.95f, 0.95f, 0.92f),
                                   gloss: 0.06f, spec: 0.06f, rim: 0.20f);
        var blackMat = CelMaterial("Shiosai_StartBlack", new Color(0.075f, 0.075f, 0.085f),
                                   gloss: 0.06f, spec: 0.06f, rim: 0.20f);
        var postMat  = CelMaterial("Shiosai_StartPost", new Color(0.93f, 0.93f, 0.91f),
                                   gloss: 0.12f, spec: 0.12f, rim: 0.35f);
        var bandMat  = CelMaterial("Shiosai_StartBand", new Color(0.784f, 0.192f, 0.157f),
                                   gloss: 0.20f, spec: 0.18f, rim: 0.40f);

        var wv = new List<Vector3>(); var wu = new List<Vector2>(); var wt = new List<int>();
        var bv = new List<Vector3>(); var bu = new List<Vector2>(); var bt = new List<int>();
        var pv = new List<Vector3>(); var pu = new List<Vector2>(); var pt = new List<int>();
        var rv = new List<Vector3>(); var ru = new List<Vector2>(); var rt = new List<int>();

        // Emits a checker grid over a parametric surface, white/black by parity, double-sided.
        void Checker(System.Func<float, float, Vector3> at,
                     float uMin, float uMax, int uc, float vMin, float vMax, int vc)
        {
            float du = (uMax - uMin) / uc, dv = (vMax - vMin) / vc;
            for (int r = 0; r < vc; r++)
            for (int c = 0; c < uc; c++)
            {
                float u0 = uMin + c * du, u1 = u0 + du;
                float v0 = vMin + r * dv, v1 = v0 + dv;
                var A = at(u0, v0); var B = at(u1, v0); var C = at(u1, v1); var D = at(u0, v1);
                bool white = ((r + c) & 1) == 0;
                var vv = white ? wv : bv; var uv = white ? wu : bu; var tr = white ? wt : bt;
                Quad(vv, uv, tr, A, B, C, D, false);
                Quad(vv, uv, tr, A, B, C, D, true);
            }
        }
        // A solid vermilion strip (banner valance / hem), double-sided.
        void Band(System.Func<float, float, Vector3> at, float uMin, float uMax, float vBot, float vTop)
        {
            var A = at(uMin, vBot); var B = at(uMax, vBot); var C = at(uMax, vTop); var D = at(uMin, vTop);
            Quad(rv, ru, rt, A, B, C, D, false);
            Quad(rv, ru, rt, A, B, C, D, true);
        }

        // ---- 1) a CLEAN, THIN checkered start/finish line, flat on the carriageway at arc 0 --
        // Zwift/Rouvy keep the on-road mark a tidy ~1 m line and let the overhead banner carry
        // the message - a 2 m checkerboard slab under the wheels reads as amateur. Two rows,
        // centred on the line, edge to edge.
        const float halfW = RoadHalfWidth;
        const float square = 0.5f;
        int cols = Mathf.Max(2, Mathf.RoundToInt(halfW * 2f / square));
        float cell = halfW * 2f / cols;
        const int rows = 2;                                // ~1 m deep: a line, not a slab
        float depth = rows * cell;
        Vector3 OnRoad(float off, float along) => new Vector3(
            p.x + t.x * along + s.x * off,
            SurfaceHeight(route, i0, off) + RoadSurfaceLiftM + CrownAt(off) + StartLineLiftM,
            p.z + t.z * along + s.z * off);
        Checker(OnRoad, -halfW, halfW, cols, -depth * 0.5f, depth * 0.5f, rows);

        // ---- 2) the START GANTRY, placed AHEAD so it reads as an arch you ride under ---------
        // A gantry 1.4 m ahead is entirely off-frame (posts to the sides, banner above the top
        // of the view). Zwift/Rouvy put the banner ~10-15 m down the road as the hero element;
        // place it from the route node at that chainage so it stays square on a curve.
        int jG = route.IndexAt(13f);
        var pG = route.Position[jG];
        var tG = new Vector3(route.Tangent[jG].x, 0f, route.Tangent[jG].z).normalized;
        var sG = route.SideFlat(jG);
        const float span = halfW + 1.4f;                   // posts just off the paved edge
        const float postH = 6.4f;                          // ~7 m arch, well clear of rider/cam
        float baseYL = SurfaceHeight(route, jG, -span) + RoadSurfaceLiftM + CrownAt(-span);
        float baseYR = SurfaceHeight(route, jG,  span) + RoadSurfaceLiftM + CrownAt( span);
        Vector3 Foot(float off, float baseY) => new Vector3(pG.x + sG.x * off, baseY, pG.z + sG.z * off);
        Box(pv, pu, pt, Foot(-span, baseYL) + Vector3.up * (postH * 0.5f),
            new Vector3(0.5f, postH, 0.5f), tG);
        Box(pv, pu, pt, Foot( span, baseYR) + Vector3.up * (postH * 0.5f),
            new Vector3(0.5f, postH, 0.5f), tG);
        float topY = Mathf.Max(baseYL, baseYR) + postH;
        // Top beam - the horizontal above the banner is what makes the pair of posts read as a
        // gantry rather than two lone poles.
        Box(pv, pu, pt, new Vector3(pG.x, topY + 0.32f, pG.z),
            new Vector3(span * 2f + 0.9f, 0.55f, 0.7f), tG);
        // Banner hung under the beam: vermilion valance + hem framing a checker cloth, so it
        // reads as a race banner rather than a bare checkerboard.
        const float bannerH = 1.9f;
        const float valance = 0.32f;
        float bTop = topY;
        float bBot = topY - bannerH;
        int bcols = Mathf.Max(8, Mathf.RoundToInt(span * 2f / 0.55f));
        Vector3 Banner(float off, float y) => new Vector3(pG.x + sG.x * off, y, pG.z + sG.z * off);
        Band(Banner, -span, span, bTop - valance, bTop);
        Band(Banner, -span, span, bBot, bBot + valance);
        Checker(Banner, -span, span, bcols, bBot + valance, bTop - valance, 3);

        // ---- 3) a checkered marshal flag on a pole beside the gantry (inland verge) ----------
        float flagOff = span + 1.4f;
        float flagBaseY = SurfaceHeight(route, jG, flagOff) + RoadSurfaceLiftM;
        const float flagPoleH = 3.6f;
        Box(pv, pu, pt,
            new Vector3(pG.x + sG.x * flagOff, flagBaseY + flagPoleH * 0.5f, pG.z + sG.z * flagOff),
            new Vector3(0.14f, flagPoleH, 0.14f), tG);
        float flagTop = flagBaseY + flagPoleH - 0.05f;
        float flagBot = flagTop - 0.6f;
        Checker(Banner, flagOff - 0.9f, flagOff, 3, flagBot, flagTop, 3);

        AddMesh(group, "Start Checker White",
                Finish("Shiosai_StartWhite", wv.ToArray(), wu.ToArray(), wt), whiteMat, false);
        AddMesh(group, "Start Checker Black",
                Finish("Shiosai_StartBlack", bv.ToArray(), bu.ToArray(), bt), blackMat, false);
        AddMesh(group, "Start Gantry",
                Finish("Shiosai_StartPost", pv.ToArray(), pu.ToArray(), pt), postMat, false);
        AddMesh(group, "Start Banner Trim",
                Finish("Shiosai_StartBand", rv.ToArray(), ru.ToArray(), rt), bandMat, false);

        Debug.Log($"[shiosai] start line rebuilt: thin checker {cols}x{rows} at arc0, " +
                  $"gantry arch at {route.Distance[jG]:0} m (top {topY:0.0} m).");
    }

    // --------------------------------------------------------------- guardrail

    /// <summary>
    /// Lays the authored guardrail bay (W-beam + post + ORANGE REFLECTOR) end to end along the
    /// seaward verge and BAKES the run into one combined mesh per material.
    ///
    /// 2.9 km at 4 m bays is ~720 instances; as GameObjects that would be ~2,200 renderers for
    /// a handrail. Combining collapses it to one renderer per material while keeping the real
    /// modelled cross-section. Post spacing is deliberately regular - MLIT's landscape guideline
    /// explicitly calls irregular guardrail spacing "cluttered".
    /// </summary>
    private static void BuildGuardrail(Transform root, CoastRoute route)
    {
        var group = new GameObject("Coast Guardrail").transform;
        group.SetParent(root, false);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetDir}/Shiosai_GuardrailBay.glb");
        if (prefab == null)
        {
            Debug.LogWarning("[shiosai] Shiosai_GuardrailBay.glb missing - guardrail skipped.");
            return;
        }

        // Pull (mesh, submesh, material) out of the authored bay once.
        var sources = new List<(Mesh mesh, int sub, string mat, Matrix4x4 local)>();
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null) continue;
            var local = mf.transform.localToWorldMatrix;   // prefab-local == identity chain
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
            {
                string mn = s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null
                    ? mr.sharedMaterials[s].name.ToLowerInvariant() : "";
                sources.Add((mf.sharedMesh, s, mn, local));
            }
        }

        var byMat = new Dictionary<string, List<CombineInstance>>();
        int bays = 0;
        // Walking the route SAMPLES and taking the first one past each 4 m mark snapped bays to
        // the ~2.9 m sample stride, so the real spacing averaged 5.7 m and the run rendered as
        // detached fence panels. Stepping by exact arc length and interpolating between samples
        // butts the beams end to end.
        //
        // STEP == BAY, EXACTLY. The 0.08 m overlap this used to carry did not hide a joint: on a
        // curving verge it left every bay end pushed 8 cm past its neighbour at a slightly
        // different yaw, which is the scalloped, stepped silhouette badcliff.png shows and the
        // main reason the rail read as disconnected panels. The authored bay is now a CLOSED
        // solid with end caps (build_shiosai.build_guardrail_bay), so butting the bays at
        // exactly the bay length gives one continuous rail.
        const float BayM = 4.0f;                   // must match build_guardrail_bay(bay=...)
        const float Step = BayM;
        for (float d = 0f; d < route.Length; d += Step)
        {
            // The authored bridge carries its own coral parapet; a steel W-beam swept through it
            // would render as two fences occupying the same metre of deck edge.
            if (OnRedBridge(route, d)) continue;
            if (InTunnel(route, d)) continue;   // no guardrail inside the bore
            if (TunaPortOwnsSeawardVerge(route, d)) continue;   // port apron / sea-wall parapet instead
            int i = route.IndexAt(d);
            int j = Mathf.Min(i + 1, route.Count - 1);
            float span = Mathf.Max(0.001f, route.Distance[j] - route.Distance[i]);
            float f = Mathf.Clamp01((d - route.Distance[i]) / span);

            var a = PointAt(route, i, GuardrailOffset);
            var b = PointAt(route, j, GuardrailOffset);
            var at = Vector3.Lerp(a, b, f);
            // A bay is a RIGID 4 m object centred on `d`. Seating each bay at the LOWEST of its
            // own ends (the first attempt at "never floating") staircases the run: on a 6.5%
            // grade each bay drops ~0.26 m relative to where its neighbour ended, which renders
            // as exactly the detached-panel look this fix exists to remove. Instead the bay is
            // seated on the interpolated surface at its centre AND PITCHED onto the 3-D tangent
            // (below), so consecutive bays meet in both height and slope; the 0.30 m of buried
            // post authored into the bay absorbs the remaining sub-centimetre verge ripple.
            var t = Vector3.Slerp(route.Tangent[i], route.Tangent[j], f);
            // Continuous-run props run along local +X. A YAW-ONLY rotation keeps every 4 m bay
            // dead level, so on a graded road the downhill end of one bay sits up to a quarter
            // of a metre above the uphill end of the next - the stepped, disconnected rail the
            // gameplay screenshots show. Aligning local +X with the FULL 3-D tangent pitches the
            // bay onto the grade so the beams meet flush.
            var rot = Quaternion.LookRotation(t.normalized, Vector3.up) *
                      Quaternion.Euler(0f, -90f, 0f);
            var trs = Matrix4x4.TRS(at, rot, Vector3.one);

            foreach (var (mesh, sub, mn, local) in sources)
            {
                if (!byMat.TryGetValue(mn, out var list))
                    byMat[mn] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = trs * local });
            }
            bays++;
        }

        int reflectors = 0;
        foreach (var kv in byMat)
        {
            // A combined 2.9 km run blows past 65k verts, so the 32-bit index format is required.
            var combined = new Mesh { name = $"Shiosai_Guardrail_{SafeName(kv.Key)}",
                                      indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            combined.CombineMeshes(kv.Value.ToArray(), true, true);
            combined.RecalculateNormals();
            combined.RecalculateBounds();
            var matAsset = CoastMaterialFor(kv.Key) ??
                           CelMaterial("Shiosai_Steel", new Color(0.80f, 0.82f, 0.80f));
            AddMesh(group, $"Guardrail {SafeName(kv.Key)}", combined, matAsset, collider: false);
            if (kv.Key.Contains("reflector")) reflectors = kv.Value.Count;
        }

        Debug.Log($"[shiosai] guardrail: {bays} bays over {route.Length:0} m, " +
                  $"{byMat.Count} material runs, {reflectors} orange reflectors.");
    }

    private static string SafeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Run";
        var sb = new System.Text.StringBuilder();
        foreach (char c in raw)
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.Length == 0 ? "Run" : sb.ToString();
    }

    // --------------------------------------------------------------- staging authored assets

    private const string AssetDir = "Assets/Environment/ShiosaiCoast/BlenderAssets";

    // M5: the coast has no sakura or verge-flower builder of its own, and authoring one would
    // duplicate flora Sakura Pass already ships in the exact cel-shaded idiom this region
    // wants. These are OUR assets (not imported third-party art), so the coast borrows the
    // GLBs and, crucially, the already-correct alpha-blended foliage MATERIALS - re-deriving
    // them through CoastMaterialFor would have produced opaque black cards, which is the
    // classic failure mode for cutout foliage staged with a solid cel material.
    private const string SakuraAssetDir = "Assets/Environment/SakuraPass/BlenderAssets";
    private const string SakuraMatDir = "Assets/Environment/SakuraPass/Materials";

    private static Material SakuraMat(string name)
        => AssetDatabase.LoadAssetAtPath<Material>($"{SakuraMatDir}/{name}.mat");

    /// <summary>
    /// Coast-own verge flower tints. Sakura Pass's VergeFlower set is deliberately hot pink /
    /// coral to sit under blossom; on the Shiosai verge that read as magenta twigs (see the
    /// M5 ch3 crop). References 03/10 put CREAM and pale yellow daisies among the green, so
    /// the coast clones Sakura's proven alpha-cutout foliage material - authoring a fresh cel
    /// material here would produce opaque black cards - and only re-tints it. PROVISIONAL.
    /// </summary>
    private static readonly Color[] VergeFlowerColours =
    {
        new Color(0.96f, 0.95f, 0.90f),   // cream white daisy
        new Color(0.95f, 0.90f, 0.62f),   // pale yellow
        new Color(0.88f, 0.90f, 0.86f),   // near-white
    };

    private static Material CoastVergeFlowerMat(int idx)
    {
        idx = Mathf.Clamp(idx, 0, VergeFlowerColours.Length - 1);
        string name = $"Shiosai_VergeFlower_{idx}";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        var src = SakuraMat("SakuraPass_VergeFlower_0");
        if (src == null) return null;
        var mat = LoadOrCreate(name, src.shader.name);
        mat.CopyPropertiesFromMaterial(src);
        mat.shader = src.shader;
        mat.SetColor("_Color", VergeFlowerColours[idx]);
        mat.SetColor("_TransColor", new Color(1f, 0.96f, 0.80f, 1f));
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Stages a borrowed Sakura Pass flora GLB, keeping Sakura's own foliage materials.
    /// <paramref name="tint"/> selects one of the crown/flower tint variants so a bank of them
    /// is not one flat colour. <paramref name="leanDeg"/> tilts the trunk (sakura in reference
    /// 03 lean out OVER the carriageway; a perfectly plumb tree never reads as "overhanging").
    /// </summary>
    private static readonly Dictionary<string, int> FloraMatSeen = new Dictionary<string, int>();

    private static GameObject PlaceSakuraFlora(string asset, string displayName, Transform parent,
                                               Vector3 position, float yawDeg, float scale,
                                               int tint, float leanDeg = 0f, float leanDirDeg = 0f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{SakuraAssetDir}/{asset}.glb");
        if (prefab == null)
        {
            Debug.LogWarning($"[shiosai] missing borrowed flora asset '{asset}.glb'.");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = displayName;
        go.transform.SetPositionAndRotation(
            position,
            Quaternion.Euler(0f, yawDeg, 0f) * Quaternion.AngleAxis(leanDeg, Quaternion.Euler(0f, leanDirDeg, 0f) * Vector3.forward));
        go.transform.localScale = Vector3.one * scale;
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(col);

        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                Material repl = null;
                if (n.Contains("bark")) repl = SakuraMat("SakuraPass_Bark");
                else if (n.Contains("blossom") || n.Contains("petal")) repl = SakuraMat($"SakuraPass_BlossomCrown_{tint & 3}");
                else if (n.Contains("flower")) repl = CoastVergeFlowerMat(tint % 3) ?? SakuraMat($"SakuraPass_VergeFlower_{tint % 3}");
                else if (n.Contains("shrub")) repl = SakuraMat("SakuraPass_VergeShrub");
                else if (n.Contains("needle")) repl = SakuraMat("SakuraPass_Needle");
                else if (n.Contains("leaf")) repl = SakuraMat($"SakuraPass_BroadleafCrown_{tint & 3}");
                if (repl != null) mats[i] = repl;
                // BLOCKER C instrumentation: the cream re-tint lands on disk but the render
                // stays pink, so record what source material names actually arrive here and
                // whether each one was routed. Guessing cost a cycle already.
                string key = (mats[i] != null ? mats[i].name : "<null>") +
                             (repl != null ? "  ->ROUTED" : "  ->UNROUTED(" + n + ")");
                FloraMatSeen.TryGetValue(key, out int c);
                FloraMatSeen[key] = c + 1;
            }
            r.sharedMaterials = mats;
        }
        return go;
    }

    /// <summary>
    /// Instantiates an authored GLB and re-materialises it with the coast's cel materials.
    /// Returns null (with a warning) when the asset is missing, so a half-built asset folder
    /// degrades to "that prop is absent" rather than to a null-reference mid-build.
    /// </summary>
    private static GameObject Place(string asset, string displayName, Transform parent,
                                    Vector3 position, float yawDeg, float scale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetDir}/{asset}.glb");
        if (prefab == null)
        {
            Debug.LogWarning($"[shiosai] missing authored asset '{asset}.glb' - run: " +
                             "blender -b -P tools/blender/build_shiosai.py");
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = displayName;
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDeg, 0f));
        go.transform.localScale = Vector3.one * scale;
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(col);
        AssignCoastMaterials(go);
        return go;
    }

    /// <summary>
    /// Routes every imported glTF material onto a cel material by NAME. The Blender builders
    /// name their materials descriptively precisely so this can work (see build_shiosai.py).
    /// Anything unmatched keeps its glTF material rather than being silently flattened.
    /// </summary>
    private static void AssignCoastMaterials(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                mats[i] = CoastMaterialFor(n) ?? mats[i];
            }
            r.sharedMaterials = mats;
        }
    }

    private static Material CoastMaterialFor(string n)
    {
        // THE RED BRIDGE FIRST. Its material names contain "red" (the asset is Shiosai_RedBridge)
        // and "deck", which the generic red/concrete rules further down would both catch in the
        // wrong order - the concrete deck would have come out vermilion. Explicit names, matched
        // first, also let the landmark carry the spec's coral vermilion (#D95748) instead of the
        // deliberately dark torii red, which is authored low precisely so a 7 m shrine gate does
        // not clip; a 170 m bridge needs the brighter accent the value hierarchy (3.3) asks for.
        if (n.Contains("bridgecoral"))
            return CelMaterial("Shiosai_BridgeCoral", new Color(0.72f, 0.26f, 0.19f), gloss: 0.26f,
                               spec: 0.20f, rim: 0.50f);
        if (n.Contains("bridgedeck"))
            return CelMaterial("Shiosai_BridgeConcrete", new Color(0.68f, 0.67f, 0.63f), gloss: 0.08f,
                               spec: 0.07f, rim: 0.25f,
                               texture: SakuraTexture("Sakura_Scree_Albedo.png"));

        // M4: the foam collar authored at the foot of every sea stack. Must be matched BEFORE
        // the rock/stack rules below, whose "stack" and generic rock tests would both claim it.
        if (n.Contains("foam"))
            // M4c: pure white at rim 0.55 made the wash read as a cut-out paper disc. A touch
            // of the sea's own blue in the shadow keeps it reading as aerated water.
            return CelMaterial("Shiosai_Foam", new Color(0.90f, 0.94f, 0.95f), gloss: 0.18f,
                               spec: 0.08f, rim: 0.25f);

        // The fishing boat FIRST: its hull material is named "..._Hull_White", which the generic
        // "white|tower" rule would have caught anyway - but its deck material is "..._Deck_Trim",
        // and "trim" belongs to the bark rule, so the boats rendered as dark brown wrecks.
        if (n.Contains("fishingboat"))
        {
            if (n.Contains("hull"))
                return CelMaterial("Shiosai_BoatHull", new Color(0.88f, 0.88f, 0.86f), gloss: 0.30f,
                                   spec: 0.25f, rim: 0.50f);
            return CelMaterial("Shiosai_BoatTrim", new Color(0.20f, 0.32f, 0.52f), gloss: 0.22f,
                               spec: 0.18f, rim: 0.45f);
        }

        // Per-house variants: a fishing town of identical white boxes with identical grey
        // roofs read as a housing estate in the render. The three house builders name their
        // materials with the house letter, so the town can be given three palettes for free.
        // The benchmark kit must be handled before the original House_B/C branches: its names
        // also contain "house_b" etc., but its clean plaster/foundation panels deliberately
        // do NOT use the old scree texture that made the first harbour look like noisy concrete.
        if (n.Contains("harbourhouse"))
        {
            if (n.Contains("foundation"))
                return ArchitectureMaterial("Shiosai_HarbourFoundation", new Color(0.42f, 0.42f, 0.39f),
                                            smoothness: 0.10f);
            if (n.Contains("trim") || n.Contains("beam") || n.Contains("post") || n.Contains("timber"))
                return ArchitectureMaterial("Shiosai_HarbourTimber", new Color(0.44f, 0.29f, 0.19f),
                                            smoothness: 0.14f);
            if (n.Contains("roof"))
            {
                // SPEC 3.2: slate-blue roofs (#344D62). The first pass used near-white pale blue,
                // which gave the village no value contrast at all against the cream walls - from
                // the road it read as a heap of white boxes. The three variants stay within a
                // few points of the spec anchor so the town still has tonal variety.
                Color roof = n.Contains("_b_") ? new Color(0.16f, 0.25f, 0.33f) :
                             n.Contains("_c_") ? new Color(0.25f, 0.35f, 0.43f) :
                                                new Color(0.204f, 0.302f, 0.384f);
                return ArchitectureMaterial("Shiosai_HarbourRoof" + (n.Contains("_b_") ? "B" : n.Contains("_c_") ? "C" : "A"),
                                            roof, CoastTexture("Shiosai_HarbourRoof_Albedo_v2.png"),
                                            smoothness: 0.34f, metallic: 0.22f);
            }
            if (n.Contains("wall") || n.Contains("render"))
            {
                // SPEC 3.2: cream stucco (#E9DFC8), with two warmer/cooler siblings.
                //
                // The albedo map is deliberately NOT applied any more. Shiosai_HarbourFacade_
                // Albedo_v2.png is a dark weatherboard stripe, and multiplying it over the cream
                // dropped the walls to a mid grey-brown banding that read - correctly, from the
                // road - as "striped shed", not as the spec's cream stucco. A flat authored
                // colour is also the right answer for cel shading: the facades are supposed to
                // be one clean value so the slate roofs and the timber trim are what carry the
                // detail, and so the town reads as a bright mass against the green headland.
                Color plaster = n.Contains("_b_") ? new Color(0.87f, 0.81f, 0.70f) :
                                n.Contains("_c_") ? new Color(0.84f, 0.84f, 0.78f) :
                                                  new Color(0.914f, 0.875f, 0.784f);
                return ArchitectureMaterial("Shiosai_HarbourFacade" + (n.Contains("_b_") ? "B" : n.Contains("_c_") ? "C" : "A"),
                                            plaster, null, smoothness: 0.18f);
            }
        }
        if (n.Contains("house_b"))
        {
            if (n.Contains("roof"))
                return CelMaterial("Shiosai_RoofTile_B", new Color(0.17f, 0.22f, 0.28f), gloss: 0.30f,
                                   spec: 0.22f, rim: 0.45f);
            if (n.Contains("wall") || n.Contains("render"))
                return CelMaterial("Shiosai_Plaster_B", new Color(0.74f, 0.71f, 0.64f), gloss: 0.08f,
                                   spec: 0.07f, rim: 0.30f, texture: SakuraTexture("Sakura_Scree_Albedo.png"));
        }
        if (n.Contains("house_c"))
        {
            if (n.Contains("roof"))
                // FIX #5: was terracotta (0.30,0.21,0.18) which read as an orange roof against
                // the cream walls; SC04 + SPEC 3.2 want dark kawara slate. Shifted to a slate
                // value a few points off RoofTile_B so the town keeps tonal variety but the whole
                // village now reads slate-blue, not orange.
                return CelMaterial("Shiosai_RoofTile_C", new Color(0.22f, 0.28f, 0.34f), gloss: 0.24f,
                                   spec: 0.18f, rim: 0.42f);
            if (n.Contains("wall") || n.Contains("render"))
                return CelMaterial("Shiosai_Plaster_C", new Color(0.62f, 0.60f, 0.56f), gloss: 0.08f,
                                   spec: 0.07f, rim: 0.30f, texture: SakuraTexture("Sakura_Scree_Albedo.png"));
        }

        if (n.Contains("reflector"))
            // Roadside reflectors are retro-reflective, not painted: a high rim and low shade
            // strength is what keeps the orange reading as a light rather than as a sticker.
            return Orange();
        // GUARDRAIL BEFORE THE GENERIC STEEL RULE. The shared Shiosai_Steel is a bright
        // (0.80,0.82,0.80) + rim 0.55 material; under the coast's daylight key that clipped the
        // whole 20 km rail run to pure white, which is why the posts read in gameplay as
        // free-floating white blobs with no beam tying them together. Galvanised steel is a
        // MID grey, so the value is dropped and the rim pulled right back; the posts are a
        // further stop darker than the beam so the rhythm of posts is legible against the sea.
        // PROVISIONAL values, tuned against badcliff.png / messedup.png.
        if (n.Contains("guardrail") && n.Contains("beam"))
            return GalvanisedSteel("Shiosai_GuardrailBeam", new Color(0.55f, 0.57f, 0.56f));
        if (n.Contains("guardrail") && n.Contains("post"))
            return GalvanisedSteel("Shiosai_GuardrailPost", new Color(0.42f, 0.44f, 0.44f));
        if (n.Contains("steel") || n.Contains("beam") || n.Contains("post"))
            return CelMaterial("Shiosai_Steel", new Color(0.80f, 0.82f, 0.80f), gloss: 0.45f,
                               spec: 0.34f, rim: 0.55f);
        if (n.Contains("red") || n.Contains("torii") || n.Contains("band"))
            return CelMaterial("Shiosai_Vermilion", new Color(0.50f, 0.13f, 0.10f), gloss: 0.18f,
                               spec: 0.14f, rim: 0.42f);
        if (n.Contains("white") || n.Contains("tower"))
            return CelMaterial("Shiosai_Whitewash", new Color(0.90f, 0.90f, 0.87f), gloss: 0.12f,
                               spec: 0.10f, rim: 0.35f);
        // Harbour-house glazing is cool and recessed.  Do this before the lighthouse-lantern
        // rule below: both names contain "glass", but windows must never inherit its warm
        // emissive-looking yellow.
        if (n.Contains("window"))
            return CelMaterial("Shiosai_WindowGlass", new Color(0.055f, 0.10f, 0.14f), gloss: 0.72f,
                               spec: 0.52f, rim: 0.38f);
        if (n.Contains("glass") || n.Contains("lantern"))
            return CelMaterial("Shiosai_LampGlass", new Color(1f, 0.88f, 0.52f), gloss: 0.70f,
                               spec: 0.60f, rim: 1.25f);
        // BARK FIRST. "Shiosai_Broadleaf_Trunk_Bark" contains "leaf", so the canopy rule below
        // would otherwise paint every broadleaf trunk with the foliage map.
        if (n.Contains("bark") || n.Contains("trunk"))
            return CelMaterial("Shiosai_Bark", new Color(0.92f, 0.90f, 0.88f), gloss: 0.06f,
                               spec: 0.05f, rim: 0.25f,
                               texture: CoastTexture("Shiosai_Bark_Albedo.png"));
        if (n.Contains("needle"))
            // Needle CANOPY: a detail map is what stops a solid canopy reading as folded
            // paper under the cel shader. Near-white tint so the authored map supplies the
            // colour rather than being multiplied back down to a flat fill.
            return CelMaterial("Shiosai_PineNeedle", new Color(0.86f, 0.94f, 0.82f), gloss: 0.05f,
                               spec: 0.05f, rim: 0.28f,
                               texture: CoastTexture("Shiosai_Needle_Albedo.png"));
        if (n.Contains("canopy") || n.Contains("leaf"))
            return CelMaterial("Shiosai_LeafCanopy", new Color(0.88f, 0.96f, 0.84f), gloss: 0.05f,
                               spec: 0.06f, rim: 0.30f,
                               // High-detail, coast-only canopy albedo.  Kept as a sibling of the
                               // original procedural map so this visual pass never mutates a shared
                               // source asset and can be reverted by changing one reference.
                               texture: CoastTexture("Shiosai_Leaf_Albedo_HQ.png"));
        if (n.Contains("petal") || n.Contains("bloom"))
            return CelMaterial("Shiosai_Hydrangea", new Color(0.45f, 0.46f, 0.82f), gloss: 0.14f,
                               spec: 0.12f, rim: 0.55f,
                               // Keep flowers as an authored, sheltered roadside accent instead
                               // of flat violet geometry.  This is a coast-only texture; it does
                               // not alter Sakura's flora or any shared source material.
                               texture: CoastTexture("Shiosai_HokkaidoBlueFlowers_Albedo.png"));
        if (n.Contains("wood") || n.Contains("trim"))
            return CelMaterial("Shiosai_Timber", new Color(0.30f, 0.24f, 0.19f), gloss: 0.06f,
                               spec: 0.05f, rim: 0.25f,
                               texture: SakuraTexture("Sakura_Bark_Albedo.png"));
        if (n.Contains("roof"))
            return CelMaterial("Shiosai_RoofTile", new Color(0.24f, 0.27f, 0.32f), gloss: 0.26f,
                               spec: 0.20f, rim: 0.45f);
        if (n.Contains("wall") || n.Contains("render"))
            return CelMaterial("Shiosai_Plaster", new Color(0.86f, 0.83f, 0.77f), gloss: 0.08f,
                               spec: 0.07f, rim: 0.30f, texture: SakuraTexture("Sakura_Scree_Albedo.png"));
        if (n.Contains("concrete") || n.Contains("deck") || n.Contains("armour"))
            return CelMaterial("Shiosai_Concrete", new Color(0.66f, 0.65f, 0.62f), gloss: 0.08f,
                               spec: 0.07f, rim: 0.25f,
                               texture: SakuraTexture("Sakura_Scree_Albedo.png"));
        if (n.Contains("hull") || n.Contains("super"))
            return CelMaterial("Shiosai_BoatHull", new Color(0.88f, 0.88f, 0.86f), gloss: 0.30f,
                               spec: 0.25f, rim: 0.50f);
        if (n.Contains("rock") || n.Contains("stack") || n.Contains("plinth") || n.Contains("stone"))
            return RockPropMaterial();
        return null;
    }

    /// <summary>
    /// Galvanised guardrail steel. The coast's default cel setup lights every shaded face with a
    /// strong sea-sky bounce (_ShadeColor 0.60/0.70/0.86 at 0.55) which, on a 20 km run of
    /// bright steel, turned the rail into a glowing blue-white ribbon. A guardrail is a MID,
    /// NEUTRAL grey object, so the shade is neutralised and weakened here. Those two properties
    /// have to be pushed onto the returned material - CelMaterial caches by name and returns
    /// early, so setting them "in" the factory call would silently do nothing (see Orange()).
    /// PROVISIONAL tuning, judged against badcliff.png / messedup.png.
    /// </summary>
    private static Material GalvanisedSteel(string name, Color albedo)
    {
        var m = CelMaterial(name, albedo, gloss: 0.16f, spec: 0.12f, rim: 0.14f);
        m.SetColor("_ShadeColor", new Color(0.58f, 0.60f, 0.64f, 1f));
        m.SetFloat("_ShadeStrength", 0.42f);
        return m;
    }

    private static Material Orange()
    {
        var m = CelMaterial("Shiosai_Reflector", new Color(0.95f, 0.34f, 0.03f), gloss: 0.35f,
                            spec: 0.22f, rim: 0.55f);
        // CelMaterial caches by name and returns early, so the shade strength has to be set on
        // the returned material - setting it "in" the factory call would silently do nothing.
        m.SetFloat("_ShadeStrength", 0.12f);
        return m;
    }

    // --------------------------------------------------------------- landmarks

    private static void BuildLandmarks(Transform root, CoastRoute route)
    {
        var group = new GameObject("Coast Landmarks").transform;
        group.SetParent(root, false);

        // ---- the harbour: town, mole, boats, and the light on its rock ---------------------
        BuildHarbour(group, route);

        // ---- chapter hero landmarks, each anchored to its published route anchor -----------
        BuildGatewayArch(group, route);
        BuildRedBridge(group, route);
        BuildLighthousePromontory(group, route);
        BuildSeaArchGate(group, route);
        BuildCliffTunnel(group, route);

        // ---- red torii standing IN THE SEA, off the fishing village's bay ------------------
        // Anchored to the village chapter, not to "55% of the route": on the 42 km course the
        // fraction resolved to 23.1 km, i.e. into the Lighthouse Climb, where the road is 20 m
        // up a headland and the torii stood in open water nobody ever looks at.
        {
            int i = route.AnchorIndex(AnchorVillageCentre, AnchorVillageCentreM) + SeaToriiLeadSamples;
            i = Mathf.Clamp(i, 0, route.Count - 1);
            var p = route.Position[i];
            var s = route.SideFlat(i);
            float off = -78f;
            var at = new Vector3(p.x + s.x * off, SeaLevelY, p.z + s.z * off);
            float yaw = Mathf.Atan2(-s.x, -s.z) * Mathf.Rad2Deg;   // faces the shore
            var go = Place("Shiosai_SeaTorii", "Shiosai Sea Torii", group, at, yaw, 1f);
            if (go != null) Debug.Log($"[shiosai] sea torii at {at} (d={route.Distance[i]:0} m).");
        }

        // ---- a torii on the grass shoulder, set INLAND of the road ------------------------
        // The first pass put this at offset -9, i.e. straddling the seaward verge: from the
        // rider's eye it filled the whole frame and blocked the sea view it was meant to frame.
        // Anchored to SC_KM_140_ShrineOverlook, which is what the spec calls this landmark.
        {
            int i = route.AnchorIndex(AnchorShrineOverlook, AnchorShrineOverlookM);
            var at = PointAt(route, i, 13.5f);
            float yaw = Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg;
            // FIX #4 (ch3 hero framing). SC03 reads a prominent shrine gate at the descent; at
            // scale 1.0 (~7 m) this torii sat below the rising inland hillside and never appeared
            // in the ch3 rider's-eye render. Scaled up so it clears the slope and reads as the
            // chapter's landmark. KEPT INLAND (+13.5) on purpose: a prior pass at seaward offset
            // -9 filled the frame and blocked the sea view (see the sea-torii note above), so this
            // stays inland and only grows in height rather than moving toward the carriageway.
            Place("Shiosai_Torii", "Shiosai Clifftop Torii", group, at, yaw, 1.7f);
            Debug.Log($"[shiosai] shrine-overlook torii at d={route.Distance[i]:0} m.");
        }


        // ---- offshore sea stacks ----------------------------------------------------------
        // The renders show a HANDFUL of big dramatic stacks gathered off the headlands, not a
        // uniform field. The first pass scattered 55 small ones evenly and they read as litter.
        var rng = new System.Random(ScatterSeed + 7);
        string[] stacks = { "Shiosai_SeaStack_A", "Shiosai_SeaStack_B", "Shiosai_SeaStack_C" };
        // build_sea_stack's own base radius per asset (tools/blender/build_shiosai.py "stacks"
        // entry), and the widest point of its undercut profile is ~1.15x that at the waterline -
        // needed below to keep siblings from silhouette-overlapping into one fused tangle.
        // M4b: these must track build_shiosai.py's "stacks" entry - the stacks were slimmed
        // from 5.4/4.2/8.0 to 3.7/2.9/5.4 to get a spire silhouette instead of a gumdrop.
        var stackBaseRadius = new System.Collections.Generic.Dictionary<string, float>
        {
            { "Shiosai_SeaStack_A", 3.7f }, { "Shiosai_SeaStack_B", 2.9f }, { "Shiosai_SeaStack_C", 5.4f },
        };
        int placed = 0;
        float nextStack = 140f;
        for (int i = 0; i < route.Count; i++)
        {
            if (route.Distance[i] < nextStack) continue;
            // OFFSHORE ROCKS BELONG OFFSHORE. The scatter used to run from 140 m to the end of
            // the route, but chapters 1-2 climb to 420 m through an inland mountain (see
            // Seaness): a sea stack seated at sea level 85-175 m to the side of a 210 m alpine
            // hairpin is a rock hanging in mid-air beside the road, which is exactly the
            // "floating island" the Gateway render showed. Only place them where there is sea.
            if (Seaness(route.Distance[i]) < StackSeanessMin) { nextStack = route.Distance[i]; continue; }
            nextStack = route.Distance[i] + 240f + (float)rng.NextDouble() * 180f;
            int cluster = 2 + rng.Next(3);
            var p = route.Position[i];
            var s = route.SideFlat(i);
            var t = route.Tangent[i];
            float groupOff = -85f - (float)rng.NextDouble() * 90f;
            // Placed members of THIS cluster only: (xz position, footprint radius), so the next
            // member can be rejection-sampled far enough away not to silhouette-overlap them.
            var clusterPlaced = new System.Collections.Generic.List<(Vector2 xz, float footprint)>();
            for (int k = 0; k < cluster; k++)
            {
                string asset = stacks[rng.Next(stacks.Length)];
                float scale = k == 0 ? 2.0f + (float)rng.NextDouble() * 1.3f
                                     : 0.9f + (float)rng.NextDouble() * 1.0f;
                // Undercut sea stacks bulge to ~1.15x their base radius at the waterline (the
                // widest silhouette point) - two stacks whose footprints overlap there don't
                // read as two rocks, they fuse into a single tangled, interpenetrating mass
                // that looks like broken/exploded geometry even though each mesh on its own is
                // perfectly solid (confirmed by isolated render). A little extra margin (1.35x
                // combined radius) keeps them looking like a deliberate cluster, not a single
                // fused blob or a scattered mess.
                float footprint = stackBaseRadius[asset] * scale * 1.15f;

                Vector3 at = default;
                bool ok = false;
                for (int attempt = 0; attempt < 24 && !ok; attempt++)
                {
                    // Tight cluster: one hero stack with smaller siblings crowded around it.
                    float off = groupOff + ((float)rng.NextDouble() - 0.5f) * 46f;
                    float along = ((float)rng.NextDouble() - 0.5f) * 52f;
                    at = new Vector3(p.x + s.x * off + t.x * along, SeaLevelY - 1.6f,
                                     p.z + s.z * off + t.z * along);
                    // Two exclusions learned from the render: stacks parked ON the beach ribbon,
                    // and a cluster dumped across the harbour mouth in front of the lighthouse.
                    if (Vector2.Distance(new Vector2(at.x, at.z), HarbourCentre) < 330f) continue;
                    if (off > -66f) continue;
                    var xz = new Vector2(at.x, at.z);
                    ok = true;
                    foreach (var other in clusterPlaced)
                    {
                        if (Vector2.Distance(xz, other.xz) < (footprint + other.footprint) * 1.35f)
                        { ok = false; break; }
                    }
                }
                if (!ok) continue;   // gave up finding clear space for this sibling - skip it, don't fuse it in
                clusterPlaced.Add((new Vector2(at.x, at.z), footprint));

                // build_sea_stack authors its origin AT THE WATERLINE and carries the wave-cut
                // foot down to -2.5x height below it, so the correct seat is sea level itself.
                // Sinking it "to stop it floating" actually buries the wide base and leaves the
                // narrow undercut at the surface - which is what read as a hovering rock.
                at.y = SeaLevelY - 0.6f;
                var stackGo = Place(asset, $"Sea Stack {placed:000}", group, at,
                      (float)rng.NextDouble() * 360f, scale);
                if (placed == 0 && stackGo != null)
                {
                    // Measure, do not assume: the render kept showing stacks hovering, so log
                    // where the imported mesh actually starts relative to the placed origin.
                    var r0 = stackGo.GetComponentInChildren<MeshRenderer>();
                    if (r0 != null)
                        Debug.Log($"[shiosai] sea stack probe: origin y {at.y:0.00}, " +
                                  $"bounds min y {r0.bounds.min.y:0.00}, max y {r0.bounds.max.y:0.00}.");
                }
                placed++;
            }
        }
        Debug.Log($"[shiosai] landmarks: {placed} sea stacks.");
        BuildFinaleIslands(group, route);
        BuildBackdrop(root, route);
    }

    /// <summary>
    /// SC08 "Island Reveal" finale hero (spec 5.8). The Coastal Highway's payoff is a cluster of
    /// islands off the seaward bow that GROW as the rider closes on the finish, plus a wide
    /// circular overlook/turnaround to arrive at. Without them the ch8 render was just road,
    /// grass and pines - indistinguishable from any other coastal stop.
    ///
    /// Built Unity-side (like the backdrop ridges) rather than in Blender so it tracks the live
    /// centreline. The islands sit NEARER and greener than BuildBackdrop's horizon ridges, so
    /// they read as reachable landmasses - the objective - not as haze.
    /// </summary>
    private static void BuildFinaleIslands(Transform group, CoastRoute route)
    {
        int i0 = route.AnchorIndex(AnchorIslandReveal, AnchorIslandRevealM);
        var p0 = route.Position[i0];
        var fwd = new Vector3(route.Tangent[i0].x, 0f, route.Tangent[i0].z).normalized;
        var side = route.SideFlat(i0);   // seaward is the NEGATIVE side (guardrail lives at -5.2)

        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        var rng = new System.Random(ScatterSeed + 211);

        // (ahead along the road, seaward offset, island run, crest height). Stepped away toward
        // the horizon so the near one is a clear landmass and the far ones recede.
        var isles = new (float ahead, float outM, float len, float h)[]
        {
            ( 360f,  360f, 300f,  85f),
            ( 820f,  560f, 460f, 150f),
            (1360f,  820f, 640f, 220f),
            (2050f, 1160f, 820f, 300f),
        };
        foreach (var (ahead, outM, len, h) in isles)
        {
            var c = p0 + fwd * ahead - side * outM;
            c.y = SeaLevelY - 34f;
            // Islands run roughly along the line of sight so they present a broad flank, not an
            // edge, to the arriving rider.
            var dir = (fwd * 0.65f - side * 0.35f).normalized;
            Ridge(verts, uvs, tris, c - dir * (len * 0.5f), dir, len, len * 0.42f,
                  SeaLevelY - 40f, h * 0.5f, h, rng);
        }

        var isleMat = CelMaterial("Shiosai_FinaleIsland", new Color(0.34f, 0.49f, 0.44f),
                                  gloss: 0.03f, spec: 0.03f, rim: 0.22f);
        AddMesh(group, "Coast Finale Islands",
                Finish("Shiosai_FinaleIslands", verts.ToArray(), uvs.ToArray(), tris),
                isleMat, collider: false);

        // Circular overlook / turnaround at the finish, seated on the seaward bulge at road
        // height so the rider arrives at a viewpoint out over the islands.
        var oc = PointAt(route, i0, -20f);
        oc.y = route.Position[i0].y + 0.06f;
        var dv = new List<Vector3>(); var du = new List<Vector2>(); var dt = new List<int>();
        const int Seg = 40; const float R = 26f;
        int centre = 0;
        dv.Add(oc); du.Add(new Vector2(0.5f, 0.5f));
        for (int k = 0; k <= Seg; k++)
        {
            float a = k / (float)Seg * Mathf.PI * 2f;
            dv.Add(oc + new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R));
            du.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
        }
        for (int k = 1; k <= Seg; k++)
        {
            dt.Add(centre); dt.Add(k + 1); dt.Add(k);   // up-facing
            dt.Add(centre); dt.Add(k); dt.Add(k + 1);   // and its reverse, so winding can't cull it
        }
        var stoneMat = CelMaterial("Shiosai_OverlookStone", new Color(0.74f, 0.72f, 0.67f),
                                   gloss: 0.06f, spec: 0.05f, rim: 0.12f);
        AddMesh(group, "Coast Finale Overlook",
                Finish("Shiosai_FinaleOverlook", dv.ToArray(), du.ToArray(), dt),
                stoneMat, collider: false);

        Debug.Log($"[shiosai] finale: {isles.Length} reveal islands + overlook at " +
                  $"d={route.Distance[i0]:0} m ({oc}).");
    }

    /// <summary>
    /// Far headlands across the water. The concept renders never show an empty horizon - there
    /// is always a hazed green landmass on the far side of the bay - and without it the ocean
    /// plane meets the skybox in a hard line that reads as the edge of the world.
    ///
    /// These are pure silhouette: no detail survives the haze, so they are cheap overlapping
    /// cones on ONE mesh with a desaturated material that fakes aerial perspective.
    /// </summary>
    private static void BuildBackdrop(Transform root, CoastRoute route)
    {
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        var rng = new System.Random(ScatterSeed + 101);

        // Ridge groups placed out to sea and beyond both ends of the route, so the horizon is
        // closed in every direction the rider can look from the road.
        var anchors = new List<(Vector3 at, float run, float yaw, float scale)>();
        for (int g = 0; g < 8; g++)
        {
            float d = route.Length * (0.06f + 0.13f * g);
            int i = route.IndexAt(d);
            var p = route.Position[i];
            var s = route.SideFlat(i);
            float off = -950f - (float)rng.NextDouble() * 1350f;
            anchors.Add((new Vector3(p.x + s.x * off, SeaLevelY - 30f, p.z + s.z * off),
                         900f + (float)rng.NextDouble() * 700f,
                         Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z),
                         1f + (float)rng.NextDouble() * 0.8f));
        }

        foreach (var (at, run, yaw, scale) in anchors)
        {
            var dir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            // Same change as the inland ranges: a far headland is a RIDGE, not a heap of cones.
            // The cones' 8 facets were large enough at this distance to read as folded paper.
            float h = (120f + (float)rng.NextDouble() * 140f) * scale;
            Ridge(verts, uvs, tris, at - dir * (run * 0.5f), dir, run * 1.4f,
                  260f + (float)rng.NextDouble() * 200f, SeaLevelY - 40f, h * 0.55f, h, rng);
        }

        // AERIAL PERSPECTIVE, NOT A DARK FILL. The old material was CelLit hand-authored at
        // (0.15,0.23,0.22) purely so it would not out-brighten the sky - which is exactly what
        // made it read as a flat dark cut-out. RidgeHaze fades the ridge into RidgeHazeColour by
        // distance and pools extra haze low down, so the backdrop now recedes instead of
        // standing there. Colours are authored as real (if desaturated) coastal green again.
        var mat = RidgeMaterial("Shiosai_FarHeadland",
                                baseColour: new Color(0.20f, 0.30f, 0.27f),
                                crestColour: new Color(0.27f, 0.36f, 0.34f),
                                hazeStart: 700f, hazeFull: 3600f,
                                hazeMin: 0.22f, hazeMax: 0.58f,
                                // These headlands are only 70-260 m tall, so a 150 m valley-fill
                                // height hazed the WHOLE landmass away and left the bare
                                // ocean/sky seam the backdrop exists to close (verified on the
                                // reverse start render). The fill now only eats their footings.
                                baseY: SeaLevelY - 40f, valleyHeight: 60f, valleyFill: 0.34f,
                                crestHeight: 200f);
        AddMesh(root, "Coast Backdrop Headlands",
                Finish("Shiosai_Backdrop", verts.ToArray(), uvs.ToArray(), tris),
                mat, collider: false);
        Debug.Log($"[shiosai] backdrop: {anchors.Count} far ridges, {verts.Count} verts.");
    }

    // --------------------------------------------------------------- distant depth layers

    // PROVISIONAL art tuning for the background-depth pass.
    //
    // WHY THIS EXISTS: the gameplay frames (good_graphics/shiosai_play/*.png) read flat behind
    // the near roadside trees, while the 20 concept renders (ShiosaiCoast_01..20.png) are built
    // out of LAYERS - a detailed foreground, a softer midground treeline, then two or three
    // ranges of hazed mountains stacked back to the horizon. BuildBackdrop already closes the
    // SEAWARD horizon, but inland there was nothing at all beyond the 210 m-wide headland
    // cross-section, so the sky came straight down to the grass.
    //
    // Everything here is pure silhouette: no detail survives at these distances, so each layer
    // is ONE merged mesh with ONE flat, progressively sky-shifted material. That is what buys
    // the depth cheaply - three draw calls, no new textures, no streaming.

    /// <summary>Inland mountain bands: (offset from the road, height low, height high, colour).</summary>
    /// PROVISIONAL tuning. The offsets are NOT cosmetic: the roadside hillside occludes
    /// everything below roughly roadY + 0.34 x distance, so a range only reads if it is both
    /// far enough away to sit behind the haze and tall enough to clear that sightline.
    private static readonly (float offset, float hLo, float hHi, Color tint, string name)[] InlandBands =
    {
        // HEIGHTS ARE SET BY SIGHTLINE, NOT BY REALISM. The near inland hillside tops out at
        // roadY + 74 m only ~210 m from the camera - about 19 degrees of elevation. A 620 m peak
        // 2.3 km away subtends only 15 degrees, so the first version of these bands was built,
        // emitted 2166 verts, and was completely hidden behind the verge. Each band is raised
        // until it clears that near ridge with margin.
        //
        // CARDBOARD-CONE FIX (opening milestone 2). These tints were previously crushed almost
        // black - (0.11,0.17,0.15) and friends - for one reason: the bands were CelLit
        // silhouettes with no haze between them and the camera, so the ONLY way to stop them
        // out-brightening the sky was to author them darker than it. That is what produced flat
        // dark-teal cut-outs with hard edges. They now render through MapleRide/HDRP/RidgeHaze,
        // which applies its own distance ramp and valley fill per band (see RidgeHazeFor below),
        // so the authored colour can go back to being a plausible forested landform and the
        // LAYERING is produced by haze rather than by four shades of near-black.
        ( 2300f,  880f, 1250f, new Color(0.19f, 0.28f, 0.24f), "Foot"),
        ( 3400f, 1200f, 1650f, new Color(0.20f, 0.29f, 0.26f), "Near"),
        ( 4900f, 1600f, 2100f, new Color(0.21f, 0.30f, 0.28f), "Mid"),
        ( 6600f, 1950f, 2500f, new Color(0.22f, 0.31f, 0.30f), "Far"),
    };

    /// <summary>
    /// Per-band aerial-perspective ramp: (haze at the near end, haze at the far end).
    /// PROVISIONAL. Stepping these up with band index is what makes four ranges read as four
    /// RECEDING layers rather than as one teal mass.
    ///
    /// TUNED BY RENDER. The first pass ran 0.30-0.91 and washed every band out to the same pale
    /// ghost-blue: at that strength the near Foot band lost all its own colour, so the four
    /// layers had nothing left to separate them and the ranges read as flat pale cards. The
    /// near bands now keep a substantial amount of their green and only the Far band approaches
    /// full dissolution.
    /// </summary>
    private static readonly (float min, float max)[] InlandBandHaze =
    {
        (0.12f, 0.36f),   // Foot: keep the forested face readable
        (0.22f, 0.48f),   // Near
        (0.34f, 0.61f),   // Mid
        (0.48f, 0.74f),   // Far: still atmospheric, never a white card
    };

    /// <summary>
    /// No inland cone may come within this many metres of the road. Cone()'s third argument is
    /// a RADIUS, not a width - the first pass treated it as a width and produced 800 m-radius
    /// peaks at a 650 m offset, i.e. a pale wall the chase camera rode straight through.
    /// </summary>
    private const float InlandClearanceM = 900f;

    /// <summary>
    /// Multipliers tried, in order, on a range band's nominal inland offset before the ridge is
    /// given up on. PROVISIONAL. See BuildInlandRanges: on the folded mountain half of the route
    /// the nominal offset is almost always fouled by some other limb of the road, and a bare
    /// accept/reject test left those chapters with no mountain horizon at all.
    /// </summary>
    private static readonly float[] PushOut = { 1.0f, 1.35f, 1.8f };

    /// <summary>Metres along the route between inland ridge peaks.</summary>
    private const float InlandPeakStrideM = 2000f;

    // --- route clearance ---------------------------------------------------------------------

    private static CoastRoute _clearRoute;
    private static Dictionary<long, List<int>> _clearGrid;
    private const float ClearCell = 256f;

    /// <summary>
    /// Plan distance from an arbitrary world XZ point to the NEAREST part of the centreline,
    /// capped at <paramref name="cap"/> (anything further just returns <paramref name="cap"/>).
    ///
    /// ROOT CAUSE OF THE "FLAT PALE WEDGE CUTTING THROUGH THE HILLSIDE" DEFECT. The inland
    /// mountain ranges are placed 3-6.2 km INLAND OF EACH ROUTE STATION, and their only guard was
    /// <see cref="InlandClearanceM"/>, which keeps a peak clear of the station it was placed from.
    /// That was sufficient for a 2.892 km road running broadly in one direction. The 42 km Grand
    /// Coast doubles back across an 11 x 25 km footprint, so "6 km inland of chapter 7" is
    /// somewhere in the middle of chapters 4-5 - a 1.4 km-radius hazed cone dropped straight
    /// through the road, which rendered as a huge featureless pale plane slicing across the
    /// foreground. A peak must therefore clear the WHOLE route, not the station that spawned it.
    /// </summary>
    private static float RouteClearance(CoastRoute r, float x, float z, float cap)
    {
        if (!ReferenceEquals(_clearRoute, r) || _clearGrid == null)
        {
            _clearRoute = r;
            _clearGrid = new Dictionary<long, List<int>>();
            for (int k = 0; k < r.Count; k++)
            {
                var q = r.Position[k];
                long key = ((long)Mathf.FloorToInt(q.x / ClearCell) << 32) ^
                           (uint)Mathf.FloorToInt(q.z / ClearCell);
                if (!_clearGrid.TryGetValue(key, out var list)) _clearGrid[key] = list = new List<int>();
                list.Add(k);
            }
        }

        int cx = Mathf.FloorToInt(x / ClearCell), cz = Mathf.FloorToInt(z / ClearCell);
        int span = Mathf.CeilToInt(cap / ClearCell);
        float best = cap * cap;
        for (int ax = -span; ax <= span; ax++)
        for (int az = -span; az <= span; az++)
        {
            long key = ((long)(cx + ax) << 32) ^ (uint)(cz + az);
            if (!_clearGrid.TryGetValue(key, out var list)) continue;
            foreach (int k in list)
            {
                var q = r.Position[k];
                float d2 = (q.x - x) * (q.x - x) + (q.z - z) * (q.z - z);
                if (d2 < best) best = d2;
            }
        }
        return Mathf.Sqrt(best);
    }

    /// <summary>
    /// True when a world XZ point lies on the INLAND side of the nearest stretch of centreline
    /// (positive cross-section offset). Used to keep the background ranges off the water: on a
    /// route that doubles back, "inland of station i" is frequently out at sea as far as the
    /// nearest station is concerned.
    /// </summary>
    private static bool IsInland(CoastRoute r, float x, float z)
    {
        int best = -1; float bestD2 = float.MaxValue;
        // A coarse scan is enough: the answer only has to be right to within the sampling
        // interval of the coastline's own curvature, and this runs a few hundred times.
        for (int k = 0; k < r.Count; k += 8)
        {
            var q = r.Position[k];
            float d2 = (q.x - x) * (q.x - x) + (q.z - z) * (q.z - z);
            if (d2 < bestD2) { bestD2 = d2; best = k; }
        }
        if (best < 0) return true;
        var p = r.Position[best];
        var s = r.SideFlat(best);
        return (x - p.x) * s.x + (z - p.z) * s.z > 0f;
    }

    /// <summary>Metres along the route between distant tree clumps.</summary>
    private const float DistantTreeStrideM = 13f;
    /// <summary>
    /// Inland band the hazed silhouette treeline occupies, in metres from the centreline.
    /// It must start BEHIND the detailed roadside scatter (which stops around 94 m) and end
    /// INSIDE the land ribbon's outermost cross-section node (210 m) - see BuildDistantTreeline.
    /// PROVISIONAL art tuning.
    /// </summary>
    private const float DistantTreeNearM = 128f;
    private const float DistantTreeFarM = 198f;

    /// <summary>
    /// Metres of terrain a tree must have outboard of it before it may be planted. Keeps a
    /// canopy from overhanging the very edge triangle of the ribbon (which reads as floating
    /// from the road even when the trunk is technically on the mesh).
    /// </summary>
    private const float TreeGroundMarginM = 6f;

    /// <summary>
    /// Adds the two background layers the coast was missing: receding inland mountain ranges,
    /// and a sparse hazed tree-silhouette layer on the far hillside between the detailed
    /// roadside trees and those mountains.
    /// </summary>
    private static void BuildDistantDepth(Transform root, CoastRoute route)
    {
        var group = new GameObject("Coast Distance").transform;
        group.SetParent(root, false);

        BuildInlandRanges(group, route);
        BuildDistantTreeline(group, route);
    }

    /// <summary>
    /// Three ranges of hazed inland mountains, each its own mesh and its own progressively
    /// sky-shifted material, so they read as SEPARATE layers rather than as one blue mass.
    /// Bases sit at sea level and the peaks are tall enough to clear the 74 m headland ridge
    /// the rider is looking over - a shorter range is simply invisible from the road.
    /// </summary>
    private static void BuildInlandRanges(Transform group, CoastRoute route)
    {
        int total = 0;
        for (int band = 0; band < InlandBands.Length; band++)
        {
            var (offset, hLo, hHi, tint, label) = InlandBands[band];
            var rng = new System.Random(ScatterSeed + 401 + band * 17);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();

            // Walk the whole route so the range follows the coast rather than sitting in one
            // place, and run past both ends so the player never rides off the end of it.
            for (float d = -2400f; d <= route.Length + 2400f; d += InlandPeakStrideM)
            {
                int i = route.IndexAt(Mathf.Clamp(d, 0f, route.Length));
                var p = route.Position[i];
                var s = route.SideFlat(i);
                var t = route.Tangent[i];
                float overrun = d < 0f ? d : (d > route.Length ? d - route.Length : 0f);

                // RIDGES, NOT CONES.
                //
                // Each band used to be a scatter of 8-segment cones up to 1.4 km across. At that
                // size an 8-gon shows the viewer two or three enormous flat facets, so a "range"
                // rendered as exactly what the QA pass called it: flat cardboard triangles with
                // straight edges standing on the horizon. Spec 3.3 asks for LAYERED RANGES with
                // atmospheric perspective, and a range is a continuous serrated crest, so each
                // band is now a run of long ridges laid along the route with a saw-toothed
                // skyline and tapered ends that sink into the haze.
                float ridgeLen = 2600f + (float)rng.NextDouble() * 1800f;
                float depth = 420f + (float)rng.NextDouble() * 380f;
                float o = Mathf.Max(offset * (0.90f + (float)rng.NextDouble() * 0.26f),
                                    InlandClearanceM + depth);

                // Must clear the WHOLE route, not just the station it was placed from - but the
                // test has to be per-POINT along the crest, not one test with the ridge's whole
                // length folded into the radius. Folding the length in demanded ~3 km of
                // clearance from every part of a 42 km route that doubles back on itself, which
                // rejected almost every ridge and left the horizon empty - the ranges were being
                // built and then silently discarded.
                //
                // REJECTION IS THE LAST RESORT, NOT THE FIRST. On the Gateway/Switchback half
                // the route folds back on itself so tightly that a point 2.3 km from THIS
                // station is often <1 km from another one, so a pure accept/reject test deleted
                // every range in exactly the two chapters that most needed a mountain horizon.
                // Push the ridge further out first; only skip if even the far cap is fouled.
                float needed = depth * 0.5f + InlandClearanceM;
                bool clear = false;
                Vector3 from = Vector3.zero;
                float usedPush = 1f;
                foreach (float push in PushOut)
                {
                    float oTry = o * push;
                    var midTry = new Vector3(p.x + s.x * oTry + t.x * overrun,
                                             SeaLevelY,
                                             p.z + s.z * oTry + t.z * overrun);
                    from = midTry - t * (ridgeLen * 0.5f);
                    clear = true;
                    // INLAND ONLY. On a route that doubles back, "the inland side of station i"
                    // can be out over open water as far as station j is concerned - that is how
                    // a range band ended up standing on the sea at the right of the overlook
                    // render. Reject any crest point that is on the seaward side of the nearest
                    // stretch of road.
                    for (int sample = 0; sample <= 6 && clear; sample++)
                    {
                        var q = from + t * (ridgeLen * (sample / 6f));
                        if (RouteClearance(route, q.x, q.z, needed) < needed) clear = false;
                        else if (!IsInland(route, q.x, q.z)) clear = false;
                    }
                    if (clear) { usedPush = push; break; }
                }
                if (!clear) continue;

                // A pushed-out ridge is made only slightly taller (NOT proportionally): scaling
                // height by the full push factor produced 7 km peaks that filled the whole sky.
                float hs = Mathf.Lerp(1f, usedPush, 0.45f);
                Ridge(verts, uvs, tris, from, t, ridgeLen * usedPush, depth * usedPush,
                      SeaLevelY - 90f, hLo * hs, hHi * hs, rng);
            }

            // AERIAL PERSPECTIVE, done by the shader rather than by authoring four shades of
            // near-black. The haze ramp is keyed off the band's own nominal offset so the
            // distance fade lands where the range actually stands, and the valley-fill height
            // scales with the band's peak height so the FEET dissolve while the crests survive.
            var (hzMin, hzMax) = InlandBandHaze[Mathf.Min(band, InlandBandHaze.Length - 1)];
            var mat = RidgeMaterial($"Shiosai_InlandRange_{label}",
                                    baseColour: tint,
                                    crestColour: tint * 1.34f,
                                    hazeStart: offset * 0.45f, hazeFull: offset * 1.35f,
                                    hazeMin: hzMin, hazeMax: hzMax,
                                    baseY: SeaLevelY - 90f,
                                    valleyHeight: hHi * 0.72f, valleyFill: 0.62f,
                                    crestHeight: hHi);
            var meshR = Finish($"Shiosai_InlandRange_{label}", verts.ToArray(), uvs.ToArray(), tris);
            AddMesh(group, $"Inland Range {label}", meshR, mat, collider: false);
            total += verts.Count;
        }
        Debug.Log($"[shiosai] inland ranges: {InlandBands.Length} bands, {total} verts.");
    }

    /// <summary>
    /// The missing MIDGROUND: a sparse, desaturated tree-silhouette layer on the far hillside,
    /// between the detailed roadside pines/broadleaves (which stop around 94 m inland) and the
    /// mountains. Deliberately NOT more copies of the full-detail GLB props - at 120-200 m the
    /// canopy is a shape and a value, so these are merged cones on one mesh with one hazy
    /// material, which is why a whole extra treeline costs one draw call.
    /// </summary>
    private static void BuildDistantTreeline(Transform group, CoastRoute route)
    {
        var rng = new System.Random(ScatterSeed + 733);
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        int trees = 0;
        float next = 10f;

        for (int i = 0; i < route.Count; i++)
        {
            float d = route.Distance[i];
            if (d < next) continue;
            next = d + DistantTreeStrideM * (0.6f + (float)rng.NextDouble() * 0.9f);

            int clump = 3 + rng.Next(5);
            for (int k = 0; k < clump; k++)
            {
                // A BAND BEHIND THE ROADSIDE DETAIL, BUT STILL ON THE HILL.
                //
                // The previous pass asked for 210-390 m inland. The land ribbon's outermost
                // cross-section node is at 210 m, so from 210 m out there is no ground at all -
                // SurfaceHeight simply extrapolated the ridge height and the whole layer was
                // planted in mid-air above the skyline. That is the "tons of floating trees"
                // the player was seeing. The band now lives inside the real hillside
                // (DistantTreeNearM..the ribbon's own edge, minus a margin) and is additionally
                // clamped per station, so a switchback's shortened profile cannot strand it.
                float o = DistantTreeNearM +
                          (float)rng.NextDouble() * (DistantTreeFarM - DistantTreeNearM);
                float along = ((float)rng.NextDouble() - 0.5f) * 22f;
                int idx = Mathf.Clamp(i + Mathf.RoundToInt(along / 3f), 0, route.Count - 1);
                TerrainSpan(route, idx, out _, out float inland);
                float edge = inland - TreeGroundMarginM;
                if (edge < DistantTreeNearM) continue;   // clamped bend: no room for this band
                var at = PointAt(route, idx, Mathf.Min(o, edge));
                if (at.y < 2f) continue;   // never floats over the water

                float h = 12f + (float)rng.NextDouble() * 12f;
                Cone(verts, uvs, tris, at + Vector3.up * h * 0.5f, h,
                     h * (0.22f + (float)rng.NextDouble() * 0.16f),
                     ((float)rng.NextDouble() - 0.5f) * 0.18f);
                trees++;
            }
        }

        // Hazier and bluer than the near foliage, and a touch lighter - the far hillside must
        // sit BEHIND the roadside trees tonally, not compete with them.
        // Measured, not guessed: the first pass asked for (0.30, 0.44, 0.38) and photographed as
        // RGB ~(150, 180, 190) - a pale blue spike field. The cel ramp's cool shade colour plus
        // the near-white rim lift this layer a long way, so the albedo has to be driven much
        // darker than the value you want on screen. rim = 0 keeps the silhouettes matte.
        var mat = CelMaterial("Shiosai_DistantTrees", new Color(0.10f, 0.19f, 0.15f),
                              gloss: 0.0f, spec: 0.0f, rim: 0.0f);
        AddMesh(group, "Distant Treeline",
                Finish("Shiosai_DistantTrees", verts.ToArray(), uvs.ToArray(), tris),
                mat, collider: false);
        Debug.Log($"[shiosai] distant treeline: {trees} silhouettes, {verts.Count} verts.");
    }

    private static void BuildHarbour(Transform group, CoastRoute route)
    {
        // THE HARBOUR IS A CHAPTER, NOT A COINCIDENCE OF WORLD Z.
        // This used to be "the first sample whose z >= -940", a heuristic inherited from the
        // 2.892 km mock. On the 42 km route that resolves to chainage 22.7 km - the Red Bridge /
        // Lighthouse Climb boundary - so the Fishing Village chapter (16.0-20.0 km) rendered as
        // empty coast road while a whole fishing town stood 4.7 km down the course. Anchor it to
        // the published SC_KM_180_VillageCenter instead.
        int hi = route.AnchorIndex(AnchorVillageCentre, AnchorVillageCentreM);
        Debug.Log($"[shiosai] harbour anchored to {AnchorVillageCentre} " +
                  $"(d={route.Distance[hi]:0} m, p={route.Position[hi]}).");

        var hp = route.Position[hi];
        var hs = route.SideFlat(hi);
        var ht = route.Tangent[hi];
        float baseYaw = Mathf.Atan2(ht.x, ht.z) * Mathf.Rad2Deg;

        var town = new GameObject("Shiosai Harbour").transform;
        town.SetParent(group, false);
        var rng = new System.Random(ScatterSeed + 3);

        // 2026-09-27 TUNA PORT: the chapter is rebuilt as a working Japanese port town (market
        // hall, quay, fleet, fishmonger street, machiya hillside) - ShiosaiCoastEnvironment.TunaPort*.cs.
        // The legacy house/mole/boat layout below is kept intact as the TunaPortEnabled=false fallback.
        if (TunaPortEnabled)
        {
            BuildTunaPort(town, route);
            HarbourCentre = new Vector2(hp.x, hp.z);
            return;
        }

        // ---- the town ----------------------------------------------------------------------
        // A fishing port is DENSE: houses packed gable-to-gable along the waterfront lane, not
        // a suburb sprayed across a meadow. The first pass used 26 m spacing over 4 deep rows
        // and read as detached bungalows on a golf course. Tight rows, aligned to the road.
        // Dedicated benchmark kit.  The original simple houses remain on disk as a reversible
        // fallback; only the harbour descent uses these higher-detail coastal variants.
        string[] houses = { "Shiosai_HarbourHouse_A", "Shiosai_HarbourHouse_B", "Shiosai_HarbourHouse_C" };
        int built = 0;
        // Preserve a road-side green buffer and let the homes step UP the slope.  The old first
        // row began 14 m off the centreline and produced a continuous façade wall in the cycling
        // camera.  These four rows still read as a compact harbour town from the overlooks, but
        // create a real reveal of the sea, houses, then harbour as the rider descends.
        // A benchmark district is a *place*, not a city generator: keep this deliberately to a
        // 230 m-long, three-terrace slice.  That gives the ride camera room for sky, sea and
        // roadside planting, while keeping enough frontage to judge materials and LOD density.
        // SEAWARD rows first, then the inland terraces. A fishing village is defined by its
        // WATERFRONT: the previous pass put every house on the inland hillside above the road,
        // so the ch4 establishing shot showed a hill of houses with an empty beach below and
        // read as a hill town, not a fishing port. The two negative offsets put a packed row of
        // cottages down on the foreshore beside the boats and the breakwater, which is what
        // makes the chapter read as a harbour. Houses whose lot is actually in the water are
        // dropped by the y guard below, so a steep-shored station simply builds fewer.
        // PROVISIONAL (illustrative visual tuning, not design requirements).
        const float HarbourJitterAlong = 0.40f;    // fraction of row pitch
        const float HarbourJitterLateralM = 9.0f;  // metres across the row
        const float HarbourLotVacancy = 0.09f;     // share of lots left as yard / slipway
        float[] rowOffsets = { -46f, -33f, -21f, 17f, 28f, 40f, 54f, 70f, 88f, 108f };
        // DENSITY (Stage 3, PROVISIONAL). The reference (ShiosaiCoast_06.png) is a town packed
        // gable-to-gable in tiers up a forested slope around the basin - not six sparse rows on
        // a lawn. Rows went 6 -> 10, lot pitch 16 m -> 11.5 m, and the vacancy rate 17% -> 9%.
        // Depth index is derived from the OFFSET BAND rather than the row number, so adding or
        // removing a row can no longer drive `half` negative and silently empty a terrace.
        for (int row = 0; row < rowOffsets.Length; row++)
        {
            float off = rowOffsets[row];
            float a = Mathf.Abs(off);
            int depth = a < 24f ? 0 : a < 42f ? 1 : a < 60f ? 2 : a < 80f ? 3 : 4;
            int half = Mathf.Max(4, 13 - depth * 2);
            float pitch = 11.5f + depth * 1.4f;
            for (int k = -half; k <= half; k++)
            {
                // PROVISIONAL (visual tuning): a real fishing town grows lot by lot, so the
                // rows must not survey as a grid. Jitter is now ~40% of pitch and 30% of row
                // spacing, and one lot in six is simply left empty (garden/yard/slipway).
                // At the previous +/-3.2 m the aerial (diag_shiosai_harbour.png) read as a
                // suburban subdivision, which is the opposite of the chapter's identity.
                if (rng.NextDouble() < HarbourLotVacancy) continue;
                float along = k * pitch + ((float)rng.NextDouble() - 0.5f) * pitch * HarbourJitterAlong;
                int idx = Mathf.Clamp(hi + Mathf.RoundToInt(along / 3f), 2, route.Count - 3);
                var at = PointAt(route, idx,
                                 off + ((float)rng.NextDouble() - 0.5f) * HarbourJitterLateralM);
                if (at.y < (off < 0f ? 0.85f : 1.5f)) continue;   // never stand a house in the sea
                // Eaves face the lane: only a few degrees of jitter, or the town looks bombed.
                // "IDENTICAL BLOCKS" FIX: the waterfront rows keep their tight alignment (a
                // working quayside really is built to the lane), but the inland terraces get a
                // much wider spread. Real hillside lots follow their own contour, and +/-15 deg
                // across the whole town was small enough that from the ch4 camera every house
                // presented the same face at the same angle - the single biggest reason they
                // read as one repeated prop. PROVISIONAL.
                float yawJitterDeg = off < 0f ? 15f : 46f;
                float yaw = Mathf.Atan2(route.Tangent[idx].x, route.Tangent[idx].z)
                            * Mathf.Rad2Deg + 90f
                            + ((float)rng.NextDouble() - 0.5f) * yawJitterDeg;
                // Avoid a landmark-sized house in every lot.  Most of the kit is a one- or
                // two-storey weatherboard home; the larger C variant is an occasional inn/
                // fishing cooperative that gives the ridge a readable focal point.
                int variant = rng.NextDouble() < 0.16 ? 2 : rng.Next(2);
                var house = Place(houses[variant], $"Harbour House {built:000}", town, at,
                      yaw, 0.78f + (float)rng.NextDouble() * 0.16f);
                // M3: give each lot its OWN wall and roof colour. The three GLB variants only
                // carry three shared palettes between them, so from the overlook the town read
                // as a grid of identical white boxes with identical navy roofs. The references
                // (ShiosaiCoast_03/10/14) show a mixed coastal town: creams and whites next to
                // pale blue, terracotta and sage, under warm red-brown tile as well as slate.
                RecolourVillageHouse(house,
                                     rng.Next(VillageWallColours.Length),
                                     rng.Next(VillageRoofColours.Length),
                                     rng.Next(VillageTimberColours.Length));
                built++;
            }
        }

        // ---- the mole ----------------------------------------------------------------------
        // Shiosai_Breakwater is a 12 m caisson lying along its local +X. The first pass stepped
        // it along the seaward axis while ALSO yawing each section progressively, so the long
        // axis stopped agreeing with the step direction and the arm rendered as a zig-zag
        // staircase of loose blocks. One straight arm, step < section length, one fixed yaw.
        const float MoleScale = 1.15f;
        const float MoleStep = 12f * MoleScale * 0.94f;   // slight overlap welds the run
        const int MoleSections = 13;
        var seaward = new Vector3(-hs.x, 0f, -hs.z).normalized;
        // Same convention as the guardrail: yaw that maps the prop's local +X onto a direction.
        float moleYaw = Mathf.Atan2(seaward.x, seaward.z) * Mathf.Rad2Deg;
        var moleRoot = new Vector3(hp.x + hs.x * -30f - ht.x * 52f, SeaLevelY,
                                   hp.z + hs.z * -30f - ht.z * 52f);
        Vector3 moleHead = moleRoot;
        for (int k = 0; k < MoleSections; k++)
        {
            var at = moleRoot + seaward * (k * MoleStep);
            Place("Shiosai_Breakwater", $"Harbour Mole {k:00}", town, at, moleYaw, MoleScale);
            moleHead = at;
        }

        // ---- TETRAPOD ARMOUR along the OUTER face of the mole --------------------------------
        // The art-director reference (ShiosaiCoast_06.png) reads as a real working port largely
        // because of the ragged grey heap of tetrapods on the seaward side of the mole - without
        // them a breakwater is just a grey kerb in the water. `ht` is the basin-ward transverse
        // (the moored boats use +ht), so the exposed face is -ht.
        //
        // Two staggered rows, jittered in offset/height/yaw, because cast armour is DUMPED, not
        // laid: a tidy row reads as fence posts. All PROVISIONAL.
        const float TetraScale = 1.30f;
        const float TetraStep = 4.4f;
        const float TetraOuterOffsetM = 8.6f;   // from the mole centreline to the first row
        var basinward = new Vector3(ht.x, 0f, ht.z).normalized;
        int tetraCount = Mathf.CeilToInt((MoleSections - 1) * MoleStep / TetraStep) + 2;
        for (int k = 0; k < tetraCount; k++)
        {
            for (int row = 0; row < 2; row++)
            {
                float along = k * TetraStep + row * TetraStep * 0.5f;
                float outAt = TetraOuterOffsetM + row * 3.5f
                              + ((float)rng.NextDouble() - 0.5f) * 1.6f;
                var at = moleRoot + seaward * along - basinward * outAt
                         + Vector3.up * (row == 0 ? -0.35f : -1.25f)
                         + Vector3.up * (float)rng.NextDouble() * 0.5f;
                Place("Shiosai_Tetrapod", $"Mole Armour {k:00}{(row == 0 ? 'a' : 'b')}", town, at,
                      (float)rng.NextDouble() * 360f,
                      TetraScale * (0.85f + (float)rng.NextDouble() * 0.35f));
            }
        }

        // ---- moored boats -------------------------------------------------------------------
        // Alongside the arm, INSIDE the basin - the first pass moored them on the sand.
        for (int k = 0; k < 12; k++)
        {
            var at = moleRoot + seaward * (14f + k * 9.5f)
                     + new Vector3(ht.x, 0f, ht.z) * (17f + (float)rng.NextDouble() * 16f)
                     + Vector3.down * 0.30f;
            Place("Shiosai_FishingBoat", $"Harbour Boat {k:00}", town, at,
                  Mathf.Atan2(seaward.x, seaward.z) * Mathf.Rad2Deg + 90f
                  + ((float)rng.NextDouble() - 0.5f) * 16f,
                  1.45f + (float)rng.NextDouble() * 0.75f);
        }

        // ---- THE HARBOUR LIGHT, standing at the head of the mole ----------------------------
        // The BIG white lighthouse is the Lighthouse Climb chapter's hero and now stands on its
        // own promontory at SC_KM_275 (see BuildLighthousePromontory). What guards a fishing
        // harbour entrance is the small red-and-white breakwater light that reference render 05
        // shows on the mole head - so the village keeps a light, but not THE light.
        {
            var at = moleHead + seaward * 4f + new Vector3(0f, 2.1f, 0f);
            var lh = Place("Shiosai_Lighthouse", "Shiosai Harbour Light", town, at,
                           baseYaw + 90f, HarbourLightScale);
            if (lh != null) Debug.Log($"[shiosai] harbour light on mole head at {at}.");
        }

        HarbourCentre = new Vector2(hp.x, hp.z);
    }

    /// <summary>XZ centre of the harbour, used to keep offshore scatter out of the basin.</summary>
    private static Vector2 HarbourCentre;

    /// <summary>
    /// PROVISIONAL (illustrative visual tuning, not a design requirement). Wall colours for the
    /// fishing town, read off the reference art: mostly cream/white with a minority of painted
    /// houses. Order matters only in that the first entries are drawn more often by chance.
    /// </summary>
    private static readonly Color[] VillageWallColours =
    {
        // MEASURED, NOT GUESSED ("still looks Roblox / flat tan-cream palette" fix).
        //
        // The old palette was authored as seven genuinely different hues in the 0.25-0.32 band
        // and photographed as SEVEN SHADES OF THE SAME TAN. Sampling diag_shiosai_ch4_village
        // .png explains why: the coast's key light is strongly warm, so the effective per-channel
        // gain from authored tint to screen pixel measures about R:G:B = 1 : 0.84 : 0.65. Any
        // tint authored neutral or warm therefore lands on screen as saturated ochre, and the
        // cool entries ("pale sea blue", "sage") were pulled back to tan before they ever
        // reached the frame.
        //
        // These are authored with B ABOVE R by roughly the inverse of that gain, so the warm
        // light cancels and the town lands where the port-town reference (ShiosaiCoast_06.png)
        // actually sits: muted grey-blue plaster and weathered timber under dark tile, with one
        // warm sand note kept as a minority so the village is not monochrome.
        // Target screen values are noted per entry. ALL PROVISIONAL.
        // A SECOND, MORE AGGRESSIVE PASS WAS TRIED AND REVERTED - recording it so nobody spends
        // the afternoon again. Re-measuring the gain on an unclipped surface suggests the sunlit
        // gain is nearer 1 : 0.45 : 0.25, and dividing these tints through by THAT lands the
        // sunlit faces on target but drives the (much larger) shaded faces to a saturated toy
        // blue - the render went from "coastal town" to "blue plastic village" in one step. The
        // values below are the compromise that actually photographs correctly; the residual warm
        // cast on the sunlit slopes of the two or three NEAREST buildings is a LIGHTING problem,
        // not a palette one, and is deferred (see the milestone report).
        new Color(0.273f, 0.342f, 0.463f),   // pale grey-blue plaster   -> ~(172,180,188)
        new Color(0.296f, 0.357f, 0.468f),   // weathered off-white grey -> ~(186,188,190)
        new Color(0.210f, 0.271f, 0.382f),   // muted slate blue-grey    -> ~(132,143,155)
        new Color(0.223f, 0.285f, 0.340f),   // sage grey-green          -> ~(140,150,138)
        new Color(0.165f, 0.180f, 0.205f),   // weathered timber brown   -> ~(104,95,83)
        new Color(0.118f, 0.138f, 0.175f),   // yakisugi charred cedar   -> ~( 74, 73, 71)
        new Color(0.280f, 0.315f, 0.369f),   // muted sand grey (warm minority) -> ~(176,166,150)
    };

    /// <summary>
    /// PROVISIONAL. Roof colours: SPEC 3.2 + SC04 make dark kawara SLATE the village note - the
    /// harbour reads as a mass of slate-blue tile roofs over cream walls, NOT warm pantile. The
    /// palette is unified into the slate/grey family (with a weathered grey-green metal note for
    /// variety) so no house renders orange/terracotta from the road. FIX #5.
    ///
    /// "BLOCKY/ROBLOX HOUSES" FIX: every entry is driven ~30% darker and cooler than the FIX #5
    /// values. The sunlit slope of a roof at these tints was photographing as a pale pinkish tan
    /// (sampled ~(154,128,121)), so from the road the town read as light roofs on light walls -
    /// no value hierarchy at all. Kawara is the DARKEST thing in a port town; the roofs have to
    /// stay dark even on the sunlit slope for the silhouettes to separate.
    /// </summary>
    private static readonly Color[] VillageRoofColours =
    {
        new Color(0.132f, 0.196f, 0.253f),   // slate blue (spec 3.2 anchor, driven darker)
        new Color(0.104f, 0.163f, 0.219f),   // dark slate
        new Color(0.155f, 0.220f, 0.276f),   // lighter slate
        new Color(0.092f, 0.148f, 0.196f),   // deep slate-teal
        new Color(0.212f, 0.252f, 0.245f),   // grey-green metal (weathered variety note)
    };

    /// <summary>
    /// PROVISIONAL. Weathered-timber tints for the per-lot trim (bands, corner posts, window
    /// frames, balcony). One shared near-black trim on every house was part of why the town
    /// read as a repeated prop; a real port has silvered, tarred and freshly creosoted timber
    /// side by side. Authored against the same warm-light gain as the walls.
    /// </summary>
    private static readonly Color[] VillageTimberColours =
    {
        new Color(0.118f, 0.122f, 0.135f),   // dark tarred timber
        new Color(0.155f, 0.168f, 0.190f),   // weathered silvered cedar
        new Color(0.135f, 0.128f, 0.122f),   // warm creosote brown
    };

    /// <summary>
    /// Swaps one placed house's wall and roof onto a per-lot palette. Matches on the SHARED
    /// material the name router already assigned (Shiosai_HarbourFacade*/Shiosai_HarbourRoof*),
    /// so it is a pure post-step and the router stays the single source of truth for what a
    /// surface IS; this only decides what colour that surface is painted.
    /// </summary>
    private static void RecolourVillageHouse(GameObject go, int wallIdx, int roofIdx, int timberIdx)
    {
        if (go == null) return;
        var wall = ArchitectureMaterial($"Shiosai_VillageWall_{wallIdx}",
                                        VillageWallColours[wallIdx], null, smoothness: 0.06f);
        // ROOF SPECULAR IS THE REAL "WARM ROOF" CULPRIT. Darkening the roof ALBEDO alone changed
        // nothing on screen: at smoothness 0.30 / metallic 0.22 the whole sunlit slope was
        // covered by a broad specular sheen the colour of the (strongly warm) key light, which
        // is what sampled as pale pinkish-tan regardless of what colour the tile underneath was.
        // Kawara is a matt, unglazed clay tile - it has essentially no specular lobe at village
        // distance. PROVISIONAL.
        var roof = ArchitectureMaterial($"Shiosai_VillageRoof_{roofIdx}",
                                        VillageRoofColours[roofIdx],
                                        CoastTexture("Shiosai_HarbourRoof_Albedo_v2.png"),
                                        smoothness: 0.05f, metallic: 0f);
        // Cool, dark poured-concrete base. The shared router foundation is a neutral 0.42 grey,
        // which under the same warm key renders as a bright cream skirt - a second bright band
        // under already-bright walls, so the houses lost their footing and read as floating
        // blocks. PROVISIONAL.
        var foundation = ArchitectureMaterial("Shiosai_VillageFoundation",
                                              new Color(0.105f, 0.128f, 0.168f), null,
                                              smoothness: 0.04f);
        var timber = ArchitectureMaterial($"Shiosai_VillageTimber_{timberIdx}",
                                          VillageTimberColours[timberIdx], null,
                                          smoothness: 0.12f);
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string nm = mats[i].name.ToLowerInvariant();
                // ROOF FIRST - "Shiosai_HarbourHouse_A_Roof_Tile" would otherwise be caught by
                // the timber rule below on the word "tile"-adjacent trim names.
                if (nm.Contains("roof")) { mats[i] = roof; changed = true; }
                else if (nm.Contains("foundation")) { mats[i] = foundation; changed = true; }
                else if (nm.Contains("facade") || nm.Contains("plaster")
                         || nm.Contains("wall") || nm.Contains("render")) { mats[i] = wall; changed = true; }
                // TIMBER MATCH IS DELIBERATELY WIDE. The first attempt matched only "timber",
                // the name the Unity-side material router produces. The render showed every
                // house still wearing one flat warm-brown soffit, fascia and batten set, because
                // parts whose GLB material survives the router (or that route through the
                // "trim"/"beam"/"post" branch under a different asset name) never contain the
                // word "timber" at all - they are named "..._Trim_Wood". Matching the whole
                // vocabulary is safe here: this runs ONLY over one house's own children.
                else if (nm.Contains("timber") || nm.Contains("trim") || nm.Contains("wood")
                         || nm.Contains("beam") || nm.Contains("post") || nm.Contains("batten")
                         || nm.Contains("eave") || nm.Contains("rafter") || nm.Contains("balcon")
                         || nm.Contains("awning")) { mats[i] = timber; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    // --------------------------------------------------------------- chapter hero landmarks

    /// <summary>Scale of the small red-and-white light on the harbour mole. PROVISIONAL.</summary>
    private const float HarbourLightScale = 0.85f;
    /// <summary>Scale of THE lighthouse on its promontory (ch6 hero). PROVISIONAL.</summary>
    private const float LighthouseScale = 2.3f;
    /// <summary>Samples past the village anchor the sea torii stands. PROVISIONAL.</summary>
    private const int SeaToriiLeadSamples = 60;
    /// <summary>Below this Seaness a station is inland: no offshore rocks. PROVISIONAL.</summary>
    private const float StackSeanessMin = 0.65f;

    /// <summary>
    /// Ch1 hero: the timber gateway the rider rides THROUGH on the way up the Gateway climb
    /// (spec 5.1 "original timber/stone gateway straddles or frames the road").
    ///
    /// It straddles the carriageway, so it is built from the same torii kit scaled up until its
    /// 5.4 m clear opening comfortably clears the 7 m carriageway, plus a pair of stone marker
    /// posts on the verge so the arch reads as a deliberate boundary rather than a stray shrine.
    /// </summary>
    private static void BuildGatewayArch(Transform group, CoastRoute route)
    {
        int i = route.IndexAt(GatewayArchM);
        var p = route.Position[i];
        float yaw = Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg;

        // Straddles the road: origin ON the centreline, scaled so the 5.4 m-wide kit spans the
        // 7 m carriageway plus both verges with headroom. PROVISIONAL scale.
        const float GateScale = 2.6f;
        var at = new Vector3(p.x, p.y - VergeDropM, p.z);
        var go = Place("Shiosai_Torii", "Shiosai Mountain Gateway", group, at, yaw, GateScale);

        // Stone markers either side, clear of the braking line.
        for (int k = 0; k < 2; k++)
        {
            float off = k == 0 ? -9.5f : 9.5f;
            var post = GroundedPoint(route, Mathf.Clamp(i + 14, 0, route.Count - 1), off, 2f);
            Place("Shiosai_Breakwater", $"Gateway Marker {k}", group, post, yaw, 0.18f);
        }
        Debug.Log($"[shiosai] mountain gateway at d={route.Distance[i]:0} m, {at} " +
                  $"({(go != null ? "placed" : "MISSING ASSET")}).");
    }

    /// <summary>
    /// Ch6 hero: THE lighthouse, on the headland the route climbs to at SC_KM_275.
    ///
    /// It used to be the harbour light 4.7 km back down the course, so the Lighthouse Climb had
    /// no lighthouse in it at all. The tower stands just OUTSIDE the seaward guardrail on a
    /// keeper's terrace, so the rider crests the climb with a white tower against open water -
    /// the spec's brightest environmental feature - rather than passing a dead-end driveway.
    /// </summary>
    private static void BuildLighthousePromontory(Transform group, CoastRoute route)
    {
        int i = route.AnchorIndex(AnchorLighthouseSummit, AnchorLighthouseSummitM);
        float t = Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg;

        // Seat it on the clifftop shelf, just outside the seaward guardrail. The first attempt
        // used -26 m, which is OUT ON THE CLIFF FACE: GroundedPoint duly seated it on real
        // ground 40 m BELOW the carriageway, where the seaward verge hid it completely and the
        // Lighthouse Climb render had no lighthouse in it for the second time. The only shelf
        // this cross-section owns on the seaward side is the clifftop node itself (-11.0 m).
        var at = PointAt(route, i, LighthouseOffsetM);
        var lh = Place("Shiosai_Lighthouse", "Shiosai Light", group, at, t + 90f, LighthouseScale);

        // Keeper's terrace: a low stone platform so a 30 m tower does not sprout from grass.
        for (int k = 0; k < 6; k++)
        {
            float a = k / 6f * 360f;
            var ring = at + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), -1.6f,
                                        Mathf.Sin(a * Mathf.Deg2Rad)) * 7f;
            Place("Shiosai_Breakwater", $"Light Terrace {k}", group, ring, a, 0.5f);
        }
        Debug.Log($"[shiosai] lighthouse at {AnchorLighthouseSummit} d={route.Distance[i]:0} m, " +
                  $"{at} ({(lh != null ? "placed" : "MISSING ASSET")}).");
    }

    /// <summary>Metres seaward of the centreline the lighthouse terrace sits. The clifftop node
    /// is at -11.0 m and everything beyond it is cliff FACE, so this must stay inboard of it.
    /// PROVISIONAL.</summary>
    private const float LighthouseOffsetM = -10.2f;

    /// <summary>Rock thickness of the sea-arch ring over the crown / at the springings, metres.
    /// PROVISIONAL art tuning read off reference render 07.</summary>
    private const float ArchThickCrownM = 6.5f;
    private const float ArchThickSpringM = 15f;

    /// <summary>Authored span of Shiosai_RedBridge.glb, metres. MUST match build_shiosai.py's
    /// build_red_bridge(span=...): the guardrail suppression is cut on it.</summary>
    private const float RedBridgeSpanM = 170f;

    /// <summary>
    /// Ch5 hero: the coral-red through-arch bridge, seated on the centreline at
    /// SC_KM_215_BridgeMidpoint.
    ///
    /// The GLB is authored along local +Z with its origin at deck level on the centreline, so
    /// seating it is exactly "centreline position, yaw = atan2(T.x, T.z)". Nothing is baked in
    /// about deck height, which is what keeps the authored deck from fighting the swept
    /// carriageway the rider actually rides.
    /// </summary>
    private static void BuildRedBridge(Transform group, CoastRoute route)
    {
        int i = route.AnchorIndex(AnchorBridgeMidpoint, AnchorBridgeMidpointM);
        var p = route.Position[i];
        float yaw = Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg;
        var at = new Vector3(p.x, p.y + RoadSurfaceLiftM, p.z);
        var go = Place("Shiosai_RedBridge", "Shiosai Red Bridge", group, at, yaw, 1f);
        Debug.Log($"[shiosai] red bridge at {AnchorBridgeMidpoint} d={route.Distance[i]:0} m, " +
                  $"{at} ({(go != null ? "placed" : "MISSING ASSET - run build_shiosai.py redbridge")}).");
    }

    /// <summary>True where the authored bridge parapet replaces the swept guardrail. Without
    /// this the steel guardrail runs straight through the coral parapet for the whole span.
    /// Resolved from the ROUTE rather than from a field set during landmark staging, because
    /// BuildGuardrail runs before BuildLandmarks.</summary>
    private static bool OnRedBridge(CoastRoute route, float s) =>
        Mathf.Abs(s - route.Anchor(AnchorBridgeMidpoint, AnchorBridgeMidpointM))
            < RedBridgeSpanM * 0.5f + 3f;

    /// <summary>
    /// Ch3 hero (spec 5.3): the DARK CLIFF TUNNEL between SC_KM_155_TunnelEntry and
    /// SC_KM_160_TunnelExit, immediately before the harbour. The rider leaves the flower-framed
    /// descent, is swallowed by wet dark stone for ~150 m, and is then thrown out of a bright
    /// portal onto the port town - "the glimpse of harbour light" the spec asks for.
    ///
    /// Built in C#, not Blender, for the same reason as the sea arch and the road: the bore has
    /// to follow the centreline's heading, grade and bank at every station, and that is ribbon
    /// geometry measured off the route.
    ///
    /// CULLING (environment-skill invariant 3): the camera rides INSIDE this geometry. The bore
    /// skin and the portal reveals therefore use double-sided materials (_Cull = 0). This is the
    /// single most expensive mistake available here - a one-sided bore renders as nothing at all
    /// from the inside and the "tunnel" becomes an invisible hole in the hillside.
    ///
    /// All dimensions below are PROVISIONAL tunables.
    /// </summary>
    private static void BuildCliffTunnel(Transform group, CoastRoute route)
    {
        float sEntry = route.Anchor(AnchorTunnelEntry, AnchorTunnelEntryM);
        float sExit = route.Anchor(AnchorTunnelExit, AnchorTunnelExitM);
        int iA = Mathf.Clamp(route.IndexAt(sEntry), 1, route.Count - 2);
        int iB = Mathf.Clamp(route.IndexAt(sExit), 1, route.Count - 2);
        if (iB - iA < 4) { Debug.LogWarning("[shiosai] tunnel span too short - skipped."); return; }

        // ---- the bore cross-section: vertical walls under a semicircular crown --------------
        // Wide and tall enough for the third-person chase camera to ride through without the
        // near plane clipping into stone: 13 m clear span, 9.4 m clear height.
        var prof = new List<Vector2>();                       // (lateral, height above slab)
        for (int k = 0; k <= 2; k++)                          // right wall, bottom -> springline
            prof.Add(new Vector2(TunnelBoreHalfW, TunnelSpringH * k / 2f));
        for (int k = 1; k < TunnelCrownSegs; k++)             // crown, springline to springline
        {
            float a = Mathf.PI * k / TunnelCrownSegs;
            prof.Add(new Vector2(Mathf.Cos(a) * TunnelBoreHalfW,
                                 TunnelSpringH + Mathf.Sin(a) * TunnelBoreHalfW));
        }
        for (int k = 2; k >= 0; k--)                          // left wall, springline -> bottom
            prof.Add(new Vector2(-TunnelBoreHalfW, TunnelSpringH * k / 2f));
        int K = prof.Count;

        // Outward normal of the section at each profile point, so the rock shell is a true
        // offset ring (an arch), not a slab with a keyhole punched through it.
        var outward = new Vector2[K];
        for (int k = 0; k < K; k++)
        {
            var q = prof[k];
            outward[k] = q.y <= TunnelSpringH + 1e-3f
                ? new Vector2(Mathf.Sign(q.x), 0f)
                : new Vector2(q.x, q.y - TunnelSpringH).normalized;
        }

        var rng = new System.Random(ScatterSeed + 733);
        int stations = iB - iA + 1;
        var inner = new Vector3[stations][];
        var outer = new Vector3[stations][];

        for (int n = 0; n < stations; n++)
        {
            int idx = iA + n;
            var p = route.Position[idx];
            var side = route.SideFlat(idx);
            // Rock cover swells toward the middle of the bore so the massif reads as a headland
            // the road burrows through, and thins to a clean arch ring at the two portals.
            float t = stations <= 1 ? 0f : n / (float)(stations - 1);
            float swell = Mathf.Sin(Mathf.PI * t);
            float cover = TunnelPortalCoverM + (TunnelMidCoverM - TunnelPortalCoverM) * swell;

            inner[n] = new Vector3[K];
            outer[n] = new Vector3[K];
            for (int k = 0; k < K; k++)
            {
                var q = prof[k];
                float baseY = p.y - VergeDropM;
                inner[n][k] = new Vector3(p.x + side.x * q.x, baseY + q.y, p.z + side.z * q.x);

                float th = cover + 1.8f * Mathf.Sin(k * 1.31f + n * 0.21f)
                           + (float)rng.NextDouble() * 1.2f;
                float gx = q.x + outward[k].x * th;
                float gy = q.y + outward[k].y * th;
                outer[n][k] = new Vector3(p.x + side.x * gx, baseY + gy, p.z + side.z * gx);
            }
        }

        // ---- mesh assembly -----------------------------------------------------------------
        List<Vector3> bV = new(); List<Vector2> bUv = new(); List<int> bT = new();
        List<Vector3> mV = new(); List<Vector2> mUv = new(); List<int> mT = new();
        List<Vector3> pV = new(); List<Vector2> pUv = new(); List<int> pT = new();

        // NOTE: every quad is emitted ONCE with its own four verts and a single winding, then
        // RecalculateNormals is used. Double-winding a quad and recalculating produces cancelled
        // (zero-length) normals and the black zebra striping this project has already paid for.
        static void Quad(List<Vector3> V, List<Vector2> U, List<int> T,
                         Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uw, float vh)
        {
            int o = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            U.Add(new Vector2(0f, 0f)); U.Add(new Vector2(uw, 0f));
            U.Add(new Vector2(uw, vh)); U.Add(new Vector2(0f, vh));
            T.Add(o); T.Add(o + 2); T.Add(o + 1);
            T.Add(o); T.Add(o + 3); T.Add(o + 2);
        }

        for (int n = 0; n < stations - 1; n++)
        {
            float uv = route.Distance[iA + n] * 0.08f;
            for (int k = 0; k < K - 1; k++)
            {
                // bore skin (what the rider is inside) and the exterior rock shell
                Quad(bV, bUv, bT, inner[n][k], inner[n][k + 1], inner[n + 1][k + 1], inner[n + 1][k], 0.9f, 0.24f);
                Quad(mV, mUv, mT, outer[n][k + 1], outer[n][k], outer[n + 1][k], outer[n + 1][k + 1], 1.1f, 0.24f);
            }
            // Skirt: carry both outer springings down well below the verge so the massif is
            // buried in the hillside rather than floating as a tube over a hole.
            float footY = Mathf.Min(outer[n][0].y, outer[n + 1][0].y) - TunnelSkirtDropM;
            for (int side2 = 0; side2 < 2; side2++)
            {
                int k = side2 == 0 ? 0 : K - 1;
                var a = outer[n][k]; var b = outer[n + 1][k];
                var ad = new Vector3(a.x, footY, a.z); var bd = new Vector3(b.x, footY, b.z);
                if (side2 == 0) Quad(mV, mUv, mT, ad, a, b, bd, 1.1f, 0.6f);
                else Quad(mV, mUv, mT, a, ad, bd, b, 1.1f, 0.6f);
            }
        }

        // ---- the two portal faces: the ring of wet-darkened stone around each mouth ---------
        for (int end = 0; end < 2; end++)
        {
            int n = end == 0 ? 0 : stations - 1;
            for (int k = 0; k < K - 1; k++)
            {
                if (end == 0)
                    Quad(pV, pUv, pT, inner[n][k + 1], inner[n][k], outer[n][k], outer[n][k + 1], 1f, 1f);
                else
                    Quad(pV, pUv, pT, inner[n][k], inner[n][k + 1], outer[n][k + 1], outer[n][k], 1f, 1f);
            }
        }

        static Mesh Bake(string name, List<Vector3> V, List<Vector2> U, List<int> T)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(V); m.SetUVs(0, U); m.SetTriangles(T, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        AddMesh(group, "Shiosai Tunnel Bore", Bake("Shiosai_TunnelBore", bV, bUv, bT),
                TunnelInteriorMaterial(), collider: false);
        AddMesh(group, "Shiosai Tunnel Massif", Bake("Shiosai_TunnelMassif", mV, mUv, mT),
                CliffRockMaterial(), collider: false);
        AddMesh(group, "Shiosai Tunnel Portals", Bake("Shiosai_TunnelPortals", pV, pUv, pT),
                TunnelPortalMaterial(), collider: false);

        // ---- interior lighting: emissive sodium lamp strips along the crown -----------------
        //
        // "TUNNEL LAMPS INVISIBLE" FIX. Lamps were already being generated here and the log
        // happily reported "8 lamp pairs" - another case of a green metric on something the
        // camera cannot see. Two independent reasons they never reached a pixel:
        //
        //  1. GEOMETRY. Each lamp was a flat quad spanned by (forward, up) sitting 0.12 m off
        //     the springline wall - i.e. a panel whose normal points STRAIGHT AT THE WALL,
        //     perpendicular to the bore axis. The diag camera looks down that axis, so every
        //     lamp was seen exactly edge-on and covered well under a pixel. Lamps now hang
        //     from the CROWN facing DOWN the bore and DOWN at the road, which is both what a
        //     real road tunnel looks like and what a forward-facing camera can actually see.
        //
        //  2. MATERIAL. CelMaterial is still a LIT shader. Inside a sealed bore there is no
        //     sun and almost no ambient, so a "sodium yellow" cel lamp shades to near black
        //     along with everything else. Lamps must be self-lit to read at all.
        //
        // These stay EMISSIVE GEOMETRY rather than real Light components on purpose: the ask
        // is a tunnel that reads as a real road tunnel WITHOUT washing out the dark mood, and
        // six HDRP point lights would both blow out the bore and put realtime light cost into
        // a 34k-renderer scene for one 150 m stretch.
        var lampMat = TunnelLampMaterial();
        List<Vector3> lV = new(); List<Vector2> lU = new(); List<int> lT = new();
        int lampCount = 0;
        for (float d = sEntry + TunnelLampSpacingM * 0.5f; d < sExit; d += TunnelLampSpacingM)
        {
            int idx = Mathf.Clamp(route.IndexAt(d), 0, route.Count - 1);
            var p = route.Position[idx];
            var side = route.SideFlat(idx);
            var fwd = route.Tangent[idx];
            var road = new Vector3(p.x, p.y - VergeDropM, p.z);

            // Twin strips either side of the crown centreline, the usual Japanese bore layout.
            for (int s2 = 0; s2 < 2; s2++)
            {
                float ox = (s2 == 0 ? 1f : -1f) * TunnelLampOffsetM;
                var c = road + side * ox + Vector3.up * (TunnelSpringH + TunnelBoreHalfW - TunnelLampDropM);
                var hf = fwd * (TunnelLampLengthM * 0.5f);
                var hs = side * (TunnelLampWidthM * 0.5f);

                // Downward-facing luminous face. Wound so its normal points at the road.
                Quad(lV, lU, lT, c - hf - hs, c - hf + hs, c + hf + hs, c + hf - hs, 1f, 1f);
                // Rear-facing luminous face, so the strip still glows as the rider passes under
                // it and the receding row reads as a row rather than as one bar.
                var up = Vector3.up * TunnelLampWidthM * 0.5f;
                Quad(lV, lU, lT, c - hf - hs, c - hf + hs, c - hf + hs + up, c - hf - hs + up, 1f, 1f);
                lampCount++;
            }
        }
        if (lV.Count > 0)
            AddMesh(group, "Shiosai Tunnel Lamps", Bake("Shiosai_TunnelLamps", lV, lU, lT),
                    lampMat, collider: false);

        Debug.Log($"[shiosai] cliff tunnel {sEntry:0}-{sExit:0} m ({sExit - sEntry:0} m bore), " +
                  $"{stations} stations, bore {bV.Count} verts _Cull=0, massif {mV.Count} verts, " +
                  $"portals {pV.Count} verts, {lampCount} crown lamps.");
    }

    /// <summary>True inside the cliff tunnel bore - nothing may be scattered or railed there.</summary>
    private static bool InTunnel(CoastRoute route, float s) =>
        s > route.Anchor(AnchorTunnelEntry, AnchorTunnelEntryM) - 6f &&
        s < route.Anchor(AnchorTunnelExit, AnchorTunnelExitM) + 6f;

    /// <summary>
    /// Ch7 hero: the sea-arch gate the road rides THROUGH (reference render 07), plus the
    /// offshore arched stacks behind it.
    ///
    /// Built here in C# rather than Blender for one reason: the arch has to straddle the
    /// carriageway at whatever width, height and heading the centreline has at that station, and
    /// that is ribbon geometry measured off the route - the same argument that keeps the road
    /// and guardrail in this file.
    ///
    /// CULLING: the camera rides THROUGH the opening, so the arch material must be double-sided
    /// (_Cull = 0) or the inside face of the span is invisible from under it.
    /// </summary>
    private static void BuildSeaArchGate(Transform group, CoastRoute route)
    {
        int i = route.AnchorIndex(AnchorSeaArch, AnchorSeaArchM);

        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        var rng = new System.Random(ScatterSeed + 311);

        // The arch is a thick rock ring swept across the road: an inner opening the carriageway
        // clears, an outer silhouette that merges into the hillside on the inland side.
        const int Rings = 22;          // segments around the opening
        const float OpenHalfW = 9.0f;  // clear half width  (carriageway 3.5 + shoulder + margin)
        const float OpenHeight = 10.5f; // clear height above the road
        const float Thick = 11.0f;     // rock thickness, road-wise (the "tunnel" depth)

        int i0 = Mathf.Clamp(i - Mathf.RoundToInt(Thick * 0.5f / 3f), 1, route.Count - 2);
        int i1 = Mathf.Clamp(i + Mathf.RoundToInt(Thick * 0.5f / 3f), 1, route.Count - 2);

        // Two sweep stations (entry face and exit face), each a ring of points around the
        // opening, offset outwards by a jittered rock mass.
        Vector3[] RingAt(int idx, float bulge)
        {
            var p = route.Position[idx];
            var s = route.SideFlat(idx);
            var ring = new Vector3[Rings * 2];
            for (int k = 0; k < Rings; k++)
            {
                float a = Mathf.PI * k / (Rings - 1);          // 0..pi, springing to springing
                float ox = -Mathf.Cos(a) * OpenHalfW;
                float oy = Mathf.Sin(a) * OpenHeight;
                // inner edge of the rock ring
                ring[k] = new Vector3(p.x + s.x * ox, p.y - VergeDropM + oy, p.z + s.z * ox);
                // outer edge: an OUTER ELLIPSE concentric with the opening, thickest at the
                // springings and thinnest over the crown - i.e. an arch.
                //
                // The first attempt grew the outer edge by up to 36 m along +/-X and lifted it by
                // half that again, which is not an arch at all: it built a 60 m cliff slab with a
                // keyhole in it, hanging over the headland. An arch is a RING, so the outer
                // profile has to follow the same ellipse, offset by a (varying) thickness.
                float thick = ArchThickCrownM
                              + (ArchThickSpringM - ArchThickCrownM) * (1f - Mathf.Sin(a))
                              + 1.6f * Mathf.Sin(a * 5.3f + 0.7f)
                              + (float)rng.NextDouble() * 1.4f + bulge;
                float gx = -Mathf.Cos(a) * (OpenHalfW + thick);
                float gy = Mathf.Sin(a) * (OpenHeight + thick * 0.85f);
                ring[Rings + k] = new Vector3(p.x + s.x * gx, p.y - VergeDropM + gy,
                                              p.z + s.z * gx);
            }
            return ring;
        }

        var A = RingAt(i0, 0f);
        var B = RingAt(i1, 2.2f);

        void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            int b0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f)); uvs.Add(new Vector2(0f, 1f));
            tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 1);
            tris.Add(b0); tris.Add(b0 + 3); tris.Add(b0 + 2);
        }

        for (int k = 0; k < Rings - 1; k++)
        {
            // soffit (the underside the rider sees), outer skin, and the two faces
            Q(A[k], A[k + 1], B[k + 1], B[k], 1.2f);                                  // soffit
            Q(A[Rings + k + 1], A[Rings + k], B[Rings + k], B[Rings + k + 1], 1.2f);  // outer skin
            Q(A[k + 1], A[k], A[Rings + k], A[Rings + k + 1], 1.2f);                  // entry face
            Q(B[k], B[k + 1], B[Rings + k + 1], B[Rings + k], 1.2f);                  // exit face
        }

        // Legs: carry both springings down to the terrain so the arch is not a floating ring.
        for (int side = 0; side < 2; side++)
        {
            int k = side == 0 ? 0 : Rings - 1;
            float footY = Mathf.Min(A[k].y, B[k].y) - 42f;   // well below the verge, buried
            var a0 = A[k]; var a1 = A[Rings + k];
            var b0v = B[k]; var b1 = B[Rings + k];
            var a0d = new Vector3(a0.x, footY, a0.z); var a1d = new Vector3(a1.x, footY, a1.z);
            var b0d = new Vector3(b0v.x, footY, b0v.z); var b1d = new Vector3(b1.x, footY, b1.z);
            Q(a0, a0d, b0d, b0v, 2f);          // inner leg face (the rider passes it)
            Q(a1d, a1, b1, b1d, 2f);           // outer leg face
            Q(a0d, a0, a1, a1d, 2f);           // entry-side leg
            Q(b0v, b0d, b1d, b1, 2f);          // exit-side leg
        }

        var mesh = new Mesh { name = "Shiosai_SeaArchGate",
                              indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        var rock = SeaArchRockMaterial();

        AddMesh(group, "Shiosai Sea Arch", mesh, rock, collider: false);

        // A second, purely offshore arch so the chapter reads as an arch COAST, not one prop.
        {
            int j = Mathf.Clamp(i + 90, 0, route.Count - 1);
            var p = route.Position[j];
            var s = route.SideFlat(j);
            for (int k = 0; k < 4; k++)
            {
                float off = -95f - k * 34f - (float)rng.NextDouble() * 22f;
                var at = new Vector3(p.x + s.x * off, SeaLevelY - 0.6f, p.z + s.z * off);
                if (Vector2.Distance(new Vector2(at.x, at.z), HarbourCentre) < 330f) continue;
                Place(k % 2 == 0 ? "Shiosai_SeaStack_C" : "Shiosai_SeaStack_A",
                      $"Sea Arch Stack {k}", group, at, (float)rng.NextDouble() * 360f,
                      2.4f + (float)rng.NextDouble() * 1.4f);
            }
        }

        Debug.Log($"[shiosai] sea arch at {AnchorSeaArch} d={route.Distance[i]:0} m, " +
                  $"{verts.Count} verts, _Cull=0.");
    }

    /// <summary>
    /// True when a lateral offset at station <paramref name="i"/> is still INSIDE the terrain
    /// ribbon that CrossSection actually builds there.
    ///
    /// Scatter used to be placed at its nominal offset (up to +94 m inland) and then dropped
    /// onto whatever GroundedPoint happened to hit. On a switchback the curvature/proximity
    /// clamp can pull that station's ribbon in to ~20 m, so the tree either hit a neighbouring
    /// leg's ribbon far above or below its own road, or hit nothing at all and was left at its
    /// fallback height - which is exactly the band of trees apparently floating in mid-air over
    /// the sea on the left of the ch1 Gateway render. Asking the same clamp the geometry used
    /// keeps the dressing on the ground it was built for; a clamped station simply plants fewer
    /// trees, which is also the correct read (there is no room for a forest on a hairpin).
    /// </summary>
    private static bool OnRibbon(CoastRoute r, int i, float offset)
    {
        CurvatureLimits(r, i, out float posLimit, out float negLimit);
        float limit = Mathf.Min(offset >= 0f ? posLimit : negLimit, ProximityLimit(r, i));
        if (limit == float.MaxValue) return true;
        // A small inset, because the outermost metre of the ribbon is the EdgeSkirt face.
        return Mathf.Abs(offset) <= limit - ScatterEdgeInsetM;
    }

    /// <summary>Margin kept between the last scattered prop and the clamped ribbon edge, so
    /// nothing is planted on the vertical edge skirt. PROVISIONAL.</summary>
    private const float ScatterEdgeInsetM = 4f;

    // ------------------------------------------------------------------- M5 flora tunables
    // ALL PROVISIONAL (illustrative tuning, not confirmed direction) - metres along the route
    // between successive clumps, and the jitter added on top.
    private const float HydrangeaStepM = 3.4f;
    private const float HydrangeaStepJitterM = 4.6f;

    // ------------------------------------------------------- opening dressing density
    // The first stretch of road is the OPENING - it is what the three start-line diagnostics
    // frame and the only part of the route a new rider judges the game on. Against
    // ShiosaiCoast_02.png the stock scatter read sparse there: isolated pines up an empty slope
    // and a thin dotted line of hydrangeas, where the plate has a massed verge planting and a
    // properly wooded hillside. These multipliers densify ONLY that band, so the tri budget for
    // the other 42 km is untouched. ALL PROVISIONAL.
    /// <summary>Metres of route treated as "the opening" for dressing density.</summary>
    private const float OpeningLengthM = 210f;
    /// <summary>Metres over which the opening boost blends back to the normal density.</summary>
    private const float OpeningFalloffM = 70f;
    /// <summary>Stride multiplier inside the opening - smaller means more clumps per metre.</summary>
    private const float OpeningStrideScale = 0.52f;
    /// <summary>Extra trees added to each opening clump.</summary>
    private const int OpeningExtraTrees = 3;
    /// <summary>Extra hydrangeas added to each opening clump.</summary>
    private const int OpeningExtraHydrangea = 4;

    /// <summary>The three authored アジサイ variants (see build_shiosai.py BUILDERS). Picking
    /// per bush is what breaks the "identical plastic balls" silhouette.</summary>
    private static readonly string[] HydrangeaAssets =
    { "Shiosai_Hydrangea", "Shiosai_Hydrangea_B", "Shiosai_Hydrangea_C" };

    /// <summary>
    /// 1 inside the opening, falling smoothly to 0 by OpeningLengthM + OpeningFalloffM. A hard
    /// cut would put a visible planting seam across the road at exactly the distance the chase
    /// camera is looking at.
    /// </summary>
    private static float OpeningWeight(float d)
    {
        if (d <= OpeningLengthM) return 1f;
        return Mathf.Clamp01(1f - (d - OpeningLengthM) / OpeningFalloffM);
    }
    private const float SakuraStepM = 52f;
    private const float SakuraStepJitterM = 46f;
    private const float FlowerStepM = 5.0f;
    private const float FlowerStepJitterM = 6.0f;
    /// <summary>Base tilt of a verge sakura toward the carriageway, degrees. PROVISIONAL.</summary>
    private const float SakuraLeanDeg = 13f;

    /// <summary>
    /// アジサイ bloom colours. A hydrangea bank in reference 03/10 is not one blue - it runs
    /// from cobalt through violet to a washed pink, which is what stops a dense planting from
    /// reading as one repeated prop.
    ///
    /// VISUAL FIX (badcliff.png / messedup.png): the previous set was near-fully-saturated
    /// primary blue/violet, which under the cel ramp came out as glowing plastic. Real アジサイ
    /// mopheads are PALE and slightly grey - the saturation lives in the shadow ramp, not the
    /// albedo - so every entry is desaturated and lifted, and the set widened to eight so a
    /// 720-bush bank carries real tonal variation rather than five repeated fills. PROVISIONAL
    /// palette.
    /// </summary>
    private static readonly Color[] HydrangeaBloomColours =
    {
        // Iteration 3: every entry lifted ~+0.12 because the floret albedo MULTIPLIES the tint,
        // so the staged bank read far darker (uniform navy at distance) than the authored values.
        new Color(0.64f, 0.70f, 0.88f),   // washed cobalt
        new Color(0.70f, 0.70f, 0.86f),   // periwinkle
        new Color(0.74f, 0.68f, 0.84f),   // soft violet
        new Color(0.80f, 0.72f, 0.82f),   // mauve
        new Color(0.86f, 0.78f, 0.84f),   // dusty rose (kept rare - references are mostly blue)
        new Color(0.72f, 0.80f, 0.92f),   // pale sky blue
        new Color(0.56f, 0.62f, 0.78f),   // deeper, shaded blue for tonal contrast
        new Color(0.86f, 0.88f, 0.92f),   // nearly-white lacecap
    };

    /// <summary>Leaf tints applied per bush so a bank is not one flat green. PROVISIONAL.</summary>
    private static readonly Color[] HydrangeaLeafColours =
    {
        new Color(0.74f, 0.86f, 0.70f),
        new Color(0.80f, 0.90f, 0.74f),
        new Color(0.66f, 0.80f, 0.64f),
    };

    /// <summary>
    /// Swaps one placed hydrangea's bloom material onto a palette variant. The GLB ships a
    /// single shared bloom material, so - exactly like the village houses - variety has to be
    /// applied per instance AFTER staging, and each variant needs its own material NAME or the
    /// material cache hands back the first one and the whole bank comes out one colour.
    ///
    /// THE TEXTURE IS THE FIX. The palette variant used to be built with no texture at all, so
    /// it silently threw away the authored floret albedo that CoastMaterialFor assigns - every
    /// bush was a flat untextured fill, i.e. the "gummy Roblox sphere" read. The variant now
    /// carries the same floret map, with the plastic sheen (gloss/rim) pulled down.
    /// </summary>
    private static void RecolourHydrangea(GameObject go, int idx)
    {
        if (go == null) return;
        var bloom = CelMaterial($"Shiosai_HydrangeaBloom_{idx}", HydrangeaBloomColours[idx],
                                gloss: 0.05f, spec: 0.05f, rim: 0.16f,
                                texture: CoastTexture("Shiosai_HokkaidoBlueFlowers_Albedo.png"));
        var leaf = CelMaterial($"Shiosai_HydrangeaLeaf_{idx % HydrangeaLeafColours.Length}",
                               HydrangeaLeafColours[idx % HydrangeaLeafColours.Length],
                               gloss: 0.04f, spec: 0.05f, rim: 0.22f,
                               texture: CoastTexture("Shiosai_Leaf_Albedo_HQ.png"));
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string nm = mats[i].name.ToLowerInvariant();
                if (nm.Contains("hydrangealeaf")) continue;   // never re-tint an already-tinted leaf
                if (nm.Contains("hydrangea") || nm.Contains("bloom") || nm.Contains("floret"))
                { mats[i] = bloom; changed = true; }
                else if (nm.Contains("leafcanopy") || nm.Contains("leaf_cluster"))
                { mats[i] = leaf; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    private static void ScatterCoast(Transform root, CoastRoute route)
    {        var group = new GameObject("Coast Dressing").transform;
        group.SetParent(root, false);

        var rng = new System.Random(ScatterSeed);
        int pines = 0, broadleaf = 0, hydrangeas = 0, rocks = 0, sakura = 0, flowers = 0;

        // THE VILLAGE IS A CLEARING. Roadside pines at 16-94 m inland are exactly where the
        // harbour town stands, so the first village render was a forest with a couple of roofs
        // peeping through it. Trees are suppressed inside the town footprint (hydrangeas and
        // clifftop pines are kept - they dress the verge without hiding a facade).
        float villageS = route.Anchor(AnchorVillageCentre, AnchorVillageCentreM);
        const float VillageClearHalfM = 190f;   // PROVISIONAL: matches the widened frontage

        var rockVerts = new List<Vector3>(); var rockUv = new List<Vector2>(); var rockTri = new List<int>();
        float nextPine = 15f, nextHydrangea = 22f, nextRock = 40f;
        float nextSakura = 30f, nextFlower = 12f;

        // PROVISIONAL mix, read off renders 01/15: the coast is far more BROADLEAF than pine.
        // A hillside of nothing but conifers was a large part of why the gameplay frame read as
        // a placeholder treeline; pines now hold the clifftops and the broadleaves the slopes.
        var pineAssets = new[] { "Shiosai_Pine", "Shiosai_Pine_B" };
        var leafAssets = new[] { "Shiosai_Broadleaf", "Shiosai_Broadleaf_B" };
        const float BroadleafShare = 0.58f;

        for (int i = 0; i < route.Count; i++)
        {
            float d = route.Distance[i];
            // Nothing grows inside a tunnel. Without this the bore fills with pines and
            // hydrangeas seated on the verge that the massif now sits on top of.
            if (InTunnel(route, d)) continue;
            float open = OpeningWeight(d);
            float openStride = Mathf.Lerp(1f, OpeningStrideScale, open);

            // --- 黒松 coastal pines + broadleaf: headland behind the road and the cliff edge
            if (d >= nextPine)
            {
                nextPine = d + (6f + (float)rng.NextDouble() * 10f) * openStride;
                int clump = 2 + rng.Next(4) + Mathf.RoundToInt(OpeningExtraTrees * open);
                for (int k = 0; k < clump; k++)
                {
                    bool clifftop = rng.NextDouble() < 0.30;
                    if (!clifftop && Mathf.Abs(d - villageS) < VillageClearHalfM) continue;
                    float o = clifftop ? -10.5f - (float)rng.NextDouble() * 4f
                                       : 16f + (float)rng.NextDouble() * 78f;
                    float along = ((float)rng.NextDouble() - 0.5f) * 14f;
                    int idx = Mathf.Clamp(i + Mathf.RoundToInt(along / 3f), 0, route.Count - 1);
                    if (!OnRibbon(route, idx, o)) continue;
                    var at = GroundedPoint(route, idx, o, TreeGroundMarginM);
                    if (at.y < 1.0f) continue;
                    // Clifftops are wind-blasted: pines only. Inland slopes get the mix.
                    bool leaf = !clifftop && rng.NextDouble() < BroadleafShare;
                    var set = leaf ? leafAssets : pineAssets;
                    string asset = set[rng.Next(set.Length)];
                    float pYaw = (float)rng.NextDouble() * 360f;
                    float pScale = 0.70f + (float)rng.NextDouble() * 0.70f;
                    if (InTunaPortFootprint(route, idx, o)) continue;   // rng already consumed: layout elsewhere unchanged
                    Place(asset, leaf ? $"Broadleaf {broadleaf:0000}" : $"Pine {pines:0000}",
                          group, at, pYaw, pScale);
                    if (leaf) broadleaf++; else pines++;
                }
            }

            // --- アジサイ hydrangeas: a dense BANK lining the road ---------------------------
            // M5: the previous pass placed clumps of 2-5 every 7-18 m at 9.8-16.8 m inland.
            // From the saddle that read as isolated blue dots scattered up a bare green
            // hillside - references 03/10/14 show a CONTINUOUS bank hard against the guardrail.
            // Three changes make the bank: place them right at the verge, halve the spacing,
            // and give each clump one of four bloom colours so it reads as a hedgerow rather
            // than as one repeated prop. All PROVISIONAL tuning.
            if (d >= nextHydrangea)
            {
                nextHydrangea = d + (HydrangeaStepM + (float)rng.NextDouble() * HydrangeaStepJitterM) * openStride;
                int clump = 4 + rng.Next(5) + Mathf.RoundToInt(OpeningExtraHydrangea * open);
                for (int k = 0; k < clump; k++)
                {
                    bool inland = rng.NextDouble() < 0.62;
                    float o = inland ? 7.2f + (float)rng.NextDouble() * 5.4f
                                     : -8.0f - (float)rng.NextDouble() * 2.6f;
                    float along = ((float)rng.NextDouble() - 0.5f) * 9f;
                    int idx = Mathf.Clamp(i + Mathf.RoundToInt(along / 3f), 0, route.Count - 1);
                    var at = GroundedPoint(route, idx, o, 1.5f);
                    if (at.y < 1.0f) continue;
                    // Three authored variants, chosen per bush: identical silhouettes repeated
                    // 720 times is what made the bank read as a row of plastic balls even
                    // before the material was looked at.
                    string hAsset = HydrangeaAssets[rng.Next(HydrangeaAssets.Length)];
                    float hYaw = (float)rng.NextDouble() * 360f;
                    float hScale = 0.80f + (float)rng.NextDouble() * 0.95f;
                    int hBloom = rng.Next(HydrangeaBloomColours.Length);
                    if (InTunaPortFootprint(route, idx, o)) continue;   // rng already consumed
                    var bush = Place(hAsset, $"Hydrangea {hydrangeas:0000}", group, at, hYaw, hScale);
                    RecolourHydrangea(bush, hBloom);
                    hydrangeas++;
                }
            }

            // --- 桜 sakura leaning OUT over the carriageway (reference 03) -------------------
            // M5: borrowed from Sakura Pass (see PlaceSakuraFlora). The lean is the whole
            // point: reference 03's hero read is pink blossom hanging INTO frame above the
            // road, which a plumb tree set back on the verge never produces.
            if (d >= nextSakura)
            {
                nextSakura = d + SakuraStepM + (float)rng.NextDouble() * SakuraStepJitterM;
                if (Mathf.Abs(d - villageS) > VillageClearHalfM)
                {
                    bool inland = rng.NextDouble() < 0.75;
                    float o = inland ? 8.0f + (float)rng.NextDouble() * 3.5f
                                     : -9.0f - (float)rng.NextDouble() * 2.0f;
                    if (OnRibbon(route, i, o))
                    {
                        var at = GroundedPoint(route, i, o, 1.2f);
                        if (at.y >= 1.0f)
                        {
                            // lean back toward the carriageway, i.e. opposite the verge side
                            var side = route.SideFlat(i);
                            float towardRoadDeg = Mathf.Atan2(inland ? -side.x : side.x,
                                                              inland ? -side.z : side.z) * Mathf.Rad2Deg;
                            string[] sakuraAssets = { "SakuraPass_Sakura_Tree_A", "SakuraPass_Sakura_Tree_B",
                                                      "SakuraPass_Sakura_Tree_C" };
                            string sAsset = sakuraAssets[rng.Next(sakuraAssets.Length)];
                            float sYaw = (float)rng.NextDouble() * 360f;
                            float sScale = 0.85f + (float)rng.NextDouble() * 0.55f;
                            int sTint = rng.Next(4);
                            float sLean = SakuraLeanDeg + (float)rng.NextDouble() * 8f;
                            if (!InTunaPortFootprint(route, i, o))
                            {
                                PlaceSakuraFlora(sAsset, $"Coast Sakura {sakura:000}", group, at,
                                                 sYaw, sScale, sTint, sLean, towardRoadDeg);
                                sakura++;
                            }
                        }
                    }
                }
            }

            // --- roadside wildflowers: the white/yellow verge fringe in every reference ------
            if (d >= nextFlower)
            {
                nextFlower = d + FlowerStepM + (float)rng.NextDouble() * FlowerStepJitterM;
                string[] flowerAssets = { "SakuraPass_Flower_Clump_A", "SakuraPass_Flower_Clump_B",
                                          "SakuraPass_Flower_Clump_C" };
                int clump = 3 + rng.Next(4);
                for (int k = 0; k < clump; k++)
                {
                    float o = rng.NextDouble() < 0.5 ? 6.4f + (float)rng.NextDouble() * 3.4f
                                                     : -7.2f - (float)rng.NextDouble() * 2.4f;
                    float along = ((float)rng.NextDouble() - 0.5f) * 8f;
                    int idx = Mathf.Clamp(i + Mathf.RoundToInt(along / 3f), 0, route.Count - 1);
                    var at = GroundedPoint(route, idx, o, 0.8f);
                    if (at.y < 1.0f) continue;
                    string fAsset = flowerAssets[rng.Next(flowerAssets.Length)];
                    float fYaw = (float)rng.NextDouble() * 360f;
                    float fScale = 0.9f + (float)rng.NextDouble() * 0.8f;
                    int fTint = rng.Next(3);
                    if (InTunaPortFootprint(route, idx, o)) continue;   // rng already consumed
                    PlaceSakuraFlora(fAsset, $"Verge Flowers {flowers:0000}", group, at, fYaw, fScale, fTint);
                    flowers++;
                }
            }

            // --- shore boulders: still generated, because they are scatter noise rather than
            //     a modelled prop and 300 GLB instances would buy nothing --------------------
            if (d >= nextRock)
            {
                nextRock = d + 16f + (float)rng.NextDouble() * 24f;
                bool shore = rng.NextDouble() < 0.6;
                float o = shore ? -13f - (float)rng.NextDouble() * 26f
                                : 14f + (float)rng.NextDouble() * 40f;
                if (!OnRibbon(route, i, o)) continue;
                var at = GroundedPoint(route, i, o, 2f);
                float s = 0.8f + (float)rng.NextDouble() * 2.2f;
                if (InTunaPortFootprint(route, i, o))
                    Rock(new List<Vector3>(), new List<Vector2>(), new List<int>(), at, s, rng);   // keep the rng in step
                else
                    Rock(rockVerts, rockUv, rockTri, at + Vector3.up * s * 0.3f, s, rng);
                rocks++;
            }
        }

        int outcrops = InlandOutcrops(route, villageS, VillageClearHalfM, rockVerts, rockUv, rockTri,
                                      out int outcropRocks);
        rocks += outcropRocks;
        Debug.Log($"[shiosai] inland outcrops: {outcrops} clusters, {outcropRocks} rocks.");

        AddMesh(group, "Coast Rocks",
                Finish("Shiosai_Rocks", rockVerts.ToArray(), rockUv.ToArray(), rockTri),
                RockPropMaterial(),
                collider: false);

        Debug.Log($"[shiosai] dressing: {pines} pines, {broadleaf} broadleaf, " +
                  $"{hydrangeas} hydrangeas, {sakura} sakura, {flowers} flower clumps, " +
                  $"{rocks} boulders.");
        foreach (var kv in FloraMatSeen)
            Debug.Log($"[shiosai] flora-mat {kv.Value,6} x  {kv.Key}");
        FloraMatSeen.Clear();
    }

    // Inland outcrop tunables. ALL PROVISIONAL, judged from the chapter captures.
    private const float OutcropStrideMinM = 70f, OutcropStrideMaxM = 140f;
    private const float OutcropInlandMinM = 18f, OutcropInlandMaxM = 62f;
    private const float OutcropRoadClearM = 10f;

    /// <summary>
    /// GRAPHICS OVERHAUL (claude-cowork, 2026-09-25). Half-buried rock OUTCROPS on the inland
    /// slope: one large weathered boulder with 2-5 smaller stones around it.
    ///
    /// WHY. In every chapter capture the headland above the road was one uninterrupted sheet of
    /// bright lawn with hydrangea bushes along the verge. A real Japanese coastal headland
    /// breaks through its grass: grey andesite shows in clusters on the slope. The existing
    /// boulder pass is one stone every 16-40 m, 60 % of them on the shore, so the slope stayed
    /// bare. Clusters (not more single stones) are what the eye reads as geology.
    ///
    /// Uses the existing Rock() primitive, material and merged "Coast Rocks" mesh (no new draw
    /// call) and its OWN rng, so the shared scatter sequence - every pine, hydrangea and flower
    /// - lands exactly where it did before. Keeps out of the tunnel, the red-bridge span, the
    /// village clearing and anywhere another leg of the route passes close by.
    /// </summary>
    private static int InlandOutcrops(CoastRoute route, float villageS, float villageHalfM,
                                      List<Vector3> v, List<Vector2> uv, List<int> tri, out int stones)
    {
        var rng = new System.Random(ScatterSeed + 977);
        int clusters = 0;
        stones = 0;
        float next = 60f;
        for (int i = 0; i < route.Count; i++)
        {
            float d = route.Distance[i];
            if (d < next) continue;
            next = d + Mathf.Lerp(OutcropStrideMinM, OutcropStrideMaxM, (float)rng.NextDouble());
            if (InTunnel(route, d) || OnRedBridge(route, d)) continue;
            // The km209 review found one large outcrop on the concave switchback lip.
            // Its generic cross-section clamp seats the footprint on the wrong folded
            // triangle, so it reads as a black boulder floating above the slope. Leave this
            // narrow authored window to the dedicated correction stage, which seats the rock
            // against the local surface and keeps the shared scatter deterministic elsewhere.
            if (Mathf.Abs(d - 2090f) < 115f) continue;
            if (Mathf.Abs(d - villageS) < villageHalfM) continue;

            float o = Mathf.Lerp(OutcropInlandMinM, OutcropInlandMaxM, (float)rng.NextDouble());
            if (!OnRibbon(route, i, o)) continue;
            var centre = GroundedPoint(route, i, o, 3f);
            if (RouteClearance(route, centre.x, centre.z, OutcropRoadClearM) < OutcropRoadClearM) continue;

            // the anchor stone: big, sunk to about a third of its height
            float big = 2.4f + (float)rng.NextDouble() * 2.2f;
            Rock(v, uv, tri, centre + Vector3.up * big * 0.12f, big, rng);
            stones++;

            int extra = 2 + rng.Next(4);
            var t = route.Tangent[i]; t.y = 0f; t.Normalize();
            for (int k = 0; k < extra; k++)
            {
                float along = ((float)rng.NextDouble() - 0.5f) * big * 3.2f;
                float across = ((float)rng.NextDouble() - 0.5f) * big * 2.4f;
                int j = Mathf.Clamp(i + Mathf.RoundToInt(along / 3f), 0, route.Count - 1);
                float oj = o + across;
                if (!OnRibbon(route, j, oj)) continue;
                var at = GroundedPoint(route, j, oj, 3f);
                if (RouteClearance(route, at.x, at.z, OutcropRoadClearM) < OutcropRoadClearM) continue;
                float sz = 0.5f + (float)rng.NextDouble() * 1.3f;
                Rock(v, uv, tri, at + Vector3.up * sz * 0.18f, sz, rng);
                stones++;
            }
            clusters++;
        }
        return clusters;
    }

    // --------------------------------------------------------------- mesh helpers

    private static void Quad(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0));
        uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
        if (flip) { tri.Add(i); tri.Add(i + 2); tri.Add(i + 1); tri.Add(i); tri.Add(i + 3); tri.Add(i + 2); }
        else { tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3); }
    }

    /// <summary>Axis-aligned-ish box, yawed to face <paramref name="forward"/>.</summary>
    private static void Box(List<Vector3> v, List<Vector2> uv, List<int> tri,
                            Vector3 centre, Vector3 size, Vector3 forward)
    {
        var f = new Vector3(forward.x, 0f, forward.z);
        if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        var rot = Quaternion.LookRotation(f.normalized, Vector3.up);
        var h = size * 0.5f;
        var corners = new Vector3[8];
        int k = 0;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        for (int sz = -1; sz <= 1; sz += 2)
            corners[k++] = centre + rot * new Vector3(sx * h.x, sy * h.y, sz * h.z);
        // corners index = (x+1)/2*4 + (y+1)/2*2 + (z+1)/2
        Vector3 C(int x, int y, int z) => corners[x * 4 + y * 2 + z];
        Quad(v, uv, tri, C(0, 1, 0), C(1, 1, 0), C(1, 1, 1), C(0, 1, 1), false);   // top
        Quad(v, uv, tri, C(0, 0, 0), C(0, 0, 1), C(1, 0, 1), C(1, 0, 0), false);   // bottom
        Quad(v, uv, tri, C(0, 0, 0), C(1, 0, 0), C(1, 1, 0), C(0, 1, 0), false);
        Quad(v, uv, tri, C(1, 0, 1), C(0, 0, 1), C(0, 1, 1), C(1, 1, 1), false);
        Quad(v, uv, tri, C(0, 0, 1), C(0, 0, 0), C(0, 1, 0), C(0, 1, 1), false);
        Quad(v, uv, tri, C(1, 0, 0), C(1, 0, 1), C(1, 1, 1), C(1, 1, 0), false);
    }

    /// <summary>A closed cone, optionally leaning - the mock's pine canopy and sea stack.</summary>
    private static void Cone(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 centre, float height, float radius, float lean)
    {
        const int seg = 8;
        var apex = centre + new Vector3(lean * height * 0.3f, height * 0.5f, lean * height * 0.2f);
        var baseC = centre - Vector3.up * height * 0.5f;
        int start = v.Count;
        v.Add(apex); uv.Add(new Vector2(0.5f, 1f));
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            v.Add(baseC + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            uv.Add(new Vector2(i / (float)seg, 0f));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = start + 1 + i, b = start + 1 + (i + 1) % seg;
            tri.Add(start); tri.Add(b); tri.Add(a);
            // base cap, so the cone is closed from below
            if (i >= 1 && i < seg - 1)
            {
                tri.Add(start + 1); tri.Add(a); tri.Add(b);
            }
        }
    }

    /// <summary>
    /// A long, serrated mountain RIDGE: a crest line with a base skirt on both sides.
    ///
    /// Replaces the cone scatter the background ranges used to be built from. A cone big enough
    /// to be a mountain (1+ km radius, 8 segments) presents two or three enormous flat facets,
    /// which is why the ranges photographed as cardboard triangles; a ridge presents a
    /// continuous saw-toothed skyline, which is what actually reads as a range once aerial
    /// perspective has flattened all the internal shading away.
    ///
    /// Both slopes are emitted with both windings. These are unlit silhouette layers whose only
    /// job is to occlude sky, and a range viewed from the wrong side of its crest would
    /// otherwise vanish - a failure mode that costs a whole build/capture cycle to spot.
    ///
    /// CARDBOARD-CONE FIX (opening visual overhaul, milestone 2). The previous version sampled
    /// its crest at <c>length / 340 m</c>, clamped to a maximum of 40 stations, and carried a
    /// THREE-point cross-section (base, crest, base). Two consequences, both visible at the
    /// start line:
    ///   * a 3 km ridge got 8-13 stations, i.e. ~250 m of straight skyline per segment. At the
    ///     2.3 km the nearest band sits at, that is ~6 degrees of dead-straight edge, which is
    ///     precisely why the ranges photographed as hard-edged triangles.
    ///   * a 3-point profile is a TENT. RecalculateNormals gives it exactly two flat faces per
    ///     segment, so each flank shaded as one poster-paint plane.
    /// Now: the crest is sampled ~5x finer and driven by summed octaves (a fractal skyline
    /// rather than a random zig-zag), and the cross-section is a 5-point CONVEX profile, so the
    /// flanks curve and shade as a rounded landform. Vert cost stays modest - 5 points x up to
    /// 90 stations per ridge - because every band is still ONE merged mesh and ONE draw call.
    /// </summary>
    private static void Ridge(List<Vector3> v, List<Vector2> uv, List<int> tri,
                              Vector3 start, Vector3 dir, float length, float depth,
                              float baseY, float hLo, float hHi, System.Random rng)
    {
        dir = new Vector3(dir.x, 0f, dir.z).normalized;
        var right = new Vector3(dir.z, 0f, -dir.x);
        // ~33 m of skyline per station on a 3 km ridge - below the angular resolution at which
        // a straight segment reads as a cut edge from the road.
        int n = Mathf.Clamp(Mathf.RoundToInt(length / RidgeStationM), 24, RidgeMaxStations);
        const int W = RidgeCrossPoints;                       // cross-section points
        int b0 = v.Count;

        // Fractal crest. Three octaves of sine with random phase/frequency give a skyline with
        // major peaks, secondary shoulders and fine serration - the profile a real range has -
        // instead of the per-station coin flip that produced a saw-tooth of equal triangles.
        float p1 = (float)rng.NextDouble() * 6.283f, f1 = 1.4f + (float)rng.NextDouble() * 1.3f;
        float p2 = (float)rng.NextDouble() * 6.283f, f2 = 3.3f + (float)rng.NextDouble() * 2.4f;
        float p3 = (float)rng.NextDouble() * 6.283f, f3 = 7.7f + (float)rng.NextDouble() * 5.0f;
        float p4 = (float)rng.NextDouble() * 6.283f, f4 = 2.1f + (float)rng.NextDouble() * 1.1f;
        float hMid = Mathf.Lerp(hLo, hHi, 0.5f + ((float)rng.NextDouble() - 0.5f) * 0.4f);
        // SWING IS A FRACTION OF THE MEAN, NOT THE BAND WIDTH. Using (hHi - hLo) * 0.5 gave the
        // Foot band only +/-17% of crest variation, which rendered as a nearly level plateau
        // edge - still a cut-out, just a hazed one. 0.42 of the mean puts real peaks and real
        // saddles on the skyline, which is what makes a range read as a range.
        float hSwing = hMid * 0.42f;

        for (int k = 0; k <= n; k++)
        {
            float f = k / (float)n;
            // Taper the ends so a range sinks into the haze instead of ending in a vertical wall.
            // Mathf.Sin(PI) returns a SLIGHTLY NEGATIVE float (-8.7e-8), and Pow(negative, 0.42)
            // is NaN - which poisoned the mesh bounds, so Unity culled every range mesh from
            // every camera and the horizon stayed empty even though the geometry was built.
            float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(f * Mathf.PI)), 0.42f);

            float oct = Mathf.Sin(f * f1 * 6.283f + p1) * 0.50f
                      + Mathf.Sin(f * f2 * 6.283f + p2) * 0.31f
                      + Mathf.Sin(f * f3 * 6.283f + p3) * 0.19f;
            float h = (hMid + oct * hSwing) * (0.30f + 0.70f * taper);
            h = Mathf.Max(h, hLo * 0.18f);

            // Depth also breathes along the crest, so the range is not a constant-width wall.
            float dpt = depth * (0.72f + 0.42f * (0.5f + 0.5f * Mathf.Sin(f * f4 * 6.283f + p4)));
            // Lateral wander, low frequency: a range meanders, it does not run on a ruler.
            var p = start + dir * (length * f)
                    + right * (Mathf.Sin(f * f2 * 3.141f + p2) * depth * 0.30f);

            for (int j = 0; j < W; j++)
            {
                float t = j / (float)(W - 1) * 2f - 1f;        // -1 .. +1 across the ridge
                // CONVEX flank. pow(1-|t|, 1.35) puts a shoulder at the half-way point instead of
                // running straight from base to crest, which is what turns a tent into a landform
                // once RecalculateNormals smooths across the extra ring.
                float prof = Mathf.Pow(1f - Mathf.Abs(t), 1.35f);
                v.Add(p + right * (dpt * t) + Vector3.up * (baseY + h * prof));
                uv.Add(new Vector2(f, prof));
            }
        }

        for (int k = 0; k < n; k++)
        for (int j = 0; j < W - 1; j++)
        {
            int a = b0 + k * W + j, b = b0 + (k + 1) * W + j;
            AddQuad(tri, a, a + 1, b + 1, b);
        }
    }

    /// <summary>Metres of skyline per crest station on a background ridge. PROVISIONAL.</summary>
    private const float RidgeStationM = 70f;
    /// <summary>Upper bound on crest stations, so one very long ridge cannot blow the vert budget.</summary>
    private const int RidgeMaxStations = 90;
    /// <summary>Points across a ridge's cross-section. 3 = a flat tent; 5 = a rounded flank.</summary>
    private const int RidgeCrossPoints = 5;

    /// <summary>
    /// One SINGLE-WINDING quad.
    ///
    /// The ridges used to emit every quad with BOTH windings so a range stayed visible from
    /// either side of its crest on a route that doubles back. That was both unnecessary and
    /// actively harmful: MapleRide/HDRP/RidgeHaze already renders these meshes with Cull Off,
    /// which shows both sides for free - while two coincident faces of opposite winding sum to
    /// EXACTLY ZERO in Mesh.RecalculateNormals, leaving a large fraction of the crest vertices
    /// with a (0,0,0) normal. normalize() of that is NaN, and the first render of the haze
    /// shader showed the whole horizon as black-and-white zebra stripes because of it.
    /// Single winding also halves the index count on ~100 ridge meshes.
    /// </summary>
    private static void AddQuad(List<int> tri, int a, int b, int c, int d)
    {
        tri.Add(a); tri.Add(b); tri.Add(c);
        tri.Add(a); tri.Add(c); tri.Add(d);
    }

    /// <summary>A jittered boulder: a subdivided box pushed around by the scatter rng.</summary>
    private static void Rock(List<Vector3> v, List<Vector2> uv, List<int> tri,
                             Vector3 centre, float size, System.Random rng)
    {
        int start = v.Count;
        var dirs = new List<Vector3>();
        for (int y = 0; y < 3; y++)
        {
            float pitch = Mathf.Lerp(-0.9f, 0.95f, y / 2f);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f + y * 0.3f;
                float r = Mathf.Cos(pitch);
                dirs.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(pitch), Mathf.Sin(a) * r));
            }
        }
        foreach (var d in dirs)
        {
            float jitter = 0.75f + (float)rng.NextDouble() * 0.5f;
            var p = centre + Vector3.Scale(d * size * jitter, new Vector3(1f, 0.72f, 1f));
            v.Add(p);
            uv.Add(new Vector2(p.x * 0.2f, p.z * 0.2f));
        }
        const int ring = 6;
        for (int y = 0; y < 2; y++)
        for (int i = 0; i < ring; i++)
        {
            int a = start + y * ring + i;
            int b = start + y * ring + (i + 1) % ring;
            int c = start + (y + 1) * ring + i;
            int d2 = start + (y + 1) * ring + (i + 1) % ring;
            tri.Add(a); tri.Add(c); tri.Add(b);
            tri.Add(b); tri.Add(c); tri.Add(d2);
        }
        // caps
        for (int i = 1; i < ring - 1; i++)
        {
            tri.Add(start); tri.Add(start + i); tri.Add(start + i + 1);
            int top = start + 2 * ring;
            tri.Add(top); tri.Add(top + i + 1); tri.Add(top + i);
        }
    }

    private static Mesh Cylinder(string name, float rBottom, float rTop, float height, int seg)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
            var b0 = new Vector3(Mathf.Cos(a0) * rBottom, 0f, Mathf.Sin(a0) * rBottom);
            var b1 = new Vector3(Mathf.Cos(a1) * rBottom, 0f, Mathf.Sin(a1) * rBottom);
            var t0 = new Vector3(Mathf.Cos(a0) * rTop, height, Mathf.Sin(a0) * rTop);
            var t1 = new Vector3(Mathf.Cos(a1) * rTop, height, Mathf.Sin(a1) * rTop);
            Quad(v, uv, tri, b0, b1, t1, t0, false);
        }
        // caps
        int capStart = v.Count;
        v.Add(new Vector3(0f, height, 0f)); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a) * rTop, height, Mathf.Sin(a) * rTop));
            uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
        }
        for (int i = 0; i < seg; i++)
        {
            tri.Add(capStart);
            tri.Add(capStart + 1 + i);
            tri.Add(capStart + 1 + (i + 1) % seg);
        }
        return Finish(name, v.ToArray(), uv.ToArray(), tri);
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
        // UV1 carries the terrain shader's (rock, scree) splat weights. It must be written even
        // when it is all zeros: MapleRide/SakuraTerrain reads uv2_RockTex, and on a mesh with no
        // UV1 Unity falls back to UV0 - whose v runs to hundreds of metres, saturates to 1 and
        // painted the whole green headland as beige SCREE. That is exactly what the first
        // gameplay frame of this pass showed.
        if (uv2 != null) mesh.uv2 = uv2;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static GameObject AddMesh(Transform parent, string name, Mesh mesh, Material mat,
                                      bool collider, Vector3? localPos = null)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos ?? Vector3.zero;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // Persist the mesh so the saved scene does not carry a dangling reference to a runtime
        // Mesh object (which would come back as an empty renderer on the next editor session).
        string path = $"{MeshDir}/{mesh.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return go;
    }

    // --------------------------------------------------------------- materials

    private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

    private static Material LoadOrCreate(string name, string shaderName)
    {
        // Resolve through MapleRideShaderNames rather than using the Built-in name directly.
        // This method re-assigns the shader on every build, so a hard-coded Built-in name here
        // silently reverted the HDRP material conversion each time the coast was regenerated -
        // including inside the diagnostics capture that was supposed to verify the conversion.
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

    /// <summary>
    /// Material for the background ranges (inland bands + the seaward backdrop).
    ///
    /// These used to be CelMaterial, hand-authored almost black so they would not out-brighten
    /// the sky - which is what produced the flat dark-teal "cardboard cones" at the start line.
    /// They now use MapleRide/HDRP/RidgeHaze, which computes its OWN aerial perspective (a
    /// distance ramp plus a valley-fill term that dissolves the feet of a range into the air),
    /// so each band can be authored at an honest landform colour and still sit correctly behind
    /// the one in front of it.
    ///
    /// All values here are PROVISIONAL art tuning.
    /// </summary>
    /// <param name="hazeMin">Haze already applied at <paramref name="hazeStart"/> metres.</param>
    /// <param name="hazeMax">Haze at <paramref name="hazeFull"/> metres and beyond.</param>
    private static Material RidgeMaterial(string name, Color baseColour, Color crestColour,
                                          float hazeStart, float hazeFull,
                                          float hazeMin, float hazeMax,
                                          float baseY, float valleyHeight, float valleyFill,
                                          float crestHeight)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        // Resolved directly rather than through MapleRideShaderNames: there is no Built-in
        // counterpart to fall back to, so if the HDRP shader is missing (a rollback to the
        // Built-in checkpoint) the ranges must land on CelLit rather than on a null shader,
        // which would render magenta across the whole horizon.
        var shader = Shader.Find(RidgeShaderName);
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning($"[shiosai] '{RidgeShaderName}' unavailable - background ranges fall " +
                             "back to CelLit and will not be hazed.");
            return CelMaterial(name, baseColour, gloss: 0f, spec: 0f, rim: 0.10f);
        }

        string path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.name = name;
        mat.enableInstancing = true;

        mat.SetColor("_Color", baseColour);
        mat.SetColor("_CrestColor", crestColour);
        mat.SetColor("_HazeColor", RidgeHazeColour);
        mat.SetFloat("_HazeStart", hazeStart);
        mat.SetFloat("_HazeFull", hazeFull);
        mat.SetFloat("_HazeMin", hazeMin);
        mat.SetFloat("_HazeMax", hazeMax);
        mat.SetFloat("_BaseY", baseY);
        mat.SetFloat("_ValleyHeight", valleyHeight);
        mat.SetFloat("_ValleyFill", valleyFill);
        // Form shading is deliberately weak but NOT absent: at 0.22 the first render's ridges had
        // no internal modelling left once hazed, so each one read as a single pale silhouette.
        mat.SetFloat("_FormShading", 0.34f);
        mat.SetFloat("_CrestHeight", crestHeight);
        mat.SetFloat("_MacroNoise", 0.22f);
        mat.SetFloat("_MacroScale", 760f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    private static Material CelMaterial(string name, Color albedo, float gloss = 0.2f,                                        float spec = 0.18f, float rim = 0.7f, Texture texture = null)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, CelShaderName);
        mat.SetColor("_Color", albedo);
        mat.SetColor("_ShadeColor", new Color(0.60f, 0.70f, 0.86f, 1f));   // cool sea-sky bounce, not the pass's mauve
        mat.SetFloat("_ShadeStrength", 0.55f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.06f);
        mat.SetColor("_RimColor", new Color(0.88f, 0.95f, 1f, 1f));        // daylight rim, not sunset amber
        mat.SetFloat("_RimStrength", rim);
        mat.SetFloat("_Gloss", gloss);
        mat.SetFloat("_SpecStrength", spec);
        // Unconditional - see ArchitectureMaterial for why a null-guarded assignment cannot
        // remove a map that is already baked into the persisted .mat asset.
        mat.SetTexture("_MainTex", texture);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// FIDELITY POC: a genuine PBR surface on the hero geometry, via HDRP's own <c>HDRP/Lit</c>.
    ///
    /// WHY THIS EXISTS. Every coast surface currently runs through <c>MapleRide/HDRP/CelLit</c>,
    /// which has no normal-map slot at all and quantises diffuse into 3 hard bands. On the
    /// carriageway - the single largest thing in almost every frame - that produces the flat,
    /// matte, self-lit grey sheet visible in reference/good_graphics/BEFORE_diag_shiosai_start_3q.png.
    /// The asphalt ALREADY ships an albedo + normal + roughness set
    /// (Shiosai_Asphalt_{Albedo,Normal,Rough}.png); CelLit was simply throwing two thirds of it
    /// away. This helper wires the full set up instead, so the road gets real grazing-angle
    /// specular, real surface grain, and real response to the sun - the "wet-ish worn tarmac"
    /// read the target frame has.
    ///
    /// The cel shader is deliberately NOT modified: it stays the default for the stylised
    /// props, and this is opt-in per hero surface, so the POC is reversible.
    ///
    /// The mask map (R=metallic, G=AO, B=detail, A=smoothness) is generated from the roughness
    /// map by tools/blender/build_shiosai_maskmaps.py. If it is missing the material still
    /// works, falling back to the scalar smoothness below.
    /// </summary>
    /// <param name="tileM">Metres per texture tile, isotropic on both axes.</param>
    private static Material PbrMaterial(string name, string texStem, float tileM,
                                        float smoothness = 0.25f, float normalScale = 1f,
                                        Color? tint = null)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        // Resolved directly: HDRP/Lit is HDRP's built-in surface shader, so there is no
        // MapleRideShaderNames entry and no Built-in counterpart to fall back to.
        var mat = LoadOrCreate(name, "HDRP/Lit");

        var albedo = CoastTexture($"{texStem}_Albedo.png");
        var normal = CoastTexture($"{texStem}_Normal.png");
        var mask   = CoastTexture($"{texStem}_Mask.png");

        // Assigned UNCONDITIONALLY, for the same reason ArchitectureMaterial documents: these
        // are persisted .mat assets, so a null-guarded assignment can only ever ADD a map.
        mat.SetColor("_BaseColor", tint ?? Color.white);
        mat.SetTexture("_BaseColorMap", albedo);
        mat.SetTexture("_NormalMap", normal);
        mat.SetTexture("_MaskMap", mask);
        mat.SetFloat("_NormalScale", normalScale);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", smoothness);

        // HDRP reads smoothness from the mask's ALPHA and remaps it through these two. Without
        // the remap pair the mask is imported but ignored, which looks exactly like "the mask
        // map did nothing".
        mat.SetFloat("_SmoothnessRemapMin", 0f);
        mat.SetFloat("_SmoothnessRemapMax", mask != null ? 1f : smoothness);
        mat.SetFloat("_AORemapMin", 0f);
        mat.SetFloat("_AORemapMax", 1f);

        // HDRP/Lit keys off shader keywords, not just properties: assigning _NormalMap and
        // _MaskMap by hand leaves _NORMALMAP / _MASKMAP undefined, so the maps are bound to the
        // sampler and then never read. This is the PBR equivalent of the _Cull=0 invariant -
        // everything probes perfectly and renders as if nothing was assigned.
        if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
        if (mask != null) mat.EnableKeyword("_MASKMAP"); else mat.DisableKeyword("_MASKMAP");

        float tiles = tileM > 0.001f ? 1f / tileM : 1f;
        mat.SetVector("_UVMappingMask", new Vector4(1, 0, 0, 0));   // UV0
        mat.mainTextureScale = new Vector2(1f, 1f);                 // UVs are already in metres/tileM

        HDMaterial.ValidateMaterial(mat);
        EditorUtility.SetDirty(mat);
        Debug.Log($"[shiosai] PBR '{name}': albedo={(albedo != null)} normal={(normal != null)} " +
                  $"mask={(mask != null)} tileM={tileM} (1/{tileM}={tiles:0.###}) smooth={smoothness}");
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>Continuous-lighting, textured material used only by the coastal town benchmark.</summary>
    private static Material ArchitectureMaterial(string name, Color albedo, Texture texture = null,                                                 float smoothness = 0.25f, float metallic = 0f)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, ArchitectureShaderName);
        mat.SetColor("_Color", albedo);
        mat.SetFloat("_Glossiness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_BumpStrength", 0.45f);
        mat.SetFloat("_Occlusion", 0.92f);
        // Assigned UNCONDITIONALLY. These materials are persisted .mat assets, so the old
        // "if (texture != null)" could only ever ADD a map - once a texture had been baked into
        // the asset, changing the C# to pass null left the old map in place for ever. That is
        // why the harbour facades kept rendering as dark weatherboard stripes after the code had
        // been switched to flat cream stucco. Writing the intended value every build is also
        // what makes the pass idempotent.
        mat.SetTexture("_MainTex", texture);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// The cliff face was the one band still painted with the plain single-tap SakuraCel
    /// material (_MainTex = Sakura_Rock_Albedo.png stretched 1:1 over the ribbon UVs). That
    /// texture is a strata scan authored to be broken up by TRIPLANAR world-space projection,
    /// macro-scale tint variation and a normal map (see MapleRide/SakuraTerrain, which is what
    /// Sakura Pass's own cliffs actually use) - sampled planar and un-varied like this, its
    /// high-frequency dark/light bands alias into flat "TV static"/camouflage instead of
    /// painterly rock. Reuse the SAME shared terrain shader and textures Sakura Pass already
    /// uses rather than hand-tuning a second rock look: no baked splat UV1 is needed because
    /// the cliff ribbon is steep enough everywhere that the shader's own slope-based auto-rock
    /// term (_SlopeRockStart/_SlopeRockEnd) already selects 100% rock, with grass only fading
    /// in if a future revision ever flattens part of the band.
    /// </summary>
    private static Material CliffRockMaterial()
    {
        const string name = "Shiosai_CliffRock";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.SetTexture("_GrassTex", CoastTexture("Shiosai_Grass_Albedo.png"));
        mat.SetTexture("_GrassNormal", CoastTexture("Shiosai_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", CoastTexture("Shiosai_Grass_Rough.png"));
        // COAST-OWN ROCK, not Sakura's. Sakura_Rock_Albedo.png is a dark slate / khaki / OLIVE
        // field - visually literal military camouflage - and no amount of triplanar break-up can
        // make a palette that wrong read as sunlit coastal stone. It is what made the cliff skirt
        // look like camo netting and the sea stacks like shattered black blobs (fidelity findings
        // #1/#2). Shiosai_Rock_* is stratified pale grey-tan stone authored for this region.
        mat.SetTexture("_RockTex", CoastTexture("Shiosai_Rock_Albedo.png"));
        mat.SetTexture("_RockNormal", CoastTexture("Shiosai_Rock_Normal.png"));
        mat.SetTexture("_RockRough", CoastTexture("Shiosai_Rock_Rough.png"));
        mat.SetTexture("_ScreeTex", CoastTexture("Shiosai_Scree_Albedo.png"));
        mat.SetTexture("_ScreeNormal", CoastTexture("Shiosai_Scree_Normal.png"));
        mat.SetTexture("_ScreeRough", CoastTexture("Shiosai_Scree_Rough.png"));
        mat.SetColor("_GrassColor", new Color(0.235f, 0.395f, 0.175f, 1f));   // match Shiosai_Grass
        // The albedo already carries the coast's pale grey-tan value range, so the tint is a mild
        // darkening multiplier. PROVISIONAL: a near-white tint here blew the cliffs out to white.
        // FIX #6 (ch1 gateway "grey slab"). The Coast Cliff Face ribbon (the only mesh using this
        // material - the sea arch overrides its own _RockColor, and the stacks/plinth are separate
        // props) reads at the ch1 gateway as a flat muddy-brown wall: it is a large near-planar
        // face broadside to the cam and largely turned away from the low warm key, so at the old
        // dark (0.40,0.38,0.35) darkening tint it sat as dead mud. Lifting + slightly warming the
        // tint (still a sub-1.0 multiplier, well under the near-white blowout the comment warns of)
        // lets the pale grey-tan albedo read as sunlit coastal stone even on the shaded face.
        mat.SetColor("_RockColor", new Color(0.52f, 0.48f, 0.42f, 1f));
        mat.SetColor("_ScreeColor", new Color(0.52f, 0.48f, 0.42f, 1f));
        mat.SetFloat("_GrassScale", 6.0f);
        // A near-vertical undercut cliff wall is exactly the tall-cut-face case Sakura's own
        // comment warns about ("40 m tall and near-vertical... visible quilt"); use the same
        // coarser tiling that fixed it there.
        mat.SetFloat("_RockScale", 15.0f);
        mat.SetFloat("_ScreeScale", 4.0f);
        mat.SetFloat("_SlopeRockStart", 30f);
        mat.SetFloat("_SlopeRockEnd", 50f);
        mat.SetFloat("_MacroVariation", 0.35f);
        mat.SetFloat("_NormalStrength", 1.0f);
        // Daylight cel tuning to match the rest of the coast's CelMaterial() palette, not the
        // pass's mauve sunset defaults baked into the shared shader.
        mat.SetColor("_ShadeColor", new Color(0.60f, 0.70f, 0.86f, 1f));
        mat.SetFloat("_ShadeStrength", 0.55f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.06f);
        mat.SetColor("_RimColor", new Color(0.88f, 0.95f, 1f, 1f));
        mat.SetFloat("_RimStrength", 0.34f);
        mat.SetFloat("_SpecStrength", 0.10f);
        mat.SetVector("_HeightRange", new Vector4(1000f, 2000f, 0f, 0f)); // disable snow tint on the coast
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// The sea-arch gate's rock, which differs from <see cref="CliffRockMaterial"/> in exactly
    /// one respect: the camera rides THROUGH the opening, so it must be DOUBLE-SIDED.
    ///
    /// Invariant 3 of the environment skill: geometry the camera is inside (tunnels, rings,
    /// skirts) must set _Cull = 0 or the inward-facing half of the shell is simply not drawn and
    /// the "tunnel" is invisible from under it. Copying the cliff material wholesale and then
    /// overriding _Cull keeps the two rock looks identical by construction.
    /// </summary>
    private static Material SeaArchRockMaterial()
    {
        const string name = "Shiosai_SeaArchRock";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var src = CliffRockMaterial();
        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.CopyPropertiesFromMaterial(src);
        mat.shader = src.shader;
        mat.name = name;
        mat.SetFloat("_Cull", 0f);                       // <- the whole point of this material
        mat.SetFloat("_SlopeRockStart", 0f);             // an arch soffit is never grass
        mat.SetFloat("_SlopeRockEnd", 1f);
        // M4b: the arch is a convex free-standing prop like the stacks, not a planar wall, so
        // it hit the same rim-wash blowout - ch7 rendered it near-white. Same remedy.
        mat.SetColor("_RockColor", new Color(0.32f, 0.31f, 0.29f, 1f));
        mat.SetColor("_ScreeColor", new Color(0.32f, 0.31f, 0.29f, 1f));
        mat.SetFloat("_RimStrength", 0.10f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// DARK BORE INTERIOR. The camera is inside this, so _Cull = 0 is mandatory (invariant 3):
    /// a one-sided bore renders as nothing at all from within and the tunnel becomes an
    /// invisible hole in the hillside. Derived from the cliff rock so the stone reads as the
    /// same headland the road burrows into, then dropped to roughly a fifth of its value and
    /// cooled, which is what "wet dark stone" looks like out of the sun.
    /// </summary>
    private static Material TunnelInteriorMaterial()
    {
        const string name = "Shiosai_TunnelInterior";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var src = CliffRockMaterial();
        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.CopyPropertiesFromMaterial(src);
        mat.shader = src.shader;
        mat.name = name;
        mat.SetFloat("_Cull", 0f);                       // <- the whole point of this material
        mat.SetFloat("_SlopeRockStart", 0f);             // a bore soffit is never grass
        mat.SetFloat("_SlopeRockEnd", 1f);
        var dark = new Color(0.055f, 0.058f, 0.070f, 1f);
        mat.SetColor("_RockColor", dark);
        mat.SetColor("_ScreeColor", dark);
        mat.SetColor("_GrassColor", dark);
        mat.SetFloat("_RimStrength", 0.02f);             // no rim wash: this is unlit stone
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Self-lit sodium-amber lamp material for the tunnel crown strips.
    ///
    /// Must be UNLIT: the bore is a sealed volume with no sun and almost no ambient, so the
    /// previous cel-shaded lamp material shaded down to black along with the walls. HDRP/Unlit
    /// with an emissive colour is the cheapest thing that actually emits pixels - it needs no
    /// Light component, adds no realtime light cost, and keeps the surrounding bore dark.
    /// Falls back to a flat bright cel material if HDRP/Unlit is missing (Built-in rollback),
    /// which still reads as a lamp even though it will not glow.
    /// </summary>
    private static Material TunnelLampMaterial()
    {
        const string name = "Shiosai_TunnelLamp";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("[shiosai] 'HDRP/Unlit' unavailable - tunnel lamps fall back to cel " +
                             "and will not glow.");
            return CelMaterial(name, TunnelLampColour, gloss: 0f, spec: 0f, rim: 0f);
        }

        string path = $"{MaterialDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.name = name;
        mat.enableInstancing = true;

        // HDRP/Unlit does not use _Color. Set every spelling that could apply so the material
        // is correct whether it was freshly created or carried over from an older shader.
        if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", TunnelLampColour);
        if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", null);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", TunnelLampColour);

        // Emission. _UseEmissiveIntensity = 1 makes HDRP treat _EmissiveColorLDR x
        // _EmissiveIntensity as the authored value, which is the only combination that survives
        // a material re-save; setting _EmissiveColor alone gets recomputed away by the HDRP
        // material editor on the next import.
        if (mat.HasProperty("_UseEmissiveIntensity")) mat.SetFloat("_UseEmissiveIntensity", 1f);
        if (mat.HasProperty("_EmissiveIntensityUnit")) mat.SetFloat("_EmissiveIntensityUnit", 0f); // nits
        if (mat.HasProperty("_EmissiveIntensity")) mat.SetFloat("_EmissiveIntensity", TunnelLampNits);
        if (mat.HasProperty("_EmissiveColorLDR")) mat.SetColor("_EmissiveColorLDR", TunnelLampColour);
        if (mat.HasProperty("_EmissiveColor"))
            mat.SetColor("_EmissiveColor", TunnelLampColour * TunnelLampNits);
        if (mat.HasProperty("_EmissiveExposureWeight")) mat.SetFloat("_EmissiveExposureWeight", 0f);
        mat.EnableKeyword("_EMISSIVE_COLOR_MAP");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// PORTAL REVEAL: the ring of wet-darkened stone framing each mouth. Also double-sided -
    /// the exit ring is seen from inside the bore on the way out, and the entry ring from
    /// inside on the way in. Mid-way in value between the daylit massif and the dark bore, so
    /// the mouth reads as a real transition rather than a hard black cut-out.
    /// </summary>
    private static Material TunnelPortalMaterial()
    {
        const string name = "Shiosai_TunnelPortal";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var src = CliffRockMaterial();
        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.CopyPropertiesFromMaterial(src);
        mat.shader = src.shader;
        mat.name = name;
        mat.SetFloat("_Cull", 0f);
        mat.SetFloat("_SlopeRockStart", 0f);
        mat.SetFloat("_SlopeRockEnd", 1f);
        var wet = new Color(0.135f, 0.140f, 0.150f, 1f);
        mat.SetColor("_RockColor", wet);
        mat.SetColor("_ScreeColor", wet);
        mat.SetFloat("_RimStrength", 0.06f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// The offshore sea stacks, the lighthouse's rock plinth, and any other freestanding
    /// "rock/stack/plinth/stone" prop were STILL being routed (via <see cref="CoastMaterialFor"/>)
    /// to the same plain single-tap SakuraCel material as the cliff face used to use before
    /// <see cref="CliffRockMaterial"/> - i.e. Sakura_Rock_Albedo.png stretched planar over each
    /// prop's own UVs with no triplanar break-up, which is exactly the "TV static"/camouflage
    /// failure mode finding #2 described, just on a different set of meshes. These props are far
    /// too small and irregular (a squat prism, not a swept ribbon) for hand-tuned planar UVs to
    /// ever read as fine rock grain - ride the SAME shared triplanar terrain shader Sakura Pass
    /// and the fixed cliff face use instead of re-deriving a second rock look. Unlike the cliff,
    /// these are never grassy, so pin the slope-based auto-blend fully to rock regardless of the
    /// prop's (often near-vertical AND overhanging/undercut) local surface slope.
    /// </summary>
    private static Material RockPropMaterial()
    {
        const string name = "Shiosai_RockProp";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.SetTexture("_GrassTex", CoastTexture("Shiosai_Grass_Albedo.png"));
        mat.SetTexture("_GrassNormal", CoastTexture("Shiosai_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", CoastTexture("Shiosai_Grass_Rough.png"));
        // Same coast-own stratified rock as the cliff band - see CliffRockMaterial for why the
        // borrowed Sakura maps had to go.
        mat.SetTexture("_RockTex", CoastTexture("Shiosai_Rock_Albedo.png"));
        mat.SetTexture("_RockNormal", CoastTexture("Shiosai_Rock_Normal.png"));
        mat.SetTexture("_RockRough", CoastTexture("Shiosai_Rock_Rough.png"));
        mat.SetTexture("_ScreeTex", CoastTexture("Shiosai_Scree_Albedo.png"));
        mat.SetTexture("_ScreeNormal", CoastTexture("Shiosai_Scree_Normal.png"));
        mat.SetTexture("_ScreeRough", CoastTexture("Shiosai_Scree_Rough.png"));
        // M4b: 0.42 matched the cliff band, but the cliff band is a near-planar wall while a
        // sea stack is a small CONVEX prop - every part of its silhouette sits at a grazing
        // view angle, so the rim term applies almost everywhere at once instead of only along
        // an edge. At _RimStrength 0.34 that washed the whole stack to near-white lilac (see
        // the M4 overlook render) even though the identical tint reads correctly mid-grey on
        // the cliff. Convex props need a darker base AND a rim reserved for the true edge.
        mat.SetColor("_RockColor", new Color(0.26f, 0.25f, 0.24f, 1f));
        mat.SetColor("_ScreeColor", new Color(0.26f, 0.25f, 0.24f, 1f));
        // Stacks are ~5-16 m tall - far smaller than the 40 m cliff band - so the grain has to
        // be tuned finer than the cliff's _RockScale=15 or it would still read as a couple of
        // giant blotches across the whole prop.
        mat.SetFloat("_RockScale", 5.0f);
        mat.SetFloat("_ScreeScale", 3.0f);
        // Force 100% rock everywhere: these props have no grass band, and their undercut
        // profile means "slope" is not a reliable grass/rock discriminator like it is on the
        // swept cliff ribbon.
        mat.SetFloat("_SlopeRockStart", 0f);
        mat.SetFloat("_SlopeRockEnd", 1f);
        mat.SetFloat("_MacroVariation", 0.40f);
        mat.SetFloat("_NormalStrength", 1.0f);
        mat.SetColor("_ShadeColor", new Color(0.52f, 0.60f, 0.76f, 1f));
        mat.SetFloat("_ShadeStrength", 0.62f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.06f);
        mat.SetColor("_RimColor", new Color(0.86f, 0.92f, 1f, 1f));
        mat.SetFloat("_RimStrength", 0.10f);   // M4b: see _RockColor note - convex prop, not a wall
        mat.SetFloat("_SpecStrength", 0.10f);
        mat.SetVector("_HeightRange", new Vector4(1000f, 2000f, 0f, 0f)); // no snow tint on the coast
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    private static Material WaterMaterial()
    {
        if (MaterialCache.TryGetValue("Water", out var cached) && cached != null) return cached;

        var mat = LoadOrCreate("Shiosai_Ocean", WaterShaderName);

        // SPEC 3.2 PALETTE, MAPPED ONTO THE SHADER'S OWN LOGIC.
        //
        // MapleRide/HDRP/Ocean computes  body = lerp(_DeepColor, _ShallowColor, facing)  where
        // facing = dot(surface normal, view). For a flat sea that makes `facing` a proxy for
        // VIEW ANGLE, i.e. for distance: water near the rider is looked at steeply (facing -> 1)
        // and water out toward the horizon is grazing (facing -> 0), with _SkyTint taking over
        // at the extreme grazing angles through the Fresnel term. That is exactly the spec's
        // near-ocean / far-ocean / horizon triple, so the three palette anchors drop straight in:
        //
        //     _ShallowColor = near ocean turquoise  #20AFC1
        //     _DeepColor    = far ocean cobalt      #2377B9
        //     _SkyTint      = horizon               #466E9C
        //     _FoamColor    = foam                  #F4FCFB
        //
        // WHY THE OLD VALUES WERE WRONG, not merely different. A previous pass authored these
        // very dark (deep = 0.006, 0.045, 0.17) to stop the bay "blooming into swimming-pool
        // turquoise". But BOTH ends were darkened, so `facing` had nothing left to interpolate
        // between and every view of the water - near, far, cove, headland - resolved to the same
        // near-black navy: the flat dark plane the QA pass reported. The ocean is supposed to be
        // the region's dominant cool colour mass (3.2), and 3.3 makes foam one of the two
        // brightest features in the scene; a uniformly dark sea fails both.
        //
        // Colours are set in LINEAR (the shader bridges back with MR_AuthoredCol), so each value
        // below is the linear form of its sRGB hex, not the hex itself.
        // OCEAN-REALISM MILESTONE: THE TURQUOISE MOVED OFF THE OCEAN AND ONTO THE SHELF.
        //
        // Until now the ocean plane itself carried the turquoise -> cobalt gradient, driven by
        // DISTANCE FROM THE CAMERA (_DepthBlend = 1). That model has one fatal property: it is
        // a function of where you stand, not of the water. From the harbour overlook every
        // square metre within a couple of hundred metres of the eye - open sea included - came
        // out swimming-pool turquoise, and the bright band swam around as the camera moved.
        // Water colour is a property of DEPTH, and depth is now known exactly: BuildShallows
        // sweeps the shelf over the cross-section's real sea-floor nodes and bakes the true
        // depth per vertex (see MapleRideShallows.shader).
        //
        // So the ocean plane's job changes: it is now only ever OPEN WATER, and open water is
        // dark and saturated when looked into steeply and light and sky-reflecting at grazing
        // angles. The shader's view-angle model says exactly that once the two anchors are
        // filled in with the right ends, so _DepthBlend goes back to 0 and the names read
        // backwards on purpose:
        //     _ShallowColor = the colour of water looked straight DOWN into (deepest reading)
        //     _DeepColor    = the colour at a grazing angle, out toward the middle distance
        //     _SkyTint      = the horizon, taken over by the Fresnel term
        // Sakura Pass's lake is unaffected: it has its own material and its own values.
        SetIf(mat, "_ShallowColor", Srgb(0x0D, 0x3C, 0x76));   // looked into steeply: deep blue
        SetIf(mat, "_DeepColor", Srgb(0x1C, 0x63, 0xAC));      // grazing / middle distance cobalt
        SetIf(mat, "_SkyTint", Srgb(0x4E, 0x88, 0xC6));        // horizon - matched to the deep-blue sky
        SetIf(mat, "_FoamColor", Srgb(0xF4, 0xFC, 0xFB));      // foam
        SetIf(mat, "_MidColor", Srgb(0x19, 0x5E, 0xA6));       // (unused while _DepthBlend = 0)
        if (mat.HasProperty("_DepthBlend")) mat.SetFloat("_DepthBlend", 0f);
        if (mat.HasProperty("_ShoreFadeStart")) mat.SetFloat("_ShoreFadeStart", 35f);
        if (mat.HasProperty("_ShoreFadeEnd")) mat.SetFloat("_ShoreFadeEnd", 1600f);
        SetIf(mat, "_Color", Srgb(0x1C, 0x63, 0xAC));
        SetIf(mat, "_SpecColor", new Color(1f, 0.99f, 0.94f, 1f));
        SetIf(mat, "_SunColor", Srgb(0xFF, 0xF3, 0xDC));       // sunny-noon sparkle, not golden hour
        if (mat.HasProperty("_FresnelBoost")) mat.SetFloat("_FresnelBoost", 0.55f);
        if (mat.HasProperty("_FresnelPower")) mat.SetFloat("_FresnelPower", 4.6f);
        // START-LINE MILESTONE (gap #4): the reference plate's sea carries visible sun sparkle.
        // 0.75 was tuned when the sky was a pale overcast wash and the whole frame sat at a much
        // lower contrast; against the new deep-blue sky the sparkle had disappeared entirely.
        // PROVISIONAL.
        if (mat.HasProperty("_GlitterBoost")) mat.SetFloat("_GlitterBoost", 1.3f);
        // GOLD HORIZON BAND. The coast OPTS IN to the shader's glitter falloff; Sakura Pass's
        // lake leaves _GlitterFalloff at 0 and renders exactly as before. Without this the far
        // ocean - where the wave normal has faded to a perfect mirror - resolved to a solid bar
        // of _SunColor along the entire horizon plus a blown warm smear on the water.
        // Both PROVISIONAL, tuned against diag_shiosai_ch8_highway.png.
        if (mat.HasProperty("_GlitterFalloff")) mat.SetFloat("_GlitterFalloff", 1f);
        if (mat.HasProperty("_GlitterGrazeEnd")) mat.SetFloat("_GlitterGrazeEnd", 0.35f);
        // Foam is a named value-hierarchy anchor, not a garnish: at 0.10 the crests never
        // resolved at all beyond a few metres.
        if (mat.HasProperty("_FoamAmount")) mat.SetFloat("_FoamAmount", 0.22f);
        // Ocean swell is longer and stronger than the pass's lake.
        if (mat.HasProperty("_WaveScale")) mat.SetFloat("_WaveScale", 0.10f);
        if (mat.HasProperty("_WaveStrength")) mat.SetFloat("_WaveStrength", 0.38f);
        // The ripple/foam detail used to be gone by 900 m, which on a region whose hero views are
        // 2-6 km of open water meant almost all of the sea was a flat untextured sheet.
        if (mat.HasProperty("_DetailFadeStart")) mat.SetFloat("_DetailFadeStart", 260f);
        if (mat.HasProperty("_DetailFadeEnd")) mat.SetFloat("_DetailFadeEnd", 3200f);
        // PROVISIONAL (tuned against diag_shiosai_seaview_*.png, not derived): the ocean opts in
        // to the ambient term the shader now exposes. Sakura Pass's lake keeps the old sun-only
        // lighting because the property defaults to 0 there.
        // START-LINE MILESTONE (gap #4): lifted from 0.65/1.15. Against the deep-blue sky the sea
        // was the darkest mass in frame and read as near-black navy rather than as the reference
        // plate's bright saturated blue. Both PROVISIONAL.
        if (mat.HasProperty("_AmbientWeight")) mat.SetFloat("_AmbientWeight", 0.85f);
        if (mat.HasProperty("_LightGain")) mat.SetFloat("_LightGain", 1.35f);
        // OCEAN-REALISM MILESTONE: opt in to the multi-scale micro chop (see the shader's
        // _ChopWeight). The coast's hero views are 300 m - 6 km of open water, where a sea of
        // three swell trains alone reads as a smooth vinyl sheet. Sakura Pass's lake leaves
        // _ChopWeight at 0 and is byte-identical. All three PROVISIONAL.
        if (mat.HasProperty("_ChopWeight")) mat.SetFloat("_ChopWeight", 1f);
        if (mat.HasProperty("_ChopScale")) mat.SetFloat("_ChopScale", 5f);
        if (mat.HasProperty("_ChopStrength")) mat.SetFloat("_ChopStrength", 0.30f);
        EditorUtility.SetDirty(mat);
        MaterialCache["Water"] = mat;
        return mat;
    }

    /// <summary>Sets a colour only when the shader actually has the property.</summary>
    private static void SetIf(Material m, string prop, Color c)
    {
        if (m.HasProperty(prop)) m.SetColor(prop, c);
    }

    /// <summary>
    /// Converts a spec 3.2 sRGB hex triple into the LINEAR colour a material property must hold.
    ///
    /// The palette in the spec is written as sRGB hex, the project renders Linear, and the
    /// MapleRide HDRP shaders bridge material Color properties back to authored space with
    /// MR_AuthoredCol (= LinearToSRGB). Writing the hex values straight into SetColor would
    /// therefore land every palette anchor about one gamma step too bright - which is the
    /// mistake that makes "matched to the spec palette" look nothing like the spec.
    /// </summary>
    private static Color Srgb(int r, int g, int b) =>
        new Color(SrgbToLinear(r / 255f), SrgbToLinear(g / 255f), SrgbToLinear(b / 255f), 1f);

    private static float SrgbToLinear(float c) =>
        c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>
    /// THE FIX FOR THE "FLOATING GREY ISLAND WITH A SAND STRIPE" AT THE MOUNTAIN GATEWAY.
    ///
    /// The landform is four whole-route ribbons with ONE material each: sea floor, beach, cliff
    /// face, headland. Inland (see <see cref="Seaness"/>) the same nodes are re-aimed at a
    /// mountain flank - but the sea-floor band was a flat grey-green cel material and the beach
    /// band was flat sand, so at the Gateway the mountainside below the road rendered as a grey
    /// shelf with a beach stripe running along it 150 m up a mountain. <see cref="SplatWeights"/>
    /// has ALWAYS published the correct per-node intent (grass inland, sand/shingle on the
    /// coast) in UV1 - the two lower bands simply used a shader that cannot read it.
    ///
    /// Both bands now use the shared triplanar terrain shader, with the coast's own SAND map in
    /// the scree slot. That makes one material serve both stories: splat (0.05, 0.95) on the
    /// coast paints it sand, splat (0.28, 0.04) inland paints it grass over rock, and the
    /// transition is the same smooth Seaness blend the geometry already uses.
    /// </summary>
    private static Material ShoreTerrainMaterial(string name)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, TerrainShaderName);
        mat.SetTexture("_GrassTex", CoastTexture("Shiosai_HokkaidoGround_Albedo.png"));
        mat.SetTexture("_GrassNormal", CoastTexture("Shiosai_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", CoastTexture("Shiosai_Grass_Rough.png"));
        mat.SetTexture("_RockTex", CoastTexture("Shiosai_Rock_Albedo.png"));
        mat.SetTexture("_RockNormal", CoastTexture("Shiosai_Rock_Normal.png"));
        mat.SetTexture("_RockRough", CoastTexture("Shiosai_Rock_Rough.png"));
        // SCREE SLOT = SAND. This is the whole trick: the splat channel the beach already asks
        // for (0.95 scree) becomes the coast's sand, while inland it drops to 0.04 and the
        // grass takes over.
        mat.SetTexture("_ScreeTex", CoastTexture("Shiosai_Sand_Albedo.png"));
        mat.SetTexture("_ScreeNormal", CoastTexture("Shiosai_Sand_Normal.png"));
        mat.SetTexture("_ScreeRough", CoastTexture("Shiosai_Sand_Rough.png"));
        mat.SetColor("_GrassColor", new Color(0.90f, 0.96f, 0.84f, 1f));   // map is pre-coloured
        mat.SetColor("_RockColor", new Color(0.40f, 0.38f, 0.35f, 1f));
        mat.SetColor("_ScreeColor", new Color(0.40f, 0.37f, 0.32f, 1f));   // warm pale sand
        mat.SetFloat("_GrassScale", 5.0f);
        mat.SetFloat("_RockScale", 12.0f);
        mat.SetFloat("_ScreeScale", 6.0f);
        // The shader adds its own slope-driven rock. Kept LATE so a gently shelving beach or a
        // grassy flank is never repainted grey; only genuinely sheer ground turns to rock.
        mat.SetFloat("_SlopeRockStart", 42f);
        mat.SetFloat("_SlopeRockEnd", 64f);
        mat.SetFloat("_MacroVariation", 0.34f);
        mat.SetFloat("_NormalStrength", 1.0f);
        mat.SetColor("_ShadeColor", new Color(0.60f, 0.70f, 0.86f, 1f));
        mat.SetFloat("_ShadeStrength", 0.44f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.08f);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    private static Texture SakuraTexture(string file)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>($"{SakuraTextureDir}/{file}");
    }

    /// <summary>
    /// Shiosai's OWN texture set (tools/blender/build_shiosai_textures.py). Sakura Pass's maps
    /// are shared and must never be mutated, so anything that needs a coastal look - warm lush
    /// grass instead of an alpine meadow, cracked coastal highway asphalt instead of a dry
    /// mountain road - gets its own map here. Falls back to null so a missing map degrades to
    /// the material's flat base colour rather than to a null-reference mid-build.
    /// </summary>
    private static Texture CoastTexture(string file)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoastTextureDir}/{file}");
        if (t == null)
            Debug.LogWarning($"[shiosai] missing coast texture '{file}' - run: " +
                             "blender -b -P tools/blender/build_shiosai_textures.py");
        return t;
    }

    /// <summary>
    /// The green headland was the last big band still painted with the plain single-tap
    /// SakuraCel material, and in the GAMEPLAY frame that is what made every hillside read as
    /// flat poster-paint green with a corduroy stripe across it: one albedo tap, no normal, no
    /// slope break-up, stretched over UVs that run in 8 m steps. It gets the same shared
    /// triplanar terrain shader the cliff face already uses - grass where it is flat, rock
    /// where it steepens, all with normals - just tuned to the coast's own lush grass map.
    /// </summary>
    private static Material HeadlandMaterial()
    {
        const string name = "Shiosai_Headland";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, TerrainShaderName);
        // Shiosai's exposed slopes need a cooler, wind-brushed coastal groundcover instead of
        // the previous uniform bright-green field.  The original procedural map remains in the
        // project as a safe fallback; this only changes the coast material binding.
        mat.SetTexture("_GrassTex", CoastTexture("Shiosai_HokkaidoGround_Albedo.png"));
        mat.SetTexture("_GrassNormal", CoastTexture("Shiosai_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", CoastTexture("Shiosai_Grass_Rough.png"));
        mat.SetTexture("_RockTex", CoastTexture("Shiosai_Rock_Albedo.png"));
        mat.SetTexture("_RockNormal", CoastTexture("Shiosai_Rock_Normal.png"));
        mat.SetTexture("_RockRough", CoastTexture("Shiosai_Rock_Rough.png"));
        mat.SetTexture("_ScreeTex", CoastTexture("Shiosai_Scree_Albedo.png"));
        mat.SetTexture("_ScreeNormal", CoastTexture("Shiosai_Scree_Normal.png"));
        mat.SetTexture("_ScreeRough", CoastTexture("Shiosai_Scree_Rough.png"));
        // The map is already authored in the coast's green, so the tint is near-white - tinting
        // it again would push the headland back toward the flat poster colour it just left.
        mat.SetColor("_GrassColor", new Color(0.90f, 0.96f, 0.84f, 1f));
        mat.SetColor("_RockColor", new Color(0.40f, 0.38f, 0.35f, 1f));
        mat.SetColor("_ScreeColor", new Color(0.40f, 0.38f, 0.35f, 1f));
        // PROVISIONAL tiling: 5 m per grass tile keeps blade detail legible from the saddle
        // (the camera is ~3 m up) without quilting on the long hillside runs.
        mat.SetFloat("_GrassScale", 5.0f);
        mat.SetFloat("_RockScale", 12.0f);
        mat.SetFloat("_ScreeScale", 4.0f);
        // Rock only on genuinely steep ground - the headland is meant to be green.
        mat.SetFloat("_SlopeRockStart", 38f);
        mat.SetFloat("_SlopeRockEnd", 60f);
        mat.SetFloat("_MacroVariation", 0.42f);
        // DE-TILING, opening visual overhaul. The hillside grass quilted visibly on the open
        // slope at the start line. All PROVISIONAL:
        //  - two-scale blend at a 2.71x ratio under a 46 m patch mask kills the constant repeat;
        //  - a 34 m meso break varies value and hue where the eye looks for the repeat;
        //  - detail fades toward the macro colour past 80 m, which is what removes the corduroy
        //    moire on the far slope in the reverse shot.
        mat.SetFloat("_DetileAmount", 0.72f);
        mat.SetFloat("_DetileRatio", 2.71f);
        mat.SetFloat("_DetileBlendM", 46f);
        mat.SetFloat("_MesoVariation", 0.55f);
        mat.SetFloat("_MesoScaleM", 34f);
        mat.SetFloat("_MesoHue", 0.42f);
        mat.SetFloat("_DetailFadeStart", 80f);
        mat.SetFloat("_DetailFadeRange", 300f);
        mat.SetFloat("_DetailFadeAmt", 0.55f);
        mat.SetFloat("_NormalStrength", 1.15f);
        mat.SetColor("_ShadeColor", new Color(0.60f, 0.70f, 0.86f, 1f));
        mat.SetFloat("_ShadeStrength", 0.48f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.08f);
        mat.SetColor("_RimColor", new Color(0.88f, 0.95f, 1f, 1f));
        mat.SetFloat("_RimStrength", 0.30f);
        mat.SetFloat("_SpecStrength", 0.08f);
        mat.SetVector("_HeightRange", new Vector4(1000f, 2000f, 0f, 0f));  // no snow on the coast
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Shiosai's OWN skybox: bright coastal daylight. Same MapleRide/SakuraSky shader, an
    /// entirely different parameter set, and its own material asset - so swapping regions can
    /// never contaminate the pass's authored sunset.
    ///
    /// PROVISIONAL art tuning, read off the 20 concept renders: a deep blue zenith, a pale
    /// cyan-white horizon band, small hard sun, high white cumulus.
    /// </summary>
    private static Material BuildCoastSky()
    {
        var sky = LoadOrCreate("Shiosai_Sky", "MapleRide/SakuraSky");
        sky.SetColor("_ZenithColor", new Color(0.12f, 0.38f, 0.80f, 1f));
        sky.SetColor("_MidColor", new Color(0.40f, 0.68f, 0.94f, 1f));
        sky.SetColor("_HorizonColor", new Color(0.80f, 0.92f, 0.98f, 1f));
        sky.SetColor("_GroundColor", new Color(0.30f, 0.45f, 0.52f, 1f));
        // A tighter horizon band than the pass: on the coast the pale band is a thin strip over
        // the sea, not half the sky.
        sky.SetFloat("_HorizonSharp", 4.2f);
        sky.SetFloat("_MidPoint", 0.30f);
        sky.SetColor("_SunColor", new Color(1f, 0.98f, 0.92f, 1f));
        sky.SetFloat("_SunSize", 0.030f);
        sky.SetFloat("_SunSoftness", 0.010f);
        // A midday sun has a small, hard glow. The pass's broad amber lobe is exactly the thing
        // that made the coast read as dusk.
        sky.SetFloat("_SunGlow", 0.55f);
        sky.SetFloat("_SunGlowPower", 40f);
        sky.SetColor("_CloudColor", new Color(1f, 1f, 1f, 1f));
        // M6: at 0.52 strength / 0.30 spread the cumulus never resolved - every diagnostic shot
        // came back as a plain gradient. The shader gates clouds behind saturate(fbm*1.7-0.52),
        // so only a strong lerp weight and a wide band actually put puffs in frame. PROVISIONAL.
        sky.SetFloat("_CloudStrength", 0.95f);
        sky.SetFloat("_CloudScale", 3.1f);
        sky.SetFloat("_CloudHeight", 0.24f);
        sky.SetFloat("_CloudSpread", 0.55f);
        sky.SetFloat("_Exposure", 1.02f);
        EditorUtility.SetDirty(sky);
        return sky;
    }

    // --------------------------------------------------------------- world map art + systems

    /// <summary>
    /// Copies the shipped world-map art into Resources so the runtime overlay can load it in a
    /// player build as well as in the editor. Idempotent: only copies when missing or stale.
    /// </summary>
    private static void ImportWorldMapArt()
    {
        // Moved under reference/images by the 2026-09-15 restructure; the repo-root location is
        // still probed so an older checkpoint restored in place keeps working.
        string src = Path.Combine(MapleRidePaths.Reference("images"), "worldmap.png");
        if (!File.Exists(src)) src = Path.Combine(MapleRidePaths.RepoRoot, "worldmap.png");
        string dstAsset = $"Assets/Resources/{RegionCatalog.MapTextureResource}.png";
        string dst = Path.Combine(Directory.GetParent(Application.dataPath).FullName, dstAsset);

        if (!File.Exists(src))
        {
            Debug.LogWarning($"[shiosai] worldmap.png not found at {src}.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        if (!File.Exists(dst) || File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dst))
        {
            File.Copy(src, dst, true);
            AssetDatabase.ImportAsset(dstAsset, ImportAssetOptions.ForceSynchronousImport);
        }

        var importer = AssetImporter.GetAtPath(dstAsset) as TextureImporter;
        if (importer != null)
        {
            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            { importer.textureType = TextureImporterType.Sprite; dirty = true; }
            if (importer.maxTextureSize < 2048) { importer.maxTextureSize = 2048; dirty = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (dirty)
            {
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(dstAsset, ImportAssetOptions.ForceSynchronousImport);
            }
        }
        Debug.Log($"[shiosai] world-map art ready at Resources/{RegionCatalog.MapTextureResource}.");
    }

    /// <summary>
    /// Stages the fast-travel systems: a <see cref="RegionDirector"/> on the ride host, the
    /// EventSystem the overlay's buttons need, and the HUD wiring. Idempotent.
    /// </summary>
    public static void SetupRegionSystems(RouteGraph graph)
    {
        var host = GameObject.Find(RideBootstrap.RootName);
        if (host == null)
        {
            Debug.LogWarning("[shiosai] no ride host in the scene - run the Sakura environment " +
                             "pass first; world map not staged.");
            return;
        }

        var boot = host.GetComponent<RideBootstrap>();
        var session = host.GetComponent<RideSession>();
        var hud = host.GetComponent<RideHud>();
        var regions = host.GetComponent<RegionDirector>() ?? host.AddComponent<RegionDirector>();

        regions.boot = boot;
        regions.session = session;
        regions.follower = boot != null ? boot.follower : null;
        // Per-region sky/lighting. Pushed onto the serialized component (a code default cannot
        // reference an asset), so travelling to the coast swaps the sunset skybox for a bright
        // coastal daylight one instead of leaving Shiosai reading as "Sakura at dusk".
        regions.sakuraSky = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Environment/SakuraPass/Materials/SakuraPass_Sky.mat");
        regions.shiosaiSky = BuildCoastSky();
        regions.keyLight = null;
        regions.fillLight = null;
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l == null || l.type != LightType.Directional) continue;
            if (l.name == "Sakura Sunset Key") regions.keyLight = l;
            if (l.name == "Sakura Valley Fill") regions.fillLight = l;
        }
        regions.streamer = UnityEngine.Object.FindFirstObjectByType<RouteDressingStreamer>(
            FindObjectsInactive.Include);
        regions.currentRegionId = RegionCatalog.SakuraPass;
        if (boot != null) boot.regions = regions;

        var worldMap = host.GetComponent<WorldMapHud>() ?? host.AddComponent<WorldMapHud>();
        worldMap.session = session;
        worldMap.regions = regions;
        worldMap.openKey = KeyCode.Tab;
        worldMap.closeKey = KeyCode.Escape;
        worldMap.isOpen = false;
        if (hud != null) hud.worldMap = worldMap;

        EnsureEventSystem();

        // Start every session in Sakura Pass, with only that region drawn.
        if (session != null)
        {
            session.Graph = null;
            session.EnsureCourse();
        }
        regions.SyncFromSession();

        int courses = graph != null ? graph.courses.Length : 0;
        Debug.Log($"[shiosai] region systems staged: {courses} courses across " +
                  $"{RegionCatalog.UnlockedIds().Length} built regions; current " +
                  $"'{regions.currentRegionId}'.");
    }

    private static void EnsureEventSystem()
    {
        var existing = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 1; i < existing.Length; i++)
        {
            Debug.Log($"[shiosai] pruning duplicate EventSystem '{existing[i].name}'.");
            UnityEngine.Object.DestroyImmediate(existing[i].gameObject);
        }
        if (existing.Length > 0) return;

        var go = new GameObject("MapleRide EventSystem",
                                typeof(UnityEngine.EventSystems.EventSystem),
                                typeof(UnityEngine.EventSystems.StandaloneInputModule));
        Debug.Log($"[shiosai] created '{go.name}' - the World Map's buttons need it.");
    }

    private static List<GameObject> FindRootsByExactName(string exactName)
    {
        var hits = new List<GameObject>();
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == exactName) hits.Add(go);
        return hits;
    }
}










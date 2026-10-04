using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Scenic environment pass for Sakura Pass.
///
/// The prototype rendered as flat vector art floating in a blue void: every material was
/// Unlit/Color so the key light affected nothing, the ground was hidden and never replaced,
/// and the skybox was cleared to a solid colour with a hard rectangular horizon.
///
/// This pass stages the authored Blender landscape (tools/blender/build_all.py) instead of
/// generating anything procedurally in Unity - no primitives anywhere - then dresses the
/// route with scattered flora and props, and applies cel/foliage/terrain/water/sky materials
/// so the sunset key light finally does some work.
///
/// Everything exported by the Blender pipeline is authored through <c>sakura_lib.u2b()</c>,
/// which means each GLB already sits at its intended world position: staging is a plain
/// instantiate at identity transform.
/// </summary>
public static class SakuraPassEnvironment
{
    private const string ShaderDir = "Assets/Environment/SakuraPass/Shaders";
    private const string MaterialDir = "Assets/Environment/SakuraPass/Materials";
    private const string TextureDir = "Assets/Environment/SakuraPass/Textures";
    private const string AssetDir = "Assets/Environment/SakuraPass/BlenderAssets";
    private const string RoutePath = "Assets/Environment/SakuraPass/SakuraRoute.json";
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    private const string CelShaderName = "MapleRide/SakuraCel";
    private const string FoliageShaderName = "MapleRide/SakuraFoliage";
    private const string SkyShaderName = "MapleRide/SakuraSky";
    private const string WaterShaderName = "MapleRide/SakuraWater";
    private const string TerrainShaderName = "MapleRide/SakuraTerrain";

    private const string EnvironmentRootName = "Sakura Pass Environment";

    // ---------------------------------------------------- Part B section 24: lighting pass
    //
    // Every value here is PROVISIONAL (illustrative tuning, not confirmed direction). They exist
    // as named constants so the benchmark can be re-graded without hunting magic literals.
    //
    // WHY SHADOWS CAME BACK ON
    // The key light previously ran with LightShadows.None. That is the single reason the road
    // read as "uniformly illuminated" - section 24's headline complaint - and why section 39
    // criterion 3 ("tree shadows break up the road surface naturally") could never pass. The
    // original reason for disabling them was visible rectangular cascade patches across the
    // carriageway; that is a cascade-fit problem, not a reason to ship an unshadowed world, and
    // it is addressed by ShadowDistanceM + StableFit + 4 cascades below rather than by giving up.

    /// <summary>Sun elevation, degrees. MEASURED, not guessed: an AttenDebug sweep of the raw
    /// shadow term over the benchmark road (see ShadowProbe.RunAtten) found a hard canopy cutoff
    /// at ~48 deg. At 30 the carriageway was 0% lit - the whole corridor sat at the shadow floor,
    /// which is why it looked flat and why no amount of light tuning produced dapple. Measured
    /// dapple (spread of the shadow term) peaks 55-60 and collapses again by 70 as the road goes
    /// uniformly lit. 58 keeps a bright, readable road with strong canopy breakup. PROVISIONAL -
    /// re-measure if the canopy density changes (section 27 will change it).</summary>
    public const float SunElevationDeg = 58f;
    /// <summary>Sun yaw, degrees. UNCHANGED - it is tuned to model the hero volcano's flank.</summary>
    public const float SunYawDeg = 62f;
    /// <summary>Shadows should tint the surface, not punch holes in it (cel shading, no GI).
    /// Raised from 0.62: at 0.62 the shadow removed only ~38% of a sun term that was itself a
    /// minority of the road's light budget, so the dapple was below the noise floor of the
    /// asphalt texture. PROVISIONAL (section 18-style tunable).</summary>
    public const float SunShadowStrength = 0.78f;

    // --- section 24: THE ambient/key balance. This is the fix for "the road has no shadows".
    //
    // Diagnosis, proven by render (good_graphics/benchmark/shadow_road_ambblack.png): forcing
    // ambient to flat black turned the ENTIRE frame dark, which means the road was being lit
    // roughly 60% by ambient and only ~40% by the sun. A shadow can only subtract the sun term,
    // so at that ratio it was never going to read.
    //
    // Worse, the trap that hid this for six diagnostic runs: ambientMode is Trilight, and
    // Trilight/Gradient ambient IGNORES RenderSettings.ambientIntensity entirely - only the three
    // colours matter. Every attempt to "crush ambient" by lowering ambientIntensity was a silent
    // no-op, which is why a 4-config lighting sweep rendered four pixel-identical images.
    // => scale the COLOURS, never the intensity, while the mode is Trilight.
    /// <summary>Multiplier applied to the three Trilight ambient colours. REBALANCED from 0.55:
    /// that value was chosen while the road was 100% shadowed, so it had to be aggressive to
    /// produce any contrast at all. Now that ~60% of the carriageway is genuinely sunlit, 0.55
    /// crushed the shaded half; 0.72 keeps the shade readable. PROVISIONAL.</summary>
    public const float AmbientScale = 0.72f;
    /// <summary>Key intensity. Raised back to 1.40 now that SakuraCel owns its ambient and
    /// occludes it in shadow (see _ShadowAmbient): the sunlit road needs the extra key to open
    /// the gap against the shade floor, which is what makes canopy dapple read. PROVISIONAL.</summary>
    public const float KeyIntensity = 1.40f;

    // --- Section 25: atmosphere and depth. ALL PROVISIONAL look values. -------------------
    /// <summary>Aerial perspective stays off the foreground: the carriageway the rider is
    /// actually on, the guardrail and the near trunks must keep full saturation.</summary>
    public const float AerialStartM = 110f;
    /// <summary>Distance over which the aerial cues reach full strength. Tuned so the mid-ground
    /// sakura wall is partly desaturated and the ridge rings are fully atmospheric.</summary>
    public const float AerialRangeM = 950f;
    /// <summary>Saturation removed at full distance - the direct answer to section 25's "distant
    /// objects should not compete with foreground detail".</summary>
    public const float AerialDesaturation = 0.50f;
    /// <summary>Local contrast flattened toward mid-grey at full distance.</summary>
    public const float AerialFlatten = 0.40f;
    /// <summary>How far the far field drifts toward the horizon tint on top of scene fog.</summary>
    public const float AerialTintAmount = 0.22f;
    /// <summary>Valley mist: world Y of full mist. The route runs 24-37 m, so the mist pools
    /// below the carriageway and reads as a filled valley rather than fog on the road.</summary>
    public const float MistBaseY = 6f;
    /// <summary>World Y at which mist has cleared.</summary>
    public const float MistTopY = 34f;
    public const float MistStrength = 0.45f;
    /// <summary>No mist inside this radius. Guards against fogging the road under the wheels.</summary>
    public const float MistStartM = 140f;
    public static readonly Color MistColor = new Color(0.95f, 0.86f, 0.84f, 1f);

    // --- Section 26: road as a hero asset. ALL PROVISIONAL look values. -------------------
    /// <summary>MUST equal ASPHALT_TILE in tools/blender/build_road.py. The carriageway is swept
    /// with u = (metres across the cross-section) * ASPHALT_TILE, so this is how the shader
    /// recovers cross-road position for the wheel-track and shoulder masks. Change one without
    /// the other and every mask slides sideways off the lanes.</summary>
    public const float RoadUvPerMetre = 0.22f;
    /// <summary>Measured u-metre of the carriageway centreline.
    ///
    /// THIS IS NOT _RoadWidthM * 0.5. build_road.py derives u from the cumulative arc length of
    /// the full swept cross-section - outer skirt, shoulder, lanes, shoulder, outer skirt - so
    /// u = 0 lands on the outer skirt, 2.28 m outboard of the carriageway edge. Assuming 3.5
    /// here shifted every section-26 cross-road mask sideways by 2.28 m and smeared the
    /// shoulder tint across a whole lane. Recompute from the profile if the cross-section in
    /// build_road.py changes.</summary>
    public const float RoadUvCentreM = 5.7824f;
    /// <summary>Carriageway width; 2 * ROAD_HALF_WIDTH (3.5 m) in build_road.py.</summary>
    public const float RoadWidthM = 7.0f;
    /// <summary>Detail-normal tile rate. uv.x spans 0..1.54 across the 7 m carriageway, so 34
    /// gives roughly 7 repeats per metre - aggregate grain, not a visible pattern. Deliberately
    /// unrelated to the albedo rate so the two never line up; that beat kills the visible repeat.</summary>
    public const float RoadDetailTile = 34.0f;
    public const float RoadDetailStrength = 0.35f;
    /// <summary>Macro colour variation: the albedo re-read at a large non-harmonic scale. Kept
    /// modest - pushed higher it washes out the aggregate grain it is meant to sit under.</summary>
    public const float RoadMacroScale = 0.031f;
    public const float RoadMacroAmount = 0.22f;
    /// <summary>Resurfacing patches: a second, larger read thresholded into soft slabs.</summary>
    public const float RoadPatchScale = 0.013f;
    public const float RoadPatchAmount = 0.22f;
    public const float RoadRoughVariation = 0.45f;
    /// <summary>Wheel tracks sit +/- this from each lane centre (lane centres at W/4).</summary>
    public const float RoadTrackOffsetM = 0.80f;
    // QA fix #3: 0.55 m / 0.16 was barely visible at gameplay camera distance. Widened and
    // darkened so the worn-wheel-track cue reads clearly (macro-noise modulation in the shader
    // still breaks it up into a worn look rather than two perfect stripes).
    public const float RoadTrackWidthM = 0.70f;
    public const float RoadTrackDarken = 0.30f;
    public const float RoadShoulderWidthM = 0.75f;
    public const float RoadShoulderAmount = 0.55f;
    /// <summary>Shoulder dirt tint. RETUNED from (0.62, 0.50, 0.50): that pink-grey was chosen
    /// against the old pale speckled asphalt and, applied to the outer 0.75 m of BOTH lanes, it
    /// was a major contributor to the carriageway reading as a purple strip. Neutral road-dust
    /// grey-brown. PROVISIONAL.</summary>
    public static readonly Color RoadShoulderColor = new Color(0.46f, 0.44f, 0.41f, 1f);
    // Section 28 transitional shoulder, measured OUTBOARD of the carriageway edge. ALL PROVISIONAL.
    /// <summary>Metres of verge ramp beyond the road edge. The swept shoulder is 0.55 m and the
    /// skirt starts burying itself after that, so ~1.4 m covers everything still visible.</summary>
    public const float RoadVergeWidthM = 1.45f;
    public const float RoadVergeAmount = 0.80f;
    /// <summary>How hard the high-frequency noise read chews the verge boundary. 0 would put
    /// the perfectly straight line back.</summary>
    public const float RoadVergeBreakup = 0.42f;
    public static readonly Color RoadVergeGravelColor = new Color(0.55f, 0.53f, 0.49f, 1f);
    public static readonly Color RoadVergeSoilColor = new Color(0.34f, 0.29f, 0.24f, 1f);
    /// <summary>Ceiling on painted-marking tint. Section 26: "no glowing white appearance".</summary>
    public const float MarkingPeak = 0.82f;

    // ---------------------------------------------------------------- section 27: vegetation
    // The doc's target composition is 40-50% sakura / 30-40% green evergreen-deciduous /
    // 10-20% shrubs-grasses-flowers-rocks, and explicitly calls out that "pink becomes more
    // powerful when contrasted against green and dark trunks".
    //
    // Why the route failed that test: conifers are banned from the verge band and damped in
    // the middle band (a 12 m opaque dark cone at 6 m owns a third of the rider's frame), so
    // the only green in the mix lived in the far band where the camera barely resolves it.
    // Every tree the player actually sees near or mid was therefore pink - the "uniform pink
    // wall" in the baseline screenshots.
    //
    // The fix is a green *broadleaf* built on the sakura growth structure (same translucent
    // card canopy, same crown scale) rather than more conifers, so green can sit right on the
    // verge without blocking the road. These shares are the share of NON-conifer trees that
    // are drawn green, per depth band. ALL PROVISIONAL composition tuning.
    public const float VergeGreenShare = 0.42f;   // band 0, on the verge
    public const float MidGreenShare = 0.34f;   // band 1, the middle wall
    public const float BackGreenShare = 0.22f;   // band 2, behind the conifer treeline
    /// <summary>Broadleaf_C is ~31 k tris against ~14 k for A. Kept out of the near band where
    /// the scatter is densest. PROVISIONAL.</summary>
    public const float BroadleafHeroMaxBand = 0.5f;
    /// <summary>Section 27 "variety": per-instance crown tints, picked from a small shared
    /// palette rather than a MaterialPropertyBlock so instancing/batching still applies.
    /// "Do not randomize so aggressively that the art direction becomes noisy" - hence a
    /// narrow spread around one authored green. PROVISIONAL.</summary>
    public static readonly Color[] BroadleafTints =
    {
        new Color(0.62f, 0.82f, 0.52f, 1f),
        new Color(0.74f, 0.86f, 0.56f, 1f),
        new Color(0.55f, 0.74f, 0.50f, 1f),
        new Color(0.80f, 0.84f, 0.48f, 1f),
    };
    /// <summary>Blossom crowns get the same treatment so the remaining sakura stop reading as
    /// clones of one another. Narrow spread - these must still all read as one species.</summary>
    public static readonly Color[] BlossomTints =
    {
        new Color(0.94f, 0.66f, 0.74f, 1f),
        new Color(0.97f, 0.74f, 0.79f, 1f),
        new Color(0.90f, 0.58f, 0.69f, 1f),
        new Color(0.96f, 0.70f, 0.66f, 1f),
    };

    // ------------------------------------------------------ section 28: groundcover / verge
    // The section-28 rule is that the road edge must not read as
    //     asphalt ends -> perfect grass texture begins
    // which is exactly what the route did. The existing cover pass places grass and ferns at
    // 4.4-11.4 m from the centreline and then rejects anything inside CorridorHalf + 1.4 m,
    // so in practice nothing at all landed in the first ~1.5 m off the shoulder - the strip
    // the rider's camera looks straight down. The result was a clean asphalt/shoulder line
    // against unbroken bright grass.
    //
    // This band is dressed separately, with its own (much smaller) clearance, its own asset
    // vocabulary - moss and bare-soil patches, loose stones, small flowers, low shrubs - and
    // a bias that puts the flattest, most terrain-like pieces nearest the asphalt. Section 28
    // says near-road density matters more than distant density, so this is deliberately the
    // densest scatter on the route. ALL PROVISIONAL tuning.
    /// <summary>Metres from the centreline the verge band starts. Carriageway half-width is
    /// 3.5 m and the shoulder runs to 4.05 m, so this sits just outboard of the chip seal.</summary>
    public const float EdgeBandInner = 4.5f;
    /// <summary>Metres from the centreline the verge band ends - it hands over to the existing
    /// cover pass rather than duplicating it.</summary>
    public const float EdgeBandOuter = 7.2f;
    /// <summary>Road clearance for verge dressing. RoadClearProp (1.4 m) is sized for rock
    /// clusters; moss and flowers are supposed to crowd the shoulder, so they get their own.</summary>
    public const float EdgeClearance = 0.30f;
    /// <summary>Placement attempts per route sample per side.</summary>
    public const int EdgeSlotsPerSample = 5;
    /// <summary>Probability an attempt is taken at all.</summary>
    public const float EdgeChance = 0.62f;
    /// <summary>Steepest ground verge dressing will sit on.</summary>
    public const float EdgeMaxSlope = 48f;
    /// <summary>Fraction of the band width treated as "against the asphalt" - inside this the
    /// scatter picks flat, terrain-like pieces (moss, soil, gravel) so the transition reads as
    /// material rather than as props standing on grass.</summary>
    public const float EdgeInnerFraction = 0.45f;
    /// <summary>Flower palette for the verge. Small, and deliberately close to the blossom
    /// palette so they read as fallen/roadside sakura kin rather than a foreign colour.</summary>
    public static readonly Color[] EdgeFlowerTints =
    {
        new Color(0.96f, 0.78f, 0.86f, 1f),
        new Color(0.98f, 0.93f, 0.68f, 1f),
        new Color(0.78f, 0.70f, 0.90f, 1f),
    };

    /// <summary>Ceiling for cel material _Color tints. SakuraPass_Asphalt shipped at 1.55, which
    /// pushed the lit road above the clip ceiling so lit and shadowed pixels both resolved to
    /// white. Cel shading has no energy conservation, so this has to be capped by hand. PROVISIONAL.</summary>
    public const float MaxCelTint = 1.0f;
    /// <summary>Metres. Deliberately short: the shadow budget is spent on the near road and the
    /// canopy over it, which is all the player ever sees at speed. A long distance is exactly
    /// what produced the rectangular patches that got shadows switched off in the first place.</summary>
    public const float ShadowDistanceM = 150f;

    // ---------------------------------------------------- Part B section 25: atmosphere/depth
    /// <summary>Exponential-squared fog density. Section 25 wants readable near/mid/far layers;
    /// this is re-tuned against the ridge rings whenever distant geometry changes.</summary>
    public const float FogDensity = 0.00052f;

    // ------------------------------------------------------------------ menu

    [MenuItem("MapleRide/Environment/Build Scenic Environment Pass", priority = 20)]
    public static void BuildEnvironmentPass()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Apply();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("Sakura Pass scenic environment pass complete.");
    }

    /// <summary>Builds the whole environment into the active scene. Safe to run repeatedly.</summary>
    /// <remarks>
    /// In batch mode the active scene is an empty untitled one, so this opens the pass scene first
    /// and saves it at the end. Without that, <c>-executeMethod SakuraPassEnvironment.Apply</c>
    /// builds the entire environment into a scene that is thrown away on quit - it exits 0 and
    /// logs completely plausible prop counts while the renders keep showing the previous build.
    /// That cost an afternoon of tuning against stale images; do not remove this.
    /// </remarks>
    public static void Apply()
    {
        bool headless = Application.isBatchMode;
        // The caller may have just created a brand-new scene (for example BuildSakuraPass).
        // Reopening the previous SakuraPass file here would discard the newly staged rider and
        // bicycle before the caller can save them. Standalone callers are responsible for opening
        // their target scene first.

        Directory.CreateDirectory(MaterialDir);
        SakuraTextureImportSettings.ApplyAll();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        VerifyShaders();
        MaterialCache.Clear();
        GroundHost = null;

        var existing = GameObject.Find(EnvironmentRootName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

        var root = new GameObject(EnvironmentRootName).transform;

        RemoveLegacyBackdropPlates();
        HidePlaceholderGeometry();

        var sun = ConfigureLightingAndSky();
        var route = SakuraRoute.Load();
        var segments = SakuraRoute.LoadSegments();

        var ground = StageLandscape(root);
        StageRoad(root);
        StageExpansion(root, route);
        StageExpansionLandmarks(root.Find("Landmarks"), segments);
        ScatterDressing(root, route, ground);
        ScatterExpansionDressing(root, segments, ground);
        ScatterEdgeDressing(root, segments, ground);
        BuildRoadEdgeDecal(root, segments, ground);     // E4 huddle idea 5
        BuildRoadLightProbes(root, segments);           // Agent HQ: lamp probes along the road
        BuildPetalVfx(root, sun);
        BuildWindVolumes(root, segments);
        ConfigureCamera();
        UpgradeSceneMaterialsToCel();
        SakuraPassLiving.StageLiving(root);

        // The collider only existed so the scatter pass could find the ground; the riding
        // surface has its own collision and a 72k-triangle mesh collider is pure overhead.
        // Validate seating *before* it goes: a probe run in a later session would find no ground
        // collider at all, silently skip every prop and report a clean bill of health.
        if (ground != null) ValidateSeating(root);
        if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
        GroundHost = null;

        // Ride foundation: bake the network into a runtime asset, stage the systems, then chunk
        // the dressing so the expanded world streams instead of being drawn all at once.
        var graph = RouteGraphBaker.BakeAsset();
        SetupRideSystems(graph);
        var player = GameObject.Find("Kuro on Sakura Pass");
        ChunkDressing(root, segments, player != null ? player.transform : null);

        if (headless)
        {
            var active = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(active);
            EditorSceneManager.SaveScene(active);
            Debug.Log($"Sakura Pass: saved '{active.path}'.");
        }
    }

    /// <summary>
    /// Logs anything whose lowest point sits noticeably above the terrain under it. Runs inside
    /// the build pass because the ground colliders are temporary.
    /// </summary>
    private static void ValidateSeating(Transform root)
    {
        int floating = 0, checkedCount = 0;
        var dressing = root.Find("Route Dressing");
        if (dressing == null) return;

        foreach (Transform group in dressing)
        foreach (Transform prop in group)
        {
            // One combined bounds per prop, not per renderer: a guardrail reflector or a tree
            // canopy is *supposed* to be in the air, and only the prop as a whole is seated.
            var rends = prop.GetComponentsInChildren<MeshRenderer>(true);
            if (rends.Length == 0) continue;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            var g = SampleGround(new Vector3(b.center.x, 0f, b.center.z));
            if (!g.Hit) continue;
            checkedCount++;
            float gap = b.min.y - g.Point.y;
            if (gap > 0.9f)
            {
                floating++;
                if (floating <= 12)
                    Debug.LogWarning($"[seating] '{prop.name}' floats {gap:0.00} m at {b.center} " +
                                     $"(ground y {g.Point.y:0.0}).");
            }
        }
        Debug.Log($"[seating] {floating} of {checkedCount} scattered props sit more than 0.9 m " +
                  "clear of the ground.");
    }

    [MenuItem("MapleRide/Environment/Upgrade Scene Materials To Cel", priority = 21)]
    public static void UpgradeMaterialsMenu()
    {
        UpgradeSceneMaterialsToCel();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    private static void VerifyShaders()
    {
        foreach (var name in new[] { CelShaderName, FoliageShaderName, SkyShaderName,
                                     WaterShaderName, TerrainShaderName })
        {
            if (MapleRideShaderNames.Find(name) == null)
                throw new FileNotFoundException(
                    $"Shader '{name}' was not found. Expected the .shader files under {ShaderDir} to be imported.");
        }
    }

    // ------------------------------------------------------------------ route

    [Serializable]
    private class RouteSampleDto
    {
        public float[] p, t, s, u;
        public float bank, d;
    }

    [Serializable]
    private class RouteDto
    {
        public float roadHalfWidth = 3.5f;
        public float shoulderWidth = 0.55f;
        public float climbLength = 0f;
        public RouteSampleDto[] samples;
        public RouteSegmentDto[] segments;
    }

    [Serializable]
    private class RouteSegmentDto
    {
        public string id;
        public string name;
        public RouteSampleDto[] samples;
    }

    /// <summary>
    /// The banked centreline frames published by tools/blender/sakura_route.py.
    ///
    /// Unity consumes the baked samples rather than re-deriving the Catmull-Rom resample and
    /// the curvature-driven banking: if the two implementations ever drifted, scattered props
    /// would sit off the carriageway the road mesh was actually swept along.
    /// </summary>
    public class SakuraRoute
    {
        public float HalfWidth = 3.5f;
        public float ShoulderWidth = 0.55f;
        public Vector3[] Position = Array.Empty<Vector3>();
        public Vector3[] Tangent = Array.Empty<Vector3>();
        public Vector3[] Side = Array.Empty<Vector3>();     // rider's right - the valley side
        public Vector3[] Up = Array.Empty<Vector3>();
        public float[] Distance = Array.Empty<float>();

        public int Count => Position.Length;
        public float Length => Distance.Length == 0 ? 0f : Distance[Distance.Length - 1];

        /// <summary>
        /// Arc length at the summit - the boundary between the original climb and the descent
        /// that was added on the far side of the pass.
        ///
        /// Everything authored before the descent existed (dressing sections, the hillside
        /// lettering, the diagnostic stops) was expressed as a fraction of route length. The
        /// route then more than doubled, so those fractions have to be resolved against *this*
        /// or every one of them slides hundreds of metres off the feature it was placed for.
        /// </summary>
        public float ClimbLength = 530.7f;

        /// <summary>Sample index at or past arc length <paramref name="metres"/>.</summary>
        public int IndexAt(float metres)
        {
            if (Count == 0) return 0;
            for (int i = 0; i < Count; i++)
                if (Distance[i] >= metres) return i;
            return Count - 1;
        }

        /// <summary>Sample index at a fraction of the climb (0 = start, 1 = summit).</summary>
        public int IndexAtClimbFraction(float f) => IndexAt(f * ClimbLength);

        public static SakuraRoute Load()
        {
            var dto = LoadDto();
            return FromSamples(dto, dto.samples, dto.climbLength, "pass", "Sakura Pass");
        }

        /// <summary>
        /// Every drivable centreline in the network, keyed by segment id.
        ///
        /// The staged expansion added the Kawabe lakeshore return, the Aozora spur and the Maple
        /// City road. They share the pass's cross-section and dressing vocabulary, but each needs
        /// its own carve, its own scatter and its own place in the corridor test, so the staging
        /// pass works from this rather than from the pass alone.
        /// </summary>
        public static Dictionary<string, SakuraRoute> LoadSegments()
        {
            var dto = LoadDto();
            var map = new Dictionary<string, SakuraRoute>();
            if (dto.segments != null)
            {
                foreach (var s in dto.segments)
                {
                    if (s?.samples == null || s.samples.Length < 2) continue;
                    map[s.id] = FromSamples(dto, s.samples, dto.climbLength, s.id, s.name);
                }
            }
            if (!map.ContainsKey("pass")) map["pass"] = Load();
            return map;
        }

        public string Id = "pass";
        public string DisplayName = "Sakura Pass";

        private static RouteDto LoadDto()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(RoutePath);
            if (text == null)
                throw new FileNotFoundException(
                    $"{RoutePath} is missing. Run: blender -b -P tools/blender/build_all.py -- route");

            var dto = JsonUtility.FromJson<RouteDto>(text.text);
            if (dto?.samples == null || dto.samples.Length < 2)
                throw new InvalidDataException(
                    $"{RoutePath} has no 'samples' array. Regenerate it with build_all.py -- route");
            return dto;
        }

        private static SakuraRoute FromSamples(RouteDto dto, RouteSampleDto[] samples,
                                               float climbLength, string id, string name)
        {
            int n = samples.Length;
            var r = new SakuraRoute
            {
                Id = id,
                DisplayName = string.IsNullOrEmpty(name) ? id : name,
                HalfWidth = dto.roadHalfWidth,
                ShoulderWidth = dto.shoulderWidth,
                Position = new Vector3[n],
                Tangent = new Vector3[n],
                Side = new Vector3[n],
                Up = new Vector3[n],
                Distance = new float[n],
            };
            if (climbLength > 1f) r.ClimbLength = climbLength;
            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                r.Position[i] = V(s.p);
                r.Tangent[i] = V(s.t).normalized;
                r.Side[i] = V(s.s).normalized;
                r.Up[i] = V(s.u).normalized;
                r.Distance[i] = s.d;
            }
            return r;
        }

        private static Vector3 V(float[] a)
        {
            return a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);
        }
    }

    // ------------------------------------------------------------- materials

    private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

    private static Material LoadOrCreate(string name, string shaderName)
    {
        string path = $"{MaterialDir}/{Sanitize(name)}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        // Resolve through MapleRideShaderNames, never Shader.Find directly: this method
        // re-assigns .shader on EVERY region build, so a hard-coded Built-in name silently
        // reverted the HDRP conversion and the entire region drew with the magenta error
        // shader. ShiosaiCoastEnvironment.LoadOrCreate already routes through the resolver.
        var shader = MapleRideShaderNames.Find(shaderName);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.name = name;
        // 4,150 of the scene's 4,567 renderers are scattered flora sharing a handful of meshes and
        // materials. All four Sakura shaders are surface shaders, which support GPU instancing by
        // default, so this collapses thousands of draw calls into a few instanced batches with no
        // visual difference whatsoever. Triangle load is unaffected - see the perf notes.
        mat.enableInstancing = true;
        return mat;
    }

    /// <summary>Creates (and caches on disk) a cel-lit material so the sunset key light actually shades it.</summary>
    public static Material CelMaterial(string name, Color albedo, float gloss = 0.2f, float spec = 0.18f,
                                       float rim = 0.7f, Color? shade = null, Texture texture = null)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, CelShaderName);
        mat.SetColor("_Color", albedo);
        // The default shade tone is what every unlit face in the scene becomes. At (0.40, 0.46,
        // 0.70) that was a saturated blue-violet, and with _ShadeStrength at 0.74 it dominated:
        // grass, asphalt, bark and stone all went purple the moment they turned away from the
        // sun. A light warm mauve at 0.55 keeps the form reading without recolouring the world.
        mat.SetColor("_ShadeColor", shade ?? new Color(0.66f, 0.62f, 0.76f, 1f));
        mat.SetFloat("_ShadeStrength", 0.55f);
        mat.SetFloat("_RampSteps", 3f);
        mat.SetFloat("_RampSmooth", 0.06f);
        mat.SetColor("_RimColor", new Color(1f, 0.74f, 0.55f, 1f));
        mat.SetFloat("_RimStrength", rim);
        mat.SetFloat("_Gloss", gloss);
        mat.SetFloat("_SpecStrength", spec);
        if (texture != null) mat.SetTexture("_MainTex", texture);
        ApplyWeatheringPreset(mat, name);
        ApplyDetailNormal(mat, name);                    // E4 huddle idea 8 / critic bark
        EditorUtility.SetDirty(mat);

        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// Section 30's prop list, mapped to weathering presets by material name. Driving this from
    /// one table rather than from each CelMaterial() call site matters because several of these
    /// materials are created from three or four different places in this file (the stone lantern
    /// material alone has four), and a per-call-site opt-in would inevitably miss one and leave
    /// a visibly pristine prop standing next to a weathered one.
    /// </summary>
    private static readonly Dictionary<string, Weathering> WeatheringByMaterial =
        new Dictionary<string, Weathering>
        {
            { "SakuraPass_Stone", Weathering.Stone },        // lanterns, shrine steps, walls, roadside rocks
            { "SakuraPass_TunnelFace", Weathering.Stone },
            { "SakuraPass_TunnelLining", Weathering.Stone },
            { "SakuraPass_TunnelPlaque", Weathering.Stone },
            { "SakuraPass_RailWood", Weathering.Timber },
            { "SakuraPass_RailTimber", Weathering.Timber },
            { "SakuraPass_Guardrail", Weathering.Steel },
            { "SakuraPass_Vermilion", Weathering.Painted }, // torii, painted shrine timber
            { "SakuraPass_SignPlinth", Weathering.Signage },
            { "SakuraPass_SignGlyph", Weathering.Signage },
        };

    private static void ApplyWeatheringPreset(Material mat, string name)
    {
        if (WeatheringByMaterial.TryGetValue(name, out var kind)) Weather(mat, kind);
    }

    /// <summary>
    /// Section 30 weathering presets for near-road hard-surface props.
    ///
    /// Section 30 asks for normal detail, roughness variation, edge wear, dirt/moss and subtle
    /// colour variation on stone walls, roadside rocks, wooden guardrails, chevron signs,
    /// lanterns, torii, shrine details and signage - and asks for it as a modular reusable set
    /// rather than dozens of one-off assets. Every one of those props already shares
    /// SakuraCel.shader, so a handful of named presets covers the whole list.
    ///
    /// All values are PROVISIONAL tuning. "amount" is the master blend; 0 leaves a surface
    /// rendering exactly as it did before this pass.
    /// </summary>
    public enum Weathering { Stone, Timber, Steel, Painted, Signage }

    public static Material Weather(Material mat, Weathering kind, float amount = 1f,
                                   float mossBase = 0f)
    {
        if (mat == null || !mat.HasProperty("_WeatherAmount")) return mat;

        float moss = 0f, grime = 0f, wear = 0f, detail = 0f, detailScale = 9f, tint = 0f;
        var mossColor = new Color(0.30f, 0.40f, 0.24f, 1f);
        var wearColor = new Color(1f, 0.97f, 0.90f, 1f);
        float mossHeight = 2.2f;

        switch (kind)
        {
            // Granite kerbs, lantern bodies, shrine steps, retaining walls. Stone is the
            // mossiest thing on a damp Japanese pass, and it sun-bleaches on its top faces.
            case Weathering.Stone:
                moss = 0.50f; grime = 0.28f; wear = 0.30f; detail = 0.38f;
                detailScale = 7.0f; tint = 0.16f; mossHeight = 2.6f;
                break;
            // Timber guardrail rails and posts. Silvers off badly on the up-facing surfaces,
            // greens up at the base, and every post should differ slightly from its neighbour.
            case Weathering.Timber:
                moss = 0.38f; grime = 0.34f; wear = 0.42f; detail = 0.45f;
                detailScale = 14.0f; tint = 0.26f; mossHeight = 1.1f;
                wearColor = new Color(0.96f, 0.93f, 0.88f, 1f);
                break;
            // Steel W-beam guardrail. Galvanised steel does not host much moss, but it collects
            // road grime on the underside and chalks on the sun-facing top edge.
            case Weathering.Steel:
                moss = 0.10f; grime = 0.45f; wear = 0.28f; detail = 0.22f;
                detailScale = 18.0f; tint = 0.08f; mossHeight = 0.8f;
                break;
            // Vermilion torii and painted shrine timber. Paint fades hard in sun and the
            // ground-contact zone of the uprights goes dark and mossy.
            case Weathering.Painted:
                moss = 0.34f; grime = 0.26f; wear = 0.46f; detail = 0.26f;
                detailScale = 11.0f; tint = 0.14f; mossHeight = 1.6f;
                wearColor = new Color(1f, 0.90f, 0.82f, 1f);
                break;
            // Chevron boards, route signage, plinths. Kept the most restrained of the set -
            // signage has to stay legible, so this is dirt and a little fade, nothing more.
            case Weathering.Signage:
                moss = 0.12f; grime = 0.30f; wear = 0.20f; detail = 0.18f;
                detailScale = 16.0f; tint = 0.06f; mossHeight = 0.9f;
                break;
        }

        mat.SetFloat("_WeatherAmount", Mathf.Clamp01(amount));
        mat.SetColor("_MossColor", mossColor);
        mat.SetFloat("_MossAmount", moss);
        mat.SetFloat("_MossHeight", mossHeight);
        mat.SetFloat("_MossBase", mossBase);
        mat.SetFloat("_GrimeAmount", grime);
        mat.SetColor("_WearColor", wearColor);
        mat.SetFloat("_WearAmount", wear);
        mat.SetFloat("_DetailScale", detailScale);
        mat.SetFloat("_DetailAmount", detail);
        mat.SetFloat("_TintVariation", tint);
        mat.SetFloat("_TintVarScale", 0.045f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material FoliageMaterial(string name, Color albedo, Texture texture = null,
                                            float wind = 0.14f)
    {
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;

        var mat = LoadOrCreate(name, FoliageShaderName);
        mat.SetColor("_Color", albedo);
        mat.SetFloat("_Cutoff", 0.35f);
        mat.SetFloat("_Translucency", 1.0f);
        mat.SetColor("_TransColor", new Color(1f, 0.60f, 0.74f, 1f));
        mat.SetFloat("_WindStrength", wind);
        if (texture != null) mat.SetTexture("_MainTex", texture);
        // QA 2026-09-30: canopy crowns only - edge-on card slivers caught full rim light.
        bool crown = name.Contains("Crown_") || name == "SakuraPass_Blossom";
        if (mat.HasProperty("_EdgeOnFade")) mat.SetFloat("_EdgeOnFade", crown ? CanopyEdgeOnFade : 0f);
        EditorUtility.SetDirty(mat);

        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>
    /// The three-layer splat terrain material. Weights ride in the mesh's second UV channel
    /// (uv1.x = rock, uv1.y = scree) and everything is projected triplanar from world space,
    /// which is what stops the grass smearing into vertical streaks down the cut slopes.
    /// </summary>
    private static Material TerrainMaterial()
    {
        if (MaterialCache.TryGetValue("Terrain", out var cached) && cached != null) return cached;

        var mat = LoadOrCreate("SakuraPass_Terrain", TerrainShaderName);
        mat.SetTexture("_GrassTex", LoadTexture("Sakura_Grass_Albedo.png"));
        mat.SetTexture("_GrassNormal", LoadTexture("Sakura_Grass_Normal.png"));
        mat.SetTexture("_GrassRough", LoadTexture("Sakura_Grass_Rough.png"));
        mat.SetTexture("_RockTex", LoadTexture("Sakura_Rock_Albedo.png"));
        mat.SetTexture("_RockNormal", LoadTexture("Sakura_Rock_Normal.png"));
        mat.SetTexture("_RockRough", LoadTexture("Sakura_Rock_Rough.png"));
        mat.SetTexture("_ScreeTex", LoadTexture("Sakura_Scree_Albedo.png"));
        mat.SetTexture("_ScreeNormal", LoadTexture("Sakura_Scree_Normal.png"));
        mat.SetTexture("_ScreeRough", LoadTexture("Sakura_Scree_Rough.png"));
        // Section 29 layers: forest soil / leaf litter, plus moss and petal accumulation as
        // tint films. All values below are PROVISIONAL tuning.
        if (mat.HasProperty("_SoilTex"))
        {
            mat.SetTexture("_SoilTex", LoadTexture("Sakura_Soil_Albedo.png"));
            mat.SetTexture("_SoilNormal", LoadTexture("Sakura_Soil_Normal.png"));
            mat.SetTexture("_SoilRough", LoadTexture("Sakura_Soil_Rough.png"));
            mat.SetColor("_SoilColor", new Color(0.86f, 0.78f, 0.66f, 1f));
            mat.SetFloat("_SoilScale", 5.0f);
            mat.SetColor("_MossColor", new Color(0.38f, 0.52f, 0.30f, 1f));
            mat.SetFloat("_MossStrength", 0.40f);
            mat.SetFloat("_MossScale", 11.0f);
            mat.SetColor("_PetalColor", new Color(0.95f, 0.78f, 0.84f, 1f));
            mat.SetFloat("_PetalStrength", 0.50f);
            mat.SetFloat("_PetalScale", 3.5f);
        }
        // Brighter, greener grass and warmer stone. These are tints on top of the baked albedo,
        // and the baked set is deliberately desaturated so the tint is the only knob that decides
        // whether the valley reads as an alpine meadow or as damp moorland.
        mat.SetColor("_GrassColor", new Color(0.68f, 0.82f, 0.47f, 1f));
        mat.SetColor("_RockColor", new Color(0.70f, 0.65f, 0.63f, 1f));
        mat.SetColor("_ScreeColor", new Color(0.78f, 0.72f, 0.63f, 1f));
        mat.SetFloat("_GrassScale", 6.0f);
        // The cut faces on the descent are 40 m tall and near-vertical, and at _RockScale 9 with
        // only 0.25 macro variation the triplanar rock tiled into a visible quilt across them.
        // A coarser projection plus stronger macro break-up is what stops it reading as wallpaper.
        mat.SetFloat("_RockScale", 15.0f);
        mat.SetFloat("_ScreeScale", 5.5f);
        mat.SetFloat("_SlopeRockStart", 34f);
        mat.SetFloat("_SlopeRockEnd", 58f);
        mat.SetFloat("_MacroVariation", 0.45f);
        mat.SetFloat("_NormalStrength", 1.1f);
        // The cel lighting is additive on top of ambient, so the rim and spec terms have to
        // stay subtle or every slope facing the sun clips to white.
        mat.SetFloat("_RimStrength", 0.14f);
        mat.SetFloat("_SpecStrength", 0.10f);
        mat.SetFloat("_ShadeStrength", 0.48f);
        // The snow grade starts well above the pass itself; starting it at road height bleached
        // the valley walls the rider actually sees.
        mat.SetVector("_HeightRange", new Vector4(72f, 140f, 0.30f, 0f));
        EditorUtility.SetDirty(mat);

        MaterialCache["Terrain"] = mat;
        return mat;
    }

    private static Material WaterMaterial()
    {
        if (MaterialCache.TryGetValue("Water", out var cached) && cached != null) return cached;

        var mat = LoadOrCreate("SakuraPass_LakeWater", WaterShaderName);
        if (mat.HasProperty("_ShallowColor"))
            mat.SetColor("_ShallowColor", new Color(0.30f, 0.55f, 0.62f, 1f));
        if (mat.HasProperty("_DeepColor"))
            mat.SetColor("_DeepColor", new Color(0.06f, 0.14f, 0.28f, 1f));
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", new Color(0.14f, 0.30f, 0.42f, 1f));
        if (mat.HasProperty("_SpecColor"))
            mat.SetColor("_SpecColor", new Color(1f, 0.82f, 0.64f, 1f));
        if (mat.HasProperty("_SkyTint"))
            mat.SetColor("_SkyTint", new Color(0.66f, 0.58f, 0.68f, 1f));
        if (mat.HasProperty("_FresnelBoost")) mat.SetFloat("_FresnelBoost", 0.85f);
        if (mat.HasProperty("_FresnelPower")) mat.SetFloat("_FresnelPower", 4.5f);
        if (mat.HasProperty("_GlitterBoost")) mat.SetFloat("_GlitterBoost", 1.2f);
        // A 450 m lake needs long, shallow swell: a tight wave scale plus generous foam turned
        // the surface into a regular dotted lattice.
        if (mat.HasProperty("_WaveScale")) mat.SetFloat("_WaveScale", 0.22f);
        if (mat.HasProperty("_WaveStrength")) mat.SetFloat("_WaveStrength", 0.22f);
        if (mat.HasProperty("_FoamAmount")) mat.SetFloat("_FoamAmount", 0.045f);
        // The water now runs out past the ridge rings, so the ripple has to stop being evaluated
        // long before the horizon or it aliases into moire banding. Pushed rather than left to
        // the shader default: this material is already serialised in the project.
        if (mat.HasProperty("_DetailFadeStart")) mat.SetFloat("_DetailFadeStart", 140f);
        if (mat.HasProperty("_DetailFadeEnd")) mat.SetFloat("_DetailFadeEnd", 900f);
        if (mat.HasProperty("_FoamColor")) mat.SetColor("_FoamColor", new Color(0.82f, 0.86f, 0.90f, 1f));
        EditorUtility.SetDirty(mat);

        MaterialCache["Water"] = mat;
        return mat;
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    private static Texture2D LoadTexture(string fileName)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDir}/{fileName}");
    }

    // -------------------------------------------------------- lighting / sky

    private static Light ConfigureLightingAndSky()
    {
        var sky = LoadOrCreate("SakuraPass_Sky", SkyShaderName);
        // A *golden hour* sunset, not a dusk one. The previous gradient ran navy -> violet ->
        // orange, and because the sky is also the ambient probe and the fog colour, that violet
        // was tinting the asphalt, the grass and every shadow in the valley: the whole pass came
        // out reading as a cold purple night scene with an orange strip along the horizon.
        // Warm gold at the horizon, rose through the mid band and a *light* periwinkle zenith
        // keeps the sunset while leaving the world lit and readable.
        sky.SetColor("_ZenithColor", new Color(0.40f, 0.55f, 0.87f, 1f));
        sky.SetColor("_MidColor", new Color(0.94f, 0.56f, 0.63f, 1f));
        sky.SetColor("_HorizonColor", new Color(1f, 0.72f, 0.38f, 1f));
        sky.SetColor("_GroundColor", new Color(0.46f, 0.36f, 0.38f, 1f));
        // Broad, soft bands. At _HorizonSharp 3.4 / _MidPoint 0.32 the warm part of the sunset
        // was a thin strip hugging the skyline that the ridges hid completely, so from the road
        // the sky was just blue-violet. Widening both is what actually puts the sunset on screen.
        sky.SetFloat("_HorizonSharp", 1.7f);
        sky.SetFloat("_MidPoint", 0.46f);
        sky.SetColor("_SunColor", new Color(1f, 0.88f, 0.66f, 1f));
        sky.SetFloat("_SunSize", 0.050f);
        // The glow was broad (2.2) and shallow (pow 9), which put a large bright lobe of sky
        // directly behind the hero volcano. Combined with a near-white snow cap that pushed the
        // summit over the bloom threshold, that lobe was read as a *light source on the
        // mountain* - the hard vertical streak down its face. Tightening the glow keeps the
        // sunset without lighting the subject in front of it.
        sky.SetFloat("_SunGlow", 1.45f);
        sky.SetFloat("_SunGlowPower", 13f);
        sky.SetColor("_CloudColor", new Color(1f, 0.78f, 0.70f, 1f));
        sky.SetFloat("_CloudStrength", 0.58f);
        sky.SetFloat("_CloudScale", 3.2f);
        sky.SetFloat("_CloudHeight", 0.17f);
        sky.SetFloat("_CloudSpread", 0.34f);
        sky.SetFloat("_Exposure", 1.12f);

        // Match the key light by its exact name and prune duplicates.
        //
        // This used to look for any directional light whose name merely *contained* "Sakura",
        // which matched "Sakura Valley Fill" first: the fill got renamed and re-aimed into a
        // second sun, then the fill block below could not find "Sakura Valley Fill" any more and
        // made a fresh one. Every build therefore added another full-strength key light - the
        // scene had accumulated five of them, which is what was bleaching the whole valley.
        var directionals = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                        .Where(l => l != null && l.type == LightType.Directional)
                        .ToList();

        var keys = directionals.Where(l => l.name == "Sakura Sunset Key").ToList();
        for (int i = 1; i < keys.Count; i++)
            UnityEngine.Object.DestroyImmediate(keys[i].gameObject);
        if (keys.Count > 1)
            Debug.Log($"Sakura Pass: removed {keys.Count - 1} duplicate key light(s).");

        var sun = keys.FirstOrDefault();
        if (sun == null)
        {
            sun = new GameObject("Sakura Sunset Key").AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.name = "Sakura Sunset Key";
        // A low, raking sun is what gives a mountain pass its long shadows and warm rim - but at
        // 14 degrees it was grazing the carriageway at such an angle that the road itself picked
        // up almost no direct light. 20 degrees still reads as golden hour and actually lands on
        // the asphalt.
        // Golden hour, raking across the pass rather than straight along it. At yaw 28 the key
        // pointed almost exactly at the hero volcano, so the cone was lit dead-on: its brightest
        // generatrix ran straight down the middle of the snow cap as a hard vertical stripe with
        // no modelling either side of it. Swinging the key 34 degrees round puts the light across
        // the mountain's shoulder, which is what gives it a lit flank and a shaded flank instead
        // of a searchlight streak - and it lands across the carriageway at a similar angle.
        // Elevation stays low enough to read as evening but high enough to reach the asphalt.
        sun.transform.rotation = Quaternion.Euler(SunElevationDeg, SunYawDeg, 0f);
        // Warmer and slightly softer than the old 0.96 white-gold. The intensity budget is spent
        // on ambient instead (see below) - a hot key over a cold ambient is exactly what made
        // the lighting read as inorganic/CG.
        sun.color = new Color(1f, 0.81f, 0.60f, 1f);
        sun.intensity = KeyIntensity;
        // Part B section 24. Soft directional shadows are the highest-leverage item in the whole
        // visual upgrade: they are what turns a flat ribbon of asphalt into the section-24
        // rhythm "shadow patch -> sun patch -> tree shadow -> open bright section".
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = SunShadowStrength;
        // Bias is tight because the shadow distance is short. The previous 0.05 / 0.5 pair was
        // sized for a long-distance map and detaches contact shadows from tree trunks and
        // guardrail posts at this range (section 24 explicitly asks for contact shadows).
        sun.shadowBias = 0.02f;
        sun.shadowNormalBias = 0.35f;
        sun.shadowNearPlane = 0.2f;

        // THE reason shadows rendered as nothing at all, despite every setting above reading
        // correct. Built-in Forward assigns its single per-pixel ForwardBase slot by light
        // importance, and this scene carries 91 realtime point lights (lantern glows at 2.1,
        // tunnel lamps at 2.6) clustered along the carriageway. Left on Auto, those point lights
        // out-rank the 0.92-intensity sun near the road, the sun is demoted to an additive pass,
        // and an additive pass renders NO directional shadow map. The scene still looked lit -
        // which is why quality settings, bias, cascades, cast/receive flags, shader keywords and
        // render path all audited clean while a stock-shader control plane came back pure ambient.
        //
        // ForcePixel pins the sun to the base pass permanently. The lantern/lamp sweep below is
        // both the other half of the fix and a free win for the section 37 performance pass:
        // decorative point lights have no business being per-pixel.
        sun.renderMode = LightRenderMode.ForcePixel;

        // The cascade fit, not the light, is what decides whether shadows read as crisp canopy
        // dapple or as the rectangular patches that got them switched off before.
        QualitySettings.shadowDistance = ShadowDistanceM;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;   // no swimming under motion
        QualitySettings.shadowCascades = 4;
        // Most of the map spent on the first ~20 m, which is the road under and just ahead of the
        // rider - the only place shadow detail is legible at riding speed.
        QualitySettings.shadowCascade4Split = new Vector3(0.045f, 0.130f, 0.320f);

        sky.SetVector("_SunDirection", -sun.transform.forward);
        EditorUtility.SetDirty(sky);

        var fills = directionals.Where(l => l != null && l.name == "Sakura Valley Fill").ToList();
        for (int i = 1; i < fills.Count; i++)
            UnityEngine.Object.DestroyImmediate(fills[i].gameObject);

        var fill = fills.FirstOrDefault();
        if (fill == null)
        {
            // Unity's "fake null" overrides == but not ??, so a missing component would sail
            // straight through `GetComponent<Light>() ?? AddComponent<Light>()` and then throw.
            var fillGo = GameObject.Find("Sakura Valley Fill");
            if (fillGo == null) fillGo = new GameObject("Sakura Valley Fill");
            fill = fillGo.GetComponent<Light>();
            if (fill == null) fill = fillGo.AddComponent<Light>();
        }
        fill.gameObject.name = "Sakura Valley Fill";
        fill.type = LightType.Directional;
        fill.transform.rotation = Quaternion.Euler(-18f, 210f, 0f);
        fill.color = new Color(0.58f, 0.72f, 0.98f, 1f);
        fill.intensity = 0.30f;
        fill.shadows = LightShadows.None;
        // The fill must never compete with the key for the per-pixel base slot either.
        fill.renderMode = LightRenderMode.ForceVertex;

        // Sweep every decorative point light down to vertex importance. Named-helper-built
        // lanterns already do this, but the scene has accumulated point lights from several
        // build generations and the stragglers on Auto are what stole the sun's base-pass slot.
        int demoted = 0;
        foreach (var pl in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (pl == null || pl.type == LightType.Directional) continue;
            if (pl.renderMode == LightRenderMode.ForceVertex) continue;
            pl.renderMode = LightRenderMode.ForceVertex;
            EditorUtility.SetDirty(pl);
            demoted++;
        }
        Debug.Log($"[sakura-light] sun=ForcePixel, fill=ForceVertex, demoted {demoted} " +
                  $"non-directional light(s) to ForceVertex so the sun owns the ForwardBase slot.");

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        // Warm, high-key ambient. This is the single biggest lever on the "chibi cycling game"
        // read: with cel shading and no energy conservation, ambient *is* the shadow colour, so
        // a dim blue-violet ambient made every unlit surface in the scene look like dusk.
        // Section 24: scaled by AmbientScale. NOTE the Trilight trap documented on AmbientScale -
        // ambientIntensity does nothing in this mode, so the scale MUST be folded into the colours.
        RenderSettings.ambientSkyColor = new Color(0.60f, 0.70f, 0.90f, 1f) * AmbientScale;
        RenderSettings.ambientEquatorColor = new Color(0.86f, 0.70f, 0.68f, 1f) * AmbientScale;
        RenderSettings.ambientGroundColor = new Color(0.40f, 0.35f, 0.33f, 1f) * AmbientScale;
        // Kept at 1.0 purely as documentation: this value is INERT while ambientMode is Trilight.
        RenderSettings.ambientIntensity = 1.0f;

        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 256;
        RenderSettings.reflectionIntensity = 0.42f;

        // Fog colour matches the sky horizon so distant ridges dissolve instead of cutting out;
        // now that the horizon is warm gold the fog is too, which is what turns the ridge rings
        // into receding *warm* layers instead of a bank of grey-violet murk.
        //
        // Density is tuned against the ridge rings and against Fuji itself. At 0.0016 even the
        // nearest ring was down to ~55% and the whole backdrop vanished into flat haze. The rings
        // have since moved out to 2.0-3.5 km, which at 0.00048 buried them completely, so density
        // eases to 0.00038: the outermost ring holds ~17% (a faint silhouette, which is correct
        // aerial perspective), the inner ring ~56%, and the hero volcano runs ~73% transmittance
        // from the starting line (1342 m: hazy, blue, clearly far off) to ~91% at the finish
        // (800 m: crisp, with gullies and the snow line legible). That *change* in clarity along
        // the route is the aerial-perspective half of the approach - the other half is parallax.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.93f, 0.76f, 0.64f, 1f);
        RenderSettings.fogDensity = FogDensity;

        RenderSettings.haloStrength = 0.35f;
        RenderSettings.flareStrength = 0.6f;

        DynamicGI.UpdateEnvironment();
        return sun;
    }

    /// <summary>
    /// Part B (sections 23-25) iteration entry point: re-applies ONLY lighting, sky, atmosphere
    /// and the camera grade, then saves.
    ///
    /// <see cref="Apply"/> destroys and rebuilds the entire environment root, re-bakes the route
    /// graph and re-chunks the dressing - correct, but far too slow to iterate a lighting value
    /// against a benchmark render, and it resets serialized gameplay framing that other setup
    /// passes own. The section-23 upgrade order requires many small lighting/atmosphere
    /// iterations, so they get a surgical entry point.
    ///
    /// This touches no geometry, so it cannot violate section 22's "do not destroy the route".
    /// </summary>
    [MenuItem("MapleRide/Environment/Apply Lighting And Atmosphere Only", priority = 21)]
    public static void ApplyLightingOnly()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        MaterialCache.Clear();
        ConfigureLightingAndSky();
        ClampCelTints();
        UpgradeRoadMaterial();
        ConfigureCamera();
        SakuraSceneDefaults.Fix();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[sakura-light] lighting/atmosphere applied and saved '{scene.path}'. " +
                  $"sun elev {SunElevationDeg} yaw {SunYawDeg}, shadows Soft strength " +
                  $"{SunShadowStrength}, shadowDistance {ShadowDistanceM} m, fog {FogDensity}.");
    }

    /// <summary>
    /// Section 24. Caps every in-scene SakuraCel material's _Color tint at MaxCelTint.
    /// SakuraPass_Asphalt shipped at (1.55, 1.48, 1.44): the cel shader has no energy
    /// conservation, so a >1 tint pushed the lit road past the clip ceiling and BOTH the lit and
    /// the shadowed value resolved to white - erasing the dapple no matter how the light was set.
    /// DistantRange_* is excluded: its flat, over-bright treatment is the deliberate haze device.
    /// </summary>
    /// <summary>
    /// Part B section 26: promote the carriageway to a hero asset.
    ///
    /// Swaps SakuraPass_Asphalt from the generic MapleRide/SakuraCel onto MapleRide/SakuraRoad
    /// (same section-24 lighting, plus the section-26 layer stack: macro colour variation,
    /// detail normal, roughness variation, resurfacing patches, wheel-track darkening and a
    /// shoulder dirt/petal mask), and takes the glow off the painted markings.
    ///
    /// Idempotent by construction: it matches the material by exact name and re-pushes every
    /// value, so running it twice leaves the same result. It touches materials only - no
    /// geometry, so section 22's "do not destroy the route" cannot be violated here.
    /// </summary>
    private static void UpgradeRoadMaterial()
    {
        var roadShader = MapleRideShaderNames.Find("MapleRide/SakuraRoad");
        if (roadShader == null) { Debug.LogWarning("[sakura-road] MapleRide/SakuraRoad not found."); return; }

        const string matPath = "Assets/Environment/SakuraPass/Materials/SakuraPass_Asphalt.mat";
        var asphalt = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (asphalt == null) { Debug.LogWarning($"[sakura-road] missing {matPath}"); return; }

        // Preserve the authored albedo/tint/shade across the shader swap; only the new layers
        // are introduced. Losing these would undo the section-24 cel-tint clamp.
        var albedo = asphalt.HasProperty("_MainTex") ? asphalt.GetTexture("_MainTex") : null;
        var baseColor = asphalt.HasProperty("_Color") ? asphalt.GetColor("_Color") : Color.white;
        var shade = asphalt.HasProperty("_ShadeColor") ? asphalt.GetColor("_ShadeColor")
                                                       : new Color(0.72f, 0.66f, 0.74f, 1f);

        if (asphalt.shader != roadShader) asphalt.shader = roadShader;
        if (albedo != null) asphalt.SetTexture("_MainTex", albedo);
        asphalt.SetColor("_Color", baseColor);
        asphalt.SetColor("_ShadeColor", shade);

        var detail = LoadTexture("Sakura_Asphalt_Normal.png");
        if (detail != null) asphalt.SetTexture("_DetailNormal", detail);

        // All PROVISIONAL section-26 tuning. _UvPerMetre MUST equal ASPHALT_TILE in build_road.py
        // (0.22) or every cross-road mask below slides off the lanes.
        asphalt.SetFloat("_UvPerMetre", RoadUvPerMetre);
        asphalt.SetFloat("_RoadWidthM", RoadWidthM);
        asphalt.SetFloat("_UvCentreM", RoadUvCentreM);
        asphalt.SetFloat("_DetailTile", RoadDetailTile);
        asphalt.SetFloat("_DetailStrength", RoadDetailStrength);
        asphalt.SetFloat("_MacroScale", RoadMacroScale);
        asphalt.SetFloat("_MacroAmount", RoadMacroAmount);
        asphalt.SetFloat("_PatchScale", RoadPatchScale);
        asphalt.SetFloat("_PatchAmount", RoadPatchAmount);
        asphalt.SetFloat("_RoughVariation", RoadRoughVariation);
        asphalt.SetFloat("_TrackOffsetM", RoadTrackOffsetM);
        asphalt.SetFloat("_TrackWidthM", RoadTrackWidthM);
        asphalt.SetFloat("_TrackDarken", RoadTrackDarken);
        asphalt.SetFloat("_ShoulderWidthM", RoadShoulderWidthM);
        asphalt.SetFloat("_ShoulderAmount", RoadShoulderAmount);
        asphalt.SetColor("_ShoulderColor", RoadShoulderColor);
        // Section 28 transitional shoulder.
        asphalt.SetFloat("_VergeWidthM", RoadVergeWidthM);
        asphalt.SetFloat("_VergeAmount", RoadVergeAmount);
        asphalt.SetFloat("_VergeBreakup", RoadVergeBreakup);
        asphalt.SetColor("_VergeGravelColor", RoadVergeGravelColor);
        asphalt.SetColor("_VergeSoilColor", RoadVergeSoilColor);
        asphalt.SetFloat("_ShadowAmbient", 0.38f);
        EditorUtility.SetDirty(asphalt);

        // --- section 26 markings: "no glowing white appearance". The markings arrive with the
        // GLB's own materials, so they are damped in place rather than re-authored: cap the tint
        // under 1, drop the rim (which was drawing a bright fringe along every dash) and cut
        // specular so the paint reads matte against the asphalt's sheen.
        int damped = 0;
        // Robust hierarchy match: the staged display name ("Road Markings") and the GLB's own
        // object name ("SakuraPass_RoadMarkings") have both been the parent across build
        // generations, so walk up from each renderer and accept either.
        foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool isMarking = false;
            for (var t = r.transform; t != null && !isMarking; t = t.parent)
                if (t.name.IndexOf("Marking", System.StringComparison.OrdinalIgnoreCase) >= 0) isMarking = true;
            if (!isMarking) continue;
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_Color"))
                    {
                        var c = m.GetColor("_Color");
                        float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                        if (peak > MarkingPeak) m.SetColor("_Color", c * (MarkingPeak / peak));
                    }
                    if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.04f);
                    if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", 0.06f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
                    EditorUtility.SetDirty(m);
                    damped++;
                }
            }
        }
        Debug.Log($"[sakura-road] carriageway -> MapleRide/SakuraRoad (macro+detail+tracks+shoulder); " +
                  $"damped {damped} marking material slot(s) to peak {MarkingPeak}.");
    }

    private static void ClampCelTints()
    {
        var seen = new HashSet<Material>();
        int capped = 0;
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null || r.sharedMaterial == null) continue;
            var m = r.sharedMaterial;
            if (!seen.Add(m)) continue;
            if (m.shader == null || m.shader.name != "MapleRide/SakuraCel") continue;
            if (m.name.StartsWith("DistantRange")) continue;
            if (!m.HasProperty("_Color")) continue;

            var c = m.GetColor("_Color");
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (peak <= MaxCelTint) continue;

            float k = MaxCelTint / peak;          // scale, so the tint's HUE is preserved
            m.SetColor("_Color", new Color(c.r * k, c.g * k, c.b * k, c.a));
            EditorUtility.SetDirty(m);
            capped++;
            Debug.Log($"[sakura-light] capped '{m.name}' _Color {c} -> {m.GetColor("_Color")}");
        }
        Debug.Log($"[sakura-light] cel tint cap: {capped} material(s) brought under {MaxCelTint}.");
    }

    private static void ConfigureCamera()
    {
        var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                        .Where(c => c != null).ToList();

        var cam = cams.FirstOrDefault(c => c.name.Contains("Sakura")) ?? Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.15f;
            // Must exceed the outermost ridge ring or the horizon gets clipped away. The rings
            // moved out to 3.5 km (plus the rider's ~530 m offset from their centre, plus the
            // radius wobble), so 3000 now clips the entire backdrop out of the frame.
            cam.farClipPlane = 9000f;
            cam.allowHDR = true;
            cam.fieldOfView = 52f;
        }

        // Every camera that renders the pass gets the grade, and the values are *pushed* rather
        // than left to the component's defaults. A SakuraPostFX already sitting on the gameplay
        // camera keeps whatever was serialised into the scene, so retuning the defaults in code
        // only ever changed the diagnostic captures while the in-game view stayed blown out.
        foreach (var c in cams)
        {
            if (c.GetComponent<Camera>() == null) continue;
            var fx = c.GetComponent<SakuraPostFX>();
            if (fx == null && c != cam) continue;
            if (fx == null) fx = c.gameObject.AddComponent<SakuraPostFX>();
            TunePostFX(fx);
        }

        EnsureSakuraGradeCustomPassVolume();
    }

    /// <summary>
    /// HDRP HOST FOR SakuraPostFX (fix for the project-wide dead-grade bug: OnRenderImage never
    /// fires under HDRP, see the header comment on SakuraPostFX.cs for the measured proof).
    ///
    /// One GLOBAL CustomPassVolume at AfterPostProcess is enough for the whole project: the
    /// pass itself (SakuraGradeCustomPass) only touches cameras that already carry a
    /// SakuraPostFX component, which is exactly the set of cameras RegionDirector.ApplyAmbience
    /// already writes into every frame - gameplay camera included, across every region. Cameras
    /// deliberately built WITHOUT one (portrait/matte captures) are left untouched.
    ///
    /// Idempotent by exact name match, per the project's staging-pass rule: re-running this
    /// finds the existing volume/pass rather than creating a duplicate.
    /// </summary>
    private static void EnsureSakuraGradeCustomPassVolume()
    {
        const string VolumeName = "Sakura Grade Custom Pass Volume";

        var existing = UnityEngine.Object.FindObjectsByType<CustomPassVolume>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(v => v != null && v.name == VolumeName)
            .ToList();

        CustomPassVolume volume;
        if (existing.Count > 0)
        {
            volume = existing[0];
            // Prune any accidental duplicates from a previous partial run rather than leaving a
            // second copy of the pass double-grading every frame.
            for (int i = 1; i < existing.Count; i++)
                if (existing[i] != null) UnityEngine.Object.DestroyImmediate(existing[i].gameObject);
        }
        else
        {
            var go = new GameObject(VolumeName);
            volume = go.AddComponent<CustomPassVolume>();
        }

        volume.isGlobal = true;
        volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;

        bool hasPass = volume.customPasses.Any(p => p is SakuraGradeCustomPass);
        if (!hasPass)
        {
            var pass = volume.AddPassOfType<SakuraGradeCustomPass>();
            pass.name = "Sakura Grade (HDRP)";
            pass.enabled = true;
        }
        else
        {
            foreach (var p in volume.customPasses)
                if (p is SakuraGradeCustomPass) p.enabled = true;
        }

        EditorUtility.SetDirty(volume);
        Debug.Log("[sakura-grade] ensured global AfterPostProcess CustomPassVolume + SakuraGradeCustomPass.");
    }

    /// <summary>
    /// The one place the sunset grade is defined. Cel and terrain shading are additive on top of
    /// ambient in gamma space, so the combined exposure budget is tight: anything above roughly
    /// 0.8 exposure with a sub-1.4 bloom threshold clips the asphalt and the verge to white.
    /// </summary>
    public static void TunePostFX(SakuraPostFX fx)
    {
        if (fx == null) return;
        fx.bloomThreshold = 1.55f;
        fx.bloomSoftKnee = 0.40f;
        fx.bloomIntensity = 0.34f;
        fx.bloomIterations = 4;
        // Exposure comes *down* as the scene lighting goes up. The point of the trade is to move
        // the midtones and shadows up without moving the highlight ceiling: raising lighting
        // alone just clipped the asphalt and the verge to white.
        fx.exposure = 0.80f;
        // The chibi/cel read is high chroma at low contrast. 1.10/1.06 left the world looking
        // like washed-out photography of a purple evening.
        fx.saturation = 1.32f;
        fx.contrast = 1.02f;
        // Lift warm rather than blue: this is the last place a violet cast can creep back into
        // the shadows after the ambient has been fixed.
        fx.lift = new Color(0.045f, 0.030f, 0.020f, 0f);
        fx.gain = new Color(1.04f, 1.00f, 0.96f, 0f);
        // A heavy vignette on a bright, open scene reads as tunnel vision, and it was darkening
        // exactly the corners where the new descent and the ridge layers live.
        fx.vignetteStrength = 0.20f;
        fx.vignetteSoftness = 0.75f;
        // --- depth of field (provisional look values) ---------------------------------------
        // Sharp under the wheels, soft on the horizon. 110 m is past the far end of the visible
        // carriageway from a rider's eye on every stop on the route, so no part of the road the
        // rider is actually riding is ever defocused; the blend then ramps over 700 m, which puts
        // the lake shore in light haze, the volcano (800-1300 m) at roughly two thirds, and the
        // ridge rings (1.4-3.5 km) at the full 0.80. Capped below 1.0 deliberately: a fully
        // blurred backdrop reads as a smeared matte painting rather than as distance.
        fx.dofFocusDistance = 160f;
        fx.dofFocusRange = 700f;
        fx.dofFalloff = 1.55f;
        fx.dofStrength = 0.62f;
        fx.dofIterations = 2;

        // Section 25 atmosphere. Pushed onto the serialized component (never left to the field
        // initialisers) so an instance already saved in the scene actually picks these up.
        fx.aerialStart = AerialStartM;
        fx.aerialRange = AerialRangeM;
        fx.aerialDesaturation = AerialDesaturation;
        fx.aerialFlatten = AerialFlatten;
        fx.aerialTint = RenderSettings.fogColor;
        fx.aerialTintAmount = AerialTintAmount;
        fx.mistBaseY = MistBaseY;
        fx.mistTopY = MistTopY;
        fx.mistStrength = MistStrength;
        fx.mistStart = MistStartM;
        fx.mistColor = MistColor;
        EditorUtility.SetDirty(fx);
    }

    // ------------------------------------------------------- legacy clean-up

    /// <summary>
    /// The "Fuji Lake Background" quads were flat photo plates whose rectangular silhouette
    /// was visible against the sky. The sky shader plus the authored ridge rings replace them.
    /// </summary>
    private static void RemoveLegacyBackdropPlates()
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).ToList())
        {
            if (t == null) continue;
            if (t.name.Contains("Fuji Lake Background") ||
                t.name.Contains("Scenic Backdrop") ||
                t.name.Contains("Fuji Vista") ||
                t.name == "Fuji Lake Vista" ||
                t.name == "Summit Approach Lake View")
            {
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }
    }

    /// <summary>Placeholder embankments are redundant now that the authored terrain is staged.</summary>
    private static void HidePlaceholderGeometry()
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            if (t.name != "Left Sloped Terrain" && t.name != "Right Sloped Terrain" &&
                t.name != "Roadbed Foundation") continue;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }
    }

    // --------------------------------------------------------- GLB staging

    private static GameObject LoadBlenderAsset(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetDir}/{name}.glb");
    }

    /// <summary>
    /// Instantiates an authored GLB. The Blender side authors through <c>u2b()</c>, which
    /// round-trips exactly to Unity world space, so staging is identity - no rotation fix-up.
    /// </summary>
    private static GameObject Stage(string assetName, string displayName, Transform parent,
                                    Material overrideMat = null, bool keepTextures = true)
    {
        var asset = LoadBlenderAsset(assetName);
        if (asset == null)
        {
            Debug.LogWarning($"Sakura Pass: missing {AssetDir}/{assetName}.glb - run tools/blender/build_all.py");
            return null;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (go == null) return null;
        go.name = displayName;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            if (overrideMat == null) continue;

            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                // Only substitute when the GLB genuinely has no authored texture to preserve -
                // the old build overwrote multi-megabyte hero textures with flat colours.
                bool hasTexture = keepTextures && mats[i] != null &&
                                  mats[i].HasProperty("_MainTex") && mats[i].GetTexture("_MainTex") != null;
                if (!hasTexture) mats[i] = overrideMat;
            }
            r.sharedMaterials = mats;
        }
        return go;
    }

    /// <summary>Stages the landscape and returns a temporary collider host for ground queries.</summary>
    private static GameObject StageLandscape(Transform root)
    {
        var parent = new GameObject("Landscape").transform;
        parent.SetParent(root, false);

        var terrainMat = TerrainMaterial();
        var terrain = Stage("SakuraPass_Valley_Terrain_HD", "Valley Terrain", parent);
        if (terrain != null)
        {
            foreach (var r in terrain.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = terrainMat;
                r.sharedMaterials = mats;
            }
        }

        // Distant ranges are backdrop silhouettes, not lit terrain: the baked gradient texture
        // read as a white wall behind the valley, so they get a flat haze-toned material with
        // no rim, spec or shading and take no shadows.
        var rangeMat = CelMaterial("SakuraPass_DistantRange", new Color(0.52f, 0.52f, 0.66f, 1f),
                                   gloss: 0f, spec: 0f, rim: 0f,
                                   shade: new Color(0.50f, 0.50f, 0.64f, 1f));
        rangeMat.SetFloat("_ShadeStrength", 0.22f);
        bool rangeLod = UseRangeLods && LoadBlenderAsset("SakuraPass_Distant_Ranges_LOD") != null;
        var ranges = Stage(rangeLod ? "SakuraPass_Distant_Ranges_LOD" : "SakuraPass_Distant_Ranges",
                           "Distant Ranges", parent, rangeMat, keepTextures: false);
        if (ranges != null)
        {
            // Each ring gets its own tint, lightening toward the fog colour with distance, so the
            // three rings separate into receding planes instead of merging into one silhouette.
            // The progression is also a *hue* ramp, not just a value ramp: near ridges hold some
            // blue-green, far ones go warm rose as they pick up the horizon. That hue shift is
            // what actually sells aerial perspective - three tints of the same violet just read
            // as one flat backdrop at slightly different brightnesses, which is what the previous
            // set did.
            var ringTints = new[]
            {
                // Four rings since the descent was built: foothills, near range, mid range, far
                // range. The nearest is the darkest and coolest; the farthest is the lightest and
                // warmest, because it is seen through the most air.
                new Color(0.22f, 0.34f, 0.48f, 1f),
                new Color(0.32f, 0.41f, 0.57f, 1f),
                new Color(0.48f, 0.48f, 0.66f, 1f),
                new Color(0.68f, 0.58f, 0.72f, 1f),
            };
            // Ring summit heights from build_terrain.build_distant_ranges; all four skirt down to -110.
            var ringTops = new[] { 190f, 300f, 430f, 560f };
            var ringMats = new Material[ringTints.Length];
            for (int i = 0; i < ringTints.Length; i++)
            {
                ringMats[i] = CelMaterial("SakuraPass_DistantRange_" + i, ringTints[i],
                                          gloss: 0f, spec: 0f, rim: 0f,
                                          shade: new Color(ringTints[i].r * 0.94f,
                                                           ringTints[i].g * 0.94f,
                                                           ringTints[i].b * 0.96f, 1f));
                ringMats[i].SetFloat("_ShadeStrength", 0.18f);
                // Flat paper-cut backdrop, not a lit subject. CelRamp quantises N.L into hard
                // bands, and because the rings are faceted low-poly shells, adjacent facets land
                // in *different* bands - which painted sharp vertical seams straight down the
                // backdrop that read as tears in the sky. Two steps, offset so only the upper
                // band is ever used, and maximum softness collapses the ramp into a continuous
                // gradient with no step edge anywhere on the shell.
                ringMats[i].SetFloat("_RampSteps", 2f);
                ringMats[i].SetFloat("_RampOffset", 0.5f);
                ringMats[i].SetFloat("_RampSmooth", 0.35f);
                // The rings are a closed shell the camera lives inside; without this their
                // outward-facing triangles are all backface-culled and the backdrop is invisible.
                ringMats[i].SetFloat("_Cull", 0f);

                // Aerial perspective: the base of each ring is further away and sits in valley haze,
                // so the albedo is the hazy low-altitude tone and _HeightTint darkens toward the
                // summit. Without this the nearest ring reads as one flat slab of purple.
                ringMats[i].SetColor("_HeightTint", new Color(0.60f + i * 0.08f,
                                                              0.58f + i * 0.08f,
                                                              0.76f + i * 0.06f, 1f));
                ringMats[i].SetVector("_HeightRange",
                                      new Vector4(-110f, ringTops[i], 1f - i * 0.22f, 0f));
            }

            foreach (var r in ranges.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                int ring = 0;
                for (int i = ringTints.Length - 1; i >= 0; i--)
                    if (r.name.Contains("DistantRange_" + i)) { ring = i; break; }
                r.sharedMaterial = ringMats[ring];
            }
            if (rangeLod) BuildRangeLodGroups(ranges.transform);   // E4 huddle idea 7
        }

        // The hero volcano stands 800 m beyond the finish line on its forward tangent, broad
        // enough that its flank breaks the lake surface ~295 m past the end of the road. The snow
        // cap is a shader height blend rather than a second material slot - see build_fuji().
        var fuji = Stage("SakuraPass_Fuji", "Hero Volcano", parent, keepTextures: false);
        if (fuji != null)
        {
            // NO albedo texture. The rock set is strongly *directional*, and the cone's UVs run u
            // around the azimuth and v up the flank, so tiling it wrapped the bedding planes
            // around the mountain as concentric contour bands - the same "tree-ring" failure the
            // tunnel massif hit, and for the same reason: this texture only works projected from
            // world space, which SakuraCel does not do. It also dragged the flank down to a
            // near-black purple and buried the relief it was supposed to reveal.
            //
            // A cel-shaded Fuji does not need grain; it needs *form*. The detail now comes from
            // the geometry - build_fuji() displaces every vertex by ridged and fbm noise sampled
            // at its true world (x, z), so the flank carries real spurs and hollows that catch
            // the low sun and that the snow line has to follow.
            var fujiMat = CelMaterial("SakuraPass_Fuji", new Color(0.34f, 0.36f, 0.54f, 1f),
                                      gloss: 0f, spec: 0f, rim: 0f,
                                      shade: new Color(0.46f, 0.43f, 0.60f, 1f));
            fujiMat.SetFloat("_ShadeStrength", 0.20f);
            // Explicitly clear it: CelMaterial only *assigns* a texture when one is passed, and
            // the material asset on disk still carries the striped rock albedo from the previous
            // build. Push the cleared state rather than relying on the code default - a
            // serialized material keeps whatever was last written to it.
            fujiMat.SetTexture("_MainTex", null);
            fujiMat.SetTextureScale("_MainTex", Vector2.one);
            // Same reasoning as the ridge rings: keep the cone's form from the two-band ramp but
            // soften the band edge so the flank facets don't show a quantisation seam.
            fujiMat.SetFloat("_RampSteps", 2f);
            fujiMat.SetFloat("_RampSmooth", 0.35f);
            // Offset the ramp so the *whole* lit half of the cone sits inside one band, exactly
            // as the ridge rings do. Without it the two bands met along the sun-facing generatrix
            // and painted a hard-edged vertical stripe from the summit to the base - the single
            // most inorganic thing in the frame. The cone's form now comes from the geometry
            // relief and the height tint, not from a quantised terminator.
            fujiMat.SetFloat("_RampOffset", 0.5f);
            // Same aerial-perspective trick as the rings: hazy at the base, deeper toward the top.
            fujiMat.SetColor("_HeightTint", new Color(0.68f, 0.62f, 0.80f, 1f));
            fujiMat.SetVector("_HeightRange", new Vector4(-110f, 370f, 1f, 0f));
            // Summit is y = 370. A 12 m band centred on the 200 m snow line. The flank relief
            // swings the surface by roughly +/-17 m up here, so a band this tight still reads as
            // a wobbling line rather than the horizontal ellipse a bare height blend would give;
            // widen it much past ~20 m and the cap turns into a soft haze instead.
            // The approach now reaches the real lower flank, so the cap needs enough tonal range
            // to retain its gullies at close range instead of flattening into a white silhouette.
            // Summit is y = 370. A 14 m band centred on the 193 m snow line. The flank relief now
            // swings the surface by roughly +/-45 m up here, so a band this tight reads as a
            // strongly wobbling line rather than the horizontal ellipse a bare height blend would
            // give; widen it much past ~20 m and the cap turns into a soft haze instead - which
            // is exactly what the earlier 22 m band produced once the mountain came close enough
            // to read.
            // Snow, not a lightbulb. At 0.98/0.97/1.00 the cap multiplied out to roughly 1.5 under
            // key + ambient, which is within a rounding error of the 1.55 bloom threshold - so the
            // brightest part of the cap bled into the sky and the summit grew a halo. A slightly
            // ducked, faintly warm-shadowed white keeps it clearly snow while staying under the
            // bloom knee, and lets the sunset tint it rather than the other way round.
            fujiMat.SetColor("_SnowColor", new Color(0.90f, 0.89f, 0.93f, 1f));
            fujiMat.SetVector("_SnowRange", new Vector4(186f, 200f, 1f, 0f));

            foreach (var r in fuji.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = fujiMat;
                r.sharedMaterials = mats;
            }
        }

        var lake = Stage("SakuraPass_Lake", "Valley Lake", parent, WaterMaterial(), keepTextures: false);        if (lake != null)
        {
            foreach (var r in lake.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        return terrain != null ? AddGroundColliders(terrain) : null;
    }

    private static void StageRoad(Transform root)
    {
        var parent = new GameObject("Roadway").transform;
        parent.SetParent(root, false);

        // The baked asphalt albedo is authored dark (0.16 linear) for a daylight key. Under a
        // low sun it came out near-black, which is what made the carriageway read as a purple
        // strip. _Color is a straight gain on the texture, so pushing it past 1 is the cheapest
        // way to lift the road without rebuilding the (slow) texture stage.
        var asphalt = CelMaterial("SakuraPass_Asphalt", new Color(1.55f, 1.48f, 1.44f, 1f),
                                  gloss: 0.42f, spec: 0.30f, rim: 0.25f,
                                  shade: new Color(0.72f, 0.66f, 0.74f, 1f),
                                  texture: LoadTexture("Sakura_Asphalt_Albedo.png"));
        var carriageway = Stage("SakuraPass_Road_HD", "Carriageway", parent, asphalt);

        // The authored asphalt is also the riding surface: the rider's grounding and safety
        // scripts raycast against it, so it is the one staged asset that keeps collision.
        if (carriageway != null)
        {
            foreach (var mf in carriageway.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.enabled = true;
            }

            // Agent HQ: lamp probes along the road. The road surface is the one thing near every
            // lantern that can actually bounce its warm light back up into the probe network -
            // mark it Contribute GI / Receive: Light Probes (NOT Lightmaps) so a bake needs no
            // lightmap UV2 pass on this procedurally regenerated mesh, just the cheap probe pass.
            foreach (var r in carriageway.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI);
                r.receiveGI = ReceiveGI.LightProbes;
            }
        }

        Stage("SakuraPass_RoadMarkings", "Road Markings", parent);

        // MLIT's 景観 guideline replaced the default white-grey W-beam with dark brown
        // 10YR 2.0/1.0 (~#332C28) on national-park and scenic routes, precisely so the rail
        // stops reading as a bright line cutting across the view. Keeping the higher gloss and
        // spec (vs the timber rail) is what still sells it as painted steel rather than wood.
        var steel = CelMaterial("SakuraPass_Guardrail", new Color(0.20f, 0.173f, 0.157f, 1f),
                                gloss: 0.42f, spec: 0.20f, rim: 0.18f);
        // E4 / huddle idea 2: one <=300-tri bevelled segment, instanced along the same runs.
        if (!(UseInstancedGuardrail && StageInstancedGuardrail(parent, steel)))
            Stage("SakuraPass_Guardrail_HD", "Guardrail", parent, steel);

        // Concept panels 02 and 05 show the timber 景観ガードレール, not the steel W-beam. The two
        // are route-baked over complementary arc-length runs (see TIMBER_RUNS in build_expand.py
        // and the matching gaps in build_road.build_guardrail), so they never overlap.
        var timber = CelMaterial("SakuraPass_RailWood", new Color(0.42f, 0.31f, 0.24f, 1f),
                                 gloss: 0.12f, spec: 0.08f, rim: 0.5f,
                                 texture: LoadTexture("Sakura_Bark_Albedo.png"));
        Stage("SakuraPass_Timber_Guardrail", "Timber Guardrail", parent, timber);

        // Fallen-petal drifts along both kerbs. Shadow casting off: a 3 cm ribbon lying on the
        // road contributes nothing but shadow acne along its own edge.
        var drift = FoliageMaterial("SakuraPass_PetalDrift", new Color(0.97f, 0.76f, 0.83f, 1f),
                                    LoadTexture("Sakura_Blossom_Atlas.png"), wind: 0.02f);
        var drifts = Stage("SakuraPass_Petal_Drifts", "Petal Drifts", parent, drift);
        if (drifts != null)
            foreach (var r in drifts.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // The road is the one surface with its own dedicated shaders, its own drainage geometry
        // and a UV-space contract with build_road.py. That whole milestone lives in
        // SakuraRoadPass so the narrow road pass and this full pass cannot drift apart; under a
        // Built-in pipeline the call is a no-op and the cel materials above stand.
        SakuraRoadPass.ApplyToRoadGroup(parent);
    }

    /// <summary>
    /// The concept board's remaining landmarks: the cliffside tunnel, the far-shore town, the
    /// torii standing in the lake and the hillside lettering above the hairpin.
    /// </summary>
    private static void StageExpansion(Transform root, SakuraRoute route)
    {
        var parent = new GameObject("Landmarks").transform;
        parent.SetParent(root, false);

        // --- cliffside tunnel (route-baked, so it lands at identity) -------------------
        var tunnel = Stage("SakuraPass_Tunnel", "Cliff Tunnel", parent);
        if (tunnel != null)
        {
            // The massif is a hillside, not a prop: give it the triplanar terrain material so it
            // blends into the slope it is cut into. Its own UV-mapped rock texture banded the
            // whole cross-section like a layer cake - the striated albedo only works projected
            // from world space, which is exactly what the terrain shader does. With no uv1 the
            // splat weights read zero, so it takes the grass layer, and the shader's own
            // slope blend (34-58 degrees) brings rock back in on the steep cut faces.
            var rock = TerrainMaterial();
            // The bore is enclosed. Unity's ambient term is unshadowed, so albedo is the only
            // lever that makes an interior read as dim.
            var boreRock = CelMaterial("SakuraPass_TunnelBore", new Color(0.21f, 0.205f, 0.195f, 1f),
                                       gloss: 0.04f, spec: 0.03f, rim: 0.04f,
                                       shade: new Color(0.14f, 0.15f, 0.22f, 1f),
                                       texture: LoadTexture("Sakura_Rock_Albedo.png"));
            // Ambient light is unshadowed, so the rock casting shadows does nothing for the bore
            // interior - albedo is the only lever that makes a tunnel read as dim. A 0.24 panel
            // still comes out well above the JIS 2.3 cd/m2 a real 60 km/h touge bore runs at.
            var lining = CelMaterial("SakuraPass_TunnelLining", new Color(0.24f, 0.235f, 0.225f, 1f),
                                     gloss: 0.30f, spec: 0.08f, rim: 0.05f,
                                     shade: new Color(0.16f, 0.17f, 0.24f, 1f),
                                     texture: LoadTexture("Sakura_Scree_Albedo.png"));
            // MLIT 10-10 requires a face-wall portal to carry a low-reflectance finish: a bright
            // concrete face makes the black-hole effect worse, because the eye adapts to the
            // glare and the bore behind it reads as a solid black rectangle. It still needs a
            // texture though - untextured, a 17 x 10 m slab at 0.09 read as a hole cut in the
            // world rather than a wall. Scree gives it a coarse shuttered-concrete grain.
            var face = CelMaterial("SakuraPass_TunnelFace", new Color(0.17f, 0.167f, 0.160f, 1f),
                                   gloss: 0.03f, spec: 0.02f, rim: 0.06f,
                                   shade: new Color(0.10f, 0.11f, 0.16f, 1f),
                                   texture: LoadTexture("Sakura_Scree_Albedo.png"));
            // 扁額 - the carved name tablet. It needs its own lighter stone so it reads against
            // the deliberately dark face wall; sharing the portal material made it vanish.
            var plaque = CelMaterial("SakuraPass_TunnelPlaque", new Color(0.52f, 0.49f, 0.44f, 1f),
                                     gloss: 0.10f, spec: 0.06f, rim: 0.20f,
                                     texture: LoadTexture("Sakura_Scree_Albedo.png"));
            // E4 huddle idea 9: world-space macro variation over the tiled lining / bore / face.
            ApplyMacroVariation(boreRock); ApplyMacroVariation(lining); ApplyMacroVariation(face);
            var sodium = LoadOrCreate("SakuraPass_Sodium", "Unlit/Color");
            sodium.SetColor("_Color", new Color(1f, 0.78f, 0.42f, 1f));

            foreach (var r in tunnel.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                Material m = n.Contains("lamp") ? sodium
                           : n.Contains("lining") ? lining
                           : n.Contains("plaque") ? plaque
                           : (n.Contains("portal") || n.Contains("coping")) ? face
                           : n.Contains("bore") ? boreRock
                           : rock;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;

                if (!n.Contains("lamp")) continue;
                // A warm point light per luminaire. JIS Z 9116 puts a 60 km/h touge tunnel at
                // 2.3 cd/m2 - genuinely dim - so these are for shape and colour, not to light
                // the bore like a showroom.
                // Staged meshes carry world-space vertices on an identity transform (the u2b
                // convention), so the lamp GameObject sits at the world origin - and a Light
                // added to it went with it, parking six warm point lights on the open climb
                // instead of inside the bore. The light therefore lives on its own child, whose
                // transform can be moved without dragging the world-space mesh with it.
                var stray = r.gameObject.GetComponent<Light>();
                if (stray != null) UnityEngine.Object.DestroyImmediate(stray);

                Transform lampT = r.transform.Find("Lamp Glow");
                if (lampT == null)
                {
                    var lampGo = new GameObject("Lamp Glow");
                    lampGo.transform.SetParent(r.transform, false);
                    lampT = lampGo.transform;
                }
                lampT.position = r.bounds.center;

                var light = lampT.GetComponent<Light>();
                if (light == null) light = lampT.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.72f, 0.34f, 1f);
                light.intensity = 2.6f;
                light.range = 16f;
                light.shadows = LightShadows.None;
                // Vertex lighting, like the lanterns: a per-pixel additive pass for a JIS-dim
                // luminaire buys nothing and costs a full extra draw of every object it touches.
                light.renderMode = LightRenderMode.ForceVertex;
            }
        }

        // --- Lake Village: now the aid stop ON the Kawabe lakeshore road ----------------
        // It used to be a silhouette 200 m across open water, so it was built without shadows
        // and without shadow receipt. The circuit's recovery half now rides straight past the
        // frontage at 4 m, where an unshadowed building reads as a flat sticker pasted on the
        // hillside - so both are back on.
        var village = Stage("SakuraPass_Lake_Village", "Lake Village", parent);
        if (village != null)
            foreach (var r in village.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }

        // --- torii standing in the lake ------------------------------------------------
        // Local origin is the waterline, so the object drops straight onto the lake level.
        // 朱色 authored dark: at 0.74 the sun plus bloom clipped this to a flat fire-engine red
        // with no visible modelling. A real weathered lacquer reads far deeper than the swatch.
        var lakeTorii = Stage("SakuraPass_Lake_Torii", "Lake Torii", parent,
                              CelMaterial("SakuraPass_Vermilion", new Color(0.44f, 0.11f, 0.08f, 1f),
                                          gloss: 0.42f, spec: 0.22f, rim: 0.22f));
        if (lakeTorii != null)
        {
            lakeTorii.transform.position = new Vector3(126f, -44f, 52f);
            // Span runs along local X; the rider looks out across +x, so yawing 90 degrees
            // presents the opening rather than the side of the gate.
            lakeTorii.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        }

        // --- hillside lettering above the hairpin --------------------------------------
        var sign = Stage("SakuraPass_Hillside_Sign", "Hillside Lettering", parent);
        if (sign != null)
        {
            int i = Mathf.Clamp(route.IndexAtClimbFraction(0.72f), 1, route.Count - 2);
            var side = route.Side[i];
            // Clear of the tunnel massif (which occupies 0.565-0.645) and high enough on the
            // flank to clear the treeline. It reads like hillside lettering only if it is well
            // above the rider - low on the slope it just looks like a roadside board.
            var at = route.Position[i] - side * 40f;
            var g = SampleGround(at);
            at.y = (g.Hit ? g.Point.y : route.Position[i].y) + 17f;
            sign.transform.position = at;
            // Faces the road, not down the pass: the lettering is on the rider's left flank, so
            // squaring it to the tangent would present it edge-on for the whole approach. The
            // text mesh reads along -forward, hence the negated side vector.
            sign.transform.rotation = Quaternion.LookRotation(new Vector3(-side.x, 0f, -side.z));
            Debug.Log($"[expansion] hillside lettering at {at} (ground hit {g.Hit})");

            // Split the letters from the plinth: near-white characters against a dark stone bar
            // are what make hillside lettering legible at 200 m. A single grey material for both
            // left it reading as a smudge on the slope.
            var glyph = CelMaterial("SakuraPass_SignGlyph", new Color(0.95f, 0.94f, 0.91f, 1f),
                                    gloss: 0.1f, spec: 0.08f, rim: 0.25f,
                                    shade: new Color(0.62f, 0.64f, 0.72f, 1f));
            var plinth = CelMaterial("SakuraPass_SignPlinth", new Color(0.21f, 0.20f, 0.19f, 1f),
                                     gloss: 0.08f, spec: 0.05f, rim: 0.1f);
            foreach (var r in sign.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.gameObject.name.ToLowerInvariant().Contains("plinth") ? plinth : glyph;
                var ms = r.sharedMaterials;
                for (int s = 0; s < ms.Length; s++) ms[s] = m;
                r.sharedMaterials = ms;
            }
        }
    }

    /// <summary>
    /// Landmarks for the expansion's named terminal checkpoints.
    ///
    /// A checkpoint the HUD names in gold and the map pins has to be something the rider can
    /// actually see, or arriving at it is just a number changing. Both are assembled from staged
    /// assets at measured arc positions on their own segment, so a re-cut route carries them.
    /// </summary>
    private static void StageExpansionLandmarks(Transform parent,
                                                Dictionary<string, SakuraRoute> segments)
    {
        var vermilion = CelMaterial("SakuraPass_Vermilion", new Color(0.44f, 0.11f, 0.08f, 1f),
                                    gloss: 0.42f, spec: 0.22f, rim: 0.22f);
        var stone = CelMaterial("SakuraPass_Stone", Color.white, gloss: 0.16f, spec: 0.10f,
                                rim: 0.9f, texture: LoadTexture("Sakura_Rock_Albedo.png"));

        // (segment, arc fraction, group name, whether the torii spans the road)
        var sites = new[]
        {
            ("aozora", 0.985f, "Cloudline Shrine"),
            ("maple", 0.985f, "Maple City Gate"),
        };

        foreach (var (segId, f, label) in sites)
        {
            if (!segments.TryGetValue(segId, out var route) || route.Count < 6) continue;
            int i = Mathf.Clamp(route.IndexAt(f * route.Length), 2, route.Count - 3);
            var p = route.Position[i];
            var side = route.Side[i];
            var fwd = route.Tangent[i];
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            var group = new GameObject(label).transform;
            group.SetParent(parent, false);

            // Road-spanning torii: the gate the rider passes through, so the checkpoint reads
            // as an arrival rather than as a HUD event.
            var torii = Stage("SakuraPass_Torii_Great", $"{label} Torii", group, vermilion);
            if (torii != null)
            {
                var g = SampleGround(p);
                var at = p;
                at.y = (g.Hit ? g.Point.y : p.y) - 0.15f;
                torii.transform.position = at;
                // Same convention as the pass's Torii Gate: yaw alone aligns the gate's local +Z
                // with the road tangent, which puts its span axis (local X) ACROSS the road.
                // Adding +90 here rotated the span along the road and planted a pillar in the
                // carriageway - caught in diag_maple_gate.png, where it read as a red slab.
                torii.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            // A short lantern avenue on the approach, both shoulders.
            for (int k = 1; k <= 3; k++)
            {
                int li = Mathf.Clamp(route.IndexAt((f * route.Length) - k * 11f), 1, route.Count - 2);
                for (int s = -1; s <= 1; s += 2)
                {
                    var xz = route.Position[li] + route.Side[li] * (s * (route.HalfWidth + 1.6f));
                    var g = SampleGround(xz);
                    if (!g.Hit || g.SlopeDeg > 36f) continue;
                    var lantern = Stage("SakuraPass_Stone_Lantern",
                                        $"{label} Lantern {k}{(s < 0 ? "L" : "R")}", group, stone);
                    if (lantern == null) continue;
                    lantern.transform.position = g.Point + Vector3.down * 0.08f;
                    lantern.transform.rotation = Quaternion.Euler(
                        0f, Mathf.Atan2(route.Tangent[li].x, route.Tangent[li].z) * Mathf.Rad2Deg + 90f, 0f);
                    AddLanternGlow(lantern);
                }
            }

            // Name board, set back on the inland shoulder and facing back down the road.
            var sign = Stage("SakuraPass_Summit_Sign", $"{label} Sign", group);
            if (sign != null)
            {
                var xz = p - side * (route.HalfWidth + 3.2f);
                var g = SampleGround(xz);
                if (g.Hit)
                {
                    sign.transform.position = g.Point;
                    sign.transform.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
                }
                else UnityEngine.Object.DestroyImmediate(sign);
            }

            Debug.Log($"[expansion] landmark '{label}' at {p} on segment {segId}.");
        }
    }

    /// <summary>
    /// Mesh colliders on the staged terrain so the scatter pass can raycast for ground height
    /// and slope. Torn down again once dressing is placed.
    /// </summary>
    private static GameObject AddGroundColliders(GameObject terrain)
    {
        var host = new GameObject("~SakuraGroundQuery");
        host.hideFlags = HideFlags.HideAndDontSave;

        foreach (var mf in terrain.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var proxy = new GameObject(mf.name);
            proxy.transform.SetParent(host.transform, false);
            proxy.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
            proxy.transform.localScale = mf.transform.lossyScale;
            proxy.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        Physics.SyncTransforms();
        GroundHost = host.transform;
        return host;
    }

    private struct Ground
    {
        public bool Hit;
        public Vector3 Point;
        public Vector3 Normal;
        public float SlopeDeg;
    }

    /// <summary>The terrain-only collider host, so ground queries cannot latch onto the road,
    /// a prop already placed, or the scene's valley catch floor far below.</summary>
    private static Transform GroundHost;

    private static readonly RaycastHit[] GroundHits = new RaycastHit[32];

    private static int GroundQueries;
    private static int GroundMisses;

    /// <summary>
    /// True when the ground around <paramref name="xz"/> falls away on most sides, i.e. the point
    /// is on a convex crest rather than a shelf.
    ///
    /// A single downward raycast says nothing about the *footprint* a prop needs. A rock cluster
    /// spans a couple of metres, so dropping one onto a narrow ridge line leaves its outer rocks
    /// hanging in mid-air even though the centre sample was a perfect hit - which is exactly how
    /// boulders ended up floating over the skyline. Slope angle does not catch this either: the
    /// crest itself is nearly flat.
    /// </summary>
    private static bool IsCrest(Vector3 xz, float centreY, float radius, float drop = 1.2f)
    {
        int falls = 0;
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            var probe = xz + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            var g = SampleGround(probe);
            if (!g.Hit || centreY - g.Point.y > drop) falls++;
        }
        // Two of four is deliberately strict. A rounded hilltop only falls away sharply on the
        // two downhill sides, and that is already enough to leave a cluster's outer rocks in air.
        return falls >= 2;
    }

    /// <summary>
    /// The lowest ground height under a prop's *footprint*, plus how much the ground varies
    /// across it.
    ///
    /// A single downward ray gives the height at one point, which is all a lantern needs but
    /// nothing like enough for a multi-metre boulder or shelf: on a 40 degree slope the ground
    /// falls 3 m across a 4 m footprint, so a prop seated at the centre height hangs a metre and
    /// a half in the air on its downhill edge. Seat props at <paramref name="lowest"/> and reject
    /// them when <paramref name="spread"/> is more than the prop can bury.
    /// </summary>
    private static bool SampleFootprint(Vector3 xz, float radius, out float lowest, out float spread)
    {
        lowest = float.MaxValue;
        spread = 0f;
        float highest = float.MinValue;
        int hits = 0;

        for (int i = 0; i <= 8; i++)
        {
            var probe = xz;
            if (i > 0)
            {
                float a = (i - 1) * Mathf.PI * 0.25f;
                probe += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            }
            var g = SampleGround(probe);
            if (!g.Hit) continue;
            hits++;
            lowest = Mathf.Min(lowest, g.Point.y);
            highest = Mathf.Max(highest, g.Point.y);
        }

        // A footprint that only partly finds ground is over an edge; refuse it outright.
        if (hits < 9) return false;
        spread = highest - lowest;
        return true;
    }

    // --- road corridor clearance -------------------------------------------------------
    // Scatter offsets are measured from the LOCAL centreline sample, which was safe while the
    // route was a single strand. The switchback descent folds the road back on itself, so a
    // tree legitimately 20 m off limb A now lands squarely on the carriageway of limb B one
    // level below. This tests a candidate against EVERY route sample in plan, not just its own,
    // and is what keeps trunks out of the road. Provisional clearance values live in the
    // constants below.
    private const float RoadClearTree = 3.2f;   // metres of verge kept clear beyond HalfWidth for trunks
    private const float RoadClearProp = 1.4f;   // ...for rocks and ground cover
    private const float RoadClearHeight = 9f;   // only limbs within this vertical band can conflict
    // Tree-free arc around the Descent Overlook landmark (metres past the summit). Wider than the
    // railing run itself so the approach frames the view instead of revealing it at the last metre.
    private const float OverlookClearStart = 112f;  // provisional
    private const float OverlookClearEnd = 178f;    // provisional

    private static Vector3[] CorridorPts = Array.Empty<Vector3>();
    private static float CorridorHalf = 3.5f;

    private static void BuildCorridor(SakuraRoute route)
    {
        BuildCorridor(new[] { route });
    }

    /// <summary>
    /// Plan-view corridor for every road in the network.
    ///
    /// Since the expansion this must cover all four segments, not just the pass: without the
    /// Aozora limbs in here, dressing scattered off the pass would be planted straight down the
    /// middle of a switchback 80 m inland, and vice versa.
    /// </summary>
    private static void BuildCorridor(IEnumerable<SakuraRoute> routes)
    {
        var pts = new List<Vector3>();
        float half = 3.5f;
        foreach (var r in routes)
        {
            if (r == null) continue;
            pts.AddRange(r.Position);
            half = r.HalfWidth;
        }
        CorridorPts = pts.ToArray();
        CorridorHalf = half;
    }

    private static bool ClearOfRoad(Vector3 xz, float y, float clearance)
    {
        float lim = CorridorHalf + clearance;
        float lim2 = lim * lim;
        for (int i = 0; i < CorridorPts.Length; i++)
        {
            var c = CorridorPts[i];
            // Different levels of the same hairpin stack are allowed to overlap in plan.
            if (Mathf.Abs(c.y - y) > RoadClearHeight) continue;
            float dx = c.x - xz.x, dz = c.z - xz.z;
            if (dx * dx + dz * dz < lim2) return false;
        }
        return true;
    }

    private static Ground SampleGround(Vector3 xz)
    {        var origin = new Vector3(xz.x, 400f, xz.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits, 1200f);
        GroundQueries++;

        bool found = false;
        var best = default(RaycastHit);
        for (int i = 0; i < count; i++)
        {
            var h = GroundHits[i];
            if (GroundHost != null && !h.collider.transform.IsChildOf(GroundHost)) continue;
            if (found && h.distance >= best.distance) continue;
            best = h;
            found = true;
        }

        if (!found)
        {
            GroundMisses++;
            return new Ground { Hit = false, Normal = Vector3.up };
        }

        // Terrain tiles export facing up, but a hit on the underside would still report a
        // downward normal; flipping keeps slope maths sane instead of silently tipping props.
        var n = best.normal.y < 0f ? -best.normal : best.normal;
        return new Ground
        {
            Hit = true,
            Point = best.point,
            Normal = n,
            SlopeDeg = Vector3.Angle(n, Vector3.up),
        };
    }

    // ------------------------------------------------------------- section map

    /// <summary>
    /// The seven sections of the concept board, plus the three that make up the descent off the
    /// far side of the pass. Boundaries are arc lengths, not fractions - see SectionEnds.
    /// </summary>
    public enum Section
    {
        Forest, ShrineGate, Lakeside, Cliffside, Hairpin, SummitApproach, Summit,
        // --- the far side ---
        LakeApproach,   // the crest rolls over and reveals the lake beneath Fuji
        FujiShore,      // exposed shoreline traverse with the largest parallax change
        FujiFoot,       // final hairpin and trailhead on the lower mountain shelf
    }

    /// <summary>
    /// Per-section dressing rules. Measured curvature decides where the *landmarks* go, but it
    /// says nothing about what should be *growing* there - the concept board wants conifers in
    /// the forest, bare scree on the cliffside and an open, almost treeless summit. This table is
    /// the only place those differences are expressed.
    /// </summary>
    private struct SectionRule
    {
        public float TreeChance;      // per tree slot, per sample
        public float PineRatio;       // share of trees that are conifers
        public float CoverChance;     // grass/fern per slot
        public float RockChance;      // rock cluster per sample
        public float MinOffset;       // metres from the centreline trees may start
        public float Reach;           // how far past MinOffset trees may stand
    }

    /// <summary>
    /// Section boundaries. The first seven are fractions of the <i>climb</i>; the descent
    /// sections that follow are absolute arc lengths past the summit.
    ///
    /// These were fractions of total route length until the far side of the pass was built. The
    /// route then went from 531 m to ~1120 m, which would have stretched "Forest" from the first
    /// 69 m to the first 145 m and pushed "Summit" dressing onto the valley floor. Anchoring the
    /// climb to the climb keeps every existing section exactly where it was.
    /// </summary>
    private static readonly float[] ClimbSectionEnds = { 0.13f, 0.23f, 0.40f, 0.56f, 0.75f, 0.94f, 1.01f };

    /// <summary>Metres past the summit at which each Fuji-approach section ends.</summary>
    private static readonly float[] ApproachSectionEnds = { 180f, 470f, 1e6f };

    /// <summary>
    /// Steepest ground a tree will root on.
    ///
    /// This single number is what emptied the pass: at 34 degrees, **951 of 1019** placement
    /// attempts were rejected and only 68 trees survived, because the valley falls away hard
    /// within 15 m of the verge and the inland cut rises just as fast. Mountain sakura genuinely
    /// cling to slopes this steep, so the limit is the thing that was wrong, not the density.
    /// Measure the rejection rate before tuning densities - a filter this dominant makes every
    /// other knob look broken.
    /// </summary>
    private const float TreeMaxSlope = 60f;

    /// <summary>
    /// Arc-length window of the cliffside tunnel, kept in step with D_START / D_END in
    /// build_tunnel.py. Scatter is suppressed across it because the bore has 6 m of rock
    /// overhead - trees placed here grow through the massif.
    /// </summary>
    private const float TunnelStart = 300f;
    private const float TunnelEnd = 342f;

    /// <summary>
    /// The pass is named for its sakura, so sakura is the default everywhere and conifers are an
    /// accent - never the other way round. Only the cliffside is genuinely sparse, because
    /// nothing roots on scree; even the summit gets a grove, because a bare summit on a road
    /// called Sakura Pass reads as missing content rather than as altitude.
    /// </summary>
    private static readonly SectionRule[] SectionRules =
    {
        // Forest: a dense sakura wood with conifers threaded through it, crowding the verge.
        new SectionRule { TreeChance = 0.80f, PineRatio = 0.30f, CoverChance = 0.62f, RockChance = 0.10f, MinOffset = 6.2f, Reach = 30f },
        // Shrine gate: still dense, but held back from the carriageway so the torii reads clean.
        new SectionRule { TreeChance = 0.70f, PineRatio = 0.10f, CoverChance = 0.52f, RockChance = 0.06f, MinOffset = 8.5f, Reach = 26f },
        // Lakeside: sakura in the open, leaning over the water. Almost no conifer, no stone.
        new SectionRule { TreeChance = 0.82f, PineRatio = 0.06f, CoverChance = 0.66f, RockChance = 0.05f, MinOffset = 6.2f, Reach = 30f },
        // Cliffside: the one genuinely sparse stretch - scree and outcrops.
        new SectionRule { TreeChance = 0.42f, PineRatio = 0.30f, CoverChance = 0.36f, RockChance = 0.32f, MinOffset = 7.5f, Reach = 26f },
        // Hairpin: heavy blossom on the outside of the bends, thinned right at the verge so the
        // chevrons stay readable.
        new SectionRule { TreeChance = 0.74f, PineRatio = 0.22f, CoverChance = 0.54f, RockChance = 0.20f, MinOffset = 7.0f, Reach = 28f },
        // Summit approach: the treeline, so conifers gain ground - but sakura still leads.
        new SectionRule { TreeChance = 0.68f, PineRatio = 0.14f, CoverChance = 0.48f, RockChance = 0.24f, MinOffset = 6.0f, Reach = 28f },
        // Summit: the payoff. A blossom grove around the viewpoint, set back off the railing.
        new SectionRule { TreeChance = 0.78f, PineRatio = 0.08f, CoverChance = 0.58f, RockChance = 0.18f, MinOffset = 8.4f, Reach = 26f },
        // Lake approach: the crest rolls over into the switchbacks and the water first opens
        // beneath Fuji. Deliberately the thinnest wooded section on the route: this is the one
        // place where the rider looks *down* onto the limbs they are about to ride, and the
        // first pass at 0.52/9.5 filled the flank between the limbs with blossom until neither
        // the hairpins nor the lake were visible from the overlook.
        new SectionRule { TreeChance = 0.30f, PineRatio = 0.34f, CoverChance = 0.54f, RockChance = 0.34f, MinOffset = 12f, Reach = 30f },
        // Fuji shore: deliberately sparse on the valley side so the lake provides a continuous
        // middle-distance layer while the mountain grows beyond it.
        new SectionRule { TreeChance = 0.34f, PineRatio = 0.26f, CoverChance = 0.60f, RockChance = 0.30f, MinOffset = 11f, Reach = 34f },
        // Fuji foot: trees return around the trailhead, but remain set back so the snow cap and
        // arrival gate stay readable through the final bend.
        new SectionRule { TreeChance = 0.58f, PineRatio = 0.18f, CoverChance = 0.68f, RockChance = 0.18f, MinOffset = 9.0f, Reach = 28f },
    };

    private static Section SectionAt(SakuraRoute route, int i)
    {
        float d = route.Distance[i];
        float climb = Mathf.Max(1f, route.ClimbLength);
        if (d <= climb)
        {
            float f = d / climb;
            for (int s = 0; s < ClimbSectionEnds.Length; s++)
                if (f < ClimbSectionEnds[s]) return (Section)s;
            return Section.Summit;
        }

        float past = d - climb;
        for (int s = 0; s < ApproachSectionEnds.Length; s++)
            if (past < ApproachSectionEnds[s]) return (Section)((int)Section.LakeApproach + s);
        return Section.FujiFoot;
    }

    // ------------------------------------------------------------- scatter

    /// <summary>
    /// Dresses the route with authored flora and props. Everything is placed by raycasting
    /// onto the staged terrain, so props always sit on the ground rather than at the road's
    /// altitude - the valley falls away 50 m over the width of the corridor.
    /// </summary>
    private static void ScatterDressing(Transform root, SakuraRoute route, GameObject ground)
    {
        var parent = new GameObject("Route Dressing").transform;
        parent.SetParent(root, false);

        var flora = new GameObject("Flora").transform;
        flora.SetParent(parent, false);
        var props = new GameObject("Props").transform;
        props.SetParent(parent, false);

        var rng = new System.Random(20260911);
        // The corridor test must cover EVERY road in the network, not just the pass, or dressing
        // scattered off the pass lands in the middle of an Aozora switchback 80 m inland.
        BuildCorridor(SakuraRoute.LoadSegments().Values);
        bool canQuery = ground != null;
        GroundQueries = 0;
        GroundMisses = 0;

        var barkMat = CelMaterial("SakuraPass_Bark", Color.white, gloss: 0.12f, spec: 0.06f,
                                  rim: 0.5f, texture: LoadTexture("Sakura_Bark_Albedo.png"));
        var blossomMat = FoliageMaterial("SakuraPass_Blossom", new Color(0.94f, 0.66f, 0.74f, 1f),
                                         LoadTexture("Sakura_Blossom_Atlas.png"), wind: 0.18f);
        var grassMat = FoliageMaterial("SakuraPass_GroundCover", new Color(0.86f, 1f, 0.80f, 1f),
                                       LoadTexture("Sakura_Leaf_Atlas.png"), wind: 0.26f);
        var stoneMat = CelMaterial("SakuraPass_Stone", Color.white, gloss: 0.16f, spec: 0.10f,
                                   rim: 0.9f, texture: LoadTexture("Sakura_Rock_Albedo.png"));

        string[] trees = { "SakuraPass_Sakura_Tree_A", "SakuraPass_Sakura_Tree_B", "SakuraPass_Sakura_Tree_C" };
        string[] pines = { "SakuraPass_Pine_A", "SakuraPass_Pine_B" };
        // Section 27: green broadleaf. _C is the expensive hero crown, so the near-band list
        // deliberately omits it (see BroadleafHeroMaxBand).
        string[] broadNear = { "SakuraPass_Broadleaf_A", "SakuraPass_Broadleaf_B" };
        string[] broadFar = { "SakuraPass_Broadleaf_A", "SakuraPass_Broadleaf_B", "SakuraPass_Broadleaf_C" };
        var needleMat = CelMaterial("SakuraPass_Needle", new Color(0.26f, 0.40f, 0.28f, 1f),
                                    gloss: 0.10f, spec: 0.04f, rim: 0.45f);
        // Section 27 "variety": a small palette of shared tinted crown materials. Picked per
        // instance so no two neighbouring trees are exact clones, but still shared assets so
        // batching survives - a MaterialPropertyBlock per tree would break it at this count.
        var leafMats = new Material[BroadleafTints.Length];
        for (int t = 0; t < BroadleafTints.Length; t++)
            leafMats[t] = FoliageMaterial($"SakuraPass_BroadleafCrown_{t}", BroadleafTints[t],
                                          LoadTexture("Sakura_Leaf_Atlas.png"), wind: 0.22f);
        var blossomMats = new Material[BlossomTints.Length];
        for (int t = 0; t < BlossomTints.Length; t++)
            blossomMats[t] = BlossomCrownMaterial(t);
        int placed = 0;
        int treeTooSteep = 0, treeNoGround = 0, treeTried = 0, treeOnRoad = 0;
        var pineSection = new int[SectionRules.Length];
        // Section 27: the whole point of this milestone is a composition ratio, so it gets
        // measured and logged rather than eyeballed off a render.
        var broadSection = new int[SectionRules.Length];
        var perSection = new int[SectionRules.Length];
        var triedSection = new int[SectionRules.Length];

        float nextLantern = 45f;
        float nextMarker = 100f;
        float nextWall = 30f;

        for (int i = 2; i < route.Count - 2; i++)
        {
            var p = route.Position[i];
            var side = route.Side[i];
            float yaw = Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg;
            float d = route.Distance[i];
            var section = SectionAt(route, i);
            var rule = SectionRules[(int)section];

            // Inside the tunnel the sky is 6 m of rock, so nothing is scattered through the bore
            // or into the portal cut. The window matches D_START/D_END in build_tunnel.py with a
            // few metres of margin for the portal face walls and their wing walls.
            if (d > TunnelStart - 8f && d < TunnelEnd + 8f) continue;

            // --- trees: the pass is named for these, so they are laid down thickly -----------
            // Eight slots per sample in three depth bands. A single uniform band leaves an even
            // scatter that reads as parkland; banding gives an avenue right on the verge, a solid
            // middle wall of blossom, and a thinning back grove that hides the terrain seams.
            for (int slot = 0; slot < 8; slot++)
            {
                float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                // The Descent Overlook is a designed viewpoint: the whole point of the pull-off
                // is the uninterrupted look down the hairpins to the lake, and the torii is its
                // landmark. A single 12 m conifer on the inland cut fills half that frame, so the
                // window is held clear on the inland side and thinned hard on the valley side.
                // Window constants mirror OverlookStart/OverlookEnd in ScatterDressing's landmark
                // block - both are provisional.
                float ovD = d - route.ClimbLength;
                bool inOverlook = ovD > OverlookClearStart && ovD < OverlookClearEnd;
                if (inOverlook && sgn < 0f) continue;
                // Open the lake-facing verge after the crest. The eye needs an uninterrupted
                // road -> guardrail -> lake -> ridge -> Fuji sequence to read the scale change;
                // a uniform blossom wall collapses all five layers into one flat pink plane.
                float treeChance = rule.TreeChance;
                if (inOverlook) treeChance *= 0.10f;
                if (sgn > 0f && section >= Section.LakeApproach)
                    treeChance *= section == Section.FujiShore ? 0.18f
                                : section == Section.LakeApproach ? 0.14f : 0.38f;
                if (rng.NextDouble() > treeChance) continue;
                treeTried++;
                triedSection[(int)section]++;

                float band = slot < 3 ? 0f : slot < 6 ? 1f : 2f;
                // Asymmetric: the valley shoulder carries the lanterns, markers and the overlook
                // railing, so trees must stand off it. The inland cut has nothing on it, and at
                // the summit the corridor is a narrow ridge - holding both sides back that far is
                // what left the viewpoint bare.
                float minOff = sgn > 0f ? rule.MinOffset : Mathf.Min(rule.MinOffset, 5.5f);
                float near = minOff + band * rule.Reach * 0.30f;
                float far = near + rule.Reach * (band == 2f ? 0.44f : 0.34f);
                float offset = sgn * Mathf.Lerp(near, far, (float)rng.NextDouble());
                var xz = p + side * offset + route.Tangent[i] * (float)(rng.NextDouble() * 3.4 - 1.7);

                var g = canQuery ? SampleGround(xz) : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                if (!g.Hit) { treeNoGround++; continue; }
                // Trees do not grow on scree or a cliff face.
                if (g.SlopeDeg > TreeMaxSlope) { treeTooSteep++; continue; }
                // ...nor in the middle of the lane one switchback below.
                if (!ClearOfRoad(xz, g.Point.y, RoadClearTree)) { treeOnRoad++; continue; }

                // Conifers are depth-biased, not evenly mixed. A flat PineRatio put dark green
                // cones right on the verge, where they masked the blossom and buried the chevron
                // sign - in a pass named for its cherry trees the near band must read pink. Pushing
                // them back also pays for itself: a pine is ~330 tris against 11-24k for a sakura,
                // so the crowded far band gets much cheaper.
                // A pine is a 12 m opaque dark cone; a sakura crown is 9 m and translucent pink.
                // At equal counts the conifers win the frame outright, so they are banned from the
                // verge entirely - a single band-0 pine at 6 m blocks a third of the rider's view -
                // and pushed into the back bands, where they read as a treeline behind the blossom.
                float pineBias = band == 0f ? 0f : band == 1f ? 0.75f : 1.9f;
                bool pine = rng.NextDouble() < Mathf.Min(rule.PineRatio * pineBias, 0.85f);
                // Section 27: of the trees that are NOT conifers, a share is drawn as green
                // broadleaf instead of sakura. This is the lever that breaks the uniform pink
                // wall, and it works precisely where conifers cannot: the verge band.
                float greenShare = band == 0f ? VergeGreenShare
                                 : band == 1f ? MidGreenShare
                                              : BackGreenShare;
                bool broadleaf = !pine && rng.NextDouble() < greenShare;
                // Saplings fill the near band, where a full 9 m crown would smother the road.
                bool sapling = !pine && (band == 0f ? rng.NextDouble() < 0.42 : rng.NextDouble() < 0.16);
                string asset = pine ? pines[rng.Next(pines.Length)]
                             : broadleaf ? (sapling ? "SakuraPass_Broadleaf_Sapling"
                                          : band > BroadleafHeroMaxBand ? broadFar[rng.Next(broadFar.Length)]
                                                                        : broadNear[rng.Next(broadNear.Length)])
                             : sapling ? "SakuraPass_Sakura_Sapling"
                                       : trees[rng.Next(trees.Length)];

                var tree = Stage(Card8Asset(asset), $"{(pine ? "Pine" : broadleaf ? (sapling ? "Broadleaf Sapling" : "Broadleaf") : sapling ? "Sakura Sapling" : "Sakura Tree")} {placed:000}", flora);
                if (tree == null) continue;
                // Sink deeper on steeper ground: the root flare is modelled as a cone around a
                // level base, so on a slope its uphill side would otherwise stand clear of the dirt.
                float sink = 0.25f + Mathf.InverseLerp(20f, TreeMaxSlope, g.SlopeDeg) * 1.1f;
                tree.transform.position = g.Point + Vector3.down * sink;
                tree.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                float s = 0.82f + (float)rng.NextDouble() * 0.45f;
                tree.transform.localScale = new Vector3(s, s * (0.92f + (float)rng.NextDouble() * 0.2f), s);
                AssignFoliage(tree, barkMat,
                              pine ? needleMat
                                   : broadleaf ? leafMats[rng.Next(leafMats.Length)]
                                               : blossomMats[rng.Next(blossomMats.Length)]);

                // A blossom canopy 25 m off the road contributes nothing to the shadow map but
                // costs a full extra pass; with this many trees that is the difference between
                // playable and not.
                if (band == 2f)
                    foreach (var r in tree.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                perSection[(int)section]++;
                if (pine) pineSection[(int)section]++;
                if (broadleaf) broadSection[(int)section]++;
                placed++;
            }

            // --- ground cover hugging the verge --------------------------------------------
            for (int slot = 0; slot < 3; slot++)
            {
                if (rng.NextDouble() > rule.CoverChance) continue;

                float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                float offset = sgn * (float)(4.4 + rng.NextDouble() * 7.0);
                var xz = p + side * offset + route.Tangent[i] * (float)(rng.NextDouble() * 2.6 - 1.3);

                var g = canQuery ? SampleGround(xz) : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                if (!g.Hit || g.SlopeDeg > 38f) continue;
                if (!ClearOfRoad(xz, g.Point.y, RoadClearProp)) continue;

                bool fern = rng.NextDouble() < 0.4;
                var clump = Stage(fern ? "SakuraPass_Fern_Clump" : "SakuraPass_Grass_Tuft",
                                  fern ? $"Fern {placed:000}" : $"Grass {placed:000}", flora);
                if (clump == null) continue;
                clump.transform.position = g.Point + Vector3.down * 0.06f;
                clump.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                float s = 0.7f + (float)rng.NextDouble() * 0.9f;
                clump.transform.localScale = new Vector3(s, s, s);
                foreach (var r in clump.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterial = grassMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                placed++;
            }

            // --- rock clusters on the steeper inland cut ------------------------------------
            if (rng.NextDouble() < rule.RockChance)
            {
                var xz = p - side * (float)(8.0 + rng.NextDouble() * 14.0);
                var g = canQuery ? SampleGround(xz) : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                // Same reasoning as the outcrops: a cluster sitting on a near-vertical cut only
                // touches the slope at one point and reads as floating, so keep them on ground a
                // boulder could actually rest on.
                float rockY = 0f, rockSpread = 0f;
                if (g.Hit && g.SlopeDeg < 34f && !IsCrest(xz, g.Point.y, 3.0f)
                    && ClearOfRoad(xz, g.Point.y, RoadClearTree)
                    && SampleFootprint(xz, 1.8f, out rockY, out rockSpread)
                    && rockSpread < 1.8f)
                {
                    var rock = Stage(rng.NextDouble() < 0.5 ? "SakuraPass_Rock_Cluster_A"
                                                            : "SakuraPass_Rock_Cluster_B",
                                     $"Rocks {placed:000}", props, stoneMat);
                    if (rock != null)
                    {
                        rock.transform.position = new Vector3(xz.x, rockY - 1.0f, xz.z);
                        rock.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                        float s = 0.75f + (float)rng.NextDouble() * 1.1f;
                        rock.transform.localScale = new Vector3(s, s * 0.9f, s);
                        placed++;
                    }
                }
            }

            // --- a cliff ledge outcrop now and then, well back from the road ----------------
            if (rng.NextDouble() < 0.05)
            {
                var xz = p - side * (float)(20.0 + rng.NextDouble() * 16.0);
                var g = canQuery ? SampleGround(xz) : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                // Outcrops need a slope that actually reads as a shelf. On a near-vertical cut the
                // downward raycast lands on a face the ledge can only touch at a single point, so
                // it hangs in the air off the cliff - hence the upper bound as well as the lower.
                // The slope test alone is not enough though: it samples a single triangle, and the
                // ledge is ~4 m across. The footprint test is what actually stops it floating.
                float ledgeY = 0f, ledgeSpread = 0f;
                bool seated = g.Hit && SampleFootprint(xz, 2.6f, out ledgeY, out ledgeSpread)
                                    && ledgeSpread < 2.4f;
                if (seated && g.SlopeDeg > 22f && g.SlopeDeg < 48f && !IsCrest(xz, g.Point.y, 3.2f)
                    && ClearOfRoad(xz, g.Point.y, RoadClearTree))
                {
                    var ledge = Stage("SakuraPass_Cliff_Ledge", $"Outcrop {placed:000}", props, stoneMat);
                    if (ledge != null)
                    {
                        // Seated on the *lowest* ground under the footprint, not the centre.
                        ledge.transform.position = new Vector3(xz.x, ledgeY - 1.2f, xz.z);
                        ledge.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                        ledge.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.6f);
                        placed++;
                    }
                }
            }

            // --- stone lanterns line the valley shoulder ------------------------------------
            if (d >= nextLantern)
            {
                nextLantern = d + 52f + (float)rng.NextDouble() * 34f;
                var xz = p + side * (route.HalfWidth + 1.9f);
                var g = canQuery ? SampleGround(xz) : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                if (g.Hit)
                {
                    var lantern = Stage("SakuraPass_Stone_Lantern", $"Lantern {placed:000}", props, stoneMat);
                    if (lantern != null)
                    {
                        lantern.transform.position = g.Point + Vector3.down * 0.08f;
                        lantern.transform.rotation = Quaternion.Euler(0f, yaw + 90f, 0f);
                        AddLanternGlow(lantern);
                        placed++;
                    }
                }
            }

            // --- retaining wall along the inland cut ----------------------------------------
            if (d >= nextWall)
            {
                nextWall = d + 26f;
                var xz = p - side * (route.HalfWidth + 2.1f);
                var g = canQuery ? SampleGround(xz) : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                // Only where the land actually rises behind the verge - a wall on flat ground
                // reads as a random fence.
                if (g.Hit && g.SlopeDeg > 26f)
                {
                    var wall = Stage("SakuraPass_Retaining_Wall", $"Retaining Wall {placed:000}",
                                     props, stoneMat);
                    if (wall != null)
                    {
                        wall.transform.position = g.Point + Vector3.down * 0.5f;
                        wall.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        placed++;
                    }
                }
            }

            // --- kilometre markers on the valley shoulder -----------------------------------
            if (d >= nextMarker)
            {
                nextMarker = d + 100f;
                var xz = p + side * (route.HalfWidth + 1.1f);
                var g = canQuery ? SampleGround(xz) : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                if (g.Hit)
                {
                    var marker = Stage("SakuraPass_Distance_Marker", $"Marker {placed:000}", props);
                    if (marker != null)
                    {
                        marker.transform.position = g.Point;
                        marker.transform.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
                        placed++;
                    }
                }
            }
        }

        // --- the shrine gate: a road-spanning torii with a lantern avenue (concept panel 03) --
        // A sixth of the *climb* - which is where it has always stood, ~88 m in, at the head of
        // the forest section. A sixth of the full route now lands halfway up the cliffside.
        int gateIndex = Mathf.Clamp(route.IndexAtClimbFraction(1f / 6f), 2, route.Count - 3);
        var gp = route.Position[gateIndex];
        var gtan = route.Tangent[gateIndex];
        float gyaw = Mathf.Atan2(gtan.x, gtan.z) * Mathf.Rad2Deg;
        var gg = canQuery ? SampleGround(gp) : new Ground { Hit = true, Point = gp, Normal = Vector3.up };
        var torii = Stage("SakuraPass_Torii_Great", "Torii Gate", props);
        if (torii != null)
        {
            // Ground is sampled at the road centre so the base sits on the carriageway; the
            // 11.6 m span then reaches clear over both verges, which is the whole point of the
            // shot. The yaw aligns the gate's local +Z with the tangent, so its span axis (local
            // X) crosses the road.
            torii.transform.position = (gg.Hit ? gg.Point : gp) + Vector3.down * 0.15f;
            torii.transform.rotation = Quaternion.Euler(0f, gyaw, 0f);
        }

        // Lanterns in mirrored pairs on the approach, tightening the framing into the gate.
        for (int k = 1; k <= 4; k++)
        {
            int idx = Mathf.Clamp(gateIndex - k * 3, 0, route.Count - 1);
            var bp = route.Position[idx];
            var btan = route.Tangent[idx];
            float byaw = Mathf.Atan2(btan.x, btan.z) * Mathf.Rad2Deg;
            var bside = route.Side[idx];
            for (int s = -1; s <= 1; s += 2)
            {
                var xz = bp + bside * (s * (route.HalfWidth + 1.6f));
                var g2 = canQuery ? SampleGround(xz)
                                  : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                if (!g2.Hit) continue;
                var av = Stage("SakuraPass_Stone_Lantern",
                               $"Gate Lantern {k}{(s < 0 ? "L" : "R")}", props, stoneMat);
                if (av == null) continue;
                av.transform.position = g2.Point + Vector3.down * 0.08f;
                av.transform.rotation = Quaternion.Euler(0f, byaw + 90f, 0f);
                AddLanternGlow(av);
                placed++;
            }
        }

        // --- banner poles flanking the gate (concept panel 03) --------------------------------
        for (int k = 0; k <= 1; k++)
        {
            int idx = Mathf.Clamp(gateIndex + (k == 0 ? -6 : 5), 0, route.Count - 1);
            var bp = route.Position[idx];
            var byaw2 = Mathf.Atan2(route.Tangent[idx].x, route.Tangent[idx].z) * Mathf.Rad2Deg;
            for (int s = -1; s <= 1; s += 2)
            {
                var xz = bp + route.Side[idx] * (s * (route.HalfWidth + 3.1f));
                var g3 = canQuery ? SampleGround(xz)
                                  : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                if (!g3.Hit) continue;
                var pole = Stage("SakuraPass_Banner_Pole",
                                 $"Gate Banner {k}{(s < 0 ? "L" : "R")}", props);
                if (pole == null) continue;
                pole.transform.position = g3.Point + Vector3.down * 0.1f;
                // Banners hang on the pole's +X side, so face each pair inward over the road.
                pole.transform.rotation = Quaternion.Euler(0f, byaw2 + (s < 0 ? 0f : 180f), 0f);
                placed++;
            }
        }

        // --- chevron boards on the sharpest bends (concept panel 06) --------------------------
        // Picking the sharpest bends by measurement beats gating on an authored arc-length band:
        // the route geometry can be re-sculpted without the signs drifting off the corners.
        var bends = new System.Collections.Generic.List<(int i, float turn)>();
        for (int i = 1; i < route.Count - 1; i++)
            bends.Add((i, Vector3.Angle(route.Tangent[i], route.Tangent[i + 1])));
        bends.Sort((a, b) => b.turn.CompareTo(a.turn));

        var takenBends = new System.Collections.Generic.List<int>();
        foreach (var (i, turn) in bends)
        {
            // The descent is twice as long as the climb and turns far more, so the budget scales
            // with route length rather than sitting at the five the climb alone needed.
            if (takenBends.Count >= 10) break;
            if (turn < 2.5f) break;                       // straight enough to need no warning
            if (takenBends.Exists(j => Mathf.Abs(j - i) < 10)) continue;   // ~28 m apart

            // The change in tangent points into the curve, so its negation is the outside of the
            // bend - which is where a chevron board belongs, and is convention-free.
            var delta = route.Tangent[i + 1] - route.Tangent[i];
            delta.y = 0f;
            if (delta.sqrMagnitude < 1e-6f) continue;
            var outward = -delta.normalized;

            var xz = route.Position[i] + outward * (route.HalfWidth + 2.0f);
            var g4 = canQuery ? SampleGround(xz)
                              : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
            if (!g4.Hit || g4.SlopeDeg > 40f) continue;

            var sign = StageTrimSign("SakuraPass_Chevron_Sign", $"Chevron {takenBends.Count}", props);
            if (sign == null) continue;
            sign.transform.position = g4.Point + Vector3.down * 0.1f;
            // Face back down the road so the rider reads the board head-on on the approach.
            sign.transform.rotation = Quaternion.Euler(
                0f, Mathf.Atan2(-route.Tangent[i].x, -route.Tangent[i].z) * Mathf.Rad2Deg, 0f);
            // The arrows are authored pointing along the board's local +X. Which way that ends up
            // facing depends on the sign's yaw *and* on handedness, so rather than hard-code a
            // convention, measure it: mirror the board when its +X does not already point into
            // the curve. Unity's built-in pipeline flips cull winding for negatively scaled
            // renderers, so a -1 on one axis is safe here.
            if (Vector3.Dot(sign.transform.right, delta) < 0f)
                sign.transform.localScale = new Vector3(-1f, 1f, 1f);
            takenBends.Add(i);
            placed++;
            Debug.Log($"[sakura] chevron at route sample {i}/{route.Count} (turn {turn:0.0} deg)");
        }

        // --- the pass signboard at the summit (concept panel 07) ------------------------------
        // Anchored to the climb, not to total route length: with the descent added, 0.90 of the
        // route is now most of the way down the far side.
        int signIndex = Mathf.Clamp(route.IndexAtClimbFraction(0.90f), 0, route.Count - 1);
        var sxz = route.Position[signIndex] + route.Side[signIndex] * (route.HalfWidth + 3.3f);
        var sg = canQuery ? SampleGround(sxz)
                          : new Ground { Hit = true, Point = sxz, Normal = Vector3.up };
        var summitSign = StageTrimSign("SakuraPass_Summit_Sign", "Summit Signboard", props);
        if (summitSign != null)
        {
            summitSign.transform.position = (sg.Hit ? sg.Point : sxz) + Vector3.down * 0.12f;
            // Faces back down the road, not across it: the board is there to be read on the
            // approach, and side-on it is a 90 mm edge.
            summitSign.transform.rotation = Quaternion.Euler(
                0f, Mathf.Atan2(-route.Tangent[signIndex].x, -route.Tangent[signIndex].z) * Mathf.Rad2Deg, 0f);
            placed++;
        }

        // --- the summit overlook railing (concept panel 07) ---------------------------------
        // A continuous run on the valley side, set back behind the verge. Each copy is 6.4 m of
        // local +X, so stepping by the *measured* spacing between the chosen samples and yawing
        // to the local tangent keeps the run unbroken through the curve rather than fanning open.
        var railMat = CelMaterial("SakuraPass_RailTimber", new Color(0.46f, 0.33f, 0.24f, 1f),
                                  gloss: 0.14f, spec: 0.06f, rim: 0.5f,
                                  texture: LoadTexture("Sakura_Bark_Albedo.png"));
        int railStart = Mathf.Clamp(route.IndexAtClimbFraction(0.93f), 2, route.Count - 3);
        // Runs to the end of the level crest shelf, ~55 m past the summit, rather than to the end
        // of the route - which is now a valley shelf 590 m down the far side.
        int railEnd = Mathf.Clamp(route.IndexAt(route.ClimbLength + 55f), railStart + 1, route.Count - 3);
        float railRun = 6.4f;
        float travelled = railRun;   // trip the first placement immediately
        Vector3 lastRail = Vector3.zero;
        int rails = 0;
        for (int i = railStart; i <= railEnd; i++)
        {
            // Set back well clear of the steel guardrail, which already occupies the verge -
            // otherwise the two run through each other.
            var rxz = route.Position[i] + route.Side[i] * (route.HalfWidth + 3.6f);
            if (rails > 0) travelled += Vector3.Distance(
                new Vector3(rxz.x, 0f, rxz.z), new Vector3(lastRail.x, 0f, lastRail.z));
            if (travelled < railRun * 0.94f) continue;

            var rg = canQuery ? SampleGround(rxz) : new Ground { Hit = true, Point = rxz, Normal = Vector3.up };
            if (!rg.Hit) { lastRail = rxz; travelled = 0f; continue; }

            var rail = Stage("SakuraPass_Overlook_Railing", $"Overlook Railing {rails:00}", props, railMat);
            if (rail != null)
            {
                rail.transform.position = rg.Point + Vector3.down * 0.20f;
                // Local +X runs along the rail, so yaw is the tangent turned 90 degrees.
                rail.transform.rotation = Quaternion.Euler(
                    0f, Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg + 90f, 0f);
                rails++;
                placed++;
            }
            lastRail = rxz;
            travelled = 0f;
        }
        Debug.Log($"Sakura Pass: summit overlook railing - {rails} sections from sample {railStart}.");

        // --- the descent overlook: a scenic pull-off above the first hairpin ------------------
        // The crest viewpoint is where the rider *arrives*; this is where they get their first
        // look down the switchbacks they are about to ride, so it is dressed as a real 展望台
        // pull-off: a railed terrace on the drop side, a pair of stone lanterns marking its ends
        // and a small torii set back on the inland side for a footpath shrine.
        //
        // All four numbers below are provisional staging values, not design requirements. The
        // arc-length window is measured past the summit and was chosen against the current
        // control points (hairpin 1 apex sits at ~166 m past the summit); re-measure it if the
        // descent is ever re-cut.
        const float OverlookStart = 128f;     // provisional: end of the first descending limb
        const float OverlookEnd = 166f;       // provisional: the hairpin apex itself
        int ovStart = Mathf.Clamp(route.IndexAt(route.ClimbLength + OverlookStart), 2, route.Count - 3);
        int ovEnd = Mathf.Clamp(route.IndexAt(route.ClimbLength + OverlookEnd), ovStart + 1, route.Count - 3);
        int ovRails = 0;
        float ovTravelled = railRun;
        Vector3 ovLast = Vector3.zero;
        for (int i = ovStart; i <= ovEnd; i++)
        {
            var rxz = route.Position[i] + route.Side[i] * (route.HalfWidth + 3.6f);
            if (ovRails > 0) ovTravelled += Vector3.Distance(
                new Vector3(rxz.x, 0f, rxz.z), new Vector3(ovLast.x, 0f, ovLast.z));
            if (ovTravelled < railRun * 0.94f) continue;

            var rg = canQuery ? SampleGround(rxz) : new Ground { Hit = true, Point = rxz, Normal = Vector3.up };
            if (!rg.Hit) { ovLast = rxz; ovTravelled = 0f; continue; }

            var rail = Stage("SakuraPass_Overlook_Railing", $"Descent Overlook Railing {ovRails:00}",
                             props, railMat);
            if (rail != null)
            {
                rail.transform.position = rg.Point + Vector3.down * 0.20f;
                rail.transform.rotation = Quaternion.Euler(
                    0f, Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg + 90f, 0f);
                ovRails++;
                placed++;
            }
            ovLast = rxz;
            ovTravelled = 0f;
        }

        // A lantern at each end of the terrace, on the valley shoulder inside the railing.
        foreach (int i in new[] { ovStart, ovEnd })
        {
            int idx = Mathf.Clamp(i, 0, route.Count - 1);
            var lxz = route.Position[idx] + route.Side[idx] * (route.HalfWidth + 2.0f);
            var lg = canQuery ? SampleGround(lxz) : new Ground { Hit = true, Point = lxz, Normal = Vector3.up };
            if (!lg.Hit) continue;
            var lantern = Stage("SakuraPass_Stone_Lantern",
                                $"Descent Overlook Lantern {(i == ovStart ? "A" : "B")}", props, stoneMat);
            if (lantern == null) continue;
            lantern.transform.position = lg.Point + Vector3.down * 0.08f;
            lantern.transform.rotation = Quaternion.Euler(
                0f, Mathf.Atan2(route.Tangent[idx].x, route.Tangent[idx].z) * Mathf.Rad2Deg + 90f, 0f);
            AddLanternGlow(lantern);
            placed++;
        }

        // The footpath torii on the inland cut, opposite the middle of the terrace. Set well back
        // so it frames a path into the hillside instead of overhanging the carriageway.
        {
            int idx = Mathf.Clamp((ovStart + ovEnd) / 2, 0, route.Count - 1);
            var txz = route.Position[idx] - route.Side[idx] * (route.HalfWidth + 4.4f);
            var tg = canQuery ? SampleGround(txz) : new Ground { Hit = true, Point = txz, Normal = Vector3.up };
            if (tg.Hit)
            {
                var gate = Stage("SakuraPass_Torii_Gate", "Descent Overlook Torii", props,
                                 CelMaterial("SakuraPass_Vermilion", new Color(0.44f, 0.11f, 0.08f, 1f),
                                             gloss: 0.42f, spec: 0.22f, rim: 0.22f));
                if (gate != null)
                {
                    gate.transform.position = tg.Point + Vector3.down * 0.10f;
                    // The span runs along local X, so yaw to the tangent presents the opening to
                    // a rider coming down the limb rather than the side of the gate.
                    gate.transform.rotation = Quaternion.Euler(
                        0f, Mathf.Atan2(route.Tangent[idx].x, route.Tangent[idx].z) * Mathf.Rad2Deg, 0f);
                    placed++;
                }
            }
        }
        Debug.Log($"Sakura Pass: descent overlook - {ovRails} railing sections from sample {ovStart}.");

        Debug.Log($"[trees] {treeTried} attempts, {treeTooSteep} rejected as too steep " +
                  $"(> {TreeMaxSlope} deg), {treeNoGround} found no ground, " +
                  $"{treeOnRoad} rejected inside the road corridor.");
        for (int s = 0; s < perSection.Length; s++)
            Debug.Log($"[trees]   {(Section)s,-15} {perSection[s],4} placed / {triedSection[s],4} tried" +
                      $"  ({pineSection[s],3} pine, {broadSection[s],4} broadleaf, " +
                      $"{perSection[s] - pineSection[s] - broadSection[s],4} sakura)");
        {
            // Section 27 target: 40-50% sakura / 30-40% green (conifer + broadleaf). Logged as a
            // measured ratio so the composition is verified, not assumed.
            int tp = 0, tb = 0, tt = 0;
            for (int s = 0; s < perSection.Length; s++)
            { tt += perSection[s]; tp += pineSection[s]; tb += broadSection[s]; }
            int ts = tt - tp - tb;
            if (tt > 0)
                Debug.Log($"[trees] COMPOSITION over {tt} trees: sakura {100f * ts / tt:0.0}% " +
                          $"| green {100f * (tp + tb) / tt:0.0}% (broadleaf {100f * tb / tt:0.0}%, " +
                          $"conifer {100f * tp / tt:0.0}%)  [section 27 target ~45% / ~35%]");
        }

        Debug.Log($"Sakura Pass: scattered {placed} authored props/flora along {route.Length:0} m of route " +
                  $"({GroundQueries} ground queries, {GroundMisses} missed the terrain).");

        // A high miss rate means the terrain is not being hit at all - usually inverted face
        // winding on the exported tiles - and every prop would silently vanish or sink.
        if (GroundQueries > 0 && GroundMisses > GroundQueries / 4)
            Debug.LogWarning($"Sakura Pass: {GroundMisses} of {GroundQueries} ground queries missed the " +
                             "terrain collider. Check the terrain GLB's face winding.");
    }

    /// <summary>
    /// Trunk geometry gets the cel bark material; blossom/leaf cards get the alpha-cut foliage
    /// shader with wind. Without the split, the canopy cards render as opaque rectangles.
    /// </summary>
    /// <summary>
    /// Dressing for the three expansion segments.
    ///
    /// Deliberately a separate, simpler pass from <see cref="ScatterDressing"/>: that one carries
    /// the pass's one-off landmarks (the great torii, the chevron set, the summit signboard, the
    /// overlook railing) and a seven-section concept-board map that only describes the pass. The
    /// new roads need biome character, not those landmarks, so they get their own rule table and
    /// share every staged asset and material.
    ///
    /// Densities are PROVISIONAL and deliberately below the pass's: the pass is the hero, and
    /// 5.3 km of new road at the pass's ~1 object/m would have tripled the scene on its own.
    /// </summary>
    private struct ExpansionRule
    {
        public float TreeChance;      // per slot, 4 slots per sample
        public float PineRatioLow;    // conifer share at the bottom of the segment
        public float PineRatioHigh;   // ...and at the top
        public float SaplingChance;
        public float CoverChance;
        public float RockChance;
        public float MinOffset;
        public float Reach;
        public float LanternEvery;
        public float MarkerEvery;
        public float MaxSlope;
    }

    private static readonly Dictionary<string, ExpansionRule> ExpansionRules =
        new Dictionary<string, ExpansionRule>
        {
            // Open water, reeds and low stone walls - the deliberate anti-forest contrast to the
            // climb, and the recovery half of every lap.
            ["s1"] = new ExpansionRule
            {
                TreeChance = 0.30f, PineRatioLow = 0.02f, PineRatioHigh = 0.06f,
                SaplingChance = 0.34f, CoverChance = 0.66f, RockChance = 0.07f,
                MinOffset = 6.5f, Reach = 26f, LanternEvery = 88f, MarkerEvery = 200f,
                MaxSlope = 52f,
            },
            // Conifer treeline giving way to scree and a bare shelf at the Cloudline Shrine.
            ["aozora"] = new ExpansionRule
            {
                TreeChance = 0.46f, PineRatioLow = 0.35f, PineRatioHigh = 0.88f,
                SaplingChance = 0.16f, CoverChance = 0.34f, RockChance = 0.34f,
                MinOffset = 6.0f, Reach = 24f, LanternEvery = 150f, MarkerEvery = 250f,
                MaxSlope = 58f,
            },
            // Farmland and rice terraces on the false flat down to the hub gate.
            ["maple"] = new ExpansionRule
            {
                TreeChance = 0.40f, PineRatioLow = 0.06f, PineRatioHigh = 0.06f,
                SaplingChance = 0.30f, CoverChance = 0.72f, RockChance = 0.05f,
                MinOffset = 7.0f, Reach = 28f, LanternEvery = 120f, MarkerEvery = 200f,
                MaxSlope = 46f,
            },
        };

    private static void ScatterExpansionDressing(Transform root,
                                                 Dictionary<string, SakuraRoute> segments,
                                                 GameObject ground)
    {
        var parent = root.Find("Route Dressing");
        if (parent == null)
        {
            parent = new GameObject("Route Dressing").transform;
            parent.SetParent(root, false);
        }
        var flora = parent.Find("Expansion Flora");
        if (flora == null)
        {
            flora = new GameObject("Expansion Flora").transform;
            flora.SetParent(parent, false);
        }
        var props = parent.Find("Expansion Props");
        if (props == null)
        {
            props = new GameObject("Expansion Props").transform;
            props.SetParent(parent, false);
        }

        var barkMat = CelMaterial("SakuraPass_Bark", Color.white, gloss: 0.12f, spec: 0.06f,
                                  rim: 0.5f, texture: LoadTexture("Sakura_Bark_Albedo.png"));
        var blossomMat = FoliageMaterial("SakuraPass_Blossom", new Color(0.94f, 0.66f, 0.74f, 1f),
                                         LoadTexture("Sakura_Blossom_Atlas.png"), wind: 0.18f);
        var grassMat = FoliageMaterial("SakuraPass_GroundCover", new Color(0.86f, 1f, 0.80f, 1f),
                                       LoadTexture("Sakura_Leaf_Atlas.png"), wind: 0.26f);
        var stoneMat = CelMaterial("SakuraPass_Stone", Color.white, gloss: 0.16f, spec: 0.10f,
                                   rim: 0.9f, texture: LoadTexture("Sakura_Rock_Albedo.png"));
        var needleMat = CelMaterial("SakuraPass_Needle", new Color(0.26f, 0.40f, 0.28f, 1f),
                                    gloss: 0.10f, spec: 0.04f, rim: 0.45f);

        string[] trees = { "SakuraPass_Sakura_Tree_A", "SakuraPass_Sakura_Tree_B",
                           "SakuraPass_Sakura_Tree_C" };
        string[] pines = { "SakuraPass_Pine_A", "SakuraPass_Pine_B" };
        string[] cover = { "SakuraPass_Grass_Tuft", "SakuraPass_Fern_Clump" };
        string[] rocks = { "SakuraPass_Rock_Cluster_A", "SakuraPass_Rock_Cluster_B" };
        // Section 27 green broadleaf + crown-tint palettes (see ScatterDressing for the rationale).
        string[] broadNear = { "SakuraPass_Broadleaf_A", "SakuraPass_Broadleaf_B" };
        string[] broadFar = { "SakuraPass_Broadleaf_A", "SakuraPass_Broadleaf_B", "SakuraPass_Broadleaf_C" };
        var leafMats = new Material[BroadleafTints.Length];
        for (int t = 0; t < BroadleafTints.Length; t++)
            leafMats[t] = FoliageMaterial($"SakuraPass_BroadleafCrown_{t}", BroadleafTints[t],
                                          LoadTexture("Sakura_Leaf_Atlas.png"), wind: 0.22f);
        var blossomMats = new Material[BlossomTints.Length];
        for (int t = 0; t < BlossomTints.Length; t++)
            blossomMats[t] = BlossomCrownMaterial(t);

        int totalTrees = 0, totalCover = 0, totalRocks = 0, totalProps = 0;

        foreach (var kv in segments)
        {
            if (kv.Key == "pass") continue;
            if (!ExpansionRules.TryGetValue(kv.Key, out var rule)) continue;
            var route = kv.Value;
            if (route.Count < 6) continue;

            // Fixed per-segment seed: layouts stay identical and diffable between builds.
            var rng = new System.Random(20260913 ^ kv.Key.GetHashCode());
            float nextLantern = rule.LanternEvery * 0.5f;
            float nextMarker = rule.MarkerEvery * 0.5f;
            int segTrees = 0;

            for (int i = 2; i < route.Count - 2; i++)
            {
                var p = route.Position[i];
                var side = route.Side[i];
                float yaw = Mathf.Atan2(route.Tangent[i].x, route.Tangent[i].z) * Mathf.Rad2Deg;
                float d = route.Distance[i];
                float altitude = route.Length <= 1f ? 0f : d / route.Length;

                // --- trees ------------------------------------------------------------
                for (int slot = 0; slot < 4; slot++)
                {
                    if (rng.NextDouble() > rule.TreeChance) continue;
                    float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float band = slot < 2 ? 0f : slot < 3 ? 1f : 2f;
                    float near = rule.MinOffset + band * rule.Reach * 0.32f;
                    float far = near + rule.Reach * 0.34f;
                    float offset = sgn * Mathf.Lerp(near, far, (float)rng.NextDouble());
                    var xz = p + side * offset +
                             route.Tangent[i] * (float)(rng.NextDouble() * 3.0 - 1.5);

                    var g = SampleGround(xz);
                    if (!g.Hit || g.SlopeDeg > rule.MaxSlope) continue;
                    if (!ClearOfRoad(xz, g.Point.y, RoadClearTree)) continue;

                    // Conifers take over with altitude on the Aozora face and are pushed to the
                    // back bands everywhere - a 12 m opaque cone on the verge owns the frame.
                    float pineRatio = Mathf.Lerp(rule.PineRatioLow, rule.PineRatioHigh, altitude);
                    float pineBias = band == 0f ? 0f : band == 1f ? 0.8f : 1.9f;
                    bool pine = rng.NextDouble() < Mathf.Min(pineRatio * pineBias, 0.9f);
                    // Section 27: same green-broadleaf lever as the main pass. The 's1' valley
                    // rule runs PineRatioLow 0.02, so without this the whole warm-up corridor
                    // is a solid pink tunnel for its entire length.
                    float greenShare = band == 0f ? VergeGreenShare
                                     : band == 1f ? MidGreenShare
                                                  : BackGreenShare;
                    bool broadleaf = !pine && rng.NextDouble() < greenShare;
                    bool sapling = !pine && rng.NextDouble() < rule.SaplingChance;
                    string asset = pine ? pines[rng.Next(pines.Length)]
                                 : broadleaf ? (sapling ? "SakuraPass_Broadleaf_Sapling"
                                              : band > BroadleafHeroMaxBand ? broadFar[rng.Next(broadFar.Length)]
                                                                            : broadNear[rng.Next(broadNear.Length)])
                                 : sapling ? "SakuraPass_Sakura_Sapling"
                                           : trees[rng.Next(trees.Length)];

                    var tree = Stage(Card8Asset(asset), $"{kv.Key} {(pine ? "Pine" : broadleaf ? "Broadleaf" : "Sakura")} {segTrees:000}",
                                     flora);
                    if (tree == null) continue;
                    float sink = 0.25f + Mathf.InverseLerp(20f, rule.MaxSlope, g.SlopeDeg) * 1.1f;
                    tree.transform.position = g.Point + Vector3.down * sink;
                    tree.transform.rotation =
                        Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                    float s = 0.80f + (float)rng.NextDouble() * 0.42f;
                    tree.transform.localScale =
                        new Vector3(s, s * (0.92f + (float)rng.NextDouble() * 0.2f), s);
                    AssignFoliage(tree, barkMat,
                                  pine ? needleMat
                                       : broadleaf ? leafMats[rng.Next(leafMats.Length)]
                                                   : blossomMats[rng.Next(blossomMats.Length)]);
                    if (band == 2f)
                        foreach (var r in tree.GetComponentsInChildren<Renderer>(true))
                            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    segTrees++;
                    totalTrees++;
                }

                // --- ground cover ------------------------------------------------------
                for (int slot = 0; slot < 2; slot++)
                {
                    if (rng.NextDouble() > rule.CoverChance) continue;
                    float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float offset = sgn * (4.6f + (float)rng.NextDouble() * 6.6f);
                    var xz = p + side * offset +
                             route.Tangent[i] * (float)(rng.NextDouble() * 3.0 - 1.5);
                    var g = SampleGround(xz);
                    if (!g.Hit || g.SlopeDeg > 38f) continue;
                    if (!ClearOfRoad(xz, g.Point.y, RoadClearProp)) continue;

                    var c = Stage(cover[rng.Next(cover.Length)], $"{kv.Key} Cover {totalCover:0000}",
                                  flora, grassMat);
                    if (c == null) continue;
                    c.transform.position = g.Point + Vector3.down * 0.06f;
                    c.transform.rotation =
                        Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                    float cs = 0.8f + (float)rng.NextDouble() * 0.6f;
                    c.transform.localScale = Vector3.one * cs;
                    foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    totalCover++;
                }

                // --- rock clusters ------------------------------------------------------
                if (rng.NextDouble() < rule.RockChance)
                {
                    float offset = -(8f + (float)rng.NextDouble() * 14f);
                    var xz = p + side * offset;
                    var g = SampleGround(xz);
                    if (g.Hit && g.SlopeDeg < 36f && !IsCrest(xz, g.Point.y, 2.2f) &&
                        SampleFootprint(xz, 1.8f, out float lowest, out float spread) &&
                        spread < 1.8f && ClearOfRoad(xz, g.Point.y, RoadClearProp))
                    {
                        var rock = Stage(rocks[rng.Next(rocks.Length)],
                                         $"{kv.Key} Rocks {totalRocks:000}", props, stoneMat);
                        if (rock != null)
                        {
                            rock.transform.position = new Vector3(xz.x, lowest - 0.35f, xz.z);
                            rock.transform.rotation =
                                Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                            totalRocks++;
                        }
                    }
                }

                // --- roadside furniture --------------------------------------------------
                if (d >= nextLantern)
                {
                    nextLantern = d + rule.LanternEvery * (0.8f + (float)rng.NextDouble() * 0.5f);
                    var xz = p + side * (route.HalfWidth + 1.5f);
                    var g = SampleGround(xz);
                    if (g.Hit && g.SlopeDeg < 34f)
                    {
                        var lantern = Stage("SakuraPass_Stone_Lantern",
                                            $"{kv.Key} Lantern {totalProps:000}", props, stoneMat);
                        if (lantern != null)
                        {
                            lantern.transform.position = g.Point + Vector3.down * 0.08f;
                            lantern.transform.rotation = Quaternion.Euler(0f, yaw + 90f, 0f);
                            AddLanternGlow(lantern);
                            totalProps++;
                        }
                    }
                }

                if (d >= nextMarker)
                {
                    nextMarker = d + rule.MarkerEvery;
                    var xz = p + side * (route.HalfWidth + 1.1f);
                    var g = SampleGround(xz);
                    if (g.Hit)
                    {
                        var marker = Stage("SakuraPass_Distance_Marker",
                                           $"{kv.Key} Marker {totalProps:000}", props);
                        if (marker != null)
                        {
                            marker.transform.position = g.Point;
                            marker.transform.rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
                            totalProps++;
                        }
                    }
                }
            }

            Debug.Log($"[expansion-dressing] {kv.Key}: {segTrees} trees over {route.Length:0} m");
        }

        Debug.Log($"[expansion-dressing] total {totalTrees} trees, {totalCover} cover, " +
                  $"{totalRocks} rock clusters, {totalProps} props.");
    }

    // ------------------------------------------------------- section 28: verge / road edge

    /// <summary>
    /// Dresses the narrow band immediately outboard of the shoulder on every road in the
    /// network - the strip the rider's camera looks straight down and the one place the
    /// existing cover pass could never reach (see EdgeBandInner for why).
    ///
    /// Section 28's rule is that "asphalt ends -> perfect grass texture begins" must not be
    /// readable as a line. Two things break it here: flat terrain-like pieces (moss/soil
    /// patches and half-buried gravel) laid right against the chip seal so the boundary is
    /// made of *material* rather than props, and taller irregular pieces (flowers, low shrubs,
    /// grass) further out so the band has a broken profile instead of a mown edge.
    ///
    /// Runs after both scatter passes and before ChunkDressing, so the verge streams with
    /// everything else.
    /// </summary>
    private static void ScatterEdgeDressing(Transform root,
                                            Dictionary<string, SakuraRoute> segments,
                                            GameObject ground)
    {
        var parent = root.Find("Route Dressing");
        if (parent == null)
        {
            parent = new GameObject("Route Dressing").transform;
            parent.SetParent(root, false);
        }
        // Idempotent: exact-name match, and the previous pass is destroyed outright rather
        // than appended to, or a rebuild doubles the verge density every time.
        var old = parent.Find("Verge Dressing");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var verge = new GameObject("Verge Dressing").transform;
        verge.SetParent(parent, false);

        var rng = new System.Random(20260913);
        bool canQuery = ground != null;

        // --- materials -------------------------------------------------------------------
        // Flat pieces are opaque cel surfaces (they are pretending to be terrain); the
        // upright pieces are alpha foliage cards.
        var mossMat = CelMaterial("SakuraPass_VergeMoss", new Color(0.42f, 0.45f, 0.31f, 1f),
                                  gloss: 0.06f, spec: 0.02f, rim: 0.25f,
                                  texture: LoadTexture("Sakura_Rock_Albedo.png"));
        var soilMat = CelMaterial("SakuraPass_VergeSoil", new Color(0.50f, 0.42f, 0.36f, 1f),
                                  gloss: 0.05f, spec: 0.02f, rim: 0.22f,
                                  texture: LoadTexture("Sakura_Rock_Albedo.png"));
        var gravelMat = CelMaterial("SakuraPass_VergeGravel", new Color(0.60f, 0.58f, 0.56f, 1f),
                                    gloss: 0.14f, spec: 0.08f, rim: 0.6f,
                                    texture: LoadTexture("Sakura_Rock_Albedo.png"));
        // Moss and soil are flat OPEN discs. recalc_normals on an open disc can settle on
        // either face, and the cel material is back-face culled by default - the first pass
        // placed thousands of these and none of them appeared in the render while the closed
        // stone meshes beside them rendered fine. Double-siding removes the question entirely;
        // they are ground-hugging, so there is no silhouette cost.
        mossMat.SetFloat("_Cull", 0f);
        soilMat.SetFloat("_Cull", 0f);
        var shrubMat = FoliageMaterial("SakuraPass_VergeShrub", new Color(0.60f, 0.78f, 0.48f, 1f),
                                       LoadTexture("Sakura_Leaf_Atlas.png"), wind: 0.30f);
        var grassMat = FoliageMaterial("SakuraPass_GroundCover", new Color(0.86f, 1f, 0.80f, 1f),
                                       LoadTexture("Sakura_Leaf_Atlas.png"), wind: 0.26f);
        var flowerMats = new Material[EdgeFlowerTints.Length];
        for (int t = 0; t < EdgeFlowerTints.Length; t++)
            flowerMats[t] = FoliageMaterial($"SakuraPass_VergeFlower_{t}", EdgeFlowerTints[t],
                                            LoadTexture("Sakura_Blossom_Atlas.png"), wind: 0.24f);

        string[] mossAssets = { "SakuraPass_Moss_Patch_A", "SakuraPass_Moss_Patch_B" };
        var splatMat = GroundSplatMaterial();
        var splatRng = new System.Random(4242);          // own stream: keeps verge placement draws unchanged
        string[] stoneAssets = { "SakuraPass_Stone_Scatter_A", "SakuraPass_Stone_Scatter_B" };
        string[] flowerAssets =
        {
            "SakuraPass_Flower_Clump_A", "SakuraPass_Flower_Clump_B", "SakuraPass_Flower_Clump_C",
        };
        string[] shrubAssets = { "SakuraPass_Low_Shrub_A", "SakuraPass_Low_Shrub_B" };

        int flat = 0, upright = 0, tried = 0, rejected = 0;

        foreach (var kv in segments)
        {
            var route = kv.Value;
            bool isMain = kv.Key == "pass";
            for (int i = 2; i < route.Count - 2; i++)
            {
                var p = route.Position[i];
                var side = route.Side[i];
                float d = route.Distance[i];

                // Nothing inside the bore: the verge there is 6 m of rock.
                if (isMain && d > TunnelStart - 8f && d < TunnelEnd + 8f) continue;

                for (int slot = 0; slot < EdgeSlotsPerSample; slot++)
                {
                    if (rng.NextDouble() > EdgeChance) continue;
                    tried++;

                    float sgn = rng.NextDouble() < 0.5 ? -1f : 1f;
                    // Square-rooted so the density is weighted toward the asphalt rather than
                    // spread evenly across the band - section 28's "near-road density".
                    float t01 = (float)(1.0 - Math.Sqrt(rng.NextDouble()));
                    float offset = sgn * Mathf.Lerp(EdgeBandInner, EdgeBandOuter, t01);
                    var xz = p + side * offset +
                             route.Tangent[i] * (float)(rng.NextDouble() * 1.6 - 0.8);

                    var g = canQuery ? SampleGround(xz)
                                     : new Ground { Hit = true, Point = xz, Normal = Vector3.up };
                    if (!g.Hit || g.SlopeDeg > EdgeMaxSlope) { rejected++; continue; }
                    if (!ClearOfRoad(xz, g.Point.y, EdgeClearance)) { rejected++; continue; }

                    bool inner = t01 < EdgeInnerFraction;
                    string asset;
                    Material mat;
                    float sink;
                    if (inner)
                    {
                        // Against the chip seal: flat material, not props.
                        double r = rng.NextDouble();
                        if (r < 0.44)
                        {
                            asset = mossAssets[rng.Next(mossAssets.Length)];
                            mat = rng.NextDouble() < 0.4 ? soilMat : mossMat;
                            // E4 critic fix: irregular alpha-clipped soil/moss splats replace the
                            // round discs (same rng draws, so the rest of the verge is unchanged).
                            if (UseGroundSplats && splatMat != null)
                            {
                                int cell = (mat == soilMat ? 0 : 2) + (asset.EndsWith("_B") ? 1 : 0);
                                asset = $"SakuraPass_Ground_Splat_{cell}";
                                mat = splatMat;
                            }
                            // Negative = lifted. Aligned to the ground normal these sit flush;
                            // a centimetre proud keeps them off the terrain's z-buffer.
                            sink = -0.012f;
                        }
                        else if (r < 0.80)
                        {
                            asset = stoneAssets[rng.Next(stoneAssets.Length)];
                            mat = gravelMat;
                            sink = -0.008f;
                        }
                        else
                        {
                            asset = flowerAssets[rng.Next(flowerAssets.Length)];
                            mat = flowerMats[rng.Next(flowerMats.Length)];
                            sink = 0.04f;
                        }
                    }
                    else
                    {
                        double r = rng.NextDouble();
                        if (r < 0.38)
                        {
                            asset = flowerAssets[rng.Next(flowerAssets.Length)];
                            mat = flowerMats[rng.Next(flowerMats.Length)];
                            sink = 0.04f;
                        }
                        else if (r < 0.72)
                        {
                            asset = shrubAssets[rng.Next(shrubAssets.Length)];
                            mat = shrubMat;
                            sink = 0.06f;
                        }
                        else
                        {
                            asset = "SakuraPass_Grass_Tuft";
                            mat = grassMat;
                            sink = 0.06f;
                        }
                    }

                    var go = Stage(asset, $"Verge {(flat + upright):0000}", verge, mat);
                    if (go == null) { rejected++; continue; }
                    go.transform.position = g.Point + Vector3.down * sink;
                    // Flat pieces MUST follow the ground normal. Pinned to world up, a 0.85 m
                    // moss disc on the verge (which falls away from the road) buries its uphill
                    // half and floats its downhill half - the first pass placed 5477 of these
                    // and almost none of them were visible in the render. Upright pieces stay
                    // vertical, partially tilted toward the slope so they don't look planted.
                    float yaw = (float)(rng.NextDouble() * 360.0);
                    var align = Quaternion.FromToRotation(Vector3.up, g.Normal);
                    go.transform.rotation = inner
                        ? align * Quaternion.Euler(0f, yaw, 0f)
                        : Quaternion.Slerp(Quaternion.identity, align, 0.35f) *
                          Quaternion.Euler(0f, yaw, 0f);
                    float s = 0.72f + (float)rng.NextDouble() * 0.72f;
                    go.transform.localScale = new Vector3(s, s, s);
                    if (mat == splatMat && splatMat != null)
                        go.transform.localScale = new Vector3(s * (0.8f + (float)splatRng.NextDouble() * 0.6f), 1f,
                                                              s * (0.6f + (float)splatRng.NextDouble() * 0.5f));
                    foreach (var r2 in go.GetComponentsInChildren<Renderer>(true))
                    {
                        r2.sharedMaterial = mat;
                        // Nothing in this band is tall enough for its shadow to be worth a
                        // shadow-map draw, and the canopy dapple from section 24 is the cue
                        // that matters on the verge.
                        r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    if (inner) flat++; else upright++;
                }
            }
        }

        Debug.Log($"[verge] section 28: {flat} flat transition pieces (moss/soil/gravel), " +
                  $"{upright} upright pieces (flowers/shrubs/grass), " +
                  $"{flat + upright} total from {tried} attempts ({rejected} rejected).");
    }

    /// <summary>
    /// Buckets every piece of dressing into a <c>Chunk_####</c> group by nearest route arc, and
    /// wires the runtime streamer to them.
    ///
    /// Chunking is the streaming unit: the rider's arc position is known exactly, so a chunk can
    /// be switched off with certainty rather than guessed at by a frustum test. Chunk centres and
    /// radii are baked here so nothing walks thousands of renderers at load time.
    /// </summary>
    private static void ChunkDressing(Transform root, Dictionary<string, SakuraRoute> segments,
                                      Transform rider)
    {
        const float ChunkMetres = 110f;      // PROVISIONAL

        var dressing = root.Find("Route Dressing");
        if (dressing == null) return;

        // Flatten every existing group into one list of props.
        var props = new List<Transform>();
        foreach (Transform group in dressing)
        {
            if (group.name.StartsWith("Chunk_")) continue;
            foreach (Transform prop in group) props.Add(prop);
        }
        if (props.Count == 0) return;

        // Build the chunk key space: one bucket per ChunkMetres of every segment's centreline.
        var keys = new List<(string seg, int idx, Vector3 centre)>();
        foreach (var kv in segments)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(kv.Value.Length / ChunkMetres));
            for (int c = 0; c < n; c++)
            {
                float d = (c + 0.5f) * ChunkMetres;
                int i = Mathf.Clamp(kv.Value.IndexAt(d), 0, kv.Value.Count - 1);
                keys.Add((kv.Key, c, kv.Value.Position[i]));
            }
        }

        var buckets = new Dictionary<string, Transform>();
        var members = new Dictionary<string, List<Transform>>();

        foreach (var prop in props)
        {
            var p = prop.position;
            float best = float.MaxValue;
            string bestKey = null;
            Vector3 bestCentre = Vector3.zero;
            foreach (var k in keys)
            {
                float dx = k.centre.x - p.x, dz = k.centre.z - p.z, dy = k.centre.y - p.y;
                float sq = dx * dx + dz * dz + dy * dy * 0.25f;
                if (sq >= best) continue;
                best = sq;
                bestKey = $"Chunk_{k.seg}_{k.idx:0000}";
                bestCentre = k.centre;
            }
            if (bestKey == null) continue;

            if (!buckets.TryGetValue(bestKey, out var bucket))
            {
                var go = new GameObject(bestKey);
                go.transform.SetParent(dressing, false);
                go.transform.position = bestCentre;
                bucket = go.transform;
                buckets[bestKey] = bucket;
                members[bestKey] = new List<Transform>();
            }
            members[bestKey].Add(prop);
        }

        foreach (var kv in members)
            foreach (var prop in kv.Value)
                prop.SetParent(buckets[kv.Key], true);

        // Drop the now-empty original groups so the hierarchy stays honest.
        for (int i = dressing.childCount - 1; i >= 0; i--)
        {
            var child = dressing.GetChild(i);
            if (!child.name.StartsWith("Chunk_") && child.childCount == 0)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        // Bake centres + radii for the streamer.
        var chunks = new List<RouteDressingStreamer.Chunk>();
        foreach (var kv in buckets)
        {
            var rends = kv.Value.GetComponentsInChildren<MeshRenderer>(true);
            if (rends.Length == 0) continue;
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            chunks.Add(new RouteDressingStreamer.Chunk
            {
                root = kv.Value,
                center = b.center,
                radius = b.extents.magnitude,
            });
        }

        var host = GameObject.Find("MapleRide Ride");
        if (host == null) host = new GameObject("MapleRide Ride");
        var streamer = host.GetComponent<RouteDressingStreamer>();
        if (streamer == null) streamer = host.AddComponent<RouteDressingStreamer>();
        streamer.chunks = chunks.ToArray();
        streamer.rider = rider;
        // Diagnostic captures are wide shots from far off the route; streaming them would render
        // an empty world. It is turned on by RideBootstrap when a ride actually starts.
        streamer.streamingEnabled = false;

        Debug.Log($"[chunking] {props.Count} dressing props into {chunks.Count} chunks " +
                  $"(~{ChunkMetres:0} m of route each).");
    }

    /// <summary>
    /// Authors the two strong local wind reads called out by the weather handoff instead of
    /// asking the generic terrain heuristic to infer them: Kawabe's enclosed lake basin is
    /// sheltered (0.6x), while the Sakura and Aozora crests are exposed (1.3x). The rest of the
    /// network deliberately has no boxes and therefore continues to use TerrainExposure().
    /// </summary>
    private static void BuildWindVolumes(Transform root,
                                         IReadOnlyDictionary<string, SakuraRoute> segments)
    {
        var parent = new GameObject("Wind Volumes").transform;
        parent.SetParent(root, false);

        int sheltered = 0, exposed = 0;
        if (segments.TryGetValue("s1", out var kawabe))
            sheltered += BuildWindZone(parent, kawabe, 520f, 1540f, 0.6f, "Valley Kawabe");

        if (segments.TryGetValue("pass", out var pass))
            exposed += BuildWindZone(parent, pass, 390f, 670f, 1.3f, "Ridge Sakura");

        if (segments.TryGetValue("aozora", out var aozora))
            exposed += BuildWindZone(parent, aozora, 2150f, aozora.Length, 1.3f, "Ridge Aozora");

        Debug.Log($"[sakura] wind volumes: {sheltered} sheltered x0.60, {exposed} exposed x1.30");
    }

    /// <summary>
    /// Places overlapping route-aligned boxes. A 75 m step with a 125 m box leaves enough
    /// interior on both neighbours for the 18 m edge blend to stay at full authored strength;
    /// only the entrance and exit of a zone fade back to the terrain-derived value.
    /// </summary>
    private static int BuildWindZone(Transform parent, SakuraRoute route, float fromM, float toM,
                                     float multiplier, string label)
    {
        const float StepM = 75f;
        const float BoxLengthM = 125f;
        const float BoxWidthM = 60f;
        const float BoxHeightM = 120f;
        const float EdgeBlendM = 18f;

        fromM = Mathf.Clamp(fromM, 0f, route.Length);
        toM = Mathf.Clamp(toM, fromM, route.Length);
        int count = 0;
        float last = float.NegativeInfinity;

        for (float d = fromM; d <= toM + 0.01f; d += StepM)
        {
            AddWindVolume(parent, route, d, multiplier, label, BoxWidthM, BoxHeightM,
                          BoxLengthM, EdgeBlendM, count++);
            last = d;
        }

        // Cap the far boundary when the step does not land on it, avoiding a short un-authored
        // gap before the zone fades out.
        if (toM - last > 0.01f)
            AddWindVolume(parent, route, toM, multiplier, label, BoxWidthM, BoxHeightM,
                          BoxLengthM, EdgeBlendM, count++);
        return count;
    }

    private static void AddWindVolume(Transform parent, SakuraRoute route, float distanceM,
                                      float multiplier, string label, float widthM, float heightM,
                                      float lengthM, float edgeBlendM, int ordinal)
    {
        int i = Mathf.Clamp(route.IndexAt(distanceM), 0, route.Count - 1);
        Vector3 p = route.Position[i];
        Vector3 fwd = route.Tangent[i];
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;

        var go = new GameObject($"{label} {ordinal:00} {distanceM:0}m x{multiplier:0.00}");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(widthM, heightM, lengthM);
        var volume = go.AddComponent<WeatherWindVolume>();
        volume.windMultiplier = multiplier;
        volume.edgeBlendM = edgeBlendM;
    }

    /// <summary>
    /// Stages the ride foundation: device layer, physics, ride session, checkpoints, the
    /// route follower on the player and the HUD/GPS canvas.
    ///
    /// Idempotent by construction - it looks the host up by EXACT name ("MapleRide Ride"),
    /// reuses the components already on it, and PUSHES values onto those serialized instances
    /// rather than relying on field initialisers, which do nothing to an object already saved
    /// into the scene.
    /// </summary>
    private static void SetupRideSystems(RouteGraph graph)
    {
        if (graph == null)
        {
            Debug.LogWarning("[ride] no RouteGraph - skipping ride system setup.");
            return;
        }

        // --- host ---------------------------------------------------------------------
        var hosts = UnityEngine.Object.FindObjectsByType<RideBootstrap>(FindObjectsInactive.Include,
                                                                       FindObjectsSortMode.None);
        for (int i = 1; i < hosts.Length; i++)
        {
            Debug.Log($"[ride] pruning duplicate ride host '{hosts[i].name}'.");
            UnityEngine.Object.DestroyImmediate(hosts[i].gameObject);
        }

        var host = GameObject.Find(RideBootstrap.RootName);
        if (host == null) host = new GameObject(RideBootstrap.RootName);
        host.transform.SetParent(null);
        host.transform.position = Vector3.zero;

        var devices = host.GetComponent<DeviceManager>() ?? host.AddComponent<DeviceManager>();
        var session = host.GetComponent<RideSession>() ?? host.AddComponent<RideSession>();
        var director = host.GetComponent<RouteDirector>() ?? host.AddComponent<RouteDirector>();
        var hud = host.GetComponent<RideHud>() ?? host.AddComponent<RideHud>();
        var boot = host.GetComponent<RideBootstrap>() ?? host.AddComponent<RideBootstrap>();

        // --- push serialized values (code defaults never reach a saved instance) --------
        devices.source = DeviceManager.SourceKind.Simulator;
        devices.ftpWatts = 220f;             // PROVISIONAL - FTP setup is unresolved (context 12)
        devices.riderMassKg = 68f;           // PROVISIONAL
        devices.acceptKeyboardEffort = true;
        // Mash to ride: the player produces every watt. PROVISIONAL tuning, pushed onto the
        // serialized instance because code defaults never reach a saved scene object.
        devices.effortSource = DeviceManager.EffortSource.MashPedalStrokes;
        devices.mash.strokeKey = KeyCode.UpArrow;
        devices.mash.strokeKeyAlt = KeyCode.W;
        devices.mash.keyboardHoldEnabled = true;
        devices.mash.keyboardHoldKey = KeyCode.UpArrow;
        devices.mash.keyboardHoldWatts = 220f;
        devices.mash.keyboardHoldRampSeconds = 0.45f;
        devices.mash.keyboardReleaseSeconds = 0.55f;
        devices.mash.keyboardHoldCadenceRpm = 88f;
        devices.mash.scoutHoldMode = false;  // production default; RideBootstrap forces the QA flythrough on at runtime
        devices.mash.wattsPerStroke = 70f;   // PROVISIONAL
        devices.mash.decaySeconds = 0.85f;   // PROVISIONAL
        devices.mash.maxWatts = 650f;        // PROVISIONAL

        session.devices = devices;
        session.courseId = "sakura_circuit";
        session.autoLapsFromTarget = true;
        session.targetDurationMinutes = 30f; // PROVISIONAL default session length
        session.gradeWindowM = 8f;           // PROVISIONAL
        session.gradeDisplayClampPct = 20f;  // PROVISIONAL
        session.physics.riderMassKg = 68f;
        session.physics.bikeMassKg = 8.5f;
        session.physics.cdA = 0.32f;
        session.physics.crr = 0.005f;
        session.physics.descentCapKph = 58f; // PROVISIONAL braking cap

        director.session = session;
        director.arrivalRadiusM = 12f;       // PROVISIONAL
        director.bannerSeconds = 4f;

        hud.session = session;
        hud.director = director;
        hud.devices = devices;

        boot.session = session;
        boot.devices = devices;
        boot.director = director;
        boot.hud = hud;
        boot.shortRideMinutes = 30f;
        boot.longRideMinutes = 60f;
        boot.routeFollowing = true;

        // --- the player --------------------------------------------------------------
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player != null)
        {
            var follower = player.GetComponent<RouteFollower>() ?? player.AddComponent<RouteFollower>();
            follower.session = session;
            follower.rider = player.transform;
            // THE SINGLE SHARED LINE - the same one every ambient rider uses. This used to be a
            // keep-left -1.7 m, which put the player on one side of the road and Sakura's
            // oncoming roster on the other, and meant the two never had to acknowledge each
            // other. Sharing the line is what makes an oncoming rider's step-aside legible and
            // what gives the player somebody to actually go around.
            follower.laneOffset = TrafficLine.SharedLineM;

            // The lateral steer, so the player can pull out and pass the same-direction riders
            // the roster now includes. Previously off for Sakura on the grounds that there was
            // no same-lane traffic to pass; there is now.
            follower.laneChangeEnabled = true;
            follower.useArrowKeysForLaneChange = false;
            follower.passShiftM = TrafficLine.PassShiftM;
            follower.laneChangeSpeedMps = TrafficLine.ShiftSpeedMps;
            follower.heightOffset = 0.02f;
            follower.bankBlend = 0.85f;
            boot.follower = follower;
            boot.rider = player.transform;

            // The riding posture driver (climb out of the saddle >= 4 %, sprint tuck on S).
            // Staged here, on the same object as the rig and the follower, so a saved scene
            // already has it: a component only added at runtime is a component that is missing
            // from every capture and every build.
            var pose = player.GetComponent<KuroRidePose>() ?? player.AddComponent<KuroRidePose>();
            pose.session = session;
            pose.rig = player.GetComponentInChildren<KuroBikeRig>(true);
            pose.climbGradeThreshold = 0.04f;   // PROVISIONAL: out of the saddle at 4 %
            pose.climbGradeFull = 0.065f;       // PROVISIONAL
            pose.sprintKey = KeyCode.S;
            // The posture owner also stages the player-only cockpit sockets/visual. It prunes by
            // exact name and never edits the imported/shared bike, so a full environment rebuild
            // cannot lose the four-pose system or leak duplicate aero bars.
            KuroCyclingPostureSetup.ConfigurePlayer(player, true);
            EditorUtility.SetDirty(pose);

            // Free roam and route following are mutually exclusive owners of the transform.
            // Disabled, never deleted, so [F] still gives the original prototype back.
            foreach (var mb in new MonoBehaviour[]
                     {
                         player.GetComponent<KuroKeyboardController>(),
                         player.GetComponent<KuroRoadSafety>(),
                         player.GetComponent<KuroRoadGrounding>(),
                     })
                if (mb != null) mb.enabled = false;

            // Seat the rider on the course in the saved scene, so it does not sit at the world
            // origin (which in this project means floating over the lake) before play starts.
            session.Graph = null;
            session.EnsureCourse();
            session.ResetRide();
            follower.Apply();
            Debug.Log($"[ride] player seated at {player.transform.position} " +
                      $"on '{session.Course?.DisplayName}'.");
        }
        else
        {
            Debug.LogWarning("[ride] 'Kuro on Sakura Pass' not found - route follower not staged.");
        }

        var cam = GameObject.Find("Sakura Camera");
        boot.rideCamera = cam != null ? cam.GetComponent<Camera>() : Camera.main;

        // The HUD canvas is rebuilt from scratch by RideHud.Build() at runtime; leaving a stale
        // one serialized into the scene is exactly the accumulate-instead-of-converge bug this
        // pass exists to avoid.
        for (int i = host.transform.childCount - 1; i >= 0; i--)
        {
            var c = host.transform.GetChild(i);
            if (c.name == RideHud.CanvasName) UnityEngine.Object.DestroyImmediate(c.gameObject);
        }

        Debug.Log($"[ride] ride systems staged: {graph.courses.Length} courses, " +
                  $"{graph.segments.Length} segments, default '{session.courseId}' " +
                  $"{session.Course?.Length / 1000f:0.00} km x {session.TotalLaps} laps " +
                  $"(~{session.EstimateLapMinutes():0.0} min/lap).");
    }

    // ---- E4 / huddle idea 1 (2026-09-30): alpha-clip 8-island blossom atlas + baked normal ----
    // Built by tools/blender/build_sakura_photoreal_petals.py. The *_Card8 trees carry ONE canopy
    // mesh/material (trunk + canopy per tree). Same rng draws as before, so placement is unchanged.
    // PROVISIONAL toggle: set false to fall back to the old 2x2 atlas trees.
    public static bool UseCard8Blossom = true;
    private const string Card8Albedo = "Sakura_Blossom_Atlas8.png";
    private const string Card8Normal = "Sakura_Blossom_Atlas8_Normal.png";
    private const string FoliageNormalShaderName = "MapleRide/HDRP/FoliageNormal";
    // PROVISIONAL art tuning: raised from 1.0 (Agent HQ 'flat noisy cotton canopy' - the baked
    // petal normal was wired but too subtle under this route's warm/golden grade to break up the
    // canopy silhouette at gameplay distance; paired with a deeper CROWN_AO_MIN in
    // build_sakura_photoreal_petals.py for the vertex-colour interior darkening).
    private const float Card8NormalStrength = 1.7f;

    private static string Card8Asset(string asset)
    {
        if (!UseCard8Blossom) return asset;
        if (asset != "SakuraPass_Sakura_Tree_A" && asset != "SakuraPass_Sakura_Tree_B" &&
            asset != "SakuraPass_Sakura_Tree_C" && asset != "SakuraPass_Sakura_Sapling") return asset;
        string c8 = asset + "_Card8";
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetDir}/{c8}.glb") != null ? c8 : asset;
    }

    private static Material BlossomCrownMaterial(int t)
    {
        var shader = Shader.Find(FoliageNormalShaderName);
        var alb = LoadTexture(Card8Albedo);
        if (!UseCard8Blossom || shader == null || alb == null)
            return FoliageMaterial($"SakuraPass_BlossomCrown_{t}", BlossomTints[t],
                                   LoadTexture("Sakura_Blossom_Atlas.png"), wind: 0.18f);
        string name = $"SakuraPass_BlossomCard8_{t}";
        if (MaterialCache.TryGetValue(name, out var cached) && cached != null) return cached;
        ConfigureCard8Importers();
        var mat = LoadOrCreate(name, FoliageNormalShaderName);
        mat.SetColor("_Color", BlossomTints[t]);
        mat.SetFloat("_Cutoff", 0.45f);
        mat.SetFloat("_Translucency", 1.0f);
        mat.SetColor("_TransColor", new Color(1f, 0.60f, 0.74f, 1f));
        mat.SetFloat("_WindStrength", 0.18f);
        mat.SetTexture("_MainTex", LoadTexture(Card8Albedo));
        mat.SetTexture("_BumpMap", LoadTexture(Card8Normal));
        mat.SetFloat("_BumpScale", Card8NormalStrength);
        if (mat.HasProperty("_EdgeOnFade")) mat.SetFloat("_EdgeOnFade", CanopyEdgeOnFade);
        EditorUtility.SetDirty(mat);
        MaterialCache[name] = mat;
        return mat;
    }

    /// <summary>PROVISIONAL (QA 2026-09-30): 1 = fully fade rim/backlight and clip near-edge-on
    /// canopy cards (the bright crosshatch slivers); 0 = legacy look.</summary>
    private const float CanopyEdgeOnFade = 1f;

    private static void ConfigureCard8Importers()
    {
        string dir = "Assets/Environment/SakuraPass/Textures";
        if (AssetImporter.GetAtPath($"{dir}/{Card8Albedo}") is TextureImporter a &&
            (!a.mipMapsPreserveCoverage || !a.alphaIsTransparency))
        {
            a.alphaIsTransparency = true;
            a.mipMapsPreserveCoverage = true;     // alpha-clip canopy must not thin out with distance
            a.alphaTestReferenceValue = 0.45f;
            a.SaveAndReimport();
        }
        if (AssetImporter.GetAtPath($"{dir}/{Card8Normal}") is TextureImporter n &&
            n.textureType != TextureImporterType.NormalMap)
        {
            n.textureType = TextureImporterType.NormalMap;
            n.SaveAndReimport();
        }
    }

    // ---- E4 / huddle batch 2 (2026-09-30): ideas 5, 7, 8, 9, 10 + art-critic Sakura fixes ----
    // Assets: tools/textures/make_sakura_photoreal_textures.py, tools/blender/build_sakura_photoreal_props.py.
    // Every toggle and number below is PROVISIONAL art tuning, not a design requirement.
    public static bool UseTrimSigns = true;        // idea 8 + critic "blank roadside sign"
    public static bool UseGroundSplats = true;     // critic "circular ground patches"
    public static bool UseRoadEdgeDecal = true;    // idea 5
    public static bool UseMacroTunnel = true;      // idea 9
    public static bool UseRangeLods = true;        // idea 7
    private const float EdgeDecalInner = 0.20f;    // m outside HalfWidth (over the chip-seal shoulder)
    private const float EdgeDecalOuter = 1.90f;    // m outside HalfWidth (into the grass)
    private const float EdgeDecalRepeatM = 4.0f;   // m of road per texture repeat
    private const float EdgeDecalLift = 0.03f;
    private const float EdgeDecalMaxStep = 0.6f;   // ground this far off the road edge = cliff/hairpin: cut the strip
    private const float MacroScale = 0.05f;        // repeats per metre (one 20 m macro tile)
    private const float MacroAmount = 0.85f;       // bumped from 0.55 (QA: tunnel wall read as flat/noiseless)
    private const float RangeLod1Distance = 2400f; // m: sector switches to its 40 % Decimate
    private const float RangeLod2Distance = 3800f; // m: sector switches to its 12 % Decimate
    private const float RangeLodFovDeg = 60f;

    private static readonly Dictionary<string, (string tex, float strength)> DetailNormals =
        new Dictionary<string, (string, float)>
        {
            { "SakuraPass_Bark", ("Sakura_Bark_Normal.png", 0.8f) },
            { "SakuraPass_RailTimber", ("Sakura_Wood_Normal.png", 0.7f) },
            { "SakuraPass_RailWood", ("Sakura_Wood_Normal.png", 0.7f) },
            { "SakuraPass_SignTrim", ("Sakura_Sign_Trim_Normal.png", 0.6f) },
        };

    private static void EnsureImporter(string file, bool normal, bool srgb = true,
                                       TextureWrapMode? wrapV = null,
                                       bool preserveAlphaCoverage = false,
                                       float alphaTestReference = 0.5f)
    {
        string path = $"Assets/Environment/SakuraPass/Textures/{file}";
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        bool dirty = false;
        if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (!normal && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
        if (wrapV.HasValue && ti.wrapModeV != wrapV.Value) { ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = wrapV.Value; dirty = true; }
        if (preserveAlphaCoverage && (!ti.alphaIsTransparency || !ti.mipMapsPreserveCoverage ||
                                      !Mathf.Approximately(ti.alphaTestReferenceValue, alphaTestReference)))
        {
            ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = alphaTestReference;
            dirty = true;
        }
        if (dirty) ti.SaveAndReimport();
    }

    private static void ApplyDetailNormal(Material mat, string name)
    {
        if (!DetailNormals.TryGetValue(name, out var dn) || !mat.HasProperty("_NormalMap")) return;
        EnsureImporter(dn.tex, normal: true);
        var tex = LoadTexture(dn.tex);
        if (tex == null) return;
        mat.SetTexture("_NormalMap", tex);
        mat.SetFloat("_NormalStrength", dn.strength);
    }

    private static Material SignTrimMaterial()
    {
        var alb = LoadTexture("Sakura_Sign_Trim.png");
        if (alb == null) return null;
        var mat = CelMaterial("SakuraPass_SignTrim", Color.white, gloss: 0.16f, spec: 0.10f, rim: 0.30f,
                              texture: alb);
        Weather(mat, Weathering.Signage, amount: 0.5f);   // legible first: half the signage preset
        return mat;
    }

    private static GameObject StageTrimSign(string asset, string display, Transform parent)
    {
        var trim = UseTrimSigns && LoadBlenderAsset(asset + "_Trim") != null ? SignTrimMaterial() : null;
        if (trim == null) return Stage(asset, display, parent);
        return Stage(asset + "_Trim", display, parent, trim, keepTextures: false);
    }

    private static Material GroundSplatMaterial()
    {
        if (!UseGroundSplats || LoadBlenderAsset("SakuraPass_Ground_Splat_0") == null) return null;
        var tex = LoadTexture("Sakura_Ground_Splat.png");
        if (tex == null) return null;
        // Keep the authored multi-lobed silhouette and broad feather instead of letting
        // alpha-mip filtering turn the splat into a hard, stamped island at verge distance.
        // Coverage preservation keeps the partial-alpha edge stable as it minifies over grass.
        EnsureImporter("Sakura_Ground_Splat.png", normal: false, srgb: true,
                       wrapV: TextureWrapMode.Clamp,
                       preserveAlphaCoverage: true, alphaTestReference: 0.08f);
        var mat = FoliageMaterial("SakuraPass_GroundSplat", new Color(0.86f, 0.86f, 0.84f, 1f), tex, wind: 0f);
        // The feather is intentionally sampled below the foliage default so the
        // irregular outer pixels survive alpha clipping instead of drawing a crisp ring.
        // The texture deliberately carries a long grass-coloured alpha fringe. A lower
        // cutoff preserves that transition at oblique verge distance; the old 0.18 value
        // removed the feather and exposed a perfect circular stamp.
        mat.SetFloat("_Cutoff", 0.08f);
        mat.SetFloat("_Translucency", 0f);    // ground, not a backlit leaf
        mat.SetFloat("_RimStrength", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void ApplyMacroVariation(Material mat)
    {
        if (!UseMacroTunnel || mat == null) return;
        var sh = Shader.Find("MapleRide/HDRP/CelLitMacro");
        var tex = LoadTexture("Sakura_Macro_Variation.png");
        if (sh == null || tex == null) return;
        EnsureImporter("Sakura_Macro_Variation.png", normal: false, srgb: false);
        mat.shader = sh;                       // same property block as CelLit + three macro props
        mat.SetTexture("_MacroTex", tex);
        mat.SetFloat("_MacroScale", MacroScale);
        mat.SetFloat("_MacroAmount", MacroAmount);
        // The bore interior sits in near-zero ambient, where every lighting term downstream is
        // albedo-multiplied, so the macro grain was invisible: darkening an already ~0.02 linear
        // albedo by ambient*0.3-ish stays indistinguishable from 0 black on screen (QA/fix-implementer
        // finding: tunnel wall reads as flat noiseless black, not a textured bore). A small
        // per-channel floor below the material's own base colour (same trick already used for
        // near-black character kit) guarantees a non-zero base for the macro multiply to modulate,
        // so the overlay reads as visible low-res grain even where direct light never reaches.
        var baseCol = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.black;
        mat.SetColor("_MatteFloor", new Color(baseCol.r * 0.40f, baseCol.g * 0.40f, baseCol.b * 0.40f, 1f));
        EditorUtility.SetDirty(mat);
    }

    private static void LightLanternHead(GameObject lantern)
    {
        var glow = LoadOrCreate("SakuraPass_LanternEmissive", "Unlit/Color");
        glow.SetColor("_Color", new Color(1f, 0.80f, 0.50f, 1f));
        foreach (var r in lantern.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.name.IndexOf("glow", StringComparison.OrdinalIgnoreCase) < 0) continue;
            r.sharedMaterial = glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>Idea 5: a draped, alpha-clipped dirt strip over the asphalt/grass seam on both
    /// sides of every road segment. Needs the terrain collider, so it runs inside Apply.</summary>
    private static void BuildRoadEdgeDecal(Transform root, Dictionary<string, SakuraRoute> segments,
                                           GameObject ground)
    {
        for (int c = root.childCount - 1; c >= 0; c--)
            if (root.GetChild(c).name == "Road Edge Decal") UnityEngine.Object.DestroyImmediate(root.GetChild(c).gameObject);
        if (!UseRoadEdgeDecal || ground == null) return;
        var tex = LoadTexture("Sakura_EdgeDirt_Decal.png");
        if (tex == null) return;
        // Preserve the authored feather through mip reduction. Without coverage
        // preservation the alpha-clipped strip hardens into a straight brown band
        // at ride distance, defeating the dirt-to-grass blend this decal is for.
        EnsureImporter("Sakura_EdgeDirt_Decal.png", normal: false, wrapV: TextureWrapMode.Clamp,
                       preserveAlphaCoverage: true, alphaTestReference: 0.40f);

        var mat = FoliageMaterial("SakuraPass_RoadEdgeDirt", new Color(0.92f, 0.90f, 0.87f, 1f), tex, wind: 0f);
        mat.SetFloat("_Cutoff", 0.40f);
        mat.SetFloat("_Translucency", 0f);
        mat.SetFloat("_RimStrength", 0f);
        EditorUtility.SetDirty(mat);

        var group = new GameObject("Road Edge Decal").transform;
        group.SetParent(root, false);
        float[] offs = { EdgeDecalInner, 0.55f, 1.0f, EdgeDecalOuter };
        float[] vs = { 0f, 0.22f, 0.5f, 1f };
        int quads = 0;
        foreach (var kv in segments)
        {
            var route = kv.Value;
            bool isMain = kv.Key == "pass";
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                int prev = -1;
                for (int i = 0; i < route.Count; i++)
                {
                    float d = route.Distance[i];
                    bool ok = !(isMain && d > TunnelStart - 2f && d < TunnelEnd + 2f);
                    var p = route.Position[i];
                    var side = route.Side[i];
                    var ring = new Vector3[offs.Length];
                    for (int k = 0; ok && k < offs.Length; k++)
                    {
                        var xz = p + side * (sgn * (route.HalfWidth + offs[k]));
                        float roadY = p.y - 0.045f * Mathf.Clamp01(offs[k] / 0.55f);
                        var g = SampleGround(xz);
                        float y = roadY;
                        if (g.Hit)
                        {
                            if (Mathf.Abs(g.Point.y - roadY) > EdgeDecalMaxStep) { ok = false; break; }
                            y = Mathf.Max(g.Point.y, roadY);
                        }
                        else if (k > 1) { ok = false; break; }
                        ring[k] = new Vector3(xz.x, y + EdgeDecalLift, xz.z);
                    }
                    if (!ok) { prev = -1; continue; }
                    int b = verts.Count;
                    for (int k = 0; k < offs.Length; k++)
                    {
                        verts.Add(ring[k]);
                        uvs.Add(new Vector2(d / EdgeDecalRepeatM, vs[k]));
                    }
                    if (prev >= 0)
                        for (int k = 0; k < offs.Length - 1; k++)
                        {
                            tris.Add(prev + k); tris.Add(b + k); tris.Add(b + k + 1);
                            tris.Add(prev + k); tris.Add(b + k + 1); tris.Add(prev + k + 1);
                            quads++;
                        }
                    prev = b;
                }
                if (tris.Count == 0) continue;
                var mesh = new Mesh { name = $"RoadEdgeDecal_{kv.Key}_{(sgn < 0 ? "L" : "R")}" };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                // double-sided foliage shader: fix any downward normals so lighting reads the top
                var nrm = mesh.normals;
                for (int n = 0; n < nrm.Length; n++) if (nrm[n].y < 0f) nrm[n] = -nrm[n];
                mesh.normals = nrm;
                mesh.RecalculateBounds();
                var go = new GameObject(mesh.name);
                go.transform.SetParent(group, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        Debug.Log($"[sakura] road edge decal: {quads} quads over {segments.Count} segments");
    }

    /// <summary>Idea 7: one LODGroup per range sector (LOD0 full, LOD1/LOD2 Decimate collapse).</summary>
    private static void BuildRangeLodGroups(Transform ranges)
    {
        var bySector = new Dictionary<string, Renderer[]>();
        foreach (var r in ranges.GetComponentsInChildren<Renderer>(true))
        {
            int at = r.name.LastIndexOf("_LOD", StringComparison.Ordinal);
            if (at < 0 || !int.TryParse(r.name.Substring(at + 4), out int lod) || lod < 0 || lod > 2) continue;
            string key = r.name.Substring(0, at);
            if (!bySector.TryGetValue(key, out var arr)) bySector[key] = arr = new Renderer[3];
            arr[lod] = r;
        }
        float tanHalf = Mathf.Tan(RangeLodFovDeg * 0.5f * Mathf.Deg2Rad);
        int groups = 0;
        foreach (var kv in bySector)
        {
            var rs = kv.Value;
            if (rs[0] == null || rs[1] == null || rs[2] == null) continue;
            var host = new GameObject(kv.Key).transform;
            host.SetParent(ranges, false);
            foreach (var r in rs) r.transform.SetParent(host, true);
            var lg = host.gameObject.AddComponent<LODGroup>();
            lg.fadeMode = LODFadeMode.None;
            lg.SetLODs(new[] { new LOD(0.5f, new[] { rs[0] }), new LOD(0.1f, new[] { rs[1] }), new LOD(0f, new[] { rs[2] }) });
            lg.RecalculateBounds();
            float size = lg.size;
            float h1 = size * 0.5f / (RangeLod1Distance * tanHalf);
            float h2 = size * 0.5f / (RangeLod2Distance * tanHalf);
            h1 = Mathf.Clamp(h1, 0.02f, 0.99f); h2 = Mathf.Clamp(h2, 0.01f, h1 * 0.95f);
            lg.SetLODs(new[] { new LOD(h1, new[] { rs[0] }), new LOD(h2, new[] { rs[1] }), new LOD(0f, new[] { rs[2] }) });
            groups++;
        }
        Debug.Log($"[sakura] distant range LOD groups: {groups} sectors x 3 LODs");
    }
    // ---- E4 / huddle idea 2 (2026-09-30): instanced guardrail segment ----
    // tools/blender/build_sakura_photoreal_guardrail.py writes the segment GLB and the
    // placements (stride 10: pos, forward, up, chord length). The source builder
    // enforces a <=300-triangle post-bevel budget; keep this toggle for fallback.
    public static bool UseInstancedGuardrail = true;

    [System.Serializable] private class GuardrailPlacements { public int stride; public float segEffective; public float[] d; }

    private static bool StageInstancedGuardrail(Transform parent, Material steel)
    {
        var asset = LoadBlenderAsset("SakuraPass_Guardrail_Seg");
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Environment/SakuraPass/SakuraGuardrailPlacements.json");
        if (asset == null || json == null) return false;
        var mf = asset.GetComponentInChildren<MeshFilter>(true);
        if (mf == null || mf.sharedMesh == null) return false;
        var data = JsonUtility.FromJson<GuardrailPlacements>(json.text);
        if (data == null || data.d == null || data.stride != 10) return false;

        // idempotent: exact-name match, prune every duplicate
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).name == "Guardrail") UnityEngine.Object.DestroyImmediate(parent.GetChild(i).gameObject);
        var group = new GameObject("Guardrail").transform;
        group.SetParent(parent, false);
        steel.enableInstancing = true;
        var mesh = mf.sharedMesh;
        int n = data.d.Length / 10;
        for (int k = 0; k < n; k++)
        {
            int o = k * 10;
            var pos = new Vector3(data.d[o], data.d[o + 1], data.d[o + 2]);
            var fwd = new Vector3(data.d[o + 3], data.d[o + 4], data.d[o + 5]);
            var up = new Vector3(data.d[o + 6], data.d[o + 7], data.d[o + 8]);
            float len = data.d[o + 9];
            var go = new GameObject($"Guardrail Seg {k:0000}");
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
            go.transform.localScale = new Vector3(1f, 1f, Mathf.Max(0.15f, len / data.segEffective));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = steel;
        }
        Debug.Log($"[sakura] instanced guardrail: {n} segments x {mesh.triangles.Length / 3} tris");
        return true;
    }

    private static void AssignFoliage(GameObject tree, Material bark, Material blossom)
    {
        foreach (var r in tree.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = (mats[i] != null ? mats[i].name : r.gameObject.name).ToLowerInvariant();
                bool leafy = n.Contains("blossom") || n.Contains("leaf") || n.Contains("canopy") ||
                             n.Contains("petal") || n.Contains("cluster") || n.Contains("needle");
                mats[i] = leafy ? blossom : bark;
            }
            r.sharedMaterials = mats;
            if (r.gameObject.name.ToLowerInvariant().Contains("cluster"))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        }
    }

    /// <summary>A dim warm point light inside each lantern - the reason to ride at dusk.</summary>
    private static void AddLanternGlow(GameObject lantern)
    {
        var go = new GameObject("Lantern Glow");
        go.transform.SetParent(lantern.transform, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.72f, 0.38f, 1f);
        l.intensity = 2.1f;
        l.range = 7.5f;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForceVertex;
        // Agent HQ: baked light probes along the road. Mixed keeps every existing real-time
        // effect exactly as before (this light still lights nearby dynamic props/rider every
        // frame) and ADDITIONALLY lets a probe bake pick up its bounce as a warm pool in the
        // light-probe network for whatever's passing through - a genuinely additive change.
        l.lightmapBakeType = LightmapBakeType.Mixed;
        LightLanternHead(lantern);                       // E4 huddle idea 10
    }

    // ------------------------------------------------------------ Agent HQ: lamp probes

    /// <summary>
    /// A continuous line of light probes down the carriageway (two lateral offsets, two heights),
    /// so any dynamic object riding through (the player, NPCs - none of them are lightmapped)
    /// samples the interpolated SH9 from <c>LightingSakuraCel</c>'s <c>ShadeSH9</c> call instead
    /// of only the flat global ambient probe. On its own this group is inert data waiting to be
    /// baked; see <see cref="BakeRoadLightProbes"/>, which is a deliberately separate, rare step -
    /// baking every routine Apply() would make iteration unworkably slow.
    /// </summary>
    private static void BuildRoadLightProbes(Transform root, Dictionary<string, SakuraRoute> segments)
    {
        const string name = "Road Light Probes";
        var old = root.Find(name);
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

        const float spacingM = 14f;
        const float lateralM = 2.6f;      // inside the 3.5 m carriageway half-width
        float[] heights = { 0.4f, 2.1f }; // rider/bike height, lantern-head height

        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var group = go.AddComponent<LightProbeGroup>();

        var positions = new List<Vector3>();
        foreach (var kv in segments)
        {
            var seg = kv.Value;
            if (seg.Count < 2) continue;
            float next = 0f;
            for (int i = 0; i < seg.Count; i++)
            {
                if (seg.Distance[i] < next) continue;
                next += spacingM;
                var p = seg.Position[i];
                var side = seg.Side[i];
                var up = seg.Up[i];
                foreach (var h in heights)
                {
                    positions.Add(p + up * h - side * lateralM);
                    positions.Add(p + up * h + side * lateralM);
                }
            }
        }
        group.probePositions = positions.ToArray();
        Debug.Log($"[light-probes] staged {positions.Count} probe positions along the road network.");
    }

    /// <summary>
    /// Bakes light probes only (no lightmaps - nothing in this kit is marked Lightmap Static, only
    /// Receive: Light Probes, so there is no UV2 pass to run). Deliberately a separate, rare menu
    /// item / -executeMethod target rather than part of Apply(): a full Apply() run must stay fast
    /// for iteration, and this scene's ~90+ Mixed lantern lights make a bake worth minutes, not
    /// seconds. Run this once after staging, whenever lantern placement or route geometry changes.
    /// </summary>
    [MenuItem("MapleRide/Environment/Bake Road Light Probes", priority = 22)]
    public static void BakeRoadLightProbes()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
        // Baked Indirect: direct lighting stays 100% real-time for every object (matches the
        // section-24 "ForcePixel sun / per-vertex lanterns" setup); only the bounce is baked.
        // No shadowmask, so no extra runtime cost or texture budget.
        //
        // Lightmapping.lightingSettings must point at a SAVED asset, not a bare `new
        // LightingSettings()`: an unsaved instance assigns fine but Bake() throws "Lightmapping.
        // lightingSettings is null" because the scene's lighting data serialises by asset
        // reference. So create (or reuse) a real .lighting asset under the scene folder.
        const string settingsPath = "Assets/Environment/SakuraPass/SakuraRoadProbeBake.lighting";
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
        if (settings == null)
        {
            settings = new LightingSettings();
            AssetDatabase.CreateAsset(settings, settingsPath);
        }
        settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
        Lightmapping.lightingSettings = settings;

        bool ok = Lightmapping.Bake();
        Debug.Log($"[light-probes] bake finished: {ok}");

        var active = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(active);
        EditorSceneManager.SaveScene(active);
    }

    // ------------------------------------------------------------ petal VFX

    // Agent HQ ticket (2026-09-30, suggested by Builder + QA Validator): "Rebuild cherry-blossom
    // petals as atlased alpha-clip cards with a baked normal." The previous version was a single
    // soft-alpha-blended sprite with no lighting response (Particles/Standard Unlit or Sprites/
    // Default, _LightingEnabled off) - flat pink confetti with no silhouette variety and blend-
    // order sorting artefacts as petals tumbled past each other. This reuses the SAME proven
    // alpha-clip + baked-normal pattern E4 already shipped for the blossom canopy cards
    // (MapleRide/HDRP/FoliageNormal, Cull Off / ZWrite On / Queue=AlphaTest - no sorting needed,
    // casts real shadows, lit by the sun): a 2x2 atlas of 4 distinct petal silhouettes, each with
    // an analytically baked tangent-space normal for its cupped surface, assigned to 4 separate
    // tumbling card meshes via ParticleSystemRenderer.SetMeshes (Unity round-robins per particle).
    private const string PetalAtlasAlbedo = "Sakura_PetalDrift_Atlas.png";
    private const string PetalAtlasNormal = "Sakura_PetalDrift_Atlas_Normal.png";
    private const int PetalAtlasCols = 2;
    private const int PetalAtlasRows = 2;
    private const int PetalCellPx = 128;

    private struct PetalVariant
    {
        public float WidthScale;   // silhouette width
        public float NotchBase;    // tip notch depth (higher = shallower notch)
        public float CupAmp;       // baked-normal cup height (how strongly it catches light)
        public float HueShift;     // per-card tint variance so the drift doesn't look stamped
    }

    private static readonly PetalVariant[] PetalVariants =
    {
        new PetalVariant { WidthScale = 1.00f, NotchBase = 0.92f, CupAmp = 0.34f, HueShift = 0.00f },
        new PetalVariant { WidthScale = 0.84f, NotchBase = 0.86f, CupAmp = 0.46f, HueShift = 0.035f },
        new PetalVariant { WidthScale = 1.14f, NotchBase = 0.97f, CupAmp = 0.26f, HueShift = -0.03f },
        new PetalVariant { WidthScale = 0.95f, NotchBase = 0.89f, CupAmp = 0.40f, HueShift = 0.02f },
    };

    private static void BuildPetalVfx(Transform root, Light sun)
    {
        EnsurePetalAtlas();
        ConfigurePetalAtlasImporters();

        var shader = Shader.Find("MapleRide/HDRP/FoliageNormal");
        var alb = LoadTexture(PetalAtlasAlbedo);
        var bump = LoadTexture(PetalAtlasNormal);
        Material mat;
        if (shader != null && alb != null && bump != null)
        {
            // Alpha-clip double-sided foliage card shader: no blend-order sorting needed as
            // petals tumble, and the baked normal lets them catch the sun as they turn.
            mat = LoadOrCreate("SakuraPass_PetalDriftCard8", "MapleRide/HDRP/FoliageNormal");
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Cutoff", 0.42f);
            mat.SetTexture("_MainTex", alb);
            mat.SetTexture("_BumpMap", bump);
            mat.SetFloat("_BumpScale", 1.3f);
            mat.SetFloat("_Translucency", 1.1f);
            mat.SetColor("_TransColor", new Color(1f, 0.62f, 0.76f, 1f));
            mat.SetFloat("_WindStrength", 0.06f); // subtle extra flutter; the particle sim drives the main motion
            if (mat.HasProperty("_EdgeOnFade")) mat.SetFloat("_EdgeOnFade", 1f);
            EditorUtility.SetDirty(mat);
        }
        else
        {
            // Fallback only if the shared foliage-normal shader/textures are ever missing.
            var tex = EnsurePetalTexture();
            var petalShader = MapleRideShaderNames.Find("Particles/Standard Unlit");
            if (petalShader == null) petalShader = MapleRideShaderNames.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            if (petalShader == null) petalShader = MapleRideShaderNames.Find("Sprites/Default");
            mat = LoadOrCreate("SakuraPetal", petalShader.name);
            MapleRideShaderNames.SetBaseTexture(mat, tex);
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
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            MapleRideShaderNames.SetBaseColor(mat, Color.white);
            if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
            if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
            if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 0f);
            if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
            if (mat.HasProperty("_TransparentZWrite")) mat.SetFloat("_TransparentZWrite", 0f);
            if (mat.HasProperty("_SurfaceType")) { mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.EnableKeyword("_BLENDMODE_ALPHA"); }
            if (mat.HasProperty("_DoubleSidedEnable")) mat.EnableKeyword("_DOUBLESIDED_ON");
            mat.renderQueue = 3000;
            EditorUtility.SetDirty(mat);
        }

        var go = new GameObject("Sakura Petal Drift");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(0f, 18f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 12f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.26f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.78f, 0.86f, 1f), new Color(1f, 0.56f, 0.72f, 1f));
        main.gravityModifier = 0.035f;
        main.maxParticles = 1400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 120f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(110f, 26f, 420f);
        shape.position = new Vector3(0f, 22f, 60f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-1.4f, 1.4f);
        vel.y = new ParticleSystem.MinMaxCurve(-1.5f, -0.5f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.7f);
        noise.frequency = 0.32f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.25f);
        noise.damping = true;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
        rot.z = new ParticleSystem.MinMaxCurve(-2.0f, 2.0f);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        if (shader != null && alb != null && bump != null)
        {
            // One mesh per atlas cell, each with its own UV rect; Unity assigns a mesh per
            // particle (stable per particle index), so the drift reads as 4 distinct silhouettes
            // instead of one sprite stamped thousands of times.
            var meshes = new Mesh[PetalVariants.Length];
            for (int v = 0; v < PetalVariants.Length; v++) meshes[v] = BuildPetalMesh(v);
            renderer.SetMeshes(meshes);
        }
        else
        {
            renderer.mesh = BuildPetalMesh(-1);
        }
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = ParticleSystemRenderSpace.World;
    }

    /// <summary>variantIndex &lt; 0 = the single legacy full-rect quad (fallback path).
    /// Otherwise a quad UV'd to its atlas cell, cupped by that variant's bake amplitude so the
    /// silhouette the eye sees roughly matches the surface the baked normal describes.</summary>
    private static Mesh BuildPetalMesh(int variantIndex)
    {
        var mesh = new Mesh { name = variantIndex < 0 ? "Sakura Petal" : $"Sakura Petal {variantIndex}" };
        float cup = variantIndex >= 0 ? 0.08f + PetalVariants[variantIndex].CupAmp * 0.18f : 0.14f;
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, cup, 0.5f), new Vector3(0.5f, cup, 0.5f)
        };
        if (variantIndex < 0)
        {
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        }
        else
        {
            int col = variantIndex % PetalAtlasCols;
            int row = variantIndex / PetalAtlasCols;
            float u0 = col / (float)PetalAtlasCols, u1 = (col + 1) / (float)PetalAtlasCols;
            // Row 0 is the TOP of the atlas in pixel space; Unity UV v=0 is the bottom, so flip.
            float v1 = 1f - row / (float)PetalAtlasRows, v0 = 1f - (row + 1) / (float)PetalAtlasRows;
            mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u0, v1), new Vector2(u1, v1) };
        }
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Procedurally bakes a 2x2 atlas of 4 distinct sakura-petal silhouettes (albedo,
    /// hard-edged alpha for clip) plus a matching tangent-space normal map, analytically derived
    /// from each petal's own cupped height field (height = CupAmp * silhouette coverage; the
    /// normal is the height field's gradient). Regenerated every build, same convention as the
    /// old single-sprite EnsurePetalTexture.</summary>
    private static void EnsurePetalAtlas()
    {
        int w = PetalAtlasCols * PetalCellPx, h = PetalAtlasRows * PetalCellPx;
        var albedoTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var normalTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var height = new float[w, h];
        var alpha = new float[w, h];
        var colorR = new float[w, h]; var colorG = new float[w, h]; var colorB = new float[w, h];

        for (int cell = 0; cell < PetalAtlasCols * PetalAtlasRows; cell++)
        {
            var variant = PetalVariants[Mathf.Min(cell, PetalVariants.Length - 1)];
            int col = cell % PetalAtlasCols, row = cell / PetalAtlasCols;
            int x0 = col * PetalCellPx, y0 = row * PetalCellPx;
            for (int py = 0; py < PetalCellPx; py++)
            {
                for (int px = 0; px < PetalCellPx; px++)
                {
                    float u = (px / (float)(PetalCellPx - 1)) * 2f - 1f;
                    float v = (py / (float)(PetalCellPx - 1)) * 2f - 1f;
                    float t = Mathf.Clamp01((v + 0.96f) / 1.90f);
                    float halfWidth = 0.56f * variant.WidthScale *
                        Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.62f) * 0.97f), 0.70f);
                    float body = Smooth01(1.0f, 0.78f, Mathf.Abs(u) / Mathf.Max(halfWidth, 1e-4f));
                    float tipLimit = variant.NotchBase - 0.20f * Mathf.Exp(-Mathf.Pow(u / 0.21f, 2f));
                    float tip = Smooth01(tipLimit, tipLimit - 0.12f, v);
                    float a = Mathf.Clamp01(body * tip);

                    var c = Color.Lerp(new Color(1f, 0.50f + variant.HueShift, 0.66f),
                                        new Color(1f, 0.94f, 0.97f - variant.HueShift), t);

                    int ax = x0 + px, ay = y0 + py;
                    alpha[ax, ay] = a;
                    height[ax, ay] = variant.CupAmp * a;
                    colorR[ax, ay] = c.r; colorG[ax, ay] = c.g; colorB[ax, ay] = c.b;
                }
            }
        }

        const float normalStrength = 2.6f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                albedoTex.SetPixel(x, y, new Color(colorR[x, y], colorG[x, y], colorB[x, y], alpha[x, y]));

                int xm = Mathf.Max(x - 1, 0), xp = Mathf.Min(x + 1, w - 1);
                int ym = Mathf.Max(y - 1, 0), yp = Mathf.Min(y + 1, h - 1);
                float dhdx = (height[xp, y] - height[xm, y]) * 0.5f;
                float dhdy = (height[x, yp] - height[x, ym]) * 0.5f;
                var n = new Vector3(-dhdx * normalStrength, -dhdy * normalStrength, 1f).normalized;
                normalTex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
            }
        }
        albedoTex.Apply();
        normalTex.Apply();

        SavePetalPng(PetalAtlasAlbedo, albedoTex);
        SavePetalPng(PetalAtlasNormal, normalTex);
        UnityEngine.Object.DestroyImmediate(albedoTex);
        UnityEngine.Object.DestroyImmediate(normalTex);
    }

    private static void SavePetalPng(string file, Texture2D tex)
    {
        string assetPath = $"{TextureDir}/{file}";
        string absolute = Path.Combine(Application.dataPath, "Environment/SakuraPass/Textures", file);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute));
        File.WriteAllBytes(absolute, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void ConfigurePetalAtlasImporters()
    {
        string dir = TextureDir;
        if (AssetImporter.GetAtPath($"{dir}/{PetalAtlasAlbedo}") is TextureImporter a &&
            (!a.alphaIsTransparency || !a.mipMapsPreserveCoverage))
        {
            a.alphaIsTransparency = true;
            a.mipMapsPreserveCoverage = true;   // alpha-clip cards must not thin out with distance
            a.alphaTestReferenceValue = 0.42f;
            a.wrapMode = TextureWrapMode.Clamp;
            a.SaveAndReimport();
        }
        if (AssetImporter.GetAtPath($"{dir}/{PetalAtlasNormal}") is TextureImporter n &&
            n.textureType != TextureImporterType.NormalMap)
        {
            n.textureType = TextureImporterType.NormalMap;
            n.wrapMode = TextureWrapMode.Clamp;
            n.SaveAndReimport();
        }
    }

    /// <summary>HLSL-style smoothstep. Mathf.SmoothStep interpolates between its two
    /// arguments instead, which silently produces a ramp that never reaches 0 or 1.</summary>
    private static float Smooth01(float edge0, float edge1, float x)
    {
        if (Mathf.Approximately(edge0, edge1)) return x < edge0 ? 0f : 1f;
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static Texture2D EnsurePetalTexture()
    {
        const string file = "SakuraPetal_Sprite.png";
        string assetPath = $"{TextureDir}/{file}";
        string absolute = Path.Combine(Application.dataPath, "Environment/SakuraPass/Textures", file);

        // Regenerated every build. The first version of this sprite used
        // Mathf.SmoothStep(edge, edge - 0.2f, r), which is NOT HLSL's smoothstep: Unity's
        // interpolates *between* the two arguments, so alpha only ever ran 0.92 -> 0.72 and the
        // sprite was a fully opaque pink square with no silhouette at all. It has to fall to a
        // hard 0 outside the petal or the quad's own rectangle shows.
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x / (float)(size - 1)) * 2f - 1f;
                float v = (y / (float)(size - 1)) * 2f - 1f;

                // t: 0 at the stem end, 1 at the notched tip.
                float t = Mathf.Clamp01((v + 0.96f) / 1.90f);
                // Teardrop: tapered at the stem, widest around two thirds up.
                float halfWidth = 0.56f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.62f) * 0.97f), 0.70f);
                float body = Smooth01(1.0f, 0.78f, Mathf.Abs(u) / Mathf.Max(halfWidth, 1e-4f));
                // The characteristic sakura notch cut into the tip.
                float tipLimit = 0.92f - 0.20f * Mathf.Exp(-Mathf.Pow(u / 0.21f, 2f));
                float tip = Smooth01(tipLimit, tipLimit - 0.12f, v);
                float alpha = Mathf.Clamp01(body * tip);

                var c = Color.Lerp(new Color(1f, 0.50f, 0.66f), new Color(1f, 0.94f, 0.97f), Mathf.Clamp01(t));
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, alpha));
            }
        }
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(absolute));
        File.WriteAllBytes(absolute, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null && (!importer.alphaIsTransparency
                                 || importer.textureCompression != TextureImporterCompression.Uncompressed))
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            // 128 px of alpha ramp: DXT5 block artefacts are clearly visible on the silhouette
            // and cost nothing to avoid at this size.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    // ------------------------------------------------- material upgrade pass

    /// <summary>
    /// Converts every remaining Unlit material in the scene to the cel shader, preserving
    /// colour and any texture. This is the single biggest visual win: previously the
    /// directional light affected nothing at all.
    /// </summary>
    public static void UpgradeSceneMaterialsToCel()
    {
        var cel = MapleRideShaderNames.Find(CelShaderName);
        var foliage = MapleRideShaderNames.Find(FoliageShaderName);
        if (cel == null || foliage == null) return;

        var converted = new Dictionary<Material, Material>();
        int count = 0;

        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r is ParticleSystemRenderer) continue;

            var mats = r.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null) continue;
                // Deliberately-unlit materials opt out. The tunnel luminaires are Unlit/Color on
                // purpose: a sodium lamp is a light source, and cel-shading it makes it take the
                // sunset key like a painted box and go dark inside the bore.
                if (m.name.Contains("Sodium")) continue;
                if (m.name.Contains("Emissive")) continue;   // E4 idea 10 lantern heads, same reasoning
                if (!m.shader.name.StartsWith("Unlit/") &&
                    !m.shader.name.StartsWith("Legacy Shaders/Unlit") &&
                    m.shader.name != "Standard") continue;

                if (converted.TryGetValue(m, out var replacement))
                {
                    mats[i] = replacement; changed = true; continue;
                }

                var color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                string name = string.IsNullOrEmpty(m.name) ? $"Converted {count}" : m.name;

                bool isFoliage = LooksLikeFoliage(name) || LooksLikeFoliage(r.gameObject.name);
                Material nm = isFoliage
                    ? FoliageMaterial("Cel " + name, color, tex)
                    : CelMaterial("Cel " + name, color, GlossFor(name), SpecFor(name), RimFor(name), texture: tex);

                converted[m] = nm;
                mats[i] = nm;
                changed = true;
                count++;
            }

            if (changed)
            {
                r.sharedMaterials = mats;
                r.receiveShadows = true;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off)
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        Debug.Log($"Sakura Pass: upgraded {count} unlit materials to cel lighting.");
    }

    private static bool LooksLikeFoliage(string n)
    {
        n = n.ToLowerInvariant();
        return n.Contains("blossom") || n.Contains("petal") || n.Contains("leaf") ||
               n.Contains("foliage") || n.Contains("grass") || n.Contains("fern") ||
               n.Contains("shrub") || n.Contains("tuft");
    }

    private static float GlossFor(string n)
    {
        n = n.ToLowerInvariant();
        if (n.Contains("asphalt") || n.Contains("road")) return 0.42f;   // damp tarmac catches the low sun
        if (n.Contains("guardrail") || n.Contains("galvan")) return 0.55f;
        if (n.Contains("reflector")) return 0.85f;
        if (n.Contains("lacquer") || n.Contains("vermilion") || n.Contains("torii")) return 0.5f;
        if (n.Contains("water") || n.Contains("lake")) return 0.8f;
        return 0.18f;
    }

    private static float SpecFor(string n)
    {
        n = n.ToLowerInvariant();
        if (n.Contains("asphalt") || n.Contains("road")) return 0.30f;
        if (n.Contains("guardrail") || n.Contains("galvan")) return 0.65f;
        if (n.Contains("reflector") || n.Contains("glow") || n.Contains("lantern")) return 1.2f;
        if (n.Contains("line") || n.Contains("edge")) return 0.35f;
        return 0.10f;
    }

    private static float RimFor(string n)
    {
        n = n.ToLowerInvariant();
        if (n.Contains("rock") || n.Contains("stone") || n.Contains("cliff")) return 0.9f;
        if (n.Contains("asphalt") || n.Contains("road")) return 0.25f;
        return 0.6f;
    }
}

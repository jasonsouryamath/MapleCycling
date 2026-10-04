using UnityEngine;

/// <summary>
/// Fast travel between world-map regions.
///
/// REGION MODEL (the choice this milestone makes):
/// every region lives in the ONE playable scene, in its own patch of world space, and every
/// region's road is a group of segments and courses inside the ONE baked
/// <see cref="RouteGraph"/>. Fast travel is therefore:
///
///   1. select the region's course on the <see cref="RideSession"/> (the ride systems already
///      support this - it is what the 1-4 keys do),
///   2. reset the ride and re-seat the rider through the <see cref="RouteFollower"/>,
///   3. enable that region's environment root and disable the others.
///
/// No scene load, no second RouteGraph asset, no duplicated rider/HUD/camera rig, and nothing
/// in the ride foundation has to learn about regions. The cost is one shared lighting/sky setup
/// and a scene that grows with each region; when that stops being acceptable the same three
/// steps become an additive scene load behind this one method.
/// </summary>
[DefaultExecutionOrder(-90)]
public class RegionDirector : MonoBehaviour
{
    [Header("Wiring (pushed by the setup pass)")]
    public RideSession session;
    public RideBootstrap boot;
    public RouteFollower follower;
    public RouteDressingStreamer streamer;

    [Tooltip("Region the rider is currently in. Kept in sync with the selected course.")]
    public string currentRegionId = RegionCatalog.SakuraPass;

    /// <summary>Fires after a successful fast travel, with the new region id.</summary>
    public event System.Action<string> RegionChanged;

    // ---- Per-region ambience -------------------------------------------------------------
    // PROVISIONAL TUNING. One scene means ONE set of RenderSettings, one key light and one
    // post-FX grade, so each region has to carry the whole time-of-day look that makes it read
    // correctly and re-apply it on arrival:
    //
    //   Sakura Pass  = golden-hour sunset over a mountain pass (warm haze, low raking key)
    //   Shiosai Coast= bright coastal DAYLIGHT (clear blue sky, high near-white key, sea haze)
    //
    // Before this, Shiosai inherited Sakura's sunset skybox and read as "Sakura at dusk by the
    // sea", which is exactly what the coast must not look like. The Sakura numbers mirror
    // SakuraPassEnvironment.ConfigureLightingAndSky/TunePostFX so travelling home restores
    // exactly what the environment pass authored.
    [System.Serializable]
    public struct Ambience
    {
        public Color fog; public float fogDensity;
        public Color ambientSky, ambientEquator, ambientGround; public float ambientIntensity;

        // Key / fill directional lights (the scene has exactly one of each, shared).
        public Vector3 keyEuler; public Color keyColor; public float keyIntensity;
        public Vector3 fillEuler; public Color fillColor; public float fillIntensity;

        // SakuraPostFX grade.
        public float bloomThreshold, bloomIntensity;
        public float exposure, saturation, contrast;
        public Color lift, gain;
        public float vignette, vignetteSoftness;
        public float dofFocusDistance, dofFocusRange, dofStrength;
        /// <summary>Curve exponent and blur iteration count - see SakuraPostFX.</summary>
        public float dofFalloff; public int dofIterations;

        // --- Part B section 25. Atmosphere/depth. Same reasoning as the shadow fields below:
        // ApplyAmbience overwrites the whole SakuraPostFX grade, so anything set only by the
        // editor-time environment pass is dead code in play mode. aerialRange <= 0 means
        // "region opts out" and leaves the component's own values alone (Shiosai does).
        public float aerialStart, aerialRange, aerialDesaturation, aerialFlatten, aerialTintAmount;
        public Color aerialTint;
        public float mistBaseY, mistTopY, mistStrength, mistStart;
        public Color mistColor;

        // --- Part B section 24. Realtime shadow state. These MUST live here: ApplyAmbience is
        // the runtime lighting authority and overwrites the sun wholesale, so anything set only
        // in SakuraPassEnvironment.ConfigureLightingAndSky is dead code the moment play starts.
        public bool keyShadows; public float keyShadowStrength; public float shadowDistance;
    }

    public static readonly Ambience SakuraAmbience = new Ambience
    {
        fog = new Color(0.93f, 0.76f, 0.64f, 1f), fogDensity = 0.00038f,
        // Section 24 ambient rebalance. PROVISIONAL - mirrors SakuraPassEnvironment.AmbientScale
        // (0.72). The scale is folded into the COLOURS on purpose: ambientMode is Trilight, and
        // Trilight ambient ignores ambientIntensity entirely, so scaling the intensity is a no-op.
        ambientSky = new Color(0.432f, 0.504f, 0.648f, 1f),
        ambientEquator = new Color(0.619f, 0.504f, 0.490f, 1f),
        ambientGround = new Color(0.288f, 0.252f, 0.238f, 1f),
        ambientIntensity = 1.0f,   // inert while ambientMode is Trilight; kept for documentation

        // Elevation 19 -> 58. MEASURED via ShadowProbe.RunAtten, which reads the raw shadow term
        // off the carriageway: the sakura canopy is a continuous roof with a hard cutoff at ~48
        // deg, so at 19 (and at 30) the road was 0% sunlit and sat entirely at the shadow floor.
        // That - not the shader, the material, the camera or the render path - is why the road
        // had no dapple. Dapple peaks 55-60 deg and flattens again by 70. PROVISIONAL.
        keyEuler = new Vector3(58f, 62f, 0f),
        keyColor = new Color(1f, 0.81f, 0.60f, 1f), keyIntensity = 1.40f,
        fillEuler = new Vector3(-18f, 210f, 0f),
        fillColor = new Color(0.58f, 0.72f, 0.98f, 1f), fillIntensity = 0.30f,

        keyShadows = true, keyShadowStrength = 0.78f, shadowDistance = 150f,

        bloomThreshold = 1.55f, bloomIntensity = 0.34f,
        // HDRP-LIVE CORRECTION, ROUND 2. Round 1 moved exposure alone (0.80 -> 0.88) on the
        // theory that contrast/saturation were "carrying the correct look" and only needed
        // exposure to lift the shadow floor. A full ~30-shot QA pass proved that theory wrong:
        // the canopy/tunnel crush (near-black, often violet-cast) was still pervasive. The
        // shader applies saturation and contrast AFTER ACES tonemap, in that order:
        //   col = lerp(lum, col, saturation);                    // can EXTRAPOLATE past 1.0
        //   col = saturate((col - 0.5) * contrast + 0.5);
        // At saturation 1.32 (the highest of any region - next is Maple City's 1.26), a shadow
        // pixel whose tiny remaining signal is the cool ambientSky bounce (0.432, 0.504, 0.648 -
        // bluer than the midpoint) gets EXTRAPOLATED, not just preserved: the lerp with t > 1
        // pushes red/green further toward zero while blue is relatively spared, which is
        // measurably the "near-texture-less dark violet slab" QA reported in diag_gate and
        // diag_signboard - a colour-cast bug, not just a brightness one. Contrast then squashes
        // whatever is left even further toward black. Both numbers move together because the
        // crush and the violet cast are the SAME bug, not two: saturation 1.32 -> 1.18 (still
        // the second-most-saturated region in the game, preserving the "controlled saturated
        // cel shading" look documented for the sunlit two-thirds of every frame) and contrast
        // 1.02 -> 0.98 (a small net LIFT of the shadow floor, the same class of correction Taka
        // and Fuji needed once their own grades went live). Re-verified against the full ~44-shot
        // diagnostic set, not the 3-shot sample the round-1 fix used - see FiSakuraGradeCaptureAll.
        exposure = 0.88f, saturation = 1.18f, contrast = 0.98f,
        lift = new Color(0.045f, 0.030f, 0.020f, 0f),
        gain = new Color(1.04f, 1.00f, 0.96f, 0f),
        vignette = 0.20f, vignetteSoftness = 0.75f,
        dofFocusDistance = 160f, dofFocusRange = 700f, dofStrength = 0.62f,
        dofFalloff = 1.55f, dofIterations = 2,
        // Section 25 atmosphere. Mirrors the SakuraPassEnvironment constants of the same name;
        // both authorities must be updated together or the look changes on entering play mode.
        aerialStart = 110f, aerialRange = 950f,
        aerialDesaturation = 0.50f, aerialFlatten = 0.40f,
        aerialTint = new Color(0.93f, 0.78f, 0.70f, 1f), aerialTintAmount = 0.22f,
        mistBaseY = 6f, mistTopY = 34f, mistStrength = 0.45f, mistStart = 140f,
        mistColor = new Color(0.95f, 0.86f, 0.84f, 1f),
    };

    public static readonly Ambience ShiosaiAmbience = new Ambience
    {
        // Clear sea air: the ocean and horizon have to survive to the far headland, and the fog
        // must be a pale sky-blue so distance reads as haze rather than as a warm veil.
        //
        // Density raised 0.00016 -> 0.00026 for the background-depth pass. At the old value the
        // new inland ranges at 650 / 1450 / 2700 m all came back at nearly full saturation and
        // stacked into one flat blue wall; exponential-squared fog at 0.00026 leaves 200 m of
        // road untouched (0.3% fog) while putting 24% of haze on the near range, 66% on the
        // far one - which is what actually makes them read as three separate distances.
        // GOLDEN-HOUR REGRADE (fix #1). Spec + all 8 SC refs call for a warm late-afternoon
        // sun over the sea with COOL BLUE SHADOWS, not the bright midday-blue this region shipped
        // with. The warmth is carried by the low warm KEY and the warm grade GAIN; the coolness
        // is carried by this ambient dome + the cool lift + the cool sea-bounce fill. Fog is kept
        // cool/blue on purpose: a warm fog saturates over the far ocean and paints a gold bar
        // across the horizon (the exact ch8 regression), so the sky/sun warm the frame while the
        // sea stays cobalt. Nudged very slightly warmer than pure blue so the marine haze reads
        // as late-afternoon air, not noon.
        // FIX #2 (cardboard distant mountains). The inland ranges sit 6.4-8.5 km from the
        // chapter cams. The in-shader distance fog (MR_ApplyFog, ExponentialSquared) fully
        // saturates to _MR_FogColor by ~8 km, so those ridges were painted flat in the pale
        // sky-blue fog above - BRIGHTER than the sky - and read as unshaded cardboard cutouts
        // (immune to albedo/rim/ambient, which were all tested and ruled out). Recolouring the
        // fog to a marine TEAL and easing the density makes the fully-fogged ridges resolve as
        // solid, layered, cool silhouettes that sit BELOW the sky in value (SPEC "layered
        // distant silhouettes" + "cool blue shadows"), and keeps the far sea saturated turquoise
        // -> cobalt longer (helps #3). The sky dome is a separate GradientSky volume and is NOT
        // fogged, so the warm late-afternoon sky is untouched; ch8 has its own fog override so
        // the golden finale is unaffected.
        fog = new Color(0.36f, 0.48f, 0.52f, 1f), fogDensity = 0.00013f,
        // Cooler + a touch deeper than the old midday dome so shade sides read distinctly blue
        // and sit BELOW the sunlit sides in value (SPEC 3.3 warm-lit / cool-shadow split).
        ambientSky = new Color(0.44f, 0.60f, 0.86f, 1f),
        ambientEquator = new Color(0.49f, 0.59f, 0.73f, 1f),
        ambientGround = new Color(0.22f, 0.27f, 0.31f, 1f),
        // 0.80 washed every small prop out: pines went mint, hydrangeas went white, and the
        // headland read as a neon lawn. The sun does the lighting here, not the ambient dome.
        // M5: 0.60 -> 0.50. The mid-day flatness the overhaul had to remove was mostly an
        // ambient-dome problem, not a key problem: at 0.60 the shadow floor sat so high that
        // the cliff faces, the pine canopies and the harbour eaves all landed within a few
        // points of their lit sides, so nothing modelled and the frame read as a flat card.
        // Dropping the dome is what buys the value hierarchy in SPEC 3.3 - it lowers the
        // BOTTOM of the range without touching the top, so the foam and the lighthouse stay
        // the brightest things in frame while everything else gains separation. PROVISIONAL.
        ambientIntensity = 0.50f,

        // High, near-white sun: what makes the grass read GREEN and the sea deep blue instead of
        // the pass's low amber rake. 48 deg is late-morning, not noon, so cliffs still model.
        // M5: 0.95 -> 1.06 and a hair warmer. With the dome down 0.10 the total exposure budget
        // is unchanged, but the light is now nearly all directional, so surfaces separate by
        // their angle to the sun rather than by their albedo alone.
        // GOLDEN-HOUR KEY (fix #1). Dropped 40 -> 30 deg (late afternoon, long raking light,
        // not the old near-noon 40) and warmed the colour from near-white to a golden amber.
        // This is the single biggest lever moving all 8 chapters + the finale toward the refs:
        // the sun now lights the warm side of every cliff, roof and pine while the cool ambient
        // above holds the shade blue. Azimuth pulled a touch toward the sea to rake the road.
        keyEuler = new Vector3(30f, 124f, 0f),
        keyColor = new Color(1f, 0.875f, 0.685f, 1f), keyIntensity = 1.16f,
        // The fill is the SEA bounce. Pushing it slightly cooler and slightly stronger keeps the
        // shadow side of white geometry (lighthouse, foam, stucco) reading as sunlit-white-in-
        // shade rather than as grey, which is what stops the deeper dome from muddying the town.
        fillEuler = new Vector3(24f, 310f, 0f),
        fillColor = new Color(0.55f, 0.74f, 0.98f, 1f), fillIntensity = 0.30f,

        // M5 GRADE - SPEC 3.3 value hierarchy.
        //
        // Threshold 1.62 -> 1.34: at 1.62 essentially nothing in the coast frame ever bloomed,
        // because the grade tops out around 1.5. The two things the spec wants to be the
        // brightest features in the frame - the white foam line and the lighthouse tower - are
        // exactly the things that sit in the 1.3-1.5 band, so lowering the knee makes THEM
        // glow and nothing else. Intensity is pulled back 0.26 -> 0.21 at the same time so the
        // result is a highlight bloom, not a haze over the whole image.
        // bloomIntensity zeroed for the same HDRP-grade-ownership reason as exposure/saturation/
        // contrast below: the Shiosai Sky Volume already carries its own native HDRP Bloom
        // override, and letting SakuraGradeCustomPass add a second bloom pass on top double-
        // blooms every highlight. bloomThreshold is kept at the tuned value for provenance/if
        // this region is ever moved back to the shared grade; it is inert while intensity is 0.
        bloomThreshold = 1.34f, bloomIntensity = 0.00f,
        // Exposure 0.82 -> 0.78 protects the foam and the whitewashed tower from clipping now
        // that contrast and saturation are both up; contrast 1.07 -> 1.14 and saturation
        // 1.12 -> 1.22 are the "controlled saturated cel shading" the spec asks for - enough
        // chroma for the coral-red torii / bridge / lighthouse band to read as an ACCENT
        // against the blue-green world, without tipping the grass into neon.
        // HDRP GRADE OWNERSHIP FIX (post-migration). This exposure/saturation/contrast/lift/gain
        // block, and the M5/GOLDEN-HOUR GRADE values that used to live here (exposure 0.80,
        // saturation 1.22, contrast 1.17, lift (0.004,0.018,0.040), gain (1.11,1.015,0.885),
        // vignette 0.17) were tuned and proven correct against real renders BEFORE this project
        // moved to HDRP, back when SakuraPostFX.OnRenderImage actually executed. When HDRP
        // stopped calling OnRenderImage, ShiosaiCoastEnvironment.ConfigureCoastSkyVolume grew its
        // OWN native HDRP ColorAdjustments/Bloom/Tonemapping override on the scene-local
        // "Shiosai Sky Volume" as a (correct, and still the intended long-term design) workaround
        // - see the "HDRP owns the sky and the grade in this project" note a few lines up. Now
        // that SakuraGradeCustomPass revives SakuraPostFX under HDRP too, pushing the old values
        // here would apply BOTH grades on top of each other: confirmed by render as a torii gate
        // crushed to near-black with a red bloom-fringe artifact and an over-saturated navy sky.
        // Neutralised here so the Shiosai Sky Volume remains the single source of truth for this
        // region's grade; the historical values are preserved above for provenance only.
        exposure = 1.00f, saturation = 1.00f, contrast = 1.00f,
        lift = new Color(0f, 0f, 0f, 0f),
        gain = new Color(1f, 1f, 1f, 0f),
        vignette = 0.00f, vignetteSoftness = 0.82f,

        // DEPTH OF FIELD - retuned for the background-depth pass.
        //
        // The old 240 / 1400 / 0.42 was very nearly a dead setting: with SakuraPostFX's
        // coc = ((eye - focus) / range)^falloff * strength, a headland at 400 m came back at
        // 1.5% blend and one at 1 km at 16%, so the background was effectively as crisp as the
        // road and the frame read flat. Pulling the focus distance in to 150 m and the range
        // down to 520 m puts the sharp-to-soft transition just past the far end of the visible
        // carriageway (the coast road's longest sightline from the saddle is ~140 m) and tops
        // the blend out by ~670 m, so everything from the midground treeline back is genuinely
        // soft. dofIterations 2 -> 3 widens the half-res blur enough for that softness to read
        // as atmosphere rather than as a slightly out-of-focus photograph.
        //
        // NOT raised further on purpose: the road, the guardrail, the HUD and any rider the
        // player is actually interacting with all sit well inside 150 m and stay untouched.
        dofFocusDistance = 150f, dofFocusRange = 520f, dofStrength = 0.58f,
        dofFalloff = 1.25f, dofIterations = 3,

        // SHADOW DISTANCE. Shiosai previously left this at 0, which meant it inherited whatever
        // the last region set - in practice Sakura's 150 m. On a climb with eleven riders that
        // is fine; on a coast road with thirty ambient riders, a 150 m shadow volume re-submits
        // every rider, every pine and every guardrail post a second time. The coast's own
        // sightline from the saddle is ~140 m and its sun is low and raking, so contact shadows
        // on the road are what matters - 70 m keeps all of those and drops the rest.
        // PROVISIONAL.
        // M5: 0.74 -> 0.80. With the ambient dome down to 0.50 a weak shadow term made contact
        // shadows on the road vanish; 0.80 is what puts the guardrail posts, the pines and the
        // harbour eaves back on the ground. PROVISIONAL.
        keyShadows = true, keyShadowStrength = 0.84f, shadowDistance = 70f,

        // M6. THE GOLD HORIZON BAND. Shiosai was the ONLY region leaving aerialRange at 0,
        // which the struct documents as "region opts out and the component keeps its own
        // values" - and those values are whatever the last pass serialized onto SakuraPostFX,
        // i.e. SAKURA'S SUNSET aerial tint (0.93, 0.78, 0.70) at amount 0.22. Aerial tint
        // saturates at maximum distance, so an ocean region inherited a warm cream wash across
        // its entire sea horizon: the tan/gold bar visible above the water in ch8_highway.
        // Every other region already opts in with a COOL tint (Azora 0.688/0.762/0.860, Taka
        // 0.640/0.720/0.842); the coast now does too, with the marine haze matching the new
        // GradientSky horizon stop so distance reads as sea air rather than as dusk.
        //
        // NOTE (cardboard-mountains investigation, fix #2): these aerial params only touch
        // DEPTH-WRITING terrain (the sea sheet and the near hills). The distant inland ranges,
        // backdrop headlands and distant treeline are silhouette meshes that do NOT populate the
        // HDRP camera-depth texture, so SakuraPostFX reads sky depth through them and skips the
        // aerial/mist stage entirely on those pixels. Their "cardboard" look was therefore NOT an
        // aerial problem - it was pure albedo brightness (they rendered LIGHTER than the sky) and
        // is fixed on the band albedos in ShiosaiCoastEnvironment, not here. These values are the
        // originals, tuned for the sea horizon (kills the M6 gold band); leave them be.
        // Range is the largest in the game on purpose: the coast's backdrop is open water and
        // headlands out to ~25 km, not a 950 m valley. ALL PROVISIONAL art tuning.
        aerialStart = 350f, aerialRange = 7500f,
        aerialDesaturation = 0.45f, aerialFlatten = 0.34f,
        aerialTint = new Color(0.700f, 0.800f, 0.884f, 1f), aerialTintAmount = 0.26f,

        // Sea haze kept deliberately weak and low: the references show clean air with crisp
        // stacks, not a foggy bay. mistBaseY/mistTopY are ABSOLUTE WORLD Y and the coast road
        // runs just above sea level, so the band sits from the waterline to ~46 m.
        mistBaseY = 0f, mistTopY = 46f, mistStrength = 0.18f, mistStart = 600f,
    };

    /// <summary>
    /// MINATO COAST - BRIGHT MIDDAY MODERN PORT CITY.
    ///
    /// Re-graded from the previous "late-afternoon maritime" look (key elevation 32 deg, warm
    /// 1.0/0.89/0.72 key at 1.18, ambient intensity 0.52, exposure 0.80, saturation 1.12). That
    /// grade is what made the region read drab, dark and bleak: a low warm raking sun over a
    /// navy sky, with murky ambient in every shadow. Minato is the lively modern port city on
    /// the world map, so it now gets the brightest, highest key in the game.
    ///
    /// keyEuler.x = 62 deg. A near-midday sun: short shadows, sunlit roadway, and - critically
    /// for a city of tall towers - the boulevard is NOT in the shade of its own skyline. Both
    /// this and the HDRP volume in MinatoCoastEnvironment.ConfigureAtmosphere must be changed
    /// together; ApplyAmbience is the runtime lighting authority and overwrites the sun, while
    /// the volume owns the sky, fog and grade. ALL PROVISIONAL.
    /// </summary>
    public static readonly Ambience MinatoAmbience = new Ambience
    {
        // GOLDEN HOUR (2026-09-25, user reference: low sun over the harbour, long warm shadows).
        // Was bright midday: key (62, 128) white 1.24, cool fog/ambient. The route leaves the
        // port heading ~+X with the sea on the rider's LEFT (+Z), so the sun sits low (12 deg)
        // ahead-left over the water: light forward = away from that = yaw ~233. Warm key, cool
        // sky fill from the opposite side, warm equator bounce. ALL PROVISIONAL art tuning.
        // Pass 2: pass 1 read as a uniform orange filter - warmth now lives in the key light and
        // horizon, while the sky fill and fog stay closer to neutral so whites read white.
        fog = new Color(0.95f, 0.87f, 0.78f, 1f), fogDensity = 0.000030f,
        ambientSky = new Color(0.60f, 0.72f, 0.95f, 1f),
        ambientEquator = new Color(0.92f, 0.80f, 0.68f, 1f),
        ambientGround = new Color(0.44f, 0.37f, 0.34f, 1f),
        ambientIntensity = 0.80f,
        keyEuler = new Vector3(12f, 233f, 0f),
        keyColor = new Color(1f, 0.82f, 0.62f, 1f), keyIntensity = 1.34f,
        fillEuler = new Vector3(28f, 53f, 0f),
        fillColor = new Color(0.56f, 0.68f, 1f, 1f), fillIntensity = 0.42f,
        keyShadows = true, keyShadowStrength = 0.74f, shadowDistance = 300f,
        // HDRP GRADE OWNERSHIP FIX (post-migration), same reasoning as ShiosaiAmbience above:
        // MinatoCoastEnvironment.ConfigureAtmosphere already carries its own native HDRP
        // ColorAdjustments/Bloom/Tonemapping/WhiteBalance override on "Minato_SkyProfile", added
        // as a workaround for this exact dead-grade bug. Now that SakuraGradeCustomPass revives
        // SakuraPostFX under HDRP, pushing the historical values here (bloomIntensity 0.16,
        // exposure 1.06, saturation 1.34, contrast 1.06, lift (0.026,0.034,0.042), gain
        // (1.06,1.06,1.05), vignette 0.06) would double-grade every Minato frame the same way it
        // did for Shiosai. Neutralised so Minato_SkyProfile remains the single source of truth;
        // historical values kept in this comment for provenance.
        bloomThreshold = 1.45f, bloomIntensity = 0.00f,
        exposure = 1.00f, saturation = 1.00f, contrast = 1.00f,
        lift = new Color(0f, 0f, 0f, 0f),
        gain = new Color(1f, 1f, 1f, 0f),
        vignette = 0.00f, vignetteSoftness = 0.88f,
        dofFocusDistance = 240f, dofFocusRange = 2400f, dofStrength = 0.18f,
        dofFalloff = 1.20f, dofIterations = 2,
        aerialStart = 1200f, aerialRange = 22000f,
        aerialDesaturation = 0.14f, aerialFlatten = 0.10f,
        aerialTint = new Color(0.80f, 0.88f, 0.97f, 1f), aerialTintAmount = 0.10f,
        mistBaseY = -4f, mistTopY = 45f, mistStrength = 0.03f, mistStart = 3200f,
        mistColor = new Color(0.86f, 0.92f, 0.98f, 1f),
    };

    /// <summary>
    /// MAPLE CITY - "The Heart". Warm autumn golden hour in a dense anime metropolis.
    ///
    /// The city is deliberately the THIRD distinct time of day on the map, because it has to
    /// read as neither of the other two: Sakura Pass is a low amber mountain sunset, Shiosai
    /// Coast is bright near-white coastal daylight, and Maple City is a warm, enclosed,
    /// low-altitude GOLDEN HOUR - burnt-orange maple on warm grey stone, with cool blue shadow
    /// in the alleys and pools of warm window/lantern light (design doc section 3).
    ///
    /// Two numbers here are load-bearing and were chosen against the city's geometry rather
    /// than copied from a sibling region:
    ///
    ///   keyEuler.x = 34 deg. The design's single most specific lighting note is "long building
    ///   shadows striping the road". Sakura's 58 deg was measured to fix a problem the city
    ///   does not have (a continuous petal canopy overhead swallowing the sun); a boulevard has
    ///   no roof, so the city can afford a genuinely low raking key. At 34 deg a 20 m building
    ///   throws a ~30 m shadow, which lands as a clean band across a 7 m carriageway. Dropping
    ///   further would start putting the whole street in shade.
    ///
    ///   mistBaseY = 38 / mistTopY = 64. These are ABSOLUTE WORLD Y, not heights above the
    ///   road. The city floor sits at y = 40 m and the Old Town terrace crests near y = 90 m
    ///   (see tools/blender/maple_city_route.py), so Sakura's 6..34 m band would have put the
    ///   entire region above the mist and made it dead. 38..64 keeps river/canal mist pooling
    ///   in the low half of the loop and leaves the Sky Terrace looking out OVER it, which is
    ///   what the "whole-city vista" overlook is for.
    ///
    /// Everything below is PROVISIONAL art tuning.
    /// </summary>
    /// <summary>
    /// MAPLE CITY - VAPORWAVE NEO-TOKYO NIGHT (user, 2026-10-03: "vapor wave ... future Tokyo vibes"). Replaces the warm
    /// golden hour with a violet/magenta night: moonlit lavender key, magenta fill, violet haze, hot bloom so neon reads as light.
    /// The warm look stays in <see cref="MapleCityAmbience"/> for rollback; flip this flag to return to it. The documented
    /// correctness lessons are KEPT, not relaxed: ambient stays bright enough that the rider is never a black silhouette (the
    /// trilight levels have the same luminance as the warm ones, only re-hued), the key still rakes camera-facing surfaces
    /// (azimuth 300), shadows stay readable, and exposure x contrast stays inside the range proven not to crush facades.
    /// PROVISIONAL art tuning - verify against a live capture.
    /// </summary>
    public const bool MapleCityVaporwave = true;

    public static readonly Ambience MapleCityVaporAmbience = new Ambience
    {
        fog = new Color(0.17f, 0.09f, 0.32f, 1f), fogDensity = 0.00048f,
        ambientSky = new Color(0.40f, 0.37f, 0.76f, 1f),
        ambientEquator = new Color(0.62f, 0.42f, 0.74f, 1f),
        ambientGround = new Color(0.28f, 0.18f, 0.40f, 1f),
        ambientIntensity = 1.0f,
        keyEuler = new Vector3(30f, 300f, 0f), keyColor = new Color(0.74f, 0.70f, 1.0f, 1f), keyIntensity = 0.85f,
        fillEuler = new Vector3(-14f, 120f, 0f), fillColor = new Color(1.0f, 0.38f, 0.80f, 1f), fillIntensity = 0.50f,
        keyShadows = true, keyShadowStrength = 0.72f, shadowDistance = 190f,
        bloomThreshold = 1.0f, bloomIntensity = 0.90f,
        exposure = 0.92f, saturation = 1.35f, contrast = 1.06f,
        lift = new Color(0.030f, 0.014f, 0.050f, 0f), gain = new Color(1.02f, 0.96f, 1.10f, 0f),
        vignette = 0.24f, vignetteSoftness = 0.78f,
        dofFocusDistance = 90f, dofFocusRange = 420f, dofStrength = 0.55f,
        dofFalloff = 1.50f, dofIterations = 2,
        aerialStart = 90f, aerialRange = 700f, aerialDesaturation = 0.18f, aerialFlatten = 0.18f,
        aerialTint = new Color(0.46f, 0.26f, 0.70f, 1f), aerialTintAmount = 0.20f,
        mistBaseY = 38f, mistTopY = 64f, mistStrength = 0.10f, mistStart = 160f,
        mistColor = new Color(0.30f, 0.16f, 0.52f, 1f),
    };

    public static readonly Ambience MapleCityAmbience = new Ambience
    {
        // Warm grey-gold urban haze. Denser than the pass (0.00038) because the city is
        // COMPACT: the far side of the 5 km loop is only ~1.3 km away, and without real haze
        // there the opposite boulevard would read as a crisp cardboard cut-out instead of as
        // distance. At 0.00055, exp2 fog leaves 200 m of road at ~1% fog, puts ~10% on the
        // 600 m mid-distance and ~40% on the 1.3 km far side - three readable depth planes.
        // COLOUR, measured: this was (0.86, 0.74, 0.64). Fog is the single largest contributor to
        // a street shot in a compact city - at these distances it dominates the floor entirely,
        // and darkening the paving albedo by 19% moved the rendered pixels by only 1.6% because
        // what was actually being seen WAS the fog. Its green sat 0.01 below the red/blue
        // midpoint and, multiplied by the grade's 1.26 saturation, that is the rose cast that
        // read as "mauve pavement". Same warmth and value, green lifted back above the midpoint,
        // so the haze reads grey-gold instead of dusty pink.
        fog = new Color(0.820f, 0.762f, 0.672f, 1f), fogDensity = 0.00055f,

        // Trilight ambient. RAISED ~1.6x on 2026-09-14 as part of the cross-region ambience
        // back-fix, and this is a RUNTIME correctness fix, not a look tweak.
        //
        // THE BUG. Under Trilight, the ambient probe is the ONLY fill on a surface the key does
        // not reach. At sky 0.288 / equator 0.446 / ground 0.187 there was almost nothing there,
        // so anything the key missed went to near-black - and the thing the key missed most
        // reliably was the RIDER, who is a small vertical object whose camera-facing side is by
        // definition the side pointing away from a key that lit +Z. That is the whole "Kuro
        // renders as a black silhouette" report: not a material bug, not a capture bug, a genuine
        // lighting bug that shipped into play mode, because ApplyAmbience is the runtime
        // authority and overwrites anything an editor pass set.
        //
        // The hue relationships are preserved exactly (cool dusk sky, warm plaster bounce, warm
        // dark ground); only the level moved. The city's sunset identity is in the KEY colour and
        // the fog, which are untouched.
        ambientSky = new Color(0.461f, 0.530f, 0.691f, 1f),
        ambientEquator = new Color(0.602f, 0.505f, 0.437f, 1f),
        ambientGround = new Color(0.318f, 0.282f, 0.269f, 1f),
        ambientIntensity = 1.0f,   // inert while ambientMode is Trilight; kept for documentation

        // AZIMUTH 118 -> 300 deg, the second half of the same back-fix.
        //
        // At Y = 118 the key's forward vector is about (+0.88, -, -0.47): the light TRAVELS
        // toward -Z, which means it lights surfaces whose normals point +Z. The ride camera and
        // every diagnostic camera sit behind the rider looking forward, so the surfaces facing
        // the lens point -Z and received key light of exactly zero. Combined with the dark
        // ambient above, that is a black cut-out in a lit street.
        //
        // Y = 300 gives forward ~(-0.87, -, +0.50): the light travels +Z and -X, raking
        // camera-facing surfaces from the upper right. THE ELEVATION IS DELIBERATELY UNCHANGED at
        // 34 deg, because that is where the design's "long building shadows across the boulevard"
        // note lives - the shadows stay exactly as long, they simply now fall across the frame
        // instead of away from it.
        keyEuler = new Vector3(34f, 300f, 0f),
        // INTENSITY, measured: at 1.30 the sunlit city FLOOR clipped - cutting its albedo by 36%
        // moved the rendered pixels by only 2.6%, which is the signature of a surface saturating
        // before the grade ever sees it. A clipped surface loses its slab/joint detail and takes
        // the key's own red bias as a rose tint, which is the other half of the "mauve pavement"
        // report. The pass gets away with 1.30 because its ground is dark asphalt and darker
        // grass; a pale stone city needs the key pulled back to keep paving off the ceiling.
        keyColor = new Color(1f, 0.855f, 0.675f, 1f), keyIntensity = 1.12f,
        // Cool sky-bounce fill from the opposite quarter - this is what keeps the shaded side
        // of a street blue rather than black, and it is half of the design's warm/cool split.
        // Moved to stay opposite the key now that the key has rotated.
        fillEuler = new Vector3(-14f, 120f, 0f),
        fillColor = new Color(0.54f, 0.65f, 0.92f, 1f), fillIntensity = 0.34f,

        // Buildings are 3-6 storeys, so the shadow casters are far taller than the pass's trees
        // and the road needs shadow coverage much further out or the stripes pop in at 70 m.
        keyShadows = true, keyShadowStrength = 0.80f, shadowDistance = 190f,

        // Warm golden grade. Bloom runs hotter and lower-threshold than either sibling region
        // ON PURPOSE: lit windows, neon signage and paper lanterns are the artificial-light
        // layer no other region has, and they only read as light sources if they bloom.
        bloomThreshold = 1.42f, bloomIntensity = 0.42f,
        // The pavement/plaster base values were originally pushed near-white so the neutral
        // textures showed unmodified - and the result blew out under this key. They are now
        // authored around 0.72-0.75 instead, so the key can stay warm and strong without the
        // whole street clipping. Contrast carries a little more of the shaping in exchange.
        // HDRP-LIVE CORRECTION. Once SakuraGradeCustomPass actually ran this grade for the first
        // time (it was silently dead under HDRP's OnRenderImage gap - see SakuraPostFX.cs), a
        // render comparison showed contrast=1.12 combined with this region's low 0.78 exposure
        // crushing the shaded building facades to near-black across the whole street (buildings
        // that read as light grey/blue before the grade applied came back nearly solid black,
        // leaving only lit windows, foliage and sky). That combination sits outside the range
        // proven safe elsewhere in the game - Sakura and Azora pair exposure 0.80 with contrast
        // 1.02-1.06 (Fuji, which also runs 0.88 exposure, needed its own 1.14 -> 1.04 correction
        // for the identical crush once its grade went live - see FujiAmbience). Dialed back to
        // 1.03 here, the smallest change that removes the crush, so
        // exposure keeps doing the warm-key shaping described above and contrast is no longer
        // pushing the same shadows a second time. PROVISIONAL, needs an art pass to confirm the
        // street's warm/cool split still reads as intended now that it is actually visible.
        exposure = 0.78f, saturation = 1.26f, contrast = 1.03f,
        // LIFT WARM -> COOL (2026-09-25 claude, C7). The warm lift (0.040, 0.028, 0.018) pushed
        // every shadow brown: in life_chase_0120m the shaded asphalt measured RGB 55/46/35 and
        // the shaded grass 69/61/44 - blue crushed across the whole shaded street, the opposite
        // of the design's "warm key, cool blue shadow". Same total lift, hue flipped (the Fuji
        // pattern: cool lift against a warm gain); the sunlit side keeps the warm gain.
        lift = new Color(0.018f, 0.024f, 0.034f, 0f),   // 0.044 blue read navy on the shaded road
        gain = new Color(1.05f, 1.00f, 0.95f, 0f),
        vignette = 0.18f, vignetteSoftness = 0.78f,
        // Enclosed streets: the interesting depth is 20-400 m, not the pass's 160-860 m.
        dofFocusDistance = 90f, dofFocusRange = 420f, dofStrength = 0.55f,
        dofFalloff = 1.50f, dofIterations = 2,

        // Atmosphere: the city OPTS IN (Shiosai opts out by leaving aerialRange at 0). The
        // design asks explicitly for "distant buildings desaturate into warm grey", which is
        // exactly what aerialDesaturation + a warm tint does, and it is the cheapest way to
        // stop a dense skyline reading as noise.
        // TINTS, measured: aerialTint was (0.88, 0.80, 0.74) and mistColor (0.93, 0.87, 0.82).
        // Each carries only a ~0.01 green deficit, which looks harmless in isolation - but they
        // are applied over the WHOLE frame, on top of the fog and the warm key, and the deficits
        // stack. On a grey city floor the sum is the residual rose. Same warmth and value, green
        // nudged just above the red/blue midpoint so the haze is grey-gold, not dusty pink.
        //
        // HDRP-LIVE CORRECTION (round 2, aerial/mist over-application). Now that the grade is
        // actually live, diag_maple_terrace_overlook - the city's one elevated look-back shot -
        // read as almost shadowless and flat (mean brightness ~195/255, effectively the whole
        // frame sitting in a narrow washed-out mid-band), and the skyline in the rider shots was
        // unusually pale/flattened. Root cause: mistBaseY/mistTopY (38/64 m ABSOLUTE world Y) sit
        // ABOVE every building in a 3-6 storey city, so the height gate never actually fades -
        // essentially the WHOLE city sits "at or below the base" and gets mist at FULL strength,
        // stacked with aerial desaturation/flatten that were tuned as strong as Sakura's (a
        // 950 m sightline region) despite this city's loop being 1.3 km across at its widest.
        // This is the same "never actually confirmed against a live frame" class of bug as the
        // facade-shadow crush fixed above - the numbers were authored once, pre-HDRP-migration,
        // and this is the first render that ever actually applied them. Strength alone moves
        // (not the height band, which is a design/geometry decision out of scope for a grade
        // retune): aerialDesaturation 0.55 -> 0.35, aerialFlatten 0.45 -> 0.28, mistStrength
        // 0.35 -> 0.16 - enough that distant buildings still desaturate into the documented warm
        // grey and the street-level haze still reads, but the near-ground city no longer washes
        // its own shadow model out. Re-verified against diag_maple_terrace_overlook and the
        // diag_maple_rider_0/1 skyline together. PROVISIONAL, needs an art pass.
        aerialStart = 90f, aerialRange = 700f,
        aerialDesaturation = 0.35f, aerialFlatten = 0.28f,
        aerialTint = new Color(0.860f, 0.812f, 0.752f, 1f), aerialTintAmount = 0.28f,
        mistBaseY = 38f, mistTopY = 64f, mistStrength = 0.16f, mistStart = 160f,
        mistColor = new Color(0.910f, 0.878f, 0.830f, 1f),
    };

    /// <summary>
    /// AZORA HIGHLANDS - "Earn the View". Clean, high-altitude late-morning daylight.
    ///
    /// The fourth distinct time of day on the map, and the design doc is unusually explicit
    /// about what it must NOT be: not Sakura's low amber sunset, not Maple City's enclosed
    /// golden hour, and "not a cold cobalt alpine" either. It is thin, clean, high-altitude
    /// light at mid-morning - strong near-white sun, deep blue zenith, and air so clear that
    /// the payoff at the col genuinely reads as thirty kilometres of visible world.
    ///
    /// Four numbers here are load-bearing and were chosen against this region's geometry rather
    /// than copied from a sibling:
    ///
    ///   fogDensity = 0.00009. The LOWEST in the game, by a wide margin (the city runs 0.00055,
    ///   six times denser). Every other region uses fog to hide a short sightline; Azora's whole
    ///   premise is the opposite. At 0.00009, exp2 fog leaves a 1 km ridge at only ~8% haze and
    ///   a 5 km horizon at ~80%, so the far valley is visible-but-distant instead of a wall.
    ///   The depth cue up here is AERIAL PERSPECTIVE, not fog density - see below.
    ///
    ///   aerialStart = 300 / aerialRange = 4200. Roughly six times the city's range, because
    ///   the col looks out over the entire climbing stem: the Meadow Gate start is ~7 km away
    ///   in plan and 1,080 m below. Desaturating and flattening over 4.2 km is what turns that
    ///   into receding blue-grey ranges rather than one flat green field, and the design calls
    ///   the reveal "the payoff of the region".
    ///
    ///   mistBaseY = 880 / mistTopY = 1210. ABSOLUTE WORLD Y, and this is the trap that would
    ///   have silently killed the effect: Azora's road runs from y = 900 m to y = 1,980 m, so
    ///   Sakura's 6..34 m band and even Maple City's 38..64 m band sit kilometres UNDERGROUND
    ///   here and the mist would simply never appear. 880..1210 pools valley haze around the
    ///   start and the lower switchbacks and leaves everything from the pasture up standing
    ///   clear above it - which is precisely the "climbing out of the morning haze" read.
    ///
    ///   keyEuler.x = 52 deg. High enough to be mid-morning and to keep the turf reading GREEN
    ///   rather than amber, low enough that the dry-stone walls and the rolling shoulders still
    ///   model in relief. A flatter sun would erase the landform this region is made of.
    ///
    /// Everything below is PROVISIONAL art tuning.
    /// </summary>
    // WINTER (2026-09-26 user directive: "weather must be snow, photorealistic snow"). The summer
    // block this replaced is described in the summary above; what changed and why:
    //  * FOG thicker and cool-white: falling snow and ice crystals put a real haze in the air,
    //    and it is the cue that makes distant snowfields read as distance instead of white paper.
    //  * AMBIENT GROUND BRIGHT and cool: a snowfield is a ~90%-albedo reflector, so the underside
    //    of everything (eaves, faces, the shadow side of trees) is lit from below. Without it the
    //    region looks like a summer scene painted white.
    //  * KEY LOWER (30 deg) and WARM, FILL strongly BLUE: low winter sun gives the warm-lit /
    //    blue-shadow split that is THE signature of photographed snow (and the concept sheet's
    //    golden light), and long shadows model the drifts.
    //  * EXPOSURE DOWN, saturation near neutral, bloom threshold kept high: snow must never clip
    //    to flat white or go cream.
    public static readonly Ambience AzoraAmbience = new Ambience
    {
        fog = new Color(0.800f, 0.852f, 0.905f, 1f), fogDensity = 0.00016f,

        ambientSky = new Color(0.585f, 0.705f, 0.868f, 1f),
        ambientEquator = new Color(0.690f, 0.724f, 0.780f, 1f),
        ambientGround = new Color(0.700f, 0.738f, 0.800f, 1f),
        ambientIntensity = 1.0f,   // inert while ambientMode is Trilight; kept for documentation

        // Azimuth 300 kept (camera-facing key on the northbound road - see the summer notes).
        keyEuler = new Vector3(30f, 300f, 0f),
        keyColor = new Color(1f, 0.915f, 0.800f, 1f), keyIntensity = 0.95f,
        fillEuler = new Vector3(20f, 122f, 0f),
        fillColor = new Color(0.520f, 0.650f, 0.920f, 1f), fillIntensity = 0.36f,

        keyShadows = true, keyShadowStrength = 0.66f, shadowDistance = 170f,

        bloomThreshold = 1.45f, bloomIntensity = 0.24f,
        exposure = 0.72f, saturation = 1.06f, contrast = 1.05f,
        lift = new Color(0.004f, 0.012f, 0.030f, 0f),
        gain = new Color(1.00f, 1.00f, 1.02f, 0f),
        vignette = 0.12f, vignetteSoftness = 0.86f,

        dofFocusDistance = 260f, dofFocusRange = 1600f, dofStrength = 0.46f,
        dofFalloff = 1.20f, dofIterations = 3,

        aerialStart = 250f, aerialRange = 3800f,
        aerialDesaturation = 0.55f, aerialFlatten = 0.50f,
        aerialTint = new Color(0.760f, 0.820f, 0.900f, 1f), aerialTintAmount = 0.36f,
        mistBaseY = 880f, mistTopY = 1210f, mistStrength = 0.46f, mistStart = 380f,
        mistColor = new Color(0.905f, 0.925f, 0.950f, 1f),
    };

    /// <summary>
    /// TAKA MOUNTAINS - "The High Road". The coldest, highest and hardest-lit region in the game.
    ///
    /// The design brief is explicit and unusually strict: "deep cobalt sky darkening toward the
    /// zenith with altitude, a hard low-humidity sun throwing crisp black rock-shadows and
    /// blinding snow glare". Three consequences drive every number below, and all three are the
    /// OPPOSITE of what the other four regions wanted.
    ///
    ///   1. THIN AIR MEANS LESS HAZE, NOT MORE. fogDensity drops to 0.00005 - half Azora's,
    ///      which was already the lowest in the game - because at 2,800 m there is barely any
    ///      atmosphere left between the rider and the next ridge. The aerial-perspective stage
    ///      does the distance work instead, and it does it by DESATURATING (0.78, the strongest
    ///      in the game) rather than by whitening: cold clean air drains colour out of far peaks
    ///      long before it fogs them.
    ///
    ///   2. THE SHADOWS ARE THE SUBJECT. A hard sun on bare granite is a contrast image, so
    ///      keyShadowStrength goes to 0.86 (Azora sits at 0.62) and the lift is pushed further
    ///      into blue than anywhere else. "Crisp black rock-shadow" in a cel-shaded world must
    ///      still READ, so it is deep blue rather than literally black - a truly black shadow on
    ///      grey rock kills the form it is supposed to describe.
    ///
    ///   3. SNOW CLIPS. Snowfields are the brightest surface in the project by a wide margin, and
    ///      exposure at Azora's 0.80 blows them to flat white paper. Exposure comes DOWN to 0.70
    ///      and the bloom threshold stays high (1.60) so glare comes from genuinely
    ///      over-range highlights - sun on ice - and not from every lit snow face at once.
    ///
    /// mistBaseY/mistTopY are ABSOLUTE WORLD Y, as on Azora. The band sits at 1,300-1,720 m:
    /// below the trailhead and up through the lower wall, so the rider climbs UP OUT of the last
    /// of the valley air and the summit is perfectly clear. That vertical escape is the whole
    /// emotional shape of the region.
    ///
    /// Everything below is PROVISIONAL art tuning.
    /// </summary>
    public static readonly Ambience TakaAmbience = new Ambience
    {
        // Cold pale blue-white: what little haze exists up here is ice crystal, not water vapour.
        fog = new Color(0.760f, 0.824f, 0.886f, 1f), fogDensity = 0.00005f,

        // Trilight, intensity folded into the colours (Trilight ignores ambientIntensity).
        // Sky is a deep saturated cobalt - the design's signature - equator cold neutral, and
        // the ground bounce is SNOW: pale, cold and unusually strong, because a snowfield throws
        // far more light back up at the rider than turf or asphalt ever does.
        // LIFTED HARD after the first render pass. The original values here were the SKYBOX
        // cobalt copied into the ambient probe, and that was a category error: under Trilight
        // the ambient IS the fill on every unlit face, so a deep-cobalt ambient rendered the
        // entire camera-facing half of the mountain as a near-black blue slab while the far side
        // sat in full sun. The trailhead and gallery frames were about 60% dead pixels.
        //
        // A real snow-covered mountain has the HIGHEST ambient bounce of any natural
        // environment - a snowfield is a 90%-albedo reflector pointing straight up at a bright
        // sky - so these are now the brightest ambient values in the game, and the cobalt lives
        // only in the skybox material where it belongs.
        ambientSky = new Color(0.470f, 0.580f, 0.760f, 1f),
        ambientEquator = new Color(0.660f, 0.690f, 0.730f, 1f),
        ambientGround = new Color(0.740f, 0.770f, 0.810f, 1f),
        ambientIntensity = 1.0f,   // inert while ambientMode is Trilight; kept for documentation

        // A hard, high, almost colourless sun. 58 deg is the highest key in the game: it is late
        // morning at altitude, and it keeps the snowfields lit flat-on so they glare while the
        // rock walls still model. Any warmth here would read as sunset and undo the cold.
        // AZIMUTH REVERSED after the first render pass, and this is a compositional fix rather
        // than a colour one. At Y = 156 deg the key's forward vector is about (+0.41, -, -0.91):
        // the light travels toward -Z, so it lights surfaces whose normals point +Z. Every
        // diagnostic camera - and the ride camera - looks ALONG the route, which runs north, so
        // every slope facing the lens pointed -Z and sat in full shade. Two thirds of the
        // trailhead and gallery frames were a dead blue-grey wall no amount of ambient could
        // rescue, because the geometry was genuinely unlit.
        //
        // Y = 300 deg gives forward ~(-0.87, -, +0.50): the light travels +Z and -X, so it rakes
        // camera-facing slopes from the upper right. That is a three-quarter key, which is what
        // models rock, rather than the back-key that flattened it.
        keyEuler = new Vector3(56f, 300f, 0f),
        // Pushed to NEUTRAL-COOL. At 0.968 blue the key was warm enough that lit granite and
        // half-weight snow mixed to a khaki tan on the big far slopes - the one warm note in a
        // region whose whole brief is cold.
        keyColor = new Color(0.972f, 0.984f, 1f, 1f), keyIntensity = 0.98f,
        // Strong cobalt sky-bounce fill. This is the highest fill intensity in the game (0.38)
        // and it is doing real work: on a bare mountain the shadow side of every rock is lit
        // ONLY by the sky, and that deep blue against the white key is the region's entire
        // warm/cool separation.
        // Kept roughly opposite the key so the sky-bounce fills what the key now leaves.
        fillEuler = new Vector3(20f, 122f, 0f),
        fillColor = new Color(0.436f, 0.588f, 0.878f, 1f), fillIntensity = 0.38f,

        // Hard, long shadows. The casters are guardrail, snow poles, gallery pillars and the
        // hairpin stack itself - all high-frequency verticals whose shadows ARE the texture of
        // the wall. 190 m to reach across a full hairpin stack.
        keyShadows = true, keyShadowStrength = 0.86f, shadowDistance = 190f,

        // Snow protection: exposure down, threshold high. See note 3 above.
        bloomThreshold = 1.60f, bloomIntensity = 0.26f,
        // HDRP-LIVE CORRECTION. This region's exposure (0.70, the lowest in the game - see
        // "snow protection" above) was never paired with contrast=1.14 through a real render
        // until SakuraGradeCustomPass revived the grade under HDRP; every Taka diagnostic frame
        // came back darker AND more saturated than intended, with snowfields and the road
        // crushing to a near-black navy instead of the documented "rock shadow goes blue, never
        // black / snow stays snow, never black". Fuji needed the identical correction for the
        // identical reason once its own grade went live (1.14 -> 1.04, see FujiAmbience);
        // Taka's exposure has to stay low to
        // protect the snow from blow-out, so contrast is the one dialed back instead, to 1.04 -
        // the smallest change that stops the crush without touching the exposure/lift/gain trio
        // that actually carries this region's cold-mountain identity. PROVISIONAL, needs an art
        // pass now that the grade is actually visible.
        exposure = 0.70f, saturation = 1.10f, contrast = 1.04f,
        // The deepest cool lift in the game - rock shadow goes blue, never black - against a
        // faintly cool gain so the snow stays snow and never turns cream.
        lift = new Color(0.004f, 0.018f, 0.042f, 0f),
        gain = new Color(0.99f, 1.00f, 1.03f, 0f),
        vignette = 0.16f, vignetteSoftness = 0.82f,

        // The subject of a Taka frame is usually a wall or a range kilometres away, so focus
        // reaches even further out than Azora's.
        dofFocusDistance = 300f, dofFocusRange = 2200f, dofStrength = 0.40f,
        dofFalloff = 1.20f, dofIterations = 3,

        // Distance is sold by DESATURATION, not by fog - see note 1.
        // PULLED WAY BACK after the first render pass. Starting the aerial stage at 420 m with
        // 0.78 desaturation drained every frame past the next corner to flat grey - the hairpin
        // stack, which should read as a wall of stacked road, came back as a fog-bound dome.
        // Thin dry air is the region's premise, so the stage now starts nearly a kilometre out
        // and only half-desaturates: distance is sold by the RANGE TINTS and the sky, not by
        // washing the mountain the rider is standing on.
        aerialStart = 950f, aerialRange = 9000f,
        aerialDesaturation = 0.48f, aerialFlatten = 0.38f,
        aerialTint = new Color(0.640f, 0.720f, 0.842f, 1f), aerialTintAmount = 0.22f,
        // ABSOLUTE WORLD Y. Valley air the rider climbs out of; the summit is clear.
        mistBaseY = 1300f, mistTopY = 1720f, mistStrength = 0.24f, mistStart = 700f,
        mistColor = new Color(0.846f, 0.888f, 0.930f, 1f),
    };

    /// <summary>
    /// FUJI RIDGE - "Clouds Above". The sacred one, and the only region whose atmosphere IS the
    /// gameplay beat rather than a backdrop to it.
    ///
    /// THE CLOUD SEA IS THE MIST BAND, AND THAT IS NOT A COMPROMISE - IT IS THE RIGHT TOOL.
    /// The design doc's hero element is "a flat, glowing cloud deck sitting at ~1,200 m that the
    /// rider climbs OUT of", with dense cool mist and near-total desaturation below the break
    /// and crystalline clean air above it. SakuraPostFX's mist stage is a height-banded fog
    /// keyed on ABSOLUTE WORLD Y with its own colour and its own start distance - which is
    /// precisely a cloud deck. Setting the band to 900..1,215 m puts its top surface within
    /// twenty metres of where the route generator actually lands the Cloudbreak Overlook
    /// checkpoint (5.98 km, 1,194.6 m), so the reveal happens AT the named landmark, on the
    /// geometry, with no scripting and nothing to keep in sync.
    ///
    /// mistStrength is 0.62, by far the highest in the game (Sakura 0.45, Azora 0.42, Taka
    /// 0.24). Every other region uses mist as seasoning and wants to see through it. Fuji wants
    /// the valley to be GONE - the doc calls it "a blue ghost" - so that cresting the deck reads
    /// as arrival in a different world.
    ///
    /// DAWN, NOT SUNSET. Sakura and Maple City are both warm-low-sun regions already, so the
    /// risk here was building a third one. What separates Fuji is that its warmth is a narrow
    /// BAND rather than a global wash: the key is a low, blazing, strongly amber sunrise, but
    /// the ambient probe stays deep pre-dawn blue and the grade lifts the shadows cold. The
    /// result is the doc's "black rock, white cloud, and a single band of sunrise fire" instead
    /// of Sakura's all-over pink.
    ///
    /// KEY ELEVATION: 40 DEGREES, NOT THE 14 THE DAWN IDENTITY ARGUES FOR. This was measured,
    /// not guessed. At 14 and again at 24 degrees the cone shadows itself across most of its own
    /// surface and every capture came back a pure silhouette - fell pixels at RGB 4/6/8, uniform
    /// to within five levels, with no texture visible at any distance, on a ground material that
    /// a line-by-line diff proved byte-identical to Taka's working one. Taka runs 56 degrees and
    /// reads correctly; Maple City runs 34. Fuji now sits between them.
    ///
    /// The dawn identity is CARRIED BY COLOUR, NOT BY ANGLE, and that is the right place for it:
    /// a strongly amber key against a cool pre-dawn ambient, the warm mist band in the cloud sea
    /// and a cold-lift/warm-gain grade all still say sunrise, and they say it on a mountain the
    /// player can actually see. An authentic 14-degree sun photographs a black triangle.
    /// </summary>
    public static readonly Ambience FujiAmbience = new Ambience
    {
        // Sky/fog: deep pre-dawn blue, lifted toward coral only where the mist band catches the
        // key. fogDensity sits between Azora's and Taka's - the base is genuinely humid cedar
        // forest, but the summit air above the deck is thin and clean, and the mist stage rather
        // than the global fog is what sells the difference.
        fog = new Color(0.706f, 0.734f, 0.812f, 1f), fogDensity = 0.00006f,

        // Trilight ambient. Deep pre-dawn blue sky, a coral-touched equator (the cloud deck
        // below IS a giant coral-lit reflector once the sun crests it, which is a real and very
        // characteristic bounce on this mountain), and a near-black volcanic ground bounce -
        // black scoria returns almost nothing, and that is what keeps the cinder reading black
        // instead of grey.
        //
        // Authored at the corrected LEVEL from the start: this region never shipped the dark
        // probe that made riders read as silhouettes on Maple City and Azora.
        ambientSky = new Color(0.560f, 0.626f, 0.772f, 1f),
        ambientEquator = new Color(0.646f, 0.602f, 0.584f, 1f),
        ambientGround = new Color(0.520f, 0.502f, 0.510f, 1f),
        ambientIntensity = 1.0f,   // inert while ambientMode is Trilight; kept for documentation

        // The sunrise itself: very low, very warm, and strong. Intensity is high because it has
        // to carve a black mountain out of a blue pre-dawn - a weak key here reads as overcast.
        keyEuler = new Vector3(30f, 300f, 0f),
        keyColor = new Color(1f, 0.786f, 0.624f, 1f), keyIntensity = 1.22f,
        // Cold sky-bounce from the opposite quarter, kept deliberately weak. Fuji's shadows are
        // supposed to be DEEP - "reverent quiet", "stillness" - and a strong fill would turn the
        // doc's monochrome cone into a pleasant blue-grey hillside.
        fillEuler = new Vector3(26f, 120f, 0f),
        fillColor = new Color(0.500f, 0.592f, 0.836f, 1f), fillIntensity = 0.36f,

        // Long shadows from a 14-degree sun stretch enormously, which is the point; the reach
        // has to cover them or they visibly truncate across the cinder.
        keyShadows = true, keyShadowStrength = 0.80f, shadowDistance = 240f,

        // Bloom is restrained and thresholded HIGH on purpose. The doc asks for "restrained
        // bloom off the snow and the cloud horizon" - the cloud deck is a huge bright surface
        // and a low threshold would bleed it over the whole frame, destroying the crisp cone
        // silhouette that is the region's single most important read.
        bloomThreshold = 1.85f, bloomIntensity = 0.18f,
        // HDRP-LIVE CORRECTION. This block's own comment used to claim "Fuji runs 1.14 contrast
        // safely because it pairs it with a much higher 0.88 exposure" - that was NEVER actually
        // confirmed against a live render (Fuji's grade, like every region's, was a silent no-op
        // under HDRP's dead OnRenderImage until SakuraGradeCustomPass). A 13+ shot pixel-sampled
        // QA pass (cloud_sea, cloudbreak, cone, drop, cedar_belt, rider_1, parapet, scree,
        // strata_wall, treeline) found the exact same crush floor (~RGB 17,31,54) as Sakura and
        // pre-fix Taka - contrast=1.14 is a stale value-class that predates any of these regions
        // actually being graded, not a value proven safe by exposure pairing. Taka needed the
        // identical correction for the identical reason (1.14 -> 1.04, see TakaAmbience); Fuji
        // gets the same absolute change here for the same reason - the smallest move that
        // removes the crush without touching the exposure/lift/gain trio that carries this
        // region's sunrise identity. PROVISIONAL, needs an art pass now that the grade is
        // actually visible.
        exposure = 0.88f, saturation = 1.10f, contrast = 1.04f,
        // Cold lift against a warm gain: shadows go blue, lit rock and cloud go coral. This one
        // pair of values is what produces "black rock, white cloud, one band of fire".
        lift = new Color(0.006f, 0.014f, 0.038f, 0f),
        gain = new Color(1.04f, 0.99f, 0.96f, 0f),
        vignette = 0.20f, vignetteSoftness = 0.78f,

        // The subject is usually the cone or the cloud horizon, both far away.
        dofFocusDistance = 280f, dofFocusRange = 1900f, dofStrength = 0.42f,
        dofFalloff = 1.20f, dofIterations = 3,

        // Aerial: moderate reach, and tinted COOL even though the key is warm. Distance on this
        // mountain goes blue-grey, not gold - the gold is a band at the horizon, not a haze.
        aerialStart = 900f, aerialRange = 7000f,
        aerialDesaturation = 0.38f, aerialFlatten = 0.26f,
        aerialTint = new Color(0.624f, 0.690f, 0.828f, 1f), aerialTintAmount = 0.30f,

        // THE CLOUD SEA. ABSOLUTE WORLD Y, and the single most important pair of numbers in this
        // block. Base 820 m is below the Cedar Base Gate at 776 m + the forest canopy, so the
        // lower forest is genuinely mist-hung; top 1,215 m is 20 m above the Cloudbreak Overlook
        // checkpoint, so the rider climbs out of the deck exactly at the landmark named for it.
        // mistStart is short (180 m) because a cloud you are INSIDE begins at arm's length.
        //
        // STRENGTH AND START RESTORED, 2026-09-24. The two numbers had drifted to
        // mistStrength 0.14 / mistStart 1500 m - i.e. a whisper of haze that does not even begin
        // until a kilometre and a half from the lens - which is why diag_fuji_cloud_sea rendered
        // a clear blue-sky mountain with NO cloud deck anywhere in it, on the one region whose
        // design brief calls the cloud sea "the defining feature" and whose reveal above it is
        // the whole emotional shape of the climb. The prose four lines above had said 0.62 all
        // along; only the values had moved. They now match the documentation again.
        //
        // WHY 0.62 IS NOT EXCESSIVE HERE. Every other region uses mist as seasoning and wants to
        // see through it (Sakura 0.45, Azora 0.42, Taka 0.24). Fuji wants the valley GONE - the
        // brief calls it "a blue ghost" - so that cresting the deck reads as arrival in a
        // different world. The band is hard-capped at 1,215 m in ABSOLUTE world Y, so this
        // cannot touch the summit, the knife-edge ridge or the torii: above the deck the air
        // stays crystalline exactly as the brief asks. PROVISIONAL art tuning.
        mistBaseY = 820f, mistTopY = 1215f, mistStrength = 0.62f, mistStart = 180f,
        mistColor = new Color(0.952f, 0.858f, 0.822f, 1f),
    };

    public static Ambience AmbienceFor(string regionId) =>
        regionId == RegionCatalog.ShiosaiCoast ? ShiosaiAmbience :
        regionId == RegionCatalog.MinatoCoast ? MinatoAmbience :
        regionId == NagisaBayLook.RegionId ? NagisaBayLook.Ambience :   // B5 (copilot)
        regionId == RegionCatalog.MapleCity ? (MapleCityVaporwave ? MapleCityVaporAmbience : MapleCityAmbience) :
        regionId == RegionCatalog.TakaMountains ? TakaAmbience :
        regionId == RegionCatalog.FujiRidge ? FujiAmbience :
        regionId == RegionCatalog.AzoraHighlands ? AzoraAmbience : SakuraAmbience;

    // ---- Per-region skybox ---------------------------------------------------------------
    // Serialized so the editor pass pushes the real material assets onto the instance (a code
    // default cannot reference an asset, and Resources.Load would need a second copy).
    [Header("Per-region sky (pushed by the setup pass)")]
    public Material sakuraSky;
    public Material shiosaiSky;
    public Material mapleCitySky;
    public Material azoraSky;
    public Material takaSky;
    public Material fujiSky;

    [Tooltip("The one shared key/fill directional light pair, matched by exact name.")]
    public Light keyLight;
    public Light fillLight;

    public Material SkyFor(string regionId) =>
        regionId == RegionCatalog.ShiosaiCoast ? shiosaiSky :
        regionId == RegionCatalog.MinatoCoast ? shiosaiSky :
        regionId == NagisaBayLook.RegionId ? shiosaiSky :   // B5 (copilot): HDRP volume owns the sky
        regionId == RegionCatalog.MapleCity ? mapleCitySky :
        regionId == RegionCatalog.TakaMountains ? takaSky :
        regionId == RegionCatalog.FujiRidge ? fujiSky :
        regionId == RegionCatalog.AzoraHighlands ? azoraSky : sakuraSky;

    private void ResolveLights()
    {
        if (keyLight == null || fillLight == null)
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l == null || l.type != LightType.Directional) continue;
                if (keyLight == null && l.name == "Sakura Sunset Key") keyLight = l;
                if (fillLight == null && l.name == "Sakura Valley Fill") fillLight = l;
            }
        }
    }

    /// <summary>
    /// Applies the whole time-of-day look for the current region: fog, ambient, skybox, the
    /// shared key/fill lights and the post-FX grade on every camera that carries one.
    /// </summary>
    public void ApplyAmbience()
    {
        var a = AmbienceFor(currentRegionId);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = a.fog;
        RenderSettings.fogDensity = a.fogDensity;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = a.ambientSky;
        RenderSettings.ambientEquatorColor = a.ambientEquator;
        RenderSettings.ambientGroundColor = a.ambientGround;
        RenderSettings.ambientIntensity = a.ambientIntensity;

        ResolveLights();
        if (keyLight != null)
        {
            keyLight.transform.rotation = Quaternion.Euler(a.keyEuler);
            keyLight.color = a.keyColor;
            keyLight.intensity = a.keyIntensity;
            // Part B section 24: the sun's shadow state is part of the region's ambience, not a
            // one-off editor setting. Without this, entering play mode silently reverted the
            // shadow work to whatever the prefab/scene happened to carry.
            if (a.keyShadowStrength > 0f)
            {
                keyLight.shadows = a.keyShadows ? LightShadows.Soft : LightShadows.None;
                keyLight.shadowStrength = a.keyShadowStrength;
                // Built-in Forward has ONE per-pixel ForwardBase slot and this scene carries 91
                // realtime point lights; left on Auto they out-rank the sun near the road and it
                // is demoted to an additive pass that carries no shadows at all.
                keyLight.renderMode = LightRenderMode.ForcePixel;
                if (a.shadowDistance > 0f) QualitySettings.shadowDistance = a.shadowDistance;
            }
            RenderSettings.sun = keyLight;
        }
        if (fillLight != null)
        {
            fillLight.transform.rotation = Quaternion.Euler(a.fillEuler);
            fillLight.color = a.fillColor;
            fillLight.intensity = a.fillIntensity;
            fillLight.renderMode = LightRenderMode.ForceVertex;   // never contest the sun's slot
        }

        // HDRP owns the sky and the grade in this project; the ShiosaiCoast look lives on the
        // scene-local "Shiosai Sky Volume" (ShiosaiCoastEnvironment.ConfigureCoastSkyVolume),
        // which outranks anything pushed here. RenderSettings.skybox below is Built-in-only and
        // is kept solely so the cel shaders' ambient/sun-disc plumbing stays consistent.
        var sky = SkyFor(currentRegionId);
        if (sky != null)
        {
            RenderSettings.skybox = sky;
            // The sky shader draws its own sun disc; keep it on the actual key light.
            if (keyLight != null && sky.HasProperty("_SunDirection"))
                sky.SetVector("_SunDirection", -keyLight.transform.forward);
            DynamicGI.UpdateEnvironment();
        }

        foreach (var fx in FindObjectsByType<SakuraPostFX>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None))
        {
            if (fx == null) continue;
            fx.bloomThreshold = a.bloomThreshold;
            fx.bloomIntensity = a.bloomIntensity;
            fx.exposure = a.exposure;
            fx.saturation = a.saturation;
            fx.contrast = a.contrast;
            fx.lift = a.lift;
            fx.gain = a.gain;
            fx.vignetteStrength = a.vignette;
            fx.vignetteSoftness = a.vignetteSoftness;
            fx.dofFocusDistance = a.dofFocusDistance;
            fx.dofFocusRange = a.dofFocusRange;
            fx.dofStrength = a.dofStrength;
            if (a.dofFalloff > 0.001f) fx.dofFalloff = a.dofFalloff;
            if (a.dofIterations > 0) fx.dofIterations = a.dofIterations;

            // Section 25 atmosphere. Opt-in per region: Shiosai leaves aerialRange at 0.
            if (a.aerialRange > 0.001f)
            {
                fx.aerialStart = a.aerialStart;
                fx.aerialRange = a.aerialRange;
                fx.aerialDesaturation = a.aerialDesaturation;
                fx.aerialFlatten = a.aerialFlatten;
                fx.aerialTint = a.aerialTint;
                fx.aerialTintAmount = a.aerialTintAmount;
                fx.mistBaseY = a.mistBaseY;
                fx.mistTopY = a.mistTopY;
                fx.mistStrength = a.mistStrength;
                fx.mistStart = a.mistStart;
                fx.mistColor = a.mistColor;
            }
        }
    }

    private void Awake()
    {
        Resolve();
        SyncFromSession();
    }

    public void Resolve()
    {
        if (boot == null) boot = GetComponent<RideBootstrap>();
        if (session == null) session = boot != null ? boot.session : GetComponent<RideSession>();
        if (follower == null && boot != null) follower = boot.follower;
        if (streamer == null)
            streamer = FindFirstObjectByType<RouteDressingStreamer>(FindObjectsInactive.Include);
    }

    /// <summary>Reads the region back off whichever course the session currently rides.</summary>
    public void SyncFromSession()
    {
        if (session != null && session.Graph == null) session.EnsureCourse();

        // NEVER EARLY-RETURN WITHOUT APPLYING VISIBILITY.
        //
        // Every region shares ONE scene and this component is the only thing that hides the
        // ones you are not in, so returning here leaves the scene in whatever state it was
        // SERIALIZED in. When an editor build pass had saved the scene with the coast showing,
        // that meant a player starting a Sakura Pass ride got the coast's ocean plane and inland
        // ranges rendered over their course - the HUD said Sakura Circuit while the view was
        // Shiosai. A missing session or an unresolved graph is exactly the boot-order case where
        // that is most likely, so fall back to the default region and apply it rather than
        // leaving the saved state untouched.
        string resolved = (session != null && session.Graph != null)
            ? session.Graph.RegionOfCourse(session.courseId)
            : RegionCatalog.SakuraPass;

        currentRegionId = resolved;
        ApplyEnvironmentVisibility();
        ApplyAmbience();
    }

    public bool CanTravelTo(string regionId)
    {
        var region = RegionCatalog.Find(regionId);
        if (region == null || !region.Unlocked) return false;
        // The session only resolves its Graph when it boots, so asking before Awake (an editor
        // self test, or a world map opened on the very first frame) used to see a null graph and
        // report EVERY pin as locked. Fall back to the shared asset instead of the session's
        // not-yet-resolved handle.
        var graph = session != null && session.Graph != null ? session.Graph : RouteGraph.Load();
        return graph != null && graph.Course(region.BuiltCourseId) != null;
    }

    /// <summary>
    /// Drops the rider onto a region's built course. Returns false (and changes nothing) when
    /// the region has no road yet, so a locked pin is a no-op rather than a broken ride.
    /// </summary>
    public bool FastTravel(string regionId)
    {
        Resolve();
        var region = RegionCatalog.Find(regionId);
        if (region == null)
        {
            Debug.LogWarning($"[region] unknown region '{regionId}'.");
            return false;
        }
        if (!CanTravelTo(regionId))
        {
            Debug.Log($"[region] '{region.DisplayName}' is not built yet - pin stays locked.");
            return false;
        }
        if (session == null) { Debug.LogWarning("[region] no RideSession."); return false; }

        session.autoLapsFromTarget = true;
        session.SelectCourse(region.BuiltCourseId);
        session.ResetRide();

        currentRegionId = regionId;
        ApplyEnvironmentVisibility();
        ApplyAmbience();

        if (follower != null) { follower.enabled = boot == null || boot.routeFollowing; follower.Apply(); }
        if (streamer != null && follower != null && follower.rider != null)
            streamer.Apply(follower.rider.position);
        if (boot != null && boot.hud != null && boot.hud.map != null) boot.hud.map.Invalidate();

        Debug.Log($"[region] fast travelled to {region.DisplayName} - " +
                  $"'{session.Course?.DisplayName}' {session.Course?.Length / 1000f:0.00} km " +
                  $"x {session.TotalLaps} laps, rider at {session.WorldPosition}.");
        RegionChanged?.Invoke(regionId);
        return true;
    }

    /// <summary>
    /// Only the region being ridden is drawn. Matched by EXACT name and every matching root is
    /// handled, so a stray duplicate cannot leave half a region visible behind the other one.
    /// </summary>
    public void ApplyEnvironmentVisibility()
    {
        foreach (var region in RegionCatalog.Regions)
        {
            if (string.IsNullOrEmpty(region.EnvironmentRoot)) continue;
            bool show = region.Id == currentRegionId;
            foreach (var go in FindRoots(region.EnvironmentRoot))
                if (go.activeSelf != show) go.SetActive(show);
        }
    }

    private static System.Collections.Generic.List<GameObject> FindRoots(string exactName)
    {
        var found = new System.Collections.Generic.List<GameObject>();
        foreach (var go in gameObjectScene())
            if (go.name == exactName) found.Add(go);
        return found;
    }

    /// <summary>
    /// Every root of every LOADED scene, not just the active one. Shiosai Coast now lives in its
    /// own scene (spec 6.1) and additive segment scenes follow, so an active-scene-only scan
    /// would silently fail to find - and therefore fail to show or hide - a region root.
    /// </summary>
    private static GameObject[] gameObjectScene()
    {
        var all = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded) all.AddRange(scene.GetRootGameObjects());
        }
        return all.ToArray();
    }
}


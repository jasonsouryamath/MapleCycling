using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the thirty-rider NPC cast for FUJI RIDGE - the 13.0 km point-to-point pilgrimage climb
/// described in <c>reference/improve/region_designs/fuji_ridge.md</c> section 5.
///
/// This is the sibling of <see cref="TakaNpcRoster"/> and shares every verified construction
/// number with it, deliberately and verbatim. Taka is the closest template rather than the
/// Sakura/Maple City/Azora rosters because those three are CLOSED LOOPS: a loop has a seam to
/// hide a rider behind and a second lap on which to re-meet them, so a misjudged seating merely
/// shifts when the player sees somebody. Fuji, like Taka, is OPEN - one ascent, no laps, no
/// descent - so every rider is met exactly once, at exactly one place, and the seating has to
/// earn its place the first time.
///
/// What differs from Taka is the REGION, and only the region: the segment the riders are bound
/// to, the scene parent they hang from, the object-name prefix used to prune and re-find them,
/// the distances they are seated at, the pace they descend at, and the voice they speak in.
/// Rig, bike, scales, lean angles, wheel radius and ground offset all come from
/// <see cref="NpcCanonicalConformance"/>, which is the single authority for Kuro-matched body
/// scale and RoadRacerAggressive fit across every roster in the project. Nothing about scale is
/// re-derived here, because those numbers are a property of the RIG, not of the road.
///
/// WHY EVERY RIDER IS CORAL AGAIN. Coral's sculpt (KuroNPC_Coral_Rigged.glb) remains the only
/// full-resolution cyclist in the project - 109,358 triangles at a 0.007 median edge, against
/// 18,651 triangles and a 0.019 edge for the decimated KuroNPC_&lt;name&gt;.glb variants, which a
/// chibi silhouette does not survive. Riders are told apart by TEXTURE, never by material
/// tinting: her whole body is one 2048x2048 atlas on a single 'Material_0' slot, so a
/// baseColorFactor tint would recolour each rider's FACE along with her jersey. Instead
/// design_assets/3d/kuro/npc_palette_variants.py recolours the saturated kit pixels - leaving
/// skin and outlines byte-for-byte identical - and writes Assets/Kuro/NPC/Textures/CoralKit_*.png
/// keyed by the same names used below.
///
/// PLACEMENT IS THE WHOLE POINT OF THIS ROSTER, and it is the one thing that is NOT copied from
/// Taka. Taka spreads thirty riders at a near-uniform ~735 m because its identity is loneliness.
/// Fuji's design doc asks for the opposite shape and says so explicitly: density "near one per
/// 100 m", and "pilgrims may cluster near the base gate and the summit shrine". So this roster
/// is deliberately CLUSTERED around the region's six named landmarks - seven riders in the first
/// 700 m, five in the last 700 m, five at the weather hut, four at the Cloudbreak reveal - with
/// long, deliberately empty stretches of cedar forest and bare scoria between them. A rider is
/// placed where their ROLE puts them (the meteorologists at the station, the ash crew on the
/// hairpins they sweep and at the strata wall, the photographers where the cloud sea catches
/// fire, the scout on the knife-edge, the shrine staff at the torii), never at the next step of
/// an arithmetic sequence. See the block comment above <see cref="Roster"/>.
///
/// NAME COLLISIONS ARE A REAL HAZARD HERE. Materials (<c>&lt;Name&gt;_Body</c>), scene objects and
/// baked portrait PNGs are all keyed by rider name, and every region lives in the SAME scene
/// file. All thirty names below were checked against the Sakura, Maple City, Azora, Taka,
/// Shiosai and Minato rosters. Two of the design doc's names collided and are renamed: doc
/// rider 12 "Tatsuya" and doc rider 18 "Junpei" are both owned by MinatoNpcTraffic, and appear
/// here as TATSUHIRO and JUNNOSUKE. (Doc rider 16 "Shiro" is kept - Taka's "Shirou" is a
/// different string.)
///
/// Menu: MapleRide/NPCs/Stage Fuji Roster
/// </summary>
public static class FujiNpcRoster
{
    /// <summary>All regions share one scene file; they are separated by ROUTE SEGMENT, not by
    /// scene. See <see cref="SegmentId"/>.</summary>
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>The one surviving full-resolution cyclist sculpt - see the class remarks.</summary>
    const string RigPath = "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb";

    /// <summary>
    /// The shared production bicycle, for every rider. The baked hand grip and the canonical
    /// RoadRacerAggressive fit were solved against THIS frame's hoods at the bike scale below;
    /// another frame puts the hoods somewhere else and leaves thirty riders clutching thin air.
    /// Livery variety comes from paint, not from frame shape.
    /// </summary>
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;

    const string MaterialDir = "Assets/Kuro/NPC/Materials";
    const string KitDir = "Assets/Kuro/NPC/Textures";

    /// <summary>
    /// The Fuji climb in the baked RouteGraph, as published by
    /// Assets/Environment/FujiRidge/FujiRoute.json (3,251 samples, 13,000 m). Getting this
    /// wrong does not error - it silently stages thirty pilgrims on whichever other region owns
    /// the id, on top of the cast already there.
    /// </summary>
    const string SegmentId = "fuji";

    /// <summary>Scene parent for this region's riders, kept separate from every other region's
    /// so the cast can be toggled, moved or deleted as a unit.</summary>
    const string ParentName = "Fuji NPCs";

    /// <summary>Object-name prefix. Pruning, pace re-application and FujiRidgeDiagnostics'
    /// rider shots all match on the EXACT string <c>NamePrefix + rider name</c> - the
    /// diagnostics harness already filters on "Fuji NPC ", so this string is load-bearing and
    /// must not be reflowed.</summary>
    const string NamePrefix = "Fuji NPC ";

    // ---- canonical numbers, reused unchanged (see NpcCanonicalConformance) ------------------

    /// <summary>Bike scale, owned by the conformance authority and matched to the production
    /// rig's arm length. Never scale the bike independently of the rider.</summary>
    const float BikeScale = NpcCanonicalConformance.BikeScale;

    /// <summary>Measured seated height of the shared sculpt at scale 1, from a BAKED skinned
    /// mesh. Renderer.bounds on a SkinnedMeshRenderer is the import-time estimate and lies - it
    /// has reported this same chibi as 2.09 m tall.</summary>
    const float CoralSeatedHeight = 1.185f;

    /// <summary>Route points describe the road's underlying centreline, but the tarmac carries a
    /// parabolic crown; this clears it plus a small bias.</summary>
    const float GroundOffset = 0.065f;

    /// <summary>Must match the spacing used to resample the route.</summary>
    public const float RouteSpacing = 3.0f;

    /// <summary>
    /// PROVISIONAL: global multiplier applied to every roster rider's authored
    /// <see cref="Npc.Speed"/> before it is written onto the staged <see cref="NPCCyclist"/>.
    ///
    /// Sakura and Taka verified 0.55 / 0.58; Maple City needed 0.48 because its loop is flanked
    /// by facades and street furniture at 6 m, so near-field parallax made any ground speed read
    /// faster. Fuji sits between the two cases and is set to 0.55:
    ///
    ///   * Below Cloudbreak the road runs through dense cedar at 4-8 m, which is Maple City's
    ///     near-field parallax problem in a forest, pushing the number DOWN.
    ///   * Above the deck there is nothing within a kilometre but black cinder and cloud, which
    ///     is Taka's no-reference problem, pushing it back UP.
    ///
    /// 0.55 gives 1.76 - 2.97 m/s, i.e. 6.3 - 10.7 kph apparent. That is deliberately the
    /// calmest cast in the game: the design doc's identity for this region is reverence and
    /// stillness - "this is not a race, it is a pilgrimage" - and riders visibly hammering a
    /// descent would contradict the one thing the region is built to say.
    ///
    /// PROVISIONAL: tune here, then re-run <c>MapleRide/NPCs/Apply Fuji Pace To Scene</c> - the
    /// staged riders carry a SERIALIZED speed, so changing this constant alone does nothing to
    /// the saved scene.
    /// </summary>
    public const float PaceScale = 0.55f;

    /// <summary>The pace a rider is actually staged at, m/s.</summary>
    public static float StagedSpeed(float authoredSpeed) => authoredSpeed * PaceScale;

    // ---- the region's own landmarks -----------------------------------------------------
    //
    // Mirrored from FujiRidgeEnvironment's private Cp* constants so every distance below is
    // expressed RELATIVE TO A LANDMARK rather than as a bare number. That is what makes the
    // placement auditable: "Katsuo is 40 m below the weather hut" survives a route re-bake as an
    // intention, where "Katsuo is at 9710" does not.
    //
    // (FujiRidgeEnvironment is deliberately not modified to expose these - its internals carry a
    // known copy-paste naming artifact from TakaMountainsEnvironment and are out of scope. These
    // are read-only copies of its checkpoint distances, in metres along the "fuji" segment.)
    const float CpBaseGate = 0f;        // Cedar Base Gate        arc 0.00,   776 m elevation
    const float CpCloudbreak = 5980f;   // Cloudbreak Overlook    arc 0.46, 1,195 m
    const float CpStrata = 8060f;       // Banded Strata Wall     arc 0.62, 1,361 m
    const float CpWeatherHut = 9750f;   // Weather Station Hut    arc 0.75, 1,496 m
    const float CpKnifeEdge = 11440f;   // Knife-Edge Ridge       arc 0.88, 1,629 m
    const float CpSummit = 13000f;      // Summit Torii Shrine    arc 1.00, 1,776 m

    sealed class Npc
    {
        public string Name;
        public float TargetHeight;   // metres, seated - DESCRIPTIVE ONLY; see RigScaleFor
        public float Speed;          // m/s, authored; PaceScale is applied at staging time
        public float Lane;           // metres from the centreline; negative = oncoming lane
        public float MeetDistance;   // metres along the climb from the base gate
        public string Frame, Accent; // bike livery hexes
        public string Where;         // why this rider is HERE - logged at staging time
        public string[] Lines;
        /// <summary>True = rides UP the climb with the player (reverse = false). The original
        /// thirty are all descenders; only the MovingCyclists below use this.</summary>
        public bool Ascending;
    }

    // -----------------------------------------------------------------------------------------
    // THE CAST, IN MEET ORDER, PLACED BY ROLE.
    //
    // Thirty riders over 13.0 km is one per 433 m on average, which is roughly the doc's asked-for
    // density once its "~one per 100 m" is read as the density AT THE CLUSTERS rather than along
    // the whole climb. The average is not the design, though - the SHAPE is:
    //
    //   base gate  0 -   700 m   7 riders   ~100 m apart   the pilgrim muster before dawn
    //   hairpins   1.2 - 2.9 km  3 riders   ~600-900 m     the ash crew who sweep them, one racer
    //   cedar      2.9 - 5.0 km  0 riders   ---            THE EMPTY STRETCH. Deliberate: the
    //                                                      doc's forested grind is where the
    //                                                      region earns its solitude, and a
    //                                                      metronomic roster would fill it.
    //   treeline   5.0 - 5.3 km  2 riders   ~260 m         botanists watching the forest stop
    //   Cloudbreak 5.9 - 6.2 km  4 riders   ~100 m         the reveal - photographers, cloud-
    //                                                      chasers, a meteorologist reading it
    //   scoria     7.2 km        1 rider    ---            one figure on a bare black slope
    //   strata     8.0 - 8.1 km  2 riders   ~160 m         the cinder crew at the cutting
    //   (gap)      8.1 - 9.6 km  0 riders   ---            the exposed flank, empty on purpose
    //   hut        9.6 - 9.9 km  5 riders   ~80 m          the station and the ridge hut
    //   knife-edge 11.4 km       1 rider    ---            the safety scout, alone by definition
    //   summit    12.2 - 12.9 km 5 riders   ~175 m         the shrine: attendants and the last
    //                                                      pilgrims, walking the final ramp
    //
    // The two ends are 4-6x denser than the middle, which is the doc's explicit instruction and
    // is the thing the verification renders are built to check by eye.
    //
    // ROLE-TO-LANDMARK is not decoration either. Yusuke (meteorologist) and Katsuo (anemometer
    // technician) are AT the weather station because that is where their instruments are; Osamu
    // (the old meteorologist who reads the sky) is the one exception and sits at Cloudbreak,
    // because his line is about the cloud DECK and the deck is only readable from above it.
    // Ryosuke and Susumu sweep the hairpins they talk about; Shiro and Naoya are at the strata
    // wall whose ash they complain about; Kaede and Sae are 50 m apart because a photographer
    // and their assistant work together; Minoru the scout is alone on the knife-edge; Koji,
    // Junnosuke, Eiji, Kazuo and Tatsuo are at the torii.
    //
    // PROVISIONAL, all of it. Distances are metres along the "fuji" segment and are only
    // meaningful while that segment is 13,000 m; heights, speeds, lanes and liveries are art
    // direction and are expected to move.
    //
    // Every rider is on the oncoming side (negative lane) with reverse = true, which on a
    // point-to-point climb means they DESCEND while the player ascends. That is both correct
    // traffic and the only arrangement in which a greeting can fire at all: a rider travelling
    // the player's way at a similar speed sits permanently behind them, outside the view cone.
    //
    // LANES ARE TIGHTER THAN TAKA'S (-1.42 to -1.70 against -1.56 to -1.84). The doc fixes this
    // road at roughly 5 m wide - narrower than any other region's - and the knife-edge section
    // is "barely two lanes". Taka's offsets would hang a descending rider's outside wheel over
    // the cinder shoulder.
    //
    // SPEEDS ARE THE LOWEST IN THE GAME (3.2 - 5.4 authored, against Taka's 3.5 - 5.7 and
    // Azora's 3.7 - 6.4), and the slowest riders are the ones whose character note is slowness:
    // Eiji is walking her bike (3.2), Chiyo is an elder pilgrim (3.2), Junnosuke is carrying a
    // shimenawa rope (3.4), Tsukasa is a pilgrim with a bell (3.4). The fastest are the two
    // riders racing the light - Tatsuhiro (5.4) and Tatsuo (5.3) - and Minoru (5.2), who is
    // sweeping the ridge for the hut.
    //
    // Kit colours live in npc_palette_variants.py, keyed by these same names. The Frame/Accent
    // hexes below are the BIKE livery and are matched to each rider's kit, not to each other.
    static readonly Npc[] Roster =
    {
        // ======================================================== CEDAR BASE GATE (0 m)
        // Seven riders in the first 700 m. This is the doc's explicit "pilgrims may cluster near
        // the base gate" and it is the player's first impression of the region: a muster of
        // people in the blue hush before dawn, at a mossy torii, about to climb. No two of the
        // seven share a colour family (linen white, pale grey, silver, heather mauve, faded
        // coral, bright coral, honey amber), because at ~100 m spacing two and three of them are
        // in frame at once.
        new Npc { Name = "Tsukasa", TargetHeight = 1.40f, Speed = 3.4f, Lane = -1.46f,
                  MeetDistance = CpBaseGate + 60f, Frame = "F2EDE4", Accent = "B4382C",
                  Where = "at the base gate torii - the shrine pilgrim who opens the region",
                  Lines = new[] { "Ring the bell at the top for luck.",
                                  "We climb to give thanks, not to win.",
                                  "Bow at the gate. The mountain notices.",
                                  "Thirteen kilometres of quiet ahead of you." } },

        new Npc { Name = "Sayaka", TargetHeight = 1.34f, Speed = 3.5f, Lane = -1.58f,
                  MeetDistance = CpBaseGate + 140f, Frame = "C9D3DC", Accent = "6E3F6B",
                  Where = "just inside the gate - young pilgrim, beads on the bars",
                  Lines = new[] { "Say a word at the little shrine.",
                                  "The silence up here is the point.",
                                  "I count the switchbacks like beads." } },

        new Npc { Name = "Chiyo", TargetHeight = 1.31f, Speed = 3.2f, Lane = -1.44f,
                  MeetDistance = CpBaseGate + 215f, Frame = "DDDCDA", Accent = "8E2B24",
                  Where = "first hundred metres of cedar - the elder pilgrim, slowest of the cast",
                  Lines = new[] { "My legs are slow, my faith isn't.",
                                  "Fifty years I've greeted this sun.",
                                  "Go ahead, child. The gate will wait for me." } },

        new Npc { Name = "Yoshiko", TargetHeight = 1.38f, Speed = 3.6f, Lane = -1.62f,
                  MeetDistance = CpBaseGate + 300f, Frame = "8F7FA0", Accent = "8A6A3C",
                  Where = "base switchbacks - the retired guide, giving away the pacing advice",
                  Lines = new[] { "No hurry - the mountain waits.",
                                  "Save something for the last ramp.",
                                  "Everyone goes too hard in the trees.",
                                  "Thirteen kilometres. Ride the first ten gently." } },

        new Npc { Name = "Genji", TargetHeight = 1.44f, Speed = 4.4f, Lane = -1.50f,
                  MeetDistance = CpBaseGate + 430f, Frame = "D4705C", Accent = "4A6577",
                  Where = "base cedar - the veteran, foreshadowing the Cloudbreak reveal",
                  Lines = new[] { "Forty sunrises I've climbed this cone.",
                                  "Wait for the break - you'll understand.",
                                  "Nothing up there but light. That's enough." } },

        new Npc { Name = "Michiko", TargetHeight = 1.41f, Speed = 5.0f, Lane = -1.66f,
                  MeetDistance = CpBaseGate + 560f, Frame = "FF6A4F", Accent = "2A9AA8",
                  Where = "base cedar - the sunrise tourer, already chasing the light",
                  Lines = new[] { "We'll crest just as the sun does - push!",
                                  "Best light of the year, right now.",
                                  "Don't stop in the trees. Stop above them." } },

        new Npc { Name = "Shohei", TargetHeight = 1.43f, Speed = 4.6f, Lane = -1.54f,
                  MeetDistance = CpBaseGate + 700f, Frame = "E8A03C", Accent = "3E6B4A",
                  Where = "edge of the base cluster - bikepacker who slept at the gate",
                  Lines = new[] { "Slept at the base to catch this dawn.",
                                  "Coffee at the overlook - come find me.",
                                  "Panniers make the gradient honest." } },

        // ======================================================== CEDAR HAIRPINS (1.2 - 2.9 km)
        // The doc puts "tight hairpin switchbacks on the base" and an ash crew who sweep them.
        // Susumu and Ryosuke are placed ON the hairpins they talk about rather than lumped with
        // the rest of the crew at the strata wall - the crew works the whole mountain, and that
        // only reads if they are spread across it. They are the only hi-vis kits down here.
        new Npc { Name = "Susumu", TargetHeight = 1.45f, Speed = 3.9f, Lane = -1.70f,
                  MeetDistance = 1180f, Frame = "D8E040", Accent = "32363D",
                  Where = "lower hairpins - ash crew, broom on the rack",
                  Lines = new[] { "Cleared the hairpins for you - push on.",
                                  "Cinder's endless up here, but we keep at it.",
                                  "Sweep it Monday, it's black again Tuesday." } },

        new Npc { Name = "Ryosuke", TargetHeight = 1.46f, Speed = 4.0f, Lane = -1.48f,
                  MeetDistance = 1620f, Frame = "F07A22", Accent = "2E333A",
                  Where = "upper hairpins - maintenance rider heading down off shift",
                  Lines = new[] { "Swept the hairpins already - ride clean.",
                                  "Mind the ash drift past the strata.",
                                  "Tarmac's good to the hut. After that, take care." } },

        new Npc { Name = "Tatsuhiro", TargetHeight = 1.42f, Speed = 5.4f, Lane = -1.60f,
                  MeetDistance = 2900f, Frame = "1E2E6E", Accent = "E8C24A",
                  Where = "mid cedar - the racer-tourer, fastest authored rider on the mountain",
                  Lines = new[] { "Beat the sun above the deck?",
                                  "Tempo now - the reveal's worth it.",
                                  "Trees end at six kilometres. Ride to that." } },

        // 2.9 km -> 5.0 km is EMPTY, on purpose. Two kilometres of mist-hung cedar with nobody
        // on it is the region's solitude, and it is the contrast that makes the clusters read as
        // clusters rather than as the baseline.

        // ======================================================== THE TREELINE (5.0 - 5.3 km)
        // The doc calls the treeline "a story beat" - vegetation ENDS at Cloudbreak. Two riders
        // whose whole character is that boundary are placed just below it, watching it happen.
        new Npc { Name = "Hotaru", TargetHeight = 1.36f, Speed = 4.2f, Lane = -1.52f,
                  MeetDistance = 4980f, Frame = "4E8A46", Accent = "C9A23A",
                  Where = "last cedars below Cloudbreak - alpine botanist at the treeline",
                  Lines = new[] { "Last cedar's just ahead - then rock.",
                                  "Sorrel still grows above the clouds.",
                                  "The forest gives up at twelve hundred metres." } },

        new Npc { Name = "Kozue", TargetHeight = 1.33f, Speed = 4.3f, Lane = -1.64f,
                  MeetDistance = 5240f, Frame = "7CC13F", Accent = "EFE6CC",
                  Where = "final cedar belt - the botanist's student, sketching it",
                  Lines = new[] { "Sketching the last cedars before the rock.",
                                  "Nothing grows past the break - just sky.",
                                  "You can see the line where the green stops." } },

        // ======================================================== CLOUDBREAK OVERLOOK (5,980 m)
        // THE REVEAL. Four riders inside 300 m of the arc-0.46 viewpoint, because this is where
        // the region's premise actually happens and the people who would be here are exactly the
        // people who came for it. Kaede and Sae are 50 m apart - the tightest pairing in the
        // roster - because they are a photographer and their assistant working one shot.
        new Npc { Name = "Kaede", TargetHeight = 1.37f, Speed = 4.1f, Lane = -1.46f,
                  MeetDistance = CpCloudbreak - 100f, Frame = "8A6444", Accent = "E2A33C",
                  Where = "at the Cloudbreak overlook - dawn photographer, camera on the chest",
                  Lines = new[] { "Hold there - the cloud sea's on fire.",
                                  "One frame before the mist lifts!",
                                  "Twenty minutes of this light a year. This is it." } },

        new Npc { Name = "Sae", TargetHeight = 1.35f, Speed = 4.2f, Lane = -1.68f,
                  MeetDistance = CpCloudbreak - 50f, Frame = "6E7A88", Accent = "F2F4F6",
                  Where = "50 m from Kaede at the overlook - her assistant, holding the reflector",
                  Lines = new[] { "Wait for the light on the cone - now!",
                                  "The torii against the gold, every time.",
                                  "Don't ride through the frame, ride into it." } },

        new Npc { Name = "Momoka", TargetHeight = 1.34f, Speed = 4.8f, Lane = -1.56f,
                  MeetDistance = CpCloudbreak + 60f, Frame = "F49ABE", Accent = "F6F2F4",
                  Where = "just above the break - the cloud-chaser, the reason she climbed",
                  Lines = new[] { "The whole valley's underwater today!",
                                  "Break through and it turns to gold.",
                                  "I climb for this one minute." } },

        new Npc { Name = "Osamu", TargetHeight = 1.39f, Speed = 3.8f, Lane = -1.44f,
                  MeetDistance = CpCloudbreak + 200f, Frame = "EDE8D8", Accent = "2B5F97",
                  Where = "above the deck - the old meteorologist, reading it from the only " +
                          "place it can be read from",
                  Lines = new[] { "Cirrus like that means a clean summit.",
                                  "The deck sits low this morning - lucky us.",
                                  "Forty years of notebooks say today is a good one." } },

        // ======================================================== VOLCANIC RIDGE (7.2 km)
        // One rider on the long bare scoria grind. Not a cluster and not a gap: a single figure
        // on a black slope is the shot the art direction is built around, and it also stops the
        // 6.2 -> 8.0 km stretch from being genuinely empty.
        new Npc { Name = "Shinji", TargetHeight = 1.47f, Speed = 4.0f, Lane = -1.62f,
                  MeetDistance = 7150f, Frame = "2B2A30", Accent = "E07A2A",
                  Where = "open scoria slope - cinder worker, the darkest kit on the mountain",
                  Lines = new[] { "Loose scoria on the shoulder - hold your line.",
                                  "Whole slope's ash and ember-stone.",
                                  "Mask up if the wind turns." } },

        // ======================================================== BANDED STRATA WALL (8,060 m)
        // The rest of the ash crew, at the cutting whose ash they are complaining about. Two
        // riders 160 m apart: a working pair, not a queue.
        new Npc { Name = "Shiro", TargetHeight = 1.48f, Speed = 3.9f, Lane = -1.50f,
                  MeetDistance = CpStrata - 100f, Frame = "5A646E", Accent = "E8D23A",
                  Where = "below the strata cutting - ash-crew foreman with the barrow",
                  Lines = new[] { "Fresh grit patched past the hut.",
                                  "We keep this ribbon rideable - enjoy it.",
                                  "Four of us for thirteen kilometres of cinder." } },

        new Npc { Name = "Naoya", TargetHeight = 1.50f, Speed = 3.9f, Lane = -1.66f,
                  MeetDistance = CpStrata + 60f, Frame = "262C3C", Accent = "A8D83A",
                  Where = "at the strata wall - the grader, tallest silhouette in the crew",
                  Lines = new[] { "Ash gets thick past the strata wall.",
                                  "Ride the darker line - it's firmer.",
                                  "Goggles on. It blows straight across here." } },

        // 8.1 km -> 9.6 km is EMPTY. The doc's "long, honest, rhythmic climbing past the weather
        // station along the exposed flank" - the hardest stretch of the climb, and the one where
        // company would be most welcome, which is exactly why there is none.

        // ======================================================== WEATHER STATION HUT (9,750 m)
        // FIVE riders inside 320 m, the tightest cluster on the mountain after the base gate.
        // This is the region's ONLY building, and a building is the one place a population has a
        // reason to concentrate: two station staff, the hut keeper, the cook, and the keeper's
        // daughter with the lantern. Ordered so the player meets the instruments first and the
        // warmth last, which is how you actually arrive at a refuge.
        new Npc { Name = "Yusuke", TargetHeight = 1.43f, Speed = 4.5f, Lane = -1.54f,
                  MeetDistance = CpWeatherHut - 130f, Frame = "7D8E9C", Accent = "2FC2D6",
                  Where = "just below the station - meteorologist, instrument bag on the back",
                  Lines = new[] { "Clear above the deck this morning.",
                                  "Gust warning on the knife-edge - steady.",
                                  "Twelve metres a second at the ridge. Plan for it." } },

        new Npc { Name = "Katsuo", TargetHeight = 1.41f, Speed = 4.4f, Lane = -1.46f,
                  MeetDistance = CpWeatherHut - 40f, Frame = "1F8FA3", Accent = "D9A62E",
                  Where = "at the anemometer - the technician who services it",
                  Lines = new[] { "Wind's at twelve on the station gauge.",
                                  "We're above twelve hundred - feel it?",
                                  "That spinning cup is the only clock up here." } },

        new Npc { Name = "Noboru", TargetHeight = 1.40f, Speed = 3.7f, Lane = -1.60f,
                  MeetDistance = CpWeatherHut + 50f, Frame = "2C3A8C", Accent = "C9732E",
                  Where = "at the hut door - the ridge-hut keeper, thermos on the frame",
                  Lines = new[] { "Hot tea at the hut when you pass.",
                                  "Cold up here - keep the legs turning.",
                                  "Door's never locked. Nobody to lock it against." } },

        new Npc { Name = "Fumiko", TargetHeight = 1.32f, Speed = 3.5f, Lane = -1.70f,
                  MeetDistance = CpWeatherHut + 110f, Frame = "B45A2A", Accent = "EFE0C4",
                  Where = "just past the hut - the cook, ladle in the bag",
                  Lines = new[] { "Miso's hot when you reach the hut.",
                                  "Eat before the ramp - you'll need it.",
                                  "Nobody summits on an empty stomach." } },

        new Npc { Name = "Masao", TargetHeight = 1.30f, Speed = 3.8f, Lane = -1.52f,
                  MeetDistance = CpWeatherHut + 180f, Frame = "D99A2E", Accent = "F05E2A",
                  Where = "top of the hut cluster - the keeper's daughter, lantern on the bars",
                  Lines = new[] { "Lantern's lit at the hut - you're close.",
                                  "Warm up here before the cold summit.",
                                  "I can see your light from the window." } },

        // ======================================================== KNIFE-EDGE RIDGE (11,440 m)
        // ONE rider, and that is the placement. The doc's knife-edge is "narrow, wind-buffeted,
        // cloud falling away both sides" - a place a safety scout is stationed alone and a place
        // nobody else lingers. A cluster here would contradict the geometry as well as the mood.
        new Npc { Name = "Minoru", TargetHeight = 1.45f, Speed = 5.2f, Lane = -1.48f,
                  MeetDistance = CpKnifeEdge - 60f, Frame = "6F7A34", Accent = "D8342C",
                  Where = "on the knife-edge itself - the ridge scout, alone by definition",
                  Lines = new[] { "Knife-edge is clear - go careful anyway.",
                                  "Radio the hut if the cloud closes in.",
                                  "Wind comes across, not along. Lean into it." } },

        // ======================================================== SUMMIT TORII SHRINE (13,000 m)
        // The second of the doc's two named clusters: five riders in the last 800 m. Ordered so
        // the final ramp is a crescendo rather than a wall of people - a tourer finishing, a
        // pilgrim walking, a rider stopped in silence, then the two shrine staff at the torii
        // itself. Nobody is seated past 12,900 m: the last hundred metres belong to the player.
        new Npc { Name = "Tatsuo", TargetHeight = 1.44f, Speed = 5.3f, Lane = -1.58f,
                  MeetDistance = CpSummit - 800f, Frame = "F4F0EA", Accent = "F2624C",
                  Where = "foot of the summit ramp - the fast alpine tourer, still smooth",
                  Lines = new[] { "Air's thin but the road's yours.",
                                  "Keep it smooth to the summit ring.",
                                  "Ten percent left. That's all that's left." } },

        new Npc { Name = "Eiji", TargetHeight = 1.33f, Speed = 3.2f, Lane = -1.42f,
                  MeetDistance = CpSummit - 580f, Frame = "7A3C6E", Accent = "E0CC96",
                  Where = "on the steep ramp - the pilgrim walking her bike, slowest of the cast",
                  Lines = new[] { "Sometimes you walk the steep bit - no shame.",
                                  "The torii's close now. Breathe.",
                                  "Walking is still climbing." } },

        new Npc { Name = "Kazuo", TargetHeight = 1.38f, Speed = 4.0f, Lane = -1.64f,
                  MeetDistance = CpSummit - 440f, Frame = "8FC4DC", Accent = "F4F6F8",
                  Where = "upper ramp - the quiet dawn rider, the region's thesis in one person",
                  Lines = new[] { "Listen - there's nothing to hear. Beautiful.",
                                  "Just you, the cloud, and the light.",
                                  "No music. Not up here." } },

        new Npc { Name = "Koji", TargetHeight = 1.36f, Speed = 3.6f, Lane = -1.50f,
                  MeetDistance = CpSummit - 260f, Frame = "D93A22", Accent = "E0B24A",
                  Where = "at the torii - the bell-shrine attendant, brightest kit in the region",
                  Lines = new[] { "Hear my bell? Follow it to the top.",
                                  "Coins for the offering box up there.",
                                  "One ring on the way in, one on the way out." } },

        new Npc { Name = "Junnosuke", TargetHeight = 1.42f, Speed = 3.4f, Lane = -1.68f,
                  MeetDistance = CpSummit - 100f, Frame = "2A2F62", Accent = "DCC79A",
                  Where = "at the shrine, last rider on the mountain - the caretaker with the rope",
                  Lines = new[] { "Renewing the shimenawa rope today.",
                                  "Bow at the gate before you cross.",
                                  "You made it. Now stand still a moment." } },
    };

    // -----------------------------------------------------------------------------------------
    // MOVING CYCLISTS - the Italian hill-town layer (Copilot, 2026-09-26; COPILOT_HANDOFF Fuji
    // step 3). User direction (claude-fuji 08:33): Fuji's lower route is now a European hill
    // town 140-1000 m (piazza 560 m), vineyards + cypress 1030-3600 m, and a rifugio cafe at
    // 6150 m. The thirty above are the pilgrimage cast and ALL descend; these sixteen are local
    // club riders and visitors, in BOTH directions, so the town and the vineyards read as a
    // place people ride through, not a road the player meets people on.
    //
    //   town       140 - 1000 m   5 riders  (3 up, 2 down)
    //   vineyards 1030 - 3600 m   5 riders  (3 up, 2 down)
    //   climb     3600 - 6000 m   3 riders  (2 up, 1 down)
    //   rifugio   6030 - 6300 m   3 riders  (1 arriving, 2 leaving)
    //
    // LANES. The player rides at side * -1.7 (RouteFollower.laneOffset). Descenders keep the
    // existing convention (negative Lane + reverse = side * +1.5). Ascenders are at side * -0.85
    // to -1.0 - the player's half of the road, on the crown side of the player's line, so the
    // player overtakes them on the outside and never rides through them (TrafficAvoidance on
    // the root handles the rare close pass). All kits are KuroRiderBodies riders; colours live
    // in design_assets/3d/kuro/fuji_npc_liveries.py (fuji_riders.json).
    //
    // They are appended AFTER the thirty so the child order of "Fuji NPCs" - which
    // FujiRidge.Town BuildTownLife uses to pick the rifugio's frozen rider clones (children
    // 0..5) - is unchanged. Names checked against every other roster; clubs are fictional.
    // PROVISIONAL: distances, speeds, lanes, liveries and lines are all art tuning.
    static readonly Npc[] MovingCyclists =
    {
        // ======================================================== HILL TOWN (140 - 1000 m)
        new Npc { Name = "Lorenzo", TargetHeight = 1.44f, Speed = 4.6f, Lane = -0.95f, Ascending = true,
                  MeetDistance = 180f, Frame = "2A7FD4", Accent = "F2F4F7",
                  Where = "town gate, riding up - GS Borgo Alto, azzurro club kit",
                  Lines = new[] { "Buongiorno! The climb starts at the fountain.",
                                  "Espresso first, then the mountain.",
                                  "Ride with us as far as the vineyards?" } },

        new Npc { Name = "Giulia", TargetHeight = 1.36f, Speed = 4.2f, Lane = -0.85f, Ascending = true,
                  MeetDistance = 380f, Frame = "F28BB5", Accent = "1E1E24",
                  Where = "town street below the piazza, riding up - SC Campanile Rosa",
                  Lines = new[] { "Ciao! Mind the cobbles by the arcade.",
                                  "The bells ring at the hour - we race them.",
                                  "Best pastries are on the left, after the piazza." } },

        new Npc { Name = "Matteo", TargetHeight = 1.45f, Speed = 4.8f, Lane = -1.55f,
                  MeetDistance = 480f, Frame = "7A1F2E", Accent = "D9C7A0",
                  Where = "town street, riding down to the gate - US Vigneto Bordeaux",
                  Lines = new[] { "Coming down for coffee - see you at the piazza!",
                                  "Fresh tarmac to the vineyards. Enjoy it.",
                                  "Salve! Save your legs for the last ramps." } },

        new Npc { Name = "Chiara", TargetHeight = 1.35f, Speed = 3.9f, Lane = -1.00f, Ascending = true,
                  MeetDistance = 640f, Frame = "7FD1C9", Accent = "1F4E5F",
                  Where = "just past the piazza, riding up - Velo Club Fontana, celeste",
                  Lines = new[] { "Filled my bottle at the fountain - you should too.",
                                  "Easy pace to the vines, then we see.",
                                  "The campanile is our finish line on Sundays." } },

        new Npc { Name = "Pietro", TargetHeight = 1.43f, Speed = 4.4f, Lane = -1.62f,
                  MeetDistance = 900f, Frame = "2E7D4A", Accent = "E8C24A",
                  Where = "top of the town, riding down - Ciclistica Val d'Oro",
                  Lines = new[] { "Forza! The vineyards are just above.",
                                  "Wind's calm today. Perfect for the climb.",
                                  "I've done the rifugio already - strudel's good." } },

        // ======================================================== VINEYARDS (1030 - 3600 m)
        new Npc { Name = "Sofia", TargetHeight = 1.38f, Speed = 4.0f, Lane = -0.90f, Ascending = true,
                  MeetDistance = 1250f, Frame = "E0A82E", Accent = "7A1F2E",
                  Where = "first vineyard terraces, riding up - Ciclo Club Cipresso",
                  Lines = new[] { "Smell the grapes? Harvest is close.",
                                  "Count the cypress trees - it passes the time.",
                                  "Steady here. The road kicks up after the vines." } },

        new Npc { Name = "Tommaso", TargetHeight = 1.46f, Speed = 5.0f, Lane = -1.50f,
                  MeetDistance = 1650f, Frame = "2A2C31", Accent = "F07A22",
                  Where = "cypress avenue, riding down fast - Team Torchio",
                  Lines = new[] { "Descending for lunch - pranzo waits for no one!",
                                  "Watch the gravel at the vineyard gates.",
                                  "Up there it gets serious. Good luck!" } },

        new Npc { Name = "Anouk", TargetHeight = 1.37f, Speed = 4.4f, Lane = -0.95f, Ascending = true,
                  MeetDistance = 2100f, Frame = "F28C28", Accent = "1F2A44",
                  Where = "mid vineyards, riding up - Dutch visitor, Wielerclub De Molen",
                  Lines = new[] { "Hoi! No hills like this at home.",
                                  "My club would never believe this view.",
                                  "Flat roads are overrated, ja?" } },

        new Npc { Name = "Emile", TargetHeight = 1.44f, Speed = 4.6f, Lane = -1.60f,
                  MeetDistance = 2600f, Frame = "1F3F8F", Accent = "D62630",
                  Where = "upper vineyards, riding down - French tourer, Velo Club des Cretes",
                  Lines = new[] { "Bonjour! The rifugio is worth every metre.",
                                  "Allez, allez - it's only six kilometres!",
                                  "Take the photo at the top of the vines." } },

        new Npc { Name = "Valentina", TargetHeight = 1.36f, Speed = 4.1f, Lane = -0.85f, Ascending = true,
                  MeetDistance = 3150f, Frame = "6B3FA0", Accent = "E8C24A",
                  Where = "last vineyard rows, riding up - SC Uva Nera",
                  Lines = new[] { "Last vines ahead - then the forest.",
                                  "I ride here every morning before work.",
                                  "Keep a little in reserve for the rifugio." } },

        // ======================================================== THE CLIMB (3600 - 6000 m)
        new Npc { Name = "Luca", TargetHeight = 1.45f, Speed = 4.8f, Lane = -0.95f, Ascending = true,
                  MeetDistance = 3900f, Frame = "D62630", Accent = "F2F4F7",
                  Where = "lower climb above the vines, riding up - GS Passo Alto",
                  Lines = new[] { "Tempo, tempo - the rifugio has fresh strudel!",
                                  "The road gets steeper at the trees.",
                                  "Ride my wheel for a bit if you like." } },

        new Npc { Name = "Ingrid", TargetHeight = 1.40f, Speed = 4.3f, Lane = -1.58f,
                  MeetDistance = 4500f, Frame = "D8E8F2", Accent = "C8102E",
                  Where = "mid climb, riding down - Norwegian visitor, Sykkelklubb Nordlys",
                  Lines = new[] { "Hei! Warmer than home, and steeper too.",
                                  "The rifugio coffee is very good.",
                                  "Cold on the descent - zip up at the top." } },

        new Npc { Name = "Dario", TargetHeight = 1.44f, Speed = 4.5f, Lane = -0.90f, Ascending = true,
                  MeetDistance = 5300f, Frame = "17897E", Accent = "F2D81B",
                  Where = "upper climb, riding up - Squadra Dolomiti",
                  Lines = new[] { "Almost at the rifugio - one more bend.",
                                  "Breathe with the pedals, not against them.",
                                  "Above the rifugio it's rock and sky." } },

        // ======================================================== RIFUGIO CAFE (6150 m)
        new Npc { Name = "Bianca", TargetHeight = 1.35f, Speed = 3.8f, Lane = -0.95f, Ascending = true,
                  MeetDistance = 6030f, Frame = "F4F2EE", Accent = "2A9AA8",
                  Where = "arriving at the rifugio - Stella Alpina CC",
                  Lines = new[] { "Strudel and a cappuccino - I earned it.",
                                  "The terrace has the best view on the mountain.",
                                  "Stop for a bit. The summit isn't going anywhere." } },

        new Npc { Name = "Jonas", TargetHeight = 1.45f, Speed = 4.2f, Lane = -1.52f,
                  MeetDistance = 6200f, Frame = "5A646E", Accent = "D8342C",
                  Where = "leaving the rifugio downhill - Swiss visitor, RV Gipfelwind",
                  Lines = new[] { "Gruezi! Rifugio's open - go warm up.",
                                  "Descending slowly - my legs are finished.",
                                  "Wind picks up above here. Take care." } },

        new Npc { Name = "Margaux", TargetHeight = 1.36f, Speed = 4.0f, Lane = -1.66f,
                  MeetDistance = 6300f, Frame = "8FD6B0", Accent = "D4409C",
                  Where = "just below the rifugio, riding down - Club Cyclo Belvedere",
                  Lines = new[] { "Salut! The rifugio terrace is full of cyclists.",
                                  "Try the blueberry tart. Trust me.",
                                  "Bonne route - it's beautiful higher up." } },
    };

    /// <summary>Every Fuji rider this roster owns: the thirty, then the moving cyclists.</summary>
    static IEnumerable<Npc> AllRiders => Roster.Concat(MovingCyclists);

    /// <summary>Names of the moving-cyclist layer, for harnesses (FujiNpcPlaymodeCapture).</summary>
    public static string[] MovingCyclistNames => MovingCyclists.Select(n => n.Name).ToArray();

    /// <summary>
    /// Bike materials to repaint. The metal and rubber material names are deliberately absent:
    /// spokes, chain and tyres stay metal and rubber on anyone's bicycle.
    /// </summary>
    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    [MenuItem("MapleRide/NPCs/Stage Fuji Roster")]
    public static void AddAllToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Find-or-create, never create-blindly: a second "Fuji NPCs" root would hide the first
        // one's riders from Prune() and quietly double the whole cast.
        var root = GameObject.Find(ParentName);
        if (root == null) root = new GameObject(ParentName);

        // Only ever used as the FALLBACK line inside Stage(); the real line comes from the baked
        // route graph. Built once rather than per-rider so thirty riders do not resample the
        // same road thirty times.
        var route = MapleRideKuroSetup.ResampleRoute(MapleRideKuroSetup.GetRoadPoints(), RouteSpacing);
        StageAll(root.transform, route);
        EnsureTrafficAndLod(root);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[fuji] staged {Roster.Length + MovingCyclists.Length} riders on segment '{SegmentId}'.");
    }

    /// <summary>
    /// Stages ONLY the moving-cyclist layer (the sixteen town/vineyard/climb/rifugio riders),
    /// leaving the thirty pilgrims - and, crucially, their material assets - untouched.
    ///
    /// Why not just AddAllToScene: its PurgeOldMaterials deletes and re-creates every roster
    /// rider's <c>&lt;Name&gt;_*.mat</c> under a NEW GUID. The rifugio's frozen rider clones
    /// ("Fuji Rifugio Rider N", built by FujiRidge.Town BuildTownLife from children 0..5) are
    /// separate scene copies that reference the OLD materials, so a full re-stage leaves them
    /// with missing materials until FujiRidgeEnvironment.Apply is re-run. This entry point only
    /// purges and re-creates the new riders' own materials, so the clones stay intact.
    /// Idempotent (exact-name prune). Also bakes each new rider's greeting portrait.
    /// Menu: MapleRide/NPCs/Stage Fuji Moving Cyclists
    /// </summary>
    [MenuItem("MapleRide/NPCs/Stage Fuji Moving Cyclists")]
    public static void AddMovingCyclistsToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        KuroRiderBodies.Reload();
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find(ParentName);
        if (root == null) root = new GameObject(ParentName);

        var route = MapleRideKuroSetup.ResampleRoute(MapleRideKuroSetup.GetRoadPoints(), RouteSpacing);
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");
        PurgeOldMaterials(MovingCyclists);
        int staged = 0;
        foreach (var def in MovingCyclists)
        {
            if (!KuroRiderBodies.Has(def.Name))
                Debug.LogWarning($"[fuji] {def.Name}: not in any KuroRiders manifest - run " +
                                 "design_assets/3d/kuro/fuji_npc_liveries.py first.");
            var go = Stage(def, root.transform, route);
            if (go == null) continue;
            staged++;
            var cyc = go.GetComponent<NPCCyclist>();
            Debug.Log($"[fuji] moving {def.Name}: {def.MeetDistance:F0} m {(def.Ascending ? "UP" : "DOWN")}, " +
                      $"scale {RigScaleFor(def):F3}, {StagedSpeed(def.Speed):F2} m/s, lane {def.Lane:F2}, " +
                      $"progress {cyc.progress:F0}/{cyc.route.Length}, pos {go.transform.position:F1} - {def.Where}");
        }
        AssetDatabase.SaveAssets();
        EnsureTrafficAndLod(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        // Portraits for the greeting face card (BakeSingle writes one PNG each and saves).
        int baked = 0;
        foreach (var def in MovingCyclists)
            if (NpcPortraitBake.BakeSingle(def.Name, exposureEv: -1.5f)) baked++;

        int kids = root.transform.childCount;
        Debug.Log($"[fuji] staged {staged}/{MovingCyclists.Length} moving cyclists, {baked} portraits; " +
                  $"'{ParentName}' now has {kids} children (first: " +
                  $"{(kids > 0 ? root.transform.GetChild(0).name : "-")}).");
    }

    /// <summary>
    /// Idempotent runtime helpers on the Fuji roster: one TrafficAvoidance ON the root object
    /// itself (never a child - BuildTownLife clones children of "Fuji NPCs" as riders), with
    /// its rider list written explicitly, and a FujiRiderLod on every "Fuji NPC " rider.
    /// </summary>
    static void EnsureTrafficAndLod(GameObject root)
    {
        var riders = root.GetComponentsInChildren<NPCCyclist>(true)
                         .Where(c => c.gameObject.name.StartsWith(NamePrefix)).ToArray();
        var avoid = root.GetComponent<TrafficAvoidance>();
        if (avoid == null) avoid = root.AddComponent<TrafficAvoidance>();
        avoid.riders = riders;
        EditorUtility.SetDirty(avoid);
        int lods = 0;
        foreach (var c in riders)
        {
            if (c.GetComponent<FujiRiderLod>() == null) { c.gameObject.AddComponent<FujiRiderLod>(); lods++; }
        }
        Debug.Log($"[fuji] traffic avoidance over {riders.Length} riders; FujiRiderLod added to {lods} " +
                  $"(total {riders.Count(c => c.GetComponent<FujiRiderLod>() != null)}).");
    }

    /// <summary>Stages every roster rider. Idempotent - each is pruned by exact name first.</summary>
    public static void StageAll(Transform parent, Vector3[] route)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");

        PurgeOldMaterials(AllRiders);

        foreach (var def in AllRiders)
        {
            var go = Stage(def, parent, route);
            if (go != null)
                Debug.Log($"[fuji] {def.Name}: {def.MeetDistance:F0} m, scale {RigScaleFor(def):F3}, " +
                          $"{StagedSpeed(def.Speed):F2} m/s ({StagedSpeed(def.Speed) * 3.6f:F1} kph), " +
                          $"lane {def.Lane:F2}, pos {go.transform.position:F1} - {def.Where}");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Re-writes only the riding PACE onto the Fuji riders already staged in the scene.
    ///
    /// Exists because <see cref="NPCCyclist.speed"/> is serialized into the scene: changing
    /// <see cref="PaceScale"/> alone changes nothing the player sees, and re-running the full
    /// <see cref="AddAllToScene"/> to pick it up would re-import, re-rig and re-paint thirty
    /// riders to alter one float.
    ///
    /// Idempotent, and matched by EXACT name. A Contains-style match here would reach across
    /// regions - every roster lives in this one scene.
    /// </summary>
    [MenuItem("MapleRide/NPCs/Apply Fuji Pace To Scene")]
    public static void ApplyPaceToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int applied = 0, missing = 0;
        var all = UnityEngine.Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
        foreach (var def in AllRiders)
        {
            string objName = NamePrefix + def.Name;
            var cyc = all.FirstOrDefault(c => c.gameObject.name == objName);
            if (cyc == null) { missing++; continue; }

            float before = cyc.speed;
            cyc.speed = StagedSpeed(def.Speed);
            EditorUtility.SetDirty(cyc);
            applied++;
            Debug.Log($"[fuji] {def.Name}: pace {before:F2} -> {cyc.speed:F2} m/s " +
                      $"({cyc.speed * 3.6f:F1} kph)");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[fuji] pace applied to {applied} rider(s) at scale {PaceScale:F2}" +
                  (missing > 0 ? $"; {missing} roster rider(s) are not in this scene." : "."));
    }

    /// <summary>
    /// Deletes THIS roster's previously generated materials, and nothing else.
    ///
    /// Without it every run leaks a full set: SaveMaterial uses GenerateUniqueAssetPath, so a
    /// second pass writes Koji_Body 1.mat, a third Koji_Body 2.mat, and the folder grows without
    /// bound while the scene silently references whichever copy was newest.
    ///
    /// The ownership test is the leading <c>&lt;Name&gt;_</c> segment matched against THIS
    /// roster's names only. Every other region's riders share this folder, so a broader sweep
    /// here would delete their materials and leave a hundred riders rendering with missing
    /// textures - with nothing in the log to say why.
    /// </summary>
    static void PurgeOldMaterials(IEnumerable<Npc> riders)
    {
        var owned = new HashSet<string>(riders.Select(r => r.Name));
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var file = System.IO.Path.GetFileNameWithoutExtension(path);
            int underscore = file.IndexOf('_');
            if (underscore <= 0) continue;
            if (owned.Contains(file.Substring(0, underscore)))
                AssetDatabase.DeleteAsset(path);
        }
    }

    /// <summary>
    /// Scales the WHOLE rider - body and bicycle as one unit - from the single shared authority.
    /// <see cref="Npc.TargetHeight"/> is descriptive art direction only and is deliberately NOT
    /// consulted: every production NPC in the game is Kuro-matched at RiderScale, and a roster
    /// that derived its own scale would reintroduce exactly the per-region drift
    /// NpcCanonicalConformance exists to remove.
    /// </summary>
    static float RigScaleFor(Npc def) => NpcCanonicalConformance.RigScaleForAnyProductionNpc();

    static GameObject Stage(Npc def, Transform parent, Vector3[] route)
    {
        string objName = NamePrefix + def.Name;
        Prune(parent, objName);

        // Kuro-based body for every named rider in a KuroRiders manifest (see KuroRiderBodies).
        var rigAsset = KuroRiderBodies.Has(def.Name) ? KuroRiderBodies.Rig(def.Name) : LoadPrefab(RigPath);
        var bikeAsset = LoadPrefab(BikePath);
        if (rigAsset == null) { Debug.LogWarning($"[fuji] missing rig {RigPath}"); return null; }
        if (bikeAsset == null) { Debug.LogWarning($"[fuji] missing bike {BikePath}"); return null; }

        float rigScale = RigScaleFor(def);

        // NPCCyclist drives this root along the route and aims it down the tangent; the greeting
        // reads its forward axis for the view cone, so everything hangs off it.
        var npc = new GameObject(objName);
        npc.transform.SetParent(parent, false);

        var rider = new GameObject(def.Name + "Rider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * rigScale;

        // MUST be a child named exactly "Bike". KuroBikeRig.Setup looks for a local "Bike" first
        // and only then falls back to a GLOBAL GameObject.Find("Bike") - which, with well over a
        // hundred bicycles in this scene, is a lottery that can hand this rider the player's
        // drivetrain and leave the player's wheels driven by a pilgrim's speed.
        var bikeAnchor = new GameObject("Bike");
        bikeAnchor.transform.SetParent(rider.transform, false);
        bikeAnchor.transform.localScale = Vector3.one * BikeScale;
        var bikeModel = Instantiate(bikeAsset, bikeAnchor.transform);
        bikeModel.name = "BikeMesh";
        RepaintBike(def, bikeModel);

        var body = Instantiate(rigAsset, rider.transform);
        body.name = def.Name + "ArmatureAndMesh";
        ApplyKit(def, body);
        KuroRiderBodies.Finish(body, def.Name, MaterialDir);

        var rig = rider.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        // DriveDrivetrain divides WORLD distance travelled by this, so both scales have to be
        // folded in or the wheels spin at the wrong rate for the speed the rider is moving.
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        rig.previewCadenceRpm = 55f;
        NpcCanonicalConformance.Configure(rig);

        var cyclist = npc.AddComponent<NPCCyclist>();
        cyclist.speed = StagedSpeed(def.Speed);
        cyclist.laneOffset = def.Lane;
        cyclist.groundOffset = GroundOffset;
        // Reversed: oncoming traffic is what lets the greeting fire at all. On a point-to-point
        // climb this means the cast DESCENDS while the player ascends, which is also the only
        // traffic pattern that makes sense on a one-way pilgrimage road.
        cyclist.reverse = !def.Ascending;
        // The Fuji climb, NOT the pass. Set before RebuildFromGraph, which reads it.
        cyclist.segmentId = SegmentId;

        // Derive the line from the BAKED ROUTE GRAPH rather than from a private Catmull-Rom
        // resample. The graph frames carry the road's superelevation, so the lane offset is
        // measured across the banked surface instead of horizontally, and a re-baked road cannot
        // leave the cast riding the shape of an older mountain.
        if (!cyclist.RebuildFromGraph())
        {
            Debug.LogWarning($"[fuji] {def.Name}: no '{SegmentId}' segment in the route graph - " +
                             "falling back to the resampled Sakura road. Bake the Fuji route " +
                             "and re-run this menu item.");
            cyclist.route = def.Ascending ? route : route.Reverse().ToArray();
            cyclist.routeIncludesOffsets = false;
        }

        // Placed by DISTANCE, never by a fraction of the route. A fraction silently relocates
        // every rider the moment the climb changes length. Because reverse=true reversed the
        // point array, counting `back` points in from the END lands on arc distance
        // MeetDistance measured from the BASE GATE - which is what every comment above means.
        // An ascending rider's array runs from the base gate, so it is counted from the start.
        int back = Mathf.RoundToInt(def.MeetDistance / NPCCyclist.Spacing);
        int last = cyclist.route.Length - 1;
        cyclist.progress = Mathf.Clamp(def.Ascending ? back : last - back, 0, last);
        // Update() only runs in Play mode; without this the saved scene parks them at the world
        // origin, in a heap.
        cyclist.ApplyPose();
        EditorUtility.SetDirty(cyclist);

        var greeting = npc.AddComponent<NpcGreeting>();
        greeting.triggerDistance = 13f;
        greeting.viewAngle = 150f;
        greeting.visibleSeconds = 3.5f;
        greeting.rearmSeconds = 6f;
        greeting.bubbleOffset = new Vector3(0f, NpcCanonicalConformance.BubbleHeight, 0f);
        greeting.smileRendererName = "SmileDecal";
        // Per-instance lines. NpcGreeting.Phrases is STATIC, so without this all thirty riders
        // would draw from one shared pool and the whole mountain would speak in the PASS's
        // voice, which talks about blossom and racing rather than cloud, ash and the shrine.
        greeting.ownPhrases = def.Lines;

        // Greeting face card. riderName is what NpcPortraitBake keys the baked portrait on
        // (Assets/Resources/NpcPortraits/<Name>.png) and what NpcGreeting.ResolveIdentity uses to
        // find it at runtime, so a rider staged today picks up a portrait baked later WITHOUT a
        // re-stage. `portrait` is deliberately left null: it is assigned by
        // MapleRide/NPCs/Bake Greeting Portraits, which must run against the STAGED rider - a
        // portrait baked from the source GLB would show Coral's colours for all thirty, since
        // the livery only exists on the scene instance.
        greeting.riderName = def.Name;
        greeting.useFaceCard = true;
        EditorUtility.SetDirty(greeting);

        // NOTE: no KuroOutline here, and that is a decision rather than an omission - it skips
        // anything under "Bike", so on a rider it would only shell the SKINNED body, and a
        // skinned hull is driven entirely by its bones, so the 1+thickness localScale it relies
        // on is ignored and the hull renders exactly coincident with the body. Zero pixels, one
        // duplicated 109k-triangle mesh per rider.
        SetSmileNeutral(npc, def.Name);

        // Seat the rider once in the editor so the saved scene opens with a real pose rather
        // than a body standing through its own bicycle.
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);

        // MEASURES the posed rider with BakeMesh and writes real local bounds, instead of
        // leaving updateWhenOffscreen on and re-skinning a 109k-triangle body every frame
        // off-camera. With thirty more riders in a scene that already holds well over a hundred,
        // that is the difference between culling working and not existing.
        SakuraNpcRoster.FreezeSkinnedBounds(npc);
        return npc;
    }

    /// <summary>
    /// Swaps in this rider's recoloured kit atlas.
    ///
    /// The sculpt is shared by every rider in the GAME, not just in this roster, so its material
    /// is shared BY REFERENCE - retexturing it in place would repaint all thirty pilgrims and
    /// every other region's cast with them. Always clone.
    /// </summary>
    static void ApplyKit(Npc def, GameObject body)
    {
        // Kuro-based riders (KuroRiderBodies) wear their KuroKit atlas; anyone else keeps Coral's.
        var kitPath = KuroRiderBodies.Has(def.Name) ? KuroRiderBodies.KitPath(def.Name)
                                                    : $"{KitDir}/CoralKit_{def.Name}.png";
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(kitPath);
        if (kit == null)
        {
            Debug.LogWarning($"[fuji] {def.Name}: no kit texture at {kitPath} - run " +
                             "design_assets/3d/kuro/npc_palette_variants.py. " +
                             "She will wear Coral's colours.");
            return;
        }

        Material clone = null;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                // Body atlas only. The smile decal keeps its own material, or the mouth would be
                // painted with a full-body texture.
                if (!mats[i].name.StartsWith("Material_")) continue;

                // One clone per rider, shared across her renderers - see RepaintBike.
                if (clone == null)
                {
                    clone = new Material(mats[i]) { name = $"{def.Name}_Body" };
                    SetTexture(clone, kit);
                    SaveMaterial(clone);
                }
                mats[i] = clone;
                touched = true;
            }
            if (touched)
            {
                renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    /// <summary>
    /// Paints this rider's bike livery onto CLONES of the shared frame materials.
    ///
    /// The clone is not an optimisation, it is the whole correctness of the function: the GLB's
    /// materials are shared by reference with every other bicycle in the project, including the
    /// player's, so tinting them in place would repaint the entire game one colour.
    /// </summary>
    static void RepaintBike(Npc def, GameObject bike)
    {
        var frame = Hex(def.Frame);
        var accent = Hex(def.Accent);

        // ONE clone per source material per rider, cached by name. The frame is built from many
        // separate parts sharing a handful of materials, so cloning per renderer produced dozens
        // of copies per rider and thousands per staging run, each one a separate draw call's
        // worth of state because Unity cannot batch distinct material instances.
        var clones = new Dictionary<string, Material>();

        foreach (var renderer in bike.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string src = mats[i].name.Replace(" (Instance)", "");
                Color? tint = FrameMaterials.Contains(src) ? frame
                            : AccentMaterials.Contains(src) ? accent
                            : (Color?)null;
                if (tint == null) continue;

                if (!clones.TryGetValue(src, out var clone))
                {
                    clone = new Material(mats[i]) { name = $"{def.Name}_{src}" };
                    SetColor(clone, tint.Value);
                    SaveMaterial(clone);
                    clones[src] = clone;
                }
                mats[i] = clone;
                touched = true;
            }
            if (touched)
            {
                renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    /// <summary>
    /// The smile decal ships with the GLB and must start hidden; NpcGreeting turns it on only
    /// while the rider is speaking. Pushing it off here means the saved scene - and the editor
    /// preview of it - both show a neutral face.
    /// </summary>
    static void SetSmileNeutral(GameObject npc, string name)
    {
        var smile = FindChild(npc.transform, "SmileDecal");
        if (smile == null)
        {
            Debug.LogWarning($"[fuji] {name}: no 'SmileDecal' under the rig - she will greet " +
                             "but will not smile, and her portrait will bake neutral.");
            return;
        }
        var renderer = smile.GetComponent<Renderer>();
        if (renderer != null) { renderer.enabled = false; EditorUtility.SetDirty(renderer); }
    }

    // glTFast materials do not use the built-in _Color/_MainTex names, and which properties
    // exist depends on the import path, so set every variant that is actually present.
    static readonly string[] TextureProps = { "baseColorTexture", "_BaseMap", "_MainTex", "_BaseColorMap" };
    static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

    static void SetTexture(Material m, Texture2D tex)
    {
        bool any = false;
        foreach (var p in TextureProps)
            if (m.HasProperty(p)) { m.SetTexture(p, tex); any = true; }
        if (!any)
            Debug.LogWarning($"[fuji] '{m.shader.name}' exposes no known base-colour texture slot.");
        // The atlas carries the colour; a leftover tint would multiply over the new kit.
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, Color.white);
    }

    static void SetColor(Material m, Color c)
    {
        foreach (var p in ColorProps)
            if (m.HasProperty(p)) m.SetColor(p, p == "baseColorFactor" ? c.linear : c);
    }

    static void SaveMaterial(Material m)
    {
        var path = AssetDatabase.GenerateUniqueAssetPath($"{MaterialDir}/{m.name}.mat");
        AssetDatabase.CreateAsset(m, path);
    }

    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    static void Prune(Transform parent, string objName)
    {
        // EXACT name match, which is what makes re-running this menu item CONVERGE rather than
        // accumulate. Contains()-style matching once renamed a fill light into a second sun in
        // this project and leaked one extra per build - and here it would reach across to every
        // other region's riders.
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).name == objName)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject LoadPrefab(string path) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(path) ??
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().FirstOrDefault();

    static GameObject Instantiate(GameObject prefab, Transform parent)
    {
        var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (go == null) go = Object.Instantiate(prefab);
        if (PrefabUtility.IsPartOfPrefabInstance(go))
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }
}

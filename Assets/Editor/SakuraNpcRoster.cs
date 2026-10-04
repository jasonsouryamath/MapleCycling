using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the eleven-rider NPC cast for Sakura Pass (the ten-name roster plus Coral).
///
/// EVERY rider is Coral's sculpt (KuroNPC_Coral_Rigged.glb) wearing a recoloured kit, and that
/// is a deliberate reversal of how this roster was first built.
///
/// The first attempt gave each rider one of the five KuroNPC_&lt;name&gt;.glb variants. Those were
/// decimated to roughly 18% on export, and a chibi character does not survive that:
///
///     Coral   109,358 triangles   median edge 0.0070
///     Aoi      18,651 triangles   median edge 0.0190   (all four prims combined)
///
/// Their skinning was fine - every vertex weighted, weights summing to 1.0, no degenerate or
/// spiked triangles - so nothing ever showed up as "broken" in a log. The damage was purely in
/// the silhouette: at three times the edge length a rounded chibi limb becomes a handful of flat
/// facets, and the rider reads as crumpled foil rather than a person. No material or staging fix
/// can put those triangles back, so the variants are abandoned rather than patched.
///
/// Coral is the only full-resolution cyclist sculpt in the project, and she is already proven in
/// the scene: correct hand grip on the brake hoods, a baked facial expression, a smile decal and
/// a verified road-bike lean. So she is the template, and the numbers below are hers verbatim -
/// bike scale, lean angles, wheel radius, ground offset.
///
/// Riders are told apart by TEXTURE, not by material tinting. Her whole body - skin, hair,
/// helmet, jersey, shorts, gloves, shoes - is one 2048x2048 atlas on a single 'Material_0' slot,
/// so there is no per-part slot to tint, and a baseColorFactor tint would recolour each rider's
/// face along with her jersey. Instead assets/3d/kuro/npc_palette_variants.py recolours the
/// saturated kit pixels - leaving skin and outlines byte-for-byte identical - and writes
/// Assets/Kuro/NPC/Textures/CoralKit_*.png. Each rider gets a cloned material pointing at hers.
///
/// That script now recolours HAIR, KIT and TRIM as three independent regions. The first version
/// rotated the whole coral hue family as one block, so hair, helmet and jersey always landed on
/// the same hue and two riders 30 deg apart on the wheel (Nao and Takumi) rendered as literal
/// twins. Kit and hair also carry their own saturation and value scales, so riders differ in
/// LIGHTNESS as well as hue - Takumi's charcoal kit and ginger hair cannot be confused with
/// Nao's jade kit and blonde hair even in a glance at distance. The bike livery below is
/// matched to each rider's new kit.
///
/// Menu: MapleRide/NPCs/Stage Full Rider Roster
/// </summary>
public static class SakuraNpcRoster
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>The one surviving full-resolution cyclist sculpt - see the class remarks.</summary>
    const string RigPath = "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb";

    /// <summary>
    /// Coral's Colnago, for every rider. Her hand grip was baked against THIS frame's brake
    /// hoods at the bike scale below; another frame puts the hoods somewhere else and leaves ten
    /// riders clutching thin air. Livery variety comes from paint, not from frame shape.
    /// </summary>
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;

    const string MaterialDir = "Assets/Kuro/NPC/Materials";
    const string KitDir = "Assets/Kuro/NPC/Textures";

    // ---- Coral's verified numbers, reused unchanged (see CoralNpcSetup) --------------------

    /// <summary>Her chibi arm chain is only ~0.39 m; the bike must come down to 0.9 for the
    /// hands to reach the hoods at all.</summary>
    const float BikeScale = NpcCanonicalConformance.BikeScale;

    /// <summary>Measured seated height of her sculpt at scale 1, from a BAKED skinned mesh.
    /// Renderer.bounds on a SkinnedMeshRenderer is the import-time estimate and lies.</summary>
    const float CoralSeatedHeight = 1.185f;

    /// <summary>Route points describe the road's underlying centreline, but the asphalt carries
    /// a 6 cm parabolic crown; this clears it plus a small bias.</summary>
    const float GroundOffset = 0.065f;

    /// <summary>Must match the spacing used to resample the route.</summary>
    public const float RouteSpacing = 3.0f;

    /// <summary>
    /// PROVISIONAL: global multiplier applied to every roster rider's authored
    /// <see cref="Npc.Speed"/> before it is written onto the staged <see cref="NPCCyclist"/>.
    ///
    /// The authored 3.8 - 5.6 m/s numbers are honest road-cyclist speeds (14 - 20 kph), but they
    /// read as sped-up footage in this world: the riders are chibi (~1.4 m), so a metre of road
    /// is a much larger fraction of the frame than it would be at human scale, and their legs -
    /// whose cadence KuroBikeRig derives from distance travelled - spin to match. Scaling the
    /// whole roster keeps the authored per-rider VARIATION (Ren is still the quickest, Takumi
    /// still the slowest) while bringing the group down to a believable social pace.
    ///
    /// 0.55 -> 2.09 - 3.08 m/s, i.e. 7.5 - 11 kph. Tune here, then re-run
    /// <c>MapleRide/NPCs/Apply Roster Pace To Scene</c> - the staged riders carry a SERIALIZED
    /// speed, so changing this constant alone does nothing to the saved scene.
    /// </summary>
    public const float PaceScale = 0.55f;

    /// <summary>The pace a rider is actually staged at, m/s.</summary>
    public static float StagedSpeed(float authoredSpeed) => authoredSpeed * PaceScale;

    sealed class Npc
    {
        public string Name;
        public float TargetHeight;   // metres, seated, after the rig scale
        public float Speed;          // m/s
        public float Lane;           // LEGACY keep-left lane; superseded by TrafficLine.SharedLineM
        public float MeetDistance;   // metres up the pass from the player's start line
        /// <summary>
        /// True for a rider travelling the SAME way as the player, who therefore gets caught and
        /// overtaken rather than met head-on.
        ///
        /// The original roster was oncoming-only, and the reason is in NpcGreeting: its view cone
        /// is measured off the NPC's own forward axis, so a rider you are sitting BEHIND never
        /// has you in view and never greets. That is still true - but it only holds until you go
        /// PAST them, at which point you are in front of them, inside the cone, and they greet
        /// you. So a same-direction rider does not lose their greeting, it just moves to the
        /// other side of the pass, which is the more satisfying place for it anyway.
        /// </summary>
        public bool SameWay;
        public string Frame, Accent; // bike livery hexes
        /// <summary>True for a rider who wears the source atlas unaltered (Coral).</summary>
        public bool OwnAtlas;
        /// <summary>
        /// Optional path to this rider's OWN skinned GLB, instead of Coral's sculpt.
        ///
        /// Every rider above is Coral's body with a hue-rotated copy of her atlas, which is
        /// exactly why the roster reads as one person in eleven jerseys: the silhouette, the
        /// face, the hair mass and the helmet are identical in all of them. The quartet built
        /// by design_assets/3d/kuro/q4_build_quartet.py keeps Coral's dense sculpt, her 24-joint
        /// skin, her baked hand grip and her SmileDecal - so the bike fit and the greeting are
        /// unchanged - but CUTS AND RESHAPES the hair and repaints the atlas per texel in 3D,
        /// so the difference is geometry and printed kit rather than a hue offset.
        ///
        /// A rider with a Rig set carries her kit IN THE ATLAS, so <see cref="ApplyKit"/> must
        /// leave her alone; swapping in a CoralKit_*.png would paint Coral's jersey back on.
        /// </summary>
        public string Rig;
        public string[] Lines;
        /// <summary>Route-graph segment ("pass" when null). The moving layer also rides the
        /// Kawabe lakeshore return ("s1") so the lake village has cyclists.</summary>
        public string Segment;
        /// <summary>True = parked on the verge at <see cref="MeetDistance"/> (speed 0, never
        /// swerves). N1 stopped riders at the torii and the overlooks.</summary>
        public bool Stopped;
        /// <summary>Verge position of a stopped rider, metres along the road's side axis
        /// (+ valley side, - inland side), measured for the rider's own heading.</summary>
        public float StoppedLane;
        public string Where;
    }

    // Spread 90 m -> 1040 m so the player meets roughly one rider per 100 m of climbing instead
    // of all ten at once, with varied speeds so the encounters do not feel metronomic.
    //
    // Riders are now a MIX of oncoming and same-direction (see Npc.SameWay). The four slowest
    // travel the player's way so there is somebody to catch and overtake on the climb; the rest
    // are met head-on. Nobody keeps a lane: every rider is placed on the single shared line
    // (TrafficLine.SharedLineM) and only steps off it to get round somebody. The per-rider Lane
    // values below are kept for reference but are no longer what places anyone.
    //
    // Kit colours live in npc_palette_variants.py, keyed by these same names.
    static readonly Npc[] Roster =
    {
        new Npc { Name = "Aoi", TargetHeight = 1.42f, Speed = 4.8f, Lane = -1.80f, MeetDistance = 90f,
                  Frame = "2E6BE6", Accent = "FFC24D",
                  Lines = new[] { "Morning! The light is perfect up there.",
                                  "Good rhythm - hold that cadence.",
                                  "Watch the gravel on the next bend!",
                                  "Almost at the shrine, keep going." } },

        // Coral rides the same path as everyone else now. She used to be staged separately by
        // CoralNpcSetup, which left her with a redundant KuroOutline hull (218,796 triangles to
        // every other rider's 109,438, for no visible silhouette) and no ownPhrases, so she was
        // the only rider drawing from the shared static pool. Her numbers below are hers
        // verbatim - 1.384 m seated, 4.5 m/s, lane -1.8, 140 m up the pass - so she does not
        // move, and OwnAtlas keeps her original coral/teal colours.
        new Npc { Name = "Coral", TargetHeight = 1.384f, Speed = 4.5f, Lane = -1.80f, MeetDistance = 140f,
                  Frame = "1F8FA3", Accent = "FA645A", OwnAtlas = true,
                  Lines = new[] { "There you are! Lovely morning for it.",
                                  "Blossom's thick through the next cutting.",
                                  "Mind the camber on the hairpin, it bites.",
                                  "I'll wave again on the way back down!" } },

        new Npc { SameWay = true, Name = "Haruka", TargetHeight = 1.36f, Speed = 4.2f, Lane = -1.70f, MeetDistance = 190f,
                  Frame = "E0459B", Accent = "FFD1E8",
                  Lines = new[] { "The blossom is early this year!",
                                  "You're climbing well - nice and steady.",
                                  "Say hello to the lake for me.",
                                  "I take this pass every morning." } },

        // SHIORI - the first of the four concept-sheet riders, built from
        // Assets/Kuro/NPC/Concept/ImageGen/NPC_01_TealCoral_Feminine.png. She is not another
        // hue rotation of Coral: she carries her own GLB with a cut auburn bob, a reshaped
        // matte-black helmet and a teal/coral kit painted into her atlas in 3D. See Npc.Rig.
        new Npc { Name = "Shiori", TargetHeight = 1.39f, Speed = 5.1f, Lane = -1.78f, MeetDistance = 245f,
                  Frame = "12575C", Accent = "EE6A5E",
                  Rig = "Assets/Kuro/NPC/KuroNPC_Shiori_Rigged.glb",
                  Lines = new[] { "Freckles and sunburn, that's my spring!",
                                  "This cutting is my favourite bit of road.",
                                  "Teal and coral - you can't miss me, right?",
                                  "Save something for the hairpins." } },

        new Npc { Name = "Mika", TargetHeight = 1.44f, Speed = 5.3f, Lane = -1.90f, MeetDistance = 300f,
                  Frame = "F08A1E", Accent = "3FA9D6",
                  Lines = new[] { "Hey! Great day to be out here.",
                                  "Tunnel's just ahead - mind the dark.",
                                  "Push on, the view is worth it!",
                                  "Fresh legs? You'll need them." } },

        new Npc { SameWay = true, Name = "Nao", TargetHeight = 1.33f, Speed = 4.0f, Lane = -1.65f, MeetDistance = 400f,
                  Frame = "2FB37A", Accent = "F2E07A",
                  Lines = new[] { "Careful, the hairpins bite!",
                                  "I always stop at the overlook.",
                                  "Nice bike! Ride safe.",
                                  "Downhill from here soon." } },

        new Npc { Name = "Ren", TargetHeight = 1.40f, Speed = 5.6f, Lane = -1.85f, MeetDistance = 500f,
                  Frame = "7B4FD6", Accent = "FFC94D",
                  Lines = new[] { "Chasing a personal best today!",
                                  "Summit's close - don't ease up now.",
                                  "That was a clean line, nice one.",
                                  "See you on the descent!" } },

        new Npc { Name = "Daichi", TargetHeight = 1.46f, Speed = 4.4f, Lane = -1.75f, MeetDistance = 610f,
                  Frame = "8A9A3A", Accent = "5A6BD6",
                  Lines = new[] { "Over the top! It opens right up.",
                                  "Fuji's out today - you'll see.",
                                  "Long way down from here, enjoy it.",
                                  "Best road on the island, this." } },

        new Npc { SameWay = true, Name = "Sora", TargetHeight = 1.31f, Speed = 4.6f, Lane = -1.60f, MeetDistance = 720f,
                  Frame = "23A8C9", Accent = "FF7FB0",
                  Lines = new[] { "The lake looks incredible from here.",
                                  "Mind your speed on this stretch!",
                                  "Nearly at the shore road now.",
                                  "Tailwind all the way down, lucky you." } },

        new Npc { Name = "Emi", TargetHeight = 1.38f, Speed = 5.0f, Lane = -1.95f, MeetDistance = 820f,
                  Frame = "A64FD6", Accent = "5FD6A0",
                  Lines = new[] { "Beautiful out on the water today.",
                                  "You made it this far - respect!",
                                  "Keep left through the village.",
                                  "The mountain gets big fast now." } },

        new Npc { Name = "Yuki", TargetHeight = 1.48f, Speed = 4.9f, Lane = -1.80f, MeetDistance = 930f,
                  Frame = "9FC8E8", Accent = "E8F4FF",
                  Lines = new[] { "Snow's still on the peak up there.",
                                  "Nearly at the foot of it now!",
                                  "Cold wind off the lake - wrap up.",
                                  "Worth every metre of that climb." } },

        new Npc { SameWay = true, Name = "Takumi", TargetHeight = 1.34f, Speed = 3.8f, Lane = -1.70f, MeetDistance = 1040f,
                  Frame = "E2661F", Accent = "33383D",
                  Lines = new[] { "End of the road - and what a view.",
                                  "You rode the whole pass? Well done!",
                                  "I come here just to look at it.",
                                  "Fuji never gets old, does it?" } },
    };

    // -----------------------------------------------------------------------------------------
    // MOVING + STOPPED CYCLISTS (claude N1 "Sakura Pass NPCs", 2026-09-30), following the Fuji /
    // Taka MovingCyclists pattern. Eighteen riders in blossom-season club kits
    // (design_assets/3d/kuro/sakura_npc_liveries.py -> KuroKit_<Name>.png + sakura_riders.json):
    //   * three rider GROUPS riding in a bunch (same speed, ~5 m apart): a trio climbing past the
    //     shrine torii, a trio descending the hairpins, a pair climbing to Fuji Foot;
    //   * five singles, both directions, on the pass;
    //   * three STOPPED at the torii and the two overlooks (speed 0, parked on the inland verge);
    //   * three on the Kawabe lakeshore return (segment "s1"), the last at the lake village.
    // SameWay = true rides UP / with the player's travel; false is oncoming (greets at once).
    // Appended after the twelve, so their child order is unchanged. PROVISIONAL art tuning.
    static readonly Npc[] MovingCyclists =
    {
        // --- Kawabe Cycling Club: climbing trio, shrine torii (84.6 m)
        new Npc { SameWay = true, Name = "Haruto", TargetHeight = 1.42f, Speed = 4.4f, Lane = -1.7f, MeetDistance = 118f,
                  Frame = "E8738F", Accent = "F6EDEF", Where = "pass, past the shrine torii - Kawabe CC trio, lead",
                  Lines = new[] { "Kawabe Cycling Club, out for the blossom run!", "Draft behind us if you like.", "The torii is the best photo on the pass." } },
        new Npc { SameWay = true, Name = "Himari", TargetHeight = 1.36f, Speed = 4.4f, Lane = -1.7f, MeetDistance = 111f,
                  Frame = "F4B6C6", Accent = "C4456A", Where = "pass, Kawabe CC trio",
                  Lines = new[] { "Pink jerseys on purpose - spring is for it.", "Haruto never lets us stop at the shrine.", "Keep that cadence smooth, we're a bunch today." } },
        new Npc { SameWay = true, Name = "Kotone", TargetHeight = 1.34f, Speed = 4.4f, Lane = -1.7f, MeetDistance = 104f,
                  Frame = "C4456A", Accent = "F4B6C6", Where = "pass, Kawabe CC trio, tail",
                  Lines = new[] { "Wait for me! The petals slow me down.", "Left foot first through the lantern avenue.", "We ride every Sunday in April." } },
        // --- singles on the climb
        new Npc { Name = "Satoshi", TargetHeight = 1.45f, Speed = 5.0f, Lane = -1.8f, MeetDistance = 340f,
                  Frame = "1E3A6B", Accent = "E8C24A", Where = "pass, coming down through the cliff tunnel",
                  Lines = new[] { "Indigo and gold - my grandfather's club colours.", "Mind the echo in the tunnel.", "Fast descent, so hold your line." } },
        new Npc { Name = "Misaki", TargetHeight = 1.38f, Speed = 5.2f, Lane = -1.8f, MeetDistance = 575f,
                  Frame = "7AB87A", Accent = "2F6B3F", Where = "pass, just over the summit",
                  Lines = new[] { "Matcha green, matcha fuelled.", "You can see the whole lake from the crest.", "Brakes early on the hairpins!" } },
        // --- Shikisai Racing: descending trio, hairpins
        new Npc { Name = "Hikaru", TargetHeight = 1.46f, Speed = 5.6f, Lane = -1.8f, MeetDistance = 706f,
                  Frame = "F2F0E8", Accent = "D62B2B", Where = "pass, hairpins - Shikisai Racing trio, tail",
                  Lines = new[] { "Shikisai Racing, coming through!", "White and vermilion, like a shrine gate.", "Fast but friendly - wave back!" } },
        new Npc { Name = "Nanami", TargetHeight = 1.37f, Speed = 5.6f, Lane = -1.8f, MeetDistance = 700f,
                  Frame = "D62B2B", Accent = "F2F0E8", Where = "pass, hairpins, Shikisai trio",
                  Lines = new[] { "I'm on Hikaru's wheel, don't tell him.", "That corner is tighter than it looks.", "Race you to the lake!" } },
        new Npc { Name = "Makoto", TargetHeight = 1.44f, Speed = 5.6f, Lane = -1.8f, MeetDistance = 694f,
                  Frame = "2A2C31", Accent = "D62B2B", Where = "pass, hairpins, Shikisai trio, lead",
                  Lines = new[] { "Black kit hides the bug splatter.", "Third in line, and happy about it.", "Nice and smooth through the apex." } },
        new Npc { SameWay = true, Name = "Kaori", TargetHeight = 1.35f, Speed = 4.0f, Lane = -1.7f, MeetDistance = 880f,
                  Frame = "A68BD0", Accent = "F2D81B", Where = "pass, descent, riding the player's way",
                  Lines = new[] { "Wisteria purple, in season next month.", "Take the long view down to the lake.", "I always roll the last bend slowly." } },
        // --- Fuji Foot pair, climbing
        new Npc { SameWay = true, Name = "Tomoe", TargetHeight = 1.34f, Speed = 4.2f, Lane = -1.7f, MeetDistance = 1010f,
                  Frame = "F2A93B", Accent = "1E3A6B", Where = "pass, lower descent - persimmon pair, lead",
                  Lines = new[] { "Persimmon orange, for the autumn I miss.", "Fuji looks bigger every lap.", "We're doing the loop twice today." } },
        new Npc { SameWay = true, Name = "Takeshi", TargetHeight = 1.45f, Speed = 4.2f, Lane = -1.7f, MeetDistance = 1004f,
                  Frame = "3B8EA5", Accent = "F2A93B", Where = "pass, lower descent - persimmon pair, tail",
                  Lines = new[] { "Teal for the lake, orange for the sunset.", "Is that a headwind or my legs?", "Tomoe sets a hard pace." } },
        new Npc { Name = "Chihiro", TargetHeight = 1.38f, Speed = 4.8f, Lane = -1.8f, MeetDistance = 1250f,
                  Frame = "E8E0C8", Accent = "2F6B3F", Where = "pass, near Fuji Foot, climbing against the player",
                  Lines = new[] { "Cream and pine - I match the lakeside.", "Long climb ahead of me, short one for you.", "Fuji Foot is quiet this early." } },
        // --- stopped riders: torii, lakeside overlook, descent overlook (inland verge, speed 0)
        new Npc { SameWay = true, Stopped = true, StoppedLane = -4.7f, Name = "Yuzu", TargetHeight = 1.36f, Speed = 0f, Lane = -4.7f, MeetDistance = 114f,
                  Frame = "F2D84B", Accent = "1E3A6B", Where = "STOPPED just past the shrine torii, inland verge",
                  Lines = new[] { "Just catching my breath at the torii.", "Photo stop - the blossom frames the gate.", "I'll set off again once the petals settle." } },
        new Npc { Stopped = true, StoppedLane = -4.7f, Name = "Shigeru", TargetHeight = 1.46f, Speed = 0f, Lane = -4.7f, MeetDistance = 216f,
                  Frame = "5A646E", Accent = "E8C24A", Where = "STOPPED at the lakeside overlook, inland verge",
                  Lines = new[] { "Stopped to look at the lake. Join me?", "You can see the torii from here.", "There's no rush on this road." } },
        new Npc { SameWay = true, Stopped = true, StoppedLane = -4.7f, Name = "Sumire", TargetHeight = 1.35f, Speed = 0f, Lane = -4.7f, MeetDistance = 676f,
                  Frame = "7A4FA8", Accent = "F4B6C6", Where = "STOPPED at the descent overlook, inland verge",
                  Lines = new[] { "Violet, like the evening over Fuji.", "This is where I eat my rice ball.", "The overlook is the reason I climb." } },
        // --- Kawabe lakeshore return (segment s1)
        new Npc { SameWay = true, Segment = "s1", Name = "Mizuki", TargetHeight = 1.37f, Speed = 4.4f, Lane = -1.8f, MeetDistance = 420f,
                  Frame = "4FB3C9", Accent = "F4F2EE", Where = "s1 lakeshore, early, with the player",
                  Lines = new[] { "Lake blue, and the lake agrees.", "Flat and fast along the water.", "Watch for the herons on the shore." } },
        new Npc { Segment = "s1", Name = "Ritsu", TargetHeight = 1.45f, Speed = 5.0f, Lane = -1.8f, MeetDistance = 900f,
                  Frame = "C8452A", Accent = "F2D84B", Where = "s1 lakeshore, oncoming, near the causeway",
                  Lines = new[] { "Kiln red, like the village pots.", "Strong crosswind on the causeway today.", "The village bakery opens at nine." } },
        new Npc { SameWay = true, Segment = "s1", Name = "Azusa", TargetHeight = 1.39f, Speed = 4.0f, Lane = -1.8f, MeetDistance = 1120f,
                  Frame = "5E9E5E", Accent = "F4B6C6", Where = "s1 lakeshore, approaching the lake village",
                  Lines = new[] { "Fern green, and I'm heading for tea.", "The lake village has the best dumplings.", "Slow down for the villagers, they stroll." } },
    };

    const string NamePrefix = "Sakura NPC ";

    /// <summary>Every rider this roster owns: the twelve, then the N1 moving/stopped layer.</summary>
    static IEnumerable<Npc> AllRiders => Roster.Concat(MovingCyclists);

    /// <summary>Names of the N1 layer, for harnesses (SakuraNpcPlaymodeCapture) and the self-test.</summary>
    public static string[] MovingCyclistNames => MovingCyclists.Select(n => n.Name).ToArray();

    /// <summary>Names of the parked riders only (speed 0).</summary>
    public static string[] StoppedCyclistNames => MovingCyclists.Where(n => n.Stopped).Select(n => n.Name).ToArray();

    /// <summary>
    /// Bike materials to repaint. Brushed_Metal and Road_Tyre are deliberately absent: spokes,
    /// chain and tyres stay metal and rubber on anyone's bicycle.
    /// </summary>
    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    [MenuItem("MapleRide/NPCs/Stage Full Rider Roster")]
    public static void AddAllToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find("NPCs");
        if (root == null) root = new GameObject("NPCs");

        var route = MapleRideKuroSetup.ResampleRoute(MapleRideKuroSetup.GetRoadPoints(), RouteSpacing);
        StageAll(root.transform, route);

        // The swerve-to-pass director. Added to the roster root rather than to each rider: the
        // rule is pairwise, so it needs to see everybody at once, and one component that walks a
        // list is far cheaper (and far easier to reason about) than sixteen that each re-find
        // their neighbours. Idempotent - GetComponent-or-add, and it re-collects its riders on
        // enable so a re-staged roster is picked up without touching this.
        var avoid = root.GetComponent<TrafficAvoidance>();
        if (avoid == null) avoid = root.AddComponent<TrafficAvoidance>();
        avoid.riders = AvoidanceRiders(root.transform);
        EditorUtility.SetDirty(avoid);
        Debug.Log($"[roster] traffic avoidance wired to {avoid.riders.Length} rider(s) " +
                  $"(shared line {TrafficLine.SharedLineM:F2} m, meet shift {TrafficLine.MeetShiftM:F2} m, " +
                  $"pass shift {TrafficLine.PassShiftM:F2} m).");

        // Put the PLAYER on the same line here as well as in SakuraPassEnvironment.
        //
        // Not redundancy for its own sake: the environment pass is a twenty-minute rebuild of the
        // whole region, and the shared line is a traffic decision that will be re-tuned far more
        // often than the terrain will. Staging the roster is the cheap, frequently-run pass, so
        // it owns the value too - and because both write the SAME constant, they cannot drift.
        var player = GameObject.Find("Kuro on Sakura Pass");
        var follower = player != null ? player.GetComponent<RouteFollower>() : null;
        if (follower != null)
        {
            follower.laneOffset = TrafficLine.SharedLineM;
            follower.laneChangeEnabled = true;
            follower.passShiftM = TrafficLine.PassShiftM;
            follower.laneChangeSpeedMps = TrafficLine.ShiftSpeedMps;
            EditorUtility.SetDirty(follower);
            Debug.Log($"[roster] player moved onto the shared line ({follower.laneOffset:F2} m), " +
                      "lateral steer enabled.");
        }
        else Debug.LogWarning("[roster] no player RouteFollower found - shared line not applied " +
                              "to the player. Re-run the Sakura environment pass.");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[roster] staged {Roster.Length} riders on a {route.Length}-point route.");
    }

    /// <summary>Every NPCCyclist under the roster root EXCEPT the parked N1 riders: a rider with
    /// speed 0 sits on the verge and must neither swerve nor be swerved around.</summary>
    static NPCCyclist[] AvoidanceRiders(Transform root)
    {
        var parked = new HashSet<string>(MovingCyclists.Where(m => m.Stopped).Select(m => NamePrefix + m.Name));
        return root.GetComponentsInChildren<NPCCyclist>(true)
                   .Where(c => !parked.Contains(c.gameObject.name)).ToArray();
    }

    /// <summary>
    /// Stages ONLY the N1 moving/stopped layer, leaving the twelve riders and their material
    /// assets untouched (same reasoning as TakaNpcRoster.AddMovingCyclistsToScene). Idempotent
    /// (exact-name prune, own materials purged by name). Also bakes each new rider's portrait,
    /// wires TrafficAvoidance over the moving riders and adds a SakuraRiderLod to each new rider.
    /// Menu: MapleRide/NPCs/Stage Sakura Moving Cyclists
    /// </summary>
    [MenuItem("MapleRide/NPCs/Stage Sakura Moving Cyclists")]
    public static void AddMovingCyclistsToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        KuroRiderBodies.Reload();
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = GameObject.Find("NPCs");
        if (root == null) root = new GameObject("NPCs");

        var route = MapleRideKuroSetup.ResampleRoute(MapleRideKuroSetup.GetRoadPoints(), RouteSpacing);
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");
        PurgeOldMaterials(MovingCyclists);
        var celLitCache = new Dictionary<Material, Material>();
        int staged = 0;
        foreach (var def in MovingCyclists)
        {
            if (!KuroRiderBodies.Has(def.Name))
                Debug.LogWarning($"[sakura-npc] {def.Name}: not in any KuroRiders manifest - run " +
                                 "design_assets/3d/kuro/sakura_npc_liveries.py first.");
            var go = Stage(def, root.transform, route, celLitCache);
            if (go == null) continue;
            staged++;
            var cyc = go.GetComponent<NPCCyclist>();
            Debug.Log($"[sakura-npc] {(def.Stopped ? "STOPPED" : def.SameWay ? "UP" : "DOWN")} {def.Name} on '{cyc.segmentId}': " +
                      $"{def.MeetDistance:F0} m, {StagedSpeed(def.Speed):F2} m/s, lane {cyc.laneOffset:F2}, " +
                      $"progress {cyc.progress}/{cyc.route.Length}, pos {go.transform.position:F1} - {def.Where}");
        }
        AssetDatabase.SaveAssets();
        EnsureTrafficAndLod(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        int baked = 0;
        foreach (var def in MovingCyclists)
            if (NpcPortraitBake.BakeSingle(def.Name, exposureEv: -1.5f)) baked++;
        Debug.Log($"[sakura-npc] staged {staged}/{MovingCyclists.Length} moving+stopped cyclists " +
                  $"({MovingCyclists.Count(m => m.Stopped)} stopped), {baked} portraits; 'NPCs' now has " +
                  $"{root.transform.childCount} children.");
    }

    /// <summary>One TrafficAvoidance ON the "NPCs" root (parked riders excluded) and a
    /// SakuraRiderLod on every N1 rider. Idempotent.</summary>
    static void EnsureTrafficAndLod(GameObject root)
    {
        var avoid = root.GetComponent<TrafficAvoidance>();
        if (avoid == null) avoid = root.AddComponent<TrafficAvoidance>();
        avoid.riders = AvoidanceRiders(root.transform);
        EditorUtility.SetDirty(avoid);
        var mine = new HashSet<string>(MovingCyclists.Select(m => NamePrefix + m.Name));
        int lods = 0;
        foreach (var c in root.GetComponentsInChildren<NPCCyclist>(true))
            if (mine.Contains(c.gameObject.name) && c.GetComponent<SakuraRiderLod>() == null)
            { c.gameObject.AddComponent<SakuraRiderLod>(); lods++; }
        Debug.Log($"[sakura-npc] traffic avoidance over {avoid.riders.Length} riders; SakuraRiderLod added to {lods}.");
    }

    /// <summary>Stages every roster rider. Idempotent - each is pruned by exact name first.</summary>
    public static void StageAll(Transform parent, Vector3[] route)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");

        PurgeOldMaterials();

        // QA #2 fix: shared across the whole roster so any rider whose SmileDecal (or a def
        // that skips the kit clone via OwnAtlas/Rig) still points at the raw, un-cloned glTF
        // sub-asset converges on ONE persisted CelLit clone, not one per rider.
        var celLitCache = new Dictionary<Material, Material>();

        foreach (var def in Roster)
        {
            var go = Stage(def, parent, route, celLitCache);
            if (go != null)
                Debug.Log($"[roster] {def.Name}: {def.MeetDistance:F0} m, scale {RigScaleFor(def):F3}, " +
                          $"{StagedSpeed(def.Speed):F2} m/s ({StagedSpeed(def.Speed) * 3.6f:F1} kph), " +
                          $"{(def.SameWay ? "same-way" : "oncoming")}, pos {go.transform.position:F1}");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Re-writes only the riding PACE onto the riders already staged in the scene.
    ///
    /// Exists because <see cref="NPCCyclist.speed"/> is serialized into the scene: changing
    /// <see cref="PaceScale"/> alone changes nothing the player sees, and re-running the full
    /// <see cref="AddAllToScene"/> to pick it up would re-import, re-rig and re-paint ten riders
    /// to alter one float - a large, risky pass for a tuning change.
    ///
    /// Idempotent, and matched by EXACT name: a Contains-style match here would also grab
    /// Hanakage (who is not an NPCCyclist and has her own pacing) and any rider staged for
    /// another region.
    /// </summary>
    [MenuItem("MapleRide/NPCs/Apply Roster Pace To Scene")]
    public static void ApplyPaceToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int applied = 0, missing = 0;
        var all = UnityEngine.Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
        foreach (var def in AllRiders)
        {
            // EXACT name, and the exact name Stage() uses. A Contains-style match here would
            // also grab Hanakage and any rider staged for another region.
            string objName = NamePrefix + def.Name;
            var cyc = all.FirstOrDefault(c => c.gameObject.name == objName);
            if (cyc == null) { missing++; continue; }

            float before = cyc.speed;
            cyc.speed = StagedSpeed(def.Speed);
            EditorUtility.SetDirty(cyc);
            applied++;
            Debug.Log($"[roster] {def.Name}: pace {before:F2} -> {cyc.speed:F2} m/s " +
                      $"({cyc.speed * 3.6f:F1} kph)");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[roster] pace applied to {applied} rider(s) at scale {PaceScale:F2}" +
                  (missing > 0 ? $"; {missing} roster rider(s) are not in this scene." : "."));
    }

    /// <summary>
    /// Deletes this roster's previously generated materials.
    ///
    /// Without it every run leaks a full set: SaveMaterial uses GenerateUniqueAssetPath, so a
    /// second pass writes Aoi_Body 1.mat, a third Aoi_Body 2.mat, and the folder grows without
    /// bound while the scene silently references whichever copy was newest. Only names owned by
    /// a roster rider are removed - and Coral is now one of them, so her materials are rebuilt
    /// by this pass rather than left behind by the retired CoralNpcSetup staging path.
    /// </summary>
    static void PurgeOldMaterials() => PurgeOldMaterials(Roster);

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
    /// Scales the WHOLE rider - body and bicycle as one unit. Scaling only the bike would break
    /// the hood reach that <see cref="BikeScale"/> is tuned to and undo the baked hand grip.
    /// </summary>
    static float RigScaleFor(Npc def) => NpcCanonicalConformance.RigScaleForAnyProductionNpc();

    static GameObject Stage(Npc def, Transform parent, Vector3[] route, Dictionary<Material, Material> celLitCache)
    {
        string objName = NamePrefix + def.Name;
        Prune(parent, objName);

        // Kuro-based body for every named rider in a KuroRiders manifest (Coral's sculpt tears
        // when posed - see KuroRiderBodies); bespoke-rig heroes keep their own model.
        var rigAsset = def.Rig == null && KuroRiderBodies.Has(def.Name)
            ? KuroRiderBodies.Rig(def.Name) : LoadPrefab(def.Rig ?? RigPath);
        var bikeAsset = LoadPrefab(BikePath);
        if (rigAsset == null) { Debug.LogWarning($"[roster] missing rig {def.Rig ?? RigPath}"); return null; }
        if (bikeAsset == null) { Debug.LogWarning($"[roster] missing bike {BikePath}"); return null; }

        float rigScale = RigScaleFor(def);

        // NPCCyclist drives this root along the route and aims it down the tangent; the greeting
        // reads its forward axis for the view cone, so everything hangs off it.
        var npc = new GameObject(objName);
        npc.transform.SetParent(parent, false);

        var rider = new GameObject(def.Name + "Rider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * rigScale;

        // MUST be a child named exactly "Bike". KuroBikeRig.Setup looks for a local "Bike" first
        // and only falls back to a GLOBAL GameObject.Find("Bike") - which, with eleven bicycles
        // in the scene, is a lottery that can hand this rider the player's drivetrain.
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

        // QA #2 fix: ApplyKit only ever recolours the body atlas (and does nothing at all for a
        // def marked OwnAtlas/Rig, whose own-atlas body was never cloned) - no roster ever
        // converted the raw glTF-pbrMetallicRoughness shader itself, and none ever touched
        // SmileDecal beyond disabling its renderer. metallicFactor=1/roughnessFactor=1 mirrors
        // the sky: flat/blown-out on the broad face, banded on the curved helmet - same shader
        // bug, two different apparent "looks" on one rider. Converts whatever ApplyKit already
        // cloned in place, and redirects any still-shared material (SmileDecal, or an
        // OwnAtlas/Rig body) to a persisted, region-owned CelLit clone.
        NpcCelLitConversion.ConvertRiderBody(body, MaterialDir, sharedCache: celLitCache);
        NpcCelLitConversion.AssertNoGltfMaterials(body, $"sakura-roster {objName} body");

        var rig = rider.AddComponent<CoralBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        // DriveDrivetrain divides WORLD distance travelled by this, so BOTH scales have to be
        // folded in or the wheels spin at the wrong rate for the speed the rider is moving.
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        rig.previewCadenceRpm = 55f;
        NpcCanonicalConformance.Configure(rig);

        var cyclist = npc.AddComponent<NPCCyclist>();
        // Authored speed scaled to the group's riding pace; see PaceScale for why.
        cyclist.speed = StagedSpeed(def.Speed);
        cyclist.groundOffset = GroundOffset;
        cyclist.reverse = !def.SameWay;

        // THE SINGLE SHARED LINE.
        //
        // NPCCyclist bakes the lane as side * (dir * laneOffset), where dir is -1 for a reversed
        // rider. That form is what used to give two lanes for free - and it is also why simply
        // writing the shared line into laneOffset would put oncoming riders on the WRONG side of
        // it. Pre-multiplying by dir cancels the term exactly, so every rider - whichever way
        // they are going - lands on the same road-side offset.
        //
        // The jitter is derived from the rider's name rather than Random, so re-running staging
        // converges instead of shuffling the whole roster a few centimetres every time.
        float jitter = ((def.Name.GetHashCode() & 0xFFFF) / 65535f - 0.5f) * 2f
                       * TrafficLine.LineJitterM;
        cyclist.laneOffset = (TrafficLine.SharedLineM + jitter) * (cyclist.reverse ? -1f : 1f);
        // N1: a STOPPED rider is parked on the verge instead of the shared line (same sign
        // convention as above, so a reversed rider still lands on the requested side).
        if (def.Stopped)
        {
            cyclist.speed = 0f;
            cyclist.laneOffset = def.StoppedLane * (cyclist.reverse ? -1f : 1f);
        }
        cyclist.segmentId = def.Segment ?? "pass";

        // Derive the line from the BAKED ROUTE GRAPH rather than from a private Catmull-Rom
        // resample. Two defects came out of the old path and both are fixed here:
        //
        //   * The resampled array was serialized into the scene and never revisited, so when the
        //     pass grew the whole roster carried on riding the old shape - up to 236 m out, which
        //     left riders buried in the current asphalt or stranded below it, still greeting the
        //     player from inside the hill because NpcGreeting only tests distance and a cone.
        //   * ResampleRoute's spacing is per-control-point-span, NOT a uniform 3 m, so the
        //     "MeetDistance / RouteSpacing" seating below was never quite the distance it claimed.
        //     Sampling the graph by arc length makes the spacing genuinely uniform.
        //
        // The graph frames also carry the road's superelevation, so the lane offset is measured
        // across the banked surface instead of horizontally (up to 0.24 m of error on the bends).
        if (!cyclist.RebuildFromGraph())
        {
            Debug.LogWarning("[roster] no route graph - falling back to the resampled route.");
            cyclist.route = route.Reverse().ToArray();
            cyclist.routeIncludesOffsets = false;
        }

        // Placed by DISTANCE, never by a fraction of the route. A fraction silently relocates
        // every rider the moment the pass changes length - which is exactly how Coral ended up
        // 277 m away when the route was extended from 530 m to 1142 m for the Fuji approach.
        // A reversed rider's points were flipped at build, so counting BACK from the end is what
        // puts them MeetDistance along the pass. A same-direction rider's points were not, so the
        // same distance is counted forward from the start. Using one form for both is how a
        // same-direction rider ends up starting at the summit riding away from the player.
        int steps = Mathf.RoundToInt(def.MeetDistance / NPCCyclist.Spacing);
        cyclist.progress = cyclist.reverse
            ? Mathf.Clamp(cyclist.route.Length - 1 - steps, 0, cyclist.route.Length - 1)
            : Mathf.Clamp(steps, 0, cyclist.route.Length - 1);
        // Update() only runs in Play mode; without this the saved scene parks them at the world
        // origin, which here is out over the lake.
        cyclist.ApplyPose();
        EditorUtility.SetDirty(cyclist);

        var greeting = npc.AddComponent<NpcGreeting>();
        greeting.triggerDistance = 13f;
        greeting.viewAngle = 150f;
        greeting.visibleSeconds = 3.5f;
        greeting.rearmSeconds = 6f;
        greeting.bubbleOffset = new Vector3(0f, NpcCanonicalConformance.BubbleHeight, 0f);
        greeting.smileRendererName = "SmileDecal";
        // Per-instance lines. NpcGreeting.Phrases is STATIC, so without this all ten riders
        // would draw from one shared pool and speak in a single voice.
        greeting.ownPhrases = def.Lines;

        // --- face card ----------------------------------------------------------------------
        // Explicit, rather than relying on NpcGreeting.ResolveIdentity() to derive the name from
        // the object name at Awake. Sakura's cards DO work today through that fallback, which is
        // precisely the problem: nothing in the saved scene records who these riders are, so the
        // identity only exists in play mode and nothing offline (a self-test, a portrait bake, a
        // future roster tool) can see it. Writing it down costs two lines and makes the roster
        // inspectable.
        greeting.useFaceCard = true;
        greeting.riderName = def.Name;
        greeting.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(
            $"Assets/Resources/{NpcGreeting.PortraitResourceDir}{def.Name}.png");
        if (greeting.portrait == null)
            Debug.LogWarning($"[roster] {def.Name}: no baked portrait - card falls back to the " +
                             "silhouette.");

        EditorUtility.SetDirty(greeting);

        // NOTE: no KuroOutline here, and that is a decision rather than an omission.
        // It skips anything under "Bike" by default, so on a rider it would only ever shell the
        // SKINNED body - and a skinned hull is driven entirely by its bones, so the 1+thickness
        // localScale it relies on is ignored and the hull renders exactly coincident with the
        // body. It produces no visible silhouette while duplicating a 109k-triangle mesh AND
        // setting updateWhenOffscreen = true on the copy, which defeats culling and re-skins it
        // every frame even when the rider is nowhere near the camera. Ten riders, zero pixels.
        SetSmileNeutral(npc, def.Name);

        // Seat the rider once in the editor so the saved scene opens with a real pose rather
        // than a body standing through its own bicycle.
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        FreezeSkinnedBounds(npc);
        return npc;
    }

    /// <summary>
    /// Turns OFF <c>updateWhenOffscreen</c> on every skinned renderer under <paramref name="root"/>
    /// and gives it explicit local bounds instead.
    ///
    /// The flag arrived as a blunt fix for the fact that an imported
    /// <see cref="SkinnedMeshRenderer"/>'s bounds are the import-time estimate and cannot be
    /// trusted, but it costs far more than it fixes: it defeats frustum culling outright and
    /// re-skins a 109k-triangle body (and its decal) every frame for all eleven riders, whether
    /// or not the camera can see them. The honest fix is to MEASURE the posed rider - BakeMesh,
    /// never Renderer.bounds - and write bounds that are actually correct.
    ///
    /// Which transform <c>localBounds</c> is relative to differs with whether a root bone is
    /// assigned, so this does not guess: it writes a candidate, reads back the resulting WORLD
    /// bounds and, if they do not contain the measured mesh, falls back to the other space.
    /// A rider that still fails both is left with the flag on rather than allowed to pop out of
    /// view - correctness first, performance second.
    /// </summary>
    public static void FreezeSkinnedBounds(GameObject root)
    {
        NpcCanonicalConformance.FinalizeStagedPose(
            root.GetComponentInChildren<CoralBikeRig>(true));
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;

            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            Object.DestroyImmediate(baked);
            if (verts.Length == 0) continue;

            // BakeMesh yields vertices in the renderer transform's own space.
            var world = new Bounds(smr.transform.TransformPoint(verts[0]), Vector3.zero);
            foreach (var v in verts) world.Encapsulate(smr.transform.TransformPoint(v));

            // The rider is posed by an IK solver that moves the limbs well outside the seated
            // silhouette, so pad generously. A slightly loose box only costs a few frames of
            // culling; a tight one pops the rider out of view mid-corner.
            world.Expand(world.size.magnitude * 0.5f);

            bool ok = false;
            foreach (var space in new[] { smr.rootBone, smr.transform })
            {
                if (space == null) continue;
                smr.updateWhenOffscreen = false;
                smr.localBounds = ToLocal(world, space);
                if (Contains(smr.bounds, world)) { ok = true; break; }
            }
            if (!ok)
            {
                smr.updateWhenOffscreen = true;
                Debug.LogWarning($"[roster] {smr.name}: could not write reliable local bounds - " +
                                 "leaving updateWhenOffscreen on.");
            }
            EditorUtility.SetDirty(smr);
        }
    }

    static Bounds ToLocal(Bounds world, Transform space)
    {
        var c = world.center;
        var e = world.extents;
        var b = new Bounds(space.InverseTransformPoint(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
            b.Encapsulate(space.InverseTransformPoint(c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
        return b;
    }

    static bool Contains(Bounds outer, Bounds inner) =>
        outer.Contains(inner.min) && outer.Contains(inner.max);

    /// <summary>
    /// Swaps in this rider's recoloured kit atlas.
    ///
    /// The sculpt is shared by all ten riders, so its material is shared BY REFERENCE -
    /// retexturing it in place would repaint every rider and Coral herself. Always clone.
    /// </summary>
    static void ApplyKit(Npc def, GameObject body)
    {
        if (def.OwnAtlas) return;   // wears the source atlas unaltered - nothing to swap.
        if (def.Rig != null) return;  // her kit is painted into her own atlas - see Npc.Rig.

        // Kuro-based riders (KuroRiderBodies) wear their KuroKit atlas; anyone else keeps Coral's.
        var kitPath = KuroRiderBodies.Has(def.Name) ? KuroRiderBodies.KitPath(def.Name)
                                                    : $"{KitDir}/CoralKit_{def.Name}.png";
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(kitPath);
        if (kit == null)
        {
            Debug.LogWarning($"[roster] {def.Name}: no kit texture at {kitPath} - run " +
                             "assets/3d/kuro/npc_palette_variants.py. She will wear Coral's colours.");
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
                // Body atlas only. The smile decal keeps its own material, or her mouth would
                // be painted with a full-body texture.
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

    static void RepaintBike(Npc def, GameObject bike)
    {
        var frame = Hex(def.Frame);
        var accent = Hex(def.Accent);

        // ONE clone per source material per rider, cached by name.
        //
        // The Colnago is built from ~78 separate parts that all share just two materials, so
        // cloning per renderer produced 23 copies of Carbon_Black and 11 of Racing_Red for
        // every rider - 340 redundant material assets per staging run, each a separate draw
        // call's worth of state because Unity cannot batch distinct material instances.
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
            Debug.LogWarning($"[roster] {name}: no 'SmileDecal' under the rig - she will greet " +
                             "but will not smile.");
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
            Debug.LogWarning($"[roster] '{m.shader.name}' exposes no known base-colour texture slot.");
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
        // EXACT name match. Contains()-style matching once renamed a fill light into a second
        // sun in this project and leaked one extra per build.
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

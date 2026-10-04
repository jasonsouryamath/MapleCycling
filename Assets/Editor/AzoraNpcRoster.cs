using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the thirty-rider NPC cast for AZORA HIGHLANDS - the 24.06 km point-to-point ascent
/// described in <c>improve/region_designs/azora_highlands.md</c> section 5.
///
/// This is the sibling of <see cref="SakuraNpcRoster"/> and shares every verified construction
/// number with it, deliberately and verbatim. What differs is the REGION, and only the region:
/// the segment the riders are bound to, the scene parent they hang from, the object-name prefix
/// used to prune and re-find them, the distances they are seated at, and the voice they speak in.
/// Everything else - rig, bike, scales, lean angles, wheel radius, ground offset - is copied
/// without change, because those numbers were tuned against Coral's sculpt and her Colnago and
/// are a property of the RIG, not of the road.
///
/// WHY EVERY RIDER IS CORAL AGAIN. Coral's sculpt (KuroNPC_Coral_Rigged.glb) remains the only
/// full-resolution cyclist in the project: 109,358 triangles at a 0.007 median edge, against
/// 18,651 triangles and a 0.019 edge for the decimated KuroNPC_&lt;name&gt;.glb variants. A chibi
/// character does not survive that decimation - the rounded limbs collapse into flat facets and
/// the rider reads as crumpled foil rather than as a person - so the variants stay abandoned and
/// the highland cast is built the same way the pass and city casts are.
///
/// Riders are told apart by TEXTURE, never by material tinting. Her whole body - skin, hair,
/// helmet, jersey, shorts, gloves, shoes - is one 2048x2048 atlas on a single 'Material_0' slot,
/// so there is no per-part slot to tint, and a baseColorFactor tint would recolour each rider's
/// FACE along with her jersey. Instead assets/3d/kuro/npc_palette_variants.py recolours the
/// saturated kit pixels - leaving skin and outlines byte-for-byte identical - and writes
/// Assets/Kuro/NPC/Textures/CoralKit_*.png, keyed by the same names used below. Each rider gets
/// a cloned material pointing at hers.
///
/// WHY THIRTY AND NOT ELEVEN. Azora is a 24 km road, four times the city loop: thirty riders
/// bustling street population, so the density here is roughly one rider every 165 m against the
/// pass's one per 100 m over a much shorter stretch. Two consequences are baked into the table
/// below rather than left to chance - neighbouring riders in the meet order are separated in
/// LIGHTNESS as well as hue (two riders share the frame far more often than on the pass), and
/// the per-rider speeds are authored across a wider band so the group does not move as a block.
///
/// NAME COLLISIONS ARE A REAL HAZARD HERE. Materials (<c>&lt;Name&gt;_Body</c>) and scene objects
/// are keyed by rider name, and both regions live in the SAME scene file. Every name below was
/// checked against the Sakura roster (Aoi, Coral, Haruka, Mika, Nao, Ren, Daichi, Sora, Emi,
/// Yuki, Takumi) and against Hanakage; a collision would have one region's staging pass silently
/// repaint the other's rider.
///
/// Menu: MapleRide/NPCs/Stage Azora Roster
/// </summary>
public static class AzoraNpcRoster
{
    /// <summary>All regions share one scene file; they are separated by ROUTE SEGMENT, not by
    /// scene. See <see cref="SegmentId"/>.</summary>
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>The one surviving full-resolution cyclist sculpt - see the class remarks.</summary>
    const string RigPath = "Assets/Kuro/NPC/KuroNPC_Coral_Rigged.glb";

    /// <summary>
    /// Coral's Colnago, for every rider. Her hand grip was baked against THIS frame's brake
    /// hoods at the bike scale below; another frame puts the hoods somewhere else and leaves
    /// thirty riders clutching thin air. Livery variety comes from paint, not from frame shape.
    /// </summary>
    const string BikePath = NpcCanonicalConformance.BikeAssetPath;

    const string MaterialDir = "Assets/Kuro/NPC/Materials";
    const string KitDir = "Assets/Kuro/NPC/Textures";

    /// <summary>
    /// The Azora ascent in the baked RouteGraph. Sakura's roster uses "pass"; getting this
    /// wrong does not error - it silently stages thirty highland riders on the mountain pass, on top
    /// of the eleven already there.
    /// </summary>
    const string SegmentId = "azora";

    /// <summary>Scene parent for this region's riders, kept separate from the pass's "NPCs" so a
    /// region can be toggled, moved or deleted as a unit.</summary>
    const string ParentName = "Azora NPCs";

    /// <summary>Object-name prefix. Pruning, pace re-application and any future tooling all match
    /// on the EXACT string <c>NamePrefix + rider name</c>, which is what makes re-running this
    /// menu item converge instead of accumulating duplicates.</summary>
    const string NamePrefix = "Azora NPC ";

    // ---- Coral's verified numbers, reused unchanged (see CoralNpcSetup / SakuraNpcRoster) ----

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
    /// The authored 3.5 - 5.7 m/s numbers are honest cyclist speeds (13 - 21 kph), but they read
    /// as sped-up footage in this world: the riders are chibi (~1.4 m), so a metre of road is a
    /// much larger fraction of the frame than it would be at human scale, and their legs - whose
    /// cadence KuroBikeRig derives from distance travelled - spin to match.
    ///
    /// Azora takes 0.55, the value verified on the SAKURA pass roster, rather than Maple City's
    /// 0.48. The city needed the lower number because its loop is flanked by facades, tram poles
    /// and street furniture at 6 m, so near-field parallax made any given ground speed read
    /// faster. Azora is the opposite case: an open fell with the nearest object often a
    /// dry-stone wall 8 m off and then nothing for a kilometre, so there is almost no near-field
    /// reference and the same speed reads SLOWER. Dropping to 0.48 here would have the descent
    /// traffic look like it was freewheeling in treacle. 0.55 gives 1.93 - 3.14 m/s, i.e.
    /// 6.9 - 11.3 kph apparent - riders coming down a pass without racing it.
    ///
    /// PROVISIONAL: tune here, then re-run <c>MapleRide/NPCs/Apply Azora Pace To Scene</c> - the
    /// staged riders carry a SERIALIZED speed, so changing this constant alone does nothing to
    /// the saved scene.
    /// </summary>
    public const float PaceScale = 0.55f;

    /// <summary>The pace a rider is actually staged at, m/s.</summary>
    public static float StagedSpeed(float authoredSpeed) => authoredSpeed * PaceScale;

    sealed class Npc
    {
        public string Name;
        public float TargetHeight;   // metres, seated, after the rig scale
        public float Speed;          // m/s, authored; PaceScale is applied at staging time
        public float Lane;           // metres from the centreline; negative = oncoming lane
        public float MeetDistance;   // metres around the loop from the player's start line
        public string Frame, Accent; // bike livery hexes
        /// <summary>
        /// Optional path to this rider's OWN skinned GLB, instead of Coral's sculpt.
        ///
        /// Every other rider here is Coral's body with a hue-rotated copy of her atlas, which is
        /// why the highland cast shares one silhouette, one face and one hair mass. The quartet
        /// built by design_assets/3d/kuro/q4_build_quartet.py keeps Coral's dense sculpt, her
        /// 24-joint skin, her baked hand grip and her SmileDecal - so the bike fit and the
        /// greeting are unchanged - but cuts and reshapes the hair and repaints the atlas per
        /// texel in 3D, so the difference is geometry and printed kit rather than a hue offset.
        ///
        /// A rider with a Rig set carries her kit IN THE ATLAS, so <see cref="ApplyKit"/> must
        /// leave her alone; swapping in a CoralKit_*.png would paint Coral's jersey back on.
        /// </summary>
        public string Rig;
        public string[] Lines;
        /// <summary>True = rides UP the climb with the player (reverse = false). The original
        /// roster all descend; only <see cref="MovingCyclists"/> use this (copilot A2, 2026-09-26).</summary>
        public bool Ascending;
        /// <summary>Why this rider is HERE - logged at staging time (moving cyclists only).</summary>
        public string Where;
    }

    // Spread 320 m -> 23,110 m along the 24.06 km POINT-TO-POINT ascent, so the player meets
    // roughly one rider every 780 m. The spacing deliberately WIDENS above ~19 km: the windward
    // ramp and the col are exposed, cold and 1,900 m up, and a crowd there would read as a
    // stadium rather than as a high pass. The lower pasture, where the walls and the hut are,
    // carries the denser traffic.
    //
    // This course is OPEN, not a loop. There is no seam to hide a rider behind and no second
    // lap to re-meet them on, so every meet distance has to earn its place the first time.
    //
    // PROVISIONAL, all of it. Distances are metres along the "azora" segment and are only
    // meaningful while that segment is ~24 km; heights, speeds, lanes and liveries are art
    // direction and are expected to move.
    //
    // Every rider is on the oncoming side (negative lane) with reverse = true - which on a
    // point-to-point climb means they are DESCENDING the pass while the player ascends it. That
    // is both correct traffic (the road serves both directions) and the only arrangement in
    // which a greeting can ever fire: an NPC travelling the player's way at a similar speed sits
    // permanently behind them, outside the greeting view cone.
    //
    // Speeds track personality: Hinata (sprinter) and Homare (record-chaser) fastest, Raku
    // (loaded bikepacker) and Suzuha (hut girl on a town bike) slowest, with the working riders
    // - shepherds, wallers, hut staff - purposeful in between.
    //
    // Kit colours live in npc_palette_variants.py, keyed by these same names. The Frame/Accent
    // hexes below are the BIKE livery and are matched to each rider's kit, not to each other.
    static readonly Npc[] Roster =
    {
        new Npc { Name = "Takane", TargetHeight = 1.36f, Speed = 5.0f, Lane = -1.70f, MeetDistance = 320f,
                  Frame = "3FA9E0", Accent = "F2C233",
                  Lines = new[] { "Air's rising - perfect flying day!",
                                  "I'll wave from up on the thermals.",
                                  "Wing's packed, legs next. Up we go.",
                                  "Every col is a launch site if you squint." } },

        new Npc { Name = "Midori", TargetHeight = 1.43f, Speed = 3.8f, Lane = -1.88f, MeetDistance = 1050f,
                  Frame = "4E7A3A", Accent = "C9A24A",
                  Lines = new[] { "Mind the sheep on the verge!",
                                  "Whole hillside's mine to watch today.",
                                  "They wander. I pedal. It balances out.",
                                  "Gate's shut behind you? Good lad." } },

        new Npc { Name = "Asahi", TargetHeight = 1.39f, Speed = 5.4f, Lane = -1.62f, MeetDistance = 1760f,
                  Frame = "F07A22", Accent = "F3EBD6",
                  Lines = new[] { "Best drag in the world at sunrise.",
                                  "Settle in - it's a long one.",
                                  "I've done this climb before breakfast.",
                                  "Tempo, not heroics. That's the secret." } },

        new Npc { Name = "Natsuki", TargetHeight = 1.33f, Speed = 4.2f, Lane = -1.78f, MeetDistance = 2380f,
                  Frame = "8B5FD0", Accent = "FFFFFF",
                  Lines = new[] { "The gentian's out this week!",
                                  "A daisy for the summit?",
                                  "Basket's nearly full - one more verge.",
                                  "Purple, blue, gold. The whole meadow." } },

        new Npc { Name = "Shinobu", TargetHeight = 1.37f, Speed = 5.2f, Lane = -1.70f, MeetDistance = 2750f,
                  Frame = "222A45", Accent = "9A8FC4",
                  Rig = "Assets/Kuro/NPC/KuroNPC_Shinobu_Rigged.glb",
                  Lines = new[] { "Thin air suits me. Always has.",
                                  "Navy and violet - I picked it for the dusk.",
                                  "Keep your shoulders loose on the ramps.",
                                  "I'll be on the col before the cloud is." } },

        new Npc { Name = "Ibuki", TargetHeight = 1.45f, Speed = 5.6f, Lane = -1.92f, MeetDistance = 3110f,
                  Frame = "8A9298", Accent = "2E86A8",
                  Lines = new[] { "Get in the echelon - wind's cruel!",
                                  "Lean into it and it'll hold you.",
                                  "Crosswind's a wall you can't see.",
                                  "Shelter's on my wheel. Take it." } },

        new Npc { Name = "Suzuha", TargetHeight = 1.31f, Speed = 3.6f, Lane = -1.66f, MeetDistance = 3880f,
                  Frame = "D6444E", Accent = "F5EFE2",
                  Lines = new[] { "Soup's on at the refuge!",
                                  "Rest at the hut before the col.",
                                  "Mum says nobody climbs hungry.",
                                  "Two more bends and you're at our door." } },

        new Npc { Name = "Reika", TargetHeight = 1.37f, Speed = 5.1f, Lane = -1.84f, MeetDistance = 4520f,
                  Frame = "55636E", Accent = "E09A38",
                  Lines = new[] { "You'll cry at the top, I promise.",
                                  "One more ramp for that view.",
                                  "I came for the panorama, stayed for the pain.",
                                  "Camera's ready. So are my legs." } },

        new Npc { Name = "Kanade", TargetHeight = 1.34f, Speed = 4.0f, Lane = -1.72f, MeetDistance = 5260f,
                  Frame = "B598E8", Accent = "D47ACB",
                  Lines = new[] { "I hum on the long drags - join in!",
                                  "The wind carries a tune up here.",
                                  "Find the rhythm and the hill shrinks.",
                                  "Four beats a pedal stroke. Try it." } },

        new Npc { Name = "Shione", TargetHeight = 1.46f, Speed = 3.9f, Lane = -1.90f, MeetDistance = 6040f,
                  Frame = "C79A5A", Accent = "6E8A3C",
                  Lines = new[] { "Hay's in before the weather turns.",
                                  "Cattle grid ahead - steady now.",
                                  "Land doesn't wait, and nor do I.",
                                  "Tractor on the bend. Give it room." } },

        new Npc { Name = "Tamaki", TargetHeight = 1.40f, Speed = 4.6f, Lane = -1.75f, MeetDistance = 6810f,
                  Frame = "6E8F5C", Accent = "D9A63F",
                  Lines = new[] { "She'll herd you if you're slow!",
                                  "Good line - even the dog approves.",
                                  "Collie's fitter than both of us.",
                                  "Whistle twice and she'll bring you home." } },

        new Npc { Name = "Noa", TargetHeight = 1.32f, Speed = 4.1f, Lane = -1.68f, MeetDistance = 7480f,
                  Frame = "A8D4F0", Accent = "F2F2F2",
                  Lines = new[] { "Those cumulus mean a warm one.",
                                  "Watch the cloud-shadow race us up.",
                                  "Sky's doing something lovely today.",
                                  "No rain in that. Trust me, I watch." } },

        new Npc { Name = "Itsuki", TargetHeight = 1.28f, Speed = 4.8f, Lane = -1.80f, MeetDistance = 8250f,
                  Frame = "1F3A8E", Accent = "FFFFFF",
                  Lines = new[] { "How do you pace this whole thing?",
                                  "My legs, the wind, the hill - all three!",
                                  "School team said the col was easy. Liars.",
                                  "Can I sit on your wheel? Please?" } },

        new Npc { Name = "Ayame", TargetHeight = 1.35f, Speed = 4.3f, Lane = -1.64f, MeetDistance = 9010f,
                  Frame = "6B2FA8", Accent = "E8C84A",
                  Lines = new[] { "The verges are a painting today.",
                                  "Purple all the way to the col!",
                                  "Irises down by the beck - go look.",
                                  "Flowers first, finish time second." } },

        new Npc { Name = "Sousuke", TargetHeight = 1.47f, Speed = 4.0f, Lane = -1.94f, MeetDistance = 9740f,
                  Frame = "B8B3A6", Accent = "4E5C66",
                  Lines = new[] { "Built half these walls myself.",
                                  "Stone lasts - so does a steady pace.",
                                  "No mortar in any of it. Just fit.",
                                  "That gap's a sheep creep, not a fault." } },

        new Npc { Name = "Momiji", TargetHeight = 1.38f, Speed = 4.4f, Lane = -1.76f, MeetDistance = 10420f,
                  Frame = "A8452A", Accent = "D99A3C",
                  Lines = new[] { "Grass goes gold before the frost.",
                                  "Golden slopes, golden light - go!",
                                  "Two weeks and this'll all be bronze.",
                                  "I ride the turn of the season, every year." } },

        new Npc { Name = "Hinata", TargetHeight = 1.36f, Speed = 5.7f, Lane = -1.60f, MeetDistance = 11180f,
                  Frame = "F5D423", Accent = "2A2A2A",
                  Lines = new[] { "Full gas across the sunny shelf!",
                                  "Chase me to the next cattle grid!",
                                  "Sun's out, so am I. Go go go!",
                                  "I only climb fast. Slow hurts more." } },

        new Npc { Name = "Raku", TargetHeight = 1.44f, Speed = 3.5f, Lane = -1.86f, MeetDistance = 11960f,
                  Frame = "7A8A46", Accent = "C4A878",
                  Lines = new[] { "No rush - the col waits for us.",
                                  "Camping by the tarn tonight.",
                                  "Everything I own is on this bike.",
                                  "Four days out. Best four of the year." } },

        new Npc { Name = "Chiharu", TargetHeight = 1.34f, Speed = 4.2f, Lane = -1.70f, MeetDistance = 12640f,
                  Frame = "6F9A4E", Accent = "C9A86A",
                  Lines = new[] { "That's knapweed, not thistle!",
                                  "Rare orchid on this shoulder - careful.",
                                  "Forty-one species on this verge alone.",
                                  "Notebook's full and we're not halfway." } },

        new Npc { Name = "Kazuha", TargetHeight = 1.41f, Speed = 4.9f, Lane = -1.82f, MeetDistance = 13390f,
                  Frame = "2FB3A8", Accent = "E8B33C",
                  Lines = new[] { "Wind's free energy up here.",
                                  "Turbines and legs - both love a gale.",
                                  "I service the mill on the ridge.",
                                  "Tailwind home is my whole wage." } },

        new Npc { Name = "Yamato", TargetHeight = 1.49f, Speed = 4.7f, Lane = -1.96f, MeetDistance = 14150f,
                  Frame = "16255C", Accent = "F0F0F0",
                  Lines = new[] { "I don't attack - I just don't stop.",
                                  "Sit on my wheel, I'll grind the drag.",
                                  "Same gear, same cadence, all day.",
                                  "Diesel doesn't sprint. Diesel arrives." } },

        new Npc { Name = "Michiru", TargetHeight = 1.39f, Speed = 4.4f, Lane = -1.74f, MeetDistance = 14880f,
                  Frame = "8A6038", Accent = "7A9A3C",
                  Lines = new[] { "Sheep track shortcut - follow me?",
                                  "Every path up here earns something.",
                                  "Maps are suggestions on this fell.",
                                  "I've never taken the same line twice." } },

        new Npc { Name = "Sara", TargetHeight = 1.33f, Speed = 4.1f, Lane = -1.66f, MeetDistance = 15620f,
                  Frame = "F2F2F0", Accent = "2F86C4",
                  Lines = new[] { "Look up - three gliders circling!",
                                  "They soar, we climb. Both worth it.",
                                  "Club's flying from the ridge today.",
                                  "That's a thermal. Right over the col." } },

        new Npc { Name = "Tomo", TargetHeight = 1.37f, Speed = 3.7f, Lane = -1.88f, MeetDistance = 16400f,
                  Frame = "C8342E", Accent = "F5EFE2",
                  Lines = new[] { "Stew's simmering - earn it!",
                                  "Nothing beats soup at 1,900 metres.",
                                  "I carry the stock up on this rack.",
                                  "Bowl's yours if you make the top." } },

        new Npc { Name = "Genki", TargetHeight = 1.27f, Speed = 5.5f, Lane = -1.63f, MeetDistance = 17150f,
                  Frame = "9BE032", Accent = "2A2A2A",
                  Lines = new[] { "This is the BEST hill ever!",
                                  "Race you to the next wall - go go go!",
                                  "I'm not even tired! Okay, a bit!",
                                  "Again! Can we do it again after?" } },

        new Npc { Name = "Kiyoshi", TargetHeight = 1.45f, Speed = 3.9f, Lane = -1.90f, MeetDistance = 17930f,
                  Frame = "96A0A6", Accent = "8A7050",
                  Lines = new[] { "Slow breath, long climb, clear mind.",
                                  "I've ridden this drag fifty years.",
                                  "The hill hasn't changed. I have.",
                                  "Patience is a gear, lad. Use it." } },

        new Npc { Name = "Nagi", TargetHeight = 1.35f, Speed = 4.3f, Lane = -1.72f, MeetDistance = 18660f,
                  Frame = "3FC4A0", Accent = "F0F0F0",
                  Lines = new[] { "No wind yet - savour it.",
                                  "The pasture's a mirror at first light.",
                                  "Stillness like this lasts an hour.",
                                  "Listen. Nothing. Isn't it perfect?" } },

        new Npc { Name = "Subaru", TargetHeight = 1.40f, Speed = 4.6f, Lane = -1.84f, MeetDistance = 19420f,
                  Frame = "2E2A8C", Accent = "F2C233",
                  Lines = new[] { "Clearest stars are from the col.",
                                  "Ride the golden hour up with me.",
                                  "Dynamo's charged. I ride till dark.",
                                  "Milky Way straight over that cairn." } },

        new Npc { Name = "Homare", TargetHeight = 1.42f, Speed = 5.6f, Lane = -1.68f, MeetDistance = 20880f,
                  Frame = "D81F35", Accent = "F5F5F5",
                  Lines = new[] { "Chasing my own record - hold on!",
                                  "Beat my time to the col, I dare you.",
                                  "Thirty-eight minutes. That's the mark.",
                                  "No team, no coach. Just the clock." } },

        new Npc { Name = "Kasumi", TargetHeight = 1.34f, Speed = 4.0f, Lane = -1.78f, MeetDistance = 21940f,
                  Frame = "B6C2C8", Accent = "EDEDED",
                  Lines = new[] { "The valleys are blue with haze today.",
                                  "Hold still - the distance is perfect.",
                                  "Layers on layers, all the way out.",
                                  "Aerial depth like this is a gift." } },

        new Npc { Name = "Tsumugi", TargetHeight = 1.38f, Speed = 4.2f, Lane = -1.86f, MeetDistance = 23110f,
                  Frame = "C79A4E", Accent = "8A4FA8",
                  Lines = new[] { "I weave the colours of these meadows.",
                                  "Every drag stitches into the story.",
                                  "Emerald to gold - that's my next warp.",
                                  "The panniers are handmade. So am I." } },
    };

    // -----------------------------------------------------------------------------------------
    // MOVING CYCLISTS (copilot session 1, A2, 2026-09-26) - the Swiss WINTER club riders, same
    // pattern as TakaNpcRoster.MovingCyclists. Fourteen riders in winter club kits (thermal
    // long sleeves, tights, gloves, overshoes) from design_assets/3d/kuro/azora_npc_liveries.py
    // -> KuroKit_<Name>.png + azora_riders.json. Most climb WITH the player, a few descend; two
    // pass through each village (2.6 km, 12.6 km). Appended after the roster, so the original
    // riders' child order is unchanged. Names checked against every roster. PROVISIONAL tuning.
    // WP-H3 (2026-09-27) appends sixteen more (30 total): four club groups and four singles.
    static readonly Npc[] MovingCyclists =
    {
        new Npc { Name = "Nina", TargetHeight = 1.36f, Speed = 4.2f, Lane = -0.95f, Ascending = true, MeetDistance = 380f,
                  Frame = "D52B1E", Accent = "F4F4F2", Where = "lakeview approach, riding up - VC Azora",
                  Lines = new[] { "Gruezi! Cold legs, warm heart.", "The lake is freezing at the edges already.", "Coffee in the village - two kilometres!" } },
        new Npc { Name = "Noah", TargetHeight = 1.44f, Speed = 4.6f, Lane = -1.00f, Ascending = true, MeetDistance = 1150f,
                  Frame = "1E3A6E", Accent = "7FB8E0", Where = "by the island bridge, riding up - RV Seeblick",
                  Lines = new[] { "Look at the island town under the snow!", "Keep your gloves on - the wind bites here.", "Hopp, hopp!" } },
        new Npc { Name = "Lea", TargetHeight = 1.35f, Speed = 3.9f, Lane = -0.90f, Ascending = true, MeetDistance = 2450f,
                  Frame = "F4F4F2", Accent = "D52B1E", Where = "first village, riding up past the cafe - Club Edelweiss",
                  Lines = new[] { "Hot chocolate at the cafe, yes?", "Mind the slush by the fountain.", "Salut! The chalets look like cake." } },
        new Npc { Name = "Elias", TargetHeight = 1.45f, Speed = 5.0f, Lane = -1.60f, MeetDistance = 2780f,
                  Frame = "2E5E3E", Accent = "E8C24A", Where = "first village, riding DOWN - SC Arvenwald",
                  Lines = new[] { "Down to the lake - my hands are ice!", "The pass is open. Just.", "Bon courage up there!" } },
        new Npc { Name = "Laura", TargetHeight = 1.37f, Speed = 4.0f, Lane = -1.00f, Ascending = true, MeetDistance = 4550f,
                  Frame = "7A2F3E", Accent = "F2C9A0", Where = "river valley below the viaduct, riding up - Velo Viadukt",
                  Lines = new[] { "Did you see the frozen waterfall?", "The river never freezes. Too fast.", "Wait for the train on the viaduct!" } },
        new Npc { Name = "Anja", TargetHeight = 1.35f, Speed = 4.3f, Lane = -0.95f, Ascending = true, MeetDistance = 7050f,
                  Frame = "E07A22", Accent = "1E1E22", Where = "switchbacks, riding up - Team Steinbock",
                  Lines = new[] { "Hairpin after hairpin. I love it.", "Count the snow poles, not the metres.", "You're doing great!" } },
        new Npc { Name = "Nico", TargetHeight = 1.46f, Speed = 5.4f, Lane = -1.62f, MeetDistance = 9600f,
                  Frame = "2A2C31", Accent = "D52B1E", Where = "upper switchbacks, riding DOWN - Schwarzsee RC",
                  Lines = new[] { "Brr - the descent is the hard part!", "Snow on the ridge, but the road is clear.", "Hopp Schwiiz!" } },
        new Npc { Name = "Seraina", TargetHeight = 1.36f, Speed = 3.8f, Lane = -0.95f, Ascending = true, MeetDistance = 12450f,
                  Frame = "3F6F8F", Accent = "F4F4F2", Where = "second village, riding up - Glacier Club",
                  Lines = new[] { "One more village, then only sky.", "The church bells mean it's noon.", "Warm up by the stove if you stop." } },
        new Npc { Name = "Fabian", TargetHeight = 1.44f, Speed = 5.1f, Lane = -1.58f, MeetDistance = 12760f,
                  Frame = "C9A46A", Accent = "F4F4F2", Where = "second village, riding DOWN to the bakery - Kaffee Velo",
                  Lines = new[] { "Nusstorte at the bakery - trust me.", "The observatory is worth every metre.", "Gute Fahrt!" } },
        new Npc { Name = "Reto", TargetHeight = 1.45f, Speed = 4.4f, Lane = -1.00f, Ascending = true, MeetDistance = 15150f,
                  Frame = "D52B1E", Accent = "F2D21B", Where = "cliffside ridge, riding up - Gotthard Cyclisme",
                  Lines = new[] { "Look down - a sea of cloud!", "Stay inside the guardrail, eh?", "The chevrons mean it bites." } },
        new Npc { Name = "Ursina", TargetHeight = 1.37f, Speed = 5.5f, Lane = -1.60f, MeetDistance = 17300f,
                  Frame = "8E6CC8", Accent = "F4F4F2", Where = "cliffside ridge, riding DOWN - Alpenrose Velo",
                  Lines = new[] { "The summit is magic in the snow.", "Two kilometres of drifts up there.", "Allez, nearly at the top!" } },
        new Npc { Name = "Gian", TargetHeight = 1.44f, Speed = 3.9f, Lane = -0.95f, Ascending = true, MeetDistance = 19250f,
                  Frame = "17897E", Accent = "F2F4F7", Where = "summit plateau, riding up - Col Azora",
                  Lines = new[] { "See the dome? That's the observatory.", "The wind makes waves in the snow.", "Photo at the col, then down!" } },
        new Npc { Name = "Flurina", TargetHeight = 1.35f, Speed = 4.8f, Lane = -1.60f, MeetDistance = 19900f,
                  Frame = "F28BB5", Accent = "1F4E5F", Where = "just past the col, riding DOWN the way you came - VC Morgenrot",
                  Lines = new[] { "Made it! Earned the view.", "Tea at the observatory kept me alive.", "Enjoy the descent!" } },
        new Npc { Name = "Beat", TargetHeight = 1.45f, Speed = 5.2f, Lane = -1.00f, Ascending = true, MeetDistance = 21300f,
                  Frame = "5A646E", Accent = "E8C24A", Where = "summit descent by the viewpoint, riding with you - Granit RV",
                  Lines = new[] { "The frozen lake at sunset - look left!", "Easy on the brakes, it's icy in the shade.", "Last one down buys the fondue." } },

        // ============ WP-H3 (copilot, 2026-09-27): about double the moving layer (14 -> 30).
        // Club GROUPS ride at ONE shared pace, so TrafficLine never makes them swerve around each
        // other (a same-direction pass needs a 0.2 m/s pace margin). Riders two abreast share a
        // MeetDistance and differ in lane by ~0.75 m (0.55 read as helmets touching); rows are 6 m apart (two 3 m route points).
        // Ascending: a larger MeetDistance is further up the road, i.e. the front of the group.
        // Descending: the front is the SMALLER MeetDistance. Kits: azora_npc_liveries.py.
        // --- Club Lac Azora trio, lakeview approach, riding up
        new Npc { Name = "Marco", TargetHeight = 1.45f, Speed = 4.4f, Lane = -0.95f, Ascending = true, MeetDistance = 696f,
                  Frame = "2A7F9E", Accent = "F4F4F2", Where = "lakeview approach, leading the Club Lac Azora trio up",
                  Lines = new[] { "Club ride! Jump on the back if you like.", "Julia sets the pace, I just pull.", "Gruezi mitenand!" } },
        new Npc { Name = "Julia", TargetHeight = 1.36f, Speed = 4.4f, Lane = -0.60f, Ascending = true, MeetDistance = 690f,
                  Frame = "2A7F9E", Accent = "F4F4F2", Where = "lakeview approach, Club Lac Azora trio (second row, inside)",
                  Lines = new[] { "Steady, boys - it's a long way up.", "Isn't the lake gorgeous in winter?", "Hopp, hopp, hopp!" } },
        new Npc { Name = "Andrin", TargetHeight = 1.44f, Speed = 4.4f, Lane = -1.35f, Ascending = true, MeetDistance = 690f,
                  Frame = "2A7F9E", Accent = "F4F4F2", Where = "lakeview approach, Club Lac Azora trio (second row, outside)",
                  Lines = new[] { "Sitting in. Don't tell Marco.", "My toes stopped talking an hour ago.", "Salut!" } },
        // --- Rennclub Zuerisee quartet, false flat, riding up two by two
        new Npc { Name = "Selina", TargetHeight = 1.37f, Speed = 4.6f, Lane = -0.60f, Ascending = true, MeetDistance = 10806f,
                  Frame = "1B2A4A", Accent = "F2C230", Where = "false flat, Rennclub Zuerisee quartet (front, inside)",
                  Lines = new[] { "Two by two, nice and tidy!", "False flat - the legs know it's a climb.", "Allez, allez!" } },
        new Npc { Name = "Nadine", TargetHeight = 1.35f, Speed = 4.6f, Lane = -1.35f, Ascending = true, MeetDistance = 10806f,
                  Frame = "1B2A4A", Accent = "F2C230", Where = "false flat, Rennclub Zuerisee quartet (front, outside)",
                  Lines = new[] { "We ride this every Sunday. Snow or not.", "Tea stop at the next village!", "Gruezi!" } },
        new Npc { Name = "Pascal", TargetHeight = 1.45f, Speed = 4.6f, Lane = -0.60f, Ascending = true, MeetDistance = 10800f,
                  Frame = "1B2A4A", Accent = "F2C230", Where = "false flat, Rennclub Zuerisee quartet (back, inside)",
                  Lines = new[] { "Wheel on! Hold the line.", "This wind is from Italy, I swear.", "Hopp Schwiiz!" } },
        new Npc { Name = "Corinne", TargetHeight = 1.36f, Speed = 4.6f, Lane = -1.35f, Ascending = true, MeetDistance = 10800f,
                  Frame = "1B2A4A", Accent = "F2C230", Where = "false flat, Rennclub Zuerisee quartet (back, outside)",
                  Lines = new[] { "Chatting keeps you warm!", "Did you see the church spire back there?", "Bis spaeter!" } },
        // --- Bergsteiger VC trio, cliff ridge, riding DOWN
        new Npc { Name = "Silvan", TargetHeight = 1.46f, Speed = 5.2f, Lane = -1.60f, MeetDistance = 16644f,
                  Frame = "6E2233", Accent = "B8BEC6", Where = "cliffside ridge, leading the Bergsteiger VC trio down",
                  Lines = new[] { "Careful - black ice under the cliff!", "We summited at dawn. Worth it.", "Gute Fahrt!" } },
        new Npc { Name = "Rahel", TargetHeight = 1.36f, Speed = 5.2f, Lane = -1.22f, MeetDistance = 16650f,
                  Frame = "6E2233", Accent = "B8BEC6", Where = "cliffside ridge, Bergsteiger VC trio (second row, inside)",
                  Lines = new[] { "The cloud sea is below us - magic!", "Keep your hands on the drops.", "Tschau!" } },
        new Npc { Name = "Leonie", TargetHeight = 1.35f, Speed = 5.2f, Lane = -1.98f, MeetDistance = 16650f,
                  Frame = "6E2233", Accent = "B8BEC6", Where = "cliffside ridge, Bergsteiger VC trio (second row, outside)",
                  Lines = new[] { "Summit selfie done!", "My fingers are ice lollies.", "Nearly there for you!" } },
        // --- retired couple, summit descent, riding with the player side by side
        new Npc { Name = "Urs", TargetHeight = 1.43f, Speed = 3.9f, Lane = -1.35f, Ascending = true, MeetDistance = 22400f,
                  Frame = "2E5E3E", Accent = "E8E4DA", Where = "summit descent, riding side by side with Gianna",
                  Lines = new[] { "Forty winters on this pass, and still.", "No hurry. The lake isn't going anywhere.", "Gruezi, young one!" } },
        new Npc { Name = "Gianna", TargetHeight = 1.36f, Speed = 3.9f, Lane = -0.60f, Ascending = true, MeetDistance = 22400f,
                  Frame = "E8E4DA", Accent = "2E5E3E", Where = "summit descent, riding side by side with Urs",
                  Lines = new[] { "Urs brakes for every view. Every one.", "Soup at the bottom - barley, the best.", "Take care on the bends!" } },
        // --- singles. Timo, Curdin and Ladina roll SLOWLY through the cafe and the col viewpoint
        // (arriving / leaving). A true foot-down stop needs RaceNpc's planted-foot pose; a
        // zero-speed NPCCyclist freezes mid-pedal (the Fuji F5 defect), so they keep rolling.
        new Npc { Name = "Timo", TargetHeight = 1.44f, Speed = 2.0f, Lane = -1.00f, Ascending = true, MeetDistance = 2560f,
                  Frame = "F2C230", Accent = "1E1E22", Where = "first village, rolling up to the cafe - Postauto Velo",
                  Lines = new[] { "Cafe stop! Best Nusstorte on the pass.", "Rolling in slow - the cobbles are icy.", "Gruezi!" } },
        new Npc { Name = "Curdin", TargetHeight = 1.45f, Speed = 2.1f, Lane = -1.60f, MeetDistance = 12640f,
                  Frame = "3A3F46", Accent = "E07A22", Where = "second village, rolling out past the bakery - Engadin RV",
                  Lines = new[] { "Just had a Gipfeli. Now I'm unstoppable.", "Slow through the village, please!", "Tschau zaeme!" } },
        new Npc { Name = "Ladina", TargetHeight = 1.35f, Speed = 2.2f, Lane = -1.60f, MeetDistance = 19700f,
                  Frame = "4FA3C7", Accent = "F4F4F2", Where = "col viewpoint, rolling away from the summit - Gletscher RC",
                  Lines = new[] { "Did you stop for the view? You must!", "The observatory has hot tea.", "You made it - bravo!" } },
        new Npc { Name = "Mirjam", TargetHeight = 1.37f, Speed = 5.3f, Lane = -1.62f, MeetDistance = 8300f,
                  Frame = "A8324A", Accent = "F4F4F2", Where = "switchbacks, riding DOWN - Velo Himbeer",
                  Lines = new[] { "Wheee - hairpins are more fun going down!", "Snow poles every fifty metres - count them.", "Ciao!" } },
    };

    /// <summary>Every Azora rider this roster owns: the original roster, then the moving cyclists.</summary>
    static IEnumerable<Npc> AllRiders => Roster.Concat(MovingCyclists);

    /// <summary>Names of the moving-cyclist layer, for harnesses (AzoraNpcPlaymodeCapture).</summary>
    public static string[] MovingCyclistNames => MovingCyclists.Select(n => n.Name).ToArray();

    /// <summary>
    /// Bike materials to repaint. Brushed_Metal and Road_Tyre are deliberately absent: spokes,
    /// chain and tyres stay metal and rubber on anyone's bicycle.
    /// </summary>
    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    [MenuItem("MapleRide/NPCs/Stage Azora Roster")]
    public static void AddAllToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Find-or-create, never create-blindly: a second "Azora NPCs" root would hide the
        // first one's riders from Prune() and quietly double the whole cast.
        var root = GameObject.Find(ParentName);
        if (root == null) root = new GameObject(ParentName);

        // Only ever used as the FALLBACK line inside Stage(); the real line comes from the baked
        // route graph. It is built here rather than per-rider so thirty riders do not resample
        // the same road thirty times.
        var route = MapleRideKuroSetup.ResampleRoute(MapleRideKuroSetup.GetRoadPoints(), RouteSpacing);
        StageAll(root.transform, route);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[azora] staged {Roster.Length} riders on segment '{SegmentId}'.");
    }

    /// <summary>
    /// Stages ONLY the moving-cyclist layer (A2), leaving the original roster and its material
    /// assets untouched (same reasoning as TakaNpcRoster.AddMovingCyclistsToScene). Idempotent
    /// (exact-name prune). Also bakes each new rider's greeting portrait.
    /// Menu: MapleRide/NPCs/Stage Azora Moving Cyclists
    /// </summary>
    [MenuItem("MapleRide/NPCs/Stage Azora Moving Cyclists")]
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
                Debug.LogWarning($"[azora] {def.Name}: not in any KuroRiders manifest - run " +
                                 "design_assets/3d/kuro/azora_npc_liveries.py first.");
            var go = Stage(def, root.transform, route);
            if (go == null) continue;
            staged++;
            var cyc = go.GetComponent<NPCCyclist>();
            Debug.Log($"[azora] moving {def.Name}: {def.MeetDistance:F0} m {(def.Ascending ? "UP" : "DOWN")}, " +
                      $"{StagedSpeed(def.Speed):F2} m/s, lane {def.Lane:F2}, progress {cyc.progress}/{cyc.route.Length}, " +
                      $"pos {go.transform.position:F1} - {def.Where}");
        }
        AssetDatabase.SaveAssets();
        EnsureTrafficAndLod(root);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        int baked = 0;
        foreach (var def in MovingCyclists)
            if (NpcPortraitBake.BakeSingle(def.Name, exposureEv: -1.5f)) baked++;
        Debug.Log($"[azora] staged {staged}/{MovingCyclists.Length} moving cyclists, {baked} portraits; " +
                  $"'{ParentName}' now has {root.transform.childCount} children.");
    }

    /// <summary>
    /// Idempotent runtime helpers: one TrafficAvoidance ON the "Azora NPCs" root with its rider
    /// list written explicitly, and an AzoraRiderLod on every moving cyclist.
    /// </summary>
    static void EnsureTrafficAndLod(GameObject root)
    {
        var riders = root.GetComponentsInChildren<NPCCyclist>(true)
                         .Where(c => c.gameObject.name.StartsWith(NamePrefix)).ToArray();
        var avoid = root.GetComponent<TrafficAvoidance>();
        if (avoid == null) avoid = root.AddComponent<TrafficAvoidance>();
        avoid.riders = riders;
        EditorUtility.SetDirty(avoid);
        var moving = new HashSet<string>(MovingCyclists.Select(m => NamePrefix + m.Name));
        int lods = 0;
        foreach (var c in riders)
            if (moving.Contains(c.gameObject.name) && c.GetComponent<AzoraRiderLod>() == null)
            { c.gameObject.AddComponent<AzoraRiderLod>(); lods++; }
        Debug.Log($"[azora] traffic avoidance over {riders.Length} riders; AzoraRiderLod added to {lods}.");
    }

    /// <summary>Stages every roster rider. Idempotent - each is pruned by exact name first.</summary>
    public static void StageAll(Transform parent, Vector3[] route)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");

        PurgeOldMaterials();

        foreach (var def in Roster)
        {
            var go = Stage(def, parent, route);
            if (go != null)
                Debug.Log($"[azora] {def.Name}: {def.MeetDistance:F0} m, scale {RigScaleFor(def):F3}, " +
                          $"{StagedSpeed(def.Speed):F2} m/s ({StagedSpeed(def.Speed) * 3.6f:F1} kph), " +
                          $"lane {def.Lane:F2}, pos {go.transform.position:F1}");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Re-writes only the riding PACE onto the Azora riders already staged in the scene.
    ///
    /// Exists because <see cref="NPCCyclist.speed"/> is serialized into the scene: changing
    /// <see cref="PaceScale"/> alone changes nothing the player sees, and re-running the full
    /// <see cref="AddAllToScene"/> to pick it up would re-import, re-rig and re-paint thirty
    /// riders to alter one float - a large, risky pass for a tuning change.
    ///
    /// Idempotent, and matched by EXACT name. A Contains-style match here would reach across
    /// regions: "Sakura NPC Mika" and this roster's riders live in the same scene, and Hanakage
    /// is not an NPCCyclist at all and has her own pacing.
    /// </summary>
    [MenuItem("MapleRide/NPCs/Apply Azora Pace To Scene")]
    public static void ApplyPaceToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int applied = 0, missing = 0;
        var all = UnityEngine.Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
        foreach (var def in AllRiders)
        {
            // EXACT name, and the exact name Stage() uses.
            string objName = NamePrefix + def.Name;
            var cyc = all.FirstOrDefault(c => c.gameObject.name == objName);
            if (cyc == null) { missing++; continue; }

            float before = cyc.speed;
            cyc.speed = StagedSpeed(def.Speed);
            EditorUtility.SetDirty(cyc);
            applied++;
            Debug.Log($"[azora] {def.Name}: pace {before:F2} -> {cyc.speed:F2} m/s " +
                      $"({cyc.speed * 3.6f:F1} kph)");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[azora] pace applied to {applied} rider(s) at scale {PaceScale:F2}" +
                  (missing > 0 ? $"; {missing} roster rider(s) are not in this scene." : "."));
    }

    /// <summary>
    /// Deletes THIS roster's previously generated materials, and nothing else.
    ///
    /// Without it every run leaks a full set: SaveMaterial uses GenerateUniqueAssetPath, so a
    /// second pass writes Rin_Body 1.mat, a third Rin_Body 2.mat, and the folder grows without
    /// bound while the scene silently references whichever copy was newest.
    ///
    /// The ownership test is the leading <c>&lt;Name&gt;_</c> segment matched against THIS
    /// roster's names only. The Sakura riders share this folder, so a broader sweep here would
    /// delete their materials and leave eleven riders on the pass rendering with missing
    /// textures - with nothing in the log to say why.
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

    static GameObject Stage(Npc def, Transform parent, Vector3[] route)
    {
        string objName = NamePrefix + def.Name;
        Prune(parent, objName);

        // Kuro-based body for every named rider in a KuroRiders manifest (Coral's sculpt tears
        // when posed - see KuroRiderBodies); bespoke-rig heroes keep their own model.
        var rigAsset = def.Rig == null && KuroRiderBodies.Has(def.Name)
            ? KuroRiderBodies.Rig(def.Name) : LoadPrefab(def.Rig ?? RigPath);
        var bikeAsset = LoadPrefab(BikePath);
        if (rigAsset == null) { Debug.LogWarning($"[azora] missing rig {def.Rig ?? RigPath}"); return null; }
        if (bikeAsset == null) { Debug.LogWarning($"[azora] missing bike {BikePath}"); return null; }

        float rigScale = RigScaleFor(def);

        // NPCCyclist drives this root along the route and aims it down the tangent; the greeting
        // reads its forward axis for the view cone, so everything hangs off it.
        var npc = new GameObject(objName);
        npc.transform.SetParent(parent, false);

        var rider = new GameObject(def.Name + "Rider");
        rider.transform.SetParent(npc.transform, false);
        rider.transform.localScale = Vector3.one * rigScale;

        // MUST be a child named exactly "Bike". KuroBikeRig.Setup looks for a local "Bike" first
        // and only falls back to a GLOBAL GameObject.Find("Bike") - which, with forty-odd
        // bicycles now in this scene, is a lottery that can hand this rider the player's
        // drivetrain and leave the player's wheels driven by a highland NPC's speed.
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
        // DriveDrivetrain divides WORLD distance travelled by this, so BOTH scales have to be
        // folded in or the wheels spin at the wrong rate for the speed the rider is moving.
        rig.wheelRadius = NpcCanonicalConformance.WheelRadius;
        rig.gearRatio = 2.8f;
        rig.previewCadenceRpm = 55f;
        NpcCanonicalConformance.Configure(rig);

        var cyclist = npc.AddComponent<NPCCyclist>();
        // Authored speed scaled to the group's riding pace; see PaceScale for why.
        cyclist.speed = StagedSpeed(def.Speed);
        cyclist.laneOffset = def.Lane;
        cyclist.groundOffset = GroundOffset;
        // Reversed: oncoming traffic is what lets the greeting fire at all. A rider going the
        // same way at a similar pace sits permanently behind the player, outside the view cone.
        cyclist.reverse = !def.Ascending;   // moving cyclists may ride UP (A2)
        // The Azora ascent, NOT the pass. Set before RebuildFromGraph, which reads it.
        cyclist.segmentId = SegmentId;

        // Derive the line from the BAKED ROUTE GRAPH rather than from a private Catmull-Rom
        // resample, exactly as the pass roster does. Two defects came out of the old path:
        //
        //   * The resampled array was serialized into the scene and never revisited, so when the
        //     road grew the whole roster carried on riding the old shape - which left riders
        //     buried in the current asphalt or stranded below it, still greeting the player from
        //     inside the geometry, because NpcGreeting only tests distance and a cone.
        //   * ResampleRoute's spacing is per-control-point-span, NOT a uniform 3 m, so the
        //     "MeetDistance / Spacing" seating below was never quite the distance it claimed.
        //     Sampling the graph by arc length makes the spacing genuinely uniform.
        //
        // The graph frames also carry the road's superelevation, so the lane offset is measured
        // across the banked surface instead of horizontally.
        if (!cyclist.RebuildFromGraph())
        {
            Debug.LogWarning($"[azora] {def.Name}: no '{SegmentId}' segment in the route graph - " +
                             "falling back to the resampled Sakura road. Bake the Azora " +
                             "route and re-run this menu item.");
            cyclist.route = def.Ascending ? route : route.Reverse().ToArray();
            cyclist.routeIncludesOffsets = false;
        }

        // Placed by DISTANCE, never by a fraction of the route. A fraction silently relocates
        // every rider the moment the loop changes length - which is exactly how Coral once ended
        // up 277 m from where she was authored when the pass was extended.
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
        // would draw from one shared pool and the whole fell would speak in one voice - and in
        // the PASS's voice, which talks about summits and blossom.
        greeting.ownPhrases = def.Lines;

        // Greeting face card. riderName is what NpcPortraitBake keys the baked portrait on
        // (Assets/Resources/NpcPortraits/<Name>.png) and what NpcGreeting.ResolveIdentity uses
        // to find that portrait at runtime, so setting it here means a rider staged today picks
        // up a portrait baked later WITHOUT a re-stage. `portrait` is deliberately left null:
        // it is assigned by MapleRide/NPCs/Bake Greeting Portraits, which must run in Unity
        // against the staged rider - a portrait baked from the source GLB would show Coral's
        // colours for all thirty, since the livery only exists on the scene instance.
        greeting.riderName = def.Name;
        greeting.useFaceCard = true;
        EditorUtility.SetDirty(greeting);

        // NOTE: no KuroOutline here, and that is a decision rather than an omission.
        // It skips anything under "Bike" by default, so on a rider it would only ever shell the
        // SKINNED body - and a skinned hull is driven entirely by its bones, so the 1+thickness
        // localScale it relies on is ignored and the hull renders exactly coincident with the
        // body. It produces no visible silhouette while duplicating a 109k-triangle mesh AND
        // setting updateWhenOffscreen = true on the copy, which defeats culling and re-skins it
        // every frame. Thirty riders, zero pixels.
        SetSmileNeutral(npc, def.Name);

        // Seat the rider once in the editor so the saved scene opens with a real pose rather
        // than a body standing through its own bicycle.
        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);

        // Public on the pass roster, so it is called rather than copied: it MEASURES the posed
        // rider with BakeMesh and writes real local bounds, instead of leaving
        // updateWhenOffscreen on and re-skinning a 109k-triangle body every frame off-camera.
        // With thirty riders on a closed loop - most of them behind the camera at any moment -
        // that is the difference between culling working and not existing.
        SakuraNpcRoster.FreezeSkinnedBounds(npc);
        return npc;
    }

    /// <summary>
    /// Swaps in this rider's recoloured kit atlas.
    ///
    /// The sculpt is shared by every rider in the GAME, not just in this roster, so its material
    /// is shared BY REFERENCE - retexturing it in place would repaint all thirty highland riders,
    /// all eleven pass riders and Coral herself. Always clone.
    /// </summary>
    static void ApplyKit(Npc def, GameObject body)
    {
        if (def.Rig != null) return;  // her kit is painted into her own atlas - see Npc.Rig.

        // Kuro-based riders (KuroRiderBodies) wear their KuroKit atlas; anyone else keeps Coral's.
        var kitPath = KuroRiderBodies.Has(def.Name) ? KuroRiderBodies.KitPath(def.Name)
                                                    : $"{KitDir}/CoralKit_{def.Name}.png";
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(kitPath);
        if (kit == null)
        {
            Debug.LogWarning($"[azora] {def.Name}: no kit texture at {kitPath} - run " +
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

    /// <summary>
    /// Paints this rider's bike livery onto CLONES of the shared Colnago materials.
    ///
    /// The clone is not an optimisation, it is the whole correctness of the function: the GLB's
    /// materials are shared by reference with every other bicycle in the project, including the
    /// player's, so tinting them in place would repaint the entire game one colour.
    /// </summary>
    static void RepaintBike(Npc def, GameObject bike)
    {
        var frame = Hex(def.Frame);
        var accent = Hex(def.Accent);

        // ONE clone per source material per rider, cached by name.
        //
        // The Colnago is built from ~78 separate parts that all share just two materials, so
        // cloning per renderer produced 23 copies of Carbon_Black and 11 of Racing_Red for
        // every rider - and at thirty riders that is over a thousand redundant material assets
        // per staging run, each a separate draw call's worth of state because Unity cannot batch
        // distinct material instances.
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
            Debug.LogWarning($"[azora] {name}: no 'SmileDecal' under the rig - she will greet " +
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
            Debug.LogWarning($"[azora] '{m.shader.name}' exposes no known base-colour texture slot.");
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
        // this project and leaked one extra per build - and here it would also reach across to
        // the Sakura roster's riders.
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

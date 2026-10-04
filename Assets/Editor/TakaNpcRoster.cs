using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the thirty-rider NPC cast for TAKA MOUNTAINS - the 22.11 km point-to-point high road
/// described in <c>improve/region_designs/taka_mountains.md</c> section 5.
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
/// WHY THIRTY AND NOT ELEVEN. Taka is a 22 km road climbing to 2,842 m: thirty riders spread
/// across it give roughly one rider every 735 m, against the
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
/// Menu: MapleRide/NPCs/Stage Taka Roster
/// </summary>
public static class TakaNpcRoster
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
    /// The Taka high road in the baked RouteGraph. Sakura's roster uses "pass"; getting this
    /// wrong does not error - it silently stages thirty highland riders on the mountain pass, on top
    /// of the eleven already there.
    /// </summary>
    const string SegmentId = "taka";

    /// <summary>Scene parent for this region's riders, kept separate from the pass's "NPCs" so a
    /// region can be toggled, moved or deleted as a unit.</summary>
    const string ParentName = "Taka NPCs";

    /// <summary>Object-name prefix. Pruning, pace re-application and any future tooling all match
    /// on the EXACT string <c>NamePrefix + rider name</c>, which is what makes re-running this
    /// menu item converge instead of accumulating duplicates.</summary>
    const string NamePrefix = "Taka NPC ";

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
    /// Taka takes 0.55, the value verified on the SAKURA pass roster, rather than Maple City's
    /// 0.48. The city needed the lower number because its loop is flanked by facades, tram poles
    /// and street furniture at 6 m, so near-field parallax made any given ground speed read
    /// faster. Taka is the opposite case: an open fell with the nearest object often a
    /// dry-stone wall 8 m off and then nothing for a kilometre, so there is almost no near-field
    /// reference and the same speed reads SLOWER. Dropping to 0.48 here would have the descent
    /// traffic look like it was freewheeling in treacle. 0.55 gives 1.93 - 3.14 m/s, i.e.
    /// 6.9 - 11.3 kph apparent - riders coming down a pass without racing it.
    ///
    /// PROVISIONAL: tune here, then re-run <c>MapleRide/NPCs/Apply Taka Pace To Scene</c> - the
    /// staged riders carry a SERIALIZED speed, so changing this constant alone does nothing to
    /// the saved scene.
    /// </summary>
    public const float PaceScale = 0.58f;

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
        /// why the high-road cast shares one silhouette, one face and one hair mass. The quartet
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
        /// thirty all descend; only <see cref="MovingCyclists"/> use this (claude-taka2).</summary>
        public bool Ascending;
        /// <summary>Why this rider is HERE - logged at staging time (moving cyclists only).</summary>
        public string Where;
    }

    // Spread 300 m -> 21,640 m along the 22.11 km POINT-TO-POINT high road, so the player meets
    // roughly one rider every 735 m. That is deliberately SPARSER than Azora's 780 m felt in
    // practice and far sparser than the city loop, because the design doc's identity for this
    // region is loneliness: above the treeline a road is a thread, and a crowd on it reads as a
    // sportive rather than as the highest pass in the game. The density does not thin toward the
    // summit the way Taka's does - the working riders (plough crew, gallery maintenance, pole
    // crew, hut keeper) have business at the top, so the cast stays present all the way up.
    //
    // This course is OPEN, not a loop. There is no seam to hide a rider behind and no second lap
    // to re-meet them on, so every meet distance has to earn its place the first time.
    //
    // PROVISIONAL, all of it. Distances are metres along the "taka" segment and are only
    // meaningful while that segment is ~22 km; heights, speeds, lanes and liveries are art
    // direction and are expected to move.
    //
    // Every rider is on the oncoming side (negative lane) with reverse = true, which on a
    // point-to-point climb means they DESCEND while the player ascends. That is both correct
    // traffic and the only arrangement in which a greeting can fire at all: a rider travelling
    // the player's way at a similar speed sits permanently behind them, outside the view cone.
    //
    // SPEEDS ARE AUTHORED LOWER THAN AZORA'S ACROSS THE BOARD. The band here is 3.7 - 6.4 m/s
    // against Taka's 3.5 - 5.7, but these riders are DESCENDING a 11-12% wall while Taka's
    // descend a gentler one, so the same authored number would read as sluggish. The working
    // riders (Seiji 3.7, Kogane 3.8, Kenzan 3.9, Gou 4.0) stay slow because they are carrying
    // something; the specialists (Hayate 6.2, Reiji 6.0, Hyouma 5.9) are fast because descending
    // is literally their character note.
    //
    // NAME COLLISIONS: every name below was checked against the Sakura, Shiosai, Maple City and
    // Taka rosters. The design doc's rider 16 is "Hayato", which ShiosaiNpcTraffic already owns
    // - materials, scene objects and portrait PNGs are all keyed by NAME in one shared scene, so
    // that collision would silently repaint a coast rider. Renamed HAYATE. Rider 30 is spelled
    // "Hyoga" with a macron in the doc; ASCII here, because this string becomes a filename.
    //
    // Kit colours live in npc_palette_variants.py, keyed by these same names. The Frame/Accent
    // hexes below are the BIKE livery and are matched to each rider's kit, not to each other.
    static readonly Npc[] Roster =
    {
        new Npc { Name = "Iwao", TargetHeight = 1.46f, Speed = 4.6f, Lane = -1.84f, MeetDistance = 300f,
                  Frame = "6A6F77", Accent = "2B3038",
                  Lines = new[] { "This wall breaks the proud.",
                                  "Eleven percent doesn't care who you are.",
                                  "Granite underneath, granite in the legs.",
                                  "Climbed it forty winters. It never softened." } },

        new Npc { Name = "Gaku", TargetHeight = 1.42f, Speed = 5.0f, Lane = -1.66f, MeetDistance = 1020f,
                  Frame = "53606E", Accent = "F2F6FA",
                  Lines = new[] { "Higher is the only direction.",
                                  "Save something - the North Wall's next.",
                                  "Axe on the frame, in case the road ends.",
                                  "Peaks don't come to you. Go up." } },

        new Npc { Name = "Kenzan", TargetHeight = 1.48f, Speed = 3.9f, Lane = -1.95f, MeetDistance = 1760f,
                  Frame = "F07A16", Accent = "FFD24A",
                  Lines = new[] { "I plough this road at dawn - mind the grit.",
                                  "Ice in the shade. Keep it upright.",
                                  "Cleared to the gallery. Past that, gamble.",
                                  "Fat tyres. Because the road lies." } },

        new Npc { Name = "Arata", TargetHeight = 1.37f, Speed = 5.8f, Lane = -1.58f, MeetDistance = 2510f,
                  Frame = "C41E32", Accent = "141418",
                  Lines = new[] { "Came all this way to see if I'd break.",
                                  "Match me to the gallery - go!",
                                  "Nobody knows my name up here. Yet.",
                                  "If I crack, at least I cracked high." } },

        new Npc { Name = "Akane", TargetHeight = 1.40f, Speed = 5.4f, Lane = -1.72f, MeetDistance = 2870f,
                  Frame = "7A1220", Accent = "D3B36A",
                  Rig = "Assets/Kuro/NPC/KuroNPC_Akane_Rigged.glb",
                  Lines = new[] { "Burgundy and gold. Club colours, since you ask.",
                                  "Tie the hair up before the col - trust me.",
                                  "I descend this better than I climb it.",
                                  "Nine hairpins left. I've counted them all." } },

        new Npc { Name = "Touma", TargetHeight = 1.41f, Speed = 4.8f, Lane = -1.88f, MeetDistance = 3240f,
                  Frame = "F4F4F6", Accent = "D0303C",
                  Lines = new[] { "Radio me if the weather turns.",
                                  "Guardrail's your friend - respect it.",
                                  "I ride down what others get carried down.",
                                  "Cold's the one that gets you. Not the grade." } },

        new Npc { Name = "Reiji", TargetHeight = 1.39f, Speed = 6.0f, Lane = -1.62f, MeetDistance = 3980f,
                  Frame = "2A2E3C", Accent = "7FBFE0",
                  Lines = new[] { "Every second colder is a second lost.",
                                  "No wheels to hide behind up here.",
                                  "Frost on the disc. Still turning.",
                                  "The clock doesn't feel altitude." } },

        new Npc { Name = "Kazan", TargetHeight = 1.38f, Speed = 5.6f, Lane = -1.74f, MeetDistance = 4720f,
                  Frame = "E8531A", Accent = "FFB03A",
                  Lines = new[] { "I'll melt this whole mountain!",
                                  "Freezing air, boiling legs - let's go!",
                                  "Snow underfoot, fire in the chest.",
                                  "Cold? I brought my own weather." } },

        new Npc { Name = "Yukito", TargetHeight = 1.35f, Speed = 4.2f, Lane = -1.90f, MeetDistance = 5460f,
                  Frame = "EDF2F7", Accent = "6FA8D8",
                  Lines = new[] { "Spindrift's dancing - beautiful, isn't it?",
                                  "Snow poles guide us home.",
                                  "I come up here just to watch it blow.",
                                  "Winter's not a season. It's an address." } },

        new Npc { Name = "Fubuki", TargetHeight = 1.43f, Speed = 5.3f, Lane = -1.68f, MeetDistance = 6190f,
                  Frame = "5A626E", Accent = "D8E2EC",
                  Lines = new[] { "A whiteout is just a bigger dare.",
                                  "Hold your line when the gust hits!",
                                  "Storm on the col. Perfect.",
                                  "Deep sections in crosswind. Ask me why." } },

        new Npc { Name = "Kai", TargetHeight = 1.36f, Speed = 4.5f, Lane = -1.80f, MeetDistance = 6930f,
                  Frame = "1F6FC4", Accent = "E7C84A",
                  Lines = new[] { "The tarn's melting a metre a decade.",
                                  "Careful - that's a crevasse edge.",
                                  "Sensors in the pannier, ice in the notebook.",
                                  "This glacier remembers longer than we do." } },

        new Npc { Name = "Ryoma", TargetHeight = 1.44f, Speed = 4.4f, Lane = -1.92f, MeetDistance = 7660f,
                  Frame = "7A5A38", Accent = "9C3020",
                  Lines = new[] { "Pain's just weakness leaving cold.",
                                  "We climbed this before gears were easy.",
                                  "Wool breathes. Complaining doesn't.",
                                  "Steel, legs, road. What else do you need?" } },

        new Npc { Name = "Seiji", TargetHeight = 1.40f, Speed = 3.7f, Lane = -1.86f, MeetDistance = 8400f,
                  Frame = "6E6A60", Accent = "D4A72C",
                  Lines = new[] { "Lights are fixed in gallery three.",
                                  "Watch your eyes - dark to bright, quick.",
                                  "Somebody has to keep the roof on.",
                                  "Cargo's heavy. The road's heavier." } },

        new Npc { Name = "Tetsu", TargetHeight = 1.47f, Speed = 4.3f, Lane = -1.70f, MeetDistance = 9140f,
                  Frame = "4A5058", Accent = "B8BEC6",
                  Lines = new[] { "I don't spin - I stamp.",
                                  "Big gear, small steps, all the way up.",
                                  "Cadence is for people in a hurry.",
                                  "Iron legs, iron frame, iron patience." } },

        new Npc { Name = "Masaru", TargetHeight = 1.42f, Speed = 5.1f, Lane = -1.64f, MeetDistance = 9870f,
                  Frame = "F5F3EE", Accent = "2F4C82",
                  Lines = new[] { "The wall rewards patience, not panic.",
                                  "Breathe the thin air slow.",
                                  "Nothing up here is won in the first hour.",
                                  "Ride within it. Then a little more." } },

        new Npc { Name = "Ginji", TargetHeight = 1.41f, Speed = 4.9f, Lane = -1.82f, MeetDistance = 10610f,
                  Frame = "C6CBD2", Accent = "4E93B8",
                  Lines = new[] { "Two thousand climbs, still cold.",
                                  "The lake's frozen glass today.",
                                  "Titanium doesn't rust up here. I might.",
                                  "I know every hairpin by its wind." } },

        new Npc { Name = "Hayate", TargetHeight = 1.38f, Speed = 6.2f, Lane = -1.60f, MeetDistance = 11340f,
                  Frame = "1B1B22", Accent = "8B3CC8",
                  Lines = new[] { "Up is theirs - down is mine!",
                                  "Brake late, live loud on the descent.",
                                  "See you at the bottom. Long before you.",
                                  "Gravity's the only trainer I need." } },

        new Npc { Name = "Souma", TargetHeight = 1.43f, Speed = 4.7f, Lane = -1.88f, MeetDistance = 12080f,
                  Frame = "3C5A82", Accent = "A8C4DC",
                  Lines = new[] { "Head down, six clicks of twelve percent.",
                                  "Don't look up - just turn the pedals.",
                                  "The ridge goes on. So do we.",
                                  "Grind. Then grind. That's the plan." } },

        new Npc { Name = "Ryusei", TargetHeight = 1.31f, Speed = 4.1f, Lane = -1.76f, MeetDistance = 12820f,
                  Frame = "18C0D8", Accent = "F2E24A",
                  Lines = new[] { "First time above the treeline - wow.",
                                  "Did I really just hold your wheel?!",
                                  "It's so QUIET up here!",
                                  "My legs hurt and I love it." } },

        new Npc { Name = "Kogane", TargetHeight = 1.34f, Speed = 3.8f, Lane = -1.94f, MeetDistance = 13550f,
                  Frame = "E8A02C", Accent = "C05A18",
                  Lines = new[] { "Hot cocoa waiting at the lake hut.",
                                  "Earn the summit - then thaw with me.",
                                  "Stove's lit. Come find it.",
                                  "Nobody leaves my counter cold." } },

        new Npc { Name = "Akira", TargetHeight = 1.40f, Speed = 5.7f, Lane = -1.66f, MeetDistance = 14290f,
                  Frame = "9E9E9E", Accent = "1A1A1A",
                  Lines = new[] { "Save it for the North Wall pitches.",
                                  "Every hairpin's a decision - choose well.",
                                  "Read the wall. Then ride it.",
                                  "Effort spent early is effort thrown away." } },

        new Npc { Name = "Shirou", TargetHeight = 1.39f, Speed = 4.6f, Lane = -1.84f, MeetDistance = 15020f,
                  Frame = "FAFCFF", Accent = "B6C6D4",
                  Lines = new[] { "Everything's white up here but the road.",
                                  "Blend into the mountain with me.",
                                  "White bike, white kit, white world.",
                                  "You'll lose me in the next flurry." } },

        new Npc { Name = "Hyouma", TargetHeight = 1.37f, Speed = 5.9f, Lane = -1.62f, MeetDistance = 15760f,
                  Frame = "79C2E8", Accent = "20456E",
                  Lines = new[] { "Ice in the corners - I read it early.",
                                  "Follow my line, not my speed.",
                                  "The descent is a language. I speak it.",
                                  "Cold rubber, warm nerve." } },

        new Npc { Name = "Tenma", TargetHeight = 1.36f, Speed = 5.2f, Lane = -1.78f, MeetDistance = 16500f,
                  Frame = "1F4E8C", Accent = "F0C24A",
                  Lines = new[] { "The sky goes dark blue up here.",
                                  "Closer to heaven every hairpin.",
                                  "Look up. That's why we came.",
                                  "Feather frame, heavy dreams." } },

        new Npc { Name = "Isamu", TargetHeight = 1.42f, Speed = 5.5f, Lane = -1.70f, MeetDistance = 17230f,
                  Frame = "C8263C", Accent = "F2F2F2",
                  Lines = new[] { "The drop keeps you honest.",
                                  "Lean in - the rail holds, your nerve must too.",
                                  "I ride the white line and smile.",
                                  "Fear's just altitude with an opinion." } },

        new Npc { Name = "Gou", TargetHeight = 1.49f, Speed = 4.0f, Lane = -1.90f, MeetDistance = 17970f,
                  Frame = "D2691A", Accent = "3A3A40",
                  Lines = new[] { "I move snow AND mountains.",
                                  "Grind it out - the top comes to the stubborn.",
                                  "Burly bike, burlier rider.",
                                  "Twenty tonnes at dawn. This is my rest day." } },

        new Npc { Name = "Raiga", TargetHeight = 1.40f, Speed = 5.8f, Lane = -1.64f, MeetDistance = 18700f,
                  Frame = "F0D02A", Accent = "2E2E38",
                  Lines = new[] { "I attack out of every gallery!",
                                  "Hold that - betcha you can't.",
                                  "Thunder in the legs, snow in the face.",
                                  "Sit in if you like. I won't." } },

        new Npc { Name = "Kanata", TargetHeight = 1.38f, Speed = 4.4f, Lane = -1.86f, MeetDistance = 19440f,
                  Frame = "3E8ECC", Accent = "E0A93A",
                  Lines = new[] { "You can see three ranges from the col.",
                                  "Everything below us is small now.",
                                  "Loaded bike, open horizon.",
                                  "I'm not going anywhere. Just onward." } },

        new Npc { Name = "Kensuke", TargetHeight = 1.41f, Speed = 4.2f, Lane = -1.92f, MeetDistance = 20170f,
                  Frame = "B6E024", Accent = "E06A18",
                  Lines = new[] { "Set the last poles before the storm.",
                                  "Follow the poles if the white comes down.",
                                  "Red, white, red. Every fifty metres.",
                                  "Someone marked this road for you. You're welcome." } },

        new Npc { Name = "Michio", TargetHeight = 1.44f, Speed = 4.5f, Lane = -1.80f, MeetDistance = 20910f,
                  Frame = "2E2A70", Accent = "D8B44A",
                  Lines = new[] { "The mountain lets you pass, not conquer it.",
                                  "Bow to the summit, then climb.",
                                  "Old steel, older road.",
                                  "Respect first. Speed, if there's any left." } },

        // ---------------------------------------------------------------- THE LEGENDARY
        // Hyoga, "The North Wall" (design doc rider 30, and section 4's encounter).
        //
        // He is staged HERE, in the ordinary roster, on purpose. The Hanakage encounter system
        // owns the scripted four-phase duel; what this entry owns is his PRESENCE - the fact
        // that a player grinding the high road can meet him on the road before any encounter
        // ever triggers, and have him speak. That is the same order Sakura built Hanakage in.
        //
        // Everything about him is authored one notch past the rest of the cast, because a
        // legendary that is merely another rider is not a legendary: he is the tallest
        // silhouette on the mountain (1.58 m seated against a 1.31-1.49 m field), the fastest
        // (6.4 m/s authored, above Hayate's 6.2 descent specialist), and the only rider in the
        // region whose kit is BRIGHTER than the snow - glacier white over cobalt.
        //
        // He is seated last, at 21,640 m: past the summit, on the technical descent, so the
        // player meets him only after earning the whole climb.
        new Npc { Name = "Hyoga", TargetHeight = 1.58f, Speed = 6.4f, Lane = -1.56f, MeetDistance = 21640f,
                  Frame = "F4FAFF", Accent = "1F4E8C",
                  Lines = new[] { "No one holds my wheel on the North Wall.",
                                  "Climb with me - if the mountain lets you.",
                                  "You lasted longer than most. The high road is still mine.",
                                  "I am the wall. The road only agrees with me." } },
    };

    // -----------------------------------------------------------------------------------------
    // MOVING CYCLISTS (claude-taka2, 2026-09-26) - the Mt. Ventoux sportive crowd, following
    // Copilot's Fuji pattern (FujiNpcRoster.MovingCyclists). Fourteen riders in French club kits
    // (design_assets/3d/kuro/taka_npc_liveries.py -> KuroKit_<Name>.png + taka_riders.json),
    // most GRINDING UP the climb with the player, a few descending. Bands: village 40-760 m,
    // forest 0.8-8 km, Chalet stop 9.4 km, limestone moonscape 11-15.3 km, summit 15.4 km.
    // Appended after the thirty, so the original riders' child order is unchanged. Names checked
    // against every roster (C# and every *_riders.json). PROVISIONAL art tuning throughout.
    static readonly Npc[] MovingCyclists =
    {
        new Npc { Name = "Mathis", TargetHeight = 1.44f, Speed = 4.4f, Lane = -0.95f, Ascending = true, MeetDistance = 150f,
                  Frame = "F2D21B", Accent = "1E1E24", Where = "village, riding up - VC Bedoin, jaune",
                  Lines = new[] { "Bonjour! Coffee at the top, not before.", "The village is the last shade you get.", "Allez, allez - it's only 21 km." } },
        new Npc { Name = "Camille", TargetHeight = 1.36f, Speed = 4.0f, Lane = -0.85f, Ascending = true, MeetDistance = 420f,
                  Frame = "8E6CC8", Accent = "F2D21B", Where = "village by the boulangerie, riding up - Lavande Cyclo",
                  Lines = new[] { "Smell the lavender? Enjoy it while it lasts.", "A croissant is a climbing gel, right?", "Salut! Easy pace to the forest." } },
        new Npc { Name = "Baptiste", TargetHeight = 1.45f, Speed = 5.2f, Lane = -1.60f, MeetDistance = 640f,
                  Frame = "1F3F8F", Accent = "D62630", Where = "village, riding DOWN to the cafe - AC Tricolore",
                  Lines = new[] { "Done the summit! Cafe du Col is calling.", "Watch the wind up top - le mistral.", "Bon courage!" } },
        new Npc { Name = "Elodie", TargetHeight = 1.35f, Speed = 3.8f, Lane = -1.00f, Ascending = true, MeetDistance = 1900f,
                  Frame = "2E7D4A", Accent = "E8C24A", Where = "forest, riding up - UC des Cedres",
                  Lines = new[] { "The forest hides the gradient. It's ten percent.", "Keep it steady - the chalet is far.", "Cedars smell better than the city." } },
        new Npc { Name = "Theo", TargetHeight = 1.44f, Speed = 4.6f, Lane = -0.90f, Ascending = true, MeetDistance = 3600f,
                  Frame = "D8342C", Accent = "F2F4F7", Where = "forest, riding up - Team Mistral",
                  Lines = new[] { "Two hours up, twenty minutes down.", "Don't look at the numbers. Look at the trees.", "Pace yourself for the moon." } },
        new Npc { Name = "Manon", TargetHeight = 1.37f, Speed = 3.9f, Lane = -1.05f, Ascending = true, MeetDistance = 5200f,
                  Frame = "F28BB5", Accent = "1F4E5F", Where = "forest, riding up - VC Rose des Vents",
                  Lines = new[] { "Halfway to the chalet. Maybe.", "My legs say no. My heart says oui.", "Ride with me a while?" } },
        new Npc { Name = "Julien", TargetHeight = 1.46f, Speed = 5.4f, Lane = -1.62f, MeetDistance = 7100f,
                  Frame = "2A2C31", Accent = "F07A22", Where = "upper forest, riding DOWN - Cycles Garrigue",
                  Lines = new[] { "Freezing up there - zip your gilet!", "The moonscape is something else.", "Keep turning those pedals!" } },
        new Npc { Name = "Amandine", TargetHeight = 1.35f, Speed = 3.7f, Lane = -0.90f, Ascending = true, MeetDistance = 9250f,
                  Frame = "7FD1C9", Accent = "1F4E5F", Where = "leaving the Chalet stop, riding up - CC Reynaud",
                  Lines = new[] { "The tart at the chalet was worth the stop.", "Six kilometres of stone from here.", "No more trees. No more shade." } },
        new Npc { Name = "Florian", TargetHeight = 1.44f, Speed = 4.2f, Lane = -1.00f, Ascending = true, MeetDistance = 9550f,
                  Frame = "5A646E", Accent = "E8C24A", Where = "just past the Chalet stop, riding up - VC Calcaire",
                  Lines = new[] { "Welcome to the moon.", "The tower is the finish. It never gets closer.", "Stay out of the wind if you can." } },
        new Npc { Name = "Lucie", TargetHeight = 1.36f, Speed = 3.8f, Lane = -0.95f, Ascending = true, MeetDistance = 11300f,
                  Frame = "F4F2EE", Accent = "C8102E", Where = "limestone moonscape, riding up - Club Lune Blanche",
                  Lines = new[] { "White stone everywhere - like riding on the moon.", "Four kilometres. I can do four.", "Don't stop, you'll never start again." } },
        new Npc { Name = "Remi", TargetHeight = 1.45f, Speed = 4.3f, Lane = -0.90f, Ascending = true, MeetDistance = 12600f,
                  Frame = "E0A82E", Accent = "7A1F2E", Where = "moonscape, riding up - Ocre Velo",
                  Lines = new[] { "The mistral wants us to turn back.", "See the tower? Red and white. Aim for it.", "Allez! Nearly there!" } },
        new Npc { Name = "Oceane", TargetHeight = 1.37f, Speed = 5.6f, Lane = -1.58f, MeetDistance = 13400f,
                  Frame = "2A7FD4", Accent = "F2F4F7", Where = "moonscape, riding DOWN - VC Azur",
                  Lines = new[] { "The view from the top - incroyable!", "Last kilometre is the steepest. Sorry!", "Bravo, keep going!" } },
        new Npc { Name = "Quentin", TargetHeight = 1.45f, Speed = 4.0f, Lane = -1.00f, Ascending = true, MeetDistance = 14300f,
                  Frame = "17897E", Accent = "F2D81B", Where = "last kilometre, riding up - Geant Cyclisme",
                  Lines = new[] { "One more bend. Then the sign.", "Read the names painted on the road - one is yours.", "Photo at the summit sign, yes?" } },
        new Npc { Name = "Maelle", TargetHeight = 1.36f, Speed = 5.0f, Lane = -1.62f, MeetDistance = 15250f,
                  Frame = "C8102E", Accent = "F4F2EE", Where = "summit, starting the descent - Sommet Club",
                  Lines = new[] { "Made it! Crepes are by the tower.", "Now the fun part - downhill!", "Welcome to the Giant." } },
    };

    /// <summary>Every Taka rider this roster owns: the thirty, then the moving cyclists.</summary>
    static IEnumerable<Npc> AllRiders => Roster.Concat(MovingCyclists);

    /// <summary>Names of the moving-cyclist layer, for harnesses (TakaNpcPlaymodeCapture).</summary>
    public static string[] MovingCyclistNames => MovingCyclists.Select(n => n.Name).ToArray();

    /// <summary>
    /// Bike materials to repaint. Brushed_Metal and Road_Tyre are deliberately absent: spokes,
    /// chain and tyres stay metal and rubber on anyone's bicycle.
    /// </summary>
    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    [MenuItem("MapleRide/NPCs/Stage Taka Roster")]
    public static void AddAllToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Find-or-create, never create-blindly: a second "Taka NPCs" root would hide the
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
        Debug.Log($"[taka] staged {Roster.Length} riders on segment '{SegmentId}'.");
    }

    /// <summary>
    /// Stages ONLY the moving-cyclist layer (claude-taka2), leaving the thirty riders and their
    /// material assets untouched (same reasoning as FujiNpcRoster.AddMovingCyclistsToScene).
    /// Idempotent (exact-name prune). Also bakes each new rider's greeting portrait.
    /// Menu: MapleRide/NPCs/Stage Taka Moving Cyclists
    /// </summary>
    [MenuItem("MapleRide/NPCs/Stage Taka Moving Cyclists")]
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
                Debug.LogWarning($"[taka] {def.Name}: not in any KuroRiders manifest - run " +
                                 "design_assets/3d/kuro/taka_npc_liveries.py first.");
            var go = Stage(def, root.transform, route);
            if (go == null) continue;
            staged++;
            var cyc = go.GetComponent<NPCCyclist>();
            Debug.Log($"[taka] moving {def.Name}: {def.MeetDistance:F0} m {(def.Ascending ? "UP" : "DOWN")}, " +
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
        Debug.Log($"[taka] staged {staged}/{MovingCyclists.Length} moving cyclists, {baked} portraits; " +
                  $"'{ParentName}' now has {root.transform.childCount} children.");
    }

    /// <summary>
    /// Idempotent runtime helpers: one TrafficAvoidance ON the "Taka NPCs" root with its rider
    /// list written explicitly, and a TakaRiderLod on every moving cyclist.
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
            if (moving.Contains(c.gameObject.name) && c.GetComponent<TakaRiderLod>() == null)
            { c.gameObject.AddComponent<TakaRiderLod>(); lods++; }
        Debug.Log($"[taka] traffic avoidance over {riders.Length} riders; TakaRiderLod added to {lods}.");
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
                Debug.Log($"[taka] {def.Name}: {def.MeetDistance:F0} m, scale {RigScaleFor(def):F3}, " +
                          $"{StagedSpeed(def.Speed):F2} m/s ({StagedSpeed(def.Speed) * 3.6f:F1} kph), " +
                          $"lane {def.Lane:F2}, pos {go.transform.position:F1}");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Re-writes only the riding PACE onto the Taka riders already staged in the scene.
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
    [MenuItem("MapleRide/NPCs/Apply Taka Pace To Scene")]
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
            Debug.Log($"[taka] {def.Name}: pace {before:F2} -> {cyc.speed:F2} m/s " +
                      $"({cyc.speed * 3.6f:F1} kph)");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[taka] pace applied to {applied} rider(s) at scale {PaceScale:F2}" +
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
        if (rigAsset == null) { Debug.LogWarning($"[taka] missing rig {def.Rig ?? RigPath}"); return null; }
        if (bikeAsset == null) { Debug.LogWarning($"[taka] missing bike {BikePath}"); return null; }

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
        cyclist.reverse = !def.Ascending;   // moving cyclists may ride UP (claude-taka2)
        // The Taka ascent, NOT the pass. Set before RebuildFromGraph, which reads it.
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
            Debug.LogWarning($"[taka] {def.Name}: no '{SegmentId}' segment in the route graph - " +
                             "falling back to the resampled Sakura road. Bake the Taka " +
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
            Debug.LogWarning($"[taka] {def.Name}: no kit texture at {kitPath} - run " +
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
            Debug.LogWarning($"[taka] {name}: no 'SmileDecal' under the rig - she will greet " +
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
            Debug.LogWarning($"[taka] '{m.shader.name}' exposes no known base-colour texture slot.");
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

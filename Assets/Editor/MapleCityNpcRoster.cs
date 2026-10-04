using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stages the thirty-rider NPC cast for MAPLE CITY - the 5 km closed crit loop described in
/// <c>improve/region_designs/maple_city.md</c> section 5.
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
/// the city cast is built the same way the pass cast is.
///
/// Riders are told apart by TEXTURE, never by material tinting. Her whole body - skin, hair,
/// helmet, jersey, shorts, gloves, shoes - is one 2048x2048 atlas on a single 'Material_0' slot,
/// so there is no per-part slot to tint, and a baseColorFactor tint would recolour each rider's
/// FACE along with her jersey. Instead assets/3d/kuro/npc_palette_variants.py recolours the
/// saturated kit pixels - leaving skin and outlines byte-for-byte identical - and writes
/// Assets/Kuro/NPC/Textures/CoralKit_*.png, keyed by the same names used below. Each rider gets
/// a cloned material pointing at hers.
///
/// WHY THIRTY AND NOT ELEVEN. Maple City is the game's social hub: the design calls for a
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
/// Menu: MapleRide/NPCs/Stage Maple City Roster
/// </summary>
public static class MapleCityNpcRoster
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
    /// The Maple City loop in the baked RouteGraph. Sakura's roster uses "pass"; getting this
    /// wrong does not error - it silently stages thirty city riders on the mountain pass, on top
    /// of the eleven already there.
    /// </summary>
    const string SegmentId = "maplecity";

    /// <summary>Scene parent for this region's riders, kept separate from the pass's "NPCs" so a
    /// region can be toggled, moved or deleted as a unit.</summary>
    const string ParentName = "Maple City NPCs";

    /// <summary>Object-name prefix. Pruning, pace re-application and any future tooling all match
    /// on the EXACT string <c>NamePrefix + rider name</c>, which is what makes re-running this
    /// menu item converge instead of accumulating duplicates.</summary>
    const string NamePrefix = "Maple City NPC ";

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
    /// The authored 3.6 - 5.8 m/s numbers are honest city-cyclist speeds (13 - 21 kph), but they
    /// read as sped-up footage in this world: the riders are chibi (~1.4 m), so a metre of road
    /// is a much larger fraction of the frame than it would be at human scale, and their legs -
    /// whose cadence KuroBikeRig derives from distance travelled - spin to match. The city makes
    /// this worse rather than better: the loop is flanked by buildings, trams and street
    /// furniture at close range, so there is far more near-field parallax than on the open pass
    /// and any given speed reads faster still. Scaling the whole roster keeps the authored
    /// per-rider VARIATION (Sota and Kaito are still the quickest, Sosuke and Mao still the
    /// slowest) while bringing the group down to a believable social pace.
    ///
    /// 0.55 is the value verified on the pass roster after the player flagged its NPCs as sped
    /// up. Maple City goes slightly lower, to 0.48, for the near-field-parallax reason above:
    /// the same ground speed that reads as a social pace on an open mountain road reads faster
    /// between two rows of six-storey facades with tram poles and street furniture flicking past
    /// at 6 m. That gives 1.73 - 2.78 m/s, i.e. 6.2 - 10.0 kph apparent - a bunch soft-pedalling
    /// through town, which is what a city circuit crowd should look like. PROVISIONAL: tune
    /// here, then re-run <c>MapleRide/NPCs/Apply Maple City Pace To Scene</c> - the staged
    /// riders carry a SERIALIZED speed, so changing this constant alone does nothing to the
    /// saved scene.
    /// </summary>
    public const float PaceScale = 0.48f;

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
        /// why the city cast shares one silhouette, one face and one hair mass. The quartet
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
    }

    // Spread 60 m -> 4845 m around the 5.0 km closed loop, so the player meets roughly one rider
    // every 165 m rather than riding through a crowd and then an empty city. The spacing is
    // deliberately JITTERED rather than an exact 165 m step: a perfectly regular interval on a
    // closed loop is audible as a rhythm after two laps.
    //
    // PROVISIONAL, all of it. Distances are metres along the "maplecity" segment and are only
    // meaningful while that segment is ~5 km; heights, speeds, lanes and liveries are art
    // direction and are expected to move.
    //
    // Every rider is on the oncoming side (negative lane) with reverse = true. An NPC travelling
    // the SAME way at a similar speed keeps the player permanently behind them, outside the
    // greeting view cone, and would never greet at all. That is not a preference - it is the
    // reason the pass roster is oncoming too.
    //
    // Speeds track personality: Sota (club sprinter) and Kaito (ambitious junior) are fastest,
    // Sosuke (retiree on the canal path) and Mao (tea-house server) slowest, with the working
    // riders - couriers, delivery, market rounds - purposeful in between.
    //
    // Kit colours live in npc_palette_variants.py, keyed by these same names. The Frame/Accent
    // hexes below are the BIKE livery and are matched to each rider's kit, not to each other.
    static readonly Npc[] Roster =
    {
        new Npc { Name = "Rin", TargetHeight = 1.38f, Speed = 5.4f, Lane = -1.75f, MeetDistance = 60f,
                  Frame = "FF7A18", Accent = "2B2F33",
                  Lines = new[] { "Left! Package run, mind your line.",
                                  "See you at the plaza, I'm always circling.",
                                  "Three drops left and the light's going.",
                                  "Fastest way east is the tram lane - don't tell anyone." } },

        new Npc { Name = "Kenji", TargetHeight = 1.44f, Speed = 4.0f, Lane = -1.90f, MeetDistance = 215f,
                  Frame = "E8D9B5", Accent = "6B4A2F",
                  Lines = new[] { "Pop into the café after your laps!",
                                  "Fresh roast on when you finish.",
                                  "First cup's on me - you've earned it.",
                                  "Milk run for the shop, then I'm home." } },

        new Npc { Name = "Mei", TargetHeight = 1.33f, Speed = 4.2f, Lane = -1.65f, MeetDistance = 395f,
                  Frame = "9FE8C4", Accent = "F2A0C8",
                  Lines = new[] { "This light's perfect for drawing.",
                                  "Hold that line, you'd sketch beautifully.",
                                  "I fill a whole page on this corner.",
                                  "The tram windows catch the sunset - look!" } },

        new Npc { Name = "Taro", TargetHeight = 1.46f, Speed = 4.4f, Lane = -1.85f, MeetDistance = 545f,
                  Frame = "1F3A6E", Accent = "FFC24D",
                  Lines = new[] { "Mind the rails on the bend!",
                                  "Right on time, same as my tram.",
                                  "Cross the tracks square or not at all.",
                                  "Six minutes till the next service - room to move." } },

        new Npc { Name = "Yuna", TargetHeight = 1.31f, Speed = 5.0f, Lane = -1.60f, MeetDistance = 730f,
                  Frame = "FF2E8A", Accent = "18C8D6",
                  Lines = new[] { "Nice rig! Skid contest at the bridge?",
                                  "The boulevard's ours tonight.",
                                  "No brakes, no problem - watch this.",
                                  "Deep sections sound better, that's just facts." } },

        new Npc { Name = "Hiro", TargetHeight = 1.42f, Speed = 4.1f, Lane = -1.80f, MeetDistance = 880f,
                  Frame = "6E767E", Accent = "B03A3A",
                  Lines = new[] { "Home before dark, that's the plan.",
                                  "One more lap won't hurt, right?",
                                  "The folder fits under my desk all day.",
                                  "Best ten minutes I get - this stretch." } },

        new Npc { Name = "Akihiro", TargetHeight = 1.41f, Speed = 4.9f, Lane = -1.88f, MeetDistance = 965f,
                  Frame = "3A3C40", Accent = "F07A22",
                  Rig = "Assets/Kuro/NPC/KuroNPC_Akihiro_Rigged.glb",
                  Lines = new[] { "Graphite and burnt orange - hard to lose me in traffic.",
                                  "Two more laps and the lights are all mine.",
                                  "Hold the inside line, I'll come round you.",
                                  "City's better from a saddle. Always was." } },

        new Npc { Name = "Aki", TargetHeight = 1.35f, Speed = 4.3f, Lane = -1.70f, MeetDistance = 1055f,
                  Frame = "F5C518", Accent = "3E8C4A",
                  Lines = new[] { "Ginkgo's turning gold this week.",
                                  "A bloom for the winner!",
                                  "Basket's full - careful passing me.",
                                  "Shop smells like the whole season in there." } },

        new Npc { Name = "Sota", TargetHeight = 1.45f, Speed = 5.8f, Lane = -1.95f, MeetDistance = 1200f,
                  Frame = "C41E32", Accent = "1A1C1F",
                  Lines = new[] { "Meet you at the sprint banner?",
                                  "Out of the last corner — full gas!",
                                  "Wind's behind us on the boulevard, use it.",
                                  "Lead me out and I'll take the lamppost." } },

        new Npc { Name = "Sosuke", TargetHeight = 1.48f, Speed = 3.6f, Lane = -1.70f, MeetDistance = 1390f,
                  Frame = "B9A8E0", Accent = "5A7FA8",
                  Lines = new[] { "No rush on the canal, friend.",
                                  "I've watched this river for forty years.",
                                  "The city changes; the water doesn't.",
                                  "Sit up a while - you'll see more." } },

        new Npc { Name = "Daisuke", TargetHeight = 1.36f, Speed = 5.1f, Lane = -1.75f, MeetDistance = 1540f,
                  Frame = "1F9AA8", Accent = "FFD24D",
                  Lines = new[] { "First crit tonight — any tips?",
                                  "Did I hold your wheel okay?",
                                  "Coach says stay off the front. I never do.",
                                  "How do you take that turn so smooth?" } },

        new Npc { Name = "Keisuke", TargetHeight = 1.41f, Speed = 4.5f, Lane = -1.85f, MeetDistance = 1720f,
                  Frame = "5A6066", Accent = "C08A3E",
                  Lines = new[] { "Chain sounds sweet — you tuned it?",
                                  "Bring it by the shop anytime.",
                                  "Half a turn on that barrel and it's perfect.",
                                  "Shop's behind the tram depot, you can't miss it." } },

        new Npc { Name = "Sakura", TargetHeight = 1.34f, Speed = 4.9f, Lane = -1.65f, MeetDistance = 1870f,
                  Frame = "8A5A33", Accent = "F0E2C8",
                  Lines = new[] { "Two laps, then my shift starts!",
                                  "Iced or hot at the finish?",
                                  "I clock in smelling like coffee either way.",
                                  "Save me a seat by the window!" } },

        new Npc { Name = "Ryo", TargetHeight = 1.43f, Speed = 5.3f, Lane = -1.90f, MeetDistance = 2050f,
                  Frame = "24262B", Accent = "FF3B30",
                  Lines = new[] { "City's best after dark.",
                                  "Follow my light through the alleys.",
                                  "Nobody's out at this hour but us.",
                                  "Red light behind me - just keep it in sight." } },

        new Npc { Name = "Hana", TargetHeight = 1.37f, Speed = 3.9f, Lane = -1.70f, MeetDistance = 2200f,
                  Frame = "2B4A8C", Accent = "E8D5A8",
                  Lines = new[] { "Careful, the cobbles are old.",
                                  "The shrine's just down this lane.",
                                  "I've lived on this street my whole life.",
                                  "Slow through here - people still walk it." } },

        new Npc { Name = "Kaito", TargetHeight = 1.39f, Speed = 5.7f, Lane = -1.95f, MeetDistance = 2380f,
                  Frame = "1E7BFF", Accent = "F5D020",
                  Lines = new[] { "One day I'll turn pro, watch.",
                                  "Can you hold this pace? I can't yet.",
                                  "Every lap is training if you mean it.",
                                  "Going again from the bridge - coming?" } },

        new Npc { Name = "Yui", TargetHeight = 1.32f, Speed = 3.9f, Lane = -1.60f, MeetDistance = 2530f,
                  Frame = "8A6A3E", Accent = "6E7A4A",
                  Lines = new[] { "Quiet route through the back streets?",
                                  "New arrivals in the shop, come by.",
                                  "No traffic down the old lane at all.",
                                  "I ride slowly so I can read the signs." } },

        new Npc { Name = "Jun", TargetHeight = 1.44f, Speed = 5.5f, Lane = -1.85f, MeetDistance = 2715f,
                  Frame = "2FA84F", Accent = "F2F5F7",
                  Lines = new[] { "Beat the 6:15 tram to the bridge?",
                                  "Rails are slick tonight — careful.",
                                  "It always wins the straight. Always.",
                                  "One gear, one line, no excuses." } },

        new Npc { Name = "Miku", TargetHeight = 1.30f, Speed = 4.6f, Lane = -1.65f, MeetDistance = 2860f,
                  Frame = "FFB3D9", Accent = "FFFFFF",
                  Lines = new[] { "Cutest bike in the city, right?!",
                                  "Wave to the crowd on the boulevard!",
                                  "I added more stickers. There's always room.",
                                  "Smile - someone's always watching the plaza!" } },

        new Npc { Name = "Goro", TargetHeight = 1.50f, Speed = 4.0f, Lane = -2.00f, MeetDistance = 3040f,
                  Frame = "2E5A73", Accent = "F2C230",
                  Lines = new[] { "Market's packing up — ride safe.",
                                  "Fresh catch at dawn, come early.",
                                  "Loaded trike - give me the whole lane.",
                                  "Up before the trams, home before lunch." } },

        new Npc { Name = "Airi", TargetHeight = 1.38f, Speed = 4.4f, Lane = -1.75f, MeetDistance = 3190f,
                  Frame = "6B7279", Accent = "EDF1F4",
                  Lines = new[] { "Look up — the rooftops are art.",
                                  "The Sky Terrace has the best view.",
                                  "Straight lines everywhere, then this curve.",
                                  "I sketch facades instead of watching traffic." } },

        new Npc { Name = "Shin", TargetHeight = 1.47f, Speed = 4.7f, Lane = -1.90f, MeetDistance = 3370f,
                  Frame = "C25A3A", Accent = "3E5A7A",
                  Lines = new[] { "We used to race this loop for real.",
                                  "Smooth pedaling — that never goes.",
                                  "Wool jersey, steel frame, same as always.",
                                  "The legs go. The line stays." } },

        new Npc { Name = "Kana", TargetHeight = 1.36f, Speed = 5.2f, Lane = -1.80f, MeetDistance = 3520f,
                  Frame = "18A3A8", Accent = "F5F7F8",
                  Lines = new[] { "Five drops before dinner, gotta fly.",
                                  "You're quick — you should ride for us.",
                                  "Battery's at forty, that's two more runs.",
                                  "Dinner rush starts now - I'm gone!" } },

        new Npc { Name = "Riku", TargetHeight = 1.31f, Speed = 4.8f, Lane = -1.60f, MeetDistance = 3700f,
                  Frame = "8FD41E", Accent = "7A3FB5",
                  Lines = new[] { "Bunnyhop the crosswalk with me!",
                                  "Plaza's the spot after dark.",
                                  "Flat bar beats drops for curbs, fight me.",
                                  "Landed that one clean - did you see?" } },

        new Npc { Name = "Mao", TargetHeight = 1.33f, Speed = 3.7f, Lane = -1.70f, MeetDistance = 3850f,
                  Frame = "7FB58A", Accent = "E8A8B0",
                  Lines = new[] { "Stop for tea when you finish?",
                                  "The canal's prettiest at dusk.",
                                  "No hurry - the pot's always warm.",
                                  "Sit by the water a moment, then ride." } },

        new Npc { Name = "Toru", TargetHeight = 1.45f, Speed = 5.0f, Lane = -1.95f, MeetDistance = 4030f,
                  Frame = "2A3E6B", Accent = "E8EBEE",
                  Lines = new[] { "Sit on my wheel, I'll pull you.",
                                  "Save it for the sprint — I've got the wind.",
                                  "Somebody has to do the work up front.",
                                  "Two laps of shelter, then it's yours." } },

        new Npc { Name = "Sana", TargetHeight = 1.34f, Speed = 4.2f, Lane = -1.65f, MeetDistance = 4180f,
                  Frame = "3A3F45", Accent = "E8A33D",
                  Lines = new[] { "Hold still — the light's gold!",
                                  "This bridge shot never gets old.",
                                  "Twenty minutes of this light a day. Twenty.",
                                  "Ride past again - I nearly had it." } },

        new Npc { Name = "Kohei", TargetHeight = 1.42f, Speed = 4.5f, Lane = -1.85f, MeetDistance = 4360f,
                  Frame = "D42B2B", Accent = "F7F7F7",
                  Lines = new[] { "Cold drink at the plaza machine?",
                                  "I know every backstreet in town.",
                                  "Twelve machines left before the depot closes.",
                                  "Take the alley - it cuts a whole block." } },

        new Npc { Name = "Nozomi", TargetHeight = 1.40f, Speed = 5.6f, Lane = -1.90f, MeetDistance = 4510f,
                  Frame = "7B2FD6", Accent = "B8E62E",
                  Lines = new[] { "Dive the inside line with me!",
                                  "Elbows in through the plaza turn.",
                                  "Corners are free speed if you trust them.",
                                  "Brake early, exit fast - that's the whole trick." } },

        new Npc { Name = "Wataru", TargetHeight = 1.46f, Speed = 4.1f, Lane = -1.80f, MeetDistance = 4695f,
                  Frame = "E8901E", Accent = "8A4A1E",
                  Lines = new[] { "Lanterns go up at dusk — stay for it.",
                                  "Follow the amber all the way home.",
                                  "I light this street myself, every evening.",
                                  "Warm light makes a city feel like a room." } },

        new Npc { Name = "Chika", TargetHeight = 1.32f, Speed = 4.7f, Lane = -1.70f, MeetDistance = 4845f,
                  Frame = "1FA8B5", Accent = "D6A85A",
                  Lines = new[] { "Racing you to the bridge — go!",
                                  "One more lap before curfew?",
                                  "Bag's heavy but I'm still faster than you.",
                                  "Straight home after this. Probably." } },
    };

    /// <summary>
    /// Bike materials to repaint. Brushed_Metal and Road_Tyre are deliberately absent: spokes,
    /// chain and tyres stay metal and rubber on anyone's bicycle.
    /// </summary>
    static readonly string[] FrameMaterials = NpcCanonicalConformance.FrameMaterialNames;
    static readonly string[] AccentMaterials = NpcCanonicalConformance.AccentMaterialNames;

    [MenuItem("MapleRide/NPCs/Stage Maple City Roster")]
    public static void AddAllToScene()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Find-or-create, never create-blindly: a second "Maple City NPCs" root would hide the
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
        Debug.Log($"[city] staged {Roster.Length} riders on segment '{SegmentId}'.");
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
                Debug.Log($"[city] {def.Name}: {def.MeetDistance:F0} m, scale {RigScaleFor(def):F3}, " +
                          $"{StagedSpeed(def.Speed):F2} m/s ({StagedSpeed(def.Speed) * 3.6f:F1} kph), " +
                          $"lane {def.Lane:F2}, pos {go.transform.position:F1}");
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Re-writes only the riding PACE onto the city riders already staged in the scene.
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
    [MenuItem("MapleRide/NPCs/Apply Maple City Pace To Scene")]
    public static void ApplyPaceToScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int applied = 0, missing = 0;
        var all = UnityEngine.Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None);
        foreach (var def in Roster)
        {
            // EXACT name, and the exact name Stage() uses.
            string objName = NamePrefix + def.Name;
            var cyc = all.FirstOrDefault(c => c.gameObject.name == objName);
            if (cyc == null) { missing++; continue; }

            float before = cyc.speed;
            cyc.speed = StagedSpeed(def.Speed);
            EditorUtility.SetDirty(cyc);
            applied++;
            Debug.Log($"[city] {def.Name}: pace {before:F2} -> {cyc.speed:F2} m/s " +
                      $"({cyc.speed * 3.6f:F1} kph)");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[city] pace applied to {applied} rider(s) at scale {PaceScale:F2}" +
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
    static void PurgeOldMaterials()
    {
        var owned = new HashSet<string>(Roster.Select(r => r.Name));
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
        if (rigAsset == null) { Debug.LogWarning($"[city] missing rig {def.Rig ?? RigPath}"); return null; }
        if (bikeAsset == null) { Debug.LogWarning($"[city] missing bike {BikePath}"); return null; }

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
        // drivetrain and leave the player's wheels driven by a city NPC's speed.
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
        cyclist.reverse = true;
        // The Maple City loop, NOT the pass. Set before RebuildFromGraph, which reads it.
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
            Debug.LogWarning($"[city] {def.Name}: no '{SegmentId}' segment in the route graph - " +
                             "falling back to the resampled Sakura road. Bake the Maple City " +
                             "route and re-run this menu item.");
            cyclist.route = route.Reverse().ToArray();
            cyclist.routeIncludesOffsets = false;
        }

        // Placed by DISTANCE, never by a fraction of the route. A fraction silently relocates
        // every rider the moment the loop changes length - which is exactly how Coral once ended
        // up 277 m from where she was authored when the pass was extended.
        int back = Mathf.RoundToInt(def.MeetDistance / NPCCyclist.Spacing);
        cyclist.progress = Mathf.Clamp(cyclist.route.Length - 1 - back, 0, cyclist.route.Length - 1);
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
        // would draw from one shared pool and the whole city would speak in one voice - and in
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
    /// is shared BY REFERENCE - retexturing it in place would repaint all thirty city riders,
    /// all eleven pass riders and Coral herself. Always clone.
    /// </summary>
    static void ApplyKit(Npc def, GameObject body)
    {
        if (def.Rig != null) return;  // his kit is painted into his own atlas - see Npc.Rig.

        // Kuro-based riders (KuroRiderBodies) wear their KuroKit atlas; anyone else keeps Coral's.
        var kitPath = KuroRiderBodies.Has(def.Name) ? KuroRiderBodies.KitPath(def.Name)
                                                    : $"{KitDir}/CoralKit_{def.Name}.png";
        var kit = AssetDatabase.LoadAssetAtPath<Texture2D>(kitPath);
        if (kit == null)
        {
            Debug.LogWarning($"[city] {def.Name}: no kit texture at {kitPath} - run " +
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
            Debug.LogWarning($"[city] {name}: no 'SmileDecal' under the rig - she will greet " +
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
            Debug.LogWarning($"[city] '{m.shader.name}' exposes no known base-colour texture slot.");
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

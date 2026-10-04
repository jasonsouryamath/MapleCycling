using UnityEngine;

/// <summary>One NPC who races. Data only; <see cref="RaceNpc"/> is the scene side.</summary>
public sealed class RacerDef
{
    public string Id;
    public string Name;
    /// <summary>Exact name of the EXISTING rider in the saved scene this racer is. Roster riders
    /// (NPCCyclist-driven) are taken over in place; pooled traffic riders (Minato/Shiosai, which
    /// the traffic director recycles) are cloned instead, see <see cref="CloneSource"/>.</summary>
    public string SceneObject;
    public bool CloneSource;
    public string CourseId;
    /// <summary>Course arc metres where the racer's bike is parked. The race starts just ahead.</summary>
    public float SpotM;
    public int Level;
    public RaceType Type;
    public RacerStance Stance;
    public Color Accent;
    public string Challenge;
    /// <summary>Said when the NPC beats Kuro.</summary>
    public string WinQuip;
    /// <summary>Said when Kuro beats the NPC.</summary>
    public string LossQuip;

    public bool ClimbSpecialist => Type == RaceType.Climb;
    public float LengthM => RaceMath.LengthM(Type);
}

/// <summary>
/// The thirteen racers of the brief's level spread (1, 3, 7, 12, 18, 24, 31, 38, 45, 48, 52, 56,
/// 60), plus the regional rivals of regions built after it (<see cref="RegionalExtras"/>, e.g.
/// Nagisa Bay's Kaimana, Lv 28). Every one of them is an EXISTING rider of their region, already staged with their own
/// Kuro-based body, hair, kit and greeting portrait, so nobody new had to be modelled.
///
/// Placement follows the brief: the low levels do Standard/Sprint in Maple City (the hub, where
/// the campaign starts), the KOM riders sit at the foot of real climbs, and Lv 52+ are Elite on
/// flat-into-climb stretches. Every spot was chosen from the baked route grades (100 m windows):
///   Maple City crit   flat 0-1,750 m, climb 1,800-2,450 m at +4-8 %
///   Sakura circuit    Sakura climb 100-450 m at +6-10 %; lakeshore flat 1,980-2,680 m, then +4-6 %
///   Minato crossing   flat causeway 3-7 km; flat 13.1-13.4 km into +3-5 %
///   Taka high road    steady +7-8 % from 1.8 km
///   Shiosai coast     flat (-0.5 %) 5.2-5.8 km
///   Nagisa Bay loop   hill wall from ~5.3 km, a steady +9.5 % from 5.6 km (+9.2 % over 5.49-5.79 km)
/// PROVISIONAL: which rider sits where is a content choice, easy to reshuffle here.
/// </summary>
public static class RaceRoster
{
    public static readonly RacerDef[] All =
    {
        new RacerDef {
            Id = "hana", Name = "Hana", SceneObject = "Maple City NPC Hana",
            CourseId = "maple_city_crit", SpotM = 160f, Level = 1, Type = RaceType.Standard,
            Stance = RacerStance.ArmsCrossed, Accent = RaceMath.Hex(0x4f8cff),
            Challenge = "First race? Don't worry, I'll go easy. Probably.",
            WinQuip = "Ha! Keep pedalling, you'll get there.",
            LossQuip = "Whoa, you're quick! Rematch soon, okay?" },
        new RacerDef {
            Id = "kenji", Name = "Kenji", SceneObject = "Maple City NPC Kenji",
            CourseId = "maple_city_crit", SpotM = 820f, Level = 3, Type = RaceType.Sprint,
            Stance = RacerStance.HandsOnHips, Accent = RaceMath.Hex(0xff6b3d),
            Challenge = "Two hundred metres. Blink and it's over.",
            WinQuip = "Too slow off the mark, friend!",
            LossQuip = "Tch. My legs weren't warm yet." },
        new RacerDef {
            Id = "mei", Name = "Mei", SceneObject = "Maple City NPC Mei",
            CourseId = "maple_city_crit", SpotM = 1020f, Level = 7, Type = RaceType.Standard,
            Stance = RacerStance.Relaxed, Accent = RaceMath.Hex(0xf06fb4),
            Challenge = "I race everyone who stops here. Your turn!",
            WinQuip = "Sit on my wheel next time, you'll learn something.",
            LossQuip = "Okay, okay, you earned that one." },
        new RacerDef {
            Id = "taro", Name = "Taro", SceneObject = "Maple City NPC Taro",
            CourseId = "maple_city_crit", SpotM = 1240f, Level = 12, Type = RaceType.TimeTrial,
            Stance = RacerStance.ArmsCrossed, Accent = RaceMath.Hex(0x9d6bff),
            Challenge = "Just you and the clock. And my ghost.",
            WinQuip = "Pacing, Kuro. It's all about pacing.",
            LossQuip = "New personal best for you. Annoyingly." },
        new RacerDef {
            Id = "sota", Name = "Sota", SceneObject = "Maple City NPC Sota",
            CourseId = "maple_city_crit", SpotM = 1450f, Level = 52, Type = RaceType.Elite,
            Stance = RacerStance.HandsOnHips, Accent = RaceMath.Hex(0xffc23d),
            Challenge = "Flat, then the wall. Nobody in this city beats me up it.",
            WinQuip = "Come back when that insignia has wings.",
            LossQuip = "...Impossible. Who trained you?" },
        new RacerDef {
            Id = "hiro", Name = "Hiro", SceneObject = "Maple City NPC Hiro",
            CourseId = "maple_city_crit", SpotM = 1830f, Level = 18, Type = RaceType.Climb,
            Stance = RacerStance.Relaxed, Accent = RaceMath.Hex(0x3ddc74),
            Challenge = "See that ramp? King of the Mountain, first to the top.",
            WinQuip = "Hills don't care how fast you are on the flat.",
            LossQuip = "You climb like you've got wings!" },
        new RacerDef {
            Id = "ren", Name = "Ren", SceneObject = "Sakura NPC Ren",
            CourseId = "sakura_circuit", SpotM = 110f, Level = 38, Type = RaceType.Climb,
            Stance = RacerStance.ArmsCrossed, Accent = RaceMath.Hex(0x2fc98f),
            Challenge = "Ten percent under the blossoms. Care to suffer with me?",
            WinQuip = "The pass decides who's strong. Not me.",
            LossQuip = "The petals were rooting for you today." },
        new RacerDef {
            Id = "mika", Name = "Mika", SceneObject = "Sakura NPC Mika",
            CourseId = "sakura_circuit", SpotM = 2027f, Level = 24, Type = RaceType.Sprint,
            Stance = RacerStance.HandsOnHips, Accent = RaceMath.Hex(0xff4f7b),
            Challenge = "Flat road, lake view, flat-out sprint. Go?",
            WinQuip = "You blinked. I didn't.",
            LossQuip = "Nice jump! I didn't even see you coming." },
        new RacerDef {
            Id = "yuki", Name = "Yuki", SceneObject = "Sakura NPC Yuki",
            CourseId = "sakura_circuit", SpotM = 2377f, Level = 56, Type = RaceType.Elite,
            Stance = RacerStance.Relaxed, Accent = RaceMath.Hex(0xffd166),
            Challenge = "Lakeshore flat, then the climb home. I don't lose here.",
            WinQuip = "Quiet legs, loud results. Train more.",
            LossQuip = "Hm. The lake's never seen that before." },
        new RacerDef {
            Id = "marina", Name = "Marina", SceneObject = "Minato Rider 08 Marina", CloneSource = true,
            CourseId = "minato_crossing", SpotM = 4000f, Level = 31, Type = RaceType.TimeTrial,
            Stance = RacerStance.ArmsCrossed, Accent = RaceMath.Hex(0x6a7dff),
            Challenge = "Half a kilometre of causeway. Beat my ghost.",
            WinQuip = "The sea breeze was on my side. And skill.",
            LossQuip = "Faster than the tide. Impressive." },
        new RacerDef {
            Id = "kohaku", Name = "Kohaku", SceneObject = "Minato Rider 02 Kohaku", CloneSource = true,
            CourseId = "minato_crossing", SpotM = 7400f, Level = 60, Type = RaceType.Elite,
            Stance = RacerStance.HandsOnHips, Accent = RaceMath.Hex(0xffb42e),
            Challenge = "I am the Rising Sun of this coast. Are you sure?",
            WinQuip = "The horizon is far. Keep riding toward it.",
            LossQuip = "So the sun sets after all. Well ridden, Kuro." },
        new RacerDef {
            Id = "kazan", Name = "Kazan", SceneObject = "Taka NPC Kazan",
            CourseId = "taka_high_road", SpotM = 2000f, Level = 45, Type = RaceType.Climb,
            Stance = RacerStance.Relaxed, Accent = RaceMath.Hex(0x2bb673),
            Challenge = "The High Road only gets steeper. Climb with me.",
            WinQuip = "Mountains are patient. So am I.",
            LossQuip = "You're built for the high roads, Kuro." },
        new RacerDef {
            Id = "nami", Name = "Nami", SceneObject = "Shiosai Rider 16 Nami", CloneSource = true,
            CourseId = "shiosai_breeze", SpotM = 5300f, Level = 48, Type = RaceType.Standard,
            Stance = RacerStance.HandsOnHips, Accent = RaceMath.Hex(0x3fb8ff),
            Challenge = "Coast road, four hundred metres, winner buys shaved ice.",
            WinQuip = "Strawberry syrup, please. Your treat!",
            LossQuip = "Okay, okay, the shaved ice is on me." },
        new RacerDef {
            Id = "kaimana", Name = "Kaimana", SceneObject = "Nagisa NPC Kaimana",
            CourseId = "nagisa_bay_loop", SpotM = 5480f, Level = 28, Type = RaceType.Climb,
            Stance = RacerStance.ArmsCrossed, Accent = RaceMath.Hex(0xffb42e),
            Challenge = "Nine and a half percent to the palms. First to the top wears the Diamond Crest.",
            WinQuip = "The jungle keeps the heat in. You'll learn to love it.",
            LossQuip = "You took my hill... Enjoy the view, you earned it." },
    };

    /// <summary>Racers added after the brief's thirteen (one rival per newer region). They sit
    /// outside the brief's level spread on purpose; RaceTests checks the spread without them.</summary>
    public static readonly string[] RegionalExtras = { "kaimana" };

    public static RacerDef Find(string id)
    {
        foreach (var r in All) if (r.Id == id) return r;
        return null;
    }
}

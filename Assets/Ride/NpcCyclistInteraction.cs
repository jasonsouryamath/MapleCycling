using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Guarantees EVERY riding NPC cyclist, in every region, can greet Kuro with a name card and a line.
///
/// WHY. The card system hangs off <see cref="NpcGreeting"/>, and several spawn paths quietly drop it:
///  * NagisaCyclistBooster / NagisaMovingRiders clone ~140 extra riders and set greeting.enabled = false;
///  * ShuntaNpcLibrary.Sanitize disables every MonoBehaviour except LOD scripts, which includes NpcGreeting;
///  * clones are named "Nagisa Extra Cyclist 012" / "Shunta reused rider 3 (Rider_04)", so even an enabled
///    greeting would introduce them as "012" or "(Rider_04)" with a silhouette portrait.
/// This self-booting scanner runs in every scene, finds each live rider (a KuroBikeRig that is not the player,
/// not parked at a cafe, not a race-day RaceNpc which owns its own card), and makes sure it has an enabled
/// NpcGreeting with a real name and a baked portrait from Resources/NpcPortraits. Named roster riders keep
/// their identity. Rewrites nothing on disk; works for pooled and recycled riders and for riders spawned later.
/// </summary>
[DefaultExecutionOrder(60)]
public sealed class NpcCyclistInteraction : MonoBehaviour
{
    public static NpcCyclistInteraction Instance { get; private set; }
    public static int Covered { get; private set; }
    public static int Repaired { get; private set; }

    /// <summary>Portraits shipped in Resources/NpcPortraits (names only; textures load on demand).</summary>
    public static readonly string[] PortraitNames =
    {
        "Airi","Akane","Aki","Akihiro","Akira","Amandine","Andrin","Anja","Anouk","Aoi","Arata","Asahi","Asuka","Ayame","Baptiste","Beat",
        "Bianca","Camille","Chiara","Chiharu","Chika","Chiyo","Coral","Corinne","Curdin","Daichi","Daisuke","Dario","Eiji","Elias","Elodie",
        "Emi","Emile","Fabian","Florian","Flurina","Fubuki","Fumiko","Gaku","Genji","Genki","Gian","Gianna","Ginji","Giulia","Goro","Gou",
        "Hana","Haruka","Hayate","Hayato","Hibiki","Hinata","Hiro","Homare","Hotaru","Hyoga","Hyouma","Ibuki","Ingrid","Isamu","Isuzu","Itsuki",
        "Iwao","Jonas","Julia","Julien","Jun","Junnosuke","Junpei","Kaede","Kai","Kailani","Kaimana","Kaito","Kaiyo","Kana","Kanade","Kanata",
        "Kanoa","Kanon","Kaoru","Kasumi","Katsuo","Kazan","Kazuha","Kazuo","Keisuke","Kenji","Kensuke","Kenzan","Keoni","Kiyoshi","Kogane",
        "Kohaku","Kohei","Koji","Kozue","Ladina","Laura","Lea","Leilani","Leonie","Lorenzo","Luca","Lucie","Maelle","Mahina","Makoa","Manon",
        "Mao","Marco","Margaux","Marina","Masao","Masaru","Mathis","Matteo","Mei","Michiko","Michio","Michiru","Midori","Mika","Miku","Minami",
        "Minori","Minoru","Mio","Mirjam","Momiji","Momoka","Nadine","Nagi","Nalu","Nami","Nao","Naoya","Natsu","Natsuki","Nico","Nina","Noa",
        "Noah","Noboru","Noelani","Nozomi","Oceane","Osamu","Pascal","Pietro","Quentin","Rahel","Raiga","Raku","Rei","Reiji","Reika","Remi",
        "Ren","Reto","Riku","Rin","Rina","Ryo","Ryoko","Ryoma","Ryosuke","Ryusei","Sae","Sakura","Sana","Sara","Sayaka","Seiji","Selina","Sena",
        "Seraina","Shin","Shinji","Shinobu","Shion","Shione","Shiori","Shiro","Shirou","Shohei","Silvan","Sofia","Sora","Sosuke","Sota","Souma",
        "Sousuke","Subaru","Susumu","Suzu","Suzuha","Takane","Takumi","Tamaki","Taro","Tatsuhiro","Tatsuo","Tatsuya","Tenma","Tetsu","Theo",
        "Timo","Toma","Tommaso","Tomo","Toru","Touma","Tsubasa","Tsukasa","Tsumugi","Umi","Urs","Ursina","Valentina","Wataru","Yamato","Yoshiko",
        "Yui","Yuki","Yukito","Yuna","Yusuke","Yutaka",
    };
    static readonly HashSet<string> portraitSet = new HashSet<string>(PortraitNames, System.StringComparer.OrdinalIgnoreCase);

    const float ScanSeconds = 1.25f;
    const int PerPass = 24;                            // riders repaired per scan so a 400-rider region never hitches
    readonly HashSet<int> handled = new HashSet<int>();
    readonly Dictionary<string, int> nameUse = new Dictionary<string, int>();
    float nextScan;
    RideBootstrap boot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("NPC cyclist interaction"); DontDestroyOnLoad(go);
        Instance = go.AddComponent<NpcCyclistInteraction>();
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + ScanSeconds;
        if (boot == null) boot = FindFirstObjectByType<RideBootstrap>();
        Transform player = boot != null && boot.follower != null ? boot.follower.rider : null;

        int covered = 0, repaired = 0;
        var rigs = FindObjectsByType<KuroBikeRig>(FindObjectsSortMode.None);
        foreach (var rig in rigs)
        {
            if (rig == null || !IsNpcCyclist(rig, player)) continue;
            covered++;
            var root = RiderRoot(rig);
            int id = root.GetInstanceID();
            var greet = root.GetComponentInChildren<NpcGreeting>(true);
            bool ok = greet != null && greet.enabled && greet.useFaceCard && HasProperIdentity(greet);
            if (ok && handled.Contains(id)) { TuneCone(greet, root, player); continue; }
            if (repaired >= PerPass) continue;
            Repair(root, greet, id, player);
            repaired++; Repaired++;
        }
        Covered = covered;
    }

    static Transform RiderRoot(KuroBikeRig rig)
    {
        // The rider object is the one that carries NPCCyclist / NpcGreeting when it exists, otherwise the rig's own object.
        var cyc = rig.GetComponentInParent<NPCCyclist>();
        if (cyc != null) return cyc.transform;
        var g = rig.GetComponentInParent<NpcGreeting>();
        return g != null ? g.transform : rig.transform;
    }

    static bool IsNpcCyclist(KuroBikeRig rig, Transform player)
    {
        if (!rig.isActiveAndEnabled) return false;                      // pedestrians (Sanitize disables their rig), hidden pool slots
        var t = rig.transform;
        if (player != null && (t == player || t.IsChildOf(player) || player.IsChildOf(t))) return false;
        if (rig.GetComponentInParent<RaceNpc>() != null) return false;  // race roster owns its own challenge card
        if (rig.GetComponentInParent<HanakageRider>() != null) return false;   // scripted encounter
        var cyc = rig.GetComponentInParent<NPCCyclist>();
        if (cyc != null && !cyc.enabled) return false;                  // parked/stopped riders (cafe)
        for (var p = t; p != null; p = p.parent)
            if (p.name.IndexOf("Cafe", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }

    static bool HasProperIdentity(NpcGreeting g)
    {
        if (string.IsNullOrEmpty(g.riderName)) return false;
        return portraitSet.Contains(g.riderName);
    }

    void Repair(Transform root, NpcGreeting greet, int id, Transform player)
    {
        handled.Add(id);
        if (greet == null)
        {
            greet = root.gameObject.AddComponent<NpcGreeting>();
            greet.triggerDistance = 13f; greet.viewAngle = 150f; greet.visibleSeconds = 3.5f; greet.rearmSeconds = 6f;
            greet.smileRendererName = "SmileDecal"; greet.bubbleOffset = new Vector3(0f, 1.35f, 0f);
        }
        greet.enabled = true; greet.useFaceCard = true;
        if (!HasProperIdentity(greet))
        {
            // a stable, region-varied name per rider: hash of the object's path, de-duplicated among riders currently alive
            string key = Path(root);
            int h = Mathf.Abs(key.GetHashCode());
            string pick = null;
            for (int k = 0; k < PortraitNames.Length; k++)
            {
                string cand = PortraitNames[(h + k * 37) % PortraitNames.Length];
                if (cand == "Kuro") continue;
                if (!nameUse.TryGetValue(cand, out int used) || used < 1 + k / PortraitNames.Length) { pick = cand; break; }
            }
            pick = pick ?? PortraitNames[h % PortraitNames.Length];
            nameUse[pick] = nameUse.TryGetValue(pick, out int u) ? u + 1 : 1;
            greet.riderName = pick;
            greet.portrait = null;                                       // force a reload for the new name
        }
        greet.ResolveIdentity();
        TuneCone(greet, root, player);
    }

    /// <summary>Oncoming riders see Kuro in front of them (150-degree cone). Riders going the same way only ever have Kuro
    /// behind or beside them, so they get a wide cone and a shorter range - their card still shows their face.</summary>
    static void TuneCone(NpcGreeting g, Transform root, Transform player)
    {
        if (player == null) return;
        Vector3 f = root.forward; f.y = 0f; Vector3 pf = player.forward; pf.y = 0f;
        bool sameWay = f.sqrMagnitude > .001f && pf.sqrMagnitude > .001f && Vector3.Dot(f.normalized, pf.normalized) > .35f;
        g.viewAngle = sameWay ? 300f : 150f;
        g.triggerDistance = sameWay ? 9f : 13f;
    }

    static string Path(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (var p = t.parent; p != null && sb.Length < 120; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DOUBLES the cyclists on the Maple City loop (user, 2026-10-03: "double its NPCs, both on the road and on the bike").
///
/// The staged roster is 31 named, oncoming riders at ~150 m pitch (MapleCityNpcRoster). This clones each of them at run
/// time - exactly like NagisaCyclistBooster does for Nagisa Bay, so no scene re-stage or region re-export is needed:
///   * every clone is seated half a pitch (~75 m) behind its donor along the same lane, so the loop reads as constant
///     oncoming traffic instead of widely spaced singletons;
///   * clones keep the donor's route, lane, bike livery, LOD and shadow scripts; <see cref="NPCCyclist.Awake"/> rebuilds each
///     route from the baked graph on instantiation;
///   * the clone's pace is nudged so a donor and its clone drift apart / together slowly instead of travelling as a fixed pair;
///   * greeting + name card is NOT copied from the donor (a clone would repeat the donor's name); NpcCyclistInteraction gives
///     every clone its own name, portrait and an enabled NpcGreeting;
///   * <see cref="TrafficAvoidance"/> is refreshed so the clones also swerve around Kuro and each other.
/// Kill switch for before/after captures: env MR_MAPLE_EXTRA_RIDERS=0.
/// </summary>
public sealed class MapleCityCyclistBooster : MonoBehaviour
{
    public const string RootName = "Maple City NPCs";
    public const string GroupName = "Maple City Extra Cyclists";
    public const string EastGroupName = "Maple City East Cyclists";
    const string DonorPrefix = "Maple City NPC ";

    [Tooltip("Metres behind the donor a clone is seated. PROVISIONAL: half the staged ~150 m pitch.")]
    public float offsetM = 75f;
    public int perFrame = 3;

    static MapleCityCyclistBooster _instance;
    bool _spawning, _done;
    RegionDirector _regions;
    float _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_MAPLE_EXTRA_RIDERS") == "0") return;
        var go = new GameObject("~Maple City Cyclist Booster");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MapleCityCyclistBooster>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { _instance = null; }

    void Update()
    {
        if (_spawning || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        if (_regions == null) _regions = FindFirstObjectByType<RegionDirector>();
        if (_regions == null || _regions.currentRegionId != RegionCatalog.MapleCity) { _done = false; return; }
        if (_done) return;
        var root = GameObject.Find(RootName);
        if (root == null) return;
        if (root.transform.Find(GroupName) != null) { _done = true; return; }     // already doubled (scene kept alive)
        StartCoroutine(Spawn(root));
    }

    IEnumerator Spawn(GameObject root)
    {
        _spawning = true;
        var donors = new List<NPCCyclist>();
        foreach (var c in root.GetComponentsInChildren<NPCCyclist>(true))
            if (c.transform.parent == root.transform && c.name.StartsWith(DonorPrefix) && c.enabled)
                donors.Add(c);
        donors.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (donors.Count < 4)
        {
            Debug.LogWarning($"[maple-riders+] only {donors.Count} donor riders; not doubling.");
            _spawning = false; _done = true; yield break;
        }

        var group = new GameObject(GroupName).transform;
        group.SetParent(root.transform, false);
        var made = new List<NPCCyclist>();
        int budget = Mathf.Max(1, perFrame);
        for (int k = 0; k < donors.Count; k++)
        {
            var source = donors[k];
            var clone = Instantiate(source.gameObject, group);
            clone.name = $"Maple City Extra Cyclist {k + 1:D3}";
            var c = clone.GetComponent<NPCCyclist>();
            if (c == null || c.route == null || c.route.Length < 2) { Destroy(clone); continue; }
            // the greeting copy would repeat the donor's name; NpcCyclistInteraction re-issues a fresh identity
            var greet = clone.GetComponent<NpcGreeting>();
            if (greet != null) { greet.riderName = ""; greet.portrait = null; greet.ownPhrases = new string[0]; }
            float back = offsetM / NPCCyclist.Spacing;
            float p = source.progress - back;
            int n = c.route.Length - 1;
            if (p < 0f) p += n;                                       // the loop is closed: wrap
            c.progress = Mathf.Repeat(p, n);
            c.speed = source.speed * (0.92f + 0.016f * (k % 10));     // 0.92 .. 1.06 of the donor, so pairs drift
            c.ApplyPose();
            made.Add(c);
            if (--budget <= 0) { budget = Mathf.Max(1, perFrame); yield return null; }
        }

        // ---- second district: one oncoming rider per staged donor on the East loop (segment "maplecity_east", also 5.0 km)
        int eastMade = 0;
        var eastGroup = new GameObject(EastGroupName).transform;
        eastGroup.SetParent(root.transform, false);
        for (int k = 0; k < donors.Count; k++)
        {
            var source = donors[k];
            var clone = Instantiate(source.gameObject, eastGroup);
            clone.name = $"Maple City East Cyclist {k + 1:D3}";
            var c = clone.GetComponent<NPCCyclist>();
            if (c == null) { Destroy(clone); continue; }
            c.segmentId = "maplecity_east";
            if (!c.RebuildFromGraph() || c.route == null || c.route.Length < 2)
            {
                Destroy(clone);                                          // East segment not baked: skip silently below
                continue;
            }
            var greet = clone.GetComponent<NpcGreeting>();
            if (greet != null) { greet.riderName = ""; greet.portrait = null; greet.ownPhrases = new string[0]; }
            int n = c.route.Length - 1;
            c.progress = Mathf.Repeat(source.progress + 30f / NPCCyclist.Spacing, n);
            c.speed = source.speed * (0.94f + 0.012f * (k % 10));
            c.ApplyPose();
            made.Add(c); eastMade++;
            if (--budget <= 0) { budget = Mathf.Max(1, perFrame); yield return null; }
        }
        if (eastMade == 0) Destroy(eastGroup.gameObject);

        var avoid = root.GetComponent<TrafficAvoidance>();
        if (avoid != null)
        {
            var all = new List<NPCCyclist>(avoid.riders ?? new NPCCyclist[0]);
            all.AddRange(made);
            avoid.riders = all.ToArray();
        }
        Debug.Log($"[maple-riders+] added {made.Count} cyclists ({eastMade} on the East district) to {donors.Count} staged riders " +
                  $"({(avoid != null ? avoid.riders.Length : donors.Count + made.Count)} managed in total).");
        _spawning = false; _done = true;
    }
}

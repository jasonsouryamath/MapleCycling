using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// More cyclists on the Nagisa Bay road (2026-10-02, user: "add more cyclist").
///
/// The staged roster is ~48 riders (NagisaNpcRoster + NagisaMovingRiders), clustered on the two coastal
/// spans at a 175 m pitch, with the whole climb almost empty. This adds roughly 90 more at run time by
/// cloning those same validated riders, exactly as NagisaMovingRiders does at edit time:
///   * interleaved with the staged ones (offset pitch) on the coastal spans, and thinly across the climb;
///   * both directions, so there are always riders coming and going;
///   * a different source rider each time, so the same livery is rarely adjacent;
///   * greeting cards stay with the original named riders; clones get no <see cref="NpcGreeting"/>;
///   * <see cref="NagisaRiderLod"/> (cloned) still culls/throttles by distance, <see cref="TrafficAvoidance"/>
///     is refreshed so the clones also swerve around the player and each other.
/// Done at run time (not baked into the scene) so no region re-export is needed. Clones live under the
/// "Nagisa NPCs" root, so the existing region gating hides them when the rider leaves Nagisa Bay.
/// Spawned a few per frame; <see cref="NPCCyclist.Awake"/> rebuilds each route from the baked graph.
/// </summary>
public sealed class NagisaCyclistBooster : MonoBehaviour
{
    public const string RootName = "Nagisa NPCs";
    public const string GroupName = "Nagisa Extra Cyclists";
    const string DonorPrefix = "Nagisa NPC ";

    [Tooltip("Kill switch for before/after captures: env MR_NAGISA_EXTRA_RIDERS=0.")]
    public int maxAdded = 90;
    public float coastalPitchM = 105f;
    public float climbPitchM = 380f;
    public int perFrame = 3;

    // Coastal promenade spans and the climb between them (metres along the ride road).
    static readonly Vector2 CoastA = new Vector2(150f, 4650f);
    static readonly Vector2 Climb = new Vector2(4800f, 12100f);
    static readonly Vector2 CoastB = new Vector2(12240f, 14900f);

    /// <summary>Authored pace x NagisaNpcRoster.PaceScale (0.56: chibi riders read fast at true speed).</summary>
    const float PaceScale = 0.56f;

    static NagisaCyclistBooster _instance;
    bool _spawning, _done;
    RegionDirector _regions;
    float _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_NAGISA_EXTRA_RIDERS") == "0") return;
        var go = new GameObject("~Nagisa Cyclist Booster");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<NagisaCyclistBooster>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { _instance = null; }

    void Update()
    {
        if (_spawning || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        if (_regions == null) _regions = FindFirstObjectByType<RegionDirector>();
        if (_regions == null || _regions.currentRegionId != NagisaBayLook.RegionId) { _done = false; return; }
        if (_done) return;
        var root = GameObject.Find(RootName);
        if (root == null) return;
        if (root.transform.Find(GroupName) != null) { _done = true; return; }   // already boosted (scene kept alive)
        StartCoroutine(Spawn(root));
    }

    IEnumerator Spawn(GameObject root)
    {
        _spawning = true;
        var donors = new List<NPCCyclist>();
        foreach (var c in root.GetComponentsInChildren<NPCCyclist>(true))
            if (c.transform.parent == root.transform && c.name.StartsWith(DonorPrefix) && !c.name.EndsWith("Kaimana"))
                donors.Add(c);
        donors.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var fwd = donors.FindAll(d => !d.reverse);
        var rev = donors.FindAll(d => d.reverse);
        if (donors.Count < 8 || fwd.Count == 0 || rev.Count == 0)
        {
            Debug.LogWarning($"[nagisa-riders+] only {donors.Count} donor riders ({fwd.Count} with / {rev.Count} against); not boosting.");
            _spawning = false; _done = true; yield break;
        }

        var plan = new List<float>();
        for (float d = CoastA.x + coastalPitchM * 0.5f; d < CoastA.y; d += coastalPitchM) plan.Add(d);
        for (float d = Climb.x; d < Climb.y; d += climbPitchM) plan.Add(d);
        for (float d = CoastB.x + coastalPitchM * 0.5f; d < CoastB.y; d += coastalPitchM) plan.Add(d);
        if (plan.Count > maxAdded)   // thin evenly rather than dropping a whole end of the road
        {
            var thinned = new List<float>(maxAdded);
            for (int i = 0; i < maxAdded; i++) thinned.Add(plan[Mathf.FloorToInt(i * (plan.Count / (float)maxAdded))]);
            plan = thinned;
        }

        var group = new GameObject(GroupName).transform;
        group.SetParent(root.transform, false);
        var made = new List<NPCCyclist>();
        int frameBudget = Mathf.Max(1, perFrame);
        for (int k = 0; k < plan.Count; k++)
        {
            bool reverse = (k % 2 == 0);
            var pool = reverse ? rev : fwd;
            var source = pool[(k * 7 + 3) % pool.Count];
            var clone = Instantiate(source.gameObject, group);
            clone.name = $"Nagisa Extra Cyclist {k + 1:D3}";
            var c = clone.GetComponent<NPCCyclist>();
            if (c == null || c.route == null || c.route.Length < 2) { Destroy(clone); continue; }
            int index = Mathf.RoundToInt(plan[k] / NPCCyclist.Spacing);
            c.progress = Mathf.Clamp(reverse ? c.route.Length - 1 - index : index, 0, c.route.Length - 2);
            c.speed = (3.8f + (k % 5) * 0.32f) * PaceScale;
            c.ApplyPose();
            var greet = clone.GetComponent<NpcGreeting>();
            if (greet != null) greet.enabled = false;
            if (clone.GetComponent<NagisaRiderLod>() == null) clone.AddComponent<NagisaRiderLod>();
            NagisaBikeShadow.Attach(clone);
            made.Add(c);
            if (--frameBudget <= 0) { frameBudget = Mathf.Max(1, perFrame); yield return null; }
        }

        var avoid = root.GetComponent<TrafficAvoidance>();
        if (avoid != null)
        {
            var all = new List<NPCCyclist>(avoid.riders ?? new NPCCyclist[0]);
            all.AddRange(made);
            avoid.riders = all.ToArray();
        }
        Debug.Log($"[nagisa-riders+] added {made.Count} cyclists (pitch {coastalPitchM:0} m coast / {climbPitchM:0} m climb); " +
                  $"{(avoid != null ? avoid.riders.Length : donors.Count + made.Count)} managed in total.");
        _spawning = false; _done = true;
    }
}

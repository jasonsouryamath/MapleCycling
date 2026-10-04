using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DOUBLES the people on Maple City's pavements (user, 2026-10-03: "double its NPCs").
///
/// MapleCityLife staged ~30 m-pitch walkers and ~60 joggers into the scene. This clones every pavement figure at run time
/// (walkers and runners both carry a <see cref="MapleCityWalker"/> on their root) and seats the clone ~12-19 m further along
/// the same lane with a small lateral offset, so the pavements read as busy instead of evenly spaced. Position stays a pure
/// function of Time.time inside MapleCityWalker, so clones cost nothing extra to keep in step. Done at run time, like
/// MapleCityCyclistBooster / NagisaCyclistBooster, so no re-stage or scene re-export is required.
/// Kill switch for before/after captures: env MR_MAPLE_EXTRA_PEOPLE=0.
/// </summary>
public sealed class MapleCityPedestrianBooster : MonoBehaviour
{
    public const string GroupName = "Maple City Life";
    public const string ExtraName = "Maple City Extra People";
    public int perFrame = 4;

    static MapleCityPedestrianBooster _instance;
    bool _spawning, _done;
    RegionDirector _regions;
    float _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_MAPLE_EXTRA_PEOPLE") == "0") return;
        var go = new GameObject("~Maple City Pedestrian Booster");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MapleCityPedestrianBooster>();
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
        var group = GameObject.Find(GroupName);
        if (group == null) return;
        if (group.transform.Find(ExtraName) != null) { _done = true; return; }
        StartCoroutine(Spawn(group.transform));
    }

    IEnumerator Spawn(Transform group)
    {
        _spawning = true;
        var donors = new List<MapleCityWalker>();
        foreach (var w in group.GetComponentsInChildren<MapleCityWalker>(true))
            if (w != null && w.lanes != null && w.lanes.Valid(w.lane)) donors.Add(w);
        if (donors.Count < 10)
        {
            Debug.LogWarning($"[maple-people+] only {donors.Count} pavement figures; not doubling.");
            _spawning = false; _done = true; yield break;
        }
        var holder = new GameObject(ExtraName).transform;
        holder.SetParent(group, false);
        int made = 0, budget = Mathf.Max(1, perFrame);
        for (int k = 0; k < donors.Count; k++)
        {
            var src = donors[k];
            var clone = Instantiate(src.gameObject, holder);
            clone.name = src.name + "_x2";
            var w = clone.GetComponent<MapleCityWalker>();
            float shift = 12f + 7f * Hash01(k);                                       // 12..19 m further along the lane
            w.startS = src.startS + src.heading * shift;
            w.lateral = src.lateral + (Hash01(k + 777) - 0.5f) * 0.5f;
            w.speed = src.speed * (0.9f + 0.2f * Hash01(k + 31));
            w.Place(Time.time);
            made++;
            if (--budget <= 0) { budget = Mathf.Max(1, perFrame); yield return null; }
        }
        Debug.Log($"[maple-people+] added {made} pavement figures to {donors.Count} staged ones.");
        _spawning = false; _done = true;
    }

    static float Hash01(int n)
    {
        unchecked { uint x = (uint)(n * 747796405 + 2891336453); x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737; x = (x >> 22) ^ x; return (x & 0xFFFF) / 65535f; }
    }
}

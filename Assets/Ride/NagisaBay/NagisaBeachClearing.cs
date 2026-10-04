using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Clears the trees from the east-coast beach run (2026-10-02, user: "remove the trees ... make it all beach
/// and vast ocean like the beginning").
///
/// The trees are baked into the streamed region cells, so rather than re-exporting every region this hides
/// them as each Nagisa cell loads: every tree-family instance whose position projects onto the road inside
/// [<see cref="FromM"/>, <see cref="ToM"/>] and sits more than <see cref="KeepVergeM"/> from the centreline
/// is deactivated (the road-edge palms stay so the road still has a frame). Merged "FarForest" chunks are
/// judged by their bounds centre. Work is spread across frames; each scene is processed once.
/// Disable with env MR_NAGISA_KEEP_TREES=1.
/// </summary>
public sealed class NagisaBeachClearing : MonoBehaviour
{
    public float FromM = 11900f, ToM = 15000f;
    public float KeepVergeM = 7f;
    public int nodesPerFrame = 2500;


    // Placed-instance name tokens (matched on the topmost object whose name contains one).
    static readonly string[] Tokens =
    {
        "CoconutPalm", "CanopyTree", "FanPalm", "RoyalPalm", "Frangipani", "StreetTree", "FarForest",
        "Black Pine", "BlackPine",
    };   // instance names only: a GROUP name (e.g. "Tropical Forest") must be descended into, not matched whole
    static readonly string[] Skip = { "Planter", "Lamp", "Light", "Sign", "Hotel", "Bench" };

    static NagisaBeachClearing _instance;
    readonly HashSet<int> _done = new HashSet<int>();
    public static int Hidden { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_NAGISA_KEEP_TREES") == "1") return;
        var go = new GameObject("~Nagisa Beach Clearing");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<NagisaBeachClearing>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { _instance = null; Hidden = 0; }

    void OnEnable() { SceneManager.sceneLoaded += OnLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnLoaded; }

    void OnLoaded(Scene scene, LoadSceneMode mode) => Queue(scene);

    float _next;
    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;
        for (int i = 0; i < SceneManager.sceneCount; i++) Queue(SceneManager.GetSceneAt(i));   // scenes loaded before we booted
    }

    void Queue(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        string path = (scene.path ?? "").Replace('\\', '/');
        if (!path.Contains("nagisa_bay")) return;
        if (!_done.Add(scene.handle)) return;
        StartCoroutine(Process(scene));
    }

    static bool IsTree(string name)
    {
        foreach (var s in Skip) if (name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
        foreach (var t in Tokens) if (name.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    IEnumerator Process(Scene scene)
    {
        while (!NagisaRoadIndex.Ready) yield return null;
        int budget = nodesPerFrame, hidden = 0, seen = 0;
        var stack = new Stack<Transform>();
        foreach (var root in scene.GetRootGameObjects()) if (root != null) stack.Push(root.transform);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (t == null || !t.gameObject.activeSelf) continue;
            seen++;
            if (IsTree(t.name))
            {
                if (InClearing(t)) { t.gameObject.SetActive(false); hidden++; }
                // matched instances are handled whole; never descend into them
            }
            else
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            if (--budget <= 0) { budget = nodesPerFrame; yield return null; if (!scene.isLoaded) yield break; }
        }
        Hidden += hidden;
        if (hidden > 0) Debug.Log($"[nagisa-clear] {scene.name}: hid {hidden} trees in {FromM:0}-{ToM:0} m ({seen} nodes scanned).");
    }

    bool InClearing(Transform t)
    {
        var p = t.position;
        var r = t.GetComponentInChildren<Renderer>(true);
        if (r != null && (t.name.IndexOf("FarForest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                          t.name.IndexOf("Forest", System.StringComparison.OrdinalIgnoreCase) >= 0)) p = r.bounds.center;
        if (!NagisaRoadIndex.Project(p, out float d, out float lat, out _, 220f)) return false;
        return d >= FromM && d <= ToM && Mathf.Abs(lat) > KeepVergeM;
    }
}

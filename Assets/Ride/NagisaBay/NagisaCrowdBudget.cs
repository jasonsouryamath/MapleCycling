using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// NB4 runtime crowd budget for the Nagisa Bay promenade crowd (the "NB Crowd" stage group).
/// Time-sliced, allocation-free after Start. Per person, by distance to the main camera:
///   &lt; 120 m  : full detail, shadows on
///   &gt; 120 m  : shadow casting off (people read as dark flecks anyway)
///   &gt; 200 m  : LODGroup forced to the last (low "impostor" mesh) level
///   &gt; 800 m  : the whole person is switched off (no skinning, no animation, no LOD evaluation)
/// Walkers / runners / skaters re-derive their position from the shared clock when they wake, so
/// sleeping people never drift or pop. Hysteresis keeps people from flickering at a band edge.
/// Only people under this group are managed: NB13 / BeachLife crowds are not touched.
/// </summary>
[DisallowMultipleComponent]
public sealed class NagisaCrowdBudget : MonoBehaviour
{
    public float shadowOffDistance = 120f;
    public float impostorDistance = 200f;
    public float sleepDistance = 800f;
    [Tooltip("People examined per frame (time slicing).")] public int perFrame = 48;
    [Range(0.02f, 0.3f)] public float hysteresis = 0.08f;

    private enum Band : byte { Near, NoShadow, Impostor, Asleep }

    private sealed class Person
    {
        public Transform root;
        public Vector3 home;           // rest position used while asleep (people move < 30 m from their lane, so this is fine)
        public LODGroup lod;
        public Renderer[] renderers;
        public ShadowCastingMode[] shadows;
        public Band band = Band.Near;
    }

    private readonly List<Person> _people = new List<Person>(1024);
    private int _cursor;
    private Camera _cam;

    public int Count => _people.Count;
    public int AsleepCount { get; private set; }

    /// <summary>Distribution snapshot, for tests: near / noShadow / impostor / asleep.</summary>
    public int[] Histogram()
    {
        var h = new int[4];
        foreach (var p in _people) h[(int)p.band]++;
        return h;
    }

    private void Start() => Collect();

    public void Collect()
    {
        _people.Clear();
        // a person = the nearest ancestor-or-self that owns the LODGroup (donor clones keep their LODGroup at the root)
        var seen = new HashSet<Transform>();
        foreach (var g in GetComponentsInChildren<LODGroup>(true))
        {
            if (!seen.Add(g.transform)) continue;
            var rs = g.GetComponentsInChildren<Renderer>(true);
            var p = new Person { root = g.transform, home = g.transform.position, lod = g, renderers = rs, shadows = new ShadowCastingMode[rs.Length] };
            for (int i = 0; i < rs.Length; i++) p.shadows[i] = rs[i] != null ? rs[i].shadowCastingMode : ShadowCastingMode.Off;
            _people.Add(p);
        }
    }

    private void LateUpdate()
    {
        int n = _people.Count;
        if (n == 0) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        var cp = _cam.transform.position;
        int steps = Mathf.Min(perFrame, n);
        for (int s = 0; s < steps; s++)
        {
            if (_cursor >= n) _cursor = 0;
            var p = _people[_cursor++];
            if (p.root == null) continue;
            // asleep people are inactive, so measure from the cached home point
            Vector3 pos = p.band == Band.Asleep ? p.home : p.root.position;
            float d = Vector3.Distance(pos, cp);
            Apply(p, Classify(p.band, d));
        }
    }

    private Band Classify(Band cur, float d)
    {
        float h = hysteresis;
        // thresholds widen on the way out of a band (wake/enter only after clearly crossing)
        float sleep = sleepDistance * (cur == Band.Asleep ? 1f - h : 1f + h * 0.0f);
        float imp = impostorDistance * (cur >= Band.Impostor ? 1f - h : 1f);
        float sh = shadowOffDistance * (cur >= Band.NoShadow ? 1f - h : 1f);
        if (d > sleep) return Band.Asleep;
        if (d > imp) return Band.Impostor;
        if (d > sh) return Band.NoShadow;
        return Band.Near;
    }

    private void Apply(Person p, Band b)
    {
        if (b == p.band) return;
        var old = p.band;
        p.band = b;
        if (old == Band.Asleep) { p.root.gameObject.SetActive(true); AsleepCount--; p.home = p.root.position; }
        if (b == Band.Asleep)
        {
            p.home = p.root.position;
            if (p.lod != null) p.lod.ForceLOD(-1);
            p.root.gameObject.SetActive(false);
            AsleepCount++;
            return;
        }
        // shadows: off from NoShadow outwards
        bool noShadow = b >= Band.NoShadow;
        for (int i = 0; i < p.renderers.Length; i++)
        {
            var r = p.renderers[i];
            if (r == null) continue;
            r.shadowCastingMode = noShadow ? ShadowCastingMode.Off : p.shadows[i];
        }
        if (p.lod != null)
        {
            if (b == Band.Impostor) p.lod.ForceLOD(Mathf.Max(0, p.lod.lodCount - 1));
            else p.lod.ForceLOD(-1);
        }
    }

    private void OnDisable()
    {
        // leave the scene fully enabled when the budget stops (domain reload / stage rebuild)
        foreach (var p in _people)
        {
            if (p.root == null) continue;
            if (p.band == Band.Asleep) p.root.gameObject.SetActive(true);
            if (p.lod != null) p.lod.ForceLOD(-1);
            for (int i = 0; i < p.renderers.Length; i++)
                if (p.renderers[i] != null) p.renderers[i].shadowCastingMode = p.shadows[i];
            p.band = Band.Near;
        }
        AsleepCount = 0;
    }
}

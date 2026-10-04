using UnityEngine;

[DefaultExecutionOrder(150)]
public class NPCCyclist : MonoBehaviour
{
    public Vector3[] route = new Vector3[0];
    [Tooltip("Travel speed along the route, m/s. PROVISIONAL: 2.2 m/s is ~8 kph, a relaxed " +
             "social pace. The old 4 m/s default read as sped-up film at this world's chibi " +
             "scale - the riders are ~1.4 m tall, so a metre of road covers far more of the " +
             "frame than it would for a full-size rider and the same speed looks nearly double. " +
             "The staged roster's per-rider speeds come from SakuraNpcRoster.PaceScale.")]
    public float speed = 2.2f;
    public float laneOffset;
    public float progress;
    public float groundOffset = 0.12f;

    // There is deliberately no greet-time look-at here. An earlier version turned the rider's
    // whole body toward the player for the duration of a greeting, which made a cyclist
    // travelling at speed swivel on the spot and look as though the bike were sliding
    // sideways down the road. NPCs now always face their direction of travel; NpcGreeting
    // uses a view cone instead, so the NPC greets whoever comes into view without turning.

    [Header("Live route binding")]
    [Tooltip("Segment in the baked RouteGraph this rider belongs to. The route array is only a " +
             "cached copy of it; leave this set and the rider re-derives its line at startup.")]
    public string segmentId = "pass";

    [Tooltip("True when the rider travels against the segment's own direction (Sakura's roster " +
             "is oncoming traffic, which is what makes the greeting view cone fire at all).")]
    public bool reverse = true;

    /// <summary>
    /// True when <see cref="route"/> already has the lane and ground offsets folded into it,
    /// measured against the road's real side/up frames. Set by <see cref="RebuildFromGraph"/>.
    ///
    /// The old path applied the lane at runtime as <c>Cross(Vector3.up, tangent)</c>, which is a
    /// HORIZONTAL side vector. Sakura Pass is superelevated up to 6.8 degrees, so on the banked
    /// bends that put a rider 2 m off the centreline up to 0.24 m out vertically - reading as a
    /// rider sunk into, or hovering over, the asphalt.
    /// </summary>
    public bool routeIncludesOffsets;

    /// <summary>
    /// Spacing used when sampling the graph, in metres. Kept uniform on purpose: the roster
    /// seats riders with <c>MeetDistance / RouteSpacing</c>, which is only a distance if the
    /// points really are evenly spaced.
    /// </summary>
    public const float Spacing = 3.0f;

    // ---- shared riding line + swerve-to-pass ------------------------------------------------

    /// <summary>
    /// Transient lateral step-out in metres, measured on the ROAD's side axis (+ve = the
    /// segment's own right). Driven by <see cref="TrafficAvoidance"/> while this rider is going
    /// around somebody, and returned to zero the moment the conflict clears.
    ///
    /// This is deliberately NOT folded into <see cref="laneOffset"/>. The lane offset is baked
    /// into <see cref="route"/> against the road's real banked frame at rebuild time, which is
    /// what stops a rider hovering over the asphalt on Sakura's 6.8 degree superelevation. A
    /// swerve is a per-frame value that cannot be baked, so it is carried separately and applied
    /// against the cached side vector for the point the rider is actually on.
    /// </summary>
    [System.NonSerialized] public float lateralBias;

    /// <summary>
    /// Road side vector per route point, cached by <see cref="RebuildFromGraph"/>.
    ///
    /// Needed because <see cref="lateralBias"/> has to be applied on the same axis the lane was
    /// baked on. Deriving it at runtime as <c>Cross(up, tangent)</c> would be the HORIZONTAL
    /// side, which on a banked bend points somewhere the road does not go - the exact bug the
    /// baked-offset path was introduced to kill.
    /// </summary>
    [System.NonSerialized] public Vector3[] routeSide;

    private void Awake()
    {
        // The route is SERIALIZED into the scene, so a rebuilt road silently leaves every rider
        // riding the shape of an older pass - which is exactly how the roster ended up buried in
        // (and floating under) the current asphalt, greeting the player from inside the hill.
        // Re-deriving from the baked graph at startup makes that class of drift impossible.
        RebuildFromGraph(warnOnDrift: true);
    }

    /// <summary>
    /// Re-derives <see cref="route"/> from the live <see cref="RouteGraph"/>, folding the lane
    /// and ground offsets in against the road's real side/up frames.
    ///
    /// Returns false and leaves the cached route untouched when no graph is available, so
    /// editor tooling and the self-tests can still build a rider in isolation.
    /// </summary>
    public bool RebuildFromGraph(bool warnOnDrift = false)
    {
        if (string.IsNullOrEmpty(segmentId)) return false;
        var graph = RouteGraph.Load();
        var seg = graph != null ? graph.Segment(segmentId) : null;
        if (seg == null || seg.Count < 2) return false;

        int count = Mathf.Max(2, Mathf.FloorToInt(seg.Length / Spacing) + 1);
        var pts = new Vector3[count];
        var sides = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float metres = Mathf.Min(i * Spacing, seg.Length);
            int a = seg.IndexAt(metres);
            int b = Mathf.Min(a + 1, seg.Count - 1);
            float span = seg.distance[b] - seg.distance[a];
            float f = span > 1e-4f ? (metres - seg.distance[a]) / span : 0f;

            Vector3 p = Vector3.Lerp(seg.position[a], seg.position[b], f);
            Vector3 side = Vector3.Slerp(seg.side[a], seg.side[b], f).normalized;
            Vector3 up = Vector3.Slerp(seg.up[a], seg.up[b], f).normalized;

            // Lane is measured against the rider's OWN heading, so a rider travelling against
            // the segment keeps to the other side of the carriageway automatically.
            float dir = reverse ? -1f : 1f;
            pts[i] = p + side * (dir * laneOffset) + up * groundOffset;
            sides[i] = side;
        }

        if (reverse) { System.Array.Reverse(pts); System.Array.Reverse(sides); }

        if (warnOnDrift && route != null && route.Length > 0)
        {
            float worst = 0f;
            int n = Mathf.Min(route.Length, pts.Length);
            for (int i = 0; i < n; i++) worst = Mathf.Max(worst, Vector3.Distance(route[i], pts[i]));
            if (route.Length != pts.Length || worst > 0.25f)
                Debug.LogWarning($"[npc] {name}: cached route was stale (points {route.Length} -> " +
                                 $"{pts.Length}, worst drift {worst:F2} m) and has been re-derived " +
                                 "from the route graph. Re-run SakuraNpcRoster.AddAllToScene to " +
                                 "bake the fix into the scene.");
        }

        route = pts;
        routeSide = sides;
        routeIncludesOffsets = true;
        return true;
    }

    /// <summary>
    /// Road side axis at the rider's current position, used to apply <see cref="lateralBias"/>.
    /// Falls back to the horizontal side when the cache is missing (a rider restored straight
    /// from the scene without a graph rebuild), which is close enough for a transient swerve.
    /// </summary>
    public Vector3 SideAtProgress()
    {
        if (routeSide != null && routeSide.Length == route.Length && route.Length >= 2)
        {
            int s = Mathf.Clamp(Mathf.FloorToInt(progress), 0, routeSide.Length - 2);
            float t = progress - s;
            var v = Vector3.Slerp(routeSide[s], routeSide[s + 1], t);
            if (v.sqrMagnitude > 1e-6f) return v.normalized;
        }
        int seg = Mathf.Clamp(Mathf.FloorToInt(progress), 0, Mathf.Max(0, route.Length - 2));
        var tan = (route[Mathf.Min(seg + 1, route.Length - 1)] - route[seg]).normalized;
        var side = Vector3.Cross(Vector3.up, tan);
        return side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
    }

    /// <summary>Arc distance this rider has covered along its own (possibly reversed) line.</summary>
    public float ArcM => progress * Spacing;

    void Update()
    {
        if (route == null || route.Length < 2) return;
        float distance = speed * Time.deltaTime;
        while (distance > 0f)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(progress), 0, route.Length - 2);
            Vector3 a = route[i], b = route[i + 1];
            float len = Mathf.Max(0.01f, Vector3.Distance(a, b));
            float step = Mathf.Min(distance, len * (1f - (progress - i)));
            progress += step / len;
            distance -= step;
            if (progress >= route.Length - 1) progress = 0f;
        }
        ApplyPose();
    }

    /// <summary>
    /// Places the rider on the route at the current <see cref="progress"/> and points it down
    /// the tangent. Exposed so editor tooling can seat the NPC without entering Play mode -
    /// otherwise a staged NPC sits at the world origin in the saved scene, which in this
    /// project means floating over the lake.
    /// </summary>
    public void ApplyPose()
    {
        if (route == null || route.Length < 2) return;
        int seg = Mathf.Clamp(Mathf.FloorToInt(progress), 0, route.Length - 2);
        float t = progress - seg;
        Vector3 center = Vector3.Lerp(route[seg], route[seg + 1], t);
        Vector3 tangent = (route[seg + 1] - route[seg]).normalized;

        if (routeIncludesOffsets)
        {
            // The lane and the ride height are already in the points, measured against the
            // banked road frame. Applying them again here would double them AND flatten the
            // superelevation back out. The swerve is the ONE lateral term that is not baked,
            // because it only exists while this rider is going around somebody.
            transform.position = center + SideAtProgress() * lateralBias;
        }
        else
        {
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            transform.position = center + side * (laneOffset + lateralBias) + Vector3.up * groundOffset;
        }

        if (tangent.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
    }
}

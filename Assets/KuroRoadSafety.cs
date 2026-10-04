using UnityEngine;

/// <summary>
/// Keeps the keyboard prototype inside the road corridor and recovers from a fall.
///
/// The corridor polyline is *baked in by MapleRideKuroSetup* rather than hardcoded here.
/// It used to be a private copy of the route, which silently went stale the moment the pass
/// grew its descent: the copy stopped at the old summit (z = 306) and maxZ was 330, so a rider
/// who crested the pass got pinned inside a 3.25 m bubble around a dead end point and read it
/// as an invisible wall across the road. There is now exactly one authored route.
/// </summary>
[DefaultExecutionOrder(200)]
public class KuroRoadSafety : MonoBehaviour
{
    [Tooltip("Dense centreline, in world space. Baked by MapleRideKuroSetup from the authored route.")]
    public Vector3[] route = new Vector3[0];

    public float minX = -290f;
    public float maxX = 200f;
    public float minZ = -260f;
    public float maxZ = 430f;
    public float minY = -12f;

    [Tooltip("How far from the centreline the rider may stray. The carriageway is 7 m wide.")]
    public float roadCorridor = 3.9f;

    public Vector3 recoveryPosition = new Vector3(-2f, 0.2f, -170f);

    void LateUpdate()
    {
        var p = transform.position;
        if (p.x < minX || p.x > maxX || p.z < minZ || p.z > maxZ || p.y < minY)
        {
            transform.position = recoveryPosition;
            transform.rotation = Quaternion.identity;
            return;
        }

        // With no baked route the only safe behaviour is to leave the rider alone. Falling back
        // to a built-in polyline is what caused the invisible wall in the first place.
        if (route == null || route.Length < 2) return;

        var nearest = p;
        float nearestY = p.y;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < route.Length - 1; i++)
        {
            var a = route[i];
            var b = route[i + 1];
            var ab = new Vector3(b.x - a.x, 0f, b.z - a.z);
            var ap = new Vector3(p.x - a.x, 0f, p.z - a.z);
            var denom = Mathf.Max(.001f, ab.sqrMagnitude);
            var t = Mathf.Clamp01(Vector3.Dot(ap, ab) / denom);
            var q = a + (b - a) * t;
            var sqr = (new Vector3(p.x, 0f, p.z) - new Vector3(q.x, 0f, q.z)).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; nearest = new Vector3(q.x, p.y, q.z); nearestY = q.y; }
        }

        if (bestSqr > roadCorridor * roadCorridor)
        {
            var delta = new Vector3(p.x - nearest.x, 0f, p.z - nearest.z);
            var safe = new Vector3(nearest.x, nearestY + .16f, nearest.z)
                       + (delta.sqrMagnitude > .001f ? delta.normalized * roadCorridor : Vector3.zero);
            transform.position = safe;
        }
    }
}

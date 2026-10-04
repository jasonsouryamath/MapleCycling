using System;
using UnityEngine;

/// <summary>
/// Restrained ambient motion for Minato's sea traffic, harbour buoys and the Port Gate freight
/// train. Staged by MinatoCoastEnvironment.RedesignHarbor (MinatoRedesign.Harbor.cs).
///
/// Deliberately cheap: one component drives every mover from Time.time (no physics, no per-object
/// scripts, no allocations). Each mover shuttles along a straight lane A->B and back with eased
/// ends, turning slowly on the spot while it is nearly stopped - so a ship never pops or snaps,
/// it just comes about. Lanes are validated in the editor to stay clear of the road, causeway,
/// bridge piers and the harbour structures, so nothing here needs to test geometry at runtime.
/// Bobbing (heave/pitch/roll) is a few centimetres/degrees and phase-offset per mover.
/// Lives under the Minato region root, so RegionDirector's visibility toggle also stops Update.
/// </summary>
public class MinatoSeaTraffic : MonoBehaviour
{
    [Serializable]
    public class Mover
    {
        public Transform target;
        public Vector3 a;
        public Vector3 b;
        /// <summary>Cruise speed m/s. 0 = stationary (buoys: bob only).</summary>
        public float speed;
        /// <summary>0..1 start position along the round trip, so movers are desynchronised.</summary>
        public float phase;
        /// <summary>Heave amplitude in metres and pitch/roll amplitude in degrees.</summary>
        public float heave = 0.12f;
        public float tilt = 0.8f;
        /// <summary>Keep the authored heading on the return leg (push-pull train) instead of turning.</summary>
        public bool keepHeading;
        /// <summary>Degrees per second while coming about at a lane end.</summary>
        public float turnRate = 14f;
        [NonSerialized] public Quaternion heading;
        [NonSerialized] public bool init;
        [NonSerialized] public Quaternion baseRot;
    }

    public Mover[] movers = Array.Empty<Mover>();

    /// <summary>Fraction of each leg spent easing in/out at the ends.</summary>
    const float EaseFraction = 0.12f;

    void OnEnable()
    {
        foreach (var m in movers)
            if (m != null && m.target != null && !m.init)
            {
                m.baseRot = m.target.rotation;
                m.heading = m.baseRot;
                m.init = true;
            }
    }

    void Update()
    {
        float t = Time.time;
        float dt = Time.deltaTime;
        for (int i = 0; i < movers.Length; i++)
        {
            var m = movers[i];
            if (m == null || m.target == null) continue;
            if (!m.init) { m.baseRot = m.target.rotation; m.heading = m.baseRot; m.init = true; }

            Vector3 pos = m.a;
            Vector3 dir = Vector3.zero;
            if (m.speed > 0.01f)
            {
                var ab = m.b - m.a;
                float len = ab.magnitude;
                if (len > 1f)
                {
                    // One leg at cruise speed plus the eased ends (which average half speed).
                    float legTime = len / m.speed * (1f + EaseFraction);
                    float cycle = (t / (2f * legTime) + m.phase) % 1f;
                    bool back = cycle >= 0.5f;
                    float u = back ? (cycle - 0.5f) * 2f : cycle * 2f;
                    float s = Ease(u);
                    if (back) s = 1f - s;
                    pos = m.a + ab * s;
                    // Moving direction; near the ends (speed ~0) the ship turns towards the NEXT leg.
                    dir = (back ? -ab : ab) / len;
                    if (u > 0.93f) dir = -dir;
                }
            }

            if (!m.keepHeading && dir.sqrMagnitude > 0.5f)
            {
                var want = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z), Vector3.up);
                m.heading = Quaternion.RotateTowards(m.heading, want, m.turnRate * dt);
            }
            else if (m.keepHeading) m.heading = m.baseRot;

            float ph = i * 1.7f;
            float heave = Mathf.Sin(t * 0.55f + ph) * m.heave;
            var bob = Quaternion.Euler(Mathf.Sin(t * 0.43f + ph) * m.tilt, 0f,
                                       Mathf.Sin(t * 0.61f + ph * 1.3f) * m.tilt * 1.3f);
            m.target.SetPositionAndRotation(pos + new Vector3(0f, heave, 0f), m.heading * bob);
        }
    }

    /// <summary>Linear cruise with smoothstep ramps over the first/last EaseFraction of the leg.</summary>
    static float Ease(float u)
    {
        float e = EaseFraction;
        // Piecewise: accelerate over [0,e], cruise, decelerate over [1-e,1]. Normalised so
        // Ease(1) = 1 with continuous velocity.
        float total = 1f - e;              // distance units: ramps count half
        float d;
        if (u < e) d = (u * u) / (2f * e);
        else if (u > 1f - e) { float r = 1f - u; d = total - (r * r) / (2f * e); }
        else d = u - e * 0.5f;
        return Mathf.Clamp01(d / total);
    }
}

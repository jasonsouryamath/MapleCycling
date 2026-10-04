using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives swerve-to-pass avoidance for <see cref="NPCCyclist"/>-based traffic (Sakura's named
/// roster), and keeps the player in the same conversation.
///
/// Shiosai's traffic is a recycled pool driven by <see cref="ShiosaiTrafficDirector"/>, which
/// already owns an arc coordinate for every rider and does its own avoidance inline. Sakura's
/// roster is the opposite: a fixed set of individually-placed riders, each walking its own baked
/// polyline, with no shared coordinate at all. Rather than force one of those into the other's
/// shape, both use the SAME RULE (<see cref="TrafficLine"/>) expressed in whatever coordinates
/// they naturally have.
///
/// Here that is pure world space, which has the pleasant side effect of being route-shape
/// agnostic: "in front of me" is a dot product against my own heading, so a hairpin, a spiral
/// and a straight all behave identically and no arc bookkeeping can go stale.
///
/// COST. This is O(n^2) over the riders in one region. Sakura's roster is ~16, so that is ~256
/// dot products a frame - far below the cost of the pairwise check being wrong.
/// </summary>
[DefaultExecutionOrder(140)]   // before NPCCyclist (150), so this frame's bias is the one applied
public class TrafficAvoidance : MonoBehaviour
{
    [Tooltip("Riders managed by this director. Left empty it collects every NPCCyclist under " +
             "this object's parent at enable, which is how the roster staging wires it.")]
    public NPCCyclist[] riders = new NPCCyclist[0];

    [Tooltip("The player's route follower, so riders go round the player too rather than " +
             "through them. Optional: found by type when left empty.")]
    public RouteFollower player;

    [Tooltip("Player ride session, read for the player's current speed. Optional.")]
    public RideSession session;

    [Header("Diagnostics (read-only)")]
    public int ridersOut;
    public int meetings;
    public int overtakes;

    private readonly List<NPCCyclist> _live = new List<NPCCyclist>();
    private bool[] _out;

    private void OnEnable()
    {
        if (riders == null || riders.Length == 0)
        {
            var scope = transform.parent != null ? transform.parent : transform;
            riders = scope.GetComponentsInChildren<NPCCyclist>(true);
        }
        _out = new bool[riders.Length];
        if (player == null) player = FindFirstObjectByType<RouteFollower>();
        if (session == null) session = FindFirstObjectByType<RideSession>();
    }

    private void Update()
    {
        if (riders == null || riders.Length == 0) return;
        if (_out == null || _out.Length != riders.Length) _out = new bool[riders.Length];

        float dt = Time.deltaTime;

        _live.Clear();
        for (int i = 0; i < riders.Length; i++)
            if (riders[i] != null && riders[i].isActiveAndEnabled) _live.Add(riders[i]);

        Transform playerT = player != null ? player.rider : null;
        float playerPace = session != null ? session.SpeedMps : 0f;

        ridersOut = meetings = overtakes = 0;

        for (int i = 0; i < riders.Length; i++)
        {
            var a = riders[i];
            if (a == null || !a.isActiveAndEnabled) continue;

            Vector3 pa = a.transform.position;
            Vector3 fa = a.transform.forward;
            Vector3 ra = a.transform.right;

            float want = 0f;

            // --- other riders -------------------------------------------------------------
            for (int k = 0; k < _live.Count; k++)
            {
                var b = _live[k];
                if (b == a) continue;
                float s = Pair(pa, fa, ra, b.transform.position, b.transform.forward,
                               a.speed, b.speed, _out[i], out bool onc);
                if (Mathf.Abs(s) > Mathf.Abs(want)) { want = s; if (onc) meetings++; else overtakes++; }
            }

            // --- the player ---------------------------------------------------------------
            // The player is never steered by this: they have their own lane change on
            // RouteFollower and having the game move them would fight the input. They are only
            // ever an OBSTACLE here, which is the asymmetry that makes an oncoming rider
            // reliably step aside for a player who is holding the middle of the road.
            if (playerT != null)
            {
                float s = Pair(pa, fa, ra, playerT.position, playerT.forward,
                               a.speed, playerPace, _out[i], out bool onc);
                if (Mathf.Abs(s) > Mathf.Abs(want)) { want = s; if (onc) meetings++; else overtakes++; }
            }

            _out[i] = Mathf.Abs(want) > 0.01f;
            if (_out[i]) ridersOut++;

            // Convert "metres to my own right" into the road's side axis, which is the axis the
            // lane was baked on. The sign flips for a rider travelling against the segment, and
            // getting that backwards puts an oncoming rider's avoidance INTO the player.
            float sgn = Mathf.Sign(Vector3.Dot(ra, a.SideAtProgress()));
            if (sgn == 0f) sgn = 1f;
            a.lateralBias = TrafficLine.Approach(a.lateralBias, want * sgn, dt);
        }
    }

    /// <summary>One pairwise check, in the subject's own frame.</summary>
    private static float Pair(Vector3 pa, Vector3 fa, Vector3 ra,
                              Vector3 pb, Vector3 fb,
                              float paceA, float paceB, bool alreadyOut, out bool oncoming)
    {
        Vector3 rel = pb - pa;
        oncoming = Vector3.Dot(fa, fb) < 0f;

        // Anything further than the widest window cannot matter; rejecting on the cheap squared
        // distance first keeps the inner loop honest for a 30-rider pool.
        float far = Mathf.Max(TrafficLine.MeetLookAheadM, TrafficLine.PassLookAheadM) + 4f;
        if (rel.sqrMagnitude > far * far) return 0f;

        float along = Vector3.Dot(rel, fa);
        float lat = Vector3.Dot(rel, ra);
        return TrafficLine.DesiredShift(along, lat, oncoming, paceA, paceB, alreadyOut);
    }
}

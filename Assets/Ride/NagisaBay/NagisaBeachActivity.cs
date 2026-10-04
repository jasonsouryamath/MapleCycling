using UnityEngine;

/// <summary>
/// NB6 (Nagisa Bay beach activities): one cheap driver per activity group, no per-figure
/// MonoBehaviour, no physics, no raycasts, no per-frame allocations.
///
///   Volley - a ball arcs between the players of a 2 v 2 court in a looping rally; the hitter
///            hops as the ball arrives and every player shuffles a little.
///   Glide  - figures (skaters, rollerbladers, runners) loop a closed baked polyline at their own
///            speed, facing the tangent, with optional side-to-side stroke sway and body roll.
///   Swash  - foam strips at the waterline slide up the sand and sink back (the swash), and a
///            breaker line rises, rolls in and drops again (a stand-in until NB1's NagisaSurf).
///
/// Runs in LateUpdate so it wins over MinatoCrowdActor's per-frame ground snap. Culls itself
/// beyond <see cref="cullDistance"/> of the camera (checked 4x a second). Deterministic: the
/// pose is a pure function of time (<see cref="Step"/>), which the editor uses to bake a pose
/// into the saved scene and the self-test uses to prove motion.
/// ALL numbers are PROVISIONAL art tuning.
/// </summary>
public sealed class NagisaBeachActivity : MonoBehaviour
{
    public enum Kind { Volley, Glide, Swash }

    public Kind kind = Kind.Glide;
    [Min(10f)] public float cullDistance = 450f;

    // ---- shared: the animated figures / strips and their base (baked) poses
    public Transform[] items = new Transform[0];
    public Vector3[] basePos = new Vector3[0];
    public Vector3[] baseDir = new Vector3[0];      // Volley: facing; Swash: seaward unit vector

    // ---- Volley
    public Transform ball;
    public int[] rally = new int[0];                 // indices into items, in hit order
    public float flightSeconds = 1.15f;
    public float hitHeight = 1.9f;
    public float arcOverNet = 3.4f, arcSameSide = 2.2f;
    public float hopHeight = 0.32f;

    // ---- Glide
    public Vector3[] path = new Vector3[0];          // closed loop, world space
    public float[] speeds = new float[0];            // m/s per item
    public float[] startS = new float[0];            // start distance per item
    public float[] sway = new float[0];              // lateral stroke amplitude per item (m)
    public float[] liftY = new float[0];             // height above the path (board deck) per item
    public float swayHz = 0.75f;

    // ---- Swash
    public float swashPeriod = 7.5f;
    public float[] reach = new float[0];             // landward travel per strip (m)
    public float[] phase = new float[0];             // 0..1 per strip
    public float[] depthLo = new float[0];           // y offset at the bottom of the cycle (sunk)

    private float[] _cum;
    private float _len;
    private float _nextCull;
    private bool _culled;

    public float PathLength { get { EnsurePath(); return _len; } }
    public bool IsCulled => _culled;

    private void EnsurePath()
    {
        if (_cum != null && _cum.Length == path.Length + 1) return;
        _cum = new float[path.Length + 1];
        _len = 0f;
        for (int i = 0; i < path.Length; i++)
        {
            _cum[i] = _len;
            _len += Vector3.Distance(path[i], path[(i + 1) % path.Length]);
        }
        _cum[path.Length] = _len;
    }

    /// <summary>Position + forward on the closed path at arc length s.</summary>
    public Vector3 SamplePath(float s, out Vector3 fwd)
    {
        EnsurePath();
        fwd = Vector3.forward;
        if (path.Length < 2 || _len <= 0f) return path.Length > 0 ? path[0] : transform.position;
        s = Mathf.Repeat(s, _len);
        int lo = 0, hi = path.Length;
        while (hi - lo > 1) { int m = (lo + hi) >> 1; if (_cum[m] <= s) lo = m; else hi = m; }
        var a = path[lo]; var b = path[(lo + 1) % path.Length];
        float seg = _cum[lo + 1] - _cum[lo];
        float u = seg > 1e-4f ? (s - _cum[lo]) / seg : 0f;
        var d = b - a; d.y = 0f;
        if (d.sqrMagnitude > 1e-6f) fwd = d.normalized;
        return Vector3.Lerp(a, b, u);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (Time.time >= _nextCull)
        {
            _nextCull = Time.time + 0.25f;
            var refPos = AmbientCull.GetReferencePosition(out bool has);
            _culled = has && (refPos - CentreOf()).sqrMagnitude > cullDistance * cullDistance;
        }
        if (_culled) return;
        Step(Time.time);
    }

    private Vector3 CentreOf()
    {
        if (kind == Kind.Glide && path.Length > 0)
        {
            // nearest of a few path samples, so a long loop is not culled while you ride past it
            var refPos = AmbientCull.GetReferencePosition(out _);
            float best = float.MaxValue; Vector3 bp = path[0];
            for (int i = 0; i < path.Length; i += Mathf.Max(1, path.Length / 24))
            {
                float d = (path[i] - refPos).sqrMagnitude;
                if (d < best) { best = d; bp = path[i]; }
            }
            return bp;
        }
        return transform.position;
    }

    /// <summary>Pose every item at time t (seconds). Pure function of t.</summary>
    public void Step(float t)
    {
        switch (kind)
        {
            case Kind.Volley: StepVolley(t); break;
            case Kind.Glide: StepGlide(t); break;
            case Kind.Swash: StepSwash(t); break;
        }
    }

    private void StepVolley(float t)
    {
        int n = items.Length;
        if (n == 0 || rally.Length < 2) return;
        float fs = Mathf.Max(0.3f, flightSeconds);
        float cyc = t / fs;
        int k = Mathf.FloorToInt(cyc);
        float u = cyc - k;
        int from = rally[((k % rally.Length) + rally.Length) % rally.Length];
        int to = rally[(((k + 1) % rally.Length) + rally.Length) % rally.Length];
        for (int i = 0; i < n; i++)
        {
            var it = items[i];
            if (it == null) continue;
            // small ready-stance shuffle, each player on its own phase
            float sh = Mathf.Sin(t * 1.7f + i * 1.9f) * 0.22f;
            var side = Vector3.Cross(Vector3.up, baseDir[i]).normalized;
            var p = basePos[i] + side * sh;
            // hop: the receiver jumps as the ball lands on them (u 0.8..1), the hitter lands (u 0..0.2)
            float hop = 0f;
            if (i == to && u > 0.78f) hop = Mathf.Sin((u - 0.78f) / 0.44f * Mathf.PI);
            if (i == from && u < 0.22f) hop = Mathf.Sin((u + 0.22f) / 0.44f * Mathf.PI);
            p.y += hop * hopHeight;
            it.position = p;
            var look = (ball != null ? ball.position : basePos[to]) - p; look.y = 0f;
            var face = Vector3.Slerp(baseDir[i], look.sqrMagnitude > 0.01f ? look.normalized : baseDir[i], 0.6f);
            if (face.sqrMagnitude > 1e-4f) it.rotation = Quaternion.LookRotation(face, Vector3.up);
        }
        if (ball != null)
        {
            var a = basePos[from] + Vector3.up * hitHeight;
            var b = basePos[to] + Vector3.up * hitHeight;
            bool cross = Vector3.Dot(basePos[from] - transform.position, basePos[to] - transform.position) < 0f;
            float arc = cross ? arcOverNet : arcSameSide;
            var bp = Vector3.Lerp(a, b, u) + Vector3.up * (4f * arc * u * (1f - u));
            ball.position = bp;
            ball.rotation = Quaternion.Euler(t * 420f, t * 260f, 0f);
        }
    }

    private void StepGlide(float t)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it == null) continue;
            float v = i < speeds.Length ? speeds[i] : 3f;
            float s0 = i < startS.Length ? startS[i] : 0f;
            var p = SamplePath(s0 + v * t, out var fwd);
            var side = Vector3.Cross(Vector3.up, fwd);
            float sw = i < sway.Length ? sway[i] : 0f;
            float ph = t * swayHz * Mathf.PI * 2f + i * 1.3f;
            p += side * (Mathf.Sin(ph) * sw);
            p.y += i < liftY.Length ? liftY[i] : 0f;
            it.position = p;
            float roll = sw > 0.01f ? -Mathf.Cos(ph) * 9f : 0f;
            it.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
        }
    }

    private void StepSwash(float t)
    {
        float per = Mathf.Max(1f, swashPeriod);
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it == null) continue;
            float u = Mathf.Repeat(t / per + (i < phase.Length ? phase[i] : 0f), 1f);
            // 0..0.55: surge up the sand (ease-out), 0.55..1: backwash + sink
            float x, y;
            float lo = i < depthLo.Length ? depthLo[i] : -0.2f;
            if (u < 0.55f) { float a = u / 0.55f; x = 1f - (1f - a) * (1f - a); y = Mathf.Lerp(lo, 0f, Mathf.Min(1f, a * 3f)); }
            else { float a = (u - 0.55f) / 0.45f; x = 1f - a * 0.85f; y = Mathf.Lerp(0f, lo, a * a); }
            float r = i < reach.Length ? reach[i] : 3f;
            var sea = baseDir[i];
            it.position = basePos[i] - sea * (x * r) + Vector3.up * y;
        }
    }
}

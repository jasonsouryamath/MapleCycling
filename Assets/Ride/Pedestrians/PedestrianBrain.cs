using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AMBIENT PEDESTRIANS (copilot, 2026-09-26). The runtime "brain" of one ambient pedestrian:
/// WHERE it walks, HOW FAST, WHEN it stops, WHAT it does while stopped and HOW it reacts to the
/// player's bicycle. The body is still the shared <see cref="MinatoCrowdActor"/> procedural rig
/// (gait, rendered-sole foot planting, LODs); this component drives it through the actor's
/// external-drive hooks (root pose, integrated gait phase, gait weight).
///
/// PATHS. Lane = a Maple City pavement lane (<see cref="MapleCityWalkLanes"/>, closed loop);
/// Segment = a Minato corridor (ping-pong, clipped at start-up to clear ground by
/// <see cref="PedestrianDirector"/>); Spot = a stationary figure (standers, chats, sitters), which
/// keeps its own idle and only gets head-looks and the occasional small idle action.
///
/// LOCOMOTION. Speed eases (accel/decel) toward a per-NPC preferred pace; the gait phase is
/// INTEGRATED at hz = speed / measured stride, so the planted foot does not slide, and the
/// stride shrinks (gait weight) below the slowest natural cadence so starts/stops do not skate.
/// Facing turns at a capped rate toward a look-ahead direction, and a figure that has to turn a
/// lot starts walking while it turns (no pirouette-then-go). Segment ends are an ease-to-stop,
/// a short pause and a smooth turn - never the old instant 180 flip.
///
/// PLAYER AWARENESS. Head tracks an approaching bicycle inside AwareRadius; if its predicted
/// closest pass is inside YieldRadius the figure eases to a stop and steps aside (within its
/// clear lateral range), waits for it to pass, then resumes. A very close fast pass gives a brief
/// startle (small lean back, hands up, one step). Cooldowns stop it from flinching constantly.
///
/// Every number marked PROVISIONAL is an illustrative tuning default, not a requirement.
/// </summary>
[DisallowMultipleComponent]
public sealed class PedestrianBrain : MonoBehaviour
{
    public enum PathKind { Lane, Segment, Spot }
    public enum State { Walk, Pause, Turn, Yield, Startle }
    public enum Idle { None, Phone, LookAround, Adjust, Stretch, Scenery, Photo, Map, Wait }

    // ------------------------------------------------------------------ PROVISIONAL knobs
    public static float Accel = 0.65f;           // m/s^2
    public static float Decel = 1.1f;
    public static float HardDecel = 2.6f;
    public static float WalkTurnRate = 160f;     // deg/s
    public static float StandTurnRate = 95f;
    public static float SideStepSpeed = 0.5f;    // m/s
    public static float AwareRadius = 10f;
    public static float YieldRadius = 1.8f;
    public static float StartleRadius = 1.3f;
    public static float YieldHorizon = 2.8f;     // s of cyclist travel we predict
    public static float HearRadius = 4.5f;       // a bike behind you is noticed inside this
    public static float MinCyclistSpeed = 1.2f;
    public static float MinHz = 0.8f, MaxHz = 2.1f;
    public static float StopEveryMin = 22f, StopEveryMax = 70f;   // s between idle stops

    // ------------------------------------------------------------------ wiring
    public PathKind kind;
    public MinatoCrowdActor actor;
    public MapleCityWalker walker;
    public PedestrianAppearance look;
    public bool hasGesture, seated;

    // path
    MapleCityWalkLanes _lanes;
    int _lane;
    Vector3 _a, _b;
    float _len;
    float[] _latLo, _latHi;       // segment clear lateral range per SegmentStep
    const float SegmentStep = 0.5f;
    public float s;
    public int dirSign = 1;
    public float lateral, lateralBase, lateralTarget;
    float _lift;
    Vector3 _buildingSide;        // Lane only: unit vector toward the shopfronts at s (0 if unknown)

    // motion
    public float prefSpeed, speed;
    Vector3 _facing = Vector3.forward;
    float _phase, _weight;
    float _stride = -1f;

    // state
    public State state;
    float _stateT, _stateDur;
    public Idle idle;
    float _idleT, _idleDur;
    float _nextStop;
    Vector3 _idleFace;
    bool _idleTurn;
    bool _turnAtEnd;

    // awareness
    float _aware, _startle;
    Vector3 _lookAt;
    float _startleCooldown, _yieldCooldown;
    float _headYaw, _headPitch;
    float _personaLean;

    // counters (harness)
    public int yields, startles, stops, turnarounds;
    public float lastYieldTime = -99f, lastStartleTime = -99f;
    public float minCyclistDistance = float.MaxValue;
    public bool Deliberate => state != State.Walk || speed < 0.05f && kind != PathKind.Spot;

    // companions (Maple City pairs): the follower keeps station beside its leader and stops with it
    [NonSerialized] public PedestrianBrain leader, follower;
    float _stationS;              // follower's arc offset from the leader (stagger)
    [NonSerialized] public int tier;

    public void MakeFollower(PedestrianBrain lead)
    {
        leader = lead;
        lead.follower = this;
        _stationS = (s - lead.s) * dirSign;
        prefSpeed = lead.prefSpeed;
        if (look != null && lead.look != null && look.persona != lead.look.persona)
            prefSpeed = lead.prefSpeed;          // pairs share a pace, whatever their age
        // side-by-side with a readable gap (the builder's +-0.28 m overlapped chibi shoulders)
        float side = lateralBase >= lead.lateralBase ? 1f : -1f;
        lateralBase = lateralTarget = side * CompanionHalfGap;
        lead.lateralBase = lead.lateralTarget = -side * CompanionHalfGap;
    }
    public static float CompanionHalfGap = 0.38f;   // PROVISIONAL
    public static float ClearLookAhead = 1.4f;       // m of lane checked ahead for narrowing
    public static float SingleFileGap = 0.85f;       // companions fall in behind on a narrow pavement
    public static float PairLookAhead = 3.5f;        // m ahead a pair checks for a pinch

    bool NarrowAhead(float dist)
    {
        if (kind != PathKind.Lane || _lanes == null) return false;
        for (float d = 1f; d <= dist; d += 1.25f)
        {
            _lanes.LateralRange(_lane, s + dirSign * d, out float lo, out float hi);
            if (hi - lo < CompanionHalfGap * 2f) return true;
        }
        return false;
    }

    System.Random _rng;
    bool _ready;

    float Rand() => (float)_rng.NextDouble();
    float Rand(float a, float b) => Mathf.Lerp(a, b, Rand());

    // ================================================================== init

    public void InitLane(MapleCityWalker w)
    {
        walker = w;
        kind = PathKind.Lane;
        _lanes = w.lanes;
        _lane = w.lane;
        dirSign = w.heading >= 0 ? 1 : -1;
        s = w.ArcAt(Time.time);
        lateral = lateralBase = lateralTarget = w.lateral;
        _lift = w.lift;
        prefSpeed = w.speed;
        w.externalDrive = true;
        CommonInit();
        _buildingSide = BuildingSideAt(s);
        PlaceNow(true);
    }

    public void InitSegment(Vector3 a, Vector3 b, float startS, int dir)
    {
        kind = PathKind.Segment;
        _a = a; _b = b;
        _len = new Vector2(b.x - a.x, b.z - a.z).magnitude;   // (Flat() normalises - never use it for distances)
        s = Mathf.Clamp(startS, 0f, _len);
        dirSign = dir >= 0 ? 1 : -1;
        prefSpeed = actor != null ? Mathf.Max(0.45f, actor.moveSpeed) : 0.75f;
        CommonInit();
        PlaceNow(true);
    }

    public void InitSpot()
    {
        kind = PathKind.Spot;
        prefSpeed = 0f;
        CommonInit();
        _facing = Flat(transform.forward);
        if (actor != null && actor.motion == MinatoCrowdActor.MotionKind.Walk)
        {
            // a walker whose corridor turned out to be blocked: it stands (rendered-sole grounded)
            actor.externalDrive = true;
            actor.gaitWeight = 0f;
        }
    }

    /// <summary>Per-sample lateral clear range for a segment (from the director's obstacle scan).</summary>
    public void SetSegmentClearance(float[] lo, float[] hi) { _latLo = lo; _latHi = hi; }

    void CommonInit()
    {
        int seed = gameObject.name.GetHashCode() ^ (int)(transform.position.x * 131f) ^ (int)(transform.position.z * 71f);
        _rng = new System.Random(seed);
        _nextStop = Rand(StopEveryMin * 0.3f, StopEveryMax);
        _facing = Flat(transform.forward);
        if (look != null && look.persona == PedestrianAppearance.Persona.Senior)
        {
            prefSpeed *= 0.72f;
            _personaLean = 7f;
        }
        else if (look != null && look.persona == PedestrianAppearance.Persona.Young)
            prefSpeed *= 1.08f;
        speed = kind == PathKind.Spot ? 0f : prefSpeed;
        _weight = speed > 0f ? 1f : 0f;
        _phase = actor != null ? actor.phaseOffset * Mathf.PI * 2f : 0f;
        if (actor != null && kind != PathKind.Spot)
        {
            actor.externalDrive = true;
            _stride = actor.MeasuredStridePerCycle();
        }
        _ready = true;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-8f ? Vector3.forward : v.normalized; }

    // ================================================================== path sampling

    Vector3 PathPoint(float at, out Vector3 dir)
    {
        if (kind == PathKind.Lane)
        {
            var p = _lanes.Sample(_lane, at, out dir);
            return p;
        }
        dir = Flat(_b - _a);
        float u = _len > 1e-3f ? Mathf.Clamp01(at / _len) : 0f;
        return Vector3.Lerp(_a, _b, u);
    }

    /// <summary>Smoothed walking direction (look-ahead over polyline corners).</summary>
    Vector3 PathDir(float at)
    {
        if (kind != PathKind.Lane) return Flat(_b - _a) * dirSign;
        var p0 = _lanes.Sample(_lane, at - 0.9f * dirSign, out _);
        var p1 = _lanes.Sample(_lane, at + 1.4f * dirSign, out _);
        return Flat(p1 - p0);
    }

    void ClearRange(out float lo, out float hi)
    {
        if (kind == PathKind.Lane)
        {
            _lanes.LateralRange(_lane, s, out lo, out hi);
            // look ahead so a narrowing (clutter, tree pit) is side-stepped BEFORE it is reached
            _lanes.LateralRange(_lane, s + dirSign * ClearLookAhead, out float lo2, out float hi2);
            if (Mathf.Max(lo, lo2) <= Mathf.Min(hi, hi2)) { lo = Mathf.Max(lo, lo2); hi = Mathf.Min(hi, hi2); }
            if (dirSign < 0) { float t = lo; lo = -hi; hi = -t; }
        }
        else if (_latLo != null && _latLo.Length > 0)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(s / SegmentStep), 0, _latLo.Length - 1);
            lo = _latLo[i]; hi = _latHi[i];
            if (dirSign < 0) { float t = lo; lo = -hi; hi = -t; }
        }
        else { lo = -0.3f; hi = 0.3f; }
        if (hi < lo) lo = hi = 0f;
    }

    /// <summary>Lane only: the building side is the side AWAY from the sibling kerb-side lane.</summary>
    Vector3 BuildingSideAt(float at)
    {
        if (kind != PathKind.Lane || _lanes == null || _lanes.lanes.Length < 2) return Vector3.zero;
        // lanes are built in pairs per side: [kerb 1.0 m, kerb 1.9 m]
        int pair = _lane ^ 1;
        if (!_lanes.Valid(pair)) return Vector3.zero;
        var p = _lanes.Sample(_lane, at, out _);
        var sib = _lanes.lanes[pair];
        float best = float.MaxValue; Vector3 q = p;
        for (int i = 0; i < sib.points.Length; i += 1)
        {
            float d = (sib.points[i] - p).sqrMagnitude;
            if (d < best) { best = d; q = sib.points[i]; }
        }
        var toSib = q - p; toSib.y = 0f;
        if (toSib.sqrMagnitude < 0.04f) return Vector3.zero;
        // even index = kerb-side (1.0 m) lane: buildings are toward the sibling; odd = away from it
        return ((_lane & 1) == 0 ? toSib : -toSib).normalized;
    }

    // ================================================================== tick

    /// <summary>Called by the director. <paramref name="full"/> = near the camera (awareness, avoidance).</summary>
    public void Tick(float dt, bool full, PedestrianDirector dir)
    {
        if (!_ready || dt <= 0f) return;
        if (!full && look != null) look.HideProp();     // handheld props are near-tier only
        dt = Mathf.Min(dt, 0.5f);
        _startleCooldown -= dt;
        _yieldCooldown -= dt;
        _stateT += dt;
        if (kind == PathKind.Spot) { if (full) Awareness(dt, dir); SpotIdle(dt); return; }

        float target = prefSpeed;
        if (full) Awareness(dt, dir);

        switch (state)
        {
            case State.Walk:
            {
                if (leader != null)
                {
                    // keep station beside the leader; stop when it stops
                    // side by side where the pavement allows, single file where it does not
                    // (the pinch is looked for ~3 s ahead so the follower has dropped back BEFORE
                    // the pair has to close up; it only moves across once it is behind)
                    ClearRange(out float plo, out float phi);
                    var toLeader = leader.transform.position - transform.position; toLeader.y = 0f;
                    bool narrow = phi - plo < CompanionHalfGap * 2f || NarrowAhead(PairLookAhead) ||
                                  toLeader.magnitude < CompanionHalfGap * 1.5f;         // squeezed together: fall in behind
                    float station = narrow ? -SingleFileGap : _stationS;
                    float behind = (leader.s - s) * dirSign;
                    if (leader.state == State.Walk && state == State.Walk)
                        lateralTarget = narrow ? (behind > SingleFileGap * 0.7f ? Mathf.Clamp(leader.lateral, plo, phi) : lateralBase) : lateralBase;
                    float wantS = leader.s + station * dirSign;
                    float err = (wantS - s) * dirSign;
                    target = Mathf.Max(0f, leader.speed + Mathf.Clamp(err * 1.2f, -0.6f, 0.35f));
                    if (leader.state == State.Pause && !leader._turnAtEnd)
                    {
                        BeginPause(99f, leader.idle == Idle.Phone ? Idle.LookAround : leader.idle);
                        _idleFace = leader._idleFace; _idleTurn = leader._idleTurn;
                        _idleDur = leader._stateDur;
                    }
                }
                else
                {
                    // idle stops
                    _nextStop -= dt;
                    if (_nextStop <= 0f && speed > 0.2f) BeginStop();
                }
                // segment ends: ease to a stop exactly at the end, then pause + turn
                if (kind == PathKind.Segment)
                {
                    float remaining = dirSign > 0 ? _len - s : s;
                    float brake = Mathf.Sqrt(2f * Decel * Mathf.Max(0f, remaining - 0.02f));
                    target = Mathf.Min(target, brake);
                    if (remaining < 0.06f && speed < 0.12f)
                    {
                        _turnAtEnd = true;
                        if (_len < 8f)
                        {
                            // a short corridor is a browse spot (stall, view, shop window): linger
                            float r = Rand();
                            var what = r < 0.3f ? Idle.Scenery : r < 0.5f ? Idle.Phone : r < 0.68f ? Idle.LookAround
                                     : r < 0.8f ? Idle.Photo : r < 0.9f ? Idle.Map : Idle.Wait;
                            BeginPause(Rand(3f, 8f), what);
                            if (what == Idle.Scenery || what == Idle.Photo)
                            {
                                _idleFace = Flat(Vector3.Cross(Vector3.up, _facing) * (Rand() < 0.5f ? 1f : -1f));
                                _idleTurn = true;
                            }
                        }
                        else BeginPause(Rand(0.6f, 2.6f), Rand() < 0.45f ? Idle.LookAround : Idle.None);
                    }
                }
                // a figure still turning into its walk direction walks slowly while it turns
                float misalign = Vector3.Angle(_facing, PathDir(s));
                target *= Mathf.Clamp01(1.15f - misalign / 70f);
                if (full) target = Avoid(dt, target, dir);
                break;
            }
            case State.Pause:
                target = 0f;
                if (leader != null && _stateDur >= 99f) { if (leader.state != State.Pause) Resume(); }
                else if (_stateT >= _stateDur)
                {
                    if (_turnAtEnd) { _turnAtEnd = false; dirSign = -dirSign; turnarounds++; state = State.Turn; _stateT = 0f; }
                    else Resume();
                }
                break;
            case State.Turn:
                target = 0f;
                if (Vector3.Angle(_facing, PathDir(s)) < 25f) Resume();
                break;
            case State.Yield:
                target = 0f;
                if (_stateT >= _stateDur) Resume();
                break;
            case State.Startle:
                target = 0f;
                if (_stateT >= _stateDur) Resume();
                break;
        }

        float rate = target > speed ? Accel : (state == State.Startle || state == State.Yield ? HardDecel : Decel);
        speed = Mathf.MoveTowards(speed, Mathf.Max(0f, target), rate * dt);

        // advance
        s += dirSign * speed * dt;
        if (kind == PathKind.Segment) s = Mathf.Clamp(s, 0f, _len);

        // lateral: clamp the target into the clear range; step sideways at a walking pace
        ClearRange(out float lo, out float hi);
        float lt = Mathf.Clamp(lateralTarget, lo, hi);
        float before = lateral;
        lateral = Mathf.MoveTowards(lateral, lt, SideStepSpeed * dt);
        lateral = Mathf.Clamp(lateral, Mathf.Min(lo, lateral), Mathf.Max(hi, lateral));
        float latVel = Mathf.Abs(lateral - before) / dt;

        // facing
        Vector3 want = PathDir(s);
        float turnRate = WalkTurnRate;
        if (state == State.Pause && _idleTurn) { want = _idleFace; turnRate = StandTurnRate; }
        else if (state == State.Turn) turnRate = StandTurnRate * 1.4f;
        else if (state == State.Yield || state == State.Startle) { want = _facing; }
        _facing = Vector3.RotateTowards(_facing, want, turnRate * Mathf.Deg2Rad * dt, 0f);
        _facing = Flat(_facing);

        // gait: integrate phase at the rate that keeps the planted foot still
        float ground = Mathf.Sqrt(speed * speed + latVel * latVel);
        float stride = _stride > 0f ? _stride : MinatoCrowdActor.NominalStridePerCycle;
        float full1 = stride * MinHz;
        float wantW = Mathf.Clamp01(ground / Mathf.Max(0.05f, full1));
        float hz = ground > full1 ? Mathf.Min(MaxHz, ground / stride) : MinHz;
        float turning = Vector3.Angle(_facing, want);
        if (state == State.Turn || (_idleTurn && state == State.Pause && turning > 4f))
        {
            wantW = Mathf.Max(wantW, 0.3f);   // shuffle while turning on the spot
            hz = Mathf.Max(hz, 1.1f);
        }
        if (look != null && look.persona == PedestrianAppearance.Persona.Senior) wantW *= 0.85f;
        _weight = Mathf.MoveTowards(_weight, wantW, 2.5f * dt);
        if (_weight > 0.01f) _phase += hz * Mathf.PI * 2f * dt;
        else _phase = Mathf.Round(_phase / Mathf.PI) * Mathf.PI;     // settle on a two-feet-down phase
        if (actor != null)
        {
            actor.drivePhase = _phase;
            actor._driveHz = hz;
            actor.gaitWeight = _weight;
            actor.moveSpeed = Mathf.Max(0.05f, speed);
        }
        PlaceNow(false);
    }

    void PlaceNow(bool snapFacing)
    {
        var p = PathPoint(s, out var dir);
        if (snapFacing) _facing = PathDir(s);
        var right = Vector3.Cross(Vector3.up, kind == PathKind.Lane ? (dirSign < 0 ? -dir : dir) : Flat(_b - _a) * dirSign);
        p += right * lateral;
        if (kind == PathKind.Lane) p += Vector3.up * _lift;
        else p.y = transform.position.y;   // the actor grounds Minato figures on the rendered surface
        transform.SetPositionAndRotation(p, Quaternion.LookRotation(_facing, Vector3.up));
    }

    void Resume()
    {
        state = State.Walk;
        _stateT = 0f;
        idle = Idle.None;
        _idleTurn = false;
        lateralTarget = lateralBase;
    }

    void BeginPause(float dur, Idle what)
    {
        state = State.Pause;
        _stateT = 0f;
        _stateDur = dur;
        idle = what;
        _idleT = 0f;
        _idleDur = dur;
        _idleTurn = false;
    }

    void BeginStop()
    {
        _nextStop = Rand(StopEveryMin, StopEveryMax);
        stops++;
        // what to do: weighted by context
        float r = Rand();
        Idle what;
        if (r < 0.24f) what = Idle.Phone;
        else if (r < 0.40f) what = Idle.LookAround;
        else if (r < 0.56f) what = Idle.Scenery;
        else if (r < 0.66f) what = Idle.Adjust;
        else if (r < 0.76f) what = Idle.Photo;
        else if (r < 0.86f) what = Idle.Map;
        else if (r < 0.93f) what = Idle.Stretch;
        else what = Idle.Wait;
        float dur = what == Idle.Adjust ? Rand(1.6f, 2.4f) : what == Idle.Stretch ? Rand(2.6f, 3.4f) : Rand(3.2f, 7.5f);
        BeginPause(dur, what);
        // window-shop / look at the view: turn toward the shopfronts (or a random side)
        if (what == Idle.Scenery || what == Idle.Photo)
        {
            var side = _buildingSide.sqrMagnitude > 0.5f ? _buildingSide
                     : Vector3.Cross(Vector3.up, _facing) * (Rand() < 0.5f ? 1f : -1f);
            _idleFace = Flat(Vector3.Slerp(_facing, side, Rand(0.55f, 0.9f)));
            _idleTurn = true;
            // step a little toward that side if the lane has room (out of the flow)
            lateralTarget = lateralBase + Vector3.Dot(side, Vector3.Cross(Vector3.up, PathDir(s))) * 0.35f;
        }
    }

    /// <summary>Harness/showcase hook: play one idle action now.</summary>
    public void ForceIdle(Idle what, float dur)
    {
        if (kind == PathKind.Spot) { idle = what; _idleT = 0f; _idleDur = dur; return; }
        BeginPause(dur, what);
        _nextStop = Rand(StopEveryMin, StopEveryMax);
    }

    // ------------------------------------------------------------------ spot idles

    float _spotNext = -1f;
    void SpotIdle(float dt)
    {
        if (seated || hasGesture) return;           // cafe/chat figures already act
        if (_spotNext < 0f) _spotNext = Rand(4f, 30f);
        if (idle == Idle.None)
        {
            _spotNext -= dt;
            if (_spotNext <= 0f)
            {
                float r = Rand();
                idle = r < 0.35f ? Idle.Phone : r < 0.65f ? Idle.LookAround : r < 0.8f ? Idle.Adjust : r < 0.9f ? Idle.Map : Idle.Stretch;
                _idleT = 0f;
                _idleDur = idle == Idle.Adjust ? 2f : idle == Idle.Stretch ? 3f : Rand(4f, 9f);
                _spotNext = Rand(10f, 35f);
            }
        }
        else
        {
            _idleT += dt;
            if (_idleT >= _idleDur) idle = Idle.None;
        }
    }

    // ------------------------------------------------------------------ neighbours

    float Avoid(float dt, float target, PedestrianDirector dir)
    {
        if (dir == null) return target;
        var p = transform.position;
        var fwd = PathDir(s);
        var right = Vector3.Cross(Vector3.up, fwd);
        ClearRange(out float lo, out float hi);
        var near = dir.Neighbours(p, 3.2f);
        float best = Mathf.Clamp(lateralBase, lo, hi), bestCost = float.MaxValue;
        float follow = float.MaxValue;
        for (int k = 0; k <= 6; k++)
        {
            float cand = Mathf.Lerp(lo, hi, k / 6f);
            float cost = 0.6f * Mathf.Abs(cand - lateralBase) + 0.3f * Mathf.Abs(cand - lateral);
            for (int i = 0; i < near.Count; i++)
            {
                var o = near[i];
                if (o == null || o == this) continue;
                bool partner = o == leader || o == follower;
                var rel = o.transform.position - p;
                float along = Vector3.Dot(rel, fwd);
                if (along < -0.3f || along > 3.2f) continue;
                if (partner && Mathf.Abs(along) > 0.6f) continue;   // partner only matters beside me
                float side = Vector3.Dot(rel, right) + lateral;   // other's offset from my lane line
                float gap = Mathf.Abs(side - cand);
                if (gap >= (partner ? 0.62f : 0.7f)) continue;
                bool oncoming = Vector3.Dot(o._facing, _facing) < -0.3f && o.speed > 0.1f;
                float w = oncoming ? 1.4f : 1f;
                cost += (0.7f - gap) * 5f * w * (1f - along / 3.6f);
            }
            if (cost < bestCost) { bestCost = cost; best = cand; }
        }
        lateralTarget = best;
        // anyone still directly ahead in my band after the side step: match their pace or stop
        for (int i = 0; i < near.Count; i++)
        {
            var o = near[i];
            if (o == null || o == this || o == leader || o == follower) continue;
            var rel = o.transform.position - p;
            float along = Vector3.Dot(rel, fwd);
            if (along < 0.05f || along > 1.6f) continue;
            float side = Vector3.Dot(rel, right) + lateral;
            if (Mathf.Abs(side - lateral) > 0.5f) continue;
            float theirs = Vector3.Dot(o._facing, fwd) * o.speed;
            float gapSpeed = Mathf.Max(0f, (along - 0.7f) * 1.2f);
            follow = Mathf.Min(follow, Mathf.Max(0f, theirs) + gapSpeed);
        }
        // shoulder to shoulder with a stranger (e.g. the next lane) and no room to side-step:
        // one of the two (a fixed tie-break) eases off and lets the other draw ahead
        for (int i = 0; i < near.Count; i++)
        {
            var o = near[i];
            if (o == null || o == this || o == leader || o == follower) continue;
            var rel = o.transform.position - p; rel.y = 0f;
            float along = Vector3.Dot(rel, fwd);
            if (along < -0.3f || along >= 0.05f || rel.magnitude > 0.5f) continue;
            if (Vector3.Dot(o._facing, _facing) < 0.3f) continue;          // oncoming handled above
            if (GetInstanceID() < o.GetInstanceID()) continue;
            follow = Mathf.Min(follow, Mathf.Max(0f, o.speed * 0.55f));
        }
        return Mathf.Min(target, follow);
    }

    // ------------------------------------------------------------------ the player's bicycle

    void Awareness(float dt, PedestrianDirector dir)
    {
        float awareGoal = 0f;
        if (dir != null && dir.HasCyclist)
        {
            var me = transform.position;
            var cp = dir.CyclistPosition;
            var cv = dir.CyclistVelocity;
            var rel = cp - me; rel.y = 0f;
            float dist = rel.magnitude;
            minCyclistDistance = Mathf.Min(minCyclistDistance, dist);
            float cs = new Vector2(cv.x, cv.z).magnitude;
            bool approaching = Vector3.Dot(-rel, new Vector3(cv.x, 0f, cv.z)) > 0f;
            // a bike coming up from BEHIND is only noticed once it is heard (close)
            bool behind = Vector3.Dot(_facing, rel) < -0.25f * dist;
            if (behind && dist > HearRadius && !seated) { _aware = Mathf.MoveTowards(_aware, 0f, 1.2f * dt); return; }
            if (dist < AwareRadius && cs > MinCyclistSpeed && (approaching || dist < 3.5f))
            {
                awareGoal = Mathf.Clamp01(1.3f - dist / AwareRadius);
                _lookAt = cp + Vector3.up * 0.9f;
            }
            if (cs > MinCyclistSpeed && kind != PathKind.Spot && !seated)
            {
                // predicted closest approach over the horizon (my own motion included)
                var myV = _facing * speed * (state == State.Walk ? 1f : 0f);
                var relV = new Vector3(cv.x, 0f, cv.z) - myV;
                float tStar = relV.sqrMagnitude > 1e-4f ? Mathf.Clamp(-Vector3.Dot(rel, relV) / relV.sqrMagnitude, 0f, YieldHorizon) : 0f;
                var closest = rel + relV * tStar;
                float pass = closest.magnitude;
                if (state == State.Walk && pass < YieldRadius && tStar > 0.05f && _yieldCooldown <= 0f)
                    BeginYield(cp, cv, tStar);
                if (dist < StartleRadius && cs > 2.5f && _startleCooldown <= 0f && state != State.Startle)
                    BeginStartle(cp, cv);
            }
        }
        _aware = Mathf.MoveTowards(_aware, awareGoal, (awareGoal > _aware ? 2.4f : 1.2f) * dt);
        _startle = Mathf.MoveTowards(_startle, state == State.Startle && _stateT < 0.55f ? 1f : 0f,
                                     (state == State.Startle ? 5f : 2f) * dt);
    }

    void BeginYield(Vector3 cp, Vector3 cv, float tStar)
    {
        state = State.Yield;
        _stateT = 0f;
        _stateDur = Mathf.Clamp(tStar + Rand(0.6f, 1.3f), 1.0f, 4.5f);
        idle = Idle.None;
        _idleTurn = false;
        yields++;
        lastYieldTime = Time.time;
        _yieldCooldown = 3.5f;
        StepAwayFrom(cp, cv, 0.55f);
    }

    void BeginStartle(Vector3 cp, Vector3 cv)
    {
        state = State.Startle;
        _stateT = 0f;
        _stateDur = Rand(1.0f, 1.5f);
        startles++;
        lastStartleTime = Time.time;
        _startleCooldown = 9f;
        _yieldCooldown = 3f;
        StepAwayFrom(cp, cv, 0.35f);
    }

    /// <summary>Choose the lateral side that puts more distance between me and the bike's line.</summary>
    void StepAwayFrom(Vector3 cp, Vector3 cv, float amount)
    {
        // a companion pair steps aside as ONE unit (independent steps collapsed pairs together)
        if (leader != null)
        {
            if (leader.state != State.Yield && leader.state != State.Startle) leader.StepAwayFrom(cp, cv, amount);
            return;
        }
        var fwd = PathDir(s);
        var right = Vector3.Cross(Vector3.up, fwd);
        var line = new Vector3(cv.x, 0f, cv.z);
        if (line.sqrMagnitude < 1e-4f) line = fwd;
        line.Normalize();
        var me = transform.position; me.y = 0f;
        var c = cp; c.y = 0f;
        var off = (me - c) - line * Vector3.Dot(me - c, line);     // from the bike's line to me
        float sideOfLine = Vector3.Dot(off, right);
        float step = (Mathf.Abs(sideOfLine) < 0.05f ? (lateral >= 0f ? 1f : -1f) : Mathf.Sign(sideOfLine)) * amount;
        ClearRange(out float lo, out float hi);
        float cand = Mathf.Clamp(lateral + step, lo, hi);
        if (Mathf.Abs(cand - lateral) < amount * 0.4f) cand = Mathf.Clamp(lateral - step * 0.6f, lo, hi);   // no room: other way a bit
        if (follower != null)
        {
            // limit the shared shift to what the follower's clear range allows too
            float d = cand - lateral;
            follower.ClearRange(out float flo, out float fhi);
            float fc = Mathf.Clamp(follower.lateral + d, flo, fhi);
            float room = fc - follower.lateral;
            if (Mathf.Abs(room) < Mathf.Abs(d)) d = room;
            cand = lateral + d;
            follower.lateralTarget = follower.lateral + d;
        }
        lateralTarget = cand;
    }

    // ================================================================== upper-body overlays

    Transform _head, _neck, _spine2, _rArm, _rFore, _lArm, _lFore;
    bool _bonesResolved;

    void ResolveBones()
    {
        if (_bonesResolved || actor == null) return;
        _bonesResolved = true;
        _head = actor.Bone("Head");
        _neck = actor.Bone("Neck");
        _spine2 = actor.Bone("Spine02");
        _rArm = actor.Bone("RightArm");
        _rFore = actor.Bone("RightForeArm");
        _lArm = actor.Bone("LeftArm");
        _lFore = actor.Bone("LeftForeArm");
    }

    static float Env(float t, float dur, float fade = 0.6f)
    {
        if (dur <= 0f) return 0f;
        float a = Mathf.Clamp01(t / fade), b = Mathf.Clamp01((dur - t) / fade);
        float x = Mathf.Min(a, b);
        return x * x * (3f - 2f * x);
    }

    static void Rot(Transform bone, Vector3 axis, float deg)
    {
        if (bone == null || Mathf.Abs(deg) < 0.01f) return;
        bone.rotation = Quaternion.AngleAxis(deg, axis) * bone.rotation;
    }

    /// <summary>
    /// Additive acting on top of the actor's pose. Runs only on frames the actor rebuilt the
    /// skeleton (it resets bones in Update), so nothing accumulates. Axes are the figure's own
    /// right/up/forward exactly like MinatoCrowdActor.WalkPose / MapleCityCafeGesture:
    /// negative about +right swings a hanging arm FORWARD; positive about +right dips the head.
    /// </summary>
    public void LateTick(float dt)
    {
        if (!_ready || actor == null || actor.AnimatedFrame != Time.frameCount) return;
        ResolveBones();
        var right = transform.right; var up = Vector3.up; var fwd = transform.forward;
        if (seated && hasGesture) { HeadLook(dt, 0.6f); look?.HideProp(); look?.LateTick(fwd); return; }

        if (_personaLean > 0f && _spine2 != null) Rot(_spine2, right, _personaLean);

        // idle actions (while paused, or on a spot figure)
        float w = 0f;
        if (idle != Idle.None)
        {
            float t = kind == PathKind.Spot ? _idleT : _stateT;
            w = Env(t, _idleDur);
            float armSide = 1f;
            switch (idle)
            {
                case Idle.Phone:
                    // chest-height texting hold: forearm ~25 deg above horizontal, hand in front of
                    // the sternum, so the phone sits ABOVE the palm and reads from the front (a steeper
                    // -92 deg forearm put the hand at the neck and hid the phone behind it)
                    Rot(_rArm, right, -46f * w); Rot(_rArm, fwd, -14f * w * armSide);
                    Rot(_rFore, right, -70f * w);
                    Rot(_head, right, 22f * w);
                    break;
                case Idle.Map:
                    Rot(_rArm, right, -34f * w); Rot(_rArm, fwd, -10f * w);
                    Rot(_lArm, right, -34f * w); Rot(_lArm, fwd, 10f * w);
                    Rot(_rFore, right, -70f * w); Rot(_lFore, right, -70f * w);
                    Rot(_head, right, 20f * w);
                    break;
                case Idle.Photo:
                    Rot(_rArm, right, -70f * w); Rot(_rArm, fwd, -14f * w);
                    Rot(_lArm, right, -70f * w); Rot(_lArm, fwd, 14f * w);
                    Rot(_rFore, right, -62f * w); Rot(_lFore, right, -62f * w);
                    Rot(_head, right, 2f * w);
                    break;
                case Idle.Adjust:
                    Rot(_rArm, right, -46f * w); Rot(_rArm, fwd, -30f * w);
                    Rot(_rFore, right, -105f * w);
                    Rot(_head, up, -8f * w); Rot(_head, right, 6f * w);
                    break;
                case Idle.Stretch:
                    Rot(_rArm, right, -125f * w); Rot(_lArm, right, -125f * w);
                    Rot(_rArm, fwd, -12f * w); Rot(_lArm, fwd, 12f * w);
                    Rot(_rFore, right, -25f * w); Rot(_lFore, right, -25f * w);
                    Rot(_spine2, right, -6f * w);
                    Rot(_head, right, -8f * w);
                    break;
                case Idle.LookAround:
                {
                    float t2 = kind == PathKind.Spot ? _idleT : _stateT;
                    Rot(_head, up, Mathf.Sin(t2 * 0.9f) * 48f * w);
                    Rot(_spine2, up, Mathf.Sin(t2 * 0.9f) * 10f * w);
                    Rot(_head, right, -3f * w);
                    break;
                }
                case Idle.Scenery:
                    Rot(_head, right, -7f * w);
                    Rot(_head, up, Mathf.Sin(_stateT * 0.4f) * 12f * w);
                    Rot(_rArm, fwd, 5f * w); Rot(_lArm, fwd, -5f * w);
                    break;
                case Idle.Wait:
                {
                    // shifting weight, glancing one way then the other
                    float g = Mathf.Sin(_stateT * 0.8f);
                    Rot(_head, up, g * 38f * w);
                    Rot(_spine2, fwd, Mathf.Sin(_stateT * 0.5f) * 2.5f * w);
                    break;
                }
            }
        }

        // startle: small lean back, hands up a little
        if (_startle > 0.01f)
        {
            float e = _startle;
            Rot(_spine2, right, -9f * e);
            Rot(_rArm, right, -30f * e); Rot(_rArm, fwd, 22f * e);
            Rot(_lArm, right, -30f * e); Rot(_lArm, fwd, -22f * e);
            Rot(_rFore, right, -45f * e); Rot(_lFore, right, -45f * e);
        }

        HeadLook(dt, idle == Idle.Phone || idle == Idle.Map ? 0.35f : 1f);
        // handheld prop for the phone / photo / map idles (after every arm + chest overlay)
        var prop = idle == Idle.Phone ? PedestrianAppearance.Prop.Phone
                 : idle == Idle.Photo ? PedestrianAppearance.Prop.Camera
                 : idle == Idle.Map ? PedestrianAppearance.Prop.Map : PedestrianAppearance.Prop.None;
        look?.ShowProp(prop, w, _head);
        look?.LateTick(fwd);
    }

    void HeadLook(float dt, float scale)
    {
        if (_head == null) return;
        float yaw = 0f, pitch = 0f;
        float weight = Mathf.Clamp01(Mathf.Max(_aware, _startle)) * scale;
        if (weight > 0.01f)
        {
            var from = _head.position;
            var to = _lookAt - from;
            var flat = Flat(to);
            var fwd = Flat(transform.forward);
            yaw = Mathf.Clamp(Vector3.SignedAngle(fwd, flat, Vector3.up), -80f, 80f);
            float horiz = new Vector2(to.x, to.z).magnitude;
            pitch = Mathf.Clamp(-Mathf.Atan2(to.y, Mathf.Max(0.1f, horiz)) * Mathf.Rad2Deg, -20f, 20f);
        }
        float goalYaw = yaw * weight, goalPitch = pitch * weight;
        _headYaw = Mathf.MoveTowards(_headYaw, goalYaw, 220f * dt);
        _headPitch = Mathf.MoveTowards(_headPitch, goalPitch, 120f * dt);
        var right = transform.right;
        // share the turn: a little in the chest, more in the neck/head
        Rot(_spine2, Vector3.up, _headYaw * 0.22f);
        if (_neck != null) { Rot(_neck, Vector3.up, _headYaw * 0.3f); Rot(_head, Vector3.up, _headYaw * 0.48f); }
        else Rot(_head, Vector3.up, _headYaw * 0.78f);
        Rot(_head, right, _headPitch * 0.8f);
    }

    public float PathLength => _len;
    public float RemainingOnPath => kind == PathKind.Segment ? (dirSign > 0 ? _len - s : s) : float.MaxValue;
    public float HeadYawTowardCyclist => _headYaw;
    public float Awareness01 => _aware;
    public Vector3 Facing => _facing;
    public float GaitWeight => _weight;
}

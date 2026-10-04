"""
Single source of truth for the AZORA HIGHLANDS route ("Earn the View").

Publishes ``Assets/Environment/TakaMountains/TakaRoute.json`` in exactly the
schema ``sakura_route`` / ``shiosai_route`` / ``maple_city_route`` use, so ``RouteGraphBaker``
merges a FOURTH region into the one runtime ``RouteGraph`` asset and every ride system
(physics, HUD, GPS window, follower, dressing streamer, trainer grade) works up here with NO
new code paths.

Standalone: needs no ``bpy`` and no ``sakura_lib``, so it runs under Blender's Python *or* any
Python with numpy:

    python azora_route.py

All coordinates are **Unity** world space (Y up, metres).

WHAT MAKES THIS ROUTE DIFFERENT FROM THE OTHER THREE
----------------------------------------------------
1. It is the world's first genuine **POINT-TO-POINT**. Sakura Pass and Shiosai Coast are linear
   roads ridden OUT AND BACK (two legs, ``closed: True``, so the seam is a 180 deg turn at each
   end). Maple City is a truly CLOSED 5 km circuit. Azora is ridden ONCE, end to end, and
   finishes 420 m BELOW where it started and several kilometres away (design doc section 2).

   That means ``closed: False``. ``RouteCourse.Wrap`` clamps an open course instead of looping
   it, which is exactly right for a summit run: you cannot "lap" a point-to-point, and the
   rider should stop at the finish rather than teleport back to the valley. It also means this
   file has NONE of Maple City's closure machinery - no zero-mean grade correction, no closing
   sample, no wrapping finite differences. Ends are CLAMPED, like the open coast road.

2. It is by far the LONGEST route in the game: 24 km against Sakura's 6.7 km network and the
   city's 5 km lap. Sample spacing is therefore 4 m rather than 3 m - 6,000 stations instead of
   8,000 - which keeps the published JSON, the baked ``RouteGraph`` arrays and (most of all)
   the generated road/wall ribbon meshes to a sane size without coarsening any corner that
   matters. The tightest feature up here is a grass hairpin measured in tens of metres, so 4 m
   stations are still ~10 per hairpin.

3. Its ELEVATION is the design, not a by-product. On the other three regions the gradient was
   authored as a grade profile and integrated. Here the design doc pins nine exact (distance,
   elevation) knots - 900 m base, 1,980 m col, 1,560 m finish, 1,080 m of climbing - and those
   numbers ARE the gameplay truth ("do not destroy the route"). So the elevation is
   INTERPOLATED THROUGH THE KNOTS with a shape-preserving monotone cubic (Fritsch-Carlson
   PCHIP), and the grade falls out of it by differentiation. A plain cubic spline would
   overshoot at the cirque lip and invent a false summit above 1,980 m; PCHIP cannot overshoot, so the
   col is exactly the cirque lip and no phase gains a bump the designer did not draw.

MOCK-PASS SCOPE
---------------
The centreline shape and its course definition are real and final-shaped. The PLAN geometry
(how the serpentine is drawn in XZ) is PROVISIONAL - the design fixes the profile, the
checkpoint fractions and the lollipop topology, not the exact metre-by-metre plan.
"""

import json
import math
import os

import numpy as np

# --------------------------------------------------------------------------- identity

REGION_ID = "taka_mountains"
SEGMENT_ID = "taka"
SEGMENT_NAME = "Taka Mountains High Road"
COURSE_ID = "taka_high_road"
COURSE_NAME = "Taka Mountains - The High Road"

# Azora sits in its OWN patch of world space so all four regions share one scene without ever
# touching. Sakura Pass occupies x -285..190 / z -295..900; Shiosai Coast is parked at x=+3000;
# Maple City at x=-3000 (x -3655..-2271). This route's plan is ~6 km across, so it is parked far
# NORTH instead of further along x - that keeps it clear of all three existing regions in both
# axes with kilometres to spare. Fast travel stays a course seek + a transform move.
ORIGIN_X = -12000.0
ORIGIN_Z = 0.0

TARGET_LENGTH_M = 22000.0  # design doc section 2: "22 km point-to-point"
SAMPLE_SPACING = 4.0       # see header note 2
PLAN_RESOLUTION = 8192     # spline samples used to solve the plan before arc-length resampling

ROAD_HALF_WIDTH = 2.6      # "barely two lanes wide" - the narrowest road in the game
SHOULDER_WIDTH = 0.45      # there is no verge on a wall: rock one side, guardrail the other

TARGET_MINUTES = 95.0      # PROVISIONAL: 1,442 m at 11-12% is the hardest ride in the game

BANK_GAIN = 0.85           # radians of curvature -> degrees of bank, before the clamp
MAX_BANK_DEG = 5.5         # cut-and-fill hairpins on a wall carry real camber

# --------------------------------------------------------------------------- design truth

# Named checkpoints at their arc fractions, straight from design doc section 2.
CHECKPOINTS = [
    (0.00, "Trailhead Galleries"),
    (0.20, "Avalanche Gallery"),
    (0.40, "The Stacked Hairpins"),
    (0.55, "North Wall"),
    (0.70, "Kori Lake Summit"),
    (0.88, "Ice-Blue Descent"),
]

# (distance_km, elevation_m) from the design's phase table. These are the gameplay truth: the
# 900 m base, the two 8% ramps, the false-flat col at 1,980 m and the 1,560 m finish all live
# here, and the sum 1,980 - 900 = 1,080 m is the advertised climbing.
ELEVATION_KNOTS = [
    # Straight from design doc section 2. 1,400 m base -> 2,842 m glacial-lake summit at 15 km
    # -> 2,200 m finish at 22 km: 1,442 m of climbing, the most in the game, and a -12% plunge.
    (0.0,  1400.0),   # trailhead, first avalanche gallery      (+5%)
    (2.0,  1500.0),   # end of the approach                     (+5%)
    (4.0,  1690.0),   # lower wall                              (+9..10%)
    (6.0,  1890.0),   # stacked hairpins begin                  (+10%)
    (8.0,  2115.0),   # THE NORTH WALL - sustained              (+11.2%)
    (10.0, 2340.0),   #                                         (+11.3%)
    (12.0, 2560.0),   # top of the wall                         (+11.0%)
    (14.0, 2760.0),   # upper switchbacks                       (+10%)
    (15.0, 2842.0),   # KORI LAKE SUMMIT - the roof of the world (+8%)
    (16.0, 2842.0),   # glacial lake plateau - the only rest     (0%)
    (17.0, 2790.0),   # descent shoulder                         (-5%)
    # As on Azora, the descent is written out as an explicit near-uniform ramp rather than left
    # to one long interval. A shape-preserving cubic reproduces a constant slope exactly, and
    # anything else overshoots: the first cut of this tail hit -19% inside the first 500 m,
    # which on a trainer holding real torque is not "technical", it is a freewheel.
    (17.8, 2694.0),
    (18.6, 2598.0),
    (19.4, 2502.0),
    (20.2, 2406.0),
    (21.1, 2298.0),
    (22.0, 2200.0),   # finish, off the far switchbacks          (design label: -12%)
]

# The PLAN is generated parametrically rather than hand-placed - see build_control_points().
#
# The first cut of this route WAS a hand-placed control polygon read off azora_highlands.png.
# It measured out at a 7.1 x 13.9 km footprint with a 523 m minimum turn radius: geographically
# plausible for 24 km of mountain road, but it produced sweeping motorway bends rather than the
# design's "long open switchback hairpins", and a corridor that size is punishing to dress.
#
# Parametric instead: a sine serpentine climbing the pasture gives explicit control over how
# many switchbacks there are and how tight they turn, and folding the road tightly means 24 km
# of riding fits in a far smaller (and far denser-feeling) piece of hillside. PROVISIONAL.
# TAKA'S PLAN IS A HAIRPIN STACK, NOT A SERPENTINE.
#
# Azora's road wanders across an open hillside; Taka's is pinned to a granite wall and has to
# gain 1,442 m in 15 km, which is only possible by folding back on itself again and again in a
# narrow corridor. So the stem keeps Azora's sine but inverts its proportions: a LARGE lateral
# amplitude against a SMALL northward advance, which is exactly what "hairpins piled visibly one
# above the next" looks like from the valley.
#
# The binding constraint is the reversal radius. For a sine of amplitude A and wavelength L the
# minimum radius is L^2 / (4*pi^2*A), so folding tighter costs radius quadratically. The numbers
# below were chosen against that formula and then checked against the generator's own reported
# minimum radius - a hairpin under about 12 m is not a road, it is a spiral staircase.
# PROVISIONAL.
STEM_NORTH_M = 4300.0     # the wall is only ~4.3 km deep; the road climbs it, it does not cross it
STEM_AMPLITUDE_M = 610.0  # half the lateral swing - wide hairpins stacked up one face
STEM_SWITCHBACKS = 8.25   # the .25 leaves the road heading off-centre over the cirque lip
PLATEAU_RADIUS_M = 290.0  # the short flat arc around Kori Lake - the only rest on the mountain
PLATEAU_SWEEP_DEG = 150.0
DESCENT_M = 4600.0        # the Ice-Blue Descent off the far switchbacks
CONTROL_SPACING_M = 110.0 # analytic shape is sampled this finely into Catmull-Rom controls


def build_control_points():
    """
    The ascent's plan as control points, in metres, before the final scale-to-24 km.

    Three analytic pieces, sampled into control points and then smoothed by the centripetal
    Catmull-Rom below. Sampling-then-splining (rather than concatenating the analytic pieces
    directly) is what keeps the JOINS smooth: a sine meeting a circular arc is continuous in
    position but not in curvature, and a curvature step at the cirque lip would show up as a visible
    crease in the road mesh and a twitch in the banking.
    """
    pts = []

    # 1. The climbing stem: a hairpin stack working north up the granite wall. The amplitude tapers
    #    in at the very bottom so the Trailhead start is a straight run off the valley floor
    #    rather than the rider being dropped mid-corner.
    stem_len = 0.0
    n_stem = int(STEM_NORTH_M / (CONTROL_SPACING_M * 0.55))
    for i in range(n_stem + 1):
        v = i / n_stem
        z = v * STEM_NORTH_M
        taper = min(1.0, v / 0.06) * min(1.0, (1.0 - v) / 0.10 + 0.55)
        x = STEM_AMPLITUDE_M * taper * math.sin(2.0 * math.pi * STEM_SWITCHBACKS * v)
        pts.append((x, z))
    stem_end = pts[-1]

    # 2. The plateau arc: swings east along the summit cirque, past Kori Lake. Centred so
    #    it leaves the stem heading roughly north and finishes heading roughly south-east.
    cx = stem_end[0] + PLATEAU_RADIUS_M
    cz = stem_end[1]
    for i in range(1, int(PLATEAU_SWEEP_DEG / 3.0) + 1):
        a = math.radians(180.0 - i * 3.0)
        pts.append((cx + PLATEAU_RADIUS_M * math.cos(a),
                    cz + PLATEAU_RADIUS_M * math.sin(a)))

    # 3. The descent: off the far shoulder, running south-east to the finish, easing straighter
    #    as it goes - a fast open plunge, not a technical drop.
    ax, az = pts[-1]
    bx, bz = pts[-2]
    hx, hz = ax - bx, az - bz
    h = math.hypot(hx, hz) or 1.0
    hx, hz = hx / h, hz / h
    n_desc = int(DESCENT_M / CONTROL_SPACING_M)
    for i in range(1, n_desc + 1):
        v = i / n_desc
        # Gently unwind the heading eastward as the road straightens out.
        a = math.atan2(hz, hx) + math.radians(38.0) * (v ** 1.6)
        step = DESCENT_M / n_desc
        ax += math.cos(a) * step
        az += math.sin(a) * step
        pts.append((ax, az))

    return pts


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


# --------------------------------------------------------------------------- plan

def _catmull_rom(points, resolution):
    """
    Centripetal Catmull-Rom through the control points, as a dense open polyline.

    Centripetal (alpha = 0.5) rather than uniform parameterisation because uniform Catmull-Rom
    forms cusps and self-intersections exactly where control points bunch up - which on a
    switchback road is everywhere. Centripetal is provably cusp-free.
    """
    p = np.asarray(points, dtype=float)
    # Duplicate the end points so the curve starts and ends AT the first/last control point.
    p = np.vstack([p[:1], p, p[-1:]])

    # Centripetal knot sequence.
    t = [0.0]
    for i in range(1, len(p)):
        t.append(t[-1] + max(np.linalg.norm(p[i] - p[i - 1]), 1e-9) ** 0.5)
    t = np.asarray(t)

    out = []
    per_span = max(4, resolution // (len(p) - 3))
    for i in range(1, len(p) - 2):
        t0, t1, t2, t3 = t[i - 1], t[i], t[i + 1], t[i + 2]
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for k in range(per_span):
            tt = t1 + (t2 - t1) * (k / per_span)
            a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1
            a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2
            a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3
            b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2
            b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3
            out.append((t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2)
    out.append(p[-2])
    return np.asarray(out)


def plan_polyline():
    """The ascent as a dense open XZ polyline, scaled so its length is TARGET_LENGTH_M."""
    dense = _catmull_rom(build_control_points(), PLAN_RESOLUTION)

    seg = np.hypot(np.diff(dense[:, 0]), np.diff(dense[:, 1]))
    length = float(seg.sum())
    scale = TARGET_LENGTH_M / length
    dense = dense * scale

    dense[:, 0] += ORIGIN_X
    dense[:, 1] += ORIGIN_Z
    return dense


def resample_open(p, spacing=SAMPLE_SPACING):
    """
    Resamples an OPEN XZ polyline to (near-)uniform arc spacing.

    Open, not closed: the last station IS the finish line and gets no closing span back to the
    start. This is the one place the point-to-point differs structurally from the city loop.
    """
    seg = np.hypot(np.diff(p[:, 0]), np.diff(p[:, 1]))
    arc = np.concatenate([[0.0], np.cumsum(seg)])
    total = float(arc[-1])

    n = max(2, int(round(total / spacing)) + 1)
    want = np.linspace(0.0, total, n)
    x = np.interp(want, arc, p[:, 0])
    z = np.interp(want, arc, p[:, 1])
    return np.stack([x, z], axis=1), total


# --------------------------------------------------------------------------- elevation

def _pchip(xk, yk, x):
    """
    Fritsch-Carlson shape-preserving monotone cubic interpolation.

    Hand-rolled so this file keeps its "numpy only, no scipy" promise. The property that matters
    is NO OVERSHOOT: a natural cubic spline through these knots puts a false summit ABOVE the
    1,980 m col (because it must curve to meet the -3% plateau smoothly), which would invent a
    higher point than the design's headline elevation. PCHIP clamps the slopes so every
    interval stays monotone between its own knots, so the cirque lip is the highest point, exactly.
    """
    xk = np.asarray(xk, dtype=float)
    yk = np.asarray(yk, dtype=float)
    h = np.diff(xk)
    delta = np.diff(yk) / h

    n = len(xk)
    d = np.zeros(n)
    # Interior slopes: weighted harmonic mean, zeroed at any local extremum.
    for i in range(1, n - 1):
        if delta[i - 1] * delta[i] <= 0.0:
            d[i] = 0.0
        else:
            w1 = 2.0 * h[i] + h[i - 1]
            w2 = h[i] + 2.0 * h[i - 1]
            d[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i])
    # One-sided ends (Fritsch-Carlson end rule), clamped so the ends cannot overshoot either.
    def end_slope(hs, ds, other):
        s = ((2.0 * hs[0] + hs[1]) * ds[0] - hs[0] * ds[1]) / (hs[0] + hs[1])
        if s * ds[0] <= 0.0:
            return 0.0
        if abs(s) > 3.0 * abs(ds[0]):
            return 3.0 * ds[0]
        return s
    d[0] = end_slope(h, delta, d[1])
    d[-1] = end_slope(h[::-1], delta[::-1], d[-2])

    idx = np.clip(np.searchsorted(xk, x) - 1, 0, n - 2)
    hs = h[idx]
    t = (x - xk[idx]) / hs
    t2, t3 = t * t, t * t * t
    h00 = 2 * t3 - 3 * t2 + 1
    h10 = t3 - 2 * t2 + t
    h01 = -2 * t3 + 3 * t2
    h11 = t3 - t2
    return (h00 * yk[idx] + h10 * hs * d[idx] +
            h01 * yk[idx + 1] + h11 * hs * d[idx + 1])


def smooth_open(values, window_samples):
    """
    Moving average with SLOPE-PRESERVING (odd-reflected) ends.

    Deliberately not the city's circular smoother: wrapping a point-to-point would drag the
    -14% finish descent into the +2.7% valley start and put a phantom kink in both.

    The padding is an odd reflection (2*y[0] - y[k]), not the obvious edge replication. Edge
    replication implicitly asserts the road is FLAT just beyond each end, so it drags the last
    few samples uphill towards the mean - which on a -14% finish lifted the final station by
    most of a metre. Re-pinning the exact design elevation afterwards then turned that error
    into a one-sample cliff, and np.gradient read the cliff as a -24.7% wall across the finish
    line: a spike that existed in no knot, was invisible in the elevation plot, and would have
    slammed the trainer's resistance to maximum on the last pedal stroke. Odd reflection
    continues the end SLOPE instead of flattening it, so the ends survive smoothing intact and
    need no re-pin at all.
    """
    w = max(1, int(window_samples) | 1)
    pad = w // 2
    head = 2.0 * values[0] - values[1:pad + 1][::-1]
    tail = 2.0 * values[-1] - values[-pad - 1:-1][::-1]
    padded = np.concatenate([head, values, tail])
    kernel = np.ones(w) / w
    return np.convolve(padded, kernel, mode="valid")


def elevation_profile(arc, total):
    """Elevation and grade at every station, interpolated THROUGH the design's knots."""
    # The knots are authored in DESIGN kilometres; the built road is TARGET_LENGTH_M long and the
    # resampled plan is a few metres off that again, so the knot axis is normalised by the LAST
    # KNOT rather than by any literal. Azora hard-coded its own 24.0 here, and carrying that
    # constant into a 22 km region compressed every knot by 8.3%: the 2,842 m summit landed at
    # 13.79 km instead of 15.0, and the finish came in 178 m below the design's 2,200 m while
    # every other number in the report still looked plausible.
    span_km = float(ELEVATION_KNOTS[-1][0])
    xk = [k * TARGET_LENGTH_M / span_km for k, _ in ELEVATION_KNOTS]  # km -> m along the route
    yk = [e for _, e in ELEVATION_KNOTS]
    y = _pchip(xk, yk, arc)

    # Light smoothing so no knot is a visible crease in the road surface, then the grade is
    # differentiated from the SMOOTHED elevation - that way the trainer and the mesh agree.
    # No re-pinning of the endpoints or the cirque lip afterwards: with slope-preserving padding the
    # smoother leaves all three headline elevations where PCHIP put them (to the centimetre),
    # and forcing them back was what manufactured the finish-line grade cliff in the first
    # place. The build report below prints the actual values so the claim is checked, not
    # assumed.
    y = smooth_open(y, 9)

    grade = np.gradient(y, arc)
    grade = smooth_open(grade, 21)
    return y, grade


# --------------------------------------------------------------------------- frames

def frames(spacing=SAMPLE_SPACING):
    """
    Position / tangent / side / up / bank / arc for every station.

    Same convention as the other three regions - cross(up, tangent) is the rider's RIGHT - but
    every finite difference CLAMPS at the ends (np.gradient's one-sided rule) instead of
    wrapping, because this road genuinely has two ends.
    """
    dense = plan_polyline()
    plan, _ = resample_open(dense, spacing)

    xz = plan
    d = np.hypot(*(np.diff(xz, axis=0)).T)
    arc = np.concatenate([[0.0], np.cumsum(d)])
    total = float(arc[-1])

    y, grade = elevation_profile(arc, total)
    p = np.stack([xz[:, 0], y, xz[:, 1]], axis=1)

    tangents = np.gradient(p, axis=0)
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)

    world_up = np.array([0.0, 1.0, 0.0])
    sides = np.cross(world_up, tangents)
    sides /= np.maximum(np.linalg.norm(sides, axis=1, keepdims=True), 1e-9)

    prev_t = np.vstack([tangents[:1], tangents[:-1]])
    next_t = np.vstack([tangents[1:], tangents[-1:]])
    cross = np.cross(prev_t, next_t)
    span = np.maximum(np.linalg.norm(
        np.vstack([p[1:2], p[2:], p[-1:]]) - np.vstack([p[:1], p[:-2], p[-2:-1]]), axis=1), 1e-6)
    curvature = smooth_open(cross @ world_up / span, 9)

    banks = np.clip(curvature * BANK_GAIN * 180.0, -MAX_BANK_DEG, MAX_BANK_DEG)
    banks = smooth_open(banks, 9)

    ups = np.zeros_like(p)
    for i in range(len(p)):
        roll = math.radians(banks[i])
        s = sides[i]
        u = np.cross(tangents[i], s)
        u /= max(np.linalg.norm(u), 1e-9)
        ups[i] = u * math.cos(roll) + s * math.sin(roll)
        sides[i] = s * math.cos(roll) - u * math.sin(roll)

    return p, tangents, sides, ups, banks, arc, total, grade


# --------------------------------------------------------------------------- publish

def write_route_json():
    p, t, s, u, bank, arc, total, grade = frames()

    samples = [{
        "p": [round(float(v), 4) for v in p[i]],
        "t": [round(float(v), 4) for v in t[i]],
        "s": [round(float(v), 4) for v in s[i]],
        "u": [round(float(v), 4) for v in u[i]],
        "bank": round(float(bank[i]), 3),
        "d": round(float(arc[i]), 3),
    } for i in range(len(p))]

    # NO closing sample. The city loop needs one because its last station's neighbour is station
    # 0; an open road's last station is simply the finish line.

    out_dir = os.path.join(repo_root(), "Assets", "Environment", "TakaMountains")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "TakaRoute.json")

    payload = {
        "_comment": "Generated by tools/blender/azora_route.py - do not edit by hand.",
        "regionId": REGION_ID,
        "roadHalfWidth": ROAD_HALF_WIDTH,
        "shoulderWidth": SHOULDER_WIDTH,
        "seaLevel": 0.0,
        "samples": samples,
        "segments": [{"id": SEGMENT_ID, "name": SEGMENT_NAME, "samples": samples}],
        # ONE leg, ridden ONCE, OPEN. This is the first course in the game that is not lapped:
        # RouteCourse.Wrap clamps an open course, so the rider tops out at the finish instead of
        # being teleported back to the valley floor. RideSession still derives a lap count from
        # the target duration, but for a 24 km point-to-point that resolves to a single pass.
        "courses": [{
            "id": COURSE_ID,
            "name": COURSE_NAME,
            "closed": False,
            "targetMinutes": TARGET_MINUTES,
            "legs": [{"segment": SEGMENT_ID, "from": 0.0, "to": round(total, 3)}],
        }],
        "checkpoints": [
            {"segment": SEGMENT_ID, "distance": round(f * total, 2), "name": n}
            for f, n in CHECKPOINTS
        ],
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=1)

    # ---------------------------------------------------------------- build report
    # Verified here rather than trusted. A point-to-point can fail in ways a loop cannot: the
    # profile can miss its advertised col, the ascent can not add up to 1,080 m, or the plan can
    # double back through itself on a switchback.
    ascent = float(np.sum(np.clip(np.diff(p[:, 1]), 0.0, None)))
    descent = float(-np.sum(np.clip(np.diff(p[:, 1]), None, 0.0)))
    col_i = int(np.argmax(p[:, 1]))

    print(f"[taka] wrote {path}")
    print(f"[taka]   {len(samples)} samples, {total / 1000.0:.3f} km point-to-point, "
          f"spacing {total / (len(samples) - 1):.2f} m")
    print(f"[taka]   elevation {p[:, 1].min():.1f} - {p[:, 1].max():.1f} m, "
          f"start {p[0, 1]:.1f} m, col {p[col_i, 1]:.1f} m at "
          f"{arc[col_i] / 1000.0:.2f} km ({arc[col_i] / total:.3f} arc), finish {p[-1, 1]:.1f} m")
    print(f"[taka]   ascent +{ascent:.0f} m / descent -{descent:.0f} m "
          f"(design: +1,080 m gross)")
    print(f"[taka]   grade {grade.min() * 100:.2f}% .. {grade.max() * 100:.2f}%")

    # Fraction of the route inside the design's signature sustained band.
    sustained = float(np.mean((grade >= 0.045) & (grade <= 0.070)))
    ramps = float(np.mean(grade >= 0.075))
    print(f"[taka]   {sustained * 100:.1f}% of the route sits in the 4.5-7.0% sustained band, "
          f"{ramps * 100:.1f}% on the 8% ramps")

    # Plan sanity: the tightest turn the rider will actually steer.
    dx = np.gradient(p[:, 0]); dz = np.gradient(p[:, 2])
    ddx = np.gradient(dx); ddz = np.gradient(dz)
    k = np.abs(dx * ddz - dz * ddx) / np.maximum((dx * dx + dz * dz) ** 1.5, 1e-9)
    radii = 1.0 / np.maximum(k, 1e-9)
    print(f"[taka]   plan bounds x {p[:, 0].min():.0f}..{p[:, 0].max():.0f}, "
          f"z {p[:, 2].min():.0f}..{p[:, 2].max():.0f}")
    print(f"[taka]   tightest turn radius {np.percentile(radii, 0.5):.0f} m, "
          f"median {np.median(radii):.0f} m")
    print(f"[taka]   max bank {np.abs(bank).max():.2f} deg")
    print("[taka]   checkpoints:")
    for f_, n in CHECKPOINTS:
        i = int(np.clip(round(f_ * (len(p) - 1)), 0, len(p) - 1))
        print(f"[taka]     {f_:.2f}  {arc[i] / 1000.0:6.2f} km  {p[i, 1]:7.1f} m  {n}")


if __name__ == "__main__":
    write_route_json()

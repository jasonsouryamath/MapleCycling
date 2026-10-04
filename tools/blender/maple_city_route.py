"""
Single source of truth for the MAPLE CITY route ("The Heart").

Publishes ``Assets/Environment/MapleCity/MapleRoute.json`` in exactly the schema
``sakura_route.write_route_json()`` / ``shiosai_route.write_route_json()`` use, so
``RouteGraphBaker`` merges a third region into the one runtime ``RouteGraph`` asset and every
ride system (physics, HUD, GPS window, follower, dressing streamer, trainer grade) works in the
city with NO new code paths.

Deliberately standalone: it needs no ``bpy`` and no ``sakura_lib`` (which does import ``bpy``),
so it runs under Blender's Python *or* any Python with numpy:

    blender -b -P maple_city_route.py
    python maple_city_route.py

All coordinates are **Unity** world space (Y up, metres).

WHAT MAKES THIS ROUTE DIFFERENT FROM THE OTHER TWO
--------------------------------------------------
Sakura Pass and Shiosai Coast are both *linear* roads ridden out and back; their "closed"
course is a there-and-back-again whose seam is a 180 deg turn at each end. Maple City is the
world's only genuinely **closed 5.0 km criterium loop** (design doc section 2), which imposes
two constraints neither of the other regions had to satisfy:

  1. The PLAN must close smoothly AND must read as a CITY. Those pull in opposite directions.
     The first cut of this route used a periodic Fourier radius, r(theta) = R0 * (1 + sum
     a_k cos(k*theta + phi_k)), which is closed and C-infinity by construction - but measured
     out at a 372 m minimum turn radius with a 765 m median, i.e. a gigantic smooth oval. No
     amount of buildings would have made that read as a criterium: a city circuit is
     STRAIGHT AVENUES joined by CORNERS, and a Fourier loop cannot produce a straight.

     So the plan is authored the way a real crit circuit is laid out: a closed POLYGON of
     avenues, with every vertex FILLETED by a circular arc of its own radius. Tangent
     continuity is exact (an arc's tangent at its tangency point is the avenue direction, by
     construction), so the seam and every corner are smooth; curvature is piecewise constant,
     which is what a real road is, and the banking filter smooths its steps. That buys long
     boulevards, four genuine 35-46 m Old Town corners and wide 110-155 m boulevard sweeps.

  2. The ELEVATION must close too. The rider finishes every lap where they started, so the
     grade profile has to integrate to exactly zero over the lap or the road would step
     vertically at the start/finish line. The design's four phases (flat canal, +7% Old Town
     ramp, -6% boulevard descent, gentle run-in) sum to about +15 m of drift, so the mean grade
     is subtracted off before integration. That is a 0.3% shift - it does not meaningfully
     change any authored gradient, and it is the ONLY honest way to close the loop.

MOCK-PASS SCOPE
---------------
The centreline, its elevation profile and its course definition are real and final-shaped, but
every number below is PROVISIONAL tuning that the design handoff explicitly leaves open (route
metrics are an unresolved decision, context section 12). Nothing here is a final requirement.
"""

import json
import math
import os

import numpy as np

# --------------------------------------------------------------------------- provisional tuning

REGION_ID = "maple_city"
SEGMENT_ID = "maplecity"
SEGMENT_NAME = "Maple City Circuit"
COURSE_ID = "maple_city_crit"
COURSE_NAME = "Maple City Crit"

# Maple City sits in its OWN patch of world space so all three regions share one scene without
# ever touching. Sakura Pass occupies x -285..190 / z -295..900; Shiosai Coast is parked at
# x = +3000. The city loop is ~1.6 km across, so -3000 gives it a clear 2 km moat on every side.
# Fast travel therefore stays a course seek + a transform move - never a scene load.
ORIGIN_X = -3000.0
ORIGIN_Z = 0.0

# Output file inside Assets/Environment/MapleCity. Overridden by maple_city_east_route.py for the second district.
OUT_FILE = "MapleRoute.json"

TARGET_LENGTH_M = 5000.0   # design doc section 2: "5.0 km closed criterium"
SAMPLE_SPACING = 3.0       # matches SakuraRoute.json and ShiosaiRoute.json
PLAN_RESOLUTION = 4096     # theta samples used to solve the plan before arc-length resampling

ROAD_HALF_WIDTH = 3.5      # same 7 m nominal carriageway as the other two regions.
SHOULDER_WIDTH = 0.55
# NOTE: the design calls for road width to VARY (wide boulevard <-> 4 m cobbled street). That is
# deliberately a *rendering* property owned by MapleCityEnvironment's width profile, not a route
# property: the published half width stays 3.5 m so the rider's lane offset, the NPC lane
# offsets and the physics cross-section remain identical in every region.

# --- plan (the loop shape) --------------------------------------------------------------------
# The city circuit as (x, z, fillet radius) avenue corners, in ride order, in DRAFT metres.
# The whole polygon is uniformly rescaled at build time so the finished lap is exactly
# TARGET_LENGTH_M, so only the *proportions* here matter; the printed build report gives the
# real, scaled radius of every corner.
#
# The corner characters are chosen straight off design doc sections 2 and 4:
#   0  Maple Gate Plaza     wide sweeping plaza turn onto the canal - the start/finish
#   1  Canal head           long lazy bend where the canal path meets the old town
#   2  Old Town entry       the road narrows; first of the tight cobbled corners
#   3  Ramp corner          TIGHTEST corner on the circuit, taken at +7% out of the saddle
#   4  Terrace corner       second tight cobbled corner, at the crest by the Sky Terrace
#   5  Boulevard drop       wide fast turn onto the -6% descent - a sweeper, not a hairpin
#   6  Ginkgo turn          broad tree-lined boulevard bend
#   7  Bridge approach      long arc onto the river bridge
#   8  Riverside            final bend back to the plaza; sets up the finish drag
#
# PROVISIONAL: these are art/gameplay proportions, not surveyed data.
CIRCUIT = [
    (   0.0,    0.0, 140.0),
    (  60.0,  900.0,  90.0),
    ( 330.0, 1180.0,  45.0),
    ( 820.0, 1160.0,  38.0),
    (1150.0,  880.0,  34.0),
    (1280.0,  420.0, 150.0),
    ( 980.0, -260.0, 110.0),
    ( 430.0, -520.0, 120.0),
    (-120.0, -300.0, 130.0),
]

# Which corner the start/finish line sits on. The lap is rolled so arc 0 is the MIDDLE of this
# corner's fillet, which is what puts the finish arch in Maple Gate Plaza rather than 175 m up
# the canal - and, by the same roll, lands every other landmark within ~0.01 of the arc
# fraction the design doc assigns it.
START_CORNER = 0

# Arc step used when tessellating the fillet arcs, metres. Finer than SAMPLE_SPACING so the
# tight 35 m corners are solved accurately before the uniform arc-length resample.
FILLET_STEP = 1.0

# --- vertical profile -------------------------------------------------------------------------
# Design doc section 2, expressed as (arc fraction start, arc fraction end, grade at start,
# grade at end) in PERCENT. Grades are linearly interpolated inside each phase and then smoothed
# circularly, so the trainer never sees a step change in resistance at a phase boundary.
#
#   0.00-0.38  Canal flat          0 -> +1%    dead-flat canal warmup, false-flat drag
#   0.38-0.52  Old Town Ramps     +6 -> +9%    the one real climb, ~55 m in 700 m of cobbles
#   0.52-0.62  Boulevard descent  -6 -> -6%    fast wide tram-tracked drop off the terrace
#   0.62-1.00  Boulevard run-in   -1 ->  0%    gently downhill sprint drag to the River Bridge
GRADE_PHASES = [
    (0.000, 0.365,  0.0,  1.0),
    (0.365, 0.500,  6.0,  9.0),
    (0.500, 0.600, -6.0, -6.0),
    (0.600, 1.000, -1.0,  0.0),
]
# Circular smoothing window for the grade profile, in metres of arc. 90 m is roughly three
# seconds at crit speed: long enough to kill the phase-boundary steps, short enough that the
# +9% pitch of the Old Town Ramps survives essentially intact.
GRADE_SMOOTH_M = 90.0

BASE_HEIGHT = 40.0         # design: "valley floor ~40 m base" - the low point of the loop

# --- banking ----------------------------------------------------------------------------------
# A crit corners harder than a mountain pass but on FLAT city asphalt, so the banking is a
# camera/feel cue only and is held tighter than Shiosai's 5 deg.
BANK_GAIN = 0.45
MAX_BANK_DEG = 3.5

TARGET_MINUTES = 30        # PROVISIONAL session length; matches the other two regions' default

# (fraction along the segment, name). The GPS window pins these and the RouteDirector fires
# arrival banners off them, exactly as on the pass. Straight from design doc section 2.
CHECKPOINTS = [
    (0.00, "Maple Gate Plaza"),
    (0.16, "Canal Sprint"),
    (0.36, "Old Town Ramps"),
    (0.52, "Sky Terrace Overlook"),
    (0.74, "Ginkgo Boulevard"),
    (0.92, "River Bridge Finish"),
]


def repo_root():
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", ".."))


# --------------------------------------------------------------------------- plan

def _fillet_corner(prev_p, p, next_p, radius_m):
    """
    Solves one filleted corner: the arc of radius <= radius_m tangent to both avenues.

    Returns (entry tangency point, exit tangency point, arc points, achieved radius).
    The requested radius is CLAMPED so the two tangent lengths never eat more than 45% of
    either adjacent avenue - otherwise two neighbouring wide corners on a short avenue would
    overlap and the polyline would fold back through itself.
    """
    u = prev_p - p
    u /= np.linalg.norm(u)
    w = next_p - p
    w /= np.linalg.norm(w)

    interior = math.acos(max(-1.0, min(1.0, float(u @ w))))
    half = interior / 2.0
    if half < 1e-4 or abs(math.pi - interior) < 1e-4:
        return p.copy(), p.copy(), [p.copy()], 0.0

    tangent_len = radius_m / math.tan(half)
    tangent_len = min(tangent_len,
                      float(np.linalg.norm(prev_p - p)) * 0.45,
                      float(np.linalg.norm(next_p - p)) * 0.45)
    r = tangent_len * math.tan(half)

    t_in = p + u * tangent_len
    t_out = p + w * tangent_len
    bisector = u + w
    bisector /= np.linalg.norm(bisector)
    centre = p + bisector * (r / math.sin(half))

    a0 = math.atan2(t_in[1] - centre[1], t_in[0] - centre[0])
    a1 = math.atan2(t_out[1] - centre[1], t_out[0] - centre[0])
    sweep = (a1 - a0) % (2.0 * math.pi)
    if sweep > math.pi:
        sweep -= 2.0 * math.pi

    steps = max(2, int(abs(sweep) * r / FILLET_STEP))
    arc = [centre + r * np.array([math.cos(a0 + sweep * s / steps),
                                  math.sin(a0 + sweep * s / steps)])
           for s in range(steps + 1)]
    return t_in, t_out, arc, r


def plan_polyline():
    """
    The circuit as a dense closed XZ polyline, scaled so its perimeter is TARGET_LENGTH_M and
    rolled so index 0 sits in the middle of the START_CORNER fillet.

    Also returns per-corner diagnostics so the build report can state the REAL radius and arc
    fraction of every corner rather than the requested one.
    """
    pts = [np.array([x, z], dtype=np.float64) for x, z, _ in CIRCUIT]
    radii = [r for _, _, r in CIRCUIT]
    n = len(pts)

    solved = [_fillet_corner(pts[i - 1], pts[i], pts[(i + 1) % n], radii[i]) for i in range(n)]

    out = []
    corner_index = []          # polyline index of each corner's arc MIDPOINT
    achieved = []
    for i in range(n):
        t_in, t_out, arc, r = solved[i]
        prev_out = solved[i - 1][1]
        # the straight avenue from the previous corner's exit to this corner's entry
        span = float(np.linalg.norm(t_in - prev_out))
        steps = max(1, int(span / FILLET_STEP))
        for s in range(steps):
            out.append(prev_out + (t_in - prev_out) * (s / steps))
        corner_index.append(len(out) + len(arc) // 2)
        out.extend(arc[:-1])   # drop the last point: it IS the next avenue's first point
        achieved.append(r)

    q = np.array(out)

    # Uniform rescale about the circuit's own centroid, THEN translate to the region origin, so
    # the loop is centred on (ORIGIN_X, ORIGIN_Z) at whatever length the design asks for.
    seg = np.linalg.norm(np.roll(q, -1, axis=0) - q, axis=1)
    scale = TARGET_LENGTH_M / float(seg.sum())
    centroid = q.mean(axis=0)
    q = (q - centroid) * scale
    achieved = [r * scale for r in achieved]

    roll = corner_index[START_CORNER] % len(q)
    q = np.roll(q, -roll, axis=0)
    corner_index = [(c - roll) % len(q) for c in corner_index]

    q[:, 0] += ORIGIN_X
    q[:, 1] += ORIGIN_Z
    return q, corner_index, achieved


def resample_closed(p, spacing=SAMPLE_SPACING):
    """
    Resamples a closed XZ polyline to (near-)uniform arc spacing.

    The sample count is rounded so that spacing divides the loop EXACTLY, which is what keeps
    the seam span the same length as every other span. A closed course whose last span is a
    different length is how a lap counter ends up half a metre out after twenty laps.
    """
    closed = np.vstack([p, p[:1]])
    seg = np.hypot(np.diff(closed[:, 0]), np.diff(closed[:, 1]))
    arc = np.concatenate([[0.0], np.cumsum(seg)])
    total = arc[-1]

    count = max(64, int(round(total / spacing)))
    want = np.linspace(0.0, total, count, endpoint=False)
    x = np.interp(want, arc, closed[:, 0])
    z = np.interp(want, arc, closed[:, 1])
    return np.stack([x, z], axis=1), total


# --------------------------------------------------------------------------- vertical

def grade_profile(frac):
    """Design grade in percent at a normalised arc fraction, before closure correction."""
    f = frac % 1.0
    for lo, hi, g_lo, g_hi in GRADE_PHASES:
        if lo <= f < hi:
            t = (f - lo) / (hi - lo)
            return g_lo + (g_hi - g_lo) * t
    return GRADE_PHASES[-1][3]


def circular_smooth(values, window_samples):
    """Box smoothing that WRAPS, so the start/finish line is not a discontinuity."""
    w = max(1, int(window_samples) | 1)           # force odd so the window is centred
    if w <= 1:
        return values.copy()
    kernel = np.ones(w) / w
    pad = w // 2
    padded = np.concatenate([values[-pad:], values, values[:pad]])
    return np.convolve(padded, kernel, mode="valid")


def elevation_profile(arc, total):
    """
    Integrates the design's grade profile into a CLOSED elevation curve.

    Three steps, in this order and for these reasons:
      1. sample the authored grade phases at every station,
      2. smooth circularly so no phase boundary is a step (the trainer would feel it as a jolt),
      3. subtract the mean so the integral over one lap is exactly zero - without this the road
         steps vertically at the start/finish line by the ~15 m the four phases fail to balance.
    """
    frac = arc / total
    grade = np.array([grade_profile(f) for f in frac]) / 100.0
    spacing = total / len(arc)
    grade = circular_smooth(grade, round(GRADE_SMOOTH_M / spacing))

    drift = float(np.mean(grade))
    grade = grade - drift                          # closure correction

    y = np.concatenate([[0.0], np.cumsum(grade[:-1] * np.diff(arc))])
    y = y - float(np.min(y)) + BASE_HEIGHT
    return y, grade, drift


# --------------------------------------------------------------------------- frames

def frames(spacing=SAMPLE_SPACING):
    """
    Position / tangent / side / up / bank / arc for every station on the closed loop.

    Same convention as the other two regions - cross(up, tangent) is the rider's RIGHT - but
    every finite difference here WRAPS via np.roll instead of clamping at the ends, because on a
    closed loop the last station's neighbour is the first station. Clamping (what
    shiosai_route.frames does, correctly, for an open road) would leave the seam with a
    one-sided tangent and a curvature spike.
    """
    dense, corner_index, corner_radii = plan_polyline()
    # Corner arc fractions, measured on the dense plan before the uniform resample.
    dseg = np.linalg.norm(np.roll(dense, -1, axis=0) - dense, axis=1)
    darc = np.concatenate([[0.0], np.cumsum(dseg[:-1])])
    corner_fracs = [float(darc[c] / dseg.sum()) for c in corner_index]

    plan, plan_total = resample_closed(dense, spacing)
    n = len(plan)
    # Arc length measured on the FINAL stations, not on the dense solving polyline.
    xz = plan
    d = np.hypot(*(np.roll(xz, -1, axis=0) - xz).T)
    arc = np.concatenate([[0.0], np.cumsum(d[:-1])])
    total = float(arc[-1] + d[-1])                 # includes the closing span

    y, grade, drift = elevation_profile(arc, total)

    p = np.stack([xz[:, 0], y, xz[:, 1]], axis=1)

    tangents = np.roll(p, -1, axis=0) - np.roll(p, 1, axis=0)
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)

    world_up = np.array([0.0, 1.0, 0.0])
    sides = np.cross(world_up, tangents)
    sides /= np.maximum(np.linalg.norm(sides, axis=1, keepdims=True), 1e-9)

    # Signed curvature about world up, smoothed circularly - this is what banks the corners.
    prev_t = np.roll(tangents, 1, axis=0)
    next_t = np.roll(tangents, -1, axis=0)
    cross = np.cross(prev_t, next_t)
    span = np.linalg.norm(np.roll(p, -1, axis=0) - np.roll(p, 1, axis=0), axis=1)
    curvature = circular_smooth(cross @ world_up / np.maximum(span, 1e-6), 9)

    banks = np.clip(curvature * BANK_GAIN * 180.0, -MAX_BANK_DEG, MAX_BANK_DEG)
    banks = circular_smooth(banks, 9)

    ups = np.zeros_like(p)
    for i in range(n):
        roll = math.radians(banks[i])
        s = sides[i]
        u = np.cross(tangents[i], s)
        u /= max(np.linalg.norm(u), 1e-9)
        ups[i] = u * math.cos(roll) + s * math.sin(roll)
        sides[i] = s * math.cos(roll) - u * math.sin(roll)

    return p, tangents, sides, ups, banks, arc, total, grade, drift, corner_fracs, corner_radii


# --------------------------------------------------------------------------- publish

def write_route_json():
    (p, t, s, u, bank, arc, total, grade, drift,
     corner_fracs, corner_radii) = frames()

    samples = [{
        "p": [round(float(v), 4) for v in p[i]],
        "t": [round(float(v), 4) for v in t[i]],
        "s": [round(float(v), 4) for v in s[i]],
        "u": [round(float(v), 4) for v in u[i]],
        "bank": round(float(bank[i]), 3),
        "d": round(float(arc[i]), 3),
    } for i in range(len(p))]

    # CLOSING SAMPLE. RouteCourse flattens a leg into an open polyline and measures arc length
    # between consecutive samples, so a leg of 0..arc[-1] would stop one span SHORT of the start
    # and the lap would be 3 m under. Repeating station 0 at d = total gives the flattened course
    # a real closing span; RouteCourse's own duplicate-vertex guard is keyed on position, and
    # these two positions are identical, so it drops the duplicate *after* the span is counted.
    closing = dict(samples[0])
    closing["d"] = round(float(total), 3)
    samples_closed = samples + [closing]

    out_dir = os.path.join(repo_root(), "Assets", "Environment", "MapleCity")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, OUT_FILE)

    payload = {
        "_comment": "Generated by tools/blender/maple_city_route.py - do not edit by hand.",
        "regionId": REGION_ID,
        "roadHalfWidth": ROAD_HALF_WIDTH,
        "shoulderWidth": SHOULDER_WIDTH,
        "seaLevel": 0.0,
        "samples": samples_closed,
        "segments": [{"id": SEGMENT_ID, "name": SEGMENT_NAME, "samples": samples_closed}],
        # ONE leg, all the way round, closed. Unlike the other two regions this needs no
        # out-and-back: the city circuit genuinely returns to Maple Gate Plaza, so lap 2 simply
        # continues past the finish arch. Lap count is still derived from the rider's target
        # duration (RideSession.autoLapsFromTarget), giving the design's 1 / 2 / 3 lap formats.
        "courses": [{
            "id": COURSE_ID,
            "name": COURSE_NAME,
            "closed": True,
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
    # Verified here rather than trusted: a closed loop can fail in ways an open road cannot.
    real_grades = []
    for i in range(len(p)):
        j = (i + 1) % len(p)
        run = math.hypot(p[j][0] - p[i][0], p[j][2] - p[i][2])
        if run > 0.5:
            real_grades.append((p[j][1] - p[i][1]) / run * 100.0)
    seam_dy = abs(float(p[0][1] - p[-1][1] - grade[-1] * SAMPLE_SPACING))

    print(f"[maple] wrote route definition -> {path}")
    print(f"[maple]   {len(samples_closed)} samples, lap {total / 1000.0:.3f} km, "
          f"{len(CIRCUIT)} filleted corners")
    print(f"[maple]   elevation {p[:, 1].min():.1f} - {p[:, 1].max():.1f} m "
          f"(+{p[:, 1].max() - p[:, 1].min():.1f} m of relief)")
    print(f"[maple]   grade {min(real_grades):.2f} % .. {max(real_grades):.2f} % "
          f"(closure correction {drift * 100:.3f} %)")
    print(f"[maple]   seam elevation error {seam_dy * 100:.2f} cm, "
          f"plan seam gap {math.dist(p[0][::2], p[-1][::2]):.2f} m")
    print(f"[maple]   bounds x {p[:, 0].min():.0f}..{p[:, 0].max():.0f}  "
          f"z {p[:, 2].min():.0f}..{p[:, 2].max():.0f}")
    print("[maple]   corners:")
    for i, (f, r) in enumerate(zip(corner_fracs, corner_radii)):
        print(f"[maple]     corner {i}  R {r:6.1f} m  at {f * total / 1000.0:5.2f} km "
              f"(frac {f:.3f})")
    print("[maple]   checkpoints:")
    for f, n in CHECKPOINTS:
        i = min(int(f * len(p)), len(p) - 1)
        print(f"[maple]     {f * total / 1000.0:5.2f} km  y {p[i][1]:5.1f} m  {n}")
    return path


if __name__ == "__main__":
    write_route_json()

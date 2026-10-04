"""
Single source of truth for the Sakura Pass route.

Both the Blender landscape/road builders and the Unity staging code need the exact same centreline,
otherwise the terrain carve does not line up with the asphalt. The control points here mirror
``MapleRideKuroSetup.GetRoadPoints()``; ``write_route_json()`` publishes them into the Unity project
so the C# side can load them instead of keeping a second hard-coded copy.

All coordinates in this module are **Unity** world space (Y up).
"""

import json
import math
import os

import numpy as np

from sakura_lib import repo_root


# Unity-space control points: (x, y, z) - a 3 km alpine pass climbing ~36 m over ~600 m of run.
#
# ELEVATION RE-CUT (2026-09-13).  The climb used to deliver its honest 6.7 % average as a
# 37-40 % wall between arc 400 and 475 m (control points 15-19): fine to look at, impossible to
# ride once watts->speed physics sits on top of it, because the rider pins to the minimum-speed
# clamp for 75 m.  Only the ``y`` values of indices 0-21 were re-cut; every ``x``/``z`` and the
# 36.5/36.8 m summit are byte-identical, so the terrain carve, the tunnel bore, the hillside
# lettering, the guardrail runs and every ``ClimbLength``-anchored dressing window stay exactly
# where they were.  Shape: ease in, hold ~10 %, ease back onto the crest shelf.
CONTROL_POINTS = [
    (-2.0, 0.00, -200.0),
    (0.0, 0.24, -175.0),
    (5.0, 0.59, -150.0),
    (-2.0, 1.13, -125.0),
    (3.0, 1.91, -100.0),
    (9.0, 3.03, -74.0),
    (13.0, 4.48, -48.0),
    (7.0, 6.29, -22.0),
    (14.0, 8.53, 5.0),
    (10.0, 11.01, 32.0),
    (17.0, 13.87, 60.0),
    (8.0, 17.10, 90.0),
    (-4.0, 20.45, 120.0),
    (-16.0, 23.79, 150.0),
    (-10.0, 26.55, 176.0),
    (-4.0, 28.65, 196.0),
    (2.0, 30.41, 214.0),
    (6.0, 31.94, 232.0),
    (2.0, 33.14, 248.0),
    (-6.0, 34.27, 264.0),
    (-2.0, 35.41, 285.0),
    (8.0, 36.50, 306.0),
    # ---- summit crest shelf: the pass tops out and runs level past the viewpoint ----
    (16.0, 36.80, 326.0),
    (16.0, 36.40, 348.0),
    # ---- switchback descent: the pass falls away from the crest overlook ----------
    # The first version of the far side left the crest on a 4% traverse, which from the
    # overlook read as "the road carries on round the headland" rather than "the pass drops
    # away beneath you".  The descent now sheds its first 20 m of altitude through two
    # stacked 180-degree hairpins on a single flank, at roughly 6.5%, so a rider standing at
    # the viewpoint looks straight down onto the limb they are about to ride.
    #
    # Limb separation is the constraint that shapes these numbers: the terrain builder holds a
    # 7.4 m flat verge either side of every centreline and then batters away, so two limbs
    # closer than ~25 m in plan with more than ~8 m of height between them come out as a
    # vertical wall instead of a slope.  Every pair below clears that.
    # -- limb A: leaves the crest shelf heading west-south-west --
    (2.0, 34.40, 370.0),
    (-18.0, 32.20, 386.0),
    (-42.0, 30.20, 397.0),
    (-62.0, 28.40, 401.0),
    # -- hairpin 1 (left, ~14 m radius) --
    (-78.0, 27.20, 395.0),
    (-80.0, 26.00, 383.0),
    (-70.0, 25.00, 373.0),
    # -- limb B: back east, one shelf below limb A --
    (-54.0, 24.00, 369.0),
    (-38.0, 23.00, 366.0),
    # -- hairpin 2 (right, ~14 m radius) --
    (-26.0, 22.20, 362.0),
    (-18.0, 21.40, 354.0),
    (-24.0, 20.60, 344.0),
    (-38.0, 20.00, 340.0),
    # -- limb C: west again, below limb B, and out onto the open flank --
    (-58.0, 19.00, 342.0),
    (-80.0, 18.00, 350.0),
    (-102.0, 17.00, 362.0),
    # ---- Fuji approach: descend around the lakeshore and finish at the mountain ----
    # A broad descending arc around the lake.  Every successive turn reduces the straight-line
    # distance to FUJI_CENTER, so the hero mountain grows naturally through perspective and
    # parallax rather than being scaled by a camera trick.  The final north-east tangent points
    # directly into Fuji's lower flank and stops on the last safe shelf at its foot.
    (-122.0, 16.00, 378.0),
    (-140.0, 15.00, 400.0),
    (-156.0, 13.80, 428.0),
    (-170.0, 12.60, 460.0),
    (-183.0, 11.40, 498.0),
    (-194.0, 10.20, 540.0),
    (-203.0, 9.00, 586.0),
    (-210.0, 7.80, 634.0),
    (-215.0, 6.60, 684.0),
    (-217.0, 5.40, 730.0),
    # A wide 180-degree bend exposes the lake below before the road turns back toward Fuji.
    (-215.0, 4.40, 766.0),
    (-207.0, 3.60, 785.0),
    (-195.0, 3.00, 799.0),
    (-181.0, 2.60, 810.0),
    (-165.0, 2.40, 817.0),
]

# Index of the summit control point - everything after it is the new descent. Landmark and
# dressing windows that were authored against the climb are expressed as fractions of
# ``CLIMB_LENGTH`` so they stay exactly where they were when the route grew a second half.
SUMMIT_CONTROL_INDEX = 21

ROAD_HALF_WIDTH = 3.5          # 7 m carriageway, Japanese mountain-pass standard
SHOULDER_WIDTH = 0.55
ROAD_CROWN = 0.06              # centre camber so water sheds to the edges


def _catmull_rom(p0, p1, p2, p3, t):
    t2 = t * t
    t3 = t2 * t
    return tuple(
        0.5 * ((2 * p1[i]) +
               (-p0[i] + p2[i]) * t +
               (2 * p0[i] - 5 * p1[i] + 4 * p2[i] - p3[i]) * t2 +
               (-p0[i] + 3 * p1[i] - 3 * p2[i] + p3[i]) * t3)
        for i in range(3)
    )


def sample_centerline(spacing=2.0):
    """Dense Catmull-Rom resample of the control points, in Unity space."""
    return sample_polyline(CONTROL_POINTS, spacing)


def sample_polyline(control, spacing=2.0):
    """Dense Catmull-Rom resample of any control-point list, in Unity space."""
    pts = []
    n = len(control)
    for i in range(n - 1):
        p0 = control[max(0, i - 1)]
        p1 = control[i]
        p2 = control[i + 1]
        p3 = control[min(n - 1, i + 2)]
        seg = math.dist(p1, p2)
        steps = max(2, int(math.ceil(seg / spacing)))
        for s in range(steps):
            pts.append(_catmull_rom(p0, p1, p2, p3, s / steps))
    pts.append(control[-1])
    return np.array(pts, dtype=np.float64)


def frames(spacing=2.0, bank_gain=0.55, max_bank_deg=7.0):
    """
    Returns (positions, tangents, sides, ups, banks, arc_length) for the built pass.

    ``sides`` points to the rider's right. ``banks`` is a per-sample roll angle derived from
    curvature so the asphalt actually leans into corners the way a real pass does.
    """
    return frames_for(CONTROL_POINTS, spacing, bank_gain, max_bank_deg)


def frames_for(control, spacing=2.0, bank_gain=0.55, max_bank_deg=7.0):
    """``frames()`` for an arbitrary control-point list - used by every expansion segment."""
    p = sample_polyline(control, spacing)
    n = len(p)

    tangents = np.zeros_like(p)
    tangents[1:-1] = p[2:] - p[:-2]
    tangents[0] = p[1] - p[0]
    tangents[-1] = p[-1] - p[-2]
    tangents /= np.linalg.norm(tangents, axis=1, keepdims=True)

    world_up = np.array([0.0, 1.0, 0.0])
    # cross(up, tangent) points to the rider's RIGHT, matching the sign convention that
    # ``distance_field`` returns. (cross(tangent, up) would point left and put the crash
    # barrier on the cliff side instead of the drop.)
    sides = np.cross(world_up, tangents)
    sides /= np.maximum(np.linalg.norm(sides, axis=1, keepdims=True), 1e-9)

    # Signed curvature about the up axis -> banking.
    curvature = np.zeros(n)
    for i in range(1, n - 1):
        a = tangents[i - 1]
        b = tangents[i + 1]
        cross = np.cross(a, b)
        seg = np.linalg.norm(p[i + 1] - p[i - 1])
        curvature[i] = np.dot(cross, world_up) / max(seg, 1e-6)

    # Smooth so banking eases in and out instead of snapping at control points.
    kernel = np.ones(9) / 9.0
    curvature = np.convolve(curvature, kernel, mode='same')

    banks = np.clip(curvature * bank_gain * 180.0, -max_bank_deg, max_bank_deg)
    banks = np.convolve(banks, kernel, mode='same')

    ups = np.zeros_like(p)
    for i in range(n):
        roll = math.radians(banks[i])
        s = sides[i]
        u = np.cross(tangents[i], s)
        u /= max(np.linalg.norm(u), 1e-9)
        ups[i] = u * math.cos(roll) + s * math.sin(roll)
        sides[i] = s * math.cos(roll) - u * math.sin(roll)

    arc = np.zeros(n)
    arc[1:] = np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))

    return p, tangents, sides, ups, banks, arc


def climb_length(spacing=2.0):
    """
    Arc length of the original climb, i.e. the distance at the summit control point.

    Guardrail runs, the timber rail, flora sections and the diagnostic stops were all authored
    as fractions of the route length back when the route *was* the climb. Rescaling them against
    the now-doubled route would silently drag every one of them hundreds of metres downhill, so
    they are resolved against this instead.
    """
    p = sample_centerline(spacing)
    summit = np.array(CONTROL_POINTS[SUMMIT_CONTROL_INDEX])
    i = int(np.argmin(np.linalg.norm(p - summit, axis=1)))
    arc = np.zeros(len(p))
    arc[1:] = np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))
    return float(arc[i])


def index_at_distance(arc, d):
    """First sample index at or past arc length ``d`` metres."""
    return int(np.clip(np.searchsorted(arc, d), 0, len(arc) - 1))


def climb_window(arc, f0, f1, spacing=2.0):
    """Convert a (start, end) fraction *of the climb* into sample indices on ``arc``."""
    L = climb_length(spacing)
    return index_at_distance(arc, f0 * L), index_at_distance(arc, f1 * L)


# =====================================================================================
#  STAGED ROUTE EXPANSION  (docs/SAKURA_PASS_ROUTE_EXPANSION_PLAN.md)
# =====================================================================================
#
# The built pass is 1.38 km / ~4 min.  A deliberate 30-60 minute session needs a *network*,
# not a longer line, so the pass is closed into a circuit and given one stacked climb and one
# hub link.  Each segment below is authored exactly the way ``CONTROL_POINTS`` is - plan nodes
# in Unity space, Catmull-Rom resampled, elevation assigned from grade-bounded anchors - so the
# terrain carve, the road sweep, the banking and the dressing all follow automatically.
#
# Junctions (shared endpoints, matched to the metre):
#   J1 Ride Gate      (-2, 0, -200)      pass start  /  s1 finish  /  maple start
#   J8 Aozora Jn      pass @ 250 m       pass        /  aozora start
#   J5 Fuji Foot      (-165, 2.4, 817)   pass finish /  s1 start
#
# DEVIATION FROM THE PLAN, recorded deliberately: the plan's S2b "Skyline Traverse" would have
# had to cross either the descent hairpins or the open lake view to get back to the summit
# crest, so Aozora is delivered as an out-and-back spur to the Cloudline Shrine instead.  The
# course system rides a segment in either direction, so C2 still lands on the plan's 8.4 km /
# ~27 min budget with 2.5 km less geometry and no road crossings.  ALL numbers here are
# PROVISIONAL tuning.

SHORE_Y = -30.0          # lakeshore bench: a road shelf ~14 m above the water. The Lake Village
                         # is re-sited onto this shelf (build_expand.build_village), so the road
                         # and the town share one ground instead of the town keeping a private
                         # bench 26 m below a road the terrain would then have buried it under.
SHRINE_Y = 165.0         # Cloudline Shrine shelf - the Aozora high point
MAPLE_GATE_Y = -12.0     # Maple City sits on the valley floor, below the Ride Gate


def _smoothstep(t):
    return t * t * (3.0 - 2.0 * t)


def _ramp(anchors, step=0.04):
    """
    Densify (fraction, y) anchors so each span is walked at a CONSTANT grade.

    ``_with_elevation`` eases between anchors, which peaks a long span at 1.5x its average
    grade - that is what turns a 3.5 % average lakeshore climb into a 13 % ramp. Splitting the
    span into short linear steps keeps the eased grade within a few tenths of the average.
    """
    out = []
    for (f0, y0), (f1, y1) in zip(anchors, anchors[1:]):
        n = max(1, int(round((f1 - f0) / step)))
        for i in range(n):
            t = i / n
            out.append((f0 + (f1 - f0) * t, y0 + (y1 - y0) * t))
    out.append(anchors[-1])
    return out


def _with_elevation(plan, anchors):
    """plan: [(x, z), ...] -> [(x, y, z), ...] with y eased between fractional anchors."""
    d = [0.0]
    for a, b in zip(plan, plan[1:]):
        d.append(d[-1] + math.dist(a, b))
    total = max(d[-1], 1e-6)
    out = []
    for (x, z), s in zip(plan, d):
        f = s / total
        y = anchors[-1][1]
        for (f0, y0), (f1, y1) in zip(anchors, anchors[1:]):
            if f <= f1:
                t = 0.0 if f1 <= f0 else (f - f0) / (f1 - f0)
                y = y0 + (y1 - y0) * _smoothstep(min(1.0, max(0.0, t)))
                break
        out.append((x, y, z))
    return out


def _catmull_xz(nodes, spacing=6.0):
    """Plan-view Catmull-Rom resample; keeps hairpins round before elevation is applied."""
    pts = []
    n = len(nodes)
    for i in range(n - 1):
        p0 = nodes[max(0, i - 1)]
        p1, p2 = nodes[i], nodes[i + 1]
        p3 = nodes[min(n - 1, i + 2)]
        seg = math.dist(p1, p2)
        steps = max(2, int(math.ceil(seg / spacing)))
        for s in range(steps):
            t = s / steps
            t2, t3 = t * t, t * t * t
            pts.append(tuple(
                0.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t
                       + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2
                       + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t3)
                for k in range(2)))
    pts.append(nodes[-1])
    return pts


def _switchbacks(x_a, x_b, z0, dz, limbs, radius=32.0):
    """
    Switchback field: straight limbs alternating between x_a and x_b, 180 deg hairpins between.

    The hairpin bulges *past* the limb end it turns at, so the turn radius is real geometry
    rather than a kink the Catmull-Rom has to round off.
    """
    nodes = []
    z = float(z0)
    a, b = float(x_a), float(x_b)
    for i in range(limbs):
        nodes += [(a, z), (a + (b - a) * 0.34, z + 1.5), (a + (b - a) * 0.72, z), (b, z)]
        if i < limbs - 1:
            sgn = 1.0 if b > a else -1.0            # bulge outward, past the limb end
            nodes += [(b + sgn * radius * 0.72, z + dz * 0.16),
                      (b + sgn * radius, z + dz * 0.50),
                      (b + sgn * radius * 0.72, z + dz * 0.84)]
            z += dz
        a, b = b, a
    return nodes


def _junction(distance_m, spacing=2.0):
    """The point on the built pass at a given arc length - used to anchor a branch."""
    p = sample_centerline(spacing)
    arc = np.zeros(len(p))
    arc[1:] = np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))
    i = index_at_distance(arc, distance_m)
    return tuple(float(v) for v in p[i])


AOZORA_JUNCTION_M = 150.0        # PROVISIONAL: where the climber's road leaves the pass


def _build_expansion():
    """Assemble the expansion segments' 3D control points. Called once at import."""
    j8 = _junction(AOZORA_JUNCTION_M)

    # --- S1 Kawabe Lakeshore Return -------------------------------------------------
    # Fuji Foot -> lake head -> Minamo Causeway -> Lake Village frontage -> Kawabe Bridge ->
    # Ride Gate.  Closes the pass into a circuit; the recovery half of every lap.
    s1_plan = _catmull_xz([
        (-165.0, 817.0),
        (-150.0, 852.0), (-120.0, 884.0), (-78.0, 902.0), (-30.0, 906.0),
        (20.0, 896.0), (68.0, 874.0),
        (110.0, 846.0), (152.0, 812.0), (186.0, 770.0), (206.0, 722.0),
        (214.0, 668.0),
        (216.0, 604.0), (212.0, 540.0), (214.0, 470.0), (218.0, 400.0),
        (214.0, 330.0), (206.0, 262.0),
        (200.0, 196.0),
        (196.0, 120.0), (194.0, 44.0), (193.0, -32.0),
        (190.0, -104.0),
        (182.0, -170.0), (168.0, -228.0), (146.0, -282.0), (118.0, -326.0),
        (84.0, -352.0), (44.0, -364.0), (4.0, -360.0), (-32.0, -338.0),
        (-56.0, -310.0), (-64.0, -280.0), (-56.0, -248.0), (-34.0, -220.0),
        (-2.0, -200.0),
    ])
    s1 = _with_elevation(s1_plan, _ramp([(0.00, 2.40), (0.30, SHORE_Y), (0.45, SHORE_Y - 2.0),
                                         (0.70, SHORE_Y - 2.0), (1.00, 0.00)]))

    # --- S2 Aozora Ascent (out-and-back spur to the Cloudline Shrine) ---------------
    # Seven switchback limbs up the inland face: conifer treeline -> scree -> a bare shelf.
    # The field is deliberately pinned to z -60..252 and x -120..-414: the built pass occupies
    # x -102..-217 for every z above 340, so a field that reached that far north stacked a
    # 148 m road 12 m in plan from a 15 m one and asked the terrain for a 1100 % cross-slope.
    aozora_plan = _catmull_xz(
        [(j8[0], j8[2]), (-40.0, -76.0), (-96.0, -72.0)]
        + _switchbacks(x_a=-140.0, x_b=-390.0, z0=-60.0, dz=52.0, limbs=7, radius=34.0)
        + [(-414.0, 272.0), (-422.0, 298.0), (-406.0, 318.0), (-380.0, 326.0)])
    aozora = _with_elevation(aozora_plan, _ramp([(0.00, j8[1]), (0.05, j8[1] + 6.0),
                                                 (0.95, SHRINE_Y - 6.0), (1.00, SHRINE_Y)]))

    # --- S3 Maple City Road ---------------------------------------------------------
    # A false flat south-west through farmland and rice terraces to the hub gate: neutral zone
    # in, cooldown out, and the place Quick Ride spawns the rider.
    maple_plan = _catmull_xz([
        (-2.0, -200.0), (-36.0, -210.0), (-72.0, -224.0), (-104.0, -246.0),
        (-128.0, -276.0), (-142.0, -312.0), (-146.0, -350.0), (-140.0, -388.0),
        (-124.0, -422.0), (-100.0, -450.0), (-72.0, -472.0), (-44.0, -492.0),
        (-24.0, -520.0), (-18.0, -554.0), (-28.0, -588.0), (-50.0, -616.0),
        (-80.0, -638.0), (-116.0, -654.0), (-154.0, -664.0), (-192.0, -672.0),
        (-230.0, -682.0), (-266.0, -696.0), (-298.0, -716.0),
    ])
    maple = _with_elevation(maple_plan, _ramp([(0.00, 0.0), (0.45, -5.0),
                                               (1.00, MAPLE_GATE_Y)]))

    return {"s1": s1, "aozora": aozora, "maple": maple}


EXPANSION_CONTROL_POINTS = _build_expansion()

# id -> (display name, control points, whether the segment carries a steel barrier)
SEGMENTS = {
    "pass": ("Sakura Pass", CONTROL_POINTS, True),
    "s1": ("Kawabe Lakeshore Return", EXPANSION_CONTROL_POINTS["s1"], False),
    "aozora": ("Aozora Ascent", EXPANSION_CONTROL_POINTS["aozora"], False),
    "maple": ("Maple City Road", EXPANSION_CONTROL_POINTS["maple"], False),
}

EXPANSION_IDS = ["s1", "aozora", "maple"]


def segment_frames(seg_id, spacing=2.0):
    return frames_for(SEGMENTS[seg_id][1], spacing=spacing)


def segment_length(seg_id, spacing=2.0):
    p = sample_polyline(SEGMENTS[seg_id][1], spacing)
    return float(np.sum(np.linalg.norm(np.diff(p, axis=0), axis=1)))


_ALL_CENTERLINES = {}


def all_centerlines(spacing=2.0):
    """Every drivable centreline in the network, concatenated - what the terrain carves to."""
    key = round(spacing, 4)
    if key not in _ALL_CENTERLINES:
        _ALL_CENTERLINES[key] = np.concatenate(
            [sample_polyline(cp, spacing) for (_, cp, _) in SEGMENTS.values()], axis=0)
    return _ALL_CENTERLINES[key]


# ------------------------------------------------------------------- course definitions
#
# A course is an ordered list of legs; a leg is (segment, from_m, to_m) and is ridden REVERSED
# when from_m > to_m.  New courses therefore cost zero new geometry.  ``targetMinutes`` and
# ``defaultLaps`` are PROVISIONAL tuning - the runtime RideScheduler re-derives laps from the
# player's own FTP.
COURSES = [
    dict(id="pass_sprint", name="Pass Sprint", closed=False, targetMinutes=15,
         legs=[("pass", 0.0, None)]),
    dict(id="sakura_circuit", name="Sakura Circuit", closed=True, targetMinutes=30,
         legs=[("pass", 0.0, None), ("s1", 0.0, None)]),
    dict(id="aozora_loop", name="Aozora Skyline Loop", closed=True, targetMinutes=30,
         legs=[("pass", 0.0, AOZORA_JUNCTION_M),
               ("aozora", 0.0, None), ("aozora", None, 0.0),
               ("pass", AOZORA_JUNCTION_M, None),
               ("s1", 0.0, None)]),
    dict(id="gran_fondo", name="Sakura Gran Fondo", closed=True, targetMinutes=60,
         legs=[("maple", None, 0.0),
               ("pass", 0.0, AOZORA_JUNCTION_M),
               ("aozora", 0.0, None), ("aozora", None, 0.0),
               ("pass", AOZORA_JUNCTION_M, None),
               ("s1", 0.0, None),
               ("maple", 0.0, None)]),
]

# Named waypoints, anchored to (segment, arc metres along that segment). The GPS window pins
# them, the RouteDirector fires arrival events off them and the Journal uses the same list.
CHECKPOINTS = [
    ("pass", 0.0, "Ride Gate"),
    ("pass", 0.16, "Shrine Torii"),          # fraction of climb - resolved below
    ("pass", 0.40, "Lakeside Overlook"),
    ("pass", 0.60, "Cliff Tunnel"),
    ("pass", 0.72, "Hillside Sign"),
    ("pass", 0.90, "Summit Sign"),
    ("pass", 1.00, "Summit Crest"),
    ("pass", "descent:0.45", "Hairpin Twins"),
    ("pass", "descent:0.72", "Descent Overlook"),
    ("pass", "descent:1.00", "Fuji Foot"),
    ("s1", 0.18, "Lake Head"),
    ("s1", 0.36, "Minamo Causeway"),
    ("s1", 0.62, "Lake Village"),
    ("s1", 0.86, "Kawabe Bridge"),
    ("aozora", 0.10, "Aozora Junction"),
    ("aozora", 0.52, "Switchback Field"),
    ("aozora", 1.00, "Cloudline Shrine"),
    ("maple", 0.55, "Rice Terraces"),
    ("maple", 1.00, "Maple City Gate"),
]


def _resolved_checkpoints(spacing=3.0):
    """Turn the authoring fractions above into absolute metres along their segment."""
    lengths = {sid: segment_length(sid, spacing) for sid in SEGMENTS}
    climb = climb_length(spacing)
    out = []
    for seg, where, name in CHECKPOINTS:
        if isinstance(where, str) and where.startswith("descent:"):
            f = float(where.split(":")[1])
            d = climb + (lengths["pass"] - climb) * f
        elif seg == "pass":
            d = climb * float(where)
        else:
            d = lengths[seg] * float(where)
        out.append({"segment": seg, "distance": round(min(d, lengths[seg]), 2), "name": name})
    return out


def distance_field(xs, zs, spacing=2.0, tau_floor=2.5, tau_scale=0.38):
    """
    For a grid of Unity (x, z) coords return (distance_to_centreline, road_y, side).

    ``side`` is a *continuous* value in roughly [-1, +1]: +1 on the rider's right - the valley
    side of this pass - and -1 inland.

    A hard nearest-sample lookup makes ``road_y`` and ``side`` jump discontinuously along the
    medial axis between two limbs of a switchback, which the terrain builder then renders as a
    vertical wall. So both quantities are softmin-weighted across every route sample instead:
    weights are ``exp(-(d_i - d_min) / tau)`` with ``tau`` growing with distance. Close to the
    carriageway one sample dominates and the result matches the exact nearest-point answer;
    out where two limbs are equidistant the two blend into a smooth saddle.

    Vectorised because the terrain builder calls it for ~200k points.

    Since the staged expansion the field is measured against **every** segment in the network,
    not just the pass, so the lakeshore return, the Aozora switchbacks and the Maple City road
    all get a carved bench under them from the same heightfield.
    """
    p = all_centerlines(spacing)
    n = len(p)

    # Tangents are computed per segment and then concatenated: a central difference taken
    # straight across the join between two segments produces a tangent pointing from the end of
    # one road to the start of the next, which flips ``side`` for the two samples either side of
    # every seam and punches a spike through the terrain there.
    tangent_parts = []
    for (_, cp, _) in SEGMENTS.values():
        q = sample_polyline(cp, spacing)
        t = np.zeros_like(q)
        t[1:-1] = q[2:] - q[:-2]
        t[0] = q[1] - q[0]
        t[-1] = q[-1] - q[-2]
        t /= np.linalg.norm(t, axis=1, keepdims=True)
        tangent_parts.append(t)
    tangents = np.concatenate(tangent_parts, axis=0)

    shape = np.asarray(xs).shape
    q = np.stack([np.asarray(xs).ravel(), np.asarray(zs).ravel()], axis=1)   # (M, 2)
    road_xz = p[:, [0, 2]]                                                   # (N, 2)
    tan_xz = tangents[:, [0, 2]]                                             # (N, 2)
    ys = p[:, 1]                                                             # (N,)

    m = len(q)
    dist = np.empty(m)
    road_y = np.empty(m)
    side = np.empty(m)

    chunk = max(1, int(3_000_000 / max(n, 1)))
    for start in range(0, m, chunk):
        end = min(start + chunk, m)
        # (c, N, 2) offsets from every route sample to every query point
        off = q[start:end, None, :] - road_xz[None, :, :]
        d2 = np.einsum('ijk,ijk->ij', off, off)
        d = np.sqrt(d2)

        d_min = d.min(axis=1)
        dist[start:end] = d_min

        tau = np.maximum(tau_floor, d_min * tau_scale)[:, None]
        w = np.exp(-(d - d_min[:, None]) / tau)
        w_sum = w.sum(axis=1)

        road_y[start:end] = (w * ys[None, :]).sum(axis=1) / w_sum

        # y component of cross(tangent, toPoint) in the XZ plane, normalised to a unit sign
        cross_y = tan_xz[None, :, 1] * off[:, :, 0] - tan_xz[None, :, 0] * off[:, :, 1]
        s = cross_y / np.maximum(d, 1e-6)
        side[start:end] = np.clip((w * s).sum(axis=1) / w_sum, -1.0, 1.0)

    return dist.reshape(shape), road_y.reshape(shape), side.reshape(shape)


def _sample_payload(spacing=3.0, seg_id=None):
    """
    Dense, banked frames along a route, in Unity space.

    Unity consumes these directly rather than re-implementing the Catmull-Rom resample and
    the curvature-driven banking - if the two ever drifted apart, scattered props would sit
    off the carriageway the Blender road mesh was actually swept along.
    """
    if seg_id is None:
        p, tangents, sides, ups, banks, arc = frames(spacing=spacing)
    else:
        p, tangents, sides, ups, banks, arc = segment_frames(seg_id, spacing=spacing)
    out = []
    for i in range(len(p)):
        out.append({
            "p": [round(float(v), 4) for v in p[i]],
            "t": [round(float(v), 4) for v in tangents[i]],
            "s": [round(float(v), 4) for v in sides[i]],
            "u": [round(float(v), 4) for v in ups[i]],
            "bank": round(float(banks[i]), 3),
            "d": round(float(arc[i]), 3),
        })
    return out


def _course_payload(lengths):
    out = []
    for c in COURSES:
        legs = []
        for (seg, a, b) in c["legs"]:
            L = lengths[seg]
            legs.append({"segment": seg,
                         "from": round(L if a is None else float(a), 3),
                         "to": round(L if b is None else float(b), 3)})
        out.append({"id": c["id"], "name": c["name"], "closed": c["closed"],
                    "targetMinutes": c["targetMinutes"], "legs": legs})
    return out


def write_route_json():
    """Publish the route network into the Unity project so the C# side shares this definition."""
    out_dir = os.path.join(repo_root(), "Assets",
                           "Environment", "SakuraPass")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "SakuraRoute.json")

    spacing = 3.0
    segments = []
    lengths = {}
    for sid, (name, _, _) in SEGMENTS.items():
        samples = _sample_payload(spacing, None if sid == "pass" else sid)
        lengths[sid] = samples[-1]["d"]
        segments.append({"id": sid, "name": name, "samples": samples})

    payload = {
        "_comment": "Generated by tools/blender/sakura_route.py - do not edit by hand.",
        "roadHalfWidth": ROAD_HALF_WIDTH,
        "shoulderWidth": SHOULDER_WIDTH,
        # Arc length at the summit: the boundary between the original climb and the descent.
        # Unity resolves its section / landmark windows against this, not against total length.
        "climbLength": round(climb_length(spacing=spacing), 3),
        "aozoraJunction": AOZORA_JUNCTION_M,
        "controlPoints": [{"x": p[0], "y": p[1], "z": p[2]} for p in CONTROL_POINTS],
        # The built pass, kept at the top level so every pre-expansion consumer (terrain carve
        # checks, the scatter pass, the diagnostic cameras) keeps working untouched.
        "samples": segments[0]["samples"],
        "segments": segments,
        "courses": _course_payload(lengths),
        "checkpoints": _resolved_checkpoints(spacing),
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=1)
    print(f"[sakura] wrote route definition -> {path}")
    print(f"[sakura]   climb {climb_length(spacing=spacing):.1f} m, "
          f"pass {lengths['pass']:.1f} m")
    for sid in EXPANSION_IDS:
        print(f"[sakura]   segment {sid:<8} {lengths[sid]:8.1f} m")
    total = sum(lengths.values())
    print(f"[sakura]   network {total:.1f} m unique across {len(SEGMENTS)} segments, "
          f"{len(COURSES)} courses")
    return path

"""
Single source of truth for the MINATO COAST route ("Beyond the Horizon").

Publishes ``Assets/Environment/MinatoCoast/MinatoRoute.json`` in exactly the schema
``sakura_route`` / ``shiosai_route`` / ``taka_route`` use, so ``RouteGraphBaker`` merges a
SEVENTH region into the one runtime ``RouteGraph`` asset and every ride system (physics, HUD,
GPS window, follower, dressing streamer, trainer grade) works here with NO new code paths.

Standalone: needs no ``bpy`` and no ``sakura_lib``, so it runs under Blender's Python *or* any
Python with numpy:

    python minato_route.py

All coordinates are **Unity** world space (Y up, metres).


WHAT THIS ROUTE IS
------------------
One continuous 19 km point-to-point in five clearly named chapters, matching the five
authoritative concept renders in ``Assets/Environment/MinatoCoast/Concept/RideSections``:

  1. PORT CITY DEPARTURE   0.0 -  1.8 km   waterfront boulevard, road aimed at the bridge
  2. BRIDGE APPROACH       1.8 -  3.3 km   steady 3-4 % climb onto the deck (y 11 -> 62 m)
  3. OPEN OCEAN MIDPOINT   3.3 -  9.9 km   6.6 km of repeating arch spans over open water
  4. FAR-SHORE LANDFALL    9.9 - 13.4 km   descent onto the mainland + green coastal valley
  5. INLAND MOUNTAIN       13.4 - 19.0 km  forested switchbacks, rear viewpoints of the bridge

The 6.6 km midpoint is deliberate and is the headline requirement: the ocean crossing has to
*feel* multi-kilometre, so it is longer than the whole of Sakura Pass and roughly a third of
the entire route. At a realistic 30 km/h that is ~13 minutes of bridge.


WORLD PLACEMENT
---------------
Every region shares one boot scene, so each one owns a disjoint slab of world space:

    Sakura Pass      X [  -217,     17]  Z [  -200,    817]
    Maple City       X [ -3655,  -2271]  Z [  -874,    855]
    Taka Mountains   X [-12532,  -8377]  Z [     0,   4009]
    Azora Highlands  X [  -569,   3910]  Z [ 14000,  21707]
    Fuji Ridge       X [  4283,   8345]  Z [-10319,  -6862]
    Shiosai Coast    X [  2065,   6600]  Z [-11000,   2969]
    Minato Coast     X [  9000,  22800]  Z [  4000,   9600]   <- this file, previously unused

The world-map art (``reference/images/worldmap.png``) puts MINATO PORT on the southern coast
with its ferris wheel and container docks, a long road bridge striking out east across open
water, a green far shore beyond it and the terrain rising inland behind that. This route is
laid out to read the same way: it starts on the west (port) side, crosses eastward, makes
landfall on the eastern shore and climbs inland to the north-east.


ELEVATION
---------
Interpolated through explicit (distance, elevation) knots with a shape-preserving monotone
cubic (Fritsch-Carlson PCHIP), same as ``taka_route``. PCHIP cannot overshoot, which matters
twice here: the bridge deck must not develop a false hump above its 62 m navigation clearance,
and the mountain finish must not invent a summit above the authored 400 m.

PROVISIONAL / ILLUSTRATIVE TUNING
---------------------------------
Every number below is named and tunable. The chapter *structure* and the "ocean must feel
long" requirement are the direction; the specific metres, grades and target minutes are
placeholders chosen to be plausible, not design law.
"""

import json
import math
import os

import numpy as np

# --------------------------------------------------------------------------- identity

REGION_ID = "minato_coast"
SEGMENT_ID = "minato"
SEGMENT_NAME = "Minato Coast Crossing"
COURSE_ID = "minato_crossing"
COURSE_NAME = "Minato Coast - Beyond the Horizon"

# --------------------------------------------------------------------------- tuning

# World origin of the first station (Unity metres). See WORLD PLACEMENT above.
ORIGIN_X = 9000.0
ORIGIN_Z = 4200.0

SAMPLE_SPACING = 3.0       # m between stations; matches Shiosai Coast's coastal road
ROAD_HALF_WIDTH = 4.0      # SPEC 2026-09-22: road width target 7-9 m -> 8.0 m carriageway
SHOULDER_WIDTH = 0.55      # sealed shoulder outside the edge line
SEA_LEVEL = 0.0

TARGET_MINUTES = 62.0      # PROVISIONAL: ~19 km with 470 m of climbing

BANK_GAIN = 0.85           # radians of curvature -> degrees of bank, before the clamp
MAX_BANK_DEG = 5.0         # trunk-road superelevation; the mountain hairpins carry real camber

PLAN_RESOLUTION = 24000    # dense polyline samples before arc-length resampling
CONTROL_STEP_M = 30.0      # spacing of generated plan control points

# Deck height of the crossing above sea level. Drives chapters 2-4 and every bridge pier.
# SPEC 2026-09-22: deck 35-60 m above ocean -> 48 m (was 62, out of range).
BRIDGE_DECK_Y = 48.0
# Height of the city waterfront above sea level.
QUAY_Y = 6.0
# Road height on the sea-level causeway (REDESIGN 2026-09-25): low enough that surf and rock
# armour read beside the rider, high enough to clear the waterline.
CAUSEWAY_Y = 4.2
# Elevation of the mainland valley floor after landfall.
VALLEY_Y = 20.0
# Elevation at the inland finish.
# REDESIGN 2026-10: the finish is now downtown in the Horizon Metropolis at street level
# (was a 400 m mountain summit). PROVISIONAL tuning.
FINISH_Y = 12.0
# Street level of the metropolis after the descent off the red suspension bridge.
CITY_Y = 10.0
# Chapter-5 downtown grid: (length m, turn deg). Straights ease; 90 deg corners are tight arcs.
# PROVISIONAL layout - total must stay 5600 m (chapter length).
PLAN_CITY_LIMBS = [
    (700.0, 0.0), (60.0, +90.0), (800.0, 0.0), (60.0, -90.0),
    (900.0, 0.0), (60.0, +90.0), (800.0, 0.0), (60.0, -90.0),
    (900.0, 0.0), (60.0, +90.0), (1204.2, 0.0),   # +4.2 m restores the 19010 m course length lost in the corners
]


# --------------------------------------------------------------------------- chapters
#
# (id, display name, planned length in metres). These are the "clearly named chapter
# sections" the Unity hierarchy, the diagnostics cameras and the checkpoints all key off.

CHAPTERS = [
    ("port_city_departure",      "1 - Port City Departure",      1800.0),
    ("bridge_approach",          "2 - Seawall and Marina",       1500.0),
    ("open_ocean_midpoint",      "3 - Causeway and Bridge",      6600.0),
    ("far_shore_landfall",       "4 - Far-Shore Landfall",       3500.0),
    ("inland_mountain",          "5 - Horizon Metropolis",       5600.0),
]

# REDESIGN 2026-10: chapter 5 is now a downtown metropolis, not a mountain. The chapter id stays
# "inland_mountain" (hierarchy/diagnostics key), but the rider-facing checkpoint names follow the
# new content (RouteNames.Checkpoint turns "MC_HORIZON_METROPOLIS" into "Horizon Metropolis").
CHECKPOINT_NAME_OVERRIDES = {"inland_mountain": "MC_HORIZON_METROPOLIS"}
FINISH_CHECKPOINT_NAME = "MC_METROPOLIS_FINISH"


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


# --------------------------------------------------------------------------- plan

class Turtle:
    """
    Metric plan builder.

    Each ``walk`` lays down control points along a segment of known length while distributing a
    total heading change across it with a raised-cosine weight, so curvature starts and ends at
    zero. That gives clothoid-ish transitions (no instantaneous steering input at a corner
    entry) and - critically - keeps each chapter's ARC LENGTH equal to the length asked for,
    which is what lets the chapter boundaries land on the metre marks documented above.
    """

    def __init__(self, x, z, heading_deg):
        self.x = x
        self.z = z
        self.h = math.radians(heading_deg)
        self.pts = [(x, z)]
        self.marks = []   # (chapter_index, len(self.pts)) at each chapter boundary

    def walk(self, length, turn_deg=0.0, ease=True, step=CONTROL_STEP_M):
        n = max(2, int(round(length / step)))
        if ease:
            w = np.array([(1.0 - math.cos(2.0 * math.pi * (i + 0.5) / n)) / 2.0
                          for i in range(n)])
        else:
            w = np.ones(n)
        s = w.sum()
        w = w / s if s > 1e-9 else np.full(n, 1.0 / n)

        ds = length / n
        for i in range(n):
            self.h += math.radians(turn_deg) * w[i]
            self.x += math.cos(self.h) * ds
            self.z += math.sin(self.h) * ds
            self.pts.append((self.x, self.z))
        return self

    def mark(self, chapter_index):
        self.marks.append((chapter_index, len(self.pts) - 1))
        return self


def build_plan():
    """
    The full course as plan control points, plus the control-point index that ends each chapter.

    Heading convention: 0 deg = +X (east), positive turn = toward +Z (north).
    """
    t = Turtle(ORIGIN_X, ORIGIN_Z, heading_deg=0.0)

    # ---- 1. PORT CITY DEPARTURE (1800 m) -----------------------------------
    # A waterfront boulevard: docks and quay on the rider's right, dense port city on the left.
    # The final limb straightens so the bridge sits dead ahead, exactly as in concept 01.
    t.walk(500.0, -6.0)      # hug the quay
    t.walk(700.0, +10.0)     # swing left around the marina basin
    t.walk(600.0, -4.0)      # straighten: bridge dead ahead
    t.mark(0)

    # ---- 2. BRIDGE APPROACH (1500 m) ---------------------------------------
    # Nearly straight so the climb onto the deck reads as a ramp, not a corner (concept 02).
    t.walk(400.0, +2.0)
    t.walk(1100.0, -2.0)
    t.mark(1)

    # ---- 3. OPEN OCEAN MIDPOINT (6600 m) -----------------------------------
    # One very large-radius arc across open water. A dead-straight bridge would let the whole
    # span line vanish to a single point; a ~4 km radius sweep keeps successive arches stacked
    # and offset so they recede into haze the way concept 03 shows.
    t.walk(6600.0, +42.0, ease=False)
    t.mark(2)

    # ---- 4. FAR-SHORE LANDFALL (3500 m) ------------------------------------
    # The last spans curve toward the shore, the road drops onto the mainland and then runs
    # through a green coastal valley past homes and retaining walls (concept 04).
    t.walk(900.0, +14.0)     # final spans bending to the shore
    t.walk(800.0, +26.0)     # landfall, road turns to follow the coast
    t.walk(900.0, -34.0)     # valley: away from the water, between the foothills
    t.walk(900.0, +22.0)
    t.mark(3)

    # ---- 5. INLAND MOUNTAIN CONTINUATION (5600 m) --------------------------
    # Forested switchbacks. Each limb is a long straight plus a genuine ~29 m radius hairpin;
    # the 165 deg (not 180 deg) reversals fan the limbs out so they never run into each other,
    # and the road keeps gaining ground inland rather than stacking on one face.
    # REDESIGN 2026-10 (user feedback): the inland mountain switchbacks are replaced by the
    # HORIZON METROPOLIS - a dense Shanghai-like downtown grid. Long straight avenues joined by
    # right-angle street corners in a staircase (alternating left/right) so the plan advances
    # monotonically inland and can never cross itself. Chapter length stays 5600 m so the
    # checkpoint distances are unchanged.
    for L, turn in PLAN_CITY_LIMBS:
        t.walk(L, turn, ease=(turn == 0.0))
    t.mark(4)

    return np.asarray(t.pts, dtype=float), t.marks


def _catmull_rom(points, resolution):
    """
    Centripetal Catmull-Rom through the control points, as a dense open polyline.

    Centripetal (alpha = 0.5) rather than uniform parameterisation because uniform Catmull-Rom
    forms cusps and self-intersections exactly where control points bunch up - which on the
    mountain hairpins is everywhere. Centripetal is provably cusp-free.
    """
    p = np.asarray(points, dtype=float)
    p = np.vstack([p[:1], p, p[-1:]])

    t = [0.0]
    for i in range(1, len(p)):
        t.append(t[-1] + max(np.linalg.norm(p[i] - p[i - 1]), 1e-9) ** 0.5)
    t = np.asarray(t)

    out = []
    per_span = max(3, resolution // max(1, (len(p) - 3)))
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


def resample_open(p, spacing=SAMPLE_SPACING):
    """
    Resamples an OPEN XZ polyline to (near-)uniform arc spacing.

    Open, not closed: the last station IS the finish line, high on the mountain road, and gets
    no closing span back to the port.
    """
    seg = np.hypot(np.diff(p[:, 0]), np.diff(p[:, 1]))
    arc = np.concatenate([[0.0], np.cumsum(seg)])
    total = float(arc[-1])

    n = max(2, int(round(total / spacing)) + 1)
    want = np.linspace(0.0, total, n)
    x = np.interp(want, arc, p[:, 0])
    z = np.interp(want, arc, p[:, 1])
    return np.stack([x, z], axis=1), total, arc


def chapter_bounds(dense, control_pts, marks, arc_dense):
    """
    Arc distance (metres, along the dense plan) at which each chapter ends.

    Found by locating the dense-polyline sample nearest to the control point that ended the
    chapter, rather than by assuming the Catmull-Rom preserved the turtle's exact lengths.
    """
    bounds = []
    for (_ci, idx) in marks:
        target = control_pts[idx]
        d2 = np.sum((dense - target[None, :]) ** 2, axis=1)
        bounds.append(float(arc_dense[int(np.argmin(d2))]))
    return bounds


# --------------------------------------------------------------------------- elevation

def _pchip(xk, yk, x):
    """
    Fritsch-Carlson shape-preserving monotone cubic interpolation.

    Hand-rolled so this file keeps its "numpy only, no scipy" promise. The property that
    matters is NO OVERSHOOT: a natural cubic spline through these knots would put a hump above
    the 62 m bridge deck (because it must curve to meet the 3.5 % approach smoothly), which
    would give the crossing a false crest and break the "no joint height jumps" requirement.
    """
    xk = np.asarray(xk, dtype=float)
    yk = np.asarray(yk, dtype=float)
    h = np.diff(xk)
    delta = np.diff(yk) / h

    n = len(xk)
    d = np.zeros(n)
    for i in range(1, n - 1):
        if delta[i - 1] * delta[i] <= 0.0:
            d[i] = 0.0
        else:
            w1 = 2.0 * h[i] + h[i - 1]
            w2 = h[i] + 2.0 * h[i - 1]
            d[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i])

    def end_slope(hs, ds):
        s = ((2.0 * hs[0] + hs[1]) * ds[0] - hs[0] * ds[1]) / (hs[0] + hs[1])
        if s * ds[0] <= 0.0:
            return 0.0
        if abs(s) > 3.0 * abs(ds[0]):
            return 3.0 * ds[0]
        return s

    d[0] = end_slope(h, delta)
    d[-1] = end_slope(h[::-1], delta[::-1])

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


def elevation_knots(bounds):
    """
    (distance, elevation) knots, expressed relative to the MEASURED chapter boundaries so the
    profile always lines up with the plan even if the spline shortens a chapter slightly.
    """
    c1, c2, c3, c4, c5 = bounds

    def lerp(a, b, f):
        return a + (b - a) * f

    knots = [
        # ---- 1. port city: flat quay, a couple of metres of rise through the city ----
        (0.0,                   QUAY_Y),
        (lerp(0.0, c1, 0.35),   QUAY_Y + 1.0),
        (lerp(0.0, c1, 0.75),   QUAY_Y + 2.2),
        (c1,                    QUAY_Y + 5.0),          # 11.0 m - the ramp starts here

        # ---- REDESIGN 2026-09-25 (MINATO_VISUAL_DESIGN.md, user-approved): the crossing is
        #      re-profiled so the concept sequence reads in order -
        #      Seawall Sprint -> Marina Ribbon -> Stormglass Causeway at sea level, THEN the
        #      cable-stayed bridge climb as the climax, then landfall. Plan (x/z), chapter bounds
        #      and checkpoint distances are unchanged; only elevation moves. ----
        # 2. seawall + marina: ease down off the city quay to a low waterside road.
        (lerp(c1, c2, 0.35),    QUAY_Y + 1.5),          # 7.5 m
        (lerp(c1, c2, 0.70),    CAUSEWAY_Y + 0.6),
        (c2,                    CAUSEWAY_Y),

        # 3. causeway at sea level, gentle undulation, then the bridge ramp (~3.4 %) to the
        #    signature cable-stayed span and a short descent toward landfall.
        (lerp(c2, c3, 0.20),    CAUSEWAY_Y + 0.8),
        (lerp(c2, c3, 0.42),    CAUSEWAY_Y + 0.2),
        (lerp(c2, c3, 0.62),    CAUSEWAY_Y + 0.9),      # ~7.4 km: last low point
        (lerp(c2, c3, 0.745),   lerp(CAUSEWAY_Y, BRIDGE_DECK_Y, 0.45)),
        (lerp(c2, c3, 0.845),   BRIDGE_DECK_Y),          # ~8.9 km: onto the signature span
        (lerp(c2, c3, 0.905),   BRIDGE_DECK_Y + 2.2),   # crest between the pylons
        (lerp(c2, c3, 0.965),   BRIDGE_DECK_Y + 0.4),
        (c3,                    BRIDGE_DECK_Y - 1.0),

        # ---- 4. far-shore landfall. REDESIGN 2026-10 (user feedback): the road DESCENDS off the
        #      red suspension bridge deck straight after the shore anchorage (~10.37 km) down to
        #      street level of the Horizon Metropolis, then stays at city grade. ----
        (lerp(c3, c4, 0.125),   BRIDGE_DECK_Y - 4.0),   # ~10.34 km: still on the deck at the anchorage
        (lerp(c3, c4, 0.20),    lerp(BRIDGE_DECK_Y - 4.0, CITY_Y, 0.38)),   # ~10.6 km, ~6 % down
        (lerp(c3, c4, 0.29),    lerp(BRIDGE_DECK_Y - 4.0, CITY_Y, 0.82)),
        (lerp(c3, c4, 0.37),    CITY_Y + 1.0),          # ~11.2 km: at street level
        (lerp(c3, c4, 0.55),    CITY_Y),
        (lerp(c3, c4, 0.78),    CITY_Y + 1.5),
        (c4,                    CITY_Y + 0.5),

        # ---- 5. Horizon Metropolis: nearly flat downtown streets, gentle rises ----
        (lerp(c4, c5, 0.18),    CITY_Y + 2.5),
        (lerp(c4, c5, 0.38),    CITY_Y + 0.5),
        (lerp(c4, c5, 0.60),    CITY_Y + 3.0),
        (lerp(c4, c5, 0.82),    CITY_Y + 1.0),
        (c5,                    FINISH_Y),
    ]

    xk = [k[0] for k in knots]
    yk = [k[1] for k in knots]
    # Guard against any two knots colliding after the lerps.
    for i in range(1, len(xk)):
        if xk[i] <= xk[i - 1]:
            xk[i] = xk[i - 1] + 1.0
    return np.asarray(xk), np.asarray(yk)


def smooth_open(values, window):
    """Moving average with slope-preserving (odd-reflected) ends."""
    v = np.asarray(values, dtype=float)
    if window < 3:
        return v
    half = window // 2
    head = 2.0 * v[0] - v[1:half + 1][::-1]
    tail = 2.0 * v[-1] - v[-half - 1:-1][::-1]
    padded = np.concatenate([head, v, tail])
    kernel = np.ones(window) / window
    return np.convolve(padded, kernel, mode="valid")[:len(v)]


# --------------------------------------------------------------------------- frames

def frames(spacing=SAMPLE_SPACING):
    control_pts, marks = build_plan()
    dense = _catmull_rom(control_pts, PLAN_RESOLUTION)

    seg = np.hypot(np.diff(dense[:, 0]), np.diff(dense[:, 1]))
    arc_dense = np.concatenate([[0.0], np.cumsum(seg)])

    bounds = chapter_bounds(dense, control_pts, marks, arc_dense)

    xz, total, _ = resample_open(dense, spacing)

    d = np.hypot(*(np.diff(xz, axis=0)).T)
    arc = np.concatenate([[0.0], np.cumsum(d)])
    total = float(arc[-1])
    bounds[-1] = total   # the last chapter always ends at the finish line

    xk, yk = elevation_knots(bounds)
    y = _pchip(xk, yk, arc)
    y = smooth_open(y, 7)

    p = np.stack([xz[:, 0], y, xz[:, 1]], axis=1)

    tangents = np.gradient(p, axis=0)
    tangents /= np.maximum(np.linalg.norm(tangents, axis=1, keepdims=True), 1e-9)

    world_up = np.array([0.0, 1.0, 0.0])
    sides = np.cross(world_up, tangents)          # rider's RIGHT - do not reverse
    sides /= np.maximum(np.linalg.norm(sides, axis=1, keepdims=True), 1e-9)

    prev_t = np.vstack([tangents[:1], tangents[:-1]])
    next_t = np.vstack([tangents[1:], tangents[-1:]])
    cross = np.cross(prev_t, next_t)
    span = np.maximum(np.linalg.norm(
        np.vstack([p[1:2], p[2:], p[-1:]]) - np.vstack([p[:1], p[:-2], p[-2:-1]]),
        axis=1), 1e-6)

    curvature = smooth_open(cross @ world_up / span, 9)
    banks = np.clip(curvature * BANK_GAIN * 180.0, -MAX_BANK_DEG, MAX_BANK_DEG)
    banks = smooth_open(banks, 11)

    ups = np.zeros_like(p)
    for i in range(len(p)):
        roll = math.radians(banks[i])
        s = sides[i].copy()
        u = np.cross(tangents[i], s)
        u /= max(np.linalg.norm(u), 1e-9)
        ups[i] = u * math.cos(roll) + s * math.sin(roll)
        sides[i] = s * math.cos(roll) - u * math.sin(roll)

    return p, tangents, sides, ups, banks, arc, total, bounds


# --------------------------------------------------------------------------- validation

def validate(p, arc, total, bounds):
    """Same continuity gate ``shiosai_route`` applies: monotone distance, sane grade, no
    grade steps, and no place where the road runs into itself."""
    issues = []

    if not np.all(np.diff(arc) > 0):
        issues.append("distances are not strictly monotonic")

    window = 10
    dd = arc[window:] - arc[:-window]
    g = np.where(dd > 0.01, (p[window:, 1] - p[:-window, 1]) / np.maximum(dd, 1e-6), 0.0)
    max_grade = float(np.max(np.abs(g)))
    max_step = float(np.max(np.abs(np.diff(g))))

    if max_grade > 0.11:
        issues.append(f"max smoothed grade {max_grade * 100:.1f}% exceeds 11%")
    if max_step > 0.02:
        issues.append(f"max grade step {max_step * 100:.2f}% exceeds 2%")

    # Adjacent-sample height jump: the requirement is "no joint height jumps".
    dy = np.abs(np.diff(p[:, 1]))
    if float(dy.max()) > 0.40:
        issues.append(f"adjacent-station height jump {dy.max():.3f} m exceeds 0.40 m")

    # Self-conflict: far apart along the route but close in plan and not vertically separated.
    ignore_span, clearance, vertical = 260.0, 26.0, 14.0
    step = max(1, int(round(18.0 / SAMPLE_SPACING)))
    q = p[::step]
    qa = arc[::step]
    conflicts = []
    for i in range(len(q)):
        dx = q[i + 1:, 0] - q[i, 0]
        dz = q[i + 1:, 2] - q[i, 2]
        dyy = np.abs(q[i + 1:, 1] - q[i, 1])
        da = qa[i + 1:] - qa[i]
        bad = (da > ignore_span) & (np.hypot(dx, dz) < clearance) & (dyy < vertical)
        if np.any(bad):
            j = int(np.argmax(bad)) + i + 1
            conflicts.append((qa[i], qa[j]))
    if conflicts:
        spans = ", ".join(f"{a / 1000:.2f}/{b / 1000:.2f} km" for a, b in conflicts[:6])
        issues.append(f"{len(conflicts)} self-conflict sample pair(s) near {spans}")

    print(f"[minato] length          {total:,.1f} m  ({len(p):,} stations @ {SAMPLE_SPACING} m)")
    print(f"[minato] elevation       {p[:, 1].min():.1f} -> {p[:, 1].max():.1f} m")
    print(f"[minato] max grade       {max_grade * 100:.2f} %   max grade step {max_step * 100:.3f} %")
    print(f"[minato] max height step {dy.max() * 1000:.1f} mm between adjacent stations")
    print(f"[minato] plan bounds     X [{p[:, 0].min():,.0f}, {p[:, 0].max():,.0f}]  "
          f"Z [{p[:, 2].min():,.0f}, {p[:, 2].max():,.0f}]")
    prev = 0.0
    for (cid, cname, _want), b in zip(CHAPTERS, bounds):
        print(f"[minato]   {cname:<34s} {prev:8.1f} -> {b:8.1f} m   "
              f"({b - prev:7.1f} m)")
        prev = b

    if issues:
        for i in issues:
            print(f"[minato] ISSUE: {i}")
    else:
        print("[minato] continuity OK")
    return issues


# --------------------------------------------------------------------------- output

def write_route_json():
    p, t, s, u, bank, arc, total, bounds = frames()
    issues = validate(p, arc, total, bounds)

    samples = [{
        "p": [round(float(v), 4) for v in p[i]],
        "t": [round(float(v), 4) for v in t[i]],
        "s": [round(float(v), 4) for v in s[i]],
        "u": [round(float(v), 4) for v in u[i]],
        "bank": round(float(bank[i]), 3),
        "d": round(float(arc[i]), 3),
    } for i in range(len(p))]

    out_dir = os.path.join(repo_root(), "Assets", "Environment", "MinatoCoast")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "MinatoRoute.json")

    chapters = []
    prev = 0.0
    for (cid, cname, _want), b in zip(CHAPTERS, bounds):
        chapters.append({
            "id": cid,
            "name": cname,
            "from": round(prev, 2),
            "to": round(float(b), 2),
        })
        prev = float(b)

    checkpoints = []
    prev = 0.0
    for (cid, cname, _want), b in zip(CHAPTERS, bounds):
        checkpoints.append({
            "segment": SEGMENT_ID,
            "distance": round(prev, 2),
            "name": CHECKPOINT_NAME_OVERRIDES.get(cid, f"MC_{cid.upper()}"),
        })
        prev = float(b)
    checkpoints.append({
        "segment": SEGMENT_ID,
        "distance": round(total, 2),
        "name": FINISH_CHECKPOINT_NAME,
    })

    payload = {
        "_comment": "Generated by tools/blender/minato_route.py - do not edit by hand.",
        "regionId": REGION_ID,
        "roadHalfWidth": ROAD_HALF_WIDTH,
        "shoulderWidth": SHOULDER_WIDTH,
        "seaLevel": SEA_LEVEL,
        "bridgeDeckY": BRIDGE_DECK_Y,
        "samples": samples,
        "chapters": chapters,
        "segments": [{"id": SEGMENT_ID, "name": SEGMENT_NAME, "samples": samples}],
        "courses": [{
            "id": COURSE_ID,
            "name": COURSE_NAME,
            "closed": False,
            "targetMinutes": TARGET_MINUTES,
            "legs": [{"segment": SEGMENT_ID, "from": 0.0, "to": round(total, 3)}],
        }],
        "checkpoints": checkpoints,
    }

    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=1)

    print(f"[minato] wrote {path}  ({os.path.getsize(path) / 1024 / 1024:.2f} MB)")
    return path, issues


if __name__ == "__main__":
    write_route_json()

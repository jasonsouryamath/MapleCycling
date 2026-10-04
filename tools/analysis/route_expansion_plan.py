"""
Sakura Pass route-expansion planning maths (v2).

Reads the REAL authored route, adds PROPOSED expansion segments as plan-view node lists that
are Catmull-Rom smoothed exactly the way tools/blender/sakura_route.py smooths CONTROL_POINTS,
assigns elevation from grade-bounded anchors, and costs every course in ride-minutes using the
physics model the ride foundation (milestone 2) will use.

Planning tool only - it writes nothing into the game pipeline. The emitted control points are
the numbers a future sakura_route.py edit would paste into CONTROL_POINTS.

    python tools/analysis/route_expansion_plan.py
"""

import json
import math
import os

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ROUTE = os.path.join(REPO, "Assets", "Environment", "SakuraPass",
                     "SakuraRoute.json")
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "route_expansion_plan.json")

# --------------------------------------------------------------------------- physics
# ALL PROVISIONAL. Every constant here is an illustrative default that belongs behind a
# serialized, tunable field on CyclingPhysics - none of it is a design requirement.
RIDER_MASS_KG = 68.0
BIKE_MASS_KG = 8.5
CDA = 0.32
CRR = 0.005
AIR_DENSITY = 1.225
DRIVETRAIN_EFF = 0.975
REFERENCE_FTP_W = 220.0         # provisional "average player" FTP until FTP setup exists
ENDURANCE_FRACTION = 0.72       # power an unforced scenic ride settles at  -> 158 W
DESCENT_FRACTION = 0.25         # riders soft-pedal downhill
DESCENT_CAP_KPH = 58.0
MIN_SPEED_KPH = 5.0
LAKE_Y = -44.0                  # build_terrain.LAKE_LEVEL
DECK_Y = LAKE_Y + 2.6           # causeway / bridge deck sits just clear of the water

MASS = RIDER_MASS_KG + BIKE_MASS_KG
G = 9.80665


def speed_for(power_w, grade):
    theta = math.atan(grade)
    lo, hi = 0.05, 30.0
    for _ in range(70):
        v = 0.5 * (lo + hi)
        need = (0.5 * AIR_DENSITY * CDA * v ** 3
                + CRR * MASS * G * math.cos(theta) * v
                + MASS * G * math.sin(theta) * v) / DRIVETRAIN_EFF
        if need > power_w:
            hi = v
        else:
            lo = v
    return 0.5 * (lo + hi)


# ------------------------------------------------------------------------- geometry
def catmull(nodes, spacing=4.0):
    """Same resample sakura_route.sample_centerline uses, on (x, z) plan nodes."""
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


def smoothstep(t):
    return t * t * (3 - 2 * t)


def with_elevation(plan, anchors):
    """
    plan    : [(x, z), ...] smoothed plan polyline
    anchors : [(fraction_of_length, y), ...] ascending; y eased between them so grades stay
              continuous instead of snapping at nodes.
    """
    d = [0.0]
    for a, b in zip(plan, plan[1:]):
        d.append(d[-1] + math.dist(a, b))
    total = d[-1]
    out = []
    for (x, z), s in zip(plan, d):
        f = s / total
        placed = False
        for (f0, y0), (f1, y1) in zip(anchors, anchors[1:]):
            if f <= f1:
                t = 0.0 if f1 <= f0 else (f - f0) / (f1 - f0)
                out.append((x, y0 + (y1 - y0) * smoothstep(min(1.0, max(0.0, t))), z))
                placed = True
                break
        if not placed:
            out.append((x, anchors[-1][1], z))
    return out


def ramp(anchors, step=0.04):
    """
    Densify elevation anchors so each span is walked at a CONSTANT grade.

    with_elevation() eases between anchors, which peaks a long span at 1.5x its average
    grade - that is what turned a 3.5% average lakeshore climb into a 13% ramp. Splitting the
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


def serpentine(x_east, x_west, z_start, z_step, limbs, hairpin_r=38.0):
    """Switchback field: straight limbs in x, alternating 180 deg hairpins at each end."""
    nodes = []
    z = z_start
    east_to_west = True
    for i in range(limbs):
        a, b = (x_east, x_west) if east_to_west else (x_west, x_east)
        nodes += [(a, z), (a + (b - a) * 0.35, z + 2.0), (a + (b - a) * 0.75, z), (b, z)]
        if i < limbs - 1:
            sgn = -1.0 if east_to_west else 1.0     # hairpin bulges past the limb end
            nodes += [(b + sgn * hairpin_r * 0.72, z + z_step * 0.16),
                      (b + sgn * hairpin_r, z + z_step * 0.50),
                      (b + sgn * hairpin_r * 0.72, z + z_step * 0.84)]
            z += z_step
        east_to_west = not east_to_west
    return nodes


def stats(pts):
    length = sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))
    gain = sum(max(0.0, b[1] - a[1]) for a, b in zip(pts, pts[1:]))
    loss = sum(max(0.0, a[1] - b[1]) for a, b in zip(pts, pts[1:]))
    grades, minutes = [], 0.0
    for a, b in zip(pts, pts[1:]):
        run = math.dist((a[0], a[2]), (b[0], b[2]))
        arc_len = math.dist(a, b)
        if run < 1e-6:
            continue
        gr = (b[1] - a[1]) / run
        grades.append(gr * 100.0)
        power = REFERENCE_FTP_W * (DESCENT_FRACTION if gr < -0.015 else ENDURANCE_FRACTION)
        v = min(max(speed_for(power, gr), MIN_SPEED_KPH / 3.6), DESCENT_CAP_KPH / 3.6)
        minutes += arc_len / v / 60.0
    return {"length_m": length, "gain_m": gain, "loss_m": loss,
            "max_grade_pct": max(grades), "min_grade_pct": min(grades), "minutes": minutes}


# --------------------------------------------------------------- existing (real) route
route = json.load(open(ROUTE, encoding="utf-8"))
existing = [tuple(s["p"]) for s in route["samples"]]
arc = [s["d"] for s in route["samples"]]
SUMMIT_I = max(range(len(existing)), key=lambda i: existing[i][1])


def point_at(dist_m):
    for i, a in enumerate(arc):
        if a >= dist_m:
            return existing[i]
    return existing[-1]


J8 = point_at(250.0)
print(f"[plan] existing route {arc[-1]:.1f} m; summit {existing[SUMMIT_I][1]:.1f} m at "
      f"{arc[SUMMIT_I]:.1f} m; J8 junction {J8}")

# ------------------------------------------------------------ proposed new geometry
S1_PLAN = catmull([
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
    (182.0, -170.0), (168.0, -232.0), (146.0, -288.0), (118.0, -334.0),
    (84.0, -366.0), (44.0, -382.0), (2.0, -380.0), (-36.0, -362.0),
    (-64.0, -332.0),
    (-80.0, -296.0), (-84.0, -262.0), (-70.0, -232.0), (-40.0, -210.0),
    (-2.0, -200.0),
])
SHORE_Y = -18.0     # lakeshore shelf: a road bench ~26 m above the water, not a causeway
S1 = with_elevation(S1_PLAN, ramp([(0.00, 2.40), (0.30, SHORE_Y), (0.45, SHORE_Y - 2.0),
                                   (0.70, SHORE_Y - 2.0), (1.00, 0.00)]))

SHRINE_Y = 180.0    # Cloudline Shrine shelf - the Aozora high point
S2A_PLAN = catmull([(J8[0], J8[2]), (-16.0, 58.0), (-52.0, 52.0)]
                   + serpentine(x_east=-64.0, x_west=-452.0, z_start=52.0,
                                z_step=62.0, limbs=5)
                   + [(-470.0, 324.0), (-482.0, 368.0), (-476.0, 410.0)])
S2A = with_elevation(S2A_PLAN, ramp([(0.00, J8[1]), (0.06, 10.0),
                                     (0.94, SHRINE_Y - 9.0), (1.00, SHRINE_Y)]))

S2B_PLAN = catmull([(-476.0, 410.0), (-464.0, 462.0), (-440.0, 508.0)]
                   + serpentine(x_east=-124.0, x_west=-436.0, z_start=536.0,
                                z_step=58.0, limbs=4)
                   + [(-96.0, 700.0), (-60.0, 664.0), (-30.0, 612.0), (-8.0, 552.0),
                      (2.0, 486.0), (-6.0, 424.0), (-2.0, 372.0), (16.0, 326.0)])
S2B = with_elevation(S2B_PLAN, ramp([(0.00, SHRINE_Y), (0.08, SHRINE_Y - 8.0),
                                     (0.92, 44.0), (1.00, 36.8)]))

S3_PLAN = catmull([
    (-2.0, -200.0), (-14.0, -248.0), (-34.0, -300.0), (-66.0, -352.0), (-108.0, -402.0),
    (-152.0, -452.0), (-190.0, -508.0), (-216.0, -572.0), (-232.0, -642.0),
    (-240.0, -716.0), (-244.0, -792.0), (-250.0, -866.0), (-262.0, -938.0),
    (-284.0, -1006.0), (-316.0, -1068.0), (-356.0, -1120.0),
])
S3 = with_elevation(S3_PLAN, ramp([(0.00, 0.0), (0.45, -5.0), (1.00, -12.0)]))

# ------------------------------------------------- prerequisite: re-cut the built climb
# The built climb averages a sane 6.7% but delivers it as a 37-40% wall between 400 and 475 m
# (control points 15-19). Nothing rideable can be tuned on top of that, so the expansion plan
# carries a re-cut: keep every x/z and the 36.8 m summit exactly where they are, and only
# redistribute y so the grade ramps up and eases back onto the crest shelf.
def recut_climb(control_points, summit_index=21):
    xs = [(p["x"], p["y"], p["z"]) for p in control_points[:summit_index + 1]]
    s = [0.0]
    for a, b in zip(xs, xs[1:]):
        s.append(s[-1] + math.dist((a[0], a[2]), (b[0], b[2])))
    total = s[-1]

    def shape(f):
        return (0.15 + 1.55 * smoothstep(min(1.0, f / 0.55))
                - 0.95 * smoothstep(max(0.0, (f - 0.72) / 0.28)))

    raw = [0.0]
    for a, b in zip(s, s[1:]):
        raw.append(raw[-1] + shape((a + b) * 0.5 / total) * (b - a))
    k = (xs[-1][1] - xs[0][1]) / raw[-1]
    ys = [xs[0][1] + r * k for r in raw]
    grades = [(ys[i + 1] - ys[i]) / max(1e-6, s[i + 1] - s[i]) * 100 for i in range(len(s) - 1)]
    return ys, max(grades), total


recut_y, recut_max, recut_len = recut_climb(route["controlPoints"])
print(f"\n[plan] climb re-cut: {recut_len:.0f} m of run, max grade {recut_max:.1f}% "
      f"(built route peaks at 40.2%), summit unchanged at {recut_y[-1]:.1f} m")
print("[plan] proposed CONTROL_POINTS y, indices 0-21: "
      + ", ".join(f"{y:.2f}" for y in recut_y))


SEGMENTS = {
    "Pass Ascent (built)": existing[:SUMMIT_I + 1],
    "Fuji Descent (built)": existing[SUMMIT_I:],
    "S1 Kawabe Lakeshore Return": S1,
    "S2a Aozora Ascent": S2A,
    "S2b Skyline Traverse": S2B,
    "S3 Maple City Road": S3,
}

print(f"\n{'segment':<30}{'m':>8}{'+m':>7}{'-m':>7}{'max%':>7}{'min%':>7}{'min':>7}")
data = {}
for name, pts in SEGMENTS.items():
    s = stats(pts)
    data[name] = s
    print(f"{name:<30}{s['length_m']:8.0f}{s['gain_m']:7.0f}{s['loss_m']:7.0f}"
          f"{s['max_grade_pct']:7.1f}{s['min_grade_pct']:7.1f}{s['minutes']:7.1f}")

COURSES = {
    "C0 Pass Sprint (today)": ["Pass Ascent (built)", "Fuji Descent (built)"],
    "C1 Sakura Circuit (lap)": ["Pass Ascent (built)", "Fuji Descent (built)",
                                "S1 Kawabe Lakeshore Return"],
    "C2 Aozora Skyline Loop": ["Pass Ascent (built)", "S2a Aozora Ascent",
                               "S2b Skyline Traverse", "Fuji Descent (built)",
                               "S1 Kawabe Lakeshore Return"],
    "C3 Gran Fondo (unique)": ["S3 Maple City Road", "S3 Maple City Road",
                               "Pass Ascent (built)", "S2a Aozora Ascent",
                               "S2b Skyline Traverse", "Fuji Descent (built)",
                               "S1 Kawabe Lakeshore Return"],
}
print(f"\n{'course':<30}{'km':>8}{'gain':>7}{'min':>7}{'x->30':>8}{'x->60':>8}")
courses = {}
for name, segs in COURSES.items():
    L = sum(data[s]["length_m"] for s in segs)
    up = sum(data[s]["gain_m"] for s in segs)
    t = sum(data[s]["minutes"] for s in segs)
    courses[name] = {"km": L / 1000.0, "gain_m": up, "minutes": t, "segments": segs}
    print(f"{name:<30}{L/1000:8.2f}{up:7.0f}{t:7.1f}{30/t:8.1f}{60/t:8.1f}")

payload = {
    "existing_length_m": arc[-1],
    "existing_summit_m": arc[SUMMIT_I],
    "recut_climb_y": [round(y, 2) for y in recut_y],
    "recut_max_grade_pct": recut_max,
    "segments": data,
    "courses": courses,
    "physics": {"rider_kg": RIDER_MASS_KG, "bike_kg": BIKE_MASS_KG, "cda": CDA, "crr": CRR,
                "ftp_w": REFERENCE_FTP_W, "endurance_fraction": ENDURANCE_FRACTION,
                "descent_cap_kph": DESCENT_CAP_KPH},
    "polylines": {k: [[round(c, 2) for c in p] for p in v] for k, v in SEGMENTS.items()},
}

# --------------------------------------------------------------- terrain / build budget
allpts = [p for v in SEGMENTS.values() for p in v]
bx = [p[0] for p in allpts]
bz = [p[2] for p in allpts]
MARGIN = 62.0                      # build_terrain.RELIEF_RAMP - the batter needs this much
need = {"min_x": min(bx) - MARGIN, "max_x": max(bx) + MARGIN,
        "min_z": min(bz) - MARGIN, "max_z": max(bz) + MARGIN}
cur_area = (190 + 285) * (900 + 295)
new_area = (need["max_x"] - need["min_x"]) * (need["max_z"] - need["min_z"])
tile_x = (190 + 285) / 5.0
tile_z = (900 + 295) / 14.0
payload["terrain"] = {
    **{k: round(v) for k, v in need.items()},
    "area_m2": new_area, "area_vs_today": new_area / cur_area,
    "quads_at_step_2_5": new_area / 6.25,
    "tiles_x": math.ceil((need["max_x"] - need["min_x"]) / tile_x),
    "tiles_z": math.ceil((need["max_z"] - need["min_z"]) / tile_z),
}
print(f"\n[plan] terrain must grow to x {need['min_x']:.0f}..{need['max_x']:.0f}, "
      f"z {need['min_z']:.0f}..{need['max_z']:.0f} "
      f"({new_area/1e6:.2f} km2, {new_area/cur_area:.1f}x today, "
      f"{new_area/6.25/1000:.0f}k quads at STEP 2.5, "
      f"TILES_X/Z {payload['terrain']['tiles_x']}/{payload['terrain']['tiles_z']})")
unique_km = courses["C3 Gran Fondo (unique)"]["km"]
print(f"[plan] dressing budget at today's density (~1.02 objects/m of route): "
      f"{unique_km * 1000 * 1.02:.0f} objects for {unique_km:.1f} km "
      f"(scene holds 1411 today)")

json.dump(payload, open(OUT, "w", encoding="utf-8"), indent=1)
print(f"\nwrote {OUT}")

"""
Implementation check for the staged route expansion.

Reports the REAL geometry now authored in tools/blender/sakura_route.py - per-segment length,
gain, grade envelope and ride minutes, per-course km/minutes/laps, the terrain bounds the
network needs, and whether the climb re-cut actually removed the 37-40 % wall.

Runs outside Blender (sakura_lib is stubbed for its one path helper), so it is the fast loop
for tuning geometry before paying for a terrain rebuild.

    python tools/analysis/network_report.py
"""

import math
import os
import sys
import types

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(REPO, "tools", "blender"))

# sakura_lib pulls in bpy; sakura_route only needs repo_root() from it.
stub = types.ModuleType("sakura_lib")
stub.repo_root = lambda: REPO
sys.modules.setdefault("sakura_lib", stub)

import numpy as np                                                    # noqa: E402
import sakura_route as R                                              # noqa: E402

# --- provisional physics (mirrors Assets/Ride/CyclingPhysics.cs defaults) -------------
MASS = 68.0 + 8.5
CDA, CRR, RHO, ETA, G = 0.32, 0.005, 1.225, 0.975, 9.80665
FTP_W = 220.0
ENDURANCE = 0.72
DESCENT = 0.25
DESCENT_CAP = 58.0 / 3.6
MIN_SPEED = 5.0 / 3.6


def speed_for(power_w, grade):
    theta = math.atan(grade)
    lo, hi = 0.05, 30.0
    for _ in range(70):
        v = 0.5 * (lo + hi)
        need = (0.5 * RHO * CDA * v ** 3
                + CRR * MASS * G * math.cos(theta) * v
                + MASS * G * math.sin(theta) * v) / ETA
        if need > power_w:
            hi = v
        else:
            lo = v
    return 0.5 * (lo + hi)


def walk(pts):
    length = gain = loss = minutes = 0.0
    grades = []
    for a, b in zip(pts, pts[1:]):
        run = math.dist((a[0], a[2]), (b[0], b[2]))
        arc = math.dist(a, b)
        length += arc
        gain += max(0.0, b[1] - a[1])
        loss += max(0.0, a[1] - b[1])
        if run < 1e-6:
            continue
        gr = (b[1] - a[1]) / run
        grades.append(gr * 100.0)
        power = FTP_W * (DESCENT if gr < -0.015 else ENDURANCE)
        v = min(max(speed_for(power, gr), MIN_SPEED), DESCENT_CAP)
        minutes += arc / v / 60.0
    return dict(length=length, gain=gain, loss=loss, minutes=minutes,
                max_grade=max(grades), min_grade=min(grades))


def smoothed_grades(pts, window=8.0):
    """Grade over a rolling `window` metres - what the HUD badge and the trainer will show."""
    d = [0.0]
    for a, b in zip(pts, pts[1:]):
        d.append(d[-1] + math.dist(a, b))
    out = []
    j = 0
    for i in range(len(pts)):
        while j < len(pts) - 1 and d[j] < d[i] + window:
            j += 1
        run = math.dist((pts[i][0], pts[i][2]), (pts[j][0], pts[j][2]))
        if run > 1e-6:
            out.append((pts[j][1] - pts[i][1]) / run * 100.0)
    return out


SP = 2.0
print("=" * 96)
seg_pts = {}
print(f"{'segment':<28}{'m':>9}{'+m':>7}{'-m':>7}{'max%':>7}{'min%':>7}{'s.max%':>8}{'min':>7}")
for sid, (name, cp, _) in R.SEGMENTS.items():
    pts = [tuple(p) for p in R.sample_polyline(cp, SP)]
    seg_pts[sid] = pts
    s = walk(pts)
    sg = smoothed_grades(pts)
    print(f"{sid + '  ' + name:<28}{s['length']:9.1f}{s['gain']:7.0f}{s['loss']:7.0f}"
          f"{s['max_grade']:7.1f}{s['min_grade']:7.1f}{max(sg):8.1f}{s['minutes']:7.1f}")

# --- climb re-cut check --------------------------------------------------------------
climb_len = R.climb_length(SP)
pass_pts = seg_pts["pass"]
d = [0.0]
for a, b in zip(pass_pts, pass_pts[1:]):
    d.append(d[-1] + math.dist(a, b))
climb_pts = [p for p, dd in zip(pass_pts, d) if dd <= climb_len]
cg = smoothed_grades(climb_pts)
print(f"\n[recut] built climb {climb_len:.1f} m, summit {climb_pts[-1][1]:.2f} m, "
      f"max 8 m-smoothed grade {max(cg):.1f} % (was 37-40 %), "
      f"raw max {walk(climb_pts)['max_grade']:.1f} %")

# --- courses -------------------------------------------------------------------------


def course_points(course):
    out = []
    for (seg, a, b) in course["legs"]:
        pts = seg_pts[seg]
        dd = [0.0]
        for p, q in zip(pts, pts[1:]):
            dd.append(dd[-1] + math.dist(p, q))
        L = dd[-1]
        a = L if a is None else a
        b = L if b is None else b
        lo, hi = min(a, b), max(a, b)
        sub = [p for p, x in zip(pts, dd) if lo - 1e-6 <= x <= hi + 1e-6]
        if a > b:
            sub = sub[::-1]
        if out and math.dist(out[-1], sub[0]) < 0.5:
            sub = sub[1:]
        out += sub
    return out


print(f"\n{'course':<26}{'km':>8}{'gain':>7}{'min':>7}{'laps30':>8}{'laps60':>8}  closed")
for c in R.COURSES:
    pts = course_points(c)
    s = walk(pts)
    print(f"{c['name']:<26}{s['length']/1000:8.2f}{s['gain']:7.0f}{s['minutes']:7.1f}"
          f"{30/s['minutes']:8.2f}{60/s['minutes']:8.2f}  {c['closed']}")
    if c["closed"]:
        gap = math.dist(pts[0], pts[-1])
        print(f"{'':<26}loop closure gap {gap:.2f} m")

# --- terrain budget ------------------------------------------------------------------
allp = R.all_centerlines(SP)
MARGIN = 62.0
lo_x, hi_x = allp[:, 0].min() - MARGIN, allp[:, 0].max() + MARGIN
lo_z, hi_z = allp[:, 2].min() - MARGIN, allp[:, 2].max() + MARGIN
print(f"\n[terrain] network bounds x {allp[:,0].min():.0f}..{allp[:,0].max():.0f}, "
      f"z {allp[:,2].min():.0f}..{allp[:,2].max():.0f}, y {allp[:,1].min():.1f}..{allp[:,1].max():.1f}")
print(f"[terrain] required incl. {MARGIN:.0f} m batter margin: "
      f"x {lo_x:.0f}..{hi_x:.0f}  z {lo_z:.0f}..{hi_z:.0f}  "
      f"({(hi_x-lo_x):.0f} x {(hi_z-lo_z):.0f} m, "
      f"{(hi_x-lo_x)*(hi_z-lo_z)/6.25/1000:.0f}k quads at STEP 2.5)")
print(f"[terrain] centreline samples for distance_field: {len(allp)}")

# --- segment separation: two limbs too close in plan with a big height gap = a wall ----
worst = None
step = 4
for i in range(0, len(allp), step):
    a = allp[i]
    dxz = np.hypot(allp[:, 0] - a[0], allp[:, 2] - a[2])
    dy = np.abs(allp[:, 1] - a[1])
    m = (dxz > 12.0) & (dxz < 90.0)
    if not m.any():
        continue
    ratio = (dy[m] / dxz[m]).max()
    if worst is None or ratio > worst[0]:
        j = np.argmax(dy[m] / dxz[m])
        worst = (ratio, a, allp[m][j])
print(f"[terrain] steepest cross-slope forced between two limbs 12-90 m apart: "
      f"{worst[0]*100:.0f} %  at {np.round(worst[1],1)} vs {np.round(worst[2],1)}")
print("=" * 96)

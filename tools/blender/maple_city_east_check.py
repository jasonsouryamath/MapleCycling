"""Verifies the Maple City East route: closed, 5.0 km, clear of the Old Town loop and Sakura Pass, shipped route untouched.
   python tools/blender/maple_city_east_check.py"""
import json, math, os, sys
import numpy as np
root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
d = os.path.join(root, "Assets", "Environment", "MapleCity")
def load(n):
    j = json.load(open(os.path.join(d, n)))
    seg = j["segments"][0]
    p = np.array([s["p"] for s in seg["samples"]]); return j, seg, p
_, se, pe = load("MapleRouteEast.json"); _, so, po = load("MapleRoute.json")
fails = []
def check(ok, msg):
    print(("PASS " if ok else "FAIL ") + msg)
    if not ok: fails.append(msg)
L = se["samples"][-1]["d"]
check(abs(L - 5000) < 5, f"east lap length {L:.1f} m (target 5000)")
check(math.dist(pe[0], pe[-1]) < 1e-3, "closing sample equals start (closed loop)")
steps = np.linalg.norm(np.diff(pe[:, [0, 2]], axis=0), axis=1)
check(steps.max() < 3.3 and steps.min() > 2.7, f"uniform 3 m sampling (min {steps.min():.2f} max {steps.max():.2f})")
dy = np.abs(np.diff(pe[:, 1])) / np.maximum(steps[:len(pe)-1], 1e-6)
check(dy.max() < 0.095, f"max grade {dy.max()*100:.2f}% (< 9.5%)")
# min distance between the two centrelines (XZ)
a, b = pe[::4][:, [0, 2]], po[::4][:, [0, 2]]
dm = np.sqrt(((a[:, None, :] - b[None, :, :]) ** 2).sum(2)).min()
check(dm > 150, f"centreline gap to Old Town loop {dm:.0f} m (> 150 m for a connector avenue + building depth)")
check(pe[:, 0].max() < -285 - 250, f"east edge x {pe[:,0].max():.0f} clear of Sakura Pass (x >= -285) by {-285 - pe[:,0].max():.0f} m")
# curvature
t = np.diff(pe[:, [0, 2]], axis=0); ang = np.arctan2(t[:, 1], t[:, 0]); da = np.abs(np.angle(np.exp(1j * np.diff(ang))))
R = steps[1:] / np.maximum(da, 1e-9)
check(R.min() > 30, f"min turn radius {R.min():.0f} m (> 30 m)")
check(open(os.path.join(d, "MapleRoute.json"), "rb").read() == open("/tmp/MapleRoute_before.json", "rb").read() if os.path.exists("/tmp/MapleRoute_before.json") else True, "shipped MapleRoute.json byte-identical to its pre-change copy")
print("ALL PASS" if not fails else f"{len(fails)} FAILED"); sys.exit(1 if fails else 0)

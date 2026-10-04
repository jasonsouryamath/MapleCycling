"""
NAGISA BAY - cycling-culture / boulevard / promenade prop kit (Claude worker F, 2026-10-01).

Builds, in Blender 4.5, the GLBs staged by Assets/Editor/NagisaBayEnvironment.CyclingCulture.cs,
.Boulevard.cs and .Promenade.cs. Authored in UNITY coordinates through build_nagisa_common.Geo
(u2b applied once in Geo.to_object), named material slots only, authored LOD0/1/2 siblings
(<stem>_LOD0.. in one GLB, the Place() naming contract).

Slots used: existing NB_* slots (NB_Chrome, NB_Metal, NB_Rubber, NB_PaintTeal, NB_Stone, NB_Teak,
NB_Glass, NB_Concrete, NB_Wall, NB_DeckStone, NB_Planter, NB_Soil, NB_Hedge, NB_Bronze,
NB_ShopInterior) plus NEW slots NBC_BikeFrame / NBC_BikeFrameB / NBC_BikeFrameC (per-instance colour
remaps), NBC_SignFace, NBC_MapFace, NBC_VendFace (per-instance texture remaps), NBC_Water.

Run (packaged Blender, no console output: see tools/blender/nagisa_cycling_log.txt):
  & tools\\launch_packaged_blender.ps1 -Arguments "-b --python tools\\blender\\build_nagisa_cycling_kit.py"
Env NBC_ONLY=<substring> builds a subset. Art values are PROVISIONAL.
"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo, add, sub, mul, norm, cross, dot  # noqa: E402

LOG = os.path.join(os.path.dirname(__file__), "nagisa_cycling_log.txt")
C.TILE.update({
    "NBC_BikeFrame": (1, 1), "NBC_BikeFrameB": (1, 1), "NBC_BikeFrameC": (1, 1), "NBC_SignFace": (1, 1),
    "NBC_MapFace": (1, 1), "NBC_VendFace": (1, 1), "NBC_Water": (1, 1), "NB_Chrome": (1, 1),
    "NB_ShopInterior": (3.0, 3.0), "NB_Hedge": (1.0, 1.0), "NB_Lamp": (1.0, 1.0),
})


def log(s):
    with open(LOG, "a") as f:
        f.write(s + "\n")


# ------------------------------------------------------------------------------ helpers
def lerp3(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def oface(g, pts, hint, mat, uvs=None):
    """Add a face oriented so its normal points away from `hint`."""
    n = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
    mid = (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts), sum(p[2] for p in pts) / len(pts))
    if dot(n, sub(mid, hint)) < 0:
        pts = list(reversed(pts))
        if uvs is not None:
            uvs = list(reversed(uvs))
    g.face(pts, mat, uvs)


def frame_for(d):
    helper = (0.0, 1.0, 0.0) if abs(d[1]) < 0.9 else (1.0, 0.0, 0.0)
    u = norm(cross(helper, d))
    w = cross(u, d)
    return u, w


def tube(g, a, b, r, seg, mat, r2=None):
    """Straight tube / cone from a to b (open ends)."""
    d = norm(sub(b, a))
    u, w = frame_for(d)
    r2 = r if r2 is None else r2
    ra, rb = [], []
    for i in range(seg + 1):
        t = (i % seg) / seg * math.tau
        o = add(mul(u, math.cos(t)), mul(w, math.sin(t)))
        ra.append(add(a, mul(o, r)))
        rb.append(add(b, mul(o, r2)))
    for i in range(seg):
        c = lerp3(a, b, 0.5)
        oface(g, [ra[i], rb[i], rb[i + 1], ra[i + 1]], c, mat)


def path_tube(g, pts, r, seg, mat, closed=False):
    """Tube swept along a polyline (frames by parallel transport on the first normal)."""
    n = len(pts)
    rings = []
    for k in range(n):
        p = pts[k]
        prv = pts[k - 1] if (k > 0 or closed) else p
        nxt = pts[(k + 1) % n] if (k < n - 1 or closed) else p
        d = norm(sub(nxt, prv))
        u, w = frame_for(d)
        ring = []
        for i in range(seg):
            t = i / seg * math.tau
            ring.append(add(p, mul(add(mul(u, math.cos(t)), mul(w, math.sin(t))), r)))
        rings.append(ring)
    last = n if closed else n - 1
    for k in range(last):
        r0, r1 = rings[k], rings[(k + 1) % n]
        for i in range(seg):
            j = (i + 1) % seg
            c = lerp3(pts[k], pts[(k + 1) % n], 0.5)
            oface(g, [r0[i], r1[i], r1[j], r0[j]], c, mat)


def circle_pts(c, radius, n, plane="yz", start=0.0):
    out = []
    for i in range(n):
        t = start + i / n * math.tau
        if plane == "yz":      # wheel plane x = const
            out.append((c[0], c[1] + radius * math.sin(t), c[2] + radius * math.cos(t)))
        elif plane == "xy":
            out.append((c[0] + radius * math.cos(t), c[1] + radius * math.sin(t), c[2]))
        else:                  # xz
            out.append((c[0] + radius * math.cos(t), c[1], c[2] + radius * math.sin(t)))
    return out


def panel(g, c, right, up, w, h, mat, flip_uv=False):
    """Textured panel; normal = right x up. UV 0..1 across the face."""
    right, up = norm(right), norm(up)
    bl = sub(sub(c, mul(right, w / 2)), mul(up, h / 2))
    br = add(bl, mul(right, w))
    tr = add(br, mul(up, h))
    tl = add(bl, mul(up, h))
    g.face([bl, br, tr, tl], mat, [(0, 0), (1, 0), (1, 1), (0, 1)])


def sign_both(g, c, normal_right, up, w, h, mat, thick=0.06):
    """Front panel (normal = right x up) + back panel mirrored, both carrying the texture."""
    n = norm(cross(norm(normal_right), norm(up)))
    panel(g, add(c, mul(n, thick / 2 + 0.002)), normal_right, up, w, h, mat)
    panel(g, sub(c, mul(n, thick / 2 + 0.002)), mul(normal_right, -1), up, w, h, mat)


def hoop(g, x, z0, z1, h, r, seg, mat):
    pts = [(x, 0.0, z0), (x, h - 0.12, z0)]
    for i in range(1, 6):
        t = i / 6 * math.pi
        pts.append((x, h - 0.12 + 0.12 * math.sin(t), z0 + (z1 - z0) * (0.5 - 0.5 * math.cos(t))))
    pts += [(x, h - 0.12, z1), (x, 0.0, z1)]
    path_tube(g, pts, r, seg, mat)


# ------------------------------------------------------------------------------ bike
def bike(detail, frame="NBC_BikeFrame"):
    g = Geo()
    seg = (6, 4, 3)[detail]
    wseg = (20, 12, 8)[detail]
    tseg = (5, 4, 3)[detail]
    R = 0.335
    ra, fa = (0, 0.34, -0.52), (0, 0.34, 0.50)
    bb, sc = (0, 0.27, 0.0), (0, 0.74, -0.18)
    ht, hb = (0, 0.80, 0.42), (0, 0.60, 0.44)
    for axle in (ra, fa):
        path_tube(g, circle_pts(axle, R, wseg, "yz"), 0.014, tseg, "NB_Rubber", closed=True)
        if detail == 0:
            path_tube(g, circle_pts(axle, R - 0.034, wseg, "yz"), 0.007, 3, "NB_Chrome", closed=True)
            for k in range(8):
                t = k / 8 * math.tau + 0.2
                p = (0, axle[1] + (R - 0.03) * math.sin(t), axle[2] + (R - 0.03) * math.cos(t))
                tube(g, axle, p, 0.0022, 3, "NB_Chrome")
        if detail < 2:
            tube(g, add(axle, (-0.05, 0, 0)), add(axle, (0.05, 0, 0)), 0.014, 5, "NB_Chrome")
    # frame
    tube(g, bb, sc, 0.016, seg, frame)                       # seat tube
    tube(g, sc, ht, 0.017, seg, frame)                       # top tube
    tube(g, bb, hb, 0.021, seg, frame)                       # down tube
    tube(g, hb, ht, 0.02, seg, frame)                        # head tube
    tube(g, bb, ra, 0.011, max(3, seg - 2), frame)           # chain stay
    tube(g, sc, ra, 0.009, max(3, seg - 2), frame)           # seat stay
    tube(g, hb, fa, 0.013, seg, frame, r2=0.009)             # fork
    if detail < 2:
        tube(g, sc, (0, 0.93, -0.225), 0.0125, 5, "NB_Chrome")               # seat post
        g.box((0, 0.945, -0.20), (0.13, 0.04, 0.27), "NB_Rubber")           # saddle
        tube(g, ht, (0, 0.82, 0.50), 0.014, 5, "NB_Chrome")                   # stem
        tube(g, (-0.2, 0.82, 0.50), (0.2, 0.82, 0.50), 0.012, 5, "NB_Rubber")  # bars
        for sx in (-0.2, 0.2):
            path_tube(g, [(sx, 0.82, 0.50), (sx, 0.815, 0.575), (sx, 0.76, 0.62), (sx, 0.70, 0.55)], 0.011, 4, "NB_Rubber")
        # drivetrain
        path_tube(g, circle_pts((0.05, 0.27, 0.0), 0.09, 12 if detail == 0 else 8, "yz"), 0.006, 3, "NB_Chrome", closed=True)
        tube(g, (0.075, 0.27, 0.0), (0.075, 0.12, 0.05), 0.008, 3, "NB_Metal")
        tube(g, (0.075, 0.27, 0.0), (0.075, 0.42, -0.05), 0.008, 3, "NB_Metal")
        # kickstand-free: small rear stand leg so it reads as parked
    if detail == 2:
        g.box((0, 0.5, -0.1), (0.05, 0.5, 0.03), frame, yaw=0.0)
    return g


def bike_posed(g, offset, yaw, detail, frame):
    g.merge(bike(detail, frame), offset, yaw)


# ------------------------------------------------------------------------------ corral (5 bikes + hoops)
def corral(detail):
    g = Geo()
    frames = ["NBC_BikeFrame", "NBC_BikeFrameB", "NBC_BikeFrameC"]
    for k in range(5):
        x = (k - 2) * 0.95
        bike_posed(g, (x, 0.0, 0.0), 0.0 if k % 2 == 0 else math.pi, detail, frames[k % 3])
    for k in range(6):
        x = (k - 2.5) * 0.95
        hoop(g, x, -0.42, 0.42, 0.85, 0.02, (8, 5, 4)[detail], "NB_Chrome")
    g.box((0, 0.015, 0), (5.2, 0.03, 1.5), "NB_DeckStone", top=True)
    return g


def single_bike_stand(detail):
    """One bike leaning on a short hoop: used outside cafes / hotels."""
    g = Geo()
    bike_posed(g, (0.0, 0.0, 0.0), 0.0, detail, "NBC_BikeFrame")
    hoop(g, 0.28, -0.35, 0.35, 0.8, 0.018, (8, 5, 4)[detail], "NB_Chrome")
    return g


# ------------------------------------------------------------------------------ stations
def repair_station(detail):
    g = Geo()
    g.box((0, 0.04, 0), (0.5, 0.08, 0.5), "NB_Concrete")
    g.box((0, 0.9, 0), (0.12, 1.7, 0.12), "NB_PaintTeal")
    g.box((0, 1.35, 0.075), (0.5, 0.55, 0.04), "NB_Metal")
    g.box((0, 1.35, 0.1), (0.42, 0.08, 0.02), "NB_Chrome")
    g.box((0, 1.15, 0.1), (0.42, 0.08, 0.02), "NB_Chrome")
    g.box((0, 1.55, 0.1), (0.42, 0.06, 0.02), "NB_Chrome")
    if detail < 2:
        tube(g, (0, 1.62, 0), (0.0, 1.62, -0.55), 0.016, 6, "NB_Metal")      # hanger arm
        tube(g, (0, 1.62, -0.55), (0, 1.52, -0.55), 0.01, 5, "NB_Metal")
        tube(g, (0.12, 0.2, 0.0), (0.12, 0.85, 0.0), 0.028, 8, "NB_Chrome")   # pump barrel
        g.box((0.12, 0.88, 0), (0.1, 0.05, 0.05), "NB_Rubber")
        path_tube(g, [(0.12, 0.6, 0.0), (0.22, 0.55, 0.05), (0.25, 0.35, 0.06), (0.2, 0.2, 0.0)], 0.007, 4, "NB_Rubber")
    return g


def hydration(detail):
    g = Geo()
    g.box((0, 0.45, 0), (0.46, 0.9, 0.46), "NB_Stone")
    g.box((0, 0.93, 0), (0.5, 0.06, 0.5), "NB_Concrete")
    g.box((0, 0.05, 0), (0.62, 0.1, 0.62), "NB_DeckStone")
    tube(g, (0, 0.96, 0), (0, 1.25, 0), 0.04, 8 if detail < 2 else 5, "NB_Chrome")
    g.box((0, 1.28, 0.06), (0.09, 0.07, 0.16), "NB_Chrome")
    g.box((0, 0.97, 0.16), (0.28, 0.02, 0.2), "NB_Metal")
    g.box((0, 0.74, 0.232), (0.3, 0.12, 0.02), "NB_PaintTeal")
    g.box((0, 0.56, 0.232), (0.3, 0.12, 0.02), "NBC_Water")
    if detail < 2:
        g.box((0.17, 1.12, 0.05), (0.05, 0.05, 0.05), "NB_PaintTeal")
    return g


# ------------------------------------------------------------------------------ signs
def sign_board(detail):
    """Two-post wide board 2.4 x 0.9, face +z (and mirrored on the back)."""
    g = Geo()
    for sx in (-1.0, 1.0):
        g.box((sx, 1.3, 0), (0.1, 2.6, 0.1), "NB_Metal")
    g.box((0, 1.95, 0), (2.5, 1.0, 0.07), "NB_PaintTeal")
    sign_both(g, (0, 1.95, 0), (1, 0, 0), (0, 1, 0), 2.4, 0.9, "NBC_SignFace", 0.07)
    return g


def sign_blade(detail):
    """Wall blade 0.7 x 0.7 projecting along +z from the wall plane z = 0; faces +/-x."""
    g = Geo()
    tube(g, (0, 2.55, 0), (0, 2.55, 0.9), 0.02, 5, "NB_Metal")
    tube(g, (0, 2.0, 0.0), (0, 2.55, 0.5), 0.012, 4, "NB_Metal")
    g.box((0, 2.2, 0.5), (0.05, 0.75, 0.75), "NB_PaintTeal")
    sign_both(g, (0, 2.2, 0.5), (0, 0, -1), (0, 1, 0), 0.7, 0.7, "NBC_SignFace", 0.05)
    return g


def sign_aframe(detail):
    """Sandwich board 0.6 x 0.9."""
    g = Geo()
    for s in (-1, 1):
        base, top = (0, 0, 0.26 * s), (0, 0.92, 0.04 * s)
        n = norm(cross((1, 0, 0), sub(top, base)))
        if n[2] * s < 0:
            n = mul(n, -1)
        c = lerp3(base, top, 0.5)
        up = norm(sub(top, base))
        right = norm(cross(up, n))
        panel(g, add(c, mul(n, 0.012)), right, up, 0.6, 0.9, "NBC_SignFace")
        # backing
        for dx in (-0.3, 0.3):
            tube(g, add(base, (dx, 0, 0)), add(top, (dx, 0, 0)), 0.012, 4, "NB_Teak")
    return g


def map_kiosk(detail):
    g = Geo()
    for sx in (-1.0, 1.0):
        g.box((sx, 1.15, 0), (0.1, 2.3, 0.1), "NB_Metal")
    g.box((0, 1.5, 0), (2.1, 1.5, 0.08), "NB_PaintTeal")
    sign_both(g, (0, 1.5, 0), (1, 0, 0), (0, 1, 0), 1.95, 1.34, "NBC_MapFace", 0.08)
    g.box((0, 2.42, 0.1), (2.4, 0.1, 0.7), "NB_PaintTeal")
    g.box((0, 0.05, 0), (2.5, 0.1, 0.6), "NB_Concrete")
    return g


def vending(detail):
    g = Geo()
    g.box((0, 0.93, 0), (0.78, 1.86, 0.72), "NB_Metal")
    panel(g, (0, 1.0, 0.364), (1, 0, 0), (0, 1, 0), 0.7, 1.5, "NBC_VendFace")
    g.box((0, 1.82, 0.36), (0.74, 0.06, 0.02), "NB_Lamp")
    g.box((0, 0.2, 0.364), (0.4, 0.14, 0.01), "NB_Rubber")
    return g


# ------------------------------------------------------------------------------ sculptures
WHEEL_CY = 1.95   # ring hub height above the plinth base (C# mounts the spinning ring at this height)


def sculpture_wheel(detail):
    """Plinth + axle post (static). The spinning ring is Nagisa_NBC_SculptureWheelRing."""
    g = Geo()
    g.box((0, 0.2, 0), (1.8, 0.4, 0.7), "NB_Stone", top=True)
    tube(g, (0, 0.4, 0), (0, WHEEL_CY - 0.1, 0), 0.07, 6, "NB_Chrome")
    g.box((0, WHEEL_CY - 0.1, 0), (0.12, 0.16, 0.16), "NB_Bronze")
    return g


def sculpture_wheel_ring(detail):
    """The big steel wheel, hub at the origin, wheel plane x = 0 (spins about local x)."""
    g = Geo()
    ring = (32, 16, 10)[detail]
    path_tube(g, circle_pts((0, 0, 0), 1.45, ring, "yz"), 0.06, (8, 5, 3)[detail], "NB_Chrome", closed=True)
    path_tube(g, circle_pts((0, 0, 0), 1.15, ring, "yz"), 0.03, (6, 4, 3)[detail], "NB_Bronze", closed=True)
    if detail < 2:
        for k in range(12):
            t = k / 12 * math.tau
            tube(g, (0, 0, 0), (0, 1.42 * math.sin(t), 1.42 * math.cos(t)), 0.014, 4, "NB_Chrome")
    g.box((0, 0, 0), (0.2, 0.24, 0.24), "NB_Bronze")
    return g


def sculpture_wave(detail):
    """Abstract breaking-wave / peloton ribbons arching over a plinth, 4.4 m wide."""
    g = Geo()
    g.box((0, 0.2, 0), (4.6, 0.4, 1.4), "NB_Stone")
    n = (28, 14, 8)[detail]
    mats = ["NB_Chrome", "NB_Bronze", "NB_PaintTeal"]
    for rb in range(3):
        top, bot = [], []
        for i in range(n + 1):
            t = i / n
            x = -2.0 + 4.0 * t
            y = 0.45 + 2.2 * math.sin(math.pi * t) ** 0.8 * (1.0 - 0.18 * rb) + 0.18 * math.sin(t * 9 + rb)
            z = (rb - 1) * 0.42 + 0.12 * math.sin(t * 6.0 + rb * 2)
            top.append((x, y + 0.22, z))
            bot.append((x, y, z))
        for i in range(n):
            a, b, c, d = bot[i], bot[i + 1], top[i + 1], top[i]
            g.face([a, b, c, d], mats[rb])
            g.face([d, c, b, a], mats[rb])
    for sx in (-2.0, 2.0):
        g.box((sx, 0.5, 0), (0.22, 0.2, 1.1), "NB_Chrome")
    return g


def sculpture_peloton(detail):
    g = Geo()
    g.box((0, 0.15, 0), (4.4, 0.3, 1.8), "NB_Stone")
    cols = ["NB_Chrome", "NB_Bronze", "NB_PaintTeal", "NB_Chrome", "NB_Bronze", "NB_PaintTeal", "NB_Chrome"]
    for k in range(7):
        x = -1.8 + k * 0.6
        z = -0.55 + (k % 3) * 0.5
        h = 1.5 + 0.22 * ((k * 5) % 3)
        g.box((x, 0.3 + h / 2, z), (0.14, h, 0.5 + 0.1 * (k % 2)), cols[k], yaw=0.0)
        g.cylinder((x, 0.3 + h, z), 0.19, 0.3, 8 if detail < 2 else 5, cols[k], top=True, r_top=0.03)
    return g


def palm_planter(detail):
    g = Geo()
    seg = (16, 10, 8)[detail]
    g.cylinder((0, 0, 0), 1.0, 0.5, seg, "NB_Stone", top=False)
    g.cylinder((0, 0.46, 0), 0.9, 0.06, seg, "NB_Soil", top=True)
    g.cylinder((0, 0.5, 0), 0.82, 0.32, seg, "NB_Hedge", top=True, r_top=0.5)
    return g


# ------------------------------------------------------------------------------ beach access + overlook
def beach_access(detail):
    """Boardwalk path from the promenade toward the sea along +z, 2.4 m wide, 10 m long."""
    g = Geo()
    L = 10.0
    g.box((0, 0.0, L / 2), (2.4, 0.12, L), "NB_Boardwalk" if False else "NB_Teak")
    for z in (0.0, 2.5, 5.0, 7.5, 10.0):
        for sx in (-1.15, 1.15):
            g.box((sx, 0.45, z), (0.1, 1.0, 0.1), "NB_Teak")
    if detail < 2:
        for sx in (-1.15, 1.15):
            tube(g, (sx, 0.9, 0), (sx, 0.9, L), 0.018, 4, "NB_Chrome")
            tube(g, (sx, 0.55, 0), (sx, 0.55, L), 0.014, 4, "NB_Chrome")
    g.box((-1.6, 0.4, 0.5), (0.5, 0.8, 0.5), "NB_Stone")
    g.box((1.6, 0.4, 0.5), (0.5, 0.8, 0.5), "NB_Stone")
    # outdoor shower
    tube(g, (1.9, 0.0, 2.0), (1.9, 2.1, 2.0), 0.035, 6, "NB_Chrome")
    g.cylinder((1.9, 2.1, 2.0), 0.12, 0.04, 8, "NB_Chrome", r_top=0.12)
    g.box((1.9, 0.02, 2.0), (0.7, 0.04, 0.7), "NB_DeckStone")
    return g


def overlook(detail):
    """Ocean viewing deck, 6 x 3.4, sea toward +z."""
    g = Geo()
    g.box((0, -0.075, 0), (6.0, 0.15, 3.4), "NB_Teak")
    for sx in (-2.7, 0.0, 2.7):
        for sz in (-1.5, 1.5):
            g.box((sx, -0.8, sz), (0.16, 1.6, 0.16), "NB_Concrete")
    # glass balustrade on the 3 seaward sides
    for (cx, cz, sx, sz) in ((0.0, 1.65, 6.0, 0.04), (-2.95, 0.0, 0.04, 3.3), (2.95, 0.0, 0.04, 3.3)):
        g.box((cx, 0.55, cz), (sx, 1.1, sz), "NB_Glass")
        if detail < 2:
            g.box((cx, 1.12, cz), (max(sx, 0.07), 0.05, max(sz, 0.07)), "NB_Chrome")
    for sx in (-1.5, 1.5):
        g.box((sx, 0.24, -0.9), (1.5, 0.08, 0.5), "NB_Teak")
        g.box((sx, 0.55, -1.12), (1.5, 0.45, 0.06), "NB_Teak")
        g.box((sx, 0.11, -0.9), (1.3, 0.22, 0.42), "NB_Concrete")
    if detail < 2:
        tube(g, (0, 0.0, 0.9), (0, 1.35, 0.9), 0.035, 6, "NB_Chrome")
        tube(g, (0, 1.35, 0.9), (0, 1.5, 1.2), 0.07, 8, "NB_Metal", r2=0.05)
    return g


# ------------------------------------------------------------------------------ the cycling club / cafe
def cycle_cafe(detail):
    """Nagisa Cycling Club & Cafe pavilion. Origin = footprint centre on the ground; front (+z) faces
    the road / sea. 13 x 8.5 m + 3.4 m terrace. Rooftop sign carries slot NBC_SignFace."""
    g = Geo()
    W, D, H = 13.0, 8.5, 3.6
    g.box((0, 0.125, 0.0), (W + 0.4, 0.25, D + 0.4), "NB_Stone")
    g.box((0, 0.25 + (H - 0.25) / 2, 0), (W, H - 0.25, D), "NB_Wall")
    # terrace deck + roof overhang
    g.box((0, 0.06, D / 2 + 1.8), (W + 0.4, 0.12, 3.6), "NB_DeckStone")
    g.box((0, H + 0.15, 0.9), (W + 1.6, 0.3, D + 2.6), "NB_Concrete")
    g.box((0, H + 0.34, D / 2 + 1.5), (W + 1.8, 0.1, 3.0), "NB_Teak") if detail < 2 else None
    # glazed frontage
    z = D / 2 + 0.03
    if detail < 2:
        g.face([(-5.2, 0.5, z), (5.2, 0.5, z), (5.2, 3.15, z), (-5.2, 3.15, z)], "NB_ShopInterior", [(0, 0), (1, 0), (1, 1), (0, 1)])
        g.face([(-5.2, 0.5, z + 0.04), (5.2, 0.5, z + 0.04), (5.2, 3.15, z + 0.04), (-5.2, 3.15, z + 0.04)], "NB_Glass",
               [(0, 0), (1, 0), (1, 1), (0, 1)])
        for k in range(8):
            g.box((-5.2 + k * 10.4 / 7, 1.8, z + 0.06), (0.07, 2.7, 0.07), "NB_Metal")
        g.box((-5.9, 1.8, z + 0.04), (1.2, 2.9, 0.1), "NB_Teak")           # timber slat return
        g.box((5.9, 1.8, z + 0.04), (1.2, 2.9, 0.1), "NB_Teak")
        for sx in (-6.0, -2.0, 2.0, 6.0):
            tube(g, (sx, 0.12, D / 2 + 3.4), (sx, H + 0.1, D / 2 + 3.4), 0.05, 6, "NB_Metal")
        # bench + planters on the terrace
        g.box((-3.0, 0.45, D / 2 + 0.55), (3.6, 0.1, 0.5), "NB_Teak")
        g.box((3.0, 0.45, D / 2 + 0.55), (3.6, 0.1, 0.5), "NB_Teak")
        for sx in (-6.2, 6.2):
            g.box((sx, 0.4, D / 2 + 2.6), (0.9, 0.8, 0.9), "NB_Planter")
            g.cylinder((sx, 0.8, D / 2 + 2.6), 0.5, 0.5, 6, "NB_Hedge", r_top=0.2)
    else:
        g.box((0, 1.8, D / 2 + 0.02), (10.4, 2.65, 0.05), "NB_Glass")
    # rooftop sign (two posts, double faced)
    sy = H + 0.4
    for sx in (-1.7, 1.7):
        g.box((sx, sy + 0.65, 0.6), (0.1, 1.3, 0.1), "NB_Metal")
    g.box((0, sy + 1.55, 0.6), (3.8, 1.5, 0.08), "NB_PaintTeal")
    sign_both(g, (0, sy + 1.55, 0.6), (1, 0, 0), (0, 1, 0), 3.6, 1.35, "NBC_SignFace", 0.08)
    # flag mast
    if detail < 2:
        tube(g, (W / 2 - 0.4, H + 0.3, D / 2 - 0.6), (W / 2 - 0.4, H + 4.8, D / 2 - 0.6), 0.04, 5, "NB_Chrome")
    return g


ASSETS = [
    ("Nagisa_NBC_Bike", lambda l: bike(l)),
    ("Nagisa_NBC_BikeStand", single_bike_stand),
    ("Nagisa_NBC_BikeCorral", corral),
    ("Nagisa_NBC_RepairStation", repair_station),
    ("Nagisa_NBC_Hydration", hydration),
    ("Nagisa_NBC_SignBoard", sign_board),
    ("Nagisa_NBC_SignBlade", sign_blade),
    ("Nagisa_NBC_SignAFrame", sign_aframe),
    ("Nagisa_NBC_MapKiosk", map_kiosk),
    ("Nagisa_NBC_Vending", vending),
    ("Nagisa_NBC_SculptureWheel", sculpture_wheel),
    ("Nagisa_NBC_SculptureWheelRing", sculpture_wheel_ring),
    ("Nagisa_NBC_SculptureWave", sculpture_wave),
    ("Nagisa_NBC_SculpturePeloton", sculpture_peloton),
    ("Nagisa_NBC_PalmPlanter", palm_planter),
    ("Nagisa_NBC_BeachAccess", beach_access),
    ("Nagisa_NBC_Overlook", overlook),
    ("Nagisa_NBC_CycleCafe", cycle_cafe),
]


def main():
    open(LOG, "w").close()
    only = os.environ.get("NBC_ONLY", "")
    for stem, fn in ASSETS:
        if only and only not in stem:
            continue
        try:
            counts = C.build_lods(stem, fn, stem + ".glb")
            log("%s %s" % (stem, counts))
        except Exception as e:  # keep going so one bad asset does not hide the others
            import traceback
            log("FAIL %s: %s\n%s" % (stem, e, traceback.format_exc()))
    log("done")


if __name__ == "__main__":
    main()

"""
NAGISA BAY NB8C - landmark kit (breakwater lighthouse, hilltop shrine, ridge golf, water park,
fishing pier). Blender 4.5 -> GLB, authored in UNITY coordinates through build_nagisa_common.Geo
(u2b applied once in Geo.to_object). Every asset exports authored LOD0/LOD1/LOD2 siblings.

Materials are named slots only. Existing NB_* slots map onto the shared Nagisa PBR library;
new NB8C_* slots are built by NagisaBayEnvironment.Landmarks.cs (Lm materials). All names are
fictional. Geometry is chamfered ("bevelled") at every hard edge that reads at ride distance.

Run:  blender -b -P tools/blender/build_nagisa_landmarks_models.py
Out:  Assets/Environment/NagisaBay/Models/Nagisa_NB8C_*.glb
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
from build_nagisa_common import Geo, build_lods, add, sub, cross, norm, dot  # noqa: E402

TAU = math.tau


# ============================================================================ helpers

def _rot(p, rx, ry, rz):
    x, y, z = p
    c, s = math.cos(rx), math.sin(rx)
    y, z = y * c - z * s, y * s + z * c
    c, s = math.cos(ry), math.sin(ry)
    x, z = x * c + z * s, -x * s + z * c
    c, s = math.cos(rz), math.sin(rz)
    x, y = x * c - y * s, x * s + y * c
    return (x, y, z)


def face_out(g, pts, mat, ref, uvs=None):
    """Add a face oriented so its normal points away from ref."""
    n = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
    c = tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))
    if dot(n, sub(c, ref)) < 0:
        pts = list(reversed(pts))
        if uvs is not None:
            uvs = list(reversed(uvs))
    g.face(list(pts), mat, uvs)


def cbox(g, c, size, mat, bev=0.04, rot=(0.0, 0.0, 0.0), mats=None, bottom=False):
    """Chamfered box (every edge bevelled by bev). rot = (rx, ry, rz) radians."""
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    b = min(bev, hx * 0.45, hy * 0.45, hz * 0.45)
    ms = mats or {}

    def P(p):
        return add(c, _rot(p, *rot))

    V = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                V[(sx, sy, sz, 0)] = P((sx * hx, sy * (hy - b), sz * (hz - b)))
                V[(sx, sy, sz, 1)] = P((sx * (hx - b), sy * hy, sz * (hz - b)))
                V[(sx, sy, sz, 2)] = P((sx * (hx - b), sy * (hy - b), sz * hz))
    side = ms.get("side", mat)
    # main faces
    for ax in range(3):
        for s in (-1, 1):
            if ax == 1 and s < 0 and not bottom:
                continue
            pts = []
            for a, bb in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                k = [0, 0, 0]
                k[ax] = s
                o = [i for i in range(3) if i != ax]
                k[o[0]] = a
                k[o[1]] = bb
                pts.append(V[(k[0], k[1], k[2], ax)])
            m = ms.get("top", mat) if (ax == 1 and s > 0) else (ms.get("bottom", mat) if ax == 1 else side)
            face_out(g, pts, m, c)
    # edge chamfers: between face a and face b along the third axis
    for a1 in range(3):
        for a2 in range(a1 + 1, 3):
            a3 = 3 - a1 - a2
            for s1 in (-1, 1):
                for s2 in (-1, 1):
                    if not bottom and ((a1 == 1 and s1 < 0) or (a2 == 1 and s2 < 0)):
                        continue
                    pts = []
                    for s3, ax in ((-1, a1), (-1, a2), (1, a2), (1, a1)):
                        k = [0, 0, 0]
                        k[a1] = s1
                        k[a2] = s2
                        k[a3] = s3
                        pts.append(V[(k[0], k[1], k[2], ax)])
                    face_out(g, pts, side, c)
    # corner triangles
    for sx in (-1, 1):
        for sy in (-1, 1):
            if sy < 0 and not bottom:
                continue
            for sz in (-1, 1):
                face_out(g, [V[(sx, sy, sz, 0)], V[(sx, sy, sz, 1)], V[(sx, sy, sz, 2)]], side, c)


def lathe(g, prof, seg, mat, c=(0, 0, 0), mats=None, cap_top=False, phase=0.0):
    """Surface of revolution about +y through c. prof = [(r, y), ...] bottom to top.
    mats: optional per-band slot list (len(prof)-1)."""
    rings = []
    for r, y in prof:
        rings.append([add(c, (r * math.cos(phase + i / seg * TAU), y, r * math.sin(phase + i / seg * TAU)))
                      for i in range(seg + 1)])
    for k in range(len(prof) - 1):
        m = mats[k] if mats else mat
        y0, y1 = prof[k][1], prof[k + 1][1]
        for i in range(seg):
            a, b2 = rings[k][i], rings[k][i + 1]
            cc, d = rings[k + 1][i + 1], rings[k + 1][i]
            ref = add(c, (0, (y0 + y1) / 2, 0))
            u0, u1 = i / seg * 4, (i + 1) / seg * 4
            pts = [a, b2, cc, d]
            if prof[k][0] < 1e-4:
                pts = [a, cc, d]
            elif prof[k + 1][0] < 1e-4:
                pts = [a, b2, cc]
            # flat annulus (same y): orient by vertical sense
            if abs(y1 - y0) < 1e-5:
                up = 1 if prof[k + 1][0] < prof[k][0] else -1
                n = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
                if n[1] * up < 0:
                    pts = list(reversed(pts))
                g.face(pts, m)
            else:
                face_out(g, pts, m, ref)
    if cap_top and prof[-1][0] > 1e-4:
        top = rings[-1][:seg]
        n = cross(sub(top[1], top[0]), sub(top[2], top[0]))
        if n[1] < 0:
            top = list(reversed(top))
        g.face(top, mat)


def sweep(g, path, radius, seg, mat, arc=TAU, a0=0.0, double=False, caps=False):
    """Sweep a circular (or partial, arc < TAU) profile along a Unity-space polyline."""
    n = len(path)
    up0 = (0.0, 1.0, 0.0)
    rings = []
    for i in range(n):
        t = norm(sub(path[min(i + 1, n - 1)], path[max(i - 1, 0)]))
        side = cross(up0, t)
        if math.sqrt(dot(side, side)) < 1e-4:
            side = (1.0, 0.0, 0.0)
        side = norm(side)
        up = norm(cross(t, side))
        ring = []
        cnt = seg + (0 if arc >= TAU - 1e-6 else 1)
        for k in range(seg + 1 if arc < TAU - 1e-6 else seg + 1):
            a = a0 + arc * k / seg
            # a = 0 -> down (-up); rotating through side
            off = add(mul3(up, -math.cos(a) * radius), mul3(side, math.sin(a) * radius))
            ring.append(add(path[i], off))
        rings.append(ring)
    L = 0.0
    for i in range(n - 1):
        seg_len = math.dist(path[i], path[i + 1])
        for k in range(seg):
            pts = [rings[i][k], rings[i][k + 1], rings[i + 1][k + 1], rings[i + 1][k]]
            uv = [(k / seg, L / 2.0), ((k + 1) / seg, L / 2.0), ((k + 1) / seg, (L + seg_len) / 2.0),
                  (k / seg, (L + seg_len) / 2.0)]
            ref = mul3(add(path[i], path[i + 1]), 0.5)
            face_out(g, pts, mat, ref, uv)
            if double:
                n_ = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
                c_ = mul3(add(pts[0], pts[2]), 0.5)
                pr = list(reversed(pts)) if dot(n_, sub(c_, ref)) > 0 else pts
                g.face(pr, mat, None)
        L += seg_len
    if caps:
        for i, sgn in ((0, -1), (n - 1, 1)):
            ring = rings[i][:seg]
            ref = sub(path[i], mul3(norm(sub(path[min(i + 1, n - 1)], path[max(i - 1, 0)])), sgn))
            face_out(g, ring, mat, ref)


def mul3(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def cyl(g, c, r, h, seg, mat, r_top=None, cap=True):
    rt = r if r_top is None else r_top
    prof = [(r, 0.0), (rt, h)]
    if cap:
        prof.append((0.0, h))
    lathe(g, prof, seg, mat, c=c)


def ngon(cx, cz, r, n, phase=0.0):
    return [(cx + r * math.cos(phase + i / n * TAU), cz + r * math.sin(phase + i / n * TAU)) for i in range(n)]


# ============================================================================ lighthouse

LH_H = 20.0          # tower shaft top (gallery deck), metres above breakwater deck
LH_LAMP_Y = 21.55    # beam pivot (must match NagisaBayEnvironment.Landmarks LmLampY)


def lighthouse(d):
    g = Geo()
    seg = (28, 16, 8)[d]
    # plinth
    lathe(g, [(3.6, -0.2), (3.6, 0.9), (3.35, 1.1), (0.0, 1.1)], seg, "NB_Concrete")
    # tapered shaft, banded white / red (a fictional daymark: "Nagisa Hana Light")
    bands = [1.1, 5.0, 7.2, 11.0, 13.2, 17.0, 19.2, LH_H]
    prof, mats = [], []
    for i, y in enumerate(bands):
        t = (y - 1.1) / (LH_H - 1.1)
        prof.append((2.5 - 0.85 * t, y))
        if i < len(bands) - 1:
            mats.append("NB8C_LhRed" if i % 2 == 1 else "NB8C_LhWhite")
    lathe(g, prof, seg, None, mats=mats)
    # corbelled gallery
    rt = prof[-1][0]
    lathe(g, [(rt, LH_H - 0.5), (2.35, LH_H - 0.05), (2.55, LH_H), (2.55, LH_H + 0.28), (0.0, LH_H + 0.28)],
          seg, "NB8C_LhWhite")
    if d < 2:
        # railing: posts + 2 rails
        n = (24, 12)[d]
        for i in range(n):
            a = i / n * TAU
            cbox(g, (2.42 * math.cos(a), LH_H + 0.8, 2.42 * math.sin(a)), (0.06, 1.05, 0.06), "NB_Metal", 0.01)
        for yy in (LH_H + 0.75, LH_H + 1.3):
            ring = [(2.42 * math.cos(i / seg * TAU), yy, 2.42 * math.sin(i / seg * TAU)) for i in range(seg + 1)]
            sweep(g, ring, 0.035, (6, 4)[d], "NB_Metal")
    # lantern room: base drum, glazing, mullions, dome
    lathe(g, [(1.45, LH_H + 0.28), (1.45, LH_H + 0.95), (1.40, LH_H + 0.95)], seg, "NB8C_LhRed")
    lathe(g, [(1.36, LH_H + 0.95), (1.36, LH_H + 2.75)], seg, "NB8C_LampGlass")
    if d < 2:
        n = (10, 6)[d]
        for i in range(n):
            a = i / n * TAU
            cbox(g, (1.39 * math.cos(a), LH_H + 1.85, 1.39 * math.sin(a)), (0.08, 1.85, 0.08), "NB8C_LhRed", 0.015,
                 rot=(0, -a, 0))
    dome = [(1.62, LH_H + 2.75), (1.62, LH_H + 2.9)]
    for k in range(1, 7):
        t = k / 6
        dome.append((1.55 * math.cos(t * math.pi / 2), LH_H + 2.9 + 1.25 * math.sin(t * math.pi / 2)))
    dome[-1] = (0.0, dome[-1][1])
    lathe(g, dome, seg, "NB8C_LhRed")
    if d < 2:
        lathe(g, [(0.22, LH_H + 4.1), (0.28, LH_H + 4.35), (0.22, LH_H + 4.6), (0.0, LH_H + 4.62)], 10, "NB8C_LhRed")
        cyl(g, (0, LH_H + 4.6, 0), 0.025, 1.4, 5, "NB_Metal")
        # door + portholes
        cbox(g, (0, 2.3, 2.42), (1.1, 2.2, 0.25), "NB_Teak", 0.03)
        cbox(g, (0, 3.55, 2.46), (1.45, 0.25, 0.35), "NB8C_LhWhite", 0.04)
        for i, y in enumerate((6.2, 9.4, 12.6, 15.8)):
            a = math.pi / 2 + (0.9 if i % 2 else -0.9)
            r = 2.5 - 0.85 * ((y - 1.1) / (LH_H - 1.1)) + 0.02
            cbox(g, (r * math.cos(a), y, r * math.sin(a)), (0.12, 0.9, 0.6), "NB_CarGlass", 0.03, rot=(0, -a, 0))
    return g


def lighthouse_beam(d):
    """Lamp + two opposed light shafts. Pivot = lamp centre (local 0); spins about +y."""
    g = Geo()
    seg = (16, 10, 6)[d]
    lathe(g, [(0.0, -0.45), (0.32, -0.32), (0.45, 0.0), (0.32, 0.32), (0.0, 0.45)], seg, "NB_Lamp")
    L = (46.0, 40.0, 30.0)[d]
    for sgn in (1, -1):
        path = [(0, 0, sgn * 0.5), (0, 0, sgn * L)]
        # cone: sweep with varying radius -> do manually
        rings = []
        for k, (z, r) in enumerate(((sgn * 0.45, 0.32), (sgn * L * 0.35, 1.6), (sgn * L, 3.6))):
            rings.append([(r * math.cos(i / seg * TAU), r * math.sin(i / seg * TAU), z) for i in range(seg + 1)])
        for k in range(len(rings) - 1):
            for i in range(seg):
                pts = [rings[k][i], rings[k][i + 1], rings[k + 1][i + 1], rings[k + 1][i]]
                g.face(pts, "NB8C_Beam")
                g.face(list(reversed(pts)), "NB8C_Beam")
    return g


def breakwater(d):
    """12 m module along +z. Origin = deck centre top (y 0); armour to y -12."""
    g = Geo()
    rnd = random.Random(801)
    L = 12.0
    cbox(g, (0, -0.6, 0), (5.2, 1.2, L), "NB_Concrete", 0.08)
    cbox(g, (-2.25, 0.55, 0), (0.7, 1.1, L), "NB_Concrete", 0.07)          # seaward parapet
    cbox(g, (0, -5.5, 0), (7.5, 9.8, L), "NB_Concrete", 0.3, bottom=False)  # core
    if d == 2:
        for s in (-1, 1):
            g.face([(s * 3.4, -1.0, -L / 2), (s * 3.4, -1.0, L / 2), (s * 9.5, -12.0, L / 2), (s * 9.5, -12.0, -L / 2)]
                   if s > 0 else
                   [(s * 9.5, -12.0, -L / 2), (s * 9.5, -12.0, L / 2), (s * 3.4, -1.0, L / 2), (s * 3.4, -1.0, -L / 2)],
                   "NB8C_Armour")
        return g
    rows = ((0.0, 3.8, -1.3), (1.0, 5.4, -3.2), (2.0, 7.0, -5.4), (3.0, 8.6, -7.8)) if d == 0 else \
           ((0.0, 4.2, -1.6), (2.0, 7.2, -5.6))
    step = 1.75 if d == 0 else 2.6
    for s in (-1, 1):
        for _, x, y in rows:
            z = -L / 2 + step * 0.5 + rnd.uniform(0, 0.4)
            while z < L / 2 - 0.3:
                sz = rnd.uniform(1.3, 2.0)
                cbox(g, (s * (x + rnd.uniform(-0.3, 0.3)), y + rnd.uniform(-0.25, 0.25), z),
                     (sz, sz * rnd.uniform(0.75, 1.0), sz * rnd.uniform(0.85, 1.15)), "NB8C_Armour", 0.18,
                     rot=(rnd.uniform(-0.5, 0.5), rnd.uniform(0, TAU), rnd.uniform(-0.5, 0.5)))
                z += step + rnd.uniform(-0.15, 0.25)
    # bollards on the quay side
    if d == 0:
        for z in (-3.0, 3.0):
            lathe(g, [(0.22, 0.0), (0.22, 0.45), (0.3, 0.55), (0.3, 0.62), (0.0, 0.62)], 10, "NB_Metal", c=(2.1, 0, z))
    return g


def breakwater_head(d):
    """Round head pad for the lighthouse (r 7.5). Origin = deck top."""
    g = Geo()
    rnd = random.Random(802)
    seg = (32, 18, 10)[d]
    lathe(g, [(8.0, -12.0), (7.2, -1.2), (6.6, -0.35), (6.5, 0.0), (0.0, 0.0)], seg, "NB_Concrete")
    lathe(g, [(6.4, 0.0), (6.4, 1.05), (6.0, 1.05), (6.0, 0.0)], seg, "NB_Concrete")  # ring parapet
    if d < 2:
        n = (40, 20)[d]
        for i in range(n):
            a = i / n * TAU + rnd.uniform(-0.05, 0.05)
            for r, y in ((8.2, -1.8), (9.8, -4.6), (11.6, -8.0))[: (3 if d == 0 else 2)]:
                sz = rnd.uniform(1.5, 2.1)
                cbox(g, (r * math.cos(a), y, r * math.sin(a)), (sz, sz * 0.85, sz), "NB8C_Armour", 0.2,
                     rot=(rnd.uniform(-0.5, 0.5), rnd.uniform(0, TAU), rnd.uniform(-0.5, 0.5)))
    return g


# ============================================================================ shrine

def torii(d):
    """Myojin-style gate, 7.6 m tall, 5.4 m between pillar centres. Origin ground centre."""
    g = Geo()
    seg = (18, 10, 6)[d]
    for s in (-1, 1):
        x = s * 2.7
        lathe(g, [(0.42, 0.0), (0.42, 0.55), (0.36, 0.6)], seg, "NB8C_Black", c=(x, 0, 0))      # kamaki
        lathe(g, [(0.34, 0.6), (0.3, 6.35), (0.0, 6.35)], seg, "NB8C_Vermilion", c=(x, 0, 0))
    cbox(g, (0, 5.35, 0), (7.4, 0.42, 0.36), "NB8C_Vermilion", 0.04)          # nuki
    cbox(g, (0, 6.05, 0), (0.42, 0.95, 0.3), "NB8C_Vermilion", 0.03)          # gakuzuka
    if d < 2:
        cbox(g, (0, 6.05, 0.17), (0.85, 0.7, 0.06), "NB8C_Black", 0.02)       # name plaque
    # shimaki + kasagi with upturned ends (segmented bent beam)
    n = (16, 8, 4)[d]
    W = 9.6
    for layer, (y0, h, dep, m) in enumerate(((6.5, 0.36, 0.5, "NB8C_Vermilion"), (6.86, 0.3, 0.62, "NB8C_Black"))):
        for i in range(n):
            x0, x1 = -W / 2 + W * i / n, -W / 2 + W * (i + 1) / n
            xm = (x0 + x1) / 2
            lift = 0.42 * (abs(xm) / (W / 2)) ** 2.6
            ang = 0.42 * 2.6 * (abs(xm) / (W / 2)) ** 1.6 / (W / 2) * (1 if xm > 0 else -1)
            cbox(g, (xm, y0 + lift + h / 2, 0), (x1 - x0 + 0.02, h, dep), m, 0.03, rot=(0, 0, math.atan(ang)))
    return g


def toro(d):
    """Stone lantern (kasuga-style), 2.3 m."""
    g = Geo()
    seg = 6
    lathe(g, [(0.55, 0.0), (0.55, 0.18), (0.42, 0.28), (0.0, 0.28)], seg, "NB_Stone", phase=math.pi / 6)
    lathe(g, [(0.16, 0.28), (0.14, 1.05), (0.0, 1.05)], (12, 8, 6)[d], "NB_Stone")
    lathe(g, [(0.4, 1.05), (0.4, 1.2), (0.0, 1.2)], seg, "NB_Stone", phase=math.pi / 6)
    # fire box: 6 posts + glowing core
    if d < 2:
        for i in range(6):
            a = i / 6 * TAU
            cbox(g, (0.3 * math.cos(a), 1.45, 0.3 * math.sin(a)), (0.1, 0.5, 0.1), "NB_Stone", 0.02, rot=(0, -a, 0))
    lathe(g, [(0.22, 1.2), (0.22, 1.7), (0.0, 1.7)], seg, "NB8C_LanternGlow")
    lathe(g, [(0.62, 1.7), (0.64, 1.78), (0.35, 2.0), (0.14, 2.08), (0.0, 2.08)], seg, "NB_Stone",
          phase=math.pi / 6)
    lathe(g, [(0.1, 2.08), (0.12, 2.18), (0.0, 2.32)], 8, "NB_Stone")
    return g


def shrine_hall(d):
    """Haiden: 11 x 8 m on a stone podium, copper roof. Origin ground centre, front = +z."""
    g = Geo()
    W, D = 11.0, 8.0
    cbox(g, (0, 0.45, 0), (W + 1.6, 0.9, D + 1.6), "NB_Stone", 0.06)
    for i in range(4):                                              # front steps
        h = 0.9 - i * 0.225
        cbox(g, (0, h / 2, D / 2 + 0.8 + 0.3 + i * 0.35), (4.4, h, 0.36), "NB_Stone", 0.03)
    cbox(g, (0, 1.0, 0), (W, 0.2, D), "NB_Teak", 0.03)                 # floor
    # posts on a grid
    xs = [-W / 2 + 0.3 + (W - 0.6) * i / 4 for i in range(5)]
    zs = [-D / 2 + 0.3 + (D - 0.6) * i / 3 for i in range(4)]
    for x in xs:
        for z in zs:
            if abs(x) < W / 2 - 0.4 and abs(z) < D / 2 - 0.4:
                continue
            cbox(g, (x, 1.1 + 1.7, z), (0.3, 3.4, 0.3), "NB8C_Vermilion", 0.04)
    # walls: back + sides plaster panels between posts, front lattice doors
    for i in range(4):
        xm = (xs[i] + xs[i + 1]) / 2
        cbox(g, (xm, 2.6, -D / 2 + 0.3), (xs[i + 1] - xs[i] - 0.3, 3.0, 0.12), "NB_Plaster", 0.02)
        if d < 2:
            cbox(g, (xm, 2.2, D / 2 - 0.3), (xs[i + 1] - xs[i] - 0.3, 2.2, 0.08), "NB_Teak", 0.02)
            if d == 0:
                for k in range(1, 6):
                    cbox(g, (xm - (xs[i + 1] - xs[i] - 0.3) / 2 + k * (xs[i + 1] - xs[i] - 0.3) / 6, 2.2, D / 2 - 0.24),
                         (0.05, 2.2, 0.05), "NB8C_Black", 0.0)
                for k in range(1, 5):
                    cbox(g, (xm, 1.1 + k * 0.44, D / 2 - 0.24), (xs[i + 1] - xs[i] - 0.3, 0.05, 0.05), "NB8C_Black", 0.0)
    for s in (-1, 1):
        for j in range(3):
            zm = (zs[j] + zs[j + 1]) / 2
            cbox(g, (s * (W / 2 - 0.3), 2.6, zm), (0.12, 3.0, zs[j + 1] - zs[j] - 0.3), "NB_Plaster", 0.02)
    # tie beams
    for z in (zs[0], zs[-1]):
        cbox(g, (0, 4.35, z), (W, 0.34, 0.34), "NB8C_Vermilion", 0.04)
    for x in (xs[0], xs[-1]):
        cbox(g, (x, 4.35, 0), (0.34, 0.34, D), "NB8C_Vermilion", 0.04)
    # roof: curved (sagging) gable, big overhang. ridge along x.
    ov_x, ov_z = 1.9, 2.3
    hx, hz = W / 2 + ov_x, D / 2 + ov_z
    eave_y, ridge_y = 4.55, 8.1
    n = (8, 4, 2)[d]
    for s in (-1, 1):  # two slopes (+z front, -z back)
        prof = []
        for k in range(n + 1):
            t = k / n
            z = s * hz * (1 - t)
            y = eave_y + (ridge_y - eave_y) * (t ** 1.45)         # concave ("sori") curve
            prof.append((z, y))
        th = 0.28
        for k in range(n):
            (z0, y0), (z1, y1) = prof[k], prof[k + 1]
            top = [(-hx, y0 + th, z0), (hx, y0 + th, z0), (hx, y1 + th, z1), (-hx, y1 + th, z1)]
            bot = [(-hx, y0, z0), (hx, y0, z0), (hx, y1, z1), (-hx, y1, z1)]
            face_out(g, top, "NB8C_Copper", (0, y0 - 5, z0 * 0.2))
            face_out(g, bot, "NB_Teak", (0, y0 + 5, z0 * 0.2))
            for x in (-hx, hx):
                face_out(g, [(x, y0, z0), (x, y1, z1), (x, y1 + th, z1), (x, y0 + th, z0)], "NB_Teak", (0, y0, z0 * 0.5))
        face_out(g, [(-hx, eave_y, s * hz), (hx, eave_y, s * hz), (hx, eave_y + th, s * hz), (-hx, eave_y + th, s * hz)],
                 "NB_Teak", (0, eave_y, 0))
    # gable triangles
    for x in (-W / 2 - 0.2, W / 2 + 0.2):
        pts = [(x, 4.55, -D / 2 - 0.2), (x, 4.55, D / 2 + 0.2), (x, ridge_y - 0.4, 0)]
        face_out(g, pts, "NB8C_Vermilion", (0, 5, 0))
    cbox(g, (0, ridge_y + 0.45, 0), (2 * hx + 0.4, 0.5, 0.7), "NB8C_Black", 0.08)
    if d == 0:
        for x in (-4.0, -2.0, 0.0, 2.0, 4.0):                     # katsuogi
            sweep(g,  [(x, ridge_y + 0.95, -0.9), (x, ridge_y + 0.95, 0.9)], 0.2, 8, "NB8C_Gold", caps=True)
        # shimenawa rope across the front + shide paper
        rope = [(-3.2 + 6.4 * i / 16, 4.05 - 0.45 * math.sin(math.pi * i / 16), D / 2 + 0.1) for i in range(17)]
        sweep(g, rope, 0.13, 8, "NB_Thatch")
        for i in (3, 8, 13):
            x, y, z = rope[i]
            cbox(g, (x, y - 0.4, z), (0.16, 0.55, 0.02), "NB8C_Paper", 0.0)
        # offering box
        cbox(g, (0, 1.1 + 0.4, D / 2 + 0.5), (1.6, 0.8, 0.8), "NB_Teak", 0.05)
    return g


def chochin(d):
    """Hanging paper lantern. Origin = hook (top); body hangs below."""
    g = Geo()
    seg = (14, 8, 6)[d]
    prof = [(0.0, -1.0), (0.14, -1.0), (0.14, -0.94)]
    for k in range(1, 8):
        t = k / 8
        prof.append((0.3 * math.sin(math.pi * t) + 0.12, -0.94 + 0.8 * t))
    prof += [(0.14, -0.14), (0.14, -0.08), (0.0, -0.08)]
    mats = []
    for k in range(len(prof) - 1):
        mats.append("NB8C_Black" if k < 2 or k >= len(prof) - 3 else "NB8C_Chochin")
    lathe(g, prof, seg, None, mats=mats)
    cyl(g, (0, -0.08, 0), 0.015, 0.08, 4, "NB_Metal")
    return g


# ============================================================================ golf

def golf_pin(d):
    g = Geo()
    lathe(g, [(0.12, -0.02), (0.12, 0.02), (0.0, 0.02)], 12, "NB_Metal")
    cyl(g, (0, 0, 0), 0.018, 2.3, (8, 6, 4)[d], "NB8C_LhWhite")
    return g


def golf_flag(d):
    """Cloth: origin at the pole top attach point; flies toward +x."""
    g = Geo()
    n = (6, 3, 1)[d]
    W, H = 0.62, 0.42
    for i in range(n):
        x0, x1 = W * i / n, W * (i + 1) / n
        z0, z1 = 0.05 * math.sin(x0 * 9), 0.05 * math.sin(x1 * 9)
        q = [(x0, -H, z0), (x1, -H, z1), (x1, 0, z1), (x0, 0, z0)]
        g.face(q, "NB8C_Flag")
        g.face(list(reversed(q)), "NB8C_Flag")
    return g


def golf_cart(d):
    """2.4 m cart, front = +z. Origin ground centre."""
    g = Geo()
    body = "NB8C_CartBody"
    cbox(g, (0, 0.55, 0), (1.2, 0.35, 2.3), body, 0.1)                  # chassis
    cbox(g, (0, 0.85, 0.85), (1.18, 0.35, 0.6), body, 0.14)            # front cowl
    cbox(g, (0, 0.85, -0.25), (1.1, 0.14, 0.62), "NB_Rubber", 0.05)    # seat base
    cbox(g, (0, 1.12, -0.58), (1.1, 0.5, 0.12), "NB_Rubber", 0.05)     # backrest
    cbox(g, (0, 0.95, -0.95), (0.9, 0.5, 0.5), "NB_Rubber", 0.06)      # bag rack
    if d < 2:
        for x in (-0.25, 0.25):
            cyl(g, (x, 0.95, -1.05), 0.12, 0.95, 8, "NB_Canvas")        # bags
    for x in (-0.52, 0.52):
        for z in (0.55, -0.6):
            cyl(g, (x, 1.0, z), 0.025, 1.0, 6, "NB_Metal", cap=False)
    cbox(g, (0, 2.03, 0), (1.3, 0.07, 1.6), body, 0.03)                # canopy
    if d < 2:
        g.face([(-0.55, 1.1, 0.58), (0.55, 1.1, 0.58), (0.55, 1.95, 0.52), (-0.55, 1.95, 0.52)], "NB_CarGlass")
    for x in (-0.55, 0.55):
        for z in (0.78, -0.78):
            seg = (12, 8, 6)[d]
            ring = [(x - 0.1, 0.23, z), (x + 0.1, 0.23, z)]
            sweep(g, [(x - 0.09, 0.23, z), (x + 0.09, 0.23, z)], 0.23, seg, "NB_Rubber", caps=True)
    return g


# ============================================================================ water park

WP_SPLASH = (8.0, -16.0)        # splash pool centre (x, z); must match Landmarks.cs
WP_TOWER = (-4.0, -30.0)


def _rect_pool(g, x0, z0, x1, z1, depth, mat_edge="NB_PoolTile"):
    """Recessed pool: tiled walls + floor + water at -0.25. Deck is cut by the caller."""
    w = [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]
    c = ((x0 + x1) / 2, -depth / 2, (z0 + z1) / 2)
    for i in range(4):
        a, b = w[i], w[(i + 1) % 4]
        pts = [(a[0], 0.0, a[1]), (b[0], 0.0, b[1]), (b[0], -depth, b[1]), (a[0], -depth, a[1])]
        # inward-facing
        n = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
        cc = ((a[0] + b[0]) / 2, -depth / 2, (a[1] + b[1]) / 2)
        if dot(n, sub(c, cc)) < 0:
            pts = list(reversed(pts))
        g.face(pts, mat_edge)
    g.face([(x0, -depth, z0), (x0, -depth, z1), (x1, -depth, z1), (x1, -depth, z0)], "NB_PoolTile")
    q = [(x0, -0.22, z0), (x0, -0.22, z1), (x1, -0.22, z1), (x1, -0.22, z0)]
    n = cross(sub(q[1], q[0]), sub(q[2], q[0]))
    if n[1] < 0:
        q = list(reversed(q))
    g.face(q, "NB_PoolWater")
    # coping
    for (ax, az), (bx, bz) in zip(w, w[1:] + w[:1]):
        pass


WP_POOLS = [(-40.0, 2.0, -6.0, 30.0, 1.6), (0.0, -24.0, 16.0, -8.0, 2.4), (18.0, 14.0, 38.0, 30.0, 0.6)]
WP_W, WP_D = 96.0, 72.0


def _deck(g, pools, cell):
    x = -WP_W / 2
    while x < WP_W / 2 - 1e-3:
        z = -WP_D / 2
        x1 = min(x + cell, WP_W / 2)
        while z < WP_D / 2 - 1e-3:
            z1 = min(z + cell, WP_D / 2)
            cx, cz = (x + x1) / 2, (z + z1) / 2
            if not any(p[0] < cx < p[2] and p[1] < cz < p[3] for p in pools):
                g.face([(x, 0.0, z), (x, 0.0, z1), (x1, 0.0, z1), (x1, 0.0, z)], "NB_DeckStone")
            z = z1
        x = x1


def _spiral(cx, cz, r, y0, y1, turns, n, a0=0.0):
    return [(cx + r * math.cos(a0 + TAU * turns * i / n), y0 + (y1 - y0) * i / n, cz + r * math.sin(a0 + TAU * turns * i / n))
            for i in range(n + 1)]


def water_park(d):
    """96 x 72 m deck at y 0 (origin centre); front (+z) faces the viewer. Retaining wall to -6."""
    g = Geo()
    pools = [(p[0], p[1], p[2], p[3]) for p in WP_POOLS]
    _deck(g, pools, 4.0 if d == 0 else 8.0)
    for p in WP_POOLS:
        _rect_pool(g, *p)
    # coping + perimeter retaining wall
    for (ax, az, bx, bz) in ((-WP_W / 2, -WP_D / 2, WP_W / 2, -WP_D / 2), (WP_W / 2, -WP_D / 2, WP_W / 2, WP_D / 2),
                             (WP_W / 2, WP_D / 2, -WP_W / 2, WP_D / 2), (-WP_W / 2, WP_D / 2, -WP_W / 2, -WP_D / 2)):
        L = math.hypot(bx - ax, bz - az)
        cx, cz = (ax + bx) / 2, (az + bz) / 2
        yaw = math.atan2(bx - ax, bz - az)
        cbox(g, (cx, -3.0, cz), (0.6, 6.4, L + 0.6), "NB_Stone", 0.08, rot=(0, yaw, 0))
        if d < 2:
            # glass balustrade posts + rail
            n = int(L / 3)
            for i in range(n + 1):
                t = i / n
                cbox(g, (ax + (bx - ax) * t, 0.55, az + (bz - az) * t), (0.08, 1.1, 0.08), "NB_Metal", 0.01)
            cbox(g, (cx, 1.1, cz), (0.08, 0.06, L), "NB_Metal", 0.01, rot=(0, yaw, 0))
            cbox(g, (cx, 0.55, cz), (0.02, 0.9, L), "NB_BalconyGlass", 0.0, rot=(0, yaw, 0))
    # slide tower (steel frame, 3 decks, canopy)
    tx, tz = WP_TOWER
    for sx in (-1, 1):
        for sz in (-1, 1):
            cyl(g, (tx + sx * 2.6, 0, tz + sz * 2.6), 0.16, 18.2, (10, 6, 4)[d], "NB8C_SlideSteel")
    for y in (6.0, 11.0, 16.0):
        cbox(g, (tx, y, tz), (6.0, 0.25, 6.0), "NB8C_SlideSteel", 0.04)
        if d < 2:
            for sx, sz, w, dd in ((0, 3.0, 6.0, 0.06), (0, -3.0, 6.0, 0.06), (3.0, 0, 0.06, 6.0), (-3.0, 0, 0.06, 6.0)):
                cbox(g, (tx + sx, y + 0.6, tz + sz), (w, 1.1, dd), "NB_BalconyGlass", 0.0)
    lathe(g, [(4.4, 18.2), (0.0, 20.2)], 4, "NB8C_Canopy", c=(tx, 0, tz), phase=math.pi / 4)
    if d < 2:
        # stair flights (zigzag) on the -x side
        y = 0.0
        flights = [(0.0, 6.0), (6.0, 11.0), (11.0, 16.0)]
        for k, (ya, yb) in enumerate(flights):
            n = int((yb - ya) / 0.3)
            for i in range(n):
                t = (i + 0.5) / n
                zz = tz + (-2.6 + 5.2 * t) * (1 if k % 2 == 0 else -1)
                cbox(g, (tx - 4.0, ya + (yb - ya) * t, zz), (1.4, 0.12, 0.34), "NB8C_SlideSteel", 0.01)
            cbox(g, (tx - 4.0, (ya + yb) / 2, tz), (0.1, 0.1, 5.4), "NB8C_SlideSteel", 0.0,
                 rot=(math.atan2(yb - ya, 5.2) * (1 if k % 2 == 0 else -1), 0, 0))
    sx_, sz_ = WP_SPLASH
    seg = (16, 10, 6)[d]
    # 1) closed spiral tube (yellow) from 16 m
    sp = [(tx + 3.0, 16.3, tz + 1.0)] + _spiral(tx + 11.0, tz + 2.0, 6.0, 15.5, 3.0, 2.25, (72, 36, 18)[d], a0=math.pi)
    sp += [(sx_ - 4.0, 1.4, sz_ - 5.0), (sx_ - 2.0, 0.3, sz_ - 2.0)]
    sweep(g, sp, 0.7, seg, "NB8C_SlideYellow", double=(d == 0))
    # 2) open flume (blue, wavy) from 11 m
    fl = []
    n = (60, 30, 14)[d]
    for i in range(n + 1):
        t = i / n
        fl.append((tx + 3.0 + 22.0 * t, 11.3 - 11.0 * t ** 1.1, tz + 3.0 + 12.0 * t + 4.0 * math.sin(t * TAU * 1.5)))
    fl[-1] = (sx_ + 5.0, 0.35, sz_ - 1.0)
    sweep(g, fl, 0.75, seg // 2, "NB8C_SlideBlue", arc=math.pi, a0=-math.pi / 2, double=True)
    # 3) speed slide (red), straight & steep from 16 m
    sd = [(tx + 1.0, 16.3, tz + 3.0), (tx + 1.0, 15.8, tz + 5.0)]
    for i in range(1, 11):
        t = i / 10
        sd.append((tx + 1.0 + 7.0 * t, 15.8 - 15.4 * (t ** 0.8), tz + 5.0 + 18.0 * t))
    sweep(g, sd, 0.6, seg // 2, "NB8C_SlideRed", arc=math.pi, a0=-math.pi / 2, double=True)
    # support columns under the slides
    if d < 2:
        for path, every in ((sp, 6), (fl, 5), (sd, 2)):
            for p in path[2:-2:every]:
                if p[1] > 1.5:
                    cyl(g, (p[0], 0, p[2]), 0.12, p[1] - 0.6, 6, "NB8C_SlideSteel", cap=False)
    # lounge pavilion / kiosk (front-right)
    cbox(g, (34.0, 1.6, -22.0), (14.0, 3.2, 8.0), "NB_Wall", 0.1)
    cbox(g, (34.0, 3.35, -22.0), (15.6, 0.3, 9.6), "NB_Trim", 0.08)
    if d < 2:
        cbox(g, (34.0, 1.5, -17.95), (11.0, 2.0, 0.1), "NB_ShopInterior", 0.0)
    return g


# ============================================================================ fishing pier

def pier_bay(d):
    """8 m pier module along +z; deck 4.2 m wide at y 0, pilings to -10. Origin deck centre."""
    g = Geo()
    L, W = 8.0, 4.2
    n = (20, 8, 1)[d]
    for i in range(n):
        z0 = -L / 2 + L * i / n
        cbox(g, (0, -0.07, z0 + L / n / 2), (W, 0.14, L / n - (0.02 if d == 0 else 0)),
             "NB_Boardwalk", 0.01 if d == 0 else 0.0)
    for x in (-1.6, 0.0, 1.6):
        cbox(g, (x, -0.4, 0), (0.22, 0.5, L), "NB_PontoonWood", 0.03)
    cbox(g, (0, -0.8, -L / 2 + 0.2), (W + 0.4, 0.4, 0.3), "NB_PontoonWood", 0.03)
    for x in (-2.0, 2.0):
        cyl(g, (x, -10.5, -L / 2 + 0.2), 0.2, 10.4, (10, 6, 4)[d], "NB_PontoonWood")
    if d < 2:
        for x in (-2.0, 2.0):  # cross brace
            cbox(g, (x, -4.0, 0), (0.12, 0.25, 8.8), "NB_PontoonWood", 0.02, rot=(0.55, 0, 0))
        for s in (-1, 1):
            for z in (-L / 2 + 0.2, 0.2):
                cbox(g, (s * 2.05, 0.55, z), (0.12, 1.1, 0.12), "NB_Teak", 0.02)
            cbox(g, (s * 2.05, 1.08, 0), (0.16, 0.08, L), "NB_Teak", 0.02)
            cbox(g, (s * 2.05, 0.6, 0), (0.06, 0.08, L), "NB_Teak", 0.01)
    return g


def pier_head(d):
    """T-head 16 x 10 m, origin deck centre; joins a bay at z -5. Shelter + benches."""
    g = Geo()
    Wx, Dz = 16.0, 10.0
    cbox(g, (0, -0.1, 0), (Wx, 0.2, Dz), "NB_Boardwalk", 0.02)
    for x in (-7.5, -2.5, 2.5, 7.5):
        for z in (-4.5, 4.5):
            cyl(g, (x, -10.5, z), 0.22, 10.4, (10, 6, 4)[d], "NB_PontoonWood")
    if d < 2:
        for (ax, az, bx, bz) in ((-Wx / 2, Dz / 2, Wx / 2, Dz / 2), (Wx / 2, -Dz / 2, Wx / 2, Dz / 2),
                                 (-Wx / 2, -Dz / 2, -Wx / 2, Dz / 2), (-Wx / 2, -Dz / 2, -2.2, -Dz / 2),
                                 (2.2, -Dz / 2, Wx / 2, -Dz / 2)):
            L = math.hypot(bx - ax, bz - az)
            yaw = math.atan2(bx - ax, bz - az)
            cbox(g, ((ax + bx) / 2, 1.08, (az + bz) / 2), (0.16, 0.08, L), "NB_Teak", 0.02, rot=(0, yaw, 0))
            for i in range(int(L / 2.5) + 1):
                t = i / max(1, int(L / 2.5))
                cbox(g, (ax + (bx - ax) * t, 0.55, az + (bz - az) * t), (0.12, 1.1, 0.12), "NB_Teak", 0.02)
    # shelter
    for x in (-2.0, 2.0):
        for z in (-1.5, 1.5):
            cbox(g, (x, 1.45, z), (0.16, 2.9, 0.16), "NB_Teak", 0.03)
    for s in (-1, 1):
        q = [(-2.8, 2.9, 0.0), (2.8, 2.9, 0.0), (2.8, 2.55, s * 2.4), (-2.8, 2.55, s * 2.4)]
        face_out(g, q, "NB_MetalRoof", (0, 0, 0))
        face_out(g, [(p[0], p[1] - 0.06, p[2]) for p in q], "NB_Teak", (0, 5, 0))
    for z in (-1.2, 1.2):
        cbox(g, (0, 0.45, z), (3.2, 0.08, 0.45), "NB_Teak", 0.02)
    return g


def rod(d):
    """Fishing rod: butt at origin, points +z and up 35 deg (rotate in C#); line hangs from tip."""
    g = Geo()
    L = 3.4
    seg = (6, 4, 3)[d]
    tip = (0.0, L * math.sin(0.6), L * math.cos(0.6))
    sweep(g, [(0, 0, 0), mul3(tip, 0.5), tip], 0.018, seg, "NB_Rubber")
    if d < 2:
        lathe(g, [(0.04, 0.0), (0.04, 0.06), (0.0, 0.06)], 8, "NB_Metal", c=(0.05, 0.25, 0.3))
        cbox(g, (tip[0], tip[1] - 1.6, tip[2]), (0.006, 3.2, 0.006), "NB_Net", 0.0)
    return g


def cooler(d):
    g = Geo()
    cbox(g, (0, 0.2, 0), (0.6, 0.4, 0.38), "NB8C_CartBody", 0.05)
    cbox(g, (0, 0.42, 0), (0.62, 0.06, 0.4), "NB_PaintTeal", 0.02)
    return g


# ============================================================================ main

ASSETS = [
    ("Nagisa_NB8C_Lighthouse", lighthouse),
    ("Nagisa_NB8C_LighthouseBeam", lighthouse_beam),
    ("Nagisa_NB8C_Breakwater", breakwater),
    ("Nagisa_NB8C_BreakwaterHead", breakwater_head),
    ("Nagisa_NB8C_Torii", torii),
    ("Nagisa_NB8C_Toro", toro),
    ("Nagisa_NB8C_ShrineHall", shrine_hall),
    ("Nagisa_NB8C_Chochin", chochin),
    ("Nagisa_NB8C_GolfPin", golf_pin),
    ("Nagisa_NB8C_GolfFlag", golf_flag),
    ("Nagisa_NB8C_GolfCart", golf_cart),
    ("Nagisa_NB8C_WaterPark", water_park),
    ("Nagisa_NB8C_PierBay", pier_bay),
    ("Nagisa_NB8C_PierHead", pier_head),
    ("Nagisa_NB8C_Rod", rod),
    ("Nagisa_NB8C_Cooler", cooler),
]

if __name__ == "__main__":
    only = os.environ.get("NB8C_ONLY", "")
    for stem, fn in ASSETS:
        if only and stem not in only.split(","):
            continue
        build_lods(stem, fn, stem + ".glb")

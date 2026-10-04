"""
NAGISA BAY (B5) - tropical vegetation, authored LOD0/1/2 per asset.

  Nagisa_CoconutPalm_A.glb  11.5 m curved coconut palm, 16 arching fronds + coconut cluster
  Nagisa_CoconutPalm_B.glb   8.5 m leaning coconut palm (beach lean), 14 fronds
  Nagisa_FanPalm.glb         5.5 m fan palm (palmate leaves on petioles)
  Nagisa_Hibiscus.glb        1.6 m hibiscus shrub (crossed cutout cards, red/pink blooms)
  Nagisa_Bougainvillea.glb   2.2 m bougainvillea mound (magenta bracts)

Fronds / leaves use the cutout albedo atlases NB_PalmFrond / NB_FanFrond / NB_Hibiscus /
NB_Bougainvillea (u across, v base->tip) and render with MapleRide/HDRP/Foliage (Cull Off),
so cards are single faces. Trunks are NB_PalmBark on MapleRide/HDRP/CelLit.

Run: blender.exe -b -P tools/blender/build_nagisa_palms.py
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo, add, mul, norm, cross, sub  # noqa: E402


def trunk(g, height, lean, bend, r0, r1, sides, rings, yaw=0.0):
    """Tapered curved trunk; returns the crown point and the tip direction."""
    pts = []
    for i in range(rings + 1):
        t = i / rings
        # lean grows with height, plus a gentle upward-curving bend (coconut 'sweep')
        off = lean * t + bend * t * t
        x = off * math.cos(yaw)
        z = off * math.sin(yaw)
        pts.append((x, height * t, z))
    rad = [r0 * (1 - t) + r1 * t + (0.22 * r0 * max(0.0, 1 - t * 8)) for t in (i / rings for i in range(rings + 1))]
    rings_v = []
    for i, p in enumerate(pts):
        ring = []
        for k in range(sides + 1):
            a = k / sides * math.tau
            ring.append((p[0] + rad[i] * math.cos(a), p[1], p[2] + rad[i] * math.sin(a)))
        rings_v.append(ring)
    L = 0.0
    for i in range(rings):
        seg = math.dist(pts[i], pts[i + 1])
        for k in range(sides):
            u0, u1 = k / sides, (k + 1) / sides
            g.quad(rings_v[i][k], rings_v[i + 1][k], rings_v[i + 1][k + 1], rings_v[i][k + 1], "NB_PalmBark",
                   uvs=[(u0, L / 0.6), (u0, (L + seg) / 0.6), (u1, (L + seg) / 0.6), (u1, L / 0.6)])
        L += seg
    top = pts[-1]
    d = norm(sub(pts[-1], pts[-2]))
    # crown cap
    g.face([rings_v[-1][k] for k in range(sides, 0, -1)], "NB_PalmBark")
    return top, d


def frond(g, base, yaw, pitch, length, width, segs, droop, mat="NB_PalmFrond", fold=0.18):
    """Arching frond card. pitch = initial elevation (rad), droop = downward curvature."""
    fwd = (math.cos(yaw), 0.0, math.sin(yaw))
    side = (-math.sin(yaw), 0.0, math.cos(yaw))
    spine = []
    ang = pitch
    p = base
    step = length / segs
    for i in range(segs + 1):
        spine.append(p)
        dirv = (fwd[0] * math.cos(ang), math.sin(ang), fwd[2] * math.cos(ang))
        p = add(p, mul(dirv, step))
        ang -= droop / segs
    for i in range(segs):
        t0, t1 = i / segs, (i + 1) / segs
        w0 = width * (0.35 + 0.65 * math.sin(math.pi * min(1.0, 0.15 + t0 * 0.95)))
        w1 = width * (0.35 + 0.65 * math.sin(math.pi * min(1.0, 0.15 + t1 * 0.95)))
        a, b = spine[i], spine[i + 1]
        # V-fold: the centre line sits higher than the leaflet tips
        la = add(a, add(mul(side, -w0 / 2), (0, -fold * w0, 0)))
        ra = add(a, add(mul(side, w0 / 2), (0, -fold * w0, 0)))
        lb = add(b, add(mul(side, -w1 / 2), (0, -fold * w1, 0)))
        rb = add(b, add(mul(side, w1 / 2), (0, -fold * w1, 0)))
        g.quad(la, lb, b, a, mat, uvs=[(0.0, t0), (0.0, t1), (0.5, t1), (0.5, t0)])
        g.quad(a, b, rb, ra, mat, uvs=[(0.5, t0), (0.5, t1), (1.0, t1), (1.0, t0)])


def coconut_cluster(g, top, n, detail):
    rng = random.Random(7)
    for k in range(n):
        a = k / n * math.tau + rng.uniform(-0.3, 0.3)
        c = add(top, (0.32 * math.cos(a), -0.45 - rng.uniform(0, 0.25), 0.32 * math.sin(a)))
        g.cylinder(add(c, (0, -0.14, 0)), 0.16, 0.30, (8, 6, 4)[detail], "NB_PalmBark", top=True, bottom=True,
                   r_top=0.13)


def coconut_palm(detail, height, lean, bend, nfr, seed, yaw=0.6):
    g = Geo()
    rng = random.Random(seed)
    sides = (10, 6, 4)[detail]
    rings = (10, 6, 3)[detail]
    top, d = trunk(g, height, lean, bend, 0.30, 0.19, sides, rings, yaw)
    nf = (nfr, int(nfr * 0.7), 7)[detail]
    segs = (6, 4, 2)[detail]
    for k in range(nf):
        yawk = k / nf * math.tau + rng.uniform(-0.15, 0.15)
        pitch = rng.uniform(0.25, 0.75) if k % 3 else rng.uniform(0.9, 1.2)   # a few young upright fronds
        L = rng.uniform(5.2, 6.4)
        frond(g, add(top, (0, 0.1, 0)), yawk, pitch, L, rng.uniform(1.9, 2.4), segs, rng.uniform(1.6, 2.3))
    if detail < 2:
        coconut_cluster(g, top, (7, 4)[detail], detail)
    return g


def fan_palm(detail):
    g = Geo()
    rng = random.Random(33)
    top, d = trunk(g, 4.6, 0.25, 0.0, 0.26, 0.22, (10, 6, 4)[detail], (6, 4, 2)[detail], 0.3)
    n = (18, 12, 7)[detail]
    for k in range(n):
        yawk = k / n * math.tau + rng.uniform(-0.2, 0.2)
        pitch = rng.uniform(-0.25, 1.1)
        fwd = (math.cos(yawk) * math.cos(pitch), math.sin(pitch), math.sin(yawk) * math.cos(pitch))
        L = rng.uniform(1.2, 1.8)
        tip = add(top, mul(fwd, L))
        # petiole (thin strip, both sides visible via Foliage Cull Off = bark on CelLit -> box)
        if detail < 2:
            side = (-math.sin(yawk), 0, math.cos(yawk))
            g.quad(add(top, mul(side, -0.04)), add(tip, mul(side, -0.03)), add(tip, mul(side, 0.03)),
                   add(top, mul(side, 0.04)), "NB_FanFrond", uvs=[(0.49, 0.0), (0.49, 0.06), (0.51, 0.06), (0.51, 0.0)])
        # palmate leaf: a folded fan card, petiole at v=0 (bottom centre of the texture)
        size = rng.uniform(1.7, 2.2)
        up_leaf = norm((fwd[0], fwd[1] + 0.15, fwd[2]))
        side = norm(cross(up_leaf, (0, 1, 0))) if abs(up_leaf[1]) < 0.99 else (1, 0, 0)
        side = (-math.sin(yawk), 0, math.cos(yawk))
        c0 = tip
        c1 = add(tip, mul(up_leaf, size))
        mid = add(tip, mul(up_leaf, size * 0.55))
        # 3 panels across with a gentle pleat (centre raised)
        cols = [(-1.0, 0.0), (-0.34, 0.12), (0.34, 0.12), (1.0, 0.0)]
        for j in range(3):
            (s0, h0), (s1, h1) = cols[j], cols[j + 1]
            lift = norm(cross(side, up_leaf))
            a = add(c0, mul(side, s0 * 0.05))
            b = add(c0, mul(side, s1 * 0.05))
            A = add(add(c1, mul(side, s0 * size * 0.5)), mul(lift, h0 * size))
            B = add(add(c1, mul(side, s1 * size * 0.5)), mul(lift, h1 * size))
            u0, u1 = (s0 + 1) / 2, (s1 + 1) / 2
            g.quad(a, A, B, b, "NB_FanFrond", uvs=[(0.5 + (u0 - 0.5) * 0.06, 0.06), (u0, 1.0), (u1, 1.0),
                                                   (0.5 + (u1 - 0.5) * 0.06, 0.06)])
    return g


def shrub(detail, mat, height, radius, cards, seed):
    """Dome of crossed cutout cards (each card = 2 quads in an X)."""
    g = Geo()
    rng = random.Random(seed)
    n = (cards, max(3, cards // 2), 3)[detail]
    for k in range(n):
        a = rng.uniform(0, math.pi)
        r = rng.uniform(0, radius * 0.45)
        ca = rng.uniform(0, math.tau)
        cx, cz = r * math.cos(ca), r * math.sin(ca)
        w = radius * rng.uniform(1.1, 1.5)
        h = height * rng.uniform(0.8, 1.05)
        y0 = -0.08
        for rot in (a, a + math.pi / 2):
            dx, dz = math.cos(rot) * w / 2, math.sin(rot) * w / 2
            g.quad((cx - dx, y0, cz - dz), (cx - dx, y0 + h, cz - dz), (cx + dx, y0 + h, cz + dz),
                   (cx + dx, y0, cz + dz), mat, uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
    # a horizontal cap card breaks the 'X' read from above
    if detail < 2:
        s = radius * 1.1
        g.quad((-s, height * 0.78, -s), (-s, height * 0.78, s), (s, height * 0.78, s), (s, height * 0.78, -s), mat,
               uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
    return g


def canopy_tree(detail, height, spread, clumps, seed):
    """Tropical broadleaf (rain-tree / banyan read): leaning branching trunk + clumps of
    crossed NB_Canopy cards forming a broad umbrella crown."""
    g = Geo()
    rng = random.Random(seed)
    sides = (7, 5, 4)[detail]
    top, _ = trunk(g, height * 0.55, 0.4, 0.3, 0.38, 0.22, sides, (5, 3, 2)[detail], rng.uniform(0, 6.28))
    nb = (4, 3, 0)[detail]
    for k in range(nb):
        a = k / max(1, nb) * math.tau + rng.uniform(-0.3, 0.3)
        L = spread * rng.uniform(0.45, 0.6)
        end = (top[0] + math.cos(a) * L, top[1] + height * rng.uniform(0.18, 0.3), top[2] + math.sin(a) * L)
        g.cylinder(top, 0.16, 0.1, 4, "NB_PalmBark", top=False)  # knuckle
        # branch as a thin prism quad strip
        dx, dz = -math.sin(a) * 0.12, math.cos(a) * 0.12
        g.quad((top[0] - dx, top[1], top[2] - dz), (end[0] - dx, end[1], end[2] - dz),
               (end[0] + dx, end[1], end[2] + dz), (top[0] + dx, top[1], top[2] + dz), "NB_PalmBark", double=True)
    n = (clumps, max(4, clumps // 2), 3)[detail]
    for k in range(n):
        ca = rng.uniform(0, math.tau)
        r = spread * math.sqrt(rng.uniform(0.0, 1.0)) * 0.5
        cx, cz = top[0] + r * math.cos(ca), top[2] + r * math.sin(ca)
        cy = height * rng.uniform(0.62, 0.86)
        w = spread * rng.uniform(0.42, 0.6) * (1.6 if detail == 2 else 1.0)
        h = w * 0.62
        a = rng.uniform(0, math.pi)
        for rot in (a, a + math.pi / 2):
            dx, dz = math.cos(rot) * w / 2, math.sin(rot) * w / 2
            g.quad((cx - dx, cy - h / 2, cz - dz), (cx - dx, cy + h / 2, cz - dz), (cx + dx, cy + h / 2, cz + dz),
                   (cx + dx, cy - h / 2, cz + dz), "NB_Canopy", uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
        s = w * 0.55
        g.quad((cx - s, cy + h * 0.2, cz - s), (cx - s, cy + h * 0.2, cz + s), (cx + s, cy + h * 0.2, cz + s),
               (cx + s, cy + h * 0.2, cz - s), "NB_Canopy", uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
    return g


def main():
    C.build_lods("Nagisa_CoconutPalm_A", lambda d: coconut_palm(d, 11.5, 0.9, 1.1, 16, 11), "Nagisa_CoconutPalm_A.glb")
    C.build_lods("Nagisa_CoconutPalm_B", lambda d: coconut_palm(d, 8.5, 2.4, 0.4, 14, 23, yaw=-0.4),
                 "Nagisa_CoconutPalm_B.glb")
    C.build_lods("Nagisa_FanPalm", fan_palm, "Nagisa_FanPalm.glb")
    C.build_lods("Nagisa_Hibiscus", lambda d: shrub(d, "NB_Hibiscus", 1.6, 1.1, 8, 5), "Nagisa_Hibiscus.glb")
    C.build_lods("Nagisa_Bougainvillea", lambda d: shrub(d, "NB_Bougainvillea", 2.2, 1.5, 10, 9),
                 "Nagisa_Bougainvillea.glb")
    C.build_lods("Nagisa_CanopyTree_A", lambda d: canopy_tree(d, 14.0, 13.0, 14, 31), "Nagisa_CanopyTree_A.glb")
    C.build_lods("Nagisa_CanopyTree_B", lambda d: canopy_tree(d, 10.0, 9.0, 10, 47), "Nagisa_CanopyTree_B.glb")


main()

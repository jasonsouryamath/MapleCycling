"""
NAGISA BAY (B5) - beach + promenade props, authored LOD0/1/2.

  Nagisa_BeachUmbrella.glb   2.6 m teak-pole parasol with canvas canopy (striped skirt)
  Nagisa_Lounger.glb         teak sun lounger with cream cushion
  Nagisa_LifeguardTower.glb  raised hut on stilts, ramp, red/white paint, flag mast
  Nagisa_PromenadeLamp.glb   4.2 m bronze promenade lamp (glowing lantern head)
  Nagisa_Bench.glb           teak slat bench on stone legs
  Nagisa_Railing.glb         4 m promenade railing bay (bronze posts, teak cap rail, cables)

Local frame: +z = the prop's front (loungers face +z; place with yaw toward the sea).
Run: blender.exe -b -P tools/blender/build_nagisa_beach.py
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo  # noqa: E402


def umbrella(detail):
    g = Geo()
    seg = (16, 10, 6)[detail]
    g.cylinder((0, 0, 0), 0.03, 2.6, (8, 6, 4)[detail], "NB_Teak", top=True)
    R, h0, h1 = 1.35, 2.15, 2.62
    apex = (0, h1, 0)
    rim = [(R * math.cos(i / seg * math.tau), h0, R * math.sin(i / seg * math.tau)) for i in range(seg + 1)]
    for i in range(seg):
        a, b = rim[i], rim[i + 1]
        # canopy: top + underside (the underside is what a rider sees from below)
        g.tri(a, apex, b, "NB_Canvas", uvs=[(0.02, 0.02), (0.02, 0.45), (0.45, 0.02)])
        g.tri(b, apex, a, "NB_Canvas", uvs=[(0.45, 0.02), (0.02, 0.45), (0.02, 0.02)])
        if detail < 2:  # striped valance skirt
            u0, u1 = i / seg, (i + 1) / seg
            a2, b2 = (a[0], h0 - 0.2, a[2]), (b[0], h0 - 0.2, b[2])
            g.quad(a2, a, b, b2, "NB_Canvas", uvs=[(u0, 0.55), (u0, 0.95), (u1, 0.95), (u1, 0.55)], double=True)
    return g


def lounger(detail):
    g = Geo()
    L, W = 1.95, 0.68
    # frame (teak): two side rails + legs
    for sx in (-W / 2 + 0.03, W / 2 - 0.03):
        g.box((sx, 0.30, -0.15), (0.05, 0.06, 1.35), "NB_Teak", bottom=True)
        if detail < 2:
            for z in (-0.78, 0.45):
                g.box((sx, 0.14, z), (0.05, 0.28, 0.05), "NB_Teak", top=False)
    # seat cushion (flat) + backrest (raised at 35 degrees toward -z end... backrest at +z)
    g.box((0, 0.37, -0.25), (W - 0.04, 0.08, 1.15), "NB_Canvas", bottom=True, mats={"side": "NB_Canvas"})
    ang = math.radians(38)
    Lb = 0.75
    cz, cy = 0.33 + math.cos(ang) * Lb / 2, 0.37 + math.sin(ang) * Lb / 2
    # backrest as an inclined box (build as quads)
    hw = (W - 0.04) / 2
    t = 0.08
    p0 = (0, 0.37, 0.33)
    d = (0, math.sin(ang), math.cos(ang))
    n = (0, math.cos(ang), -math.sin(ang))
    def P(sx, s, o):
        return (sx, p0[1] + d[1] * s + n[1] * o, p0[2] + d[2] * s + n[2] * o)
    A, B, Cc, D = P(-hw, 0, t / 2), P(hw, 0, t / 2), P(hw, Lb, t / 2), P(-hw, Lb, t / 2)
    a, b, c, dd = P(-hw, 0, -t / 2), P(hw, 0, -t / 2), P(hw, Lb, -t / 2), P(-hw, Lb, -t / 2)
    g.quad(A, D, Cc, B, "NB_Canvas", double=True)          # front (faces the sky / -z-ish)
    g.quad(b, c, dd, a, "NB_Teak", double=True)            # back
    g.quad(D, dd, c, Cc, "NB_Canvas", double=True)         # top edge
    g.quad(A, a, dd, D, "NB_Canvas", double=True)
    g.quad(B, Cc, c, b, "NB_Canvas", double=True)
    if detail < 2:
        g.box((0, 0.2, 0.62), (0.05, 0.4, 0.05), "NB_Teak", yaw=0.0, top=False)
    return g


def lifeguard_tower(detail):
    g = Geo()
    # stilts
    for sx in (-1.1, 1.1):
        for sz in (-1.0, 1.0):
            g.box((sx, 1.2, sz), (0.16, 2.4, 0.16), "NB_Teak", top=False)
    if detail < 2:  # cross bracing
        for sz in (-1.0, 1.0):
            g.box((0, 1.2, sz), (2.4, 0.08, 0.08), "NB_Teak", yaw=0.0)
    # platform + hut (red/white paint), windows as dark glass band
    g.box((0, 2.5, 0), (3.0, 0.2, 2.8), "NB_Teak", bottom=True)
    g.box((0, 3.6, 0.2), (2.4, 2.0, 2.0), "NB_Paint", bottom=True, mats={"top": "NB_Paint"})
    g.box((0, 3.9, -0.81), (2.2, 0.8, 0.02), "NB_Glass", top=False)
    # hip roof
    apex = (0, 5.2, 0.2)
    c4 = [(-1.5, 4.6, -1.1), (1.5, 4.6, -1.1), (1.5, 4.6, 1.5), (-1.5, 4.6, 1.5)]
    for k in range(4):
        a, b = c4[k], c4[(k + 1) % 4]
        g.tri(a, apex, b, "NB_PaintTeal")
        g.tri(b, apex, a, "NB_PaintTeal")
    # front deck rail
    g.box((0, 3.05, -1.35), (3.0, 0.08, 0.08), "NB_Paint")
    for sx in (-1.45, 0.0, 1.45):
        g.box((sx, 2.8, -1.35), (0.06, 0.55, 0.06), "NB_Paint", top=False)
    # ramp down to the sand (+z side)
    if detail < 2:
        n = 8 if detail == 0 else 4
        for i in range(n):
            z0 = 1.4 + i * (3.2 / n)
            y = 2.5 - (i + 0.5) * (2.5 / n)
            g.box((0, y, z0 + 1.6 / n), (1.0, 0.08, 3.2 / n + 0.02), "NB_Teak", bottom=True)
    # flag mast
    g.cylinder((1.35, 4.6, 1.3), 0.03, 2.4, 6, "NB_Metal", top=True)
    g.quad((1.38, 6.2, 1.3), (1.38, 6.8, 1.3), (2.3, 6.8, 1.3), (2.3, 6.2, 1.3), "NB_PaintTeal", double=True)
    return g


def lamp(detail):
    g = Geo()
    s = (12, 8, 5)[detail]
    g.cylinder((0, 0, 0), 0.16, 0.5, s, "NB_Stone", top=True)
    g.cylinder((0, 0.5, 0), 0.06, 3.3, s, "NB_Bronze", top=False, r_top=0.045)
    # lantern head
    g.cylinder((0, 3.8, 0), 0.20, 0.36, s, "NB_Lamp", top=False, bottom=True)
    g.cylinder((0, 4.16, 0), 0.30, 0.10, s, "NB_Bronze", top=True, bottom=True, r_top=0.08)
    if detail == 0:
        g.box((0, 3.75, 0), (0.44, 0.04, 0.44), "NB_Bronze", bottom=True)
    return g


def bench(detail):
    g = Geo()
    for sx in (-0.8, 0.8):
        g.box((sx, 0.22, 0), (0.12, 0.44, 0.5), "NB_Stone")
    n = (5, 3, 1)[detail]
    for i in range(n):
        z = -0.2 + i * (0.4 / max(1, n - 1)) if n > 1 else 0
        g.box((0, 0.47, z), (1.9, 0.05, 0.4 / n * 0.8), "NB_Teak", bottom=True)
    if detail < 2:
        g.box((0, 0.78, 0.26), (1.9, 0.26, 0.05), "NB_Teak", bottom=True)
    return g


def railing(detail):
    g = Geo()
    L = 4.0
    for x in (-L / 2, 0.0, L / 2) if detail < 2 else (-L / 2, L / 2):
        g.box((x, 0.55, 0), (0.07, 1.1, 0.07), "NB_Bronze", top=False)
    g.box((0, 1.12, 0), (L + 0.1, 0.06, 0.14), "NB_Teak", bottom=True)
    if detail == 0:
        for y in (0.35, 0.6, 0.85):
            g.box((0, y, 0), (L, 0.012, 0.012), "NB_Metal")
    return g


def main():
    C.build_lods("Nagisa_BeachUmbrella", umbrella, "Nagisa_BeachUmbrella.glb")
    C.build_lods("Nagisa_Lounger", lounger, "Nagisa_Lounger.glb")
    C.build_lods("Nagisa_LifeguardTower", lifeguard_tower, "Nagisa_LifeguardTower.glb")
    C.build_lods("Nagisa_PromenadeLamp", lamp, "Nagisa_PromenadeLamp.glb")
    C.build_lods("Nagisa_Bench", bench, "Nagisa_Bench.glb")
    C.build_lods("Nagisa_Railing", railing, "Nagisa_Railing.glb")


main()

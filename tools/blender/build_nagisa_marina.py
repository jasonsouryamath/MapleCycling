"""
NAGISA BAY (B5) - marina kit (Blender 4.5 -> GLB).

  Nagisa_Pontoon     40 m main walkway + 6 x 10 m finger pontoons, piles, cleats
  Nagisa_MotorYacht  16 m white motor yacht (gelcoat hull, dark-glass superstructure, teak deck)
  Nagisa_Sailboat    11 m sloop, 15 m mast, furled boom cover
  Nagisa_WaterTaxi   9 m resort water taxi with canvas canopy (the ambient mover)

Hulls are lofted from stations; every face is wound OUTWARD from the hull axis so backface
culling never opens a hole. Waterline = y 0 (sea level), bow = +z. Origin = hull centre.
Run:  blender -b -P tools/blender/build_nagisa_marina.py      PROVISIONAL art numbers.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo, sub, cross, dot, norm  # noqa: E402

C.TILE["NB_Concrete"] = (3.0, 3.0)


def face_out(g, pts, mat, centre, uvs=None):
    """Add a face whose normal points away from `centre` (x, y, z)."""
    n = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
    c = [sum(p[k] for p in pts) / len(pts) for k in range(3)]
    if dot(n, sub(tuple(c), centre)) < 0:
        pts = list(reversed(pts))
        if uvs is not None:
            uvs = list(reversed(uvs))
    g.face(pts, mat, uvs)


def hull(g, length, beam, freeboard, draft, stations, sections, deck_mat="NB_Teak",
         hull_mat="NB_Gelcoat", boot=True):
    """Lofted planing hull. Returns sheer points (port, starboard) per station."""
    L = length
    rings = []
    for i in range(stations + 1):
        s = i / stations                       # 0 = stern, 1 = bow
        z = -L / 2 + s * L
        taper = 1.0 if s < 0.55 else math.cos((s - 0.55) / 0.45 * math.pi / 2) ** 0.9
        b = beam / 2 * max(0.04, taper) * (0.92 + 0.08 * math.sin(s * math.pi))
        fb = freeboard * (1.0 + 0.35 * s ** 2)  # sheer rises to the bow
        dr = draft * (0.25 + 0.75 * max(0.0, 1 - abs(s - 0.35) / 0.65)) * (1 - 0.8 * s ** 4)
        ring = []
        for k in range(sections + 1):          # keel (k=0) -> sheer (k=sections), starboard
            a = k / sections
            x = b * math.sin(a * math.pi / 2) ** 0.7
            y = -dr + (fb + dr) * a ** 1.3
            ring.append((x, y, z))
        rings.append(ring)
    centre_axis = lambda z: (0.0, freeboard * 0.2, z)
    for i in range(stations):
        for k in range(sections):
            for side in (1, -1):
                p = [rings[i][k], rings[i + 1][k], rings[i + 1][k + 1], rings[i][k + 1]]
                p = [(side * q[0], q[1], q[2]) for q in p]
                zc = (p[0][2] + p[1][2]) / 2
                mat = hull_mat
                if boot and p[0][1] < 0.12 and p[3][1] > 0.02:
                    mat = "NB_Superstructure"   # dark boot stripe at the waterline
                face_out(g, p, mat, centre_axis(zc))
    # deck
    for i in range(stations):
        a, b = rings[i][-1], rings[i + 1][-1]
        p = [(-a[0], a[1], a[2]), (a[0], a[1], a[2]), (b[0], b[1], b[2]), (-b[0], b[1], b[2])]
        face_out(g, p, deck_mat, (0.0, -10.0, (a[2] + b[2]) / 2))
    # transom
    r0 = rings[0]
    pts = [(q[0], q[1], q[2]) for q in r0] + [(-q[0], q[1], q[2]) for q in reversed(r0)]
    face_out(g, pts, hull_mat, (0.0, freeboard * 0.2, 1.0))
    return rings


def rail(g, rings, h, detail, start=0.15, end=0.9):
    if detail > 0:
        return
    n = len(rings) - 1
    for side in (1, -1):
        prev = None
        for i in range(int(start * n), int(end * n) + 1):
            q = rings[i][-1]
            p = (side * (q[0] - 0.12), q[1] + h, q[2])
            g.box((p[0], q[1] + h / 2, p[2]), (0.04, h, 0.04), "NB_Metal", top=False)
            if prev is not None:
                mid = ((p[0] + prev[0]) / 2, p[1], (p[2] + prev[2]) / 2)
                L = math.dist(p, prev)
                yaw = math.atan2(p[0] - prev[0], p[2] - prev[2])
                g.box(mid, (0.04, 0.04, L), "NB_Metal", yaw=yaw, top=True, bottom=True)
            prev = p


def motor_yacht(detail):
    g = Geo()
    st, se = (14, 8, 4)[detail], (6, 4, 2)[detail]
    rings = hull(g, 16.0, 4.6, 1.6, 0.9, st, se)
    fb = 1.6
    # superstructure: two tiers of dark glass + white roofs
    g.box((0, fb + 1.0, -1.2), (3.6, 2.0, 7.2), "NB_Superstructure", top=False)
    g.box((0, fb + 2.08, -1.2), (3.9, 0.16, 7.8), "NB_Gelcoat", top=True, bottom=True)
    g.box((0, fb + 2.9, -2.0), (3.0, 1.6, 4.4), "NB_Superstructure", top=False)
    g.box((0, fb + 3.76, -2.4), (3.4, 0.14, 5.6), "NB_Gelcoat", top=True, bottom=True)
    if detail < 2:
        g.box((0, fb + 4.3, -1.2), (0.3, 1.0, 0.3), "NB_Metal")                    # radar mast
        g.box((0, fb + 4.8, -1.2), (1.6, 0.1, 0.2), "NB_Metal", top=True, bottom=True)
        g.box((0, fb + 0.25, -6.9), (4.0, 0.12, 1.6), "NB_Teak", top=True)          # swim platform
    rail(g, rings, 0.8, detail)
    return g


def sailboat(detail):
    g = Geo()
    st, se = (12, 6, 4)[detail], (6, 4, 2)[detail]
    rings = hull(g, 11.0, 3.6, 1.1, 0.7, st, se)
    fb = 1.1
    g.box((0, fb + 0.45, -0.6), (2.2, 0.9, 3.6), "NB_Gelcoat", top=True)            # coachroof
    if detail < 2:
        g.box((0, fb + 0.55, -0.6), (2.24, 0.26, 2.8), "NB_Superstructure", top=False)  # ports
    seg = (8, 6, 4)[detail]
    g.cylinder((0, fb, 0.8), 0.09, 15.0, seg, "NB_Metal", top=True, r_top=0.06)       # mast
    g.cylinder((0, fb + 2.0, 0.8), 0.18, 0.2, seg, "NB_Metal", top=True)
    # boom with a navy sail cover
    g.box((0, fb + 2.0, -1.8), (0.34, 0.42, 4.8), "NB_Superstructure", top=True, bottom=True)
    if detail == 0:   # stays as thin boxes
        for (z0, z1) in ((0.8, 5.3), (0.8, -5.2)):
            top = (0.0, fb + 14.8, z0)
            bot = (0.0, fb + 0.2, z1)
            mid = tuple((a + b) / 2 for a, b in zip(top, bot))
            L = math.dist(top, bot)
            pitch = math.atan2(top[2] - bot[2], top[1] - bot[1])
            # approximate with a vertical box tilted by building quads manually
            dz = (top[2] - bot[2]) / 2
            g.quad((-0.02, bot[1], bot[2]), (0.02, bot[1], bot[2]), (0.02, top[1], top[2]),
                   (-0.02, top[1], top[2]), "NB_Metal", double=True)
            _ = (mid, L, pitch, dz)
    rail(g, rings, 0.6, detail)
    return g


def water_taxi(detail):
    g = Geo()
    st, se = (10, 6, 4)[detail], (5, 3, 2)[detail]
    hull(g, 9.0, 2.9, 0.9, 0.5, st, se, deck_mat="NB_Teak")
    fb = 0.9
    # canopy on four posts, bench seats, helm console
    for x in (-1.1, 1.1):
        for z in (-2.6, 1.6):
            g.box((x, fb + 1.0, z), (0.07, 2.0, 0.07), "NB_Metal", top=False)
    g.box((0, fb + 2.02, -0.5), (2.6, 0.08, 4.6), "NB_Canvas", top=True, bottom=True)
    if detail < 2:
        for z in (-2.2, -1.0, 0.2):
            g.box((0, fb + 0.25, z), (2.2, 0.5, 0.5), "NB_PaintTeal", top=True)
        g.box((0, fb + 0.55, 1.4), (0.9, 1.1, 0.6), "NB_Gelcoat", top=True)
        g.box((0, fb + 1.2, 1.25), (0.9, 0.3, 0.05), "NB_Superstructure")
    return g


def pontoon(detail):
    g = Geo()
    deck_y = 0.55
    L, W = 40.0, 3.0
    g.box((0, deck_y - 0.3, 0), (W, 0.6, L), "NB_PontoonWood",
          mats={"side": "NB_Concrete", "top": "NB_PontoonWood"})
    for k in range(6):
        z = -L / 2 + 4.0 + k * 6.4
        g.box((W / 2 + 5.0, deck_y - 0.3, z), (10.0, 0.6, 1.4), "NB_PontoonWood",
              mats={"side": "NB_Concrete", "top": "NB_PontoonWood"})
        if detail < 2:
            g.cylinder((W / 2 + 10.2, -3.0, z), 0.22, 5.2, (8, 5)[detail], "NB_Concrete", top=True)
            g.box((W / 2 + 9.6, deck_y + 0.08, z + 0.5), (0.3, 0.16, 0.12), "NB_Metal")  # cleat
    if detail < 2:
        for z in (-L / 2 + 1.0, 0.0, L / 2 - 1.0):
            for x in (-W / 2 - 0.3, W / 2 + 0.3):
                g.cylinder((x, -3.0, z), 0.25, 5.4, (8, 5)[detail], "NB_Concrete", top=True)
    if detail == 0:   # lamp posts on the walkway
        for z in (-12.0, 12.0):
            g.box((-W / 2 + 0.2, deck_y + 1.1, z), (0.1, 2.2, 0.1), "NB_Metal", top=False)
            g.box((-W / 2 + 0.2, deck_y + 2.3, z), (0.3, 0.3, 0.3), "NB_Lamp", top=True, bottom=True)
    return g


def main():
    C.build_lods("Nagisa_Pontoon", pontoon, "Nagisa_Pontoon.glb")
    C.build_lods("Nagisa_MotorYacht", motor_yacht, "Nagisa_MotorYacht.glb")
    C.build_lods("Nagisa_Sailboat", sailboat, "Nagisa_Sailboat.glb")
    C.build_lods("Nagisa_WaterTaxi", water_taxi, "Nagisa_WaterTaxi.glb")


main()

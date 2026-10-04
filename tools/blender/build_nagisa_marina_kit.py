"""
NAGISA BAY - MARINA 2 kit (worker G, destination brief section 7). Blender 4.5 -> GLBs in
Assets/Environment/NagisaBay/Models, three authored LODs each (C.build_lods).

  Nagisa_MR_Superyacht   34 m three-deck white superyacht (the marina's showpiece, moored at the head)
  Nagisa_MR_Sloop        14 m cruising sloop, mainsail + jib hoisted (sailboats read as sailboats)
  Nagisa_MR_Catamaran    13 m sailing catamaran, trampoline deck, mast + sails
  Nagisa_MR_Runabout     6.5 m open sport boat with windscreen and outboard
  Nagisa_MR_Dinghy       3.4 m tender
  Nagisa_MR_FishingBoat  13 m working boat: wheelhouse forward, outrigger booms, deck gear, red/blue hull
  Nagisa_MR_FishShed     14 x 8 m fish-market hall: low-pitch metal roof, open loading bay, ice machine
  Nagisa_MR_FishDock     6 x 3 m dock clutter: stacked crates, barrels, net heaps, buoy rack
  Nagisa_MR_NetRack      A-frame net-drying rack with hanging nets
  Nagisa_MR_BoatHoist    mobile boat-hoist (travel lift) straddling a slip
  Nagisa_MR_Gull         0.9 m wingspan seagull (gliding pose) for AmbientFlock circling the marina and harbour
  Nagisa_MR_CafeTerrace  10 x 6 m waterfront cafe terrace: timber deck, pergola, tables, parasols, glass wind-screen

Waterline y = 0, bow = +z, origin = hull centre (boats). Props: origin = footprint centre at finished
ground. Material slots reuse the NB_* set; the new slots NB_MR_Hull (hull paint: recoloured per boat by
the C# remap), NB_MR_Sail (cream sailcloth), NB_MR_Net (dark net) are registered by
NagisaBayEnvironment.Marina2.cs.
Run:  blender -b -P tools/blender/build_nagisa_marina_kit.py            PROVISIONAL art numbers.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo, sub, cross, dot, norm  # noqa: E402

# Borrow hull()/rail()/face_out() from the original marina kit without running its main().
_src = open(os.path.join(os.path.dirname(__file__), "build_nagisa_marina.py")).read().split("def motor_yacht")[0]
exec(compile(_src, "build_nagisa_marina_borrowed", "exec"))

C.TILE["NB_MR_Hull"] = (1.0, 1.0)
C.TILE["NB_MR_Sail"] = (2.0, 2.0)
C.TILE["NB_MR_Net"] = (1.0, 1.0)
HULL = "NB_MR_Hull"


def sail(g, a, b, c, mat="NB_MR_Sail"):
    """Double-sided triangular sail (a, b, c in Unity coords)."""
    g.face([a, b, c], mat, [(0, 0), (1, 0), (0.5, 1)])
    g.face([c, b, a], mat, [(0.5, 1), (1, 0), (0, 0)])


def stay(g, p, q, mat="NB_Metal", w=0.03):
    g.quad((p[0] - w, p[1], p[2]), (p[0] + w, p[1], p[2]), (q[0] + w, q[1], q[2]), (q[0] - w, q[1], q[2]),
           mat, double=True)


# ------------------------------------------------------------------ vessels
def superyacht(detail):
    g = Geo()
    st, se = (18, 10, 5)[detail], (7, 5, 3)[detail]
    rings = hull(g, 34.0, 7.6, 2.5, 1.5, st, se, hull_mat=HULL)
    fb = 2.5
    # main deck saloon + sun deck + bridge deck, stepped
    g.box((0, fb + 1.2, -3.0), (6.4, 2.4, 17.0), "NB_Superstructure", top=False)
    g.box((0, fb + 2.5, -3.0), (6.9, 0.2, 18.0), "NB_Gelcoat", top=True, bottom=True)
    g.box((0, fb + 3.5, -5.2), (5.4, 1.8, 11.0), "NB_Superstructure", top=False)
    g.box((0, fb + 4.5, -5.6), (5.9, 0.2, 12.0), "NB_Gelcoat", top=True, bottom=True)
    g.box((0, fb + 5.4, -7.0), (4.2, 1.6, 6.4), "NB_Superstructure", top=False)
    g.box((0, fb + 6.3, -7.2), (4.8, 0.18, 7.2), "NB_Gelcoat", top=True, bottom=True)
    if detail < 2:
        g.box((0, fb + 0.1, -13.5), (5.4, 0.18, 3.6), "NB_Teak", top=True)         # swim platform
        g.box((0, fb + 7.2, -7.2), (0.22, 1.8, 0.22), "NB_Metal")                  # mast
        g.box((0, fb + 7.9, -7.2), (2.4, 0.1, 0.16), "NB_Metal", top=True, bottom=True)
        g.cylinder((0, fb + 6.4, -9.0), 0.55, 0.6, 8, "NB_Gelcoat", top=True)        # radome
        g.box((0, fb + 2.64, 5.4), (3.2, 0.1, 3.4), "NB_Teak", top=True)             # foredeck sunpad
        g.box((0, fb + 0.7, 8.8), (2.6, 1.4, 2.2), "NB_Gelcoat", top=True)           # tender garage / jacuzzi
    rail(g, rings, 0.9, detail)
    return g


def sloop(detail):
    g = Geo()
    st, se = (12, 6, 4)[detail], (6, 4, 2)[detail]
    rings = hull(g, 14.0, 4.1, 1.15, 0.9, st, se, hull_mat=HULL)
    fb = 1.15
    g.box((0, fb + 0.5, -0.8), (2.5, 1.0, 4.6), "NB_Gelcoat", top=True)
    if detail < 2:
        g.box((0, fb + 0.62, -0.8), (2.54, 0.3, 3.6), "NB_Superstructure", top=False)
    g.cylinder((0, fb, 1.4), 0.1, 18.5, (8, 6, 4)[detail], "NB_Metal", top=True, r_top=0.06)       # mast
    mast_top = fb + 18.2
    # mainsail aft of the mast, jib forward
    sail(g, (0, fb + 2.6, 1.3), (0, fb + 2.6, -5.6), (0, mast_top - 0.5, 1.3))
    sail(g, (0, fb + 1.3, 6.8), (0, fb + 1.3, 1.9), (0, mast_top - 1.2, 1.5))
    g.box((0, fb + 2.6, -2.2), (0.16, 0.18, 7.6), "NB_Metal", top=True, bottom=True)              # boom
    if detail == 0:
        stay(g, (0, mast_top, 1.4), (0, fb, 7.4))
        stay(g, (0, mast_top, 1.4), (0, fb + 0.4, -6.8))
    rail(g, rings, 0.6, detail)
    return g


def catamaran(detail):
    g = Geo()
    st, se = (10, 6, 4)[detail], (5, 3, 2)[detail]
    for sx in (-2.8, 2.8):
        h = Geo()
        hull(h, 13.0, 1.5, 1.2, 0.7, st, se, hull_mat=HULL)
        g.merge(h, offset=(sx, 0, 0))
    g.box((0, 1.35, 0.4), (6.6, 0.22, 11.0), "NB_Gelcoat", top=True, bottom=True)                   # bridge deck
    g.box((0, 2.0, -1.8), (4.2, 1.3, 4.6), "NB_Superstructure", top=False)                          # saloon
    g.box((0, 2.72, -1.8), (4.5, 0.14, 5.0), "NB_Gelcoat", top=True, bottom=True)
    if detail < 2:
        g.box((0, 1.47, 4.0), (5.0, 0.04, 3.2), "NB_Canvas", top=True)                                 # trampoline
    g.cylinder((0, 2.7, 2.0), 0.1, 14.0, (8, 6, 4)[detail], "NB_Metal", top=True, r_top=0.06)
    sail(g, (0, 3.4, 1.9), (0, 3.4, -4.6), (0, 15.8, 1.9))
    sail(g, (0, 2.0, 6.4), (0, 2.0, 2.4), (0, 12.8, 2.1))
    return g


def runabout(detail):
    g = Geo()
    st, se = (10, 6, 4)[detail], (5, 3, 2)[detail]
    rings = hull(g, 6.5, 2.3, 0.75, 0.38, st, se, hull_mat=HULL)
    fb = 0.7
    g.box((0, fb + 0.28, -0.4), (1.4, 0.55, 0.6), "NB_Gelcoat", top=True)                           # console
    if detail < 2:
        g.quad((-0.62, fb + 0.5, 0.0), (0.62, fb + 0.5, 0.0), (0.62, fb + 1.0, 0.25), (-0.62, fb + 1.0, 0.25),
               "NB_BalconyGlass", double=True)
        g.box((0, fb + 0.18, -2.0), (1.5, 0.36, 0.7), "NB_Canvas", top=True)                       # rear bench
        g.box((0.0, fb + 0.4, -3.45), (0.32, 0.9, 0.5), "NB_Metal", top=True)                      # outboard
    return g


def dinghy(detail):
    g = Geo()
    st, se = (6, 4, 3)[detail], (4, 3, 2)[detail]
    hull(g, 3.4, 1.5, 0.5, 0.22, st, se, hull_mat=HULL, boot=False)
    if detail < 2:
        g.box((0, 0.5, 0.0), (1.1, 0.06, 0.4), "NB_Teak", top=True)
        g.box((0, 0.5, -1.4), (0.2, 0.3, 0.25), "NB_Metal", top=True)
    return g


def fishing_boat(detail):
    g = Geo()
    st, se = (12, 6, 4)[detail], (6, 4, 2)[detail]
    rings = hull(g, 13.0, 4.2, 1.55, 1.0, st, se, deck_mat="NB_Concrete", hull_mat=HULL, boot=True)
    fb = 1.55
    g.box((0, fb + 1.2, 2.6), (3.0, 2.4, 3.4), "NB_Gelcoat", top=True)                              # wheelhouse forward
    if detail < 2:
        g.box((0, fb + 1.6, 4.35), (2.7, 0.8, 0.06), "NB_Superstructure", top=False)               # windscreen
        g.box((1.52, fb + 1.6, 2.6), (0.06, 0.8, 2.4), "NB_Superstructure", top=False)
        g.box((-1.52, fb + 1.6, 2.6), (0.06, 0.8, 2.4), "NB_Superstructure", top=False)
    g.cylinder((0, fb, 0.2), 0.14, 7.5, (8, 6, 4)[detail], "NB_Metal", top=True, r_top=0.1)         # mast
    # outrigger booms
    for sx in (-1, 1):
        stay(g, (sx * 0.1, fb + 6.4, 0.2), (sx * 5.2, fb + 2.4, 0.2), w=0.07)
    g.box((0, fb + 0.45, -3.4), (3.0, 0.9, 2.4), "NB_PaintTeal", top=True)                          # fish hold hatch
    if detail < 2:
        g.box((0, fb + 0.22, -1.0), (2.6, 0.45, 1.2), "NB_MR_Net", top=True)                         # net pile
        for z in (-5.2, -4.6):
            g.cylinder((1.3, fb, z), 0.28, 0.6, 6, "NB_Paint", top=True)                              # buoys
    return g


# ------------------------------------------------------------------ fishing-harbour + marina props
def fish_shed(detail):
    g = Geo()
    hx, hz, wall_h = 7.0, 4.0, 4.0
    g.box((0, 0.1, 0), (2 * hx + 0.6, 0.2, 2 * hz + 0.6), "NB_Concrete", top=True)                  # slab
    g.box((0, wall_h / 2 + 0.2, hz - 0.2), (2 * hx, wall_h, 0.4), "NB_Concrete", top=False)          # back wall
    g.box((-hx + 0.2, wall_h / 2 + 0.2, 0), (0.4, wall_h, 2 * hz), "NB_Concrete", top=False)         # end walls
    g.box((hx - 0.2, wall_h / 2 + 0.2, 0), (0.4, wall_h, 2 * hz), "NB_Concrete", top=False)
    # blue painted front band + open loading bay (front = -z)
    g.box((0, wall_h + 0.1, -hz + 0.2), (2 * hx, 1.0, 0.3), "NB_PaintTeal", top=False)
    for x in (-hx + 0.6, -hx / 2, 0.0, hx / 2, hx - 0.6):
        g.box((x, wall_h / 2 + 0.2, -hz + 0.3), (0.35, wall_h, 0.35), "NB_Concrete", top=False)
    # low-pitch metal roof (two planes) overhanging
    ridge = wall_h + 1.9
    for s in (-1, 1):
        a = (-hx - 0.6, wall_h + 0.5, s * (hz + 0.7))
        b = (hx + 0.6, wall_h + 0.5, s * (hz + 0.7))
        c = (hx + 0.6, ridge, 0.0)
        d = (-hx - 0.6, ridge, 0.0)
        if s < 0:
            g.quad(a, d, c, b, "NB_Metal", double=True)
        else:
            g.quad(a, b, c, d, "NB_Metal", double=True)
    for z in (-hz + 0.2, hz - 0.2):                                                                  # gable infill
        g.tri((-hx, wall_h + 0.5, z), (hx, wall_h + 0.5, z), (0, ridge, z), "NB_Concrete")
        g.tri((hx, wall_h + 0.5, z), (-hx, wall_h + 0.5, z), (0, ridge, z), "NB_Concrete")
    if detail < 2:
        g.box((hx - 1.4, 1.1, -0.5), (1.8, 1.8, 1.4), "NB_Gelcoat", top=True)                         # ice machine
        for k in range(4):
            g.box((-hx + 1.2 + k * 1.2, 0.5, 0.6), (1.0, 0.6, 0.7), "NB_PaintTeal", top=True)         # fish crates
        g.box((0, 2.6, -hz - 0.1), (5.0, 1.0, 0.1), "NB_Signs", top=True)                             # sign board
    return g


def fish_dock(detail):
    g = Geo()
    for i in range(3):
        for j in range(2):
            for k in range(3 - i):
                g.box((-2.3 + i * 0.75, 0.25 + k * 0.45, -0.6 + j * 0.6), (0.7, 0.4, 0.5),
                      "NB_PaintTeal" if (i + j + k) % 2 == 0 else "NB_Paint", top=True)
    for k in range(3):
        g.cylinder((0.4 + k * 0.7, 0, 0.8), 0.28, 0.85, (8, 6, 4)[detail], "NB_Metal", top=True)    # barrels
    g.box((0.5, 0.3, -0.6), (1.5, 0.5, 1.0), "NB_MR_Net", top=True)                                  # net heap
    if detail < 2:
        g.box((2.3, 0.7, -0.8), (0.1, 1.4, 1.4), "NB_Teak", top=True)                                 # buoy rack
        for k in range(3):
            g.cylinder((2.3, 0.35 + k * 0.4, -0.5 - k * 0.0 + 0.1 * k), 0.17, 0.35, 6, "NB_Paint", top=True)
    return g


def net_rack(detail):
    g = Geo()
    for sx in (-3.6, 0.0, 3.6):
        for z in (-0.9, 0.9):
            g.box((sx, 1.6, z), (0.12, 3.2, 0.12), "NB_Teak", top=True)
    g.box((0, 3.1, -0.9), (7.4, 0.12, 0.12), "NB_Teak", top=True)
    g.box((0, 3.1, 0.9), (7.4, 0.12, 0.12), "NB_Teak", top=True)
    for x0, x1 in ((-3.5, -0.1), (0.1, 3.5)):
        g.quad((x0, 3.05, -0.8), (x1, 3.05, -0.8), (x1, 1.0, -0.4), (x0, 1.0, -0.4), "NB_MR_Net", double=True)
        g.quad((x0, 3.05, 0.8), (x1, 3.05, 0.8), (x1, 0.8, 0.4), (x0, 0.8, 0.4), "NB_MR_Net", double=True)
    return g


def boat_hoist(detail):
    g = Geo()
    for sx in (-3.4, 3.4):
        for z in (-3.0, 3.0):
            g.box((sx, 3.2, z), (0.5, 6.4, 0.5), "NB_PaintTeal", top=True)
        g.box((sx, 0.3, 0), (0.6, 0.6, 7.4), "NB_Metal", top=True)
    g.box((0, 6.5, -3.0), (7.4, 0.55, 0.55), "NB_PaintTeal", top=True)
    g.box((0, 6.5, 3.0), (7.4, 0.55, 0.55), "NB_PaintTeal", top=True)
    g.box((0, 5.6, 0.0), (7.0, 0.35, 0.35), "NB_Metal", top=True)
    if detail < 2:
        for z in (-1.4, 1.4):
            stay(g, (0, 5.6, z), (0, 2.4, z), w=0.05)
        g.box((0, 2.2, 0), (0.4, 0.3, 3.4), "NB_Concrete", top=True)
    return g


def cafe_terrace(detail):
    g = Geo()
    hx, hz = 5.0, 3.0
    g.box((0, 0.2, 0), (2 * hx, 0.4, 2 * hz), "NB_Teak", top=True, mats={"side": "NB_Concrete", "top": "NB_Teak"})
    # pergola frame (posts at corners + mid) and a canvas roof strip
    for x in (-hx + 0.2, 0.0, hx - 0.2):
        for z in (-hz + 0.2, hz - 0.2):
            g.box((x, 1.6, z), (0.14, 2.8, 0.14), "NB_Teak", top=True)
    for x in (-hx + 0.2, 0.0, hx - 0.2):
        g.box((x, 3.05, 0), (0.16, 0.2, 2 * hz), "NB_Teak", top=True)
    for k in range(7):
        g.box((-hx + 0.7 + k * (2 * hx - 1.4) / 6, 3.18, 0), (0.1, 0.1, 2 * hz + 0.6), "NB_Teak", top=True)
    if detail < 2:
        # glass wind-screen along the seaward (-z) edge
        g.quad((-hx + 0.1, 0.45, -hz + 0.05), (hx - 0.1, 0.45, -hz + 0.05), (hx - 0.1, 1.5, -hz + 0.05),
               (-hx + 0.1, 1.5, -hz + 0.05), "NB_BalconyGlass", double=True)
        for k in range(4):
            x = -hx + 1.4 + k * (2 * hx - 2.8) / 3
            z = 0.2 if k % 2 == 0 else -0.5
            g.cylinder((x, 0.4, z), 0.45, 0.72, 8, "NB_Teak", top=True)                                # table
            for a in range(3):
                ang = a * 2.1 + k
                g.box((x + math.cos(ang) * 0.85, 0.65, z + math.sin(ang) * 0.85), (0.42, 0.5, 0.42), "NB_Canvas", top=True)
            g.cylinder((x, 0.4, z), 0.04, 2.3, 4, "NB_Metal", top=False)
            g.cylinder((x, 2.3, z), 1.55, 0.4, 8, "NB_Canvas", top=True, r_top=0.08)                    # parasol
    return g


def gull(detail):
    g = Geo()
    # body (tapered box), head, tail, two swept wings (double-sided quads) with dark tips
    g.box((0, 0, 0), (0.14, 0.12, 0.42), "NB_Gelcoat", top=True, bottom=True)
    g.box((0, 0.03, -0.26), (0.09, 0.09, 0.10), "NB_Gelcoat", top=True, bottom=True)
    g.box((0, 0.0, -0.33), (0.025, 0.025, 0.07), "NB_Paint", top=True, bottom=True)          # beak
    g.box((0, 0.0, 0.26), (0.10, 0.02, 0.14), "NB_Gelcoat", top=True, bottom=True)             # tail
    for sx in (-1, 1):
        root0, root1 = (sx * 0.06, 0.03, -0.12), (sx * 0.06, 0.03, 0.14)
        tip0, tip1 = (sx * 0.52, 0.10, 0.02), (sx * 0.50, 0.08, 0.20)
        g.quad(root0, root1, tip1, tip0, "NB_Gelcoat", double=True)
        if detail < 2:
            g.quad(tip0, tip1, (sx * 0.58, 0.12, 0.22), (sx * 0.60, 0.13, 0.04), "NB_Superstructure", double=True)
    return g


def main():
    C.build_lods("Nagisa_MR_Gull", gull, "Nagisa_MR_Gull.glb")
    for stem, fn in (("Nagisa_MR_Superyacht", superyacht), ("Nagisa_MR_Sloop", sloop),
                     ("Nagisa_MR_Catamaran", catamaran), ("Nagisa_MR_Runabout", runabout),
                     ("Nagisa_MR_Dinghy", dinghy), ("Nagisa_MR_FishingBoat", fishing_boat),
                     ("Nagisa_MR_FishShed", fish_shed), ("Nagisa_MR_FishDock", fish_dock),
                     ("Nagisa_MR_NetRack", net_rack), ("Nagisa_MR_BoatHoist", boat_hoist),
                     ("Nagisa_MR_CafeTerrace", cafe_terrace)):
        C.build_lods(stem, fn, stem + ".glb")


main()

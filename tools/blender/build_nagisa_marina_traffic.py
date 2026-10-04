"""
NAGISA BAY - traffic polish kit (worker G, destination brief section 12). Extends the NB3 highway mix with
the vehicles the brief names that the street/NB3 kits lack:

  Nagisa_MR_LuxurySedan   5.1 x 1.9 x 1.42 m long low executive saloon (black / pearl / champagne)
  Nagisa_MR_Shuttle       6.4 x 2.05 x 2.5 m resort shuttle minibus, window band, roof AC pod, livery stripe
  Nagisa_MR_DeliveryVan   4.6 x 1.7 x 2.1 m small box delivery van, side roller door, livery band
  Nagisa_MR_Scooter       1.85 x 0.7 x 1.45 m step-through scooter with a helmeted rider (motor traffic only)

Same conventions as build_nagisa_vehicles_nb3.py: front = -z, origin = ground centre, LOD0/1/2, slots
NB_CarPaint / NB_CarGlass / NB_Rubber / NB_Chrome / NB_Metal / NB_Trim / NB_Paint / NB_Lamp (Unity re-liveries
NB_CarPaint per vehicle); head/tail lamps are added by Highway.cs from its dims table.
Run:  blender -b -P tools/blender/build_nagisa_marina_traffic.py       PROVISIONAL sizes.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import build_nagisa_common as C           # noqa: E402
from build_nagisa_common import Geo       # noqa: E402
import build_nagisa_arch as A             # noqa: E402

C.TILE.update({"NB_CarPaint": (1.0, 1.0), "NB_CarGlass": (1.0, 1.0), "NB_Rubber": (1.0, 1.0),
               "NB_Metal": (1.0, 1.0)})

_src = open(os.path.join(HERE, "build_nagisa_street.py"), encoding="utf-8").read().split("\nPROPS = {")[0]
S = {"__file__": os.path.join(HERE, "build_nagisa_street.py"), "__name__": "street_kit"}
exec(compile(_src, "build_nagisa_street.py", "exec"), S)
wheel = S["wheel"]


def B(g, c, size, mat, d, bev=0.06):
    A.bbox(g, c, size, mat, bev if d == 0 else 0.0, detail=d)


def side_quad(g, s, y0, y1, z0, z1, x, mat):
    A.qn(g, [(s * x, y0, z0), (s * x, y1, z0), (s * x, y1, z1), (s * x, y0, z1)], (s, 0, 0), mat)


def wheels(g, zs, r, hw, w, d, inset=0.14):
    for z in zs:
        for s in (-1, 1):
            wheel(g, (s * (hw - inset), r, z), r, w, d)


def luxury_sedan(d):
    g = S["car"](d, L=5.1, W=1.9, H=1.42, cab=(0.30, 0.78), belt=0.80, wheel_r=0.34, nose=0.8)
    if d < 2:   # chrome side strip + a long boot lid line
        for s in (-1, 1):
            B(g, (s * 0.955, 0.80, 0.0), (0.02, 0.04, 4.4), "NB_Chrome", 2)
    return g


def shuttle(d):
    g = Geo()
    L, W = 6.4, 2.05
    hw, hl = W / 2, L / 2
    B(g, (0, 0.95, 0.0), (W, 1.1, L), "NB_CarPaint", d, 0.12)                    # lower body 0.4 .. 1.5
    B(g, (0, 1.98, 0.35), (W - 0.06, 0.95, L - 0.9), "NB_CarGlass", d, 0.08)     # window band
    B(g, (0, 2.5, 0.0), (W, 0.12, L - 0.2), "NB_CarPaint", d, 0.08)              # roof
    S["frustum"](g, hw - 0.04, (-hl + 0.05, -hl + 1.0), hw - 0.1, (-hl + 0.45, -hl + 1.0), 1.45, 2.5,
                 "NB_CarGlass", "NB_CarGlass", "NB_CarPaint")                     # raked windscreen/cab
    if d < 2:
        for s in (-1, 1):
            for z in (-1.6, -0.5, 0.6, 1.7, 2.7):                                # window pillars
                A.seg_box(g, (s * (hw - 0.02), 1.5, z), (s * (hw - 0.02), 2.48, z), 0.09, 0.06, "NB_CarPaint")
            B(g, (s * (hw + 0.005), 1.18, 0.0), (0.02, 0.16, L - 0.8), "NB_Paint", 2)    # livery stripe
            side_quad(g, s, 0.5, 1.5, -1.0, 0.2, hw + 0.006, "NB_Rubber") if s > 0 else None   # sliding door recess
        B(g, (0, 2.68, 0.6), (1.4, 0.22, 2.2), "NB_Trim", d, 0.08)                      # roof AC pod
        B(g, (0, 0.5, -hl - 0.03), (W, 0.28, 0.12), "NB_Rubber", d)
        B(g, (0, 0.5, hl + 0.03), (W, 0.28, 0.12), "NB_Rubber", d)
    wheels(g, (-hl + 1.15, hl - 1.2), 0.38, hw, 0.24, d, 0.12)
    return g


def delivery_van(d):
    g = Geo()
    L, W = 4.6, 1.7
    hw, hl = W / 2, L / 2
    B(g, (0, 0.8, -hl + 0.7), (W, 0.95, 1.4), "NB_CarPaint", d, 0.12)               # cab lower
    S["frustum"](g, hw - 0.05, (-hl + 0.2, -hl + 1.4), hw - 0.12, (-hl + 0.55, -hl + 1.35), 1.2, 1.85,
                 "NB_CarGlass", "NB_CarGlass", "NB_CarPaint")
    B(g, (0, 1.25, 0.65), (W, 1.9, 3.0), "NB_Trim", d, 0.1)                            # box body 0.3 .. 2.2
    if d < 2:
        for s in (-1, 1):
            B(g, (s * (hw + 0.005), 1.25, 0.65), (0.02, 0.5, 2.8), "NB_Paint", 2)      # livery band
            B(g, (s * (hw + 0.005), 0.5, 0.65), (0.02, 0.2, 2.8), "NB_Rubber", 2)
            B(g, (s * (hw + 0.1), 1.5, -hl + 1.0), (0.08, 0.3, 0.1), "NB_Rubber", 2)   # mirrors
        side_quad(g, 1, 0.5, 2.0, -0.3, 1.4, hw + 0.008, "NB_Metal")                    # roller door (kerb side)
        B(g, (0, 0.38, hl + 0.04), (W, 0.2, 0.12), "NB_Rubber", d)
        B(g, (0, 0.38, -hl - 0.03), (W, 0.2, 0.12), "NB_Rubber", d)
    wheels(g, (-hl + 0.85, hl - 0.8), 0.32, hw, 0.2, d, 0.1)
    return g


def scooter(d):
    g = Geo()
    wheel(g, (0, 0.21, -0.62), 0.21, 0.1, d)
    wheel(g, (0, 0.21, 0.55), 0.21, 0.12, d)
    B(g, (0, 0.34, -0.05), (0.3, 0.07, 0.8), "NB_CarPaint", d, 0.03)        # floorboard
    B(g, (0, 0.52, 0.55), (0.34, 0.34, 0.5), "NB_CarPaint", d, 0.08)        # rear body
    B(g, (0, 0.55, -0.55), (0.26, 0.5, 0.2), "NB_CarPaint", d, 0.06)        # leg shield
    B(g, (0, 0.74, 0.42), (0.28, 0.1, 0.6), "NB_Rubber", d, 0.04)           # seat
    if d < 2:
        A.seg_box(g, (0, 0.8, -0.55), (0, 1.05, -0.62), 0.05, 0.05, "NB_Chrome")
        A.seg_box(g, (-0.3, 1.05, -0.62), (0.3, 1.05, -0.62), 0.035, 0.035, "NB_Rubber")
        B(g, (0, 0.5, 0.88), (0.2, 0.1, 0.12), "NB_Metal", 2)
    # rider: upright, open helmet, light jacket
    B(g, (0, 1.08, 0.38), (0.38, 0.55, 0.26), "NB_Paint", d, 0.08)
    B(g, (-0.12, 0.8, 0.2), (0.13, 0.13, 0.5), "NB_Rubber", d, 0.04)
    B(g, (0.12, 0.8, 0.2), (0.13, 0.13, 0.5), "NB_Rubber", d, 0.04)
    if d < 2:
        for s in (-1, 1):
            A.seg_box(g, (s * 0.2, 1.22, 0.3), (s * 0.28, 1.0, -0.5), 0.09, 0.09, "NB_Paint")
            A.seg_box(g, (s * 0.13, 0.7, 0.0), (s * 0.13, 0.34, -0.1), 0.1, 0.1, "NB_Rubber")
        B(g, (0, 1.5, 0.35), (0.25, 0.25, 0.28), "NB_CarPaint", d, 0.1)
    return g


PROPS = {"Nagisa_MR_LuxurySedan": luxury_sedan, "Nagisa_MR_Shuttle": shuttle,
         "Nagisa_MR_DeliveryVan": delivery_van, "Nagisa_MR_Scooter": scooter}


def main():
    for name, fn in PROPS.items():
        C.build_lods(name, fn, name + ".glb")


main()

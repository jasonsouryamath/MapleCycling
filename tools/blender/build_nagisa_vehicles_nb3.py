"""
NAGISA BAY NB3 - highway vehicle kit (coach, city bus, box truck, surf SUV, convertible, camper, motorbike).

Run: blender.exe -b -P tools/blender/build_nagisa_vehicles_nb3.py [-- Names...]

Same conventions as build_nagisa_street.py (front = -z, origin at ground centre, LOD0/1/2, material
slots NB_CarPaint / NB_CarGlass / NB_Rubber / NB_Chrome / NB_Metal / NB_Trim / NB_Paint / NB_Lamp so the
Unity side re-liveries NB_CarPaint per vehicle). Head/tail lights are NOT modelled here: Highway.cs adds
emissive lamp quads from the HwDims table. Fictional liveries only. PROVISIONAL sizes.

  Nagisa_NB3_Coach        12.0 x 2.5 x 3.3 m tour coach, three axles, luggage bays, roof AC pod
  Nagisa_NB3_CityBus      10.5 x 2.5 x 3.1 m low-floor bus, big side glass, kerb-side doors, roof pod
  Nagisa_NB3_BoxTruck      7.4 x 2.3 x 3.3 m cab + white box, roller door, chassis rails
  Nagisa_NB3_SurfSUV       4.8 x 1.9 x 2.0 m SUV with roof rails and three surfboards
  Nagisa_NB3_Convertible   4.5 x 1.8 x 1.25 m open two-seater, roll hoops, folded hood
  Nagisa_NB3_Camper        5.6 x 2.05 x 2.9 m camper van, pop-top, awning stripe, roof board
  Nagisa_NB3_Motorbike     2.1 x 0.8 x 1.4 m road bike with a helmeted rider (motor traffic only)
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
               "NB_Metal": (1.0, 1.0), "NB_Net": (1.0, 1.0)})

# Re-use the street kit's car(), wheel() etc. without running its main().
_src = open(os.path.join(HERE, "build_nagisa_street.py"), encoding="utf-8").read().split("\nPROPS = {")[0]
S = {"__file__": os.path.join(HERE, "build_nagisa_street.py"), "__name__": "street_kit"}
exec(compile(_src, "build_nagisa_street.py", "exec"), S)
car, wheel, hcyl = S["car"], S["wheel"], S["hcyl"]


def B(g, c, size, mat, d, bev=0.06):
    A.bbox(g, c, size, mat, bev if d == 0 else 0.0, detail=d)


def side_quad(g, s, y0, y1, z0, z1, x, mat):
    p = [(s * x, y0, z0), (s * x, y1, z0), (s * x, y1, z1), (s * x, y0, z1)]
    A.qn(g, p, (s, 0, 0), mat)


def board(g, cx, y, cz, L, W, mat, th=0.06, tip=0.55):
    """Surfboard on a roof: outline in x/z, long axis z, pointed nose."""
    poly = []
    n = 10
    for i in range(n):
        a = i / n * math.tau
        zz = math.cos(a)
        xx = math.sin(a) * (1.0 - 0.35 * max(0.0, -zz))        # wider towards the tail
        poly.append((cx + xx * W / 2, cz + zz * L / 2 * (1.0 if zz < 0 else tip + (1 - tip))))
    A.prism_poly(g, poly, y, y + th, mat)


def wheels(g, zs, r, hw, w, d, inset=0.14):
    for z in zs:
        for s in (-1, 1):
            wheel(g, (s * (hw - inset), r, z), r, w, d)


# ------------------------------------------------------------------ coach
def coach(d):
    g = Geo()
    L, W = 12.0, 2.5
    hw, hl = W / 2, L / 2
    B(g, (0, 0.95, 0), (W, 1.1, L), "NB_CarPaint", d, 0.12)
    B(g, (0, 2.15, 0.1), (W - 0.08, 1.25, L - 0.6), "NB_CarGlass", d, 0.08)
    B(g, (0, 2.93, 0), (W, 0.22, L - 0.2), "NB_Trim", d, 0.09)
    if d < 2:
        for s in (-1, 1):
            for i in range(9):
                z = -5.0 + i * 1.25
                A.seg_box(g, (s * (hw - 0.02), 1.5, z), (s * (hw - 0.02), 2.84, z), 0.1, 0.06, "NB_CarPaint")
            B(g, (s * (hw + 0.005), 1.22, 0.2), (0.02, 0.16, L - 1.6), "NB_Paint", 2)      # livery swoosh
            for z0, z1 in ((-3.0, -1.5), (0.9, 2.4), (3.4, 4.9)):                           # luggage bays
                side_quad(g, s, 0.6, 1.4, z0, z1, hw + 0.006, "NB_Rubber")
            B(g, (s * (hw + 0.2), 2.15, -hl + 0.5), (0.08, 0.42, 0.1), "NB_Rubber", 2)      # mirrors
        B(g, (0, 0.52, -hl - 0.05), (W, 0.3, 0.16), "NB_Rubber", d)
        B(g, (0, 0.52, hl + 0.05), (W, 0.3, 0.16), "NB_Rubber", d)
        B(g, (0, 3.22, 1.6), (1.7, 0.26, 2.6), "NB_Trim", d, 0.1)                           # AC pod
        B(g, (0, 1.5, -hl - 0.02), (W - 0.5, 0.45, 0.05), "NB_Rubber", 2)                    # grille
        for s in (-1, 1):                                                                   # entrance door glass (kerb side +x)
            pass
        A.qn(g, [(hw + 0.008, 0.45, -4.6), (hw + 0.008, 2.6, -4.6), (hw + 0.008, 2.6, -3.5), (hw + 0.008, 0.45, -3.5)],
             (1, 0, 0), "NB_CarGlass")
    wheels(g, (-3.9, 3.0, 4.4), 0.52, hw, 0.32, d, 0.12)
    return g


# ------------------------------------------------------------------ city bus
def city_bus(d):
    g = Geo()
    L, W = 10.5, 2.5
    hw, hl = W / 2, L / 2
    B(g, (0, 0.8, 0), (W, 0.9, L), "NB_CarPaint", d, 0.1)          # 0.35 .. 1.25
    B(g, (0, 1.95, 0), (W - 0.06, 1.3, L - 0.2), "NB_CarGlass", d, 0.08)
    B(g, (0, 2.74, 0), (W, 0.2, L - 0.1), "NB_CarPaint", d, 0.08)
    if d < 2:
        for s in (-1, 1):
            for i in range(8):
                z = -4.6 + i * 1.3
                A.seg_box(g, (s * (hw - 0.02), 1.28, z), (s * (hw - 0.02), 2.64, z), 0.12, 0.06, "NB_CarPaint")
            B(g, (s * (hw + 0.005), 1.05, 0), (0.02, 0.16, L - 0.6), "NB_Trim", 2)
        # kerb side (+x) doors: two dark glass leaves + wider recess
        for z0, z1 in ((-4.5, -3.5), (1.0, 2.0)):
            A.qn(g, [(hw + 0.01, 0.4, z0), (hw + 0.01, 2.6, z0), (hw + 0.01, 2.6, z1), (hw + 0.01, 0.4, z1)], (1, 0, 0), "NB_Rubber")
        B(g, (0, 0.5, -hl - 0.05), (W, 0.28, 0.14), "NB_Rubber", d)
        B(g, (0, 0.5, hl + 0.05), (W, 0.28, 0.14), "NB_Rubber", d)
        B(g, (0, 2.96, -2.0), (1.6, 0.24, 2.2), "NB_Trim", d, 0.1)                           # AC pod
        B(g, (0, 2.96, 2.6), (1.4, 0.2, 1.4), "NB_Trim", d, 0.1)
        B(g, (0, 2.58, -hl - 0.02), (1.3, 0.18, 0.05), "NB_Lamp", 2)                          # route board
    wheels(g, (-3.5, 3.4), 0.5, hw, 0.32, d, 0.1)
    return g


# ------------------------------------------------------------------ box truck
def box_truck(d):
    g = Geo()
    L, W = 7.4, 2.3
    hw, hl = W / 2, L / 2
    # chassis + cab
    B(g, (0, 0.62, 0), (W - 0.4, 0.34, L - 0.2), "NB_Metal", d, 0.05)
    B(g, (0, 1.35, -hl + 1.0), (W, 1.25, 1.9), "NB_CarPaint", d, 0.12)
    S["frustum"](g, hw - 0.06, (-hl + 0.12, -hl + 1.85), hw - 0.14, (-hl + 0.55, -hl + 1.8), 1.7, 2.45,
                 "NB_CarGlass", "NB_CarGlass", "NB_CarPaint")
    # box body
    B(g, (0, 2.1, 1.1), (W, 2.6, 5.0), "NB_Trim", d, 0.1)
    if d < 2:
        for s in (-1, 1):
            B(g, (s * (hw + 0.005), 1.35, 1.1), (0.02, 0.5, 4.6), "NB_CarPaint", 2)            # livery band
            B(g, (s * (hw - 0.06), 0.62, 1.1), (0.04, 0.2, 4.4), "NB_Rubber", 2)               # skirt
            B(g, (s * (hw + 0.14), 1.9, -hl + 0.55), (0.1, 0.42, 0.1), "NB_Rubber", 2)         # mirrors
        A.qn(g, [(-hw + 0.2, 0.9, hl + 0.006), (hw - 0.2, 0.9, hl + 0.006), (hw - 0.2, 3.0, hl + 0.006),
                 (-hw + 0.2, 3.0, hl + 0.006)], (0, 0, 1), "NB_Metal")                          # roller door
        B(g, (0, 0.5, hl + 0.06), (W, 0.2, 0.14), "NB_Rubber", d)
        B(g, (0, 0.5, -hl - 0.04), (W, 0.24, 0.14), "NB_Rubber", d)
        for k in range(4):                                                                  # roof ribs
            B(g, (0, 3.42, -0.8 + k * 1.2), (W - 0.1, 0.05, 0.1), "NB_Metal", 2)
    wheels(g, (-hl + 1.1, hl - 1.2), 0.46, hw, 0.3, d, 0.12)
    return g


# ------------------------------------------------------------------ surf SUV
def surf_suv(d):
    g = S["car"](d, L=4.8, W=1.9, H=1.78, cab=(0.26, 0.97), belt=0.96, wheel_r=0.37, nose=0.9, van=False)
    if d < 2:
        hw = 0.95
        for s in (-1, 1):                                  # roof rails
            A.seg_box(g, (s * (hw - 0.3), 1.83, -0.5), (s * (hw - 0.3), 1.83, 1.7), 0.05, 0.05, "NB_Chrome")
        for z in (-0.3, 1.5):                              # cross bars
            A.seg_box(g, (-hw + 0.3, 1.9, z), (hw - 0.3, 1.9, z), 0.06, 0.05, "NB_Metal")
        for x, mat, ln in ((-0.5, "NB_Gelcoat", 2.5), (0.0, "NB_PaintTeal", 2.3), (0.5, "NB_Paint", 2.1)):
            board(g, x, 1.94 + (0.0 if x == 0 else 0.0), 0.5, ln, 0.38, mat)
    return g


# ------------------------------------------------------------------ convertible
def convertible(d):
    g = Geo()
    L, W = 4.5, 1.8
    hw, hl = W / 2, L / 2
    B(g, (0, 0.55, 0), (W, 0.6, L), "NB_CarPaint", d, 0.14)            # tub 0.25 .. 0.85
    if d < 2:
        # bonnet hump + boot deck
        A.qn(g, [(-hw + 0.1, 0.85, -hl + 0.05), (hw - 0.1, 0.85, -hl + 0.05), (hw - 0.14, 0.93, -hl + 1.1),
                 (-hw + 0.14, 0.93, -hl + 1.1)], (0, 1, -0.2), "NB_CarPaint")
        # cockpit: dark interior + two seats
        A.qn(g, [(-hw + 0.12, 0.87, -0.4), (hw - 0.12, 0.87, -0.4), (hw - 0.12, 0.87, 1.5), (-hw + 0.12, 0.87, 1.5)],
             (0, 1, 0), "NB_Rubber")
        for x in (-0.42, 0.42):
            B(g, (x, 1.0, 0.75), (0.55, 0.12, 0.55), "NB_Trim", d, 0.04)       # cushion
            B(g, (x, 1.25, 1.05), (0.55, 0.5, 0.12), "NB_Trim", d, 0.04)       # backrest
        # windscreen (raked) + frame
        A.qn(g, [(-hw + 0.1, 0.9, -0.55), (hw - 0.1, 0.9, -0.55), (hw - 0.1, 1.27, -0.12), (-hw + 0.1, 1.27, -0.12)],
             (0, 0.6, -1), "NB_CarGlass")
        A.seg_box(g, (-hw + 0.1, 1.27, -0.12), (hw - 0.1, 1.27, -0.12), 0.05, 0.04, "NB_Chrome")
        for x in (-0.55, 0.55):                                               # roll hoops
            A.seg_box(g, (x, 0.88, 1.35), (x, 1.32, 1.35), 0.07, 0.07, "NB_Chrome")
        B(g, (0, 0.95, 1.7), (W - 0.2, 0.18, 0.5), "NB_Trim", d, 0.05)         # folded hood
        for z, s in ((-hl - 0.02, -1), (hl + 0.02, 1)):
            B(g, (0, 0.38, z), (W - 0.04, 0.18, 0.12), "NB_Rubber", d, 0.03)
        for s in (-1, 1):
            B(g, (s * (hw + 0.06), 1.0, -0.5), (0.1, 0.07, 0.1), "NB_CarPaint", 2)  # mirrors
    wheels(g, (-hl + 0.85, hl - 0.8), 0.33, hw, 0.22, d, 0.13)
    return g


# ------------------------------------------------------------------ camper van
def camper(d):
    g = Geo()
    L, W = 5.6, 2.05
    hw, hl = W / 2, L / 2
    B(g, (0, 1.1, 0.35), (W, 1.7, L - 0.7), "NB_CarPaint", d, 0.12)      # box body 0.25 .. 1.95
    B(g, (0, 0.9, -hl + 0.55), (W, 1.2, 1.1), "NB_CarPaint", d, 0.12)    # bonnet / nose
    S["frustum"](g, hw - 0.05, (-hl + 1.1, -hl + 1.95), hw - 0.12, (-hl + 1.55, -hl + 1.95), 1.5, 2.1,
                 "NB_CarGlass", "NB_CarGlass", "NB_CarPaint")
    B(g, (0, 2.3, 0.6), (W - 0.2, 0.45, 3.4), "NB_Trim", d, 0.1)           # pop-top shell
    if d < 2:
        for s in (-1, 1):
            side_quad(g, s, 1.25, 1.85, -0.2, 1.3, hw + 0.006, "NB_CarGlass")      # side windows
            side_quad(g, s, 1.25, 1.85, 1.8, 2.4, hw + 0.006, "NB_CarGlass")
            B(g, (s * (hw + 0.006), 0.85, 0.35), (0.02, 0.14, 4.4), "NB_Paint", 2)  # stripe
            B(g, (s * (hw + 0.1), 1.45, -hl + 1.2), (0.08, 0.36, 0.1), "NB_Rubber", 2)
        # awning roll above the sliding door, kerb side
        A.seg_box(g, (hw + 0.1, 2.0, -0.2), (hw + 0.1, 2.0, 1.6), 0.1, 0.1, "NB_Paint")
        # roof rack + board
        for z in (-0.2, 1.4):
            A.seg_box(g, (-hw + 0.3, 2.58, z), (hw - 0.3, 2.58, z), 0.05, 0.05, "NB_Metal")
        board(g, 0.2, 2.62, 0.6, 2.6, 0.42, "NB_PaintTeal")
        B(g, (0, 0.38, hl + 0.05), (W, 0.2, 0.12), "NB_Rubber", d, 0.03)
        B(g, (0, 0.38, -hl - 0.03), (W, 0.2, 0.12), "NB_Rubber", d, 0.03)
        B(g, (0.4, 1.2, hl + 0.02), (0.5, 0.45, 0.04), "NB_Metal", 2)         # spare wheel cover
    wheels(g, (-hl + 1.0, hl - 1.15), 0.36, hw, 0.24, d, 0.12)
    return g


# ------------------------------------------------------------------ motorbike + rider
def motorbike(d):
    g = Geo()
    wheel(g, (0, 0.31, -0.68), 0.31, 0.13, d)
    wheel(g, (0, 0.31, 0.68), 0.31, 0.15, d)
    B(g, (0, 0.55, 0.05), (0.26, 0.28, 0.7), "NB_Metal", d, 0.04)          # engine block
    B(g, (0, 0.86, -0.18), (0.3, 0.26, 0.5), "NB_CarPaint", d, 0.08)       # tank
    B(g, (0, 0.82, 0.38), (0.3, 0.1, 0.55), "NB_Rubber", d, 0.04)          # seat
    if d < 2:
        A.seg_box(g, (0, 0.95, -0.52), (0, 0.4, -0.7), 0.06, 0.06, "NB_Chrome")      # forks
        A.seg_box(g, (-0.34, 1.0, -0.5), (0.34, 1.0, -0.5), 0.04, 0.04, "NB_Rubber")  # bars
        B(g, (0, 0.92, -0.62), (0.2, 0.2, 0.12), "NB_Chrome", 2)               # lamp bucket
        A.seg_box(g, (0.17, 0.4, 0.2), (0.17, 0.5, 0.95), 0.07, 0.07, "NB_Chrome")   # exhaust
        B(g, (0, 0.6, 0.78), (0.2, 0.06, 0.34), "NB_CarPaint", d, 0.02)        # rear mudguard
    # rider: leans forward, full helmet, jacket, trousers (motor traffic only: never on foot)
    B(g, (0, 1.22, 0.3), (0.4, 0.55, 0.28), "NB_Paint", d, 0.08)            # torso
    B(g, (-0.13, 0.9, 0.18), (0.14, 0.14, 0.6), "NB_Rubber", d, 0.04)       # thighs
    B(g, (0.13, 0.9, 0.18), (0.14, 0.14, 0.6), "NB_Rubber", d, 0.04)
    if d < 2:
        for s in (-1, 1):
            A.seg_box(g, (s * 0.2, 1.4, 0.2), (s * 0.3, 1.05, -0.5), 0.1, 0.1, "NB_Paint")   # arms
            A.seg_box(g, (s * 0.15, 0.82, 0.1), (s * 0.16, 0.38, 0.0), 0.12, 0.12, "NB_Rubber")  # shins
        B(g, (0, 1.65, 0.05), (0.26, 0.28, 0.32), "NB_CarPaint", d, 0.1)    # helmet
        B(g, (0, 1.64, -0.12), (0.2, 0.1, 0.04), "NB_CarGlass", 2)            # visor
    return g


PROPS = {
    "Nagisa_NB3_Coach": coach, "Nagisa_NB3_CityBus": city_bus, "Nagisa_NB3_BoxTruck": box_truck,
    "Nagisa_NB3_SurfSUV": surf_suv, "Nagisa_NB3_Convertible": convertible, "Nagisa_NB3_Camper": camper,
    "Nagisa_NB3_Motorbike": motorbike,
}


def main():
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PROPS)
    for name in want:
        counts = C.build_lods(name, PROPS[name], name + ".glb")
        print("[nagisa-nb3] %-24s %s" % (name, " / ".join("{:,}".format(x) for x in counts)))


main()

"""
NAGISA BAY (B5) - pastel tropical town kit + beach cafe / surf shop (Blender 4.5 -> GLB).

  Nagisa_MidriseA   5 storeys, flat roof, full-width balconies (pastel wall slot NB_TownWall)
  Nagisa_MidriseB   4 storeys, terracotta hip roof, corner balconies
  Nagisa_MidriseC   6 storeys, stepped top floor with roof terrace + pergola
  Nagisa_BeachCafe  single storey cafe, deep canvas awning, sign board, outdoor tables
  Nagisa_SurfShop   single storey surf shop, sign board, surfboard rack

Walls use the slot NB_TownWall (window atlas, 6.0 x 6.4 m tile = 4 x 4 windows of 1.5 x 1.6 m),
which NagisaBayEnvironment.Town.cs tints per instance to pink / mint / lemon / sky / coral.
Footprint half-widths are multiples of 1.5 m and storeys 3.2 m (ground floor 4.8 m) so the
atlas cells land on whole windows. Front (shop / balcony face) = -z. Origin = ground centre.

Run:  blender -b -P tools/blender/build_nagisa_town.py
PROVISIONAL art numbers throughout.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo  # noqa: E402

C.TILE["NB_TownWall"] = (6.0, 6.4)
C.TILE["NB_ShopGlass"] = (6.0, 4.8)
GF = 4.8     # ground floor height
FH = 3.2     # upper storey


def rect(x0, z0, x1, z1):
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1), (x0, z0)]


def walls(g, x0, z0, x1, z1, y0, y1, mat):
    g.ribbon(rect(x0, z0, x1, z1), y0, y1, mat)


def shopfront(g, hx, hz, detail):
    """Ground floor: glazed front (-z), plaster elsewhere, stone plinth band."""
    g.ribbon([(-hx, -hz), (hx, -hz)], 0.0, GF - 0.9, "NB_Glass", v_tile=12.8)
    g.ribbon([(-hx, -hz), (hx, -hz)], GF - 0.9, GF, "NB_Plaster")
    g.ribbon([(hx, -hz), (hx, hz), (-hx, hz), (-hx, -hz)], 0.0, GF, "NB_Plaster")
    if detail < 2:   # mullions
        n = int(hx * 2 / 3.0)
        for k in range(n + 1):
            x = -hx + k * (2 * hx / n)
            g.box((x, (GF - 0.9) / 2, -hz - 0.08), (0.18, GF - 0.9, 0.18), "NB_Bronze", top=False)
    # fascia canopy over the shopfront
    g.box((0, GF - 0.55, -hz - 1.1), (2 * hx + 0.4, 0.22, 2.2), "NB_Plaster", bottom=True)


def balconies(g, hx, hz, floors, detail, x_from=None, x_to=None):
    xa = -hx if x_from is None else x_from
    xb = hx if x_to is None else x_to
    for f in range(floors):
        y = GF + f * FH
        g.box(((xa + xb) / 2, y + 0.12, -hz - 0.7), (xb - xa, 0.24, 1.4), "NB_Plaster", bottom=True)
        if detail < 2:
            g.ribbon([(xa, -hz - 1.38), (xb, -hz - 1.38)], y + 0.24, y + 1.2, "NB_BalconyGlass", double=True)
        if detail == 0:
            g.box(((xa + xb) / 2, y + 1.22, -hz - 1.38), (xb - xa, 0.06, 0.08), "NB_Bronze", top=True)


def parapet(g, hx, hz, y):
    g.ribbon(rect(-hx - 0.1, -hz - 0.1, hx + 0.1, hz + 0.1), y, y + 0.9, "NB_Plaster", double=True)
    g.quad((-hx, y + 0.05, hz), (hx, y + 0.05, hz), (hx, y + 0.05, -hz), (-hx, y + 0.05, -hz), "NB_DeckStone")


def roof_kit(g, hx, hz, y, detail):
    if detail < 2:
        g.box((hx * 0.45, y + 0.8, hz * 0.3), (2.2, 1.6, 1.6), "NB_Metal", top=True)     # AC units
        g.cylinder((-hx * 0.5, y, hz * 0.4), 1.0, 2.2, (10, 6)[detail], "NB_Plaster", top=True)  # water tank


def hip_roof(g, hx, hz, y, rise, over=0.8):
    x0, x1, z0, z1 = -hx - over, hx + over, -hz - over, hz + over
    r = min(hx, hz) + over
    ridge0, ridge1 = (x0 + r, y + rise, 0.0), (x1 - r, y + rise, 0.0)
    if hx < hz:
        ridge0, ridge1 = (0.0, y + rise, z0 + r), (0.0, y + rise, z1 - r)
        g.tri((x0, y, z0), ridge0, (x1, y, z0), "NB_RoofTile")
        g.quad((x1, y, z0), ridge0, ridge1, (x1, y, z1), "NB_RoofTile")
        g.tri((x1, y, z1), ridge1, (x0, y, z1), "NB_RoofTile")
        g.quad((x0, y, z1), ridge1, ridge0, (x0, y, z0), "NB_RoofTile")
    else:
        g.tri((x0, y, z0), ridge0, (x0, y, z1), "NB_RoofTile")
        g.quad((x0, y, z1), ridge0, ridge1, (x1, y, z1), "NB_RoofTile")
        g.tri((x1, y, z1), ridge1, (x1, y, z0), "NB_RoofTile")
        g.quad((x1, y, z0), ridge1, ridge0, (x0, y, z0), "NB_RoofTile")
    # soffit (underside) so the eave never shows a hole from below
    g.quad((x0, y - 0.02, z0), (x1, y - 0.02, z0), (x1, y - 0.02, z1), (x0, y - 0.02, z1), "NB_Plaster")


def midrise_a(detail):
    g = Geo()
    hx, hz, floors = 9.0, 6.0, 4
    shopfront(g, hx, hz, detail)
    walls(g, -hx, -hz, hx, hz, GF, GF + floors * FH, "NB_TownWall")
    balconies(g, hx, hz, floors, detail)
    top = GF + floors * FH
    parapet(g, hx, hz, top)
    roof_kit(g, hx, hz, top, detail)
    return g


def midrise_b(detail):
    g = Geo()
    hx, hz, floors = 7.5, 6.0, 3
    shopfront(g, hx, hz, detail)
    walls(g, -hx, -hz, hx, hz, GF, GF + floors * FH, "NB_TownWall")
    balconies(g, hx, hz, floors, detail, x_from=-hx, x_to=-hx + 6.0)
    balconies(g, hx, hz, floors, detail, x_from=hx - 6.0, x_to=hx)
    top = GF + floors * FH
    g.box((0, top + 0.15, 0), (2 * hx + 0.3, 0.3, 2 * hz + 0.3), "NB_Plaster", bottom=True)
    hip_roof(g, hx, hz, top + 0.3, 3.2)
    return g


def midrise_c(detail):
    g = Geo()
    hx, hz, floors = 10.5, 7.5, 5
    shopfront(g, hx, hz, detail)
    walls(g, -hx, -hz, hx, hz, GF, GF + floors * FH, "NB_TownWall")
    balconies(g, hx, hz, floors, detail, x_from=-hx + 1.5, x_to=hx - 1.5)
    top = GF + floors * FH
    # set-back penthouse (half depth) with a roof terrace in front
    phz = hz * 0.5
    g.quad((-hx, top + 0.05, hz), (hx, top + 0.05, hz), (hx, top + 0.05, -hz), (-hx, top + 0.05, -hz), "NB_DeckStone")
    walls(g, -hx + 3.0, 0.0, hx - 3.0, hz, top, top + FH, "NB_TownWall")
    g.box((0, top + FH + 0.15, hz * 0.5), (2 * hx - 5.6, 0.3, hz + 0.4), "NB_Plaster", bottom=True)
    g.ribbon([(-hx, -hz - 0.05), (hx, -hz - 0.05)], top, top + 1.1, "NB_BalconyGlass", double=True)
    g.ribbon([(hx, -hz), (hx, hz)], top, top + 1.0, "NB_Plaster", double=True)
    g.ribbon([(-hx, hz), (-hx, -hz)], top, top + 1.0, "NB_Plaster", double=True)
    if detail < 2:   # pergola over the terrace
        for k in range(8 if detail == 0 else 4):
            x = -hx + 2.0 + k * ((2 * hx - 4.0) / (7 if detail == 0 else 3))
            g.box((x, top + 2.9, -phz * 0.9), (0.22, 0.3, hz - 0.6), "NB_Teak", bottom=True)
        for x in (-hx + 1.0, hx - 1.0):
            g.box((x, top + 1.45, -hz + 0.6), (0.3, 2.9, 0.3), "NB_Teak", top=False)
    _ = phz
    return g


def sign_uv(row):
    """UV rect of atlas row 0..3 (row 0 = top of the PNG = v in [0.75, 1])."""
    v0, v1 = 1.0 - (row + 1) / 4.0, 1.0 - row / 4.0
    return v0, v1


def sign_board(g, x0, x1, y0, y1, z, row):
    v0, v1 = sign_uv(row)
    # faces -z: CCW from the front (seen from -z looking +z, x runs right-to-left... use box order)
    g.quad((x0, y0, z), (x0, y1, z), (x1, y1, z), (x1, y0, z), "NB_Signs",
           uvs=[(0.0, v0), (0.0, v1), (1.0, v1), (1.0, v0)])
    g.box(((x0 + x1) / 2, (y0 + y1) / 2, z + 0.08), (x1 - x0 + 0.1, y1 - y0 + 0.1, 0.14), "NB_Teak",
          top=True, bottom=True)


def kiosk(g, hx, hz, h, row, detail):
    # timber-and-plaster box, open glazed front, deep sloped canvas awning
    g.ribbon([(-hx, -hz), (hx, -hz)], 0.0, h - 0.9, "NB_Glass", v_tile=12.8)
    g.ribbon([(-hx, -hz), (hx, -hz)], h - 0.9, h, "NB_Plaster")
    g.ribbon([(hx, -hz), (hx, hz), (-hx, hz), (-hx, -hz)], 0.0, h, "NB_TownWall")
    g.box((0, h + 0.15, 0), (2 * hx + 1.2, 0.3, 2 * hz + 1.2), "NB_Teak", bottom=True,
          mats={"top": "NB_RoofTile"})
    # awning
    ya, yb, d = h - 0.6, h - 1.5, 3.2
    g.quad((-hx - 0.3, ya, -hz), (hx + 0.3, ya, -hz), (hx + 0.3, yb, -hz - d), (-hx - 0.3, yb, -hz - d),
           "NB_Canvas", double=True)
    if detail < 2:
        for x in (-hx - 0.1, hx + 0.1):
            g.box((x, yb / 2, -hz - d + 0.1), (0.12, yb, 0.12), "NB_Teak", top=False)
    sign_board(g, -hx * 0.8, hx * 0.8, h + 0.35, h + 0.35 + hx * 0.4, -hz - 0.62, row)


def tables(g, hz, n, detail):
    seg = (10, 6, 4)[detail]
    for k in range(n):
        x = -((n - 1) * 2.4) / 2 + k * 2.4
        z = -hz - 5.0
        g.cylinder((x, 0.72, z), 0.45, 0.05, seg, "NB_Teak", top=True, bottom=True)
        g.cylinder((x, 0.0, z), 0.05, 0.72, 4, "NB_Metal", top=False)
        if detail < 2:
            for dx in (-0.75, 0.75):
                g.box((x + dx, 0.23, z), (0.42, 0.46, 0.42), "NB_Teak", top=True)
                g.box((x + dx * 1.2, 0.7, z), (0.06, 0.5, 0.42), "NB_Teak")


def beach_cafe(detail):
    g = Geo()
    kiosk(g, 6.0, 4.5, 3.6, 0, detail)
    tables(g, 4.5, 4, detail)
    return g


def surf_shop(detail):
    g = Geo()
    kiosk(g, 5.0, 4.0, 3.6, 1, detail)
    if detail < 2:   # surfboard rack: boards leaning on a teak rail
        g.box((0, 1.4, -4.0 - 4.2), (7.0, 0.1, 0.1), "NB_Teak")
        for x in (-3.4, 3.4):
            g.box((x, 0.7, -8.2), (0.12, 1.4, 0.12), "NB_Teak", top=False)
        cols = ["NB_Paint", "NB_PaintTeal", "NB_Canvas", "NB_PaintTeal", "NB_Paint", "NB_Canvas"]
        for k, m in enumerate(cols[: (6 if detail == 0 else 3)]):
            x = -2.8 + k * 1.1
            g.box((x, 1.05, -8.05), (0.52, 2.1, 0.07), m, yaw=0.0, top=True, bottom=True)
    return g


def main():
    C.build_lods("Nagisa_MidriseA", midrise_a, "Nagisa_MidriseA.glb")
    C.build_lods("Nagisa_MidriseB", midrise_b, "Nagisa_MidriseB.glb")
    C.build_lods("Nagisa_MidriseC", midrise_c, "Nagisa_MidriseC.glb")
    C.build_lods("Nagisa_BeachCafe", beach_cafe, "Nagisa_BeachCafe.glb")
    C.build_lods("Nagisa_SurfShop", surf_shop, "Nagisa_SurfShop.glb")


main()

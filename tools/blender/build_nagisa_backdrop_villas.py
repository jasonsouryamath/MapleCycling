"""
NAGISA BAY NB8A - hillside backdrop: villas + terraced condos with infinity pools and lit windows.

Run: blender.exe -b -P tools/blender/build_nagisa_backdrop_villas.py [-- Name1 Name2 ...]

Placed by Assets/Editor/NagisaBayEnvironment.Backdrop.cs ([NagisaStage(96, "Backdrop")]) on the hills
beyond ~150 m from the ride road. Built on the pass-2 architecture kit (build_nagisa_arch: bevelled
solids, recessed windows into the emissive NB_Interior room atlas = lit windows, glass balustrades),
so every material slot is an existing Nagisa PBR slot remapped in C# (NB_Wall / NB_Accent colourways).

Front (sea / downhill face) = local -z; origin = finished ground at the house centre. A deep stone
retaining plinth (RETAIN_D) lets C# seat the lot on a hillside: the uphill back buries into the slope,
the downhill terrace stands on its retaining wall. LOD0 / LOD1 / LOD2 in one GLB each.
All dimensions are PROVISIONAL art numbers.
"""
import math
import os
import random
import sys

sys.path.append(os.path.dirname(__file__))
import build_nagisa_common as C          # noqa: E402
from build_nagisa_common import Geo      # noqa: E402
import build_nagisa_arch as A             # noqa: E402

RETAIN_D = 7.0        # retaining wall depth below the platform (m) - PROVISIONAL, covers ~0.3 slopes
POOL_D = 1.4


# ------------------------------------------------------------------ shared pieces
def deck_with_hole(g, x0, z0, x1, z1, hole, y, mat):
    """Horizontal deck x0..x1 / z0..z1 at y with a rectangular hole (hx0, hz0, hx1, hz1) cut out."""
    hx0, hz0, hx1, hz1 = hole
    up = (0, 1, 0)
    for (a0, b0, a1, b1) in ((x0, z0, x1, hz0), (x0, hz1, x1, z1), (x0, hz0, hx0, hz1), (hx1, hz0, x1, hz1)):
        if a1 - a0 > 0.01 and b1 - b0 > 0.01:
            A.qn(g, [(a0, y, b0), (a1, y, b0), (a1, y, b1), (a0, y, b1)], up, mat)


def infinity_pool(g, x0, z0, x1, z1, y, detail, loungers=4, rng=None):
    """Basin sunk into a deck at y; the -z edge is the vanishing edge that spills over the retaining
    wall into a catch trough. NB_PoolWater surface, NB_PoolTile basin, NB_DeckStone coping."""
    rng = rng or random.Random(3)
    d = POOL_D
    wy = y - 0.05
    A.qn(g, [(x0, y - d, z0), (x1, y - d, z0), (x1, y - d, z1), (x0, y - d, z1)], (0, 1, 0), "NB_PoolTile")
    A.qn(g, [(x0, y - d, z0), (x1, y - d, z0), (x1, y - 0.04, z0), (x0, y - 0.04, z0)], (0, 0, 1), "NB_PoolTile")
    A.qn(g, [(x0, y - d, z1), (x1, y - d, z1), (x1, y, z1), (x0, y, z1)], (0, 0, -1), "NB_PoolTile")
    A.qn(g, [(x0, y - d, z0), (x0, y - d, z1), (x0, y, z1), (x0, y, z0)], (1, 0, 0), "NB_PoolTile")
    A.qn(g, [(x1, y - d, z0), (x1, y - d, z1), (x1, y, z1), (x1, y, z0)], (-1, 0, 0), "NB_PoolTile")
    A.qn(g, [(x0, wy, z0), (x1, wy, z0), (x1, wy, z1), (x0, wy, z1)], (0, 1, 0), "NB_PoolWater")
    # vanishing edge: a glassy tile spill face down the front of the retaining wall + catch trough
    A.qn(g, [(x0, y - 0.9, z0 - 0.02), (x1, y - 0.9, z0 - 0.02), (x1, y - 0.04, z0 - 0.02),
             (x0, y - 0.04, z0 - 0.02)], (0, 0, -1), "NB_PoolTile")
    A.bbox(g, ((x0 + x1) / 2, y - 1.05, z0 - 0.45), (x1 - x0 + 0.4, 0.3, 0.9), "NB_Stone", 0.04, detail=detail)
    A.qn(g, [(x0, y - 0.92, z0 - 0.85), (x1, y - 0.92, z0 - 0.85), (x1, y - 0.92, z0 - 0.05),
             (x0, y - 0.92, z0 - 0.05)], (0, 1, 0), "NB_PoolWater")
    if detail < 2:   # coping on the three dry edges
        A.bbox(g, ((x0 + x1) / 2, y + 0.03, z1 + 0.25), (x1 - x0 + 1.0, 0.1, 0.5), "NB_DeckStone", 0.02, detail=detail)
        for x in (x0 - 0.25, x1 + 0.25):
            A.bbox(g, (x, y + 0.03, (z0 + z1) / 2), (0.5, 0.1, z1 - z0), "NB_DeckStone", 0.02, detail=detail)
    if detail == 0:
        for k in range(loungers):
            x = x0 + 0.9 + k * 1.6
            if x > x1 - 0.6:
                break
            lounger(g, (x, y, z1 + 1.6), rng)
        for k in range(max(1, loungers // 3)):
            A.cyl(g, (x0 + 1.7 + k * 4.8, y, z1 + 2.8), 0.05, 2.3, 8, "NB_Metal", top=False)
            A.cyl(g, (x0 + 1.7 + k * 4.8, y + 2.3, z1 + 2.8), 1.35, 0.35, 10, "NB_Canvas", top=True, r_top=0.05)


def lounger(g, c, rng):
    x, y, z = c
    A.bbox(g, (x, y + 0.3, z), (0.7, 0.12, 1.9), "NB_Teak", 0.02)
    A.bbox(g, (x, y + 0.4, z + 0.1), (0.62, 0.06, 1.6), "NB_Canvas", 0.02)
    A.bbox(g, (x, y + 0.62, z + 0.8), (0.62, 0.5, 0.12), "NB_Canvas", 0.02)
    for sx in (-0.28, 0.28):
        for sz in (-0.8, 0.8):
            A.bbox(g, (x + sx, y + 0.12, z + sz), (0.06, 0.24, 0.06), "NB_Teak", 0.0, detail=1)


def glass_rail(g, a, b, y, detail, h=1.05):
    """Frameless glass balustrade along a->b (x, z) with a slim top rail."""
    pa, pb = (a[0], y, a[1]), (b[0], y, b[1])
    g.quad(pa, pb, (b[0], y + h, b[1]), (a[0], y + h, a[1]), "NB_BalconyGlass", double=True)
    if detail < 2:
        A.seg_box(g, (a[0], y + h, a[1]), (b[0], y + h, b[1]), 0.06, 0.05, "NB_Trim", band=A.TRIM_BRONZE)


def platform(g, x0, z0, x1, z1, detail, top=0.0, mat="NB_Stone"):
    """Retaining-wall platform: stone walls from top down RETAIN_D, capped by coping."""
    poly = [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]
    A.prism_poly(g, poly, top - RETAIN_D, top, mat, top=False)
    if detail < 2:
        for a, b in A.poly_edges(poly):
            F = A.Frame(a, b)
            A._frame_box(g, F, F.P(F.L / 2, top - 0.05, 0.05), F.L + 0.1, 0.12, 0.1, A.TRIM_COPING)


def palms(g, pts, y, detail, rng):
    if detail >= 2:
        return
    for (x, z) in pts:
        h = 5.5 + rng.random() * 2.5
        A.cyl(g, (x, y, z), 0.2, h, (8, 5, 4)[detail], "NB_PalmBark", top=False, r_top=0.14)
        A.plant_cards(g, (x, y + h, z), 2.4, 2.2, "NB_PalmFrond", n=3 if detail == 0 else 2, yaw=rng.random() * 3)


def villa_block(g, cx, cz, hx, hz, y0, fh, detail, seed, glass_front=True, balcony=False, mat="NB_Wall"):
    poly = [(cx - hx, cz - hz), (cx + hx, cz - hz), (cx + hx, cz + hz), (cx - hx, cz + hz)]
    front = A.FacadeSpec(bay=3.0, full_height=glass_front, win_w=0.86 if glass_front else 0.55,
                         win_h=0.62, sill=0.8, fh=fh, reveal=0.3, mullions=2, transom=False,
                         frame_band=A.TRIM_BLACK, wall=mat, spandrel=mat, pier=mat, seed=seed)
    side = A.FacadeSpec(bay=3.2, win_w=0.4, win_h=0.5, sill=1.0, fh=fh, reveal=0.22, frame_band=A.TRIM_BLACK,
                        wall=mat, louvres=True, seed=seed + 1)
    for i, (a, b) in enumerate(A.poly_edges(poly)):
        A.facade(g, a, b, y0, 1, front if i == 0 else side, detail)
    if balcony:
        F = A.Frame(poly[0], poly[1])
        A.balcony(g, F, 0.2, F.L - 0.2, y0, 1.6, "glass", detail, random.Random(seed), furnish=detail == 0,
                  rail_band=A.TRIM_BRONZE, slab_mat="NB_DeckStone")
    return poly


# ================================================================== VILLA A - cantilever modernist
def villa_cliff(detail):
    """2-storey tropical-modern villa: glazed ground floor, upper floor cantilevered 2.5 m over the
    pool terrace, deep roof eyebrow, 12 m infinity pool on a stone platform, glass rail, palms."""
    g = Geo()
    rng = random.Random(801)
    hx, hz, fh = 8.0, 5.5, 3.4
    fz0 = -hz - 10.0                     # platform front (downhill)
    platform(g, -12.0, fz0, 12.0, hz + 1.0, detail)
    hole = (-10.0, fz0 + 0.6, 2.0, fz0 + 5.0)
    deck_with_hole(g, -12.0, fz0, 12.0, hz + 1.0, hole, 0.0, "NB_DeckStone")
    infinity_pool(g, *hole, 0.0, detail, loungers=6, rng=rng)
    p0 = villa_block(g, -1.0, 0.0, hx, hz, 0.02, fh, detail, 811)
    A.floor_slab_edge(g, p0, fh + 0.02, detail, proj=0.2)
    p1 = villa_block(g, 1.5, -1.25, hx - 0.5, hz + 1.25, fh + 0.05, fh, detail, 821, balcony=False, mat="NB_Accent")
    ytop = 2 * fh + 0.05
    A.roof_deck_edge(g, p1, ytop, detail, h=0.55, proj=0.9)
    A.parapet(g, p1, ytop + 0.55, 0.5, detail, roof_mat="NB_Concrete")
    # cantilever soffit under the overhang
    A.qn(g, [(-6.0, fh + 0.05, -hz - 2.5), (9.5, fh + 0.05, -hz - 2.5), (9.5, fh + 0.05, -hz),
             (-6.0, fh + 0.05, -hz)], (0, -1, 0), "NB_Teak")
    glass_rail(g, (2.3, fz0 + 0.1), (12.0, fz0 + 0.1), 0.0, detail)
    glass_rail(g, (12.0, fz0 + 0.1), (12.0, hz + 1.0), 0.0, detail)
    if detail < 2:
        A.pergola(g, 3.0, fz0 + 1.2, 11.0, fz0 + 5.0, 0.0, 2.7, detail)
        A.solar_array(g, (3.0, ytop + 0.55, 1.0), 4, 2, 0.0, detail)
    palms(g, [(-11.2, hz + 0.2), (11.2, -hz - 3.0), (-11.3, fz0 + 6.0)], 0.0, detail, rng)
    return g


# ================================================================== VILLA B - tiled courtyard villa
def villa_tile(detail):
    """Single-storey L-plan resort villa: red-tile hip roofs with deep eaves, teak engawa, pool
    courtyard with a vanishing edge toward the sea, coral-stone garden walls."""
    g = Geo()
    rng = random.Random(802)
    fh = 3.4
    fz0 = -16.0
    platform(g, -14.0, fz0, 14.0, 9.0, detail)
    hole = (-12.5, fz0 + 0.6, -1.0, fz0 + 4.6)
    deck_with_hole(g, -14.0, fz0, 14.0, 9.0, hole, 0.0, "NB_DeckStone")
    infinity_pool(g, *hole, 0.0, detail, loungers=5, rng=rng)
    main = villa_block(g, -3.0, 3.0, 9.0, 4.5, 0.02, fh, detail, 831)
    A.hip_roof(g, -13.4, -2.9, 7.4, 8.9, fh + 0.05, 3.2, detail=detail)
    villa_block(g, 9.0, -2.0, 3.8, 6.5, 0.02, fh, detail, 841, glass_front=True, mat="NB_Accent")
    A.hip_roof(g, 4.3, -9.9, 13.7, 5.9, fh + 0.05, 2.8, detail=detail)
    A.bbox(g, (-3.0, 0.12, -2.4), (18.0, 0.2, 2.2), "NB_Teak", 0.02, detail=detail)          # engawa
    if detail < 2:
        for x in (-11.6, -6.0, -0.4, 5.2):
            A.bbox(g, (x, fh / 2, -3.4), (0.18, fh, 0.18), "NB_Teak", 0.02, detail=detail)
    glass_rail(g, (0.0, fz0 + 0.1), (14.0, fz0 + 0.1), 0.0, detail)
    palms(g, [(-13.2, 8.0), (2.5, fz0 + 2.0), (13.0, 8.0), (-13.4, -6.0)], 0.0, detail, rng)
    return g


# ================================================================== VILLA C - stacked cubist villa
def villa_stack(detail):
    """3 offset stacked volumes with roof terraces and glass rails; lap pool along the platform."""
    g = Geo()
    rng = random.Random(803)
    fh = 3.3
    fz0 = -15.0
    platform(g, -11.0, fz0, 11.0, 8.0, detail)
    hole = (-9.5, fz0 + 0.6, 7.5, fz0 + 3.6)
    deck_with_hole(g, -11.0, fz0, 11.0, 8.0, hole, 0.0, "NB_DeckStone")
    infinity_pool(g, *hole, 0.0, detail, loungers=7, rng=rng)
    b0 = villa_block(g, -1.0, 1.5, 8.0, 5.5, 0.02, fh, detail, 851)
    A.parapet(g, b0, fh + 0.02, 0.1, detail, roof_mat="NB_Teak")
    b1 = villa_block(g, 2.0, 0.5, 6.0, 5.0, fh + 0.12, fh, detail, 861, balcony=False, mat="NB_Accent")
    A.floor_slab_edge(g, b1, fh + 0.12, detail, proj=0.3)
    glass_rail(g, (-9.0, -4.0), (-4.0, -4.0), fh + 0.12, detail)
    b2 = villa_block(g, -1.5, 2.0, 4.0, 3.5, 2 * fh + 0.2, fh, detail, 871)
    A.roof_deck_edge(g, b2, 3 * fh + 0.2, detail, h=0.45, proj=0.6)
    A.parapet(g, b2, 3 * fh + 0.65, 0.4, detail, roof_mat="NB_Concrete")
    glass_rail(g, (2.5, -4.5), (8.0, -4.5), 2 * fh + 0.2, detail)
    if detail < 2:
        A.pergola(g, 3.0, -4.0, 7.6, 2.0, 2 * fh + 0.2, 2.5, detail)
    palms(g, [(-10.3, 7.2), (10.3, 7.2), (10.2, fz0 + 5.5)], 0.0, detail, rng)
    return g


# ================================================================== TERRACED CONDOS
def terraced(detail, width, tiers, step, depth_back, fh, pools, seed, crown=True):
    """Stepped hillside condo: tier k occupies y k*fh .. (k+1)*fh from its front line
    z_k = -Z/2 + k*step to the back; tier k's exposed roof strip is the terrace of tier k+1's flats
    (glass rails, privacy fins, planters). Shared infinity pool terrace in front of tier 0."""
    g = Geo()
    rng = random.Random(seed)
    hx = width / 2
    Z = tiers * step + depth_back
    zf0 = -Z / 2
    front_deck = 12.0
    platform(g, -hx - 2.0, zf0 - front_deck, hx + 2.0, Z / 2, detail)
    # pool terrace
    holes = []
    pw = (width - 4.0 * (pools + 1)) / pools
    for p in range(pools):
        x0 = -hx + 4.0 + p * (pw + 4.0)
        holes.append((x0, zf0 - front_deck + 0.6, x0 + pw, zf0 - front_deck + 5.2))
    # deck split around each hole: build per-hole strips
    xs = [-hx - 2.0] + [c for h in holes for c in (h[0], h[2])] + [hx + 2.0]
    zA, zB = zf0 - front_deck, zf0
    for i in range(0, len(xs), 2):
        A.qn(g, [(xs[i], 0.0, zA), (xs[i + 1], 0.0, zA), (xs[i + 1], 0.0, zB), (xs[i], 0.0, zB)], (0, 1, 0),
             "NB_DeckStone")
    for h in holes:
        deck_with_hole(g, h[0], zA, h[2], zB, h, 0.0, "NB_DeckStone")
        infinity_pool(g, *h, 0.0, detail, loungers=int(pw / 1.6), rng=rng)
    for k in range(tiers):
        zk = zf0 + k * step
        y0 = k * fh + 0.02
        poly = [(-hx, zk), (hx, zk), (hx, Z / 2), (-hx, Z / 2)]
        front = A.FacadeSpec(bay=3.4, full_height=True, win_w=0.84, fh=fh, reveal=0.35, mullions=2,
                             frame_band=A.TRIM_BRONZE, wall="NB_Wall", spandrel="NB_Wall", pier="NB_Accent",
                             seed=seed + k)
        side = A.FacadeSpec(bay=3.4, win_w=0.45, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, frame_band=A.TRIM_WHITE,
                            wall="NB_Wall", louvres=True, seed=seed + 20 + k)
        for i, (a, b) in enumerate(A.poly_edges(poly)):
            if i == 2:
                continue          # buried in the hillside
            A.facade(g, a, b, y0, 1, front if i == 0 else side, detail)
        A.floor_slab_edge(g, poly, y0 + fh, detail, proj=0.35)
        # terrace on this tier's exposed roof (the next tier's front yard)
        yt = y0 + fh + 0.02
        ztop = zk + step if k < tiers - 1 else Z / 2
        A.qn(g, [(-hx, yt, zk), (hx, yt, zk), (hx, yt, ztop), (-hx, yt, ztop)], (0, 1, 0),
             "NB_Teak" if k < tiers - 1 else "NB_Concrete")
        glass_rail(g, (-hx + 0.1, zk + 0.15), (hx - 0.1, zk + 0.15), yt, detail)
        if k < tiers - 1 and detail < 2:
            nfl = int(width / 6.8)
            for f in range(1, nfl):
                x = -hx + f * width / nfl
                A.bbox(g, (x, yt + 1.0, zk + step * 0.45), (0.2, 2.0, step * 0.8), "NB_Accent", 0.03, detail=detail)
            if detail == 0:
                for f in range(nfl):
                    x = -hx + (f + 0.5) * width / nfl
                    A.roof_planter(g, x - 1.2, zk + 0.5, x + 1.2, zk + 1.2, yt, detail)
                    A.cyl(g, (x + 1.8, yt, zk + 3.0), 1.1, 0.35, 10, "NB_Canvas", top=True, r_top=0.05) \
                        if rng.random() < 0.5 else None
    if crown and detail < 2:
        ytop = tiers * fh + 0.04
        A.solar_array(g, (-hx * 0.4, ytop, Z / 2 - step * 0.6), 6, 2, 0.0, detail)
        A.water_tank(g, (hx - 3.0, ytop, Z / 2 - 3.0), detail, r=0.8, h=1.4)
    palms(g, [(-hx - 1.2, zA + 1.0), (hx + 1.2, zA + 1.0), (0.0, zA + 7.5)], 0.0, detail, rng)
    return g


def condo_terrace_a(detail):
    return terraced(detail, width=36.0, tiers=5, step=7.0, depth_back=9.0, fh=3.3, pools=1, seed=901)


def condo_terrace_b(detail):
    return terraced(detail, width=54.0, tiers=4, step=8.0, depth_back=10.0, fh=3.4, pools=2, seed=911)


BUILDERS = {
    "Nagisa_BD_VillaCliff": villa_cliff,
    "Nagisa_BD_VillaTile": villa_tile,
    "Nagisa_BD_VillaStack": villa_stack,
    "Nagisa_BD_TerraceCondoA": condo_terrace_a,
    "Nagisa_BD_TerraceCondoB": condo_terrace_b,
}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(BUILDERS)
    for name in names:
        C.build_lods(name, BUILDERS[name], name + ".glb")


if __name__ == "__main__":
    main()

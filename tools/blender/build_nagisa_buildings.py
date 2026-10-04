"""
NAGISA BAY (B5) PASS 2 - individually designed buildings for the SW-shore town.

Run: blender.exe -b -P tools/blender/build_nagisa_buildings.py [-- Name1 Name2 ...]

Each type is its own design (not a scaled box): distinct plan, massing, facade rhythm, balcony
type, roof kit and ground floor. All use build_nagisa_arch (recessed windows with frames and
mullions into the NB_Interior room atlas, real balcony slabs + balustrades, shopfronts, roof kit).

Front (street face) = local -z; origin = ground centre; y = 0 is finished ground at the front
door. A below-grade plinth (PLINTH_D) lets C# sit a building on gently sloping ground without
floating. Colourways are C# material remaps of NB_Wall / NB_Accent (see NagisaBayEnvironment.Town2).

Writes Models/Nagisa_B_Footprints.json (half extents + lot + anchors per type) for C# placement.
PROVISIONAL art numbers throughout.
"""
import json
import math
import os
import random
import sys

sys.path.append(os.path.dirname(__file__))
import build_nagisa_common as C          # noqa: E402
from build_nagisa_common import Geo, add  # noqa: E402
import build_nagisa_arch as A             # noqa: E402

PLINTH_D = 1.6
FOOT = {}


def lot_rec(name, hx, hz, h, lot=None, **kw):
    FOOT[name] = dict(hx=hx, hz=hz, h=h, lot=lot or [hx, hz], **kw)


# ================================================================== block composer
def block(g, hx, hz, y0, floors, fh, specs, detail, balconies=None, rng=None, poly=None):
    """Rectangular storeys: specs = [front, right, back, left] FacadeSpec or None (blank wall).
    balconies = dict(face index -> dict(kind, depth, every, start, cont, furnish, ac, side_walls))."""
    rng = rng or random.Random(7)
    poly = poly or A.footprint_rect(hx, hz)
    edges = A.poly_edges(poly)
    frames = []
    for i, (a, b) in enumerate(edges):
        sp = specs[i]
        if sp is None:
            F = A.Frame(a, b)
            A.qn(g, [F.P(0, y0), F.P(F.L, y0), F.P(F.L, y0 + floors * fh), F.P(0, y0 + floors * fh)], F.n, "NB_Wall")
            frames.append(F)
            continue
        frames.append(A.facade(g, a, b, y0, floors, sp, detail))
    if balconies:
        for fi, bc in balconies.items():
            F = frames[fi]
            sp = specs[fi]
            nb = max(1, int(round(F.L / sp.bay)))
            bw = F.L / nb
            for f in range(bc.get("start", 0), floors):
                y = y0 + f * fh
                if bc.get("cont", False):
                    A.balcony(g, F, 0.05, F.L - 0.05, y, bc["depth"], bc["kind"], detail, rng,
                              furnish=bc.get("furnish", True), ac=bc.get("ac", False),
                              side_walls=bc.get("side_walls", False), rail_band=bc.get("band", A.TRIM_WHITE),
                              slab_mat=bc.get("slab", "NB_Plaster"), solid_mat=bc.get("solid", "NB_Accent"))
                    continue
                for k in range(nb):
                    pat = bc.get("pattern")
                    if pat is not None and not pat(f, k):
                        continue
                    grp = bc.get("group", 1)
                    if k % grp:
                        continue
                    s0 = k * bw + 0.15
                    s1 = min(F.L, (k + grp) * bw) - 0.15
                    A.balcony(g, F, s0, s1, y, bc["depth"], bc["kind"], detail, rng,
                              furnish=bc.get("furnish", True), ac=bc.get("ac", False),
                              side_walls=bc.get("side_walls", False), rail_band=bc.get("band", A.TRIM_WHITE),
                              slab_mat=bc.get("slab", "NB_Plaster"), solid_mat=bc.get("solid", "NB_Accent"),
                              divider=bc.get("divider", False))
    return frames


def lobby(g, hx, hz, h, detail, sign=None, canopy=True, mat="NB_Accent", glass_cell=0, faces=(0,)):
    """Double-height lobby: full-height glazing on the listed faces, stone piers, entrance canopy."""
    poly = A.footprint_rect(hx, hz)
    edges = A.poly_edges(poly)
    for i, (a, b) in enumerate(edges):
        if i in faces:
            sp = A.FacadeSpec(bay=3.6, win_w=0.86, full_height=True, fh=h, reveal=0.4, mullions=2, transom=True,
                              frame_band=A.TRIM_BRONZE, wall=mat, spandrel=mat, pier=mat, seed=11 + i)
            A.facade(g, a, b, 0.0, 1, sp, detail)
        else:
            sp = A.FacadeSpec(bay=3.6, win_w=0.5, win_h=0.55, sill=1.2, fh=h, reveal=0.3, wall=mat, spandrel=mat,
                              pier=mat, seed=21 + i)
            A.facade(g, a, b, 0.0, 1, sp, detail)
    A.floor_slab_edge(g, poly, h - 0.1, detail, proj=0.25, mat=mat)
    if canopy:
        cw = min(2 * hx - 2, 14.0)
        A.bbox(g, (0, h - 0.6, -hz - 3.0), (cw, 0.45, 6.2), "NB_Accent", 0.05, detail=detail)
        A.qn(g, [(-cw / 2 + 0.2, h - 0.84, -hz - 5.9), (cw / 2 - 0.2, h - 0.84, -hz - 5.9),
                 (cw / 2 - 0.2, h - 0.84, -hz - 0.2), (-cw / 2 + 0.2, h - 0.84, -hz - 0.2)], (0, -1, 0), "NB_Teak")
        for x in (-cw / 2 + 0.6, cw / 2 - 0.6):
            A.cyl(g, (x, 0, -hz - 5.5), 0.16, h - 0.8, (12, 8, 4)[detail], "NB_Trim" if False else "NB_Bronze", top=False)
        if sign is not None:
            F = A.Frame((-cw / 2, -hz - 6.1), (cw / 2, -hz - 6.1))
            sw = min(cw * 0.7, 7.0)
            A.sign_board(g, F, cw / 2 - sw / 2, cw / 2 + sw / 2, h - 0.35, h + 0.55, 0.02, sign, detail)


def roof_kit(g, hx, hz, y, detail, rng, tanks=2, solar=(0, 0), ac=3, overrun_at=(0.0, 0.0), pergola=None,
             garden=None, parapet_h=1.1, mat="NB_Wall"):
    poly = A.footprint_rect(hx, hz)
    A.parapet(g, poly, y, parapet_h, detail, mat=mat)
    if detail >= 2:
        A.bbox(g, (overrun_at[0], y + 1.5, overrun_at[1]), (4.0, 3.0, 5.0), mat, 0, detail=2)
        return
    A.overrun(g, (overrun_at[0], y, overrun_at[1]), (4.0, 3.2, 5.0), detail, mat)
    for k in range(tanks):
        A.water_tank(g, (overrun_at[0] - 1.2 + k * 2.4, y + 3.3, overrun_at[1] + 0.8), detail, r=0.75, h=1.3)
    if solar[0]:
        A.solar_array(g, (-hx * 0.5, y, hz * 0.35), solar[0], solar[1], 0.0, detail)
    if ac:
        A.ac_plant(g, (hx * 0.55, y, -hz * 0.4), 0.0, ac, detail)
    if pergola:
        x0, z0, x1, z1 = pergola
        A.pergola(g, x0, z0, x1, z1, y, 2.7, detail)
        A.qn(g, [(x0 - 0.5, y + 0.04, z0 - 0.5), (x1 + 0.5, y + 0.04, z0 - 0.5), (x1 + 0.5, y + 0.04, z1 + 0.5),
                 (x0 - 0.5, y + 0.04, z1 + 0.5)], (0, 1, 0), "NB_Teak")
        if detail == 0:
            for k in range(3):
                x = x0 + (x1 - x0) * (k + 0.5) / 3
                A.table(g, (x, y + 0.04, (z0 + z1) / 2), 0.45, 0.74, 10)
                A.chair(g, (x - 0.8, y + 0.04, (z0 + z1) / 2), math.pi / 2)
                A.chair(g, (x + 0.8, y + 0.04, (z0 + z1) / 2), -math.pi / 2)
    if garden:
        x0, z0, x1, z1 = garden
        A.roof_planter(g, x0, z0, x1, z1, y, detail)


# ================================================================== CONDO 1 - "Palm Court" 18 storeys
def condo1(detail):
    """Slender 18-storey vacation-rental tower: 26 x 20 m, wrap-around glass-rail corner balconies
    on the sea face, projecting slab edges every floor, vertical accent piers, 3-storey setback crown
    with roof pool pergola. Street lobby with bronze canopy and sign."""
    g = Geo()
    rng = random.Random(101)
    hx, hz = 13.0, 10.0
    fh = 3.1
    lobby_h = 5.0
    floors = 15
    A.plinth(g, A.footprint_rect(hx + 0.3, hz + 0.3), 0.0, PLINTH_D, detail)
    lobby(g, hx, hz, lobby_h, detail, sign=A.SIGN_PALM_COURT)
    y0 = lobby_h
    face = A.FacadeSpec(bay=3.25, full_height=True, win_w=0.82, fh=fh, reveal=0.25, mullions=1, transom=True,
                        frame_band=A.TRIM_WHITE, wall="NB_Wall", spandrel="NB_Wall", pier="NB_Accent", seed=3)
    side = A.FacadeSpec(bay=3.3, win_w=0.55, win_h=0.52, sill=0.95, fh=fh, reveal=0.22, mullions=1,
                        frame_band=A.TRIM_WHITE, wall="NB_Wall", seed=4, louvres=True)
    back = A.FacadeSpec(bay=3.25, win_w=0.45, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, mullions=0,
                        frame_band=A.TRIM_WHITE, wall="NB_Wall", seed=5)
    block(g, hx, hz, y0, floors, fh, [face, side, back, side], detail,
          balconies={0: dict(kind="glass", depth=2.2, group=2, furnish=True, ac=False, divider=True),
                     2: dict(kind="bars", depth=1.1, group=2, furnish=False, ac=True,
                             pattern=lambda f, k: (f + k // 2) % 2 == 0)}, rng=rng)
    poly = A.footprint_rect(hx, hz)
    for f in range(floors + 1):
        A.floor_slab_edge(g, poly, y0 + f * fh + 0.05, detail, proj=0.14)
    # vertical accent piers on the front corners (full height)
    ytop = y0 + floors * fh
    for sx in (-1, 1):
        A.bbox(g, (sx * (hx - 0.4), (lobby_h + ytop) / 2, -hz - 0.35), (0.9, ytop - lobby_h, 0.8), "NB_Accent", 0.05,
               detail=detail)
    # setback crown: 3 storeys, inset 3 m, with a big terrace + pergola at the setback
    chx, chz = hx - 3.0, hz - 2.5
    A.parapet(g, poly, ytop, 1.1, detail, mat="NB_Wall", roof_mat="NB_DeckStone")
    cf = 3
    cface = A.FacadeSpec(bay=3.4, full_height=True, win_w=0.9, fh=fh, reveal=0.3, mullions=2,
                         frame_band=A.TRIM_BRONZE, wall="NB_Accent", spandrel="NB_Accent", pier="NB_Accent", seed=6)
    cside = A.FacadeSpec(bay=3.4, win_w=0.6, win_h=0.55, sill=0.9, fh=fh, reveal=0.25, mullions=1,
                         frame_band=A.TRIM_BRONZE, wall="NB_Accent", seed=7)
    block(g, chx, chz, ytop, cf, fh, [cface, cside, cside, cside], detail, poly=[(p[0], p[1] + 1.5) for p in A.footprint_rect(chx, chz)])
    for f in range(cf + 1):
        A.floor_slab_edge(g, [(p[0], p[1] + 1.5) for p in A.footprint_rect(chx, chz)], ytop + f * fh + 0.05, detail, proj=0.3)
    A.pergola(g, -hx + 1.0, -hz + 0.6, hx - 1.0, -chz + 1.5 - 0.3, ytop, 2.8, detail)
    if detail == 0:
        for k in range(4):
            x = -hx + 3 + k * (2 * hx - 6) / 3
            A.bbox(g, (x, ytop + 0.3, -hz + 2.0), (1.8, 0.35, 0.7), "NB_Canvas", 0.05)
            A.potted_plant(g, (x + 1.4, ytop + 0.02, -hz + 1.0), detail, big=True)
    ycrown = ytop + cf * fh
    roof_kit(g, chx, chz, ycrown, detail, rng, tanks=2, solar=(6, 2), ac=3, overrun_at=(chx * 0.4, 2.0),
             mat="NB_Accent")
    lot_rec("Nagisa_B_Condo1", hx, hz, ycrown + 4.0, lot=[hx + 4, hz + 7], door=[0, -hz - 6])
    return g


# ================================================================== CONDO 2 - "Sunset Terraces" 12 storeys
def condo2(detail):
    """Wide 12-storey slab 40 x 14 m: staggered checkerboard balconies with solid painted parapets +
    bars, deep sun-shade frame (egg-crate) on the end bays, AC units, rooftop water tanks + PV,
    ground-floor shops (pharmacy + poke bar) under a continuous awning."""
    g = Geo()
    rng = random.Random(202)
    hx, hz = 20.0, 7.0
    fh = 3.0
    gf = 4.2
    floors = 11
    A.plinth(g, A.footprint_rect(hx + 0.3, hz + 0.3), 0.0, PLINTH_D, detail)
    # ground floor: two shops + lobby door in the middle
    A.shopfront(g, (-hx, -hz), (-3.0, -hz), 0.0, gf, detail, rng, sign=A.SIGN_CORAL_PHARMACY, awning_band=6,
                shop_cell=5, bays=5, door_bay=1)
    A.shopfront(g, (3.0, -hz), (hx, -hz), 0.0, gf, detail, rng, sign=A.SIGN_ISLAND_POKE, awning_band=0,
                shop_cell=1, bays=5, door_bay=3)
    lob = A.FacadeSpec(bay=3.0, full_height=True, win_w=0.9, fh=gf, reveal=0.5, mullions=2, frame_band=A.TRIM_BRONZE,
                       wall="NB_Accent", spandrel="NB_Accent", pier="NB_Accent", seed=9)
    A.facade(g, (-3.0, -hz), (3.0, -hz), 0.0, 1, lob, detail)
    sideg = A.FacadeSpec(bay=3.5, win_w=0.5, win_h=0.5, sill=1.4, fh=gf, reveal=0.2, wall="NB_Accent", seed=10)
    for a, b in (((hx, -hz), (hx, hz)), ((hx, hz), (-hx, hz)), ((-hx, hz), (-hx, -hz))):
        A.facade(g, a, b, 0.0, 1, sideg, detail)
    A.floor_slab_edge(g, A.footprint_rect(hx, hz), gf, detail, proj=0.3)
    face = A.FacadeSpec(bay=3.33, full_height=True, win_w=0.72, fh=fh, reveal=0.2, mullions=1, transom=True,
                        frame_band=A.TRIM_BLACK, seed=12)
    back = A.FacadeSpec(bay=3.33, win_w=0.5, win_h=0.55, sill=0.95, fh=fh, reveal=0.2, frame_band=A.TRIM_WHITE,
                        seed=13, shutters=None)
    side = A.FacadeSpec(bay=3.5, win_w=0.4, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, frame_band=A.TRIM_WHITE, seed=14)
    frames = block(g, hx, hz, gf, floors, fh, [face, side, back, side], detail,
                   balconies={0: dict(kind="rail", depth=1.6, group=1, furnish=True, ac=True, band=A.TRIM_BLACK,
                                      solid="NB_Accent", pattern=lambda f, k: (f + k) % 2 == 0),
                              2: dict(kind="bars", depth=0.9, group=3, furnish=False, ac=True)}, rng=rng)
    poly = A.footprint_rect(hx, hz)
    ytop = gf + floors * fh
    for f in range(1, floors + 1):
        A.floor_slab_edge(g, poly, gf + f * fh, detail, proj=0.1)
    # egg-crate sun frame over the two end bays of the sea/street face
    if detail < 2:
        for sx in (-1, 1):
            xa = sx * (hx - 3.33)
            for x in (xa, sx * hx):
                A.bbox(g, (x, (gf + ytop) / 2, -hz - 1.0), (0.3, ytop - gf, 2.0), "NB_Accent", 0.03, detail=detail)
            for f in range(floors + 1):
                A.bbox(g, ((xa + sx * hx) / 2, gf + f * fh + 0.1, -hz - 1.0), (3.33, 0.2, 2.0), "NB_Accent", 0.02,
                       detail=detail)
    roof_kit(g, hx, hz, ytop, detail, rng, tanks=3, solar=(10, 2), ac=4, overrun_at=(-6.0, 2.0),
             garden=(4.0, -hz + 1.0, hx - 2.0, -hz + 2.2))
    lot_rec("Nagisa_B_Condo2", hx, hz, ytop + 4.0, lot=[hx + 3, hz + 6])
    return g


# ================================================================== RESORT HOTEL 2 - "Hotel Shirahama"
def resort2(detail):
    """14-storey resort hotel, 64 x 18 m, three-part massing: 14-storey centre stepping to 10-storey
    wings, continuous deep sea-face balconies with privacy fins + glass rails, projecting white
    slab edges, road-face corridor windows with louvres, rooftop sky lounge pergola and crown sign.
    Front (-z) is the SEA face; the lobby/porte is on +z (road)."""
    g = Geo()
    rng = random.Random(303)
    fh = 3.2
    base = 6.0
    parts = [(-22.0, -10.0, 9), (-10.0, 10.0, 13), (10.0, 22.0, 9)]     # x0, x1, floors above base
    hz = 9.0
    X0, X1 = -32.0, 32.0
    # podium base (restaurant level): full-height glazing to the sea, stone
    A.plinth(g, A.footprint_rect(33.0, hz + 0.3), 0.0, PLINTH_D, detail)
    lob = A.FacadeSpec(bay=4.0, full_height=True, win_w=0.88, fh=base, reveal=0.5, mullions=2, transom=True,
                       frame_band=A.TRIM_BRONZE, wall="NB_Stone", spandrel="NB_Stone", pier="NB_Stone", seed=31)
    blank = A.FacadeSpec(bay=4.0, win_w=0.6, win_h=0.5, sill=1.8, fh=base, reveal=0.3, wall="NB_Stone",
                         spandrel="NB_Stone", pier="NB_Stone", seed=32)
    poly = [(X0, -hz), (X1, -hz), (X1, hz), (X0, hz)]
    for i, (a, b) in enumerate(A.poly_edges(poly)):
        A.facade(g, a, b, 0.0, 1, lob if i in (0, 2) else blank, detail)
    A.floor_slab_edge(g, poly, base, detail, proj=0.6, mat="NB_Stone")
    # wing ends of the podium roof (terraces) around the tower parts
    A.parapet(g, poly, base, 1.1, detail, mat="NB_Stone", roof_mat="NB_DeckStone")
    face = A.FacadeSpec(bay=4.0, full_height=True, win_w=0.84, fh=fh, reveal=0.28, mullions=1, transom=True,
                        frame_band=A.TRIM_WHITE, seed=33)
    back = A.FacadeSpec(bay=4.0, win_w=0.4, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, louvres=True, seed=34)
    side = A.FacadeSpec(bay=3.0, win_w=0.55, win_h=0.55, sill=0.9, fh=fh, reveal=0.25, mullions=1, seed=35)
    for (x0, x1, fl) in parts:
        pp = [(x0, -hz + 0.5), (x1, -hz + 0.5), (x1, hz - 1.0), (x0, hz - 1.0)]
        edges = A.poly_edges(pp)
        specs = [face, side, back, side]
        frames = []
        for i, (a, b) in enumerate(edges):
            frames.append(A.facade(g, a, b, base, fl, specs[i], detail))
        F = frames[0]
        nb = int(round(F.L / 4.0))
        bw = F.L / nb
        for f in range(fl):
            y = base + f * fh
            for k in range(nb):
                A.balcony(g, F, k * bw + 0.05, (k + 1) * bw - 0.05, y, 2.6, "glass", detail, rng, furnish=True,
                          plants=True, side_walls=True, rail_band=A.TRIM_WHITE, solid_mat="NB_Accent")
            A.floor_slab_edge(g, pp, y + fh, detail, proj=0.12)
        ytop = base + fl * fh
        A.roof_deck_edge(g, pp, ytop, detail, mat="NB_Accent", h=0.6, proj=0.5)
        if fl > 10:
            # sky lounge: glass pavilion + pergola + crown sign facing the sea
            A.parapet(g, pp, ytop + 0.6, 0.5, detail, roof_mat="NB_DeckStone")
            sk = A.FacadeSpec(bay=3.3, full_height=True, win_w=0.92, fh=4.0, reveal=0.1, mullions=1,
                              frame_band=A.TRIM_BRONZE, wall="NB_Accent", spandrel="NB_Accent", pier="NB_Accent", seed=36)
            skp = [(x0 + 3, -hz + 5.0), (x1 - 3, -hz + 5.0), (x1 - 3, hz - 3.0), (x0 + 3, hz - 3.0)]
            for i, (a, b) in enumerate(A.poly_edges(skp)):
                A.facade(g, a, b, ytop + 0.6, 1, sk, detail)
            A.roof_deck_edge(g, skp, ytop + 4.6, detail, mat="NB_Accent", h=0.5, proj=0.9)
            A.pergola(g, x0 + 1.0, -hz + 1.2, x1 - 1.0, -hz + 4.6, ytop + 0.6, 3.0, detail)
            Fs = A.Frame((x0 + 4, -hz + 0.3), (x1 - 4, -hz + 0.3))
            A.sign_board(g, Fs, 0.5, Fs.L - 0.5, ytop + 5.3, ytop + 7.6, 0.0, A.SIGN_HOTEL_SHIRAHAMA, detail)
            if detail < 2:
                for x in (x0 + 5, x1 - 5):
                    A.bbox(g, (x, ytop + 6.4, -hz + 0.45), (0.2, 3.0, 0.2), "NB_Trim", 0.0, detail=1)
        else:
            A.parapet(g, pp, ytop + 0.6, 0.5, detail)
            if detail < 2:
                A.overrun(g, ((x0 + x1) / 2, ytop + 0.6, 3.0), (4.0, 3.0, 4.5), detail)
                A.solar_array(g, ((x0 + x1) / 2, ytop + 0.6, -3.0), 6, 2, 0.0, detail)
                A.water_tank(g, ((x0 + x1) / 2 + 3.5, ytop + 0.6, 4.0), detail)
    # road-side porte-cochere (+z) with sign + planters
    if detail < 2:
        A.bbox(g, (0, 5.2, hz + 5.0), (16.0, 0.7, 10.0), "NB_Accent", 0.06, detail=detail)
        A.qn(g, [(-7.8, 4.84, hz + 0.2), (7.8, 4.84, hz + 0.2), (7.8, 4.84, hz + 9.8), (-7.8, 4.84, hz + 9.8)],
             (0, -1, 0), "NB_Teak")
        for x in (-6.5, 6.5):
            A.cyl(g, (x, 0, hz + 8.8), 0.35, 4.9, (14, 8, 4)[detail], "NB_Stone", top=False)
        Fp = A.Frame((7.0, hz + 10.02), (-7.0, hz + 10.02))
        A.sign_board(g, Fp, 1.0, 13.0, 4.95, 5.5, 0.02, A.SIGN_HOTEL_SHIRAHAMA, detail)
    lot_rec("Nagisa_B_ResortHotel2", 33.0, hz, base + 13 * fh + 8, lot=[36.0, hz + 12], sea_front=True)
    return g


# ================================================================== BOUTIQUE 1 - "Villa Hinata" (Okinawan)
def boutique1(detail):
    """6-storey boutique hotel, 24 x 14 m: Ryukyu-modern. White stucco with red-tile hip roof +
    ridge caps, deep eaves, teak louvred shutters, black-steel bar balconies with planter boxes,
    stone base with arched-feel deep entrance, blade sign."""
    g = Geo()
    rng = random.Random(404)
    hx, hz = 12.0, 7.0
    fh = 3.2
    floors = 5
    gf = 4.2
    A.plinth(g, A.footprint_rect(hx + 0.3, hz + 0.3), 0.0, PLINTH_D, detail)
    base = A.FacadeSpec(bay=4.0, full_height=True, win_w=0.7, fh=gf, reveal=0.6, mullions=1, transom=True,
                        frame_band=A.TRIM_BLACK, wall="NB_Stone", spandrel="NB_Stone", pier="NB_Stone", seed=41)
    bside = A.FacadeSpec(bay=4.0, win_w=0.4, win_h=0.5, sill=1.4, fh=gf, reveal=0.3, wall="NB_Stone", seed=42)
    for i, (a, b) in enumerate(A.poly_edges(A.footprint_rect(hx, hz))):
        A.facade(g, a, b, 0.0, 1, base if i == 0 else bside, detail)
    A.floor_slab_edge(g, A.footprint_rect(hx, hz), gf, detail, proj=0.25, mat="NB_Stone")
    face = A.FacadeSpec(bay=3.0, full_height=True, win_w=0.56, fh=fh, reveal=0.3, mullions=1, transom=True,
                        frame_band=A.TRIM_TEAK, shutters=A.TRIM_TEAK, seed=43)
    side = A.FacadeSpec(bay=3.5, win_w=0.4, win_h=0.55, sill=0.9, fh=fh, reveal=0.3, frame_band=A.TRIM_TEAK,
                        shutters=A.TRIM_TEAK, seed=44)
    block(g, hx, hz, gf, floors, fh, [face, side, face, side], detail,
          balconies={0: dict(kind="bars", depth=0.9, group=1, furnish=False, band=A.TRIM_BLACK),
                     2: dict(kind="bars", depth=0.9, group=1, furnish=False, band=A.TRIM_BLACK,
                             pattern=lambda f, k: k % 2 == 0)}, rng=rng)
    ytop = gf + floors * fh
    for f in range(1, floors):
        A.floor_slab_edge(g, A.footprint_rect(hx, hz), gf + f * fh, detail, proj=0.06, mat="NB_Accent")
    A.corner_quoins(g, A.footprint_rect(hx, hz), gf, ytop, detail, w=0.5)
    # planter boxes on the balcony rails (bougainvillea spill)
    if detail == 0:
        F = A.Frame((-hx, -hz), (hx, -hz))
        for f in range(floors):
            for k in range(8):
                s = (k + 0.5) * F.L / 8
                A.roof_planter(g, *_rail_box(F, s, 0.9), gf + f * fh + 1.1, 1,
                               mat="NB_Bougainvillea" if (k + f) % 2 else "NB_Hibiscus")
    # deep eaves + red tile hip roof
    A.bbox(g, (0, ytop + 0.15, 0), (2 * hx + 2.2, 0.3, 2 * hz + 2.2), "NB_Plaster", 0.05, detail=detail, bottom=True)
    A.hip_roof(g, -hx - 1.1, -hz - 1.1, hx + 1.1, hz + 1.1, ytop + 0.3, 4.2, detail=detail)
    F = A.Frame((-hx, -hz), (hx, -hz))
    A.blade_sign(g, F, 2.0, gf - 0.2, gf + 2.6, A.SIGN_VILLA_HINATA, detail, out=1.0)
    # entrance steps + lanterns
    if detail < 2:
        for i in range(3):
            A.bbox(g, (0, 0.08 + i * 0.0, -hz - 1.2 - i * 0.35), (6.0 - i * 0.0, 0.16, 0.35), "NB_Stone", 0.02, detail=detail)
        for x in (-3.6, 3.6):
            A.bbox(g, (x, 0.6, -hz - 0.9), (0.6, 1.2, 0.6), "NB_Stone", 0.04, detail=detail)
            A.potted_plant(g, (x, 1.2, -hz - 0.9), detail, big=True, mat="NB_Hibiscus")
    lot_rec("Nagisa_B_Boutique1", hx + 1.1, hz + 1.1, ytop + 4.5, lot=[hx + 3, hz + 4])
    return g


def _rail_box(F, s, depth):
    a = F.P(s - 0.6, 0, depth - 0.25)
    b = F.P(s + 0.6, 0, depth - 0.05)
    return (min(a[0], b[0]), min(a[2], b[2]), max(a[0], b[0]), max(a[2], b[2]))


# ================================================================== BOUTIQUE 2 - "Umi Terrace" modern
def boutique2(detail):
    """7-storey tropical-modern boutique hotel, 20 x 16 m: timber-board accent frames, vertical
    teak fin screens, stepped terraces on the upper two floors with roof garden, full-height glazing
    and glass balconies, rooftop plunge pool pergola."""
    g = Geo()
    rng = random.Random(505)
    hx, hz = 10.0, 8.0
    fh = 3.3
    gf = 4.5
    A.plinth(g, A.footprint_rect(hx + 0.3, hz + 0.3), 0.0, PLINTH_D, detail)
    lobby(g, hx, hz, gf, detail, sign=A.SIGN_BLUE_REEF if False else None, canopy=False, mat="NB_Stone",
          faces=(0, 1))
    A.shopfront(g, (-hx, -hz - 0.01), (0.0, -hz - 0.01), 0.0, gf, detail, rng, sign=A.SIGN_YASHI_COFFEE,
                awning_band=7, shop_cell=1, bays=3, door_bay=2, fascia_mat="NB_WallBoard")
    face = A.FacadeSpec(bay=3.33, full_height=True, win_w=0.86, fh=fh, reveal=0.35, mullions=1, transom=False,
                        frame_band=A.TRIM_BLACK, pier="NB_WallBoard", seed=51)
    side = A.FacadeSpec(bay=3.2, win_w=0.62, win_h=0.62, sill=0.8, fh=fh, reveal=0.3, frame_band=A.TRIM_BLACK,
                        mullions=1, seed=52, louvres=True)
    lower = 4
    block(g, hx, hz, gf, lower, fh, [face, side, side, side], detail,
          balconies={0: dict(kind="glass", depth=1.8, group=1, furnish=True, band=A.TRIM_BLACK, divider=True)},
          rng=rng)
    y1 = gf + lower * fh
    for f in range(1, lower + 1):
        A.floor_slab_edge(g, A.footprint_rect(hx, hz), gf + f * fh, detail, proj=0.3, mat="NB_Wall")
    # teak fin screens on the right side
    if detail < 2:
        n = 14 if detail == 0 else 7
        for k in range(n):
            z = -hz + 1.0 + k * (2 * hz - 2.0) / (n - 1)
            A.bbox(g, (hx + 0.35, (gf + y1) / 2, z), (0.5, y1 - gf, 0.09), "NB_Teak", 0.0, detail=1)
    # upper stepped floors (2), inset 3 m at the front each
    ptop = A.footprint_rect(hx, hz)
    for s in range(2):
        inset = 3.0 * (s + 1)
        pp = [(-hx, -hz + inset), (hx, -hz + inset), (hx, hz), (-hx, hz)]
        yb = y1 + s * fh
        edges = A.poly_edges(pp)
        for i, (a, b) in enumerate(edges):
            A.facade(g, a, b, yb, 1, face if i == 0 else side, detail)
        A.parapet(g, [(-hx, -hz + inset - 3.0), (hx, -hz + inset - 3.0), (hx, -hz + inset), (-hx, -hz + inset)],
                  yb, 1.05, detail, mat="NB_Wall", roof_mat="NB_Teak")
        A.floor_slab_edge(g, pp, yb + fh, detail, proj=0.3, mat="NB_Wall")
        if detail == 0:
            for k in range(3):
                x = -hx + 3.5 + k * 6.5
                A.bbox(g, (x, yb + 0.3, -hz + inset - 1.5), (1.8, 0.35, 0.7), "NB_Canvas", 0.05)
                A.potted_plant(g, (x + 1.6, yb, -hz + inset - 0.6), detail, big=True, mat="NB_Bougainvillea")
    ytop = y1 + 2 * fh
    pr = [(-hx, -hz + 6.0), (hx, -hz + 6.0), (hx, hz), (-hx, hz)]
    A.parapet(g, pr, ytop, 1.1, detail, roof_mat="NB_DeckStone")
    if detail < 2:
        # plunge pool + pergola on the roof
        A.bbox(g, (-3.0, ytop + 0.45, 3.0), (9.0, 0.9, 4.0), "NB_PoolTile", 0.05, detail=detail)
        A.qn(g, [(-7.3, ytop + 0.91, 1.2), (1.3, ytop + 0.91, 1.2), (1.3, ytop + 0.91, 4.8), (-7.3, ytop + 0.91, 4.8)],
             (0, 1, 0), "NB_PoolWater")
        A.pergola(g, 2.5, -1.5, 8.5, 6.5, ytop, 2.7, detail)
        A.roof_planter(g, -hx + 0.5, 6.0, -1.0, 7.4, ytop, detail)
        A.ac_plant(g, (5.5, ytop, 7.0), 0.0, 2, detail)
    lot_rec("Nagisa_B_Boutique2", hx + 0.6, hz, ytop + 3.5, lot=[hx + 3, hz + 4])
    return g


# ================================================================== SHOPHOUSES (main street)
SHOP_SIGNS = {"A": [A.SIGN_YASHI_COFFEE, A.SIGN_HANABI_RAMEN, A.SIGN_NAGISA_BOOKS, A.SIGN_IZAKAYA_NAMI, A.SIGN_RENTAL_BIKES],
              "B": [A.SIGN_HIBISCUS_BAKERY, A.SIGN_BLUE_REEF, A.SIGN_SANGO_GELATO, A.SIGN_SURF_SHOP_KAI, A.SIGN_RAMEN_TAKI],
              "C": [A.SIGN_ISLAND_POKE, A.SIGN_TIDA_SURF, A.SIGN_CORAL_PHARMACY, A.SIGN_POKE_HOUSE, A.SIGN_UKULELE,
                    A.SIGN_SHELL_GALLERY, A.SIGN_DIVE_HOUSE]}
SHOP_CELLS = {A.SIGN_YASHI_COFFEE: 1, A.SIGN_HANABI_RAMEN: 1, A.SIGN_NAGISA_BOOKS: 6, A.SIGN_HIBISCUS_BAKERY: 1,
              A.SIGN_BLUE_REEF: 7, A.SIGN_SANGO_GELATO: 3, A.SIGN_ISLAND_POKE: 1, A.SIGN_TIDA_SURF: 2,
              A.SIGN_CORAL_PHARMACY: 5, A.SIGN_IZAKAYA_NAMI: 1, A.SIGN_RENTAL_BIKES: 2, A.SIGN_SURF_SHOP_KAI: 2,
              A.SIGN_RAMEN_TAKI: 1, A.SIGN_POKE_HOUSE: 1, A.SIGN_UKULELE: 6, A.SIGN_SHELL_GALLERY: 6, A.SIGN_DIVE_HOUSE: 7}


def shophouse(style, sign, awn):
    def build(detail):
        g = Geo()
        rng = random.Random(600 + sign)
        if style == "A":
            # 3-storey, 9 m wide, flat roof with parapet + water tank, bar balconies, shutters
            hx, hz, fh, fl, gf = 4.5, 7.0, 3.1, 2, 4.0
            A.plinth(g, A.footprint_rect(hx, hz), 0.0, PLINTH_D, detail)
            A.shopfront(g, (-hx, -hz), (hx, -hz), 0.0, gf, detail, rng, sign=sign, awning_band=awn,
                        shop_cell=SHOP_CELLS[sign], bays=3, door_bay=0)
            face = A.FacadeSpec(bay=3.0, win_w=0.6, win_h=0.68, sill=0.7, fh=fh, reveal=0.25, mullions=1,
                                transom=True, frame_band=A.TRIM_WHITE, shutters=A.TRIM_TILE, seed=sign)
            side = A.FacadeSpec(bay=3.5, win_w=0.3, win_h=0.45, sill=1.2, fh=fh, reveal=0.2, seed=sign + 1)
            for i, (a, b) in enumerate(A.poly_edges(A.footprint_rect(hx, hz))):
                if i == 0:
                    continue
                A.facade(g, a, b, 0.0, 1, A.FacadeSpec(bay=3.5, win_w=0.35, win_h=0.4, sill=1.8, fh=gf, reveal=0.2,
                                                       seed=sign + 2), detail)
            block(g, hx, hz, gf, fl, fh, [face, None, side, None], detail,
                  balconies={0: dict(kind="bars", depth=0.8, group=3, furnish=False, band=A.TRIM_BLACK, ac=True)},
                  rng=rng)
            ytop = gf + fl * fh
            A.floor_slab_edge(g, A.footprint_rect(hx, hz), gf, detail, proj=0.15)
            A.parapet(g, A.footprint_rect(hx, hz), ytop, 1.2, detail)
            if detail < 2:
                A.water_tank(g, (2.0, ytop, 4.0), detail, r=0.7, h=1.2)
                A.ac_plant(g, (-2.0, ytop, 4.5), 0.0, 2, detail)
                # stepped decorative parapet crest on the street face
                A.bbox(g, (0, ytop + 1.6, -hz + 0.1), (4.0, 0.8, 0.22), "NB_Accent", 0.03, detail=detail)
            h = ytop + 2.0
        elif style == "B":
            # 2-storey, 8 m wide, red tile hip roof, deep veranda balcony with teak posts
            hx, hz, fh, fl, gf = 4.0, 6.5, 3.2, 1, 4.0
            A.plinth(g, A.footprint_rect(hx, hz), 0.0, PLINTH_D, detail)
            A.shopfront(g, (-hx, -hz), (hx, -hz), 0.0, gf, detail, rng, sign=sign, awning_band=None,
                        shop_cell=SHOP_CELLS[sign], bays=3, door_bay=1, fascia_mat="NB_WallBoard")
            for i, (a, b) in enumerate(A.poly_edges(A.footprint_rect(hx, hz))):
                if i:
                    A.facade(g, a, b, 0.0, 1, A.FacadeSpec(bay=3.2, win_w=0.4, win_h=0.45, sill=1.5, fh=gf,
                                                           reveal=0.2, seed=sign + 3), detail)
            face = A.FacadeSpec(bay=2.66, full_height=True, win_w=0.6, fh=fh, reveal=0.25, mullions=1,
                                frame_band=A.TRIM_TIMBER, shutters=A.TRIM_TIMBER, seed=sign + 4)
            side = A.FacadeSpec(bay=3.2, win_w=0.45, win_h=0.55, sill=0.9, fh=fh, reveal=0.2, frame_band=A.TRIM_TIMBER,
                                seed=sign + 5)
            block(g, hx, hz, gf, fl, fh, [face, side, side, side], detail, rng=rng)
            ytop = gf + fh
            # veranda: slab over the pavement on posts (arcade) + timber balustrade
            A.bbox(g, (0, gf - 0.1, -hz - 1.3), (2 * hx, 0.24, 2.6), "NB_Plaster", 0.03, detail=detail)
            A.qn(g, [(-hx, gf - 0.23, -hz - 2.6), (hx, gf - 0.23, -hz - 2.6), (hx, gf - 0.23, -hz), (-hx, gf - 0.23, -hz)],
                 (0, -1, 0), "NB_Teak")
            for x in (-hx + 0.2, 0.0, hx - 0.2):
                A.bbox(g, (x, (gf - 0.2) / 2, -hz - 2.4), (0.22, gf - 0.2, 0.22), "NB_Teak", 0.02, detail=detail)
            F = A.Frame((-hx, -hz), (hx, -hz))
            A.balcony(g, F, 0.1, 2 * hx - 0.1, gf, 2.6, "bars", detail, rng, furnish=True, rail_band=A.TRIM_TIMBER,
                      slab_mat="NB_Plaster")
            A.bbox(g, (0, ytop + 0.12, 0), (2 * hx + 1.6, 0.24, 2 * hz + 1.6), "NB_Plaster", 0.04, detail=detail, bottom=True)
            A.hip_roof(g, -hx - 0.8, -hz - 0.8, hx + 0.8, hz + 0.8, ytop + 0.24, 2.6, detail=detail)
            h = ytop + 3.0
        else:
            # 4-storey, 10 m wide, modern: metal gable roof, big corner glazing, louvred screen, blade sign
            hx, hz, fh, fl, gf = 5.0, 7.5, 3.0, 3, 4.2
            A.plinth(g, A.footprint_rect(hx, hz), 0.0, PLINTH_D, detail)
            A.shopfront(g, (-hx, -hz), (hx, -hz), 0.0, gf, detail, rng, sign=sign, awning_band=awn,
                        shop_cell=SHOP_CELLS[sign], bays=3, door_bay=2, fascia_mat="NB_Accent", awning_depth=1.4)
            for i, (a, b) in enumerate(A.poly_edges(A.footprint_rect(hx, hz))):
                if i:
                    A.facade(g, a, b, 0.0, 1, A.FacadeSpec(bay=3.75, win_w=0.4, win_h=0.45, sill=1.6, fh=gf,
                                                           reveal=0.2, seed=sign + 6), detail)
            face = A.FacadeSpec(bay=3.33, win_w=0.8, win_h=0.72, sill=0.5, fh=fh, reveal=0.3, mullions=2,
                                frame_band=A.TRIM_BLACK, seed=sign + 7)
            side = A.FacadeSpec(bay=3.0, win_w=0.4, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, frame_band=A.TRIM_BLACK,
                                seed=sign + 8)
            block(g, hx, hz, gf, fl, fh, [face, side, side, side], detail, rng=rng,
                  balconies={0: dict(kind="glass", depth=0.7, group=3, furnish=False, band=A.TRIM_BLACK,
                                     pattern=lambda f, k: f == 1)})
            ytop = gf + fl * fh
            for f in range(fl + 1):
                A.floor_slab_edge(g, A.footprint_rect(hx, hz), gf + f * fh, detail, proj=0.12, mat="NB_Trim" if False else "NB_Accent")
            F = A.Frame((-hx, -hz), (hx, -hz))
            A.blade_sign(g, F, 2 * hx - 0.6, gf + 1.0, gf + 4.5, sign, detail, out=0.9)
            A.gable_roof(g, -hx, -hz, hx, hz, ytop, 2.4, detail=detail)
            h = ytop + 2.6
        lot_rec("Nagisa_B_Shop%s_%d" % (style, sign), hx, hz, h, lot=[hx, hz + 3], style=style)
        return g
    return build


# ================================================================== HOUSES (residential)
def house_wall(g, x0, z0, x1, z1, detail, h=1.2, mat="NB_Wall", gate=None, open_front=None):
    """Garden wall with coping around a lot rectangle; gate = (x centre, width) on the front (z0)."""
    for (a, b) in (((x0, z0), (x1, z0)), ((x1, z0), (x1, z1)), ((x1, z1), (x0, z1)), ((x0, z1), (x0, z0))):
        segs = [(a, b)]
        if a[1] == z0 and b[1] == z0:
            cuts = []
            if gate:
                cuts.append((gate[0] - gate[1] / 2, gate[0] + gate[1] / 2))
            if open_front:
                cuts.append(open_front)
            cuts.sort()
            segs, xc = [], x0
            for c0, c1 in cuts:
                segs.append(((xc, z0), (c0, z0)))
                xc = c1
            segs.append(((xc, z0), (x1, z0)))
        for p, q in segs:
            L = math.hypot(q[0] - p[0], q[1] - p[1])
            if L < 0.1:
                continue
            A.seg_box(g, (p[0], h / 2, p[1]), (q[0], h / 2, q[1]), 0.22, h + 0.6, mat, bev=0.02, detail=detail) \
                if False else A.bbox(g, ((p[0] + q[0]) / 2, h / 2 - 0.3, (p[1] + q[1]) / 2),
                                     (abs(q[0] - p[0]) + 0.22, h + 0.6, abs(q[1] - p[1]) + 0.22), mat, 0.03, detail=detail)
            if detail < 2:
                A.tbox(g, ((p[0] + q[0]) / 2, h + 0.04, (p[1] + q[1]) / 2),
                       (abs(q[0] - p[0]) + 0.3, 0.08, abs(q[1] - p[1]) + 0.3), A.TRIM_COPING, detail=1)
    if gate and detail < 2:
        gx, gw = gate
        for sx in (-1, 1):
            A.bbox(g, (gx + sx * (gw / 2 + 0.2), 0.75 - 0.3, z0), (0.45, 2.1, 0.45), mat, 0.03, detail=detail)
            A.tbox(g, (gx + sx * (gw / 2 + 0.2), 1.52, z0), (0.55, 0.1, 0.55), A.TRIM_COPING, detail=1)
        # slatted metal gate leaves (half open)
        for sx in (-1, 1):
            p0 = (gx + sx * gw / 2, 0.05, z0)
            ang = sx * 0.9
            n = 8 if detail == 0 else 2
            for k in range(n):
                t = (k + 0.5) / n * (gw / 2 - 0.05)
                px = p0[0] - sx * t * math.cos(ang)
                pz = z0 - t * math.sin(abs(ang))
                A.tbox(g, (px, 0.65, pz), (0.04, 1.1, 0.04), A.TRIM_BLACK, detail=1)
            A.seg_box(g, (p0[0], 1.15, z0), (p0[0] - sx * (gw / 2) * math.cos(ang), 1.15, z0 - (gw / 2) * math.sin(abs(ang))),
                      0.05, 0.05, "NB_Trim", band=A.TRIM_BLACK)
            A.seg_box(g, (p0[0], 0.15, z0), (p0[0] - sx * (gw / 2) * math.cos(ang), 0.15, z0 - (gw / 2) * math.sin(abs(ang))),
                      0.05, 0.05, "NB_Trim", band=A.TRIM_BLACK)


def garden(g, x0, z0, x1, z1, detail, mat="NB_Lawn", beds=True, rng=None):
    rng = rng or random.Random(3)
    A.qn(g, [(x0, 0.03, z0), (x1, 0.03, z0), (x1, 0.03, z1), (x0, 0.03, z1)], (0, 1, 0), mat)
    if beds and detail < 2:
        A.roof_planter(g, x0 + 0.3, z0 + 0.3, x0 + 1.2, z1 - 0.3, -0.35, detail)
        if detail == 0:
            for k in range(3):
                A.plant_cards(g, (x0 + 2.0 + rng.random() * (x1 - x0 - 3), 0.0, z0 + 0.8 + rng.random() * (z1 - z0 - 1.6)),
                              1.1, 0.7, "NB_Hibiscus" if k % 2 else "NB_Bougainvillea", n=3, yaw=k)


def house_a(detail):
    """2-storey flat-roof modern pastel house, 11 x 9 m on a 16 x 20 lot: roof terrace with pergola,
    glass balcony, louvred windows, garden wall + gate, carport with slatted roof, lawn + beds."""
    g = Geo()
    rng = random.Random(701)
    hx, hz, fh = 5.5, 4.5, 3.1
    zc = 0.5                                       # house centred slightly back in the lot
    A.plinth(g, [(p[0], p[1] + zc) for p in A.footprint_rect(hx, hz)], 0.25, 1.4, detail)
    poly = [(p[0], p[1] + zc) for p in A.footprint_rect(hx, hz)]
    face = A.FacadeSpec(bay=2.75, full_height=False, win_w=0.62, win_h=0.66, sill=0.8, fh=fh, reveal=0.22,
                        mullions=1, transom=True, frame_band=A.TRIM_WHITE, louvres=True, seed=71)
    side = A.FacadeSpec(bay=3.0, win_w=0.4, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, frame_band=A.TRIM_WHITE, seed=72)
    block(g, hx, hz, 0.25, 2, fh, [face, side, side, side], detail, poly=poly,
          balconies={0: dict(kind="glass", depth=1.4, start=1, group=2, furnish=True,
                             pattern=lambda f, k: k == 0)}, rng=rng)
    A.floor_slab_edge(g, poly, 0.25 + fh, detail, proj=0.18, mat="NB_Accent")
    ytop = 0.25 + 2 * fh
    A.roof_deck_edge(g, poly, ytop, detail, h=0.45, proj=0.45)
    A.parapet(g, poly, ytop + 0.45, 0.9, detail, roof_mat="NB_Teak")
    if detail < 2:
        A.pergola(g, -hx + 0.8, -hz + zc + 0.8, 0.5, zc + 1.0, ytop + 0.45, 2.5, detail)
        A.water_tank(g, (hx - 1.2, ytop + 0.45, hz + zc - 1.2), detail, r=0.55, h=1.0)
        A.roof_planter(g, 1.5, -hz + zc + 0.4, hx - 0.4, -hz + zc + 1.1, ytop + 0.45, detail)
    # front door canopy + step
    A.bbox(g, (1.8, 2.6, -hz + zc - 0.7), (2.4, 0.16, 1.4), "NB_Accent", 0.02, detail=detail)
    # lot: 16 x 20 (x -8..8, z -10..10)
    LX, LZ = 8.0, 10.0
    garden(g, -LX + 0.2, -LZ + 0.2, LX - 5.0, -hz + zc - 0.2, detail, rng=rng)
    garden(g, -LX + 0.2, hz + zc + 0.2, LX - 0.2, LZ - 0.2, detail, beds=False, rng=rng)
    A.qn(g, [(LX - 4.8, 0.035, -LZ), (LX - 0.2, 0.035, -LZ), (LX - 0.2, 0.035, hz + zc), (LX - 4.8, 0.035, hz + zc)],
         (0, 1, 0), "NB_PlazaStone")
    house_wall(g, -LX, -LZ, LX, LZ, detail, h=1.1, gate=(-1.5, 1.4), open_front=(LX - 4.8, LX - 0.2))
    # carport (slatted pergola roof on 4 steel posts) over the drive
    if detail < 2:
        for x in (LX - 4.6, LX - 0.4):
            for z in (-LZ + 1.5, -hz + zc - 0.3):
                A.tbox(g, (x, 1.25, z), (0.12, 2.5, 0.12), A.TRIM_BLACK, detail=1)
        A.bbox(g, (LX - 2.5, 2.55, (-LZ + 1.5 - hz + zc) / 2), (4.6, 0.12, abs(-LZ + 1.5 + hz - zc) + 0.6), "NB_Accent",
               0.02, detail=detail)
    lot_rec("Nagisa_B_HouseA", LX, LZ, ytop + 3.5, lot=[LX, LZ], drive=[LX - 2.5, -LZ], carport=[LX - 2.5, -6.2])
    return g


def house_b(detail):
    """Single-storey Okinawan house, 13 x 10 m on a 16 x 18 lot: red-tile hip roof with deep eaves,
    engawa veranda with timber posts, coral-stone garden wall (hinpun screen behind the gate),
    shisa-guardian gate posts, sliding shoji-style glazing, kitchen garden."""
    g = Geo()
    rng = random.Random(702)
    hx, hz, h = 6.5, 5.0, 3.1
    zc = 1.0
    poly = [(p[0], p[1] + zc) for p in A.footprint_rect(hx, hz)]
    A.plinth(g, poly, 0.45, 1.4, detail, mat="NB_Stone")
    face = A.FacadeSpec(bay=2.6, full_height=True, win_w=0.8, fh=h - 0.3, reveal=0.15, mullions=3, transom=True,
                        frame_band=A.TRIM_TIMBER, wall="NB_Wall", seed=73)
    side = A.FacadeSpec(bay=3.3, win_w=0.45, win_h=0.5, sill=0.9, fh=h - 0.3, reveal=0.2, frame_band=A.TRIM_TIMBER,
                        shutters=A.TRIM_TIMBER, seed=74)
    block(g, hx, hz, 0.45, 1, h - 0.3, [face, side, side, side], detail, poly=poly, rng=rng)
    # engawa (veranda deck) + posts
    A.bbox(g, (0, 0.38, -hz + zc - 1.0), (2 * hx, 0.14, 2.0), "NB_Teak", 0.02, detail=detail)
    for x in (-hx + 0.2, -hx / 3, hx / 3, hx - 0.2):
        A.bbox(g, (x, 0.45 + (h - 0.45) / 2, -hz + zc - 1.8), (0.16, h - 0.45, 0.16), "NB_Teak", 0.02, detail=detail)
    A.hip_roof(g, -hx - 1.4, -hz + zc - 2.1, hx + 1.4, hz + zc + 1.0, h + 0.15, 2.9, detail=detail)
    LX, LZ = 8.0, 9.0
    garden(g, -LX + 0.2, -LZ + 0.2, LX - 0.2, -hz + zc - 2.2, detail, mat="NB_Groundcover", rng=rng)
    garden(g, -LX + 0.2, hz + zc + 0.3, LX - 0.2, LZ - 0.2, detail, mat="NB_Lawn", beds=False, rng=rng)
    A.qn(g, [(-1.0, 0.04, -LZ), (1.0, 0.04, -LZ), (1.0, 0.04, -hz + zc - 2.0), (-1.0, 0.04, -hz + zc - 2.0)], (0, 1, 0),
         "NB_PlazaStone")
    house_wall(g, -LX, -LZ, LX, LZ, detail, h=1.4, mat="NB_Stone", gate=(0.0, 2.0))
    if detail < 2:
        # hinpun screen wall behind the gate
        A.bbox(g, (0, 0.6, -LZ + 2.2), (3.4, 1.8, 0.4), "NB_Stone", 0.04, detail=detail)
        A.tbox(g, (0, 1.54, -LZ + 2.2), (3.5, 0.08, 0.5), A.TRIM_COPING, detail=1)
        # shisa guardians (stylised: body + head blocks, terracotta) on the gate posts
        for sx in (-1, 1):
            A.bbox(g, (sx * 1.2, 1.7, -LZ), (0.34, 0.3, 0.4), "NB_RoofTile", 0.06, detail=detail)
            A.bbox(g, (sx * 1.2, 1.95, -LZ - 0.08), (0.3, 0.26, 0.28), "NB_RoofTile", 0.06, detail=detail)
    lot_rec("Nagisa_B_HouseB", LX, LZ, h + 3.2, lot=[LX, LZ], gate=[0, -LZ])
    return g


def house_c(detail):
    """2-storey timber beach house, 10 x 8 m on a 14 x 16 lot: board-and-batten walls (NB_WallBoard),
    metal gable roof, wrap veranda on the ground floor + upper deck with white timber balustrade,
    outdoor shower, surfboards against the wall, low picket fence, sandy garden with groundcover."""
    g = Geo()
    rng = random.Random(703)
    hx, hz, fh = 5.0, 4.0, 3.0
    y0 = 0.6
    poly = A.footprint_rect(hx, hz)
    # piers (raised on stumps)
    if detail < 2:
        for x in (-hx + 0.3, 0, hx - 0.3):
            for z in (-hz + 0.3, 0, hz - 0.3):
                A.bbox(g, (x, y0 / 2 - 0.2, z), (0.3, y0 + 0.4, 0.3), "NB_Stone", 0.02, detail=detail)
    A.bbox(g, (0, y0 - 0.1, -0.8), (2 * hx + 0.2, 0.2, 2 * hz + 1.8), "NB_Teak", 0.02, detail=detail, bottom=True)
    face = A.FacadeSpec(bay=2.5, full_height=True, win_w=0.7, fh=fh, reveal=0.12, mullions=1, transom=True,
                        frame_band=A.TRIM_TIMBER, wall="NB_WallBoard", spandrel="NB_WallBoard", pier="NB_WallBoard", seed=75)
    side = A.FacadeSpec(bay=2.6, win_w=0.45, win_h=0.55, sill=0.9, fh=fh, reveal=0.12, frame_band=A.TRIM_TIMBER,
                        wall="NB_WallBoard", spandrel="NB_WallBoard", pier="NB_WallBoard", shutters=A.TRIM_TILE, seed=76)
    block(g, hx, hz, y0, 2, fh, [face, side, side, side], detail, poly=poly, rng=rng)
    F = A.Frame((-hx, -hz), (hx, -hz))
    A.balcony(g, F, 0.1, 2 * hx - 0.1, y0 + fh, 1.6, "bars", detail, rng, furnish=True, rail_band=A.TRIM_TIMBER,
              slab_mat="NB_Teak")
    A.balcony(g, F, 0.1, 2 * hx - 0.1, y0, 1.7, "bars", detail, rng, furnish=True, rail_band=A.TRIM_TIMBER,
              slab_mat="NB_Teak")
    ytop = y0 + 2 * fh
    A.gable_roof(g, -hx, -hz, hx, hz, ytop, 2.2, gable_mat="NB_WallBoard", detail=detail, over=0.7)
    if detail < 2:
        # steps + surfboards + shower
        for i in range(3):
            A.bbox(g, (-hx + 1.2, y0 - 0.2 - i * 0.2, -hz - 1.8 - i * 0.3), (1.4, 0.06, 0.3), "NB_Teak", 0.0, detail=1)
        for k, band in enumerate((0, 1, 3)):
            A.bbox(g, (hx + 0.12, y0 + 1.1, -1.8 + k * 0.62), (0.07, 2.1, 0.52), ("NB_Gelcoat", "NB_PaintTeal", "NB_Paint")[k % 3], 0.03, detail=detail)
        A.tbox(g, (hx + 0.35, y0 + 1.2, 2.0), (0.06, 2.4, 0.06), A.TRIM_WHITE, detail=1)
    LX, LZ = 7.0, 8.0
    A.qn(g, [(-LX, 0.03, -LZ), (LX, 0.03, -LZ), (LX, 0.03, LZ), (-LX, 0.03, LZ)], (0, 1, 0), "NB_Groundcover")
    if detail < 2:
        # low white picket fence
        n = 22 if detail == 0 else 6
        for (a, b) in (((-LX, -LZ), (-1.0, -LZ)), ((1.0, -LZ), (LX, -LZ)), ((LX, -LZ), (LX, LZ)), ((-LX, LZ), (-LX, -LZ))):
            A.seg_box(g, (a[0], 0.55, a[1]), (b[0], 0.55, b[1]), 0.04, 0.08, "NB_Trim", band=A.TRIM_WHITE)
            A.seg_box(g, (a[0], 0.25, a[1]), (b[0], 0.25, b[1]), 0.04, 0.08, "NB_Trim", band=A.TRIM_WHITE)
            if detail == 0:
                L = math.hypot(b[0] - a[0], b[1] - a[1])
                m = int(L / 0.25)
                for k in range(m + 1):
                    f = k / max(1, m)
                    A.tbox(g, (a[0] + (b[0] - a[0]) * f, 0.4, a[1] + (b[1] - a[1]) * f), (0.07, 0.8, 0.025),
                           A.TRIM_WHITE, detail=1)
        garden(g, -LX + 0.5, -LZ + 0.5, -hx - 0.3, LZ - 0.5, detail, mat="NB_Groundcover", rng=rng)
    lot_rec("Nagisa_B_HouseC", LX, LZ, ytop + 2.6, lot=[LX, LZ], gate=[0, -LZ])
    return g


# ================================================================== SMALL BUILDINGS
def marina_club(detail):
    """Nagisa Marina Club: 2-storey clubhouse/restaurant 26 x 14 m, white render + teak, full glazing
    to the water (-z), wrap-around teak deck with glass balustrade, upper terrace under a deep
    flat roof with timber soffit, restaurant umbrellas + tables, flagpoles, sign."""
    g = Geo()
    rng = random.Random(801)
    hx, hz, gf, fh = 13.0, 7.0, 4.2, 3.6
    A.plinth(g, A.footprint_rect(hx, hz), 0.0, PLINTH_D, detail)
    glaz = A.FacadeSpec(bay=3.25, full_height=True, win_w=0.92, fh=gf, reveal=0.2, mullions=2, transom=True,
                        frame_band=A.TRIM_BRONZE, seed=81)
    side = A.FacadeSpec(bay=3.5, win_w=0.55, win_h=0.55, sill=1.0, fh=gf, reveal=0.25, frame_band=A.TRIM_BRONZE, seed=82)
    for i, (a, b) in enumerate(A.poly_edges(A.footprint_rect(hx, hz))):
        A.facade(g, a, b, 0.0, 1, glaz if i in (0, 1) else side, detail)
    A.floor_slab_edge(g, A.footprint_rect(hx, hz), gf, detail, proj=0.3, mat="NB_Wall")
    A.qn(g, [(p[0], gf + 0.005, p[1]) for p in A.footprint_rect(hx, hz)], (0, 1, 0), "NB_Teak")   # lower roof = terrace
    uhx = hx - 3.0
    for sx in (-1, 1):   # glass rails along the exposed end terraces
        Fe = A.Frame((sx * hx, hz) if sx < 0 else (sx * hx, -hz), (sx * hx, -hz) if sx < 0 else (sx * hx, hz))
        if detail < 2:
            p, q = Fe.P(0.1, gf, -0.1), Fe.P(Fe.L - 0.1, gf, -0.1)
            g.quad(p, q, add(q, (0, 1.05, 0)), add(p, (0, 1.05, 0)), "NB_BalconyGlass", double=True)
            A.seg_box(g, add(p, (0, 1.08, 0)), add(q, (0, 1.08, 0)), 0.07, 0.06, "NB_Trim", band=A.TRIM_BRONZE)
    up = [(-uhx, -hz + 3.0), (uhx, -hz + 3.0), (uhx, hz), (-uhx, hz)]
    ug = A.FacadeSpec(bay=3.33, full_height=True, win_w=0.92, fh=fh, reveal=0.2, mullions=2, frame_band=A.TRIM_BRONZE,
                      wall="NB_WallBoard", spandrel="NB_WallBoard", pier="NB_WallBoard", seed=83)
    for i, (a, b) in enumerate(A.poly_edges(up)):
        A.facade(g, a, b, gf, 1, ug if i == 0 else A.FacadeSpec(bay=3.3, win_w=0.5, win_h=0.5, sill=1.0, fh=fh,
                                                                  wall="NB_WallBoard", seed=84 + i), detail)
    # deep flat roof over the upper terrace, timber soffit
    A.bbox(g, (0, gf + fh + 0.25, -1.5), (2 * hx + 1.0, 0.5, 2 * hz + 3.0), "NB_Wall", 0.05, detail=detail, bottom=False)
    A.qn(g, [(-hx - 0.4, gf + fh - 0.01, -hz - 2.9), (hx + 0.4, gf + fh - 0.01, -hz - 2.9), (hx + 0.4, gf + fh - 0.01, hz + 1.4),
             (-hx - 0.4, gf + fh - 0.01, hz + 1.4)], (0, -1, 0), "NB_Teak")
    for x in (-hx + 0.3, -hx / 2, 0, hx / 2, hx - 0.3):
        A.tbox(g, (x, gf + fh / 2, -hz - 2.6), (0.2, fh, 0.2), A.TRIM_WHITE, detail=detail)
    # upper terrace deck with glass rail + tables
    F = A.Frame((-hx, -hz), (hx, -hz))
    A.balcony(g, F, 0.05, 2 * hx - 0.05, gf, 2.8, "glass", detail, rng, furnish=False, rail_band=A.TRIM_BRONZE,
              slab_mat="NB_Teak")
    # ground deck (teak) wrap to the water + umbrellas
    A.bbox(g, (0, 0.12, -hz - 4.0), (2 * hx + 4.0, 0.24, 8.0), "NB_Teak", 0.02, detail=detail)
    if detail < 2:
        Fd = A.Frame((-hx - 2.0, -hz - 8.0), (hx + 2.0, -hz - 8.0))
        for sa, sb in ((0.1, Fd.L - 0.1),):
            for (p, q) in ((Fd.P(sa, 0.3, 0.0), Fd.P(sb, 0.3, 0.0)),):
                g.quad(p, q, add(q, (0, 1.0, 0)), add(p, (0, 1.0, 0)), "NB_BalconyGlass", double=True)
                A.seg_box(g, add(p, (0, 1.05, 0)), add(q, (0, 1.05, 0)), 0.07, 0.06, "NB_Trim", band=A.TRIM_BRONZE)
    if detail == 0:
        for k in range(5):
            x = -hx + 2.5 + k * (2 * hx - 5) / 4
            z = -hz - 4.2
            A.table(g, (x, 0.24, z), 0.45, 0.74, 10)
            for a in range(4):
                A.chair(g, (x + math.cos(a * math.pi / 2) * 0.85, 0.24, z + math.sin(a * math.pi / 2) * 0.85),
                        a * math.pi / 2 + math.pi / 2)
            A.cyl(g, (x, 0.24, z), 0.03, 2.5, 4, "NB_Teak", top=False)
            A.cyl(g, (x, 2.2, z), 1.5, 0.45, 8, "NB_Canvas", top=True, r_top=0.08)
        for k in range(3):
            x = -hx + 1 + k * (2 * hx - 2) / 2
            A.table(g, (x, gf + 0.02, -hz - 1.4), 0.4, 0.74, 8)
            A.chair(g, (x - 0.7, gf + 0.02, -hz - 1.4), math.pi / 2)
            A.chair(g, (x + 0.7, gf + 0.02, -hz - 1.4), -math.pi / 2)
    if detail < 2:
        for x in (hx + 1.5, hx + 2.3):
            A.cyl(g, (x, 0.24, -hz - 7.4), 0.06, 9.0, 6, "NB_Plaster", top=True)
        Fs = A.Frame((-7.0, hz + 0.02), (7.0, hz + 0.02))
        Fs.n = (0.0, 0.0, 1.0)
        Fs2 = A.Frame((7.0, hz + 0.02), (-7.0, hz + 0.02))
        A.sign_board(g, Fs2, 1.0, 13.0, gf + fh - 0.9, gf + fh + 0.45, 0.25, A.SIGN_MARINA_CLUB, detail)
        A.sign_board(g, A.Frame((-hx, -hz - 3.0), (hx, -hz - 3.0)), 7.0, 19.0, gf + fh + 0.02, gf + fh + 0.48, 0.0,
                     A.SIGN_MARINA_CLUB, detail)
    lot_rec("Nagisa_B_MarinaClub", hx + 2, hz + 8, gf + fh + 1, lot=[hx + 2, hz + 8], sea_front=True)
    return g


def kiosk_base(g, hx, hz, h, detail, sign, awn, cell, rng, roof="flat", wall="NB_Wall"):
    A.plinth(g, A.footprint_rect(hx, hz), 0.0, 0.8, detail)
    A.shopfront(g, (-hx, -hz), (hx, -hz), 0.0, h, detail, rng, sign=sign, awning_band=awn, shop_cell=cell,
                bays=max(1, int(round(2 * hx / 3.0))), door_bay=0, awning_depth=1.6)
    for i, (a, b) in enumerate(A.poly_edges(A.footprint_rect(hx, hz))):
        if i:
            A.facade(g, a, b, 0.0, 1, A.FacadeSpec(bay=3.0, win_w=0.45, win_h=0.4, sill=1.3, fh=h, reveal=0.15,
                                                   wall=wall, spandrel=wall, pier=wall, seed=sign + i), detail)
    if roof == "flat":
        A.roof_deck_edge(g, A.footprint_rect(hx, hz), h, detail, h=0.4, proj=0.5)
        A.qn(g, [(p[0], h + 0.4, p[1]) for p in A.footprint_rect(hx + 0.5, hz + 0.5)], (0, 1, 0), "NB_Concrete")
    elif roof == "gable":
        A.gable_roof(g, -hx, -hz, hx, hz, h, 1.6, detail=detail, over=0.6)
    else:
        A.hip_roof(g, -hx - 0.8, -hz - 0.8, hx + 0.8, hz + 0.8, h, 1.8, detail=detail)


def surf_shop(detail):
    """Tida Surf Rentals: timber-board shack 10 x 6 m, metal gable roof, surfboard racks (colourful
    boards on a rack), wetsuit rail, SUP stack, sandwich board."""
    g = Geo()
    rng = random.Random(901)
    hx, hz, h = 5.0, 3.0, 3.6
    kiosk_base(g, hx, hz, h, detail, A.SIGN_TIDA_SURF, 0, 2, rng, roof="gable", wall="NB_WallBoard")
    if detail < 2:
        # board rack right of the shop (A-frame with 8 boards)
        rx = hx + 1.6
        for z in (-hz + 0.3, hz - 0.3):
            A.seg_box(g, (rx - 0.6, 0, z), (rx, 1.9, z), 0.08, 0.08, "NB_Teak")
            A.seg_box(g, (rx + 0.6, 0, z), (rx, 1.9, z), 0.08, 0.08, "NB_Teak")
        n = 8 if detail == 0 else 3
        for k in range(n):
            s = -1 if k % 2 else 1
            A.bbox(g, (rx + s * 0.35, 1.2, -hz + 0.6 + (k // 2) * 1.2), (0.07, 2.3, 0.52), ("NB_Gelcoat", "NB_PaintTeal", "NB_Paint")[k % 3], 0.03, detail=detail)
        if detail == 0:
            # sandwich board + SUP stack
            A.seg_box(g, (0.5, 0, -hz - 2.2), (0.5, 0.9, -hz - 2.0), 0.6, 0.04, "NB_Signs2")
            for k in range(4):
                A.bbox(g, (-hx - 1.4, 0.1 + k * 0.14, 0.0), (0.8, 0.12, 3.2), "NB_Gelcoat", 0.04)
    lot_rec("Nagisa_B_SurfShop", hx + 2.3, hz + 2.5, h + 1.8)
    return g


def beach_bar(detail):
    """Umi no Ie beach bar: open-sided timber pavilion 12 x 7 m on a raised deck, thatch-look hip
    roof, bar counter with stools, back-bar shelves, string of lamps, loungers out front."""
    g = Geo()
    rng = random.Random(902)
    hx, hz, h = 6.0, 3.5, 3.2
    A.bbox(g, (0, 0.2, 0), (2 * hx + 2.0, 0.4, 2 * hz + 2.0), "NB_Teak", 0.02, detail=detail)
    for x in (-hx, -hx / 3, hx / 3, hx):
        for z in (-hz, hz):
            A.bbox(g, (x, 0.4 + h / 2, z), (0.22, h, 0.22), "NB_Teak", 0.02, detail=detail)
    A.hip_roof(g, -hx - 1.0, -hz - 1.0, hx + 1.0, hz + 1.0, h + 0.4, 2.2, mat="NB_Thatch", soffit="NB_Teak", detail=detail)
    # back wall (kitchen) with shop-interior shelves
    Fb = A.Frame((hx, hz), (-hx, hz))
    A.qn(g, [Fb.P(0, 0.4), Fb.P(Fb.L, 0.4), Fb.P(Fb.L, h + 0.4), Fb.P(0, h + 0.4)], Fb.n, "NB_WallBoard")
    cu = A.shop_uv(1)
    Fi = A.Frame((-hx, hz - 0.05), (hx, hz - 0.05))
    A.qn(g, [Fi.P(0.5, 0.4), Fi.P(Fi.L - 0.5, 0.4), Fi.P(Fi.L - 0.5, h + 0.2), Fi.P(0.5, h + 0.2)], Fi.n,
         "NB_ShopInterior", [(cu[0], cu[1]), (cu[2], cu[1]), (cu[2], cu[3]), (cu[0], cu[3])])
    # bar counter (L) + stools
    A.bbox(g, (0, 0.4 + 0.55, 0.6), (8.0, 1.1, 0.7), "NB_WallBoard", 0.03, detail=detail)
    A.tbox(g, (0, 0.4 + 1.12, 0.5), (8.3, 0.06, 0.95), A.TRIM_TEAK, detail=detail)
    if detail == 0:
        for k in range(8):
            x = -3.5 + k
            A.cyl(g, (x, 0.4, -0.2), 0.03, 0.72, 4, "NB_Trim", top=False)
            A.cyl(g, (x, 1.12, -0.2), 0.2, 0.06, 8, "NB_Teak", top=True)
        for k in range(10):
            x = -hx + 0.6 + k * (2 * hx - 1.2) / 9
            A.cyl(g, (x, h + 0.1, -hz - 0.05), 0.08, 0.14, 6, "NB_Lamp", top=True, bottom=True)
    Fs = A.Frame((-hx - 1.0, -hz - 1.02), (hx + 1.0, -hz - 1.02))
    A.sign_board(g, Fs, 3.5, 10.5, h + 0.0, h + 0.75, 0.0, A.SIGN_UMI_NO_IE, detail)
    lot_rec("Nagisa_B_BeachBar", hx + 1, hz + 1, h + 2.6)
    return g


def gelato_kiosk(detail):
    """Sango Gelato: small pastel kiosk 5 x 4 m with a big serving window, striped awning, freezer
    counter, giant cone sign on the roof, 2 cafe tables."""
    g = Geo()
    rng = random.Random(903)
    hx, hz, h = 2.5, 2.0, 3.0
    kiosk_base(g, hx, hz, h, detail, A.SIGN_SANGO_GELATO, 5, 3, rng, roof="flat")
    if detail < 2:
        # rooftop cone sign
        A.cyl(g, (0, h + 0.4, 0), 0.02, 1.6, 10, "NB_WallBoard", top=True, r_top=0.45)
        g.cylinder((0, h + 2.0, 0), 0.5, 0.5, 10, "NB_Awning", top=True, r_top=0.1)
    if detail == 0:
        for x in (-1.6, 1.6):
            A.table(g, (x, 0.0, -hz - 2.8), 0.35, 0.74, 8)
            A.chair(g, (x - 0.6, 0.0, -hz - 2.8), math.pi / 2)
            A.chair(g, (x + 0.6, 0.0, -hz - 2.8), -math.pi / 2)
    lot_rec("Nagisa_B_Gelato", hx + 1, hz + 3.5, h + 2.6)
    return g


def shave_ice(detail):
    g = Geo()
    rng = random.Random(906)
    hx, hz, h = 2.0, 1.8, 2.8
    kiosk_base(g, hx, hz, h, detail, A.SIGN_SHAVE_ICE, 0, 3, rng, roof="gable", wall="NB_WallBoard")
    lot_rec("Nagisa_B_ShaveIce", hx + 0.6, hz + 2.0, h + 1.8)
    return g


def konbini(detail):
    """Kaiyo Mart 24: convenience store 16 x 12 m, full-width glazing, backlit fascia with the
    stripe band, forecourt canopy, bins, ATM, bike parking, ice chest."""
    g = Geo()
    rng = random.Random(904)
    hx, hz, h = 8.0, 6.0, 4.4
    A.plinth(g, A.footprint_rect(hx, hz), 0.0, 0.8, detail)
    A.shopfront(g, (-hx, -hz), (hx, -hz), 0.0, h, detail, rng, sign=A.SIGN_KAIYO_MART, awning_band=None,
                shop_cell=5, bays=5, door_bay=1, fascia_mat="NB_Wall", recess=0.25)
    for i, (a, b) in enumerate(A.poly_edges(A.footprint_rect(hx, hz))):
        if i:
            A.qn(g, [A.Frame(a, b).P(0, 0), A.Frame(a, b).P(A.Frame(a, b).L, 0), A.Frame(a, b).P(A.Frame(a, b).L, h),
                     A.Frame(a, b).P(0, h)], A.Frame(a, b).n, "NB_Wall")
    # colour stripe band on the fascia (awning sheet teal / green bands, flat)
    Fr = A.Frame((-hx, -hz), (hx, -hz))
    v0, v1 = A.band_v(0)
    A.qn(g, [Fr.P(0, h - 0.95, 0.03), Fr.P(Fr.L, h - 0.95, 0.03), Fr.P(Fr.L, h - 0.8, 0.03), Fr.P(0, h - 0.8, 0.03)], Fr.n,
         "NB_Awning", [(0, v0), (0.01, v0), (0.01, v1), (0, v1)])
    A.roof_deck_edge(g, A.footprint_rect(hx, hz), h, detail, h=0.35, proj=0.25)
    A.qn(g, [(p[0], h + 0.35, p[1]) for p in A.footprint_rect(hx + 0.25, hz + 0.25)], (0, 1, 0), "NB_Concrete")
    if detail < 2:
        A.ac_plant(g, (hx - 2.0, h + 0.35, 2.0), 0.0, 3, detail)
        # bins + ATM + ice chest along the front
        A.bbox(g, (hx - 1.0, 0.55, -hz - 0.5), (1.6, 1.1, 0.6), "NB_Trim", 0.03, uvs_band=A.TRIM_WHITE, detail=detail)
        A.bbox(g, (-hx + 0.9, 0.9, -hz - 0.45), (0.9, 1.8, 0.7), "NB_Trim", 0.03, uvs_band=A.TRIM_BLACK, detail=detail)
        A.bbox(g, (-hx + 2.4, 0.45, -hz - 0.5), (1.3, 0.9, 0.7), "NB_Trim", 0.05, uvs_band=A.TRIM_WHITE, detail=detail)
    lot_rec("Nagisa_B_Konbini", hx, hz + 6, h + 1, parking=[0, -hz - 6])
    return g


def shrine(detail):
    """Small seaside shrine: vermilion torii, stone lanterns, stone path, a haiden with a copper-green
    curved-feel gable roof (layered), offering box, rope; on a raised stone terrace."""
    g = Geo()
    rng = random.Random(905)
    # terrace
    A.bbox(g, (0, 0.3, 4.0), (12.0, 0.9, 12.0), "NB_Stone", 0.05, detail=detail)
    for i in range(4):
        A.bbox(g, (0, 0.1 + i * 0.12 - 0.12, -2.0 - 0.9 + (i) * 0.3 - 1.2), (4.0, 0.2, 0.3 + 0.0), "NB_Stone", 0.02, detail=detail) \
            if False else A.bbox(g, (0, 0.12 * i, -2.6 + i * 0.35), (4.0, 0.24, 0.35), "NB_Stone", 0.02, detail=detail)
    A.qn(g, [(-1.0, 0.04, -9.0), (1.0, 0.04, -9.0), (1.0, 0.04, -2.8), (-1.0, 0.04, -2.8)], (0, 1, 0), "NB_PlazaStone")
    A.qn(g, [(-1.0, 0.76, -1.8), (1.0, 0.76, -1.8), (1.0, 0.76, 1.5), (-1.0, 0.76, 1.5)], (0, 1, 0), "NB_Gravel")
    # torii (vermilion = NB_Paint slot, black kasagi)
    tz = -7.0
    seg = (14, 8, 5)[detail]
    for sx in (-1.6, 1.6):
        A.cyl(g, (sx, 0, tz), 0.2, 4.2, seg, "NB_Paint", top=False, r_top=0.17)
        A.cyl(g, (sx, 0, tz), 0.26, 0.4, seg, "NB_Trim", top=True)
    A.bbox(g, (0, 3.5, tz), (3.9, 0.26, 0.26), "NB_Paint", 0.03, detail=detail)
    A.bbox(g, (0, 4.3, tz), (4.8, 0.3, 0.45), "NB_Paint", 0.04, detail=detail)
    A.bbox(g, (0, 4.55, tz), (5.3, 0.22, 0.55), "NB_Trim", 0.04, uvs_band=A.TRIM_BLACK, detail=detail)
    # stone lanterns
    if detail < 2:
        for sx in (-2.5, 2.5):
            for z in (-4.5,):
                A.bbox(g, (sx, 0.3, z), (0.7, 0.6, 0.7), "NB_Stone", 0.04, detail=detail)
                A.cyl(g, (sx, 0.6, z), 0.15, 0.8, 6, "NB_Stone", top=False)
                A.bbox(g, (sx, 1.6, z), (0.55, 0.45, 0.55), "NB_Stone", 0.04, detail=detail)
                A.hip_roof(g, sx - 0.5, z - 0.5, sx + 0.5, z + 0.5, 1.85, 0.35, mat="NB_Stone", soffit="NB_Stone", fascia=False)
    # haiden: 6 x 5 hall, timber walls, raised floor, layered gable roof
    hy = 0.75
    A.bbox(g, (0, hy + 0.3, 4.5), (6.4, 0.3, 5.4), "NB_Teak", 0.02, detail=detail)
    for x in (-3.0, -1.0, 1.0, 3.0):
        for z in (2.0, 7.0):
            A.cyl(g, (x, hy + 0.45, z), 0.14, 3.0, (10, 6, 4)[detail], "NB_Paint", top=False)
    back = A.FacadeSpec(bay=2.0, win_w=0.5, win_h=0.5, sill=0.9, fh=3.0, wall="NB_WallBoard", spandrel="NB_WallBoard",
                        pier="NB_WallBoard", shutters=None, seed=95)
    for i, (a, b) in enumerate(A.poly_edges([(-2.8, 2.6), (2.8, 2.6), (2.8, 6.8), (-2.8, 6.8)])):
        if i == 0:
            A.facade(g, a, b, hy + 0.45, 1, A.FacadeSpec(bay=1.87, full_height=True, win_w=0.9, fh=3.0, reveal=0.1,
                                                          mullions=3, transom=True, frame_band=A.TRIM_TEAK,
                                                          wall="NB_WallBoard", spandrel="NB_WallBoard",
                                                          pier="NB_WallBoard", seed=96), detail)
        else:
            A.facade(g, a, b, hy + 0.45, 1, back, detail)
    A.gable_roof(g, -3.6, 1.2, 3.6, 7.8, hy + 3.45, 2.6, mat="NB_MetalRoof", gable_mat="NB_Teak", detail=detail, over=0.9)
    if detail < 2:
        A.bbox(g, (0, hy + 3.3, 1.0), (4.2, 0.2, 1.2), "NB_MetalRoof", 0.02, detail=detail)   # kohai porch roof
        A.bbox(g, (0, hy + 0.9, 1.6), (1.4, 0.7, 0.6), "NB_Teak", 0.02, detail=detail)      # offering box
        A.cyl(g, (0, hy + 2.9, 1.1), 0.08, 0.1, 8, "NB_Canvas", top=True)
        A.seg_box(g, (-2.6, hy + 3.1, 1.3), (2.6, hy + 3.1, 1.3), 0.12, 0.12, "NB_Canvas")   # shimenawa rope
    lot_rec("Nagisa_B_Shrine", 6.0, 10.0, 8.0)
    return g


BUILDERS = {
    "Nagisa_B_Condo1": condo1,
    "Nagisa_B_Condo2": condo2,
    "Nagisa_B_ResortHotel2": resort2,
    "Nagisa_B_Boutique1": boutique1,
    "Nagisa_B_Boutique2": boutique2,
    "Nagisa_B_HouseA": house_a,
    "Nagisa_B_HouseB": house_b,
    "Nagisa_B_HouseC": house_c,
    "Nagisa_B_MarinaClub": marina_club,
    "Nagisa_B_SurfShop": surf_shop,
    "Nagisa_B_BeachBar": beach_bar,
    "Nagisa_B_Gelato": gelato_kiosk,
    "Nagisa_B_ShaveIce": shave_ice,
    "Nagisa_B_Konbini": konbini,
    "Nagisa_B_Shrine": shrine,
}
for _st, _signs in SHOP_SIGNS.items():
    for _k, _s in enumerate(_signs):
        BUILDERS["Nagisa_B_Shop%s_%d" % (_st, _s)] = shophouse(_st, _s, [0, 1, 2, 3, 4, 5][(_s + _k) % 6])


def main():
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(BUILDERS)
    fp_path = os.path.join(C.OUT, "Nagisa_B_Footprints.json")
    if os.path.exists(fp_path):
        with open(fp_path) as f:
            FOOT.update(json.load(f))
    log = []
    for name in want:
        counts = C.build_lods(name, BUILDERS[name], name + ".glb")
        FOOT.setdefault(name, {})["tris"] = counts
        log.append((name, counts))
    with open(fp_path, "w") as f:
        json.dump(FOOT, f, indent=1)
    for n, c in log:
        print("[nagisa-b] %-26s %s" % (n, " / ".join("{:,}".format(x) for x in c)))


main()

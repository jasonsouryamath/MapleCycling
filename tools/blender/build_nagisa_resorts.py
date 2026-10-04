"""
NAGISA BAY NB2 pass 3 - the RESORT STRIP: four more individually designed resort hotels, so the beach
strip has 8 DIFFERENT silhouettes (hero curved tower, 3-part slab ResortHotel2, + these four, + the two
boutique hotels and two condos from build_nagisa_buildings.py).

  Nagisa_B_ResortTerrace   "Coral Terrace Resort"  9 storeys stepping back 2.6 m per floor toward the road,
                           a planted glass-railed terrace + pool deck on every setback, sea-face
                           full-height glazing, rooftop infinity pool + bar.
  Nagisa_B_ResortTwin      "Twin Palms"            two 18-storey chamfered towers on a 3-storey podium,
                           joined by a glazed SKY BRIDGE (rooftop bar) at storeys 14-15, crown lamp rings.
  Nagisa_B_ResortBungalow  "Hinata Bungalows"      low-rise garden resort: 2-storey hip-roof lobby pavilion,
                           lagoon pool with swim-up bar, 10 thatch / tile bungalows with decks + plunge
                           pools, boardwalks, cabanas.
  Nagisa_B_ResortCrescent  "Shiosai Crescent"      16-storey concave crescent (sea face wraps a pool court),
                           stepped roofline, continuous wavy balcony ribbons with fins.

Local frame: origin = ground centre at the front door level, FRONT (sea face / street face) = local -z,
exactly like build_nagisa_buildings.py; a below-grade plinth (PLINTH_D) lets C# seat it on sloping ground.
Run: blender.exe -b -P tools/blender/build_nagisa_resorts.py [-- Name ...]
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


# ================================================================== shared pieces
def pool(g, x0, z0, x1, z1, y, detail, loungers=True, rng=None, parasols=False):
    """Pool set into a stone deck: coping, tile floor, translucent water, loungers."""
    rng = rng or random.Random(5)
    rim = 0.35
    A.qn(g, [(x0 - 0.2, y + 0.04, z0 - 0.2), (x1 + 0.2, y + 0.04, z0 - 0.2), (x1 + 0.2, y + 0.04, z1 + 0.2),
             (x0 - 0.2, y + 0.04, z1 + 0.2)], (0, 1, 0), "NB_PoolTile")
    for (a, b, c, d) in ((x0 - rim, z0 - rim, x1 + rim, z0), (x0 - rim, z1, x1 + rim, z1 + rim),
                         (x0 - rim, z0, x0, z1), (x1, z0, x1 + rim, z1)):
        A.bbox(g, ((a + c) / 2, y + 0.11, (b + d) / 2), (c - a, 0.22, d - b), "NB_Stone", 0.03, detail=detail)
    A.qn(g, [(x0, y + 0.17, z0), (x1, y + 0.17, z0), (x1, y + 0.17, z1), (x0, y + 0.17, z1)], (0, 1, 0), "NB_PoolWater")
    if loungers and detail < 2:
        n = max(2, int((x1 - x0) / 3.0))
        for k in range(n):
            x = x0 + (x1 - x0) * (k + 0.5) / n
            for zz, yaw in ((z0 - 1.6, 0.0), (z1 + 1.6, math.pi)):
                if rng.random() < 0.8:
                    lounger(g, (x, y + 0.02, zz), yaw, detail)
            if parasols and k % 2 == 0 and detail == 0:
                parasol(g, (x + 1.4, y + 0.02, z1 + 2.8), detail)


def lounger(g, c, yaw, detail):
    A.bbox(g, add(c, (0, 0.28, 0)), (0.7, 0.12, 1.9), "NB_Canvas", 0.03, yaw, detail=detail)
    if detail == 0:
        A.bbox(g, add(c, C.rot_y((0, 0.45, 0.78), yaw)), (0.7, 0.08, 0.6), "NB_Canvas", 0.02, yaw)
        for sx in (-0.3, 0.3):
            for sz in (-0.8, 0.8):
                A.bbox(g, add(c, C.rot_y((sx, 0.12, sz), yaw)), (0.05, 0.24, 0.05), "NB_Trim", 0, yaw,
                       uvs_band=A.TRIM_BLACK)


def parasol(g, c, detail):
    A.cyl(g, c, 0.04, 2.5, 6, "NB_Trim", top=False)
    A.cyl(g, add(c, (0, 2.1, 0)), 1.5, 0.45, (12, 8, 6)[detail], "NB_Canvas", top=True, r_top=0.06)


def crown_lamp(g, poly, y, detail, h=0.18):
    """Thin emissive LED ring around the crown (NB_Lamp is emissive in Unity)."""
    if detail >= 2:
        return
    for a, b in A.poly_edges(poly):
        F = A.Frame(a, b)
        A._frame_box(g, F, F.P(F.L / 2, y, 0.05), F.L, h, 0.1, None, mat="NB_Lamp")


def porte_cochere(g, cx, zf, w, depth, h, sign, detail, drop_mat="NB_Accent"):
    """Entrance canopy projecting from a front (-z) face at zf toward -z, slim bronze columns, sign band."""
    A.bbox(g, (cx, h, zf - depth / 2), (w, 0.55, depth), drop_mat, 0.06, detail=detail)
    A.qn(g, [(cx - w / 2 + 0.2, h - 0.29, zf - depth + 0.2), (cx + w / 2 - 0.2, h - 0.29, zf - depth + 0.2),
             (cx + w / 2 - 0.2, h - 0.29, zf - 0.2), (cx - w / 2 + 0.2, h - 0.29, zf - 0.2)], (0, -1, 0), "NB_Teak")
    for x in (cx - w / 2 + 0.7, cx + w / 2 - 0.7):
        A.cyl(g, (x, 0, zf - depth + 0.7), 0.2, h - 0.3, (14, 8, 4)[detail], "NB_Bronze", top=False)
    if sign is not None:
        F = A.Frame((cx - w / 2, zf - depth - 0.02), (cx + w / 2, zf - depth - 0.02))
        sw = min(w - 1.0, 9.0)
        A.sign_board(g, F, (w - sw) / 2, (w + sw) / 2, h - 0.15, h + 0.55, 0.0, sign, detail)


def glass_fins(g, F, nb, y0, y1, depth, detail, mat="NB_Accent"):
    """Vertical brise-soleil fins at every bay line between y0..y1 (shadow rhythm)."""
    if detail >= 2:
        return
    bw = F.L / nb
    for k in range(nb + 1):
        A._frame_box(g, F, F.P(k * bw, (y0 + y1) / 2, depth / 2), 0.14, y1 - y0, depth, None, mat=mat,
                     bev=0.02 if detail == 0 else 0)


def lobby_front(g, poly_front, h, detail, sign_k=None, mat="NB_Stone"):
    a, b = poly_front
    sp = A.FacadeSpec(bay=4.2, full_height=True, win_w=0.88, fh=h, reveal=0.5, mullions=2, transom=True,
                      frame_band=A.TRIM_BRONZE, wall=mat, spandrel=mat, pier=mat, seed=61, glass="NB_SkyGlass")
    return A.facade(g, a, b, 0.0, 1, sp, detail)


# ================================================================== 1. TERRACE RESORT
def resort_terrace(detail):
    g = Geo()
    rng = random.Random(411)
    fh = 3.3
    hx, z0, z1 = 38.0, -18.0, 18.0
    step = 2.6
    nfl = 9
    base = 6.6                                     # 2-storey lobby / restaurant podium, full footprint
    poly = [(-hx, z0), (hx, z0), (hx, z1), (-hx, z1)]
    A.plinth(g, poly, 0.0, PLINTH_D, detail)
    # podium
    lob = A.FacadeSpec(bay=4.2, full_height=True, win_w=0.88, fh=base, reveal=0.5, mullions=2, transom=True,
                       frame_band=A.TRIM_BRONZE, wall="NB_Stone", spandrel="NB_Stone", pier="NB_Stone", seed=61)
    blank = A.FacadeSpec(bay=4.2, win_w=0.55, win_h=0.5, sill=2.2, fh=base, reveal=0.3, wall="NB_Stone",
                         spandrel="NB_Stone", pier="NB_Stone", seed=62)
    for i, (a, b) in enumerate(A.poly_edges(poly)):
        A.facade(g, a, b, 0.0, 1, lob if i == 0 else blank, detail)
    A.floor_slab_edge(g, poly, base, detail, proj=0.5, mat="NB_Stone")
    porte_cochere(g, 0.0, z0, 16.0, 7.0, 4.6, A.SIGN_CORAL_TERRACE, detail)
    # stepped guest floors
    face = A.FacadeSpec(bay=3.8, full_height=True, win_w=0.82, fh=fh, reveal=0.28, mullions=1, transom=True,
                        frame_band=A.TRIM_WHITE, wall="NB_Wall", spandrel="NB_Wall", pier="NB_Accent", seed=63)
    side = A.FacadeSpec(bay=3.2, win_w=0.5, win_h=0.5, sill=0.95, fh=fh, reveal=0.24, mullions=1, louvres=True,
                        frame_band=A.TRIM_WHITE, seed=64)
    back = A.FacadeSpec(bay=3.8, win_w=0.4, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, louvres=True, seed=65)
    ytop = base
    prev_front = z0
    for f in range(nfl):
        zf = z0 + step * (f + 1)
        pp = [(-hx + 0.5, zf), (hx - 0.5, zf), (hx - 0.5, z1 - 0.6), (-hx + 0.5, z1 - 0.6)]
        y = base + f * fh
        edges = A.poly_edges(pp)
        specs = [face, side, back, side]
        frames = [A.facade(g, a, b, y, 1, specs[i], detail) for i, (a, b) in enumerate(edges)]
        F = frames[0]
        # terrace in front of this floor (roof of the floor below): deck, glass rail, planter, furniture
        nb = max(1, int(round(F.L / 3.8)))
        bw = F.L / nb
        for k in range(nb):
            A.balcony(g, F, k * bw + 0.04, (k + 1) * bw - 0.04, y, step, "glass", detail, rng, furnish=True,
                      plants=True, side_walls=False, slab_mat="NB_DeckStone", rail_band=A.TRIM_WHITE)
        if detail < 2:
            A.roof_planter(g, -hx + 1.0, zf - step + 0.1, -hx + 5.0, zf - step + 0.9, y, detail)
            A.roof_planter(g, hx - 5.0, zf - step + 0.1, hx - 1.0, zf - step + 0.9, y, detail)
        A.floor_slab_edge(g, pp, y + fh, detail, proj=0.1)
        ytop = y + fh
        prev_front = zf
    # roof: infinity pool + bar pergola on the top terrace
    zf = z0 + step * nfl
    pp = [(-hx + 0.5, zf), (hx - 0.5, zf), (hx - 0.5, z1 - 0.6), (-hx + 0.5, z1 - 0.6)]
    A.parapet(g, pp, ytop, 1.1, detail, roof_mat="NB_DeckStone")
    pool(g, -hx + 8.0, zf + 1.6, -hx + 30.0, zf + 6.4, ytop, detail, rng=rng, parasols=True)
    if detail < 2:
        A.pergola(g, hx - 22.0, zf + 1.2, hx - 6.0, zf + 6.0, ytop, 3.0, detail)
        if detail == 0:
            for k in range(4):
                A.table(g, (hx - 20.0 + k * 3.6, ytop + 0.04, zf + 3.6), 0.45, 1.05, 10)
        A.overrun(g, (0.0, ytop, z1 - 5.0), (6.0, 3.2, 5.0), detail)
        for k in range(2):
            A.water_tank(g, (-3.0 + k * 4.0, ytop + 3.3, z1 - 3.0), detail)
        A.solar_array(g, (hx - 18.0, ytop, z1 - 5.0), 8, 2, 0.0, detail)
    crown_lamp(g, pp, ytop + 1.2, detail)
    lot_rec("Nagisa_B_ResortTerrace", hx, 18.0, ytop + 4.0, lot=[hx + 4, 26.0], sea_front=True, front=-25.0, back=18.0, hw=hx)
    return g


# ================================================================== 2. TWIN TOWERS + SKY BRIDGE
def chamfer_poly(cx, cz, hx, hz, c):
    return [(cx - hx + c, cz - hz), (cx + hx - c, cz - hz), (cx + hx, cz - hz + c), (cx + hx, cz + hz - c),
            (cx + hx - c, cz + hz), (cx - hx + c, cz + hz), (cx - hx, cz + hz - c), (cx - hx, cz - hz + c)]


def resort_twin(detail):
    g = Geo()
    rng = random.Random(421)
    fh = 3.2
    hx, hz = 49.0, 16.0
    base = 10.4                                       # 3-storey podium (arrival, ballroom, restaurants)
    poly = [(-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)]
    A.plinth(g, poly, 0.0, PLINTH_D, detail)
    lob = A.FacadeSpec(bay=4.4, full_height=True, win_w=0.9, fh=base, reveal=0.55, mullions=2, transom=True,
                       frame_band=A.TRIM_BRONZE, wall="NB_Stone", spandrel="NB_Stone", pier="NB_Stone", seed=71)
    blank = A.FacadeSpec(bay=4.4, win_w=0.5, win_h=0.5, sill=3.0, fh=base, reveal=0.3, wall="NB_Stone",
                         spandrel="NB_Stone", pier="NB_Stone", seed=72)
    for i, (a, b) in enumerate(A.poly_edges(poly)):
        A.facade(g, a, b, 0.0, 1, lob if i == 0 else blank, detail)
    A.floor_slab_edge(g, poly, base, detail, proj=0.6, mat="NB_Stone")
    A.parapet(g, poly, base, 1.1, detail, mat="NB_Stone", roof_mat="NB_DeckStone")
    porte_cochere(g, 0.0, -hz, 20.0, 8.0, 6.0, A.SIGN_TWIN_PALMS, detail)
    # pool terrace on the podium roof between the towers (sea side)
    pool(g, -8.0, -hz + 3.0, 8.0, -hz + 9.0, base + 0.0, detail, rng=rng, parasols=True)
    nfl = 18
    cx = (-26.0, 26.0)
    thx, thz, ch = 12.0, 8.0, 3.0
    face = A.FacadeSpec(bay=3.4, full_height=True, win_w=0.84, fh=fh, reveal=0.26, mullions=1, transom=True,
                        frame_band=A.TRIM_WHITE, wall="NB_Wall", spandrel="NB_Wall", pier="NB_Accent", seed=73)
    side = A.FacadeSpec(bay=3.2, win_w=0.52, win_h=0.52, sill=0.95, fh=fh, reveal=0.24, mullions=1,
                        frame_band=A.TRIM_WHITE, louvres=True, seed=74)
    back = A.FacadeSpec(bay=3.2, win_w=0.4, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, seed=75)
    corner = A.FacadeSpec(bay=3.2, win_w=0.0001, win_h=0.0001, sill=1.0, fh=fh, reveal=0.0, seed=76)
    for ti, x0 in enumerate(cx):
        tp = chamfer_poly(x0, 0.0, thx, thz, ch)
        edges = A.poly_edges(tp)
        # edge order: 0 front, 1 front-right chamfer, 2 right, 3 back-right chamfer, 4 back, 5 .., 6 left, 7 front-left
        specs = [face, corner, side, corner, back, corner, side, corner]
        frames = []
        for i, (a, b) in enumerate(edges):
            if specs[i] is corner:
                # solid chamfer panel (blank wall) - cheaper than empty openings and reads as a structural core
                Fc = A.Frame(a, b)
                A.qn(g, [Fc.P(0, base), Fc.P(Fc.L, base), Fc.P(Fc.L, base + nfl * fh), Fc.P(0, base + nfl * fh)], Fc.n,
                     "NB_Accent")
                frames.append(Fc)
            else:
                frames.append(A.facade(g, a, b, base, nfl, specs[i], detail,
                                       loggia=(lambda f, k, ti=ti: (f + k + ti) % 3 == 1) if i == 0 else None))
        F = frames[0]
        nb = max(1, int(round(F.L / 3.4)))
        bw = F.L / nb
        for f in range(nfl):
            y = base + f * fh
            for k in range(nb):
                if (f + k + ti) % 3 == 1:
                    continue          # this bay is a recessed loggia (drawn by the facade)
                A.balcony(g, F, k * bw + 0.05, (k + 1) * bw - 0.05, y, 2.1, "glass", detail, rng, furnish=True,
                          plants=True, side_walls=True, rail_band=A.TRIM_WHITE, solid_mat="NB_Accent")
            A.floor_slab_edge(g, tp, y + fh, detail, proj=0.1)
        glass_fins(g, F, nb, base + 0.0, base + nfl * fh, 0.5, detail) if detail == 0 else None
        ytop = base + nfl * fh
        # crown: setback box + lamp ring + mast
        A.roof_deck_edge(g, tp, ytop, detail, mat="NB_Accent", h=0.6, proj=0.5)
        ip = chamfer_poly(x0, 0.0, thx - 3.0, thz - 2.5, 2.0)
        A.parapet(g, ip, ytop + 0.6, 0.5, detail, roof_mat="NB_DeckStone")
        sk = A.FacadeSpec(bay=3.3, full_height=True, win_w=0.92, fh=3.6, reveal=0.1, mullions=1,
                          frame_band=A.TRIM_BRONZE, wall="NB_Accent", spandrel="NB_Accent", pier="NB_Accent", seed=77,
                          glass="NB_SkyGlass")
        for i, (a, b) in enumerate(A.poly_edges(ip)):
            A.facade(g, a, b, ytop + 0.6, 1, sk, detail)
        A.roof_deck_edge(g, ip, ytop + 4.2, detail, mat="NB_Accent", h=0.5, proj=0.8)
        crown_lamp(g, ip, ytop + 4.4, detail)
        if detail < 2:
            A.overrun(g, (x0, ytop + 4.7, 1.5), (4.0, 3.0, 4.0), detail)
            A.cyl(g, (x0, ytop + 4.7, -1.0), 0.18, 11.0, 8 if detail == 0 else 5, "NB_Trim", top=True)
        else:
            A.bbox(g, (x0, ytop + 6.0, 0.0), (6.0, 3.0, 6.0), "NB_Accent", 0, detail=2)
    # SKY BRIDGE: glazed 2-storey box + underside truss between the towers at floors 14-15
    yb = base + 13 * fh
    bx0, bx1 = cx[0] + thx, cx[1] - thx
    bz0, bz1 = -5.0, 5.0
    bp = [(bx0, bz0), (bx1, bz0), (bx1, bz1), (bx0, bz1)]
    bridge = A.FacadeSpec(bay=3.4, full_height=True, win_w=0.95, fh=2 * fh - 0.8, reveal=0.12, mullions=2,
                          frame_band=A.TRIM_BRONZE, wall="NB_Accent", spandrel="NB_Accent", pier="NB_Accent", seed=78,
                          glass="NB_SkyGlass")
    for i, (a, b) in enumerate(A.poly_edges(bp)):
        if i in (1, 3):
            continue                                  # ends dock into the towers
        A.facade(g, a, b, yb + 0.5, 1, bridge, detail)
    A.bbox(g, ((bx0 + bx1) / 2, yb + 0.25, 0.0), (bx1 - bx0 + 0.4, 0.5, 10.6), "NB_Accent", 0.05, detail=detail)
    A.bbox(g, ((bx0 + bx1) / 2, yb + 2 * fh - 0.15, 0.0), (bx1 - bx0 + 0.4, 0.5, 10.8), "NB_Accent", 0.05, detail=detail)
    # roof terrace of the bridge: pergola bar
    A.qn(g, [(bx0, yb + 2 * fh + 0.12, bz0), (bx1, yb + 2 * fh + 0.12, bz0), (bx1, yb + 2 * fh + 0.12, bz1),
             (bx0, yb + 2 * fh + 0.12, bz1)], (0, 1, 0), "NB_DeckStone")
    if detail < 2:
        A.pergola(g, bx0 + 2.0, bz0 + 1.0, bx1 - 2.0, bz1 - 1.0, yb + 2 * fh + 0.12, 2.8, detail)
        # tapered steel truss under the span
        for sgn in (-1, 1):
            A.seg_box(g, (bx0 + 0.5, yb, sgn * 4.6), ((bx0 + bx1) / 2, yb - 3.2, sgn * 4.6), 0.28, 0.28, "NB_Trim",
                      band=A.TRIM_BLACK)
            A.seg_box(g, (bx1 - 0.5, yb, sgn * 4.6), ((bx0 + bx1) / 2, yb - 3.2, sgn * 4.6), 0.28, 0.28, "NB_Trim",
                      band=A.TRIM_BLACK)
        Fb = A.Frame((bx0 + 1.0, bz0 - 0.1), (bx1 - 1.0, bz0 - 0.1))
        A.sign_board(g, Fb, 3.0, Fb.L - 3.0, yb + 2 * fh - 0.1, yb + 2 * fh + 0.9, 0.0, A.SIGN_TWIN_PALMS, detail)
    # podium sign
    lot_rec("Nagisa_B_ResortTwin", hx, hz, base + nfl * fh + 14.0, lot=[hx + 4, hz + 12], sea_front=True, front=-hz - 8.0, back=hz, hw=hx)
    return g


# ================================================================== 3. BUNGALOW RESORT
def ellipse(cx, cz, rx, rz, n=28):
    return [(cx + rx * math.cos(i / n * math.tau), cz + rz * math.sin(i / n * math.tau)) for i in range(n)]


def boards(g, x0, z0, x1, z1, y, mat="NB_Boardwalk", along_x=True):
    """Flat boardwalk / deck quad (UV by world metres through TILE)."""
    A.qn(g, [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], (0, 1, 0), mat)


def bungalow(g, c, yaw, kind, detail, rng, face_pool=1):
    """One 9 x 6.4 m bungalow with a pool-side glazed front, porch deck + plunge pool. c = ground centre,
    local -z of the unit faces the pool (rotated by yaw). kind 0 = thatch gable, 1 = tile hip, 2 = metal gable."""
    sub = Geo()
    hx, hz = 4.5, 3.2
    wall = "NB_WallBoard" if kind != 1 else "NB_Wall"
    poly = A.footprint_rect(hx, hz)
    A.plinth(sub, A.footprint_rect(hx + 0.2, hz + 0.2), 0.0, 0.7, detail)
    A.bbox(sub, (0, 0.32, 0), (2 * hx + 0.5, 0.64, 2 * hz + 0.5), "NB_Stone", 0.04, detail=detail)   # raised stone base
    fr = A.FacadeSpec(bay=4.5, full_height=True, win_w=0.9, fh=3.0, reveal=0.22, mullions=3, frame_band=A.TRIM_TEAK,
                      wall=wall, spandrel=wall, pier="NB_Teak", seed=81 + kind)
    sd = A.FacadeSpec(bay=3.2, win_w=0.45, win_h=0.5, sill=1.0, fh=3.0, reveal=0.2, frame_band=A.TRIM_TEAK,
                      wall=wall, seed=85 + kind, shutters=A.TRIM_TEAK if kind == 1 else None)
    specs = [fr, sd, sd, sd]
    # lift the walls on the stone base
    for i, (a, b) in enumerate(A.poly_edges(poly)):
        A.facade(sub, a, b, 0.64, 1, specs[i], detail)
    y = 0.64 + 3.0
    if kind == 0:
        A.gable_roof(sub, -hx, -hz, hx, hz, y, 2.9, mat="NB_Thatch", gable_mat="NB_Teak", detail=detail, over=0.9)
    elif kind == 1:
        A.hip_roof(sub, -hx - 0.8, -hz - 0.8, hx + 0.8, hz + 0.8, y, 2.2, mat="NB_RoofTile", detail=detail)
    else:
        A.gable_roof(sub, -hx, -hz, hx, hz, y, 2.2, mat="NB_MetalRoof", gable_mat="NB_WallBoard", detail=detail, over=0.8)
    # porch deck + pergola + plunge pool
    boards(sub, -hx - 0.5, -hz - 3.6, hx + 0.5, -hz - 0.25, 0.7, "NB_Boardwalk")
    if detail < 2:
        A.pergola(sub, -hx + 0.3, -hz - 3.4, hx - 0.3, -hz - 0.4, 0.7, 2.7, detail)
        A.bbox(sub, (hx - 1.8, 0.7 + 0.12, -hz - 2.0), (2.6, 0.24, 1.8), "NB_Stone", 0.03, detail=detail)
        A.qn(sub, [(hx - 3.0, 0.7 + 0.25, -hz - 2.8), (hx - 0.6, 0.7 + 0.25, -hz - 2.8), (hx - 0.6, 0.7 + 0.25, -hz - 1.2),
                   (hx - 3.0, 0.7 + 0.25, -hz - 1.2)], (0, 1, 0), "NB_PoolWater")
        if detail == 0:
            lounger(sub, (-hx + 1.5, 0.72, -hz - 1.8), math.pi / 2, detail)
            A.potted_plant(sub, (-hx - 0.2, 0.7, -hz - 3.2), detail, big=True)
    g.merge(sub, offset=c, yaw=yaw)


def resort_bungalow(detail):
    g = Geo()
    rng = random.Random(431)
    hx, hz = 56.0, 42.0
    # ground: stone-skirted raised lawn plinth so C# can sit it on sloping ground
    plot = [(-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)]
    A.prism_poly(g, plot, -PLINTH_D, 0.04, "NB_Stone", top=True, top_mat="NB_Lawn")
    # lagoon pool (kidney) + wide deck
    lag = ellipse(0.0, -8.0, 22.0, 9.5, 26 if detail == 0 else 14)
    A.qn(g, [(-27.0, 0.07, -22.0), (27.0, 0.07, -22.0), (27.0, 0.07, 6.0), (-27.0, 0.07, 6.0)], (0, 1, 0), "NB_DeckStone")
    A.qn(g, [(p[0], 0.09, p[1]) for p in lag], (0, 1, 0), "NB_PoolTile")
    A.qn(g, [(p[0], 0.40, p[1]) for p in lag], (0, 1, 0), "NB_PoolWater")
    ringo = ellipse(0.0, -8.0, 22.35, 9.85, len(lag))
    if detail < 2:
        for i in range(len(lag)):
            a, b = ringo[i], ringo[(i + 1) % len(ringo)]
            A.seg_box(g, (a[0], 0.22, a[1]), (b[0], 0.22, b[1]), 0.5, 0.44, "NB_Stone", bev=0.0)
    # swim-up bar on the lagoon: thatched hut on stilts with stools
    bx, bz = -9.0, -8.0
    A.bbox(g, (bx, 0.65, bz), (5.0, 1.0, 2.6), "NB_Teak", 0.04, detail=detail)
    A.hip_roof(g, bx - 3.4, bz - 2.6, bx + 3.4, bz + 2.6, 3.2, 1.8, mat="NB_Thatch", soffit="NB_Teak", detail=detail)
    if detail < 2:
        for sx in (-3.0, 3.0):
            for sz in (-2.2, 2.2):
                A.cyl(g, (bx + sx, 0.4, bz + sz), 0.11, 2.8, 6, "NB_Teak", top=False)
    # cabanas + loungers on the sea-side deck
    for k in range(5):
        x = -22.0 + k * 11.0
        if detail < 2:
            A.pergola(g, x, -22.5, x + 4.0, -18.5, 0.07, 2.5, detail)
            A.bbox(g, (x + 2.0, 0.4, -20.5), (3.0, 0.4, 1.6), "NB_Canvas", 0.05, detail=detail)
            A.qn(g, [(x - 0.3, 2.7, -22.8), (x + 4.3, 2.7, -22.8), (x + 4.3, 2.7, -18.2), (x - 0.3, 2.7, -18.2)],
                 (0, 1, 0), "NB_Canvas")
    if detail < 2:
        for k in range(9):
            lounger(g, (-24.0 + k * 6.0, 0.08, -13.5 - (k % 2) * 0.0), 0.0 if k % 2 == 0 else math.pi, detail) if k % 3 else None
    # main lobby pavilion (back, +z): 2 storeys, hip roof with big eaves, open veranda on the pool side
    px, pz, phx, phz = 0.0, 27.0, 18.0, 8.0
    pc = A.footprint_rect(phx, phz)
    sub = Geo()
    A.plinth(sub, A.footprint_rect(phx + 0.3, phz + 0.3), 0.0, 0.8, detail)
    A.bbox(sub, (0, 0.4, 0), (2 * phx + 1.0, 0.8, 2 * phz + 1.0), "NB_Stone", 0.05, detail=detail)
    lobspec = A.FacadeSpec(bay=4.5, full_height=True, win_w=0.9, fh=4.2, reveal=0.4, mullions=2, transom=True,
                           frame_band=A.TRIM_BRONZE, wall="NB_Wall", spandrel="NB_Wall", pier="NB_Teak", seed=91,
                           glass="NB_SkyGlass")
    up = A.FacadeSpec(bay=4.0, win_w=0.55, win_h=0.55, sill=0.9, fh=3.3, reveal=0.25, mullions=1, shutters=A.TRIM_TEAK,
                      frame_band=A.TRIM_WHITE, wall="NB_Wall", seed=92)
    sidespec = A.FacadeSpec(bay=3.6, win_w=0.5, win_h=0.5, sill=1.0, fh=3.3, reveal=0.24, frame_band=A.TRIM_WHITE,
                            wall="NB_Wall", seed=93)
    for i, (a, b) in enumerate(A.poly_edges(pc)):
        A.facade(sub, a, b, 0.8, 1, lobspec if i == 0 else sidespec, detail)
    A.floor_slab_edge(sub, pc, 0.8 + 4.2, detail, proj=0.4)
    for i, (a, b) in enumerate(A.poly_edges(pc)):
        A.facade(sub, a, b, 0.8 + 4.2, 1, up if i == 0 else sidespec, detail)
    Fv = A.Frame(pc[0], pc[1])
    for k in range(int(Fv.L / 4.0)):                 # upper-floor balcony with timber rail
        A.balcony(sub, Fv, k * 4.0 + 0.1, (k + 1) * 4.0 - 0.1, 0.8 + 4.2, 1.8, "bars", detail, rng, slab_mat="NB_Teak",
                  rail_band=A.TRIM_TEAK, furnish=True, plants=True)
    A.hip_roof(sub, -phx - 2.4, -phz - 2.4, phx + 2.4, phz + 2.4, 0.8 + 4.2 + 3.3, 4.2, mat="NB_RoofTile", detail=detail)
    # veranda + canopy toward the pool
    if detail < 2:
        A.pergola(sub, -phx + 2.0, -phz - 6.0, phx - 2.0, -phz - 0.4, 0.8, 3.2, detail)
        Fs = A.Frame((-phx + 2.0, -phz - 0.8), (phx - 2.0, -phz - 0.8))
        A.sign_board(sub, Fs, 5.0, Fs.L - 5.0, 0.8 + 3.2 - 0.9, 0.8 + 3.2 + 0.1, 0.0, A.SIGN_HINATA_BUNGALOWS, detail)
    g.merge(sub, offset=(px, 0.04, pz))
    # bungalows: two rows flanking the lagoon + a front row facing the sea, each facing the pool centre
    spots = []
    for k in range(4):
        spots.append((-40.0 + (k % 2) * 0.0, -22.0 + k * 9.5, math.pi / 2 * -1.0))        # west row faces +x
    for k in range(4):
        spots.append((40.0, -22.0 + k * 9.5, math.pi / 2))                                 # east row faces -x
    spots += [(-26.0, 28.0, 0.0), (26.0, 28.0, 0.0)]                                       # lobby flank units
    for n, (x, z, yaw) in enumerate(spots):
        # local -z faces the pool: rotate so that -z points toward the lagoon centre
        ang = math.atan2(-(0.0 - x), -(-8.0 - z))
        if n >= 8:
            ang = 0.0
        bungalow(g, (x, 0.04, z), ang, n % 3, detail, rng)
    # boardwalk paths: lobby -> pool -> bungalow rows
    boards(g, -2.0, 6.0, 2.0, 17.8, 0.1, "NB_Boardwalk")
    for k in range(4):
        z = -22.0 + k * 9.5
        boards(g, -34.5, z - 0.9, -27.0, z + 0.9, 0.1, "NB_Boardwalk")
        boards(g, 27.0, z - 0.9, 34.5, z + 0.9, 0.1, "NB_Boardwalk")
    # low hedge + planter edging (hedge boxes) and lamps along the boundary front
    if detail < 2:
        for k in range(14):
            x = -52.0 + k * 8.0
            A.bbox(g, (x, 0.45, -hz + 1.2), (5.5, 0.8, 1.0), "NB_Hedge", 0.06, detail=detail)
    # entrance gate piers on the sea (front) edge + sign
    for sx in (-6.0, 6.0):
        A.bbox(g, (sx, 1.3, -hz + 0.4), (1.0, 2.6, 1.0), "NB_Stone", 0.06, detail=detail)
    Fg = A.Frame((-6.0, -hz + 0.2), (6.0, -hz + 0.2))
    A.sign_board(g, Fg, 1.2, 10.8, 1.0, 2.3, 0.0, A.SIGN_HINATA_BUNGALOWS, detail)
    lot_rec("Nagisa_B_ResortBungalow", hx, hz, 12.0, lot=[hx + 4, hz + 6], sea_front=True, front=-hz, back=hz, hw=hx)
    return g


# ================================================================== 4. CRESCENT TOWER
def arc_poly(R, D, T, n, zc):
    """Concave-to-sea crescent footprint: front arc radius R (centre on the -z / sea side), back arc R + D."""
    front = [(R * math.sin(-T + 2 * T * i / n), zc + R * math.cos(-T + 2 * T * i / n)) for i in range(n + 1)]
    back = [((R + D) * math.sin(T - 2 * T * i / n), zc + (R + D) * math.cos(T - 2 * T * i / n)) for i in range(n + 1)]
    return front + back


def resort_crescent(detail):
    g = Geo()
    rng = random.Random(441)
    fh = 3.2
    R, D, T = 95.0, 17.0, 0.40
    zc = -R + 6.0                      # front arc apex at z = +6, ends toward the sea at z = zc + R cos T
    nseg = 12 if detail < 2 else 6
    base = 6.4
    poly = arc_poly(R, D, T, nseg, zc)
    A.plinth(g, poly, 0.0, PLINTH_D, detail)
    # podium (restaurants + lobby) along the whole crescent
    lob = A.FacadeSpec(bay=4.2, full_height=True, win_w=0.88, fh=base, reveal=0.5, mullions=2, transom=True,
                       frame_band=A.TRIM_BRONZE, wall="NB_Stone", spandrel="NB_Stone", pier="NB_Stone", seed=101)
    blank = A.FacadeSpec(bay=4.2, win_w=0.5, win_h=0.5, sill=2.4, fh=base, reveal=0.3, wall="NB_Stone",
                         spandrel="NB_Stone", pier="NB_Stone", seed=102)
    edges = A.poly_edges(poly)
    for i, (a, b) in enumerate(edges):
        A.facade(g, a, b, 0.0, 1, lob if i < nseg else blank, detail)
    A.floor_slab_edge(g, poly, base, detail, proj=0.5, mat="NB_Stone")
    A.parapet(g, poly, base, 1.1, detail, mat="NB_Stone", roof_mat="NB_DeckStone")
    # tower body: centre third 10 floors, wings 16 floors
    nlow, nhigh = 10, 16
    face = A.FacadeSpec(bay=3.6, full_height=True, win_w=0.84, fh=fh, reveal=0.26, mullions=1, transom=True,
                        frame_band=A.TRIM_WHITE, wall="NB_Wall", spandrel="NB_Wall", pier="NB_Accent", seed=103)
    back = A.FacadeSpec(bay=3.4, win_w=0.42, win_h=0.5, sill=1.0, fh=fh, reveal=0.2, louvres=True, seed=104)
    end = A.FacadeSpec(bay=3.0, win_w=0.5, win_h=0.5, sill=0.95, fh=fh, reveal=0.22, mullions=1, seed=105)
    ytop_low = base + nlow * fh
    ytop_hi = base + nhigh * fh
    # one inset polygon for everything above the podium
    ip = arc_poly(R + 0.6, D - 1.4, T - 0.01, nseg, zc)
    iedges = A.poly_edges(ip)
    third = nseg // 3
    for i, (a, b) in enumerate(iedges):
        if i < nseg:                                   # front (sea) arc segment i
            hi = (i < third) or (i >= nseg - third)
            fl = nhigh if hi else nlow
            F = A.facade(g, a, b, base, fl, face, detail, loggia=lambda f, k, i=i: (f + k + i) % 4 == 2)
            nb = max(1, int(round(F.L / 3.6)))
            bw = F.L / nb
            for f in range(fl):
                y = base + f * fh
                for k in range(nb):
                    if (f + k + i) % 4 == 2:
                        continue
                    kind = "glass" if (f + i) % 3 else "bars"
                    A.balcony(g, F, k * bw + 0.05, (k + 1) * bw - 0.05, y, 2.4, kind, detail, rng,
                              furnish=(f + k) % 3 == 0, plants=(f + k) % 4 == 0, side_walls=(f + k) % 2 == 0, rail_band=A.TRIM_WHITE, solid_mat="NB_Accent")
            if detail == 0:
                glass_fins(g, F, nb, base, base + fl * fh, 0.35, detail)
        elif i == nseg:                                 # right end wall
            A.facade(g, a, b, base, nhigh, end, detail)
        elif i < 2 * nseg + 1:                          # back arc (all floors by third, as the front)
            j = i - (nseg + 1)
            seg_from_right = j                          # back edges run right -> left
            hi = (seg_from_right < third) or (seg_from_right >= nseg - third)
            A.facade(g, a, b, base, nhigh if hi else nlow, back, detail)
        else:                                           # left end wall
            A.facade(g, a, b, base, nhigh, end, detail)
    # exposed wing faces above the low centre: the two inner end walls (wing flanks over the roof garden)
    fpts = [iedges[k][0] for k in range(nseg)] + [iedges[nseg - 1][1]]
    bpts = [iedges[nseg + 1 + m][0] for m in range(nseg)] + [iedges[2 * nseg][1]]
    A.facade(g, fpts[third], bpts[nseg - third], ytop_low, nhigh - nlow, end, detail)
    A.facade(g, bpts[third], fpts[nseg - third], ytop_low, nhigh - nlow, end, detail)
    for f in range(nhigh + 1):
        A.floor_slab_edge(g, ip, base + f * fh + 0.03, detail, proj=0.1) if f <= nlow else None
    # centre-third roof garden at nlow: deck + pool + planters
    cxs = (iedges[third][0][0], iedges[nseg - third - 1][1][0])
    zmid = zc + R * math.cos(0.0) - 2.0
    A.qn(g, [(cxs[0], ytop_low + 0.04, zc + (R + 0.6) * math.cos(0.15)), (cxs[1], ytop_low + 0.04, zc + (R + 0.6) * math.cos(0.15)),
             (cxs[1], ytop_low + 0.04, zc + R + D - 1.5), (cxs[0], ytop_low + 0.04, zc + R + D - 1.5)], (0, 1, 0), "NB_DeckStone")
    pool(g, -10.0, zc + R + 3.0 - 0.0, 10.0, zc + R + 7.5, ytop_low, detail, rng=rng, parasols=True)
    # wing roofs
    for si in range(nseg):
        hi = (si < third) or (si >= nseg - third)
        if not hi:
            continue
        a, b = iedges[si]
        F = A.Frame(a, b)
        A._frame_box(g, F, F.P(F.L / 2, ytop_hi + 0.3, 0.2), F.L + 0.6, 0.6, 0.8, None, mat="NB_Accent", bev=0.03 if detail == 0 else 0)
    # sky bar crown on the two wings (small glass pavilions) + sign
    if detail < 2:
        for si in (1, nseg - 2):
            a, b = iedges[si]
            F = A.Frame(a, b)
            mx = F.P(F.L / 2, ytop_hi + 0.6, -7.0)
            A.bbox(g, mx, (F.L - 0.6, 3.4, 4.0), "NB_Accent", 0.05, detail=detail)
            A.pergola(g, mx[0] - F.L / 2 + 0.5, mx[2] - 2.0, mx[0] + F.L / 2 - 0.5, mx[2] + 2.0, ytop_hi + 4.5, 2.4, detail) if False else None
        a, b = iedges[1]
        Fh = A.Frame(a, b)
        A.sign_board(g, Fh, 0.3, Fh.L - 0.3, ytop_hi + 1.0, ytop_hi + 2.6, 0.5, A.SIGN_SHIOSAI_CRESCENT, detail)
        a, b = iedges[nseg - 2]
        Fh = A.Frame(a, b)
        A.sign_board(g, Fh, 0.3, Fh.L - 0.3, ytop_hi + 1.0, ytop_hi + 2.6, 0.5, A.SIGN_SHIOSAI_CRESCENT, detail)
    crown_lamp(g, ip, ytop_hi + 0.9, detail)
    # porte-cochere at the apex
    porte_cochere(g, 0.0, zc + R, 18.0, 8.0, 5.2, A.SIGN_SHIOSAI_CRESCENT, detail)
    xs = R * math.sin(T)
    lot_rec("Nagisa_B_ResortCrescent", xs + 4.0, 24.0, ytop_hi + 6.0, lot=[xs + 8.0, 34.0], sea_front=True,
            front=-2.0, back=R * 0 + D + 6.0, hw=(R + D) * math.sin(T) + 1.0)
    return g


BUILDERS = {
    "Nagisa_B_ResortTerrace": resort_terrace,
    "Nagisa_B_ResortTwin": resort_twin,
    "Nagisa_B_ResortBungalow": resort_bungalow,
    "Nagisa_B_ResortCrescent": resort_crescent,
}


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

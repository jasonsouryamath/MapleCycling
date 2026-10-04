"""
NAGISA BAY (B5) - HERO: the Grand Shiokaze Resort (fictional) + its pool podium + the
boutique resort block across the road.

  Nagisa_HotelTower.glb     curved, west-stepped 30-storey tower on a 4-level lobby base,
                            balconies with glass balustrades + bronze rails on every floor,
                            stone fins, sky-bar crown with teak pergola and a crown sail,
                            porte-cochere with drive loop facing the road (+z).
  Nagisa_HotelPodium.glb    beach-side pool deck: stone plinth, infinity pool with a vanishing
                            edge toward the sea (-z), jacuzzi, cabanas, planters, balustrade,
                            broad steps down to the sand.
  Nagisa_BoutiqueResort.glb 9-storey terraced boutique block (white plaster, teak screens,
                            roof pergola), terraces facing the sea (-z).

Local frame (Unity coords): origin = pad centre at pad height; -z = sea, +z = road.
Also writes Models/Nagisa_HotelAnchors.json: palm / lounger / umbrella anchors in the same
local frame, consumed by NagisaBayEnvironment.Resort.cs.

Run: blender.exe -b -P tools/blender/build_nagisa_hotel.py
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo  # noqa: E402

# ------------------------------------------------------------------ tower parameters
# PROVISIONAL art direction numbers (storeys / radii / spans), tune freely.
FLOORS = 30               # tower storeys above the lobby base
FLOOR_H = 3.2             # floor-to-floor; 4 floors = one NB_Glass texture tile
BASE_H = 12.8             # lobby base (4 x 3.2)
R_FRONT = 160.0           # sea-face glass-line radius (convex toward -z)
DEPTH = 20.0              # glass line to back wall
BALC = 2.4                # balcony depth
APEX_Z = -8.0             # front apex z
THETA = 0.2625            # half span -> ~84 m long tower
CZ = APEX_Z + R_FRONT     # arc centre z (on +z side)


def arc(r, t0, t1, n):
    """Points (x, z) on the arc radius r about (0, CZ); t measured from -z (the apex),
    positive toward +x. Returned in the order t0 -> t1."""
    pts = []
    for i in range(n + 1):
        t = t0 + (t1 - t0) * i / n
        pts.append((r * math.sin(t), CZ - r * math.cos(t)))
    return pts


def west_theta(f):
    """West end steps back from floor 16 upward (terraced silhouette)."""
    return THETA - max(0, f - 16) * 0.0125


def seg_count(detail, span):
    per = (28, 12, 6)[detail]
    return max(2, int(round(per * span / (2 * THETA))))


def lerp2(a, b, f):
    return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)


def end_wall(g, a, b, y0, detail):
    """One storey of an end wall a->b (outward = right of travel): stone piers either side of a
    glazed slot with a stone spandrel + head, so the end elevations read as rooms, not a blank slab."""
    if detail == 2:
        g.ribbon([a, b], y0, y0 + FLOOR_H, "NB_Stone")
        return
    k = (0.0, 0.14, 0.40, 0.60, 0.86, 1.0)
    for i in range(5):
        p0, p1 = lerp2(a, b, k[i]), lerp2(a, b, k[i + 1])
        if i in (1, 3):   # two window bays
            g.ribbon([p0, p1], y0, y0 + 0.95, "NB_Stone")
            g.ribbon([p0, p1], y0 + 0.95, y0 + FLOOR_H - 0.35, "NB_Glass", v_tile=12.8)
            g.ribbon([p0, p1], y0 + FLOOR_H - 0.35, y0 + FLOOR_H, "NB_Stone")
        else:
            g.ribbon([p0, p1], y0, y0 + FLOOR_H, "NB_Stone")


def tower(detail):
    g = Geo()
    Rf, Rb = R_FRONT, R_FRONT - DEPTH
    # ---------------- lobby base (y 0..BASE_H): double-height glass front, stone piers
    n = seg_count(detail, 2 * THETA + 0.06)
    tb0, tb1 = -THETA - 0.03, THETA + 0.03
    front = arc(Rf + 7.0, tb0, tb1, n)
    back = arc(Rb - 4.0, tb0, tb1, n)
    # glass front (outward = -z side = right of travel when going +x along the front arc)
    g.ribbon(front, 0.0, BASE_H - 1.2, "NB_Glass", v_tile=12.8 * 1.25)
    g.ribbon(back, 0.0, BASE_H - 1.2, "NB_Glass", outward=False, v_tile=12.8 * 1.25)
    # stone band + roof of the base
    g.slab([(p[0], p[1]) for p in arc(Rf + 8.0, tb0, tb1, n)],
           [(p[0], p[1]) for p in arc(Rb - 5.0, tb0, tb1, n)], BASE_H - 1.2, BASE_H, "NB_Stone")
    g.ribbon(arc(Rb - 5.0, tb0, tb1, n), BASE_H - 1.2, BASE_H, "NB_Stone", outward=False)
    # end walls of the base
    for t, sgn in ((tb0, -1), (tb1, 1)):
        a = (math.sin(t) * (Rf + 8.0), CZ - math.cos(t) * (Rf + 8.0))
        b = (math.sin(t) * (Rb - 5.0), CZ - math.cos(t) * (Rb - 5.0))
        if sgn < 0:
            g.ribbon([b, a], 0.0, BASE_H, "NB_Stone")
        else:
            g.ribbon([a, b], 0.0, BASE_H, "NB_Stone")
    # stone piers on the lobby front
    if detail < 2:
        step = 2 if detail == 0 else 4
        for p in front[::step]:
            ang = math.atan2(p[0], CZ - p[1])
            g.box((p[0] - math.sin(ang) * 0.4, (BASE_H - 1.2) / 2, p[1] + 0.0), (1.0, BASE_H - 1.2, 1.4),
                  "NB_Stone", yaw=ang, top=False)

    # ---------------- tower floors
    fins_t = []
    for f in range(FLOORS):
        y0 = BASE_H + f * FLOOR_H
        tw = west_theta(f)
        te = THETA
        n = seg_count(detail, te + tw)
        glass_line = arc(Rf, -tw, te, n)
        balc_edge = arc(Rf + BALC, -tw, te, n)
        back_line = arc(Rb, -tw, te, n)
        # balcony slab (stone) - top, soffit and the white edge band
        g.slab(balc_edge, glass_line, y0, y0 + 0.32, "NB_Stone")
        # sea-face glass
        g.ribbon(glass_line, y0 + 0.32, y0 + FLOOR_H, "NB_Glass")
        # road-face: stone spandrel + window band
        g.ribbon(back_line, y0, y0 + 1.0, "NB_Stone", outward=False)
        g.ribbon(back_line, y0 + 1.0, y0 + FLOOR_H, "NB_Glass", outward=False)
        # glass balustrade + bronze handrail
        if detail == 0:
            rail = arc(Rf + BALC - 0.06, -tw, te, n)
            g.ribbon(rail, y0 + 0.32, y0 + 1.36, "NB_BalconyGlass", double=True)
            g.slab(arc(Rf + BALC - 0.01, -tw, te, n), arc(Rf + BALC - 0.13, -tw, te, n),
                   y0 + 1.36, y0 + 1.44, "NB_Bronze")
        elif detail == 1:
            rail = arc(Rf + BALC - 0.06, -tw, te, n)
            g.ribbon(rail, y0 + 0.32, y0 + 1.40, "NB_BalconyGlass", double=True)
        # west end wall (steps) - stone
        a = (math.sin(-tw) * (Rf + BALC + 0.2), CZ - math.cos(-tw) * (Rf + BALC + 0.2))
        b = (math.sin(-tw) * Rb, CZ - math.cos(-tw) * Rb)
        end_wall(g, b, a, y0, detail)
        # exposed terrace roof where the next floor steps back
        tw_next = west_theta(f + 1) if f + 1 < FLOORS else None
        if tw_next is not None and tw_next < tw - 1e-6:
            m = max(1, seg_count(detail, tw - tw_next))
            g.slab(arc(Rf + BALC, -tw, -tw_next, m), arc(Rb, -tw, -tw_next, m),
                   y0 + FLOOR_H - 0.01, y0 + FLOOR_H + 0.02, "NB_DeckStone")
            if detail < 2:  # terrace parapet glass along the front
                g.ribbon(arc(Rf + BALC - 0.06, -tw, -tw_next, m), y0 + FLOOR_H, y0 + FLOOR_H + 1.1,
                         "NB_BalconyGlass", double=True)
    # east end wall full height (+ fins list)
    top_y = BASE_H + FLOORS * FLOOR_H
    a = (math.sin(THETA) * (Rf + BALC + 0.2), CZ - math.cos(THETA) * (Rf + BALC + 0.2))
    b = (math.sin(THETA) * Rb, CZ - math.cos(THETA) * Rb)
    for f in range(FLOORS):
        end_wall(g, a, b, BASE_H + f * FLOOR_H, detail)
    # vertical stone fins on the sea face (every ~6 m), full height of the floors they cross
    nf = (14, 7, 0)[detail]
    for i in range(nf + 1):
        t = -THETA + 2 * THETA * i / nf if nf else 0
        # the fin only rises over floors whose west end still includes t
        top_f = FLOORS
        for f in range(FLOORS):
            if t < -west_theta(f) + 1e-4:
                top_f = f
                break
        if top_f == 0:
            continue
        h = top_f * FLOOR_H
        r = Rf + BALC * 0.5 + 0.15
        g.box((math.sin(t) * r, BASE_H + h / 2, CZ - math.cos(t) * r), (0.38, h, BALC + 0.3),
              "NB_Stone", yaw=-t, top=True)
    # ---------------- roof + sky bar crown
    tw_top = west_theta(FLOORS - 1)
    n = seg_count(detail, THETA + tw_top)
    g.slab(arc(Rf + BALC, -tw_top, THETA, n), arc(Rb, -tw_top, THETA, n), top_y - 0.02, top_y + 0.4,
           "NB_DeckStone", mats={"edge": "NB_Stone", "bottom": "NB_Stone"})
    # sky bar: set-back glass pavilion over the central-east part
    sb0, sb1 = -tw_top + 0.03, THETA - 0.05
    ns = seg_count(detail, sb1 - sb0)
    g.ribbon(arc(Rf - 2.5, sb0, sb1, ns), top_y + 0.4, top_y + 5.2, "NB_Glass", v_tile=12.8 * 1.5)
    g.ribbon(arc(Rb + 3.0, sb0, sb1, ns), top_y + 0.4, top_y + 5.2, "NB_Glass", outward=False,
             v_tile=12.8 * 1.5)
    for t, rev in ((sb0, True), (sb1, False)):
        a = (math.sin(t) * (Rf - 2.5), CZ - math.cos(t) * (Rf - 2.5))
        b = (math.sin(t) * (Rb + 3.0), CZ - math.cos(t) * (Rb + 3.0))
        g.ribbon([b, a] if rev else [a, b], top_y + 0.4, top_y + 5.2, "NB_Glass", v_tile=12.8 * 1.5)
    g.slab(arc(Rf - 1.5, sb0, sb1, ns), arc(Rb + 2.0, sb0, sb1, ns), top_y + 5.2, top_y + 5.8, "NB_Stone")
    # pergola over the roof terrace (radial teak beams + two stone runners)
    if detail < 2:
        np_ = (22, 10)[detail]
        for i in range(np_ + 1):
            t = sb0 + (sb1 - sb0) * i / np_
            r = (Rf + Rb) / 2 + 1.0
            g.box((math.sin(t) * r, top_y + 7.4, CZ - math.cos(t) * r), (0.28, 0.42, DEPTH + 3.0),
                  "NB_Teak", yaw=-t, top=True, bottom=True)
        for rr in (Rf + 0.5, Rb + 1.5):
            g.slab(arc(rr + 0.3, sb0, sb1, ns), arc(rr - 0.3, sb0, sb1, ns), top_y + 6.9, top_y + 7.2,
                   "NB_Stone")
        for i in range(0, np_ + 1, 3 if detail == 0 else 5):
            t = sb0 + (sb1 - sb0) * i / np_
            for rr in (Rf + 0.5, Rb + 1.5):
                g.box((math.sin(t) * rr, top_y + 6.35, CZ - math.cos(t) * rr), (0.4, 1.2, 0.4), "NB_Bronze",
                      yaw=-t)
    # crown sail: a curved stone blade on the east end rising above the roof
    ncs = (10, 5, 3)[detail]
    t0 = THETA - 0.10
    pts = arc(Rf + BALC + 0.4, t0, THETA, ncs)
    for i in range(ncs):
        a, b = pts[i], pts[i + 1]
        h0 = 4.0 + 14.0 * (i / ncs) ** 1.6
        h1 = 4.0 + 14.0 * ((i + 1) / ncs) ** 1.6
        g.quad((a[0], top_y, a[1]), (a[0], top_y + h0, a[1]), (b[0], top_y + h1, b[1]), (b[0], top_y, b[1]),
               "NB_Stone", double=True)
    # ---------------- porte-cochere (road side, +z)
    zc0 = CZ - (Rb - 5.0)          # lobby back apex z
    cz0, cz1, cx = zc0 - 1.0, zc0 + 26.0, 20.0
    g.box((0, 7.6, (cz0 + cz1) / 2), (2 * cx, 1.3, cz1 - cz0), "NB_Stone", top=True, bottom=False,
          mats={"top": "NB_Stone"})
    g.quad((-cx + 0.3, 6.94, cz0), (cx - 0.3, 6.94, cz0), (cx - 0.3, 6.94, cz1 - 0.3), (-cx + 0.3, 6.94, cz1 - 0.3),
           "NB_Teak")  # soffit (faces down)
    for x in (-cx + 3.0, cx - 3.0):
        for z in (cz0 + 10.0, cz1 - 3.0):
            g.cylinder((x, 0, z), 0.55, 6.95, (16, 10, 6)[detail], "NB_Bronze", top=False)
    # sign band on the fascia (+z face), UV mapped to the hotel sign texture
    sw, sh = 22.0, 1.0
    g.quad((sw / 2, 7.1, cz1 + 0.02), (sw / 2, 8.1, cz1 + 0.02), (-sw / 2, 8.1, cz1 + 0.02),
           (-sw / 2, 7.1, cz1 + 0.02), "NB_HotelSign", uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
    # drive loop (paving) + centre planter island
    seg = (40, 20, 10)[detail]
    ring = [(0 + 17.0 * math.cos(i / seg * math.tau), (cz0 + cz1) / 2 + 8.0 + 17.0 * math.sin(i / seg * math.tau))
            for i in range(seg)]
    g.face([(p[0], 0.04, p[1]) for p in reversed(ring)], "NB_DeckStone")
    g.cylinder((0, 0, (cz0 + cz1) / 2 + 8.0), 5.5, 0.7, seg, "NB_Planter", top=False)
    g.cylinder((0, 0.02, (cz0 + cz1) / 2 + 8.0), 5.2, 0.62, seg, "NB_Soil", top=True, bottom=False)
    return g


# ------------------------------------------------------------------ podium / pool deck
DECK_Y = 4.0
DX0, DX1, DZ0, DZ1 = -78.0, 78.0, -52.0, -6.0    # plinth footprint (sea edge at DZ0)
POOL = (-58.0, 22.0, -44.0, -20.0)                # x0 x1 z0 z1, vanishing edge at z0


def podium(detail):
    g = Geo()
    # plinth: stone walls + deck top with the pool cut out (deck built as tiles around it)
    x0, x1, z0, z1 = DX0, DX1, DZ0, DZ1
    g.ribbon([(x0, z0), (x1, z0)], -3.0, DECK_Y, "NB_Stone")            # sea face
    g.ribbon([(x1, z0), (x1, z1)], -3.0, DECK_Y, "NB_Stone")
    g.ribbon([(x0, z1), (x0, z0)], -3.0, DECK_Y, "NB_Stone")
    px0, px1, pz0, pz1 = POOL
    Y = DECK_Y

    def deck(a, b, c, d):
        g.quad((a, Y, c), (a, Y, d), (b, Y, d), (b, Y, c), "NB_DeckStone")
    deck(x0, px0, z0, z1)
    deck(px1, x1, z0, z1)
    deck(px0, px1, pz1, z1)
    deck(px0, px1, z0, pz0 - 3.0)                    # thin ledge seaward of the catch trough
    # infinity pool tank
    depth = 1.5
    for (a, b) in (((px0, pz1), (px1, pz1)), ((px1, pz1), (px1, pz0)), ((px0, pz0), (px0, pz1))):
        g.ribbon([a, b], Y - depth, Y, "NB_PoolTile", outward=True)     # faces into the pool
    g.quad((px0, Y - depth, pz1), (px1, Y - depth, pz1), (px1, Y - depth, pz0), (px0, Y - depth, pz0),
           "NB_PoolTile")
    # vanishing edge: the tank wall drops 0.35 below the waterline to a catch trough
    g.ribbon([(px1, pz0), (px0, pz0)], Y - depth, Y - 0.06, "NB_PoolTile", outward=True)
    g.quad((px0, Y - 0.40, pz0 - 3.0), (px0, Y - 0.40, pz0), (px1, Y - 0.40, pz0), (px1, Y - 0.40, pz0 - 3.0),
           "NB_PoolTile")
    g.ribbon([(px0, pz0 - 3.0), (px1, pz0 - 3.0)], Y - 0.40, Y, "NB_Stone", outward=False)
    g.ribbon([(px1, pz0), (px0, pz0)], Y - 0.40, Y - 0.06, "NB_Stone", outward=False)
    # water surfaces (pool + trough), HDRP/Lit water in Unity
    g.quad((px0, Y - 0.06, pz0), (px0, Y - 0.06, pz1), (px1, Y - 0.06, pz1), (px1, Y - 0.06, pz0),
           "NB_PoolWater", uvs=[(0, 0), (0, 3), (10, 3), (10, 0)])
    g.quad((px0, Y - 0.46 + 0.1, pz0 - 3.0), (px0, Y - 0.36, pz0), (px1, Y - 0.36, pz0), (px1, Y - 0.36, pz0 - 3.0),
           "NB_PoolWater", uvs=[(0, 0), (0, 0.4), (10, 0.4), (10, 0)])
    # jacuzzi
    seg = (32, 16, 10)[detail]
    g.cylinder((40.0, Y - 1.0, -30.0), 4.2, 1.45, seg, "NB_Stone", top=False)
    ring = [(40.0 + 3.9 * math.cos(i / seg * math.tau), -30.0 + 3.9 * math.sin(i / seg * math.tau)) for i in range(seg)]
    g.face([(p[0], Y + 0.35, p[1]) for p in reversed(ring)], "NB_PoolWater")
    # broad steps down to the sand in the east part (x 30..60)
    sx0, sx1 = 30.0, 62.0
    nsteps = (12, 6, 3)[detail]
    for i in range(nsteps):
        yt = Y - (i + 1) * (Y + 1.0) / nsteps
        za, zb = z0 - i * (6.0 / nsteps), z0 - (i + 1) * (6.0 / nsteps)
        g.quad((sx0, yt, zb), (sx0, yt, za), (sx1, yt, za), (sx1, yt, zb), "NB_DeckStone")
        hh = (Y + 1.0) / nsteps     # riser at the landward edge of this tread, facing the sea
        g.quad((sx0, yt + hh, za), (sx1, yt + hh, za), (sx1, yt, za), (sx0, yt, za), "NB_Stone")
    for sx in (sx0, sx1):
        g.box((sx, (Y - 1.0) / 2 - 0.5, z0 - 3.0), (0.8, Y + 1.0, 6.0), "NB_Stone")
    # glass balustrade along the sea edge (except steps + vanishing edge)
    if detail < 2:
        for a, b in (((x0, z0), (px0 - 0.5, z0)), ((px1 + 0.5, z0), (sx0, z0)), ((sx1, z0), (x1, z0)),
                     ((x1, z0), (x1, z1)), ((x0, z1), (x0, z0))):
            g.ribbon([a, b], Y, Y + 1.1, "NB_BalconyGlass", double=True)
            if detail == 0:
                ax, az = a
                bx, bz = b
                L = math.hypot(bx - ax, bz - az)
                g.box(((ax + bx) / 2, Y + 1.14, (az + bz) / 2), (L if abs(bx - ax) > 1 else 0.1, 0.07,
                      L if abs(bz - az) > 1 else 0.1), "NB_Bronze", bottom=True)
    # cabanas along the west end of the deck
    cab = []
    for i in range(6 if detail < 2 else 0):
        cx, cz = x0 + 6.0, z1 - 6.0 - i * 7.0
        cab.append((cx, cz))
        for sx in (-2.2, 2.2):
            for sz in (-2.2, 2.2):
                g.box((cx + sx, Y + 1.4, cz + sz), (0.18, 2.8, 0.18), "NB_Teak")
        apex = (cx, Y + 4.0, cz)
        c4 = [(cx - 2.6, Y + 2.8, cz - 2.6), (cx + 2.6, Y + 2.8, cz - 2.6), (cx + 2.6, Y + 2.8, cz + 2.6),
              (cx - 2.6, Y + 2.8, cz + 2.6)]
        for k in range(4):
            a, b = c4[k], c4[(k + 1) % 4]
            g.tri(a, apex, b, "NB_Canvas", uvs=[(0, 0.5), (0.5, 1.0), (1, 0.5)])
            g.tri(b, apex, a, "NB_Canvas", uvs=[(1, 0.5), (0.5, 1.0), (0, 0.5)])
        g.box((cx, Y + 0.3, cz), (3.6, 0.6, 3.6), "NB_Canvas", mats={"side": "NB_Teak"})
        if detail == 0:  # tied-back curtains at the corners
            for sx, sz in ((-2.2, -2.2), (2.2, -2.2)):
                g.box((cx + sx, Y + 1.5, cz + sz), (0.5, 2.6, 0.5), "NB_Canvas")
    # planters (palms placed into these in Unity)
    planters = []
    for x in (-72.0, -30.0, 10.0, 28.0, 70.0):
        planters.append((x, -11.0))
    for z in (-18.0, -34.0):
        planters.append((72.0, z))
    seg = (16, 10, 6)[detail]
    for (x, z) in planters:
        g.cylinder((x, Y, z), 2.0, 0.8, seg, "NB_Planter", top=False)
        g.cylinder((x, Y + 0.02, z), 1.85, 0.7, seg, "NB_Soil", top=True, bottom=False)
    return g, planters, cab


# ------------------------------------------------------------------ boutique resort
def boutique(detail):
    """9 storeys in three terraced tiers stepping back north (+z); terraces face the sea."""
    g = Geo()
    tiers = [(0, 4, 88.0, 34.0, 0.0), (4, 7, 72.0, 26.0, 8.0), (7, 9, 52.0, 18.0, 16.0)]
    fh = 3.4
    for (f0, f1, w, d, zoff) in tiers:
        x0, x1 = -w / 2, w / 2
        zb = 17.0
        zf = zb - d
        y0, y1 = f0 * fh, f1 * fh
        # walls: plaster with window atlas on the sea face, plaster elsewhere
        g.ribbon([(x0, zf), (x1, zf)], y0, y1, "NB_TownWindows", u_tile=6.0, v_tile=6.8)
        g.ribbon([(x1, zf), (x1, zb)], y0, y1, "NB_Plaster")
        g.ribbon([(x1, zb), (x0, zb)], y0, y1, "NB_TownWindows", u_tile=6.0, v_tile=6.8)
        g.ribbon([(x0, zb), (x0, zf)], y0, y1, "NB_Plaster")
        # per floor: projecting plaster balcony slab + teak screen posts
        for f in range(f0, f1):
            yy = f * fh
            g.box((0, yy + 0.15, zf - 1.1), (w + 0.4, 0.3, 2.2), "NB_Plaster", bottom=True)
            if detail < 2:
                g.ribbon([(x0, zf - 2.15), (x1, zf - 2.15)], yy + 0.3, yy + 1.3, "NB_BalconyGlass", double=True)
            if detail == 0:
                for k in range(int(w // 6) + 1):
                    x = x0 + k * (w / int(w // 6))
                    g.box((x, yy + fh / 2 + 0.15, zf - 2.0), (0.14, fh - 0.3, 0.14), "NB_Teak", top=False)
        # roof slab / terrace deck
        g.box((0, y1 + 0.2, (zf + zb) / 2), (w + 0.6, 0.4, zb - zf + 0.6), "NB_DeckStone",
              mats={"side": "NB_Plaster", "top": "NB_DeckStone"})
    # roof pergola on the top tier
    top = 9 * fh + 0.4
    if detail < 2:
        for k in range(12 if detail == 0 else 6):
            x = -24 + k * (48 / (11 if detail == 0 else 5))
            g.box((x, top + 2.8, 8.0), (0.3, 0.4, 14.0), "NB_Teak", bottom=True)
        for z in (2.0, 14.0):
            g.box((0, top + 2.4, z), (50.0, 0.4, 0.4), "NB_Teak", bottom=True)
            for x in (-24.0, 0.0, 24.0):
                g.box((x, top + 1.1, z), (0.35, 2.2, 0.35), "NB_Teak", top=False)
    # entrance canopy toward the road (sea side, -z) at ground
    g.box((0, 3.6, -21.0), (18.0, 0.5, 8.0), "NB_Teak", bottom=True, mats={"side": "NB_Plaster"})
    for x in (-8.0, 8.0):
        g.box((x, 1.7, -24.5), (0.4, 3.4, 0.4), "NB_Bronze", top=False)
    return g


def main():
    C.build_lods("Nagisa_HotelTower", tower, "Nagisa_HotelTower.glb")
    anchors = {}

    def pod(detail):
        g, planters, cab = podium(detail)
        if detail == 0:
            anchors["planters"] = planters
            anchors["cabanas"] = cab
        return g
    C.build_lods("Nagisa_HotelPodium", pod, "Nagisa_HotelPodium.glb")
    C.build_lods("Nagisa_BoutiqueResort", boutique, "Nagisa_BoutiqueResort.glb")
    # lounger + umbrella rows on the pool deck (east of the pool) and palm island positions
    loungers = []
    for i in range(10):
        loungers.append((-54.0 + i * 8.0, -15.5, 180.0))
    for i in range(4):
        loungers.append((28.5 + i * 0.0, -40.0 + i * 5.5, 90.0))
    anchors["loungers"] = loungers
    anchors["deckY"] = DECK_Y
    zc0 = CZ - (R_FRONT - DEPTH - 5.0)
    anchors["driveIsland"] = (0.0, (zc0 - 1.0 + zc0 + 26.0) / 2 + 8.0)
    anchors["driveEdgeZ"] = (zc0 - 1.0 + zc0 + 26.0) / 2 + 8.0 + 17.0
    anchors["tower"] = {"floors": FLOORS, "height": BASE_H + FLOORS * FLOOR_H}
    with open(os.path.join(C.OUT, "Nagisa_HotelAnchors.json"), "w") as f:
        json.dump(anchors, f, indent=1)
    print("[nagisa-glb] anchors written")


main()

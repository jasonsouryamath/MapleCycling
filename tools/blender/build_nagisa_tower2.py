"""
NAGISA BAY (B5) PASS 2 - hero resort tower REDO: "Grand Shiokaze Resort" (fictional).

Run: blender.exe -b -P tools/blender/build_nagisa_tower2.py

Replaces the pass-1 Nagisa_HotelTower (flat cream slab + window stripes). Same pad frame so the
existing podium / anchors / drive still line up (-z = sea, +z = road; sea-face arc radius 160 m
about (0, CZ), apex z = -8, 20 m deep):

  * 4-storey curved lobby base with double-height bronze-framed glazing between stone piers
  * 30 guest floors; the silhouette steps back on BOTH ends (west from floor 14, east from 21)
    so the tower reads as a terraced curved sail, not a slab
  * every guest room (4.2 m bay) has a real balcony: 0.22 m slab whose depth undulates floor to
    floor (2.2 - 3.4 m) giving the flowing Gold-Coast balcony edge, frameless glass balustrade with
    bronze handrail, accent privacy fins between rooms, loungers / table + chairs / potted palms
  * recessed full-height room glazing into the NB_Interior room atlas (lit at dusk)
  * road face: lift core expressed as a stone spine, corridor windows with louvre shades
  * step-back terraces with glass parapets + planters; crown: 2-storey glass sky bar (NB_SkyGlass,
    strongly emissive) under a deep floating roof, 14 m bronze crown fins, roof pergola bar
  * road-side porte-cochere (drive loop + island, same anchors as pass 1) with a lit canopy sign

PROVISIONAL art numbers.
"""
import math
import os
import random
import sys

sys.path.append(os.path.dirname(__file__))
import build_nagisa_common as C          # noqa: E402
from build_nagisa_common import Geo, add  # noqa: E402
import build_nagisa_arch as A             # noqa: E402

C.TILE.update({"NB_SkyGlass": (4.0, 4.2)})

FLOORS = 30
FH = 3.2
BASE_H = 12.8
R_FRONT = 160.0
DEPTH = 20.0
APEX_Z = -8.0
THETA = 0.2625
CZ = APEX_Z + R_FRONT
BAY = 4.2


def arcp(r, t):
    return (r * math.sin(t), CZ - r * math.cos(t))


def arc(r, t0, t1, n):
    return [arcp(r, t0 + (t1 - t0) * i / n) for i in range(n + 1)]


def west_t(f):
    return THETA - max(0, f - 13) * 0.0105


def east_t(f):
    return THETA - max(0, f - 20) * 0.016


def nseg(t0, t1, r, detail):
    L = r * (t1 - t0)
    return max(2, int(round(L / (BAY if detail < 2 else BAY * 3))))


def floor_poly(f, detail):
    tw, te = west_t(f), east_t(f)
    n = nseg(-tw, te, R_FRONT, detail)
    front = arc(R_FRONT, -tw, te, n)
    back = arc(R_FRONT - DEPTH, -tw, te, max(2, n // 2))
    return front, back, list(front) + list(reversed(back))


ROOM = None


def guest_floor(g, f, detail, rng):
    y = BASE_H + f * FH
    front, back, poly = floor_poly(f, detail)
    room = A.FacadeSpec(bay=BAY, full_height=True, win_w=0.9, fh=FH, reveal=0.32, mullions=1, transom=False,
                        frame_band=A.TRIM_BRONZE, wall="NB_Wall", spandrel="NB_Wall", pier="NB_Accent",
                        seed=1000 + f)
    corr = A.FacadeSpec(bay=3.6, win_w=0.42, win_h=0.52, sill=0.95, fh=FH, reveal=0.25, frame_band=A.TRIM_BRONZE,
                        louvres=True, wall="NB_Wall", seed=2000 + f)
    endw = A.FacadeSpec(bay=3.4, win_w=0.62, win_h=0.66, sill=0.7, fh=FH, reveal=0.28, mullions=1,
                        frame_band=A.TRIM_BRONZE, wall="NB_Wall", seed=3000 + f)
    nf = len(front) - 1
    # sea face: one bay per segment (bays=1 keeps the room = segment)
    for i in range(nf):
        A.facade(g, front[i], front[i + 1], y, 1, room, detail, bays=1)
    # east end, road face, west end
    A.facade(g, front[-1], back[-1], y, 1, endw, detail)
    for i in range(len(back) - 1, 0, -1):
        a, b = back[i], back[i - 1]
        mid = (a[0] + b[0]) / 2
        if abs(mid) < 5.0:        # lift core spine: blank stone with a slot window
            F = A.Frame(a, b)
            A.qn(g, [F.P(0, y), F.P(F.L, y), F.P(F.L, y + FH), F.P(0, y + FH)], F.n, "NB_Stone")
            if detail < 2:      # stone spine: bronze floor band + a lit vertical slot so it reads as a lift core
                A._frame_box(g, F, F.P(F.L / 2, y + 0.1, 0.06), F.L + 0.1, 0.2, 0.12, A.TRIM_BRONZE)
                A.qn(g, [F.P(F.L * 0.38, y + 0.5, 0.03), F.P(F.L * 0.62, y + 0.5, 0.03),
                         F.P(F.L * 0.62, y + FH - 0.5, 0.03), F.P(F.L * 0.38, y + FH - 0.5, 0.03)], F.n, "NB_Glass")
            continue
        A.facade(g, a, b, y, 1, corr, detail)
    A.facade(g, back[0], front[0], y, 1, endw, detail)
    # balconies: undulating depth, glass balustrade, privacy fins, furniture
    for i in range(nf):
        a, b = front[i], front[i + 1]
        F = A.Frame(a, b)
        tmid = (i + 0.5) / nf
        depth = 2.8 + 0.6 * math.sin(tmid * 7.0 + f * 0.42)
        tower_balcony(g, F, y, depth, detail, rng, fin_left=(i == 0), fin_right=True, f=f, i=i)
    # slab edge band on the road + ends (the sea face's band is the balcony slab)
    if detail < 2:
        Fb = [A.Frame(back[i + 1], back[i]) for i in range(len(back) - 1)]
        for Fr in Fb:
            A._frame_box(g, Fr, Fr.P(Fr.L / 2, y + 0.11, 0.07), Fr.L + 0.2, 0.22, 0.14, None,
                         bev=0.02 if detail == 0 else 0, mat="NB_Accent")
    return poly


def tower_balcony(g, F, y, depth, detail, rng, fin_left, fin_right, f, i):
    s0, s1 = -0.12, F.L + 0.12
    t = 0.24
    A._frame_box(g, F, F.P(F.L / 2, y - t / 2 + 0.02, depth / 2), s1 - s0, t, depth, None,
                 bev=0.04 if detail == 0 else 0.0, mat="NB_Plaster")
    if detail < 2:
        e = depth - 0.08
        top = y + 0.02
        p0, p1 = F.P(s0 + 0.02, top + 0.05, e), F.P(s1 - 0.02, top + 0.05, e)
        g.quad(p0, p1, add(p1, (0, 1.05, 0)), add(p0, (0, 1.05, 0)), "NB_BalconyGlass", double=True)
        A._frame_box(g, F, F.P(F.L / 2, top + 1.12, e), s1 - s0, 0.05, 0.08, A.TRIM_BRONZE)
    # privacy fin (accent, full storey, full balcony depth) on the right edge of each room
    if fin_right and detail < 2:
        A._frame_box(g, F, F.P(F.L, y + FH / 2, depth / 2 - 0.05), 0.18, FH - 0.22, depth - 0.1, None,
                     bev=0.03 if detail == 0 else 0.0, mat="NB_Accent")
    if fin_left and detail < 2:
        A._frame_box(g, F, F.P(0.0, y + FH / 2, depth / 2 - 0.05), 0.18, FH - 0.22, depth - 0.1, None,
                     bev=0.03 if detail == 0 else 0.0, mat="NB_Accent")
    if detail == 0:
        top = y + 0.02
        r = rng.random()
        yaw = math.atan2(-F.u[2], F.u[0])
        if r < 0.45:
            for k in (-0.55, 0.55):
                A.bbox(g, F.P(F.L / 2 + k, top + 0.28, depth * 0.5), (0.62, 0.1, 1.75), "NB_Canvas", 0.03, yaw)
                A.bbox(g, F.P(F.L / 2 + k, top + 0.13, depth * 0.5), (0.58, 0.2, 1.6), "NB_Teak", 0.0, yaw)
        elif r < 0.8:
            A.table(g, F.P(F.L / 2, top, depth * 0.5), 0.34, 0.72, 8)
            A.chair(g, F.P(F.L / 2 - 0.7, top, depth * 0.5), yaw + math.pi / 2)
            A.chair(g, F.P(F.L / 2 + 0.7, top, depth * 0.5), yaw - math.pi / 2)
        if rng.random() < 0.55:
            A.potted_plant(g, F.P(F.L - 0.45, top, depth - 0.5), detail, big=True,
                           mat="NB_Hibiscus" if rng.random() < 0.5 else "NB_Bougainvillea")


def terrace(g, f, detail, rng):
    """Exposed roof where floor f+1 steps back relative to floor f (both ends)."""
    y = BASE_H + (f + 1) * FH
    tw0, te0 = west_t(f), east_t(f)
    tw1, te1 = west_t(f + 1), east_t(f + 1)
    for (ta, tb) in (((-tw0), (-tw1)), ((te1), (te0))):
        if tb - ta < 1e-5:
            continue
        n = max(1, nseg(ta, tb, R_FRONT, detail))
        fr = arc(R_FRONT + 0.3, ta, tb, n)
        bk = arc(R_FRONT - DEPTH, ta, tb, n)
        poly = list(fr) + list(reversed(bk))
        A.qn(g, [(p[0], y + 0.02, p[1]) for p in poly], (0, 1, 0), "NB_DeckStone")
        if detail < 2:
            for k in range(n):
                F = A.Frame(fr[k], fr[k + 1])
                p0, p1 = F.P(0, y + 0.05), F.P(F.L, y + 0.05)
                g.quad(p0, p1, add(p1, (0, 1.05, 0)), add(p0, (0, 1.05, 0)), "NB_BalconyGlass", double=True)
                A._frame_box(g, F, F.P(F.L / 2, y + 1.12, 0), F.L, 0.05, 0.08, A.TRIM_BRONZE)
            if detail == 0:
                for k in range(n):
                    m = arcp(R_FRONT - 3.0, ta + (tb - ta) * (k + 0.5) / n)
                    A.potted_plant(g, (m[0], y + 0.02, m[1]), detail, big=True)
                    m2 = arcp(R_FRONT - 7.0, ta + (tb - ta) * (k + 0.5) / n)
                    A.bbox(g, (m2[0], y + 0.3, m2[1]), (1.8, 0.35, 0.7), "NB_Canvas", 0.05)


def tower(detail):
    g = Geo()
    rng = random.Random(4242)
    # ---------------- lobby base: 2 double-height storeys, curved
    tb0, tb1 = -THETA - 0.02, THETA + 0.02
    n = max(2, int(round(R_FRONT * (tb1 - tb0) / (6.0 if detail < 2 else 18.0))))
    fr = arc(R_FRONT + 3.0, tb0, tb1, n)
    bk = arc(R_FRONT - DEPTH - 3.0, tb0, tb1, max(2, n // 2))
    lob = A.FacadeSpec(bay=6.0, full_height=True, win_w=0.86, fh=6.4, reveal=0.6, mullions=2, transom=True,
                       frame_band=A.TRIM_BRONZE, wall="NB_Stone", spandrel="NB_Stone", pier="NB_Stone", seed=7)
    for lv in range(2):
        yb = lv * 6.4
        for i in range(n):
            A.facade(g, fr[i], fr[i + 1], yb, 1, lob, detail, bays=1)
        for i in range(len(bk) - 1, 0, -1):
            A.facade(g, bk[i], bk[i - 1], yb, 1, lob, detail, bays=1)
        A.facade(g, fr[-1], bk[-1], yb, 1, lob, detail)
        A.facade(g, bk[0], fr[0], yb, 1, lob, detail)
    base_poly = list(fr) + list(reversed(bk))
    for yy in (6.4, BASE_H):
        for i in range(len(base_poly)):
            a, b = base_poly[i], base_poly[(i + 1) % len(base_poly)]
            F = A.Frame(a, b)
            A._frame_box(g, F, F.P(F.L / 2, yy - 0.2, 0.25), F.L + 0.5, 0.6 if yy == BASE_H else 0.4, 0.5, None,
                         bev=0.04 if detail == 0 else 0, mat="NB_Stone")
    A.qn(g, [(p[0], BASE_H + 0.01, p[1]) for p in base_poly], (0, 1, 0), "NB_DeckStone")
    # stone piers standing proud of the lobby glazing, full base height
    if detail < 2:
        for p in fr[::1 if detail == 0 else 2]:
            t = math.atan2(p[0], CZ - p[1])
            A.bbox(g, (p[0] - math.sin(t) * 0.5, BASE_H / 2, p[1] + math.cos(t) * -0.5), (1.1, BASE_H, 1.3),
                   "NB_Stone", 0.06, yaw=-t, detail=detail)
    # ---------------- guest floors
    for f in range(FLOORS):
        guest_floor(g, f, detail, rng)
        if f + 1 < FLOORS:
            terrace(g, f, detail, rng)
    top_y = BASE_H + FLOORS * FH
    # ---------------- roof + crown
    tw, te = west_t(FLOORS - 1), east_t(FLOORS - 1)
    nr = nseg(-tw, te, R_FRONT, detail)
    rf = arc(R_FRONT + 0.2, -tw, te, nr)
    rb = arc(R_FRONT - DEPTH, -tw, te, max(2, nr // 2))
    roof = list(rf) + list(reversed(rb))
    A.parapet(g, roof, top_y, 1.1, detail, mat="NB_Accent", roof_mat="NB_DeckStone")
    # sky bar pavilion (2 storeys, set back 3 m, strongly lit glazing)
    sb0, sb1 = -tw + 0.02, te - 0.02
    ns = max(2, nseg(sb0, sb1, R_FRONT - 3.5, detail))
    sf = arc(R_FRONT - 3.5, sb0, sb1, ns)
    sbk = arc(R_FRONT - DEPTH + 3.0, sb0, sb1, max(2, ns // 2))
    sky = A.FacadeSpec(bay=4.0, full_height=True, win_w=0.95, fh=4.2, reveal=0.15, mullions=1,
                       frame_band=A.TRIM_BRONZE, wall="NB_Accent", spandrel="NB_Accent", pier="NB_Accent",
                       glass="NB_SkyGlass", seed=77)
    for i in range(ns):
        A.facade(g, sf[i], sf[i + 1], top_y + 0.3, 1, sky, detail, bays=1)
    for i in range(len(sbk) - 1, 0, -1):
        A.facade(g, sbk[i], sbk[i - 1], top_y + 0.3, 1, sky, detail, bays=1)
    A.facade(g, sf[-1], sbk[-1], top_y + 0.3, 1, sky, detail)
    A.facade(g, sbk[0], sf[0], top_y + 0.3, 1, sky, detail)
    # floating roof (deep overhang) over the sky bar
    of = arc(R_FRONT + 1.0, sb0 - 0.01, sb1 + 0.01, ns)
    ob = arc(R_FRONT - DEPTH + 1.0, sb0 - 0.01, sb1 + 0.01, max(2, ns // 2))
    opoly = list(of) + list(reversed(ob))
    A.prism_poly(g, opoly, top_y + 4.7, top_y + 5.5, "NB_Accent", top=True, top_mat="NB_Concrete", bottom=False)
    A.qn(g, [(p[0], top_y + 4.7, p[1]) for p in opoly], (0, -1, 0), "NB_Teak")
    if detail < 2:
        # slim bronze columns carrying the roof out over the terrace
        for i in range(0, ns + 1, 1 if detail == 0 else 2):
            p = arcp(R_FRONT + 0.4, sb0 + (sb1 - sb0) * i / ns)
            A.cyl(g, (p[0], top_y + 0.3, p[1]), 0.12, 4.4, (8, 6, 4)[detail], "NB_Bronze", top=False)
        # crown: a low symmetric rhythm of bronze blades over the sky-bar roof (radial, centred in the
        # slab depth), tallest mid-span, plus a bronze cap beam on the sea edge and a roof plant screen
        nfin = 13
        for k in range(nfin):
            t = sb0 + (sb1 - sb0) * (k + 0.5) / nfin
            p = arcp(R_FRONT - DEPTH * 0.5, t)
            h = 2.6 + 3.2 * math.sin(math.pi * (k + 0.5) / nfin)
            A.bbox(g, (p[0], top_y + 5.5 + h / 2, p[1]), (0.3, h, DEPTH - 5.0), "NB_Accent", 0.04, yaw=-t,
                   detail=detail)
        for i in range(ns):
            a0, b0 = arcp(R_FRONT + 0.6, sb0 + (sb1 - sb0) * i / ns), arcp(R_FRONT + 0.6, sb0 + (sb1 - sb0) * (i + 1) / ns)
            Fc = A.Frame(a0, b0)
            A._frame_box(g, Fc, Fc.P(Fc.L / 2, top_y + 5.62, 0.0), Fc.L + 0.1, 0.24, 0.4, A.TRIM_BRONZE)
        mid_t = (sb0 + sb1) / 2
        pm = arcp(R_FRONT - DEPTH + 4.0, mid_t)
        A.bbox(g, (pm[0], top_y + 5.5 + 1.6, pm[1]), (22.0, 3.2, 3.0), "NB_Bronze", 0.05, yaw=-mid_t, detail=detail)
        # sign on the crown (sea face)
        m = arcp(R_FRONT + 1.05, (sb0 + sb1) / 2 + 0.03)
        t = (sb0 + sb1) / 2 + 0.03
        Fs = A.Frame(arcp(R_FRONT + 1.05, t - 0.05), arcp(R_FRONT + 1.05, t + 0.05))
        # GRAND SHIOKAZE RESORT lettering (21:1 texture) on the floating-roof fascia, sea face
        s0 = Fs.L / 2 - 8.4
        A.qn(g, [Fs.P(s0, top_y + 4.72, 0.03), Fs.P(s0 + 16.8, top_y + 4.72, 0.03), Fs.P(s0 + 16.8, top_y + 5.52, 0.03),
                 Fs.P(s0, top_y + 5.52, 0.03)], Fs.n, "NB_HotelSign", [(0, 0), (1, 0), (1, 1), (0, 1)])
    # ---------------- porte-cochere (road side, +z), same anchors as pass 1
    zc0 = CZ - (R_FRONT - DEPTH - 5.0)
    cz0, cz1, cx = zc0 - 1.0, zc0 + 26.0, 20.0
    A.bbox(g, (0, 7.4, (cz0 + cz1) / 2), (2 * cx, 1.1, cz1 - cz0), "NB_Accent", 0.08, detail=detail)
    A.qn(g, [(-cx + 0.1, 6.84, cz0), (cx - 0.1, 6.84, cz0), (cx - 0.1, 6.84, cz1 - 0.1), (-cx + 0.1, 6.84, cz1 - 0.1)],
         (0, -1, 0), "NB_Teak")
    if detail < 2:
        # recessed downlight grid (lamp dots) in the soffit
        if detail == 0:
            for ix in range(8):
                for iz in range(5):
                    x = -cx + 2.5 + ix * (2 * cx - 5) / 7
                    z = cz0 + 2.5 + iz * (cz1 - cz0 - 5) / 4
                    A.qn(g, [(x - 0.2, 6.83, z - 0.2), (x + 0.2, 6.83, z - 0.2), (x + 0.2, 6.83, z + 0.2),
                             (x - 0.2, 6.83, z + 0.2)], (0, -1, 0), "NB_Lamp")
        A.tbox(g, (0, 7.98, (cz0 + cz1) / 2), (2 * cx + 0.3, 0.08, cz1 - cz0 + 0.3), A.TRIM_BRONZE, detail=1)
    for x in (-cx + 3.0, cx - 3.0):
        for z in (cz0 + 10.0, cz1 - 3.0):
            A.cyl(g, (x, 0, z), 0.5, 6.85, (16, 10, 6)[detail], "NB_Stone", top=False)
            if detail < 2:
                A.cyl(g, (x, 0, z), 0.62, 0.5, (16, 10, 6)[detail], "NB_Bronze", top=True)
    # GRAND SHIOKAZE RESORT lettering on the canopy's road-edge fascia (21:1)
    A.qn(g, [(8.95, 7.02, cz1 + 0.03), (-8.95, 7.02, cz1 + 0.03), (-8.95, 7.87, cz1 + 0.03), (8.95, 7.87, cz1 + 0.03)],
         (0, 0, 1), "NB_HotelSign", [(0, 0), (1, 0), (1, 1), (0, 1)])
    # drive loop + island
    seg = (40, 20, 10)[detail]
    icz = (cz0 + cz1) / 2 + 8.0
    ring = [(17.0 * math.cos(i / seg * math.tau), icz + 17.0 * math.sin(i / seg * math.tau)) for i in range(seg)]
    A.qn(g, [(p[0], 0.04, p[1]) for p in ring], (0, 1, 0), "NB_PlazaStone")
    A.cyl(g, (0, 0, icz), 5.5, 0.6, seg, "NB_Stone", top=False)
    A.cyl(g, (0, 0.02, icz), 5.25, 0.55, seg, "NB_Groundcover", top=True)
    return g


def main():
    counts = C.build_lods("Nagisa_B_HeroTower", tower, "Nagisa_B_HeroTower.glb")
    print("[nagisa-b] Nagisa_B_HeroTower %s  height %.1f m" % (" / ".join("{:,}".format(c) for c in counts),
                                                              BASE_H + FLOORS * FH + 5.5))


main()

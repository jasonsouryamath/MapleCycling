"""
NAGISA BAY (B5) PASS 2 - street, beach and small-craft prop kit.

Run: blender.exe -b -P tools/blender/build_nagisa_street.py [-- Names...]

All props: real dimensions, bevelled, LOD0/1/2 (build_lods). Unity coords, front = -z where it
matters, origin at ground centre. Colour-variant slots (NB_CarPaint, NB_Awning bands) are remapped
per placement in C#. Fictional liveries only, no brands. PROVISIONAL sizes.

  cars      Nagisa_S_CarKei (3.4 m kei hatch), CarSedan (4.6 m), CarVan (4.7 m minivan),
            CarTaxi (sedan + roof lamp), FoodTruck (5.4 m, hatch open, awning, counter)
  2-wheel   Nagisa_S_Scooter (1.8 m step-through)
  street    StreetLight (8 m, single arm), BusStop, BikeRack (5 hoops), Bollard, Planter,
            HedgeSeg (4 m x 0.85 m clipped hedge), StreetTreeA (clipped round crown, 4.5 m),
            StreetTreeB (flat umbrella crown, 5 m), Crosswalk-free (decals are C# quads)
  beach     RentalStand, VolleyNet, Cabana, Towel, Parasol (low), Stroller, Surfboard
  water     JetSki, SUP, BananaBoat
"""
import math
import os
import random
import sys

sys.path.append(os.path.dirname(__file__))
import build_nagisa_common as C          # noqa: E402
from build_nagisa_common import Geo, add  # noqa: E402
import build_nagisa_arch as A             # noqa: E402

C.TILE.update({"NB_CarPaint": (1.0, 1.0), "NB_CarGlass": (1.0, 1.0), "NB_Rubber": (1.0, 1.0),
               "NB_Metal": (1.0, 1.0), "NB_Net": (1.0, 1.0)})
FOOT = {}
BOARD_MATS = ("NB_Gelcoat", "NB_PaintTeal", "NB_Paint")


def rec(name, hx, hz, h, **kw):
    FOOT[name] = dict(hx=hx, hz=hz, h=h, **kw)


# ------------------------------------------------------------------ helpers
def hcyl(g, c, r, w, seg, mat, axis="x", cap_mat=None, r2=None):
    """Horizontal cylinder centred at c, axis x or z, width w."""
    r2 = r if r2 is None else r2
    pts0, pts1 = [], []
    for i in range(seg):
        a = i / seg * math.tau
        cy, sy = math.cos(a), math.sin(a)
        if axis == "x":
            pts0.append((c[0] - w / 2, c[1] + r * sy, c[2] + r * cy))
            pts1.append((c[0] + w / 2, c[1] + r2 * sy, c[2] + r2 * cy))
        else:
            pts0.append((c[0] + r * cy, c[1] + r * sy, c[2] - w / 2))
            pts1.append((c[0] + r2 * cy, c[1] + r2 * sy, c[2] + w / 2))
    for i in range(seg):
        j = (i + 1) % seg
        A.qc(g, [pts0[i], pts0[j], pts1[j], pts1[i]], c, mat)
    cm = cap_mat or mat
    ax = (1, 0, 0) if axis == "x" else (0, 0, 1)
    A.qn(g, pts0, (-ax[0], 0, -ax[2]), cm)
    A.qn(g, pts1, ax, cm)


def wheel(g, c, r, w, detail, mat_tyre="NB_Rubber", mat_rim="NB_Chrome"):
    seg = (14, 8, 6)[detail]
    hcyl(g, c, r, w, seg, mat_tyre, cap_mat=mat_tyre)
    if detail < 2:
        for s in (-1, 1):
            p = (c[0] + s * (w / 2 + 0.005), c[1], c[2])
            ring = [(p[0], p[1] + r * 0.62 * math.sin(i / seg * math.tau), p[2] + r * 0.62 * math.cos(i / seg * math.tau))
                    for i in range(seg)]
            A.qn(g, ring if s > 0 else list(reversed(ring)), (s, 0, 0), mat_rim)


def frustum(g, b0, b1, t0, t1, y0, y1, side_mat, end_mat, top_mat, zc_off=0.0):
    """Car cabin: bottom rect x +-b0 z(b1) at y0, top rect x +-t0 z(t1) at y1.
    b1 / t1 = (z_front, z_back)."""
    B = [(-b0, y0, b1[0]), (b0, y0, b1[0]), (b0, y0, b1[1]), (-b0, y0, b1[1])]
    T = [(-t0, y1, t1[0]), (t0, y1, t1[0]), (t0, y1, t1[1]), (-t0, y1, t1[1])]
    ctr = (0, (y0 + y1) / 2 - 0.5, (b1[0] + b1[1]) / 2)
    A.qc(g, [B[0], B[1], T[1], T[0]], ctr, end_mat)            # windscreen (front, -z)
    A.qc(g, [B[1], B[2], T[2], T[1]], ctr, side_mat)           # right
    A.qc(g, [B[2], B[3], T[3], T[2]], ctr, end_mat)            # rear glass
    A.qc(g, [B[3], B[0], T[0], T[3]], ctr, side_mat)           # left
    A.qn(g, [T[0], T[1], T[2], T[3]], (0, 1, 0), top_mat)
    return B, T


# ------------------------------------------------------------------ cars
def car(detail, L=4.6, W=1.78, H=1.45, cab=(0.35, 0.72), belt=0.82, wheel_r=0.32, nose=0.5,
        taxi=False, van=False):
    """Bevelled body + glass cabin with pillars. Front = -z."""
    g = Geo()
    hl, hw = L / 2, W / 2
    body_h = belt - 0.22
    A.bbox(g, (0, 0.22 + body_h / 2, 0), (W, body_h, L), "NB_CarPaint", 0.12 if detail == 0 else 0.0, detail=detail)
    # bonnet / boot slope: a tapered cap on the front
    if not van and detail < 2:
        A.qn(g, [(-hw + 0.06, belt, -hl + 0.02), (hw - 0.06, belt, -hl + 0.02), (hw - 0.06, belt + 0.05, -hl + nose),
                 (-hw + 0.06, belt + 0.05, -hl + nose)], (0, 1, -0.2), "NB_CarPaint")
    # cabin
    zf = -hl + L * cab[0]
    zb = -hl + L * cab[1] if not van else hl - 0.12
    tf = zf + (0.55 if not van else 0.35)
    tb = zb - (0.35 if not van else 0.05)
    frustum(g, hw - 0.06, (zf, zb), hw - 0.16, (tf, tb), belt, H, "NB_CarGlass", "NB_CarGlass", "NB_CarPaint")
    if detail < 2:
        # roof skin + pillars (A, B, C) in body colour
        A.bbox(g, (0, H + 0.015, (tf + tb) / 2), (2 * (hw - 0.14), 0.05, tb - tf + 0.06), "NB_CarPaint", 0.02, detail=detail)
        for s in (-1, 1):
            for (zb0, zt0) in ((zf, tf), ((zf + zb) / 2, (tf + tb) / 2), (zb, tb)):
                A.seg_box(g, (s * (hw - 0.055), belt, zb0), (s * (hw - 0.155), H, zt0), 0.09, 0.07, "NB_CarPaint")
        # bumpers, lights, plates, mirrors
        for z, s in ((-hl - 0.02, -1), (hl + 0.02, 1)):
            A.bbox(g, (0, 0.36, z), (W - 0.04, 0.2, 0.12), "NB_Rubber", 0.03, detail=detail)
            A.qn(g, [(-0.26, 0.42, z + s * 0.065), (0.26, 0.42, z + s * 0.065), (0.26, 0.54, z + s * 0.065),
                     (-0.26, 0.54, z + s * 0.065)] if s > 0 else
                 [(0.26, 0.42, z + s * 0.065), (-0.26, 0.42, z + s * 0.065), (-0.26, 0.54, z + s * 0.065),
                  (0.26, 0.54, z + s * 0.065)], (0, 0, s), "NB_Trim", A.rect_uv(0.0, *A.band_v(0)[:1], 0.3, A.band_v(0)[1]))
            lm = "NB_Lamp" if s < 0 else "NB_Paint"
            for x in (-hw + 0.28, hw - 0.28):
                A.bbox(g, (x, belt - 0.12, (-hl + 0.01) if s < 0 else (hl - 0.01)), (0.34, 0.14, 0.04), lm, 0.0)
        for s in (-1, 1):
            A.bbox(g, (s * (hw + 0.08), belt + 0.12, zf + 0.12), (0.14, 0.1, 0.08), "NB_CarPaint", 0.02)
        if detail == 0:
            # door shut lines + handles
            for s in (-1, 1):
                for zz in ((zf + zb) / 2, zb - 0.05):
                    A.qn(g, [(s * (hw + 0.002), 0.3, zz - 0.01), (s * (hw + 0.002), belt, zz - 0.01),
                             (s * (hw + 0.002), belt, zz + 0.01), (s * (hw + 0.002), 0.3, zz + 0.01)] if s > 0 else
                         [(s * (hw + 0.002), 0.3, zz + 0.01), (s * (hw + 0.002), belt, zz + 0.01),
                          (s * (hw + 0.002), belt, zz - 0.01), (s * (hw + 0.002), 0.3, zz - 0.01)], (s, 0, 0), "NB_Rubber")
                    A.bbox(g, (s * (hw + 0.01), belt - 0.1, zz - 0.25), (0.03, 0.03, 0.16), "NB_Chrome", 0.0)
    # wheel arches (dark) + wheels
    wz = hl - 0.72 if L > 4 else hl - 0.55
    for z in (-wz, wz):
        for s in (-1, 1):
            wheel(g, (s * (hw - 0.14), wheel_r, z), wheel_r, 0.21, detail)
    if taxi:
        A.bbox(g, (0, H + 0.14, (tf + tb) / 2), (0.62, 0.22, 0.26), "NB_Lamp", 0.04, detail=detail)
    return g


def car_kei(d):
    return car(d, L=3.4, W=1.47, H=1.62, cab=(0.24, 0.86), belt=0.86, wheel_r=0.28, nose=0.4)


def car_sedan(d):
    return car(d)


def car_van(d):
    return car(d, L=4.7, W=1.8, H=1.85, cab=(0.2, 0.97), belt=0.92, wheel_r=0.33, nose=0.7, van=True)


def car_taxi(d):
    return car(d, taxi=True)


def food_truck(detail):
    g = Geo()
    L, W = 5.4, 2.1
    hl, hw = L / 2, W / 2
    # cab (front, -z) + box body
    A.bbox(g, (0, 1.0, -hl + 0.9), (W, 1.4, 1.8), "NB_CarPaint", 0.12 if detail == 0 else 0, detail=detail)
    frustum(g, hw - 0.05, (-hl + 0.3, -hl + 1.75), hw - 0.12, (-hl + 0.75, -hl + 1.75), 1.7, 2.35, "NB_CarGlass",
            "NB_CarGlass", "NB_CarPaint")
    A.bbox(g, (0, 1.65, 0.85), (W, 2.6, 3.6), "NB_CarPaint", 0.1 if detail == 0 else 0, detail=detail)
    # serving hatch on +x side: recess to shop interior, counter, lifted awning flap
    F = A.Frame((hw + 0.001, -0.6), (hw + 0.001, 2.2))
    cu = A.shop_uv(1)
    A.qn(g, [F.P(0.3, 1.2, -0.02), F.P(2.5, 1.2, -0.02), F.P(2.5, 2.4, -0.02), F.P(0.3, 2.4, -0.02)], F.n, "NB_ShopInterior",
         A.rect_uv(*cu))
    if detail < 2:
        A._frame_box(g, F, F.P(1.4, 1.18, 0.2), 2.4, 0.06, 0.45, A.TRIM_WHITE)
        p0, p1 = F.P(0.2, 2.5, 0.02), F.P(2.6, 2.5, 0.02)
        p2, p3 = F.P(2.6, 2.75, 1.1), F.P(0.2, 2.75, 1.1)
        A.qn(g, [p0, p1, p2, p3], (0.3, 1, 0), "NB_CarPaint")
        A.qn(g, [p0, p1, p2, p3], (-0.3, -1, 0), "NB_Trim")
        for s in (0.3, 2.5):
            A.seg_box(g, F.P(s, 2.4, 0.0), F.P(s, 2.72, 1.05), 0.03, 0.03, "NB_Chrome")
        # menu board sign
        A.qn(g, [F.P(0.4, 2.9 - 0.4, 0.01), F.P(2.4, 2.9 - 0.4, 0.01), F.P(2.4, 2.95, 0.01), F.P(0.4, 2.95, 0.01)], F.n,
             "NB_Signs2", A.rect_uv(*A.sign_uv(A.SIGN_ISLAND_POKE)))
        # roof vent + stripe
        A.bbox(g, (0, 3.0, 1.2), (0.6, 0.18, 0.6), "NB_Metal", 0.03)
    for z in (-hl + 0.8, hl - 0.8):
        for s in (-1, 1):
            wheel(g, (s * (hw - 0.16), 0.36, z), 0.36, 0.24, detail)
    return g


def scooter(detail):
    g = Geo()
    wheel(g, (0, 0.23, -0.62), 0.23, 0.1, detail)
    wheel(g, (0, 0.23, 0.6), 0.23, 0.1, detail)
    A.bbox(g, (0, 0.34, 0.05), (0.32, 0.12, 0.9), "NB_CarPaint", 0.04, detail=detail)   # footboard
    A.bbox(g, (0, 0.62, 0.52), (0.38, 0.42, 0.62), "NB_CarPaint", 0.1 if detail == 0 else 0, detail=detail)  # rear body
    A.bbox(g, (0, 0.88, 0.5), (0.3, 0.1, 0.6), "NB_Rubber", 0.04, detail=detail)       # seat
    A.seg_box(g, (0, 0.34, -0.42), (0, 1.02, -0.58), 0.34, 0.1, "NB_CarPaint")         # leg shield
    if detail < 2:
        A.seg_box(g, (0, 0.5, -0.6), (0, 1.1, -0.56), 0.05, 0.05, "NB_Chrome")
        A.seg_box(g, (-0.32, 1.1, -0.56), (0.32, 1.1, -0.56), 0.035, 0.035, "NB_Rubber")
        A.bbox(g, (0, 1.0, -0.64), (0.16, 0.1, 0.06), "NB_Lamp", 0.0)
    return g


# ------------------------------------------------------------------ street furniture
def street_light(detail):
    g = Geo()
    seg = (12, 8, 5)[detail]
    A.cyl(g, (0, 0, 0), 0.16, 0.5, seg, "NB_Metal", top=True)
    A.cyl(g, (0, 0.5, 0), 0.085, 7.5, seg, "NB_Metal", top=True, r_top=0.06)
    if detail < 2:
        A.seg_box(g, (0, 7.6, 0), (0, 8.0, -1.9), 0.06, 0.06, "NB_Metal")
        A.bbox(g, (0, 7.96, -2.05), (0.34, 0.14, 0.7), "NB_Metal", 0.04, detail=detail)
        A.qn(g, [(-0.14, 7.88, -2.35), (0.14, 7.88, -2.35), (0.14, 7.88, -1.75), (-0.14, 7.88, -1.75)], (0, -1, 0),
             "NB_Lamp")
        # banner bracket (small hanging pennant, awning colour)
        v0, v1 = A.band_v(3)
        A.qn(g, [(0, 4.2, 0.1), (0, 4.2, 0.62), (0, 5.4, 0.62), (0, 5.4, 0.1)], (1, 0, 0), "NB_Awning",
             [(0, v0), (0.4, v0), (0.4, v1), (0, v1)])
        A.qn(g, [(0, 4.2, 0.62), (0, 4.2, 0.1), (0, 5.4, 0.1), (0, 5.4, 0.62)], (-1, 0, 0), "NB_Awning",
             [(0, v0), (0.4, v0), (0.4, v1), (0, v1)])
    return g


def bus_stop(detail):
    g = Geo()
    L = 4.0
    A.bbox(g, (0, 2.5, 0), (L + 0.4, 0.12, 1.7), "NB_Accent", 0.03, detail=detail)
    for x in (-L / 2, L / 2):
        A.bbox(g, (x, 1.25, 0.6), (0.1, 2.5, 0.1), "NB_Metal", 0.0)
    A.qn(g, [(L / 2, 0.15, 0.6), (-L / 2, 0.15, 0.6), (-L / 2, 2.44, 0.6), (L / 2, 2.44, 0.6)], (0, 0, -1),
         "NB_BalconyGlass")
    A.qn(g, [(-L / 2, 0.15, 0.6), (L / 2, 0.15, 0.6), (L / 2, 2.44, 0.6), (-L / 2, 2.44, 0.6)], (0, 0, 1),
         "NB_BalconyGlass")
    if detail < 2:
        A.bbox(g, (0, 0.46, 0.35), (L - 0.6, 0.06, 0.42), "NB_Teak", 0.02)
        for x in (-1.2, 1.2):
            A.bbox(g, (x, 0.22, 0.35), (0.06, 0.44, 0.36), "NB_Metal", 0.0)
        # timetable panel + route sign pole
        A.qn(g, [(L / 2 - 0.05, 0.9, 0.35), (L / 2 - 0.05, 0.9, -0.3), (L / 2 - 0.05, 2.1, -0.3), (L / 2 - 0.05, 2.1, 0.35)],
             (-1, 0, 0), "NB_Signs2", A.rect_uv(*A.sign_uv(A.SIGN_NAGISA_BOOKS)))
        A.cyl(g, (-L / 2 - 0.8, 0, -0.4), 0.05, 2.9, 6, "NB_Metal")
        A.cyl(g, (-L / 2 - 0.8, 2.6, -0.4), 0.28, 0.06, 12, "NB_Paint", top=True)
    return g


def bike_rack(detail):
    g = Geo()
    for k in range(5):
        x = -2.0 + k
        a, b = (x, 0, -0.35), (x, 0, 0.35)
        A.seg_box(g, a, (x, 0.75, -0.35), 0.05, 0.05, "NB_Metal")
        A.seg_box(g, b, (x, 0.75, 0.35), 0.05, 0.05, "NB_Metal")
        A.seg_box(g, (x, 0.8, -0.35), (x, 0.8, 0.35), 0.05, 0.05, "NB_Metal")
    return g


def bollard(detail):
    g = Geo()
    seg = (10, 6, 4)[detail]
    A.cyl(g, (0, 0, 0), 0.11, 0.9, seg, "NB_Metal", top=True, r_top=0.1)
    if detail < 2:
        A.cyl(g, (0, 0.72, 0), 0.113, 0.07, seg, "NB_Lamp", top=False)
    return g


def planter(detail):
    g = Geo()
    A.bbox(g, (0, 0.3, 0), (1.8, 0.6, 0.9), "NB_Stone", 0.05, detail=detail)
    A.qn(g, [(-0.8, 0.56, -0.35), (0.8, 0.56, -0.35), (0.8, 0.56, 0.35), (-0.8, 0.56, 0.35)], (0, 1, 0), "NB_Groundcover")
    if detail < 2:
        for x in (-0.5, 0.0, 0.5):
            A.plant_cards(g, (x, 0.56, 0), 0.75, 0.38, "NB_Hibiscus" if x else "NB_Bougainvillea", n=3 if detail == 0 else 2)
    return g


def rounded_mass(g, c, rx, ry, rz, mat, rings, segs, flat_bottom=True):
    """Clipped topiary / crown: squashed UV sphere (flat bottom), textured by planar projection."""
    pts = []
    for i in range(rings + 1):
        phi = (math.pi / 2) * (1 - i / rings) if flat_bottom else math.pi * (1 - i / rings) - math.pi / 2
        pts.append(phi)
    rows = []
    for phi in pts:
        y = ry * math.sin(phi)
        rr = math.cos(phi)
        rows.append([(c[0] + rx * rr * math.cos(t / segs * math.tau), c[1] + y, c[2] + rz * rr * math.sin(t / segs * math.tau))
                     for t in range(segs)])
    rows.reverse()   # bottom -> top
    for r in range(len(rows) - 1):
        for t in range(segs):
            u = (t + 1) % segs
            A.qc(g, [rows[r][t], rows[r][u], rows[r + 1][u], rows[r + 1][t]], (c[0], c[1] - ry * 0.3, c[2]), mat)
    if flat_bottom:
        A.qn(g, list(reversed(rows[0])), (0, -1, 0), mat)


def hedge_seg(detail):
    g = Geo()
    A.bbox(g, (0, 0.36, 0), (4.0, 0.72, 0.62), "NB_Hedge", 0.14 if detail == 0 else 0.0, detail=detail)
    if detail < 2:
        # rounded top
        for k in range(4 if detail == 0 else 2):
            n = 4 if detail == 0 else 2
            x = -2.0 + (k + 0.5) * 4.0 / n
            rounded_mass(g, (x, 0.66, 0), 4.0 / n / 2 + 0.05, 0.2, 0.33, "NB_Hedge", 2, 8 if detail == 0 else 5)
    return g


def street_tree_a(detail):
    """Clipped fukugi-like round crown on a 2.2 m clear trunk + tree grate."""
    g = Geo()
    seg = (8, 6, 4)[detail]
    A.cyl(g, (0, 0, 0), 0.13, 2.6, seg, "NB_PalmBark", top=False, r_top=0.1)
    rounded_mass(g, (0, 2.2, 0), 1.45, 2.1, 1.45, "NB_Hedge", (4, 3, 2)[detail], (12, 8, 5)[detail])
    if detail < 2:
        A.qn(g, [(-0.7, 0.03, -0.7), (0.7, 0.03, -0.7), (0.7, 0.03, 0.7), (-0.7, 0.03, 0.7)], (0, 1, 0), "NB_Metal")
    return g


def street_tree_b(detail):
    """Flat umbrella crown (small monkeypod / tabebuia style), 3 clipped tiers."""
    g = Geo()
    seg = (8, 6, 4)[detail]
    A.cyl(g, (0, 0, 0), 0.15, 3.0, seg, "NB_PalmBark", top=False, r_top=0.1)
    for (y, r, h) in ((2.8, 2.3, 0.7), (3.3, 1.7, 0.6), (3.8, 1.0, 0.45))[:(3, 2, 1)[detail]]:
        rounded_mass(g, (0, y, 0), r, h, r, "NB_Hedge", (3, 2, 2)[detail], (12, 8, 5)[detail])
    if detail == 0:
        for k in range(3):
            a = k * 2.1
            A.seg_box(g, (0, 2.3, 0), (math.cos(a) * 1.2, 3.0, math.sin(a) * 1.2), 0.08, 0.08, "NB_PalmBark")
        A.qn(g, [(-0.7, 0.03, -0.7), (0.7, 0.03, -0.7), (0.7, 0.03, 0.7), (-0.7, 0.03, 0.7)], (0, 1, 0), "NB_Metal")
    return g


# ------------------------------------------------------------------ beach
def rental_stand(detail):
    g = Geo()
    A.bbox(g, (0, 0.55, 0), (2.4, 1.1, 1.2), "NB_WallBoard", 0.03, detail=detail)
    A.bbox(g, (0, 1.12, -0.05), (2.6, 0.06, 1.4), "NB_Teak", 0.02, detail=detail)
    for x in (-1.15, 1.15):
        A.bbox(g, (x, 1.8, 0.5), (0.08, 1.4, 0.08), "NB_Teak", 0.0)
    v0, v1 = A.band_v(0)
    A.qn(g, [(-1.4, 2.5, -0.6), (1.4, 2.5, -0.6), (1.4, 2.7, 0.8), (-1.4, 2.7, 0.8)], (0, 1, -0.15), "NB_Awning",
         [(0, v1), (2.4, v1), (2.4, v0), (0, v0)])
    A.qn(g, [(-1.4, 2.5, -0.6), (1.4, 2.5, -0.6), (1.4, 2.7, 0.8), (-1.4, 2.7, 0.8)], (0, -1, 0.15), "NB_Awning",
         [(0, v1), (2.4, v1), (2.4, v0), (0, v0)])
    A.qn(g, [(-1.0, 0.25, -0.61), (1.0, 0.25, -0.61), (1.0, 0.75, -0.61), (-1.0, 0.75, -0.61)], (0, 0, -1), "NB_Signs2",
         A.rect_uv(*A.sign_uv(A.SIGN_TIDA_SURF)))
    if detail < 2:
        # board rack with 6 boards + stacked SUPs
        for k in range(6 if detail == 0 else 3):
            A.bbox(g, (1.9 + (k % 2) * 0.2, 1.15, -0.9 + k * 0.36), (0.07, 2.3, 0.5), BOARD_MATS[k % 3], 0.03, detail=detail)
        for k in range(3):
            A.bbox(g, (-2.2, 0.1 + k * 0.14, 0), (0.8, 0.12, 3.2), "NB_Gelcoat", 0.04, detail=detail)
    return g


def volley_net(detail):
    g = Geo()
    w = 9.5
    for x in (-w / 2, w / 2):
        A.cyl(g, (x, 0, 0), 0.05, 2.6, (8, 6, 4)[detail], "NB_Metal")
    A.bbox(g, (0, 2.38, 0), (w, 0.06, 0.02), "NB_Canvas", 0.0)
    if detail < 2:
        nx = 24 if detail == 0 else 8
        for k in range(nx + 1):
            x = -w / 2 + k * w / nx
            A.bbox(g, (x, 1.9, 0), (0.012, 0.95, 0.012), "NB_Rubber", 0.0)
        for yy in (1.45, 1.75, 2.05):
            A.bbox(g, (0, yy, 0), (w, 0.012, 0.012), "NB_Rubber", 0.0)
    # court boundary lines (flat tapes on sand)
    if detail < 2:
        for z in (-8.0, 8.0):
            A.qn(g, [(-w / 2, 0.02, z - 0.03), (w / 2, 0.02, z - 0.03), (w / 2, 0.02, z + 0.03), (-w / 2, 0.02, z + 0.03)],
                 (0, 1, 0), "NB_Canvas")
        for x in (-w / 2, w / 2):
            A.qn(g, [(x - 0.03, 0.02, -8.0), (x + 0.03, 0.02, -8.0), (x + 0.03, 0.02, 8.0), (x - 0.03, 0.02, 8.0)],
                 (0, 1, 0), "NB_Canvas")
    return g


def cabana(detail):
    g = Geo()
    s = 1.5
    for x in (-s, s):
        for z in (-s, s):
            A.bbox(g, (x, 1.3, z), (0.12, 2.6, 0.12), "NB_Teak", 0.02, detail=detail)
    A.bbox(g, (0, 2.62, 0), (2 * s + 0.4, 0.14, 2 * s + 0.4), "NB_Teak", 0.03, detail=detail)
    A.qn(g, [(-s - 0.3, 2.7, -s - 0.3), (s + 0.3, 2.7, -s - 0.3), (0, 3.4, 0)], (0, 1, -0.6), "NB_Canvas")
    A.qn(g, [(s + 0.3, 2.7, -s - 0.3), (s + 0.3, 2.7, s + 0.3), (0, 3.4, 0)], (0.6, 1, 0), "NB_Canvas")
    A.qn(g, [(s + 0.3, 2.7, s + 0.3), (-s - 0.3, 2.7, s + 0.3), (0, 3.4, 0)], (0, 1, 0.6), "NB_Canvas")
    A.qn(g, [(-s - 0.3, 2.7, s + 0.3), (-s - 0.3, 2.7, -s - 0.3), (0, 3.4, 0)], (-0.6, 1, 0), "NB_Canvas")
    # daybed + cushions
    A.bbox(g, (0, 0.25, 0.2), (2.2, 0.5, 2.0), "NB_Teak", 0.03, detail=detail)
    A.bbox(g, (0, 0.58, 0.2), (2.1, 0.16, 1.9), "NB_Canvas", 0.05, detail=detail)
    if detail < 2:
        for x in (-0.6, 0.6):
            A.bbox(g, (x, 0.8, 1.0), (0.8, 0.35, 0.18), "NB_Awning", 0.06, detail=detail)
        # tied-back curtains at the four corners
        for x in (-s, s):
            for z in (-s, s):
                A.bbox(g, (x * 0.93, 1.35, z * 0.93), (0.28, 2.4, 0.28), "NB_Canvas", 0.08, detail=detail)
    return g


def towel(detail):
    g = Geo()
    v0, v1 = A.band_v(5)
    A.qn(g, [(-0.45, 0.015, -0.9), (0.45, 0.015, -0.9), (0.45, 0.015, 0.9), (-0.45, 0.015, 0.9)], (0, 1, 0), "NB_Awning",
         [(0, v0), (0.9, v0), (0.9, v1), (0, v1)])
    return g


def parasol(detail):
    g = Geo()
    seg = (12, 8, 6)[detail]
    A.cyl(g, (0, 0, 0), 0.025, 2.1, 6, "NB_Metal")
    ring = [(1.0 * math.cos(i / seg * math.tau), 1.9, 1.0 * math.sin(i / seg * math.tau)) for i in range(seg)]
    for i in range(seg):
        j = (i + 1) % seg
        band = (i // 1) % 2 * 3
        v0, v1 = A.band_v(band)
        tri = [ring[j], ring[i], (0, 2.2, 0)]
        g.face(tri, "NB_Awning", [(0.2, v0), (0.0, v0), (0.1, v1)])
        g.face(list(reversed(tri)), "NB_Awning", [(0.1, v1), (0.0, v0), (0.2, v0)])
    return g


def stroller(detail):
    g = Geo()
    A.bbox(g, (0, 0.55, 0), (0.46, 0.3, 0.7), "NB_CarPaint", 0.05, detail=detail)
    A.seg_box(g, (0, 0.5, 0.35), (0, 1.05, 0.55), 0.42, 0.03, "NB_Metal")
    A.seg_box(g, (-0.22, 1.05, 0.55), (0.22, 1.05, 0.55), 0.03, 0.03, "NB_Rubber")
    if detail < 2:
        # canopy hood
        for k in range(4):
            a0, a1 = k / 4 * math.pi / 2, (k + 1) / 4 * math.pi / 2
            p = lambda a, x: (x, 0.7 + 0.35 * math.sin(a), 0.3 - 0.35 * math.cos(a) + 0.35 * 0.0)
            A.qn(g, [p(a0, -0.24), p(a0, 0.24), p(a1, 0.24), p(a1, -0.24)], (0, 1, 0), "NB_Canvas")
    for z in (-0.28, 0.3):
        for s in (-1, 1):
            wheel(g, (s * 0.24, 0.1, z), 0.1, 0.04, max(detail, 1))
    return g


def surfboard(detail):
    """Carried board, long axis y (held upright under the arm), 2.1 m."""
    g = Geo()
    A.bbox(g, (0, 1.05, 0), (0.07, 2.1, 0.52), "NB_Gelcoat", 0.03 if detail == 0 else 0, detail=detail)
    if detail < 2:
        A.qn(g, [(0.037, 0.3, -0.04), (0.037, 0.3, 0.04), (0.037, 1.9, 0.04), (0.037, 1.9, -0.04)], (1, 0, 0), "NB_Paint")
        A.qn(g, [(-0.037, 0.3, 0.04), (-0.037, 0.3, -0.04), (-0.037, 1.9, -0.04), (-0.037, 1.9, 0.04)], (-1, 0, 0), "NB_Paint")
    return g


# ------------------------------------------------------------------ small craft (waterline y = 0)
def jet_ski(detail):
    g = Geo()
    A.bbox(g, (0, 0.2, 0), (1.1, 0.5, 3.1), "NB_Gelcoat", 0.14 if detail == 0 else 0, detail=detail)
    A.bbox(g, (0, 0.55, -0.9), (0.9, 0.35, 0.9), "NB_PaintTeal", 0.12 if detail == 0 else 0, detail=detail)
    A.bbox(g, (0, 0.62, 0.35), (0.42, 0.22, 1.2), "NB_Rubber", 0.08 if detail == 0 else 0, detail=detail)
    if detail < 2:
        A.seg_box(g, (-0.4, 0.95, -0.55), (0.4, 0.95, -0.55), 0.04, 0.04, "NB_Rubber")
        A.bbox(g, (0, 0.8, -0.55), (0.1, 0.3, 0.1), "NB_Metal", 0.0)
    return g


def sup(detail):
    g = Geo()
    A.bbox(g, (0, 0.06, 0), (0.78, 0.12, 3.3), "NB_Gelcoat", 0.05 if detail == 0 else 0, detail=detail)
    if detail < 2:
        A.qn(g, [(-0.3, 0.125, -0.3), (0.3, 0.125, -0.3), (0.3, 0.125, 0.6), (-0.3, 0.125, 0.6)], (0, 1, 0), "NB_PaintTeal")
    return g


def banana_boat(detail):
    g = Geo()
    seg = (12, 8, 6)[detail]
    hcyl(g, (0, 0.3, 0), 0.38, 5.0, seg, "NB_Lamp", axis="z", cap_mat="NB_Lamp")      # yellow tube
    for s in (-1, 1):
        hcyl(g, (s * 0.55, 0.18, 0.4), 0.2, 4.0, seg, "NB_Paint", axis="z")          # side pontoons
    if detail < 2:
        for k in range(5):
            z = -1.6 + k * 0.8
            A.seg_box(g, (-0.3, 0.72, z), (0.3, 0.72, z), 0.04, 0.04, "NB_Rubber")   # handles
    return g


def dog(detail):
    """Medium dog (shiba-ish), 0.75 m long, facing -z. Walks with its owner (parented)."""
    g = Geo()
    A.bbox(g, (0, 0.42, 0), (0.24, 0.24, 0.56), "NB_Teak", 0.06 if detail == 0 else 0, detail=detail)
    A.bbox(g, (0, 0.58, -0.34), (0.2, 0.2, 0.22), "NB_Teak", 0.05 if detail == 0 else 0, detail=detail)
    A.bbox(g, (0, 0.53, -0.49), (0.11, 0.1, 0.12), "NB_Canvas", 0.03, detail=detail)
    if detail < 2:
        for s in (-1, 1):
            A.seg_box(g, (s * 0.06, 0.68, -0.34), (s * 0.07, 0.78, -0.32), 0.06, 0.03, "NB_Teak")   # ears
            for z in (-0.2, 0.2):
                A.bbox(g, (s * 0.08, 0.16, z), (0.07, 0.32, 0.07), "NB_Teak", 0.0)
        A.seg_box(g, (0, 0.5, 0.27), (0, 0.66, 0.36), 0.06, 0.06, "NB_Teak")                         # tail
    return g


PROPS = {
    "Nagisa_S_CarKei": car_kei, "Nagisa_S_CarSedan": car_sedan, "Nagisa_S_CarVan": car_van,
    "Nagisa_S_CarTaxi": car_taxi, "Nagisa_S_FoodTruck": food_truck, "Nagisa_S_Scooter": scooter,
    "Nagisa_S_StreetLight": street_light, "Nagisa_S_BusStop": bus_stop, "Nagisa_S_BikeRack": bike_rack,
    "Nagisa_S_Bollard": bollard, "Nagisa_S_Planter": planter, "Nagisa_S_HedgeSeg": hedge_seg,
    "Nagisa_S_StreetTreeA": street_tree_a, "Nagisa_S_StreetTreeB": street_tree_b,
    "Nagisa_S_RentalStand": rental_stand, "Nagisa_S_VolleyNet": volley_net, "Nagisa_S_Cabana": cabana,
    "Nagisa_S_Towel": towel, "Nagisa_S_Parasol": parasol, "Nagisa_S_Stroller": stroller,
    "Nagisa_S_Surfboard": surfboard, "Nagisa_S_JetSki": jet_ski, "Nagisa_S_SUP": sup,
    "Nagisa_S_BananaBoat": banana_boat, "Nagisa_S_Dog": dog,
}


def main():
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PROPS)
    for name in want:
        counts = C.build_lods(name, PROPS[name], name + ".glb")
        print("[nagisa-s] %-24s %s" % (name, " / ".join("{:,}".format(x) for x in counts)))


main()

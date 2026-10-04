"""
NAGISA BAY - NB8B animated attractions kit (COORDINATION.md NB8, sub-task 8B).

Builds, in Blender 4.5, the GLBs staged by Assets/Editor/NagisaBayEnvironment.Attractions.cs:
  Shiokaze Pier (fictional):   PierDeck, PierRail, PierArch, WheelFrame, WheelRim, WheelCabin,
                               CoasterCar
  Palm Ridge Gondola (fict.):  GondolaStation, Bullwheel, GondolaCabin, TowerMast, TowerHead,
                               TowerFooting
Everything is authored in UNITY coordinates through build_nagisa_common.Geo (u2b applied once
in Geo.to_object), with bevelled/chamfered edges, named material slots only (NB8B_* remapped in
Unity to CelLit / HDRP-Lit), and authored LOD0/1/2 exported as <stem>_LODn siblings.

The numbers the C# stage must agree with are the CONTRACT block below (keep them in sync with
the constants at the top of NagisaBayEnvironment.Attractions.cs). Art values are PROVISIONAL.

Run:  blender -b -P tools/blender/build_nagisa_attractions_models.py
"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402  (read-only use of the NB2 helper module)
from build_nagisa_common import Geo, add, sub, mul, norm, cross, rot_y  # noqa: E402

# ------------------------------------------------------------------ CONTRACT (mirror in C#)
DECK_BAY_W = 18.0          # PierDeck width (x), metres
DECK_BAY_L = 20.0          # PierDeck length (z)
PILE_DEPTH = 16.0          # piles reach this far below the deck top
WHEEL_HUB_Y = 26.0         # hub height above the deck (WheelFrame / WheelRim origin)
WHEEL_R = 22.0             # cabin pin radius
WHEEL_PINS = 24            # cabins
RIM_Z = 1.8                # the two rims sit at z = +-RIM_Z (wheel axis = local z)
STATION_CABLE_Y = 6.3      # gondola cable height in the station (local y)
CABLE_GAUGE = 3.2          # up/down cable lines at x = +-CABLE_GAUGE
BULLWHEEL_Z = -8.0         # bullwheel centre (local z) inside the station
HEAD_CABLE_Y = 1.2         # cable height above the tower-head origin (mast top)

TILE_EXTRA = {
    "NB8B_Wood": (1.12, 1.12), "NB8B_TimberDark": (1.5, 1.5), "NB8B_Concrete": (3.0, 3.0),
    "NB8B_PaintWhite": (1.0, 1.0), "NB8B_Steel": (1.0, 1.0), "NB8B_Glass": (2.0, 2.0),
    "NB8B_Bulb": (1.0, 1.0), "NB8B_SignGlow": (1.0, 1.0), "NB8B_SignBoard": (1.0, 1.0),
    "NB8B_CabinPaint": (1.0, 1.0), "NB8B_CoasterPaint": (1.0, 1.0), "NB8B_Seat": (1.0, 1.0),
    "NB8B_Skin": (1.0, 1.0), "NB8B_Shirt": (1.0, 1.0), "NB8B_Hair": (1.0, 1.0),
    "NB8B_Roof": (2.0, 2.0), "NB8B_Rubber": (1.0, 1.0),
}
C.TILE.update(TILE_EXTRA)

X = (1.0, 0.0, 0.0)
Y = (0.0, 1.0, 0.0)
Z = (0.0, 0.0, 1.0)


# ------------------------------------------------------------------ extra primitives
def tube(g, a, b, r, seg, mat, caps=False, r_b=None):
    """Cylinder between two arbitrary Unity-space points (same winding as Geo.cylinder)."""
    d = sub(b, a)
    L = math.sqrt(C.dot(d, d))
    if L < 1e-5:
        return
    d = mul(d, 1.0 / L)
    ref = X if abs(d[0]) < 0.9 else Z
    u = norm(cross(ref, d))
    w = norm(cross(u, d))
    rb = r if r_b is None else r_b
    r0, r1 = [], []
    for i in range(seg + 1):
        t = i / seg * math.tau
        o = add(mul(u, math.cos(t)), mul(w, math.sin(t)))
        r0.append(add(a, mul(o, r)))
        r1.append(add(b, mul(o, rb)))
    for i in range(seg):
        u0, u1 = i / seg, (i + 1) / seg
        g.quad(r0[i], r1[i], r1[i + 1], r0[i + 1], mat, uvs=[(u0, 0), (u0, L), (u1, L), (u1, 0)])
    if caps:
        g.face([r1[i] for i in range(seg, 0, -1)], mat)
        g.face([r0[i] for i in range(seg)], mat)


def _outline(hx, hz, b):
    """Chamfered rectangle, angle-ordered from +x toward +z (Geo.prism convention)."""
    b = min(b, hx * 0.45, hz * 0.45)
    return [(hx, -hz + b), (hx, hz - b), (hx - b, hz), (-hx + b, hz),
            (-hx, hz - b), (-hx, -hz + b), (-hx + b, -hz), (hx - b, -hz)]


def bbox(g, c, size, mat, bev=0.04, yaw=0.0, bottom=False, top_mat=None):
    """Bevelled box: chamfered vertical edges + a chamfered top rim. size = full extents."""
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    bev = min(bev, hy * 0.45)
    O = _outline(hx, hz, bev)
    I = _outline(max(hx - bev, 0.01), max(hz - bev, 0.01), bev * 0.5)

    def P(p, y):
        return add(c, rot_y((p[0], y, p[1]), yaw))
    y0, y1, y2 = -hy, hy - bev, hy
    n = len(O)
    for i in range(n):
        j = (i + 1) % n
        g.quad(P(O[i], y0), P(O[i], y1), P(O[j], y1), P(O[j], y0), mat)
        g.quad(P(O[i], y1), P(I[i], y2), P(I[j], y2), P(O[j], y1), mat)
    g.face([P(I[i], y2) for i in range(n - 1, -1, -1)], top_mat or mat)
    if bottom:
        g.face([P(O[i], y0) for i in range(n)], mat)


def seg_box(g, a, b, w, h, mat):
    """Straight beam (w x h section) between a and b, bevel-free, oriented by the segment."""
    d = sub(b, a)
    L = math.sqrt(C.dot(d, d))
    if L < 1e-5:
        return
    f = mul(d, 1.0 / L)
    ref = Y if abs(f[1]) < 0.95 else X
    s = norm(cross(ref, f))
    u = norm(cross(f, s))
    hs, hu = mul(s, w / 2), mul(u, h / 2)
    corners = [add(hs, hu), add(mul(hs, -1), hu), add(mul(hs, -1), mul(hu, -1)), add(hs, mul(hu, -1))]
    A = [add(a, k) for k in corners]
    B = [add(b, k) for k in corners]
    for i in range(4):
        j = (i + 1) % 4
        # outward check by centroid direction
        q = [A[i], B[i], B[j], A[j]]
        n = cross(sub(q[1], q[0]), sub(q[2], q[0]))
        mid = mul(add(add(q[0], q[1]), add(q[2], q[3])), 0.25)
        cen = mul(add(a, b), 0.5)
        if C.dot(n, sub(mid, cen)) < 0:
            q.reverse()
        g.quad(*q, mat)
    g.face([A[3], A[2], A[1], A[0]], mat)
    g.face([B[0], B[1], B[2], B[3]], mat)
    # fix cap winding the same way
    for cap, pts, out in ((-1, None, None),):
        pass


def ring_tube(g, centre, radius, axis, r, n, seg, mat, a0=0.0):
    """Polygonal torus-ish ring made of n straight tubes, in the plane normal to axis."""
    ref = Y if abs(C.dot(axis, Y)) < 0.9 else X
    u = norm(cross(axis, ref))
    w = norm(cross(axis, u))
    pts = []
    for i in range(n + 1):
        t = a0 + i / n * math.tau
        pts.append(add(centre, add(mul(u, radius * math.cos(t)), mul(w, radius * math.sin(t)))))
    for i in range(n):
        tube(g, pts[i], pts[i + 1], r, seg, mat)
    return pts


def text(g, body, centre, height, mat, depth=0.06, face_neg_z=True):
    """Blender text converted into Geo faces: reads +x, up +y, front faces -z (Unity)."""
    cu = bpy.data.curves.new("nb8b_txt", "FONT")
    cu.body = body
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    cu.extrude = depth / max(height, 1e-3)
    cu.resolution_u = 3
    ob = bpy.data.objects.new("nb8b_txt", cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = ob.evaluated_get(dg).to_mesh()
    s = height
    sign = -1.0 if face_neg_z else 1.0
    verts = [(v.co.x * s + centre[0], v.co.y * s + centre[1], sign * v.co.z * s + centre[2]) for v in me.vertices]
    for p in me.polygons:
        idx = list(p.vertices)
        if face_neg_z:
            idx.reverse()            # (x,y,z)->(x,y,-z) is a reflection: keep the front outward
        g.face([verts[i] for i in idx], mat, uvs=[(0.5, 0.5)] * len(idx))
    ob.evaluated_get(dg).to_mesh_clear()
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)


# ------------------------------------------------------------------ pier
def pier_deck(lvl):
    g = Geo()
    hw, L = DECK_BAY_W / 2, DECK_BAY_L
    if lvl == 0:
        n = int(L / 0.25)
        for i in range(n):
            z = (i + 0.5) * L / n
            bbox(g, (0, -0.045, z), (DECK_BAY_W - 0.04, 0.09, L / n - 0.025), "NB8B_Wood", bev=0.012)
    else:
        bbox(g, (0, -0.045, L / 2), (DECK_BAY_W, 0.09, L), "NB8B_Wood", bev=0.02)
    # stringers + fascia
    for x in ((-hw + 0.2, -6.0, 0.0, 6.0, hw - 0.2) if lvl == 0 else (-hw + 0.2, 0.0, hw - 0.2)):
        bbox(g, (x, -0.36, L / 2), (0.28, 0.54, L), "NB8B_TimberDark", bev=0.02)
    # pile caps + piles
    seg = (12, 8, 5)[lvl]
    for z in (1.0, 11.0):
        bbox(g, (0, -0.95, z), (DECK_BAY_W, 0.62, 0.7), "NB8B_TimberDark", bev=0.04)
        for x in (-7.5, 0.0, 7.5):
            tube(g, (x, -PILE_DEPTH, z), (x, -1.26, z), 0.34, seg, "NB8B_Concrete", r_b=0.30)
        if lvl == 0:   # X bracing between piles
            for x0, x1 in ((-7.5, 0.0), (0.0, 7.5)):
                tube(g, (x0, -1.6, z), (x1, -7.0, z), 0.09, 6, "NB8B_Steel")
                tube(g, (x1, -1.6, z), (x0, -7.0, z), 0.09, 6, "NB8B_Steel")
    return g


def pier_rail(lvl):
    """20 m rail run along +z at x = 0 with a lamp standard at z = 10 (outer side = -x)."""
    g = Geo()
    step = (2.5, 5.0, 10.0)[lvl]
    k = int(DECK_BAY_L / step)
    for i in range(k + 1):
        z = i * step
        bbox(g, (0, 0.55, z), (0.12, 1.1, 0.12), "NB8B_PaintWhite", bev=0.02)
    if lvl < 2:
        tube(g, (0, 1.12, 0), (0, 1.12, DECK_BAY_L), 0.055, (8, 5)[lvl], "NB8B_PaintWhite")
    else:
        bbox(g, (0, 1.1, DECK_BAY_L / 2), (0.12, 0.1, DECK_BAY_L), "NB8B_PaintWhite", bev=0.0)
    if lvl == 0:
        for y in (0.42, 0.78):
            tube(g, (0, y, 0), (0, y, DECK_BAY_L), 0.025, 5, "NB8B_PaintWhite")
    # lamp standard (Victorian-seaside style, fictional)
    seg = (10, 6, 4)[lvl]
    tube(g, (0.0, 0.0, 10.0), (0.0, 4.6, 10.0), 0.10, seg, "NB8B_Steel", r_b=0.07)
    bbox(g, (0.0, 0.18, 10.0), (0.34, 0.36, 0.34), "NB8B_Steel", bev=0.04)
    for sx in (-1.0, 1.0):
        tube(g, (0.0, 4.4, 10.0), (0.55 * sx, 4.55, 10.0), 0.035, 5, "NB8B_Steel")
        bbox(g, (0.62 * sx, 4.3, 10.0), (0.26, 0.40, 0.26), "NB8B_Bulb", bev=0.03)
        bbox(g, (0.62 * sx, 4.54, 10.0), (0.34, 0.08, 0.34), "NB8B_Steel", bev=0.02)
    if lvl == 0:   # string of festoon bulbs along the top rail
        for i in range(20):
            bbox(g, (0.0, 1.24, 0.5 + i), (0.09, 0.09, 0.09), "NB8B_Bulb", bev=0.0)
    return g


def pier_arch(lvl):
    """Entrance arch at z = 0, spanning x = +-8.2, sign faces -z (toward the promenade)."""
    g = Geo()
    for sx in (-1.0, 1.0):
        bbox(g, (8.2 * sx, 4.5, 0), (1.3, 9.0, 1.3), "NB8B_PaintWhite", bev=0.08)
        bbox(g, (8.2 * sx, 9.25, 0), (1.6, 0.5, 1.6), "NB8B_Roof", bev=0.08)
        bbox(g, (8.2 * sx, 0.3, 0), (1.7, 0.6, 1.7), "NB8B_Concrete", bev=0.06)
        tube(g, (8.2 * sx, 9.5, 0), (8.2 * sx, 10.9, 0), 0.05, 6, "NB8B_Steel")
        bbox(g, (8.2 * sx, 11.0, 0), (0.3, 0.3, 0.3), "NB8B_Bulb", bev=0.05)
    n = (16, 10, 6)[lvl]
    prev = None
    for i in range(n + 1):
        t = i / n
        x = -8.2 + 16.4 * t
        y = 8.2 + 1.6 * math.sin(math.pi * t)
        p = (x, y, 0.0)
        if prev is not None:
            seg_box(g, prev, p, 0.7, 0.6, "NB8B_PaintWhite")
            if lvl == 0:
                bbox(g, (x, y + 0.42, -0.36), (0.16, 0.16, 0.16), "NB8B_Bulb", bev=0.0)
        prev = p
    bbox(g, (0, 7.0, -0.05), (12.4, 2.0, 0.28), "NB8B_SignBoard", bev=0.06)
    if lvl < 2:
        text(g, "SHIOKAZE PIER", (0, 7.12, -0.24), 1.05, "NB8B_SignGlow", depth=0.05)
        if lvl == 0:
            text(g, "AMUSEMENTS  -  WHEEL  -  COASTER", (0, 6.35, -0.24), 0.36, "NB8B_SignGlow", depth=0.03)
    return g


# ------------------------------------------------------------------ Ferris wheel
def wheel_frame(lvl):
    g = Geo()
    seg = (12, 8, 5)[lvl]
    hub = (0.0, WHEEL_HUB_Y, 0.0)
    for zs in (-1.0, 1.0):
        foot_z = 5.2 * zs
        top = (0.0, WHEEL_HUB_Y, 2.9 * zs)
        for xs in (-1.0, 1.0):
            foot = (11.5 * xs, 0.0, foot_z)
            tube(g, foot, top, 0.55, seg, "NB8B_PaintWhite", r_b=0.38)
            bbox(g, (foot[0], 0.35, foot[2]), (2.2, 0.7, 2.2), "NB8B_Concrete", bev=0.08)
        if lvl < 2:
            for f in (0.3, 0.62):
                a = (-11.5 * (1 - f), WHEEL_HUB_Y * f, foot_z + (top[2] - foot_z) * f)
                b = (11.5 * (1 - f), WHEEL_HUB_Y * f, foot_z + (top[2] - foot_z) * f)
                tube(g, a, b, 0.22, seg, "NB8B_PaintWhite")
    tube(g, (0, WHEEL_HUB_Y, -3.3), (0, WHEEL_HUB_Y, 3.3), 0.7, seg, "NB8B_Steel", caps=True)
    # boarding platform + ticket booth + canopy (at the bottom of the wheel)
    bbox(g, (0, 0.5, -4.8), (9.0, 1.0, 5.0), "NB8B_Concrete", bev=0.06, top_mat="NB8B_Wood")
    for i in range(3):
        if lvl == 0:
            bbox(g, (-2.5 + 2.5 * i, 1.2, -7.3), (1.4, 0.42, 0.14), "NB8B_PaintWhite", bev=0.02)
    bbox(g, (6.8, 1.35, -6.5), (2.8, 2.7, 2.4), "NB8B_CabinPaint", bev=0.08)
    bbox(g, (6.8, 1.6, -7.72), (1.8, 0.9, 0.06), "NB8B_Glass", bev=0.0)
    bbox(g, (6.8, 2.9, -6.5), (3.4, 0.25, 3.0), "NB8B_Roof", bev=0.06)
    if lvl < 2:
        for x in (-4.2, 4.2):
            for z in (-7.0, -2.6):
                tube(g, (x, 1.0, z), (x, 3.8, z), 0.08, 6, "NB8B_Steel")
        bbox(g, (0, 3.95, -4.8), (9.2, 0.18, 5.0), "NB8B_Roof", bev=0.05)
    return g


def wheel_rim(lvl):
    """Origin = hub. Rotates about local z. Spokes, twin rims, truss, bulbs."""
    g = Geo()
    n = (48, 32, 24)[lvl]
    seg = (6, 5, 4)[lvl]
    rims = []
    for zs in (-1.0, 1.0):
        rims.append(ring_tube(g, (0, 0, RIM_Z * zs), WHEEL_R, Z, 0.24, n, seg, "NB8B_PaintWhite"))
        if lvl < 2:
            ring_tube(g, (0, 0, RIM_Z * zs), WHEEL_R - 2.2, Z, 0.13, n, seg, "NB8B_PaintWhite")
    hubr = 1.5
    tube(g, (0, 0, -2.8), (0, 0, 2.8), hubr, (16, 10, 8)[lvl], "NB8B_Steel", caps=True)
    for k in range(WHEEL_PINS):
        t = k / WHEEL_PINS * math.tau
        c, s = math.cos(t), math.sin(t)
        for zs in (-1.0, 1.0):
            hubp = (hubr * c, hubr * s, 2.6 * zs)
            rimp = (WHEEL_R * c, WHEEL_R * s, RIM_Z * zs)
            tube(g, hubp, rimp, 0.10, (5, 4, 3)[lvl], "NB8B_PaintWhite")
            if lvl == 0:
                for b in range(3, 11):
                    f = b / 11.0
                    p = add(mul(hubp, 1 - f), mul(rimp, f))
                    bbox(g, add(p, (0, 0, 0.14 * zs)), (0.18, 0.18, 0.18), "NB8B_Bulb", bev=0.0)
            if lvl == 1:
                for b in (4, 7, 10):
                    f = b / 11.0
                    p = add(mul(hubp, 1 - f), mul(rimp, f))
                    bbox(g, p, (0.28, 0.28, 0.28), "NB8B_Bulb", bev=0.0)
        # cabin pin axle between the rims
        tube(g, (WHEEL_R * c, WHEEL_R * s, -RIM_Z), (WHEEL_R * c, WHEEL_R * s, RIM_Z), 0.09, 6, "NB8B_Steel")
        if lvl == 0:   # truss diagonals outer <-> inner ring
            t2 = (k + 0.5) / WHEEL_PINS * math.tau
            for zs in (-1.0, 1.0):
                a = (WHEEL_R * c, WHEEL_R * s, RIM_Z * zs)
                b = ((WHEEL_R - 2.2) * math.cos(t2), (WHEEL_R - 2.2) * math.sin(t2), RIM_Z * zs)
                tube(g, a, b, 0.06, 4, "NB8B_PaintWhite")
    if lvl < 2:   # rim bulbs
        m = (96, 48)[lvl]
        for i in range(m):
            t = i / m * math.tau
            for zs in (-1.0, 1.0):
                bbox(g, ((WHEEL_R + 0.3) * math.cos(t), (WHEEL_R + 0.3) * math.sin(t), RIM_Z * zs),
                     (0.22, 0.22, 0.22), "NB8B_Bulb", bev=0.0)
    return g


def wheel_cabin(lvl):
    """Origin = the pin; hangs below it. Pin axis = local z."""
    g = Geo()
    for zs in (-1.0, 1.0):
        tube(g, (0, 0, 0.95 * zs), (0, -1.05, 0.95 * zs), 0.06, 6, "NB8B_Steel")
    bbox(g, (0, -1.15, 0), (2.5, 0.28, 2.3), "NB8B_CabinPaint", bev=0.12)
    bbox(g, (0, -1.02, 0), (1.5, 0.12, 1.3), "NB8B_Roof", bev=0.05)
    bbox(g, (0, -1.95, 0), (2.25, 1.35, 2.05), "NB8B_Glass", bev=0.1 if lvl == 0 else 0.05)
    bbox(g, (0, -3.0, 0), (2.4, 0.8, 2.2), "NB8B_CabinPaint", bev=0.14, bottom=True)
    if lvl == 0:
        for x in (-1.1, 1.1):
            for z in (-1.0, 1.0):
                bbox(g, (x, -1.95, z), (0.1, 1.4, 0.1), "NB8B_PaintWhite", bev=0.0)
        bbox(g, (0, -2.45, 0.55), (1.8, 0.4, 0.5), "NB8B_Seat", bev=0.05)
        bbox(g, (0, -2.45, -0.55), (1.8, 0.4, 0.5), "NB8B_Seat", bev=0.05)
        bbox(g, (0, -1.35, 0), (0.25, 0.1, 0.25), "NB8B_Bulb", bev=0.0)
    return g


# ------------------------------------------------------------------ coaster car
def coaster_car(lvl):
    """Origin = track centre at rail-top level; +z forward. 2.6 m long, 2 rows x 2 riders."""
    g = Geo()
    bbox(g, (0, 0.25, 0), (1.5, 0.3, 2.4), "NB8B_Steel", bev=0.05, bottom=True)
    bbox(g, (0, 0.7, 0.05), (1.6, 0.6, 2.5), "NB8B_CoasterPaint", bev=0.18 if lvl == 0 else 0.08)
    bbox(g, (0, 0.62, 1.25), (1.5, 0.5, 0.25), "NB8B_CoasterPaint", bev=0.12)
    if lvl < 2:
        for zr in (0.5, -0.6):
            bbox(g, (0, 1.2, zr - 0.3), (1.4, 0.8, 0.14), "NB8B_Seat", bev=0.05)
            for xs in (-0.36, 0.36):
                # rider: torso + head + hair cap (chibi-proportioned silhouettes)
                bbox(g, (xs, 1.3, zr), (0.46, 0.62, 0.34), "NB8B_Shirt", bev=0.1)
                tube(g, (xs, 1.62, zr), (xs, 1.68, zr), 0.08, 6, "NB8B_Skin")
                bbox(g, (xs, 1.92, zr), (0.46, 0.44, 0.44), "NB8B_Skin", bev=0.16)
                bbox(g, (xs, 2.08, zr - 0.02), (0.5, 0.2, 0.48), "NB8B_Hair", bev=0.08)
                if lvl == 0:   # arms up on the drop
                    tube(g, (xs + 0.18 * (1 if xs > 0 else -1), 1.55, zr),
                         (xs + 0.34 * (1 if xs > 0 else -1), 2.25, zr + 0.1), 0.06, 5, "NB8B_Shirt")
            tube(g, (-0.7, 1.35, zr + 0.3), (0.7, 1.35, zr + 0.3), 0.04, 5, "NB8B_Steel")
    for xs in (-0.55, 0.55):
        for zs in (-0.9, 0.9):
            tube(g, (xs - 0.08, 0.0, zs), (xs + 0.08, 0.0, zs), 0.14, (10, 6, 4)[lvl], "NB8B_Rubber", caps=True)
    return g


# ------------------------------------------------------------------ gondola
def gondola_cabin(lvl):
    """Origin = cable grip; +z = direction of travel; hangs below."""
    g = Geo()
    bbox(g, (0, -0.1, 0), (0.36, 0.34, 0.9), "NB8B_Steel", bev=0.05)
    tube(g, (0, -0.25, 0), (0, -1.9, 0.0), 0.07, (8, 6, 4)[lvl], "NB8B_Steel")
    tube(g, (0, -1.9, 0.0), (0, -2.35, 0.0), 0.12, 6, "NB8B_Steel")
    bbox(g, (0, -2.5, 0), (2.0, 0.3, 1.9), "NB8B_CabinPaint", bev=0.14)
    bbox(g, (0, -3.35, 0), (1.9, 1.4, 1.8), "NB8B_Glass", bev=0.12 if lvl == 0 else 0.05)
    bbox(g, (0, -4.35, 0), (2.0, 0.6, 1.9), "NB8B_CabinPaint", bev=0.16, bottom=True)
    if lvl == 0:
        for x in (-0.95, 0.95):
            for z in (-0.9, 0.9):
                bbox(g, (x, -3.35, z), (0.08, 1.45, 0.08), "NB8B_PaintWhite", bev=0.0)
        bbox(g, (0, -3.35, -0.91), (0.05, 1.4, 0.04), "NB8B_PaintWhite", bev=0.0)   # door split
        bbox(g, (0, -3.85, 0.55), (1.5, 0.35, 0.45), "NB8B_Seat", bev=0.05)
        bbox(g, (0, -3.85, -0.55), (1.5, 0.35, 0.45), "NB8B_Seat", bev=0.05)
    return g


def gondola_station(lvl, sign):
    """Origin = ground centre; cable leaves along +z at STATION_CABLE_Y; lines at x = +-3.2."""
    g = Geo()
    bbox(g, (0, -2.4, 1.0), (17.0, 7.2, 28.0), "NB8B_Concrete", bev=0.1)            # plinth (buried)
    bbox(g, (0, 1.3, 1.0), (16.0, 0.3, 27.0), "NB8B_Concrete", bev=0.05, top_mat="NB8B_Wood")
    seg = (10, 6, 4)[lvl]
    for x in (-7.4, 7.4):
        for z in (-11.5, -3.0, 5.5, 14.0):
            tube(g, (x, 1.4, z), (x, 11.0, z), 0.22, seg, "NB8B_Steel")
    # canopy roof (sloped, deep fascia) + sign
    bbox(g, (0, 11.3, 1.2), (18.4, 0.6, 29.2), "NB8B_Roof", bev=0.2)
    bbox(g, (0, 10.7, 15.7), (18.4, 1.3, 0.35), "NB8B_SignBoard", bev=0.08)
    if lvl < 2:
        text(g, sign, (0, 10.7, 15.9), 0.8, "NB8B_SignGlow", depth=0.05, face_neg_z=False)
    # glazed back hall (ticketing / queue) behind the bullwheel
    bbox(g, (0, 5.0, -11.8), (15.0, 7.2, 3.8), "NB8B_Glass", bev=0.08)
    bbox(g, (0, 1.9, -11.8), (15.2, 1.0, 4.0), "NB8B_PaintWhite", bev=0.06)
    # side glass screens along the platform + bullwheel housing beam
    for x in (-7.6, 7.6):
        bbox(g, (x, 2.6, 2.0), (0.12, 2.2, 22.0), "NB8B_Glass", bev=0.02)
        bbox(g, (x, 1.55, 2.0), (0.3, 0.3, 22.0), "NB8B_Steel", bev=0.03)
    bbox(g, (0, 9.4, BULLWHEEL_Z), (9.0, 0.7, 1.2), "NB8B_Steel", bev=0.06)
    tube(g, (0, 9.1, BULLWHEEL_Z), (0, STATION_CABLE_Y + 0.4, BULLWHEEL_Z), 0.35, seg, "NB8B_Steel")
    # detach rails that guide the cabins through the station (U around the bullwheel)
    if lvl < 2:
        for x in (-CABLE_GAUGE, CABLE_GAUGE):
            tube(g, (x, STATION_CABLE_Y + 0.35, BULLWHEEL_Z), (x, STATION_CABLE_Y + 0.35, 14.0), 0.09, 6, "NB8B_Steel")
        for i in range(6):
            z = BULLWHEEL_Z + 3.5 * i
            seg_box(g, (-CABLE_GAUGE, 9.2, z), (-CABLE_GAUGE, STATION_CABLE_Y + 0.45, z), 0.12, 0.12, "NB8B_Steel")
            seg_box(g, (CABLE_GAUGE, 9.2, z), (CABLE_GAUGE, STATION_CABLE_Y + 0.45, z), 0.12, 0.12, "NB8B_Steel")
    return g


def bullwheel(lvl):
    """Origin = centre; horizontal wheel rotating about local y; radius = CABLE_GAUGE."""
    g = Geo()
    n = (32, 20, 12)[lvl]
    ring_tube(g, (0, 0, 0), CABLE_GAUGE, Y, 0.2, n, (6, 5, 4)[lvl], "NB8B_PaintWhite")
    tube(g, (0, -0.35, 0), (0, 0.35, 0), 0.5, (12, 8, 6)[lvl], "NB8B_Steel", caps=True)
    for k in range(8):
        t = k / 8 * math.tau
        tube(g, (0.5 * math.cos(t), 0, 0.5 * math.sin(t)),
             (CABLE_GAUGE * math.cos(t), 0, CABLE_GAUGE * math.sin(t)), 0.08, 4, "NB8B_Steel")
    return g


def tower_mast(lvl):
    """Unit-height tapered mast y 0..1 (scaled in y by the stage)."""
    g = Geo()
    tube(g, (0, 0, 0), (0, 1, 0), 0.85, (12, 8, 6)[lvl], "NB8B_Steel", r_b=0.55)
    return g


def tower_head(lvl):
    """Origin = mast top. Crossarm along x with sheave trains under each cable at HEAD_CABLE_Y."""
    g = Geo()
    bbox(g, (0, 0.1, 0), (1.5, 0.6, 1.5), "NB8B_Steel", bev=0.08)
    bbox(g, (0, 0.45, 0), (2 * CABLE_GAUGE + 1.4, 0.7, 0.8), "NB8B_PaintWhite", bev=0.08)
    for xs in (-1.0, 1.0):
        x = CABLE_GAUGE * xs
        bbox(g, (x, 0.95, 0), (0.28, 0.4, 3.8), "NB8B_Steel", bev=0.04)
        n = (6, 3, 0)[lvl]
        for i in range(n):
            z = -1.5 + 3.0 * i / max(n - 1, 1)
            tube(g, (x - 0.12, HEAD_CABLE_Y - 0.2, z), (x + 0.12, HEAD_CABLE_Y - 0.2, z), 0.2, 8, "NB8B_Rubber", caps=True)
    if lvl == 0:   # service catwalk + handrail + aviation marker
        bbox(g, (0, 0.05, -1.2), (2 * CABLE_GAUGE + 1.0, 0.08, 0.9), "NB8B_Steel", bev=0.0)
        tube(g, (-CABLE_GAUGE - 0.5, 1.1, -1.6), (CABLE_GAUGE + 0.5, 1.1, -1.6), 0.03, 5, "NB8B_Steel")
        bbox(g, (0, 1.2, 0), (0.25, 0.25, 0.25), "NB8B_Bulb", bev=0.0)
    return g


def tower_footing(lvl):
    g = Geo()
    bbox(g, (0, -0.8, 0), (3.2, 2.4, 3.2), "NB8B_Concrete", bev=0.12)
    if lvl == 0:
        for x in (-0.6, 0.6):
            for z in (-0.6, 0.6):
                tube(g, (x, 0.35, z), (x, 0.6, z), 0.07, 6, "NB8B_Steel", caps=True)
    return g


ASSETS = [
    ("Nagisa_NB8B_PierDeck", pier_deck),
    ("Nagisa_NB8B_PierRail", pier_rail),
    ("Nagisa_NB8B_PierArch", pier_arch),
    ("Nagisa_NB8B_WheelFrame", wheel_frame),
    ("Nagisa_NB8B_WheelRim", wheel_rim),
    ("Nagisa_NB8B_WheelCabin", wheel_cabin),
    ("Nagisa_NB8B_CoasterCar", coaster_car),
    ("Nagisa_NB8B_GondolaCabin", gondola_cabin),
    ("Nagisa_NB8B_GondolaStationLow", lambda l: gondola_station(l, "PALM RIDGE GONDOLA")),
    ("Nagisa_NB8B_GondolaStationTop", lambda l: gondola_station(l, "PALM RIDGE  SUMMIT")),
    ("Nagisa_NB8B_Bullwheel", bullwheel),
    ("Nagisa_NB8B_TowerMast", tower_mast),
    ("Nagisa_NB8B_TowerHead", tower_head),
    ("Nagisa_NB8B_TowerFooting", tower_footing),
]


def main():
    only = os.environ.get("NB8B_ONLY", "")
    for stem, fn in ASSETS:
        if only and only not in stem:
            continue
        C.build_lods(stem, fn, stem + ".glb")
    print("[nb8b] done")


if __name__ == "__main__":
    main()

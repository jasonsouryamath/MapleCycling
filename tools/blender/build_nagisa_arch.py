"""
NAGISA BAY (B5) PASS 2 - modular architecture kit (library, imported by build_nagisa_*.py).

Replaces the pass-1 "extruded box + window texture" approach. Everything here builds REAL
depth in Unity coordinates (x right, y up, z forward, metres) on build_nagisa_common.Geo
(which applies sakura_lib.u2b once at export):

  bbox()        chamfered (bevelled) box - the kit's basic solid, 44 tris, reads as a lit edge
  facade()      a wall strip along a footprint edge: spandrels + piers on the outer plane,
                recessed openings with reveal faces, frame ring + mullions/transoms from the
                trim sheet, projecting sills, glass panes UV'd into the NB_Interior room atlas
  balcony()     slab + glass / metal-bar / solid balustrade with rail and posts, privacy fins,
                potted plants, chairs + table, AC condensers (detail-gated)
  roof_*()      parapet + coping, membrane / terrace floor, water tanks on stands, PV arrays,
                AC plant, lift overrun, pergolas, planters, hip / gable roofs
  shopfront()   ground-floor retail: stall-riser, display glazing into NB_ShopInterior, door,
                fascia sign from NB_Signs2, striped awning from NB_Awning (band per colourway)

Every face is emitted through qn(), which orients it against an explicit outward normal, so no
builder can ship a flipped face (skill invariant 2: fix winding at the builder, never globally).

Front of every building = local -z (the street face, NagisaBayEnvironment.FaceRoadYaw). Origin =
ground centre of the footprint. Material slots are names only; NagisaBayEnvironment.*.cs remaps
them (CelLit + _NormalStrength for solids, HDRP/Lit for glass / pool water only).

Detail levels: 0 = LOD0 (full), 1 = LOD1 (no furniture / mullions / small plant), 2 = LOD2
(outer planes + flush glass). PROVISIONAL art numbers throughout.
"""
import math
import random

import build_nagisa_common as C
from build_nagisa_common import Geo, add, sub, mul, cross, norm, dot

# ------------------------------------------------------------------ tiling (metres per UV)
C.TILE.update({
    "NB_Wall": (3.0, 3.0), "NB_Accent": (3.0, 3.0), "NB_WallBoard": (1.6, 1.6),
    "NB_Paving": (2.4, 2.4), "NB_PlazaStone": (2.4, 2.4), "NB_Kerb": (1.0, 0.3),
    "NB_Lawn": (4.0, 4.0), "NB_Groundcover": (2.0, 2.0), "NB_Gravel": (2.0, 2.0),
    "NB_Hedge": (1.5, 1.5), "NB_MetalRoof": (2.0, 2.0), "NB_Terrazzo": (2.4, 2.4),
    "NB_Boardwalk": (1.2, 1.2), "NB_Asphalt2": (4.0, 4.0), "NB_Solar": (1.0, 1.0),
    "NB_CarPaint": (1.0, 1.0), "NB_CarGlass": (1.0, 1.0), "NB_Rubber": (1.0, 1.0),
    "NB_Chrome": (1.0, 1.0), "NB_Thatch": (1.5, 1.5), "NB_Water": (4.0, 4.0),
    "NB_Signs3": (1.0, 1.0),
})

# NB_Trim sheet bands (128 px each, top of the PNG = band 0 = v in [7/8, 1])
TRIM_WHITE, TRIM_BRONZE, TRIM_BLACK, TRIM_COPING, TRIM_TEAK, TRIM_SILL, TRIM_TILE, TRIM_TIMBER = range(8)
# NB_Awning bands (same layout): 0 teal, 1 red, 2 navy, 3 yellow, 4 green, 5 pink, 6 sand, 7 charcoal
# NB_Signs2 cells: 2 columns x 8 rows, index k -> col k % 2, row k // 2 (see SIGNS in textures2)
SIGN_KAIYO_MART, SIGN_SANGO_GELATO, SIGN_TIDA_SURF, SIGN_UMI_NO_IE, SIGN_YASHI_COFFEE, \
    SIGN_HIBISCUS_BAKERY, SIGN_BLUE_REEF, SIGN_NAGISA_BOOKS, SIGN_HOTEL_SHIRAHAMA, SIGN_VILLA_HINATA, \
    SIGN_CORAL_PHARMACY, SIGN_ISLAND_POKE, SIGN_MARINA_CLUB, SIGN_SHAVE_ICE, SIGN_PALM_COURT, \
    SIGN_HANABI_RAMEN = range(16)


def band_v(band, bands=8, inset=0.08):
    v1 = 1.0 - band / bands
    v0 = v1 - 1.0 / bands
    pad = (v1 - v0) * inset
    return v0 + pad, v1 - pad


# Signs atlas 2 (NB_Signs3, indices 16..31): resort names + extra town shops (see textures2.SIGNS3)
(SIGN_GRAND_SHIOKAZE, SIGN_CORAL_TERRACE, SIGN_TWIN_PALMS, SIGN_HINATA_BUNGALOWS, SIGN_SHIOSAI_CRESCENT,
 SIGN_BLUE_LAGOON, SIGN_SUNSET_VILLAS, SIGN_BEACH_CLUB, SIGN_IZAKAYA_NAMI, SIGN_RENTAL_BIKES,
 SIGN_SURF_SHOP_KAI, SIGN_RAMEN_TAKI, SIGN_POKE_HOUSE, SIGN_UKULELE, SIGN_SHELL_GALLERY,
 SIGN_DIVE_HOUSE) = range(16, 32)


def sign_mat(k):
    return "NB_Signs2" if k < 16 else "NB_Signs3"


def sign_uv(k):
    k = k % 16
    col, row = k % 2, k // 2
    u0, u1 = col * 0.5, col * 0.5 + 0.5
    v1 = 1.0 - row / 8.0
    v0 = v1 - 1.0 / 8.0
    return (u0 + 0.004, v0 + 0.002, u1 - 0.004, v1 - 0.002)


def interior_uv(cell, n=4):
    i, j = cell % n, (cell // n) % n
    u0, u1 = i / n, (i + 1) / n
    v1 = 1.0 - j / n
    v0 = v1 - 1.0 / n
    e = 0.004
    return (u0 + e, v0 + e, u1 - e, v1 - e)


def shop_uv(cell):
    i, j = cell % 4, (cell // 4) % 2
    u0, u1 = i / 4, (i + 1) / 4
    v1 = 1.0 - j / 2
    v0 = v1 - 0.5
    return (u0 + 0.004, v0 + 0.004, u1 - 0.004, v1 - 0.004)


# ------------------------------------------------------------------ oriented faces
def qn(g, pts, n, mat, uvs=None):
    """Emit a planar face whose front side faces the outward normal n (Unity coords)."""
    if len(pts) < 3:
        return
    fn = cross(sub(pts[1], pts[0]), sub(pts[2], pts[0]))
    if dot(fn, fn) < 1e-14 and len(pts) > 3:
        fn = cross(sub(pts[2], pts[0]), sub(pts[3], pts[0]))
    if dot(fn, n) < 0:
        pts = list(reversed(pts))
        if uvs is not None:
            uvs = list(reversed(uvs))
    g.face(list(pts), mat, uvs)


def qc(g, pts, center, mat, uvs=None):
    """Emit a face oriented away from an interior point (convex solids)."""
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    cz = sum(p[2] for p in pts) / len(pts)
    qn(g, pts, sub((cx, cy, cz), center), mat, uvs)


def rect_uv(u0, v0, u1, v1):
    return [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]


def trim_uv(pts, band, along_len=None):
    """UVs for a face on the trim sheet: u along the longest edge (1 m per U), v across the band."""
    v0, v1 = band_v(band)
    e1 = sub(pts[1], pts[0])
    e2 = sub(pts[-1], pts[0])
    l1 = math.sqrt(dot(e1, e1)); l2 = math.sqrt(dot(e2, e2))
    if l1 >= l2:
        return [(0, v0), (l1, v0), (l1, v1), (0, v1)][:len(pts)] if len(pts) == 4 else None
    return [(0, v0), (0, v1), (l2, v1), (l2, v0)][:len(pts)] if len(pts) == 4 else None


# ------------------------------------------------------------------ frames
class Frame:
    """Local 2D facade frame: origin o (x, y, z), unit u along the wall (horizontal), up = +y,
    outward normal n (horizontal). P(s, v, d) = o + u*s + y*v + n*d."""

    def __init__(self, a, b, y0=0.0):
        self.o = (a[0], y0, a[1])
        dx, dz = b[0] - a[0], b[1] - a[1]
        L = math.hypot(dx, dz) or 1.0
        self.L = L
        self.u = (dx / L, 0.0, dz / L)
        self.n = (dz / L, 0.0, -dx / L)        # outward = right of travel for the CCW footprint

    def P(self, s, v, d=0.0):
        return (self.o[0] + self.u[0] * s + self.n[0] * d, self.o[1] + v,
                self.o[2] + self.u[2] * s + self.n[2] * d)


def footprint_rect(hx, hz):
    """CCW-in-this-kit rectangle (front edge first, along +x at z = -hz). Outward = right of travel."""
    return [(-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)]


def poly_edges(poly):
    return [(poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly))]


# ------------------------------------------------------------------ solids
def bbox(g, c, size, mat, bev=0.04, yaw=0.0, bottom=False, uvs_band=None, detail=0):
    """Chamfered box centred at c. uvs_band: trim-sheet band for NB_Trim faces."""
    hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
    b = min(bev, hx * 0.45, hy * 0.45, hz * 0.45)
    if detail >= 2 or b < 0.004:
        _plain_box(g, c, size, mat, yaw, bottom, uvs_band)
        return

    def T(p):
        return add(c, C.rot_y(p, yaw))

    center = c
    corner = {}
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                corner[(sx, sy, sz)] = (
                    T((sx * hx, sy * (hy - b), sz * (hz - b))),     # on x face
                    T((sx * (hx - b), sy * hy, sz * (hz - b))),     # on y face
                    T((sx * (hx - b), sy * (hy - b), sz * hz)),     # on z face
                )

    def F(pts):
        uv = None
        if uvs_band is not None:
            uv = trim_uv(pts, uvs_band)
        qc(g, pts, center, mat, uv)

    # main faces
    for ax in range(3):
        for s in (-1, 1):
            if ax == 1 and s == -1 and not bottom:
                continue
            pts = []
            for k in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                key = [0, 0, 0]
                key[ax] = s
                o = [i for i in range(3) if i != ax]
                key[o[0]], key[o[1]] = k
                pts.append(corner[tuple(key)][ax])
            F(pts)
    # edge strips (between two faces, along the third axis)
    for a1, a2 in ((0, 1), (1, 2), (0, 2)):
        a3 = 3 - a1 - a2
        for s1 in (-1, 1):
            for s2 in (-1, 1):
                if not bottom and ((a1 == 1 and s1 == -1) or (a2 == 1 and s2 == -1)):
                    # keep the lower chamfer strips (they read as the plinth edge) only if bottom
                    pass
                k0 = [0, 0, 0]; k1 = [0, 0, 0]
                k0[a1] = k1[a1] = s1
                k0[a2] = k1[a2] = s2
                k0[a3], k1[a3] = -1, 1
                p = [corner[tuple(k0)][a1], corner[tuple(k0)][a2], corner[tuple(k1)][a2], corner[tuple(k1)][a1]]
                F(p)
    # corner triangles
    for key, (px, py, pz) in corner.items():
        if not bottom and key[1] == -1:
            continue
        qc(g, [px, py, pz], center, mat)


def _plain_box(g, c, size, mat, yaw=0.0, bottom=False, band=None):
    hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
    loc = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz),
           (-hx, hy, -hz), (hx, hy, -hz), (hx, hy, hz), (-hx, hy, hz)]
    P = [add(c, C.rot_y(p, yaw)) for p in loc]
    faces = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7)]
    if bottom:
        faces.append((0, 1, 2, 3))
    for f in faces:
        pts = [P[i] for i in f]
        qc(g, pts, c, mat, trim_uv(pts, band) if band is not None else None)


def box(g, c, size, mat, yaw=0.0, bottom=False, band=None):
    _plain_box(g, c, size, mat, yaw, bottom, band)


def tbox(g, c, size, band, bev=0.015, yaw=0.0, detail=0, bottom=False):
    """Trim-sheet box (frames, rails, copings)."""
    if detail >= 1 or min(size) < 0.05:
        _plain_box(g, c, size, "NB_Trim", yaw, bottom, band)
    else:
        bbox(g, c, size, "NB_Trim", bev, yaw, bottom, uvs_band=band)


def seg_box(g, a, b, w, h, mat, y=None, band=None, bev=0.0, detail=0):
    """Box whose long axis runs from point a to point b (any 3D direction). w = width (horizontal
    across), h = height (perpendicular, as close to world-up as the direction allows)."""
    d = sub(b, a)
    L = math.sqrt(dot(d, d))
    if L < 1e-4:
        return
    X = mul(d, 1.0 / L)
    up = (0.0, 1.0, 0.0) if abs(X[1]) < 0.95 else (1.0, 0.0, 0.0)
    Z = norm(cross(X, up))
    Y = norm(cross(Z, X))
    c = mul(add(a, b), 0.5)
    hx, hy, hz = L / 2.0, h / 2.0, w / 2.0
    if abs(X[1]) >= 0.95:
        hy, hz = w / 2.0, w / 2.0

    def P(sx, sy, sz):
        return add(c, add(mul(X, sx * hx), add(mul(Y, sy * hy), mul(Z, sz * hz))))
    m = "NB_Trim" if band is not None else mat
    faces = [((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)),
             ((-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)),
             ((-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)),
             ((-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)),
             ((-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)),
             ((1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1))]
    for f in faces:
        pts = [P(*k) for k in f]
        qc(g, pts, c, m, trim_uv(pts, band) if band is not None else None)


def cyl(g, c, r, h, seg, mat, top=True, bottom=False, r_top=None):
    g.cylinder(c, r, h, max(3, seg), mat, top=top, bottom=bottom, r_top=r_top)


def prism_poly(g, poly, y0, y1, mat, top=True, top_mat=None, bottom=False):
    """Extruded polygon, faces oriented outward from the polygon centroid (convex or star-shaped)."""
    cx = sum(p[0] for p in poly) / len(poly)
    cz = sum(p[1] for p in poly) / len(poly)
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        pts = [(a[0], y0, a[1]), (b[0], y0, b[1]), (b[0], y1, b[1]), (a[0], y1, a[1])]
        ex, ez = b[0] - a[0], b[1] - a[1]
        nrm = (ez, 0.0, -ex)
        mid = ((a[0] + b[0]) / 2 - cx, 0, (a[1] + b[1]) / 2 - cz)
        if dot(nrm, mid) < 0:
            nrm = mul(nrm, -1)
        qn(g, pts, nrm, mat)
    if top:
        qn(g, [(p[0], y1, p[1]) for p in poly], (0, 1, 0), top_mat or mat)
    if bottom:
        qn(g, [(p[0], y0, p[1]) for p in poly], (0, -1, 0), mat)


def slab_poly(g, poly, y0, y1, mat, top_mat=None, edge_mat=None, bev=0.0):
    prism_poly(g, poly, y0, y1, edge_mat or mat, top=True, top_mat=top_mat or mat, bottom=True)


# ------------------------------------------------------------------ plants
def plant_cards(g, c, h, r, mat="NB_Hibiscus", n=3, yaw=0.0):
    """Crossed double-sided foliage cards (alpha atlas slots on the Foliage shader)."""
    for k in range(n):
        a = yaw + k * math.pi / n
        dx, dz = math.cos(a) * r, math.sin(a) * r
        p0 = (c[0] - dx, c[1], c[2] - dz); p1 = (c[0] + dx, c[1], c[2] + dz)
        p2 = (c[0] + dx, c[1] + h, c[2] + dz); p3 = (c[0] - dx, c[1] + h, c[2] - dz)
        uv = [(0, 0), (1, 0), (1, 1), (0, 1)]
        g.quad(p0, p1, p2, p3, mat, uvs=uv, double=True)


def potted_plant(g, c, detail, big=False, mat="NB_Hibiscus"):
    r = 0.34 if big else 0.24
    hp = 0.55 if big else 0.4
    cyl(g, c, r, hp, (10, 6, 4)[detail], "NB_Planter", top=False, r_top=r * 1.15)
    qn(g, [(c[0] + math.cos(a) * r * 1.05, c[1] + hp - 0.05, c[2] + math.sin(a) * r * 1.05)
           for a in [i / 6 * math.tau for i in range(6)]], (0, 1, 0), "NB_Soil")
    if detail < 2:
        plant_cards(g, (c[0], c[1] + hp - 0.1, c[2]), 1.3 if big else 0.8, 0.55 if big else 0.4, mat,
                    n=3 if detail == 0 else 2, yaw=c[0] * 3.1)


# ------------------------------------------------------------------ furniture
def chair(g, c, yaw, mat="NB_Teak"):
    bbox(g, add(c, (0, 0.44, 0)), (0.46, 0.05, 0.46), mat, 0.012, yaw)
    back = add(c, C.rot_y((0, 0.72, 0.21), yaw))
    _plain_box(g, back, (0.46, 0.5, 0.04), mat, yaw)
    for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        p = add(c, C.rot_y((sx * 0.2, 0.21, sz * 0.2), yaw))
        _plain_box(g, p, (0.035, 0.42, 0.035), "NB_Trim", yaw, False, TRIM_BLACK)


def table(g, c, r=0.38, h=0.74, seg=10, top_mat="NB_Terrazzo"):
    cyl(g, add(c, (0, h - 0.03, 0)), r, 0.03, seg, top_mat, top=True, bottom=True)
    cyl(g, c, 0.035, h - 0.03, 4, "NB_Trim", top=False)
    cyl(g, c, 0.22, 0.02, 6, "NB_Trim", top=True)


def ac_unit(g, c, yaw, detail):
    bbox(g, add(c, (0, 0.3, 0)), (0.8, 0.6, 0.3), "NB_Trim", 0.02, yaw, uvs_band=TRIM_WHITE, detail=detail)
    if detail == 0:
        fc = add(c, C.rot_y((-0.1, 0.3, -0.155), yaw))
        n = C.rot_y((0, 0, -1), yaw)
        pts = []
        for i in range(8):
            a = i / 8 * math.tau
            pts.append(add(fc, add(mul(C.rot_y((1, 0, 0), yaw), math.cos(a) * 0.22), (0, math.sin(a) * 0.22, 0))))
        qn(g, pts, n, "NB_Trim", [(0.5 + 0.4 * math.cos(i / 8 * math.tau), band_v(TRIM_BLACK)[0] + 0.02) for i in range(8)])


# ------------------------------------------------------------------ facade
class FacadeSpec:
    """Facade design parameters. All dims metres. PROVISIONAL."""

    def __init__(self, bay=3.0, win_w=0.62, win_h=0.62, sill=0.9, reveal=0.22, frame_band=TRIM_WHITE,
                 mullions=1, transom=False, sill_proj=True, wall="NB_Wall", spandrel=None,
                 pier=None, glass="NB_Interior", fh=3.2, full_height=False, shutters=None,
                 louvres=False, seed=1, cell_pool=16, spandrel_proj=0.0, pier_proj=0.0):
        self.bay, self.win_w, self.win_h, self.sill = bay, win_w, win_h, sill
        self.reveal, self.frame_band, self.mullions, self.transom = reveal, frame_band, mullions, transom
        self.sill_proj, self.wall, self.spandrel, self.pier = sill_proj, wall, spandrel or wall, pier or wall
        self.glass, self.fh, self.full_height, self.shutters, self.louvres = glass, fh, full_height, shutters, louvres
        self.rng = random.Random(seed)
        self.cell_pool = cell_pool
        self.spandrel_proj, self.pier_proj = spandrel_proj, pier_proj


def facade(g, a, b, y0, floors, spec, detail, skip=None, bays=None, loggia=None):
    """One wall along footprint edge a->b (x, z), floors of spec.fh starting at y0.
    skip(floor, bay) -> True leaves a plain wall bay (no opening)."""
    F = Frame(a, b)
    L = F.L
    nb = bays or max(1, int(round(L / spec.bay)))
    bw = L / nb
    fh = spec.fh
    ww = bw * spec.win_w if not spec.full_height else bw * spec.win_w
    wh = fh * spec.win_h if not spec.full_height else fh - 0.35
    sill = spec.sill if not spec.full_height else 0.05
    rev = spec.reveal if detail < 2 else 0.0
    n = F.n
    for f in range(floors):
        yb = y0 + f * fh
        v0, v1 = yb + sill, yb + sill + wh
        opens = []
        for k in range(nb):
            if skip is not None and skip(f, k):
                opens.append(None)
                continue
            s0 = k * bw + (bw - ww) / 2
            if loggia is not None and loggia(f, k):
                opens.append((k * bw + 0.12, (k + 1) * bw - 0.12, True))      # recessed balcony bay
            else:
                opens.append((s0, s0 + ww))
        # outer plane: spandrel under, head band over, piers between
        qn(g, [F.P(0, yb), F.P(L, yb), F.P(L, v0), F.P(0, v0)], n, spec.spandrel)
        qn(g, [F.P(0, v1), F.P(L, v1), F.P(L, yb + fh), F.P(0, yb + fh)], n, spec.wall)
        s = 0.0
        for o in opens + [None]:
            if o is None:
                continue
            if o[0] - s > 1e-3:
                qn(g, [F.P(s, v0), F.P(o[0], v0), F.P(o[0], v1), F.P(s, v1)], n, spec.pier)
            s = o[1]
        # right-most pier (and any skipped bays already covered by a wide pier)
        if L - s > 1e-3:
            qn(g, [F.P(s, v0), F.P(L, v0), F.P(L, v1), F.P(s, v1)], n, spec.pier)
        for k, o in enumerate(opens):
            if o is None:
                continue
            s0, s1 = o[0], o[1]
            if len(o) > 2:
                _loggia_bay(g, F, s0, s1, v0, v1, yb, fh, spec, detail, spec.rng)
                continue
            cell = spec.rng.randrange(spec.cell_pool)
            uv = interior_uv(cell)
            if rev > 0:
                # reveals: jambs (face along +-u), head soffit (faces down), sill (faces up)
                qn(g, [F.P(s0, v0, 0), F.P(s0, v0, -rev), F.P(s0, v1, -rev), F.P(s0, v1, 0)], F.u, spec.pier)
                qn(g, [F.P(s1, v0, 0), F.P(s1, v1, 0), F.P(s1, v1, -rev), F.P(s1, v0, -rev)], mul(F.u, -1), spec.pier)
                qn(g, [F.P(s0, v1, 0), F.P(s1, v1, 0), F.P(s1, v1, -rev), F.P(s0, v1, -rev)], (0, -1, 0), spec.wall)
                qn(g, [F.P(s0, v0, 0), F.P(s0, v0, -rev), F.P(s1, v0, -rev), F.P(s1, v0, 0)], (0, 1, 0), spec.spandrel)
            # glass
            qn(g, [F.P(s0, v0, -rev), F.P(s1, v0, -rev), F.P(s1, v1, -rev), F.P(s0, v1, -rev)], n, spec.glass,
               [(uv[0], uv[1]), (uv[2], uv[1]), (uv[2], uv[3]), (uv[0], uv[3])])
            if detail == 0:
                fw = 0.07
                dd = -rev + 0.035
                # frame ring (4 thin trim boxes just proud of the glass)
                for (sa, va, sb, vb) in ((s0, v0, s1, v0 + fw), (s0, v1 - fw, s1, v1),
                                         (s0, v0, s0 + fw, v1), (s1 - fw, v0, s1, v1)):
                    cc = F.P((sa + sb) / 2, (va + vb) / 2, dd)
                    _frame_box(g, F, cc, sb - sa, vb - va, 0.07, spec.frame_band)
            if detail == 0:
                for m in range(spec.mullions):
                    sm = s0 + (s1 - s0) * (m + 1) / (spec.mullions + 1)
                    _frame_box(g, F, F.P(sm, (v0 + v1) / 2, -rev + 0.04), 0.05, v1 - v0, 0.06, spec.frame_band)
                if spec.transom:
                    vt = v1 - (v1 - v0) * 0.24
                    _frame_box(g, F, F.P((s0 + s1) / 2, vt, -rev + 0.04), s1 - s0, 0.05, 0.06, spec.frame_band)
                if spec.sill_proj and not spec.full_height:
                    _frame_box(g, F, F.P((s0 + s1) / 2, v0 - 0.04, 0.03), s1 - s0 + 0.16, 0.07, rev + 0.12,
                               TRIM_SILL, bev=0.012)
            if spec.shutters and detail < 2:
                # pair of louvred shutters folded open against the wall beside the opening
                sw = (s1 - s0) * 0.5
                for side, sx in ((-1, s0 - sw / 2 - 0.02), (1, s1 + sw / 2 + 0.02)):
                    cc = F.P(sx, (v0 + v1) / 2, 0.03)
                    _frame_box(g, F, cc, sw, v1 - v0, 0.04, spec.shutters, bev=0.0 if detail else 0.01)
                    if detail == 0:
                        for r in range(int((v1 - v0) / 0.22)):
                            vv = v0 + 0.14 + r * 0.22
                            _frame_box(g, F, F.P(sx, vv, 0.06), sw - 0.06, 0.03, 0.03, spec.shutters)
            if spec.louvres and detail < 2:
                # horizontal sun-shade: 3 thin aluminium blades on two brackets over the opening
                for r in range(3 if detail == 0 else 1):
                    _frame_box(g, F, F.P((s0 + s1) / 2, v1 + 0.10 + r * 0.02, 0.18 + r * 0.22), s1 - s0 + 0.5, 0.025,
                               0.18, TRIM_WHITE)
                if detail == 0:
                    for sx in (s0 - 0.15, s1 + 0.15):
                        _frame_box(g, F, F.P(sx, v1 + 0.12, 0.35), 0.03, 0.08, 0.7, TRIM_WHITE)
        if spec.spandrel_proj > 0 and detail < 2:
            # projecting floor band (slab edge) - chamfered, reads as a lit ledge
            cc = F.P(L / 2, yb + 0.12, spec.spandrel_proj / 2)
            _frame_box(g, F, cc, L + 2 * spec.spandrel_proj, 0.24, spec.spandrel_proj, TRIM_COPING, bev=0.02,
                       mat="NB_Accent")
        if spec.pier_proj > 0 and detail < 2:
            for k in range(nb + 1):
                sp = k * bw
                cc = F.P(sp, yb + fh / 2, spec.pier_proj / 2)
                _frame_box(g, F, cc, 0.3, fh, spec.pier_proj, TRIM_COPING, bev=0.02, mat="NB_Accent")
    return F


def _loggia_bay(g, F, s0, s1, v0, v1, yb, fh, spec, detail, rng, depth=1.9):
    """Recessed balcony: the wall opens full-height, side walls + soffit + terrazzo floor run back `depth`,
    a sliding glass door (interior card behind) closes the back, a glass / bar balustrade sits on the facade
    plane, with a chair, plant and an AC condenser on the floor at LOD0."""
    n = F.n
    D = depth if detail < 2 else 0.6
    w = s1 - s0
    # side walls (jambs), soffit, floor
    qn(g, [F.P(s0, v0, 0), F.P(s0, v0, -D), F.P(s0, v1, -D), F.P(s0, v1, 0)], F.u, spec.pier)
    qn(g, [F.P(s1, v0, 0), F.P(s1, v1, 0), F.P(s1, v1, -D), F.P(s1, v0, -D)], mul(F.u, -1), spec.pier)
    qn(g, [F.P(s0, v1, 0), F.P(s1, v1, 0), F.P(s1, v1, -D), F.P(s0, v1, -D)], (0, -1, 0), "NB_Plaster")
    qn(g, [F.P(s0, v0 + 0.02, 0), F.P(s0, v0 + 0.02, -D), F.P(s1, v0 + 0.02, -D), F.P(s1, v0 + 0.02, 0)], (0, 1, 0), "NB_Terrazzo")
    # back wall: sliding door glass over most of the width, wall strip above
    doorh = (v1 - v0) * 0.86
    cell = spec.rng.randrange(spec.cell_pool)
    uv = interior_uv(cell)
    dw0, dw1 = s0 + 0.3, s1 - 0.3
    qn(g, [F.P(s0, v0, -D), F.P(s1, v0, -D), F.P(s1, v1, -D), F.P(s0, v1, -D)], n, spec.wall)
    qn(g, [F.P(dw0, v0 + 0.04, -D + 0.02), F.P(dw1, v0 + 0.04, -D + 0.02), F.P(dw1, v0 + doorh, -D + 0.02),
           F.P(dw0, v0 + doorh, -D + 0.02)], n, spec.glass, [(uv[0], uv[1]), (uv[2], uv[1]), (uv[2], uv[3]), (uv[0], uv[3])])
    if detail < 2:
        for sx in (dw0, (dw0 + dw1) / 2, dw1):
            _frame_box(g, F, F.P(sx, v0 + doorh / 2, -D + 0.05), 0.07, doorh, 0.07, TRIM_BLACK)
        for vz in (v0 + 0.04, v0 + doorh):
            _frame_box(g, F, F.P((dw0 + dw1) / 2, vz, -D + 0.05), dw1 - dw0, 0.07, 0.07, TRIM_BLACK)
    # balustrade on the facade plane
    rh = 1.05
    if detail < 2:
        g.quad(F.P(s0 + 0.05, v0 + 0.1, -0.08), F.P(s1 - 0.05, v0 + 0.1, -0.08), F.P(s1 - 0.05, v0 + rh, -0.08),
               F.P(s0 + 0.05, v0 + rh, -0.08), "NB_BalconyGlass", double=True)
        _frame_box(g, F, F.P((s0 + s1) / 2, v0 + rh, -0.08), w - 0.04, 0.05, 0.06, TRIM_WHITE)
        _frame_box(g, F, F.P((s0 + s1) / 2, v0 + 0.07, -0.08), w - 0.04, 0.08, 0.1, TRIM_WHITE)
    else:
        qn(g, [F.P(s0, v0, -0.05), F.P(s1, v0, -0.05), F.P(s1, v0 + rh, -0.05), F.P(s0, v0 + rh, -0.05)], n, "NB_BalconyGlass")
    if detail == 0:
        mid = (s0 + s1) / 2
        r = rng.random()
        yaw = math.atan2(F.n[0], F.n[2])
        if r < 0.55:
            table(g, F.P(mid, v0 + 0.02, -D * 0.5), 0.3, 0.72, 8)
            chair(g, F.P(mid - 0.55, v0 + 0.02, -D * 0.5), yaw + math.pi / 2)
            chair(g, F.P(mid + 0.55, v0 + 0.02, -D * 0.5), yaw - math.pi / 2)
        if rng.random() < 0.6:
            potted_plant(g, F.P(s1 - 0.4, v0 + 0.02, -0.5), detail, big=rng.random() < 0.5,
                         mat="NB_Hibiscus" if rng.random() < 0.6 else "NB_Bougainvillea")
        if rng.random() < 0.4:
            ac_unit(g, F.P(s0 + 0.5, v0 + 0.02, -D + 0.35), math.atan2(-F.u[2], F.u[0]), detail)


def _frame_box(g, F, c, w_along, h, depth, band, bev=0.0, mat=None):
    """Axis-aligned-to-the-facade box centred at c: w along u, h up, depth along n."""
    yaw = math.atan2(-F.u[2], F.u[0])
    size = (w_along, h, depth)
    if mat is not None:
        if bev > 0:
            bbox(g, c, size, mat, bev, yaw)
        else:
            _plain_box(g, c, size, mat, yaw)
        return
    if bev > 0:
        bbox(g, c, size, "NB_Trim", bev, yaw, uvs_band=band)
    else:
        _plain_box(g, c, size, "NB_Trim", yaw, False, band)


# ------------------------------------------------------------------ balconies
def balcony(g, F, s0, s1, y, depth, kind, detail, rng, slab_mat="NB_Plaster", rail_band=TRIM_WHITE,
            furnish=True, plants=True, ac=False, divider=False, side_walls=False, solid_mat="NB_Accent"):
    """Balcony on facade frame F from s0..s1 at floor level y, projecting depth outward.
    kind: 'glass' | 'bars' | 'solid' | 'rail' (bars + solid lower half)."""
    t = 0.2
    c = F.P((s0 + s1) / 2, y - t / 2 + 0.02, depth / 2)
    _frame_box(g, F, c, s1 - s0, t, depth, None, bev=0.03 if detail == 0 else 0.0, mat=slab_mat)
    top = y + 0.02
    rh = 1.1
    e = depth - 0.06          # rail line just inside the slab edge
    if kind == "glass":
        for (sa, sb, da, db) in ((s0 + 0.05, s1 - 0.05, e, e),) + (((s0 + 0.05, s0 + 0.05, 0.0, e), (s1 - 0.05, s1 - 0.05, 0.0, e)) if not side_walls else ()):
            p0 = F.P(sa, top + 0.06, da); p1 = F.P(sb, top + 0.06, db)
            p2 = F.P(sb, top + rh - 0.04, db); p3 = F.P(sa, top + rh - 0.04, da)
            g.quad(p0, p1, p2, p3, "NB_BalconyGlass", double=True)
        if detail < 2:
            _frame_box(g, F, F.P((s0 + s1) / 2, top + rh, e), s1 - s0, 0.05, 0.07, rail_band, bev=0.01 if detail == 0 else 0)
            _frame_box(g, F, F.P((s0 + s1) / 2, top + 0.04, e), s1 - s0, 0.05, 0.05, rail_band)
            if not side_walls:
                for sx in (s0 + 0.05, s1 - 0.05):
                    _frame_box(g, F, F.P(sx, top + rh, e / 2), 0.05, 0.05, e, rail_band)
            if detail == 0:
                nposts = max(2, int((s1 - s0) / 1.4) + 1)
                for k in range(nposts):
                    sx = s0 + 0.05 + (s1 - s0 - 0.1) * k / (nposts - 1)
                    _frame_box(g, F, F.P(sx, top + rh / 2, e + 0.03), 0.04, rh, 0.04, rail_band)
    elif kind in ("bars", "rail"):
        lo = top + (0.5 if kind == "rail" else 0.0)
        if kind == "rail":
            _frame_box(g, F, F.P((s0 + s1) / 2, top + 0.25, e), s1 - s0, 0.5, 0.12, None, mat=solid_mat,
                       bev=0.02 if detail == 0 else 0)
        _frame_box(g, F, F.P((s0 + s1) / 2, top + rh, e), s1 - s0, 0.06, 0.06, rail_band, bev=0.01 if detail == 0 else 0)
        if detail < 2:
            if detail == 0:
                nb = int((s1 - s0) / 0.13)
                for k in range(nb + 1):
                    sx = s0 + 0.05 + (s1 - s0 - 0.1) * k / max(1, nb)
                    _frame_box(g, F, F.P(sx, (lo + top + rh) / 2, e), 0.022, top + rh - lo, 0.022, rail_band)
            else:
                for r in range(3):
                    _frame_box(g, F, F.P((s0 + s1) / 2, lo + 0.15 + r * (top + rh - lo - 0.2) / 3, e),
                               s1 - s0, 0.03, 0.03, rail_band)
            if not side_walls:
                for sx in (s0 + 0.04, s1 - 0.04):
                    _frame_box(g, F, F.P(sx, top + rh, e / 2), 0.05, 0.05, e, rail_band)
                    if detail == 0:
                        for r in range(4):
                            _frame_box(g, F, F.P(sx, lo + 0.15 + r * 0.22, e / 2), 0.02, 0.02, e, rail_band)
    else:   # solid parapet
        _frame_box(g, F, F.P((s0 + s1) / 2, top + rh / 2, e), s1 - s0, rh, 0.14, None, mat=solid_mat,
                   bev=0.03 if detail == 0 else 0)
        if detail < 2:
            _frame_box(g, F, F.P((s0 + s1) / 2, top + rh + 0.03, e), s1 - s0 + 0.04, 0.06, 0.2, TRIM_COPING)
    if side_walls:
        for sx in (s0 + 0.08, s1 - 0.08):
            _frame_box(g, F, F.P(sx, top + 1.4, depth / 2), 0.16, 2.8, depth, None, mat=solid_mat,
                       bev=0.02 if detail == 0 else 0)
    if divider and detail < 2:
        _frame_box(g, F, F.P(s1, top + 0.9, depth / 2), 0.05, 1.8, depth - 0.1, None, mat="NB_BalconyGlass")
    if detail == 0 and furnish:
        mid = (s0 + s1) / 2
        dd = depth * 0.5
        kind_f = rng.random()
        if kind_f < 0.55:
            yaw = math.atan2(-F.n[2], F.n[0]) if False else math.atan2(F.n[0], F.n[2])
            table(g, F.P(mid, top, dd), 0.3, 0.72, 8)
            chair(g, F.P(mid - 0.6, top, dd), yaw + math.pi / 2)
            chair(g, F.P(mid + 0.6, top, dd), yaw - math.pi / 2)
        elif kind_f < 0.8:
            # lounge chair
            bbox(g, F.P(mid, top + 0.25, dd), (1.7, 0.1, 0.62), "NB_Canvas", 0.03, math.atan2(-F.u[2], F.u[0]))
        if plants and rng.random() < 0.7:
            potted_plant(g, F.P(s1 - 0.4, top, depth - 0.4), detail, big=rng.random() < 0.5,
                         mat="NB_Hibiscus" if rng.random() < 0.6 else "NB_Bougainvillea")
        if ac and rng.random() < 0.6:
            ac_unit(g, F.P(s0 + 0.6, top, 0.25), math.atan2(-F.u[2], F.u[0]), detail)


# ------------------------------------------------------------------ roofs
def parapet(g, poly, y, h, detail, mat="NB_Wall", t=0.2, coping=True, roof_mat="NB_Concrete"):
    """Parapet wall around a footprint polygon, both faces + coping, and the roof surface."""
    for a, b in poly_edges(poly):
        F = Frame(a, b)
        # outer face
        qn(g, [F.P(0, y), F.P(F.L, y), F.P(F.L, y + h), F.P(0, y + h)], F.n, mat)
        # inner face
        qn(g, [F.P(t, y, -t), F.P(F.L - t, y, -t), F.P(F.L - t, y + h, -t), F.P(t, y + h, -t)], mul(F.n, -1), mat)
        if coping and detail < 2:
            _frame_box(g, F, F.P(F.L / 2, y + h + 0.04, -t / 2 + 0.03), F.L + 0.08, 0.08, t + 0.1, TRIM_COPING,
                       bev=0.015 if detail == 0 else 0)
        else:
            qn(g, [F.P(0, y + h, 0), F.P(F.L, y + h, 0), F.P(F.L - t, y + h, -t), F.P(t, y + h, -t)], (0, 1, 0), mat)
    inset = shrink(poly, t)
    qn(g, [(p[0], y + 0.02, p[1]) for p in inset], (0, 1, 0), roof_mat)


def shrink(poly, d):
    """Inset a convex CCW(kit) polygon by d (miter)."""
    n = len(poly)
    out = []
    for i in range(n):
        p0, p1, p2 = poly[i - 1], poly[i], poly[(i + 1) % n]
        e0 = norm((p1[0] - p0[0], 0, p1[1] - p0[1]))
        e1 = norm((p2[0] - p1[0], 0, p2[1] - p1[1]))
        n0 = (e0[2], 0, -e0[0]); n1 = (e1[2], 0, -e1[0])     # outward
        m = norm(add(n0, n1))
        cosh = max(0.2, dot(m, n0))
        out.append((p1[0] - m[0] * d / cosh, p1[1] - m[2] * d / cosh))
    return out


def water_tank(g, c, detail, r=0.8, h=1.6):
    seg = (14, 8, 6)[detail]
    if detail < 2:
        for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            seg_box(g, add(c, (sx * r * 0.6, 0, sz * r * 0.6)), add(c, (sx * r * 0.6, 0.6, sz * r * 0.6)), 0.08, 0.08,
                    "NB_Trim", band=TRIM_BLACK)
        _plain_box(g, add(c, (0, 0.62, 0)), (r * 1.5, 0.06, r * 1.5), "NB_Trim", 0, False, TRIM_BLACK)
    cyl(g, add(c, (0, 0.65, 0)), r, h, seg, "NB_Trim" if False else "NB_Plaster", top=False)
    cyl(g, add(c, (0, 0.65 + h, 0)), r, 0.25, seg, "NB_Plaster", top=True, r_top=r * 0.3)


def solar_array(g, c, cols, rows, yaw, detail, tilt=0.26):
    """PV panels 1.0 x 1.7 m on a tilted aluminium frame, facing -z (south-ish) locally."""
    pw, pl = 1.05, 1.72
    for i in range(cols):
        for j in range(rows):
            lx = (i - (cols - 1) / 2) * (pw + 0.04)
            lz = (j - (rows - 1) / 2) * (pl * math.cos(tilt) + 0.9)
            ctr = add(c, C.rot_y((lx, 0.55, lz), yaw))
            hz = pl / 2
            corners = [(-pw / 2, -hz), (pw / 2, -hz), (pw / 2, hz), (-pw / 2, hz)]
            pts = []
            for (x, z) in corners:
                y = -z * math.sin(tilt) * -1.0
                pts.append(add(ctr, C.rot_y((x, z * math.sin(tilt), z * math.cos(tilt)), yaw)))
            nrm = C.rot_y((0, math.cos(tilt), -math.sin(tilt)), yaw)
            qn(g, pts, nrm, "NB_Solar", [(0, 0), (1, 0), (1, 1), (0, 1)])
            if detail == 0:
                qn(g, list(reversed(pts)), mul(nrm, -1), "NB_Trim", [(0, band_v(TRIM_WHITE)[0])] * 4)
                for (x, z) in ((-pw / 2 + 0.05, -hz + 0.1), (pw / 2 - 0.05, -hz + 0.1), (-pw / 2 + 0.05, hz - 0.1), (pw / 2 - 0.05, hz - 0.1)):
                    top = add(ctr, C.rot_y((x, z * math.sin(tilt), z * math.cos(tilt)), yaw))
                    seg_box(g, (top[0], c[1], top[2]), top, 0.04, 0.04, "NB_Trim", band=TRIM_WHITE)


def ac_plant(g, c, yaw, n, detail):
    for k in range(n):
        p = add(c, C.rot_y((k * 1.1 - (n - 1) * 0.55, 0, 0), yaw))
        bbox(g, add(p, (0, 0.55, 0)), (1.0, 1.1, 1.0), "NB_Trim", 0.03, yaw, uvs_band=TRIM_WHITE, detail=detail)
        if detail == 0:
            pts = [add(add(p, (0, 1.105, 0)), C.rot_y((math.cos(a) * 0.36, 0, math.sin(a) * 0.36), yaw))
                   for a in [i / 10 * math.tau for i in range(10)]]
            qn(g, pts, (0, 1, 0), "NB_Trim", [(0.5, band_v(TRIM_BLACK)[0] + 0.03)] * 10)


def overrun(g, c, size, detail, mat="NB_Wall"):
    bbox(g, add(c, (0, size[1] / 2, 0)), size, mat, 0.05, detail=detail)
    if detail < 2:
        _plain_box(g, add(c, (0, size[1] + 0.05, 0)), (size[0] + 0.2, 0.1, size[2] + 0.2), "NB_Trim", 0, False, TRIM_COPING)
        # door on the -z face
        qn(g, [add(c, (-0.45, 0.02, -size[2] / 2 - 0.01)), add(c, (0.45, 0.02, -size[2] / 2 - 0.01)),
               add(c, (0.45, 2.1, -size[2] / 2 - 0.01)), add(c, (-0.45, 2.1, -size[2] / 2 - 0.01))], (0, 0, -1),
           "NB_Trim", [(0, band_v(TRIM_BLACK)[0]), (0.9, band_v(TRIM_BLACK)[0]), (0.9, band_v(TRIM_BLACK)[1]), (0, band_v(TRIM_BLACK)[1])])


def pergola(g, x0, z0, x1, z1, y, h, detail, mat="NB_Teak", slats=True):
    for x in (x0, x1):
        for z in (z0, z1):
            bbox(g, (x, y + h / 2, z), (0.18, h, 0.18), mat, 0.02, detail=detail)
    for z in (z0, z1):
        bbox(g, ((x0 + x1) / 2, y + h - 0.12, z), (x1 - x0 + 0.4, 0.24, 0.14), mat, 0.02, detail=detail)
    if slats and detail < 2:
        n = int((x1 - x0) / (0.45 if detail == 0 else 0.9))
        for k in range(n + 1):
            x = x0 + (x1 - x0) * k / max(1, n)
            _plain_box(g, (x, y + h + 0.05, (z0 + z1) / 2), (0.07, 0.14, z1 - z0 + 0.5), mat)


def roof_planter(g, x0, z0, x1, z1, y, detail, mat="NB_Hibiscus"):
    bbox(g, ((x0 + x1) / 2, y + 0.3, (z0 + z1) / 2), (x1 - x0, 0.6, z1 - z0), "NB_Planter", 0.03, detail=detail)
    qn(g, [(x0 + 0.08, y + 0.58, z0 + 0.08), (x1 - 0.08, y + 0.58, z0 + 0.08), (x1 - 0.08, y + 0.58, z1 - 0.08),
           (x0 + 0.08, y + 0.58, z1 - 0.08)], (0, 1, 0), "NB_Groundcover")
    if detail < 2:
        L = max(x1 - x0, z1 - z0)
        n = max(1, int(L / 1.2))
        for k in range(n):
            f = (k + 0.5) / n
            px = x0 + (x1 - x0) * (f if x1 - x0 >= z1 - z0 else 0.5)
            pz = z0 + (z1 - z0) * (f if z1 - z0 > x1 - x0 else 0.5)
            plant_cards(g, (px, y + 0.5, pz), 0.9, 0.5, mat if k % 3 else "NB_Bougainvillea", n=2 if detail else 3, yaw=k)


def hip_roof(g, x0, z0, x1, z1, y, rise, mat="NB_RoofTile", soffit="NB_Plaster", fascia=True, detail=0):
    hx, hz = (x1 - x0) / 2, (z1 - z0) / 2
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    r = min(hx, hz)
    if hx >= hz:
        ra, rb = (x0 + r, y + rise, cz), (x1 - r, y + rise, cz)
    else:
        ra, rb = (cx, y + rise, z0 + r), (cx, y + rise, z1 - r)
    A, B, Cc, D = (x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)
    ctr = (cx, y - 1.0, cz)
    if hx >= hz:
        qc(g, [A, B, rb, ra], ctr, mat); qc(g, [B, Cc, rb], ctr, mat)
        qc(g, [Cc, D, ra, rb], ctr, mat); qc(g, [D, A, ra], ctr, mat)
    else:
        qc(g, [A, B, ra], ctr, mat); qc(g, [B, Cc, rb, ra], ctr, mat)
        qc(g, [Cc, D, rb], ctr, mat); qc(g, [D, A, ra, rb], ctr, mat)
    qn(g, [A, B, Cc, D], (0, -1, 0), soffit)
    if fascia and detail < 2:
        for p, q in ((A, B), (B, Cc), (Cc, D), (D, A)):
            seg_box(g, (p[0], y - 0.08, p[2]), (q[0], y - 0.08, q[2]), 0.05, 0.2, "NB_Trim", band=TRIM_TIMBER)
        if detail == 0:  # ridge + hip caps
            seg_box(g, add(ra, (0, 0.05, 0)), add(rb, (0, 0.05, 0)), 0.22, 0.12, mat)
            for p, q in ((A, ra), (D, ra), (B, rb), (Cc, rb)):
                seg_box(g, add(p, (0, 0.05, 0)), add(q, (0, 0.05, 0)), 0.2, 0.1, mat)


def gable_roof(g, x0, z0, x1, z1, y, rise, mat="NB_MetalRoof", gable_mat="NB_WallBoard", detail=0, over=0.5):
    """Ridge along x. Eaves overhang `over` on the z sides and 0.3 on the gable ends."""
    zc = (z0 + z1) / 2
    a0, a1 = x0 - 0.3, x1 + 0.3
    e0, e1 = z0 - over, z1 + over
    rise_e = rise + 0.05     # eaves sit at wall-top height (so fascia signs never poke through the slope)
    R0, R1 = (a0, y + rise, zc), (a1, y + rise, zc)
    ye = y + rise - rise_e
    ctr = ((x0 + x1) / 2, y - 3, zc)
    qc(g, [(a0, ye, e0), (a1, ye, e0), R1, R0], ctr, mat)
    qc(g, [(a1, ye, e1), (a0, ye, e1), R0, R1], ctr, mat)
    # undersides
    qn(g, [(a0, ye - 0.02, e0), (a1, ye - 0.02, e0), (a1, y + rise - 0.02, zc), (a0, y + rise - 0.02, zc)], (0, -1, 0.3), "NB_Teak")
    qn(g, [(a1, ye - 0.02, e1), (a0, ye - 0.02, e1), (a0, y + rise - 0.02, zc), (a1, y + rise - 0.02, zc)], (0, -1, -0.3), "NB_Teak")
    for x, s in ((x0, -1), (x1, 1)):
        qn(g, [(x, y, z0), (x, y, z1), (x, y + rise, zc)], (s, 0, 0), gable_mat)
    if detail < 2:
        seg_box(g, (a0, y + rise + 0.04, zc), (a1, y + rise + 0.04, zc), 0.2, 0.1, "NB_Trim", band=TRIM_WHITE)
        for z in (e0, e1):
            seg_box(g, (a0, ye - 0.05, z), (a1, ye - 0.05, z), 0.05, 0.15, "NB_Trim", band=TRIM_WHITE)


# ------------------------------------------------------------------ retail
def awning(g, F, s0, s1, y, depth, drop, band, detail, valance=True):
    """Sloped striped awning off facade F from s0..s1 at height y, projecting depth, falling drop."""
    v0, v1 = band_v(band, inset=0.1)
    w = s1 - s0
    uv = [(0, v1), (w / 1.2, v1), (w / 1.2, v0 + (v1 - v0) * 0.3), (0, v0 + (v1 - v0) * 0.3)]
    p0, p1 = F.P(s0, y, 0.02), F.P(s1, y, 0.02)
    p2, p3 = F.P(s1, y - drop, depth), F.P(s0, y - drop, depth)
    top_n = add(mul(F.n, drop), (0, depth, 0))
    qn(g, [p0, p1, p2, p3], top_n, "NB_Awning", uv)
    qn(g, [p0, p1, p2, p3], mul(top_n, -1), "NB_Awning", uv)
    if valance:
        vh = 0.28
        uvv = [(0, v0), (w / 1.2, v0), (w / 1.2, v0 + (v1 - v0) * 0.3), (0, v0 + (v1 - v0) * 0.3)]
        q0, q1 = F.P(s0, y - drop - vh, depth), F.P(s1, y - drop - vh, depth)
        qn(g, [q0, q1, p2, p3], F.n, "NB_Awning", uvv)
        qn(g, [q0, q1, p2, p3], mul(F.n, -1), "NB_Awning", uvv)
        if detail == 0:   # scallops: small triangles below the valance
            nsc = int(w / 0.3)
            for k in range(nsc):
                sa = s0 + w * k / nsc; sb = s0 + w * (k + 1) / nsc
                t0, t1, t2 = F.P(sa, y - drop - vh, depth), F.P(sb, y - drop - vh, depth), F.P((sa + sb) / 2, y - drop - vh - 0.1, depth)
                qn(g, [t0, t1, t2], F.n, "NB_Awning", [(0, v0), (0.25, v0), (0.12, v0)])
                qn(g, [t0, t1, t2], mul(F.n, -1), "NB_Awning", [(0, v0), (0.25, v0), (0.12, v0)])
    if detail < 2:  # side cheeks + arms
        for s in (s0, s1):
            qn(g, [F.P(s, y, 0.02), F.P(s, y - drop, depth), F.P(s, y - drop - (0.28 if valance else 0), depth)], F.u, "NB_Awning",
               [(0, v1), (0.4, v0), (0.4, v0)])
            qn(g, [F.P(s, y, 0.02), F.P(s, y - drop, depth), F.P(s, y - drop - (0.28 if valance else 0), depth)], mul(F.u, -1), "NB_Awning",
               [(0, v1), (0.4, v0), (0.4, v0)])
            if detail == 0:
                seg_box(g, F.P(s + (0.05 if s == s0 else -0.05), y - 0.6, 0.02),
                        F.P(s + (0.05 if s == s0 else -0.05), y - drop - 0.02, depth - 0.05), 0.03, 0.03, "NB_Trim", band=TRIM_BLACK)


def sign_board(g, F, s0, s1, v0, v1, d, k, detail, frame_band=TRIM_BRONZE):
    u0, vv0, u1, vv1 = sign_uv(k)
    qn(g, [F.P(s0, v0, d), F.P(s1, v0, d), F.P(s1, v1, d), F.P(s0, v1, d)], F.n, sign_mat(k),
       [(u0, vv0), (u1, vv0), (u1, vv1), (u0, vv1)])
    if detail < 2:
        _frame_box(g, F, F.P((s0 + s1) / 2, (v0 + v1) / 2, d - 0.05), s1 - s0 + 0.08, v1 - v0 + 0.08, 0.09, frame_band,
                   bev=0.01 if detail == 0 else 0)


def blade_sign(g, F, s, v0, v1, k, detail, out=0.9):
    """Projecting double-sided blade sign (perpendicular to the facade)."""
    u0, vv0, u1, vv1 = sign_uv(k)
    a, b = F.P(s, v0, 0.15), F.P(s, v0, 0.15 + out)
    c, d = F.P(s, v1, 0.15 + out), F.P(s, v1, 0.15)
    # the sign art runs vertically on a blade: rotate UVs
    qn(g, [a, b, c, d], F.u, sign_mat(k), [(u0, vv0), (u1, vv0), (u1, vv1), (u0, vv1)])
    qn(g, [a, b, c, d], mul(F.u, -1), sign_mat(k), [(u0, vv0), (u1, vv0), (u1, vv1), (u0, vv1)])
    if detail < 2:
        seg_box(g, F.P(s, v1 + 0.05, 0.0), F.P(s, v1 + 0.05, 0.2 + out), 0.05, 0.05, "NB_Trim", band=TRIM_BLACK)


def shopfront(g, a, b, y0, h, detail, rng, sign=None, awning_band=None, shop_cell=0, riser_mat="NB_Trim",
              riser_band=TRIM_TILE, frame_band=TRIM_BLACK, bays=None, door_bay=None, fascia_mat="NB_Accent",
              awning_depth=1.8, recess=0.35):
    """Ground-floor retail on footprint edge a->b from y0, height h (usually 3.6-4.5 m)."""
    F = Frame(a, b)
    L = F.L
    nb = bays or max(1, int(round(L / 3.2)))
    bw = L / nb
    door_bay = nb // 2 if door_bay is None else door_bay
    gh = h - 1.0                       # glazing top
    fascia0, fascia1 = gh + 0.15, h - 0.05
    rev = recess if detail < 2 else 0.0
    n = F.n
    # outer frame: fascia band across the top, piers at bay lines
    qn(g, [F.P(0, y0 + gh), F.P(L, y0 + gh), F.P(L, y0 + h), F.P(0, y0 + h)], n, fascia_mat)
    pw = 0.28
    for k in range(nb + 1):
        s = k * bw
        sa, sb = max(0.0, s - pw / 2), min(L, s + pw / 2)
        qn(g, [F.P(sa, y0), F.P(sb, y0), F.P(sb, y0 + gh), F.P(sa, y0 + gh)], n, fascia_mat)
        if detail < 2:
            _frame_box(g, F, F.P(s, y0 + gh / 2, 0.06), pw + 0.06, gh, 0.12, None, bev=0.02 if detail == 0 else 0, mat=fascia_mat)
    for k in range(nb):
        s0, s1 = k * bw + pw / 2, (k + 1) * bw - pw / 2
        # reveal soffit + jambs
        if rev > 0:
            qn(g, [F.P(s0, y0 + gh, 0), F.P(s1, y0 + gh, 0), F.P(s1, y0 + gh, -rev), F.P(s0, y0 + gh, -rev)], (0, -1, 0), fascia_mat)
            qn(g, [F.P(s0, y0, 0), F.P(s0, y0, -rev), F.P(s0, y0 + gh, -rev), F.P(s0, y0 + gh, 0)], F.u, fascia_mat)
            qn(g, [F.P(s1, y0, 0), F.P(s1, y0 + gh, 0), F.P(s1, y0 + gh, -rev), F.P(s1, y0, -rev)], mul(F.u, -1), fascia_mat)
            qn(g, [F.P(s0, y0 + 0.01, 0), F.P(s0, y0 + 0.01, -rev), F.P(s1, y0 + 0.01, -rev), F.P(s1, y0 + 0.01, 0)], (0, 1, 0), "NB_Terrazzo")
        is_door = (k == door_bay)
        riser = 0.0 if is_door else 0.5
        if riser > 0:
            if riser_mat == "NB_Trim":
                _frame_box(g, F, F.P((s0 + s1) / 2, y0 + riser / 2, -rev + 0.06), s1 - s0, riser, 0.12, riser_band)
            else:
                _frame_box(g, F, F.P((s0 + s1) / 2, y0 + riser / 2, -rev + 0.06), s1 - s0, riser, 0.12, None, mat=riser_mat)
        cu = shop_uv(shop_cell if not is_door else (shop_cell + 1) % 8)
        qn(g, [F.P(s0, y0 + riser, -rev), F.P(s1, y0 + riser, -rev), F.P(s1, y0 + gh, -rev), F.P(s0, y0 + gh, -rev)], n,
           "NB_ShopInterior", [(cu[0], cu[1] + (cu[3] - cu[1]) * (riser / gh)), (cu[2], cu[1] + (cu[3] - cu[1]) * (riser / gh)),
                               (cu[2], cu[3]), (cu[0], cu[3])])
        if detail < 2:
            fw = 0.06
            for (sa, va, sb, vb) in ((s0, y0 + gh - fw, s1, y0 + gh), (s0, y0 + riser, s0 + fw, y0 + gh),
                                     (s1 - fw, y0 + riser, s1, y0 + gh), (s0, y0 + riser, s1, y0 + riser + fw)):
                _frame_box(g, F, F.P((sa + sb) / 2, (va + vb) / 2, -rev + 0.03), sb - sa, vb - va, 0.06, frame_band)
            if detail == 0:
                vt = y0 + gh - 0.55
                _frame_box(g, F, F.P((s0 + s1) / 2, vt, -rev + 0.03), s1 - s0, 0.05, 0.05, frame_band)
                if is_door:
                    _frame_box(g, F, F.P((s0 + s1) / 2, (y0 + vt) / 2, -rev + 0.03), 0.05, vt - y0, 0.05, frame_band)
                    for sx in ((s0 + s1) / 2 - 0.12, (s0 + s1) / 2 + 0.12):
                        _frame_box(g, F, F.P(sx, y0 + 1.05, -rev + 0.08), 0.03, 0.4, 0.04, TRIM_BRONZE)
                else:
                    _frame_box(g, F, F.P((s0 + s1) / 2, (y0 + riser + vt) / 2, -rev + 0.03), 0.05, vt - y0 - riser, 0.05, frame_band)
    if sign is not None:
        sw = min(L - 0.6, (fascia1 - fascia0) * 4.0)
        sc = L / 2
        sign_board(g, F, sc - sw / 2, sc + sw / 2, y0 + fascia0 + 0.05, y0 + fascia1 - 0.05, 0.12, sign, detail)
    if awning_band is not None:
        awning(g, F, 0.1, L - 0.1, y0 + gh - 0.05, awning_depth, 0.55, awning_band, detail)
    return F


# ------------------------------------------------------------------ plinth
def plinth(g, poly, y_top, depth, detail, mat="NB_Stone"):
    """Foundation skirt so buildings sit on sloping ground without floating (goes below grade)."""
    prism_poly(g, poly, y_top - depth, y_top, mat, top=False)
    if detail < 2:
        for a, b in poly_edges(poly):
            F = Frame(a, b)
            _frame_box(g, F, F.P(F.L / 2, y_top - 0.05, 0.03), F.L + 0.06, 0.1, 0.06, TRIM_COPING)


def floor_slab_edge(g, poly, y, detail, proj=0.08, mat="NB_Accent"):
    """Projecting floor-slab band around a footprint (every storey) - horizontal shadow line."""
    if detail >= 2:
        return
    for a, b in poly_edges(poly):
        F = Frame(a, b)
        _frame_box(g, F, F.P(F.L / 2, y, proj / 2), F.L + 2 * proj, 0.22, proj, None,
                   bev=0.02 if detail == 0 else 0, mat=mat)


def corner_quoins(g, poly, y0, y1, detail, w=0.45, proj=0.06, mat="NB_Accent"):
    if detail >= 2:
        return
    for p in poly:
        bbox(g, (p[0], (y0 + y1) / 2, p[1]), (w, y1 - y0, w), mat, 0.03 if detail == 0 else 0.0, detail=detail)


def roof_deck_edge(g, poly, y, detail, mat="NB_Accent", h=0.5, proj=0.35):
    """Deep projecting roof fascia / eyebrow (flat-roof tropical modern)."""
    for a, b in poly_edges(poly):
        F = Frame(a, b)
        _frame_box(g, F, F.P(F.L / 2, y + h / 2, proj / 2 - 0.05), F.L + 2 * proj, h, proj + 0.1, None,
                   bev=0.03 if detail == 0 else 0, mat=mat)

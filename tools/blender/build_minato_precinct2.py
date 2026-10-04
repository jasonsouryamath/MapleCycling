"""
MINATO COAST - START PRECINCT (board rows E7 "Minato polish" + B2 "ride start backdrop").

The Minato ride starts at route metre 0 (9000, 6, 4200) on an empty lawn: the start pad, a
grass field, a far tan box warehouse and nothing else within 150 m. This module authors the
hero-quality harbour architecture that turns that lawn into a real port precinct:

  1. RED BRICK WAREHOUSE (Yokohama Aka-renga style, No.1 = 12 bays / 76 m, No.2 = 9 bays / 57 m):
     three storeys of running-bond brick between stone plinth, string courses and a stepped
     cornice; pilasters at every bay; an arcade of arched shopfronts on the ground floor;
     recessed sash windows with stone lintels/sills and white frames; cast-iron balconies;
     a slate gable roof with ridge monitors and chimneys; glass-and-iron entrance canopies;
     gable roundels and a frieze sign board.
  2. PORT SHED (modern cold store): corrugated cladding (tinted per instance in Unity), a
     translucent clerestory, roller doors in framed reveals, a loading dock with bumpers and a
     hazard nosing, a cantilever canopy, fascia sign band, downpipes. It replaces the legacy
     flat 'Minato_Port_Warehouse' boxes kept as city backdrop.
  3. PRECINCT TOTEM: the MINATO PORT monument that terminates the road axis behind the start.

Textures (procedural, written to Assets/Environment/MinatoCoast/Textures):
  Minato_Precinct_Brick_Albedo, _Slate_, _Paver_, _Corrugated_, _RollerDoor_, _Sign_.

Run:  tools/blender-4.5.10-windows-x64/blender.exe -b --factory-startup -P tools/blender/build_minato_precinct2.py
      [-- textures warehouse1 warehouse2 shed totem]

Authoring: UNITY coordinates through the harbor MB builder (per-quad intended normals, so
winding is right without a recalc). Local axes per family: +Z = long axis, +X = across,
+Y = up, origin at ground centre, y = 0 = grade (plinths continue 1.5 m below it so nothing
floats on a sloping lawn). Material SLOT NAMES are the Unity contract (MinatoRedesign
.StartPrecinct.cs re-skins by token "prec_*" / "shed_*").
"""

import math
import os
import sys

import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402
from build_minato_harbor import MB, save_png, _noise, tri_count  # noqa: E402

M.PALETTE.update({
    "prec_brick":      (0.620, 0.250, 0.180, 1.0),
    "prec_stone":      (0.860, 0.820, 0.740, 1.0),
    "prec_iron":       (0.100, 0.200, 0.170, 1.0),
    "prec_glass":      (0.180, 0.260, 0.320, 1.0),
    "prec_glass_warm": (0.900, 0.700, 0.420, 1.0),
    "prec_frame":      (0.930, 0.920, 0.880, 1.0),
    "prec_timber":     (0.330, 0.200, 0.120, 1.0),
    "prec_slate":      (0.250, 0.270, 0.300, 1.0),
    "prec_sign":       (0.100, 0.250, 0.200, 1.0),
    "prec_copper":     (0.420, 0.640, 0.560, 1.0),
    "shed_clad":       (0.880, 0.890, 0.900, 1.0),
    "shed_trim":       (0.150, 0.300, 0.550, 1.0),
    "shed_door":       (0.760, 0.780, 0.800, 1.0),
    "shed_panel":      (0.820, 0.860, 0.880, 1.0),
    "shed_concrete":   (0.700, 0.690, 0.660, 1.0),
    "shed_dark":       (0.120, 0.130, 0.140, 1.0),
    "shed_hazard":     (0.900, 0.700, 0.150, 1.0),
    "shed_sign":       (0.100, 0.200, 0.500, 1.0),
    "shed_glass":      (0.200, 0.300, 0.360, 1.0),
})

# UV density per slot (UV units per metre). Brick/slate/pavers tile every 2 m, cladding 2 m.
UVK = {"prec_brick": 0.5, "prec_slate": 0.5, "prec_stone": 0.5, "shed_clad": 0.5,
       "shed_door": 0.25, "shed_concrete": 0.4, "shed_hazard": 1.0}


# ============================================================================ facade frame

class Face:
    """A planar facade frame: P(a, b, c) = origin + a*u + b*up + c*n (n = outward)."""

    def __init__(self, mb, origin, u, n):
        self.mb = mb
        self.o = Vector(origin)
        self.u = Vector(u).normalized()
        self.n = Vector(n).normalized()
        self.v = Vector((0.0, 1.0, 0.0))

    def P(self, a, b, c=0.0):
        return tuple(self.o + self.u * a + self.v * b + self.n * c)

    def poly(self, pts_abc, slot, normal, uv_mode="ab"):
        k = UVK.get(slot, 0.35)
        pts = [self.P(*p) for p in pts_abc]
        if uv_mode == "ab":
            uvs = [(p[0] * k, p[1] * k) for p in pts_abc]
        elif uv_mode == "cb":
            uvs = [(p[2] * k, p[1] * k) for p in pts_abc]
        else:
            uvs = [(p[0] * k, p[2] * k) for p in pts_abc]
        self.mb.poly(pts, uvs, slot, tuple(normal))

    def rect(self, a0, b0, a1, b1, slot, c=0.0, uvs=None):
        pts = [(a0, b0, c), (a1, b0, c), (a1, b1, c), (a0, b1, c)]
        if uvs is not None:
            self.mb.poly([self.P(*p) for p in pts], uvs, slot, tuple(self.n))
        else:
            self.poly(pts, slot, self.n)

    def box(self, a0, a1, b0, b1, c0, c1, slot, skip="n-", front_uv=None):
        """Box in the frame. skip: faces not emitted (default the back, which sits on the wall)."""
        U, V, N = self.u, self.v, self.n
        faces = {
            "n+": ([(a0, b0, c1), (a1, b0, c1), (a1, b1, c1), (a0, b1, c1)], N, "ab"),
            "n-": ([(a0, b0, c0), (a1, b0, c0), (a1, b1, c0), (a0, b1, c0)], -N, "ab"),
            "u+": ([(a1, b0, c0), (a1, b0, c1), (a1, b1, c1), (a1, b1, c0)], U, "cb"),
            "u-": ([(a0, b0, c0), (a0, b0, c1), (a0, b1, c1), (a0, b1, c0)], -U, "cb"),
            "v+": ([(a0, b1, c0), (a1, b1, c0), (a1, b1, c1), (a0, b1, c1)], V, "ac"),
            "v-": ([(a0, b0, c0), (a1, b0, c0), (a1, b0, c1), (a0, b0, c1)], -V, "ac"),
        }
        for key, (pts, nrm, mode) in faces.items():
            if key in skip:
                continue
            if key == "n+" and front_uv is not None:
                self.mb.poly([self.P(*p) for p in pts], front_uv, slot, tuple(nrm))
            else:
                self.poly(pts, slot, nrm, mode)


def arc_pts(am, spring, r, t0, t1, n):
    return [(am + r * math.cos(t0 + (t1 - t0) * i / n), spring + r * math.sin(t0 + (t1 - t0) * i / n))
            for i in range(n + 1)]


def wall_with_openings(f, a0, a1, b0, b1, openings, slot, c=0.0):
    """Fill the rectangle [a0,a1]x[b0,b1] with wall quads around rectangular/arched openings."""
    br = sorted(set([a0, a1] + [x for o in openings for x in (o["a0"], o["a1"])]))
    for i in range(len(br) - 1):
        x0, x1 = br[i], br[i + 1]
        if x1 - x0 < 1e-4:
            continue
        xm = 0.5 * (x0 + x1)
        blocked = sorted((o["b0"], o["b1"]) for o in openings if o["a0"] <= xm <= o["a1"])
        y = b0
        for y0, y1 in blocked:
            if y0 > y + 1e-4:
                f.rect(x0, y, x1, y0, slot, c)
            y = max(y, y1)
        if y < b1 - 1e-4:
            f.rect(x0, y, x1, b1, slot, c)
    for o in openings:
        r = o.get("r", 0.0)
        if r <= 0:
            continue
        am = 0.5 * (o["a0"] + o["a1"])
        spring = o["b1"] - r
        for corner, t0, t1 in ((o["a0"], math.pi, math.pi * 0.5), (o["a1"], 0.0, math.pi * 0.5)):
            arc = arc_pts(am, spring, r, t0, t1, 6)
            for k in range(len(arc) - 1):
                f.poly([(corner, o["b1"], c), (arc[k][0], arc[k][1], c), (arc[k + 1][0], arc[k + 1][1], c)],
                       slot, f.n)


def opening_fill(f, o, depth, reveal_slot, fill_slot, c=0.0, frame_slot=None, mullions=(1, 1),
                 detail=0):
    """Reveals (jambs, head/arch, sill) and the recessed fill (glass or door) of one opening."""
    a0, a1, b0, b1 = o["a0"], o["a1"], o["b0"], o["b1"]
    r = o.get("r", 0.0)
    d = c - depth
    U, V = f.u, f.v
    top = b1 - r if r > 0 else b1
    f.poly([(a0, b0, c), (a0, top, c), (a0, top, d), (a0, b0, d)], reveal_slot, U, "cb")
    f.poly([(a1, b0, c), (a1, top, c), (a1, top, d), (a1, b0, d)], reveal_slot, -U, "cb")
    f.poly([(a0, b0, c), (a1, b0, c), (a1, b0, d), (a0, b0, d)], reveal_slot, V, "ac")
    if r <= 0:
        f.poly([(a0, b1, c), (a1, b1, c), (a1, b1, d), (a0, b1, d)], reveal_slot, -V, "ac")
        f.rect(a0, b0, a1, b1, fill_slot, d, uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
    else:
        am = 0.5 * (a0 + a1)
        arc = arc_pts(am, top, r, math.pi, 0.0, 10)
        for k in range(len(arc) - 1):
            (x0, y0), (x1, y1) = arc[k], arc[k + 1]
            mid = 0.5 * (math.pi - math.pi * (k + 0.5) / 10.0) + 0.0
            ang = math.pi - math.pi * (k + 0.5) / 10.0
            inward = -(U * math.cos(ang) + V * math.sin(ang))
            f.poly([(x0, y0, c), (x1, y1, c), (x1, y1, d), (x0, y0, d)], reveal_slot, inward, "ac")
        f.rect(a0, b0, a1, top, fill_slot, d, uvs=[(0, 0), (1, 0), (1, 0.7), (0, 0.7)])
        for k in range(len(arc) - 1):
            f.mb.poly([f.P(am, top, d), f.P(arc[k][0], arc[k][1], d), f.P(arc[k + 1][0], arc[k + 1][1], d)],
                      [(0.5, 0.7), (0, 1), (1, 1)], fill_slot, tuple(f.n))
    if frame_slot is None or detail > 0:
        return
    # Frames: perimeter + mullion/transom bars, just proud of the fill.
    t, fc0, fc1 = 0.07, d, d + 0.06
    f.box(a0, a0 + t, b0, top, fc0, fc1, frame_slot)
    f.box(a1 - t, a1, b0, top, fc0, fc1, frame_slot)
    f.box(a0, a1, b0, b0 + t, fc0, fc1, frame_slot)
    if r <= 0:
        f.box(a0, a1, b1 - t, b1, fc0, fc1, frame_slot)
    else:
        f.box(a0, a1, top - t * 0.5, top + t * 0.5, fc0, fc1, frame_slot)   # transom at springing
    nu, nv = mullions
    for i in range(1, nu + 1):
        x = a0 + (a1 - a0) * i / (nu + 1)
        f.box(x - t * 0.4, x + t * 0.4, b0, top, fc0, fc1 - 0.01, frame_slot)
    for j in range(1, nv + 1):
        y = b0 + (top - b0) * j / (nv + 1)
        f.box(a0, a1, y - t * 0.4, y + t * 0.4, fc0, fc1 - 0.01, frame_slot)


def arch_ring(f, o, slot, width=0.32, proud=0.06, c=0.0, keystone=True):
    """Stone voussoir ring over an arched opening (face annulus + edges + keystone)."""
    r = o["r"]
    am = 0.5 * (o["a0"] + o["a1"])
    spring = o["b1"] - r
    inner = arc_pts(am, spring, r, math.pi, 0.0, 10)
    outer = arc_pts(am, spring, r + width, math.pi, 0.0, 10)
    cp = c + proud
    for k in range(10):
        (ix0, iy0), (ix1, iy1) = inner[k], inner[k + 1]
        (ox0, oy0), (ox1, oy1) = outer[k], outer[k + 1]
        f.poly([(ix0, iy0, cp), (ix1, iy1, cp), (ox1, oy1, cp), (ox0, oy0, cp)], slot, f.n)
        ang = math.pi - math.pi * (k + 0.5) / 10.0
        radial = f.u * math.cos(ang) + f.v * math.sin(ang)
        f.poly([(ox0, oy0, c), (ox1, oy1, c), (ox1, oy1, cp), (ox0, oy0, cp)], slot, radial, "ac")
        f.poly([(ix0, iy0, c), (ix1, iy1, c), (ix1, iy1, cp), (ix0, iy0, cp)], slot, -radial, "ac")
    if keystone:
        top = o["b1"]
        f.box(am - 0.22, am + 0.22, top - 0.06, top + width + 0.12, c, cp + 0.06, slot)


# ============================================================================ pixel font

FONT = {
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "C": ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "G": ["01111", "10000", "10000", "10011", "10001", "10001", "01111"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "I": ["11111", "00100", "00100", "00100", "00100", "00100", "11111"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
    "T": ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "W": ["10001", "10001", "10001", "10101", "10101", "11011", "10001"],
    "Y": ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
    "0": ["01110", "10011", "10101", "10101", "10101", "11001", "01110"],
    "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
    "2": ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
    "9": ["01110", "10001", "10001", "01111", "00001", "00001", "01110"],
    ".": ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
    "-": ["00000", "00000", "00000", "01110", "00000", "00000", "00000"],
    " ": ["00000"] * 7,
}


def stamp_text(rgb, text, x0, x1, y0, y1, color):
    """Centre ``text`` in the pixel box [x0,x1)x[y0,y1) (row 0 = top) with a 5x7 font."""
    cols = len(text) * 6 - 1
    s = max(1, min((x1 - x0) // cols, (y1 - y0) // 7))
    w, h = cols * s, 7 * s
    ox, oy = x0 + (x1 - x0 - w) // 2, y0 + (y1 - y0 - h) // 2
    for ci, ch in enumerate(text):
        g = FONT.get(ch, FONT[" "])
        for gy, row in enumerate(g):
            for gx, bit in enumerate(row):
                if bit == "1":
                    px, py = ox + (ci * 6 + gx) * s, oy + gy * s
                    rgb[py:py + s, px:px + s] = color


# ============================================================================ textures

def brick_texture():
    """Running-bond red brick with lime mortar; 1 UV = 2 m (8 bricks x 26 courses)."""
    W = H = 1024
    rgb = np.zeros((H, W, 3), dtype=np.float32)
    courses, per = 26, 8
    ch, bw = H / courses, W / per
    rng = np.random.default_rng(1911)
    y = np.arange(H)[:, None]
    x = np.arange(W)[None, :]
    row = (y // ch).astype(int)
    xo = x + (row % 2) * (bw * 0.5)
    col = (xo // bw).astype(int) % per
    tints = rng.random((courses, per))
    base = np.array([0.60, 0.24, 0.16], dtype=np.float32)
    dark = np.array([0.40, 0.17, 0.13], dtype=np.float32)
    warm = np.array([0.72, 0.34, 0.20], dtype=np.float32)
    t = tints[row % courses, col]
    col_rgb = np.where((t < 0.12)[:, :, None], dark, np.where((t > 0.84)[:, :, None], warm, base))
    jitter = (0.88 + 0.22 * rng.random((courses, per)))[row % courses, col][:, :, None]
    rgb[:] = col_rgb * jitter
    n = _noise(H, W, 18.0, 4)
    rgb *= (0.86 + 0.24 * n)[:, :, None]
    mortar = ((y % ch) < 3.2) | ((xo % bw) < 3.2)
    rgb = np.where(mortar[:, :, None], np.array([0.74, 0.71, 0.65], dtype=np.float32) * (0.9 + 0.1 * n[:, :, None]), rgb)
    return save_png("Minato_Precinct_Brick_Albedo", rgb)


def slate_texture():
    W = H = 512
    rgb = np.zeros((H, W, 3), dtype=np.float32)
    rows, per = 16, 10
    ch, bw = H / rows, W / per
    rng = np.random.default_rng(77)
    y = np.arange(H)[:, None]
    x = np.arange(W)[None, :]
    row = (y // ch).astype(int)
    xo = x + (row % 2) * (bw * 0.5)
    col = (xo // bw).astype(int) % per
    v = (0.80 + 0.30 * rng.random((rows, per)))[row % rows, col]
    rgb[:] = np.array([0.30, 0.32, 0.36], dtype=np.float32) * v[:, :, None]
    shade = ((y % ch) / ch)[:, :, None]
    rgb *= (0.72 + 0.34 * shade)
    rgb = np.where(((y % ch) < 2)[:, :, None] | ((xo % bw) < 1.5)[:, :, None], rgb * 0.55, rgb)
    return save_png("Minato_Precinct_Slate_Albedo", rgb)


def paver_texture():
    """Granite setts in a stretcher bond, 1 UV = 2.4 m (8 x 0.3 m by 4 x 0.6 m)."""
    W = H = 1024
    rng = np.random.default_rng(88)
    rows, per = 8, 4
    ch, bw = H / rows, W / per
    y = np.arange(H)[:, None]
    x = np.arange(W)[None, :]
    row = (y // ch).astype(int)
    xo = x + (row % 2) * (bw * 0.5)
    col = (xo // bw).astype(int) % per
    tone = rng.random((rows, per))
    a = np.array([0.66, 0.64, 0.60], dtype=np.float32)
    b = np.array([0.55, 0.54, 0.52], dtype=np.float32)
    c = np.array([0.74, 0.70, 0.64], dtype=np.float32)
    t = tone[row % rows, col][:, :, None]
    rgb = np.where(t < 0.35, b, np.where(t > 0.8, c, a)) * (0.92 + 0.12 * t)
    n = _noise(H, W, 10.0, 12)
    speck = _noise(H, W, 2.0, 13)
    rgb *= (0.90 + 0.14 * n)[:, :, None]
    rgb *= (0.94 + 0.10 * speck)[:, :, None]
    joint = ((y % ch) < 4) | ((xo % bw) < 4)
    rgb = np.where(joint[:, :, None], np.array([0.36, 0.35, 0.33], dtype=np.float32), rgb)
    return save_png("Minato_Precinct_Paver_Albedo", rgb.astype(np.float32))


def corrugated_texture():
    """White-grey trapezoid ribs (tinted by the Unity material), 1 UV = 2 m, 10 ribs per UV."""
    W = H = 512
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    ph = (U * 10.0) % 1.0
    rib = np.where(ph < 0.28, 1.0, np.where(ph < 0.40, 0.78, np.where(ph < 0.88, 0.90, 0.70)))
    val = 0.80 + 0.18 * rib
    streak = _noise(H, W, 40.0, 5)
    val *= 0.93 + 0.08 * streak
    val -= 0.10 * np.clip(V - 0.8, 0, 1) * 5.0 * np.clip(_noise(H, W, 16.0, 6) - 0.3, 0, 1)
    rgb = np.stack([val, val, val], axis=-1)
    return save_png("Minato_Precinct_Corrugated_Albedo", rgb.astype(np.float32))


def rollerdoor_texture():
    W = H = 256
    v = (np.arange(H) + 0.5) / H
    V = np.repeat(v[:, None], W, axis=1)
    ph = (V * 16.0) % 1.0
    val = np.where(ph < 0.12, 0.62, 0.86 + 0.08 * np.sin(ph * math.pi))
    val = val * (0.94 + 0.08 * _noise(H, W, 20.0, 9))
    rgb = np.stack([val, val, val * 1.01], axis=-1)
    return save_png("Minato_Precinct_RollerDoor_Albedo", rgb.astype(np.float32))


SIGN_ROWS = ["MINATO SOKO NO.1", "MINATO SOKO NO.2", "MINATO PORT", "MINATO LOGISTICS",
             "EST. 1911", "MINATO COLD STORE", "PORT OF MINATO", "WELCOME RIDERS"]


def sign_texture():
    """8 rows x 1024 x 128 px; each row a sign face. Brick-era rows green/gold, modern navy/white."""
    W, H = 1024, 1024
    rgb = np.zeros((H, W, 3), dtype=np.float32)
    green = np.array([0.07, 0.22, 0.17], dtype=np.float32)
    gold = np.array([0.93, 0.76, 0.38], dtype=np.float32)
    navy = np.array([0.06, 0.18, 0.46], dtype=np.float32)
    white = np.array([0.97, 0.97, 0.98], dtype=np.float32)
    coral = np.array([0.95, 0.44, 0.27], dtype=np.float32)
    for i, text in enumerate(SIGN_ROWS):
        y0, y1 = i * 128, (i + 1) * 128
        heritage = i in (0, 1, 4, 6)
        bg, fg = (green, gold) if heritage else (navy, white)
        rgb[y0:y1] = bg
        rgb[y0 + 6:y0 + 10, 8:W - 8] = fg
        rgb[y1 - 10:y1 - 6, 8:W - 8] = fg
        rgb[y0 + 6:y1 - 6, 8:12] = fg
        rgb[y0 + 6:y1 - 6, W - 12:W - 8] = fg
        if not heritage:
            rgb[y1 - 22:y1 - 14, 40:W - 40] = coral
        stamp_text(rgb, text, 40, W - 40, y0 + 18, y1 - 26, fg)
    n = _noise(H, W, 30.0, 3)
    rgb *= (0.94 + 0.08 * n)[:, :, None]
    return save_png("Minato_Precinct_Sign_Albedo", rgb)


def sign_uv(row):
    """Front-face UVs for SIGN_ROWS[row] (V = 0 is the texture's bottom)."""
    v0 = 1.0 - (row + 1) / len(SIGN_ROWS)
    v1 = 1.0 - row / len(SIGN_ROWS)
    return [(0.0, v0), (1.0, v0), (1.0, v1), (0.0, v1)]


# ============================================================================ red brick warehouse

PLINTH_TOP, F2_Y, F3_Y, EAVE_Y = 1.1, 5.6, 10.4, 15.0
CORNICE_Y = 15.8
ROOF_PITCH = math.radians(24.0)
BELOW = -1.5


def brick_facade(mb, f, length, bays, detail, gable=False, sign_row=None, entrance=False):
    """One elevation: f.u runs along it from a = 0..length."""
    bay = length / bays
    pil = 0.72
    ops, arcs = [], []
    for i in range(bays):
        a0, a1 = i * bay + pil * 0.5, (i + 1) * bay - pil * 0.5
        am = 0.5 * (a0 + a1)
        # ground floor: one arched shopfront / door per bay
        w = min(3.5, (a1 - a0) - 1.3)
        mid_bay = i == bays // 2
        door = (entrance and mid_bay) or (not gable and i % 4 == 1)
        o = {"a0": am - w * 0.5, "a1": am + w * 0.5, "b0": PLINTH_TOP,
             "b1": 4.85, "r": w * 0.5, "kind": "door" if door else "shop"}
        ops.append(o)
        arcs.append(o)
        if door:
            # the ground floor is a raised loading level: two stone steps up to the threshold
            f.box(o["a0"] - 0.35, o["a1"] + 0.35, BELOW, 0.55, 0.0, 1.10, "prec_stone")
            f.box(o["a0"] - 0.20, o["a1"] + 0.20, 0.55, PLINTH_TOP, 0.0, 0.55, "prec_stone")
        # upper floors: two sash windows per bay (one on narrow gable bays); balcony bays get
        # French doors down to the balcony slab
        n_win = 2 if (a1 - a0) > 4.2 else 1
        ww = 1.22
        balcony = (not gable) and i % 2 == 1
        for fl, (wb0, wb1) in enumerate(((6.55, 8.95), (11.3, 13.6))):
            if balcony:
                wb0 = (F2_Y, F3_Y)[fl] + 0.42
            for k in range(n_win):
                ax = am + (k - (n_win - 1) * 0.5) * 2.1
                ops.append({"a0": ax - ww * 0.5, "a1": ax + ww * 0.5, "b0": wb0, "b1": wb1, "kind": "win"})
    # --- wall
    if detail < 2:
        wall_with_openings(f, 0.0, length, PLINTH_TOP, EAVE_Y, ops, "prec_brick")
        for o in ops:
            kind = o["kind"]
            if kind == "win":
                opening_fill(f, o, 0.26, "prec_brick", "prec_glass", frame_slot="prec_frame",
                             mullions=(1, 2), detail=detail)
                f.box(o["a0"] - 0.14, o["a1"] + 0.14, o["b1"], o["b1"] + 0.30, 0.0, 0.12, "prec_stone")
                f.box(o["a0"] - 0.10, o["a1"] + 0.10, o["b0"] - 0.10, o["b0"], 0.0, 0.16, "prec_stone")
            else:
                fill = "prec_timber" if kind == "door" else "prec_glass_warm"
                opening_fill(f, o, 0.34, "prec_stone", fill, frame_slot="prec_frame",
                             mullions=(1, 1) if kind == "door" else (2, 1), detail=detail)
                arch_ring(f, o, "prec_stone", keystone=detail == 0)
    else:
        f.rect(0.0, PLINTH_TOP, length, EAVE_Y, "prec_brick")
        for o in ops:
            slot = "prec_glass" if o["kind"] == "win" else ("prec_timber" if o["kind"] == "door" else "prec_glass_warm")
            top = o["b1"] - o.get("r", 0.0) * 0.4
            f.rect(o["a0"], o["b0"], o["a1"], top, slot, 0.02, uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
    # --- pilasters (incl. the corner quoins) and horizontal stone
    for i in range(bays + 1):
        a = i * bay
        a0 = -0.30 if i == 0 else a - pil * 0.5
        a1 = length + 0.30 if i == bays else a + pil * 0.5
        if detail < 2:
            f.box(a0, a1, PLINTH_TOP, EAVE_Y, 0.0, 0.30, "prec_brick")
            f.box(a0 - 0.04, a1 + 0.04, EAVE_Y - 0.9, EAVE_Y - 0.55, 0.0, 0.36, "prec_stone")   # capital
    f.box(-0.12, length + 0.12, BELOW, PLINTH_TOP, 0.0, 0.14, "prec_stone")                 # plinth
    for y in (F2_Y, F3_Y):
        f.box(0.0, length, y - 0.16, y + 0.14, 0.0, 0.22, "prec_stone")                      # string course
    f.box(-0.10, length + 0.10, EAVE_Y, EAVE_Y + 0.38, 0.0, 0.30, "prec_stone")              # cornice
    f.box(-0.30, length + 0.30, EAVE_Y + 0.38, CORNICE_Y, 0.0, 0.58, "prec_stone")
    if detail == 0:
        for i in range(bays * 3):                                                             # dentils
            a = (i + 0.5) * length / (bays * 3)
            f.box(a - 0.14, a + 0.14, EAVE_Y + 0.14, EAVE_Y + 0.38, 0.30, 0.44, "prec_stone")
    # --- iron balconies on alternate bays of the long elevations
    if not gable and detail == 0:
        for i in range(1, bays, 2):
            am = (i + 0.5) * bay
            for y in (F2_Y + 0.14, F3_Y + 0.14):
                hw = 1.95
                f.box(am - hw, am + hw, y, y + 0.14, 0.0, 1.18, "prec_iron")
                f.box(am - hw, am + hw, y + 1.0, y + 1.07, 1.10, 1.18, "prec_iron")          # top rail
                f.box(am - hw, am + hw, y + 0.34, y + 0.40, 1.12, 1.16, "prec_iron")          # bottom rail
                for side in (-1, 1):
                    f.box(am + side * hw - 0.04, am + side * hw + 0.04, y + 1.0, y + 1.07, 0.22, 1.18, "prec_iron")
                nb = 14
                for k in range(nb + 1):
                    x = am - hw + 2 * hw * k / nb
                    f.box(x - 0.02, x + 0.02, y + 0.14, y + 1.0, 1.12, 1.16, "prec_iron")
                for side in (-0.7, 0.7):                                                      # brackets
                    f.box(am + side * hw - 0.05, am + side * hw + 0.05, y - 0.55, y, 0.22, 0.75, "prec_iron")
    # --- downpipes at the ends and every 4 bays
    if detail == 0:
        for i in range(0, bays + 1, 4 if not gable else bays):
            a = min(length - 0.55, max(0.55, i * bay + (0.55 if i == 0 else -0.55)))
            f.box(a - 0.08, a + 0.08, 0.0, EAVE_Y + 0.3, 0.02, 0.18, "prec_iron")
    # --- frieze sign board (long elevations) / gable roundel is added by the caller
    if sign_row is not None and detail < 2:
        mid = length * 0.5
        f.box(mid - 7.0, mid + 7.0, 13.95, 14.85, 0.40, 0.50, "prec_sign", front_uv=sign_uv(sign_row))
    return arcs


def entrance_canopy(f, am, detail):
    """Cast-iron and glass canopy over the gable's middle arch."""
    hw, proj, y = 2.8, 2.6, 5.0
    f.box(am - hw, am + hw, y, y + 0.16, 0.0, proj, "prec_iron", skip="")
    f.box(am - hw, am + hw, y + 0.16, y + 0.22, 0.05, proj - 0.05, "prec_glass", skip="")
    f.box(am - hw, am + hw, y - 0.36, y, proj - 0.10, proj, "prec_iron", skip="")
    for side in (-1, 1):
        f.box(am + side * (hw - 0.1) - 0.09, am + side * (hw - 0.1) + 0.09, 0.0, y, proj - 0.24, proj - 0.06,
              "prec_iron", skip="")
        if detail == 0:
            f.box(am + side * (hw - 0.1) - 0.16, am + side * (hw - 0.1) + 0.16, 0.0, 0.5, proj - 0.30, proj,
                  "prec_iron", skip="")


def brick_warehouse(length, bays, sign_row, detail=0, name="Minato_Precinct_BrickWarehouse"):
    D = 21.0
    hx, hz = D * 0.5, length * 0.5
    mb = MB()
    # elevations: +X long side, -X long side, +Z gable, -Z gable (u runs so n is outward)
    long_p = Face(mb, (hx, 0.0, -hz), (0, 0, 1), (1, 0, 0))
    long_m = Face(mb, (-hx, 0.0, hz), (0, 0, -1), (-1, 0, 0))
    gab_p = Face(mb, (hx, 0.0, hz), (-1, 0, 0), (0, 0, 1))
    gab_m = Face(mb, (-hx, 0.0, -hz), (1, 0, 0), (0, 0, -1))
    brick_facade(mb, long_p, length, bays, detail, sign_row=sign_row)
    brick_facade(mb, long_m, length, bays, detail, sign_row=sign_row)
    for g in (gab_p, gab_m):
        brick_facade(mb, g, D, 3, detail, gable=True, entrance=True)
        entrance_canopy(g, D * 0.5, detail)

    # --- roof: gable along Z with eaves overhang, fascia and soffit
    ov, ovg = 0.75, 0.5
    rise = (hx + ov) * math.tan(ROOF_PITCH)
    ey = CORNICE_Y
    ry = ey + rise
    z0, z1 = -hz - ovg, hz + ovg
    k = UVK["prec_slate"]
    slope = math.hypot(hx + ov, rise)
    for s in (1, -1):
        ex = s * (hx + ov)
        nrm = (s * math.sin(ROOF_PITCH), math.cos(ROOF_PITCH), 0.0)
        mb.poly([(ex, ey, z0), (ex, ey, z1), (0.0, ry, z1), (0.0, ry, z0)],
                [(0, 0), ((z1 - z0) * k, 0), ((z1 - z0) * k, slope * k), (0, slope * k)], "prec_slate", nrm)
        # soffit + fascia (so the eave never reads as a paper edge from below)
        mb.poly([(ex, ey - 0.25, z0), (ex, ey - 0.25, z1), (s * hx, ey - 0.25, z1), (s * hx, ey - 0.25, z0)],
                [(0, 0), (1, 0), (1, 1), (0, 1)], "prec_timber", (0, -1, 0))
        mb.poly([(ex, ey - 0.25, z0), (ex, ey - 0.25, z1), (ex, ey, z1), (ex, ey, z0)],
                [(0, 0), (1, 0), (1, 1), (0, 1)], "prec_frame", (s, 0, 0))
    # gable triangles (brick) + barge boards + roundel
    for g, zc in ((gab_p, hz), (gab_m, -hz)):
        g.poly([(0.0, CORNICE_Y, 0.0), (D, CORNICE_Y, 0.0), (hx, ry - 0.05, 0.0)], "prec_brick", g.n)
        sgn = 1 if zc > 0 else -1
        for s in (1, -1):
            ex = s * (hx + ov)
            mb.poly([(ex, ey - 0.02, zc + sgn * ovg), (0.0, ry - 0.02, zc + sgn * ovg),
                     (0.0, ry + 0.02, zc + sgn * (ovg + 0.01)), (ex, ey + 0.02, zc + sgn * (ovg + 0.01))],
                    [(0, 0), (1, 0), (1, 1), (0, 1)], "prec_frame", (0, 0, sgn))
            # barge board face
            mb.poly([(ex, ey - 0.35, zc + sgn * ovg), (0.0, ry - 0.35, zc + sgn * ovg),
                     (0.0, ry, zc + sgn * ovg), (ex, ey, zc + sgn * ovg)],
                    [(0, 0), (1, 0), (1, 1), (0, 1)], "prec_frame", (0, 0, sgn))
        if detail < 2:
            # roundel: octagonal stone disc with the sign-row "EST. 1911" plate below it
            cy, rr = CORNICE_Y + rise * 0.45, 1.25
            pts = [(hx + rr * math.cos(t * math.pi / 4), cy + rr * math.sin(t * math.pi / 4)) for t in range(8)]
            for t in range(8):
                a, b = pts[t], pts[(t + 1) % 8]
                g.poly([(hx, cy, 0.14), (a[0], a[1], 0.14), (b[0], b[1], 0.14)], "prec_stone", g.n)
                g.poly([(a[0], a[1], 0.0), (b[0], b[1], 0.0), (b[0], b[1], 0.14), (a[0], a[1], 0.14)],
                       "prec_stone", (g.u * math.cos((t + 0.5) * math.pi / 4) + g.v * math.sin((t + 0.5) * math.pi / 4)),
                       "ac")
            ri = rr * 0.72
            ptsi = [(hx + ri * math.cos(t * math.pi / 4), cy + ri * math.sin(t * math.pi / 4)) for t in range(8)]
            for t in range(8):
                a, b = ptsi[t], ptsi[(t + 1) % 8]
                g.poly([(hx, cy, 0.18), (a[0], a[1], 0.18), (b[0], b[1], 0.18)], "prec_copper", g.n)
            g.box(hx - 3.2, hx + 3.2, cy - rr - 0.95, cy - rr - 0.2, 0.0, 0.12, "prec_sign",
                  front_uv=sign_uv(4))
    # ridge cap
    mb.box((0.0, ry + 0.08, 0.0), (0.42, 0.22, z1 - z0 + 0.1), "prec_slate", "xXYzZ")
    if detail < 2:
        # ridge monitors (ventilators) and chimneys
        nmon = max(2, int(length / 18))
        for i in range(nmon):
            zc = -hz + (i + 0.5) * length / nmon
            mb.box((0.0, ry + 0.55, zc), (1.8, 0.9, 3.2), "prec_frame", "xXzZ")
            for s in (1, -1):
                mb.poly([(s * 1.25, ry + 1.0, zc - 1.8), (s * 1.25, ry + 1.0, zc + 1.8),
                         (0.0, ry + 1.55, zc + 1.8), (0.0, ry + 1.55, zc - 1.8)],
                        [(0, 0), (1, 0), (1, 1), (0, 1)], "prec_slate", (s * 0.4, 1.0, 0.0))
            for e in (-1, 1):
                mb.poly([(-1.25, ry + 1.0, zc + e * 1.8), (1.25, ry + 1.0, zc + e * 1.8),
                         (0.0, ry + 1.55, zc + e * 1.8)], [(0, 0), (1, 0), (0.5, 1)], "prec_slate", (0, 0, e))
        for i, zc in enumerate((-hz * 0.62, hz * 0.05, hz * 0.7)):
            x = hx * (0.42 if i % 2 == 0 else -0.42)
            base = ey + (hx + ov - abs(x)) * math.tan(ROOF_PITCH) - 0.6
            top = ry + 1.2
            mb.box((x, 0.5 * (base + top), zc), (1.1, top - base, 1.6), "prec_brick", "xXzZ")
            mb.box((x, top + 0.12, zc), (1.35, 0.24, 1.85), "prec_stone")
            if detail == 0:
                for pz in (-0.4, 0.4):
                    mb.box((x, top + 0.45, zc + pz), (0.34, 0.42, 0.34), "prec_iron", "xXYzZ")
    return mb.build(f"{name}_LOD{detail}")


# ============================================================================ port shed

def port_shed(detail=0, name="Minato_Precinct_PortShed"):
    L, D, H = 48.0, 26.0, 11.0
    hx, hz = D * 0.5, L * 0.5
    mb = MB()
    front = Face(mb, (hx, 0.0, -hz), (0, 0, 1), (1, 0, 0))      # +X: loading side
    back = Face(mb, (-hx, 0.0, hz), (0, 0, -1), (-1, 0, 0))
    gp = Face(mb, (hx, 0.0, hz), (-1, 0, 0), (0, 0, 1))
    gm = Face(mb, (-hx, 0.0, -hz), (1, 0, 0), (0, 0, -1))
    base_top = 0.6
    clere0, clere1 = H - 2.6, H - 0.8
    doors_front = [(8.0, 13.0), (21.5, 26.5), (35.0, 40.0)]
    doors_back = [(12.0, 17.0), (31.0, 36.0)]

    def elevation(f, length, doors, windows, office=False):
        ops = [{"a0": a0, "a1": a1, "b0": base_top if not office else base_top, "b1": 5.8, "kind": "door"}
               for a0, a1 in doors]
        ops += [{"a0": a0, "a1": a1, "b0": 2.4, "b1": 3.6, "kind": "win"} for a0, a1 in windows]
        if detail < 2:
            wall_with_openings(f, 0.0, length, base_top, clere0, ops, "shed_clad")
            for o in ops:
                if o["kind"] == "door":
                    opening_fill(f, o, 0.35, "shed_trim", "shed_door", detail=2)
                    if detail == 0:
                        f.box(o["a0"] - 0.22, o["a0"], base_top, o["b1"] + 0.2, 0.0, 0.18, "shed_trim")
                        f.box(o["a1"], o["a1"] + 0.22, base_top, o["b1"] + 0.2, 0.0, 0.18, "shed_trim")
                        f.box(o["a0"] - 0.22, o["a1"] + 0.22, o["b1"], o["b1"] + 0.75, 0.0, 0.55, "shed_trim")  # roller box
                        for bx in (o["a0"] + 0.35, o["a1"] - 0.35):                                              # bollards
                            f.box(bx - 0.14, bx + 0.14, 0.0, 1.1, 0.25, 0.53, "shed_hazard", skip="")
                else:
                    opening_fill(f, o, 0.12, "shed_trim", "shed_glass", frame_slot="shed_trim",
                                 mullions=(1, 0), detail=detail)
        else:
            f.rect(0.0, base_top, length, clere0, "shed_clad")
            for o in ops:
                f.rect(o["a0"], o["b0"], o["a1"], o["b1"], "shed_door" if o["kind"] == "door" else "shed_glass",
                       0.02, uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
        # translucent clerestory band + fascia
        f.rect(0.0, clere0, length, clere1, "shed_panel", 0.0,
               uvs=[(0, 0), (length / 3.0, 0), (length / 3.0, 1), (0, 1)])
        if detail < 2:
            for i in range(int(length / 3.0) + 1):
                a = min(length, i * 3.0)
                f.box(a - 0.06, a + 0.06, clere0, clere1, 0.0, 0.08, "shed_trim")
        f.box(-0.2, length + 0.2, clere1, H + 0.25, 0.0, 0.16, "shed_trim")
        f.box(-0.1, length + 0.1, BELOW, base_top, 0.0, 0.10, "shed_concrete")
        f.box(0.0, length, clere0 - 0.12, clere0, 0.0, 0.10, "shed_trim")
        # corner flashings
        f.box(-0.2, 0.18, base_top, H + 0.25, 0.0, 0.2, "shed_trim")
        f.box(length - 0.18, length + 0.2, base_top, H + 0.25, 0.0, 0.2, "shed_trim")

    elevation(front, L, doors_front, [(42.0, 44.2), (44.8, 47.0)])
    elevation(back, L, doors_back, [(3.0, 5.2), (6.0, 8.2), (40.0, 42.2)])
    elevation(gp, D, [(10.5, 15.5)], [(3.0, 5.4), (20.6, 23.0)])
    elevation(gm, D, [], [(4.0, 6.4), (8.0, 10.4), (15.6, 18.0), (19.6, 22.0)])
    # sign band on the loading side and the +Z gable
    if detail < 2:
        front.box(L * 0.5 - 9.0, L * 0.5 + 9.0, clere1 + 0.02, H + 0.22, 0.16, 0.24, "shed_sign",
                  front_uv=sign_uv(5))
        gp.box(D * 0.5 - 6.0, D * 0.5 + 6.0, clere1 + 0.02, H + 0.22, 0.16, 0.24, "shed_sign",
               front_uv=sign_uv(3))
    # low-pitch roof (5 deg) with a small overhang
    ov = 0.45
    rise = (hx + ov) * math.tan(math.radians(5.0))
    ey, ry = H + 0.25, H + 0.25 + rise
    z0, z1 = -hz - ov, hz + ov
    for s in (1, -1):
        ex = s * (hx + ov)
        mb.poly([(ex, ey, z0), (ex, ey, z1), (0.0, ry, z1), (0.0, ry, z0)],
                [(0, 0), ((z1 - z0) * 0.5, 0), ((z1 - z0) * 0.5, (hx + ov) * 0.5), (0, (hx + ov) * 0.5)],
                "shed_clad", (s * 0.087, 1.0, 0.0))
        mb.poly([(ex, ey - 0.02, z0), (ex, ey - 0.02, z1), (s * hx, ey - 0.02, z1), (s * hx, ey - 0.02, z0)],
                [(0, 0), (1, 0), (1, 1), (0, 1)], "shed_trim", (0, -1, 0))
        if detail < 2:
            for i in range(5):                                                          # roof lights
                zc = -hz + (i + 0.5) * L / 5
                t0, t1 = 0.25, 0.75
                p = lambda t, z: (ex * (1 - t), ey + rise * t + 0.03, z)  # noqa: E731
                mb.poly([p(t0, zc - 2.5), p(t0, zc + 2.5), p(t1, zc + 2.5), p(t1, zc - 2.5)],
                        [(0, 0), (1, 0), (1, 1), (0, 1)], "shed_panel", (s * 0.087, 1.0, 0.0))
    for g, zc in ((gp, hz), (gm, -hz)):
        g.poly([(0.0, ey, 0.0), (D, ey, 0.0), (hx, ry, 0.0)], "shed_trim", g.n)
    mb.box((0.0, ry + 0.05, 0.0), (0.5, 0.14, z1 - z0), "shed_trim", "xXYzZ")
    if detail < 2:
        # loading dock + bumpers + hazard nosing + canopy on the +X side
        dz0, dz1 = -hz + 6.0, hz - 7.5
        mb.box((hx + 1.6, 0.6 + BELOW * 0.5 - 0.3, 0.5 * (dz0 + dz1)), (3.2, 1.2 - BELOW + 0.6, dz1 - dz0),
               "shed_concrete", "XYzZ")
        mb.box((hx + 3.18, 1.17, 0.5 * (dz0 + dz1)), (0.06, 0.12, dz1 - dz0), "shed_hazard", "XY")
        for z in np.arange(dz0 + 1.5, dz1, 3.0):
            mb.box((hx + 3.28, 0.75, float(z)), (0.2, 0.5, 0.35), "shed_dark", "XYyzZ")
        cy = 6.9
        mb.box((hx + 2.2, cy, 0.5 * (dz0 + dz1)), (4.4, 0.18, dz1 - dz0 + 1.0), "shed_trim", "all")
        if detail == 0:
            for z in np.arange(dz0, dz1 + 0.1, 6.0):
                mb.box((hx + 2.2, cy - 0.3, float(z)), (4.4, 0.36, 0.2), "shed_dark", "xXyzZ")   # cantilever beams
            for z in (-hz + 0.35, hz - 0.35):                                              # downpipes
                for s in (1, -1):
                    mb.box((s * (hx + 0.3), H * 0.5, z), (0.16, H, 0.16), "shed_trim", "xXzZ")
    return mb.build(f"{name}_LOD{detail}")


# ============================================================================ precinct totem

def totem(detail=0):
    """MINATO PORT monument: a brick pier with stone bands, a sign board both ways, a lantern."""
    mb = MB()
    fp = Face(mb, (2.6, 0.0, 0.6), (-1, 0, 0), (0, 0, 1))
    fm = Face(mb, (-2.6, 0.0, -0.6), (1, 0, 0), (0, 0, -1))
    fx = Face(mb, (2.6, 0.0, -0.6), (0, 0, 1), (1, 0, 0))
    fxm = Face(mb, (-2.6, 0.0, 0.6), (0, 0, -1), (-1, 0, 0))
    for f, w in ((fp, 5.2), (fm, 5.2), (fx, 1.2), (fxm, 1.2)):
        f.rect(0.0, 0.6, w, 4.2, "prec_brick")
        f.box(-0.1, w + 0.1, BELOW, 0.6, 0.0, 0.16, "prec_stone")
        f.box(-0.1, w + 0.1, 4.2, 4.5, 0.0, 0.18, "prec_stone")
        f.box(-0.18, w + 0.18, 4.5, 4.7, 0.0, 0.26, "prec_stone")
        f.box(-0.05, w + 0.05, 2.0, 2.12, 0.0, 0.06, "prec_stone")
    mb.box((0.0, 4.72, 0.0), (5.6, 0.04, 1.72), "prec_stone", "Y")
    for f, row in ((fp, 2), (fm, 7)):
        f.box(0.35, 4.85, 2.35, 3.85, 0.0, 0.10, "prec_sign", front_uv=sign_uv(row))
    for s in (-1, 1):
        x = s * 2.0
        mb.box((x, 5.2, 0.0), (0.18, 1.0, 0.18), "prec_iron", "xXzZ")
        mb.box((x, 5.95, 0.0), (0.5, 0.6, 0.5), "prec_glass_warm", "xXzZ")
        mb.box((x, 6.32, 0.0), (0.66, 0.12, 0.66), "prec_iron", "xXyYzZ")
    return mb.build(f"Minato_Precinct_Totem_LOD{detail}")


# ============================================================================ export

def export(objs, filename):
    tris = tri_count(objs[0])
    M.export_fbx(objs, filename)
    print(f"[precinct] {filename:<40s} LOD0 {tris:>7,} tris  (+{len(objs) - 1} LODs)")
    for o in objs:
        M.stat(o)
    return tris


CATALOG = {
    "warehouse1": lambda: ([brick_warehouse(76.0, 12, 0, d, "Minato_Precinct_BrickWarehouseA") for d in (0, 1, 2)],
                           "Minato_Precinct_BrickWarehouseA.fbx"),
    "warehouse2": lambda: ([brick_warehouse(57.0, 9, 1, d, "Minato_Precinct_BrickWarehouseB") for d in (0, 1, 2)],
                           "Minato_Precinct_BrickWarehouseB.fbx"),
    "shed":       lambda: ([port_shed(d) for d in (0, 1, 2)], "Minato_Precinct_PortShed.fbx"),
    "totem":      lambda: ([totem(0)], "Minato_Precinct_Totem.fbx"),
}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or ["textures"] + list(CATALOG.keys())
    if "textures" in names:
        S.reset_scene()
        brick_texture()
        slate_texture()
        paver_texture()
        corrugated_texture()
        rollerdoor_texture()
        sign_texture()
    total = 0
    for n in names:
        if n == "textures":
            continue
        S.reset_scene()
        objs, fn = CATALOG[n]()
        total += export(objs, fn)
    print(f"[precinct] complete: {total:,} LOD0 tris total")


if __name__ == "__main__":
    main()

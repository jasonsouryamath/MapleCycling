"""
WP-C  Azora Highlands - Alpine Village kit (WINTER).   Copilot session 2, 2026-09-26.

Builds the bespoke village GLBs that AzoraHighlands.Village.cs stages at route m 2600 / 12600:

    Azora_Village_ChaletA / B / C   three Swiss chalet variants (stone plinth, rendered ground
                                    floor, Strickbau log upper storey with corner log-ends, carved
                                    balcony boards, deep eaves on purlins + knee braces, shutters,
                                    window grids, flower boxes with geraniums + fir sprigs, warm lit
                                    windows, explicit rounded roof-snow slabs and icicles)
    Azora_Village_Cafe / Bakery     shop-front chalets with signboards (3D lettering)
    Azora_Village_Church            white nave, stone tower, clock faces, belfry, copper spire
    Azora_Village_Fountain          octagonal stone basin, frozen surface, icicled spouts
    Azora_Village_SignArch          "AZORA" welcome arch, 13.2 m clear span (riding corridor +-3 m)
    Azora_Village_Umbrella          closed, snow-dusted cafe umbrella
    Azora_Village_FlagPole          Swiss flag on a white pole
    *_LOD1                          low-poly background versions of every building
    Azora_Village_*.png             timber / render / shingle / cobble / flag textures

Every vertex is authored in UNITY coordinates (x right, y up, +z = FRONT, the side that faces the
road) and converted with sakura_lib.u2b, so Unity stages with an identity transform. Origin = the
centre of the footprint at FLOOR level (top of the pavement at the front door). Plinths run down to
y = -1.2 m; Village.cs extends them further on steep ground.

Run:  tools\\blender-4.5.10-windows-x64\\blender.exe -b --factory-startup -P tools/blender/build_azora_village.py [-- --preview]

Snow: WP-E adds a global white top coat (_MR_SnowCover) to every CelLit surface, so only snow that
adds SILHOUETTE is modelled here (roof slabs with soft rounded edges, ridge rolls, icicles, caps).
"""

import math
import os
import random
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sakura_lib as S  # noqa: E402

ROOT = S.repo_root()
MODELS = os.path.join(ROOT, "Assets", "Environment", "AzoraHighlands", "Models")
PREVIEW_DIR = os.path.join(ROOT, "reference", "good_graphics", "azora_concept", "wpC_blender")
os.makedirs(MODELS, exist_ok=True)
# export_glb writes into blender_assets_dir(); point it at the Azora Models folder.
S.blender_assets_dir = lambda: MODELS

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = "--preview" in ARGS
STATS = "--stats" in ARGS          # print per-element tri estimates; skip export + textures
_PRIM_FUNCS = {"prim", "box", "beam", "prism", "cyl", "cone", "blob", "octa", "sheet", "text_mesh"}
# ---- LOD0 detail budget (azC2 perf pass; PROVISIONAL tuning - ChaletB was 41k tris, Cafe 28k)
BEVEL_MIN_DIM = 0.12      # boxes thinner than this (m) get no bevel
ICICLE_SPACING = 0.28     # was 0.20
GARLAND_STEP = 0.13       # was 0.09 (sprig spacing along a swag)
LOUVRE_PITCH = 0.34       # was 0.22 (shutter slat pitch)
FLOWER_PITCH = 0.21       # was 0.16 (geranium pitch in a window box)
BALUSTER_W = 0.18         # was 0.15 (carved balcony board width)

# ============================================================================ materials

# role -> (blender viewport colour, roughness, emission)   The Unity side re-materialises every
# slot by NAME (Azora_Village_<Role>) with CelLit / Unlit materials; these colours only drive the
# Blender preview and the glTF fallback.
ROLES = {
    "Stone":      (0.55, 0.54, 0.50),
    "Render":     (0.86, 0.83, 0.76),
    "Timber":     (0.46, 0.29, 0.16),
    "TimberDark": (0.22, 0.13, 0.08),
    "Roof":       (0.26, 0.21, 0.19),
    "Snow":       (0.95, 0.97, 1.00),
    "Ice":        (0.74, 0.87, 0.95),
    "Glow":       (1.00, 0.74, 0.40),
    "GlassDark":  (0.10, 0.13, 0.18),
    "Trim":       (0.93, 0.90, 0.82),
    "Shutter":    (0.17, 0.35, 0.23),
    "ShutterRed": (0.62, 0.13, 0.12),
    "Geranium":   (0.86, 0.10, 0.13),
    "Garland":    (0.10, 0.24, 0.14),
    "Metal":      (0.12, 0.12, 0.13),
    "Gold":       (0.86, 0.66, 0.26),
    "Copper":     (0.30, 0.52, 0.45),
    "Fabric":     (0.74, 0.11, 0.13),
    "FabricBlue": (0.15, 0.29, 0.56),
    "SignRed":    (0.78, 0.08, 0.10),
    "SignWhite":  (0.97, 0.97, 0.95),
    "Belfry":     (0.05, 0.05, 0.06),
    "Flag":       (0.80, 0.10, 0.10),
}
# Metres of world covered by one texture tile (box-projected UVs); 1 for untextured roles.
UV_TILE = {"Stone": 2.0, "Render": 3.0, "Timber": 2.0, "TimberDark": 2.0, "Roof": 2.0}

_MATS = {}


def material(role):
    if role in _MATS:
        return _MATS[role]
    col = ROLES[role]
    emis = (col[0], col[1], col[2], 1.0) if role == "Glow" else None
    m = S.pbr_material(f"Azora_Village_{role}", base_color=(col[0], col[1], col[2], 1.0),
                       roughness=0.2 if role in ("Ice", "Gold", "Copper") else 0.8,
                       metallic=0.6 if role in ("Gold", "Metal") else 0.0,
                       emission=emis, emission_strength=4.0 if emis else 0.0)
    m.diffuse_color = (col[0], col[1], col[2], 1.0)
    _MATS[role] = m
    return m


# ============================================================================ kit

class Kit:
    """Collects closed primitives per (role, bevel) group in UNITY coordinates, then builds one
    multi-material Blender object. Each primitive keeps its own vertices, so bmesh's flood-fill
    normal recalculation orients every primitive (an island) outward independently."""

    def __init__(self, lod=0):
        self.groups = {}
        self.xf = Matrix.Identity(4)
        self.stack = []
        self.lod = lod
        self.stats = {}
        self.callers = {}

    # ---- frames
    def push(self, m):
        self.stack.append(self.xf)
        self.xf = self.xf @ m

    def pop(self):
        self.xf = self.stack.pop()

    def _grp(self, role, bev):
        if self.lod:
            bev = (0.0, 1)  # background LOD: no bevels
        return self.groups.setdefault((role, bev), [])

    def prim(self, role, verts, faces, bev=(0.0, 1), closed=True, hint=None):
        vv = [self.xf @ Vector(v) for v in verts]
        self._grp(role, bev).append((vv, faces, closed, hint))
        if STATS:
            f = sys._getframe(1)
            while f is not None and f.f_code.co_name in _PRIM_FUNCS:
                f = f.f_back
            who = f.f_code.co_name if f is not None else "?"
            nt = sum(len(fc) - 2 for fc in faces)
            w = bev[0] if not self.lod else 0.0
            if w > 0:
                nt = nt * (4 if bev[1] <= 1 else 3 + bev[1] * 2)  # rough bevel multiplier
            self.callers[who] = self.callers.get(who, 0) + nt

    # ---- primitives (all in the current frame, Unity coords)
    def box(self, role, c, size, rot=None, bev=0.015, seg=1):
        hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
        # perf (azC2): a seg-1 bevel turns a 12-tri box into ~44 tris. On thin trim (window frames,
        # glazing bars, quoins, rafters, shutters) it is invisible at riding distance, so only
        # boxes whose thinnest side is >= BEVEL_MIN_DIM keep it.
        if bev > 0 and min(size) < BEVEL_MIN_DIM:
            bev = 0.0
        R = rot if rot is not None else Matrix.Identity(3)
        cv = Vector(c)
        vs = []
        for i in range(8):
            p = Vector(((hx if i & 1 else -hx), (hy if i & 2 else -hy), (hz if i & 4 else -hz)))
            vs.append(cv + R @ p)
        faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        self.prim(role, vs, faces, (bev, seg) if bev > 0 else (0.0, 1))

    def beam(self, role, a, b, w, h, bev=0.01, up=(0, 1, 0)):
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        if L < 1e-5:
            return
        x = d.normalized()
        u = Vector(up)
        if abs(x.dot(u)) > 0.95:
            u = Vector((1, 0, 0)) if abs(x.x) < 0.9 else Vector((0, 0, 1))
        z = x.cross(u).normalized()
        y = z.cross(x).normalized()
        R = Matrix((x, y, z)).transposed()
        self.box(role, (a + b) * 0.5, (L, h, w), rot=R, bev=bev)

    def prism(self, role, poly, z0, z1, bev=0.0, seg=1):
        """Extrude a 2-D polygon (x, y) along z from z0 to z1 (convex or not)."""
        n = len(poly)
        vs = [(p[0], p[1], z0) for p in poly] + [(p[0], p[1], z1) for p in poly]
        faces = [tuple(range(n))[::-1], tuple(range(n, 2 * n))]
        for i in range(n):
            j = (i + 1) % n
            faces.append((i, j, n + j, n + i))
        self.prim(role, vs, faces, (bev, seg) if bev > 0 else (0.0, 1))

    def cyl(self, role, c, r0, r1, h, n=10, bev=0.0, axis=None):
        """Frustum from c (base) up h; axis = 3x3 rotation (default y-up)."""
        R = axis if axis is not None else Matrix.Identity(3)
        cv = Vector(c)
        vs = []
        for k in range(2):
            r = r0 if k == 0 else r1
            for i in range(n):
                a = i / n * math.tau
                vs.append(cv + R @ Vector((math.cos(a) * r, h * k, math.sin(a) * r)))
        faces = [tuple(range(n)), tuple(range(n, 2 * n))[::-1]]
        for i in range(n):
            j = (i + 1) % n
            faces.append((i, n + i, n + j, j))
        self.prim(role, vs, faces, (bev, 1) if bev > 0 else (0.0, 1))

    def cone(self, role, c, r, h, n=6, axis=None):
        """Cone with base at c and apex at c + h*up (h < 0 hangs down)."""
        R = axis if axis is not None else Matrix.Identity(3)
        cv = Vector(c)
        vs = [cv + R @ Vector((math.cos(i / n * math.tau) * r, 0, math.sin(i / n * math.tau) * r)) for i in range(n)]
        vs.append(cv + R @ Vector((0, h, 0)))
        faces = [tuple(range(n))] + [(i, (i + 1) % n, n) for i in range(n)]
        self.prim(role, vs, faces)

    def blob(self, role, c, r, sy=1.0, seg=6, rings=4, rot=0.0):
        cv = Vector(c)
        vs = [cv + Vector((0, -r * sy, 0))]
        for j in range(1, rings):
            ph = -math.pi / 2 + j / rings * math.pi
            for i in range(seg):
                a = i / seg * math.tau + rot
                vs.append(cv + Vector((math.cos(a) * math.cos(ph) * r, math.sin(ph) * r * sy,
                                       math.sin(a) * math.cos(ph) * r)))
        vs.append(cv + Vector((0, r * sy, 0)))
        top = len(vs) - 1
        faces = [(0, 1 + (i + 1) % seg, 1 + i) for i in range(seg)]
        for j in range(rings - 2):
            b0, b1 = 1 + j * seg, 1 + (j + 1) * seg
            for i in range(seg):
                i2 = (i + 1) % seg
                faces.append((b0 + i, b0 + i2, b1 + i2, b1 + i))
        last = 1 + (rings - 2) * seg
        faces += [(last + i, last + (i + 1) % seg, top) for i in range(seg)]
        self.prim(role, vs, faces)

    def octa(self, role, c, r, sy=1.0, rng=None):
        cv = Vector(c)
        jit = (lambda: 1.0 + (rng.random() - 0.5) * 0.5) if rng else (lambda: 1.0)
        vs = [cv + Vector((r * jit(), 0, 0)), cv + Vector((-r * jit(), 0, 0)),
              cv + Vector((0, r * sy * jit(), 0)), cv + Vector((0, -r * sy * jit(), 0)),
              cv + Vector((0, 0, r * jit())), cv + Vector((0, 0, -r * jit()))]
        faces = [(0, 2, 4), (4, 2, 1), (1, 2, 5), (5, 2, 0), (0, 4, 3), (4, 1, 3), (1, 5, 3), (5, 0, 3)]
        self.prim(role, vs, faces)

    def sheet(self, role, verts, faces, hint):
        """Open surface with an explicit outward hint (Unity-space vector) - never recalculated."""
        self.prim(role, verts, faces, closed=False, hint=hint)

    # ---- build
    def build(self, name):
        objs = []
        for (role, bev), prims in self.groups.items():
            bm = bmesh.new()
            closed_faces = []
            sheet_faces = []
            for vv, faces, closed, hint in prims:
                bv = [bm.verts.new(S.u2b(v.x, v.y, v.z)) for v in vv]
                for f in faces:
                    try:
                        bf = bm.faces.new([bv[i] for i in f])
                    except ValueError:
                        continue
                    (closed_faces if closed else sheet_faces).append((bf, hint))
            bm.normal_update()
            if closed_faces:
                bmesh.ops.recalc_face_normals(bm, faces=[f for f, _ in closed_faces])
            for bf, hint in sheet_faces:
                hb = Vector(S.u2b(*hint))
                if bf.normal.dot(hb) < 0:
                    bf.normal_flip()
            ngons = [f for f in bm.faces if len(f.verts) > 4]
            if ngons:
                bmesh.ops.triangulate(bm, faces=ngons)
            me = bpy.data.meshes.new(f"{name}_{role}")
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(f"{name}_{role}", me)
            S.link(ob)
            ob.data.materials.append(material(role))
            width, seg = bev
            if width > 0:
                m = ob.modifiers.new("Bevel", 'BEVEL')
                m.width = width
                m.segments = seg
                m.limit_method = 'ANGLE'
                m.angle_limit = math.radians(30)
                m.use_clamp_overlap = True
                S.apply_modifiers(ob)
            smooth = role in ("Snow", "Ice", "Geranium")
            for p in ob.data.polygons:
                p.use_smooth = smooth
            box_uv(ob, UV_TILE.get(role, 1.0))
            objs.append(ob)
            ntri = sum(len(p.vertices) - 2 for p in ob.data.polygons)
            self.stats[role] = self.stats.get(role, 0) + ntri
        print(f"[azora-village] {name} tris by role: " +
              ", ".join(f"{r}={t}" for r, t in sorted(self.stats.items(), key=lambda kv: -kv[1])))
        if STATS:
            print(f"[azora-village] {name} ~tris by element: " +
                  ", ".join(f"{r}={t}" for r, t in sorted(self.callers.items(), key=lambda kv: -kv[1])))
        ob = S.join(objs, name)
        return ob


def box_uv(ob, tile):
    """Per-loop box projection in metres / tile (Blender axes: z is Unity up)."""
    me = ob.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers[0].data
    inv = 1.0 / tile
    for p in me.polygons:
        n = p.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if ax == 2:
                uv[li].uv = (co.x * inv, co.y * inv)
            elif ax == 0:
                uv[li].uv = (co.y * inv, co.z * inv)
            else:
                uv[li].uv = (co.x * inv, co.z * inv)


def Ry(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'Y')


def Rz(rad):
    return Matrix.Rotation(rad, 3, 'Z')


def Rx(rad):
    return Matrix.Rotation(rad, 3, 'X')


def frame(pos, yaw_deg=0.0):
    return Matrix.Translation(Vector(pos)) @ Matrix.Rotation(math.radians(yaw_deg), 4, 'Y')


# ============================================================================ elements

def window(k, rng, w, h, lit=True, shutter="Shutter", flowers=True, frame_role="Trim"):
    """A window on the wall plane z = 0 of the CURRENT frame, facing +z, centred at x = 0 with its
    sill at y = 0. Glass, bevelled frame, glazing bars, sill, louvred shutters, flower box."""
    glass = "Glow" if lit else "GlassDark"
    k.box(glass, (0, h * 0.5, 0.005), (w, h, 0.05), bev=0)
    if k.lod:
        if shutter:
            for s in (-1, 1):
                k.box(shutter, (s * (w * 0.75 + 0.06), h * 0.5, 0.03), (w * 0.5, h, 0.06), bev=0)
        return
    fw = 0.075
    k.box(frame_role, (0, h + fw * 0.5, 0.045), (w + 2 * fw, fw, 0.09))
    k.box(frame_role, (0, -fw * 0.5 + 0.02, 0.045), (w + 2 * fw, fw, 0.09))
    for s in (-1, 1):
        k.box(frame_role, (s * (w + fw) * 0.5, h * 0.5, 0.045), (fw, h, 0.09))
    # glazing bars: one vertical, one transom at 60 %, a second transom on tall windows
    k.box(frame_role, (0, h * 0.5, 0.04), (0.035, h, 0.05), bev=0.006)
    k.box(frame_role, (0, h * 0.62, 0.04), (w, 0.035, 0.05), bev=0.006)
    if h > 1.2:
        k.box(frame_role, (0, h * 0.3, 0.04), (w, 0.03, 0.05), bev=0.006)
    k.box("Trim" if frame_role != "Trim" else "TimberDark", (0, -0.03, 0.09), (w + 0.26, 0.06, 0.18))  # sill
    if shutter:
        sw = w * 0.5
        for s in (-1, 1):
            cx = s * (w * 0.5 + fw + sw * 0.5 + 0.02)
            k.box(shutter, (cx, h * 0.5, 0.035), (sw, h + 0.02, 0.05), bev=0.008)
            n = max(2, int(h / LOUVRE_PITCH))
            for i in range(n):
                y = (i + 0.5) / n * h
                k.box(shutter, (cx, y, 0.068), (sw - 0.07, 0.07, 0.025), bev=0)
            k.box("Metal", (cx - s * sw * 0.2, h * 0.2, 0.066), (sw * 0.55, 0.03, 0.02), bev=0)  # strap hinge
    if flowers:
        flower_box(k, rng, w + 0.2)


def flower_box(k, rng, bw):
    """Winter window box under the sill: timber trough, red geraniums in front, fir sprigs and a
    soft snow cap behind them."""
    k.box("TimberDark", (0, -0.2, 0.2), (bw, 0.2, 0.24))
    if k.lod:
        return
    k.box("Timber", (0, -0.2, 0.325), (bw + 0.04, 0.05, 0.02), bev=0)  # face board
    n = max(3, int(bw / FLOWER_PITCH))
    for i in range(n):
        x = (i + 0.5) / n * bw - bw * 0.5
        if rng.random() < 0.85:
            k.blob("Geranium", (x + (rng.random() - 0.5) * 0.04, -0.07 + rng.random() * 0.04, 0.27),
                   0.065 + rng.random() * 0.025, sy=0.8, seg=5, rings=3)
        if i % 2 == 0:
            k.octa("Garland", (x + 0.05, -0.08, 0.14), 0.11, sy=0.55, rng=rng)
    k.box("Snow", (0, -0.085, 0.12), (bw - 0.02, 0.06, 0.14), bev=0.025, seg=2)


def garland(k, rng, a, b, sag=0.35, step=None, r=0.085, bows=True):
    """Evergreen swag between two points, hanging in a catenary-ish curve, red bows at the ends."""
    step = GARLAND_STEP if step is None else step
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    if k.lod or L < 0.05:
        return
    n = max(3, int(L / step))
    for i in range(n + 1):
        t = i / n
        p = a.lerp(b, t) - Vector((0, sag * 4 * t * (1 - t), 0))
        k.octa("Garland", p + Vector(((rng.random() - 0.5) * 0.05, (rng.random() - 0.5) * 0.05,
                                      (rng.random() - 0.5) * 0.05)), r, sy=0.8, rng=rng)
    if bows:
        for p in (a, b):
            k.blob("Geranium", p + Vector((0, -0.02, 0.05)), 0.07, sy=0.7, seg=5, rings=3)


def icicles(k, rng, a, b, spacing=None, lmin=0.12, lmax=0.65):
    """Row of icicles hanging from the edge line a->b."""
    if k.lod:
        return
    spacing = ICICLE_SPACING if spacing is None else spacing * (ICICLE_SPACING / 0.2)
    a, b = Vector(a), Vector(b)
    L = (b - a).length
    n = int(L / spacing)
    for i in range(n):
        if rng.random() < 0.35:
            continue
        t = (i + 0.5 + (rng.random() - 0.5) * 0.4) / n
        p = a.lerp(b, t)
        ln = lmin + (lmax - lmin) * (rng.random() ** 2.2)
        if rng.random() < 0.08:
            ln *= 1.6
        r = 0.022 + ln * 0.05
        k.cone("Ice", p + Vector((0, 0.02, 0)), r, -ln, n=5)


def gable_roof(k, rng, W, D, y_eave, pitch_deg, ov_side, ov_front, thick=0.2, snow=0.34,
               teeth=True, purlins=True, braces=True, rafters=True, snow_role="Snow"):
    """Gable roof with the ridge along z (gable faces +z). Roof-plane bottom passes through
    (x = +-W, y_eave). Returns (y_ridge_bottom, eave edge height)."""
    p = math.radians(pitch_deg)
    tp, cp, sp = math.tan(p), math.cos(p), math.sin(p)
    y_top = y_eave + tp * W
    Ws = W + ov_side
    L = Ws / cp
    Z = 2 * (D + ov_front)
    y_edge = y_top - tp * Ws
    for s in (-1, 1):
        R = Rz(-s * p)
        nrm = Vector((s * sp, cp, 0))
        mid = Vector((s * Ws * 0.5, y_top - tp * Ws * 0.5, 0))
        k.box("Roof", mid + nrm * (thick * 0.5), (L + 0.02, thick, Z), rot=R, bev=0.02)
        # snow slab: soft, rounded, a little lip over the eave, stops short of the gable edge
        if snow > 0:
            k.box(snow_role, mid + nrm * (thick + snow * 0.42) + Vector((s * cp * 0.06, -sp * 0.06, 0)),
                  (L + 0.05, snow, Z - 0.12), rot=R, bev=min(0.14, snow * 0.45), seg=3)
        # eave fascia board
        edge = Vector((s * Ws, y_edge, 0))
        k.box("TimberDark", edge + nrm * (thick * 0.5) + Vector((s * 0.02, 0, 0)), (0.06, thick + 0.1, Z + 0.02), rot=R)
        # icicles along the eave
        ie = edge + Vector((-s * 0.04, -0.03, 0))
        icicles(k, rng, ie + Vector((0, 0, -Z * 0.5 + 0.1)), ie + Vector((0, 0, Z * 0.5 - 0.1)))
        # rafter tails under the eave
        if rafters and not k.lod:
            nr = int(Z / 0.75)
            for i in range(nr + 1):
                z = -Z * 0.5 + 0.2 + i * (Z - 0.4) / nr
                a = Vector((s * (W - 0.1), y_eave - 0.02, z)) - Vector((0, 0.08, 0))
                b = Vector((s * (Ws - 0.08), y_edge - 0.02, z)) - Vector((0, 0.08, 0))
                k.beam("Timber", a, b, 0.1, 0.14)
        # verge (barge) boards on both gables, with fretwork teeth
        for zs in (-1, 1):
            zf = zs * (Z * 0.5 + 0.03)
            a = Vector((0, y_top + 0.02, zf))
            b = Vector((s * (Ws + 0.02), y_edge - 0.02, zf))
            k.beam("TimberDark", a - Vector((0, 0.12, 0)), b - Vector((0, 0.12, 0)), 0.07, 0.3)
            if teeth and not k.lod:
                nt = int((b - a).length / 0.26)
                for i in range(1, nt):
                    q = a.lerp(b, i / nt) - Vector((0, 0.27, 0))
                    k.cone("Timber", q, 0.06, -0.14, n=4)
    # ridge snow roll + ridge board
    k.box("TimberDark", (0, y_top + thick / cp + 0.02, 0), (0.22, 0.12, Z + 0.06))
    if snow > 0:
        k.box(snow_role, (0, y_top + thick / cp + snow * 0.55, 0), (0.7, snow * 0.7, Z - 0.1), bev=0.13, seg=3)
    # purlins sticking out of the gables + knee braces
    if purlins:
        for x in (-W, -W * 0.5, 0.0, W * 0.5, W):
            y = y_top - tp * abs(x) - 0.13
            k.box("Timber", (x, y, 0), (0.18, 0.22, Z - 0.1))
    if braces and not k.lod:
        for zs in (-1, 1):
            for x in (-W, W):
                a = Vector((x, y_eave - 1.0, zs * (D + 0.04)))
                b = Vector((x, y_eave - 0.14, zs * (D + ov_front - 0.35)))
                k.beam("Timber", a, b, 0.14, 0.14)
    return y_top, y_edge


def balcony(k, rng, x0, x1, y, zwall, depth, carved=True, garlands=True, snow=True):
    """Front balcony on the wall plane z = zwall, floor at y, projecting depth along +z."""
    zf = zwall + depth
    L = x1 - x0
    xc = (x0 + x1) * 0.5
    k.box("Timber", (xc, y - 0.06, zwall + depth * 0.5), (L, 0.12, depth))
    k.box("TimberDark", (xc, y - 0.16, zf - 0.06), (L, 0.1, 0.1))
    if k.lod:
        k.box("Timber", (xc, y + 0.45, zf - 0.03), (L, 0.9, 0.05), bev=0)
        return
    # brackets
    nb = max(2, int(L / 1.6))
    for i in range(nb + 1):
        x = x0 + 0.1 + i * (L - 0.2) / nb
        k.beam("Timber", (x, y - 0.8, zwall + 0.02), (x, y - 0.1, zf - 0.12), 0.1, 0.12)
    # carved balustrade: waisted boards whose shaped gaps make the classic Swiss fretwork
    bw, h = BALUSTER_W, 0.82
    # carved profile, 9 points per side (was 10; the bulge's flat top is merged to one point)
    prof = [(0.0, 0.075), (0.12, 0.075), (0.18, 0.042), (0.29, 0.042), (0.42, 0.074),
            (0.55, 0.042), (0.66, 0.042), (0.72, 0.075), (h, 0.075)]
    poly = [(hw, yy) for yy, hw in prof] + [(-hw, yy) for yy, hw in reversed(prof)]
    n = int(L / bw)
    for i in range(n):
        x = x0 + (i + 0.5) * L / n
        k.push(Matrix.Translation(Vector((x, y + 0.08, 0))))
        if carved:
            k.prism("Timber", poly, zf - 0.05, zf - 0.02)
        else:
            k.box("Timber", (0, h * 0.5, zf - 0.035), (bw - 0.02, h, 0.03), bev=0)
        k.pop()
    k.box("TimberDark", (xc, y + 0.08 + h + 0.04, zf - 0.035), (L + 0.08, 0.08, 0.12))  # hand rail
    k.box("TimberDark", (xc, y + 0.06, zf - 0.035), (L, 0.05, 0.08))
    for x in (x0 + 0.04, x1 - 0.04):
        k.box("TimberDark", (x, y + 0.5, zf - 0.035), (0.08, 1.0, 0.1))
    if snow:
        k.box("Snow", (xc, y + h + 0.2, zf - 0.035), (L + 0.02, 0.07, 0.13), bev=0.03, seg=2)
        k.box("Snow", (xc, y + 0.04, zwall + depth * 0.45), (L - 0.3, 0.05, depth * 0.6), bev=0.02, seg=2)
    if garlands:
        ns = max(1, int(L / 1.4))
        for i in range(ns):
            a = Vector((x0 + i * L / ns + 0.05, y + h + 0.06, zf + 0.04))
            b = Vector((x0 + (i + 1) * L / ns - 0.05, y + h + 0.06, zf + 0.04))
            garland(k, rng, a, b, sag=0.28)
    # a row of geranium boxes on the rail
    for i in range(max(1, int(L / 2.2))):
        x = x0 + (i + 0.5) * L / max(1, int(L / 2.2))
        k.push(Matrix.Translation(Vector((x, y + h + 0.3, zf + 0.08))))
        flower_box(k, rng, 0.9)
        k.pop()


def log_ends(k, W, D, y0, y1, reach=0.2):
    """Strickbau corner joints: log ends protruding past each corner, alternating per course."""
    if k.lod:
        return
    n = int((y1 - y0) / 0.21)
    for i in range(n):
        y = y0 + (i + 0.5) * (y1 - y0) / n
        for sx in (-1, 1):
            for sz in (-1, 1):
                if i % 2 == 0:
                    k.box("Timber", (sx * (W + reach * 0.5), y, sz * (D - 0.09)), (reach + 0.1, 0.19, 0.18), bev=0)
                else:
                    k.box("Timber", (sx * (W - 0.09), y, sz * (D + reach * 0.5)), (0.18, 0.19, reach + 0.1), bev=0)


def door(k, rng, x, zwall, w=1.0, h=2.15, role="TimberDark", glazed=False, canopy=True, lantern=True):
    k.box("Timber", (x, h * 0.5 + 0.05, zwall + 0.03), (w + 0.22, h + 0.14, 0.08))
    k.box("Glow" if glazed else role, (x, h * 0.5, zwall + 0.06), (w, h, 0.05), bev=0)
    if not k.lod:
        if glazed:
            k.box("TimberDark", (x, h * 0.5, zwall + 0.09), (0.05, h, 0.03), bev=0)
            k.box("TimberDark", (x, h * 0.35, zwall + 0.09), (w, 0.07, 0.03), bev=0)
        else:
            for yy in (0.55, 1.45):
                for sx in (-1, 1):
                    k.box(role, (x + sx * w * 0.23, yy, zwall + 0.09), (w * 0.36, 0.7, 0.03), bev=0.01)
            k.box("Gold", (x + w * 0.38, 1.05, zwall + 0.11), (0.05, 0.14, 0.03), bev=0)
        garland(k, rng, (x - w * 0.62, h + 0.12, zwall + 0.12), (x + w * 0.62, h + 0.12, zwall + 0.12), sag=0.22)
    k.box("Stone", (x, -0.1, zwall + 0.35), (w + 0.6, 0.2, 0.6))  # step
    if canopy:
        k.box("Roof", (x, h + 0.42, zwall + 0.45), (w + 0.8, 0.08, 0.9), rot=Rx(-0.25))
        k.box("Snow", (x, h + 0.53, zwall + 0.47), (w + 0.75, 0.14, 0.86), rot=Rx(-0.25), bev=0.05, seg=2)
        icicles(k, rng, (x - w * 0.45, h + 0.3, zwall + 0.88), (x + w * 0.45, h + 0.3, zwall + 0.88), 0.14, 0.08, 0.35)
    if lantern:
        lx = x + w * 0.5 + 0.35
        k.box("Metal", (lx, h - 0.25, zwall + 0.12), (0.04, 0.04, 0.2), bev=0)
        k.box("Metal", (lx, h - 0.45, zwall + 0.24), (0.2, 0.04, 0.2), bev=0)
        k.box("Glow", (lx, h - 0.62, zwall + 0.24), (0.15, 0.3, 0.15), bev=0)
        k.cone("Metal", (lx, h - 0.47, zwall + 0.24), 0.14, 0.14, n=4)


def signboard(k, text, x, y, zwall, width, height, board="TimberDark", letters="Gold", depth=0.05):
    k.box(board, (x, y, zwall + 0.04), (width, height, 0.08), bev=0.02)
    k.box("Timber", (x, y, zwall + 0.02), (width + 0.1, height + 0.1, 0.04))
    text_mesh(k, letters, text, (x, y, zwall + 0.08), height * 0.62, depth, max_w=width * 0.9)


_TEXT_CACHE = {}


def text_mesh(k, role, text, centre, cap_h, depth, max_w=None, facing=1):
    """3-D extruded lettering centred at `centre`, reading correctly for a viewer on the +z side
    (facing = 1) or the -z side (facing = -1) of the current frame."""
    if k.lod and len(text) < 3:
        return
    key = text
    if key not in _TEXT_CACHE:
        cu = bpy.data.curves.new("txt_" + text, 'FONT')
        cu.body = text
        cu.align_x = 'CENTER'
        cu.align_y = 'CENTER'
        cu.size = 1.0
        cu.extrude = 0.5
        cu.offset = 0.035   # heavier stroke - Bfont Regular is thin at road distance
        cu.resolution_u = 3
        ob = bpy.data.objects.new("txt_" + text, cu)
        S.link(ob)
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        verts = [v.co.copy() for v in me.vertices]
        faces = [tuple(p.vertices) for p in me.polygons]
        bpy.data.objects.remove(ob)
        xs = [v.x for v in verts]
        ys = [v.y for v in verts]
        _TEXT_CACHE[key] = (verts, faces, (min(xs), max(xs), min(ys), max(ys)))
    verts, faces, (x0, x1, y0, y1) = _TEXT_CACHE[key]
    s = cap_h / max(1e-4, (y1 - y0))
    if max_w is not None and (x1 - x0) * s > max_w:
        s = max_w / (x1 - x0)
    cx, cy = (x0 + x1) * 0.5, (y0 + y1) * 0.5
    zc = 0.5 * (0.5 + -0.5)  # font extrude is symmetric about 0 (+-0.5)
    out = []
    for v in verts:
        tx, ty, tz = (v.x - cx) * s, (v.y - cy) * s, (v.z - zc) / 1.0 * depth
        # Unity is left-handed: a viewer on +z looking toward -z has +x on their LEFT,
        # so lettering read from +z must run along -x.
        if facing > 0:
            out.append((centre[0] - tx, centre[1] + ty, centre[2] + depth * 0.5 + tz))
        else:
            out.append((centre[0] + tx, centre[1] + ty, centre[2] - depth * 0.5 - tz))
    k.prim(role, out, faces)


def swiss_cross(k, c, size, zface, facing=1):
    """Red square with a white cross, on the plane z = zface facing +z (or -z)."""
    x, y = c
    f = facing
    k.box("SignRed", (x, y, zface + f * 0.02), (size, size, 0.04), bev=0.01)
    a, b = size * 0.62, size * 0.2
    k.box("SignWhite", (x, y, zface + f * 0.045), (a, b, 0.02), bev=0)
    k.box("SignWhite", (x, y, zface + f * 0.045), (b, a, 0.02), bev=0)


def chimney(k, rng, x, z, y_base, y_top):
    k.box("Stone", (x, (y_base + y_top) * 0.5, z), (0.6, y_top - y_base, 0.6), bev=0.02)
    k.box("Stone", (x, y_top + 0.05, z), (0.74, 0.1, 0.74), bev=0.015)
    k.box("Metal", (x, y_top + 0.28, z), (0.5, 0.05, 0.5), bev=0)
    for sx in (-1, 1):
        k.box("Metal", (x + sx * 0.22, y_top + 0.18, z), (0.04, 0.2, 0.04), bev=0)
    if not k.lod:
        k.blob("Snow", (x, y_top + 0.35, z), 0.3, sy=0.35, seg=8, rings=4)


# ============================================================================ buildings

def chalet(name, lod, W, D, g, storeys, pitch, ov_side, ov_front, seed, shop=None, shutter="Shutter",
           render_ground=True, attic_balcony=False):
    """Swiss chalet. shop = None | ('CAFÉ', 'Fabric') | ('BÄCKEREI', 'FabricBlue')."""
    rng = random.Random(seed)
    k = Kit(lod)
    u_each = 2.65
    y_up0 = g + 0.12
    y_eave = y_up0 + u_each * storeys
    # ---- plinth + ground floor
    k.box("Stone", (0, -0.375, 0), (2 * W + 0.3, 1.65, 2 * D + 0.3), bev=0.03)
    k.box("Stone", (0, 0.47, 0), (2 * W + 0.36, 0.06, 2 * D + 0.36), bev=0.01)  # coping
    k.box("Render" if render_ground else "Timber", (0, (0.45 + g) * 0.5, 0), (2 * W, g - 0.45, 2 * D), bev=0.02)
    if render_ground and not lod:
        for sx in (-1, 1):
            for sz in (-1, 1):
                for q in range(int((g - 0.5) / 0.45)):
                    lng = q % 2 == 0
                    y = 0.72 + q * 0.45
                    k.box("Stone", (sx * (W - (0.3 if lng else 0.2)), y, sz * (D + 0.015)),
                          (0.6 if lng else 0.4, 0.4, 0.06), bev=0.012)
                    k.box("Stone", (sx * (W + 0.015), y, sz * (D - (0.2 if lng else 0.3))),
                          (0.06, 0.4, 0.4 if lng else 0.6), bev=0.012)
    k.box("TimberDark", (0, g + 0.02, 0), (2 * W + 0.2, 0.22, 2 * D + 0.2), bev=0.02)  # sill beam
    # ---- upper storeys (Strickbau)
    k.box("Timber", (0, (y_up0 + y_eave) * 0.5, 0), (2 * W + 0.06, y_eave - y_up0, 2 * D + 0.06), bev=0.02)
    for st in range(1, storeys):
        k.box("TimberDark", (0, y_up0 + st * u_each, 0), (2 * W + 0.14, 0.12, 2 * D + 0.14), bev=0.015)
    log_ends(k, W, D, y_up0, y_eave)
    # ---- gable (attic) prism
    p = math.radians(pitch)
    rise = math.tan(p) * (W + 0.03)
    k.prism("Timber", [(-W - 0.03, y_eave), (W + 0.03, y_eave), (0, y_eave + rise)], -D - 0.03, D + 0.03)
    # ---- roof, snow, icicles
    y_top, y_edge = gable_roof(k, rng, W, D, y_eave, pitch, ov_side, ov_front)
    chimney(k, rng, W * 0.45, -D * 0.35, y_eave, y_top + 0.9)

    # ---- front (+z) facade
    zf = D + 0.03
    if shop is None:
        door(k, rng, 0.0, D, lantern=True)
        nwin = 2 if W < 4.4 else 3
        xs = [(-W * 0.55), (W * 0.55)] if nwin == 2 else [-W * 0.62, W * 0.62]
        for i, x in enumerate(xs):
            k.push(Matrix.Translation(Vector((x, 0.95, D))))
            window(k, rng, 0.8, 1.05, lit=rng.random() < 0.75, shutter=shutter)
            k.pop()
    else:
        text, awning = shop
        # glazed shop front, glazed door, signboard, rolled-up awning (winter)
        sx0, sx1 = -W + 0.45, W - 1.7
        k.box("TimberDark", ((sx0 + sx1) * 0.5, 0.35, D + 0.06), (sx1 - sx0 + 0.2, 0.7, 0.12))
        k.box("Glow", ((sx0 + sx1) * 0.5, 1.45, D + 0.02), (sx1 - sx0, 1.5, 0.06), bev=0)
        if not lod:
            nm = int((sx1 - sx0) / 0.9)
            for i in range(nm + 1):
                x = sx0 + i * (sx1 - sx0) / nm
                k.box("TimberDark", (x, 1.45, D + 0.07), (0.07, 1.55, 0.06), bev=0.008)
            k.box("TimberDark", ((sx0 + sx1) * 0.5, 2.23, D + 0.07), (sx1 - sx0 + 0.14, 0.1, 0.08))
            k.box("TimberDark", ((sx0 + sx1) * 0.5, 1.85, D + 0.07), (sx1 - sx0, 0.05, 0.05), bev=0)
        door(k, rng, W - 0.95, D, w=0.95, glazed=True, canopy=False, lantern=False)
        signboard(k, text, (sx0 + sx1) * 0.5, g - 0.3, D, min(sx1 - sx0 + 0.4, 4.2), 0.55)
        # rolled awning cassette with a striped valance
        k.box(awning, ((sx0 + sx1) * 0.5, 2.48, D + 0.2), (sx1 - sx0 + 0.3, 0.2, 0.26), bev=0.03)
        if not lod:
            nv = int((sx1 - sx0 + 0.3) / 0.3)
            for i in range(nv):
                x = sx0 - 0.15 + (i + 0.5) * (sx1 - sx0 + 0.3) / nv
                k.box("SignWhite" if i % 2 else awning, (x, 2.32, D + 0.34), ((sx1 - sx0 + 0.3) / nv, 0.14, 0.02), bev=0)
            k.box("Snow", ((sx0 + sx1) * 0.5, 2.62, D + 0.2), (sx1 - sx0 + 0.25, 0.08, 0.22), bev=0.03, seg=2)
        if text.startswith("B"):
            pretzel_sign(k, rng, W + 0.1, g + 0.6, D + 0.1)
    # upper floors: windows + balcony on the first upper storey
    for st in range(storeys):
        y0 = y_up0 + st * u_each + 0.85
        nwin = 3 if W >= 4.4 else 2
        for i in range(nwin):
            x = (i - (nwin - 1) * 0.5) * (2 * W / nwin)
            k.push(Matrix.Translation(Vector((x, y0, zf))))
            window(k, rng, 0.75, 1.1, lit=rng.random() < 0.7, shutter=shutter, flowers=(st > 0 or not lod) and st > 0)
            k.pop()
        if st == 0:
            balcony(k, rng, -W - 0.3, W + 0.3, y_up0 + 0.05, zf, 1.05)
    # attic: small window pair in the gable, optional attic balcony
    ya = y_eave + 0.35
    for x in (-0.55, 0.55):
        k.push(Matrix.Translation(Vector((x, ya, zf))))
        window(k, rng, 0.45, 0.75, lit=rng.random() < 0.5, shutter=None, flowers=False, frame_role="TimberDark")
        k.pop()
    if attic_balcony:
        balcony(k, rng, -W * 0.55, W * 0.55, y_eave - 0.1, zf, 0.8, garlands=False)
    # decorative sun/date board under the ridge
    if not lod:
        k.box("TimberDark", (0, y_eave + rise * 0.72, zf + 0.02), (0.9, 0.3, 0.04))
        k.blob("Gold", (0, y_eave + rise * 0.72, zf + 0.05), 0.1, sy=1.0, seg=6, rings=3)

    # ---- side (+-x) facades: two windows per floor, garland under the eave
    for sx in (-1, 1):
        R = frame((sx * (W + 0.03), 0, 0), 90 * sx)
        # in this frame local +z points outward from the side wall
        k.push(R)
        for z in (-D * 0.45, D * 0.45):
            k.push(Matrix.Translation(Vector((z, 0.95, 0))))
            window(k, rng, 0.7, 0.95, lit=rng.random() < 0.6, shutter=shutter, flowers=rng.random() < 0.6)
            k.pop()
            for st in range(storeys):
                k.push(Matrix.Translation(Vector((z, y_up0 + st * u_each + 0.85, 0))))
                window(k, rng, 0.7, 1.0, lit=rng.random() < 0.6, shutter=shutter, flowers=rng.random() < 0.5)
                k.pop()
        k.pop()
    # back (-z): simple windows
    k.push(frame((0, 0, -D - 0.03), 180))
    for x in (-W * 0.5, W * 0.5):
        for st in range(storeys):
            k.push(Matrix.Translation(Vector((x, y_up0 + st * u_each + 0.85, 0))))
            window(k, rng, 0.7, 1.0, lit=rng.random() < 0.5, shutter=shutter, flowers=False)
            k.pop()
    k.pop()
    # firewood stack against one side wall (winter!)
    if not lod:
        for i in range(10):
            for j in range(4):
                y = 0.1 + j * 0.17
                z = -D * 0.8 + 0.08 + i * 0.176
                k.cyl("Timber", (-W - 0.35, y, z), 0.08, 0.08, 0.55, n=5,
                      axis=Matrix.Rotation(math.pi / 2, 3, 'Z'))
        k.box("Roof", (-W - 0.45, 0.95, -D * 0.8 + 0.9), (0.8, 0.06, 2.1), bev=0)
    return k.build(name)


def pretzel_sign(k, rng, x, y, z):
    """Bakery bracket sign with a gilded pretzel."""
    k.beam("Metal", (x - 0.1, y, z - 0.1), (x + 0.9, y, z - 0.1), 0.05, 0.05)
    k.beam("Metal", (x - 0.1, y - 0.5, z - 0.1), (x + 0.5, y, z - 0.1), 0.04, 0.04)
    cx, cy = x + 0.6, y - 0.5
    pts = []
    for i in range(33):
        t = i / 32 * math.tau
        px = 0.34 * math.sin(t)
        py = 0.16 * math.cos(t) + 0.14 * math.cos(2 * t)
        pts.append(Vector((cx + px, cy + py, z - 0.1)))
    for a, b in zip(pts[:-1], pts[1:]):
        k.beam("Gold", a, b, 0.07, 0.07, bev=0)
    k.beam("Metal", (cx, y, z - 0.1), (cx, cy + 0.3, z - 0.1), 0.02, 0.02, bev=0)


def church(name, lod):
    rng = random.Random(77)
    k = Kit(lod)
    # nave: long axis along x, the +z side faces the road
    NL, NW, H = 9.0, 5.0, 7.4
    k.box("Stone", (0, -0.4, 0), (2 * NL + 0.3, 1.8, 2 * NW + 0.3), bev=0.03)
    k.box("Render", (0, (0.5 + H) * 0.5, 0), (2 * NL, H - 0.5, 2 * NW), bev=0.03)
    k.box("Stone", (0, H - 0.1, 0), (2 * NL + 0.12, 0.2, 2 * NW + 0.12), bev=0.02)  # cornice
    # arched windows on both long sides
    arch = [(-0.55, 0.0), (0.55, 0.0), (0.55, 2.4)] + \
           [(0.55 * math.cos(a), 2.4 + 0.55 * math.sin(a)) for a in [i / 6 * math.pi for i in range(1, 6)]] + \
           [(-0.55, 2.4)]
    surround = [(-0.72, -0.15), (0.72, -0.15), (0.72, 2.4)] + \
               [(0.72 * math.cos(a), 2.4 + 0.72 * math.sin(a)) for a in [i / 6 * math.pi for i in range(1, 6)]] + \
               [(-0.72, 2.4)]
    for zs in (-1, 1):
        for x in (-6.0, -2.0, 2.0, 6.0):
            k.push(frame((x, 2.0, zs * NW), 0 if zs > 0 else 180))
            k.prism("Stone", surround, -0.02, 0.06)
            k.prism("Glow", arch, 0.0, 0.08)
            if not lod:
                k.box("Metal", (0, 1.5, 0.09), (0.03, 3.0, 0.02), bev=0)
                k.box("Metal", (0, 1.4, 0.09), (1.1, 0.03, 0.02), bev=0)
            k.pop()
    # rose window + door on the -x gable end (faces the approaching road direction as well)
    k.push(frame((-NL, 0, 0), -90))
    k.cyl("Stone", (0, 9.0, -0.02), 0.95, 0.95, 0.1, n=16, axis=Rx(math.pi / 2))
    k.cyl("Glow", (0, 9.0, 0.05), 0.75, 0.75, 0.04, n=16, axis=Rx(math.pi / 2))
    door(k, rng, 0.0, 0.0, w=1.6, h=2.9, canopy=True, lantern=False)
    k.pop()
    # gable prism + roof (ridge along x): build in a frame rotated 90 deg so gable_roof's z = world x
    k.push(frame((0, 0, 0), 90))
    rise = math.tan(math.radians(42)) * (NW + 0.02)
    k.prism("Render", [(-NW - 0.02, H), (NW + 0.02, H), (0, H + rise)], -NL - 0.02, NL - 0.02)
    gable_roof(k, rng, NW, NL, H, 42, 0.7, 0.6, thick=0.22, snow=0.4, teeth=False, purlins=False, braces=False)
    k.pop()
    # ---- tower at +x end
    TX, TH, TS = NL + 2.7, 20.0, 2.6
    k.box("Stone", (TX, -0.4 + (TH * 0.5 + 0.4) * 0.5, 0), (2 * TS + 0.25, TH * 0.5 + 1.2, 2 * TS + 0.25), bev=0.03)
    k.box("Render", (TX, TH * 0.5 + TH * 0.25, 0), (2 * TS, TH * 0.5, 2 * TS), bev=0.03)
    if not lod:
        for sx in (-1, 1):
            for sz in (-1, 1):
                for q in range(int(TH / 0.6)):
                    lng = q % 2 == 0
                    y = 0.3 + q * 0.6 + 0.25
                    k.box("Stone", (TX + sx * (TS - (0.35 if lng else 0.22)), y, sz * (TS + 0.02)),
                          (0.7 if lng else 0.44, 0.5, 0.06), bev=0.012)
                    k.box("Stone", (TX + sx * (TS + 0.02), y, sz * (TS - (0.22 if lng else 0.35))),
                          (0.06, 0.5, 0.44 if lng else 0.7), bev=0.012)
    # tower door toward the road
    k.push(Matrix.Translation(Vector((TX, 0, TS))))
    door(k, rng, 0.0, 0.0, w=1.3, h=2.6, canopy=False, lantern=True)
    k.pop()
    k.box("Stone", (TX, TH - 0.1, 0), (2 * TS + 0.35, 0.3, 2 * TS + 0.35), bev=0.03)  # cornice
    k.box("Snow", (TX, TH + 0.1, 0), (2 * TS + 0.3, 0.14, 2 * TS + 0.3), bev=0.05, seg=2)
    # clock faces + belfry openings on 4 sides
    for yaw in (0, 90, 180, 270):
        k.push(frame((TX, 0, 0), yaw))
        k.push(Matrix.Translation(Vector((0, 0, TS))))
        # clock
        k.cyl("TimberDark", (0, 13.2, -0.02), 1.0, 1.0, 0.1, n=20, axis=Rx(math.pi / 2))
        k.cyl("SignWhite", (0, 13.2, 0.07), 0.86, 0.86, 0.04, n=20, axis=Rx(math.pi / 2))
        if not lod:
            for h in range(12):
                a = h / 12 * math.tau
                k.box("Belfry", (math.sin(a) * 0.72, 13.2 + math.cos(a) * 0.72, 0.12), (0.06, 0.16, 0.02),
                      rot=Matrix.Rotation(-a, 3, 'Z'), bev=0)
            k.box("Gold", (0.18, 13.35, 0.14), (0.05, 0.45, 0.02), rot=Matrix.Rotation(-0.7, 3, 'Z'), bev=0)
            k.box("Gold", (-0.05, 13.5, 0.15), (0.04, 0.62, 0.02), rot=Matrix.Rotation(0.15, 3, 'Z'), bev=0)
        # belfry pair of arched openings with louvres
        for x in (-0.95, 0.95):
            k.push(Matrix.Translation(Vector((x, 15.8, 0))))
            op = [(-0.5, 0.0), (0.5, 0.0), (0.5, 1.9)] + \
                 [(0.5 * math.cos(a), 1.9 + 0.5 * math.sin(a)) for a in [i / 5 * math.pi for i in range(1, 5)]] + \
                 [(-0.5, 1.9)]
            k.prism("Belfry", op, -0.01, 0.03)
            if not lod:
                for j in range(6):
                    k.box("Timber", (0, 0.25 + j * 0.3, 0.04), (0.98, 0.06, 0.04), rot=Rx(0.5), bev=0)
                k.box("Stone", (0, -0.05, 0.08), (1.25, 0.1, 0.18), bev=0.01)
            k.pop()
        k.pop()
        k.pop()
    # spire: octagonal copper pyramid with a gold ball and cross
    n = 8
    base_y, apex_y, r = TH + 0.15, TH + 12.5, 2.95
    vs = [(TX + math.cos((i + 0.5) / n * math.tau) * r, base_y, math.sin((i + 0.5) / n * math.tau) * r) for i in range(n)]
    vs.append((TX, apex_y, 0))
    faces = [tuple(range(n))] + [(i, (i + 1) % n, n) for i in range(n)]
    k.prim("Copper", vs, faces)
    if not lod:
        # ribs
        for i in range(n):
            a = vs[i]
            k.beam("Copper", (a[0], a[1] + 0.02, a[2]), (TX, apex_y - 0.2, 0), 0.08, 0.08, bev=0)
        # snow collar where the spire meets the cornice, and snow on the four gablets
        for i in range(n):
            a = Vector(vs[i])
            b = Vector(vs[(i + 1) % n])
            m = (a + b) * 0.5
            k.box("Snow", (m.x, base_y + 0.25, m.z), ((b - a).length + 0.1, 0.35, 0.5),
                  rot=Matrix.Rotation(-math.atan2(b.z - a.z, b.x - a.x), 3, 'Y'), bev=0.1, seg=2)
    k.blob("Gold", (TX, apex_y + 0.25, 0), 0.25, seg=8, rings=5)
    k.box("Gold", (TX, apex_y + 1.2, 0), (0.08, 1.5, 0.08), bev=0)
    k.box("Gold", (TX, apex_y + 1.5, 0), (0.7, 0.08, 0.08), bev=0)
    return k.build(name)


def fountain(name):
    rng = random.Random(5)
    k = Kit(0)
    n, R, H = 8, 2.1, 0.72
    for i in range(n):
        a0, a1 = i / n * math.tau, (i + 1) / n * math.tau
        p0 = Vector((math.cos(a0) * R, 0, math.sin(a0) * R))
        p1 = Vector((math.cos(a1) * R, 0, math.sin(a1) * R))
        m = (p0 + p1) * 0.5
        L = (p1 - p0).length
        rot = Matrix.Rotation(-math.atan2(p1.z - p0.z, p1.x - p0.x), 3, 'Y')
        k.box("Stone", (m.x, H * 0.5 - 0.15, m.z), (L + 0.12, H + 0.3, 0.32), rot=rot, bev=0.03)
        k.box("Stone", (m.x * 1.01, H + 0.04, m.z * 1.01), (L + 0.2, 0.1, 0.42), rot=rot, bev=0.02)
        k.box("Snow", (m.x, H + 0.15, m.z), (L + 0.1, 0.14, 0.34), rot=rot, bev=0.05, seg=2)
        icicles(k, rng, p0 * 1.1 + Vector((0, H, 0)), p1 * 1.1 + Vector((0, H, 0)), 0.18, 0.05, 0.22)
    # frozen water surface (octagon) with a little snow drift
    oct_ = [(math.cos((i + 0.5) / n * math.tau) * (R - 0.05), math.sin((i + 0.5) / n * math.tau) * (R - 0.05)) for i in range(n)]
    vs =  [(x, 0.5, z) for x, z in oct_] + [(x, 0.35, z) for x, z in oct_]
    faces = [tuple(range(n)), tuple(range(n, 2 * n))] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    k.prim("Ice", vs, faces)
    k.blob("Snow", (0.9, 0.5, -0.6), 0.9, sy=0.18, seg=8, rings=4)
    # central column (lathe-like stack)
    k.cyl("Stone", (0, 0.3, 0), 0.45, 0.4, 0.4, n=12, bev=0.02)
    k.cyl("Stone", (0, 0.7, 0), 0.22, 0.2, 1.1, n=12)
    k.cyl("Stone", (0, 1.75, 0), 0.25, 0.75, 0.3, n=12, bev=0.02)  # upper bowl
    k.cyl("Stone", (0, 2.05, 0), 0.75, 0.78, 0.1, n=12)
    k.blob("Snow", (0, 2.18, 0), 0.72, sy=0.22, seg=10, rings=4)
    k.cyl("Stone", (0, 2.1, 0), 0.12, 0.1, 0.8, n=10)
    k.blob("Stone", (0, 3.0, 0), 0.18, seg=8, rings=5)
    k.blob("Snow", (0, 3.14, 0), 0.16, sy=0.4, seg=8, rings=4)
    # spouts with frozen drips
    for i in range(4):
        a = i / 4 * math.tau + math.pi / 4
        d = Vector((math.cos(a), 0, math.sin(a)))
        a0 = Vector((0, 1.25, 0)) + d * 0.2
        a1 = Vector((0, 1.2, 0)) + d * 0.75
        k.beam("Metal", a0, a1, 0.05, 0.05, bev=0)
        k.cone("Ice", a1 + Vector((0, -0.02, 0)), 0.06, -0.6, n=6)
        k.cone("Ice", a1 + Vector((0, -1.2 + 0.52, 0)) + d * 0.02, 0.09, 0.35, n=6)
    # fir wreath around the column
    for i in range(18):
        a = i / 18 * math.tau
        k.octa("Garland", (math.cos(a) * 0.3, 1.0, math.sin(a) * 0.3), 0.1, sy=0.8, rng=rng)
    k.blob("Geranium", (0.32, 1.02, 0), 0.07, sy=0.8, seg=5, rings=3)
    return k.build(name)


def sign_arch(name):
    """AZORA welcome arch. Inner faces of the pillars at x = +-6.6 m (the riding corridor is +-3 m,
    carriageway + shoulder +-3.7 m); the underside of the sign board sits at 5.3 m."""
    rng = random.Random(9)
    k = Kit(0)
    PX = 7.05
    for sx in (-1, 1):
        x = sx * PX
        k.box("Stone", (x, 0.9, 0), (0.9, 2.4, 0.9), bev=0.04)
        k.box("Stone", (x, 2.15, 0), (1.05, 0.12, 1.05), bev=0.02)
        k.blob("Snow", (x, 2.25, 0), 0.52, sy=0.28, seg=8, rings=4)
        k.box("TimberDark", (x, 4.9, 0), (0.36, 5.6, 0.36), bev=0.03)
        for zs in (-1, 1):
            swiss_cross(k, (x, 1.3), 0.55, zs * 0.45, facing=zs)
            # knee braces to the beam
            k.beam("TimberDark", (x, 6.0, zs * 0.0), (x - sx * 1.2, 7.1, 0), 0.16, 0.16)
            # lantern hanging from the post
            lx = x - sx * 0.1
            k.beam("Metal", (x, 4.3, zs * 0.18), (x, 4.3, zs * 0.55), 0.04, 0.04, bev=0)
            k.box("Metal", (lx, 4.12, zs * 0.55), (0.22, 0.04, 0.22), bev=0)
            k.box("Glow", (lx, 3.9, zs * 0.55), (0.16, 0.36, 0.16), bev=0)
            k.cone("Metal", (lx, 4.12, zs * 0.55), 0.16, 0.16, n=4)
    # double cross-beam
    k.box("TimberDark", (0, 7.15, 0), (2 * PX + 1.1, 0.34, 0.34), bev=0.03)
    k.box("TimberDark", (0, 5.25, 0), (2 * PX - 0.3, 0.2, 0.22), bev=0.02)
    # red sign board with white AZORA both ways
    k.box("SignRed", (0, 6.15, 0), (7.4, 1.55, 0.16), bev=0.03)
    k.box("Trim", (0, 6.15, 0), (7.6, 1.75, 0.1), bev=0.02)
    for f in (1, -1):
        text_mesh(k, "SignWhite", "AZORA", (0, 6.15, f * 0.08), 1.05, 0.08, max_w=5.2, facing=f)
        swiss_cross(k, (-3.15, 6.15), 0.62, f * 0.08, facing=f)
        swiss_cross(k, (3.15, 6.15), 0.62, f * 0.08, facing=f)
    # small shingle roof over the beam with a snow slab and icicles
    k.push(frame((0, 0, 0), 90))
    gable_roof(k, rng, 0.35, PX + 0.4, 7.35, 35, 0.45, 0.35, thick=0.1, snow=0.24, teeth=False,
               purlins=False, braces=False, rafters=False)
    k.pop()
    # garland along the lower beam with bows
    for i in range(6):
        a = Vector((-PX + 0.4 + i * (2 * PX - 0.8) / 6, 5.15, 0.16))
        b = Vector((-PX + 0.4 + (i + 1) * (2 * PX - 0.8) / 6, 5.15, 0.16))
        garland(k, rng, a, b, sag=0.35)
        garland(k, rng, a * Vector((1, 1, -1)), b * Vector((1, 1, -1)), sag=0.35)
    return k.build(name)


def umbrella(name):
    k = Kit(0)
    k.cyl("Metal", (0, 0, 0), 0.26, 0.22, 0.12, n=10, bev=0.01)
    k.cyl("SignWhite", (0, 0.12, 0), 0.028, 0.028, 1.2, n=8)
    n = 16
    y0, y1, top = 1.25, 2.3, 2.52
    vs = []
    for i in range(n):
        a = i / n * math.tau
        rr = 0.17 if i % 2 == 0 else 0.11
        vs.append((math.cos(a) * 0.05, y0, math.sin(a) * 0.05))
    for i in range(n):
        a = i / n * math.tau
        rr = 0.19 if i % 2 == 0 else 0.12
        vs.append((math.cos(a) * rr, y1, math.sin(a) * rr))
    vs.append((0, top, 0))
    faces = [tuple(range(n))[::-1]] + [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)] + \
            [(n + i, n + (i + 1) % n, 2 * n) for i in range(n)]
    k.prim("Fabric", vs, faces)
    k.cyl("SignWhite", (0, 1.8, 0), 0.155, 0.155, 0.06, n=12)  # tie strap
    k.cyl("SignWhite", (0, top - 0.02, 0), 0.02, 0.02, 0.2, n=6)
    k.blob("Snow", (0, 2.43, 0), 0.13, sy=0.9, seg=8, rings=4)
    k.blob("Snow", (0.06, 2.3, 0.05), 0.1, sy=0.5, seg=6, rings=3)
    return k.build(name)


def flag_pole(name):
    k = Kit(0)
    k.cyl("Stone", (0, -0.2, 0), 0.25, 0.22, 0.45, n=10, bev=0.01)
    k.cyl("SignWhite", (0, 0.2, 0), 0.055, 0.04, 7.0, n=10)
    k.blob("Gold", (0, 7.28, 0), 0.09, seg=8, rings=4)
    # waving square flag (texture: Azora_Village_SwissFlag.png), a closed thin solid
    nx, ny, size, thick = 12, 8, 1.5, 0.015
    top, left = 7.1, 0.06
    grid = []
    for j in range(ny + 1):
        for i in range(nx + 1):
            u, v = i / nx, j / ny
            x = left + u * size
            y = top - size + v * size
            z = math.sin(u * 5.0 + v * 0.8) * 0.12 * u
            grid.append((x, y, z))
    for side in (1, -1):
        verts = [(x, y, z + side * thick) for (x, y, z) in grid]
        faces = []
        for j in range(ny):
            for i in range(nx):
                a = j * (nx + 1) + i
                faces.append((a, a + 1, a + nx + 2, a + nx + 1))
        k.sheet("Flag", verts, faces, hint=(0, 0, side))
    ob = k.build(name + "_tmp")
    # square UVs on the flag faces (box_uv gave them metres); mirrored on the back so the
    # cross reads the same from both sides
    me = ob.data
    fl = None
    for mi, m in enumerate(me.materials):
        if m and m.name.endswith("_Flag"):
            fl = mi
    bm = bmesh.new()
    bm.from_mesh(me)
    uvl = bm.loops.layers.uv[0]
    front = Vector(S.u2b(0, 0, 1))
    for f in bm.faces:
        if f.material_index != fl:
            continue
        is_front = f.normal.dot(front) > 0
        for l in f.loops:
            co = l.vert.co
            ux, uy = -co.x, co.z   # blender -> unity (-bx, bz, -by)
            u = (ux - left) / size
            if not is_front:
                u = 1.0 - u
            l[uvl].uv = (u, (uy - (top - size)) / size)
    bm.to_mesh(me)
    bm.free()
    ob.name = name
    return ob


# ============================================================================ textures

def _save_png(name, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    px = np.ones((h, w, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px.ravel())
    path = os.path.join(MODELS, name + ".png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    print(f"[azora-village] texture {name}.png {w}x{h}")


def _grid(res):
    v, u = np.mgrid[0:res, 0:res].astype(np.float32) / res
    return u, v


def build_textures():
    res = 512
    u, v = _grid(res)
    # --- Timber: horizontal Strickbau logs, 10 courses per 2 m tile, grain along u
    course = v * 10.0
    ci = np.floor(course)
    cf = course - ci
    tone = 0.85 + 0.3 * (S.fbm_2d(ci / 10.0 + 0.37, ci / 10.0, 3.0, 2, seed=3, tile=1.0) - 0.5)
    grain = S.fbm_2d(u * 1.0, v * 12.0, 4.0, 4, seed=11, tile=1.0)
    streak = S.fbm_2d(u * 0.25, v * 40.0, 8.0, 3, seed=5, tile=1.0)
    lum = tone * (0.78 + 0.28 * grain + 0.12 * (streak - 0.5))
    groove = np.clip(1.0 - np.exp(-((cf - 0.0) / 0.05) ** 2) - np.exp(-((cf - 1.0) / 0.05) ** 2), 0.25, 1.0)
    roundness = 0.82 + 0.18 * np.sin(cf * math.pi)
    lum = lum * groove * roundness
    base = np.array([0.50, 0.31, 0.17])
    _save_png("Azora_Village_Timber", lum[..., None] * base[None, None, :] * 1.15)
    # --- Render: warm lime-wash with soft mottling, 3 m tile
    n1 = S.fbm_2d(u, v, 6.0, 5, seed=21, tile=1.0)
    n2 = S.fbm_2d(u, v, 40.0, 3, seed=22, tile=1.0)
    lum = 0.9 + 0.08 * (n1 - 0.5) + 0.05 * (n2 - 0.5)
    base = np.array([0.93, 0.90, 0.84])
    _save_png("Azora_Village_Render", lum[..., None] * base[None, None, :])
    # --- Shingles: staggered wooden shingles, 14 rows per 2 m
    rows = v * 14.0
    ri = np.floor(rows)
    rf = rows - ri
    cols = u * 20.0 + (ri % 2) * 0.5 + 0.3 * (S.value_noise_2d(ri / 14.0, ri / 14.0, 7.0, seed=4, tile=1.0) - 0.5)
    cf2 = cols - np.floor(cols)
    sh_tone = 0.7 + 0.5 * S.value_noise_2d(np.floor(cols) / 20.0, ri / 14.0, 20.0, seed=8, tile=1.0)
    edge = np.clip(np.minimum(cf2, 1 - cf2) / 0.06, 0.35, 1.0)
    shade = 0.55 + 0.45 * rf
    lum = sh_tone * edge * shade
    base = np.array([0.30, 0.24, 0.21])
    _save_png("Azora_Village_Shingle", lum[..., None] * base[None, None, :] * 1.2)
    # --- Cobbles: granite setts in running bond, 16 x 16 per 2 m tile, dark joints
    ny, nx = 16, 12
    rr = v * ny
    ri = np.floor(rr)
    rf = rr - ri
    cc = u * nx + (ri % 2) * 0.5
    ci = np.floor(cc)
    cf = cc - ci
    stone = 0.55 + 0.35 * S.value_noise_2d(ci / nx + 0.013, ri / ny + 0.021, 64.0, seed=13, tile=1.0)
    dx = np.minimum(cf, 1 - cf) / 0.12
    dy = np.minimum(rf, 1 - rf) / 0.14
    bev = np.clip(np.minimum(dx, dy), 0.0, 1.0) ** 0.6
    speck = S.fbm_2d(u, v, 90.0, 2, seed=17, tile=1.0)
    lum = (0.18 + bev * (stone - 0.18)) * (0.9 + 0.2 * speck)
    base = np.array([0.66, 0.65, 0.63])
    tint = 1.0 + 0.06 * (S.value_noise_2d(ci / nx, ri / ny, 64.0, seed=31, tile=1.0)[..., None] - 0.5) * np.array([1.0, 0.2, -0.8])
    _save_png("Azora_Village_Cobble", lum[..., None] * base[None, None, :] * tint)
    # --- Swiss flag (square, white cross arms 6/32 wide, 20/32 long)
    r = 128
    fu, fv = _grid(r)
    red = np.array([0.85, 0.08, 0.10])
    img = np.ones((r, r, 3)) * red
    cx = np.abs(fu - 0.5)
    cy = np.abs(fv - 0.5)
    arm = ((cx < 3 / 32) & (cy < 10 / 32)) | ((cy < 3 / 32) & (cx < 10 / 32))
    img[arm] = (1.0, 1.0, 1.0)
    _save_png("Azora_Village_SwissFlag", img)


# ============================================================================ previews

def preview(obj, fname, dist=1.0):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x, scene.render.resolution_y = 900, 700
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w") if not bpy.data.worlds else bpy.data.worlds[0]
    scene.world = world
    world.color = (0.55, 0.62, 0.72)
    for o in bpy.data.objects:
        o.hide_render = o is not obj and o.type == 'MESH'
    bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    mn = Vector((min(p.x for p in bb), min(p.y for p in bb), min(p.z for p in bb)))
    mx = Vector((max(p.x for p in bb), max(p.y for p in bb), max(p.z for p in bb)))
    c = (mn + mx) * 0.5
    size = (mx - mn).length
    cam_d = bpy.data.cameras.new("cam")
    cam_d.lens = 35
    cam = bpy.data.objects.new("cam", cam_d)
    S.link(cam)
    # Unity front (+z) = Blender -y; look from front-right-above
    eye = c + Vector(S.u2b(0.55, 0.35, 0.95)).normalized() * size * 1.05 * dist
    cam.location = eye
    d = (c - eye)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    scene.render.filepath = os.path.join(PREVIEW_DIR, fname)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ============================================================================ main

CHALETS = {
    # name: W, D, g (ground floor), storeys, pitch, overhang side, front, seed, shutter, attic balcony
    "ChaletA": dict(W=4.2, D=5.0, g=2.75, storeys=1, pitch=27, ov_side=1.3, ov_front=1.5, seed=11, shutter="Shutter"),
    "ChaletB": dict(W=5.0, D=6.0, g=2.8, storeys=2, pitch=24, ov_side=1.5, ov_front=1.8, seed=23, shutter="Shutter",
                    attic_balcony=True),
    "ChaletC": dict(W=3.6, D=4.6, g=2.6, storeys=1, pitch=30, ov_side=1.2, ov_front=1.3, seed=37, shutter="ShutterRed",
                    render_ground=False),
    "Cafe":    dict(W=4.6, D=5.4, g=3.0, storeys=1, pitch=26, ov_side=1.4, ov_front=1.6, seed=41, shutter="Shutter",
                    shop=("CAFÉ", "Fabric")),
    "Bakery":  dict(W=4.4, D=5.2, g=3.0, storeys=1, pitch=27, ov_side=1.3, ov_front=1.5, seed=53, shutter="ShutterRed",
                    shop=("BÄCKEREI", "FabricBlue")),
}


def export(ob, fname):
    S.report(fname, ob)
    if not STATS:
        S.export_glb([ob], fname)


def main():
    S.reset_scene()
    if not STATS:
        build_textures()
    built = []
    for nm, p in CHALETS.items():
        for lod in (0, 1):
            full = f"Azora_Village_{nm}" + ("_LOD1" if lod else "")
            ob = chalet(full, lod, **p)
            export(ob, full + ".glb")
            built.append((ob, full, lod))
    for lod in (0, 1):
        full = "Azora_Village_Church" + ("_LOD1" if lod else "")
        ob = church(full, lod)
        export(ob, full + ".glb")
        built.append((ob, full, lod))
    for fn, nm in ((fountain, "Fountain"), (sign_arch, "SignArch"), (umbrella, "Umbrella"), (flag_pole, "FlagPole")):
        full = f"Azora_Village_{nm}"
        ob = fn(full)
        export(ob, full + ".glb")
        built.append((ob, full, 0))
    if PREVIEW:
        for ob, full, lod in built:
            preview(ob, full + ".png")
    print("[azora-village] done")


main()

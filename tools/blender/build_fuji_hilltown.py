"""
E1 (pass 1)  Fuji Ridge - Italian hill-town HOUSE KIT.   copilot CLI (E1 owner), 2026-09-28.

Replaces the procedural box houses of FujiRidge.Town.cs::House() with six hero stone/stucco
townhouses, each exported at three LODs:

    Fuji_Town_<Variant>_LOD0.glb   hero: true 22 cm window reveals, travertine surrounds + sills,
                                   louvred persiane (open / ajar / closed), glazing bars, wrought
                                   iron balconies on stone corbels, quoins, stepped cornice,
                                   rafter tails, coppi ridge + eave tile ends, gutters/downpipes,
                                   chimney, shop fronts with awnings and 3D lettering, round-arch
                                   porticoes, geranium window boxes
    Fuji_Town_<Variant>_LOD1.glb   same silhouette + reveals, no small parts (~20-30% of LOD0)
    Fuji_Town_<Variant>_LOD2.glb   massing box + roof + flat opening quads (a few hundred tris)

and the PBR map set they use (albedo + tangent-space normal, OpenGL/Unity Y+):

    Fuji_Town_{Stucco,Masonry,Coppi,Travertine,Louvre,Wood}_{Albedo,Normal}.png

KIT FRAME (Unity coordinates, converted with sakura_lib.u2b so Unity stages with identity):
    x  along the street (the house's own right),  y up,  +z = FRONT (faces the carriageway)
    origin = centre of the frontage, ON the facade plane, at pavement level.
    The body runs back to z = -D; the plinth runs down to y = -3 m so a 7% street never gaps.
Material slots are named Fuji_Town_<Role>; FujiRidge.HillTownKit.cs re-materialises them by
name (CelLit + these maps). Colours here only drive the Blender preview / glTF fallback.

Run:
  tools\\blender-4.5.10-windows-x64\\blender.exe -b --factory-startup -P tools/blender/build_fuji_hilltown.py
        [-- --preview]      also render EEVEE previews to reference/good_graphics/fuji_e1/
        [-- --no-textures]  skip the (slow-ish) texture stage
All dimensions below are PROVISIONAL art tuning, not design requirements.
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
MODELS = os.path.join(ROOT, "Assets", "Environment", "FujiRidge", "Models")
TEXTURES = os.path.join(ROOT, "Assets", "Environment", "FujiRidge", "Textures")
PREVIEW_DIR = os.path.join(ROOT, "reference", "good_graphics", "fuji_e1")
os.makedirs(MODELS, exist_ok=True)
os.makedirs(TEXTURES, exist_ok=True)
S.blender_assets_dir = lambda: MODELS

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = "--preview" in ARGS
NO_TEX = "--no-textures" in ARGS

# ---- provisional art tuning ---------------------------------------------------------------
GROUND_H = 3.5        # ground-floor storey (shops / portico)
FLOOR_H = 3.1         # upper storeys (matches FujiRidge.Town.cs fh so laundry lines still fit)
REVEAL = 0.22         # window reveal depth
PLINTH_DOWN = 3.0     # body continues this far below pavement level
ROOF_PITCH = 19.0     # coppi roofs are low
EAVE_FRONT = 0.62
EAVE_SIDE = 0.18
TEX_RES = 1024

# ============================================================================ roles

# role -> (preview colour, roughness, texture stem or None, uv tile metres)
ROLES = {
    "RenderOchre":  ((0.90, 0.64, 0.33), 0.92, "Stucco", 4.0),
    "RenderRosa":   ((0.88, 0.62, 0.52), 0.92, "Stucco", 4.0),
    "RenderCream":  ((0.96, 0.84, 0.62), 0.92, "Stucco", 4.0),
    "RenderSiena":  ((0.74, 0.42, 0.26), 0.92, "Stucco", 4.0),
    "Masonry":      ((1.00, 1.00, 1.00), 0.95, "Masonry", 2.0),
    "Travertine":   ((1.00, 1.00, 1.00), 0.80, "Travertine", 1.0),
    "Coppi":        ((1.00, 1.00, 1.00), 0.85, "Coppi", 1.2),
    "ShutterGreen": ((0.30, 0.47, 0.34), 0.70, "Louvre", 1.0),
    "ShutterBrown": ((0.52, 0.34, 0.22), 0.70, "Louvre", 1.0),
    "Wood":         ((1.00, 1.00, 1.00), 0.75, "Wood", 1.0),
    "Frame":        ((0.90, 0.88, 0.82), 0.60, None, 1.0),
    "Glass":        ((0.10, 0.12, 0.14), 0.08, None, 1.0),
    "Interior":     ((0.12, 0.10, 0.09), 0.95, None, 1.0),
    "Iron":         ((0.07, 0.07, 0.075), 0.45, None, 1.0),
    "Copper":       ((0.55, 0.36, 0.22), 0.40, None, 1.0),
    "Terracotta":   ((0.66, 0.34, 0.20), 0.85, None, 1.0),
    "Geranium":     ((0.86, 0.10, 0.12), 0.80, None, 1.0),
    "Leaf":         ((0.16, 0.34, 0.12), 0.80, None, 1.0),
    "AwningRed":    ((0.70, 0.12, 0.10), 0.90, None, 1.0),
    "AwningGreen":  ((0.14, 0.36, 0.24), 0.90, None, 1.0),
    "AwningCream":  ((0.90, 0.84, 0.70), 0.90, None, 1.0),
    "SignGold":     ((0.86, 0.66, 0.26), 0.35, None, 1.0),
    "SignDark":     ((0.10, 0.16, 0.14), 0.70, None, 1.0),
}

_MATS = {}


def material(role):
    if role in _MATS:
        return _MATS[role]
    col, rough, _, _ = ROLES[role]
    m = S.pbr_material(f"Fuji_Town_{role}", base_color=(col[0], col[1], col[2], 1.0), roughness=rough,
                       metallic=0.5 if role in ("Iron", "Copper", "SignGold") else 0.0)
    m.diffuse_color = (col[0], col[1], col[2], 1.0)
    _MATS[role] = m
    return m


# ============================================================================ kit

class Kit:
    """Closed primitives per role in UNITY coords; each primitive is its own island so bmesh's
    flood-fill normal recalculation orients every one outward independently (no global flip)."""

    def __init__(self, lod):
        self.lod = lod
        self.groups = {}
        self.xf = Matrix.Identity(4)
        self.stack = []

    def push(self, m):
        self.stack.append(self.xf)
        self.xf = self.xf @ m

    def pop(self):
        self.xf = self.stack.pop()

    def prim(self, role, verts, faces):
        self.groups.setdefault(role, []).append(([self.xf @ Vector(v) for v in verts], faces))

    def box(self, role, c, size, rot=None):
        hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
        if min(size) <= 0:
            return
        R = rot if rot is not None else Matrix.Identity(3)
        cv = Vector(c)
        vs = [cv + R @ Vector(((hx if i & 1 else -hx), (hy if i & 2 else -hy), (hz if i & 4 else -hz)))
              for i in range(8)]
        faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        self.prim(role, vs, faces)

    def aabb(self, role, x0, x1, y0, y1, z0, z1):
        self.box(role, ((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5), (x1 - x0, y1 - y0, z1 - z0))

    def beam(self, role, a, b, w, h, up=(0, 1, 0)):
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
        self.box(role, (a + b) * 0.5, (L, h, w), rot=Matrix((x, y, z)).transposed())

    def hexa(self, role, front4, depth_vec):
        """Convex quad (4 pts, any winding) extruded by depth_vec -> closed hexahedron."""
        f = [Vector(p) for p in front4]
        d = Vector(depth_vec)
        vs = f + [p + d for p in f]
        faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
        self.prim(role, vs, faces)

    def tri_prism(self, role, tri, depth_vec):
        f = [Vector(p) for p in tri]
        d = Vector(depth_vec)
        vs = f + [p + d for p in f]
        faces = [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)]
        self.prim(role, vs, faces)

    def cyl(self, role, c, r0, r1, h, n=10, axis=None):
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
        self.prim(role, vs, faces)

    def blob(self, role, c, r, sy=1.0, seg=6, rings=4):
        cv = Vector(c)
        vs = [cv + Vector((0, -r * sy, 0))]
        for j in range(1, rings):
            ph = -math.pi / 2 + j / rings * math.pi
            for i in range(seg):
                a = i / seg * math.tau
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

    def text(self, role, s, centre, cap_h, depth, max_w):
        """3-D lettering on the facade (Unity x-y plane, facing +z)."""
        cu = bpy.data.curves.new("txt", type='FONT')
        cu.body = s
        cu.size = cap_h / 0.72
        cu.extrude = depth * 0.5
        cu.align_x = 'CENTER'
        cu.align_y = 'CENTER'
        ob = bpy.data.objects.new("txt", cu)
        S.link(ob)
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        xs = [v.co.x for v in me.vertices]
        w = (max(xs) - min(xs)) if xs else 1.0
        sc = min(1.0, max_w / max(w, 1e-4))
        vs = [(centre[0] - v.co.x * sc, centre[1] + v.co.y * sc, centre[2] + depth * 0.5 + v.co.z) for v in me.vertices]
        faces = [tuple(p.vertices) for p in me.polygons]
        self.prim(role, vs, faces)
        bpy.data.objects.remove(ob)
        bpy.data.curves.remove(cu)
        bpy.data.meshes.remove(me)

    # ---- build
    def build(self, name):
        objs = []
        tris = 0
        for role, prims in self.groups.items():
            bm = bmesh.new()
            for vv, faces in prims:
                bv = [bm.verts.new(S.u2b(v.x, v.y, v.z)) for v in vv]
                for f in faces:
                    try:
                        bm.faces.new([bv[i] for i in f])
                    except ValueError:
                        continue
            bm.normal_update()
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            ngons = [f for f in bm.faces if len(f.verts) > 4]
            if ngons:
                bmesh.ops.triangulate(bm, faces=ngons)
            me = bpy.data.meshes.new(f"{name}_{role}")
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(f"{name}_{role}", me)
            S.link(ob)
            ob.data.materials.append(material(role))
            smooth = role in ("Geranium", "Leaf")
            for p in ob.data.polygons:
                p.use_smooth = smooth
            box_uv(ob, ROLES[role][3], flip_back=(role == "Coppi"))
            tris += sum(len(p.vertices) - 2 for p in ob.data.polygons)
            objs.append(ob)
        ob = S.join(objs, name)
        print(f"[fuji-town] {name}: ~{tris:,} tris, {len(self.groups)} roles")
        return ob


def box_uv(ob, tile, flip_back=False):
    """Per-loop box projection in metres/tile (Blender axes: z is Unity up). For roofs the back
    slope's v is mirrored so the coppi courses always overlap DOWN-slope."""
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
                vv = co.y
                if flip_back and n.y > 0:
                    vv = -co.y
                uv[li].uv = (co.x * inv, vv * inv)
            elif ax == 0:
                uv[li].uv = (co.y * inv, co.z * inv)
            else:
                uv[li].uv = (co.x * inv, co.z * inv)


def Rx(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'X')


def Ry(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'Y')


# ============================================================================ house plan

VARIANTS = {
    # name: W, D, floors (incl. ground), wall, ground type, shutter, awning, sign, seed, extras
    "CasaBar":      dict(W=7.0, D=8.5, floors=2, wall="RenderOchre", ground="shop", shutter="ShutterGreen",
                         awning="AwningRed", sign="BAR · CAFFÈ", seed=11, balcony_floors=(1,)),
    "CasaBottega":  dict(W=8.5, D=9.0, floors=3, wall="RenderRosa", ground="shop", shutter="ShutterGreen",
                         awning="AwningGreen", sign="ALIMENTARI", seed=23, balcony_floors=(1,)),
    "CasaPortico":  dict(W=9.5, D=9.0, floors=3, wall="Masonry", ground="arcade", shutter="ShutterBrown",
                         seed=37, balcony_floors=()),
    "CasaAlta":     dict(W=7.5, D=8.0, floors=4, wall="RenderCream", ground="door", shutter="ShutterGreen",
                         seed=41, balcony_floors=(1, 2)),
    "CasaPietra":   dict(W=9.0, D=8.5, floors=2, wall="Masonry", ground="arcade", shutter="ShutterGreen",
                         seed=53, balcony_floors=()),
    "CasaForno":    dict(W=8.0, D=8.5, floors=4, wall="RenderSiena", ground="shop", shutter="ShutterBrown",
                         awning="AwningCream", sign="FORNO", seed=67, balcony_floors=(1,), long_balcony=True),
}


def plan_house(p):
    """Deterministic layout shared by every LOD (so LOD0/1/2 always agree)."""
    rng = random.Random(p["seed"])
    W, floors = p["W"], p["floors"]
    H = GROUND_H + (floors - 1) * FLOOR_H + 0.35
    cols = max(2, int(W // 2.45))
    xs = [(-W / 2) + W * (i + 0.5) / cols for i in range(cols)]
    ups = []
    for f in range(1, floors):
        y0 = GROUND_H + (f - 1) * FLOOR_H
        top = f == floors - 1 and floors >= 4
        balc = f in p.get("balcony_floors", ())
        row = []
        for ci, x in enumerate(xs):
            french = balc and (p.get("long_balcony") or ci in (cols // 2 - (1 - cols % 2), cols // 2))
            w = 0.95 if not top else 0.8
            h = 2.35 if french else (1.55 if not top else 1.05)
            ys = y0 + (0.03 if french else (0.92 if not top else 1.1))
            r = rng.random()
            state = "open" if r < 0.55 else ("closed" if r < 0.8 else "ajar")
            row.append(dict(x=x, w=w, y=ys, h=h, french=french, state=state,
                            flowers=(not french and rng.random() < 0.45), seed=rng.randrange(1 << 30)))
        ups.append(dict(y0=y0, top=top, balcony=balc, windows=row))
    return dict(W=W, D=p["D"], H=H, floors=floors, cols=cols, xs=xs, ups=ups, rng_seed=rng.randrange(1 << 30))


# ============================================================================ elements

def facade_band(k, role, W, y0, y1, openings, z0=-REVEAL, z1=0.0):
    """Solid facade slab y0..y1 across the width with rectangular holes (x0, x1, ya, yb). The holes'
    backs are closed by the body box at z0, which gives every opening a true reveal."""
    xs = sorted({-W / 2, W / 2} | {o[0] for o in openings} | {o[1] for o in openings})
    for a, b in zip(xs, xs[1:]):
        if b - a < 1e-4:
            continue
        mid = (a + b) * 0.5
        hole = next((o for o in openings if o[0] <= mid <= o[1]), None)
        if hole is None:
            k.aabb(role, a, b, y0, y1, z0, z1)
        else:
            if hole[2] > y0 + 1e-4:
                k.aabb(role, a, b, y0, hole[2], z0, z1)
            if hole[3] < y1 - 1e-4:
                k.aabb(role, a, b, hole[3], y1, z0, z1)


def surround(k, x, y, w, h, sill=True, lintel=True):
    """Travertine frame, projecting sill and a lintel block (keystone on wide ones)."""
    t = 0.11
    k.aabb("Travertine", x - w / 2 - t, x - w / 2, y, y + h, 0.0, 0.035)
    k.aabb("Travertine", x + w / 2, x + w / 2 + t, y, y + h, 0.0, 0.035)
    if lintel:
        k.aabb("Travertine", x - w / 2 - t, x + w / 2 + t, y + h, y + h + 0.16, 0.0, 0.045)
        if w > 1.2 and not k.lod:
            k.aabb("Travertine", x - 0.1, x + 0.1, y + h - 0.02, y + h + 0.22, 0.0, 0.07)
    if sill:
        k.aabb("Travertine", x - w / 2 - 0.16, x + w / 2 + 0.16, y - 0.08, y, -0.03, 0.09)


def glazing(k, x, y, w, h, bars=True):
    k.aabb("Glass", x - w / 2, x + w / 2, y, y + h, -REVEAL, -REVEAL + 0.03)
    if not bars or k.lod:
        return
    z0, z1 = -REVEAL + 0.03, -REVEAL + 0.08
    fw = 0.055
    k.aabb("Frame", x - w / 2, x - w / 2 + fw, y, y + h, z0, z1)
    k.aabb("Frame", x + w / 2 - fw, x + w / 2, y, y + h, z0, z1)
    k.aabb("Frame", x - w / 2, x + w / 2, y, y + fw, z0, z1)
    k.aabb("Frame", x - w / 2, x + w / 2, y + h - fw, y + h, z0, z1)
    k.aabb("Frame", x - 0.025, x + 0.025, y, y + h, z0, z1)
    for t in (0.36, 0.68):
        yy = y + h * t
        k.aabb("Frame", x - w / 2, x + w / 2, yy - 0.02, yy + 0.02, z0, z1)


def shutters(k, role, x, y, w, h, state):
    lw = w * 0.5
    th = 0.035
    for s in (-1, 1):
        if state == "closed":
            cx = x + s * lw * 0.5
            k.aabb(role, cx - lw / 2 + 0.005, cx + lw / 2 - 0.005, y + 0.01, y + h - 0.01, -0.12, -0.12 + th)
        elif state == "open":
            # folded flat back onto the wall beside the opening (outside the stone surround)
            x0 = x + s * (w / 2 + 0.12)
            x1 = x0 + s * lw
            k.aabb(role, min(x0, x1), max(x0, x1), y, y + h, 0.04, 0.04 + th)
        else:  # ajar: hinged at the jamb, swung ~65 degrees out
            hinge = Vector((x + s * (w / 2), y, 0.02))
            k.push(Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(-s * 65), 4, 'Y'))
            k.aabb(role, min(0, s * lw), max(0, s * lw), 0, h, 0.0, th)
            k.pop()
        if not k.lod and state != "closed":
            # iron hinge pins + a shutter keeper
            k.aabb("Iron", x + s * (w / 2 + 0.06) - 0.02, x + s * (w / 2 + 0.06) + 0.02, y + h * 0.2, y + h * 0.2 + 0.05, 0.0, 0.07)
            k.aabb("Iron", x + s * (w / 2 + 0.06) - 0.02, x + s * (w / 2 + 0.06) + 0.02, y + h * 0.8, y + h * 0.8 + 0.05, 0.0, 0.07)


def flower_box(k, x, w, y, rng):
    k.aabb("Terracotta", x - w / 2, x + w / 2, y, y + 0.18, 0.09, 0.30)
    if k.lod:
        k.aabb("Leaf", x - w / 2 + 0.03, x + w / 2 - 0.03, y + 0.18, y + 0.28, 0.12, 0.28)
        return
    n = int(w / 0.14)
    for i in range(n):
        px = x - w / 2 + (i + 0.5) * w / n
        k.blob("Leaf", (px, y + 0.24, 0.19 + (rng.random() - 0.5) * 0.06), 0.09, 0.8, seg=5, rings=3)
        if rng.random() < 0.7:
            k.blob("Geranium", (px + (rng.random() - 0.5) * 0.05, y + 0.33 + rng.random() * 0.05,
                                0.2 + (rng.random() - 0.5) * 0.08), 0.055, 1.0, seg=5, rings=3)
        if rng.random() < 0.35:  # trailing foliage over the lip
            k.blob("Leaf", (px, y + 0.02, 0.31), 0.05, 2.2, seg=4, rings=3)


def balcony(k, x0, x1, y, rng, depth=0.78):
    """Stone slab on stepped corbels with a wrought-iron railing."""
    k.aabb("Travertine", x0, x1, y - 0.15, y, -0.02, depth)
    ncb = max(2, int((x1 - x0) / 0.9) + 1)
    for i in range(ncb):
        cx = x0 + 0.12 + i * ((x1 - x0 - 0.24) / (ncb - 1))
        k.aabb("Travertine", cx - 0.08, cx + 0.08, y - 0.34, y - 0.15, 0.0, depth * 0.72)
        if not k.lod:
            k.aabb("Travertine", cx - 0.07, cx + 0.07, y - 0.5, y - 0.34, 0.0, depth * 0.42)
    rail_h = 1.0
    zf = depth - 0.05
    # rails (front + two returns)
    k.aabb("Iron", x0 + 0.02, x1 - 0.02, y + rail_h - 0.04, y + rail_h, zf - 0.025, zf + 0.025)
    k.aabb("Iron", x0 + 0.02, x1 - 0.02, y + 0.08, y + 0.11, zf - 0.015, zf + 0.015)
    for xe in (x0 + 0.03, x1 - 0.03):
        k.aabb("Iron", xe - 0.02, xe + 0.02, y + rail_h - 0.04, y + rail_h, 0.0, zf)
        k.aabb("Iron", xe - 0.015, xe + 0.015, y, y + rail_h, zf - 0.015, zf + 0.015)
    pitch = 0.12 if not k.lod else 0.3
    n = int((x1 - x0) / pitch)
    for i in range(1, n):
        bx = x0 + i * (x1 - x0) / n
        k.aabb("Iron", bx - 0.009, bx + 0.009, y + 0.11, y + rail_h - 0.04, zf - 0.009, zf + 0.009)
    if not k.lod:
        # side balusters on the returns + a scroll band under the top rail
        for xe in (x0 + 0.03, x1 - 0.03):
            for t in (0.3, 0.55):
                k.aabb("Iron", xe - 0.009, xe + 0.009, y + 0.11, y + rail_h - 0.04, zf * t - 0.009, zf * t + 0.009)
        k.aabb("Iron", x0 + 0.02, x1 - 0.02, y + rail_h - 0.2, y + rail_h - 0.17, zf - 0.012, zf + 0.012)
        # potted geraniums on the slab
        for i in range(max(1, int((x1 - x0) / 0.8))):
            px = x0 + 0.35 + i * 0.8 + rng.random() * 0.15
            if px > x1 - 0.25:
                break
            k.cyl("Terracotta", (px, y, zf - 0.2), 0.12, 0.15, 0.24, n=8)
            k.blob("Leaf", (px, y + 0.33, zf - 0.2), 0.17, 0.75, seg=6, rings=3)
            k.blob("Geranium", (px + 0.04, y + 0.45, zf - 0.17), 0.08, 1.0, seg=5, rings=3)


def quoins(k, W, y0, y1):
    """Alternating long/short travertine corner blocks on both front corners."""
    y = y0
    i = 0
    while y < y1 - 0.05:
        h = min(0.34, y1 - y)
        L = 0.55 if i % 2 == 0 else 0.32
        for s in (-1, 1):
            xa = s * W / 2
            k.aabb("Travertine", min(xa, xa - s * L), max(xa, xa - s * L), y + 0.01, y + h - 0.01, -0.02, 0.03)
        y += h
        i += 1


def cornice(k, W, H):
    k.aabb("Travertine", -W / 2, W / 2, H - 0.12, H, -0.05, 0.12)
    k.aabb("Travertine", -W / 2 - 0.02, W / 2 + 0.02, H, H + 0.12, -0.05, 0.26)
    k.aabb("Travertine", -W / 2 - 0.05, W / 2 + 0.05, H + 0.12, H + 0.2, -0.05, 0.42)


def roof(k, W, D, H, rng, chimney_x):
    ye = H + 0.2                      # eave line
    zf, zb = EAVE_FRONT, -D - EAVE_FRONT
    zr = -D / 2
    t = math.tan(math.radians(ROOF_PITCH))
    yr = ye + (zf - zr) * t           # ridge height
    x0, x1 = -W / 2 - EAVE_SIDE, W / 2 + EAVE_SIDE
    th = 0.12
    for (za, zb2) in ((zf, zr), (zb, zr)):
        # slab as a hexahedron from eave (za) to ridge (zb2)
        ya = ye
        yb = yr
        n = Vector((0, zb2 - za, -(yb - ya))).normalized()
        if n.y < 0:
            n = -n
        up = n * th
        front = [(x0, ya, za), (x1, ya, za), (x1, yb, zb2), (x0, yb, zb2)]
        k.hexa("Coppi", [Vector(p) for p in front], up)
    # gable end walls (hidden by neighbours in a row, visible across gaps)
    for s in (-1, 1):
        xg = s * W / 2
        tri = [(xg, H, 0.0), (xg, H, -D), (xg, H + (D / 2) * t + 0.2, -D / 2)]
        k.tri_prism("Masonry" if k.wall == "Masonry" else k.wall, tri, (-s * 0.3, 0, 0))
    # back wall top (behind the cornice, up to the eave)
    k.aabb(k.wall, -W / 2, W / 2, H, H + 0.2, -D, -D + 0.3)
    # ridge coppi
    ridge_y = yr + th * 0.9
    k.cyl("Coppi", (x0, ridge_y, zr), 0.12, 0.12, x1 - x0, n=8 if not k.lod else 5, axis=Matrix.Rotation(-math.pi / 2, 3, 'Z'))
    if k.lod == 0:
        # eave tile ends (the scalloped coppi silhouette) + rafter tails + gutter + downpipe
        step = 0.16
        x = x0 + step / 2
        while x < x1:
            k.cyl("Coppi", (x, ye + 0.07, zf - 0.28), 0.07, 0.065, 0.32, n=6, axis=Rx(90))
            x += step
        x = -W / 2 + 0.3
        while x < W / 2:
            k.aabb("Wood", x - 0.05, x + 0.05, ye - 0.14, ye - 0.02, 0.0, zf - 0.04)
            x += 0.6
        k.cyl("Copper", (x0 + 0.05, ye - 0.04, zf - 0.02), 0.07, 0.07, x1 - x0 - 0.1, n=6, axis=Matrix.Rotation(-math.pi / 2, 3, 'Z'))
        dx = W / 2 - 0.12 if rng.random() < 0.5 else -W / 2 + 0.12
        k.cyl("Copper", (dx, -0.1, zf - 0.55 if False else 0.12), 0.045, 0.045, ye + 0.05, n=6)
        k.aabb("Copper", dx - 0.05, dx + 0.05, ye - 0.1, ye, 0.08, zf)
    # chimney (comignolo) with its own little tiled cap
    cz = -D * 0.62
    cyb = ye + (zf - cz) * t if cz > zr else yr
    k.aabb(k.wall if k.wall != "Masonry" else "Masonry", chimney_x - 0.32, chimney_x + 0.32, cyb - 1.0, yr + 1.1, cz - 0.3, cz + 0.3)
    k.aabb("Travertine", chimney_x - 0.38, chimney_x + 0.38, yr + 1.1, yr + 1.18, cz - 0.36, cz + 0.36)
    if k.lod < 2:
        for s in (-1, 1):
            k.aabb("Travertine", chimney_x - 0.34, chimney_x + 0.34, yr + 1.18, yr + 1.45, cz + s * 0.3 - 0.04, cz + s * 0.3 + 0.04)
        k.hexa("Coppi", [(chimney_x - 0.45, yr + 1.45, cz - 0.45), (chimney_x + 0.45, yr + 1.45, cz - 0.45),
                         (chimney_x + 0.45, yr + 1.6, cz), (chimney_x - 0.45, yr + 1.6, cz)], (0, 0.06, 0))
        k.hexa("Coppi", [(chimney_x - 0.45, yr + 1.45, cz + 0.45), (chimney_x + 0.45, yr + 1.45, cz + 0.45),
                         (chimney_x + 0.45, yr + 1.6, cz), (chimney_x - 0.45, yr + 1.6, cz)], (0, 0.06, 0))


def arch_front(k, role, cx, r, ys, ytop, z0, z1, ring=0.18, n=10):
    """Round arch of clear half-span r springing at ys, as convex cells: archivolt ring cells
    (travertine) + spandrel cells up to ytop (wall), all extruded z0..z1."""
    r2 = r + ring
    d = (0, 0, z0 - z1)
    for i in range(n):
        a0 = math.pi - i * math.pi / n
        a1 = math.pi - (i + 1) * math.pi / n
        pi0 = (cx + math.cos(a0) * r, ys + math.sin(a0) * r, z1)
        pi1 = (cx + math.cos(a1) * r, ys + math.sin(a1) * r, z1)
        po0 = (cx + math.cos(a0) * r2, ys + math.sin(a0) * r2, z1)
        po1 = (cx + math.cos(a1) * r2, ys + math.sin(a1) * r2, z1)
        k.hexa("Travertine" if k.lod < 2 else role, [pi0, pi1, po1, po0], d)
        k.hexa(role, [po0, po1, (po1[0], ytop, z1), (po0[0], ytop, z1)], d)
    # spring blocks below the ring down to the pier top
    k.aabb(role, cx - r2, cx - r, 0.0, ys, z0, z1)
    k.aabb(role, cx + r, cx + r2, 0.0, ys, z0, z1)


# ============================================================================ house

def house(name, lod, p):
    L = plan_house(p)
    W, D, H = L["W"], L["D"], L["H"]
    rng = random.Random(L["rng_seed"] + 7)       # small-part rng; LOD-independent seed
    k = Kit(lod)
    k.wall = p["wall"]
    wall = p["wall"]
    ground = p["ground"]
    arcade_depth = 2.4 if ground == "arcade" else 0.0

    # ---- body -----------------------------------------------------------------------------
    back_z = -REVEAL if lod < 2 else 0.0
    k.aabb(wall, -W / 2, W / 2, GROUND_H, H, -D, back_z)                         # upper storeys
    if ground == "arcade":
        k.aabb(wall, -W / 2, W / 2, -PLINTH_DOWN, GROUND_H, -D, -arcade_depth)   # back of portico
        k.aabb("Masonry", -W / 2, W / 2, -PLINTH_DOWN, 0.0, -arcade_depth, 0.0)  # plinth under floor
        k.aabb("Travertine", -W / 2 + 0.01, W / 2 - 0.01, -0.02, 0.04, -arcade_depth, 0.02)
    else:
        k.aabb(wall, -W / 2, W / 2, -PLINTH_DOWN, GROUND_H, -D, back_z)
    # stone socle / base band (render houses) - runs down with the plinth
    if wall != "Masonry" and ground != "arcade":
        gaps = [(W / 2 - 1.2 - 0.55, W / 2 - 1.2 + 0.55)] if ground == "shop" else [(-0.75, 0.75)]
        xs = [-W / 2]
        for a, b in gaps:
            xs += [a, b]
        xs.append(W / 2)
        for i in range(0, len(xs), 2):
            k.aabb("Masonry", xs[i], xs[i + 1], -PLINTH_DOWN, 0.45, -0.3, 0.05)
        for a, b in gaps:   # under the threshold only
            k.aabb("Masonry", a, b, -PLINTH_DOWN, -0.1, -0.3, 0.05)

    # ---- upper floors ---------------------------------------------------------------------
    if lod < 2:
        for fl in L["ups"]:
            y0, y1 = fl["y0"], fl["y0"] + FLOOR_H
            if fl is L["ups"][-1]:
                y1 = H
            holes = [(w["x"] - w["w"] / 2, w["x"] + w["w"] / 2, w["y"], w["y"] + w["h"]) for w in fl["windows"]]
            facade_band(k, wall, W, y0, y1, holes)
            for w in fl["windows"]:
                wr = random.Random(w["seed"])
                surround(k, w["x"], w["y"], w["w"], w["h"], sill=not w["french"])
                glazing(k, w["x"], w["y"], w["w"], w["h"])
                shutters(k, p["shutter"], w["x"], w["y"], w["w"], w["h"], w["state"])
                if w["flowers"] and lod == 0 or (w["flowers"] and lod == 1 and wr.random() < 0.5):
                    flower_box(k, w["x"], w["w"] + 0.1, w["y"], wr)
            if fl["balcony"]:
                fr = [w for w in fl["windows"] if w["french"]]
                if fr:
                    if p.get("long_balcony"):
                        balcony(k, -W / 2 + 0.35, W / 2 - 0.35, fl["y0"], rng)
                    else:
                        for w in fr:
                            balcony(k, w["x"] - w["w"] / 2 - 0.45, w["x"] + w["w"] / 2 + 0.45, fl["y0"], rng)
        # string course between ground floor and piano nobile
        k.aabb("Travertine", -W / 2, W / 2, GROUND_H - 0.1, GROUND_H + 0.06, -0.05, 0.1)
        if wall != "Masonry":
            quoins(k, W, 0.45, H - 0.12)
        cornice(k, W, H)
    else:
        # LOD2: flat opening quads on the massing box
        for fl in L["ups"]:
            for w in fl["windows"]:
                k.aabb("Glass", w["x"] - w["w"] / 2, w["x"] + w["w"] / 2, w["y"], w["y"] + w["h"], 0.0, 0.02)
                if w["state"] != "closed":
                    for s in (-1, 1):
                        xa = w["x"] + s * (w["w"] / 2 + 0.1)
                        k.aabb(p["shutter"], min(xa, xa + s * w["w"] * 0.5), max(xa, xa + s * w["w"] * 0.5),
                               w["y"], w["y"] + w["h"], 0.0, 0.03)
                else:
                    k.aabb(p["shutter"], w["x"] - w["w"] / 2, w["x"] + w["w"] / 2, w["y"], w["y"] + w["h"], 0.0, 0.03)
        k.aabb("Travertine", -W / 2, W / 2, H - 0.05, H + 0.2, 0.0, 0.3)

    # ---- ground floor ---------------------------------------------------------------------
    if ground == "shop":
        gw = min(W * 0.52, 4.2)
        gx = -W / 2 + 0.7 + gw / 2
        dx = W / 2 - 1.2
        holes = [(gx - gw / 2, gx + gw / 2, 0.45, 2.75), (dx - 0.55, dx + 0.55, 0.0, 2.55)]
        if lod < 2:
            facade_band(k, wall, W, 0.45 if wall != "Masonry" else 0.0, GROUND_H - 0.1, holes, z0=-REVEAL, z1=0.0)
            if wall != "Masonry":
                # the socle must be cut for the door
                pass
            surround(k, gx, 0.45, gw, 2.3, sill=True)
            k.aabb("Glass", gx - gw / 2, gx + gw / 2, 0.45, 2.75, -REVEAL, -REVEAL + 0.03)
            if lod == 0:
                for t in (0.0, 0.33, 0.66, 1.0):
                    xx = gx - gw / 2 + gw * t
                    k.aabb("SignDark", xx - 0.04, xx + 0.04, 0.45, 2.75, -REVEAL + 0.03, -REVEAL + 0.1)
                k.aabb("SignDark", gx - gw / 2, gx + gw / 2, 2.2, 2.26, -REVEAL + 0.03, -REVEAL + 0.1)
                k.aabb("SignDark", gx - gw / 2, gx + gw / 2, 0.45, 0.6, -REVEAL + 0.03, -REVEAL + 0.1)
            # door: glazed timber shop door in a stone frame
            surround(k, dx, 0.0, 1.1, 2.55, sill=False)
            k.aabb("Wood", dx - 0.55, dx + 0.55, 0.0, 2.55, -REVEAL, -REVEAL + 0.06)
            k.aabb("Glass", dx - 0.4, dx + 0.4, 1.0, 2.35, -REVEAL + 0.06, -REVEAL + 0.08)
            k.aabb("Travertine", dx - 0.7, dx + 0.7, -0.1, 0.03, -REVEAL, 0.25)             # threshold step
            # awning (tenda) over the window, sloped out over the pavement, with valance
            aw = p.get("awning", "AwningCream")
            ax0, ax1 = gx - gw / 2 - 0.15, gx + gw / 2 + 0.15
            k.hexa(aw, [(ax0, 3.05, 0.02), (ax1, 3.05, 0.02), (ax1, 2.55, 1.15), (ax0, 2.55, 1.15)], (0, 0.03, 0))
            k.aabb(aw, ax0, ax1, 2.32, 2.58, 1.12, 1.16)
            if lod == 0:
                for s in (ax0 + 0.05, ax1 - 0.05):
                    k.beam("Iron", (s, 2.95, 0.04), (s, 2.55, 1.12), 0.025, 0.025)
                k.aabb("Iron", ax0, ax1, 3.02, 3.08, 0.0, 0.08)
            # sign board with 3D lettering on the string-course zone
            sgn = p.get("sign")
            if sgn:
                if not p.get("long_balcony"):   # the long balcony's corbels own that zone
                    k.aabb("SignDark", gx - gw / 2, gx + gw / 2, 3.1, 3.38, 0.0, 0.04)
                    if lod == 0:
                        k.text("SignGold", sgn, (gx, 3.24, 0.04), 0.18, 0.02, gw - 0.3)
                if lod == 0:   # lettering on the awning valance too (readable from the saddle)
                    k.text("AwningCream" if aw != "AwningCream" else "SignDark", sgn, (gx, 2.45, 1.16), 0.15, 0.012, gw - 0.4)
            # wall lantern by the door
            if lod == 0:
                k.aabb("Iron", dx + 0.75, dx + 0.79, 2.7, 2.74, 0.0, 0.32)
                k.cyl("Iron", (dx + 0.77, 2.38, 0.32), 0.09, 0.12, 0.32, n=6)
                k.cyl("Glass", (dx + 0.77, 2.42, 0.32), 0.075, 0.1, 0.26, n=6)
        else:
            k.aabb("Glass", gx - gw / 2, gx + gw / 2, 0.45, 2.75, 0.0, 0.02)
            k.aabb("Wood", dx - 0.55, dx + 0.55, 0.0, 2.55, 0.0, 0.02)
            aw = p.get("awning", "AwningCream")
            k.hexa(aw, [(gx - gw / 2, 3.05, 0.02), (gx + gw / 2, 3.05, 0.02), (gx + gw / 2, 2.55, 1.15), (gx - gw / 2, 2.55, 1.15)], (0, 0.03, 0))
    elif ground == "door":
        holes = [(-0.75, 0.75, 0.0, 2.9)]
        wx = [-W / 2 + 1.1, W / 2 - 1.1]
        for x in wx:
            holes.append((x - 0.45, x + 0.45, 1.2, 2.5))
        if lod < 2:
            facade_band(k, wall, W, 0.45, GROUND_H - 0.1, holes)
            # portone: rusticated stone frame + twin-leaf panelled door
            k.aabb("Travertine", -0.95, -0.75, 0.0, 3.1, 0.0, 0.06)
            k.aabb("Travertine", 0.75, 0.95, 0.0, 3.1, 0.0, 0.06)
            k.aabb("Travertine", -0.95, 0.95, 2.9, 3.25, 0.0, 0.07)
            k.aabb("Wood", -0.75, 0.75, 0.0, 2.9, -REVEAL, -REVEAL + 0.08)
            if lod == 0:
                for s in (-1, 1):
                    for yy in (0.3, 1.25):
                        k.aabb("Wood", s * 0.37 - 0.25, s * 0.37 + 0.25, yy, yy + 0.8, -REVEAL + 0.08, -REVEAL + 0.11)
                    k.aabb("SignGold", s * 0.06 - 0.02, s * 0.06 + 0.02, 1.25, 1.4, -REVEAL + 0.08, -REVEAL + 0.14)
                k.aabb("Glass", -0.6, 0.6, 2.45, 2.82, -REVEAL + 0.08, -REVEAL + 0.1)  # fanlight
                for i in range(1, 6):
                    xx = -0.6 + i * 0.2
                    k.aabb("Iron", xx - 0.01, xx + 0.01, 2.45, 2.82, -REVEAL + 0.1, -REVEAL + 0.12)
            k.aabb("Travertine", -1.0, 1.0, -0.12, 0.05, -REVEAL, 0.32)
            for x in wx:
                surround(k, x, 1.2, 0.9, 1.3)
                glazing(k, x, 1.2, 0.9, 1.3)
                # inferriata: iron grille
                n = 5 if lod == 0 else 3
                for i in range(n):
                    xx = x - 0.45 + (i + 0.5) * 0.9 / n
                    k.aabb("Iron", xx - 0.012, xx + 0.012, 1.2, 2.5, -0.06, -0.035)
                for yy in (1.5, 2.2):
                    k.aabb("Iron", x - 0.45, x + 0.45, yy - 0.012, yy + 0.012, -0.06, -0.035)
            if lod == 0:
                # house number plaque + a pair of potted plants either side of the door
                k.aabb("Travertine", 1.05, 1.3, 2.3, 2.5, 0.0, 0.025)
                for s in (-1, 1):
                    k.cyl("Terracotta", (s * 1.25, 0.0, 0.35), 0.2, 0.26, 0.5, n=10)
                    k.blob("Leaf", (s * 1.25, 0.85, 0.35), 0.3, 1.2, seg=7, rings=4)
        else:
            k.aabb("Wood", -0.75, 0.75, 0.0, 2.9, 0.0, 0.02)
            for x in wx:
                k.aabb("Glass", x - 0.45, x + 0.45, 1.2, 2.5, 0.0, 0.02)
    elif ground == "arcade":
        bays = 3
        pier = 0.55
        span = (W - pier * (bays + 1)) / bays
        r = span / 2
        ys = GROUND_H - 0.25 - r - 0.18
        ys = max(ys, 1.9)
        if lod < 2:
            xcur = -W / 2
            for b in range(bays):
                k.aabb(wall, xcur, xcur + pier, 0.0, GROUND_H - 0.1, -0.55, 0.0)            # pier
                if lod == 0:
                    k.aabb("Travertine", xcur - 0.03, xcur + pier + 0.03, ys - 0.14, ys, -0.58, 0.03)  # impost
                    k.aabb("Travertine", xcur - 0.02, xcur + pier + 0.02, 0.0, 0.3, -0.57, 0.02)       # pier base
                cx = xcur + pier + r
                arch_front(k, wall, cx, r, ys, GROUND_H - 0.1, -0.55, 0.0, ring=0.18, n=10 if lod == 0 else 6)
                xcur += pier + span
            k.aabb(wall, xcur, W / 2, 0.0, GROUND_H - 0.1, -0.55, 0.0)
            if lod == 0:
                k.aabb("Travertine", xcur - 0.03, W / 2 + 0.0, ys - 0.14, ys, -0.58, 0.03)
                k.aabb("Travertine", xcur - 0.02, W / 2, 0.0, 0.3, -0.57, 0.02)
            # the portico ceiling above the arches (between the front wall and the back wall)
            k.aabb(wall, -W / 2, W / 2, GROUND_H - 0.1, GROUND_H, -arcade_depth, 0.0)
            # end walls closing the portico at the party walls
            k.aabb(wall, -W / 2, -W / 2 + 0.25, 0.0, GROUND_H - 0.1, -arcade_depth, -0.55)
            k.aabb(wall, W / 2 - 0.25, W / 2, 0.0, GROUND_H - 0.1, -arcade_depth, -0.55)
            # back wall: shop windows + doors in the shade of the portico
            zb = -arcade_depth
            xcur = -W / 2 + pier
            for b in range(bays):
                cx = xcur + r
                if b % 2 == 0:
                    k.aabb("Glass", cx - r * 0.7, cx + r * 0.7, 0.5, 2.6, zb, zb + 0.04)
                    if lod == 0:
                        k.aabb("Frame", cx - r * 0.7, cx + r * 0.7, 2.6, 2.7, zb, zb + 0.06)
                        k.aabb("Frame", cx - 0.03, cx + 0.03, 0.5, 2.6, zb, zb + 0.06)
                        k.aabb("SignDark", cx - r * 0.6, cx + r * 0.6, 2.85, 3.1, zb, zb + 0.04)
                else:
                    k.aabb("Wood", cx - 0.6, cx + 0.6, 0.0, 2.6, zb, zb + 0.06)
                    k.aabb("Travertine", cx - 0.72, cx + 0.72, 2.6, 2.75, zb, zb + 0.08)
                xcur += pier + span
            if lod == 0:
                # hanging lantern in the middle bay + a bench against the back wall
                cx = -W / 2 + pier + r + (pier + span)
                k.aabb("Iron", cx - 0.01, cx + 0.01, GROUND_H - 0.9, GROUND_H - 0.1, -1.2, -1.18)
                k.cyl("Iron", (cx, GROUND_H - 1.25, -1.19), 0.1, 0.13, 0.35, n=6)
                k.cyl("Glass", (cx, GROUND_H - 1.21, -1.19), 0.085, 0.11, 0.28, n=6)
                k.aabb("Wood", -W / 2 + 0.6, -W / 2 + 2.0, 0.42, 0.47, zb + 0.05, zb + 0.45)
                for xx in (-W / 2 + 0.7, -W / 2 + 1.9):
                    k.aabb("Iron", xx - 0.02, xx + 0.02, 0.0, 0.42, zb + 0.1, zb + 0.4)
        else:
            xcur = -W / 2 + pier
            for b in range(bays):
                k.aabb("Interior", xcur, xcur + span, 0.0, ys + r * 0.7, 0.0, 0.02)
                xcur += pier + span
            k.aabb("Travertine", -W / 2, W / 2, GROUND_H - 0.1, GROUND_H + 0.06, 0.0, 0.1)

    # ---- roof -----------------------------------------------------------------------------
    chim = (W / 2 - 1.2) if (L["rng_seed"] % 2) else (-W / 2 + 1.2)
    if lod < 2:
        roof(k, W, D, H, rng, chim)
    else:
        ye = H + 0.2
        t = math.tan(math.radians(ROOF_PITCH))
        yr = ye + (EAVE_FRONT + D / 2) * t
        x0, x1 = -W / 2 - EAVE_SIDE, W / 2 + EAVE_SIDE
        for za in (EAVE_FRONT, -D - EAVE_FRONT):
            k.hexa("Coppi", [(x0, ye, za), (x1, ye, za), (x1, yr, -D / 2), (x0, yr, -D / 2)], (0, 0.12, 0))
        for s in (-1, 1):
            xg = s * W / 2
            k.tri_prism(wall, [(xg, H, 0.0), (xg, H, -D), (xg, yr, -D / 2)], (-s * 0.3, 0, 0))
        k.aabb(wall, -W / 2, W / 2, H, H + 0.2, -D, 0.0)
        k.aabb(wall, chim - 0.32, chim + 0.32, yr - 0.6, yr + 1.1, -D * 0.62 - 0.3, -D * 0.62 + 0.3)

    return k.build(name)


# ============================================================================ textures

def _save_png(name, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    px = np.ones((h, w, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px.ravel())
    path = os.path.join(TEXTURES, name + ".png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)
    print(f"[fuji-town] texture {name}.png {w}x{h}")


def _normal_from_height(h, strength):
    """Tangent-space normal (OpenGL / Unity: +G = +v). h is periodic, indexed [v, u]."""
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    nx, ny = -dx * strength, -dy * strength
    nz = np.ones_like(h)
    L = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / L, ny / L, nz / L], axis=-1) * 0.5 + 0.5


def _grid(res):
    v, u = np.mgrid[0:res, 0:res].astype(np.float64) / res
    return u, v


def _smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def _cells(u, v, rows, rng, lmin, lmax, stagger=True):
    """Random-coursed cell field (periodic in u and v). Returns (edge distance in tile units,
    cell id, row index)."""
    cum = np.concatenate([[0.0], np.cumsum(rows)])
    cum = cum / cum[-1]
    ri = np.clip(np.searchsorted(cum, v, side="right") - 1, 0, len(rows) - 1)
    r0, r1 = cum[ri], cum[ri + 1]
    dy = np.minimum(v - r0, r1 - v)
    edge = np.full(u.shape, 1.0)
    cid = np.zeros(u.shape, dtype=np.int64)
    for r in range(len(rows)):
        lens = []
        tot = 0.0
        while tot < 1.0:
            L = rng.uniform(lmin, lmax)
            lens.append(L)
            tot += L
        lens = np.array(lens) / tot
        c = np.concatenate([[0.0], np.cumsum(lens)])
        off = rng.random() if stagger else 0.0
        m = ri == r
        uu = (u[m] + off) % 1.0
        ci = np.clip(np.searchsorted(c, uu, side="right") - 1, 0, len(lens) - 1)
        dx = np.minimum(uu - c[ci], c[ci + 1] - uu)
        edge[m] = np.minimum(dx, dy[m])
        cid[m] = r * 1000 + ci
    return edge, cid, ri


def _cell_rand(cid, seed):
    rng = np.random.default_rng(seed)
    uniq, inv = np.unique(cid, return_inverse=True)
    vals = rng.random(len(uniq))
    return vals[inv].reshape(cid.shape)


def build_textures():
    res = TEX_RES
    u, v = _grid(res)
    rng = np.random.default_rng(5)

    # --- Stucco: warm lime render, sun-bleached mottling, rain streaks, a few spalled patches
    #     showing the stone behind. Neutral-light so Unity's per-house _Color tints it. (4 m tile)
    n1 = S.fbm_2d(u, v, 5.0, 5, seed=21, tile=1.0)
    n2 = S.fbm_2d(u, v, 48.0, 3, seed=22, tile=1.0)
    streak = S.fbm_2d(u * 1.0, v * 0.12, 60.0, 3, seed=23, tile=1.0)
    spall = S.fbm_2d(u, v, 3.0, 5, seed=24, tile=1.0)
    patch = _smooth(spall, 0.745, 0.765) * 0.85   # rare, soft-edged (was leopard-spotted)
    lum = 0.93 + 0.10 * (n1 - 0.5) + 0.05 * (n2 - 0.5) - 0.10 * _smooth(streak, 0.55, 0.8)
    stucco = lum[..., None] * np.array([0.96, 0.91, 0.84])
    stone = (0.55 + 0.25 * S.fbm_2d(u, v, 90.0, 3, seed=25, tile=1.0))[..., None] * np.array([0.86, 0.78, 0.66])
    alb = stucco * (1 - patch[..., None]) + stone * patch[..., None]
    _save_png("Fuji_Town_Stucco_Albedo", alb)
    h = 0.6 * n2 + 0.25 * n1 - 1.4 * patch + 0.3 * patch * S.fbm_2d(u, v, 120.0, 2, seed=26, tile=1.0)
    _save_png("Fuji_Town_Stucco_Normal", _normal_from_height(h, 16.0))

    # --- Masonry: random-coursed limestone rubble with recessed lime mortar (2 m tile)
    rows = rng.uniform(0.7, 1.3, 8)
    wu = u + 0.012 * (S.fbm_2d(u, v, 12.0, 3, seed=31, tile=1.0) - 0.5)
    wv = v + 0.012 * (S.fbm_2d(u, v, 12.0, 3, seed=32, tile=1.0) - 0.5)
    edge, cid, _ = _cells(wu % 1.0, wv % 1.0, rows, rng, 0.11, 0.30)
    tone = _cell_rand(cid, 33)
    tone2 = _cell_rand(cid, 34)
    mortar = 1.0 - _smooth(edge, 0.004, 0.011)
    dome = _smooth(edge, 0.004, 0.035)
    grain = S.fbm_2d(u, v, 70.0, 4, seed=35, tile=1.0)
    base = np.stack([0.66 + 0.18 * tone, 0.60 + 0.15 * tone, 0.50 + 0.12 * tone], -1)
    base = base * (1 + 0.10 * (tone2[..., None] - 0.5) * np.array([1.0, 0.2, -0.6]))
    base = base * (0.82 + 0.3 * grain[..., None])
    mort = np.array([0.80, 0.76, 0.68]) * (0.85 + 0.2 * grain[..., None])
    alb = base * (1 - mortar[..., None]) + mort * mortar[..., None]
    _save_png("Fuji_Town_Masonry_Albedo", alb)
    h = dome * (0.9 + 0.4 * grain) - 0.3 * mortar
    _save_png("Fuji_Town_Masonry_Normal", _normal_from_height(h, 60.0))

    # --- Coppi: Italian barrel tiles. u = across the slope (8 channels / 1.2 m tile), v = up the
    #     slope (3 courses). Over-tiles (cappi) sit on the joints between under-tiles (canali).
    ch = u * 8.0
    fc = ch - np.floor(ch)
    over = np.abs(np.cos(fc * math.pi))            # 1 at the joints (cappo crowns), 0 mid-channel
    course = v * 3.0
    ci = np.floor(course)
    cf = course - ci
    lap = _smooth(cf, 0.0, 0.18) * (1.0 - 0.35 * _smooth(cf, 0.8, 1.0))   # shadow at each course lap
    colid = (np.floor(ch + 0.5) % 8) * 10 + ci
    tone = _cell_rand(colid.astype(np.int64), 41)
    lich = _smooth(S.fbm_2d(u, v, 22.0, 4, seed=42, tile=1.0), 0.64, 0.74)
    grain = S.fbm_2d(u, v, 90.0, 3, seed=43, tile=1.0)
    tile_c = np.stack([0.70 + 0.14 * tone, 0.36 + 0.10 * tone, 0.22 + 0.05 * tone], -1)
    tile_c = tile_c * (0.62 + 0.38 * over[..., None]) * (0.55 + 0.45 * lap[..., None]) * (0.9 + 0.2 * grain[..., None])
    tile_c = tile_c * (1 - 0.6 * lich[..., None]) + np.array([0.62, 0.60, 0.46]) * 0.6 * lich[..., None]
    _save_png("Fuji_Town_Coppi_Albedo", tile_c)
    h = over * 1.0 + 0.35 * lap + 0.05 * grain
    _save_png("Fuji_Town_Coppi_Normal", _normal_from_height(h, 24.0))

    # --- Travertine: pale cream cut stone with horizontal pitted veins (1 m tile)
    vein = S.fbm_2d(u * 0.25, v * 3.0, 24.0, 4, seed=51, tile=1.0)
    pits = _smooth(S.fbm_2d(u, v, 140.0, 2, seed=52, tile=1.0), 0.72, 0.8)
    lum = 0.86 + 0.10 * (vein - 0.5) - 0.18 * pits
    alb = lum[..., None] * np.array([0.97, 0.92, 0.82])
    _save_png("Fuji_Town_Travertine_Albedo", alb)
    _save_png("Fuji_Town_Travertine_Normal", _normal_from_height(0.4 * vein - 0.8 * pits, 20.0))

    # --- Louvre: persiana slats, 16 per metre along v, near-white so the role colour tints it
    sl = v * 16.0
    sf = sl - np.floor(sl)
    slat = 0.62 + 0.38 * _smooth(sf, 0.05, 0.55) - 0.35 * _smooth(sf, 0.85, 1.0)
    wear = S.fbm_2d(u, v, 30.0, 4, seed=61, tile=1.0)
    alb = (slat * (0.88 + 0.18 * wear))[..., None] * np.array([1.0, 1.0, 0.98])
    _save_png("Fuji_Town_Louvre_Albedo", alb)
    _save_png("Fuji_Town_Louvre_Normal", _normal_from_height(sf * (sf < 0.85) * 1.0, 18.0))

    # --- Wood: vertical planks, weathered walnut (1 m tile, 7 planks)
    pk = u * 7.0
    pf = pk - np.floor(pk)
    ptone = _cell_rand(np.floor(pk).astype(np.int64), 71)
    grain = S.fbm_2d(u * 12.0, v * 0.6, 8.0, 5, seed=72, tile=1.0)
    gap = 1.0 - _smooth(np.minimum(pf, 1 - pf), 0.0, 0.05)
    lum = (0.75 + 0.2 * ptone) * (0.75 + 0.35 * grain) * (1 - 0.7 * gap)
    alb = lum[..., None] * np.array([0.42, 0.28, 0.17])
    _save_png("Fuji_Town_Wood_Albedo", alb)
    _save_png("Fuji_Town_Wood_Normal", _normal_from_height(0.4 * grain - gap, 14.0))


# ============================================================================ preview (EEVEE)

def _tex_materials():
    """Swap every Fuji_Town_* material for an albedo x tint + normal-map node tree (preview only,
    AFTER export, so the GLBs themselves stay texture-free and Unity supplies the maps)."""
    for role, (col, rough, stem, _) in ROLES.items():
        m = _MATS.get(role)
        if m is None or stem is None:
            continue
        nt = m.node_tree
        bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
        alb = nt.nodes.new("ShaderNodeTexImage")
        alb.image = bpy.data.images.load(os.path.join(TEXTURES, f"Fuji_Town_{stem}_Albedo.png"), check_existing=True)
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = 'RGBA'
        mix.blend_type = 'MULTIPLY'
        mix.inputs[0].default_value = 1.0
        nt.links.new(alb.outputs["Color"], mix.inputs[6])
        mix.inputs[7].default_value = (col[0], col[1], col[2], 1.0)
        nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
        nrm = nt.nodes.new("ShaderNodeTexImage")
        nrm.image = bpy.data.images.load(os.path.join(TEXTURES, f"Fuji_Town_{stem}_Normal.png"), check_existing=True)
        nrm.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = 1.0
        nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])


def _setup_scene():
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    vts = [v.identifier for v in sc.view_settings.bl_rna.properties["view_transform"].enum_items]
    sc.view_settings.view_transform = "AgX" if "AgX" in vts else "Filmic"
    sc.render.resolution_x, sc.render.resolution_y = 1500, 900
    try:
        sc.eevee.use_shadows = True
        sc.eevee.taa_render_samples = 32
    except Exception:
        pass
    w = bpy.data.worlds.new("w")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.52, 0.66, 0.86, 1)
    bg.inputs[1].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.2
    sun.data.color = (1.0, 0.94, 0.84)
    sun.data.angle = math.radians(1.5)
    # late-morning Tuscan sun from the street side, raking across the facades
    sun.rotation_euler = (math.radians(52), 0, math.radians(-150))
    S.link(sun)
    # street: a pavement plane in front of the row (Unity y=0 plane)
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -0.01))
    gp = bpy.context.active_object
    gm = S.pbr_material("preview_paving", base_color=(0.46, 0.43, 0.39, 1), roughness=0.9)
    gp.data.materials.append(gm)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    S.link(cam)
    sc.camera = cam
    return sc, cam


def _shoot(sc, cam, eye_u, tgt_u, lens, fname):
    """Camera from Unity-space eye/target."""
    eye = Vector(S.u2b(*eye_u))
    tgt = Vector(S.u2b(*tgt_u))
    cam.location = eye
    cam.rotation_euler = (tgt - eye).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    cam.data.clip_end = 1000
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    sc.render.filepath = os.path.join(PREVIEW_DIR, fname)
    bpy.ops.render.render(write_still=True)
    print(f"[fuji-town] preview {fname}")


def previews(built):
    _tex_materials()
    sc, cam = _setup_scene()
    order = list(VARIANTS.keys())
    # --- 1. a street row of every LOD0, as they will stand on the town street
    for ob, nm, lod in built:
        ob.hide_render = True
    x = 0.0
    row = {}
    for nm in order:
        ob = next(o for o, n, l in built if n == nm and l == 0)
        W = VARIANTS[nm]["W"]
        cx = x + W / 2
        bx, by, bz = S.u2b(cx, 0.0, 0.0)
        ob.location = (bx, by, bz)
        ob.hide_render = False
        row[nm] = cx
        x += W + 0.1
    L = x
    _shoot(sc, cam, (L * 0.05, 1.7, 11.0), (L * 0.55, 5.0, -2.0), 24, "e1_street_oblique.png")
    _shoot(sc, cam, (L * 0.5, 6.0, 30.0), (L * 0.5, 6.0, 0.0), 32, "e1_street_front.png")
    _shoot(sc, cam, (L * 0.95, 1.6, 7.0), (L * 0.45, 4.0, -1.0), 22, "e1_street_rideview.png")
    # --- 2. close hero shots of each variant
    for nm in order:
        cx = row[nm]
        W = VARIANTS[nm]["W"]
        _shoot(sc, cam, (cx + W * 0.7, 2.2, 9.0), (cx, 4.5, -1.0), 26, f"e1_{nm}_q34.png")
    # --- 3. LOD0 / LOD1 / LOD2 of two variants side by side (as seen from 25 m)
    for ob, nm, lod in built:
        ob.hide_render = True
    for vi, nm in enumerate(("CasaBottega", "CasaPortico")):
        W = VARIANTS[nm]["W"]
        for lod in (0, 1, 2):
            ob = next(o for o, n, l in built if n == nm and l == lod)
            cx = lod * (W + 2.0)
            ob.location = S.u2b(cx, 0.0, -40.0 * (vi + 1))
            ob.hide_render = False
        _shoot(sc, cam, ((W + 2.0), 5.0, -40.0 * (vi + 1) + 26.0), ((W + 2.0), 5.0, -40.0 * (vi + 1)), 35, f"e1_lods_{nm}.png")
        for lod in (0, 1, 2):
            next(o for o, n, l in built if n == nm and l == lod).hide_render = True


# ============================================================================ main

def export(ob, fname):
    S.report(fname, ob)
    S.export_glb([ob], fname)


def main():
    S.reset_scene()
    if not NO_TEX:
        build_textures()
    built = []
    for nm, p in VARIANTS.items():
        for lod in (0, 1, 2):
            full = f"Fuji_Town_{nm}_LOD{lod}"
            ob = house(full, lod, p)
            export(ob, full + ".glb")
            built.append((ob, nm, lod))
    if PREVIEW:
        previews(built)
    print("[fuji-town] done")


main()

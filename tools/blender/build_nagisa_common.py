"""
NAGISA BAY (B5) - shared Blender 4.5 helpers for every build_nagisa_*.py asset script.

AUTHORING CONVENTION (mapleride-environment-assets invariant): all geometry is written in
UNITY coordinates (x right, y up, z forward; metres) and converted with sakura_lib.u2b =
(-x, -z, y) exactly once, in Geo.to_object(). u2b is a reflection (det = -1), so the face
loop order is reversed at the same time; a face authored counter-clockwise when seen from
outside (right-hand rule in Unity coords) therefore exports with an outward normal and the
glTF importer's own handedness flip brings it back correct in Unity.

MATERIALS are named slots only ("NB_Stone", "NB_Glass", ...). NagisaBayEnvironment.Resort.cs
remaps each renderer's material by that name to a CelLit / HDRP-Lit asset built from the
PBR textures in Assets/Environment/NagisaBay/Textures, so the GLBs carry no images.

LODS are authored, not decimated: each asset builder takes a detail level 0/1/2 and the
three results are exported as <obj>_LOD0/_LOD1/_LOD2 siblings in one GLB (the
MinatoCoastEnvironment.Inst() naming contract).
"""
import math
import os
import sys

import bpy
import bmesh

sys.path.insert(0, os.path.dirname(__file__))
from sakura_lib import u2b  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Models")

# Texture tile size in metres (u, v) per material slot. Must agree with the texture authoring
# in build_nagisa_textures.py. PROVISIONAL art values.
TILE = {
    "NB_Stone": (2.4, 2.4), "NB_StoneDark": (2.4, 2.4), "NB_Glass": (7.2, 12.8),
    "NB_BalconyGlass": (2.0, 2.0), "NB_Teak": (1.12, 1.12), "NB_Bronze": (1.0, 1.0),
    "NB_Plaster": (3.0, 3.0), "NB_PlasterPink": (3.0, 3.0), "NB_PlasterMint": (3.0, 3.0),
    "NB_PlasterYellow": (3.0, 3.0), "NB_PlasterBlue": (3.0, 3.0), "NB_PlasterCoral": (3.0, 3.0),
    "NB_RoofTile": (2.4, 2.4), "NB_DeckStone": (2.4, 2.4), "NB_PoolTile": (2.0, 2.0),
    "NB_PoolWater": (8.0, 8.0), "NB_Canvas": (2.0, 2.0), "NB_CanvasStripe": (2.0, 2.0),
    "NB_Gelcoat": (1.0, 1.0), "NB_PontoonWood": (1.12, 1.12), "NB_PalmBark": (1.0, 0.6),
    "NB_PalmFrond": (1.0, 1.0), "NB_FanFrond": (1.0, 1.0), "NB_Hibiscus": (1.0, 1.0),
    "NB_Bougainvillea": (1.0, 1.0), "NB_TownWindows": (6.0, 6.4), "NB_Signs": (1.0, 1.0),
    "NB_Superstructure": (1.0, 1.0), "NB_Paint": (1.0, 1.0), "NB_PaintTeal": (1.0, 1.0),
    "NB_Metal": (1.0, 1.0), "NB_Rubber": (1.0, 1.0), "NB_Planter": (2.4, 2.4),
    "NB_Soil": (2.0, 2.0), "NB_Lamp": (1.0, 1.0), "NB_Concrete": (3.0, 3.0),
}


def v3(x, y, z):
    return (float(x), float(y), float(z))


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def mul(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(a):
    l = math.sqrt(a[0] ** 2 + a[1] ** 2 + a[2] ** 2) or 1.0
    return (a[0] / l, a[1] / l, a[2] / l)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def rot_y(p, ang):
    c, s = math.cos(ang), math.sin(ang)
    return (p[0] * c + p[2] * s, p[1], -p[0] * s + p[2] * c)


class Geo:
    """Unity-space mesh accumulator: verts, faces (loops), per-loop UVs, per-face material."""

    def __init__(self):
        self.verts = []
        self.faces = []      # list of (vertex index tuple, uv tuple, material name)

    # -- low level ------------------------------------------------------------------------
    def face(self, pts, mat, uvs=None):
        """pts in Unity coords, counter-clockwise seen from the FRONT side (right-hand rule
        normal = front). uvs: explicit per-point UVs, else planar-projected by TILE."""
        base = len(self.verts)
        self.verts.extend(pts)
        if uvs is None:
            uvs = self.planar_uv(pts, mat)
        self.faces.append((tuple(range(base, base + len(pts))), tuple(uvs), mat))

    def planar_uv(self, pts, mat):
        n = norm(cross(sub(pts[1], pts[0]), sub(pts[2], pts[0])))
        tu, tv = TILE.get(mat, (1.0, 1.0))
        if abs(n[1]) > 0.7:                       # horizontal face: world XZ
            return [(p[0] / tu, p[2] / tv) for p in pts]
        # vertical-ish: u along the horizontal tangent, v = height
        t = norm((n[2], 0.0, -n[0]))
        return [((p[0] * t[0] + p[2] * t[2]) / tu, p[1] / tv) for p in pts]

    def quad(self, a, b, c, d, mat, uvs=None, double=False):
        self.face([a, b, c, d], mat, uvs)
        if double:
            self.face([d, c, b, a], mat, None if uvs is None else [uvs[3], uvs[2], uvs[1], uvs[0]])

    def tri(self, a, b, c, mat, uvs=None):
        self.face([a, b, c], mat, uvs)

    # -- primitives ------------------------------------------------------------------------
    def box(self, c, size, mat, yaw=0.0, top=True, bottom=False, mats=None):
        """Axis box centred at c (size = full extents), optional yaw about +y (radians).
        mats: optional dict {'top','bottom','side'} overriding the slot per face group."""
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        loc = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz),
               (-hx, hy, -hz), (hx, hy, -hz), (hx, hy, hz), (-hx, hy, hz)]
        P = [add(c, rot_y(p, yaw)) for p in loc]
        ms = mats or {}
        side = ms.get("side", mat)
        # faces CCW from outside
        self.quad(P[0], P[4], P[5], P[1], side)   # -z
        self.quad(P[1], P[5], P[6], P[2], side)   # +x
        self.quad(P[2], P[6], P[7], P[3], side)   # +z
        self.quad(P[3], P[7], P[4], P[0], side)   # -x
        if top:
            self.quad(P[4], P[7], P[6], P[5], ms.get("top", mat))
        if bottom:
            self.quad(P[0], P[1], P[2], P[3], ms.get("bottom", mat))

    def cylinder(self, c, r, h, seg, mat, top=True, bottom=False, r_top=None, v_tile=None):
        rt = r if r_top is None else r_top
        tv = v_tile or TILE.get(mat, (1, 1))[1]
        ring0, ring1 = [], []
        for i in range(seg + 1):
            a = i / seg * math.tau
            ring0.append(add(c, (r * math.cos(a), 0, r * math.sin(a))))
            ring1.append(add(c, (rt * math.cos(a), h, rt * math.sin(a))))
        for i in range(seg):
            u0, u1 = i / seg, (i + 1) / seg
            # CCW from outside: bottom i -> bottom i+1 is anticlockwise seen from above (+y),
            # so seen from outside the face order is (b_i, t_i, t_i+1, b_i+1).
            self.quad(ring0[i], ring1[i], ring1[i + 1], ring0[i + 1], mat,
                      uvs=[(u0, 0), (u0, h / tv), (u1, h / tv), (u1, 0)])
        if top:
            self.face([ring1[i] for i in range(seg, 0, -1)], mat)
        if bottom:
            self.face([ring0[i] for i in range(seg)], mat)

    def prism(self, poly, y0, y1, mat, top=True, bottom=False, mats=None):
        """Extrude a closed XZ polygon (list of (x, z), counter-clockwise seen from +y)."""
        ms = mats or {}
        n = len(poly)
        side = ms.get("side", mat)
        for i in range(n):
            a, b = poly[i], poly[(i + 1) % n]
            A0, B0 = (a[0], y0, a[1]), (b[0], y0, b[1])
            A1, B1 = (a[0], y1, a[1]), (b[0], y1, b[1])
            self.quad(A0, A1, B1, B0, side)
        if top:
            self.face([(p[0], y1, p[1]) for p in reversed(poly)], ms.get("top", mat))
        if bottom:
            self.face([(p[0], y0, p[1]) for p in poly], ms.get("bottom", mat))

    def ribbon(self, pts, y0, y1, mat, double=False, u_tile=None, v_tile=None, outward=True):
        """Vertical wall through a polyline of (x, z). Front = left of travel direction when
        outward=True (i.e. for a CCW polygon, the outside)."""
        tu, tv = TILE.get(mat, (1, 1))
        tu = u_tile or tu
        tv = v_tile or tv
        s = 0.0
        for i in range(len(pts) - 1):
            a, b = pts[i], pts[i + 1]
            L = math.hypot(b[0] - a[0], b[1] - a[1])
            A0, B0 = (a[0], y0, a[1]), (b[0], y0, b[1])
            A1, B1 = (a[0], y1, a[1]), (b[0], y1, b[1])
            uv = [(s / tu, y0 / tv), (s / tu, y1 / tv), ((s + L) / tu, y1 / tv), ((s + L) / tu, y0 / tv)]
            if outward:
                self.quad(A0, A1, B1, B0, mat, uvs=uv, double=double)
            else:
                self.quad(B0, B1, A1, A0, mat, uvs=[uv[3], uv[2], uv[1], uv[0]], double=double)
            s += L

    def slab(self, outer, inner, y0, y1, mat, mats=None):
        """Horizontal band between two matching polylines (outer = the free edge): top,
        bottom and the outer edge face. Both lists (x, z), same length, open strip."""
        ms = mats or {}
        n = len(outer)
        for i in range(n - 1):
            o0, o1, i0, i1 = outer[i], outer[i + 1], inner[i], inner[i + 1]
            # top (normal +y): order must be CCW seen from above
            T = [(i0[0], y1, i0[1]), (o0[0], y1, o0[1]), (o1[0], y1, o1[1]), (i1[0], y1, i1[1])]
            if dot(cross(sub(T[1], T[0]), sub(T[2], T[0])), (0, 1, 0)) < 0:
                T.reverse()
            self.face(T, ms.get("top", mat))
            B = [(p[0], y0, p[2]) for p in reversed(T)]
            self.face(B, ms.get("bottom", mat))
        self.ribbon(outer, y0, y1, ms.get("edge", mat), outward=_is_left_outward(outer, inner))

    # -- transforms --------------------------------------------------------------------------
    def merge(self, other, offset=(0, 0, 0), yaw=0.0, scale=1.0):
        base = len(self.verts)
        for p in other.verts:
            q = rot_y(mul(p, scale), yaw)
            self.verts.append(add(q, offset))
        for idx, uvs, m in other.faces:
            self.faces.append((tuple(i + base for i in idx), uvs, m))

    def tris(self):
        return sum(len(f[0]) - 2 for f in self.faces)

    # -- export ------------------------------------------------------------------------------
    def to_object(self, name):
        mats = []
        for _, _, m in self.faces:
            if m not in mats:
                mats.append(m)
        me = bpy.data.meshes.new(name)
        bm = bmesh.new()
        bverts = [bm.verts.new(u2b(*p)) for p in self.verts]
        bm.verts.ensure_lookup_table()
        uvl = bm.loops.layers.uv.new("UVMap")
        for idx, uvs, m in self.faces:
            # u2b mirrors -> reverse loop order to keep the authored front side outward.
            ridx = list(reversed(idx))
            ruv = list(reversed(uvs))
            try:
                f = bm.faces.new([bverts[i] for i in ridx])
            except ValueError:
                continue    # duplicate face (degenerate) - skip
            f.material_index = mats.index(m)
            for loop, uv in zip(f.loops, ruv):
                loop[uvl].uv = uv
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:   # glTF tangents need tris/quads
            bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        for m in mats:
            mat = bpy.data.materials.get(m) or bpy.data.materials.new(m)
            me.materials.append(mat)
        for p in me.polygons:
            p.use_smooth = False
        return ob


def _is_left_outward(outer, inner):
    # the outer polyline's free side is away from inner
    a, b = outer[0], outer[1]
    t = (b[0] - a[0], b[1] - a[1])
    left = (-t[1], t[0])            # left of travel in XZ (x, z) with y up... (see ribbon)
    away = (outer[0][0] - inner[0][0], outer[0][1] - inner[0][1])
    return (left[0] * away[0] + left[1] * away[1]) < 0


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(objs, filename):
    os.makedirs(OUT, exist_ok=True)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.hide_render = False
        o.hide_viewport = False
        o.select_set(True)
    path = os.path.join(OUT, filename)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                              export_apply=False, export_yup=True, export_normals=True,
                              export_tangents=True, export_texcoords=True,
                              export_materials="EXPORT", export_cameras=False,
                              export_lights=False, export_animations=False)
    return path


def build_lods(stem, builder, filename, levels=(0, 1, 2)):
    """builder(detail) -> Geo. Exports <stem>_LOD0.._LOD2 into one GLB; returns tri counts."""
    reset()
    objs, counts = [], []
    for lvl in levels:
        g = builder(lvl)
        counts.append(g.tris())
        objs.append(g.to_object("%s_LOD%d" % (stem, lvl)))
    export(objs, filename)
    print("[nagisa-glb] %-28s LOD tris %s" % (filename, " / ".join("{:,}".format(c) for c in counts)))
    return counts

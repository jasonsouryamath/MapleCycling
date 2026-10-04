"""
mr_kit2.py - shared Blender kit helpers for the E1 (Fuji Ridge) and E2 (Taka Mountains) build-outs.
Claude worker D, 2026-09-30.

Import from a build script run with  blender -b --factory-startup -P tools/blender/build_xxx.py :

    sys.path.insert(0, HERE); import mr_kit2 as K

Everything is authored in UNITY coordinates (x right, y up, z forward); Kit.build converts with
sakura_lib.u2b so the glTF imports into Unity with an identity transform (same convention as
build_fuji_hilltown.py). Each primitive is its own closed island so bmesh recalculates its normals
outward independently.

    Kit(prefix, roles)        roles: role -> (rgb, roughness, metallic, uv_tile_m, smooth)
    k.box/aabb/beam/cyl/hexa/tri_prism/blob/rock/leafy/tube/ring_wall ... (all in Unity space)
    k.build(name, bevel={role: width})   -> joined Blender object, one material slot per role,
                                            box-projected UVs, optional hard-edge bevel on the roles named
    export(ob, filename)

Materials are named <prefix>_<Role>; the Unity staging files re-materialise by that name.
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
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import sakura_lib as S  # noqa: E402


# --------------------------------------------------------------------------- noise (smooth, deterministic)

def vnoise(x, y, z, seed=0.0):
    s = seed * 12.9898
    a = math.sin(x * 1.7 + s) * math.cos(y * 2.3 + s * 1.3)
    b = math.sin(z * 2.9 + x * 1.1 + s * 0.7) * math.cos(y * 1.9 - z * 0.8 + s * 2.1)
    c = math.sin((x + y) * 3.7 + z * 0.9 + s * 1.7) * 0.5
    return (a + b + c) / 2.5


def fbm3(x, y, z, seed=0.0, octaves=3):
    t, amp, f, norm = 0.0, 1.0, 1.0, 0.0
    for o in range(octaves):
        t += vnoise(x * f, y * f, z * f, seed + o * 3.1) * amp
        norm += amp
        amp *= 0.5
        f *= 2.07
    return t / norm


# --------------------------------------------------------------------------- uv

def box_uv(ob, tile, flip_back=False):
    """Per-loop box projection, metres per tile (Blender axes: z is Unity up)."""
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


def Rz(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'Z')


# --------------------------------------------------------------------------- kit

class Kit:
    def __init__(self, prefix, roles):
        self.prefix = prefix
        self.roles = roles
        self.groups = {}
        self.xf = Matrix.Identity(4)
        self.stack = []
        self._mats = {}

    # transform stack (Unity space)
    def push(self, m):
        self.stack.append(self.xf)
        self.xf = self.xf @ m

    def pop(self):
        self.xf = self.stack.pop()

    def material(self, role):
        if role in self._mats:
            return self._mats[role]
        col, rough, metal, _, _ = self.roles[role]
        m = S.pbr_material(f"{self.prefix}_{role}", base_color=(col[0], col[1], col[2], 1.0),
                           roughness=rough, metallic=metal)
        m.diffuse_color = (col[0], col[1], col[2], 1.0)
        self._mats[role] = m
        return m

    def prim(self, role, verts, faces):
        self.groups.setdefault(role, []).append(([self.xf @ Vector(v) for v in verts], faces))

    # ---- primitives -------------------------------------------------------------------------
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

    def cyl(self, role, c, r0, r1, h, n=10, axis=None, cap=True):
        R = axis if axis is not None else Matrix.Identity(3)
        cv = Vector(c)
        vs = []
        for k in range(2):
            r = r0 if k == 0 else r1
            for i in range(n):
                a = i / n * math.tau
                vs.append(cv + R @ Vector((math.cos(a) * r, h * k, math.sin(a) * r)))
        faces = [tuple(range(n)), tuple(range(n, 2 * n))[::-1]] if cap else []
        for i in range(n):
            j = (i + 1) % n
            faces.append((i, n + i, n + j, j))
        self.prim(role, vs, faces)

    def prism_poly(self, role, c, r0, r1, h, n=8, rot_deg=0.0):
        """n-gon frustum (octagonal basins, belfry columns)."""
        cv = Vector(c)
        vs = []
        for k in range(2):
            r = r0 if k == 0 else r1
            for i in range(n):
                a = i / n * math.tau + math.radians(rot_deg)
                vs.append(cv + Vector((math.cos(a) * r, h * k, math.sin(a) * r)))
        faces = [tuple(range(n)), tuple(range(n, 2 * n))[::-1]]
        for i in range(n):
            j = (i + 1) % n
            faces.append((i, n + i, n + j, j))
        self.prim(role, vs, faces)

    def pyramid(self, role, c, hw, hd, h, base_y=None):
        cv = Vector(c)
        vs = [cv + Vector((-hw, 0, -hd)), cv + Vector((hw, 0, -hd)), cv + Vector((hw, 0, hd)),
              cv + Vector((-hw, 0, hd)), cv + Vector((0, h, 0))]
        faces = [(0, 1, 2, 3), (0, 4, 1), (1, 4, 2), (2, 4, 3), (3, 4, 0)]
        self.prim(role, vs, faces)

    def sphere_like(self, role, c, rx, ry, rz, seg, rings, disp=None):
        cv = Vector(c)
        vs = [cv + Vector((0, -ry, 0))]
        for j in range(1, rings):
            ph = -math.pi / 2 + j / rings * math.pi
            for i in range(seg):
                a = i / seg * math.tau
                d = Vector((math.cos(a) * math.cos(ph), math.sin(ph), math.sin(a) * math.cos(ph)))
                s = disp(d) if disp else 1.0
                vs.append(cv + Vector((d.x * rx * s, d.y * ry * s, d.z * rz * s)))
        vs.append(cv + Vector((0, ry, 0)))
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

    def blob(self, role, c, r, sy=1.0, seg=6, rings=4):
        self.sphere_like(role, c, r, r * sy, r, seg, rings)

    def rock(self, role, c, r, seed, flat=0.7, amp=0.28, seg=10, rings=7, ridged=False, sink=0.35):
        """Lumpy boulder: noise-displaced sphere, flattened, bottom clamped (sunk into the ground)."""
        cv = Vector(c)

        def disp(d):
            n = fbm3(d.x * 1.6, d.y * 1.6, d.z * 1.6, seed, 3)
            if ridged:
                n = 1.0 - 2.0 * abs(n)
            return 1.0 + amp * n

        # build then clamp the bottom
        vs = [cv + Vector((0, -r * flat, 0))]
        for j in range(1, rings):
            ph = -math.pi / 2 + j / rings * math.pi
            for i in range(seg):
                a = i / seg * math.tau
                d = Vector((math.cos(a) * math.cos(ph), math.sin(ph), math.sin(a) * math.cos(ph)))
                s = disp(d)
                p = Vector((d.x * r * s, d.y * r * flat * s, d.z * r * s))
                p.y = max(p.y, -r * flat * sink)
                vs.append(cv + p)
        vs.append(cv + Vector((0, r * flat * 1.0, 0)))
        vs[0] = cv + Vector((0, -r * flat * sink, 0))
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

    def leafy(self, role, c, r, seed, sy=0.7, amp=0.22, seg=8, rings=5):
        """Lumpy foliage mass (smooth shaded)."""
        def disp(d):
            return 1.0 + amp * fbm3(d.x * 2.2, d.y * 2.2, d.z * 2.2, seed, 2)
        self.sphere_like(role, c, r, r * sy, r, seg, rings, disp)

    def tube(self, role, pts, radii, n=8, taper_cap=True):
        """Swept tube along a polyline of Unity points with per-point radii (trunks, limbs)."""
        P = [Vector(p) for p in pts]
        m = len(P)
        vs, faces = [], []
        prev_x = None
        for k in range(m):
            t = (P[min(k + 1, m - 1)] - P[max(k - 1, 0)]).normalized()
            up = Vector((0, 1, 0)) if abs(t.y) < 0.95 else Vector((1, 0, 0))
            x = t.cross(up).normalized()
            y = t.cross(x).normalized()
            r = radii[k] if isinstance(radii, (list, tuple)) else radii
            for i in range(n):
                a = i / n * math.tau
                vs.append(P[k] + x * (math.cos(a) * r) + y * (math.sin(a) * r))
        for k in range(m - 1):
            for i in range(n):
                j = (i + 1) % n
                faces.append((k * n + i, (k + 1) * n + i, (k + 1) * n + j, k * n + j))
        faces.append(tuple(range(n))[::-1])
        faces.append(tuple(range((m - 1) * n, m * n)))
        self.prim(role, vs, faces)

    def stones_wall(self, role, x0, x1, y0, y1, z_c, depth, rng, course_h=(0.16, 0.26), len_rng=(0.28, 0.6),
                    jitter=0.025, face_z=0.0, bulge=0.04):
        """Dry-stone / ashlar infill along x between y0..y1 (facing +z): irregular blocks per course."""
        y = y0
        row = 0
        while y < y1 - 0.05:
            ch = min(rng.uniform(*course_h), y1 - y)
            x = x0 - (rng.uniform(0, 0.3) if row % 2 else 0)
            while x < x1:
                lw = rng.uniform(*len_rng)
                xa, xb = max(x, x0), min(x + lw, x1)
                if xb - xa > 0.04:
                    zf = z_c + depth * 0.5 + rng.uniform(-bulge, bulge)
                    zb = z_c - depth * 0.5
                    ja = [rng.uniform(-jitter, jitter) for _ in range(4)]
                    a, b = xa + 0.012, xb - 0.012
                    yy0, yy1 = y + 0.01, y + ch - 0.01
                    self.hexa(role, [(a + ja[0], yy0 + ja[1], zf), (b + ja[2], yy0 + ja[3], zf),
                                     (b - ja[0], yy1 - ja[1], zf), (a - ja[2], yy1 - ja[3], zf)],
                              (0, 0, zb - zf))
                x += lw
            y += ch
            row += 1

    def text(self, role, s, centre, cap_h, depth, max_w):
        """3-D lettering (Unity x-y plane, facing +z) - same convention as build_fuji_hilltown.py."""
        cu = bpy.data.curves.new("txt", type='FONT')
        cu.body = s
        cu.size = cap_h / 0.72
        cu.extrude = depth * 0.5
        cu.resolution_u = 1
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

    # ---- build ------------------------------------------------------------------------------
    def build(self, name, bevel=None, tag="kit"):
        bevel = bevel or {}
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
            ob.data.materials.append(self.material(role))
            smooth = self.roles[role][4]
            for p in ob.data.polygons:
                p.use_smooth = smooth
            if role in bevel:
                S.add_bevel(ob, width=bevel[role], segments=1, angle_deg=40.0)
                S.apply_modifiers(ob)
                bm2 = bmesh.new()
                bm2.from_mesh(ob.data)
                ng = [f for f in bm2.faces if len(f.verts) > 3]
                if ng:
                    bmesh.ops.triangulate(bm2, faces=ng)
                bm2.to_mesh(ob.data)
                bm2.free()
                for p in ob.data.polygons:
                    p.use_smooth = smooth
            box_uv(ob, self.roles[role][3])
            tris += sum(len(p.vertices) - 2 for p in ob.data.polygons)
            objs.append(ob)
        ob = S.join(objs, name)
        print(f"[{tag}] {name}: ~{tris:,} tris, {len(self.groups)} roles")
        return ob


def export(ob, fname):
    S.report(fname, ob)
    S.export_glb([ob], fname)


# --------------------------------------------------------------------------- texture helpers

def save_png(path, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    px = np.ones((h, w, 4), dtype=np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def normal_from_height(h, strength):
    gx = np.roll(h, -1, 1) - np.roll(h, 1, 1)
    gy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    n = np.dstack([-gx * strength, -gy * strength, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n * 0.5 + 0.5


def grid(res):
    u = (np.arange(res) + 0.5) / res
    return np.meshgrid(u, u)


def smoothstep(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

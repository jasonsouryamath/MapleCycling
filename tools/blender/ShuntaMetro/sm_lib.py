"""
SHUNTA METRO city assets - shared Blender 4.5 helpers (vehicles + buildings).

Reuses the Nagisa pipeline (build_nagisa_common.Geo: Unity-space authoring x right / y up / z fwd,
metres, u2b conversion, glTF export) and adds:
  * SGeo   - smooth-shaded lofted shells (car bodies, trains, rounded corners)
  * make_object(name, geos) - one Blender object, several materials, flat + smooth faces
  * MATS   - shared PBR material table (also dumped to materials.json for the Unity builder)
  * export_lods(stem, builder, out_sub): <stem>_LOD0 / <stem>_LOD1 siblings in one GLB.
Run: blender.exe -b --python build_vehicles.py   (see run_all.ps1)
"""
import json
import math
import os
import random
import sys

import bpy
import bmesh

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo, add, sub, mul, rot_y, v3  # noqa: E402,F401
from sakura_lib import u2b  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
MODELS = os.path.join(ROOT, "Assets", "Models", "ShuntaMetro")

# ---------------------------------------------------------------------------------- materials
# base (linear-ish sRGB triple), metal, rough, emit rgb, emit strength (relative), coat, tex name, tile m
def M(base, metal=0.0, rough=0.5, emit=None, es=0.0, coat=0.0, tex=None, tile=2.0):
    return dict(base=list(base), metal=metal, rough=rough, emit=list(emit) if emit else [0, 0, 0],
                emitRel=es, coat=coat, tex=tex, tile=tile)


MATS = {
    # --- vehicle paints (glossy clear-coat)
    "SM_PaintTaxi": M((0.93, 0.70, 0.04), 0.35, 0.18, coat=1.0),
    "SM_PaintWhite": M((0.88, 0.89, 0.9), 0.3, 0.2, coat=1.0),
    "SM_PaintSilver": M((0.55, 0.57, 0.6), 0.75, 0.25, coat=1.0),
    "SM_PaintBlack": M((0.015, 0.016, 0.02), 0.4, 0.15, coat=1.0),
    "SM_PaintRed": M((0.55, 0.03, 0.04), 0.35, 0.2, coat=1.0),
    "SM_PaintBlue": M((0.05, 0.14, 0.45), 0.35, 0.2, coat=1.0),
    "SM_PaintBus": M((0.82, 0.84, 0.86), 0.3, 0.22, coat=0.8),
    "SM_BusStripe": M((0.02, 0.42, 0.25), 0.2, 0.3),
    "SM_VanBox": M((0.78, 0.8, 0.82), 0.2, 0.35, coat=0.5),
    "SM_Glass": M((0.02, 0.03, 0.04), 0.0, 0.04, coat=0.0),
    "SM_Chrome": M((0.85, 0.86, 0.88), 1.0, 0.12),
    "SM_Trim": M((0.02, 0.02, 0.022), 0.0, 0.55),
    "SM_Tire": M((0.012, 0.012, 0.012), 0.0, 0.85),
    "SM_Rim": M((0.6, 0.62, 0.65), 1.0, 0.22),
    "SM_Plate": M((0.9, 0.92, 0.9), 0.0, 0.4),
    "SM_Seat": M((0.08, 0.07, 0.07), 0.0, 0.7),
    "SM_Headlight": M((0.9, 0.95, 1.0), 0.0, 0.05, emit=(0.85, 0.92, 1.0), es=5.0),
    "SM_Taillight": M((0.5, 0.0, 0.0), 0.0, 0.1, emit=(1.0, 0.04, 0.02), es=2.6),
    "SM_Indicator": M((0.8, 0.3, 0.0), 0.0, 0.1, emit=(1.0, 0.55, 0.05), es=1.6),
    "SM_TaxiSignGreen": M((0.1, 0.8, 0.3), 0.0, 0.3, emit=(0.25, 1.0, 0.45), es=3.4),
    "SM_TaxiSignYellow": M((0.9, 0.8, 0.2), 0.0, 0.3, emit=(1.0, 0.85, 0.25), es=3.0),
    "SM_DestSign": M((0.05, 0.03, 0.0), 0.0, 0.3, emit=(1.0, 0.55, 0.08), es=3.2),
    "SM_InteriorLight": M((0.9, 0.9, 0.8), 0.0, 0.5, emit=(1.0, 0.95, 0.8), es=1.8),
    # --- building shell
    "SM_ConcreteA": M((0.34, 0.34, 0.35), 0.0, 0.85, tex="concrete", tile=3.0),
    "SM_ConcreteB": M((0.52, 0.5, 0.46), 0.0, 0.88, tex="concrete", tile=3.0),
    "SM_ConcreteDark": M((0.12, 0.12, 0.13), 0.0, 0.8, tex="concrete", tile=3.0),
    "SM_TileBeige": M((0.55, 0.46, 0.36), 0.0, 0.45, tex="tile", tile=2.4),
    "SM_TileBrown": M((0.28, 0.16, 0.11), 0.0, 0.45, tex="tile", tile=2.4),
    "SM_TileWhite": M((0.7, 0.72, 0.74), 0.0, 0.4, tex="tile", tile=2.4),
    "SM_Metal": M((0.35, 0.37, 0.4), 0.9, 0.4, tex="metal", tile=1.5),
    "SM_MetalDark": M((0.07, 0.075, 0.085), 0.8, 0.45, tex="metal", tile=1.5),
    "SM_Curtain": M((0.04, 0.07, 0.1), 0.5, 0.08, coat=0.0),
    "SM_RoofTar": M((0.05, 0.05, 0.055), 0.0, 0.95),
    "SM_Fabric": M((0.7, 0.7, 0.75), 0.0, 0.9),
    "SM_FabricRed": M((0.65, 0.08, 0.08), 0.0, 0.9),
    "SM_FabricBlue": M((0.1, 0.2, 0.6), 0.0, 0.9),
    "SM_FabricYellow": M((0.8, 0.65, 0.1), 0.0, 0.9),
    "SM_ACUnit": M((0.78, 0.8, 0.8), 0.0, 0.5),
    "SM_Asphalt": M((0.05, 0.05, 0.055), 0.0, 0.7),
    "SM_RoadMark": M((0.8, 0.8, 0.75), 0.0, 0.6),
    # --- window family (per-window variation chosen by material)
    "SM_WinDark": M((0.015, 0.02, 0.03), 0.4, 0.06),
    "SM_WinWarm": M((0.1, 0.08, 0.04), 0.0, 0.3, emit=(1.0, 0.72, 0.38), es=1.5),
    "SM_WinWarmDim": M((0.06, 0.04, 0.02), 0.0, 0.3, emit=(1.0, 0.62, 0.3), es=0.55),
    "SM_WinCool": M((0.05, 0.08, 0.1), 0.0, 0.3, emit=(0.6, 0.85, 1.0), es=1.6),
    "SM_WinTV": M((0.05, 0.07, 0.1), 0.0, 0.3, emit=(0.55, 0.75, 1.0), es=2.6),
    "SM_WinMix": M((0.07, 0.06, 0.04), 0.0, 0.3, emit=(1.0, 0.8, 0.5), es=0.7),
    "SM_WinShop": M((0.2, 0.18, 0.12), 0.0, 0.2, emit=(1.0, 0.95, 0.85), es=2.4),
    # --- neon / signs / screens
    "SM_NeonRed": M((0.5, 0.02, 0.04), 0.0, 0.3, emit=(1.0, 0.08, 0.1), es=4.0),
    "SM_NeonPink": M((0.5, 0.05, 0.3), 0.0, 0.3, emit=(1.0, 0.15, 0.6), es=4.0),
    "SM_NeonCyan": M((0.05, 0.4, 0.5), 0.0, 0.3, emit=(0.1, 0.9, 1.0), es=4.0),
    "SM_NeonYellow": M((0.6, 0.5, 0.05), 0.0, 0.3, emit=(1.0, 0.85, 0.1), es=3.6),
    "SM_NeonGreen": M((0.05, 0.5, 0.15), 0.0, 0.3, emit=(0.2, 1.0, 0.35), es=3.6),
    "SM_NeonBlue": M((0.05, 0.1, 0.6), 0.0, 0.3, emit=(0.15, 0.3, 1.0), es=3.8),
    "SM_NeonWhite": M((0.8, 0.8, 0.85), 0.0, 0.3, emit=(1.0, 0.98, 0.95), es=3.4),
    "SM_SignBoard": M((0.9, 0.9, 0.88), 0.0, 0.4, emit=(0.95, 0.9, 0.8), es=1.6),
    "SM_Lantern": M((0.6, 0.05, 0.03), 0.0, 0.5, emit=(1.0, 0.25, 0.08), es=2.2),
    "SM_VideoWall": M((0.02, 0.02, 0.03), 0.0, 0.2, emit=(0.8, 0.9, 1.0), es=3.0),
    "SM_ScreenB": M((0.02, 0.02, 0.03), 0.0, 0.2, emit=(1.0, 0.4, 0.8), es=3.0),
    "SM_ScreenC": M((0.02, 0.02, 0.03), 0.0, 0.2, emit=(0.3, 1.0, 0.7), es=3.0),
    "SM_KonbiniGreen": M((0.0, 0.4, 0.2), 0.0, 0.4, emit=(0.1, 0.9, 0.35), es=2.4),
    "SM_KonbiniBlue": M((0.0, 0.2, 0.6), 0.0, 0.4, emit=(0.1, 0.4, 1.0), es=2.4),
    "SM_KonbiniOrange": M((0.7, 0.3, 0.0), 0.0, 0.4, emit=(1.0, 0.45, 0.05), es=2.4),
    "SM_Warning": M((0.7, 0.05, 0.02), 0.0, 0.3, emit=(1.0, 0.1, 0.05), es=3.0),
    # --- infrastructure / rail
    "SM_ConcreteInfra": M((0.3, 0.3, 0.3), 0.0, 0.9, tex="concrete", tile=3.0),
    "SM_Brick": M((0.32, 0.12, 0.08), 0.0, 0.8, tex="tile", tile=2.0),
    "SM_TrainBody": M((0.72, 0.74, 0.76), 0.7, 0.28, coat=0.7),
    "SM_TrainStripeGreen": M((0.0, 0.5, 0.3), 0.2, 0.3),
    "SM_TrainStripeOrange": M((0.9, 0.4, 0.02), 0.2, 0.3),
    "SM_TrainStripeBlue": M((0.0, 0.2, 0.6), 0.2, 0.3),
    "SM_TrainWindow": M((0.03, 0.04, 0.05), 0.0, 0.05, emit=(1.0, 0.95, 0.82), es=1.1),
    "SM_TrainDoor": M((0.45, 0.46, 0.48), 0.6, 0.3),
    "SM_RailSteel": M((0.2, 0.2, 0.22), 1.0, 0.35),
    "SM_TunnelLamp": M((0.7, 0.8, 0.9), 0.0, 0.3, emit=(1.0, 0.7, 0.35), es=3.0),
    "SM_Guardrail": M((0.55, 0.57, 0.6), 0.9, 0.35),
    "SM_Reflector": M((0.7, 0.4, 0.0), 0.0, 0.3, emit=(1.0, 0.6, 0.05), es=1.2),
}

for _k, _v in MATS.items():
    C.TILE[_k] = (_v["tile"], _v["tile"])


def write_materials_json():
    os.makedirs(MODELS, exist_ok=True)
    items = []
    for k in sorted(MATS):
        d = MATS[k]
        items.append(dict(name=k, base=d["base"], metal=d["metal"], rough=d["rough"], emit=d["emit"], emitRel=d["emitRel"],
                          coat=d["coat"], tex=d["tex"] or "", tile=d["tile"]))
    with open(os.path.join(MODELS, "materials.json"), "w") as f:
        json.dump({"items": items}, f, indent=1)


def get_bl_material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    d = MATS.get(name, M((0.5, 0.5, 0.5)))
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    b = d["base"]
    bsdf.inputs["Base Color"].default_value = (b[0], b[1], b[2], 1)
    bsdf.inputs["Metallic"].default_value = d["metal"]
    bsdf.inputs["Roughness"].default_value = d["rough"]
    if d["coat"] > 0:
        bsdf.inputs["Coat Weight"].default_value = d["coat"]
        bsdf.inputs["Coat Roughness"].default_value = 0.03
    if d["emitRel"] > 0:
        e = d["emit"]
        bsdf.inputs["Emission Color"].default_value = (e[0], e[1], e[2], 1)
        bsdf.inputs["Emission Strength"].default_value = d["emitRel"]
    return m


# ---------------------------------------------------------------------------------- smooth shells
def sgnpow(c, e):
    return math.copysign(abs(c) ** e, c)


def lerp(a, b, t):
    return a + (b - a) * t


def keyinterp(keys, z):
    """keys: list of tuples (z, v0, v1, ...) sorted by z. Smoothstep interpolation."""
    if z <= keys[0][0]:
        return keys[0][1:]
    if z >= keys[-1][0]:
        return keys[-1][1:]
    for i in range(len(keys) - 1):
        a, b = keys[i], keys[i + 1]
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / max(b[0] - a[0], 1e-6)
            t = t * t * (3 - 2 * t) * 0.6 + t * 0.4
            return tuple(lerp(a[j], b[j], t) for j in range(1, len(a)))
    return keys[-1][1:]


class SGeo:
    """Shared-vertex smooth shell. Faces are quads/tris of vertex indices + material.
    Winding is fixed up by bmesh.recalc_face_normals in make_object."""

    def __init__(self):
        self.verts, self.uvs, self.faces = [], [], []

    def v(self, p, uv=(0, 0)):
        self.verts.append(p)
        self.uvs.append(uv)
        return len(self.verts) - 1

    def loft(self, stations, seg, mat, matfn=None, cap=True, capmat=None, uv_u=1.0, uv_v=0.25):
        """stations: list of (z, cx, ymid, hw, hh, nx, ny). Cross-section is a superellipse,
        seam at the bottom. matfn(k, seg, si) -> material name."""
        rings = []
        for si, (z, cx, ym, hw, hh, nx, ny) in enumerate(stations):
            ring = []
            for k in range(seg + 1):
                t = -math.pi / 2 + math.tau * k / seg
                cc, ss = math.cos(t), math.sin(t)
                x = cx + hw * sgnpow(cc, 2.0 / nx)
                y = ym + hh * sgnpow(ss, 2.0 / ny)
                ring.append(self.v((x, y, z), (k / seg * uv_u, z * uv_v)))
            rings.append(ring)
        for si in range(len(rings) - 1):
            for k in range(seg):
                m = matfn(k, seg, si) if matfn else mat
                a, b = rings[si][k], rings[si][k + 1]
                c, d = rings[si + 1][k + 1], rings[si + 1][k]
                self.faces.append(((a, b, c, d), m))
        if cap:
            cm = capmat or mat
            for ring, st in ((rings[0], stations[0]), (rings[-1], stations[-1])):
                cen = self.v((st[1], st[2], st[0]), (0.5, st[0] * uv_v))
                for k in range(seg):
                    self.faces.append(((ring[k], ring[k + 1], cen), cm))
        return rings

    def tris(self):
        return sum(len(f[0]) - 2 for f in self.faces)


def cyl_x(cx, cy, cz, r, w, seg, mat, g, rim_mat=None, rim_r=None):
    """Wheel-style cylinder along X in Unity space (axis = x) into Geo g: tread + two side caps."""
    x0, x1 = cx - w / 2, cx + w / 2
    ring0 = [(x0, cy + r * math.cos(i / seg * math.tau), cz + r * math.sin(i / seg * math.tau)) for i in range(seg)]
    ring1 = [(x1, p[1], p[2]) for p in ring0]
    for i in range(seg):
        j = (i + 1) % seg
        g.quad(ring0[i], ring0[j], ring1[j], ring1[i], mat, uvs=[(i / seg, 0), ((i + 1) / seg, 0), ((i + 1) / seg, 1), (i / seg, 1)])
    rm = rim_mat or mat
    rr = rim_r if rim_r else r
    cen0, cen1 = (x0, cy, cz), (x1, cy, cz)
    for i in range(seg):
        j = (i + 1) % seg
        g.tri(cen0, ring0[j], ring0[i], rm if rr >= r else mat)
        g.tri(cen1, ring1[i], ring1[j], rm if rr >= r else mat)


def wheel(g, cx, cy, cz, r, w, side, detail, rim=True, spokes=5):
    """Tyre + rim (+ spokes) at (cx,cy,cz). side=+1/-1 outer face direction."""
    seg = 20 if detail == 0 else 10
    cyl_x(cx, cy, cz, r, w, seg, "SM_Tire", g)
    if detail == 0 and rim:
        # raised rim disk on outer face and dark inner hub
        xo = cx + side * (w / 2 + 0.004)
        rr = r * 0.66
        ring = [(xo, cy + rr * math.cos(i / seg * math.tau), cz + rr * math.sin(i / seg * math.tau)) for i in range(seg)]
        cen = (xo + side * 0.012, cy, cz)
        for i in range(seg):
            j = (i + 1) % seg
            if side > 0:
                g.tri(cen, ring[i], ring[j], "SM_Rim")
            else:
                g.tri(cen, ring[j], ring[i], "SM_Rim")
        for s in range(spokes):
            a = s / spokes * math.tau
            for da in (0.0,):
                p0 = (xo + side * 0.014, cy + 0.05 * r * math.cos(a), cz + 0.05 * r * math.sin(a))
                p1 = (xo + side * 0.014, cy + rr * 0.9 * math.cos(a + 0.3), cz + rr * 0.9 * math.sin(a + 0.3))
                p2 = (xo + side * 0.014, cy + rr * 0.9 * math.cos(a - 0.3), cz + rr * 0.9 * math.sin(a - 0.3))
                if side > 0:
                    g.tri(p0, p2, p1, "SM_Trim")
                else:
                    g.tri(p0, p1, p2, "SM_Trim")
    elif rim:
        xo = cx + side * (w / 2 + 0.003)
        rr = r * 0.62
        ring = [(xo, cy + rr * math.cos(i / seg * math.tau), cz + rr * math.sin(i / seg * math.tau)) for i in range(seg)]
        for i in range(1, seg - 1):
            if side > 0:
                g.tri(ring[0], ring[i], ring[i + 1], "SM_Rim")
            else:
                g.tri(ring[0], ring[i + 1], ring[i], "SM_Rim")


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def oquad(g, pts, mat, center, uvs=None):
    """Add a convex face with winding chosen so the normal points away from `center`."""
    n = _cross(_sub(pts[1], pts[0]), _sub(pts[2], pts[0]))
    fc = tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))
    away = _sub(fc, center)
    if n[0] * away[0] + n[1] * away[1] + n[2] * away[2] < 0:
        pts = list(reversed(pts))
        if uvs:
            uvs = list(reversed(uvs))
    g.face(list(pts), mat, uvs)


def hexa(g, bot, top, mat, mats=None):
    """Hexahedron from 4 bottom + 4 top corners (same winding order). mats: dict top/bottom/side."""
    ms = mats or {}
    allp = list(bot) + list(top)
    cen = tuple(sum(p[i] for p in allp) / 8 for i in range(3))
    oquad(g, bot, ms.get("bottom", mat), cen)
    oquad(g, top, ms.get("top", mat), cen)
    for i in range(4):
        j = (i + 1) % 4
        oquad(g, [bot[i], bot[j], top[j], top[i]], ms.get("side", mat), cen)


def beam(g, a, b, t, mat, t2=None):
    """Square-section beam from a to b with thickness t (t2 at b)."""
    t2 = t if t2 is None else t2
    ax = _sub(b, a)
    L = math.sqrt(sum(c * c for c in ax)) or 1.0
    ax = tuple(c / L for c in ax)
    up = (0, 1, 0) if abs(ax[1]) < 0.9 else (1, 0, 0)
    s = _cross(ax, up)
    sl = math.sqrt(sum(c * c for c in s)) or 1.0
    s = tuple(c / sl for c in s)
    u = _cross(s, ax)

    def ring(p, th):
        h = th / 2
        return [tuple(p[i] + s[i] * h * sx + u[i] * h * sy for i in range(3)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    hexa(g, ring(a, t), ring(b, t2), mat)


def disc_x(g, cx, cy, cz, r, seg, mat, facing):
    """Flat disc in the YZ plane at x=cx facing +x (facing=+1) or -x."""
    ring = [(cx, cy + r * math.cos(i / seg * math.tau), cz + r * math.sin(i / seg * math.tau)) for i in range(seg)]
    cen = (cx, cy, cz)
    for i in range(seg):
        j = (i + 1) % seg
        if facing > 0:
            g.tri(cen, ring[i], ring[j], mat)
        else:
            g.tri(cen, ring[j], ring[i], mat)


# ---------------------------------------------------------------------------------- object build
def make_object(name, geos):
    mats = []
    for g in geos:
        for f in g.faces:
            m = f[-1] if isinstance(g, Geo) is False else f[2]
            if m not in mats:
                mats.append(m)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    for g in geos:
        if isinstance(g, Geo):
            bverts = [bm.verts.new(u2b(*p)) for p in g.verts]
            bm.verts.ensure_lookup_table()
            for idx, uvs, m in g.faces:
                ridx = list(reversed(idx))
                ruv = list(reversed(uvs))
                try:
                    f = bm.faces.new([bverts[i] for i in ridx])
                except ValueError:
                    continue
                f.material_index = mats.index(m)
                f.smooth = False
                for loop, uv in zip(f.loops, ruv):
                    loop[uvl].uv = uv
        else:
            bverts = [bm.verts.new(u2b(*p)) for p in g.verts]
            bm.verts.ensure_lookup_table()
            newfaces = []
            for idx, m in g.faces:
                try:
                    f = bm.faces.new([bverts[i] for i in idx])
                except ValueError:
                    continue
                f.material_index = mats.index(m)
                f.smooth = True
                for loop, vi in zip(f.loops, idx):
                    loop[uvl].uv = g.uvs[vi]
                newfaces.append(f)
            bmesh.ops.recalc_face_normals(bm, faces=newfaces)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for m in mats:
        me.materials.append(get_bl_material(m))
    return ob


def total_tris(ob):
    me = ob.data
    return sum(len(p.vertices) - 2 for p in me.polygons)


def export_lods(stem, builder, out_sub, levels=(0, 1)):
    """builder(detail) -> list of geos. Writes Assets/Models/ShuntaMetro/<out_sub>/<stem>.glb."""
    C.reset()
    objs, counts = [], []
    for lvl in levels:
        geos = builder(lvl)
        ob = make_object("%s_LOD%d" % (stem, lvl), geos)
        objs.append(ob)
        counts.append(total_tris(ob))
    C.OUT = os.path.join(MODELS, out_sub)
    C.export(objs, stem + ".glb")
    print("[sm-glb] %-34s tris LOD0/LOD1 %s" % (out_sub + "/" + stem, " / ".join("{:,}".format(c) for c in counts)))
    return counts

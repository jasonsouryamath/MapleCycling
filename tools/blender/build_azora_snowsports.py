"""
WP-H2  Azora Highlands - snowsports kit.   Copilot session 4, 2026-09-26.

Builds the small equipment GLBs that AzoraHighlands.Snowsports.cs binds to the posed skier and
snowboarder NPCs on the slopes beside the route:

    Azora_Ski          one alpine ski (sidecut, tip + tail rise, top sheet, binding + plate)
                       origin = centre of the ski BASE under the boot, +Z = tip, +Y = up.
                       The boot sole sits BootSoleY (0.05 m) above the base.
    Azora_SkiPole      one pole, origin = top of the grip, running down -Y for exactly 1.0 m
                       (C# scales Y to the length it needs; the radius is left alone).
    Azora_Snowboard    one board, origin = centre of the BASE, +Z = nose, +X = TOE edge,
                       two angled bindings with highbacks on the heel (-X) edge.
    Azora_SkiHelmet    a UNIT head: shell + goggle frame + lens + strap, centred on the head
                       centre, +Z = face, +Y = up, radius 1 in every axis. C# scales it per donor
                       from the measured head extents.

Every vertex is authored in UNITY coordinates and converted with sakura_lib.u2b, so Unity stages
with an identity transform. Material slots are named Azora_Snow_<Role>; the Unity side
re-materialises every slot by role with MapleRide CelLit materials (and per-rider colours), so the
Blender colours below only drive the preview and the glTF fallback.

Run:  tools\\blender-4.5.10-windows-x64\\blender.exe -b --factory-startup -P tools/blender/build_azora_snowsports.py
"""

import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sakura_lib as S  # noqa: E402

ROOT = S.repo_root()
MODELS = os.path.join(ROOT, "Assets", "Environment", "AzoraHighlands", "Models")
os.makedirs(MODELS, exist_ok=True)
S.blender_assets_dir = lambda: MODELS

ROLES = {
    "SkiBase":     (0.08, 0.08, 0.09),
    "SkiTop":      (0.80, 0.12, 0.10),
    "Binding":     (0.18, 0.19, 0.21),
    "BindingTrim": (0.85, 0.86, 0.88),
    "PoleShaft":   (0.72, 0.74, 0.78),
    "PoleGrip":    (0.07, 0.07, 0.08),
    "BoardBase":   (0.06, 0.06, 0.07),
    "BoardTop":    (0.12, 0.40, 0.62),
    "Helmet":      (0.93, 0.93, 0.94),
    "GoggleFrame": (0.05, 0.05, 0.06),
    "GoggleLens":  (0.95, 0.52, 0.12),
}

_MATS = {}


def material(role):
    if role not in _MATS:
        c = ROLES[role]
        m = S.pbr_material(f"Azora_Snow_{role}", base_color=(c[0], c[1], c[2], 1.0),
                           roughness=0.25 if role in ("GoggleLens", "SkiTop", "BoardTop", "Helmet") else 0.7,
                           metallic=0.5 if role in ("PoleShaft",) else 0.0)
        m.diffuse_color = (c[0], c[1], c[2], 1.0)
        _MATS[role] = m
    return _MATS[role]


class Kit:
    """Closed primitives per role in UNITY coords -> one multi-material object (same idea as
    build_azora_village.Kit: each primitive is its own island, so recalc_face_normals orients each
    one outward independently; open sheets carry an explicit outward hint)."""

    def __init__(self):
        self.groups = {}

    def prim(self, role, verts, faces, hint=None):
        self.groups.setdefault(role, []).append(([Vector(v) for v in verts], faces, hint))

    def box(self, role, c, size, yaw_deg=0.0):
        hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
        ca, sa = math.cos(math.radians(yaw_deg)), math.sin(math.radians(yaw_deg))
        vs = []
        for i in range(8):
            x, y, z = (hx if i & 1 else -hx), (hy if i & 2 else -hy), (hz if i & 4 else -hz)
            vs.append((c[0] + x * ca + z * sa, c[1] + y, c[2] - x * sa + z * ca))
        faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        self.prim(role, vs, faces)

    def cyl_y(self, role, c, r0, r1, y0, y1, n=8):
        vs = []
        for k, (r, y) in enumerate(((r0, y0), (r1, y1))):
            for i in range(n):
                a = i / n * math.tau
                vs.append((c[0] + math.cos(a) * r, y, c[2] + math.sin(a) * r))
        faces = [tuple(range(n)), tuple(range(n, 2 * n))[::-1]]
        for i in range(n):
            j = (i + 1) % n
            faces.append((i, n + i, n + j, j))
        self.prim(role, vs, faces)

    def loft(self, role, sections):
        """sections: list of 4-vertex rings (closed tube with end caps)."""
        vs = [p for ring in sections for p in ring]
        faces = [(0, 1, 2, 3)[::-1]]
        n = len(sections)
        for s in range(n - 1):
            a, b = s * 4, (s + 1) * 4
            for i in range(4):
                j = (i + 1) % 4
                faces.append((a + i, a + j, b + j, b + i))
        last = (n - 1) * 4
        faces.append((last, last + 1, last + 2, last + 3))
        self.prim(role, vs, faces)

    def build(self, name, smooth_roles=()):
        objs = []
        for role, prims in self.groups.items():
            bm = bmesh.new()
            closed, sheets = [], []
            for vv, faces, hint in prims:
                bv = [bm.verts.new(S.u2b(v.x, v.y, v.z)) for v in vv]
                for f in faces:
                    try:
                        bf = bm.faces.new([bv[i] for i in f])
                    except ValueError:
                        continue
                    (sheets if hint is not None else closed).append((bf, hint))
            bm.normal_update()
            if closed:
                bmesh.ops.recalc_face_normals(bm, faces=[f for f, _ in closed])
            for bf, hint in sheets:
                if bf.normal.dot(Vector(S.u2b(*hint))) < 0:
                    bf.normal_flip()
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
            me = bpy.data.meshes.new(f"{name}_{role}")
            bm.to_mesh(me)
            bm.free()
            ob = bpy.data.objects.new(f"{name}_{role}", me)
            S.link(ob)
            ob.data.materials.append(material(role))
            for p in ob.data.polygons:
                p.use_smooth = role in smooth_roles
            objs.append(ob)
        ob = S.join(objs, name)
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        print(f"[azora-snow] {name}: {tris} tris, roles {sorted(self.groups)}")
        return ob


# ============================================================================ ski

SKI_TAIL, SKI_TIP = -0.80, 0.86


def ski_width(z):
    # sidecut: 110 mm tip, 70 mm waist, 95 mm tail; rounded shovel at the very tip
    if z >= 0:
        w = 0.070 + (0.110 - 0.070) * (z / 0.70) ** 2 if z < 0.70 else 0.110 - (z - 0.70) / 0.16 * 0.075
    else:
        w = 0.070 + (0.095 - 0.070) * (-z / 0.76) ** 2 if z > -0.76 else 0.095 - (-0.76 - z) / 0.04 * 0.03
    return max(w, 0.025)


def ski_rise(z):
    if z > 0.60:
        return ((z - 0.60) / (SKI_TIP - 0.60)) ** 2 * 0.085
    if z < -0.70:
        return ((-0.70 - z) / (0.70 + SKI_TAIL)) ** 2 * 0.030 if SKI_TAIL < -0.70 else 0.0
    return 0.0


def ski_thick(z):
    return 0.008 + 0.012 * max(0.0, 1.0 - abs(z) / 0.85)


def build_ski():
    k = Kit()
    N = 30
    body, top = [], []
    for i in range(N + 1):
        z = SKI_TAIL + (SKI_TIP - SKI_TAIL) * i / N
        hw, y0, t = ski_width(z) * 0.5, ski_rise(z), ski_thick(z)
        body.append([(-hw, y0, z), (hw, y0, z), (hw, y0 + t, z), (-hw, y0 + t, z)])
        tw = max(hw - 0.004, 0.008)
        top.append((-tw, y0 + t + 0.0012, z, tw))
    k.loft("SkiBase", body)
    # top sheet: an open strip just above the deck (outward hint = up)
    vs, fs = [], []
    for i, (xl, y, z, xr) in enumerate(top):
        vs += [(xl, y, z), (xr, y, z)]
        if i:
            a = 2 * (i - 1)
            fs.append((a, a + 1, a + 3, a + 2))
    k.prim("SkiTop", vs, fs, hint=(0, 1, 0))
    deck = ski_thick(0.0)
    # binding: plate, toe piece, heel piece (boot sole at BootSoleY = deck + plate + ~0.02)
    k.box("Binding", (0, deck + 0.006, -0.01), (0.062, 0.012, 0.44))
    k.box("Binding", (0, deck + 0.030, 0.185), (0.074, 0.040, 0.085))
    k.box("BindingTrim", (0, deck + 0.052, 0.200), (0.050, 0.012, 0.040))
    k.box("Binding", (0, deck + 0.036, -0.215), (0.080, 0.052, 0.110))
    k.box("BindingTrim", (0, deck + 0.066, -0.235), (0.056, 0.014, 0.070))
    return k.build("Azora_Ski")


# ============================================================================ pole

def build_pole():
    k = Kit()
    k.cyl_y("PoleGrip", (0, 0, 0), 0.0165, 0.0185, -0.17, 0.0, n=10)
    k.cyl_y("PoleGrip", (0, 0, 0), 0.0200, 0.0200, -0.008, 0.008, n=10)       # grip cap
    k.cyl_y("PoleShaft", (0, 0, 0), 0.0070, 0.0095, -0.975, -0.165, n=8)
    k.cyl_y("PoleGrip", (0, 0, 0), 0.0460, 0.0460, -0.905, -0.895, n=12)      # basket
    k.cyl_y("PoleGrip", (0, 0, 0), 0.0015, 0.0070, -1.0, -0.975, n=6)         # tip
    return k.build("Azora_SkiPole")


# ============================================================================ snowboard

BOARD_HALF_L = 0.77


def board_half_width(z):
    a = abs(z)
    core = 0.125 + 0.020 * (a / 0.58) ** 2                     # 250 waist -> 290 at the contact points
    if a <= 0.58:
        return core
    u = min(1.0, (a - 0.58) / (BOARD_HALF_L - 0.58))           # rounded nose / tail
    return 0.145 * math.sqrt(max(0.0, 1.0 - u * u)) + 0.004


def board_rise(z):
    a = abs(z)
    return 0.0 if a < 0.56 else ((a - 0.56) / (BOARD_HALF_L - 0.56)) ** 2 * 0.065


def build_board():
    k = Kit()
    N = 36
    body, top = [], []
    for i in range(N + 1):
        z = -BOARD_HALF_L + 2 * BOARD_HALF_L * i / N
        hw, y0, t = board_half_width(z), board_rise(z), 0.013
        body.append([(-hw, y0, z), (hw, y0, z), (hw, y0 + t, z), (-hw, y0 + t, z)])
        tw = max(hw - 0.006, 0.004)
        top.append((-tw, y0 + t + 0.0012, z, tw))
    k.loft("BoardBase", body)
    vs, fs = [], []
    for i, (xl, y, z, xr) in enumerate(top):
        vs += [(xl, y, z), (xr, y, z)]
        if i:
            a = 2 * (i - 1)
            fs.append((a, a + 1, a + 3, a + 2))
    k.prim("BoardTop", vs, fs, hint=(0, 1, 0))
    # bindings: +Z (nose) at +15 deg, -Z (tail) at -6 deg (a duck-ish all-mountain stance)
    for zc, ang in ((0.265, 15.0), (-0.265, -6.0)):
        k.box("Binding", (0, 0.022, zc), (0.25, 0.014, 0.13), yaw_deg=ang)          # base plate
        k.box("Binding", (-0.105, 0.105, zc), (0.022, 0.17, 0.15), yaw_deg=ang)     # highback (heel edge)
        k.box("BindingTrim", (0.02, 0.075, zc), (0.13, 0.022, 0.055), yaw_deg=ang)  # ankle strap
        k.box("BindingTrim", (0.10, 0.040, zc), (0.05, 0.016, 0.10), yaw_deg=ang)   # toe strap
    return k.build("Azora_Snowboard")


# ============================================================================ helmet

def build_helmet():
    """Unit head: shell (outer 1.0, inner 0.94), cut higher at the face, lower at the back/nape;
    goggles (frame + lens) across the face just under the brim; strap round the back."""
    k = Kit()
    NA, NR = 28, 9

    def cut(theta):
        # theta = azimuth from the FACE (+Z). polar cut angle from the crown: 72 deg at the front
        # (brow), 112 deg at the back (nape), smooth in between.
        f = 0.5 + 0.5 * math.cos(theta)
        return math.radians(112.0 + (72.0 - 112.0) * f)

    def sph(r, theta, phi):
        return (r * math.sin(phi) * math.sin(theta), r * math.cos(phi), r * math.sin(phi) * math.cos(theta))

    vs, fs = [], []
    for shell, r in enumerate((1.0, 0.94)):
        base = len(vs)
        vs.append((0.0, r, 0.0))                                      # crown
        for j in range(1, NR + 1):
            for i in range(NA):
                th = i / NA * math.tau
                vs.append(sph(r, th, cut(th) * j / NR))
        for i in range(NA):
            i2 = (i + 1) % NA
            f = (base, base + 1 + i2, base + 1 + i)
            fs.append(f if shell == 0 else f[::-1])
        for j in range(NR - 1):
            a0, a1 = base + 1 + j * NA, base + 1 + (j + 1) * NA
            for i in range(NA):
                i2 = (i + 1) % NA
                f = (a0 + i, a0 + i2, a1 + i2, a1 + i)
                fs.append(f if shell == 0 else f[::-1])
    # rim joining outer and inner rings
    o = 1 + (NR - 1) * NA
    inn = (1 + NR * NA) + o
    for i in range(NA):
        i2 = (i + 1) % NA
        fs.append((o + i, inn + i, inn + i2, o + i2))
    k.prim("Helmet", vs, fs)

    # goggles: a curved band across the face, azimuth -78..78 deg, from just under the brim down
    brim_y = math.cos(math.radians(72.0))
    y_top, y_bot = brim_y + 0.02, brim_y - 0.40
    NG = 14

    def band(role, r_out, y0, y1, a0, a1, depth):
        vv, ff = [], []
        for i in range(NG + 1):
            th = math.radians(a0 + (a1 - a0) * i / NG)
            for (r, y) in ((r_out, y0), (r_out, y1), (r_out - depth, y1), (r_out - depth, y0)):
                vv.append((r * math.sin(th), y, r * math.cos(th)))
        for i in range(NG):
            a, b = i * 4, (i + 1) * 4
            for q in range(4):
                q2 = (q + 1) % 4
                ff.append((a + q, a + q2, b + q2, b + q))
        ff.append((0, 1, 2, 3)[::-1])
        ff.append((NG * 4, NG * 4 + 1, NG * 4 + 2, NG * 4 + 3))
        k.prim(role, vv, ff)

    band("GoggleFrame", 1.10, y_bot - 0.03, y_top + 0.02, -80, 80, 0.10)
    band("GoggleLens", 1.125, y_bot + 0.02, y_top - 0.02, -70, 70, 0.03)
    band("GoggleFrame", 1.035, y_bot + 0.06, y_top - 0.06, 80, 280, 0.03)     # strap round the back
    return k.build("Azora_SkiHelmet", smooth_roles=("Helmet", "GoggleLens"))


def main():
    S.reset_scene()
    for fn, fname in ((build_ski, "Azora_Ski.glb"), (build_pole, "Azora_SkiPole.glb"),
                      (build_board, "Azora_Snowboard.glb"), (build_helmet, "Azora_SkiHelmet.glb")):
        ob = fn()
        S.export_glb([ob], fname)
        print(f"[azora-snow] exported {fname}")
        bpy.data.objects.remove(ob, do_unlink=True)


if __name__ == "__main__":
    main()

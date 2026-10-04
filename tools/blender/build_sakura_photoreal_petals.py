"""
E4 / Agent HQ huddle idea 1 (2026-09-30): sakura blossom canopy as ALPHA-CLIP cards on ONE
8-island atlas with a BAKED normal from high-poly petals.

  1. Builds real high-poly sakura flowers (5 notched, cupped petals + stamens, buds, twigs,
     young bronze leaves) laid out in a 4 x 2 grid of cells (= 8 UV islands).
  2. Cycles selected-to-active bakes onto a flat UV'd target plane:
       - NORMAL (tangent space)   -> Sakura_Blossom_Atlas8_Normal.png
       - EMIT (vertex colour)     -> albedo
       - EMIT (white-on-black)    -> coverage mask -> alpha
     Albedo is colour-dilated under the clip edge so mips never fringe dark.
  3. Regrows the Sakura Pass trees with the SAME growth code as build_flora.py (imported,
     not edited), but routes every canopy card into ONE mesh / ONE material, so each tree is
     trunk + canopy (the canopy is a single draw call; bark keeps its own cel shader).
     Exported under NEW names (*_Card8.glb) so Shiosai, which reuses the old tree GLBs with the
     old 2x2 atlas, is unaffected.

Run:  blender -b -P build_sakura_photoreal_petals.py            (bake + trees)
      blender -b -P build_sakura_photoreal_petals.py -- trees   (trees only, reuse bake)

All sizes/counts below are PROVISIONAL art tuning.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import math
import random

import bpy
import numpy as np

import sakura_lib as S
import build_flora as F

TAU = math.pi * 2.0

# ------------------------------------------------------------------ tunables (PROVISIONAL)
ATLAS_W, ATLAS_H = 2048, 1024          # 4 x 2 cells of 512 px
COLS, ROWS = 4, 2
BAKE_SAMPLES = 16
ALBEDO_NAME = "Sakura_Blossom_Atlas8.png"
NORMAL_NAME = "Sakura_Blossom_Atlas8_Normal.png"
LEAF_CELLS = (6, 7)                    # bronze young-leaf + blossom mixes
BLOSSOM_CELLS = (0, 1, 2, 3, 4, 5)
# (flowers, buds, twig, leaves) per cell.  Twig stubs are deliberately omitted from
# every card recipe: branches are real geometry on the tree, while a twig baked into a
# blossom card becomes an opaque diagonal alpha-cutout that repeats across the canopy
# at many rotations and reads as scratchy cotton/noise.  This keeps the atlas cells
# focused on petals/buds/leaves and lets the crown AO + normal provide depth.
CELL_RECIPES = [(8, 2, False, 0), (5, 3, False, 0), (9, 1, False, 0), (4, 4, False, 0),
                (7, 2, False, 0), (3, 1, False, 0), (4, 1, False, 3), (3, 2, False, 4)]

PETAL_BASE = (0.93, 0.50, 0.63)
PETAL_MID = (0.99, 0.82, 0.88)
PETAL_TIP = (1.00, 0.93, 0.95)
STAMEN = (0.80, 0.28, 0.36)
ANTHER = (0.98, 0.84, 0.35)
BUD = (0.88, 0.36, 0.52)
TWIG = (0.26, 0.17, 0.15)
LEAF = (0.46, 0.44, 0.20)


def cell_uv_rect(c):
    col, row = c % COLS, c // COLS
    return (col / COLS, row / ROWS, (col + 1) / COLS, (row + 1) / ROWS)


# ------------------------------------------------------------ high-poly flower authoring
class Acc:
    def __init__(self):
        self.v, self.f, self.c = [], [], []

    def grid(self, pts, cols, nu, nv):
        base = len(self.v)
        self.v.extend(pts)
        self.c.extend(cols)
        for i in range(nu - 1):
            for j in range(nv - 1):
                a = base + i * nv + j
                self.f.append((a, a + nv, a + nv + 1, a + 1))


def _rot_matrix(axis, ang):
    axis = np.asarray(axis, float)
    axis /= max(np.linalg.norm(axis), 1e-9)
    x, y, z = axis
    c, s = math.cos(ang), math.sin(ang)
    C = 1 - c
    return np.array([[c + x * x * C, x * y * C - z * s, x * z * C + y * s],
                     [y * x * C + z * s, c + y * y * C, y * z * C - x * s],
                     [z * x * C - y * s, z * y * C + x * s, c + z * z * C]])


def _lerp3(a, b, t):
    return tuple(a[k] + (b[k] - a[k]) * t for k in range(3))


def petal_colour(u):
    return _lerp3(PETAL_BASE, PETAL_MID, u / 0.45) if u < 0.45 else _lerp3(PETAL_MID, PETAL_TIP, (u - 0.45) / 0.55)


def flower(acc, centre, R, rng, tilt):
    """Five notched, cupped petals + a stamen ring. Built flat around +Z then tilted."""
    M = _rot_matrix((rng.uniform(-1, 1), rng.uniform(-1, 1), 0.0), tilt)
    spin = rng.uniform(0, TAU)
    nu, nv = 12, 9
    for p in range(5):
        a = spin + p * TAU / 5 + rng.uniform(-0.08, 0.08)
        ax = np.array([math.cos(a), math.sin(a), 0.0])
        side = np.array([-math.sin(a), math.cos(a), 0.0])
        cup = rng.uniform(0.18, 0.32)
        pts, cols = [], []
        for i in range(nu):
            u = i / (nu - 1)
            for j in range(nv):
                s = -1.0 + 2.0 * j / (nv - 1)
                umax = 1.0 - 0.17 * math.exp(-(s / 0.26) ** 2)          # the sakura notch
                uu = u * umax
                hw = R * 0.44 * math.sin(math.pi * min(uu * 0.93 + 0.04, 1.0)) ** 0.75
                q = ax * (R * (0.10 + 0.90 * uu)) + side * (s * hw)
                z = R * (cup * uu * uu + 0.10 * s * s * uu + 0.025 * math.sin(uu * 9 + s * 3)) \
                    + p * 0.0015
                pts.append(tuple(M @ np.array([q[0], q[1], z]) + centre))
                cols.append(petal_colour(uu))
        acc.grid(pts, cols, nu, nv)
    # stamens: thin tapered strips with a yellow anther tip
    for k in range(18):
        a = rng.uniform(0, TAU)
        rr = R * rng.uniform(0.22, 0.36)
        tip = np.array([math.cos(a) * rr, math.sin(a) * rr, R * rng.uniform(0.28, 0.40)])
        side = np.array([-math.sin(a), math.cos(a), 0.0]) * R * 0.018
        pts, cols = [], []
        for i in range(4):
            t = i / 3
            mid = tip * t
            w = side * (1.0 if i < 3 else 2.4)
            pts += [tuple(M @ (mid - w) + centre), tuple(M @ (mid + w) + centre)]
            c = ANTHER if i == 3 else STAMEN
            cols += [c, c]
        acc.grid(pts, cols, 4, 2)


def ellipsoid(acc, centre, radii, colour, axis_tilt, rng, nu=10, nv=12):
    M = _rot_matrix((rng.uniform(-1, 1), rng.uniform(-1, 1), 0.0), axis_tilt)
    pts, cols = [], []
    for i in range(nu):
        th = math.pi * (i / (nu - 1))
        for j in range(nv):
            ph = TAU * j / (nv - 1)
            p = np.array([math.sin(th) * math.cos(ph) * radii[0],
                          math.sin(th) * math.sin(ph) * radii[1],
                          math.cos(th) * radii[2]])
            pts.append(tuple(M @ p + centre))
            shade = 0.85 + 0.15 * math.cos(th)
            cols.append(tuple(ch * shade for ch in colour))
    acc.grid(pts, cols, nu, nv)


def tube(acc, a, b, r0, r1, colour, seg=8):
    a, b = np.asarray(a, float), np.asarray(b, float)
    d = b - a
    d /= np.linalg.norm(d)
    n1 = np.cross(d, [0, 0, 1.0])
    if np.linalg.norm(n1) < 1e-6:
        n1 = np.array([1.0, 0, 0])
    n1 /= np.linalg.norm(n1)
    n2 = np.cross(d, n1)
    pts, cols = [], []
    for i in range(2):
        c, r = (a, r0) if i == 0 else (b, r1)
        for j in range(seg + 1):
            t = TAU * j / seg
            pts.append(tuple(c + (n1 * math.cos(t) + n2 * math.sin(t)) * r))
            cols.append(colour)
    acc.grid(pts, cols, 2, seg + 1)


def leaf(acc, centre, L, rng):
    a = rng.uniform(0, TAU)
    ax = np.array([math.cos(a), math.sin(a), 0.0])
    side = np.array([-math.sin(a), math.cos(a), 0.0])
    tilt = _rot_matrix(side, rng.uniform(-0.5, 0.3))
    nu, nv = 14, 7
    pts, cols = [], []
    for i in range(nu):
        u = i / (nu - 1)
        for j in range(nv):
            s = -1.0 + 2.0 * j / (nv - 1)
            serr = 1.0 + 0.06 * math.sin(u * 60.0)
            hw = L * 0.26 * math.sin(math.pi * u) ** 0.8 * serr
            q = ax * (L * u) + side * (s * hw)
            z = L * (0.10 * abs(s) - 0.06 * u * u)            # centre-vein fold
            pts.append(tuple(tilt @ q + centre + np.array([0, 0, z])))
            k = 0.8 + 0.2 * abs(s)
            cols.append(tuple(ch * k for ch in _lerp3(LEAF, (0.62, 0.30, 0.20), 0.35 * u)))
    acc.grid(pts, cols, nu, nv)


def build_highpoly(seed=7):
    rng = random.Random(seed)
    acc = Acc()
    for c, (nf, nb, twig, nl) in enumerate(CELL_RECIPES):
        col, row = c % COLS, c // COLS
        cx, cy = col + 0.5, row + 0.5
        placed = []
        if twig:
            a = rng.uniform(0, TAU)
            p0 = np.array([cx + math.cos(a) * 0.47, cy + math.sin(a) * 0.47, -0.05])
            tube(acc, p0, (cx, cy, -0.02), 0.028, 0.016, TWIG)
        for k in range(nl):
            a = rng.uniform(0, TAU)
            leaf(acc, np.array([cx + math.cos(a) * 0.08, cy + math.sin(a) * 0.08, -0.06]),
                 rng.uniform(0.26, 0.34), rng)
        for k in range(nf):
            R = rng.uniform(0.11, 0.155)
            for _ in range(40):
                rr = 0.30 * math.sqrt(rng.random())
                a = rng.uniform(0, TAU)
                p = np.array([cx + math.cos(a) * rr, cy + math.sin(a) * rr, rng.uniform(-0.04, 0.06)])
                if all(np.linalg.norm(p[:2] - q[:2]) > (R + qr) * 0.72 for q, qr in placed):
                    break
            # keep inside the cell (island margin)
            p[0] = min(max(p[0], col + R + 0.04), col + 1 - R - 0.04)
            p[1] = min(max(p[1], row + R + 0.04), row + 1 - R - 0.04)
            placed.append((p, R))
            flower(acc, p, R, rng, tilt=rng.uniform(0.0, 0.75))
        for k in range(nb):
            a = rng.uniform(0, TAU)
            rr = rng.uniform(0.22, 0.36)
            p = np.array([min(max(cx + math.cos(a) * rr, col + 0.07), col + 0.93),
                          min(max(cy + math.sin(a) * rr, row + 0.07), row + 0.93), 0.02])
            ellipsoid(acc, p, (0.035, 0.035, 0.06), BUD, rng.uniform(0.6, 1.3), rng)

    me = bpy.data.meshes.new("HP_Petals")
    me.from_pydata(acc.v, [], acc.f)
    me.update()
    ca = me.color_attributes.new("Col", 'FLOAT_COLOR', 'POINT')
    for i, c in enumerate(acc.c):
        ca.data[i].color = (c[0], c[1], c[2], 1.0)
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new("HP_Petals", me)
    bpy.context.scene.collection.objects.link(ob)
    print(f"[sakura-e4] high-poly petals: {len(acc.v)} verts, {len(acc.f)} quads")
    return ob


def _plane(name, z, w=COLS, h=ROWS, uv=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([(0, 0, z), (w, 0, z), (w, h, z), (0, h, z)], [], [(0, 1, 2, 3)])
    me.update()
    if uv:
        uvl = me.uv_layers.new(name="UVMap")
        for li, vi in enumerate(me.polygons[0].vertices):
            x, y, _ = me.vertices[vi].co
            uvl.data[li].uv = (x / w, y / h)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def _emit_mat(name, colour=None, use_attr=False):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    if use_attr:
        at = nt.nodes.new("ShaderNodeVertexColor")
        at.layer_name = "Col"
        nt.links.new(at.outputs["Color"], em.inputs["Color"])
    else:
        em.inputs["Color"].default_value = (*colour, 1.0)
    return m


def _set_mat(ob, m):
    ob.data.materials.clear()
    ob.data.materials.append(m)


def _target_image(ob, name, non_color):
    img = bpy.data.images.new(name, ATLAS_W, ATLAS_H, alpha=False, float_buffer=True)
    if non_color:
        img.colorspace_settings.name = 'Non-Color'
    m = ob.data.materials[0]
    nt = m.node_tree
    for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
        nt.nodes.remove(n)
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = img
    nt.nodes.active = tn
    return img


def _bake(kind, target, sources, img_name, non_color):
    img = _target_image(target, img_name, non_color)
    bpy.ops.object.select_all(action='DESELECT')
    for s in sources:
        s.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bk = bpy.context.scene.render.bake
    bk.use_selected_to_active = True
    bk.cage_extrusion = 0.6
    bk.max_ray_distance = 1.3
    bk.margin = 4
    bk.use_clear = True
    if kind == 'NORMAL':
        bk.normal_space = 'TANGENT'
    bpy.ops.object.bake(type=kind)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(ATLAS_H, ATLAS_W, 4)
    return px[..., :3].copy()


def _dilate_colour(rgb, alpha, iters=48):
    """Push opaque colour outward under the transparent region (mip/bilinear fringe fix)."""
    col = rgb * alpha[..., None]
    w = alpha.copy()
    known = alpha > 0.5
    for _ in range(iters):
        acc_c = np.zeros_like(col)
        acc_w = np.zeros_like(w)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            acc_c += np.roll(np.roll(col, dy, 0), dx, 1)
            acc_w += np.roll(np.roll(w, dy, 0), dx, 1)
        grow = (~known) & (acc_w > 0)
        col[grow] = acc_c[grow] / acc_w[grow, None]
        w[grow] = 1.0
        known = known | grow
        col[known & (alpha > 0.5)] = rgb[known & (alpha > 0.5)]
    out = np.where(known[..., None], col, rgb)
    return out


def _save(name, rgba):
    h, w = rgba.shape[:2]
    img = bpy.data.images.new(name + "_out", w, h, alpha=True)
    img.pixels.foreach_set(np.clip(rgba, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(S.textures_dir(), name)
    img.file_format = 'PNG'
    img.save()
    print(f"[sakura-e4] wrote {img.filepath_raw}")


def bake_atlas():
    S.reset_scene()
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = BAKE_SAMPLES
    hp = build_highpoly()
    back = _plane("HP_Back", -0.45, uv=False)
    target = _plane("LP_Target", 0.0)
    _set_mat(target, _emit_mat("LP_Mat", (0, 0, 0)))

    # 1) tangent-space normal (backplane faces +Z -> flat where there is no petal)
    _set_mat(hp, _emit_mat("HP_N", (1, 1, 1)))
    _set_mat(back, _emit_mat("BK_N", (0, 0, 0)))
    nrm = _bake('NORMAL', target, [hp, back], "bake_n", True)
    # 2) coverage mask
    mask = _bake('EMIT', target, [hp, back], "bake_m", True)[..., 0]
    # 3) albedo from the vertex colours
    _set_mat(hp, _emit_mat("HP_C", use_attr=True))
    alb = _bake('EMIT', target, [hp, back], "bake_c", False)

    alpha = np.clip((mask - 0.15) / 0.7, 0.0, 1.0)
    alb = _dilate_colour(alb, alpha)
    # float_buffer bake is linear; write sRGB for the albedo
    srgb = np.where(alb <= 0.0031308, alb * 12.92, 1.055 * np.power(np.clip(alb, 0, None), 1 / 2.4) - 0.055)
    _save(ALBEDO_NAME, np.dstack([srgb, alpha]))
    flat = np.array([0.5, 0.5, 1.0], dtype=np.float32)
    nrm = np.where(alpha[..., None] > 0.02, nrm, flat)
    _save(NORMAL_NAME, np.dstack([nrm, np.ones_like(alpha)]))
    print(f"[sakura-e4] atlas coverage {float((alpha > 0.5).mean()):.2f}")


# --------------------------------------------------------------------------- the trees
def canopy_material():
    mat = S.pbr_material("SakuraPass_BlossomCanopy8", base_color=(1, 1, 1, 1), roughness=0.72)
    t = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(t, ALBEDO_NAME)), "Base Color")
    # Route the crown-AO colour attribute through the material so the exporter (vertex colour
    # mode 'MATERIAL') writes COLOR_0; the Unity FoliageNormal shader multiplies it in.
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = bsdf.inputs["Base Color"].links[0].from_node if bsdf.inputs["Base Color"].links else None
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = 'RGBA'
    mul.blend_type = 'MULTIPLY'
    mul.inputs[0].default_value = 1.0
    if tex is not None:
        nt.links.new(tex.outputs["Color"], mul.inputs[6])
    nt.links.new(vc.outputs["Color"], mul.inputs[7])
    nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
    S.set_texture(mat, S.load_image(os.path.join(t, NORMAL_NAME), True), "Normal", non_color=True)
    if hasattr(mat, "blend_method"):
        try:
            mat.blend_method = 'CLIP'
        except TypeError:
            pass
    return mat


# ---- batch 2 (critic: stretched bark, flat canopy) ------------------------------------
BARK_TILE_M = 0.9        # PROVISIONAL: metres of trunk per bark texture repeat (square texels)
CROWN_AO_MIN = 0.34      # PROVISIONAL (Agent HQ 'flat noisy cotton canopy'): deepened from 0.52 -
                         # the original range only dimmed the crown core to 52% brightness, too
                         # subtle a gradient to read as a lit volume against the warm/golden grade;
                         # a wider 0.34..1.0 spread gives the shell-vs-core falloff more bite.


def bark_tube(verts, faces, uvs, b, segments, rng):
    """build_flora.branch_tube geometry (identical verts, no rng draw) with WORLD-SCALED UVs.

    build_flora spreads a fixed 1.5 U round every limb whatever its girth, so the 0.5 m trunk
    stretched the bark 3x sideways and twigs squashed it; each tube also restarted V at 0, which
    is the diagonal seam at every fork. Here U wraps an INTEGER number of repeats (no seam) and V
    runs in metres from a per-limb random phase, so texels stay square on every limb."""
    rings = 6
    base = len(verts)
    r_mid = 0.5 * (b.radius + b.tip_radius)
    u_rep = max(1, int(round(TAU * r_mid / BARK_TILE_M)))
    tile_v = TAU * r_mid / u_rep                       # metres per repeat along the limb
    phase = (hash((round(b.start[0], 3), round(b.start[2], 3), b.depth)) % 997) / 997.0
    for r in range(rings):
        t = r / (rings - 1)
        pos = b.point_at(t)
        radius = b.radius + (b.tip_radius - b.radius) * t
        s2, u2, _ = F._basis(b.direction)
        for k in range(segments):
            a = k / segments * TAU
            lobe = 1.0 + 0.06 * math.sin(a * 5.0 + b.depth) + 0.03 * math.sin(a * 3.0 - t * 4.0)
            p = pos + (s2 * math.cos(a) + u2 * math.sin(a)) * radius * lobe
            verts.append(S.u2b(p[0], p[1], p[2]))
    for r in range(rings - 1):
        for k in range(segments):
            k2 = (k + 1) % segments
            a = base + r * segments + k
            bb = base + r * segments + k2
            c = base + (r + 1) * segments + k2
            dd = base + (r + 1) * segments + k
            faces.append((a, bb, c, dd))
            u0 = k / segments * u_rep
            u1 = (k + 1) / segments * u_rep
            v0 = phase + (r / (rings - 1)) * b.length / tile_v
            v1 = phase + ((r + 1) / (rings - 1)) * b.length / tile_v
            uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])


def bake_crown_ao(obj, cv):
    """Cheap crown occlusion into COLOR_0: cards deep inside the crown and on its underside go
    darker, so the canopy reads as a volume instead of a flat noisy sheet."""
    P = np.asarray(cv, dtype=np.float64)   # cv is already Blender coords (u2b)
    c = P.mean(axis=0)
    d = P - c
    rad = np.linalg.norm(d * np.array([1.0, 1.0, 0.8]), axis=1)
    rmax = max(1e-3, np.percentile(rad, 96))
    x = np.clip(rad / rmax, 0.0, 1.0)
    shell = x * x * (3 - 2 * x)
    up = np.clip(d[:, 2] / rmax * 0.5 + 0.5, 0.0, 1.0)             # Blender Z = up
    ao = CROWN_AO_MIN + (1.0 - CROWN_AO_MIN) * np.clip(shell * 0.75 + up * 0.35, 0.0, 1.0)
    me = obj.data
    attr = me.color_attributes.new(name="Col", type='BYTE_COLOR', domain='POINT')
    cols = np.repeat(ao[:, None], 4, axis=1).astype(np.float32)
    cols[:, 3] = 1.0
    attr.data.foreach_set("color", cols.reshape(-1))
    me.color_attributes.active_color = attr
    print(f"[sakura-e4] crown AO {obj.name}: min {ao.min():.2f} mean {ao.mean():.2f}")


def build_tree_card8(name, seed=1, height=7.5, max_depth=5, blossom_density=1.0,
                     leaf_ratio=0.28, lean=0.10):
    """build_flora.build_sakura_tree with every canopy card in ONE mesh on the 8-cell atlas."""
    rng = random.Random(seed)
    trunk_dir = np.array([rng.uniform(-lean, lean), 1.0, rng.uniform(-lean, lean)])
    trunk_dir /= np.linalg.norm(trunk_dir)
    trunk = F.Branch((0.0, 0.0, 0.0), trunk_dir, height * 0.40, height * 0.058, 0)
    branches = F.grow(rng, trunk, max_depth)

    wv, wf, wu = [], [], []
    for b in branches:
        bark_tube(wv, wf, wu, b, max(6, 14 - b.depth * 2), rng)
    wood = S.mesh_from_arrays(f"{name}_Wood", wv, wf, uvs=wu)
    S.assign_material(wood, F.bark_material())

    root_profile = [(height * 0.058 * 0.94, 0.34)]
    for i in range(1, 8):
        t = i / 7.0
        root_profile.append((height * 0.058 * (1.0 + 1.25 * t ** 2.2), 0.34 - 0.95 * t))
    roots = S.lathe(f"{name}_Roots", root_profile, segments=16, uv_scale=(2.0, 1.0))
    S.assign_material(roots, F.bark_material())
    wood = S.join([wood, roots], f"{name}_Wood")

    tips = [b for b in branches if b.depth >= max_depth - 2]
    cv, cf, cu = [], [], []
    for b in tips:
        n_cluster = max(2, int(round(3 * blossom_density * rng.uniform(0.7, 1.3))))
        for _ in range(n_cluster):
            t = rng.uniform(0.18, 1.05)
            centre = b.point_at(min(t, 1.0)) + b.direction * (b.length * max(0.0, t - 1.0))
            centre = centre + np.array([rng.gauss(0, 0.42), rng.gauss(0, 0.30), rng.gauss(0, 0.42)])
            size = rng.uniform(0.85, 1.55)
            outward = centre - np.array([0.0, height * 0.55, 0.0])
            if np.linalg.norm(outward) < 1e-6:
                outward = np.array([1.0, 0.2, 0.0])
            outward /= np.linalg.norm(outward)
            for k in range(3):
                n = F._rotate(outward, np.array([0.0, 1.0, 0.0]), k * TAU / 3.0 + rng.uniform(-0.3, 0.3))
                n = n + np.array([0.0, rng.uniform(-0.35, 0.25), 0.0])
                right = np.cross(np.array([0.0, 1.0, 0.0]), n)
                if np.linalg.norm(right) < 1e-6:
                    right = np.array([1.0, 0.0, 0.0])
                if rng.random() < leaf_ratio * 0.5:      # full bloom: mostly blossom cells
                    cell = LEAF_CELLS[rng.randrange(len(LEAF_CELLS))]
                else:
                    cell = BLOSSOM_CELLS[rng.randrange(len(BLOSSOM_CELLS))]
                F.add_card(cv, cf, cu, centre, n, right, size, size, cell_uv_rect(cell),
                           droop=size * 0.16)
    canopy = S.mesh_from_arrays(f"{name}_Canopy", cv, cf, uvs=cu)
    bake_crown_ao(canopy, cv)
    S.assign_material(canopy, canopy_material())
    objs = [wood, canopy]
    for o in objs:
        S.report(o.name, o)
    return objs


VARIANTS = [
    ("SakuraPass_Sakura_Tree_A_Card8", dict(seed=3, height=8.2, max_depth=5, blossom_density=1.15)),
    ("SakuraPass_Sakura_Tree_B_Card8", dict(seed=17, height=6.6, max_depth=5, blossom_density=0.95,
                                            leaf_ratio=0.36, lean=0.16)),
    ("SakuraPass_Sakura_Tree_C_Card8", dict(seed=42, height=9.4, max_depth=6, blossom_density=0.85,
                                            leaf_ratio=0.22)),
    ("SakuraPass_Sakura_Sapling_Card8", dict(seed=88, height=3.6, max_depth=4, blossom_density=1.0,
                                             leaf_ratio=0.45, lean=0.2)),
]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "trees" not in argv:
        bake_atlas()
    for name, kw in VARIANTS:
        S.reset_scene()
        print(f"[sakura-e4] growing {name}...")
        S.export_glb(build_tree_card8(name, **kw), f"{name}.glb")
    print("[sakura-e4] done.")


if __name__ == "__main__":
    main()

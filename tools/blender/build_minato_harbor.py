"""
MINATO COAST - HARBOR workstream asset families (zone 1 "Harbor Awakens" + Port Gate + sea traffic).

Run:  tools/blender-4.5.10-windows-x64/blender.exe -b --factory-startup -P tools/blender/build_minato_harbor.py
      (optional trailing "-- name1 name2" to rebuild only some families)

Everything here is authored in UNITY-local coordinates through ``minato_lib`` / a small local
mesh builder and exported with minato_lib's measured clean-root FBX settings, so Unity places
each piece with an identity root. Conventions per family (all origins at ground / waterline):

  * Cranes, gantries:   +Z = the boom / seaward direction, X = along the quay, y = 0 quay deck.
  * Vessels:            +Z = bow, y = 0 = design waterline (hulls continue a few m below it).
  * Kerbs, barriers, breakwater segments, viaduct spans: +Z = along the run.

Material SLOT NAMES are the contract with Unity: MinatoCoastEnvironment.RedesignHarbor re-skins
every imported slot by token ("harbor_coral", "harbor_c_navy", ...) with its own CelLit
materials, so the Blender colours below are only preview colours.

LODs: repeated/large pieces ship _LOD1/_LOD2. The container-heavy pieces build their coarser
LODs STRUCTURALLY (one box per container column / per bay) instead of by collapse-decimation,
because decimating hundreds of separate boxes just shreds them.

Triangle budgets (LOD0): STS crane <= 25k, ship <= 16k, everything repeated a few hundred to
a few thousand tris. Real numbers are printed at export.
"""

import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402

u2b = S.u2b

# Preview colours for the harbor slots (Unity re-skins by slot name; see module docstring).
M.PALETTE.update({
    "harbor_coral":    (0.930, 0.400, 0.240, 1.0),
    "harbor_white":    (0.920, 0.920, 0.900, 1.0),
    "harbor_dark":     (0.150, 0.160, 0.180, 1.0),
    "harbor_yellow":   (0.950, 0.740, 0.120, 1.0),
    "harbor_glass":    (0.200, 0.320, 0.400, 1.0),
    "harbor_navy":     (0.070, 0.130, 0.290, 1.0),
    "harbor_red":      (0.560, 0.130, 0.100, 1.0),
    "harbor_deck":     (0.330, 0.360, 0.340, 1.0),
    "harbor_concrete": (0.720, 0.710, 0.680, 1.0),
    "harbor_rock":     (0.480, 0.470, 0.450, 1.0),
    "harbor_hazard":   (0.900, 0.700, 0.150, 1.0),
    "harbor_chevron":  (0.900, 0.700, 0.150, 1.0),
    "harbor_buoy":     (0.850, 0.150, 0.120, 1.0),
    "harbor_lamp":     (1.000, 0.950, 0.800, 1.0),
    "harbor_steelblue": (0.300, 0.380, 0.460, 1.0),
    "harbor_rail":     (0.300, 0.280, 0.260, 1.0),
    "harbor_loco":     (0.120, 0.300, 0.720, 1.0),
    "harbor_sign":     (0.100, 0.250, 0.600, 1.0),
    "harbor_c_navy":   (0.100, 0.180, 0.420, 1.0),
    "harbor_c_cyan":   (0.150, 0.620, 0.720, 1.0),
    "harbor_c_coral":  (0.880, 0.360, 0.240, 1.0),
    "harbor_c_white":  (0.900, 0.900, 0.880, 1.0),
    "harbor_c_grey":   (0.550, 0.570, 0.590, 1.0),
    "harbor_c_rust":   (0.580, 0.220, 0.160, 1.0),
})

CONTAINER_SLOTS = ["harbor_c_navy", "harbor_c_cyan", "harbor_c_coral",
                   "harbor_c_white", "harbor_c_grey", "harbor_c_rust"]
CONTAINER_WEIGHTS = [0.24, 0.16, 0.18, 0.12, 0.14, 0.16]

TEX_DIR = M.textures_dir()


# ============================================================================ mesh builder
#
# Most harbor geometry is thousands of small boxes (containers) or lofted rings (hulls). Making
# one Blender object per box and joining is slow and the joined mesh loses control of UVs, so
# this accumulates quads straight into arrays in UNITY space and emits one multi-material mesh.
# Each quad carries its intended outward normal; the builder flips winding per quad so faces
# point the right way without a flood-fill recalc (which misfires on stacks of touching boxes).

class MB:
    def __init__(self):
        self.v = []
        self.f = []
        self.uv = []
        self.mi = []
        self.slots = []

    def slot(self, name):
        if name not in self.slots:
            self.slots.append(name)
        return self.slots.index(name)

    def poly(self, pts, uvs, slot, normal=None):
        """pts in Unity space. normal (Unity) = intended outward direction, or None."""
        bp = [Vector(u2b(*p)) for p in pts]
        if normal is not None and len(bp) >= 3:
            nb = (bp[1] - bp[0]).cross(bp[2] - bp[0])
            if nb.length < 1e-12 and len(bp) > 3:
                nb = (bp[2] - bp[0]).cross(bp[3] - bp[0])
            if nb.dot(Vector(u2b(*normal))) < 0:
                bp = bp[::-1]
                uvs = uvs[::-1]
        base = len(self.v)
        self.v.extend(bp)
        self.f.append(tuple(range(base, base + len(bp))))
        self.uv.extend(uvs)
        self.mi.append(self.slot(slot))

    def quad(self, a, b, c, d, slot, normal=None, uvs=None, uv_per_m=0.35):
        if uvs is None:
            w = (Vector(b) - Vector(a)).length * uv_per_m
            h = (Vector(d) - Vector(a)).length * uv_per_m
            uvs = [(0, 0), (w, 0), (w, h), (0, h)]
        self.poly([a, b, c, d], uvs, slot, normal)

    def box(self, c, size, slot, faces="all", uvfn=None, uv_per_m=0.35):
        """Axis-aligned box. faces: 'all' or a string of letters from 'xXyYzZ' (lower = -)."""
        cx, cy, cz = c
        hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
        want = "xXyYzZ" if faces == "all" else faces
        P = lambda sx, sy, sz: (cx + sx * hx, cy + sy * hy, cz + sz * hz)  # noqa: E731
        spec = {
            "X": ([P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1)], (1, 0, 0), (size[2], size[1])),
            "x": ([P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1)], (-1, 0, 0), (size[2], size[1])),
            "Z": ([P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1)], (0, 0, 1), (size[0], size[1])),
            "z": ([P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1)], (0, 0, -1), (size[0], size[1])),
            "Y": ([P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1)], (0, 1, 0), (size[0], size[2])),
            "y": ([P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1)], (0, -1, 0), (size[0], size[2])),
        }
        for k in want:
            pts, n, (w, h) = spec[k]
            if uvfn is not None:
                uvs = uvfn(k, w, h)
            else:
                uvs = [(0, 0), (w * uv_per_m, 0), (w * uv_per_m, h * uv_per_m), (0, h * uv_per_m)]
            self.poly(pts, uvs, slot, n)

    def build(self, name):
        mesh = bpy.data.meshes.new(name + "_Mesh")
        mesh.from_pydata([tuple(p) for p in self.v], [], self.f)
        layer = mesh.uv_layers.new(name="UVMap")
        layer.data.foreach_set("uv", np.asarray(self.uv, dtype=np.float32).reshape(-1))
        for s in self.slots:
            mesh.materials.append(M.mat(s))
        mesh.polygons.foreach_set("material_index", np.asarray(self.mi, dtype=np.int32))
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        S.link(obj)
        return obj


def shift(obj, ux, uy, uz):
    """Translate a mesh's vertices by a UNITY-space offset (lathe() builds about the origin)."""
    d = Vector(u2b(ux, uy, uz))
    for v in obj.data.vertices:
        v.co += d
    obj.data.update()
    return obj


def lathe_at(name, profile, x, y, z, segments=12):
    """sakura_lib.lathe about Unity +Y, then moved to (x, y, z)."""
    return shift(S.lathe(name, profile, segments=segments), x, y, z)


def smoothstep(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def finish_slot(parts, slot, name, bevel=0.03, smooth=32.0):
    obj = M.finish(parts, name, bevel=bevel, smooth_angle=smooth)
    if obj is not None:
        obj.data.materials.clear()
        M.paint(obj, slot)
    return obj


def tri_count(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


# ============================================================================ textures

def save_png(name, rgb):
    """rgb: HxWx3 float array, row 0 = TOP of the image."""
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(rgb[::-1], 0.0, 1.0)      # Blender stores bottom row first
    img.pixels.foreach_set(rgba.reshape(-1))
    path = os.path.join(TEX_DIR, name + ".png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    print(f"[harbor] texture {name}.png {w}x{h}")
    return path


def _noise(h, w, scale, seed):
    """Cheap value-noise fbm (tileable enough for our use) in [0, 1]."""
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w), dtype=np.float32)
    amp, tot = 1.0, 0.0
    for o in range(4):
        gh, gw = max(2, int(h / scale * 2 ** o)), max(2, int(w / scale * 2 ** o))
        g = rng.random((gh + 1, gw + 1)).astype(np.float32)
        yy = np.linspace(0, gh, h, endpoint=False)
        xx = np.linspace(0, gw, w, endpoint=False)
        y0, x0 = yy.astype(int), xx.astype(int)
        ty, tx = (yy - y0)[:, None], (xx - x0)[None, :]
        ty, tx = ty * ty * (3 - 2 * ty), tx * tx * (3 - 2 * tx)
        a = g[y0][:, x0]; b = g[y0][:, x0 + 1]
        c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
        out += amp * ((a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty)
        tot += amp
        amp *= 0.5
    return out / tot


def container_texture():
    """
    Greyscale-white container atlas, tinted per livery by the Unity material colour.
      u 0.00-0.70 : long side (corrugated), 40 ft maps the full band, 20 ft half of it
      u 0.70-0.86 : door end (two leaves, locking bars)
      u 0.86-1.00 : front end (corrugated)
    Weathering (rust runs from the top rail, salt bloom at the base, grime) is painted here
    because it has to survive the tint - the cel shader's own weathering is too uniform for
    boxes this size.
    """
    W, H = 1024, 256
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H           # 0 = top row here
    U, V = np.meshgrid(u, v)
    val = np.full((H, W), 0.93, dtype=np.float32)

    side = U < 0.70
    door = (U >= 0.70) & (U < 0.86)
    front = U >= 0.86
    # corrugation: trapezoidal ribs across the side panels
    ribs = 0.5 + 0.5 * np.sin(U / 0.70 * 2 * math.pi * 44.0)
    ribs = np.clip((ribs - 0.5) * 2.2 + 0.5, 0, 1)
    val = np.where(side, 0.80 + 0.17 * ribs, val)
    fr = 0.5 + 0.5 * np.sin((U - 0.86) / 0.14 * 2 * math.pi * 7.0)
    val = np.where(front, 0.82 + 0.14 * fr, val)
    # door: two leaves, recessed panel ribs, locking bars
    du = (U - 0.70) / 0.16
    dribs = 0.5 + 0.5 * np.sin(V * 2 * math.pi * 6.0)
    val = np.where(door, 0.84 + 0.10 * dribs, val)
    for bar in (0.14, 0.36, 0.64, 0.86):
        m = door & (np.abs(du - bar) < 0.018)
        val = np.where(m, 0.58, val)
    val = np.where(door & (np.abs(du - 0.5) < 0.006), 0.35, val)
    # rails and corner posts (all regions)
    val = np.where(V < 0.06, val * 0.72, val)            # top rail
    val = np.where(V > 0.93, val * 0.62, val)            # bottom rail
    for edge in (0.0, 0.70, 0.86, 1.0):
        val = np.where(np.abs(U - edge) < 0.006, val * 0.62, val)
    rgb = np.stack([val, val, val], axis=-1)

    # grime and salt
    n = _noise(H, W, 48.0, 7)
    rgb *= (0.90 + 0.16 * n)[:, :, None]
    salt = np.clip((V - 0.72) / 0.25, 0, 1) * np.clip(_noise(H, W, 24.0, 9) * 1.6 - 0.5, 0, 1)
    rgb = rgb * (1 - 0.35 * salt[:, :, None]) + 0.97 * 0.35 * salt[:, :, None]
    # rust runs hanging from the top rail
    rng = np.random.default_rng(3)
    rust = np.array([0.62, 0.40, 0.28], dtype=np.float32)
    for _ in range(90):
        x = rng.integers(0, W)
        L = rng.uniform(0.15, 0.7)
        wpx = rng.integers(1, 4)
        strength = rng.uniform(0.25, 0.6)
        ys = int(H * 0.05)
        ye = int(H * min(0.95, 0.05 + L))
        prof = np.linspace(1, 0, ye - ys)[:, None] ** 1.5 * strength
        seg = rgb[ys:ye, max(0, x - wpx):x + wpx]
        seg[:] = seg * (1 - prof[:, :, None] if seg.ndim == 3 else 1) + rust * prof[:, :, None]
    return save_png("Minato_Harbor_Container_Albedo", rgb)


def hazard_texture():
    """Diagonal yellow/black hazard stripes, one period per UV unit, scuffed."""
    W = H = 256
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    band = ((U + V) * 2.0) % 1.0 < 0.5
    yel = np.array([0.96, 0.74, 0.10], dtype=np.float32)
    blk = np.array([0.08, 0.08, 0.09], dtype=np.float32)
    rgb = np.where(band[:, :, None], yel, blk).astype(np.float32)
    n = _noise(H, W, 32.0, 21)
    scuff = np.clip(n * 1.8 - 1.05, 0, 1)[:, :, None]
    rgb = rgb * (0.88 + 0.14 * n[:, :, None])
    rgb = rgb * (1 - scuff) + np.array([0.62, 0.61, 0.58]) * scuff
    return save_png("Minato_Harbor_Hazard_Albedo", rgb)


def chevron_texture():
    """Black board with yellow chevrons pointing +U (the barrier faces carry it)."""
    W, H = 512, 128
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    k = (U * 4.0 - np.abs(V - 0.5) * 1.1) % 1.0
    chev = (k < 0.42) & (V > 0.10) & (V < 0.90)
    yel = np.array([0.97, 0.72, 0.08], dtype=np.float32)
    blk = np.array([0.07, 0.07, 0.08], dtype=np.float32)
    rgb = np.where(chev[:, :, None], yel, blk).astype(np.float32)
    n = _noise(H, W, 24.0, 5)
    rgb = rgb * (0.9 + 0.12 * n[:, :, None])
    return save_png("Minato_Harbor_Chevron_Albedo", rgb)


def sign_texture():
    """Marine sign face: navy board, white border, white wave emblem and 'text' bars."""
    W, H = 512, 256
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    navy = np.array([0.08, 0.22, 0.58], dtype=np.float32)
    white = np.array([0.96, 0.97, 0.98], dtype=np.float32)
    rgb = np.broadcast_to(navy, (H, W, 3)).copy()
    border = (U < 0.025) | (U > 0.975) | (V < 0.05) | (V > 0.95)
    inner = (U > 0.04) & (U < 0.96) & (V > 0.08) & (V < 0.92)
    rgb[border] = white
    rgb[~border & ~inner] = navy * 0.8
    # three wave strokes in the left emblem square
    for k, vc in enumerate((0.34, 0.50, 0.66)):
        wave = vc + 0.05 * np.sin((U - 0.06) / 0.26 * 2 * math.pi * 1.5)
        m = (U > 0.07) & (U < 0.31) & (np.abs(V - wave) < 0.028)
        rgb[m] = white
    # "lettering" bars
    for vc, length in ((0.34, 0.58), (0.54, 0.48), (0.72, 0.36)):
        m = (U > 0.36) & (U < 0.36 + length) & (np.abs(V - vc) < (0.055 if vc < 0.4 else 0.035))
        rgb[m] = white
    # break the bars into "words"
    for gap in (0.52, 0.66, 0.78):
        m = (np.abs(U - gap) < 0.008) & (V > 0.25) & (V < 0.8)
        rgb[m] = navy
    return save_png("Minato_Harbor_Sign_Albedo", rgb)


# ============================================================================ containers

C40, C20, CW, CH = 12.19, 6.06, 2.44, 2.59


def container_uv(length):
    """UV function for a container box aligned with its length along Z."""
    span = 0.70 * (length / C40)

    def fn(face, w, h):
        if face in "xX":                       # long sides
            return [(0, 0), (span, 0), (span, 1), (0, 1)]
        if face == "z":                        # door end (rear)
            return [(0.70, 0), (0.86, 0), (0.86, 1), (0.70, 1)]
        if face == "Z":                        # front end
            return [(0.86, 0), (1.0, 0), (1.0, 1), (0.86, 1)]
        return [(0, 0.3), (0.12, 0.3), (0.12, 0.7), (0, 0.7)]   # roof / floor
    return fn


def add_container(mb, x, y, z, length, slot, along_x=False, top=True, bottom=False):
    """One container (base at y). Hidden interior faces are skipped by the caller's flags."""
    faces = "xXzZ" + ("Y" if top else "") + ("y" if bottom else "")
    if along_x:
        # rotate the UV convention: long sides are the Z faces
        span = 0.70 * (length / C40)

        def fn(face, w, h):
            if face in "zZ":
                return [(0, 0), (span, 0), (span, 1), (0, 1)]
            if face == "x":
                return [(0.70, 0), (0.86, 0), (0.86, 1), (0.70, 1)]
            if face == "X":
                return [(0.86, 0), (1.0, 0), (1.0, 1), (0.86, 1)]
            return [(0, 0.3), (0.12, 0.3), (0.12, 0.7), (0, 0.7)]
        mb.box((x, y + CH * 0.5, z), (length, CH, CW), slot, faces, uvfn=fn)
    else:
        mb.box((x, y + CH * 0.5, z), (CW, CH, length), slot, faces, uvfn=container_uv(length))


def pick_slot(rng):
    return rng.choices(CONTAINER_SLOTS, weights=CONTAINER_WEIGHTS)[0]


def container_block(tag, rows, bays, tiers_lo, tiers_hi, seed, length=C40):
    """
    A terminal stack block: ``rows`` across X, ``bays`` along Z, stack heights varying so the
    block has a stepped skyline instead of a brick. Origin at the block's ground centre.
    Returns the LOD0/LOD1/LOD2 objects.
    """
    rng = random.Random(seed)
    pitch_x, pitch_z = CW + 0.28, length + 0.9
    heights = {}
    for b in range(bays):
        base = rng.randint(tiers_lo, tiers_hi)
        for r in range(rows):
            h = base + rng.choice((-1, 0, 0, 0, 1))
            if rng.random() < 0.06:
                h = rng.randint(0, 1)
            heights[(r, b)] = max(0, min(tiers_hi, h))
    x0 = -(rows - 1) * pitch_x * 0.5
    z0 = -(bays - 1) * pitch_z * 0.5
    lod0, lod1, lod2 = MB(), MB(), MB()
    top_slot = {}
    for (r, b), h in heights.items():
        x, z = x0 + r * pitch_x, z0 + b * pitch_z
        col_slot = pick_slot(rng)
        for t in range(h):
            slot = col_slot if rng.random() < 0.45 else pick_slot(rng)
            add_container(lod0, x, t * (CH + 0.02), z, length, slot, top=(t == h - 1))
            top_slot[(r, b)] = slot
        if h:
            lod1.box((x, h * (CH + 0.02) * 0.5, z), (CW, h * (CH + 0.02), length),
                     top_slot[(r, b)], "xXzZY", uvfn=lambda f, w, hh, L=length: container_uv(L)(f, w, hh))
    for b in range(bays):
        hs = [heights[(r, b)] for r in range(rows)]
        hm = sorted(hs)[len(hs) // 2]
        if hm:
            z = z0 + b * pitch_z
            lod2.box((0, hm * CH * 0.5, z), (rows * pitch_x, hm * CH, length), top_slot.get((rows // 2, b), CONTAINER_SLOTS[0]),
                     "xXzZY", uv_per_m=0.2)
    name = f"Minato_Harbor_ContainerBlock{tag}"
    return [lod0.build(name + "_LOD0"), lod1.build(name + "_LOD1"), lod2.build(name + "_LOD2")]


def container_wall(tag, length_m, tiers, seed):
    """Port Gate container WALL: one row of containers stacked along Z (the road-side wall)."""
    rng = random.Random(seed)
    lod0, lod1 = MB(), MB()
    z = -length_m * 0.5
    while z < length_m * 0.5 - 1:
        L = C40 if rng.random() < 0.7 else C20
        h = tiers + rng.choice((-1, 0, 0, 1))
        h = max(1, h)
        slot = pick_slot(rng)
        for t in range(h):
            s = slot if rng.random() < 0.5 else pick_slot(rng)
            add_container(lod0, 0.0, t * (CH + 0.02), z + L * 0.5, L, s, top=(t == h - 1))
        lod1.box((0.0, h * CH * 0.5, z + L * 0.5), (CW, h * CH, L), slot, "xXzZY",
                 uvfn=lambda f, w, hh, LL=L: container_uv(LL)(f, w, hh))
        z += L + 0.35
    name = f"Minato_Harbor_ContainerWall{tag}"
    return [lod0.build(name + "_LOD0"), lod1.build(name + "_LOD1")]


# ============================================================================ STS crane

def sts_crane(raised=False):
    """
    Ship-to-shore gantry container crane, modelled after the coral cranes in the targets:
    four legs on a 30.5 m rail gauge, portal frame with side X-bracing, twin lattice boom girders
    running from the backreach over the machinery house out 57 m past the waterside legs, an
    A-frame with fore/back stays, trolley with hanging operator cab, spreader on ropes, bogies.
    ``raised`` lifts the waterside boom 78 deg (the classic parked silhouette).
    """
    LZ, LX = 15.25, 9.0          # waterside legs at +LZ, landside at -LZ
    PY = 40.0                    # portal frame top
    GY = 44.2                    # boom girder centreline height
    GX = 3.6                     # girder half-spacing
    HALF = 1.8                   # girder half-depth
    BACK, TIP = -36.0, 72.0

    coral, white, dark, yellow, glass, hazard = [], [], [], [], [], []

    # --- legs, bogies, hazard bands
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * LX, sz * LZ
            coral.append(M.beam(f"leg{sx}{sz}", (x, 2.2, z), (x, PY + 1.0, z), 2.4, 2.4,
                                up_hint=(0, 0, 1), uv_per_m=0.5, taper=0.92))
            hazard.append(M.beam(f"hz{sx}{sz}", (x, 2.2, z), (x, 6.2, z), 2.56, 2.56,
                                 up_hint=(0, 0, 1), uv_per_m=1.0))
            yellow.append(M.box(f"eq{sx}{sz}", (x, 1.55, z), (6.4, 1.0, 2.0), uv_per_m=0.6))
            yellow.append(M.box(f"cap{sx}{sz}", (x, 2.25, z), (3.0, 0.6, 2.8), uv_per_m=0.6))
            for k in range(4):
                wx = x - 2.4 + k * 1.6
                dark.append(M.tube(f"wh{sx}{sz}{k}", (wx, 0.5, z - 0.75), (wx, 0.5, z + 0.75),
                                   0.5, segments=8, up_hint=(0, 1, 0)))
    # --- portal frame: X-direction beams at both leg lines, Z-direction side beams
    for sz in (-1, 1):
        coral.append(M.beam(f"portal{sz}", (-LX - 1.3, PY + 1.3, sz * LZ), (LX + 1.3, PY + 1.3, sz * LZ),
                            2.8, 2.6, uv_per_m=0.4))
    for sx in (-1, 1):
        x = sx * LX
        coral.append(M.beam(f"side{sx}", (x, PY + 1.3, -LZ - 1.3), (x, PY + 1.3, LZ + 1.3),
                            2.4, 2.6, uv_per_m=0.4))
        coral.append(M.beam(f"tie{sx}", (x, 16.0, -LZ), (x, 16.0, LZ), 1.4, 1.6, uv_per_m=0.5))
        for (a, b) in (((x, 16.0, -LZ), (x, PY, LZ)), ((x, 16.0, LZ), (x, PY, -LZ)),
                       ((x, 3.2, -LZ), (x, 16.0, -LZ + 7.0)), ((x, 3.2, LZ), (x, 16.0, LZ - 7.0))):
            coral.append(M.beam(f"xb{sx}{a[1]:.0f}{a[2]:.0f}", a, b, 1.0, 1.0, uv_per_m=0.6))
    coral.append(M.beam("sill", (-LX, 11.0, -LZ), (LX, 11.0, -LZ), 1.4, 1.6, uv_per_m=0.5))

    # --- girders: solid backreach box girders + lattice boom over the water
    hinge_z = LZ + 1.5
    blen = TIP - hinge_z
    ang = math.radians(78.0) if raised else 0.0
    fwd = Vector((0.0, math.sin(ang), math.cos(ang)))
    upv = Vector((0.0, math.cos(ang), -math.sin(ang)))

    def bpt(sx, t, dy):
        """Point on the waterside boom: t metres from the hinge, dy along the boom's up."""
        return Vector((sx * GX, GY, hinge_z)) + fwd * t + upv * dy

    for sx in (-1, 1):
        x = sx * GX
        coral.append(M.beam(f"bg{sx}", (x, GY, BACK), (x, GY, hinge_z), 1.6, HALF * 2, uv_per_m=0.4))
        # lattice boom: top + bottom chords, verticals + alternating diagonals
        top = [bpt(sx, blen * i / 12, HALF - 0.3) for i in range(13)]
        bot = [bpt(sx, blen * i / 12, -HALF + 0.3) for i in range(13)]
        for i in range(12):
            tp = 1.0 if i < 9 else 0.8
            coral.append(M.beam(f"tc{sx}{i}", top[i], top[i + 1], 1.3 * tp, 0.9, uv_per_m=0.5,
                                up_hint=tuple(upv)))
            coral.append(M.beam(f"bc{sx}{i}", bot[i], bot[i + 1], 1.3 * tp, 0.9, uv_per_m=0.5,
                                up_hint=tuple(upv)))
            coral.append(M.beam(f"vt{sx}{i}", bot[i], top[i], 0.55, 0.55, uv_per_m=0.6,
                                up_hint=tuple(fwd)))
            a, b = (bot[i], top[i + 1]) if i % 2 == 0 else (top[i], bot[i + 1])
            coral.append(M.beam(f"dg{sx}{i}", a, b, 0.45, 0.45, uv_per_m=0.6, up_hint=(1, 0, 0)))
        coral.append(M.beam(f"vtE{sx}", bot[12], top[12], 0.55, 0.55, up_hint=tuple(fwd)))
    # cross ties between the girders (backreach + boom)
    for z in range(int(BACK) + 2, int(hinge_z), 7):
        coral.append(M.beam(f"xt{z}", (-GX, GY + HALF - 0.2, z), (GX, GY + HALF - 0.2, z), 0.5, 0.5))
    for i in range(1, 13, 2):
        a, b = bpt(-1, blen * i / 12, HALF - 0.3), bpt(1, blen * i / 12, HALF - 0.3)
        coral.append(M.beam(f"bxt{i}", a, b, 0.5, 0.5))
        a, b = bpt(-1, blen * i / 12, -HALF + 0.3), bpt(1, blen * i / 12, -HALF + 0.3)
        coral.append(M.beam(f"bxb{i}", a, b, 0.45, 0.45))
    tipc = bpt(0, blen, 0.0)
    coral.append(M.beam("tiphead", tipc - Vector((GX + 1.0, 0, 0)), tipc + Vector((GX + 1.0, 0, 0)),
                        2.2, HALF * 2.2, up_hint=tuple(upv)))
    coral.append(M.box("counter", (0.0, GY - 0.4, BACK - 0.8), (GX * 2 + 3.0, 4.4, 2.6)))

    # --- A-frame and stays
    APY, APZ = 80.0, -4.0
    for sx in (-1, 1):
        base_f = (sx * 5.6, PY + 2.6, LZ)
        base_b = (sx * 5.6, PY + 2.6, -LZ)
        apex = (sx * 4.3, APY, APZ)
        coral.append(M.beam(f"af{sx}", base_f, apex, 1.9, 1.9, uv_per_m=0.5, up_hint=(1, 0, 0)))
        coral.append(M.beam(f"ab{sx}", base_b, apex, 1.9, 1.9, uv_per_m=0.5, up_hint=(1, 0, 0)))
        # mid-height strut and a diagonal inside each A
        fm = Vector(base_f).lerp(Vector(apex), 0.45)
        bm = Vector(base_b).lerp(Vector(apex), 0.45)
        coral.append(M.beam(f"am{sx}", fm, bm, 0.9, 0.9, up_hint=(1, 0, 0)))
        coral.append(M.beam(f"ad{sx}", Vector(base_b), fm, 0.7, 0.7, up_hint=(1, 0, 0)))
        # stays: forestays to the boom (two points), backstay to the backreach end
        for t in (0.55, 1.0):
            coral.append(M.tube(f"fs{sx}{t}", apex, bpt(sx, blen * t, HALF), 0.34, segments=6))
        coral.append(M.tube(f"bs{sx}", apex, (sx * GX, GY + HALF, BACK + 1.0), 0.38, segments=6))
    for y in (APY, APY - 17.0):
        t = (y - (PY + 2.6)) / (APY - (PY + 2.6))
        xw = lerp(5.6, 4.3, t)
        zc = lerp(LZ, APZ, t)
        coral.append(M.beam(f"ax{y:.0f}", (-xw, y, zc), (xw, y, zc), 1.5, 1.5))
    coral.append(M.box("apexhead", (0.0, APY + 1.3, APZ), (10.4, 2.0, 3.0)))

    # --- machinery house, electrical room, stair tower, trolley + cab + spreader
    white.append(M.box("house", (0.0, GY + HALF + 2.8, -27.0), (10.2, 5.6, 13.0), uv_per_m=0.5))
    white.append(M.box("houseroof", (0.0, GY + HALF + 5.9, -27.0), (10.6, 0.5, 13.4), uv_per_m=0.5))
    white.append(M.box("eroom", (0.0, PY - 2.2, -LZ - 3.4), (8.0, 3.6, 4.0), uv_per_m=0.5))
    white.append(M.box("stair", (LX + 2.3, 20.5, -LZ), (2.2, 37.0, 2.2), uv_per_m=0.5))
    for y in (8.0, 16.0, 24.0, 32.0):
        dark.append(M.box(f"land{y}", (LX + 2.3, y, -LZ), (2.8, 0.25, 2.8), uv_per_m=0.5))
    if not raised:
        zt = 30.0
        tro = bpt(0, zt - hinge_z, 0.0)
        white.append(M.box("trolley", (0.0, tro.y + 0.3, tro.z), (6.4, 2.4, 6.2), uv_per_m=0.5))
        white.append(M.box("cab", (-1.6, tro.y - 3.4, tro.z + 1.6), (2.8, 2.8, 3.4), uv_per_m=0.6))
        glass.append(M.box("cabglass", (-1.6, tro.y - 3.3, tro.z + 3.32), (2.5, 1.7, 0.08)))
        glass.append(M.box("cabglass2", (-3.02, tro.y - 3.3, tro.z + 1.6), (0.08, 1.7, 3.0)))
        for dx in (-1.0, 1.0):
            for dz in (-2.0, 2.0):
                dark.append(M.tube(f"rope{dx}{dz}", (dx, tro.y - 0.9, tro.z + dz),
                                   (dx * 0.7, 23.2, tro.z + dz * 0.4), 0.06, segments=4))
        yellow.append(M.box("headblock", (0.0, 22.6, tro.z), (2.6, 1.3, 3.0), uv_per_m=0.6))
        yellow.append(M.box("spreader", (0.0, 21.5, tro.z), (2.6, 0.9, 12.4), uv_per_m=0.6))
    else:
        white.append(M.box("trolley", (0.0, GY + 0.3, 0.0), (6.4, 2.4, 6.2), uv_per_m=0.5))
        white.append(M.box("cab", (-1.6, GY - 3.1, 1.6), (2.8, 2.8, 3.4), uv_per_m=0.6))
        glass.append(M.box("cabglass", (-1.6, GY - 3.0, 3.32), (2.5, 1.7, 0.08)))

    parts = [finish_slot(coral, "harbor_coral", "c", bevel=0.05),
             finish_slot(white, "harbor_white", "w", bevel=0.05),
             finish_slot(dark, "harbor_dark", "d", bevel=0.0),
             finish_slot(yellow, "harbor_yellow", "y", bevel=0.04),
             finish_slot(glass, "harbor_glass", "g", bevel=0.0),
             finish_slot(hazard, "harbor_hazard", "h", bevel=0.0)]
    name = "Minato_Harbor_STSCrane" + ("Raised" if raised else "")
    return S.join([p for p in parts if p is not None], name + "_LOD0")


def rtg_crane():
    """Rubber-tyred yard gantry straddling 6 container rows - yard texture between the STS cranes."""
    SPAN, H, L = 11.0, 21.0, 6.5     # half-span across X, height, half-length along Z
    white, coral, dark, yellow = [], [], [], []
    for sx in (-1, 1):
        for sz in (-1, 1):
            coral.append(M.beam(f"lg{sx}{sz}", (sx * SPAN, 1.6, sz * L), (sx * SPAN, H, sz * L * 0.8),
                                1.2, 1.4, up_hint=(0, 0, 1), uv_per_m=0.5))
            for k in (-1, 1):
                dark.append(M.tube(f"ty{sx}{sz}{k}", (sx * SPAN - 0.5, 0.8, sz * L + k * 0.9),
                                   (sx * SPAN + 0.5, 0.8, sz * L + k * 0.9), 0.8, segments=10))
        coral.append(M.beam(f"sl{sx}", (sx * SPAN, 1.8, -L - 1), (sx * SPAN, 1.8, L + 1), 1.0, 1.2))
        coral.append(M.beam(f"tp{sx}", (sx * SPAN, H + 0.6, -L), (sx * SPAN, H + 0.6, L), 1.2, 1.4))
    for sz in (-1, 1):
        coral.append(M.beam(f"gd{sz}", (-SPAN - 0.8, H + 1.6, sz * L * 0.8), (SPAN + 0.8, H + 1.6, sz * L * 0.8),
                            1.4, 2.2, uv_per_m=0.4))
    white.append(M.box("trol", (3.0, H + 3.2, 0.0), (4.0, 1.8, L * 1.8)))
    white.append(M.box("eh", (-SPAN, H - 1.5, 0.0), (1.8, 3.0, 4.0)))
    yellow.append(M.box("spr", (3.0, 10.0, 0.0), (2.5, 0.8, 12.2)))
    for dz in (-1.5, 1.5):
        dark.append(M.tube(f"rp{dz}", (3.0, H + 2.3, dz), (3.0, 10.4, dz), 0.05, segments=4))
    parts = [finish_slot(coral, "harbor_coral", "c", 0.04), finish_slot(white, "harbor_white", "w", 0.04),
             finish_slot(dark, "harbor_dark", "d", 0.0), finish_slot(yellow, "harbor_yellow", "y", 0.03)]
    return S.join([p for p in parts if p], "Minato_Harbor_RTG_LOD0")


# ============================================================================ hulls

def hull_loft(mb, L, B, T, D, table, rake=0.0, forecastle=0.0, split_y=0.6,
              above="harbor_navy", below="harbor_red", deck="harbor_deck", band=None,
              stations=40):
    """
    Loft a displacement hull from a station table [(s, wl, dk, keel_rise)], s in [-0.5, 0.5]
    stern->bow, wl/dk = waterline/deck half-beam fraction, keel_rise lifts the keel (forefoot /
    stern cut-up). ``rake`` pushes the upper stem forward (metres at deck level), ``forecastle``
    raises the deck over the fore 12 %. Faces below ``split_y`` get the antifouling slot.
    ``band`` optionally paints the top strake (P5->P6) with an accent slot.
    """
    ss = [table[0][0] + (table[-1][0] - table[0][0]) * i / stations for i in range(stations + 1)]

    def interp(s):
        for i in range(len(table) - 1):
            if table[i][0] <= s <= table[i + 1][0]:
                t = (s - table[i][0]) / (table[i + 1][0] - table[i][0])
                return [lerp(table[i][k], table[i + 1][k], t) for k in (1, 2, 3)]
        return list(table[-1][1:])

    rings = []
    for s in ss:
        wl, dk, kr = interp(s)
        bw, bd = max(wl * B, 0.05), max(dk * B, 0.1)
        ky = -T * (1.0 - kr)
        Dz = D + forecastle * smoothstep(0.36, 0.44, s)
        half = [(0.0, ky), (bw * 0.72, ky), (bw * 0.96, ky + min(1.4, (split_y - ky) * 0.3)),
                (bw, ky + min(2.8, (split_y - ky) * 0.6)), (bw, split_y),
                (lerp(bw, bd, 0.55), lerp(split_y, Dz, 0.5)), (bd, Dz)]
        pts = [(-x, y) for (x, y) in reversed(half[1:])] + half
        z0 = s * L
        ring = []
        for (x, y) in pts:
            zr = z0 + rake * max(0.0, (s - 0.38) / 0.12) * ((y + T) / (Dz + T))
            ring.append((x, y, zr))
        rings.append(ring)
    n = len(rings[0])
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for j in range(n - 1):
            ym = (a[j][1] + a[j + 1][1] + b[j][1] + b[j + 1][1]) * 0.25
            xm = (a[j][0] + a[j + 1][0]) * 0.5
            slot = below if ym < split_y else above
            if band and ym > split_y and (j == 0 or j == n - 2):
                slot = band
            nrm = (xm, ym - (a[j][1] + a[n // 2][1]) * 0.5, 0.0)
            if abs(xm) < 0.05:
                nrm = (0.0, -1.0, 0.0)
            mb.quad(a[j], a[j + 1], b[j + 1], b[j], slot, normal=nrm, uv_per_m=0.12)
        # deck strip between the two deck edges
        mb.quad(a[0], a[-1], b[-1], b[0], deck, normal=(0, 1, 0), uv_per_m=0.1)
    # transom (stern) and stem closures as fans
    for ring, nz in ((rings[0], -1.0), (rings[-1], 1.0)):
        cx = 0.0
        cy = sum(p[1] for p in ring) / n
        cz = sum(p[2] for p in ring) / n
        for j in range(n - 1):
            ym = (ring[j][1] + ring[j + 1][1]) * 0.5
            mb.poly([ring[j], ring[j + 1], (cx, cy, cz)], [(0, 0), (1, 0), (0.5, 1)],
                    below if ym < split_y else above, normal=(0, 0, nz))
        mb.poly([ring[-1], ring[0], (cx, cy, cz)], [(0, 0), (1, 0), (0.5, 1)], deck, normal=(0, 0, nz))
    return rings


SHIP_TABLE = [(-0.50, 0.84, 0.92, 0.50), (-0.47, 0.90, 0.96, 0.32), (-0.42, 0.97, 0.99, 0.10),
              (-0.34, 1.0, 1.0, 0.0), (0.24, 1.0, 1.0, 0.0), (0.32, 0.96, 0.99, 0.0),
              (0.39, 0.84, 0.95, 0.04), (0.44, 0.60, 0.83, 0.14), (0.475, 0.34, 0.62, 0.30),
              (0.50, 0.04, 0.26, 0.52)]


def container_ship(detail=0, seed=11):
    """
    Laden post-panamax container ship (~210 m) - navy hull, red antifouling, white aft house,
    stepped multi-livery deck load. detail 0/1/2 = LOD0/1/2 (deck load built per container /
    per column / per bay rather than decimated).
    """
    L, B, T, D = 210.0, 16.0, 7.0, 9.0
    mb = MB()
    hull_loft(mb, L, B, T, D, SHIP_TABLE, rake=7.0, forecastle=3.0,
              stations=44 if detail == 0 else 22 if detail == 1 else 12)
    rng = random.Random(seed)
    zs = -L / 2
    # aft house, bridge, funnel, lifeboats
    mb.box((0.0, D + 9.0, zs + 28.0), (24.0, 18.0, 13.0), "harbor_white", "xXzZY")
    if detail < 2:
        for k in range(6):
            mb.box((0.0, D + 2.2 + k * 2.85, zs + 28.0), (24.15, 0.9, 13.15), "harbor_glass", "xXzZ")
    mb.box((0.0, D + 19.4, zs + 31.0), (33.0, 2.8, 7.0), "harbor_white", "xXzZYy")
    mb.box((0.0, D + 19.6, zs + 31.0), (33.1, 1.1, 7.1), "harbor_glass", "xXzZ")
    mb.box((0.0, D + 18.5, zs + 17.5), (6.4, 13.0, 6.0), "harbor_navy", "xXzZY")
    mb.box((0.0, D + 23.0, zs + 17.5), (6.5, 1.6, 6.1), "harbor_coral", "xXzZ")
    if detail < 2:
        mb.box((0.0, D + 24.5, zs + 29.0), (0.6, 7.0, 0.6), "harbor_white", "xXzZY")
        mb.box((0.0, D + 26.5, zs + 29.0), (5.0, 0.35, 0.6), "harbor_white", "xXzZY")
        for sx in (-1, 1):
            mb.box((sx * 12.9, D + 6.0, zs + 24.0), (1.8, 1.8, 5.0), "harbor_coral", "xXzZY")
        mb.box((0.0, D + 3.4 + 3.0, L / 2 - 14.0), (0.5, 9.0, 0.5), "harbor_white", "xXzZY")
    # deck load
    BAY = C40 + 1.1
    z_first, z_last = zs + 44.0, L / 2 - 26.0
    nb = int((z_last - z_first) // BAY) + 1
    for b in range(nb):
        z = z_first + b * BAY
        s = z / L
        dk = [lerp(SHIP_TABLE[i][2], SHIP_TABLE[i + 1][2],
                   (s - SHIP_TABLE[i][0]) / (SHIP_TABLE[i + 1][0] - SHIP_TABLE[i][0]))
              for i in range(len(SHIP_TABLE) - 1) if SHIP_TABLE[i][0] <= s <= SHIP_TABLE[i + 1][0]]
        halfw = (dk[0] if dk else 1.0) * B - 1.0
        rows = max(2, int(2 * halfw // (CW + 0.12)))
        edge = min(1.0, max(0.0, (z - z_first) / 30.0)) * min(1.0, max(0.0, (z_last - z) / 30.0))
        base = int(round(lerp(3, 6, edge))) + rng.choice((0, 0, 1, -1))
        x0 = -(rows - 1) * (CW + 0.12) * 0.5
        mb.box((0.0, D + 0.45, z), (rows * (CW + 0.12) + 0.6, 0.9, C40 + 0.4), "harbor_deck", "xXzZY")
        col_h = []
        for r in range(rows):
            outer = min(r, rows - 1 - r)
            h = base - (1 if outer == 0 else 0) + rng.choice((0, 0, 0, 1, -1))
            h = max(1, min(8, h))
            col_h.append(h)
            x = x0 + r * (CW + 0.12)
            slot = pick_slot(rng)
            if detail == 0:
                for t in range(h):
                    sl = slot if rng.random() < 0.4 else pick_slot(rng)
                    add_container(mb, x, D + 0.9 + t * (CH + 0.02), z, C40, sl, top=(t == h - 1))
            elif detail == 1:
                mb.box((x, D + 0.9 + h * CH * 0.5, z), (CW, h * CH, C40), slot, "xXzZY",
                       uvfn=lambda f, w, hh: container_uv(C40)(f, w, hh))
        if detail == 2:
            hm = sorted(col_h)[len(col_h) // 2]
            mb.box((0.0, D + 0.9 + hm * CH * 0.5, z), (rows * (CW + 0.12), hm * CH, C40),
                   pick_slot(rng), "xXzZY", uv_per_m=0.2)
    return mb.build(f"Minato_Harbor_ContainerShip_LOD{detail}")


TUG_TABLE = [(-0.50, 0.86, 0.90, 0.45), (-0.40, 0.97, 0.99, 0.15), (-0.20, 1.0, 1.0, 0.0),
             (0.15, 1.0, 1.0, 0.0), (0.30, 0.88, 0.97, 0.05), (0.42, 0.55, 0.82, 0.2),
             (0.50, 0.08, 0.40, 0.5)]


def tug():
    """Harbour tug (~30 m): navy/red hull, coral sheer band, black fenders, white house."""
    L, B, T, D = 30.0, 5.0, 2.6, 1.9
    mb = MB()
    rings = hull_loft(mb, L, B, T, D, TUG_TABLE, rake=1.2, forecastle=1.1, band="harbor_coral",
                      stations=24)
    # superstructure
    mb.box((0.0, D + 1.3, 1.5), (6.4, 2.6, 9.0), "harbor_white", "xXzZY")
    mb.box((0.0, D + 3.7, 2.5), (5.6, 2.2, 5.2), "harbor_white", "xXzZ")
    mb.box((0.0, D + 3.9, 2.5), (5.7, 1.1, 5.3), "harbor_glass", "xXzZ")
    mb.box((0.0, D + 4.95, 2.5), (6.4, 0.3, 6.0), "harbor_white", "xXzZYy")
    for sx in (-1, 1):
        mb.box((sx * 1.7, D + 4.2, -1.6), (0.9, 3.4, 1.5), "harbor_coral", "xXzZY")
        mb.box((sx * 1.7, D + 6.0, -1.6), (0.95, 0.3, 1.55), "harbor_dark", "xXzZY")
    mb.box((0.0, D + 7.8, 3.0), (0.25, 5.2, 0.25), "harbor_white", "xXzZY")
    mb.box((0.0, D + 9.4, 3.0), (2.4, 0.18, 0.2), "harbor_white", "xXzZY")
    mb.box((0.0, D + 0.6, -9.5), (2.2, 1.2, 2.0), "harbor_dark", "xXzZY")      # tow winch
    mb.box((0.0, D + 1.2, -11.2), (0.6, 1.6, 0.6), "harbor_yellow", "xXzZY")   # tow hook post
    obj = mb.build("t_body")
    # fenders: tube along each sheer + bow pad
    fend = []
    for side in (0, -1):
        pts = [Vector(r[side]) + Vector((0.28 if side == -1 else -0.28, -0.35, 0.0)) for r in rings]
        for i in range(0, len(pts) - 2, 2):
            fend.append(M.tube(f"f{side}{i}", pts[i], pts[i + 2], 0.28, segments=6))
    bow = [Vector((x * 1.8, 1.0, L / 2 - 0.4 - abs(x) * 0.9)) for x in (-1.0, -0.5, 0.0, 0.5, 1.0)]
    for i in range(len(bow) - 1):
        fend.append(M.tube(f"bf{i}", bow[i], bow[i + 1], 0.55, segments=8))
    f = finish_slot(fend, "harbor_dark", "fen", bevel=0.0)
    return S.join([obj, f], "Minato_Harbor_Tug_LOD0")


def harbor_ferry():
    """Commuter ferry (~52 m): white two-deck superstructure, navy hull + stripe, coral funnel."""
    L, B, T, D = 52.0, 6.2, 2.4, 3.0
    mb = MB()
    hull_loft(mb, L, B, T, D, TUG_TABLE, rake=3.0, forecastle=0.8, above="harbor_white",
              below="harbor_navy", band="harbor_navy", stations=24)
    mb.box((0.0, D + 1.5, -2.0), (11.6, 3.0, 36.0), "harbor_white", "xXzZY")
    mb.box((0.0, D + 1.7, -2.0), (11.7, 1.3, 36.1), "harbor_glass", "xXzZ")
    mb.box((0.0, D + 4.3, -4.0), (10.2, 2.6, 26.0), "harbor_white", "xXzZY")
    mb.box((0.0, D + 4.5, -4.0), (10.3, 1.1, 26.1), "harbor_glass", "xXzZ")
    mb.box((0.0, D + 6.4, 5.0), (8.0, 1.8, 5.0), "harbor_white", "xXzZY")
    mb.box((0.0, D + 6.5, 5.0), (8.1, 0.9, 5.1), "harbor_glass", "xXzZ")
    mb.box((0.0, D + 6.8, -10.0), (2.6, 4.2, 3.4), "harbor_coral", "xXzZY")
    mb.box((0.0, D + 8.6, -10.0), (2.7, 0.5, 3.5), "harbor_navy", "xXzZY")
    mb.box((0.0, D + 9.5, 5.5), (0.2, 4.2, 0.2), "harbor_white", "xXzZY")
    return mb.build("Minato_Harbor_Ferry_LOD0")


# ============================================================================ small marine props

def buoy(kind):
    """Lateral / special-mark buoy: float, tripod tower, topmark, lantern. kind: can|cone|cross."""
    parts_b, parts_d, parts_l = [], [], []
    parts_b.append(lathe_at("float", [(0.0, -1.2), (0.9, -1.1), (1.3, -0.4), (1.3, 0.55),
                                      (0.8, 0.9), (0.0, 0.92)], 0, 0, 0, segments=14))
    parts_d.append(lathe_at("wl", [(1.33, -0.05), (1.33, 0.25)], 0, 0, 0, segments=14))
    for k in range(3):
        a = k / 3 * math.tau
        parts_b.append(M.beam(f"leg{k}", (math.cos(a) * 0.75, 0.85, math.sin(a) * 0.75),
                              (math.cos(a) * 0.22, 3.6, math.sin(a) * 0.22), 0.12, 0.12))
    parts_b.append(lathe_at("ring", [(0.62, 2.1), (0.62, 2.22)], 0, 0, 0, segments=10))
    parts_b.append(lathe_at("plat", [(0.45, 3.55), (0.45, 3.7)], 0, 0, 0, segments=10))
    if kind == "can":
        parts_b.append(lathe_at("top", [(0.32, 3.95), (0.32, 4.6)], 0, 0, 0, segments=10))
    elif kind == "cone":
        parts_b.append(lathe_at("top", [(0.40, 3.95), (0.0, 4.75)], 0, 0, 0, segments=10))
    else:
        parts_b.append(M.beam("x1", (-0.35, 3.95, 0), (0.35, 4.65, 0), 0.12, 0.12, up_hint=(0, 0, 1)))
        parts_b.append(M.beam("x2", (0.35, 3.95, 0), (-0.35, 4.65, 0), 0.12, 0.12, up_hint=(0, 0, 1)))
    parts_l.append(lathe_at("lamp", [(0.14, 3.7), (0.14, 3.95)], 0, 0, 0, segments=8))
    objs = [finish_slot(parts_b, "harbor_buoy", "b", 0.0), finish_slot(parts_d, "harbor_dark", "d", 0.0),
            finish_slot(parts_l, "harbor_lamp", "l", 0.0)]
    return S.join([o for o in objs if o], f"Minato_Harbor_Buoy{kind.capitalize()}_LOD0")


def lighthouse():
    """Small red harbour-entrance light on a concrete plinth (target: breakwater head, left)."""
    red, white, conc, glass, dark = [], [], [], [], []
    conc.append(lathe_at("plinth", [(3.4, 0.0), (3.4, 1.6), (3.0, 2.2), (0.0, 2.2)], 0, 0, 0, 8))
    red.append(lathe_at("t1", [(2.0, 2.2), (1.78, 8.0)], 0, 0, 0, 16))
    white.append(lathe_at("t2", [(1.78, 8.0), (1.72, 9.6)], 0, 0, 0, 16))
    red.append(lathe_at("t3", [(1.72, 9.6), (1.5, 14.6)], 0, 0, 0, 16))
    red.append(lathe_at("gal", [(2.35, 14.6), (2.35, 15.1)], 0, 0, 0, 16))
    white.append(lathe_at("rail", [(2.3, 15.95), (2.3, 16.1)], 0, 0, 0, 16))
    for k in range(10):
        a = k / 10 * math.tau
        white.append(M.beam(f"rp{k}", (math.cos(a) * 2.28, 15.1, math.sin(a) * 2.28),
                            (math.cos(a) * 2.28, 16.0, math.sin(a) * 2.28), 0.06, 0.06))
    glass.append(lathe_at("lantern", [(1.05, 15.1), (1.05, 17.1)], 0, 0, 0, 12))
    red.append(lathe_at("cap", [(1.3, 17.1), (0.9, 17.8), (0.2, 18.4), (0.0, 18.5)], 0, 0, 0, 12))
    dark.append(M.beam("finial", (0, 18.4, 0), (0, 19.4, 0), 0.08, 0.08, up_hint=(0, 0, 1)))
    dark.append(M.box("door", (0.0, 3.4, -1.95), (1.0, 2.1, 0.2)))
    for y in (6.0, 11.5):
        dark.append(M.box(f"win{y}", (0.0, y, -1.72), (0.45, 0.8, 0.2)))
    objs = [finish_slot(red, "harbor_buoy", "r", 0.02), finish_slot(white, "harbor_white", "w", 0.0),
            finish_slot(conc, "harbor_concrete", "c", 0.03), finish_slot(glass, "harbor_lamp", "g", 0.0),
            finish_slot(dark, "harbor_dark", "d", 0.0)]
    return S.join([o for o in objs if o], "Minato_Harbor_Lighthouse_LOD0")


def _rock(name, c, r, seed):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0)
    rng = random.Random(seed)
    sx, sy, sz = r * rng.uniform(0.8, 1.3), r * rng.uniform(0.55, 0.85), r * rng.uniform(0.8, 1.3)
    for v in bm.verts:
        j = 1.0 + rng.uniform(-0.18, 0.18)
        x, y, z = v.co.x * sx * j, v.co.z * sy * j, v.co.y * sz * j     # unity-ish local
        v.co = Vector(u2b(c[0] + x, c[1] + y, c[2] + z))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    uv = me.uv_layers.new(name="UVMap")
    for loop in me.loops:
        co = me.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (co.x * 0.3, co.z * 0.3)
    obj = bpy.data.objects.new(name, me)
    S.link(obj)
    return obj


def _tetrapod(name, c, scale, rng):
    """Four truncated-cone legs on tetrahedral axes, randomly tumbled."""
    dirs = [Vector((0, 1, 0)), Vector((0.943, -0.333, 0)), Vector((-0.471, -0.333, 0.816)),
            Vector((-0.471, -0.333, -0.816))]
    ax = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized()
    from mathutils import Quaternion
    q = Quaternion(ax, rng.uniform(0, math.tau))
    parts = []
    cv = Vector(c)
    for k, d in enumerate(dirs):
        d2 = q @ d
        parts.append(M.tube(f"{name}{k}", cv, cv + d2 * 1.55 * scale, 0.58 * scale, segments=6,
                            taper=0.55))
    return parts


def breakwater_segment():
    """40 m rubble-mound breakwater: rock core, tetrapod armour on the seaward (+X) flank,
    rock armour landward, concrete crest slab and seaward crown wall."""
    rng = random.Random(41)
    conc, rock, pods = [], [], []
    sec = [(-12.5, -4.0), (12.5, -4.0), (3.4, 2.3), (-3.4, 2.3)]
    rock.append(M.prism("core", sec, (0, 0, -20.0), (0, 0, 20.0), uv_per_m=0.3))
    conc.append(M.box("slab", (0.0, 2.55, 0.0), (6.6, 0.5, 40.0), uv_per_m=0.3))
    conc.append(M.box("wall", (2.7, 3.7, 0.0), (1.2, 2.0, 40.0), uv_per_m=0.3))
    for k in range(9):
        conc.append(M.box(f"joint{k}", (2.7, 3.7, -20 + k * 5.0), (1.26, 2.04, 0.12), uv_per_m=0.3))
    # tetrapods: 3 rows down the seaward slope
    for row, (xo, yo) in enumerate(((4.9, 1.6), (7.4, 0.0), (9.9, -1.6))):
        z = -19.0 + rng.uniform(0, 1.2)
        while z < 19.5:
            c = (xo + rng.uniform(-0.6, 0.6), yo + rng.uniform(-0.3, 0.4), z)
            pods += _tetrapod(f"tp{row}_{z:.0f}", c, rng.uniform(0.95, 1.15), rng)
            z += rng.uniform(2.3, 2.9)
    # rock armour landward
    z = -19.5
    k = 0
    while z < 20:
        for (xo, yo) in ((-5.5, 1.0), (-9.0, -1.2)):
            rock.append(_rock(f"rk{k}", (xo + rng.uniform(-0.8, 0.8), yo, z + rng.uniform(-0.6, 0.6)),
                              rng.uniform(1.1, 1.7), k))
            k += 1
        z += rng.uniform(2.0, 2.8)
    objs = [finish_slot(conc, "harbor_concrete", "c", 0.04),
            finish_slot(rock, "harbor_rock", "r", 0.0, smooth=None),
            finish_slot(pods, "harbor_concrete", "p", 0.0, smooth=40.0)]
    return S.join([o for o in objs if o], "Minato_Harbor_Breakwater_LOD0")


def breakwater_head():
    """Round breakwater head (lighthouse platform) with a tetrapod skirt all round."""
    rng = random.Random(43)
    conc, rock, pods = [], [], []
    rock.append(lathe_at("mound", [(15.0, -4.0), (7.2, 2.3), (0.0, 2.3)], 0, 0, 0, 20))
    conc.append(lathe_at("pad", [(7.4, 2.3), (7.4, 2.8), (0.0, 2.8)], 0, 0, 0, 20))
    for ring, (r, y, n) in enumerate(((8.8, 1.7, 22), (11.4, 0.1, 28), (13.8, -1.5, 34))):
        for k in range(n):
            a = (k + rng.uniform(-0.3, 0.3)) / n * math.tau
            c = (math.cos(a) * r, y + rng.uniform(-0.3, 0.3), math.sin(a) * r)
            pods += _tetrapod(f"h{ring}_{k}", c, rng.uniform(0.95, 1.15), rng)
    objs = [finish_slot(conc, "harbor_concrete", "c", 0.03),
            finish_slot(rock, "harbor_rock", "r", 0.0, smooth=None),
            finish_slot(pods, "harbor_concrete", "p", 0.0, smooth=40.0)]
    return S.join([o for o in objs if o], "Minato_Harbor_BreakwaterHead_LOD0")


def bollard():
    """Mooring bitt."""
    b = lathe_at("b", [(0.46, 0.0), (0.46, 0.08), (0.27, 0.13), (0.24, 0.55), (0.37, 0.62),
                       (0.37, 0.78), (0.0, 0.81)], 0, 0, 0, 12)
    return finish_slot([b], "harbor_dark", "Minato_Harbor_Bollard_LOD0", 0.0)


def hazard_kerb():
    """3 m seawall kerb painted in diagonal hazard stripes (the targets' yellow/black edge)."""
    mb = MB()
    L = 3.0

    def fn(face, w, h):
        if face in "xX":
            return [(0, 0), (L, 0), (L, 0.45), (0, 0.45)]
        if face == "Y":
            return [(0, 0), (0.6, 0), (0.6, L), (0, L)]
        return [(0, 0), (0.6, 0), (0.6, 0.45), (0, 0.45)]
    mb.box((0.0, 0.225, 0.0), (0.6, 0.45, L - 0.06), "harbor_hazard", "xXzZY", uvfn=fn)
    return mb.build("Minato_Harbor_HazardKerb_LOD0")


def chevron_barrier():
    """Port Gate concrete barrier wall (3 m) with a chevron board facing the road (-X)."""
    mb = MB()
    L = 3.0
    sec = [(0.0, 0.0), (0.0, 1.05)]
    mb.box((0.0, 0.55, 0.0), (0.7, 1.1, L - 0.08), "harbor_concrete", "xXzZY", uv_per_m=0.5)
    mb.box((0.0, 1.13, 0.0), (0.5, 0.06, L - 0.1), "harbor_concrete", "Y", uv_per_m=0.5)
    mb.quad((-0.37, 0.25, L * 0.5 - 0.2), (-0.37, 0.25, -L * 0.5 + 0.2),
            (-0.37, 0.95, -L * 0.5 + 0.2), (-0.37, 0.95, L * 0.5 - 0.2), "harbor_chevron",
            normal=(-1, 0, 0), uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
    return mb.build("Minato_Harbor_ChevronBarrier_LOD0")


def chevron_post():
    """Free-standing hazard post (target: yellow/black post at the kerb)."""
    mb = MB()

    def fn(face, w, h):
        return [(0, 0), (0.5, 0), (0.5, 2.2), (0, 2.2)]
    mb.box((0.0, 0.6, 0.0), (0.45, 1.2, 0.45), "harbor_hazard", "xXzZY", uvfn=fn)
    mb.box((0.0, 1.23, 0.0), (0.5, 0.06, 0.5), "harbor_concrete", "xXzZY")
    return mb.build("Minato_Harbor_HazardPost_LOD0")


def marine_sign():
    """Harbour sign on two posts; the face carries the navy wave-emblem board texture."""
    mb = MB()
    for sx in (-1, 1):
        mb.box((sx * 1.7, 1.6, 0.0), (0.14, 3.2, 0.14), "harbor_white", "xXzZY")
    mb.box((0.0, 2.6, 0.06), (4.0, 1.9, 0.1), "harbor_white", "xXzZY")
    mb.quad((2.0, 1.65, -0.0), (-2.0, 1.65, -0.0), (-2.0, 3.55, -0.0), (2.0, 3.55, -0.0), "harbor_sign",
            normal=(0, 0, -1), uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
    return mb.build("Minato_Harbor_Sign_LOD0")


def light_mast():
    """35 m yard floodlight mast."""
    parts_w, parts_l = [], []
    parts_w.append(M.tube("pole", (0, 0, 0), (0, 34.0, 0), 0.55, segments=8, taper=0.45))
    parts_w.append(M.box("head", (0.0, 34.2, 0.0), (4.2, 0.5, 1.8)))
    for k in range(4):
        parts_l.append(M.box(f"l{k}", (-1.5 + k * 1.0, 33.7, -0.4), (0.8, 0.5, 0.6)))
    objs = [finish_slot(parts_w, "harbor_white", "w", 0.0), finish_slot(parts_l, "harbor_lamp", "l", 0.0)]
    return S.join(objs, "Minato_Harbor_LightMast_LOD0")


# ============================================================================ Port Gate rail

def rail_viaduct_span():
    """
    30 m freight-rail viaduct DECK span (target: Port Gate): concrete box deck with parapets,
    ballast and rails, plus the steel crosshead of the bent at its -Z end. Origin = deck TOP at
    the span centre (y = 0), +Z along the line. The columns are a separate unit-height piece
    (RailBent) scaled per bent in Unity, so the deck stays level over uneven ground without
    squashing the deck section.
    """
    SPAN = 30.0
    conc, steel, rail = [], [], []
    conc.append(M.prism("deck", [(-3.6, -1.6), (3.6, -1.6), (4.4, 0.0), (-4.4, 0.0)],
                        (0, 0.0, -SPAN * 0.5), (0, 0.0, SPAN * 0.5), uv_per_m=0.3))
    for sx in (-1, 1):
        conc.append(M.box(f"par{sx}", (sx * 4.1, 0.55, 0.0), (0.4, 1.1, SPAN), uv_per_m=0.3))
        steel.append(M.box(f"cat{sx}", (sx * 3.0, 3.0, -SPAN * 0.5 + 0.5), (0.25, 6.0, 0.25)))
    steel.append(M.box("catx", (0.0, 5.8, -SPAN * 0.5 + 0.5), (6.3, 0.25, 0.25)))
    rail.append(M.box("ballast", (0.0, 0.18, 0.0), (3.2, 0.36, SPAN), uv_per_m=0.5))
    for sx in (-1, 1):
        rail.append(M.box(f"r{sx}", (sx * 0.72, 0.46, 0.0), (0.12, 0.18, SPAN), uv_per_m=1.0))
    steel.append(M.box("xhead", (0.0, -2.3, -SPAN * 0.5), (9.0, 1.4, 1.8), uv_per_m=0.4))
    objs = [finish_slot(conc, "harbor_concrete", "c", 0.04), finish_slot(steel, "harbor_steelblue", "s", 0.03),
            finish_slot(rail, "harbor_rail", "r", 0.0)]
    return S.join([o for o in objs if o], "Minato_Harbor_RailSpan_LOD0")


def rail_bent():
    """Two steel columns of UNIT height (y 0..1, scaled per bent in Unity) with a light X brace."""
    steel = []
    for sx in (-1, 1):
        steel.append(M.beam(f"col{sx}", (sx * 3.2, 0.0, 0.0), (sx * 3.2, 1.0, 0.0), 1.3, 1.3,
                            up_hint=(0, 0, 1), uv_per_m=0.5))
    steel.append(M.beam("xb1", (-3.0, 0.15, 0), (3.0, 0.85, 0), 0.35, 0.35, up_hint=(0, 0, 1)))
    steel.append(M.beam("xb2", (3.0, 0.15, 0), (-3.0, 0.85, 0), 0.35, 0.35, up_hint=(0, 0, 1)))
    return finish_slot(steel, "harbor_steelblue", "Minato_Harbor_RailBent_LOD0", 0.0, smooth=None)


def freight_train():
    """Blue electric locomotive + 5 container flat wagons (~110 m). Origin at rail top, +Z = front."""
    mb = MB()
    rng = random.Random(77)
    # locomotive (front, z 0 .. -18)
    mb.box((0.0, 2.3, -9.0), (2.9, 2.8, 17.0), "harbor_loco", "xXzZY")
    mb.box((0.0, 1.0, -9.0), (2.7, 0.8, 16.0), "harbor_dark", "xXzZ")
    mb.box((0.0, 3.8, -9.0), (2.4, 0.3, 14.0), "harbor_c_grey", "xXzZY")
    for zc in (-0.3, -17.7):
        mb.box((0.0, 2.9, zc), (2.6, 1.0, 0.1), "harbor_glass", "zZ")
    mb.box((0.0, 2.0, -9.0), (2.95, 0.35, 17.05), "harbor_white", "xX")
    z = -18.8
    for w in range(5):
        L = 20.0
        mb.box((0.0, 1.1, z - L * 0.5), (2.6, 0.5, L), "harbor_dark", "xXzZY")
        n40 = 1 if rng.random() < 0.6 else 0
        if n40:
            add_container(mb, 0.0, 1.35, z - L * 0.5, C40, pick_slot(rng))
        else:
            add_container(mb, 0.0, 1.35, z - 5.2, C20, pick_slot(rng))
            add_container(mb, 0.0, 1.35, z - 14.8, C20, pick_slot(rng))
        z -= L + 1.0
    body = mb.build("tr_body")
    wheels = []
    for zc in [-3.0, -15.0] + [-18.8 - 21.0 * w - k for w in range(5) for k in (3.0, 17.0)]:
        for sx in (-1, 1):
            wheels.append(M.tube(f"w{zc}{sx}", (sx * 0.85, 0.45, zc - 0.9), (sx * 0.85, 0.45, zc + 0.9),
                                 0.42, segments=6))
    w = finish_slot(wheels, "harbor_dark", "wh", 0.0)
    return S.join([body, w], "Minato_Harbor_FreightTrain_LOD0")


# ============================================================================ export

def export_family(objs, filename):
    tris = tri_count(objs[0])
    M.export_fbx(objs, filename)
    print(f"[harbor] {filename:<40s} LOD0 {tris:>7,} tris  (+{len(objs) - 1} LODs)")
    return tris


def with_lods(obj, ratios):
    base = obj.name.replace("_LOD0", "")
    return [obj] + [M.decimated_copy(obj, f"{base}_LOD{i + 1}", r) for i, r in enumerate(ratios)]


CATALOG = {
    "crane":       lambda: (with_lods(sts_crane(False), (0.45, 0.18)), "Minato_Harbor_STSCrane.fbx"),
    "craneraised": lambda: (with_lods(sts_crane(True), (0.45, 0.18)), "Minato_Harbor_STSCraneRaised.fbx"),
    "rtg":         lambda: (with_lods(rtg_crane(), (0.4,)), "Minato_Harbor_RTG.fbx"),
    "ship":        lambda: ([container_ship(0), container_ship(1), container_ship(2)],
                            "Minato_Harbor_ContainerShip.fbx"),
    "tug":         lambda: (with_lods(tug(), (0.4,)), "Minato_Harbor_Tug.fbx"),
    "ferry":       lambda: (with_lods(harbor_ferry(), (0.4,)), "Minato_Harbor_Ferry.fbx"),
    "buoycan":     lambda: (with_lods(buoy("can"), (0.35,)), "Minato_Harbor_BuoyCan.fbx"),
    "buoycone":    lambda: (with_lods(buoy("cone"), (0.35,)), "Minato_Harbor_BuoyCone.fbx"),
    "buoycross":   lambda: (with_lods(buoy("cross"), (0.35,)), "Minato_Harbor_BuoyCross.fbx"),
    "lighthouse":  lambda: (with_lods(lighthouse(), (0.4,)), "Minato_Harbor_Lighthouse.fbx"),
    "breakwater":  lambda: (with_lods(breakwater_segment(), (0.35, 0.12)), "Minato_Harbor_Breakwater.fbx"),
    "bwhead":      lambda: (with_lods(breakwater_head(), (0.35, 0.12)), "Minato_Harbor_BreakwaterHead.fbx"),
    "bollard":     lambda: ([bollard()], "Minato_Harbor_Bollard.fbx"),
    "kerb":        lambda: ([hazard_kerb()], "Minato_Harbor_HazardKerb.fbx"),
    "barrier":     lambda: ([chevron_barrier()], "Minato_Harbor_ChevronBarrier.fbx"),
    "hazardpost":  lambda: ([chevron_post()], "Minato_Harbor_HazardPost.fbx"),
    "sign":        lambda: ([marine_sign()], "Minato_Harbor_Sign.fbx"),
    "lightmast":   lambda: ([light_mast()], "Minato_Harbor_LightMast.fbx"),
    "blockA":      lambda: (container_block("A", 6, 4, 2, 5, 101), "Minato_Harbor_ContainerBlockA.fbx"),
    "blockB":      lambda: (container_block("B", 8, 3, 3, 5, 202), "Minato_Harbor_ContainerBlockB.fbx"),
    "blockC":      lambda: (container_block("C", 5, 6, 1, 4, 303, length=C20), "Minato_Harbor_ContainerBlockC.fbx"),
    "wallA":       lambda: (container_wall("A", 60.0, 3, 501), "Minato_Harbor_ContainerWallA.fbx"),
    "wallB":       lambda: (container_wall("B", 60.0, 4, 502), "Minato_Harbor_ContainerWallB.fbx"),
    "railspan":    lambda: (with_lods(rail_viaduct_span(), (0.4,)), "Minato_Harbor_RailSpan.fbx"),
    "railbent":    lambda: ([rail_bent()], "Minato_Harbor_RailBent.fbx"),
    "train":       lambda: (with_lods(freight_train(), (0.45,)), "Minato_Harbor_FreightTrain.fbx"),
}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(CATALOG.keys())
    if not argv or "textures" in argv:
        S.reset_scene()
        container_texture()
        hazard_texture()
        chevron_texture()
        sign_texture()
    total = 0
    for n in names:
        if n == "textures":
            continue
        S.reset_scene()
        objs, fn = CATALOG[n]()
        total += export_family(objs, fn)
    print(f"[harbor] complete: {len(names)} families, {total:,} LOD0 tris total")


if __name__ == "__main__":
    main()

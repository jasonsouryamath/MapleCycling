"""
MINATO COAST - BOULEVARD DRESSING (milestone 1 of the "After" concept recreation).

Authors the two families that the Minamo_01 / Minamo_02 "After" concept boards carry and the
current staged scene does not:

  1. BANNER FLAGS - the tall blue vertical banner with a white ocean-wave motif that hangs from
     a bracket on EVERY lamp standard along the waterfront boulevard. Authored with its origin
     at the LAMP BASE (not at the banner) so the Unity pass can parent it to an existing
     ``Minato_Bridge_Lamp`` instance with an identity local transform and it lands on the pole
     with no per-instance offset table. Two variants (high / low mount) so a run of posts does
     not read as a photocopy.

  2. PLANTED MEDIAN - the palms, flowering beds, magenta shrubs and timber kerb planters that
     turn the bare grass median into the lush divided boulevard of the concept.

Everything is authored in UNITY coordinates through ``minato_lib`` primitives (which convert via
``S.u2b`` at mesh build time) with:

    +Z = forward along the route      +X = rider's right      +Y = up      origin at ground

AUTHORING NOTES THAT MATTER
---------------------------
* Every cloth panel and every palm frond is a CLOSED THIN SHELL (front sheet + back sheet +
  edge strips), never a single quad. A single quad is backface-culled by MapleRide/SakuraCel,
  so half the banners on the boulevard would have been invisible depending on which way the
  rider was travelling - the classic "it rendered fine in the one frame I checked" failure.
* The banner cloth is UV-mapped 0..1 so the authored ``Minato_Banner_Albedo.png`` lands on the
  panel exactly once, right way up. The bracket steel keeps its own slot so the Unity pass can
  retint cloth and steel separately (see MinatoCoastEnvironment.RetintBoulevard).
* ALL dimensions and colours here are PROVISIONAL illustrative tuning, read off the concept
  boards; nothing in the design handoff fixes them.
"""

import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402
from build_textures import write_png, to_u8  # noqa: E402


# ============================================================ provisional tuning

BANNER_W = 0.94          # cloth width, m
BANNER_H = 3.20          # cloth drop, m
BANNER_TOP_HIGH = 7.40   # top bracket height on the 9.4 m lamp standard
BANNER_TOP_LOW = 6.55    # the second variant, so a run of posts varies
BRACKET_OUT = 0.50       # bracket reach from the column face
CLOTH_T = 0.035          # cloth thickness (reads as fabric, kills the backface problem)
CLOTH_RIPPLE = 0.09      # gentle furl depth across the panel

TEX_W, TEX_H = 512, 1024


# ============================================================ banner texture

def _srgb(r, g, b):
    return np.array([r, g, b], dtype=np.float32) / 255.0


def banner_texture():
    """
    The banner artwork: a deep maritime blue field, a white wave crest motif and a pale
    top/bottom hem band - the motif read off Minamo_01/02.

    Written straight to Assets/Environment/MinatoCoast/Textures so Unity imports it as a normal
    sRGB albedo with no extra wiring.
    """
    h, w = TEX_H, TEX_W
    v, u = np.meshgrid(np.linspace(0.0, 1.0, h), np.linspace(0.0, 1.0, w), indexing="ij")

    deep = _srgb(18, 62, 140)
    mid = _srgb(32, 96, 186)
    pale = _srgb(120, 186, 236)
    white = _srgb(246, 250, 253)

    # Vertical gradient field, slightly lighter towards the bottom hem.
    t = np.clip(v * 1.15 - 0.05, 0.0, 1.0)[..., None]
    img = deep * (1.0 - t) + mid * t

    # Three stacked white wave crests in the lower two thirds - the ocean motif. The bands are
    # deliberately CRISP (near-binary coverage with ~1.5 px of anti-aliasing): a feathered edge
    # reads as a printing smudge at the 20-200 m distances the boulevard is actually seen from.
    px = 1.0 / TEX_H
    for k, (yc, amp, freq, thick, col) in enumerate([
        (0.46, 0.034, 2.0, 0.026, white),
        (0.575, 0.040, 2.0, 0.022, pale),
        (0.685, 0.030, 2.0, 0.019, white),
    ]):
        wave = yc + amp * np.sin((u * freq + k * 0.22) * math.tau)
        band = np.clip((thick - np.abs(v - wave)) / (1.6 * px), 0.0, 1.0)
        img = img * (1.0 - band[..., None]) + col * band[..., None]

    # A stylised white sun/crest disc in the upper third - the club emblem on the banner.
    cx, cy, rad = 0.5, 0.245, 0.115
    d = np.sqrt(((u - cx) * (TEX_W / TEX_H)) ** 2 + (v - cy) ** 2)
    disc = np.clip((rad - d) / (1.6 * px), 0.0, 1.0)
    img = img * (1.0 - disc[..., None]) + white * disc[..., None]
    # Two blue wave strokes cut across the disc so it reads as a crest, not a dot.
    for yc, amp in ((0.252, 0.018), (0.288, 0.016)):
        wave = yc + amp * np.sin((u - 0.5) * 6.0 * math.tau / 6.0 * 3.0)
        cut = np.clip((0.013 - np.abs(v - wave)) / (1.6 * px), 0.0, 1.0) * disc
        img = img * (1.0 - cut[..., None]) + mid * cut[..., None]

    # Hem bands top and bottom.
    hem = np.clip((0.030 - v) / (1.6 / TEX_H), 0.0, 1.0) + \
        np.clip((0.030 - (1.0 - v)) / (1.6 / TEX_H), 0.0, 1.0)
    hem = np.clip(hem, 0.0, 1.0)
    img = img * (1.0 - hem[..., None]) + white * hem[..., None]

    # Weave grain + a soft vertical shading so the cloth is never a flat colour field.
    grain = (S.fbm_2d(u * 26.0, v * 90.0, 1.0, octaves=3, seed=7) - 0.5) * 0.05
    img = np.clip(img + grain[..., None], 0.0, 1.0)
    shade = 1.0 - 0.10 * np.clip(np.cos((u - 0.5) * math.pi), 0.0, 1.0)
    img = np.clip(img * shade[..., None], 0.0, 1.0)

    alpha = np.ones((h, w), dtype=np.float32)
    rgba = np.dstack([img, alpha])
    path = os.path.join(M.textures_dir(), "Minato_Banner_Albedo.png")
    write_png(path, to_u8(rgba))
    return path


def banner_material():
    name = "Minato_banner_cloth"
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    path = os.path.join(M.textures_dir(), "Minato_Banner_Albedo.png")
    if not os.path.exists(path):
        banner_texture()
    m = S.pbr_material(name, base_color=(0.10, 0.30, 0.66, 1.0), roughness=0.82, metallic=0.0)
    try:
        S.set_texture(m, S.load_image(path), slot="Base Color")
    except Exception as exc:                                   # pragma: no cover
        print(f"[minato] WARNING: banner texture not bound in Blender preview: {exc}")
    return m


# ============================================================ thin-shell helper

def sheet(name, corner_fn, nu, nv, thickness, uv01=True, uv_per_m=None):
    """
    A CLOSED thin shell swept from a parametric surface ``corner_fn(u, v) -> (pos, normal)``.

    Front sheet, back sheet offset along -normal by ``thickness``, and four edge strips. This
    is the workhorse for banner cloth and palm fronds: both must be visible from BOTH sides,
    and a single quad under a back-face-culling cel shader is only visible from one.
    """
    front, back, nrm = [], [], []
    for iv in range(nv + 1):
        for iu in range(nu + 1):
            u = iu / nu
            v = iv / nv
            p, n = corner_fn(u, v)
            p = Vector(p)
            n = Vector(n).normalized()
            front.append(p + n * (thickness * 0.5))
            back.append(p - n * (thickness * 0.5))
            nrm.append(n)

    stride = nu + 1
    nf = len(front)
    verts = front + back
    faces, uvs = [], []

    def quad(a, b, c, d, uvq):
        faces.append((a, b, c, d))
        uvs.extend(uvq)

    for iv in range(nv):
        for iu in range(nu):
            a = iv * stride + iu
            b = a + 1
            c = a + stride + 1
            d = a + stride
            u0, u1 = iu / nu, (iu + 1) / nu
            v0, v1 = iv / nv, (iv + 1) / nv
            if not uv01:
                s = uv_per_m or 1.0
                u0, u1, v0, v1 = u0 * s, u1 * s, v0 * s, v1 * s
            # Front face: wound so its normal follows +n.
            quad(a, b, c, d, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
            # Back face: reversed winding, mirrored U so the artwork is not mirrored-readable.
            quad(nf + d, nf + c, nf + b, nf + a,
                 [(1.0 - u0, v1), (1.0 - u1, v1), (1.0 - u1, v0), (1.0 - u0, v0)])

    # Edge strips - left, right, bottom, top - so the shell is watertight.
    for iv in range(nv):
        a, b = iv * stride, (iv + 1) * stride
        quad(nf + a, nf + b, b, a, [(0, 0), (0, 1), (0.02, 1), (0.02, 0)])
        a2, b2 = iv * stride + nu, (iv + 1) * stride + nu
        quad(a2, b2, nf + b2, nf + a2, [(1, 0), (1, 1), (0.98, 1), (0.98, 0)])
    for iu in range(nu):
        a, b = iu, iu + 1
        quad(a, b, nf + b, nf + a, [(0, 0), (1, 0), (1, 0.02), (0, 0.02)])
        a2, b2 = nv * stride + iu, nv * stride + iu + 1
        quad(nf + a2, nf + b2, b2, a2, [(0, 1), (1, 1), (1, 0.98), (0, 0.98)])

    return S.mesh_from_arrays(name, [M.u2b(*v) for v in verts], faces, uvs=uvs, smooth=False)


def blob(name, centre, radius, squash=(1.0, 1.0, 1.0), subdiv=2):
    """A round foliage / blossom mass. Spheres are symmetric, so the coordinate swap is a
    no-op on the shape and only the centre needs converting.

    ANIME QUALITY PASS (2026-09-24): was subdiv=1 (80 faces) shaded at a 40-42 deg smooth
    angle, i.e. every clump rendered as a flat-faceted gem - the single most "low-poly" tell in
    the boulevard. Now subdiv 2 with a gentle per-clump noise swell (an irregular, leafy
    silhouette rather than a perfect ball) and FULLY smooth shading in the callers' finish()."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=radius,
                                          location=M.u2b(*centre))
    o = bpy.context.active_object
    o.name = name
    seed = Vector((centre[0] * 3.1, centre[1] * 5.7, centre[2] * 2.3))
    for v in o.data.vertices:
        n = v.co.normalized()   # ico verts are local to the sphere's own origin
        v.co += n * radius * 0.16 * noise.noise(n * 2.4 + seed)
    # Clump-height UVs: v runs underside (0.25) -> crown (1.0) so the foliage gradient texture
    # (Minato_Foliage_Gradient.png) shades every clump dark-below / sunlit-above, the painted
    # anime bush read. u jitters per clump for a little hue variety. Blossom materials ignore it.
    uv = o.data.uv_layers.active or o.data.uv_layers.new(name="UVMap")
    u0 = (abs(seed.x) * 7.13 + abs(seed.z) * 3.7) % 1.0
    for loop in o.data.loops:
        z = o.data.vertices[loop.vertex_index].co.z
        uv.data[loop.index].uv = (u0, 0.25 + 0.75 * max(0.0, min(1.0, (z / radius + 1.0) * 0.5)))
    # squash is authored in UNITY axes (x, y=up, z) -> blender (x, z, y) magnitudes.
    o.scale = (squash[0], squash[2], squash[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o


# ============================================================ 1. banner flag

def _banner(top_y, tag):
    """
    Banner + bracket, authored about a lamp column standing at the local origin.

    The bracket reaches out along +X and the cloth hangs in the X/Y plane, so its FACE normal
    points along the route (+/-Z) - which is how a rider riding down the boulevard sees the
    artwork full-on, exactly as the concept boards show it.
    """
    steel = []
    x_in, x_out = 0.10, BRACKET_OUT
    bot_y = top_y - BANNER_H
    # Two horizontal bracket arms plus a diagonal stay on the upper one.
    steel.append(M.tube(f"{tag}_armT", (x_in, top_y, 0.0), (x_out + BANNER_W, top_y, 0.0),
                        0.032, segments=6, uv_per_m=1.2))
    steel.append(M.tube(f"{tag}_armB", (x_in, bot_y, 0.0), (x_out + BANNER_W, bot_y, 0.0),
                        0.026, segments=6, uv_per_m=1.2))
    steel.append(M.tube(f"{tag}_stay", (x_in, top_y + 0.46, 0.0),
                        (x_out + BANNER_W * 0.55, top_y, 0.0), 0.020, segments=5, uv_per_m=1.2))
    # Collars where the arms meet the column.
    for y in (top_y, bot_y):
        steel.append(M.tube(f"{tag}_collar{y:.0f}", (0.0, y - 0.09, 0.0), (0.0, y + 0.09, 0.0),
                            0.135, segments=10, uv_per_m=1.2))

    def surface(u, v):
        # u across the width (outboard), v down from the top bracket.
        x = x_out + u * BANNER_W
        y = top_y - v * BANNER_H
        # Gentle furl: the cloth bows away from the pole and picks up more curl low down.
        z = CLOTH_RIPPLE * math.sin(u * math.pi) * (0.35 + 0.65 * v)
        # Surface normal of that bow, in the X/Z plane.
        dz = CLOTH_RIPPLE * math.pi * math.cos(u * math.pi) * (0.35 + 0.65 * v) / BANNER_W
        n = Vector((-dz, 0.0, 1.0))
        return (x, y, z), n

    cloth = sheet(f"{tag}_cloth", surface, 5, 10, CLOTH_T)

    st = M.finish(steel, f"{tag}_steel", bevel=0.006, smooth_angle=34.0)
    M.paint(st, "steel_dark")
    S.assign_material(cloth, banner_material())
    obj = S.join([st, cloth], f"Minato_City_BannerFlag{tag}_LOD0")
    return obj


def banner_high():
    return _banner(BANNER_TOP_HIGH, "A")


def banner_low():
    o = _banner(BANNER_TOP_LOW, "B")
    o.name = "Minato_City_BannerFlagB_LOD0"
    return o


# ============================================================ 2. median planting

def _frond(tag, base, direction, length, width, droop, seed_phase):
    """One palm frond as a drooping, tapering, V-folded CLOSED blade."""
    d = Vector(direction).normalized()
    side = Vector((-d.z, 0.0, d.x)).normalized()

    def surface(u, v):
        # v runs from the crown outwards; u across the blade.
        s = v
        along = d * (length * s)
        fall = -droop * (s ** 2.2) + 0.55 * length * 0.24 * math.sin(s * 1.5 + seed_phase) * 0.12
        # Taper: broad at a third of the length, pointed at the tip.
        w = width * math.sin(min(1.0, 0.18 + s * 0.95) * math.pi) ** 0.7
        lat = (u - 0.5) * w
        # V-fold: the blade's centre rib sits above its edges.
        fold = (0.5 - abs(u - 0.5)) * w * 0.55
        p = Vector(base) + along + side * lat + Vector((0.0, fall + fold, 0.0))
        n = Vector((0.0, 1.0, 0.0)) - side * ((u - 0.5) * 1.4)
        return (p.x, p.y, p.z), n

    return sheet(tag, surface, 4, 6, 0.035)


def _palm(tag, height, fronds, lean, seed, trunk_r=0.28):
    r = M.rng(seed)
    trunk, foli = [], []
    prev = Vector((0.0, 0.0, 0.0))
    segs = 9
    for i in range(1, segs + 1):
        f = i / segs
        nxt = Vector((math.sin(f * 1.35) * lean, height * f,
                      math.cos(f * 1.9 + seed) * lean * 0.38))
        trunk.append(M.tube(f"{tag}_t{i}", prev, nxt, trunk_r * (1.0 - 0.45 * f),
                            segments=8, uv_per_m=0.9))
        prev = nxt
    # Crown collar - the ragged boot of old frond bases, which is what makes a palm read.
    trunk.append(M.tube(f"{tag}_crown", prev - Vector((0, 0.45, 0)), prev + Vector((0, 0.22, 0)),
                        trunk_r * 0.78, segments=9, uv_per_m=1.0, taper=0.74))

    for i in range(fronds):
        a = (i / fronds) * math.tau + float(r.uniform(-0.12, 0.12))
        rise = 0.55 if i % 3 == 0 else (0.22 if i % 3 == 1 else -0.05)
        direction = Vector((math.cos(a), rise, math.sin(a)))
        L = float(r.uniform(2.9, 4.1)) * (height / 9.0)
        foli.append(_frond(f"{tag}_f{i}", prev + Vector((0, 0.12, 0)), direction,
                           L, float(r.uniform(0.62, 0.86)), float(r.uniform(1.1, 2.0)),
                           float(r.uniform(0, 6.0))))
    # A few coconut/date clusters under the crown.
    for i in range(3):
        a = i / 3.0 * math.tau
        foli.append(blob(f"{tag}_cl{i}",
                         (prev.x + math.cos(a) * 0.32, prev.y - 0.25, prev.z + math.sin(a) * 0.32),
                         0.20, squash=(1.0, 0.8, 1.0)))

    t = M.finish(trunk, f"{tag}_tr", bevel=0.012, smooth_angle=44.0)
    fr = M.finish(foli, f"{tag}_fo", bevel=0.0, weld=0.0, smooth_angle=42.0)
    M.paint(t, "timber")
    M.paint(fr, "foliage")
    return S.join([t, fr], f"Minato_Flora_{tag}_LOD0")


def palm_tall():
    return _palm("PalmTall", 9.4, 17, 0.85, 11)


def palm_short():
    return _palm("PalmShort", 6.3, 15, 1.25, 23, trunk_r=0.24)


def flower_bed():
    """
    A planted bed for the central median: a dark soil rim, a green under-mass and a DENSE
    pink/red blossom mound - the flowering beds that run down the concept's median.

    Iteration 2: the first version carried a pale concrete kerb and a low, sparse blossom
    scatter, and it rendered as a white paving slab with a few dots on it (seen on
    diag_minato_target_crowd.png). The kerb is now dark soil, the footprint is smaller and the
    flower mass is both denser and genuinely mounded, so the bed reads as flowers at 20-150 m.
    """
    r = M.rng(41)
    L, W = 3.0, 1.55
    rim = [
        M.prism("rim", [(-W * 0.5, 0.0), (W * 0.5, 0.0), (W * 0.5, 0.20), (-W * 0.5, 0.20)],
                (0.0, 0.0, -L * 0.5), (0.0, 0.0, L * 0.5), up_hint=(0, 1, 0), uv_per_m=0.9),
    ]
    soil = [M.box("soil", (0.0, 0.20, 0.0), (W - 0.12, 0.12, L - 0.12), uv_per_m=1.0)]
    green, pink, red = [], [], []
    # Green under-mass: a proper crowned mound, highest along the bed's spine.
    for i in range(34):
        x = float(r.uniform(-W * 0.46, W * 0.46))
        z = float(r.uniform(-L * 0.47, L * 0.47))
        crown = 1.0 - (abs(x) / (W * 0.5)) ** 2 * 0.55
        g = float(r.uniform(0.24, 0.40))
        green.append(blob(f"g{i}", (x, 0.26 + g * 0.55 + crown * 0.16, z), g,
                          squash=(1.1, 0.9, 1.1)))
    # Blossom heads sit ON the mound, not beside it.
    for i in range(56):
        x = float(r.uniform(-W * 0.48, W * 0.48))
        z = float(r.uniform(-L * 0.48, L * 0.48))
        crown = 1.0 - (abs(x) / (W * 0.5)) ** 2 * 0.55
        y = 0.52 + crown * 0.30 + float(r.uniform(-0.10, 0.26))
        b = float(r.uniform(0.13, 0.22))
        (pink if r.random() < 0.58 else red).append(
            blob(f"b{i}", (x, y, z), b, squash=(1.25, 0.78, 1.25)))

    k = M.finish(rim, "k", bevel=0.02, smooth_angle=34.0)
    s = M.finish(soil, "s", bevel=0.01, smooth_angle=34.0)
    g = M.finish(green, "g", bevel=0.0, weld=0.0, smooth_angle=180.0)
    p = M.finish(pink, "p", bevel=0.0, weld=0.0, smooth_angle=180.0)
    d = M.finish(red, "d", bevel=0.0, weld=0.0, smooth_angle=180.0)
    M.paint(k, "soil_bed")
    M.paint(s, "soil_bed")
    M.paint(g, "foliage")
    M.paint(p, "blossom_pink")
    M.paint(d, "blossom_red")
    return S.join([k, s, g, p, d], "Minato_Flora_FlowerBed_LOD0")


def shrub_magenta():
    """The magenta flowering shrub that punctuates the median and the far-shore walls."""
    r = M.rng(53)
    green, mag = [], []
    for i in range(7):
        a = float(r.uniform(0, math.tau))
        rad = float(r.uniform(0.0, 0.55))
        s = float(r.uniform(0.42, 0.66))
        green.append(blob(f"g{i}", (math.cos(a) * rad, 0.34 + s * 0.35, math.sin(a) * rad),
                          s, squash=(1.15, 0.92, 1.15)))
    for i in range(22):
        a = float(r.uniform(0, math.tau))
        rad = float(r.uniform(0.15, 0.82))
        y = float(r.uniform(0.35, 1.05))
        mag.append(blob(f"m{i}", (math.cos(a) * rad, y, math.sin(a) * rad),
                        float(r.uniform(0.09, 0.16)), squash=(1.3, 0.7, 1.3)))
    g = M.finish(green, "g", bevel=0.0, weld=0.0, smooth_angle=180.0)
    m = M.finish(mag, "m", bevel=0.0, weld=0.0, smooth_angle=180.0)
    M.paint(g, "foliage")
    M.paint(m, "blossom_magenta")
    return S.join([g, m], "Minato_Flora_ShrubMagenta_LOD0")


def kerb_planter():
    """
    The timber planter box that lines the kerbs and the promenade edge in Minamo_02: a boarded
    box with corner posts, dark soil and a spilling pink/white flowering mass.
    """
    r = M.rng(67)
    L, W, H = 2.45, 0.86, 0.72
    timber, soil, green, pink, white = [], [], [], [], []
    # Boarded sides: three horizontal boards per long face, two per end.
    for k in range(3):
        y = 0.12 + k * 0.24
        for s in (-1, 1):
            timber.append(M.box(f"bd{k}{s}", (s * W * 0.5, y, 0.0), (0.07, 0.21, L), uv_per_m=1.2))
        for s in (-1, 1):
            timber.append(M.box(f"be{k}{s}", (0.0, y, s * L * 0.5), (W, 0.21, 0.07), uv_per_m=1.2))
    for sx in (-1, 1):
        for sz in (-1, 1):
            timber.append(M.box(f"post{sx}{sz}", (sx * W * 0.5, H * 0.5, sz * L * 0.5),
                                (0.13, H, 0.13), uv_per_m=1.4))
    timber.append(M.box("cap", (0.0, H + 0.03, 0.0), (W + 0.10, 0.06, L + 0.10), uv_per_m=1.2))
    soil.append(M.box("soil", (0.0, H - 0.10, 0.0), (W - 0.14, 0.14, L - 0.14), uv_per_m=1.2))

    for i in range(18):
        x = float(r.uniform(-W * 0.34, W * 0.34))
        z = float(r.uniform(-L * 0.44, L * 0.44))
        g = float(r.uniform(0.17, 0.27))
        green.append(blob(f"g{i}", (x, H - 0.02 + g * 0.5, z), g, squash=(1.2, 0.85, 1.2)))
    for i in range(28):
        x = float(r.uniform(-W * 0.40, W * 0.40))
        z = float(r.uniform(-L * 0.46, L * 0.46))
        y = H + 0.12 + float(r.uniform(0.0, 0.30))
        b = float(r.uniform(0.08, 0.15))
        (pink if r.random() < 0.72 else white).append(
            blob(f"b{i}", (x, y, z), b, squash=(1.3, 0.7, 1.3)))

    t = M.finish(timber, "t", bevel=0.012, smooth_angle=34.0)
    s = M.finish(soil, "s", bevel=0.01, smooth_angle=34.0)
    g = M.finish(green, "g", bevel=0.0, weld=0.0, smooth_angle=180.0)
    p = M.finish(pink, "p", bevel=0.0, weld=0.0, smooth_angle=180.0)
    w = M.finish(white, "w", bevel=0.0, weld=0.0, smooth_angle=180.0)
    M.paint(t, "timber")
    M.paint(s, "soil_bed")
    M.paint(g, "foliage")
    M.paint(p, "blossom_pink")
    M.paint(w, "blossom_white")
    return S.join([t, s, g, p, w], "Minato_City_KerbPlanter_LOD0")


# ============================================================ export table

CATALOG = [
    (banner_high,   "Minato_City_BannerFlag.fbx",     (0.45, 0.18)),
    (banner_low,    "Minato_City_BannerFlagB.fbx",    (0.45, 0.18)),
    (palm_tall,     "Minato_Flora_PalmTall.fbx",      (0.40, 0.15)),
    (palm_short,    "Minato_Flora_PalmShort.fbx",     (0.40, 0.15)),
    (flower_bed,    "Minato_Flora_FlowerBed.fbx",     (0.35, 0.12)),
    (shrub_magenta, "Minato_Flora_ShrubMagenta.fbx",  (0.35, 0.12)),
    (kerb_planter,  "Minato_City_KerbPlanter.fbx",    (0.35, 0.12)),
]


def main():
    banner_texture()
    total = 0
    for builder, filename, ratios in CATALOG:
        S.reset_scene()
        obj = builder()
        M.stat(obj, filename.replace(".fbx", ""))
        exports = [obj]
        base = obj.name.replace("_LOD0", "")
        for i, ratio in enumerate(ratios):
            exports.append(M.decimated_copy(obj, f"{base}_LOD{i + 1}", ratio))
        M.export_fbx(exports, filename)
        M.save_blend(filename.replace(".fbx", ".blend"))
        obj.data.calc_loop_triangles()
        total += len(obj.data.loop_triangles)
    print(f"[minato] boulevard complete: {len(CATALOG)} assets, {total:,} LOD0 tris")


if __name__ == "__main__":
    main()

"""
Sakura Pass flora.

The old scene placed trees as flat crossed billboards with a solid pink material, which is the
single loudest tell in the "bad graphics" screenshot. These are real trees: a recursively branched
trunk built from tapered swept tubes with bark texture and proper UVs, plus a canopy of blossom
cards distributed through the crown volume and oriented outward so the silhouette reads as clustered
flower mass rather than two intersecting planes.

Produces:
  SakuraPass_Sakura_Tree_A/B/C.glb   three mature cherry trees (different seeds/ages)
  SakuraPass_Sakura_Sapling.glb      a small filler tree
  SakuraPass_Grass_Tuft.glb          ground-cover card cluster for scattering
  SakuraPass_Fern_Clump.glb          undergrowth for the cut slope

Run via:  blender -b -P build_flora.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import math
import random

import numpy as np

import sakura_lib as S


TAU = math.pi * 2.0


# --------------------------------------------------------------------------- maths

def _basis(direction):
    """An orthonormal (side, up, dir) frame for an arbitrary direction."""
    d = np.asarray(direction, dtype=np.float64)
    d = d / max(np.linalg.norm(d), 1e-9)
    ref = np.array([0.0, 1.0, 0.0]) if abs(d[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
    s = np.cross(ref, d)
    s /= max(np.linalg.norm(s), 1e-9)
    u = np.cross(d, s)
    return s, u, d


def _rotate(v, axis, angle):
    v = np.asarray(v, dtype=np.float64)
    k = np.asarray(axis, dtype=np.float64)
    k = k / max(np.linalg.norm(k), 1e-9)
    return (v * math.cos(angle) +
            np.cross(k, v) * math.sin(angle) +
            k * np.dot(k, v) * (1 - math.cos(angle)))


# ------------------------------------------------------------------------ branching

class Branch:
    __slots__ = ("start", "direction", "length", "radius", "tip_radius", "depth", "tip", "bend_axis")

    def __init__(self, start, direction, length, radius, depth, tip_radius=None):
        self.start = np.asarray(start, dtype=np.float64)
        self.direction = np.asarray(direction, dtype=np.float64)
        self.length = length
        self.radius = radius
        self.tip_radius = tip_radius if tip_radius is not None else radius * TAPER
        self.depth = depth
        self.tip = None
        self.bend_axis = None

    def point_at(self, t):
        """Position along the (bent) spine, 0 at the base and 1 at the tip."""
        p = self.start + self.direction * (self.length * t)
        if self.bend_axis is not None:
            p = p + self.bend_axis * (self.length * math.sin(t * math.pi) * 0.5)
        return p


TAPER = 0.58                # tip radius as a fraction of base radius


def grow(rng, seed_branch, max_depth, splits=(2, 3)):
    """
    Recursive branch skeleton.

    Cherry trees fork low and wide with a slight upward reach at the tips, so children get a large
    divergence angle and a gentle gravity correction rather than staying parallel to the parent.

    Two details matter for the geometry reading as wood rather than spikes:
    * children inherit the parent's *tip* radius (divided by cross-sectional area), so the joints
      are continuous instead of stepping from a needle point back up to a fat cylinder;
    * children start at staggered fractions along the parent, so limbs do not all fork from one
      point like a candelabra.
    """
    branches = []
    queue = [seed_branch]
    while queue:
        b = queue.pop(0)
        b.bend_axis = _bend_axis(rng, b)
        branches.append(b)
        b.tip = b.point_at(1.0)
        if b.depth >= max_depth:
            continue

        n_children = rng.randint(*splits)
        # Wider fork on the lower orders; twiggy ends stay tighter.
        spread = math.radians(30 + b.depth * 8) * rng.uniform(0.8, 1.25)
        roll0 = rng.uniform(0, TAU)
        side, up, d = _basis(b.direction)

        # Conserve cross-sectional area across the fork (da Vinci's rule).
        child_r = b.tip_radius / (n_children ** 0.5)

        for c in range(n_children):
            roll = roll0 + c / n_children * TAU + rng.uniform(-0.4, 0.4)
            axis = side * math.cos(roll) + up * math.sin(roll)
            nd = _rotate(d, axis, spread * rng.uniform(0.6, 1.0))
            # Phototropism: tips lift back toward the light.
            nd = nd + np.array([0.0, 0.16 + 0.06 * b.depth, 0.0])
            nd /= max(np.linalg.norm(nd), 1e-9)

            nl = b.length * rng.uniform(0.68, 0.86)
            # Stagger the fork point along the last quarter of the parent.
            t0 = 1.0 if c == 0 else rng.uniform(0.74, 1.0)
            r = child_r * rng.uniform(0.92, 1.10)
            # Interpolate the parent radius at the fork so the joint matches.
            r = min(r, b.radius + (b.tip_radius - b.radius) * t0)
            queue.append(Branch(b.point_at(t0), nd, nl, r, b.depth + 1))

    return branches


def _bend_axis(rng, b):
    side, up, _ = _basis(b.direction)
    a = rng.uniform(0, TAU)
    return (side * math.cos(a) + up * math.sin(a)) * rng.uniform(0.05, 0.18)


def branch_tube(name_verts, name_faces, name_uvs, b, segments, rng, v_offset=0.0):
    """
    Append one tapered, curved tube for a branch into shared vertex/face/uv lists.

    The taper is linear from ``radius`` to ``tip_radius`` (not down to a point), which is what
    keeps limbs looking like wood instead of flat wedges.
    """
    rings = 6
    base_index = len(name_verts)
    # Bark texel density: aim for one texture tile per ~0.6 m of true circumference, rounded to
    # a whole number of wraps. A fractional wrap (the old hardcoded "* 1.5") makes u snap from
    # e.g. 1.5 back to 0 at the seam edge instead of meeting itself, which reads as a harsh
    # diagonal seam; rounding also keeps thin twigs from stretching the same texture span as
    # the thick trunk base.
    avg_radius = (b.radius + b.tip_radius) * 0.5
    circumference = TAU * max(avg_radius, 1e-4)
    u_repeats = max(1, round(circumference / 0.6))
    for r in range(rings):
        t = r / (rings - 1)
        pos = b.point_at(t)
        radius = b.radius + (b.tip_radius - b.radius) * t

        s2, u2, _ = _basis(b.direction)
        for k in range(segments):
            a = k / segments * TAU
            # Bark is not circular; a little lobing reads as a real trunk section.
            lobe = 1.0 + 0.06 * math.sin(a * 5.0 + b.depth) + 0.03 * math.sin(a * 3.0 - t * 4.0)
            p = pos + (s2 * math.cos(a) + u2 * math.sin(a)) * radius * lobe
            name_verts.append(S.u2b(p[0], p[1], p[2]))

    for r in range(rings - 1):
        for k in range(segments):
            k2 = (k + 1) % segments
            a = base_index + r * segments + k
            bb = base_index + r * segments + k2
            c = base_index + (r + 1) * segments + k2
            dd = base_index + (r + 1) * segments + k
            name_faces.append((a, bb, c, dd))
            u0 = k / segments * u_repeats
            u1 = (k + 1) / segments * u_repeats
            v0 = v_offset + r / (rings - 1) * b.length * 0.85
            v1 = v_offset + (r + 1) / (rings - 1) * b.length * 0.85
            name_uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])


# --------------------------------------------------------------------------- cards

def add_card(verts, faces, uvs, centre, normal, right, w, h, uv_rect, droop=0.0):
    """
    A blossom/leaf quad, optionally drooped so the cluster is not a flat plane.

    ``uv_rect`` selects one cell of the atlas, which is what stops every cluster in the canopy
    looking like a clone of every other one.
    """
    n = np.asarray(normal, dtype=np.float64)
    n /= max(np.linalg.norm(n), 1e-9)
    r = np.asarray(right, dtype=np.float64)
    r = r - n * np.dot(r, n)
    r /= max(np.linalg.norm(r), 1e-9)
    u = np.cross(n, r)

    base = len(verts)
    u0, v0, u1, v1 = uv_rect
    corners = [(-0.5, -0.5, u0, v0), (0.5, -0.5, u1, v0),
               (0.5, 0.5, u1, v1), (-0.5, 0.5, u0, v1)]
    for (cx, cy, uu, vv) in corners:
        p = centre + r * (cx * w) + u * (cy * h)
        # Droop the outer corners so light rakes across the cluster.
        p = p - n * (droop * (cx * cx + cy * cy))
        verts.append(S.u2b(p[0], p[1], p[2]))
        uvs.append((uu, vv))
    faces.append((base, base + 1, base + 2, base + 3))


ATLAS_CELLS = [(c % 2 * 0.5, c // 2 * 0.5, c % 2 * 0.5 + 0.5, c // 2 * 0.5 + 0.5)
               for c in range(4)]


# ---------------------------------------------------------------------- materials

def bark_material():
    mat = S.pbr_material("SakuraPass_Bark", base_color=(0.34, 0.27, 0.25, 1.0), roughness=0.86)
    t = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Bark_Albedo.png")), "Base Color")
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Bark_Rough.png"), True), "Roughness")
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Bark_Normal.png"), True), "Normal")
    return mat


def blossom_material():
    mat = S.pbr_material("SakuraPass_Blossom", base_color=(1.0, 1.0, 1.0, 1.0),
                         roughness=0.72, alpha_blend=True)
    S.set_texture(mat, S.load_image(os.path.join(S.textures_dir(),
                                                 "Sakura_Blossom_Atlas.png")), "Base Color")
    return mat


def leaf_material():
    mat = S.pbr_material("SakuraPass_Leaf", base_color=(1.0, 1.0, 1.0, 1.0),
                         roughness=0.60, alpha_blend=True)
    S.set_texture(mat, S.load_image(os.path.join(S.textures_dir(),
                                                 "Sakura_Leaf_Atlas.png")), "Base Color")
    return mat


# ------------------------------------------------------------------------- the tree

def build_sakura_tree(name, seed=1, height=7.5, max_depth=5, blossom_density=1.0,
                      leaf_ratio=0.28, lean=0.10):
    rng = random.Random(seed)

    trunk_dir = np.array([rng.uniform(-lean, lean), 1.0, rng.uniform(-lean, lean)])
    trunk_dir /= np.linalg.norm(trunk_dir)
    trunk = Branch((0.0, 0.0, 0.0), trunk_dir, height * 0.40, height * 0.058, 0)

    branches = grow(rng, trunk, max_depth)

    # --- woody geometry -------------------------------------------------------
    wv, wf, wu = [], [], []
    for b in branches:
        segs = max(6, 14 - b.depth * 2)
        branch_tube(wv, wf, wu, b, segs, rng=rng)
    wood = S.mesh_from_arrays(f"{name}_Wood", wv, wf, uvs=wu)
    S.assign_material(wood, bark_material())

    # --- flared root buttress -------------------------------------------------
    # Root flare. The top ring sits *inside* the trunk tube so its cap face is never visible.
    root_profile = [(height * 0.058 * 0.94, 0.34)]
    for i in range(1, 8):
        t = i / 7.0
        root_profile.append((height * 0.058 * (1.0 + 1.25 * t ** 2.2), 0.34 - 0.95 * t))
    roots = S.lathe(f"{name}_Roots", root_profile, segments=16, uv_scale=(2.0, 1.0))
    S.assign_material(roots, bark_material())

    # --- canopy ---------------------------------------------------------------
    tips = [b for b in branches if b.depth >= max_depth - 2]
    bv, bf, bu = [], [], []
    lv, lf, lu = [], [], []

    for b in tips:
        # Distribute clusters along the outer half of each twig, not just at the very tip,
        # so the canopy has depth instead of a hollow shell.
        n_cluster = max(2, int(round(3 * blossom_density * rng.uniform(0.7, 1.3))))
        for c in range(n_cluster):
            t = rng.uniform(0.18, 1.05)
            centre = b.point_at(min(t, 1.0)) + b.direction * (b.length * max(0.0, t - 1.0))
            centre = centre + np.array([rng.gauss(0, 0.42), rng.gauss(0, 0.30),
                                        rng.gauss(0, 0.42)])
            size = rng.uniform(0.85, 1.55)

            # Two crossed-but-jittered cards per cluster plus one facing outward from the crown.
            outward = centre - np.array([0.0, height * 0.55, 0.0])
            if np.linalg.norm(outward) < 1e-6:
                outward = np.array([1.0, 0.2, 0.0])
            outward /= np.linalg.norm(outward)

            for k in range(3):
                n = _rotate(outward, np.array([0.0, 1.0, 0.0]), k * TAU / 3.0 + rng.uniform(-0.3, 0.3))
                n = n + np.array([0.0, rng.uniform(-0.35, 0.25), 0.0])
                right = np.cross(np.array([0.0, 1.0, 0.0]), n)
                if np.linalg.norm(right) < 1e-6:
                    right = np.array([1.0, 0.0, 0.0])
                cell = ATLAS_CELLS[rng.randrange(len(ATLAS_CELLS))]
                if rng.random() < leaf_ratio:
                    add_card(lv, lf, lu, centre, n, right, size * 0.7, size * 0.7, cell,
                             droop=size * 0.12)
                else:
                    add_card(bv, bf, bu, centre, n, right, size, size, cell,
                             droop=size * 0.18)

    objs = [wood, roots]
    # At leaf_ratio = 1.0 every canopy card is routed to the leaf atlas and the blossom mesh
    # comes out empty - that is exactly how the section 27 green broadleaf variants are built,
    # so an empty blossom mesh must be skipped rather than exported as a zero-vertex object.
    if bf:
        blossom = S.mesh_from_arrays(f"{name}_Blossom", bv, bf, uvs=bu)
        S.assign_material(blossom, blossom_material())
        objs.append(blossom)
    if lf:
        leaves = S.mesh_from_arrays(f"{name}_Leaves", lv, lf, uvs=lu)
        S.assign_material(leaves, leaf_material())
        objs.append(leaves)

    for o in objs:
        S.report(o.name, o)
    return objs


# ---------------------------------------------------------------------- conifer

def needle_material():
    return S.pbr_material("SakuraPass_Needle", base_color=(0.16, 0.30, 0.20, 1.0),
                          roughness=0.82)


def build_pine(name, seed=5, height=11.0, tiers=9, base_radius=2.5):
    """
    A cedar/pine for the forest section (concept panel 02).

    Deliberately *solid* geometry rather than needle cards: at the distances these are scattered
    at, alpha-tested foliage cards on a conifer read as grey mush, while a stack of jittered cone
    skirts keeps a crisp spiky silhouette under the cel shader. Each skirt is a closed solid
    (apex + drooping outer ring + a raised centre to cap the underside), so ``recalc_normals``
    can orient it unconditionally.
    """
    rng = random.Random(seed)
    objs = []

    trunk_r = height * 0.030
    trunk = S.lathe(f"{name}_Trunk",
                    [(trunk_r * 1.9, -0.15), (trunk_r * 1.25, height * 0.06),
                     (trunk_r, height * 0.45), (trunk_r * 0.45, height * 0.92),
                     (0.0, height)],
                    segments=10, uv_scale=(1.0, 3.0))
    S.assign_material(trunk, bark_material())
    objs.append(trunk)

    verts, faces, uvs = [], [], []
    y0, y1 = height * 0.16, height * 0.94
    segs = 12
    for k in range(tiers):
        t = k / (tiers - 1)
        y = y0 + (y1 - y0) * t
        # Skirts shrink toward the crown; the lowest two are pulled in a little so the tree does
        # not read as a perfect triangle.
        r = base_radius * (1.0 - t) ** 0.78 * (0.82 + 0.18 * min(1.0, t * 4.0))
        r *= rng.uniform(0.88, 1.08)
        tier_h = (y1 - y0) / tiers * rng.uniform(1.5, 2.1)
        droop = tier_h * 0.42
        phase = rng.uniform(0, TAU)

        base_i = len(verts)
        verts.append(S.u2b(0.0, y + tier_h, 0.0))            # apex
        uvs_apex = (0.5, 1.0)
        for s in range(segs):
            a = phase + s / segs * TAU
            # Alternating spoke length is what gives the silhouette its teeth.
            rr = r * (1.0 if s % 2 == 0 else rng.uniform(0.62, 0.78)) * rng.uniform(0.92, 1.08)
            verts.append(S.u2b(math.cos(a) * rr, y - droop * rng.uniform(0.8, 1.2),
                               math.sin(a) * rr))
        verts.append(S.u2b(0.0, y + tier_h * 0.18, 0.0))     # underside cap centre

        apex = base_i
        cap = base_i + segs + 1
        for s in range(segs):
            v0 = base_i + 1 + s
            v1 = base_i + 1 + (s + 1) % segs
            faces.append((apex, v0, v1))
            faces.append((cap, v1, v0))
            uvs.extend([uvs_apex, (s / segs, 0.0), ((s + 1) / segs, 0.0)])
            uvs.extend([(0.5, 0.2), ((s + 1) / segs, 0.0), (s / segs, 0.0)])

    canopy = S.mesh_from_arrays(f"{name}_Canopy", verts, faces, uvs=uvs, smooth=False)
    S.recalc_normals(canopy)
    S.assign_material(canopy, needle_material())
    objs.append(canopy)

    for o in objs:
        S.report(o.name, o)
    return objs


# ----------------------------------------------------------------- ground cover

def build_grass_tuft(name="SakuraPass_Grass_Tuft", seed=7, blades=26, radius=0.42):
    """A cluster of curved blade cards - scatterable ground cover for the verge."""
    rng = random.Random(seed)
    verts, faces, uvs = [], [], []

    for i in range(blades):
        a = rng.uniform(0, TAU)
        r = radius * math.sqrt(rng.random())
        base = np.array([math.cos(a) * r, 0.0, math.sin(a) * r])
        h = rng.uniform(0.22, 0.52)
        lean_dir = np.array([math.cos(a), 0.0, math.sin(a)]) * rng.uniform(0.15, 0.45)

        # Each blade is a 3-segment tapered ribbon that curls over.
        segs = 3
        right = np.array([-math.sin(a), 0.0, math.cos(a)])
        base_i = len(verts)
        for s in range(segs + 1):
            t = s / segs
            p = base + np.array([0.0, h * t, 0.0]) + lean_dir * (t * t)
            w = 0.028 * (1.0 - t) ** 0.7
            for sgn in (-1.0, 1.0):
                q = p + right * (w * sgn)
                verts.append(S.u2b(q[0], q[1], q[2]))
                uvs.append(((sgn * 0.5 + 0.5), t))
        for s in range(segs):
            a0 = base_i + s * 2
            faces.append((a0, a0 + 1, a0 + 3, a0 + 2))

    obj = S.mesh_from_arrays(name, verts, faces, uvs=None)
    # Per-loop UVs need reordering to match the quad winding, so rebuild them here.
    layer = obj.data.uv_layers.new(name="UVMap") if not obj.data.uv_layers else obj.data.uv_layers[0]
    for poly in obj.data.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            layer.data[li].uv = uvs[vi]

    mat = S.pbr_material("SakuraPass_GrassBlade", base_color=(0.42, 0.55, 0.26, 1.0),
                         roughness=0.78, alpha_blend=True)
    S.assign_material(obj, mat)
    S.report(name, obj)
    return [obj]


def build_fern_clump(name="SakuraPass_Fern_Clump", seed=11, fronds=9):
    """Arching fern fronds built as tapered ribbons - undergrowth for the shaded cut slope."""
    rng = random.Random(seed)
    verts, faces, uvs = [], [], []

    for i in range(fronds):
        a = i / fronds * TAU + rng.uniform(-0.25, 0.25)
        right = np.array([-math.sin(a), 0.0, math.cos(a)])
        out = np.array([math.cos(a), 0.0, math.sin(a)])
        length = rng.uniform(0.75, 1.15)
        segs = 7
        base_i = len(verts)
        for s in range(segs + 1):
            t = s / segs
            # Arch: rises fast, then falls away at the tip.
            y = length * (1.35 * t - 0.95 * t * t)
            p = out * (length * t * 0.85) + np.array([0.0, y, 0.0])
            w = 0.11 * math.sin(math.pi * min(1.0, t * 1.15)) ** 0.6
            for sgn in (-1.0, 1.0):
                q = p + right * (w * sgn)
                verts.append(S.u2b(q[0], q[1], q[2]))
                uvs.append(((sgn * 0.5 + 0.5), t))
        for s in range(segs):
            a0 = base_i + s * 2
            faces.append((a0, a0 + 1, a0 + 3, a0 + 2))

    obj = S.mesh_from_arrays(name, verts, faces, uvs=None)
    layer = obj.data.uv_layers.new(name="UVMap") if not obj.data.uv_layers else obj.data.uv_layers[0]
    for poly in obj.data.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            layer.data[li].uv = uvs[vi]

    mat = S.pbr_material("SakuraPass_Fern", base_color=(0.28, 0.44, 0.21, 1.0),
                         roughness=0.72, alpha_blend=True)
    S.assign_material(obj, mat)
    S.report(name, obj)
    return [obj]


# --------------------------------------------------------------- section 28 groundcover
#
# Section 28's rule is that the road edge must not read as "asphalt ends -> perfect grass
# texture begins". The route already had grass tufts, ferns, rock clusters and petal drifts;
# what it had no vocabulary for was the *transitional* material - small flowers, low shrubs,
# moss/soil patches and loose stones - that physically breaks the boundary line.
#
# All of these are deliberately small and cheap: section 28 says near-road density matters
# more than distant density, so these are authored to be scattered in bulk in the 0.2-3 m band
# either side of the shoulder, where the camera actually resolves them.


def _cards_obj(name, verts, faces, uvs):
    """Build a card mesh and re-lay its per-vertex UVs onto loops (see build_grass_tuft)."""
    obj = S.mesh_from_arrays(name, verts, faces, uvs=None)
    layer = obj.data.uv_layers.new(name="UVMap") if not obj.data.uv_layers else obj.data.uv_layers[0]
    for poly in obj.data.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            layer.data[li].uv = uvs[vi]
    return obj


def _ribbon(verts, faces, uvs, base, direction, right, length, width, segs=3, curl=0.0):
    """A tapered curling ribbon - the shared primitive behind stems, blades and petals."""
    base_i = len(verts)
    for s in range(segs + 1):
        t = s / segs
        p = base + direction * (length * t) + np.array([0.0, -curl * t * t, 0.0])
        w = width * (1.0 - t) ** 0.6
        for sgn in (-1.0, 1.0):
            q = p + right * (w * sgn)
            verts.append(S.u2b(q[0], q[1], q[2]))
            uvs.append(((sgn * 0.5 + 0.5), t))
    for s in range(segs):
        a0 = base_i + s * 2
        faces.append((a0, a0 + 1, a0 + 3, a0 + 2))


def build_flower_clump(name, seed=21, stems=16, radius=0.30, colour=(0.92, 0.72, 0.82, 1.0)):
    """
    Small roadside flowers: a thin stem with a 3-card crossed head.

    One material per object is the pipeline convention, so the stem is tinted toward the
    petal colour rather than being separately green - at 0.3 m tall and scattered by the
    hundred, the head is all the camera resolves anyway.
    """
    rng = random.Random(seed)
    verts, faces, uvs = [], [], []

    for _ in range(stems):
        a = rng.uniform(0, TAU)
        r = radius * math.sqrt(rng.random())
        base = np.array([math.cos(a) * r, 0.0, math.sin(a) * r])
        h = rng.uniform(0.16, 0.34)
        lean = np.array([math.cos(a), 0.0, math.sin(a)]) * rng.uniform(0.04, 0.14)
        up = np.array([0.0, 1.0, 0.0])

        stem_dir = up + lean
        stem_dir = stem_dir / np.linalg.norm(stem_dir)
        right = np.array([-math.sin(a), 0.0, math.cos(a)])
        _ribbon(verts, faces, uvs, base, stem_dir, right, h, 0.012, segs=2)

        # Head: three crossed cards at the stem tip so the bloom reads from any angle.
        head = base + stem_dir * h
        petal = rng.uniform(0.045, 0.085)
        for k in range(3):
            ang = a + k * (TAU / 3.0) + rng.uniform(-0.2, 0.2)
            hr = np.array([-math.sin(ang), 0.0, math.cos(ang)])
            hd = np.array([math.cos(ang) * 0.45, 0.85, math.sin(ang) * 0.45])
            hd = hd / np.linalg.norm(hd)
            _ribbon(verts, faces, uvs, head - hd * petal * 0.5, hd, hr,
                    petal * 2.0, petal * 0.8, segs=2)

    obj = _cards_obj(name, verts, faces, uvs)
    mat = S.pbr_material(name.replace("SakuraPass_", "SakuraPass_Mat_"),
                         base_color=colour, roughness=0.74, alpha_blend=True)
    S.assign_material(obj, mat)
    S.report(name, obj)
    return [obj]


def build_low_shrub(name, seed=31, cards=36, radius=0.52, height=0.62):
    """A low leafy dome - the mid-height filler between grass tufts and tree trunks."""
    rng = random.Random(seed)
    verts, faces, uvs = [], [], []

    for _ in range(cards):
        # Sample the upper hemisphere, biased outward so the silhouette is a squat dome
        # rather than a ball floating on the ground.
        a = rng.uniform(0, TAU)
        el = math.asin(rng.uniform(0.05, 1.0)) * rng.uniform(0.55, 1.0)
        r = radius * math.cos(el)
        y = height * math.sin(el)
        base = np.array([math.cos(a) * r, y * 0.35, math.sin(a) * r])
        out = np.array([math.cos(a) * 0.8, 0.6, math.sin(a) * 0.8])
        out = out / np.linalg.norm(out)
        right = np.array([-math.sin(a), 0.0, math.cos(a)])
        leaf = rng.uniform(0.16, 0.30)
        _ribbon(verts, faces, uvs, base, out, right, leaf, leaf * 0.42, segs=2,
                curl=leaf * rng.uniform(0.15, 0.45))

    obj = _cards_obj(name, verts, faces, uvs)
    mat = S.pbr_material(name.replace("SakuraPass_", "SakuraPass_Mat_"),
                         base_color=(0.30, 0.42, 0.22, 1.0), roughness=0.80, alpha_blend=True)
    S.assign_material(obj, mat)
    S.report(name, obj)
    return [obj]


def build_moss_patch(name, seed=41, radius=0.85, rings=3, segments=18):
    """
    A flat irregular moss / bare-soil patch that hugs the ground.

    This is the piece that actually attacks the section-28 rule: scattered along the shoulder
    it replaces stretches of "perfect grass texture" with damp soil and moss, so the asphalt
    meets a broken, organic edge instead of a clean one.
    """
    rng = random.Random(seed)
    verts, faces, uvs = [], [], []

    # Irregular outline: per-segment radius scale, smoothed so the rim undulates.
    jitter = [rng.uniform(0.55, 1.0) for _ in range(segments)]
    jitter = [(jitter[i - 1] + jitter[i] + jitter[(i + 1) % segments]) / 3.0
              for i in range(segments)]

    verts.append(S.u2b(0.0, 0.030, 0.0))
    uvs.append((0.5, 0.5))
    for ring in range(1, rings + 1):
        t = ring / rings
        for i in range(segments):
            a = i / segments * TAU
            r = radius * jitter[i] * t
            # Domed very slightly at the centre, feathering to ground level at the rim.
            y = 0.030 * (1.0 - t * t) + rng.uniform(-0.004, 0.004)
            verts.append(S.u2b(math.cos(a) * r, y, math.sin(a) * r))
            uvs.append((0.5 + math.cos(a) * t * 0.5, 0.5 + math.sin(a) * t * 0.5))

    for i in range(segments):
        faces.append((0, 1 + i, 1 + (i + 1) % segments))
    for ring in range(1, rings):
        a0 = 1 + (ring - 1) * segments
        b0 = 1 + ring * segments
        for i in range(segments):
            j = (i + 1) % segments
            faces.append((a0 + i, b0 + i, b0 + j, a0 + j))

    obj = _cards_obj(name, verts, faces, uvs)
    S.recalc_normals(obj)
    mat = S.pbr_material(name.replace("SakuraPass_", "SakuraPass_Mat_"),
                         base_color=(0.32, 0.34, 0.22, 1.0), roughness=0.92)
    S.assign_material(obj, mat)
    S.report(name, obj)
    return [obj]


def build_stone_scatter(name, seed=51, stones=13, spread=0.55):
    """Loose gravel / small retaining stones, each half-buried so nothing looks dropped on."""
    rng = random.Random(seed)
    verts, faces, uvs = [], [], []
    rings, segs = 4, 7

    for _ in range(stones):
        a = rng.uniform(0, TAU)
        d = spread * math.sqrt(rng.random())
        centre = np.array([math.cos(a) * d, 0.0, math.sin(a) * d])
        rad = rng.uniform(0.05, 0.17)
        flat = rng.uniform(0.42, 0.68)
        # Sunk so roughly a third of the stone is below ground: half-buried stones read as
        # part of the terrain, stones sitting on a plane read as props.
        sink = rad * flat * 0.35
        lumps = [rng.uniform(0.78, 1.22) for _ in range(rings * segs)]

        base_i = len(verts)
        for ri in range(rings + 1):
            phi = math.pi * ri / rings
            for si in range(segs):
                th = TAU * si / segs
                k = lumps[min(ri, rings - 1) * segs + si]
                p = centre + np.array([
                    math.sin(phi) * math.cos(th) * rad * k,
                    math.cos(phi) * rad * flat * k - sink,
                    math.sin(phi) * math.sin(th) * rad * k,
                ])
                verts.append(S.u2b(p[0], p[1], p[2]))
                uvs.append((si / float(segs), ri / float(rings)))
        for ri in range(rings):
            for si in range(segs):
                sj = (si + 1) % segs
                a0 = base_i + ri * segs
                b0 = base_i + (ri + 1) * segs
                faces.append((a0 + si, b0 + si, b0 + sj, a0 + sj))

    obj = _cards_obj(name, verts, faces, uvs)
    S.recalc_normals(obj)
    mat = S.pbr_material(name.replace("SakuraPass_", "SakuraPass_Mat_"),
                         base_color=(0.46, 0.44, 0.43, 1.0), roughness=0.88)
    S.assign_material(obj, mat)
    S.report(name, obj)
    return [obj]


def main():
    variants = [
        ("SakuraPass_Sakura_Tree_A", dict(seed=3, height=8.2, max_depth=5, blossom_density=1.15)),
        ("SakuraPass_Sakura_Tree_B", dict(seed=17, height=6.6, max_depth=5, blossom_density=0.95,
                                          leaf_ratio=0.36, lean=0.16)),
        ("SakuraPass_Sakura_Tree_C", dict(seed=42, height=9.4, max_depth=6, blossom_density=0.85,
                                          leaf_ratio=0.22)),
        ("SakuraPass_Sakura_Sapling", dict(seed=88, height=3.6, max_depth=4, blossom_density=1.0,
                                           leaf_ratio=0.45, lean=0.2)),
        # --- section 27: green broadleaf deciduous ------------------------------------------
        # The doc asks for 30-40% green evergreen/deciduous against 40-50% sakura. The only
        # green tree the route had was a 12 m opaque conifer, which is why the scatter banned
        # conifers from the verge outright (a band-0 pine owns a third of the rider's frame).
        # The result was that every tree the camera actually sees near or mid was pink.
        # These share the sakura growth structure and crown scale - same translucent card
        # canopy, same silhouette family - but route every card to the leaf atlas
        # (leaf_ratio = 1.0), so they give green contrast at the verge WITHOUT blocking the
        # view the way a conifer does. Heights/seeds are PROVISIONAL tuning.
        ("SakuraPass_Broadleaf_A", dict(seed=101, height=8.0, max_depth=5, blossom_density=1.05,
                                        leaf_ratio=1.0)),
        ("SakuraPass_Broadleaf_B", dict(seed=137, height=6.4, max_depth=5, blossom_density=1.20,
                                        leaf_ratio=1.0, lean=0.18)),
        ("SakuraPass_Broadleaf_C", dict(seed=163, height=9.6, max_depth=6, blossom_density=0.90,
                                        leaf_ratio=1.0)),
        ("SakuraPass_Broadleaf_Sapling", dict(seed=199, height=3.4, max_depth=4,
                                              blossom_density=1.15, leaf_ratio=1.0, lean=0.22)),
    ]

    for (name, kw) in variants:
        S.reset_scene()
        print(f"[sakura] growing {name}...")
        objs = build_sakura_tree(name, **kw)
        S.export_glb(objs, f"{name}.glb")

    S.reset_scene()
    S.export_glb(build_grass_tuft(), "SakuraPass_Grass_Tuft.glb")

    S.reset_scene()
    S.export_glb(build_fern_clump(), "SakuraPass_Fern_Clump.glb")

    # --- section 28: transitional groundcover for the road edge --------------------------
    # Palettes and counts are PROVISIONAL tuning.
    for (gname, fn, kw) in (
        ("SakuraPass_Flower_Clump_A", build_flower_clump,
         dict(seed=21, stems=16, colour=(0.94, 0.74, 0.84, 1.0))),   # pale sakura pink
        ("SakuraPass_Flower_Clump_B", build_flower_clump,
         dict(seed=23, stems=14, radius=0.26, colour=(0.96, 0.90, 0.62, 1.0))),  # yellow
        ("SakuraPass_Flower_Clump_C", build_flower_clump,
         dict(seed=27, stems=12, radius=0.22, colour=(0.70, 0.62, 0.86, 1.0))),  # violet
        ("SakuraPass_Low_Shrub_A", build_low_shrub, dict(seed=31)),
        ("SakuraPass_Low_Shrub_B", build_low_shrub,
         dict(seed=37, cards=28, radius=0.40, height=0.46)),
        ("SakuraPass_Moss_Patch_A", build_moss_patch, dict(seed=41)),
        ("SakuraPass_Moss_Patch_B", build_moss_patch, dict(seed=47, radius=1.25, rings=4)),
        ("SakuraPass_Stone_Scatter_A", build_stone_scatter, dict(seed=51)),
        ("SakuraPass_Stone_Scatter_B", build_stone_scatter,
         dict(seed=57, stones=9, spread=0.85)),
    ):
        S.reset_scene()
        print(f"[sakura] growing {gname}...")
        S.export_glb(fn(gname, **kw), f"{gname}.glb")

    for (pname, kw) in (("SakuraPass_Pine_A", dict(seed=5, height=12.4, tiers=10)),
                        ("SakuraPass_Pine_B", dict(seed=29, height=9.2, tiers=8,
                                                   base_radius=2.15))):
        S.reset_scene()
        print(f"[sakura] growing {pname}...")
        S.export_glb(build_pine(pname, **kw), f"{pname}.glb")

    print("[sakura] flora build complete.")


if __name__ == "__main__":
    main()

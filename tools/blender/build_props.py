"""
Sakura Pass props.

All of these replace scaled cubes and cylinders in the old setup script with modelled geometry:

  SakuraPass_Torii_Gate.glb        tapered pillars, curved kasagi with upswept ends, nuki beam,
                                   gakuzuka tablet and stone base plinths
  SakuraPass_Torii_Great.glb       the same gate at road-spanning scale (11.6 m)
  SakuraPass_Chevron_Sign.glb      yellow hairpin chevron board on twin posts
  SakuraPass_Summit_Sign.glb       timber pass signboard for the summit viewpoint
  SakuraPass_Banner_Pole.glb       nobori banner on a cross-armed pole
  SakuraPass_Stone_Lantern.glb     a lathed kasuga-doro: base, shaft, platform, fire box with
                                   cut light openings, faceted roof and jewel finial
  SakuraPass_Retaining_Wall.glb    a battered dry-stone wall built from individually placed,
                                   bevelled blocks in staggered courses, with a coping run
  SakuraPass_Rock_Cluster_A/B.glb  displaced icosahedra with flat bedding planes
  SakuraPass_Cliff_Ledge.glb       a stratified outcrop for the cut slope
  SakuraPass_Distance_Marker.glb   roadside kilometre post

Run via:  blender -b -P build_props.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import math
import random

import numpy as np

import sakura_lib as S


TAU = math.pi * 2.0


# ------------------------------------------------------------------- materials

def stone_material(name="SakuraPass_Stone", color=(0.52, 0.51, 0.48, 1.0)):
    mat = S.pbr_material(name, base_color=color, roughness=0.88)
    t = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Rock_Albedo.png")), "Base Color")
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Rock_Rough.png"), True), "Roughness")
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Rock_Normal.png"), True), "Normal")
    return mat


def vermilion_material():
    """Shu-iro - the traditional vermilion lacquer of a torii."""
    return S.pbr_material("SakuraPass_Vermilion", base_color=(0.76, 0.16, 0.11, 1.0),
                          roughness=0.46)


# ---------------------------------------------------------------------- helpers

def box(name, size, centre=(0, 0, 0), taper=1.0, bevel=0.0, uv_scale=0.45, uv_offset=(0.0, 0.0)):
    """
    An axis-aligned block that can taper toward +Y and carry a bevel.

    Used for wall stones and beams - still real modelled geometry with its own UVs, not a
    ``CreatePrimitive`` cube with a non-uniform scale baked into the transform.
    ``uv_offset`` lets callers slide each block to a different part of a shared stone texture so a
    wall does not read as a hundred copies of one stone.
    """
    sx, sy, sz = (s * 0.5 for s in size)
    cx, cy, cz = centre
    verts_u = []
    for (yi, yy) in ((0, -sy), (1, sy)):
        k = 1.0 if yi == 0 else taper
        for (ax, az) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            verts_u.append((cx + ax * sx * k, cy + yy, cz + az * sz * k))

    verts = [S.u2b(*v) for v in verts_u]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4),
             (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    # World-scaled UVs so a shared stone texture never looks different block to block.
    uvs = []
    for f in faces:
        for i in f:
            v = verts_u[i]
            uvs.append(((v[0] + v[2]) * uv_scale + uv_offset[0],
                        v[1] * uv_scale + uv_offset[1]))

    obj = S.mesh_from_arrays(name, verts, faces, uvs=uvs, smooth=False)
    if bevel > 0:
        S.add_bevel(obj, width=bevel, segments=2, angle_deg=30.0)
    return obj


def curved_beam(name, length, rise, width, height, segments=26, end_lift=0.0):
    """
    The kasagi (top lintel) of a torii: a beam with a shallow sag-reversed camber whose ends sweep
    upward. Swept as a real cross-section along a curved spine.
    """
    verts = []
    faces = []
    uvs = []
    for i in range(segments + 1):
        t = i / segments
        x = (t - 0.5) * length
        # Camber plus an accelerating lift in the outer 25% at each end.
        e = max(0.0, abs(t - 0.5) * 2.0 - 0.75) / 0.25
        y = rise * math.sin(t * math.pi) + end_lift * (e ** 2.0)
        # Slight taper toward the tips.
        w = width * (1.0 - 0.18 * abs(t - 0.5) * 2.0)
        h = height * (1.0 - 0.22 * abs(t - 0.5) * 2.0)
        for (ax, ay) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            verts.append(S.u2b(x, y + ay * h * 0.5, ax * w * 0.5))

    for i in range(segments):
        a = i * 4
        b = (i + 1) * 4
        for k in range(4):
            k2 = (k + 1) % 4
            faces.append((a + k, a + k2, b + k2, b + k))
            uvs.extend([(k / 4.0, i / segments * length),
                        ((k + 1) / 4.0, i / segments * length),
                        ((k + 1) / 4.0, (i + 1) / segments * length),
                        (k / 4.0, (i + 1) / segments * length)])
    faces.append((0, 3, 2, 1))
    uvs.extend([(0, 0), (1, 0), (1, 1), (0, 1)])
    last = segments * 4
    faces.append((last, last + 1, last + 2, last + 3))
    uvs.extend([(0, 0), (1, 0), (1, 1), (0, 1)])

    return S.mesh_from_arrays(name, verts, faces, uvs=uvs, smooth=False)


# ------------------------------------------------------------------------ torii

def build_torii(name="SakuraPass_Torii_Gate", span=5.6, height=6.4, pillar_r=0.30):
    print(f"[sakura] building {name}...")
    objs = []
    red = vermilion_material()
    stone = stone_material("SakuraPass_ToriiPlinth")

    for sgn in (-1.0, 1.0):
        x = sgn * span * 0.5
        # Pillars lean inward slightly (korobi) and taper - both are real torii construction.
        lean = 0.055
        profile = []
        n = 9
        for i in range(n):
            t = i / (n - 1)
            profile.append((pillar_r * (1.0 - 0.16 * t), t * height))
        pillar = S.lathe(f"{name}_Pillar_{int(sgn)}", profile, segments=20, uv_scale=(2.0, 3.0))
        pillar.location = S.u2b_vec((x, 0.0, 0.0))
        pillar.rotation_euler = (0.0, 0.0, 0.0)
        # Apply the inward lean as a shear by moving the object and rotating about Blender's Y.
        pillar.rotation_mode = 'XYZ'
        pillar.rotation_euler[1] = -sgn * lean
        S.assign_material(pillar, red)
        objs.append(pillar)

        plinth = S.lathe(f"{name}_Plinth_{int(sgn)}",
                         [(pillar_r * 2.0, -0.30), (pillar_r * 2.0, 0.16),
                          (pillar_r * 1.55, 0.30), (pillar_r * 1.2, 0.34)],
                         segments=18, uv_scale=(2.0, 1.0))
        plinth.location = S.u2b_vec((x, 0.0, 0.0))
        S.assign_material(plinth, stone)
        objs.append(plinth)

    # Kasagi: the curved top lintel, overhanging the pillars.
    kasagi = curved_beam(f"{name}_Kasagi", span * 1.55, 0.30, 0.46, 0.30,
                         end_lift=0.42)
    kasagi.location = S.u2b_vec((0.0, height + 0.30, 0.0))
    S.assign_material(kasagi, red)
    objs.append(kasagi)

    # Shimaki: the secondary lintel directly beneath it.
    shimaki = curved_beam(f"{name}_Shimaki", span * 1.48, 0.24, 0.36, 0.20, end_lift=0.30)
    shimaki.location = S.u2b_vec((0.0, height + 0.03, 0.0))
    S.assign_material(shimaki, red)
    objs.append(shimaki)

    # Nuki: the straight tie beam that passes through the pillars.
    nuki = box(f"{name}_Nuki", (span * 1.18, 0.28, 0.24),
               centre=(0.0, height * 0.74, 0.0), bevel=0.02)
    S.assign_material(nuki, red)
    objs.append(nuki)

    # Gakuzuka: the small tablet strut between nuki and shimaki.
    gaku = box(f"{name}_Gakuzuka", (0.46, height * 0.24, 0.16),
               centre=(0.0, height * 0.74 + height * 0.12 + 0.02, 0.0), bevel=0.02)
    S.assign_material(gaku, red)
    objs.append(gaku)

    for o in objs:
        S.report(o.name, o)
    S.export_glb(objs, f"{name}.glb")
    return objs


def build_great_torii(name="SakuraPass_Torii_Great"):
    """
    The road-spanning shrine gate of concept panel 03.

    ``build_torii``'s 5.6 m default is a footpath gate; the carriageway alone is 7 m wide
    (``ROAD_HALF_WIDTH`` 3.5) before shoulders, so the pillars need an 11.6 m span to stand clear
    of the verge, and the height has to scale with it or the gate reads as squat. Pillar radius
    scales too - a 0.30 m pillar carrying a 18 m kasagi looks like scaffolding.
    """
    return build_torii(name, span=11.6, height=9.4, pillar_r=0.56)


# ---------------------------------------------------------------- stone lantern

def build_lantern(name="SakuraPass_Stone_Lantern"):
    """A kasuga-doro: the classic pedestal lantern found along Japanese mountain roads."""
    print(f"[sakura] building {name}...")
    objs = []
    stone = stone_material("SakuraPass_LanternStone", color=(0.58, 0.57, 0.53, 1.0))

    # Kiso (base) + sao (shaft) as one lathed run so the silhouette is continuous.
    body = [
        (0.34, 0.00), (0.34, 0.10), (0.28, 0.16), (0.22, 0.20),
        (0.12, 0.26), (0.105, 0.62), (0.115, 0.98), (0.10, 1.04),
        (0.13, 1.10), (0.115, 1.16),
    ]
    shaft = S.lathe(f"{name}_Shaft", body, segments=20, uv_scale=(7.0, 9.0))
    S.assign_material(shaft, stone)
    objs.append(shaft)

    # Chudai: the platform the fire box sits on.
    chudai = S.lathe(f"{name}_Platform",
                     [(0.16, 1.16), (0.30, 1.22), (0.32, 1.30), (0.26, 1.36), (0.24, 1.40)],
                     segments=20, uv_scale=(7.0, 3.0))
    S.assign_material(chudai, stone)
    objs.append(chudai)

    # Hibukuro: hexagonal fire box, built as six wall panels so the light openings are real
    # geometry rather than a texture.
    hb_r, hb_h = 0.26, 0.40
    for f in range(6):
        a0 = f / 6.0 * TAU
        a1 = (f + 1) / 6.0 * TAU
        p0 = np.array([math.cos(a0) * hb_r, 0.0, math.sin(a0) * hb_r])
        p1 = np.array([math.cos(a1) * hb_r, 0.0, math.sin(a1) * hb_r])
        thick = 0.035
        inward = -(p0 + p1) / max(np.linalg.norm(p0 + p1), 1e-9) * thick

        if f % 2 == 0:
            # Open face: a frame with a window - four bars around a void.
            bars = [(0.00, 0.09), (0.31, 0.40)]         # bottom rail, top rail (as v-ranges)
            for (v0, v1) in bars:
                verts, faces, uvs = [], [], []
                for (pp, ii) in ((p0, 0), (p1, 1)):
                    for vv in (v0, v1):
                        verts.append(S.u2b(pp[0], 1.40 + vv, pp[2]))
                        verts.append(S.u2b(pp[0] + inward[0], 1.40 + vv, pp[2] + inward[2]))
                faces.append((0, 1, 5, 4))
                faces.append((2, 6, 7, 3))
                faces.append((0, 4, 6, 2))
                faces.append((1, 3, 7, 5))
                faces.append((0, 2, 3, 1))
                faces.append((4, 5, 7, 6))
                uvs = [(0.12, 0.12), (0.30, 0.12), (0.30, 0.30), (0.12, 0.30)] * len(faces)
                bar = S.mesh_from_arrays(f"{name}_Box_{f}_{int(v0 * 100)}", verts, faces,
                                         uvs=uvs, smooth=False)
                S.recalc_normals(bar)
                S.assign_material(bar, stone)
                objs.append(bar)
        else:
            verts, faces, uvs = [], [], []
            for (pp, ii) in ((p0, 0), (p1, 1)):
                for vv in (0.0, hb_h):
                    verts.append(S.u2b(pp[0], 1.40 + vv, pp[2]))
                    verts.append(S.u2b(pp[0] + inward[0], 1.40 + vv, pp[2] + inward[2]))
            faces = [(0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5),
                     (0, 2, 3, 1), (4, 5, 7, 6)]
            uvs = [(0.12, 0.12), (0.30, 0.12), (0.30, 0.30), (0.12, 0.30)] * len(faces)
            panel = S.mesh_from_arrays(f"{name}_Box_{f}", verts, faces, uvs=uvs, smooth=False)
            S.recalc_normals(panel)
            S.assign_material(panel, stone)
            objs.append(panel)

    # Kasa: the hexagonal roof, with the warabite (upswept corner) profile approximated by a
    # concave flare, plus the hoju (jewel) finial.
    roof_top = 1.40 + hb_h
    kasa = []
    for i in range(9):
        t = i / 8.0
        r = 0.50 * (1.0 - t) ** 0.62 + 0.05
        # Concave sweep - the eave curls back up at the rim.
        y = roof_top + 0.30 * t ** 1.5 - 0.06 * math.sin(t * math.pi) + (0.05 if i == 0 else 0.0)
        kasa.append((r, y))
    roof = S.lathe(f"{name}_Roof", kasa, segments=6, uv_scale=(5.0, 4.0))
    S.assign_material(roof, stone)
    objs.append(roof)

    finial = S.lathe(f"{name}_Finial",
                     [(0.055, roof_top + 0.30), (0.10, roof_top + 0.40),
                      (0.085, roof_top + 0.50), (0.035, roof_top + 0.58),
                      (0.0, roof_top + 0.62)],
                     segments=14, uv_scale=(4.0, 4.0))
    S.assign_material(finial, stone)
    objs.append(finial)

    # A warm glow inside the fire box sells it at dusk.
    glow_mat = S.pbr_material("SakuraPass_LanternGlow", base_color=(1.0, 0.78, 0.42, 1.0),
                              roughness=1.0, emission=(1.0, 0.72, 0.36, 1.0),
                              emission_strength=2.2)
    glow = S.lathe(f"{name}_Glow", [(0.0, 1.46), (0.16, 1.50), (0.16, 1.70), (0.0, 1.74)],
                   segments=12)
    S.assign_material(glow, glow_mat)
    objs.append(glow)

    for o in objs:
        S.report(o.name, o)
    S.export_glb(objs, f"{name}.glb")
    return objs


# -------------------------------------------------------------- retaining wall

def build_retaining_wall(name="SakuraPass_Retaining_Wall", length=12.0, height=2.6,
                         batter=0.12, seed=5):
    """
    Dry-stone revetment of the kind that holds up every Japanese mountain road.

    Each course is laid as individually sized, jittered blocks so the joints stagger; the whole
    face leans back by ``batter`` as it rises, which is what makes it read as engineered masonry
    instead of a textured slab.
    """
    print(f"[sakura] building {name}...")
    rng = random.Random(seed)
    objs = []
    stone = stone_material("SakuraPass_WallStone", color=(0.50, 0.49, 0.46, 1.0))

    courses = 9
    y = 0.0
    for c in range(courses):
        ch = height / courses * rng.uniform(0.88, 1.12)
        t = y / height
        setback = batter * height * t
        depth = 0.55 * (1.0 - 0.25 * t)

        x = -length * 0.5
        # Offset the first joint per course so vertical seams never line up.
        x += rng.uniform(0.0, 0.7)
        idx = 0
        while x < length * 0.5:
            w = rng.uniform(0.42, 0.95)
            w = min(w, length * 0.5 - x)
            if w < 0.16:
                break
            blk = box(f"{name}_C{c}_S{idx}",
                      (w * 0.96, ch * 0.94, depth),
                      # Align every stone's FRONT face on the course plane and let the depth
                      # variation run backwards into the fill. Centring them instead made the
                      # deeper stones jut out and the face read as a stack of shelves.
                      centre=(x + w * 0.5, y + ch * 0.5,
                              setback + depth * 0.5 + rng.uniform(-0.015, 0.015)),
                      bevel=0.018,
                      uv_offset=(rng.uniform(0, 4), rng.uniform(0, 4)))
            # A touch of random roll so the faces catch light unevenly.
            blk.rotation_mode = 'XYZ'
            blk.rotation_euler[2] = rng.uniform(-0.03, 0.03)
            S.assign_material(blk, stone)
            objs.append(blk)
            x += w + rng.uniform(0.01, 0.05)
            idx += 1
        y += ch

    # Coping course: one continuous run of flat capstones, proud of the face.
    x = -length * 0.5
    idx = 0
    while x < length * 0.5:
        w = min(rng.uniform(0.7, 1.2), length * 0.5 - x)
        if w < 0.2:
            break
        cap = box(f"{name}_Cap_{idx}", (w * 0.97, 0.20, 0.72),
                  centre=(x + w * 0.5, y + 0.10, batter * height + 0.30), bevel=0.025,
                  uv_offset=(rng.uniform(0, 4), rng.uniform(0, 4)))
        S.assign_material(cap, stone)
        objs.append(cap)
        x += w + 0.02
        idx += 1

    print(f"[sakura]   {len(objs)} stones")
    S.export_glb(objs, f"{name}.glb")
    return objs


# -------------------------------------------------------------------- rockwork

def _displaced_sphere(name, radius, seed, rings=16, segs=24, squash=0.62,
                      strata=0.35, roughness=0.42):
    """
    A boulder: a UV sphere pushed around by fbm, then flattened into horizontal bedding planes.

    The strata term is what stops it looking like a lumpy potato - real rock breaks along planes.
    """
    rng = random.Random(seed)
    off = np.array([rng.uniform(0, 100) for _ in range(3)])

    verts = []
    for r in range(rings + 1):
        phi = r / rings * math.pi
        for s in range(segs):
            th = s / segs * TAU
            n = np.array([math.sin(phi) * math.cos(th),
                          math.cos(phi),
                          math.sin(phi) * math.sin(th)])
            p = n * radius
            q = p + off
            d = S.fbm_2d(np.array([q[0] * 1.1 + q[1] * 0.4]),
                         np.array([q[2] * 1.1 - q[1] * 0.4]), 1.0, octaves=4, seed=seed)[0]
            d2 = S.fbm_2d(np.array([q[0] * 3.3]), np.array([q[2] * 3.3]), 1.0,
                          octaves=3, seed=seed + 7)[0]
            scale = 1.0 + (d - 0.5) * 2.0 * roughness + (d2 - 0.5) * 2.0 * roughness * 0.3
            p = n * radius * scale
            p[1] *= squash
            # Quantise height into beds and blend partway back, giving flat-ish strata.
            bed = 0.22 * radius
            flat = round(p[1] / bed) * bed
            p[1] = p[1] * (1.0 - strata) + flat * strata
            verts.append(S.u2b(p[0], p[1] + radius * squash, p[2]))

    faces = []
    uvs = []
    for r in range(rings):
        for s in range(segs):
            s2 = (s + 1) % segs
            a = r * segs + s
            b = r * segs + s2
            c = (r + 1) * segs + s2
            d = (r + 1) * segs + s
            faces.append((a, b, c, d))
            u0, u1 = s / segs * 3.0, (s + 1) / segs * 3.0
            v0, v1 = r / rings * 2.0, (r + 1) / rings * 2.0
            uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    obj = S.mesh_from_arrays(name, verts, faces, uvs=uvs)
    S.add_weld(obj, distance=0.001)
    S.recalc_normals(obj)
    return obj


def build_rock_cluster(name, seed, count=5, spread=1.9, base_radius=0.85):
    print(f"[sakura] building {name}...")
    rng = random.Random(seed)
    stone = stone_material("SakuraPass_Boulder")
    objs = []
    for i in range(count):
        r = base_radius * rng.uniform(0.45, 1.25)
        rock = _displaced_sphere(f"{name}_{i}", r, seed=seed * 31 + i * 7,
                                 squash=rng.uniform(0.66, 0.95),
                                 strata=rng.uniform(0.12, 0.28))
        a = rng.uniform(0, TAU)
        d = spread * math.sqrt(rng.random())
        rock.location = S.u2b_vec((math.cos(a) * d, -r * 0.30, math.sin(a) * d))
        rock.rotation_mode = 'XYZ'
        rock.rotation_euler[2] = rng.uniform(0, TAU)
        S.assign_material(rock, stone)
        objs.append(rock)
    S.export_glb(objs, f"{name}.glb")
    return objs


def build_cliff_ledge(name="SakuraPass_Cliff_Ledge", seed=91):
    """A stratified outcrop to break up the cut slope above the road."""
    print(f"[sakura] building {name}...")
    rng = random.Random(seed)
    stone = stone_material("SakuraPass_Outcrop")
    objs = []
    y = 0.0
    for i in range(6):
        r = rng.uniform(1.6, 3.1) * (1.0 - i * 0.09)
        slab = _displaced_sphere(f"{name}_{i}", r, seed=seed + i * 13,
                                 squash=rng.uniform(0.30, 0.44),
                                 strata=0.34, roughness=0.30)
        slab.location = S.u2b_vec((rng.uniform(-1.1, 1.1), y, rng.uniform(-0.9, 0.9)))
        slab.rotation_mode = 'XYZ'
        slab.rotation_euler[2] = rng.uniform(0, TAU)
        S.assign_material(slab, stone)
        objs.append(slab)
        y += r * rng.uniform(0.30, 0.48)
    S.export_glb(objs, f"{name}.glb")
    return objs


def _plane_bar(name, p0, p1, w, z0, z1):
    """
    A flat bar lying in the sign plane (Unity XY), extruded through Z from z0 to z1.

    Sign faces are the one place flat quads are tempting, and they are a trap: a single quad is
    backface-culled from one side and ``recalc_normals`` cannot orient a non-manifold sheet. Every
    marking here is therefore a closed prism, which ``recalc_normals`` *can* fix unconditionally.
    """
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    nx, ny = -uy * w * 0.5, ux * w * 0.5

    ring = [(p0[0] - nx, p0[1] - ny), (p1[0] - nx, p1[1] - ny),
            (p1[0] + nx, p1[1] + ny), (p0[0] + nx, p0[1] + ny)]
    verts = [S.u2b(x, y, z0) for (x, y) in ring] + [S.u2b(x, y, z1) for (x, y) in ring]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4),
             (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    uvs = []
    for f in faces:
        uvs.extend([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)][:len(f)])
    obj = S.mesh_from_arrays(name, verts, faces, uvs=uvs)
    S.recalc_normals(obj)
    return obj


# ------------------------------------------------------------- hairpin chevrons

def build_chevron(name="SakuraPass_Chevron_Sign"):
    """The yellow chevron board that warns of a hairpin (concept panel 06)."""
    print(f"[sakura] building {name}...")
    objs = []
    yellow = S.pbr_material("SakuraPass_SignYellow", base_color=(0.86, 0.66, 0.10, 1.0),
                            roughness=0.55)
    black = S.pbr_material("SakuraPass_SignBlack", base_color=(0.07, 0.07, 0.08, 1.0),
                           roughness=0.60)
    grey = S.pbr_material("SakuraPass_SignPost", base_color=(0.62, 0.62, 0.60, 1.0),
                          roughness=0.55)

    top, board_h, board_w = 2.15, 0.78, 2.60
    for sgn in (-1.0, 1.0):
        post = box(f"{name}_Post_{int(sgn)}", (0.10, top, 0.10),
                   centre=(sgn * board_w * 0.32, top * 0.5, 0.0), bevel=0.01)
        S.assign_material(post, grey)
        objs.append(post)

    board = box(f"{name}_Board", (board_w, board_h, 0.07),
                centre=(0.0, top - board_h * 0.5, 0.055), bevel=0.02)
    S.assign_material(board, yellow)
    objs.append(board)

    # Three ">" marks, each a pair of bars meeting at the tip. Drawn in board-local XY with the
    # board centre at the origin, then offset onto the board face.
    cy = top - board_h * 0.5
    half = board_h * 0.30
    for i, cx in enumerate((-0.82, 0.0, 0.82)):
        tip = (cx + 0.30, cy)
        for end in ((cx - 0.30, cy + half), (cx - 0.30, cy - half)):
            bar = _plane_bar(f"{name}_Chevron_{i}_{end[1] > cy}", end, tip, 0.16, 0.09, 0.125)
            S.assign_material(bar, black)
            objs.append(bar)

    for o in objs:
        S.report(o.name, o)
    S.export_glb(objs, f"{name}.glb")
    return objs


# --------------------------------------------------------------- summit sign

def build_summit_sign(name="SakuraPass_Summit_Sign"):
    """The pass signboard of concept panel 07: a timber board on two stout posts."""
    print(f"[sakura] building {name}...")
    objs = []
    timber = S.pbr_material("SakuraPass_SignTimber", base_color=(0.30, 0.19, 0.13, 1.0),
                            roughness=0.78)
    face = S.pbr_material("SakuraPass_SignFace", base_color=(0.80, 0.74, 0.62, 1.0),
                          roughness=0.72)
    ink = S.pbr_material("SakuraPass_SignInk", base_color=(0.10, 0.09, 0.10, 1.0),
                         roughness=0.65)

    top, board_h, board_w = 2.85, 1.15, 3.40
    for sgn in (-1.0, 1.0):
        post = box(f"{name}_Post_{int(sgn)}", (0.20, top, 0.20),
                   centre=(sgn * board_w * 0.40, top * 0.5, 0.0), taper=0.86, bevel=0.02)
        S.assign_material(post, timber)
        objs.append(post)

    cy = top - board_h * 0.5 - 0.12
    panel = box(f"{name}_Panel", (board_w, board_h, 0.09), centre=(0.0, cy, 0.06), bevel=0.02)
    S.assign_material(panel, face)
    objs.append(panel)

    # Frame rails top and bottom so the board does not read as a floating slab.
    for sgn in (-1.0, 1.0):
        rail = box(f"{name}_Rail_{int(sgn)}", (board_w + 0.22, 0.13, 0.15),
                   centre=(0.0, cy + sgn * (board_h * 0.5 + 0.02), 0.06), bevel=0.02)
        S.assign_material(rail, timber)
        objs.append(rail)

    # Vertical strokes standing in for the carved "SAKURA PASS / 1,472 m" legend. Real glyphs
    # would need a text texture; at riding speed the rhythm of the columns is what reads.
    for i in range(7):
        x = -1.24 + i * 0.41
        h = 0.62 if i % 2 == 0 else 0.46
        bar = _plane_bar(f"{name}_Glyph_{i}", (x, cy - h * 0.5), (x, cy + h * 0.5),
                         0.085, 0.105, 0.135)
        S.assign_material(bar, ink)
        objs.append(bar)

    for o in objs:
        S.report(o.name, o)
    S.export_glb(objs, f"{name}.glb")
    return objs


# ---------------------------------------------------------------- banner pole

def build_banner_pole(name="SakuraPass_Banner_Pole"):
    """A nobori banner beside the shrine approach (concept panel 03)."""
    print(f"[sakura] building {name}...")
    objs = []
    red = vermilion_material()
    timber = S.pbr_material("SakuraPass_BannerPole", base_color=(0.36, 0.27, 0.19, 1.0),
                            roughness=0.80)

    height = 4.2
    pole = S.lathe(f"{name}_Pole", [(0.055, 0.0), (0.050, height), (0.0, height + 0.10)],
                   segments=10, uv_scale=(1.0, 4.0))
    S.assign_material(pole, timber)
    objs.append(pole)

    # Cross arm at the top; the banner hangs from it, clear of the pole.
    arm = box(f"{name}_Arm", (0.80, 0.055, 0.055), centre=(0.33, height - 0.06, 0.0), bevel=0.01)
    S.assign_material(arm, timber)
    objs.append(arm)

    # The banner is a thin *solid*, not a plane: a single quad would vanish from one side.
    banner = box(f"{name}_Banner", (0.62, 2.85, 0.035),
                 centre=(0.40, height - 0.06 - 1.48, 0.0), bevel=0.005, uv_scale=1.0)
    S.assign_material(banner, red)
    objs.append(banner)

    for o in objs:
        S.report(o.name, o)
    S.export_glb(objs, f"{name}.glb")
    return objs


# -------------------------------------------------------- summit overlook rail

def build_overlook_railing(name="SakuraPass_Overlook_Railing", length=6.4):
    """
    The timber viewpoint railing of concept panel 07.

    Built along local +X so Unity can yaw a run of them along the route tangent, and sized so
    consecutive copies butt together: the end posts sit half a bay in from each tip.
    """
    print(f"[sakura] building {name}...")
    objs = []
    timber = S.pbr_material("SakuraPass_RailTimber", base_color=(0.33, 0.22, 0.15, 1.0),
                            roughness=0.82)

    post_h, bays = 1.12, 4
    bay = length / bays
    for i in range(bays + 1):
        x = -length * 0.5 + i * bay
        post = box(f"{name}_Post_{i}", (0.13, post_h, 0.13),
                   centre=(x, post_h * 0.5, 0.0), taper=0.88, bevel=0.012)
        S.assign_material(post, timber)
        objs.append(post)

    # Top rail plus two thinner stretchers; the top one overhangs the end posts slightly.
    for j, (y, h, over) in enumerate(((post_h - 0.06, 0.12, 0.22),
                                      (post_h * 0.62, 0.075, 0.0),
                                      (post_h * 0.30, 0.075, 0.0))):
        rail = box(f"{name}_Rail_{j}", (length + over, h, 0.10),
                   centre=(0.0, y, 0.0), bevel=0.012)
        S.assign_material(rail, timber)
        objs.append(rail)

    for o in objs:
        S.report(o.name, o)
    S.export_glb(objs, f"{name}.glb")
    return objs


# ------------------------------------------------------------- distance marker
def build_marker(name="SakuraPass_Distance_Marker"):
    print(f"[sakura] building {name}...")
    objs = []
    post = S.lathe(f"{name}_Post",
                   [(0.075, 0.0), (0.075, 1.05), (0.068, 1.12), (0.045, 1.16), (0.0, 1.18)],
                   segments=10, uv_scale=(1.0, 2.0))
    white = S.pbr_material("SakuraPass_MarkerPost", base_color=(0.88, 0.88, 0.86, 1.0),
                           roughness=0.6)
    S.assign_material(post, white)
    objs.append(post)

    band = S.lathe(f"{name}_Band", [(0.079, 0.72), (0.079, 0.92)], segments=10)
    red = S.pbr_material("SakuraPass_MarkerBand", base_color=(0.72, 0.14, 0.12, 1.0),
                         roughness=0.5)
    S.assign_material(band, red)
    objs.append(band)

    plate = box(f"{name}_Plate", (0.30, 0.22, 0.035), centre=(0.0, 1.0, 0.07), bevel=0.01)
    S.assign_material(plate, white)
    objs.append(plate)

    S.export_glb(objs, f"{name}.glb")
    return objs


def main():
    for fn in (build_torii, build_great_torii, build_lantern, build_retaining_wall,
               build_cliff_ledge, build_marker, build_chevron, build_summit_sign,
               build_banner_pole, build_overlook_railing):
        S.reset_scene()
        fn()

    S.reset_scene()
    build_rock_cluster("SakuraPass_Rock_Cluster_A", seed=21, count=6)
    S.reset_scene()
    build_rock_cluster("SakuraPass_Rock_Cluster_B", seed=64, count=4, spread=1.3,
                       base_radius=1.35)

    print("[sakura] props build complete.")


if __name__ == "__main__":
    main()

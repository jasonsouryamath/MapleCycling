"""MINATO COAST hero lighthouse and rocky promontory (Blender 4.5 -> glTF -> Unity)."""

import math
import os
import random
import sys

import bpy
import bmesh

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sakura_lib as S
import minato_lib as M


ASSET = "Minato_Lighthouse_Hero.glb"
BLEND = "Minato_Lighthouse_Hero.blend"


def assets_dir():
    path = os.path.join(S.repo_root(), "Assets", "Environment", "MinatoCoast", "BlenderAssets")
    os.makedirs(path, exist_ok=True)
    return path


def box(name, centre, size):
    x, y, z = centre
    sx, sy, sz = size
    return M.beam(name, (x, y, z - sz * 0.5), (x, y, z + sz * 0.5),
                  sx, sy, uv_per_m=0.18)


def ring_tube(name, centre, radius, tube_radius, segments=40, sides=8):
    cx, cy, cz = centre
    verts = []
    faces = []
    uvs = []
    for i in range(segments):
        a = i / segments * math.tau
        radial = (math.cos(a), 0.0, math.sin(a))
        for j in range(sides):
            b = j / sides * math.tau
            r = radius + math.cos(b) * tube_radius
            verts.append(S.u2b(cx + math.cos(a) * r,
                               cy + math.sin(b) * tube_radius,
                               cz + math.sin(a) * r))
    for i in range(segments):
        ni = (i + 1) % segments
        for j in range(sides):
            nj = (j + 1) % sides
            a = i * sides + j
            b = ni * sides + j
            c = ni * sides + nj
            d = i * sides + nj
            faces.append((a, b, c, d))
            uvs.extend(((i / segments, j / sides), ((i + 1) / segments, j / sides),
                        ((i + 1) / segments, (j + 1) / sides),
                        (i / segments, (j + 1) / sides)))
    obj = S.mesh_from_arrays(name, verts, faces, uvs=uvs, smooth=True)
    S.recalc_normals(obj)
    return obj


def island_mesh():
    rng = random.Random(4404)
    segments = 96
    radii = (0.0, 11.0, 24.0, 39.0, 56.0, 74.0, 92.0, 112.0)
    heights = (8.4, 8.0, 6.9, 5.4, 3.8, 1.7, -0.4, -3.6)
    verts = [S.u2b(0.0, heights[0], 0.0)]
    ring_indices = []
    for ring in range(1, len(radii)):
        indices = []
        for i in range(segments):
            a = i / segments * math.tau
            wobble = 1.0 + 0.10 * math.sin(a * 5.0 + ring * 0.7) + 0.045 * math.sin(a * 13.0)
            radius = radii[ring] * wobble
            height = heights[ring] + math.sin(a * 4.0 + ring) * 0.55 + rng.uniform(-0.24, 0.24)
            indices.append(len(verts))
            verts.append(S.u2b(math.cos(a) * radius, height, math.sin(a) * radius))
        ring_indices.append(indices)

    faces = []
    uvs = []
    first = ring_indices[0]
    for i in range(segments):
        ni = (i + 1) % segments
        faces.append((0, first[i], first[ni]))
        uvs.extend(((0.5, 0.5), (0.5 + math.cos(i / segments * math.tau) * 0.07,
                                 0.5 + math.sin(i / segments * math.tau) * 0.07),
                    (0.5 + math.cos(ni / segments * math.tau) * 0.07,
                     0.5 + math.sin(ni / segments * math.tau) * 0.07)))
    for ring in range(len(ring_indices) - 1):
        inner = ring_indices[ring]
        outer = ring_indices[ring + 1]
        for i in range(segments):
            ni = (i + 1) % segments
            faces.append((inner[i], outer[i], outer[ni], inner[ni]))
            r0 = radii[ring + 1] / radii[-1] * 0.5
            r1 = radii[ring + 2] / radii[-1] * 0.5
            a0 = i / segments * math.tau
            a1 = ni / segments * math.tau
            uvs.extend(((0.5 + math.cos(a0) * r0, 0.5 + math.sin(a0) * r0),
                        (0.5 + math.cos(a0) * r1, 0.5 + math.sin(a0) * r1),
                        (0.5 + math.cos(a1) * r1, 0.5 + math.sin(a1) * r1),
                        (0.5 + math.cos(a1) * r0, 0.5 + math.sin(a1) * r0)))
    obj = S.mesh_from_arrays("Promontory_Rock_LOD0", verts, faces, uvs=uvs, smooth=True)
    S.recalc_normals(obj)
    S.shade_auto_smooth(obj, 64.0)
    return obj


def rock_blob(name, x, y, z, radius, height, seed):
    rng = random.Random(seed)
    segments = 12
    rings = 4
    verts = []
    for ring in range(rings + 1):
        t = ring / rings
        rr = radius * math.sin(t * math.pi * 0.88)
        yy = y + height * t
        for i in range(segments):
            a = i / segments * math.tau
            wobble = 0.78 + rng.random() * 0.38
            verts.append(S.u2b(x + math.cos(a) * rr * wobble, yy,
                               z + math.sin(a) * rr * wobble))
    faces = []
    for ring in range(rings):
        for i in range(segments):
            ni = (i + 1) % segments
            a = ring * segments + i
            b = ring * segments + ni
            c = (ring + 1) * segments + ni
            d = (ring + 1) * segments + i
            faces.append((a, b, c, d))
    obj = S.mesh_from_arrays(name, verts, faces, smooth=True)
    S.cap_open_boundaries(obj)
    S.recalc_normals(obj)
    S.shade_auto_smooth(obj, 58.0)
    return obj


def main():
    S.reset_scene()
    parts = {key: [] for key in ("Rock", "Body", "Band", "Roof", "Metal", "Glass", "Light", "Plant")}
    parts["Rock"].append(island_mesh())

    rng = random.Random(8404)
    for i in range(30):
        a = rng.random() * math.tau
        r = 42.0 + rng.random() * 62.0
        parts["Rock"].append(rock_blob(f"Promontory_Boulder_{i}",
                                      math.cos(a) * r, -0.8 + rng.random() * 1.6,
                                      math.sin(a) * r, 4.0 + rng.random() * 5.0,
                                      0.8 + rng.random() * 1.5, 700 + i))

    # Foundation, tapered tower, alternating signal bands, door and inset windows.
    parts["Rock"].append(box("TowerPlinth", (0.0, 8.8, 0.0), (16.0, 3.0, 16.0)))
    parts["Body"].append(M.tube("TowerBody", (0.0, 10.3, 0.0), (0.0, 36.0, 0.0),
                                7.3, segments=32, taper=0.58, uv_per_m=0.18))
    parts["Band"].append(M.tube("TowerBandLower", (0.0, 18.0, 0.0), (0.0, 21.2, 0.0),
                                6.35, segments=32, taper=0.96, uv_per_m=0.18))
    parts["Band"].append(M.tube("TowerBandUpper", (0.0, 28.2, 0.0), (0.0, 31.2, 0.0),
                                5.25, segments=32, taper=0.96, uv_per_m=0.18))
    parts["Roof"].append(box("TowerDoor", (0.0, 13.2, 6.48), (2.7, 5.0, 0.42)))
    for level, yy in enumerate((23.8, 33.0)):
        for quadrant in range(4):
            a = quadrant * math.pi * 0.5
            radius = 5.7 - level * 0.75
            parts["Glass"].append(box(f"TowerWindow_{level}_{quadrant}",
                                      (math.sin(a) * radius, yy, math.cos(a) * radius),
                                      (1.6 if quadrant % 2 == 0 else 0.36, 2.0,
                                       0.36 if quadrant % 2 == 0 else 1.6)))

    # Gallery deck and fully modelled lantern room.
    parts["Metal"].append(M.tube("GalleryDeck", (0.0, 36.0, 0.0), (0.0, 37.1, 0.0),
                                 7.1, segments=32, taper=1.0))
    parts["Glass"].append(M.tube("LanternGlass", (0.0, 37.1, 0.0), (0.0, 42.6, 0.0),
                                 4.55, segments=20, taper=0.96))
    parts["Light"].append(M.tube("LanternLight", (0.0, 38.2, 0.0), (0.0, 41.8, 0.0),
                                 1.15, segments=20, taper=1.0))
    for i in range(16):
        a = i / 16.0 * math.tau
        x = math.cos(a) * 4.65
        z = math.sin(a) * 4.65
        parts["Metal"].append(M.tube(f"LanternMullion_{i}", (x, 37.0, z), (x, 42.7, z),
                                     0.11, segments=8))
    parts["Roof"].append(M.tube("LanternRoof", (0.0, 42.6, 0.0), (0.0, 47.2, 0.0),
                                5.6, segments=32, taper=0.04))
    parts["Metal"].append(M.tube("LightningRod", (0.0, 47.1, 0.0), (0.0, 51.0, 0.0),
                                 0.11, segments=8, taper=0.55))

    parts["Metal"].append(ring_tube("GalleryRailTop", (0.0, 39.0, 0.0), 7.25, 0.10))
    parts["Metal"].append(ring_tube("GalleryRailMid", (0.0, 38.0, 0.0), 7.25, 0.075))
    for i in range(24):
        a = i / 24.0 * math.tau
        x = math.cos(a) * 7.25
        z = math.sin(a) * 7.25
        parts["Metal"].append(M.tube(f"GalleryPost_{i}", (x, 37.0, z), (x, 39.2, z),
                                     0.075, segments=8))

    # Authored low scrub masses around the plinth.
    for i in range(48):
        a = rng.random() * math.tau
        r = 18.0 + rng.random() * 72.0
        x = math.cos(a) * r
        z = math.sin(a) * r
        base = 0.2 + (1.0 - r / 100.0) * 5.0
        h = 0.8 + rng.random() * 1.8
        parts["Plant"].append(M.tube(f"ScrubStem_{i}", (x, base, z), (x, base + h, z),
                                     0.12, segments=7, taper=0.5))
        parts["Plant"].append(rock_blob(f"ScrubCrown_{i}", x, base + h * 0.62, z,
                                       0.8 + rng.random() * 1.1, 0.9 + rng.random() * 1.2,
                                       1200 + i))

    colors = {
        "Rock": (0.27, 0.31, 0.31), "Body": (0.86, 0.84, 0.74),
        "Band": (0.32, 0.08, 0.07), "Roof": (0.42, 0.08, 0.06),
        "Metal": (0.16, 0.18, 0.19), "Glass": (0.26, 0.48, 0.57),
        "Light": (1.0, 0.72, 0.28), "Plant": (0.18, 0.32, 0.16),
    }
    objects = []
    for category, source in parts.items():
        obj = M.finish(source, f"Minato_LighthouseHero_{category}_LOD0",
                       bevel=0.045 if category not in ("Rock", "Plant", "Glass") else 0.018,
                       bevel_segments=2, weld=0.0012,
                       smooth_angle=58.0 if category in ("Rock", "Plant") else 42.0)
        obj.data.materials.append(bpy.data.materials.new(f"MinatoLighthouse_{category}"))
        obj.data.materials[0].diffuse_color = (*colors[category], 1.0)
        objects.append(obj)
        if category not in ("Metal", "Glass", "Light"):
            objects.append(M.decimated_copy(obj, f"Minato_LighthouseHero_{category}_LOD1", 0.58))
            objects.append(M.decimated_copy(obj, f"Minato_LighthouseHero_{category}_LOD2", 0.24))

    for obj in objects:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()

    M.save_blend(BLEND)
    original = S.blender_assets_dir
    S.blender_assets_dir = assets_dir
    try:
        S.export_glb(objects, ASSET)
    finally:
        S.blender_assets_dir = original


if __name__ == "__main__":
    main()

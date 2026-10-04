"""
MINATO COAST - authored chapter-05 mountain landscape (Blender 4.5 -> glTF -> Unity).

The Unity terrain remains the ride/camera collision source.  This asset is a Minato-owned
visual shell and a set of rock cuts that replace the low-resolution generated terrain in the
chapter-05 camera corridor.  Every vertex is authored in Unity coordinates and passed through
sakura_lib.u2b().

    blender -b -P build_minato_hero_mountains.py
"""

import json
import math
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sakura_lib as S


ASSET_NAME = "Minato_Mountain_HeroValley.glb"
BLEND_NAME = "Minato_Mountain_HeroValley.blend"
ANCHOR_DISTANCE = 16000.0
MIN_ROUTE_DISTANCE = 13600.0
MAX_ROUTE_DISTANCE = 19010.0
GRID_STEP = 6.0
GRID_MARGIN = 1220.0


def minato_root():
    return os.path.join(S.repo_root(), "Assets", "Environment", "MinatoCoast")


def minato_assets_dir():
    path = os.path.join(minato_root(), "BlenderAssets")
    os.makedirs(path, exist_ok=True)
    return path


def load_route():
    path = os.path.join(minato_root(), "MinatoRoute.json")
    with open(path, "r", encoding="utf-8") as handle:
        data = json.load(handle)
    samples = data["samples"]
    points = np.asarray([sample["p"] for sample in samples], dtype=np.float64)
    tangents = np.asarray([sample["t"] for sample in samples], dtype=np.float64)
    sides = np.asarray([sample["s"] for sample in samples], dtype=np.float64)
    distances = np.asarray([sample["d"] for sample in samples], dtype=np.float64)
    keep = (distances >= MIN_ROUTE_DISTANCE) & (distances <= MAX_ROUTE_DISTANCE)
    points = points[keep][::2]
    tangents = tangents[keep][::2]
    sides = sides[keep][::2]
    distances = distances[keep][::2]
    return points, tangents, sides, distances


def nearest_route_fields(world_x, world_z, route_points, route_sides):
    flat_x = world_x.reshape(-1)
    flat_z = world_z.reshape(-1)
    nearest = np.empty(flat_x.shape[0], dtype=np.int32)
    nearest_distance = np.empty(flat_x.shape[0], dtype=np.float64)
    smooth_route_y = np.empty(flat_x.shape[0], dtype=np.float64)

    route_xz = route_points[:, (0, 2)]
    for start in range(0, flat_x.shape[0], 256):
        end = min(start + 256, flat_x.shape[0])
        dx = flat_x[start:end, None] - route_xz[None, :, 0]
        dz = flat_z[start:end, None] - route_xz[None, :, 1]
        distance_sq = dx * dx + dz * dz
        local = np.argmin(distance_sq, axis=1)
        nearest[start:end] = local
        nearest_distance[start:end] = np.sqrt(distance_sq[np.arange(end - start), local])
        weights = np.exp(-distance_sq / (2.0 * 360.0 * 360.0))
        smooth_route_y[start:end] = (
            np.sum(weights * route_points[None, :, 1], axis=1) /
            np.maximum(np.sum(weights, axis=1), 1.0e-9)
        )

    route_y = route_points[nearest, 1]
    delta_x = flat_x - route_points[nearest, 0]
    delta_z = flat_z - route_points[nearest, 2]
    signed_side = delta_x * route_sides[nearest, 0] + delta_z * route_sides[nearest, 2]
    return (
        nearest.reshape(world_x.shape),
        nearest_distance.reshape(world_x.shape),
        route_y.reshape(world_x.shape),
        smooth_route_y.reshape(world_x.shape),
        signed_side.reshape(world_x.shape),
    )


def smoothstep(lo, hi, value):
    t = np.clip((value - lo) / max(hi - lo, 1.0e-6), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def mountain_height(world_x, world_z, route_points, route_sides):
    nearest, distance_to_route, route_y, smooth_route_y, signed_side = nearest_route_fields(
        world_x, world_z, route_points, route_sides
    )
    shoulder = smoothstep(12.0, 76.0, distance_to_route)
    far = smoothstep(78.0, 940.0, distance_to_route)
    side_scale = np.where(signed_side >= 0.0, 1.08, 0.78)

    broad = S.fbm_2d(world_x, world_z, 0.00042, octaves=5, gain=0.52, seed=5107)
    ridged = S.ridged_2d(world_x, world_z, 0.00078, octaves=5, seed=5209)
    meso = S.ridged_2d(world_x, world_z, 0.00165, octaves=4, seed=5261)
    detail = S.fbm_2d(world_x, world_z, 0.0038, octaves=4, gain=0.46, seed=5303)
    erosion = S.ridged_2d(world_x, world_z, 0.0068, octaves=3, seed=5369)
    breakup = S.fbm_2d(world_x, world_z, 0.0105, octaves=2, gain=0.42, seed=5381)

    distance_rise = 45.0 * np.power(np.clip(distance_to_route / 190.0, 0.0, 6.0), 1.05)
    distance_rise = np.minimum(distance_rise, 310.0)
    relief = (
        (broad - 0.47) * 132.0
        + (ridged - 0.44) * 142.0
        + (meso - 0.48) * 42.0
    ) * far
    fine_relief = (detail - 0.5) * 32.0 * smoothstep(42.0, 310.0, distance_to_route)
    fine_relief += (erosion - 0.50) * 18.0 * smoothstep(34.0, 250.0, distance_to_route)
    fine_relief += (breakup - 0.50) * 7.0 * smoothstep(24.0, 160.0, distance_to_route)

    peaks = np.zeros_like(world_x)
    authored_peaks = (
        (18350.0, 11100.0, 340.0, 620.0, 430.0),
        (19020.0, 11940.0, 290.0, 520.0, 390.0),
        (20420.0, 11150.0, 310.0, 600.0, 450.0),
        (20600.0, 9820.0, 265.0, 500.0, 360.0),
        (18700.0, 9520.0, 235.0, 430.0, 330.0),
    )
    for px, pz, height, radius_x, radius_z in authored_peaks:
        radial = ((world_x - px) / radius_x) ** 2 + ((world_z - pz) / radius_z) ** 2
        peaks += height * np.exp(-1.8 * radial)

    # Keep the exact road elevation only in the immediate visual shoulder. Adjacent
    # switchbacks can be tens of metres apart but differ greatly in height; carrying the
    # nearest sample out to 165 m created Voronoi-like elevation jumps and black slab cliffs
    # where the nearest branch changed. Beyond the shoulder use the smoothly weighted route
    # field so the hillside connects the bends continuously.
    route_blend = smoothstep(10.0, 52.0, distance_to_route)
    landscape_base = route_y * (1.0 - route_blend) + smooth_route_y * route_blend
    height = landscape_base - 0.55
    height += shoulder * side_scale * distance_rise
    height += shoulder * relief + fine_relief + peaks * far

    corridor = smoothstep(8.0, 34.0, distance_to_route)
    height = (route_y - 0.65) * (1.0 - corridor) + height * corridor
    return height, nearest, distance_to_route


def terrain_weights(height, world_x, world_z):
    dz, dx = np.gradient(height, world_z[:, 0], world_x[0, :])
    slope = np.sqrt(dx * dx + dz * dz)
    rock_noise = S.fbm_2d(world_x, world_z, 0.0024, octaves=4, gain=0.5, seed=5429)
    rock = smoothstep(0.72, 1.62, slope + (rock_noise - 0.5) * 0.48)
    altitude = smoothstep(175.0, 520.0, height)
    scree = np.clip(smoothstep(0.50, 1.05, slope) * (1.0 - rock) * 0.12, 0.0, 0.12)
    soil = np.clip((1.0 - rock) * (0.07 + 0.12 * altitude), 0.0, 0.20)
    total = np.maximum(rock + scree + soil, 1.0)
    return rock / total, scree / total, soil / total


def build_grid_mesh(name, world_x, world_z, height, anchor, step_scale=1):
    x = world_x[::step_scale, ::step_scale]
    z = world_z[::step_scale, ::step_scale]
    y = height[::step_scale, ::step_scale]
    rock, soil, grass = terrain_weights(y, x, z)
    rows, cols = x.shape

    verts = []
    for row in range(rows):
        for col in range(cols):
            verts.append(
                S.u2b(
                    float(x[row, col] - anchor[0]),
                    float(y[row, col] - anchor[1]),
                    float(z[row, col] - anchor[2]),
                )
            )

    faces = []
    uvs = []
    uv2 = []
    uv3 = []

    def append_face(indices):
        faces.append(indices)
        for index in indices:
            row = index // cols
            col = index % cols
            uvs.append((float(x[row, col] * 0.018), float(z[row, col] * 0.018)))
            uv2.append((float(rock[row, col]), float(soil[row, col])))
            uv3.append((float(grass[row, col]), 0.0))

    for row in range(rows - 1):
        for col in range(cols - 1):
            a = row * cols + col
            b = a + 1
            c = a + cols + 1
            d = a + cols
            diagonal = ((row * 73856093) ^ (col * 19349663) ^ 0x5BD1E995) & 1
            if diagonal:
                append_face((a, b, d))
                append_face((b, c, d))
            else:
                append_face((a, b, c))
                append_face((a, c, d))

    obj = S.mesh_from_arrays(name, verts, faces, uvs=uvs, uv2=uv2, uv3=uv3, smooth=True)
    # Open height fields have no enclosed "outside"; Blender's recalculate-outside can choose
    # the underside for the whole sheet. Winding above is authored explicitly upward, so keep it.
    S.shade_auto_smooth(obj, 78.0)
    return obj


def route_sample_at(distance, route_points, route_tangents, route_sides, route_distances):
    index = int(np.argmin(np.abs(route_distances - distance)))
    return route_points[index], route_tangents[index], route_sides[index]


def build_rock_cut(name, d0, d1, side_sign, offset, height, anchor,
                   route_points, route_tangents, route_sides, route_distances):
    count = max(16, int((d1 - d0) / 8.0) + 1)
    distances = np.linspace(d0, d1, count)
    layers = 11
    front = np.zeros((count, layers, 3), dtype=np.float64)
    back = np.zeros((count, layers, 3), dtype=np.float64)

    for i, distance in enumerate(distances):
        point, tangent, side = route_sample_at(
            distance, route_points, route_tangents, route_sides, route_distances
        )
        side_xz = np.asarray([side[0], 0.0, side[2]], dtype=np.float64)
        side_xz /= max(np.linalg.norm(side_xz), 1.0e-6)
        lateral_wobble = (3.4 * math.sin(i * 0.53)
                          + 1.8 * math.sin(i * 1.71)
                          + 0.7 * math.sin(i * 3.17))
        base = point + side_xz * side_sign * (offset + lateral_wobble)
        local_height = height * (0.76 + 0.2 * math.sin(i * 0.43 + 0.8) +
                                 0.08 * math.sin(i * 1.73))
        for layer in range(layers):
            t = layer / (layers - 1)
            ledge = (
                3.8 * math.sin(t * math.pi * 3.0 + i * 0.47)
                + 1.7 * math.sin(t * math.pi * 7.0 + i * 1.19)
            ) * t
            front[i, layer] = base + side_xz * side_sign * ledge
            front[i, layer, 1] = point[1] - 2.0 + local_height * t
            back[i, layer] = base + side_xz * side_sign * (
                20.0 + 18.0 * t + 2.5 * math.sin(i * 0.61 + t * 4.0)
            )
            back[i, layer, 1] = (
                point[1] - 5.0
                + local_height * t * (0.90 + 0.06 * math.sin(i * 0.43))
            )

    verts = []
    for band in (front, back):
        for i in range(count):
            for layer in range(layers):
                p = band[i, layer]
                verts.append(S.u2b(p[0] - anchor[0], p[1] - anchor[1], p[2] - anchor[2]))

    faces = []
    uvs = []
    uv2 = []
    uv3 = []

    def append_face(indices, u0, v0, u1, v1):
        faces.append(indices)
        coords = ((u0, v0), (u1, v0), (u1, v1), (u0, v1))
        uvs.extend(coords)
        uv2.extend(((1.0, 0.0),) * 4)
        uv3.extend(((0.0, 0.0),) * 4)

    band_size = count * layers
    for band_offset in (0, band_size):
        for i in range(count - 1):
            for layer in range(layers - 1):
                a = band_offset + i * layers + layer
                b = band_offset + (i + 1) * layers + layer
                c = b + 1
                d = a + 1
                append_face((a, b, c, d), i * 0.8, layer * 0.8, (i + 1) * 0.8, (layer + 1) * 0.8)

    for i in range(count - 1):
        for layer in (0, layers - 1):
            a = i * layers + layer
            b = (i + 1) * layers + layer
            c = band_size + (i + 1) * layers + layer
            d = band_size + i * layers + layer
            append_face((a, b, c, d), i * 0.8, 0.0, (i + 1) * 0.8, 1.0)

    for i in (0, count - 1):
        for layer in range(layers - 1):
            a = i * layers + layer
            b = i * layers + layer + 1
            c = band_size + i * layers + layer + 1
            d = band_size + i * layers + layer
            append_face((a, b, c, d), 0.0, layer * 0.8, 1.0, (layer + 1) * 0.8)

    obj = S.mesh_from_arrays(name, verts, faces, uvs=uvs, uv2=uv2, uv3=uv3, smooth=True)
    S.recalc_normals(obj)
    S.add_bevel(obj, width=0.32, segments=2, angle_deg=34.0)
    S.shade_auto_smooth(obj, 58.0)
    S.apply_modifiers(obj)
    triangulate = obj.modifiers.new("Triangulate for glTF tangents", "TRIANGULATE")
    triangulate.quad_method = "BEAUTY"
    S.apply_modifiers(obj)
    return obj


def uphill_sign(distance, route_points, route_tangents, route_sides, route_distances):
    point, _, side = route_sample_at(
        distance, route_points, route_tangents, route_sides, route_distances
    )
    side = np.asarray([side[0], 0.0, side[2]], dtype=np.float64)
    side /= max(np.linalg.norm(side), 1.0e-6)
    probes = []
    for sign in (-1.0, 1.0):
        q = point + side * sign * 96.0
        qx = np.asarray([[q[0]]], dtype=np.float64)
        qz = np.asarray([[q[2]]], dtype=np.float64)
        qh, _, _ = mountain_height(qx, qz, route_points, route_sides)
        probes.append(float(qh[0, 0]))
    return -1.0 if probes[0] >= probes[1] else 1.0


def boulder(name, centre, radii, seed, anchor):
    rng = np.random.default_rng(seed)
    rings = 4
    segments = 10
    verts = []
    cx, cy, cz = centre
    rx, ry, rz = radii
    verts.append(S.u2b(cx - anchor[0], cy + ry * 0.92 - anchor[1], cz - anchor[2]))
    for ring in range(1, rings + 1):
        phi = ring / (rings + 1) * math.pi
        for segment in range(segments):
            theta = segment / segments * math.tau
            jitter = 0.82 + rng.random() * 0.32
            x = cx + math.sin(phi) * math.cos(theta) * rx * jitter
            y = cy + math.cos(phi) * ry * (0.88 + rng.random() * 0.16)
            z = cz + math.sin(phi) * math.sin(theta) * rz * jitter
            verts.append(S.u2b(x - anchor[0], y - anchor[1], z - anchor[2]))
    bottom = len(verts)
    verts.append(S.u2b(cx - anchor[0], cy - ry * 0.72 - anchor[1], cz - anchor[2]))

    faces = []
    for segment in range(segments):
        nxt = (segment + 1) % segments
        faces.append((0, 1 + segment, 1 + nxt))
    for ring in range(rings - 1):
        row = 1 + ring * segments
        nxt_row = row + segments
        for segment in range(segments):
            nxt = (segment + 1) % segments
            faces.append((row + segment, nxt_row + segment, nxt_row + nxt, row + nxt))
    last = 1 + (rings - 1) * segments
    for segment in range(segments):
        nxt = (segment + 1) % segments
        faces.append((last + segment, bottom, last + nxt))

    obj = S.mesh_from_arrays(name, verts, faces, smooth=True)
    S.recalc_normals(obj)
    S.shade_auto_smooth(obj, 48.0)
    return obj


def build_talus(anchor, route_points, route_tangents, route_sides, route_distances):
    rng = np.random.default_rng(5603)
    rocks = []
    for index in range(150):
        distance = float(rng.uniform(14500.0, 17850.0))
        point, _, side = route_sample_at(
            distance, route_points, route_tangents, route_sides, route_distances
        )
        sign = uphill_sign(
            distance, route_points, route_tangents, route_sides, route_distances
        )
        side = np.asarray([side[0], 0.0, side[2]], dtype=np.float64)
        side /= max(np.linalg.norm(side), 1.0e-6)
        offset = float(rng.uniform(15.0, 74.0))
        q = point + side * sign * offset
        qx = np.asarray([[q[0]]], dtype=np.float64)
        qz = np.asarray([[q[2]]], dtype=np.float64)
        qh, _, _ = mountain_height(qx, qz, route_points, route_sides)
        radius = float(rng.uniform(1.6, 5.8))
        centre = (q[0], float(qh[0, 0]) - radius * 0.18, q[2])
        rocks.append(
            boulder(
                f"Talus_{index:03d}",
                centre,
                (
                    radius * float(rng.uniform(0.8, 1.5)),
                    radius * float(rng.uniform(0.55, 1.05)),
                    radius * float(rng.uniform(0.8, 1.45)),
                ),
                5700 + index,
                anchor,
            )
        )
    return S.join(rocks, "Minato_Mountain_Talus")


def export(objs):
    original = S.blender_assets_dir
    S.blender_assets_dir = minato_assets_dir
    try:
        S.export_glb(objs, ASSET_NAME)
    finally:
        S.blender_assets_dir = original


def main():
    S.reset_scene()
    route_points, route_tangents, route_sides, route_distances = load_route()
    anchor_index = int(np.argmin(np.abs(route_distances - ANCHOR_DISTANCE)))
    anchor = route_points[anchor_index].copy()

    x_min = math.floor((route_points[:, 0].min() - GRID_MARGIN) / GRID_STEP) * GRID_STEP
    x_max = math.ceil((route_points[:, 0].max() + GRID_MARGIN) / GRID_STEP) * GRID_STEP
    z_min = math.floor((route_points[:, 2].min() - GRID_MARGIN) / GRID_STEP) * GRID_STEP
    z_max = math.ceil((route_points[:, 2].max() + GRID_MARGIN) / GRID_STEP) * GRID_STEP
    xs = np.arange(x_min, x_max + GRID_STEP * 0.5, GRID_STEP)
    zs = np.arange(z_min, z_max + GRID_STEP * 0.5, GRID_STEP)
    world_x, world_z = np.meshgrid(xs, zs)
    height, _, _ = mountain_height(world_x, world_z, route_points, route_sides)

    # Feather the rectangular shell into low foothills well outside the route corridor. Without
    # this, the chapter-04 camera sees the elevated boundary as a detached sheet in the sky.
    edge_distance = np.minimum.reduce((
        world_x - x_min,
        x_max - world_x,
        world_z - z_min,
        z_max - world_z,
    ))
    edge_blend = smoothstep(180.0, 1080.0, edge_distance)
    edge_noise = S.fbm_2d(world_x, world_z, 0.0012, octaves=4, gain=0.5, seed=5407)
    edge_base = route_points[:, 1].min() - 45.0 + (edge_noise - 0.5) * 42.0
    height = edge_base * (1.0 - edge_blend) + height * edge_blend

    objects = [
        build_grid_mesh("Minato_Mountain_HeroValley_LOD0", world_x, world_z, height, anchor, 1),
        build_grid_mesh("Minato_Mountain_HeroValley_LOD1", world_x, world_z, height, anchor, 2),
        build_grid_mesh("Minato_Mountain_HeroValley_LOD2", world_x, world_z, height, anchor, 4),
    ]

    talus = build_talus(
        anchor, route_points, route_tangents, route_sides, route_distances
    )
    if talus is not None:
        objects.append(talus)

    for obj in objects:
        obj["minato_authored_asset"] = "chapter05_mountain"
        obj["unity_anchor_x"] = float(anchor[0])
        obj["unity_anchor_y"] = float(anchor[1])
        obj["unity_anchor_z"] = float(anchor[2])

    blend_dir = os.path.join(minato_root(), "Blender")
    os.makedirs(blend_dir, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(blend_dir, BLEND_NAME), check_existing=False)
    export(objects)

    print(
        "[minato-hero-mountain] built "
        f"{len(xs)}x{len(zs)} terrain grid, 150 talus rocks, "
        f"anchor=({anchor[0]:.2f},{anchor[1]:.2f},{anchor[2]:.2f})"
    )


if __name__ == "__main__":
    main()

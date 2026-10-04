"""
MINATO COAST - authored port district (Blender 4.5 -> glTF -> Unity).

This replaces the close-camera repeated house field with one composed waterfront district:
mixed-use blocks, shopfronts, balconies, warehouses, loading bays, service yards, seawalls,
street furniture and dock machinery.  The module is local +Z forward and is staged on the
chapter-01 route frame.

    blender -b -P build_minato_hero_port.py
"""

import math
import os
import sys
import bisect
import json

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sakura_lib as S
import minato_lib as M


ASSET_NAME = "Minato_Port_HeroDistrict.glb"
BLEND_NAME = "Minato_Port_HeroDistrict.blend"

ROUTE_DISTANCE = []
ROUTE_HEIGHT = []
ROUTE_ANCHOR_DISTANCE = 900.0
ROUTE_ANCHOR_HEIGHT = 0.0


def minato_assets_dir():
    path = os.path.join(S.repo_root(), "Assets", "Environment", "MinatoCoast", "BlenderAssets")
    os.makedirs(path, exist_ok=True)
    return path


def init_route(anchor_distance):
    global ROUTE_DISTANCE, ROUTE_HEIGHT, ROUTE_ANCHOR_DISTANCE, ROUTE_ANCHOR_HEIGHT
    route_path = os.path.join(
        S.repo_root(), "Assets", "Environment", "MinatoCoast", "MinatoRoute.json"
    )
    with open(route_path, "r", encoding="utf-8") as handle:
        samples = json.load(handle)["samples"]
    ROUTE_DISTANCE = [float(sample["d"]) for sample in samples]
    ROUTE_HEIGHT = [float(sample["p"][1]) for sample in samples]
    ROUTE_ANCHOR_DISTANCE = float(anchor_distance)
    ROUTE_ANCHOR_HEIGHT = route_height_world(anchor_distance)


def route_height_world(distance):
    if not ROUTE_DISTANCE:
        return 0.0
    distance = max(ROUTE_DISTANCE[0], min(ROUTE_DISTANCE[-1], float(distance)))
    index = bisect.bisect_left(ROUTE_DISTANCE, distance)
    if index <= 0:
        return ROUTE_HEIGHT[0]
    if index >= len(ROUTE_DISTANCE):
        return ROUTE_HEIGHT[-1]
    d0, d1 = ROUTE_DISTANCE[index - 1], ROUTE_DISTANCE[index]
    t = (distance - d0) / max(d1 - d0, 1.0e-6)
    return ROUTE_HEIGHT[index - 1] * (1.0 - t) + ROUTE_HEIGHT[index] * t


def ground_y(local_z):
    return route_height_world(ROUTE_ANCHOR_DISTANCE + local_z) - ROUTE_ANCHOR_HEIGHT


def box(name, centre, size):
    x, y, z = centre
    sx, sy, sz = size
    return M.beam(name, (x, y, z - sz * 0.5), (x, y, z + sz * 0.5), sx, sy,
                  uv_per_m=0.12, smooth=False)


def gable(name, centre, width, length, rise):
    x, y, z = centre
    section = [(-width * 0.5, 0.0), (0.0, rise), (width * 0.5, 0.0)]
    return M.prism(name, section, (x, y, z - length * 0.5),
                   (x, y, z + length * 0.5), uv_per_m=0.12, smooth=False)


def mat(name, color):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.roughness = 0.72
    return material


PARTS = {
    "FacadeLight": [],
    "FacadeColor": [],
    "FacadeWarm": [],
    "FacadeSlate": [],
    "Roof": [],
    "Metal": [],
    "Glass": [],
    "Concrete": [],
    "Accent": [],
}


def add(category, obj):
    PARTS[category].append(obj)
    return obj


def sloped_strip(name, x, width, z0, z1, thickness=0.18, category="Concrete",
                 segment_length=48.0):
    count = max(1, int(math.ceil((z1 - z0) / segment_length)))
    for segment in range(count):
        a_z = z0 + (z1 - z0) * segment / count
        b_z = z0 + (z1 - z0) * (segment + 1) / count
        a_y = ground_y(a_z) + thickness * 0.5
        b_y = ground_y(b_z) + thickness * 0.5
        add(
            category,
            M.beam(
                f"{name}_{segment:02d}",
                (x, a_y, a_z),
                (x, b_y, b_z),
                width,
                thickness,
                uv_per_m=0.12,
                smooth=False,
            ),
        )


def triangulate_mesh(obj):
    modifier = obj.modifiers.new("Triangulate for glTF tangents", "TRIANGULATE")
    modifier.quad_method = "BEAUTY"
    S.apply_modifiers(obj)


def detailed_block(index, x, z, width, depth, floors, color_group, roof_kind, yaw=0.0):
    gy = ground_y(z)
    height = floors * 3.25
    body = add(color_group, box(f"Block{index}_Body", (x, gy + height * 0.5, z),
                                (width, height, depth)))
    body.rotation_euler[2] = math.radians(yaw)

    if roof_kind == "gable":
        roof = add("Roof", gable(f"Block{index}_Roof", (x, gy + height, z),
                                 width + 1.6, depth + 1.8, 2.8))
    else:
        roof = add("Roof", box(f"Block{index}_Roof", (x, gy + height + 0.35, z),
                               (width + 1.2, 0.7, depth + 1.2)))
        add("Metal", box(f"Block{index}_RoofPlant",
                         (x + width * 0.18, gy + height + 1.15, z),
                         (4.5, 1.0, 3.2)))
    roof.rotation_euler[2] = math.radians(yaw)

    # Recessed storefront, awning, door and fascia.
    street_side = -1.0 if x > 0 else 1.0
    face_x = x + street_side * (width * 0.5 + 0.08)
    add("Glass", box(f"Block{index}_Storefront", (face_x, gy + 1.35, z),
                     (0.18, 2.35, depth * 0.62)))
    add("Accent", box(f"Block{index}_Awning",
                      (face_x + street_side * 0.75, gy + 2.75, z),
                      (1.65, 0.18, depth * 0.68)))
    add("Metal", box(f"Block{index}_Sign", (face_x + street_side * 0.22, gy + 3.45,
                                            z - depth * 0.22), (0.35, 0.95, depth * 0.34)))

    # Window bays with projecting frames break the facade into occupied rooms.
    bays = max(2, int(depth / 5.0))
    for floor in range(1, floors):
        yy = gy + floor * 3.25 + 1.45
        for bay in range(bays):
            zz = z - depth * 0.42 + (bay + 0.5) * depth * 0.84 / bays
            add("Glass", box(f"Block{index}_Window_{floor}_{bay}",
                             (face_x + street_side * 0.12, yy, zz),
                             (0.22, 1.35, max(1.4, depth * 0.54 / bays))))
            if floor == 1 and bay % 2 == index % 2:
                add("Concrete", box(f"Block{index}_Balcony_{bay}",
                                    (face_x + street_side * 0.85, yy - 0.55, zz),
                                    (1.65, 0.18, max(2.2, depth * 0.68 / bays))))
                for rail in (-0.62, 0.62):
                    add("Metal", M.tube(f"Block{index}_Rail_{bay}_{rail}",
                                        (face_x + street_side * 1.35, yy - 0.35, zz + rail),
                                        (face_x + street_side * 1.35, yy + 0.42, zz + rail),
                                        0.055, segments=8))

    # Side-wall service details: gutters, downpipes and exterior stairs.
    side_z = z + depth * 0.5 + 0.10
    add("Metal", M.tube(f"Block{index}_Gutter",
                        (x - width * 0.42, gy + height - 0.3, side_z),
                        (x + width * 0.42, gy + height - 0.3, side_z), 0.10, segments=10))
    add("Metal", M.tube(f"Block{index}_Downpipe",
                        (x + width * 0.40, gy + 0.25, side_z),
                        (x + width * 0.40, gy + height - 0.3, side_z), 0.085, segments=10))


def warehouse(index, x, z, width, depth, height):
    gy = ground_y(z)
    add("FacadeColor", box(f"Warehouse{index}_Body", (x, gy + height * 0.5, z),
                           (width, height, depth)))
    roof_bays = max(2, int(width / 12.0))
    bay_width = width / roof_bays
    for bay in range(roof_bays):
        bx = x - width * 0.5 + bay_width * (bay + 0.5)
        add("Roof", gable(f"Warehouse{index}_Roof_{bay}", (bx, gy + height, z),
                          bay_width + 0.4, depth + 1.2, 2.5))

    road_side = 1.0 if x < 0 else -1.0
    face_x = x + road_side * (width * 0.5 + 0.08)
    doors = max(2, int(depth / 10.0))
    for door in range(doors):
        zz = z - depth * 0.42 + (door + 0.5) * depth * 0.84 / doors
        add("Metal", box(f"Warehouse{index}_Door_{door}", (face_x, gy + 2.4, zz),
                         (0.22, 4.4, max(4.0, depth * 0.60 / doors))))
        add("Concrete", box(f"Warehouse{index}_Dock_{door}",
                            (face_x + road_side * 2.0, gy + 0.55, zz),
                            (4.0, 1.1, max(4.4, depth * 0.66 / doors))))

    for vent in range(3):
        vx = x - width * 0.28 + vent * width * 0.28
        add("Metal", M.tube(f"Warehouse{index}_Vent_{vent}",
                            (vx, gy + height + 0.6, z), (vx, gy + height + 3.4, z),
                            0.42, segments=12, taper=0.72))


def street_furniture():
    # Continuous service streets tie the authored blocks into a district instead of leaving
    # isolated buildings on grass.
    sloped_strip("LandwardServiceStreet", 72.0, 20.0, -810.0, 1070.0, 0.14)
    sloped_strip("HarbourServiceStreet", -48.0, 18.0, -810.0, 890.0, 0.16)
    for z in (-610, -330, -40, 250, 540, 820, 1060):
        add("Concrete", box(f"CrossStreet_{z}", (96.0, ground_y(z) + 0.09, z),
                            (150.0, 0.18, 13.0)))
    for side in (-1.0, 1.0):
        for z in range(-760, 801, 44):
            x = side * 19.5
            gy = ground_y(z)
            add("Metal", M.tube(f"LampPost_{side}_{z}", (x, gy, z), (x, gy + 8.2, z),
                                0.14, segments=10, taper=0.72))
            add("Metal", M.beam(f"LampArm_{side}_{z}", (x, gy + 7.8, z),
                                (x - side * 2.1, gy + 8.2, z), 0.17, 0.17))
            add("Accent", box(f"LampHead_{side}_{z}",
                              (x - side * 2.4, gy + 8.15, z), (1.0, 0.28, 0.48)))

    # Connected low retaining walls and pocket plazas.
    for side in (-1.0, 1.0):
        for z in (-620, -260, 120, 490):
            x = side * 29.0
            add("Concrete", box(f"Retaining_{side}_{z}", (x, ground_y(z) + 0.65, z),
                                (2.2, 1.3, 140.0)))
    for z in (-560, -80, 360, 700):
        gy = ground_y(z)
        add("Concrete", box(f"PocketPlaza_{z}", (55.0, gy + 0.08, z), (52.0, 0.16, 45.0)))
        for k in range(4):
            add("Accent", box(f"MarketCanopy_{z}_{k}",
                              (40.0 + k * 9.0, gy + 3.15, z - 10.0 + (k & 1) * 17.0),
                              (7.0, 0.22, 6.0)))
            add("Metal", M.tube(f"MarketPost_{z}_{k}",
                                (40.0 + k * 9.0, gy, z - 10.0 + (k & 1) * 17.0),
                                (40.0 + k * 9.0, gy + 3.1, z - 10.0 + (k & 1) * 17.0),
                                0.07, segments=8))


def dockyard():
    # Seaward service apron, seawall, loading rails and bollards.
    sloped_strip("PortApron", -122.0, 150.0, -735.0, 815.0, 0.20)
    sloped_strip("Seawall", -198.0, 4.2, -735.0, 815.0, 3.5)
    for z in range(-700, 751, 28):
        gy = ground_y(z)
        add("Metal", M.tube(f"Bollard_{z}", (-193.0, gy, z), (-193.0, gy + 1.15, z),
                            0.28, segments=12, taper=0.78))
    for z in (-590, -190, 230, 630):
        gy = ground_y(z)
        # Detailed dock gantry rather than a single silhouette box.
        for x in (-178.0, -118.0):
            add("Metal", M.beam(f"GantryLeg_{x}_{z}", (x, gy, z), (x, gy + 22.0, z),
                                1.35, 1.35, taper=0.78))
        add("Metal", M.beam(f"GantryTop_{z}", (-182.0, gy + 21.5, z),
                            (-112.0, gy + 21.5, z),
                            1.7, 1.7))
        add("Metal", M.beam(f"GantryBoom_{z}", (-145.0, gy + 21.5, z),
                            (-218.0, gy + 18.0, z), 1.05, 1.05, taper=0.56))
        add("Accent", box(f"GantryCab_{z}", (-143.0, gy + 18.7, z),
                          (6.0, 4.8, 5.5)))
        add("Metal", M.tube(f"GantryCable_{z}", (-206.0, gy + 17.4, z),
                            (-206.0, gy + 5.0, z), 0.10, segments=8))

    # Container stacks are grouped by yard and separated by service aisles.
    colors = ("Accent", "FacadeColor", "Roof")
    for yard, base_z in enumerate((-460, -40, 390)):
        for row in range(3):
            for col in range(5):
                x = -165.0 + row * 16.0
                z = base_z + col * 17.0
                levels = 1 + ((yard + row + col) % 3)
                gy = ground_y(z)
                for level in range(levels):
                    add(colors[(yard + row + col + level) % len(colors)],
                        box(f"Container_{yard}_{row}_{col}_{level}",
                            (x, gy + 1.45 + level * 2.9, z), (13.5, 2.75, 5.8)))


def main():
    S.reset_scene()
    init_route(900.0)
    for key in PARTS:
        PARTS[key].clear()

    blocks = (
        (0, 58, -700, 31, 31, 3, "FacadeLight", "gable"),
        (1, 92, -585, 42, 34, 4, "FacadeWarm", "flat"),
        (2, 54, -430, 28, 42, 3, "FacadeSlate", "gable"),
        (3, 102, -270, 50, 36, 5, "FacadeLight", "flat"),
        (4, 58, -90, 34, 38, 4, "FacadeWarm", "gable"),
        (5, 98, 90, 46, 42, 3, "FacadeLight", "flat"),
        (6, 54, 285, 30, 44, 4, "FacadeSlate", "gable"),
        (7, 96, 455, 52, 36, 5, "FacadeLight", "flat"),
        (8, 58, 650, 38, 42, 3, "FacadeWarm", "gable"),
        (9, 112, 730, 58, 44, 4, "FacadeSlate", "flat"),
        (10, 48, 845, 36, 48, 4, "FacadeLight", "gable"),
        (11, 96, 940, 54, 52, 5, "FacadeColor", "flat"),
        (12, 52, 1060, 42, 58, 4, "FacadeWarm", "gable"),
        (13, 112, 1110, 64, 62, 5, "FacadeLight", "flat"),
        (14, 156, -540, 58, 48, 4, "FacadeSlate", "gable"),
        (15, 162, -285, 72, 58, 5, "FacadeLight", "flat"),
        (16, 152, 15, 54, 52, 3, "FacadeWarm", "gable"),
        (17, 166, 285, 78, 64, 5, "FacadeLight", "flat"),
        (18, 154, 575, 60, 54, 4, "FacadeSlate", "gable"),
        (19, 170, 860, 82, 66, 5, "FacadeWarm", "flat"),
        (20, 158, 1090, 68, 60, 4, "FacadeLight", "gable"),
        (21, -58, -650, 34, 42, 3, "FacadeWarm", "gable"),
        (22, -92, -515, 48, 38, 4, "FacadeSlate", "flat"),
        (23, -52, -350, 30, 46, 3, "FacadeLight", "gable"),
        (24, -104, -205, 52, 42, 4, "FacadeWarm", "flat"),
        (25, -56, -30, 36, 48, 3, "FacadeSlate", "gable"),
        (26, -108, 135, 58, 44, 5, "FacadeLight", "flat"),
        (27, -54, 315, 32, 46, 3, "FacadeWarm", "gable"),
        (28, -102, 480, 54, 40, 4, "FacadeSlate", "flat"),
        (29, -58, 675, 38, 50, 4, "FacadeLight", "gable"),
        (30, -112, 840, 62, 48, 5, "FacadeWarm", "flat"),
        (31, -62, 1010, 42, 52, 4, "FacadeSlate", "gable"),
        (32, 210, -650, 52, 50, 4, "FacadeWarm", "gable"),
        (33, 226, -400, 68, 56, 5, "FacadeSlate", "flat"),
        (34, 214, -105, 58, 54, 4, "FacadeLight", "gable"),
        (35, 232, 210, 72, 62, 5, "FacadeWarm", "flat"),
        (36, 218, 515, 60, 56, 4, "FacadeSlate", "gable"),
        (37, 236, 790, 78, 64, 5, "FacadeLight", "flat"),
        (38, 216, 1035, 66, 58, 4, "FacadeWarm", "gable"),
    )
    for args in blocks:
        detailed_block(*args)

    warehouse(0, -76, -610, 62, 78, 13)
    warehouse(1, -98, -410, 86, 94, 16)
    warehouse(2, -84, -120, 68, 105, 14)
    warehouse(3, -108, 180, 92, 112, 18)
    warehouse(4, -82, 510, 66, 88, 14)
    street_furniture()
    dockyard()

    colors = {
        "FacadeLight": (0.78, 0.76, 0.68),
        "FacadeColor": (0.45, 0.60, 0.63),
        "FacadeWarm": (0.68, 0.52, 0.42),
        "FacadeSlate": (0.36, 0.48, 0.58),
        "Roof": (0.19, 0.25, 0.29),
        "Metal": (0.28, 0.34, 0.37),
        "Glass": (0.14, 0.28, 0.37),
        "Concrete": (0.54, 0.55, 0.52),
        "Accent": (0.74, 0.31, 0.21),
    }

    objects = []
    for category, parts in PARTS.items():
        obj = M.finish(parts, f"Minato_PortHero_{category}_LOD0",
                       bevel=0.055 if category not in ("Glass", "Metal") else 0.025,
                       bevel_segments=2, weld=0.0012, smooth_angle=52.0)
        if obj is None:
            continue
        if category == "Concrete":
            triangulate_mesh(obj)
        obj.data.materials.append(mat(f"MinatoPort_{category}", colors[category]))
        objects.append(obj)
        if category not in ("Glass", "Metal"):
            objects.append(M.decimated_copy(obj, f"Minato_PortHero_{category}_LOD1", 0.58))
            objects.append(M.decimated_copy(obj, f"Minato_PortHero_{category}_LOD2", 0.24))

    M.save_blend(BLEND_NAME)
    original = S.blender_assets_dir
    S.blender_assets_dir = minato_assets_dir
    try:
        S.export_glb(objects, ASSET_NAME)
    finally:
        S.blender_assets_dir = original

    for obj in objects:
        M.stat(obj)


if __name__ == "__main__":
    main()

"""Authored Minato chapter-02 bridge-approach urban and marina module."""

import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sakura_lib as S
import minato_lib as M
import build_minato_hero_port as P


def main():
    S.reset_scene()
    P.init_route(2070.0)
    for key in P.PARTS:
        P.PARTS[key].clear()

    blocks = (
        (30, -44, -245, 44, 48, 4, "FacadeColor", "gable"),
        (31, -76, -105, 62, 56, 5, "FacadeWarm", "flat"),
        (32, -42, 55, 48, 60, 4, "FacadeSlate", "gable"),
        (33, -82, 205, 70, 68, 5, "FacadeLight", "flat"),
        (34, 46, -205, 48, 54, 3, "FacadeLight", "gable"),
        (35, 82, -45, 66, 64, 4, "FacadeWarm", "flat"),
        (36, 44, 125, 52, 60, 4, "FacadeSlate", "gable"),
        (37, -48, 350, 52, 66, 5, "FacadeWarm", "flat"),
        (38, 52, 325, 58, 64, 4, "FacadeLight", "gable"),
        (39, -96, 465, 74, 70, 5, "FacadeLight", "flat"),
        (40, 92, 455, 72, 68, 5, "FacadeSlate", "flat"),
        (41, -138, -285, 48, 54, 4, "FacadeWarm", "gable"),
        (42, -152, -155, 62, 58, 5, "FacadeSlate", "flat"),
        (43, -136, 15, 52, 62, 4, "FacadeLight", "gable"),
        (44, -158, 155, 68, 64, 5, "FacadeWarm", "flat"),
        (45, -142, 315, 56, 60, 4, "FacadeSlate", "gable"),
        (46, 142, -285, 52, 58, 4, "FacadeSlate", "gable"),
        (47, 158, -135, 70, 62, 5, "FacadeWarm", "flat"),
        (48, 144, 35, 56, 60, 4, "FacadeLight", "gable"),
        (49, 162, 205, 72, 66, 5, "FacadeSlate", "flat"),
        (50, 146, 365, 58, 62, 4, "FacadeWarm", "gable"),
        (51, -44, -345, 42, 48, 3, "FacadeLight", "gable"),
        (52, 48, -355, 44, 50, 3, "FacadeWarm", "gable"),
        (53, -46, 485, 48, 54, 4, "FacadeSlate", "flat"),
        (54, 48, 510, 46, 52, 4, "FacadeLight", "gable"),
    )
    for args in blocks:
        P.detailed_block(*args)

    P.warehouse(20, 132, -220, 66, 82, 14)
    P.warehouse(21, 138, 85, 82, 94, 17)
    P.warehouse(22, 136, 395, 76, 88, 16)

    # Connected civic/industrial ground plane. The 28 m centre gap leaves the authored route
    # and its separate collision surface untouched while eliminating the rejected grass field.
    P.sloped_strip("ApproachTownApronWest", -93.5, 173.0, -290.0, 500.0, 0.16)
    P.sloped_strip("ApproachTownApronEast", 95.5, 177.0, -290.0, 500.0, 0.18)
    P.sloped_strip("ApproachPromenade", 16.5, 19.0, -290.0, 500.0, 0.20)
    P.sloped_strip("ApproachSeawall", 184.0, 4.0, -290.0, 500.0, 4.0)
    for z in (-250, -105, 55, 205, 350, 465):
        gy = P.ground_y(z)
        P.add("Concrete", P.box(f"ApproachSideStreetWest_{z}", (-92.0, gy + 0.13, z),
                                (156.0, 0.14, 12.0)))
        P.add("Concrete", P.box(f"ApproachSideStreetEast_{z}", (96.0, gy + 0.14, z),
                                (164.0, 0.16, 12.0)))

    terminal_y = P.ground_y(285.0)
    P.add("FacadeLight", P.box("FerryTerminal", (128.0, terminal_y + 5.0, 285.0),
                               (76.0, 10.0, 54.0)))
    P.add("Roof", P.box("FerryTerminalRoof", (128.0, terminal_y + 10.6, 285.0),
                        (80.0, 1.2, 58.0)))
    P.add("Glass", P.box("FerryTerminalGlass", (89.8, terminal_y + 5.0, 285.0),
                         (0.35, 6.8, 41.0)))
    for z in range(-270, 491, 32):
        gy = P.ground_y(z)
        P.add("Metal", M.tube(f"ApproachBollard_{z}", (177.0, gy, z),
                              (177.0, gy + 1.2, z), 0.26, segments=12, taper=0.78))
    for z in (-180, 50, 285, 455):
        gy = P.ground_y(z)
        for x in (120.0, 165.0):
            P.add("Metal", M.beam(f"ApproachGantryLeg_{x}_{z}", (x, gy, z),
                                  (x, gy + 16.0, z), 1.0, 1.0, taper=0.82))
        P.add("Metal", M.beam(f"ApproachGantryTop_{z}", (116.0, gy + 15.8, z),
                              (169.0, gy + 15.8, z), 1.25, 1.25))
        P.add("Accent", P.box(f"ApproachGantryCab_{z}", (144.0, gy + 13.0, z),
                              (5.2, 4.0, 4.8)))

    # Marina fingers, utility frames and occupied container yards make the seaward foreground
    # unmistakably maritime instead of a row of generic buildings.
    for z in (-120, 20, 160, 420):
        gy = P.ground_y(z)
        P.add("Concrete", P.box(f"MarinaFinger_{z}", (212.0, gy + 0.25, z),
                                (62.0, 0.50, 5.0)))
        for x in (186.0, 236.0):
            P.add("Metal", M.tube(f"MarinaPile_{x}_{z}", (x, gy - 1.5, z),
                                  (x, gy + 3.5, z), 0.38, segments=12, taper=0.92))
    for yard, base_z in enumerate((-210, 80, 365)):
        for row in range(2):
            for col in range(4):
                levels = 1 + ((yard + row + col) % 2)
                z = base_z + col * 16.0
                gy = P.ground_y(z)
                for level in range(levels):
                    category = ("Accent", "FacadeColor", "Roof")[(yard + row + col + level) % 3]
                    P.add(category, P.box(f"ApproachContainer_{yard}_{row}_{col}_{level}",
                                          (112.0 + row * 15.0,
                                           gy + 1.45 + level * 2.9,
                                           z),
                                          (13.0, 2.75, 5.6)))

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
    for category, parts in P.PARTS.items():
        obj = M.finish(parts, f"Minato_PortApproach_{category}_LOD0",
                       bevel=0.055 if category not in ("Glass", "Metal") else 0.025,
                       bevel_segments=2, weld=0.0012, smooth_angle=52.0)
        if obj is None:
            continue
        if category == "Concrete":
            P.triangulate_mesh(obj)
        obj.data.materials.append(P.mat(f"MinatoPortApproach_{category}", colors[category]))
        objects.append(obj)
        if category not in ("Glass", "Metal"):
            objects.append(M.decimated_copy(obj, f"Minato_PortApproach_{category}_LOD1", 0.58))
            objects.append(M.decimated_copy(obj, f"Minato_PortApproach_{category}_LOD2", 0.24))

    M.save_blend("Minato_Port_BridgeApproach.blend")
    original = S.blender_assets_dir
    S.blender_assets_dir = P.minato_assets_dir
    try:
        S.export_glb(objects, "Minato_Port_BridgeApproach.glb")
    finally:
        S.blender_assets_dir = original


if __name__ == "__main__":
    main()

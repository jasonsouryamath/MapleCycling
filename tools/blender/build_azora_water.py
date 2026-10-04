"""Build the Azora Highlands WP-B water hero assets.

The models are authored in Unity coordinates and converted through sakura_lib.u2b by
minato_lib's primitives.  Materials are deliberately named by role; the editor staging pass
replaces them with the project's CelLit materials.

Run from the repository root:
  tools/blender-4.5.10-windows-x64/blender.exe -b --factory-startup \
      -P tools/blender/build_azora_water.py
"""

import math
import os
import random
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402

OUT = os.path.join(S.repo_root(), "Assets", "Environment", "AzoraHighlands", "Models")


PALETTE = {
    "AzoraWater_Stone": (0.43, 0.46, 0.49, 1.0),
    "AzoraWater_StoneDark": (0.25, 0.29, 0.31, 1.0),
    "AzoraWater_Timber": (0.24, 0.16, 0.10, 1.0),
    "AzoraWater_Steel": (0.16, 0.19, 0.22, 1.0),
    "AzoraWater_Roof": (0.20, 0.16, 0.14, 1.0),
    "AzoraWater_Snow": (0.95, 0.98, 1.00, 1.0),
    "AzoraWater_Ice": (0.50, 0.76, 0.88, 0.82),
    "AzoraWater_Flow": (0.12, 0.47, 0.58, 0.85),
    "AzoraWater_WindowWarm": (1.00, 0.57, 0.20, 1.0),
    "AzoraWater_Wall": (0.78, 0.78, 0.72, 1.0),
}


def mat(name):
    found = bpy.data.materials.get(name)
    if found:
        return found
    col = PALETTE[name]
    return S.pbr_material(name, base_color=col, roughness=0.82 if "Stone" in name else 0.55,
                          metallic=0.08 if "Ice" in name else 0.0)


def finish(parts, name, role, bevel=0.045, smooth=38.0, bevel_segments=2):
    obj = M.finish(parts, name, bevel=bevel, bevel_segments=bevel_segments,
                   weld=0.001, smooth_angle=smooth)
    S.assign_material(obj, mat(role))
    return obj


def export(objects, filename):
    old = S.blender_assets_dir
    S.blender_assets_dir = lambda: OUT
    try:
        S.export_glb(objects, filename)
    finally:
        S.blender_assets_dir = old


def beam_between(name, a, b, width, depth, role_parts):
    role_parts.append(M.beam(name, a, b, width, depth, up_hint=(0, 1, 0), uv_per_m=0.26))


def arch_bridge(filename, name, length, arches, deck_y, pier_bottom, width,
                snow=True, low=False, rail=False, crown_drop=3.1):
    """True open arch bridge: piers + short voussoir blocks, not a painted arch.
    crown_drop = how far below the deck the arch crown sits; the spandrel band above it is
    0.3 m deeper, so a low bridge over water can keep its openings above the waterline."""
    stone, dark, snow_parts, steel, sleepers = [], [], [], [], []
    span = length / arches
    half = length * 0.5
    spring_y = pier_bottom + (deck_y - pier_bottom) * 0.43
    crown_y = deck_y - crown_drop
    pier_w = max(2.2, span * 0.105)

    # Piers carry visible cutwaters so the silhouette reads as masonry infrastructure.
    for i in range(arches + 1):
        z = -half + i * span
        h = deck_y - 2.2 - pier_bottom
        stone.append(M.box(f"Pier_{i}", (0, pier_bottom + h * 0.5, z),
                           (width * 0.80, h, pier_w), uv_per_m=0.20))
        if not low:
            for x in (-width * 0.45, width * 0.45):
                dark.append(M.prism(f"Cutwater_{i}_{x:+.0f}",
                                    [(-pier_w * 0.52, 0), (pier_w * 0.52, 0),
                                     (pier_w * 0.35, 4.8), (-pier_w * 0.35, 4.8)],
                                    (x, pier_bottom, z - pier_w * 0.85),
                                    (x, pier_bottom, z + pier_w * 0.85), uv_per_m=0.22))

    # Each opening is genuinely empty below the arch ring.
    for a in range(arches):
        cz = -half + (a + 0.5) * span
        radius = span * 0.5 - pier_w * 0.42
        segments = 7 if low else 13
        for k in range(segments):
            t0 = math.pi * (k / segments)
            t1 = math.pi * ((k + 1) / segments)
            p0 = (0, spring_y + math.sin(t0) * (crown_y - spring_y), cz - math.cos(t0) * radius)
            p1 = (0, spring_y + math.sin(t1) * (crown_y - spring_y), cz - math.cos(t1) * radius)
            beam_between(f"Voussoir_{a}_{k}", p0, p1, width, 2.2, stone)
        # Spandrel above the crown and compact shoulders above the haunches.
        band = crown_drop + 0.3
        stone.append(M.box(f"Spandrel_{a}", (0, deck_y - band * 0.5, cz),
                           (width, band, span - pier_w * 0.95), uv_per_m=0.20))

    stone.append(M.box("Deck", (0, deck_y + 0.15, 0), (width + 2.1, 0.85, length + 1.6), 0.20))
    for x in (-width * 0.5 - 0.55, width * 0.5 + 0.55):
        stone.append(M.box(f"Parapet_{x:+.0f}", (x, deck_y + 1.3, 0),
                           (0.72, 2.0, length + 1.2), 0.22))
        if snow:
            snow_parts.append(M.box(f"ParapetSnow_{x:+.0f}", (x, deck_y + 2.36, 0),
                                    (0.92, 0.18, length + 1.35), 0.35))
    if snow:
        snow_parts.append(M.box("DeckSnowBanksL", (-width * 0.5 + 0.25, deck_y + 0.68, 0),
                                (0.80, 0.34, length), 0.35))
        snow_parts.append(M.box("DeckSnowBanksR", (width * 0.5 - 0.25, deck_y + 0.68, 0),
                                (0.80, 0.34, length), 0.35))

    if rail:
        for x in (-0.78, 0.78):
            steel.append(M.box(f"Rail_{x:+.2f}", (x, deck_y + 0.84, 0),
                               (0.12, 0.15, length - 1.0), 0.55))
        spacing = 6.0 if low else 2.4
        z = -length * 0.5 + 1.2
        while z < length * 0.5 - 1.2:
            sleepers.append(M.box(f"Sleeper_{z:+.1f}", (0, deck_y + 0.69, z),
                                  (3.8, 0.18, 0.34), 0.45))
            z += spacing

    objects = [finish(stone, name + "_Stone", "AzoraWater_Stone", 0.035 if low else 0.055,
                      bevel_segments=1 if low else 2)]
    if dark:
        objects.append(finish(dark, name + "_Cutwater", "AzoraWater_StoneDark", 0.035))
    if snow_parts:
        objects.append(finish(snow_parts, name + "_Snow", "AzoraWater_Snow", 0.025 if low else 0.035,
                              bevel_segments=1 if low else 2))
    if steel:
        objects.append(finish(steel, name + "_SteelRail", "AzoraWater_Steel", 0.012,
                              bevel_segments=1))
    if sleepers:
        objects.append(finish(sleepers, name + "_Sleepers", "AzoraWater_Timber", 0.018,
                              bevel_segments=1))
    export(objects, filename)
    return objects


def gable_roof(name, centre, width, length, eave_y, ridge_y, role):
    x, _, z = centre
    section = [(-width * 0.5, 0), (0, ridge_y - eave_y), (width * 0.5, 0)]
    return M.prism(name, section, (x, eave_y, z - length * 0.5),
                   (x, eave_y, z + length * 0.5), up_hint=(0, 1, 0), uv_per_m=0.24)


def build_island_town():
    stone, wall, timber, roof, snow_parts, windows = [], [], [], [], [], []
    houses = [(-24, -18, 13, 10, 0), (19, -21, 15, 11, 180),
              (-27, 17, 14, 10, 10), (19, 19, 13, 9, -12), (0, 31, 12, 9, 3)]
    for i, (x, z, w, l, yaw) in enumerate(houses):
        # The composition is rotationally varied by placement; fine facade detail gives scale.
        base = 0.4 + (i % 2) * 0.25
        stone.append(M.box(f"House{i}_Plinth", (x, base + 0.7, z), (w + 0.8, 1.4, l + 0.8), 0.25))
        wall.append(M.box(f"House{i}_Walls", (x, base + 4.0, z), (w, 6.5, l), 0.28))
        roof.append(gable_roof(f"House{i}_Roof", (x, 0, z), w + 2.0, l + 2.0,
                               base + 7.1, base + 10.8, "roof"))
        snow_parts.append(gable_roof(f"House{i}_RoofSnow", (x, 0, z), w + 2.25, l + 2.25,
                                     base + 7.24, base + 10.98, "snow"))
        # balconies, deep eaves and warm window cards on both long elevations
        timber.append(M.box(f"House{i}_Balcony", (x, base + 5.2, z - l * 0.52),
                            (w * 0.72, 0.28, 1.45), 0.32))
        for face in (-1, 1):
            for wx in (-w * 0.28, w * 0.28):
                for wy in (base + 3.0, base + 5.7):
                    windows.append(M.box(f"House{i}_Window_{face}_{wx}_{wy}",
                                         (x + wx, wy, z + face * l * 0.506), (2.1, 1.75, 0.10), 0.5))
        for cx in (-w * 0.46, w * 0.46):
            timber.append(M.box(f"House{i}_Corner_{cx}", (x + cx, base + 4.0, z - l * 0.51),
                                (0.28, 6.4, 0.22), 0.4))
        # a stone chimney through the snow slab, with its own snow cap
        chx, chz = x + w * (0.22 if i % 2 else -0.22), z + l * 0.18
        stone.append(M.box(f"House{i}_Chimney", (chx, base + 10.2, chz), (1.0, 3.2, 1.0), 0.3))
        snow_parts.append(M.box(f"House{i}_ChimneySnow", (chx, base + 11.9, chz), (1.25, 0.28, 1.25), 0.4))

    # Church: readable spire and lit lancet windows above the clustered roofs.
    stone.append(M.box("ChurchNave", (0, 3.2, -1), (12, 6.4, 23), 0.24))
    roof.append(gable_roof("ChurchNaveRoof", (0, 0, 0.5), 13.6, 21.5, 6.4, 11.4, "roof"))
    snow_parts.append(gable_roof("ChurchNaveRoofSnow", (0, 0, 0.5), 13.9, 21.8, 6.54, 11.58, "snow"))
    for zz in (-6.0, 0.0, 6.0):
        for xx in (-6.05, 6.05):
            windows.append(M.box(f"ChurchNaveWindow_{xx}_{zz}", (xx, 3.6, zz), (0.12, 2.6, 1.3), 0.5))
    wall.append(M.box("ChurchTower", (0, 8.2, -13.5), (8.2, 16.4, 8.2), 0.25))
    roof.append(M.tube("ChurchSpire", (0, 16.4, -13.5), (0, 28.5, -13.5),
                       5.5, segments=8, taper=0.02))
    snow_parts.append(M.tube("ChurchSpireSnow", (0, 16.55, -13.5), (0, 28.75, -13.5),
                             5.67, segments=8, taper=0.02))
    for y in (4.5, 8.2, 12.0):
        windows.append(M.box(f"ChurchWindow_{y}", (0, y, -17.64), (2.0, 2.7, 0.12), 0.5))
    timber.append(M.tube("CrossPost", (0, 28.2, -13.5), (0, 31.3, -13.5), 0.12, segments=8))
    timber.append(M.beam("CrossArm", (-1.25, 30.1, -13.5), (1.25, 30.1, -13.5), 0.16, 0.16))

    objects = [
        finish(stone, "Azora_IslandTown_Stone", "AzoraWater_Stone", 0.045),
        finish(wall, "Azora_IslandTown_Wall", "AzoraWater_Wall", 0.05),
        finish(timber, "Azora_IslandTown_Timber", "AzoraWater_Timber", 0.035),
        finish(roof, "Azora_IslandTown_Roof", "AzoraWater_Roof", 0.035),
        finish(snow_parts, "Azora_IslandTown_Snow", "AzoraWater_Snow", 0.045),
        finish(windows, "Azora_IslandTown_WindowWarm", "AzoraWater_WindowWarm", 0.012),
    ]
    export(objects, "Azora_Island_Town.glb")


def low_poly_boulder(name, seed, scale):
    rng = random.Random(seed)
    verts, faces = [], []
    rings, seg = 5, 10
    for r in range(rings + 1):
        phi = math.pi * r / rings
        for s in range(seg):
            th = math.tau * s / seg
            jitter = 0.83 + rng.random() * 0.26
            x = math.sin(phi) * math.cos(th) * scale[0] * jitter
            y = (math.cos(phi) * 0.5 + 0.5) * scale[1] * (0.91 + rng.random() * 0.13)
            z = math.sin(phi) * math.sin(th) * scale[2] * jitter
            verts.append(S.u2b(x, y, z))
    for r in range(rings):
        for s in range(seg):
            n = (s + 1) % seg
            a, b = r * seg + s, r * seg + n
            c, d = (r + 1) * seg + n, (r + 1) * seg + s
            faces.append((a, b, c, d))
    rock = S.mesh_from_arrays(name + "_Stone", verts, faces, smooth=False)
    S.assign_material(rock, mat("AzoraWater_StoneDark"))
    # A flattened, offset cap reads as accumulated snow instead of a white recolour.
    snow = M.tube(name + "_SnowCap", (0, scale[1] * 0.70, 0), (0, scale[1] * 0.96, 0),
                  max(scale[0], scale[2]) * 0.74, segments=10, taper=0.62)
    S.assign_material(snow, mat("AzoraWater_Snow"))
    export([rock, snow], name + ".glb")


def build_waterfall():
    flow, ice, rock = [], [], []
    # Folded ribbons leave dark rock gaps and catch light on alternating planes. The old
    # three full-height boxes read as a single flat, pale billboard from the road.
    def ribbon(name, cx, width, top, low, phase, depth):
        verts, faces = [], []
        for j in range(13):
            t = j / 12.0
            y = top + (low - top) * t
            sway = 0.55 * math.sin(t * 10.0 + phase) + 0.22 * math.sin(t * 23.0 + phase)
            half = width * (0.45 + 0.07 * math.sin(t * 14.0 + phase))
            z = depth + 0.55 * math.sin(t * 8.0 + phase)
            verts.extend((S.u2b(cx + sway - half, y, z + 0.25),
                          S.u2b(cx + sway, y, z - 0.55),
                          S.u2b(cx + sway + half, y, z + 0.15)))
            if j:
                a = (j - 1) * 3
                faces.extend(((a, a + 1, a + 4, a + 3),
                              (a + 1, a + 2, a + 5, a + 4)))
        return S.mesh_from_arrays(name, verts, faces, smooth=False)

    for i, spec in enumerate(((-15, 5.5, 55, 4, 0.2, -0.55),
                              (-8.5, 6.8, 58, 1, 1.4, -0.8),
                              (-1, 6.2, 57, 0, 2.6, -0.5),
                              (6.5, 7.0, 56, 2, 3.8, -0.75),
                              (14.5, 5.8, 53, 5, 5.0, -0.45))):
        flow.append(ribbon(f"CascadeFold_{i}", *spec))
    # Narrow frozen runnels sit in front of the moving turquoise water; the icicles have
    # staggered lengths so the lower silhouette is not a horizontal cut-off.
    for i, spec in enumerate(((-17, 1.2, 55, 26, 0.4, -1.05),
                              (-11, 1.7, 58, 12, 1.7, -1.35),
                              (-3, 1.3, 57, 32, 2.9, -1.1),
                              (4, 1.7, 56, 7, 4.0, -1.25),
                              (12, 1.5, 54, 20, 5.4, -1.1),
                              (18, 1.1, 52, 29, 6.3, -0.9))):
        ice.append(ribbon(f"FrozenRunnel_{i}", *spec))
    for i, (x, h, rad) in enumerate(((-18, 22, 0.95), (-13, 36, 1.15), (-5, 18, 0.7),
                                      (2, 31, 1.1), (9, 43, 1.3), (16, 25, 0.85))):
        ice.append(M.tube(f"Icicle_{i}", (x, 55, -1.55), (x, 55 - h, -1.6),
                          rad, segments=8, taper=0.06))
    rock.append(M.box("FallLip", (0, 58.4, 1.2), (41, 3.6, 7.5), 0.18))
    rock.append(M.box("FallFoot", (0, 0.5, 1.4), (44, 3.0, 8.5), 0.18))
    # The cliff the fall drops off: stepped, jittered rock slabs behind the sheets (+z is into
    # the ravine head wall) and two lower wings, so the fall never stands free in front of a
    # smooth slope. The mass runs 40 m back so its top meets the natural ground above the wall.
    rng = random.Random(4242)
    for k in range(8):
        y0 = k * 7.6
        x = -27.0 + k * 0.65
        while x < 27.0 - k * 0.65:
            bw = rng.uniform(7.0, 13.0)
            front = 1.4 + k * 0.85 + rng.uniform(-1.6, 1.6)
            bh = rng.uniform(7.8, 10.6)
            rock.append(M.box(f"Cliff_{k}_{x:+.0f}", (x + bw * 0.5, y0 + bh * 0.5, front + 20.0),
                              (bw + 0.6, bh, 40.0), 0.16))
            x += bw
    for sx in (-1, 1):
        for k in range(4):
            h = 44.0 - k * 9.0
            rock.append(M.box(f"CliffWing_{sx}_{k}", (sx * (29.0 + k * 5.5), h * 0.5 - 1.0, 12.0 + k * 3.0),
                              (8.0 + rng.uniform(0, 2), h, 26.0 - k * 2.0), 0.16))
    objects = [finish(flow, "Azora_Waterfall_Flow", "AzoraWater_Flow", 0.0),
               finish(ice, "Azora_Waterfall_Ice", "AzoraWater_Ice", 0.0, 50.0),
               finish(rock, "Azora_Waterfall_Rock", "AzoraWater_StoneDark", 0.08)]
    export(objects, "Azora_Waterfall_IceCurtain.glb")


def main():
    S.reset_scene()
    os.makedirs(OUT, exist_ok=True)
    if "--fall-only" in sys.argv:
        build_waterfall()
        print("[azora-water] built frozen waterfall only in", OUT)
        return
    arch_bridge("Azora_Viaduct_StoneRail_LOD0.glb", "Azora_Viaduct", 182.0, 6,
                0.0, -43.0, 10.5, True, rail=True)
    arch_bridge("Azora_Viaduct_StoneRail_LOD1.glb", "Azora_Viaduct_LOD1", 182.0, 6,
                0.0, -43.0, 10.5, True, low=True, rail=True)
    arch_bridge("Azora_Island_Bridge.glb", "Azora_IslandBridge", 104.0, 4,
                0.0, -9.0, 7.0, True, crown_drop=1.6)
    build_island_town()
    low_poly_boulder("Azora_Water_Boulder_A", 1701, (4.7, 3.9, 3.7))
    low_poly_boulder("Azora_Water_Boulder_B", 1702, (3.3, 5.0, 4.4))
    build_waterfall()
    print("[azora-water] built WP-B assets in", OUT)


if __name__ == "__main__":
    main()

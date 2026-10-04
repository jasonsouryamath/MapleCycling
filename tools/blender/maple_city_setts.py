"""
MAPLE CITY - Old Town granite setts (cobble) texture generator.

WHY THIS EXISTS
---------------
The Old Town climb is the one stretch of Maple City that is NOT asphalt: the design doc calls
for worn granite setts, and the surface change is what makes the ramps read as the oldest part
of the city rather than just a steeper piece of the same boulevard.

The project had no paving texture, so the first build borrowed Sakura_Rock_Albedo.png - a
MOSSY CLIFF texture. It compiled, it tiled, and it rendered the historic quarter's high street
as a green riverbed. This script replaces that borrow with a purpose-built, seamlessly tiling
setts albedo + normal + roughness set.

Output (2048x2048, tiling):
    Assets/Environment/MapleCity/Textures/MapleCity_Setts_Albedo.png
    Assets/Environment/MapleCity/Textures/MapleCity_Setts_Normal.png
    Assets/Environment/MapleCity/Textures/MapleCity_Setts_Rough.png

All tuning below is PROVISIONAL and named.
"""

import os
import numpy as np
from PIL import Image

# ----------------------------------------------------------------- provisional tuning
RES = 2048                 # texture resolution
TILE_METRES = 4.0          # the material maps this texture across 4 m of road
SETT_W_M = 0.18            # a real granite sett is ~180 x 120 mm
SETT_H_M = 0.12
JOINT_M = 0.014            # mortar joint width
ROW_STAGGER = 0.5          # half-sett offset per row, the classic running bond
SEED = 20260914

# Weathered grey granite, slightly warm so it sits with the city's golden-hour grade.
STONE_BASE = np.array([0.470, 0.452, 0.432])
STONE_VARIANCE = 0.07     # per-sett lightness spread - this is what stops it reading as a grid
JOINT_COLOUR = np.array([0.175, 0.165, 0.155])
GRAIN_STRENGTH = 0.055     # fine speckle within each sett
POLISH_STRENGTH = 0.10     # centre-of-road wear: setts are polished smooth by a century of traffic
DOME_LIGHT = 0.70          # baked per-sett dome shading (CelLit has no normal input)
DOME_OCCLUSION = 0.50      # darkening as a sett rolls down into its joint
STONE_HUES = [             # per-sett multipliers on STONE_BASE
    (1.00, 1.00, 1.00),
    (1.03, 1.00, 0.96),    # warm
    (0.97, 0.99, 1.02),    # blue-grey
    (1.04, 0.99, 0.94),    # rust-flecked
    (0.98, 1.00, 0.99),
]

OUT_DIR = os.path.join(
    # this file lives at <repo>/tools/blender/, so three levels up is the repo root
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "Environment", "MapleCity", "Textures")


def build():
    rng = np.random.default_rng(SEED)

    # Work in metres across the tile so the sett size is physically meaningful, then rasterise.
    px_per_m = RES / TILE_METRES
    sett_w = SETT_W_M * px_per_m
    sett_h = SETT_H_M * px_per_m
    joint = JOINT_M * px_per_m

    # Force a WHOLE number of setts across the tile in both axes, otherwise the tile seam cuts a
    # sett in half and every repeat shows a visible ruled line.
    cols = max(1, int(round(RES / sett_w)))
    rows = max(1, int(round(RES / sett_h)))
    sett_w = RES / cols
    sett_h = RES / rows

    yy, xx = np.mgrid[0:RES, 0:RES].astype(np.float64)

    row_idx = np.floor(yy / sett_h).astype(np.int64)
    # Running bond: every other row shifts by half a sett. Modulo RES keeps it tiling.
    shift = (row_idx % 2) * (ROW_STAGGER * sett_w)
    xs = np.mod(xx + shift, RES)
    col_idx = np.floor(xs / sett_w).astype(np.int64)

    # Local coordinates inside each sett, 0..1.
    fx = (xs / sett_w) - col_idx
    fy = (yy / sett_h) - row_idx

    # Per-sett random lightness. Indexing a random table by (row, col) keeps it stable and,
    # because row_idx/col_idx wrap with the tile, seamless.
    tint = rng.normal(0.0, STONE_VARIANCE, size=(rows, cols))
    # A few setts are noticeably darker - replaced stones, wet patches, old repairs.
    tint += (rng.random((rows, cols)) < 0.06) * rng.normal(-0.09, 0.03, size=(rows, cols))
    sett_tint = tint[row_idx % rows, col_idx % cols]

    # Slight per-sett rotation of the "grain" is faked by jittering each sett's edges a little,
    # so the joints are not laser-straight.
    jitter = rng.normal(0.0, 0.06, size=(rows, cols, 2))
    jx = jitter[row_idx % rows, col_idx % cols, 0]
    jy = jitter[row_idx % rows, col_idx % cols, 1]

    # Distance (in sett-local units) to the nearest edge -> the mortar joint mask.
    ex = np.minimum(fx, 1.0 - fx) + jx * 0.04
    ey = np.minimum(fy, 1.0 - fy) + jy * 0.04
    edge = np.minimum(ex * sett_w, ey * sett_h)          # in pixels

    # Smooth the joint so it antialiases instead of stair-stepping.
    joint_mask = np.clip((edge - joint * 0.5) / max(1.0, joint * 0.6), 0.0, 1.0)

    # Fine granite speckle.
    grain = rng.normal(0.0, 1.0, size=(RES, RES))
    grain = (grain + np.roll(grain, 1, 0) + np.roll(grain, 1, 1)) / 3.0
    grain *= GRAIN_STRENGTH

    # Centre-of-tile polish band: the tile is applied along the road, so a soft vertical band of
    # slightly lighter, smoother stone reads as the worn wheel track.
    polish = np.exp(-((xx / RES - 0.5) ** 2) / (2 * 0.19 ** 2)) * POLISH_STRENGTH

    # BAKED RELIEF (2026-09-25 claude): MapleRideCelLit reads no normal map, so the first
    # render showed a flat printed brick grid. Each sett now carries its own dome lighting
    # (lit from the upper-left in tile space, which is roughly the sun side along most of the
    # Old Town climb) and a soft occlusion falloff into the joint.
    edge_n = np.clip(edge / max(1.0, 0.22 * min(sett_w, sett_h)), 0.0, 1.0)
    occl = 1.0 - (1.0 - edge_n) ** 2 * DOME_OCCLUSION
    slope = ((0.5 - fx) * 0.8 + (0.5 - fy) * 0.6)          # + toward the lit side
    dome_light = 1.0 + slope * DOME_LIGHT * (1.0 - edge_n * 0.3)

    lightness = (1.0 + sett_tint + grain + polish) * occl * dome_light
    # Per-sett hue: granite from several quarries - warm grey, blue-grey, rust-flecked.
    hue_pick = rng.integers(0, len(STONE_HUES), size=(rows, cols))
    hue = np.asarray(STONE_HUES)[hue_pick[row_idx % rows, col_idx % cols]]
    stone = STONE_BASE[None, None, :] * hue * lightness[:, :, None]
    joint_rgb = np.broadcast_to(JOINT_COLOUR[None, None, :], stone.shape)

    albedo = joint_rgb + (stone - joint_rgb) * joint_mask[:, :, None]
    albedo = np.clip(albedo, 0.0, 1.0)

    # ---- normal map: setts are domed, joints are recessed.
    height = joint_mask * (0.72 + 0.28 * np.clip(1.0 - (1.0 - joint_mask) * 2.0, 0, 1))
    height += sett_tint * 0.12 + grain * 0.5
    # Dome each sett by its distance from its own centre.
    dome = 1.0 - ((fx - 0.5) ** 2 + (fy - 0.5) ** 2) * 1.6
    height += np.clip(dome, 0, 1) * 0.22 * joint_mask

    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5
    gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5
    strength = 6.0
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(gx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx * inv, ny * inv, nz * inv], axis=-1) * 0.5 + 0.5

    # ---- roughness: joints are rough mortar, sett tops are polished.
    rough = 0.86 - joint_mask * 0.30 - polish * 1.4 + grain * 0.6
    rough = np.clip(rough, 0.18, 1.0)

    os.makedirs(OUT_DIR, exist_ok=True)

    def save(arr, name):
        img = Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8))
        path = os.path.join(OUT_DIR, name)
        img.save(path)
        return path

    p1 = save(albedo, "MapleCity_Setts_Albedo.png")
    p2 = save(normal, "MapleCity_Setts_Normal.png")
    p3 = save(np.repeat(rough[:, :, None], 3, axis=2), "MapleCity_Setts_Rough.png")

    print(f"[setts] {cols} x {rows} setts per {TILE_METRES:.1f} m tile "
          f"({RES/cols:.1f} x {RES/rows:.1f} px each)")
    print(f"[setts] albedo mean {albedo.reshape(-1,3).mean(axis=0)}")
    for p in (p1, p2, p3):
        print(f"[setts] wrote {p}")


if __name__ == "__main__":
    build()

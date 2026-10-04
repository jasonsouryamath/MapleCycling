"""
MAPLE CITY - facade, pavement and leaf-sprite texture generator.

WHY THIS EXISTS
---------------
The M1 blockout render (good_graphics/diag_maple_sky_terrace.png) came back with 663 buildings
that all read as the SAME cream slab, even though the three districts each carry their own
plaster palette and the facade shader genuinely does multiply per-building vertex colour.

The cause was the borrowed texture. The blockout used Shiosai_HarbourFacade_Albedo_v2.png - a
strongly-patterned CREAM harbour plank map. Multiplying a cream tint by a cream texture gives
cream; multiplying a dark-timber tint by it gives slightly darker cream. The texture was
dominating, and every building lost its district.

The fix is a facade map that is deliberately NEUTRAL MID-GREY and carries STRUCTURE (floor
bands, window bays, ground-floor shopfront) rather than colour, so the vertex tint is what the
eye actually reads. Same reasoning for the pavement: the blockout reused a warm terrain albedo
and the footpaths came back mauve.

Outputs (all tiling):
    MapleCity_Facade_Albedo.png    2048  neutral facade structure
    MapleCity_Facade_Normal.png    2048
    MapleCity_Pavement_Albedo.png  1024  cast-concrete paving slabs
    MapleCity_Pavement_Normal.png  1024
    MapleCity_Leaf_Sprite.png       512  RGBA maple/ginkgo leaf atlas for the signature VFX

All tuning below is PROVISIONAL and named.
"""

import os
import numpy as np
from PIL import Image

SEED = 20260914

OUT_DIR = os.path.join(
    # this file lives at <repo>/tools/blender/, so three levels up is the repo root
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "Environment", "MapleCity", "Textures")


def _normal_from_height(height, strength):
    """Central-difference normal map from a height field. np.roll keeps it tiling."""
    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5
    gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(gx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx * inv, ny * inv, nz * inv], axis=-1) * 0.5 + 0.5


def _save(arr, name):
    if arr.shape[-1] == 4:
        img = Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8), mode="RGBA")
    else:
        img = Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8))
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"[maple-tex] wrote {path}")
    return path


# ------------------------------------------------------------------ facade
FACADE_RES = 2048
FACADE_FLOORS = 6            # floor bands across the tile
FACADE_BAYS = 8              # window bays across the tile
# Deliberately NEUTRAL: this map carries structure, the vertex tint carries colour. A mid value
# near 0.72 keeps tinted facades bright enough to read at golden hour without clipping.
FACADE_BASE = 0.72
FACADE_BAND = 0.055          # floor-slab banding contrast
FACADE_RECESS = 0.30         # how much darker a window reveal is


def build_facade():
    rng = np.random.default_rng(SEED + 1)
    R = FACADE_RES
    yy, xx = np.mgrid[0:R, 0:R].astype(np.float64) / R

    fy = yy * FACADE_FLOORS
    fx = xx * FACADE_BAYS
    floor = np.floor(fy).astype(np.int64)
    bay = np.floor(fx).astype(np.int64)
    ly, lx = fy - floor, fx - bay

    v = np.full((R, R), FACADE_BASE)

    # Floor slab band: a darker horizontal line at each storey division, plus a soft gradient so
    # the wall is not perfectly flat.
    band = np.clip(1.0 - np.abs(ly - 0.02) / 0.06, 0, 1)
    v -= band * FACADE_BAND * 2.0
    v += (1.0 - ly) * 0.012

    # Window reveal: a recessed rectangle per bay, skipped on the ground floor (shopfront).
    win = ((lx > 0.22) & (lx < 0.78) & (ly > 0.30) & (ly < 0.82) & (floor > 0))
    # Mullion: a vertical bar splitting the window, so it reads as glazing not a hole.
    mullion = win & (np.abs(lx - 0.5) < 0.030)
    v = np.where(win, v * (1.0 - FACADE_RECESS), v)
    v = np.where(mullion, FACADE_BASE * 0.92, v)

    # Ground-floor shopfront: taller glazing, its own frame.
    shop = ((lx > 0.10) & (lx < 0.90) & (ly > 0.18) & (ly < 0.86) & (floor == 0))
    v = np.where(shop, v * (1.0 - FACADE_RECESS * 0.75), v)

    # Per-bay lightness so neighbouring panels are not identical.
    panel = rng.normal(0.0, 0.022, size=(FACADE_FLOORS, FACADE_BAYS))
    v += panel[floor % FACADE_FLOORS, bay % FACADE_BAYS]

    # Fine plaster grain + faint vertical weathering streaks under the window sills.
    grain = rng.normal(0.0, 1.0, size=(R, R))
    grain = (grain + np.roll(grain, 1, 0) + np.roll(grain, 1, 1) + np.roll(grain, 2, 1)) / 4.0
    v += grain * 0.020
    streak = rng.normal(0.0, 1.0, size=(1, R))
    streak = np.repeat(streak, R, axis=0)
    v -= np.clip(streak, 0, None) * 0.012 * np.clip(ly - 0.8, 0, 1) * 6.0

    v = np.clip(v, 0.05, 1.0)
    albedo = np.repeat(v[:, :, None], 3, axis=2)

    h = np.where(win | shop, 0.0, 1.0) * 0.8 + band * (-0.4) + grain * 0.15
    _save(albedo, "MapleCity_Facade_Albedo.png")
    _save(_normal_from_height(h, 5.0), "MapleCity_Facade_Normal.png")
    print(f"[maple-tex] facade: {FACADE_FLOORS} floors x {FACADE_BAYS} bays, "
          f"mean {albedo.mean():.3f} (neutral so vertex tint dominates)")


# ---------------------------------------------------------------- pavement
PAVE_RES = 1024
PAVE_SLABS = 6               # slabs across the tile
# Cool light concrete. The blockout's footpaths came back MAUVE because they reused a warm
# terrain albedo under a golden-hour grade; a neutral-cool base is what reads as pavement.
PAVE_BASE = np.array([0.615, 0.608, 0.592])
PAVE_JOINT = np.array([0.400, 0.396, 0.392])
PAVE_VARIANCE = 0.045


def build_pavement():
    rng = np.random.default_rng(SEED + 2)
    R = PAVE_RES
    yy, xx = np.mgrid[0:R, 0:R].astype(np.float64) / R
    fx, fy = xx * PAVE_SLABS, yy * PAVE_SLABS
    cx, cy = np.floor(fx).astype(np.int64), np.floor(fy).astype(np.int64)
    lx, ly = fx - cx, fy - cy

    tint = rng.normal(0.0, PAVE_VARIANCE, size=(PAVE_SLABS, PAVE_SLABS))
    slab = tint[cy % PAVE_SLABS, cx % PAVE_SLABS]

    edge = np.minimum(np.minimum(lx, 1 - lx), np.minimum(ly, 1 - ly)) * (R / PAVE_SLABS)
    joint_mask = np.clip((edge - 2.0) / 2.5, 0.0, 1.0)

    grain = rng.normal(0.0, 1.0, size=(R, R))
    grain = (grain + np.roll(grain, 1, 0) + np.roll(grain, 1, 1)) / 3.0

    stone = PAVE_BASE[None, None, :] * (1.0 + slab + grain * 0.035)[:, :, None]
    joint = np.broadcast_to(PAVE_JOINT[None, None, :], stone.shape)
    albedo = np.clip(joint + (stone - joint) * joint_mask[:, :, None], 0, 1)

    h = joint_mask + grain * 0.10
    _save(albedo, "MapleCity_Pavement_Albedo.png")
    _save(_normal_from_height(h, 4.0), "MapleCity_Pavement_Normal.png")
    print(f"[maple-tex] pavement: {PAVE_SLABS}x{PAVE_SLABS} slabs, "
          f"mean {albedo.reshape(-1,3).mean(axis=0)}")


# -------------------------------------------------------------- leaf sprite
LEAF_RES = 512               # 2x2 atlas of 256 px leaves
# The signature VFX: red-and-gold maple + ginkgo, the city's answer to Sakura's petals.
LEAF_COLOURS = [
    # NOTE: the first variant used to be (0.851, 0.325, 0.431) - a fuchsia whose BLUE channel sat
    # above its green. Bloomed at particle scale that reads as a cherry BLOSSOM, i.e. Sakura's
    # signature leaking into the city. An autumn maple is scarlet: red high, green low, blue lowest.
    (0.757, 0.204, 0.157),   # crimson maple
    (0.878, 0.455, 0.184),   # burnt orange maple
    (0.918, 0.780, 0.310),   # golden ginkgo
    (0.937, 0.612, 0.216),   # amber
]


def _maple_mask(n):
    """Five-lobed maple silhouette as a polar radius threshold."""
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    x = (xx / n - 0.5) * 2.0
    y = (yy / n - 0.5) * 2.0
    r = np.sqrt(x * x + y * y) + 1e-6
    a = np.arctan2(y, x)
    # 5 lobes, deep sinuses, plus a stem running down.
    lobe = 0.60 + 0.30 * np.abs(np.cos(2.5 * a)) ** 0.7
    # Serrated edge.
    lobe += 0.035 * np.sin(22.0 * a)
    mask = (r < lobe).astype(np.float64)
    stem = (np.abs(x) < 0.035) & (y > 0.35) & (y < 0.95)
    mask = np.maximum(mask, stem.astype(np.float64))
    return mask, r, a


def _ginkgo_mask(n):
    """Ginkgo fan: a wedge with a notched outer edge."""
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64)
    x = (xx / n - 0.5) * 2.0
    y = (yy / n - 0.5) * 2.0
    r = np.sqrt(x * x + y * y) + 1e-6
    a = np.arctan2(y, x)
    wedge = np.abs(a + np.pi / 2) < 0.95          # opening upward
    outer = 0.86 + 0.05 * np.sin(9.0 * a)
    mask = (wedge & (r < outer) & (r > 0.10)).astype(np.float64)
    stem = (np.abs(x) < 0.035) & (y > 0.05) & (y < 0.95)
    return np.maximum(mask, stem.astype(np.float64)), r, a


def build_leaf():
    n = LEAF_RES // 2
    out = np.zeros((LEAF_RES, LEAF_RES, 4), dtype=np.float64)
    for i, col in enumerate(LEAF_COLOURS):
        # Three maples and one ginkgo, matching the design's "red-and-gold maple + ginkgo".
        mask, r, _ = _ginkgo_mask(n) if i == 2 else _maple_mask(n)

        # Antialias the silhouette so leaves do not read as jagged confetti.
        m = mask.copy()
        for _ in range(2):
            m = (m + np.roll(m, 1, 0) + np.roll(m, -1, 0)
                 + np.roll(m, 1, 1) + np.roll(m, -1, 1)) / 5.0
        alpha = np.clip(m * 1.6, 0, 1) * mask.clip(0, 1).max()
        alpha = np.clip((m - 0.35) / 0.35, 0, 1)

        # Veins: slightly lighter radial lines, and a darker edge so the leaf has form.
        shade = 1.0 - np.clip((r - 0.25) * 0.45, 0, 0.30)
        rgb = np.array(col)[None, None, :] * shade[:, :, None]

        oy, ox = (i // 2) * n, (i % 2) * n
        out[oy:oy + n, ox:ox + n, :3] = rgb
        out[oy:oy + n, ox:ox + n, 3] = alpha

    _save(out, "MapleCity_Leaf_Sprite.png")
    print(f"[maple-tex] leaf atlas: 2x2, {len(LEAF_COLOURS)} variants, "
          f"coverage {out[:,:,3].mean()*100:.1f}%")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    build_facade()
    build_pavement()
    build_leaf()

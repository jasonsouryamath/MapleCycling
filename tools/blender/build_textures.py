"""
Procedural, seamlessly tiling PBR texture set for Sakura Pass.

Tiling detail maps (rather than one baked atlas) are what let a 600 m mountainside hold up at
close range without a gigabyte of texture memory. Everything here is periodic noise, so the maps
wrap perfectly in both axes.

Outputs albedo (sRGB), normal (linear tangent-space) and roughness (linear) for each surface,
plus alpha-cut blossom and leaf atlases for the canopies.

Run via:  blender -b -P build_textures.py
"""

import os
import struct
import sys
import zlib

# Blender runs scripts with -P outside any package context, so make sibling modules importable.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

from sakura_lib import textures_dir, fbm_2d, ridged_2d, value_noise_2d

RES = 1024
TILE = 1.0          # world period of the noise field in UV units


# --------------------------------------------------------------------------- png io

def write_png(path, rgba):
    """Minimal, dependency-free 8-bit RGBA PNG writer (row filter 0)."""
    h, w, c = rgba.shape
    assert c in (3, 4)
    color_type = 6 if c == 4 else 2

    raw = b"".join(b"\x00" + rgba[y].tobytes() for y in range(h))
    comp = zlib.compress(raw, 9)

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = (b"\x89PNG\r\n\x1a\n" +
           chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, color_type, 0, 0, 0)) +
           chunk(b"IDAT", comp) +
           chunk(b"IEND", b""))

    with open(path, "wb") as f:
        f.write(png)
    print(f"[sakura] texture -> {os.path.basename(path)} ({len(png)/1024:.0f} KB)")


def to_u8(a):
    return np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8)


def save_rgb(name, rgb):
    write_png(os.path.join(textures_dir(), name), to_u8(rgb))


def save_rgba(name, rgb, alpha):
    img = np.dstack([rgb, alpha[..., None]])
    write_png(os.path.join(textures_dir(), name), to_u8(img))


# ------------------------------------------------------------------------ utilities

def uv_grid(res=RES):
    t = (np.arange(res) + 0.5) / res
    return np.meshgrid(t, t, indexing='xy')


def height_to_normal(height, strength=1.0):
    """Sobel-derived tangent-space normal map. Wraps, so the result stays seamless."""
    hx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    hy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx = -hx * strength * RES / 64.0
    ny = -hy * strength * RES / 64.0
    nz = np.ones_like(height)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.dstack([nx * inv * 0.5 + 0.5, ny * inv * 0.5 + 0.5, nz * inv * 0.5 + 0.5])


def mix(a, b, t):
    t = t[..., None] if (a.ndim == 3 and t.ndim == 2) else t
    return a * (1.0 - t) + b * t


def norm01(a):
    """Rescale a noise field to use the full 0..1 range so downstream masks behave predictably."""
    lo, hi = float(np.min(a)), float(np.max(a))
    return (a - lo) / max(hi - lo, 1e-9)


def contrast(a, amount=1.0, pivot=0.5):
    return np.clip((a - pivot) * amount + pivot, 0.0, 1.0)


def srgb(hex_or_tuple):
    if isinstance(hex_or_tuple, str):
        h = hex_or_tuple.lstrip("#")
        return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])
    return np.array(hex_or_tuple)


def flat(color, res=RES):
    return np.ones((res, res, 3)) * srgb(color)


# --------------------------------------------------------------------------- alpine grass

def build_grass():
    u, v = uv_grid()

    clump = norm01(fbm_2d(u, v, 6, octaves=5, seed=11, tile=TILE))
    patch = norm01(fbm_2d(u, v, 2, octaves=3, seed=23, tile=TILE))
    blade = norm01(value_noise_2d(u, v, 190, seed=31, tile=TILE))
    fine = norm01(fbm_2d(u, v, 55, octaves=3, seed=47, tile=TILE))

    # Directional streaking reads as blades lying over rather than a noise soup. The aspect was
    # 1:20, which looks right on flat lawn but is projected world-aligned by the terrain's
    # triplanar sampling, so every wooded slope picked up hard fall-line striping. A gentler
    # 1:3.7 keeps the lie of the grass without reading as erosion scars. PROVISIONAL tuning.
    streak = norm01(value_noise_2d(u * 0.6, v * 2.2, 90, seed=53, tile=None))

    deep = srgb("2f4a20")
    mid = srgb("4a6b2c")
    dry = srgb("7f8a45")
    bloom = srgb("93a15a")

    base = np.ones((RES, RES, 3)) * deep
    base = mix(base, np.ones_like(base) * mid, contrast(clump, 1.5))
    base = mix(base, np.ones_like(base) * dry, np.clip(patch * 1.4 - 0.45, 0, 1))
    base = mix(base, np.ones_like(base) * bloom, np.clip(streak * 1.2 - 0.55, 0, 1))
    base *= (0.68 + 0.62 * blade)[..., None]
    base *= (0.86 + 0.28 * fine)[..., None]

    # Scattered small wildflowers - a tiny amount of hue break stops the green reading as plastic.
    spots = norm01(value_noise_2d(u, v, 230, seed=71, tile=TILE))
    flower = np.clip((spots - 0.93) * 26.0, 0, 1)
    base = mix(base, np.ones_like(base) * srgb("f2e6ef"), flower * 0.75)

    height = clump * 0.55 + fine * 0.3 + blade * 0.15
    rough = 0.72 + 0.2 * (1.0 - clump) + 0.06 * fine

    save_rgb("Sakura_Grass_Albedo.png", np.clip(base, 0, 1))
    save_rgb("Sakura_Grass_Normal.png", height_to_normal(height, 1.5))
    save_rgb("Sakura_Grass_Rough.png", np.dstack([rough] * 3).clip(0, 1))


# ------------------------------------------------------------------------ volcanic rock

def build_rock():
    u, v = uv_grid()

    # Bedding planes: near-horizontal strata warped by low-frequency noise.
    warp = norm01(fbm_2d(u, v, 3, octaves=4, seed=91, tile=TILE))
    strata = np.sin((v * 11.0 + warp * 4.5) * np.pi) * 0.5 + 0.5
    strata = contrast(strata, 1.9)

    crack = norm01(ridged_2d(u, v, 6, octaves=5, seed=101, tile=TILE))
    crack_fine = norm01(ridged_2d(u, v, 17, octaves=4, seed=107, tile=TILE))
    grain = norm01(fbm_2d(u, v, 80, octaves=4, seed=113, tile=TILE))
    blotch = norm01(fbm_2d(u, v, 4, octaves=3, seed=127, tile=TILE))

    # The darkest bedding tone used to be #22242a, close enough to black that the strata read as
    # cast shadows lying across the slope rather than as rock colour, and competed with the real
    # cel shadow term from section 24. Lifted so the strata still give the cliff scale without
    # impersonating shadow. PROVISIONAL.
    dark = srgb("3a3d42")
    mid = srgb("4e4c48")
    warm = srgb("776a58")
    pale = srgb("968d7e")
    moss = srgb("3d5a2f")

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * pale, contrast(strata, 1.4) * 0.55)
    base = mix(base, np.ones_like(base) * warm, np.clip(blotch * 1.5 - 0.45, 0, 1))
    base = mix(base, np.ones_like(base) * dark, np.clip((1.0 - strata) * 1.2 - 0.35, 0, 1))

    # Deep fissures read almost black; this is what gives a cliff its sense of scale.
    fissure = np.clip((0.42 - crack) * 3.4, 0, 1)
    fissure = np.maximum(fissure, np.clip((0.30 - crack_fine) * 3.0, 0, 1) * 0.55)
    base = mix(base, np.ones_like(base) * dark * 0.75, fissure)

    base *= (0.74 + 0.46 * grain)[..., None]

    # Moss only where fissures hold moisture, and only on part of the surface.
    moss_mask = fissure * np.clip(blotch * 2.2 - 0.9, 0, 1)
    base = mix(base, np.ones_like(base) * moss, np.clip(moss_mask * 1.4, 0, 1) * 0.85)

    height = (1.0 - fissure) * 0.55 + strata * 0.28 + grain * 0.17
    rough = 0.78 + 0.16 * grain - 0.20 * moss_mask

    save_rgb("Sakura_Rock_Albedo.png", np.clip(base, 0, 1))
    save_rgb("Sakura_Rock_Normal.png", height_to_normal(height, 3.2))
    save_rgb("Sakura_Rock_Rough.png", np.dstack([rough] * 3).clip(0, 1))


# ------------------------------------------------------------------------------ scree

def build_scree():
    u, v = uv_grid()

    # Layered voronoi-ish cells approximate packed gravel.
    cells = np.zeros((RES, RES))
    for i, f in enumerate((26, 52, 96)):
        cells = np.maximum(cells, norm01(value_noise_2d(u, v, f, seed=200 + i * 37, tile=TILE)) *
                           (0.9 - i * 0.2))
    cells = norm01(cells)
    grit = norm01(fbm_2d(u, v, 150, octaves=3, seed=311, tile=TILE))
    silt = norm01(fbm_2d(u, v, 5, octaves=4, seed=331, tile=TILE))

    pale = srgb("8b8071")
    mid = srgb("5f574c")
    dark = srgb("3a352f")

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * pale, np.clip(cells * 1.6 - 0.45, 0, 1))
    base = mix(base, np.ones_like(base) * dark, np.clip(silt * 1.5 - 0.6, 0, 1))
    base *= (0.7 + 0.6 * grit)[..., None]

    height = cells * 0.7 + grit * 0.3
    rough = 0.86 + 0.1 * grit

    save_rgb("Sakura_Scree_Albedo.png", np.clip(base, 0, 1))
    save_rgb("Sakura_Scree_Normal.png", height_to_normal(height, 2.2))
    save_rgb("Sakura_Scree_Rough.png", np.dstack([rough] * 3).clip(0, 1))


# ---------------------------------------------------------------------------- forest soil

def build_soil():
    """
    Forest soil with leaf litter - section 29's 'forest soil' and 'leaf litter' layers in one
    map, because they never occur apart on a wooded valley flank. Damp humus base, drifted
    broadleaf flakes on top, occasional exposed mineral earth where the litter thins.

    This is the layer that keeps a moderately steep slope from reading as either perfect lawn
    or bare cliff: the terrain builder opens it between the grass and the rock band.
    """
    u, v = uv_grid()

    humus = norm01(fbm_2d(u, v, 7, octaves=5, seed=901, tile=TILE))
    patch = norm01(fbm_2d(u, v, 3, octaves=3, seed=947, tile=TILE))
    grit = norm01(fbm_2d(u, v, 190, octaves=3, seed=977, tile=TILE))

    # Leaf flakes: cells at a few scales and orientations so the litter reads as overlapping
    # blades rather than as noise. The aspect ratios are kept close to square on purpose - an
    # earlier set at 1:2.7 and 2.4:1 was strongly anisotropic, and because the terrain samples
    # this triplanar the flakes lined up into world-aligned fall-line streaks across every
    # wooded slope. Mild aspect gives the same organic feel without a preferred direction.
    litter = np.zeros((RES, RES))
    for i, (f, sx, sy) in enumerate(((34, 1.0, 1.35), (48, 1.25, 1.0), (62, 1.1, 1.15))):
        cell = norm01(value_noise_2d(u * sx, v * sy, f, seed=1009 + i * 53, tile=TILE))
        litter = np.maximum(litter, cell * (0.95 - i * 0.18))
    litter = norm01(litter)
    # Litter thins out where the mineral earth shows through.
    litter *= np.clip(patch * 1.7 - 0.25, 0, 1)

    # Japanese woodland litter under a low sun reads as warm mid-brown, not peat. An earlier
    # palette (3a2c20 base) baked at ~0.23 luminance and the cut slopes rendered near-black once
    # the cel ramp shaded them, so the whole set is lifted about one stop. PROVISIONAL tuning.
    earth_dark = srgb("6b5540")
    earth_mid = srgb("8a6c4c")
    humus_warm = srgb("9c7752")
    leaf_tan = srgb("bd9663")
    leaf_rust = srgb("ac7548")

    base = np.ones((RES, RES, 3)) * earth_mid
    base = mix(base, np.ones_like(base) * earth_dark, np.clip(humus * 1.4 - 0.35, 0, 1))
    base = mix(base, np.ones_like(base) * humus_warm, np.clip(patch * 1.2 - 0.3, 0, 1))
    base = mix(base, np.ones_like(base) * leaf_tan, np.clip(litter * 1.5 - 0.35, 0, 1))
    base = mix(base, np.ones_like(base) * leaf_rust,
               np.clip(litter * norm01(fbm_2d(u, v, 21, octaves=2, seed=1061, tile=TILE)) * 1.8 - 0.5, 0, 1))
    base *= (0.82 + 0.32 * grit)[..., None]

    height = litter * 0.62 + humus * 0.28 + grit * 0.10
    # Damp humus is matte; dry leaf flakes catch a little more light.
    rough = 0.94 - 0.16 * litter

    save_rgb("Sakura_Soil_Albedo.png", np.clip(base, 0, 1))
    save_rgb("Sakura_Soil_Normal.png", height_to_normal(height, 1.9))
    save_rgb("Sakura_Soil_Rough.png", np.dstack([rough] * 3).clip(0, 1))


# ---------------------------------------------------------------------------- asphalt

def build_asphalt():
    """
    Dense-graded asphalt concrete, authored the way the material actually looks.

    The previous set read as pale speckled gravel: a 0x4d light aggregate was mixed in at high
    contrast over most of the surface, and the wheel-track polish was BAKED INTO THE TILE - so
    the "worn tracks" repeated every 4.5 m across the whole carriageway. Both are why the road
    came out as a light, noisy, purple-grey strip.

    Real asphalt is a DARK bitumen matrix in which only the top faces of the aggregate are
    exposed, so:
      * the binder colour dominates (~75% of the surface)
      * the aggregate is a narrow value range just above it, not a bright speckle
      * the wear pattern belongs in ROAD space, so it now lives in MapleRide/HDRP/Road, which
        knows where the lanes and wheel paths actually are.
    """
    u, v = uv_grid()

    # ~1024 px over a 4.5 m tile: 150 cells ~ 3 cm stones, 320 ~ 1.4 cm chips, 700 ~ 6 mm fines.
    agg = norm01(value_noise_2d(u, v, 150, seed=401, tile=TILE))
    chip = norm01(value_noise_2d(u, v, 320, seed=409, tile=TILE))
    fines = norm01(value_noise_2d(u, v, 700, seed=413, tile=TILE))
    binder = norm01(fbm_2d(u, v, 7, octaves=4, seed=419, tile=TILE))
    mat_var = norm01(fbm_2d(u, v, 2.2, octaves=4, seed=423, tile=TILE))

    binder_c = srgb("232428")     # bitumen matrix - the dominant colour
    stone_c = srgb("3a3b40")      # exposed aggregate face
    stone_pale = srgb("46474b")   # the occasional pale stone
    rich_c = srgb("1b1c1f")       # bitumen-rich, freshly bled patch

    base = np.ones((RES, RES, 3)) * binder_c

    # Only the top third of the aggregate cells break the surface, and they land close in value
    # to the binder: that narrow spread is what separates asphalt from chip seal. Threshold
    # raised (0.58 -> 0.63) and the blend softened after the first render: at a 1.3 m rider's
    # eye the exposed faces still read as light speckle rather than as aggregate in bitumen.
    face = np.clip((agg - 0.63) * 2.0, 0, 1)
    base = mix(base, np.ones_like(base) * stone_c, face * 0.70)
    face2 = np.clip((chip - 0.82) * 3.0, 0, 1)
    base = mix(base, np.ones_like(base) * stone_pale, face2 * 0.35)

    # Sand fraction: pure low-contrast value break-up, no colour shift.
    base *= (0.95 + 0.10 * fines)[..., None]

    # Bitumen-rich areas and the paving mat variation, both very soft.
    base = mix(base, np.ones_like(base) * rich_c, np.clip(binder * 1.25 - 0.55, 0, 1) * 0.55)
    base *= (0.92 + 0.16 * mat_var)[..., None]

    # Aggregate relief drives the normal; the binder is smooth, the stones stand proud.
    height = face * 0.55 + face2 * 0.20 + fines * 0.15 + binder * 0.10
    # Asphalt is matte overall, slightly less so where aggregate is polished flat.
    rough = 0.90 - 0.12 * face + 0.05 * fines

    save_rgb("Sakura_Asphalt_Albedo.png", np.clip(base, 0, 1))
    save_rgb("Sakura_Asphalt_Normal.png", height_to_normal(height, 0.85))
    save_rgb("Sakura_Asphalt_Rough.png", np.dstack([rough] * 3).clip(0, 1))


# ------------------------------------------------------------------------------- bark

def build_bark():
    u, v = uv_grid()

    # Vertical fissures, stretched hard along V (the trunk axis). fbm rather than ridged
    # noise: ridged creases land exactly on the value-noise lattice and print a visible
    # grid of hairlines onto the trunk.
    fis = contrast(norm01(fbm_2d(u * 5.0, v * 0.55, 9, octaves=5, seed=501, tile=None)), 1.9)
    coarse = norm01(fbm_2d(u * 2.2, v * 0.30, 5, octaves=3, seed=505, tile=None))
    fine = norm01(fbm_2d(u * 1.6, v * 0.55, 26, octaves=3, seed=511, tile=None))
    # Horizontal lenticels are the signature of cherry bark: short silver dashes that run
    # around the trunk, so they are wide in U and very thin in V.
    lent = norm01(fbm_2d(u * 1.30, v * 3.0, 20, octaves=2, seed=521, tile=None))
    band = norm01(value_noise_2d(u * 0.05, v * 3.5, 26, seed=531, tile=None))
    lent = np.clip((lent - 0.55) * 2.4, 0, 1) * np.clip((band - 0.50) * 2.6, 0, 1)

    dark = srgb("2c1e1b")
    mid = srgb("5a3b31")
    warm = srgb("7d5341")
    silver = srgb("b0988a")

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * dark, np.clip(fis * 1.35 - 0.30, 0, 1))
    base = mix(base, np.ones_like(base) * warm, np.clip((1 - fis) * 1.25 - 0.40, 0, 1))
    base = mix(base, np.ones_like(base) * silver, lent * 0.60)
    base *= (0.84 + 0.26 * coarse)[..., None] * (0.92 + 0.14 * fine)[..., None]

    height = (1.0 - fis) * 0.70 + coarse * 0.20 + fine * 0.10 + lent * 0.14
    rough = 0.84 + 0.10 * fine - 0.26 * lent

    save_rgb("Sakura_Bark_Albedo.png", np.clip(base, 0, 1))
    save_rgb("Sakura_Bark_Normal.png", height_to_normal(height, 2.1))
    save_rgb("Sakura_Bark_Rough.png", np.dstack([rough] * 3).clip(0, 1))


# -------------------------------------------------------------------- blossom / leaf

def _petal_field(cx, cy, radius, rot, res):
    """Signed coverage of one five-lobed sakura flower centred at (cx, cy)."""
    t = (np.arange(res) + 0.5) / res
    x, y = np.meshgrid(t, t, indexing='xy')
    dx = x - cx
    dy = y - cy
    r = np.sqrt(dx * dx + dy * dy) / max(radius, 1e-6)
    a = np.arctan2(dy, dx) + rot

    # Five lobes with a notch at each petal tip.
    lobe = 0.62 + 0.38 * np.abs(np.cos(a * 2.5))
    notch = 1.0 - 0.22 * np.clip(np.cos(a * 5.0) * -1.0, 0, 1)
    edge = lobe * notch
    return np.clip((edge - r) * 9.0, 0, 1), r


def build_blossom_atlas():
    res = RES
    rng = np.random.default_rng(9001)

    alpha = np.zeros((res, res))
    color = np.zeros((res, res, 3))
    depth = np.zeros((res, res))

    deep = srgb("e2568c")
    petal = srgb("f9a8c4")
    pale = srgb("fdeaf1")
    centre = srgb("b8305f")

    # 2 x 2 atlas of dense clusters; each cell is one canopy card.
    for cell_y in range(2):
        for cell_x in range(2):
            ox, oy = cell_x * 0.5, cell_y * 0.5
            count = 46
            for i in range(count):
                cx = ox + 0.25 + rng.normal(0, 0.105)
                cy = oy + 0.25 + rng.normal(0, 0.105)
                cx = np.clip(cx, ox + 0.045, ox + 0.455)
                cy = np.clip(cy, oy + 0.045, oy + 0.455)
                rad = rng.uniform(0.028, 0.062)
                rot = rng.uniform(0, np.pi * 2)
                cov, r = _petal_field(cx, cy, rad, rot, res)
                if cov.max() <= 0:
                    continue

                z = rng.uniform(0, 1)
                mask = (cov > 0.02) & (z >= depth)

                shade = rng.uniform(0.82, 1.12)
                tint = np.clip(r, 0, 1)[..., None]
                col = petal * (1 - tint) + pale * tint
                col = col * (1 - 0.30 * tint[..., 0][..., None]) + deep * 0.30 * tint
                col = col * shade

                # Stamen cluster at the flower heart.
                heart = np.clip((0.26 - r) * 6.0, 0, 1)[..., None]
                col = col * (1 - heart) + centre * heart
                col = col * (1 - heart * 0.35) + srgb("ffd98a") * heart * 0.35

                color[mask] = col[mask]
                alpha[mask] = np.maximum(alpha[mask], cov[mask])
                depth[mask] = z

    # Slight overall noise break so repeated cards do not look identical.
    u, v = uv_grid()
    color *= (0.9 + 0.2 * fbm_2d(u, v, 40, octaves=3, seed=777, tile=TILE))[..., None]

    save_rgba("Sakura_Blossom_Atlas.png", np.clip(color, 0, 1), np.clip(alpha, 0, 1))


def build_leaf_atlas():
    res = RES
    rng = np.random.default_rng(4242)
    alpha = np.zeros((res, res))
    color = np.zeros((res, res, 3))
    depth = np.zeros((res, res))

    t = (np.arange(res) + 0.5) / res
    gx, gy = np.meshgrid(t, t, indexing='xy')

    young = srgb("6d9a43")
    mature = srgb("35602a")
    dark = srgb("21401d")

    for cell_y in range(2):
        for cell_x in range(2):
            ox, oy = cell_x * 0.5, cell_y * 0.5
            for i in range(34):
                cx = np.clip(ox + 0.25 + rng.normal(0, 0.1), ox + 0.05, ox + 0.45)
                cy = np.clip(oy + 0.25 + rng.normal(0, 0.1), oy + 0.05, oy + 0.45)
                L = rng.uniform(0.055, 0.105)
                W = L * rng.uniform(0.38, 0.52)
                rot = rng.uniform(0, np.pi * 2)

                dx = gx - cx
                dy = gy - cy
                rx = dx * np.cos(rot) - dy * np.sin(rot)
                ry = dx * np.sin(rot) + dy * np.cos(rot)

                # Ovate leaf with a drawn-out tip, like a cherry leaf.
                s = np.clip((rx / L) * 0.5 + 0.5, 0, 1)
                halfw = W * np.sin(np.pi * s ** 0.75)
                cov = np.clip((halfw - np.abs(ry)) * 120.0, 0, 1)
                cov *= np.clip((1.0 - np.abs(rx / L)) * 6.0, 0, 1)
                if cov.max() <= 0:
                    continue

                z = rng.uniform(0, 1)
                mask = (cov > 0.02) & (z >= depth)

                blend = np.clip(s, 0, 1)[..., None]
                col = mature * (1 - blend) + young * blend
                col = col * rng.uniform(0.82, 1.15)
                # Midrib
                rib = np.clip((0.0035 - np.abs(ry)) * 400.0, 0, 1)[..., None]
                col = col * (1 - rib) + dark * rib

                color[mask] = col[mask]
                alpha[mask] = np.maximum(alpha[mask], cov[mask])
                depth[mask] = z

    save_rgba("Sakura_Leaf_Atlas.png", np.clip(color, 0, 1), np.clip(alpha, 0, 1))


# ------------------------------------------------------------------------------ main

def build_ridge_gradient():
    """
    Vertical rock -> snow ramp for the distant ranges.

    The ranges previously carried snow as a separate mesh split off by altitude, which read as
    blocky white rectangles because the quads are hundreds of metres wide at that distance.
    Sampling this gradient by altitude instead gives a soft, wandering snow line for free.
    """
    h, w = 256, 16
    v = np.linspace(0.0, 1.0, h)[:, None]

    base = srgb("#3b4154")
    scree_c = srgb("#6b6f83")
    snow_c = srgb("#f2eef2")

    # Two soft steps: talus emerging from haze, then the snow line.
    t1 = np.clip((v - 0.18) / 0.40, 0, 1)
    t1 = t1 * t1 * (3 - 2 * t1)
    t2 = np.clip((v - 0.62) / 0.16, 0, 1)
    t2 = t2 * t2 * (3 - 2 * t2)

    rgb = mix(base[None, None, :], scree_c[None, None, :], t1[..., None])
    rgb = mix(rgb, snow_c[None, None, :], t2[..., None])

    # Break the snow line horizontally so it is not a perfectly level contour.
    wob = np.sin(np.linspace(0, np.pi * 2, w))[None, :, None] * 0.03
    rgb = np.clip(rgb + wob, 0, 1)

    rgb = np.broadcast_to(rgb, (h, w, 3)).copy()
    save_rgb("Sakura_Ridge_Gradient.png", rgb)


def build_all_textures():
    print("[sakura] generating tiling PBR texture set...")
    build_grass()
    build_rock()
    build_scree()
    build_soil()
    build_asphalt()
    build_bark()
    build_blossom_atlas()
    build_leaf_atlas()
    build_ridge_gradient()
    print("[sakura] texture set complete.")


if __name__ == "__main__":
    build_all_textures()

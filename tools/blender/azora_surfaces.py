"""
AZORA HIGHLANDS - turf, dry-stone and seed-drift texture generator.

WHY THIS EXISTS
---------------
Azora's ground story is a GRADIENT, not a material: the design doc's palette runs emerald turf
(#3f8f4a) in the sheltered valley to golden dry grass (#d9b24a) on the wind-scoured tops, and
the rider is meant to watch it change as they climb. Nothing in the existing library carries
that. Sakura's grass map is a dark forest-floor green authored for a shaded pass, and the coast
has nothing but dune sand.

The second reason is the region's STRUCTURAL SIGNATURE: dry-stone walls. The doc makes them the
thing that tells you where you are - they follow every field boundary and switchback for 24 km.
Per-stone geometry over that distance would be absurd, so the walls are a swept ribbon and ALL
of their character has to live in the albedo and normal. That needs a real, purpose-built
coursed-rubble map, not a cliff face at a small tile.

Third, the signature VFX. The doc asks for wind-blown grass seed, dandelion fluff and drifting
wildflower petals. That is explicitly NOT Sakura's petals and NOT Maple City's maple leaves,
and the distinction has to survive bloom: a variant whose BLUE channel sits above its GREEN
turns pink when it blooms and the whole region instantly reads as cherry blossom. (That exact
bug shipped in Maple City's first leaf atlas and had to be measured out of a render.) Every
variant here is therefore cream, white, straw or lilac-grey with green >= blue, and the one
genuinely purple wildflower is kept small and rare.

Outputs (all tiling except the sprite atlas):
    Azora_Turf_Albedo.png       1024  upland pasture: fine grass, clover, bare scuffs
    Azora_Turf_Normal.png       1024
    Azora_DryGrass_Albedo.png   1024  wind-scoured golden tops, tussocky
    Azora_DryGrass_Normal.png   1024
    Azora_Stone_Albedo.png      1024  coursed dry-stone rubble (walls, cairns, col marker)
    Azora_Stone_Normal.png      1024
    Azora_Seed_Sprite.png        512  RGBA 2x2 seed / fluff / petal atlas for the drift VFX

All tuning below is PROVISIONAL and named.
"""

import os

import numpy as np
from PIL import Image

SEED = 20260921

OUT_DIR = os.path.join(
    # this file lives at <repo>/tools/blender/, so three levels up is the repo root
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "Environment", "AzoraHighlands", "Textures")

# ---- palette, straight from design doc section 3 -------------------------------------------
TURF_GREEN = np.array([0.247, 0.561, 0.290])   # #3f8f4a sheltered emerald pasture
DRY_GOLD = np.array([0.851, 0.698, 0.290])     # #d9b24a wind-scoured tops
STONE_GREY = np.array([0.604, 0.588, 0.549])   # #9a968c limestone dry-stone
WILDFLOWER = np.array([0.545, 0.373, 0.816])   # #8b5fd0 knapweed / scabious
CREAM = np.array([0.953, 0.941, 0.902])        # #f3f0e6 seed heads, lichen, hut render


def _rng():
    return np.random.default_rng(SEED)


def _normal_from_height(height, strength):
    """Central-difference normal map from a height field. np.roll keeps it tiling."""
    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5
    gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(gx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx * inv, ny * inv, nz * inv], axis=-1) * 0.5 + 0.5


def _save(arr, name):
    os.makedirs(OUT_DIR, exist_ok=True)
    mode = "RGBA" if arr.shape[-1] == 4 else "RGB"
    img = Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8), mode=mode)
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"[azora-tex] wrote {path}")
    return path


# MOIRE NOTE (read before raising any 'fine'/'blade' amplitude below).
#
# These maps tile on the ground at 7-13 m and are usually seen down a 24 km fell at a very
# grazing angle, so the top octave lands well under one screen pixel. High-contrast content at
# that scale does not read as detail - it aliases, and the aliasing is spatially regular, which
# the eye reports as "the ground texture is tiling". The Azora QA finding was largely this.
# The fix is three-part and all three parts are needed:
#   1. anisoLevel 8 on import (SakuraTextureImportSettings.Dirs - these folders were missed),
#   2. larger, mutually-prime tile scales (AzoraHighlandsEnvironment.GroundMaterial),
#   3. LOWER top-octave amplitude here, which is why the 'fine' and 'blade' bands are damped.
# Detail you cannot resolve is not detail. Spend the contrast budget on the macro band instead;
# that one the shader re-taps at 190 m and it is what actually breaks the repeat.

def _fbm(n, octaves, base_freq, rng, gain=0.5):
    """
    Tiling fractal noise.

    Built by upsampling small random lattices, rather than by sampling a continuous noise
    function: the lattice wraps by construction, so every map here tiles seamlessly without any
    mirroring or edge blending.

    THE UPSAMPLE IS SMOOTHSTEP-BILINEAR, NOT np.kron + BOX BLUR.
    The original used np.kron to stamp each lattice cell as a solid block and then ran two
    wrap-around box blurs over it. That is adequate only while the low-frequency octaves carry
    almost no amplitude - at base_freq 2 the blur radius needed to erase a 512 px block is far
    larger than the two passes provide, so the block edges survive as faint straight horizontal
    and vertical seams. They were invisible while the turf's macro swing was +/-2%; the moment
    this pass gave the macro band real contrast (to fix the region's flat monotone ground) the
    seams became a visible rectangular grid across the fell - trading one tiling artefact for
    another. Periodic smoothstep-bilinear interpolation has no block edges to erase.
    """
    out = np.zeros((n, n))
    amp, total = 1.0, 0.0
    freq = base_freq
    for _ in range(octaves):
        f = max(2, int(freq))
        lattice = rng.random((f, f))

        # Periodic smoothstep-bilinear upsample. Index f wraps to 0, so the map tiles.
        idx = np.arange(n) * f / float(n)
        i0 = np.floor(idx).astype(int) % f
        i1 = (i0 + 1) % f
        t = idx - np.floor(idx)
        t = t * t * (3.0 - 2.0 * t)          # smoothstep: C1 across every lattice boundary

        rows = lattice[i0, :] * (1.0 - t)[:, None] + lattice[i1, :] * t[:, None]
        up = rows[:, i0] * (1.0 - t)[None, :] + rows[:, i1] * t[None, :]

        out += up * amp
        total += amp
        amp *= gain
        freq *= 2
    return out / total


def _grass_blades(n, rng, count, length, lean, width=1):
    """
    Scratches a field of short directional strokes into a mask.

    This is what stops the turf reading as noise-on-a-plane: real pasture at a 4 m tile is made
    of thousands of tiny near-vertical blades all leaning the same way (the prevailing wind the
    design doc keeps referring to), and that directionality is most of what the eye uses to
    identify grass.
    """
    mask = np.zeros((n, n))
    ys = rng.integers(0, n, count)
    xs = rng.integers(0, n, count)
    lens = rng.integers(max(2, length // 2), length + 1, count)
    for i in range(count):
        y, x, L = int(ys[i]), int(xs[i]), int(lens[i])
        dx = lean * (rng.random() - 0.2)
        for s in range(L):
            yy = (y - s) % n
            xx = int(x + dx * s) % n
            for w in range(width):
                mask[yy, (xx + w) % n] = max(mask[yy, (xx + w) % n], 1.0 - s / (L + 1.0))
    return mask


# --------------------------------------------------------------------------- turf

def build_turf():
    """
    Sheltered upland pasture: the valley floor and the lower switchbacks.

    Kept deliberately DARKER and less saturated than the palette's #3f8f4a. The palette entry is
    the colour the region should READ as on screen, and it gets there via the cel material's
    tint and a near-white 1.06-intensity key; authoring the albedo at the target value as well
    would double-count and give fluorescent lawn - the exact failure the coast pass documented
    when its headland came back as neon.
    """
    n = 1024
    rng = _rng()

    # THREE scales now, not two. See MACRO below for why the third one had to be added.
    macro = _fbm(n, 2, 2, rng)     # ~256-512 px features: the shader's 190 m macro tap reads this
    broad = _fbm(n, 4, 4, rng)
    fine = _fbm(n, 4, 32, rng)

    # DESATURATED toward olive, not the raw palette hue.
    #
    # The first build authored this as TURF_GREEN * 0.62 straight, and the render came back as a
    # snooker table: one flat, intensely saturated green with no readable landform. Real upland
    # pasture is a grey-green - there is always dead thatch, stone dust and last year's growth
    # mixed into it - and it is the LOW saturation that lets the cel ramp and the key light shape
    # the hillside. Mixing 32% of a neutral olive in drops the chroma without touching the hue.
    olive = np.array([0.352, 0.376, 0.286])
    # AUTHORED AT THE READ VALUE, NOT BELOW IT.
    #
    # The previous pass authored this at 0.58 x 0.68 of the palette green AND the Unity material
    # multiplied it again by _GrassColor (0.356, 0.430, 0.284). Both halves were documented as
    # "authored dark on purpose so the key light does not double-count" - neither half knew the
    # other existed, so the fell shipped at an effective albedo of 0.074/0.155/0.060. That is
    # near-black, and it is exactly what the QA pass reported as "flat monotone terrain colour".
    # The key light is 1.06 intensity; it does not multiply by four. The texture now carries the
    # intended read and the material tint is neutral. Changing one without the other re-breaks it.
    base = TURF_GREEN * 0.82 * 0.72 + olive * 0.36
    rich = TURF_GREEN * 1.16 * 0.74 + olive * 0.30
    albedo = base + (rich - base) * broad[..., None]
    # Wider value swing than the first pass (0.88-1.12 was invisible): broad patches of grazing
    # are the largest-scale detail the eye gets on an open hillside.
    albedo *= (0.87 + 0.27 * fine)[..., None]   # amplitude cut: see MOIRE note in _fbm

    # MACRO: the single largest-amplitude term in the map, and the reason it exists.
    #
    # SakuraTerrain.shader breaks up its tiling with ONE tap - `TriSample(_GrassTex, ...,
    # 190.0).g` - i.e. it re-samples this very albedo's GREEN channel at a 190 m tile and uses it
    # as a wide tint break. Azora's turf green had a standard deviation of 0.045, so that tap
    # returned a near-constant and _MacroVariation 0.42 did precisely nothing: the tile repeat
    # was the only structure left in the frame. Giving the green channel real low-frequency
    # contrast is what turns that existing shader feature back on, WITHOUT touching a shader that
    # five other regions share.
    albedo *= (0.70 + 0.62 * macro)[..., None]
    # Extra GREEN-ONLY swing on top, because the macro tap reads .g and nothing else. This is
    # what actually decides how much tile break-up the whole 24 km fell gets.
    albedo[..., 1] *= (0.80 + 0.42 * macro)

    # Clover and finer fescue: slightly bluer, slightly lighter.
    clover = np.clip((_fbm(n, 3, 16, rng) - 0.55) * 4.0, 0, 1)
    albedo += (np.array([0.02, 0.07, 0.04]) * clover[..., None])

    # Bare scuffs where sheep have worn the turf through to soil.
    #
    # COLOUR, learned from the first render: this was a dark warm brown (0.286, 0.243, 0.196).
    # Against a saturated green field, a dark warm patch does not read as soil - simultaneous
    # contrast pushes it hard towards RED, and the turf came back speckled with maroon blotches
    # that looked like damage. The fix is a soil that is barely warmer and barely darker than
    # the grass around it, which is what worn upland turf actually looks like.
    scuff = np.clip((_fbm(n, 3, 12, rng) - 0.70) * 5.0, 0, 1) * 0.65
    soil = np.array([0.372, 0.338, 0.258])
    albedo = albedo * (1 - scuff[..., None]) + soil * scuff[..., None]

    # Blades: WIDTH 2, because at a 1024 map on a ~5 m tile a single-pixel blade is well under
    # one screen pixel at any riding distance and simply averages away into flat colour.
    blades = _grass_blades(n, rng, count=30000, length=8, lean=0.55, width=2)
    albedo *= (0.93 + 0.14 * blades)[..., None]  # amplitude cut: see MOIRE note in _fbm

    _save(albedo, "Azora_Turf_Albedo.png")
    height = fine * 0.5 + blades * 0.5 - scuff * 0.3 + macro * 0.25
    _save(_normal_from_height(height, 5.0), "Azora_Turf_Normal.png")


def build_dry_grass():
    """
    The wind-scoured tops: tussocky golden grass from the pasture false-flat upward.

    Same construction as the turf so the two blend without a seam, but the blades are LONGER and
    lean harder (nothing shelters them up here) and the colour runs to straw with only a little
    green surviving in the hollows.
    """
    n = 1024
    rng = np.random.default_rng(SEED + 7)

    macro = _fbm(n, 2, 2, rng)
    broad = _fbm(n, 4, 4, rng)
    fine = _fbm(n, 4, 28, rng)

    # AUTHORED AT THE READ VALUE. Same correction as build_turf: this map used to be authored at
    # 0.55 x DRY_GOLD and then multiplied AGAIN by the material's _ScreeColor (0.520, 0.462,
    # 0.300), for an effective albedo of 0.191/0.148/0.051 - a dark red-brown. The design calls
    # for golden #d9b24a on the wind-scoured tops, and the col panorama (the shot this region is
    # named for) was rendering as a mud plain. The material tint is now neutral.
    base = DRY_GOLD * 0.86
    pale = DRY_GOLD * 1.10
    albedo = base + (pale - base) * broad[..., None]
    # Wider value swing, for the same reason the turf needed one.
    albedo *= (0.87 + 0.26 * fine)[..., None]   # amplitude cut: see MOIRE note in _fbm

    # MACRO. See build_turf - the shader's 190 m tile-break tap reads the GRASS map's green
    # channel, but the dry grass needs its own low-frequency swing too or the tops read as one
    # flat gold wash even once the macro break is working on the turf underneath.
    albedo *= (0.72 + 0.58 * macro)[..., None]

    # Green survives in the damp hollows - without this the tops read as desert, and the design
    # explicitly wants "gold ABOVE green", a gradient, not a different biome.
    damp = np.clip((_fbm(n, 3, 10, rng) - 0.52) * 3.4, 0, 1)
    albedo = albedo * (1 - damp[..., None] * 0.6) + (TURF_GREEN * 0.92) * (damp[..., None] * 0.6)

    # Tussocks: dense clumps with bare stony ground between them.
    #
    # FLOOR RAISED 0.55 -> 0.74. At 0.55 the gaps between tussocks were a 45% darkening applied
    # on top of every other multiplier in this function, and since the tussock mask averages
    # about a third, it was quietly taking a fifth off the whole map's value. The tops are meant
    # to be BRIGHT - they are the most exposed ground in the region.
    tussock = np.clip((_fbm(n, 3, 8, rng) - 0.44) * 3.0, 0, 1)
    grit = np.array([0.610, 0.586, 0.536])
    albedo = albedo * (0.85 + 0.16 * tussock[..., None]) + grit * ((1 - tussock) * 0.12)[..., None]

    blades = _grass_blades(n, rng, count=22000, length=14, lean=1.5, width=2)
    albedo *= (0.93 + 0.15 * blades)[..., None]  # amplitude cut: see MOIRE note in _fbm

    _save(albedo, "Azora_DryGrass_Albedo.png")
    height = tussock * 0.6 + blades * 0.6 + fine * 0.2
    _save(_normal_from_height(height, 6.0), "Azora_DryGrass_Normal.png")


# --------------------------------------------------------------------------- dry stone

def build_stone():
    """
    Coursed dry-stone rubble: the region's structural signature.

    Authored as real COURSES (roughly horizontal bands of irregular stones) rather than as a
    random voronoi rubble, because that is what actually identifies a dry-stone wall at a
    distance - the horizontal banding survives when the individual stones no longer resolve, and
    over 24 km most of these walls are seen from a long way off.

    The deep mortar-less joints are pure black in the height field. That is the whole reason the
    wall ribbon can be flat geometry and still look built: the normal map does the relief, and a
    dry-stone wall's defining feature is the SHADOW in its gaps.
    """
    n = 1024
    rng = np.random.default_rng(SEED + 13)

    COURSE_H = 46           # pixels per course; ~ 6 courses per metre at the wall's tiling
    height = np.zeros((n, n))
    stone_id = np.zeros((n, n), dtype=int)
    next_id = 1

    y = 0
    while y < n:
        h = COURSE_H + int(rng.integers(-10, 11))
        x = 0
        # Offset each course so joints never line up vertically - a stacked joint instantly
        # reads as brickwork or, worse, as a texture that failed to randomise.
        x_off = int(rng.integers(0, n))
        while x < n:
            w = int(rng.integers(34, 96))
            ys, ye = y, min(y + h, n)
            for yy in range(ys, ye):
                for xx in range(x, x + w):
                    height[yy % n, (xx + x_off) % n] = 1.0
                    stone_id[yy % n, (xx + x_off) % n] = next_id
            # The joint: a 3-5 px gap left at zero height.
            x += w + int(rng.integers(3, 6))
            next_id += 1
        y += h + int(rng.integers(3, 6))

    # Round each stone's face slightly so it is a weathered cobble, not a tile.
    soften = _fbm(n, 4, 64, rng)
    height = height * (0.70 + 0.30 * soften)
    # Blur the joints just enough that the normal map has a ramp to shade rather than a cliff.
    for _ in range(2):
        height = (height * 2 + np.roll(height, 1, 0) + np.roll(height, -1, 0) +
                  np.roll(height, 1, 1) + np.roll(height, -1, 1)) / 6.0

    # Per-stone colour: limestone runs from pale bone to a darker weathered grey, and each stone
    # is a different one. Looking the tint up by stone_id is what keeps a single stone uniform.
    tint = rng.random(next_id + 1) * 0.42 + 0.79
    per_stone = tint[stone_id]
    albedo = STONE_GREY[None, None, :] * per_stone[..., None]

    # Lichen: the pale yellow-green crust that covers the sun-facing side of every upland wall.
    # Kept subtle and patchy - it is a detail that says "old" without saying "mouldy".
    lichen = np.clip((_fbm(n, 3, 20, rng) - 0.60) * 4.0, 0, 1) * 0.55
    lichen_col = np.array([0.702, 0.733, 0.541])
    albedo = albedo * (1 - lichen[..., None]) + lichen_col * lichen[..., None]

    # Darken the joints in the ALBEDO too, not just the normal. Ambient occlusion baked into the
    # colour is what sells depth on a flat ribbon under a cel shader, which has no AO of its own.
    joint = np.clip(height * 2.4, 0, 1)
    albedo *= (0.34 + 0.66 * joint)[..., None]

    _save(albedo, "Azora_Stone_Albedo.png")
    _save(_normal_from_height(height, 9.0), "Azora_Stone_Normal.png")


# --------------------------------------------------------------------------- seed VFX

def _radial(n, cx, cy, r):
    yy, xx = np.mgrid[0:n, 0:n]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
    return np.clip(1.0 - d, 0, 1)


def _dandelion(n, rng):
    """A dandelion clock: a dense core with ~22 fine filaments radiating out."""
    a = np.zeros((n, n))
    c = n * 0.5
    a += _radial(n, c, c, n * 0.055) ** 0.6 * 0.95
    for k in range(22):
        ang = k / 22.0 * 2 * np.pi + rng.random() * 0.12
        L = n * (0.32 + rng.random() * 0.10)
        for s in range(int(L)):
            t = s / L
            x = int(c + np.cos(ang) * s)
            y = int(c + np.sin(ang) * s)
            if 0 <= x < n and 0 <= y < n:
                a[y, x] = max(a[y, x], 0.85 * (1 - t * 0.5))
        # The little parachute tuft at the filament tip.
        x = int(c + np.cos(ang) * L)
        y = int(c + np.sin(ang) * L)
        if 2 < x < n - 2 and 2 < y < n - 2:
            a[y - 2:y + 2, x - 2:x + 2] = np.maximum(a[y - 2:y + 2, x - 2:x + 2], 0.7)
    for _ in range(2):
        a = (a * 3 + np.roll(a, 1, 0) + np.roll(a, -1, 0) +
             np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 7.0
    return np.clip(a * 1.6, 0, 1)


def _seed_head(n, rng):
    """A grass seed head: a narrow leaning ellipse of packed grains."""
    yy, xx = np.mgrid[0:n, 0:n]
    c = n * 0.5
    # Shear so it leans, like a seed head caught in the wind.
    sx = (xx - c) - (yy - c) * 0.35
    sy = (yy - c)
    d = np.sqrt((sx / (n * 0.10)) ** 2 + (sy / (n * 0.34)) ** 2)
    a = np.clip(1.4 - d, 0, 1) ** 0.7
    grain = _fbm(n, 3, 26, rng)
    return np.clip(a * (0.55 + 0.75 * grain), 0, 1)


def _petal(n, rng):
    """A small wildflower petal: a rounded teardrop."""
    yy, xx = np.mgrid[0:n, 0:n]
    c = n * 0.5
    d = np.sqrt(((xx - c) / (n * 0.24)) ** 2 + ((yy - c) / (n * 0.32)) ** 2)
    a = np.clip(1.25 - d, 0, 1) ** 0.8
    # Taper one end so it is a petal rather than a pill.
    a *= np.clip((yy - n * 0.14) / (n * 0.28), 0, 1) ** 0.5 + 0.25
    return np.clip(a, 0, 1)


def _thistle(n, rng):
    """Thistledown: a loose, soft, near-circular puff."""
    a = np.zeros((n, n))
    c = n * 0.5
    for k in range(34):
        ang = rng.random() * 2 * np.pi
        r = n * (0.08 + rng.random() * 0.26)
        x, y = c + np.cos(ang) * r, c + np.sin(ang) * r
        a = np.maximum(a, _radial(n, x, y, n * 0.075) ** 1.4 * (0.5 + rng.random() * 0.5))
    a = np.maximum(a, _radial(n, c, c, n * 0.09) ** 0.8)
    for _ in range(2):
        a = (a * 2 + np.roll(a, 2, 0) + np.roll(a, -2, 0) +
             np.roll(a, 2, 1) + np.roll(a, -2, 1)) / 6.0
    return np.clip(a * 1.5, 0, 1)


# The four drift variants.
#
# COLOUR RULE, and it is not cosmetic: every variant's GREEN channel must sit at or above its
# BLUE channel. Maple City shipped a leaf variant at (0.851, 0.325, 0.431) - blue above green -
# and once it bloomed the render came back with what read unmistakably as cherry blossom drifting
# through an autumn city. Each region's signature VFX is supposed to be its OWN, so the
# constraint is enforced by the assertion at the bottom of build_seed() rather than by care.
SEED_VARIANTS = [
    ("dandelion", np.array([0.961, 0.949, 0.906]), 0.92),  # cream-white clock
    ("seedhead", np.array([0.882, 0.792, 0.549]), 0.88),   # straw grass seed
    ("thistle", np.array([0.925, 0.918, 0.878]), 0.78),    # pale thistledown
    # The wildflower petal, and a deliberate DEPARTURE from the doc's palette entry.
    #
    # The doc's wildflower is knapweed purple (#8b5fd0), and the assertion below rejected it on
    # the first run: purple is blue-above-green by definition, so airborne purple petals bloom
    # towards pink and Azora would have picked up the one read it must never have. The purple
    # is NOT lost - it stays where it belongs, in the static wildflower scatter on the turf,
    # where it is seen against green ground at ground level and cannot be confused with drifting
    # blossom. What DRIFTS is a pale yellow upland floret (tormentil / lady's bedstraw), which
    # is botanically at home at 1,500 m and reads as "meadow", not "cherry tree".
    ("petal", np.array([0.902, 0.855, 0.612]), 0.90),
]


def build_seed():
    """The 2x2 RGBA drift atlas: dandelion clock, grass seed head, thistledown, wildflower petal."""
    cell = 256
    n = cell * 2
    rng = np.random.default_rng(SEED + 23)
    atlas = np.zeros((n, n, 4))

    makers = {
        "dandelion": _dandelion,
        "seedhead": _seed_head,
        "thistle": _thistle,
        "petal": _petal,
    }

    # NOTE ON CELL ORDER: Unity's texture-sheet animation indexes cells LEFT-TO-RIGHT,
    # TOP-TO-BOTTOM, but a PNG's row 0 is the TOP row while Unity's UV v=0 is the BOTTOM. The
    # order only matters for which variant is "frame 0", and the emitter randomises the start
    # frame over the whole sheet, so all four are equally likely either way.
    for idx, (kind, colour, alpha_gain) in enumerate(SEED_VARIANTS):
        a = makers[kind](cell, rng) * alpha_gain
        # Slight per-variant brightness mottle so a hundred on screen are not identical.
        mottle = 0.90 + 0.18 * _fbm(cell, 3, 8, rng)
        rgb = colour[None, None, :] * mottle[..., None]
        r, c = idx // 2, idx % 2
        atlas[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell, :3] = rgb
        atlas[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell, 3] = a

    # Enforce the no-blossom rule. Asserted, not trusted: this failed silently once already and
    # cost a full build-and-capture cycle plus a pixel-measurement session to find.
    for kind, colour, _ in SEED_VARIANTS:
        g, b = colour[1], colour[2]
        assert g >= b - 1e-6, (
            f"Azora seed variant '{kind}' has blue ({b:.3f}) above green ({g:.3f}). "
            "Once bloomed it will read as a cherry blossom and the region will look like "
            "Sakura Pass. Pick a cream/straw/lilac-grey instead.")

    _save(atlas, "Azora_Seed_Sprite.png")
    print("[azora-tex] seed atlas variants: " +
          ", ".join(k for k, _, _ in SEED_VARIANTS) + " (all green >= blue: no blossom read)")


def build_tuft():
    """
    The 2x2 RGBA ground-flora atlas: grass tussock, purple wildflower clump, thistle, bracken.

    These are the STATIC scatter billboards planted along the verges, and they are where the
    design's #8b5fd0 knapweed purple actually lives. Seen at ground level against green turf,
    a purple flower head is unmistakably a flower; the same colour AIRBORNE would have read as
    cherry blossom, which is why the drift atlas above had to give it up.

    Alpha is a hard-ish cutout rather than a soft fade: these are rendered by the foliage shader
    with alpha testing, and a soft edge on a cutout shader produces a halo of half-transparent
    fringe that reads as fog clinging to every plant.
    """
    cell = 256
    n = cell * 2
    rng = np.random.default_rng(SEED + 31)
    atlas = np.zeros((n, n, 4))

    def tussock(colour_lo, colour_hi, count, length, lean, spread):
        a = np.zeros((cell, cell))
        col = np.zeros((cell, cell, 3))
        for _ in range(count):
            x0 = cell * 0.5 + (rng.random() - 0.5) * cell * spread
            L = length * (0.55 + rng.random() * 0.75)
            dx = (x0 - cell * 0.5) / (cell * 0.5) * lean + (rng.random() - 0.5) * 0.4
            shade = rng.random()
            c = colour_lo + (colour_hi - colour_lo) * shade
            for s in range(int(L)):
                t = s / max(L, 1.0)
                y = int(cell - 1 - s)
                x = int(x0 + dx * s * 6.0)
                if 0 <= x < cell and 0 <= y < cell:
                    w = 2 if t < 0.6 else 1
                    for k in range(w):
                        xx = min(cell - 1, x + k)
                        a[y, xx] = 1.0
                        col[y, xx] = c
        return a, col

    GRASS_LO = np.array([0.204, 0.376, 0.208])
    GRASS_HI = np.array([0.451, 0.608, 0.322])

    # 1. Grass tussock.
    a1, c1 = tussock(GRASS_LO, GRASS_HI, 150, cell * 0.72, 0.22, 0.42)

    # 2. Wildflower clump: grass with knapweed-purple heads on top.
    a2, c2 = tussock(GRASS_LO, GRASS_HI, 110, cell * 0.60, 0.18, 0.46)
    for _ in range(26):
        x = int(cell * 0.5 + (rng.random() - 0.5) * cell * 0.5)
        y = int(cell * (0.18 + rng.random() * 0.30))
        r = int(4 + rng.random() * 4)
        tint = WILDFLOWER * (0.72 + rng.random() * 0.42)
        for yy in range(max(0, y - r), min(cell, y + r)):
            for xx in range(max(0, x - r), min(cell, x + r)):
                if (xx - x) ** 2 + (yy - y) ** 2 <= r * r:
                    a2[yy, xx] = 1.0
                    c2[yy, xx] = tint

    # 3. Thistle: taller, greyer, with a lilac crown.
    a3, c3 = tussock(np.array([0.353, 0.408, 0.310]), np.array([0.545, 0.584, 0.463]),
                     70, cell * 0.80, 0.10, 0.26)
    for _ in range(9):
        x = int(cell * 0.5 + (rng.random() - 0.5) * cell * 0.26)
        y = int(cell * (0.10 + rng.random() * 0.18))
        r = int(6 + rng.random() * 4)
        for yy in range(max(0, y - r), min(cell, y + r)):
            for xx in range(max(0, x - r), min(cell, x + r)):
                if (xx - x) ** 2 + (yy - y) ** 2 <= r * r:
                    a3[yy, xx] = 1.0
                    c3[yy, xx] = np.array([0.678, 0.549, 0.749]) * (0.8 + rng.random() * 0.3)

    # 4. Bracken: broad, rusty, low - the one warm note on the hillside.
    a4, c4 = tussock(np.array([0.478, 0.373, 0.216]), np.array([0.702, 0.541, 0.271]),
                     130, cell * 0.52, 0.55, 0.62)

    for idx, (a, c) in enumerate([(a1, c1), (a2, c2), (a3, c3), (a4, c4)]):
        # RGB DILATION UNDER TRANSPARENT PIXELS.
        #
        # Every pixel with alpha 0 currently carries RGB (0,0,0). That is invisible at mip 0 and
        # a disaster from about 40 m out: mip generation averages colour WITHOUT regard to alpha,
        # so a tuft that is 85% transparent averages down toward black long before its alpha
        # falls below the cutout threshold. The verge scrub then reads as a line of dark bushes
        # against the fell - which is exactly what the first Azora render showed.
        #
        # Unity's alphaIsTransparency importer flag does this dilation on import, and
        # SakuraTextureImportSettings asks for it - but that helper only ever walked the Sakura
        # and Shiosai folders, so Azora's atlas never received it (see the Dirs() fix). The flag
        # is fixed now; this bakes the correct colour into the source PNG as well so the asset is
        # right regardless of import path.
        lit = a > 0.01
        if lit.any():
            fill = c[lit].mean(axis=0)
            c = np.where(lit[..., None], c, fill[None, None, :])
        r, cc = idx // 2, idx % 2
        atlas[r * cell:(r + 1) * cell, cc * cell:(cc + 1) * cell, :3] = c
        atlas[r * cell:(r + 1) * cell, cc * cell:(cc + 1) * cell, 3] = a

    _save(atlas, "Azora_Tuft_Sprite.png")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    build_turf()
    build_dry_grass()
    build_stone()
    build_seed()
    build_tuft()
    print("[azora-tex] done.")

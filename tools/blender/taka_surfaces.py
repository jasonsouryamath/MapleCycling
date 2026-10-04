# -*- coding: utf-8 -*-
"""
Surface textures for TAKA MOUNTAINS - "The High Road".

Sibling of azora_surfaces.py, and it imports that module's helpers rather than copying them:
the fBm, the height-to-normal conversion and the save path logic were all debugged once and
there is no reason for a second, subtly different copy to exist.

What is NOT shared is the palette, and that is the whole point of the file. Azora is emerald
turf and golden dry grass; Taka is above the treeline and has no vegetation at all worth the
name. Every texture here is grey, white or ice-blue, straight from design doc section 3:

    cold granite grey   #6a6f77     bare rock, the region's dominant surface
    dark rock-shadow    #2b3038     the deep tone inside cracks and strata
    snow white          #f2f6fa     snowfields and summit dusting
    ice blue            #7fbfe0     shaded ice, the tarn edge
    pale glacier        #bfe0ea     lake ice
    asphalt             #585c63     the near-black road ribbon

TWO THINGS THIS FILE DELIBERATELY GUARDS AGAINST, both learned the expensive way on Azora:

  1. AUTHORED ALBEDO IS NOT SCREEN VALUE. A texture authored AT the palette's target brightness
     double-counts the light, because the palette describes what the surface should READ as once
     lit. Snow is the worst case in the project: authored at #f2f6fa it clips to flat white paper
     under any key light at all. So the snow albedo here peaks around 0.86, not 0.96, and the
     region's exposure (0.70, the lowest in the game) does the rest.

  2. A SPRITE ATLAS MUST NOT ALIAS ANOTHER REGION'S SIGNATURE. Azora's drift atlas originally
     carried a purple wildflower that read as a Sakura petal at distance. Taka's spindrift is
     therefore pure achromatic white-to-ice-blue streaks with NO round blobs: a round pale
     particle is a petal or a seed head, a STREAK is wind-driven snow. The assertion at the
     bottom enforces it.

Run:  python tools/blender/taka_surfaces.py
"""
import importlib.util
import os

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("azora_surfaces",
                                               os.path.join(_HERE, "azora_surfaces.py"))
_az = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_az)

_fbm = _az._fbm
_normal_from_height = _az._normal_from_height
_radial = _az._radial

SEED = 20260930
SIZE = 1024

OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(_HERE)),
                       "Assets", "Environment", "TakaMountains", "Textures")

GRANITE = np.array([0.416, 0.435, 0.467])    # #6a6f77
ROCK_SHADOW = np.array([0.169, 0.188, 0.220])  # #2b3038
SNOW = np.array([0.949, 0.965, 0.980])       # #f2f6fa  (authored DOWN - see header note 1)
ICE_BLUE = np.array([0.498, 0.749, 0.878])   # #7fbfe0
GLACIER = np.array([0.749, 0.878, 0.918])    # #bfe0ea
CONCRETE = np.array([0.560, 0.556, 0.540])


def _rng():
    return np.random.default_rng(SEED)


def _save(arr, name):
    from PIL import Image
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    a = np.clip(arr, 0.0, 1.0)
    mode = "RGBA" if a.shape[2] == 4 else "RGB"
    Image.fromarray((a * 255.0 + 0.5).astype(np.uint8), mode).save(path, optimize=True)
    print("[taka] %-28s %s  mean %s" % (name, a.shape[:2], np.round(a[..., :3].reshape(-1, 3).mean(0), 3)))


# --------------------------------------------------------------------------- granite

def build_granite():
    """
    Bare granite with STRATA. The strata are the reason this is not just noise: a mountain wall
    without bedding planes reads as a pile of gravel rather than as cut rock, and the strata are
    what make a 400 m rock face legible as one solid mass at a distance.
    """
    rng = _rng()
    n = SIZE
    grain = _fbm(n, 6, 5.0, rng)
    coarse = _fbm(n, 4, 2.0, rng)

    # Bedding planes: a low-frequency banding along one axis, warped by noise so they are not
    # ruler-straight, and sharpened into narrow dark seams rather than a soft gradient.
    y = np.linspace(0.0, 1.0, n)[:, None] * np.ones((1, n))
    warp = (_fbm(n, 4, 3.0, rng) - 0.5) * 0.26
    bands = np.sin((y + warp) * np.pi * 2.0 * 4.0)
    # Widened and softened (3.2 -> 1.9 sharpen, 1.8 -> 2.6 falloff) so the bedding reads as a
    # tonal band rather than a drawn line: sharp seams plus sharp cracks made a brick grid.
    seams = np.clip(1.0 - np.abs(bands) * 1.9, 0.0, 1.0) ** 2.6

    # Fracture cracks: thin dark lines from a second, higher-frequency ridged field.
    ridged = 1.0 - np.abs(_fbm(n, 5, 7.0, rng) * 2.0 - 1.0)
    cracks = np.clip(ridged * 1.15 - 0.955, 0.0, 1.0) * 3.0
    cracks = np.clip(cracks, 0.0, 1.0)

    # Base tone sits HIGH (0.88 of palette) rather than mid: the seams and cracks below subtract
    # a great deal of energy, and a first pass authored at 0.62 landed the tile mean at 0.258 -
    # 62% of the palette's #6a6f77 - which is a wall that reads black-grey in shadow and dull
    # even in full sun. Lit granite wants to sit just under its palette value, not half of it.
    tone = 0.88 + (grain - 0.5) * 0.26 + (coarse - 0.5) * 0.30
    albedo = GRANITE[None, None, :] * tone[..., None]
    albedo = albedo * (1.0 - 0.30 * seams[..., None]) + ROCK_SHADOW[None, None, :] * 0.30 * seams[..., None]
    albedo = albedo * (1.0 - 0.58 * cracks[..., None]) + ROCK_SHADOW[None, None, :] * 0.58 * cracks[..., None]

    # Orange/grey lichen crusting - the design's ONLY concession to living things on the rock.
    # Kept under 8% coverage and low saturation: any more and the wall goes rusty.
    lich = _fbm(n, 5, 9.0, rng)
    patch = np.clip((lich - 0.66) * 6.0, 0.0, 1.0) * 0.55
    lichen_col = np.array([0.612, 0.478, 0.267])
    albedo = albedo * (1.0 - patch[..., None]) + lichen_col[None, None, :] * patch[..., None]

    height = tone * 0.5 + (1.0 - seams) * 0.28 + (1.0 - cracks) * 0.22
    _save(albedo, "Taka_Granite_Albedo.png")
    _save(_normal_from_height(height, 11.0), "Taka_Granite_Normal.png")
    return float(patch.mean())


# --------------------------------------------------------------------------- snow

def build_snow():
    """
    Wind-packed snowfield with SASTRUGI - the hard wind-carved ridges that form on exposed high
    snow. Without them a snowfield is a flat white polygon and the eye has nothing to hold, which
    is exactly how a snow region ends up looking like unlit paper.

    Authored deliberately DARK of the palette (see header note 1): mean lands near 0.80, and the
    region's low exposure lifts it to read as #f2f6fa on screen.
    """
    rng = _rng()
    n = SIZE
    fine = _fbm(n, 6, 8.0, rng)

    # Sastrugi: stretched ridges along the prevailing wind. Anisotropic on purpose - sampling a
    # NON-stretched noise here gives dunes, which is a desert, not a mountain.
    #
    # BUT anisotropy alone is not enough. A first pass used a clean sin() at frequency 14 with a
    # single warp and rendered as WOOD GRAIN: evenly spaced parallel stripes running the full
    # height of the tile with no beginning or end. Real sastrugi are SHORT, overlapping, broken
    # scoops. Three changes fix it: a much lower base frequency, a second cross-axis modulation
    # that chops the ridges into finite lengths, and a broken-ness mask that dissolves the
    # pattern entirely over roughly a third of the tile so there are calm packed patches too.
    xs = np.linspace(0.0, 1.0, n)[None, :] * np.ones((n, 1))
    ys = np.linspace(0.0, 1.0, n)[:, None] * np.ones((1, n))
    drift = _fbm(n, 4, 2.0, rng)
    cross = _fbm(n, 4, 4.0, rng)
    ridge = np.sin((xs * 5.0 + drift * 3.0 + ys * 0.6) * np.pi * 2.0)
    # chop into finite scoops: a second low-frequency field gates ridge amplitude along the wind
    chop = np.clip((np.sin((ys * 3.2 + cross * 2.6) * np.pi * 2.0) + 0.4) * 1.1, 0.0, 1.0)
    broken = np.clip((_fbm(n, 3, 1.8, rng) - 0.34) * 3.0, 0.0, 1.0)
    sastrugi = np.clip(np.abs(ridge) ** 0.8 * chop * broken, 0.0, 1.0)

    # Sparkle: isolated bright specks, a few per thousand pixels. This is the "glint" the design
    # asks for and it survives mipping better than a specular trick.
    sparkle = (rng.random((n, n)) > 0.9975).astype(float)

    tone = 0.87 + (fine - 0.5) * 0.10 + (sastrugi - 0.5) * 0.09
    albedo = SNOW[None, None, :] * tone[..., None]
    # Blue ambient in the troughs. Kept to 0.18 rather than 0.30: with the ridges now broken and
    # short, a heavy blue in every trough turned the tile into a blue-and-white striped fabric.
    shade = np.clip(1.0 - sastrugi, 0.0, 1.0) ** 2.0
    albedo = albedo * (1.0 - 0.18 * shade[..., None]) + ICE_BLUE[None, None, :] * 0.18 * shade[..., None]
    albedo = np.clip(albedo + sparkle[..., None] * 0.22, 0.0, 1.0)

    height = sastrugi * 0.7 + fine * 0.3
    _save(albedo, "Taka_Snow_Albedo.png")
    _save(_normal_from_height(height, 6.0), "Taka_Snow_Normal.png")


# --------------------------------------------------------------------------- lake ice

def build_ice():
    """Kori Lake's frozen surface: translucent ice-blue with crevasse/pressure-ridge detail."""
    rng = _rng()
    n = SIZE
    deep = _fbm(n, 4, 2.2, rng)
    ridged = 1.0 - np.abs(_fbm(n, 5, 5.0, rng) * 2.0 - 1.0)
    cracks = np.clip(ridged * 1.2 - 0.88, 0.0, 1.0) * 3.5
    cracks = np.clip(cracks, 0.0, 1.0)

    albedo = (GLACIER[None, None, :] * (0.80 + (deep - 0.5) * 0.30)[..., None])
    # Cracks go WHITE, not dark: a fracture in lake ice is full of shattered crystal and is the
    # brightest thing on the surface. Drawing them dark is the classic tell of faked ice.
    albedo = albedo * (1.0 - cracks[..., None]) + np.array([0.94, 0.97, 0.99])[None, None, :] * cracks[..., None]
    # Deep water shows through in patches.
    win = np.clip((deep - 0.62) * 5.0, 0.0, 1.0) * 0.5
    albedo = albedo * (1.0 - win[..., None]) + (ICE_BLUE * 0.62)[None, None, :] * win[..., None]

    height = deep * 0.6 + cracks * 0.4
    _save(albedo, "Taka_Ice_Albedo.png")
    _save(_normal_from_height(height, 4.0), "Taka_Ice_Normal.png")


# --------------------------------------------------------------------------- concrete

def build_concrete():
    """Avalanche-gallery / tunnel concrete: board-formed, stained, cold."""
    rng = _rng()
    n = SIZE
    grain = _fbm(n, 6, 6.0, rng)
    stain = _fbm(n, 3, 1.6, rng)

    # Board-form lines: horizontal shuttering marks every ~1/12 of the tile.
    y = np.linspace(0.0, 1.0, n)[:, None] * np.ones((1, n))
    boards = np.clip(1.0 - np.abs(np.sin(y * np.pi * 12.0)) * 5.0, 0.0, 1.0)

    tone = 0.80 + (grain - 0.5) * 0.18 + (stain - 0.5) * 0.30
    albedo = CONCRETE[None, None, :] * tone[..., None]
    albedo = albedo * (1.0 - 0.35 * boards[..., None]) + ROCK_SHADOW[None, None, :] * 0.35 * boards[..., None]

    height = tone * 0.7 + (1.0 - boards) * 0.3
    _save(albedo, "Taka_Concrete_Albedo.png")
    _save(_normal_from_height(height, 7.0), "Taka_Concrete_Normal.png")


# --------------------------------------------------------------------------- spindrift

def _streak(n, rng, angle_deg, length, width, bright):
    """One wind-driven snow streak: a soft anisotropic smear, never a round blob."""
    a = np.zeros((n, n))
    cx, cy = n * 0.5, n * 0.5
    th = np.radians(angle_deg)
    ys, xs = np.mgrid[0:n, 0:n]
    dx, dy = (xs - cx) / n, (ys - cy) / n
    # rotate into streak space
    u = dx * np.cos(th) + dy * np.sin(th)
    v = -dx * np.sin(th) + dy * np.cos(th)
    a = np.exp(-(u / length) ** 2 * 4.0) * np.exp(-(v / width) ** 2 * 4.0)
    # break it up so it is a flurry, not an airbrush stroke
    a *= 0.55 + 0.45 * _fbm(n, 4, 12.0, rng)
    return np.clip(a * bright, 0.0, 1.0)


def build_spindrift():
    """
    The region's SIGNATURE VFX atlas: 2x2 cells of wind-driven snow.

    Every cell is a STREAK, never a disc. Azora's first drift atlas shipped a round purple
    wildflower that read as a Sakura petal at any distance over ten metres, and the same trap is
    waiting here: a small round white particle is a blossom. Wind-blown snow is directional, so
    the silhouette itself has to carry the direction. The assertion in main() checks it.
    """
    rng = _rng()
    c = 256
    atlas = np.zeros((c * 2, c * 2, 4))
    specs = [
        (18.0, 0.42, 0.055, 1.00),   # long fast streak
        (8.0, 0.26, 0.085, 0.86),    # shorter, fatter gust
        (26.0, 0.50, 0.038, 0.72),   # fine high veil
        (-6.0, 0.20, 0.10, 0.94),    # near-camera puff, still elongated
    ]
    for i, (ang, ln, wd, br) in enumerate(specs):
        a = _streak(c, rng, ang, ln, wd, br)
        cell = np.zeros((c, c, 4))
        # White core lifting toward ice-blue at the soft edges - snow in sun against sky.
        core = np.array([1.0, 1.0, 1.0])
        edge = GLACIER
        t = np.clip(a * 1.6, 0.0, 1.0)[..., None]
        cell[..., :3] = edge[None, None, :] * (1.0 - t) + core[None, None, :] * t
        cell[..., 3] = a
        atlas[(i // 2) * c:(i // 2 + 1) * c, (i % 2) * c:(i % 2 + 1) * c] = cell
    _save(atlas, "Taka_Spindrift_Sprite.png")
    return atlas


# --------------------------------------------------------------------------- lichen

def build_lichen():
    """
    2x2 atlas of the only living things above the treeline: cushion moss, crustose lichen,
    alpine saxifrage in a crack, and a tuft of dead wind-scoured sedge. Used as ground decals on
    the rock, never as the billowing canopy cards the lower regions use.
    """
    rng = _rng()
    c = 256
    atlas = np.zeros((c * 2, c * 2, 4))
    palettes = [
        (np.array([0.365, 0.408, 0.290]), 0.36),   # cushion moss, olive
        (np.array([0.706, 0.596, 0.353]), 0.30),   # crustose lichen, ochre
        (np.array([0.780, 0.800, 0.760]), 0.30),   # saxifrage, pale
        (np.array([0.510, 0.451, 0.345]), 0.28),   # dead sedge, tan
    ]
    for i, (col, radius) in enumerate(palettes):
        f = _fbm(c, 5, 7.0, rng)
        r = _radial(c, c * 0.5, c * 0.5, c * radius)
        mask = np.clip((f * 0.65 + r * 0.9) - 0.62, 0.0, 1.0) * 3.0
        mask = np.clip(mask, 0.0, 1.0)
        cell = np.zeros((c, c, 4))
        cell[..., :3] = col[None, None, :] * (0.72 + f[..., None] * 0.45)
        cell[..., 3] = mask
        atlas[(i // 2) * c:(i // 2 + 1) * c, (i % 2) * c:(i % 2 + 1) * c] = cell
    _save(atlas, "Taka_Lichen_Sprite.png")


def main():
    lichen_share = build_granite()
    build_snow()
    build_ice()
    build_concrete()
    atlas = build_spindrift()
    build_lichen()

    # ---- assertions, because "it saved" is not "it is right" -----------------------------
    #
    # 1. The spindrift must be ACHROMATIC. Any hue in it and it stops being snow.
    rgb = atlas[..., :3]
    a = atlas[..., 3]
    lit = a > 0.15
    if lit.sum() > 0:
        px = rgb[lit]
        chroma = float((px.max(axis=1) - px.min(axis=1)).mean())
        assert chroma < 0.16, "spindrift is not achromatic (mean chroma %.3f) - it will read as petals" % chroma
        print("[taka] spindrift mean chroma %.3f (achromatic, reads as snow not petals)" % chroma)

    # 2. Each spindrift cell must be ELONGATED, not round. Compare the alpha's second moments.
    c = atlas.shape[0] // 2
    for i in range(4):
        cell = atlas[(i // 2) * c:(i // 2 + 1) * c, (i % 2) * c:(i % 2 + 1) * c, 3]
        ys, xs = np.mgrid[0:c, 0:c]
        w = cell.sum()
        mx, my = (xs * cell).sum() / w, (ys * cell).sum() / w
        vx = ((xs - mx) ** 2 * cell).sum() / w
        vy = ((ys - my) ** 2 * cell).sum() / w
        ratio = max(vx, vy) / max(1e-6, min(vx, vy))
        assert ratio > 2.2, "spindrift cell %d is round (axis ratio %.2f) - that is a petal" % (i, ratio)
    print("[taka] all 4 spindrift cells are elongated streaks, not discs")

    print("[taka] lichen covers %.1f%% of the granite tile (design: 'sparse survivors only')"
          % (lichen_share * 100.0))
    print("[taka] textures written to %s" % OUT_DIR)


if __name__ == "__main__":
    main()

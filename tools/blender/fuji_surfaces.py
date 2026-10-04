# -*- coding: utf-8 -*-
"""
Surface textures for TAKA MOUNTAINS - "The High Road".

Sibling of azora_surfaces.py, and it imports that module's helpers rather than copying them:
the fBm, the height-to-normal conversion and the save path logic were all debugged once and
there is no reason for a second, subtly different copy to exist.

What is NOT shared is the palette, and that is the whole point of the file. Azora is emerald
turf and golden dry grass; Fuji's vegetation ENDS at the cloud line by design, and above it
name. Every texture here is grey, white or ice-blue, straight from design doc section 3:

    volcanic charcoal   #2b2a30     aged tarmac and black scoria, the dominant surface
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
     carried a purple wildflower that read as a Sakura petal at distance. Fuji's cloud/ash is
     therefore pure achromatic white-to-ice-blue streaks with NO round blobs: a round pale
     particle is a petal or a seed head, a STREAK is wind-driven snow. The assertion at the
     bottom enforces it.

Run:  python tools/blender/fuji_surfaces.py
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

SEED = 20261014
SIZE = 1024

OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(_HERE)),
                       "Assets", "Environment", "FujiRidge", "Textures")

# Design doc section 3, authored as LINEAR albedo. Note that every value here is authored
# BELOW its palette hex, for the reason the Taka generator records at length: the palette states
# what a surface should READ AS on screen, after the key, the ambient and the grade have all
# multiplied into it. Authoring at the target double-counts the light.
#
# Fuji is the darkest palette in the atlas by a wide margin. Black volcanic scoria genuinely
# returns almost nothing - real basalt cinder sits near 0.05-0.08 reflectance - but a surface
# that dark renders as a hole, so these are lifted to roughly 0.16-0.22 and the BLACKNESS is
# carried by the contrast against snow and cloud instead. That is the same trick the doc is
# describing when it calls the region "black rock, white cloud, and a single band of fire".
#
# THE ROCK AND CINDER ENTRIES ARE WARM-BIASED (r > g > b) ON PURPOSE, and it is not a style
# choice. FujiRidgeEnvironment.GroundShade documents the project's G-gap rule: the cel shade
# tint swings any surface whose green sits BELOW the mean of its red and blue towards mauve in
# shadow. The three near-black entries here were inherited cool (b > r > g, G-gap about -0.013)
# from the Taka copy, which is a measurable rose cast on every cinder slope in the region and
# is a large part of why Fuji rendered as a brown-mauve plain. Re-balanced at the same
# luminance so the hue changes and the brightness does not. Warm-shifted near-black is also
# what basalt cinder actually is; neutral-to-cool black is granite, i.e. Taka.
TARMAC = np.array([0.312, 0.298, 0.286])     # #2b2a30 volcanic ridge road, lifted off pure black
SCORIA = np.array([0.222, 0.208, 0.196])     # the deeper black cinder shoulder
PREDAWN = np.array([0.110, 0.137, 0.200])    # #1c2333 the pre-dawn blue that fills every shadow
CORAL = np.array([1.000, 0.478, 0.420])      # #ff7a6b the single band of sunrise fire
DAWN_CLOUD = np.array([0.965, 0.824, 0.769]) # #f6d2c4 dawn-lit cloud
PALE_CLOUD = np.array([0.957, 0.902, 0.894]) # #f4e6e4 the flat top of the cloud sea
VALLEY_HAZE = np.array([0.561, 0.722, 0.847])# #8fb8d8 cool misty blue-white below the deck
SNOW = np.array([0.930, 0.945, 0.962])       # clean snow on the upper cone (authored down)
STRATA_RED = np.array([0.412, 0.208, 0.157])  # the red band in the strata wall
STRATA_OCHRE = np.array([0.514, 0.376, 0.204])# the ochre band
STRATA_BLACK = np.array([0.140, 0.130, 0.122])# the black band
TIMBER = np.array([0.352, 0.286, 0.231])     # weathered shrine timber
VERMILION = np.array([0.776, 0.208, 0.145])  # torii lacquer



def _rng():
    return np.random.default_rng(SEED)


def _save(arr, name):
    from PIL import Image
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    a = np.clip(arr, 0.0, 1.0)
    mode = "RGBA" if a.shape[2] == 4 else "RGB"
    Image.fromarray((a * 255.0 + 0.5).astype(np.uint8), mode).save(path, optimize=True)
    print("[fuji] %-28s %s  mean %s" % (name, a.shape[:2], np.round(a[..., :3].reshape(-1, 3).mean(0), 3)))


def _directional_blur(field, axis, factor):
    """
    Elongate a field's features along `axis` (0 = rows/Y, 1 = columns/X) by `factor`, used ONLY
    by build_snow() to turn isotropic noise into wind-stretched streaks.

    THIS REPLACES A raw sin()-WARP ANISOTROPY, and the reason is worth recording. The first
    attempt at directional noise built it the way _fbm's own lattice trick suggests: sample two
    lattices at DIFFERENT frequencies per axis (few cells along the stretch axis, many across
    it). That produces exactly the axis-aligned rectangular seams the lattice-and-box-blur
    method is prone to at low cell counts, and at kilometre tiling scale the seams line up into
    a second, only slightly less regular grid than the sin() wave this whole fix removes - a
    different bug wearing the same symptom.

    This does the elongation by DOWNSAMPLING then upsampling along one axis only (bilinear, via
    PIL, since that dependency is already load-bearing for every _save() call in this file). A
    directional low-pass filter is exactly what "wind-stretched" means: full detail is kept
    across the ridge, and detail is smeared away along it, with no periodic structure introduced
    anywhere, because the filter has no frequency of its own - it only removes frequencies that
    were already present in truly-random input.
    """
    from PIL import Image
    h, w = field.shape
    img = Image.fromarray((np.clip(field, 0.0, 1.0) * 65535.0 + 0.5).astype(np.uint16))
    if axis == 1:
        small = img.resize((max(1, w // factor), h), Image.Resampling.BILINEAR)
        big = small.resize((w, h), Image.Resampling.BILINEAR)
    else:
        small = img.resize((w, max(1, h // factor)), Image.Resampling.BILINEAR)
        big = small.resize((w, h), Image.Resampling.BILINEAR)
    return np.asarray(big).astype(np.float64) / 65535.0


# --------------------------------------------------------------------------- tarmac

def build_tarmac():
    """
    THE VOLCANIC RIDGE ROAD (design doc section 3: "aged tarmac with macro colour variation, a
    tire-darkened centre line, cinder grit patched into the surface, cracks and frost-heave,
    drifted BLACK ASH on the shoulders and upper slopes").

    WHAT THIS REPLACES, AND WHY. The previous version of this function was TakaMountains' dry
    STONE-WALL generator with the identifiers renamed: it drew warped bedding planes, deep
    fracture seams and orange lichen crusting. Renaming a rock wall "tarmac" does not make it a
    road, and it was doing real visual damage in two places at once - it was both the road-
    adjacent stone material AND (through Fuji_Ground's _GrassTex/_SoilTex) the region's entire
    ground surface, so the bedding planes tiled across every slope and the mountain read as a
    brown BRICKWORK desert rather than a black volcanic cone. The bedding-plane recipe was not
    deleted, it MOVED: it is now build_strata(), where cut rock belongs.

    A road surface has none of that structure. It is a dense, isotropic mineral aggregate with
    no directional banding at all, so this is built from three things only: fine aggregate
    grain, a sparse scatter of brighter chipping faces, and the region-specific overlays -
    drifted ash and hairline frost cracks.

    NO DIRECTIONAL WHEEL TRACKS ARE AUTHORED HERE, deliberately. The road ribbon is swept along
    the route and its UVs run along the arc; a track painted down one texture axis would stripe
    the road ACROSS the direction of travel wherever the sweep's UV convention differs from the
    guess. The tyre-darkened centre belongs in the road builder's UV space, not in a tiling map.
    """
    rng = _rng()
    n = SIZE
    grain = _fbm(n, 6, 6.0, rng)
    coarse = _fbm(n, 4, 2.2, rng)

    # Aggregate chippings: isolated brighter mineral faces catching the light out of the binder.
    # A ridged field thresholded hard gives compact blobs rather than the connected ribbons a
    # plain fbm threshold produces.
    # 2026-09-26 REGRESSION FIX (REGION_REVIEW_TODO 7): the chippings used to be a RIDGED fbm
    # (1-|2f-1|) thresholded at 0.80. A ridged field peaks along the ZERO-CROSSING LINES of the
    # noise, i.e. along connected curves, so the "chips" were 1.85x-bright veins - the white
    # marble crackle. Chips are now isolated specks from a plain high-frequency field, and the
    # cracks/drift contrast is cut to a whisper: calm dark tarmac, like the Shiosai/Minato fix.
    speck = _fbm(n, 3, 64.0, rng)
    chips = np.clip((speck - 0.70) * 6.0, 0.0, 1.0) * 0.10

    ridged = 1.0 - np.abs(_fbm(n, 5, 7.0, rng) * 2.0 - 1.0)
    cracks = np.clip((ridged - 0.985) * 40.0, 0.0, 1.0) * 0.35

    tone = 0.74 + (grain - 0.5) * 0.24 + (coarse - 0.5) * 0.18
    albedo = TARMAC[None, None, :] * tone[..., None]

    chip_col = TARMAC * 1.18
    albedo = albedo * (1.0 - chips[..., None]) + chip_col[None, None, :] * chips[..., None]

    drift_field = _fbm(n, 4, 2.6, rng)
    drift = np.clip((drift_field - 0.58) * 1.6, 0.0, 1.0) * 0.22
    albedo = albedo * (1.0 - drift[..., None]) + (SCORIA * 0.80)[None, None, :] * drift[..., None]

    albedo = albedo * (1.0 - cracks[..., None]) + (STRATA_BLACK * 0.9)[None, None, :] * cracks[..., None]

    height = tone * 0.42 + chips * 0.20 + (1.0 - cracks) * 0.08 + drift * 0.02
    _save(albedo, "Fuji_Tarmac_Albedo.png")
    _save(_normal_from_height(height, 9.0), "Fuji_Tarmac_Normal.png")
    return float(drift.mean())


# --------------------------------------------------------------------------- cinder / scoria

def build_cinder():
    """
    BLACK SCORIA AND ASH - the dominant ground surface of the whole region above the treeline
    ("the slope turns to black volcanic cinder ... bare black scoria slopes").

    THIS IS A NEW MAP, AND IT IS THE POINT OF THE FUJI IDENTITY PASS. Fuji_Ground previously
    drove BOTH its cinder-field slot and its soil slot from the stone-wall map, which is why
    every render of the region came back as a brown plain with a visible repeating grid: the
    wall's bedding planes and fracture seams are a legible, directional, roughly rectangular
    pattern, and a legible pattern tiled over a mountainside is the definition of visible
    tiling. Cinder is the opposite kind of surface - a granular field with NO structure at any
    scale the eye can lock onto - so it tiles essentially invisibly, which is exactly what a
    ground map for a 13 km cone needs.

    Three frequency bands, no banding, no lichen, no cracks:
      * fine ash grain, at the highest frequency the tile can carry;
      * individual scoria clasts - small, near-black, slightly warm lumps;
      * a low-frequency ash-vs-clinker drift so the slope is not a uniform field.
    """
    rng = _rng()
    n = SIZE

    ash = _fbm(n, 7, 18.0, rng)          # fine ash, near-white-noise at tile scale
    clast_field = 1.0 - np.abs(_fbm(n, 6, 13.0, rng) * 2.0 - 1.0)
    clasts = np.clip((clast_field - 0.68) * 3.6, 0.0, 1.0)
    macro = _fbm(n, 4, 2.0, rng)         # ash sheets vs. coarse clinker fields

    # Cinder is authored a touch ABOVE its true reflectance (real basalt scoria is 0.05-0.08)
    # for the reason the header records: a surface that dark renders as a hole. The blackness of
    # the region is carried by contrast against the snow and the cloud deck, not by the albedo.
    tone = 0.92 + (ash - 0.5) * 0.30 + (macro - 0.5) * 0.34
    albedo = SCORIA[None, None, :] * tone[..., None]

    # Clasts: warm-shifted near-black lumps of ember stone sitting in the ash.
    clast_col = np.array([0.132, 0.118, 0.106])
    albedo = albedo * (1.0 - 0.70 * clasts[..., None]) + clast_col[None, None, :] * 0.70 * clasts[..., None]

    # A very sparse scatter of pale pumice - the only light specks on the slope. Kept under 3%:
    # more than that and the field reads as gravel path, not as volcanic cinder.
    pumice = (rng.random((n, n)) > 0.978).astype(float) * 0.62
    albedo = np.clip(albedo + pumice[..., None] * 0.34, 0.0, 1.0)

    # G-gap guard (see FujiRidgeEnvironment.GroundShade): keep g strictly between r and b so the
    # cel shade tint cannot swing the slope mauve in shadow. SCORIA and clast_col are both
    # warm-shifted (r > g > b), so this holds by construction; assert it rather than trust it.
    m = albedo.reshape(-1, 3).mean(0)
    assert m[0] > m[1] > m[2], "cinder tile lost its warm bias (mean %s)" % np.round(m, 3)

    height = ash * 0.34 + clasts * 0.52 + macro * 0.14
    _save(albedo, "Fuji_Cinder_Albedo.png")
    _save(_normal_from_height(height, 13.0), "Fuji_Cinder_Normal.png")
    return float(clasts.mean())


# --------------------------------------------------------------------------- snow

def build_snow():
    """
    Wind-packed snowfield with SASTRUGI - the hard wind-carved ridges that form on exposed high
    snow. Without them a snowfield is a flat white polygon and the eye has nothing to hold, which
    is exactly how a snow region ends up looking like unlit paper.

    Authored deliberately DARK of the palette (see header note 1): mean lands near 0.80, and the
    region's low exposure lifts it to read as #f2f6fa on screen.

    THE GRID BUG THIS REPLACES, AND WHY IT WAS SO OBVIOUS. This was the one map in the file the
    tarmac/cinder/strata identity pass never touched, and it was still built from a raw sin()
    wave: `sin((x*5 + drift*3 + y*0.6) * 2*pi)` chopped by a SECOND sin() at frequency 3.2. Two
    crossed periodic waves are precisely the textbook recipe for a Moire diamond/quatrefoil grid,
    and unlike every other map in this file it has no random phase per repeat - the pattern in
    tile copy #1 is bit-identical to tile copy #400. At `_ScreeScale = 1.6` (metres per tile) that
    repeats roughly every 1.6 m over 13+ km of upper mountain, so the diamond lattice was not a
    subtle flaw, it was the dominant thing on screen in both a static summit shot and the on-bike
    POV - confirmed by QA pixel-sampling the albedo as neutral grey/white, ruling out colour and
    leaving frequency/tiling as the only possible cause.

    THE FIX FOLLOWS THE SAME RECIPE AS build_cinder()/build_tarmac()/build_strata(): structure
    built from independent random noise LATTICES (_fbm), never from a closed-form periodic
    function, because a lattice reseeded per octave has no repeat inside the tile for a periodic
    wave to alias against. Getting the ANISOTROPY (real sastrugi are wind-stretched, not round
    blobs) from that noise without reintroducing a grid took one more step: sampling _fbm at
    unequal x/y frequencies just moves the seam problem from "wrong wave" to "wrong lattice cell
    size" and produces the same kind of axis-aligned rectangular seams. What works is building an
    ISOTROPIC ridge field first (exactly cinder's clast recipe - fbm, ridged, thresholded) and
    THEN stretching it with a one-axis directional blur (_directional_blur): a low-pass filter has
    no frequency of its own, so it cannot introduce a repeat, it can only smear the aperiodic
    noise that is already there into elongated streaks.
    """
    rng = _rng()
    n = SIZE
    fine = _fbm(n, 6, 8.0, rng)

    # Sastrugi ridge shapes: an isotropic multi-octave ridged field (same transform as cinder's
    # clasts - fbm, folded to peak at the mid-tone, thresholded) so the raw shapes are irregular
    # blobs with no imposed axis, then stretched along the wind axis by the directional blur.
    # Six octaves keep any single lattice's cell size from dominating (the tarmac/cinder/strata
    # guard against exactly this).
    ridge_base = _fbm(n, 6, 13.0, rng)
    ridge_field = 1.0 - np.abs(ridge_base * 2.0 - 1.0)
    sastrugi_blobs = np.clip((ridge_field - 0.60) * 2.8, 0.0, 1.0)
    sastrugi_streaks = _directional_blur(sastrugi_blobs, axis=1, factor=6)
    # Re-contrast after the blur, which necessarily flattens peak amplitude.
    m = float(sastrugi_streaks.mean())
    sastrugi_streaks = np.clip((sastrugi_streaks - m) * 2.6 + m * 1.4, 0.0, 1.0)

    # Chop into finite scoops and dissolve roughly a third of the tile into calm packed patches -
    # the same design intent the old sin()-chop and broken-mask had, rebuilt on smooth multi-
    # octave fbm (never the 2-4 octave, near-single-cell fields that show their own lattice edges
    # once thresholded) so neither mask contributes a seam of its own.
    chop = np.clip((_fbm(n, 6, 6.0, rng) - 0.40) * 2.2, 0.0, 1.0)
    broken = np.clip((_fbm(n, 6, 3.0, rng) - 0.40) * 2.2, 0.0, 1.0)
    sastrugi = np.clip(sastrugi_streaks * (0.55 + 0.45 * chop) * (0.50 + 0.50 * broken), 0.0, 1.0)

    # Sparkle: isolated bright specks, a few per thousand pixels. This is the "glint" the design
    # asks for and it survives mipping better than a specular trick.
    sparkle = (rng.random((n, n)) > 0.9975).astype(float)

    tone = 0.87 + (fine - 0.5) * 0.10 + (sastrugi - 0.5) * 0.09
    albedo = SNOW[None, None, :] * tone[..., None]
    # Blue/red ambient in the troughs - the ALREADY-ACCEPTED subtle strata tint, unrelated to the
    # grid bug this function fixes and deliberately left in place. Exponent raised from 2.0 to
    # 3.2: the new streaked sastrugi field has a lower mean coverage than the old sin() ridge did
    # (real wind scoops are a minority of the surface, not tiled edge-to-edge), so `1 - sastrugi`
    # now reads as "trough" over more of the tile than before. A steeper falloff keeps the tint
    # confined to genuine low pockets instead of washing the whole tile toward it, which is what
    # kept the tile mean near the design's ~0.80 target rather than drifting toward ~0.75.
    shade = np.clip(1.0 - sastrugi, 0.0, 1.0) ** 3.2
    albedo = albedo * (1.0 - 0.18 * shade[..., None]) + STRATA_RED[None, None, :] * 0.18 * shade[..., None]
    albedo = np.clip(albedo + sparkle[..., None] * 0.22, 0.0, 1.0)

    height = sastrugi * 0.7 + fine * 0.3
    _save(albedo, "Fuji_Snow_Albedo.png")
    _save(_normal_from_height(height, 6.0), "Fuji_Snow_Normal.png")


# --------------------------------------------------------------------------- banded strata

def build_strata():
    """
    THE BANDED VOLCANIC STRATA WALL (design doc: "banded volcanic strata rock (red-ochre-black
    layers)", landmark 3 "Banded Strata Wall - a cutting exposing coloured volcanic rock layers,
    red-black-ochre bands").

    WHAT THIS REPLACES. The previous version of this function drew TakaMountains' FROZEN LAKE
    ICE - pale blue slab, white pressure-ridge cracks, deep-water windows - under the new name.
    It was reached through Fuji_Ground's _RockTex, so every steep face on the volcano was
    surfaced with lake ice, and it was also the material of a summit tarn that Fuji does not
    have (BuildTarn is never called; the basin depth and fill are both zeroed).

    The bedding-plane recipe that used to live in build_tarmac() is reused here almost verbatim,
    because it was always a CUT ROCK recipe - it was only ever in the wrong function. What is new
    is the colour: instead of one grey tinted by its own tone, each band is assigned a colour
    from the red / ochre / black triple, so the wall reads as deposited layers rather than as
    shaded stone.
    """
    rng = _rng()
    n = SIZE
    grain = _fbm(n, 6, 5.0, rng)
    coarse = _fbm(n, 4, 2.0, rng)

    # Bedding planes: low-frequency banding along one axis, warped so they are not ruler-straight.
    # The band PHASE (continuous, -1..1) selects the layer colour; a second, sharper function of
    # the same phase draws the dark seam at each bedding contact.
    y = np.linspace(0.0, 1.0, n)[:, None] * np.ones((1, n))
    warp = (_fbm(n, 4, 3.0, rng) - 0.5) * 0.26
    phase = np.sin((y + warp) * np.pi * 2.0 * 4.0)
    seams = np.clip(1.0 - np.abs(phase) * 1.9, 0.0, 1.0) ** 2.6

    # Layer colour. Three deposits alternating with the bedding: red scoria, ochre tuff and a
    # black ash layer. Mixed with smooth weights, not hard steps - a real cutting weathers the
    # contacts into each other.
    w_red = np.clip(phase, 0.0, 1.0)
    w_black = np.clip(-phase, 0.0, 1.0)
    w_ochre = np.clip(1.0 - np.abs(phase) * 1.15, 0.0, 1.0)
    tot = w_red + w_black + w_ochre + 1e-6
    layer = (STRATA_RED[None, None, :] * (w_red / tot)[..., None]
             + STRATA_BLACK[None, None, :] * (w_black / tot)[..., None]
             + STRATA_OCHRE[None, None, :] * (w_ochre / tot)[..., None])

    # Fracture cracks: thin dark lines from a higher-frequency ridged field.
    ridged = 1.0 - np.abs(_fbm(n, 5, 7.0, rng) * 2.0 - 1.0)
    cracks = np.clip(np.clip(ridged * 1.15 - 0.955, 0.0, 1.0) * 3.0, 0.0, 1.0)

    tone = 0.92 + (grain - 0.5) * 0.24 + (coarse - 0.5) * 0.28
    albedo = layer * tone[..., None]
    # DESATURATE toward the band's own luminance. Full-strength red/ochre/black bands read as
    # streaky bacon at tile scale; the design asks for a RESTRAINED palette ("charcoal + coral +
    # snow", "monochrome" meaning low-chroma). 45% toward grey keeps the layers legible as
    # coloured deposits without turning a volcanic cutting into a sandstone postcard.
    lum = albedo @ np.array([0.299, 0.587, 0.114])
    albedo = albedo * 0.55 + lum[..., None] * 0.45
    # FLATTEN THE BAND CONTRAST. At full strength the black/red/ochre layers are legible as
    # STRIPES rather than as rock: this material also skins the parapet coping and the cairns,
    # whose UVs run the long axis of the object, so a high-contrast banded map arrives rotated
    # 90 degrees and turns a stone parapet into what looks like corrugated fencing. Compressing
    # each pixel most of the way toward the tile's mean luminance keeps the layering readable as
    # a hint of deposition on the big cuttings while stopping it from striping the small props.
    # Provisional: if the strata wall ever gets its own UV-controlled mesh, raise this back up.
    mean_l = float(lum.mean())
    albedo = albedo * 0.38 + (albedo / np.maximum(lum[..., None], 1e-4)) * mean_l * 0.62
    albedo = np.clip(albedo, 0.0, 1.0)
    albedo = albedo * (1.0 - 0.34 * seams[..., None]) + STRATA_BLACK[None, None, :] * 0.34 * seams[..., None]
    albedo = albedo * (1.0 - 0.60 * cracks[..., None]) + STRATA_BLACK[None, None, :] * 0.60 * cracks[..., None]

    # Sparse grey-orange lichen. The design's only concession to living things on the rock, and
    # kept low: the bands already carry the colour, so lichen here is texture, not palette.
    lich = _fbm(n, 5, 9.0, rng)
    patch = np.clip((lich - 0.74) * 6.0, 0.0, 1.0) * 0.40
    lichen_col = np.array([0.540, 0.462, 0.320])
    albedo = albedo * (1.0 - patch[..., None]) + lichen_col[None, None, :] * patch[..., None]

    # The wall must stay WARM (r > g > b) for the same G-gap reason as the cinder tile.
    m = albedo.reshape(-1, 3).mean(0)
    assert m[0] > m[1] > m[2], "strata tile is not warm-biased (mean %s)" % np.round(m, 3)

    height = tone * 0.5 + (1.0 - seams) * 0.28 + (1.0 - cracks) * 0.22
    _save(albedo, "Fuji_Strata_Albedo.png")
    _save(_normal_from_height(height, 6.0), "Fuji_Strata_Normal.png")
    return float(patch.mean())


# --------------------------------------------------------------------------- concrete

def build_timber():
    """Avalanche-gallery / tunnel concrete: board-formed, stained, cold."""
    rng = _rng()
    n = SIZE
    grain = _fbm(n, 6, 6.0, rng)
    stain = _fbm(n, 3, 1.6, rng)

    # Board-form lines: horizontal shuttering marks every ~1/12 of the tile.
    y = np.linspace(0.0, 1.0, n)[:, None] * np.ones((1, n))
    boards = np.clip(1.0 - np.abs(np.sin(y * np.pi * 12.0)) * 5.0, 0.0, 1.0)

    tone = 0.80 + (grain - 0.5) * 0.18 + (stain - 0.5) * 0.30
    albedo = TIMBER[None, None, :] * tone[..., None]
    albedo = albedo * (1.0 - 0.35 * boards[..., None]) + SCORIA[None, None, :] * 0.35 * boards[..., None]

    height = tone * 0.7 + (1.0 - boards) * 0.3
    _save(albedo, "Fuji_Timber_Albedo.png")
    _save(_normal_from_height(height, 7.0), "Fuji_Timber_Normal.png")


# --------------------------------------------------------------------------- cloud/ash

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


def build_cloud_ash():
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
        edge = STRATA_OCHRE
        t = np.clip(a * 1.6, 0.0, 1.0)[..., None]
        cell[..., :3] = edge[None, None, :] * (1.0 - t) + core[None, None, :] * t
        cell[..., 3] = a
        atlas[(i // 2) * c:(i // 2 + 1) * c, (i % 2) * c:(i % 2 + 1) * c] = cell
    _save(atlas, "Fuji_CloudAsh_Sprite.png")
    return atlas


# --------------------------------------------------------------------------- lichen

def build_sorrel():
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
    _save(atlas, "Fuji_Sorrel_Sprite.png")


def main():
    ash_share = build_tarmac()
    clast_share = build_cinder()
    build_snow()
    strata_lichen = build_strata()
    build_timber()
    atlas = build_cloud_ash()
    build_sorrel()

    # ---- assertions, because "it saved" is not "it is right" -----------------------------
    #
    # 1. The cloud/ash must be ACHROMATIC. Any hue in it and it stops being snow.
    rgb = atlas[..., :3]
    a = atlas[..., 3]
    lit = a > 0.15
    if lit.sum() > 0:
        px = rgb[lit]
        chroma = float((px.max(axis=1) - px.min(axis=1)).mean())
        assert chroma < 0.30, ("cloud/ash is too saturated (mean chroma %.3f) - a coral-lit\n                             wisp is nearly white with a warm CAST, not a coloured sprite" % chroma)
        print("[fuji] cloud/ash mean chroma %.3f (achromatic, reads as snow not petals)" % chroma)

    # 2. Each cloud/ash cell must be ELONGATED, not round. Compare the alpha's second moments.
    c = atlas.shape[0] // 2
    for i in range(4):
        cell = atlas[(i // 2) * c:(i // 2 + 1) * c, (i % 2) * c:(i % 2 + 1) * c, 3]
        ys, xs = np.mgrid[0:c, 0:c]
        w = cell.sum()
        mx, my = (xs * cell).sum() / w, (ys * cell).sum() / w
        vx = ((xs - mx) ** 2 * cell).sum() / w
        vy = ((ys - my) ** 2 * cell).sum() / w
        ratio = max(vx, vy) / max(1e-6, min(vx, vy))
        assert ratio > 2.2, "cloud/ash cell %d is round (axis ratio %.2f) - that is a petal" % (i, ratio)
    print("[fuji] all 4 cloud/ash cells are elongated streaks, not discs")

    print("[fuji] ash drifted over %.1f%% of the road tile; scoria clasts cover %.1f%% of the "
          "cinder tile; strata lichen %.1f%%"
          % (ash_share * 100.0, clast_share * 100.0, strata_lichen * 100.0))
    print("[fuji] textures written to %s" % OUT_DIR)


if __name__ == "__main__":
    main()

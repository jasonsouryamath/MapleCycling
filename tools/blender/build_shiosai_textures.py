"""
SHIOSAI COAST - procedural, seamlessly tiling texture set.

Run via:  blender -b -P build_shiosai_textures.py
(It only needs numpy, so a plain `python build_shiosai_textures.py` works too.)

WHY A SECOND TEXTURE SET
------------------------
Shiosai was borrowing Sakura Pass's alpine maps for everything: Sakura_Asphalt_Albedo on the
carriageway, Sakura_Grass_Albedo on the headland. Two problems with that:
  * the pass's asphalt is a dry mountain-road grey with no cracking or patching, so up close the
    coast road read as a flat grey plane with one white dash on it, and
  * the pass's grass is a cool ALPINE meadow; the concept renders (ShiosaiCoast_01..20) show a
    warm, lush, sub-tropical coastal green.
Sakura's maps are shared and must not be mutated, so the coast gets its own set here, written to
Assets/Environment/ShiosaiCoast/Textures/.

Outputs albedo (sRGB), normal (linear tangent-space) and roughness (linear) per surface, plus
two foliage DETAIL albedos (needle / broadleaf) that break up the canopies' flat fill colour.

All fields are periodic noise, so every map wraps in both axes.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

from sakura_lib import repo_root, fbm_2d, ridged_2d, value_noise_2d
from build_textures import (write_png, to_u8, uv_grid, height_to_normal, mix, norm01,
                            contrast, srgb, RES, TILE)


def shiosai_textures_dir():
    d = os.path.join(repo_root(), "Assets", "Environment",
                     "ShiosaiCoast", "Textures")
    os.makedirs(d, exist_ok=True)
    return d


def save_rgb(name, rgb):
    write_png(os.path.join(shiosai_textures_dir(), name), to_u8(np.clip(rgb, 0, 1)))


def save_rgba(name, rgb, alpha):
    write_png(os.path.join(shiosai_textures_dir(), name),
              to_u8(np.clip(np.dstack([rgb, alpha[..., None]]), 0, 1)))


def grey(a):
    return np.dstack([a] * 3)


TAU = 2.0 * np.pi


# --------------------------------------------------------------------------- 1. asphalt

def build_asphalt():
    """
    Coastal highway asphalt, authored at 4 m per tile.

    The concept renders (01, 15) show a dark, slightly blue-grey surface with visible aggregate,
    a thin craquelure of hairline cracks, occasional lighter repair patches, and two polished,
    darker wheel tracks. All four of those are what separate "road" from "grey plane"; the pass's
    map only has the first.
    """
    u, v = uv_grid()

    # ANIME PASS (2026-09-24): the old 190/430-cell value noise put each chip on 2-5 texels of
    # blocky, hard-edged value noise, and the "pale grit" layer lit isolated single texels - at
    # chase-cam minification that aliased into the blue-white sparkle that made every road look
    # low-res. Chips are now fewer, softer (fbm, not blocky value noise) and low-contrast; the
    # road reads as a calm, clean painted surface with wear carried by tracks and patches.
    agg_c = norm01(fbm_2d(u, v, 60, octaves=2, seed=9001, tile=TILE))  # soft coarse aggregate
    agg_f = norm01(fbm_2d(u, v, 140, octaves=2, seed=9007, tile=TILE)) # soft fine grit
    binder = norm01(fbm_2d(u, v, 8, octaves=4, seed=9013, tile=TILE))  # bitumen richness
    macro = norm01(fbm_2d(u, v, 2.0, octaves=3, seed=9019, tile=TILE)) # metre-scale mottle

    # Craquelure: ridged noise inverted into thin dark valleys. Two octaves at different
    # frequencies so the cracks branch instead of forming one regular web.
    crack_a = norm01(ridged_2d(u, v, 7, octaves=4, seed=9023, tile=TILE))
    crack_b = norm01(ridged_2d(u, v, 17, octaves=3, seed=9029, tile=TILE))
    cracks = np.clip((crack_a - 0.80) * 7.0, 0, 1) + 0.6 * np.clip((crack_b - 0.86) * 8.0, 0, 1)
    # Cracking is not uniform: it concentrates where the binder has aged.
    cracks = np.clip(cracks * np.clip(macro * 1.5 - 0.2, 0, 1) * 1.6, 0, 1)

    # Repair patches: big soft blobs of slightly different, fresher asphalt.
    patch = np.clip((norm01(fbm_2d(u, v, 2.6, octaves=3, seed=9031, tile=TILE)) - 0.70) * 5.0, 0, 1)

    # Wheel tracks: stretched hard along V (= along the road) so they read as two polished lanes.
    polish = np.clip(norm01(fbm_2d(u * 0.28, v * 4.0, 5, octaves=3, seed=9037, tile=TILE))
                     * 1.5 - 0.42, 0, 1)

    base_c = srgb("35363b")      # clean coastal bitumen, barely cool
    light = srgb("45474d")       # exposed chipping (low contrast - no sparkle)
    pale = srgb("4e5056")        # grit, now a whisper rather than white flecks
    tar = srgb("16171b")
    fresh = srgb("26272c")       # a newer repair patch

    base = np.ones((RES, RES, 3)) * base_c
    base = mix(base, np.ones_like(base) * light, np.clip((agg_c - 0.55) * 2.0, 0, 1) * 0.7)
    base = mix(base, np.ones_like(base) * pale, np.clip((agg_f - 0.70) * 2.5, 0, 1) * 0.35)
    base = mix(base, np.ones_like(base) * tar, np.clip(binder * 1.25 - 0.48, 0, 1))
    base = mix(base, np.ones_like(base) * fresh, patch * 0.75)
    base *= (0.94 + 0.10 * agg_c)[..., None]
    base *= (0.90 + 0.18 * macro)[..., None]
    base = mix(base, np.ones_like(base) * tar, polish * 0.42)
    base = mix(base, np.ones_like(base) * tar * 0.65, cracks * 0.55)

    height = agg_c * 0.35 + agg_f * 0.10 + binder * 0.18 - cracks * 0.6 + patch * 0.06
    rough = 0.90 - 0.38 * polish + 0.08 * agg_c - 0.10 * patch

    save_rgb("Shiosai_Asphalt_Albedo.png", base)
    save_rgb("Shiosai_Asphalt_Normal.png", height_to_normal(height, 1.25))
    save_rgb("Shiosai_Asphalt_Rough.png", grey(rough))


# ------------------------------------------------------------------------ 2. coastal grass

def build_grass():
    """
    Warm, lush coastal grass. Brighter and yellower than the pass's alpine meadow, with the
    scrubby clumping the headlands in renders 01/03/09 show.
    """
    u, v = uv_grid()

    clump = norm01(fbm_2d(u, v, 5, octaves=5, seed=9101, tile=TILE))
    patch = norm01(fbm_2d(u, v, 1.8, octaves=3, seed=9107, tile=TILE))
    blade = norm01(value_noise_2d(u, v, 170, seed=9113, tile=TILE))
    fine = norm01(value_noise_2d(u, v, 330, seed=9119, tile=TILE))
    scrub = np.clip((norm01(fbm_2d(u, v, 11, octaves=4, seed=9127, tile=TILE)) - 0.66) * 4.0, 0, 1)

    deep = srgb("2c5324")        # shaded blade roots
    mid = srgb("4a7c2e")         # the body colour
    bright = srgb("74a63c")      # sunlit tips
    dry = srgb("9ba85a")         # sun-dried tussock
    bush = srgb("2f5a35")        # darker coastal scrub

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * deep, np.clip(clump * 1.2 - 0.35, 0, 1))
    base = mix(base, np.ones_like(base) * bright, np.clip((blade - 0.55) * 2.2, 0, 1))
    base = mix(base, np.ones_like(base) * dry, np.clip((patch - 0.72) * 3.4, 0, 1) * 0.7)
    base = mix(base, np.ones_like(base) * bush, scrub * 0.65)
    base *= (0.88 + 0.24 * fine)[..., None]

    height = clump * 0.5 + blade * 0.3 + scrub * 0.35 + fine * 0.12
    rough = 0.90 - 0.06 * blade

    save_rgb("Shiosai_Grass_Albedo.png", base)
    save_rgb("Shiosai_Grass_Normal.png", height_to_normal(height, 1.6))
    save_rgb("Shiosai_Grass_Rough.png", grey(rough))


# --------------------------------------------------------------------------- 3. sand

def build_sand():
    """Pale shell sand for the cove beaches, with wet-line darkening and ripple lines."""
    u, v = uv_grid()

    grain = norm01(value_noise_2d(u, v, 460, seed=9201, tile=TILE))
    ripple = norm01(fbm_2d(u * 0.6, v * 5.0, 9, octaves=3, seed=9207, tile=TILE))
    shell = np.clip((norm01(value_noise_2d(u, v, 120, seed=9213, tile=TILE)) - 0.86) * 7.0, 0, 1)
    macro = norm01(fbm_2d(u, v, 2.2, octaves=3, seed=9219, tile=TILE))

    pale = srgb("e2d6b8")
    warm = srgb("cbb894")
    damp = srgb("9d8d70")

    base = np.ones((RES, RES, 3)) * pale
    base = mix(base, np.ones_like(base) * warm, np.clip(macro * 1.3 - 0.3, 0, 1))
    base = mix(base, np.ones_like(base) * damp, np.clip(ripple * 1.2 - 0.55, 0, 1) * 0.8)
    base = mix(base, np.ones_like(base) * srgb("f4efe2"), shell)
    base *= (0.92 + 0.16 * grain)[..., None]

    height = ripple * 0.6 + grain * 0.25 + shell * 0.4
    save_rgb("Shiosai_Sand_Albedo.png", base)
    save_rgb("Shiosai_Sand_Normal.png", height_to_normal(height, 1.1))
    save_rgb("Shiosai_Sand_Rough.png", grey(0.86 - 0.12 * ripple))


# ------------------------------------------------------------- 3b. coastal cliff rock / scree

def _strata(u, v):
    """
    Shared bedding field for the rock maps.

    The coast's cliffs (refs 07/08 and SC/01..08) are STRATIFIED: roughly horizontal sedimentary
    beds, undulating gently, cut by near-vertical fracture joints. That banding is the single
    read that separates "cliff" from "grey noise", so it is built explicitly rather than hoped
    for out of fbm.

    Everything here is periodic: the sine phases use INTEGER band counts over the 0..1 tile and
    every noise call keeps `tile=TILE` with integer coordinate scaling, so the maps still wrap.
    """
    # Warp fields: make the beds undulate and pinch rather than run as ruler-straight stripes.
    warp = norm01(fbm_2d(u, v, 3.0, octaves=4, seed=9601, tile=TILE)) - 0.5
    fine_warp = norm01(fbm_2d(u, v, 9.0, octaves=3, seed=9607, tile=TILE)) - 0.5

    # Major bedding planes: 7 beds per tile (~1.1 m apart at the 8 m authoring scale).
    bed = 0.5 + 0.5 * np.sin((v * 7.0 + warp * 0.9 + fine_warp * 0.18) * TAU)
    # Fine laminations inside each bed.
    lam = 0.5 + 0.5 * np.sin((v * 29.0 + warp * 2.2 + fine_warp * 0.6) * TAU)

    # Vertical joint fractures: ridged noise stretched hard along V (integer scale keeps it
    # seamless), thresholded into thin dark clefts.
    joint = norm01(ridged_2d(u, v * 4.0, 5, octaves=3, seed=9613, tile=TILE))
    joint = np.clip((joint - 0.70) * 7.0, 0, 1)
    # A second, coarser joint set so the face breaks into ANGULAR BLOCKS rather than reading as
    # one continuous wood-grain sheet - blockiness is what the references' cliffs actually show.
    joint_b = norm01(ridged_2d(u, v * 2.0, 2, octaves=2, seed=9617, tile=TILE))
    joint = np.clip(joint + np.clip((joint_b - 0.80) * 8.0, 0, 1) * 0.9, 0, 1)
    # Bedding-plane partings: a THIN dark seam at the trough of each bed only. (Keying this off
    # |bed-0.5| instead darkens the crest as well as the trough, which doubles the band count and
    # turns the map into wide zebra stripes - it is the bug that made this read as timber.)
    parting = np.clip((0.14 - bed) * 8.0, 0, 1)
    # Harden the bed profile: a raw sine gives a soft gradient that reads as polished timber;
    # real strata step between flat-toned beds with a sharp parting between them.
    bed = contrast(bed, 1.4)
    return bed, lam, joint, parting, warp


def build_rock():
    """
    Shiosai coastal cliff rock: pale grey-tan stratified stone.

    WHY THIS EXISTS: every rock surface in the region (cliff band, sea stacks, lighthouse plinth,
    sea arch) was sampling Sakura Pass's Sakura_Rock_Albedo.png, which is a dark slate/khaki/
    OLIVE-GREEN field - i.e. literal military camouflage. Triplanar projection cannot rescue a
    palette that wrong: the stacks read as shattered black blobs and the cliff skirt as camo
    netting, which is finding #1/#2 of the fidelity gap. The references want warm, sunlit,
    grey-tan banded rock with only sparse lichen, so the coast gets its own map.

    Authored for ~8 m per tile, matching the terrain ribbons' _RockScale.
    """
    u, v = uv_grid()
    bed, lam, joint, parting, warp = _strata(u, v)

    grain = norm01(value_noise_2d(u, v, 240, seed=9619, tile=TILE))    # crystal grain
    fine = norm01(value_noise_2d(u, v, 520, seed=9623, tile=TILE))     # micro speckle
    macro = norm01(fbm_2d(u, v, 2.0, octaves=3, seed=9629, tile=TILE)) # metre-scale tone drift
    spall = np.clip((norm01(fbm_2d(u, v, 6.0, octaves=4, seed=9631, tile=TILE)) - 0.70) * 4.0, 0, 1)
    # Lichen: ochre/sage crusts, SPARSE and desaturated. The failure to avoid here is the
    # saturated leaf-green blotches that made the borrowed map look like camouflage.
    lichen = np.clip((norm01(fbm_2d(u, v, 13.0, octaves=3, seed=9637, tile=TILE)) - 0.78) * 5.0, 0, 1)

    pale = srgb("d3cab4")      # sun-bleached bed top
    mid = srgb("9c9285")       # body stone
    warm_tan = srgb("b89d74")  # iron-stained warm bed
    shade = srgb("5f594f")     # shaded bed / recessed lamination
    cleft = srgb("453f37")     # deep joint fracture
    lich = srgb("8c8a63")      # muted ochre-sage lichen

    base = np.ones((RES, RES, 3)) * mid
    # Alternate bed tones so the strata are visible as COLOUR as well as relief. Weighted
    # DELIBERATELY toward the pale side: sunlit coastal rock is mostly light grey-tan with only
    # narrow shadowed partings. Symmetric pale/dark mixing turns it into zebra-stripe timber.
    base = mix(base, np.ones_like(base) * pale, np.clip((bed - 0.40) * 2.0, 0, 1) * 0.95)
    base = mix(base, np.ones_like(base) * shade, np.clip((0.30 - bed) * 4.0, 0, 1) * 0.60)
    # A few beds run warm/iron-stained - picked by the macro field so it is not every other band.
    base = mix(base, np.ones_like(base) * warm_tan,
               np.clip((macro - 0.58) * 2.6, 0, 1) * np.clip((bed - 0.40) * 1.6, 0, 1) * 0.7)
    # Laminations, spalled faces, then the dark structural lines on top of everything.
    base = mix(base, np.ones_like(base) * shade, np.clip((0.42 - lam) * 1.8, 0, 1) * 0.30)
    base = mix(base, np.ones_like(base) * pale, spall * 0.45)
    base = mix(base, np.ones_like(base) * lich, lichen * 0.55)
    base = mix(base, np.ones_like(base) * cleft, parting * 0.80)
    base = mix(base, np.ones_like(base) * cleft, joint * 0.85)
    base *= (0.90 + 0.20 * grain)[..., None]
    base *= (0.94 + 0.12 * fine)[..., None]
    base *= (0.92 + 0.16 * macro)[..., None]

    height = (bed * 0.55 + lam * 0.18 + grain * 0.14 + spall * 0.25
              - parting * 0.85 - joint * 1.0)
    rough = 0.92 - 0.10 * spall + 0.05 * grain - 0.08 * lichen

    save_rgb("Shiosai_Rock_Albedo.png", base)
    save_rgb("Shiosai_Rock_Normal.png", height_to_normal(height, 1.9))
    save_rgb("Shiosai_Rock_Rough.png", grey(rough))


def build_scree():
    """
    Broken rubble / talus for the foot of the cliffs and the wave-cut platform: the same stone
    palette as build_rock, but as loose angular fragments instead of intact beds, authored finer
    (~3 m per tile) because it is only ever seen at the base of a face.
    """
    u, v = uv_grid()

    frag = norm01(value_noise_2d(u, v, 26, seed=9701, tile=TILE))      # pebble cells
    frag_f = norm01(value_noise_2d(u, v, 74, seed=9707, tile=TILE))    # smaller chips
    gap = norm01(ridged_2d(u, v, 26, octaves=3, seed=9713, tile=TILE))
    gap = np.clip((gap - 0.66) * 5.0, 0, 1)                            # shadow between stones
    grit = norm01(value_noise_2d(u, v, 430, seed=9719, tile=TILE))
    macro = norm01(fbm_2d(u, v, 2.0, octaves=3, seed=9723, tile=TILE))
    wet = np.clip((norm01(fbm_2d(u, v, 4.0, octaves=3, seed=9729, tile=TILE)) - 0.62) * 3.2, 0, 1)

    pale = srgb("c6bda9")
    mid = srgb("a2988a")
    warm_tan = srgb("b2a081")
    dark = srgb("5e584e")

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * pale, np.clip((frag - 0.56) * 2.4, 0, 1))
    base = mix(base, np.ones_like(base) * warm_tan, np.clip((frag_f - 0.62) * 2.2, 0, 1) * 0.6)
    base = mix(base, np.ones_like(base) * dark, gap * 0.85)
    base = mix(base, np.ones_like(base) * dark, wet * 0.35)      # damp splash-zone stones
    base *= (0.90 + 0.20 * grit)[..., None]
    base *= (0.93 + 0.14 * macro)[..., None]

    height = frag * 0.6 + frag_f * 0.25 + grit * 0.1 - gap * 1.0
    save_rgb("Shiosai_Scree_Albedo.png", base)
    save_rgb("Shiosai_Scree_Normal.png", height_to_normal(height, 2.0))
    save_rgb("Shiosai_Scree_Rough.png", grey(0.93 - 0.10 * wet + 0.05 * frag))


# ------------------------------------------------------------------- 4. foliage detail maps

def _needle_field(u, v):
    """Anisotropic streaks: long thin lines, which is what a pine needle mass looks like."""
    a = norm01(fbm_2d(u * 0.35, v * 7.0, 22, octaves=3, seed=9301, tile=TILE))
    b = norm01(fbm_2d(u * 7.0, v * 0.35, 22, octaves=3, seed=9307, tile=TILE))
    return a, b


def build_needle_detail():
    """
    Pine-needle DETAIL albedo for the coastal pine canopies.

    The canopies are solid geometry lit by the cel shader; without a detail map they are one
    flat fill colour and read as folded paper, which is exactly the "party hat" failure in the
    gameplay screenshot. Tiled at ~1.2 m this breaks every skirt into needle clusters.
    """
    u, v = uv_grid()
    a, b = _needle_field(u, v)
    tuft = norm01(fbm_2d(u, v, 9, octaves=4, seed=9313, tile=TILE))
    gap = np.clip((norm01(fbm_2d(u, v, 5, octaves=3, seed=9319, tile=TILE)) - 0.62) * 3.2, 0, 1)

    dark = srgb("16341c")
    mid = srgb("24512a")
    lit = srgb("46793a")
    tip = srgb("6f9e46")

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * dark, np.clip(tuft * 1.3 - 0.30, 0, 1))
    base = mix(base, np.ones_like(base) * lit, np.clip((a - 0.56) * 2.6, 0, 1))
    base = mix(base, np.ones_like(base) * tip, np.clip((b - 0.74) * 3.4, 0, 1))
    base = mix(base, np.ones_like(base) * dark * 0.75, gap * 0.8)
    save_rgb("Shiosai_Needle_Albedo.png", base)


def build_leaf_detail():
    """Broadleaf canopy detail: rounded leaf clusters, brighter and yellower than the pines."""
    u, v = uv_grid()
    leaf = norm01(value_noise_2d(u, v, 46, seed=9401, tile=TILE))
    fine = norm01(value_noise_2d(u, v, 150, seed=9407, tile=TILE))
    mass = norm01(fbm_2d(u, v, 6, octaves=4, seed=9413, tile=TILE))
    shadow = np.clip((norm01(fbm_2d(u, v, 3.2, octaves=3, seed=9419, tile=TILE)) - 0.55) * 2.6, 0, 1)

    dark = srgb("1f4520")
    mid = srgb("3c7a2c")
    lit = srgb("6aa93a")
    sun = srgb("9ccb4f")

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * dark, shadow * 0.85)
    base = mix(base, np.ones_like(base) * lit, np.clip((leaf - 0.52) * 2.4, 0, 1))
    base = mix(base, np.ones_like(base) * sun, np.clip((fine - 0.82) * 4.0, 0, 1) *
               np.clip(mass * 1.4 - 0.3, 0, 1))
    base *= (0.90 + 0.20 * mass)[..., None]
    save_rgb("Shiosai_Leaf_Albedo.png", base)


# ------------------------------------------------------------------------------ 5. bark

def build_bark():
    """Black-pine bark: thick dark plates split by deep fissures - not cherry bark."""
    u, v = uv_grid()
    fis = contrast(norm01(fbm_2d(u * 4.0, v * 0.7, 8, octaves=5, seed=9501, tile=None)), 2.2)
    plate = norm01(value_noise_2d(u * 2.0, v * 0.8, 16, seed=9507, tile=None))
    fine = norm01(fbm_2d(u * 2.0, v * 0.9, 40, octaves=3, seed=9513, tile=None))

    dark = srgb("221a16")
    mid = srgb("46372c")
    grey_p = srgb("6b5b4b")

    base = np.ones((RES, RES, 3)) * mid
    base = mix(base, np.ones_like(base) * dark, np.clip(fis * 1.5 - 0.35, 0, 1))
    base = mix(base, np.ones_like(base) * grey_p, np.clip((plate - 0.58) * 2.4, 0, 1))
    base *= (0.86 + 0.24 * fine)[..., None]

    height = (1.0 - fis) * 0.8 + plate * 0.2
    save_rgb("Shiosai_Bark_Albedo.png", base)
    save_rgb("Shiosai_Bark_Normal.png", height_to_normal(height, 2.0))
    save_rgb("Shiosai_Bark_Rough.png", grey(0.88 + 0.08 * fine))


def main():
    print("[shiosai] building coast textures ->", shiosai_textures_dir())
    build_asphalt()
    build_grass()
    build_sand()
    build_rock()
    build_scree()
    build_needle_detail()
    build_leaf_detail()
    build_bark()
    print("[shiosai] textures done.")


if __name__ == "__main__":
    main()

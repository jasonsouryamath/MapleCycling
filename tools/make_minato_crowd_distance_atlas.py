"""
Generate distance-legible crowd atlas variants for Minato Coast.

WHY (root cause, measured -- not guessed):
Every Minato crowd figure is textured from one 2048x2048 donor atlas that contains large
regions of PURE BLACK (hair, shorts/bib, shoes and the cel line-art). Beyond ~80 m a crowd
figure covers only a handful of pixels, so the GPU samples the deepest mips -- which are just
the AVERAGE of that atlas. Measured averages of the shipped atlases:

    Akihiro  lum  68.4   Shinobu  lum  80.6   Akane  lum  84.5
    Shiori   lum  87.6   Coral    lum  98.0        (out of 255)

Against the Minato plaza pavement (measured median lum 186) those averages read as flat,
near-monochrome DARK PINS -- the "NPCs look 2D" defect. The concept art
(Minamo_01_PortDeparture_After.png) keeps its background crowd legible because its clothing
is bright and saturated, so it never averages to a silhouette.

WHAT THIS DOES:
Writes a corrected copy of each unique atlas whose *average* is bright and whose colour
blocks stay separated, by lifting the crushed blacks and boosting saturation. The variant is
used ONLY by the distant LOD1 crowd tier (see MinatoCrowdPopulation.ApplyDistanceReadability),
so the near-range LOD0 look that already reads correctly is left byte-identical.

Run:  python tools/make_minato_crowd_distance_atlas.py
"""

import io
import json
import os
import struct

import numpy as np
from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_DIR = os.path.join(REPO, "Assets", "Kuro", "NPC", "CrowdHigh")
OUT_DIR = os.path.join(REPO, "Assets", "Environment", "MinatoCoast", "Textures", "Crowd")

# ---------------------------------------------------------------- tunables (PROVISIONAL)
# All three are illustrative tuning, not confirmed design requirements.
#
# Tuned against a measured render, not by eye: in diag_minato_target_walker_contact.png the
# 80-250 m crowd band measured figure luminance 89 against pavement 166 (10th percentile 49)
# at BLACK_LIFT 0.34 -- still reading as dark pins. Note the lift is applied in sRGB-encoded
# space but sampled by Unity in LINEAR space, so a 0.34 lift only puts pure black at linear
# ~0.09; it has to be much higher to survive the sRGB->linear conversion and the sun shading.
BLACK_LIFT = 0.44   # 0 -> this value. Stops hair/shorts/line-art crushing to a black pin.
SATURATION = 1.55   # keeps hair/skin/top/bottom as distinct colour blocks once lifted.
GAIN = 1.06         # small overall exposure trim after the lift.

# One entry per UNIQUE donor; several archetypes share a donor. These keys MUST match the
# donor column of MinatoCrowdPopulation.Archetypes -- ApplyDistanceReadability looks the
# variant up by donor name and errors out if one is missing.
# KURO-BASED CROWD (2026-09-24): donors are the Minato riders' Kuro-based bodies, each wearing
# its own kit atlas - so the source is that kit PNG directly (the crowd GLBs also embed normal /
# roughness maps, and "first embedded image" is not guaranteed to be the albedo).
KIT_DIR = os.path.join(REPO, "Assets", "Kuro", "NPC", "KuroRiders")
ATLASES = {name: os.path.join(KIT_DIR, f"KuroKit_{name}.png") for name in (
    "Minori", "Kohaku", "Marina", "Kaoru", "Rina", "Sena",
    "Tatsuya", "Ryoko", "Hibiki", "Asuka", "Yutaka", "Junpei")}


def read_glb_images(path):
    """Yield every embedded image of a .glb as a PIL RGB image."""
    data = open(path, "rb").read()
    if data[:4] != b"glTF":
        raise ValueError(f"{path} is not a binary glTF")
    offset, js, bin_start = 12, None, None
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        if kind == 0x4E4F534A:
            js = json.loads(data[offset:offset + length])
        elif kind == 0x004E4942:
            bin_start = offset
        offset += length
    views = js["bufferViews"]
    for image in js.get("images", []):
        view = views[image["bufferView"]]
        start = bin_start + view.get("byteOffset", 0)
        raw = data[start:start + view["byteLength"]]
        yield Image.open(io.BytesIO(raw)).convert("RGB")


def correct(img):
    a = np.asarray(img).astype(np.float32) / 255.0
    a = BLACK_LIFT + (1.0 - BLACK_LIFT) * a
    lum = (a * np.array([0.299, 0.587, 0.114], np.float32)).sum(2, keepdims=True)
    a = np.clip((lum + (a - lum) * SATURATION) * GAIN, 0.0, 1.0)
    return Image.fromarray((a * 255.0).astype(np.uint8))


def mean_luminance(img):
    avg = np.asarray(img.resize((1, 1), Image.BOX)).astype(float).reshape(3)
    return avg, 0.299 * avg[0] + 0.587 * avg[1] + 0.114 * avg[2]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for donor, glb in sorted(ATLASES.items()):
        image = (Image.open(glb).convert("RGB") if glb.endswith(".png")
                 else next(read_glb_images(os.path.join(SRC_DIR, glb))))
        before_rgb, before_lum = mean_luminance(image)
        fixed = correct(image)
        after_rgb, after_lum = mean_luminance(fixed)
        out = os.path.join(OUT_DIR, f"MinatoCrowd_{donor}_Atlas_Distance.png")
        fixed.save(out)
        print(f"{donor:9s} {before_rgb.astype(int)} lum {before_lum:5.1f}  ->  "
              f"{after_rgb.astype(int)} lum {after_lum:5.1f}   {os.path.basename(out)}")


if __name__ == "__main__":
    main()

"""B5 Nagisa Bay resort looks (look prefix "NB") for the Maple City crowd donors.

Imports tools/blender/build_maple_city_life_textures.py WITHOUT editing it and reuses its donor
atlas analysis (_glb / _class_map / _height_map / _remap / _hair_cap_texture), the same way its
own winter_looks() does. It writes MapleLife_Crowd_<key>_NB<n>.png (+ _HairCap, + _Hair) into the
shared Life/Textures folder, where MapleCityLife.DressForRegion(go, donor, "NB", seated) picks
them up.

Resort wardrobe (one per look index, cycling):
  NB0 aloha shirt (floral print) + khaki shorts, bare calves, sandals
  NB1 linen shirt + linen trousers, loafers (resort guest / marina crew)
  NB2 sundress (top and skirt one colour to the knee), bare calves, sandals, straw sun hat
  NB3 beach: bright tank + board shorts, bare calves, flip-flops, straw sun hat
No helmets (C8): the head keeps a hair cap (or a straw-coloured "sun hat" cap).

Run:  python tools/blender/looks/nagisa_looks.py
"""
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import build_maple_city_life_textures as L  # noqa: E402  (not edited, only imported)

from PIL import Image, ImageDraw  # noqa: E402

NB_LOOKS = 4
ALOHA_BASE = ["#1f6f8b", "#c8453a", "#2e5e3e", "#1c2c52", "#3d7a9e", "#6a2f5a"]
ALOHA_PRINT = ["#f4e9d0", "#ff8a5b", "#ffd166", "#7fd1c4", "#f7f3ea"]
LINEN = ["#efe9dc", "#bcd3e0", "#c9d6c1", "#f4f2ec"]
LINEN_LEG = ["#d8ccb2", "#8a7a62", "#2c3a52", "#efe9dc"]
DRESS = ["#8fd3e0", "#f6e27a", "#ffffff", "#c4a3e0", "#7ec8a8", "#3a7bff", "#e8457a"]
TANK = ["#19b8c9", "#ffd23f", "#3a7bff", "#f4f4f2", "#2ec4b6", "#e63946"]
BOARD = ["#1f4e79", "#2a9d8f", "#264653", "#3d348b", "#0b6e4f"]
KHAKI = ["#c8b690", "#a38d68", "#8f8a6a", "#6d7686"]
SANDAL = ["#6a4a33", "#2a2a2a", "#8c5a3c", "#3b2a1e"]
STRAW = ["#d9c08a", "#e3cf9e", "#cdb27a"]
KNEE = 0.30      # normalised body height of the hem of shorts / dresses (0 soles .. 1 crown)


def _print_mask(shape, seed, density=0.18, r=9):
    """Hibiscus-ish five-petal blobs in atlas space for the aloha print."""
    import math
    import numpy as np
    h, w = shape
    m = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(m)
    rng = random.Random(seed)
    n = int(w * h * density / (math.pi * r * r * 3.0))
    for _ in range(n):
        cx, cy = rng.randrange(w), rng.randrange(h)
        rr = r * rng.uniform(0.7, 1.4)
        a0 = rng.uniform(0, 2 * math.pi)
        for k in range(5):
            a = a0 + k * 2 * math.pi / 5
            px, py = cx + rr * 0.9 * math.cos(a), cy + rr * 0.9 * math.sin(a)
            d.ellipse([px - rr * 0.65, py - rr * 0.65, px + rr * 0.65, py + rr * 0.65], fill=255)
    return np.asarray(m) > 0


def nagisa_looks():
    import numpy as np
    L._assert_not_skin(ALOHA_BASE + LINEN + DRESS + TANK + BOARD, "nagisa garment")
    os.makedirs(L.OUT, exist_ok=True)
    wrote = 0
    for key in L.CROWD_VARIANTS:
        path = os.path.join(L.CROWD_DIR, f"MinatoCrowd_{key}.glb")
        if not os.path.exists(path):
            print("  skip (no donor):", key)
            continue
        j, acc, image = L._glb(path)
        atlas = image("KuroKit_")
        hair_strip = image("NpcHair_")
        size = atlas.size[0]
        cls = L._class_map(j, acc, size)
        hgt = L._height_map(j, acc, size)
        face = L._front_head_mask(j, acc, size)
        base = np.asarray(atlas).astype(np.float32) / 255.0
        mx, mn = base.max(-1), base.min(-1)
        sat = (mx - mn) / np.maximum(mx, 1e-4)
        r, g, bl = base[..., 0], base[..., 1], base[..., 2]
        hue = np.degrees(np.arctan2(np.sqrt(3.0) * (g - bl), 2.0 * r - g - bl)) % 360.0
        skin = (((hue < 42) | (hue > 350)) & (sat > 0.10) & (sat < 0.55) & (mx > 0.62))
        white = (sat < 0.12) & (mx > 0.80)
        head = (cls == L.CLS_HEAD) | (cls == L.CLS_HEADUP)
        skin_col = (np.median(base[skin & head], axis=0) if (skin & head).any()
                    else np.array([0.96, 0.80, 0.68], np.float32)).astype(np.float32)
        top = cls == L.CLS_TOP
        legs = cls == L.CLS_LEGS
        feet = cls == L.CLS_FEET
        hands = cls == L.CLS_HAND
        above_knee = hgt > KNEE
        rng = random.Random(f"nagisa-bay-{key}")
        for v in range(NB_LOOKS):
            kind = v % 4
            hair = L._rgb(rng.choice(L.HAIR))
            out = base.copy()
            L._remap(out, head & ~skin & ~(white & face), hair, 0.55)
            L._remap(out, hands & ~skin, skin_col, 0.25)      # gloves -> bare hands
            cap = hair
            if kind == 0:     # aloha shirt + khaki shorts
                c0 = L._rgb(rng.choice(ALOHA_BASE))
                c1 = L._rgb(rng.choice(ALOHA_PRINT))
                L._remap(out, top & ~skin, c0, 0.30, 0.85)
                pm = _print_mask(out.shape[:2], f"{key}-{v}") & top & ~skin
                out[pm] = np.clip(out[pm] / np.maximum(c0[None, :], 0.05) * c1[None, :], 0, 1)
                L._remap(out, legs & above_knee, L._rgb(rng.choice(KHAKI)), 0.25, 0.8)
                L._remap(out, legs & ~above_knee, skin_col, 0.2, 0.6)
                L._remap(out, feet, L._rgb(rng.choice(SANDAL)), 0.3, 0.6)
                what = "aloha + shorts"
            elif kind == 1:   # linen shirt + trousers
                L._remap(out, top & ~skin, L._rgb(rng.choice(LINEN)), 0.25, 0.9)
                leg = L._rgb(rng.choice(LINEN_LEG))
                L._remap(out, legs & ~skin, leg, 0.25, 0.85)
                L._remap(out, legs & skin, leg, 0.25, 0.85)
                L._remap(out, feet, L._rgb(rng.choice(SANDAL)), 0.3, 0.6)
                what = "linen"
            elif kind == 2:   # sundress to the knee
                dress = L._rgb(rng.choice(DRESS))
                L._remap(out, top & ~skin, dress, 0.28, 0.9)
                L._remap(out, legs & above_knee, dress, 0.28, 0.9)
                L._remap(out, legs & ~above_knee, skin_col, 0.2, 0.6)
                L._remap(out, feet, L._rgb(rng.choice(SANDAL)), 0.3, 0.6)
                cap = L._rgb(rng.choice(STRAW))
                what = "sundress + sun hat"
            else:             # beach: tank + board shorts
                L._remap(out, top & ~skin, L._rgb(rng.choice(TANK)), 0.30, 0.85)
                L._remap(out, legs & above_knee, L._rgb(rng.choice(BOARD)), 0.25, 0.8)
                L._remap(out, legs & ~above_knee, skin_col, 0.2, 0.6)
                L._remap(out, feet, L._rgb("#f2f2ee"), 0.3, 0.6)
                cap = L._rgb(rng.choice(STRAW))
                what = "tank + board shorts + sun hat"
            tag = f"NB{v}"
            Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{tag}.png"), optimize=True)
            L._hair_cap_texture(cap, 5000 + 7 * sum(map(ord, tag))).save(
                os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{tag}_HairCap.png"))
            if hair_strip is not None:
                hs = np.asarray(hair_strip).astype(np.float32) / 255.0
                L._remap(hs, np.ones(hs.shape[:2], bool), hair, 0.6)
                Image.fromarray((hs * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                    os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{tag}_Hair.png"))
            wrote += 1
            print(f"  nagisa look {key} {tag}: {what}")
    print(f"wrote {wrote} NB looks to {L.OUT}")


if __name__ == "__main__":
    nagisa_looks()

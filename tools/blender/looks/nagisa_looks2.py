"""NB4 Nagisa Bay crowd looks, set 2 (look prefixes "NR" resort and "NJ" running kit).

Imports tools/blender/build_maple_city_life_textures.py WITHOUT editing it (N rule 8) and reuses its
donor atlas analysis (_glb / _class_map / _height_map / _front_head_mask / _remap / _hair_cap_texture),
exactly like looks/nagisa_looks.py (prefix "NB", untouched). Output goes to the shared Life/Textures
folder as MapleLife_Crowd_<key>_<NR|NJ><n>.png (+ _HairCap, + _Hair), where
MapleCityLife.DressForRegion(go, donor, "NR"/"NJ", seated) picks them up.

NR resort wardrobe (PROVISIONAL palette, cycles by look index):
  NR0 rash guard (short sleeve) + board shorts, barefoot
  NR1 bikini top + wrap sarong to the knee, sandals, straw sun hat
  NR2 aloha shirt (two-tone stripes) + white shorts, sunglasses, sandals
  NR3 sundress (print hem band), sun hat, sunglasses
  NR4 swim shorts + open linen shirt, sunglasses, flip-flops
  NR5 bikini top + denim shorts, sun hat
NJ running kit (runners, >= 80 on the promenade):
  NJ0..NJ3 technical singlet/tee + split shorts or 3/4 tights, bright trainers, sport shades on NJ1/NJ3
No helmets (C8): the head keeps a hair cap (or a straw "sun hat" / sweat-band coloured cap).

Run:  python tools/blender/looks/nagisa_looks2.py
"""
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import build_maple_city_life_textures as L  # noqa: E402  (not edited, only imported)

from PIL import Image  # noqa: E402

NR_LOOKS = 6
NJ_LOOKS = 4
RASH = ["#19b8c9", "#ff6b35", "#3a7bff", "#2ec4b6", "#f4f4f2", "#e63946"]
BOARD = ["#1f4e79", "#2a9d8f", "#264653", "#3d348b", "#0b6e4f", "#f4a261"]
BIKINI = ["#e8457a", "#ffd23f", "#19b8c9", "#f94144", "#ffffff", "#7b2cbf"]
SARONG = ["#ff8a5b", "#7fd1c4", "#f6e27a", "#c4a3e0", "#e5383b", "#90e0ef"]
ALOHA = ["#1f6f8b", "#c8453a", "#2e5e3e", "#3d7a9e", "#6a2f5a"]
STRIPE = ["#f4e9d0", "#ffd166", "#7fd1c4", "#f7f3ea"]
DRESS = ["#f6e27a", "#8fd3e0", "#e8457a", "#c4a3e0", "#7ec8a8", "#ffffff"]
LINEN = ["#efe9dc", "#bcd3e0", "#f4f2ec", "#c9d6c1"]
DENIM = ["#4a6fa5", "#6b8cc4", "#3b5998"]
WHITE = "#f2f2ee"
SANDAL = ["#6a4a33", "#2a2a2a", "#8c5a3c", "#3b2a1e"]
STRAW = ["#d9c08a", "#e3cf9e", "#cdb27a"]
SHADES = "#101418"
SINGLET = ["#ff3d7f", "#00c2d1", "#c6ff00", "#ff7a00", "#3a7bff", "#ffffff", "#1b1b1f"]
RUNSHORT = ["#1b1b1f", "#263238", "#1f4e79", "#3d348b"]
TRAINER = ["#ff5a36", "#00d0ff", "#e8ff3a", "#ffffff", "#ff3d7f"]
BAND = ["#ffffff", "#ff3d7f", "#00c2d1", "#1b1b1f"]
KNEE = 0.30      # normalised body height of shorts / sarong hems (0 soles .. 1 crown)
MIDTHIGH = 0.36
ARM_CLASS = dict(L.JOINT_CLASS)
for _n in ("LeftArm", "RightArm", "LeftShoulder", "RightShoulder"):
    ARM_CLASS[_n] = L.CLS_FEET     # marker class: upper arm + shoulder cap


def _shades(out, face, head, hgt, base):
    """Wrap-around sunglasses: a dark band through the drawn eyes on the front of the head."""
    import numpy as np
    mx = base.max(-1)
    eyes = face & head & (mx < 0.32)
    if eyes.sum() < 20:
        return False
    hs = hgt[eyes]
    lo, hi = np.percentile(hs, 10), np.percentile(hs, 90)
    pad = max(0.004, 0.25 * (hi - lo))
    band = face & head & (hgt > lo - pad) & (hgt < hi + pad)
    L._remap(out, band, L._rgb(SHADES), 0.15, 0.7)
    return True


def nagisa_looks2():
    import numpy as np
    L._assert_not_skin(RASH + BIKINI + SARONG + DRESS + SINGLET + DENIM, "nagisa looks2 garment")
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
        cls_arm = L._class_map(j, acc, size, ARM_CLASS)
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
        arms = top & (cls_arm == L.CLS_FEET)
        torso = top & ~arms
        legs = cls == L.CLS_LEGS
        feet = cls == L.CLS_FEET
        hands = cls == L.CLS_HAND
        above_knee = hgt > KNEE
        above_mid = hgt > MIDTHIGH
        if torso.any():
            t_lo, t_hi = np.percentile(hgt[torso], 5), np.percentile(hgt[torso], 95)
        else:
            t_lo, t_hi = 0.45, 0.62
        bust = torso & (hgt > t_lo + 0.55 * (t_hi - t_lo)) & (hgt < t_lo + 0.85 * (t_hi - t_lo))
        rng = random.Random(f"nagisa-bay-looks2-{key}")

        def save(out, tag, cap, hair, what):
            nonlocal wrote
            Image.fromarray((np.clip(out, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{tag}.png"), optimize=True)
            L._hair_cap_texture(cap, 7100 + 7 * sum(map(ord, tag))).save(
                os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{tag}_HairCap.png"))
            if hair_strip is not None:
                hs = np.asarray(hair_strip).astype(np.float32) / 255.0
                L._remap(hs, np.ones(hs.shape[:2], bool), hair, 0.6)
                Image.fromarray((hs * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                    os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{tag}_Hair.png"))
            wrote += 1
            print(f"  nagisa look2 {key} {tag}: {what}")

        def bare(out, mask):
            L._remap(out, mask, skin_col, 0.2, 0.6)

        # ------------------------------------------------------------ NR resort looks
        for v in range(NR_LOOKS):
            hair = L._rgb(rng.choice(L.HAIR))
            out = base.copy()
            L._remap(out, head & ~skin & ~(white & face), hair, 0.55)
            bare(out, hands & ~skin)                     # gloves -> bare hands / forearms
            cap = hair
            kind = v % NR_LOOKS
            if kind == 0:
                L._remap(out, top & ~skin, L._rgb(rng.choice(RASH)), 0.30, 0.85)
                L._remap(out, legs & above_knee, L._rgb(rng.choice(BOARD)), 0.25, 0.8)
                bare(out, legs & ~above_knee)
                bare(out, feet)
                what = "rash guard + board shorts, barefoot"
            elif kind == 1:
                bare(out, top)
                L._remap(out, bust, L._rgb(rng.choice(BIKINI)), 0.3, 0.85)
                L._remap(out, legs & above_knee, L._rgb(rng.choice(SARONG)), 0.3, 0.85)
                bare(out, legs & ~above_knee)
                L._remap(out, feet, L._rgb(rng.choice(SANDAL)), 0.3, 0.6)
                cap = L._rgb(rng.choice(STRAW))
                what = "bikini + sarong + sun hat"
            elif kind == 2:
                c0, c1 = L._rgb(rng.choice(ALOHA)), L._rgb(rng.choice(STRIPE))
                L._remap(out, top & ~skin, c0, 0.30, 0.85)
                stripe = top & ~skin & ((hgt * 90.0).astype(np.int32) % 3 == 0)
                L._remap(out, stripe, c1, 0.3, 0.85)
                L._remap(out, legs & above_knee, L._rgb(WHITE), 0.25, 0.8)
                bare(out, legs & ~above_knee)
                L._remap(out, feet, L._rgb(rng.choice(SANDAL)), 0.3, 0.6)
                _shades(out, face, head, hgt, base)
                what = "aloha stripes + white shorts + sunglasses"
            elif kind == 3:
                dress = L._rgb(rng.choice(DRESS))
                bare(out, arms)
                L._remap(out, torso & ~skin, dress, 0.28, 0.9)
                L._remap(out, legs & above_knee, dress, 0.28, 0.9)
                hem = legs & above_knee & ~above_mid
                L._remap(out, hem, L._rgb(WHITE), 0.3, 0.9)
                bare(out, legs & ~above_knee)
                L._remap(out, feet, L._rgb(rng.choice(SANDAL)), 0.3, 0.6)
                cap = L._rgb(rng.choice(STRAW))
                _shades(out, face, head, hgt, base)
                what = "sundress + sun hat + sunglasses"
            elif kind == 4:
                L._remap(out, top & ~skin, L._rgb(rng.choice(LINEN)), 0.25, 0.9)
                bare(out, torso & (hgt < t_lo + 0.9 * (t_hi - t_lo)) & ~bust)   # open shirt front
                L._remap(out, legs & above_knee, L._rgb(rng.choice(BOARD)), 0.25, 0.8)
                bare(out, legs & ~above_knee)
                L._remap(out, feet, L._rgb(WHITE), 0.3, 0.6)
                _shades(out, face, head, hgt, base)
                what = "open linen shirt + swim shorts + sunglasses"
            else:
                bare(out, top)
                L._remap(out, bust, L._rgb(rng.choice(BIKINI)), 0.3, 0.85)
                L._remap(out, legs & above_mid, L._rgb(rng.choice(DENIM)), 0.25, 0.8)
                bare(out, legs & ~above_mid)
                L._remap(out, feet, L._rgb(rng.choice(SANDAL)), 0.3, 0.6)
                cap = L._rgb(rng.choice(STRAW))
                what = "bikini + denim shorts + sun hat"
            save(out, f"NR{v}", cap, hair, what)

        # ------------------------------------------------------------ NJ running kit
        for v in range(NJ_LOOKS):
            hair = L._rgb(rng.choice(L.HAIR))
            out = base.copy()
            L._remap(out, head & ~skin & ~(white & face), hair, 0.55)
            bare(out, hands & ~skin)
            singlet = L._rgb(rng.choice(SINGLET))
            if v % 2 == 0:
                bare(out, arms)
                L._remap(out, torso & ~skin, singlet, 0.3, 0.85)
            else:
                L._remap(out, top & ~skin, singlet, 0.3, 0.85)
            short = L._rgb(rng.choice(RUNSHORT))
            hem = MIDTHIGH if v < 2 else 0.22           # split shorts / 3/4 tights
            L._remap(out, legs & (hgt > hem), short, 0.25, 0.8)
            bare(out, legs & (hgt <= hem))
            L._remap(out, feet, L._rgb(rng.choice(TRAINER)), 0.3, 0.7)
            if v % 2 == 1:
                _shades(out, face, head, hgt, base)
            cap = L._rgb(rng.choice(BAND)) if v == 2 else hair
            save(out, f"NJ{v}", cap, hair, "running singlet/tee + shorts/tights + trainers")
    print(f"wrote {wrote} NR/NJ looks to {L.OUT}")


if __name__ == "__main__":
    nagisa_looks2()

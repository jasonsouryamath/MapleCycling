"""N1 Sakura Pass crowd looks (look prefixes "SK" = village / visitor wardrobe, "SM" = shrine staff).

Imports tools/blender/build_maple_city_life_textures.py WITHOUT editing it (N rule 8) and reuses its
donor atlas analysis (_glb / _class_map / _height_map / _front_head_mask / _remap / _hair_cap_texture),
exactly like looks/nagisa_looks2.py. Output goes to the shared Life/Textures folder as
MapleLife_Crowd_<key>_<SK|SM><n>.png (+ _HairCap, + _Hair), where
MapleCityLife.DressForRegion(go, donor, "SK"/"SM", seated) picks them up.

SK (Sakura wardrobe, PROVISIONAL palette; the look index is picked at random per clone):
  SK0 yukata, pale blue, pink obi, geta           SK3 spring windbreaker + dark trousers
  SK1 yukata, sakura pink, navy obi, geta         SK4 pastel cardigan + chinos, trainers
  SK2 dark yukata, grey obi, geta (men)           SK5 kimono, deep teal, gold obi, zori
SM shrine staff: SM0 white top + vermilion hakama (miko), SM1 white top + indigo hakama (priest).
No helmets (C8): the head keeps a hair cap. Forearms are sleeve (long sleeves), hands stay bare.

Run:  python tools/blender/looks/sakura_looks.py
"""
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import build_maple_city_life_textures as L  # noqa: E402  (not edited, only imported)

from PIL import Image  # noqa: E402

SK_LOOKS = 6
SM_LOOKS = 2
YUKATA_A = ["#9bbad6", "#b9d4e6", "#a9c7d4", "#c1b6dd"]
YUKATA_B = ["#f0b6c8", "#e9a7be", "#f3c4d2", "#e8b4d6"]
YUKATA_C = ["#26385f", "#1f2b4a", "#2b3f6e", "#33455a"]
KIMONO = ["#2f6f78", "#3f7f6a", "#6a4a8a", "#8a3a4a"]
OBI_PINK = ["#d85b86", "#e07aa0", "#c24a74"]
OBI_NAVY = ["#1f2b4a", "#2b3f6e", "#33455a"]
OBI_GREY = ["#8a8d92", "#6b6e74", "#b0b3b8"]
OBI_GOLD = ["#c9a227", "#d6b24a", "#b8932a"]
JACKET = ["#8fb7d9", "#f0c8d4", "#b7d3a8", "#e9e2c6", "#4a6b8a", "#7e7aa8", "#d6e2c8", "#9ec7c0"]
TROUSER = ["#2a2d33", "#23324d", "#3a4458", "#5c6068", "#4a5a46"]
CHINO = ["#cfd4bf", "#b9c4cf", "#a9b5a0", "#9aa4b5"]
CARDI = ["#f0b6c8", "#cfe3c4", "#c6d4ee", "#e6dff5", "#f5e6a8"]
GETA = ["#7a5a3a", "#5a4026", "#8a6a44"]
ZORI = ["#2a2a2a", "#7a2f3e", "#3a3a40"]
TRAINER = ["#f2f2f0", "#e8e6e0", "#7aa2c6", "#c8d6c0"]
MIKO_TOP = "#f3f1ec"
MIKO_HAKAMA = ["#c23a2b", "#b8321f"]
PRIEST_HAKAMA = ["#4d5d86", "#3f4d6b"]
TABI = "#ecece6"
ARM_CLASS = dict(L.JOINT_CLASS_WINTER)
for _n in ("LeftArm", "RightArm", "LeftShoulder", "RightShoulder"):
    ARM_CLASS[_n] = L.CLS_FEET     # marker class: upper arm + shoulder cap


def sakura_looks():
    import numpy as np
    L._assert_not_skin(YUKATA_A + YUKATA_B + YUKATA_C + KIMONO + JACKET + CARDI + MIKO_HAKAMA
                       + PRIEST_HAKAMA + OBI_PINK + OBI_NAVY + OBI_GREY + OBI_GOLD, "sakura garment")
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
        cls = L._class_map(j, acc, size, L.JOINT_CLASS_WINTER)        # long sleeves: forearm = top
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
        torso = top & (cls_arm != L.CLS_FEET)
        legs = cls == L.CLS_LEGS
        feet = cls == L.CLS_FEET
        hands = cls == L.CLS_HAND
        t_lo = float(np.percentile(hgt[torso], 5)) if torso.any() else 0.45
        waist = (torso | legs) & (hgt > t_lo - 0.015) & (hgt < t_lo + 0.075)
        ankle = hgt < 0.085
        yy, xx = np.mgrid[0:size, 0:size]
        dots = (((xx // 22) + (yy // 22)) % 3 == 0) & (((xx % 22) < 9) & ((yy % 22) < 9))
        rng = random.Random(f"sakura-looks-{key}")

        def save(prefix, out, tag, cap, hair, what):
            nonlocal wrote
            Image.fromarray((np.clip(out, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{prefix}{tag}.png"), optimize=True)
            L._hair_cap_texture(cap, 9100 + 7 * sum(map(ord, prefix + str(tag)))).save(
                os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{prefix}{tag}_HairCap.png"))
            if hair_strip is not None:
                hs = np.asarray(hair_strip).astype(np.float32) / 255.0
                L._remap(hs, np.ones(hs.shape[:2], bool), hair, 0.6)
                Image.fromarray((hs * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                    os.path.join(L.OUT, f"MapleLife_Crowd_{key}_{prefix}{tag}_Hair.png"))
            wrote += 1
            print(f"  sakura look {key} {prefix}{tag}: {what}")

        def base_head(out):
            hair = L._rgb(rng.choice(L.HAIR))
            L._remap(out, head & ~skin & ~(white & face), hair, 0.55)
            L._remap(out, hands, skin_col, 0.2, 0.6)          # gloves -> bare hands
            return hair

        def pattern(out, mask, lift=1.14):
            m = mask & dots
            out[m] = np.clip(out[m] * lift, 0.0, 1.0)

        def yukata(out, cloth, obi, shoe):
            L._remap(out, (top | legs), cloth, 0.28, 0.9)
            L._remap(out, legs & ankle, skin_col, 0.2, 0.6)    # bare ankles under the hem
            pattern(out, (top | legs) & ~ankle)
            L._remap(out, waist, obi, 0.3, 0.9)
            L._remap(out, feet, shoe, 0.3, 0.6)

        # ------------------------------------------------------------ SK village / visitor looks
        for v in range(SK_LOOKS):
            out = base.copy()
            hair = base_head(out)
            cap = hair
            if v == 0:
                yukata(out, L._rgb(rng.choice(YUKATA_A)), L._rgb(rng.choice(OBI_PINK)), L._rgb(rng.choice(GETA)))
                what = "pale-blue yukata, pink obi, geta"
            elif v == 1:
                yukata(out, L._rgb(rng.choice(YUKATA_B)), L._rgb(rng.choice(OBI_NAVY)), L._rgb(rng.choice(GETA)))
                what = "sakura-pink yukata, navy obi, geta"
            elif v == 2:
                yukata(out, L._rgb(rng.choice(YUKATA_C)), L._rgb(rng.choice(OBI_GREY)), L._rgb(rng.choice(GETA)))
                what = "dark yukata, grey obi, geta"
            elif v == 3:
                L._remap(out, top & ~skin, L._rgb(rng.choice(JACKET)), 0.30, 0.85)
                L._remap(out, top & skin & ~head, L._rgb(rng.choice(JACKET)), 0.30, 0.85)
                L._remap(out, legs, L._rgb(rng.choice(TROUSER)), 0.25, 0.8)
                L._remap(out, feet, L._rgb(rng.choice(TRAINER)), 0.3, 0.7)
                what = "spring windbreaker + dark trousers"
            elif v == 4:
                L._remap(out, top & ~skin, L._rgb(rng.choice(CARDI)), 0.28, 0.9)
                L._remap(out, top & skin & ~head, L._rgb(rng.choice(CARDI)), 0.28, 0.9)
                L._remap(out, legs, L._rgb(rng.choice(CHINO)), 0.25, 0.8)
                L._remap(out, feet, L._rgb(rng.choice(TRAINER)), 0.3, 0.7)
                what = "pastel cardigan + chinos"
            else:
                yukata(out, L._rgb(rng.choice(KIMONO)), L._rgb(rng.choice(OBI_GOLD)), L._rgb(rng.choice(ZORI)))
                what = "deep kimono, gold obi, zori"
            save("SK", out, v, cap, hair, what)

        # ------------------------------------------------------------ SM shrine staff
        for v in range(SM_LOOKS):
            out = base.copy()
            hair = base_head(out)
            hak = L._rgb(rng.choice(MIKO_HAKAMA if v == 0 else PRIEST_HAKAMA))
            L._remap(out, top, L._rgb(MIKO_TOP), 0.25, 0.9)
            L._remap(out, legs & (hgt > 0.05), hak, 0.28, 0.9)
            L._remap(out, legs & (hgt <= 0.05), L._rgb(TABI), 0.2, 0.9)
            L._remap(out, waist, hak * 0.8, 0.3, 0.9)
            L._remap(out, feet, L._rgb(TABI) if v == 0 else L._rgb(ZORI[0]), 0.3, 0.7)
            save("SM", out, v, hair, hair, "miko (white + vermilion hakama)" if v == 0 else "priest (white + indigo hakama)")
    print(f"wrote {wrote} SK/SM looks to {L.OUT}")


if __name__ == "__main__":
    sakura_looks()

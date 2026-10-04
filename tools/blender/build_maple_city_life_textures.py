"""Maple City LIFE pass textures: neighbourhood cafe fronts, awnings, chalkboards.

Pure Python + PIL - no Blender needed:
    python tools/blender/build_maple_city_life_textures.py

Writes Assets/Environment/MapleCity/Life/Textures/MapleLife_*.png:
  Sign_<ID>       1024x256  fascia sign board
  Awning_<ID>     256x256   striped awning fabric (tiles along U)
  Interior_<ID>   1024x512  lit cafe interior card seen through the glazing
  Chalkboard      256x384   A-frame menu board
  Wood            256x256   warm timber (tables, chairs, window frames)

Every cafe name here is fictional. Consumed by Assets/Editor/MapleCityLife.cs (claude-city).
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "MapleCity", "Life", "Textures")
SS = 2

FONT_DIRS = [
    r"C:\Windows\Fonts",
    "/usr/share/fonts/truetype/google-fonts",
    "/usr/share/fonts/truetype/dejavu",
    "/usr/share/fonts/opentype/noto",
]


def font(names, size):
    for n in names:
        for d in FONT_DIRS:
            p = os.path.join(d, n)
            if os.path.exists(p):
                try:
                    return ImageFont.truetype(p, size)
                except OSError:
                    pass
    return ImageFont.load_default()


SERIF = ["Lora-Variable.ttf", "georgiab.ttf", "georgia.ttf", "DejaVuSerif-Bold.ttf"]
SANS = ["Poppins-Bold.ttf", "segoeuib.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"]
SANS_L = ["Poppins-Medium.ttf", "segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"]
JP = ["NotoSerifCJK-Bold.ttc", "YuGothB.ttc", "msgothic.ttc"]


def hexc(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def canvas(w, h, col):
    return Image.new("RGBA", (w * SS, h * SS), col)


def save(img, name):
    w, h = img.size
    img = img.resize((w // SS, h // SS), Image.LANCZOS)
    os.makedirs(OUT, exist_ok=True)
    img.convert("RGB").save(os.path.join(OUT, f"MapleLife_{name}.png"))


# id, display name, sub line, jp, sign bg, sign ink, awning A, awning B, interior wall, style
CAFES = [
    ("KOMOREBI", "Komorebi Coffee", "ROASTED IN MAPLE CITY", "木漏れ日", "#1f3b2d", "#f1e6cc", "#2f5a41", "#efe6d2", "#6b4a33", "serif"),
    ("GINKGO", "GINKGO ROASTERS", "ESPRESSO  ·  POUR-OVER", "銀杏", "#262427", "#e9b949", "#d9a432", "#2a2a2c", "#3b3431", "sans"),
    ("TRAMLINE", "Tramline Café", "SINCE THE FIRST TRAM", "電車", "#1c2d4f", "#f4efe2", "#223a66", "#f4efe2", "#7a5a3c", "serif"),
    ("HIKARI", "Kissa Hikari", "PUDDING  ·  SIPHON DRIP", "ひかり", "#5a1a22", "#f3dfb8", "#7c2330", "#f3e2c4", "#4e2c22", "serif"),
    ("MAPLEMILK", "MAPLE & MILK", "LATTES  ·  BAKES  ·  BRUNCH", "楓", "#f2e8d9", "#b3452c", "#c0532f", "#f6ecdc", "#8b6446", "sans"),
    ("CANALSIDE", "Canalside Brew", "COLD BREW ON THE WATER", "運河", "#0f4b4f", "#f2f0e6", "#157a7a", "#f2f0e6", "#5d4636", "serif"),
]


def cup_icon(d, cx, cy, s, ink):
    """A plain coffee cup + steam, drawn from geometry."""
    w = s
    d.rounded_rectangle([cx - w * 0.5, cy - w * 0.25, cx + w * 0.38, cy + w * 0.45], radius=w * 0.14, fill=ink)
    d.ellipse([cx + w * 0.22, cy - w * 0.12, cx + w * 0.62, cy + w * 0.28], outline=ink, width=int(w * 0.09))
    d.rounded_rectangle([cx - w * 0.7, cy + w * 0.48, cx + w * 0.58, cy + w * 0.58], radius=w * 0.05, fill=ink)
    for k in (-0.25, 0.0, 0.25):
        x = cx + w * k - w * 0.05
        pts = [(x + math.sin(t / 6.0 * math.pi * 2) * w * 0.06, cy - w * 0.35 - t * w * 0.07) for t in range(7)]
        d.line(pts, fill=ink, width=int(w * 0.06), joint="curve")


def sign(c):
    cid, name, sub, jp, bg, ink, *_rest, style = c
    W, H = 1024 * SS, 256 * SS
    img = canvas(1024, 256, hexc(bg))
    d = ImageDraw.Draw(img)
    inkc = hexc(ink)
    m = 14 * SS
    d.rectangle([m, m, W - m, H - m], outline=inkc, width=4 * SS)
    d.rectangle([m + 10 * SS, m + 10 * SS, W - m - 10 * SS, H - m - 10 * SS], outline=inkc, width=1 * SS)
    cup_icon(d, 130 * SS, 118 * SS, 110 * SS, inkc)
    f = font(SERIF if style == "serif" else SANS, 92 * SS)
    tw = d.textlength(name, font=f)
    maxw = 600 * SS
    if tw > maxw:
        f = font(SERIF if style == "serif" else SANS, int(92 * SS * maxw / tw))
        tw = d.textlength(name, font=f)
    x0 = 250 * SS + (maxw - tw) / 2
    d.text((x0, 58 * SS), name, font=f, fill=inkc)
    fs = font(SANS_L, 26 * SS)
    sw = d.textlength(sub, font=fs)
    d.text((250 * SS + (maxw - sw) / 2, 176 * SS), sub, font=fs, fill=inkc)
    # small stacked JP mark inside the right border (one or two characters)
    fj = font(JP, 44 * SS)
    chars = jp[:2]
    y = (128 - 24 * len(chars)) * SS
    for ch in chars:
        cw = d.textlength(ch, font=fj)
        d.text((938 * SS - cw / 2, y), ch, font=fj, fill=inkc)
        y += 48 * SS
    save(img, f"Sign_{cid}")


def awning(c):
    cid, *_x = c
    a, b = c[6], c[7]
    img = canvas(256, 256, hexc(b))
    d = ImageDraw.Draw(img)
    n = 8
    sw = 256 * SS / n
    for k in range(0, n, 2):
        d.rectangle([k * sw, 0, (k + 1) * sw, 256 * SS], fill=hexc(a))
    # soft fold shading so the fabric reads as fabric, not paint
    shade = Image.new("L", (256 * SS, 256 * SS), 0)
    sd = ImageDraw.Draw(shade)
    for k in range(n):
        x = k * sw
        sd.rectangle([x, 0, x + sw * 0.18, 256 * SS], fill=40)
    img = Image.composite(Image.new("RGBA", img.size, (0, 0, 0, 255)), img, shade.filter(ImageFilter.GaussianBlur(6 * SS)))
    save(img, f"Awning_{cid}")


def interior(c, seed):
    cid = c[0]
    wall = hexc(c[8])
    rng = random.Random(seed)
    W, H = 1024 * SS, 512 * SS
    img = canvas(1024, 512, wall)
    d = ImageDraw.Draw(img)
    # warm gradient: lit ceiling fading down
    for y in range(0, H, 2 * SS):
        k = 1.0 - y / H
        col = tuple(min(255, int(wall[i] * (0.75 + 0.7 * k) + 40 * k)) for i in range(3)) + (255,)
        d.rectangle([0, y, W, y + 2 * SS], fill=col)
    # shelves with jars / cups
    for row in range(3):
        y = (90 + row * 70) * SS
        d.rectangle([40 * SS, y, 470 * SS, y + 8 * SS], fill=(60, 38, 26, 255))
        x = 50 * SS
        while x < 460 * SS:
            w = rng.randint(18, 34) * SS
            h = rng.randint(24, 48) * SS
            col = rng.choice([(236, 226, 205), (190, 120, 70), (120, 150, 110), (225, 190, 120), (90, 60, 45)])
            d.rounded_rectangle([x, y - h, x + w, y], radius=4 * SS, fill=col + (255,))
            x += w + rng.randint(6, 14) * SS
    # menu board - drawn inside the clearest glazing bay so no mullion cuts it (QA #10)
    u0, u1 = window_bay_u(cid)
    bw = (u1 - u0) * 1024
    mw = min(340.0, bw * 0.84)
    mx0 = (u0 + u1) * 0.5 * 1024 - mw * 0.5
    menu = CAFE_MENUS[cid][5]
    # top edge kept below ~y115: from the street the awning valance hides the upper glazing
    d.rectangle([mx0 * SS, 116 * SS, (mx0 + mw) * SS, 248 * SS], fill=(34, 36, 34, 255), outline=(120, 90, 60, 255), width=6 * SS)
    fm = font(SANS_L, 18 * SS)
    for k, line in enumerate(menu):
        tw = d.textlength(line, font=fm)
        f = fm if tw <= (mw - 36) * SS else font(SANS_L, int(18 * SS * (mw - 36) * SS / tw))
        d.text(((mx0 + 18) * SS, (128 + k * 28) * SS), line, font=f, fill=(236, 232, 220, 255))
    # pendant lamps (glowing)
    for x in (180, 420, 660, 880):
        d.line([(x * SS, 0), (x * SS, 40 * SS)], fill=(30, 20, 15, 255), width=2 * SS)
        glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        gd.ellipse([(x - 70) * SS, -10 * SS, (x + 70) * SS, 130 * SS], fill=(255, 210, 140, 90))
        img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(24 * SS)))
        d = ImageDraw.Draw(img)
        d.polygon([((x - 26) * SS, 60 * SS), ((x + 26) * SS, 60 * SS), ((x + 12) * SS, 40 * SS), ((x - 12) * SS, 40 * SS)], fill=(40, 34, 30, 255))
        d.ellipse([(x - 16) * SS, 54 * SS, (x + 16) * SS, 70 * SS], fill=(255, 236, 190, 255))
    # counter + espresso machine + a barista silhouette
    d.rectangle([0, 330 * SS, W, H], fill=(88, 56, 36, 255))
    d.rectangle([0, 322 * SS, W, 336 * SS], fill=(210, 196, 170, 255))
    d.rounded_rectangle([640 * SS, 250 * SS, 820 * SS, 322 * SS], radius=10 * SS, fill=(190, 190, 196, 255))
    d.rectangle([660 * SS, 300 * SS, 700 * SS, 322 * SS], fill=(40, 40, 44, 255))
    d.rectangle([760 * SS, 300 * SS, 800 * SS, 322 * SS], fill=(40, 40, 44, 255))
    bx = rng.choice([300, 420, 500])
    d.ellipse([(bx - 34) * SS, 196 * SS, (bx + 34) * SS, 264 * SS], fill=(40, 28, 24, 255))  # head
    d.rounded_rectangle([(bx - 60) * SS, 256 * SS, (bx + 60) * SS, 340 * SS], radius=30 * SS, fill=(56, 70, 60, 255))  # apron torso
    # pastry case
    d.rectangle([60 * SS, 350 * SS, 380 * SS, 420 * SS], fill=(240, 226, 196, 255), outline=(200, 200, 205, 255), width=4 * SS)
    for k in range(6):
        d.ellipse([(80 + k * 48) * SS, 372 * SS, (116 + k * 48) * SS, 402 * SS], fill=(196, 130, 70, 255))
    img = img.filter(ImageFilter.GaussianBlur(1.2 * SS))  # seen through glass: slightly soft
    save(img, f"Interior_{cid}")


def chalkboard(tag="", title="Today", lines=None, board=(38, 42, 40), frame=(128, 92, 58),
               ink=(236, 232, 218), accent=(230, 196, 120), title_font=None):
    img = canvas(256, 384, board + (255,))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 256 * SS - 1, 384 * SS - 1], outline=frame + (255,), width=14 * SS)
    ft = font(title_font or SERIF, 34 * SS)
    tw = d.textlength(title, font=ft)
    if tw > 188 * SS:
        ft = font(title_font or SERIF, int(34 * SS * 188 * SS / tw))
    fl = font(SANS_L, 22 * SS)
    d.text((34 * SS, 30 * SS), title, font=ft, fill=tuple(min(255, c + 6) for c in ink) + (255,))
    lines = lines or ["Maple latte", "Hojicha latte", "Cold brew", "Pudding", "Melon soda"]
    for k, t in enumerate(lines[:5]):
        tw = d.textlength(t, font=fl)
        f = fl if tw <= 190 * SS else font(SANS_L, int(22 * SS * 190 * SS / tw))
        d.text((34 * SS, (90 + k * 40) * SS), t, font=f, fill=ink + (255,))
    cup_icon(d, 180 * SS, 320 * SS, 44 * SS, accent + (255,))
    save(img, "Chalkboard" + (f"_{tag}" if tag else ""))


# 2026-09-25 copilot (QA #10): every cafe used the same "Today / Maple latte" A-board and the
# same window menu. Each brand now gets two boards (A for its first cafe, B for the second
# time the brand comes round the loop) and its own window menu. All items fictional/generic.
# id -> (board A (title, lines), board B (title, lines), board rgb, frame rgb, ink rgb, window menu)
CAFE_MENUS = {
    "KOMOREBI": (("Today", ["Maple latte", "Forest drip", "Matcha tonic", "Yuzu scone", "Oat flat white"]),
                 ("Morning set", ["Toast + drip 600", "Egg sando", "Hojicha latte", "Kinako roll", "Iced drip"]),
                 (34, 48, 38), (92, 70, 48), (234, 236, 214),
                 ["DRIP  450", "MAPLE LATTE  520", "MATCHA  500", "SCONE  380"]),
    "GINKGO": (("Espresso bar", ["Single origin", "Cortado", "Pour-over", "Affogato", "Espresso tonic"]),
               ("Roasted today", ["Ethiopia natural", "Colombia washed", "House blend", "Decaf", "Cold brew"]),
               (30, 30, 32), (170, 132, 50), (236, 214, 150),
               ["ESPRESSO  380", "CORTADO  450", "POUR-OVER  600", "COLD BREW  520"]),
    "TRAMLINE": (("Daily", ["Tram blend", "Cafe au lait", "Cheese toast", "Napolitan", "Cream soda"]),
                 ("Last stop", ["Hot cocoa", "Milk tea", "Egg custard", "Ham sando", "Lemonade"]),
                 (30, 38, 56), (120, 96, 70), (230, 234, 240),
                 ["BLEND  420", "AU LAIT  480", "NAPOLITAN  780", "TOAST  450"]),
    "HIKARI": (("Kissa menu", ["Siphon coffee", "Purin a la mode", "Melon soda", "Pizza toast", "Royal milk tea"]),
               ("Sweets", ["Custard purin", "Coffee jelly", "Fruit sando", "Hot cake", "Cream soda"]),
               (48, 26, 26), (150, 110, 70), (242, 222, 190),
               ["SIPHON  550", "PURIN  480", "MELON SODA  500", "HOT CAKE  650"]),
    "MAPLEMILK": (("Brunch", ["Maple pancakes", "Avocado toast", "Flat white", "Granola bowl", "Chai latte"]),
                  ("Bakes", ["Maple bun", "Banana bread", "Cinnamon roll", "Oat cookie", "Mocha"]),
                  (40, 40, 38), (190, 90, 60), (244, 232, 214),
                  ["PANCAKES  980", "FLAT WHITE  520", "MAPLE BUN  360", "CHAI  500"]),
    "CANALSIDE": (("On the water", ["Nitro cold brew", "Canal tonic", "Iced latte", "Lemon cake", "Sparkling tea"]),
                  ("Boat snacks", ["Fish sando", "Onigiri set", "Iced hojicha", "Cheesecake", "Ginger ale"]),
                  (26, 44, 46), (80, 120, 118), (226, 240, 236),
                  ["NITRO  580", "ICED LATTE  540", "TONIC  560", "CAKE  420"]),
}

# Shopfront layout mirrored from MapleCityLife.BuildCafe (claude-city) so the window menu
# can be drawn BETWEEN the glazing bars instead of behind one. Keep in sync with Brands[].Width.
CAFE_WIDTH = {"KOMOREBI": 7.6, "GINKGO": 8.4, "TRAMLINE": 7.2, "HIKARI": 6.8, "MAPLEMILK": 7.8, "CANALSIDE": 8.0}


def window_bay_u(cid):
    """(u0, u1) of the widest clear glazing bay, in the interior card's U (0.22..1 = window)."""
    W = CAFE_WIDTH[cid]
    hw = W * 0.5
    door_x0 = hw - 1.75
    L = door_x0 + hw - 0.1                      # glass quad length, door mullion -> end
    bays = max(1, round((door_x0 + hw) / 1.6))
    bars = [door_x0 - (1 - b / bays) * (door_x0 + hw) for b in range(1, bays)]  # x of the bars
    xs = [door_x0 - 0.07] + sorted(bars, reverse=True) + [-hw + 0.19]          # clear edges, left->right
    best = None
    for a, b in zip(xs, xs[1:]):
        u0 = 0.22 + 0.78 * (door_x0 - a) / L
        u1 = 0.22 + 0.78 * (door_x0 - b) / L
        # prefer the bay nearest the window's middle-right (behind the counter), then widest
        score = (u1 - u0) - 0.25 * abs((u0 + u1) * 0.5 - 0.66)
        if best is None or score > best[0]:
            best = (score, u0, u1)
    return best[1], best[2]


def wood():
    rng = random.Random(7)
    img = canvas(256, 256, (120, 78, 50, 255))
    d = ImageDraw.Draw(img)
    for y in range(0, 256 * SS, 3 * SS):
        k = rng.uniform(-18, 18)
        d.line([(0, y), (256 * SS, y + rng.uniform(-4, 4) * SS)], fill=(int(120 + k), int(78 + k * 0.7), int(50 + k * 0.5), 255), width=3 * SS)
    save(img, "Wood")


# ============================================================================ crowd looks
# 2026-09-25 copilot (QA #6): Maple City's pedestrians and cafe customers are clones of the
# Minato crowd donors, who are all dismounted cyclists: helmet, jersey, bib shorts, gloves and
# cycling shoes are baked into ONE projection atlas per donor (no separate helmet mesh or
# material slot to hide). So each donor gets a few "civilian" atlas variants instead:
#   helmet shell -> hair colour     jersey -> casual top     gloves -> bare skin
#   bibs/socks   -> trousers        cycling shoes -> sneakers
# Regions come from the donor GLB itself: every triangle is classed by its dominant skin
# joint and rasterised into UV space, so the masks cannot drift from the mesh. Donor GLBs and
# their embedded atlases are READ ONLY; outputs are new PNGs here, consumed by
# MapleCityLife.Dress (cloned materials under Assets/Environment/MapleCity/Life/Materials).
#   Crowd_<key>_V<n>.png        2048 body atlas (LOD0 live skin AND the LOD1 bake share UVs)
#   Crowd_<key>_V<n>_Hair.png   16x256 NpcHair strip, for donors that have one
CROWD_DIR = os.path.join(ROOT, "Assets", "Kuro", "NPC", "CrowdHigh")
CROWD_VARIANTS = {  # donor key -> number of looks (the only sitter donor gets the most)
    "06_Sit_Sena": 6,
    "05_Walk_Rina": 3, "09_Walk_Hibiki": 3, "10_Walk_Asuka": 3, "11_Walk_Yutaka": 3, "12_Walk_Junpei": 3,
    "02_Wave_Kohaku": 2, "03_StandA_Marina": 2, "04_StandB_Kaoru": 2,
}
# no blonde/grey: under the awnings the blue sky ambient turns pale hair caps into "blue helmets"
HAIR = ["#1c1715", "#3a261a", "#6b4128", "#8a3b24", "#2a1f1a", "#7a5234", "#5a3d2e", "#4a2c1e"]
TOPS = ["#2c3c5e", "#e4dac6", "#3a5a40", "#6e2a33", "#c8973a", "#8b8d92", "#efeeea", "#7aa2c6",
        "#262628", "#c9694f", "#5b4a7a", "#a8b89a"]
LEGS = ["#3b4f73", "#34353a", "#a38d68", "#1f1f23", "#c8b690", "#56583c", "#5a4436", "#6d7686"]
SHOES = ["#e8e6e0", "#262626", "#6a4a33", "#9b9b9b", "#c7b08a"]
# C8 runners: bright technical tops, black/charcoal tights, loud trainers (no helmet: hair cap)
RUN_LOOKS = 3
RUN_TOPS = ["#ff5a36", "#19b8c9", "#c6e83a", "#ff3d8b", "#3a7bff", "#ffc93a", "#8f5bff", "#f4f4f2"]
RUN_LEGS = ["#18181c", "#1f2126", "#2a2c33"]
RUN_SHOES = ["#f2f2f0", "#ff6a3d", "#2ad1c9", "#e6ff4a", "#1c1c1c"]
CLS_NONE, CLS_HEAD, CLS_TOP, CLS_HAND, CLS_LEGS, CLS_FEET, CLS_HEADUP = 0, 1, 2, 3, 4, 5, 6
JOINT_CLASS = {"Head": CLS_HEAD, "head_end": CLS_HEAD, "headfront": CLS_HEAD, "neck": CLS_TOP,
               "Spine": CLS_TOP, "Spine01": CLS_TOP, "Spine02": CLS_TOP,
               "LeftShoulder": CLS_TOP, "RightShoulder": CLS_TOP, "LeftArm": CLS_TOP, "RightArm": CLS_TOP,
               "LeftForeArm": CLS_HAND, "RightForeArm": CLS_HAND,   # short sleeves: forearm = bare
               "LeftHand": CLS_HAND, "RightHand": CLS_HAND,
               "Hips": CLS_LEGS, "LeftUpLeg": CLS_LEGS, "RightUpLeg": CLS_LEGS, "LeftLeg": CLS_LEGS,
               "RightLeg": CLS_LEGS, "LeftFoot": CLS_FEET, "RightFoot": CLS_FEET,
               "LeftToeBase": CLS_FEET, "RightToeBase": CLS_FEET}


def _glb(path):
    import json, struct
    import numpy as np
    b = open(path, "rb").read()
    n = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + n])
    off = 20 + n + 8
    ct = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
    nc = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

    def acc(i):
        a = j["accessors"][i]
        v = j["bufferViews"][a["bufferView"]]
        dt, k = ct[a["componentType"]], nc[a["type"]]
        start = off + v.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride, isz = v.get("byteStride", 0), np.dtype(dt).itemsize * k
        if stride and stride != isz:
            raw = np.frombuffer(b, np.uint8, stride * a["count"], start).reshape(a["count"], stride)[:, :isz]
            arr = np.frombuffer(raw.tobytes(), dt).reshape(a["count"], k)
        else:
            arr = np.frombuffer(b, dt, a["count"] * k, start).reshape(a["count"], k)
        if a.get("normalized"):
            arr = arr.astype(np.float32) / np.iinfo(dt).max
        return arr

    def image(name_prefix):
        import io
        for img in j.get("images", []):
            if (img.get("name") or "").startswith(name_prefix):
                v = j["bufferViews"][img["bufferView"]]
                s = off + v.get("byteOffset", 0)
                return Image.open(io.BytesIO(b[s:s + v["byteLength"]])).convert("RGB")
        return None

    return j, acc, image


def _class_map(j, acc, size, joint_class=None):
    """Per-pixel body-region class in the atlas, from the triangles' dominant skin joints."""
    joint_class = JOINT_CLASS if joint_class is None else joint_class
    import numpy as np
    prim = j["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    uv, jt, wt, idx = acc(at["TEXCOORD_0"]), acc(at["JOINTS_0"]), acc(at["WEIGHTS_0"]), acc(prim["indices"]).reshape(-1, 3)
    names = [j["nodes"][k]["name"] for k in j["skins"][0]["joints"]]
    cls_of_joint = np.array([joint_class.get(n, CLS_TOP) for n in names], np.uint8)
    # summed per-class weight over the triangle's three corners
    score = np.zeros((len(idx), 6), np.float32)
    for c in range(3):
        vj, vw = jt[idx[:, c]], wt[idx[:, c]]
        for s in range(4):
            np.add.at(score, (np.arange(len(idx)), cls_of_joint[vj[:, s]]), vw[:, s])
    tri_cls = score.argmax(1).astype(np.uint8)
    # Split the head by height: its bottom ~9 % is the jersey COLLAR (Head-weighted on these
    # rigs) and belongs to the top; above 30 % is where the helmet shell can be (CLS_HEADUP).
    pos = acc(at["POSITION"])
    cy = pos[idx][:, :, 1].mean(1)
    hm = tri_cls == CLS_HEAD
    y0, y1 = cy[hm].min(), cy[hm].max()
    tri_cls[hm & (cy < y0 + 0.09 * (y1 - y0))] = CLS_TOP
    tri_cls[hm & (cy > y0 + 0.30 * (y1 - y0))] = CLS_HEADUP
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    px = uv * size
    for t in range(len(idx)):
        a, b, c = idx[t]
        d.polygon([tuple(px[a]), tuple(px[b]), tuple(px[c])], fill=int(tri_cls[t]))
    arr = np.asarray(m).copy()
    # grow the classes into the gutters so bilinear/mip sampling at seams stays in-region
    for _ in range(6):
        grown = np.asarray(Image.fromarray(arr).filter(ImageFilter.MaxFilter(3)))
        arr = np.where(arr == 0, grown, arr)
    return arr


def _front_head_mask(j, acc, size):
    """Front half of the head (the face): keeps eye whites and highlights on the face."""
    import numpy as np
    prim = j["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos, uv, idx = acc(at["POSITION"]), acc(at["TEXCOORD_0"]), acc(prim["indices"]).reshape(-1, 3)
    cz = pos[idx][:, :, 2].mean(1)
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    px = uv * size
    for t in np.nonzero(cz > 0.0)[0]:
        a, b, c = idx[t]
        d.polygon([tuple(px[a]), tuple(px[b]), tuple(px[c])], fill=255)
    return np.asarray(m.filter(ImageFilter.MaxFilter(5))) > 0


def _rgb(h):
    import numpy as np
    return np.array(hexc(h)[:3], np.float32) / 255.0


def _remap(src, mask, target, contrast, flatten=0.0):
    """Recolour masked pixels to `target`, keeping their shading (luminance, compressed).

    `flatten` (0..1) replaces that much of the per-pixel luminance with a wide in-mask blur of
    it, so printed detail (jersey side panels, bib-short hems, cleat straps) disappears while
    the broad baked shading stays (C8: civilian clothes must not read as cycling kit).
    """
    import numpy as np
    if not mask.any():
        return
    lum = src[..., 0] * 0.299 + src[..., 1] * 0.587 + src[..., 2] * 0.114
    if flatten > 0.0:
        from scipy.ndimage import gaussian_filter
        mf = mask.astype(np.float32)
        blur = gaussian_filter(lum * mf, 36.0) / np.maximum(gaussian_filter(mf, 36.0), 1e-4)
        lum = lum * (1.0 - flatten) + blur * flatten
    ref = float(np.median(lum[mask])) + 1e-3
    k = np.clip(1.0 - contrast + contrast * lum / ref, 0.25, 1.45)
    tl = float(target @ np.array([0.299, 0.587, 0.114], np.float32))
    # very light targets (white tee, cream) must not clip: pull the gain under 1 for them
    k = np.where(tl > 0.75, np.minimum(k, 1.0 + (1.0 - tl)), k)
    src[mask] = np.clip(target[None, :] * k[mask][:, None], 0.0, 1.0)


def _helmet_mask(j, acc, atlas_arr, cls, size_out=2048):
    """UV mask of the helmet shell: head pixels coloured like the crown of the head.

    The crown (top 12 % of the head) is pure helmet on every donor, so its median colour is
    the helmet colour whatever it is (blue, teal, white...). MapleCityLife fits its hair cap
    to the vertices under this mask, so it only has to be roughly right: vents and stripes
    that fall outside it are closed over by the cap's coverage fill.
    """
    import numpy as np
    prim = j["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos, uv = acc(at["POSITION"]), acc(at["TEXCOORD_0"])
    jt, wt = acc(at["JOINTS_0"]), acc(at["WEIGHTS_0"])
    names = [j["nodes"][k]["name"] for k in j["skins"][0]["joints"]]
    head_j = [k for k, n in enumerate(names) if JOINT_CLASS.get(n) == CLS_HEAD]
    dom = jt[np.arange(len(jt)), wt.argmax(1)]
    head_v = np.isin(dom, head_j)
    y = pos[:, 1]
    y0, y1 = y[head_v].min(), y[head_v].max()
    crown = head_v & (y > y1 - 0.12 * (y1 - y0))
    H, W = atlas_arr.shape[:2]
    px = np.clip((uv[:, 0] * W).astype(int), 0, W - 1)
    py = np.clip((uv[:, 1] * H).astype(int), 0, H - 1)
    helmet_col = np.median(atlas_arr[py[crown], px[crown]], axis=0)
    dist = np.sqrt(((atlas_arr - helmet_col[None, None, :]) ** 2).sum(-1))
    mask = (cls == CLS_HEADUP) & (dist < 0.20)
    img = Image.fromarray((mask * 255).astype(np.uint8), "L").filter(ImageFilter.MinFilter(3))
    if img.size[0] != size_out:
        img = img.resize((size_out, size_out), Image.NEAREST)
    return img, helmet_col


def _hair_cap_texture(hair, seed):
    """64x256 strand texture for the hair cap (u wraps round the head, v runs crown -> rim)."""
    import numpy as np
    rng = np.random.default_rng(seed)
    w, h = 64, 256
    streak = np.cumsum(rng.normal(0.0, 0.35, w))
    streak = np.convolve(np.tile(streak - streak.mean(), 3), np.ones(3) / 3, "same")[w:2 * w]
    streak = (streak - streak.min()) / max(1e-3, np.ptp(streak))
    v = np.linspace(0.0, 1.0, h)[:, None]
    shade = 1.02 + 0.30 * streak[None, :] - 0.12 * v + 0.06 * np.sin(v * 9.0 + streak[None, :] * 5.0)
    shade += 0.10 * np.exp(-((v - 0.28) ** 2) / 0.004)          # soft highlight band
    col = np.clip(hair[None, None, :] * shade[..., None], 0.0, 1.0)
    return Image.fromarray((col * 255.0 + 0.5).astype(np.uint8), "RGB")


def crowd_looks():
    import numpy as np
    for key, count in CROWD_VARIANTS.items():
        path = os.path.join(CROWD_DIR, f"MinatoCrowd_{key}.glb")
        if not os.path.exists(path):
            print("  skip (no donor):", key)
            continue
        j, acc, image = _glb(path)
        atlas = image("KuroKit_")
        hair_strip = image("NpcHair_")
        size = atlas.size[0]
        cls = _class_map(j, acc, size)
        face = _front_head_mask(j, acc, size)
        base = np.asarray(atlas).astype(np.float32) / 255.0
        helmet_img, helmet_col = _helmet_mask(j, acc, base, cls)
        os.makedirs(OUT, exist_ok=True)
        helmet_img.convert("RGB").save(os.path.join(OUT, f"MapleLife_Crowd_{key}_Helmet.png"))
        mx, mn = base.max(-1), base.min(-1)
        sat = (mx - mn) / np.maximum(mx, 1e-4)
        # hue in degrees
        r, g, bl = base[..., 0], base[..., 1], base[..., 2]
        hue = np.degrees(np.arctan2(np.sqrt(3.0) * (g - bl), 2.0 * r - g - bl)) % 360.0
        skin = (((hue < 42) | (hue > 350)) & (sat > 0.10) & (sat < 0.55) & (mx > 0.62))
        white = (sat < 0.12) & (mx > 0.80)
        head = (cls == CLS_HEAD) | (cls == CLS_HEADUP)
        skin_col = np.median(base[skin & head], axis=0) if (skin & head).any() \
            else np.array([0.98, 0.82, 0.72], np.float32)
        rng = random.Random(f"maplelife-{key}")
        hairs = rng.sample(HAIR, min(count, len(HAIR)))
        tops = rng.sample(TOPS, min(count, len(TOPS)))
        legs = [rng.choice(LEGS) for _ in range(count)]

        def emit(tag, hair, top, leg, shoe, flat_top, flat_leg):
            out = base.copy()
            # helmet shell, vents, stripes and the donor's own hair all become hair; the face
            # keeps skin, and eye whites/highlights on the FRONT of the head.
            _remap(out, head & ~skin & ~(white & face), hair, 0.55)
            _remap(out, (cls == CLS_TOP) & ~skin, top, 0.35, flat_top)
            _remap(out, (cls == CLS_HAND) & ~skin, skin_col.astype(np.float32), 0.25)   # gloves -> hands
            # C8: always full-length (trousers / tights): bare calves over short bibs + socks
            # was the strongest "dismounted cyclist" cue. Skin and fabric are levelled
            # separately (each to its own median) so the calves do not show through paler.
            _remap(out, (cls == CLS_LEGS) & ~skin, leg, 0.25, flat_leg)
            _remap(out, (cls == CLS_LEGS) & skin, leg, 0.25, flat_leg)
            _remap(out, cls == CLS_FEET, shoe, 0.35, 0.6)
            img = Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGB")
            img.save(os.path.join(OUT, f"MapleLife_Crowd_{key}_{tag}.png"), optimize=True)
            _hair_cap_texture(hair, 1000 + 7 * sum(map(ord, tag))).save(os.path.join(OUT, f"MapleLife_Crowd_{key}_{tag}_HairCap.png"))
            if hair_strip is not None:
                hs = np.asarray(hair_strip).astype(np.float32) / 255.0
                _remap(hs, np.ones(hs.shape[:2], bool), hair, 0.6)
                Image.fromarray((hs * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                    os.path.join(OUT, f"MapleLife_Crowd_{key}_{tag}_Hair.png"))

        for v in range(count):
            emit(f"V{v}", _rgb(hairs[v % len(hairs)]), _rgb(tops[v]), _rgb(legs[v]), _rgb(rng.choice(SHOES)), 0.8, 0.75)
        runs = RUN_LOOKS if key in RUN_DONORS else 0
        rtops = rng.sample(RUN_TOPS, runs)
        for v in range(runs):
            emit(f"R{v}", _rgb(rng.choice(HAIR)), _rgb(rtops[v]), _rgb(rng.choice(RUN_LEGS)),
                 _rgb(rng.choice(RUN_SHOES)), 0.85, 0.85)
        print(f"  crowd looks {key}: {count} (hair {hairs[:count]}, tops {tops[:count]}), running kit {rtops}")


# ----------------------------------------------------------------------------- run cycles
# 2026-09-25 copilot (C8 runners): the Minato crowd rigs are the Kuro skeleton (same 24 joints,
# same bind joint POSITIONS as Assets/Kuro/kuro_run_fixed.glb, only the bone rolls differ), so
# Kuro's authored "running" clip retargets exactly as a world-space rotation delta against the
# two bind poses:  W_donor(t) = W_clip(t) * W_clip_bind^-1 * W_donor_bind.
# Baked per walker donor into Life/Anim/MapleLife_RunCycle_<key>.json (Unity space: glTF X is
# mirrored, so q -> (x,-y,-z,w), p -> (-x,y,z)), read at runtime by MapleCityRunner. Hips
# height is corrected so the lowest rendered sole of the cycle sits exactly where the rest pose
# sole does (the lane height MapleCityLife already calibrates walkers to).
RUN_CLIP = os.path.join(ROOT, "Assets", "Kuro", "kuro_run_fixed.glb")
ANIM_OUT = os.path.join(ROOT, "Assets", "Environment", "MapleCity", "Life", "Anim")
RUN_DONORS = ["05_Walk_Rina", "09_Walk_Hibiki", "10_Walk_Asuka", "11_Walk_Yutaka", "12_Walk_Junpei"]


def _gltf_rig(path):
    """Nodes, parents, rest TRS and bind world matrices of a skinned glb (numpy)."""
    import numpy as np
    j, acc, _ = _glb(path)
    names = [nd.get("name") for nd in j["nodes"]]
    parent = {}
    for i, nd in enumerate(j["nodes"]):
        for c in nd.get("children", []):
            parent[c] = i
    sk = j["skins"][0]
    ibm = acc(sk["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
    bind = {names[k]: np.linalg.inv(m) for k, m in zip(sk["joints"], ibm)}
    return j, acc, names, parent, sk, bind


def _trs(t, r, s):
    import numpy as np
    from scipy.spatial.transform import Rotation as R
    m = np.eye(4)
    m[:3, :3] = R.from_quat(r).as_matrix() * np.asarray(s)[None, :]
    m[:3, 3] = t
    return m


def _world(names, parent, local):
    """Global 4x4 per node index from local 4x4s (dict idx -> m)."""
    import numpy as np
    out = {}

    def g(i):
        if i in out:
            return out[i]
        m = local[i] if i not in parent else g(parent[i]) @ local[i]
        out[i] = m
        return m
    for i in range(len(names)):
        g(i)
    return out


def _rot_of(m):
    import numpy as np
    from scipy.spatial.transform import Rotation as R
    r = m[:3, :3] / np.linalg.norm(m[:3, :3], axis=0)[None, :]
    return R.from_matrix(r)


def run_cycles(preview=None):
    import json
    import numpy as np
    from scipy.spatial.transform import Rotation as R
    cj, cacc, cn, cpar, csk, cbind = _gltf_rig(RUN_CLIP)
    an = cj["animations"][0]
    chans = {}
    times = None
    for ch in an["channels"]:
        s = an["samplers"][ch["sampler"]]
        t = cacc(s["input"]).ravel()
        times = t if times is None or len(t) > len(times) else times
        chans[(ch["target"]["node"], ch["target"]["path"])] = (t, cacc(s["output"]))
    frames = len(times)
    duration = float(times[-1] - times[0])
    # last key repeats the first on a looping clip: drop it so the cycle wraps cleanly
    loop_frames = frames - 1 if frames > 2 else frames

    def clip_local(i, k):
        nd = cj["nodes"][i]
        vals = {"translation": nd.get("translation", [0, 0, 0]), "rotation": nd.get("rotation", [0, 0, 0, 1]),
                "scale": nd.get("scale", [1, 1, 1])}
        for p in vals:
            if (i, p) in chans:
                t, v = chans[(i, p)]
                vals[p] = v[min(k, len(v) - 1)]
        return _trs(vals["translation"], vals["rotation"], vals["scale"])

    clip_world = []
    for k in range(frames):
        clip_world.append(_world(cn, cpar, {i: clip_local(i, k) for i in range(len(cn))}))
    cidx = {n: i for i, n in enumerate(cn)}
    joints = [cn[k] for k in csk["joints"]]
    os.makedirs(ANIM_OUT, exist_ok=True)
    arm_c = cidx["Armature"]

    for key in RUN_DONORS:
        path = os.path.join(CROWD_DIR, f"MinatoCrowd_{key}.glb")
        if not os.path.exists(path):
            print("  run cycle skip (no donor):", key)
            continue
        dj, dacc, dn, dpar, dsk, dbind = _gltf_rig(path)
        didx = {n: i for i, n in enumerate(dn)}
        rest_local = {}
        for i, nd in enumerate(dj["nodes"]):
            rest_local[i] = _trs(nd.get("translation", [0, 0, 0]), nd.get("rotation", [0, 0, 0, 1]), nd.get("scale", [1, 1, 1]))
        rest_world = _world(dn, dpar, rest_local)
        arm_d = didx["Armature"]
        arm_w = rest_world[arm_d]
        # sole markers: lowest 3% of the foot/toe-dominated skin vertices at rest
        prim = dj["meshes"][0]["primitives"][0]
        at = prim["attributes"]
        vpos, vj, vw = dacc(at["POSITION"]), dacc(at["JOINTS_0"]), dacc(at["WEIGHTS_0"])
        djoints = [dn[k] for k in dsk["joints"]]
        dom = vj[np.arange(len(vj)), vw.argmax(1)]
        mesh_node = next(i for i, nd in enumerate(dj["nodes"]) if nd.get("mesh") == 0)
        mesh_w = rest_world[mesh_node]
        sole_idx, sole_joint = [], []
        foot_names = ("LeftFoot", "LeftToeBase", "RightFoot", "RightToeBase")
        for fn in foot_names:
            ji = djoints.index(fn)
            sel = np.nonzero(dom == ji)[0]
            if len(sel) == 0:
                continue
            m = rest_world[didx[fn]] @ np.linalg.inv(dbind[fn])
            wp = (mesh_w @ m @ np.c_[vpos[sel], np.ones(len(sel))].T).T[:, :3]
            order = np.argsort(wp[:, 1])[:max(6, len(sel) // 30)]
            sole_idx.extend(sel[order]); sole_joint.extend([fn] * len(order))
        sole_idx = np.array(sole_idx)
        sole_v = np.c_[vpos[sole_idx], np.ones(len(sole_idx))]

        def sole_y(world):
            ys = []
            for fn in set(sole_joint):
                sel = np.array([q == fn for q in sole_joint])
                m = world[didx[fn]] @ np.linalg.inv(dbind[fn])
                ys.append((mesh_w @ m @ sole_v[sel].T)[1].min())
            return min(ys)

        def sole_y_side(world, side):
            ys = []
            for fn in set(sole_joint):
                if not fn.startswith(side):
                    continue
                sel = np.array([q == fn for q in sole_joint])
                m = world[didx[fn]] @ np.linalg.inv(dbind[fn])
                ys.append((mesh_w @ m @ sole_v[sel].T)[1].min())
            return min(ys)

        rest_sole = sole_y(rest_world)
        per_frame_local, hips_local, worlds = [], [], []
        for k in range(frames):
            cw = clip_world[k]
            wrot = {}
            for n in joints:
                d = _rot_of(cw[cidx[n]]) * _rot_of(cbind[n]).inv()
                wrot[n] = d * _rot_of(dbind[n])
            local = dict(rest_local)
            rots = {}
            for n in joints:
                i = didx[n]
                pr = _rot_of(rest_world[dpar[i]]) if dn[dpar[i]] not in wrot else wrot[dn[dpar[i]]]
                lr = pr.inv() * wrot[n]
                rots[n] = lr
                t = np.array(dj["nodes"][i].get("translation", [0, 0, 0]), float)
                if n == "Hips":
                    # clip hips relative to its Armature, in metres, re-expressed under the donor's
                    hp = np.linalg.inv(cw[arm_c]) @ cw[cidx["Hips"]] @ np.array([0, 0, 0, 1.0])
                    hp = hp[:3] * np.linalg.norm(cw[arm_c][:3, :3], axis=0)   # undo the 0.01 armature scale
                    t = hp
                m = np.eye(4); m[:3, :3] = lr.as_matrix(); m[:3, 3] = t
                local[i] = m
            w = _world(dn, dpar, local)
            per_frame_local.append(rots)
            hips_local.append(local[didx["Hips"]][:3, 3].copy())
            worlds.append(w)
        soles = [sole_y(w) for w in worlds]
        lift = rest_sole - min(soles)
        for h in hips_local:
            h[1] += lift
        # natural ground speed: how fast the planted sole slides back under the hips
        speeds = []
        for side in ("Left", "Right"):
            ys = np.array([sole_y_side(w, side) for w in worlds]) + lift
            toe = np.array([(w[didx[side + "ToeBase"]] @ [0, 0, 0, 1.0])[:3] for w in worlds])
            for k in range(frames - 1):
                if ys[k] - rest_sole < 0.012 and ys[k + 1] - rest_sole < 0.012:
                    speeds.append(-(toe[k + 1, 2] - toe[k, 2]) / (times[k + 1] - times[k]))
        natural = float(np.median(speeds)) if speeds else 2.0
        data = {"donor": key, "duration": duration * loop_frames / (frames - 1), "frames": loop_frames,
                "naturalSpeed": round(natural, 4), "bones": joints, "rot": [], "hips": [], "restRot": []}
        for k in range(loop_frames):
            for n in joints:
                x, y, z, wq = per_frame_local[k][n].as_quat()
                data["rot"] += [round(float(x), 6), round(float(-y), 6), round(float(-z), 6), round(float(wq), 6)]
            hx, hy, hz = hips_local[k]
            data["hips"] += [round(float(-hx), 6), round(float(hy), 6), round(float(hz), 6)]
        for n in joints:
            x, y, z, wq = R.from_quat(dj["nodes"][didx[n]].get("rotation", [0, 0, 0, 1])).as_quat()
            data["restRot"] += [round(float(x), 6), round(float(-y), 6), round(float(-z), 6), round(float(wq), 6)]
        rh = dj["nodes"][didx["Hips"]].get("translation", [0, 0, 0])
        data["restHips"] = [round(float(-rh[0]), 6), round(float(rh[1]), 6), round(float(rh[2]), 6)]
        with open(os.path.join(ANIM_OUT, f"MapleLife_RunCycle_{key}.json"), "w") as f:
            json.dump(data, f, separators=(",", ":"))
        print(f"  run cycle {key}: {loop_frames} frames / {data['duration']:.3f} s, natural speed "
              f"{natural:.2f} m/s, hips lift {lift * 100:+.1f} cm, sole range "
              f"{(min(soles) + lift - rest_sole) * 100:.1f}..{(max(soles) + lift - rest_sole) * 100:.1f} cm")
        if preview and key == RUN_DONORS[0]:
            _run_preview(dn, dpar, didx, worlds, lift, rest_sole, joints, preview)


def _run_preview(dn, dpar, didx, worlds, lift, ground, joints, out):
    """Side-view stick figures of the baked cycle (verification only)."""
    import numpy as np
    n = len(worlds)
    W, H = 160 * min(n, 10), 260
    img = Image.new("RGB", (W, H * ((n + 9) // 10)), (250, 250, 250))
    d = ImageDraw.Draw(img)
    for k, w in enumerate(worlds):
        ox, oy = 80 + (k % 10) * 160, H * (k // 10) + 230
        sc = 200.0
        P = {nm: (w[didx[nm]] @ [0, 0, 0, 1.0])[:3] + [0, lift, 0] for nm in joints}
        def xy(p):
            return ox + p[2] * sc, oy - (p[1] - ground) * sc
        d.line([(ox - 70, oy), (ox + 70, oy)], fill=(120, 120, 120))
        for nm in joints:
            pi = dpar[didx[nm]]
            if dn[pi] in P and nm not in ("head_end", "headfront"):
                col = (220, 40, 40) if nm.startswith("Left") else (40, 40, 220) if nm.startswith("Right") else (30, 30, 30)
                d.line([xy(P[dn[pi]]), xy(P[nm])], fill=col, width=3)
        d.text((ox - 70, oy - 220), f"f{k}", fill=(0, 0, 0))
    img.save(out)


def contact_shadow():
    """Soft elliptical contact shadow under standing/walking figures (QA #9): RGBA, black."""
    import numpy as np
    n = 128
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    d = np.sqrt(((x - n / 2 + 0.5) / (n / 2)) ** 2 + ((y - n / 2 + 0.5) / (n / 2)) ** 2)
    a = np.clip(1.0 - d, 0.0, 1.0) ** 1.6
    rgba = np.zeros((n, n, 4), np.uint8)
    rgba[..., 3] = (a * 255.0 + 0.5).astype(np.uint8)
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(rgba, "RGBA").save(os.path.join(OUT, "MapleLife_ContactShadow.png"))


# ============================================================================ winter looks
# 2026-09-26 copilot (Azora Highlands WP-F): the same Maple City donors dressed for a Swiss
# winter village. Written as MapleLife_Crowd_<key>_W<n>.png (+ _HairCap / _Hair), picked by
# MapleCityLife.Dress when LookPrefixOverride = "W" (AzoraHighlands.Townsfolk.cs). V/R looks are
# untouched.
#   long sleeves (forearms are jacket), gloves (hands), full-length trousers, winter boots,
#   a knitted scarf at the collar, puffer baffles on ~60% of jackets, and a knit BEANIE on
#   ~65% of looks (the smooth hair cap is tinted to the beanie colour instead of the hair).
# WP-G (claude, 2026-09-26): camel #c9a46a, cream #e9e4d8 and tan-brown #6b5a45 sat inside the skin
# gamut, so on a long sleeve they read as BARE FOREARMS in the close-up. Replaced 1:1 (same list
# length, so every look keeps its seed) with a berry, an icy ski-white and a dark loden, and
# _assert_not_skin() now refuses any jacket that would pass as skin.
W_JACKETS = ["#b3262d", "#1f2b44", "#2d4a3a", "#171719", "#8e2a4f", "#dde5ee", "#4b5d73",
             "#7a2f3e", "#e0561f", "#5a5f66", "#3f6f8f", "#3a3f2c"]
W_LEGS = ["#2a2d33", "#23324d", "#1a1a1c", "#4a4238", "#5c6068", "#2e3a52"]
W_BOOTS = ["#3b2a1e", "#1c1c1e", "#6d4c33", "#8a8d91", "#5a3b26"]
W_GLOVES = ["#1f1f22", "#6b1e22", "#2a3550", "#4a3a2c", "#3d3f44"]
W_KNITS = ["#c23b32", "#e6d9b8", "#2f4d7a", "#d9a441", "#3e6b4b", "#8c2f5a", "#f1efe9", "#353a40", "#9e4a2a"]
W_LOOKS = 4
JOINT_CLASS_WINTER = dict(JOINT_CLASS, LeftForeArm=CLS_TOP, RightForeArm=CLS_TOP)


def _height_map(j, acc, size):
    """Per-pixel normalised body height (0 = soles, 1 = crown) in atlas space, float32."""
    import numpy as np
    prim = j["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos, uv, idx = acc(at["POSITION"]), acc(at["TEXCOORD_0"]), acc(prim["indices"]).reshape(-1, 3)
    y = pos[:, 1]
    y0, y1 = float(y.min()), float(y.max())
    cy = (pos[idx][:, :, 1].mean(1) - y0) / max(1e-4, y1 - y0)
    m = Image.new("I", (size, size), 0)
    d = ImageDraw.Draw(m)
    px = uv * size
    for t in range(len(idx)):
        a, b, c = idx[t]
        d.polygon([tuple(px[a]), tuple(px[b]), tuple(px[c])], fill=int(cy[t] * 100000) + 1)
    arr = np.asarray(m).astype(np.int64)
    for _ in range(6):
        grown = np.asarray(Image.fromarray(arr.astype(np.int32), "I").filter(ImageFilter.MaxFilter(3))).astype(np.int64)
        arr = np.where(arr == 0, grown, arr)
    return np.clip((arr - 1) / 100000.0, 0.0, 1.0).astype(np.float32)


def _assert_not_skin(hexes, what):
    """No garment colour may pass the skin test used by the looks (hue 0-42/350-360, mid sat, light)."""
    import colorsys
    for h in hexes:
        r, g, b = _rgb(h)
        hue, sat, val = colorsys.rgb_to_hsv(r, g, b)
        hue *= 360.0
        skinlike = (hue < 42 or hue > 350) and 0.08 < sat < 0.60 and val > 0.45
        assert not skinlike, f"{what} colour {h} reads as bare skin (hue {hue:.0f}, sat {sat:.2f}, val {val:.2f})"


def winter_looks():
    import numpy as np
    _assert_not_skin(W_JACKETS, "winter jacket")
    for key in CROWD_VARIANTS:
        path = os.path.join(CROWD_DIR, f"MinatoCrowd_{key}.glb")
        if not os.path.exists(path):
            print("  skip (no donor):", key)
            continue
        j, acc, image = _glb(path)
        atlas = image("KuroKit_")
        hair_strip = image("NpcHair_")
        size = atlas.size[0]
        cls = _class_map(j, acc, size, JOINT_CLASS_WINTER)
        hgt = _height_map(j, acc, size)
        face = _front_head_mask(j, acc, size)
        base = np.asarray(atlas).astype(np.float32) / 255.0
        mx, mn = base.max(-1), base.min(-1)
        sat = (mx - mn) / np.maximum(mx, 1e-4)
        r, g, bl = base[..., 0], base[..., 1], base[..., 2]
        hue = np.degrees(np.arctan2(np.sqrt(3.0) * (g - bl), 2.0 * r - g - bl)) % 360.0
        skin = (((hue < 42) | (hue > 350)) & (sat > 0.10) & (sat < 0.55) & (mx > 0.62))
        white = (sat < 0.12) & (mx > 0.80)
        head = (cls == CLS_HEAD) | (cls == CLS_HEADUP)
        top = cls == CLS_TOP
        # the collar: top-class pixels in the band just under the chin
        head_lo = float(hgt[head].min()) if head.any() else 0.8
        scarf = top & (hgt > head_lo - 0.045) & (hgt < head_lo + 0.03)
        # cuff band: sleeve texels within ~6 px (UV) of a glove texel
        hand_m = Image.fromarray(((cls == CLS_HAND) * 255).astype(np.uint8))
        for _ in range(6):
            hand_m = hand_m.filter(ImageFilter.MaxFilter(3))
        cuff = top & ~scarf & (np.asarray(hand_m) > 0) & (cls != CLS_HAND)
        rng = random.Random(f"maplelife-winter-{key}")
        jackets = rng.sample(W_JACKETS, W_LOOKS)
        for v in range(W_LOOKS):
            hair = _rgb(rng.choice(HAIR))
            jacket = _rgb(jackets[v])
            knit = _rgb(rng.choice([k for k in W_KNITS if k != jackets[v]]))
            beanie = rng.random() < 0.65
            puffer = rng.random() < 0.6
            out = base.copy()
            _remap(out, head & ~skin & ~(white & face), hair, 0.55)
            _remap(out, top & ~scarf, jacket, 0.30, 0.85)
            # skin in the sleeves (forearms on short-sleeve donors) becomes sleeve too
            _remap(out, top & skin & ~scarf & ~head, jacket, 0.30, 0.85)
            if puffer:
                # horizontal quilted baffles: shading bands every ~4.5% of body height
                band = 0.5 + 0.5 * np.cos(hgt * (2.0 * np.pi / 0.045))
                k = 1.0 - 0.16 * band ** 3
                m = top & ~scarf
                out[m] *= k[m][:, None]
            # knitted scarf: rib texture from height + a little noise
            ribs = 0.92 + 0.08 * np.cos(hgt * (2.0 * np.pi / 0.006))
            sc = np.clip(knit[None, :] * ribs[scarf][:, None], 0.0, 1.0)
            out[scarf] = sc
            _remap(out, cls == CLS_HAND, _rgb(rng.choice(W_GLOVES)), 0.25, 0.5)
            # WP-G: a darker knitted cuff where the sleeve meets the glove, so the long sleeve
            # visibly ENDS at the wrist instead of the forearm reading as one pale limb.
            out[cuff] = np.clip(out[cuff] * 0.72, 0.0, 1.0)
            leg = _rgb(rng.choice(W_LEGS))
            _remap(out, (cls == CLS_LEGS) & ~skin, leg, 0.25, 0.8)
            _remap(out, (cls == CLS_LEGS) & skin, leg, 0.25, 0.8)
            boot = _rgb(rng.choice(W_BOOTS))
            feet = cls == CLS_FEET
            _remap(out, feet, boot, 0.30, 0.6)
            # rubber sole / snow on the welt: lighter band at the very bottom of the boot
            sole = feet & (hgt < 0.018)
            out[sole] = np.clip(out[sole] * 0.5 + np.array([0.80, 0.80, 0.78], np.float32) * 0.5, 0.0, 1.0)
            tag = f"W{v}"
            Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                os.path.join(OUT, f"MapleLife_Crowd_{key}_{tag}.png"), optimize=True)
            cap = knit if beanie else hair
            _hair_cap_texture(cap, 3000 + 7 * sum(map(ord, tag))).save(os.path.join(OUT, f"MapleLife_Crowd_{key}_{tag}_HairCap.png"))
            if hair_strip is not None:
                hs = np.asarray(hair_strip).astype(np.float32) / 255.0
                _remap(hs, np.ones(hs.shape[:2], bool), hair, 0.6)
                Image.fromarray((hs * 255.0 + 0.5).astype(np.uint8), "RGB").save(
                    os.path.join(OUT, f"MapleLife_Crowd_{key}_{tag}_Hair.png"))
            print(f"  winter look {key} {tag}: jacket {jackets[v]} {'puffer' if puffer else 'coat'}, "
                  f"{'beanie' if beanie else 'bare head'}")


if __name__ == "__main__":
    import sys
    if "--winter-only" in sys.argv:
        winter_looks()
        sys.exit(0)
    if "--run-only" in sys.argv:
        k = sys.argv.index("--run-only")
        run_cycles(sys.argv[k + 1] if len(sys.argv) > k + 1 else None)
        sys.exit(0)
    only_crowd = "--crowd-only" in sys.argv
    if not only_crowd:
        contact_shadow()
        for i, c in enumerate(CAFES):
            sign(c)
            awning(c)
            interior(c, 100 + i)
        chalkboard()   # legacy name, kept so older scenes still resolve it
        for c in CAFES:
            cid = c[0]
            (ta, la), (tb, lb), board, frame, ink, _menu = CAFE_MENUS[cid]
            tf = SERIF if c[9] == "serif" else SANS
            chalkboard(f"{cid}_A", ta, la, board, frame, ink, title_font=tf)
            chalkboard(f"{cid}_B", tb, lb, board, frame, ink, title_font=tf)
        wood()
    if "--no-crowd" not in sys.argv:
        crowd_looks()
        run_cycles()
    print("wrote", sorted(os.listdir(OUT)))

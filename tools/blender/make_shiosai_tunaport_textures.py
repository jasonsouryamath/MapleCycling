"""Shiosai Grand Coast TUNA PORT texture set (signs, noren, nobori, lanterns, tuna skins,
kawara, cedar boards, koshi lattice). Plain Python + Pillow, no Blender needed.

    python tools/blender/make_shiosai_tunaport_textures.py

Output: Assets/Environment/ShiosaiCoast/Textures/TunaPort/TP_*.png
All shop names are fictional. Japanese text uses Yu Gothic Bold (Windows).
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "ShiosaiCoast", "Textures", "TunaPort")
FONT_JP = r"C:\Windows\Fonts\YuGothB.ttc"
FONT_JP_M = r"C:\Windows\Fonts\YuGothM.ttc"
os.makedirs(OUT, exist_ok=True)
rnd = random.Random(20260927)


def font(size, medium=False):
    return ImageFont.truetype(FONT_JP_M if medium else FONT_JP, size)


def save(img, name):
    img.convert("RGB").save(os.path.join(OUT, name), optimize=True)
    print("wrote", name, img.size)


def grain(img, amount=10, seed=0):
    r = random.Random(seed)
    px = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            n = r.randint(-amount, amount)
            c = px[x, y]
            px[x, y] = tuple(max(0, min(255, v + n)) for v in c[:3])
    return img


def centred(d, box, text, f, fill, stroke=0, stroke_fill=None, spacing=0):
    x0, y0, x1, y1 = box
    bb = d.textbbox((0, 0), text, font=f, stroke_width=stroke)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    # shrink to fit the box (92%) so no sign ever clips its own text
    while (tw > (x1 - x0) * 0.92 or th > (y1 - y0) * 0.92) and f.size > 8:
        f = f.font_variant(size=int(f.size * 0.94))
        bb = d.textbbox((0, 0), text, font=f, stroke_width=stroke)
        tw, th = bb[2] - bb[0], bb[3] - bb[1]
    d.text((x0 + (x1 - x0 - tw) / 2 - bb[0], y0 + (y1 - y0 - th) / 2 - bb[1]), text, font=f,
           fill=fill, stroke_width=stroke, stroke_fill=stroke_fill)


def vertical(d, box, text, f, fill, stroke=0, stroke_fill=None):
    """Top-to-bottom Japanese vertical text, one glyph per cell."""
    x0, y0, x1, y1 = box
    n = len(text)
    cell = (y1 - y0) / n
    for k, ch in enumerate(text):
        centred(d, (x0, y0 + k * cell, x1, y0 + (k + 1) * cell), ch, f, fill, stroke, stroke_fill)


# ------------------------------------------------------------------ market roof sign
def market_sign():
    W, H = 2048, 320
    img = Image.new("RGB", (W, H), (22, 38, 72))
    d = ImageDraw.Draw(img)
    d.rectangle([10, 10, W - 11, H - 11], outline=(236, 232, 220), width=8)
    # red rising-circle emblem with a tuna silhouette
    cx, cy, r = 170, H // 2, 118
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(200, 40, 36))
    d.polygon([(cx - 88, cy), (cx - 40, cy - 34), (cx + 40, cy - 30), (cx + 70, cy - 6),
               (cx + 100, cy - 40), (cx + 92, cy), (cx + 100, cy + 40), (cx + 70, cy + 6),
               (cx + 40, cy + 30), (cx - 40, cy + 34)], fill=(245, 240, 228))
    centred(d, (330, 20, W - 40, 230), "しおさい漁港  まぐろ市場", font(168), (246, 242, 230))
    centred(d, (330, 228, W - 40, 300), "SHIOSAI FISHING PORT  ·  TUNA MARKET  ·  EST. 1952",
            font(52, True), (236, 196, 92))
    save(grain(img, 6, 1), "TP_MarketSign.png")


def market_vertical():
    W, H = 256, 1024
    img = Image.new("RGB", (W, H), (196, 36, 34))
    d = ImageDraw.Draw(img)
    d.rectangle([8, 8, W - 9, H - 9], outline=(248, 240, 226), width=8)
    vertical(d, (0, 40, W, H - 40), "魚市場", font(200), (250, 246, 236))
    save(img, "TP_MarketVertical.png")


def auction_board():
    W, H = 1024, 512
    img = Image.new("RGB", (W, H), (30, 52, 40))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W - 1, H - 1], outline=(140, 100, 60), width=18)
    centred(d, (0, 20, W, 140), "本日の競り  セリ開始 5:30", font(66), (244, 240, 226))
    rows = [("本鮪  生", "3本"), ("本鮪  冷凍", "28本"), ("めばち", "41本"), ("きはだ", "17本")]
    for k, (a, b) in enumerate(rows):
        y = 170 + k * 80
        d.text((80, y), a, font=font(56), fill=(236, 232, 214))
        d.text((720, y), b, font=font(56), fill=(246, 214, 96))
    save(grain(img, 8, 2), "TP_AuctionBoard.png")


# ------------------------------------------------------------------ fishmonger fascia boards
SHOPS = [
    ("丸八鮮魚店", (240, 234, 220), (28, 30, 34), (190, 36, 30)),
    ("魚河岸  大吉", (34, 44, 78), (246, 242, 232), (232, 190, 80)),
    ("まぐろ問屋  しおさい", (196, 38, 34), (250, 246, 236), (250, 246, 236)),
    ("海鮮食堂  はま", (118, 82, 48), (250, 240, 220), (250, 240, 220)),
    ("鮮魚  山本商店", (236, 230, 214), (24, 52, 96), (24, 52, 96)),
    ("干物  かねよ", (40, 40, 42), (240, 200, 90), (240, 200, 90)),
]


def shop_signs():
    for i, (name, bg, fg, accent) in enumerate(SHOPS):
        W, H = 1024, 192
        img = Image.new("RGB", (W, H), bg)
        d = ImageDraw.Draw(img)
        d.rectangle([6, 6, W - 7, H - 7], outline=accent, width=6)
        # wood grain on the light boards
        if sum(bg) > 500:
            for y in range(0, H, 5):
                shade = rnd.randint(-10, 4)
                d.line([(0, y), (W, y + rnd.randint(-2, 2))],
                       fill=tuple(max(0, min(255, c + shade)) for c in bg), width=2)
            d.rectangle([6, 6, W - 7, H - 7], outline=accent, width=6)
        centred(d, (20, 10, W - 20, H - 10), name, font(112), fg)
        save(grain(img, 5, 10 + i), f"TP_ShopSign_{i}.png")


# ------------------------------------------------------------------ noren (split curtains)
NOREN = [
    ("魚", (30, 44, 86), (244, 240, 230)),
    ("鮮魚", (178, 34, 32), (250, 246, 236)),
    ("まぐろ", (26, 30, 38), (246, 242, 232)),
    ("海鮮", (54, 86, 132), (246, 242, 232)),
]


def noren():
    for i, (txt, bg, fg) in enumerate(NOREN):
        W, H = 512, 384
        img = Image.new("RGB", (W, H), bg)
        d = ImageDraw.Draw(img)
        d.rectangle([0, 0, W, 34], fill=tuple(int(c * 0.72) for c in bg))
        f = font(230 if len(txt) == 1 else 150 if len(txt) == 2 else 112)
        centred(d, (0, 40, W, H - 20), txt, f, fg)
        # the slits between panels: a darker seam every quarter
        for k in (1, 2, 3):
            x = k * W // 4
            d.rectangle([x - 3, 60, x + 3, H], fill=tuple(int(c * 0.45) for c in bg))
        save(grain(img, 6, 30 + i), f"TP_Noren_{i}.png")


# ------------------------------------------------------------------ nobori (vertical banners)
NOBORI = [
    ("まぐろ", (248, 246, 240), (200, 32, 30), (200, 32, 30)),
    ("大漁", (196, 34, 32), (250, 246, 236), (240, 200, 60)),
    ("本マグロ", (30, 50, 104), (250, 246, 236), (250, 246, 236)),
    ("海鮮丼", (246, 200, 50), (30, 30, 34), (196, 34, 32)),
    ("鮮魚", (248, 246, 240), (24, 52, 110), (24, 52, 110)),
]


def nobori():
    for i, (txt, bg, fg, edge) in enumerate(NOBORI):
        W, H = 160, 720
        img = Image.new("RGB", (W, H), bg)
        d = ImageDraw.Draw(img)
        d.rectangle([0, 0, 18, H], fill=edge)          # the chichi (pole loops) edge
        d.rectangle([0, H - 40, W, H], fill=edge)
        vertical(d, (22, 26, W - 4, H - 50), txt, font(118 if len(txt) <= 3 else 104), fg)
        save(img, f"TP_Nobori_{i}.png")


# ------------------------------------------------------------------ lanterns
def lanterns():
    for i, (txt, body, ink) in enumerate([("魚", (206, 40, 30), (24, 20, 20)),
                                          ("祭", (214, 52, 32), (24, 20, 20)),
                                          ("大漁", (238, 226, 196), (190, 30, 28))]):
        W, H = 512, 256
        img = Image.new("RGB", (W, H), body)
        d = ImageDraw.Draw(img)
        for y in range(0, H, 11):                      # the bamboo ribs
            d.line([(0, y), (W, y)], fill=tuple(int(c * 0.82) for c in body), width=2)
        d.rectangle([0, 0, W, 18], fill=(24, 22, 22))
        d.rectangle([0, H - 18, W, H], fill=(24, 22, 22))
        f = font(170 if len(txt) == 1 else 120)
        centred(d, (0, 20, W // 2, H - 20), txt, f, ink)
        centred(d, (W // 2, 20, W, H - 20), txt, f, ink)
        save(img, f"TP_Lantern_{i}.png")


# ------------------------------------------------------------------ tuna skins
def lerp(a, b, t):
    return tuple(int(a[k] + (b[k] - a[k]) * t) for k in range(3))


def tuna_fresh():
    """u = nose(0) -> tail(1); v = around the body, 0 & 1 = back (dorsal), 0.5 = belly.
    Bluefin read: a wide gunmetal-navy back, an iridescent steel-blue lateral band, silver belly."""
    W, H = 512, 256
    img = Image.new("RGB", (W, H))
    px = img.load()
    back = (10, 18, 40)
    steel = (46, 78, 122)
    flank = (128, 146, 168)
    belly = (206, 212, 220)
    r = random.Random(3)
    for y in range(H):
        v = y / (H - 1)
        a = abs(v - 0.5) * 2.0          # 1 = back, 0 = belly
        for x in range(W):
            u = x / (W - 1)
            if a > 0.56:
                c = lerp(steel, back, min(1, (a - 0.56) / 0.22))
            elif a > 0.40:
                c = lerp(flank, steel, (a - 0.40) / 0.16)
            elif a > 0.24:
                c = lerp(belly, flank, (a - 0.24) / 0.16)
            else:
                c = belly
            # silvery vertical bars on the lower flank (bluefin pattern)
            if 0.22 < a < 0.46 and int(u * 42) % 2 == 0 and 0.18 < u < 0.78:
                c = lerp(c, (232, 236, 244), 0.30)
            # yellow finlets toward the tail on back and belly edges
            if 0.66 < u < 0.88 and (a > 0.9 or a < 0.07) and int(u * 60) % 2 == 0:
                c = (226, 188, 38)
            if u > 0.88:                  # dark caudal keel
                c = lerp(c, (18, 24, 38), (u - 0.88) / 0.12)
            if u < 0.10:                  # head: darker, gill line
                c = lerp((34, 44, 64), c, u / 0.10)
            if 0.105 < u < 0.115 and a < 0.8:
                c = lerp(c, (20, 26, 40), 0.7)
            n = r.randint(-5, 5)
            px[x, y] = tuple(max(0, min(255, c[k] + n)) for k in range(3))
    d = ImageDraw.Draw(img)
    for vy in (0.25, 0.75):
        d.ellipse([22, vy * H - 11, 44, vy * H + 11], fill=(190, 170, 90))
        d.ellipse([27, vy * H - 7, 39, vy * H + 7], fill=(10, 10, 12))
    save(img.filter(ImageFilter.GaussianBlur(0.6)), "TP_Tuna.png")


def tuna_frozen():
    """Frozen auction tuna: hoar-frosted skin over a dark back, head on, tail sawn off (red)."""
    W, H = 512, 256
    img = Image.new("RGB", (W, H))
    px = img.load()
    r = random.Random(7)
    # low-frequency frost blotches
    blot = Image.effect_noise((128, 64), 90).filter(ImageFilter.GaussianBlur(2.2)).resize((W, H), Image.BICUBIC).point(lambda q: max(0, min(255, int((q - 128) * 2.6 + 128)))).load()
    for y in range(H):
        v = y / (H - 1)
        a = abs(v - 0.5) * 2.0
        for x in range(W):
            u = x / (W - 1)
            under = lerp((196, 204, 214), (44, 60, 88), min(1, max(0, a - 0.3) / 0.5))
            f = (blot[x, y] - 128) / 128.0          # -1..1 frost thickness
            c = lerp(under, (224, 230, 238), max(0, min(1, 0.28 + 0.35 * f)))
            n = r.randint(-10, 10)
            c = tuple(max(0, min(255, c[k] + n)) for k in range(3))
            if u < 0.09:
                c = lerp((70, 82, 104), c, u / 0.09)
            if u > 0.93:                  # the sawn tail: red frozen flesh
                c = (170, 40, 44) if a < 0.8 else (120, 30, 34)
            px[x, y] = c
    d = ImageDraw.Draw(img)
    # red painted lot number on the flank (both sides)
    for vy in (0.3, 0.7):
        d.text((250, vy * H - 22), "27", font=font(44), fill=(196, 28, 30))
    save(img.filter(ImageFilter.GaussianBlur(0.7)), "TP_TunaFrozen.png")


# ------------------------------------------------------------------ building surfaces (tile)
def kawara():
    W, H = 512, 512
    img = Image.new("RGB", (W, H), (78, 84, 94))
    d = ImageDraw.Draw(img)
    cols, rows = 8, 8
    cw, rh = W // cols, H // rows
    for c in range(cols):
        x0 = c * cw
        for xx in range(cw):
            t = xx / cw
            s = math.sin(t * math.pi)       # rounded pantile profile
            shade = int(46 + 72 * s)
            d.line([(x0 + xx, 0), (x0 + xx, H)], fill=(shade, shade + 4, shade + 12))
    for rr in range(rows):
        y = rr * rh
        d.rectangle([0, y, W, y + 5], fill=(26, 28, 34))
        d.rectangle([0, y + 6, W, y + 9], fill=(118, 124, 134))
    save(grain(img, 7, 3), "TP_Kawara.png")


def cedar():
    W, H = 512, 512
    img = Image.new("RGB", (W, H), (60, 48, 38))
    d = ImageDraw.Draw(img)
    x = 0
    while x < W:
        bw = rnd.randint(28, 40)
        base = rnd.randint(46, 78)
        col = (base, int(base * 0.82), int(base * 0.66))
        d.rectangle([x, 0, x + bw, H], fill=col)
        for k in range(10):
            gx = x + rnd.randint(2, bw - 2)
            d.line([(gx, 0), (gx + rnd.randint(-4, 4), H)],
                   fill=tuple(int(c * 0.8) for c in col), width=1)
        d.line([(x, 0), (x, H)], fill=(20, 16, 14), width=3)
        x += bw
    for y in (170, 340):                     # horizontal battens
        d.rectangle([0, y, W, y + 14], fill=(36, 28, 24))
    save(grain(img, 6, 4), "TP_Cedar.png")


def koshi():
    """Machiya street lattice: dense vertical slats over a dark interior."""
    W, H = 256, 256
    img = Image.new("RGB", (W, H), (22, 18, 16))
    d = ImageDraw.Draw(img)
    for k in range(16):
        x = k * 16
        d.rectangle([x, 0, x + 9, H], fill=(104, 74, 50))
        d.line([(x + 1, 0), (x + 1, H)], fill=(132, 98, 66), width=2)
    for y in (0, 250):
        d.rectangle([0, y, W, y + 6], fill=(84, 60, 42))
    save(grain(img, 5, 5), "TP_Koshi.png")


def shutter():
    W, H = 256, 256
    img = Image.new("RGB", (W, H), (150, 156, 160))
    d = ImageDraw.Draw(img)
    for y in range(0, H, 10):
        d.line([(0, y), (W, y)], fill=(96, 100, 106), width=2)
        d.line([(0, y + 3), (W, y + 3)], fill=(190, 194, 198), width=1)
    save(grain(img, 6, 6), "TP_Shutter.png")


def ice_tray():
    """Crushed ice with fish laid on it, for the fishmonger display trays (u across, v along)."""
    W, H = 512, 256
    img = Image.new("RGB", (W, H), (224, 234, 240))
    d = ImageDraw.Draw(img)
    r = random.Random(11)
    for k in range(900):
        x, y = r.randint(0, W), r.randint(0, H)
        s = r.randint(2, 6)
        g = r.randint(200, 255)
        d.ellipse([x, y, x + s, y + s], fill=(g - 10, g - 4, g))
    fish = [((40, 60, 90), (200, 210, 220)), ((150, 40, 40), (230, 120, 90)),
            ((170, 150, 60), (230, 220, 180)), ((60, 80, 100), (210, 220, 230))]
    for row in range(3):
        for col in range(7):
            back, belly = fish[(row + col) % len(fish)]
            cx = 36 + col * 68 + r.randint(-6, 6)
            cy = 40 + row * 84 + r.randint(-6, 6)
            d.ellipse([cx - 30, cy - 11, cx + 30, cy + 11], fill=back)
            d.ellipse([cx - 26, cy - 2, cx + 26, cy + 10], fill=belly)
            d.polygon([(cx + 28, cy), (cx + 42, cy - 12), (cx + 42, cy + 12)], fill=back)
            d.ellipse([cx - 24, cy - 6, cx - 18, cy], fill=(10, 10, 10))
    # red price tags
    for k in range(6):
        x, y = r.randint(10, W - 70), r.randint(10, H - 30)
        d.rectangle([x, y, x + 56, y + 22], fill=(250, 246, 236), outline=(200, 30, 30), width=2)
        d.text((x + 6, y + 1), "¥%d" % r.choice([380, 580, 980, 1280]), font=font(16), fill=(200, 30, 30))
    save(img, "TP_IceTray.png")


def maguro_cut():
    """Sashimi-grade cuts on a board: akami / chutoro / otoro blocks for the tuna shop window."""
    W, H = 512, 256
    img = Image.new("RGB", (W, H), (236, 230, 214))
    d = ImageDraw.Draw(img)
    cols = [(168, 26, 36), (206, 84, 90), (232, 170, 168)]
    labels = ["赤身", "中トロ", "大トロ"]
    for k in range(3):
        x0 = 20 + k * 164
        d.rectangle([x0, 40, x0 + 140, 180], fill=cols[k])
        for s in range(6):
            yy = 50 + s * 22
            d.line([(x0 + 4, yy), (x0 + 136, yy + 6)], fill=tuple(min(255, c + 30) for c in cols[k]), width=3)
        centred(d, (x0, 186, x0 + 140, 246), labels[k], font(40), (30, 30, 30))
    save(img, "TP_MaguroCuts.png")


def tairyobata():
    """Big-catch flags flown from fishing-boat masts: rays, a border and 大漁."""
    palettes = [((250, 246, 236), (206, 36, 32), (30, 60, 140)),
                ((30, 70, 150), (250, 246, 236), (240, 200, 50)),
                ((240, 200, 50), (200, 36, 32), (30, 60, 140)),
                ((206, 36, 32), (250, 246, 236), (40, 130, 80))]
    for i, (bg, ink, ray) in enumerate(palettes):
        W, H = 384, 256
        img = Image.new("RGB", (W, H), bg)
        d = ImageDraw.Draw(img)
        cx, cy = 60, H
        for k in range(9):
            a0 = math.radians(-90 + k * 20)
            a1 = math.radians(-90 + k * 20 + 10)
            d.polygon([(cx, cy), (cx + 600 * math.cos(a0), cy + 600 * math.sin(a0)),
                       (cx + 600 * math.cos(a1), cy + 600 * math.sin(a1))], fill=ray)
        d.rectangle([0, 0, W - 1, H - 1], outline=ink, width=14)
        centred(d, (90, 20, W - 20, H - 20), "大漁", font(140), ink, stroke=6, stroke_fill=bg)
        save(img, f"TP_Tairyobata_{i}.png")


if __name__ == "__main__":
    tairyobata()
    market_sign()
    market_vertical()
    auction_board()
    shop_signs()
    noren()
    nobori()
    lanterns()
    tuna_fresh()
    tuna_frozen()
    kawara()
    cedar()
    koshi()
    shutter()
    ice_tray()
    maguro_cut()

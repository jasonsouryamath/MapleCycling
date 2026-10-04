"""Pass-2 textures for the Shiosai tuna port (street life / dressing).

Writes Assets/Environment/ShiosaiCoast/Textures/TunaPort/TP_*.png:
  cut-outs (RGBA, alpha-tested in HDRP/Lit): TP_Himono, TP_Squid, TP_Net
  cards: TP_Manhole, TP_KaitaiBanner, TP_LotCards (4x2 atlas), TP_Menu_0..2, TP_Seri, TP_BusStop
Run: python tools/blender/make_shiosai_tunaport_detail_textures.py
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "ShiosaiCoast", "Textures", "TunaPort")
FONT_B = r"C:\Windows\Fonts\YuGothB.ttc"
FONT_M = r"C:\Windows\Fonts\YuGothM.ttc"


def font(size, medium=False):
    return ImageFont.truetype(FONT_M if medium else FONT_B, size)


def save(img, name):
    img.save(os.path.join(OUT, name))
    print("wrote", name, img.size)


def centred(d, box, text, f, fill, stroke=0, stroke_fill=None):
    x0, y0, x1, y1 = box
    size = f.size
    while True:
        bb = d.textbbox((0, 0), text, font=f, stroke_width=stroke)
        if bb[2] - bb[0] <= (x1 - x0) and bb[3] - bb[1] <= (y1 - y0) or size < 10:
            break
        size = int(size * 0.92)
        f = ImageFont.truetype(f.path, size)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    d.text((x0 + (x1 - x0 - w) / 2 - bb[0], y0 + (y1 - y0 - h) / 2 - bb[1]), text, font=f, fill=fill,
           stroke_width=stroke, stroke_fill=stroke_fill)


def vertical(d, box, text, f, fill):
    x0, y0, x1, y1 = box
    n = len(text)
    step = (y1 - y0) / n
    for i, ch in enumerate(text):
        centred(d, (x0, y0 + i * step, x1, y0 + (i + 1) * step), ch, f, fill)


# ------------------------------------------------------------------ cut-outs
def net_layer(img, pitch, width, colour, seed=0):
    d = ImageDraw.Draw(img)
    W, H = img.size
    r = random.Random(seed)
    for k in range(-H, W + H, pitch):
        j = r.uniform(-1.5, 1.5)
        d.line([(k + j, 0), (k + H + j, H)], fill=colour, width=width)
        d.line([(k + H + j, 0), (k + j, H)], fill=colour, width=width)


def himono():
    """A bamboo-framed drying net with rows of split, butterflied aji (horse mackerel)."""
    W, H = 512, 256
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    net_layer(img, 18, 2, (40, 64, 58, 255), 1)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W - 1, 9], fill=(150, 118, 70, 255))
    d.rectangle([0, H - 10, W - 1, H - 1], fill=(150, 118, 70, 255))
    d.rectangle([0, 0, 9, H - 1], fill=(150, 118, 70, 255))
    d.rectangle([W - 10, 0, W - 1, H - 1], fill=(150, 118, 70, 255))
    r = random.Random(4)
    for row in range(2):
        for col in range(7):
            cx = 40 + col * 70 + r.uniform(-4, 4)
            cy = 70 + row * 118 + r.uniform(-4, 4)
            # butterflied fish: two lobes, pink-amber flesh, silver rim, dark spine, tail fork
            for box in ([cx - 26, cy - 44, cx + 1, cy + 30], [cx - 1, cy - 44, cx + 26, cy + 30]):
                d.ellipse(box, fill=(196, 150, 110, 255), outline=(200, 200, 204, 255), width=3)
            d.line([(cx, cy - 42), (cx, cy + 30)], fill=(90, 52, 40, 255), width=3)
            d.polygon([(cx, cy + 28), (cx - 15, cy + 50), (cx, cy + 42), (cx + 15, cy + 50)], fill=(150, 150, 156, 255))
            d.ellipse([cx - 7, cy - 48, cx + 7, cy - 34], fill=(70, 60, 56, 255))
    save(img, "TP_Himono.png")


def squid():
    """Dried surume squid, hung by the tail: a flat amber mantle, fins, and a tassel of arms."""
    W, H = 256, 512
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    body = (206, 150, 92, 255)
    d.polygon([(128, 20), (60, 70), (196, 70)], fill=(214, 164, 104, 255))          # fin
    d.rounded_rectangle([78, 50, 178, 300], radius=40, fill=body)
    for k in range(10):
        x = 86 + k * 9.5
        d.line([(x, 290), (x + math.sin(k) * 10, 470 - (k % 3) * 20)], fill=(186, 124, 74, 255), width=6)
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    d = ImageDraw.Draw(img)
    for y in range(60, 290, 8):
        d.line([(84, y), (172, y)], fill=(190, 132, 80, 90), width=1)
    d.line([(128, 0), (128, 22)], fill=(60, 60, 60, 255), width=3)                  # clothes-peg string
    save(img, "TP_Squid.png")


def net():
    W, H = 512, 512
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    net_layer(img, 22, 3, (36, 92, 70, 255), 2)
    d = ImageDraw.Draw(img)
    r = random.Random(9)
    for k in range(9):                      # a few orange floats along the head rope
        x = 30 + k * 56
        d.ellipse([x - 12, 4, x + 12, 28], fill=(236, 110, 20, 255))
    d.line([(0, 16), (W, 16)], fill=(200, 190, 150, 255), width=5)
    save(img, "TP_Net.png")


# ------------------------------------------------------------------ cards
def manhole():
    """Japanese design manhole: cast iron, a leaping tuna over waves, 'しおさい' and ｵｽｲ lettering."""
    S = 512
    img = Image.new("RGB", (S, S), (92, 94, 98))
    d = ImageDraw.Draw(img)
    d.ellipse([6, 6, S - 6, S - 6], fill=(70, 72, 76), outline=(40, 40, 42), width=10)
    d.ellipse([46, 46, S - 46, S - 46], outline=(110, 112, 116), width=6)
    for k in range(5):                      # waves
        y = 300 + k * 26
        pts = [(60 + x, y + 12 * math.sin(x / 26.0 + k)) for x in range(0, S - 120, 6)]
        d.line(pts, fill=(118, 122, 128), width=7)
    # tuna silhouette
    d.polygon([(130, 250), (230, 175), (340, 190), (395, 225), (430, 185), (420, 250), (440, 305),
               (392, 268), (330, 292), (220, 290)], fill=(126, 130, 136))
    d.ellipse([160, 212, 176, 228], fill=(60, 60, 64))
    centred(d, (130, 70, 382, 150), "しおさい", font(78), (126, 130, 136))
    centred(d, (170, 420, 342, 470), "おすい", font(40, True), (120, 124, 128))
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    save(img, "TP_Manhole.png")


def kaitai_banner():
    W, H = 1024, 256
    img = Image.new("RGB", (W, H), (178, 22, 26))
    d = ImageDraw.Draw(img)
    d.rectangle([10, 10, W - 11, H - 11], outline=(245, 230, 200), width=6)
    centred(d, (40, 26, W - 260, 170), "本日 マグロ解体ショー", font(120), (255, 250, 240), 4, (90, 10, 12))
    centred(d, (40, 170, W - 260, 236), "毎朝十一時 ・ 本まぐろ 一本さばき", font(48, True), (255, 226, 120))
    d.ellipse([W - 230, 30, W - 40, 220], fill=(255, 250, 240))
    centred(d, (W - 220, 40, W - 50, 210), "鮪", font(150), (178, 22, 26))
    save(img, "TP_KaitaiBanner.png")


def lot_cards():
    """4x2 atlas of auction lot placards: white card, big red lot number, weight in black."""
    W, H = 1024, 512
    img = Image.new("RGB", (W, H), (240, 240, 236))
    d = ImageDraw.Draw(img)
    r = random.Random(11)
    for i in range(8):
        cx, cy = (i % 4) * 256, (i // 4) * 256
        d.rectangle([cx + 6, cy + 6, cx + 249, cy + 249], fill=(246, 246, 242), outline=(30, 30, 30), width=5)
        centred(d, (cx + 20, cy + 20, cx + 236, cy + 160), str(r.randint(11, 98)), font(140), (196, 20, 24))
        centred(d, (cx + 20, cy + 170, cx + 236, cy + 236), f"{r.randint(86, 262)}kg", font(56, True), (20, 20, 20))
    save(img, "TP_LotCards.png")


def menus():
    items = [
        [("まぐろ丼", "1,200"), ("中とろ丼", "2,400"), ("海鮮丼", "1,800"), ("鉄火巻", "900")],
        [("本まぐろ 刺身", "1,500"), ("かま焼き", "800"), ("ねぎとろ", "700"), ("まぐろ汁", "300")],
        [("あじ干物", "450"), ("いか一夜干し", "600"), ("さば文化干し", "500"), ("金目鯛", "1,200")],
    ]
    for i, rows in enumerate(items):
        W, H = 256, 512
        img = Image.new("RGB", (W, H), (32, 34, 30))
        d = ImageDraw.Draw(img)
        d.rectangle([6, 6, W - 7, H - 7], outline=(150, 112, 64), width=10)
        centred(d, (20, 22, W - 20, 90), ["お品書き", "本日のおすすめ", "干物"][i], font(46), (255, 240, 200))
        for k, (name, price) in enumerate(rows):
            y = 110 + k * 96
            centred(d, (20, y, W - 20, y + 50), name, font(40, True), (250, 250, 246))
            centred(d, (20, y + 48, W - 20, y + 86), price + "円", font(34, True), (255, 210, 90))
        img = img.filter(ImageFilter.GaussianBlur(0.4))
        save(img, f"TP_Menu_{i}.png")


def seri_sign():
    W, H = 512, 256
    img = Image.new("RGB", (W, H), (250, 248, 240))
    d = ImageDraw.Draw(img)
    d.rectangle([8, 8, W - 9, H - 9], outline=(20, 40, 110), width=12)
    centred(d, (30, 20, W - 30, 170), "せり場", font(140), (20, 40, 110))
    centred(d, (30, 170, W - 30, 236), "関係者以外立入禁止", font(44, True), (190, 20, 24))
    save(img, "TP_Seri.png")


def bus_stop():
    S = 256
    img = Image.new("RGB", (S, S), (250, 250, 248))
    d = ImageDraw.Draw(img)
    d.ellipse([4, 4, S - 4, S - 4], fill=(30, 110, 60))
    d.ellipse([22, 22, S - 22, S - 22], fill=(250, 250, 248))
    centred(d, (40, 50, S - 40, 150), "しおさい港", font(44), (30, 110, 60))
    centred(d, (40, 150, S - 40, 210), "バスのりば", font(36, True), (30, 30, 30))
    save(img, "TP_BusStop.png")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    himono(); squid(); net()
    manhole(); kaitai_banner(); lot_cards(); menus(); seri_sign(); bus_stop()

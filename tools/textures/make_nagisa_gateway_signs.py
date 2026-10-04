"""Nagisa Bay gateway / overlook / bridge signage textures (worker E, brief sections 1, 10, 17).

Writes PNGs to Assets/Environment/NagisaBay/Textures/Gateway/ :
  NB_GW_Wordmark.png   2048x512  NAGISA BAY wordmark board (gateway beam, photo wall, bridge plaque)
  NB_GW_Welcome.png    1024x1024 gateway pylon plaque: welcome + cyclist rules
  NB_GW_Skyline.png    2048x512  Skyline Terrace low-wall sign (elevation read from NagisaRoute.json)
  NB_GW_Panorama.png   2048x768  overlook interpretation board: bay panorama with labelled landmarks
  NB_GW_Kiosk.png      1024x1024 cafe kiosk: fascia (top) + menu board (bottom)
  NB_GW_Direction.png  1024x512  directional finger board (marina / promenade / resort / KOM)
  NB_GW_Bridge.png     1024x256  Nagisa Bridge plaque
  NB_GW_Lane.png       512x512   green cycle-lane tile (bike pictogram once per tile; tile = 20 m)
All text is fictional / original. Run:  python tools/textures/make_nagisa_gateway_signs.py
"""
import json, math, os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ROUTE = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "NagisaRoute.json")
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures", "Gateway")
os.makedirs(OUT, exist_ok=True)

TEAL = (14, 108, 122); TEAL_D = (8, 66, 80); AQUA = (96, 205, 205); CORAL = (255, 118, 88)
WHITE = (250, 249, 243); SAND = (236, 222, 190); INK = (20, 28, 36); GOLD = (255, 196, 70)
LAWN = (40, 150, 90)
LAT = "C:/Windows/Fonts/segoeuib.ttf"
LATL = "C:/Windows/Fonts/segoeuisl.ttf"
JP = "C:/Windows/Fonts/YuGothB.ttc"


def font(path, px):
    return ImageFont.truetype(path, px)


def centered(d, xy, text, f, fill, anchor="mm", spacing=0):
    if spacing:
        # manual letter spacing
        w = sum(d.textlength(c, font=f) for c in text) + spacing * (len(text) - 1)
        x = xy[0] - w / 2
        for c in text:
            d.text((x, xy[1]), c, font=f, fill=fill, anchor="lm")
            x += d.textlength(c, font=f) + spacing
    else:
        d.text(xy, text, font=f, fill=fill, anchor=anchor)


def wave_mark(d, cx, cy, r, col, col2):
    """Brand glyph: sun disc over three wave lines."""
    d.ellipse([cx - r * 0.55, cy - r * 0.95, cx + r * 0.55, cy + r * 0.15], fill=col2)
    for k in range(3):
        y = cy + r * (0.15 + 0.32 * k)
        pts = []
        for i in range(0, 61):
            x = cx - r + 2 * r * i / 60
            pts.append((x, y + math.sin(i / 60 * 4 * math.pi + k) * r * 0.09))
        d.line(pts, fill=col, width=max(3, int(r * 0.12)), joint="curve")


def gradient(w, h, top, bot):
    im = Image.new("RGB", (w, h))
    px = im.load()
    for y in range(h):
        t = y / (h - 1)
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    return im


def route_info():
    d = json.load(open(ROUTE))
    z = d["zones"]
    return z, d["samples"]


def wordmark():
    W, H = 2048, 512
    im = gradient(W, H, TEAL, TEAL_D)
    d = ImageDraw.Draw(im)
    d.rectangle([14, 14, W - 15, H - 15], outline=WHITE, width=8)
    d.rectangle([34, H - 74, W - 35, H - 38], fill=CORAL)
    wave_mark(d, 250, 215, 150, AQUA, GOLD)
    centered(d, (1190, 190), "NAGISA BAY", font(LAT, 220), WHITE, spacing=18)
    centered(d, (1190, 345), "PREMIER CYCLING RESORT", font(LATL, 74), AQUA, spacing=10)
    centered(d, (1190, H - 56), "渚ベイ  ・  サイクリングリゾート", font(JP, 34), WHITE)
    im.save(os.path.join(OUT, "NB_GW_Wordmark.png"))


def welcome():
    W, H = 1024, 1024
    im = gradient(W, H, WHITE, SAND)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 210], fill=TEAL)
    centered(d, (W // 2, 105), "WELCOME", font(LAT, 130), WHITE, spacing=10)
    centered(d, (W // 2, 285), "ようこそ 渚ベイへ", font(JP, 80), TEAL_D)
    wave_mark(d, W // 2, 470, 140, TEAL, CORAL)
    rows = [("CYCLIST WELCOME", CORAL), ("BIKE STORAGE  ·  BIKE WASH", TEAL), ("ROUTE INFORMATION", TEAL)]
    y = 640
    for t, c in rows:
        d.rounded_rectangle([60, y - 42, W - 60, y + 42], radius=22, fill=c)
        centered(d, (W // 2, y), t, font(LAT, 54), WHITE, spacing=4)
        y += 120
    centered(d, (W // 2, 990), "NAGISA CYCLING CLUB", font(LATL, 50), TEAL_D, spacing=6)
    im.save(os.path.join(OUT, "NB_GW_Welcome.png"))


def skyline(elev):
    W, H = 2048, 512
    im = gradient(W, H, SAND, WHITE)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 16], fill=TEAL)
    d.rectangle([0, H - 16, W, H], fill=CORAL)
    wave_mark(d, 190, 250, 130, TEAL, CORAL)
    centered(d, (1140, 170), "SKYLINE TERRACE", font(LAT, 190), TEAL_D, spacing=12)
    centered(d, (1140, 320), f"スカイラインテラス  ·  {elev:.0f} m above the bay", font(JP, 78), TEAL)
    centered(d, (1140, 430), "NAGISA BAY  ·  PANORAMIC OVERLOOK", font(LATL, 64), INK, spacing=8)
    im.save(os.path.join(OUT, "NB_GW_Skyline.png"))


def panorama(zs):
    W, H = 2048, 768
    im = Image.new("RGB", (W, H), (190, 226, 238))
    d = ImageDraw.Draw(im)
    # stylised panorama: sky -> sea band -> beach -> resort silhouette -> hills
    for y in range(H):
        t = y / H
        c = (int(150 + 60 * t), int(205 + 20 * t), int(238 - 10 * t))
        d.line([(0, y), (W, y)], fill=c)
    d.rectangle([0, 330, W, 520], fill=(60, 190, 200))
    d.polygon([(0, 520), (W, 470), (W, 560), (0, 590)], fill=(246, 228, 178))
    # hills
    pts = [(0, 330)]
    for i in range(0, 41):
        x = i * W / 40
        pts.append((x, 330 - 90 * (0.55 + 0.45 * math.sin(i * 0.62 + 0.8)) - 40 * math.sin(i * 1.7)))
    pts.append((W, 330))
    d.polygon(pts, fill=(88, 140, 112))
    # resort skyline
    import random
    rnd = random.Random(7)
    x = 900
    while x < 1500:
        w = rnd.randint(30, 60); h = rnd.randint(50, 150)
        d.rectangle([x, 470 - h, x + w, 480], fill=(236, 238, 232), outline=(180, 186, 188))
        x += w + rnd.randint(4, 16)
    # islands
    d.ellipse([1580, 318, 1760, 350], fill=(70, 130, 110)); d.ellipse([1760, 322, 1850, 346], fill=(80, 140, 118))
    # marina yachts
    for k in range(7):
        xx = 250 + k * 46
        d.polygon([(xx, 470), (xx + 26, 470), (xx + 13, 430)], fill=WHITE)
    # road ribbon
    d.line([(0, 575), (500, 545), (1100, 540), (1700, 520), (W, 505)], fill=(90, 96, 104), width=10)
    # labels
    def tag(x, y, t, ja):
        d.rounded_rectangle([x - 150, y - 40, x + 150, y + 40], radius=18, fill=TEAL_D)
        centered(d, (x, y - 10), t, font(LAT, 36), WHITE)
        centered(d, (x, y + 22), ja, font(JP, 22), AQUA)
        d.line([(x, y + 40), (x, y + 78)], fill=TEAL_D, width=5)
    tag(330, 240, "NAGISA MARINA", "ナギサ・マリーナ")
    tag(1000, 230, "RESORT DISTRICT", "リゾート地区")
    tag(1400, 130, "NAGISA BEACH", "渚ビーチ")
    tag(1720, 260, "OUTER ISLANDS", "沖の島々")
    tag(660, 690, "THE ROAD YOU RODE", "走ってきた道")
    d.rectangle([0, 0, W, 76], fill=TEAL)
    centered(d, (W // 2, 38), "NAGISA BAY PANORAMA  ·  渚ベイ全景", font(LAT, 50), WHITE, spacing=6)
    im.save(os.path.join(OUT, "NB_GW_Panorama.png"))


def kiosk():
    W, H = 1024, 1024
    im = Image.new("RGB", (W, H), TEAL_D)
    d = ImageDraw.Draw(im)
    # fascia (top 256 px)
    d.rectangle([0, 0, W, 255], fill=CORAL)
    d.rectangle([0, 238, W, 255], fill=WHITE)
    centered(d, (W // 2, 100), "SUMMIT CAFE", font(LAT, 130), WHITE, spacing=8)
    centered(d, (W // 2, 188), "サミットカフェ  ·  COFFEE · SHAVE ICE", font(JP, 44), WHITE)
    # menu board (bottom 768)
    d.rectangle([0, 256, W, H], fill=(30, 44, 48))
    d.rectangle([24, 280, W - 24, H - 24], outline=WHITE, width=6)
    centered(d, (W // 2, 340), "RIDER'S MENU", font(LAT, 80), GOLD, spacing=6)
    items = [("Flat White", "¥520"), ("Iced Latte", "¥560"), ("Espresso Tonic", "¥600"), ("Shave Ice Mango", "¥680"),
             ("Banana Bread", "¥450"), ("Energy Onigiri", "¥380"), ("Electrolyte Refill", "FREE")]
    y = 440
    for n, p in items:
        d.text((90, y), n, font=font(LAT, 56), fill=WHITE, anchor="lm")
        d.text((W - 90, y), p, font=font(LAT, 56), fill=AQUA, anchor="rm")
        d.line([(90, y + 40), (W - 90, y + 40)], fill=(70, 90, 96), width=3)
        y += 92
    im.save(os.path.join(OUT, "NB_GW_Kiosk.png"))


def direction(zs):
    W, H = 1024, 512
    im = Image.new("RGB", (W, H), TEAL_D)
    d = ImageDraw.Draw(im)
    rows = [("NAGISA MARINA", "ナギサ・マリーナ", "←", "6.1 km"),
            ("BEACH PROMENADE", "ビーチプロムナード", "←", "4.7 km"),
            ("PALM RIDGE KOM", "パームリッジ", "→", "0.1 km"),
            ("NAGISA BRIDGE", "渚大橋", "→", "7.0 km")]
    rh = H // 4
    for i, (t, ja, a, k) in enumerate(rows):
        y = i * rh
        d.rectangle([6, y + 6, W - 7, y + rh - 6], fill=TEAL if i % 2 == 0 else (22, 132, 146), outline=WHITE, width=4)
        d.text((40, y + rh * 0.38), t, font=font(LAT, 46), fill=WHITE, anchor="lm")
        d.text((40, y + rh * 0.76), ja, font=font(JP, 26), fill=AQUA, anchor="lm")
        d.text((W - 40, y + rh * 0.5), k, font=font(LAT, 52), fill=GOLD, anchor="rm")
    im.save(os.path.join(OUT, "NB_GW_Direction.png"))


def bridge():
    W, H = 1024, 256
    im = gradient(W, H, WHITE, SAND)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, 14], fill=TEAL); d.rectangle([0, H - 14, W, H], fill=CORAL)
    wave_mark(d, 110, 130, 80, TEAL, CORAL)
    centered(d, (590, 100), "NAGISA BRIDGE", font(LAT, 112), TEAL_D, spacing=8)
    centered(d, (590, 195), "渚大橋  ·  1.2 km over the open sea", font(JP, 40), TEAL)
    im.save(os.path.join(OUT, "NB_GW_Bridge.png"))


def lane():
    S = 512
    im = Image.new("RGB", (S, S), (38, 150, 104))
    d = ImageDraw.Draw(im)
    # asphalt grain
    import random
    rnd = random.Random(3)
    for _ in range(6000):
        x, y = rnd.randrange(S), rnd.randrange(S)
        v = rnd.randint(-12, 12)
        d.point((x, y), fill=(38 + v, 150 + v, 104 + v))
    # edge lines (u runs across the lane; v along the road)
    d.rectangle([0, 0, 10, S], fill=WHITE); d.rectangle([S - 11, 0, S, S], fill=WHITE)
    # bike pictogram, drawn pointing along +v (up in the image)
    cx, cy = S // 2, S // 2
    w = (250, 250, 244)
    d.ellipse([cx - 46, cy - 150, cx + 46, cy - 58], outline=w, width=14)     # front wheel
    d.ellipse([cx - 46, cy + 70, cx + 46, cy + 162], outline=w, width=14)     # rear wheel
    d.line([(cx, cy - 104), (cx, cy - 10)], fill=w, width=14)                  # fork / down tube
    d.line([(cx, cy - 10), (cx, cy + 116)], fill=w, width=14)                  # frame
    d.line([(cx - 38, cy - 34), (cx + 38, cy - 34)], fill=w, width=14)         # handlebar
    d.ellipse([cx - 16, cy + 20, cx + 16, cy + 52], fill=w)                     # rider
    im.save(os.path.join(OUT, "NB_GW_Lane.png"))


if __name__ == "__main__":
    zs, samples = route_info()
    # overlook elevation: highest sample inside the Skyline Terrace window (matches Overlook.cs OvDeckFromM..OvDeckToM)
    el = max(s["p"][1] for s in samples if 8080 <= s["d"] <= 8300)
    wordmark(); welcome(); skyline(el); panorama(zs); kiosk(); direction(zs); bridge(); lane()
    print("wrote", OUT, "overlook elevation", round(el, 1))

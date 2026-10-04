"""Nagisa Bay cycling-culture sign / map / vending / bike-lane textures (Claude worker F, 2026-10-01).

Pure Pillow, no Blender. Writes Assets/Environment/NagisaBay/Textures/Cycling/*.png, consumed by
Assets/Editor/NagisaBayEnvironment.CyclingCulture.cs (slots NBC_SignFace / NBC_MapFace / NBC_VendFace
are remapped per instance; the bike lane strip is a C# mesh). All brand text is fictional.
Run:  python tools/textures/make_nagisa_cycling_signs.py
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures", "Cycling")
ROUTE = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "NagisaRoute.json")
EN = r"C:\Windows\Fonts\arialbd.ttf"
JP = r"C:\Windows\Fonts\YuGothB.ttc"

NAVY = (14, 44, 78)
TEAL = (16, 128, 140)
CORAL = (255, 104, 84)
SAND = (248, 238, 218)
WHITE = (250, 250, 246)
AMBER = (255, 190, 40)
INK = (24, 28, 34)
MINT = (120, 214, 190)


def font(path, px):
    try:
        return ImageFont.truetype(path, px)
    except OSError:
        return ImageFont.load_default()


def fit(d, text, path, max_w, px):
    while px > 12:
        f = font(path, px)
        if d.textlength(text, font=f) <= max_w:
            return f
        px -= 2
    return font(path, px)


def centered(d, w, y, text, path, px, fill, max_w=None):
    f = fit(d, text, path, max_w or w - 80, px)
    d.text(((w - d.textlength(text, font=f)) / 2, y), text, font=f, fill=fill)


def bike_icon(d, cx, cy, s, fill, width=None):
    """Simple pictogram bicycle, scale s ~ wheel radius."""
    lw = width or max(3, int(s * 0.12))
    r = s
    for sx in (-1, 1):
        x = cx + sx * 1.35 * r
        d.ellipse([x - r, cy - r + r * 0.4, x + r, cy + r + r * 0.4], outline=fill, width=lw)
    ax, bx = cx - 1.35 * r, cx + 1.35 * r
    y0 = cy + r * 0.4
    pts = [(ax, y0), (cx - 0.15 * r, cy - 0.45 * r), (cx + 0.9 * r, cy - 0.45 * r), (bx, y0)]
    d.line(pts, fill=fill, width=lw)
    d.line([(cx - 0.15 * r, cy - 0.45 * r), (cx + 0.2 * r, y0), (ax, y0)], fill=fill, width=lw)
    d.line([(cx + 0.2 * r, y0), (cx + 0.9 * r, cy - 0.45 * r)], fill=fill, width=lw)
    d.line([(cx + 0.9 * r, cy - 0.45 * r), (cx + 0.8 * r, cy - 0.85 * r), (cx + 1.15 * r, cy - 0.85 * r)], fill=fill, width=lw)
    d.line([(cx - 0.35 * r, cy - 0.7 * r), (cx + 0.05 * r, cy - 0.7 * r)], fill=fill, width=lw)


def board(key, en, jp, sub=None, bg=NAVY, accent=CORAL, W=1024, H=384, icon=True):
    im = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([8, 8, W - 9, H - 9], 28, outline=WHITE, width=8)
    d.rectangle([24, H - 62, W - 25, H - 28], fill=accent)
    if icon:
        bike_icon(d, 140, 150, 40, WHITE)
        tx0, tw = 250, W - 280
    else:
        tx0, tw = 40, W - 80
    f = fit(d, en, EN, tw, 92)
    d.text((tx0 + (tw - d.textlength(en, font=f)) / 2, 54), en, font=f, fill=WHITE)
    fj = fit(d, jp, JP, tw, 62)
    d.text((tx0 + (tw - d.textlength(jp, font=fj)) / 2, 178), jp, font=fj, fill=MINT)
    if sub:
        centered(d, W, H - 60, sub, EN, 26, WHITE, W - 90)
    im.save(os.path.join(OUT, "NBC_Sign_%s.png" % key))


def blade(key, en, jp, bg, fg=WHITE, accent=AMBER, glyph=None):
    W = H = 512
    im = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([10, 10, W - 11, H - 11], 40, outline=fg, width=10)
    if glyph == "bike":
        bike_icon(d, 256, 150, 52, fg)
    elif glyph == "cup":
        d.rounded_rectangle([170, 100, 320, 210], 18, fill=fg)
        d.arc([300, 120, 370, 190], 270, 90, fill=fg, width=14)
        for k in range(3):
            d.line([(200 + k * 40, 78), (212 + k * 40, 50)], fill=accent, width=8)
    elif glyph == "bread":
        d.ellipse([150, 100, 360, 210], fill=accent)
        for k in range(3):
            d.line([(200 + k * 45, 120), (225 + k * 45, 190)], fill=bg, width=9)
    elif glyph == "cone":
        d.polygon([(256, 230), (200, 140), (312, 140)], fill=accent)
        d.ellipse([196, 70, 316, 160], fill=fg)
    elif glyph == "store":
        d.rectangle([160, 110, 352, 215], fill=fg)
        d.rectangle([160, 110, 352, 140], fill=accent)
        d.rectangle([240, 160, 280, 215], fill=bg)
    fj = fit(d, jp, JP, W - 70, 110)
    d.text(((W - d.textlength(jp, font=fj)) / 2, 235), jp, font=fj, fill=fg)
    centered(d, W, 390, en, EN, 66, accent, W - 70)
    im.save(os.path.join(OUT, "NBC_Blade_%s.png" % key))


def aframe(key, lines, bg=(32, 36, 40)):
    W, H = 512, 768
    im = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([12, 12, W - 13, H - 13], 24, outline=SAND, width=8)
    y = 40
    for text, px, col, path in lines:
        f = fit(d, text, path, W - 70, px)
        d.text(((W - d.textlength(text, font=f)) / 2, y), text, font=f, fill=col)
        y += int(px * 1.35)
    im.save(os.path.join(OUT, "NBC_Aframe_%s.png" % key))


def vend(key, base, can_cols):
    W, H = 512, 1024
    im = Image.new("RGB", (W, H), base)
    d = ImageDraw.Draw(im)
    d.rectangle([14, 14, W - 15, 90], fill=WHITE)
    centered(d, W, 18, "ICE COLD  冷たい", JP, 50, base, W - 40)
    for r in range(5):
        for c in range(4):
            x = 34 + c * 112
            y = 120 + r * 150
            col = can_cols[(r * 4 + c) % len(can_cols)]
            d.rounded_rectangle([x, y, x + 78, y + 118], 12, fill=col)
            d.rectangle([x, y + 38, x + 78, y + 66], fill=WHITE)
            d.text((x + 10, y + 120), str(130 + 10 * ((r * 4 + c) % 5)), font=font(EN, 20), fill=WHITE)
    d.rectangle([14, 880, W - 15, H - 15], fill=(18, 22, 26))
    d.rectangle([60, 905, 260, 990], fill=(60, 66, 74))
    d.ellipse([360, 905, 440, 985], fill=AMBER)
    im.save(os.path.join(OUT, "NBC_Vend_%s.png" % key))


def route_map():
    r = json.load(open(ROUTE, encoding="utf-8"))
    S = r["samples"]
    xs = [s["p"][0] for s in S]
    zs = [s["p"][2] for s in S]
    W, H = 1024, 704
    im = Image.new("RGB", (W, H), (214, 238, 246))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, 78], fill=NAVY)
    d.text((28, 12), "NAGISA BAY  CYCLING ROUTE", font=font(EN, 38), fill=WHITE)
    d.text((W - 340, 24), "渚ベイ サイクリングルート", font=font(JP, 26), fill=MINT)
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    pad, top, bot = 50, 96, 190
    sc = min((W - 2 * pad) / max(1, x1 - x0), (H - top - bot) / max(1, z1 - z0))
    def pt(i):
        return (pad + (xs[i] - x0) * sc, H - bot - (zs[i] - z0) * sc - 10)
    ox = (W - (x1 - x0) * sc) / 2 - pad
    pts = [(pt(i)[0] + ox, pt(i)[1]) for i in range(0, len(S), 3)]
    d.rectangle([20, top - 6, W - 20, H - bot + 6], fill=(236, 244, 232))
    d.line(pts, fill=WHITE, width=17)
    d.line(pts, fill=CORAL, width=9)
    for cp in r["checkpoints"]:
        i = min(range(len(S)), key=lambda k: abs(S[k]["d"] - cp["distance"]))
        px, py = pt(i)
        px += ox
        d.ellipse([px - 11, py - 11, px + 11, py + 11], fill=NAVY, outline=WHITE, width=4)
    # profile strip
    ys = [s["p"][1] for s in S]
    y0, y1 = min(ys), max(ys)
    L = S[-1]["d"]
    d.rectangle([20, H - bot + 18, W - 20, H - 20], fill=WHITE)
    prof = [(34 + (S[i]["d"] / L) * (W - 68), H - 36 - (ys[i] - y0) / max(1, y1 - y0) * (bot - 80)) for i in range(0, len(S), 4)]
    d.line(prof, fill=TEAL, width=5)
    d.text((34, H - bot + 22), "ELEVATION   16.2 km   max +%d m" % (y1 - y0), font=font(EN, 24), fill=NAVY)
    # legend
    d.ellipse([34, 108, 56, 130], fill=CORAL)
    d.text((64, 106), "ROUTE", font=font(EN, 22), fill=NAVY)
    d.text((34, 138), "● CHECKPOINTS", font=font(EN, 20), fill=NAVY)
    d.text((W - 270, 108), "YOU ARE HERE ▶ 現在地", font=font(JP, 22), fill=CORAL)
    im.save(os.path.join(OUT, "NBC_Map.png"))


def bike_lane():
    """64 x 2048 strip = 0.95 m x 40 m: jade green, white bicycle pictogram, a worn-paint speckle."""
    W, H = 64, 2048
    im = Image.new("RGB", (W, H), (46, 150, 128))
    d = ImageDraw.Draw(im)
    import random
    rnd = random.Random(5)
    for _ in range(900):
        x, y = rnd.randrange(W), rnd.randrange(H)
        c = rnd.choice([(52, 160, 136), (40, 138, 118), (58, 168, 142)])
        d.point((x, y), fill=c)
    # pictogram: side-view bike drawn on a small canvas, rotated so the wheelbase runs along travel (+v = up the image)
    icon = Image.new("RGB", (120, 64), (46, 150, 128))
    bike_icon(ImageDraw.Draw(icon), 60, 30, 14, WHITE, width=4)
    icon = icon.rotate(90, expand=True).resize((46, 92))      # 46 wide x 92 long; rotate(90) puts the front wheel on top
    for k in range(4):
        cy = 270 + k * 500
        im.paste(icon, (9, cy - 46))
    d.rectangle([0, 0, 2, H], fill=(236, 240, 236))
    im.save(os.path.join(OUT, "NBC_BikeLane.png"))


def main():
    os.makedirs(OUT, exist_ok=True)
    board("cyclist_welcome", "CYCLIST WELCOME", "サイクリスト歓迎", "Bike storage · Bike wash · Route information")
    board("bike_storage", "BIKE STORAGE", "自転車置き場", "Secure · Covered · Guests of the resort")
    board("bike_wash", "BIKE WASH", "バイクウォッシュ", "Hose · Brushes · Degreaser")
    board("route_info", "ROUTE INFORMATION", "ルート案内", "Maps · Climb profile · Weather")
    board("club", "NAGISA CYCLING CLUB", "渚サイクリングクラブ", "Group rides daily 06:30", bg=NAVY, accent=AMBER)
    board("cafe", "NAGISA CYCLING CAFE", "渚サイクリングカフェ", "Espresso · Onigiri · Cold brew", bg=TEAL, accent=CORAL)
    board("rental", "BIKE RENTAL", "レンタサイクル", "Road · Gravel · E-bike")
    board("shop", "BIKE SHOP · SERVICE", "自転車専門店", "Fitting · Repairs · Parts", bg=INK, accent=CORAL)
    board("water", "WATER REFILL", "給水所", "Free · Chilled · Bring a bottle", bg=TEAL, accent=WHITE)
    board("repair", "REPAIR STATION", "修理ステーション", "Pump · Tools · Stand", bg=NAVY, accent=MINT)
    board("beach_access", "BEACH ACCESS", "ビーチ入口", "Showers · Lifeguard 09-18", bg=TEAL, accent=AMBER, icon=False)
    board("overlook", "OCEAN OVERLOOK", "展望テラス", "Photo point", bg=NAVY, accent=CORAL, icon=False)
    board("promenade", "NAGISA PROMENADE", "渚プロムナード", "Cyclists keep left · Pedestrians keep right", bg=NAVY, accent=MINT, icon=False)
    board("resort", "NAGISA BAY RESORT", "渚ベイリゾート", "Welcome", bg=(18, 70, 96), accent=AMBER, icon=False)
    board("marina_dir", "MARINA  ▶", "渚マリーナ", "Cafés · Boardwalk · Yacht Club", bg=NAVY, accent=CORAL, icon=False)
    board("town_dir", "TOWN CENTRE  ▶", "リゾートタウン", "Shops · Bakery · Konbini", bg=(60, 40, 90), accent=AMBER, icon=False)
    blade("bakery", "BAKERY", "パン屋", (150, 84, 52), WHITE, AMBER, "bread")
    blade("konbini", "24H", "コンビニ", (24, 120, 170), WHITE, AMBER, "store")
    blade("cafe", "CAFE", "喫茶", (34, 90, 70), WHITE, AMBER, "cup")
    blade("icecream", "ICE CREAM", "アイス", (230, 120, 150), WHITE, (255, 236, 160), "cone")
    blade("bike", "BIKES", "自転車", NAVY, WHITE, CORAL, "bike")
    blade("welcome", "CYCLISTS", "歓迎", TEAL, WHITE, AMBER, "bike")
    blade("storage", "BIKE STORAGE", "駐輪", NAVY, WHITE, MINT, "bike")
    blade("souvenir", "SOUVENIRS", "お土産", (120, 60, 110), WHITE, AMBER, "store")
    aframe("coffee", [("COFFEE", 110, AMBER, EN), ("本日のコーヒー", 52, WHITE, JP), ("Flat white  ¥480", 42, WHITE, EN),
                      ("Iced latte  ¥520", 42, WHITE, EN), ("Cyclists: free water refill", 34, MINT, EN)])
    aframe("repair", [("BIKE REPAIR", 84, CORAL, EN), ("自転車修理", 62, WHITE, JP), ("Puncture · 15 min", 40, WHITE, EN),
                      ("Tune-up · 1 hr", 40, WHITE, EN), ("Walk-ins welcome", 34, MINT, EN)])
    aframe("today", [("TODAY", 100, AMBER, EN), ("本日のおすすめ", 56, WHITE, JP), ("Melon pan  ¥220", 42, WHITE, EN),
                     ("Anpan  ¥180", 42, WHITE, EN), ("Fresh from the oven", 32, MINT, EN)])
    vend("drinks", (200, 36, 46), [(255, 120, 40), (40, 150, 220), (60, 180, 90), (240, 200, 40), (210, 60, 120)])
    vend("blue", (24, 104, 168), [(240, 240, 240), (255, 150, 30), (60, 170, 110), (180, 60, 180)])
    route_map()
    bike_lane()
    print("wrote textures to", OUT)


main()

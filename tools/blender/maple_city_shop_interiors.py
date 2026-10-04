"""
Shop interiors for the Maple City ShopGlazing atlas, used by shop_glazing() in
tools/blender/build_maple_city_street_textures.py (pure PIL/numpy).

Cell k of the 4x4 ShopGlazing atlas now shows the interior of SHOPS[k] - the same trade as
fascia sign k - so a BAKERY window shows bread and a CYCLES window shows bikes on the wall.
Before, eight generic interiors (three of them random-colour "rainbow" shelving) were picked
independently of the sign. MapleCityEnvironment.StreetBuilding now uses the fascia index for
the glazing cell too.

Values stay <= 235: this atlas is drawn UNLIT and meant to glow warm, but not blow out.
"""
import random

import numpy as np
from PIL import Image, ImageDraw

CW, CH = 512, 320
FLOOR = int(CH * 0.80)


def _cl(c, hi=235):
    return tuple(int(max(0, min(hi, v))) for v in c)


def _mul(c, k):
    return _cl([v * k for v in c])


def _room(d, wall, floor):
    for y in range(CH):
        t = y / CH
        d.line((0, y, CW, y), fill=_cl([v * (1.06 - 0.30 * t) for v in wall]))
    d.rectangle((0, FLOOR, CW, CH), fill=floor)
    d.line((0, FLOOR, CW, FLOOR), fill=_mul(floor, 0.7), width=2)


def _pendants(d, rng, xs=None, shade=(255, 232, 186), drop=40):
    xs = xs or list(range(70, CW, 150))
    for lx in xs:
        d.line((lx, 0, lx, drop), fill=(56, 46, 38), width=2)
        d.pieslice((lx - 20, drop - 8, lx + 20, drop + 24), 180, 360, fill=_mul(shade, 0.8))
        d.ellipse((lx - 9, drop + 4, lx + 9, drop + 14), fill=shade)


def _spots(d, xs, y=6):
    for x in xs:
        d.rectangle((x - 6, y, x + 6, y + 10), fill=(40, 40, 42))
        d.polygon([(x - 4, y + 10), (x + 4, y + 10), (x + 40, CH), (x - 40, CH)],
                  fill=None)


def _person(d, x, y, body, head=(44, 36, 34), s=1.0):
    d.ellipse((x - 13 * s, y - 28 * s, x + 13 * s, y - 2 * s), fill=head)
    d.rounded_rectangle((x - 22 * s, y, x + 22 * s, y + 62 * s), int(10 * s), fill=body)


def _shelves(d, ys, x0=20, x1=CW - 20, col=(92, 66, 46), t=6):
    for sy in ys:
        d.rectangle((x0, sy, x1, sy + t), fill=col)


# --------------------------------------------------------------------------- the 16 trades

def bakery(d, rng):
    _room(d, (214, 180, 132), (120, 84, 56))
    _shelves(d, (96, 150))
    for sy in (96, 150):
        x = 28
        while x < CW - 60:
            w = rng.randint(34, 52)
            c = rng.choice([(196, 136, 66), (172, 104, 48), (214, 170, 100), (150, 92, 50)])
            d.ellipse((x, sy - 22, x + w, sy + 2), fill=c)
            d.arc((x + 6, sy - 18, x + w - 6, sy - 4), 200, 340, fill=_mul(c, 1.25), width=2)
            x += w + rng.randint(4, 10)
    # glass pastry counter
    d.rectangle((30, 196, CW - 30, FLOOR), fill=(104, 72, 50))
    d.rectangle((34, 174, CW - 34, 198), fill=(186, 192, 186))
    for x in range(44, CW - 60, 28):
        d.ellipse((x, 180, x + 20, 194), fill=rng.choice([(210, 160, 90), (230, 200, 140), (170, 80, 70)]))
    _pendants(d, rng, [90, 256, 422])


def books(d, rng):
    _room(d, (150, 112, 80), (96, 70, 50))
    spines = [(112, 40, 44), (40, 56, 96), (74, 90, 52), (170, 132, 60), (206, 196, 170),
              (60, 44, 40), (128, 82, 60), (86, 100, 110)]
    for case_x in (14, 262):
        d.rectangle((case_x, 30, case_x + 236, FLOOR), fill=(70, 48, 34))
        for sy in range(78, FLOOR, 48):
            d.rectangle((case_x + 6, sy, case_x + 230, sy + 5), fill=(98, 70, 48))
            x = case_x + 10
            while x < case_x + 222:
                w = rng.randint(6, 13)
                hh = rng.randint(28, 42)
                d.rectangle((x, sy - hh, x + w, sy), fill=_mul(rng.choice(spines), rng.uniform(0.85, 1.1)))
                if rng.random() < 0.06:          # a lying stack
                    x += w
                    d.rectangle((x, sy - 12, x + 24, sy), fill=rng.choice(spines))
                    x += 24
                x += w + 1
    d.line((244, 40, 290, FLOOR), fill=(120, 90, 60), width=4)   # library ladder
    d.line((264, 40, 310, FLOOR), fill=(120, 90, 60), width=4)
    for yy in range(60, FLOOR, 30):
        t = (yy - 40) / (FLOOR - 40)
        d.line((244 + 46 * t, yy, 264 + 46 * t, yy), fill=(120, 90, 60), width=3)
    _pendants(d, rng, [130, 380], shade=(250, 220, 160))


def florist(d, rng):
    _room(d, (196, 204, 188), (90, 96, 88))
    for x in range(30, CW, 70):                                    # hanging plants
        d.line((x, 0, x, 50), fill=(80, 70, 60), width=2)
        d.ellipse((x - 26, 44, x + 26, 86), fill=(70, 124, 66))
        d.rectangle((x - 12, 70, x + 12, 88), fill=(170, 110, 80))
    for row, (y, step) in enumerate(((150, 44), (196, 40))):
        for x in range(18 + row * 20, CW - 30, step):
            d.rectangle((x, y + 20, x + 30, y + 64), fill=(84, 96, 104))
            base = rng.choice([(226, 86, 110), (240, 200, 80), (246, 240, 226), (196, 58, 64),
                               (160, 96, 200), (244, 150, 90)])
            for _ in range(6):
                cx, cy = x + rng.randint(0, 30), y + rng.randint(-6, 18)
                d.ellipse((cx - 8, cy - 8, cx + 8, cy + 8), fill=_mul(base, rng.uniform(0.85, 1.05)))
            d.ellipse((x - 4, y + 12, x + 34, y + 30), fill=(70, 130, 70))
    d.rectangle((0, FLOOR, CW, CH), fill=(90, 96, 88))


def coffee(d, rng):
    _room(d, (178, 140, 104), (90, 62, 44))
    d.rectangle((300, 40, 480, 120), fill=(38, 40, 38))                # chalk menu
    for i, y in enumerate(range(54, 112, 14)):
        d.line((316, y, 316 + rng.randint(60, 140), y), fill=(214, 210, 196), width=2)
    _shelves(d, (70,), 24, 270)
    for x in range(34, 260, 22):
        d.rectangle((x, 52, x + 14, 70), fill=rng.choice([(226, 222, 212), (60, 90, 110), (150, 70, 50)]))
    d.rectangle((0, 176, CW, FLOOR), fill=(76, 52, 38))
    d.rectangle((0, 170, CW, 180), fill=(170, 150, 120))
    d.rectangle((60, 130, 160, 172), fill=(190, 196, 200))              # espresso machine
    d.rectangle((70, 140, 150, 160), fill=(120, 124, 128))
    for x in (84, 124):
        d.rectangle((x, 160, x + 12, 170), fill=(40, 40, 40))
    for x in range(200, 300, 24):
        d.rectangle((x, 156, x + 14, 170), fill=(236, 232, 224))
    _person(d, 400, 150, (60, 76, 70))
    _pendants(d, rng, [110, 256, 402])


def ramen(d, rng):
    _room(d, (168, 120, 80), (70, 50, 36))
    for x in range(0, CW, 64):                                       # noren curtain
        d.rectangle((x + 2, 0, x + 60, 60), fill=(150, 44, 40) if (x // 64) % 2 == 0 else (130, 36, 34))
    for x in (90, 420):                                              # paper lanterns
        d.ellipse((x - 22, 70, x + 22, 128), fill=(236, 200, 140))
        d.line((x - 22, 99, x + 22, 99), fill=(180, 60, 50), width=3)
    _person(d, 256, 130, (226, 222, 214), head=(50, 40, 36))           # chef in whites
    for x in (220, 300):                                             # steam
        for k in range(3):
            d.arc((x - 10 + k * 4, 96 - k * 16, x + 10 + k * 4, 116 - k * 16), 180, 360,
                  fill=(236, 230, 220), width=2)
    d.rectangle((0, 190, CW, 226), fill=(128, 92, 58))
    for x in range(40, CW - 20, 70):
        d.ellipse((x, 180, x + 34, 196), fill=(236, 230, 220))
        d.ellipse((x + 6, 182, x + 28, 192), fill=(206, 150, 70))
        d.rectangle((x + 8, 226, x + 26, FLOOR), fill=(60, 44, 34))
        d.ellipse((x + 2, 222, x + 32, 234), fill=(150, 44, 40))


def optician(d, rng):
    _room(d, (214, 214, 206), (150, 146, 138))
    _shelves(d, (80, 128, 176), 30, CW - 30, col=(200, 196, 188), t=4)
    for sy in (80, 128, 176):
        for x in range(44, CW - 60, 44):
            c = rng.choice([(30, 30, 32), (120, 70, 40), (40, 60, 90), (150, 40, 40), (180, 170, 160)])
            d.ellipse((x, sy - 18, x + 16, sy - 4), outline=c, width=3)
            d.ellipse((x + 20, sy - 18, x + 36, sy - 4), outline=c, width=3)
            d.line((x + 16, sy - 12, x + 20, sy - 12), fill=c, width=2)
    d.rectangle((150, 206, 360, FLOOR), fill=(90, 84, 78))
    d.rectangle((140, 200, 370, 210), fill=(230, 226, 218))
    d.ellipse((420, 120, 480, 220), fill=(186, 200, 210))             # mirror
    _pendants(d, rng, [100, 256, 412], shade=(250, 246, 236))


def deli(d, rng):
    _room(d, (200, 186, 156), (110, 90, 70))
    d.rectangle((20, 50, CW - 20, 110), fill=(66, 58, 50))              # back shelf of jars
    for x in range(30, CW - 40, 24):
        d.rounded_rectangle((x, 60, x + 18, 102), 5, fill=rng.choice([(180, 100, 60), (120, 140, 60), (200, 170, 90), (150, 50, 50)]))
    d.rectangle((20, 196, CW - 20, FLOOR), fill=(96, 76, 58))           # glass case of sozai
    d.rectangle((24, 150, CW - 24, 198), fill=(196, 204, 200))
    for x in range(34, CW - 70, 58):
        d.rectangle((x, 166, x + 50, 192), fill=(230, 226, 214))
        c = rng.choice([(200, 120, 60), (90, 140, 70), (230, 200, 120), (170, 60, 60), (130, 90, 60)])
        d.ellipse((x + 4, 168, x + 46, 190), fill=c)
        d.rectangle((x + 14, 152, x + 36, 162), fill=(240, 236, 226))
    _pendants(d, rng, [90, 256, 422])


def tea_house(d, rng):
    _room(d, (190, 170, 130), (150, 146, 100))
    for x in range(20, CW - 20, 120):                                # shoji screens
        d.rectangle((x, 30, x + 110, 196), fill=(236, 226, 196))
        for gx in range(x, x + 111, 22):
            d.line((gx, 30, gx, 196), fill=(110, 80, 56), width=3)
        for gy in range(30, 197, 28):
            d.line((x, gy, x + 110, gy), fill=(110, 80, 56), width=3)
    d.rectangle((0, FLOOR - 20, CW, CH), fill=(156, 150, 104))           # tatami
    for x in range(0, CW, 128):
        d.line((x, FLOOR - 20, x, CH), fill=(90, 84, 60), width=3)
    for x in (120, 360):                                             # low tables + guests
        d.rectangle((x - 60, 222, x + 60, 232), fill=(80, 54, 36))
        d.ellipse((x - 10, 214, x + 10, 224), fill=(110, 130, 90))
        _person(d, x - 70, 186, (84, 70, 110), s=0.8)
        _person(d, x + 70, 186, (110, 60, 60), s=0.8)
    for x in (256,):
        d.ellipse((x - 18, 0, x + 18, 50), fill=(236, 212, 160))


def gallery(d, rng):
    _room(d, (228, 226, 220), (170, 164, 156))
    for i, x in enumerate(range(30, CW - 80, 120)):
        w, h = rng.randint(70, 100), rng.randint(60, 110)
        y = 70 + (110 - h) // 2
        d.rectangle((x - 4, y - 4, x + w + 4, y + h + 4), fill=(40, 36, 32))
        # abstract painting: 3 colour fields
        cols = [(rng.randint(60, 220), rng.randint(60, 200), rng.randint(60, 200)) for _ in range(3)]
        d.rectangle((x, y, x + w, y + h), fill=cols[0])
        d.rectangle((x, y + h // 2, x + w, y + h), fill=cols[1])
        d.ellipse((x + w // 4, y + h // 4, x + 3 * w // 4, y + 3 * h // 4), fill=cols[2])
        d.rectangle((x + w // 2 - 6, 4, x + w // 2 + 6, 14), fill=(40, 40, 42))
    d.rectangle((180, 230, 330, 244), fill=(90, 80, 70))                 # bench
    _person(d, 420, 190, (50, 50, 60), s=0.9)


def sushi(d, rng):
    _room(d, (196, 170, 130), (90, 70, 50))
    d.rectangle((0, 0, CW, 40), fill=(40, 60, 70))                       # indigo noren
    _person(d, 180, 124, (230, 228, 222))
    _person(d, 340, 124, (230, 228, 222))
    d.rectangle((0, 176, CW, 214), fill=(200, 176, 132))                  # hinoki counter
    d.rectangle((40, 150, CW - 40, 178), fill=(196, 210, 214))            # neta case
    for x in range(52, CW - 60, 34):
        c = rng.choice([(226, 120, 100), (236, 150, 90), (240, 220, 210), (180, 60, 60)])
        d.rectangle((x, 160, x + 26, 172), fill=c)
    d.rectangle((0, 214, CW, FLOOR), fill=(110, 84, 58))
    for x in range(60, CW, 100):
        d.rectangle((x, 214, x + 20, FLOOR), fill=(60, 44, 34))


def cycles(d, rng):
    _room(d, (180, 184, 184), (92, 94, 96))
    # bikes hung on the wall: two wheels + a diamond frame each
    for i, x in enumerate((20, 190, 360)):
        y = 60 + (i % 2) * 20
        frame = rng.choice([(200, 50, 50), (40, 90, 160), (230, 230, 226), (30, 30, 34), (60, 150, 120)])
        r = 34
        d.ellipse((x, y + 30, x + 2 * r, y + 30 + 2 * r), outline=(30, 30, 32), width=5)
        d.ellipse((x + 88, y + 30, x + 88 + 2 * r, y + 30 + 2 * r), outline=(30, 30, 32), width=5)
        rear, front = (x + r, y + 30 + r), (x + 88 + r, y + 30 + r)
        seat, head, bb = (x + 52, y + 26), (x + 112, y + 26), (x + 70, y + 30 + r + 6)
        for a, b in ((rear, seat), (seat, bb), (bb, rear), (seat, head), (head, bb), (head, front)):
            d.line((a, b), fill=frame, width=5)
        d.line((seat[0] - 10, seat[1] - 6, seat[0] + 10, seat[1] - 6), fill=(30, 30, 32), width=4)
        d.line((head[0], head[1], head[0] + 10, head[1] - 10), fill=(30, 30, 32), width=3)
    _shelves(d, (200,), 20, CW - 20, col=(70, 70, 74))
    for x in range(34, CW - 40, 40):                                  # helmets
        d.pieslice((x, 176, x + 30, 216), 180, 360, fill=rng.choice([(220, 60, 50), (240, 240, 236), (40, 90, 160), (240, 200, 60)]))
    d.rectangle((0, FLOOR, CW, CH), fill=(92, 94, 96))
    for x in (90, 256, 422):
        d.rectangle((x - 30, 2, x + 30, 8), fill=(240, 240, 236))


def pharmacy(d, rng):
    _room(d, (224, 228, 226), (170, 174, 172))
    _shelves(d, (70, 112, 154, 196), 20, 360, col=(200, 204, 204), t=4)
    for sy in (70, 112, 154, 196):
        x = 24
        while x < 350:
            w = rng.randint(12, 22)
            c = rng.choice([(236, 236, 232), (90, 150, 200), (80, 170, 120), (236, 236, 232), (230, 120, 90)])
            d.rectangle((x, sy - rng.randint(18, 30), x + w, sy), fill=c)
            x += w + 2
    d.rectangle((400, 40, 470, 110), fill=(60, 170, 100))               # green cross
    d.rectangle((425, 50, 445, 100), fill=(236, 240, 236))
    d.rectangle((410, 65, 460, 85), fill=(236, 240, 236))
    d.rectangle((380, 200, CW - 10, FLOOR), fill=(200, 204, 204))
    _person(d, 440, 140, (236, 236, 232))
    for x in (100, 256, 412):
        d.rectangle((x - 40, 2, x + 40, 8), fill=(250, 250, 246))


def noodles(d, rng):
    _room(d, (206, 190, 158), (100, 80, 60))
    d.rectangle((20, 40, 200, 150), fill=(170, 180, 180))              # glass noodle room
    d.rectangle((26, 46, 194, 144), fill=(206, 200, 184))
    _person(d, 110, 90, (236, 234, 228), s=0.8)
    d.rectangle((50, 130, 180, 144), fill=(236, 232, 220))              # flour board
    for x in (270, 380):                                             # big pots + steam
        d.rectangle((x, 130, x + 80, 176), fill=(140, 144, 148))
        for k in range(3):
            d.arc((x + 20 + k * 6, 96 - k * 12, x + 50 + k * 6, 126 - k * 12), 180, 360, fill=(236, 232, 226), width=2)
    d.rectangle((0, 176, CW, 220), fill=(150, 112, 70))
    for x in range(30, CW, 64):
        d.ellipse((x, 168, x + 36, 182), fill=(226, 220, 210))
    for x in range(40, CW, 90):
        _person(d, x, 200, (rng.randint(50, 120), 60, 80), s=0.85)


def studio(d, rng):
    _room(d, (120, 120, 124), (70, 70, 72))
    d.rectangle((150, 20, 362, FLOOR + 10), fill=(214, 206, 190))          # paper backdrop roll
    d.rectangle((140, 12, 372, 24), fill=(40, 40, 42))
    for x in (70, 440):                                              # softboxes
        d.line((x, 120, x, FLOOR), fill=(30, 30, 32), width=3)
        d.polygon([(x - 36, 60), (x + 36, 60), (x + 22, 130), (x - 22, 130)], fill=(244, 242, 236))
    d.line((256, 180, 236, FLOOR), fill=(30, 30, 32), width=3)             # tripod
    d.line((256, 180, 276, FLOOR), fill=(30, 30, 32), width=3)
    d.rectangle((244, 166, 268, 182), fill=(26, 26, 28))
    for x in (20, 400):
        d.rectangle((x, 180, x + 70, 230), fill=(30, 30, 30))
        d.rectangle((x + 5, 185, x + 65, 225), fill=(rng.randint(100, 200), rng.randint(100, 180), 150))


def records(d, rng):
    _room(d, (60, 56, 70), (50, 46, 52))
    for x in range(20, CW - 60, 96):                                  # posters
        d.rectangle((x, 30, x + 72, 120), fill=(rng.randint(120, 230), rng.randint(60, 200), rng.randint(60, 200)))
        d.ellipse((x + 16, 50, x + 56, 90), fill=(30, 28, 30))
    for x in range(20, CW - 20, 160):                                 # crate bins
        d.rectangle((x, 176, x + 140, FLOOR), fill=(120, 86, 56))
        for k in range(12):
            xx = x + 6 + k * 11
            d.rectangle((xx, 150 + rng.randint(0, 10), xx + 8, 178), fill=rng.choice([(200, 60, 60), (230, 200, 90), (60, 90, 160), (220, 220, 214), (40, 40, 40)]))
    d.ellipse((380, 118, 440, 148), fill=(20, 20, 22))                   # turntable
    _pendants(d, rng, [110, 400], shade=(255, 200, 150))


def bistro(d, rng):
    _room(d, (170, 110, 80), (80, 56, 42))
    _shelves(d, (80,), 30, CW - 30)
    for x in range(40, CW - 40, 16):                                   # wine wall
        d.rounded_rectangle((x, 44, x + 9, 80), 3, fill=rng.choice([(50, 70, 40), (90, 30, 40), (170, 150, 90)]))
    for x in (110, 300, 450):                                         # tables with cloths
        d.rectangle((x - 50, 196, x + 50, 214), fill=(236, 232, 224))
        d.rectangle((x - 6, 214, x + 6, FLOOR), fill=(60, 44, 34))
        d.ellipse((x - 6, 186, x + 6, 198), fill=(255, 220, 150))       # candle glow
        _person(d, x - 60, 160, (rng.randint(40, 120), 50, 60), s=0.9)
        if rng.random() < 0.7:
            _person(d, x + 60, 160, (60, rng.randint(50, 110), 90), s=0.9)
    _pendants(d, rng, [110, 300, 450], shade=(255, 214, 160))


TRADES = [bakery, books, florist, coffee, ramen, optician, deli, tea_house,
          gallery, sushi, cycles, pharmacy, noodles, studio, records, bistro]


def shop_glazing_cells():
    G = Image.new("RGB", (CW * 4, CH * 4), (0, 0, 0))
    for k, fn in enumerate(TRADES):
        rng = random.Random(100 + k)
        cell = Image.new("RGB", (CW, CH))
        d = ImageDraw.Draw(cell)
        fn(d, rng)
        # mullions, a door on half the cells, and a faint glass reflection streak
        for x in (CW // 3, 2 * CW // 3):
            if rng.random() < 0.5:
                d.rectangle((x - 3, 0, x + 3, CH), fill=(36, 34, 32))
        if k % 2 == 1:
            dx = rng.choice([24, CW - 144])
            d.rectangle((dx, 40, dx + 120, CH), outline=(40, 36, 32), width=8)
            d.rectangle((dx + 92, 170, dx + 98, 206), fill=(200, 190, 160))
        arr = np.asarray(cell, np.float32)
        yy = np.linspace(0, 1, CH)[:, None]
        xx = np.linspace(0, 1, CW)[None, :]
        streak = np.clip(1 - np.abs((xx * 1.2 - yy * 0.6) - 0.45) * 6, 0, 1)[..., None]
        arr = arr * 0.9 + streak * 34
        cell = Image.fromarray(np.clip(arr, 0, 240).astype(np.uint8))
        G.paste(cell, ((k % 4) * CW, (k // 4) * CH))
    return G


if __name__ == "__main__":
    shop_glazing_cells().save("ShopGlazing_preview.png")   # preview only

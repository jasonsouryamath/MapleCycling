"""Maple City street-wall textures (task C7 - street wall + ground level).

Pure Python + PIL/numpy - no Blender needed:
    python tools/blender/build_maple_city_street_textures.py

Writes Assets/Environment/MapleCity/Street/Textures/MapleStreet_*.png

  Facade_<Kind>_Albedo / _Normal   tiling upper-storey cladding. ONE TILE = 4 bays x 4 storeys
                                   (12.0 m x 13.6 m). Kinds: Brick, Limestone, Metal, Glass,
                                   Plaster, Timber. Windows are painted IN (frame, reveal
                                   shadow, sill, glass with sky reflection, some lit rooms) so
                                   the facade reads recessed without per-window geometry.
  Plain                            near-white noise card; trim/cornice/furniture take their
                                   colour from vertex tint through the CityFacade shader.
  ShopAtlas                        2048x2048: 16 fascia signs (top half, 1024x128 cells) and
                                   8 striped awning canvases (bottom half, 1024x256 cells).
  ShopGlazing                      2048x1280: 16 lit shop interiors (512x320 cells), drawn
                                   UNLIT in Unity so the ground floor glows warm.
  Pavement                         1024x512 = 5.0 m (kerb -> building line) x 2.5 m: granite
                                   kerb top, basalt-sett furniture strip, yellow tactile line,
                                   stretcher-bond concrete pavers.
  Kerb                             256x256 speckled granite for the kerb face.

COLOUR RULE: MapleCity's cel/facade shaders use texture BYTES as albedo (MR_Authored undoes
the sRGB decode), and pale albedo compresses to white under the cel grade - so every
non-emissive surface here stays at or below ~190/255 (0.75). Only the unlit glazing and the
lit-room windows go brighter, because they are meant to read as light.

All shop words are generic nouns (BAKERY, BOOKS...) - no real brands, no logos.
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "MapleCity", "Street", "Textures")
FONTS = r"C:\Windows\Fonts"

TILE = 1024          # facade tile px (square; maps to 12.0 m x 13.6 m)
BAYS, STOREYS = 4, 4
BW = TILE // BAYS    # px per bay
SH = TILE // STOREYS # px per storey
MAXV = 190           # albedo byte ceiling for lit surfaces


def font(name, size):
    try:
        return ImageFont.truetype(os.path.join(FONTS, name), size)
    except OSError:
        return ImageFont.truetype(os.path.join(FONTS, "arialbd.ttf"), size)


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, f"MapleStreet_{name}.png")
    img.convert("RGB").save(path, optimize=True)
    print(f"[maple-street-tex] {path}  {img.width}x{img.height}")


def clampv(c, hi=MAXV):
    return tuple(int(max(0, min(hi, v))) for v in c)


def jitter(c, rng, amt):
    return clampv([v + rng.uniform(-amt, amt) for v in c])


def noise(w, h, scale, seed, octaves=3):
    """Cheap value noise in [0,1], tileable (wraps via np.roll-friendly upscale)."""
    rng = np.random.default_rng(seed)
    acc = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        gw, gh = max(2, int(w / scale * 2 ** o)), max(2, int(h / scale * 2 ** o))
        g = rng.random((gh, gw)).astype(np.float32)
        img = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        acc += np.asarray(img, np.float32) / 255.0 * amp
        tot += amp
        amp *= 0.5
    return acc / tot


def apply_grain(img, strength, seed, scale=24):
    a = np.asarray(img.convert("RGB"), np.float32)
    n = noise(img.width, img.height, scale, seed)[..., None]
    a = a * (1.0 + (n - 0.5) * strength)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def normal_from_height(h, strength):
    """h: float32 HxW in [0,1]. Returns an RGB normal map (tangent space, +Y up in image)."""
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-gx, gy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8))


# =============================================================== windows

def glass_fill(d, box, rng, lit, frame_col, mull=None):
    """Glass with a sky-reflection gradient, or a lit room. Returns nothing; draws into d."""
    x0, y0, x1, y1 = box
    if lit:
        warm = (rng.randint(215, 240), rng.randint(168, 196), rng.randint(96, 124))
        d.rectangle(box, fill=warm)
        # curtain edges + a ceiling-lamp hotspot, so a lit room is not a flat card
        cw = max(2, (x1 - x0) // 6)
        cur = (rng.randint(150, 185), rng.randint(120, 150), rng.randint(90, 120))
        if rng.random() < 0.7:
            d.rectangle((x0, y0, x0 + cw, y1), fill=cur)
            d.rectangle((x1 - cw, y0, x1, y1), fill=cur)
        d.rectangle((x0, y1 - (y1 - y0) // 4, x1, y1), fill=clampv([v * 0.8 for v in warm], 255))
    else:
        # dusk sky in the upper glass fading to the dark room below
        h = max(1, y1 - y0)
        top = (rng.randint(96, 120), rng.randint(122, 146), rng.randint(150, 172))
        bot = (rng.randint(28, 40), rng.randint(34, 46), rng.randint(46, 58))
        for yy in range(y0, y1 + 1):
            t = (yy - y0) / h
            t = min(1.0, t * 1.5)
            c = tuple(int(top[k] * (1 - t) + bot[k] * t) for k in range(3))
            d.line((x0, yy, x1, yy), fill=c)
        # a diagonal reflection streak
        if rng.random() < 0.6:
            sx = rng.randint(x0, x1)
            d.polygon([(sx, y0), (sx + (x1 - x0) // 5, y0), (sx - (x1 - x0) // 3, y1),
                       (sx - (x1 - x0) // 2, y1)], fill=(120, 140, 160))
        if rng.random() < 0.35:   # drawn blind
            d.rectangle((x0, y0, x1, y0 + int(h * rng.uniform(0.2, 0.55))),
                        fill=(150, 146, 136))
    if mull:
        mx = (x0 + x1) // 2
        d.rectangle((mx - mull, y0, mx + mull, y1), fill=frame_col)


def punched_window(d, hmap, box, rng, lit, frame, reveal, sill, lintel, mull=3, frame_w=5):
    x0, y0, x1, y1 = box
    # reveal shadow (the recess), then the frame, then the glass
    d.rectangle((x0 - 2, y0 - 2, x1 + 2, y1 + 2), fill=reveal)
    d.rectangle((x0 + 4, y0 + 4, x1, y1), fill=frame)
    glass_fill(d, (x0 + 4 + frame_w, y0 + 4 + frame_w, x1 - frame_w, y1 - frame_w), rng, lit,
               frame, mull)
    hmap.rectangle((x0 - 2, y0 - 2, x1 + 2, y1 + 2), fill=40)
    hmap.rectangle((x0 + 4, y0 + 4, x1, y1), fill=90)
    hmap.rectangle((x0 + 4 + frame_w, y0 + 4 + frame_w, x1 - frame_w, y1 - frame_w), fill=20)
    if sill:
        d.rectangle((x0 - 8, y1 + 2, x1 + 8, y1 + 10), fill=sill)
        d.rectangle((x0 - 8, y1 + 10, x1 + 8, y1 + 13), fill=clampv([v * 0.55 for v in sill]))
        hmap.rectangle((x0 - 8, y1 + 2, x1 + 8, y1 + 10), fill=230)
    if lintel:
        d.rectangle((x0 - 6, y0 - 16, x1 + 6, y0 - 3), fill=lintel)
        hmap.rectangle((x0 - 6, y0 - 16, x1 + 6, y0 - 3), fill=200)


# =============================================================== facade kinds

def facade(kind, seed):
    rng = random.Random(seed)
    img = Image.new("RGB", (TILE, TILE))
    d = ImageDraw.Draw(img)
    hm = Image.new("L", (TILE, TILE), 128)
    h = ImageDraw.Draw(hm)
    lit_rate = {"Brick": 0.22, "Limestone": 0.2, "Metal": 0.25, "Glass": 0.18,
                "Plaster": 0.26, "Timber": 0.3}[kind]

    if kind == "Brick":
        base = (150, 84, 62)
        d.rectangle((0, 0, TILE, TILE), fill=(96, 84, 76))       # mortar
        h.rectangle((0, 0, TILE, TILE), fill=100)
        course = 8
        bw = 30
        for row in range(TILE // course):
            off = (bw // 2) if row % 2 else 0
            y = row * course
            for x in range(-bw, TILE + bw, bw):
                c = jitter(base, rng, 22)
                if rng.random() < 0.08:
                    c = clampv([v * 0.72 for v in c])
                d.rectangle((x + off + 1, y + 1, x + off + bw - 2, y + course - 2), fill=c)
                h.rectangle((x + off + 1, y + 1, x + off + bw - 2, y + course - 2), fill=150)
        for s in range(STOREYS):
            # a stone string course at each floor line
            y = s * SH
            d.rectangle((0, y, TILE, y + 6), fill=(168, 156, 136))
            h.rectangle((0, y, TILE, y + 6), fill=200)
            for b in range(BAYS):
                x = b * BW
                box = (x + 70, y + 58, x + BW - 70, y + SH - 50)
                punched_window(d, h, box, rng, rng.random() < lit_rate, (44, 40, 38),
                               (40, 30, 26), (176, 166, 148), (170, 158, 140), mull=3)
    elif kind == "Limestone":
        d.rectangle((0, 0, TILE, TILE), fill=(158, 150, 132))
        h.rectangle((0, 0, TILE, TILE), fill=140)
        # ashlar courses
        cy = 0
        while cy < TILE:
            ch = 28
            bx = rng.randint(-60, 0)
            while bx < TILE:
                bwid = rng.randint(70, 130)
                c = jitter((178, 170, 150), rng, 9)
                d.rectangle((bx + 1, cy + 1, bx + bwid - 1, cy + ch - 1), fill=c)
                bx += bwid
            d.line((0, cy, TILE, cy), fill=(128, 120, 104), width=2)
            h.line((0, cy, TILE, cy), fill=90, width=2)
            cy += ch
        for s in range(STOREYS):
            y = s * SH
            d.rectangle((0, y, TILE, y + 10), fill=(186, 178, 160))
            d.rectangle((0, y + 10, TILE, y + 13), fill=(110, 102, 90))
            h.rectangle((0, y, TILE, y + 10), fill=220)
            for b in range(BAYS):
                x = b * BW
                box = (x + 62, y + 40, x + BW - 62, y + SH - 44)
                punched_window(d, h, box, rng, rng.random() < lit_rate, (58, 60, 64),
                               (80, 74, 64), (186, 178, 160), None, mull=3, frame_w=6)
                # a thin surround band
                d.rectangle((box[0] - 10, box[1] - 10, box[2] + 10, box[1] - 4), fill=(188, 180, 162))
    elif kind == "Metal":
        d.rectangle((0, 0, TILE, TILE), fill=(58, 62, 68))
        h.rectangle((0, 0, TILE, TILE), fill=140)
        for s in range(STOREYS):
            y = s * SH
            # spandrel panels with reveals
            for px in range(0, TILE, 64):
                c = jitter((66, 70, 78), rng, 5)
                d.rectangle((px + 2, y + 2, px + 62, y + 80), fill=c)
                d.line((px, y, px, y + 82), fill=(34, 36, 40), width=2)
                h.line((px, y, px, y + 82), fill=60, width=2)
            d.rectangle((0, y + 82, TILE, y + 88), fill=(96, 100, 108))
            # ribbon windows: continuous glazing with slim mullions
            gy0, gy1 = y + 94, y + SH - 18
            h.rectangle((0, gy0, TILE, gy1), fill=30)
            for b in range(BAYS * 2):
                x0 = b * (BW // 2)
                glass_fill(d, (x0 + 4, gy0, x0 + BW // 2 - 4, gy1), rng, rng.random() < lit_rate,
                           (30, 32, 36))
                d.rectangle((x0, gy0, x0 + 4, gy1), fill=(30, 32, 36))
                h.rectangle((x0, gy0, x0 + 4, gy1), fill=110)
            d.rectangle((0, gy1, TILE, gy1 + 5), fill=(110, 114, 120))
    elif kind == "Glass":
        d.rectangle((0, 0, TILE, TILE), fill=(40, 46, 54))
        h.rectangle((0, 0, TILE, TILE), fill=60)
        # big sky reflection over the whole tile so neighbouring panes read as one curtain
        sky = noise(TILE, TILE, 180, seed + 5, 2)
        arr = np.asarray(img, np.float32)
        for s in range(STOREYS):
            y = s * SH
            for col in range(8):
                x0 = col * (TILE // 8)
                lit = rng.random() < lit_rate
                box = (x0 + 5, y + 44, x0 + TILE // 8 - 5, y + SH - 6)
                if lit:
                    glass_fill(d, box, rng, True, (30, 34, 40))
                else:
                    glass_fill(d, box, rng, False, (30, 34, 40))
            d.rectangle((0, y, TILE, y + 44), fill=(52, 60, 70))       # spandrel glass
            d.rectangle((0, y + 40, TILE, y + 44), fill=(120, 126, 134))
            h.rectangle((0, y + 40, TILE, y + 46), fill=200)
        for col in range(9):
            x = col * (TILE // 8)
            d.rectangle((x - 3, 0, x + 3, TILE), fill=(116, 122, 130))
            h.rectangle((x - 3, 0, x + 3, TILE), fill=220)
        arr = np.asarray(img, np.float32)
        refl = (sky[..., None] * np.array([60, 72, 84], np.float32))
        arr = np.clip(arr * 0.8 + refl * 0.55, 0, 255)
        img = Image.fromarray(arr.astype(np.uint8))
        d = ImageDraw.Draw(img)
    elif kind == "Plaster":
        # Japanese mid-rise apartment block: render walls, balcony slabs with metal rails.
        d.rectangle((0, 0, TILE, TILE), fill=(184, 176, 160))
        h.rectangle((0, 0, TILE, TILE), fill=140)
        for s in range(STOREYS):
            y = s * SH
            for b in range(BAYS):
                x = b * BW
                box = (x + 36, y + 36, x + BW - 36, y + SH - 70)
                punched_window(d, h, box, rng, rng.random() < lit_rate, (150, 150, 150),
                               (64, 60, 56), None, None, mull=3, frame_w=4)
            # balcony slab + railing (the silhouette every Japanese street has)
            d.rectangle((0, y + SH - 64, TILE, y + SH - 54), fill=(170, 164, 150))
            d.rectangle((0, y + SH - 54, TILE, y + SH - 50), fill=(90, 86, 80))
            d.rectangle((0, y + SH - 50, TILE, y + SH - 8), fill=(116, 118, 116))
            for rx in range(0, TILE, 10):
                d.line((rx, y + SH - 50, rx, y + SH - 8), fill=(84, 86, 88), width=2)
            d.rectangle((0, y + SH - 52, TILE, y + SH - 48), fill=(70, 72, 76))
            h.rectangle((0, y + SH - 64, TILE, y + SH - 8), fill=210)
            for b in range(BAYS):   # partition walls between flats
                x = b * BW
                d.rectangle((x - 5, y + SH - 64, x + 5, y + SH - 6), fill=(176, 170, 156))
    elif kind == "Timber":
        # Old Town machiya: dark cedar boards, plaster panels, koshi lattice windows.
        d.rectangle((0, 0, TILE, TILE), fill=(80, 60, 46))
        h.rectangle((0, 0, TILE, TILE), fill=130)
        for bx in range(0, TILE, 16):
            c = jitter((84, 62, 46), rng, 10)
            d.rectangle((bx + 1, 0, bx + 15, TILE), fill=c)
            d.line((bx, 0, bx, TILE), fill=(52, 38, 30), width=1)
            h.line((bx, 0, bx, TILE), fill=90, width=1)
        for s in range(STOREYS):
            y = s * SH
            d.rectangle((0, y, TILE, y + 12), fill=(58, 42, 32))    # floor beam
            h.rectangle((0, y, TILE, y + 12), fill=210)
            for b in range(BAYS):
                x = b * BW
                if rng.random() < 0.5:
                    # plaster panel
                    d.rectangle((x + 20, y + 30, x + BW - 20, y + SH - 30), fill=(176, 166, 146))
                box = (x + 60, y + 60, x + BW - 60, y + SH - 60)
                glass_fill(d, box, rng, rng.random() < lit_rate + 0.1, (60, 44, 34))
                for lx in range(box[0], box[2], 9):     # koshi lattice
                    d.rectangle((lx, box[1], lx + 3, box[3]), fill=(66, 48, 36))
                h.rectangle(box, fill=160)
                d.rectangle((box[0] - 6, box[1] - 6, box[2] + 6, box[1]), fill=(56, 40, 30))
                d.rectangle((box[0] - 6, box[3], box[2] + 6, box[3] + 6), fill=(56, 40, 30))

    img = apply_grain(img, 0.12, seed + 1, 40)
    # weathering: slight darkening streaks downward from every floor line
    arr = np.asarray(img, np.float32)
    streak = noise(TILE, TILE, 12, seed + 2, 1)
    streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8)).resize(
        (TILE, TILE // 8)).resize((TILE, TILE), Image.BICUBIC), np.float32) / 255.0
    arr *= (0.94 + 0.08 * streak[..., None])
    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))

    hmap = np.asarray(hm.filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255.0
    nrm = normal_from_height(hmap, 6.0).resize((512, 512), Image.LANCZOS)
    save(img, f"Facade_{kind}_Albedo")
    save(nrm, f"Facade_{kind}_Normal")


# =============================================================== plain / kerb

def plain():
    n = noise(256, 256, 32, 77, 3)
    a = (200 + (n - 0.5) * 30).astype(np.uint8)
    save(Image.fromarray(np.stack([a, a, a], -1)), "Plain")


def kerb():
    # Kerb FACE. Unity UVs: u = 0 at the road edge -> 1 at the kerb top, v = metres/2 along the
    # street, so the arris is the RIGHT column and the 1.0 m joints are HORIZONTAL lines.
    rng = np.random.default_rng(9)
    base = np.full((256, 256, 3), (150, 150, 146), np.float32)
    speck = rng.random((256, 256)).astype(np.float32)
    base -= (speck > 0.86)[..., None] * 50
    base += (speck < 0.08)[..., None] * 25
    n = noise(256, 256, 40, 10)[..., None]
    base *= 0.92 + 0.14 * n
    base[:, :24] *= 0.62                                 # road-grime foot of the kerb
    img = Image.fromarray(np.clip(base, 0, 190).astype(np.uint8))
    d = ImageDraw.Draw(img)
    d.rectangle((238, 0, 256, 256), fill=(166, 166, 160))  # sunlit arris at the top edge
    for y in range(0, 256, 128):                           # joint every 1.0 m
        d.line((0, y, 256, y), fill=(70, 70, 68), width=3)
    save(img, "Kerb")


# =============================================================== pavement

def pavement():
    W, H = 1024, 512                  # 5.0 m x 2.5 m => 204.8 px/m both axes
    ppm = W / 5.0
    rng = random.Random(31)
    img = Image.new("RGB", (W, H), (96, 94, 88))
    d = ImageDraw.Draw(img)

    def m(x):
        return int(round(x * ppm))

    # 0.00-0.30 m granite kerb top
    d.rectangle((0, 0, m(0.30), H), fill=(150, 150, 144))
    for y in range(0, H, m(1.0)):
        d.line((0, y, m(0.30), y), fill=(90, 90, 86), width=3)
    d.line((m(0.30), 0, m(0.30), H), fill=(64, 62, 58), width=3)
    # 0.30-1.40 m furniture strip: small dark basalt setts, bond by row
    s = m(0.12)
    x0, x1 = m(0.31), m(1.40)
    for row, y in enumerate(range(0, H, s)):
        off = (s // 2) if row % 2 else 0
        for x in range(x0 - off, x1, s):
            c = jitter((84, 84, 86), rng, 12)
            d.rectangle((max(x0, x + 2), y + 2, min(x1, x + s - 2), y + s - 2), fill=c)
    d.rectangle((m(1.40), 0, m(1.46), H), fill=(128, 126, 120))    # steel edging line
    # 1.46-5.00 m stretcher-bond pavers 0.60 x 0.30 m, long side along the street
    pw, pl = m(0.30), m(0.60)
    tones = [(150, 144, 132), (140, 136, 126), (158, 150, 136), (132, 128, 120), (146, 138, 124)]
    for col, x in enumerate(range(m(1.46), W, pw)):
        off = (pl // 2) if col % 2 else 0
        for y in range(-pl, H + pl, pl):
            c = jitter(rng.choice(tones), rng, 5)
            d.rectangle((x + 2, y + off + 2, x + pw - 2, y + off + pl - 2), fill=c)
    # yellow tactile guide strip (Japanese tenji block line) 2.05-2.35 m, dotted
    tx0, tx1 = m(2.05), m(2.35)
    d.rectangle((tx0, 0, tx1, H), fill=(176, 150, 52))
    for y in range(6, H, 14):
        for x in range(tx0 + 7, tx1 - 4, 14):
            d.ellipse((x - 4, y - 4, x + 4, y + 4), fill=(190, 164, 60))
    for y in range(0, H, m(0.30)):
        d.line((tx0, y, tx1, y), fill=(120, 100, 40), width=2)
    img = apply_grain(img, 0.16, 32, 20)
    # a little grime near the building line
    arr = np.asarray(img, np.float32)
    ramp = np.clip((np.arange(W) - m(4.3)) / m(0.7), 0, 1)[None, :, None]
    arr *= 1 - 0.12 * ramp
    save(Image.fromarray(np.clip(arr, 0, 190).astype(np.uint8)), "Pavement")


# =============================================================== shops

SHOPS = [
    ("BAKERY", "パン"), ("BOOKS", "本屋"), ("FLORIST", "花"), ("COFFEE", "珈琲"),
    ("RAMEN", "らーめん"), ("OPTICIAN", "眼鏡"), ("DELI", "惣菜"), ("TEA HOUSE", "お茶"),
    ("GALLERY", "画廊"), ("SUSHI", "寿司"), ("CYCLES", "自転車"), ("PHARMACY", "薬"),
    ("NOODLES", "うどん"), ("STUDIO", "写真"), ("RECORDS", "音楽"), ("BISTRO", "洋食"),
]
FASCIA = [
    ((34, 52, 44), (220, 196, 132)), ((70, 30, 34), (236, 222, 196)), ((28, 40, 70), (230, 230, 226)),
    ((40, 38, 36), (214, 176, 96)), ((150, 50, 40), (246, 236, 214)), ((22, 70, 74), (236, 232, 220)),
    ((176, 160, 128), (44, 40, 36)), ((60, 44, 34), (232, 214, 170)),
]
AWNINGS = [
    ((150, 40, 44), (206, 196, 176)), ((30, 70, 60), (200, 192, 170)), ((36, 52, 96), (204, 200, 190)),
    ((170, 112, 40), (60, 44, 34)), ((50, 50, 52), (170, 164, 150)), ((110, 30, 60), (200, 186, 170)),
    ((20, 90, 110), (210, 206, 196)), ((120, 90, 60), (206, 190, 160)),
]


def shop_atlas():
    A = Image.new("RGB", (2048, 2048), (60, 60, 60))
    d = ImageDraw.Draw(A)
    en = font("bahnschrift.ttf", 76)
    jp = font("YuGothB.ttc", 70)
    for k, (word, kana) in enumerate(SHOPS):
        cx, cy = (k % 2) * 1024, (k // 2) * 128
        bg, fg = FASCIA[k % len(FASCIA)]
        d.rectangle((cx, cy, cx + 1023, cy + 127), fill=bg)
        d.rectangle((cx + 6, cy + 6, cx + 1017, cy + 121), outline=clampv([v * 0.6 for v in bg]), width=4)
        d.rectangle((cx, cy + 118, cx + 1023, cy + 127), fill=clampv([v * 0.55 for v in bg]))
        # English word centred, kana block on the left like a real Japanese fascia
        tw = d.textlength(word, font=en)
        d.text((cx + 600 - tw / 2, cy + 20), word, font=en, fill=fg)
        d.rectangle((cx + 40, cy + 18, cx + 250, cy + 110), fill=clampv([v * 1.25 + 10 for v in bg]))
        kw = d.textlength(kana, font=jp)
        scale_font = jp if kw < 200 else font("YuGothB.ttc", int(70 * 190 / kw))
        kw = d.textlength(kana, font=scale_font)
        d.text((cx + 145 - kw / 2, cy + 22), kana, font=scale_font, fill=fg)
    # awnings: bottom half, 2 cols x 4 rows of 1024x256
    for k, (a, b) in enumerate(AWNINGS):
        cx, cy = (k % 2) * 1024, 1024 + (k // 2) * 256
        stripe = 64 if k % 3 else 1024
        for x in range(0, 1024, stripe):
            col = a if (x // stripe) % 2 == 0 else b
            d.rectangle((cx + x, cy, cx + x + stripe - 1, cy + 255), fill=col)
        # scalloped valance band along the bottom 20%
        d.rectangle((cx, cy + 204, cx + 1023, cy + 255), fill=clampv([v * 0.85 for v in a]))
        d.rectangle((cx, cy + 200, cx + 1023, cy + 205), fill=(40, 38, 36))
    A = apply_grain(A, 0.08, 51, 30)
    arr = np.asarray(A, np.float32)
    save(Image.fromarray(np.clip(arr, 0, 190).astype(np.uint8)), "ShopAtlas")


def shop_glazing():
    """16 lit shop interiors, cell k = the trade on fascia sign SHOPS[k] (bakery bread, bookshop
    cases, cycle shop bikes on the wall ...). Drawn in maple_city_shop_interiors.py (2026-09-25);
    the old version was 8 generic interiors picked independently of the sign."""
    from maple_city_shop_interiors import shop_glazing_cells
    save(shop_glazing_cells(), "ShopGlazing")


def main():
    for i, kind in enumerate(["Brick", "Limestone", "Metal", "Glass", "Plaster", "Timber"]):
        facade(kind, 700 + i * 13)
    plain()
    kerb()
    pavement()
    shop_atlas()
    shop_glazing()


if __name__ == "__main__":
    main()

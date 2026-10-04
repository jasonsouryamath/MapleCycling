"""
NAGISA BAY overhaul NB9 - tropical flora + ground: procedural texture set.

Plain CPython (numpy + Pillow), not Blender. Writes Assets/Environment/NagisaBay/Textures/Flora2/
<Name>_Albedo.png / _Normal.png / _Rough.png. Everything is drawn from noise and polygons (no
photos, CC0 by construction).

Cutout leaf cards (alpha in the albedo; card UV v = 0 at the leaf base = the image BOTTOM):
  NB9_Monstera    split-leaf philodendron: heart leaf with pinnate slits and oval holes
  NB9_Banana      oblong leaf with a pale midrib and wind tears down both halves
  NB9_Fern        pinnate sword-fern frond
  NB9_Pandanus    one long serrated screw-pine blade (full width at the base, pointed tip)
  NB9_Frangipani  leaf rosettes with white/yellow and pink five-petal flowers
  NB9_DuneGrass   a fan of beach-grass blades with seed heads
  NB9_GroundGrass lush tussock of mixed greens
  NB9_GroundLeafy low creeping ground cover (beach morning glory / wedelia, yellow + violet dots)
Tiling surfaces:
  NB9_CoralRock   porous weathered limestone / coral rock (pits, holes, salt staining)
  NB9_BananaStem  fibrous banana pseudostem
  NB9_RoyalTrunk  smooth grey royal-palm trunk with leaf-scar rings
  NB9_TropicalLawn clumpy tropical lawn (for Nagisa_Ground's grass slot): mixed blade tones,
                  darker clumps, sun-bleached patches

Run:  python tools/blender/build_nagisa_flora2_textures.py [name ...]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_nagisa_textures import tile_noise, normal_from_height  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures", "Flora2")


def save_set(name, albedo, height, rough, nstrength=6.0, alpha=None):
    os.makedirs(OUT, exist_ok=True)
    a = (np.clip(albedo, 0, 1) * 255).astype(np.uint8)
    if alpha is not None:
        a = np.dstack([a, (np.clip(alpha, 0, 1) * 255).astype(np.uint8)])
    Image.fromarray(a).save(os.path.join(OUT, name + "_Albedo.png"))
    Image.fromarray(normal_from_height(height.astype(np.float32), nstrength), "RGB").save(
        os.path.join(OUT, name + "_Normal.png"))
    Image.fromarray((np.clip(rough, 0, 1) * 255).astype(np.uint8), "L").save(
        os.path.join(OUT, name + "_Rough.png"))
    print("[nb9-tex] %-18s %dx%d" % (name, albedo.shape[1], albedo.shape[0]))


def finish_card(img, n, name, seed, vein=None, rough=0.55, nstrength=3.0):
    """Downsample a 2x canvas, bleed colour under the cutout (no black mip halos), add a
    low-frequency tone wobble, save with alpha."""
    img = img.resize((n, n), Image.LANCZOS)
    a = np.asarray(img).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3]
    bled = np.asarray(img.convert("RGB").filter(ImageFilter.MaxFilter(9))).astype(np.float32) / 255.0
    rgb = np.where(alpha[..., None] > 0.05, rgb / np.maximum(alpha[..., None], 1e-3), bled)
    v = tile_noise(n, 8, seed, octaves=3)
    rgb = np.clip(rgb * (0.9 + 0.16 * v[..., None]), 0, 1)
    h = 0.5 + 0.25 * alpha
    if vein is not None:
        vv = np.asarray(vein.resize((n, n), Image.LANCZOS)).astype(np.float32) / 255.0
        h = h + 0.25 * vv
    save_set(name, rgb, h, np.clip(rough + 0.1 * (v - 0.5), 0, 1), nstrength, alpha=alpha)


def col(rng, base, spread=0.12):
    g = rng.uniform(1 - spread, 1 + spread)
    return (int(min(255, base[0] * g)), int(min(255, base[1] * g)), int(min(255, base[2] * g)), 255)


# ================================================================================ leaf cards

def monstera(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    vein = Image.new("L", (S, S), 0)
    d, dv = ImageDraw.Draw(img), ImageDraw.Draw(vein)
    rng = np.random.default_rng(901)
    cx, base_y, top_y = S * 0.5, S * 0.97, S * 0.04
    H = base_y - top_y
    # heart-shaped outline: width profile along the midrib, notch at the base
    pts_l, pts_r = [], []
    for k in range(61):
        t = k / 60
        y = base_y - t * H
        w = S * 0.47 * math.sin(math.pi * min(1.0, 0.12 + t * 0.9)) ** 0.8 * (1 - 0.25 * t)
        if t < 0.1:
            w *= 0.6 + 4 * t
        pts_l.append((cx - w, y)); pts_r.append((cx + w, y))
    d.polygon([(cx, base_y - H * 0.06)] + pts_l + [(cx, top_y)] + pts_r[::-1], fill=(34, 96, 40, 255))
    # glossy tone: lighter band along the midrib
    for k in range(40):
        t = k / 40
        y = base_y - t * H
        d.ellipse([cx - S * 0.12, y - 20, cx + S * 0.12, y + 20], fill=(44, 114, 48, 255))
    # slits: pinnate cuts from the margin toward the midrib, between the veins
    nsl = 9
    for side in (-1, 1):
        for k in range(nsl):
            t = 0.16 + 0.78 * (k + 0.5) / nsl
            y = base_y - t * H
            w = S * 0.47 * math.sin(math.pi * min(1.0, 0.12 + t * 0.9)) ** 0.8 * (1 - 0.25 * t)
            depth = rng.uniform(0.45, 0.8)
            x_in = cx + side * w * (1 - depth)
            slope = -H * 0.035
            d.polygon([(cx + side * (w + 20), y - 14 + slope), (cx + side * (w + 20), y + 14 + slope),
                       (x_in, y + 5), (x_in, y - 5)], fill=(0, 0, 0, 0))
            # a row of oval holes nearer the midrib
            if rng.random() < 0.8:
                hx = cx + side * w * rng.uniform(0.22, 0.35)
                hy = y + rng.uniform(-10, 10) + H * 0.03
                r1, r2 = rng.uniform(14, 26), rng.uniform(26, 44)
                d.ellipse([hx - r1, hy - r2 * 0.6, hx + r1, hy + r2 * 0.6], fill=(0, 0, 0, 0))
    # veins (drawn into the vein height map + a lighter line in albedo)
    d.line([(cx, base_y), (cx, top_y + 40)], fill=(96, 150, 70, 255), width=14)
    dv.line([(cx, base_y), (cx, top_y + 40)], fill=255, width=16)
    for side in (-1, 1):
        for k in range(nsl + 1):
            t = 0.12 + 0.82 * k / nsl
            y = base_y - t * H
            w = S * 0.45 * math.sin(math.pi * min(1.0, 0.12 + t * 0.9)) ** 0.8 * (1 - 0.25 * t)
            d.line([(cx, y), (cx + side * w * 0.95, y - H * 0.05)], fill=(70, 128, 58, 255), width=5)
            dv.line([(cx, y), (cx + side * w * 0.95, y - H * 0.05)], fill=180, width=6)
    finish_card(img, n, "NB9_Monstera", 911, vein, rough=0.38)


def banana(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    vein = Image.new("L", (S, S), 0)
    d, dv = ImageDraw.Draw(img), ImageDraw.Draw(vein)
    rng = np.random.default_rng(921)
    cx, y0, y1 = S * 0.5, S * 0.99, S * 0.02
    H = y0 - y1
    W = S * 0.44
    # each half is a stack of thin strips, so the tears are gaps between strips
    strips = 70
    for side in (-1, 1):
        k = 0
        while k < strips:
            t0 = k / strips
            run = int(rng.integers(3, 11))
            t1 = min(1.0, (k + run) / strips)
            ya, yb = y0 - t0 * H, y0 - t1 * H
            wa = W * math.sin(math.pi * min(1.0, 0.08 + t0 * 0.95)) ** 0.5
            wb = W * math.sin(math.pi * min(1.0, 0.08 + t1 * 0.95)) ** 0.5
            lean = H * 0.05     # strips angle up toward the tip, like the lateral veins
            c = col(rng, (70, 140, 48), 0.10)
            d.polygon([(cx, ya), (cx + side * wa, ya - lean), (cx + side * wb, yb - lean), (cx, yb)], fill=c)
            # a brown dried margin on some strips
            if rng.random() < 0.35:
                e = rng.uniform(0.86, 0.95)
                d.polygon([(cx + side * wa * e, ya - lean * e), (cx + side * wa, ya - lean),
                           (cx + side * wb, yb - lean), (cx + side * wb * e, yb - lean * e)],
                          fill=col(rng, (132, 118, 60), 0.12))
            k += run
            # the tear: a 2-4 px gap reaching most of the way to the midrib
            if rng.random() < 0.7 and k < strips:
                tt = k / strips
                y = y0 - tt * H
                ww = W * math.sin(math.pi * min(1.0, 0.08 + tt * 0.95)) ** 0.5
                reach = rng.uniform(0.2, 0.9)
                d.line([(cx + side * ww * (1 - reach), y - lean * (1 - reach)), (cx + side * ww, y - lean)],
                       fill=(0, 0, 0, 0), width=int(rng.integers(3, 7)))
    d.line([(cx, y0), (cx, y1 + 30)], fill=(178, 196, 112, 255), width=22)
    dv.line([(cx, y0), (cx, y1 + 30)], fill=255, width=26)
    for k in range(90):
        t = k / 90
        y = y0 - t * H
        for side in (-1, 1):
            dv.line([(cx, y), (cx + side * W, y - H * 0.05)], fill=60, width=2)
    finish_card(img, n, "NB9_Banana", 931, vein, rough=0.45)


def fern(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(941)
    cx, y0, y1 = S * 0.5, S * 0.99, S * 0.03
    H = y0 - y1
    d.line([(cx, y0), (cx, y1)], fill=(88, 110, 46, 255), width=10)
    pin = 42
    for side in (-1, 1):
        for k in range(pin):
            t = (k + 0.5) / pin
            y = y0 - H * (0.06 + 0.92 * t)
            L = S * 0.40 * math.sin(math.pi * min(1.0, 0.15 + t * 0.9)) * (1 - 0.45 * t)
            ang = math.radians(72 - 20 * t)
            ex, ey = cx + side * L * math.sin(ang), y - L * math.cos(ang)
            w = 16 * (1 - 0.5 * t) + 6
            c = col(rng, (52, 118, 40), 0.12)
            nx, ny = -(ey - y), (ex - cx)
            ln = math.hypot(nx, ny) + 1e-6
            nx, ny = nx / ln * w, ny / ln * w
            mx, my = (cx + ex) / 2, (y + ey) / 2
            d.polygon([(cx, y), (mx + nx, my + ny), (ex, ey), (mx - nx, my - ny)], fill=c)
            # serrated lobes along each pinna
            for j in range(5):
                u = (j + 1) / 6
                px, py = cx + (ex - cx) * u, y + (ey - y) * u
                r = w * (1.0 - 0.5 * u)
                d.ellipse([px - r * 0.6, py - r * 0.6, px + r * 0.6, py + r * 0.6], fill=c)
    finish_card(img, n, "NB9_Fern", 951, rough=0.6)


def pandanus(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    vein = Image.new("L", (S, S), 0)
    d, dv = ImageDraw.Draw(img), ImageDraw.Draw(vein)
    rng = np.random.default_rng(961)
    y0, y1 = S * 0.995, S * 0.005
    H = y0 - y1
    left, right = [], []
    for k in range(121):
        t = k / 120
        y = y0 - t * H
        w = S * 0.46 * (1 - t) ** 0.8 + 2
        tooth = 10 * (1 - t) if k % 2 == 0 else 0
        left.append((S / 2 - w - tooth, y)); right.append((S / 2 + w + tooth, y))
    d.polygon(left + right[::-1], fill=(62, 120, 58, 255))
    # keel + parallel veins; a yellow variegated edge band on some blades is done by tint in Unity
    d.line([(S / 2, y0), (S / 2, y1)], fill=(96, 150, 80, 255), width=18)
    dv.line([(S / 2, y0), (S / 2, y1)], fill=255, width=22)
    for f in (-0.55, -0.3, 0.3, 0.55):
        pts = [(S / 2 + f * (S * 0.46 * (1 - k / 60) ** 0.8), y0 - k / 60 * H) for k in range(61)]
        d.line(pts, fill=(74, 132, 64, 255), width=5)
        dv.line(pts, fill=120, width=6)
    # dried tip
    for k in range(60):
        t = 0.88 + 0.12 * k / 60
        y = y0 - t * H
        w = S * 0.46 * (1 - t) ** 0.8 + 3
        d.line([(S / 2 - w, y), (S / 2 + w, y)], fill=col(rng, (140, 120, 70), 0.05), width=3)
    finish_card(img, n, "NB9_Pandanus", 971, vein, rough=0.42)


def frangipani(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(981)
    rosettes = 22
    for _ in range(rosettes):
        x, y = rng.uniform(0.18, 0.82) * S, rng.uniform(0.2, 0.85) * S
        nl = int(rng.integers(6, 10))
        for k in range(nl):
            a = k / nl * math.tau + rng.uniform(-0.2, 0.2)
            L = rng.uniform(150, 230)
            w = L * 0.22
            ex, ey = x + L * math.cos(a), y + L * math.sin(a)
            nx, ny = -math.sin(a) * w, math.cos(a) * w
            mx, my = (x + ex) / 2, (y + ey) / 2
            c = col(rng, (48, 112, 44), 0.14)
            d.polygon([(x, y), (mx + nx, my + ny), (ex, ey), (mx - nx, my - ny)], fill=c)
            d.line([(x, y), (ex, ey)], fill=(92, 150, 76, 255), width=4)
    for _ in range(46):
        x, y = rng.uniform(0.15, 0.85) * S, rng.uniform(0.12, 0.8) * S
        pink = rng.random() < 0.3
        petal = (246, 150, 180, 255) if pink else (250, 248, 238, 255)
        centre = (250, 196, 80, 255) if not pink else (252, 214, 120, 255)
        r = rng.uniform(26, 38)
        rot = rng.uniform(0, math.tau)
        for k in range(5):
            a = rot + k * math.tau / 5
            px, py = x + r * 0.62 * math.cos(a), y + r * 0.62 * math.sin(a)
            d.ellipse([px - r * 0.62, py - r * 0.38, px + r * 0.62, py + r * 0.38], fill=petal)
        d.ellipse([x - r * 0.35, y - r * 0.35, x + r * 0.35, y + r * 0.35], fill=centre)
    finish_card(img, n, "NB9_Frangipani", 991, rough=0.5)


def dune_grass(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(1001)
    for k in range(120):
        bx = S * 0.5 + rng.normal(0, S * 0.07)
        a = rng.normal(0, 0.42)
        L = S * rng.uniform(0.55, 0.95)
        bend = rng.normal(0, 0.25)
        dry = rng.random()
        c = col(rng, (156, 150, 92) if dry < 0.45 else (102, 138, 70), 0.14)
        pts, w0 = [], rng.uniform(8, 14)
        for j in range(13):
            t = j / 12
            ang = a + bend * t * t
            pts.append((bx + math.sin(ang) * L * t, S - 4 - math.cos(ang) * L * t))
        for j in range(12):
            w = max(1, int(w0 * (1 - j / 12)))
            d.line([pts[j], pts[j + 1]], fill=c, width=w)
        if rng.random() < 0.18:      # seed head
            hx, hy = pts[-1]
            for s in range(10):
                d.ellipse([hx - 6, hy + s * 9 - 5, hx + 6, hy + s * 9 + 7], fill=col(rng, (190, 170, 110), 0.08))
    finish_card(img, n, "NB9_DuneGrass", 1011, rough=0.7)


def ground_grass(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(1021)
    for k in range(260):
        bx = S * 0.5 + rng.normal(0, S * 0.16)
        a = rng.normal(0, 0.55)
        L = S * rng.uniform(0.3, 0.8)
        bend = rng.normal(0, 0.5)
        tone = rng.random()
        base = (46, 104, 36) if tone < 0.5 else ((74, 132, 44) if tone < 0.85 else (122, 150, 62))
        c = col(rng, base, 0.12)
        pts, w0 = [], rng.uniform(10, 18)
        for j in range(11):
            t = j / 10
            ang = a + bend * t * t
            pts.append((bx + math.sin(ang) * L * t, S - 4 - math.cos(ang) * L * t))
        for j in range(10):
            d.line([pts[j], pts[j + 1]], fill=c, width=max(1, int(w0 * (1 - j / 10))))
    finish_card(img, n, "NB9_GroundGrass", 1031, rough=0.62)


def ground_leafy(n=1024):
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(1041)
    for _ in range(1400):
        x, y = rng.uniform(0.03, 0.97) * S, rng.uniform(0.03, 0.97) * S
        if (x - S / 2) ** 2 + (y - S / 2) ** 2 > (0.48 * S) ** 2:
            continue
        r = rng.uniform(22, 44)
        a = rng.uniform(0, math.tau)
        c = col(rng, (44, 110, 42), 0.18)
        # rounded heart leaf (beach morning glory)
        d.ellipse([x - r, y - r * 0.8, x + r, y + r * 0.8], fill=c)
        d.line([(x, y), (x + r * 0.9 * math.cos(a), y + r * 0.9 * math.sin(a))], fill=(92, 140, 70, 255), width=3)
    for _ in range(90):
        x, y = rng.uniform(0.1, 0.9) * S, rng.uniform(0.1, 0.9) * S
        if (x - S / 2) ** 2 + (y - S / 2) ** 2 > (0.44 * S) ** 2:
            continue
        violet = rng.random() < 0.4
        r = rng.uniform(12, 20)
        c = (196, 110, 200, 255) if violet else (250, 206, 50, 255)
        for k in range(5 if not violet else 1):
            a = k * math.tau / 5
            px, py = x + (r * 0.6 * math.cos(a) if not violet else 0), y + (r * 0.6 * math.sin(a) if not violet else 0)
            rr = r * (0.5 if not violet else 1.0)
            d.ellipse([px - rr, py - rr, px + rr, py + rr], fill=c)
        d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=(250, 240, 200, 255) if violet else (170, 110, 30, 255))
    finish_card(img, n, "NB9_GroundLeafy", 1051, rough=0.5)


# ================================================================================ tiling

def coral_rock(n=1024):
    base = tile_noise(n, 6, 1101, octaves=6)
    pits = tile_noise(n, 48, 1102, octaves=3)
    holes = (pits > 0.68).astype(np.float32) * (pits - 0.68) / 0.32
    ridge = 1 - np.abs(tile_noise(n, 12, 1103, octaves=4) * 2 - 1)
    h = 0.55 * base + 0.25 * ridge - 0.6 * holes
    tone = 0.78 + 0.22 * base
    rgb = np.stack([0.80 * tone, 0.76 * tone, 0.68 * tone], -1)
    rgb *= (1 - 0.55 * holes)[..., None]
    stain = np.clip(tile_noise(n, 10, 1104, octaves=4) * 1.8 - 0.9, 0, 1)
    rgb = rgb * (1 - 0.25 * stain[..., None]) + np.array([0.46, 0.44, 0.36]) * 0.25 * stain[..., None]
    algae = np.clip(tile_noise(n, 14, 1105, octaves=3) * 2.2 - 1.45, 0, 1)
    rgb = rgb * (1 - 0.5 * algae[..., None]) + np.array([0.30, 0.36, 0.22]) * 0.5 * algae[..., None]
    save_set("NB9_CoralRock", rgb, h, 0.78 - 0.2 * algae + 0.1 * holes, 9.0)


def banana_stem(n=512):
    x = np.linspace(0, 1, n, endpoint=False)
    fib = 0.5 + 0.5 * np.sin(x * math.tau * 60)[None, :]
    nz = tile_noise(n, 16, 1111, octaves=4)
    band = tile_noise(n, 4, 1112, octaves=2)
    h = 0.4 * fib + 0.6 * nz
    g = 0.8 + 0.2 * nz
    green = np.array([0.42, 0.52, 0.26]); brown = np.array([0.46, 0.36, 0.24])
    m = np.clip(band * 1.6 - 0.4, 0, 1)[..., None]
    rgb = (green * (1 - m) + brown * m) * g[..., None] * (0.9 + 0.1 * fib[..., None])
    save_set("NB9_BananaStem", rgb, h, np.full((n, n), 0.6, np.float32), 4.0)


def royal_trunk(n=512):
    y = np.linspace(0, 1, n, endpoint=False)[:, None]
    rings = np.clip(1 - np.abs(((y * 6) % 1.0) - 0.5) * 16, 0, 1) * np.ones((1, n))
    nz = tile_noise(n, 12, 1121, octaves=4)
    streak = tile_noise(n, 3, 1122, octaves=2)
    h = 0.7 * nz - 0.35 * rings
    v = 0.66 + 0.16 * nz - 0.12 * rings + 0.06 * streak
    rgb = np.stack([v * 0.98, v * 0.97, v * 0.93], -1)
    save_set("NB9_RoyalTrunk", rgb, h, 0.7 + 0.1 * nz, 5.0)


def tropical_lawn(n=1024):
    rng = np.random.default_rng(1131)
    img = Image.new("RGB", (n, n), (60, 104, 40))
    d = ImageDraw.Draw(img)
    for _ in range(26000):
        x, y = rng.uniform(0, n), rng.uniform(0, n)
        L = rng.uniform(5, 14)
        a = rng.normal(-math.pi / 2, 0.6)
        tone = rng.random()
        base = (52, 102, 36) if tone < 0.45 else ((78, 128, 44) if tone < 0.85 else (126, 146, 66))
        c = col(rng, base, 0.10)[:3]
        ex, ey = x + L * math.cos(a), y + L * math.sin(a)
        for ox in (0, -n, n):         # wrap so the tile is seamless
            for oy in (0, -n, n):
                if (0 <= x + ox < n or 0 <= ex + ox < n) and (0 <= y + oy < n or 0 <= ey + oy < n):
                    d.line([(x + ox, y + oy), (ex + ox, ey + oy)], fill=c, width=2)
    rgb = np.asarray(img).astype(np.float32) / 255.0
    clump = tile_noise(n, 10, 1132, octaves=4)
    bleach = np.clip(tile_noise(n, 5, 1133, octaves=3) * 2.0 - 1.15, 0, 1)
    rgb *= (0.78 + 0.34 * clump)[..., None]
    rgb = rgb * (1 - 0.4 * bleach[..., None]) + np.array([0.56, 0.54, 0.32]) * 0.4 * bleach[..., None]
    lum = rgb.mean(-1)
    save_set("NB9_TropicalLawn", rgb, 0.6 * lum + 0.4 * clump, 0.72 + 0.12 * (1 - lum), 5.0)


ALL = {
    "monstera": monstera, "banana": banana, "fern": fern, "pandanus": pandanus,
    "frangipani": frangipani, "dunegrass": dune_grass, "groundgrass": ground_grass,
    "groundleafy": ground_leafy, "coralrock": coral_rock, "bananastem": banana_stem,
    "royaltrunk": royal_trunk, "lawn": tropical_lawn,
}

if __name__ == "__main__":
    names = [a for a in sys.argv[1:] if a in ALL] or list(ALL)
    for k in names:
        ALL[k]()

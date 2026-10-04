"""Nagisa Bay verge grass cards: a 4x2 atlas of painted grass clumps with real blades (alpha cutout).

    python tools/textures/make_nagisa_grass_cards.py

Each 512x512 cell is one clump seen from the side: 34-60 individually drawn, curved, tapered blades
fanning out of a dark base, dark-to-light along their length with per-blade hue / brightness variation
and a few sun-dried blades. Drawn 3x supersampled and downsampled so the blade edges are smooth; the
RGB under transparent texels is dilated from the blade colours so mip levels do not halo.
Output: Assets/Environment/NagisaBay/Textures/Nagisa_GrassCards.png (RGBA, 2048x1024)
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures", "Nagisa_GrassCards.png")
CELL, SS = 512, 3
COLS, ROWS = 4, 2


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def clump(seed):
    rng = random.Random(seed)
    S = CELL * SS
    rgb = Image.new("RGB", (S, S), (0, 0, 0))
    alpha = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(rgb)
    da = ImageDraw.Draw(alpha)
    n = rng.randint(34, 60)
    blades = []
    for _ in range(n):
        spread = rng.uniform(-1, 1)
        x0 = S * (0.5 + 0.17 * spread * rng.uniform(0.3, 1.0))
        # taller toward the middle of the clump, shorter at the fringe
        h = S * rng.uniform(0.38, 0.97) * (1.0 - 0.38 * abs(spread))
        lean = spread * rng.uniform(0.06, 0.20) + rng.uniform(-0.04, 0.04)       # sideways travel / height
        bend = rng.uniform(-0.14, 0.14) + 0.18 * spread * rng.uniform(0, 1)       # extra curvature
        w0 = S * rng.uniform(0.009, 0.019)
        blades.append((x0, h, lean, bend, w0, rng.random()))
    blades.sort(key=lambda b: b[1])                                                 # short ones behind
    base = (0.07, 0.17, 0.05)
    for x0, h, lean, bend, w0, rr in blades:
        tint = rng.uniform(-0.05, 0.05)
        dry = rr < 0.13
        tip = (0.62, 0.58, 0.24) if dry else (0.46 + tint, 0.66 + tint * 0.5, 0.20 + tint)
        mid = (0.20 + tint * 0.5, 0.40 + tint, 0.10)
        steps = 28
        left, right, cols = [], [], []
        for k in range(steps + 1):
            t = k / steps
            x = x0 + lean * h * t + bend * h * t * t
            y = S - 2 - h * t
            width = w0 * (1.0 - t) ** 0.8 * (1.0 + 0.5 * math.sin(t * 3.14159)) + S * 0.0008
            # tangent for the normal offset
            t2 = min(1.0, t + 1.0 / steps)
            x2 = x0 + lean * h * t2 + bend * h * t2 * t2
            y2 = S - 2 - h * t2
            dx, dy = x2 - x, y2 - y
            ln = math.hypot(dx, dy) or 1.0
            nx, ny = -dy / ln, dx / ln
            left.append((x + nx * width, y + ny * width))
            right.append((x - nx * width, y - ny * width))
            cols.append(lerp(base, lerp(mid, tip, max(0.0, (t - 0.45) / 0.55)), min(1.0, t * 1.6)))
        for k in range(steps):
            poly = [left[k], left[k + 1], right[k + 1], right[k]]
            c = tuple(int(255 * max(0, min(1, v))) for v in cols[k + 1])
            d.polygon(poly, fill=c)
            da.polygon(poly, fill=255)
    rgb = rgb.resize((CELL, CELL), Image.LANCZOS)
    alpha = alpha.resize((CELL, CELL), Image.LANCZOS)
    return np.asarray(rgb).astype(np.float32), np.asarray(alpha).astype(np.float32)


def dilate(rgb, alpha, iters=10):
    """Bleed blade colour into transparent texels so mips / filtering never pull in black."""
    rgb = rgb.copy()
    known = alpha > 8
    for _ in range(iters):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(alpha.shape, np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                s = np.roll(np.roll(rgb, dy, 0), dx, 1)
                k = np.roll(np.roll(known, dy, 0), dx, 1)
                acc += s * k[..., None]
                cnt += k
        fill = (~known) & (cnt > 0)
        rgb[fill] = acc[fill] / cnt[fill][..., None]
        known = known | fill
    return rgb


def main():
    atlas = np.zeros((ROWS * CELL, COLS * CELL, 4), np.uint8)
    for r in range(ROWS):
        for c in range(COLS):
            rgb, a = clump(1000 + r * COLS + c)
            rgb = dilate(rgb, a)
            tile = np.dstack([rgb, a]).clip(0, 255).astype(np.uint8)
            # Unity UV origin is bottom-left: row 0 of the atlas (bottom) is the last image row block
            y0 = (ROWS - 1 - r) * CELL
            atlas[y0:y0 + CELL, c * CELL:(c + 1) * CELL] = tile
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    Image.fromarray(atlas).save(OUT, optimize=True)
    print("wrote", OUT, atlas.shape)


if __name__ == "__main__":
    main()

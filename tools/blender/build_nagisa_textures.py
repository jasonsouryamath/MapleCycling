"""
NAGISA BAY (B5) - procedural PBR texture set (albedo / normal / roughness).

Plain CPython (numpy + Pillow), NOT Blender: writes PNGs straight into
Assets/Environment/NagisaBay/Textures. Everything is authored from noise and simple
drawing - no photos, no third-party brands; every shop name is fictional.

Sizes follow the E-rules: 2K for hero surfaces (hotel stone, hotel glass), 1K for
tiling kit surfaces. Each set is <Name>_Albedo.png, <Name>_Normal.png (OpenGL +Y,
tangent space, Unity "Normal map" import), <Name>_Rough.png (linear roughness).
Cutout sets (palm fronds, flowering shrubs) carry alpha in the albedo.

Run:  python tools/blender/build_nagisa_textures.py
"""
import math
import sys
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures")
RNG = np.random.default_rng(5150)


# ----------------------------------------------------------------------------------- noise
def tile_noise(n, cells, seed, octaves=5, gain=0.5):
    """Tileable fbm in [0,1] (bilinear value noise on a periodic lattice)."""
    rng = np.random.default_rng(seed)
    out = np.zeros((n, n), np.float32)
    amp, tot = 1.0, 0.0
    c = cells
    for _ in range(octaves):
        lat = rng.random((c, c)).astype(np.float32)
        y = np.linspace(0, c, n, endpoint=False)
        x0 = np.floor(y).astype(int)
        f = y - x0
        f = f * f * (3 - 2 * f)
        x1 = (x0 + 1) % c
        a = lat[x0][:, x0]; b = lat[x0][:, x1]
        cc = lat[x1][:, x0]; d = lat[x1][:, x1]
        fx = f[None, :]; fy = f[:, None]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (cc * (1 - fx) + d * fx) * fy)
        tot += amp
        amp *= gain
        c *= 2
    return out / tot


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    nx, ny, nz = -dx, dy, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return ((n * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8)


def save_set(name, albedo, height, rough, nstrength=6.0, alpha=None):
    os.makedirs(OUT, exist_ok=True)
    a = (np.clip(albedo, 0, 1) * 255).astype(np.uint8)
    if alpha is not None:
        a = np.dstack([a, (np.clip(alpha, 0, 1) * 255).astype(np.uint8)])
        Image.fromarray(a).save(os.path.join(OUT, name + "_Albedo.png"))
    else:
        Image.fromarray(a).save(os.path.join(OUT, name + "_Albedo.png"))
    Image.fromarray(normal_from_height(height.astype(np.float32), nstrength), "RGB").save(
        os.path.join(OUT, name + "_Normal.png"))
    Image.fromarray((np.clip(rough, 0, 1) * 255).astype(np.uint8), "L").save(
        os.path.join(OUT, name + "_Rough.png"))
    print("[nagisa-tex] %-18s %dx%d" % (name, albedo.shape[1], albedo.shape[0]))


def tint(base, var, *layers):
    col = np.ones(var.shape + (3,), np.float32) * np.array(base, np.float32)[None, None, :] / 255.0
    return col * var[..., None]


# ----------------------------------------------------------------------------------- sets
def stone(n=2048):
    """Cream travertine / limestone cladding in 1.2 m x 0.6 m courses (tile = 2.4 m)."""
    v = tile_noise(n, 8, 11)
    vein = tile_noise(n, 3, 12, octaves=6)
    # horizontal travertine banding
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    band = 0.5 + 0.5 * np.sin((yy * 17 + vein * 2.2) * math.tau)
    pits = (tile_noise(n, 64, 13, octaves=2) > 0.78).astype(np.float32)
    # course joints: 4 rows x 2 slabs, running bond
    rows, cols = 4, 2
    gy = (yy * rows) % 1.0
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    rowi = np.floor(yy * rows)
    gx = ((xx * cols) + (rowi % 2) * 0.5) % 1.0
    jw = 0.006
    joint = ((gy < jw) | (gx < jw * 0.5)).astype(np.float32)
    # per-slab value shift
    slab = np.floor(yy * rows) * 7 + np.floor((xx * cols) + (rowi % 2) * 0.5)
    shift = (np.sin(slab * 12.9898) * 43758.5453) % 1.0
    lum = 0.90 + 0.05 * (v - 0.5) + 0.035 * (band - 0.5) + 0.03 * (shift - 0.5) - 0.10 * pits
    lum = lum * (1 - 0.35 * joint)
    alb = tint((236, 226, 206), lum)
    h = 0.5 + 0.18 * v - 0.5 * joint - 0.25 * pits + 0.05 * band
    rough = 0.52 + 0.12 * v + 0.25 * joint
    save_set("NB_Stone", alb, h, rough, nstrength=5.0)


def glass(n=2048):
    """Blue-green curtain-wall glass with room interiors: 4 bays x 4 floors per tile
    (tile = 7.2 m wide x 12.8 m tall -> 1.8 m bays, 3.2 m floors)."""
    img = np.zeros((n, n, 3), np.float32)
    emis = np.zeros((n, n), np.float32)
    bays, floors = 4, 4
    bw, fh = n // bays, n // floors
    rng = np.random.default_rng(21)
    base = np.array([0.20, 0.36, 0.40], np.float32)
    sky = np.linspace(1.0, 0.72, n)[:, None, None]
    img[:] = base[None, None, :] * sky
    for f in range(floors):
        for b in range(bays):
            y0, x0 = f * fh, b * bw
            room = rng.random()
            # interior depth: darker lower part, ceiling light strip, curtain on one side
            interior = np.array([0.30, 0.28, 0.24]) if room > 0.35 else np.array([0.16, 0.20, 0.22])
            yy = np.linspace(0, 1, fh)[:, None]
            grad = (0.55 + 0.45 * yy)
            region = img[y0:y0 + fh, x0:x0 + bw]
            region[:] = region * 0.62 + (interior[None, None, :] * grad[..., None]) * 0.38
            if rng.random() < 0.6:
                cw = int(bw * rng.uniform(0.18, 0.42))
                side = rng.random() < 0.5
                cs = slice(x0, x0 + cw) if side else slice(x0 + bw - cw, x0 + bw)
                img[y0:y0 + fh, cs] = img[y0:y0 + fh, cs] * 0.55 + np.array([0.86, 0.82, 0.74]) * 0.45
            if room > 0.55:
                emis[y0 + int(fh * 0.08):y0 + int(fh * 0.16), x0 + 12:x0 + bw - 12] = rng.uniform(0.55, 1.0)
                emis[y0:y0 + fh, x0:x0 + bw] += 0.10 * rng.random()
    # mullions + slab edge (bronze-grey)
    xx = np.arange(n)[None, :] % bw
    yy = np.arange(n)[:, None] % fh
    mull = (xx < 10) | (xx > bw - 10)
    slab = (yy < 26)
    frame = np.broadcast_to(mull, (n, n)) | np.broadcast_to(slab, (n, n))
    img[frame] = np.array([0.36, 0.30, 0.24])
    emis[frame] = 0
    v = tile_noise(n, 4, 22, octaves=3)
    img *= (0.92 + 0.16 * v)[..., None]
    rough = np.where(frame, 0.45, 0.06).astype(np.float32)
    h = np.where(frame, 0.8, 0.5).astype(np.float32)
    save_set("NB_Glass", img, h, rough, nstrength=3.0)
    Image.fromarray((np.clip(emis, 0, 1) * 255).astype(np.uint8), "L").save(
        os.path.join(OUT, "NB_Glass_Emission.png"))


def teak(n=1024):
    """Teak decking / screens: 0.14 m boards, tile 1.12 m."""
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    boards = 8
    bi = np.floor(xx * boards)
    gx = (xx * boards) % 1.0
    grain = tile_noise(n, 4, 31, octaves=6)
    streak = 0.5 + 0.5 * np.sin((yy * 60 + grain * 6 + bi * 1.7) * math.tau / 3)
    bshift = (np.sin(bi * 78.233) * 43758.5453) % 1.0
    gap = (gx < 0.04).astype(np.float32)
    lum = 0.82 + 0.12 * (streak - 0.5) + 0.12 * (bshift - 0.5)
    lum = lum * (1 - 0.6 * gap)
    alb = tint((168, 112, 68), np.broadcast_to(lum, (n, n)))
    h = 0.6 + 0.1 * streak - 0.6 * gap
    save_set("NB_Teak", alb, np.broadcast_to(h, (n, n)).copy(), 0.58 + 0.1 * grain + 0.3 * gap, 4.0)


def bronze(n=1024):
    v = tile_noise(n, 6, 41, octaves=4)
    brush = tile_noise(n, 2, 42, octaves=6)
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    lines = 0.5 + 0.5 * np.sin((yy * 400 + brush * 30) * math.tau)
    lum = 0.88 + 0.08 * (v - 0.5) + 0.04 * (lines - 0.5)
    alb = tint((150, 110, 72), np.broadcast_to(lum, (n, n)))
    save_set("NB_Bronze", alb, np.broadcast_to(0.5 + 0.03 * lines, (n, n)).copy(),
             np.broadcast_to(0.34 + 0.1 * v, (n, n)).copy(), 2.0)


def plaster(n=1024):
    """White smooth stucco (tinted pastel per building in Unity), tile 3 m."""
    v = tile_noise(n, 6, 51, octaves=6)
    fine = tile_noise(n, 48, 52, octaves=3)
    streak = tile_noise(n, 3, 53, octaves=3)
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    drip = np.clip((tile_noise(n, 12, 54, octaves=2) - 0.62) * 3, 0, 1) * (0.4 + 0.6 * yy)
    lum = 0.93 + 0.04 * (v - 0.5) + 0.03 * (fine - 0.5) - 0.05 * drip - 0.03 * (streak - 0.5)
    alb = tint((246, 243, 236), lum)
    save_set("NB_Plaster", alb, 0.5 + 0.12 * fine + 0.05 * v, 0.78 + 0.1 * fine, 3.5)


def roof_tile(n=1024):
    """Terracotta barrel tile; 8 courses per tile (tile 2.4 m)."""
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    cols, rows = 10, 8
    gx = (xx * cols) % 1.0
    gy = (yy * rows) % 1.0
    barrel = np.sin(gx * math.pi)
    lap = np.clip(1 - gy * 1.1, 0, 1) ** 3
    v = tile_noise(n, 8, 61, octaves=5)
    ti = np.floor(xx * cols) + 31 * np.floor(yy * rows)
    shift = (np.sin(ti * 12.9898) * 43758.5453) % 1.0
    lum = 0.62 + 0.30 * barrel - 0.28 * lap + 0.10 * (v - 0.5) + 0.12 * (shift - 0.5)
    alb = tint((196, 104, 70), lum)
    save_set("NB_RoofTile", alb, barrel * 0.8 - lap * 0.4, 0.66 + 0.15 * v, 7.0)


def deck_stone(n=1024):
    """Pale sandstone pool-deck pavers 0.6 m, tile 2.4 m."""
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    k = 4
    gx, gy = (xx * k) % 1.0, (yy * k) % 1.0
    joint = ((gx < 0.012) | (gy < 0.012)).astype(np.float32)
    ti = np.floor(xx * k) + 17 * np.floor(yy * k)
    shift = (np.sin(ti * 91.3458) * 47453.5453) % 1.0
    v = tile_noise(n, 10, 71, octaves=5)
    lum = 0.90 + 0.06 * (v - 0.5) + 0.05 * (shift - 0.5)
    lum = lum * (1 - 0.3 * joint)
    alb = tint((228, 214, 190), lum)
    save_set("NB_DeckStone", alb, 0.5 + 0.1 * v - 0.5 * joint, 0.7 + 0.1 * v + 0.2 * joint, 4.0)


def pool_tile(n=1024):
    """Turquoise glass mosaic lining the pools (seen through HDRP/Lit water)."""
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    k = 40
    gx, gy = (xx * k) % 1.0, (yy * k) % 1.0
    joint = ((gx < 0.08) | (gy < 0.08)).astype(np.float32)
    ti = np.floor(xx * k) + 97 * np.floor(yy * k)
    shift = (np.sin(ti * 12.9898) * 43758.5453) % 1.0
    col = np.stack([0.10 + 0.08 * shift, 0.62 + 0.14 * shift, 0.70 + 0.10 * shift], -1)
    col = col * (1 - 0.25 * joint[..., None]) + 0.25 * joint[..., None] * np.array([0.85, 0.88, 0.86])
    save_set("NB_PoolTile", col, 0.5 - 0.3 * joint, 0.15 + 0.4 * joint, 2.0)


def canvas(n=1024):
    """Umbrella / cabana canvas: cream with teal awning stripes in the lower half."""
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    weave = tile_noise(n, 128, 81, octaves=1)
    v = tile_noise(n, 6, 82, octaves=4)
    stripe = (((xx * 8) % 1.0) < 0.5) & (yy > 0.5)
    base = np.where(stripe[..., None], np.array([0.12, 0.52, 0.56]), np.array([0.95, 0.92, 0.84]))
    alb = base * (0.94 + 0.05 * weave[..., None] + 0.04 * (v[..., None] - 0.5))
    save_set("NB_Canvas", alb, 0.5 + 0.05 * weave, 0.86 + 0.08 * weave, 2.0)


def gelcoat(n=1024):
    """Yacht hull gelcoat: white with a navy boot stripe band at v 0.30-0.36, teak deck
    strip in the top quarter (the yacht UVs map hull height to v)."""
    yy = np.linspace(1, 0, n, endpoint=False)[:, None]   # image row 0 = UV v 1
    v = tile_noise(n, 8, 91, octaves=4)
    col = np.ones((n, n, 3), np.float32) * np.array([0.96, 0.96, 0.95])
    boot = ((yy > 0.30) & (yy < 0.36))[..., 0] if False else np.broadcast_to((yy > 0.30) & (yy < 0.36), (n, n))
    col[boot] = np.array([0.08, 0.16, 0.30])
    anti = np.broadcast_to(yy < 0.30, (n, n))
    col[anti] = np.array([0.46, 0.12, 0.10])
    col *= (0.96 + 0.05 * v)[..., None]
    rough = np.where(anti, 0.7, 0.18).astype(np.float32)
    save_set("NB_Gelcoat", col, 0.5 + 0.01 * v, rough, 1.0)


def pontoon_wood(n=1024):
    """Weathered grey composite / hardwood pontoon decking, 0.14 m boards across."""
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    boards = 8
    bi = np.floor(yy * boards)
    gy = (yy * boards) % 1.0
    grain = tile_noise(n, 4, 101, octaves=6)
    streak = 0.5 + 0.5 * np.sin((xx * 30 + grain * 5 + bi * 2.3) * math.tau / 2)
    gap = (gy < 0.05).astype(np.float32)
    shift = (np.sin(bi * 78.233) * 43758.5453) % 1.0
    lum = 0.78 + 0.10 * (streak - 0.5) + 0.10 * (shift - 0.5)
    lum = lum * (1 - 0.7 * gap)
    alb = tint((170, 158, 140), np.broadcast_to(lum, (n, n)))
    save_set("NB_PontoonWood", alb, np.broadcast_to(0.6 + 0.1 * streak - 0.6 * gap, (n, n)).copy(),
             np.broadcast_to(0.72 + 0.1 * grain, (n, n)).copy(), 4.5)


def palm_bark(n=1024):
    """Coconut trunk: grey-brown with horizontal leaf-scar rings (v repeats 1 per 0.6 m)."""
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    v = tile_noise(n, 8, 111, octaves=6)
    fib = tile_noise(n, 64, 112, octaves=2)
    rings = 6
    gy = (yy * rings + 0.08 * np.sin(xx * math.tau * 2)) % 1.0
    scar = np.exp(-((gy - 0.5) / 0.05) ** 2)
    lum = 0.70 + 0.14 * (v - 0.5) + 0.06 * (fib - 0.5) - 0.22 * scar
    alb = tint((142, 126, 104), lum)
    save_set("NB_PalmBark", alb, 0.5 + 0.2 * v - 0.4 * scar + 0.1 * fib, 0.85 + 0.1 * v, 6.0)


def frond_atlas(n=1024, fan=False):
    """Alpha-cutout frond card. Coconut: a pinnate frond (rachis along +v, leaflets
    angled toward the tip). Fan: a radial palmate leaf."""
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(121 if not fan else 131)
    if not fan:
        cx = S // 2
        d.line([(cx, S - 10), (cx, 20)], fill=(120, 132, 60, 255), width=18)
        count = 46
        for side in (-1, 1):
            for k in range(count):
                t = (k + 0.5) / count
                y = S - 30 - t * (S - 80)
                L = S * 0.46 * math.sin(math.pi * min(1.0, 0.18 + t * 0.95)) * (1.0 - 0.35 * t)
                ang = math.radians(58 - 18 * t)
                ex = cx + side * L * math.sin(ang)
                ey = y - L * math.cos(ang)
                w = 26 * (1.0 - 0.5 * t) + 10
                g = rng.uniform(0.85, 1.1)
                col = (int(62 * g), int(128 * g), int(44 * g), 255)
                nx, ny = -(ey - y), (ex - cx)
                ln = math.hypot(nx, ny) + 1e-6
                nx, ny = nx / ln * w, ny / ln * w
                midx, midy = (cx + ex) / 2, (y + ey) / 2
                d.polygon([(cx, y), (midx + nx, midy + ny), (ex, ey), (midx - nx * 0.4, midy - ny * 0.4)],
                          fill=col)
                d.line([(cx, y), (ex, ey)], fill=(int(90 * g), int(140 * g), int(58 * g), 255), width=3)
    else:
        cx, cy = S // 2, int(S * 0.94)
        d.line([(cx, S), (cx, cy)], fill=(110, 118, 60, 255), width=20)
        segs = 34
        R = S * 0.86
        for k in range(segs):
            a0 = math.radians(-80 + 160 * k / segs)
            a1 = math.radians(-80 + 160 * (k + 1) / segs)
            am = (a0 + a1) / 2
            r = R * rng.uniform(0.88, 1.0)
            g = rng.uniform(0.85, 1.1)
            p = [(cx, cy),
                 (cx + R * 0.25 * math.sin(a0), cy - R * 0.25 * math.cos(a0)),
                 (cx + r * math.sin(a0 + 0.012), cy - r * math.cos(a0 + 0.012)),
                 (cx + r * 1.02 * math.sin(am), cy - r * 1.02 * math.cos(am)),
                 (cx + r * math.sin(a1 - 0.012), cy - r * math.cos(a1 - 0.012)),
                 (cx + R * 0.25 * math.sin(a1), cy - R * 0.25 * math.cos(a1))]
            d.polygon(p, fill=(int(58 * g), int(120 * g), int(52 * g), 255))
            d.line([(cx, cy), (cx + r * math.sin(am), cy - r * math.cos(am))],
                   fill=(int(96 * g), int(150 * g), int(70 * g), 255), width=4)
    img = img.resize((n, n), Image.LANCZOS)
    a = np.asarray(img).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3]
    # bleed colour into transparent texels so mips never go black at the cutout edge
    bled = np.asarray(img.convert("RGB").filter(ImageFilter.MaxFilter(9))).astype(np.float32) / 255.0
    rgb = np.where(alpha[..., None] > 0.05, rgb / np.maximum(alpha[..., None], 1e-3), bled)
    v = tile_noise(n, 8, 141 if fan else 142, octaves=3)
    rgb = np.clip(rgb * (0.92 + 0.14 * v[..., None]), 0, 1)
    save_set("NB_FanFrond" if fan else "NB_PalmFrond", rgb, 0.5 + 0.2 * alpha, 0.6 + 0.1 * v, 3.0, alpha=alpha)


def flower_shrub(n=1024, magenta=False, name=None, blooms=None, leaves=900, green=(40, 104, 38)):
    """Alpha-cutout leaf+blossom clump card: hibiscus (red/pink on glossy green) or
    bougainvillea (magenta bracts)."""
    S = n * 2
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(151 if magenta else (161 if name is None else 171))
    for _ in range(leaves):
        x, y = rng.uniform(0.05, 0.95) * S, rng.uniform(0.08, 0.98) * S
        if (x - S / 2) ** 2 / (0.46 * S) ** 2 + (y - S * 0.58) ** 2 / (0.44 * S) ** 2 > 1:
            continue
        r = rng.uniform(18, 42)
        a = rng.uniform(0, math.tau)
        g = rng.uniform(0.75, 1.15)
        col = (int(green[0] * g), int(green[1] * g), int(green[2] * g), 255)
        p = [(x + r * math.cos(a), y + r * math.sin(a)),
             (x + r * 0.45 * math.cos(a + 1.6), y + r * 0.45 * math.sin(a + 1.6)),
             (x - r * math.cos(a), y - r * math.sin(a)),
             (x + r * 0.45 * math.cos(a - 1.6), y + r * 0.45 * math.sin(a - 1.6))]
        d.polygon(p, fill=col)
    if blooms is None:
        blooms = 150 if magenta else 60
    for _ in range(blooms):
        x, y = rng.uniform(0.1, 0.9) * S, rng.uniform(0.15, 0.9) * S
        if (x - S / 2) ** 2 / (0.42 * S) ** 2 + (y - S * 0.58) ** 2 / (0.40 * S) ** 2 > 1:
            continue
        if magenta:
            col = (int(rng.uniform(200, 235)), int(rng.uniform(30, 60)), int(rng.uniform(130, 170)), 255)
            r = rng.uniform(14, 26)
        else:
            pick = rng.random()
            col = (232, 48, 52, 255) if pick < 0.55 else ((246, 120, 150, 255) if pick < 0.85 else (250, 190, 70, 255))
            r = rng.uniform(26, 44)
        for k in range(5):
            a = k * math.tau / 5 + rng.uniform(0, 0.4)
            d.ellipse([x + r * 0.55 * math.cos(a) - r * 0.5, y + r * 0.55 * math.sin(a) - r * 0.5,
                       x + r * 0.55 * math.cos(a) + r * 0.5, y + r * 0.55 * math.sin(a) + r * 0.5], fill=col)
        if not magenta:
            d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(250, 220, 90, 255))
    img = img.resize((n, n), Image.LANCZOS)
    a = np.asarray(img).astype(np.float32) / 255.0
    rgb, alpha = a[..., :3], a[..., 3]
    bled = np.asarray(img.convert("RGB").filter(ImageFilter.MaxFilter(9))).astype(np.float32) / 255.0
    rgb = np.where(alpha[..., None] > 0.05, rgb / np.maximum(alpha[..., None], 1e-3), bled)
    save_set(name or ("NB_Bougainvillea" if magenta else "NB_Hibiscus"), np.clip(rgb, 0, 1), 0.5 + 0.2 * alpha,
             np.full((n, n), 0.55, np.float32), 3.0, alpha=alpha)


def town_windows(n=1024):
    """Pastel mid-rise facade window atlas: 4 x 4 cells of 1.5 m x 1.6 m windows with
    white frames, some louvred shutters, some open with curtains; walls transparent-free
    (the wall colour comes from the albedo tint at the frame border)."""
    img = np.ones((n, n, 3), np.float32) * np.array([0.94, 0.93, 0.90])
    rng = np.random.default_rng(171)
    c = n // 4
    for i in range(4):
        for j in range(4):
            x0, y0 = i * c, j * c
            m = int(c * 0.14)
            img[y0 + m:y0 + c - m, x0 + m:x0 + c - m] = np.array([0.98, 0.98, 0.97])  # frame
            f = m + 12
            kind = rng.random()
            if kind < 0.4:     # glass + curtain
                img[y0 + f:y0 + c - f, x0 + f:x0 + c - f] = np.array([0.22, 0.32, 0.36])
                cw = int((c - 2 * f) * 0.35)
                img[y0 + f:y0 + c - f, x0 + f:x0 + f + cw] = np.array([0.86, 0.80, 0.70])
            elif kind < 0.75:  # louvred shutter (teal)
                sh = np.array([0.20, 0.52, 0.52]) if rng.random() < 0.5 else np.array([0.28, 0.40, 0.60])
                for k in range(y0 + f, y0 + c - f):
                    img[k, x0 + f:x0 + c - f] = sh * (0.8 + 0.2 * (((k - y0) // 10) % 2))
            else:              # glass dark
                img[y0 + f:y0 + c - f, x0 + f:x0 + c - f] = np.array([0.16, 0.24, 0.28])
            img[y0 + c // 2 - 3:y0 + c // 2 + 3, x0 + f:x0 + c - f] = np.array([0.98, 0.98, 0.97])  # transom
            img[y0 + c - m - 10:y0 + c - m + 6, x0 + m - 10:x0 + c - m + 10] = np.array([0.82, 0.80, 0.76])  # sill
    v = tile_noise(n, 8, 172, octaves=3)
    img *= (0.95 + 0.08 * v)[..., None]
    lum = img.mean(-1)
    save_set("NB_TownWindows", img, lum, 0.5 - 0.3 * (lum < 0.4), 3.0)


def signs(n=1024):
    """Fictional shop-sign atlas, 4 rows (each 1024 x 256): wood/painted boards."""
    img = Image.new("RGB", (n, n), (240, 236, 226))
    d = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/georgiab.ttf", 104)
        small = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 44)
    except Exception:
        font = small = ImageFont.load_default()
    rows = [
        ("Nagisa Beach Cafe", "coffee  shaved ice  smoothies", (26, 110, 118), (250, 246, 232)),
        ("Shiokaze Surf Co.", "boards  rentals  lessons", (232, 120, 70), (255, 250, 240)),
        ("Coral Cone Gelato", "gelato  sorbet  since 2019", (246, 170, 186), (70, 40, 50)),
        ("Blue Lagoon Rentals", "kayak  SUP  snorkel", (30, 70, 130), (250, 250, 250)),
    ]
    h = n // 4
    for k, (title, sub, bg, fg) in enumerate(rows):
        y0 = k * h
        d.rectangle([0, y0, n, y0 + h], fill=bg)
        d.rectangle([10, y0 + 10, n - 10, y0 + h - 10], outline=fg, width=6)
        f = font
        size = 104
        while d.textlength(title, font=f) > n - 70 and size > 40:
            size -= 6
            try:
                f = ImageFont.truetype("C:/Windows/Fonts/georgiab.ttf", size)
            except Exception:
                break
        tw = d.textlength(title, font=f)
        d.text(((n - tw) / 2, y0 + 40 + (104 - size) // 2), title, font=f, fill=fg)
        sw = d.textlength(sub, font=small)
        d.text(((n - sw) / 2, y0 + 168), sub, font=small, fill=fg)
    a = np.asarray(img).astype(np.float32) / 255.0
    v = tile_noise(n, 8, 181, octaves=3)
    a = a * (0.94 + 0.08 * v[..., None])
    save_set("NB_Signs", a, a.mean(-1), np.full((n, n), 0.7, np.float32), 1.0)


def hull_dark(n=1024):
    """Dark navy hull + windows band for the water taxi / motor yacht superstructure."""
    yy = np.linspace(0, 1, n, endpoint=False)[:, None]
    xx = np.linspace(0, 1, n, endpoint=False)[None, :]
    v = tile_noise(n, 8, 191, octaves=4)
    col = np.ones((n, n, 3), np.float32) * np.array([0.93, 0.93, 0.92])
    band = np.broadcast_to((yy > 0.55) & (yy < 0.78), (n, n))
    win = band & np.broadcast_to(((xx * 12) % 1.0) < 0.82, (n, n))
    col[band] = np.array([0.10, 0.12, 0.16])
    col[win] = np.array([0.14, 0.24, 0.30])
    col *= (0.96 + 0.05 * v)[..., None]
    save_set("NB_Superstructure", col, 0.5 + 0.02 * v, np.where(band, 0.08, 0.2).astype(np.float32), 1.0)


def hotel_sign():
    """Bronze letters on cream stone for the porte-cochere fascia (22 m x 1 m band)."""
    W, Hh = 2048, 96
    img = Image.new("RGB", (W, Hh), (226, 216, 196))
    d = ImageDraw.Draw(img)
    try:
        f = ImageFont.truetype("C:/Windows/Fonts/georgia.ttf", 64)
    except Exception:
        f = ImageFont.load_default()
    txt = "G R A N D    S H I O K A Z E    R E S O R T"
    tw = d.textlength(txt, font=f)
    d.text(((W - tw) / 2 + 2, 14), txt, font=f, fill=(90, 62, 36))
    d.text(((W - tw) / 2, 12), txt, font=f, fill=(176, 128, 74))
    a = np.asarray(img).astype(np.float32) / 255.0
    lum = a.mean(-1)
    save_set("NB_HotelSign", a, lum, np.where(lum < 0.6, 0.35, 0.6).astype(np.float32), 2.0)


def main():
    hotel_sign()
    stone(); glass(); teak(); bronze(); plaster(); roof_tile(); deck_stone(); pool_tile()
    canvas(); gelcoat(); pontoon_wood(); palm_bark(); frond_atlas(fan=False); frond_atlas(fan=True)
    flower_shrub(magenta=False); flower_shrub(magenta=True); town_windows(); signs(); hull_dark()
    print("[nagisa-tex] done ->", OUT)


def canopy():
    """Rainforest canopy leaf clump (no blooms), darker glossy green, denser."""
    flower_shrub(name="NB_Canopy", blooms=0, leaves=1500, green=(34, 84, 34))


if __name__ == "__main__":
    if "--canopy" in sys.argv:
        canopy()
    else:
        main()
        canopy()

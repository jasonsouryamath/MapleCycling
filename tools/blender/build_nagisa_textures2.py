"""
NAGISA BAY (B5) PASS 2 - procedural PBR texture sets for the hero-quality town kit.

Plain CPython (numpy + Pillow), like build_nagisa_textures.py (which is NOT edited): writes
<Name>_Albedo / _Normal (OpenGL +Y) / _Rough (linear) PNGs, plus _Emission (L8) for the
glazing atlases, into Assets/Environment/NagisaBay/Textures. Procedural only (CC0-equivalent),
every sign name is fictional, no real brands.

  NB_Wall          1K  fine sand-float stucco, near-white so each building tints it
  NB_WallBoard     1K  horizontal weatherboard (beach houses), near-white for tinting
  NB_Interior      2K  4x4 room-interior atlas behind every window pane (+ Emission: ~40 % lit)
  NB_ShopInterior  1K  4x2 shop-display atlas (goods, cafe counter, surfboards, gelato case)
  NB_Awning        1K  8 striped canvas colourways, one per 128 px band
  NB_Signs2        2K  2 x 8 fictional shop / hotel signs (1024 x 256 cells)
  NB_Trim          1K  trim sheet, 8 bands of 128 px (see TRIM_BANDS in build_nagisa_arch.py)
  NB_Paving        1K  sidewalk pavers, running bond, 2.4 m tile
  NB_PlazaStone    1K  large cream limestone flags with a coral band (hotel / promenade plaza)
  NB_Kerb          1K  granite kerb
  NB_Lawn          1K  mown lawn with stripe + clover variation
  NB_Groundcover   1K  tropical bed: mondo grass, small leaves, bark mulch, pebbles
  NB_Gravel        1K  pale coral gravel (car ports, parking verges)
  NB_Hedge         1K  clipped leaf mass for LOW trimmed hedges (<= 0.9 m)
  NB_Solar         1K  PV panel cells
  NB_MetalRoof     1K  standing-seam roof sheet
  NB_Asphalt2      1K  fine town-street asphalt (lighter than the route)
  NB_Terrazzo      1K  terrazzo / tiled terrace floor
  NB_Boardwalk     1K  weathered ipe boardwalk decking

Run:  python tools/blender/build_nagisa_textures2.py [names...]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures")


# ------------------------------------------------------------------------------ helpers
def tile_noise(n, cells, seed, octaves=5, gain=0.5):
    rng = np.random.default_rng(seed)
    out = np.zeros((n, n), np.float32)
    amp, tot, c = 1.0, 0.0, cells
    for _ in range(octaves):
        lat = rng.random((c, c)).astype(np.float32)
        y = np.linspace(0, c, n, endpoint=False)
        x0 = np.floor(y).astype(int)
        f = y - x0
        f = f * f * (3 - 2 * f)
        x1 = (x0 + 1) % c
        a = lat[x0][:, x0]; b = lat[x0][:, x1]; cc = lat[x1][:, x0]; d = lat[x1][:, x1]
        fx = f[None, :]; fy = f[:, None]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (cc * (1 - fx) + d * fx) * fy)
        tot += amp; amp *= gain; c *= 2
    return out / tot


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    nx, ny, nz = -dx, dy, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return ((np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8)


def save_set(name, albedo, height, rough, nstrength=6.0, emission=None):
    a = (np.clip(albedo, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(a, "RGB").save(os.path.join(OUT, name + "_Albedo.png"))
    Image.fromarray(normal_from_height(np.asarray(height, np.float32), nstrength), "RGB").save(
        os.path.join(OUT, name + "_Normal.png"))
    Image.fromarray((np.clip(rough, 0, 1) * 255).astype(np.uint8), "L").save(os.path.join(OUT, name + "_Rough.png"))
    if emission is not None:
        Image.fromarray((np.clip(emission, 0, 1) * 255).astype(np.uint8), "RGB" if emission.ndim == 3 else "L").save(
            os.path.join(OUT, name + "_Emission.png"))
    print("[nagisa-tex2] %-16s %dx%d" % (name, a.shape[1], a.shape[0]))


def col(*c):
    return np.array(c, np.float32)


def font(name, size):
    for f in (name, "C:/Windows/Fonts/arialbd.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except Exception:
            pass
    return ImageFont.load_default()


def blur(a, r):
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(r))).astype(np.float32) / 255.0


# ------------------------------------------------------------------------------ walls
def wall(n=1024):
    """Lived-in sea-air stucco (NB2 pass 3): albedo ~0.72 (white plaster is NOT 0.9 - that clipped to
    flat white under the HDRP sun), float-trowel mottling, vertical rain-wash streaks from the top of
    every tile (one per storey), a damp / salt band at the foot, hairline cracks and speckle pores."""
    rng = np.random.default_rng(11)
    fine = tile_noise(n, 64, 11, 4)
    mid = tile_noise(n, 8, 12, 4)
    trowel = tile_noise(n, 24, 13, 3)
    # rain streaks: noise stretched along v (rows), brightest near the top, fading by 70 % down the tile
    sx = tile_noise(n, 96, 14, 2)
    streak = np.clip((tile_noise(n, 48, 15, 3) - 0.5) * 3.2, 0, 1)
    yy = (np.arange(n)[:, None] / n)
    fade = np.clip(1.0 - yy / 0.75, 0, 1) ** 1.5
    # stretch: sample the noise with a coarse x lattice and a smeared y
    cols_ = (np.arange(n) // 3)
    rain = np.clip((sx[0:1, :] - 0.52) * 3.0, 0, 1) * np.clip(tile_noise(n, 5, 20, 2) - 0.35, 0, 1) * 2.0   # sparse per-column weight
    rain = rain * fade * (0.35 + 0.65 * tile_noise(n, 6, 16, 2))
    rain = blur(rain, 2.0)
    foot = np.clip((yy - 0.90) / 0.10, 0, 1) ** 1.2        # damp / grime at the tile bottom (floor slab line)
    pores = blur((tile_noise(n, 256, 17, 1) > 0.86).astype(np.float32), 0.8) * 0.07
    alb = 0.74 + 0.06 * (mid - 0.5) + 0.035 * (fine - 0.5) - pores
    alb = alb - 0.07 * rain - 0.04 * foot
    # fine cracks: thin dark lines from a thresholded ridged noise
    ridge = np.abs(tile_noise(n, 10, 18, 4) - 0.5)
    crack = np.clip(1 - ridge / 0.004, 0, 1) * (tile_noise(n, 4, 19, 1) > 0.66)
    alb = alb - 0.07 * crack
    rgb = np.stack([alb, alb * 0.985 - 0.012 * rain, alb * 0.955 - 0.022 * rain - 0.010 * foot], -1)
    h = 0.5 + 0.20 * fine + 0.12 * np.abs(trowel - 0.5) - 0.25 * crack - 0.05 * pores
    rough = 0.84 + 0.08 * fine - 0.10 * rain * 0.0 + 0.06 * foot
    save_set("NB_Wall", rgb, h, rough, 3.6)


def wall_board(n=1024):
    # 8 boards per tile (tile 1.6 m -> 200 mm boards), shadow lap at each board top
    y = np.arange(n)[:, None] / n * 8.0
    f = y - np.floor(y)
    lap = np.clip(1 - f / 0.08, 0, 1) ** 2
    grain = tile_noise(n, 32, 21, 3)
    alb = 0.90 - 0.22 * lap + 0.04 * (grain - 0.5)
    alb = np.broadcast_to(alb, (n, n))
    alb = np.stack([alb, alb * 0.99, alb * 0.97], -1)
    h = np.broadcast_to(f * 0.6 - lap * 0.4, (n, n)) + 0.05 * grain
    save_set("NB_WallBoard", alb, h, 0.7 + 0.1 * grain, 5.0)


# ------------------------------------------------------------------------------ glazing
ROOM_WALLS = [(0.93, 0.89, 0.80), (0.86, 0.90, 0.88), (0.95, 0.86, 0.78), (0.82, 0.85, 0.90),
              (0.92, 0.92, 0.88), (0.88, 0.80, 0.72)]
CURTAINS = [(0.96, 0.94, 0.88), (0.90, 0.84, 0.70), (0.62, 0.78, 0.80), (0.95, 0.80, 0.72),
            (0.80, 0.86, 0.72), (0.96, 0.96, 0.96)]


def interior(n=2048):
    """4x4 cells; each cell is one window pane as seen from outside: back wall + floor + ceiling
    in fake perspective, a lamp, furniture silhouette, sheer curtains, then a sky-reflection
    gradient + streaks on top (the glass). Emission lights ~40 % of rooms (warm)."""
    rng = np.random.default_rng(301)
    alb = np.zeros((n, n, 3), np.float32)
    emi = np.zeros((n, n, 3), np.float32)
    c = n // 4
    yy, xx = np.mgrid[0:c, 0:c].astype(np.float32) / c
    for i in range(4):
        for j in range(4):
            lit = rng.random() < 0.42
            wallc = col(*ROOM_WALLS[rng.integers(len(ROOM_WALLS))])
            dark = 0.42 if not lit else 0.78
            cell = np.ones((c, c, 3), np.float32) * wallc * dark
            # back wall inset (fake depth): floor bottom 22 %, ceiling top 12 %
            fl = yy > 0.78
            cell[fl] = col(0.55, 0.42, 0.30) * dark * (0.8 + 0.2 * xx[fl, None])
            ce = yy < 0.12
            cell[ce] = wallc * dark * 1.05
            # side walls (perspective wedge)
            side = (xx < 0.10 + 0.0 * yy) | (xx > 0.90)
            cell[side] *= 0.82
            # furniture: bed / sofa / desk silhouette on the floor line
            kind = rng.integers(4)
            fx0 = 0.18 + rng.random() * 0.25
            if kind == 0:     # bed with white linen + headboard
                m = (yy > 0.62) & (yy < 0.80) & (xx > fx0) & (xx < fx0 + 0.5)
                cell[m] = col(0.95, 0.95, 0.93) * dark
                m = (yy > 0.48) & (yy < 0.64) & (xx > fx0) & (xx < fx0 + 0.5)
                cell[m] = col(0.42, 0.30, 0.22) * dark
            elif kind == 1:   # sofa
                m = (yy > 0.60) & (yy < 0.80) & (xx > fx0) & (xx < fx0 + 0.46)
                cell[m] = col(*[(0.30, 0.46, 0.56), (0.70, 0.56, 0.40), (0.40, 0.52, 0.40)][rng.integers(3)]) * dark
            elif kind == 2:   # desk + chair + picture
                m = (yy > 0.64) & (yy < 0.68) & (xx > fx0) & (xx < fx0 + 0.4)
                cell[m] = col(0.40, 0.28, 0.20) * dark
                m = (yy > 0.30) & (yy < 0.48) & (xx > fx0 + 0.08) & (xx < fx0 + 0.3)
                cell[m] = col(0.25, 0.50, 0.62) * dark
            else:             # shelving
                for s in range(4):
                    m = (yy > 0.30 + s * 0.12) & (yy < 0.32 + s * 0.12) & (xx > 0.6) & (xx < 0.86)
                    cell[m] = col(0.38, 0.26, 0.18) * dark
                    for b in range(5):
                        bm = (yy > 0.22 + s * 0.12) & (yy < 0.30 + s * 0.12) & (xx > 0.62 + b * 0.045) & (xx < 0.65 + b * 0.045)
                        cell[bm] = col(*rng.random(3) * 0.6 + 0.2) * dark
            # lamp glow
            lx, ly = 0.2 + rng.random() * 0.6, 0.35 + rng.random() * 0.15
            glow = np.exp(-(((xx - lx) ** 2) / 0.02 + ((yy - ly) ** 2) / 0.03))
            if lit:
                cell += glow[..., None] * col(0.45, 0.32, 0.16)
            # curtains: sheer panels on both sides, sometimes half-drawn across
            cc = col(*CURTAINS[rng.integers(len(CURTAINS))])
            cw = 0.14 + rng.random() * 0.22
            folds = 0.85 + 0.15 * np.sin(xx * 90.0)
            m = (xx < cw) | (xx > 1 - cw * (0.6 + rng.random() * 0.6))
            cell[m] = cell[m] * 0.35 + (cc * (0.75 if lit else 0.55))[None, :] * folds[m][:, None] * 0.65
            if rng.random() < 0.25:   # sheer drawn fully: soft veil
                cell = cell * 0.55 + cc[None, None, :] * 0.35 * (0.9 if lit else 0.6)
            # emission: interior light where lit (not the glass reflection)
            if lit:
                e = (0.55 + 0.45 * glow)[..., None] * col(1.0, 0.78, 0.50)
                e[m] *= 0.6
                emi[j * c:(j + 1) * c, i * c:(i + 1) * c] = e
            # glass: sky reflection gradient top->bottom + diagonal streak
            refl = (0.30 - 0.22 * yy)[..., None] * col(0.62, 0.80, 0.95)
            streak = np.clip(1 - np.abs((xx + yy * 0.6) - (0.5 + rng.random() * 0.6)) / 0.06, 0, 1) * 0.10
            cell = cell * 0.78 + refl + streak[..., None]
            # frame shadow line inside the pane edges
            edge = (xx < 0.015) | (xx > 0.985) | (yy < 0.015) | (yy > 0.985)
            cell[edge] *= 0.45
            alb[j * c:(j + 1) * c, i * c:(i + 1) * c] = cell
    h = alb.mean(-1) * 0.2
    save_set("NB_Interior", alb, h, np.full((n, n), 0.06, np.float32), 0.5, emission=emi)


def shop_interior(n=1024):
    """4 x 2 cells (256 x 512 each, tall): shop displays behind the ground-floor glazing."""
    rng = np.random.default_rng(311)
    alb = np.zeros((n, n, 3), np.float32)
    emi = np.zeros((n, n, 3), np.float32)
    cw, ch = n // 4, n // 2
    yy, xx = np.mgrid[0:ch, 0:cw].astype(np.float32)
    yy /= ch; xx /= cw
    kinds = ["goods", "cafe", "surf", "gelato", "fashion", "mart", "books", "dive"]
    for k, kind in enumerate(kinds):
        i, j = k % 4, k // 4
        wallc = col(*[(0.95, 0.93, 0.86), (0.90, 0.95, 0.92), (0.96, 0.90, 0.84), (0.88, 0.92, 0.96)][k % 4])
        cell = np.ones((ch, cw, 3), np.float32) * wallc * 0.95
        cell[yy > 0.82] = col(0.72, 0.68, 0.60)          # floor
        # ceiling light strip
        cell[(yy > 0.04) & (yy < 0.06)] = col(1.0, 0.98, 0.9)
        if kind in ("goods", "mart", "books"):
            for s in range(5):
                y0 = 0.22 + s * 0.12
                cell[(yy > y0 + 0.09) & (yy < y0 + 0.105)] = col(0.55, 0.55, 0.58)
                for b in range(14):
                    x0 = 0.04 + b * 0.068
                    m = (yy > y0 + 0.02 + rng.random() * 0.03) & (yy < y0 + 0.09) & (xx > x0) & (xx < x0 + 0.055)
                    cell[m] = col(*(rng.random(3) * 0.7 + 0.25))
        elif kind == "cafe":
            cell[(yy > 0.55) & (yy < 0.82)] = col(0.42, 0.30, 0.22)   # counter
            cell[(yy > 0.53) & (yy < 0.56)] = col(0.9, 0.9, 0.88)
            for b in range(3):      # pendant lamps
                g = np.exp(-(((xx - (0.2 + b * 0.3)) ** 2) / 0.003 + ((yy - 0.25) ** 2) / 0.002))
                cell += g[..., None] * col(0.6, 0.45, 0.2)
            cell[(yy > 0.12) & (yy < 0.36) & (xx > 0.15) & (xx < 0.85)] = col(0.18, 0.20, 0.20)   # menu board
            for r in range(5):
                cell[(yy > 0.15 + r * 0.04) & (yy < 0.165 + r * 0.04) & (xx > 0.2) & (xx < 0.6 + 0.05 * (r % 2))] = col(0.9, 0.9, 0.85)
        elif kind == "surf":
            for b in range(6):
                x0 = 0.08 + b * 0.145
                c2 = col(*[(0.95, 0.5, 0.3), (0.2, 0.6, 0.7), (0.95, 0.9, 0.5), (0.9, 0.9, 0.9), (0.4, 0.7, 0.4), (0.9, 0.4, 0.5)][b])
                m = ((xx - x0 - 0.05) / 0.05) ** 2 + ((yy - 0.48) / 0.34) ** 2 < 1
                cell[m] = c2
                cell[m & (np.abs(xx - x0 - 0.05) < 0.006)] = col(0.3, 0.3, 0.3)
        elif kind == "gelato":
            cell[(yy > 0.58) & (yy < 0.82)] = col(0.85, 0.88, 0.9)
            for b in range(8):
                x0 = 0.06 + b * 0.112
                cell[(yy > 0.60) & (yy < 0.66) & (xx > x0) & (xx < x0 + 0.09)] = col(*[(0.98, 0.8, 0.85), (0.6, 0.4, 0.3), (0.98, 0.95, 0.8), (0.7, 0.9, 0.6), (0.95, 0.6, 0.4), (0.7, 0.7, 0.95), (0.98, 0.98, 0.95), (0.9, 0.4, 0.5)][b])
            cell[(yy > 0.12) & (yy < 0.40)] = col(0.98, 0.84, 0.88)
        elif kind == "fashion":
            for b in range(4):
                x0 = 0.12 + b * 0.22
                m = (np.abs(xx - x0) < 0.05 + 0.02 * (yy - 0.3)) & (yy > 0.25) & (yy < 0.72)
                cell[m] = col(*(rng.random(3) * 0.6 + 0.3))
                m = ((xx - x0) ** 2 / 0.0015 + (yy - 0.21) ** 2 / 0.0015) < 1
                cell[m] = col(0.9, 0.85, 0.8)
        else:  # dive
            cell[(yy > 0.15) & (yy < 0.5)] = col(0.12, 0.45, 0.62)
            for b in range(5):
                m = ((xx - 0.15 - b * 0.18) ** 2 / 0.002 + (yy - 0.62) ** 2 / 0.01) < 1
                cell[m] = col(0.95, 0.75, 0.2)
        # reflection + emission (shops are lit)
        e = np.ones((ch, cw, 3), np.float32) * col(1.0, 0.92, 0.78) * (0.65 + 0.35 * (1 - yy))[..., None]
        refl = (0.22 - 0.18 * yy)[..., None] * col(0.62, 0.80, 0.95)
        cell = cell * 0.82 + refl
        alb[j * ch:(j + 1) * ch, i * cw:(i + 1) * cw] = cell
        emi[j * ch:(j + 1) * ch, i * cw:(i + 1) * cw] = e * 0.8
    save_set("NB_ShopInterior", alb, alb.mean(-1) * 0.2, np.full((n, n), 0.08, np.float32), 0.5, emission=emi)


# ------------------------------------------------------------------------------ canvas / signs
AWNINGS = [((0.10, 0.52, 0.56), (0.96, 0.95, 0.90)), ((0.86, 0.36, 0.30), (0.97, 0.95, 0.90)),
           ((0.20, 0.36, 0.62), (0.96, 0.96, 0.94)), ((0.98, 0.78, 0.30), (0.98, 0.97, 0.92)),
           ((0.40, 0.62, 0.36), (0.96, 0.95, 0.88)), ((0.92, 0.56, 0.62), (0.98, 0.96, 0.94)),
           ((0.86, 0.84, 0.76), (0.86, 0.84, 0.76)), ((0.20, 0.24, 0.28), (0.20, 0.24, 0.28))]


def awning(n=1024):
    """8 bands of 128 px; u tiles every 1.2 m (8 stripes per tile). Band 6 = plain sand canvas,
    band 7 = charcoal canvas."""
    alb = np.zeros((n, n, 3), np.float32)
    weave = tile_noise(n, 128, 41, 2)
    x = np.arange(n) / n * 8.0
    stripe = (np.floor(x) % 2 == 0)
    bh = n // 8
    for k, (a, b) in enumerate(AWNINGS):
        band = np.where(stripe[None, :, None], col(*a), col(*b))
        band = np.broadcast_to(band, (bh, n, 3)).copy()
        yy = np.arange(bh)[:, None] / bh
        band *= (0.92 + 0.08 * yy)[..., None]      # sun-bleach toward the leading edge
        alb[k * bh:(k + 1) * bh] = band
    alb *= (0.94 + 0.08 * weave)[..., None]
    save_set("NB_Awning", alb, 0.5 + 0.08 * weave, 0.85 + 0.1 * weave, 2.0)


SIGNS = [
    ("Kaiyo Mart 24", "groceries  drinks  ice", (0.12, 0.46, 0.34), (1, 1, 1)),
    ("Sango Gelato", "handmade daily", (0.97, 0.72, 0.78), (0.35, 0.18, 0.22)),
    ("Tida Surf Rentals", "boards  SUP  lessons", (0.95, 0.52, 0.22), (1, 1, 1)),
    ("Umi no Ie", "beach bar  grill", (0.16, 0.30, 0.52), (0.98, 0.92, 0.72)),
    ("Yashi Coffee", "roasters  since 1998", (0.28, 0.20, 0.16), (0.96, 0.88, 0.72)),
    ("Hibiscus Bakery", "pan  cakes  sandwiches", (0.98, 0.95, 0.88), (0.72, 0.18, 0.30)),
    ("Blue Reef Dive", "snorkel  dive  tours", (0.08, 0.40, 0.58), (1, 1, 1)),
    ("Nagisa Books", "maps  novels  postcards", (0.36, 0.44, 0.34), (0.98, 0.96, 0.88)),
    ("Hotel Shirahama", "", (0.94, 0.92, 0.86), (0.22, 0.34, 0.42)),
    ("Villa Hinata", "boutique hotel", (0.20, 0.26, 0.24), (0.92, 0.84, 0.62)),
    ("Coral Pharmacy", "", (0.95, 0.96, 0.96), (0.10, 0.52, 0.42)),
    ("Island Poke", "bowls  juices", (0.98, 0.84, 0.38), (0.20, 0.20, 0.22)),
    ("Nagisa Marina Club", "restaurant  bar", (0.10, 0.20, 0.34), (0.95, 0.86, 0.60)),
    ("Sunset Shave Ice", "", (0.36, 0.72, 0.84), (1, 1, 1)),
    ("Palm Court", "residences", (0.90, 0.88, 0.80), (0.30, 0.34, 0.30)),
    ("Hanabi Ramen", "noodles  gyoza", (0.70, 0.16, 0.14), (1, 0.96, 0.88)),
]


def signs2(n=2048):
    img = Image.new("RGB", (n, n), (240, 236, 226))
    d = ImageDraw.Draw(img)
    cw, ch = n // 2, n // 8
    for k, (title, sub, bg, fg) in enumerate(SIGNS):
        i, j = k % 2, k // 2
        x0, y0 = i * cw, j * ch
        bgc = tuple(int(v * 255) for v in bg); fgc = tuple(int(v * 255) for v in fg)
        d.rectangle([x0, y0, x0 + cw, y0 + ch], fill=bgc)
        d.rectangle([x0 + 12, y0 + 12, x0 + cw - 12, y0 + ch - 12], outline=fgc, width=5)
        size = 118 if sub else 132
        f = font("C:/Windows/Fonts/georgiab.ttf", size)
        while d.textlength(title, font=f) > cw - 80 and size > 40:
            size -= 6
            f = font("C:/Windows/Fonts/georgiab.ttf", size)
        tw = d.textlength(title, font=f)
        ty = y0 + (28 if sub else (ch - size) // 2 - 8)
        d.text((x0 + (cw - tw) / 2, ty), title, font=f, fill=fgc)
        if sub:
            s = font("C:/Windows/Fonts/arialbd.ttf", 44)
            sw = d.textlength(sub, font=s)
            d.text((x0 + (cw - sw) / 2, y0 + ch - 76), sub, font=s, fill=fgc)
    a = np.asarray(img).astype(np.float32) / 255.0
    v = tile_noise(n, 16, 51, 3)
    a = a * (0.95 + 0.06 * v[..., None])
    save_set("NB_Signs2", a, a.mean(-1) * 0.4, np.full((n, n), 0.55, np.float32), 1.0)


SIGNS3 = [
    ("Grand Shiokaze", "resort  spa  residences", (0.10, 0.20, 0.30), (0.96, 0.88, 0.66)),
    ("Coral Terrace", "resort & spa", (0.95, 0.60, 0.50), (1, 1, 1)),
    ("Twin Palms", "hotel  conference", (0.14, 0.38, 0.34), (0.96, 0.92, 0.78)),
    ("Hinata Bungalows", "garden resort", (0.30, 0.22, 0.14), (0.98, 0.90, 0.68)),
    ("Shiosai Crescent", "beach hotel", (0.92, 0.94, 0.94), (0.10, 0.34, 0.50)),
    ("Blue Lagoon", "hotel", (0.05, 0.38, 0.56), (1, 1, 1)),
    ("Sunset Villas", "suites  pool  bar", (0.78, 0.30, 0.22), (1, 0.94, 0.80)),
    ("Nagisa Beach Club", "day pass  cabanas", (0.96, 0.92, 0.82), (0.12, 0.32, 0.40)),
    ("Izakaya Nami", "yakitori  sake  beer", (0.16, 0.12, 0.10), (0.96, 0.76, 0.34)),
    ("Kaze Rental Bikes", "e-bikes  scooters", (0.20, 0.50, 0.62), (1, 1, 1)),
    ("Kai Surf Shop", "boards  wax  wetsuits", (0.96, 0.78, 0.20), (0.10, 0.18, 0.28)),
    ("Ramen Taki", "tonkotsu  shoyu", (0.72, 0.12, 0.10), (1, 0.94, 0.80)),
    ("Poke House", "bowls  smoothies", (0.30, 0.62, 0.46), (1, 1, 1)),
    ("Ukulele Lane", "music  gifts", (0.54, 0.34, 0.50), (1, 0.92, 0.86)),
    ("Shell Gallery", "art  ceramics", (0.94, 0.90, 0.84), (0.34, 0.26, 0.22)),
    ("Reef Dive House", "padi  boat trips", (0.04, 0.26, 0.42), (0.96, 0.84, 0.30)),
]


def signs3(n=2048):
    global SIGNS
    keep = SIGNS
    SIGNS = SIGNS3
    try:
        # reuse the signs2 painter, then re-save under the Signs3 name
        img = Image.new("RGB", (n, n), (240, 236, 226))
        d = ImageDraw.Draw(img)
        cw, ch = n // 2, n // 8
        for k, (title, sub, bg, fg) in enumerate(SIGNS3):
            i, j = k % 2, k // 2
            x0, y0 = i * cw, j * ch
            bgc = tuple(int(v * 255) for v in bg); fgc = tuple(int(v * 255) for v in fg)
            d.rectangle([x0, y0, x0 + cw, y0 + ch], fill=bgc)
            d.rectangle([x0 + 12, y0 + 12, x0 + cw - 12, y0 + ch - 12], outline=fgc, width=5)
            d.rectangle([x0 + 22, y0 + 22, x0 + cw - 22, y0 + ch - 22], outline=fgc, width=2)
            size = 118 if sub else 132
            f = font("C:/Windows/Fonts/georgiab.ttf", size)
            while d.textlength(title, font=f) > cw - 90 and size > 40:
                size -= 6
                f = font("C:/Windows/Fonts/georgiab.ttf", size)
            tw = d.textlength(title, font=f)
            ty = y0 + (28 if sub else (ch - size) // 2 - 8)
            d.text((x0 + (cw - tw) / 2, ty), title, font=f, fill=fgc)
            if sub:
                s_ = font("C:/Windows/Fonts/arialbd.ttf", 44)
                sw = d.textlength(sub, font=s_)
                d.text((x0 + (cw - sw) / 2, y0 + ch - 76), sub, font=s_, fill=fgc)
        a = np.asarray(img).astype(np.float32) / 255.0
        v = tile_noise(n, 16, 52, 3)
        a = a * (0.95 + 0.06 * v[..., None])
        save_set("NB_Signs3", a, a.mean(-1) * 0.4, np.full((n, n), 0.55, np.float32), 1.0)
    finally:
        SIGNS = keep


# ------------------------------------------------------------------------------ trim sheet
def trim(n=1024):
    """8 bands x 128 px. 0 white powder-coat aluminium, 1 bronze anodised, 2 black steel,
    3 concrete coping, 4 teak, 5 travertine sill, 6 turquoise glazed tile, 7 white timber."""
    alb = np.zeros((n, n, 3), np.float32)
    h = np.zeros((n, n), np.float32)
    r = np.zeros((n, n), np.float32)
    bh = n // 8
    fine = tile_noise(n, 64, 61, 3)
    grain = tile_noise(n, 16, 62, 4)
    x = np.arange(n) / n
    specs = [
        (col(0.94, 0.94, 0.93), 0.35, 0.02), (col(0.46, 0.34, 0.22), 0.30, 0.02),
        (col(0.14, 0.15, 0.16), 0.40, 0.02), (col(0.78, 0.76, 0.72), 0.85, 0.10),
        (col(0.58, 0.38, 0.22), 0.60, 0.06), (col(0.88, 0.82, 0.70), 0.70, 0.08),
        (col(0.22, 0.62, 0.64), 0.20, 0.03), (col(0.95, 0.94, 0.91), 0.55, 0.03),
    ]
    for k, (c, rough, amp) in enumerate(specs):
        s = slice(k * bh, (k + 1) * bh)
        yy = np.arange(bh)[:, None] / bh
        bevel = np.clip(np.minimum(yy, 1 - yy) / 0.08, 0, 1)         # edge highlight / chamfer
        base = np.broadcast_to(c, (bh, n, 3)).copy()
        if k == 4:
            gr = np.sin((x[None, :] * 40 + grain[s] * 6) * np.pi) * 0.5 + 0.5
            base *= (0.85 + 0.2 * gr)[..., None]
        if k == 6:
            tiles = ((np.floor(x * 16) % 2) == 0)[None, :]
            base *= np.where(tiles, 1.0, 0.92)[..., None]
            gl = (np.abs((x * 16) % 1 - 0.5) > 0.47)[None, :]
            base[np.broadcast_to(gl, (bh, n))] = col(0.9, 0.9, 0.86)
        base *= (1 - amp + amp * 2 * fine[s])[..., None]
        base *= (0.80 + 0.2 * bevel)[..., None]
        alb[s] = base
        h[s] = bevel * 0.5 + amp * fine[s]
        r[s] = rough + 0.05 * fine[s]
    save_set("NB_Trim", alb, h, r, 4.0)


# ------------------------------------------------------------------------------ ground
def paving(n=1024):
    """Running-bond 300 x 150 mm pavers (tile 2.4 m -> 8 x 16), warm grey with sand joints."""
    y = np.arange(n)[:, None] / n * 16
    x = np.arange(n)[None, :] / n * 8
    row = np.floor(y)
    xo = x + (row % 2) * 0.5
    fx, fy = xo - np.floor(xo), y - row
    joint = np.clip(1 - np.minimum(np.minimum(fx, 1 - fx) * 30, np.minimum(fy, 1 - fy) * 15), 0, 1)
    rng = np.random.default_rng(71)
    lut = rng.random((16, 16)).astype(np.float32)
    pid = lut[(row.astype(int) % 16), (np.floor(xo).astype(int) % 16)]
    fine = tile_noise(n, 64, 72, 3)
    stain = tile_noise(n, 6, 73, 3)
    base = 0.74 + 0.08 * (pid - 0.5) + 0.05 * (fine - 0.5) - 0.06 * np.clip(stain - 0.55, 0, 1)
    alb = np.stack([base, base * 0.97, base * 0.92], -1)
    alb = alb * (1 - joint[..., None] * 0.35) + joint[..., None] * col(0.55, 0.50, 0.42) * 0.35
    h = 0.6 - joint * 0.6 + 0.05 * fine
    save_set("NB_Paving", alb, h, 0.8 + 0.1 * joint, 6.0)


def plaza_stone(n=1024):
    """600 x 600 flags (tile 2.4 m -> 4 x 4) cream limestone, coral-pink band every tile edge."""
    y = np.arange(n)[:, None] / n * 4
    x = np.arange(n)[None, :] / n * 4
    fx, fy = x - np.floor(x), y - np.floor(y)
    joint = np.clip(1 - np.minimum(np.minimum(fx, 1 - fx), np.minimum(fy, 1 - fy)) * 90, 0, 1)
    fine = tile_noise(n, 48, 81, 4)
    fossil = tile_noise(n, 96, 82, 2)
    rng = np.random.default_rng(83)
    lut = rng.random((4, 4)).astype(np.float32)
    pid = lut[np.floor(y).astype(int) % 4, np.floor(x).astype(int) % 4]
    base = 0.86 + 0.05 * (pid - 0.5) + 0.04 * (fine - 0.5) - 0.05 * (fossil > 0.72)
    alb = np.stack([base, base * 0.955, base * 0.89], -1)
    band = (np.abs(np.arange(n) / n - 0.5) < 0.02)
    alb[band, :, :] = alb[band, :, :] * 0.7 + col(0.86, 0.56, 0.48) * 0.3
    alb = alb * (1 - joint[..., None] * 0.25)
    save_set("NB_PlazaStone", alb, 0.6 - joint * 0.5 + 0.06 * fine, 0.7 + 0.1 * fine, 4.0)


def kerb(n=1024):
    fine = tile_noise(n, 96, 91, 3)
    speck = (tile_noise(n, 256, 92, 1) > 0.72).astype(np.float32)
    x = np.arange(n) / n * 4
    seg = np.clip(1 - np.abs((x % 1) - 0.5) * 2 / 0.015 + 49, 0, 1)
    base = 0.66 + 0.06 * (fine - 0.5) - 0.12 * speck + 0.08 * (tile_noise(n, 256, 93, 1) > 0.8)
    base = base * (1 - 0.3 * (np.abs((x % 1) - 0.5) > 0.49))[None, :]
    alb = np.stack([base, base, base * 0.98], -1)
    save_set("NB_Kerb", alb, 0.5 + 0.1 * fine, 0.75 + 0.1 * fine, 3.0)
    _ = seg


def lawn(n=1024):
    blades = tile_noise(n, 256, 101, 2)
    patch = tile_noise(n, 6, 102, 4)
    clover = tile_noise(n, 48, 103, 3)
    x = np.arange(n) / n * 4
    mow = (np.floor(x) % 2 == 0)[None, :].astype(np.float32)
    g = col(0.30, 0.46, 0.20)
    alb = np.broadcast_to(g, (n, n, 3)).copy()
    alb *= (0.84 + 0.25 * blades)[..., None]
    alb *= (0.94 + 0.08 * mow)[..., None]
    dry = np.clip(patch - 0.58, 0, 1) * 2.2
    alb = alb * (1 - dry[..., None]) + col(0.55, 0.52, 0.30) * dry[..., None]
    cl = np.clip(clover - 0.66, 0, 1) * 3
    alb = alb * (1 - cl[..., None] * 0.5) + col(0.24, 0.42, 0.18) * cl[..., None] * 0.5
    save_set("NB_Lawn", alb, 0.5 + 0.3 * blades, 0.9 - 0.05 * blades, 5.0)


def groundcover(n=1024):
    rng = np.random.default_rng(111)
    img = Image.new("RGB", (n, n), (78, 56, 38))      # bark mulch base
    d = ImageDraw.Draw(img)
    # mulch chips
    for _ in range(9000):
        x, y = rng.integers(0, n, 2)
        l = rng.integers(4, 12)
        c = tuple(int(v) for v in (np.array([96, 68, 44]) * (0.7 + 0.5 * rng.random())))
        d.line([(x, y), (x + l * rng.uniform(-1, 1), y + l * rng.uniform(-1, 1))], fill=c, width=2)
    # leafy tufts (mondo grass / small-leaf groundcover), clustered
    cl = tile_noise(n, 8, 112, 3)
    for _ in range(26000):
        x, y = rng.integers(0, n, 2)
        if cl[y, x] < 0.42:
            continue
        a = rng.uniform(0, np.pi * 2)
        l = rng.integers(6, 18)
        g = np.array([44, 98, 40]) * (0.7 + 0.6 * rng.random())
        if rng.random() < 0.08:
            g = np.array([120, 150, 60])
        d.line([(x, y), (x + np.cos(a) * l, y + np.sin(a) * l)], fill=tuple(int(v) for v in g), width=3)
    # pebbles
    for _ in range(900):
        x, y = rng.integers(0, n, 2)
        r = rng.integers(2, 6)
        v = int(170 + 60 * rng.random())
        d.ellipse([x - r, y - r, x + r, y + r], fill=(v, v - 8, v - 20))
    a = np.asarray(img).astype(np.float32) / 255.0
    # make it tile: blend with a half-shifted copy at the borders
    s = np.roll(np.roll(a, n // 2, 0), n // 2, 1)
    w = np.minimum(np.minimum(np.arange(n), n - 1 - np.arange(n)) / (n * 0.12), 1.0)
    W = np.minimum(w[:, None], w[None, :])[..., None]
    a = a * W + s * (1 - W)
    save_set("NB_Groundcover", a, a.mean(-1), 0.9 - 0.2 * a[..., 1], 6.0)


def gravel(n=1024):
    rng = np.random.default_rng(121)
    img = Image.new("RGB", (n, n), (196, 186, 168))
    d = ImageDraw.Draw(img)
    for _ in range(40000):
        x, y = rng.integers(0, n, 2)
        r = rng.integers(2, 5)
        v = 150 + 100 * rng.random()
        t = rng.random()
        c = (int(v), int(v * (0.94 + 0.04 * t)), int(v * (0.84 + 0.06 * t)))
        d.ellipse([x - r, y - r, x + r, y + r], fill=c)
    a = np.asarray(img).astype(np.float32) / 255.0
    s = np.roll(np.roll(a, n // 2, 0), n // 2, 1)
    w = np.minimum(np.minimum(np.arange(n), n - 1 - np.arange(n)) / (n * 0.1), 1.0)
    W = np.minimum(w[:, None], w[None, :])[..., None]
    a = a * W + s * (1 - W)
    save_set("NB_Gravel", a, a.mean(-1), np.full((n, n), 0.9, np.float32), 5.0)


def hedge(n=1024):
    rng = np.random.default_rng(131)
    img = Image.new("RGB", (n, n), (30, 62, 28))
    d = ImageDraw.Draw(img)
    for _ in range(30000):
        x, y = rng.integers(0, n, 2)
        rx, ry = rng.integers(5, 11), rng.integers(3, 6)
        g = np.array([52, 108, 44]) * (0.6 + 0.7 * rng.random())
        d.ellipse([x - rx, y - ry, x + rx, y + ry], fill=tuple(int(v) for v in g))
    a = np.asarray(img).astype(np.float32) / 255.0
    s = np.roll(np.roll(a, n // 2, 0), n // 2, 1)
    w = np.minimum(np.minimum(np.arange(n), n - 1 - np.arange(n)) / (n * 0.1), 1.0)
    W = np.minimum(w[:, None], w[None, :])[..., None]
    a = a * W + s * (1 - W)
    save_set("NB_Hedge", a, a.mean(-1), np.full((n, n), 0.8, np.float32), 7.0)


def solar(n=1024):
    y = np.arange(n)[:, None] / n * 10
    x = np.arange(n)[None, :] / n * 6
    fx, fy = x - np.floor(x), y - np.floor(y)
    grid = (np.minimum(fx, 1 - fx) < 0.03) | (np.minimum(fy, 1 - fy) < 0.03)
    bus = (np.abs(fx - 0.33) < 0.006) | (np.abs(fx - 0.66) < 0.006)
    alb = np.broadcast_to(col(0.08, 0.12, 0.22), (n, n, 3)).copy()
    alb[np.broadcast_to(grid, (n, n))] = col(0.75, 0.76, 0.78)
    alb[np.broadcast_to(bus & ~grid, (n, n))] = col(0.45, 0.46, 0.5)
    frame = (np.arange(n) < 10) | (np.arange(n) > n - 11)
    alb[frame, :] = col(0.72, 0.72, 0.74); alb[:, frame] = col(0.72, 0.72, 0.74)
    save_set("NB_Solar", alb, 0.5 - 0.2 * grid, np.where(grid, 0.5, 0.12).astype(np.float32), 2.0)


def metal_roof(n=1024):
    x = np.arange(n) / n * 4     # 4 seams per tile (tile 2.0 m -> 500 mm pans)
    f = x - np.floor(x)
    seam = np.exp(-((f - 0.5) ** 2) / 0.0006)
    fine = tile_noise(n, 32, 141, 3)
    base = 0.72 + 0.1 * seam - 0.06 * (np.abs(f - 0.5) > 0.44)
    alb = np.broadcast_to(base[None, :], (n, n)) * (0.96 + 0.05 * fine)
    alb = np.stack([alb, alb, alb * 1.01], -1)
    save_set("NB_MetalRoof", alb, np.broadcast_to(seam[None, :], (n, n)) + 0.02 * fine, 0.45 + 0.1 * fine, 6.0)


def asphalt2(n=1024):
    fine = tile_noise(n, 256, 151, 2)
    agg = (tile_noise(n, 512, 152, 1) > 0.7).astype(np.float32)
    patch = tile_noise(n, 5, 153, 3)
    base = 0.36 + 0.06 * (fine - 0.5) + 0.07 * agg - 0.05 * np.clip(patch - 0.6, 0, 1) * 3
    alb = np.stack([base, base, base * 1.02], -1)
    save_set("NB_Asphalt2", alb, 0.5 + 0.15 * fine + 0.1 * agg, 0.85 - 0.1 * agg, 3.0)


def terrazzo(n=1024):
    rng = np.random.default_rng(161)
    img = Image.new("RGB", (n, n), (226, 220, 206))
    d = ImageDraw.Draw(img)
    for _ in range(5000):
        x, y = rng.integers(0, n, 2)
        r = rng.integers(2, 7)
        c = [(180, 170, 150), (120, 140, 140), (210, 150, 130), (90, 90, 90), (240, 236, 228)][rng.integers(5)]
        d.ellipse([x - r, y - r, x + r * rng.uniform(0.6, 1.4), y + r], fill=c)
    a = np.asarray(img).astype(np.float32) / 255.0
    s = np.roll(np.roll(a, n // 2, 0), n // 2, 1)
    w = np.minimum(np.minimum(np.arange(n), n - 1 - np.arange(n)) / (n * 0.1), 1.0)
    W = np.minimum(w[:, None], w[None, :])[..., None]
    a = a * W + s * (1 - W)
    y = np.arange(n) / n * 4
    jt = (np.minimum(y % 1, 1 - y % 1) < 0.006)
    a[jt, :] *= 0.8; a[:, jt] *= 0.8
    save_set("NB_Terrazzo", a, a.mean(-1) * 0.3, np.full((n, n), 0.35, np.float32), 1.5)


def boardwalk(n=1024):
    """Ipe decking, 140 mm boards + 6 mm gaps across v (tile 1.2 m -> 8 boards), silvered."""
    y = np.arange(n)[:, None] / n * 8
    f = y - np.floor(y)
    gap = (f > 0.95).astype(np.float32)
    rng = np.random.default_rng(171)
    lut = rng.random(8).astype(np.float32)
    bid = lut[np.floor(y).astype(int) % 8]
    grain = tile_noise(n, 16, 172, 4)
    streak = np.sin((np.arange(n)[None, :] / n * 30 + grain * 5) * np.pi) * 0.5 + 0.5
    base = col(0.56, 0.44, 0.33)
    silver = col(0.66, 0.63, 0.58)
    mixw = (0.35 + 0.3 * bid)
    alb = base * (1 - mixw[..., None]) + silver * mixw[..., None]
    alb = np.broadcast_to(alb, (n, n, 3)) * (0.85 + 0.2 * streak)[..., None]
    alb = alb * (1 - gap[..., None] * 0.8)
    # butt joints
    x = np.arange(n)[None, :] / n * 2 + bid * 0.7
    bj = (np.abs((x % 1) - 0.5) > 0.495)
    alb = np.where(bj[..., None], alb * 0.5, alb)
    save_set("NB_Boardwalk", alb, np.broadcast_to(0.6 - gap * 0.6, (n, n)) + 0.05 * grain, 0.75 + 0.1 * grain, 5.0)


def thatch(n=1024):
    """Palm-leaf / alang-alang thatch: overlapping horizontal courses of straw strands."""
    rng = np.random.default_rng(141)
    img = Image.new("RGB", (n, n), (120, 92, 52))
    d = ImageDraw.Draw(img)
    courses = 8
    ch = n // courses
    for c in range(courses):
        y0 = c * ch
        for _ in range(2600):
            x = int(rng.integers(0, n))
            L = int(rng.integers(ch * 7 // 10, ch * 12 // 10))
            dx = int(rng.integers(-6, 7))
            k = 0.65 + 0.55 * rng.random()
            base = np.array([196, 160, 98]) * k
            for w in (0, n):
                d.line([(x + dx - w, y0 + int(rng.integers(0, 8))), (x - w, y0 + L)],
                       fill=tuple(int(min(255, v)) for v in base), width=int(rng.integers(2, 4)))
        d.line([(0, y0 + ch - 3), (n, y0 + ch - 3)], fill=(70, 52, 30), width=5)   # course shadow line
    a = np.asarray(img).astype(np.float32) / 255.0
    v = tile_noise(n, 8, 141, 3)
    a = a * (0.88 + 0.2 * v[..., None])
    save_set("NB_Thatch", a, a.mean(-1), np.full((n, n), 0.92, np.float32), 5.0)


ALL = {
    "wall": wall, "wallboard": wall_board, "interior": interior, "shop": shop_interior,
    "awning": awning, "signs2": signs2, "signs3": signs3, "trim": trim, "paving": paving, "plaza": plaza_stone,
    "kerb": kerb, "lawn": lawn, "groundcover": groundcover, "gravel": gravel, "hedge": hedge,
    "solar": solar, "metalroof": metal_roof, "asphalt2": asphalt2, "terrazzo": terrazzo,
    "boardwalk": boardwalk, "thatch": thatch,
}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    names = sys.argv[1:] or list(ALL)
    for nm in names:
        ALL[nm]()
    print("[nagisa-tex2] done ->", OUT)

"""
MINATO COAST - SKYLINE workstream facade textures (Glass Tide District).

Plain CPython (numpy + Pillow), no Blender needed:

    python tools/blender/build_minato_skyline_textures.py

Writes three TILEABLE facade textures into Assets/Environment/MinatoCoast/Skyline/:

  Minato_Skyline_Curtain.png  - unitised blue-green curtain wall. Tile = 4 panels x 4 floors
                                (6.0 m x 15.0 m). NEAR-NEUTRAL values: the Unity material colour
                                supplies the hue, so one texture serves blue, teal and deep glass.
  Minato_Skyline_Ribbon.png   - white-concrete + ribbon-window facade for the mid-rise family.
                                Tile = 4 panels x 2 floors (6.0 m x 7.5 m). Full colour, the
                                material colour stays white.
  Minato_Skyline_Shopfront.png - podium / ground-floor retail: warm interiors behind tall glazing,
                                dark mullions, white fascia. Tile = 3 bays x 1 storey (9 m x 5 m).

WHY a texture and not modelled mullions: the old SkyTower kit modelled a white mullion + spandrel
grid on every floor, which at 100-500 m collapsed into a high-contrast "graph paper" pattern and
cost thousands of boxes per tower. Here the grid is fine and LOW contrast (mullions a touch
lighter than the glass, spandrels a touch darker), and the glass carries soft per-panel value
jitter plus a sky-reflection gradient per floor, which is what reads as reflective glass at range
under a cel shader that has no real reflections. Mip-mapping averages it cleanly at distance.

The UV contract (see build_minato_skyline.py): u = perimeter metres / tile width,
v = height metres / tile height. ALL values PROVISIONAL illustrative tuning.
"""

import os

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "MinatoCoast", "Skyline")
os.makedirs(OUT, exist_ok=True)


def periodic_noise(h, w, cells, seed, octaves=3):
    """Tileable value noise in [0,1] (bilinear lattice that wraps)."""
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        cy, cx = cells[0] * (2 ** o), cells[1] * (2 ** o)
        lat = rng.random((cy, cx)).astype(np.float32)
        ys = np.arange(h) / h * cy
        xs = np.arange(w) / w * cx
        y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
        ty = (ys - y0)[:, None]; tx = (xs - x0)[None, :]
        ty = ty * ty * (3 - 2 * ty); tx = tx * tx * (3 - 2 * tx)
        y1 = (y0 + 1) % cy; x1 = (x0 + 1) % cx
        y0 %= cy; x0 %= cx
        a = lat[y0][:, x0]; b = lat[y0][:, x1]; c = lat[y1][:, x0]; d = lat[y1][:, x1]
        out += amp * ((a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty)
        tot += amp
        amp *= 0.5
    return out / tot


def save(img, name):
    arr = np.clip(img, 0.0, 1.0)
    Image.fromarray((arr * 255.0 + 0.5).astype(np.uint8)).save(os.path.join(OUT, name))
    print(f"[skyline-tex] wrote {name} {arr.shape[1]}x{arr.shape[0]}")


# ------------------------------------------------------------------ 1. curtain wall

def curtain():
    W = H = 1024
    PANELS, FLOORS = 4, 4
    pw, fh = W // PANELS, H // FLOORS
    rng = np.random.default_rng(71)
    img = np.zeros((H, W, 3), np.float32)
    # Image row 0 is the TOP of the tile (v = 1). Work in "height from floor" terms.
    yy, xx = np.mgrid[0:H, 0:W]
    floor_idx = (H - 1 - yy) // fh                   # 0 = lowest floor in the tile
    in_floor = ((H - 1 - yy) % fh) / fh              # 0 at floor line -> 1 at next floor
    panel_idx = xx // pw
    in_panel = (xx % pw) / pw

    cloud = periodic_noise(H, W, (3, 2), 5)          # broad reflected-sky variation
    fine = periodic_noise(H, W, (16, 16), 9, 2)

    base = np.zeros((H, W), np.float32)
    for f in range(FLOORS):
        for p in range(PANELS):
            m = (floor_idx == f) & (panel_idx == p)
            base[m] = 0.60 + rng.random() * 0.12
    # Sky-reflection gradient inside each vision panel: brighter towards the top.
    glass = base + 0.10 * in_floor + 0.14 * (cloud - 0.5) + 0.03 * (fine - 0.5)
    # A few panels with interior blinds drawn part-way (a lighter upper band).
    for f in range(FLOORS):
        for p in range(PANELS):
            if rng.random() < 0.18:
                drop = 0.45 + rng.random() * 0.35
                m = (floor_idx == f) & (panel_idx == p) & (in_floor > drop)
                glass[m] += 0.09

    v = glass.copy()
    # Spandrel: the bottom 17 % of each floor, a touch DARKER than the glass (dark back-pan
    # glass), with a hairline metal transom at each edge. Low contrast on purpose.
    span = in_floor < 0.17
    v[span] = 0.50 + 0.04 * cloud[span]
    trans = (np.abs(in_floor - 0.17) < 0.012) | (in_floor < 0.012) | (in_floor > 0.988)
    v[trans] = 0.84
    # Mullions: slim, lighter brushed metal.
    mull = (in_panel < 0.022) | (in_panel > 0.978)
    v[mull] = 0.86 + 0.03 * fine[mull]

    # Slight cool cast in the lit glass, neutral metal.
    img[..., 0] = v * 0.94
    img[..., 1] = v * 0.99
    img[..., 2] = v * 1.04
    metal = trans | mull
    img[metal] = np.stack([v[metal]] * 3, -1)
    save(img, "Minato_Skyline_Curtain.png")


# ------------------------------------------------------------------ 2. ribbon facade

def ribbon():
    W = H = 1024
    PANELS, FLOORS = 4, 2
    pw, fh = W // PANELS, H // FLOORS
    yy, xx = np.mgrid[0:H, 0:W]
    in_floor = ((H - 1 - yy) % fh) / fh
    in_panel = (xx % pw) / pw
    cloud = periodic_noise(H, W, (2, 3), 11)
    grain = periodic_noise(H, W, (64, 8), 13, 2)     # faint vertical render grain

    img = np.zeros((H, W, 3), np.float32)
    white = np.array([0.93, 0.925, 0.90], np.float32)
    band = in_floor < 0.30                            # 1.1 m white concrete band
    img[band] = white * (0.97 + 0.04 * grain[band])[:, None]
    # Shadow line under the slab edge (the band is proud of the glazing).
    shadow = (in_floor >= 0.30) & (in_floor < 0.34)
    img[shadow] = np.array([0.20, 0.26, 0.30])

    g = ~band & ~shadow
    t = (in_floor - 0.30) / 0.70
    glass = np.stack([0.20 + 0.10 * t, 0.36 + 0.12 * t, 0.44 + 0.14 * t], -1)
    glass += (0.10 * (cloud - 0.5))[..., None]
    img[g] = glass[g]
    mull = g & ((in_panel < 0.018) | (in_panel > 0.982))
    img[mull] = np.array([0.74, 0.77, 0.80])
    sill = g & (np.abs(in_floor - 0.345) < 0.008)
    img[sill] = np.array([0.78, 0.80, 0.82])
    save(img, "Minato_Skyline_Ribbon.png")


# ------------------------------------------------------------------ 3. shopfront

def shopfront():
    W, H = 1024, 576                                  # 9 m x 5 m -> ~114 px/m both ways
    BAYS = 3
    bw = W // BAYS
    yy, xx = np.mgrid[0:H, 0:W]
    y = (H - 1 - yy) / H * 5.0                        # metres above pavement
    in_bay = (xx % bw) / bw
    bay = xx // bw
    rng = np.random.default_rng(3)
    blob = periodic_noise(H, W, (4, 9), 21)

    img = np.zeros((H, W, 3), np.float32)
    # Warm interior: bright amber at counter height fading to a dim ceiling.
    t = np.clip((y - 0.3) / 3.4, 0, 1)
    warm = np.stack([0.95 - 0.35 * t, 0.70 - 0.30 * t, 0.42 - 0.18 * t], -1)
    warm *= (0.85 + 0.30 * blob)[..., None]
    img[:] = warm
    # Daylight reflection on the upper glazing: at 30-150 m an all-amber storefront read as
    # timber cladding, so the top of each pane reflects the cool sky/plaza like real glass.
    refl = np.clip((y - 1.9) / 1.1, 0, 1)[..., None] * (0.55 + 0.25 * blob)[..., None]
    sky = np.stack([0.62 + 0 * y, 0.72 + 0 * y, 0.80 + 0 * y], -1)
    img[:] = img * (1 - refl) + sky * refl
    # Ceiling light strip.
    img[(y > 3.35) & (y < 3.45)] = np.array([1.0, 0.93, 0.78])
    # Plinth.
    img[y < 0.28] = np.array([0.30, 0.32, 0.34])
    # Fascia band with a slim coral sign on the middle bay only (restrained accent).
    fascia = y > 3.9
    img[fascia] = np.array([0.92, 0.915, 0.89])
    sign = fascia & (bay == 1) & (in_bay > 0.25) & (in_bay < 0.75) & (y > 4.25) & (y < 4.6)
    img[sign] = np.array([0.93, 0.42, 0.30])
    # Storefront frame: dark bronze mullions at bay edges and a mid mullion, transom at 3.0 m.
    frame = ~fascia & (y >= 0.28) & ((in_bay < 0.02) | (in_bay > 0.98) |
                                     (np.abs(in_bay - 0.5) < 0.008) |
                                     (np.abs(y - 3.0) < 0.035) | (np.abs(y - 3.88) < 0.03))
    img[frame] = np.array([0.22, 0.24, 0.26])
    # Door in the left half of bay 0.
    door = (bay == 0) & (in_bay > 0.10) & (in_bay < 0.40) & (y < 2.7) & (y > 0.28)
    img[door] *= 0.8
    save(img, "Minato_Skyline_Shopfront.png")


if __name__ == "__main__":
    curtain()
    ribbon()
    shopfront()

"""Shiosai TUNA PORT - PBR surface set for HDRP/Lit (albedo sRGB, normal, mask).

    python tools/blender/make_shiosai_tunaport_pbr.py

Writes Assets/Environment/ShiosaiCoast/Textures/TunaPort/TP_<Stem>_{Albedo,Normal,Mask}.png.
Mask = HDRP layout: R metallic, G ambient occlusion, B detail mask, A smoothness.
Every map tiles seamlessly (all noise is periodic). Numpy + Pillow + scipy.
"""
import os

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "ShiosaiCoast", "Textures", "TunaPort")
os.makedirs(OUT, exist_ok=True)
N = 1024
rng = np.random.default_rng(20260927)
yy, xx = np.mgrid[0:N, 0:N] / N          # 0..1, v then u


def pnoise(freq, octaves=4, seed=0, persistence=0.5):
    """Periodic fractal value noise in [0,1] (bicubic-upsampled random lattices)."""
    r = np.random.default_rng(seed)
    acc = np.zeros((N, N))
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        f = int(freq * (2 ** o))
        g = r.random((f, f))
        g = np.tile(g, (3, 3))
        up = ndimage.zoom(g, N * 3 / (f * 3), order=3, mode="grid-wrap")[:N * 3, :N * 3]
        acc += amp * up[N:2 * N, N:2 * N]
        tot += amp
        amp *= persistence
    acc /= tot
    return (acc - acc.min()) / (acc.max() - acc.min() + 1e-9)


def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.dstack([-gx, gy, np.ones_like(h)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save(stem, albedo, height, strength, smooth, ao=None, metal=0.0):
    a = np.clip(albedo, 0, 1)
    Image.fromarray((a * 255).astype(np.uint8)).save(os.path.join(OUT, f"TP_{stem}_Albedo.png"))
    Image.fromarray(normal_from_height(height, strength), "RGB").save(os.path.join(OUT, f"TP_{stem}_Normal.png"))
    if ao is None:
        ao = np.ones((N, N))
    m = np.dstack([np.full((N, N), metal), np.clip(np.broadcast_to(ao, (N, N)), 0, 1), np.zeros((N, N)),
                   np.clip(np.broadcast_to(smooth, (N, N)), 0, 1)])
    Image.fromarray((m * 255).astype(np.uint8)).save(os.path.join(OUT, f"TP_{stem}_Mask.png"))
    print("wrote", stem)


def rgb(c):
    return np.array(c, dtype=float) / 255.0


# ------------------------------------------------------------------ plaster (shikkui), 3 m tile
def plaster():
    base = rgb((226, 222, 211))
    blot = pnoise(4, 5, 1)
    fine = pnoise(64, 3, 2)
    streak = pnoise(40, 2, 3)
    streak = ndimage.gaussian_filter(streak, (40, 1), mode="wrap")
    col = np.ones((N, N, 3)) * base
    col *= (0.9 + 0.12 * blot)[..., None]
    col *= (0.96 + 0.06 * fine)[..., None]
    # rain streaks from the top and grime rising from the ground (v=0 is the wall foot)
    v = 1.0 - yy
    grime = np.clip(1 - v / 0.14, 0, 1) ** 1.5
    col *= (1 - 0.35 * grime)[..., None]
    col *= (1 - 0.10 * np.clip(streak - 0.5, 0, 1) * 2)[..., None]
    col = col * (1 - 0.15 * grime[..., None]) + rgb((120, 112, 96)) * 0.15 * grime[..., None]
    h = 0.6 * fine + 0.4 * pnoise(160, 2, 4)
    save("Plaster", col, h, 3.0, 0.12 + 0.05 * fine, ao=1 - 0.25 * grime)


# ------------------------------------------------------------------ yakisugi / weathered cedar boards, 1.8 m tile
def cedar(stem, dark):
    boards = 10
    u = xx * boards
    idx = np.floor(u).astype(int)
    fu = u - idx
    shade = np.random.default_rng(5 if dark else 6).random(boards)[idx]
    grain = pnoise(8, 4, 7 if dark else 8)
    grain = ndimage.gaussian_filter(grain, (6, 0.5), mode="wrap")
    lines = 0.5 + 0.5 * np.sin((xx * 300 + grain * 18) * np.pi)
    if dark:
        base = rgb((44, 38, 34)) * (0.8 + 0.4 * shade[..., None])
        base = base * (0.85 + 0.2 * lines[..., None])
        cracks = (pnoise(90, 2, 9) > 0.8) * 0.4
        base *= (1 - cracks[..., None])
    else:
        base = rgb((134, 118, 98)) * (0.78 + 0.35 * shade[..., None])
        base = base * (0.88 + 0.16 * lines[..., None])
        silver = pnoise(6, 3, 10)
        base = base * (1 - 0.35 * silver[..., None]) + rgb((150, 150, 146)) * 0.35 * silver[..., None]
    gap = (fu < 0.035) | (fu > 0.965)
    base[gap] *= 0.25
    # horizontal battens every third of the tile
    bat = ((yy % (1 / 3)) < 0.035)
    base[bat] = base[bat] * 0.55
    h = 0.5 * lines + 0.3 * grain
    h[gap] = 0.0
    h = np.where(bat, 1.0, h)
    ao = np.where(gap, 0.4, 1.0) * np.where(bat, 0.8, 1.0)
    save(stem, base, h, 6.0, 0.08 if dark else 0.14, ao=ao)


# ------------------------------------------------------------------ ibushi kawara (smoked clay), 1.8 m tile, 6x6 tiles
def kawara():
    cols, rows = 6, 6
    u = xx * cols
    v = (1 - yy) * rows                    # v=0 at the eave
    cu = u - np.floor(u)
    rv = v - np.floor(v)
    tid = (np.floor(u).astype(int) * 31 + np.floor(v).astype(int) * 17) % 97
    tone = np.random.default_rng(12).random(97)[tid]
    # pantile S-profile across, each course overlapping the one below
    prof = np.sin(cu * np.pi * 2 - np.pi / 2) * 0.5 + 0.5
    overlap = np.clip(rv / 0.12, 0, 1)
    h = prof * 0.8 * overlap + (1 - rv) * 0.25
    base = rgb((96, 100, 108)) * (0.78 + 0.32 * tone[..., None])
    base *= (0.72 + 0.4 * prof[..., None])
    base[rv < 0.05] *= 0.35                # the dark shadow line under each course
    lichen = np.clip(pnoise(5, 4, 13) - 0.62, 0, 1) * 2.4
    base = base * (1 - 0.4 * lichen[..., None]) + rgb((128, 132, 110)) * 0.4 * lichen[..., None]
    ao = (0.55 + 0.45 * prof) * np.where(rv < 0.08, 0.5, 1.0)
    save("Kawara", base, h, 10.0, 0.42 * (0.7 + 0.3 * prof) * (1 - 0.6 * lichen), ao=ao, metal=0.05)


# ------------------------------------------------------------------ ishigaki / cut stone base, 2.4 m tile
def stone():
    r = np.random.default_rng(14)
    h = np.zeros((N, N))
    col = np.zeros((N, N, 3))
    rows = 8
    rh = N // rows
    for row in range(rows):
        x = int(r.integers(0, 60))
        while x < N + 200:
            w = int(r.integers(90, 190))
            y0, y1 = row * rh, (row + 1) * rh
            xs = np.arange(x, x + w) % N
            block = np.zeros((rh, w))
            by, bx = np.mgrid[0:rh, 0:w]
            edge = np.minimum(np.minimum(bx, w - 1 - bx), np.minimum(by, rh - 1 - by))
            block = np.clip(edge / 14.0, 0, 1) ** 0.6
            tone = 0.75 + 0.35 * r.random()
            h[y0:y1][:, xs] = block * (0.8 + 0.2 * r.random())
            c = rgb((122, 122, 118)) * tone
            col[y0:y1][:, xs] = c
            x += w
    fine = pnoise(48, 4, 15)
    h = h * (0.85 + 0.15 * fine)
    col *= (0.8 + 0.3 * fine)[..., None]
    moss = np.clip(pnoise(6, 4, 16) - 0.55, 0, 1) * 2
    col = col * (1 - 0.5 * moss[..., None]) + rgb((64, 78, 50)) * 0.5 * moss[..., None]
    col[h < 0.05] *= 0.3
    save("Stone", col, h, 12.0, 0.1 + 0.08 * fine, ao=0.45 + 0.55 * np.clip(h * 1.6, 0, 1))


# ------------------------------------------------------------------ stained quay concrete, 4 m tile
def concrete(stem, tint, wet):
    blot = pnoise(3, 5, 20)
    fine = pnoise(96, 3, 21)
    agg = (pnoise(220, 1, 22) > 0.72) * 0.08
    col = np.ones((N, N, 3)) * rgb(tint)
    col *= (0.82 + 0.22 * blot)[..., None] * (0.95 + 0.07 * fine)[..., None]
    col -= agg[..., None]
    rust = np.clip(pnoise(7, 4, 23) - 0.7, 0, 1) * 2.5
    col = col * (1 - 0.3 * rust[..., None]) + rgb((120, 78, 46)) * 0.3 * rust[..., None]
    joint = (xx % 0.5 < 0.004) | (yy % 0.5 < 0.004)
    col[joint] *= 0.45
    h = 0.5 * fine + 0.3 * blot
    h[joint] = 0
    smooth = (0.62 * (0.6 + 0.4 * blot) if wet else 0.18 + 0.06 * fine)
    save(stem, col, h, 3.0, smooth, ao=np.where(joint, 0.6, 1.0))


# ------------------------------------------------------------------ painted corrugated steel, 1.6 m tile
def corrugated(stem, paint, rusty=1.0):
    ripple = np.sin(xx * 2 * np.pi * 21) * 0.5 + 0.5
    rust = ndimage.gaussian_filter(np.clip(pnoise(6, 5, 30) - 0.70, 0, 1) * 1.6 * rusty, 3, mode="wrap")
    streak = ndimage.gaussian_filter(pnoise(30, 2, 31), (30, 1), mode="wrap")
    col = np.ones((N, N, 3)) * rgb(paint)
    col *= (0.78 + 0.3 * ripple)[..., None]
    col *= (0.92 + 0.12 * streak)[..., None]
    col = col * (1 - 0.6 * rust[..., None]) + rgb((126, 70, 40)) * 0.6 * rust[..., None]
    save(stem, col, ripple, 5.0, 0.45 * (1 - rust), ao=0.75 + 0.25 * ripple, metal=0.35)


# ------------------------------------------------------------------ modern lap siding, 3 m tile
def siding():
    boards = 16
    v = (1 - yy) * boards
    fv = v - np.floor(v)
    tone = np.random.default_rng(40).random(boards)[np.floor(v).astype(int) % boards]
    col = np.ones((N, N, 3)) * rgb((214, 210, 198))
    col *= (0.93 + 0.08 * tone)[..., None]
    col *= (0.86 + 0.14 * fv)[..., None]
    grime = np.clip(1 - (1 - yy) / 0.12, 0, 1)
    col *= (1 - 0.25 * grime)[..., None]
    h = fv
    save("Siding", col, h, 6.0, 0.28, ao=0.7 + 0.3 * fv)


# ------------------------------------------------------------------ weathered planks (pier deck, tables, pallets), 2 m tile
def planks():
    boards = 8
    u = xx * boards
    fu = u - np.floor(u)
    idx = np.floor(u).astype(int)
    tone = np.random.default_rng(50).random(boards)[idx]
    grain = ndimage.gaussian_filter(pnoise(10, 4, 51), (8, 0.6), mode="wrap")
    col = rgb((150, 128, 100)) * (0.75 + 0.35 * tone[..., None])
    col *= (0.85 + 0.2 * grain[..., None])
    col[(fu < 0.03) | (fu > 0.97)] *= 0.3
    h = grain * 0.6 + 0.4
    h[(fu < 0.03) | (fu > 0.97)] = 0
    save("Planks", col, h, 5.0, 0.16)


if __name__ == "__main__":
    plaster()
    cedar("Cedar", dark=False)
    cedar("Yakisugi", dark=True)
    kawara()
    stone()
    concrete("Concrete", (160, 160, 156), wet=False)
    concrete("WetConcrete", (118, 120, 122), wet=True)
    corrugated("RoofMetal", (120, 148, 170))
    corrugated("WallMetal", (196, 200, 204), rusty=0.35)
    siding()
    planks()

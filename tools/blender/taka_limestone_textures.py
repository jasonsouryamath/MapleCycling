"""Taka (Mt. Ventoux) limestone scree ground maps. numpy + PIL only, tileable.
python tools/blender/taka_limestone_textures.py ->
  Assets/Environment/TakaMountains/Textures/Taka_Limestone_Albedo.png / Taka_Limestone_Normal.png

Broken limestone scree, the bald top of Ventoux: angular shards of buff / warm-grey stone with
dark joints, a spread of darker weathered stones, darker scree STREAKS (elongated bands of
finer, darker rubble), low-frequency blotches and a few dark pebble / lichen specks.

2026-09-26 copilot session 3 (T3): the first version was tone ~0.80 off-white with an almost
neutral cast and read as SNOW under the region's sky-lit shade. This one sits at a mean of
~0.56 with a buff cast (R > G > B), so even with the ground shader's ambient lift it lands as
pale warm stone rather than a snowfield. Everything is built on the torus (periodic Voronoi,
FFT-filtered noise) so it tiles."""
import os
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Environment', 'TakaMountains', 'Textures')
N = 1024
rng = np.random.default_rng(842)
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)


def voronoi(cell, seed):
    r = np.random.default_rng(seed)
    g = N // cell
    jit = r.random((g, g, 2))
    tone = r.random((g, g))
    gx, gy = (xx // cell).astype(int), (yy // cell).astype(int)
    f1 = np.full((N, N), 1e9, np.float32); f2 = np.full((N, N), 1e9, np.float32)
    idt = np.zeros((N, N), np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            cx, cy = gx + dx, gy + dy
            wx, wy = cx % g, cy % g
            px = (cx + jit[wy, wx, 0]) * cell; py = (cy + jit[wy, wx, 1]) * cell
            # Chebyshev-ish blend -> angular shards rather than round cells
            ddx, ddy = np.abs(xx - px), np.abs(yy - py)
            d = 0.55 * np.hypot(ddx, ddy) + 0.45 * np.maximum(ddx, ddy)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            idt = np.where(closer, tone[wy, wx], idt)
            f1 = np.where(closer, d, f1)
    return f1, f2, idt


def fnoise(seed, sx, sy=None):
    """Periodic gaussian-filtered noise, normalised to -1..1. sx/sy = filter width in cycles."""
    sy = sx if sy is None else sy
    r = np.random.default_rng(seed)
    w = r.normal(0, 1, (N, N))
    fx = np.fft.fftfreq(N)[None, :] * N
    fy = np.fft.fftfreq(N)[:, None] * N
    filt = np.exp(-(fx / sx) ** 2 - (fy / sy) ** 2)
    n = np.real(np.fft.ifft2(np.fft.fft2(w) * filt))
    n -= n.mean()
    return n / (np.abs(n).max() + 1e-9)


# large shards and small chips
f1a, f2a, ta = voronoi(32, 1)
f1b, f2b, tb = voronoi(12, 2)
edge_a = np.clip((f2a - f1a) / 5.0, 0, 1)
edge_b = np.clip((f2b - f1b) / 3.0, 0, 1)

# stone tone per shard: mostly buff, ~22% noticeably darker weathered stones
dark_stone = (ta > 0.78).astype(np.float32)
tone = 0.72 + 0.12 * ta - 0.06 * tb - 0.17 * dark_stone
# shading inside each shard: joints dark, faces lit, a slight dome
# joints of the big shards are softened where the small chips take over (size mix)
mix = np.clip(0.5 + 0.8 * fnoise(13, 5.0), 0, 1)
lum = tone * (0.60 + 0.40 * (edge_a * mix + edge_b * (1 - mix))) * (0.93 + 0.07 * edge_b)

# low-frequency blotches (a few per tile) and darker scree STREAKS: noise stretched along one
# axis, thresholded into bands of darker, finer rubble
blotch = fnoise(11, 3.0)
streak = fnoise(12, 5.0, 24.0)            # elongated along x, narrow along y
streak_mask = np.clip((streak - 0.12) / 0.30, 0, 1)
lum *= 0.90 + 0.12 * blotch
lum *= 1.0 - 0.26 * streak_mask
# inside the streaks the rubble is finer: blend towards the small-chip pattern
lum = lum * (1 - 0.35 * streak_mask) + (0.46 + 0.10 * tb) * (0.6 + 0.4 * edge_b) * 0.35 * streak_mask

# Broad, fractured bedding planes break up the otherwise even pebble field. Integer
# frequencies and periodic noise keep the laminae seamless at tile boundaries.
warp = fnoise(21, 8.0) * 0.045 + fnoise(22, 21.0) * 0.012
bedding_phase = 2.0 * np.pi * (4.0 * yy / N + 2.0 * xx / N + warp)
bedding = np.maximum(0.0, np.cos(bedding_phase)) ** 14
fractures = np.clip(0.65 + 0.7 * fnoise(23, 11.0), 0, 1)
bedding *= fractures
lum *= 1.0 - 0.15 * bedding

# dark pebbles / lichen specks
speck = rng.random((N, N)) < 0.0012
speck = speck | np.roll(speck, 1, 0) | np.roll(speck, 1, 1)
lum = np.where(speck, lum * 0.70, lum)
lum += rng.normal(0, 0.018, (N, N))
lum = np.clip(lum, 0, 1)

# warm buff cast, a little stronger in the streaks (iron-stained fines) and on dark stones
warm = 1.0 + 0.04 * streak_mask + 0.03 * dark_stone
r = lum * 1.00 * warm
g = lum * 0.945
b = lum * 0.855 / warm
alb = (np.clip(np.stack([r, g, b], -1), 0, 1) * 255).astype(np.uint8)
Image.fromarray(alb).save(os.path.join(OUT, 'Taka_Limestone_Albedo.png'))

# height: each shard is a low dome, joints are low, streaks are lower (finer rubble)
h = 0.7 * edge_a + 0.3 * edge_b + 0.15 * ta - 0.25 * streak_mask - 0.13 * bedding
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 2.2
gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 2.2
nz = np.ones_like(h)
L = np.sqrt(gx * gx + gy * gy + nz)
nrm = np.stack([-gx / L, gy / L, nz / L], -1) * 0.5 + 0.5
Image.fromarray((nrm * 255).astype(np.uint8)).save(os.path.join(OUT, 'Taka_Limestone_Normal.png'))
print('ok', alb.mean(axis=(0, 1)) / 255.0)

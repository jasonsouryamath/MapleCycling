"""Procedural photographic cloud map for the Nagisa Bay HDRP CloudLayer (2026-10-02).

UPPER-HEMISPHERE layout (HDRP CloudLayer with upperHemisphereOnly=true): the WHOLE texture spans
elevation 90 deg (row 0, zenith) to 0 deg (last row, horizon) and 360 deg of longitude. Noise is
evaluated on the unit sphere (no pole pinch, seamless in longitude). A full-sphere layout leaves the
bottom half empty and the clouds vanish.
Channels (HDRP picks them per layer with opacityR/G/B/A):
  R  fair-weather cumulus: cauliflower tops, soft cores, darker/denser interiors
  G  cirrus / altostratus streaks: long thin combed filaments
  B  scattered small cumulus humilis (low layer)
  A  unused (0)
Run: python tools/art/make_nagisa_cloudmap.py [width]
"""
import os, sys
import numpy as np
from PIL import Image

W = int(sys.argv[1]) if len(sys.argv) > 1 else 4096
H = W // 2
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Environment", "NagisaBay", "Textures", "Nagisa_CloudMap.png")

rng = np.random.default_rng(20261002)
PERM = rng.permutation(256).astype(np.int32); PERM = np.concatenate([PERM, PERM])
GRAD = rng.normal(size=(256, 3)); GRAD /= np.linalg.norm(GRAD, axis=1, keepdims=True)

def fade(t): return t * t * t * (t * (t * 6 - 15) + 10)

def gnoise(p):
    """3D gradient noise, p: (N,3) float -> (N,) in about [-1,1]."""
    i = np.floor(p).astype(np.int32); f = p - i; i &= 255
    u, v, w = fade(f[:, 0]), fade(f[:, 1]), fade(f[:, 2])
    def g(dx, dy, dz):
        h = PERM[PERM[PERM[i[:, 0] + dx] + i[:, 1] + dy] + i[:, 2] + dz]
        return (GRAD[h] * (f - np.array([dx, dy, dz]))).sum(1)
    x00 = g(0,0,0) + u * (g(1,0,0) - g(0,0,0)); x10 = g(0,1,0) + u * (g(1,1,0) - g(0,1,0))
    x01 = g(0,0,1) + u * (g(1,0,1) - g(0,0,1)); x11 = g(0,1,1) + u * (g(1,1,1) - g(0,1,1))
    y0 = x00 + v * (x10 - x00); y1 = x01 + v * (x11 - x01)
    return (y0 + w * (y1 - y0)) * 1.7

ROT = np.array([[0.8, -0.6, 0.0], [0.6, 0.8, 0.0], [0.0, 0.0, 1.0]]) @ \
      np.array([[1, 0, 0], [0, 0.9, -0.43], [0, 0.43, 0.9]])

def fbm(p, octaves, lac=2.07, gain=0.5, billow=False):
    a, s, q = 1.0, 0.0, p.copy(); norm = 0.0
    for _ in range(octaves):
        n = gnoise(q)
        if billow: n = 1.0 - 2.0 * np.abs(n)          # puffy ridges: cauliflower tops
        s += a * n; norm += a; a *= gain; q = (q @ ROT.T) * lac + 11.7
    return s / norm

def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)

def directions(y0, y1):
    ys = (np.arange(y0, y1) + 0.5) / H; xs = (np.arange(W) + 0.5) / W
    lon = xs * 2 * np.pi; lat = (1.0 - ys) * (np.pi / 2)      # row 0 = zenith ... last row = horizon
    LON, LAT = np.meshgrid(lon, lat)
    d = np.stack([np.cos(LAT) * np.cos(LON), np.sin(LAT), np.cos(LAT) * np.sin(LON)], -1)
    return d.reshape(-1, 3), LAT.reshape(-1)

def cumulus(d, lat, freq, coverage, seed):
    p = d * freq + seed
    warp = np.stack([fbm(p * 0.5 + k * 7.1, 2) for k in range(3)], -1) * 0.18
    mask = fbm(d * (freq * 0.30) + seed + 40, 3) * 0.5 + 0.5          # clusters / clear lanes
    mass = fbm(p * 0.8 + warp, 4) * 0.5 + 0.5                         # rounded masses
    base = mass * 0.55 + mask * 0.60
    thr = 1.0 - coverage
    body = smooth(thr - 0.015, thr + 0.10, base)                      # firm, rounded silhouette
    # cauliflower: billowy erosion concentrated on the edge, so tops look lumpy not stringy
    bil = fbm(p * 2.6 + 33, 5, billow=True) * 0.5 + 0.5
    rim = body * (1 - body) * 4.0
    shape = np.clip(body - rim * (1 - bil) * 0.55, 0, 1)
    shape = smooth(0.08, 0.62, shape)
    core = fbm(p * 1.3 + 5, 4) * 0.5 + 0.5
    # denser, brighter-reading cores with softer thin fringes
    return shape * (0.50 + 0.50 * smooth(0.25, 0.8, core * 0.6 + body * 0.5))

def cirrus(d, lat, seed):
    # long combed filaments: stretch the noise along one great-circle direction
    ax = np.array([0.36, 0.0, 0.93]); ax /= np.linalg.norm(ax)
    along = d @ ax
    perp = d - along[:, None] * ax
    p = perp * 9.0 + along[:, None] * 1.1 + seed
    streak = fbm(p, 6) * 0.5 + 0.5
    mask = fbm(d * 1.5 + seed + 20, 3) * 0.5 + 0.5
    return smooth(0.50, 0.88, streak * 0.8 + mask * 0.42 - 0.12) * 0.55

def build():
    out = np.zeros((H, W, 4), np.float32)
    CH = 128
    for y0 in range(0, H, CH):
        y1 = min(H, y0 + CH)
        d, lat = directions(y0, y1)
        r = cumulus(d, lat, 4.4, 0.43, 3.0)
        g = cirrus(d, lat, 7.0)
        b = cumulus(d, lat, 9.0, 0.33, 17.0) * 0.8
        # Horizon rows: thin the layers a little so they dissolve into haze instead of a hard wall.
        horizon = smooth(0.0, 0.20, lat)
        sl = (slice(y0, y1), slice(None))
        out[sl + (0,)] = (r * horizon).reshape(y1 - y0, W)
        out[sl + (1,)] = (g * np.clip(horizon * 1.2, 0, 1)).reshape(y1 - y0, W)
        out[sl + (2,)] = (b * horizon).reshape(y1 - y0, W)
        print(f"rows {y1}/{H}", end="\r")
    return out

if __name__ == "__main__":
    m = build()
    img = (np.clip(m, 0, 1) * 255 + 0.5).astype(np.uint8); img[..., 3] = 0
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    Image.fromarray(img, "RGBA").save(OUT, optimize=True)
    print("\nwrote", os.path.normpath(OUT), W, H, "coverage R/G/B:", [round(float((m[..., i] > 0.15).mean()), 3) for i in range(3)])
    # Preview: composite over a sky gradient so the result can be judged by eye.
    up = m
    t = np.linspace(0, 1, up.shape[0])[:, None, None]
    sky = (1 - t) * np.array([0.22, 0.45, 0.85]) + t * np.array([0.72, 0.85, 0.95])
    a = np.clip(up[..., 0:1] + up[..., 1:2] * 0.7 + up[..., 2:3], 0, 1)
    prev = sky * (1 - a) + np.array([1, 1, 1]) * a * (0.82 + 0.18 * up[..., 0:1])
    Image.fromarray((np.clip(prev, 0, 1) * 255).astype(np.uint8)).resize((1600, 800)).save(os.path.join(os.path.dirname(OUT), "..", "..", "..", "..", "tools", "art", "nagisa_cloudmap_preview.png"))

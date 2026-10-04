"""Shared helpers for the Shunta Metro texture generators (numpy + PIL only).

All noise is periodic (FFT-filtered white noise) so every map tiles seamlessly.
Frequencies are in cycles per pixel (0.5 = Nyquist); at 227 px/m, 0.0044 c/px = 1 cycle per metre.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

DEFAULT_OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "Assets", "Textures", "ShuntaMetro"))


def out_dir():
    d = os.environ.get("SHUNTA_TEX_OUT", DEFAULT_OUT)
    os.makedirs(d, exist_ok=True)
    return d


def fbm(h, w, seed, beta=2.0, fmin=0.0, fmax=0.5):
    """Periodic band-limited noise, mean 0, std 1. Amplitude spectrum ~ f^(-beta/2)."""
    r = np.random.default_rng(seed)
    n = r.standard_normal((h, w)).astype(np.float32)
    F = np.fft.rfft2(n)
    fy = np.fft.fftfreq(h).astype(np.float32)[:, None]
    fx = np.fft.rfftfreq(w).astype(np.float32)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    filt = f ** (-beta / 2.0)
    if fmin > 0:
        filt *= 1.0 / (1.0 + (fmin / f) ** 4)
    if fmax < 0.5:
        filt *= 1.0 / (1.0 + (f / fmax) ** 4)
    filt[0, 0] = 0.0
    out = np.fft.irfft2(F * filt, s=(h, w)).astype(np.float32)
    out -= out.mean()
    out /= (out.std() + 1e-8)
    return out


def fbm1d(n, seed, beta=2.0, fmin=0.0, fmax=0.5):
    r = np.random.default_rng(seed)
    x = r.standard_normal(n).astype(np.float32)
    F = np.fft.rfft(x)
    f = np.fft.rfftfreq(n).astype(np.float32)
    f[0] = 1.0
    filt = f ** (-beta / 2.0)
    if fmin > 0:
        filt *= 1.0 / (1.0 + (fmin / f) ** 4)
    if fmax < 0.5:
        filt *= 1.0 / (1.0 + (f / fmax) ** 4)
    filt[0] = 0
    o = np.fft.irfft(F * filt, n=n).astype(np.float32)
    o -= o.mean()
    o /= (o.std() + 1e-8)
    return o


def lowpass(a, sigma_px):
    """Periodic gaussian blur by FFT (2D array)."""
    h, w = a.shape
    F = np.fft.rfft2(a)
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    g = np.exp(-2.0 * (np.pi ** 2) * (sigma_px ** 2) * (fx * fx + fy * fy)).astype(np.float32)
    return np.fft.irfft2(F * g, s=(h, w)).astype(np.float32)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-9), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    return a + (b - a) * t


def height_to_normal(hm, slope_std=0.3, flat=None):
    """Tangent-space normal (OpenGL / Unity convention) from a periodic height field.
    slope_std: target standard deviation of the slope. flat: optional 0..1 mask that flattens the normal."""
    dx = (np.roll(hm, -1, 1) - np.roll(hm, 1, 1)) * 0.5
    dy = (np.roll(hm, -1, 0) - np.roll(hm, 1, 0)) * 0.5
    k = slope_std / (np.sqrt(0.5 * (dx.std() ** 2 + dy.std() ** 2)) + 1e-8)
    nx = -dx * k
    ny = dy * k
    if flat is not None:
        nx = nx * (1 - flat)
        ny = ny * (1 - flat)
    nz = np.ones_like(nx)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    out = np.stack([nx * inv * 0.5 + 0.5, ny * inv * 0.5 + 0.5, nz * inv * 0.5 + 0.5], -1)
    return out


def to8(a):
    return np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8)


def save_png(path, arr8):
    mode = {2: "L", 3: "RGB", 4: "RGBA"}
    a = arr8
    if a.ndim == 3 and a.shape[2] == 1:
        a = a[:, :, 0]
    im = Image.fromarray(a)
    im.save(path, compress_level=4)
    print("wrote", os.path.basename(path), im.size, "%.1f MB" % (os.path.getsize(path) / 1e6), flush=True)


def wrap_lines(w, h, polylines, width):
    """Draw polylines onto a periodic canvas (wraps at the edges). Returns float 0..1 (h, w)."""
    im = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(im)
    for pl in polylines:
        for ox in (-w, 0, w):
            for oy in (-h, 0, h):
                d.line([(x + ox, y + oy) for x, y in pl], fill=255, width=width)
    return np.asarray(im, dtype=np.float32) / 255.0


def random_crack(rng, w, h, steps, step_px, wander=0.45):
    x, y = rng.uniform(0, w), rng.uniform(0, h)
    a = rng.uniform(0, 2 * np.pi)
    pts = [(x, y)]
    for _ in range(steps):
        a += rng.normal(0, wander)
        x += np.cos(a) * step_px * rng.uniform(0.6, 1.4)
        y += np.sin(a) * step_px * rng.uniform(0.6, 1.4)
        pts.append((x, y))
    return pts


def font(name, size):
    base = r"C:\Windows\Fonts"
    for f in ([name] if name else []) + ["YuGothB.ttc", "meiryob.ttc", "meiryo.ttc", "msgothic.ttc"]:
        p = os.path.join(base, f)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()

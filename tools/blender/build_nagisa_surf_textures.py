"""NB1 - generates the tileable ocean detail textures for MapleRide/HDRP/NagisaOcean (no Blender needed).

Writes to Assets/Environment/NagisaBay/Textures/Surf/:
  NagisaSurf_NormalA.png  512 px tileable water normal map (raw RG = xy, B = z; sample as linear, not sRGB)
  NagisaSurf_NormalB.png  512 px, different seed + spectrum, used at an irrational scale / rotation
  NagisaSurf_Foam.png     512 px tileable lace-foam mask (greyscale, linear)
All three are built from random-phase band-limited noise in the Fourier domain, so they tile exactly.
Run:  python tools/blender/build_nagisa_surf_textures.py
"""
import os
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Environment", "NagisaBay", "Textures", "Surf")
N = 512


def band_noise(seed, f_lo, f_hi, slope):
    rng = np.random.default_rng(seed)
    fx = np.fft.fftfreq(N)[None, :] * N
    fy = np.fft.fftfreq(N)[:, None] * N
    f = np.sqrt(fx * fx + fy * fy)
    spec = np.where((f >= f_lo) & (f <= f_hi), (np.maximum(f, 1.0)) ** (-slope), 0.0)
    ph = np.exp(2j * np.pi * rng.random((N, N)))
    a = np.fft.ifft2(spec * ph).real
    a -= a.mean()
    return a / (a.std() + 1e-9)


def normal_map(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return ((n * 0.5 + 0.5) * 255.0 + 0.5).astype(np.uint8)


def save(arr, name):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(arr).save(os.path.join(OUT, name))
    print("wrote", name)


def main():
    # A: broad chop + fine ripples; B: finer, sharper, different seed.
    ha = band_noise(11, 2, 70, 1.15) + 0.6 * band_noise(12, 40, 150, 0.6)
    hb = band_noise(21, 3, 90, 1.0) + 0.8 * band_noise(22, 60, 200, 0.4)
    save(normal_map(ha, 0.55), "NagisaSurf_NormalA.png")
    save(normal_map(hb, 0.50), "NagisaSurf_NormalB.png")

    # Lace foam: thresholded ridged noise -> web-like bubbles, soft edges.
    r = np.abs(band_noise(31, 3, 40, 0.9))
    c = band_noise(32, 20, 120, 0.5)
    lace = np.clip(1.0 - r * 1.1, 0, 1) ** 2.2
    lace = np.clip(lace * (0.65 + 0.35 * np.clip(c * 0.5 + 0.5, 0, 1)), 0, 1)
    lace = (lace - lace.min()) / (lace.max() - lace.min())
    save(np.stack([(lace * 255).astype(np.uint8)] * 3, -1), "NagisaSurf_Foam.png")


if __name__ == "__main__":
    main()

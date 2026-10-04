"""Build the irregular alpha atlas used by Nagisa's grass-detail overlay."""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures")

def noise(size, cells, seed):
    rng = np.random.default_rng(seed)
    small = rng.random((cells, cells)).astype(np.float32)
    small = np.pad(small, ((0, 1), (0, 1)), mode="wrap")
    im = Image.fromarray((small * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
    return np.asarray(im, np.float32) / 255.0

def smooth(a, lo, hi):
    t = np.clip((a - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)

def make():
    tile = 512
    atlas = np.zeros((tile * 2, tile * 2, 4), np.uint8)
    yy, xx = np.mgrid[0:tile, 0:tile].astype(np.float32)
    for i in range(4):
        rng = np.random.default_rng(730 + i)
        cx = tile * (0.5 + rng.uniform(-0.08, 0.08))
        cy = tile * (0.5 + rng.uniform(-0.08, 0.08))
        ang = rng.uniform(0, np.pi)
        ca, sa = np.cos(ang), np.sin(ang)
        rx = (xx - cx) * ca + (yy - cy) * sa
        ry = -(xx - cx) * sa + (yy - cy) * ca
        theta = np.arctan2(ry, rx)
        radius = np.sqrt((rx / (tile * 0.42)) ** 2 + (ry / (tile * 0.24)) ** 2)
        lobed = 1 + 0.15 * np.sin(theta * 3 + i) + 0.08 * np.sin(theta * 7 - i)
        edge = radius / lobed + (noise(tile, 8, 810 + i) - 0.5) * 0.28
        alpha = 1 - smooth(edge, 0.62, 0.98)
        alpha[:8] = alpha[-8:] = 0
        alpha[:, :8] = alpha[:, -8:] = 0
        broad = noise(tile, 9, 900 + i)
        fine = noise(tile, 48, 950 + i)
        if i % 2:
            base = np.array([0.38, 0.47, 0.23])
            accent = np.array([0.67, 0.58, 0.27])
        else:
            base = np.array([0.46, 0.35, 0.20])
            accent = np.array([0.72, 0.66, 0.40])
        mix = smooth(fine, 0.62, 0.88)[..., None]
        rgb = base * (0.72 + broad[..., None] * 0.42)
        rgb = rgb * (1 - mix * 0.42) + accent * mix * 0.42
        r, c = divmod(i, 2)
        atlas[r * tile:(r + 1) * tile, c * tile:(c + 1) * tile, :3] = np.clip(rgb * 255, 0, 255).astype(np.uint8)
        atlas[r * tile:(r + 1) * tile, c * tile:(c + 1) * tile, 3] = np.clip(alpha * 255, 0, 255).astype(np.uint8)
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "Nagisa_Ground_Splat.png")
    Image.fromarray(atlas, "RGBA").save(path)
    print(f"[nagisa-ground] wrote {path} 1024x1024 RGBA")

if __name__ == "__main__":
    make()

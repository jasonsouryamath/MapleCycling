"""Tiling PBR textures (albedo + normal) for the Shunta city kit. blender -b --python sm_textures.py"""
import os
import numpy as np
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Models", "ShuntaMetro", "Textures")
os.makedirs(OUT, exist_ok=True)
N = 512
rng = np.random.default_rng(7)


def noise(scale):
    s = max(2, N // scale)
    a = rng.random((s, s))
    t = np.kron(a, np.ones((scale, scale)))[:N, :N]
    # cheap smoothing (wrap) to keep tiling
    for _ in range(2):
        t = (t + np.roll(t, 1, 0) + np.roll(t, -1, 0) + np.roll(t, 1, 1) + np.roll(t, -1, 1)) / 5
    return t


def normal_from_height(h, k=3.0):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * k
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * k
    n = np.stack([-dx, -dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def save(name, rgb):
    img = bpy.data.images.new(name, N, N, alpha=True)
    a = np.ones((N, N, 4), dtype=np.float32)
    a[..., :3] = np.clip(rgb, 0, 1)
    img.pixels = a[::-1].flatten().tolist()
    img.filepath_raw = os.path.join(OUT, name + ".png")
    img.file_format = "PNG"
    img.colorspace_settings.name = "sRGB"
    img.save()


# concrete: grime + pores + panel seams
h = noise(32) * 0.5 + noise(8) * 0.3 + noise(2) * 0.2
seam = np.zeros((N, N))
seam[::128, :] = 1
seam[:, ::128] = 1
h2 = h - seam * 0.5
g = 0.78 + (noise(16) - 0.5) * 0.35 + (noise(2) - 0.5) * 0.15
streak = np.clip(noise(64)[:, :1] * np.ones((1, N)), 0, 1)
g = g - (streak - 0.4) * 0.15 - seam * 0.25
save("concrete_albedo", np.stack([g, g, g * 1.02], -1))
save("concrete_normal", normal_from_height(h2, 6))

# tile: 32 px tiles with grout lines
t = np.zeros((N, N))
t[::32, :] = 1
t[:, ::32] = 1
t = np.clip(t + np.roll(t, 1, 0) + np.roll(t, 1, 1), 0, 1)
v = 0.9 + (noise(32) - 0.5) * 0.2
a = np.where(t > 0.5, 0.45, v)
save("tile_albedo", np.stack([a, a, a], -1))
save("tile_normal", normal_from_height(-t * 0.8 + noise(4) * 0.05, 5))

# corrugated metal
x = np.arange(N)
ridge = 0.5 + 0.5 * np.sin(x / 16.0 * 2 * np.pi)
hm = np.tile(ridge[None, :], (N, 1)) * 0.6 + noise(8) * 0.1
m = 0.65 + (noise(16) - 0.5) * 0.25 - (hm * 0.1)
save("metal_albedo", np.stack([m, m, m * 1.05], -1))
save("metal_normal", normal_from_height(hm, 4))
print("[sm-tex] done")

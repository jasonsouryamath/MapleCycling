"""
Clean 3D-position repaint of the projected atlas for a generated rider (system Python + numpy + Pillow).
  python tools/blender/paint_generated.py <tris.npz> <projected_atlas.png> <out_atlas.png> [--analyze]
The generated mesh follows the concept sheet's proportions in the rig frame (sole z=-0.034), so the sheet's heights apply directly.
Skin / face / hair / helmet keep the projected pixels; jersey, shorts, sleeves, socks, gloves and shoes are repainted clean.
"""
import sys
import os
import numpy as np
from PIL import Image

tris_file, atlas_in, out = sys.argv[1], sys.argv[2], sys.argv[3]
ANALYZE = '--analyze' in sys.argv
base = np.asarray(Image.open(atlas_in).convert('RGB'), np.float32)
H, W, _ = base.shape
cache = os.path.splitext(tris_file)[0] + '_posmap.npz'
if os.path.exists(cache):
    c = np.load(cache)
    P, valid = c['P'], c['valid']
else:
    d = np.load(tris_file)
    UV, POS = d['uv'], d['pos']
    P = np.zeros((H, W, 3), np.float32)
    valid = np.zeros((H, W), bool)
    for t in range(len(UV)):
        uv = UV[t]
        px, py = uv[:, 0] * W, (1 - uv[:, 1]) * H
        x0, x1 = int(max(np.floor(px.min()), 0)), int(min(np.ceil(px.max()), W - 1))
        y0, y1 = int(max(np.floor(py.min()), 0)), int(min(np.ceil(py.max()), H - 1))
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        den = (py[1] - py[2]) * (px[0] - px[2]) + (px[2] - px[1]) * (py[0] - py[2])
        if abs(den) < 1e-9:
            continue
        l0 = ((py[1] - py[2]) * (xs - px[2]) + (px[2] - px[1]) * (ys - py[2])) / den
        l1 = ((py[2] - py[0]) * (xs - px[2]) + (px[0] - px[2]) * (ys - py[2])) / den
        l2 = 1 - l0 - l1
        ins = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not ins.any():
            continue
        pos = l0[..., None] * POS[t, 0] + l1[..., None] * POS[t, 1] + l2[..., None] * POS[t, 2]
        sl = (slice(y0, y1 + 1), slice(x0, x1 + 1))
        P[sl][ins] = pos[ins]
        valid[sl] |= ins
    np.savez_compressed(cache, P=P, valid=valid)
X, Y, Z = P[..., 0], P[..., 1], P[..., 2]
CX = 0.011
AX = np.abs(X - CX)
print('texels', int(valid.sum()))

pal = np.array([(16, 16, 20), (232, 100, 22), (236, 228, 214), (246, 246, 246)], np.float32)
dist = np.sqrt(((base[:, :, None, :] - pal[None, None]) ** 2).sum(3))
cls = dist.argmin(2)
near = dist.min(2) < np.array([60, 90, 60, 50])[cls]

if ANALYZE:
    def hist(name, m, lo, hi, n=28):
        v = Z[m & valid]
        h, _ = np.histogram(v, bins=n, range=(lo, hi))
        print(name, ' '.join(f'{int(x):4d}' for x in h))
    body = valid & (AX < 0.2) & (Z > 0.0)
    print('z bins of 0.05 from -0.05 to 1.35')
    for nm, k in (('black', 0), ('orange', 1), ('cream', 2), ('white', 3)):
        hist(nm, body & near & (cls == k) & (AX > 0.02), -0.05, 1.35)
    sys.exit(0)


def sm(x, w=0.002):
    return np.clip(0.5 + x / w, 0, 1)


def band(v, lo, hi, w=0.002):
    return np.minimum(sm(v - lo, w), sm(hi - v, w))


BLACK = np.array((18, 18, 22), np.float32)
ORANGE = np.array((232, 100, 22), np.float32)
CREAM = np.array((238, 230, 215), np.float32)
WHITE = np.array((246, 246, 246), np.float32)
SKIN = np.array((120, 72, 46), np.float32)
img = base.copy()


def paint(mask, color):
    m = (mask * valid)[..., None].astype(np.float32)
    img[:] = img * (1 - m) + np.array(color, np.float32) * m


# zones in the rig frame (sheet heights); tweakable
Z_SOCK_LO, Z_SOCK_HI = 0.085, 0.150
Z_SHORTS_HEM, Z_JERSEY_HEM, Z_SLEEVE_HEM, Z_NECK = 0.205, 0.412, 0.505, 0.70
# per-leg centring
lm = valid & (Z > 0.12) & (Z < 0.20) & (AX < 0.25)
lx = X[lm]
c1, c2 = np.percentile(lx, 25), np.percentile(lx, 75)
for _ in range(12):
    a1 = np.abs(lx - c1) < np.abs(lx - c2)
    c1, c2 = lx[a1].mean(), lx[~a1].mean()
lo_c, hi_c = min(c1, c2), max(c1, c2)
mid = (lo_c + hi_c) / 2
hw = max((np.percentile(lx, 98) - np.percentile(lx, 2)) / 4, 0.05)
outer = np.where(X >= mid, (X - hi_c) / hw, (lo_c - X) / hw)
print('leg centres', round(float(lo_c), 3), round(float(hi_c), 3), 'hw', round(float(hw), 3))
LEGX = (X > lo_c - hw - 0.03) & (X < hi_c + hw + 0.03)

# bare leg between sock and shorts: repaint smeared pixels as skin
leg = valid & LEGX & (Z > Z_SOCK_HI) & (Z < Z_SHORTS_HEM)
sk_med = np.median(base[leg & ~near], axis=0) if (leg & ~near).sum() > 100 else SKIN
paint(leg & near, sk_med)

# shorts
sh = valid & LEGX & (Z >= Z_SHORTS_HEM) & (Z < Z_JERSEY_HEM + 0.01)
paint(sh, BLACK)
paint(sh * sm(outer - 0.28, 0.04), ORANGE)
paint(sh * band(outer, 0.14, 0.28, 0.04), CREAM)
paint(sh * band(Z, Z_SHORTS_HEM, Z_SHORTS_HEM + 0.020), ORANGE)
paint(sh * band(Z, Z_SHORTS_HEM + 0.020, Z_SHORTS_HEM + 0.026), CREAM)

# jersey
jer = valid & (AX < 0.22) & (Z >= Z_JERSEY_HEM) & (Z < Z_NECK) & (np.abs(Y) < 0.2)
paint(jer, BLACK)
paint(jer * band(AX, 0.040, 0.054), CREAM)
paint(jer * band(AX, 0.056, 0.092), ORANGE)
paint(jer * band(AX, 0.094, 0.104), CREAM)
paint(jer * band(Z, Z_JERSEY_HEM, Z_JERSEY_HEM + 0.010), ORANGE)
paint(jer * band(Z, Z_JERSEY_HEM + 0.010, Z_JERSEY_HEM + 0.015), CREAM)
paint(jer * band(AX, 0.0, 0.003) * sm(Z_NECK - 0.02 - Z), (70, 70, 76))

# sleeves
sl = valid & (AX >= 0.15) & (AX < 0.36) & (Z >= Z_SLEEVE_HEM) & (Z < Z_NECK)
paint(sl, BLACK)
paint(sl * band(Z, Z_SLEEVE_HEM + 0.006, Z_SLEEVE_HEM + 0.034), ORANGE)
paint(sl * band(Z, Z_SLEEVE_HEM, Z_SLEEVE_HEM + 0.005), CREAM)
paint(sl * band(Z, Z_SLEEVE_HEM + 0.035, Z_SLEEVE_HEM + 0.040), CREAM)

# socks
so = valid & LEGX & (Z >= Z_SOCK_LO) & (Z <= Z_SOCK_HI)
paint(so, WHITE)
paint(so * band(Z, Z_SOCK_HI - 0.020, Z_SOCK_HI - 0.011), BLACK)
paint(so * band(Z, Z_SOCK_HI - 0.010, Z_SOCK_HI - 0.003), ORANGE)

# gloves: black, skin fingertips (hands are far outboard of the thighs)
gl = valid & (AX > 0.235) & (Z > 0.33) & (Z < 0.50)
paint(gl, BLACK)
paint(gl * sm(0.385 - Z, 0.004), SKIN)

# shoes: orange / brown smear -> white, keep the dark sole + cleats from the projection
shoe = valid & LEGX & (Z < Z_SOCK_LO)
r, g, b = img[..., 0], img[..., 1], img[..., 2]
smear = shoe & ((r - b) > 40)
paint(smear, WHITE)

# hair: the projection smears face-skin colour onto hair strands. Above the brow (and on the side/back of the head) any warm
# tinted texel becomes hair-black.
head = valid & (Z > 0.76)
warm = (img[..., 0] - img[..., 2]) > 22
hair_zone = head & ((Z > 0.935) | (np.abs(X - CX) > 0.165) | (Y > 0.05))
paint(hair_zone & warm, (22, 20, 22))

# gutters
filled = valid.copy()
for _ in range(10):
    acc = np.zeros_like(img)
    cnt = np.zeros(filled.shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            f = np.roll(np.roll(filled, dy, 0), dx, 1)
            acc += np.roll(np.roll(img, dy, 0), dx, 1) * f[..., None]
            cnt += f
    new = (~filled) & (cnt > 0)
    img[new] = acc[new] / cnt[new][..., None]
    filled |= new
Image.fromarray(img.clip(0, 255).astype(np.uint8)).save(out)
print('wrote', out)

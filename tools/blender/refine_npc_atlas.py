"""
3D-aware atlas repaint for Meshy NPC riders (system Python + numpy + Pillow).
  python tools/blender/paint_atlas_3d.py <dump_prefix> <pos.npz> <out.png> [--analyze]
The shattered auto-unwrap cannot be painted in UV space, so we rasterise a per-texel *position map* from the mesh
(clean_npc_rider.py --dumppos) and paint the cloth / hems / socks / helmet as functions of real surface position.
Skin, face, hair and shoes keep the original atlas pixels.
"""
import sys
import numpy as np
from PIL import Image

prefix, posfile, out = sys.argv[1], sys.argv[2], sys.argv[3]
ANALYZE = '--analyze' in sys.argv
base = np.asarray(Image.open(sys.argv[4] if len(sys.argv) > 4 and not sys.argv[4].startswith('--') else prefix + '_atlas.png').convert('RGB'), np.float32)
H, W, _ = base.shape
d = np.load(posfile)
UV, POS = d['uv'], d['pos']


def raster():
    P = np.zeros((H, W, 3), np.float32)
    valid = np.zeros((H, W), bool)
    for t in range(len(UV)):
        uv = UV[t].copy()
        px = uv[:, 0] * W
        py = (1 - uv[:, 1]) * H
        x0, x1 = int(np.floor(px.min())), int(np.ceil(px.max()))
        y0, y1 = int(np.floor(py.min())), int(np.ceil(py.max()))
        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, W - 1), min(y1, H - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        den = (py[1] - py[2]) * (px[0] - px[2]) + (px[2] - px[1]) * (py[0] - py[2])
        if abs(den) < 1e-9:
            continue
        l0 = ((py[1] - py[2]) * (xs - px[2]) + (px[2] - px[1]) * (ys - py[2])) / den
        l1 = ((py[2] - py[0]) * (xs - px[2]) + (px[0] - px[2]) * (ys - py[2])) / den
        l2 = 1 - l0 - l1
        inside = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not inside.any():
            continue
        pos = l0[..., None] * POS[t, 0] + l1[..., None] * POS[t, 1] + l2[..., None] * POS[t, 2]
        sl = (slice(y0, y1 + 1), slice(x0, x1 + 1))
        P[sl][inside] = pos[inside]
        valid[sl] |= inside
    return P, valid


P, valid = raster()
valid0 = valid.copy()
for _ in range(2):
    acc = np.zeros_like(P); cnt = np.zeros(valid.shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            f = np.roll(np.roll(valid, dy, 0), dx, 1)
            acc += np.roll(np.roll(P, dy, 0), dx, 1) * f[..., None]; cnt += f
    new = (~valid) & (cnt > 0)
    P[new] = acc[new] / cnt[new][..., None]
    valid = valid | new
np.savez_compressed(prefix + '_posmap.npz', P=P, valid=valid)
print('raster done, valid texels', int(valid.sum()), 'of', H * W)

# ---- classify the original pixels
pal = np.array([(16, 16, 20), (232, 100, 22), (236, 228, 214), (246, 246, 246)], np.float32)
dist = np.sqrt(((base[:, :, None, :] - pal[None, None]) ** 2).sum(3))
cls = dist.argmin(2)
near = dist.min(2) < np.array([46, 80, 52, 44])[cls]
X, Y, Z = P[..., 0], P[..., 1], P[..., 2]
TX = 0.011

if ANALYZE:
    def hist(name, m, ax, lo, hi, n=24):
        v = ax[m & valid]
        if len(v) == 0:
            print(name, 'none')
            return
        h, e = np.histogram(v, bins=n, range=(lo, hi))
        print(name, ' '.join(f'{int(x)}' for x in h))
    leg = valid & (np.abs(X - TX) > 0.03) & (np.abs(X - TX) < 0.2) & (Z < 0.56)
    for nm, k in (('black', 0), ('orange', 1), ('cream', 2), ('white', 3)):
        print('z hist (0..0.8, 24 bins) on legs/pelvis:')
        hist(nm, leg & near & (cls == k), Z, 0.0, 0.8)
    tor = valid & (np.abs(X - TX) < 0.16) & (Z > 0.5) & (Z < 0.8)
    for nm, k in (('black', 0), ('orange', 1), ('cream', 2)):
        hist('torso ' + nm, tor & near & (cls == k), Z, 0.5, 0.8)
    sys.exit(0)

# ----------------------------------------------------------------------------- repaint by position
def sm(x, w=0.0016):
    return np.clip(0.5 + x / w, 0, 1)


def band(v, lo, hi, w=0.0016):
    return np.minimum(sm(v - lo, w), sm(hi - v, w))


BLACK = np.array((18, 18, 22), np.float32)
ORANGE = np.array((232, 100, 22), np.float32)
CREAM = np.array((238, 230, 215), np.float32)
WHITE = np.array((246, 246, 246), np.float32)
GRAPH = np.array((46, 46, 52), np.float32)
GREY = np.array((112, 112, 120), np.float32)

_tm = valid & (Z > 0.62) & (Z < 0.70) & (np.abs(X - TX) < 0.17)
TX = float((np.percentile(X[_tm], 3) + np.percentile(X[_tm], 97)) / 2) if _tm.sum() > 100 else TX
print('torso centre x', round(TX, 3))
AX = np.abs(X - TX)
img = base.copy()


def paint(mask, color):
    m = (mask & valid)[..., None].astype(np.float32) if mask.dtype == bool else (mask * valid)[..., None]
    img[:] = img * (1 - m) + color * m


Z_SHORTS_HEM, Z_JERSEY_HEM, Z_SLEEVE_HEM = 0.268, 0.565, 0.625
Z_SOCK_LO, Z_SOCK_HI = 0.098, 0.168

# per-leg centring (the Meshy rigs are not mirror-exact): 2-means on x of the bare thigh/shin texels
lm = valid & (Z > 0.20) & (Z < 0.30) & (np.abs(X) < 0.25)
lx = X[lm]
c1, c2 = np.percentile(lx, 25), np.percentile(lx, 75)
for _ in range(12):
    a1 = np.abs(lx - c1) < np.abs(lx - c2)
    c1, c2 = lx[a1].mean(), lx[~a1].mean()
lo_c, hi_c = min(c1, c2), max(c1, c2)
hw_lo = (np.percentile(lx[lx < (lo_c + hi_c) / 2], 98) - np.percentile(lx[lx < (lo_c + hi_c) / 2], 2)) / 2
hw_hi = (np.percentile(lx[lx >= (lo_c + hi_c) / 2], 98) - np.percentile(lx[lx >= (lo_c + hi_c) / 2], 2)) / 2
mid = (lo_c + hi_c) / 2
side_hi = X >= mid
outer = np.where(side_hi, (X - hi_c) / hw_hi, (lo_c - X) / hw_lo)     # + = towards the outside of that leg
print('legs centres', round(float(lo_c), 3), round(float(hi_c), 3), 'half widths', round(float(hw_lo), 3), round(float(hw_hi), 3))
LEGX = (X > lo_c - hw_lo - 0.03) & (X < hi_c + hw_hi + 0.03)

# legs: skin between the sock top and the shorts hem - only where the old pixels are not already skin-like (fringe)
leg = valid & LEGX & (Z > Z_SOCK_HI) & (Z < Z_SHORTS_HEM)
skin_px = base[leg & ~near]
skin_col = np.median(skin_px, axis=0) if len(skin_px) else np.array((120, 70, 45), np.float32)
paint(leg & near, skin_col)

# shorts
sh = valid & (X > lo_c - hw_lo - 0.03) & (X < hi_c + hw_hi + 0.03) & (Z >= Z_SHORTS_HEM) & (Z < Z_JERSEY_HEM + 0.01)
paint(sh, BLACK)
paint(sh * sm(outer - 0.50, 0.03), ORANGE)
paint(sh * band(outer, 0.34, 0.50, 0.03), CREAM)
paint(sh * band(Z, Z_SHORTS_HEM, Z_SHORTS_HEM + 0.026), ORANGE)
paint(sh * band(Z, Z_SHORTS_HEM + 0.026, Z_SHORTS_HEM + 0.033), CREAM)

# jersey torso (front/back share x so the stripes wrap straight)
jer = valid & (AX < 0.20) & (Z >= Z_JERSEY_HEM) & (Z < 0.765)
paint(jer, BLACK)
paint(jer * band(AX, 0.040, 0.054), CREAM)
paint(jer * band(AX, 0.056, 0.092), ORANGE)
paint(jer * band(AX, 0.094, 0.104), CREAM)
paint(jer * band(Z, Z_JERSEY_HEM, Z_JERSEY_HEM + 0.012), ORANGE)
paint(jer * band(Z, Z_JERSEY_HEM + 0.012, Z_JERSEY_HEM + 0.018), CREAM)
paint(jer * band(AX, 0.0, 0.003) * sm(0.74 - Z), (70, 70, 76))

# sleeves (outboard of the torso, above the sleeve hem)
sl = valid & (AX >= 0.145) & (AX < 0.30) & (Z >= Z_SLEEVE_HEM) & (Z < 0.765)
paint(sl, BLACK)
paint(sl * band(Z, Z_SLEEVE_HEM + 0.006, Z_SLEEVE_HEM + 0.040), ORANGE)
paint(sl * band(Z, Z_SLEEVE_HEM, Z_SLEEVE_HEM + 0.005), CREAM)
paint(sl * band(Z, Z_SLEEVE_HEM + 0.041, Z_SLEEVE_HEM + 0.046), CREAM)

# socks
sk = valid & LEGX & (Z >= Z_SOCK_LO) & (Z <= Z_SOCK_HI)
paint(sk, WHITE)
paint(sk * band(Z, Z_SOCK_HI - 0.022, Z_SOCK_HI - 0.012), BLACK)
paint(sk * band(Z, Z_SOCK_HI - 0.011, Z_SOCK_HI - 0.003), ORANGE)

# helmet: graphite shell + two lighter stripes front->back
# helmet: keep the denoised original pixels (vent pits + stripes read through); clean_atlas.py already snapped them to graphite/grey

# Match the individually approved kits with continuous 3D-space graphics.
name = next((a.split('=',1)[1] for a in sys.argv if a.startswith('--name=')), 'Akihiro')
if name != 'Akihiro':
    presets = {
        'Akane': ((158,36,48),(233,190,100),(242,232,216),(20,20,24)),
        'Shiori': ((20,111,113),(240,103,86),(48,176,181),(19,25,30)),
        'Shinobu': ((31,35,65),(126,111,173),(246,246,246),(31,35,65)),
        'Coral': ((238,94,84),(246,246,246),(246,246,246),(20,49,56)),
        'Hanakage': ((238,94,84),(246,246,246),(246,246,246),(20,49,56)),
    }
    main, accent, trim, short = [np.array(c,np.float32) for c in presets[name]]
    paint(jer, main);paint(sh,short);paint(sl,main)
    if name=='Akane':
        paint(jer & (AX>.105),trim)
        paint(jer * band(AX-(Z-Z_JERSEY_HEM)*.48,.018,.033),accent)
        paint(sh * band(outer-(Z-Z_SHORTS_HEM)*3.2,-.10,.07),accent)
    elif name in ('Shiori','Shinobu'):
        # Front and back views carry the same stripe wrapped around the torso.
        diagonal = Z+.64*(X-TX)*np.where(Y<0,1,-1)
        paint(jer * band(diagonal,.625,.680),accent)
        paint(jer * band(diagonal,.680,.693),trim)
        paint(sh * band(outer-(Z-Z_SHORTS_HEM)*3.2,-.15,.10),accent)
        if name=='Shinobu':paint(sh * band(outer-(Z-Z_SHORTS_HEM)*3.2,.10,.15),trim)
    else:
        curve=.072+.055*((Z-.65)/.12)**2
        paint(jer * band(AX-curve,-.008,.008),trim)
        paint(sh * sm(outer-.55,.03),main)
        paint(sh * band(outer,.40,.55,.03),trim)
    paint(sl * band(Z,Z_SLEEVE_HEM,Z_SLEEVE_HEM+.012),trim)
    paint(sh * band(Z,Z_SHORTS_HEM,Z_SHORTS_HEM+.012),accent if name not in ('Coral','Hanakage') else short)
    paint(jer * band(AX,0,.003), (38,38,42))
    paint(sk, (246,246,246) if name not in ('Coral','Hanakage') else (19,19,23))
    paint(sk * band(Z,Z_SOCK_HI-.025,Z_SOCK_HI-.016),short)
    paint(sk * band(Z,Z_SOCK_HI-.015,Z_SOCK_HI-.005),main)

# gutters: bleed painted colours outward so mip-mapping does not pull in old pixels
filled = valid.copy()
for _ in range(10):
    acc = np.zeros_like(img)
    cnt = np.zeros(filled.shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            f = np.roll(np.roll(filled, dy, 0), dx, 1)
            c = np.roll(np.roll(img, dy, 0), dx, 1)
            acc += c * f[..., None]
            cnt += f
    new = (~filled) & (cnt > 0)
    img[new] = acc[new] / cnt[new][..., None]
    filled |= new

Image.fromarray(img.clip(0, 255).astype(np.uint8)).save(out)
print('wrote', out)

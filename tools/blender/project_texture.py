"""
Project the concept-sheet views (front, side; back reuses the front) onto an unwrapped generated rider -> albedo atlas.
  python tools/blender/project_texture.py <tris.npz> <sheet.png> <out_atlas.png> [--size 4096]
Orthographic approximation. Each view is auto-aligned to the mesh by silhouette IoU search. Per-texel colour is the
visibility-tested, facing-weighted blend of the views; unseen texels are filled from neighbours.
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

tris_file, sheet, out = sys.argv[1], sys.argv[2], sys.argv[3]
SIZE = int(sys.argv[sys.argv.index('--size') + 1]) if '--size' in sys.argv else 4096
d = np.load(tris_file)
UV, POS, NRM = d['uv'], d['pos'], d['nrm']
ref = Image.open(sheet).convert('RGB')
R = np.asarray(ref).astype(np.float32)
BG = np.array([172, 173, 176], np.float32)
MASK = (np.abs(R - BG).sum(2) > 40)
MASK[765:] = False
verts = POS.reshape(-1, 3)

PANELS = {'front': (36, 440), 'side': (919, 1224)}


def view_axes(name):
    """(u_axis, u_sign, toCamera, depth_axis, depth_sign): image-x comes from world u_axis*u_sign; image-y from -Z."""
    if name == 'front':      # camera at -Y looking +Y, image right = +X
        return 0, +1.0, np.array([0, -1.0, 0]), 1, +1.0
    if name == 'back':       # same picture reused for the rear surface; camera at +Y
        return 0, +1.0, np.array([0, +1.0, 0]), 1, -1.0
    if name == 'side_r':     # camera at -X, image right = -Y (character faces right)
        return 1, -1.0, np.array([-1.0, 0, 0]), 0, +1.0
    if name == 'side_l':     # same picture reused for the +X side
        return 1, -1.0, np.array([+1.0, 0, 0]), 0, -1.0


def project(P, name, par):
    ua, us, _, _, _ = view_axes(name)
    s, ox, oy = par
    return ox + s * us * P[..., ua], oy - s * P[..., 2]


def fit(panel, name):
    x0, x1 = PANELS[panel]
    sub = MASK[:, x0 - 8:x1 + 8]
    ys, xs = np.where(sub)
    ref_h = ys.max() - ys.min()
    zmin, zmax = verts[:, 2].min(), verts[:, 2].max()
    s0 = ref_h / (zmax - zmin)
    ua, us, _, _, _ = view_axes(name)
    uvals = us * verts[:, ua]
    best = None
    ref_c = ((xs.min() + xs.max()) / 2 + x0 - 8)
    m_c = (uvals.min() + uvals.max()) / 2
    for ds in np.linspace(0.96, 1.04, 9):
        s = s0 * ds
        for dy in range(-10, 11, 2):
            for dx in range(-10, 11, 2):
                ox = ref_c - s * m_c + dx
                oy = (ys.max() + 0.0) + s * zmin + dy
                px = np.round(ox + s * uvals).astype(int)
                py = np.round(oy - s * verts[:, 2]).astype(int)
                ok = (px >= 0) & (px < R.shape[1]) & (py >= 0) & (py < R.shape[0])
                m = np.zeros(MASK.shape, bool)
                m[py[ok], px[ok]] = True
                mm = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5))) > 0
                a = mm[:, x0 - 8:x1 + 8]
                iou = (a & sub).sum() / max((a | sub).sum(), 1)
                if best is None or iou > best[0]:
                    best = (iou, (s, ox, oy))
    print('fit', panel, 'iou', round(float(best[0]), 3), 'scale px/m', round(float(best[1][0]), 1))
    return best[1]


PAR = {'front': fit('front', 'front'), 'side': fit('side', 'side_r')}
PAR['back'] = PAR['front']
PAR['side_r'] = PAR['side']
PAR['side_l'] = PAR['side']

# ---- z-buffers per view
H, W = MASK.shape


def zbuffer(name):
    ua, us, tc, da, ds_ = view_axes(name)
    u, v = project(POS, name, PAR[name])
    depth = ds_ * POS[..., da]
    zb = np.full((H, W), 1e9, np.float32)
    for t in range(len(POS)):
        px, py, dz = u[t], v[t], depth[t]
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
        ins = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
        if not ins.any():
            continue
        z = l0 * dz[0] + l1 * dz[1] + l2 * dz[2]
        sl = (slice(y0, y1 + 1), slice(x0, x1 + 1))
        cur = zb[sl]
        cur[ins] = np.minimum(cur[ins], z[ins])
    return zb


ZB = {k: zbuffer(k) for k in ('front', 'back', 'side_r', 'side_l')}
print('zbuffers done')

# ---- raster texels -> position + normal
P = np.zeros((SIZE, SIZE, 3), np.float32)
N = np.zeros((SIZE, SIZE, 3), np.float32)
valid = np.zeros((SIZE, SIZE), bool)
for t in range(len(UV)):
    uv = UV[t]
    px = uv[:, 0] * SIZE
    py = (1 - uv[:, 1]) * SIZE
    x0, x1 = int(max(np.floor(px.min()), 0)), int(min(np.ceil(px.max()), SIZE - 1))
    y0, y1 = int(max(np.floor(py.min()), 0)), int(min(np.ceil(py.max()), SIZE - 1))
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
    nr = l0[..., None] * NRM[t, 0] + l1[..., None] * NRM[t, 1] + l2[..., None] * NRM[t, 2]
    sl = (slice(y0, y1 + 1), slice(x0, x1 + 1))
    P[sl][ins] = pos[ins]
    N[sl][ins] = nr[ins]
    valid[sl] |= ins
N /= np.maximum(np.linalg.norm(N, axis=2, keepdims=True), 1e-9)
print('texels valid', int(valid.sum()))

# ---- blend views
acc = np.zeros((SIZE, SIZE, 3), np.float32)
wsum = np.zeros((SIZE, SIZE), np.float32)
bgd = np.abs(R - BG).sum(2)
er = np.asarray(Image.fromarray((MASK * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(5))) > 0
for name in ('front', 'back', 'side_r', 'side_l'):
    ua, us, tc, da, ds_ = view_axes(name)
    u, v = project(P, name, PAR[name])
    ui = np.clip(np.round(u).astype(int), 0, W - 1)
    vi = np.clip(np.round(v).astype(int), 0, H - 1)
    depth = ds_ * P[..., da]
    vis = depth <= ZB[name][vi, ui] + 0.005
    face = np.clip((N * tc).sum(2), 0, 1)
    w = (face ** 4.0) * vis * valid * er[vi, ui] * (bgd[vi, ui] > 60)
    if name == 'back':
        w *= 0.7
    col = R[vi, ui]
    acc += col * w[..., None]
    wsum += w
    print(name, 'covers', int((w > 0.05).sum()))
res = np.where(wsum[..., None] > 1e-4, acc / np.maximum(wsum[..., None], 1e-4), 0).astype(np.float32)
have = wsum > 0.02

# ---- fill: dilate known colours into the unseen texels (then gutters)
filled = have.copy()
for it in range(60):
    a2 = np.zeros_like(res)
    c2 = np.zeros(filled.shape, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            f = np.roll(np.roll(filled, dy, 0), dx, 1)
            a2 += np.roll(np.roll(res, dy, 0), dx, 1) * f[..., None]
            c2 += f
    new = (~filled) & (c2 > 0)
    if not new.any():
        break
    res[new] = a2[new] / c2[new][..., None]
    filled |= new
Image.fromarray(res.clip(0, 255).astype(np.uint8)).save(out)
print('wrote', out, 'coverage', round(float(have.sum() / max(valid.sum(), 1)), 3))

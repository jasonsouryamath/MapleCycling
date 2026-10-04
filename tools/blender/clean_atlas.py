"""
Atlas cleanup for Meshy NPC riders (run with system Python + Pillow + numpy).
  python tools/blender/clean_atlas.py <dump_prefix> <out.png>
Inputs written by clean_npc_rider.py --dump: <prefix>_atlas.png and <prefix>_face_tris.txt (UV triangles of the face, protected).
Steps outside the face: median denoise -> snap near-palette pixels to the rider's flat palette -> majority-style median on the
snapped colours (removes torn hem fringe / specks) -> blend back.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

prefix, out = sys.argv[1], sys.argv[2]
im = Image.open(prefix + '_atlas.png').convert('RGB')
W, H = im.size
mask = Image.new('L', (W, H), 0)
d = ImageDraw.Draw(mask)
for line in open(prefix + '_face_tris.txt'):
    v = list(map(float, line.split()))
    d.polygon([(v[i] * W, (1 - v[i + 1]) * H) for i in range(0, len(v), 2)], fill=255)
mask = mask.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(3))
m = np.asarray(mask, dtype=np.float32)[..., None] / 255

a = np.asarray(im, dtype=np.float32)
med = np.asarray(im.filter(ImageFilter.MedianFilter(7)), dtype=np.float32)

# palette: jersey black, orange, cream, white, helmet graphite, helmet stripe grey
pal = np.array([(16, 16, 20), (232, 100, 22), (236, 228, 214), (246, 246, 246), (48, 48, 54), (118, 118, 124)], np.float32)
tol = np.array([42, 70, 48, 40, 22, 26], np.float32)
dist = np.sqrt(((med[:, :, None, :] - pal[None, None]) ** 2).sum(3))
idx = dist.argmin(2)
ok = (dist.min(2) < tol[idx])[..., None]
snap = np.where(ok, pal[idx], med)

# clean the fringe: median over the snapped colours (majority-like), only where the pixel was snapped
sn_img = Image.fromarray(snap.clip(0, 255).astype(np.uint8))
maj = np.asarray(sn_img.filter(ImageFilter.MedianFilter(9)), dtype=np.float32)
res = np.where(ok, maj, med)

final = a * m + res * (1 - m)
Image.fromarray(final.clip(0, 255).astype(np.uint8)).save(out)
print('wrote', out)

import sys
import numpy as np
from PIL import Image

path = sys.argv[1]
a = np.asarray(Image.open(path).convert('RGB')).astype(float)
h, w, _ = a.shape
print(path, a.shape)

dv = np.abs(a[:, 1:, :] - a[:, :-1, :]).mean(axis=(0, 2))
top = np.argsort(dv)[-6:][::-1]
print('vertical seams x =', sorted([(int(x + 1), round(float(dv[x]), 2)) for x in top]))

dh = np.abs(a[1:, :, :] - a[:-1, :, :]).mean(axis=(1, 2))
toph = np.argsort(dh)[-6:][::-1]
print('horizontal seams y =', sorted([(int(y + 1), round(float(dh[y]), 2)) for y in toph]))

# restrict to the lower-right quadrant where the rectangle was seen
q = a[int(h * 0.6):, int(w * 0.7):, :]
dvq = np.abs(q[:, 1:, :] - q[:, :-1, :]).mean(axis=(0, 2))
xq = int(np.argmax(dvq)) + 1 + int(w * 0.7)
print('lower-right strongest vertical seam x =', xq, round(float(dvq.max()), 2))
print('  left of seam  mean', a[int(h * 0.6):, xq - 60:xq - 5, :].mean(axis=(0, 1)).round(1))
print('  right of seam mean', a[int(h * 0.6):, xq + 5:xq + 60, :].mean(axis=(0, 1)).round(1))

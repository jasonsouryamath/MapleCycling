import glob
import os
import numpy as np
from PIL import Image

base = os.path.dirname(os.path.abspath(__file__))
d = r"C:\Users\jason\OneDrive\Desktop\MapleRide\good_graphics\spot"


def seam(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(float)
    dv = np.abs(a[:, 1:, :] - a[:, :-1, :]).mean(axis=(0, 2))
    # the artefact seam lives near x=800 at 1600 wide
    lo, hi = 780, 820
    return float(dv[lo:hi].max()), int(np.argmax(dv[lo:hi]) + lo)


rows = []
for p in sorted(glob.glob(os.path.join(d, 'sweep_*.png'))):
    s, x = seam(p)
    rows.append((s, x, os.path.basename(p)))

rows.sort()
for s, x, n in rows:
    print('%8.2f  x=%4d  %s' % (s, x, n))

import sys
import numpy as np
from PIL import Image

a = np.asarray(Image.open(sys.argv[1]).convert('RGB')).astype(float)
xs = [int(v) for v in sys.argv[2].split(',')]
ys = [int(v) for v in sys.argv[3].split(',')] if len(sys.argv) > 3 else []

for x in xs:
    d = np.abs(a[:, x, :] - a[:, x - 1, :]).mean(axis=1)
    r = np.where(d > 25)[0]
    print('vertical edge x=%d present rows %s..%s (n=%d)' % (x, r.min() if len(r) else '-', r.max() if len(r) else '-', len(r)))

for y in ys:
    d = np.abs(a[y, :, :] - a[y - 1, :, :]).mean(axis=1)
    c = np.where(d > 25)[0]
    print('horizontal edge y=%d present cols %s..%s (n=%d)' % (y, c.min() if len(c) else '-', c.max() if len(c) else '-', len(c)))

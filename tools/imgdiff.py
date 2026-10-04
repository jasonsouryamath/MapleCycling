import sys
from PIL import Image, ImageChops

a = Image.open(sys.argv[1]).convert("RGB")
b = Image.open(sys.argv[2]).convert("RGB")
d = ImageChops.difference(a, b)
bbox = d.getbbox()
hist = d.histogram()
# mean abs diff
n = a.size[0] * a.size[1] * 3
tot = sum(i * hist[i] for ch in range(3) for i in range(256) if False)
import numpy as np
na = np.asarray(a, dtype=np.int16)
nb = np.asarray(b, dtype=np.int16)
diff = np.abs(na - nb)
print("bbox:", bbox)
print("mean abs diff: %.3f  max: %d  pixels>8: %d" %
      (diff.mean(), diff.max(), int((diff.max(axis=2) > 8).sum())))

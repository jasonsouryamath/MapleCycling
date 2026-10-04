import sys
import numpy as np
from PIL import Image

a = np.asarray(Image.open(sys.argv[1]).convert("RGB"), dtype=np.int16)
b = np.asarray(Image.open(sys.argv[2]).convert("RGB"), dtype=np.int16)
d = np.abs(a - b).max(axis=2)
amp = np.clip(d.astype(np.float32) * 6.0, 0, 255).astype(np.uint8)
Image.fromarray(amp).save(sys.argv[3])
print("saved", sys.argv[3], "changed px:", int((d > 8).sum()))

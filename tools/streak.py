"""Measure directional streak anisotropy on the s1 1179 cut slope.

Fall-line striping shows up as a gradient that is much stronger ACROSS the streaks than ALONG
them. The ratio of the two is therefore a direction-sensitive measure that ignores the broad
shading gradient and, unlike a plain high-pass, is not dominated by tree and trunk edges.
"""
from PIL import Image
import numpy as np

BOX = (240, 165, 460, 330)  # clean patch of the left cut slope, no trunks

for tag in ("kawabefix", "terr29s1", "flatn", "terr29d"):
    a = np.asarray(Image.open(f"good_graphics/spot/{tag}_s1_1179.png").convert("L").crop(BOX)).astype(float)
    gy, gx = np.gradient(a)
    # The striping runs roughly down-right, so project onto the two diagonals.
    across = (gx - gy) / np.sqrt(2.0)
    along = (gx + gy) / np.sqrt(2.0)
    print(f"{tag:10s} across {across.std():5.2f}  along {along.std():5.2f}  ratio {across.std()/along.std():5.2f}")

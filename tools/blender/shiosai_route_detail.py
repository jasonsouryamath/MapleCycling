"""
Zoomed plan-view details of the authored Shiosai route.

The full-route plan view is too coarse to judge the features that actually decide whether the
road is rideable: a 5 km switchback stack is 400 m wide at 42 km scale. This draws each critical
feature at its own scale, with the carriageway edges swept from the published side vectors so
what is drawn is the real 7 m road, not just a centreline.

    python tools/blender/shiosai_route_detail.py
"""

import os

import numpy as np
from PIL import Image, ImageDraw

import shiosai_route as R

PANEL = 700
PAD = 40

# (title, centre chainage m, half-extent m)
FEATURES = [
    ("Ch2 Upper Switchbacks (SC02)", 8500, 420),
    ("Ch2 hairpin detail", 7400, 90),
    ("Ch5 Red Bridge (SC05)", 21500, 900),
    ("Ch6 Lighthouse promontory loop (SC06)", 26800, 420),
    ("Ch4 Fishing Village (SC04)", 18000, 500),
    ("Ch8 Coastal Highway finale (SC08)", 41000, 900),
]


def main():
    p, _, side, _, _, arc = R.centreline()
    half = R.ROAD_HALF_WIDTH + R.SHOULDER_WIDTH

    cols = 3
    rows = (len(FEATURES) + cols - 1) // cols
    img = Image.new("RGB", (cols * PANEL, rows * PANEL), (16, 21, 30))
    d = ImageDraw.Draw(img)

    for n, (title, centre, extent) in enumerate(FEATURES):
        ox = (n % cols) * PANEL
        oy = (n // cols) * PANEL
        d.rectangle([ox + 2, oy + 2, ox + PANEL - 2, oy + PANEL - 2], outline=(44, 54, 68))

        i0 = int(np.searchsorted(arc, max(0, centre - extent * 1.6)))
        i1 = int(np.searchsorted(arc, min(arc[-1], centre + extent * 1.6)))
        seg = p[i0:i1]
        sd = side[i0:i1]
        if len(seg) < 2:
            continue

        cx, cz = seg[:, 0].mean(), seg[:, 2].mean()
        s = (PANEL - 2 * PAD) / (2.0 * extent)

        def to_px(x, z):
            return (ox + PANEL * 0.5 + (x - cx) * s,
                    oy + PANEL * 0.5 - (z - cz) * s)

        left = [to_px(seg[i, 0] - sd[i, 0] * half, seg[i, 2] - sd[i, 2] * half)
                for i in range(len(seg))]
        right = [to_px(seg[i, 0] + sd[i, 0] * half, seg[i, 2] + sd[i, 2] * half)
                 for i in range(len(seg))]

        d.polygon(left + right[::-1], fill=(44, 48, 56))
        d.line(left, fill=(226, 230, 238), width=2)
        d.line(right, fill=(226, 230, 238), width=2)
        d.line([to_px(q[0], q[2]) for q in seg], fill=(250, 206, 92), width=1)

        for km, name in R.ANCHORS:
            a = km * (arc[-1] / R.TOTAL_LENGTH)
            if arc[i0] <= a <= arc[i1 - 1]:
                j = int(np.searchsorted(arc, a))
                px, py = to_px(p[j, 0], p[j, 2])
                d.ellipse([px - 5, py - 5, px + 5, py + 5], outline=(120, 230, 160), width=2)
                d.text((px + 8, py + 4), name.replace("SC_KM_", ""), fill=(120, 230, 160))

        bar = 100.0 * s
        d.line([ox + PAD, oy + PANEL - 26, ox + PAD + bar, oy + PANEL - 26],
               fill=(150, 168, 190), width=3)
        d.text((ox + PAD, oy + PANEL - 22), "100 m", fill=(150, 168, 190))
        d.text((ox + 12, oy + 12), title, fill=(236, 242, 250))
        d.text((ox + 12, oy + 28),
               f"km {(centre - extent) / 1000:.1f}-{(centre + extent) / 1000:.1f}   "
               f"road {2 * half:.1f} m incl. shoulders", fill=(140, 156, 176))

    out = os.path.join(R.renders_root(), "shiosai_route_detail.png")
    img.save(out)
    print(f"[shiosai-detail] wrote {out}")


if __name__ == "__main__":
    main()

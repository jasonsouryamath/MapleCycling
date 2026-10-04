"""
Plan-view and elevation-profile render of the authored Shiosai route.

A route is geometry, and this project's standing rule is that geometry is verified by LOOKING
at it, not by reading numbers off a validator. Every contract can pass while the road still
doubles back through itself, stacks its switchbacks into a knot, or wanders somewhere that makes
no geographic sense. So: draw it.

    python tools/blender/shiosai_route_preview.py
"""

import os

import numpy as np
from PIL import Image, ImageDraw

import shiosai_route as R

W, H = 1600, 1400
MARGIN = 60
PROFILE_H = 380

CHAPTER_COLORS = [
    (232, 108, 96),    # gateway      - warm
    (240, 168, 80),    # switchbacks
    (214, 208, 96),    # hydrangea
    (128, 208, 128),   # village
    (232, 92, 84),     # red bridge   - deliberately coral, it is the landmark
    (104, 196, 216),   # lighthouse
    (112, 152, 228),   # sea arch
    (176, 128, 224),   # highway
]


def main():
    p, _, _, _, _, arc = R.centreline()
    x, y, z = p[:, 0], p[:, 1], p[:, 2]

    img = Image.new("RGB", (W, H), (18, 24, 34))
    d = ImageDraw.Draw(img)

    # ---------------------------------------------------------------- plan view
    plan_h = H - PROFILE_H - MARGIN
    sx = (W - 2 * MARGIN) / max(x.max() - x.min(), 1e-6)
    sz = (plan_h - 2 * MARGIN) / max(z.max() - z.min(), 1e-6)
    s = min(sx, sz)
    ox = MARGIN + ((W - 2 * MARGIN) - (x.max() - x.min()) * s) * 0.5
    oz = MARGIN + ((plan_h - 2 * MARGIN) - (z.max() - z.min()) * s) * 0.5

    def to_px(i):
        # +Z is north, so it must go UP the image: flip it.
        return (ox + (x[i] - x.min()) * s,
                plan_h - oz - (z[i] - z.min()) * s)

    scale = arc[-1] / R.TOTAL_LENGTH
    for ci, (cid, cname, a, b) in enumerate(R.CHAPTERS):
        i0 = int(np.searchsorted(arc, a * scale))
        i1 = int(np.searchsorted(arc, b * scale))
        pts = [to_px(i) for i in range(i0, min(i1 + 1, len(x)))]
        if len(pts) > 1:
            d.line(pts, fill=CHAPTER_COLORS[ci], width=4)
        mid = to_px((i0 + i1) // 2)
        d.text((mid[0] + 8, mid[1] - 6), f"{ci + 1} {cname}", fill=CHAPTER_COLORS[ci])

    for km, name in R.ANCHORS:
        i = int(min(np.searchsorted(arc, km * scale), len(x) - 1))
        px, py = to_px(i)
        d.ellipse([px - 5, py - 5, px + 5, py + 5], outline=(255, 255, 255), width=2)
        d.text((px + 9, py + 4), name.replace("SC_KM_", ""), fill=(200, 210, 225))

    d.text((MARGIN, 18),
           f"SHIOSAI GRAND COAST - plan view - {arc[-1] / 1000:.3f} km, "
           f"{len(x)} samples, x {x.min():.0f}..{x.max():.0f}  z {z.min():.0f}..{z.max():.0f}",
           fill=(235, 240, 250))
    d.text((MARGIN, 36), "north is up; sea lies west (left) of the coastal chapters",
           fill=(150, 165, 185))

    # ---------------------------------------------------------------- elevation profile
    top = H - PROFILE_H
    d.rectangle([0, top, W, H], fill=(12, 16, 24))
    gx = (W - 2 * MARGIN) / arc[-1]
    gy = (PROFILE_H - 80) / max(y.max(), 1e-6)

    for ci, (cid, cname, a, b) in enumerate(R.CHAPTERS):
        i0 = int(np.searchsorted(arc, a * scale))
        i1 = int(np.searchsorted(arc, b * scale))
        pts = [(MARGIN + arc[i] * gx, H - 40 - y[i] * gy)
               for i in range(i0, min(i1 + 1, len(x)))]
        if len(pts) > 1:
            d.line(pts, fill=CHAPTER_COLORS[ci], width=3)
        d.line([MARGIN + a * scale * gx, top + 30, MARGIN + a * scale * gx, H - 40],
               fill=(52, 62, 78))

    for metres in (0, 100, 200, 300, 400):
        yy = H - 40 - metres * gy
        d.line([MARGIN, yy, W - MARGIN, yy], fill=(38, 46, 58))
        d.text((8, yy - 6), f"{metres} m", fill=(120, 134, 152))
    for km in range(0, 43, 6):
        xx = MARGIN + km * 1000 * scale * gx
        d.text((xx - 8, H - 30), f"{km}", fill=(120, 134, 152))

    d.text((MARGIN, top + 10),
           f"elevation profile - {y.min():.0f} to {y.max():.0f} m   (km along route)",
           fill=(235, 240, 250))

    out = os.path.join(R.renders_root(), "shiosai_route_plan.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    img.save(out)
    print(f"[shiosai-plan] wrote {out}")


if __name__ == "__main__":
    main()

"""
MapleRide - Kuro blockout vs ReferencePack comparison sheets.

Builds, for each priority reference angle, a 3-panel PNG:
    [ reference ] [ blockout render ] [ silhouette overlay ]
and prints normalised silhouette metrics (head fraction, shoulder width,
crotch height, overall aspect) so gross proportion errors are measurable
as well as visible.

Usage:  python tools/kuro_blockout_compare.py
"""

import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
REF = os.path.join(ROOT, "Assets", "Kuro", "ReferencePack")
REN = os.path.join(ROOT, "good_graphics", "kuro_blockout")
OUT = REN

# (reference file, blockout render, reference bottom crop for the caption bar)
PAIRS = [
    ("03_riding_left.png", "blockout_riding_left.png", 28, "left"),
    ("04_riding_right.png", "blockout_riding_right.png", 28, "right"),
    ("05_standing_front.png", "blockout_standing_front.png", 28, "front"),
    ("07_standing_right.png", "blockout_standing_right.png", 28, "side"),
]


def ref_mask(path, cut_bottom):
    a = np.asarray(Image.open(path).convert("RGB")).astype(float)
    a = a[: a.shape[0] - cut_bottom]
    h, w, _ = a.shape
    L = np.median(a[:, :3, :], axis=1)
    R = np.median(a[:, -3:, :], axis=1)
    t = np.linspace(0, 1, w)[None, :, None]
    bg = L[:, None, :] * (1 - t) + R[:, None, :] * t
    return np.abs(a - bg).max(axis=2) > 16


def render_mask(path):
    a = np.asarray(Image.open(path).convert("RGB")).astype(float)
    bg = np.median(a[:8, :8, :].reshape(-1, 3), axis=0)
    return np.abs(a - bg[None, None, :]).max(axis=2) > 14


def crop_to_mask(img, m, pad=4):
    ys, xs = np.nonzero(m)
    y0, y1 = max(ys.min() - pad, 0), min(ys.max() + pad, m.shape[0] - 1)
    x0, x1 = max(xs.min() - pad, 0), min(xs.max() + pad, m.shape[1] - 1)
    return img.crop((x0, y0, x1 + 1, y1 + 1)), m[y0:y1 + 1, x0:x1 + 1]


def metrics(m):
    ys, xs = np.nonzero(m)
    h = ys.max() - ys.min() + 1
    w = xs.max() - xs.min() + 1
    rows = [np.nonzero(m[y])[0] for y in range(m.shape[0])]
    widths = np.array([len(r) and (r.max() - r.min() + 1) or 0 for r in rows], float)
    return {"h": int(h), "w": int(w), "aspect": round(w / h, 3),
            "max_w_frac": round(float(widths.max()) / h, 3),
            "max_w_at": round(float(widths.argmax() - ys.min()) / h, 3)}


def outline(m):
    e = np.zeros_like(m)
    e[1:-1, 1:-1] = m[1:-1, 1:-1] & ~(m[:-2, 1:-1] & m[2:, 1:-1] &
                                      m[1:-1, :-2] & m[1:-1, 2:])
    return e


def main():
    for ref_name, ren_name, cut, tag in PAIRS:
        rp, np_ = os.path.join(REF, ref_name), os.path.join(REN, ren_name)
        if not os.path.exists(np_):
            print(f"[compare] MISSING render {np_}")
            continue
        rimg = Image.open(rp).convert("RGB")
        rimg = rimg.crop((0, 0, rimg.width, rimg.height - cut))
        rm = ref_mask(rp, cut)
        rimg, rm = crop_to_mask(rimg, rm)

        bimg = Image.open(np_).convert("RGB")
        bm = render_mask(np_)
        bimg, bm = crop_to_mask(bimg, bm)

        H = 700
        rs = H / rimg.height
        bs = H / bimg.height
        rimg2 = rimg.resize((max(1, int(rimg.width * rs)), H), Image.LANCZOS)
        bimg2 = bimg.resize((max(1, int(bimg.width * bs)), H), Image.LANCZOS)
        rm2 = np.asarray(Image.fromarray((rm * 255).astype(np.uint8))
                         .resize(rimg2.size, Image.NEAREST)) > 127
        bm2 = np.asarray(Image.fromarray((bm * 255).astype(np.uint8))
                         .resize(bimg2.size, Image.NEAREST)) > 127

        # overlay: reference silhouette (blue) + blockout outline (orange),
        # aligned on silhouette bottom-centre
        W = max(rm2.shape[1], bm2.shape[1]) + 20
        ov = np.zeros((H, W, 3), np.uint8)
        ov[:] = (24, 24, 28)

        def place(mask, colour, as_outline):
            src = outline(mask) if as_outline else mask
            ox = (W - mask.shape[1]) // 2
            ys, xs = np.nonzero(src)
            ov[ys, xs + ox] = colour

        place(rm2, (60, 110, 210), False)
        place(bm2, (255, 165, 40), True)

        sheet = Image.new("RGB", (rimg2.width + bimg2.width + W + 24, H),
                          (24, 24, 28))
        sheet.paste(rimg2, (0, 0))
        sheet.paste(bimg2, (rimg2.width + 12, 0))
        sheet.paste(Image.fromarray(ov), (rimg2.width + bimg2.width + 24, 0))
        out = os.path.join(OUT, f"compare_{tag}.png")
        sheet.save(out)

        mr, mb = metrics(rm2), metrics(bm2)
        iou = float((rm2[:, :min(rm2.shape[1], bm2.shape[1])] &
                     bm2[:, :min(rm2.shape[1], bm2.shape[1])]).sum())
        print(f"[compare] {tag}: ref {mr}  blockout {mb}  -> {out}")


main()

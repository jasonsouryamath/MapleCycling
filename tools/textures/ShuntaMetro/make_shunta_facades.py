"""Shunta Metro facades: window atlas (4x4 variations) + four tileable facade tiles (8 cols x 16 floors, 16 m x 32 m).

Outputs: Facade_<Style>_{Albedo,Normal,Mask,Emission}.png (1024x2048), Window_Atlas_{Albedo,Emission}.png (1024x1024, 4x4 cells).
Styles: Concrete, Tile, Metal, Glass.   Mask map: R metallic, G AO, B 0, A smoothness.
"""
import os
import sys
import numpy as np
from shunta_tex_lib import *

CELL = 128
COLS, ROWS = 8, 16
LIT = {0, 1, 2, 3, 4, 5, 10, 11, 12}
PASTELS = [(210, 170, 160), (170, 190, 210), (200, 200, 150), (160, 200, 170), (205, 165, 195), (230, 215, 190)]


def cell_draw(v, style, A, E, Hh, S, Mt, rng, s=CELL):
    """Draw window variation v into PIL images (all size s x s). A/E RGB, others L."""
    k = s / 128.0
    R = lambda *a: tuple(int(round(x * k)) for x in a)
    dA, dE, dH, dS, dM = (ImageDraw.Draw(i) for i in (A, E, Hh, S, Mt))
    if style == "Glass":
        win = R(3, 6, 125, 122)
    elif style == "Metal":
        win = R(14, 26, 114, 98)
    else:
        win = R(18, 20, 110, 102)
    if v == 12:
        win = R(6, 30, 122, 120)
    if v == 13:
        win = R(48, 14, 80, 106)
    x0, y0, x1, y1 = win
    fr = max(2, int(4 * k))
    # frame (aluminium sash) + recess
    dA.rectangle((x0 - fr, y0 - fr, x1 + fr, y1 + fr), fill=(46, 48, 52))
    dM.rectangle((x0 - fr, y0 - fr, x1 + fr, y1 + fr), fill=170)
    dS.rectangle((x0 - fr, y0 - fr, x1 + fr, y1 + fr), fill=120)
    dH.rectangle((x0 - fr, y0 - fr, x1 + fr, y1 + fr), fill=150)
    dH.rectangle(win, fill=70)
    gl = (18, 20, 25)
    dA.rectangle(win, fill=gl)
    dS.rectangle(win, fill=235)
    dM.rectangle(win, fill=0)
    warm = (int(255), int(rng.integers(190, 226)), int(rng.integers(120, 170)))
    wx, wy = x1 - x0, y1 - y0

    def fillE(col, scale=1.0, box=win):
        dE.rectangle(box, fill=tuple(int(c * scale) for c in col))

    if v in (0, 10):
        fillE(warm, rng.uniform(0.55, 1.0))
        dE.rectangle((x0, y0, x1, y0 + int(wy * 0.12)), fill=tuple(int(c * 0.5) for c in warm))
    elif v == 1:
        fillE(warm, rng.uniform(0.6, 1.0))
        for yy in range(y0, y0 + int(wy * rng.uniform(0.45, 0.7)), max(3, int(6 * k))):
            dE.line((x0, yy, x1, yy), fill=(25, 18, 10), width=max(1, int(2 * k)))
            dA.line((x0, yy, x1, yy), fill=(205, 198, 182), width=max(1, int(3 * k)))
        dS.rectangle((x0, y0, x1, y0 + int(wy * 0.5)), fill=90)
    elif v == 2:
        fillE(warm, rng.uniform(0.5, 0.9))
        cc = PASTELS[int(rng.integers(len(PASTELS)))]
        cw = int(wx * rng.uniform(0.28, 0.4))
        for bx in ((x0, x0 + cw), (x1 - cw, x1)):
            dA.rectangle((bx[0], y0, bx[1], y1), fill=cc)
            dE.rectangle((bx[0], y0, bx[1], y1), fill=tuple(int(c * 0.35) for c in warm))
            for xx in range(bx[0], bx[1], max(3, int(5 * k))):
                dA.line((xx, y0, xx, y1), fill=tuple(int(c * 0.78) for c in cc), width=max(1, int(2 * k)))
        dS.rectangle((x0, y0, x1, y1), fill=40)
    elif v == 3:
        fillE((190, 225, 255), rng.uniform(0.6, 0.95))
        for yy in (y0 + int(wy * 0.18), y0 + int(wy * 0.3)):
            dE.line((x0, yy, x1, yy), fill=(255, 255, 255), width=max(2, int(5 * k)))
        for xx in range(x0 + int(wx * .2), x1, int(wx * .3)):
            dA.rectangle((xx, y0 + int(wy * .55), xx + int(wx * .16), y1), fill=(60, 64, 72))
            dE.rectangle((xx, y0 + int(wy * .55), xx + int(wx * .16), y1), fill=(70, 100, 130))
    elif v == 4:
        fillE((50, 100, 255), rng.uniform(0.35, 0.6))
        dE.rectangle((x0 + int(wx * .3), y0 + int(wy * .35), x0 + int(wx * .7), y0 + int(wy * .7)), fill=(90, 150, 255))
    elif v == 5:
        fillE(warm, rng.uniform(0.5, 0.9))
        dE.rectangle((x0 + int(wx * .05), y0 + int(wy * .4), x0 + int(wx * .3), y1), fill=(25, 15, 8))
        dE.ellipse((x0 + int(wx * .55), y0 + int(wy * .45), x0 + int(wx * .85), y1), fill=(30, 60, 20))
        dA.rectangle((x0 + int(wx * .05), y0 + int(wy * .4), x0 + int(wx * .3), y1), fill=(50, 40, 30))
    elif v == 6:
        for i in range(wy):
            t = i / max(1, wy - 1)
            dA.line((x0, y0 + i, x1, y0 + i), fill=(int(30 + 30 * t), int(46 + 36 * t), int(72 + 44 * t)))
        dS.rectangle(win, fill=250)
    elif v == 7:
        cc = PASTELS[int(rng.integers(len(PASTELS)))]
        dA.rectangle(win, fill=tuple(int(c * 0.7) for c in cc))
        for xx in range(x0, x1, max(3, int(5 * k))):
            dA.line((xx, y0, xx, y1), fill=tuple(int(c * 0.55) for c in cc), width=max(1, int(2 * k)))
        dS.rectangle(win, fill=30)
    elif v == 8:
        dA.rectangle(win, fill=(165, 160, 146))
        for yy in range(y0, y1, max(3, int(6 * k))):
            dA.line((x0, yy, x1, yy), fill=(110, 106, 96), width=max(1, int(2 * k)))
        dS.rectangle(win, fill=60)
    elif v in (9, 10):
        pass
    elif v == 11:
        fillE(warm, rng.uniform(0.4, 0.8))
        ry = y0 + int(wy * 0.55)
        dA.rectangle((x0 - fr, ry, x1 + fr, y1 + fr + int(8 * k)), fill=(40, 42, 46))
        dE.rectangle((x0, ry, x1, y1), fill=(0, 0, 0))
        for yy in range(ry, y1 + int(8 * k), max(4, int(8 * k))):
            dA.line((x0 - fr, yy, x1 + fr, yy), fill=(88, 90, 96), width=max(1, int(2 * k)))
        dM.rectangle((x0 - fr, ry, x1 + fr, y1 + fr), fill=180)
        dH.rectangle((x0 - fr, ry, x1 + fr, y1 + fr + int(8 * k)), fill=200)
    elif v == 12:
        fillE((255, 238, 205), rng.uniform(0.75, 1.0))
        dA.rectangle((x0 - fr, y0 - fr - int(10 * k), x1 + fr, y0 - fr), fill=(35, 35, 40))
    elif v == 13:
        fillE(warm, rng.uniform(0.3, 0.8)) if rng.random() < 0.5 else None
    elif v == 15:
        dA.rectangle(win, fill=(150, 152, 158))
        for yy in range(y0, y1, max(3, int(5 * k))):
            dA.line((x0, yy, x1, yy), fill=(100, 102, 108), width=max(1, int(2 * k)))
        dM.rectangle(win, fill=150)
        dS.rectangle(win, fill=110)
    # v == 14: blank dark glass (already drawn)
    # air-conditioner outdoor unit
    if v in (9, 10):
        ax0, ay0, ax1, ay1 = R(78, 84, 118, 120) if style != "Glass" else R(84, 90, 122, 122)
        dA.rectangle((ax0, ay0, ax1, ay1), fill=(188, 190, 194))
        dE.rectangle((ax0, ay0, ax1, ay1), fill=(0, 0, 0))
        cx, cy, r = (ax0 + ax1) // 2, (ay0 + ay1) // 2, int((ay1 - ay0) * 0.34)
        dA.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(60, 62, 66))
        for a in range(-r, r, max(2, int(3 * k))):
            dA.line((cx - r, cy + a, cx + r, cy + a), fill=(120, 122, 126), width=1)
        dH.rectangle((ax0, ay0, ax1, ay1), fill=220)
        dS.rectangle((ax0, ay0, ax1, ay1), fill=100)
        dM.rectangle((ax0, ay0, ax1, ay1), fill=60)


def wall_base(style, H, W, seed):
    ppm = CELL / 2.0   # 64 px / m
    rng = np.random.default_rng(seed)
    n1 = fbm(H, W, seed + 1, 1.5, 0.01, 0.4)
    n2 = fbm(H, W, seed + 2, 2.2, 0.0015, 0.02)
    stain = smoothstep(0.2, 1.6, fbm(H, W, seed + 3, 2.0, 0.001, 0.012))
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    ly = yy % CELL
    lx = xx % CELL
    metal = np.zeros((H, W), np.float32)
    smooth = np.full((H, W), 0.30, np.float32)
    if style == "Concrete":
        lum = 148 + 10 * n1 + 14 * n2 - 38 * stain * (0.4 + 0.6 * (ly / CELL))
        alb = lum[..., None] * np.array([1.0, 0.99, 0.96], np.float32)
        hgt = 0.4 * n1 + 0.3 * n2
        slab = np.clip((ly - 112) / 4.0, 0, 1) * np.clip((127 - ly) / 2.0, 0, 1)  # floor slab ledge
        alb = alb * (1 - 0.25 * slab[..., None])
        hgt = hgt + 2.0 * slab
        jt = np.clip(1 - np.abs(lx - 0) / 1.5, 0, 1) + np.clip(1 - np.abs(lx - 127) / 1.5, 0, 1)
        hgt = hgt - jt * 1.5
        alb = alb * (1 - 0.3 * jt[..., None])
    elif style == "Tile":
        tw = 12
        gx = (xx % tw) < 1.2
        gy = (yy % (tw // 2 + 2)) < 1.2
        grout = (gx | gy).astype(np.float32)
        cell_id = (np.floor(xx / tw) * 7 + np.floor(yy / (tw // 2 + 2)) * 13) % 11
        var = (cell_id / 11.0 - 0.5) * 16
        lum = 168 + var + 7 * n1 + 8 * n2 - 30 * stain * 0.6
        alb = lum[..., None] * np.array([1.0, 0.86, 0.70], np.float32)
        alb = alb * (1 - grout[..., None] * 0.45) + grout[..., None] * 0
        hgt = 0.3 * n1 - grout * 1.8
        smooth = 0.55 - 0.2 * stain - 0.4 * grout
        slab = np.clip((ly - 112) / 4.0, 0, 1)
        alb = alb * (1 - 0.35 * slab[..., None])
    elif style == "Metal":
        cor = np.cos(2 * np.pi * xx / 10.0)
        lum = 72 + 6 * cor + 7 * n1 + 6 * n2
        alb = lum[..., None] * np.array([0.92, 1.0, 1.18], np.float32)
        hgt = 1.2 * cor + 0.2 * n1
        metal = np.full((H, W), 0.75, np.float32) * (1 - 0.3 * stain)
        smooth = 0.5 - 0.2 * stain
        seam = np.clip(1 - np.abs(ly - 8) / 1.5, 0, 1)
        alb = alb * (1 - 0.4 * seam[..., None])
        hgt = hgt - seam * 1.5
    else:  # Glass curtain wall frame
        lum = 54 + 4 * n1
        alb = lum[..., None] * np.array([0.95, 1.0, 1.06], np.float32)
        hgt = 0.2 * n1
        metal = np.full((H, W), 0.8, np.float32)
        smooth = np.full((H, W), 0.5, np.float32)
    ao = np.clip(1.0 - 0.2 * stain - 0.05 * np.clip(-n1, 0, 3), 0, 1)
    return alb, hgt, metal, smooth, ao


def lit_pattern(style, rng):
    """returns a (ROWS, COLS) array of variation indices"""
    unlit_pool = [6, 6, 7, 8, 9, 14, 15, 13]
    lit_pool = [0, 0, 1, 2, 2, 3, 4, 5, 10, 11]
    out = np.zeros((ROWS, COLS), np.int32)
    floor_bias = rng.uniform(0.2, 0.8, ROWS)
    col_bias = rng.uniform(0.3, 0.8, COLS)
    for r in range(ROWS):
        for c in range(COLS):
            if style == "Glass":
                lit = rng.random() < floor_bias[r] * 0.9
                out[r, c] = (3 if (lit and rng.random() < 0.8) else (0 if lit else (6 if rng.random() < 0.8 else 14)))
                continue
            lit = rng.random() < (floor_bias[r] * 0.5 + col_bias[c] * 0.5) * 0.85
            if lit:
                out[r, c] = lit_pool[rng.integers(len(lit_pool))]
            else:
                out[r, c] = unlit_pool[rng.integers(len(unlit_pool))]
            if rng.random() < 0.03:
                out[r, c] = 12
    return out


def pil(a):
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def make_style(style, seed):
    H, W = ROWS * CELL, COLS * CELL
    rng = np.random.default_rng(seed)
    alb, hgt, metal, smooth, ao = wall_base(style, H, W, seed)
    emi = np.zeros((H, W, 3), np.float32)
    hh = hgt * 18 + 128
    pat = lit_pattern(style, rng)
    mt = metal * 255
    sm = smooth * 255
    for r in range(ROWS):
        for c in range(COLS):
            sl = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            A = pil(alb[sl]); E = pil(emi[sl]); Hh = pil(hh[sl]); S = pil(sm[sl]); M = pil(mt[sl])
            cell_draw(int(pat[r, c]), style, A, E, Hh, S, M, rng)
            alb[sl] = np.asarray(A, np.float32); emi[sl] = np.asarray(E, np.float32)
            hh[sl] = np.asarray(Hh, np.float32); sm[sl] = np.asarray(S, np.float32); mt[sl] = np.asarray(M, np.float32)
    # recess AO: darken where the height drops
    hl = lowpass(hh, 3.0)
    ao = np.clip(ao * (1 - np.clip((hl - hh) / 90.0, 0, 0.5)), 0, 1)
    nrm = height_to_normal(hh / 40.0, 0.7)
    d = out_dir()
    save_png(os.path.join(d, "Facade_%s_Albedo.png" % style), alb.clip(0, 255).astype(np.uint8))
    save_png(os.path.join(d, "Facade_%s_Normal.png" % style), to8(nrm))
    mask = np.stack([mt / 255.0, ao, np.zeros_like(ao), sm / 255.0], -1)
    save_png(os.path.join(d, "Facade_%s_Mask.png" % style), to8(mask))
    save_png(os.path.join(d, "Facade_%s_Emission.png" % style), emi.clip(0, 255).astype(np.uint8))


def make_atlas():
    S = 256
    rng = np.random.default_rng(4242)
    A = Image.new("RGB", (S * 4, S * 4), (120, 120, 120))
    E = Image.new("RGB", (S * 4, S * 4), (0, 0, 0))
    for v in range(16):
        r, c = divmod(v, 4)
        a = Image.new("RGB", (S, S), (140, 140, 140)); e = Image.new("RGB", (S, S), (0, 0, 0))
        h = Image.new("L", (S, S), 128); sm = Image.new("L", (S, S), 80); mt = Image.new("L", (S, S), 0)
        cell_draw(v, "Concrete", a, e, h, sm, mt, rng, s=S)
        A.paste(a, (c * S, r * S)); E.paste(e, (c * S, r * S))
    d = out_dir()
    A.save(os.path.join(d, "Window_Atlas_Albedo.png")); E.save(os.path.join(d, "Window_Atlas_Emission.png"))
    print("wrote window atlas")


if __name__ == "__main__":
    which = sys.argv[1:] or ["Concrete", "Tile", "Metal", "Glass", "Atlas"]
    for i, w in enumerate(which):
        if w == "Atlas":
            make_atlas()
        else:
            make_style(w, 1000 + 37 * i)

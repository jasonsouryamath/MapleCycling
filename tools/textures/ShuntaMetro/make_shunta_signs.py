"""Shunta Metro neon sign atlas, 4096 x 2048.
Left 2048x2048  : 8 x 2 vertical blade signs, cell 256 x 1024 (tategaki).
Right 2048x2048 : 2 x 8 horizontal boards,   cell 1024 x 256.
Outputs Sign_Atlas_Albedo.png (dark housings) and Sign_Atlas_Emission.png (saturated, bloom-friendly neon).
Cell index: vertical i = 0..15 (col = i % 8, row = i // 8); horizontal j = 0..15 (col = j % 2, row = j // 2).
"""
import os
import numpy as np
from shunta_tex_lib import *

COLORS = [(255, 40, 150), (0, 215, 255), (255, 165, 30), (130, 95, 255), (40, 255, 170), (255, 60, 60), (255, 235, 110), (90, 255, 90)]
VERT = ["ラーメン", "居酒屋", "カラオケ", "薬局", "焼肉", "寿司", "麻雀", "酒場", "純喫茶", "旅館", "天ぷら", "ホテル", "営業中", "整骨院", "歯科", "古書店"]
HORZ = ["24時間営業", "渋谷", "パチンコ", "ゲームセンター", "BAR", "COFFEE", "お台場", "カフェ", "東京", "中華そば", "焼き鳥", "SALE", "OPEN", "MAHJONG", "ARROW", "BARS"]


def glow_cell(w, h, draw_fn, col_a, col_b, seed):
    """returns (albedo RGB uint8, emission RGB float 0..255)"""
    mask_text = Image.new("L", (w, h), 0)
    mask_frame = Image.new("L", (w, h), 0)
    draw_fn(ImageDraw.Draw(mask_text), ImageDraw.Draw(mask_frame))
    mt = np.asarray(mask_text, np.float32) / 255.0
    mf = np.asarray(mask_frame, np.float32) / 255.0
    halo_t = np.asarray(mask_text.filter(ImageFilter.GaussianBlur(10)), np.float32) / 255.0
    halo_f = np.asarray(mask_frame.filter(ImageFilter.GaussianBlur(8)), np.float32) / 255.0
    core_t = np.asarray(mask_text.filter(ImageFilter.GaussianBlur(3)), np.float32) / 255.0
    core_f = np.asarray(mask_frame.filter(ImageFilter.GaussianBlur(2.5)), np.float32) / 255.0
    ca = np.array(col_a, np.float32); cb = np.array(col_b, np.float32); wh = np.array([255, 255, 255], np.float32)
    # neon tube: saturated body, whiter core
    text_col = ca + (wh - ca) * (0.22 * core_t[..., None] ** 2)
    frame_col = cb + (wh - cb) * (0.22 * core_f[..., None] ** 2)
    emi = mt[..., None] * text_col + mf[..., None] * frame_col
    emi = emi + 0.30 * halo_t[..., None] * ca + 0.30 * halo_f[..., None] * cb
    emi = np.clip(emi, 0, 255)
    panel = np.array([22, 23, 29], np.float32) + 6 * (np.random.default_rng(seed).random((h, w, 1)).astype(np.float32))
    alb = panel * np.ones((h, w, 3), np.float32)
    tube = np.clip(mt + mf, 0, 1)[..., None]
    alb = alb * (1 - tube) + np.array([120, 120, 125], np.float32) * tube   # unlit glass tubing
    # metal frame rim
    rim = np.zeros((h, w), np.float32); rim[:6, :] = 1; rim[-6:, :] = 1; rim[:, :6] = 1; rim[:, -6:] = 1
    alb = alb * (1 - rim[..., None]) + np.array([58, 60, 68], np.float32) * rim[..., None]
    return alb, emi


def vertical(i):
    w, h = 256, 1024
    word = VERT[i]
    ca = COLORS[i % len(COLORS)]
    cb = COLORS[(i * 3 + 2) % len(COLORS)]
    n = len(word)
    size = int(min(190, 860 / n))
    f = font("YuGothB.ttc", size)

    def d(dt, df):
        df.rectangle((16, 16, w - 17, h - 17), outline=255, width=7)
        df.rectangle((28, 28, w - 29, 52), fill=255)
        y = 70 + (860 - n * size) // 2
        for ch in word:
            bb = dt.textbbox((0, 0), ch, font=f)
            dt.text(((w - (bb[2] - bb[0])) // 2 - bb[0], y - bb[1]), ch, font=f, fill=255)
            y += size
        df.rectangle((28, h - 70, w - 29, h - 56), fill=255)
    return glow_cell(w, h, d, ca, cb, 10 + i)


def horizontal(j):
    w, h = 1024, 256
    word = HORZ[j]
    ca = COLORS[(j * 5 + 1) % len(COLORS)]
    cb = COLORS[(j * 2 + 3) % len(COLORS)]
    if word == "ARROW":
        def d(dt, df):
            df.rectangle((16, 16, w - 17, h - 17), outline=255, width=7)
            dt.rectangle((90, 100, 700, 156), fill=255)
            dt.polygon([(700, 50), (700, 206), (900, 128)], fill=255)
        return glow_cell(w, h, d, ca, cb, 100 + j)
    if word == "BARS":
        def d(dt, df):
            for y in (50, 112, 174):
                dt.rectangle((60, y, w - 61, y + 28), fill=255)
            df.rectangle((16, 16, w - 17, h - 17), outline=255, width=5)
        return glow_cell(w, h, d, ca, cb, 100 + j)
    n = len(word)
    wide = sum(1 if ord(c) < 0x2000 else 2 for c in word)
    size = int(min(170, 930 / (wide * 0.55 + 0.001) / 1.0))
    size = min(size, 170)
    f = font("YuGothB.ttc", size)

    def d(dt, df):
        df.rectangle((16, 16, w - 17, h - 17), outline=255, width=7)
        bb = dt.textbbox((0, 0), word, font=f)
        tw, th = bb[2] - bb[0], bb[3] - bb[1]
        dt.text(((w - tw) // 2 - bb[0], (h - th) // 2 - bb[1]), word, font=f, fill=255)
    return glow_cell(w, h, d, ca, cb, 100 + j)


def main():
    A = np.zeros((2048, 4096, 3), np.float32)
    E = np.zeros((2048, 4096, 3), np.float32)
    for i in range(16):
        a, e = vertical(i)
        c, r = i % 8, i // 8
        A[r * 1024:(r + 1) * 1024, c * 256:(c + 1) * 256] = a
        E[r * 1024:(r + 1) * 1024, c * 256:(c + 1) * 256] = e
    for j in range(16):
        a, e = horizontal(j)
        c, r = j % 2, j // 2
        A[r * 256:(r + 1) * 256, 2048 + c * 1024:2048 + (c + 1) * 1024] = a
        E[r * 256:(r + 1) * 256, 2048 + c * 1024:2048 + (c + 1) * 1024] = e
    d = out_dir()
    save_png(os.path.join(d, "Sign_Atlas_Albedo.png"), A.clip(0, 255).astype(np.uint8))
    save_png(os.path.join(d, "Sign_Atlas_Emission.png"), E.clip(0, 255).astype(np.uint8))


if __name__ == "__main__":
    main()

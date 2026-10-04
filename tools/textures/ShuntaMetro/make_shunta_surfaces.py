"""Shunta Metro ground / sidewalk surfaces.
Ground_Wet_*   2048^2 tile = 32 m (dark wet urban ground) + Ground_Detail (HDRP detail map, 2 m tile, anti-repetition)
Sidewalk_Brick_* 1024^2 tile = 2 m (running-bond pavers), Tactile_* 512^2 tile = 0.6 m (yellow Tokyo guidance blocks).
Each: _Albedo, _Normal, _Mask (R metal, G AO, B detail mask, A smoothness).
"""
import os
import numpy as np
from shunta_tex_lib import *


def save_set(name, alb, nrm, ao, smooth, detail=None, metal=None):
    d = out_dir()
    h, w = ao.shape
    m = np.stack([metal if metal is not None else np.zeros_like(ao), ao, detail if detail is not None else np.zeros_like(ao), smooth], -1)
    save_png(os.path.join(d, name + "_Albedo.png"), np.clip(alb, 0, 255).astype(np.uint8))
    save_png(os.path.join(d, name + "_Normal.png"), to8(nrm))
    save_png(os.path.join(d, name + "_Mask.png"), to8(m))


def ground():
    S = 2048
    fine = fbm(S, S, 11, 1.2, 0.03, 0.5)
    mid = fbm(S, S, 12, 2.0, 0.004, 0.06)
    low = fbm(S, S, 13, 2.4, 0.0005, 0.006)
    huge = fbm(S, S, 14, 2.6, 0.0002, 0.002)
    lum = 44 + 8 * fine * 0.6 + 7 * mid + 9 * low + 6 * huge
    lum = lum + smoothstep(1.8, 2.8, fine) * 20
    alb = lum[..., None] * np.array([1.0, 1.0, 1.05], np.float32)
    # expansion joints: a faint slab grid with irregular offsets (4 m slabs)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    jx = np.clip(1 - np.abs((xx % 256) - 0) / 1.5, 0, 1) + np.clip(1 - np.abs((xx % 256) - 255) / 1.5, 0, 1)
    jy = np.clip(1 - np.abs(((yy + 90 * np.floor(xx / 256)) % 256)) / 1.5, 0, 1)
    jt = np.clip(jx + jy, 0, 1)
    alb = alb * (1 - 0.35 * jt[..., None])
    hgt = 0.5 * fine + 0.4 * mid - jt * 2
    P = 0.8 * low + 0.3 * mid + 0.5 * huge
    thr = np.quantile(P[::6, ::6], 0.7)
    pud = smoothstep(thr - 0.05, thr + 0.1, P)
    damp = smoothstep(thr - 0.8, thr, P)
    sm = 0.35 + 0.25 * damp
    sm = sm * (1 - pud) + 0.96 * pud
    alb = alb * (1 - 0.25 * damp[..., None]) * (1 - 0.3 * pud[..., None])
    nrm = height_to_normal(hgt, 0.25, flat=pud * 0.9)
    ao = np.clip(1 - 0.5 * jt - 0.05 * np.clip(-fine, 0, 3), 0, 1)
    save_set("Ground_Wet", alb, nrm, ao, np.clip(sm, 0.05, 0.99), detail=np.clip(pud + 0.2 * damp, 0, 1))
    # detail map for the ground (HDRP: R albedo, G normalY, B smoothness, A normalX), 2 m tile at 512 px
    D = 512
    f1 = fbm(D, D, 21, 1.2, 0.03, 0.5)
    f2 = fbm(D, D, 22, 2.0, 0.01, 0.12)
    n = height_to_normal(0.7 * f1 + 0.5 * f2, 0.45)
    det = np.stack([0.5 + 0.12 * f2.clip(-2, 2) / 2, n[..., 1], 0.5 + 0.08 * f1.clip(-2, 2) / 2, n[..., 0]], -1)
    save_png(os.path.join(out_dir(), "Ground_Detail.png"), to8(det))


def brick():
    S = 1024
    bw, bh = 128, 64
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    row = (yy // bh).astype(np.int32)
    xo = xx + (row % 2) * (bw // 2)
    col = ((xo // bw).astype(np.int32)) % (S // bw)
    lx, ly = xo % bw, yy % bh
    e = np.minimum(np.minimum(lx, bw - 1 - lx), np.minimum(ly, bh - 1 - ly))
    rng = np.random.default_rng(31)
    tab = rng.random((S // bh, S // bw))
    base = tab[row, col]
    palette = np.array([[132, 108, 96], [118, 104, 98], [146, 122, 104], [104, 96, 92]], np.float32)
    idx = np.minimum((base * 4).astype(np.int32), 3)
    colr = palette[idx] * (0.9 + 0.2 * rng.random((S // bh, S // bw))[row, col][..., None])
    fine = fbm(S, S, 32, 1.3, 0.02, 0.5)
    mid = fbm(S, S, 33, 2.0, 0.003, 0.05)
    grout = (e < 3).astype(np.float32)
    gsoft = np.clip(1 - e / 4.0, 0, 1)
    alb = colr * (1 + 0.07 * fine[..., None] + 0.05 * mid[..., None])
    alb = alb * (1 - gsoft[..., None]) + np.array([66, 63, 60], np.float32) * gsoft[..., None]
    cham = smoothstep(3, 9, e)
    hgt = cham * 3 + 0.5 * fine + 0.4 * mid + (base - 0.5) * 1.5
    sm = 0.5 + 0.12 * mid - 0.25 * gsoft
    ao = 1 - 0.55 * gsoft
    nrm = height_to_normal(hgt, 0.5)
    save_set("Sidewalk_Brick", alb, nrm, ao, np.clip(sm, 0.05, 0.9))


def tactile():
    S = 512
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    bx, by = xx % 256, yy % 256
    e = np.minimum(np.minimum(bx, 255 - bx), np.minimum(by, 255 - by))
    block = np.clip((e - 3) / 2.0, 0, 1)
    # four raised bars per block along V (travel direction)
    bars = np.zeros((S, S), np.float32)
    for k in range(4):
        cx = 32 + k * 64 + 0.0
        dx = np.abs(bx - cx)
        along = np.minimum(by, 255 - by)
        cap = np.clip((along - 14) / 3.0, 0, 1)
        bars = np.maximum(bars, np.clip((14 - dx) / 4.0, 0, 1) * cap)
    # rounded profile
    prof = np.sqrt(np.clip(bars, 0, 1))
    fine = fbm(S, S, 41, 1.2, 0.03, 0.5)
    mid = fbm(S, S, 42, 2.0, 0.004, 0.06)
    wear = smoothstep(0.3, 1.8, mid)
    yel = np.array([236, 190, 22], np.float32)
    grime = np.array([120, 104, 70], np.float32)
    alb = yel * (0.9 + 0.05 * fine[..., None]) * (1 - 0.25 * prof[..., None] * 0)
    alb = alb * (1 - 0.35 * wear[..., None]) + grime * 0.35 * wear[..., None]
    gap = 1 - block
    alb = alb * (1 - gap[..., None]) + np.array([70, 66, 60], np.float32) * gap[..., None]
    hgt = prof * 6 + block * 1.0 + 0.3 * fine
    nrm = height_to_normal(hgt, 0.7)
    sm = 0.45 - 0.2 * wear + 0.1 * prof
    ao = 1 - 0.5 * gap
    save_set("Tactile", alb, nrm, ao, np.clip(sm, 0.05, 0.9))


if __name__ == "__main__":
    ground(); brick(); tactile()

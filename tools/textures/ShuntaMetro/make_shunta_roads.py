"""Shunta Metro road PBR sets. One tile = 9 m (road width, U) x 18 m (along the road, V).

Outputs (Assets/Textures/ShuntaMetro, or $SHUNTA_TEX_OUT):
  Road_<Kind>_Albedo.png  RGB  sRGB
  Road_<Kind>_Normal.png  RGB  tangent normal, +Y up (Unity)
  Road_<Kind>_Mask.png    RGBA HDRP mask map: R metallic, G AO, B detail mask (puddles), A smoothness
  Road_Crosswalk_Albedo.png  RGBA (A = paint coverage, alpha-clip decal), Road_Ripple_Detail.png (HDRP detail map AG normal)
Kinds: Wet, Dry, Tunnel, Expressway, Bridge.   usage: python make_shunta_roads.py [kind ...]
"""
import os
import sys
import numpy as np
from shunta_tex_lib import *

ROAD_W, ROAD_L = 9.0, 18.0

CFG = {
    "Wet":        dict(w=2048, base=44, tint=(1.00, 1.00, 1.06), grain=15, sm=0.26, puddle=0.085, cracks=11, patches=6, centre="white", track=1.0, manhole=True, drains=True, seed=100),
    "Dry":        dict(w=2048, base=58, tint=(1.00, 0.99, 0.97), grain=17, sm=0.14, puddle=0.045, cracks=15, patches=7, centre="yellow", track=0.8, manhole=True, drains=True, seed=200),
    "Tunnel":     dict(w=1024, base=116, tint=(1.00, 0.98, 0.93), grain=10, sm=0.22, puddle=0.04, cracks=6, patches=2, centre="white", track=0.8, joints=True, soot=True, seed=300),
    "Expressway": dict(w=1024, base=50, tint=(1.00, 1.00, 1.03), grain=10, sm=0.18, puddle=0.02, drains=True, cracks=3, patches=2, centre="white", track=0.9, cats=True, seed=400),
    "Bridge":     dict(w=1024, base=56, tint=(1.00, 1.00, 1.02), grain=10, sm=0.18, puddle=0.035, drains=True, cracks=2, patches=1, centre="white", track=0.8, bridge=True, seed=500),
}


def lm(d, half, ppm):
    """antialiased line coverage from a signed distance in metres"""
    return np.clip((half - np.abs(d)) * ppm + 0.5, 0.0, 1.0)


def make_road(kind):
    c = CFG[kind]
    W = c["w"]
    H = W * 2
    ppm = W / ROAD_W
    s = c["seed"]
    X = ((np.arange(W, dtype=np.float32) + 0.5) / ppm)[None, :]
    Y = ((np.arange(H, dtype=np.float32) + 0.5) / ppm)[:, None]
    rng = np.random.default_rng(s)
    hi = ppm > 150

    fine = fbm(H, W, s + 1, 1.2, 0.03, 0.5)
    mid = fbm(H, W, s + 2, 2.0, 0.004, 0.06)
    low = fbm(H, W, s + 3, 2.6, 0.0004, 0.004)
    wear = fbm(H, W, s + 4, 1.5, 0.002, 0.3)
    fine2 = fbm(H, W, s + 5, 1.0, 0.05, 0.5)
    g = c["grain"]

    # ---------------------------------------------------------------- base asphalt
    lum = c["base"] + g * (0.55 * fine + 0.35 * mid + 0.5 * low)
    stones = smoothstep(1.7, 2.7, fine + 0.3 * mid)
    lum = lum + stones * g * 2.0
    hgt = 0.6 * fine + 0.5 * mid + 0.15 * low + stones * 0.8
    ao = 1.0 - 0.07 * np.clip(-fine, 0, 3)
    smooth = c["sm"] + 0.05 * mid + 0.03 * fine
    metal = np.zeros((H, W), np.float32)
    tint = np.array(c["tint"], np.float32)[None, None, :]

    # concrete slab panels (tunnel)
    if c.get("joints"):
        ix = np.floor(X / 4.5).astype(np.int32) % 2
        iy = np.floor(Y / 6.0).astype(np.int32) % 3
        pid = (ix * 3 + iy)
        off = np.array([0, 4, -5, -3, 3, 6], np.float32)[pid]
        lum = lum + off
        dist = np.minimum(Y % 6.0, 6.0 - (Y % 6.0))
        joint = lm(dist, 0.012, ppm)
        hgt = hgt - joint * 2.5
        lum = lum * (1 - 0.5 * joint)
        ao = ao * (1 - 0.6 * joint)
        # saw-cut at the lane edge
        sc = lm(X - 4.5, 0.01, ppm)
        hgt = hgt - sc * 1.2
        lum = lum * (1 - 0.3 * sc)
    if c.get("soot"):
        xe = np.minimum(X, ROAD_W - X)
        lum = lum * (1 - 0.38 * np.exp(-(xe / 0.8) ** 2))

    # ---------------------------------------------------------------- repaired patches with seams
    patch_total = np.zeros((H, W), np.float32)
    seam_total = np.zeros((H, W), np.float32)
    for _ in range(c["patches"]):
        cx, cy = rng.uniform(1.0, 8.0), rng.uniform(0, ROAD_L)
        pw, pl = rng.uniform(1.0, 5.5), rng.uniform(1.5, 6.0)
        dx = X - cx
        dy = ((Y - cy + ROAD_L / 2) % ROAD_L) - ROAD_L / 2
        inner = np.minimum(pw / 2 - np.abs(dx), pl / 2 - np.abs(dy))  # >0 inside
        inside = np.clip(inner * ppm + 0.5, 0, 1)
        seam = np.clip(1.0 - np.abs(inner - 0.0) * ppm / 2.2, 0, 1)
        lum = lum + inside * (rng.uniform(-15, 8) + 0.25 * g * fine2)
        hgt = hgt + inside * 0.35 - seam * 1.4
        ao = ao * (1 - 0.45 * seam)
        patch_total = np.maximum(patch_total, inside)
        seam_total = np.maximum(seam_total, seam)
    lum = lum - seam_total * 20

    # ---------------------------------------------------------------- cracks (tar-sealed hairlines + a few wide ones)
    pls = []
    for i in range(c["cracks"]):
        pls.append(random_crack(rng, W, H, int(rng.integers(30, 90)), 7 if hi else 4, 0.5))
        if rng.random() < 0.5:
            pls.append(random_crack(rng, W, H, int(rng.integers(10, 40)), 6 if hi else 3, 0.7))
    crack = wrap_lines(W, H, pls, 3 if hi else 2)
    crack = np.clip(lowpass(crack, 0.9) * 1.8, 0, 1)
    lum = lum * (1 - 0.55 * crack)
    hgt = hgt - crack * 1.6
    ao = ao * (1 - 0.55 * crack)

    # ---------------------------------------------------------------- tyre wear lanes
    wob = fbm1d(H, s + 9, 2.5, 0.0, 0.0012)[:, None] * 0.07
    track = np.zeros((H, W), np.float32)
    for lc in (2.25, 6.75):
        for dx in (-0.8, 0.8):
            d = X + wob - (lc + dx)
            track = np.maximum(track, np.exp(-0.5 * (d / 0.28) ** 2))
    track = track * c["track"] * (0.75 + 0.25 * np.clip(mid * 0.5 + 0.5, 0, 1))
    lum = lum - track * 9
    hgt = hgt - track * 0.9
    smooth = smooth + track * 0.06
    # oil drips down the lane centres
    oil_blob = smoothstep(0.7, 1.7, fbm(H, W, s + 6, 2.2, 0.003, 0.05))
    oil = np.zeros((H, W), np.float32)
    for lc in (2.25, 6.75):
        oil = np.maximum(oil, np.exp(-0.5 * ((X + wob - lc) / 0.09) ** 2))
    oil = oil * oil_blob
    lum = lum * (1 - 0.30 * oil)
    smooth = smooth + 0.07 * oil

    # ---------------------------------------------------------------- road markings
    paint_w = np.zeros((H, W), np.float32)
    paint_y = np.zeros((H, W), np.float32)
    paint_w = np.maximum(paint_w, np.maximum(lm(X - 0.38, 0.075, ppm), lm(X - (ROAD_W - 0.38), 0.075, ppm)))
    if c["centre"] == "yellow":
        paint_y = np.maximum(paint_y, lm(X - 4.5, 0.06, ppm))
    else:
        dm = np.clip(np.minimum(Y % 6.0, 3.0 - (Y % 6.0)) * ppm + 0.5, 0, 1)
        paint_w = np.maximum(paint_w, lm(X - 4.5, 0.075, ppm) * dm)
    cov = smoothstep(-1.5, 0.15, wear + 0.4 * fine) * (1 - 0.5 * track)
    paint_w = paint_w * cov
    paint_y = paint_y * cov
    paint = np.maximum(paint_w, paint_y)
    alb = lum[..., None] * tint
    wcol = np.array([214, 214, 206], np.float32)
    ycol = np.array([224, 176, 34], np.float32)
    alb = alb * (1 - paint_w[..., None] * 0.93) + wcol * paint_w[..., None] * 0.93
    alb = alb * (1 - paint_y[..., None] * 0.93) + ycol * paint_y[..., None] * 0.93
    smooth = smooth * (1 - paint) + (c["sm"] * 0.85 + 0.05) * paint
    hgt = hgt + paint * 0.5

    # ---------------------------------------------------------------- cat-eyes
    if c.get("cats"):
        for k in range(3):
            cy = k * 6.0 + 4.5
            d = np.sqrt((X - 4.5) ** 2 + (((Y - cy + 9) % 18) - 9) ** 2)
            ce = np.clip((0.07 - d) * ppm + 0.5, 0, 1)
            alb = alb * (1 - ce[..., None]) + np.array([236, 236, 230], np.float32) * ce[..., None]
            smooth = smooth * (1 - ce) + 0.9 * ce
            hgt = hgt + ce * 2.5

    # ---------------------------------------------------------------- bridge deck: finger expansion joint + grooves
    if c.get("bridge"):
        grooves = np.cos(2 * np.pi * X / 0.035)
        gw = np.clip(track * 1.4, 0, 1)
        hgt = hgt + grooves * 0.28 * gw
        yd = ((Y + ROAD_L / 2) % ROAD_L) - ROAD_L / 2   # 0 at the tile seam
        tri = np.abs(((X / 0.4) % 1.0) - 0.5) * 2  # 0..1 zig-zag
        edge = 0.20 + 0.09 * tri
        plate = np.clip((edge - np.abs(yd)) * ppm + 0.5, 0, 1)
        gap = lm(np.abs(yd) - edge + 0.03, 0.012, ppm) * (1 - plate)
        alb = alb * (1 - plate[..., None]) + np.array([96, 98, 104], np.float32) * (0.8 + 0.2 * fine2[..., None] * 0.3) * plate[..., None]
        metal = np.maximum(metal, plate * 0.85)
        smooth = smooth * (1 - plate) + 0.55 * plate
        hgt = hgt + plate * 2.0
        ao = ao * (1 - 0.7 * gap)
        alb = alb * (1 - 0.8 * gap[..., None])
        # bolts
        bx = ((X / 0.3) % 1.0 - 0.5) * 0.3
        for by in (-0.11, 0.11):
            d = np.sqrt(bx ** 2 + (yd - by) ** 2)
            b = np.clip((0.028 - d) * ppm + 0.5, 0, 1) * plate
            alb = alb * (1 - 0.5 * b[..., None]) + 40 * 0.5 * b[..., None]
            hgt = hgt + b * 1.5
        # mid-tile mastic seam
        ms = lm(((Y - 9.0)), 0.012, ppm)
        alb = alb * (1 - 0.5 * ms[..., None])
        hgt = hgt - ms * 1.4

    # ---------------------------------------------------------------- manhole + utility cover
    if c.get("manhole"):
        cx, cy = 6.3, 7.0
        dx, dy = X - cx, Y - cy
        r = np.sqrt(dx * dx + dy * dy)
        sq = np.maximum(np.abs(dx), np.abs(dy))
        frame = np.clip((0.55 - sq) * ppm + 0.5, 0, 1)
        fseam = np.clip(1 - np.abs(0.55 - sq) * ppm / 2.0, 0, 1)
        lum2 = alb.mean(-1)
        alb = alb * (1 - 0.18 * frame[..., None])
        hgt = hgt - fseam * 1.2
        ao = ao * (1 - 0.4 * fseam)
        cover = np.clip((0.33 - r) * ppm + 0.5, 0, 1)
        ring = np.clip((0.37 - r) * ppm + 0.5, 0, 1) - cover
        kk = 2 * np.pi / 0.045
        patt = 0.5 + 0.5 * np.cos(kk * (dx + dy)) * np.cos(kk * (dx - dy))
        iron = np.array([52, 50, 48], np.float32)[None, None, :] * (0.8 + 0.25 * fine[..., None] * 0.3 + 0.35 * patt[..., None])
        rust = smoothstep(1.2, 2.2, mid + 0.4 * fine)[..., None] * np.array([70, 30, 12], np.float32) * 0.45
        alb = alb * (1 - cover[..., None]) + (iron + rust) * cover[..., None]
        alb = alb * (1 - ring[..., None] * 0.7)
        hgt = hgt + cover * (0.8 + 1.2 * patt) - ring * 2.6
        ao = ao * (1 - 0.8 * ring)
        metal = np.maximum(metal, cover * 0.6)
        smooth = smooth * (1 - cover) + (0.48 + 0.2 * (c["sm"] > 0.4)) * cover
        # utility cover
        cx2, cy2 = 1.35, 13.2
        dx2, dy2 = X - cx2, Y - cy2
        rect = np.clip((np.minimum(0.32 - np.abs(dx2), 0.22 - np.abs(dy2))) * ppm + 0.5, 0, 1)
        rim = np.clip(1 - np.abs(np.minimum(0.32 - np.abs(dx2), 0.22 - np.abs(dy2))) * ppm / 2.0, 0, 1)
        stripes = 0.5 + 0.5 * np.cos(2 * np.pi * (dx2 + dy2) / 0.032)
        alb = alb * (1 - rect[..., None]) + (np.array([62, 60, 57], np.float32) * (0.75 + 0.4 * stripes[..., None])) * rect[..., None]
        hgt = hgt + rect * (0.6 + stripes) - rim * 2.0
        ao = ao * (1 - 0.7 * rim)
        metal = np.maximum(metal, rect * 0.55)
        smooth = smooth * (1 - rect) + 0.45 * rect

    # ---------------------------------------------------------------- extra manholes (2026-10-04: a cover every few metres, never the same lane twice in a row)
    if c.get("manhole"):
        for (mx, my, mr) in ((2.6, 1.8, 0.30), (5.7, 11.0, 0.33), (7.7, 16.2, 0.28)):
            dx, dy = X - mx, ((Y - my + ROAD_L / 2) % ROAD_L) - ROAD_L / 2
            rr = np.sqrt(dx * dx + dy * dy)
            cover = np.clip((mr - rr) * ppm + 0.5, 0, 1)
            ring = np.clip((mr + 0.045 - rr) * ppm + 0.5, 0, 1) - cover
            kk = 2 * np.pi / 0.04
            patt = 0.5 + 0.5 * np.cos(kk * (dx + dy)) * np.cos(kk * (dx - dy))
            iron = np.array([50, 48, 46], np.float32)[None, None, :] * (0.8 + 0.35 * patt[..., None])
            alb = alb * (1 - cover[..., None]) + iron * cover[..., None]
            alb = alb * (1 - ring[..., None] * 0.75)
            hgt = hgt + cover * (0.7 + 1.0 * patt) - ring * 2.4
            ao = ao * (1 - 0.8 * ring)
            metal = np.maximum(metal, cover * 0.55)
            smooth = smooth * (1 - cover) + 0.42 * cover

    # sewer drains: slotted iron grates in a concrete frame at the kerbs, with a dark wet stain running down the gutter
    if c.get("drains"):
        for (gx, gy) in ((0.62, 3.6), (ROAD_W - 0.62, 9.8), (0.62, 14.9), (ROAD_W - 0.62, 1.2)):
            dx, dy = X - gx, ((Y - gy + ROAD_L / 2) % ROAD_L) - ROAD_L / 2
            hx, hy = 0.34, 0.24
            inner = np.minimum(hx - np.abs(dx), hy - np.abs(dy))
            body = np.clip(inner * ppm + 0.5, 0, 1)
            frame = np.clip((inner + 0.05) * ppm + 0.5, 0, 1) - body
            slot = (np.cos(2 * np.pi * dx / 0.075) > 0.25).astype(np.float32) * np.clip((inner - 0.035) * ppm + 0.5, 0, 1)
            bars = body * (1 - slot)
            alb = alb * (1 - frame[..., None] * 0.8) + np.array([88, 86, 82], np.float32) * frame[..., None] * 0.8
            alb = alb * (1 - body[..., None]) + np.array([46, 44, 42], np.float32) * bars[..., None] + np.array([6, 6, 7], np.float32) * (body - bars)[..., None]
            hgt = hgt + bars * 1.4 - slot * 3.0 + frame * 0.5
            ao = ao * (1 - 0.85 * slot)
            metal = np.maximum(metal, bars * 0.5)
            smooth = smooth * (1 - body) + 0.38 * bars + 0.1 * slot
            stain = np.exp(-0.5 * (dx / 0.30) ** 2) * np.clip(1 - np.maximum(dy - 0.1, 0) / 1.6, 0, 1) * (dy > 0.0)
            alb = alb * (1 - 0.22 * stain[..., None] * (1 - body[..., None]))

    # ---------------------------------------------------------------- puddles, damp, oil sheen
    puddle = np.zeros((H, W), np.float32)
    damp = np.zeros((H, W), np.float32)
    if c["puddle"] > 0:
        P = 0.85 * low + 0.40 * mid + 0.15 * track      # 2026-10-04: blobby small puddles, not wheel-track strips
        thr = np.quantile(P[::6, ::6], 1.0 - c["puddle"])
        puddle = smoothstep(thr - 0.06, thr + 0.10, P)
        damp = smoothstep(thr - 0.85, thr, P)
        if kind == "Wet":
            smooth = smooth + 0.03 * damp
            smooth = smooth * (1 - puddle) + 0.975 * puddle
            alb = alb * (1 - 0.30 * damp[..., None])
            alb = alb * (1 - 0.40 * puddle[..., None])
            # thin-film oil rainbow on the water
            ob = smoothstep(0.1, 1.4, fbm(H, W, s + 7, 2.0, 0.002, 0.03))
            phase = 0.35 * low + 2.4 * mid + 0.15 * ob
            rb = np.stack([0.5 + 0.5 * np.cos(2 * np.pi * (phase + o)) for o in (0.0, 0.33, 0.67)], -1)
            alb = alb + (rb - 0.5) * 34.0 * (puddle * ob * 0.9 + oil * damp * 0.4)[..., None]
        else:
            smooth = smooth * (1 - puddle * 0.85) + 0.95 * puddle * 0.85
            alb = alb * (1 - 0.38 * puddle[..., None])
    # wet only: damp film everywhere is carried by the smoothness range; dry keeps it low

    # ---------------------------------------------------------------- output maps
    nrm = height_to_normal(hgt, 0.30 if hi else 0.26, flat=puddle * 0.88)
    mask = np.stack([metal, ao, np.clip(puddle + 0.25 * damp, 0, 1), np.clip(smooth, 0.02, 0.99)], -1)
    d = out_dir()
    save_png(os.path.join(d, "Road_%s_Albedo.png" % kind), to8(np.clip(alb, 0, 255) / 255.0))
    save_png(os.path.join(d, "Road_%s_Normal.png" % kind), to8(nrm))
    save_png(os.path.join(d, "Road_%s_Mask.png" % kind), to8(mask))


def make_crosswalk():
    W, H = 2048, 1024
    ppm = W / ROAD_W
    X = ((np.arange(W, dtype=np.float32) + 0.5) / ppm)[None, :]
    Y = ((np.arange(H, dtype=np.float32) + 0.5) / ppm)[:, None]
    fine = fbm(H, W, 901, 1.2, 0.03, 0.5)
    wear = fbm(H, W, 902, 1.6, 0.002, 0.3)
    # bars run along the travel direction (V): 0.45 m bars on a 0.9 m pitch, 4.4 m long
    px = (X % 0.9)
    bar = np.clip((0.225 - np.abs(px - 0.45)) * ppm + 0.5, 0, 1)
    Ly = np.clip((2.2 - np.abs(Y - 2.25)) * ppm + 0.5, 0, 1)
    cov = bar * Ly * smoothstep(-1.6, 0.0, wear + 0.5 * fine)
    # stop line along the U direction at the far end
    stop = np.clip((0.22 - np.abs(Y - 0.22)) * ppm + 0.5, 0, 1)
    cov = np.maximum(cov, stop * smoothstep(-1.6, 0.0, wear + 0.5 * fine) * 0.0)
    rgb = np.stack([np.full((H, W), v, np.float32) for v in (222, 222, 214)], -1) * (0.93 + 0.05 * fine[..., None] * 0.5)
    rgb = rgb * cov[..., None] + np.array([58, 58, 62], np.float32) * (1 - cov[..., None])
    out = np.concatenate([rgb / 255.0, cov[..., None]], -1)
    save_png(os.path.join(out_dir(), "Road_Crosswalk_Albedo.png"), to8(out))


def make_ripple_detail():
    """HDRP detail map: R albedo (neutral 0.5), G normal Y, B smoothness (neutral 0.5), A normal X.
    Overlapping concentric rain-drop ripples, tileable."""
    S = 512
    h = np.zeros((S, S), np.float32)
    rng = np.random.default_rng(777)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    for _ in range(46):
        cx, cy = rng.uniform(0, S), rng.uniform(0, S)
        rad = rng.uniform(14, 120)
        freq = rng.uniform(0.16, 0.30)
        amp = rng.uniform(0.5, 1.0)
        dx = np.abs(xx - cx)
        dx = np.minimum(dx, S - dx)
        dy = np.abs(yy - cy)
        dy = np.minimum(dy, S - dy)
        r = np.sqrt(dx * dx + dy * dy)
        env = np.exp(-((r - rad * 0.6) / (rad * 0.55)) ** 2) * (r < rad * 1.5)
        h += amp * env * np.sin(2 * np.pi * freq * r * (1.0)) * np.exp(-r / (rad * 1.2))
    h += 0.15 * fbm(S, S, 778, 2.0, 0.01, 0.2)
    n = height_to_normal(h, 0.55)
    ao = np.full((S, S), 0.5, np.float32)
    det = np.stack([ao, n[..., 1], np.full((S, S), 0.5, np.float32), n[..., 0]], -1)
    save_png(os.path.join(out_dir(), "Road_Ripple_Detail.png"), to8(det))


if __name__ == "__main__":
    kinds = sys.argv[1:] or list(CFG.keys()) + ["Crosswalk", "Ripple"]
    for k in kinds:
        if k == "Crosswalk":
            make_crosswalk()
        elif k == "Ripple":
            make_ripple_detail()
        else:
            make_road(k)

"""Shunta Metro buildings: modular Tokyo kit. Unity axes (+z = street-facing front, +y up), ground at y=0,
footprint centred on the origin, foundation skirt below y=0. Per-window emissive variation is done with
window MATERIAL families (SM_WinDark/Warm/WarmDim/Cool/TV) chosen per window - no texture needed.
blender.exe -b --python build_buildings.py [-- name]"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from sm_lib import *  # noqa: E402,F401,F403
import sm_lib as S  # noqa: E402

Y = (0.0, 1.0, 0.0)


def vcross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


class Fr:
    """Facade frame. P = origin point (u=0,v=0) on the wall, N = outward normal. U = Y x N (right when viewed from outside)."""

    def __init__(self, P, N):
        self.P, self.N = P, N
        self.U = vcross(Y, N)

    def pt(self, u, v, d=0.0):
        return tuple(self.P[i] + self.U[i] * u + Y[i] * v + self.N[i] * d for i in range(3))

    def quad(self, g, u0, v0, u1, v1, mat, d=0.0):
        g.face([self.pt(u0, v0, d), self.pt(u1, v0, d), self.pt(u1, v1, d), self.pt(u0, v1, d)], mat,
               [(u0 / 3, v0 / 3), (u1 / 3, v0 / 3), (u1 / 3, v1 / 3), (u0 / 3, v1 / 3)])

    def box(self, g, u0, v0, u1, v1, d0, d1, mat, mats=None):
        b = [self.pt(u0, v0, d0), self.pt(u1, v0, d0), self.pt(u1, v1, d0), self.pt(u0, v1, d0)]
        t = [self.pt(u0, v0, d1), self.pt(u1, v0, d1), self.pt(u1, v1, d1), self.pt(u0, v1, d1)]
        hexa(g, b, t, mat, mats)


def four_faces(w, d, y0=0.0):
    """Frames for the 4 walls of a w (x) by d (z) box: front(+z) right(+x) back(-z) left(-x)."""
    return {
        "front": (Fr((-w / 2, y0, d / 2), (0, 0, 1)), w),
        "right": (Fr((w / 2, y0, d / 2), (1, 0, 0)), d),
        "back": (Fr((w / 2, y0, -d / 2), (0, 0, -1)), w),
        "left": (Fr((-w / 2, y0, -d / 2), (-1, 0, 0)), d),
    }


def pick_win(rng, lit=0.55, tv=0.06, cool=0.22):
    r = rng.random()
    if r > lit:
        return "SM_WinDark"
    r2 = rng.random()
    if r2 < tv:
        return "SM_WinTV"
    if r2 < tv + cool:
        return "SM_WinCool"
    if r2 < 0.65:
        return "SM_WinWarm"
    return "SM_WinWarmDim"


def window_grid(g, fr, width, y0, floors, fh, cols, ww, wh, rng, d, lit=0.55, sill=True, margin=None,
                sill_mat="SM_ConcreteB", skip=None, win_d=0.03, frame=False):
    """Regular window grid on a frame. d = LOD (1: one banded quad per floor)."""
    margin = margin if margin is not None else (width - cols * ww) / (cols + 1)
    pitch = (width - 2 * margin) / cols if cols else 1
    for f in range(floors):
        v0 = y0 + f * fh + (fh - wh) * 0.5
        if d == 1:
            fr.quad(g, margin, v0, width - margin, v0 + wh, "SM_WinMix", 0.03)
            continue
        for c in range(cols):
            if skip and skip(f, c):
                continue
            u0 = margin + c * pitch + (pitch - ww) / 2
            m = pick_win(rng, lit)
            fr.quad(g, u0, v0, u0 + ww, v0 + wh, m, win_d)
            if sill:
                fr.box(g, u0 - 0.06, v0 - 0.08, u0 + ww + 0.06, v0, 0.0, 0.16, sill_mat)
            if frame:
                t = 0.05
                fr.box(g, u0 - t, v0 + wh, u0 + ww + t, v0 + wh + t, 0.0, 0.07, sill_mat)


def floor_bands(g, w, d, y0, floors, fh, mat="SM_ConcreteB", out=0.08, th=0.2):
    for f in range(1, floors + 1):
        g.box((0, y0 + f * fh, 0), (w + out * 2, th, d + out * 2), mat)


def ac_unit(g, c, yaw=0.0, mat="SM_ACUnit"):
    g.box(c, (0.8, 0.55, 0.3), mat, yaw=yaw)
    g.box((c[0], c[1], c[2] + 0.151 * math.cos(yaw)), (0.5, 0.45, 0.01), "SM_MetalDark", yaw=yaw)


def water_tank(g, cx, cz, y, r, h, d):
    seg = 14 if d == 0 else 8
    for dx, dz in ((-0.7, -0.7), (0.7, -0.7), (0.7, 0.7), (-0.7, 0.7)):
        if d == 0:
            beam(g, (cx + dx * r, y, cz + dz * r), (cx + dx * r, y + 1.2, cz + dz * r), 0.1, "SM_MetalDark")
    g.cylinder((cx, y + 1.2, cz), r, h, seg, "SM_Metal", top=True)
    g.cylinder((cx, y + 1.2 + h, cz), r * 0.7, 0.3, seg, "SM_Metal", top=True, r_top=r * 0.4) if d == 0 else None


def roof_clutter(g, w, d, ytop, rng, lvl, rich=1.0, sign=None):
    """Rooftop parapet + units. Footprint w x d centred at origin."""
    g.box((0, ytop + 0.05, 0), (w * 0.98, 0.1, d * 0.98), "SM_RoofTar")
    for sx in (-1, 1):
        g.box((sx * (w / 2 - 0.1), ytop + 0.5, 0), (0.2, 1.0, d), "SM_ConcreteB")
    for sz in (-1, 1):
        g.box((0, ytop + 0.5, sz * (d / 2 - 0.1)), (w, 1.0, 0.2), "SM_ConcreteB")
    if lvl == 1:
        g.box((w * 0.18, ytop + 1.8, -d * 0.1), (w * 0.28, 1.6, d * 0.3), "SM_ConcreteA")
        return
    g.box((w * 0.2, ytop + 1.9, -d * 0.12), (w * 0.28, 1.8, d * 0.28), "SM_ConcreteA")       # stair housing
    g.box((w * 0.2, ytop + 2.85, -d * 0.12), (w * 0.3, 0.12, d * 0.3), "SM_ConcreteB")
    g.box((w * 0.2 + w * 0.139, ytop + 1.6, -d * 0.12 + d * 0.141), (0.9, 1.9, 0.08), "SM_MetalDark")
    n_ac = int(5 * rich) + 2
    for i in range(n_ac):
        x = -w * 0.38 + (i % 4) * 1.4
        z = d * 0.2 + (i // 4) * 1.2
        g.box((x, ytop + 0.85, z), (1.1, 0.8, 0.9), "SM_ACUnit")
        g.box((x, ytop + 1.0, z + 0.46), (0.7, 0.55, 0.02), "SM_MetalDark")
    water_tank(g, -w * 0.28, -d * 0.22, ytop + 0.1, 1.3, 2.0, lvl)
    beam(g, (w * 0.35, ytop + 0.1, d * 0.3), (w * 0.35, ytop + 9.0, d * 0.3), 0.12, "SM_MetalDark")
    for k in range(3):
        beam(g, (w * 0.35 - 0.6, ytop + 6 + k * 1.0, d * 0.3), (w * 0.35 + 0.6, ytop + 6 + k * 1.0, d * 0.3), 0.06, "SM_MetalDark")
    g.box((w * 0.35, ytop + 9.2, d * 0.3), (0.2, 0.2, 0.2), "SM_Warning")
    if sign:
        # rooftop billboard on a frame
        bw, bh = min(w * 0.7, 9.0), 3.2
        for sx in (-0.4, 0.4):
            beam(g, (sx * bw, ytop + 1.0, -d * 0.3), (sx * bw, ytop + 1.0 + bh, -d * 0.3), 0.15, "SM_MetalDark")
        g.box((0, ytop + 1.0 + bh / 2 + 0.6, -d * 0.3), (bw + 0.3, bh + 0.2, 0.25), "SM_MetalDark")
        g.box((0, ytop + 1.0 + bh / 2 + 0.6, -d * 0.3 + 0.14), (bw, bh, 0.04), sign)


def blade_sign(g, fr, u, v, h, depth, mat, rng, d, bars=True):
    """Vertical neon blade sign protruding from a facade at (u, v-bottom)."""
    t = 0.28
    fr.box(g, u - t / 2, v, u + t / 2, v + h, 0.08, 0.08 + depth, "SM_MetalDark")
    for side in (-1, 1):
        uu = u + side * (t / 2 + 0.012)
        fr.box(g, uu - 0.012 if side > 0 else uu - 0.012, v + 0.1, uu + 0.012, v + h - 0.1, 0.14, 0.04 + depth, mat)
        if bars and d == 0:
            n = max(2, int(h / 0.9))
            for k in range(n):
                vv = v + 0.2 + k * (h - 0.4) / n
                bm = ("SM_MetalDark" if rng.random() < 0.7 else "SM_NeonWhite")
                uu2 = u + side * (t / 2 + 0.03)
                fr.box(g, uu2 - 0.012, vv, uu2 + 0.012, vv + (h - 0.4) / n * 0.6, 0.3, 0.3 + depth * 0.55, bm)


def foundation(g, w, d, depth=6.0):
    g.box((0, -depth / 2, 0), (w, depth, d), "SM_ConcreteDark", top=False)


def shopfront(g, fr, width, h, rng, d, glass="SM_WinShop", band="SM_NeonWhite", awning=None):
    """Ground-floor glass shopfront with signage band, door and optional awning."""
    m = 0.4
    fr.quad(g, m, 0.1, width - m, h - 0.7, glass, 0.04)
    fr.box(g, 0.0, 0.0, width, 0.12, 0.0, 0.1, "SM_ConcreteDark")
    fr.box(g, m - 0.1, 0.1, m, h - 0.6, 0.0, 0.1, "SM_MetalDark")
    fr.box(g, width - m, 0.1, width - m + 0.1, h - 0.6, 0.0, 0.1, "SM_MetalDark")
    fr.box(g, m - 0.1, h - 0.7, width - m + 0.1, h - 0.6, 0.0, 0.1, "SM_MetalDark")
    # door mullion
    if d == 0:
        fr.box(g, width * 0.5 - 0.04, 0.1, width * 0.5 + 0.04, h - 0.7, 0.0, 0.08, "SM_MetalDark")
    fr.box(g, 0.0, h - 0.55, width, h, 0.0, 0.25, "SM_MetalDark")
    fr.quad(g, 0.15, h - 0.45, width - 0.15, h - 0.1, band, 0.26)
    if awning:
        fr.box(g, 0.2, h + 0.0, width - 0.2, h + 0.1, 0.0, 1.3, awning)


def building_base(g, w, d, h, wall, sides=True):
    g.box((0, h / 2, 0), (w, h, d), wall)
    foundation(g, w, d)


# ======================================================================== OFFICE
def office(seed, d, floors=9, w=16.0, dep=14.0, wall="SM_ConcreteA", bay=3.2, rich=1.0, signm="SM_NeonCyan"):
    rng = random.Random(seed)
    g = Geo()
    fh = 3.6
    gh = 4.4
    H = gh + floors * fh
    building_base(g, w, dep, H, wall)
    fs = four_faces(w, dep)
    cols = max(2, int(w / bay))
    for name, (fr, wd) in fs.items():
        c = cols if name in ("front", "back") else max(2, int(dep / bay))
        window_grid(g, fr, wd, gh, floors, fh, c, bay * 0.72, 1.7, rng, d, lit=0.5 if name != "back" else 0.35, sill=(d == 0 and name != "back"))
    fr_f = fs["front"][0]
    shopfront(g, fr_f, w, gh, rng, d, awning="SM_FabricBlue" if rich > 0.5 else None)
    if d == 0:
        floor_bands(g, w, dep, 0, floors, fh=fh, mat="SM_ConcreteB") if False else None
        for f in range(1, floors + 1):
            fr_f.box(g, -0.1, gh + (f - 1) * fh + fh - 0.12, w + 0.1, gh + (f - 1) * fh + fh + 0.08, 0.0, 0.14, "SM_ConcreteB")
        # corner pilasters
        for u in (-0.15, w - 0.35):
            fr_f.box(g, u, gh, u + 0.5, H, 0.0, 0.22, "SM_ConcreteDark")
        # balcony/emergency stair on the right wall
        fr_r = fs["right"][0]
        for f in range(floors):
            fr_r.box(g, 2.0, gh + f * fh - 0.02, 5.0, gh + f * fh + 0.1, 0.0, 1.1, "SM_MetalDark")
            fr_r.box(g, 2.0, gh + f * fh + 0.1, 5.0, gh + f * fh + 1.0, 1.05, 1.1, "SM_Metal")
        blade_sign(g, fr_f, w * 0.12, gh + 1.0, 3.0, 0.9, signm, rng, d)
    # entrance sign band at top
    fr_f.box(g, w * 0.2, H - 1.4, w * 0.8, H - 0.4, 0.0, 0.3, "SM_MetalDark")
    fr_f.quad(g, w * 0.22, H - 1.3, w * 0.78, H - 0.5, signm, 0.31)
    roof_clutter(g, w, dep, H, rng, d, rich)
    return [g]


# ======================================================================== IZAKAYA ROW
def izakaya(seed, d, storeys=4, w=6.0, dep=12.0, wall="SM_TileBrown"):
    rng = random.Random(seed)
    g = Geo()
    fh = 3.2
    gh = 3.6
    H = gh + (storeys - 1) * fh
    building_base(g, w, dep, H, wall)
    fs = four_faces(w, dep)
    fr, wd = fs["front"]
    # ground: open shopfront with lantern + noren
    fr.box(g, 0.0, 0.0, wd, 0.15, 0.0, 0.3, "SM_ConcreteDark")
    fr.quad(g, 0.3, 0.2, wd - 0.3, gh - 0.9, "SM_WinShop", 0.05)
    for k in range(5 if d == 0 else 2):
        uu = 0.5 + k * (wd - 1.0) / 4.6
        fr.quad(g, uu, 1.7, uu + 0.9, gh - 0.9, "SM_FabricRed" if k % 2 == 0 else "SM_FabricYellow", 0.16)
    fr.box(g, 0.0, gh - 0.85, wd, gh - 0.05, 0.0, 0.4, "SM_MetalDark")
    fr.quad(g, 0.1, gh - 0.75, wd - 0.1, gh - 0.15, rng.choice(["SM_NeonRed", "SM_NeonYellow", "SM_NeonPink"]), 0.41)
    if d == 0:
        for k in range(3):    # paper lanterns (akachochin)
            xx = wd * (0.2 + 0.3 * k)
            g.cylinder(fr.pt(xx, 2.0, 0.55), 0.22, 0.45, 8, "SM_Lantern", top=True, bottom=True)
            g.box(fr.pt(xx, 2.5, 0.55), (0.3, 0.04, 0.3), "SM_MetalDark")
    # storeys: narrow window + signage panels
    for s in range(1, storeys):
        v0 = gh + (s - 1) * fh
        fr.box(g, -0.05, v0 - 0.1, wd + 0.05, v0 + 0.15, 0.0, 0.2, "SM_ConcreteB")
        fr.quad(g, 0.5, v0 + 0.5, wd - 0.5, v0 + 2.2, pick_win(rng, 0.8), 0.04)
        if d == 0:
            fr.box(g, 0.4, v0 + 0.4, wd - 0.4, v0 + 0.5, 0.0, 0.3, "SM_MetalDark")
            fr.box(g, 0.4, v0 + 0.5, 0.45, v0 + 1.2, 0.0, 0.3, "SM_MetalDark")       # tiny balcony rail
            fr.box(g, wd - 0.45, v0 + 0.5, wd - 0.4, v0 + 1.2, 0.0, 0.3, "SM_MetalDark")
            fr.box(g, 0.4, v0 + 1.2, wd - 0.4, v0 + 1.25, 0.28, 0.3, "SM_MetalDark")
    # protruding signs (blade) at stacked storeys, alternating neon colours
    cols = ["SM_NeonRed", "SM_NeonYellow", "SM_NeonPink", "SM_NeonCyan", "SM_NeonGreen", "SM_NeonWhite"]
    for s in range(1, storeys):
        blade_sign(g, fr, rng.choice([0.35, wd - 0.35]), gh + (s - 1) * fh + 0.2, rng.uniform(2.2, 2.9), rng.uniform(0.9, 1.3),
                   rng.choice(cols), rng, d)
    # side walls: windows, AC units and pipes
    for name in ("right", "left"):
        f2, wd2 = fs[name]
        for s in range(1, storeys):
            v0 = gh + (s - 1) * fh
            for k in range(2 if d == 0 else 1):
                f2.quad(g, 2.0 + k * 4, v0 + 0.8, 3.2 + k * 4, v0 + 2.0, pick_win(rng, 0.5), 0.03)
        if d == 0:
            for s in range(1, storeys):
                ac_unit(g, f2.pt(8.5, gh + (s - 1) * fh + 0.6, 0.35))
            beam(g, f2.pt(10.2, 0.2, 0.2), f2.pt(10.2, H, 0.2), 0.12, "SM_Metal")
    if d == 0:
        # external stair on right wall
        fr_r, wd_r = fs["right"]
        for s in range(storeys - 1):
            v0 = gh + s * fh - fh * 0.9 if s else 0.2
            for k in range(8):
                u0 = 1.0 + k * 0.5
                fr_r.box(g, u0, (gh + s * fh - fh) + k * (fh / 8), u0 + 0.5, (gh + s * fh - fh) + k * (fh / 8) + 0.06, 0.0, 0.9, "SM_MetalDark") if s > 0 else None
    roof_clutter(g, w, dep, H, rng, d, 0.4, sign=rng.choice(cols) if storeys > 3 else None)
    return [g]


# ======================================================================== GLASS TOWER
def glass_tower(seed, d, floors=26, w=18.0, dep=18.0, fh=3.9, tiers=3, crown="SM_NeonCyan"):
    rng = random.Random(seed)
    g = Geo()
    y = 0.0
    tw, td = w, dep
    foundation(g, w, dep, 8.0)
    # podium
    ph = 8.0
    g.box((0, ph / 2, 0), (w * 1.12, ph, dep * 1.12), "SM_ConcreteDark")
    fr0 = four_faces(w * 1.12, dep * 1.12)
    for name, (fr, wd) in fr0.items():
        fr.quad(g, 1.0, 0.5, wd - 1.0, 6.2, "SM_WinShop" if name == "front" else "SM_WinDark", 0.04)
        if d == 0:
            fr.box(g, 0, 6.3, wd, 7.0, 0.0, 0.3, "SM_MetalDark")
    y = ph
    per = max(floors // tiers, 4)
    for t in range(tiers):
        n = per if t < tiers - 1 else floors - per * (tiers - 1)
        g.box((0, y + n * fh / 2, 0), (tw, n * fh, td), "SM_Curtain")
        fs = four_faces(tw, td, y)
        cols = max(3, int(tw / 3.0))
        for name, (fr, wd) in fs.items():
            c = cols if name in ("front", "back") else max(3, int(td / 3.0))
            ww = wd / c * 0.8
            window_grid(g, fr, wd, 0, n, fh, c, ww, fh * 0.7, rng, d, lit=0.46, sill=False, margin=(wd - c * ww) / (c + 1) if False else None, win_d=0.04)
            if d == 0:
                for k in range(1, c):      # vertical mullions
                    uu = wd / c * k
                    fr.box(g, uu - 0.06, 0, uu + 0.06, n * fh, 0.0, 0.2, "SM_MetalDark")
        # ribbons at tier top
        g.box((0, y + n * fh + 0.2, 0), (tw + 0.4, 0.4, td + 0.4), "SM_MetalDark")
        y += n * fh + 0.4
        tw *= 0.84
        td *= 0.84
    # crown: LED band + mast
    g.box((0, y + 0.3, 0), (tw + 0.4, 0.6, td + 0.4), "SM_MetalDark")
    for sx in (-1, 1):
        g.box((sx * (tw / 2 + 0.22), y + 0.3, 0), (0.1, 0.5, td + 0.4), crown)
    for sz in (-1, 1):
        g.box((0, y + 0.3, sz * (td / 2 + 0.22)), (tw + 0.4, 0.5, 0.1), crown)
    beam(g, (0, y, 0), (0, y + 14, 0), 0.4, "SM_MetalDark", 0.1)
    g.box((0, y + 14.2, 0), (0.3, 0.4, 0.3), "SM_Warning")
    if d == 0:
        for k in range(3):
            g.box((0, y + 5 + k * 3, 0), (3.0 - k * 0.8, 0.12, 0.12), "SM_MetalDark")
        g.box((tw * 0.25, y + 1.2, td * 0.15), (3, 2.4, 3), "SM_ACUnit")
        g.box((-tw * 0.25, y + 1.2, -td * 0.2), (3, 2.4, 3), "SM_ACUnit")
    return [g]


# ======================================================================== DEPARTMENT STORE
def dept_store(seed, d, w=38.0, dep=24.0, floors=7):
    rng = random.Random(seed)
    g = Geo()
    fh = 4.2
    H = floors * fh
    building_base(g, w, dep, H, "SM_TileWhite")
    fs = four_faces(w, dep)
    fr, wd = fs["front"]
    # entrance + canopy
    fr.quad(g, 3.0, 0.2, wd - 3.0, 4.0, "SM_WinShop", 0.05)
    fr.box(g, 2.0, 4.3, wd - 2.0, 4.9, 0.0, 3.2, "SM_MetalDark")
    fr.quad(g, 2.2, 4.4, wd - 2.2, 4.8, "SM_NeonWhite", 3.21) if False else None
    for k in range(7 if d == 0 else 3):
        g.box(fr.pt(2.6 + k * (wd - 5.2) / 6, 2.2, 3.0), (0.12, 4.2, 0.12), "SM_MetalDark")
    # the VIDEO WALL SLOT: recessed dark frame + emissive panel material SM_VideoWall (UV 0..1 over the panel)
    vw0, vw1 = wd * 0.12, wd * 0.88
    vv0, vv1 = 9.5, 24.5
    fr.box(g, vw0 - 0.5, vv0 - 0.5, vw1 + 0.5, vv1 + 0.5, 0.0, 0.5, "SM_MetalDark")
    fr.quad(g, vw0, vv0, vw1, vv1, "SM_VideoWall", 0.52)
    # (UV of that quad: replace with 0..1 so a texture can be dropped on the slot)
    g.faces[-1] = (g.faces[-1][0], ((0, 0), (1, 0), (1, 1), (0, 1)), "SM_VideoWall")
    # floors below/above screen: windowless bands with window slits
    for f in range(floors):
        v = f * fh
        if v + fh < vv0 - 0.5 or v > vv1 + 0.5:
            if v > 4.5 or f == 0:
                continue
            fr.quad(g, 1.0, v + 1.2, wd - 1.0, v + 2.6, pick_win(rng, 0.8), 0.04) if d == 0 else None
        if d == 0:
            fr.box(g, 0.0, v - 0.1, wd, v + 0.15, 0.0, 0.25, "SM_ConcreteB")
    # fins
    if d == 0:
        for k in range(1, 18):
            fr.box(g, k * wd / 18 - 0.1, 0.0, k * wd / 18 + 0.1, H, 0.0, 0.5, "SM_ConcreteB") if k % 3 == 0 else None
    # side facades: ribbon windows
    for name in ("right", "left", "back"):
        f2, w2 = fs[name]
        window_grid(g, f2, w2, 4.5, floors - 1, fh, 6, 2.4, 1.6, rng, d, lit=0.4, sill=False)
    # neon ring on top + sign
    fr.box(g, 2.0, H, wd - 2.0, H + 3.0, 0.0, 0.4, "SM_MetalDark")
    fr.quad(g, 2.4, H + 0.3, wd - 2.4, H + 2.7, rng.choice(["SM_NeonRed", "SM_NeonPink"]), 0.41)
    roof_clutter(g, w, dep, H, rng, d, 1.5)
    return [g]


# ======================================================================== PACHINKO
def pachinko(seed, d, w=20.0, dep=22.0, H=15.0, pal=("SM_NeonPink", "SM_NeonYellow", "SM_NeonCyan")):
    rng = random.Random(seed)
    g = Geo()
    building_base(g, w, dep, H, "SM_ConcreteDark")
    fs = four_faces(w, dep)
    fr, wd = fs["front"]
    # emissive stacked bands + chaser bulbs
    bands = 6
    for b in range(bands):
        v0 = 0.8 + b * (H - 3.0) / bands
        mat = pal[b % len(pal)]
        fr.box(g, 0.0, v0, wd, v0 + 0.5, 0.0, 0.4, mat)
        fr.box(g, 0.0, v0 + 0.5, wd, v0 + 0.62, 0.0, 0.35, "SM_MetalDark")
    # big circular ball signs (cylinder axis z -> disc facing front)
    for k, (cu, cv, r) in enumerate(((wd * 0.2, H - 4.0, 2.6), (wd * 0.8, H - 4.0, 2.6), (wd * 0.5, H - 3.2, 1.8))):
        seg = 28 if d == 0 else 12
        c0 = fr.pt(cu, cv, 0.5)
        ring = [(c0[0] + r * math.cos(i / seg * math.tau), c0[1] + r * math.sin(i / seg * math.tau), c0[2]) for i in range(seg)]
        ring2 = [(p[0], p[1], p[2] + 0.5) for p in ring]
        for i in range(seg):
            j = (i + 1) % seg
            g.quad(ring[i], ring[j], ring2[j], ring2[i], "SM_MetalDark") if False else None
            oquad(g, [ring[i], ring[j], ring2[j], ring2[i]], "SM_MetalDark", (c0[0], c0[1], c0[2] + 0.25))
        cen = (c0[0], c0[1], c0[2] + 0.5)
        for i in range(seg):
            j = (i + 1) % seg
            g.tri(cen, ring2[i], ring2[j], pal[k % len(pal)])
        # inner dark ring for a "pachinko ball" motif
        ri = r * 0.55
        ringi = [(c0[0] + ri * math.cos(i / seg * math.tau), c0[1] + ri * math.sin(i / seg * math.tau), c0[2] + 0.52) for i in range(seg)]
        ceni = (c0[0], c0[1], c0[2] + 0.52)
        for i in range(seg):
            j = (i + 1) % seg
            g.tri(ceni, ringi[i], ringi[j], "SM_NeonWhite")
    # arched entrance with chaser bulbs
    ew = 6.0
    fr.quad(g, wd / 2 - ew / 2, 0.1, wd / 2 + ew / 2, 3.6, "SM_WinShop", 0.06)
    fr.box(g, wd / 2 - ew / 2 - 0.4, 0.0, wd / 2 - ew / 2, 4.2, 0.0, 0.5, "SM_MetalDark")
    fr.box(g, wd / 2 + ew / 2, 0.0, wd / 2 + ew / 2 + 0.4, 4.2, 0.0, 0.5, "SM_MetalDark")
    fr.box(g, wd / 2 - ew / 2 - 0.4, 3.6, wd / 2 + ew / 2 + 0.4, 4.2, 0.0, 0.6, "SM_MetalDark")
    nb = 16 if d == 0 else 6
    for k in range(nb):
        uu = wd / 2 - ew / 2 + (k + 0.5) * ew / nb
        fr.box(g, uu - 0.1, 3.75, uu + 0.1, 3.95, 0.6, 0.72, "SM_NeonWhite" if k % 2 == 0 else pal[0])
    # vertical sign columns at corners
    for u in (0.4, wd - 1.4):
        fr.box(g, u, 1.0, u + 1.0, H - 0.5, 0.0, 0.9, "SM_MetalDark")
        for k in range(5):
            fr.box(g, u + 0.1, 1.4 + k * (H - 3.0) / 5, u + 0.9, 1.4 + k * (H - 3.0) / 5 + 1.6, 0.9, 0.95, pal[(k + 1) % len(pal)])
    # sides: LED stripes
    for name in ("right", "left"):
        f2, w2 = fs[name]
        for b in range(0, 3):
            f2.box(g, 0, 2.0 + b * 4, w2, 2.3 + b * 4, 0.0, 0.2, pal[b % len(pal)])
    roof_clutter(g, w, dep, H, rng, d, 1.0)
    return [g]


# ======================================================================== KONBINI
def konbini(seed, d, w=12.0, dep=14.0, H=4.4, stripes=("SM_KonbiniGreen", "SM_KonbiniOrange", "SM_KonbiniBlue")):
    rng = random.Random(seed)
    g = Geo()
    building_base(g, w, dep, H, "SM_TileWhite")
    fs = four_faces(w, dep)
    fr, wd = fs["front"]
    fr.quad(g, 0.5, 0.15, wd - 0.5, H - 1.7, "SM_WinShop", 0.05)
    for u in (0.4, wd - 0.5, wd * 0.5 - 0.05):
        fr.box(g, u, 0.1, u + 0.1, H - 1.6, 0.0, 0.1, "SM_Metal")
    # interior shelves visible
    if d == 0:
        for k in range(4):
            g.box(fr.pt(1.5 + k * 2.4, 1.0, -1.5), (1.8, 1.8, 0.5), "SM_FabricRed" if k % 2 else "SM_FabricBlue")
    fr.box(g, 0.0, H - 1.6, wd, H, 0.0, 0.4, "SM_MetalDark")
    for k, m in enumerate(stripes):
        fr.quad(g, 0.15, H - 1.45 + k * 0.4, wd - 0.15, H - 1.15 + k * 0.4, m, 0.41)
    # canopy + pole sign
    fr.box(g, 0.0, H, wd, H + 0.2, 0.0, 1.6, "SM_ConcreteDark") if False else None
    beam(g, fr.pt(wd + 1.5, 0.0, 3.0), fr.pt(wd + 1.5, 7.0, 3.0), 0.25, "SM_MetalDark")
    fr.box(g, wd + 0.5, 6.2, wd + 2.5, 8.2, 2.9, 3.2, "SM_MetalDark")
    fr.box(g, wd + 0.6, 6.3, wd + 2.4, 6.9, 3.2, 3.24, stripes[0])
    fr.box(g, wd + 0.6, 6.95, wd + 2.4, 7.55, 3.2, 3.24, stripes[1])
    fr.box(g, wd + 0.6, 7.6, wd + 2.4, 8.1, 3.2, 3.24, stripes[2])
    # posters, ATM, bins
    if d == 0:
        fr.box(g, 1.0, 1.0, 2.0, 2.4, 0.0, 0.04, "SM_SignBoard")
        fr.box(g, wd - 2.2, 0.0, wd - 1.2, 1.0, 0.3, 1.0, "SM_MetalDark")
        fr.box(g, wd - 2.2, 1.0, wd - 1.2, 1.1, 0.3, 1.0, "SM_NeonBlue")
        for k in range(4):   # parking blocks
            fr.box(g, 1.5 + k * 2.5, 0.0, 2.2 + k * 2.5, 0.12, 2.5, 4.5, "SM_ConcreteB")
    roof_clutter(g, w, dep, H, rng, d, 0.7)
    return [g]


# ======================================================================== APARTMENT
def apartment(seed, d, floors=8, w=20.0, dep=12.0, wall="SM_ConcreteB", cols=5):
    rng = random.Random(seed)
    g = Geo()
    fh = 3.0
    gh = 3.6
    H = gh + floors * fh
    building_base(g, w, dep, H, wall)
    fs = four_faces(w, dep)
    fr, wd = fs["front"]
    fr.quad(g, 2.0, 0.1, wd - 2.0, gh - 0.6, "SM_WinShop", 0.05)
    fr.box(g, 1.8, gh - 0.6, wd - 1.8, gh - 0.3, 0.0, 0.6, "SM_MetalDark")
    pitch = wd / cols
    laundry = ["SM_FabricRed", "SM_FabricBlue", "SM_FabricYellow", "SM_Fabric", "SM_TileBeige"]
    for f in range(floors):
        v0 = gh + f * fh
        fr.box(g, 0.0, v0 - 0.12, wd, v0 + 0.1, 0.0, 1.5, "SM_ConcreteA") if d == 0 else fr.box(g, 0.0, v0 - 0.12, wd, v0 + 0.1, 0.0, 1.5, "SM_ConcreteA")
        for c in range(cols):
            u0 = c * pitch + pitch * 0.14
            u1 = (c + 1) * pitch - pitch * 0.14
            fr.quad(g, u0 + 0.2, v0 + 0.2, u1 - 0.2, v0 + 2.3, pick_win(rng, 0.55), 0.04)
            if d == 0:
                # balcony: rail panel + AC unit + laundry
                fr.box(g, u0, v0 + 0.1, u1, v0 + 1.05, 1.35, 1.45, "SM_ConcreteB")
                fr.box(g, u0, v0 + 1.05, u1, v0 + 1.1, 1.3, 1.5, "SM_Metal")
                if rng.random() < 0.8:
                    ac_unit(g, fr.pt(u0 + 0.5, v0 + 0.45, 1.0))
                if rng.random() < 0.55:
                    beam(g, fr.pt(u0 + 0.1, v0 + 2.2, 1.05), fr.pt(u1 - 0.1, v0 + 2.2, 1.05), 0.03, "SM_Metal")
                    for k in range(rng.randint(2, 4)):
                        uu = u0 + 0.35 + k * 0.45
                        if uu + 0.35 < u1:
                            fr.quad(g, uu, v0 + 1.45, uu + 0.34, v0 + 2.18, rng.choice(laundry), 1.04)
                if rng.random() < 0.3:
                    fr.box(g, u0 + 0.3, v0 + 1.1, u1 - 0.3, v0 + 1.7, 1.2, 1.35, rng.choice(laundry))   # futon over rail
            else:
                fr.box(g, u0, v0 + 0.1, u1, v0 + 1.05, 1.35, 1.45, "SM_ConcreteB")
    # sides
    for name in ("right", "left"):
        f2, w2 = fs[name]
        window_grid(g, f2, w2, gh, floors, fh, 3, 1.2, 1.2, rng, d, lit=0.45, sill=False)
    # stair core on the back + roof
    fb, wb = fs["back"]
    window_grid(g, fb, wb, gh, floors, fh, 5, 1.6, 1.0, rng, d, lit=0.35, sill=False)
    if d == 0:
        for u in (0.0, wd - 0.3):
            fr.box(g, u, 0, u + 0.3, H, 0.0, 0.3, "SM_ConcreteDark")
        blade_sign(g, fr, wd * 0.82, 0.2 + 1.0, 2.0, 0.8, rng.choice(["SM_KonbiniGreen", "SM_NeonRed", "SM_NeonCyan"]), rng, d)
    roof_clutter(g, w, dep, H, rng, d, 1.2)
    return [g]


# ======================================================================== SHIBUYA SCRAMBLE
def scramble(seed, d, w=30.0, dep=22.0, floors=9):
    rng = random.Random(seed)
    g = Geo()
    fh = 3.8
    H = floors * fh + 4.0
    building_base(g, w, dep, H, "SM_ConcreteDark")
    fs = four_faces(w, dep)
    fr, wd = fs["front"]
    fr.quad(g, 1.0, 0.2, wd - 1.0, 3.9, "SM_WinShop", 0.05)
    fr.box(g, 0.0, 4.0, wd, 4.9, 0.0, 0.5, "SM_MetalDark")
    fr.quad(g, 0.2, 4.15, wd - 0.2, 4.75, "SM_NeonPink", 0.51)

    def screen(u0, v0, u1, v1, mat):
        fr.box(g, u0 - 0.35, v0 - 0.35, u1 + 0.35, v1 + 0.35, 0.0, 0.6, "SM_MetalDark")
        fr.quad(g, u0, v0, u1, v1, mat, 0.62)
        g.faces[-1] = (g.faces[-1][0], ((0, 0), (1, 0), (1, 1), (0, 1)), mat)
    screen(wd * 0.06, 6.0, wd * 0.58, 17.5, "SM_VideoWall")
    screen(wd * 0.64, 7.5, wd * 0.95, 16.0, "SM_ScreenB")
    screen(wd * 0.14, 19.5, wd * 0.5, 27.0, "SM_ScreenC")
    screen(wd * 0.56, 19.0, wd * 0.94, 24.0, "SM_VideoWall")
    # LED ad ribbons and neon boards
    cols = ["SM_NeonRed", "SM_NeonYellow", "SM_NeonCyan", "SM_NeonPink", "SM_NeonGreen"]
    for k in range(8):
        u = 0.6 + k * (wd - 1.2) / 8
        fr.box(g, u, 28.5 + (k % 2) * 1.2, u + (wd - 1.2) / 8 - 0.4, 29.5 + (k % 2) * 1.2 + (0.8 if k % 3 == 0 else 0), 0.0, 0.5,
               "SM_MetalDark")
        fr.quad(g, u + 0.1, 28.6 + (k % 2) * 1.2, u + (wd - 1.2) / 8 - 0.5, 29.4 + (k % 2) * 1.2, cols[k % 5], 0.52)
    for name in ("right", "left", "back"):
        f2, w2 = fs[name]
        window_grid(g, f2, w2, 5.0, floors - 1, fh, 7, 2.0, 1.6, rng, d, lit=0.55, sill=False)
    for u in (0.0, wd - 0.8):
        fr.box(g, u, 0.0, u + 0.8, H, 0.0, 0.4, "SM_ConcreteA")
        for k in range(4):
            fr.box(g, u + 0.1, 5 + k * 6, u + 0.7, 5 + k * 6 + 2.2, 0.4, 0.45, cols[(k + 1) % 5])
    roof_clutter(g, w, dep, H, rng, d, 1.4, sign="SM_NeonRed")
    return [g]

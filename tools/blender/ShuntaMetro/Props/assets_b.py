"""Street furniture B: subway entrance, bike rack, bollards, guardrail, manhole, grate, bins, A-frame, awning, post box, rooftop units."""
import math, random
from mathutils import Vector
from props_lib import *
from assets_a import lathe_shift

ASSETS = {}


def asset(name, lod=True, ratio=0.4):
    def deco(fn):
        ASSETS[name] = (fn, lod, ratio)
        return fn
    return deco


# ------------------------------------------------------------------ subway entrance (A1)
@asset("subway_entrance_a1", ratio=0.35)
def subway(b):
    # stairwell walls and steps (stairs descend toward +Y, front faces -Y)
    for sx in (-1, 1):
        b.box((sx * 1.45, 0.8, 0.5), (0.3, 4.6, 1.0), "SM_Concrete", 0.02, seg=1)
        b.box((sx * 1.45, 0.8, 1.04), (0.38, 4.7, 0.08), "SM_ConcreteDark", 0.012, seg=1)       # coping
        b.box((sx * 1.45, 1.6, -1.0), (0.3, 3.0, 2.0), "SM_Concrete", 0.01, seg=1)
    n = 12
    for i in range(n):
        y = -1.0 + i * 0.3; z = -0.17 * i
        b.box((0, y, z - 0.085), (2.6, 0.3, 0.17), "SM_ConcreteDark", 0.008, seg=1)
        b.box((0, y - 0.13, z + 0.002), (2.6, 0.05, 0.012), "SM_PaintYellow", 0.002, seg=1)
    b.box((0, -1.4, 0.01), (2.6, 0.5, 0.02), "SM_ConcreteDark", 0.004)
    # handrails: two sides + centre
    for x in (-1.1, 0.0, 1.1):
        p0 = (x, -1.1, 0.95); p1 = (x, -1.0 + n * 0.3, 0.95 - 0.17 * n)
        b.cyl(p0, p1, 0.025, "SM_Steel", 8)
        for k in range(5):
            t = k / 4
            px = Vector(p0).lerp(Vector(p1), t)
            b.cyl((px.x, px.y, px.z), (px.x, px.y, px.z - 0.95 - (0.17 * n * t if False else 0) + 0.0), 0.02, "SM_Steel", 6)
        b.cyl(p0, (x, -1.1, 0.0), 0.025, "SM_Steel", 8)
        b.sphere(p0, 0.03, "SM_Steel", 6)
    # canopy
    cols = [(-1.55, -1.3, 3.1), (1.55, -1.3, 3.1), (-1.55, 2.4, 2.7), (1.55, 2.4, 2.7)]
    for x, y, h in cols:
        b.box((x, y, h / 2), (0.14, 0.14, h), "SM_PaintGreen", 0.012)
        b.box((x, y, 0.06), (0.3, 0.3, 0.12), "SM_SteelDark", 0.01)
    ang = -math.degrees(math.atan(0.4 / 3.7))
    b.box((0, 0.55, 2.95), (3.6, 4.0, 0.1), "SM_PaintWhite", 0.015, rot=(ang, 0, 0))
    for i in range(7):
        x = -1.5 + i * 0.5
        b.box((x, 0.55, 2.88), (0.06, 4.0, 0.1), "SM_PaintGreen", 0.006, rot=(ang, 0, 0), seg=1)
    # fascia with lit kanji sign
    b.box((0, -1.45, 3.0), (3.7, 0.12, 0.62), "SM_PaintGreen", 0.015)
    b.box((0, -1.52, 3.0), (3.4, 0.03, 0.48), "SM_NeonWhite", 0.006)
    b.text("地下鉄入口", (0, -1.55, 3.0), 0.3, "SM_PaintBlack", 0.01)
    # underside lights
    for i in range(3):
        b.box((0, -0.4 + i * 1.3, 2.84), (2.6, 0.12, 0.03), "SM_LampCool", 0.004)
    # sign pylon with A1 roundel
    b.box((-2.3, -1.3, 1.1), (0.5, 0.3, 2.2), "SM_PaintGreen", 0.015)
    b.box((-2.3, -1.465, 1.65), (0.4, 0.03, 0.8), "SM_NeonWhite", 0.006)
    b.text("A1", (-2.3, -1.485, 1.78), 0.34, "SM_PaintBlue", 0.01)
    b.text("出口", (-2.3, -1.485, 1.4), 0.15, "SM_PaintBlack", 0.008)
    b.cyl((-2.3, -1.28, 2.42), (-2.3, -1.4, 2.42), 0.28, "SM_NeonBlue", 20)
    b.text("M", (-2.3, -1.42, 2.42), 0.3, "SM_NeonWhite", 0.01)
    b.box((-2.3, -1.3, 2.22), (0.55, 0.35, 0.05), "SM_SteelDark", 0.01)
    # tactile paving strip
    b.box((0, -2.1, 0.01), (2.6, 0.5, 0.02), "SM_PaintYellow", 0.004)
    for i in range(8):
        b.cyl((-1.12 + i * 0.32, -2.1, 0.02), (-1.12 + i * 0.32, -2.1, 0.03), 0.045, "SM_PaintYellow", 5)


# ------------------------------------------------------------------ bike rack
@asset("bike_rack")
def bike_rack(b):
    n = 6; pitch = 0.7; y0 = -(n - 1) * pitch / 2
    for i in range(n):
        y = y0 + i * pitch
        pts = [(-0.3, y, 0.0), (-0.3, y, 0.55)]
        for k in range(1, 12):
            a = math.pi - k / 12 * math.pi
            pts.append((0.3 * math.cos(a), y, 0.55 + 0.3 * math.sin(a)))
        pts += [(0.3, y, 0.55), (0.3, y, 0.0)]
        b.tube(pts, 0.022, "SM_Steel", 8)
    for sx in (-0.3, 0.3):
        b.box((sx, 0, 0.02), (0.06, n * pitch, 0.04), "SM_SteelDark", 0.008)
    for i in range(n):
        for sx in (-0.3, 0.3):
            b.cyl((sx, y0 + i * pitch, 0.04), (sx, y0 + i * pitch, 0.055), 0.04, "SM_Steel", 8)


# ------------------------------------------------------------------ bollards
def bollard(b, x, y=0.0):
    lathe_shift(b, x, y, [(0.1, 0), (0.1, 0.02), (0.055, 0.05), (0.055, 0.7), (0.052, 0.74), (0.04, 0.8), (0.0, 0.82)], "SM_SteelDark", 14)
    lathe_shift(b, x, y, [(0.057, 0.55), (0.057, 0.62), (0.054, 0.62), (0.054, 0.55)], "SM_PaintYellow", 14)
    lathe_shift(b, x, y, [(0.057, 0.34), (0.057, 0.4)], "SM_PaintWhite", 14)
    lathe_shift(b, x, y, [(0.0, 0.0), (0.0, 0.0)], "SM_Steel", 4) if False else None


@asset("bollard", lod=False)
def bollard_single(b):
    bollard(b, 0)


@asset("bollard_chain", lod=False)
def bollard_chain(b):
    bollard(b, -1.0); bollard(b, 1.0)
    b.sphere((-1.0, 0, 0.8), 0.045, "SM_Steel", 8); b.sphere((1.0, 0, 0.8), 0.045, "SM_Steel", 8)
    for ch in range(2):
        z = 0.74 - ch * 0.12
        b.tube(catenary((-1.0, 0, z), (1.0, 0, z), 0.16, 16), 0.012, "SM_SteelDark", 6, cap=False)
        for k in range(1, 16, 2):
            q = catenary((-1.0, 0, z), (1.0, 0, z), 0.16, 16)[k]
            b.box((q.x, 0, q.z), (0.05, 0.02, 0.03), "SM_Steel", 0.004, seg=1)


# ------------------------------------------------------------------ guardrail
@asset("guardrail_section", lod=False)
def guardrail(b):
    prof = [(-0.04, 0.50), (-0.01, 0.53), (-0.01, 0.58), (-0.04, 0.62), (-0.01, 0.66), (-0.01, 0.72), (-0.04, 0.76), (-0.04, 0.80)]
    prof = [(y + 0.0, z) for y, z in prof]
    # beam: 4 m, slight overlap bevel; one-sided ribbons both faces
    b.prism_profile(prof, -2.0, 2.0, "SM_PaintWhite")
    for x in (-2.0, 0.0, 2.0):
        b.box((x, 0.1, 0.46), (0.09, 0.16, 0.92), "SM_PaintWhite", 0.012)
        b.box((x, 0.0, 0.65), (0.08, 0.1, 0.18), "SM_SteelDark", 0.008)
        b.box((x, 0.1, 0.94), (0.11, 0.18, 0.03), "SM_SteelDark", 0.008)
        for k in range(2):
            b.cyl((x - 0.02 + k * 0.04, -0.05, 0.62), (x - 0.02 + k * 0.04, -0.07, 0.62), 0.016, "SM_Steel", 6)
    b.box((0.0, -0.058, 0.78), (0.14, 0.012, 0.05), "SM_Reflector", 0.003)
    b.box((1.0, -0.058, 0.78), (0.14, 0.012, 0.05), "SM_PaintYellow", 0.003)
    b.box((-1.0, -0.058, 0.78), (0.14, 0.012, 0.05), "SM_PaintYellow", 0.003)


# ------------------------------------------------------------------ manhole / grate
@asset("manhole_cover", lod=False)
def manhole(b):
    lathe_shift(b, 0, 0, [(0.33, 0), (0.33, 0.03), (0.3, 0.03)], "SM_ConcreteDark", 32)
    lathe_shift(b, 0, 0, [(0.3, 0.0), (0.3, 0.03)], "SM_Steel", 32)
    b.disc((0, 0, 0.032), 0.3, "SM_Manhole", 32, uv_r=0.319)


@asset("drain_grate", lod=False)
def grate(b):
    b.box((0, 0, 0.02), (0.62, 0.38, 0.04), "SM_ConcreteDark", 0.006)
    b.box((0, 0, 0.0), (0.5, 0.26, 0.02), "SM_PaintBlack", 0.002, seg=1)
    for i in range(11):
        b.box((-0.225 + i * 0.045, 0, 0.034), (0.014, 0.25, 0.026), "SM_SteelDark", 0.003, seg=1)
    b.box((0, 0, 0.034), (0.5, 0.014, 0.022), "SM_SteelDark", 0.003, seg=1)


# ------------------------------------------------------------------ trash / recycling bins
@asset("recycling_bins")
def bins(b):
    cols = [("SM_PlasticRed", "もえる"), ("SM_PlasticBlue", "ペット"), ("SM_PlasticGreen", "かん・びん")]
    b.box((0, 0.12, 0.05), (1.75, 0.5, 0.1), "SM_ConcreteDark", 0.01)
    for i, (m, label) in enumerate(cols):
        x = (i - 1) * 0.56
        b.box((x, 0, 0.5), (0.5, 0.42, 0.78), m, 0.02)
        b.box((x, 0, 0.92), (0.54, 0.46, 0.07), "SM_PaintBlack", 0.015, rot=(-8, 0, 0))
        b.box((x, -0.225, 0.9), (0.28, 0.03, 0.14), "SM_PaintBlack", 0.015)           # slot
        b.box((x, -0.236, 0.9), (0.22, 0.01, 0.06), "SM_PaintBlack", 0.004, seg=1)
        b.box((x, -0.216, 0.52), (0.36, 0.015, 0.28), "SM_TextWhite", 0.006)
        b.text(label, (x, -0.228, 0.52), 0.1 if len(label) < 4 else 0.075, "SM_PaintBlack", 0.004)
        b.box((x, 0.0, 0.14), (0.52, 0.44, 0.04), "SM_PaintBlack", 0.008)
        b.cyl((x - 0.18, -0.215, 0.4), (x + 0.18, -0.215, 0.4), 0.012, "SM_Steel", 6)


# ------------------------------------------------------------------ A-frame menu
@asset("a_frame_menu", lod=False)
def a_frame(b):
    for sy, rx in ((-1, -12), (1, 12)):
        cy = sy * 0.1
        b.box((0, cy, 0.5), (0.62, 0.035, 1.0), "SM_Wood", 0.008, rot=(rx, 0, 0))
        if sy < 0:
            b.box((0, cy - 0.02, 0.52), (0.5, 0.012, 0.8), "SM_Chalkboard", 0.003, rot=(rx, 0, 0))
    b.cyl((-0.32, 0, 0.99), (0.32, 0, 0.99), 0.012, "SM_Steel", 6)
    for i, (txt, z, sz) in enumerate([("本日", 0.78, 0.09), ("ラーメン ¥900", 0.6, 0.075), ("餃子 ¥400", 0.46, 0.07)]):
        # sits on the tilted chalkboard (tilt -12 deg)
        b.text(txt, (0, -0.1 - 0.02 - 0.012 - (z - 0.5) * 0.0 + (z - 0.5) * 0.21 * -0, z), sz, "SM_TextWhite", 0.003, rot=(78, 0, 0))
    b.box((0, -0.19, 0.04), (0.64, 0.05, 0.05), "SM_Wood", 0.006)
    b.box((0, 0.2, 0.04), (0.64, 0.05, 0.05), "SM_Wood", 0.006)


# ------------------------------------------------------------------ awning shopfront
@asset("awning_shopfront")
def awning(b):
    W = 3.2
    b.box((0, 0.15, 1.7), (W + 0.2, 0.3, 3.4), "SM_Concrete", 0.015)
    b.box((-0.4, -0.02, 1.25), (2.0, 0.04, 1.5), "SM_PaintBlack", 0.01)
    b.box((-0.4, -0.045, 1.25), (1.84, 0.012, 1.34), "SM_LampWarm", 0.004)
    for i in range(1, 4):
        b.box((-1.4 + i * 0.5, -0.055, 1.25), (0.025, 0.02, 1.34), "SM_PaintBlack", 0.003, seg=1)
    b.box((-0.4, -0.055, 1.25), (1.9, 0.02, 0.025), "SM_PaintBlack", 0.003, seg=1)
    b.box((1.1, -0.04, 1.1), (0.8, 0.05, 2.1), "SM_PaintBlack", 0.01)                    # door
    b.box((1.1, -0.07, 1.1), (0.64, 0.015, 1.9), "SM_Glass", 0.003)
    b.cyl((0.85, -0.09, 1.0), (0.85, -0.09, 1.2), 0.012, "SM_Steel", 6)
    n = 8; sw = W / n
    for i in range(n):
        xa = -W / 2 + i * sw; xb = xa + sw
        m = "SM_AwningRed" if i % 2 == 0 else "SM_AwningCream"
        b.quad((xa, -1.1, 2.45), (xb, -1.1, 2.45), (xb, -0.1, 2.95), (xa, -0.1, 2.95), m)
        b.quad((xa, -1.1, 2.45), (xb, -1.1, 2.45), (xb, -1.1, 2.18), (xa, -1.1, 2.18), m)
        b.quad((xa, -1.1, 2.18), (xb, -1.1, 2.18), (xb, -1.08, 2.14), (xa, -1.08, 2.14), m)
    for sx in (-1, 1):
        b.cyl((sx * (W / 2 - 0.05), -0.12, 2.95), (sx * (W / 2 - 0.05), -1.1, 2.47), 0.015, "SM_SteelDark", 6)
    b.box((0, -1.115, 2.3), (1.1, 0.01, 0.14), "SM_PaintBlack", 0.003, seg=1)
    b.text("うどん", (0, -1.125, 2.31), 0.1, "SM_TextWhite", 0.004)
    b.box((0, -0.6, 2.4), (1.8, 0.12, 0.04), "SM_LampWarm", 0.005)


# ------------------------------------------------------------------ post box
@asset("post_box", lod=False)
def post_box(b):
    b.box((0, 0, 0.03), (0.55, 0.55, 0.06), "SM_ConcreteDark", 0.01)
    b.lathe([(0.0, 0.06), (0.26, 0.06), (0.27, 0.1), (0.25, 0.14), (0.25, 0.9), (0.27, 0.93), (0.27, 1.0), (0.23, 1.1), (0.14, 1.17), (0.0, 1.19)], "SM_PaintRed", 24)
    b.box((0, -0.25, 0.86), (0.26, 0.06, 0.05), "SM_PaintBlack", 0.008)               # slot
    b.box((0, -0.255, 0.74), (0.24, 0.02, 0.1), "SM_PaintYellow", 0.004)
    b.text("〒", (0, -0.268, 0.5), 0.2, "SM_PaintWhite" if False else "SM_TextWhite", 0.008)
    b.box((0, -0.255, 0.28), (0.2, 0.02, 0.1), "SM_TextWhite", 0.004)
    b.text("ポスト", (0, -0.267, 0.28), 0.06, "SM_PaintBlack", 0.003)
    b.box((0, -0.26, 0.15), (0.2, 0.02, 0.05), "SM_PaintBlack", 0.003)


# ------------------------------------------------------------------ rooftop units
@asset("rooftop_ac_units")
def ac_units(b):
    for sx in (-1.0, 0.0, 1.0):
        b.box((sx * 1.05, 0, 0.12), (0.12, 0.7, 0.24), "SM_SteelDark", 0.008)   # skid rails
        b.box((sx * 1.05 - 0.3, 0, 0.12), (0.12, 0.7, 0.24), "SM_SteelDark", 0.008) if False else None
    b.box((0, 0, 0.07), (3.3, 0.06, 0.14), "SM_SteelDark", 0.006)
    b.box((0, 0.5, 0.07), (3.3, 0.06, 0.14), "SM_SteelDark", 0.006)
    for i, sx in enumerate((-1.1, 0.0, 1.1)):
        h = 0.8 + 0.1 * (i % 2)
        b.box((sx, 0.25, 0.15 + h / 2), (0.95, 0.42, h), "SM_PaintWhite", 0.03)
        b.box((sx, 0.25, 0.15 + h + 0.015), (0.97, 0.44, 0.03), "SM_Plastic", 0.01)
        # round fan grille on the front
        cz = 0.15 + h * 0.55
        b.box((sx, 0.04, cz), (0.78, 0.02, h * 0.82), "SM_PaintBlack", 0.008)
        for r in (0.1, 0.2, 0.3):
            ring = [(sx + r * math.cos(k / 20 * math.tau), 0.02, cz + r * math.sin(k / 20 * math.tau)) for k in range(21)]
            b.tube(ring, 0.005, "SM_Steel", 4, cap=False)
        for k in range(8):
            a = k / 8 * math.pi
            b.cyl((sx - 0.32 * math.cos(a), 0.02, cz - 0.32 * math.sin(a)), (sx + 0.32 * math.cos(a), 0.02, cz + 0.32 * math.sin(a)), 0.004, "SM_Steel", 4)
        for k in range(4):                                       # fan blades
            a = k / 4 * math.tau + 0.4
            b.box((sx + 0.08 * math.cos(a), 0.07, cz + 0.08 * math.sin(a)), (0.16, 0.012, 0.06), "SM_SteelDark", 0.004, rot=(0, math.degrees(a), 0), seg=1)
        b.cyl((sx, 0.05, cz), (sx, 0.02, cz), 0.04, "SM_Steel", 8)
        # side louvres
        for k in range(8):
            b.box((sx + 0.485, 0.25, 0.3 + k * 0.07), (0.012, 0.3, 0.025), "SM_PaintBlack", 0.002, seg=1)
        # pipes
        b.tube([(sx - 0.2, 0.46, 0.4), (sx - 0.2, 0.7, 0.4), (sx - 0.2, 0.8, 0.9), (sx - 0.2, 0.8, 1.6)], 0.025, "SM_PaintBlack", 6)
        b.tube([(sx - 0.1, 0.46, 0.5), (sx - 0.1, 0.72, 0.5), (sx - 0.1, 0.84, 0.95), (sx - 0.1, 0.84, 1.6)], 0.017, "SM_Steel", 6)
    b.box((0, 0.84, 1.0), (3.4, 0.05, 0.05), "SM_SteelDark", 0.005)


# ------------------------------------------------------------------ rooftop water tank
@asset("rooftop_water_tank")
def water_tank(b):
    for sx in (-0.75, 0.75):
        for sy in (-0.75, 0.75):
            b.box((sx, sy, 0.7), (0.12, 0.12, 1.4), "SM_SteelDark", 0.01)
            b.box((sx, sy, 0.02), (0.3, 0.3, 0.04), "SM_Steel", 0.006)
    for z in (0.35, 0.95):
        for (a, c) in (((-0.75, -0.75), (0.75, -0.75)), ((0.75, -0.75), (0.75, 0.75)), ((0.75, 0.75), (-0.75, 0.75)), ((-0.75, 0.75), (-0.75, -0.75))):
            b.cyl((a[0], a[1], z), (c[0], c[1], z + 0.55), 0.015, "SM_Steel", 6)
    b.box((0, 0, 1.43), (1.8, 1.8, 0.06), "SM_SteelDark", 0.008)
    b.lathe([(0.0, 1.46), (0.82, 1.46), (0.9, 1.5), (0.92, 1.6), (0.92, 3.0), (0.9, 3.1), (0.8, 3.18), (0.5, 3.24), (0.0, 3.25)], "SM_Tank", 28, vtile=1.0)
    for z in (1.8, 2.3, 2.8):
        b.lathe([(0.915, z), (0.945, z), (0.945, z + 0.06), (0.915, z + 0.06)], "SM_SteelDark", 28)
    b.cyl((0.0, 0.0, 3.2), (0.0, 0.0, 3.4), 0.28, "SM_SteelDark", 14)
    b.cyl((0.0, 0.0, 3.4), (0.0, 0.0, 3.42), 0.3, "SM_Steel", 14)
    # ladder
    for sx in (-0.15, 0.15):
        b.cyl((0.9 + 0.0, sx, 0.0), (0.95, sx, 3.3), 0.016, "SM_Steel", 6)
    for k in range(12):
        z = 0.3 + k * 0.26
        b.cyl((0.93, -0.15, z), (0.93, 0.15, z), 0.011, "SM_Steel", 5)
    # inlet pipes, valve, gauge
    b.tube([(-0.5, -0.6, 1.46), (-0.5, -1.0, 1.46), (-0.5, -1.0, 0.0)], 0.035, "SM_PaintBlue", 8)
    b.cyl((-0.5, -1.0, 0.5), (-0.5, -1.2, 0.5), 0.022, "SM_Steel", 6)
    b.sphere((-0.5, -1.2, 0.5), 0.05, "SM_PaintRed", 8)
    b.tube([(0.6, -0.7, 1.7), (0.7, -0.95, 1.7), (0.7, -0.95, 2.9)], 0.014, "SM_Glass", 6)
    b.box((0.0, -0.93, 2.2), (0.12, 0.025, 0.2), "SM_PaintYellow", 0.004)

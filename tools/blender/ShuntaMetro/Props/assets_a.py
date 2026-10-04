"""Street furniture A: vending machines, lamps, utility poles, lanterns, noren, neon blades, signals."""
import math, random
from mathutils import Vector
from props_lib import *

ASSETS = {}
SPAN = [30.0]


def asset(name, lod=True, ratio=0.4):
    def deco(fn):
        ASSETS[name] = (fn, lod, ratio)
        return fn
    return deco


# ------------------------------------------------------------------ vending machines
def vending(b, tag, body, w, hdr_text, hdr_mat, text_mat, d=0.78, h=1.83, extra=False):
    hw = w / 2; fy = -d / 2; gx0 = -hw + 0.06; gx1 = hw - 0.27
    b.box((0, 0, h / 2 + 0.05), (w, d, h - 0.1), body, 0.014)
    b.box((0, 0, 0.04), (w - 0.05, d - 0.05, 0.08), "SM_PaintBlack", 0.005)
    b.box((0, 0, h - 0.02), (w + 0.03, d + 0.03, 0.05), "SM_PaintBlack", 0.01)
    gz0, gz1 = 0.92, 1.60
    # frame around the display glass
    b.box((0, fy - 0.01, gz1 + 0.03), (gx1 - gx0 + 0.06, 0.03, 0.06), "SM_PaintBlack", 0.006)
    b.box((0, fy - 0.01, gz0 - 0.03), (gx1 - gx0 + 0.06, 0.03, 0.06), "SM_PaintBlack", 0.006)
    b.box((gx0 - 0.03, fy - 0.01, (gz0 + gz1) / 2), (0.06, 0.03, gz1 - gz0), "SM_PaintBlack", 0.006)
    b.box((gx1 + 0.03, fy - 0.01, (gz0 + gz1) / 2), (0.06, 0.03, gz1 - gz0), "SM_PaintBlack", 0.006)
    b.front_panel(gx0, gx1, gz0, gz1, fy - 0.004, "SM_VendFront" + tag)
    # header
    b.box((0, fy - 0.012, 1.71), (w - 0.1, 0.03, 0.2), hdr_mat, 0.008)
    b.text(hdr_text, (0, fy - 0.03, 1.71), 0.13, text_mat, 0.006, xscale=1.0)
    # selection buttons
    nbtn = 7 if w < 1.2 else 10
    for i in range(nbtn):
        x = gx0 + 0.04 + i * ((gx1 - gx0 - 0.08) / (nbtn - 1))
        b.box((x, fy - 0.02, 0.85), (0.045, 0.014, 0.03), "SM_NeonRed" if i % 3 else "SM_NeonWhite", 0.003, seg=1)
    # coin/card column
    cx = hw - 0.14
    b.box((cx, fy - 0.01, 1.08), (0.19, 0.025, 1.0), "SM_PaintBlack", 0.008)
    b.box((cx, fy - 0.026, 1.42), (0.1, 0.01, 0.045), "SM_NeonRed", 0.003)           # price display
    b.box((cx, fy - 0.026, 1.28), (0.07, 0.012, 0.014), "SM_Steel", 0.003)           # coin slot
    b.box((cx, fy - 0.026, 1.14), (0.1, 0.012, 0.03), "SM_NeonGreen", 0.003)         # bill slot glow
    b.box((cx, fy - 0.03, 0.98), (0.1, 0.012, 0.1), "SM_NeonBlue", 0.004)             # IC card reader
    b.cyl((cx, fy - 0.026, 0.80), (cx, fy - 0.04, 0.80), 0.035, "SM_Steel", 14)        # return lever
    # pickup
    b.box((-0.12 if w < 1.2 else -0.25, fy - 0.01, 0.38), (0.5, 0.025, 0.24), "SM_PaintBlack", 0.008)
    b.box((-0.12 if w < 1.2 else -0.25, fy - 0.03, 0.38), (0.44, 0.012, 0.18), "SM_Plastic", 0.006)
    b.box((-0.12 if w < 1.2 else -0.25, fy - 0.04, 0.45), (0.12, 0.012, 0.02), "SM_Steel", 0.004)
    # warmth/cold labels, side logo band
    if extra:
        b.box((hw - 0.14, fy - 0.026, 0.62), (0.1, 0.01, 0.06), "SM_NeonCyan", 0.003)
        b.box((hw - 0.14, fy - 0.026, 0.52), (0.1, 0.01, 0.06), "SM_NeonRed", 0.003)
    b.box((0, fy - 0.012, 0.13), (w - 0.1, 0.02, 0.07), hdr_mat, 0.005)               # lit kick strip
    # side band
    for sx in (-1, 1):
        b.box((sx * (hw + 0.002), 0, 0.9), (0.006, d * 0.5, 0.5), "SM_VendBodyWhite", 0.002)
    # ventilation louvres on the back
    for i in range(6):
        b.box((0, d / 2 + 0.004, 0.25 + i * 0.05), (w * 0.6, 0.01, 0.012), "SM_PaintBlack", 0.002, seg=1)


@asset("vending_drink_a")
def vending_a(b):
    vending(b, "A", "SM_VendBodyRed", 1.0, "つめた～い", "SM_NeonWhite", "SM_PaintRed")


@asset("vending_drink_b")
def vending_b(b):
    vending(b, "B", "SM_VendBodyBlue", 1.0, "あったか～い", "SM_NeonCyan", "SM_PaintBlue", extra=True)


@asset("vending_drink_wide")
def vending_c(b):
    vending(b, "C", "SM_VendBodyWhite", 1.3, "ドリンク", "SM_NeonYellow", "SM_PaintBlack", d=0.82, extra=True)


# ------------------------------------------------------------------ lamps
def pole_rings(b, z_list, r, mat="SM_PaintBlack"):
    for z in z_list:
        b.lathe([(r, z), (r + 0.015, z), (r + 0.015, z + 0.05), (r, z + 0.05)], mat, 10)


@asset("street_lamp_jp")
def street_lamp(b):
    b.box((0, 0, 0.1), (0.46, 0.46, 0.2), "SM_Concrete", 0.02)
    b.lathe([(0.17, 0.2), (0.17, 0.3), (0.12, 0.38), (0.095, 0.7), (0.065, 6.1), (0.07, 6.2)], "SM_PaintGreen", 10)
    pole_rings(b, [1.2, 3.0, 5.0], 0.09)
    b.box((0, -0.075, 1.6), (0.09, 0.02, 0.22), "SM_PaintWhite", 0.003)           # number plate
    for a in range(4):
        ang = a * math.pi / 2 + math.pi / 4
        b.sphere((0.17 * math.cos(ang), 0.17 * math.sin(ang), 0.24), 0.018, "SM_Steel", 6)   # bolts
    pts = []
    for i in range(11):
        t = i / 10; pts.append((0, -1.9 * t ** 1.2, 6.15 + 0.7 * math.sin(t * math.pi / 2) ** 1.3))
    b.tube(pts, 0.05, "SM_PaintGreen", 8, taper=0.035)
    hz = 6.8; hy = -2.0
    b.box((0, hy, hz), (0.4, 0.82, 0.1), "SM_PaintGreen", 0.02)
    b.box((0, hy, hz + 0.07), (0.3, 0.7, 0.05), "SM_PaintGreen", 0.015)
    b.box((0, hy, hz - 0.058), (0.34, 0.74, 0.02), "SM_LampCool", 0.004)
    for i in range(4):
        b.box((0, hy - 0.3 + i * 0.2, hz + 0.1), (0.2, 0.02, 0.02), "SM_SteelDark", 0.003, seg=1)


@asset("highway_lamp")
def highway_lamp(b):
    b.cyl((0, 0, 0), (0, 0, 0.04), 0.3, "SM_Steel", 16)
    b.lathe([(0.3, 0.0), (0.3, 0.02), (0.2, 0.06), (0.17, 0.3), (0.085, 10.5), (0.075, 10.7)], "SM_SteelDark", 12)
    for a in range(8):
        ang = a * math.tau / 8
        b.cyl((0.24 * math.cos(ang), 0.24 * math.sin(ang), 0.02), (0.24 * math.cos(ang), 0.24 * math.sin(ang), 0.07), 0.025, "SM_Steel", 6)
    b.box((0, -0.09, 1.3), (0.4, 0.02, 0.5), "SM_PaintYellow", 0.004)             # access door
    pts = [(0, -3.4 * (i / 12) ** 1.1, 10.5 + 0.6 * math.sin(i / 12 * math.pi / 2)) for i in range(13)]
    b.tube(pts, 0.065, "SM_SteelDark", 8, taper=0.045)
    b.cyl((0, -2.2, 10.7), (0, -2.2, 11.2), 0.03, "SM_Steel", 6)
    hy = -3.5; hz = 11.1
    b.box((0, hy, hz), (0.5, 1.15, 0.14), "SM_SteelDark", 0.03)
    b.box((0, hy, hz - 0.075), (0.42, 1.05, 0.03), "SM_LampCool", 0.006)
    for i in range(6):
        b.box((0, hy - 0.45 + i * 0.18, hz + 0.09), (0.32, 0.03, 0.03), "SM_Steel", 0.004, seg=1)


# ------------------------------------------------------------------ utility poles
def lathe_shift(b, x, y, prof, mat, seg=8, vtile=1.0):
    n0 = set(b.bm.faces)
    b.lathe(prof, mat, seg, vtile)
    verts = {v for f in [f for f in b.bm.faces if f not in n0] for v in f.verts}
    bmesh.ops.translate(b.bm, vec=Vector((x, y, 0)), verts=list(verts))


def insulator(b, x, y, z0, mat="SM_Insulator"):
    lathe_shift(b, x, y, [(0.03, z0), (0.055, z0 + 0.03), (0.03, z0 + 0.05), (0.06, z0 + 0.08), (0.03, z0 + 0.11),
                          (0.065, z0 + 0.14), (0.025, z0 + 0.19), (0.0, z0 + 0.19)], mat, 8)


def build_pole(b, kind):
    top = 10.0
    b.lathe([(0.24, 0), (0.24, 0.28), (0.19, 0.32), (0.188, 0.5), (0.115, top)], "SM_Concrete", 12)
    for i in range(14):
        s = 1 if i % 2 else -1
        b.box((s * 0.17, 0.0, 2.4 + i * 0.46), (0.12, 0.03, 0.03), "SM_Steel", 0.004, seg=1)
    b.box((0, -0.19, 3.0), (0.2, 0.012, 0.28), "SM_PaintYellow", 0.003)
    b.box((0, -0.196, 3.12), (0.2, 0.012, 0.05), "SM_PaintBlack", 0.002, seg=1)
    b.tube([(0.14, 0.12, 0.0), (0.14, 0.12, 5.0), (0.12, 0.1, 9.0)], 0.011, "SM_Cable", 4)
    # top crossarm + insulators
    b.box((0, 0, 9.2), (2.0, 0.1, 0.09), "SM_SteelDark", 0.006)
    for s in (-1, 1):
        b.cyl((s * 0.12, 0.0, 9.1), (s * 0.78, 0.0, 9.19), 0.018, "SM_SteelDark", 4)
    for x in (-0.82, 0.0, 0.82):
        insulator(b, x, 0, 9.245)
    # lower comms arm
    b.box((0, 0, 8.2), (1.2, 0.08, 0.08), "SM_SteelDark", 0.006)
    for x in (-0.45, 0.45):
        insulator(b, x, 0, 8.24)
    if kind == "xfmr":
        b.box((0, -0.3, 7.5), (1.7, 0.09, 0.1), "SM_SteelDark", 0.006)
        b.cyl((0, -0.2, 7.5), (0, -0.3, 7.5), 0.04, "SM_SteelDark", 6)
        for x in (-0.55, 0.0, 0.55):
            lathe_shift(b, x, -0.38, [(0.0, 7.4), (0.2, 7.4), (0.215, 7.46), (0.215, 8.0), (0.19, 8.05), (0.0, 8.05)], "SM_Transformer", 12, 1.0)
            lathe_shift(b, x, -0.38, [(0.215, 7.55), (0.225, 7.55), (0.225, 7.6), (0.215, 7.6)], "SM_SteelDark", 12)
            lathe_shift(b, x, -0.38, [(0.215, 7.95), (0.225, 7.95), (0.225, 8.0), (0.215, 8.0)], "SM_SteelDark", 12)
            insulator(b, x, -0.38, 8.05, "SM_Insulator")
            b.tube([(x, -0.38, 8.24), (x * 1.2, -0.2, 8.7), (x * 1.5, -0.04, 9.4)], 0.008, "SM_Cable", 4)
        b.box((0.5, 0.17, 6.4), (0.3, 0.12, 0.38), "SM_PaintGreen", 0.01)           # fuse / meter box
        b.box((0.5, 0.108, 6.45), (0.18, 0.01, 0.12), "SM_Glass", 0.003)
    else:  # board + lamp
        b.box((0, -0.2, 5.1), (0.46, 0.04, 1.0), "SM_PaintYellow", 0.006)
        for i, ch in enumerate("歯科医"):
            b.text(ch, (0, -0.232, 5.4 - i * 0.3), 0.26, "SM_PaintBlack", 0.004)
        b.box((0, -0.2, 5.1), (0.52, 0.03, 0.04), "SM_PaintBlack", 0.004)
        pts = [(0, -0.05, 7.3), (0, -0.4, 7.5), (0, -1.0, 7.55), (0, -1.5, 7.4)]
        b.tube(pts, 0.03, "SM_SteelDark", 6)
        b.box((0, -1.62, 7.36), (0.32, 0.5, 0.1), "SM_PaintGrey" if False else "SM_SteelDark", 0.02)
        b.box((0, -1.62, 7.30), (0.26, 0.42, 0.02), "SM_LampWarm", 0.004)
        b.box((0.0, -0.17, 4.0), (0.05, 0.05, 0.4), "SM_PaintBlack", 0.004)
        b.cyl((0.0, -0.17, 0.7), (0.0, -0.17, 3.9), 0.02, "SM_PaintBlack", 5)
    # cable spans toward the next pole 30 m ahead (-Y)
    L = SPAN[0]
    for x in (-0.82, 0.0, 0.82):
        b.tube(catenary((x, 0, 9.43), (x, -L, 9.43), 0.9, 12), 0.012, "SM_Cable", 4, cap=False)
    for x in (-0.45, 0.45):
        b.tube(catenary((x, 0, 8.42), (x, -L, 8.42), 0.6, 12), 0.014, "SM_Cable", 4, cap=False)
    b.tube(catenary((0.0, 0.12, 6.9), (0.0, -L + 0.0, 6.9), 0.4, 12), 0.022, "SM_Cable", 5, cap=False)


@asset("utility_pole_xfmr", lod=False)
def pole_a(b):
    build_pole(b, "xfmr")


@asset("utility_pole_lamp", lod=False)
def pole_b(b):
    build_pole(b, "lamp")


# ------------------------------------------------------------------ chochin lanterns
def chochin(b, x, y, ztop, scale=1.0, mat="SM_Chochin", glyph=None, glyph_mat="SM_PaintBlack"):
    H = 0.46 * scale; R = 0.17 * scale
    prof = [(0.05 * scale, 0)]
    for i in range(9):
        t = i / 8
        prof.append((0.07 * scale + (R - 0.07 * scale) * (math.sin(t * math.pi) ** 0.6), 0.03 * scale + (H - 0.06 * scale) * t))
    prof.append((0.05 * scale, H))
    # body (shifted lathe), hanging so top is at ztop
    n0 = set(b.bm.faces)
    b.lathe([(r, z) for r, z in prof[1:-1]], mat, 14, vtile=0.5 * scale)
    verts = {v for f in [f for f in b.bm.faces if f not in n0] for v in f.verts}
    bmesh.ops.translate(b.bm, vec=Vector((x, y, ztop - H)), verts=list(verts))
    for zc, zz in ((ztop - H + 0.015 * scale, 0), (ztop - 0.015 * scale, 1)):
        lathe_shift(b, x, y, [(0.065 * scale, zc - 0.02 * scale), (0.08 * scale, zc - 0.02 * scale), (0.08 * scale, zc + 0.02 * scale), (0.065 * scale, zc + 0.02 * scale)], "SM_PaintBlack", 12)
        lathe_shift(b, x, y, [(0.0, zc - 0.02 * scale), (0.08 * scale, zc - 0.02 * scale), (0.08 * scale, zc - 0.02 * scale + 0.001)], "SM_PaintBlack", 12)
    b.cyl((x, y, ztop), (x, y, ztop + 0.08 * scale), 0.008 * scale, "SM_PaintBlack", 4)
    b.cyl((x, y, ztop - H), (x, y, ztop - H - 0.06 * scale), 0.004 * scale, "SM_PaintBlack", 4)
    b.sphere((x, y, ztop - H - 0.075 * scale), 0.012 * scale, "SM_PaintRed", 6)
    if glyph:
        b.text(glyph, (x, y - R - 0.004 * scale, ztop - H / 2), 0.2 * scale, glyph_mat, 0.006)


def stand(b, h, arm=0.0):
    b.cyl((0, 0, 0), (0, 0, 0.05), 0.2, "SM_SteelDark", 16)
    b.lathe([(0.2, 0.05), (0.2, 0.06), (0.04, 0.09), (0.03, 0.2), (0.025, h)], "SM_SteelDark", 8)


@asset("chochin_stand")
def chochin_stand(b):
    stand(b, 2.3)
    b.tube([(0, 0, 2.28), (0, -0.2, 2.34), (0, -0.5, 2.3)], 0.016, "SM_SteelDark", 6)
    chochin(b, 0, -0.5, 2.28, 1.0, glyph="酒")
    b.tube([(0, 0, 1.7), (0, -0.2, 1.74), (0, -0.4, 1.7)], 0.012, "SM_SteelDark", 6)
    chochin(b, 0, -0.4, 1.7, 0.7, "SM_ChochinWhite", glyph="食", glyph_mat="SM_PaintRed")


@asset("chochin_row", lod=False)
def chochin_row(b):
    for sx in (-2.2, 2.2):
        b.cyl((sx, 0, 0), (sx, 0, 0.05), 0.2, "SM_SteelDark", 16)
        lathe_shift(b, sx, 0, [(0.2, 0.05), (0.2, 0.06), (0.04, 0.09), (0.03, 0.2), (0.025, 3.0)], "SM_SteelDark", 8)
        b.sphere((sx, 0, 3.02), 0.04, "SM_SteelDark", 6)
    pts = catenary((-2.2, 0, 2.95), (2.2, 0, 2.95), 0.28, 14)
    b.tube(pts, 0.01, "SM_Cable", 4, cap=False)
    for i in range(8):
        t = (i + 0.5) / 8
        x = -2.2 + 4.4 * t; z = 2.95 - 0.28 * 4 * t * (1 - t) - 0.02
        chochin(b, x, 0, z, 0.8, "SM_Chochin" if i % 2 == 0 else "SM_ChochinWhite")


@asset("chochin_large")
def chochin_large(b):
    stand(b, 1.6)
    b.cyl((0, 0, 0.0), (0, 0, 0.1), 0.3, "SM_SteelDark", 16)
    chochin(b, 0, 0, 1.75, 2.0, glyph=None)
    for i, ch in enumerate("居酒屋"):
        b.text(ch, (0, -0.35, 1.75 - 0.18 - i * 0.26), 0.24, "SM_PaintBlack", 0.01)


# ------------------------------------------------------------------ noren door
def cloth(b, x0, x1, ztop, zbot, y, mat, cols=3, rows=6, amp=0.012, ph=0.0):
    def P(i, j):
        x = x0 + (x1 - x0) * i / cols; z = ztop - (ztop - zbot) * j / rows
        return (x, y - amp * math.sin(z * 7 + x * 4 + ph) * (j / rows), z)
    for i in range(cols):
        for j in range(rows):
            b.quad(P(i, j + 1), P(i + 1, j + 1), P(i + 1, j), P(i, j), mat,
                   uvs=((i / cols, (j + 1) / rows), ((i + 1) / cols, (j + 1) / rows), ((i + 1) / cols, j / rows), (i / cols, j / rows)))


def noren(b, chars, mat, text_mat, ph=0.0):
    w = 1.5; zt = 2.25
    for sx in (-1, 1):
        b.box((sx * (w / 2 + 0.07), 0.0, 1.2), (0.14, 0.2, 2.4), "SM_Wood", 0.01)
    b.box((0, 0, 2.36), (w + 0.4, 0.22, 0.14), "SM_Wood", 0.012)
    b.box((0, 0, 0.05), (w + 0.4, 0.3, 0.1), "SM_Stone", 0.01)
    b.cyl((-w / 2, -0.02, zt), (w / 2, -0.02, zt), 0.015, "SM_Steel", 8)
    b.box((0, 0.06, 1.1), (w - 0.1, 0.03, 2.1), "SM_PaintBlack", 0.005)          # dark interior
    n = len(chars); pw = w / n
    for i in range(n):
        xa = -w / 2 + i * pw + 0.02; xb = xa + pw - 0.04
        cloth(b, xa, xb, zt - 0.02, 1.35, -0.05, mat, 2, 6, 0.015, ph + i)
        b.text(chars[i], ((xa + xb) / 2, -0.075, 1.82), 0.3, text_mat, 0.006)
    b.box((0, -0.12, 2.58), (0.8, 0.06, 0.22), "SM_PaintBlack", 0.01)           # small lit sign over door
    b.box((0, -0.15, 2.58), (0.72, 0.02, 0.15), "SM_NeonWhite", 0.004)
    b.text("営業中", (0, -0.165, 2.58), 0.11, "SM_PaintRed", 0.004)


@asset("noren_doorway_a", lod=False)
def noren_a(b):
    noren(b, "らーめん", "SM_Cloth", "SM_TextWhite")


@asset("noren_doorway_b", lod=False)
def noren_b(b):
    noren(b, "居酒屋", "SM_ClothRed", "SM_TextWhite", 2.0)


# ------------------------------------------------------------------ neon blade signs
def blade(b, chars, neon, text_mat, trim):
    n = len(chars); pitch = 0.5; H = n * pitch + 0.5; zb = 2.4; W = 0.62
    b.cyl((0, 0, 0), (0, 0, 0.08), 0.25, "SM_SteelDark", 16)
    b.lathe([(0.25, 0.08), (0.06, 0.12), (0.045, 0.3), (0.045, zb + 0.1)], "SM_SteelDark", 10)
    b.box((0, 0, zb + H / 2), (W, 0.16, H), "SM_PaintBlack", 0.02)
    for sx in (-1, 1):
        b.box((sx * (W / 2 - 0.02), -0.082, zb + H / 2), (0.03, 0.02, H - 0.1), trim, 0.004)
    b.box((0, -0.082, zb + 0.05), (W - 0.06, 0.02, 0.03), trim, 0.004)
    b.box((0, -0.082, zb + H - 0.05), (W - 0.06, 0.02, 0.03), trim, 0.004)
    for sy, rz in ((-1, 90), (1, 90)):
        for i, ch in enumerate(chars):
            zc = zb + H - 0.5 - i * pitch + 0.1
            if sy < 0:
                b.text(ch, (0, -0.092, zc), 0.4, text_mat, 0.02)
            else:
                b.text(ch, (0, 0.092, zc), 0.4, text_mat, 0.02, rot=(90, 0, 180))
        b.box((0, sy * 0.082, zb + 0.28), (0.24, 0.02, 0.16), trim, 0.004)
    b.box((0, 0, zb + H + 0.05), (W + 0.04, 0.2, 0.1), "SM_SteelDark", 0.02)
    b.cyl((0, 0, zb + H + 0.1), (0, 0, zb + H + 0.45), 0.012, "SM_Steel", 5)


@asset("neon_blade_izakaya")
def blade_a(b):
    blade(b, "居酒屋", "SM_NeonPink", "SM_NeonPink", "SM_NeonCyan")


@asset("neon_blade_ramen")
def blade_b(b):
    blade(b, "ラーメン", "SM_NeonYellow", "SM_NeonYellow", "SM_NeonRed")


@asset("neon_blade_karaoke")
def blade_c(b):
    blade(b, "カラオケ", "SM_NeonCyan", "SM_NeonCyan", "SM_NeonPink")


# ------------------------------------------------------------------ traffic signals
def signal_head(b, y, z, lit):
    # horizontal 3-lens head facing +-X
    b.box((0, y, z), (0.3, 1.04, 0.36), "SM_PaintBlack", 0.025)
    order = [("SM_LampGreen" if lit == "g" else "SM_LampOffGreen"), "SM_LampAmber", ("SM_LampRed" if lit == "r" else "SM_LampOffRed")]
    for i, lm in enumerate(order):
        yy = y - 0.33 + i * 0.33
        for sx in (-1, 1):
            b.cyl((sx * 0.14, yy, z), (sx * 0.17, yy, z), 0.125, lm, 16)
            b.box((sx * 0.2, yy, z + 0.15), (0.14, 0.27, 0.025), "SM_PaintBlack", 0.005, seg=1)       # visor
            b.box((sx * 0.165, yy + 0.13, z + 0.09), (0.12, 0.02, 0.15), "SM_PaintBlack", 0.004, seg=1)
            b.box((sx * 0.165, yy - 0.13, z + 0.09), (0.12, 0.02, 0.15), "SM_PaintBlack", 0.004, seg=1)
    b.box((0, y, z + 0.19), (0.12, 0.2, 0.04), "SM_SteelDark", 0.006)


def ped_head(b, x, y, z, lit="g", yaw_front=True):
    sgn = -1  # faces -Y
    b.box((x, y, z), (0.36, 0.14, 0.42), "SM_PaintBlack", 0.015)
    b.box((x, y + sgn * 0.075, z), (0.30, 0.02, 0.36), "SM_PaintBlack", 0.01)
    m = "SM_LampGreen" if lit == "g" else "SM_LampRed"
    yy = y + sgn * 0.088
    b.sphere((x, yy, z + 0.12), 0.034, m, 8)
    b.box((x, yy, z + 0.02), (0.07, 0.01, 0.15), m, 0.005, seg=1)
    if lit == "g":
        b.box((x - 0.045, yy, z - 0.01), (0.03, 0.01, 0.13), m, 0.003, rot=(0, 0, 0), seg=1)
        b.box((x + 0.05, yy, z - 0.11), (0.03, 0.01, 0.12), m, 0.003, rot=(0, 20, 0), seg=1)
        b.box((x - 0.04, yy, z - 0.11), (0.03, 0.01, 0.12), m, 0.003, rot=(0, -20, 0), seg=1)
    else:
        b.box((x - 0.04, yy, z - 0.11), (0.03, 0.01, 0.12), m, 0.003, seg=1)
        b.box((x + 0.04, yy, z - 0.11), (0.03, 0.01, 0.12), m, 0.003, seg=1)
        b.box((x - 0.08, yy, z + 0.01), (0.03, 0.01, 0.1), m, 0.003, seg=1)
        b.box((x + 0.08, yy, z + 0.01), (0.03, 0.01, 0.1), m, 0.003, seg=1)
    b.box((x, y - 0.0, z + 0.235), (0.38, 0.17, 0.02), "SM_PaintBlack", 0.004)


def traffic_mast(b, lit):
    b.cyl((0, 0, 0), (0, 0, 0.08), 0.22, "SM_Steel", 16)
    b.lathe([(0.22, 0.08), (0.17, 0.12), (0.125, 0.3), (0.085, 5.6)], "SM_Steel", 12)
    for a in range(6):
        ang = a * math.tau / 6
        b.cyl((0.18 * math.cos(ang), 0.18 * math.sin(ang), 0.08), (0.18 * math.cos(ang), 0.18 * math.sin(ang), 0.13), 0.02, "SM_SteelDark", 6)
    pts = [(0, -5.0 * (i / 14), 5.5 + 0.25 * (i / 14) ** 2 * 1.0 + 0.0) for i in range(15)]
    b.tube(pts, 0.075, "SM_Steel", 10, taper=0.045)
    b.cyl((0, -0.1, 5.15), (0, -2.2, 5.55), 0.03, "SM_Steel", 6)
    signal_head(b, -2.1, 5.2, lit)
    signal_head(b, -4.1, 5.22, lit)
    b.box((0, -2.1, 5.5), (0.06, 0.12, 0.12), "SM_SteelDark", 0.01)
    b.box((0, -4.1, 5.5), (0.06, 0.12, 0.12), "SM_SteelDark", 0.01)
    ped_head(b, 0.0, -0.17, 2.6, "g")
    b.box((0.0, -0.13, 1.35), (0.14, 0.07, 0.22), "SM_PaintYellow", 0.01)       # push button box
    b.cyl((0, -0.17, 1.37), (0, -0.2, 1.37), 0.035, "SM_NeonGreen", 12)


@asset("traffic_light_mast")
def tl_mast(b):
    traffic_mast(b, "r")


@asset("traffic_light_pole")
def tl_pole(b):
    b.cyl((0, 0, 0), (0, 0, 0.06), 0.18, "SM_Steel", 14)
    b.lathe([(0.18, 0.06), (0.11, 0.2), (0.075, 4.3)], "SM_Steel", 10)
    b.tube([(0, 0, 4.1), (0, -0.4, 4.35), (0, -0.9, 4.3)], 0.04, "SM_Steel", 8)
    signal_head(b, -1.1, 4.2, "g")
    ped_head(b, 0.0, -0.13, 2.4, "r")
    b.box((0.0, -0.1, 1.3), (0.14, 0.07, 0.22), "SM_PaintYellow", 0.01)


@asset("ped_signal_pole")
def ped_pole(b):
    b.cyl((0, 0, 0), (0, 0, 0.05), 0.16, "SM_SteelDark", 14)
    b.lathe([(0.16, 0.05), (0.1, 0.1), (0.055, 2.7)], "SM_PaintBlack", 10)
    ped_head(b, 0.0, -0.1, 2.35, "g")
    ped_head(b, 0.0, -0.1, 1.85, "r")
    b.box((0.0, -0.09, 1.2), (0.14, 0.07, 0.22), "SM_PaintYellow", 0.01)
    b.cyl((0, -0.13, 1.22), (0, -0.16, 1.22), 0.035, "SM_NeonGreen", 12)
    b.box((0.0, -0.09, 0.95), (0.1, 0.04, 0.14), "SM_Steel", 0.006)
    for i in range(4):
        b.box((0.0, -0.115, 0.89 + i * 0.03), (0.07, 0.01, 0.01), "SM_PaintBlack", 0.001, seg=1)
    b.sphere((0, 0, 2.74), 0.065, "SM_PaintBlack", 8)


@asset("crosswalk_sign_pole")
def cw_pole(b):
    b.cyl((0, 0, 0), (0, 0, 0.05), 0.15, "SM_SteelDark", 14)
    b.lathe([(0.15, 0.05), (0.1, 0.1), (0.045, 2.9)], "SM_Steel", 10)
    for sx in (-1, 1):
        b.box((sx * 0.02, 0, 2.8), (0.02, 0.6, 0.6), "SM_SignBlue", 0.01)
        b.box((sx * 0.032, 0, 2.7), (0.008, 0.5, 0.04), "SM_Pictogram", 0.002, seg=1)
        b.box((sx * 0.032, 0, 2.62), (0.008, 0.5, 0.04), "SM_Pictogram", 0.002, seg=1)
        # pedestrian figure
        b.sphere((sx * 0.034, 0.0, 2.97), 0.035, "SM_Pictogram", 8)
        b.box((sx * 0.034, 0.0, 2.86), (0.008, 0.07, 0.15), "SM_Pictogram", 0.002, seg=1)
        b.box((sx * 0.034, 0.03, 2.74), (0.008, 0.035, 0.12), "SM_Pictogram", 0.002, rot=(25, 0, 0), seg=1)
        b.box((sx * 0.034, -0.03, 2.74), (0.008, 0.035, 0.12), "SM_Pictogram", 0.002, rot=(-25, 0, 0), seg=1)
        b.box((sx * 0.034, 0.15, 2.9), (0.008, 0.05, 0.045), "SM_Pictogram", 0.002, seg=1)
        b.box((sx * 0.034, -0.15, 2.9), (0.008, 0.05, 0.045), "SM_Pictogram", 0.002, seg=1)
    b.box((0, 0, 2.495), (0.05, 0.66, 0.03), "SM_Steel", 0.004)
    b.box((0, 0, 3.125), (0.05, 0.66, 0.03), "SM_Steel", 0.004)

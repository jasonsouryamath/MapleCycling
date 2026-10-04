"""
E2 (milestone 1)  Taka Mountains (Mt. Ventoux) - village / forest / moonscape / summit kit.
Claude worker D, 2026-09-30.   LOD0 / LOD1 / LOD2 GLBs in Assets/Environment/TakaMountains/Models:

    TakaK2_Provence{A,B,C}   Provencal village houses (genoise cornice, roman tile, louvred shutters, balcony)
    TakaK2_PlaneTreeA/B      mottled-bark plane trees (village avenue)
    TakaK2_CedarA/B          Atlas cedar: stacked flat blue-green tiers on a pale trunk
    TakaK2_Beech             smooth-barked beech, wide domed crown
    TakaK2_Pine              black pine: bare trunk, clustered crown pads
    TakaK2_Fountain          village fountain: stone basin, shaft, spouts
    TakaK2_Scree{A,B,C}      angular white limestone scree blocks
    TakaK2_Outcrop{A,B}      bedded limestone outcrops (5-8 m)
    TakaK2_Cairn / CairnTall wind-scoured stacked cairns
    TakaK2_Borne_<km>        kilometre bornes: white post, yellow cap, 3D 'km to summit' + gradient text (LOD0)
                             TakaK2_BorneBlank_LOD1/2 shared lower LODs
    TakaK2_WeatherTower      red/white striped weather tower + lattice antenna mast
    TakaK2_SummitHall        low concrete/stone summit buildings (restaurant block)
    TakaK2_Memorial          fictional granite memorial stele on a stepped base

Origin = base centre on ground; +z = front. Materials TakaK2_<Role>, re-materialised by TakaMountains.Kit2.cs.
Run:  tools\\blender-4.5.10-windows-x64\\blender.exe -b --factory-startup -P tools/blender/build_taka_kit.py
All dimensions are PROVISIONAL art tuning.
"""

import json
import math
import os
import random
import shutil
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mr_kit2 as K  # noqa: E402
from mr_kit2 import S, Rx, Ry, Rz  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

ROOT = S.repo_root()
TAKA = os.path.join(ROOT, "Assets", "Environment", "TakaMountains")
MODELS = os.path.join(TAKA, "Models")
TEXTURES = os.path.join(TAKA, "Textures")
FUJI_TEX = os.path.join(ROOT, "Assets", "Environment", "FujiRidge", "Textures")
os.makedirs(MODELS, exist_ok=True)
S.blender_assets_dir = lambda: MODELS

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SUMMIT_M = 15400.0

ROLES = {
    "StuccoOchre":  ((0.90, 0.68, 0.40), 0.93, 0.0, 4.0, False),
    "StuccoRose":   ((0.88, 0.62, 0.52), 0.93, 0.0, 4.0, False),
    "StuccoCream":  ((0.95, 0.85, 0.66), 0.93, 0.0, 4.0, False),
    "Stone":        ((0.82, 0.78, 0.70), 0.95, 0.0, 2.0, False),
    "Limestone":    ((0.88, 0.86, 0.80), 0.92, 0.0, 3.0, False),
    "Tile":         ((0.72, 0.38, 0.22), 0.86, 0.0, 1.2, False),
    "ShutterBlue":  ((0.34, 0.54, 0.66), 0.7, 0.0, 1.0, False),
    "ShutterGreen": ((0.40, 0.55, 0.38), 0.7, 0.0, 1.0, False),
    "ShutterLilac": ((0.56, 0.50, 0.70), 0.7, 0.0, 1.0, False),
    "Wood":         ((0.40, 0.28, 0.18), 0.75, 0.0, 1.0, False),
    "Iron":         ((0.07, 0.07, 0.075), 0.45, 0.6, 1.0, False),
    "Glass":        ((0.10, 0.13, 0.16), 0.08, 0.0, 1.0, False),
    "Interior":     ((0.06, 0.05, 0.045), 0.95, 0.0, 1.0, False),
    "Terracotta":   ((0.66, 0.34, 0.20), 0.85, 0.0, 1.0, False),
    "Geranium":     ((0.86, 0.12, 0.14), 0.8, 0.0, 1.0, True),
    "Leaf":         ((0.18, 0.36, 0.14), 0.8, 0.0, 1.0, True),
    "BarkPlane":    ((0.68, 0.64, 0.52), 0.92, 0.0, 1.5, True),
    "PlaneLeaf":    ((0.30, 0.47, 0.20), 0.85, 0.0, 2.0, True),
    "Bark":         ((0.40, 0.31, 0.24), 0.95, 0.0, 1.5, True),
    "PineBark":     ((0.48, 0.30, 0.21), 0.95, 0.0, 1.5, True),
    "CedarLeaf":    ((0.17, 0.31, 0.29), 0.88, 0.0, 2.0, True),
    "BeechLeaf":    ((0.25, 0.42, 0.15), 0.85, 0.0, 2.0, True),
    "PineLeaf":     ((0.12, 0.25, 0.11), 0.9, 0.0, 2.0, True),
    "Water":        ((0.25, 0.45, 0.52), 0.05, 0.0, 4.0, False),
    "Scree":        ((0.90, 0.88, 0.82), 0.94, 0.0, 2.5, False),
    "Concrete":     ((0.70, 0.70, 0.68), 0.95, 0.0, 2.0, False),
    "TowerRed":     ((0.78, 0.17, 0.14), 0.6, 0.0, 1.0, False),
    "TowerWhite":   ((0.92, 0.92, 0.90), 0.6, 0.0, 1.0, False),
    "Steel":        ((0.55, 0.57, 0.60), 0.45, 0.7, 1.0, False),
    "Granite":      ((0.40, 0.40, 0.42), 0.7, 0.0, 1.5, False),
    "Brass":        ((0.72, 0.56, 0.22), 0.35, 0.8, 1.0, False),
    "BorneWhite":   ((0.93, 0.92, 0.88), 0.85, 0.0, 1.0, False),
    "BorneYellow":  ((0.95, 0.78, 0.12), 0.7, 0.0, 1.0, False),
    "BorneBlack":   ((0.05, 0.05, 0.055), 0.8, 0.0, 1.0, False),
    "Red":          ((0.9, 0.1, 0.08), 0.5, 0.0, 1.0, False),
}


def kit():
    return K.Kit("TakaK2", ROLES)


def finish(k, name, bevel=None):
    ob = k.build(name, bevel=bevel, tag="taka-k2")
    K.export(ob, name + ".glb")
    return ob


# ============================================================================ Provencal houses

ROOF_PITCH = 22.0


def window(k, lod, x, y, w, h, shut, state, rng, z0=0.0):
    """Reveal frame + dark glazing + louvred shutter leaves. Faces +z on the facade plane z=z0."""
    k.aabb("Interior", x - w / 2, x + w / 2, y, y + h, z0 - 0.2, z0 - 0.12)
    # surround (limestone)
    s = 0.09
    k.aabb("Limestone", x - w / 2 - s, x - w / 2, y - 0.05, y + h + 0.1, z0 - 0.2, z0 + 0.07)
    k.aabb("Limestone", x + w / 2, x + w / 2 + s, y - 0.05, y + h + 0.1, z0 - 0.2, z0 + 0.07)
    k.aabb("Limestone", x - w / 2 - s, x + w / 2 + s, y + h, y + h + 0.1, z0 - 0.2, z0 + 0.07)
    k.aabb("Limestone", x - w / 2 - s - 0.03, x + w / 2 + s + 0.03, y - 0.1, y - 0.03, z0 - 0.2, z0 + 0.13)
    if lod == 0:
        k.aabb("Glass", x - w / 2, x + w / 2, y, y + h, z0 - 0.17, z0 - 0.15)
        k.aabb("Wood", x - 0.02, x + 0.02, y, y + h, z0 - 0.17, z0 - 0.12)
        k.aabb("Wood", x - w / 2, x + w / 2, y + h * 0.62, y + h * 0.62 + 0.03, z0 - 0.17, z0 - 0.12)
    # shutter leaves
    lw = w / 2
    if state == "closed":
        leaves = [(x - lw / 2, 0.0), (x + lw / 2, 0.0)]
        lfw = lw + 0.01
    else:
        leaves = [(x - w / 2 - 0.02 - lw / 2, 0.0), (x + w / 2 + 0.02 + lw / 2, 0.0)]
        lfw = lw
    for cx, _ in leaves:
        zz = z0 + 0.09 if state == "closed" else z0 + 0.03
        k.aabb(shut, cx - lfw / 2, cx + lfw / 2, y - 0.03, y + h + 0.03, zz, zz + 0.04)
        if lod == 0:
            n = 7
            for i in range(n):
                yy = y + 0.02 + (h - 0.04) * (i + 0.5) / n
                k.aabb("Interior", cx - lfw / 2 + 0.04, cx + lfw / 2 - 0.04, yy - 0.012, yy + 0.012, zz + 0.04, zz + 0.055)


def balcony(k, lod, x0, x1, y, rng):
    k.aabb("Limestone", x0, x1, y - 0.12, y + 0.04, 0.0, 0.8)
    for cx in (x0 + 0.25, x1 - 0.25):
        k.aabb("Limestone", cx - 0.1, cx + 0.1, y - 0.55, y - 0.12, 0.0, 0.3)
    if lod == 0:
        k.aabb("Iron", x0, x1, y + 0.95, y + 0.99, 0.76, 0.8)
        k.aabb("Iron", x0, x0 + 0.04, y + 0.04, y + 1.0, 0.0, 0.8)
        k.aabb("Iron", x1 - 0.04, x1, y + 0.04, y + 1.0, 0.0, 0.8)
        k.aabb("Iron", x0, x1, y + 0.95, y + 0.99, 0.0, 0.04)
        n = int((x1 - x0) / 0.14)
        for i in range(n):
            xx = x0 + 0.05 + (x1 - x0 - 0.1) * i / max(n - 1, 1)
            k.aabb("Iron", xx - 0.01, xx + 0.01, y + 0.04, y + 0.95, 0.77, 0.79)
        # potted geraniums
        for i in range(3):
            xx = x0 + 0.4 + i * (x1 - x0 - 0.8) / 2
            k.cyl("Terracotta", (xx, y + 0.04, 0.5), 0.1, 0.07, 0.2, n=8)
            k.leafy("Geranium", (xx, y + 0.3, 0.5), 0.16, i + x0, sy=0.8, seg=6, rings=3)


def provence(tag, W, D, floors, wall, shut, seed, balc, ground="door"):
    rng = random.Random(seed)
    GH, FH = 3.2, 3.0
    H = GH + FH * (floors - 1)
    out = []
    for lod in (0, 1, 2):
        k = kit()
        # body + plinth
        k.aabb(wall, -W / 2, W / 2, -3.0, H, -D, 0.0)
        k.aabb("Stone", -W / 2 - 0.03, W / 2 + 0.03, -3.0, 0.5, -D - 0.03, 0.06)   # stone base course
        if lod < 2:
            # quoins
            q = 0.0
            i = 0
            while q < H - 0.3:
                for sx in (-1, 1):
                    ax = 0.5 if i % 2 == 0 else 0.28
                    xa, xb = sorted((sx * (W / 2 + 0.02), sx * (W / 2 - ax)))
                    k.aabb("Limestone", xa, xb, q + 0.02, q + 0.48, -0.0, 0.05)
                q += 0.5
                i += 1
                if lod == 1:
                    break            # openings per storey
            bays = max(2, int(W // 2.6))
            for fl in range(floors):
                y = 0.0 if fl == 0 else GH + FH * (fl - 1)
                for b in range(bays):
                    x = (b + 0.5) / bays * W - W / 2
                    if fl == 0 and b == bays // 2:
                        # arched door with stone surround
                        k.aabb("Interior", x - 0.55, x + 0.55, 0.0, 2.35, -0.25, -0.1)
                        k.aabb("Wood", x - 0.55, x + 0.55, 0.0, 2.3, -0.2, -0.12)
                        k.aabb("Limestone", x - 0.72, x - 0.55, 0.0, 2.45, -0.25, 0.08)
                        k.aabb("Limestone", x + 0.55, x + 0.72, 0.0, 2.45, -0.25, 0.08)
                        k.aabb("Limestone", x - 0.72, x + 0.72, 2.3, 2.5, -0.25, 0.1)
                        if lod == 0:
                            for r in range(3):
                                k.aabb("Iron", x - 0.5, x + 0.5, 0.5 + r * 0.7, 0.54 + r * 0.7, -0.12, -0.1)
                            k.aabb("Limestone", x - 0.8, x + 0.8, -0.0, 0.1, 0.0, 0.55)   # step
                        continue
                    hh = 1.45 if fl else 1.35
                    window(k, lod, x, y + (1.0 if fl else 1.05), 0.9, hh, shut, "closed" if (b + fl + seed) % 3 == 0 else "open", rng)
            # balcony on first floor (centre bays)
            if balc and floors > 1:
                bx0 = -W / 2 + W * 0.28
                bx1 = W / 2 - W * 0.28
                balcony(k, lod, bx0, bx1, GH - 0.05, rng)
            # string course + genoise cornice (stepped tile rows)
            k.aabb("Limestone", -W / 2 - 0.05, W / 2 + 0.05, GH - 0.1, GH + 0.04, -D - 0.05, 0.1)
        # roof (low pitch gable along x; ridge over the middle)
        t = math.tan(math.radians(ROOF_PITCH))
        ov = 0.55
        ye = H + 0.1
        yr = ye + (D / 2 + ov) * t
        if lod < 2:
            for zs, z_eave in ((1, 0.0 + ov), (-1, -D - ov)):
                pass
            k.hexa("Tile", [(-W / 2 - 0.25, ye, ov), (W / 2 + 0.25, ye, ov), (W / 2 + 0.25, yr, -D / 2), (-W / 2 - 0.25, yr, -D / 2)], (0, 0.14, 0))
            k.hexa("Tile", [(-W / 2 - 0.25, ye, -D - ov), (W / 2 + 0.25, ye, -D - ov), (W / 2 + 0.25, yr, -D / 2), (-W / 2 - 0.25, yr, -D / 2)], (0, 0.14, 0))
            for sx in (-1, 1):
                k.tri_prism(wall, [(sx * W / 2, H, 0.0), (sx * W / 2, H, -D), (sx * W / 2, yr, -D / 2)], (-sx * 0.28, 0, 0))
            if lod == 0:
                # genoise: 2 rows of tile ends under the front eave
                for xx in np.arange(-W / 2 - 0.1, W / 2 + 0.2, 0.2):
                    k.aabb("Tile", xx, xx + 0.17, H - 0.12, H + 0.1, 0.06, ov - 0.05)
                k.aabb("Tile", -W / 2 - 0.27, W / 2 + 0.27, yr + 0.1, yr + 0.22, -D / 2 - 0.12, -D / 2 + 0.12)   # ridge
                # chimney
                cx = W / 2 - 1.3 if seed % 2 else -W / 2 + 1.3
                k.aabb(wall, cx - 0.35, cx + 0.35, yr - 0.8, yr + 1.1, -D * 0.6 - 0.3, -D * 0.6 + 0.3)
                k.aabb("Tile", cx - 0.42, cx + 0.42, yr + 1.1, yr + 1.2, -D * 0.6 - 0.37, -D * 0.6 + 0.37)
                # drain pipe
                k.cyl("Iron", (W / 2 - 0.08, -0.3, 0.1), 0.05, 0.05, H + 0.2, n=6)
        else:
            k.hexa("Tile", [(-W / 2 - 0.25, ye, ov), (W / 2 + 0.25, ye, ov), (W / 2 + 0.25, yr, -D / 2), (-W / 2 - 0.25, yr, -D / 2)], (0, 0.14, 0))
            k.hexa("Tile", [(-W / 2 - 0.25, ye, -D - ov), (W / 2 + 0.25, ye, -D - ov), (W / 2 + 0.25, yr, -D / 2), (-W / 2 - 0.25, yr, -D / 2)], (0, 0.14, 0))
        out.append(finish(k, f"TakaK2_Provence{tag}_LOD{lod}",
                          bevel={"Limestone": 0.01, "Stone": 0.012} if lod == 0 else None))
    return out


# ============================================================================ trees

def plane_tree(variant, h, lod, seed):
    rng = random.Random(seed)
    k = kit()
    tr = h * 0.3
    k.tube("BarkPlane", [(0, -0.3, 0), (0.05, tr * 0.5, 0.04), (-0.05, tr, 0.0)], [0.27, 0.22, 0.2], n=(10 if lod == 0 else 6))
    nb = (5, 3, 0)[lod]
    tips = []
    for i in range(nb):
        a = i / nb * math.tau + rng.uniform(-0.3, 0.3)
        tip = (math.cos(a) * h * 0.26, h * rng.uniform(0.6, 0.72), math.sin(a) * h * 0.26)
        tips.append(tip)
        if lod < 2:
            k.tube("BarkPlane", [(-0.05, tr, 0), (tip[0] * 0.5, tr + (tip[1] - tr) * 0.5, tip[2] * 0.5), tip], [0.17, 0.12, 0.07], n=(8 if lod == 0 else 5))
    cn = (14, 5, 0)[lod]
    for i in range(cn):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0.0, h * 0.3)
        y = h * rng.uniform(0.6, 0.92)
        r = h * rng.uniform(0.15, 0.22)
        if lod < 2:
            k.leafy("PlaneLeaf", (math.cos(a) * d, y, math.sin(a) * d), r, rng.uniform(0, 40), sy=0.75, amp=0.3,
                    seg=(8 if lod == 0 else 6), rings=(5 if lod == 0 else 3))
    if lod == 2:
        k.leafy("PlaneLeaf", (0, h * 0.72, 0), h * 0.32, 4.0, sy=0.78, amp=0.12, seg=6, rings=3)
        k.cyl("BarkPlane", (0, -0.2, 0), 0.25, 0.18, h * 0.45, n=5)
    return finish(k, f"TakaK2_PlaneTree{variant}_LOD{lod}")


def cedar(variant, h, lod, seed):
    """Atlas cedar: flat tiered branch plates, leader straight up, trunk pale and fluted."""
    rng = random.Random(seed)
    k = kit()
    k.tube("Bark", [(0, -0.4, 0), (0.03, h * 0.2, 0), (0, h * 0.55, 0.02), (0, h, 0)], [h * 0.028, h * 0.022, h * 0.014, 0.05], n=(9 if lod == 0 else 5))
    tiers = (10, 5, 3)[lod]
    for i in range(tiers):
        t = (i + 0.5) / tiers
        y = h * (0.22 + 0.7 * t)
        R = h * 0.34 * (1 - t) ** 0.8 + h * 0.03
        if lod == 0:
            for j in range(4):
                a = j / 4 * math.tau + i * 0.8 + rng.uniform(-0.3, 0.3)
                k.leafy("CedarLeaf", (math.cos(a) * R * 0.55, y + rng.uniform(-0.2, 0.2), math.sin(a) * R * 0.55), R * 0.62, rng.uniform(0, 60),
                        sy=0.2, amp=0.35, seg=8, rings=4)
        else:
            k.leafy("CedarLeaf", (0, y, 0), R, rng.uniform(0, 60), sy=0.28, amp=0.2, seg=(7 if lod == 1 else 6), rings=3)
    return finish(k, f"TakaK2_Cedar{variant}_LOD{lod}")


def beech(lod, h=16.0, seed=9):
    rng = random.Random(seed)
    k = kit()
    k.tube("BarkPlane", [(0, -0.3, 0), (0.1, h * 0.2, 0.05), (-0.05, h * 0.38, 0)], [0.32, 0.26, 0.2], n=(10 if lod == 0 else 6))
    nb = (5, 3, 0)[lod]
    for i in range(nb):
        a = i / nb * math.tau
        tip = (math.cos(a) * h * 0.2, h * 0.62, math.sin(a) * h * 0.2)
        if lod < 2:
            k.tube("BarkPlane", [(-0.05, h * 0.38, 0), (tip[0] * 0.5, h * 0.5, tip[2] * 0.5), tip], [0.18, 0.12, 0.07], n=(8 if lod == 0 else 5))
    cn = (18, 6, 0)[lod]
    for i in range(cn):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0.0, h * 0.28)
        y = h * rng.uniform(0.5, 0.92)
        r = h * rng.uniform(0.15, 0.22)
        if lod < 2:
            k.leafy("BeechLeaf", (math.cos(a) * d, y, math.sin(a) * d), r, rng.uniform(0, 50), sy=0.8, amp=0.3,
                    seg=(8 if lod == 0 else 6), rings=(5 if lod == 0 else 3))
    if lod == 2:
        k.leafy("BeechLeaf", (0, h * 0.68, 0), h * 0.34, 5.0, sy=0.8, amp=0.12, seg=6, rings=3)
        k.cyl("BarkPlane", (0, -0.2, 0), 0.3, 0.2, h * 0.45, n=5)
    return finish(k, f"TakaK2_Beech_LOD{lod}")


def pine(lod, h=15.0, seed=13):
    rng = random.Random(seed)
    k = kit()
    k.tube("PineBark", [(0, -0.3, 0), (0.18, h * 0.3, 0.08), (-0.1, h * 0.62, -0.05), (0.05, h * 0.9, 0)],
           [0.3, 0.22, 0.15, 0.08], n=(9 if lod == 0 else 5))
    pads = (7, 4, 2)[lod]
    for i in range(pads):
        t = (i + 0.4) / pads
        y = h * (0.55 + 0.43 * t)
        R = h * (0.2 - 0.07 * t) + 0.5
        a = i * 2.4
        if lod < 2:
            for j in range(2 if lod == 0 else 1):
                k.leafy("PineLeaf", (math.cos(a + j * 3.1) * R * 0.35, y + 0.2 * j, math.sin(a + j * 3.1) * R * 0.35), R * 0.8, rng.uniform(0, 70),
                        sy=0.34, amp=0.38, seg=(8 if lod == 0 else 6), rings=(4 if lod == 0 else 3))
        else:
            k.leafy("PineLeaf", (0, y, 0), R, rng.uniform(0, 70), sy=0.35, amp=0.15, seg=6, rings=3)
    return finish(k, f"TakaK2_Pine_LOD{lod}")


# ============================================================================ village fountain

def fountain(lod):
    n = (14, 10, 8)[lod]
    k = kit()
    k.prism_poly("Stone", (0, -0.4, 0), 1.9, 1.85, 1.15, n=n, rot_deg=11)
    k.prism_poly("Stone", (0, 0.75, 0), 2.0, 2.0, 0.1, n=n, rot_deg=11)
    k.prism_poly("Water", (0, 0.2, 0), 1.75, 1.75, 0.5, n=n, rot_deg=11)
    if lod < 2:
        k.cyl("Stone", (0, 0.2, 0), 0.4, 0.28, 1.5, n=n)
        k.prism_poly("Stone", (0, 1.65, 0), 0.3, 0.8, 0.2, n=n)
        k.prism_poly("Water", (0, 1.82, 0), 0.7, 0.7, 0.03, n=n)
        k.blob("Stone", (0, 2.0, 0), 0.16, 1.4, 8, 5)
    if lod == 0:
        for i in range(4):
            a = i / 4 * math.tau
            k.cyl("Brass", (math.cos(a) * 0.38, 0.9, math.sin(a) * 0.38), 0.04, 0.035, 0.18, n=6)
    return finish(k, f"TakaK2_Fountain_LOD{lod}", bevel={"Stone": 0.01} if lod == 0 else None)


# ============================================================================ moonscape

def scree(variant, lod):
    rng = random.Random(300 + variant)
    k = kit()
    seg, rings = ((12, 7), (7, 4), (5, 3))[lod]
    if variant == 0:
        k.rock("Scree", (0, 0.1, 0), 1.2, 1.7, flat=0.7, amp=0.55, seg=seg, rings=rings, ridged=True)
    elif variant == 1:
        for (c, r, s) in (((0, 0.1, 0), 0.9, 3.3), ((0.9, 0, 0.5), 0.55, 5.1), ((-0.7, 0, 0.6), 0.4, 7.7), ((0.2, 0, -0.9), 0.5, 2.9)):
            k.rock("Scree", c, r, s, flat=0.65, amp=0.5, seg=seg, rings=rings, ridged=True)
    else:
        k.rock("Scree", (0, 0.0, 0), 1.9, 8.2, flat=0.34, amp=0.5, seg=seg, rings=rings, ridged=True, sink=0.1)
        if lod < 2:
            for i in range(3):
                k.rock("Scree", (rng.uniform(-1.5, 1.5), 0, rng.uniform(-1.5, 1.5)), rng.uniform(0.3, 0.5), rng.uniform(0, 9), flat=0.6, amp=0.4, seg=max(5, seg - 4), rings=3, ridged=True)
    return finish(k, f"TakaK2_Scree{'ABC'[variant]}_LOD{lod}")


def outcrop(variant, lod):
    """Bedded limestone: stacked tilted slabs with a dark cleft between."""
    rng = random.Random(400 + variant)
    k = kit()
    n = (7, 4, 2)[lod]
    base = 3.2 if variant == 0 else 2.4
    y = -0.5
    for i in range(n):
        r = base * (1 - i * 0.1 / max(n, 1) * 4) * rng.uniform(0.75, 1.0)
        hgt = 0.9 if lod == 0 else 1.4
        seg, rings = ((12, 6), (8, 4), (6, 3))[lod]
        k.rock("Scree", (rng.uniform(-0.5, 0.5), y + hgt * 0.45, rng.uniform(-0.5, 0.5)), r, 20 + i * 3.3 + variant * 7,
               flat=hgt / max(r, 0.1) * 0.7, amp=0.38, seg=seg, rings=rings, ridged=True, sink=0.0)
        y += hgt * 0.62
    return finish(k, f"TakaK2_Outcrop{'AB'[variant]}_LOD{lod}")


def cairn(tall, lod):
    rng = random.Random(500 + int(tall))
    k = kit()
    n = (13 if tall else 7, 6, 3)[lod]
    y = -0.15
    r = 0.62 if tall else 0.55
    for i in range(n):
        t = i / max(n - 1, 1)
        rr = r * (1 - 0.62 * t)
        hh = rr * 0.5
        seg, rings = ((8, 4), (6, 3), (5, 3))[lod]
        k.rock("Scree", (rng.uniform(-0.05, 0.05) * (1 + t), y + hh, rng.uniform(-0.05, 0.05) * (1 + t)), rr, 60 + i * 1.7,
               flat=0.5, amp=0.25, seg=seg, rings=rings, ridged=False, sink=0.5)
        y += hh * 1.25
    return finish(k, f"TakaK2_Cairn{'Tall' if tall else ''}_LOD{lod}")


# ============================================================================ bornes

def route_gradients():
    """Average gradient (%) of the 1 km above each km-to-summit marker, from TakaRoute.json."""
    path = os.path.join(TAKA, "TakaRoute.json")
    ds, ys = [], []
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    for s in data["samples"]:
        ds.append(s["d"])
        ys.append(s["p"][1])
    ds = np.array(ds)
    ys = np.array(ys)

    def y_at(d):
        return float(np.interp(d, ds, ys))
    out = {}
    for n in range(1, 16):
        d0 = SUMMIT_M - n * 1000.0
        d1 = d0 + 1000.0
        out[n] = (y_at(d0), 100.0 * (y_at(d1) - y_at(d0)) / 1000.0)
    return out


def borne(n, alt, grade, lod, blank):
    k = kit()
    seg = (10, 8, 6)[lod]
    k.prism_poly("BorneWhite", (0, -0.25, 0), 0.2, 0.18, 1.1, n=seg, rot_deg=22)
    # head: yellow box, black stripe, text face sits at z=+0.19
    k.aabb("BorneYellow", -0.2, 0.2, 0.85, 1.12, -0.2, 0.2)
    k.aabb("BorneBlack", -0.205, 0.205, 0.85, 0.89, -0.205, 0.205)
    if lod == 0 and not blank:
        k.text("BorneBlack", f"{n} km", (0.0, 1.03, 0.2), 0.075, 0.012, 0.34)
        k.text("BorneBlack", f"{alt:.0f} m", (0.0, 0.96, 0.2), 0.045, 0.012, 0.34)
        k.text("BorneBlack", f"{grade:.1f}%", (0.0, 0.905, 0.2), 0.04, 0.012, 0.34)
    name = f"TakaK2_Borne_{n}_LOD0" if not blank else f"TakaK2_BorneBlank_LOD{lod}"
    return finish(k, name, bevel={"BorneWhite": 0.008} if (lod == 0 and not blank) else None)


# ============================================================================ summit

def weather_tower(lod):
    rng = random.Random(600)
    k = kit()
    n = (20, 12, 8)[lod]
    # base: 2 wide drums (r 7 -> 6.6) then slimmer drums alternating red/white (profile from the old procedural tower)
    y = -1.0
    drums = [(7.0, 5.0), (7.0, 5.0), (3.8, 5.0), (3.6, 5.0), (3.4, 5.0), (3.2, 5.0), (3.0, 5.0)]
    for i, (r, hh) in enumerate(drums):
        role = "TowerRed" if i % 2 == 0 else "TowerWhite"
        k.prism_poly(role, (0, y, 0), r, r * 0.985, hh, n=n, rot_deg=0)
        if lod == 0 and i >= 2:
            # window band on each tower drum
            for j in range(8):
                a = j / 8 * math.tau
                px, pz = math.cos(a) * (r + 0.02), math.sin(a) * (r + 0.02)
                k.box("Glass", (px, y + 2.5, pz), (0.7, 0.9, 0.08), rot=Rz(0) @ Ry(-math.degrees(a) + 90))
        y += hh
    # gallery platform + railing at the top of the base block
    k.prism_poly("Concrete", (0, 9.0, 0), 7.6, 7.6, 0.3, n=n)
    if lod == 0:
        for j in range(32):
            a = j / 32 * math.tau
            k.cyl("Steel", (math.cos(a) * 7.5, 9.3, math.sin(a) * 7.5), 0.03, 0.03, 1.1, n=4)
        k.cyl("Steel", (0, 10.3, 0), 7.5, 7.5, 0.05, n=32, cap=False)
    # radome (white sphere) on the top drum
    k.prism_poly("TowerWhite", (0, y, 0), 2.2, 1.6, 3.0, n=n)
    k.prism_poly("TowerWhite", (0, y + 3.0, 0), 1.6, 1.4, 2.5, n=n)
    k.blob("TowerWhite", (0, y + 5.5, 0), 1.4, 0.8, 12 if lod == 0 else 8, 6 if lod == 0 else 4)
    ytop = y + 6.2
    # lattice antenna mast to ~62 m
    mh = 24.0
    sides = 0.7
    legs = [(sx * sides / 2, sz * sides / 2) for sx in (-1, 1) for sz in (-1, 1)]
    if lod < 2:
        for (lx, lz) in legs:
            k.beam("Steel", (lx, ytop, lz), (lx * 0.35, ytop + mh, lz * 0.35), 0.07, 0.07)
        segs = 12 if lod == 0 else 4
        for i in range(segs):
            t0, t1 = i / segs, (i + 1) / segs
            w0, w1 = sides * (1 - 0.65 * t0) / 2, sides * (1 - 0.65 * t1) / 2
            y0, y1 = ytop + mh * t0, ytop + mh * t1
            if lod == 0:
                k.beam("Steel", (-w0, y0, w0), (w1, y1, w1), 0.03, 0.03)
                k.beam("Steel", (w0, y0, w0), (-w1, y1, w1), 0.03, 0.03)
                k.beam("Steel", (-w0, y0, -w0), (w1, y1, -w1), 0.03, 0.03)
                k.beam("Steel", (w0, y0, -w0), (-w1, y1, -w1), 0.03, 0.03)
                k.beam("Steel", (-w1, y1, -w1), (w1, y1, -w1), 0.03, 0.03)
                k.beam("Steel", (-w1, y1, w1), (w1, y1, w1), 0.03, 0.03)
            else:
                k.aabb("Steel", -w0, w0, y0, y1, -0.02, 0.02)
        # aviation beacon + dish + whip
        k.blob("Red", (0, ytop + mh + 0.15, 0), 0.14, 1.0, 6, 4)
        k.cyl("Steel", (0, ytop + mh + 0.2, 0), 0.01, 0.01, 6.0, n=4)
        if lod == 0:
            k.cyl("TowerWhite", (0.5, ytop + 14.0, 0.0), 0.0, 0.7, 0.25, n=14, axis=Rz(-90))
            k.beam("Steel", (0.0, ytop + 14.0, 0.0), (0.5, ytop + 14.0, 0.0), 0.05, 0.05)
    else:
        k.aabb("Steel", -0.3, 0.3, ytop, ytop + mh, -0.3, 0.3)
    return finish(k, f"TakaK2_WeatherTower_LOD{lod}", bevel={"Concrete": 0.02} if lod == 0 else None)


def summit_hall(lod):
    rng = random.Random(700)
    k = kit()
    W, D, H = 22.0, 9.0, 4.6
    k.aabb("Concrete", -W / 2, W / 2, -1.5, H, -D, 0.0)
    k.aabb("Stone", -W / 2 - 0.1, W / 2 + 0.1, -1.5, 0.8, -D - 0.1, 0.1)   # stone plinth
    k.aabb("Concrete", -W / 2 - 0.3, W / 2 + 0.3, H, H + 0.4, -D - 0.3, 0.45)   # parapet slab
    if lod < 2:
        # ribbon window + doors
        bays = 7
        for i in range(bays):
            cx = -W / 2 + (i + 0.5) * W / bays
            if i == 3:
                k.aabb("Wood", cx - 0.7, cx + 0.7, 0.0, 2.4, -0.05, 0.05)
                k.aabb("Steel", cx - 0.85, cx + 0.85, 2.4, 2.55, -0.05, 0.2)
                continue
            k.aabb("Glass", cx - 1.0, cx + 1.0, 1.0, 3.2, -0.02, 0.04)
            if lod == 0:
                k.aabb("Steel", cx - 1.04, cx + 1.04, 0.96, 1.0, -0.04, 0.08)
                k.aabb("Steel", cx - 1.04, cx + 1.04, 3.2, 3.26, -0.04, 0.08)
                k.aabb("Steel", cx - 0.03, cx + 0.03, 1.0, 3.2, -0.04, 0.06)
        # canopy over the terrace
        k.aabb("Steel", -W / 2, W / 2, H - 0.7, H - 0.62, 0.0, 2.2)
        if lod == 0:
            for xx in np.linspace(-W / 2 + 0.2, W / 2 - 0.2, 8):
                k.cyl("Steel", (xx, -0.1, 2.1), 0.05, 0.05, H - 0.8, n=6)
            # roof plant: air handler + solar panel rack + small mast
            k.aabb("Steel", W / 2 - 4, W / 2 - 1.5, H + 0.4, H + 1.6, -D + 1.0, -D + 3.2)
            for i in range(4):
                k.hexa("Glass", [(-6 + i * 1.5, H + 0.45, -D + 2.5), (-5.1 + i * 1.5, H + 0.45, -D + 2.5), (-5.1 + i * 1.5, H + 1.2, -D + 3.3), (-6 + i * 1.5, H + 1.2, -D + 3.3)], (0, 0.05, 0))
            k.aabb("Steel", -0.03, 0.03, H + 0.4, H + 4.5, -D + 1.0, -D + 1.06)
    return finish(k, f"TakaK2_SummitHall_LOD{lod}", bevel={"Concrete": 0.02} if lod == 0 else None)


def memorial(lod):
    k = kit()
    k.aabb("Granite", -1.3, 1.3, -0.4, 0.2, -1.0, 1.0)
    k.aabb("Granite", -1.0, 1.0, 0.2, 0.45, -0.75, 0.75)
    # stele (slightly tapered, front slanted)
    k.hexa("Granite", [(-0.45, 0.45, 0.28), (0.45, 0.45, 0.28), (0.38, 2.55, 0.22), (-0.38, 2.55, 0.22)], (0, 0, -0.55))
    if lod < 2:
        k.aabb("Brass", -0.3, 0.3, 1.2, 1.9, 0.275, 0.29)
        if lod == 0:
            k.aabb("Brass", -0.24, 0.24, 1.62, 1.84, 0.29, 0.3)          # relief panel
            k.cyl("Brass", (-0.12, 1.45, 0.3), 0.07, 0.07, 0.015, n=10, axis=Rx(90))   # 2 'wheels'
            k.cyl("Brass", (0.12, 1.45, 0.3), 0.07, 0.07, 0.015, n=10, axis=Rx(90))
            k.aabb("Brass", -0.12, 0.12, 1.5, 1.52, 0.3, 0.31)
            # wreath + bidon + stones
            k.leafy("Leaf", (0.0, 0.75, 0.55), 0.24, 2.2, sy=0.4, seg=8, rings=4)
            k.cyl("Red", (0.0, 0.7, 0.6), 0.02, 0.02, 0.1, n=6)
            k.cyl("BorneWhite", (0.7, 0.45, 0.5), 0.045, 0.045, 0.22, n=8)
            for i in range(6):
                k.rock("Scree", (-0.9 + i * 0.1, 0.43, 0.6 - (i % 2) * 0.1), 0.1, i * 1.3, flat=0.6, amp=0.3, seg=6, rings=3)
    return finish(k, f"TakaK2_Memorial_LOD{lod}", bevel={"Granite": 0.012} if lod == 0 else None)


# ============================================================================ textures (copied from the Fuji kit)

def copy_textures():
    pairs = {
        "Fuji_Town_Stucco_Albedo.png": "TakaK2_Stucco_Albedo.png",
        "Fuji_Town_Stucco_Normal.png": "TakaK2_Stucco_Normal.png",
        "Fuji_Town_Masonry_Albedo.png": "TakaK2_Masonry_Albedo.png",
        "Fuji_Town_Masonry_Normal.png": "TakaK2_Masonry_Normal.png",
        "Fuji_Town_Coppi_Albedo.png": "TakaK2_Tile_Albedo.png",
        "Fuji_Town_Coppi_Normal.png": "TakaK2_Tile_Normal.png",
        "Fuji_Town_Wood_Albedo.png": "TakaK2_Wood_Albedo.png",
        "Fuji_Town_Wood_Normal.png": "TakaK2_Wood_Normal.png",
        "Fuji_K2_Bark_Albedo.png": "TakaK2_Bark_Albedo.png",
        "Fuji_K2_Bark_Normal.png": "TakaK2_Bark_Normal.png",
    }
    copied = 0
    for a, b in pairs.items():
        src = os.path.join(FUJI_TEX, a)
        dst = os.path.join(TEXTURES, b)
        if os.path.exists(src) and not os.path.exists(dst):
            shutil.copyfile(src, dst)
            copied += 1
    print(f"[taka-k2] textures copied: {copied}")


def main():
    S.reset_scene()
    copy_textures()
    grads = route_gradients()
    print("[taka-k2] borne gradients:", {n: round(v[1], 1) for n, v in grads.items()})
    houses = [("A", 8.0, 7.5, 3, "StuccoOchre", "ShutterBlue", 11, True),
              ("B", 9.5, 7.5, 2, "StuccoRose", "ShutterGreen", 22, False),
              ("C", 7.5, 7.5, 3, "StuccoCream", "ShutterLilac", 33, True)]
    for h in houses:
        provence(*h)
    for lod in (0, 1, 2):
        plane_tree("A", 11.5, lod, 1)
        plane_tree("B", 9.5, lod, 2)
        cedar("A", 19.0, lod, 3)
        cedar("B", 14.0, lod, 4)
        beech(lod)
        pine(lod)
        fountain(lod)
        for v in range(3):
            scree(v, lod)
        for v in range(2):
            outcrop(v, lod)
        cairn(False, lod)
        cairn(True, lod)
        borne(0, 0, 0, lod, True)
        weather_tower(lod)
        summit_hall(lod)
        memorial(lod)
    for n, (alt, g) in grads.items():
        borne(n, alt, g, 0, False)
    print("[taka-k2] done")


main()

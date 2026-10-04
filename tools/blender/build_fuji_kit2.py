"""
E1 (milestone 2)  Fuji Ridge - landscape + landmark kit.   Claude worker D, 2026-09-30.

Builds, each at LOD0 / LOD1 / LOD2 (Assets/Environment/FujiRidge/Models/FujiK2_<Name>_LOD<n>.glb):

    CypressA / CypressB   Italian cypress, lumpy flame silhouette from overlapping foliage masses
    Olive                 gnarled multi-limb olive with silver-green canopy masses
    VineBay               one 2.4 m trellis bay: bevelled posts, 3 wires, stock, leaf canopy, grapes
    TerraceWall           4 m dry-stone terrace wall: ~55 irregular beveled stones + capstones
    Shrine                wayside edicola: brick pier, arched niche, statue, coppi gable, iron cross
    Hermitage             stone hermit's cell: coursed masonry, slate roof, bell cote, wood pile
    Campanile             brick bell tower: quoins, string courses, belfry arches, clock, copper spire
    Fountain              tiered octagonal piazza fountain, stone + water surfaces
    ScoriaA/B/C           volcanic cinder boulders (ridged noise), FumaroleVent: sulphur-crusted vent

KIT FRAME: Unity coordinates, origin at the base centre on the ground (walls/plinths sink below 0).
Linear: +z is the FRONT (facade / niche faces +z). Materials are named FujiK2_<Role>; the C# stager
(FujiRidge.Kit2.cs) re-materialises them by name onto CelLit using these textures:
    Fuji_K2_{Bark,Scoria}_{Albedo,Normal}.png (generated here) + the existing Fuji_Town_* maps.

Run:  tools\\blender-4.5.10-windows-x64\\blender.exe -b --factory-startup -P tools/blender/build_fuji_kit2.py
All dimensions are PROVISIONAL art tuning.
"""

import math
import os
import random
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mr_kit2 as K  # noqa: E402
from mr_kit2 import S, Rx, Ry, Rz  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

ROOT = S.repo_root()
MODELS = os.path.join(ROOT, "Assets", "Environment", "FujiRidge", "Models")
TEXTURES = os.path.join(ROOT, "Assets", "Environment", "FujiRidge", "Textures")
os.makedirs(MODELS, exist_ok=True)
os.makedirs(TEXTURES, exist_ok=True)
S.blender_assets_dir = lambda: MODELS

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NO_TEX = "--no-textures" in ARGS

# role -> (rgb, roughness, metallic, uv tile m, smooth)
ROLES = {
    "Cypress":    ((0.07, 0.17, 0.08), 0.92, 0.0, 2.0, True),
    "Olive":      ((0.40, 0.47, 0.35), 0.88, 0.0, 2.0, True),
    "VineLeaf":   ((0.28, 0.44, 0.14), 0.85, 0.0, 2.0, True),
    "Grape":      ((0.22, 0.07, 0.26), 0.45, 0.0, 1.0, True),
    "Bark":       ((0.46, 0.38, 0.30), 0.96, 0.0, 1.5, True),
    "Stone":      ((0.80, 0.76, 0.68), 0.95, 0.0, 2.0, False),
    "DryStone":   ((0.66, 0.62, 0.55), 0.96, 0.0, 2.0, False),
    "Brick":      ((0.70, 0.40, 0.29), 0.92, 0.0, 2.0, False),
    "Travertine": ((0.93, 0.89, 0.80), 0.82, 0.0, 1.0, False),
    "Coppi":      ((0.72, 0.38, 0.23), 0.86, 0.0, 1.2, False),
    "Slate":      ((0.30, 0.31, 0.33), 0.80, 0.0, 1.2, False),
    "Copper":     ((0.32, 0.56, 0.50), 0.45, 0.6, 1.0, False),
    "Iron":       ((0.07, 0.07, 0.075), 0.45, 0.6, 1.0, False),
    "Wood":       ((0.45, 0.31, 0.19), 0.75, 0.0, 1.0, False),
    "Water":      ((0.22, 0.40, 0.46), 0.04, 0.0, 4.0, False),
    "Interior":   ((0.06, 0.05, 0.045), 0.95, 0.0, 1.0, False),
    "Terracotta": ((0.66, 0.34, 0.20), 0.85, 0.0, 1.0, False),
    "Flower":     ((0.82, 0.18, 0.20), 0.7, 0.0, 1.0, True),
    "Scoria":     ((0.17, 0.13, 0.12), 0.97, 0.0, 2.5, False),
    "Sulphur":    ((0.84, 0.74, 0.18), 0.8, 0.0, 1.0, False),
}


def kit(tag=""):
    return K.Kit("FujiK2", ROLES)


def finish(k, name, bevel=None):
    ob = k.build(name, bevel=bevel, tag="fuji-k2")
    K.export(ob, name + ".glb")
    return ob


# ============================================================================ trees

def cypress(variant, h, lod, seed):
    rng = random.Random(seed)
    k = kit()
    R = h * 0.115
    n = (26, 9, 0)[lod]
    # trunk base
    if lod == 0:
        k.tube("Bark", [(0, -0.3, 0), (0.02, h * 0.12, 0.01), (0, h * 0.28, 0)], [0.2, 0.15, 0.1], n=8)
    if lod < 2:
        for i in range(n):
            t = (i + 0.5) / n
            prof = (t ** 0.35) * ((1 - t) ** 0.55) * 2.1
            r = max(R * prof, 0.22)
            ang = rng.uniform(0, math.tau)
            off = r * 0.35 * rng.random()
            cy = h * (0.10 + 0.88 * t)
            br = max(r * 0.82, 0.2) * (1.0 if lod == 0 else 1.35)
            seg, rings = (8, 5) if lod == 0 else (6, 3)
            k.leafy("Cypress", (math.cos(ang) * off, cy, math.sin(ang) * off), br, rng.uniform(0, 50),
                    sy=1.9 if lod == 0 else 2.3, amp=0.22, seg=seg, rings=rings)
    else:
        k.sphere_like("Cypress", (0, h * 0.5, 0), R * 1.05, h * 0.5, R * 1.05, 6, 4,
                      disp=lambda d: 1.0 - 0.55 * max(0.0, d.y) ** 3 - 0.2 * max(0.0, -d.y))
    return finish(k, f"FujiK2_Cypress{variant}_LOD{lod}")


def olive(lod, seed=7):
    rng = random.Random(seed)
    k = kit()
    # trunk: twisted, fluted, splits into 3 limbs
    tw = [(0.0, -0.3, 0.0), (0.08, 0.5, 0.05), (-0.06, 1.1, 0.0), (0.05, 1.6, -0.05)]
    tr = [0.34, 0.28, 0.24, 0.2]
    k.tube("Bark", tw, tr, n=(10 if lod == 0 else 6))
    limbs = [((1.3, 3.1, 0.7), 0.1), ((-1.2, 3.3, -0.5), 0.1), ((0.1, 3.6, 1.2), 0.1), ((-0.3, 3.0, -1.2), 0.09)]
    if lod < 2:
        for (tip, rr) in limbs:
            mid = (tip[0] * 0.45 + 0.05, 2.3, tip[2] * 0.45)
            k.tube("Bark", [(0.05, 1.6, -0.05), mid, tip], [0.15, 0.11, rr], n=(8 if lod == 0 else 5))
    nb = (17, 6, 0)[lod]
    for i in range(nb):
        ang = rng.uniform(0, math.tau)
        rad = rng.uniform(0.3, 1.9)
        y = rng.uniform(2.9, 4.6)
        r = rng.uniform(0.85, 1.35)
        seg, rings = (8, 5) if lod == 0 else (6, 3)
        if lod < 2:
            k.leafy("Olive", (math.cos(ang) * rad, y, math.sin(ang) * rad), r, rng.uniform(0, 80),
                    sy=0.62, amp=0.3, seg=seg, rings=rings)
    if lod == 2:
        k.leafy("Olive", (0, 3.7, 0), 2.1, 3.0, sy=0.55, amp=0.12, seg=6, rings=3)
        k.cyl("Bark", (0, -0.2, 0), 0.3, 0.2, 2.4, n=5)
    return finish(k, f"FujiK2_Olive_LOD{lod}")


def vinebay(lod):
    rng = random.Random(5)
    k = kit()
    L = 2.4
    for x in (-L / 2, L / 2):
        k.aabb("Wood", x - 0.045, x + 0.045, -0.3, 1.75, -0.045, 0.045)
        if lod == 0:
            k.aabb("Iron", x - 0.05, x + 0.05, 1.6, 1.64, -0.06, 0.06)   # staple strap
    if lod < 2:
        for y in (0.55, 1.05, 1.55):
            k.aabb("Iron", -L / 2, L / 2, y - 0.007, y + 0.007, -0.008, 0.008)
        k.tube("Bark", [(0.0, -0.1, 0), (0.05, 0.4, 0.04), (-0.02, 0.8, 0.0)], [0.055, 0.045, 0.035], n=(7 if lod == 0 else 4))
    nb = (8, 3, 0)[lod]
    for i in range(nb):
        x = -L / 2 + 0.2 + (L - 0.4) * (i + rng.random() * 0.4) / max(nb, 1)
        y = rng.uniform(0.75, 1.5)
        if lod < 2:
            k.leafy("VineLeaf", (x, y, rng.uniform(-0.05, 0.05)), rng.uniform(0.3, 0.42), rng.uniform(0, 40),
                    sy=0.95, amp=0.32, seg=(8 if lod == 0 else 5), rings=(5 if lod == 0 else 3))
    if lod == 0:
        for i in range(5):
            x = rng.uniform(-L / 2 + 0.3, L / 2 - 0.3)
            k.sphere_like("Grape", (x, rng.uniform(0.55, 0.85), rng.choice([-0.12, 0.12])), 0.05, 0.1, 0.05, 6, 4)
    if lod == 2:
        k.aabb("VineLeaf", -L / 2, L / 2, 0.45, 1.65, -0.28, 0.28)
    return finish(k, f"FujiK2_VineBay_LOD{lod}", bevel={"Wood": 0.008} if lod == 0 else None)


# ============================================================================ walls

def terrace_wall(lod):
    rng = random.Random(21)
    k = kit()
    L, T, H0, H1 = 4.0, 0.62, -0.7, 1.35
    if lod == 0:
        # core + front/back/ends faced in irregular stones
        k.aabb("DryStone", -L / 2, L / 2, H0, H1 - 0.15, -T / 2 + 0.07, T / 2 - 0.07)
        k.stones_wall("Stone", -L / 2, L / 2, H0, H1 - 0.12, 0.0, T, rng, course_h=(0.2, 0.3),
                      len_rng=(0.4, 0.8), jitter=0.02, bulge=0.05)
        # back face (mirror)
        k.push(Matrix.Rotation(math.pi, 4, 'Y'))
        k.stones_wall("DryStone", -L / 2, L / 2, 0.0, H1 - 0.12, 0.0, T, rng, course_h=(0.2, 0.3),
                      len_rng=(0.35, 0.7), jitter=0.02, bulge=0.04)
        k.pop()
        # capstones: shaggy flat slabs
        x = -L / 2
        while x < L / 2 - 0.1:
            w = min(rng.uniform(0.45, 0.8), L / 2 - x)
            k.aabb("Stone", x + 0.01, x + w - 0.01, H1 - 0.13, H1 + rng.uniform(0.0, 0.05), -T / 2 - 0.03, T / 2 + 0.03)
            x += w
    elif lod == 1:
        k.aabb("DryStone", -L / 2, L / 2, H0, H1 - 0.12, -T / 2, T / 2)
        k.stones_wall("Stone", -L / 2, L / 2, 0.0, H1 - 0.12, 0.0, T, rng, course_h=(0.34, 0.45),
                      len_rng=(0.8, 1.3), jitter=0.0, bulge=0.03)
        k.aabb("Stone", -L / 2, L / 2, H1 - 0.13, H1, -T / 2 - 0.03, T / 2 + 0.03)
    else:
        k.aabb("Stone", -L / 2, L / 2, H0, H1, -T / 2, T / 2)
    return finish(k, f"FujiK2_TerraceWall_LOD{lod}", bevel={"Stone": 0.012} if lod == 0 else None)


# ============================================================================ shrine + hermitage

def arch_ring(k, role, cx, cy, r, z0, z1, ring=0.14, n=9):
    """Voussoir ring (half circle) facing +z; piers are the caller's job."""
    for i in range(n):
        a0 = math.pi * i / n
        a1 = math.pi * (i + 1) / n
        ri, ro = r, r + ring
        p = lambda rad, a: (cx + rad * math.cos(a), cy + rad * math.sin(a))
        (x0i, y0i), (x1i, y1i) = p(ri, a0), p(ri, a1)
        (x0o, y0o), (x1o, y1o) = p(ro, a0), p(ro, a1)
        k.hexa(role, [(x0i, y0i, z1), (x1i, y1i, z1), (x1o, y1o, z1), (x0o, y0o, z1)], (0, 0, z0 - z1))


def shrine(lod):
    rng = random.Random(33)
    k = kit()
    W, D = 1.25, 0.95
    # plinth + steps
    k.aabb("Stone", -W / 2 - 0.25, W / 2 + 0.25, -0.5, 0.22, -D / 2 - 0.25, D / 2 + 0.3)
    k.aabb("Stone", -W / 2 - 0.1, W / 2 + 0.1, 0.22, 0.36, -D / 2 - 0.1, D / 2 + 0.12)
    # pier body (brick), niche opening cut by building jambs + lintel + back
    yb, yt = 0.36, 2.35
    nw, ny0, ny1 = 0.62, 0.95, 1.85
    k.aabb("Brick", -W / 2, -nw / 2, yb, yt, -D / 2, D / 2)
    k.aabb("Brick", nw / 2, W / 2, yb, yt, -D / 2, D / 2)
    k.aabb("Brick", -nw / 2, nw / 2, yb, ny0, -D / 2, D / 2)
    k.aabb("Brick", -nw / 2, nw / 2, ny1, yt, -D / 2, D / 2)
    k.aabb("Interior", -nw / 2, nw / 2, ny0, ny1, -D / 2 + 0.05, -D / 2 + 0.2)   # niche back (dark)
    if lod < 2:
        k.aabb("Travertine", -nw / 2 - 0.08, nw / 2 + 0.08, ny0 - 0.06, ny0 + 0.02, 0.0, D / 2 + 0.08)  # sill
        if lod == 0:
            arch_ring(k, "Travertine", 0.0, ny1 - 0.3, nw / 2, D / 2 - 0.02, D / 2 + 0.08, ring=0.11)
            for q in (-1, 1):
                k.aabb("Travertine", q * (nw / 2 + 0.01) - 0.06, q * (nw / 2 + 0.01) + 0.06, ny0, ny1 - 0.3, D / 2 - 0.02, D / 2 + 0.08)
            # statue of the Virgin: robe cone + head + halo
            k.cyl("Travertine", (0, ny0 + 0.02, 0.05), 0.15, 0.07, 0.62, n=10)
            k.blob("Travertine", (0, ny0 + 0.74, 0.05), 0.07, 1.1, 8, 5)
            k.cyl("Copper", (0, ny0 + 0.74, 0.0), 0.12, 0.12, 0.012, n=14, axis=Rx(90))
            # candles + flowers
            for i in range(3):
                k.cyl("Travertine", (-0.35 + i * 0.12, 0.36, D / 2 + 0.2), 0.02, 0.02, 0.1, n=6)
            for q in (-1, 1):
                k.cyl("Terracotta", (q * 0.52, 0.36, D / 2 + 0.28), 0.1, 0.07, 0.17, n=8)
                k.leafy("Flower", (q * 0.52, 0.58, D / 2 + 0.28), 0.14, rng.uniform(0, 9), sy=0.8, seg=6, rings=3)
        else:
            k.aabb("Travertine", -nw / 2, nw / 2, ny1 - 0.1, ny1, D / 2 - 0.02, D / 2 + 0.06)
    # cornice + gable roof (coppi)
    k.aabb("Travertine", -W / 2 - 0.08, W / 2 + 0.08, yt, yt + 0.1, -D / 2 - 0.08, D / 2 + 0.1)
    ye = yt + 0.1
    ov = 0.2
    hh = 0.62
    for zs in (1, -1):
        za = zs * (D / 2 + ov)
        k.hexa("Coppi", [(-W / 2 - ov, ye, za), (W / 2 + ov, ye, za), (W / 2 + ov, ye + hh, 0), (-W / 2 - ov, ye + hh, 0)], (0, 0.05, 0))
    k.tri_prism("Brick", [(-W / 2, ye, D / 2 - 0.02), (W / 2, ye, D / 2 - 0.02), (0, ye + hh - 0.02, D / 2 - 0.02)], (0, 0, -0.05))
    if lod == 0:
        k.aabb("Coppi", -W / 2 - ov, W / 2 + ov, ye + hh - 0.02, ye + hh + 0.07, -0.07, 0.07)
        # iron cross finial
        k.aabb("Iron", -0.02, 0.02, ye + hh, ye + hh + 0.55, -0.02, 0.02)
        k.aabb("Iron", -0.17, 0.17, ye + hh + 0.3, ye + hh + 0.35, -0.02, 0.02)
        # flanking low wall
        for q in (-1, 1):
            k.aabb("DryStone", q * (W / 2 + 0.25) + (0.0 if q > 0 else -1.2), q * (W / 2 + 0.25) + (1.2 if q > 0 else 0.0),
                   -0.4, 0.55, -0.2, 0.2)
    return finish(k, f"FujiK2_Shrine_LOD{lod}", bevel={"Stone": 0.01, "Brick": 0.01, "Travertine": 0.01} if lod == 0 else None)


def hermitage(lod):
    rng = random.Random(44)
    k = kit()
    W, D, H = 5.6, 4.6, 2.8
    if lod == 0:
        k.aabb("DryStone", -W / 2 + 0.08, W / 2 - 0.08, -0.6, H, -D / 2 + 0.08, D / 2 - 0.08)
        k.stones_wall("Stone", -W / 2, W / 2, -0.4, H, D / 2 - 0.1, 0.25, rng, course_h=(0.3, 0.45), len_rng=(0.6, 1.1), jitter=0.015, bulge=0.04)
        k.push(Matrix.Rotation(math.pi, 4, 'Y'))
        k.stones_wall("Stone", -W / 2, W / 2, -0.4, H, D / 2 - 0.1, 0.25, rng, course_h=(0.3, 0.45), len_rng=(0.6, 1.1), jitter=0.015, bulge=0.04)
        k.pop()
        for s in (-1, 1):
            k.push(Matrix.Rotation(math.pi / 2 * s, 4, 'Y'))
            k.stones_wall("Stone", -D / 2, D / 2, -0.4, H, W / 2 - 0.1, 0.25, rng, course_h=(0.3, 0.45), len_rng=(0.6, 1.1), jitter=0.015, bulge=0.04)
            k.pop()
    else:
        k.aabb("Stone", -W / 2, W / 2, -0.6, H, -D / 2, D / 2)
    # slate roof (gable along x), eaves + barge boards
    pitch = 36
    t = math.tan(math.radians(pitch))
    ye = H
    ov = 0.45
    yr = ye + (D / 2 + ov) * t
    for zs in (1, -1):
        za = zs * (D / 2 + ov)
        k.hexa("Slate", [(-W / 2 - 0.3, ye, za), (W / 2 + 0.3, ye, za), (W / 2 + 0.3, yr, 0), (-W / 2 - 0.3, yr, 0)], (0, 0.14, 0))
    for sx in (-1, 1):
        k.tri_prism("Stone", [(sx * W / 2, H, -D / 2), (sx * W / 2, H, D / 2), (sx * W / 2, yr, 0)], (-sx * 0.28, 0, 0))
    if lod == 0:
        k.aabb("Slate", -W / 2 - 0.32, W / 2 + 0.32, yr, yr + 0.1, -0.1, 0.1)   # ridge
        # chimney
        k.aabb("Stone", W / 2 - 1.1, W / 2 - 0.5, H, yr + 0.9, -0.7, -0.1)
        k.aabb("Slate", W / 2 - 1.18, W / 2 - 0.42, yr + 0.9, yr + 0.98, -0.78, 0.0)
        # door + shutters + window
        k.aabb("Wood", -0.55, 0.55, -0.05, 2.0, D / 2 - 0.12, D / 2 + 0.03)
        k.aabb("Stone", -0.72, 0.72, 2.0, 2.2, D / 2 - 0.15, D / 2 + 0.1)
        for q in (-1, 1):
            k.aabb("Stone", q * 0.72 - 0.09 if q < 0 else 0.63, q * 0.72 + (-0.09 if q < 0 else 0.09), -0.05, 2.0, D / 2 - 0.15, D / 2 + 0.08)
        k.aabb("Iron", -0.5, 0.5, 0.5, 0.56, D / 2 + 0.03, D / 2 + 0.05)
        k.aabb("Iron", -0.5, 0.5, 1.4, 1.46, D / 2 + 0.03, D / 2 + 0.05)
        k.aabb("Interior", 1.4, 2.1, 1.2, 1.95, D / 2 - 0.1, D / 2 + 0.02)
        k.aabb("Wood", 1.28, 1.4, 1.2, 1.95, D / 2 - 0.1, D / 2 + 0.06)
        k.aabb("Wood", 2.1, 2.22, 1.2, 1.95, D / 2 - 0.1, D / 2 + 0.06)
        k.aabb("Stone", 1.3, 2.2, 1.12, 1.2, D / 2 - 0.12, D / 2 + 0.14)
        # steps
        for i in range(3):
            k.aabb("Stone", -0.9, 0.9, -0.5 + i * 0.16, -0.34 + i * 0.16 - 0.0, D / 2 + 0.05 + (2 - i) * 0.3, D / 2 + 0.35 + (2 - i) * 0.3)
        # bell cote on the gable
        gz = D / 2 - 0.02
        k.aabb("Stone", -0.35, 0.35, yr - 0.7, yr + 0.6, -0.13, 0.13)
        k.aabb("Interior", -0.17, 0.17, yr + 0.05, yr + 0.45, -0.14, 0.14)
        K_arch = None
        k.cyl("Copper", (0, yr + 0.08, 0), 0.13, 0.04, 0.3, n=10)
        k.tri_prism("Stone", [(-0.4, yr + 0.6, -0.15), (0.4, yr + 0.6, -0.15), (0, yr + 1.0, -0.15)], (0, 0, 0.3))
        k.aabb("Iron", -0.015, 0.015, yr + 1.0, yr + 1.4, -0.015, 0.015)
        k.aabb("Iron", -0.1, 0.1, yr + 1.2, yr + 1.25, -0.015, 0.015)
        # log pile + bench
        for i in range(5):
            for j in range(3 - (i % 2)):
                k.cyl("Wood", (-W / 2 + 0.4 + i * 0.2, 0.0 + j * 0.2, -D / 2 - 0.5), 0.09, 0.09, 0.8, n=6, axis=Rx(90))
        k.aabb("Wood", 1.5, 2.9, 0.42, 0.47, D / 2 + 0.2, D / 2 + 0.55)
        for xx in (1.6, 2.8):
            k.aabb("Stone", xx - 0.05, xx + 0.05, -0.2, 0.42, D / 2 + 0.22, D / 2 + 0.52)
    return finish(k, f"FujiK2_Hermitage_LOD{lod}", bevel={"Slate": 0.008} if lod == 0 else None)


# ============================================================================ campanile + fountain

def campanile(lod):
    rng = random.Random(55)
    k = kit()
    W = 5.0
    h = W / 2
    SH = 24.0   # top of shaft
    BY0, BY1 = 17.2, 22.4   # belfry
    # shaft (brick) with stone plinth, quoins, string courses
    k.aabb("Stone", -h - 0.35, h + 0.35, -1.0, 1.4, -h - 0.35, h + 0.35)
    k.aabb("Brick", -h, h, 1.4, BY0, -h, h)
    if lod < 2:
        for y in (1.4, 6.0, 11.0, 15.5):
            k.aabb("Travertine", -h - 0.12, h + 0.12, y, y + 0.28, -h - 0.12, h + 0.12)
        # quoins: alternating long/short stones on the four corners
        yq = 1.4
        i = 0
        while yq < BY0 - 0.2:
            qh = 0.55
            for sx in (-1, 1):
                for sz in (-1, 1):
                    ax, az = (0.55, 0.22) if i % 2 == 0 else (0.22, 0.55)
                    xa, xb = sorted((sx * (h + 0.03), sx * (h - ax)))
                    za, zb = sorted((sz * (h + 0.03), sz * (h - az)))
                    k.aabb("Stone", xa, xb, yq, yq + qh - 0.03, za, zb)
            yq += qh
            i += 1
            if lod == 1:
                break        # slit windows on every face at three levels
        for ang in (0, 90, 180, 270):
            k.push(Matrix.Rotation(math.radians(ang), 4, 'Y'))
            for y in (4.0, 8.4, 13.2):
                k.aabb("Interior", -0.2, 0.2, y, y + 1.3, h - 0.08, h + 0.01)
                k.aabb("Travertine", -0.3, 0.3, y + 1.3, y + 1.42, h - 0.1, h + 0.1)
                k.aabb("Travertine", -0.3, 0.3, y - 0.1, y, h - 0.1, h + 0.12)
            k.pop()
    # clock faces on the 4 sides (lower shaft)
    if lod < 2:
        for ang in (0, 90, 180, 270):
            k.push(Matrix.Rotation(math.radians(ang), 4, 'Y'))
            k.cyl("Travertine", (0, 15.0, h - 0.01), 0.95, 0.95, 0.1, n=(24 if lod == 0 else 12), axis=Rx(90))
            if lod == 0:
                k.cyl("Iron", (0, 15.0, h + 0.09), 1.03, 1.03, 0.04, n=24, axis=Rx(90), cap=True)
                k.cyl("Travertine", (0, 15.0, h + 0.09), 0.9, 0.9, 0.025, n=24, axis=Rx(90))
                k.aabb("Iron", -0.04, 0.04, 15.0, 15.7, h + 0.12, h + 0.14)
                k.aabb("Iron", 0.0, 0.55, 14.96, 15.04, h + 0.12, h + 0.14)
                for hr in range(12):
                    a = hr / 12 * math.tau
                    k.aabb("Iron", math.sin(a) * 0.75 - 0.025, math.sin(a) * 0.75 + 0.025,
                           15.0 + math.cos(a) * 0.75 - 0.05, 15.0 + math.cos(a) * 0.75 + 0.05, h + 0.12, h + 0.14)
            k.pop()
    # belfry: corner piers + 2 arched bays per face
    pier = 0.7
    if lod < 2:
        for sx in (-1, 1):
            for sz in (-1, 1):
                xa, xb = sorted((sx * h, sx * (h - pier)))
                za, zb = sorted((sz * h, sz * (h - pier)))
                k.aabb("Brick", xa, xb, BY0, BY1, za, zb)
    else:
        k.aabb("Brick", -h, h, BY0, BY1, -h, h)
    if lod < 2:
        # dark core with 4 bells
        k.aabb("Interior", -h + 0.2, h - 0.2, BY0, BY1, -h + 0.2, h - 0.2)
        for ang in (0, 90, 180, 270):
            k.push(Matrix.Rotation(math.radians(ang), 4, 'Y'))
            span = (W - 2 * pier - 0.5) / 2
            cx0 = -W / 2 + pier
            for b in range(2):
                bx = cx0 + 0.15 + b * (span + 0.2) + span / 2 - 0.0
                bx = -span / 2 - 0.1 + b * (span + 0.2)
                r = span / 2
                # piers between bays handled by the central mullion
                if lod == 0:
                    arch_ring(k, "Travertine", bx, BY1 - 0.9 - r + r, r, h - 0.1, h + 0.06, ring=0.18, n=8)
                    k.aabb("Travertine", bx - r - 0.18, bx - r, BY0 + 0.6, BY1 - 0.9, h - 0.1, h + 0.06)
                    k.aabb("Travertine", bx + r, bx + r + 0.18, BY0 + 0.6, BY1 - 0.9, h - 0.1, h + 0.06)
                    k.cyl("Copper", (bx, BY0 + 1.5, 0.0), 0.32, 0.5, 0.7, n=10)
                k.aabb("Brick", bx - r - 0.2, bx + r + 0.2, BY0, BY0 + 0.6, h - 0.15, h)
            # parapet / balustrade band + central mullion
            k.aabb("Travertine", -0.12, 0.12, BY0 + 0.6, BY1 - 0.5, h - 0.1, h + 0.08)
            k.pop()
    # cornice (stepped) + octagon-free pyramidal copper spire
    for i, (e, y) in enumerate(((0.55, BY1), (0.4, BY1 + 0.22), (0.25, BY1 + 0.44))):
        k.aabb("Travertine", -h - e, h + e, y, y + 0.22, -h - e, h + e)
    ys = BY1 + 0.66
    k.pyramid("Copper", (0, ys, 0), h + 0.1, h + 0.1, 6.2)
    k.blob("Copper", (0, ys + 6.2, 0), 0.22, 1.0, 8, 5)
    if lod < 2:
        k.aabb("Iron", -0.02, 0.02, ys + 6.35, ys + 7.4, -0.02, 0.02)
        k.aabb("Iron", -0.25, 0.25, ys + 7.0, ys + 7.06, -0.02, 0.02)
        # corner pinnacles
        for sx in (-1, 1):
            for sz in (-1, 1):
                k.pyramid("Copper", (sx * (h - 0.1), ys, sz * (h - 0.1)), 0.22, 0.22, 1.4)
    return finish(k, f"FujiK2_Campanile_LOD{lod}", bevel={"Brick": 0.015, "Travertine": 0.012, "Stone": 0.015} if lod == 0 else None)


def fountain(lod):
    n = (16, 10, 8)[lod]
    k = kit()
    # sunk basin (rim under the 7% piazza slope), octagon->16 gon
    k.prism_poly("Stone", (0, -0.45, 0), 2.85, 2.75, 1.4, n=n, rot_deg=11)
    k.prism_poly("Stone", (0, 0.9, 0), 2.9, 2.9, 0.12, n=n, rot_deg=11)      # coping
    k.prism_poly("Water", (0, 0.2, 0), 2.62, 2.62, 0.0 + 0.55, n=n, rot_deg=11)
    if lod < 2:
        k.cyl("Stone", (0, 0.2, 0), 0.5, 0.36, 1.2, n=n)
        k.prism_poly("Stone", (0, 1.35, 0), 0.4, 1.35, 0.3, n=n)              # lower bowl underside
        k.prism_poly("Water", (0, 1.62, 0), 1.2, 1.2, 0.08, n=n)
        k.prism_poly("Stone", (0, 1.62, 0), 1.45, 1.42, 0.12, n=n)
        k.cyl("Stone", (0, 1.62, 0), 0.22, 0.17, 1.0, n=n)
        k.prism_poly("Stone", (0, 2.5, 0), 0.2, 0.7, 0.25, n=n)
        k.prism_poly("Water", (0, 2.72, 0), 0.6, 0.6, 0.04, n=n)
        k.blob("Stone", (0, 3.0, 0), 0.2, 1.3, 8, 5)
        k.aabb("Copper", -0.015, 0.015, 3.15, 3.55, -0.015, 0.015)
    if lod == 0:
        for i in range(8):
            a = i / 8 * math.tau
            k.cyl("Copper", (math.cos(a) * 0.5, 0.95, math.sin(a) * 0.5), 0.04, 0.03, 0.22, n=6,
                  axis=Rz(-90) @ Ry(-math.degrees(a)) if False else Matrix.Identity(3))
        for i in range(4):
            a = i / 4 * math.tau + 0.4
            k.leafy("Stone", (math.cos(a) * 2.5, 1.0, math.sin(a) * 2.5), 0.16, i, sy=1.0, amp=0.1, seg=6, rings=3)
    return finish(k, f"FujiK2_Fountain_LOD{lod}", bevel={"Stone": 0.012} if lod == 0 else None)


# ============================================================================ volcano

def scoria(variant, lod):
    rng = random.Random(70 + variant)
    k = kit()
    seg, rings = ((14, 9), (8, 5), (6, 3))[lod]
    if variant == 0:
        k.rock("Scoria", (0, 0.2, 0), 1.4, 3.1, flat=0.8, amp=0.5, seg=seg, rings=rings, ridged=True)
    elif variant == 1:
        for (c, r, s) in (((0, 0.1, 0), 1.1, 1.2), ((1.2, 0.0, 0.5), 0.75, 4.4), ((-0.9, 0.0, 0.8), 0.55, 6.7)):
            k.rock("Scoria", c, r, s, flat=0.8, amp=0.5, seg=seg, rings=rings, ridged=True)
    else:
        k.rock("Scoria", (0, 0.05, 0), 2.1, 9.0, flat=0.38, amp=0.45, seg=seg, rings=rings, ridged=True, sink=0.1)
        if lod < 2:
            k.rock("Scoria", (1.6, 0.0, -0.9), 0.7, 2.2, flat=0.7, amp=0.4, seg=max(6, seg - 3), rings=max(3, rings - 2), ridged=True)
    return finish(k, f"FujiK2_Scoria{'ABC'[variant]}_LOD{lod}")


def fumarole(lod):
    rng = random.Random(81)
    k = kit()
    seg, rings = ((12, 7), (8, 4), (6, 3))[lod]
    nrocks = (7, 5, 3)[lod]
    for i in range(nrocks):
        a = i / nrocks * math.tau + rng.uniform(-0.2, 0.2)
        d = rng.uniform(1.0, 1.35)
        k.rock("Scoria", (math.cos(a) * d, 0.0, math.sin(a) * d), rng.uniform(0.55, 0.85), rng.uniform(0, 30),
               flat=0.8, amp=0.45, seg=seg, rings=rings, ridged=True)
    k.cyl("Sulphur", (0, -0.05, 0), 1.15, 0.95, 0.22, n=(18 if lod == 0 else 10))
    k.cyl("Interior", (0, 0.1, 0), 0.5, 0.45, 0.06, n=(14 if lod == 0 else 8))
    if lod == 0:
        for i in range(8):
            a = rng.uniform(0, math.tau)
            d = rng.uniform(0.5, 1.0)
            k.rock("Sulphur", (math.cos(a) * d, 0.12, math.sin(a) * d), rng.uniform(0.1, 0.18), rng.uniform(0, 5), flat=0.6, amp=0.3, seg=6, rings=4)
    return finish(k, f"FujiK2_Fumarole_LOD{lod}")


# ============================================================================ textures

def build_textures():
    res = 512
    x, y = K.grid(res)
    # Bark: vertical furrows, tileable
    f = S.fbm_2d(x, y, 6, octaves=4, seed=11, tile=1)
    fur = S.fbm_2d(x * 1.0, y * 0.12 + 0.0, 14, octaves=3, seed=3, tile=1)
    ridge = 1.0 - np.abs(np.sin((x * 22 + fur * 3.0) * math.pi))
    h = ridge * 0.6 + f * 0.4
    base = np.dstack([0.33 + h * 0.22, 0.26 + h * 0.18, 0.20 + h * 0.14])
    K.save_png(os.path.join(TEXTURES, "Fuji_K2_Bark_Albedo.png"), base)
    K.save_png(os.path.join(TEXTURES, "Fuji_K2_Bark_Normal.png"), K.normal_from_height(h, 5.0))
    # Scoria: dark vesicular rock with pits
    f = S.fbm_2d(x, y, 5, octaves=5, seed=22, tile=1)
    pits = S.fbm_2d(x, y, 22, octaves=2, seed=5, tile=1)
    h = f * 0.7 - K.smoothstep(pits, 0.62, 0.74) * 0.6
    rust = K.smoothstep(S.fbm_2d(x, y, 4, octaves=3, seed=9, tile=1), 0.55, 0.8)
    base = np.dstack([0.14 + h * 0.12 + rust * 0.10, 0.11 + h * 0.10 + rust * 0.03, 0.10 + h * 0.09])
    K.save_png(os.path.join(TEXTURES, "Fuji_K2_Scoria_Albedo.png"), base)
    K.save_png(os.path.join(TEXTURES, "Fuji_K2_Scoria_Normal.png"), K.normal_from_height(h, 6.0))


def main():
    S.reset_scene()
    if not NO_TEX:
        build_textures()
    for lod in (0, 1, 2):
        cypress("A", 13.0, lod, 101)
        cypress("B", 9.5, lod, 202)
        olive(lod)
        vinebay(lod)
        terrace_wall(lod)
        shrine(lod)
        hermitage(lod)
        campanile(lod)
        fountain(lod)
        for v in range(3):
            scoria(v, lod)
        fumarole(lod)
    print("[fuji-k2] done")


main()

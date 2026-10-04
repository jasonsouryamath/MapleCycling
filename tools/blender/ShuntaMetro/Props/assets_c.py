"""Shrine and nature props: torii, stone lantern, tamagaki fence, three sakura variants."""
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


# ------------------------------------------------------------------ torii
@asset("torii_gate", ratio=0.35)
def torii(b):
    for sx in (-1, 1):
        x0 = sx * 1.75; x1 = sx * 1.68
        b.box((x0, 0, 0.1), (0.85, 0.85, 0.2), "SM_Stone", 0.02)
        b.cyl((x0, 0, 0.2), (x1, 0, 3.95), 0.25, "SM_ToriiRed", 20, r2=0.2)
        lathe_shift(b, x0, 0, [(0.27, 0.2), (0.27, 0.62), (0.25, 0.62)], "SM_PaintBlack", 20)
        lathe_shift(b, x0, 0, [(0.285, 0.2), (0.285, 0.26)], "SM_PaintBlack", 20)
        b.box((x1 * 1.0, 0, 3.0), (0.2, 0.3, 0.12), "SM_PaintBlack", 0.01, rot=(0, 0, 0)) if False else None
        # kusabi wedges either side of the nuki
        for dx in (-0.31, 0.31):
            b.box((x1 + dx * 0.0 + sx * 0.0 + (sx * 0.0) + dx, 0, 3.0), (0.07, 0.34, 0.3), "SM_PaintBlack", 0.008)
    # nuki tie beam through the pillars
    b.box((0, 0, 3.0), (4.5, 0.24, 0.24), "SM_ToriiRed", 0.012)
    # shimagi + kasagi (gently up-curved)
    segs = 11; half = 2.7
    for i in range(segs):
        xa = -half + i * (2 * half / segs); xb = xa + 2 * half / segs; xc = (xa + xb) / 2
        za = 0.38 * (abs(xa) / half) ** 2.2; zb = 0.38 * (abs(xb) / half) ** 2.2
        ang = math.degrees(math.atan2(zb - za, xb - xa))
        L = math.hypot(xb - xa, zb - za) + 0.03
        zc = 4.02 + (za + zb) / 2
        b.box((xc, 0, zc), (L, 0.42, 0.2), "SM_ToriiRed", 0.01, rot=(0, -ang, 0))
        b.box((xc, 0, zc + 0.13), (L, 0.5, 0.07), "SM_PaintBlack", 0.01, rot=(0, -ang, 0))
        b.box((xc, 0, zc - 0.115), (L, 0.52, 0.03), "SM_PaintBlack", 0.006, rot=(0, -ang, 0), seg=1)
    b.box((0, 0, 3.78), (4.7, 0.3, 0.18), "SM_ToriiRed", 0.012)
    # centre strut + name plaque
    b.box((0, 0, 3.4), (0.16, 0.2, 0.7), "SM_ToriiRed", 0.01)
    b.box((0, -0.12, 3.4), (0.6, 0.05, 0.75), "SM_PaintBlack", 0.01)
    b.box((0, -0.15, 3.4), (0.52, 0.015, 0.67), "SM_PaintYellow", 0.004)
    b.text("神明", (0, -0.165, 3.4), 0.3, "SM_PaintBlack", 0.008)
    # shimenawa-like rope loop hanging off the kasagi
    b.tube(catenary((-0.9, -0.3, 3.7), (0.9, -0.3, 3.7), 0.18, 10), 0.03, "SM_TextWhite", 6, cap=False)


# ------------------------------------------------------------------ stone lantern
@asset("stone_lantern")
def stone_lantern(b):
    b.lathe([(0.0, 0.0), (0.42, 0.0), (0.42, 0.1), (0.36, 0.14), (0.0, 0.14)], "SM_Stone", 6, vtile=1.0)
    b.lathe([(0.2, 0.14), (0.2, 0.2), (0.15, 0.2), (0.14, 0.7), (0.19, 0.72), (0.19, 0.78), (0.0, 0.78)], "SM_Stone", 8)
    b.lathe([(0.0, 0.78), (0.4, 0.78), (0.42, 0.84), (0.34, 0.9), (0.0, 0.9)], "SM_Stone", 6)
    # firebox: slabs + 4 posts, glowing core
    b.lathe([(0.0, 0.9), (0.26, 0.9), (0.26, 0.96), (0.0, 0.96)], "SM_Stone", 4, scale=(1.4, 1.4))
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 0.16, sy * 0.16, 1.12), (0.07, 0.07, 0.32), "SM_Stone", 0.008)
    b.box((0, 0, 1.12), (0.18, 0.18, 0.2), "SM_LampWarm", 0.003)
    b.lathe([(0.0, 1.28), (0.26, 1.28), (0.26, 1.34), (0.0, 1.34)], "SM_Stone", 4, scale=(1.4, 1.4))
    b.lathe([(0.0, 1.34), (0.58, 1.34), (0.62, 1.38), (0.52, 1.46), (0.3, 1.56), (0.12, 1.66), (0.0, 1.68)], "SM_Stone", 6)
    b.lathe([(0.0, 1.68), (0.08, 1.68), (0.075, 1.72), (0.0, 1.72)], "SM_Stone", 8)
    b.sphere((0, 0, 1.78), 0.075, "SM_Stone", 10, scale=(1, 1, 1.15))
    b.cyl((0, 0, 1.84), (0, 0, 1.95), 0.04, "SM_Stone", 8, r2=0.0)


# ------------------------------------------------------------------ shrine fence
@asset("shrine_fence", lod=False)
def shrine_fence(b):
    for x in (-2.0, 0.0, 2.0):
        b.box((x, 0, 0.62), (0.14, 0.14, 1.24), "SM_Wood", 0.01)
        b.box((x, 0, 1.27), (0.2, 0.2, 0.05), "SM_PaintRed", 0.008)
        b.cyl((x, 0, 1.29), (x, 0, 1.4), 0.05, "SM_PaintRed", 8, r2=0.0)
        b.box((x, 0, 0.04), (0.22, 0.22, 0.08), "SM_Stone", 0.008)
    for z in (1.12, 0.62, 0.2):
        b.box((0, 0, z), (4.0, 0.08, 0.1), "SM_PaintRed" if z > 1 else "SM_Wood", 0.008)
    n = 21
    for i in range(n):
        x = -1.9 + i * (3.8 / (n - 1))
        if abs(abs(x) - 2.0) < 0.1 or abs(x) < 0.1:
            continue
        b.box((x, 0.0, 0.62), (0.045, 0.045, 1.0), "SM_Wood", 0.004, seg=1)
        b.cyl((x, 0, 1.12), (x, 0, 1.17), 0.032, "SM_Wood", 4, r2=0.0)


# ------------------------------------------------------------------ sakura
def blob(b, c, r, mat, rng, jitter=0.25, subdiv=1):
    before = set(b.bm.faces)
    res = bmesh.ops.create_icosphere(b.bm, subdivisions=subdiv, radius=r)
    for v in res["verts"]:
        k = 1.0 + (rng.random() - 0.5) * 2 * jitter
        v.co *= k
        v.co.z *= 0.8
    bmesh.ops.translate(b.bm, vec=Vector(c), verts=res["verts"])
    b._fin(before, mat)


def card(b, c, n, size, rng, mat="SM_BlossomCard"):
    n = Vector(n).normalized()
    a = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0))
    t = n.cross(a).normalized(); u = n.cross(t).normalized()
    ang = rng.random() * math.tau
    t2 = t * math.cos(ang) + u * math.sin(ang); u2 = n.cross(t2).normalized()
    c = Vector(c)
    p = [c + (-t2 - u2) * size, c + (t2 - u2) * size, c + (t2 + u2) * size, c + (-t2 + u2) * size]
    b.quad(*p, mat)


def cluster(b, c, r, rng, ncards, droop=0.0):
    blob(b, c, r * 0.72, "SM_BlossomDeep" if rng.random() < 0.35 else "SM_Blossom", rng, 0.28)
    for _ in range(ncards):
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1) - droop)).normalized()
        card(b, Vector(c) + d * r * rng.uniform(0.55, 1.0), d, r * rng.uniform(0.45, 0.7), rng)


def branch(b, p0, p1, r0, r1, rng, wob=0.12):
    p0 = Vector(p0); p1 = Vector(p1); pts = []
    for i in range(6):
        t = i / 5
        q = p0.lerp(p1, t) + Vector((rng.uniform(-wob, wob), rng.uniform(-wob, wob), 0)) * math.sin(t * math.pi)
        pts.append(q)
    b.tube(pts, r0, "SM_Bark", 6, taper=r1, cap=False)


def trunk(b, rng, h, r, lean=0.25):
    pts = [(0.0, 0.0, 0.0)]
    for i in range(1, 8):
        t = i / 7
        pts.append((lean * math.sin(t * 2.0) * 0.5, lean * 0.4 * math.sin(t * 3), h * t))
    flare = [(r * 1.5, 0), (r * 1.15, 0.25), (r, 0.7)]
    b.tube(pts, r, "SM_Bark", 8, taper=r * 0.55, cap=False)
    # root flare
    b.lathe([(r * 1.7, 0.0), (r * 1.25, 0.18), (r * 1.0, 0.45)], "SM_Bark", 8)
    return Vector(pts[-1])


def petals(b, rng, rad, n):
    for _ in range(n):
        a = rng.random() * math.tau; d = rng.uniform(0.3, rad)
        c = Vector((math.cos(a) * d, math.sin(a) * d, 0.01)); s = rng.uniform(0.05, 0.13)
        ang = rng.random() * math.tau
        t = Vector((math.cos(ang), math.sin(ang), 0)) * s; u = Vector((-math.sin(ang), math.cos(ang), 0)) * s
        b.quad(c - t - u, c + t - u, c + t + u, c - t + u, "SM_BlossomCard", uvs=((0.2, 0.2), (0.8, 0.2), (0.8, 0.8), (0.2, 0.8)))


@asset("sakura_tree_full", ratio=0.3)
def sakura_a(b):
    rng = random.Random(11)
    top = trunk(b, rng, 3.2, 0.26)
    tips = []
    for i in range(7):
        a = i / 7 * math.tau + rng.uniform(-0.3, 0.3)
        L = rng.uniform(1.8, 2.8)
        s = Vector((0.05 * math.cos(a), 0.05 * math.sin(a), rng.uniform(2.4, 3.3)))
        e = Vector((L * math.cos(a), L * math.sin(a), rng.uniform(3.9, 5.3)))
        branch(b, s, e, 0.11, 0.03, rng)
        tips.append(e)
        if i % 2 == 0:
            m = s.lerp(e, 0.6)
            e2 = m + Vector((L * 0.4 * math.cos(a + 0.7), L * 0.4 * math.sin(a + 0.7), 0.7))
            branch(b, m, e2, 0.05, 0.02, rng)
            tips.append(e2)
    tips.append(Vector((0.1, 0.0, 5.5)))
    for t in tips:
        cluster(b, t, rng.uniform(0.95, 1.35), rng, 22)
    cluster(b, (0, 0, 4.4), 1.3, rng, 20)
    petals(b, rng, 3.2, 30)


@asset("sakura_tree_weeping", ratio=0.3)
def sakura_b(b):
    rng = random.Random(23)
    trunk(b, rng, 2.4, 0.2, lean=0.4)
    tips = []
    for i in range(6):
        a = i / 6 * math.tau + rng.uniform(-0.3, 0.3)
        s = Vector((0.04 * math.cos(a), 0.04 * math.sin(a), rng.uniform(1.9, 2.5)))
        mid = Vector((1.3 * math.cos(a), 1.3 * math.sin(a), 3.6))
        e = Vector((2.4 * math.cos(a), 2.4 * math.sin(a), rng.uniform(2.6, 3.2)))
        branch(b, s, mid, 0.09, 0.05, rng); branch(b, mid, e, 0.05, 0.02, rng)
        tips += [mid, e]
        # hanging strands
        for k in range(3):
            q = mid.lerp(e, rng.uniform(0.3, 1.0)) + Vector((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), 0))
            end = q + Vector((0, 0, -rng.uniform(1.0, 1.7)))
            b.tube([q, q.lerp(end, 0.5), end], 0.012, "SM_Bark", 4, cap=False)
            for j in range(6):
                p = q.lerp(end, j / 5)
                card(b, p + Vector((rng.uniform(-.1, .1), rng.uniform(-.1, .1), 0)), (rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1)), 0.4, rng)
    for t in tips:
        cluster(b, t, rng.uniform(0.7, 1.0), rng, 16, droop=0.6)
    petals(b, rng, 3.0, 36)


@asset("sakura_tree_young", ratio=0.35)
def sakura_c(b):
    rng = random.Random(37)
    trunk(b, rng, 2.2, 0.12, lean=0.2)
    tips = []
    for i in range(5):
        a = i / 5 * math.tau + rng.uniform(-0.4, 0.4)
        s = Vector((0.03 * math.cos(a), 0.03 * math.sin(a), rng.uniform(1.4, 2.2)))
        e = Vector((1.2 * math.cos(a), 1.2 * math.sin(a), rng.uniform(2.8, 3.6)))
        branch(b, s, e, 0.05, 0.015, rng)
        tips.append(e)
        # bare twig fork
        e2 = e.lerp(s, 0.3) + Vector((0.5 * math.cos(a + 1), 0.5 * math.sin(a + 1), 0.5))
        branch(b, e.lerp(s, 0.3), e2, 0.02, 0.008, rng, 0.05)
    for t in tips[:4]:
        cluster(b, t, rng.uniform(0.55, 0.8), rng, 12)
    cluster(b, (0, 0, 3.4), 0.7, rng, 10)
    petals(b, rng, 1.8, 18)

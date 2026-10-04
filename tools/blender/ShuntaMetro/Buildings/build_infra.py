"""Shunta Metro infrastructure + buildings job runner: expressway pillar/deck/barrier, railway viaduct,
tunnel portal, pedestrian overbridge, commuter train cars, plus every building variant export.
blender.exe -b --python build_infra.py [-- JobName]"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sm_lib import *  # noqa: E402,F401,F403
import sm_lib as S  # noqa: E402
from build_buildings import *  # noqa: E402,F401,F403
import build_buildings as B  # noqa: E402


def extrude_profile(g, prof, z0, z1, mat, caps=True):
    n = len(prof)
    cen = (sum(p[0] for p in prof) / n, sum(p[1] for p in prof) / n, (z0 + z1) / 2)
    for i in range(n):
        j = (i + 1) % n
        a, b = prof[i], prof[j]
        oquad(g, [(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)], mat, cen)
    if caps:
        oquad(g, [(p[0], p[1], z0) for p in prof], mat, cen)
        oquad(g, [(p[0], p[1], z1) for p in prof], mat, cen)


# ============================================================ expressway
def exp_pillar(d):
    g = Geo()
    H = 9.0
    # column: slightly tapered, rounded via chamfered octagon
    r = 1.0
    oct_ = [(r * math.cos(math.pi / 8 + i * math.pi / 4), r * math.sin(math.pi / 8 + i * math.pi / 4) * 0.8) for i in range(8)]
    for i in range(8):
        a, b = oct_[i], oct_[(i + 1) % 8]
        oquad(g, [(a[0], 0, a[1]), (b[0], 0, b[1]), (b[0] * 0.92, H, b[1] * 0.92), (a[0] * 0.92, H, a[1] * 0.92)], "SM_ConcreteInfra", (0, H / 2, 0))
    g.box((0, -3, 0), (2.6, 6, 2.2), "SM_ConcreteDark")
    # crossbeam (hammerhead) 14 m
    prof = [(-7.2, H + 0.3), (-6.2, H - 0.9), (-1.2, H - 1.4), (1.2, H - 1.4), (6.2, H - 0.9), (7.2, H + 0.3), (7.2, H + 1.4), (-7.2, H + 1.4)]
    extrude_profile(g, prof, -1.4, 1.4, "SM_ConcreteInfra")
    if d == 0:
        for sx in (-5.0, -1.8, 1.8, 5.0):
            g.box((sx, H + 1.5, 0), (1.0, 0.16, 1.0), "SM_MetalDark")     # bearings
        g.box((0, H - 1.0, 1.42), (1.6, 0.5, 0.04), "SM_Reflector")
        for k in range(5):
            g.box((0, 1.2 + k * 1.5, -1.12), (0.5, 0.04, 0.04), "SM_MetalDark")
        g.box((0, 0.15, 0), (2.4, 0.3, 2.0), "SM_Reflector") if False else None
        for sx in (-1, 1):   # hazard chevrons at the base
            g.box((sx * 0.62, 1.2, 0.95), (0.3, 1.2, 0.02), "SM_Warning")
    return [g]


def exp_deck(d):
    """10 m deck segment along z, 12 m wide, y=0 at deck top. Underside girders, barriers, reflectors."""
    g = Geo()
    L = 10.0
    prof = [(-6.2, 0.0), (6.2, 0.0), (6.0, -0.5), (3.0, -1.4), (-3.0, -1.4), (-6.0, -0.5)]
    extrude_profile(g, prof, -L / 2, L / 2, "SM_ConcreteInfra")
    g.box((0, -0.01, 0), (11.6, 0.03, L), "SM_Asphalt")
    for sx in (-1, 1):
        # solid barrier wall with jersey profile + top rail
        bp = [(-0.3, 0), (0.3, 0), (0.24, 0.35), (0.14, 1.0), (-0.14, 1.0), (-0.24, 0.35)]
        extrude_profile(g, [(sx * 5.9 + p[0], p[1]) for p in bp], -L / 2, L / 2, "SM_ConcreteB")
        if d == 0:
            for k in range(5):
                g.box((sx * 5.9, 0.78, -L / 2 + 1 + k * 2.0), (0.32, 0.12, 0.3), "SM_Reflector")
            g.box((sx * 5.9, 1.12, 0), (0.12, 0.1, L), "SM_Guardrail")
            for k in range(6):
                g.box((sx * 5.9, 1.05, -L / 2 + k * 1.9), (0.1, 0.25, 0.1), "SM_Metal")
            for k in range(0, 5, 2):
                g.box((sx * 3.0, -1.0, -L / 2 + 1 + k * 2), (0.4, 0.8, 0.3), "SM_ConcreteB")
    if d == 0:
        for k in range(4):
            g.box((0, 0.02, -L / 2 + 1.2 + k * 2.4), (0.15, 0.01, 1.2), "SM_RoadMark")   # centre dashes
        for sx in (-4.0, -1.5, 1.5, 4.0):    # girders (underside ribs)
            g.box((sx, -1.0, 0), (0.5, 0.7, L), "SM_ConcreteDark")
    return [g]


def exp_barrier(d):
    g = Geo()
    L = 4.0
    bp = [(-0.3, 0), (0.3, 0), (0.24, 0.35), (0.14, 0.9), (-0.14, 0.9), (-0.24, 0.35)]
    extrude_profile(g, bp, -L / 2, L / 2, "SM_ConcreteB")
    # smooth end bevel via extra rim + reflectors
    for z in (-L / 2 + 0.6, L / 2 - 0.6):
        g.box((0, 0.7, z), (0.34, 0.14, 0.18), "SM_Warning" if z > 0 else "SM_Reflector")
    if d == 0:
        g.box((0, 0.0, 0), (0.8, 0.04, L + 0.1), "SM_ConcreteDark")
        g.box((0, 0.93, 0), (0.1, 0.04, L), "SM_Chrome")
        for z in (-1.0, 0.0, 1.0):
            g.box((0, 0.5, z), (0.62, 0.05, 0.02), "SM_NeonYellow") if False else None
        g.box((0.0, 0.85, 0), (0.29, 0.02, L), "SM_Trim")
    return [g]


# ============================================================ viaduct
def viaduct(d):
    g = Geo()
    L, T, DH = 10.0, 9.0, 9.0
    r, cy = 3.5, 3.0
    # piers
    for sz in (-1, 1):
        g.box((0, DH / 2, sz * (L / 2 - 0.75)), (T, DH, 1.5), "SM_Brick")
    nseg = 14 if d == 0 else 6
    pts = [(r * math.cos(math.pi * i / nseg), cy + r * math.sin(math.pi * i / nseg)) for i in range(nseg + 1)]   # (z, y) from +z to -z
    for i in range(nseg):
        a, b = pts[i], pts[i + 1]
        cen = (0, 6.0, 0)
        # spandrel faces front/back
        for sx in (-1, 1):
            oquad(g, [(sx * T / 2, a[1], a[0]), (sx * T / 2, b[1], b[0]), (sx * T / 2, DH, b[0]), (sx * T / 2, DH, a[0])], "SM_Brick", (0, 5.0, 0))
        # intrados (inside arch surface)
        oquad(g, [(-T / 2, a[1], a[0]), (T / 2, a[1], a[0]), (T / 2, b[1], b[0]), (-T / 2, b[1], b[0])], "SM_ConcreteDark", (0, cy - 1.0, 0)) if False else None
        g.face([(-T / 2, a[1], a[0]), (T / 2, a[1], a[0]), (T / 2, b[1], b[0]), (-T / 2, b[1], b[0])], "SM_ConcreteDark")
        g.face([(-T / 2, b[1], b[0]), (T / 2, b[1], b[0]), (T / 2, a[1], a[0]), (-T / 2, a[1], a[0])], "SM_ConcreteDark")
    # deck: ballast bed, rails, sleepers, parapets, catenary posts
    g.box((0, DH + 0.3, 0), (T, 0.6, L), "SM_ConcreteInfra")
    g.box((0, DH + 0.7, 0), (T - 1.6, 0.25, L), "SM_ConcreteDark")
    for sx in (-1.2, 1.2, -3.0, 3.0):
        g.box((sx, DH + 0.95, 0), (0.12, 0.18, L), "SM_RailSteel")
    if d == 0:
        for sx in (-2.1, 2.1):
            for k in range(14):
                g.box((sx, DH + 0.83, -L / 2 + 0.4 + k * 0.7), (2.9 if False else 2.2, 0.08, 0.25), "SM_ConcreteB") if False else None
        for k in range(14):
            g.box((-2.1, DH + 0.85, -L / 2 + 0.35 + k * 0.7), (2.0, 0.07, 0.24), "SM_ConcreteB")
            g.box((2.1, DH + 0.85, -L / 2 + 0.35 + k * 0.7), (2.0, 0.07, 0.24), "SM_ConcreteB")
        for sx in (-1, 1):
            g.box((sx * (T / 2 - 0.1), DH + 1.4, 0), (0.2, 1.2, L), "SM_ConcreteB")
            beam(g, (sx * 3.6, DH + 0.9, 3.0), (sx * 3.6, DH + 6.2, 3.0), 0.22, "SM_MetalDark")
            beam(g, (sx * 3.6, DH + 6.1, 3.0), (-sx * 0.6, DH + 6.1, 3.0), 0.12, "SM_MetalDark")
        for sx in (-1, 1):
            for k in range(3):
                g.box((sx * T / 2, 6.8 + k * 0.8, -3.0 + k * 3.0), (0.03, 0.3, 0.3), "SM_NeonYellow") if False else None
        # arch lamps (lit): visible on the passing road
        for sz in (-0.5, 0.5):
            g.box((0, cy + r - 0.15, sz * 4.0), (T - 1.0, 0.1, 0.3), "SM_TunnelLamp")
        for sx in (-1, 1):
            g.box((sx * (T / 2 + 0.03), DH - 0.3, 0), (0.06, 0.3, L - 1), "SM_NeonYellow") if False else None
            g.box((sx * (T / 2 + 0.03), cy + r + 1.0, 0), (0.06, 0.1, L - 0.2), "SM_ConcreteB")
    return [g]


# ============================================================ tunnel portal (faces +z)
def tunnel_portal(d):
    g = Geo()
    ow, oh = 12.0, 6.6
    W, Hh, T = 30.0, 13.0, 3.0
    # wall pieces around the opening, front at z=0, depth into -z
    g.box((-(ow / 2 + (W - ow) / 4), Hh / 2, -T / 2), ((W - ow) / 2, Hh, T), "SM_ConcreteInfra")
    g.box(((ow / 2 + (W - ow) / 4), Hh / 2, -T / 2), ((W - ow) / 2, Hh, T), "SM_ConcreteInfra")
    g.box((0, oh + (Hh - oh) / 2, -T / 2), (ow, Hh - oh, T), "SM_ConcreteInfra")
    g.box((0, -3, -T / 2), (W, 6, T), "SM_ConcreteDark", top=False)
    # raised frame + hood slab
    fr = B.Fr((-W / 2, 0, 0), (0, 0, 1))
    fr.box(g, W / 2 - ow / 2 - 0.8, 0, W / 2 - ow / 2, oh + 0.8, 0.0, 0.5, "SM_ConcreteB")
    fr.box(g, W / 2 + ow / 2, 0, W / 2 + ow / 2 + 0.8, oh + 0.8, 0.0, 0.5, "SM_ConcreteB")
    fr.box(g, W / 2 - ow / 2 - 0.8, oh, W / 2 + ow / 2 + 0.8, oh + 0.8, 0.0, 0.5, "SM_ConcreteB")
    fr.box(g, W / 2 - ow / 2 - 1.2, oh + 0.8, W / 2 + ow / 2 + 1.2, oh + 1.4, 0.0, 2.2, "SM_ConcreteDark")
    # height-limit + name boards (lit) + warning lamps
    fr.box(g, W / 2 - 3.0, oh + 1.6, W / 2 + 3.0, oh + 3.4, 0.0, 0.3, "SM_MetalDark")
    fr.quad(g, W / 2 - 2.8, oh + 1.75, W / 2 + 2.8, oh + 3.25, "SM_KonbiniGreen", 0.31)
    for sx in (-1, 1):
        fr.box(g, W / 2 + sx * (ow / 2 + 1.5) - 0.3, oh - 1.2, W / 2 + sx * (ow / 2 + 1.5) + 0.3, oh - 0.6, 0.0, 0.6, "SM_Warning")
    for k in range(9 if d == 0 else 4):
        fr.box(g, W / 2 - ow / 2 + 0.5 + k * (ow - 1.0) / 8.5, oh - 0.5, W / 2 - ow / 2 + 0.9 + k * (ow - 1.0) / 8.5, oh - 0.2, 0.0, 0.3, "SM_TunnelLamp")
    # interior: lined walls, ceiling, lamp rows (14 m deep)
    D = 14.0
    g.box((-ow / 2 - 0.1, oh / 2, -T - D / 2 + 0.01), (0.2, oh, D), "SM_TileWhite")
    g.box((ow / 2 + 0.1, oh / 2, -T - D / 2 + 0.01), (0.2, oh, D), "SM_TileWhite")
    g.box((0, oh + 0.1, -T - D / 2), (ow + 0.4, 0.2, D), "SM_ConcreteDark")
    # interior wall faces: inward tiles
    for sx in (-1, 1):
        g.quad(
            (sx * ow / 2, 0.0, 0), (sx * ow / 2, 0.0, 0), (sx * ow / 2, 0.0, 0), (sx * ow / 2, 0.0, 0), "SM_TileWhite") if False else None
    nl = 7 if d == 0 else 3
    for k in range(nl):
        z = -T - 1 - k * (D / nl)
        g.box((0, oh - 0.12, z), (ow * 0.5, 0.08, 0.35), "SM_TunnelLamp")
        for sx in (-1, 1):
            g.box((sx * (ow / 2 - 0.08), 4.2, z), (0.06, 0.2, 1.2), "SM_NeonYellow")
    for sx in (-1, 1):
        g.box((sx * (ow / 2 - 0.08), 2.0, -T - D / 2), (0.08, 0.12, D), "SM_Reflector")
    if d == 0:
        # side walkways + reflective posts + emergency doors
        for sx in (-1, 1):
            g.box((sx * (ow / 2 - 0.6), 0.25, -T - D / 2), (1.2, 0.5, D), "SM_ConcreteB")
            for k in range(4):
                g.box((sx * (ow / 2 - 0.1), 1.0, -T - 1.5 - k * 3.4), (0.06, 1.8, 0.9), "SM_Warning")
    return [g]


# ============================================================ pedestrian overbridge (spans along x)
def overbridge(d):
    g = Geo()
    span, deckw, dh = 28.0, 3.0, 5.8
    # deck + side panels
    g.box((0, dh, 0), (span + 4, 0.35, deckw), "SM_ConcreteInfra")
    for sz in (-1, 1):
        g.box((0, dh + 0.9, sz * (deckw / 2 - 0.06)), (span + 4, 1.2, 0.12), "SM_Metal")
        g.box((0, dh + 1.55, sz * (deckw / 2 - 0.06)), (span + 4, 0.1, 0.16), "SM_MetalDark")
    g.box((0, dh - 0.25, 0), (span + 3.6, 0.12, deckw * 0.5), "SM_TunnelLamp")
    # piers at road edges
    for sx in (-1, 1):
        g.box((sx * (span / 2 + 0.4), dh / 2, 0), (1.0, dh, 1.6), "SM_ConcreteInfra")
        g.box((sx * (span / 2 + 0.4), -3, 0), (1.6, 6, 2.0), "SM_ConcreteDark")
    # stair ramps (x outward), two flights each side (landing)
    run = 11.0
    steps = 28 if d == 0 else 10
    for sx in (-1, 1):
        x0 = sx * (span / 2 + 2.0)
        for k in range(steps):
            t = k / steps
            x = x0 + sx * (t * run)
            y = dh - t * dh
            g.box((x + sx * run / steps / 2, y / 2, 0), (run / steps, max(y, 0.05), deckw - 0.4), "SM_ConcreteB")
        for sz in (-1, 1):
            beam(g, (x0, dh + 1.0, sz * (deckw / 2 - 0.1)), (x0 + sx * run, 1.0, sz * (deckw / 2 - 0.1)), 0.1, "SM_Metal")
            beam(g, (x0, dh + 0.45, sz * (deckw / 2 - 0.1)), (x0 + sx * run, 0.45, sz * (deckw / 2 - 0.1)), 0.06, "SM_Metal") if d == 0 else None
            if d == 0:
                for k in range(0, steps, 3):
                    t = k / steps
                    beam(g, (x0 + sx * t * run, dh - t * dh, sz * (deckw / 2 - 0.1)), (x0 + sx * t * run, dh - t * dh + 1.0, sz * (deckw / 2 - 0.1)), 0.06, "SM_Metal")
    # lit sign + roof lamps
    g.box((0, dh + 2.2, 0), (6.0, 0.8, 0.2), "SM_MetalDark")
    g.box((0, dh + 2.2, 0.11), (5.8, 0.6, 0.03), "SM_KonbiniGreen")
    g.box((0, dh + 2.2, -0.11), (5.8, 0.6, 0.03), "SM_KonbiniGreen")
    if d == 0:
        for k in range(8):
            g.box((-12 + k * 3.4, dh + 1.62, 0), (0.2, 0.06, 0.3), "SM_NeonWhite")
        # canopy
        g.box((0, dh + 3.0, 0), (span * 0.55, 0.1, deckw + 0.6), "SM_MetalDark")
        for sx in (-1, 1):
            for sz in (-1, 1):
                beam(g, (sx * span * 0.27, dh, sz * (deckw / 2)), (sx * span * 0.27, dh + 3.0, sz * (deckw / 2)), 0.1, "SM_MetalDark")
    return [g]


# ============================================================ commuter train car (z forward, 20 m)
def train_car(d, cab=False, stripe="SM_TrainStripeGreen", stripe2="SM_TrainStripeOrange", pantograph=False):
    g, sg = Geo(), SGeo()
    L2 = 10.0
    nst = 18 if d == 0 else 8
    seg = 22 if d == 0 else 12
    keys = [(-10.0, .72, 3.52, 1.42), (-9.9, .65, 3.58, 1.46), (-8.0, .62, 3.62, 1.475), (8.0, .62, 3.62, 1.475)]
    if cab:
        keys += [(9.2, .66, 3.5, 1.46), (9.7, .74, 3.2, 1.38), (9.95, .9, 2.7, 1.2)]
    else:
        keys += [(9.9, .65, 3.58, 1.46), (10.0, .72, 3.52, 1.42)]
    st = []
    for i in range(nst):
        z = lerp(-L2, L2, i / (nst - 1))
        yb, yt, hw = keyinterp(keys, z)
        st.append((z, 0, (yb + yt) / 2, hw, (yt - yb) / 2, 10.0, 8.0))
    sg.loft(st, seg, "SM_TrainBody", capmat="SM_TrainBody")
    geos = [sg, g]
    xs = 1.482
    door_z = (-7.2, -2.4, 2.4, 7.2)
    for sx in (-1, 1):
        # stripes along both sides
        g.box((sx * xs, 1.05, 0), (0.012, 0.22, 19.6 if not cab else 18.0), stripe)
        g.box((sx * xs, 1.33, 0), (0.012, 0.1, 19.6 if not cab else 18.0), stripe2)
        for dz in door_z:
            if cab and dz > 6:
                continue
            g.box((sx * xs, 2.05, dz), (0.016, 2.25, 1.35), "SM_TrainDoor")
            g.box((sx * (xs + 0.008), 2.35, dz), (0.016, 1.3, 1.0), "SM_TrainWindow")
        # window pairs between doors
        zz = [-9.0, -4.8, 0.0, 4.8, 9.0]
        for k in range(len(zz) - 1):
            zc = (zz[k] + zz[k + 1]) / 2
            if cab and zc > 7:
                continue
            for off in ((-0.9, 0.9) if d == 0 else (0.0,)):
                wz = zc + off
                if abs(wz) > 9.3:
                    continue
                g.box((sx * xs, 2.35, wz), (0.016, 1.1, 1.5 if d == 0 else 3.2), "SM_TrainWindow")
    if d == 0:
        # roof: AC units, vents, pantograph
        for z in (-5.5, -1.5, 3.0, 7.0):
            if cab and z > 6:
                continue
            g.box((0, 3.78, z), (1.7, 0.2, 1.8), "SM_ACUnit")
            g.box((0, 3.86, z), (1.5, 0.04, 1.6), "SM_Metal")
        if pantograph:
            g.box((0, 3.68, 0), (1.2, 0.12, 2.0), "SM_MetalDark")
            beam(g, (-0.5, 3.7, -0.8), (0, 4.8, 0), 0.06, "SM_MetalDark")
            beam(g, (0.5, 3.7, 0.8), (0, 4.8, 0), 0.06, "SM_MetalDark")
            g.box((0, 4.85, 0), (1.6, 0.05, 0.2), "SM_MetalDark")
        # bogies + skirts
        for bz in (-6.8, 6.8):
            g.box((0, 0.5, bz), (2.0, 0.4, 2.6), "SM_MetalDark")
            for sx in (-1, 1):
                for wz in (-0.9, 0.9):
                    cyl_x(sx * 0.72, 0.43, bz + wz, 0.43, 0.14, 14, "SM_RailSteel", g)
        g.box((0, 0.55, 0), (2.7, 0.35, 12.0), "SM_MetalDark")
        # gangway at the non-cab end
        g.box((0, 2.1, -9.97), (1.6, 2.2, 0.08), "SM_TrainDoor")
    if cab:
        # windscreen, headlights, destination LED, couplers
        hexa(g, [(-1.0, 2.35, 9.78), (1.0, 2.35, 9.78), (1.0, 2.35, 9.84), (-1.0, 2.35, 9.84)],
             [(-1.0, 3.2, 9.58), (1.0, 3.2, 9.58), (1.0, 3.2, 9.64), (-1.0, 3.2, 9.64)], "SM_Glass")
        g.box((0, 3.35, 9.5), (1.1, 0.22, 0.06), "SM_DestSign")
        for sx in (-1, 1):
            g.box((sx * 0.95, 1.2, 9.95), (0.3, 0.2, 0.08), "SM_Headlight")
            g.box((sx * 0.95, 1.5, 9.95), (0.2, 0.12, 0.08), "SM_Taillight")
        g.box((0, 0.7, 10.1), (0.5, 0.35, 0.3), "SM_MetalDark")
        g.box((0, 3.9, 9.0), (1.2, 0.15, 0.6), "SM_Warning") if False else None
    else:
        g.box((0, 0.7, -10.07), (0.4, 0.3, 0.2), "SM_MetalDark")
        g.box((0, 0.7, 10.07), (0.4, 0.3, 0.2), "SM_MetalDark")
    return geos


# ============================================================ job table
def jobs():
    J = {}
    # buildings (variants share generators with different params/seeds)
    J["Office_A"] = ("Buildings", lambda d: office(11, d, floors=9, w=16, dep=14, wall="SM_ConcreteA", signm="SM_NeonCyan"))
    J["Office_B"] = ("Buildings", lambda d: office(12, d, floors=12, w=18, dep=15, wall="SM_TileBeige", signm="SM_NeonPink"))
    J["Office_C"] = ("Buildings", lambda d: office(13, d, floors=6, w=14, dep=13, wall="SM_ConcreteB", signm="SM_NeonYellow"))
    J["Izakaya_A"] = ("Buildings", lambda d: izakaya(21, d, storeys=4, w=6, dep=12, wall="SM_TileBrown"))
    J["Izakaya_B"] = ("Buildings", lambda d: izakaya(22, d, storeys=5, w=5.2, dep=12, wall="SM_TileBeige"))
    J["Izakaya_C"] = ("Buildings", lambda d: izakaya(23, d, storeys=3, w=7, dep=11, wall="SM_ConcreteB"))
    J["GlassTower_A"] = ("Buildings", lambda d: glass_tower(31, d, floors=26, w=18, dep=18, crown="SM_NeonCyan"))
    J["GlassTower_B"] = ("Buildings", lambda d: glass_tower(32, d, floors=34, w=20, dep=16, tiers=4, crown="SM_NeonPink"))
    J["GlassTower_C"] = ("Buildings", lambda d: glass_tower(33, d, floors=20, w=16, dep=20, tiers=2, crown="SM_NeonWhite"))
    J["DeptStore"] = ("Buildings", lambda d: dept_store(41, d))
    J["Pachinko_A"] = ("Buildings", lambda d: pachinko(51, d, pal=("SM_NeonPink", "SM_NeonYellow", "SM_NeonCyan")))
    J["Pachinko_B"] = ("Buildings", lambda d: pachinko(52, d, w=16, dep=18, H=13, pal=("SM_NeonRed", "SM_NeonWhite", "SM_NeonYellow")))
    J["Konbini_A"] = ("Buildings", lambda d: konbini(61, d))
    J["Konbini_B"] = ("Buildings", lambda d: konbini(62, d, w=10, dep=12, stripes=("SM_NeonRed", "SM_NeonWhite", "SM_NeonGreen")))
    J["Apartment_A"] = ("Buildings", lambda d: apartment(71, d, floors=8, w=20, dep=12, cols=5))
    J["Apartment_B"] = ("Buildings", lambda d: apartment(72, d, floors=10, w=16, dep=12, wall="SM_TileBeige", cols=4))
    J["Apartment_C"] = ("Buildings", lambda d: apartment(73, d, floors=6, w=14, dep=11, wall="SM_ConcreteA", cols=3))
    J["Scramble"] = ("Buildings", lambda d: scramble(81, d))
    # infrastructure
    J["ExpresswayPillar"] = ("Buildings", exp_pillar)
    J["ExpresswayDeck"] = ("Buildings", exp_deck)
    J["ExpresswayBarrier"] = ("Buildings", exp_barrier)
    J["Viaduct"] = ("Buildings", viaduct)
    J["TunnelPortal"] = ("Buildings", tunnel_portal)
    J["Overbridge"] = ("Buildings", overbridge)
    J["TrainCarMid"] = ("Vehicles", lambda d: train_car(d, cab=False, pantograph=True))
    J["TrainCarCab"] = ("Vehicles", lambda d: train_car(d, cab=True))
    return J


if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else None
    write_materials_json()
    for name, (sub, fn) in jobs().items():
        if only and only not in name:
            continue
        export_lods("SM_" + name, fn, sub)

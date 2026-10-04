"""
MINATO COAST - the prop families that dress the five chapters.

Port city (ch1), ocean furniture (ch3), far-shore settlement (ch4) and mountain roadside (ch5).
Everything is authored in Unity-local coordinates through ``minato_lib`` primitives, exported
with the same measured clean-root FBX settings as the bridge kit, and given decimated LODs
where it is placed in quantity.

These are BACKGROUND assets, deliberately cheaper than the bridge: the brief's hero is the
crossing, and the port/mountain dressing has to read correctly at 50-2000 m, not at 2 m. What
they must NOT be is flat concept-image planes or empty graybox - so every family gets real
thickness, a believable silhouette and at least one detail band that catches the low sun.

Origins are at the GROUND contact point (y = 0) with the piece facing +Z, so the Unity scatter
pass can drop them onto terrain with ``LookRotation`` and no per-asset offset table.

All dimensions are PROVISIONAL illustrative tuning.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402


# ============================================================ helpers

def _windows(parts, slot, x0, x1, y0, y1, z, cols, rows, sill=0.10):
    """A grid of recessed window panels on a facade plane at constant Z."""
    out = []
    dw = (x1 - x0) / cols
    dh = (y1 - y0) / rows
    for c in range(cols):
        for r in range(rows):
            cx = x0 + (c + 0.5) * dw
            cy = y0 + (r + 0.5) * dh
            out.append(M.box(f"{slot}_w{c}_{r}", (cx, cy, z),
                             (dw * 0.62, dh * 0.58, 0.14), uv_per_m=1.6))
    parts.extend(out)
    return out


def _slab_block(parts, name, w, d, h, floors, seed=0):
    """
    A mid-rise block: shell, floor-band cornices, a parapet and window grids on all four
    facades. The bands are what stop a box reading as a box.
    """
    parts.append(M.box(f"{name}_shell", (0.0, h * 0.5, 0.0), (w, h, d), uv_per_m=0.30))
    for f in range(1, floors):
        y = h * f / floors
        parts.append(M.box(f"{name}_band{f}", (0.0, y, 0.0), (w + 0.16, 0.14, d + 0.16),
                           uv_per_m=0.7))
    parts.append(M.box(f"{name}_para", (0.0, h + 0.28, 0.0), (w + 0.22, 0.56, d + 0.22),
                       uv_per_m=0.6))
    return parts


# ============================================================ chapter 1: port city

def port_tower():
    """
    The red lattice port tower landmark from concept 01 - the thing that tells you which city
    you are in from 2 km away. Tapered four-leg lattice with an observation deck.
    """
    H = 112.0
    steel, grey = [], []
    legs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]

    def half(y):
        # Wide splayed base, tight waist, slight flare at the deck.
        f = y / H
        return 13.0 * (1.0 - f) ** 1.55 + 2.6

    levels = [i / 16.0 * H for i in range(17)]
    for (sx, sz) in legs:
        for i in range(len(levels) - 1):
            y0, y1 = levels[i], levels[i + 1]
            a = (sx * half(y0), y0, sz * half(y0))
            b = (sx * half(y1), y1, sz * half(y1))
            t = 1.30 - 0.55 * (y0 / H)
            steel.append(M.beam(f"leg{sx}{sz}_{i}", a, b, t, t, up_hint=(0, 0, 1),
                                uv_per_m=0.5))
    # Ring beams and face diagonals.
    for i, y in enumerate(levels):
        hh = half(y)
        ring = [(sx * hh, y, sz * hh) for (sx, sz) in legs]
        for j in range(4):
            steel.append(M.beam(f"ring{i}_{j}", ring[j], ring[(j + 1) % 4], 0.55, 0.55,
                                up_hint=(0, 1, 0), uv_per_m=0.6))
        if i + 1 < len(levels):
            h2 = half(levels[i + 1])
            nxt = [(sx * h2, levels[i + 1], sz * h2) for (sx, sz) in legs]
            for j in range(4):
                steel.append(M.beam(f"dia{i}_{j}", ring[j], nxt[(j + 1) % 4], 0.34, 0.34,
                                    up_hint=(0, 1, 0), uv_per_m=0.7))
    # Observation deck + mast.
    grey.append(M.prism("deck", [(-9.0, 0.0), (9.0, 0.0), (7.4, 4.6), (-7.4, 4.6)],
                        (0.0, H * 0.70, -9.0), (0.0, H * 0.70, 9.0), up_hint=(0, 1, 0),
                        uv_per_m=0.4))
    grey.append(M.box("deckband", (0.0, H * 0.70 + 3.2, 0.0), (18.6, 1.1, 18.6), uv_per_m=0.6))
    steel.append(M.tube("mast", (0.0, H, 0.0), (0.0, H + 26.0, 0.0), 0.75, segments=8,
                        uv_per_m=0.4, taper=0.30))

    st = M.finish(steel, "st", bevel=0.03, smooth_angle=32.0)
    gr = M.finish(grey, "gr", bevel=0.04, smooth_angle=30.0)
    M.paint(st, "steel_vermilion")
    M.paint(gr, "concrete")
    return S.join([st, gr], "Minato_Port_Tower_LOD0")


def ferris_wheel():
    """The waterfront ferris wheel from concept 01. Vertical wheel in the XY plane."""
    R = 27.0
    N = 24
    steel, cab = [], []
    hub = (0.0, R + 4.5, 0.0)
    rim = [(math.cos(i / N * math.tau) * R, R + 4.5 + math.sin(i / N * math.tau) * R, 0.0)
           for i in range(N)]
    for zo in (-1.6, 1.6):
        for i in range(N):
            a = (rim[i][0], rim[i][1], zo)
            b = (rim[(i + 1) % N][0], rim[(i + 1) % N][1], zo)
            steel.append(M.beam(f"rim{i}_{zo}", a, b, 0.42, 0.42, up_hint=(0, 0, 1),
                                uv_per_m=0.7))
            steel.append(M.tube(f"spoke{i}_{zo}", (hub[0], hub[1], zo), a, 0.10, segments=6,
                                uv_per_m=0.8))
    steel.append(M.tube("hub", (0.0, R + 4.5, -2.4), (0.0, R + 4.5, 2.4), 1.25, segments=12,
                        uv_per_m=0.6))
    # A-frame legs.
    for sx in (-1.0, 1.0):
        for zo in (-1.0, 1.0):
            steel.append(M.beam(f"leg{sx}{zo}", (sx * 13.0, 0.0, zo * 7.0),
                                (0.0, R + 4.5, zo * 2.2), 1.15, 1.15, up_hint=(0, 0, 1),
                                uv_per_m=0.5))
    # Gondolas hang from the rim just INSIDE it. Pushing them outboard (radius x 1.06) leaves
    # them reading as loose red squares orbiting clear of the wheel, which is what the first
    # contact sheet showed.
    for i in range(N):
        a = i / N * math.tau
        px = math.cos(a) * (R - 1.45)
        py = R + 4.5 + math.sin(a) * (R - 1.45) - 1.35
        cab.append(M.box(f"cab{i}", (px, py, 0.0), (2.1, 2.0, 2.4), uv_per_m=1.0))
        cab.append(M.box(f"cabh{i}", (px, py + 1.35, 0.0), (0.36, 0.9, 0.36), uv_per_m=1.6))
    st = M.finish(steel, "st", bevel=0.02, smooth_angle=32.0)
    cb = M.finish(cab, "cb", bevel=0.05, smooth_angle=34.0)
    M.paint(st, "hull_white")
    M.paint(cb, "steel_vermilion")
    return S.join([st, cb], "Minato_Port_FerrisWheel_LOD0")


def container_crane():
    """Ship-to-shore gantry crane - the port skyline signature of concept 01."""
    LEG = 34.0
    grey, warn = [], []
    for sx in (-1.0, 1.0):
        for sz in (-1.0, 1.0):
            grey.append(M.beam(f"leg{sx}{sz}", (sx * 10.5, 0.0, sz * 9.0),
                               (sx * 9.0, LEG, sz * 7.5), 1.5, 1.5, up_hint=(0, 0, 1),
                               uv_per_m=0.5))
        # Portal bracing.
        grey.append(M.beam(f"br{sx}", (sx * 10.2, 12.0, -8.6), (sx * 9.6, 12.0, 8.6),
                           0.8, 0.9, up_hint=(0, 1, 0), uv_per_m=0.6))
    # Sill and machinery house.
    grey.append(M.box("sill", (0.0, LEG + 1.2, 0.0), (20.0, 2.4, 17.0), uv_per_m=0.4))
    grey.append(M.box("house", (0.0, LEG + 6.0, 4.2), (11.0, 7.0, 9.0), uv_per_m=0.5))
    # Boom: seaward cantilever plus landside counter-boom, on the classic A-frame.
    grey.append(M.beam("boom", (0.0, LEG + 3.0, 8.0), (0.0, LEG + 6.5, 58.0), 6.4, 2.6,
                       up_hint=(0, 1, 0), uv_per_m=0.35))
    grey.append(M.beam("cboom", (0.0, LEG + 3.0, -6.0), (0.0, LEG + 5.2, -26.0), 6.0, 2.4,
                       up_hint=(0, 1, 0), uv_per_m=0.35))
    grey.append(M.beam("apex", (0.0, LEG + 4.0, 0.0), (0.0, LEG + 26.0, -1.0), 2.2, 2.2,
                       up_hint=(0, 0, 1), uv_per_m=0.5))
    for z, tgt in ((54.0, 1.0), (30.0, 1.0), (-24.0, 1.0)):
        grey.append(M.tube(f"stay{z:.0f}", (0.0, LEG + 25.0, -1.0),
                           (0.0, LEG + 5.4, z * tgt), 0.22, segments=6, uv_per_m=0.5))
    # Trolley and spreader.
    grey.append(M.box("trolley", (0.0, LEG + 1.6, 26.0), (5.2, 2.0, 5.0), uv_per_m=0.7))
    warn.append(M.box("spreader", (0.0, 13.0, 26.0), (2.6, 1.0, 12.4), uv_per_m=0.7))
    for sx in (-1.0, 1.0):
        warn.append(M.box(f"stripe{sx}", (sx * 9.8, 3.0, 0.0), (1.7, 6.0, 18.4), uv_per_m=0.6))
    g = M.finish(grey, "g", bevel=0.03, smooth_angle=32.0)
    w = M.finish(warn, "w", bevel=0.03, smooth_angle=32.0)
    M.paint(g, "hull_white")
    M.paint(w, "steel_vermilion")
    return S.join([g, w], "Minato_Port_ContainerCrane_LOD0")


def warehouse():
    """Long quayside transit shed: ribbed steel walls, shallow pitched roof, roller doors."""
    W, D, H = 26.0, 54.0, 11.5
    body, roof, door = [], [], []
    body.append(M.box("shell", (0.0, H * 0.5, 0.0), (W, H, D), uv_per_m=0.26))
    # Corrugation ribs.
    x = -W * 0.5 + 1.0
    i = 0
    while x < W * 0.5:
        for sz in (-1.0, 1.0):
            body.append(M.box(f"rib{i}{sz}", (x, H * 0.5, sz * (D * 0.5 + 0.04)),
                              (0.24, H - 0.4, 0.10), uv_per_m=1.0))
        x += 2.0
        i += 1
    z = -D * 0.5 + 1.0
    i = 0
    while z < D * 0.5:
        for sx in (-1.0, 1.0):
            body.append(M.box(f"rbz{i}{sx}", (sx * (W * 0.5 + 0.04), H * 0.5, z),
                              (0.10, H - 0.4, 0.24), uv_per_m=1.0))
        z += 2.0
        i += 1
    # Shallow gable roof.
    roof.append(M.prism("roof",
                        [(-W * 0.5 - 0.5, 0.0), (0.0, 2.3), (W * 0.5 + 0.5, 0.0),
                         (W * 0.5 + 0.5, -0.4), (0.0, 1.9), (-W * 0.5 - 0.5, -0.4)],
                        (0.0, H, -D * 0.5 - 0.5), (0.0, H, D * 0.5 + 0.5),
                        up_hint=(0, 1, 0), uv_per_m=0.3))
    for k in range(3):
        door.append(M.box(f"door{k}", (-W * 0.28 + k * W * 0.28, 2.6, D * 0.5 + 0.06),
                          (5.2, 5.2, 0.16), uv_per_m=0.8))
    b = M.finish(body, "b", bevel=0.02, smooth_angle=30.0)
    r = M.finish(roof, "r", bevel=0.04, smooth_angle=28.0)
    d = M.finish(door, "d", bevel=0.02, smooth_angle=34.0)
    M.paint(b, "hull_white")
    M.paint(r, "roof_slate")
    M.paint(d, "steel_grey")
    return S.join([b, r, d], "Minato_Port_Warehouse_LOD0")


def city_block(tag, w, d, h, floors, roofy=True):
    """A mid-rise waterfront block. Three variants dress the dense port city of concept 01."""
    body, glass, roof = [], [], []
    _slab_block(body, "b", w, d, h, floors)
    for sz in (-1.0, 1.0):
        _windows(glass, f"z{sz}", -w * 0.40, w * 0.40, 1.6, h - 1.4,
                 sz * (d * 0.5 + 0.03), max(2, int(w / 3.4)), floors)
    for sx in (-1.0, 1.0):
        # Side facades: same grid, rotated into the XZ sense by swapping the box axes.
        dw = (d * 0.80) / max(2, int(d / 3.4))
        cols = max(2, int(d / 3.4))
        dh = (h - 3.0) / floors
        for c in range(cols):
            for r in range(floors):
                glass.append(M.box(
                    f"x{sx}_{c}_{r}",
                    (sx * (w * 0.5 + 0.03), 1.6 + (r + 0.5) * dh,
                     -d * 0.40 + (c + 0.5) * dw),
                    (0.14, dh * 0.58, dw * 0.62), uv_per_m=1.6))
    if roofy:
        roof.append(M.box("plant", (w * 0.14, h + 1.9, -d * 0.12),
                          (w * 0.30, 2.6, d * 0.26), uv_per_m=0.6))
        roof.append(M.tube("tank", (-w * 0.22, h + 0.6, d * 0.16),
                           (-w * 0.22, h + 3.6, d * 0.16), 1.3, segments=10, uv_per_m=0.6))
    b = M.finish(body, "b", bevel=0.03, smooth_angle=30.0)
    g = M.finish(glass, "g", bevel=0.015, smooth_angle=34.0)
    M.paint(b, "white_render")
    M.paint(g, "glass")
    objs = [b, g]
    if roof:
        rf = M.finish(roof, "r", bevel=0.03, smooth_angle=32.0)
        M.paint(rf, "steel_grey")
        objs.append(rf)
    return S.join(objs, f"Minato_City_Block{tag}_LOD0")


def shophouse():
    """Low waterfront shophouse with an awning - the human-scale layer of the quay."""
    W, D, H = 9.5, 11.0, 8.2
    body, roof, awn, glass = [], [], [], []
    _slab_block(body, "b", W, D, H, 3)
    _windows(glass, "f", -W * 0.36, W * 0.36, 3.5, H - 1.2, D * 0.5 + 0.03, 3, 2)
    roof.append(M.prism("roof",
                        [(-W * 0.5 - 0.6, 0.0), (0.0, 2.0), (W * 0.5 + 0.6, 0.0),
                         (W * 0.5 + 0.6, -0.35), (0.0, 1.65), (-W * 0.5 - 0.6, -0.35)],
                        (0.0, H + 0.56, -D * 0.5 - 0.6), (0.0, H + 0.56, D * 0.5 + 0.6),
                        up_hint=(0, 1, 0), uv_per_m=0.5))
    awn.append(M.prism("awning", [(-W * 0.5, 0.0), (W * 0.5, 0.0), (W * 0.5, -0.12),
                                  (-W * 0.5, -0.12)],
                       (0.0, 3.3, D * 0.5), (0.0, 2.85, D * 0.5 + 2.3),
                       up_hint=(0, 1, 0), uv_per_m=0.8))
    glass.append(M.box("shopfront", (0.0, 1.7, D * 0.5 + 0.04), (W * 0.78, 2.9, 0.16),
                       uv_per_m=1.0))
    b = M.finish(body, "b", bevel=0.03, smooth_angle=30.0)
    g = M.finish(glass, "g", bevel=0.015, smooth_angle=34.0)
    r = M.finish(roof, "r", bevel=0.03, smooth_angle=28.0)
    a = M.finish(awn, "a", bevel=0.02, smooth_angle=34.0)
    M.paint(b, "white_render")
    M.paint(g, "glass")
    M.paint(r, "terracotta")
    M.paint(a, "steel_vermilion")
    return S.join([b, g, r, a], "Minato_City_ShopHouse_LOD0")


def _hull(name, length, beam_w, depth, sheer=0.55):
    """
    A displacement hull: pointed bow, full midbody, tucked stern, with sheer. Used by every
    vessel so the marina does not fill up with floating boxes.
    """
    parts = []
    stations = [
        (-0.50, 0.06, 0.55), (-0.40, 0.34, 0.74), (-0.26, 0.66, 0.88),
        (-0.08, 0.92, 0.97), (0.10, 1.00, 1.00), (0.28, 0.94, 0.98),
        (0.42, 0.74, 0.90), (0.50, 0.46, 0.80),
    ]
    for i in range(len(stations) - 1):
        z0, w0, d0 = stations[i]
        z1, w1, d1 = stations[i + 1]
        y0 = -depth * d0 * 0.5 + sheer * abs(z0) * 0.5
        y1 = -depth * d1 * 0.5 + sheer * abs(z1) * 0.5
        parts.append(M.beam(f"{name}_h{i}",
                            (0.0, y0, z0 * length), (0.0, y1, z1 * length),
                            beam_w * w0, depth * d0, up_hint=(0, 1, 0), uv_per_m=0.45,
                            taper=(w1 / max(w0, 1e-6))))
    return parts


def fishing_boat():
    """Small trawler for the marina in concept 01."""
    hull = _hull("fb", 11.0, 3.4, 1.9)
    house, mast = [], []
    house.append(M.box("wh", (0.0, 1.05, -1.3), (2.5, 2.1, 3.4), uv_per_m=0.7))
    house.append(M.box("wh2", (0.0, 2.35, -1.3), (2.0, 0.6, 2.8), uv_per_m=0.9))
    mast.append(M.tube("mast", (0.0, 2.6, -1.0), (0.0, 8.4, -0.4), 0.10, segments=6,
                       uv_per_m=0.8, taper=0.55))
    mast.append(M.beam("boom", (0.0, 6.2, -0.6), (0.0, 3.4, 4.2), 0.12, 0.12,
                       up_hint=(0, 1, 0), uv_per_m=0.9))
    h = M.finish(hull, "h", bevel=0.03, smooth_angle=44.0)
    w = M.finish(house, "w", bevel=0.03, smooth_angle=32.0)
    m = M.finish(mast, "m", bevel=0.01, smooth_angle=40.0)
    M.paint(h, "hull_blue")
    M.paint(w, "hull_white")
    M.paint(m, "steel_grey")
    return S.join([h, w, m], "Minato_Sea_FishingBoat_LOD0")


def ferry():
    """Passenger ferry crossing under the bridge - reads at 300-1500 m."""
    hull = _hull("fr", 46.0, 11.0, 5.4)
    sup, funnel = [], []
    sup.append(M.box("d1", (0.0, 3.6, -2.0), (9.6, 3.4, 30.0), uv_per_m=0.35))
    sup.append(M.box("d2", (0.0, 6.6, -4.0), (8.2, 2.8, 21.0), uv_per_m=0.4))
    sup.append(M.box("bridge", (0.0, 9.0, 4.0), (7.0, 2.2, 7.0), uv_per_m=0.55))
    funnel.append(M.tube("funnel", (0.0, 8.0, -8.0), (0.0, 14.6, -8.6), 1.5, segments=10,
                         uv_per_m=0.5, taper=0.86))
    h = M.finish(hull, "h", bevel=0.04, smooth_angle=44.0)
    s = M.finish(sup, "s", bevel=0.04, smooth_angle=30.0)
    f = M.finish(funnel, "f", bevel=0.03, smooth_angle=40.0)
    M.paint(h, "hull_blue")
    M.paint(s, "hull_white")
    M.paint(f, "steel_vermilion")
    return S.join([h, s, f], "Minato_Sea_Ferry_LOD0")


def cargo_ship():
    """
    Laden container ship for the open-ocean chapter's sense of distance and scale.

    The container deck load is the whole silhouette, so it has to be built as a stack of
    discrete boxes with air between them and a mix of liveries. An earlier pass ran 11 bays of
    5 flush rows from z = -44 to +121 on a hull only 178 m long: the boxes overran the bow,
    merged into one unbroken slab and the ship rendered as a 200 m red brick wall.
    """
    hull = _hull("cs", 178.0, 29.0, 15.0, sheer=1.2)
    sup = []
    cans = {"steel_vermilion": [], "hull_blue": [], "rust": [], "foliage": []}
    sup.append(M.box("house", (0.0, 11.0, -62.0), (24.0, 18.0, 17.0), uv_per_m=0.28))
    sup.append(M.box("bridge", (0.0, 21.5, -60.0), (26.0, 3.2, 10.0), uv_per_m=0.45))
    sup.append(M.tube("funnel", (0.0, 20.0, -70.0), (0.0, 30.0, -70.0), 3.1, segments=10,
                      uv_per_m=0.4, taper=0.9))
    # Hatch covers the stacks actually sit on.
    sup.append(M.box("hatch", (0.0, 2.4, 10.0), (25.0, 1.2, 128.0), uv_per_m=0.3))

    r = M.rng(11)
    slots = list(cans.keys())
    BAY, ROW = 13.2, 5.3     # pitch; boxes are smaller than the pitch, which IS the air gap
    for bay in range(9):
        z = -46.0 + bay * BAY
        # Hull narrows towards the bow, so the deck load must narrow with it.
        taper = 1.0 if z < 40.0 else max(0.35, 1.0 - (z - 40.0) / 60.0)
        rows = max(1, int(round(5 * taper)))
        for row in range(rows):
            x = -(rows - 1) * 0.5 * ROW + row * ROW
            for t in range(int(r.integers(2, 5))):
                cans[slots[int(r.integers(0, len(slots)))]].append(
                    M.box(f"c{bay}_{row}_{t}", (x, 3.9 + t * 2.75, z),
                          (4.7, 2.50, 11.6), uv_per_m=0.5))

    h = M.finish(hull, "h", bevel=0.06, smooth_angle=44.0)
    s = M.finish(sup, "s", bevel=0.05, smooth_angle=30.0)
    M.paint(h, "rust")
    M.paint(s, "hull_white")
    objs = [h, s]
    for slot, group in cans.items():
        if group:
            g = M.finish(group, f"c_{slot}", bevel=0.04, smooth_angle=34.0)
            M.paint(g, slot)
            objs.append(g)
    return S.join(objs, "Minato_Sea_CargoShip_LOD0")


def sailboat():
    """Small yacht - the scattered white triangles in concept 03."""
    hull = _hull("sb", 9.0, 2.7, 1.5)
    rig = []
    rig.append(M.tube("mast", (0.0, 0.6, 0.4), (0.0, 12.2, 0.2), 0.075, segments=6,
                      uv_per_m=0.8, taper=0.5))
    rig.append(M.plate("sailM", [(0.02, 0.9, 0.3), (0.02, 11.6, 0.2),
                                 (0.02, 1.1, -3.6), (0.02, 0.9, -3.4)], uv_per_m=0.3))
    rig.append(M.plate("sailJ", [(-0.02, 1.0, 0.5), (-0.02, 10.4, 0.3),
                                 (-0.02, 0.9, 3.9), (-0.02, 0.9, 3.7)], uv_per_m=0.3))
    h = M.finish(hull, "h", bevel=0.02, smooth_angle=44.0)
    rg = M.finish(rig, "r", bevel=0.0, weld=0.0, smooth_angle=None)
    M.paint(h, "hull_white")
    M.paint(rg, "hull_white")
    return S.join([h, rg], "Minato_Sea_Sailboat_LOD0")


def quay_kit():
    """Bollard, crate stack and a mooring cleat, joined as one cheap scatter piece."""
    parts = []
    parts.append(M.tube("bollard", (0.0, 0.0, 0.0), (0.0, 0.95, 0.0), 0.24, segments=10,
                        uv_per_m=1.0, taper=0.78))
    parts.append(M.box("bcap", (0.0, 1.02, 0.0), (0.62, 0.14, 0.62), uv_per_m=1.4))
    obj = M.finish(parts, "q", bevel=0.02, smooth_angle=34.0)
    M.paint(obj, "steel_dark")
    obj.name = "Minato_Port_Bollard_LOD0"
    return obj


def crate_stack():
    """
    A quayside container stack. Laid out on a real grid with air gaps and mixed liveries -
    jittered overlapping boxes just weld into one red slab, which is how the first pass read.
    """
    r = M.rng(23)
    groups = {"steel_vermilion": [], "hull_blue": [], "rust": [], "steel_grey": []}
    slots = list(groups.keys())
    for row in range(3):
        for col in range(2):
            h = int(r.integers(1, 4))
            for t in range(h):
                groups[slots[int(r.integers(0, len(slots)))]].append(
                    M.box(f"c{row}_{col}_{t}",
                          (col * 5.4 - 2.7, 1.30 + t * 2.65, row * 12.4 - 12.4),
                          (4.9, 2.55, 11.7), uv_per_m=0.5))
    objs = []
    for slot, group in groups.items():
        if group:
            g = M.finish(group, f"cr_{slot}", bevel=0.04, smooth_angle=34.0)
            M.paint(g, slot)
            objs.append(g)
    obj = S.join(objs, "Minato_Port_CrateStack_LOD0")
    return obj


def street_lamp():
    parts = [
        M.box("base", (0.0, 0.18, 0.0), (0.44, 0.36, 0.44), uv_per_m=1.2),
        M.tube("col", (0.0, 0.3, 0.0), (0.0, 5.2, 0.0), 0.085, segments=8, uv_per_m=0.8,
               taper=0.7),
        M.tube("arm", (0.0, 5.1, 0.0), (0.0, 5.6, 1.3), 0.065, segments=6, uv_per_m=0.9),
        M.box("lum", (0.0, 5.52, 1.5), (0.34, 0.14, 0.66), uv_per_m=1.2),
    ]
    obj = M.finish(parts, "sl", bevel=0.01, smooth_angle=36.0)
    M.paint(obj, "lamp_grey")
    obj.name = "Minato_Port_StreetLamp_LOD0"
    return obj


def palm():
    """Waterfront palm from concept 01 - crossed fronds, cheap and reads at distance."""
    H = 8.6
    trunk, fronds = [], []
    prev = Vector((0.0, 0.0, 0.0))
    for i in range(1, 9):
        f = i / 8.0
        nxt = Vector((math.sin(f * 1.5) * 0.9, H * f, math.cos(f * 2.1) * 0.35))
        trunk.append(M.tube(f"t{i}", prev, nxt, 0.30 * (1.0 - 0.42 * f), segments=7,
                            uv_per_m=0.8))
        prev = nxt
    for i in range(9):
        a = i / 9.0 * math.tau
        tip = prev + Vector((math.cos(a) * 3.6, -0.9 + 1.2 * math.cos(i * 1.3), math.sin(a) * 3.6))
        mid = prev + Vector((math.cos(a) * 1.7, 1.15, math.sin(a) * 1.7))
        fronds.append(M.plate(f"f{i}", [
            (prev.x - math.sin(a) * 0.20, prev.y, prev.z + math.cos(a) * 0.20),
            (mid.x - math.sin(a) * 0.62, mid.y, mid.z + math.cos(a) * 0.62),
            (tip.x, tip.y, tip.z),
            (prev.x + math.sin(a) * 0.20, prev.y, prev.z - math.cos(a) * 0.20),
        ], uv_per_m=0.4))
    t = M.finish(trunk, "t", bevel=0.01, smooth_angle=44.0)
    fr = M.finish(fronds, "f", bevel=0.0, weld=0.0, smooth_angle=None)
    M.paint(t, "timber")
    M.paint(fr, "foliage")
    return S.join([t, fr], "Minato_Flora_Palm_LOD0")


# ============================================================ chapter 3/4: ocean & shore

def lighthouse():
    """
    The lighthouse from concepts 03 and 04.

    It is authored as a FREESTANDING tower with its own rock plinth precisely because the brief
    forbids the bridge terminating at it: it must be placeable on a separate promontory far off
    the alignment, so it carries no deck, no approach and no road connection whatsoever.
    """
    H = 23.0
    tower, band, lamp, rock = [], [], [], []
    tower.append(M.tube("shaft", (0.0, 0.0, 0.0), (0.0, H, 0.0), 3.6, segments=16,
                        uv_per_m=0.35, taper=0.56))
    tower.append(M.tube("plinth", (0.0, -0.2, 0.0), (0.0, 2.4, 0.0), 4.5, segments=16,
                        uv_per_m=0.4, taper=0.92))
    for i in range(3):
        y = 5.0 + i * 5.6
        rad = 3.6 - (3.6 - 3.6 * 0.56) * (y / H)
        band.append(M.tube(f"band{i}", (0.0, y, 0.0), (0.0, y + 2.2, 0.0), rad + 0.06,
                           segments=16, uv_per_m=0.5))
    gy = H
    lamp.append(M.tube("gallery", (0.0, gy, 0.0), (0.0, gy + 0.5, 0.0), 3.1, segments=16,
                       uv_per_m=0.6))
    lamp.append(M.tube("lantern", (0.0, gy + 0.5, 0.0), (0.0, gy + 3.6, 0.0), 2.2,
                       segments=14, uv_per_m=0.6))
    lamp.append(M.tube("cap", (0.0, gy + 3.6, 0.0), (0.0, gy + 5.4, 0.0), 2.4, segments=14,
                       uv_per_m=0.7, taper=0.05))
    r = M.rng(31)
    for i in range(22):
        a = r.uniform(0, math.tau)
        rad = r.uniform(5.0, 13.0)
        s = r.uniform(2.2, 5.4)
        rock.append(M.box(f"rk{i}", (math.cos(a) * rad, -0.6 + s * 0.22, math.sin(a) * rad),
                          (s, s * 0.75, s * 1.15), uv_per_m=0.35))
    t = M.finish(tower, "t", bevel=0.04, smooth_angle=40.0)
    bd = M.finish(band, "b", bevel=0.03, smooth_angle=40.0)
    lm = M.finish(lamp, "l", bevel=0.03, smooth_angle=38.0)
    rk = M.finish(rock, "r", bevel=0.06, smooth_angle=32.0)
    M.paint(t, "white_render")
    M.paint(bd, "steel_vermilion")
    M.paint(lm, "steel_dark")
    M.paint(rk, "rock")
    return S.join([t, bd, lm, rk], "Minato_Sea_Lighthouse_LOD0")


def islet(tag, radius, height, seed, treed=True):
    """A wooded islet for the ocean chapter's middle distance."""
    r = M.rng(seed)
    rock, tree = [], []
    for i in range(34):
        a = r.uniform(0, math.tau)
        rad = r.uniform(0.0, 1.0) ** 0.6 * radius
        s = float(r.uniform(0.35, 1.0) * radius * 0.42)
        y = height * (1.0 - rad / max(radius, 1e-3)) ** 1.5
        rock.append(M.box(f"r{i}", (math.cos(a) * rad, y * 0.5 - 1.0, math.sin(a) * rad),
                          (s * 1.4, max(s, y * 0.9), s * 1.5), uv_per_m=0.22))
    if treed:
        for i in range(16):
            a = r.uniform(0, math.tau)
            rad = r.uniform(0.0, 0.7) * radius
            y = height * (1.0 - rad / max(radius, 1e-3)) ** 1.5
            h = float(r.uniform(6.0, 13.0))
            tip = (math.cos(a) * rad, y + h, math.sin(a) * rad)
            base = (math.cos(a) * rad, y, math.sin(a) * rad)
            tree.append(M.beam(f"t{i}", base, tip, h * 0.42, h * 0.42, up_hint=(0, 0, 1),
                               uv_per_m=0.3, taper=0.06))
    rk = M.finish(rock, "r", bevel=0.08, smooth_angle=30.0)
    M.paint(rk, "rock")
    objs = [rk]
    if tree:
        tr = M.finish(tree, "t", bevel=0.05, smooth_angle=30.0)
        M.paint(tr, "foliage")
        objs.append(tr)
    return S.join(objs, f"Minato_Sea_Islet{tag}_LOD0")


def sea_stack():
    r = M.rng(41)
    parts = []
    for i in range(12):
        f = i / 11.0
        s = (1.0 - f * 0.62) * 7.0
        parts.append(M.box(f"s{i}", (float(r.uniform(-0.8, 0.8)), 2.2 + i * 2.5,
                                     float(r.uniform(-0.8, 0.8))),
                           (s, 2.6, s * 1.15), uv_per_m=0.3))
    obj = M.finish(parts, "ss", bevel=0.10, smooth_angle=30.0)
    M.paint(obj, "rock")
    obj.name = "Minato_Sea_SeaStack_LOD0"
    return obj


def villa(tag, w, d, h, floors, roof_slot):
    """Mediterranean-Japanese coastal home from concept 04's green hillside."""
    body, roof, glass = [], [], []
    body.append(M.box("shell", (0.0, h * 0.5, 0.0), (w, h, d), uv_per_m=0.4))
    body.append(M.box("plinth", (0.0, 0.25, 0.0), (w + 0.5, 0.5, d + 0.5), uv_per_m=0.7))
    _windows(glass, "f", -w * 0.34, w * 0.34, 1.3, h - 0.9, d * 0.5 + 0.03, 3, floors)
    _windows(glass, "b", -w * 0.34, w * 0.34, 1.3, h - 0.9, -d * 0.5 - 0.03, 2, floors)
    # Hipped tile roof with a real overhang - the strongest silhouette cue at distance.
    roof.append(M.prism("roof",
                        [(-w * 0.5 - 0.9, 0.0), (0.0, h * 0.30), (w * 0.5 + 0.9, 0.0),
                         (w * 0.5 + 0.9, -0.28), (0.0, h * 0.30 - 0.28),
                         (-w * 0.5 - 0.9, -0.28)],
                        (0.0, h, -d * 0.5 - 0.9), (0.0, h, d * 0.5 + 0.9),
                        up_hint=(0, 1, 0), uv_per_m=0.7))
    b = M.finish(body, "b", bevel=0.03, smooth_angle=30.0)
    g = M.finish(glass, "g", bevel=0.015, smooth_angle=34.0)
    r = M.finish(roof, "r", bevel=0.03, smooth_angle=28.0)
    M.paint(b, "white_render")
    M.paint(g, "glass")
    M.paint(r, roof_slot)
    return S.join([b, g, r], f"Minato_Shore_Villa{tag}_LOD0")


def masonry_wall():
    """
    A 6 m stone retaining-wall module - the coursed masonry from concept 05, which is what makes
    a mountain road read as engineered rather than as a trench cut in a mesh.
    """
    L, H = 6.0, 5.2
    parts = []
    parts.append(M.prism("body", [(0.0, 0.0), (1.30, 0.0), (0.62, H), (0.0, H)],
                         (0.0, 0.0, 0.0), (0.0, 0.0, L), up_hint=(0, 1, 0), uv_per_m=0.5))
    r = M.rng(53)
    course_h = 0.62
    y = 0.16
    i = 0
    while y < H - 0.5:
        batter = 1.30 - (1.30 - 0.62) * (y / H)
        z = 0.12
        while z < L - 0.2:
            bw = float(r.uniform(0.55, 1.05))
            parts.append(M.box(f"s{i}", (batter + 0.045, y + course_h * 0.5, z + bw * 0.5),
                               (0.12, course_h * 0.84, bw * 0.90), uv_per_m=1.0))
            z += bw + 0.06
            i += 1
        y += course_h
    # Coping.
    parts.append(M.prism("cope", [(-0.10, 0.0), (0.78, 0.0), (0.74, 0.34), (-0.10, 0.34)],
                         (0.0, H, 0.0), (0.0, H, L), up_hint=(0, 1, 0), uv_per_m=0.8))
    # Weep drain.
    parts.append(M.tube("weep", (0.86, 1.05, L * 0.5), (1.12, 1.02, L * 0.5), 0.09,
                        segments=8, uv_per_m=1.4))
    obj = M.finish(parts, "mw", bevel=0.02, smooth_angle=34.0)
    M.paint(obj, "rock")
    obj.name = "Minato_Road_MasonryWall_LOD0"
    return obj


def guardrail_post():
    """W-beam guardrail post; the beam itself is swept continuously by the Unity pass."""
    parts = [
        M.beam("post", (0.0, 0.0, 0.0), (0.0, 0.78, 0.0), 0.14, 0.18, up_hint=(0, 0, 1),
               uv_per_m=1.0),
        M.box("spacer", (0.0, 0.64, 0.0), (0.16, 0.26, 0.20), uv_per_m=1.4),
    ]
    obj = M.finish(parts, "gp", bevel=0.01, smooth_angle=34.0)
    M.paint(obj, "steel_grey")
    obj.name = "Minato_Road_GuardrailPost_LOD0"
    return obj


def conifer():
    """Cedar/pine for the forested switchbacks of concept 05."""
    H = 17.0
    trunk, foli = [], []
    trunk.append(M.tube("t", (0.0, 0.0, 0.0), (0.0, H * 0.96, 0.0), 0.44, segments=8,
                        uv_per_m=0.6, taper=0.20))
    for i in range(6):
        f = i / 6.0
        y = 2.6 + f * (H - 5.0)
        rad = 3.9 * (1.0 - f) ** 0.85 + 0.5
        foli.append(M.tube(f"c{i}", (0.0, y, 0.0), (0.0, y + (H - y) * 0.52, 0.0), rad,
                           segments=9, uv_per_m=0.3, taper=0.08))
    t = M.finish(trunk, "t", bevel=0.01, smooth_angle=44.0)
    f = M.finish(foli, "f", bevel=0.0, weld=0.0, smooth_angle=42.0)
    M.paint(t, "timber")
    M.paint(f, "foliage")
    return S.join([t, f], "Minato_Flora_Conifer_LOD0")


def broadleaf():
    H = 11.5
    trunk, foli = [], []
    prev = Vector((0.0, 0.0, 0.0))
    for i in range(1, 5):
        f = i / 4.0
        nxt = Vector((math.sin(f * 2.0) * 0.55, H * 0.52 * f, math.cos(f * 1.4) * 0.4))
        trunk.append(M.tube(f"t{i}", prev, nxt, 0.42 * (1.0 - 0.5 * f), segments=7,
                            uv_per_m=0.7))
        prev = nxt
    r = M.rng(61)
    for i in range(7):
        a = r.uniform(0, math.tau)
        rad = float(r.uniform(0.5, 2.6))
        s = float(r.uniform(3.0, 4.8))
        foli.append(M.tube(f"b{i}",
                           (prev.x + math.cos(a) * rad, prev.y + float(r.uniform(0.4, 3.4)),
                            prev.z + math.sin(a) * rad),
                           (prev.x + math.cos(a) * rad, prev.y + float(r.uniform(3.6, 6.4)),
                            prev.z + math.sin(a) * rad),
                           s, segments=8, uv_per_m=0.3, taper=0.45))
    t = M.finish(trunk, "t", bevel=0.01, smooth_angle=44.0)
    f = M.finish(foli, "f", bevel=0.0, weld=0.0, smooth_angle=48.0)
    M.paint(t, "timber")
    M.paint(f, "foliage_warm")
    return S.join([t, f], "Minato_Flora_Broadleaf_LOD0")


def shrub():
    r = M.rng(67)
    parts = []
    for i in range(6):
        a = r.uniform(0, math.tau)
        rad = float(r.uniform(0.0, 0.85))
        s = float(r.uniform(0.85, 1.55))
        parts.append(M.tube(f"s{i}", (math.cos(a) * rad, 0.05, math.sin(a) * rad),
                            (math.cos(a) * rad * 0.7, s, math.sin(a) * rad * 0.7),
                            s * 0.62, segments=7, uv_per_m=0.5, taper=0.55))
    obj = M.finish(parts, "sh", bevel=0.0, weld=0.0, smooth_angle=48.0)
    M.paint(obj, "foliage")
    obj.name = "Minato_Flora_Shrub_LOD0"
    return obj


def rock_outcrop():
    r = M.rng(71)
    parts = []
    for i in range(16):
        a = r.uniform(0, math.tau)
        rad = float(r.uniform(0.0, 4.6))
        s = float(r.uniform(1.4, 4.4))
        parts.append(M.box(f"r{i}", (math.cos(a) * rad, s * 0.35, math.sin(a) * rad),
                           (s * 1.25, s, s * 1.1), uv_per_m=0.3))
    obj = M.finish(parts, "ro", bevel=0.09, smooth_angle=30.0)
    M.paint(obj, "rock")
    obj.name = "Minato_Road_RockOutcrop_LOD0"
    return obj


# ============================================================ export table

CATALOG = [
    # chapter 1 - port city departure
    (port_tower,                              "Minato_Port_Tower.fbx",          (0.30, 0.10)),
    (ferris_wheel,                            "Minato_Port_FerrisWheel.fbx",    (0.30, 0.10)),
    (container_crane,                         "Minato_Port_ContainerCrane.fbx", (0.35, 0.12)),
    (warehouse,                               "Minato_Port_Warehouse.fbx",      (0.30, 0.10)),
    (lambda: city_block("A", 22, 18, 34, 10), "Minato_City_BlockA.fbx",         (0.30, 0.10)),
    (lambda: city_block("B", 16, 16, 52, 15), "Minato_City_BlockB.fbx",         (0.30, 0.10)),
    (lambda: city_block("C", 30, 22, 22, 6),  "Minato_City_BlockC.fbx",         (0.30, 0.10)),
    (shophouse,                               "Minato_City_ShopHouse.fbx",      (0.35, 0.12)),
    (quay_kit,                                "Minato_Port_Bollard.fbx",        (0.40,)),
    (crate_stack,                             "Minato_Port_CrateStack.fbx",     (0.35,)),
    (street_lamp,                             "Minato_Port_StreetLamp.fbx",     (0.35,)),
    (palm,                                    "Minato_Flora_Palm.fbx",          (0.40, 0.15)),
    # chapter 3 - ocean
    (fishing_boat,                            "Minato_Sea_FishingBoat.fbx",     (0.35,)),
    (ferry,                                   "Minato_Sea_Ferry.fbx",           (0.30, 0.10)),
    (cargo_ship,                              "Minato_Sea_CargoShip.fbx",       (0.25, 0.08)),
    (sailboat,                                "Minato_Sea_Sailboat.fbx",        (0.40,)),
    (lighthouse,                              "Minato_Sea_Lighthouse.fbx",      (0.30, 0.10)),
    (lambda: islet("A", 46.0, 22.0, 81),      "Minato_Sea_IsletA.fbx",          (0.25, 0.08)),
    (lambda: islet("B", 88.0, 44.0, 83),      "Minato_Sea_IsletB.fbx",          (0.25, 0.08)),
    (sea_stack,                               "Minato_Sea_SeaStack.fbx",        (0.30, 0.10)),
    # chapter 4 - far shore settlement
    (lambda: villa("A", 12, 10, 7.0, 2, "terracotta"),
     "Minato_Shore_VillaA.fbx", (0.35, 0.12)),
    (lambda: villa("B", 9, 8, 5.4, 2, "roof_slate"),
     "Minato_Shore_VillaB.fbx", (0.35, 0.12)),
    (lambda: villa("C", 16, 11, 9.5, 3, "terracotta"),
     "Minato_Shore_VillaC.fbx", (0.35, 0.12)),
    # chapter 5 - mountain road furniture and flora
    (masonry_wall,                            "Minato_Road_MasonryWall.fbx",    (0.30, 0.10)),
    (guardrail_post,                          "Minato_Road_GuardrailPost.fbx",  (0.40,)),
    (rock_outcrop,                            "Minato_Road_RockOutcrop.fbx",    (0.30, 0.10)),
    (conifer,                                 "Minato_Flora_Conifer.fbx",       (0.35, 0.12)),
    (broadleaf,                               "Minato_Flora_Broadleaf.fbx",     (0.35, 0.12)),
    (shrub,                                   "Minato_Flora_Shrub.fbx",         (0.40,)),
]


def main():
    total = 0
    for builder, filename, ratios in CATALOG:
        S.reset_scene()
        obj = builder()
        M.stat(obj, filename.replace(".fbx", ""))
        exports = [obj]
        base = obj.name.replace("_LOD0", "")
        for i, r in enumerate(ratios):
            exports.append(M.decimated_copy(obj, f"{base}_LOD{i + 1}", r))
        M.export_fbx(exports, filename)
        M.save_blend(filename.replace(".fbx", ".blend"))
        obj.data.calc_loop_triangles()
        total += len(obj.data.loop_triangles)
    print(f"[minato] props complete: {len(CATALOG)} assets, {total:,} LOD0 tris")


if __name__ == "__main__":
    main()

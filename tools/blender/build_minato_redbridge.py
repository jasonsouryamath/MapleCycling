"""
MINATO COAST - red suspension bridge kit for the LAST STRETCH of the sea crossing (the landing
onto the far shore), matched to the title-screen art
(Assets/Environment/TitleScreen/Resources/MapleRideTitleSeaBridge.png): vermilion portal towers
with stacked cross-beams, main cables + vertical hangers, a red stiffening truss under a grey
deck, concrete caissons standing in the sea, and a concrete anchorage on the shore.

What lives HERE (discrete, LOD'd modules, all authored in Unity coords through M.u2b):
  * Minato_RedBridge_Tower      - one portal tower, origin = carriageway centre at deck level,
                                  +Z along the route. Legs run from TOWER_LEG_BOT (buried in
                                  the caisson) to TOWER_TOP; cable saddles on the crowns.
  * Minato_RedBridge_Caisson    - rounded concrete pier cap, origin = waterline (y 0), top +5.
  * Minato_RedBridge_Anchorage  - stepped concrete anchorage, origin = deck centreline level at
                                  the span-side face, +Z pointing TOWARDS the span. Two housings
                                  either side of the deck + a base under the girder soffit.

What does NOT live here: the main cables, hangers, stiffening truss, red railing and lamps are
swept along the real route by Assets/Editor/MinatoRedesign.RedBridge.cs (they must follow the
route's plan/profile exactly). SHARED NUMBERS: the C# Rb* constants mirror TOWER_*, CABLE_X,
CABLE_TOP_Y and ANCHOR_* below - change both together.

Run:  tools/blender-4.5.10-windows-x64/blender.exe -b --factory-startup -P tools/blender/build_minato_redbridge.py
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402
from build_minato_cablestay import loft  # noqa: E402  (rounded-section loft primitive)

# ------------------------------------------------------------------ shared dimensions (mirror C#)
TOWER_TOP = 74.0          # crown of the legs above deck level (RbTowerTopM)
TOWER_LEG_BOT = -64.0     # leg foot, buried inside the caisson
LEG_X_BOT = 11.0          # leg centre |x| at the foot
CABLE_X = 8.4             # cable plane |x| == leg centre at the crown (RbCableX)
CABLE_TOP_Y = TOWER_TOP + 2.2   # cable centre over the saddle (RbCableTopY)
SOFFIT_Y = -2.85          # white girder soffit (CsSoffitY)
TRUSS_BOT_Y = -7.6        # stiffening truss bottom chord (RbTrussBotY)
ANCHOR_CABLE = (CABLE_X, 4.6, -5.0)   # cable end inside the anchorage (RbAnchorCable*)

PALETTE_EXTRA = {
    # Title-screen vermilion ("international orange" leaning red). PROVISIONAL art tuning;
    # the Unity pass assigns its own cel material by slot name.
    "red_steel": (0.745, 0.180, 0.130, 1.0),
    "beacon": (0.98, 0.86, 0.55, 1.0),
    "concrete": (0.735, 0.720, 0.690, 1.0),
    "concrete_wet": (0.500, 0.500, 0.480, 1.0),
    "steel_dark": (0.230, 0.245, 0.260, 1.0),
}
M.PALETTE.update(PALETTE_EXTRA)


def leg_x(y):
    t = (y - TOWER_LEG_BOT) / (TOWER_TOP - TOWER_LEG_BOT)
    return LEG_X_BOT + (CABLE_X - LEG_X_BOT) * t


# Tier table: (y_from, y_to, half_x, half_z). Each tier steps back from the one below with a
# small chamfered ledge - the stepped Art-Deco silhouette of the title towers.
TIERS = [
    (TOWER_LEG_BOT, -9.0, 2.10, 3.30),
    (-9.0, 22.0, 1.85, 2.95),
    (22.0, 48.0, 1.62, 2.60),
    (48.0, TOWER_TOP, 1.40, 2.25),
]
STRUTS = [  # (centre y, depth) of the portal cross-beams
    (-12.2, 4.2),     # under-deck strut the truss bears on
    (19.5, 4.6),
    (45.5, 4.4),
    (TOWER_TOP - 2.6, 5.2),
]


def build_tower():
    red, dark, beacon = [], [], []
    for sx in (-1.0, 1.0):
        rings = []
        for k, (y0, y1, hx, hz) in enumerate(TIERS):
            if k == 0:
                rings.append((sx * leg_x(y0), y0, 0.0, hx, hz))
            else:
                # ledge: previous tier's size at y0, then chamfer in to this tier's size
                phx, phz = TIERS[k - 1][2], TIERS[k - 1][3]
                rings.append((sx * leg_x(y0), y0, 0.0, phx, phz))
                rings.append((sx * leg_x(y0 + 0.55), y0 + 0.55, 0.0, hx, hz))
            rings.append((sx * leg_x(y1 - 0.01), y1 - 0.01, 0.0, hx, hz))
        # crown cap
        rings.append((sx * CABLE_X, TOWER_TOP + 0.35, 0.0, 1.55, 2.4))
        rings.append((sx * CABLE_X, TOWER_TOP + 0.9, 0.0, 1.55, 2.4))
        red.append(loft(f"leg{int(sx)}", rings, n=24, p=9.0))

        # Vertical fluting: proud ribs on the front/back faces and the outer face of each tier,
        # the relief that makes a 140 m red leg read as riveted plate rather than a flat prism.
        for k, (y0, y1, hx, hz) in enumerate(TIERS[1:], start=1):
            a, b = y0 + 1.4, y1 - 1.2
            for fz in (-1.0, 1.0):
                for fx in (-0.55, 0.0, 0.55):
                    xa = sx * leg_x(a) + fx * hx
                    xb = sx * leg_x(b) + fx * hx
                    red.append(M.beam(f"flute{int(sx)}_{k}_{fz}_{fx}",
                                      (xa, a, fz * (hz + 0.10)), (xb, b, fz * (hz + 0.10)),
                                      0.34, 0.24, up_hint=(0, 0, 1), uv_per_m=0.3))
            for fz in (-0.5, 0.5):
                xa = sx * (leg_x(a) + hx + 0.10)
                xb = sx * (leg_x(b) + hx + 0.10)
                red.append(M.beam(f"fluteO{int(sx)}_{k}_{fz}", (xa, a, fz * hz), (xb, b, fz * hz),
                                  0.24, 0.34, up_hint=(0, 0, 1), uv_per_m=0.3))

        # Cable saddle housing on the crown: a plated box with a rounded roof along the cable.
        sy = TOWER_TOP + 0.9
        red.append(M.box(f"saddle{int(sx)}", (sx * CABLE_X, sy + 1.1, 0.0), (2.6, 2.2, 6.2)))
        roof = [(math.cos(a) * 1.3, math.sin(a) * 1.1)
                for a in [i / 10.0 * math.pi for i in range(11)]]
        red.append(M.prism(f"roof{int(sx)}", roof, (sx * CABLE_X, sy + 2.2, -3.1),
                           (sx * CABLE_X, sy + 2.2, 3.1), up_hint=(0, 1, 0), uv_per_m=0.3))
        dark.append(M.box(f"cable_clamp{int(sx)}", (sx * CABLE_X, CABLE_TOP_Y, 0.0), (1.3, 1.3, 7.2)))
        beacon.append(M.box(f"beacon{int(sx)}", (sx * CABLE_X, sy + 3.55, 0.0), (0.5, 0.7, 0.5),
                            uv_per_m=1.0))

    # Portal struts: deep plated beams with a framed front panel and curved knee braces.
    for si, (cy, depth) in enumerate(STRUTS):
        hx_here = next((t[2] for t in TIERS if t[0] <= cy <= t[1]), TIERS[-1][2])
        xi = leg_x(cy) - hx_here + 0.2
        red.append(M.beam(f"strut{si}", (-xi, cy, 0.0), (xi, cy, 0.0), 2.3, depth,
                          up_hint=(0, 1, 0), uv_per_m=0.3))
        for fz in (-1.0, 1.0):   # framed panel: top + bottom lips proud of the web
            for fy in (-1.0, 1.0):
                red.append(M.beam(f"lip{si}_{fz}_{fy}", (-xi, cy + fy * (depth * 0.5 - 0.3), fz * 1.25),
                                  (xi, cy + fy * (depth * 0.5 - 0.3), fz * 1.25), 0.25, 0.55,
                                  up_hint=(0, 1, 0), uv_per_m=0.3))
            # vertical stiffeners splitting the panel into bays
            for bx in (-0.5, 0.0, 0.5):
                red.append(M.beam(f"stf{si}_{fz}_{bx}", (bx * xi, cy - depth * 0.5 + 0.3, fz * 1.22),
                                  (bx * xi, cy + depth * 0.5 - 0.3, fz * 1.22), 0.4, 0.2,
                                  up_hint=(0, 0, 1), uv_per_m=0.3))
        if si > 0:
            # curved knee braces (quarter arcs) under the strut into each leg
            for sx in (-1.0, 1.0):
                pts = []
                for i in range(6):
                    a = i / 5.0 * (math.pi * 0.5)
                    r = 3.2
                    pts.append((sx * (xi - r + r * math.cos(a)) if False else
                                sx * (xi - r * (1.0 - math.cos(a))),
                                cy - depth * 0.5 - r * (1.0 - math.sin(a)) + 0.02, 0.0))
                red += M.polyline_beams(f"knee{si}_{int(sx)}", pts, 1.6, 0.9, up_hint=(0, 0, 1))
    # Truss bearings on the under-deck strut (the stiffening truss sits on them).
    for sx in (-1.0, 1.0):
        dark.append(M.box(f"brg{int(sx)}", (sx * 7.4, STRUTS[0][0] + STRUTS[0][1] * 0.5 + 0.35, 0.0),
                          (1.4, 0.7, 2.0), uv_per_m=1.0))
        # vertical bearing post from the strut up to the truss bottom chord
        dark.append(M.box(f"post{int(sx)}", (sx * 7.4, (TRUSS_BOT_Y + STRUTS[0][0] + STRUTS[0][1] * 0.5 + 0.7) * 0.5, 0.0),
                          (0.8, TRUSS_BOT_Y - (STRUTS[0][0] + STRUTS[0][1] * 0.5 + 0.7), 0.8), uv_per_m=1.0))

    r = M.finish(red, "red", bevel=0.06, bevel_segments=1, smooth_angle=38.0)
    d = M.finish(dark, "dark", bevel=0.03, smooth_angle=38.0)
    b = M.finish(beacon, "beacon", bevel=0.02, smooth_angle=38.0)
    M.paint(r, "red_steel")
    M.paint(d, "steel_dark")
    M.paint(b, "beacon")
    return S.join([r, d, b], "Minato_RedBridge_Tower_LOD0")


def build_caisson():
    """Rounded concrete caisson standing out of the sea; origin = waterline, top at +5."""
    body = loft("caisson", [
        (0.0, 5.0, 0.0, 15.2, 6.6),
        (0.0, 4.4, 0.0, 15.8, 7.2),
        (0.0, 1.6, 0.0, 15.8, 7.2),
        (0.0, 1.2, 0.0, 16.3, 7.7),      # cutwater/fender band starts
        (0.0, -40.0, 0.0, 16.3, 7.7),
    ], n=32, p=3.0)
    # pointed cutwater noses on both ends (against the tide), along the route axis
    noses = []
    for sz in (-1.0, 1.0):
        tri = [(-4.8, 0.0), (4.8, 0.0), (0.0, 4.8 * 1.0)]
        noses.append(M.prism(f"nose{int(sz)}", [(x, z) for (x, z) in tri],
                             (0.0, -40.0, sz * 7.2), (0.0, 1.6, sz * 7.2),
                             up_hint=(0, 0, sz), uv_per_m=0.3))
    c = M.finish([body] + noses, "concrete", bevel=0.05, smooth_angle=40.0)
    M.paint(c, "concrete")
    band = loft("tideband", [
        (0.0, 1.2, 0.0, 16.36, 7.76),
        (0.0, -1.5, 0.0, 16.36, 7.76),
    ], n=32, p=3.0, cap=False)
    wb = M.finish([band], "wet", bevel=0.0, smooth_angle=40.0)
    M.paint(wb, "concrete_wet")
    obj = S.join([c, wb], "Minato_RedBridge_Caisson_LOD0")
    return obj


def build_anchorage():
    """
    Stepped concrete anchorage. +Z faces the span; origin = deck centreline level at the face.
    Two housings (|x| 7.3 .. 16.5) flank the girder; a base under the soffit carries it.
    """
    con = []
    xi, xo = 7.3, 16.5
    for sx in (-1.0, 1.0):
        cx = sx * (xi + xo) * 0.5
        w = xo - xi
        # main mass, sloped span-side face the cable dives into
        sec = [(-38.0, -60.0), (0.0, -60.0), (0.0, 1.5), (-9.0, 8.5), (-30.0, 8.5), (-38.0, 4.0)]
        # prism section is (right, up) in the member frame; run it laterally across the housing
        con.append(M.prism(f"house{int(sx)}", [(z, y) for (z, y) in sec],
                           (cx - w * 0.5, 0.0, 0.0), (cx + w * 0.5, 0.0, 0.0),
                           up_hint=(0, 1, 0), uv_per_m=0.25))
        # crown block + pilasters (Art-Deco relief on the outer face)
        con.append(M.box(f"crown{int(sx)}", (cx, 10.0, -19.5), (w - 1.6, 3.0, 18.0)))
        for k in range(5):
            z = -32.0 + k * 6.5
            con.append(M.box(f"pil{int(sx)}_{k}", (sx * (xo + 0.25), -4.0, z), (0.5, 22.0, 1.4)))
        # cable splay collar where the cable enters the slope
        con.append(M.tube(f"collar{int(sx)}", (sx * ANCHOR_CABLE[0], 3.2, -3.4),
                          (sx * ANCHOR_CABLE[0], 5.9, -7.8), 1.25, segments=14))
    # base under the girder soffit, joining the housings
    con.append(M.box("base", (0.0, (SOFFIT_Y - 0.15 - 60.0) * 0.5, -19.0),
                     (xi * 2.0 + 0.2, SOFFIT_Y - 0.15 + 60.0, 38.0)))
    c = M.finish(con, "concrete", bevel=0.08, smooth_angle=40.0)
    M.paint(c, "concrete")
    c.name = "Minato_RedBridge_Anchorage_LOD0"
    return c


KIT = [
    (build_tower, "Minato_RedBridge_Tower.fbx", (0.45, 0.16)),
    (build_caisson, "Minato_RedBridge_Caisson.fbx", (0.5, 0.2)),
    (build_anchorage, "Minato_RedBridge_Anchorage.fbx", (0.5, 0.2)),
]


def main():
    for builder, filename, ratios in KIT:
        S.reset_scene()
        obj = builder()
        M.stat(obj, filename.replace(".fbx", ""))
        exports = [obj]
        base = obj.name.replace("_LOD0", "")
        for i, r in enumerate(ratios):
            exports.append(M.decimated_copy(obj, f"{base}_LOD{i + 1}", r))
        M.export_fbx(exports, filename)


if __name__ == "__main__":
    main()

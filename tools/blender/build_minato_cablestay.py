"""
MINATO COAST - white cable-stayed signature bridge + low-profile viaduct kit (BRIDGE workstream).

Replaces the coral arch kit (build_minato_bridge.py) visually, per MINATO_VISUAL_DESIGN.md
zones 5-6 and reference image 8: two tall white H-pylons straddling the deck, semi-fan stays
to the deck edges, and a sleek white box-girder viaduct on slender tulip piers for the rest of
the 8 km crossing.

What lives HERE (discrete, instanced, LOD'd modules):
  * Minato_Bridge_Pylon            - one whole H-pylon, origin = carriageway centre at deck level
  * Minato_Bridge_ViaductPierHead  - tulip pier head, top = girder soffit (y = SOFFIT_Y)
  * Minato_Bridge_ViaductPierShaft - 10 m slender column (y 0 -> -10), scaled in Y by Unity
  * Minato_Bridge_ViaductPierFooting - pile cap, origin = waterline / ground, top at +2.5 m

What does NOT live here: the deck girder, footway, parapet, handrails and the stay cables are
swept/generated along the real route by MinatoRedesign.Bridge.cs, because they must follow the
route's plan and profile curves exactly (a tiled straight module cannot be seam-free on the
9 km radius curve and the 2.6 % ramp).

SHARED NUMBERS - the C# constants Cs* in MinatoRedesign.Bridge.cs mirror LEG_*, STAY_* and
SOFFIT_Y below. Change both together.

Run:  tools/blender-4.5.10-windows-x64/blender.exe -b --factory-startup -P tools/blender/build_minato_cablestay.py
      (add `-- preview <outdir>` to also render a look-dev assembly)
"""

import math
import os
import sys

import bpy
from mathutils import Vector, Euler

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402

# ------------------------------------------------------------------ shared dimensions
SOFFIT_Y = -2.85          # underside of the box girder's bottom flange (deck-local)
GIRDER_HALF_BOTTOM = 3.4  # half width of the bottom flange

# Pylon leg centreline: straight lower leg from the footing to the deck crossbeam, straight
# upper leg from there to the crown. Legs lean inward so the pair reads as a tapering H.
LEG_FOOT = (15.0, -45.0)    # (x, y) at the footing top
LEG_BASE = (10.8, -5.35)    # (x, y) at the lower crossbeam (under the deck)
LEG_TOP = (8.6, 100.0)      # (x, y) at the top of the shaft, crown above
STAY_TOP_LOW, STAY_TOP_HIGH = 70.0, 98.0   # stay anchorage band on the legs
UPPER_BEAM_Y = 58.0
FOOTING_TOP_Y = -45.0

PALETTE_EXTRA = {
    "bridge_white": (0.93, 0.94, 0.94, 1.0),
    "beacon": (0.95, 0.30, 0.22, 1.0),
}
M.PALETTE.update(PALETTE_EXTRA)


def leg_x(y):
    """Leg centre x (positive side) at deck-local height y."""
    if y <= LEG_BASE[1]:
        t = (y - LEG_FOOT[1]) / (LEG_BASE[1] - LEG_FOOT[1])
        return LEG_FOOT[0] + (LEG_BASE[0] - LEG_FOOT[0]) * t
    t = (y - LEG_BASE[1]) / (LEG_TOP[1] - LEG_BASE[1])
    return LEG_BASE[0] + (LEG_TOP[0] - LEG_BASE[0]) * t


# ------------------------------------------------------------------ loft primitive

def _ring(cx, y, cz, hx, hz, n, p):
    """Superellipse ring (rounded rectangle for p ~ 4, ellipse for p = 2) in Unity coords."""
    pts = []
    for i in range(n):
        a = (i + 0.5) / n * math.tau
        c, s = math.cos(a), math.sin(a)
        x = cx + hx * math.copysign(abs(c) ** (2.0 / p), c)
        z = cz + hz * math.copysign(abs(s) ** (2.0 / p), s)
        pts.append((x, y, z))
    return pts


def loft(name, rings, n=16, p=4.0, cap=True):
    """
    Loft a vertical-ish member through ``rings`` = [(cx, y, cz, hx, hz), ...] (Unity coords).
    Used for the pylon legs, pier heads, columns and footings: every one of them is a rounded
    section whose size and centre change with height, which a single tapered prism cannot do.
    """
    verts, faces, uvs = [], [], []
    for (cx, y, cz, hx, hz) in rings:
        verts += _ring(cx, y, cz, hx, hz, n, p)
    for r in range(len(rings) - 1):
        y0, y1 = rings[r][1], rings[r + 1][1]
        per = 2.0 * (rings[r][3] + rings[r][4]) * 1.1
        for i in range(n):
            i1 = (i + 1) % n
            a, b = r * n + i, r * n + i1
            c, d = (r + 1) * n + i1, (r + 1) * n + i
            faces.append((a, b, c, d))
            u0, u1 = i / n * per * 0.25, (i + 1) / n * per * 0.25
            uvs += [(u0, y0 * 0.25), (u1, y0 * 0.25), (u1, y1 * 0.25), (u0, y1 * 0.25)]
    obj = S.mesh_from_arrays(name, [M.u2b(*v) for v in verts], faces, uvs=uvs, smooth=True)
    if cap:
        S.cap_open_boundaries(obj)
    S.recalc_normals(obj)
    return obj


# ------------------------------------------------------------------ pylon

def build_pylon():
    white, grey, red = [], [], []
    for sx in (-1.0, 1.0):
        rings = [
            (sx * LEG_FOOT[0], LEG_FOOT[1], 0.0, 2.05, 2.9),
            (sx * leg_x(-25.0), -25.0, 0.0, 1.95, 2.75),
            (sx * LEG_BASE[0], LEG_BASE[1], 0.0, 1.85, 2.55),
            (sx * leg_x(30.0), 30.0, 0.0, 1.55, 2.2),
            (sx * leg_x(UPPER_BEAM_Y), UPPER_BEAM_Y, 0.0, 1.38, 1.95),
            (sx * LEG_TOP[0], LEG_TOP[1], 0.0, 1.10, 1.60),
            # Crown: a short chamfered cap tapering to a blunt point - the silhouette that
            # identifies the pylon from the harbour 3+ km away.
            (sx * LEG_TOP[0], LEG_TOP[1] + 3.2, 0.0, 0.92, 1.35),
            (sx * LEG_TOP[0], LEG_TOP[1] + 5.4, 0.0, 0.34, 0.55),
        ]
        white.append(loft(f"leg{int(sx)}", rings, n=20, p=4.2))
        # Recessed shadow groove on the outer face of the upper leg: one cheap strip that keeps
        # the 100 m white shaft from reading as a flat extrusion under the cel ramp.
        g0, g1 = 8.0, LEG_TOP[1] - 2.0
        white.append(M.beam(f"rib{int(sx)}",
                            (sx * (leg_x(g0) + 1.52), g0, 0.0),
                            (sx * (leg_x(g1) + 1.08), g1, 0.0),
                            0.22, 0.9, up_hint=(0, 0, 1), uv_per_m=0.3))
        # Aviation beacon on each crown - a coral accent from the palette, visible as a dot.
        red.append(M.box(f"beacon{int(sx)}", (sx * LEG_TOP[0], LEG_TOP[1] + 5.8, 0.0),
                         (0.45, 0.8, 0.45), uv_per_m=1.0))

    # Upper crossbeam - the "H" bar. Slightly arched soffit via a 3-segment underside.
    yb = UPPER_BEAM_Y
    xi = leg_x(yb) - 0.6
    white.append(M.beam("beamU", (-xi, yb + 1.0, 0.0), (xi, yb + 1.0, 0.0), 2.3, 3.6,
                        up_hint=(0, 1, 0), uv_per_m=0.3))
    for sx in (-1.0, 1.0):
        white.append(M.beam(f"hunch{int(sx)}", (sx * xi, yb - 1.9, 0.0),
                            (sx * (xi - 2.6), yb - 0.6, 0.0), 1.9, 1.4,
                            up_hint=(0, 0, 1), uv_per_m=0.3))
    # Lower crossbeam that carries the girder (its top = girder soffit).
    xl = LEG_BASE[0] - 0.4
    white.append(M.beam("beamL", (-xl, SOFFIT_Y - 1.35, 0.0), (xl, SOFFIT_Y - 1.35, 0.0),
                        3.6, 2.7, up_hint=(0, 1, 0), uv_per_m=0.3))
    # Bearing plinths on the crossbeam.
    for sx in (-1.0, 1.0):
        grey.append(M.box(f"brg{int(sx)}", (sx * 2.4, SOFFIT_Y + 0.02, 0.0),
                          (1.2, 0.25, 1.4), uv_per_m=1.0))

    # Footing: a big rounded pile cap standing out of the sea.
    grey.append(loft("footing", [
        (0.0, FOOTING_TOP_Y, 0.0, 19.0, 6.4),
        (0.0, FOOTING_TOP_Y - 0.6, 0.0, 19.6, 7.0),
        (0.0, FOOTING_TOP_Y - 13.0, 0.0, 19.6, 7.0),
    ], n=28, p=3.2))

    w = M.finish(white, "white", bevel=0.05, bevel_segments=1, smooth_angle=38.0)
    g = M.finish(grey, "grey", bevel=0.05, bevel_segments=1, smooth_angle=38.0)
    r = M.finish(red, "red", bevel=0.02, smooth_angle=38.0)
    M.paint(w, "bridge_white")
    M.paint(g, "concrete")
    M.paint(r, "beacon")
    return S.join([w, g, r], "Minato_Bridge_Pylon_LOD0")


# ------------------------------------------------------------------ viaduct pier

def build_pier_head():
    """
    Portal-bent cap beam (the StormglassCauseway concept's twin-column piers): a chamfered
    white cross-head whose top is the girder soffit, sized to the two columns below it.
    """
    y0 = SOFFIT_Y
    parts = []
    # Chamfered cap: section in (lateral, up) swept along the route axis.
    sec = [(-4.9, -1.2), (-4.3, -2.3), (4.3, -2.3), (4.9, -1.2), (4.9, 0.0), (-4.9, 0.0)]
    parts.append(M.prism("cap", [(x, y) for (x, y) in sec], (0.0, y0, -1.1), (0.0, y0, 1.1),
                         up_hint=(0, 1, 0), uv_per_m=0.3))
    for sx in (-1.0, 1.0):
        parts.append(M.box(f"brg{int(sx)}", (sx * 2.2, y0 + 0.06, 0.0), (0.9, 0.12, 1.0),
                           uv_per_m=1.0))
    obj = M.finish(parts, "head", bevel=0.04, smooth_angle=40.0)
    M.paint(obj, "bridge_white")
    obj.name = "Minato_Bridge_ViaductPierHead_LOD0"
    return obj


PIER_COL_X = 3.0          # column centres either side of the centreline
PIER_HEAD_DEPTH = 2.3     # cap beam depth below the soffit (C#: CsPierHeadDepthM)


def build_pier_shaft():
    """Twin rounded-rectangle columns, 10 m long (y 0 -> -10), scaled to height in Unity."""
    cols = []
    for sx in (-1.0, 1.0):
        cols.append(loft(f"col{int(sx)}", [(sx * PIER_COL_X, 0.0, 0.0, 0.85, 0.85),
                                          (sx * PIER_COL_X, -10.0, 0.0, 0.85, 0.85)],
                         n=16, p=4.5))
    obj = M.finish(cols, "shaft", bevel=0.0, smooth_angle=40.0)
    M.paint(obj, "bridge_white")
    obj.name = "Minato_Bridge_ViaductPierShaft_LOD0"
    return obj


def build_pier_footing():
    obj = loft("footing", [
        (0.0, 2.5, 0.0, 5.4, 2.1),
        (0.0, 2.1, 0.0, 5.8, 2.5),
        (0.0, -5.0, 0.0, 5.8, 2.5),
    ], n=24, p=3.4)
    obj = M.finish([obj], "footing", bevel=0.04, smooth_angle=40.0)
    M.paint(obj, "concrete")
    obj.name = "Minato_Bridge_ViaductPierFooting_LOD0"
    return obj


LODS = (0.5, 0.22, 0.08)
KIT = [
    (build_pylon, "Minato_Bridge_Pylon.fbx", LODS),
    (build_pier_head, "Minato_Bridge_ViaductPierHead.fbx", (0.5, 0.2)),
    (build_pier_shaft, "Minato_Bridge_ViaductPierShaft.fbx", (0.5,)),
    (build_pier_footing, "Minato_Bridge_ViaductPierFooting.fbx", (0.5, 0.2)),
]


# ------------------------------------------------------------------ look-dev preview

def girder_profile():
    r = [(3.4, -2.85), (5.9, -1.45), (7.05, -0.5), (6.55, -0.2), (6.40, 0.62), (6.0, 0.62),
         (6.0, 0.12), (4.5, 0.12), (4.5, -0.30), (0.0, -0.30)]
    left = [(-x, y) for (x, y) in reversed(r)]
    return left + r


def preview(outdir):
    """Assemble the exported FBX modules + a straight girder, render to ``outdir``."""
    S.reset_scene()
    objs = {}
    for _, fn, _ in KIT:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(M.models_dir(), fn))
        for o in [o for o in bpy.data.objects if o not in before]:
            if o.name.endswith("_LOD0"):
                S.select_only(o)
                bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
                objs[fn] = o
            else:
                bpy.data.objects.remove(o, do_unlink=True)
    deck_y = 49.0

    def place(src, pos, sy=1.0):
        c = src.copy()
        S.link(c)
        c.location = Vector(M.u2b(*pos))
        c.scale = (1.0, 1.0, sy)     # Unity Y == Blender Z
        return c

    pyl = objs["Minato_Bridge_Pylon.fbx"]
    pyl.location = Vector(M.u2b(0.0, deck_y, 0.0))
    pyl2 = place(pyl, (0.0, deck_y, 607.5))
    for z in [-405.0 + 67.5 * k for k in range(-3, 0)] + [1215.0 + 67.5 * k for k in range(0, 3)]:
        top = deck_y + SOFFIT_Y
        place(objs["Minato_Bridge_ViaductPierHead.fbx"], (0.0, deck_y, z))
        shaft_top = top - PIER_HEAD_DEPTH
        L = shaft_top - 2.5
        place(objs["Minato_Bridge_ViaductPierShaft.fbx"], (0.0, shaft_top, z), L / 10.0)
        place(objs["Minato_Bridge_ViaductPierFooting.fbx"], (0.0, 0.0, z))
    for k in ("Minato_Bridge_ViaductPierHead.fbx", "Minato_Bridge_ViaductPierShaft.fbx",
              "Minato_Bridge_ViaductPierFooting.fbx"):
        objs[k].hide_render = True

    prof = girder_profile()
    g = M.prism("girder", prof, (0.0, deck_y, -700.0), (0.0, deck_y, 1500.0),
                up_hint=(0, 1, 0))
    M.paint(g, "bridge_white")
    road = M.box("road", (0.0, deck_y - 0.1, 400.0), (9.0, 0.2, 2200.0))
    M.paint(road, "asphalt")

    # stays, semi-fan, exactly as the Unity pass computes them
    for zp in (0.0, 607.5):
        for sx in (-1.0, 1.0):
            for dz in (-1.0, 1.0):
                for k in range(13):
                    h = STAY_TOP_LOW + (STAY_TOP_HIGH - STAY_TOP_LOW) * k / 12.0
                    a = (sx * leg_x(h), deck_y + h, zp + dz * 0.9)
                    b = (sx * 6.95, deck_y - 0.3, zp + dz * (24.0 + 21.5 * k))
                    t = M.tube(f"st{k}", a, b, 0.16, segments=6)
                    M.paint(t, "steel_grey")

    sea = M.box("sea", (0.0, -0.5, 400.0), (6000.0, 1.0, 8000.0))
    M.paint(sea, "hull_blue")

    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x, scn.render.resolution_y = 1280, 720
    scn.world = bpy.data.worlds.new("W")
    scn.world.use_nodes = True
    bg = scn.world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.68, 0.85, 1.0)
    bg.inputs[1].default_value = 1.6
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    sun.data.energy = 4.0
    sun.rotation_euler = Euler((math.radians(62.0), 0.0, math.radians(-130.0)))
    S.link(sun)
    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    S.link(cam)
    scn.camera = cam
    os.makedirs(outdir, exist_ok=True)

    def shot(tag, eye, look, fov):
        cam.location = Vector(M.u2b(*eye))
        cam_data.angle = math.radians(fov)
        cam_data.clip_end = 20000.0
        d = Vector(M.u2b(*look)) - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(outdir, f"cablestay_{tag}.png")
        bpy.ops.render.render(write_still=True)

    shot("deck", (1.0, deck_y + 2.4, -120.0), (0.0, deck_y + 20.0, 300.0), 62.0)
    shot("side", (-900.0, 90.0, 400.0), (0.0, 60.0, 400.0), 55.0)
    shot("approach", (0.0, 30.0, -1400.0), (0.0, 70.0, 0.0), 50.0)
    shot("under", (-60.0, 8.0, -300.0), (0.0, 40.0, -150.0), 60.0)


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
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if argv and argv[0] == "preview":
        preview(argv[1])


if __name__ == "__main__":
    main()

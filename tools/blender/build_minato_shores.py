"""
MINATO COAST - Shores workstream prop families (Seawall Sprint, Marina Ribbon, Stormglass
Causeway, Beyond the Horizon).

Targets: design_assets/concepts/MinatoCoast_SeawallSprint_Target_v01.png,
MinatoCoast_MarinaRibbon_Target_v01.png, MinatoCoast_StormglassCauseway_Target_v01.png,
MinatoCoast_BeyondTheHorizon_Target_v01.png.

Every family is a REUSABLE module: the Unity pass (Assets/Editor/MinatoRedesign.Shores.cs) places
them data-driven from route-metre ranges, so zones can be re-mapped without re-authoring.

Conventions (same as build_minato_props.py / minato_lib.py):
  * Authored in UNITY coordinates through minato_lib primitives (+X right, +Y up, +Z forward),
    exported with the measured clean-root FBX settings (identity import).
  * Origin at the ground/deck contact point.  Linear modules (seawall, rope, pontoon) run along
    +Z and are centred on z = 0 unless noted, so Unity can place them with LookRotation(tangent).
  * Material SLOT NAMES carry the surface: "Minato_Shores_shw_<slot>".  The Unity pass keys its own
    cel materials off the "shw_<slot>" token (RetintBySlot), so Blender colours are preview only.
  * Repeated props get decimated _LOD1/_LOD2 siblings; ground cover ships LOD0 only (it is GPU
    instanced and a LOD ladder on a 60-tri tuft saves nothing).

Palette: white, navy, turquoise water, coral accents, golden light, coastal greens - no pink.

Run:  blender -b --factory-startup -P tools/blender/build_minato_shores.py [-- name_filter]
"""

import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402

u2b = M.u2b

# ============================================================ palette (preview colours only)

SHW = {
    "white":     (0.930, 0.930, 0.900, 1.0),
    "trim":      (0.860, 0.850, 0.800, 1.0),
    "navy":      (0.100, 0.170, 0.320, 1.0),
    "blue":      (0.140, 0.380, 0.720, 1.0),
    "coral":     (0.950, 0.420, 0.300, 1.0),
    "towerpaint": (0.800, 0.140, 0.120, 1.0),   # red OR green harbour light, chosen in Unity
    "glass":     (0.200, 0.380, 0.460, 1.0),
    "metal":     (0.780, 0.800, 0.820, 1.0),     # brushed stainless / aluminium
    "darkmetal": (0.180, 0.190, 0.210, 1.0),
    "concrete":  (0.800, 0.790, 0.760, 1.0),
    "tetra":     (0.820, 0.810, 0.780, 1.0),
    "rock":      (0.550, 0.520, 0.480, 1.0),
    "teak":      (0.600, 0.440, 0.300, 1.0),
    "rope":      (0.900, 0.860, 0.760, 1.0),
    "scrub":     (0.300, 0.460, 0.260, 1.0),
    "grass":     (0.520, 0.580, 0.320, 1.0),
    "plume":     (0.930, 0.870, 0.720, 1.0),
    "foam":      (0.950, 0.970, 0.980, 1.0),
    "hazard":    (0.950, 0.750, 0.100, 1.0),
    "lamp":      (1.000, 0.920, 0.700, 1.0),
    "soil":      (0.300, 0.230, 0.170, 1.0),
    "canvas":    (0.960, 0.950, 0.920, 1.0),
    "rubber":    (0.080, 0.080, 0.090, 1.0),
}


def smat(slot):
    name = f"Minato_Shores_shw_{slot}"
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    return S.pbr_material(name, base_color=SHW[slot],
                          roughness=0.25 if slot in ("metal", "glass") else 0.75,
                          metallic=0.8 if slot == "metal" else 0.0)


def paint(objs, slot):
    if not isinstance(objs, (list, tuple)):
        objs = [objs]
    m = smat(slot)
    for o in objs:
        if o is not None:
            S.assign_material(o, m)
    return objs


def fin(parts, slot, name=None, bevel=0.0, smooth=None, weld=0.0008):
    """Join + finish a group of parts under ONE material slot."""
    parts = [p for p in parts if p is not None]
    if not parts:
        return None
    o = M.finish(parts, name or f"g_{slot}", bevel=bevel, smooth_angle=smooth, weld=weld)
    paint(o, slot)
    return o


def asset(groups, name):
    """Join finished slot groups into the shippable LOD0 object."""
    return S.join([g for g in groups if g is not None], f"{name}_LOD0")


# ============================================================ low-level helpers

def blob(name, centre, radii, seed, subdiv=1, jitter=0.18, flatten_base=True):
    """
    Irregular rock/shrub lump: an icosphere in UNITY space, scaled by ``radii`` and jittered
    per vertex.  Flat-shaded faceting is the look (cel shader), so no smoothing.
    """
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    r = np.random.default_rng(seed)
    verts = []
    for v in bm.verts:
        # icosphere is built in Blender space; treat its (x, y, z) as UNITY (x, z, y) so that the
        # radii tuple is (x, up, z) in Unity terms.
        ux, uy, uz = v.co.x, v.co.z, v.co.y
        k = 1.0 + r.uniform(-jitter, jitter)
        p = (centre[0] + ux * radii[0] * k,
             centre[1] + uy * radii[1] * k,
             centre[2] + uz * radii[2] * k)
        if flatten_base and p[1] < centre[1] - radii[1] * 0.35:
            p = (p[0], centre[1] - radii[1] * 0.35, p[2])
        verts.append(p)
    faces = [tuple(vv.index for vv in f.verts) for f in bm.faces]
    bm.free()
    obj = S.mesh_from_arrays(name, [u2b(*v) for v in verts], faces, smooth=False)
    S.recalc_normals(obj)
    return obj


def cyl(name, a, b, r, seg=10, taper=1.0, cap=True):
    return M.tube(name, a, b, r, segments=seg, taper=taper, cap=cap)


def blade_strip(name, base, direction, height, width, bend, segs=3):
    """
    A single grass blade as a tapered strip that arches along ``direction`` (unity xz).
    Solidified later so it survives back-face culling.
    """
    d = Vector((direction[0], 0.0, direction[1]))
    if d.length < 1e-6:
        d = Vector((1.0, 0.0, 0.0))
    d.normalize()
    side = Vector((-d.z, 0.0, d.x))
    verts = []
    for s in range(segs + 1):
        t = s / segs
        w = width * (1.0 - t * 0.92)
        c = Vector(base) + Vector((0.0, height * t, 0.0)) + d * (bend * t * t)
        verts.append(tuple(c - side * w * 0.5))
        verts.append(tuple(c + side * w * 0.5))
    faces = []
    for s in range(segs):
        a = s * 2
        faces.append((a, a + 1, a + 3, a + 2))
    return S.mesh_from_arrays(name, [u2b(*v) for v in verts], faces, smooth=False)


def solidify(obj, t=0.02):
    S.add_solidify(obj, thickness=t, offset=0.0)
    S.apply_modifiers(obj)
    return obj


def catenary(name, a, b, sag, r, n=8, seg=5):
    """Rope between two unity points with a parabolic sag."""
    a = Vector(a)
    b = Vector(b)
    pts = []
    for i in range(n + 1):
        t = i / n
        p = a.lerp(b, t)
        p.y -= sag * 4.0 * t * (1.0 - t)
        pts.append(p)
    parts = []
    for i in range(n):
        parts.append(M.tube(f"{name}_{i}", pts[i], pts[i + 1], r, segments=seg, cap=False))
    return parts


def hull_loft(name, L, B, D, sheer=0.35, n_st=12, n_sec=9, freeboard=1.0, transom=0.62):
    """
    Yacht hull lofted from U-shaped cross sections: fine entry at the bow, full midship,
    transom stern.  Returns (hull object, deck outline points per station) in UNITY space;
    +Z = bow, deck edge at y = freeboard (+ sheer rise toward the bow).
    """
    verts = []
    faces = []
    deck = []
    for i in range(n_st + 1):
        t = i / n_st                       # 0 = stern, 1 = bow
        z = -L * 0.5 + t * L
        # half-breadth: transom width at stern, max near 45 %, point at bow
        hb = B * 0.5 * (transom + (1.0 - transom) * math.sin(min(t / 0.45, 1.0) * math.pi * 0.5))
        if t > 0.45:
            hb = B * 0.5 * math.cos((t - 0.45) / 0.55 * math.pi * 0.5) ** 0.85
        hb = max(hb, 0.03)
        depth = D * (0.55 + 0.45 * math.sin(math.pi * min(max(t, 0.05), 0.95)))
        top = freeboard + sheer * (t ** 2) * 1.2
        ring = []
        for k in range(n_sec + 1):
            a = k / n_sec                  # 0 = port deck edge .. 1 = starboard deck edge
            ang = math.pi * a
            x = -math.cos(ang) * hb
            y = top - math.sin(ang) ** 0.7 * (top + depth)
            ring.append((x, y, z))
        deck.append((ring[0], ring[-1]))
        verts.extend(ring)
    cols = n_sec + 1
    for i in range(n_st):
        for k in range(n_sec):
            a = i * cols + k
            faces.append((a, a + 1, a + cols + 1, a + cols))
    obj = S.mesh_from_arrays(name, [u2b(*v) for v in verts], faces, smooth=False)
    S.cap_open_boundaries(obj)
    S.recalc_normals(obj)
    return obj, deck


def deck_plate(name, deck, inset=0.05, lift=0.0):
    """Closed deck surface following the hull's deck edge."""
    verts = []
    for (p, s) in deck:
        verts.append((p[0] + inset, p[1] + lift, p[2]))
        verts.append((s[0] - inset, s[1] + lift, s[2]))
    faces = []
    for i in range(len(deck) - 1):
        a = i * 2
        faces.append((a, a + 2, a + 3, a + 1))
    obj = S.mesh_from_arrays(name, [u2b(*v) for v in verts], faces, smooth=False)
    solidify(obj, 0.06)
    S.recalc_normals(obj)
    return obj


# ============================================================ SEAWALL SPRINT / CAUSEWAY

def seawall_rail():
    """
    6 m seawall parapet + brushed-metal railing (Seawall Sprint + Causeway targets).
    Concrete parapet 0.75 m with a seaward wave-return lip (-X is SEAWARD), a face that drops
    3.5 m below the origin so it reads as a wall on an embankment edge, and a 3-rail stainless
    handrail on square posts with base plates.  Origin: parapet base, landward face, y = 0.
    Module runs z -3..+3.
    """
    L = 6.0
    con, met = [], []
    # parapet body (x from -0.55 seaward to 0 landward)
    con.append(M.prism("parapet", [(-0.55, -3.5), (0.0, -3.5), (0.0, 0.70), (-0.08, 0.78),
                                   (-0.47, 0.78), (-0.62, 0.62), (-0.55, 0.40)],
                       (0.0, 0.0, -L * 0.5), (0.0, 0.0, L * 0.5), uv_per_m=0.35))
    # coping joint grooves every 2 m (small raised bands break the long flat face)
    for z in (-1.0, 1.0):
        con.append(M.box(f"joint{z}", (-0.28, 0.40, z), (0.62, 0.78, 0.06), uv_per_m=0.6))
    for z in (-2.0, 0.0, 2.0):
        met.append(M.box(f"plate{z}", (-0.27, 0.80, z), (0.22, 0.04, 0.22)))
        met.append(M.beam(f"post{z}", (-0.27, 0.80, z), (-0.27, 1.82, z), 0.08, 0.08))
    for (y, r) in ((1.80, 0.050), (1.46, 0.032), (1.14, 0.032)):
        met.append(cyl(f"rail{y}", (-0.27, y, -L * 0.5), (-0.27, y, L * 0.5), r, seg=8, cap=False))
    c = fin(con, "concrete", bevel=0.02, smooth=30.0)
    m = fin(met, "metal", bevel=0.0, smooth=40.0)
    return asset([c, m], "Minato_Shore_SeawallRail")


def tetrapod():
    """
    Concrete tetrapod (~3.2 m): four tapered legs to the vertices of a tetrahedron.  Cheap
    (~130 tris) because it is instanced by the thousand along the armour.
    """
    parts = []
    dirs = [Vector((0, 1, 0)),
            Vector((0.943, -0.333, 0.0)),
            Vector((-0.471, -0.333, 0.816)),
            Vector((-0.471, -0.333, -0.816))]
    for i, d in enumerate(dirs):
        parts.append(cyl(f"leg{i}", (0.0, 0.0, 0.0), tuple(d * 1.55), 0.62, seg=8, taper=0.62))
    obj = fin(parts, "tetra", bevel=0.0, smooth=None)
    # sit the three lower feet on y = 0
    for v in obj.data.vertices:
        v.co.z += 0.333 * 1.55 + 0.3
    return asset([obj], "Minato_Shore_Tetrapod")


def armour_stone(tag, seed, count, spread):
    """Cluster of angular armour rocks for rubble breakwaters and revetments."""
    r = M.rng(seed)
    parts = []
    for i in range(count):
        a = r.uniform(0, math.tau)
        rad = r.uniform(0.0, spread)
        s = float(r.uniform(1.0, 1.9))
        parts.append(blob(f"s{i}", (math.cos(a) * rad, s * 0.45, math.sin(a) * rad),
                          (s * r.uniform(0.9, 1.3), s * r.uniform(0.6, 0.85), s * r.uniform(0.9, 1.3)),
                          seed * 17 + i, subdiv=1, jitter=0.22))
    return asset([fin(parts, "rock", bevel=0.0)], f"Minato_Shore_Armour{tag}")


def surf_foam(tag, seed, length, height):
    """
    White surf where waves break on the armour: a low irregular foam skirt plus a few spray
    lumps.  Static geometry - the cel shader turns it into bright, readable white shapes.
    Runs along +Z, centred; sits on the water at y = 0.
    """
    r = M.rng(seed)
    parts = []
    n = max(4, int(length / 1.6))
    for i in range(n):
        z = -length * 0.5 + (i + 0.5) * length / n + r.uniform(-0.4, 0.4)
        s = float(r.uniform(0.7, 1.4))
        parts.append(blob(f"f{i}", (r.uniform(-0.8, 0.8), 0.05, z),
                          (s * 1.3, s * 0.35 * height, s * 1.1), seed * 31 + i, subdiv=1,
                          jitter=0.25))
    # spray: low, wide, leaning lumps (a tall narrow lump read as an ice crystal)
    for i in range(max(1, n // 3)):
        z = r.uniform(-length * 0.4, length * 0.4)
        s = float(r.uniform(0.8, 1.3))
        parts.append(blob(f"p{i}", (r.uniform(-0.3, 0.3), height * 0.45, z),
                          (s * 1.2, s * 0.55 * height, s * 1.5), seed * 57 + i, subdiv=2,
                          jitter=0.28))
    return asset([fin(parts, "foam")], f"Minato_Shore_Surf{tag}")


def pampas():
    """Pampas / silver grass clump with cream plumes (Seawall + Causeway targets). ~1.8 m."""
    r = M.rng(91)
    blades, plumes = [], []
    for i in range(16):
        a = r.uniform(0, math.tau)
        h = float(r.uniform(0.8, 1.3))
        b = blade_strip(f"b{i}", (math.cos(a) * 0.12, 0.0, math.sin(a) * 0.12),
                        (math.cos(a), math.sin(a)), h, 0.07, float(r.uniform(0.35, 0.7)))
        blades.append(solidify(b, 0.015))
    for i in range(6):
        a = r.uniform(0, math.tau)
        lean = float(r.uniform(0.15, 0.45))
        base = Vector((math.cos(a) * 0.08, 0.0, math.sin(a) * 0.08))
        top = Vector((math.cos(a) * lean, float(r.uniform(1.45, 1.85)), math.sin(a) * lean))
        mid = base.lerp(top, 0.62)
        blades.append(cyl(f"st{i}", tuple(base), tuple(mid), 0.012, seg=4))
        plumes.append(cyl(f"pl{i}", tuple(mid), tuple(top + (top - mid) * 0.15), 0.07, seg=6,
                          taper=0.25))
    g = fin(blades, "grass", weld=0.0)
    p = fin(plumes, "plume", weld=0.0)
    return asset([g, p], "Minato_Shore_Pampas")


def grass_tuft():
    """Low coastal grass tuft, ~0.6 m, ground cover for verges and headland."""
    r = M.rng(93)
    blades = []
    for i in range(9):
        a = r.uniform(0, math.tau)
        b = blade_strip(f"b{i}", (math.cos(a) * 0.08, 0.0, math.sin(a) * 0.08),
                        (math.cos(a), math.sin(a)), float(r.uniform(0.4, 0.65)), 0.06,
                        float(r.uniform(0.15, 0.35)), segs=2)
        blades.append(solidify(b, 0.012))
    return asset([fin(blades, "grass", weld=0.0)], "Minato_Shore_GrassTuft")


def scrub(tag, seed, slot):
    """Low wind-clipped coastal scrub mound (~2 x 0.9 m): the headland's main ground mass."""
    r = M.rng(seed)
    parts = []
    for i in range(6):
        a = r.uniform(0, math.tau)
        rad = float(r.uniform(0.0, 0.8))
        s = float(r.uniform(0.55, 0.95))
        parts.append(blob(f"s{i}", (math.cos(a) * rad, s * 0.42, math.sin(a) * rad),
                          (s * 1.1, s * 0.62, s * 1.0), seed * 13 + i, subdiv=1, jitter=0.2))
    return asset([fin(parts, slot)], f"Minato_Shore_Scrub{tag}")


def cliff_rock(tag, seed, w, h, d):
    """
    Stratified headland outcrop: stacked, offset, bevelled slabs so it reads as layered
    sea-cliff rock, not as a rounded mountain boulder.
    """
    r = M.rng(seed)
    parts = []
    y = -h * 0.25
    layers = 6
    for i in range(layers):
        th = h / layers * float(r.uniform(0.8, 1.2))
        f = 1.0 - i / layers * 0.55
        cx = float(r.uniform(-0.12, 0.12)) * w
        cz = float(r.uniform(-0.12, 0.12)) * d
        slab = M.box(f"l{i}", (cx, y + th * 0.5, cz),
                     (w * f * float(r.uniform(0.85, 1.05)), th, d * f * float(r.uniform(0.8, 1.05))),
                     uv_per_m=0.25)
        rot = Matrix.Rotation(float(r.uniform(-0.25, 0.25)), 4, 'Z')
        slab.data.transform(rot)
        parts.append(slab)
        y += th * 0.93
    return asset([fin(parts, "rock", bevel=0.18, smooth=30.0)], f"Minato_Shore_CliffRock{tag}")


def ring_sculpture():
    """Stainless interlocking-ring sculpture on a white plinth (Seawall target, right terrace)."""
    met, con = [], []
    con.append(M.box("plinth", (0.0, 0.4, 0.0), (2.4, 0.8, 2.4), uv_per_m=0.5))
    for k, (tilt, yaw) in enumerate(((0.22, 0.0), (-0.35, 1.2))):
        n = 28
        R, rr = 2.3, 0.26
        rot = Matrix.Rotation(yaw, 3, 'Y') @ Matrix.Rotation(tilt, 3, 'Z')
        c = Vector((0.0, 0.8 + R + 0.05, 0.0))
        pts = []
        for i in range(n + 1):
            a = i / n * math.tau
            pts.append(c + rot @ Vector((math.cos(a) * R, math.sin(a) * R, 0.0)))
        for i in range(n):
            met.append(M.tube(f"r{k}_{i}", pts[i], pts[i + 1], rr, segments=8, cap=False))
    return asset([fin(con, "white", bevel=0.03, smooth=30.0),
                  fin(met, "metal", smooth=60.0, weld=0.002)], "Minato_Shore_RingSculpture")


def terrace_steps():
    """
    Stepped concrete landscape terrace (Seawall target right side): three planter tiers with a
    central stair.  12 m wide along X, climbs toward +Z (away from the road).  Soil tops take
    pampas/scrub instances in Unity.
    """
    con, soil = [], []
    W = 12.0
    for t in range(3):
        z0 = t * 2.2
        hgt = 0.8 * (t + 1)
        for side in (-1, 1):
            cx = side * (W * 0.25 + 0.9)
            con.append(M.box(f"wall{t}{side}", (cx, hgt * 0.5, z0 + 1.1), (W * 0.5 - 1.8, hgt, 2.2),
                             uv_per_m=0.5))
            soil.append(M.box(f"soil{t}{side}", (cx, hgt + 0.02, z0 + 1.25),
                              (W * 0.5 - 2.2, 0.06, 1.7), uv_per_m=0.5))
    for s in range(12):
        con.append(M.box(f"step{s}", (0.0, 0.2 * (s + 1) * 0.5, s * 0.55 + 0.3),
                         (3.4, 0.2 * (s + 1), 0.55), uv_per_m=0.6))
    return asset([fin(con, "concrete", bevel=0.02, smooth=30.0), fin(soil, "soil")],
                 "Minato_Shore_TerraceSteps")


def bench():
    """Concrete block bench with a teak seat (Seawall + Marina targets). 2.4 m."""
    con = [M.box("base", (0.0, 0.22, 0.0), (0.55, 0.44, 2.4), uv_per_m=0.6)]
    wood = [M.box(f"s{i}", (-0.18 + i * 0.12, 0.47, 0.0), (0.10, 0.05, 2.3), uv_per_m=0.8)
            for i in range(4)]
    return asset([fin(con, "concrete", bevel=0.02, smooth=30.0),
                  fin(wood, "teak", bevel=0.008, smooth=30.0)], "Minato_Shore_Bench")


def bollard_hazard():
    """Dark kerb bollard with a yellow reflective band (Seawall target landward kerb)."""
    dm = [cyl("b", (0.0, 0.0, 0.0), (0.0, 0.95, 0.0), 0.12, seg=10)]
    dm.append(cyl("cap", (0.0, 0.95, 0.0), (0.0, 1.02, 0.0), 0.13, seg=10, taper=0.7))
    hz = [cyl("band", (0.0, 0.70, 0.0), (0.0, 0.82, 0.0), 0.125, seg=10)]
    return asset([fin(dm, "darkmetal", smooth=40.0), fin(hz, "hazard", smooth=40.0)],
                 "Minato_Shore_Bollard")


def harbour_light():
    """
    Harbour entrance light (~11 m): painted round tower (slot 'towerpaint' - red or green is
    chosen in Unity), white band, gallery with rail, glass lantern and dark cap, on a concrete
    drum base.  Causeway + Seawall targets.
    """
    paint_, white, glass, dark, con, lamp = [], [], [], [], [], []
    con.append(cyl("drum", (0.0, -1.0, 0.0), (0.0, 1.4, 0.0), 2.2, seg=16))
    paint_.append(cyl("tower", (0.0, 1.4, 0.0), (0.0, 8.0, 0.0), 1.15, seg=16, taper=0.78))
    white.append(cyl("band", (0.0, 4.2, 0.0), (0.0, 5.2, 0.0), 1.075, seg=16, taper=0.98))
    dark.append(cyl("gallery", (0.0, 8.0, 0.0), (0.0, 8.25, 0.0), 1.45, seg=16))
    for i in range(12):
        a = i / 12 * math.tau
        dark.append(cyl(f"gp{i}", (math.cos(a) * 1.38, 8.25, math.sin(a) * 1.38),
                        (math.cos(a) * 1.38, 9.15, math.sin(a) * 1.38), 0.035, seg=4))
    dark.append(cyl("grail", (0.0, 9.1, 0.0), (0.0, 9.18, 0.0), 1.40, seg=16, cap=False))
    glass.append(cyl("lantern", (0.0, 8.25, 0.0), (0.0, 9.6, 0.0), 0.72, seg=10))
    lamp.append(cyl("lampcore", (0.0, 8.5, 0.0), (0.0, 9.3, 0.0), 0.30, seg=8))
    paint_.append(cyl("cap", (0.0, 9.6, 0.0), (0.0, 10.5, 0.0), 0.85, seg=12, taper=0.1))
    dark.append(cyl("vent", (0.0, 10.4, 0.0), (0.0, 10.9, 0.0), 0.08, seg=6))
    return asset([fin(con, "concrete", bevel=0.03, smooth=40.0),
                  fin(paint_, "towerpaint", smooth=40.0), fin(white, "white", smooth=40.0),
                  fin(dark, "darkmetal", smooth=40.0), fin(glass, "glass", smooth=40.0),
                  fin(lamp, "lamp", smooth=40.0)], "Minato_Shore_HarbourLight")


def headland_lighthouse():
    """
    Headland lighthouse for the finish point (~24 m): white octagonal tapered tower with two
    coral bands, dark gallery + rail, glass lantern, navy dome, and a keeper's store (white,
    navy roof) at its foot.  Deliberately compact: it must share a small summit with the
    lookout terrace, which the 236 m Minato_Lighthouse_Hero promontory could never do.
    """
    white, coral, navy, glass, dark, lamp, con = [], [], [], [], [], [], []
    con.append(cyl("base", (0.0, -1.5, 0.0), (0.0, 0.8, 0.0), 3.6, seg=8))
    white.append(cyl("tower", (0.0, 0.8, 0.0), (0.0, 19.0, 0.0), 2.5, seg=8, taper=0.66))
    for (y0, y1) in ((6.5, 8.3), (12.6, 14.2)):
        f0 = 1.0 - (y0 - 0.8) / 18.2 * 0.34
        f1 = 1.0 - (y1 - 0.8) / 18.2 * 0.34
        coral.append(cyl(f"band{y0}", (0.0, y0, 0.0), (0.0, y1, 0.0), 2.5 * f0 + 0.04, seg=8,
                         taper=f1 / f0))
    dark.append(cyl("gallery", (0.0, 19.0, 0.0), (0.0, 19.35, 0.0), 2.35, seg=16))
    for i in range(16):
        a = i / 16 * math.tau
        dark.append(cyl(f"gp{i}", (math.cos(a) * 2.25, 19.35, math.sin(a) * 2.25),
                        (math.cos(a) * 2.25, 20.4, math.sin(a) * 2.25), 0.04, seg=4))
    dark.append(cyl("grail", (0.0, 20.35, 0.0), (0.0, 20.43, 0.0), 2.28, seg=16, cap=False))
    glass.append(cyl("lantern", (0.0, 19.35, 0.0), (0.0, 21.4, 0.0), 1.2, seg=12))
    lamp.append(cyl("lampcore", (0.0, 19.7, 0.0), (0.0, 21.0, 0.0), 0.5, seg=8))
    navy.append(cyl("dome", (0.0, 21.4, 0.0), (0.0, 22.8, 0.0), 1.45, seg=12, taper=0.15))
    dark.append(cyl("vane", (0.0, 22.7, 0.0), (0.0, 23.8, 0.0), 0.06, seg=5))
    # keeper's store
    white.append(M.box("store", (4.6, 1.7, 0.0), (4.4, 3.4, 5.6), uv_per_m=0.4))
    navy.append(M.prism("storeroof", [(-2.6, 0.0), (0.0, 1.1), (2.6, 0.0), (2.6, -0.18),
                                      (0.0, 0.92), (-2.6, -0.18)],
                        (4.6, 3.4, -3.1), (4.6, 3.4, 3.1), uv_per_m=0.5))
    glass.append(M.box("door", (6.82, 1.1, 0.0), (0.06, 2.1, 1.1)))
    return asset([fin(con, "concrete", bevel=0.03, smooth=30.0),
                  fin(white, "white", bevel=0.02, smooth=30.0),
                  fin(coral, "coral", smooth=30.0), fin(navy, "navy", bevel=0.02, smooth=30.0),
                  fin(dark, "darkmetal", smooth=40.0), fin(glass, "glass", smooth=40.0),
                  fin(lamp, "lamp", smooth=40.0)], "Minato_Shore_HeadlandLighthouse")


def lookout():
    """
    Panoramic finish overlook: a half-octagon concrete deck (16 m wide, 9 m deep) on piers,
    brushed-metal railing around the view edge, two teak benches, two coin telescopes, a navy
    info board and a coral shade sail.  Origin = centre of the ROAD-SIDE edge at deck level;
    the deck projects toward +Z (the view).
    """
    con, met, wood, navy, coral, dark = [], [], [], [], [], []
    R = 8.0
    pts = [(-R, 0.0)]
    for k in range(5):
        a = math.pi - k / 4.0 * math.pi
        pts.append((math.cos(a) * R, 1.0 + math.sin(a) * (R - 1.0)))
    pts.append((R, 0.0))
    # deck slab: fan from origin
    verts = [(0.0, 0.0, 0.0)] + [(x, 0.0, z) for (x, z) in pts]
    faces = [(0, i + 1, i + 2) for i in range(len(pts) - 1)]
    slab = S.mesh_from_arrays("slab", [u2b(*v) for v in verts], faces, smooth=False)
    solidify(slab, 0.45)
    S.recalc_normals(slab)
    con.append(slab)
    # piers
    for (x, z) in pts[1:-1]:
        con.append(M.box(f"pier{x:.1f}", (x * 0.8, -3.2, z * 0.8), (0.6, 6.0, 0.6), uv_per_m=0.4))
    con.append(M.box("pierc", (0.0, -3.2, 0.5), (0.6, 6.0, 0.6)))
    # railing along the view edge (between consecutive outline points)
    for i in range(1, len(pts) - 1):
        (x0, z0), (x1, z1) = pts[i], pts[i + 1]
        met.append(M.beam(f"post{i}", (x0, 0.0, z0), (x0, 1.15, z0), 0.07, 0.07))
        for (y, r) in ((1.12, 0.045), (0.78, 0.028), (0.45, 0.028)):
            met.append(cyl(f"r{i}_{y}", (x0, y, z0), (x1, y, z1), r, seg=6, cap=False))
    met.append(M.beam("postE", (pts[-2][0], 0.0, pts[-2][1]),
                      (pts[-2][0], 1.15, pts[-2][1]), 0.07, 0.07))
    # benches (facing the view)
    for x in (-3.2, 3.2):
        con.append(M.box(f"bb{x}", (x, 0.22, 3.2), (2.2, 0.44, 0.5), uv_per_m=0.6))
        for i in range(3):
            wood.append(M.box(f"bs{x}{i}", (x, 0.47, 3.05 + i * 0.13), (2.1, 0.05, 0.1)))
    # coin telescopes
    for x in (-5.4, 5.4):
        dark.append(cyl(f"tp{x}", (x, 0.0, 5.2), (x, 1.05, 5.2), 0.07, seg=8))
        dark.append(cyl(f"tb{x}", (x, 1.15, 4.95), (x, 1.32, 5.55), 0.16, seg=10, taper=0.8))
    # info board
    navy.append(M.box("board", (0.0, 1.1, 0.4), (1.6, 0.9, 0.08)))
    dark.append(M.beam("bl", (-0.7, 0.0, 0.42), (-0.7, 1.55, 0.42), 0.06, 0.06))
    dark.append(M.beam("br", (0.7, 0.0, 0.42), (0.7, 1.55, 0.42), 0.06, 0.06))
    # shade sail: triangle between three masts
    masts = [(-6.0, 1.2), (6.0, 1.2), (0.0, 7.0)]
    tops = [(-6.0, 3.6, 1.2), (6.0, 3.1, 1.2), (0.0, 4.2, 7.0)]
    for (x, z), t in zip(masts, tops):
        met.append(cyl(f"m{x}{z}", (x, 0.0, z), t, 0.06, seg=6))
    sail = S.mesh_from_arrays("sail", [u2b(*v) for v in tops], [(0, 1, 2)], smooth=False)
    solidify(sail, 0.03)
    coral.append(sail)
    return asset([fin(con, "concrete", bevel=0.03, smooth=30.0),
                  fin(met, "metal", smooth=40.0), fin(wood, "teak", smooth=30.0),
                  fin(navy, "navy"), fin(coral, "coral"),
                  fin(dark, "darkmetal", smooth=40.0)], "Minato_Shore_Lookout")


def promenade_lamp():
    """Slim dark promenade lamp (Marina + Causeway targets): 7 m pole, forward arm, lantern."""
    dark = [cyl("pole", (0.0, 0.0, 0.0), (0.0, 7.0, 0.0), 0.09, seg=8, taper=0.7),
            cyl("base", (0.0, 0.0, 0.0), (0.0, 0.6, 0.0), 0.16, seg=8),
            M.beam("arm", (0.0, 6.9, 0.0), (-1.3, 7.05, 0.0), 0.08, 0.06),
            M.box("head", (-1.45, 6.98, 0.0), (0.7, 0.14, 0.3))]
    lamp = [M.box("lens", (-1.45, 6.89, 0.0), (0.6, 0.04, 0.24))]
    return asset([fin(dark, "darkmetal", smooth=40.0), fin(lamp, "lamp")],
                 "Minato_Shore_PromenadeLamp")


# ============================================================ MARINA RIBBON

def pontoon_walk():
    """
    12 m main pontoon, 2.6 m wide: white floats, teak plank deck (planks are geometry so the
    grain reads without a texture), navy rubbing strake, four cleats and a service pedestal.
    Origin at the WATERLINE (y = 0), deck top at +0.5, runs z -6..+6.
    """
    L, W = 12.0, 2.6
    flt, wood, navy, dark, white = [], [], [], [], []
    flt.append(M.box("float", (0.0, 0.2, 0.0), (W, 0.4, L - 0.1), uv_per_m=0.5))
    for i in range(10):
        x = -W * 0.5 + 0.13 + i * (W - 0.26) / 9.0
        wood.append(M.box(f"pl{i}", (x, 0.46, 0.0), (0.24, 0.08, L - 0.05), uv_per_m=0.8))
    for s in (-1, 1):
        navy.append(M.box(f"strake{s}", (s * (W * 0.5 + 0.04), 0.36, 0.0), (0.1, 0.16, L),
                          uv_per_m=0.8))
        for z in (-4.0, 4.0):
            dark.append(M.box(f"cleat{s}{z}", (s * (W * 0.5 - 0.15), 0.56, z), (0.12, 0.08, 0.36)))
    white.append(M.box("pedestal", (0.9, 1.05, 0.0), (0.3, 1.1, 0.3)))
    dark.append(M.box("pedtop", (0.9, 1.63, 0.0), (0.34, 0.06, 0.34)))
    return asset([fin(flt, "white", bevel=0.03, smooth=30.0), fin(wood, "teak"),
                  fin(navy, "navy"), fin(dark, "darkmetal"), fin(white, "white")],
                 "Minato_Marina_PontoonWalk")


def pontoon_finger():
    """10 m finger pier (1.2 m) with a white-capped steel pile at the outer end. Runs z 0..10."""
    L, W = 10.0, 1.2
    flt, wood, dark, white = [], [], [], []
    flt.append(M.box("float", (0.0, 0.2, L * 0.5), (W, 0.4, L), uv_per_m=0.5))
    for i in range(5):
        x = -W * 0.5 + 0.12 + i * (W - 0.24) / 4.0
        wood.append(M.box(f"pl{i}", (x, 0.46, L * 0.5), (0.22, 0.08, L - 0.05), uv_per_m=0.8))
    dark.append(cyl("pile", (0.0, -3.0, L + 0.35), (0.0, 3.0, L + 0.35), 0.2, seg=10))
    white.append(cyl("pilecap", (0.0, 3.0, L + 0.35), (0.0, 3.35, L + 0.35), 0.22, seg=10, taper=0.4))
    dark.append(M.box("cleat", (0.4, 0.56, L - 0.6), (0.1, 0.08, 0.3)))
    return asset([fin(flt, "white", bevel=0.03, smooth=30.0), fin(wood, "teak"),
                  fin(dark, "darkmetal", smooth=40.0), fin(white, "white", smooth=40.0)],
                 "Minato_Marina_PontoonFinger")


def gangway():
    """8 m aluminium gangway with handrails; origin at the TOP (quay) end, descends toward +Z."""
    met = []
    drop = 1.6
    met.append(M.beam("deck", (0.0, 0.0, 0.0), (0.0, -drop, 8.0), 1.1, 0.12))
    for s in (-1, 1):
        met.append(M.beam(f"stringer{s}", (s * 0.55, 0.15, 0.0), (s * 0.55, 0.15 - drop, 8.0), 0.06, 0.3))
        met.append(cyl(f"hand{s}", (s * 0.55, 1.05, 0.0), (s * 0.55, 1.05 - drop, 8.0), 0.03, seg=6))
        for k in range(5):
            t = k / 4.0
            met.append(cyl(f"p{s}{k}", (s * 0.55, 0.15 - drop * t, 8.0 * t),
                           (s * 0.55, 1.05 - drop * t, 8.0 * t), 0.025, seg=5))
    return asset([fin(met, "metal", smooth=40.0)], "Minato_Marina_Gangway")


def sailing_yacht(tag, L, mast_h, seed):
    """
    Moored sloop: lofted white hull with navy boot stripe, teak deck, cabin trunk with a dark
    window band, aluminium mast/boom with a NAVY sail cover (furled - moored boats never show
    set sails), forestay/backstay, pulpit rail.  Origin at the waterline, bow toward +Z.
    """
    B = L * 0.33
    hull, deck = hull_loft("hull", L, B, L * 0.10, sheer=L * 0.03, freeboard=L * 0.10)
    white, navy, teak, glass, met = [hull], [], [], [], []
    stripe, _ = hull_loft("stripe", L * 1.002, B * 1.01, L * 0.02, sheer=L * 0.03,
                          freeboard=L * 0.012)
    navy.append(stripe)
    teak.append(deck_plate("deck", deck, inset=0.08, lift=-0.02))
    fb = L * 0.10
    white.append(M.box("cabin", (0.0, fb + 0.35, -L * 0.05), (B * 0.55, 0.7, L * 0.38), uv_per_m=0.6))
    glass.append(M.box("win", (0.0, fb + 0.45, -L * 0.05), (B * 0.56, 0.22, L * 0.30)))
    white.append(M.box("coach", (0.0, fb + 0.78, -L * 0.02), (B * 0.45, 0.18, L * 0.22)))
    mz = L * 0.08
    met.append(cyl("mast", (0.0, fb + 0.7, mz), (0.0, fb + mast_h, mz), 0.09, seg=6, taper=0.6))
    met.append(cyl("boom", (0.0, fb + 1.9, mz), (0.0, fb + 1.75, mz - L * 0.42), 0.07, seg=6))
    navy.append(M.beam("cover", (0.0, fb + 2.05, mz - 0.1), (0.0, fb + 1.9, mz - L * 0.40),
                       0.32, 0.36, taper=0.55))
    met.append(cyl("forestay", (0.0, fb + mast_h * 0.95, mz), (0.0, fb + 0.3, L * 0.48), 0.015, seg=4))
    met.append(cyl("backstay", (0.0, fb + mast_h, mz), (0.0, fb + 0.4, -L * 0.49), 0.015, seg=4))
    met.append(cyl("pulpit", (-B * 0.3, fb + 0.7, L * 0.40), (B * 0.3, fb + 0.7, L * 0.40), 0.02, seg=4))
    navy.append(cyl("furl", (0.0, fb + 0.4, L * 0.46), (0.0, fb + mast_h * 0.8, mz + 0.4), 0.09, seg=6))
    return asset([fin(white, "white", smooth=35.0), fin(navy, "navy", smooth=35.0),
                  fin(teak, "teak"), fin(glass, "glass"), fin(met, "metal", smooth=40.0)],
                 f"Minato_Marina_Yacht{tag}")


def motor_yacht():
    """13 m motor yacht: white hull + stepped superstructure, dark glazing band, navy bimini."""
    L = 13.0
    B = 4.2
    hull, deck = hull_loft("hull", L, B, 1.4, sheer=0.5, freeboard=1.5, transom=0.8)
    white, navy, glass, teak, met = [hull], [], [], [], []
    stripe, _ = hull_loft("stripe", L * 1.002, B * 1.01, 0.25, sheer=0.5, freeboard=0.2,
                          transom=0.8)
    navy.append(stripe)
    teak.append(deck_plate("deck", deck, inset=0.1, lift=-0.02))
    white.append(M.box("sup1", (0.0, 2.35, -0.8), (B * 0.78, 1.7, L * 0.55), uv_per_m=0.5))
    glass.append(M.box("g1", (0.0, 2.5, -0.8), (B * 0.79, 0.65, L * 0.48)))
    white.append(M.box("sup2", (0.0, 3.5, -1.6), (B * 0.62, 0.6, L * 0.34)))
    glass.append(M.box("wind", (0.0, 3.0, 2.0), (B * 0.7, 0.6, 0.3)))
    navy.append(M.box("bimini", (0.0, 4.6, -1.6), (B * 0.66, 0.08, L * 0.30)))
    for sx in (-1, 1):
        for sz in (-1, 1):
            met.append(cyl(f"bp{sx}{sz}", (sx * B * 0.3, 3.8, -1.6 + sz * L * 0.14),
                           (sx * B * 0.3, 4.58, -1.6 + sz * L * 0.14), 0.03, seg=4))
    met.append(cyl("radar", (0.0, 4.6, -1.0), (0.0, 5.4, -1.0), 0.05, seg=5))
    return asset([fin(white, "white", smooth=35.0), fin(navy, "navy", smooth=35.0),
                  fin(glass, "glass"), fin(teak, "teak"), fin(met, "metal")],
                 "Minato_Marina_MotorYacht")


def travel_lift():
    """
    Blue marine travel-lift gantry (Marina target boat hoist): two portal frames joined by top
    girders, wheel bogies, lifting slings.  7.5 m wide, 10 m tall, 9 m long.  A yacht is hung in
    the slings by the Unity pass.
    """
    blue, dark = [], []
    W, H, Lg = 7.5, 10.0, 9.0
    for z in (-Lg * 0.5, Lg * 0.5):
        for x in (-W * 0.5, W * 0.5):
            blue.append(M.beam(f"leg{x}{z}", (x, 0.8, z), (x, H, z), 0.55, 0.55))
            dark.append(cyl(f"wheel{x}{z}", (x - 0.35, 0.55, z), (x + 0.35, 0.55, z), 0.55, seg=12))
            blue.append(M.box(f"bogie{x}{z}", (x, 0.95, z), (0.7, 0.5, 1.6)))
        blue.append(M.beam(f"cross{z}", (-W * 0.5, H - 0.4, z), (W * 0.5, H - 0.4, z), 0.7, 0.8))
    for x in (-W * 0.5, W * 0.5):
        blue.append(M.beam(f"girder{x}", (x, H - 0.4, -Lg * 0.5), (x, H - 0.4, Lg * 0.5), 0.6, 0.9))
        blue.append(M.beam(f"brace{x}", (x, 2.0, -Lg * 0.5), (x, H - 1.2, 0.0), 0.3, 0.3))
    for z in (-2.2, 2.2):
        for x in (-W * 0.5 + 0.4, W * 0.5 - 0.4):
            dark.append(M.beam(f"cable{x}{z}", (x, H - 0.9, z), (x * 0.3, 3.2, z), 0.05, 0.05))
        dark.append(M.beam(f"sling{z}", (-1.2, 3.1, z), (1.2, 3.1, z), 0.3, 0.06))
    return asset([fin(blue, "blue", bevel=0.03, smooth=30.0), fin(dark, "darkmetal", smooth=40.0)],
                 "Minato_Marina_TravelLift")


def bollard_rope():
    """
    Quay-edge bollard + rope span (Marina target): dark cast bollard with a white dome cap and a
    white/cream rope that sags to the NEXT bollard 4 m ahead.  Module pitch 4 m along +Z.
    """
    dark = [cyl("b", (0.0, 0.0, 0.0), (0.0, 0.85, 0.0), 0.2, seg=12),
            cyl("collar", (0.0, 0.6, 0.0), (0.0, 0.68, 0.0), 0.23, seg=12)]
    white = [cyl("cap", (0.0, 0.85, 0.0), (0.0, 1.1, 0.0), 0.2, seg=12, taper=0.35)]
    rope = catenary("rope", (0.0, 0.64, 0.0), (0.0, 0.64, 4.0), 0.28, 0.035)
    return asset([fin(dark, "darkmetal", smooth=40.0), fin(white, "white", smooth=40.0),
                  fin(rope, "rope", smooth=50.0, weld=0.0)], "Minato_Marina_BollardRope")


def marina_cafe():
    """
    Marina cafe pavilion (16 x 9 m): full-height glass front with white mullions, deep white
    roof canopy, rear service wall; teak terrace deck in front (+Z) with three white parasols
    and tables.  Faces +Z (the water/road).
    """
    white, glass, teak, canvas, dark = [], [], [], [], []
    W, D, H = 16.0, 9.0, 4.2
    white.append(M.box("back", (0.0, H * 0.5, -D * 0.5 + 0.3), (W, H, 0.6), uv_per_m=0.4))
    for s in (-1, 1):
        white.append(M.box(f"side{s}", (s * (W * 0.5 - 0.3), H * 0.5, 0.0), (0.6, H, D), uv_per_m=0.4))
    glass.append(M.box("front", (0.0, H * 0.5 - 0.1, D * 0.5 - 0.3), (W - 0.8, H - 0.4, 0.1)))
    for i in range(9):
        x = -W * 0.5 + 0.6 + i * (W - 1.2) / 8.0
        white.append(M.box(f"mul{i}", (x, H * 0.5, D * 0.5 - 0.25), (0.14, H, 0.2)))
    white.append(M.box("roof", (0.0, H + 0.25, 0.9), (W + 1.2, 0.5, D + 3.4), uv_per_m=0.4))
    white.append(M.box("floor", (0.0, 0.1, 0.0), (W, 0.2, D), uv_per_m=0.4))
    teak.append(M.box("terrace", (0.0, 0.12, D * 0.5 + 3.2), (W + 1.0, 0.24, 6.0), uv_per_m=0.8))
    for k, x in enumerate((-5.0, 0.0, 5.0)):
        z = D * 0.5 + 3.4
        dark.append(cyl(f"pole{k}", (x, 0.24, z), (x, 2.75, z), 0.04, seg=6))
        canvas.append(cyl(f"shade{k}", (x, 2.3, z), (x, 2.95, z), 1.6, seg=10, taper=0.05))
        white.append(cyl(f"table{k}", (x, 0.95, z), (x, 1.0, z), 0.55, seg=10))
        dark.append(cyl(f"tleg{k}", (x, 0.24, z), (x, 0.95, z), 0.05, seg=5))
        for a in range(4):
            ang = a / 4 * math.tau + 0.4
            cx, cz = x + math.cos(ang) * 0.95, z + math.sin(ang) * 0.95
            dark.append(M.box(f"ch{k}{a}", (cx, 0.47, cz), (0.42, 0.06, 0.42)))
            dark.append(M.beam(f"cl{k}{a}", (cx, 0.24, cz), (cx, 0.47, cz), 0.3, 0.3))
    return asset([fin(white, "white", bevel=0.02, smooth=30.0), fin(glass, "glass"),
                  fin(teak, "teak"), fin(canvas, "canvas", smooth=40.0),
                  fin(dark, "darkmetal", smooth=40.0)], "Minato_Marina_Cafe")


def _windows(parts, x0, x1, y0, y1, z, cols, rows, face=1):
    dw = (x1 - x0) / cols
    dh = (y1 - y0) / rows
    for c in range(cols):
        for r in range(rows):
            parts.append(M.box(f"w{z}{c}{r}", (x0 + (c + 0.5) * dw, y0 + (r + 0.5) * dh, z),
                               (dw * 0.66, dh * 0.62, 0.12)))


def apartment():
    """
    4-storey white coastal apartment (20 x 12 x 13.6 m): continuous navy balcony slabs with
    glass balustrades on BOTH long facades (they are seen from the road and the water), recessed
    dark glazing, parapet and a teak rooftop pergola.
    """
    white, navy, glass, teak = [], [], [], []
    W, D, F = 20.0, 12.0, 4
    fh = 3.2
    H = F * fh + 0.8
    white.append(M.box("shell", (0.0, H * 0.5, 0.0), (W, H, D), uv_per_m=0.3))
    white.append(M.box("para", (0.0, H + 0.4, 0.0), (W + 0.3, 0.8, D + 0.3), uv_per_m=0.5))
    for f in range(1, F):
        y = f * fh + 0.4
        for s in (-1, 1):
            navy.append(M.box(f"bal{f}{s}", (0.0, y, s * (D * 0.5 + 0.8)), (W - 1.0, 0.22, 1.6)))
            glass.append(M.box(f"bg{f}{s}", (0.0, y + 0.55, s * (D * 0.5 + 1.55)), (W - 1.0, 0.9, 0.06)))
    for s in (-1, 1):
        _windows(glass, -W * 0.45, W * 0.45, 0.6, H - 0.8, s * (D * 0.5 + 0.02), 6, F)
    for k in range(6):
        teak.append(M.box(f"perg{k}", (-4.0 + k * 1.6, H + 2.6, 0.0), (0.16, 0.2, 6.0)))
    for sx in (-1, 1):
        for sz in (-1, 1):
            white.append(M.box(f"pp{sx}{sz}", (sx * 4.0, H + 1.3, sz * 2.8), (0.25, 2.6, 0.25)))
    return asset([fin(white, "white", bevel=0.03, smooth=30.0), fin(navy, "navy", bevel=0.02),
                  fin(glass, "glass"), fin(teak, "teak")], "Minato_Marina_Apartment")


def terrace_block():
    """3-storey stepped terrace block (16 x 14 m) - each floor sets back 2.6 m toward -Z."""
    white, navy, glass = [], [], []
    W = 16.0
    for f in range(3):
        d = 14.0 - f * 2.6
        zc = -f * 1.3
        y0 = f * 3.3
        white.append(M.box(f"f{f}", (0.0, y0 + 1.65, zc), (W - f * 0.6, 3.3, d), uv_per_m=0.3))
        navy.append(M.box(f"trim{f}", (0.0, y0 + 3.35, zc), (W - f * 0.6 + 0.2, 0.22, d + 0.2)))
        glass.append(M.box(f"rail{f}", (0.0, y0 + 3.9, zc + d * 0.5 - 0.1), (W - f * 0.6 - 0.4, 0.9, 0.06)))
        _windows(glass, -W * 0.42, W * 0.42, y0 + 0.4, y0 + 3.0, zc + d * 0.5 + 0.02, 5, 1)
        _windows(glass, -W * 0.42, W * 0.42, y0 + 0.4, y0 + 3.0, zc - d * 0.5 - 0.02, 4, 1)
    return asset([fin(white, "white", bevel=0.03, smooth=30.0), fin(navy, "navy"),
                  fin(glass, "glass")], "Minato_Marina_TerraceBlock")


def coastal_house():
    """2-storey white house (10 x 9 m) with a low navy hip roof, blue shutters, front balcony."""
    white, navy, glass, blue = [], [], [], []
    W, D, H = 10.0, 9.0, 6.4
    white.append(M.box("shell", (0.0, H * 0.5, 0.0), (W, H, D), uv_per_m=0.35))
    navy.append(M.prism("roof", [(-W * 0.5 - 0.7, 0.0), (0.0, 1.9), (W * 0.5 + 0.7, 0.0),
                                 (W * 0.5 + 0.7, -0.2), (0.0, 1.7), (-W * 0.5 - 0.7, -0.2)],
                        (0.0, H, -D * 0.5 - 0.7), (0.0, H, D * 0.5 + 0.7), uv_per_m=0.5))
    for s in (-1, 1):
        for c in range(3):
            for r in range(2):
                x = -W * 0.3 + c * W * 0.3
                y = 1.4 + r * 3.0
                z = s * (D * 0.5 + 0.02)
                glass.append(M.box(f"w{s}{c}{r}", (x, y, z), (1.1, 1.5, 0.1)))
                blue.append(M.box(f"sh{s}{c}{r}a", (x - 0.8, y, z), (0.45, 1.5, 0.12)))
                blue.append(M.box(f"sh{s}{c}{r}b", (x + 0.8, y, z), (0.45, 1.5, 0.12)))
    white.append(M.box("balc", (0.0, 3.2, D * 0.5 + 0.7), (W * 0.6, 0.18, 1.4)))
    navy.append(M.box("balcrail", (0.0, 3.7, D * 0.5 + 1.35), (W * 0.6, 0.8, 0.06)))
    return asset([fin(white, "white", bevel=0.03, smooth=30.0), fin(navy, "navy", bevel=0.02),
                  fin(glass, "glass"), fin(blue, "blue")], "Minato_Marina_House")


def boathouse():
    """Marina boat shed (18 x 12 x 8 m): white walls, navy standing-seam gable, big blue door."""
    white, navy, blue, glass = [], [], [], []
    W, D, H = 12.0, 18.0, 5.5
    white.append(M.box("shell", (0.0, H * 0.5, 0.0), (W, H, D), uv_per_m=0.3))
    navy.append(M.prism("roof", [(-W * 0.5 - 0.5, 0.0), (0.0, 2.6), (W * 0.5 + 0.5, 0.0),
                                 (W * 0.5 + 0.5, -0.25), (0.0, 2.35), (-W * 0.5 - 0.5, -0.25)],
                        (0.0, H, -D * 0.5 - 0.4), (0.0, H, D * 0.5 + 0.4), uv_per_m=0.5))
    blue.append(M.box("door", (0.0, 2.3, D * 0.5 + 0.03), (6.5, 4.6, 0.12)))
    white.append(M.prism("gable", [(-W * 0.5, 0.0), (0.0, 2.6), (W * 0.5, 0.0)],
                         (0.0, H, D * 0.5 - 0.3), (0.0, H, D * 0.5 + 0.02)))
    for s in (-1, 1):
        for k in range(4):
            glass.append(M.box(f"sw{s}{k}", (s * (W * 0.5 + 0.02), 3.6, -6.0 + k * 4.0), (0.1, 1.2, 2.0)))
    return asset([fin(white, "white", bevel=0.03, smooth=30.0), fin(navy, "navy", bevel=0.02),
                  fin(blue, "blue"), fin(glass, "glass")], "Minato_Marina_Boathouse")


# ============================================================ export table

# (builder, filename, LOD ratios).  Ground cover ships LOD0 only.
CATALOG = [
    (seawall_rail,                                   "Minato_Shore_SeawallRail.fbx",   (0.45, 0.2)),
    (tetrapod,                                       "Minato_Shore_Tetrapod.fbx",      ()),
    (lambda: armour_stone("A", 7, 5, 1.8),           "Minato_Shore_ArmourA.fbx",       (0.5,)),
    (lambda: armour_stone("B", 11, 7, 2.6),          "Minato_Shore_ArmourB.fbx",       (0.5,)),
    (lambda: surf_foam("A", 3, 8.0, 1.0),            "Minato_Shore_SurfA.fbx",         ()),
    (lambda: surf_foam("B", 5, 6.0, 1.8),            "Minato_Shore_SurfB.fbx",         ()),
    (pampas,                                         "Minato_Shore_Pampas.fbx",        ()),
    (grass_tuft,                                     "Minato_Shore_GrassTuft.fbx",     ()),
    (lambda: scrub("A", 21, "scrub"),                "Minato_Shore_ScrubA.fbx",        ()),
    (lambda: scrub("B", 23, "grass"),                "Minato_Shore_ScrubB.fbx",        ()),
    (lambda: cliff_rock("A", 31, 12.0, 7.0, 9.0),    "Minato_Shore_CliffRockA.fbx",    (0.45, 0.2)),
    (lambda: cliff_rock("B", 37, 7.0, 4.0, 6.0),     "Minato_Shore_CliffRockB.fbx",    (0.45, 0.2)),
    (ring_sculpture,                                 "Minato_Shore_RingSculpture.fbx", (0.4,)),
    (terrace_steps,                                  "Minato_Shore_TerraceSteps.fbx",  (0.5,)),
    (bench,                                          "Minato_Shore_Bench.fbx",         ()),
    (bollard_hazard,                                 "Minato_Shore_Bollard.fbx",       ()),
    (harbour_light,                                  "Minato_Shore_HarbourLight.fbx",  (0.45, 0.2)),
    (headland_lighthouse,                            "Minato_Shore_HeadlandLighthouse.fbx", (0.45, 0.2)),
    (lookout,                                        "Minato_Shore_Lookout.fbx",       (0.5,)),
    (promenade_lamp,                                 "Minato_Shore_PromenadeLamp.fbx", ()),
    (pontoon_walk,                                   "Minato_Marina_PontoonWalk.fbx",  (0.4,)),
    (pontoon_finger,                                 "Minato_Marina_PontoonFinger.fbx", (0.4,)),
    (gangway,                                        "Minato_Marina_Gangway.fbx",      ()),
    (lambda: sailing_yacht("A", 11.0, 14.5, 1),      "Minato_Marina_YachtA.fbx",       (0.45, 0.2)),
    (lambda: sailing_yacht("B", 9.0, 12.0, 2),       "Minato_Marina_YachtB.fbx",       (0.45, 0.2)),
    (motor_yacht,                                    "Minato_Marina_MotorYacht.fbx",   (0.45, 0.2)),
    (travel_lift,                                    "Minato_Marina_TravelLift.fbx",   (0.45,)),
    (bollard_rope,                                   "Minato_Marina_BollardRope.fbx",  ()),
    (marina_cafe,                                    "Minato_Marina_Cafe.fbx",         (0.45, 0.2)),
    (apartment,                                      "Minato_Marina_Apartment.fbx",    (0.45, 0.2)),
    (terrace_block,                                  "Minato_Marina_TerraceBlock.fbx", (0.45, 0.2)),
    (coastal_house,                                  "Minato_Marina_House.fbx",        (0.45, 0.2)),
    (boathouse,                                      "Minato_Marina_Boathouse.fbx",    (0.45, 0.2)),
]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    filt = argv[0] if argv else ""
    total = 0
    for builder, filename, ratios in CATALOG:
        if filt and filt not in filename:
            continue
        S.reset_scene()
        obj = builder()
        M.stat(obj, filename.replace(".fbx", ""))
        exports = [obj]
        base = obj.name.replace("_LOD0", "")
        for i, r in enumerate(ratios):
            exports.append(M.decimated_copy(obj, f"{base}_LOD{i + 1}", r))
        if not ratios:
            obj.name = base   # single-LOD ground cover: plain name, no LODGroup in Unity
        M.export_fbx(exports, filename)
        obj.data.calc_loop_triangles()
        total += len(obj.data.loop_triangles)
    print(f"[shores] complete: {total:,} LOD0 tris")


if __name__ == "__main__":
    main()

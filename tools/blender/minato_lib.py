"""
Shared authoring helpers for the MINATO COAST asset pipeline.

Builds on ``sakura_lib`` (coordinate swap, noise, mesh helpers, modifiers, materials) and adds
what the Minato brief needs and the Sakura pipeline does not have:

  * FBX export into ``Assets/Environment/MinatoCoast/Models`` with a CLEAN Unity import
    (identity root rotation, scale 1, 1 Blender metre = 1 Unity unit).
  * ``.blend`` source saving into ``Assets/Environment/MinatoCoast/Blender``.
  * Structural-member primitives - oriented box beams, tubes, tapered piers, lattice trusses -
    because a steel arch bridge is 95 % "box section between two points" and doing that by hand
    per member is how graybox happens.


COORDINATE CONVENTION - MEASURED, NOT ASSUMED
---------------------------------------------
``tools/blender/_orient_probe.py`` / ``_orient_probe_b.py`` authored an asymmetric marker
(body 1 x 2 x 4 m with a uniquely sized nub on each of +X/+Y/+Z), exported it, and a Unity
editor probe read the imported vertices back. Measured result:

    Blender (bx, by, bz)  ->  Unity (-bx, bz, -by)

which is EXACTLY the round trip ``sakura_lib`` documents for glTF. So ``sakura_lib.u2b`` -
``(x, y, z) -> (-x, -z, y)`` - is the identity-making inverse for FBX too, and every Minato
builder authors in **Unity coordinates** and passes through ``S.u2b`` like the rest of the
project. Do not hand-roll an axis swap here.

The export settings below (``bake_space_transform=True`` + ``FBX_SCALE_ALL``) are the reason
the import is clean: without them the same file arrives with a root carrying scale 100 and a
-90 deg X rotation, which breaks "clean pivots and applied transforms". Both variants were
measured; only this one gives an identity root.


MODULE-LOCAL AXES
-----------------
Every modular piece is authored in Unity-local coordinates with:

    +Z = forward along the route        +X = rider's right        +Y = up
    origin = carriageway centreline at deck level (or at the top of the piece, for piers)

so Unity can place it with ``Quaternion.LookRotation(tangent, up)`` and no correction.
"""

import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402

u2b = S.u2b


# --------------------------------------------------------------------------- paths

def minato_dir(*parts):
    d = os.path.join(S.repo_root(), "Assets", "Environment", "MinatoCoast", *parts)
    os.makedirs(d, exist_ok=True)
    return d


def models_dir():
    return minato_dir("Models")


def blend_dir():
    return minato_dir("Blender")


def textures_dir():
    return minato_dir("Textures")


# --------------------------------------------------------------------------- export

def export_fbx(objs, filename, verbose=True):
    """
    Export to ``Assets/Environment/MinatoCoast/Models/<filename>``.

    Settings are the measured "clean root" variant - see the module docstring. Modifiers are
    applied on export (``use_mesh_modifiers``) so bevels and welds reach Unity as real geometry,
    and ``use_tspace`` writes tangents so normal maps light correctly without Unity recomputing
    them against a different basis.
    """
    if not isinstance(objs, (list, tuple)):
        objs = [objs]
    objs = [o for o in objs if o is not None]
    if not objs:
        raise ValueError(f"export_fbx({filename}): nothing to export")
    S.select_only(objs)

    path = os.path.join(models_dir(), filename)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        object_types={'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_tspace=True,
        # Triangulate on export: cap_open_boundaries legitimately produces ngons (pier bases,
        # cutwaters, prism end caps) and the FBX writer refuses to compute tangent space for
        # any mesh containing one - silently shipping normal-map-broken geometry. Unity
        # triangulates at import anyway, so this costs nothing and removes the whole class.
        use_triangles=True,
        bake_space_transform=True,
        add_leaf_bones=False,
        path_mode='COPY',
        embed_textures=False,
    )

    tri = 0
    for o in objs:
        if o.type == 'MESH':
            o.data.calc_loop_triangles()
            tri += len(o.data.loop_triangles)
    if verbose:
        kb = os.path.getsize(path) / 1024.0
        print(f"[minato] exported {filename:<40s} {kb:>9,.0f} KB  ~{tri:>8,} tris")
    return path


def save_blend(filename):
    """Save the current scene as the authored source under MinatoCoast/Blender."""
    path = os.path.join(blend_dir(), filename)
    bpy.ops.wm.save_as_mainfile(filepath=path, copy=True)
    print(f"[minato] saved source {filename} ({os.path.getsize(path) / 1024.0:,.0f} KB)")
    return path


# --------------------------------------------------------------------------- maths

def norm(v):
    v = Vector(v)
    return v.normalized() if v.length > 1e-9 else Vector((0.0, 0.0, 1.0))


def frame_from(direction, up_hint=(0.0, 1.0, 0.0)):
    """Right/up/forward orthonormal frame for a member pointing along ``direction``."""
    f = norm(direction)
    up = Vector(up_hint)
    if abs(f.dot(norm(up))) > 0.98:
        up = Vector((1.0, 0.0, 0.0))
        if abs(f.dot(up)) > 0.98:
            up = Vector((0.0, 0.0, 1.0))
    r = f.cross(up).normalized()
    u = r.cross(f).normalized()
    return r, u, f


# --------------------------------------------------------------------------- solids
#
# Everything below takes and returns UNITY coordinates and converts at mesh build time.

_BOX_FACES = [
    (0, 3, 2, 1),  # -F cap
    (4, 5, 6, 7),  # +F cap
    (0, 1, 5, 4),  # -U
    (1, 2, 6, 5),  # +R
    (2, 3, 7, 6),  # +U
    (3, 0, 4, 7),  # -R
]


def beam(name, a, b, width, height, up_hint=(0.0, 1.0, 0.0), uv_per_m=0.35,
         taper=1.0, smooth=False):
    """
    A box-section structural member running from Unity point ``a`` to Unity point ``b``.

    This is the workhorse: arch rib segments, truss chords and diagonals, crane legs, tower
    lattice, pier columns, railing posts and guardrail posts are all beams. ``taper`` scales the
    section at the ``b`` end, which is what gives the arch ribs and the piers their real
    structural silhouette instead of a constant extrusion.
    """
    a = Vector(a)
    b = Vector(b)
    r, u, f = frame_from(b - a, up_hint)
    length = (b - a).length

    hw0, hh0 = width * 0.5, height * 0.5
    hw1, hh1 = hw0 * taper, hh0 * taper

    verts = []
    for (origin, hw, hh) in ((a, hw0, hh0), (b, hw1, hh1)):
        verts += [
            origin - r * hw - u * hh,
            origin + r * hw - u * hh,
            origin + r * hw + u * hh,
            origin - r * hw + u * hh,
        ]

    uvs = []
    for fi, face in enumerate(_BOX_FACES):
        if fi < 2:
            w, h = width, height
        elif fi in (2, 4):
            w, h = width, length
        else:
            w, h = height, length
        uvs += [(0.0, 0.0), (w * uv_per_m, 0.0),
                (w * uv_per_m, h * uv_per_m), (0.0, h * uv_per_m)]

    obj = S.mesh_from_arrays(name, [u2b(*v) for v in verts], _BOX_FACES,
                             uvs=uvs, smooth=smooth)
    S.recalc_normals(obj)
    return obj


def box(name, centre, size, uv_per_m=0.35, smooth=False):
    """Axis-aligned Unity-space box given its centre and full size."""
    cx, cy, cz = centre
    sx, sy, sz = size
    return beam(name, (cx, cy, cz - sz * 0.5), (cx, cy, cz + sz * 0.5),
                sx, sy, uv_per_m=uv_per_m, smooth=smooth)


def tube(name, a, b, radius, segments=10, up_hint=(0.0, 1.0, 0.0), uv_per_m=0.5,
         taper=1.0, cap=True):
    """A cylindrical member - hangers, handrails, masts, pipes, small trunks."""
    a = Vector(a)
    b = Vector(b)
    r, u, f = frame_from(b - a, up_hint)
    length = (b - a).length

    verts = []
    for (origin, rad) in ((a, radius), (b, radius * taper)):
        for s in range(segments):
            ang = s / segments * math.tau
            verts.append(origin + r * (math.cos(ang) * rad) + u * (math.sin(ang) * rad))

    faces = []
    uvs = []
    for s in range(segments):
        s1 = (s + 1) % segments
        faces.append((s, s1, segments + s1, segments + s))
        u0 = s / segments * math.tau * radius * uv_per_m
        u1 = (s + 1) / segments * math.tau * radius * uv_per_m
        v1 = length * uv_per_m
        uvs += [(u0, 0.0), (u1, 0.0), (u1, v1), (u0, v1)]

    obj = S.mesh_from_arrays(name, [u2b(*v) for v in verts], faces, uvs=uvs, smooth=True)
    if cap:
        S.cap_open_boundaries(obj)
    S.recalc_normals(obj)
    return obj


def prism(name, section, a, b, up_hint=(0.0, 1.0, 0.0), uv_per_m=0.4, close=True,
          smooth=False, taper=1.0):
    """
    Extrude an arbitrary 2D ``section`` - a list of (right, up) pairs in metres - from Unity
    point ``a`` to ``b``. Used for kerbs, parapets, I-sections, roof profiles and cutwaters.
    """
    a = Vector(a)
    b = Vector(b)
    r, u, f = frame_from(b - a, up_hint)
    length = (b - a).length
    n = len(section)

    verts = []
    for (origin, sc) in ((a, 1.0), (b, taper)):
        for (sr, su) in section:
            verts.append(origin + r * (sr * sc) + u * (su * sc))

    faces = []
    uvs = []
    span = n if close else n - 1
    peri = 0.0
    edges = []
    for j in range(span):
        j1 = (j + 1) % n
        seg = math.hypot(section[j1][0] - section[j][0], section[j1][1] - section[j][1])
        edges.append((peri, peri + seg))
        peri += seg
    for j in range(span):
        j1 = (j + 1) % n
        faces.append((j, j1, n + j1, n + j))
        u0, u1 = edges[j]
        uvs += [(u0 * uv_per_m, 0.0), (u1 * uv_per_m, 0.0),
                (u1 * uv_per_m, length * uv_per_m), (u0 * uv_per_m, length * uv_per_m)]

    obj = S.mesh_from_arrays(name, [u2b(*v) for v in verts], faces, uvs=uvs, smooth=smooth)
    if close:
        S.cap_open_boundaries(obj)
    S.recalc_normals(obj)
    return obj


def plate(name, corners, uv_per_m=0.4):
    """A single quad from four Unity-space corners - steel web plates, gussets, signs."""
    obj = S.mesh_from_arrays(
        name, [u2b(*c) for c in corners], [(0, 1, 2, 3)],
        uvs=[(0.0, 0.0), (1.0 * uv_per_m, 0.0), (1.0 * uv_per_m, 1.0 * uv_per_m),
             (0.0, 1.0 * uv_per_m)],
        smooth=False)
    return obj


# --------------------------------------------------------------------------- curves

def arch_points(span, rise, n, y0=0.0, x=0.0, z0=0.0, shape="parabola"):
    """
    Centreline of an arch rib as ``n`` Unity points, springing at ``z0`` and ``z0 + span``.

    A parabola is the funicular shape for a uniformly loaded deck, which is why real
    through-arch bridges look like one; the circular option is kept for the shallower
    approach arches.
    """
    pts = []
    for i in range(n):
        f = i / (n - 1)
        z = z0 + f * span
        if shape == "circle":
            r = (span * span / 4.0 + rise * rise) / (2.0 * rise)
            y = math.sqrt(max(r * r - (f - 0.5) ** 2 * span * span, 0.0)) - (r - rise)
        else:
            y = rise * (1.0 - (2.0 * f - 1.0) ** 2)
        pts.append((x, y0 + y, z))
    return pts


def lattice_between(name, chord_a, chord_b, thickness, every=2, up_hint=(0, 1, 0),
                    uv_per_m=0.5):
    """
    Web bracing between two parallel chord polylines: the alternating diagonals plus verticals
    that make a truss read as a truss rather than as two bent tubes.
    """
    parts = []
    n = min(len(chord_a), len(chord_b))
    for i in range(0, n - every, every):
        a0, b0 = Vector(chord_a[i]), Vector(chord_b[i])
        a1, b1 = Vector(chord_a[i + every]), Vector(chord_b[i + every])
        parts.append(beam(f"{name}_v{i}", a0, b0, thickness, thickness,
                          up_hint=up_hint, uv_per_m=uv_per_m))
        if (i // every) % 2 == 0:
            parts.append(beam(f"{name}_d{i}", a0, b1, thickness, thickness,
                              up_hint=up_hint, uv_per_m=uv_per_m))
        else:
            parts.append(beam(f"{name}_d{i}", b0, a1, thickness, thickness,
                              up_hint=up_hint, uv_per_m=uv_per_m))
    last = n - 1
    parts.append(beam(f"{name}_vEnd", Vector(chord_a[last]), Vector(chord_b[last]),
                      thickness, thickness, up_hint=up_hint, uv_per_m=uv_per_m))
    return parts


def polyline_beams(name, points, width, height, up_hint=(0, 1, 0), uv_per_m=0.35,
                   taper_profile=None):
    """Chain of beams following a polyline - an arch rib, a crane boom, a cable run."""
    parts = []
    for i in range(len(points) - 1):
        t = 1.0
        if taper_profile is not None:
            t = taper_profile(i + 1) / max(taper_profile(i), 1e-6)
        s = 1.0 if taper_profile is None else taper_profile(i)
        parts.append(beam(f"{name}_{i}", points[i], points[i + 1],
                          width * s, height * s, up_hint=up_hint,
                          uv_per_m=uv_per_m, taper=t))
    return parts


# --------------------------------------------------------------------------- finishing

def _ensure_smooth_by_angle():
    """
    Make ``shade_auto_smooth`` actually work under ``--factory-startup``.

    ``bpy.ops.object.shade_auto_smooth`` resolves its geometry-node group through the
    *essentials* asset library, which is not indexed when Blender starts with
    ``--factory-startup`` - the operator then fails with "No asset found at path ...
    Smooth by Angle". ``sakura_lib.shade_auto_smooth`` falls back to looking the node group up
    in ``bpy.data``, so appending it once from the bundled .blend is enough to restore correct
    behaviour for the whole run.

    This matters more than it sounds: without it, ``sakura_lib.shade_auto_smooth`` still runs
    its unconditional ``obj.data.shade_smooth()`` and then fails to add the angle split, so
    every box section ships FULLY smooth and 90 deg structural corners render as soft smears.
    That is a defect no log line reports.

    Deliberately re-checked on every call rather than latched behind a one-shot flag:
    ``sakura_lib.reset_scene()`` purges orphaned data between assets, so the appended node
    group does not survive from one module to the next.
    """
    if bpy.data.node_groups.get("Smooth by Angle") is not None:
        return
    blend = os.path.join(os.path.dirname(bpy.app.binary_path), bpy.app.version_string[:3].strip(),
                         "datafiles", "assets", "geometry_nodes", "smooth_by_angle.blend")
    if not os.path.exists(blend):
        print("[minato] WARNING: smooth_by_angle.blend not found; meshes will ship fully smooth")
        return
    try:
        with bpy.data.libraries.load(blend, link=False) as (src, dst):
            dst.node_groups = [n for n in src.node_groups if n == "Smooth by Angle"]
        ok = bpy.data.node_groups.get("Smooth by Angle") is not None
        print(f"[minato] smooth-by-angle node group {'appended' if ok else 'MISSING'}")
    except Exception as exc:  # pragma: no cover
        print(f"[minato] WARNING: could not append Smooth by Angle: {exc}")


def finish(parts, name, bevel=0.02, bevel_segments=1, weld=0.0008, smooth_angle=None):
    """
    Join, weld, bevel and (optionally) auto-smooth a set of parts into one shippable mesh.

    The bevel is not decoration: an unbevelled edge catches zero specular and is the single
    strongest "this is programmer art" tell at the close camera distances a cycling game uses.
    """
    obj = S.join([p for p in parts if p is not None], name)
    if obj is None:
        return None
    if weld:
        S.add_weld(obj, weld)
    if bevel:
        S.add_bevel(obj, width=bevel, segments=bevel_segments, angle_deg=35.0)
    S.apply_modifiers(obj)
    if smooth_angle is not None:
        _ensure_smooth_by_angle()
        S.shade_auto_smooth(obj, smooth_angle)
        S.apply_modifiers(obj)   # bake the Smooth-by-Angle node group into real sharp edges
    obj.data.calc_loop_triangles()
    return obj


def decimated_copy(obj, name, ratio):
    """A LOD mesh: collapse-decimated copy of ``obj``, used for the repeated bridge pieces."""
    lod = obj.copy()
    lod.data = obj.data.copy()
    lod.name = name
    lod.data.name = name + "_Mesh"
    S.link(lod)
    m = lod.modifiers.new("Decimate", 'DECIMATE')
    m.decimate_type = 'COLLAPSE'
    m.ratio = ratio
    S.apply_modifiers(lod)
    lod.data.calc_loop_triangles()
    return lod


def stat(obj, label=None):
    if obj is None or obj.type != 'MESH':
        return
    obj.data.calc_loop_triangles()
    bb = [Vector(c) for c in obj.bound_box]
    mn = Vector((min(c.x for c in bb), min(c.y for c in bb), min(c.z for c in bb)))
    mx = Vector((max(c.x for c in bb), max(c.y for c in bb), max(c.z for c in bb)))
    size = mx - mn
    print(f"[minato]   {label or obj.name:<34s} "
          f"{len(obj.data.vertices):>7,} v / {len(obj.data.loop_triangles):>7,} t   "
          f"bbox {size.x:6.1f} x {size.y:6.1f} x {size.z:6.1f} (blender)")


# --------------------------------------------------------------------------- materials
#
# Slot names matter: the Unity staging pass assigns its own HDRP materials BY SLOT NAME, so a
# piece with "steel"/"concrete"/"glass" slots gets the right surface without per-asset wiring.

PALETTE = {
    # Saturated, weathered coral-vermilion - the bridge's identity colour, read off the
    # concept renders. PROVISIONAL exact value; the weathering is done in the Unity shader.
    "steel_vermilion": (0.776, 0.243, 0.180, 1.0),
    "steel_grey":      (0.560, 0.580, 0.600, 1.0),
    "steel_dark":      (0.230, 0.245, 0.260, 1.0),
    "concrete":        (0.735, 0.720, 0.690, 1.0),
    "concrete_wet":    (0.560, 0.550, 0.530, 1.0),
    "asphalt":         (0.180, 0.180, 0.190, 1.0),
    "glass":           (0.290, 0.420, 0.520, 1.0),
    "white_render":    (0.900, 0.890, 0.860, 1.0),
    "terracotta":      (0.690, 0.330, 0.230, 1.0),
    "roof_slate":      (0.260, 0.280, 0.320, 1.0),
    "timber":          (0.420, 0.310, 0.220, 1.0),
    "rock":            (0.480, 0.470, 0.450, 1.0),
    "foliage":         (0.240, 0.420, 0.220, 1.0),
    "foliage_warm":    (0.360, 0.480, 0.220, 1.0),
    "hull_white":      (0.870, 0.880, 0.880, 1.0),
    "hull_blue":       (0.140, 0.260, 0.420, 1.0),
    "rust":            (0.430, 0.230, 0.150, 1.0),
    "lamp_grey":       (0.650, 0.660, 0.670, 1.0),
    "sand":            (0.780, 0.720, 0.600, 1.0),

    # --- BRIGHT MODERN CITY / CROWD SLOTS -------------------------------------------------
    # Added for the "lively bright bustling port city" pass. These exist so the Unity staging
    # pass can key its OWN materials off the imported slot NAME (see MinatoCoastEnvironment
    # .RetintTower / .RetintFigure); the colours here are only what Blender previews.
    # ALL PROVISIONAL illustrative tuning.
    "glass_bright":    (0.520, 0.760, 0.900, 1.0),   # daylight curtain wall
    "glass_teal":      (0.380, 0.720, 0.700, 1.0),
    "tower_frame":     (0.880, 0.895, 0.910, 1.0),   # white/silver mullions + spandrels
    "tower_crown":     (0.760, 0.800, 0.840, 1.0),
    "skin":            (0.960, 0.800, 0.690, 1.0),
    "hair":            (0.180, 0.150, 0.150, 1.0),
    "cloth_top":       (0.920, 0.280, 0.260, 1.0),   # retinted per instance in Unity
    "cloth_leg":       (0.200, 0.320, 0.580, 1.0),
    "bike_frame":      (0.950, 0.870, 0.200, 1.0),
    "parasol":         (0.980, 0.420, 0.320, 1.0),

    # --- BOULEVARD DRESSING SLOTS ---------------------------------------------------------
    # Added for the "After" concept recreation (banner flags + flowering palm median). As
    # above, these are Blender preview colours only: MinatoCoastEnvironment.RetintBoulevard
    # keys its own cel materials off the imported slot NAME. ALL PROVISIONAL.
    "soil_bed":        (0.230, 0.160, 0.120, 1.0),
    "blossom_pink":    (0.960, 0.430, 0.620, 1.0),
    "blossom_red":     (0.880, 0.180, 0.220, 1.0),
    "blossom_magenta": (0.820, 0.150, 0.560, 1.0),
    "blossom_white":   (0.980, 0.970, 0.950, 1.0),
    "banner_cloth":    (0.100, 0.300, 0.660, 1.0),

    # --- CAFE / MARKET DISTRICT SLOTS -----------------------------------------------------
    # Added for the "After" board's Mediterranean market promenade (milestone 2). The striped
    # awnings and parasols are STRIPED IN GEOMETRY, not in a texture: alternating spanwise
    # panels carry the "*_a" and "*_cream" slots, so the stripe stays razor crisp at the
    # 20-200 m distances the plaza is actually seen from and survives mip filtering, which a
    # 256 px stripe texture does not. As above these are Blender preview colours only -
    # MinatoCoastEnvironment.RetintMarket keys its own cel materials off the slot NAME.
    # ALL PROVISIONAL illustrative tuning, read off Minamo_01 / Minamo_04.
    "awning_blue":     (0.130, 0.420, 0.780, 1.0),
    "awning_red":      (0.840, 0.200, 0.200, 1.0),
    "awning_cream":    (0.970, 0.950, 0.905, 1.0),
    # A second, warmer cream so the PLAIN cream parasol still shows wedge seams. With one
    # unbroken cream canvas the cel shader gives every wedge the same flat value and the dome
    # collapses to a white disc against the white plaza tile (seen on the M2 contact frame).
    "awning_sand":     (0.905, 0.855, 0.760, 1.0),
    "produce_red":     (0.860, 0.200, 0.180, 1.0),
    "produce_green":   (0.420, 0.700, 0.260, 1.0),
    "produce_orange":  (0.960, 0.600, 0.140, 1.0),
}

_ROUGH = {
    "glass": 0.12,
    "glass_bright": 0.08,
    "glass_teal": 0.10,
    "tower_frame": 0.30,
    "tower_crown": 0.34,
    "steel_vermilion": 0.55,
    "steel_grey": 0.42,
    "steel_dark": 0.45,
    "hull_white": 0.35,
    "hull_blue": 0.35,
}


def mat(slot):
    """Get-or-create the Principled material for a palette slot."""
    name = f"Minato_{slot}"
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    return S.pbr_material(
        name,
        base_color=PALETTE.get(slot, (0.8, 0.8, 0.8, 1.0)),
        roughness=_ROUGH.get(slot, 0.78),
        metallic=0.9 if slot.startswith("steel") else 0.0,
    )


def paint(objs, slot):
    """Assign a palette slot to one or more parts."""
    if not isinstance(objs, (list, tuple)):
        objs = [objs]
    m = mat(slot)
    for o in objs:
        if o is not None:
            S.assign_material(o, m)
    return objs


def multi_material(obj, slots):
    """Ensure ``obj`` carries the given material slots in order (for multi-surface pieces)."""
    obj.data.materials.clear()
    for s in slots:
        obj.data.materials.append(mat(s))
    return obj


# --------------------------------------------------------------------------- scatter aid

def rng(seed):
    return np.random.default_rng(seed)

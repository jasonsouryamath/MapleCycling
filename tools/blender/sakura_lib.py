"""
Shared helpers for the MapleRide / Sakura Pass Blender asset pipeline.

Everything here exists so the build scripts can author *real* geometry - swept profiles, bevelled
solids, displaced landmasses, scattered foliage - and ship it to Unity as textured glTF. Nothing in
this pipeline emits a raw Blender primitive as a finished asset; primitives are only ever used as a
starting cage that is then modelled, bevelled, displaced and UV unwrapped.

Coordinate convention
---------------------
Unity is Y-up / left-handed, Blender is Z-up / right-handed. With the glTF exporter's default
"+Y up" conversion the round trip works out to a simple axis swap:

    unity(x, y, z)  <->  blender(x, z, y)

Author in Blender using ``u2b()`` and an asset lands at the intended Unity world position with no
correction rotation on the Unity side.
"""

import math
import os

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix


# --------------------------------------------------------------------------- paths

def repo_root():
    """MapleRide/ - two levels above tools/blender/."""
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", ".."))


def blender_assets_dir():
    d = os.path.join(repo_root(), "Assets", "Environment",
                     "SakuraPass", "BlenderAssets")
    os.makedirs(d, exist_ok=True)
    return d


def textures_dir():
    d = os.path.join(repo_root(), "Assets", "Environment",
                     "SakuraPass", "Textures")
    os.makedirs(d, exist_ok=True)
    return d


# ------------------------------------------------------------------ coordinate swap

def u2b(x, y, z):
    """
    Unity (x, y, z) -> Blender (x, y, z).

    Measured round trip: a Blender vertex ``(bx, by, bz)`` lands in Unity at
    ``(-bx, bz, -by)`` once the glTF export/import pair has had its way with it. The naive
    ``(x, z, y)`` swap therefore mirrored the whole world in **both** X and Z - invisible
    asset-by-asset (the terrain is symmetric in X) but it put the built world back-to-front
    against the route JSON that Unity places props and the rider against.

    ``(-x, -z, y)`` makes the round trip the identity. It differs from the old swap by a 180
    degree spin about the Blender Z axis, so handedness - and therefore every existing face
    winding - is unchanged.
    """
    return (-x, -z, y)


def u2b_vec(v):
    return Vector(u2b(v[0], v[1], v[2]))


# ------------------------------------------------------------------- scene handling

def reset_scene():
    """Factory-clean scene: no cube, no camera, no light, no orphan data."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images,
                  bpy.data.textures, bpy.data.curves, bpy.data.objects):
        for item in list(block):
            try:
                block.remove(item)
            except Exception:
                pass
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0


def select_only(objs):
    if not isinstance(objs, (list, tuple)):
        objs = [objs]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    if objs:
        bpy.context.view_layer.objects.active = objs[0]
    return objs


def link(obj):
    bpy.context.collection.objects.link(obj)
    return obj


# ------------------------------------------------------------------------- noise

def _lattice(shape, seed, period=None):
    rng = np.random.default_rng(seed)
    g = rng.random(shape)
    if period is not None:
        # Wrap the lattice so the resulting field tiles seamlessly.
        g[-1, :] = g[0, :]
        g[:, -1] = g[:, 0]
    return g


def value_noise_2d(x, y, freq, seed=0, tile=None):
    """
    Smooth value noise sampled at arbitrary float coordinates.
    ``tile`` makes the field periodic with that world-space period (used for seamless textures).
    """
    if tile is not None:
        period = max(2, int(round(freq * tile)))
        gx = (x * freq) % period
        gy = (y * freq) % period
        size = period + 1
    else:
        gx = x * freq
        gy = y * freq
        size = int(max(np.max(gx), np.max(gy))) + 3

    grid = _lattice((size + 1, size + 1), seed, period=tile)

    x0 = np.floor(gx).astype(np.int64)
    y0 = np.floor(gy).astype(np.int64)
    fx = gx - x0
    fy = gy - y0

    if tile is not None:
        x0 %= period
        y0 %= period
        x1 = (x0 + 1) % period
        y1 = (y0 + 1) % period
    else:
        x0 %= size
        y0 %= size
        x1 = (x0 + 1) % size
        y1 = (y0 + 1) % size

    # Quintic smoothstep - C2 continuous, no visible lattice creases.
    u = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
    v = fy * fy * fy * (fy * (fy * 6 - 15) + 10)

    n00 = grid[x0, y0]
    n10 = grid[x1, y0]
    n01 = grid[x0, y1]
    n11 = grid[x1, y1]

    return (n00 * (1 - u) * (1 - v) + n10 * u * (1 - v) +
            n01 * (1 - u) * v + n11 * u * v)


def fbm_2d(x, y, freq, octaves=5, lacunarity=2.0, gain=0.5, seed=0, tile=None):
    total = np.zeros_like(np.asarray(x, dtype=np.float64))
    amp = 1.0
    norm = 0.0
    f = freq
    for o in range(octaves):
        total += value_noise_2d(x, y, f, seed=seed + o * 977, tile=tile) * amp
        norm += amp
        amp *= gain
        f *= lacunarity
    return total / max(norm, 1e-9)


def ridged_2d(x, y, freq, octaves=5, seed=0, tile=None):
    """Ridged multifractal - gives mountains sharp crests instead of blobby hills."""
    total = np.zeros_like(np.asarray(x, dtype=np.float64))
    amp = 1.0
    norm = 0.0
    f = freq
    for o in range(octaves):
        n = value_noise_2d(x, y, f, seed=seed + o * 1381, tile=tile)
        n = 1.0 - np.abs(n * 2.0 - 1.0)
        total += (n ** 2) * amp
        norm += amp
        amp *= 0.5
        f *= 2.07
    return total / max(norm, 1e-9)


# --------------------------------------------------------------------- mesh building

def mesh_from_arrays(name, verts, faces, uvs=None, uv2=None, uv3=None, smooth=True):
    """
    Build a mesh from flat python/numpy arrays.
    ``uvs`` / ``uv2`` / ``uv3`` are per-loop (face-corner) coordinates.
    """
    faces = [tuple(f) for f in faces]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.validate(verbose=False)
    mesh.update()

    if uvs is not None:
        layer = mesh.uv_layers.new(name="UVMap")
        flat = np.asarray(uvs, dtype=np.float32).reshape(-1)
        layer.data.foreach_set("uv", flat)

    if uv2 is not None:
        layer2 = mesh.uv_layers.new(name="Splat")
        flat2 = np.asarray(uv2, dtype=np.float32).reshape(-1)
        layer2.data.foreach_set("uv", flat2)

    # A third set carries the remaining splat weights. Weights travel through UV rather than
    # vertex colour because glTF runs COLOR_0 through an sRGB conversion that would corrupt
    # them; a UV set is passed through untouched.
    if uv3 is not None:
        layer3 = mesh.uv_layers.new(name="Splat2")
        flat3 = np.asarray(uv3, dtype=np.float32).reshape(-1)
        layer3.data.foreach_set("uv", flat3)

    if smooth:
        mesh.shade_smooth()

    obj = bpy.data.objects.new(name, mesh)
    link(obj)
    return obj


def grid_faces(nx, nz):
    """
    Quad indices for an (nx+1) x (nz+1) vertex grid laid out row-major in j, wound CCW
    when viewed from +Z in Blender space - i.e. upward-facing once exported.
    """
    faces = []
    for i in range(nx):
        for j in range(nz):
            a = j * (nx + 1) + i
            b = a + 1
            c = a + (nx + 1)
            d = c + 1
            faces.append((a, b, d, c))
    return faces


def sweep_profile(name, profile, path, up=Vector((0, 0, 1)), close_profile=False,
                  cap_ends=True, uv_scale=(1.0, 1.0)):
    """
    Sweep a 2D profile (list of (across, up) pairs, in metres) along a 3D path.

    This is how the guardrail W-beam, the road surface and the retaining wall coping are built -
    real extruded cross-sections rather than scaled cubes.
    """
    n_path = len(path)
    n_prof = len(profile)
    if n_path < 2 or n_prof < 2:
        raise ValueError("sweep_profile needs at least 2 path points and 2 profile points")

    verts = []
    uvs_per_face = []

    # Accumulated arc length drives the V coordinate so textures do not stretch on curves.
    arc = [0.0]
    for i in range(1, n_path):
        arc.append(arc[-1] + (Vector(path[i]) - Vector(path[i - 1])).length)

    frames = []
    for i in range(n_path):
        if i == 0:
            tangent = Vector(path[1]) - Vector(path[0])
        elif i == n_path - 1:
            tangent = Vector(path[-1]) - Vector(path[-2])
        else:
            tangent = Vector(path[i + 1]) - Vector(path[i - 1])
        tangent.normalize()
        side = tangent.cross(up)
        if side.length < 1e-6:
            side = Vector((1, 0, 0))
        side.normalize()
        real_up = side.cross(tangent).normalized()
        frames.append((Vector(path[i]), side, real_up))

    for (origin, side, real_up) in frames:
        for (a, u_) in profile:
            verts.append(tuple(origin + side * a + real_up * u_))

    faces = []
    prof_span = n_prof if close_profile else n_prof - 1
    for i in range(n_path - 1):
        for j in range(prof_span):
            j0 = j
            j1 = (j + 1) % n_prof
            a = i * n_prof + j0
            b = i * n_prof + j1
            c = (i + 1) * n_prof + j1
            d = (i + 1) * n_prof + j0
            faces.append((a, b, c, d))

            # Profile-relative U, arc-length V.
            u0 = j0 / float(n_prof - 1) * uv_scale[0]
            u1 = (j0 + 1) / float(n_prof - 1) * uv_scale[0]
            v0 = arc[i] * uv_scale[1]
            v1 = arc[i + 1] * uv_scale[1]
            uvs_per_face.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    obj = mesh_from_arrays(name, verts, faces, uvs=uvs_per_face)
    if cap_ends and close_profile:
        cap_open_boundaries(obj)
    return obj


def cap_open_boundaries(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        res = bmesh.ops.holes_fill(bm, edges=edges)
        # glTF cannot compute a tangent basis for ngons, so fan the fill faces into tris.
        new_faces = [f for f in res.get("faces", []) if f.is_valid and len(f.verts) > 4]
        if new_faces:
            bmesh.ops.triangulate(bm, faces=new_faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def lathe(name, profile, segments=32, axis='Z', uv_scale=(1.0, 1.0)):
    """
    Revolve a 2D profile (list of (radius, height)) around an axis.
    Used for lantern bodies, post caps and tree trunk sections.
    """
    verts = []
    uvs = []
    n = len(profile)
    for s in range(segments):
        a = s / float(segments) * math.tau
        ca, sa = math.cos(a), math.sin(a)
        for (r, h) in profile:
            if axis == 'Z':
                verts.append((ca * r, sa * r, h))
            else:
                verts.append((h, ca * r, sa * r))

    faces = []
    for s in range(segments):
        s2 = (s + 1) % segments
        for j in range(n - 1):
            a = s * n + j
            b = s2 * n + j
            c = s2 * n + j + 1
            d = s * n + j + 1
            faces.append((a, b, c, d))
            u0 = s / float(segments) * uv_scale[0]
            u1 = (s + 1) / float(segments) * uv_scale[0]
            v0 = j / float(n - 1) * uv_scale[1]
            v1 = (j + 1) / float(n - 1) * uv_scale[1]
            uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    obj = mesh_from_arrays(name, verts, faces, uvs=uvs)
    cap_open_boundaries(obj)
    recalc_normals(obj)
    return obj


def recalc_normals(obj):
    """
    Make every face point outward - lathed profiles can wind either way.

    `bmesh.ops.recalc_face_normals` decides "outward" by flood-filling across each connected
    island of faces (walking shared edges) and only falls back to a lone per-face heuristic
    when a face has no edge-connected neighbours. Builders that add each face as its own
    independent, unwelded set of vertices (the norm in this codebase, so hard flat shading and
    per-face UVs stay simple) leave bmesh with zero shared edges between physically-adjacent
    faces even though their vertices sit at identical positions - every face becomes its own
    1-face "island". On a simple convex blob that per-face fallback still points outward often
    enough to look right, but on a concave/undercut silhouette (e.g. the sea-stack rock spires)
    it gets a large fraction of faces backwards, and Unity's default back-face culling then
    turns those into gaping holes that read as an "exploded"/birdcage mesh - even though the
    same geometry looks solid in a renderer that shows backfaces. Weld coincident vertices
    first (by-distance merge) so bmesh can see true face adjacency and flood-fill a consistent
    "outward" across the whole solid, THEN recalc; the temporary merge does not affect the
    final flat/hard-shaded look because per-face flat shading and UVs are independent of
    whether vertices happen to be shared in the intermediate bmesh.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


# ---------------------------------------------------------------------- modifiers

def add_bevel(obj, width=0.01, segments=2, angle_deg=40.0):
    """Bevelled edges are the single cheapest way to stop geometry reading as programmer-art."""
    m = obj.modifiers.new("Bevel", 'BEVEL')
    m.width = width
    m.segments = segments
    m.limit_method = 'ANGLE'
    m.angle_limit = math.radians(angle_deg)
    m.harden_normals = False
    return m


def add_subsurf(obj, levels=1, render=2):
    m = obj.modifiers.new("Subdivision", 'SUBSURF')
    m.levels = levels
    m.render_levels = render
    return m


def add_displace(obj, tex_type='CLOUDS', strength=0.1, size=1.0, seed=0, midlevel=0.5):
    tex = bpy.data.textures.new(f"{obj.name}_disp", type=tex_type)
    if hasattr(tex, "noise_scale"):
        tex.noise_scale = size
    if hasattr(tex, "noise_depth"):
        tex.noise_depth = 4
    m = obj.modifiers.new("Displace", 'DISPLACE')
    m.texture = tex
    m.strength = strength
    m.mid_level = midlevel
    m.texture_coords = 'LOCAL'
    return m


def add_solidify(obj, thickness=0.02, offset=-1.0):
    m = obj.modifiers.new("Solidify", 'SOLIDIFY')
    m.thickness = thickness
    m.offset = offset
    return m


def add_weld(obj, distance=0.0005):
    m = obj.modifiers.new("Weld", 'WELD')
    m.merge_threshold = distance
    return m


def shade_auto_smooth(obj, angle_deg=35.0):
    """Blender 4.1+ replaced mesh.auto_smooth_angle with a 'Smooth by Angle' modifier."""
    select_only(obj)
    obj.data.shade_smooth()
    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(angle_deg))
    except Exception:
        m = obj.modifiers.new("Smooth by Angle", 'NODES')
        try:
            ng = bpy.data.node_groups.get("Smooth by Angle")
            if ng:
                m.node_group = ng
        except Exception:
            obj.modifiers.remove(m)


def apply_modifiers(obj):
    select_only(obj)
    for m in list(obj.modifiers):
        try:
            bpy.ops.object.modifier_apply(modifier=m.name)
        except Exception:
            obj.modifiers.remove(m)


def join(objs, name):
    objs = [o for o in objs if o is not None]
    if not objs:
        return None
    if len(objs) == 1:
        objs[0].name = name
        return objs[0]
    select_only(objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    result = bpy.context.view_layer.objects.active
    result.name = name
    result.data.name = name + "_Mesh"
    return result


# ---------------------------------------------------------------------- materials

def pbr_material(name, base_color=(0.8, 0.8, 0.8, 1.0), roughness=0.8, metallic=0.0,
                 emission=None, emission_strength=0.0, alpha_blend=False):
    """A clean glTF-exportable Principled material."""
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (400, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (0, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    bsdf.inputs["Base Color"].default_value = base_color
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission is not None and "Emission Color" in bsdf.inputs:
        bsdf.inputs["Emission Color"].default_value = emission
        bsdf.inputs["Emission Strength"].default_value = emission_strength

    if alpha_blend:
        mat.blend_method = 'BLEND' if hasattr(mat, "blend_method") else mat.blend_method

    mat["_sakura_bsdf"] = bsdf.name
    return mat


def set_texture(mat, image, slot="Base Color", non_color=False):
    """Wire an image into a Principled input, adding a Normal Map node when needed."""
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        return None

    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.location = (-500, {"Base Color": 200, "Roughness": -100, "Normal": -400}.get(slot, 0))
    if non_color:
        tex.image.colorspace_settings.name = 'Non-Color'

    if slot == "Normal":
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.location = (-250, -400)
        nt.links.new(tex.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    else:
        nt.links.new(tex.outputs["Color"], bsdf.inputs[slot])
        if slot == "Base Color" and "Alpha" in bsdf.inputs:
            nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    return tex


def assign_material(obj, mat, slot=None):
    if slot is None:
        obj.data.materials.append(mat)
        return len(obj.data.materials) - 1
    while len(obj.data.materials) <= slot:
        obj.data.materials.append(None)
    obj.data.materials[slot] = mat
    return slot


def load_image(path, non_color=False):
    img = bpy.data.images.load(path, check_existing=True)
    if non_color:
        img.colorspace_settings.name = 'Non-Color'
    return img


# ------------------------------------------------------------------------ uv / bake

def smart_uv(obj, angle=66.0, margin=0.02):
    select_only(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')


# ------------------------------------------------------------------------- export

def export_glb(objs, filename, apply_modifiers_on_export=True):
    """
    Export the given objects to Assets/Environment/SakuraPass/BlenderAssets/<filename>.

    Kwargs are filtered against the operator's actual properties so the pipeline keeps working
    across Blender versions that rename export options.
    """
    if not isinstance(objs, (list, tuple)):
        objs = [objs]
    objs = [o for o in objs if o is not None]
    select_only(objs)

    path = os.path.join(blender_assets_dir(), filename)

    desired = dict(
        filepath=path,
        export_format='GLB',
        use_selection=True,
        export_apply=apply_modifiers_on_export,
        export_yup=True,
        export_normals=True,
        export_tangents=True,
        export_texcoords=True,
        export_materials='EXPORT',
        export_image_format='AUTO',
        export_vertex_color='MATERIAL',
        export_all_vertex_colors=False,
        export_attributes=True,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
        export_skins=False,
        export_morph=False,
        export_extras=True,
    )

    try:
        valid = set(bpy.ops.export_scene.gltf.get_rna_type().properties.keys())
        kwargs = {k: v for k, v in desired.items() if k in valid}
    except Exception:
        kwargs = desired

    bpy.ops.export_scene.gltf(**kwargs)

    size_kb = os.path.getsize(path) / 1024.0
    tri_total = 0
    for o in objs:
        if o.type == 'MESH':
            o.data.calc_loop_triangles()
            tri_total += len(o.data.loop_triangles)
    print(f"[sakura] exported {filename}  ({size_kb:,.0f} KB, ~{tri_total:,} tris)")
    return path


def report(label, obj):
    if obj is None or obj.type != 'MESH':
        return
    obj.data.calc_loop_triangles()
    print(f"[sakura]   {label}: {len(obj.data.vertices):,} verts / "
          f"{len(obj.data.loop_triangles):,} tris")

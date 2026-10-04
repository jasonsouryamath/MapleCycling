"""
M2 - MapleRide world-map art from a real 3D relief model (Blender 4.5, Cycles GPU).

    blender -b -P tools/blender/worldmap/build_world_relief.py -- [--preview] [--samples N]
            [--no-clouds] [--save-blend]

Run world_terrain.py first (it writes out/). This script:
  1. builds the terrain mesh from out/surf_m.npy (vertical exaggeration EX) with the albedo /
     roughness maps, a sea plane beyond the world edge and a Nishita sky;
  2. scatters trees (conifer, snow pine, broadleaf, sakura, autumn, bamboo, palm, dark pine),
     desert scrub, houses and Maple City's towers with Geometry Nodes, from the attr_*.npy
     densities;
  3. lays the A*-routed roads, the region ride routes (glowing, coloured by region type),
     bridges with pylons, dotted ferry lanes, boats and a few landmarks (Maple Tower, Minato
     wheel, Shiosai light, torii, Kaze wind turbines, a Sakura pagoda);
  4. frames a tilted ORTHOGRAPHIC camera (so a pin is an affine function of world position)
     and renders out/render.png;
  5. writes Assets/Resources/World/worldmap_v2_projection.json: the affine world(km, m) ->
     map-fraction projection plus every region's pin, which the Unity World Map uses.
No text is rendered: labels are live UI.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(HERE, "out")
LAYOUT = os.path.join(ROOT, "Assets", "Resources", "World", "world_layout.json")
PROJ_OUT = os.path.join(ROOT, "Assets", "Resources", "World", "worldmap_v2_projection.json")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = "--preview" in argv
CLOUDS = "--clouds" in argv
SAMPLES = int(argv[argv.index("--samples") + 1]) if "--samples" in argv else (32 if PREVIEW else 160)
RES_X, RES_Y = (1280, 800) if PREVIEW else (5120, 3200)
if "--size" in argv:
    RES_X = int(argv[argv.index("--size") + 1])
    RES_Y = RES_X * 5 // 8

EX = 7.5                      # vertical exaggeration (km per km)
TILT = 55.0                   # camera elevation above the horizon (deg)
FRAME_W = 372.0               # orthographic frame width (km)
FRAME_CY = 139.0              # ground y at the frame centre (km)
SEA_GLOW = 0.95               # unlit sea: emission x albedo (1.0 = the albedo colour exactly)

meta = json.load(open(os.path.join(OUT, "meta.json")))
L = json.load(open(LAYOUT, encoding="utf-8"))
R = json.load(open(os.path.join(OUT, "roads.json")))
W, H, RES = meta["W"], meta["H"], meta["res"]
X0, Y0 = meta.get("x0", 0.0), meta.get("y0", 0.0)
SURF = np.load(os.path.join(OUT, "surf_m.npy"))
NY, NX = SURF.shape


def z_at(x, y):
    fx = np.clip((x - X0) / RES, 0, NX - 1.001)
    fy = np.clip((y - Y0) / RES, 0, NY - 1.001)
    i, j = int(fx), int(fy)
    tx, ty = fx - i, fy - j
    a = SURF[j, i] * (1 - tx) + SURF[j, i + 1] * tx
    b = SURF[j + 1, i] * (1 - tx) + SURF[j + 1, i + 1] * tx
    return float(a * (1 - ty) + b * ty) / 1000.0 * EX


def srgb(c):
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c) + (1.0,)


# ============================================================================ scene
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'NONE'


def mat_principled(name, color, rough=0.7, emission=None, strength=0.0, metallic=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = srgb(color)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metallic
    if emission is not None:
        b.inputs["Emission Color"].default_value = srgb(emission)
        b.inputs["Emission Strength"].default_value = strength
    return m


def link(obj, coll=None):
    (coll or scene.collection).objects.link(obj)
    return obj


# ---------------------------------------------------------------------------- terrain
def build_terrain():
    xs = (X0 + np.arange(NX) * RES).astype(np.float32)
    ys = (Y0 + np.arange(NY) * RES).astype(np.float32)
    X, Y = np.meshgrid(xs, ys)
    Z = SURF / 1000.0 * EX
    verts = np.column_stack([X.ravel(), Y.ravel(), Z.ravel()]).astype(np.float32)
    j, i = np.mgrid[0:NY - 1, 0:NX - 1]
    v0 = (j * NX + i).ravel()
    quads = np.column_stack([v0, v0 + 1, v0 + NX + 1, v0 + NX]).astype(np.int32)
    nf = len(quads)
    me = bpy.data.meshes.new("Terrain")
    me.vertices.add(len(verts))
    me.vertices.foreach_set("co", verts.ravel())
    me.loops.add(nf * 4)
    me.loops.foreach_set("vertex_index", quads.ravel())
    me.polygons.add(nf)
    me.polygons.foreach_set("loop_start", (np.arange(nf, dtype=np.int32) * 4))
    try:
        me.polygons.foreach_set("loop_total", np.full(nf, 4, np.int32))
    except Exception:
        pass
    me.update(calc_edges=True)
    uvl = me.uv_layers.new(name="UVMap")
    uv = np.column_stack([(X.ravel() - X0) / (xs[-1] - X0), (Y.ravel() - Y0) / (ys[-1] - Y0)]).astype(np.float32)
    uvl.data.foreach_set("uv", uv[quads.ravel()].ravel())
    me.polygons.foreach_set("use_smooth", np.ones(nf, dtype=bool))
    for f in sorted(os.listdir(OUT)):
        if f.startswith("attr_") and f.endswith(".npy"):
            a = np.load(os.path.join(OUT, f)).astype(np.float32)
            if a.shape != SURF.shape:
                continue
            at = me.attributes.new(f[:-4], 'FLOAT', 'POINT')
            at.data.foreach_set("value", a.ravel())
    me.update()
    ob = link(bpy.data.objects.new("Terrain", me))

    m = bpy.data.materials.new("TerrainMat")
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(OUT, "albedo.png"))
    tex.interpolation = 'Cubic'
    tex.extension = 'EXTEND'
    rt = nt.nodes.new("ShaderNodeTexImage")
    rt.image = bpy.data.images.load(os.path.join(OUT, "rough.png"))
    rt.image.colorspace_settings.name = 'Non-Color'
    rt.extension = 'EXTEND'
    nt.links.new(tex.outputs["Color"], b.inputs["Base Color"])
    # calm-water mirror reflections of the coast read as a dark "wall" at this view angle, so
    # the sea is kept semi-glossy (roughness floor 0.24)
    rmax = nt.nodes.new("ShaderNodeMath")
    rmax.operation = "MAXIMUM"
    rmax.inputs[1].default_value = 0.24
    nt.links.new(rt.outputs["Color"], rmax.inputs[0])
    nt.links.new(rmax.outputs[0], b.inputs["Roughness"])
    # ripples on water only (roughness < 0.3), fine grain on land
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    nt.links.new(geo.outputs["Position"], noise.inputs["Vector"])
    noise.inputs["Scale"].default_value = 2.5
    noise.inputs["Detail"].default_value = 4.0
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.10
    bump.inputs["Distance"].default_value = 0.02
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], b.inputs["Normal"])
    ob.data.materials.append(m)

    # The sea is its own plane just above the terrain's z=0 water, covering the whole frame and
    # coloured from the same albedo (UVs continued past [0,1]; EXTEND carries the open-ocean
    # edge colour outward). It is mostly UNLIT: with EX-exaggerated coasts a low sun throws
    # km-long cast shadows onto open water that read as a dark wall along every north coast.
    gx0, gx1, gy0, gy1 = X0, xs[-1], Y0, ys[-1]
    ext = 900.0
    corners = [(gx0 - ext, gy0 - ext), (gx1 + ext, gy0 - ext), (gx1 + ext, gy1 + ext), (gx0 - ext, gy1 + ext)]
    sme = bpy.data.meshes.new("OpenSea")
    sme.from_pydata([(x, y, 0.004) for x, y in corners], [], [(0, 1, 2, 3)])
    suv = sme.uv_layers.new(name="UVMap")
    for k, (x, y) in enumerate(corners):
        suv.data[k].uv = ((x - gx0) / (gx1 - gx0), (y - gy0) / (gy1 - gy0))
    sea = link(bpy.data.objects.new("Open Sea", sme))
    sm = bpy.data.materials.new("SeaMat")
    sm.use_nodes = True
    snt = sm.node_tree
    for n in list(snt.nodes):
        snt.nodes.remove(n)
    sout = snt.nodes.new("ShaderNodeOutputMaterial")
    stex = snt.nodes.new("ShaderNodeTexImage")
    stex.image = tex.image
    stex.interpolation = 'Cubic'
    stex.extension = 'EXTEND'
    emi = snt.nodes.new("ShaderNodeEmission")
    emi.inputs["Strength"].default_value = SEA_GLOW
    gl = snt.nodes.new("ShaderNodeBsdfPrincipled")
    gl.inputs["Roughness"].default_value = 0.18
    snt.links.new(stex.outputs["Color"], gl.inputs["Base Color"])
    sgeo = snt.nodes.new("ShaderNodeNewGeometry")
    snoise = snt.nodes.new("ShaderNodeTexNoise")
    snoise.inputs["Scale"].default_value = 1.4
    snoise.inputs["Detail"].default_value = 6.0
    snt.links.new(sgeo.outputs["Position"], snoise.inputs["Vector"])
    sbump = snt.nodes.new("ShaderNodeBump")
    sbump.inputs["Strength"].default_value = 0.25
    sbump.inputs["Distance"].default_value = 0.01
    snt.links.new(snoise.outputs["Fac"], sbump.inputs["Height"])
    snt.links.new(sbump.outputs["Normal"], gl.inputs["Normal"])
    # ripple shading on the unlit colour so open water is not a flat fill
    rgain = snt.nodes.new("ShaderNodeMapRange")
    rgain.inputs["To Min"].default_value = 0.88
    rgain.inputs["To Max"].default_value = 1.10
    snt.links.new(snoise.outputs["Fac"], rgain.inputs["Value"])
    cmul = snt.nodes.new("ShaderNodeVectorMath")
    cmul.operation = 'SCALE'
    snt.links.new(stex.outputs["Color"], cmul.inputs[0])
    snt.links.new(rgain.outputs["Result"], cmul.inputs["Scale"])
    snt.links.new(cmul.outputs[0], emi.inputs["Color"])
    mix = snt.nodes.new("ShaderNodeMixShader")
    mix.inputs["Fac"].default_value = 0.22
    snt.links.new(emi.outputs[0], mix.inputs[1])
    snt.links.new(gl.outputs[0], mix.inputs[2])
    snt.links.new(mix.outputs[0], sout.inputs["Surface"])
    sea.data.materials.append(sm)
    sea.visible_shadow = False
    return ob


# ---------------------------------------------------------------------------- instancer
def cone(name, r, hgt, z0=0.0, verts=8):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=0, depth=hgt,
                                    location=(0, 0, z0 + hgt / 2))
    o = bpy.context.active_object
    o.name = name
    return o


def blob(name, r, z0, sz=0.85, sub=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub, radius=r, location=(0, 0, z0 + r * sz))
    o = bpy.context.active_object
    o.scale = (1, 1, sz)
    o.name = name
    return o


def box(name, sx, sy, sz, z0=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, z0 + sz / 2))
    o = bpy.context.active_object
    o.scale = (sx, sy, sz)
    o.name = name
    return o


def joined(name, parts, mats):
    for p, m in zip(parts, mats):
        p.data.materials.clear()
        p.data.materials.append(m)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.shade_flat()
    return o


def proto_collection(name, builders):
    """Builds prototype objects, then moves them into an unlinked collection (instanced only)."""
    coll = bpy.data.collections.new(name)
    for b in builders:
        o = b()
        for c in list(o.users_collection):
            c.objects.unlink(o)
        coll.objects.link(o)
        o.location = (0, 0, 0)
    return coll


TRUNK = None


def trunk_mat():
    global TRUNK
    if TRUNK is None:
        TRUNK = mat_principled("Trunk", (0.26, 0.18, 0.12), 0.9)
    return TRUNK


def tree_round(name, cols, r=0.2, sz=0.8):
    ms = [mat_principled(name + "_leaf%d" % i, c, 0.75) for i, c in enumerate(cols)]
    out = []
    for i, m in enumerate(ms):
        def mk(i=i, m=m):
            t = box(name + "t", 0.03, 0.03, 0.12)
            a = blob(name + "a", r * (1.0 - 0.1 * i), 0.08, sz)
            b2 = blob(name + "b", r * 0.62, 0.08 + r * 0.9, sz)
            b2.location.x += r * 0.25
            return joined(f"{name}{i}", [t, a, b2], [trunk_mat(), m, m])
        out.append(mk)
    return out


def tree_cone(name, cols, r=0.17, hgt=0.72, snow=None):
    out = []
    for i, c in enumerate(cols):
        m = mat_principled(f"{name}_c{i}", c, 0.8)
        sm = mat_principled(f"{name}_s{i}", snow, 0.6) if snow else m
        def mk(i=i, m=m, sm=sm):
            t = box(name + "t", 0.03, 0.03, 0.08)
            a = cone(name + "a", r * (1 + 0.12 * i), hgt * (1 - 0.1 * i), 0.05)
            b2 = cone(name + "b", r * 0.72, hgt * 0.62, 0.05 + hgt * 0.42)
            return joined(f"{name}{i}", [t, a, b2], [trunk_mat(), m, sm])
        out.append(mk)
    return out


def bamboo():
    m = mat_principled("Bamboo", (0.50, 0.72, 0.30), 0.6)
    m2 = mat_principled("Bamboo2", (0.38, 0.62, 0.24), 0.6)
    def mk(k, mm):
        def f():
            parts = []
            for q in range(5):
                a = q * 1.256 + k
                bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.035, depth=0.9,
                                                    location=(math.cos(a) * 0.07, math.sin(a) * 0.07, 0.45))
                parts.append(bpy.context.active_object)
            parts.append(blob("bt", 0.14, 0.62, 1.5))
            return joined("Bamboo%d" % k, parts, [mm] * len(parts))
        return f
    return [mk(0, m), mk(1, m2)]


def palm():
    m = mat_principled("Palm", (0.30, 0.66, 0.24), 0.7)
    def f():
        bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.02, depth=0.35, location=(0, 0, 0.175))
        t = bpy.context.active_object
        top = blob("pt", 0.16, 0.32, 0.45)
        return joined("Palm", [t, top], [trunk_mat(), m])
    return [f]


def houses():
    walls = [mat_principled("WallW", (0.93, 0.91, 0.86), 0.8), mat_principled("WallC", (0.90, 0.82, 0.68), 0.8)]
    roofs = [mat_principled("RoofB", (0.22, 0.28, 0.38), 0.6), mat_principled("RoofR", (0.66, 0.26, 0.18), 0.6),
             mat_principled("RoofT", (0.20, 0.46, 0.48), 0.6), mat_principled("RoofG", (0.36, 0.34, 0.34), 0.6)]
    out = []
    for k in range(6):
        def f(k=k):
            sx, sy = (0.13, 0.10) if k % 2 == 0 else (0.18, 0.11)
            hh = 0.06 + 0.03 * (k % 3)
            a = box("hw", sx, sy, hh)
            bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.75, radius2=0.0, depth=0.5,
                                            location=(0, 0, hh + 0.025))
            r = bpy.context.active_object
            r.rotation_euler.z = math.pi / 4
            r.scale = (sx * 0.95, sy * 0.95, 0.12)
            return joined("House%d" % k, [a, r], [walls[k % 2], roofs[k % 4]])
        out.append(f)
    return out


def towers():
    glass = [mat_principled("GlassA", (0.58, 0.66, 0.76), 0.25, metallic=0.4),
             mat_principled("GlassB", (0.74, 0.78, 0.82), 0.3, metallic=0.3),
             mat_principled("GlassC", (0.44, 0.52, 0.64), 0.25, metallic=0.4),
             mat_principled("Concrete", (0.80, 0.78, 0.74), 0.7)]
    out = []
    for k in range(8):
        def f(k=k):
            w = 0.14 + 0.05 * (k % 3)
            hgt = 0.25 + 0.12 * (k % 4)
            a = box("tw", w, w * (0.8 + 0.1 * (k % 2)), hgt)
            b2 = box("tt", w * 0.7, w * 0.6, hgt * 0.35, hgt)
            return joined("Tower%d" % k, [a, b2], [glass[k % 4], glass[(k + 1) % 4]])
        out.append(f)
    return out


def shrub():
    m = mat_principled("Scrub", (0.46, 0.44, 0.24), 0.9)
    return [lambda: joined("Scrub", [blob("s", 0.07, 0.0, 0.7)], [m])]


def scatter_group(name, attr, coll, smin, smax, seed, zmin=1.0, zmax=1.0, dens_mul=1.0):
    ng = bpy.data.node_groups.new(name, 'GeometryNodeTree')
    ng.interface.new_socket('Geometry', in_out='INPUT', socket_type='NodeSocketGeometry')
    ng.interface.new_socket('Geometry', in_out='OUTPUT', socket_type='NodeSocketGeometry')
    N, Lk = ng.nodes, ng.links
    gi = N.new('NodeGroupInput')
    go = N.new('NodeGroupOutput')

    def sock(sockets, name, typ):
        for s in sockets:
            if s.name == name and s.type == typ and s.enabled:
                return s
        for s in sockets:
            if s.name == name and s.type == typ:
                return s
        raise KeyError(name + "/" + typ)

    def rnd(dtype, lo, hi, sd):
        n = N.new('FunctionNodeRandomValue')
        n.data_type = dtype
        t = {'FLOAT': 'VALUE', 'INT': 'INT'}[dtype]
        sock(n.inputs, 'Min', t).default_value = lo
        sock(n.inputs, 'Max', t).default_value = hi
        sock(n.inputs, 'Seed', 'INT').default_value = sd
        return sock(n.outputs, 'Value', t)

    na = N.new('GeometryNodeInputNamedAttribute')
    na.data_type = 'FLOAT'
    na.inputs['Name'].default_value = attr
    mul = N.new('ShaderNodeMath')
    mul.operation = 'MULTIPLY'
    mul.inputs[1].default_value = dens_mul
    Lk.new(sock(na.outputs, 'Attribute', 'VALUE'), mul.inputs[0])
    dist = N.new('GeometryNodeDistributePointsOnFaces')
    dist.distribute_method = 'RANDOM'
    dist.inputs['Seed'].default_value = seed
    Lk.new(gi.outputs[0], dist.inputs['Mesh'])
    Lk.new(mul.outputs[0], dist.inputs['Density'])
    ci = N.new('GeometryNodeCollectionInfo')
    ci.transform_space = 'ORIGINAL'
    ci.inputs['Collection'].default_value = coll
    ci.inputs['Separate Children'].default_value = True
    ci.inputs['Reset Children'].default_value = True
    iop = N.new('GeometryNodeInstanceOnPoints')
    iop.inputs['Pick Instance'].default_value = True
    Lk.new(dist.outputs['Points'], iop.inputs['Points'])
    Lk.new(ci.outputs[0], iop.inputs['Instance'])
    Lk.new(rnd('INT', 0, 97, seed + 7), iop.inputs['Instance Index'])
    rot = N.new('ShaderNodeCombineXYZ')
    Lk.new(rnd('FLOAT', 0.0, 6.2832, seed + 1), rot.inputs['Z'])
    Lk.new(rot.outputs[0], iop.inputs['Rotation'])
    s = rnd('FLOAT', smin, smax, seed + 2)
    zf = N.new('ShaderNodeMath')
    zf.operation = 'MULTIPLY'
    Lk.new(s, zf.inputs[0])
    Lk.new(rnd('FLOAT', zmin, zmax, seed + 3), zf.inputs[1])
    sc = N.new('ShaderNodeCombineXYZ')
    Lk.new(s, sc.inputs['X'])
    Lk.new(s, sc.inputs['Y'])
    Lk.new(zf.outputs[0], sc.inputs['Z'])
    Lk.new(sc.outputs[0], iop.inputs['Scale'])
    join = N.new('GeometryNodeJoinGeometry')
    Lk.new(iop.outputs['Instances'], join.inputs[0])
    Lk.new(gi.outputs[0], join.inputs[0])
    Lk.new(join.outputs[0], go.inputs[0])
    return ng


def build_scatter(terrain):
    kinds = [
        ("attr_conifer", tree_cone("Conifer", [(0.10, 0.28, 0.14), (0.14, 0.33, 0.16), (0.09, 0.24, 0.15)]), 0.8, 1.3, 1.0, 1.25),
        ("attr_snowpine", tree_cone("SnowPine", [(0.10, 0.24, 0.16), (0.14, 0.28, 0.20)], snow=(0.88, 0.92, 0.96)), 0.8, 1.3, 1.0, 1.25),
        ("attr_darkpine", tree_cone("DarkPine", [(0.05, 0.16, 0.11), (0.07, 0.20, 0.12)], r=0.19, hgt=0.85), 0.9, 1.4, 1.0, 1.3),
        ("attr_broadleaf", tree_round("Broad", [(0.18, 0.40, 0.14), (0.26, 0.48, 0.16), (0.14, 0.34, 0.12)]), 0.8, 1.35, 0.9, 1.1),
        ("attr_sakura", tree_round("Sakura", [(0.98, 0.64, 0.78), (0.99, 0.76, 0.86), (0.93, 0.52, 0.70)], r=0.21), 0.8, 1.3, 0.9, 1.1),
        ("attr_autumn", tree_round("Autumn", [(0.86, 0.24, 0.10), (0.94, 0.52, 0.12), (0.92, 0.74, 0.22), (0.72, 0.16, 0.10)]), 0.8, 1.3, 0.9, 1.1),
        ("attr_bamboo", bamboo(), 0.8, 1.2, 0.9, 1.3),
        ("attr_palm", palm(), 0.9, 1.3, 1.0, 1.2),
        ("attr_shrub", shrub(), 0.8, 1.6, 1.0, 1.0),
        ("attr_house", houses(), 0.8, 1.7, 0.8, 1.6),
        ("attr_tower", towers(), 0.9, 1.5, 1.0, 6.0),
    ]
    for i, (attr, builders, smin, smax, zmin, zmax) in enumerate(kinds):
        if attr not in terrain.data.attributes:
            continue
        coll = proto_collection("P_" + attr, builders)
        ng = scatter_group("S_" + attr, attr, coll, smin, smax, 100 + i * 11, zmin, zmax,
                           0.25 if PREVIEW else 1.0)
        mod = terrain.modifiers.new("S_" + attr, 'NODES')
        mod.node_group = ng


# ---------------------------------------------------------------------------- lines
ROUTE_COL = {"sakura": (1.0, 0.45, 0.72), "city": (1.0, 0.36, 0.62), "coast": (0.25, 0.90, 1.0),
             "port": (0.35, 0.80, 1.0), "winter": (0.62, 0.86, 1.0), "alpine": (0.66, 0.84, 1.0),
             "volcanic": (0.76, 0.52, 1.0)}


def curve(name, pts, z_off, depth, mat, over_water_deck=None):
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = depth
    cu.bevel_resolution = 2
    cu.use_fill_caps = True
    sp = cu.splines.new('POLY')
    P = np.asarray(pts, np.float64).reshape(-1, 2)
    sp.points.add(len(P) - 1)
    for k, (x, y) in enumerate(P):
        z = z_at(x, y)
        if over_water_deck is not None:
            z = max(z, over_water_deck)
        sp.points[k].co = (x, y, z + z_off, 1.0)
    ob = link(bpy.data.objects.new(name, cu))
    ob.data.materials.append(mat)
    return ob


def build_lines():
    road_m = mat_principled("Road", (0.98, 0.93, 0.80), 0.5, emission=(1.0, 0.94, 0.80), strength=0.6)
    for k, rd in enumerate(R["roads"]):
        curve(f"Road {rd['a']}-{rd['b']}", rd["pts"], 0.12, 0.13, road_m)
    reg = {r["id"]: r for r in L["regions"]}
    for rt in R["routes"]:
        r = reg[rt["id"]]
        c = ROUTE_COL.get(r["biome"], (1.0, 0.55, 0.30))
        if rt["status"] != "built":
            c = (1.0, 0.62, 0.22)
        m = mat_principled("Route_" + rt["id"], c, 0.4, emission=c, strength=4.0)
        curve("Route " + rt["id"], rt["pts"], 0.22, 0.2, m, over_water_deck=0.35)
    deck = mat_principled("BridgeDeck", (0.96, 0.96, 0.98), 0.4, emission=(1, 1, 1), strength=0.4)
    pylon = mat_principled("Pylon", (0.95, 0.95, 0.97), 0.4)
    for b in R["bridges"]:
        P = np.asarray(b["pts"]).reshape(-1, 2)
        curve("Bridge " + b["name"], b["pts"], 0.0, 0.2, deck, over_water_deck=0.5)
        seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
        cum = np.concatenate([[0], np.cumsum(seg)])
        for s in np.arange(1.5, cum[-1] - 1, 3.0):
            x, y = np.interp(s, cum, P[:, 0]), np.interp(s, cum, P[:, 1])
            if z_at(x, y) < 0.02:
                bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.09, depth=2.2, location=(x, y, 1.1))
                o = bpy.context.active_object
                o.data.materials.append(pylon)
    dot = mat_principled("Ferry", (1, 1, 1), 0.5, emission=(1, 1, 1), strength=1.5)
    for f in R["ferries"]:
        P = np.asarray(f["pts"]).reshape(-1, 2)
        seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
        cum = np.concatenate([[0], np.cumsum(seg)])
        for s in np.arange(0, cum[-1], 1.6):
            x, y = np.interp(s, cum, P[:, 0]), np.interp(s, cum, P[:, 1])
            bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.3, depth=0.02, location=(x, y, 0.02))
            bpy.context.active_object.data.materials.append(dot)


def build_boats():
    water = np.load(os.path.join(OUT, "water.npy"))
    rng = np.random.RandomState(7)
    hull = mat_principled("Hull", (0.97, 0.97, 0.98), 0.4)
    sail = mat_principled("Wake", (0.95, 0.98, 1.0), 0.6, emission=(1, 1, 1), strength=0.3)
    ports = [s for s in L["settlements"] if s["name"] in
             ("Maple City", "Minato", "Nagisa Marina", "Shiosai", "Kitsune", "Tsuki harbour", "Kumo port", "Kaze")]
    n = 0
    for s in ports:
        for _ in range(60):
            if n > 140:
                break
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(s["r"] * 0.8, s["r"] * 3.0 + 4)
            x, y = s["c"][0] + math.cos(a) * r, s["c"][1] + math.sin(a) * r
            i, j = int((x - X0) / RES), int((y - Y0) / RES)
            if not (0 <= i < NX and 0 <= j < NY) or water[j, i] < 0.99 or SURF[j, i] > 1:
                continue
            L_ = rng.uniform(0.25, 0.55)
            o = box("Boat", L_, L_ * 0.35, 0.08, 0.0)
            o.location = (x, y, 0.0)
            o.rotation_euler.z = rng.uniform(0, math.pi)
            o.data.materials.append(hull)
            w = box("Wake", L_ * 1.8, L_ * 0.22, 0.004, 0.005)
            w.location = (x - math.cos(o.rotation_euler.z) * L_ * 1.2, y - math.sin(o.rotation_euler.z) * L_ * 1.2, 0.0)
            w.rotation_euler.z = o.rotation_euler.z
            w.data.materials.append(sail)
            n += 1


def build_landmarks():
    red = mat_principled("TowerRed", (0.90, 0.20, 0.10), 0.5)
    white = mat_principled("White", (0.96, 0.96, 0.96), 0.5)
    glow = mat_principled("WheelGlow", (1.0, 0.8, 0.95), 0.4, emission=(1.0, 0.75, 0.9), strength=2.0)
    torii_m = mat_principled("Torii", (0.88, 0.16, 0.08), 0.5)

    def at(x, y):
        return (x, y, z_at(x, y))

    # Maple Tower
    x, y, z = at(212.0, 151.0)
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.45, radius2=0.04, depth=3.4, location=(x, y, z + 1.7))
    bpy.context.active_object.data.materials.append(red)
    for hh, rr in ((1.1, 0.34), (2.3, 0.2)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=rr, depth=0.12, location=(x, y, z + hh))
        bpy.context.active_object.data.materials.append(white)
    # Minato wheel
    x, y, z = at(149.0, 67.5)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.55, minor_radius=0.05, location=(x, y, z + 0.65),
                                     rotation=(math.pi / 2, 0, 0.5))
    bpy.context.active_object.data.materials.append(glow)
    # Shiosai lighthouse
    x, y, z = at(66.5, 89.0)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.12, depth=0.7, location=(x, y, z + 0.35))
    bpy.context.active_object.data.materials.append(white)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.14, depth=0.14, location=(x, y, z + 0.75))
    bpy.context.active_object.data.materials.append(red)

    def torii(x, y, s):
        z = max(z_at(x, y), 0.0)
        for dx in (-0.5, 0.5):
            bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.06 * s, depth=1.0 * s,
                                                location=(x + dx * s, y, z + 0.5 * s))
            bpy.context.active_object.data.materials.append(torii_m)
        for hh, ww in ((0.95, 1.5), (0.78, 1.2)):
            o = box("tb", ww * s, 0.1 * s, 0.08 * s)
            o.location = (x, y, z + hh * s)
            o.data.materials.append(torii_m)

    torii(200.0, 213.8, 0.8)          # Fuji Ridge summit shrine
    torii(295.0, 40.5, 1.0)           # Tsuki sea gate
    torii(313.0, 153.5, 0.6)          # Sakura Pass
    # Sakura pagoda
    x, y, z = at(309.0, 149.5)
    for k in range(5):
        o = box("pg", 0.32 - k * 0.04, 0.32 - k * 0.04, 0.05)
        o.location = (x, y, z + 0.12 + k * 0.14)
        o.data.materials.append(torii_m)
        o = box("pw", 0.2 - k * 0.025, 0.2 - k * 0.025, 0.09)
        o.location = (x, y, z + 0.03 + k * 0.14)
        o.data.materials.append(white)
    # Kaze Cape wind turbines
    for k in range(7):
        x, y, z = at(13.0 + k * 1.3, 107.0 + (k % 2) * 1.1)
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.025, depth=0.9, location=(x, y, z + 0.45))
        bpy.context.active_object.data.materials.append(white)
        for b in range(3):
            a = b * 2.094 + k
            o = box("blade", 0.03, 0.02, 0.42)
            o.location = (x, y - 0.03, z + 0.9)
            o.rotation_euler = (0, a, 0)
            o.data.materials.append(white)


def build_clouds():
    m = bpy.data.materials.new("Cloud")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Color"].default_value = (1, 1, 1, 1)
    vol.inputs["Anisotropy"].default_value = 0.3
    tc = nt.nodes.new("ShaderNodeTexCoord")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.6
    noise.inputs["Detail"].default_value = 10.0
    noise.inputs["Roughness"].default_value = 0.62
    grad = nt.nodes.new("ShaderNodeTexGradient")
    grad.gradient_type = 'SPHERICAL'
    mulA = nt.nodes.new("ShaderNodeMath")
    mulA.operation = 'MULTIPLY'
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.34
    ramp.color_ramp.elements[1].position = 0.72
    dens = nt.nodes.new("ShaderNodeMath")
    dens.operation = 'MULTIPLY'
    dens.inputs[1].default_value = 3.0
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
    nt.links.new(tc.outputs["Object"], grad.inputs["Vector"])
    nt.links.new(noise.outputs["Fac"], mulA.inputs[0])
    nt.links.new(grad.outputs["Fac"], mulA.inputs[1])
    nt.links.new(mulA.outputs[0], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], dens.inputs[0])
    nt.links.new(dens.outputs[0], vol.inputs["Density"])
    nt.links.new(vol.outputs["Volume"], out.inputs["Volume"])
    rng = np.random.RandomState(3)
    # (x, y, radius km): framing clouds over open sea at the edges, a cloud sea on Kamifuji's
    # north-east flank (Fuji Ridge's "Cloudbreak") and a veil over Kumo Isle ("in the Mist").
    spots = [(-6, 20, 22), (30, 4, 18), (95, -2, 16), (175, 6, 14), (250, 0, 20), (345, 14, 22),
             (388, 60, 18), (392, 110, 14), (-12, 70, 16), (-10, 200, 22), (18, 258, 20),
             (120, 270, 18), (250, 268, 20), (372, 240, 20), (392, 180, 12),
             (214, 232, 6), (222, 222, 5), (208, 236, 5), (70, 44, 5), (48, 34, 4)]
    for k, (x, y, r) in enumerate(spots):
        zc = 9.0 if r <= 6 else 14.0
        for q in range(4):
            dx, dy = rng.uniform(-0.6, 0.6, 2) * r
            rr = r * rng.uniform(0.45, 0.8)
            bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1.0,
                                                  location=(x + dx, y + dy, zc + rng.uniform(-1, 1)))
            o = bpy.context.active_object
            o.scale = (rr, rr * rng.uniform(0.7, 1.0), rr * 0.32)
            o.data.materials.append(m)
            o.name = f"Cloud{k}_{q}"


# ---------------------------------------------------------------------------- camera / light
def build_camera_and_light():
    cam_d = bpy.data.cameras.new("MapCam")
    cam_d.type = 'ORTHO'
    cam_d.ortho_scale = FRAME_W
    cam_d.clip_start = 1.0
    cam_d.clip_end = 3000.0
    cam = link(bpy.data.objects.new("MapCam", cam_d))
    th = math.radians(90.0 - TILT)
    cam.rotation_euler = (th, 0, 0)
    view = Vector((0, math.sin(th), -math.cos(th)))
    target = Vector((FRAME_W / 2 + 0.0, FRAME_CY, 0.0))
    cam.location = target - view * 900.0
    scene.camera = cam

    sun_d = bpy.data.lights.new("Sun", 'SUN')
    sun_d.energy = 5.0
    sun_d.angle = math.radians(1.2)
    sun_d.color = (1.0, 0.94, 0.84)
    sun = link(bpy.data.objects.new("Sun", sun_d))
    el, az = math.radians(30.0), math.radians(215.0)          # low sun from the south-west
    to_sun = Vector((math.cos(el) * math.sin(az), math.cos(el) * math.cos(az), math.sin(el)))
    sun.rotation_euler = (-to_sun).to_track_quat('-Z', 'Y').to_euler()

    world = bpy.data.worlds.new("Sky")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    sky = nt.nodes.new("ShaderNodeTexSky")
    sky.sky_type = 'NISHITA'
    sky.sun_disc = False
    sky.sun_elevation = el
    sky.sun_rotation = az
    nt.links.new(sky.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 0.26
    return cam


def setup_render():
    scene.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for dev in ('OPTIX', 'CUDA'):
        try:
            prefs.compute_device_type = dev
            prefs.get_devices()
            ok = False
            for d in prefs.devices:
                d.use = d.type == dev
                ok |= d.use
            if ok:
                scene.cycles.device = 'GPU'
                print("[relief] rendering on", dev)
                break
        except Exception as e:
            print("[relief] no", dev, e)
    scene.cycles.samples = SAMPLES
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.cycles.volume_bounces = 1
    scene.cycles.volume_step_rate = 4.0
    scene.render.resolution_x = RES_X
    scene.render.resolution_y = RES_Y
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0.0
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.image_settings.color_depth = '8'


def write_projection(cam):
    dg = bpy.context.evaluated_depsgraph_get()

    def uv(x, y, z):
        c = world_to_camera_view(scene, cam, Vector((x, y, z)))
        return c.x, c.y

    o = uv(0, 0, 0)
    ex = uv(1, 0, 0)
    ey = uv(0, 1, 0)
    ez = uv(0, 0, 1)
    au = [ex[0] - o[0], ey[0] - o[0], (ez[0] - o[0]) * EX / 1000.0, o[0]]
    av = [ex[1] - o[1], ey[1] - o[1], (ez[1] - o[1]) * EX / 1000.0, o[1]]
    regions = []
    for r in L["regions"]:
        x, y = r["pos"]
        g = z_at(x, y) / EX * 1000.0
        u, v = uv(x, y, z_at(x, y))
        regions.append({"id": r["id"], "ground_m": round(g, 1), "u": round(u, 5), "v": round(v, 5)})
    data = {
        "_comment": "Generated by tools/blender/worldmap/build_world_relief.py. Map fraction "
                    "(0,0 = bottom-left of MapleRideWorldMap_v2.png) = affine(world km x, y, "
                    "ground m): u = au.(x, y, m, 1), v = av.(x, y, m, 1).",
        "image": "UI/MapleRideWorldMap_v2", "width": RES_X, "height": RES_Y,
        "exaggeration": EX, "tilt_deg": TILT, "frame_width_km": FRAME_W,
        "km_per_u": FRAME_W, "au": [round(v, 8) for v in au], "av": [round(v, 8) for v in av],
        "regions": regions,
    }
    json.dump(data, open(PROJ_OUT, "w"), indent=1)
    print("[relief] wrote", PROJ_OUT)


def main():
    t = build_terrain()
    print("[relief] terrain", len(t.data.vertices), "verts")
    build_scatter(t)
    build_lines()
    build_boats()
    build_landmarks()
    if CLOUDS:
        build_clouds()
    cam = build_camera_and_light()
    setup_render()
    write_projection(cam)
    if "--save-blend" in argv:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "world_relief.blend"))
    scene.render.filepath = os.path.join(OUT, "render_preview.png" if PREVIEW else "render.png")
    bpy.ops.render.render(write_still=True)
    print("[relief] rendered", scene.render.filepath)


main()

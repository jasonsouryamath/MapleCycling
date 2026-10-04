"""
Build every Shunta Metro prop, export FBX (LOD0+LOD1 siblings) + textures + manifest, render the contact sheet.

  tools\blender-4.5.10-windows-x64\blender.exe -b --python tools/blender/ShuntaMetro/Props/build_all.py
  ... -- --only vending_drink_a,torii_gate     (subset)      --nosheet / --noexport
"""
import sys, os, json, math, importlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector
import props_lib as L
from props_lib import *

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
only = None
if "--only" in argv:
    only = set(argv[argv.index("--only") + 1].split(","))
do_sheet = "--nosheet" not in argv
do_export = "--noexport" not in argv

import assets_a, assets_b, assets_c  # noqa: E402
REG = {}
for mod in (assets_a, assets_b, assets_c):
    REG.update(mod.ASSETS)

bpy.ops.wm.read_factory_settings(use_empty=True)
define_materials()
if only is None or "--textures" in argv:
    make_textures()
mats = make_blender_materials()
with open(os.path.join(OUT, "materials.json"), "w", encoding="utf-8") as f:
    json.dump(MATDEF, f, indent=1)


def build(name, fn, lod, ratio, span=None):
    if span is not None:
        assets_a.SPAN[0] = span
    b = B(mats)
    fn(b)
    o = b.build(name + "_LOD0")
    return o


manifest = {}
built = {}
for name, (fn, lod, ratio) in REG.items():
    if only and name not in only:
        continue
    o0 = build(name, fn, lod, ratio)
    objs = [o0]
    t0 = tri_count(o0)
    t1 = 0
    if lod:
        o1 = o0.copy(); o1.data = o0.data.copy(); o1.name = name + "_LOD1"
        bpy.context.scene.collection.objects.link(o1)
        md = o1.modifiers.new("lod", 'DECIMATE'); md.ratio = ratio; md.use_collapse_triangulate = True
        objs.append(o1)
        t1 = int(t0 * ratio)
    bb = [o0.matrix_world @ Vector(c) for c in o0.bound_box]
    mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    manifest[name] = dict(tris=t0, tris_lod1=t1, lod=lod, size=[round(mx.x - mn.x, 3), round(mx.z - mn.z, 3), round(mx.y - mn.y, 3)],
                          mats=o0.data.materials.keys())
    print(f"BUILT {name}: LOD0 {t0} tris, LOD1~{t1}, size {manifest[name]['size']}")
    if do_export:
        for ob in bpy.context.view_layer.objects:
            ob.select_set(False)
        for ob in objs:
            ob.select_set(True)
        bpy.context.view_layer.objects.active = o0
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(OUT, name + ".fbx"), use_selection=True, apply_unit_scale=True, global_scale=1.0,
            apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', object_types={'MESH'},
            use_mesh_modifiers=True, mesh_smooth_type='EDGE', use_tspace=True, use_triangles=True,
            bake_space_transform=True, add_leaf_bones=False, path_mode='STRIP', embed_textures=False)
    if lod:
        o1.hide_render = True; o1.hide_viewport = True
    built[name] = o0

if only is None:
    with open(os.path.join(OUT, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=1)
else:  # merge
    p = os.path.join(OUT, "manifest.json")
    old = json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {}
    old.update(manifest)
    json.dump(old, open(p, "w", encoding="utf-8"), indent=1)

tot = sum(m["tris"] for m in manifest.values())
print(f"TOTAL assets={len(manifest)} tris={tot} over_budget={[n for n,m in manifest.items() if m['tris']>6000]}")

# ------------------------------------------------------------------ contact sheet
if do_sheet:
    names = list(built.keys())
    # span poles: rebuild short-wire versions for the sheet
    for n in ("utility_pole_xfmr", "utility_pole_lamp"):
        if n in built:
            built[n].hide_render = True
            fn = REG[n][0]
            built[n] = build(n + "_sheet", fn, False, 1, span=1.5)
    assets_a.SPAN[0] = 30.0
    cols = 8; rows = math.ceil(len(names) / cols); cell = 1.3
    from mathutils import Euler
    for i, n in enumerate(names):
        o = built[n]
        bpy.context.view_layer.update()
        bb = [Vector(c) for c in o.bound_box]
        dim = max(max(v.x for v in bb) - min(v.x for v in bb), max(v.z for v in bb) - min(v.z for v in bb), max(v.y for v in bb) - min(v.y for v in bb), 0.01)
        if n.startswith("utility_pole"):
            dim = 10.5
        dim = max(dim, 0.2)
        s = 1.0 / dim
        cx = (min(v.x for v in bb) + max(v.x for v in bb)) / 2
        o.scale = (s, s, s)
        o.rotation_euler = (0, 0, math.radians(-28))   # 3/4 view, front toward camera-left
        col, row = i % cols, i // cols
        o.location = ((col - (cols - 1) / 2) * cell, 0, -(row - (rows - 1) / 2) * cell * 1.02 - 0.5 + 0)
        # raise so base sits on the cell floor: Blender Z up -> place along world Z
        o.location.z = -(row - (rows - 1) / 2) * cell * 1.0 - 0.5
        o.location.x -= cx * s * math.cos(math.radians(-28))
        o.location.y -= -cx * s * math.sin(math.radians(-28)) * 0
        t = bpy.data.curves.new("lbl", "FONT"); t.body = n.replace("_", " "); t.size = 0.075; t.align_x = 'CENTER'
        lo = bpy.data.objects.new("lbl", t); bpy.context.scene.collection.objects.link(lo)
        lo.rotation_euler = (math.radians(90), 0, 0)
        lo.location = ((col - (cols - 1) / 2) * cell, -0.6, o.location.z - 0.12)
        m = bpy.data.materials.new("lblm"); m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 1, 1, 1)
        m.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (1, 1, 1, 1)
        m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 1.0
        lo.data.materials.append(m)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 24; sc.cycles.use_denoising = True
    sc.cycles.device = 'CPU'
    sc.render.resolution_x = 1200; sc.render.resolution_y = int(1200 * (rows * cell + 0.3) / (cols * cell))
    sc.render.resolution_percentage = 100
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.05, 0.06, 0.12, 1); bg.inputs[1].default_value = 0.7
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'
    cam.ortho_scale = cols * cell + 0.1
    co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
    co.location = (0, -20, 0); co.rotation_euler = (math.radians(90), 0, 0)
    # tilt slightly for 3/4 look
    sun = bpy.data.lights.new("s", 'SUN'); sun.energy = 3.0; sun.angle = math.radians(20)
    so = bpy.data.objects.new("s", sun); sc.collection.objects.link(so)
    so.rotation_euler = (math.radians(55), math.radians(10), math.radians(-35))
    fill = bpy.data.lights.new("f", 'SUN'); fill.energy = 1.2; fill.color = (0.6, 0.7, 1.0)
    fo = bpy.data.objects.new("f", fill); sc.collection.objects.link(fo)
    fo.rotation_euler = (math.radians(70), 0, math.radians(140))
    sc.view_settings.view_transform = 'AgX'
    out = os.path.join(ROOT, "docs", "shunta_captures", "props_sheet.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sc.render.filepath = out
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    print("SHEET", out)

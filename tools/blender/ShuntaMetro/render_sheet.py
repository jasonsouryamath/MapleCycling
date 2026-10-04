"""Render a night contact sheet of the exported GLBs (LOD0). blender -b --python render_sheet.py -- <out.png> [filter]
Imports the real GLBs so it also proves the export. Sheet <= 1200 px wide."""
import glob
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
MODELS = os.path.join(ROOT, "Assets", "Models", "ShuntaMetro")
args = sys.argv[sys.argv.index("--") + 1:]
OUT = args[0]
FILTER = args[1] if len(args) > 1 else ""
TW, TH, COLS = 300, 225, 4


def setup_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 28
    sc.cycles.use_denoising = True
    sc.cycles.device = "CPU"
    sc.render.resolution_x, sc.render.resolution_y = TW * 2, TH * 2
    sc.render.resolution_percentage = 50
    sc.render.image_settings.file_format = "PNG"
    sc.view_settings.view_transform = "Filmic" if "Filmic" in [v.identifier for v in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else "AgX"
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.045, 0.1, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    sc.world = w
    return sc


def add_light(name, kind, loc, energy, color, size=4.0):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = energy
    ld.color = color
    if kind == "AREA":
        ld.size = size
    ob = bpy.data.objects.new(name, ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    return ob


def render_one(path, idx):
    sc = setup_scene()
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in meshes:
        if o.name.endswith("LOD1") or "_LOD1" in o.name:
            bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in meshes:
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            mn = Vector((min(mn.x, p.x), min(mn.y, p.y), min(mn.z, p.z)))
            mx = Vector((max(mx.x, p.x), max(mx.y, p.y), max(mx.z, p.z)))
    ctr = (mn + mx) / 2
    size = max((mx - mn).x, (mx - mn).y, (mx - mn).z)
    # wet dark ground
    bpy.ops.mesh.primitive_plane_add(size=size * 60, location=(ctr.x, ctr.y, mn.z - 0.01))
    gp = bpy.context.object
    gm = bpy.data.materials.new("gnd")
    gm.use_nodes = True
    b = gm.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.02, 0.022, 0.03, 1)
    b.inputs["Roughness"].default_value = 0.22
    gp.data.materials.append(gm)
    # lights: cool key, warm rim, magenta fill (neon feel)
    add_light("key", "AREA", (ctr.x - size, ctr.y - size * 1.2, mx.z + size * 0.8), size * size * 60, (0.6, 0.75, 1.0), size)
    add_light("rim", "AREA", (ctr.x + size, ctr.y + size, mx.z + size * 0.5), size * size * 40, (1.0, 0.55, 0.3), size)
    add_light("fill", "AREA", (ctr.x + size * 1.2, ctr.y - size * 0.8, mx.z + size), size * size * 8, (1.0, 0.25, 0.6), size)
    cam_d = bpy.data.cameras.new("cam")
    cam_d.lens = 38
    cam = bpy.data.objects.new("cam", cam_d)
    bpy.context.scene.collection.objects.link(cam)
    sc.camera = cam
    dist = size * 1.35 + 1.5
    cam.location = ctr + Vector((-0.78, -1.0, 0.42)).normalized() * dist
    d = ctr - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    out = os.path.join(bpy.app.tempdir, "t%03d.png" % idx)
    sc.render.filepath = out
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(out)
    w, h = img.size
    arr = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return arr


files = sorted(glob.glob(os.path.join(MODELS, "Vehicles", "*.glb"))) + sorted(glob.glob(os.path.join(MODELS, "Buildings", "*.glb")))
if FILTER:
    files = [f for f in files if FILTER.lower() in os.path.basename(f).lower()]
tiles = []
for i, f in enumerate(files):
    print("[sheet] render", os.path.basename(f))
    tiles.append(render_one(f, i))
rows = (len(tiles) + COLS - 1) // COLS
th, tw = tiles[0].shape[0], tiles[0].shape[1]
sheet = np.zeros((rows * th, COLS * tw, 4), dtype=np.float32)
sheet[..., 3] = 1
for i, t in enumerate(tiles):
    r, c = divmod(i, COLS)
    y0 = (rows - 1 - r) * th     # image rows are bottom-up in Blender
    sheet[y0:y0 + th, c * tw:(c + 1) * tw] = t
img = bpy.data.images.new("sheet", COLS * tw, rows * th)
img.pixels = sheet.flatten().tolist()
img.filepath_raw = OUT
img.file_format = "PNG"
img.save()
print("[sheet] wrote", OUT, COLS * tw, "x", rows * th, len(tiles), "tiles")

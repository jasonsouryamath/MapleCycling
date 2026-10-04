"""
NAGISA BAY (B5) - Blender preview sheet for the Nagisa GLBs (review aid, not shipped).

Imports each GLB, keeps only the _LOD0 objects, renders WORKBENCH views with BACKFACE
CULLING ON (a flipped face shows as a hole) and material colours, to
reference/good_graphics/nagisa_bay/glb_<name>_<view>.png.

Run: blender.exe -b -P tools/blender/build_nagisa_preview.py -- Nagisa_HotelTower [more names]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODELS = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Models")
OUT = os.path.join(ROOT, "reference", "good_graphics", "nagisa_bay")

COLOURS = {
    "NB_Stone": (0.92, 0.88, 0.80), "NB_Glass": (0.25, 0.45, 0.50), "NB_BalconyGlass": (0.55, 0.80, 0.85),
    "NB_Bronze": (0.55, 0.38, 0.22), "NB_Teak": (0.62, 0.40, 0.22), "NB_DeckStone": (0.86, 0.80, 0.70),
    "NB_PoolTile": (0.20, 0.65, 0.72), "NB_PoolWater": (0.30, 0.80, 0.88), "NB_Canvas": (0.95, 0.92, 0.84),
    "NB_Planter": (0.70, 0.66, 0.60), "NB_Soil": (0.30, 0.22, 0.16), "NB_HotelSign": (0.75, 0.55, 0.30),
    "NB_Plaster": (0.95, 0.94, 0.90), "NB_TownWindows": (0.70, 0.78, 0.80), "NB_TownWall": (0.95, 0.72, 0.72), "NB_RoofTile": (0.72, 0.36, 0.24), "NB_Signs": (0.20, 0.50, 0.55), "NB_PalmBark": (0.55, 0.47, 0.38), "NB_PalmFrond": (0.25, 0.55, 0.22), "NB_FanFrond": (0.30, 0.58, 0.25), "NB_Metal": (0.6, 0.62, 0.64), "NB_Paint": (0.85, 0.2, 0.2), "NB_PaintTeal": (0.2, 0.6, 0.6), "NB_Gelcoat": (0.96, 0.96, 0.95), "NB_Superstructure": (0.3, 0.35, 0.42), "NB_PontoonWood": (0.6, 0.5, 0.4), "NB_Concrete": (0.6, 0.6, 0.58), "NB_Lamp": (1, 0.9, 0.6), "NB_Canopy": (0.15, 0.40, 0.15),
}


def main():
    names = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    os.makedirs(OUT, exist_ok=True)
    for name in names:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=os.path.join(MODELS, name + ".glb"))
        for o in list(bpy.context.scene.objects):
            if o.type == "MESH" and not o.name.endswith("_LOD0"):
                bpy.data.objects.remove(o, do_unlink=True)
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        for o in meshes:
            for m in o.data.materials:
                if m is None:
                    continue
                key = m.name.split(".")[0]
                c = COLOURS.get(key, (0.8, 0.3, 0.8))
                m.diffuse_color = (c[0], c[1], c[2], 1.0)
        lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
        for o in meshes:
            for v in o.bound_box:
                w = o.matrix_world @ Vector(v)
                lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
        ctr = (lo + hi) / 2
        rad = (hi - lo).length / 2
        sc = bpy.context.scene
        sc.render.engine = "BLENDER_WORKBENCH"
        sh = sc.display.shading
        sh.light = "STUDIO"
        sh.color_type = "MATERIAL"
        sh.show_backface_culling = True
        sh.show_cavity = True
        sc.render.resolution_x, sc.render.resolution_y = 1200, 800
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        sc.collection.objects.link(cam)
        sc.camera = cam
        cam.data.lens = 40
        # Blender: Unity -z (sea side) == Blender +y.  views: sea side, road side, top, 3/4
        views = {"sea": (0.35, 1.0, 0.35), "road": (-0.3, -1.0, 0.25), "q34": (1.0, 0.7, 0.6),
                 "low": (-0.9, 0.9, 0.08)}
        for vn, d in views.items():
            dv = Vector(d).normalized()
            cam.location = ctr + dv * rad * 2.3
            cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler()
            cam.data.clip_end = rad * 10
            sc.render.filepath = os.path.join(OUT, "glb_%s_%s.png" % (name, vn))
            bpy.ops.render.render(write_still=True)
        print("[nagisa-prev] %s rendered (%.0f m radius)" % (name, rad))


main()

"""
Preview renders for the Harbor families (LOD0 only).
  blender -b --factory-startup -P tools/blender/preview_minato_harbor.py -- <outdir>
Imports the exported FBXs, lays them out in Unity space and renders a few golden-hour views.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402

MODELS = os.path.join(S.repo_root(), "Assets", "Environment", "MinatoCoast", "Models")

LAYOUT = [  # (file, unity offset, yaw deg)
    ("Minato_Harbor_STSCrane.fbx", (0, 0, 0), 0),
    ("Minato_Harbor_STSCraneRaised.fbx", (-40, 0, 0), 0),
    ("Minato_Harbor_STSCrane.fbx", (40, 0, 0), 0),
    ("Minato_Harbor_ContainerShip.fbx", (0, 0, 35), 90),
    ("Minato_Harbor_Tug.fbx", (60, 0, 80), 30),
    ("Minato_Harbor_Ferry.fbx", (-120, 0, 120), 90),
    ("Minato_Harbor_ContainerBlockA.fbx", (-10, 0, -60), 0),
    ("Minato_Harbor_ContainerBlockB.fbx", (30, 0, -60), 0),
    ("Minato_Harbor_ContainerBlockC.fbx", (65, 0, -60), 0),
    ("Minato_Harbor_RTG.fbx", (30, 0, -60), 0),
    ("Minato_Harbor_Breakwater.fbx", (-120, 0, 40), 0),
    ("Minato_Harbor_BreakwaterHead.fbx", (-120, 0, 75), 0),
    ("Minato_Harbor_Lighthouse.fbx", (-120, 2.8, 75), 0),
    ("Minato_Harbor_BuoyCan.fbx", (-70, 0, 90), 0),
    ("Minato_Harbor_BuoyCone.fbx", (-60, 0, 90), 0),
    ("Minato_Harbor_BuoyCross.fbx", (-50, 0, 90), 0),
    ("Minato_Harbor_RailSpan.fbx", (-60, 12, -110), 90),
    ("Minato_Harbor_RailSpan.fbx", (-30, 12, -110), 90),
    ("Minato_Harbor_FreightTrain.fbx", (-40, 12.46, -110), 90),
    ("Minato_Harbor_ContainerWallA.fbx", (-60, 0, -130), 90),
    ("Minato_Harbor_ChevronBarrier.fbx", (-60, 0, -120), 90),
    ("Minato_Harbor_HazardKerb.fbx", (-55, 0, -120), 90),
    ("Minato_Harbor_Sign.fbx", (-50, 0, -120), 180),
    ("Minato_Harbor_LightMast.fbx", (80, 0, -40), 0),
]


def main():
    out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "."
    os.makedirs(out, exist_ok=True)
    S.reset_scene()
    for fn, off, yaw in LAYOUT:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, fn))
        for o in set(bpy.data.objects) - before:
            if o.type == 'MESH' and not o.name.split(".")[0].endswith("_LOD0"):
                bpy.data.objects.remove(o, do_unlink=True)
                continue
            o.rotation_mode = 'XYZ'
            o.rotation_euler[2] += math.radians(-yaw)
            b = Vector(S.u2b(*off))
            o.location = o.location + b
    # sea plane
    bpy.ops.mesh.primitive_plane_add(size=2000, location=(0, 0, -0.05))
    sea = bpy.context.active_object
    m = bpy.data.materials.new("sea"); m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.25, 0.35, 1)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.15
    sea.data.materials.append(m)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.65, 0.85, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
    sun.data.energy = 4.0; sun.data.color = (1.0, 0.85, 0.65)
    sun.rotation_euler = (math.radians(55), 0, math.radians(35))
    S.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); S.link(cam); sc.camera = cam
    views = {
        "overview": ((-160, 70, -170), (0, 25, 20), 28),
        "crane": ((60, 20, -70), (0, 45, 20), 24),
        "ship": ((110, 12, 140), (0, 10, 35), 30),
        "gate": ((-45, 3, -165), (-40, 10, -110), 26),
        "breakwater": ((-80, 8, 20), (-120, 5, 70), 30),
    }
    for name, (c, t, lens) in views.items():
        cam.data.lens = lens
        cam.data.clip_end = 5000
        cp, tp = Vector(S.u2b(*c)), Vector(S.u2b(*t))
        cam.location = cp
        cam.rotation_euler = (tp - cp).normalized().to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out, f"harbor_{name}.png")
        bpy.ops.render.render(write_still=True)
        print("[harbor-preview]", sc.render.filepath)


main()

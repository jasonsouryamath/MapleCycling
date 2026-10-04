"""Preview render of the Skyline kit (LOD0 by default) with the facade textures attached.

blender -b --factory-startup -P preview_minato_skyline.py -- <out.png> [LOD0|LOD1|LOD2] [names...]
"""
import math
import os
import sys

import bpy
import mathutils

argv = sys.argv[sys.argv.index("--") + 1:]
out = argv[0]
lod = argv[1] if len(argv) > 1 else "LOD0"
names = argv[2:] or ["TowerSlimA", "TowerSlimB", "TowerRoundA", "TowerRoundB", "TowerStepped",
                     "MidriseA", "MidriseB", "PodiumA", "PodiumB", "RingSculpture", "Planter",
                     "MonorailHead", "MonorailCar", "GuidewayPier", "GuidewayBeam", "Skybridge"]
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODELS = os.path.join(ROOT, "Assets", "Environment", "MinatoCoast", "Models")
TEX = os.path.join(ROOT, "Assets", "Environment", "MinatoCoast", "Skyline")
GRAD = os.path.join(ROOT, "Assets", "Environment", "MinatoCoast", "Textures", "Minato_Foliage_Gradient.png")

bpy.ops.wm.read_factory_settings(use_empty=True)

COL = {  # approximate Unity-side tints for the preview only
    "sky_glass_blue": ((0.34, 0.60, 0.88), "Minato_Skyline_Curtain.png"),
    "sky_glass_teal": ((0.30, 0.66, 0.70), "Minato_Skyline_Curtain.png"),
    "sky_glass_deep": ((0.20, 0.34, 0.48), "Minato_Skyline_Curtain.png"),
    "sky_ribbon": ((1, 1, 1), "Minato_Skyline_Ribbon.png"),
    "sky_shop": ((1, 1, 1), "Minato_Skyline_Shopfront.png"),
    "sky_garden": ((1, 1, 1), GRAD),
}

x = 0.0
row = []
for n in names:
    path = os.path.join(MODELS, f"Minato_Skyline_{n}.fbx")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    keep = [o for o in new if o.type == 'MESH' and o.name.endswith(lod)]
    if not keep:
        keep = [o for o in new if o.type == 'MESH' and o.name.endswith("LOD1")]
    for o in new:
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in keep:
        bpy.context.view_layer.update()
        bb = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
        w = max(c.x for c in bb) - min(c.x for c in bb)
        o.location.x += x + w * 0.5 - (max(c.x for c in bb) + min(c.x for c in bb)) * 0.5
        o.location.y -= (max(c.y for c in bb) + min(c.y for c in bb)) * 0.5
        o.location.z -= min(c.z for c in bb)
        x += w + 6.0
        row.append(o)

for m in bpy.data.materials:
    key = next((k for k in COL if k in m.name), None)
    if key is None:
        continue
    tint, tex = COL[key]
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(nd for nd in nt.nodes if nd.type == 'BSDF_PRINCIPLED')
    img = nt.nodes.new("ShaderNodeTexImage")
    p = tex if os.path.isabs(tex) else os.path.join(TEX, tex)
    img.image = bpy.data.images.load(p)
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    mix.inputs[0].default_value = 1.0
    nt.links.new(img.outputs[0], mix.inputs[6])
    mix.inputs[7].default_value = (*tint, 1)
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    if "glass" in key:
        bsdf.inputs["Roughness"].default_value = 0.15
        bsdf.inputs["Metallic"].default_value = 0.3

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
scene.render.resolution_x = 1800
scene.render.resolution_y = 900
scene.world = bpy.data.worlds.new("W"); scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.70, 0.90, 1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
sun = bpy.data.lights.new("Sun", 'SUN'); sun.energy = 4.5
so = bpy.data.objects.new("Sun", sun); bpy.context.collection.objects.link(so)
so.rotation_euler = (math.radians(50), 0, math.radians(-40))
bpy.ops.mesh.primitive_plane_add(size=4000, location=(x * 0.5, 0, 0))

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
bpy.context.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 35
cx = x * 0.5
cam.location = (cx, -x * 0.75 - 60, max(20.0, x * 0.12))
cam.rotation_euler = (math.radians(80), 0, 0)
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("[preview] wrote", out)

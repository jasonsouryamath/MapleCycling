import bpy
import math
import sys

argv = sys.argv[sys.argv.index("--") + 1:]
glb_path = argv[0]
out_path = argv[1]

# reset scene
bpy.ops.wm.read_factory_settings(use_empty=True)

bpy.ops.import_scene.gltf(filepath=glb_path)

# compute bounds of imported objects
import mathutils
objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
min_v = mathutils.Vector((1e9, 1e9, 1e9))
max_v = mathutils.Vector((-1e9, -1e9, -1e9))
for o in objs:
    for corner in o.bound_box:
        world = o.matrix_world @ mathutils.Vector(corner)
        min_v.x = min(min_v.x, world.x); min_v.y = min(min_v.y, world.y); min_v.z = min(min_v.z, world.z)
        max_v.x = max(max_v.x, world.x); max_v.y = max(max_v.y, world.y); max_v.z = max(max_v.z, world.z)

centre = (min_v + max_v) * 0.5
size = max_v - min_v
radius = max(size.x, size.z, 1.0)
height = size.y

# apply the actual rock texture so we can see if the "gaps" only appear once this specific
# camo texture + directional lighting is combined with the mesh (not a geometry hole).
tex_path = argv[2] if len(argv) > 2 else None
if tex_path:
    img = bpy.data.images.load(tex_path)
    for o in objs:
        mat = bpy.data.materials.new(name="RockTest")
        mat.use_nodes = True
        mat.use_backface_culling = "--cull" in argv  # mirror Unity's SakuraCel _Cull=Back
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        tex_node = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex_node.image = img
        mat.node_tree.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
        o.data.materials.clear()
        o.data.materials.append(mat)

light_data = bpy.data.lights.new(name="Sun", type='SUN')
light_data.energy = 3.0
light_obj = bpy.data.objects.new(name="Sun", object_data=light_data)
bpy.context.collection.objects.link(light_obj)
light_obj.rotation_euler = (math.radians(55), 0, math.radians(35))

# fill light
light_data2 = bpy.data.lights.new(name="Fill", type='SUN')
light_data2.energy = 1.2
light_obj2 = bpy.data.objects.new(name="Fill", object_data=light_data2)
bpy.context.collection.objects.link(light_obj2)
light_obj2.rotation_euler = (math.radians(110), 0, math.radians(-100))

# camera
cam_data = bpy.data.cameras.new("Cam")
cam_obj = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam_obj)
dist = radius * 3.0 + 4.0
cam_obj.location = (centre.x + dist, centre.y + height * 0.15, centre.z + dist * 0.15)
bpy.context.scene.camera = cam_obj

# point camera at centre using track-to constraint
target = bpy.data.objects.new("Target", None)
target.location = centre
bpy.context.collection.objects.link(target)
con = cam_obj.constraints.new(type='TRACK_TO')
con.target = target
con.track_axis = 'TRACK_NEGATIVE_Z'
con.up_axis = 'UP_Y'

scene = bpy.context.scene
try:
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
except TypeError:
    scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 900
scene.render.resolution_y = 700
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = out_path
scene.world = bpy.data.worlds.new("World")
scene.world.use_nodes = True
bg = scene.world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.55, 0.65, 0.75, 1.0)
    bg.inputs[1].default_value = 1.0

for angle_deg in [0, 45, 90, 135, 180, 225, 270, 315]:
    ang = math.radians(angle_deg)
    cam_obj.location = (centre.x + math.cos(ang) * dist, centre.y + height * 0.15,
                        centre.z + math.sin(ang) * dist)
    scene.render.filepath = out_path.replace(".png", f"_{angle_deg:03d}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[isolate] wrote {scene.render.filepath}")


"""Quick isolate render of a Minato FBX kit piece. blender -b -P preview_minato_city.py -- <fbx> <out.png>"""
import bpy, math, sys, mathutils

argv = sys.argv[sys.argv.index("--") + 1:]
fbx_path, out_path = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx_path)

objs = [o for o in bpy.context.scene.objects if o.type == 'MESH' and "_LOD0" in o.name]
if not objs:
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and o not in objs:
        bpy.data.objects.remove(o, do_unlink=True)

mn = mathutils.Vector((1e9, 1e9, 1e9))
mx = mathutils.Vector((-1e9, -1e9, -1e9))
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(c)
        for i in range(3):
            mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
centre = (mn + mx) * 0.5
size = mx - mn
radius = max(size.x, size.y, size.z, 1.0)

for (name, energy, rot) in (("Sun", 4.0, (55, 0, 35)), ("Fill", 1.4, (110, 0, -100))):
    d = bpy.data.lights.new(name=name, type='SUN'); d.energy = energy
    o = bpy.data.objects.new(name=name, object_data=d)
    bpy.context.collection.objects.link(o)
    o.rotation_euler = tuple(math.radians(a) for a in rot)

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
bpy.context.scene.camera = cam
target = bpy.data.objects.new("Target", None); target.location = centre
bpy.context.collection.objects.link(target)
con = cam.constraints.new(type='TRACK_TO'); con.target = target
con.track_axis = 'TRACK_NEGATIVE_Z'; con.up_axis = 'UP_Y'

scene = bpy.context.scene
try:
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
except TypeError:
    scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 700
scene.render.resolution_y = 700
scene.render.image_settings.file_format = 'PNG'
scene.world = bpy.data.worlds.new("World"); scene.world.use_nodes = True
bg = scene.world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.55, 0.68, 0.82, 1.0)

dist = radius * 1.9 + 3.0
for ang_deg in (35, 125):
    a = math.radians(ang_deg)
    cam.location = (centre.x + math.cos(a) * dist, centre.y + math.sin(a) * dist,
                    centre.z + size.z * 0.18)
    scene.render.filepath = out_path.replace(".png", f"_{ang_deg:03d}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[preview] wrote {scene.render.filepath}")

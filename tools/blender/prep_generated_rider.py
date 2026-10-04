"""
Prep an image-to-3D generated rider (Hunyuan3D / Meshy style raw mesh) for the MapleRide rig.
  blender -b -P tools/blender/prep_generated_rider.py -- <raw.glb> <out.blend|.glb> [--height 1.366] [--voxel 0.004] [--tris 40000]
Steps: join -> scale/centre onto the donor rig's frame (feet z=-0.034, face -Y, x centre 0.011) -> voxel remesh (kills hair wires and
non-manifold junk) -> decimate to a hero budget. UV / texture / skinning happen in later scripts.
"""
import bpy, bmesh, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[0], argv[1]
opt = {argv[i][2:]: argv[i + 1] for i in range(2, len(argv) - 1) if argv[i].startswith('--')}
HEIGHT = float(opt.get('height', '1.366'))
ZMIN = float(opt.get('zmin', '-0.034'))
VOX = float(opt.get('voxel', '0.004'))
TRIS = int(opt.get('tris', '40000'))
XC, YC = float(opt.get('xc', '0.011')), float(opt.get('yc', '0.024'))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
objs = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = objs[0]
if len(objs) > 1:
    bpy.ops.object.join()
ob = bpy.context.view_layer.objects.active
ob.name = 'GenRider'
# bake the import transform into the vertices
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
me = ob.data
mn = Vector((min(v.co[i] for v in me.vertices) for i in range(3)))
mx = Vector((max(v.co[i] for v in me.vertices) for i in range(3)))
print('RAW bbox', tuple(round(x, 3) for x in mn), tuple(round(x, 3) for x in mx), 'tris', sum(len(p.vertices) - 2 for p in me.polygons))
s = HEIGHT / (mx.z - mn.z)
cx, cy = (mn.x + mx.x) / 2, (mn.y + mx.y) / 2
for v in me.vertices:
    v.co = Vector(((v.co.x - cx) * s + XC, (v.co.y - cy) * s + YC, (v.co.z - mn.z) * s + ZMIN))
me.update()
# voxel remesh removes hair wires / floating junk, then decimate
me.remesh_voxel_size = VOX
me.remesh_voxel_adaptivity = 0.0
bpy.ops.object.voxel_remesh()
print('after voxel', len(me.polygons))
# keep only the biggest connected piece
bm = bmesh.new()
bm.from_mesh(me)
seen = set()
comps = []
for v in bm.verts:
    if v.index in seen:
        continue
    st = [v]
    seen.add(v.index)
    c = []
    while st:
        x = st.pop()
        c.append(x)
        for e in x.link_edges:
            y = e.other_vert(x)
            if y.index not in seen:
                seen.add(y.index)
                st.append(y)
    comps.append(c)
comps.sort(key=len, reverse=True)
junk = [v for c in comps[1:] for v in c]
bmesh.ops.delete(bm, geom=junk, context='VERTS')
print('components', len(comps), 'removed verts', len(junk))
bm.to_mesh(me)
bm.free()
n = sum(len(p.vertices) - 2 for p in me.polygons)
m = ob.modifiers.new('dec', 'DECIMATE')
m.ratio = min(1.0, TRIS / n)
bpy.ops.object.modifier_apply(modifier='dec')
bpy.ops.object.shade_smooth()
print('FINAL tris', sum(len(p.vertices) - 2 for p in me.polygons), 'verts', len(me.vertices))
if OUT.endswith('.glb'):
    bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB')
else:
    bpy.ops.wm.save_as_mainfile(filepath=OUT)
print('WROTE', OUT)

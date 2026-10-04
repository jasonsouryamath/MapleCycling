"""
Unwrap a prepped generated rider and dump per-triangle UV / position / normal for numpy texture projection.
  blender -b -P tools/blender/uv_unwrap_generated.py -- <prepped.glb> <out_prefix>
Writes <out_prefix>_uv.glb (mesh with UVMap) and <out_prefix>_tris.npz (uv, pos, nrm per triangle corner).
"""
import bpy, bmesh, sys, math
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, PRE = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
for m in list(ob.data.materials):
    pass
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(70), island_margin=0.0025, area_weight=0.4)
bpy.ops.uv.pack_islands(margin=0.002)
bpy.ops.object.mode_set(mode='OBJECT')
me = ob.data
me.calc_loop_triangles()
uvl = me.uv_layers.active.data
uv, pos, nrm = [], [], []
for t in me.loop_triangles:
    uv.append([tuple(uvl[l].uv) for l in t.loops])
    pos.append([tuple(me.vertices[v].co) for v in t.vertices])
    nrm.append([tuple(me.vertices[v].normal) for v in t.vertices])
np.savez(PRE + '_tris.npz', uv=np.array(uv, np.float32), pos=np.array(pos, np.float32), nrm=np.array(nrm, np.float32))
print('TRIS', len(uv))
bpy.ops.export_scene.gltf(filepath=PRE + '_uv.glb', export_format='GLB')
print('WROTE', PRE)

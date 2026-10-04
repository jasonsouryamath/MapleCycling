"""
Seam-safe Taubin smoothing of a region of a textured generated rider (positions only; UVs untouched).
  blender -b -P tools/blender/smooth_region.py -- <in.glb> <out.glb> [--zmin 1.03 --iters 40]
Vertices that share a position (split at UV seams) are smoothed as one so seams do not open.
"""
import bpy, sys
import numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[:2]
opt = {argv[i][2:]: float(argv[i + 1]) for i in range(2, len(argv) - 1) if argv[i].startswith('--')}
ZMIN, IT = opt.get('zmin', 1.03), int(opt.get('iters', 40))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
me = ob.data
V = np.array([tuple(v.co) for v in me.vertices], np.float64)
key = np.round(V * 1e5).astype(np.int64)
_, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
inv = inv.reshape(-1)
U = V[first]
e = np.array([tuple(x.vertices) for x in me.edges])
a, b = inv[e[:, 0]], inv[e[:, 1]]
pair = np.unique(np.sort(np.stack([a, b], 1), axis=1), axis=0)
a, b = pair[:, 0], pair[:, 1]
deg = np.zeros(len(U))
np.add.at(deg, a, 1)
np.add.at(deg, b, 1)
deg[deg == 0] = 1
mask = U[:, 2] > ZMIN
for _ in range(IT):
    for f in (0.5, -0.53):
        acc = np.zeros_like(U)
        np.add.at(acc, a, U[b])
        np.add.at(acc, b, U[a])
        acc /= deg[:, None]
        U[mask] += f * (acc[mask] - U[mask])
V2 = U[inv]
for i, v in enumerate(me.vertices):
    v.co = V2[i]
me.update()
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB')
print('smoothed', int(mask.sum()), 'unique verts; WROTE', OUT)

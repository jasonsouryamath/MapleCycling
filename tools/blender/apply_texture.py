"""blender -b -P tools/blender/apply_texture.py -- <uv.glb> <atlas.png> <out.glb> [--size 2048]  : bind an atlas to the single mesh and export."""
import bpy, sys
argv = sys.argv[sys.argv.index('--') + 1:]
SRC, ATLAS, OUT = argv[:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
import os
img = bpy.data.images.load(os.path.abspath(ATLAS))
img.colorspace_settings.name = 'sRGB'
if '--size' in argv:
    n = int(argv[argv.index('--size') + 1])
    img.scale(n, n)
img.pack()
m = bpy.data.materials.new('Kuro_Rider')
m.use_nodes = True
b = m.node_tree.nodes['Principled BSDF']
t = m.node_tree.nodes.new('ShaderNodeTexImage')
t.image = img
m.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color'])
b.inputs['Roughness'].default_value = 0.55
ob.data.materials.clear()
ob.data.materials.append(m)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB')
print('WROTE', OUT)

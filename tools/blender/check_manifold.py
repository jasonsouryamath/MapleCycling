import bpy, bmesh, sys

argv = sys.argv[sys.argv.index("--") + 1:]
glb_path = argv[0]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb_path)

for o in bpy.context.scene.objects:
    if o.type != 'MESH':
        continue
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.edges.ensure_lookup_table()
    non_manifold = [e for e in bm.edges if not e.is_manifold]
    boundary = [e for e in bm.edges if len(e.link_faces) < 2]
    print(f"[check] object={o.name} verts={len(bm.verts)} faces={len(bm.faces)} "
          f"edges={len(bm.edges)} non_manifold_edges={len(non_manifold)} boundary_edges={len(boundary)}")
    # report a few boundary edge locations if any
    for e in boundary[:20]:
        v0, v1 = e.verts
        print(f"    boundary edge at {tuple(round(c,2) for c in v0.co)} -> {tuple(round(c,2) for c in v1.co)}")
    bm.free()

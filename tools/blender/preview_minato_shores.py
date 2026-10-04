"""
MINATO COAST - Shores workstream contact sheet.

Imports a list of props (FBX from MinatoCoast/Models or any GLB path), keeps LOD0 only (or the
single mesh), lays them out left-to-right on a ground plane and renders one EEVEE frame, so the
silhouette/scale/palette of shore, marina and headland assets can be judged BEFORE they reach
Unity.

Usage:
  blender -b --factory-startup -P preview_minato_shores.py -- out.png asset1 asset2 ...
Assets: a bare FBX name (looked up in MinatoCoast/Models) or an absolute .glb/.fbx path.
Optional: --eye x,y,z --look x,y,z --fov deg --gap m
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402


def _args():
    argv = sys.argv[sys.argv.index("--") + 1:]
    opts = {"eye": None, "look": None, "fov": 40.0, "gap": 4.0, "sun": 30.0}
    items = []
    it = iter(argv[1:])
    for a in it:
        if a.startswith("--"):
            opts[a[2:]] = next(it)
        else:
            items.append(a)
    return argv[0], items, opts


def _import(spec):
    path = spec
    if not os.path.isabs(spec):
        path = os.path.join(M.models_dir(), spec if spec.endswith(".fbx") else spec + ".fbx")
    before = set(bpy.data.objects)
    if path.lower().endswith(".glb"):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    lod0 = [o for o in meshes if "_LOD0" in o.name]
    keep = lod0 if lod0 else [o for o in meshes if "_LOD" not in o.name] or meshes
    for o in new:
        if o not in keep and o.type == 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    return keep


def _bounds(objs):
    bpy.context.view_layer.update()
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            mn = Vector((min(mn.x, w.x), min(mn.y, w.y), min(mn.z, w.z)))
            mx = Vector((max(mx.x, w.x), max(mx.y, w.y), max(mx.z, w.z)))
    return mn, mx


def main():
    out, items, opts = _args()
    S.reset_scene()
    x = 0.0
    total_tris = 0
    for spec in items:
        objs = _import(spec)
        if not objs:
            print(f"[shores-preview] {spec}: nothing imported")
            continue
        mn, mx = _bounds(objs)
        w = mx.x - mn.x
        shift = Vector((x - mn.x, -(mn.y + mx.y) * 0.5, 0.0))
        for o in objs:
            o.location += shift
            o.data.calc_loop_triangles()
            total_tris += len(o.data.loop_triangles)
        tris = sum(len(o.data.loop_triangles) for o in objs)
        print(f"[shores-preview] {os.path.basename(spec):<40s} w={w:6.1f} d={mx.y-mn.y:6.1f} "
              f"h={mx.z-mn.z:6.1f} (blender z-up)  tris={tris:,}")
        x += w + float(opts["gap"])

    g = M.box("Ground", (0.0, -0.05, 0.0), (4000.0, 0.1, 4000.0), uv_per_m=0.05)
    M.paint(g, "sand")

    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x, scn.render.resolution_y = 1600, 800
    scn.world = bpy.data.worlds.new("W")
    scn.world.use_nodes = True
    bg = scn.world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.68, 0.85, 1.0)
    bg.inputs[1].default_value = 0.55
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    sun.data.energy = 3.2
    sun.rotation_euler = (math.radians(55.0), 0.0, math.radians(float(opts["sun"])))
    S.link(sun)

    cd = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cd)
    S.link(cam)
    scn.camera = cam
    cd.angle = math.radians(float(opts["fov"]))
    cd.clip_end = 20000.0
    span = max(x, 10.0)
    if opts["eye"]:
        eye = Vector([float(v) for v in opts["eye"].split(",")])
        look = Vector([float(v) for v in opts["look"].split(",")])
    else:
        look = Vector((span * 0.5, 0.0, span * 0.06))
        eye = Vector((span * 0.5, -span * 0.95, span * 0.30))
    cam.location = eye
    cam.rotation_euler = (look - eye).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print(f"[shores-preview] -> {out}  ({total_tris:,} tris total)")


if __name__ == "__main__":
    main()

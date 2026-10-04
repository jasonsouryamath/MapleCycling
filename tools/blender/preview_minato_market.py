"""
MINATO COAST - cafe / market district contact sheet.

Re-imports the milestone-2 market assets and renders them at two distances: a 14 m "shopper's
eye" pass that shows whether the stripes, awnings and chairs actually read, and a 45 m pass at
roughly the distance the boulevard cameras see the plaza from.

Renders to reference/good_graphics/minato_market_*.png.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402

OUT = os.path.join(S.repo_root(), "reference", "good_graphics")

ROW = [
    ("Minato_City_MarketStall.fbx",  -9.0, 0.0),
    ("Minato_City_MarketStallB.fbx", -4.5, 0.0),
    ("Minato_City_MarketTent.fbx",    0.6, 0.0),
    ("Minato_City_CafeSet.fbx",       5.4, 0.0),
    ("Minato_City_CafeSetB.fbx",      9.2, 0.0),
    ("Minato_City_CratePile.fbx",    12.2, 0.0),
]


def place(filename, x, z):
    path = os.path.join(M.models_dir(), filename)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    keep = None
    for o in new:
        if o.name.endswith("_LOD0"):
            keep = o
        else:
            bpy.data.objects.remove(o, do_unlink=True)
    if keep is None:
        raise RuntimeError(f"{filename}: no LOD0")
    S.select_only(keep)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    keep.location = Vector(M.u2b(x, 0.0, z))
    d = keep.dimensions
    print(f"[minato] {filename:<34s} h={d.z:5.2f} m  footprint {d.x:5.2f} x {d.y:5.2f}")
    return keep


def scene(tag, eye, look, fov):
    S.reset_scene()
    for (fn, x, z) in ROW:
        place(fn, x, z)

    g = M.box("Ground", (0.0, -0.1, 0.0), (300.0, 0.2, 300.0), uv_per_m=0.2)
    M.paint(g, "concrete")

    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x, scn.render.resolution_y = 1900, 850
    scn.world = bpy.data.worlds.new("W")
    scn.world.use_nodes = True
    bg = scn.world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.48, 0.62, 0.80, 1.0)
    bg.inputs[1].default_value = 1.6

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    sun.data.energy = 4.2
    sun.data.angle = math.radians(1.4)
    sun.rotation_euler = (math.radians(50.0), 0.0, math.radians(-126.0))
    S.link(sun)

    cd = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cd)
    S.link(cam)
    scn.camera = cam
    cam.location = Vector(M.u2b(*eye))
    cd.angle = math.radians(fov)
    cd.clip_end = 5000.0
    d = Vector(M.u2b(*look)) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

    scn.render.filepath = os.path.join(OUT, f"minato_market_{tag}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[minato] market render -> minato_market_{tag}.png")


def main():
    os.makedirs(OUT, exist_ok=True)
    scene("near", (1.0, 3.0, 14.5), (1.0, 1.4, 0.0), 52.0)
    scene("far", (2.0, 9.0, 45.0), (1.0, 1.6, 0.0), 40.0)


if __name__ == "__main__":
    main()

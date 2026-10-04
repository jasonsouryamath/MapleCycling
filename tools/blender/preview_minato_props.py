"""
MINATO COAST - prop contact sheet.

Re-imports every exported prop FBX and lays it out on a ground plane in labelled rows, then
renders two views: a port-city cluster and a shore/sea/mountain cluster, both at plausible
in-game viewing distances.

The point is silhouette and scale checking in context. A prop family that exports with correct
bounds can still be unrecognisable, wrongly proportioned against its neighbours, or - the
classic - built around the wrong origin so it hovers or sinks. None of that shows in a tri
count.

Renders to reference/good_graphics/minato_props_*.png.
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

# (filename, unity x, unity z) - laid out so neighbours make sense to compare.
PORT = [
    ("Minato_Port_Tower.fbx",          -120.0,  60.0),
    ("Minato_Port_FerrisWheel.fbx",     -40.0,  60.0),
    ("Minato_Port_ContainerCrane.fbx",   50.0,  70.0),
    ("Minato_Port_Warehouse.fbx",       130.0,  60.0),
    ("Minato_City_BlockB.fbx",          -90.0,   0.0),
    ("Minato_City_BlockA.fbx",          -45.0,   0.0),
    ("Minato_City_BlockC.fbx",            5.0,   0.0),
    ("Minato_City_ShopHouse.fbx",        45.0,   0.0),
    ("Minato_Flora_Palm.fbx",            62.0,   0.0),
    ("Minato_Port_StreetLamp.fbx",       72.0,   0.0),
    ("Minato_Port_Bollard.fbx",          80.0,   0.0),
    ("Minato_Port_CrateStack.fbx",       98.0,   0.0),
    ("Minato_Sea_FishingBoat.fbx",      -80.0, -45.0),
    ("Minato_Sea_Sailboat.fbx",         -55.0, -45.0),
    ("Minato_Sea_Ferry.fbx",            -10.0, -50.0),
    ("Minato_Sea_CargoShip.fbx",        110.0, -70.0),
]

SHORE = [
    ("Minato_Sea_Lighthouse.fbx",       -95.0,  40.0),
    ("Minato_Sea_SeaStack.fbx",         -55.0,  45.0),
    ("Minato_Sea_IsletA.fbx",            10.0,  70.0),
    ("Minato_Sea_IsletB.fbx",           150.0,  90.0),
    ("Minato_Shore_VillaC.fbx",         -95.0, -10.0),
    ("Minato_Shore_VillaA.fbx",         -65.0, -10.0),
    ("Minato_Shore_VillaB.fbx",         -42.0, -10.0),
    ("Minato_Road_MasonryWall.fbx",     -20.0, -10.0),
    ("Minato_Road_GuardrailPost.fbx",    -8.0, -10.0),
    ("Minato_Road_RockOutcrop.fbx",       8.0, -12.0),
    ("Minato_Flora_Conifer.fbx",         32.0, -10.0),
    ("Minato_Flora_Broadleaf.fbx",       52.0, -10.0),
    ("Minato_Flora_Shrub.fbx",           66.0, -10.0),
]


def place(filename, x, z):
    path = os.path.join(M.models_dir(), filename)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    keep = None
    for o in new:
        if "_LOD0" in o.name:
            keep = o
        else:
            bpy.data.objects.remove(o, do_unlink=True)
    if keep is None:
        raise RuntimeError(f"{filename}: no LOD0")
    S.select_only(keep)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    keep.location = Vector(M.u2b(x, 0.0, z))
    d = keep.dimensions
    # Blender Z is Unity up: report the in-game height, which is the number that decides
    # whether a prop reads at its intended distance.
    print(f"[minato] {filename:<36s} h={d.z:6.1f} m  footprint {d.x:6.1f} x {d.y:6.1f}")
    return keep


def scene(tag, table, eye, look, fov, ground_slot="sand"):
    S.reset_scene()
    for (fn, x, z) in table:
        place(fn, x, z)

    g = M.box("Ground", (0.0, -0.1, 0.0), (900.0, 0.2, 900.0), uv_per_m=0.05)
    M.paint(g, ground_slot)

    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x, scn.render.resolution_y = 1900, 850
    scn.world = bpy.data.worlds.new("W")
    scn.world.use_nodes = True
    bg = scn.world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.45, 0.58, 0.74, 1.0)
    bg.inputs[1].default_value = 1.5

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    sun.data.energy = 4.5
    sun.data.angle = math.radians(1.2)
    sun.rotation_euler = (math.radians(52.0), 0.0, math.radians(-118.0))
    S.link(sun)

    cd = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cd)
    S.link(cam)
    scn.camera = cam
    cam.location = Vector(M.u2b(*eye))
    cd.angle = math.radians(fov)
    cd.clip_end = 20000.0
    d = Vector(M.u2b(*look)) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

    scn.render.filepath = os.path.join(OUT, f"minato_props_{tag}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[minato] props render -> minato_props_{tag}.png")


def main():
    os.makedirs(OUT, exist_ok=True)
    scene("port", PORT, (-30.0, 46.0, -230.0), (10.0, 18.0, 20.0), 52.0, "asphalt")
    scene("shore", SHORE, (-20.0, 34.0, -150.0), (10.0, 10.0, 20.0), 52.0, "foliage")


if __name__ == "__main__":
    main()

"""
Headless preview renderer for the Sakura Pass asset pipeline.

Imports one or more exported GLBs, frames them with a sunset key light and a gradient world, and
renders a PNG so the geometry can be reviewed without opening Unity.

Usage:
    blender -b -P render_preview.py -- <out.png> <asset.glb> [more.glb ...]
                                      [--cam x,y,z] [--look x,y,z] [--lens 35] [--res 1600x900]

Camera coordinates are given in **Unity** space to match the rest of the pipeline.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
from mathutils import Vector

import sakura_lib as S


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []

    out = argv[0] if argv else "preview.png"
    glbs = []
    opts = {"cam": (-2, 4, -186), "look": (6, 2, -60), "lens": 32.0, "res": (1600, 900),
            "sun": (14.0, 28.0)}

    i = 1
    while i < len(argv):
        a = argv[i]
        if a == "--cam":
            opts["cam"] = tuple(float(x) for x in argv[i + 1].split(",")); i += 2
        elif a == "--look":
            opts["look"] = tuple(float(x) for x in argv[i + 1].split(",")); i += 2
        elif a == "--lens":
            opts["lens"] = float(argv[i + 1]); i += 2
        elif a == "--res":
            w, h = argv[i + 1].lower().split("x")
            opts["res"] = (int(w), int(h)); i += 2
        elif a == "--sun":
            opts["sun"] = tuple(float(x) for x in argv[i + 1].split(",")); i += 2
        else:
            glbs.append(a); i += 1

    return out, glbs, opts


def build_world():
    """Sunset gradient world so previews read like the in-game lighting."""
    world = bpy.data.worlds.new("SakuraPreview")
    bpy.context.scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    tex = nt.nodes.new("ShaderNodeTexCoord")
    mapr = nt.nodes.new("ShaderNodeMapRange")

    nt.links.new(tex.outputs["Generated"], sep.inputs["Vector"])
    nt.links.new(sep.outputs["Z"], mapr.inputs["Value"])
    mapr.inputs["From Min"].default_value = -0.25
    mapr.inputs["From Max"].default_value = 0.55
    nt.links.new(mapr.outputs["Result"], ramp.inputs["Fac"])

    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.85, 0.44, 0.28, 1.0)
    ramp.color_ramp.elements[1].position = 1.0
    ramp.color_ramp.elements[1].color = (0.10, 0.14, 0.38, 1.0)
    mid = ramp.color_ramp.elements.new(0.42)
    mid.color = (0.42, 0.36, 0.55, 1.0)

    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 1.1
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])


def main():
    out, glbs, opts = parse_args()

    S.reset_scene()
    build_world()

    for g in glbs:
        # "file.glb@x,y,z" places the asset at a Unity-space offset so several assets can be
        # laid out side by side in one preview.
        offset = (0.0, 0.0, 0.0)
        if "@" in g:
            g, off = g.rsplit("@", 1)
            offset = tuple(float(x) for x in off.split(","))
        path = g if os.path.isabs(g) else os.path.join(S.blender_assets_dir(), g)
        if not os.path.exists(path):
            print(f"[sakura] WARNING missing {path}")
            continue
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        if any(offset):
            bo = S.u2b_vec(offset)
            for o in set(bpy.data.objects) - before:
                if o.parent is None:
                    o.location = (o.location[0] + bo[0], o.location[1] + bo[1],
                                  o.location[2] + bo[2])
        print(f"[sakura] imported {os.path.basename(path)}")

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x, scene.render.resolution_y = opts["res"]
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX' if 'AgX' in [
        t.name for t in scene.view_settings.bl_rna.properties['view_transform'].enum_items
    ] else 'Standard'
    scene.view_settings.look = 'None'

    try:
        scene.eevee.use_shadows = True
        scene.eevee.use_raytracing = True
    except Exception:
        pass

    # --- sun -----------------------------------------------------------------------
    sun_data = bpy.data.lights.new("Sun", type='SUN')
    sun_data.energy = 4.5
    sun_data.color = (1.0, 0.76, 0.56)
    sun_data.angle = math.radians(1.8)
    sun = bpy.data.objects.new("Sun", sun_data)
    S.link(sun)
    elev, azim = opts["sun"]
    sun.rotation_euler = (math.radians(90.0 - elev), 0.0, math.radians(azim))

    fill_data = bpy.data.lights.new("Fill", type='SUN')
    fill_data.energy = 1.1
    fill_data.color = (0.48, 0.60, 0.95)
    fill = bpy.data.objects.new("Fill", fill_data)
    S.link(fill)
    fill.rotation_euler = (math.radians(115.0), 0.0, math.radians(azim + 180))

    # --- camera (Unity coords in, Blender coords out) --------------------------------
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = opts["lens"]
    cam_data.clip_end = 5000.0
    cam = bpy.data.objects.new("Cam", cam_data)
    S.link(cam)
    scene.camera = cam

    cpos = Vector(S.u2b(*opts["cam"]))
    tpos = Vector(S.u2b(*opts["look"]))
    cam.location = cpos
    direction = (tpos - cpos).normalized()
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

    out_path = out if os.path.isabs(out) else os.path.join(S.repo_root(), "tools", "previews", out)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    scene.render.filepath = out_path
    scene.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    print(f"[sakura] preview -> {out_path}")


if __name__ == "__main__":
    main()

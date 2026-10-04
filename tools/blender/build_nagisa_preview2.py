"""
NAGISA BAY (B5) PASS 2 - TEXTURED review turntables for the pass-2 GLBs (review aid, not shipped).

Imports a GLB, keeps _LOD<n> (default 0), binds the NB_* texture sets by slot name (approximating
the Unity CelLit / HDRP-Lit remap in NagisaBayEnvironment.*.cs), lights with a warm sun + sky and
renders EEVEE views to reference/good_graphics/nagisa_bay/p2_<name>_<view>.png with backface
culling ON, so a flipped face shows as a hole.

Run: blender.exe -b -P tools/blender/build_nagisa_preview2.py -- [--lod 1] Nagisa_B_Condo1 [...]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODELS = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Models")
TEX = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures")
OUT = os.path.join(ROOT, "reference", "good_graphics", "nagisa_bay")

# slot -> (texture stem or None, tint rgb, roughness, alpha, emission)
SLOTS = {
    "NB_Wall": ("NB_Wall", (0.97, 0.95, 0.90), 0.8, 1, 0), "NB_Accent": ("NB_Wall", (0.80, 0.74, 0.64), 0.8, 1, 0),
    "NB_WallBoard": ("NB_WallBoard", (1, 1, 1), 0.8, 1, 0), "NB_Trim": ("NB_Trim", (1, 1, 1), 0.5, 1, 0),
    "NB_Interior": ("NB_Interior", (1, 1, 1), 0.08, 1, 1), "NB_ShopInterior": ("NB_ShopInterior", (1, 1, 1), 0.08, 1, 1),
    "NB_Awning": ("NB_Awning", (1, 1, 1), 0.9, 1, 0), "NB_Signs2": ("NB_Signs2", (1, 1, 1), 0.5, 1, 0), "NB_Signs3": ("NB_Signs3", (1, 1, 1), 0.5, 1, 0),
    "NB_Paving": ("NB_Paving", (1, 1, 1), 0.8, 1, 0), "NB_PlazaStone": ("NB_PlazaStone", (1, 1, 1), 0.8, 1, 0),
    "NB_Kerb": ("NB_Kerb", (1, 1, 1), 0.8, 1, 0), "NB_Lawn": ("NB_Lawn", (1, 1, 1), 0.9, 1, 0),
    "NB_Groundcover": ("NB_Groundcover", (1, 1, 1), 0.9, 1, 0), "NB_Gravel": ("NB_Gravel", (1, 1, 1), 0.9, 1, 0),
    "NB_Hedge": ("NB_Hedge", (1, 1, 1), 0.9, 1, 0), "NB_Solar": ("NB_Solar", (1, 1, 1), 0.2, 1, 0),
    "NB_MetalRoof": ("NB_MetalRoof", (1, 1, 1), 0.5, 1, 0), "NB_Asphalt2": ("NB_Asphalt2", (1, 1, 1), 0.9, 1, 0),
    "NB_Terrazzo": ("NB_Terrazzo", (1, 1, 1), 0.4, 1, 0), "NB_Boardwalk": ("NB_Boardwalk", (1, 1, 1), 0.8, 1, 0),
    "NB_Stone": ("NB_Stone", (0.92, 0.88, 0.80), 0.8, 1, 0), "NB_DeckStone": ("NB_DeckStone", (1, 1, 1), 0.8, 1, 0),
    "NB_Teak": ("NB_Teak", (1, 1, 1), 0.6, 1, 0), "NB_Plaster": ("NB_Plaster", (0.95, 0.94, 0.9), 0.9, 1, 0),
    "NB_RoofTile": ("NB_RoofTile", (1, 1, 1), 0.8, 1, 0), "NB_Canvas": ("NB_Canvas", (1, 1, 1), 0.9, 1, 0),
    "NB_Planter": ("NB_Stone", (0.82, 0.78, 0.72), 0.8, 1, 0), "NB_Soil": (None, (0.28, 0.22, 0.16), 1, 1, 0),
    "NB_Hibiscus": ("NB_Hibiscus", (1, 1, 1), 0.8, 0, 0), "NB_Bougainvillea": ("NB_Bougainvillea", (1, 1, 1), 0.8, 0, 0),
    "NB_PalmFrond": ("NB_PalmFrond", (1, 1, 1), 0.8, 0, 0), "NB_FanFrond": ("NB_FanFrond", (1, 1, 1), 0.8, 0, 0),
    "NB_Canopy": ("NB_Canopy", (1, 1, 1), 0.8, 0, 0), "NB_PalmBark": ("NB_PalmBark", (1, 1, 1), 0.9, 1, 0),
    "NB_BalconyGlass": (None, (0.62, 0.82, 0.80), 0.05, 0.3, 0), "NB_PoolWater": (None, (0.2, 0.72, 0.8), 0.05, 0.8, 0),
    "NB_Glass": (None, (0.2, 0.35, 0.4), 0.05, 1, 0), "NB_Concrete": ("NB_Plaster", (0.70, 0.69, 0.66), 0.9, 1, 0),
    "NB_Bronze": ("NB_Bronze", (1, 1, 1), 0.4, 1, 0), "NB_PoolTile": ("NB_PoolTile", (1, 1, 1), 0.3, 1, 0),
    "NB_CarPaint": (None, (0.85, 0.85, 0.86), 0.25, 1, 0), "NB_CarGlass": (None, (0.08, 0.1, 0.12), 0.05, 1, 0),
    "NB_Rubber": (None, (0.06, 0.06, 0.06), 0.9, 1, 0), "NB_Chrome": (None, (0.75, 0.76, 0.78), 0.15, 1, 0),
    "NB_Thatch": ("NB_Thatch", (1, 1, 1), 0.95, 1, 0), "NB_SkyGlass": ("NB_Interior", (1, 1, 1), 0.05, 1, 2), "NB_Lamp": (None, (1, 0.9, 0.6), 0.5, 1, 1),
    "NB_Gelcoat": ("NB_Gelcoat", (0.94, 0.94, 0.93), 0.3, 1, 0), "NB_Metal": (None, (0.62, 0.64, 0.66), 0.4, 1, 0),
    "NB_Paint": (None, (0.86, 0.22, 0.18), 0.4, 1, 0), "NB_PaintTeal": (None, (0.18, 0.62, 0.62), 0.4, 1, 0),
    "NB_Superstructure": ("NB_Superstructure", (1, 1, 1), 0.3, 1, 0), "NB_PontoonWood": ("NB_PontoonWood", (1, 1, 1), 0.8, 1, 0),
}


def tex(stem, kind):
    p = os.path.join(TEX, "%s_%s.png" % (stem, kind))
    if not os.path.exists(p):
        return None
    img = bpy.data.images.load(p, check_existing=True)
    if kind != "Albedo" and kind != "Emission":
        img.colorspace_settings.name = "Non-Color"
    return img


def bind(m):
    key = m.name.split(".")[0]
    spec = SLOTS.get(key)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bs.outputs[0], out.inputs[0])
    m.use_backface_culling = True
    if spec is None:
        bs.inputs["Base Color"].default_value = (1, 0, 1, 1)
        return
    stem, tint, rough, alpha, emis = spec
    bs.inputs["Roughness"].default_value = rough
    mix = nt.nodes.new("ShaderNodeRGB")
    mix.outputs[0].default_value = (tint[0], tint[1], tint[2], 1)
    if stem:
        a = tex(stem, "Albedo")
        ti = nt.nodes.new("ShaderNodeTexImage"); ti.image = a
        mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        nt.links.new(ti.outputs["Color"], mul.inputs[6]); nt.links.new(mix.outputs[0], mul.inputs[7])
        nt.links.new(mul.outputs[2], bs.inputs["Base Color"])
        if alpha == 0:
            nt.links.new(ti.outputs["Alpha"], bs.inputs["Alpha"])
            m.blend_method = "CLIP" if hasattr(m, "blend_method") else None
            m.use_backface_culling = False
        n = tex(stem, "Normal")
        if n is not None:
            tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = n
            nm = nt.nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = 0.8
            nt.links.new(tn.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs[0], bs.inputs["Normal"])
        if emis:
            e = tex(stem, "Emission")
            if e is not None:
                te = nt.nodes.new("ShaderNodeTexImage"); te.image = e
                nt.links.new(te.outputs["Color"], bs.inputs["Emission Color"])
                bs.inputs["Emission Strength"].default_value = 0.35 * emis
            bs.inputs["Specular IOR Level"].default_value = 0.8
    else:
        nt.links.new(mix.outputs[0], bs.inputs["Base Color"])
        if emis:
            bs.inputs["Emission Color"].default_value = (tint[0], tint[1], tint[2], 1)
            bs.inputs["Emission Strength"].default_value = 2.0
    if alpha not in (0, 1):
        bs.inputs["Alpha"].default_value = alpha
        m.use_backface_culling = False
        try:
            m.surface_render_method = "BLENDED"
        except Exception:
            pass


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    lod = 0
    if "--lod" in args:
        i = args.index("--lod"); lod = int(args[i + 1]); del args[i:i + 2]
    close = "--close" in args
    args = [a for a in args if a != "--close"]
    os.makedirs(OUT, exist_ok=True)
    for name in args:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=os.path.join(MODELS, name + ".glb"))
        for o in list(bpy.context.scene.objects):
            if o.type == "MESH" and not o.name.endswith("_LOD%d" % lod):
                bpy.data.objects.remove(o, do_unlink=True)
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        tris = 0
        for o in meshes:
            tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
            for m in o.data.materials:
                if m is not None:
                    bind(m)
        lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
        for o in meshes:
            for v in o.bound_box:
                w = o.matrix_world @ Vector(v)
                lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
        ctr = (lo + hi) / 2
        rad = (hi - lo).length / 2
        sc = bpy.context.scene
        sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
        sc.render.resolution_x, sc.render.resolution_y = 1400, 900
        sc.view_settings.view_transform = "AgX" if "AgX" in [v.identifier for v in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
        w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
        bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.55, 0.72, 0.92, 1); bg.inputs[1].default_value = 0.9
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.data.energy = 4.0; sun.data.color = (1.0, 0.95, 0.86)
        sun.rotation_euler = (math.radians(50), 0, math.radians(35))
        sc.collection.objects.link(sun)
        # ground plane
        bpy.ops.mesh.primitive_plane_add(size=rad * 8, location=(ctr.x, ctr.y, lo.z - 0.02))
        gp = bpy.context.active_object
        gm = bpy.data.materials.new("ground"); gm.use_nodes = True
        gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.40, 0.36, 1)
        gp.data.materials.append(gm)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        sc.collection.objects.link(cam); sc.camera = cam
        cam.data.lens = 40
        # Blender +y == Unity -z (building front / street face)
        views = {"front": (0.25, 1.0, 0.28), "q34": (1.0, 0.8, 0.55), "back": (-0.5, -1.0, 0.35),
                 "low": (-0.6, 1.0, 0.06)}
        for vn, d in views.items():
            dv = Vector(d).normalized()
            dist = rad * (1.25 if close else 2.1)
            tgt = ctr.copy()
            if vn == "low":
                tgt.z = lo.z + (hi.z - lo.z) * 0.3
            cam.location = tgt + dv * dist
            cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
            cam.data.clip_end = rad * 20
            sc.render.filepath = os.path.join(OUT, "p2_%s_%s.png" % (name, vn))
            bpy.ops.render.render(write_still=True)
        print("[nagisa-prev2] %s LOD%d %s tris, %.0f m radius" % (name, lod, "{:,}".format(tris), rad))


main()

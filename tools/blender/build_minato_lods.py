"""
Derive Minato-OWNED LOD variants from existing production Sakura/Shiosai GLBs.

The source environments are never touched. Each source GLB is imported, its mesh
objects are renamed to '<obj>_LOD0', and decimated copies '_LOD1/_LOD2/_LOD3' are
produced at the spec ratios (LOD1 55%, LOD2 25%, LOD3 8%). The result is exported
to Assets/Environment/MinatoCoast/BlenderAssets/Minato_<SourceName>.glb.

MinatoCoastEnvironment.Inst() builds an LODGroup from renderers whose names end in
_LOD0.._LOD3, so the derived GLBs light up LODs with no further Unity work.

Run:  blender.exe -b -P build_minato_lods.py
"""
import os
import sys
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SAK = os.path.join(ROOT, "Assets", "Environment", "SakuraPass", "BlenderAssets")
SHI = os.path.join(ROOT, "Assets", "Environment", "ShiosaiCoast", "BlenderAssets")
OUT = os.path.join(ROOT, "Assets", "Environment", "MinatoCoast", "BlenderAssets")

# PROVISIONAL ladder, from the brief: LOD1 50-60%, LOD2 20-30%, LOD3 5-10%.
RATIOS = [("LOD1", 0.55), ("LOD2", 0.25), ("LOD3", 0.08)]

FAMILIES = [
    # (source dir, name)
    (SAK, "SakuraPass_Grass_Tuft"),
    (SAK, "SakuraPass_Fern_Clump"),
    (SAK, "SakuraPass_Flower_Clump_A"),
    (SAK, "SakuraPass_Flower_Clump_B"),
    (SAK, "SakuraPass_Flower_Clump_C"),
    (SAK, "SakuraPass_Low_Shrub_A"),
    (SAK, "SakuraPass_Low_Shrub_B"),
    (SAK, "SakuraPass_Pine_A"),
    (SAK, "SakuraPass_Pine_B"),
    (SAK, "SakuraPass_Broadleaf_A"),
    (SAK, "SakuraPass_Broadleaf_B"),
    (SAK, "SakuraPass_Rock_Cluster_A"),
    (SAK, "SakuraPass_Rock_Cluster_B"),
    (SAK, "SakuraPass_Stone_Scatter_A"),
    (SAK, "SakuraPass_Stone_Scatter_B"),
    (SAK, "SakuraPass_Cliff_Ledge"),
    (SHI, "Shiosai_Pine"),
    (SHI, "Shiosai_Pine_B"),
    (SHI, "Shiosai_Broadleaf"),
    (SHI, "Shiosai_Broadleaf_B"),
    (SHI, "Shiosai_Hydrangea"),
    (SHI, "Shiosai_Hydrangea_B"),
    (SHI, "Shiosai_Hydrangea_C"),
    (SHI, "Shiosai_HarbourHouse_A"),
    (SHI, "Shiosai_HarbourHouse_B"),
    (SHI, "Shiosai_HarbourHouse_C"),
    (SHI, "Shiosai_House_A"),
    (SHI, "Shiosai_House_B"),
    (SHI, "Shiosai_House_C"),
    (SHI, "Shiosai_FishingBoat"),
    (SHI, "Shiosai_Breakwater"),
    (SHI, "Shiosai_SeaStack_A"),
    (SHI, "Shiosai_SeaStack_B"),
    (SHI, "Shiosai_SeaStack_C"),
]


def wipe():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def derive(src_dir, name):
    path = os.path.join(src_dir, name + ".glb")
    if not os.path.isfile(path):
        print("[lod] MISSING %s" % path)
        return None
    wipe()
    bpy.ops.import_scene.gltf(filepath=path)

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        print("[lod] no meshes in %s" % name)
        return None

    base_tris = sum(len(o.data.loop_triangles) if o.data.loop_triangles
                    else len(o.data.polygons) for o in meshes)

    exported = []
    for o in meshes:
        # Flatten the glTF node parenting so the LOD renderers are siblings; Unity's
        # LODGroup only needs the renderers, and a flat set keeps pivots identical.
        o.name = o.name + "_LOD0"
        exported.append(o)

    made = 0
    for obj in list(exported):
        if obj.name.endswith("_LOD0") is False:
            continue
        stem = obj.name[:-5]
        for suffix, ratio in RATIOS:
            cp = obj.copy()
            cp.data = obj.data.copy()
            # A copied object inherits hide_render/hide_viewport; an inherited hide
            # exports an object that is silently invisible in Unity.
            cp.hide_render = False
            cp.hide_viewport = False
            cp.name = stem + "_" + suffix
            bpy.context.scene.collection.objects.link(cp)
            m = cp.modifiers.new("dec", "DECIMATE")
            m.decimate_type = "COLLAPSE"
            m.ratio = ratio
            m.use_collapse_triangulate = True
            bpy.context.view_layer.objects.active = cp
            try:
                bpy.ops.object.modifier_apply(modifier=m.name)
            except RuntimeError as e:
                print("[lod] decimate failed on %s: %s" % (cp.name, e))
                bpy.data.objects.remove(cp, do_unlink=True)
                continue
            exported.append(cp)
            made += 1

    for o in bpy.context.scene.objects:
        o.select_set(o in exported)

    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, "Minato_" + name + ".glb")
    bpy.ops.export_scene.gltf(
        filepath=out,
        export_format="GLB",
        use_selection=True,
        export_apply=False,          # never bake modifiers on export
        export_yup=True,
    )
    tot = 0
    for o in exported:
        o.data.calc_loop_triangles()
        tot += len(o.data.loop_triangles)
    print("[lod] %-34s base %6d tris -> %2d LOD meshes, %6d tris total"
          % (name, base_tris, made, tot))
    return out


def main():
    ok = 0
    for d, n in FAMILIES:
        if derive(d, n):
            ok += 1
    print("[lod] derived %d/%d Minato LOD families into %s" % (ok, len(FAMILIES), OUT))


if __name__ == "__main__":
    main()

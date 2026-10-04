"""
SHIOSAI COAST - HERO BACKDROP landforms (Blender 4.5 -> glTF -> Unity), 2026-09-25.

    tools/blender-4.5.10-windows-x64/blender.exe -b --factory-startup -P tools/blender/build_shiosai_backdrop_hero.py
    (optional: -- Shiosai_Hero_Headland_A Shiosai_Hero_Massif_B ... to build a subset)

WHY. The QA pass on the chapter captures found the Shiosai background low fidelity: the far
ranges are smooth procedural ridges, the headlands above the road are one sheet of lawn and
there are no hero landforms on the horizon. This builds HIGH-POLY background landforms:

  Shiosai_Hero_Headland_A/B/C   ~40k tris   700 m coastal massifs, strata-banded cliff front
  Shiosai_Hero_Islet_A/B/C      ~14k tris   offshore islets with cliffs, domed tops, skerries
  Shiosai_Hero_Massif_A/B       ~40k tris   3 km distant mountain massifs, ridged crests

plus a *_LOD1 of each at ~25 % of the triangles, for Unity's LODGroup (ShiosaiHeroBackdrop.cs).

The landform maths lives in shiosai_backdrop_terrain.py (pure python, no bpy) so it can be
previewed and tested outside Blender. This script only turns its grids into meshes.

CONVENTIONS (same as build_shiosai.py):
  * vertices authored in UNITY metres and passed through S.u2b()
  * origin at the base centre, local -Z = the face that looks at the road / sea
  * material NAMES are the staging key - ShiosaiCoastEnvironment.CoastMaterialFor routes
    "..._Rock_Face" to the coast rock material and "..._Grass_Canopy" to the coast canopy one
  * one mesh per asset, two material slots (rock / grass), chosen per face by slope + height
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

import sakura_lib as S  # noqa: E402
import shiosai_backdrop_terrain as T  # noqa: E402


def assets_dir():
    d = os.path.join(S.repo_root(), "Assets", "Environment", "ShiosaiCoast", "BlenderAssets")
    os.makedirs(d, exist_ok=True)
    return d


def export(objs, filename):
    original = S.blender_assets_dir
    S.blender_assets_dir = assets_dir
    try:
        S.export_glb(objs, filename)
    finally:
        S.blender_assets_dir = original


def build_landform(name, fn, kwargs):
    verts, faces, uvs, rock = fn(**kwargs)
    bverts = [S.u2b(*v) for v in verts]
    obj = S.mesh_from_arrays(name, bverts, faces, uvs=uvs, smooth=True)
    S.recalc_normals(obj)

    kind = "Massif" if "Massif" in name else "Islet" if "Islet" in name else "Headland"
    rock_mat = S.pbr_material(f"{name}_{kind}_Rock_Face", base_color=(0.50, 0.47, 0.43, 1.0), roughness=0.95)
    grass_mat = S.pbr_material(f"{name}_{kind}_Grass_Canopy", base_color=(0.24, 0.40, 0.22, 1.0), roughness=0.9)
    S.assign_material(obj, grass_mat, slot=0)
    S.assign_material(obj, rock_mat, slot=1)
    idx = [1 if r else 0 for r in rock]
    obj.data.polygons.foreach_set("material_index", idx)
    obj.data.update()

    # Smooth shading with an angle limit keeps the cliff strata crisp while the grass rolls.
    try:
        obj.data.set_sharp_from_angle(angle=0.9)          # Blender 4.1+
    except Exception:
        pass
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    print(f"[shiosai-hero] {name}: {len(verts)} verts, {tris} tris, rock {sum(idx) / max(1, len(idx)):.0%}")
    return obj


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = args or list(T.ASSETS.keys())
    for name in names:
        if name not in T.ASSETS:
            print(f"[shiosai-hero] unknown asset '{name}'")
            continue
        fn, kw, lod_kw = T.ASSETS[name]
        for suffix, k in (("", kw), ("_LOD1", lod_kw)):
            S.reset_scene()
            obj = build_landform(name + suffix, fn, k)
            export([obj], f"{name}{suffix}.glb")
    print(f"[shiosai-hero] assets -> {assets_dir()}")


if __name__ == "__main__":
    main()

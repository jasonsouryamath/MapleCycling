"""
E4 / huddle batch 2 (2026-09-30) - Sakura Pass photoreal prop pass.

  blender -b -P build_sakura_photoreal_props.py [-- signs splats ranges]

  signs   idea 8 + critic "blank roadside sign": re-UV the summit signboard and the chevron board
          onto ONE trim-sheet atlas (Textures/Sakura_Sign_Trim.png, written by
          tools/textures/make_sakura_photoreal_textures.py). Placeholder glyph bars and the loose
          chevron meshes are dropped - the lettering / arrows now live in the trim.
          -> SakuraPass_Summit_Sign_Trim.glb, SakuraPass_Chevron_Sign_Trim.glb
  splats  critic "circular ground patches": four irregular soil/moss decal cards, each UV'd to one
          cell of the 2x2 Sakura_Ground_Splat.png atlas (alpha-clipped in Unity).
          -> SakuraPass_Ground_Splat_0..3.glb
  ranges  idea 7: split the four 360-degree distant-range rings into angular sectors (so they can
          be frustum-culled and LOD-switched) and build a 3-step LOD chain per sector with the
          sector seams protected from collapse (no cracks between neighbouring LODs): LOD0 full,
          LOD1 Decimate 40%, LOD2 Decimate 12% THEN flattened onto a constant-radius cylinder about
          the local origin (Agent HQ ticket 2026-09-30, 'add billboard to distant mountains') - a
          true billboard cutout, not just a lighter mesh. See billboard_copy().
          -> SakuraPass_Distant_Ranges_LOD.glb  (nodes DistantRange_<ring>_s<k>_LOD<n>)

Geometry round-trips through the glTF importer/exporter (identity), or is authored in Unity
coordinates through S.u2b like every other builder. Originals are never overwritten (Azora and
the village boards still stage the old GLBs).
"""
import math
import os
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402

TRIM = 2048.0
# trim-sheet rects in IMAGE pixels, origin top-left: (x0, y0, x1, y1). Must match
# tools/textures/make_sakura_photoreal_textures.py.
RECT_SUMMIT = (0, 0, 1536, 512)
RECT_CHEVRON = (0, 512, 1536, 1000)
RECT_GALV = (1536, 0, 2048, 1488)
RECT_WOOD = (0, 1536, 2048, 2048)          # tiles in U
INSET = 6.0                                 # px, keeps mips off the neighbouring rect
WOOD_REPEAT_M = 1.6                         # PROVISIONAL: metres of timber per wood-strip repeat

SECTORS = 12                                # PROVISIONAL: angular sectors per range ring
LOD_RATIOS = (1.0, 0.40, 0.12)              # PROVISIONAL: Decimate collapse ratios LOD0..2


def rect_uv(rect, s, t):
    """s,t in 0..1 across the rect (t=0 bottom) -> glTF/Blender UV (v up)."""
    x0, y0, x1, y1 = rect
    x0 += INSET; y0 += INSET; x1 -= INSET; y1 -= INSET
    px = x0 + (x1 - x0) * s
    py = y1 - (y1 - y0) * t
    return (px / TRIM, 1.0 - py / TRIM)


def unity(v):
    """Blender world co -> Unity co (inverse of S.u2b)."""
    return Vector((-v.x, v.z, -v.y))


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(name):
    path = os.path.join(S.blender_assets_dir(), name + ".glb")
    bpy.ops.import_scene.gltf(filepath=path)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in objs:                               # bake every transform into the mesh
        mw = o.matrix_world.copy()
        o.parent = None
        o.data.transform(mw)
        o.matrix_world.identity()
    for o in [o for o in bpy.context.scene.objects if o.type != 'MESH']:
        bpy.data.objects.remove(o, do_unlink=True)
    return objs


def fix_winding(objs):
    """The source sign GLBs wind their panels inward (the old material hid it with glyph meshes
    on top); Unity back-face culls, so the trimmed front face vanished. Point everything out."""
    for o in objs:
        S.recalc_normals(o)


def trim_material():
    mat = S.pbr_material("SakuraPass_SignTrim", base_color=(1, 1, 1, 1), roughness=0.6)
    t = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Sign_Trim.png")), "Base Color")
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Sign_Trim_Normal.png"), True), "Normal",
                  non_color=True)
    return mat


def bbox_u(obj):
    P = [unity(v.co) for v in obj.data.vertices]
    lo = Vector((min(p.x for p in P), min(p.y for p in P), min(p.z for p in P)))
    hi = Vector((max(p.x for p in P), max(p.y for p in P), max(p.z for p in P)))
    return lo, hi


def uv_face_planar(obj, pick):
    """pick(face_centre_unity, face_normal_unity, lo, hi) -> fn(unity_co)->uv, per face."""
    me = obj.data
    lo, hi = bbox_u(obj)
    uvl = me.uv_layers.active or me.uv_layers.new(name="UVMap")
    for f in me.polygons:
        c = unity(f.center)
        n = Vector((-f.normal.x, f.normal.z, -f.normal.y))
        fn = pick(c, n, lo, hi)
        for li in f.loop_indices:
            co = unity(me.vertices[me.loops[li].vertex_index].co)
            uvl.data[li].uv = fn(co)


def safe(a, b):
    return max(1e-4, b - a)


def wood_mapper(lo, hi):
    """Grain runs along the object's longest axis; U tiles (the strip spans the full width)."""
    ext = hi - lo
    axis = max(range(3), key=lambda i: ext[i])
    others = [i for i in range(3) if i != axis]

    def pick(c, n, _lo, _hi):
        # the across-grain coordinate: whichever remaining axis the face is NOT facing along
        a = others[0] if abs(n[others[0]]) < abs(n[others[1]]) else others[1]

        def fn(co):
            s = (co[axis] - lo[axis]) / WOOD_REPEAT_M
            t = (co[a] - lo[a]) / safe(lo[a], hi[a])
            u, v = rect_uv(RECT_WOOD, 0.0, 0.05 + 0.9 * t)
            return (s, v)
        return fn
    return pick


def galv_mapper(scale_m=1.0):
    def pick(c, n, lo, hi):
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [i for i in range(3) if i != ax]

        def fn(co):
            s = min(1.0, (co[a] - lo[a]) / max(scale_m, safe(lo[a], hi[a])))
            t = min(1.0, (co[b] - lo[b]) / max(scale_m, safe(lo[b], hi[b])))
            return rect_uv(RECT_GALV, s, t)
        return fn
    return pick


def build_signs():
    mat = None
    # ---------------------------------------------------------------- summit signboard
    clear_scene()
    objs = import_glb("SakuraPass_Summit_Sign")
    glyph_z = [unity(v.co).z for o in objs if "Glyph" in o.name for v in o.data.vertices]
    panel = next(o for o in objs if o.name.endswith("_Panel"))
    plo, phi = bbox_u(panel)
    pz = 0.5 * (plo.z + phi.z)
    front = -1.0 if (sum(glyph_z) / max(1, len(glyph_z))) < pz else 1.0   # front normal sign on Z
    print(f"[sakura-e4b] summit sign front faces Unity {'-Z' if front < 0 else '+Z'}")
    for o in [o for o in objs if "Glyph" in o.name]:
        bpy.data.objects.remove(o, do_unlink=True)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    fix_winding(objs)
    mat = trim_material()

    def panel_pick(c, n, lo, hi):
        # by POSITION, not by the normal: the panel's authored winding is not trustworthy here.
        face_z = hi.z if front > 0 else lo.z
        if abs(n.z) > 0.7 and abs(c.z - face_z) < 0.02:
            def fn(co):
                s = (co.x - lo.x) / safe(lo.x, hi.x)
                if front > 0:            # viewer looks along -Z: screen-right is -X
                    s = 1.0 - s
                return rect_uv(RECT_SUMMIT, s, (co.y - lo.y) / safe(lo.y, hi.y))
            return fn
        return galv_mapper(0.8)(c, n, lo, hi)

    for o in objs:
        if o is panel:
            uv_face_planar(o, panel_pick)
        else:
            lo, hi = bbox_u(o)
            uv_face_planar(o, wood_mapper(lo, hi))
        o.data.materials.clear()
        o.data.materials.append(mat)
        o.name = o.name.replace("SakuraPass_Summit_Sign", "SakuraPass_Summit_Sign_Trim")
    S.export_glb(objs, "SakuraPass_Summit_Sign_Trim.glb")

    # ---------------------------------------------------------------- chevron board
    clear_scene()
    objs = import_glb("SakuraPass_Chevron_Sign")
    for o in [o for o in objs if "_Chevron_" in o.name and "Chevron_Sign_Chevron" in o.name]:
        bpy.data.objects.remove(o, do_unlink=True)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    pass  # chevron board already winds outward (verified diag_road_start)
    mat = trim_material()

    def board_pick(c, n, lo, hi):
        if abs(n.z) > 0.7:          # both faces carry the arrows (as the old _True/_False meshes did)
            def fn(co):
                return rect_uv(RECT_CHEVRON, (co.x - lo.x) / safe(lo.x, hi.x),
                               (co.y - lo.y) / safe(lo.y, hi.y))
            return fn
        return galv_mapper(0.8)(c, n, lo, hi)

    for o in objs:
        uv_face_planar(o, board_pick if o.name.endswith("_Board") else galv_mapper(1.2))
        o.data.materials.clear()
        o.data.materials.append(mat)
        o.name = o.name.replace("SakuraPass_Chevron_Sign", "SakuraPass_Chevron_Sign_Trim")
    S.export_glb(objs, "SakuraPass_Chevron_Sign_Trim.glb")


# ------------------------------------------------------------------------ ground splats
SPLAT_W, SPLAT_D = 1.7, 1.1          # PROVISIONAL card size (m); Unity scales it per instance


def build_splats():
    t = S.textures_dir()
    for k in range(4):
        clear_scene()
        cx, cy = k % 2, k // 2              # atlas cell, image origin top-left
        nx, nz = 6, 4
        verts, faces, uvs = [], [], []
        for j in range(nz + 1):
            for i in range(nx + 1):
                s, q = i / nx, j / nz
                x = (s - 0.5) * SPLAT_W
                z = (q - 0.5) * SPLAT_D
                edge = max(abs(s - 0.5), abs(q - 0.5)) * 2.0
                y = -0.025 * edge * edge            # rim tucks under on convex ground
                verts.append(S.u2b(x, y, z))
        for j in range(nz):
            for i in range(nx):
                a = j * (nx + 1) + i
                quad = (a, a + 1, a + nx + 2, a + nx + 1)
                faces.append(quad)
                for vi in quad:
                    ii, jj = vi % (nx + 1), vi // (nx + 1)
                    u = (cx + ii / nx) / 2.0
                    v = 1.0 - (cy + 1.0 - jj / nz) / 2.0
                    # 3 px guard band inside the cell
                    u = (cx + 0.006 + 0.988 * ii / nx) / 2.0
                    v = 1.0 - (cy + 0.006 + 0.988 * (1.0 - jj / nz)) / 2.0
                    uvs.append((u, v))
        obj = S.mesh_from_arrays(f"SakuraPass_Ground_Splat_{k}", verts, faces, uvs=uvs)
        me = obj.data
        me.update()
        if sum(p.normal.z for p in me.polygons) < 0:        # must face up (Blender +Z)
            for p in me.polygons:
                p.flip()
            me.update()
        mat = S.pbr_material(f"SakuraPass_GroundSplat", base_color=(1, 1, 1, 1), roughness=0.95)
        S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Ground_Splat.png")), "Base Color")
        S.assign_material(obj, mat)
        S.export_glb([obj], f"SakuraPass_Ground_Splat_{k}.glb")


# ------------------------------------------------------------------------ distant range LODs
def sector_object(src, ring, k, faces_idx):
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.faces.ensure_lookup_table()
    keep = set(faces_idx)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    me = bpy.data.meshes.new(f"DistantRange_{ring}_s{k:02d}_LOD0")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(obj)
    for m in src.data.materials:
        me.materials.append(m)
    return obj


def decimate_copy(obj, ratio, lod):
    me = obj.data.copy()
    me.name = obj.name.replace("_LOD0", f"_LOD{lod}")
    o2 = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(o2)
    # protect the sector seam: boundary verts weight 0 are never collapsed, so a LOD2 sector
    # still meets its LOD0 neighbour edge-for-edge (no sky cracks at the seams).
    bm = bmesh.new()
    bm.from_mesh(me)
    step = 2 * math.pi / SECTORS

    def on_seam(co):
        a = math.atan2(co.y, co.x) % step
        return min(a, step - a) < math.radians(0.6)
    # only the sector CUT is protected; the ridge silhouette and skirt are free to simplify
    boundary = {v.index for e in bm.edges if e.is_boundary for v in e.verts if on_seam(v.co)}
    bm.free()
    vg = o2.vertex_groups.new(name="interior")
    interior = [i for i in range(len(me.vertices)) if i not in boundary]
    vg.add(interior, 1.0, 'REPLACE')
    mod = o2.modifiers.new("lod", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = ratio
    mod.vertex_group = "interior"
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o2
    for o in bpy.context.selected_objects:
        o.select_set(False)
    o2.select_set(True)
    bpy.ops.object.modifier_apply(modifier="lod")
    o2.vertex_groups.clear()
    return o2


def billboard_copy(obj, ratio, lod):
    """Agent HQ ticket (2026-09-30): the last LOD step becomes a true billboard, not just a
    lighter mesh. Idea 7's huddle log deferred a billboard because the four rings surround the
    camera (a single flat card can only ever face one way). The fix does it per SECTOR instead:
    every sector already only spans one ~30 deg arc, so collapsing every vertex onto a constant
    -radius cylinder about the local (route) origin - keeping its angle and height exactly -
    reproduces the identical silhouette as seen from that origin (a point on a ray keeps the same
    screen position regardless of its distance along that ray), while turning the sector into a
    paper-thin cardboard cutout. The route sits within a few hundred metres of the rings' shared
    origin versus a 2.4-3.8 km radius, so the parallax error for a rider anywhere on it stays a
    few degrees - invisible on a hazy backdrop LOD only used past RangeLod2Distance.
    """
    o2 = decimate_copy(obj, ratio, lod)
    me = o2.data
    if not me.vertices:
        return o2
    radii = [math.hypot(v.co.x, v.co.y) for v in me.vertices]
    r0 = sum(radii) / len(radii)
    for v in me.vertices:
        ang = math.atan2(v.co.y, v.co.x)
        v.co.x = r0 * math.cos(ang)
        v.co.y = r0 * math.sin(ang)
    me.update()
    # Recalc normals for consistent shading; the ring material already sets _Cull 0 (the camera
    # sits inside the ring), so a flipped billboard normal costs shading quality, not visibility.
    bm = bmesh.new()
    bm.from_mesh(me)
    faces = list(bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    bm.normal_update()
    inward_dot = 0.0
    for f in faces:
        c = f.calc_center_median()
        d = Vector((c.x, c.y, 0.0))
        if d.length > 1e-6:
            inward_dot += f.normal.dot(-d.normalized())
    if inward_dot < 0.0:
        for f in faces:
            f.normal_flip()
        bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    me.update()
    return o2


def build_ranges():
    clear_scene()
    rings = import_glb("SakuraPass_Distant_Ranges")
    out = []
    tot = [0, 0, 0]
    last_lod = len(LOD_RATIOS) - 1
    for src in sorted(rings, key=lambda o: o.name):
        ring = int(src.name.split("_")[-1].split(".")[0])
        buckets = [[] for _ in range(SECTORS)]
        for f in src.data.polygons:
            ang = math.atan2(f.center.y, f.center.x) % (2 * math.pi)
            buckets[min(SECTORS - 1, int(ang / (2 * math.pi) * SECTORS))].append(f.index)
        for k, idx in enumerate(buckets):
            if not idx:
                continue
            l0 = sector_object(src, ring, k, idx)

            def lod_copy(n, r):
                return billboard_copy(l0, r, n) if n == last_lod else decimate_copy(l0, r, n)
            chain = [l0] + [lod_copy(n, r) for n, r in enumerate(LOD_RATIOS) if n > 0]
            for n, o in enumerate(chain):
                tot[n] += sum(len(p.vertices) - 2 for p in o.data.polygons)
            out.extend(chain)
        bpy.data.objects.remove(src, do_unlink=True)
    print(f"[sakura-e4b] distant ranges: {len(out)} sector-LOD meshes; tris LOD0 {tot[0]:,} "
          f"LOD1 {tot[1]:,} LOD2(billboard) {tot[2]:,}")
    S.export_glb(out, "SakuraPass_Distant_Ranges_LOD.glb")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stages = argv or ["signs", "splats", "ranges"]
    if "signs" in stages:
        build_signs()
    if "splats" in stages:
        build_splats()
    if "ranges" in stages:
        build_ranges()
    print("[sakura-e4b] done.")


main()

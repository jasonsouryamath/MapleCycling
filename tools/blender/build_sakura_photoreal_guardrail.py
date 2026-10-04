"""
E4 / Agent HQ huddle idea 2 (2026-09-30): the Sakura Pass steel W-beam as ONE small instanced
segment (beam panel + post + blockout + reflector tab, bevelled with harden-normals) instead of
the route-swept Guardrail_HD mesh.

Outputs
  BlenderAssets/SakuraPass_Guardrail_Seg.glb        one ~300-tri segment, Unity-local frame:
                                                    +Z along the rail, +X outboard, +Y up,
                                                    origin on the verge at the rail line.
  SakuraPass/SakuraGuardrailPlacements.json        flat [px,py,pz, fx,fy,fz, ux,uy,uz, len]
                                                    per segment, Unity world coords.

Placement re-uses build_road.road_frames / camber / W_BEAM and the SAME run tables as
build_road.build_guardrail (duplicated below - keep them in sync; build_road.py is Claude's
file and is deliberately not edited).  Run:  blender -b -P build_sakura_photoreal_guardrail.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import json
import math

import bpy
import numpy as np

import sakura_lib as S
import sakura_route as R
import build_road as BR

# ---------------------------------------------------------------- tunables (PROVISIONAL)
SEG_EFFECTIVE = 3.81     # effective panel pitch (a 4.13 m panel lapped 0.32 m) - illustrative
SEG_LAP = 0.32
BEVEL_WIDTH = 0.006
TARGET_TRIANGLES = 300
POST_H = (-0.35, 0.80)   # post extent relative to verge (buried part trimmed to 0.35 m)

CLIMB_RUNS = [(0.135, 0.225), (0.405, 0.460), (0.500, 0.560), (0.650, 0.970)]
DESCENT_RUNS = ((8.0, 150.0), (158.0, 300.0), (312.0, 430.0), (442.0, 560.0),
                (572.0, 690.0), (702.0, 790.0))
EXPANSION_RUNS = {"s1": [(0.06, 0.30), (0.34, 0.52), (0.56, 0.80)],
                  "aozora": [(0.12, 0.46), (0.50, 0.94)],
                  "maple": [(0.30, 0.52)]}


def _sweep_straight(profile, z0, z1, closed=False, y_off=0.0):
    """Local-frame straight sweep with build_road.sweep_frames' exact face convention."""
    side = np.array([1.0, 0.0, 0.0])
    up = np.array([0.0, 1.0, 0.0])
    fw = np.array([0.0, 0.0, 1.0])
    n = len(profile)
    verts, faces, uvs = [], [], []
    for z in (z0, z1):
        for (a, h) in profile:
            p = fw * z + side * a + up * (h + y_off)
            verts.append(p)
    span = n if closed else n - 1
    for j in range(span):
        j1 = (j + 1) % n
        faces.append((j, j1, n + j1, n + j))
        uvs.extend([(j / n, z0), ((j + 1) / n, z0), ((j + 1) / n, z1), (j / n, z1)])
    return verts, faces, uvs


def _merge(parts):
    V, Fc, U = [], [], []
    for (v, f, u) in parts:
        b = len(V)
        V.extend(v)
        Fc.extend(tuple(i + b for i in face) for face in f)
        U.extend(u)
    return V, Fc, U


def build_segment():
    L = SEG_EFFECTIVE + SEG_LAP
    beam_profile = [(-d, h) for (d, h) in BR.W_BEAM]
    beam = _sweep_straight(beam_profile, 0.0, L, y_off=BR.RAIL_HEIGHT)
    # back face of the corrugated sheet (4 mm) so the rail has thickness from the valley side
    back_profile = [(-d + 0.004, h) for (d, h) in reversed(BR.W_BEAM)]
    back = _sweep_straight(back_profile, 0.0, L, y_off=BR.RAIL_HEIGHT)
    post_profile = [(-0.055, 0.0), (0.055, 0.0), (0.055, 0.018), (0.012, 0.018),
                    (0.012, 0.082), (0.055, 0.082), (0.055, 0.100), (-0.055, 0.100)]
    # post: vertical sweep; express it as a straight sweep along Y by swapping axes
    pv, pf, pu = [], [], []
    n = len(post_profile)
    for y in POST_H:
        for (a, h) in post_profile:
            pv.append(np.array([a * 0.0 + (h + 0.03), y, a]))    # x: 0.03..0.13 outboard of beam
    for j in range(n):
        j1 = (j + 1) % n
        pf.append((n + j, n + j1, j1, j))
        pu.extend([(j / n, 0), ((j + 1) / n, 0), ((j + 1) / n, 1), (j / n, 1)])
    # post top cap
    pf.append(tuple(n + j for j in range(n)))
    pu.extend([(0.5, 0.5)] * n)
    # blockout spacer (box) between post and beam at rail height
    bx0, bx1, by0, by1, bz0, bz1 = 0.004, 0.03, BR.RAIL_HEIGHT - 0.09, BR.RAIL_HEIGHT + 0.09, -0.05, 0.05
    bv = [np.array(c) for c in [(bx0, by0, bz0), (bx1, by0, bz0), (bx1, by1, bz0), (bx0, by1, bz0),
                                (bx0, by0, bz1), (bx1, by0, bz1), (bx1, by1, bz1), (bx0, by1, bz1)]]
    bf = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (0, 4, 7, 3)]
    bu = [(0, 0), (1, 0), (1, 1), (0, 1)] * 6
    # reflector tab on the beam face
    rx = -0.10
    ry = BR.RAIL_HEIGHT + 0.10
    rv = [np.array(c) for c in [(rx, ry - 0.045, -0.04), (rx, ry - 0.045, 0.04),
                                (rx, ry + 0.045, 0.04), (rx, ry + 0.045, -0.04)]]
    rf = [(0, 1, 2, 3)]
    ru = [(0, 0), (1, 0), (1, 1), (0, 1)]

    V, Fc, U = _merge([beam, back, (pv, pf, pu), (bv, bf, bu), (rv, rf, ru)])
    verts = [S.u2b(p[0], p[1], p[2]) for p in V]
    ob = S.mesh_from_arrays("SakuraPass_Guardrail_Seg", verts, Fc, uvs=U, smooth=False)
    S.recalc_normals(ob)
    S.add_weld(ob, 0.0005)
    mod = ob.modifiers.new("Bevel", 'BEVEL')
    mod.width = BEVEL_WIDTH
    mod.segments = 2
    mod.limit_method = 'ANGLE'
    mod.angle_limit = math.radians(35.0)
    mod.harden_normals = True
    S.apply_modifiers(ob)
    # Keep the authored highlight bevel, but enforce the ticket's hard per-instance
    # budget after bevel expansion.  The bevel is intentionally applied first so
    # this reduction cannot remove the edge treatment that makes the steel read.
    # Count exported triangles, not polygon faces: the W-beam deliberately contains
    # quads, and Blender's polygon count understated the GLB cost (162 faces became
    # 328 rendered triangles).  Triangulate before the budget pass so the assertion
    # matches Unity's Mesh.triangles.Length / 3 contract exactly.
    tri = ob.modifiers.new("Export Triangulate", 'TRIANGULATE')
    S.apply_modifiers(ob)
    tri_count = len(ob.data.polygons)
    if tri_count > TARGET_TRIANGLES:
        budget = ob.modifiers.new("Triangle Budget", 'DECIMATE')
        budget.decimate_type = 'COLLAPSE'
        budget.ratio = TARGET_TRIANGLES / float(tri_count)
        S.apply_modifiers(ob)
        tri_count = len(ob.data.polygons)
    if tri_count > TARGET_TRIANGLES:
        raise RuntimeError(f"Guardrail segment exceeded {TARGET_TRIANGLES}-tri budget: {tri_count}")
    print(f"[sakura-e4] guardrail segment budget: {tri_count} tris (target <= {TARGET_TRIANGLES})")
    mat = S.pbr_material("SakuraPass_Guardrail", base_color=(0.20, 0.173, 0.157, 1.0),
                         roughness=0.42, metallic=0.85)
    S.assign_material(ob, mat)
    S.report(ob.name, ob)
    return ob


def placements():
    out = []

    def emit(p, sides, ups, arc, runs):
        offset = BR.HALF + BR.SHOULDER + 0.24
        rail = np.array([p[i] + sides[i] * offset + ups[i] * (BR.camber(BR.HALF) - 0.045)
                         for i in range(len(p))])
        n = len(p)
        for (i0, i1) in runs:
            i1 = min(i1, n - 1)
            if i1 - i0 <= 2:
                continue
            a0, a1 = arc[i0], arc[i1]
            s = a0
            while s < a1 - 0.6:
                e = min(s + SEG_EFFECTIVE, a1)
                P0 = np.array([np.interp(s, arc, rail[:, k]) for k in range(3)])
                P1 = np.array([np.interp(e, arc, rail[:, k]) for k in range(3)])
                up = np.array([np.interp(s, arc, np.asarray(ups)[:, k]) for k in range(3)])
                f = P1 - P0
                ln = float(np.linalg.norm(f))
                f /= max(ln, 1e-6)
                up = up - f * np.dot(up, f)
                up /= max(np.linalg.norm(up), 1e-6)
                out.extend([*map(float, P0), *map(float, f), *map(float, up), ln])
                s = e

    p, sides, ups, arc, _, _ = BR.road_frames(spacing=0.65)
    runs = [R.climb_window(arc, f0, f1) for (f0, f1) in CLIMB_RUNS]
    ce = R.climb_length()
    for (d0, d1) in DESCENT_RUNS:
        runs.append((R.index_at_distance(arc, ce + d0), R.index_at_distance(arc, ce + d1)))
    emit(p, sides, ups, arc, runs)
    for seg_id, fr in EXPANSION_RUNS.items():
        p, sides, ups, arc, _, _ = BR.road_frames(spacing=0.65, seg_id=seg_id)
        tot = arc[-1]
        emit(p, sides, ups, arc, [(R.index_at_distance(arc, a * tot), R.index_at_distance(arc, b * tot))
                                  for (a, b) in fr])
    path = os.path.join(os.path.dirname(S.blender_assets_dir()), "SakuraGuardrailPlacements.json")
    with open(path, "w") as fh:
        json.dump({"stride": 10, "segEffective": SEG_EFFECTIVE, "d": [round(x, 4) for x in out]}, fh)
    print(f"[sakura-e4] {len(out) // 10} guardrail segments -> {path}")


def main():
    S.reset_scene()
    S.export_glb([build_segment()], "SakuraPass_Guardrail_Seg.glb")
    placements()


if __name__ == "__main__":
    main()

"""
Sakura Pass cliffside tunnel (concept panel 04).

Built to the real Japanese road-tunnel standard rather than eyeballed, because the proportions of
a tunnel are the thing people unconsciously recognise:

  * Cross-section follows MLIT 設計要領 第10章 (通常断面): the upper half is a **single-centre
    circle** (上半単心円) about a centre on the springline, the sidewalls are vertical below it.
    Internal width at the springline is 9.6 m and the crown stands 6.40 m over the carriageway,
    giving H/W = 0.67 - inside the >= 0.6 the standard requires, and comfortably clearing the
    4.5 m + 0.25 m construction allowance of the 建築限界 clearance envelope.
  * Inspection walkways (監査歩廊) either side: 0.75 m wide, mounted 0.25 m up, as 表10.6.
  * Interior lining panels (内装工) stop at 2.5 m above the carriageway; bare concrete above.
  * Luminaires sit just above the clearance line at 5.0 m in a staggered (千鳥) arrangement at
    7.2 m centres. JIS Z 9116 forbids 0.9-3.3 m spacing at 60 km/h because it induces flicker,
    so the "one lamp every couple of metres" look would be wrong as well as expensive.

The bore is a **closed horseshoe profile swept along the route spline**, not a boolean subtraction
- which is also how the real thing is specified. The rock mass is modelled as an arch that is
*open underneath*, so the carriageway simply passes through the opening: the tunnel contributes
walls, crown, kerbs and walkways but never a floor, which is the standard fix for z-fighting
between a tunnel slab and the road slab.

The massif carries its own hillside rather than relying on the terrain having a convenient hill,
and its feet are sunk 6 m below the carriageway so the rock always meets ground on both sides.
That is the same reason real portals get 抱き擁壁 embracing walls: the join is hidden by geometry
rather than trusted to line up.

Produces:
  SakuraPass_Tunnel.glb   rock massif, bore, walkways, lining band, portal face walls,
                          name plaque and sodium luminaires

Run via:  blender -b -P build_tunnel.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import math

import bmesh
import numpy as np

import sakura_lib as S
import sakura_route as R
import build_road as RD


# --------------------------------------------------------------------- geometry

D_START = 300.0            # arc length of the entry portal - the long straight at 298-345 m
D_END = 342.0              # 42 m of bore; long enough to go dark, short enough to see through
STATION = 1.5

BORE_HALF = 4.80           # internal half-width at the springline -> 9.6 m, 通常断面
SPRING_H = 1.60            # springline height above the carriageway
CROWN_R = BORE_HALF        # 上半単心円: upper half is one circle of this radius
WALK_W = 0.75              # 監査歩廊 width
WALK_H = 0.25              # walkway mount-up
KERB_A = BORE_HALF - WALK_W
FLOOR_H = -0.12            # kerb foot, just under the road slab so nothing ever co-planes

OUTER_W = 13.5             # rock massif half-width
MASSIF_UV = 3.2            # extra tiling on the exterior so strata read as grain, not bands
OUTER_CROWN = 12.6         # ~6 m of rock over the bore crown
FOOT_H = -6.0              # buried skirt

LAMP_SPACING = 7.2
LAMP_H = 5.00


def _crown_arc(segments=18):
    """Upper half: a single circle centred on the springline, right springer -> left."""
    pts = []
    for i in range(segments + 1):
        th = math.pi * i / segments
        pts.append((CROWN_R * math.cos(th), SPRING_H + CROWN_R * math.sin(th)))
    return pts


def tunnel_profile(jitter=None):
    """
    One closed cross-section of the rock, traced clockwise in (across, up).

    ``jitter`` is a per-station list of radial offsets applied to the *outer* silhouette only, so
    the massif reads as eroded rock while the bore stays dimensionally exact.
    """
    j = jitter if jitter is not None else [0.0] * 8
    outer = [
        (-OUTER_W + j[0], FOOT_H),
        (-OUTER_W + j[1], 2.6 + j[1] * 0.4),
        (-OUTER_W * 0.70 + j[2], 6.8 + j[2] * 0.5),
        (-2.4 + j[3], OUTER_CROWN + j[3] * 0.6),
        (2.6 + j[4], OUTER_CROWN - 0.3 + j[4] * 0.6),
        (OUTER_W * 0.70 + j[5], 6.6 + j[5] * 0.5),
        (OUTER_W + j[6], 2.4 + j[6] * 0.4),
        (OUTER_W + j[7], FOOT_H),
    ]

    # Right-hand side: in along the buried underside, up the kerb, out over the walkway, up the
    # wall to the springline.
    right = [
        (6.6, FOOT_H),
        (KERB_A, FLOOR_H),
        (KERB_A, WALK_H),
        (BORE_HALF, WALK_H),
        (BORE_HALF, SPRING_H),
    ]
    # Mirror for the left-hand side, walked in the opposite order.
    left = [
        (-BORE_HALF, SPRING_H),
        (-BORE_HALF, WALK_H),
        (-KERB_A, WALK_H),
        (-KERB_A, FLOOR_H),
        (-6.6, FOOT_H),
    ]
    return outer + right + _crown_arc()[1:-1] + left


def _frames():
    """Route frames clipped to the tunnel's arc-length window, resampled evenly."""
    p, sides, ups, arc, tangents, _ = RD.road_frames(spacing=STATION)
    keep = [i for i in range(len(arc)) if D_START <= arc[i] <= D_END]
    if len(keep) < 4:
        raise RuntimeError(f"tunnel window {D_START}-{D_END} m found only {len(keep)} samples")
    i0, i1 = keep[0], keep[-1]
    sl = slice(i0, i1 + 1)
    return p[sl], sides[sl], ups[sl], arc[sl] - arc[i0], tangents[sl]


def _sweep_varying(name, profiles, origins, sides, ups, arc, uv_scale=(0.12, 0.12),
                   split_at=None):
    """
    Like ``build_road.sweep_frames`` but takes one profile **per station**, which is what lets the
    outer rock silhouette wander while the bore stays exact.

    ``split_at`` is the profile index where the outer massif ends and the bore begins. When given,
    two meshes come back - ``<name>_Massif`` and ``<name>_Bore`` - sharing vertices but not
    materials. That separation matters: the exterior is a sunlit hillside and the interior is an
    enclosed bore, and because Unity's ambient term is unshadowed, albedo is the *only* lever for
    making the inside read as dim. Keeping them on one material forced a tint that was either too
    dark for the hillside or too bright for the bore.
    """
    n_path = len(origins)
    n_prof = len(profiles[0])

    pu = [0.0]
    for i in range(1, n_prof):
        pu.append(pu[-1] + math.dist(profiles[0][i], profiles[0][i - 1]))

    verts = []
    for i in range(n_path):
        o, s, u = origins[i], sides[i], ups[i]
        for (a, h) in profiles[i]:
            q = o + s * a + u * h
            verts.append(S.u2b(q[0], q[1], q[2]))

    groups = {}
    for i in range(n_path - 1):
        for j in range(n_prof):
            j1 = (j + 1) % n_prof
            a_ = i * n_prof + j
            b_ = i * n_prof + j1
            c_ = (i + 1) * n_prof + j1
            d_ = (i + 1) * n_prof + j
            u0 = pu[j] * uv_scale[0]
            u1 = pu[j1 if j1 > j else n_prof - 1] * uv_scale[0]
            v0, v1 = arc[i] * uv_scale[1], arc[i + 1] * uv_scale[1]
            if split_at is None:
                tag = ""
            else:
                # The two wrap-around strips (j == split_at-1 and j == n_prof-1) are the buried
                # underside where the massif meets the bore springer; they belong to the massif.
                tag = "_Bore" if split_at <= j < n_prof - 1 and j != split_at - 1 else "_Massif"

            if tag == "_Bore" or split_at is None:
                # Inside the bore, map U to arc length so the bedding planes run *along* the
                # tunnel. Mapping U to the profile wrapped them around the bore as concentric
                # tree-rings, which is the one thing the eye locks onto in an enclosed space.
                quad_uvs = [(v0, u0), (v0, u1), (v1, u1), (v1, u0)]
            else:
                # The massif wants the opposite. Its cross-section is what faces the rider, so
                # arc-mapped U banded the whole hillside like a layer cake. Mapping U to the
                # profile lays the strata horizontally across the face, and the finer scale keeps
                # them reading as rock grain rather than stripes.
                quad_uvs = [(u0 * MASSIF_UV, v0 * MASSIF_UV), (u1 * MASSIF_UV, v0 * MASSIF_UV),
                            (u1 * MASSIF_UV, v1 * MASSIF_UV), (u0 * MASSIF_UV, v1 * MASSIF_UV)]

            groups.setdefault(tag, []).append(((a_, b_, c_, d_), quad_uvs))

    # --- end caps -----------------------------------------------------------------
    # The rock cross-section is a *simple* polygon: the outer silhouette dips inward along the
    # buried underside, traces the bore, and comes back. So one n-gon per mouth seals the massif.
    # It has to be built from the full ring here, before the split. Letting each part cap its own
    # boundary afterwards is what sealed the two bore mouths shut with solid rock and turned the
    # tunnel into a lump you could neither see through nor ride into.
    if split_at is not None:
        cap_uv = [(a * uv_scale[0] * MASSIF_UV, h * uv_scale[1] * MASSIF_UV)
                  for (a, h) in profiles[0]]
        for i in (0, n_path - 1):
            ring = tuple(i * n_prof + j for j in range(n_prof))
            groups.setdefault("_Massif", []).append((ring, list(cap_uv)))

    # --- face orientation ---------------------------------------------------------
    # Deterministic instead of ``recalc_face_normals``: neither part is a closed solid on its own,
    # so Blender's outward heuristic has no volume to reason about. The bore must face *in* toward
    # the tunnel axis and the massif must face *out* away from it.
    axis = [np.array(S.u2b(*(origins[i] + ups[i] * SPRING_H)), dtype=float)
            for i in range(n_path)]
    fwd = axis[min(1, n_path - 1)] - axis[0]

    def _newell(pts):
        nrm = np.zeros(3)
        for k in range(len(pts)):
            a_p, b_p = pts[k], pts[(k + 1) % len(pts)]
            nrm[0] += (a_p[1] - b_p[1]) * (a_p[2] + b_p[2])
            nrm[1] += (a_p[2] - b_p[2]) * (a_p[0] + b_p[0])
            nrm[2] += (a_p[0] - b_p[0]) * (a_p[1] + b_p[1])
        return nrm

    for tag, items in groups.items():
        for k, (face, fuv) in enumerate(items):
            i = face[0] // n_prof
            pts = [np.array(verts[v], dtype=float) for v in face]
            if len(face) == n_prof:                        # an end cap: face along the bore axis
                want = -fwd if i == 0 else fwd
            else:
                radial = np.mean(pts, axis=0) - axis[i]
                want = -radial if tag == "_Bore" else radial
            if float(np.dot(_newell(pts), want)) < 0.0:
                items[k] = (tuple(reversed(face)), list(reversed(fuv)))

    out = []
    for tag, items in groups.items():
        faces = [f for f, _ in items]
        uvs = [uv for _, fuv in items for uv in fuv]
        # Drop unused vertices so each part carries only its own shell.
        used = sorted({v for f in faces for v in f})
        remap = {v: k for k, v in enumerate(used)}
        out.append(S.mesh_from_arrays(name + tag,
                                      [verts[v] for v in used],
                                      [tuple(remap[v] for v in f) for f in faces],
                                      uvs=uvs, smooth=False))
    return out if split_at is not None else out[0]


def _triangulate_ngons(obj):
    """glTF cannot build a tangent basis for n-gons, and the end caps are 35-sided."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


# -------------------------------------------------------------------- materials

def _rock_material(exterior=True):
    """
    Two tints off one texture set. The exterior massif is a sunlit hillside and wants to sit with
    the surrounding cliffs; the bore interior is an enclosed space and has to be darkened at the
    albedo, because Unity's ambient contribution ignores shadows entirely.
    """
    mat = S.pbr_material("SakuraPass_TunnelRock" if exterior else "SakuraPass_TunnelBore",
                         base_color=(0.40, 0.39, 0.36, 1.0) if exterior else (0.17, 0.165, 0.155, 1.0),
                         roughness=0.92)
    t = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Rock_Albedo.png")), "Base Color")
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Rock_Rough.png"), True), "Roughness")
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Rock_Normal.png"), True), "Normal")
    return mat


def _concrete_material(name, color, rough=0.78):
    """
    Portal face walls are deliberately dark. MLIT 10-10 requires a 面壁 portal to carry a
    low-reflectance finish, because a bright concrete face makes the black-hole effect worse -
    the driver's eye adapts to the glare and the bore reads as a solid black rectangle.
    """
    return S.pbr_material(name, base_color=color, roughness=rough)


# ------------------------------------------------------------------------ build

def build_tunnel(name="SakuraPass_Tunnel"):
    print(f"[sakura] building {name}...")
    p, sides, ups, arc, tangents = _frames()
    n = len(p)
    objs = []

    # --- rock massif + bore -------------------------------------------------------
    profiles = []
    for i in range(n):
        t = arc[i]
        jitter = []
        for k in range(8):
            v = (math.sin(t * 0.21 + k * 1.7) * 0.85 +
                 math.sin(t * 0.47 + k * 3.1) * 0.45 +
                 math.sin(t * 0.93 + k * 0.7) * 0.22)
            # Flatten the wobble at both mouths so the portal face walls seat on a clean ring.
            ease = min(1.0, min(t, arc[-1] - t) / 6.0)
            jitter.append(v * ease)
        profiles.append(tunnel_profile(jitter))

    # The massif and the bore are swept together so they share vertices, then split into two
    # meshes at the springer so each can carry its own albedo (see _sweep_varying).
    massif, bore = None, None
    for part in _sweep_varying(f"{name}_Rock", profiles, p, sides, ups, arc, split_at=8):
        # No cap_open_boundaries / recalc_normals here: the sweep already seals the massif's two
        # mouths and winds every face itself. Capping the parts separately filled in the bore.
        _triangulate_ngons(part)
        S.assign_material(part, _rock_material("Massif" in part.name))
        objs.append(part)
        if "Massif" in part.name:
            massif = part
        else:
            bore = part

    # --- interior lining band -----------------------------------------------------
    # 内装工 stops at 2.5 m; modelled as its own sheet held 4 cm off the rock rather than as a
    # second material on the same faces, so there is no coplanar flicker.
    lining_profile = [
        (KERB_A - 0.02, WALK_H), (BORE_HALF - 0.04, WALK_H),
        (BORE_HALF - 0.04, 2.5),
    ]
    for sgn, tag in ((1.0, "R"), (-1.0, "L")):
        prof = [(a * sgn, h) for (a, h) in lining_profile]
        band = RD.sweep_frames(f"{name}_Lining_{tag}", prof, p, sides, ups, arc,
                               uv_scale=(0.5, 0.35))
        S.add_solidify(band, thickness=0.03, offset=1.0 * sgn)
        S.assign_material(band, _concrete_material(f"{name}_LiningPanel",
                                                   (0.74, 0.72, 0.68, 1.0), rough=0.42))
        objs.append(band)

    # --- portal face walls (面壁) + wing walls ------------------------------------
    face_mat = _concrete_material(f"{name}_PortalFace", (0.30, 0.29, 0.28, 1.0), rough=0.86)
    for end, (idx, out_sgn) in enumerate(((0, -1.0), (n - 1, 1.0))):
        o, s, u, tg = p[idx], sides[idx], ups[idx], tangents[idx]
        depth = 0.65
        # A ring standing proud of the rock: same bore opening, rectangular outside. It stops
        # 1.5 m below the carriageway rather than following the massif's -6 m skirt - the skirt
        # is there to guarantee the rock meets ground, but carrying the *face wall* down with it
        # made the portal a 15 m billboard instead of the modest ring a real 面壁 is.
        p_foot = -1.5
        ring = [
            (-8.6, p_foot), (-8.6, 8.6), (8.6, 8.6), (8.6, p_foot),
            (6.6, p_foot), (KERB_A, FLOOR_H), (KERB_A, WALK_H),
            (BORE_HALF, WALK_H), (BORE_HALF, SPRING_H),
        ]
        ring += _crown_arc()[1:-1]
        ring += [(-BORE_HALF, SPRING_H), (-BORE_HALF, WALK_H),
                 (-KERB_A, WALK_H), (-KERB_A, FLOOR_H), (-6.6, p_foot)]

        origins = [o + tg * (out_sgn * depth * k) for k in range(2)]
        wall = RD.sweep_frames(f"{name}_Portal_{end}", ring, origins,
                               [s, s], [u, u], np.array([0.0, depth]),
                               uv_scale=(0.28, 0.28), close_profile=True)
        S.cap_open_boundaries(wall)
        S.recalc_normals(wall)
        S.assign_material(wall, face_mat)
        objs.append(wall)

        # 笠石 coping band along the top of the face wall, standing proud of it. A 面壁 is never
        # a bare slab: without a capping course the portal reads as a black rectangle pasted onto
        # the hillside, which is exactly what the first pass looked like.
        for (cy, cd, ch, cw) in ((8.82, 0.42, 0.46, 9.05), (8.30, 0.22, 0.30, 8.80)):
            cq = o + tg * (out_sgn * (depth * 0.5)) + u * cy
            cope = RD.sweep_frames(
                f"{name}_Coping_{end}_{int(cy * 10)}",
                [(-cw, -ch * 0.5), (cw, -ch * 0.5), (cw, ch * 0.5), (-cw, ch * 0.5)],
                [cq - tg * (out_sgn * (depth * 0.5 + cd)),
                 cq + tg * (out_sgn * (depth * 0.5 + cd))],
                [s, s], [u, u], np.array([0.0, depth + cd * 2.0]),
                uv_scale=(0.5, 0.5), close_profile=True)
            S.cap_open_boundaries(cope)
            S.recalc_normals(cope)
            S.assign_material(cope, face_mat)
            objs.append(cope)

        # 扁額 - the carved name tablet, set on the face below the coping course (the coping
        # bands occupy 8.15 m upward, so a tablet centred at 8.05 was buried inside them).
        pq = o + tg * (out_sgn * (depth + 0.06)) + u * 7.25
        plaque = RD.sweep_frames(
            f"{name}_Plaque_{end}", [(-1.15, -0.52), (1.15, -0.52), (1.15, 0.52), (-1.15, 0.52)],
            [pq, pq + tg * (out_sgn * 0.10)], [s, s], [u, u], np.array([0.0, 0.10]),
            uv_scale=(1, 1), close_profile=True)
        S.cap_open_boundaries(plaque)
        S.recalc_normals(plaque)
        S.assign_material(plaque, _concrete_material(f"{name}_Plaque",
                                                     (0.52, 0.47, 0.40, 1.0), rough=0.62))
        objs.append(plaque)

    # --- luminaires ---------------------------------------------------------------
    # Staggered (千鳥) at 7.2 m, mounted 5.0 m up in the haunch - just above the clearance line,
    # which is where the standard says fixtures have to live since the section gets no extra
    # allowance for them. Amber, because low-pressure sodium is the signature of these tunnels.
    sodium = S.pbr_material(f"{name}_Sodium", base_color=(1.0, 0.72, 0.30, 1.0),
                            roughness=0.30, emission=(1.0, 0.62, 0.20, 1.0),
                            emission_strength=7.0)
    nxt, k = 2.0, 0
    for i in range(n):
        if arc[i] < nxt:
            continue
        sgn = 1.0 if k % 2 == 0 else -1.0
        o, s, u, tg = p[i], sides[i], ups[i], tangents[i]
        # Lateral offset has to respect the crown arc, not the springline half-width: at LAMP_H
        # the bore is only sqrt(CROWN_R^2 - (LAMP_H - SPRING_H)^2) = 3.39 m half-wide, so the
        # old BORE_HALF - 0.55 = 4.25 m buried every fixture inside the rock mass.
        lam_x = math.sqrt(max(0.25, CROWN_R ** 2 - (LAMP_H - SPRING_H) ** 2)) - 0.52
        base = o + s * (sgn * lam_x) + u * LAMP_H
        lamp = RD.sweep_frames(f"{name}_Lamp_{k}",
                               [(-0.22, -0.13), (0.22, -0.13), (0.22, 0.13), (-0.22, 0.13)],
                               [base, base + tg * 1.60], [s, s], [u, u],
                               np.array([0.0, 1.60]), uv_scale=(1, 1), close_profile=True)
        S.cap_open_boundaries(lamp)
        S.recalc_normals(lamp)
        S.assign_material(lamp, sodium)
        objs.append(lamp)
        nxt += LAMP_SPACING
        k += 1

    print(f"[sakura]   {len(objs)} tunnel parts, {k} luminaires, bore {arc[-1]:.1f} m")
    for o in (massif, bore):
        S.report(o.name, o)
    S.export_glb(objs, f"{name}.glb")
    return objs


def main():
    S.reset_scene()
    build_tunnel()
    print("[sakura] tunnel build complete.")


if __name__ == "__main__":
    main()

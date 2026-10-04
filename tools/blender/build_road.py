"""
Sakura Pass carriageway, line markings and crash barrier.

Everything here is swept along the shared route spline from ``sakura_route.frames()``, so the
asphalt inherits the same banking and gradient the terrain was carved for. Nothing is a scaled
cube: the road is a real cambered cross-section, the W-beam barrier is a true corrugated profile,
and the posts are swept C-sections with bolt bosses.

Produces:
  SakuraPass_Road_HD.glb        carriageway slab + shoulders + buried skirt
  SakuraPass_RoadMarkings.glb   yellow no-overtaking centre line + white edge lines
  SakuraPass_RoadDrain.glb      mountain-side concrete gutter (側溝), lid joints and grate inlets
  SakuraPass_Guardrail_HD.glb   W-beam barrier, posts, blockouts and reflectors

Run via:  blender -b -P build_road.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import math

import numpy as np

import sakura_lib as S
import sakura_route as R


SPACING = 1.6                  # centreline resample; fine enough that corners read as smooth
HALF = R.ROAD_HALF_WIDTH       # 3.5 m
SHOULDER = R.SHOULDER_WIDTH    # 0.55 m
CROWN = R.ROAD_CROWN           # 0.06 m centre camber

ASPHALT_TILE = 0.22            # texture repeats per metre
LIFT = 0.014                   # markings float this far above the asphalt

# ---------------------------------------------------------------- marking standard
# Japanese 区画線 for a two-lane rural/mountain carriageway (MLIT 道路標識、区画線及び道路標示
# に関する命令). Only TWO line types belong on a 7 m two-lane touge:
#   * 中央線 - solid YELLOW where overtaking is prohibited, 15 cm
#   * 車道外側線 - solid WHITE outside line, 15 cm, just inboard of the pavement edge
# A dashed lane line inboard of the outside line (which this build used to paint) is a
# multi-lane marking; on a two-lane road it reads as an invented "cycle lane" and was the
# source of the feedback about arbitrary extra lines. PROVISIONAL widths, real standard.
CENTRE_LINE_W = 0.15
EDGE_LINE_W = 0.15
EDGE_LINE_INSET = 0.10         # clear asphalt between paint and pavement edge

# ---------------------------------------------------------------- drainage standard
# 山側 (cut side = the rider's LEFT, since route `side` is the rider's right/valley side)
# carries a concrete side ditch with lid units - the standard Japanese mountain-road section.
# The valley side keeps the chip-seal shoulder and the W-beam barrier. All PROVISIONAL.
GUTTER_WIDTH = 0.60            # lid band width, ~JIS 600 mm 側溝蓋
GUTTER_PROUD = 0.020           # lid sits this far above the carriageway edge plane
GUTTER_LID_M = 0.60            # one lid unit, so the joint pitch
GRATE_SPACING = 10.0           # metres between grate inlets (集水桝)
GRATE_LEN = 0.60
GRATE_INSET = 0.09             # grate is narrower than the lid band, both sides
GRATE_BARS = 6                 # transverse bars over the inlet
GRATE_BAR_W = 0.045


# ------------------------------------------------------------------ sweep helper

def sweep_frames(name, profile, origins, sides, ups, arc, uv_scale=(1.0, 1.0),
                 u_from_profile=True, close_profile=False):
    """
    Sweep a 2D ``(across, up)`` profile along an explicit list of frames.

    Unlike ``sakura_lib.sweep_profile`` this takes the side/up basis per sample, which is what
    lets the carriageway and barrier inherit the route's banking. Emits Unity-space vertices
    through ``u2b`` so the GLB lands at identity transform in Unity.
    """
    n_path = len(origins)
    n_prof = len(profile)

    # Cumulative profile arc length gives a non-stretching U across the cross-section.
    if u_from_profile:
        pu = [0.0]
        for i in range(1, n_prof):
            pu.append(pu[-1] + math.dist(profile[i], profile[i - 1]))
    else:
        pu = [i / float(n_prof - 1) for i in range(n_prof)]

    verts = []
    for i in range(n_path):
        o, s, u = origins[i], sides[i], ups[i]
        for (a, h) in profile:
            p = o + s * a + u * h
            verts.append(S.u2b(p[0], p[1], p[2]))

    faces = []
    uvs = []
    span = n_prof if close_profile else n_prof - 1
    for i in range(n_path - 1):
        for j in range(span):
            j0 = j
            j1 = (j + 1) % n_prof
            a = i * n_prof + j0
            b = i * n_prof + j1
            c = (i + 1) * n_prof + j1
            d = (i + 1) * n_prof + j0
            # Unity-space CCW: `u2b` preserves handedness, so authoring the quad in this order
            # is what makes the swept surface face outward/up once it reaches Unity. The
            # reversed order used to point the carriageway at the ground, which made it
            # invisible to the rider's grounding raycast.
            faces.append((a, b, c, d))
            u0 = pu[j0] * uv_scale[0]
            u1 = pu[j1 if j1 > j0 else n_prof - 1] * uv_scale[0]
            v0 = arc[i] * uv_scale[1]
            v1 = arc[i + 1] * uv_scale[1]
            uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    return S.mesh_from_arrays(name, verts, faces, uvs=uvs)


def road_frames(spacing=SPACING, seg_id=None):
    if seg_id is None:
        p, tangents, sides, ups, banks, arc = R.frames(spacing=spacing)
    else:
        p, tangents, sides, ups, banks, arc = R.segment_frames(seg_id, spacing=spacing)
    return p, sides, ups, arc, tangents, banks


# ------------------------------------------------------------------ carriageway

def camber(a):
    """Parabolic crown: highest on the centreline, shedding to each edge."""
    t = min(abs(a) / HALF, 1.0)
    return CROWN * (1.0 - t * t)


def build_road():
    print("[sakura] sweeping carriageway...")

    mat = S.pbr_material("SakuraPass_Asphalt", base_color=(0.16, 0.16, 0.17, 1.0),
                         roughness=0.82)
    tex = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(tex, "Sakura_Asphalt_Albedo.png")), "Base Color")
    S.set_texture(mat, S.load_image(os.path.join(tex, "Sakura_Asphalt_Rough.png"), True), "Roughness")
    S.set_texture(mat, S.load_image(os.path.join(tex, "Sakura_Asphalt_Normal.png"), True), "Normal")

    objs = []
    # Every segment of the network is swept with the identical cross-section, so a rider
    # crossing a junction never sees the carriageway change width, camber or texture scale.
    for seg_id in ([None] + R.EXPANSION_IDS):
        name = "SakuraPass_Road" if seg_id is None else f"SakuraPass_Road_{seg_id}"
        p, sides, ups, arc, _, _ = road_frames(seg_id=seg_id)

        # Cross-section, centre outward. Sampled densely across the lanes so the camber is a
        # real curve rather than a single flat quad, and so the crown catches a specular
        # highlight.
        steps = 14
        profile = []
        for i in range(-steps, steps + 1):
            a = HALF * i / steps
            profile.append((a, camber(a)))

        # Shoulders: coarse chip seal, slightly lower than the running surface.
        edge = HALF
        shoulder_pts = [
            (edge + SHOULDER * 0.45, -0.012),
            (edge + SHOULDER, -0.045),
        ]
        # Buried skirt: wide and shallow so the terrain verge always covers it, rather than a
        # deep vertical wall that showed as an exposed dark band down both sides of the road.
        skirt = [
            (edge + SHOULDER + 0.30, -0.19),
            (edge + SHOULDER + 1.60, -0.70),
        ]

        full = ([(-p_[0], p_[1]) for p_ in reversed(skirt)] +
                [(-p_[0], p_[1]) for p_ in reversed(shoulder_pts)] +
                profile +
                shoulder_pts + skirt)

        road = sweep_frames(name, full, p, sides, ups, arc,
                            uv_scale=(ASPHALT_TILE, ASPHALT_TILE))
        S.assign_material(road, mat)
        S.report(name, road)
        objs.append(road)

    S.export_glb(objs, "SakuraPass_Road_HD.glb")
    return objs


# ------------------------------------------------------------------ line markings

def _stripe(name, p, sides, ups, arc, offset, width, lift, dashed=None):
    """
    Build a painted stripe as its own thin ribbon of geometry.

    ``dashed`` is ``(on_m, off_m)``; ``None`` paints a continuous line.
    """
    verts = []
    faces = []
    uvs = []

    def emit(i0, i1, base):
        for i in range(i0, i1 + 1):
            o, s, u = p[i], sides[i], ups[i]
            for a in (offset - width * 0.5, offset + width * 0.5):
                q = o + s * a + u * (camber(a) + lift)
                verts.append(S.u2b(q[0], q[1], q[2]))
        for k in range(i1 - i0):
            a = base + k * 2
            faces.append((a, a + 1, a + 3, a + 2))
            v0 = arc[i0 + k] * 0.5
            v1 = arc[i0 + k + 1] * 0.5
            uvs.extend([(0, v0), (1, v0), (1, v1), (0, v1)])
        return base + (i1 - i0 + 1) * 2

    base = 0
    if dashed is None:
        base = emit(0, len(p) - 1, base)
    else:
        on, off = dashed
        period = on + off
        i = 0
        n = len(p)
        while i < n - 1:
            # Walk forward until this sample leaves the painted part of the cycle.
            if (arc[i] % period) < on:
                j = i
                while j < n - 1 and (arc[j + 1] % period) < on and arc[j + 1] > arc[j]:
                    j += 1
                if j > i:
                    base = emit(i, j, base)
                i = j + 1
            else:
                i += 1

    return S.mesh_from_arrays(name, verts, faces, uvs=uvs)


def build_markings():
    print("[sakura] painting line markings...")
    objs = []

    # Japanese mountain passes use a solid yellow centre line where overtaking is prohibited,
    # and a solid white 車道外側線 on each side. Nothing else: see the marking-standard note at
    # the top of this module for why the old dashed inner line was removed.
    yellow = S.pbr_material("SakuraPass_LineYellow", base_color=(0.86, 0.68, 0.16, 1.0),
                            roughness=0.55)
    white = S.pbr_material("SakuraPass_LineWhite", base_color=(0.90, 0.90, 0.88, 1.0),
                           roughness=0.55)

    for seg_id in ([None] + R.EXPANSION_IDS):
        tag = "" if seg_id is None else f"_{seg_id}"
        p, sides, ups, arc, _, _ = road_frames(seg_id=seg_id)

        centre = _stripe(f"Marking_Centre{tag}", p, sides, ups, arc, 0.0, CENTRE_LINE_W, LIFT)
        S.assign_material(centre, yellow)
        objs.append(centre)

        # Outside line: its OUTER edge sits EDGE_LINE_INSET inboard of the pavement edge, so the
        # paint never runs off the asphalt and the ravelled edge stays visible beyond it.
        edge_offset = HALF - EDGE_LINE_INSET - EDGE_LINE_W * 0.5
        for sign in (-1.0, 1.0):
            side_tag = 'R' if sign > 0 else 'L'
            e = _stripe(f"Marking_Edge_{side_tag}{tag}",
                        p, sides, ups, arc, sign * edge_offset, EDGE_LINE_W, LIFT)
            S.assign_material(e, white)
            objs.append(e)

    for o in objs:
        S.report(o.name, o)
    S.export_glb(objs, "SakuraPass_RoadMarkings.glb")
    return objs


# ------------------------------------------------------------------ drainage

def _quad_strip(name, quads, uvs=None):
    """One mesh holding many disjoint quads. Each quad is 4 Unity-space points, already in
    winding order; this is how ~2 300 lid joints cost one object instead of 2 300."""
    verts = []
    faces = []
    uv_out = []
    for q in quads:
        b = len(verts)
        for pt in q:
            verts.append(S.u2b(pt[0], pt[1], pt[2]))
        faces.append((b, b + 1, b + 2, b + 3))
        uv_out.extend([(0, 0), (1, 0), (1, 1), (0, 1)])
    return S.mesh_from_arrays(name, verts, faces, uvs=uv_out if uvs is None else uvs)


def build_drainage():
    """
    Mountain-side concrete side ditch (側溝) with lid units and grate inlets.

    This is the drainage cue the carriageway was missing: a Japanese mountain road does not end
    in bare asphalt against grass, it ends against a concrete channel that carries cut-slope
    runoff. Built as a SEPARATE object outboard of the carriageway edge, so the riding surface,
    its camber and its collision mesh are untouched.

    The lid band sits GUTTER_PROUD above the carriageway edge plane - the asphalt shoulder falls
    away to -0.045 over the same span, so a flush band would be buried by the shoulder and the
    terrain verge and render as nothing at all.
    """
    print("[sakura] sweeping mountain-side gutter...")

    concrete = S.pbr_material("SakuraPass_GutterConcrete", base_color=(0.62, 0.61, 0.58, 1.0),
                              roughness=0.88)
    joint_mat = S.pbr_material("SakuraPass_GutterJoint", base_color=(0.34, 0.33, 0.31, 1.0),
                               roughness=0.95)
    grate_mat = S.pbr_material("SakuraPass_DrainGrate", base_color=(0.20, 0.20, 0.21, 1.0),
                               roughness=0.60, metallic=0.55)
    void_mat = S.pbr_material("SakuraPass_DrainVoid", base_color=(0.04, 0.04, 0.05, 1.0),
                              roughness=1.0)

    objs = []
    bars = []
    p, sides, ups, arc, _, _ = road_frames(spacing=1.2)

    inner = -(HALF + 0.02)
    outer = -(HALF + 0.02 + GUTTER_WIDTH)
    # Increasing `across` order, exactly like the carriageway profile, so the swept quads come
    # out facing up in Unity without a winding flip (invariant 2).
    profile = [
        (outer - 0.10, GUTTER_PROUD - 0.26),   # buried outer face, hidden by the verge
        (outer, GUTTER_PROUD - 0.004),         # outer top edge of the lid band
        (outer + GUTTER_WIDTH * 0.55, GUTTER_PROUD - 0.012),   # shallow fall toward the channel
        (inner, GUTTER_PROUD),                 # lip against the carriageway edge
    ]
    lid = sweep_frames("Drain_Gutter", profile, p, sides, ups, arc,
                       uv_scale=(1.0, 0.5))
    S.assign_material(lid, concrete)
    S.report("Drain_Gutter", lid)
    objs.append(lid)

    # --- lid unit joints ------------------------------------------------------------------
    # A continuous concrete ribbon reads as extruded plastic. Real lids are 600 mm units, and
    # the dark joint between them is the single cue that makes the band read as placed units.
    joints = []
    grates = []
    total = float(arc[-1])
    d = GUTTER_LID_M
    next_grate = GRATE_SPACING
    while d < total - GUTTER_LID_M:
        i = R.index_at_distance(arc, d)
        o, s, u = p[i], sides[i], ups[i]
        t_hat = (p[min(i + 1, len(p) - 1)] - p[max(i - 1, 0)])
        n = float(np.linalg.norm(t_hat))
        t_hat = t_hat / n if n > 1e-6 else np.array([0.0, 0.0, 1.0])

        def at(across, up_h, along):
            q = o + s * across + u * up_h + t_hat * along
            return (q[0], q[1], q[2])

        half_j = 0.012
        joints.append([at(outer, GUTTER_PROUD + 0.0015, -half_j),
                       at(inner, GUTTER_PROUD + 0.0025, -half_j),
                       at(inner, GUTTER_PROUD + 0.0025, half_j),
                       at(outer, GUTTER_PROUD + 0.0015, half_j)])

        if d >= next_grate:
            # A flat dark panel reads as a painted rectangle. A real 集水桝 reads as a DARK VOID
            # crossed by metal bars, and that two-height read is what survives at a grazing
            # rider's-eye angle: the void sits just above the lid, the bars 8 mm above the void.
            a0 = outer + GRATE_INSET
            a1 = inner - GRATE_INSET
            hv = GUTTER_PROUD + 0.002
            hb = GUTTER_PROUD + 0.010
            grates.append([at(a0, hv, -GRATE_LEN * 0.5),
                           at(a1, hv, -GRATE_LEN * 0.5),
                           at(a1, hv, GRATE_LEN * 0.5),
                           at(a0, hv, GRATE_LEN * 0.5)])
            for k in range(GRATE_BARS):
                c0 = -GRATE_LEN * 0.5 + (k + 0.18) * (GRATE_LEN / GRATE_BARS)
                c1 = c0 + GRATE_BAR_W
                bars.append([at(a0, hb, c0), at(a1, hb, c0),
                             at(a1, hb, c1), at(a0, hb, c1)])
            next_grate += GRATE_SPACING
        d += GUTTER_LID_M

    if joints:
        jm = _quad_strip("Drain_Joints", joints)
        S.assign_material(jm, joint_mat)
        S.report("Drain_Joints", jm)
        objs.append(jm)
    if grates:
        gm = _quad_strip("Drain_Grates", grates)
        S.assign_material(gm, void_mat)
        S.report("Drain_Grates", gm)
        objs.append(gm)
    if bars:
        bm = _quad_strip("Drain_GrateBars", bars)
        S.assign_material(bm, grate_mat)
        S.report("Drain_GrateBars", bm)
        objs.append(bm)

    print(f"[sakura]   gutter {total:.0f} m, {len(joints)} lid joints, {len(grates)} grate inlets, "
          f"{len(bars)} bars")
    S.export_glb(objs, "SakuraPass_RoadDrain.glb")
    return objs


# ------------------------------------------------------------------ guardrail

# True corrugated W-beam section: 312 mm tall, 85 mm deep, flanges out, centre rib recessed.
W_BEAM = [
    (0.000, 0.156),
    (0.062, 0.140),
    (0.085, 0.098),
    (0.085, 0.052),
    (0.034, 0.006),
    (0.034, -0.006),
    (0.085, -0.052),
    (0.085, -0.098),
    (0.062, -0.140),
    (0.000, -0.156),
]

RAIL_HEIGHT = 0.72             # beam centre above the verge
POST_SPACING = 2.6


def build_guardrail():
    """
    Barrier on the rider's right - the valley side, which is the only side that needs one.

    Only placed where the drop actually warrants it, and broken into runs so it does not read as
    one endless ribbon. Posts are swept C-sections with a blockout spacer and a reflector tab.
    """
    print("[sakura] building W-beam guardrail...")

    objs = []
    steel = S.pbr_material("SakuraPass_Guardrail", base_color=(0.62, 0.64, 0.66, 1.0),
                           roughness=0.42, metallic=0.85)
    dark = S.pbr_material("SakuraPass_GuardrailPost", base_color=(0.40, 0.42, 0.44, 1.0),
                          roughness=0.55, metallic=0.8)
    reflector = S.pbr_material("SakuraPass_Reflector", base_color=(0.95, 0.75, 0.15, 1.0),
                               roughness=0.25, emission=(1.0, 0.72, 0.18, 1.0),
                               emission_strength=1.4)

    def emit_runs(p, sides, ups, arc, runs, tag, post_spacing):
        offset = HALF + SHOULDER + 0.24
        rail_origin = np.array([p[i] + sides[i] * offset + ups[i] * (camber(HALF) - 0.045)
                                for i in range(len(p))])
        # The beam faces the carriageway, so the profile's "across" axis points inboard.
        beam_profile = [(-d, h) for (d, h) in W_BEAM]
        n = len(p)
        runs = [(i0, min(i1, n - 1)) for (i0, i1) in runs if i1 - i0 > 2]

        for ri, (i0, i1) in enumerate(runs):
            origins = [rail_origin[i] for i in range(i0, i1 + 1)]
            sd = [sides[i] for i in range(i0, i1 + 1)]
            up = [ups[i] for i in range(i0, i1 + 1)]
            # Raise the whole run to beam height.
            origins = [origins[k] + up[k] * RAIL_HEIGHT for k in range(len(origins))]
            sub_arc = arc[i0:i1 + 1] - arc[i0]

            beam = sweep_frames(f"Guardrail_Beam_{tag}{ri}", beam_profile, origins, sd, up,
                                sub_arc, uv_scale=(1.6, 0.35))
            S.add_solidify(beam, thickness=0.004, offset=1.0)
            S.assign_material(beam, steel)
            objs.append(beam)

            # --- posts -----------------------------------------------------------------
            post_profile = [
                (-0.055, 0.0), (0.055, 0.0), (0.055, 0.018), (0.012, 0.018),
                (0.012, 0.082), (0.055, 0.082), (0.055, 0.100), (-0.055, 0.100),
            ]
            step = 0
            k = 0
            while k < len(origins):
                if sub_arc[k] >= step:
                    base = rail_origin[i0 + k]
                    u = up[k]
                    s = sd[k]
                    # Vertical sweep: drive the post 0.9 m into the verge, 0.78 m proud of it.
                    path = [base + u * (-0.90 + t * (0.78 + 0.90) / 6.0) for t in range(7)]
                    post = sweep_frames(f"Guardrail_Post_{tag}{ri}_{k}", post_profile, path,
                                        [s] * 7, [np.cross(s, u)] * 7,
                                        np.linspace(0, 1.68, 7), uv_scale=(1, 1),
                                        close_profile=True)
                    S.cap_open_boundaries(post)
                    S.assign_material(post, dark)
                    objs.append(post)

                    # Blockout spacer between post and beam, and a reflector on the beam face.
                    tag_o = base + u * (RAIL_HEIGHT + 0.10) - s * 0.09
                    refl = sweep_frames(f"Guardrail_Reflector_{tag}{ri}_{k}",
                                        [(0.0, -0.045), (0.0, 0.045)],
                                        [tag_o, tag_o - s * 0.012],
                                        [np.cross(u, s), np.cross(u, s)], [u, u],
                                        np.array([0.0, 0.012]))
                    S.assign_material(refl, reflector)
                    objs.append(refl)

                    step += post_spacing
                k += 1

    # ---------------------------------------------------------------- the built pass
    p, sides, ups, arc, _, _ = road_frames(spacing=0.65)

    # Continuous runs wherever the valley falls away; a gap at each end and a service gap midway.
    # The gaps are not arbitrary: 0.03-0.13 and 0.23-0.40 of the *climb* carry the timber
    # 景観ガードレール from build_expand.py instead (concept panels 02 and 05), and 0.560-0.650 is
    # the tunnel, where the barrier is replaced by the bore's own kerb and inspection walkway.
    #
    # These are resolved against the climb length rather than against total route length: once
    # the descent was added the route doubled, and plain fractions would have dragged every run
    # - and every gap the tunnel and timber rail depend on - hundreds of metres downhill.
    climb_runs = [(0.135, 0.225), (0.405, 0.460), (0.500, 0.560), (0.650, 0.970)]
    runs = [R.climb_window(arc, f0, f1) for (f0, f1) in climb_runs]

    # The descent is the exposed side of the pass: a near-continuous barrier in absolute arc
    # length, broken only where the road pulls back onto the flank. The switchback section
    # (0-300 m past the summit) is barriered almost end to end - both hairpins are on the
    # drop-off - and the runs then continue along the lakeshore traverse to the finish.
    climb_end = R.climb_length()
    for (d0, d1) in ((8.0, 150.0), (158.0, 300.0), (312.0, 430.0), (442.0, 560.0),
                     (572.0, 690.0), (702.0, 790.0)):
        runs.append((R.index_at_distance(arc, climb_end + d0),
                     R.index_at_distance(arc, climb_end + d1)))
    emit_runs(p, sides, ups, arc, runs, "", POST_SPACING)

    # ------------------------------------------------------- expansion segments
    # Barriered only where the drop is genuinely exposed, and at half the post density of the
    # hero pass: the Aozora switchback field alone would otherwise add ~3 000 objects to a
    # scene that already carries 1 400 pieces of dressing. Fractions of segment length, all
    # PROVISIONAL.
    expansion_runs = {
        "s1": [(0.06, 0.30), (0.34, 0.52), (0.56, 0.80)],       # lakeshore bench above water
        "aozora": [(0.12, 0.46), (0.50, 0.94)],                 # the switchback field
        "maple": [(0.30, 0.52)],                                # the one shelf above the paddy
    }
    for seg_id, fracs in expansion_runs.items():
        p, sides, ups, arc, _, _ = road_frames(spacing=0.65, seg_id=seg_id)
        total = arc[-1]
        runs = [(R.index_at_distance(arc, f0 * total), R.index_at_distance(arc, f1 * total))
                for (f0, f1) in fracs]
        emit_runs(p, sides, ups, arc, runs, f"{seg_id}_", POST_SPACING * 2.0)

    print(f"[sakura]   {len(objs)} barrier parts")
    S.export_glb(objs, "SakuraPass_Guardrail_HD.glb")
    return objs


def main():
    S.reset_scene()
    build_road()

    S.reset_scene()
    build_markings()

    S.reset_scene()
    build_drainage()

    S.reset_scene()
    build_guardrail()
    print("[sakura] road build complete.")


if __name__ == "__main__":
    main()

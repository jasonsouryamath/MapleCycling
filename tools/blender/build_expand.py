"""
Sakura Pass expansion assets - the concept-board elements the first pass never built.

Each one is dimensioned from a real-world standard rather than eyeballed; the sources are noted
per builder because the numbers are the whole point.

  SakuraPass_Timber_Guardrail.glb  景観ガードレール, the timber barrier of concept panels 02/05
  SakuraPass_Lake_Village.glb      the far-shore town of panels 01/05, with its own shoreline
  SakuraPass_Lake_Torii.glb        the torii standing in the water from the detail strip
  SakuraPass_Hillside_Sign.glb     the hillside SAKURA PASS lettering of panel 06
  SakuraPass_Petal_Drifts.glb      fallen-petal accumulation along both kerbs

Run via:  blender -b -P build_expand.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import math
import random

import bpy
import numpy as np

import sakura_lib as S
import sakura_route as R
import build_road as RD
import build_props as P


LAKE_LEVEL = -44.0


# ------------------------------------------------------------- timber guardrail

# 越井木材 / EMC "LOG GUARD": thinned-timber beams, and the product is deliberately dimensioned
# to match the steel W-beam so the beam alone can be swapped on existing posts. That means the
# steel rail's 4 m post pitch and 0.6-1.0 m legal height band apply unchanged.
TIMBER_RAIL_R = 0.095          # φ190 mm beam
TIMBER_POST_R = 0.085
RAIL_HEIGHTS = (0.46, 0.82)    # two rails; top lands inside the 0.60-1.00 m band
TIMBER_POST_PITCH = 2.0        # 越井 product grade - the rustic look the concept board shows

# 景観に配慮した防護柵ガイドライン replaced the old white with three landscape colours. For a
# national-park touge the default top coat is dark brown, 10YR 2.0/1.0.
DARK_BROWN = (0.20, 0.17, 0.15, 1.0)

# Route fractions that get timber instead of steel: the Forest and Lakeside sections.
TIMBER_RUNS = ((0.030, 0.130), (0.230, 0.400))


def build_timber_guardrail(name="SakuraPass_Timber_Guardrail"):
    print(f"[sakura] building {name}...")
    p, sides, ups, arc, _, _ = RD.road_frames(spacing=0.65)
    offset = RD.HALF + RD.SHOULDER + 0.24

    timber = S.pbr_material("SakuraPass_RailWood", base_color=DARK_BROWN, roughness=0.86)
    t = S.textures_dir()
    S.set_texture(timber, S.load_image(os.path.join(t, "Sakura_Bark_Albedo.png")), "Base Color")
    S.set_texture(timber, S.load_image(os.path.join(t, "Sakura_Bark_Normal.png"), True), "Normal")

    objs = []
    n = len(p)
    # Fractions of the *climb*, not of the whole route: the descent added ~590 m of road after
    # these runs were authored, and plain route fractions would have slid them off the forest
    # and lakeside sections they were placed for and straight into the steel rail's runs.
    for ri, (f0, f1) in enumerate(TIMBER_RUNS):
        i0, i1 = R.climb_window(arc, f0, f1)
        i1 = min(i1, n - 1)
        origins = [p[i] + sides[i] * offset for i in range(i0, i1 + 1)]
        sd = [sides[i] for i in range(i0, i1 + 1)]
        up = [ups[i] for i in range(i0, i1 + 1)]
        sub = arc[i0:i1 + 1] - arc[i0]

        # Round beams: an 8-gon swept along the run reads as a log at any distance we see it.
        ring = [(math.cos(k * math.pi / 4) * TIMBER_RAIL_R,
                 math.sin(k * math.pi / 4) * TIMBER_RAIL_R) for k in range(8)]
        for j, h in enumerate(RAIL_HEIGHTS):
            og = [origins[k] + up[k] * h for k in range(len(origins))]
            beam = RD.sweep_frames(f"{name}_Rail_{ri}_{j}", ring, og, sd, up, sub,
                                   uv_scale=(1.4, 0.5), close_profile=True)
            S.cap_open_boundaries(beam)
            S.assign_material(beam, timber)
            objs.append(beam)

        # Posts. The landscape guideline is explicit that post spacing must stay uniform -
        # randomising it "gives a cluttered impression" - so this steps on exact arc length.
        step, k = 0.0, 0
        while k < len(origins):
            if sub[k] >= step:
                base = origins[k]
                u, s = up[k], sd[k]
                post_ring = [(math.cos(q * math.pi / 4) * TIMBER_POST_R,
                              math.sin(q * math.pi / 4) * TIMBER_POST_R) for q in range(8)]
                path = [base + u * (-0.70 + t_ * (0.98 + 0.70) / 4.0) for t_ in range(5)]
                post = RD.sweep_frames(f"{name}_Post_{ri}_{k}", post_ring, path,
                                       [s] * 5, [np.cross(s, u)] * 5,
                                       np.linspace(0, 1.68, 5), uv_scale=(1, 1),
                                       close_profile=True)
                S.cap_open_boundaries(post)
                S.assign_material(post, timber)
                objs.append(post)
                step += TIMBER_POST_PITCH
            k += 1

    print(f"[sakura]   {len(objs)} timber barrier parts")
    S.export_glb(objs, f"{name}.glb")
    return objs


# ----------------------------------------------------------------- lake village

# Fujikawaguchiko's lakeshore is legally constrained, and those rules are exactly what make it
# look the way it does: Yamanashi's landscape ordinance caps lakeshore buildings at 16 m, allows
# no more than three colours per building, and holds the dominant colour to Munsell chroma <= 6.
# So: a uniform ribbon of 1-2 storey, low-chroma buildings, with a handful of taller ryokan.
ROOF_COLOURS = [
    (0.29, 0.32, 0.35, 1.0),   # kawara blue-grey
    (0.23, 0.20, 0.19, 1.0),   # galvalume charcoal
    (0.42, 0.23, 0.20, 1.0),   # oxidised red, the older roofs
]
WALL_COLOURS = [
    (0.85, 0.82, 0.75, 1.0),   # cream stucco
    (0.54, 0.45, 0.34, 1.0),   # unpainted cedar
    (0.66, 0.65, 0.62, 1.0),   # grey siding
]

VILLAGE_X = 198.0          # legacy anchor, kept for the lake-torii sightline maths
VILLAGE_Z0, VILLAGE_Z1 = -70.0, 250.0
# The town is now placed against the S1 lakeshore road rather than at a fixed x: the expansion
# runs the Kawabe Lakeshore Return straight down this shore, so a town pinned to lake level on
# its own private bench ended up 26 m below the carriageway and buried inside the new terrain.
VILLAGE_ROW_OFFSETS = (24.0, 40.0)     # metres to the rider's RIGHT of the road = lakeward.
                                       # At the first-pass 12 m a 14 m-deep ryokan reached to
                                       # within a metre of the shoulder and read as a grey wall
                                       # running down the carriageway; 24 m leaves room for the
                                       # verge, the guardrail, the roadside dressing and a strip
                                       # of frontage, so the town reads as a place beside the
                                       # road rather than a fence along it.
VILLAGE_SPACING = (15.0, 21.0)         # metres of arc between frontages
VILLAGE_MAX_SPREAD = 2.6               # reject a plot whose footprint is not flat enough


def _quad_strip(name, corners, mat):
    """A flat ribbon of quads, used for the façade bands. `corners` is a list of 4-vert tuples."""
    verts, faces, uvs = [], [], []
    for quad in corners:
        base = len(verts)
        verts.extend(quad)
        faces.append((base, base + 1, base + 2, base + 3))
        uvs.extend([(0, 0), (1, 0), (1, 1), (0, 1)])
    obj = S.mesh_from_arrays(name, [S.u2b(*v) for v in verts], faces, uvs=uvs, smooth=False)
    S.assign_material(obj, mat)
    return obj


def _gable_house(name, w, d, wall_h, pitch_deg, eave, mats):
    """
    One modular house: extruded footprint, gabled roof with deep eaves, plus a façade band set.

    The original was authored to be seen from 200 m across a lake, where the silhouette and the
    roof plane are the entire read - so the walls were a single untextured extrusion. The Kawabe
    lakeshore road now runs past the frontage at 4 m, and at that range a bare extrusion is a
    blank grey slab. The bands below (sill course, window ribbon per storey, eave shadow) cost a
    handful of quads each and are what stop it reading as cardboard.
    """
    ridge = math.tan(math.radians(pitch_deg)) * (w * 0.5)
    hw, hd = w * 0.5, d * 0.5
    ew, ed = hw + eave, hd + eave

    verts_u = [
        (-hw, 0.0, -hd), (hw, 0.0, -hd), (hw, 0.0, hd), (-hw, 0.0, hd),
        (-hw, wall_h, -hd), (hw, wall_h, -hd), (hw, wall_h, hd), (-hw, wall_h, hd),
    ]
    faces = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    uvs = []
    for f in faces:
        uvs.extend([(0, 0), (1, 0), (1, 1), (0, 1)])
    body = S.mesh_from_arrays(f"{name}_Body", [S.u2b(*v) for v in verts_u], faces,
                              uvs=uvs, smooth=False)
    S.assign_material(body, mats[0])
    out = [body]

    # --- façade bands, proud of the wall so they never z-fight -----------------------
    o = 0.045
    def ring(y0, y1):
        return [
            [(-hw - o, y0, -hd - o), (hw + o, y0, -hd - o),
             (hw + o, y1, -hd - o), (-hw - o, y1, -hd - o)],
            [(hw + o, y0, hd + o), (-hw - o, y0, hd + o),
             (-hw - o, y1, hd + o), (hw + o, y1, hd + o)],
            [(hw + o, y0, -hd - o), (hw + o, y0, hd + o),
             (hw + o, y1, hd + o), (hw + o, y1, -hd - o)],
            [(-hw - o, y0, hd + o), (-hw - o, y0, -hd - o),
             (-hw - o, y1, -hd - o), (-hw - o, y1, hd + o)],
        ]

    # Sill course at the base, and a shadow band tucked under the eaves.
    out.append(_quad_strip(f"{name}_Sill", ring(0.0, min(0.85, wall_h * 0.22)), mats[2]))
    out.append(_quad_strip(f"{name}_Eave", ring(wall_h - 0.42, wall_h), mats[2]))

    # One window ribbon per storey on the two long (eave) faces only - the gable ends of a
    # Japanese townhouse are mostly blank, which is exactly the contrast that makes the
    # frontage read as a frontage.
    storeys = 2 if wall_h > 5.4 else 1
    for s in range(storeys):
        y0 = 1.25 + s * (wall_h - 1.9) / max(1, storeys)
        y1 = min(wall_h - 0.6, y0 + 1.25)
        if y1 <= y0 + 0.2:
            continue
        out.append(_quad_strip(
            f"{name}_Win{s}",
            [[(-hw * 0.82, y0, -hd - o), (hw * 0.82, y0, -hd - o),
              (hw * 0.82, y1, -hd - o), (-hw * 0.82, y1, -hd - o)],
             [(hw * 0.82, y0, hd + o), (-hw * 0.82, y0, hd + o),
              (-hw * 0.82, y1, hd + o), (hw * 0.82, y1, hd + o)]],
            mats[3]))

    # Roof: two slopes meeting at a ridge, overhanging on all four sides.
    rv = [
        (-ew, wall_h, -ed), (ew, wall_h, -ed), (ew, wall_h, ed), (-ew, wall_h, ed),
        (0.0, wall_h + ridge, -ed), (0.0, wall_h + ridge, ed),
    ]
    rf = [(0, 1, 4), (1, 2, 5, 4), (2, 3, 5), (3, 0, 4, 5)]
    ruv = []
    for f in rf:
        ruv.extend([(0, 0), (1, 0), (1, 1), (0, 1)][:len(f)])
    roof = S.mesh_from_arrays(f"{name}_Roof", [S.u2b(*v) for v in rv], rf, uvs=ruv, smooth=False)
    S.assign_material(roof, mats[1])
    out.append(roof)
    return out


def _village_ground(x, z):
    """
    Terrain height under a village plot, from the same heightfield the tiles are built from.

    Sampling the real function rather than raycasting means the town is seated exactly on the
    ground Unity will import, with no dependence on collider state or import order.
    """
    import build_terrain as T
    X = np.array([[float(x)]])
    Z = np.array([[float(z)]])
    return float(T.terrain_height(X, Z)[0, 0])


def _village_plot(x, z, radius=5.0):
    """Nine probes: returns (lowest, spread) so a lumpy plot can be rejected rather than floated."""
    import build_terrain as T
    ang = np.linspace(0.0, 2.0 * np.pi, 8, endpoint=False)
    xs = np.concatenate([[x], x + np.cos(ang) * radius])
    zs = np.concatenate([[z], z + np.sin(ang) * radius])
    ys = T.terrain_height(xs.reshape(1, -1), zs.reshape(1, -1))[0]
    return float(ys.min()), float(ys.max() - ys.min())


def build_village(name="SakuraPass_Lake_Village"):
    print(f"[sakura] building {name}...")
    rng = random.Random(45217)
    objs = []

    roof_mats = [S.pbr_material(f"{name}_Roof_{i}", base_color=c, roughness=0.78)
                 for i, c in enumerate(ROOF_COLOURS)]
    wall_mats = [S.pbr_material(f"{name}_Wall_{i}", base_color=c, roughness=0.82)
                 for i, c in enumerate(WALL_COLOURS)]
    # Dark stained timber for the sill course and the eave shadow band, and a deep slate for the
    # window ribbons. Both are deliberately much darker than any wall colour: at ride speed the
    # contrast between band and wall is the whole read.
    trim_mat = S.pbr_material(f"{name}_Trim", base_color=(0.19, 0.15, 0.12, 1.0), roughness=0.85)
    glass_mat = S.pbr_material(f"{name}_Glass", base_color=(0.13, 0.17, 0.21, 1.0),
                               roughness=0.28, metallic=0.15)

    # --- frontage along the Kawabe Lakeshore Return --------------------------------
    # The town is the aid stop on the circuit's recovery half, so it is laid out the way a real
    # lakeside town is: a frontage ribbon facing the road, a looser second row behind it, both
    # on the lake side where the terrain falls away gently to the water.
    p, tangents, sides, ups, banks, arc = R.segment_frames("s1", spacing=2.0)
    shore = [i for i in range(len(p))
             if p[i][0] > 150.0 and VILLAGE_Z0 - 40.0 < p[i][2] < VILLAGE_Z1 + 40.0]
    if not shore:
        raise RuntimeError("no lakeshore samples found for the village frontage")
    i0, i1 = min(shore), max(shore)
    print(f"[sakura]   frontage from arc {arc[i0]:.0f} m to {arc[i1]:.0f} m "
          f"(z {p[i1][2]:.0f} .. {p[i0][2]:.0f})")

    idx = 0
    rejected = 0
    next_arc = arc[i0]
    for i in range(i0, i1 + 1):
        if arc[i] < next_arc:
            continue
        next_arc = arc[i] + rng.uniform(*VILLAGE_SPACING)
        yaw = math.degrees(math.atan2(tangents[i][0], tangents[i][2]))

        for row, xoff in enumerate(VILLAGE_ROW_OFFSETS):
            big = row == 0
            if row and rng.random() < 0.34:
                continue
            # Yamanashi's lakeshore ordinance caps buildings at 16 m, but that cap was written
            # for a town seen across water. Ridden past at 4 m, a 15 m ryokan on the verge is a
            # cliff: the frontage is held to two storeys with the occasional three-storey inn.
            if big and rng.random() < 0.18:
                w, d, h = rng.uniform(12, 17), rng.uniform(8, 11), rng.uniform(8.5, 11.0)
            elif big:
                w, d, h = rng.uniform(8, 12), rng.uniform(6, 9), rng.uniform(5.6, 7.4)
            else:
                w, d, h = rng.uniform(6, 10), rng.uniform(6, 9), rng.uniform(5.0, 7.0)

            jitter = rng.uniform(-2.6, 2.6)
            cx = p[i][0] + sides[i][0] * (xoff + jitter)
            cz = p[i][2] + sides[i][2] * (xoff + jitter)
            lowest, spread = _village_plot(cx, cz, radius=max(w, d) * 0.55)
            if spread > VILLAGE_MAX_SPREAD:
                rejected += 1
                continue

            house = _gable_house(
                f"{name}_H{idx}", w, d, h, rng.uniform(26.0, 31.0), rng.uniform(0.6, 0.9),
                (rng.choice(wall_mats), rng.choice(roof_mats), trim_mat, glass_mat))
            # Square the frontage to the road: a town whose roofs all face the same compass
            # direction regardless of the street reads as a tilemap, not as a place.
            ca = math.cos(math.radians(yaw))
            sa = math.sin(math.radians(yaw))
            for o in house:
                for v in o.data.vertices:
                    # Vertices were authored about the origin; rotate, then shift into place.
                    ux, uy, uz = -v.co.x, v.co.z, -v.co.y
                    rx = ux * ca + uz * sa
                    rz = -ux * sa + uz * ca
                    v.co = S.u2b(rx + cx, uy + lowest - 0.35, rz + cz)
                objs.append(o)
            idx += 1

    print(f"[sakura]   {idx} buildings on the lakeshore frontage "
          f"({rejected} plots rejected as too uneven)")
    S.export_glb(objs, f"{name}.glb")
    return objs


# ------------------------------------------------------------------- lake torii

def build_lake_torii(name="SakuraPass_Lake_Torii"):
    """
    The torii standing in the water from the board's 'Lakes & Reflections' tile - an Itsukushima-
    style 両部鳥居, which is the form that actually stands in water: the main pillars are braced
    by smaller subsidiary posts (稚児柱) fore and aft.

    Everything is authored against a local origin of y=0 == waterline, so the Unity side only has
    to drop the object at the lake level. The member set is the full 明神鳥居 stack, because the
    silhouette is what makes it read as a torii rather than a goalpost:

        笠木 kasagi     the curved top lintel, sweeping up at the tips (反り)
        島木 shimaki    the squarer beam immediately beneath it - the pair is what distinguishes
                        a 明神 torii from the plain single-lintel 神明 form
        貫   nuki       the lower crossbeam, passing *through* the pillars and protruding
        楔   kusabi     the wedges locking the nuki where it emerges from each pillar
        額束 gakuzuka   the central tab between nuki and shimaki

    Pillars carry 転び (batter): they lean inward roughly 1.7% of their height, which is what
    stops a torii looking like scaffolding.
    """
    print(f"[sakura] building {name}...")
    objs = []
    red = P.vermilion_material()

    SPAN = 11.0          # pillar centre-to-centre at the waterline
    PH = 13.0            # pillar height above water
    FOOT = -1.6          # pillars continue below the surface
    PR = 0.62            # pillar radius at the foot
    BATTER = 0.017       # inward lean as a fraction of height
    NUKI_Y = 9.30

    half = SPAN * 0.5

    for sgn in (-1.0, 1.0):
        pillar = S.lathe(f"{name}_Pillar_{int(sgn)}",
                         [(PR * 1.12, FOOT), (PR, 0.0), (PR * 0.94, PH * 0.5), (PR * 0.84, PH)],
                         segments=16, uv_scale=(1.0, 3.0))
        for v in pillar.data.vertices:
            ux, uy, uz = -v.co.x, v.co.z, -v.co.y
            # Lean the pillar in toward the centreline as it rises.
            v.co = S.u2b(ux + sgn * (half - max(0.0, uy) * BATTER), uy, uz)
        S.assign_material(pillar, red)
        objs.append(pillar)

        # 稚児柱 bracing posts fore and aft, raked inward toward the main pillar.
        for zs in (-2.7, 2.7):
            leg = S.lathe(f"{name}_Brace_{int(sgn)}_{int(zs)}",
                          [(0.34, FOOT), (0.30, 0.0), (0.24, 7.4)], segments=12, uv_scale=(1.0, 2.0))
            bx = half + 1.9
            for v in leg.data.vertices:
                ux, uy, uz = -v.co.x, v.co.z, -v.co.y
                t = max(0.0, uy) / 7.4
                v.co = S.u2b(ux + sgn * (bx - t * 0.9), uy, uz + zs * (1.0 - t * 0.16))
            S.assign_material(leg, red)
            objs.append(leg)

            # Tie beam locking the brace back to the main pillar.
            tie = P.box(f"{name}_Tie_{int(sgn)}_{int(zs)}", (2.3, 0.30, 0.28),
                        centre=(sgn * (half + 0.52), 6.60, zs * 0.90), bevel=0.02)
            S.assign_material(tie, red)
            objs.append(tie)

        # 楔 - the wedge pinning the nuki where it leaves the pillar.
        kusabi = P.box(f"{name}_Kusabi_{int(sgn)}", (0.34, 0.96, 0.64),
                       centre=(sgn * (half - NUKI_Y * BATTER), NUKI_Y, 0.0), bevel=0.02)
        S.assign_material(kusabi, red)
        objs.append(kusabi)

    # 貫 - runs through both pillars and protrudes ~1.4 m beyond each.
    nuki = P.box(f"{name}_Nuki", (SPAN + 2.8, 0.56, 0.50), centre=(0.0, NUKI_Y, 0.0), bevel=0.03)
    S.assign_material(nuki, red)
    objs.append(nuki)

    # 島木 and 笠木 share a camber and end lift so the upper one nests cleanly on the lower.
    RISE, LIFT = 0.30, 0.58
    shimaki = P.curved_beam(f"{name}_Shimaki", SPAN + 5.4, RISE, 1.22, 0.66,
                            segments=32, end_lift=LIFT)
    for v in shimaki.data.vertices:
        v.co = (v.co.x, v.co.y, v.co.z + 12.90)
    S.assign_material(shimaki, red)
    objs.append(shimaki)

    kasagi = P.curved_beam(f"{name}_Kasagi", SPAN + 6.1, RISE, 1.40, 0.46,
                           segments=32, end_lift=LIFT)
    for v in kasagi.data.vertices:
        v.co = (v.co.x, v.co.y, v.co.z + 13.46)
    S.assign_material(kasagi, red)
    objs.append(kasagi)

    # 額束 - the central tab bridging nuki to shimaki.
    gak_bot = NUKI_Y + 0.28
    gak_top = 12.90 - 0.33 + RISE
    gakuzuka = P.box(f"{name}_Gakuzuka", (0.98, gak_top - gak_bot, 0.60),
                     centre=(0.0, (gak_bot + gak_top) * 0.5, 0.0), bevel=0.02)
    S.assign_material(gakuzuka, red)
    objs.append(gakuzuka)

    S.export_glb(objs, f"{name}.glb")
    return objs


# ---------------------------------------------------------------- hillside sign

def _text_mesh(body, size, extrude):
    """Blender's font system gives real letterforms; convert to mesh so the GLB carries geometry."""
    cur = bpy.data.curves.new("tmp_font", type='FONT')
    cur.body = body
    cur.size = size
    cur.extrude = extrude
    cur.align_x = 'CENTER'
    cur.align_y = 'CENTER'
    ob = bpy.data.objects.new("tmp_text", cur)
    S.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.convert(target='MESH')
    verts = [tuple(v.co) for v in ob.data.vertices]
    faces = [tuple(pl.vertices) for pl in ob.data.polygons]
    bpy.data.objects.remove(ob, do_unlink=True)
    return verts, faces


def build_hillside_sign(name="SakuraPass_Hillside_Sign"):
    """
    The SAKURA PASS lettering standing on the hillside above the hairpin (concept panel 06).

    Letters are free-standing plates on the slope facing back down the pass, so they read from
    the approach the way the board draws them.
    """
    print(f"[sakura] building {name}...")
    verts_f, faces = _text_mesh("SAKURA PASS", size=5.4, extrude=0.34)

    # Font space is (x right, y up, z depth); stand it up facing -Z down the valley.
    verts = []
    for (fx, fy, fz) in verts_f:
        verts.append(S.u2b(fx, fy + 4.2, -fz))
    sign = S.mesh_from_arrays(f"{name}_Text", verts, faces, smooth=False)
    S.assign_material(sign, S.pbr_material(f"{name}_Face", base_color=(0.93, 0.92, 0.90, 1.0),
                                           roughness=0.55))
    objs = [sign]

    # A low plinth wall so the letters sit on something rather than sprouting from grass.
    plinth = P.box(f"{name}_Plinth", (38.0, 1.1, 1.3), centre=(0.0, 0.55, 0.0), bevel=0.06)
    S.assign_material(plinth, P.stone_material(f"{name}_Plinth_Mat"))
    objs.append(plinth)

    S.export_glb(objs, f"{name}.glb")
    return objs


# ------------------------------------------------------------------ petal drifts

def build_petal_drifts(name="SakuraPass_Petal_Drifts"):
    """
    Fallen-petal accumulation along both kerbs.

    Modelled as a shallow swept ribbon rather than a decal, because accumulation is a *volume* -
    a decal has no silhouette and reads as a stain. Width and height are driven by 1D noise along
    arc length so the drift thins and thickens, and it hugs the kerb, which is where wind-blown
    litter actually collects. The centre of the carriageway is deliberately left bare: clean wheel
    tracks are what tell the player this is a road cars still use.
    """
    print(f"[sakura] building {name}...")
    p, sides, ups, arc, _, _ = RD.road_frames(spacing=1.2)
    n = len(p)

    mat = S.pbr_material("SakuraPass_PetalDrift", base_color=(0.95, 0.72, 0.80, 1.0),
                         roughness=0.86)
    t = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(t, "Sakura_Blossom_Atlas.png")), "Base Color")

    objs = []
    for sgn, tag in ((1.0, "R"), (-1.0, "L")):
        verts, faces, uvs = [], [], []
        for i in range(n):
            d = arc[i]
            amp = (math.sin(d * 0.07 + sgn) * 0.5 + math.sin(d * 0.23) * 0.3 +
                   math.sin(d * 0.011) * 0.2)
            amp = max(0.0, 0.55 + amp * 0.45)
            width = 0.16 + amp * 0.46
            tall = 0.010 + amp * 0.030
            inner = RD.HALF - 0.05
            o, s, u = p[i], sides[i], ups[i]
            for k, (a_off, h) in enumerate(((0.0, 0.0), (width * 0.5, tall), (width, 0.0))):
                q = o + s * (sgn * (inner - a_off)) + u * (RD.camber(inner) + h)
                verts.append(S.u2b(q[0], q[1], q[2]))
        for i in range(n - 1):
            for k in range(2):
                a = i * 3 + k
                faces.append((a, a + 1, a + 4, a + 3))
                v0, v1 = arc[i] * 0.6, arc[i + 1] * 0.6
                uvs.extend([(k * 0.5, v0), ((k + 1) * 0.5, v0),
                            ((k + 1) * 0.5, v1), (k * 0.5, v1)])
        drift = S.mesh_from_arrays(f"{name}_{tag}", verts, faces, uvs=uvs)
        S.assign_material(drift, mat)
        objs.append(drift)

    S.export_glb(objs, f"{name}.glb")
    return objs


def main():
    for fn in (build_timber_guardrail, build_village, build_lake_torii,
               build_hillside_sign, build_petal_drifts):
        S.reset_scene()
        fn()
    print("[sakura] expansion build complete.")


if __name__ == "__main__":
    main()

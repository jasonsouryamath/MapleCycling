"""
Sculpted Sakura Pass landscape.

Builds two assets:

* ``SakuraPass_Valley_Terrain_HD.glb`` - the playable mountainside. A heightfield carved along the
  route spline so the road always sits on a believable shelf, with the valley draining to the lake
  on the rider's right and a cut cliff rising inland on the left. Splat weights (grass / rock /
  scree) are written to a second UV channel so Unity can blend three tiling PBR sets across it.

* ``SakuraPass_Distant_Ranges.glb`` - three concentric mountain rings with real silhouettes and
  snow-line colouring, replacing the flat photo backdrop plate that was visibly rectangular.

Run via:  blender -b -P build_terrain.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

import sakura_lib as S
import sakura_route as R


# --- cross-section constants (Unity metres) ---------------------------------------
ROAD_CORRIDOR = 7.4        # flat verge either side of the centreline; wide enough that the
                           # asphalt slab's buried skirt is always covered by terrain
CARVE_BLEND = 3.5          # short verge-to-batter transition
RELIEF_RAMP = 62.0         # relief eases in over this distance so slopes never go vertical
VALLEY_RANGE = 105.0
CLIFF_RANGE = 82.0
CLIFF_HEIGHT = 54.0
LAKE_LEVEL = -44.0

# --- extents ----------------------------------------------------------------------
# Sized around the whole staged network, not just the pass: the Kawabe lakeshore return runs
# out to x = 218 / z = 906, the Aozora switchback field to x = -424, and the Maple City road
# south to z = -716.  Generous margins keep the terrain batter and the lakeshore extending
# beyond every rider-facing camera.  Tile size is held at ~96 x 88 m - the streaming unit -
# so the tile count grows with the world instead of the tiles growing with it.
MIN_X, MAX_X = -490.0, 280.0
MIN_Z, MAX_Z = -780.0, 970.0
STEP = 2.5
TILES_X, TILES_Z = 8, 20


def terrain_height(X, Z):
    """
    Vectorised heightfield for the whole landscape, in Unity space.

    Cross-section, from the centreline outward:
      0 .. 7.4 m    flat verge, 0.20 m below the asphalt so the road slab seats on it
      7.4 m ..      a batter that starts steep and eases off - a fill slope falling to the lake on
                    the rider's right, a cut face climbing into the massif on the left
    Relief noise ramps in over RELIEF_RAMP so it can never create a vertical wall beside the road.
    """
    dist, road_y, side = R.distance_field(X, Z, spacing=2.0)
    d = np.maximum(0.0, dist - ROAD_CORRIDOR)

    # ``1 - (1 - t)^2`` leaves the verge at a realistic batter angle rather than a cliff edge.
    def batter(t):
        t = np.clip(t, 0.0, 1.0)
        return 1.0 - (1.0 - t) ** 2

    # --- valley side: fill slope draining to (and past) the lake surface ------------
    drop = LAKE_LEVEL - road_y
    valley = road_y + drop * batter(d / VALLEY_RANGE)
    valley -= np.maximum(0.0, d - VALLEY_RANGE) * 0.14

    # --- inland side: cut face rising into the massif ------------------------------
    cliff = road_y + CLIFF_HEIGHT * batter(d / CLIFF_RANGE)
    cliff += np.maximum(0.0, d - CLIFF_RANGE) * 0.58

    # ``side`` is continuous, so sharpen it into a blend factor that is saturated a few metres
    # off the centreline but still crosses over smoothly through the medial axis.
    vt = np.clip(side * 2.2, -1.0, 1.0) * 0.5 + 0.5
    vt = vt * vt * (3 - 2 * vt)
    base = cliff * (1.0 - vt) + valley * vt
    # --- hold the verge flat, then release into the batter over a short distance ----
    verge = np.clip(d / CARVE_BLEND, 0, 1)
    verge = verge * verge * (3 - 2 * verge)
    base = (road_y - 0.20) * (1.0 - verge) + base * verge

    # --- relief ---------------------------------------------------------------------
    broad = (S.fbm_2d(X, Z, 1.0 / 150.0, octaves=5, seed=1301) - 0.5) * 2.0
    medium = (S.fbm_2d(X, Z, 1.0 / 46.0, octaves=4, seed=1361) - 0.5) * 2.0
    fine = (S.fbm_2d(X, Z, 1.0 / 12.0, octaves=3, seed=1409) - 0.5) * 2.0
    ridge = S.ridged_2d(X, Z, 1.0 / 80.0, octaves=5, seed=1487)

    relief = broad * 15.0 + medium * 4.6 + fine * 1.0
    # Ridged crests and gullies only on the inland massif, and only once it has some height.
    tc = np.clip(d / CLIFF_RANGE, 0, 1)
    relief += (ridge - 0.35) * 20.0 * tc * (1.0 - vt)
    # Old landslip benches terrace the valley flank.
    tv = np.clip(d / VALLEY_RANGE, 0, 1)
    relief += np.sin(tv * np.pi * 3.0) * 2.8 * tv * vt

    # The key fix: relief fades in slowly, so it perturbs the batter instead of replacing it.
    mask = np.clip(d / RELIEF_RAMP, 0, 1)
    mask = mask * mask * (3 - 2 * mask)

    return base + relief * mask


def splat_weights(height, X, Z):
    """
    Per-vertex layer weights.

    Section 29 asks for terrain that blends more than one ground type, with steeper slopes
    exposing more rock and soil and less perfect grass. Four weights come out of here and grass
    is whatever is left:

        rock    bare cliff faces
        scree   loose material at the waterline and on mid-steep lower slopes
        soil    forest soil + leaf litter, the band between lawn and cliff
        petal   sakura petal accumulation on sheltered near-flat ground

    All thresholds are PROVISIONAL tuning.
    """
    gy, gx = np.gradient(height, STEP, STEP)
    slope = np.sqrt(gx * gx + gy * gy)           # rise over run

    # Bare rock only on genuinely cliff-like faces. The band used to open at slope 0.55
    # (29 deg), which painted near-black strata over every road-cut embankment on the lakeshore
    # return and read in-game as out-of-place grey shadows. Japanese highway cut slopes at these
    # grades are hydroseeded and green (MLIT practice), so the band now opens at ~46 deg and
    # only saturates on ~67 deg faces. PROVISIONAL tuning.
    ROCK_SLOPE_START = 1.05      # rise/run, ~46.4 deg
    ROCK_SLOPE_FULL = 2.40       # rise/run, ~67.4 deg
    rock = np.clip((slope - ROCK_SLOPE_START) / (ROCK_SLOPE_FULL - ROCK_SLOPE_START), 0, 1)
    rock = rock ** 0.75

    # Loose material collects at the waterline and on genuinely unstable upper slopes. The
    # mid-slope band used to open at 0.28 (15.6 deg), which overlapped the whole soil band and
    # turned 37.7% of the valley into gravel. Scree is now what it should be - shoreline wash
    # and talus below the cliffs - and the wooded middle belongs to soil. PROVISIONAL tuning.
    near_water = np.clip((LAKE_LEVEL + 7.0 - height) / 9.0, 0, 1)
    mid_slope = np.clip((slope - 0.80) / 0.5, 0, 1) * (1.0 - rock)
    scree = np.clip(np.maximum(near_water, mid_slope * 0.45), 0, 1)

    # Forest soil / leaf litter. This is the missing middle: below the rock band grass used to
    # run all the way to the cliff, so every embankment read as a single flat green sheet. Humus
    # accumulates on wooded slopes that are too steep to hold a closed sward but not steep
    # enough to strip to rock - roughly 17 to 46 degrees - and is broken up by a broad patch
    # mask so it drifts rather than following a contour line.
    SOIL_SLOPE_START = 0.30      # rise/run, ~16.7 deg
    SOIL_SLOPE_FULL = 1.05       # rise/run, ~46.4 deg, where the rock band takes over
    soil = np.clip((slope - SOIL_SLOPE_START) / (SOIL_SLOPE_FULL - SOIL_SLOPE_START), 0, 1)
    soil = soil ** 0.85
    drift = norm01_fbm(X, Z, 1.0 / 34.0, seed=1667)
    soil *= np.clip(0.45 + drift * 1.1, 0, 1)

    # Sakura petal accumulation. Petals settle on sheltered, near-flat ground and blow into
    # drifts, so this is the inverse of slope shaped by a coarse wind-drift mask. It is a tint
    # overlay in the shader rather than a texture layer, which is why it can afford to cover a
    # lot of ground cheaply.
    PETAL_SLOPE_END = 0.34       # rise/run, ~18.8 deg - nothing settles above this
    petal = np.clip(1.0 - slope / PETAL_SLOPE_END, 0, 1) ** 1.3
    gust = norm01_fbm(X, Z, 1.0 / 23.0, seed=1733)
    petal *= np.clip(gust * 1.9 - 0.55, 0, 1)

    # Break the transitions up so the blend is not a clean contour line. The jitter is gated by
    # how much of the layer is already present: it used to be added unconditionally, so noise
    # alone pushed up to +0.27 rock onto dead-flat grass.
    jitter = (S.fbm_2d(X, Z, 1.0 / 6.0, octaves=3, seed=1601) - 0.5) * 0.55
    rock = np.clip(rock + jitter * np.clip(rock * 3.0, 0, 1), 0, 1)
    scree = np.clip(scree + jitter * 0.7 * np.clip(scree * 3.0, 0, 1), 0, 1)
    soil = np.clip(soil + jitter * 0.8 * np.clip(soil * 3.0, 0, 1), 0, 1)
    scree = np.minimum(scree, 1.0 - rock)
    # Rock and scree are exposed mineral surfaces; litter does not sit on them.
    soil = np.minimum(soil, 1.0 - np.clip(rock + scree, 0, 1))
    # Petals land on top of whatever is there, but not on a cliff face.
    petal = np.clip(petal * (1.0 - rock), 0, 1)

    return rock, scree, soil, petal


def norm01_fbm(X, Z, freq, seed):
    """fbm in 0..1 - the library helper already returns that range, kept for readability."""
    return np.clip(S.fbm_2d(X, Z, freq, octaves=4, seed=seed), 0.0, 1.0)


def build_terrain():
    print("[sakura] sculpting valley terrain...")
    nx = int(round((MAX_X - MIN_X) / STEP))
    nz = int(round((MAX_Z - MIN_Z) / STEP))

    xs = MIN_X + np.arange(nx + 1) * STEP
    zs = MIN_Z + np.arange(nz + 1) * STEP
    X, Z = np.meshgrid(xs, zs, indexing='ij')

    Y = terrain_height(X, Z)
    rock, scree, soil, petal = splat_weights(Y, X, Z)

    print(f"[sakura]   heightfield {nx + 1} x {nz + 1} "
          f"(y range {Y.min():.1f} .. {Y.max():.1f} m)")
    # Section 29 wants measurable ground-cover variety, not a single green sheet. Report the
    # mean coverage of each layer so the mix can be judged from the build log instead of from
    # a render, the same way the flora composition counter works.
    grass = np.clip(1.0 - rock - scree - soil, 0, 1)
    print(f"[sakura]   ground cover: grass {grass.mean() * 100:.1f}%  soil/litter "
          f"{soil.mean() * 100:.1f}%  rock {rock.mean() * 100:.1f}%  scree "
          f"{scree.mean() * 100:.1f}%  (petal dusting over {petal.mean() * 100:.1f}%)")

    objs = []
    mat = S.pbr_material("SakuraPass_Terrain", base_color=(0.32, 0.40, 0.24, 1.0), roughness=0.88)
    tex_dir = S.textures_dir()
    S.set_texture(mat, S.load_image(os.path.join(tex_dir, "Sakura_Grass_Albedo.png")), "Base Color")
    S.set_texture(mat, S.load_image(os.path.join(tex_dir, "Sakura_Grass_Rough.png"), True), "Roughness")
    S.set_texture(mat, S.load_image(os.path.join(tex_dir, "Sakura_Grass_Normal.png"), True), "Normal")

    for tx in range(TILES_X):
        for tz in range(TILES_Z):
            i0 = tx * nx // TILES_X
            i1 = (tx + 1) * nx // TILES_X
            j0 = tz * nz // TILES_Z
            j1 = (tz + 1) * nz // TILES_Z
            wi, wj = i1 - i0 + 1, j1 - j0 + 1
            if wi < 2 or wj < 2:
                continue

            verts = []
            for j in range(j0, j1 + 1):
                for i in range(i0, i1 + 1):
                    # Unity -> Blender axis swap happens here, once.
                    verts.append(S.u2b(X[i, j], Y[i, j], Z[i, j]))

            faces = []
            uvs = []
            uv2 = []
            uv3 = []
            for j in range(wj - 1):
                for i in range(wi - 1):
                    a = j * wi + i
                    b = a + 1
                    c = a + wi
                    d = c + 1
                    # Wound counter-clockwise as seen from above in Unity space, so the tiles
                    # face up. Wound the other way the terrain exports inside-out: backface
                    # culling hides it and downward raycasts pass straight through it.
                    faces.append((a, b, d, c))
                    for (vi, vj) in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)):
                        gi, gj = i0 + vi, j0 + vj
                        uvs.append((X[gi, gj] * 0.062, Z[gi, gj] * 0.062))
                        uv2.append((float(rock[gi, gj]), float(scree[gi, gj])))
                        uv3.append((float(soil[gi, gj]), float(petal[gi, gj])))

            obj = S.mesh_from_arrays(f"Terrain_{tx:02d}_{tz:02d}", verts, faces,
                                     uvs=uvs, uv2=uv2, uv3=uv3)
            S.assign_material(obj, mat)
            objs.append(obj)

    print(f"[sakura]   {len(objs)} terrain tiles")
    S.export_glb(objs, "SakuraPass_Valley_Terrain_HD.glb")
    for o in objs:
        S.report(o.name, o)
    return objs


# ------------------------------------------------------------------ distant ranges

def build_distant_ranges():
    """Three silhouette rings with genuine peak/saddle structure and a snow line."""
    print("[sakura] building distant mountain ranges...")
    objs = []

    # Rings are pushed well outside the hero volcano's footprint.  They used to sit at
    # 620 / 900 / 1250 m around (-20, 300), which was fine when the route ended at z = 306 - but
    # the Fuji approach carries the rider out to (-214, 766), only ~540 m from that centre, so
    # the innermost ring's wobbled radius came within a few metres of the carriageway.  A
    # backdrop the rider can ride into is not a backdrop.
    #
    # The centre is the new world centroid and every radius clears Fuji's outer edge (967 m from
    # the centre plus a 700 m base = 1667 m), so no ring can cut across the volcano's flank.
    # Heights are scaled to hold the rings at ~11 deg of apparent elevation, i.e. under half
    # Fuji's 24 deg, which is what keeps the volcano reading as the hero rather than as one more
    # ridge in a stack.
    ring_cx, ring_cz = -60.0, 300.0
    layers = [
        # A fourth, nearer ring was added when the pass grew its switchback descent: from the
        # crest the eye now travels road -> lake -> *foothills* -> ranges -> volcano, and without
        # this layer there was a 1.4 km hole in that sequence where the valley simply ended.
        # Kept low (190 m ~ 7 deg) so it reads as foothills behind the lake, not as a wall.
        # The expansion pushed the Maple City road out to (-298, -716), 1 079 m from this
        # centre, so the innermost ring was radius-wobbling to within ~150 m of a road the
        # rider actually uses. Pushed out to 1 700 m (min wobble 1 496 m) and scaled in height
        # by the same factor, which holds its apparent elevation at ~7 deg.
        dict(radius=1700.0, height=230.0, seed=2039, segs=320, color=(0.50, 0.58, 0.74, 1.0), snow=0.0),
        dict(radius=2200.0, height=330.0, seed=2101, segs=300, color=(0.62, 0.68, 0.82, 1.0), snow=0.0),
        dict(radius=2900.0, height=460.0, seed=2203, segs=280, color=(0.78, 0.80, 0.92, 1.0), snow=0.72),
        dict(radius=3700.0, height=590.0, seed=2309, segs=260, color=(0.92, 0.90, 0.98, 1.0), snow=0.62),
    ]

    for li, L in enumerate(layers):
        segs = L["segs"]
        ang = np.linspace(0, 2 * np.pi, segs, endpoint=False)

        # Two noise bands: broad massifs plus sharper summits.
        cx = np.cos(ang)
        cz = np.sin(ang)
        broad = S.fbm_2d(cx * 2.2, cz * 2.2, 1.0, octaves=4, seed=L["seed"])
        sharp = S.ridged_2d(cx * 6.5, cz * 6.5, 1.0, octaves=4, seed=L["seed"] + 17)
        profile = 0.30 + broad * 0.85 + sharp * 0.38
        profile = profile / profile.max()

        # Radius wobble stops the ring reading as a cylinder.
        radius = L["radius"] * (0.88 + 0.24 * S.fbm_2d(cx * 1.4, cz * 1.4, 1.0,
                                                       octaves=3, seed=L["seed"] + 51))

        rows = 24
        verts = []
        for s in range(segs):
            top = L["height"] * profile[s]
            for r in range(rows):
                t = r / (rows - 1)
                # Concave flank profile: steeper near the summit, splayed at the base.
                y = -110.0 + (top + 110.0) * (t ** 1.55)
                # Ridges pinch inward as they rise.
                rr = radius[s] * (1.0 + 0.06 * (1.0 - t))
                verts.append(S.u2b(ring_cx + cx[s] * rr, y, ring_cz + cz[s] * rr))

        snow_line = L["height"] * L["snow"] if L["snow"] > 0 else 1e9
        faces = []
        face_uvs = []
        y_lo, y_hi = -110.0, L["height"] * 1.02
        # Map the snow line to v = 0.62, where the gradient texture steps to white.
        v_snow = 0.62

        def alt_v(y):
            if snow_line >= 1e8:
                return np.clip((y - y_lo) / (y_hi - y_lo), 0.0, 0.60)
            if y <= snow_line:
                return (y - y_lo) / max(snow_line - y_lo, 1e-6) * v_snow
            return v_snow + (y - snow_line) / max(y_hi - snow_line, 1e-6) * (1.0 - v_snow)

        for s in range(segs):
            s2 = (s + 1) % segs
            for r in range(rows - 1):
                a = s * rows + r
                b = s2 * rows + r
                c = s2 * rows + r + 1
                d = s * rows + r + 1
                faces.append((a, b, c, d))
                u = (s % 2) * 0.9 + 0.05
                u2 = ((s + 1) % 2) * 0.9 + 0.05
                # ``u2b`` puts Unity Y into Blender Z.
                face_uvs.extend([(u, alt_v(verts[a][2])), (u2, alt_v(verts[b][2])),
                                 (u2, alt_v(verts[c][2])), (u, alt_v(verts[d][2]))])

        obj = S.mesh_from_arrays(f"DistantRange_{li}", verts, faces, uvs=face_uvs)

        mat = S.pbr_material(f"SakuraPass_DistantRange_{li}",
                             base_color=L["color"], roughness=0.95)
        S.set_texture(mat, S.load_image(os.path.join(S.textures_dir(),
                                                     "Sakura_Ridge_Gradient.png")), "Base Color")
        S.assign_material(obj, mat)
        objs.append(obj)

    S.export_glb(objs, "SakuraPass_Distant_Ranges.glb")
    return objs


# ------------------------------------------------------------------ hero volcano

# A Fuji-style stratovolcano anchoring the horizon. It is the single most recognisable shape in
# the concept board (it appears in 5 of the 7 panels), so it is authored as real geometry rather
# than painted into the sky texture: it has to sit at a fixed world position, be occluded
# correctly by the nearer ridge rings, and pick up the same fog as everything else.
#
# ---- Placement is a framing problem, not a map-decoration problem ----
#
# The route now finishes pointed straight at the volcano, so the centre is solved *backwards*
# from what the rider must see at the finish line rather than dropped somewhere plausible.
#
# At (262, 913) the finish stood 438 m from the centre - *inside* the 700 m base radius. The
# summit then sat 39 deg above the eye, far outside a 55 deg frame, so the hero shot was a
# featureless lavender slab filling the sky: no silhouette, no snow cap, no lake. The flank
# began rising out of the ground barely 25 m past the last metre of asphalt.
#
# The constraints that actually matter, in order:
#
#   1. **The summit must be in frame at the finish.** A 55 deg vertical FOV reaches 27.5 deg
#      above the eye, so the summit wants ~24 deg - dramatic, with headroom. Summit height is
#      FUJI_BASE_Y + FUJI_HEIGHT = 370 m and the finish eye is ~16 m, so the centre must stand
#      370 - 16 over tan(24 deg) ~= 800 m from the last route sample.
#   2. **The route must never enter the base.** 800 m out with a 700 m radius leaves the finish
#      100 m clear of the skirt, so no part of the carriageway can intersect the cone.
#   3. **Open water between rider and mountain.** The flank breaks the lake surface (y = -44) at
#      r = 505 m, which puts Fuji's own shoreline ~295 m beyond the end of the road: the rider
#      stops on a shelf, the ground falls away to the lake, and the volcano rises from the far
#      side of it. That is the Kawaguchiko framing the concept board asks for.
#   4. **It has to grow on the way in.** Distance to the centre runs 1342 m at the start line ->
#      921 m at the summit -> 800 m at the finish, so Fuji swells ~1.7x in apparent size across
#      the ride while the fog thins from ~73% to ~91% transmittance. Pushing the mountain
#      further out to make it grander *reduces* that ratio - the approach is worth more than the
#      extra grandeur, and this is the balance point.
#
# The bearing is the finish tangent itself, (0.976, 0.219), so the last straight aims at the
# summit. Base-to-height stays near 1.5:1; anything steeper reads as a spike, not a
# stratovolcano, however good the concave profile is.
FUJI_CENTER = (615.0, 992.0)     # Unity (x, z) - 800 m beyond the finish, on its forward tangent
FUJI_BASE_Y = -110.0             # same buried skirt as the ridge rings
FUJI_RADIUS = 700.0              # broad: a 1.55:1 base-to-height cone reads as a spike, not Fuji
FUJI_HEIGHT = 480.0
FUJI_SNOW_Y = 200.0              # summit is 370, so snow caps the top ~35% as in the concept.
                                 # Applied by SakuraCel's _SnowRange, not by a material slot.


def build_fuji(name="SakuraPass_Fuji"):
    """
    Concave-flanked cone with erosion gullies, a summit crater and an irregular snow line.

    The flank profile is parameterised by *radius* rather than height: ``y = H * (1 - u)^1.55``
    where ``u = r / R``. That yields the flaring, almost-flat skirt and steep summit of a real
    stratovolcano; driving radius from height instead gives a dome.
    """
    print(f"[sakura] building {name}...")

    segs, rows = 256, 96
    u_top = 0.035                                    # radius fraction where the crater begins
    cx0, cz0 = FUJI_CENTER
    summit_y = FUJI_BASE_Y + FUJI_HEIGHT

    ang = np.linspace(0, 2 * np.pi, segs, endpoint=False)
    ca, sa = np.cos(ang), np.sin(ang)

    # Radial gullies: ridged noise around the azimuth so the flanks have erosion channels
    # instead of reading as a machined cone.
    gully = S.ridged_2d(ca * 3.4, sa * 3.4, 1.0, octaves=4, seed=3301)
    gully = (gully - gully.mean()) * 2.0
    # Broad lobes so the silhouette is not perfectly circular.
    lobe = S.fbm_2d(ca * 1.3, sa * 1.3, 1.0, octaves=3, seed=3307) - 0.5

    # Vertical flank relief. This does double duty: the lower flank is almost flat and faces
    # straight up, so it takes an even wash of light and reads as a featureless slab from the
    # road; and because the snow cap is now a world-height blend in the shader, this relief is
    # the *only* thing that stops the snow line from being a perfect horizontal ellipse.
    # The u*(1-u) shaping keeps it alive across the whole flank - including the snow line at
    # u ~ 0.25 - while vanishing at the buried base edge and at the summit.
    foot = S.fbm_2d(ca * 2.1, sa * 2.1, 1.0, octaves=4, seed=3319) - 0.5
    detail = S.fbm_2d(ca * 7.5, sa * 7.5, 1.0, octaves=3, seed=3313) - 0.5

    # --- vertex grid, built vectorised so the flank can carry genuine 2D relief -------------
    #
    # Every band above is a function of *azimuth only*, i.e. constant along any radial line. At
    # 950 m that was invisible, but the route now finishes 800 m out with the lower flank filling
    # a third of the frame, and an azimuth-only field renders there as perfectly straight
    # corduroy - or, once smoothed, as a flat sheet of colour. What was missing is variation
    # *along* the slope, so the ridged/fbm bands below are sampled at each vertex's true world
    # (x, z) and displace it vertically: spurs and hollows that break up the wash of light and
    # give the silhouette and the snow line something to follow.
    t = np.arange(rows) / (rows - 1)
    # Rows are spaced uniformly in *height*, not in radius. With uniform-radius rows the
    # (1-u)^1.55 profile makes the topmost faces ~15 m tall, and the snow boundary then
    # steps down them in visible blocks.
    u_row = 1.0 - (1.0 - u_top) * t ** (1.0 / 1.55)   # radius fraction, 1 -> u_top
    U = np.broadcast_to(u_row[None, :], (segs, rows))
    CA = ca[:, None]
    SA = sa[:, None]

    # Gullies bite hardest low down and vanish toward the summit.
    RR = FUJI_RADIUS * U * (1.0 + gully[:, None] * 0.045 * U + lobe[:, None] * 0.06 * U)
    PX = cx0 + CA * RR
    PZ = cz0 + SA * RR

    Y = FUJI_BASE_Y + FUJI_HEIGHT * (1.0 - U) ** 1.55
    # Peaks mid-flank, vanishes at both the buried base edge and the summit.
    shape = U * (1.0 - U) * 4.0
    Y = Y + (foot[:, None] * 34.0 + detail[:, None] * 13.0) * shape
    # Broad spurs and gullies that actually run down the slope. Kept moderate on purpose: Fuji is
    # famously smooth, and the job here is to stop the flank reading as a painted sheet, not to
    # turn it into an alpine massif.
    Y = Y + (S.ridged_2d(PX, PZ, 1.0 / 300.0, octaves=5, seed=3323) - 0.42) * 38.0 * shape
    Y = Y + (S.fbm_2d(PX, PZ, 1.0 / 110.0, octaves=4, seed=3329) - 0.5) * 18.0 * shape

    verts = []
    for s in range(segs):
        for r in range(rows):
            verts.append(S.u2b(PX[s, r], Y[s, r], PZ[s, r]))

    crater_floor = len(verts)
    verts.append(S.u2b(cx0, summit_y - 26.0, cz0))            # recessed crater bowl

    faces, uvs = [], []
    for s in range(segs):
        s2 = (s + 1) % segs
        for r in range(rows - 1):
            a = s * rows + r
            b = s2 * rows + r
            c = s2 * rows + r + 1
            d = s * rows + r + 1
            faces.append((a, b, c, d))
            v0, v1 = r / (rows - 1), (r + 1) / (rows - 1)
            u0, u1 = s / segs, (s + 1) / segs
            uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    # Close the summit with a fan into the crater bowl.
    for s in range(segs):
        s2 = (s + 1) % segs
        a = s * rows + (rows - 1)
        b = s2 * rows + (rows - 1)
        faces.append((a, b, crater_floor))
        uvs.extend([(s / segs, 1.0), ((s + 1) / segs, 1.0), (0.5, 1.0)])

    obj = S.mesh_from_arrays(name, verts, faces, uvs=uvs)
    S.cap_open_boundaries(obj)      # seal the buried base so recalc has a manifold to work with
    S.recalc_normals(obj)           # orientation-normalising: immune to winding mistakes
    S.shade_auto_smooth(obj, angle_deg=38.0)

    rock = S.pbr_material(f"{name}_Rock", base_color=(0.30, 0.28, 0.40, 1.0), roughness=0.95)
    S.assign_material(obj, rock)

    # No snow material slot. Splitting snow from rock per-face quantises the boundary to whole
    # faces, and at this distance a face is ~12 px on screen, so the snow line came out as a row
    # of rectangular blocks no amount of threshold noise could hide (raising the mesh density
    # enough to fix it would cost ~270k tris). SakuraCel blends the cap by world height instead;
    # the irregular line comes from the flank relief, which the blend follows exactly.

    S.report(name, obj)
    S.export_glb([obj], f"{name}.glb")
    return [obj]


def _water_axis(lo, hi, step, reach, growth=1.30):
    """
    Dense sampling across the valley, geometrically coarsening out to the horizon.

    The lake used to be a uniform grid inset just past the terrain. With the ridge rings pushed
    out to 2.0-3.5 km that leaves a band of *sky* between the last water vertex and the foot of
    the backdrop - a bright strip under the ridges exactly where the horizon should be. Extending
    a uniform grid that far would either cost ~500k verts or throw away the near-field ripple
    that stops the sun reading as one flat slab, so the axis is dense (``step``) across the
    valley and doubles outward from there.
    """
    core = np.arange(lo, hi + step, step)
    out, d, v = [], step, float(core[-1])
    while v < hi + reach:
        d *= growth
        v += d
        out.append(v)
    pre, d, v = [], step, float(core[0])
    while v > lo - reach:
        d *= growth
        v -= d
        pre.append(v)
    return np.concatenate([np.array(pre[::-1]), core, np.array(out)])


def build_lake():
    """
    The valley floor lake, authored rather than dropped in as a Unity quad.

    It is a gently rippled grid rather than a flat plane: even with a water shader on top, a
    perfectly flat sheet catches the low sun as one uniform slab and kills the sense of scale.
    The near field is inset under the terrain slopes; the far field runs out past the ridge rings
    so the water meets the backdrop at the horizon instead of stopping in mid-air.
    """
    print("[sakura] building lake surface...")
    xs = _water_axis(MIN_X - 60.0, MAX_X + 60.0, 12.0, 4600.0)
    zs = _water_axis(MIN_Z - 60.0, MAX_Z + 60.0, 12.0, 4600.0)
    nx, nz = len(xs), len(zs)

    X, Z = np.meshgrid(xs, zs, indexing='ij')
    ripple = (np.sin(X * 0.055 + Z * 0.021) * 0.10 +
              np.sin(X * 0.017 - Z * 0.048) * 0.14 +
              S.fbm_2d(X, Z, 1.0 / 55.0, octaves=3, seed=7717) * 0.22 - 0.11)
    # The ripple has a ~100 m wavelength, and the far-field rows are spaced further apart than
    # that, so out there it is sampled below Nyquist: adjacent quads tilt in alternating
    # directions and the water shader's specular turns the whole horizon into a band of moire
    # stripes. Fade it out past the valley, where the surface is at a grazing angle anyway and
    # the shader's own wave normals are doing all the visible work.
    fade = np.clip((2400.0 - np.hypot(X + 50.0, Z - 300.0)) / 900.0, 0.0, 1.0)
    Y = LAKE_LEVEL + ripple * fade
    print(f"[sakura]   water grid {nx} x {nz}, "
          f"x {xs[0]:.0f}..{xs[-1]:.0f}, z {zs[0]:.0f}..{zs[-1]:.0f}")

    verts = []
    for i in range(nx):
        for j in range(nz):
            verts.append(S.u2b(X[i, j], Y[i, j], Z[i, j]))

    faces, uvs = [], []
    for i in range(nx - 1):
        for j in range(nz - 1):
            a = i * nz + j
            b = (i + 1) * nz + j
            c = (i + 1) * nz + j + 1
            d = i * nz + j + 1
            faces.append((a, b, c, d))
            u0, u1 = X[i, 0] / 40.0, X[i + 1, 0] / 40.0
            v0, v1 = Z[0, j] / 40.0, Z[0, j + 1] / 40.0
            uvs.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    obj = S.mesh_from_arrays("SakuraPass_Lake", verts, faces, uvs=uvs)
    mat = S.pbr_material("SakuraPass_LakeWater", base_color=(0.10, 0.24, 0.34, 1.0),
                         roughness=0.12, metallic=0.0)
    S.assign_material(obj, mat)
    S.export_glb([obj], "SakuraPass_Lake.glb")
    return [obj]


def main():
    S.reset_scene()
    R.write_route_json()
    build_terrain()

    S.reset_scene()
    build_distant_ranges()

    S.reset_scene()
    build_fuji()

    S.reset_scene()
    build_lake()
    print("[sakura] landscape build complete.")


if __name__ == "__main__":
    main()

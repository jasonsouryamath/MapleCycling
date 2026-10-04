"""
SHIOSAI COAST - authored environment assets (Blender 4.5 -> glTF -> Unity).

Replaces the first-pass C# primitives (cones for pines, stacked cylinders for the lighthouse,
boxes for the guardrail) with real modelled geometry, built to the look of the 20 concept
renders in Assets/Environment/ShiosaiCoast/ShiosaiCoast_*.png.

    blender -b -P build_shiosai.py

WHAT STAYS IN C#, AND WHY
-------------------------
Anything swept along the published centreline - the carriageway, the cliff/beach/headland
landform and the guardrail RUN - stays in ShiosaiCoastEnvironment.cs. Those are not "crude
primitives"; they are ribbon geometry measured off ShiosaiRoute.json, and re-authoring them in
Blender would mean publishing the same centreline into a second tool for zero fidelity gain
while creating a way for the road and the land under it to desynchronise.

Everything that is a *thing* - a lighthouse, a torii, a pine, a house, a boat, a guardrail bay
with its reflector - is authored here, because that is where the primitives were genuinely
costing fidelity.

INVARIANTS (from the sakura-environment-assets skill; non-negotiable)
  * every vertex authored in UNITY coordinates and passed through S.u2b()
  * origin at the base, +Z forward, real metre scale
  * closed solids get S.recalc_normals() rather than hand-tuned winding
  * continuous-run props (the guardrail bay) run along local +X with end posts half a bay in
  * material NAMES are the staging key: ShiosaiCoastEnvironment routes cel materials by
    matching on them, so they must stay descriptive
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sakura_lib as S


# --------------------------------------------------------------------------- output location

def shiosai_assets_dir():
    d = os.path.join(S.repo_root(), "Assets", "Environment",
                     "ShiosaiCoast", "BlenderAssets")
    os.makedirs(d, exist_ok=True)
    return d


def export(objs, filename):
    """export_glb, redirected into the Shiosai region's own asset folder."""
    original = S.blender_assets_dir
    S.blender_assets_dir = shiosai_assets_dir
    try:
        S.export_glb(objs, filename)
    finally:
        S.blender_assets_dir = original


# --------------------------------------------------------------------------- geometry helpers

class Builder:
    """Accumulates Unity-space geometry into one mesh, one material slot at a time."""

    def __init__(self):
        self.verts = []
        self.faces = []
        self.uvs = []

    def add_face(self, pts, uv=None):
        base = len(self.verts)
        for p in pts:
            self.verts.append(S.u2b(*p))
        self.faces.append(tuple(range(base, base + len(pts))))
        if uv is None:
            uv = [(0, 0), (1, 0), (1, 1), (0, 1)][:len(pts)]
            while len(uv) < len(pts):
                uv.append((0, 1))
        self.uvs.extend(uv)

    def box(self, centre, size, yaw_deg=0.0, uv_scale=1.0):
        """Axis-aligned box, optionally yawed about Y. centre/size in Unity metres."""
        cx, cy, cz = centre
        hx, hy, hz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
        ca, sa = math.cos(math.radians(yaw_deg)), math.sin(math.radians(yaw_deg))

        def P(x, y, z):
            return (cx + x * ca + z * sa, cy + y, cz - x * sa + z * ca)

        c = {(sx, sy, sz): P(sx * hx, sy * hy, sz * hz)
             for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)}
        u, v = size[0] * uv_scale, size[1] * uv_scale
        quads = [
            [c[(-1, 1, -1)], c[(1, 1, -1)], c[(1, 1, 1)], c[(-1, 1, 1)]],      # top
            [c[(-1, -1, 1)], c[(1, -1, 1)], c[(1, -1, -1)], c[(-1, -1, -1)]],  # bottom
            [c[(-1, -1, -1)], c[(1, -1, -1)], c[(1, 1, -1)], c[(-1, 1, -1)]],  # -Z
            [c[(1, -1, 1)], c[(-1, -1, 1)], c[(-1, 1, 1)], c[(1, 1, 1)]],      # +Z
            [c[(-1, -1, 1)], c[(-1, -1, -1)], c[(-1, 1, -1)], c[(-1, 1, 1)]],  # -X
            [c[(1, -1, -1)], c[(1, -1, 1)], c[(1, 1, 1)], c[(1, 1, -1)]],      # +X
        ]
        for q in quads:
            self.add_face(q, [(0, 0), (u, 0), (u, v), (0, v)])

    def prism(self, rings, close=True, cap_bottom=True, cap_top=True, uv_v=1.0, uv_u=1.0):
        """
        rings = list of lists of Unity-space points, each ring the same length.
        Builds the side wall and optionally flat caps. Used for rocks and hulls.

        `uv_u` repeats the texture around the circumference `uv_u` times. Left at 1.0 the
        whole 0..1 texture is stretched once around the object - fine for a big smooth sweep,
        but on a squat many-sided rock it blows the albedo up into a handful of giant blotches
        that read as camouflage/static rather than fine rock grain. Callers wrapping a real
        rock texture around a compact prop should pass a `uv_u` that keeps individual tiles at
        roughly the same metre-scale the texture was authored for (~8 m, matching the terrain
        ribbons), not one tile stretched over the whole circumference.
        """
        n = len(rings[0])
        for r in range(len(rings) - 1):
            for j in range(n if close else n - 1):
                k = (j + 1) % n
                self.add_face([rings[r][j], rings[r][k], rings[r + 1][k], rings[r + 1][j]],
                              [(j / n * uv_u, r * uv_v), ((j + 1) / n * uv_u, r * uv_v),
                               ((j + 1) / n * uv_u, (r + 1) * uv_v), (j / n * uv_u, (r + 1) * uv_v)])
        if cap_bottom:
            self.add_face(list(reversed(rings[0])),
                          [(0.5 + 0.5 * math.cos(i / n * math.tau),
                            0.5 + 0.5 * math.sin(i / n * math.tau)) for i in range(n)])
        if cap_top:
            self.add_face(list(rings[-1]),
                          [(0.5 + 0.5 * math.cos(i / n * math.tau),
                            0.5 + 0.5 * math.sin(i / n * math.tau)) for i in range(n)])

    def tube(self, samples, segments=14, cap_bottom=True, cap_top=True, centre=(0.0, 0.0),
            uv_u=1.0):
        """samples = [(radius, y), ...] revolved about the vertical axis at `centre` (x,z)."""
        rings = []
        for (r, y) in samples:
            ring = []
            for s in range(segments):
                a = s / segments * math.tau
                ring.append((centre[0] + math.cos(a) * r, y, centre[1] + math.sin(a) * r))
            rings.append(ring)
        self.prism(rings, cap_bottom=cap_bottom, cap_top=cap_top, uv_v=0.4, uv_u=uv_u)

    def limb(self, p0, p1, r0, r1, segs=6):
        """
        A TAPERED LIMB along the true 3D segment p0 -> p1 (Unity metres).

        FLOATING-PLANK FIX. Boughs used to be `box(centre, (seg_len, thick, thick), yaw_deg=...)`:
        a box can only be YAWED, so every branch came out as a perfectly horizontal, untapered
        slab whose length was the segment's 3D length. Against the sky that is a dark rectangle
        with four big flat faces - exactly the "floating plank" the reveal render showed - and
        because the slab sat at the segment's mid height while the needle pad sat at the TIP
        height, the inner half of every bough hung in clear air below its own foliage.

        A limb is built on the segment's own axis, so it rises with the branch, and it tapers
        from butt to tip, so it reads as a bough rather than as a plank.
        """
        ax = (p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2])
        ln = math.sqrt(ax[0] ** 2 + ax[1] ** 2 + ax[2] ** 2)
        if ln < 1e-5:
            return
        ax = (ax[0] / ln, ax[1] / ln, ax[2] / ln)
        # A reference vector that is never parallel to the axis.
        ref = (0.0, 1.0, 0.0) if abs(ax[1]) < 0.9 else (1.0, 0.0, 0.0)
        u = (ax[1] * ref[2] - ax[2] * ref[1],
             ax[2] * ref[0] - ax[0] * ref[2],
             ax[0] * ref[1] - ax[1] * ref[0])
        ul = math.sqrt(u[0] ** 2 + u[1] ** 2 + u[2] ** 2) or 1.0
        u = (u[0] / ul, u[1] / ul, u[2] / ul)
        v = (ax[1] * u[2] - ax[2] * u[1],
             ax[2] * u[0] - ax[0] * u[2],
             ax[0] * u[1] - ax[1] * u[0])
        rings = []
        for (p, r) in ((p0, r0), (p1, r1)):
            ring = []
            for s in range(segs):
                a = s / segs * math.tau
                cu, sv = math.cos(a) * r, math.sin(a) * r
                ring.append((p[0] + u[0] * cu + v[0] * sv,
                             p[1] + u[1] * cu + v[1] * sv,
                             p[2] + u[2] * cu + v[2] * sv))
            rings.append(ring)
        self.prism(rings, cap_bottom=True, cap_top=True, uv_v=max(0.4, ln / 0.6), uv_u=2.0)

    def mesh(self, name, smooth=True):
        return S.mesh_from_arrays(name, self.verts, self.faces, uvs=self.uvs, smooth=smooth)


def finish(builder, name, mat, smooth=True, bevel=None):
    obj = builder.mesh(name, smooth=smooth)
    S.recalc_normals(obj)
    _triangulate_ngons(obj)
    if bevel:
        S.add_bevel(obj, width=bevel, segments=2, angle_deg=38.0)
    S.assign_material(obj, mat)
    return obj


def _triangulate_ngons(obj):
    """
    glTF cannot build a tangent basis for n-gons, and several builders here close rings with
    9-16-sided caps. Triangulating only the n-gons keeps the quad grids intact (so UVs and
    smoothing stay predictable) while silencing the tangent failure.
    """
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
        bm.to_mesh(obj.data)
        obj.data.update()
    bm.free()


def mat(name, colour, rough=0.8, metallic=0.0):
    return S.pbr_material(name, base_color=(colour[0], colour[1], colour[2], 1.0),
                          roughness=rough, metallic=metallic)


# --------------------------------------------------------------------------- 1. lighthouse

def build_lighthouse(name="Shiosai_Lighthouse"):
    """
    The white harbour light from renders 13/17/20: a slightly tapered white tower with red
    bands, a stone plinth, a railed gallery and a glazed lantern under a red cap.

    ~19 m to the cap, which is real scale for a Japanese harbour light.
    """
    print(f"[shiosai] building {name}...")
    parts = []

    stone = mat(f"{name}_Rock_Plinth", (0.56, 0.53, 0.49), rough=0.92)
    white = mat(f"{name}_White_Tower", (0.90, 0.90, 0.87), rough=0.70)
    red = mat(f"{name}_Red_Band", (0.62, 0.14, 0.11), rough=0.62)
    glassm = mat(f"{name}_Glass_Lantern", (1.0, 0.88, 0.52), rough=0.18, metallic=0.1)

    # plinth: a rough rock base so the tower is never a cylinder standing on grass
    b = Builder()
    b.tube([(4.6, 0.0), (4.3, 0.9), (3.9, 1.8), (3.5, 2.4)], segments=12,
           uv_u=max(1.0, (math.tau * 4.6) / 8.0))
    parts.append(finish(b, f"{name}_plinth", stone, smooth=False))

    # tower shaft, in three white runs with two red bands between them
    runs = [(2.55, 2.4, 2.30, 6.2), (2.22, 7.4, 2.00, 11.4), (1.95, 12.6, 1.80, 15.0)]
    b = Builder()
    for (r0, y0, r1, y1) in runs:
        b.tube([(r0, y0), (r1, y1)], segments=20, cap_bottom=False, cap_top=False)
    parts.append(finish(b, f"{name}_shaft", white))

    b = Builder()
    for (y0, y1, r) in [(6.2, 7.4, 2.26), (11.4, 12.6, 2.02)]:
        b.tube([(r, y0), (r, y1)], segments=20, cap_bottom=False, cap_top=False)
    b.tube([(2.95, 15.0), (2.95, 15.30)], segments=20)                      # gallery deck
    b.tube([(1.35, 18.1), (1.62, 18.35), (0.9, 19.0), (0.0, 19.5)], segments=16)  # cap
    parts.append(finish(b, f"{name}_red", red))

    # gallery railing - 14 little stanchions and a rail ring, the detail that makes it read
    b = Builder()
    for s in range(14):
        a = s / 14 * math.tau
        b.box((math.cos(a) * 2.80, 15.30 + 0.45, math.sin(a) * 2.80), (0.07, 0.90, 0.07),
              yaw_deg=-math.degrees(a))
    b.tube([(2.86, 16.10), (2.86, 16.22)], segments=20, cap_bottom=False, cap_top=False)
    parts.append(finish(b, f"{name}_rail_steel", red, smooth=False))

    # lantern room: glazed drum
    b = Builder()
    b.tube([(1.72, 15.30), (1.72, 18.10)], segments=16, cap_bottom=False, cap_top=False)
    parts.append(finish(b, f"{name}_glass", glassm))

    obj = S.join(parts, name)
    S.shade_auto_smooth(obj, angle_deg=40.0)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


# --------------------------------------------------------------------------- 2. torii

def _torii(name, height, width, style, colour_name):
    """
    Shared torii builder.

    style 'myojin'  - land torii: kasagi + shimaki + nuki + gakuzuka, pillars with 転び batter.
    style 'ryobu'   - the form that actually stands in water (Itsukushima): the same members
                      plus fore-and-aft subsidiary posts (稚児柱) with their own tie beams.
                      Authored with y = 0 AT THE WATERLINE so Unity only drops it to sea level.
    """
    print(f"[shiosai] building {name}...")
    parts = []
    # 朱色 authored DARK: at high albedo the daylight key plus bloom clips it to a flat
    # fire-engine red with no visible modelling. (Same lesson as the Sakura lake torii.)
    red = mat(f"{name}_Red_{colour_name}", (0.46, 0.12, 0.09), rough=0.55)
    black = mat(f"{name}_Dark_Base", (0.13, 0.12, 0.12), rough=0.85)

    half = width * 0.5
    batter = height * 0.017          # 転び: pillars lean inward ~1.7 % of height
    pr = height * 0.043              # pillar radius
    nuki_y = height * 0.62
    shim_y = height * 0.88
    kasa_y = height * 0.945
    rise = height * 0.055            # 反り: the upward sweep of the lintel tips
    overhang = width * 0.20

    b = Builder()
    # --- pillars, battered inward ---------------------------------------------------------
    for sgn in (-1, 1):
        steps = 8
        rings = []
        for k in range(steps + 1):
            t = k / steps
            y = t * (kasa_y + rise * 0.2)
            x = sgn * (half - batter * t)
            r = pr * (1.0 - 0.10 * t)
            rings.append([(x + math.cos(a / 12 * math.tau) * r, y,
                           math.sin(a / 12 * math.tau) * r) for a in range(12)])
        b.prism(rings, uv_v=0.5)
    # --- 貫 nuki: passes THROUGH the pillars and protrudes both sides ----------------------
    b.box((0.0, nuki_y, 0.0), (width + overhang * 0.9, height * 0.052, pr * 1.5))
    # --- 楔 kusabi: the wedges locking the nuki where it emerges ---------------------------
    for sgn in (-1, 1):
        b.box((sgn * (half + overhang * 0.30), nuki_y, 0.0),
              (pr * 0.5, height * 0.085, pr * 1.75))
    # --- 額束 gakuzuka: central tab bridging nuki to shimaki -------------------------------
    b.box((0.0, (nuki_y + shim_y) * 0.5, 0.0),
          (width * 0.075, shim_y - nuki_y, pr * 1.1))

    # --- 島木 shimaki and 笠木 kasagi: the pair that makes it 明神 rather than 神明 --------
    def lintel(y0, thick, depth, extra):
        seg = 16
        span = width + overhang * 2 + extra
        ring_pts = []
        for k in range(seg + 1):
            t = k / seg
            x = -span * 0.5 + span * t
            # symmetric upward sweep toward the tips
            lift = rise * (2 * abs(t - 0.5)) ** 1.7
            ring_pts.append((x, y0 + lift))
        for k in range(seg):
            x0, y_0 = ring_pts[k]
            x1, y_1 = ring_pts[k + 1]
            for dz in (-1, 1):
                b.add_face([(x0, y_0, dz * depth * 0.5), (x1, y_1, dz * depth * 0.5),
                            (x1, y_1 + thick, dz * depth * 0.5), (x0, y_0 + thick, dz * depth * 0.5)])
            b.add_face([(x0, y_0 + thick, -depth * 0.5), (x1, y_1 + thick, -depth * 0.5),
                        (x1, y_1 + thick, depth * 0.5), (x0, y_0 + thick, depth * 0.5)])
            b.add_face([(x0, y_0, depth * 0.5), (x1, y_1, depth * 0.5),
                        (x1, y_1, -depth * 0.5), (x0, y_0, -depth * 0.5)])
        for sgn in (-1, 1):
            x = sgn * span * 0.5
            y_e = y0 + rise
            b.add_face([(x, y_e, -depth * 0.5), (x, y_e, depth * 0.5),
                        (x, y_e + thick, depth * 0.5), (x, y_e + thick, -depth * 0.5)])

    lintel(shim_y, height * 0.055, pr * 2.3, 0.0)
    lintel(kasa_y, height * 0.048, pr * 2.7, width * 0.06)

    if style == 'ryobu':
        # 稚児柱: the fore/aft braces that let a torii stand in the sea.
        for sgn in (-1, 1):
            for dz in (-1, 1):
                bx = sgn * (half + width * 0.17)
                bz = dz * width * 0.17
                steps = 5
                rings = []
                for k in range(steps + 1):
                    t = k / steps
                    y = t * nuki_y * 0.92
                    r = pr * 0.52 * (1.0 - 0.12 * t)
                    cx = bx - sgn * (width * 0.055) * t
                    cz = bz - dz * (width * 0.055) * t
                    rings.append([(cx + math.cos(a / 10 * math.tau) * r, y,
                                   cz + math.sin(a / 10 * math.tau) * r) for a in range(10)])
                b.prism(rings, uv_v=0.5)
                # tie beam back to the main pillar: a real diagonal strut from the child post
                # (bx, bz) to the main pillar (sgn*half, 0), not an axis-aligned box guessed at
                # a fixed z. An unrotated box here floated disconnected from both posts whenever
                # bz != 0 - the "duplicated/disconnected slab" look on the sea torii.
                px, pz = sgn * half, 0.0
                dxb, dzb = bx - px, bz - pz
                strut_len = math.hypot(dxb, dzb)
                strut_yaw = math.degrees(math.atan2(-dzb, dxb))
                b.box(((bx + px) * 0.5, nuki_y * 0.86, (bz + pz) * 0.5),
                      (strut_len + pr, height * 0.028, pr * 0.7), yaw_deg=strut_yaw)

    parts.append(finish(b, f"{name}_red", red, bevel=height * 0.004))

    # black lacquered pillar feet (亀腹) - real torii never meet the ground in bare vermilion
    b = Builder()
    for sgn in (-1, 1):
        b.tube([(pr * 1.42, 0.0), (pr * 1.30, height * 0.028), (pr * 1.06, height * 0.055)],
               segments=12, centre=(sgn * half, 0.0))
    parts.append(finish(b, f"{name}_darkfoot", black, smooth=False))

    obj = S.join(parts, name)
    S.shade_auto_smooth(obj, angle_deg=36.0)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


def build_torii():
    _torii("Shiosai_Torii", height=7.2, width=5.4, style='myojin', colour_name="Torii")
    # Origin at the WATERLINE. Do not bake a vertical offset into the pillars - that is what
    # once left a lintel floating above its own posts on the Sakura lake torii.
    _torii("Shiosai_SeaTorii", height=14.5, width=10.6, style='ryobu', colour_name="SeaTorii")


# --------------------------------------------------------------------------- 3. guardrail bay

def build_guardrail_bay(name="Shiosai_GuardrailBay", bay=4.0):
    """
    One 4 m bay of Japanese Gr-A-E beam guardrail WITH ITS ORANGE REFLECTOR - the detail the
    concept renders show on every clifftop run, and the thing the mock's plain boxes lacked.

    Runs along local +X, beam spanning EXACTLY `bay` so Unity can butt bays end to end along
    the route (yawing each to atan2(T.x, T.z) + 90 deg) and get one continuous rail. Posts sit
    at +/- bay/4, i.e. a 2.0 m post pitch that stays uniform across the bay joint.

    COLOUR NOTE: MLIT's landscape guideline puts dark brown 10YR 2.0/1.0 on scenic mountain
    routes, which is why Sakura Pass uses it. The Shiosai renders show the *coastal* standard -
    galvanised/white beam with orange reflectors - so this asset deliberately does not follow the
    pass's brown. Provisional: if the art direction later unifies them, change it here only.
    """
    print(f"[shiosai] building {name}...")
    parts = []
    steel = mat(f"{name}_Guardrail_Steel_Beam", (0.74, 0.75, 0.73), rough=0.55, metallic=0.35)
    post_m = mat(f"{name}_Guardrail_Steel_Post", (0.56, 0.58, 0.57), rough=0.62, metallic=0.30)
    refl = mat(f"{name}_Reflector_Orange", (0.95, 0.42, 0.05), rough=0.25)

    # ---- GEOMETRY CONSTANTS (JIS Gr-A-E proportions; PROVISIONAL where noted) -------------
    BEAM_BASE = 0.58        # m, underside of the W-beam above grade
    BEAM_DEPTH = 0.075      # m, real pressed-sheet depth - NOT a 3 cm sheet
    POST_PITCH = bay * 0.5  # m, posts at 2.0 m on a 4 m bay (the JIS pitch)
    POST_BURY = 0.30        # m, how far the post is driven BELOW grade. PROVISIONAL, but it
                            # is what stops a post "floating" where the verge mesh dips a few
                            # centimetres between two route samples.
    POST_Z = -0.135         # m, post centre BEHIND the beam (road side is +Z)

    # W-BEAM AS A CLOSED SOLID.
    #
    # The previous bay was two unconnected sheets 3 cm apart: no top, no bottom, no end caps.
    # Butted end to end along a curve that reads at distance as a row of detached white
    # panels - light leaks straight through the open section and the silhouette breaks at every
    # bay joint. Building the cross-section as ONE closed loop (corrugated front, the same
    # profile offset back by BEAM_DEPTH, joined) and sweeping it between the two bay ends gives
    # a solid beam with real end caps, so consecutive bays butt into a continuous rail.
    prof = [
        (0.000, 0.000), (0.052, 0.055), (0.052, 0.115), (0.010, 0.165),
        (0.010, 0.205), (0.052, 0.255), (0.052, 0.315), (0.000, 0.370),
    ]
    loop = [(z, y) for (z, y) in prof] + \
           [(z - BEAM_DEPTH, y) for (z, y) in reversed(prof)]
    b = Builder()
    rings = [[(x, BEAM_BASE + y, z) for (z, y) in loop]
             for x in (-bay * 0.5, bay * 0.5)]
    b.prism(rings, cap_bottom=True, cap_top=True, uv_v=0.35, uv_u=1.0)
    parts.append(finish(b, f"{name}_beam", steel, smooth=False))

    # POSTS: two per bay (2 m pitch), each driven into the ground and tied to the beam with a
    # visible BLOCKOUT bracket. A single mid-bay post with nothing connecting it to the rail is
    # exactly what made the run read as "floating white posts" instead of a guardrail.
    b = Builder()
    post_x = (-POST_PITCH * 0.5, POST_PITCH * 0.5)
    for px in post_x:
        # shaft: from POST_BURY below grade up to just under the beam
        top = BEAM_BASE + 0.26
        b.box((px, (top - POST_BURY) * 0.5, POST_Z), (0.145, top + POST_BURY, 0.105))
        # ground collar, sat ON grade so the post visibly meets the verge
        b.box((px, 0.025, POST_Z), (0.30, 0.05, 0.26))
        # blockout bracket: post -> beam back face, so the rail is CONNECTED to its posts
        b.box((px, BEAM_BASE + 0.185, (POST_Z + 0.105 * 0.5 - BEAM_DEPTH) * 0.5),
              (0.165, 0.24, abs(POST_Z) + 0.02))
    parts.append(finish(b, f"{name}_post_steel", post_m, smooth=False))

    # ORANGE REFLECTOR: a round retro-reflective "delineator" head on a short stalk above the
    # post, which is what renders 01 and 15 actually show - the mock's flat 10 cm box read in
    # gameplay as a small yellow sticker instead of a light. One per bay (4 m), on the
    # downstream post, facing the carriageway (+Z).
    b = Builder()
    rx = post_x[1]
    b.box((rx, 0.95, 0.02), (0.028, 0.22, 0.028))          # stalk
    disc = []
    for r in range(3):
        # ~11 cm across, which is the real delineator head size. The first attempt at 21 cm
        # rendered as a row of yellow lollipops along the whole clifftop.
        rr = (0.042, 0.055, 0.038)[r]
        zz = (0.02, 0.055, 0.082)[r]
        disc.append([(rx + math.cos(a / 10 * math.tau) * rr,
                      1.12 + math.sin(a / 10 * math.tau) * rr, zz) for a in range(10)])
    b.prism(disc, cap_bottom=True, cap_top=True, uv_v=0.5)
    parts.append(finish(b, f"{name}_reflector", refl, smooth=False))

    obj = S.join(parts, name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


# --------------------------------------------------------------------------- 4. coastal pine

def _cushion(b, centre, radius, squash, rng, segs=11, rings=4, tile_m=0.9):
    """
    One irregular foliage CUSHION: a squashed, jittered blob.

    This is the unit the coast's canopies are made of. The previous pass built each canopy from
    4 huge closed cone skirts, which is exactly what the gameplay screenshot showed - sharp
    "paper party hat" triangles with one flat fill colour. Real pine/broadleaf canopies read as
    a cluster of rounded needle or leaf MASSES, so building a canopy from a dozen overlapping
    cushions gives a soft, layered, non-triangular silhouette at every distance.

    `tile_m` is METRES PER TEXTURE TILE. It is computed per blob from the blob's own size rather
    than passed as a raw UV multiplier: with a fixed multiplier the detail map stretched into
    long vertical streaks around every cushion, and the first gameplay frame of the new canopies
    read as crumpled green foil.

    BLOB FIX (opening visual overhaul, milestone 2). segs/rings went 9x3 -> 11x4 and the radial
    jitter is now driven by a PER-SEGMENT lobe rather than by uncorrelated noise. At 9x3 a
    cushion is a 27-quad sphere: every quad is large enough to catch its own flat highlight, so
    a canopy of them photographed as a heap of faceted balls. 11x4 halves the quad's angular
    size, and the lobe term makes the outline wavy instead of round - which is what stops the
    silhouette reading as a ball even before the extra segments help.
    """
    cx, cy, cz = centre
    # Per-blob lobe: 3-5 shallow bulges around the circumference, with a random phase. Because
    # the term is a smooth function of the angle it survives smooth shading and the silhouette,
    # whereas per-vertex random jitter just adds high-frequency noise the normals average away.
    lobes = 3.0 + math.floor(rng.next() * 3.0)
    lobe_phase = rng.next() * math.tau
    lobe_amt = 0.16 + 0.16 * rng.next()
    ring_list = []
    for r in range(rings + 2):
        # 0 -> bottom pole, rings+1 -> top pole; the poles are collapsed by prism's caps.
        t = r / (rings + 1)
        # A dome that is fuller above its equator than below - foliage sits ON its branch.
        phi = (t - 0.5) * math.pi
        rr = radius * math.cos(phi) * (0.86 + 0.30 * rng.next())
        yy = cy + math.sin(phi) * radius * squash
        ring = []
        for s in range(segs):
            a = s / segs * math.tau + t * 1.3
            lobe = 1.0 + lobe_amt * math.sin(a * lobes + lobe_phase + t * 0.9)
            jitter = lobe * (0.90 + 0.20 * rng.next())
            ring.append((cx + math.cos(a) * rr * jitter,
                         yy + (rng.next() - 0.5) * radius * 0.16,
                         cz + math.sin(a) * rr * jitter))
        ring_list.append(ring)
    # u wraps the circumference, v climbs the blob; both at ~tile_m metres per tile.
    uv_u = max(1.0, (math.tau * radius) / tile_m)
    uv_v = max(0.5, (2.0 * radius * squash) / ((rings + 1) * tile_m))
    b.prism(ring_list, cap_bottom=True, cap_top=True, uv_v=uv_v, uv_u=uv_u)


def build_pine(name="Shiosai_Pine", height=9.5, seed=3):
    """
    Japanese black pine (クロマツ) - the tree of every Japanese coastline, and the one the
    concept renders put on every headland and sea stack.

    LAYERED NEEDLE CUSHIONS, NOT CONE SKIRTS. The mock's four stacked cones read in-game as
    folded paper; a black pine's signature is horizontal PLATES of needle mass carried on
    gnarled, spreading boughs with sky visible between them. Each plate here is 3-5 overlapping
    `_cushion` blobs, so the silhouette is rounded and irregular at any distance and the
    Shiosai_Needle_Albedo detail map has real UV area to tile across.

    Sea-wind lean is still baked in - a windswept pine is the cheapest cue that this is a coast
    and not a mountain pass.
    """
    print(f"[shiosai] building {name}...")
    rng = _Rng(seed)
    parts = []
    bark = mat(f"{name}_Trunk_Bark", (0.30, 0.24, 0.20), rough=0.95)
    needle = mat(f"{name}_Needle_Canopy", (0.17, 0.34, 0.20), rough=0.88)

    lean = 0.15                      # the prevailing onshore wind
    def axis(t):
        return (lean * height * (t ** 1.6), 0.0)

    # --- trunk: a leaning, tapering tube with real spreading boughs ------------------------
    b = Builder()
    steps = 10
    rings = []
    for k in range(steps + 1):
        t = k / steps
        y = t * height * 0.80
        ax, az = axis(t)
        # A slight sway in the trunk so it is not a perfect ruler.
        ax += math.sin(t * 3.1) * 0.16
        r = 0.32 * (1.0 - 0.74 * t) + 0.02
        rings.append([(ax + math.cos(a / 9 * math.tau) * r, y,
                       az + math.sin(a / 9 * math.tau) * r) for a in range(9)])
    b.prism(rings, uv_v=0.6)

    # Boughs: (angle, height fraction, length fraction, rise). Alternating sides, longest low
    # down. FLOATING-PLANK FIX: the lengths are now a FRACTION OF TREE HEIGHT, not absolute
    # metres. They used to be fixed at 3.2-1.3 m for every variant, so Shiosai_Pine_B (12.5 m,
    # and scattered at up to 1.4x) came out as a 14 m pole carrying 3 m pads - a silhouette with
    # far more bare bough than needle mass, which is precisely what read as loose dark planks.
    # PROVISIONAL proportions.
    LS = height / 9.5
    boughs = [(0.5, 0.30, 3.2 * LS, 0.24), (3.3, 0.44, 2.9 * LS, 0.34),
              (1.9, 0.56, 2.5 * LS, 0.44), (4.9, 0.67, 2.2 * LS, 0.50),
              (0.9, 0.78, 1.7 * LS, 0.56), (2.5, 0.88, 1.3 * LS, 0.60)]
    plate_anchors = []
    for (ang, t0, ln, rise) in boughs:
        ax, az = axis(t0)
        y0 = t0 * height * 0.80
        tip = (ax + math.cos(ang) * ln, y0 + ln * rise, az + math.sin(ang) * ln)
        # Two segments so the bough kinks upward at its end, the way a pine branch does.
        mid = (ax + math.cos(ang) * ln * 0.55, y0 + ln * rise * 0.25,
               az + math.sin(ang) * ln * 0.55)
        # Tapered limbs on the branch's OWN axis (see Builder.limb). Thinner than the old
        # slabs too: whatever bough is still visible between pads should read as a twig, not
        # as a beam. PROVISIONAL radii.
        b.limb((ax, y0, az), mid, 0.085 * LS, 0.062 * LS)
        b.limb(mid, tip, 0.062 * LS, 0.034 * LS)
        plate_anchors.append(((ax, y0, az), mid, tip, ln, rise))
    parts.append(finish(b, f"{name}_trunk", bark, smooth=False))

    # --- canopy: one needle PLATE per bough plus a crown ----------------------------------
    # BLACK PINE PLATES, NOT GREEN CLOUDS (opening visual overhaul, milestone 2). The previous
    # pass scattered 13-18 near-spherical blobs in a disc around each bough tip, which at
    # roadside distance read as the faceted green boulders QA called "low-poly blobs". A black
    # pine's silhouette is the opposite: thin horizontal PADS of needle mass, wider than they are
    # deep, carried on the bough, with sky between them. Three changes deliver that:
    #   * squash 0.46 -> 0.30, so a plate is a pad rather than a ball;
    #   * the blob cloud is stretched ALONG the bough (ellipse, not disc) and its vertical spread
    #     is cut, so the pad has a defined top and bottom edge;
    #   * a ring of small outlier tufts is added just past the pad edge, which breaks the
    #     otherwise smooth elliptical outline into the ragged edge a needle mass actually has.
    b = Builder()
    for (butt, mid, tip, ln, rise) in plate_anchors:
        # Bough direction in plan, so the pad can be laid ALONG it.
        dx, dz = tip[0] - butt[0], tip[2] - butt[2]
        bl = math.hypot(dx, dz) or 1.0
        ux, uz = dx / bl, dz / bl

        def bough_y(s):
            """Height of the bough itself at arc fraction `s` (1.0 = the tip)."""
            return butt[1] + min(s, 1.0) * (tip[1] - butt[1])

        # FLOATING-PLANK FIX, part 2: the pad is now PARAMETERISED ALONG THE BOUGH instead of
        # being an ellipse centred on the tip at one flat height.
        #
        # The old cloud put every blob at `tip[1] + 0.18` - a single horizontal slab at the
        # TIP's height. The bough climbs to that tip, so its inner half hung up to 0.8 m in
        # clear air BELOW its own foliage and, silhouetted against the sea, read as a dark bar
        # floating free of any tree. Laying each blob at the height of the bough beneath it, and
        # letting `s` start at 0.26 (near the trunk) rather than at the tip, means the needle
        # mass wraps the whole limb. The width taper (narrow at the trunk, full at the tip)
        # keeps the black pine's teardrop plate rather than making it a sausage.
        # ALL PROVISIONAL tuning.
        blobs = 19 + int(rng.next() * 6.99)
        for k in range(blobs):
            s = 0.26 + 1.06 * math.sqrt(rng.next())          # 0.26 .. 1.32 of the bough
            taper = min(1.0, 0.30 + 0.85 * s)                 # narrow at the butt
            w = (rng.next() - 0.5) * 1.15 * ln * 0.62 * taper
            px = butt[0] + ux * s * ln - uz * w
            pz = butt[2] + uz * s * ln + ux * w
            rad = (0.36 + 0.24 * rng.next()) * (0.66 + 0.16 * ln)
            _cushion(b, (px, bough_y(s) + 0.10 + rng.next() * 0.16, pz),
                     rad, 0.30, rng, tile_m=0.55)
        # Ragged edge: small tufts sitting just outside the pad, at the bough's own height.
        for k in range(6):
            s = 0.55 + 0.90 * rng.next()
            a = rng.next() * math.tau
            w = math.sin(a) * 0.80 * ln * 0.62 + (rng.next() - 0.5) * 0.3
            px = butt[0] + ux * (s * ln + math.cos(a) * 0.35 * ln) - uz * w
            pz = butt[2] + uz * (s * ln + math.cos(a) * 0.35 * ln) + ux * w
            _cushion(b, (px, bough_y(s) + 0.08 + (rng.next() - 0.5) * 0.22, pz),
                     0.20 + 0.14 * rng.next(), 0.34, rng, tile_m=0.45)
    # crown - a smaller, flatter cap than the old dome, so the tree ends in a pine's blunt top
    ax, az = axis(1.0)
    for k in range(13):
        a = rng.next() * math.tau
        d = 0.25 + 0.85 * math.sqrt(rng.next())
        _cushion(b, (ax + math.cos(a) * d,
                     height * 0.80 + 0.18 + rng.next() * 0.60,
                     az + math.sin(a) * d),
                 0.34 + 0.22 * rng.next(), 0.42, rng, tile_m=0.50)
    parts.append(finish(b, f"{name}_canopy_needle", needle, smooth=True))

    obj = S.join(parts, name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


def build_broadleaf(name="Shiosai_Broadleaf", height=7.5, seed=17):
    """
    The lush evergreen broadleaf (tabunoki/shii) that actually dominates the concept renders'
    hillsides - renders 01/15 are far more broadleaf than pine, and a coastline of nothing but
    conifers was a large part of why the gameplay frame read as a placeholder.

    A short forked trunk carrying one big billowing canopy of leaf cushions.
    """
    print(f"[shiosai] building {name}...")
    rng = _Rng(seed)
    parts = []
    bark = mat(f"{name}_Trunk_Bark", (0.32, 0.27, 0.22), rough=0.95)
    leaf = mat(f"{name}_Leaf_Canopy", (0.26, 0.46, 0.22), rough=0.86)

    trunk_h = height * 0.42
    b = Builder()
    rings = []
    for k in range(7):
        t = k / 6
        r = 0.30 * (1.0 - 0.55 * t) + 0.03
        rings.append([(math.sin(t * 2.2) * 0.10 + math.cos(a / 8 * math.tau) * r, t * trunk_h,
                       math.sin(a / 8 * math.tau) * r) for a in range(8)])
    b.prism(rings, uv_v=0.6)
    forks = [(0.8, 1.5, 0.8), (2.9, 1.4, 0.9), (4.8, 1.3, 0.85)]
    tips = []
    for (ang, ln, rise) in forks:
        tip = (math.cos(ang) * ln, trunk_h + ln * rise, math.sin(ang) * ln)
        # FLOATING-PLANK FIX: the fork used to be a yaw-only `box` sized by the segment's 3D
        # length, i.e. a horizontal untapered slab that did not follow the fork it represented.
        # Same limb treatment as the pine boughs.
        b.limb((0.0, trunk_h, 0.0), tip, 0.105, 0.055)
        tips.append(tip)
    parts.append(finish(b, f"{name}_trunk", bark, smooth=False))

    # BILLOWING, NOT SPHERICAL (opening visual overhaul, milestone 2). The previous canopy was
    # 35 near-spherical blobs at squash 0.80-0.84, which is a recipe for exactly what the start
    # line showed: three or four big green balls. A broadleaf crown is a stack of overlapping
    # CLOUD LOBES - broad and flat-ish, of markedly different sizes, with a ragged, gappy rim.
    # More blobs, smaller and flatter, with a size spread and an outer rim of small tufts.
    b = Builder()
    for tip in tips:
        for k in range(16):
            a = rng.next() * math.tau
            d = 1.45 * math.sqrt(rng.next())
            # A wide size spread is what stops a cluster of equal blobs reading as a ball.
            rad = 0.30 + 0.46 * (rng.next() ** 1.6)
            _cushion(b, (tip[0] + math.cos(a) * d,
                         tip[1] + 0.28 + rng.next() * 1.10,
                         tip[2] + math.sin(a) * d),
                     rad, 0.58, rng, tile_m=0.60)
    # A fuller, higher centre mass so the crown domes rather than reading as three lumps.
    for k in range(14):
        a = rng.next() * math.tau
        d = 0.25 + 0.75 * rng.next()
        _cushion(b, (math.cos(a) * d, height * 0.78 + rng.next() * 0.95, math.sin(a) * d),
                 0.34 + 0.40 * (rng.next() ** 1.5), 0.62, rng, tile_m=0.60)
    # Ragged rim: small leaf clusters hanging past the crown's outline, so the silhouette
    # breaks up instead of ending on a clean curve.
    for k in range(18):
        a = rng.next() * math.tau
        d = 1.55 + 0.75 * rng.next()
        _cushion(b, (math.cos(a) * d,
                     height * (0.52 + 0.40 * rng.next()),
                     math.sin(a) * d),
                 0.20 + 0.18 * rng.next(), 0.56, rng, tile_m=0.48)
    parts.append(finish(b, f"{name}_canopy_leaf", leaf, smooth=True))

    obj = S.join(parts, name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


# --------------------------------------------------------------------------- 5. hydrangea
#
# REGION_REVIEW_TODO 12 (2026-09-26): the previous bush was built from unwelded Builder faces
# exported flat-shaded - 11-sided mophead domes and single-quad leaf blades - so every head and
# every leaf caught its own hard highlight and the bank read as faceted low-poly gems at road
# distance. This pass keeps the proven silhouette (a low leaf mound with bumpy mopheads seated
# down in it) but builds it the way Minato's boulevard blob() does: WELDED, SMOOTH-SHADED
# surfaces with the detail carried by smooth, correlated lumps (floret clusters) instead of
# facets. Structure is deliberately unchanged - one node, two material slots (leaf, bloom) in
# the same order, same material names - so the ~16k staged prefab instances in SakuraPass.unity
# and their per-bush RecolourHydrangea overrides pick the new mesh up on reimport with no
# re-stage. Budget ~2k tris per bush (background verge prop, no LOD ladder on the instances).


def _smooth_solid(builder, name, material):
    """Weld coincident verts, orient outward, triangulate caps and shade SMOOTH."""
    import bmesh
    obj = builder.mesh(name, smooth=True)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    # Collapsing a ring onto a pole leaves zero-area slivers; drop them before orienting.
    slivers = [f for f in bm.faces if f.calc_area() < 1e-9]
    if slivers:
        bmesh.ops.delete(bm, geom=slivers, context="FACES_ONLY")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.validate(verbose=False)
    obj.data.shade_smooth()
    S.assign_material(obj, material)
    return obj


def _smooth_leaves(builder, name, material):
    """
    Two-sided smooth leaf blades. Each blade is welded into one island, its winding made
    consistent and turned so the FRONT faces the sky, then a reversed copy is added as the
    underside (separate verts, so the two sides keep opposite smooth normals and a blade is
    never one-way invisible under back-face culling).
    """
    import bmesh
    obj = builder.mesh(name, smooth=True)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.faces.ensure_lookup_table()
    seen = set()
    for f in bm.faces:
        if f.index in seen:
            continue
        island, stack = [], [f]
        seen.add(f.index)
        while stack:
            g = stack.pop()
            island.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        nz = sum(g.normal.z * g.calc_area() for g in island)
        if nz < 0.0:
            bmesh.ops.reverse_faces(bm, faces=island)
    front = bm.faces[:]
    dup = bmesh.ops.duplicate(bm, geom=front + list({v for f in front for v in f.verts})
                              + list({e for f in front for e in f.edges}))
    back = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.reverse_faces(bm, faces=back)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.shade_smooth()
    S.assign_material(obj, material)
    return obj


def _mophead(b, centre, rad, rng, segs=14, rows=6):
    """
    One アジサイ MOPHEAD (a corymb): a slightly flattened ball whose surface is lumpy at
    floret-CLUSTER scale. The lumps are a smooth product of azimuth and latitude sines, so on a
    welded smooth-shaded surface they read as soft packed florets (and survive on the
    silhouette) rather than as the flat facets the old 11x4 flat-shaded head showed. The rings
    run from well below the equator (a rounded underside tucked into the foliage - no hard cap
    rim) up to a single crown pole.
    """
    cx, cy, cz = centre
    f1 = 6 + math.floor(rng.next() * 3.0)          # cluster lumps around the head
    f2 = f1 + 3 + math.floor(rng.next() * 2.0)
    p1, p2, p3 = rng.next() * math.tau, rng.next() * math.tau, rng.next() * math.tau
    amp = 0.11 + 0.04 * rng.next()
    squash = 0.80 + 0.14 * rng.next()               # heads are wider than tall
    lo = -1.05                                      # ~60 deg below the equator
    rings = [[(cx, cy + math.sin(lo) * rad * squash * 1.02, cz)] * segs]
    for r in range(rows + 1):
        t = r / rows
        phi = lo + 0.18 + t * (math.pi * 0.5 - lo - 0.30)
        cphi = max(0.0, math.cos(phi))
        yy = math.sin(phi) * rad * squash
        ring = []
        for s in range(segs):
            a = s / segs * math.tau + t * 0.55
            bump = (1.0
                    + amp * math.sin(a * f1 + p1) * math.sin(phi * 5.0 + p2)
                    + amp * 0.55 * math.sin(a * f2 - p3 + phi * 3.4))
            rr = rad * cphi * bump
            ring.append((cx + math.cos(a) * rr, cy + yy + rad * 0.05 * (bump - 1.0),
                         cz + math.sin(a) * rr))
        rings.append(ring)
    rings.append([(cx, cy + rad * squash * 1.01, cz)] * segs)
    # uv_u > 1 tiles the authored floret albedo a few times AROUND the head (one stretched tile
    # read as a flat fill colour in gameplay).
    b.prism(rings, cap_bottom=False, cap_top=False, uv_v=0.18, uv_u=2.0)


def _leaf_blade(b, a, inner, length, half, y0, pitch, droop, rib, cup, uv0=(0.0, 0.0), uvs=0.14):
    """
    One broad OVATE hydrangea leaf as a 3 x 2 quad grid: short petiole end, widest just past
    the middle, a blunt rounded tip, a raised midrib and slightly cupped edges. The blade runs
    radially outward from (inner, y0) with slope `pitch` (dy per metre) plus a tip `droop`.

    UVs cover only a small `uvs` window of the leaf-hedge albedo (at `uv0`): mapping the whole
    hedge texture onto each blade printed dozens of tiny leaves per leaf, which read as grass
    speckle; a small window reads as one leaf's worth of colour and vein variation.
    """
    ca, sa = math.cos(a), math.sin(a)
    widths = (0.38, 1.0, 0.86, 0.30)

    def P(along, across, lift):
        r = inner + along
        t = along / length
        y = y0 + pitch * along - droop * t * t + lift
        return (ca * r - sa * across, y, sa * r + ca * across)

    grid = []
    for i, wk in enumerate(widths):
        t = i / (len(widths) - 1)
        w = half * wk
        along = t * length
        grid.append([P(along, -w, -cup), P(along, 0.0, rib * math.sin(math.pi * min(t, 0.8)) + 0.003),
                     P(along, w, -cup)])
    n = len(widths) - 1
    for i in range(n):
        v0, v1 = i / n, (i + 1) / n
        for j in range(2):
            u0, u1 = j * 0.5, (j + 1) * 0.5
            b.add_face([grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]],
                       [(uv0[0] + u0 * uvs, uv0[1] + v0 * uvs), (uv0[0] + u1 * uvs, uv0[1] + v0 * uvs),
                        (uv0[0] + u1 * uvs, uv0[1] + v1 * uvs), (uv0[0] + u0 * uvs, uv0[1] + v1 * uvs)])


def build_hydrangea(name="Shiosai_Hydrangea", seed=11, heads_n=6, spread=1.0, tall=1.0):
    """
    アジサイ - the blue/purple flower the renders put along every coastal verge in this region.

    A low, ROUND leaf dome (three overlapping smooth cushions, not a pot-shaped stack) shingled
    with broad two-sided ovate leaves that follow the dome and flare past its rim, carrying
    `heads_n` lumpy mopheads that crown the top. Three tuned variants are exported (see
    BUILDERS) so a 16k-bush bank is not one silhouette repeated.
    """
    print(f"[shiosai] building {name}...")
    rng = _Rng(seed)
    leafm = mat(f"{name}_Leaf_Cluster", (0.18, 0.36, 0.17), rough=0.85)
    flower = mat(f"{name}_Petal_Bloom", (0.44, 0.44, 0.80), rough=0.62)

    # MOUND: a broad dome whose centre sits at ground level, so it reads as a shrub growing out
    # of the verge (the lower half is buried).
    b = Builder()
    Rh, Rv = 0.46 * spread, 0.38 * tall
    _cushion(b, (0.0, 0.0, 0.0), Rh, Rv / Rh, rng, segs=12, rings=4, tile_m=1.1)
    a0 = rng.next() * math.tau
    for k in range(2):
        a = a0 + k * math.pi * (0.75 + 0.4 * rng.next())
        rr = 0.26 * spread
        rad = (0.28 + 0.05 * rng.next()) * spread
        _cushion(b, (math.cos(a) * rr, 0.0, math.sin(a) * rr), rad,
                 (0.28 * tall) / rad, rng, segs=11, rings=4, tile_m=1.1)
    mound = _smooth_solid(b, f"{name}_mound", leafm)

    # LEAVES: shingled over the dome in three latitude bands, each blade standing a little
    # proud of the surface (flatter than the local slope) and flaring outward past the rim.
    b = Builder()
    # (count, dome latitude, blade length, blade slope). Every slope is FLATTER than the dome
    # surface at that latitude, so blades flare outward like a real shrub instead of hanging
    # down the sides as a grass skirt.
    for (count, lat, length0, pitch0) in ((14, 0.18, 0.28, -0.28), (9, 0.60, 0.23, -0.45),
                                          (5, 1.00, 0.18, -0.20)):
        for i in range(count):
            a = i / count * math.tau + rng.next() * 0.5 + lat
            e = lat + (rng.next() - 0.5) * 0.18
            inner = Rh * math.cos(e) * 0.92
            y0 = Rv * math.sin(e) * 0.95
            pitch = pitch0 + (rng.next() - 0.5) * 0.25
            _leaf_blade(b, a, inner=inner, length=(length0 + 0.10 * rng.next()) * spread,
                        half=(0.085 + 0.035 * rng.next()) * spread, y0=y0, pitch=pitch,
                        droop=0.05 + 0.07 * rng.next(), rib=0.016 + 0.010 * rng.next(),
                        cup=0.012 + 0.008 * rng.next(), uv0=(rng.next() * 0.85, rng.next() * 0.85))
    leaves = _smooth_leaves(b, f"{name}_leaves", leafm)

    # BLOOMS: crowning the dome, seated so the lower third of each head is in the foliage.
    b = Builder()
    a0 = rng.next() * math.tau
    for k in range(heads_n):
        a = a0 + k / heads_n * math.tau + (rng.next() - 0.5) * 0.5
        r = (0.08 + 0.24 * rng.next()) * spread
        rad = (0.175 + 0.070 * rng.next()) * spread
        # ride the dome: heads further out sit lower, following the curve
        y = Rv * math.sqrt(max(0.0, 1.0 - (r / Rh) ** 2)) * 0.92 + (rng.next() - 0.3) * 0.05 * tall
        _mophead(b, (math.cos(a) * r, y, math.sin(a) * r), rad, rng)
    bloom = _smooth_solid(b, f"{name}_bloom_petal", flower)

    # Join order keeps the LEAF slot first and the BLOOM slot second, exactly as before, so the
    # staged instances' m_Materials[0]/[1] overrides still land on the right surfaces.
    obj = S.join([mound, leaves, bloom], name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj

# --------------------------------------------------------------------------- 6. sea stack

def build_sea_stack(name, height, radius, seed, with_pines=True):
    """
    An offshore 奇岩 sea stack: a jagged, undercut rock spire with a green pine tuft on top -
    the silhouette that appears in almost every one of the 20 renders.

    Origin at y = 0 == WATERLINE, so Unity drops it straight onto sea level.
    """
    print(f"[shiosai] building {name}...")
    rng = _Rng(seed)
    parts = []
    rock = mat(f"{name}_Rock_Stack", (0.50, 0.47, 0.43), rough=0.95)
    grass = mat(f"{name}_Needle_Canopy", (0.20, 0.38, 0.20), rough=0.88)
    foam = mat(f"{name}_Foam_Wash", (0.95, 0.97, 0.97), rough=0.80)

    b = Builder()
    n = 56
    rings = []
    # the profile is wider at the waterline, undercut just above it, then tapers with a
    # near-vertical shoulder: that undercut is what makes it read as wave-cut rock
    # M4b: the first M4 pass kept the stock 1.48:1 height:width ratio and every stack still
    # rendered as a gumdrop. The references are spires - roughly 2.5:1 - so the radii at the
    # call site were cut, and the profile itself was sharpened: the shoulder pulls in harder
    # and a per-stack random jitter on each key stops the three assets sharing one outline.
    # PROVISIONAL tuning.
    coarse_keys = [(-2.5, 1.30), (0.0, 1.12), (0.12, 0.80), (0.30, 0.88), (0.50, 0.66),
                   (0.68, 0.58), (0.82, 0.34), (0.93, 0.22), (1.0, 0.07)]
    coarse_keys = [(t, r if t <= 0.0 else r * (0.88 + 0.24 * rng.next()))
                   for (t, r) in coarse_keys]
    # Densify vertically too: the rock texture is authored to be seen tiled many times across a
    # continuous surface (the terrain ribbons). A handful of big flat rings each showing one
    # smoothly-interpolated slab of that texture is what reads as blotchy camouflage/static
    # instead of fine painterly grain - more rings (like more circumferential segments) let the
    # same physical UV scale actually resolve into small tiles instead of a few giant ones.
    keys = []
    for i in range(len(coarse_keys) - 1):
        t0, r0 = coarse_keys[i]
        t1, r1 = coarse_keys[i + 1]
        steps = 3 if i > 0 else 1   # keep the below-waterline key untouched
        for k in range(steps):
            f = k / steps
            keys.append((t0 + (t1 - t0) * f, r0 + (r1 - r0) * f))
    keys.append(coarse_keys[-1])
    # A per-vertex INDEPENDENT random phase (one uncorrelated angle per index `s`) makes
    # neighbouring ring points jump to unrelated radii with every ~16 degree step. `prism()`
    # simply connects adjacent ring points with a quad, so that per-vertex noise turns the
    # silhouette into a zigzag star instead of a rock outline - once those quads are connected
    # to the next ring up/down (which has its own uncorrelated jump) the surface self-intersects
    # and reads as a hollow woven "birdcage"/basket, not solid stone. Real columnar-jointed rock
    # has a handful of coherent vertical ridges and gullies, i.e. a LOW-FREQUENCY, smoothly
    # varying silhouette around the circumference - not per-vertex noise. Build that with a
    # small sum of sine harmonics evaluated at the continuous angle `a` (so neighbouring samples
    # are always close together), each harmonic's own phase/weight randomised once per stack.
    # Keep every harmonic well above the Nyquist limit for `n` angular samples (>= ~8 samples
    # per period) or the "smooth" sine itself aliases back into per-vertex-looking noise on the
    # discrete ring - that is what silently reintroduced the birdcage look the first time this
    # was "fixed" with harmonics up to k=7 against only n=22 samples (~3 samples/period).
    # M4: with n = 56 angular samples the Nyquist-safe ceiling (>= 8 samples per period) rises
    # from k=4 to k=7, so the silhouette can carry real buttresses and gullies instead of the
    # near-smooth candle the k=(2,3,4) set produced. The amplitude is raised to match: at 0.12
    # the stacks read as cones in every reference comparison.
    harmonics = [(k, 0.4 + 0.6 * rng.next(), rng.next() * math.tau) for k in (2, 3, 4, 5, 7)]
    harm_total = sum(amp for (_, amp, _) in harmonics) or 1.0

    # PROVISIONAL. Per-stack silhouette strength and a lean, so a cluster of stacks is not a
    # row of identical spires.
    jag_amp = 0.20 + 0.10 * rng.next()
    lean_a = rng.next() * math.tau
    lean_r = (0.04 + 0.06 * rng.next()) * radius
    # Horizontal bedding: a slow radius ripple up the column that reads as wave-cut ledges,
    # which is the note that separates a sea stack from a rock cone in the references.
    ledge_k = 5.0 + 4.0 * rng.next()
    ledge_ph = rng.next() * math.tau

    def jag_at(a, t):
        v = 0.0
        for (k, amp, ph) in harmonics:
            v += amp * math.sin(k * a + ph + t * 0.35)
        v /= harm_total
        ledge = 0.055 * math.sin(t * ledge_k + ledge_ph)
        return 1.0 + jag_amp * v + ledge

    for (t, rscale) in keys:
        y = t * height
        ring = []
        # Lean grows with height so the spire tilts like a wave-undercut stack rather than
        # standing perfectly plumb.
        lx = math.cos(lean_a) * lean_r * max(0.0, t)
        lz = math.sin(lean_a) * lean_r * max(0.0, t)
        for s in range(n):
            a = s / n * math.tau
            jag = jag_at(a, t)
            rr = radius * rscale * jag
            ring.append((lx + math.cos(a) * rr, y, lz + math.sin(a) * rr))
        rings.append(ring)
    b.prism(rings, cap_bottom=True, cap_top=True,
            uv_v=(height / 8.0) / (len(keys) - 1), uv_u=max(1.0, (math.tau * radius) / 8.0))
    parts.append(finish(b, f"{name}_rock", rock, smooth=False))

    # ---- foam wash at the waterline -------------------------------------------------------
    # PROVISIONAL. Every reference shows white water breaking round the foot of a stack; without
    # it the spire looks pasted onto the sea. A shallow, irregular collar just above the
    # waterline, built as part of the stack so it travels with it.
    #
    # M4b: the first version of this collar was a near-vertical prism skirt sitting at local
    # y = 0.06..0.16. Two things killed it in the render:
    #   * Unity seats the stack at SeaLevelY - 0.6, so anything under local y = 0.6 (at unit
    #     scale) is BELOW the ocean plane - the entire collar was under water and invisible.
    #     What read as a "tan skirt" in the render was just the stack's own wave-cut foot.
    #   * a vertical skirt's normals point sideways/down, so even above water a cel-shaded
    #     white would have shaded dark rather than reading as bright foam.
    # A flat, UP-FACING annulus floating just proud of the waterline fixes both: upward normals
    # take the full sun so it renders as clean white water, and it reads as a wash spreading
    # away from the rock exactly like the references.
    b = Builder()
    FOAM_Y = 0.70          # PROVISIONAL: local metres, must clear the SeaLevelY-0.6 seat
    FOAM_Y_OUTER = 0.58    # outer rim droops toward the water so the wash feathers out

    def foam_r(a, k):
        # M4c: a nearly-round collar at 1.95x radius rendered as a hard-edged white "paper
        # doily"/ice floe round each spire. Real broken water is a tight, ragged ring, so the
        # reach is cut and the low-order wobble raised until the outline is visibly irregular.
        wob = (1.0 + 0.30 * math.sin(3.0 * a + ledge_ph) + 0.17 * math.sin(5.0 * a + lean_a)
               + 0.09 * math.sin(7.0 * a + ledge_ph * 1.7))
        return radius * k * wob

    for s in range(n):
        a0 = s / n * math.tau
        a1 = (s + 1) / n * math.tau
        # inner edge tucks just INSIDE the rock silhouette at the waterline so there is never a
        # gap between the wash and the stack
        ri0 = radius * 1.10 * jag_at(a0, 0.0) * 0.94
        ri1 = radius * 1.10 * jag_at(a1, 0.0) * 0.94
        ro0, ro1 = foam_r(a0, 1.28), foam_r(a1, 1.28)
        b.add_face([(math.cos(a0) * ri0, FOAM_Y, math.sin(a0) * ri0),
                    (math.cos(a0) * ro0, FOAM_Y_OUTER, math.sin(a0) * ro0),
                    (math.cos(a1) * ro1, FOAM_Y_OUTER, math.sin(a1) * ro1),
                    (math.cos(a1) * ri1, FOAM_Y, math.sin(a1) * ri1)],
                   [(s / n * 4.0, 0.0), (s / n * 4.0, 1.0),
                    ((s + 1) / n * 4.0, 1.0), ((s + 1) / n * 4.0, 0.0)])
    parts.append(finish(b, f"{name}_foam_wash", foam, smooth=True))

    if with_pines:
        # Real miniature black pines, built from the same cushion canopy as Shiosai_Pine. The
        # first gameplay frame of this pass still showed bright sharp CONE tufts on every sea
        # stack, because this builder kept its own spoke-and-apex placeholder long after the
        # roadside pines were rebuilt - the one bit of "paper party hat" left in the region.
        b = Builder()
        top_r = radius * 0.24
        for i in range(3):
            a = i / 3 * math.tau + 0.5
            cx, cz = math.cos(a) * top_r, math.sin(a) * top_r
            y0 = height * 0.97
            trunk_h = height * 0.10 + 0.5
            rings = []
            for k in range(5):
                t = k / 4
                r = 0.14 * (1.0 - 0.6 * t) + 0.02
                rings.append([(cx + math.cos(s / 7 * math.tau) * r, y0 + t * trunk_h,
                               cz + math.sin(s / 7 * math.tau) * r) for s in range(7)])
            b.prism(rings, uv_v=0.5)
            # A flat, wind-pruned crown: wide relative to its height, exactly the silhouette the
            # concept renders give the stack-top pines.
            crown_r = top_r * 1.6 + 0.6
            for k in range(11):
                ang = rng.next() * math.tau
                d = crown_r * (0.15 + 0.85 * rng.next())
                _cushion(b, (cx + math.cos(ang) * d,
                             y0 + trunk_h + (rng.next() - 0.3) * 0.35,
                             cz + math.sin(ang) * d),
                         0.34 + 0.22 * rng.next(), 0.44, rng, tile_m=0.55)
        parts.append(finish(b, f"{name}_canopy_needle", grass, smooth=True))

    obj = S.join(parts, name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


# --------------------------------------------------------------------------- 7. harbour town

# PROVISIONAL machiya/minka roof tunables, authored against ShiosaiCoast_06.png. The roof is
# the single strongest signal of "Japanese port town" vs "box on a lawn", so it is angle-driven
# rather than a fixed metre height: the pitch must survive every house footprint in the kit.
ROOF_PITCH_DEG = 29.0
ROOF_EAVE_M = 0.95        # deep eaves; the shadow line under them is most of the look
ROOF_TILE_COURSES = 9     # pantile batten courses per slope


def build_harbour_house(name, width, depth, storeys, seed, roof_colour):
    """
    A coastal-town house from the renders: cream/white rendered walls, a dark tiled hipped roof
    with deep eaves, and a first-floor balcony. Deliberately plain and uniform - Japanese
    townscape ordinances (16 m cap, <= 3 colours, low chroma) are what make a real harbour town
    read as one place instead of as a pile of unrelated boxes.
    """
    print(f"[shiosai] building {name}...")
    rng = _Rng(seed)
    parts = []
    wall = mat(f"{name}_Wall_Render", (0.86, 0.84, 0.79), rough=0.86)
    roofm = mat(f"{name}_Roof_Tile", roof_colour, rough=0.72)
    trim = mat(f"{name}_Trim_Wood", (0.34, 0.26, 0.20), rough=0.88)

    storey_h = 3.0
    body_h = storeys * storey_h
    hw, hd = width * 0.5, depth * 0.5

    b = Builder()
    b.box((0.0, body_h * 0.5, 0.0), (width, body_h, depth), uv_scale=0.35)
    parts.append(finish(b, f"{name}_walls", wall, smooth=False, bevel=0.04))

    # hipped roof: a truncated pyramid with 0.55 m eaves
    b = Builder()
    eave = 0.55
    ridge_h = 1.5 + 0.4 * rng.next()
    r0 = [(-hw - eave, body_h, -hd - eave), (hw + eave, body_h, -hd - eave),
          (hw + eave, body_h, hd + eave), (-hw - eave, body_h, hd + eave)]
    r1 = [(-hw - eave, body_h + 0.22, -hd - eave), (hw + eave, body_h + 0.22, -hd - eave),
          (hw + eave, body_h + 0.22, hd + eave), (-hw - eave, body_h + 0.22, hd + eave)]
    rk = hw * 0.30
    r2 = [(-rk, body_h + ridge_h, -rk * 0.4), (rk, body_h + ridge_h, -rk * 0.4),
          (rk, body_h + ridge_h, rk * 0.4), (-rk, body_h + ridge_h, rk * 0.4)]
    b.prism([r0, r1, r2], cap_bottom=True, cap_top=True, uv_v=0.5)
    parts.append(finish(b, f"{name}_roof", roofm, smooth=False))

    # balcony + window frames: the trim that stops it reading as an extruded block
    b = Builder()
    if storeys >= 2:
        b.box((0.0, storey_h + 0.05, hd + 0.42), (width * 0.82, 0.14, 0.90))
        b.box((0.0, storey_h + 0.55, hd + 0.85), (width * 0.82, 1.00, 0.08))
    for s in range(storeys):
        y = s * storey_h + 1.7
        for k in (-1, 1):
            b.box((k * width * 0.26, y, hd + 0.03), (0.96, 1.20, 0.08))
            b.box((hw + 0.03, y, 0.0), (0.08, 1.20, 0.96))
    parts.append(finish(b, f"{name}_trim", trim, smooth=False))

    obj = S.join(parts, name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


def build_harbour_benchmark_house(name, width, depth, storeys, seed, roof_colour, facade_colour):
    """A higher-detail Hokkaido fishing-town house for the Shiosai harbour benchmark.

    This intentionally remains modular and inexpensive (one joined export per house type), but
    gives the town the silhouettes and shadow breaks missing from the original placeholder kit:
    raised concrete bases, deep snow/rain eaves, dark recessed windows, timber framing, shutters,
    a street-facing entry, balcony rails, and a tiny service awning.  The old House_A/B/C exports
    remain untouched so the benchmark is reversible.
    """
    print(f"[shiosai] building benchmark harbour house {name}...")
    rng = _Rng(seed)
    parts = []
    wall = mat(f"{name}_Wall_Render", facade_colour, rough=0.84)
    roofm = mat(f"{name}_Roof_Tile", roof_colour, rough=0.72)
    trim = mat(f"{name}_Trim_Wood", (0.22, 0.16, 0.12), rough=0.90)
    glass = mat(f"{name}_Window_Glass", (0.055, 0.10, 0.14), rough=0.25, metallic=0.08)
    concrete = mat(f"{name}_Foundation_Concrete", (0.48, 0.47, 0.44), rough=0.92)

    storey_h = 2.85
    body_h = storeys * storey_h
    hw, hd = width * 0.5, depth * 0.5

    # Raised plinth and wall volume. The plinth's darker value anchors buildings into the
    # terraced slope and keeps the village from reading as floating paper boxes.
    # "BLOCKY HOUSES" FIX: the plinth was only 0.125 m proud of the wall, which at village
    # distance is sub-pixel - so the house met the grass as one unbroken vertical edge, the
    # single strongest "toy block" cue. A 0.30 m offset casts its own shadow line and reads as
    # a real foundation from the road. PROVISIONAL.
    b = Builder()
    b.box((0.0, 0.33, 0.0), (width + 0.60, 0.66, depth + 0.60), uv_scale=0.28)
    parts.append(finish(b, f"{name}_foundation", concrete, smooth=False, bevel=0.04))

    b = Builder()
    b.box((0.0, 0.60 + body_h * 0.5, 0.0), (width, body_h, depth), uv_scale=0.36)
    parts.append(finish(b, f"{name}_walls", wall, smooth=False, bevel=0.045))

    # ---- KAWARA HIP ROOF (瓦屋根) -----------------------------------------------------------
    # The old roof was a truncated pyramid only 1.25 m tall on a 7.4 m deep house - a ~10 deg
    # pitch with a wide flat top, which is precisely why the town read as "blocky Roblox cubes":
    # from any normal camera angle a 10 deg roof is a flat lid. A real machiya/minka roof in a
    # Japanese fishing port is a 28-32 deg hip (yosemune) with a LINE ridge, deep eaves, heavy
    # ridge tiles (munagawara) and visible pantile courses.
    #
    # All PROVISIONAL. Reference: Assets/Environment/ShiosaiCoast/ShiosaiCoast_06.png.
    b = Builder()
    eave = ROOF_EAVE_M
    roof_y = 0.60 + body_h
    # Pitch is authored as an ANGLE, so the roof stays a roof at every house size.
    pitch = math.radians(ROOF_PITCH_DEG + 2.5 * rng.next())
    ridge_h = math.tan(pitch) * (hd + eave)
    # A hip roof's ridge is the plan rectangle shortened by the depth on each end -> a LINE.
    ridge_hl = max(hw * 0.28, hw - hd * 0.82)

    r0 = [(-hw - eave, roof_y, -hd - eave), (hw + eave, roof_y, -hd - eave),
          (hw + eave, roof_y, hd + eave), (-hw - eave, roof_y, hd + eave)]
    r1 = [(-hw - eave, roof_y + 0.20, -hd - eave), (hw + eave, roof_y + 0.20, -hd - eave),
          (hw + eave, roof_y + 0.20, hd + eave), (-hw - eave, roof_y + 0.20, hd + eave)]
    r2 = [(-ridge_hl, roof_y + ridge_h, -0.13), (ridge_hl, roof_y + ridge_h, -0.13),
          (ridge_hl, roof_y + ridge_h, 0.13), (-ridge_hl, roof_y + ridge_h, 0.13)]
    b.prism([r0, r1, r2], cap_bottom=True, cap_top=True, uv_v=0.45)

    # Munagawara: the heavy capped ridge that gives a Japanese roof its silhouette.
    b.box((0.0, roof_y + ridge_h + 0.17, 0.0), (ridge_hl * 2.0 + 0.34, 0.34, 0.58))
    b.box((0.0, roof_y + ridge_h + 0.37, 0.0), (ridge_hl * 2.0 + 0.10, 0.12, 0.40))

    # Pantile courses on the two long slopes. Thin battens stepping up the rake, just proud of
    # the slope, so the roof reads as laid tile rather than a painted triangle - this is what
    # carries the "kawara" read at ride distance and in the overlook.
    for c in range(1, ROOF_TILE_COURSES):
        t = c / float(ROOF_TILE_COURSES)
        y = roof_y + 0.20 + (ridge_h - 0.20) * t
        z = (hd + eave) * (1.0 - t)
        run = (hw + eave) * 2.0 - (hd + eave - z) * 1.55
        if run <= 0.5:
            continue
        for sgn in (1.0, -1.0):
            b.box((0.0, y + 0.045, sgn * (z - 0.05)), (run, 0.09, 0.14))
    parts.append(finish(b, f"{name}_roof", roofm, smooth=False, bevel=0.025))

    # Timber, windows, door, rails and service awning.  All panels sit just proud of the facade
    # so they retain their own material/shadow response after glTF import.
    #
    # ARTICULATION ON ALL FOUR WALLS ("blocky/Roblox houses" fix). Everything below used to be
    # authored on the +Z street face only, so the other three elevations were bare slabs. From
    # the ch4 village camera most of the town is seen in three-quarter or from behind, which is
    # exactly why it read as a heap of identical featureless boxes: the detail existed, but not
    # on the faces that were actually visible. The floor band now runs right round the house,
    # all four corners get a post, and the rear and the second side get their own window group.
    b = Builder()
    w = 0.95
    for s in range(storeys):
        y = 0.60 + s * storey_h
        # Horizontal weatherboard/frame line at each floor - all four elevations.
        b.box((0.0, y + 0.08, hd + 0.035), (width + 0.08, 0.11, 0.09))
        b.box((0.0, y + 0.08, -hd - 0.035), (width + 0.08, 0.11, 0.09))
        b.box((hw + 0.035, y + 0.08, 0.0), (0.09, 0.11, depth + 0.08))
        b.box((-hw - 0.035, y + 0.08, 0.0), (0.09, 0.11, depth + 0.08))
        # Corner posts on all four corners: the vertical break that stops a wall reading as a
        # single untextured quad at village distance.
        for sx in (-1.0, 1.0):
            for sz in (-1.0, 1.0):
                b.box((sx * (hw - 0.05), y + storey_h * 0.50, sz * (hd + 0.04)),
                      (0.11, storey_h - 0.20, 0.10))
        # street-facing window frames; glazing is a separate object/material below.
        for x in (-width * 0.28, width * 0.28):
            b.box((x, y + 1.62, hd + 0.075), (w + 0.13, 1.28, 0.07))
            b.box((x, y + 1.62, hd + 0.125), (0.07, 1.42, 0.11))
            b.box((x, y + 1.62, hd + 0.125), (w + 0.19, 0.07, 0.11))
        # REAR elevation window group - the face the hillside rows present to the road.
        for x in (-width * 0.26, width * 0.26):
            b.box((x, y + 1.58, -hd - 0.075), (w + 0.13, 1.20, 0.07))
            b.box((x, y + 1.58, -hd - 0.125), (0.07, 1.34, 0.11))
        # Side window group breaks the long blank sides that exposed the old kit's box shape.
        b.box((hw + 0.04, y + 1.54, -depth * 0.18), (0.08, 1.18, 1.05))
        b.box((-hw - 0.04, y + 1.54, depth * 0.16), (0.08, 1.18, 0.95))
    # Entry porch and modest rain awning on ground floor.
    b.box((0.0, 1.42, hd + 0.07), (1.15, 2.12, 0.10))
    b.box((0.0, 2.62, hd + 0.56), (1.90, 0.12, 1.12))
    b.box((-0.78, 2.10, hd + 0.56), (0.10, 1.05, 0.10))
    b.box((0.78, 2.10, hd + 0.56), (0.10, 1.05, 0.10))
    if storeys >= 2:
        balcony_y = 0.60 + storey_h + 0.08
        b.box((0.0, balcony_y, hd + 0.46), (width * 0.76, 0.14, 0.90))
        for k in range(-4, 5):
            b.box((k * width * 0.075, balcony_y + 0.50, hd + 0.84), (0.06, 0.94, 0.06))
        b.box((0.0, balcony_y + 0.96, hd + 0.84), (width * 0.70, 0.06, 0.06))
    parts.append(finish(b, f"{name}_trim", trim, smooth=False, bevel=0.015))

    b = Builder()
    for s in range(storeys):
        y = 0.60 + s * storey_h
        for x in (-width * 0.28, width * 0.28):
            b.box((x, y + 1.62, hd + 0.125), (w, 1.15, 0.035))
        for x in (-width * 0.26, width * 0.26):
            b.box((x, y + 1.58, -hd - 0.125), (w, 1.08, 0.035))
        b.box((hw + 0.09, y + 1.54, -depth * 0.18), (0.035, 1.05, 0.92))
        b.box((-hw - 0.09, y + 1.54, depth * 0.16), (0.035, 1.05, 0.82))
    # The door is intentionally a dark timber/glass recess rather than a painted rectangle.
    b.box((0.0, 1.42, hd + 0.13), (0.98, 2.00, 0.035))
    parts.append(finish(b, f"{name}_window_glass", glass, smooth=False))

    obj = S.join(parts, name)
    S.shade_auto_smooth(obj, angle_deg=42.0)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


def build_breakwater(name="Shiosai_Breakwater"):
    """A run of the harbour mole: concrete caisson with a tetrapod armour toe."""
    print(f"[shiosai] building {name}...")
    rng = _Rng(29)
    parts = []
    conc = mat(f"{name}_Concrete_Deck", (0.68, 0.67, 0.64), rough=0.92)
    armour = mat(f"{name}_Concrete_Armour", (0.60, 0.59, 0.56), rough=0.95)

    b = Builder()
    b.box((0.0, 1.1, 0.0), (12.0, 2.2, 5.0), uv_scale=0.3)      # caisson, along local +X
    b.box((0.0, 2.55, -2.1), (12.0, 0.7, 0.8))                   # parapet
    parts.append(finish(b, f"{name}_deck", conc, smooth=False, bevel=0.06))

    b = Builder()
    for i in range(9):
        x = -5.6 + i * 1.4
        z = 3.0 + rng.next() * 1.6
        y = 0.55 + rng.next() * 0.5
        for k in range(4):
            a = k / 4 * math.tau + rng.next()
            b.box((x + math.cos(a) * 0.5, y, z + math.sin(a) * 0.5),
                  (1.1, 0.5, 0.5), yaw_deg=math.degrees(a) * 0.7)
    parts.append(finish(b, f"{name}_armour", armour, smooth=False))

    obj = S.join(parts, name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


# --------------------------------------------------------- 10b. tetrapod armour unit

def build_tetrapod(name="Shiosai_Tetrapod"):
    """
    A single concrete TETRAPOD - the four-legged wave-dissipating armour unit that lines the
    outer face of every Japanese harbour mole, and the thing the art-director reference
    (ShiosaiCoast_06.png) shows piled along the breakwater.

    Geometry: four truncated cones radiating from a common hub along tetrahedral axes - one
    leg straight up, three splayed down and out at ~19.5 degrees below horizontal, which is
    what makes a heap of them interlock. Authored in UNITY metres through S.u2b via Builder,
    origin at the BASE so the staging pass can drop it straight onto the mole toe.

    Real units run 2-50 t; this one is ~3.4 m across, i.e. the small harbour size.
    """
    print(f"[shiosai] building {name}...")
    conc = mat(f"{name}_Concrete_Armour", (0.60, 0.59, 0.56), rough=0.95)

    LEG = 1.55          # hub -> tip, metres
    R_HUB = 0.62        # leg radius at the hub
    R_TIP = 0.40        # leg radius at the foot pad
    SEGS = 7

    # Tetrahedral axes: +Y, and three at y = -1/3 spaced 120 deg apart.
    axes = [(0.0, 1.0, 0.0)]
    for k in range(3):
        a = k / 3.0 * math.tau + 0.4
        r = math.sqrt(8.0) / 3.0
        axes.append((math.cos(a) * r, -1.0 / 3.0, math.sin(a) * r))

    # Sit the three down-legs' feet on y = 0.
    hub_y = LEG / 3.0 + R_TIP * 0.55

    b = Builder()
    for (ax, ay, az) in axes:
        d = (ax, ay, az)
        # Two vectors perpendicular to d, for the ring.
        up = (0.0, 0.0, 1.0) if abs(ay) > 0.9 else (0.0, 1.0, 0.0)
        ux = (d[1] * up[2] - d[2] * up[1], d[2] * up[0] - d[0] * up[2], d[0] * up[1] - d[1] * up[0])
        ul = math.sqrt(sum(c * c for c in ux)) or 1.0
        ux = tuple(c / ul for c in ux)
        uy = (d[1] * ux[2] - d[2] * ux[1], d[2] * ux[0] - d[0] * ux[2], d[0] * ux[1] - d[1] * ux[0])

        rings = []
        for (t, rr) in ((0.0, R_HUB), (0.62, R_HUB * 0.72 + R_TIP * 0.28), (1.0, R_TIP),
                        (1.10, R_TIP * 0.80)):
            cx = d[0] * LEG * t
            cy = hub_y + d[1] * LEG * t
            cz = d[2] * LEG * t
            ring = []
            for s in range(SEGS):
                a = s / SEGS * math.tau
                ca, sa = math.cos(a) * rr, math.sin(a) * rr
                ring.append((cx + ux[0] * ca + uy[0] * sa,
                             cy + ux[1] * ca + uy[1] * sa,
                             cz + ux[2] * ca + uy[2] * sa))
            rings.append(ring)
        # No hub cap: the four legs' hub rings all overlap inside the body, which is exactly
        # how a cast tetrapod is shaped and saves four buried n-gons.
        b.prism(rings, cap_bottom=False, cap_top=True, uv_v=0.5, uv_u=1.0)

    # A small casting hub so the four legs read as one poured unit rather than four sticks.
    b.tube([(0.74, hub_y - 0.42), (0.86, hub_y), (0.74, hub_y + 0.42)], segments=8)

    obj = finish(b, name, conc, smooth=False, bevel=0.035)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


def build_boat(name="Shiosai_FishingBoat"):
    """A small white harbour fishing boat: hull, wheelhouse, mast. Origin at the waterline."""
    print(f"[shiosai] building {name}...")
    parts = []
    hull = mat(f"{name}_Hull_White", (0.88, 0.88, 0.86), rough=0.55)
    deck = mat(f"{name}_Deck_Trim", (0.20, 0.32, 0.52), rough=0.60)

    b = Builder()
    # hull: 5 stations, pointed bow at +Z, transom at -Z, tucked keel below the waterline
    stations = [(-3.2, 0.95), (-1.0, 1.18), (1.2, 1.15), (3.0, 0.80), (4.4, 0.12)]
    rings = []
    for level, (yb, wscale) in enumerate([(-0.75, 0.42), (-0.25, 0.86), (0.55, 1.0)]):
        ring = []
        for (z, w) in stations:
            ring.append((-w * wscale, yb, z))
        for (z, w) in reversed(stations):
            ring.append((w * wscale, yb, z))
        rings.append(ring)
    b.prism(rings, cap_bottom=True, cap_top=False, uv_v=0.4)
    parts.append(finish(b, f"{name}_hull", hull, smooth=False))

    b = Builder()
    b.box((0.0, 0.60, -1.3), (1.9, 0.10, 3.8))         # deck
    b.box((0.0, 1.45, -0.6), (1.7, 1.60, 2.1))         # wheelhouse
    b.box((0.0, 3.2, -0.6), (0.09, 2.0, 0.09))         # mast
    parts.append(finish(b, f"{name}_super", deck, smooth=False))

    obj = S.join(parts, name)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


def build_red_bridge(name="Shiosai_RedBridge", span=170.0, rise=30.0):
    """
    Ch5 hero: the coral-red THROUGH-ARCH bridge of reference render 05.

    Authored along local +Z (the road's forward direction) with the ORIGIN AT DECK LEVEL, on the
    centreline, at mid-span - so Unity seats it by putting the origin on the published centreline
    at SC_KM_215_BridgeMidpoint and yawing by atan2(T.x, T.z). Nothing about the deck height is
    baked in: the deck slab hangs just BELOW y = 0 so it can never z-fight the swept carriageway
    the rider actually rides on.

    The arch springs from abutments at deck level and rises ABOVE the deck, which is what makes
    the landmark read from a kilometre out and from the rider's own eye line as they cross it.
    Spec 3.2 calls the landmark red 'coral vermilion' and 3.3 warns it must be a high-chroma
    ACCENT, not the dominant colour - so the ribs, portal bracing and parapet are coral and
    everything structural under the deck is concrete.
    """
    print(f"[shiosai] building {name}...")
    parts = []
    # Named so ShiosaiCoastEnvironment.CoastMaterialFor can route them: "bridgecoral" and
    # "bridgedeck" are matched explicitly and BEFORE the generic red/concrete rules.
    coral = mat(f"{name}_BridgeCoral", (0.62, 0.20, 0.15), rough=0.52)
    conc = mat(f"{name}_BridgeDeck_Concrete", (0.68, 0.67, 0.64), rough=0.90)

    half = span * 0.5
    rib_x = 6.3        # arch rib centreline, outboard of the 7 m carriageway + footway
    rail_x = 5.45      # parapet line
    deck_half_w = 6.6

    def arch_y(z):
        t = z / half
        return rise * (1.0 - t * t)

    # ---- the two arch ribs ---------------------------------------------------------------
    b = Builder()
    steps = 40
    hw, ht = 0.95, 1.70          # rib section: width across the road, depth along the arch normal
    for sgn in (-1, 1):
        rings = []
        for k in range(steps + 1):
            t = k / steps
            z = -half + span * t
            y = arch_y(z)
            # arch tangent in the (y, z) plane, so the section stays perpendicular to the curve
            dz = 1.0
            dy = -2.0 * rise * z / (half * half)
            n = math.hypot(dy, dz)
            ty, tz = dy / n, dz / n
            ny, nz = tz, -ty
            cx = sgn * rib_x
            ring = [
                (cx + hw, y + ny * ht, z + nz * ht),
                (cx - hw, y + ny * ht, z + nz * ht),
                (cx - hw, y - ny * ht, z - nz * ht),
                (cx + hw, y - ny * ht, z - nz * ht),
            ]
            rings.append(ring)
        b.prism(rings, close=True, uv_v=0.35)

    # ---- hangers: arch down to deck ------------------------------------------------------
    k = 0
    while True:
        z = -half + 12.0 + k * 12.0
        k += 1
        if z > half - 12.0:
            break
        y = arch_y(z)
        if y < 3.0:
            continue
        for sgn in (-1, 1):
            b.box((sgn * rib_x, y * 0.5, z), (0.42, y, 0.42))

    # ---- portal bracing across the top, well clear of the carriageway --------------------
    for z in (-42.0, -21.0, 0.0, 21.0, 42.0):
        y = arch_y(z)
        if y < 11.0:
            continue
        b.box((0.0, y, z), (rib_x * 2 + hw * 2, 1.05, 1.5))
    # X-braces between the portals
    for (z0, z1) in ((-42.0, -21.0), (-21.0, 0.0), (0.0, 21.0), (21.0, 42.0)):
        y0, y1 = arch_y(z0), arch_y(z1)
        if min(y0, y1) < 11.0:
            continue
        for d in (1, -1):
            za, ya = (z0, y0) if d > 0 else (z1, y1)
            zb, yb = (z1, y1) if d > 0 else (z0, y0)
            # a brace is a thin box spanning the diagonal; authored in the X-Z plane at the
            # mean portal height, which is what the reference render actually shows
            length = math.hypot(zb - za, rib_x * 2)
            yaw = math.degrees(math.atan2(zb - za, rib_x * 2 * d))
            b.box((0.0, (ya + yb) * 0.5, (za + zb) * 0.5), (length, 0.55, 0.55), yaw_deg=yaw)

    # ---- parapet: posts and two rails, both sides ----------------------------------------
    b.box((rail_x, 1.12, 0.0), (0.18, 0.16, span))
    b.box((-rail_x, 1.12, 0.0), (0.18, 0.16, span))
    b.box((rail_x, 0.58, 0.0), (0.14, 0.13, span))
    b.box((-rail_x, 0.58, 0.0), (0.14, 0.13, span))
    posts = int(span / 2.6)
    for i in range(posts + 1):
        z = -half + i * (span / posts)
        for sgn in (-1, 1):
            b.box((sgn * rail_x, 0.60, z), (0.19, 1.20, 0.19))
    parts.append(finish(b, f"{name}_coral", coral, smooth=False, bevel=0.03))

    # ---- deck slab, edge beams and abutments --------------------------------------------
    b = Builder()
    # Sits BELOW y = 0 so the swept carriageway is always the visible running surface.
    b.box((0.0, -0.55, 0.0), (deck_half_w * 2, 1.10, span), uv_scale=0.25)
    for sgn in (-1, 1):
        b.box((sgn * (deck_half_w - 0.35), 0.10, 0.0), (0.80, 1.90, span), uv_scale=0.25)
    # abutments: short blocks that bury themselves in the terrain at both ends
    for sgn in (-1, 1):
        b.box((0.0, -4.4, sgn * (half + 3.0)), (deck_half_w * 2 + 3.0, 10.0, 7.0), uv_scale=0.2)
        # arch springing pads
        for s2 in (-1, 1):
            b.box((s2 * rib_x, -1.4, sgn * (half - 1.5)), (3.4, 3.6, 5.0), uv_scale=0.3)
    parts.append(finish(b, f"{name}_deck", conc, smooth=False, bevel=0.05))

    obj = S.join(parts, name)
    S.shade_auto_smooth(obj, angle_deg=34.0)
    S.report(name, obj)
    export([obj], f"{name}.glb")
    return obj


# --------------------------------------------------------------------------- utilities

class _Rng:
    """Tiny deterministic LCG - a fixed seed keeps every build diffable."""

    def __init__(self, seed):
        self.s = (seed * 1103515245 + 12345) & 0x7FFFFFFF

    def next(self):
        self.s = (self.s * 1103515245 + 12345) & 0x7FFFFFFF
        return self.s / 0x7FFFFFFF


# --------------------------------------------------------------------------- main

BUILDERS = {
    "lighthouse": build_lighthouse,
    "torii": build_torii,
    "guardrail": build_guardrail_bay,
    "pine": lambda: (build_pine("Shiosai_Pine", 9.5, 3),
                     build_pine("Shiosai_Pine_B", 12.5, 91)),
    "broadleaf": lambda: (build_broadleaf("Shiosai_Broadleaf", 7.5, 17),
                          build_broadleaf("Shiosai_Broadleaf_B", 9.5, 29)),
    "hydrangea": lambda: (
        # Three tuned variants so a 720-bush bank is not one prop repeated. PROVISIONAL shapes.
        build_hydrangea("Shiosai_Hydrangea", seed=11, heads_n=6, spread=1.00, tall=1.00),
        build_hydrangea("Shiosai_Hydrangea_B", seed=23, heads_n=7, spread=0.86, tall=1.22),
        build_hydrangea("Shiosai_Hydrangea_C", seed=37, heads_n=5, spread=1.18, tall=0.84)),
    "stacks": lambda: (build_sea_stack("Shiosai_SeaStack_A", 16.0, 3.7, 7),
                       build_sea_stack("Shiosai_SeaStack_B", 9.5, 2.9, 21),
                       build_sea_stack("Shiosai_SeaStack_C", 24.0, 5.4, 43)),
    "houses": lambda: (build_harbour_house("Shiosai_House_A", 8.0, 7.0, 2, 5, (0.26, 0.28, 0.32)),
                       build_harbour_house("Shiosai_House_B", 6.5, 6.0, 1, 13, (0.34, 0.22, 0.20)),
                       build_harbour_house("Shiosai_House_C", 10.5, 8.0, 3, 31, (0.24, 0.30, 0.34))),
    "harbour_benchmark_houses": lambda: (
        build_harbour_benchmark_house("Shiosai_HarbourHouse_A", 8.2, 7.4, 2, 105,
                                     (0.16, 0.20, 0.26), (0.82, 0.80, 0.73)),
        build_harbour_benchmark_house("Shiosai_HarbourHouse_B", 6.8, 6.3, 1, 113,
                                     (0.24, 0.21, 0.19), (0.68, 0.66, 0.59)),
        build_harbour_benchmark_house("Shiosai_HarbourHouse_C", 9.6, 7.8, 2, 131,
                                     (0.18, 0.25, 0.30), (0.74, 0.75, 0.70))),
    "breakwater": build_breakwater,
    "tetrapod": build_tetrapod,
    "boat": build_boat,
    "redbridge": build_red_bridge,
}


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stages = args or list(BUILDERS.keys())
    for stage in stages:
        fn = BUILDERS.get(stage)
        if fn is None:
            print(f"[shiosai] unknown stage '{stage}'")
            continue
        S.reset_scene()
        fn()
    print(f"[shiosai] assets -> {shiosai_assets_dir()}")


if __name__ == "__main__":
    main()

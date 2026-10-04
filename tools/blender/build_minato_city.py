"""
MINATO COAST - the BRIGHT MODERN CITY pass.

Adds the three families that the "drab / empty" Minato was missing:

  1. A modern glass-and-steel HIGH-RISE KIT (five silhouettes, 46 m .. 186 m) that gives the
     port city a real skyline instead of a field of warehouses.
  2. A low-poly CHIBI CROWD KIT - standing, walking and seated pedestrians plus a cyclist on a
     bike - so the waterfront reads populated. These are deliberately cheap (a few hundred tris
     each): they are placed in the hundreds and read at 15-200 m, not at 2 m.
  3. BEACH FURNITURE (parasol) so the sand reads as a used, sunny public beach.

Conventions are identical to build_minato_props.py: authored in UNITY coordinates through the
``minato_lib`` primitives (which route every vertex through ``sakura_lib.u2b``), origin at the
GROUND CONTACT POINT (y = 0) facing +Z, exported as clean-root FBX into
``Assets/Environment/MinatoCoast/Models``.

Material SLOT NAMES are the contract with Unity: MinatoCoastEnvironment re-materialises these
families by matching the imported slot name (``Minato_glass_bright``, ``Minato_cloth_top`` ...),
so every dimension and colour here is PROVISIONAL illustrative tuning that Unity can override.

Run:  blender -b -P build_minato_city.py            (everything)
      blender -b -P build_minato_city.py -- towers  (one group: towers | people | beach)
"""

import math
import os
import sys

import bpy  # noqa: F401  (imported for parity with the other builders)

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402


# =================================================================== tuning (ALL PROVISIONAL)

FLOOR_H = 3.75          # storey height driving the spandrel bands
MULLION_SPACING = 3.20  # vertical curtain-wall mullion pitch
PERSON_H = 1.66         # nominal adult height for the chibi crowd kit


# =================================================================== 1. modern high-rise kit

def _curtain_wall(glass, frame, x0, x1, z0, z1, y0, y1, inset=0.10):
    """
    A modern glass curtain wall on the four faces of a rectangular volume.

    Built as ONE inset glass box per volume plus proud horizontal spandrel bands and vertical
    mullions. That is dramatically cheaper than a per-window grid (which is what the old
    ``city_block`` did) and it is what actually reads as a modern tower at 300-1500 m: a
    continuous reflective sheet cut by a fine structural grid, not a stamp of punched holes.
    """
    cx, cz = (x0 + x1) * 0.5, (z0 + z1) * 0.5
    w, d = (x1 - x0), (z1 - z0)
    h = y1 - y0

    # The glass sheet itself: a single box inset behind the structural frame.
    glass.append(M.box("glass", (cx, (y0 + y1) * 0.5, cz),
                       (w - inset * 2.0, h - 0.30, d - inset * 2.0), uv_per_m=0.25))

    # Horizontal spandrel bands at every floor line - proud of the glass.
    floors = max(1, int(round(h / FLOOR_H)))
    for f in range(floors + 1):
        y = y0 + h * f / floors
        frame.append(M.box(f"band{int(y0)}_{f}", (cx, y, cz),
                           (w + 0.10, 0.26, d + 0.10), uv_per_m=0.55))

    # Vertical mullions on all four faces.
    nx = max(2, int(round(w / MULLION_SPACING)))
    nz = max(2, int(round(d / MULLION_SPACING)))
    for i in range(nx + 1):
        x = x0 + w * i / nx
        for zf in (z0, z1):
            frame.append(M.box(f"mulz{int(y0)}_{i}_{int(zf)}", (x, (y0 + y1) * 0.5, zf),
                               (0.22, h - 0.30, 0.16), uv_per_m=0.8))
    for i in range(nz + 1):
        z = z0 + d * i / nz
        for xf in (x0, x1):
            frame.append(M.box(f"mulx{int(y0)}_{i}_{int(xf)}", (xf, (y0 + y1) * 0.5, z),
                               (0.16, h - 0.30, 0.22), uv_per_m=0.8))


def sky_tower(tag, w, d, h, setback=0.72, stages=2, crown="mast", teal=False):
    """
    A modern glass high-rise: a stack of ``stages`` setback volumes, each fully curtain-walled,
    on a slightly wider podium, topped by a crown (mast, box plant room or a tapered cap).

    Origin is the ground contact point; the tower faces +Z like every other Minato prop.
    """
    glass, frame, crownp = [], [], []

    # Podium: 2 storeys, wider than the shaft, with its own glazed shopfront.
    pw, pd, ph = w * 1.22, d * 1.22, FLOOR_H * 2.0
    frame.append(M.box("podium", (0.0, ph * 0.5, 0.0), (pw, ph, pd), uv_per_m=0.35))
    _curtain_wall(glass, frame, -pw * 0.5, pw * 0.5, -pd * 0.5, pd * 0.5, 0.35, ph - 0.35,
                  inset=0.14)
    frame.append(M.box("podium_cap", (0.0, ph + 0.30, 0.0), (pw + 0.5, 0.60, pd + 0.5),
                       uv_per_m=0.5))

    # Shaft: successive setback volumes.
    y = ph
    cw, cd = w, d
    remaining = h - ph
    for s in range(stages):
        sh = remaining * (1.0 if s == stages - 1 else 0.62)
        remaining -= sh
        _curtain_wall(glass, frame, -cw * 0.5, cw * 0.5, -cd * 0.5, cd * 0.5, y, y + sh)
        y += sh
        frame.append(M.box(f"cap{s}", (0.0, y + 0.35, 0.0), (cw + 0.45, 0.70, cd + 0.45),
                           uv_per_m=0.5))
        cw *= setback
        cd *= setback

    # Crown.
    if crown == "mast":
        crownp.append(M.box("plant", (0.0, y + 2.4, 0.0), (cw * 0.66, 4.2, cd * 0.66),
                            uv_per_m=0.5))
        crownp.append(M.tube("mast", (0.0, y + 4.4, 0.0), (0.0, y + 4.4 + h * 0.16, 0.0),
                             0.55, segments=8, uv_per_m=0.4, taper=0.22))
    elif crown == "cap":
        crownp.append(M.beam("taper", (0.0, y, 0.0), (0.0, y + h * 0.10, 0.0),
                             cw * 0.95, cd * 0.95, up_hint=(0, 0, 1), uv_per_m=0.4,
                             taper=0.18))
    else:  # "plant"
        crownp.append(M.box("plant", (cw * 0.10, y + 2.2, -cd * 0.08),
                            (cw * 0.62, 4.4, cd * 0.56), uv_per_m=0.5))
        crownp.append(M.tube("tank", (-cw * 0.26, y + 0.6, cd * 0.20),
                             (-cw * 0.26, y + 4.2, cd * 0.20), 1.1, segments=10, uv_per_m=0.6))

    g = M.finish(glass, "g", bevel=0.02, smooth_angle=34.0)
    f = M.finish(frame, "f", bevel=0.03, smooth_angle=30.0)
    c = M.finish(crownp, "c", bevel=0.03, smooth_angle=32.0)
    M.paint(g, "glass_teal" if teal else "glass_bright")
    M.paint(f, "tower_frame")
    M.paint(c, "tower_crown")
    return S.join([o for o in (g, f, c) if o is not None], f"Minato_City_SkyTower{tag}_LOD0")


# =================================================================== 2. chibi crowd kit

def _figure(parts, pose, seed=0):
    """
    A low-poly chibi person, ``PERSON_H`` tall, in Unity coords with the feet at y = 0 and
    facing +Z. ``parts`` is a dict of lists keyed by material slot.

    Proportions follow the project's cel-shaded chibi direction: a large head (~0.22 of total
    height), short limbs, blocky masses. Everything is a bevelled box or tube, so the whole
    figure lands around 250-400 tris.
    """
    H = PERSON_H
    # Chibi proportions: a genuinely big head (~0.27 of body height) and chunky limb masses.
    # The first pass used 0.115 and read as a stick figure in the contact render. PROVISIONAL.
    head_r = H * 0.135
    hip_y = H * 0.44
    shoulder_y = H * 0.72
    neck_y = H * 0.775
    head_y = neck_y + head_r

    walk = 1.0 if pose == "walk" else 0.0
    sit = 1.0 if pose == "sit" else 0.0
    wave = 1.0 if pose == "wave" else 0.0

    if sit:
        hip_y = H * 0.26
        shoulder_y = H * 0.54
        neck_y = H * 0.595
        head_y = neck_y + head_r

    # --- legs
    leg_hw = H * 0.062
    for sx in (-1.0, 1.0):
        x = sx * H * 0.062
        if sit:
            # thigh forward, shin down
            parts["cloth_leg"].append(M.beam(f"thigh{sx}", (x, hip_y, 0.0),
                                             (x, hip_y - 0.02, H * 0.20), leg_hw * 2.0,
                                             leg_hw * 2.0, up_hint=(0, 1, 0), uv_per_m=1.0))
            parts["cloth_leg"].append(M.beam(f"shin{sx}", (x, hip_y - 0.02, H * 0.20),
                                             (x, 0.02, H * 0.19), leg_hw * 1.7, leg_hw * 1.7,
                                             up_hint=(0, 0, 1), uv_per_m=1.0))
        else:
            z0 = sx * walk * H * 0.11
            parts["cloth_leg"].append(M.beam(f"leg{sx}", (x, hip_y, z0),
                                             (x, 0.03, -z0 * 0.6), leg_hw * 2.0, leg_hw * 2.0,
                                             up_hint=(0, 0, 1), uv_per_m=1.0))
        parts["cloth_leg"].append(M.box(f"shoe{sx}", (x, 0.035, (sx * walk * H * 0.11) * -0.6
                                                      + (H * 0.19 if sit else 0.03)),
                                        (leg_hw * 2.1, 0.07, H * 0.115), uv_per_m=1.4))

    # --- torso (slightly tapered, wider at the shoulders)
    parts["cloth_top"].append(M.beam("torso", (0.0, hip_y - 0.03, 0.0), (0.0, neck_y, 0.0),
                                     H * 0.275, H * 0.180, up_hint=(0, 0, 1), uv_per_m=1.0,
                                     taper=0.92))

    # --- arms
    arm_t = H * 0.064
    for sx in (-1.0, 1.0):
        sh = (sx * H * 0.140, shoulder_y, 0.0)
        if wave and sx > 0:
            hand = (sx * H * 0.20, shoulder_y + H * 0.26, -H * 0.02)
        elif sit:
            hand = (sx * H * 0.15, hip_y + H * 0.02, H * 0.14)
        else:
            hand = (sx * H * 0.135, hip_y + H * 0.02, -sx * walk * H * 0.09)
        parts["cloth_top"].append(M.beam(f"arm{sx}", sh, hand, arm_t, arm_t,
                                         up_hint=(0, 0, 1), uv_per_m=1.2, taper=0.82))
        parts["skin"].append(M.box(f"hand{sx}", hand, (arm_t * 1.1, arm_t * 1.2, arm_t * 1.1),
                                   uv_per_m=2.0))

    # --- neck + head
    parts["skin"].append(M.tube("neck", (0.0, neck_y - 0.05, 0.0), (0.0, neck_y + 0.04, 0.0),
                                H * 0.050, segments=8, uv_per_m=1.2))
    parts["skin"].append(M.box("head", (0.0, head_y, 0.0),
                               (head_r * 1.78, head_r * 1.92, head_r * 1.70), uv_per_m=1.6))
    # Hair CAP: sits on the crown. The first pass centred it at +0.42 r with a 1.14 r height,
    # which swallowed the top 60% of the head and left the figure faceless.
    parts["hair"].append(M.box("hair", (0.0, head_y + head_r * 0.70, -head_r * 0.08),
                               (head_r * 1.88, head_r * 0.92, head_r * 1.80), uv_per_m=1.6))
    if seed % 3 == 0:
        parts["hair"].append(M.box("hair_back", (0.0, head_y - head_r * 0.10,
                                                 -head_r * 0.90),
                                   (head_r * 1.62, head_r * 1.60, head_r * 0.40),
                                   uv_per_m=1.6))


def _assemble_figure(parts, name, extra=None):
    objs = []
    for slot, items in parts.items():
        if not items:
            continue
        o = M.finish(items, slot[:2], bevel=0.012, smooth_angle=36.0)
        M.paint(o, slot)
        objs.append(o)
    if extra:
        objs.extend(extra)
    return S.join(objs, name)


def _new_parts():
    return {"skin": [], "hair": [], "cloth_top": [], "cloth_leg": [], "bike_frame": []}


def pedestrian(tag, pose, seed=0):
    parts = _new_parts()
    _figure(parts, pose, seed)
    return _assemble_figure(parts, f"Minato_People_{tag}_LOD0")


def cyclist():
    """
    A chibi rider leaning on the bars of a simple road bike. Used as BOULEVARD TRAFFIC - these
    are background cyclists that make the waterfront read as a used cycling city; they are NOT
    the rigged gameplay NPCs (those come from the Kuro skeleton via the NPC skill).
    """
    H = PERSON_H
    parts = _new_parts()

    wheel_r = 0.34
    bb_z = 0.0                      # bottom bracket
    rear_z = -0.50
    front_z = 0.52
    hub_y = wheel_r

    # --- wheels: a ring of short beams (a torus would be far heavier for no visual gain)
    for (zc, tagw) in ((rear_z, "r"), (front_z, "f")):
        n = 14
        pts = [(0.0, hub_y + math.sin(i / n * math.tau) * wheel_r,
                zc + math.cos(i / n * math.tau) * wheel_r) for i in range(n)]
        for i in range(n):
            parts["bike_frame"].append(
                M.beam(f"tyre{tagw}{i}", pts[i], pts[(i + 1) % n], 0.055, 0.075,
                       up_hint=(1, 0, 0), uv_per_m=1.2))
        for i in range(0, n, 2):
            parts["bike_frame"].append(
                M.tube(f"spoke{tagw}{i}", (0.0, hub_y, zc), pts[i], 0.014, segments=4,
                       uv_per_m=1.0))

    # --- frame
    saddle = (0.0, 0.92, -0.22)
    bars = (0.0, 0.88, 0.40)
    for (a, b, t) in (((0.0, hub_y, rear_z), (0.0, 0.30, bb_z), 0.05),
                      ((0.0, 0.30, bb_z), saddle, 0.05),
                      (saddle, bars, 0.05),
                      ((0.0, 0.30, bb_z), bars, 0.05),
                      (bars, (0.0, hub_y, front_z), 0.045),
                      ((0.0, hub_y, rear_z), saddle, 0.04)):
        parts["bike_frame"].append(M.beam(f"tube{a[2]:.2f}_{b[2]:.2f}", a, b, t, t,
                                          up_hint=(1, 0, 0), uv_per_m=1.0))
    parts["bike_frame"].append(M.box("saddle", (0.0, 0.95, -0.22), (0.10, 0.05, 0.26),
                                     uv_per_m=1.6))
    parts["bike_frame"].append(M.beam("bar", (-0.21, 0.90, 0.40), (0.21, 0.90, 0.40),
                                      0.045, 0.045, up_hint=(0, 1, 0), uv_per_m=1.2))

    # --- rider, leaning forward onto the bars
    hip = (0.0, 1.00, -0.20)
    shoulder_y = 1.24
    neck_y = 1.30
    head_r = H * 0.135
    head_y = neck_y + head_r * 0.85

    for sx in (-1.0, 1.0):
        x = sx * 0.085
        knee = (x, 0.62, 0.16 * sx)
        parts["cloth_leg"].append(M.beam(f"thigh{sx}", (x, hip[1] - 0.02, hip[2]), knee,
                                         0.115, 0.115, up_hint=(0, 0, 1), uv_per_m=1.0))
        parts["cloth_leg"].append(M.beam(f"shin{sx}", knee, (x, 0.26, 0.02 * sx),
                                         0.095, 0.095, up_hint=(0, 0, 1), uv_per_m=1.0))
        parts["cloth_leg"].append(M.box(f"shoe{sx}", (x, 0.22, 0.03 * sx),
                                        (0.10, 0.06, 0.20), uv_per_m=1.6))
        parts["cloth_top"].append(M.beam(f"arm{sx}", (sx * 0.16, shoulder_y, -0.10),
                                         (sx * 0.20, 0.90, 0.39), 0.075, 0.075,
                                         up_hint=(0, 1, 0), uv_per_m=1.2, taper=0.85))
        parts["skin"].append(M.box(f"hand{sx}", (sx * 0.20, 0.91, 0.40),
                                   (0.075, 0.085, 0.085), uv_per_m=2.0))

    parts["cloth_top"].append(M.beam("torso", hip, (0.0, neck_y, 0.02), 0.42, 0.30,
                                     up_hint=(0, 0, 1), uv_per_m=1.0, taper=0.90))
    parts["skin"].append(M.box("head", (0.0, head_y, 0.10),
                               (head_r * 1.74, head_r * 1.86, head_r * 1.70), uv_per_m=1.6))
    # Helmet, not hair - these are riders. Sits ON the crown (see the hair-cap note above).
    parts["hair"].append(M.box("helmet", (0.0, head_y + head_r * 0.66, 0.08),
                               (head_r * 1.92, head_r * 0.96, head_r * 1.88), uv_per_m=1.6))

    return _assemble_figure(parts, "Minato_People_Cyclist_LOD0")


# =================================================================== 3. beach furniture

def beach_parasol():
    """A bright beach parasol with a towel at its foot - the 'this beach is used' tell."""
    pole, canopy, towel = [], [], []
    pole.append(M.tube("pole", (0.0, 0.0, 0.0), (0.0, 2.18, 0.0), 0.035, segments=6,
                       uv_per_m=1.0))
    # Canopy: a shallow cone (tube with a hard taper), plus a scalloped rim band.
    canopy.append(M.tube("canopy", (0.0, 1.78, 0.0), (0.0, 2.30, 0.0), 1.45, segments=12,
                         uv_per_m=0.8, taper=0.06))
    canopy.append(M.tube("rim", (0.0, 1.74, 0.0), (0.0, 1.82, 0.0), 1.48, segments=12,
                         uv_per_m=0.8))
    towel.append(M.box("towel", (0.95, 0.03, 0.30), (0.85, 0.05, 1.85), uv_per_m=1.0))

    p = M.finish(pole, "p", bevel=0.01, smooth_angle=36.0)
    c = M.finish(canopy, "c", bevel=0.01, smooth_angle=40.0)
    t = M.finish(towel, "t", bevel=0.01, smooth_angle=36.0)
    M.paint(p, "lamp_grey")
    M.paint(c, "parasol")
    M.paint(t, "cloth_top")
    return S.join([p, c, t], "Minato_Beach_Parasol_LOD0")


# =================================================================== export table

TOWERS = [
    # (builder, filename, LOD ratios)
    (lambda: sky_tower("A", 21, 19, 74, stages=2, crown="mast"),
     "Minato_City_SkyTowerA.fbx", (0.32, 0.10)),
    (lambda: sky_tower("B", 26, 22, 108, stages=3, crown="plant", teal=True),
     "Minato_City_SkyTowerB.fbx", (0.32, 0.10)),
    (lambda: sky_tower("C", 18, 18, 142, stages=2, setback=0.80, crown="mast"),
     "Minato_City_SkyTowerC.fbx", (0.32, 0.10)),
    (lambda: sky_tower("D", 30, 24, 186, stages=3, setback=0.76, crown="cap"),
     "Minato_City_SkyTowerD.fbx", (0.30, 0.09)),
    (lambda: sky_tower("E", 34, 26, 48, stages=1, crown="plant", teal=True),
     "Minato_City_SkyTowerE.fbx", (0.35, 0.12)),
]

PEOPLE = [
    (lambda: pedestrian("StandA", "stand", 0), "Minato_People_StandA.fbx", ()),
    (lambda: pedestrian("StandB", "stand", 1), "Minato_People_StandB.fbx", ()),
    (lambda: pedestrian("Walk", "walk", 2), "Minato_People_Walk.fbx", ()),
    (lambda: pedestrian("Sit", "sit", 0), "Minato_People_Sit.fbx", ()),
    (lambda: pedestrian("Wave", "wave", 1), "Minato_People_Wave.fbx", ()),
    (cyclist, "Minato_People_Cyclist.fbx", ()),
]

BEACH = [
    (beach_parasol, "Minato_Beach_Parasol.fbx", ()),
]

GROUPS = {"towers": TOWERS, "people": PEOPLE, "beach": BEACH}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    wanted = argv if argv else list(GROUPS.keys())
    total = 0
    count = 0
    for key in wanted:
        if key not in GROUPS:
            print(f"[minato] unknown group '{key}' - expected one of {list(GROUPS)}")
            continue
        for builder, filename, ratios in GROUPS[key]:
            S.reset_scene()
            obj = builder()
            M.stat(obj, filename.replace(".fbx", ""))
            exports = [obj]
            base = obj.name.replace("_LOD0", "")
            for i, r in enumerate(ratios):
                exports.append(M.decimated_copy(obj, f"{base}_LOD{i + 1}", r))
            M.export_fbx(exports, filename)
            M.save_blend(filename.replace(".fbx", ".blend"))
            obj.data.calc_loop_triangles()
            total += len(obj.data.loop_triangles)
            count += 1
    print(f"[minato] city pass complete: {count} assets, {total:,} LOD0 tris")


if __name__ == "__main__":
    main()

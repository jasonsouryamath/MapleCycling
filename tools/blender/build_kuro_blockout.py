"""
MapleRide - Kuro + road-bike GRAYBOX BLOCKOUT builder.

Scope (deliberately limited - this is a reviewable silhouette pass, NOT the final model):
  * primitive bike: wheels, frame tubes, fork, saddle + post, cockpit, cranks/pedals
  * primitive rider: head ball, helmet dome, torso prism, arms, legs, shoes
  * two poses driven from ONE body definition: RIDING (refs 03/04) and STANDING (ref 05)
  * NO details, textures, decals, hair cards, fingers, drivetrain, rigging.

Reference set (sole target): Assets/Kuro/ReferencePack
  03_riding_left.png, 04_riding_right.png, 05_standing_front.png (priority)
  07_standing_right.png, 08/09 wireframes, 00_design_sheet.png (scale callout)

Authoring axes (Blender): +X = rider's right, +Y = forward (direction of travel), +Z = up.
Ground plane is Z = 0. glTF export converts to Unity Y-up / +Z-forward.

ALL numbers in DIMS are PROVISIONAL illustrative tuning locked from the reference pack
+ the existing project scale (kuro_cycle_fixed.glb = 1.384 m tall,
kuro_on_bike.glb = 1.498 m tall / 1.326 m long). They are named constants on purpose -
do not treat them as final production requirements.

Usage:
  blender.exe -b -P tools/blender/build_kuro_blockout.py -- --out <repo> [--no-render]
"""

import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector, Matrix

# ---------------------------------------------------------------------------
# LOCKED METRIC DIMENSIONS (all PROVISIONAL - derived from the ReferencePack)
# ---------------------------------------------------------------------------
DIMS = {
    # ---- global scale anchors -------------------------------------------------
    # 00_design_sheet.png: "Units: Meters / Scale 1.0 = real-world
    #                       (Character height ~1.2-1.4 m)"
    # Cross-checked against existing Assets/Kuro/kuro_cycle_fixed.glb (1.384 m).
    "rider_standing_height": 1.380,   # helmet crown -> ground, standing
    "ride_total_height": 1.500,       # helmet crown -> ground, on the bike

    # ---- rider body (measured off 05_standing_front / 07_standing_right) ------
    # 05: figure spans 362 px for 1.380 m  =>  3.812 mm/px
    "head_ball_w": 0.360,             # skull+hair mass, lateral   (~ 95 px)
    "head_ball_d": 0.390,             # skull+hair mass, fore/aft  (109 px, 07)
    "head_ball_h": 0.345,             # skull ball height
    "helmet_extra_r": 0.022,          # helmet shell over the skull ball
    "helmet_dome_h": 0.178,           # dome half-height (crown -> 1.380 m)
    "helmet_lift": 0.085,             # dome centre above the head-ball centre
    "neck_len": 0.043,                # chibi: the head sits ON the shoulders
                                      # (jaw lands at 0.945 m, crown at 1.380 m)
    "torso_depth_ratio": 0.95,        # side-profile fullness of the chest prism
    "jaw_height": 0.945,              # chin/neck underside, standing (y=134 px)
    "neck_dia": 0.095,
    "shoulder_height": 0.892,         # standing (y=148 px)
    "shoulder_width": 0.313,          # 82 px
    "chest_depth": 0.240,             # 63 px (07)
    "waist_width": 0.187,             # 49 px
    "hip_width": 0.229,               # 60 px
    "crotch_height": 0.496,           # standing (y=252 px)
    "thigh_dia": 0.126,               # 33 px
    "calf_dia": 0.105,
    "thigh_len": 0.255,               # hip joint -> knee (consistent with crotch 0.496)
    "shank_len": 0.245,               # knee -> ankle
    "arm_dia": 0.062,
    "upper_arm_len": 0.170,
    "forearm_len": 0.170,
    "hand_len": 0.075,
    "shoe_len": 0.260,                # chunky chibi shoe (ref reads 0.26-0.31)
    "shoe_w": 0.110,
    "shoe_h": 0.070,
    "stance_half_width": 0.075,       # standing foot centre offset from midline

    # ---- bike (chibi-scaled road bike, 09_bike_wireframes proportions) --------
    # Overall length locked to the project's existing 1.326 m bike footprint.
    "wheel_dia": 0.500,
    "tire_r": 0.024,
    "wheelbase": 0.826,               # 0.826 + 0.500 = 1.326 m overall length
    "bb_height": 0.200,               # bottom-bracket centre above ground
    "rear_hub_dy": -0.330,            # hub offsets from BB along +Y (forward)
    "front_hub_dy": 0.496,
    "tube_r": 0.019,
    "stay_r": 0.012,
    "fork_r": 0.014,
    # crank scaled to the chibi inseam (0.496 m); a real 0.1725 m crank is
    # kinematically impossible for this rider - see KURO_BLOCKOUT_DIMENSIONS.md
    "crank_len": 0.100,
    "pedal_len": 0.090,
    "pedal_w": 0.062,
    "saddle_top": 0.580,              # solved so the chibi leg reaches BDC
    "saddle_dy": -0.115,              # saddle centre behind BB
    "saddle_len": 0.185,
    "saddle_w": 0.105,
    # refs 03/04: the hoods sit ~0.14 m ABOVE the saddle on this chibi bike
    "hood_height": 0.720,             # bar-top / hood height above ground
    "hood_dy": 0.395,                 # hoods ahead of BB
    "bar_width": 0.330,
    "drop_depth": 0.110,
    "head_tube_top": 0.560,
    "head_tube_dy": 0.395,
    "seat_tube_angle": 72.0,          # degrees from horizontal
    "head_tube_angle": 71.0,

    # ---- RIDING pose (refs 03 / 04) ------------------------------------------
    "ride_hip_dy": -0.105,            # hip joint relative to BB
    "ride_hip_z": 0.635,
    "ride_torso_lean": 20.0,          # degrees above horizontal (hip -> shoulder) --
                                      # re-measured off refs 03/04 (px->m via the
                                      # front-wheel diameter, sole hard geometric
                                      # anchor visible in the photo): the seated
                                      # shoulder sits ~0.75 m above ground, not the
                                      # 0.888 m the old 48 deg value produced. 48 deg
                                      # read as a near-upright torso that towered
                                      # over the cockpit; 20 deg (near-horizontal
                                      # aero back) reproduces the reference shoulder
                                      # height and, unchanged otherwise, also pulls
                                      # the head/hood/arm chain back into place (see
                                      # KURO_BLOCKOUT_DIMENSIONS.md for the numbers).
    "ride_head_pitch": 22.0,          # head tips up to look down the road
    "ride_head_lift": -0.070,         # chibi: crown tucks down onto the shoulders
    "ride_head_dy": 0.130,            # head carried forward, over the cockpit
    "ride_crank_phase": 15.0,         # right crank angle, 0 = forward-horizontal
    "ride_hand_dz": 0.030,            # hands sit on top of the hoods
}

GRAY_BG = (0.105, 0.105, 0.110, 1.0)   # matches the ReferencePack backdrop
CLAY = (0.520, 0.520, 0.530, 1.0)

# ---------------------------------------------------------------------------
# primitive helpers
# ---------------------------------------------------------------------------


def _clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _clay_material():
    mat = bpy.data.materials.get("KuroBlockoutClay")
    if mat:
        return mat
    mat = bpy.data.materials.new("KuroBlockoutClay")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = CLAY
    bsdf.inputs["Roughness"].default_value = 0.85
    if "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def _finish(obj, name, coll):
    obj.name = name
    obj.data.materials.clear()
    obj.data.materials.append(_clay_material())
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)
    return obj


def sphere(coll, name, centre, size, segments=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=segments // 2,
                                         radius=0.5, location=centre)
    o = bpy.context.active_object
    o.scale = Vector(size)
    return _finish(o, name, coll)


def box(coll, name, centre, size, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=centre)
    o = bpy.context.active_object
    o.scale = Vector(size)
    if rot:
        o.rotation_euler = rot
    return _finish(o, name, coll)


def tube(coll, name, p0, p1, r0, r1=None, verts=12):
    """Tapered cylinder spanning two world points."""
    p0 = Vector(p0)
    p1 = Vector(p1)
    d = p1 - p0
    length = d.length
    if length < 1e-5:
        length = 1e-5
    r1 = r0 if r1 is None else r1
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1,
                                    depth=length, location=(p0 + p1) * 0.5)
    o = bpy.context.active_object
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = d.to_track_quat('Z', 'Y')
    return _finish(o, name, coll)


def limb(coll, name, p0, p1, r, cap=True):
    parts = [tube(coll, name, p0, p1, r)]
    if cap:
        parts.append(sphere(coll, name + "_J0", p0, (r * 2, r * 2, r * 2), 12))
        parts.append(sphere(coll, name + "_J1", p1, (r * 2, r * 2, r * 2), 12))
    return parts


def torus(coll, name, centre, major_r, minor_r, axis='X'):
    bpy.ops.mesh.primitive_torus_add(location=centre, major_radius=major_r,
                                     minor_radius=minor_r, major_segments=40,
                                     minor_segments=10)
    o = bpy.context.active_object
    if axis == 'X':
        o.rotation_euler = (0.0, math.radians(90.0), 0.0)
    return _finish(o, name, coll)


# ---------------------------------------------------------------------------
# bike
# ---------------------------------------------------------------------------


def build_bike(coll, D, crank_phase_deg):
    """Bike sits with its BB at (0, 0, bb_height); +Y is the travel direction."""
    bb = Vector((0.0, 0.0, D["bb_height"]))
    wheel_r = D["wheel_dia"] * 0.5
    hub_z = wheel_r
    rear_hub = Vector((0.0, D["rear_hub_dy"], hub_z))
    front_hub = Vector((0.0, D["front_hub_dy"], hub_z))

    # --- wheels (torus tyre + hub; spokes are detail, deliberately omitted) ---
    for tag, hub in (("Rear", rear_hub), ("Front", front_hub)):
        torus(coll, f"Wheel{tag}_Tyre", hub, wheel_r - D["tire_r"], D["tire_r"])
        tube(coll, f"Wheel{tag}_Hub", hub - Vector((0.028, 0, 0)),
             hub + Vector((0.028, 0, 0)), 0.022)

    # --- frame -----------------------------------------------------------------
    sa = math.radians(D["seat_tube_angle"])
    seat_top = Vector((0.0, D["saddle_dy"], D["saddle_top"] - 0.055))
    ha = math.radians(D["head_tube_angle"])
    ht_top = Vector((0.0, D["head_tube_dy"], D["head_tube_top"]))
    ht_bot = ht_top - Vector((0.0, math.cos(ha), math.sin(ha))) * 0.115

    tube(coll, "Frame_SeatTube", bb, seat_top, D["tube_r"])
    tube(coll, "Frame_DownTube", bb, ht_bot, D["tube_r"] * 1.15)
    tube(coll, "Frame_TopTube", seat_top - Vector((0, 0, 0.020)), ht_top, D["tube_r"])
    tube(coll, "Frame_HeadTube", ht_bot, ht_top, D["tube_r"] * 1.25)
    tube(coll, "Frame_BB", bb - Vector((0.036, 0, 0)), bb + Vector((0.036, 0, 0)),
         D["tube_r"] * 1.4)

    for s in (-1, 1):
        off = Vector((0.045 * s, 0, 0))
        tube(coll, "Frame_Chainstay", bb + off * 0.8, rear_hub + off * 0.55, D["stay_r"])
        tube(coll, "Frame_Seatstay", seat_top - Vector((0, 0, 0.02)) + off * 0.55,
             rear_hub + off * 0.55, D["stay_r"])
        tube(coll, "Fork_Blade", ht_bot + Vector((0.040 * s, 0, 0)),
             front_hub + Vector((0.048 * s, 0, 0)), D["fork_r"])

    # --- saddle + post ---------------------------------------------------------
    saddle_c = Vector((0.0, D["saddle_dy"], D["saddle_top"] - 0.022))
    tube(coll, "SeatPost", seat_top, saddle_c, D["tube_r"] * 0.85)
    sd = sphere(coll, "Saddle", saddle_c,
                (D["saddle_w"], D["saddle_len"], 0.055), 16)
    sd.rotation_euler = (math.radians(-6.0), 0.0, 0.0)

    # --- cockpit ---------------------------------------------------------------
    bar_c = Vector((0.0, D["hood_dy"] - 0.055, D["hood_height"]))
    tube(coll, "Stem", ht_top, bar_c, D["tube_r"] * 0.8)
    tube(coll, "Bar_Top", bar_c - Vector((D["bar_width"] * 0.5, 0, 0)),
         bar_c + Vector((D["bar_width"] * 0.5, 0, 0)), 0.013)
    for s in (-1, 1):
        end = bar_c + Vector((D["bar_width"] * 0.5 * s, 0, 0))
        hood = end + Vector((0.0, 0.075, 0.006))
        tube(coll, "Bar_Hood", end, hood, 0.013)
        tube(coll, "Bar_Drop", hood, hood + Vector((0.0, -0.010, -D["drop_depth"])),
             0.012)

    # --- cranks + pedals (minimal: needed to seat the feet) --------------------
    pedals = {}
    for s, phase in ((1, crank_phase_deg), (-1, crank_phase_deg + 180.0)):
        a = math.radians(phase)
        arm_x = bb.x + 0.062 * s
        tip = Vector((arm_x, bb.y + math.cos(a) * D["crank_len"],
                      bb.z + math.sin(a) * D["crank_len"]))
        tube(coll, "Crank_Arm", Vector((arm_x, bb.y, bb.z)), tip, 0.011)
        box(coll, "Pedal", tip + Vector((0.012 * s, 0, -0.010)),
            (D["pedal_w"], D["pedal_len"], 0.016))
        pedals[s] = tip + Vector((0.012 * s, 0, 0.002))
    return {"bb": bb, "pedals": pedals, "hood_l": bar_c + Vector((-D["bar_width"] * 0.5, 0.075, 0.006)),
            "hood_r": bar_c + Vector((D["bar_width"] * 0.5, 0.075, 0.006))}


# ---------------------------------------------------------------------------
# rider
# ---------------------------------------------------------------------------


def _two_bone_ik(root, target, l0, l1, bend_normal):
    """Classic 2-bone IK; returns the mid joint. bend_normal points the knee/elbow."""
    root = Vector(root)
    target = Vector(target)
    d = target - root
    dist = min(max(d.length, 1e-4), (l0 + l1) * 0.999)
    axis = d.normalized()
    # cosine rule
    cos_a = max(-1.0, min(1.0, (l0 * l0 + dist * dist - l1 * l1) / (2 * l0 * dist)))
    a = math.acos(cos_a)
    perp = (Vector(bend_normal) - axis * Vector(bend_normal).dot(axis))
    if perp.length < 1e-5:
        perp = Vector((0, 0, 1)) - axis * axis.z
    perp.normalize()
    return root + (axis * math.cos(a) + perp * math.sin(a)) * l0


def build_rider(coll, D, pose, rig):
    """pose: 'riding' or 'standing'. rig: bike dict (riding only)."""
    hw = D["hip_width"] * 0.5

    if pose == "standing":
        hip_c = Vector((0.0, 0.0, D["crotch_height"] + 0.055))
        lean = 90.0
        shoulder_c = Vector((0.0, 0.0, D["shoulder_height"]))
        head_pitch = 0.0
        feet = {s: Vector((D["stance_half_width"] * s, 0.018, D["shoe_h"] * 0.5))
                for s in (-1, 1)}
        hands = {s: Vector((D["shoulder_width"] * 0.44 * s, 0.0,
                            D["shoulder_height"] - 0.030
                            - D["upper_arm_len"] - D["forearm_len"]))
                 for s in (-1, 1)}
        knee_n = (0.0, 1.0, 0.0)
        elbow_n = (0.0, -1.0, 0.0)
    else:
        hip_c = Vector((0.0, D["ride_hip_dy"], D["ride_hip_z"]))
        lean = D["ride_torso_lean"]
        la = math.radians(lean)
        shoulder_c = hip_c + Vector((0.0, math.cos(la), math.sin(la))) * _torso_len(D)
        head_pitch = D["ride_head_pitch"]
        feet = {s: rig["pedals"][s] + Vector((0.0, 0.0, D["shoe_h"] * 0.5 + 0.012))
                for s in (-1, 1)}
        hands = {-1: rig["hood_l"] + Vector((0.0, 0.0, D["ride_hand_dz"])),
                 1: rig["hood_r"] + Vector((0.0, 0.0, D["ride_hand_dz"]))}
        knee_n = (0.0, 1.0, 0.2)
        elbow_n = (0.0, -0.4, -1.0)

    torso_len = _torso_len(D)
    up = (shoulder_c - hip_c).normalized()

    # --- pelvis + torso --------------------------------------------------------
    pel = box(coll, "Pelvis", hip_c - up * 0.015,
              (D["hip_width"] * 0.86, D["chest_depth"] * 0.62, 0.115))
    pel.rotation_euler = (math.radians(90.0 - lean), 0.0, 0.0)
    t = tube(coll, "Torso", hip_c - up * 0.030, shoulder_c, D["hip_width"] * 0.50,
             D["shoulder_width"] * 0.46, verts=10)
    # squash fore/aft in the tube's LOCAL frame so it reads as a chest, not a barrel
    t.scale = (1.0, D["torso_depth_ratio"], 1.0)

    # --- neck + head + helmet --------------------------------------------------
    # chibi heads sit on top of the shoulders, not out along the spine
    neck_base = shoulder_c + up * 0.010
    pitch = math.radians(D["ride_head_pitch"] if pose == "riding" else 0.0)
    head_up = Vector((0.0, math.sin(pitch) * 0.55, 1.0)).normalized()
    head_c = neck_base + head_up * (D["head_ball_h"] * 0.5 + D["neck_len"])
    if pose == "riding":
        head_c += Vector((0.0, D["ride_head_dy"], D["ride_head_lift"]))
    tube(coll, "Neck", neck_base, head_c - head_up * D["head_ball_h"] * 0.40,
         D["neck_dia"] * 0.5)
    sphere(coll, "Head", head_c,
           (D["head_ball_w"], D["head_ball_d"], D["head_ball_h"]), 20)
    helm = sphere(coll, "Helmet", head_c + head_up * D["helmet_lift"],
                  (D["head_ball_w"] + D["helmet_extra_r"] * 2,
                   D["head_ball_d"] + D["helmet_extra_r"] * 2,
                   D["helmet_dome_h"] * 2.0), 20)
    helm.rotation_euler = (-pitch * 0.6, 0.0, 0.0)

    # --- arms ------------------------------------------------------------------
    for s in (-1, 1):
        sh = shoulder_c + Vector((D["shoulder_width"] * 0.46 * s, 0, 0)) - up * 0.030
        hand = hands[s]
        elbow = _two_bone_ik(sh, hand, D["upper_arm_len"], D["forearm_len"], elbow_n)
        limb(coll, "UpperArm", sh, elbow, D["arm_dia"] * 0.5)
        limb(coll, "Forearm", elbow, hand, D["arm_dia"] * 0.44)
        sphere(coll, "Hand", hand, (D["arm_dia"] * 1.05, D["hand_len"],
                                    D["arm_dia"] * 0.95), 12)

    # --- legs ------------------------------------------------------------------
    for s in (-1, 1):
        hipj = hip_c + Vector((hw * 0.68 * s, 0, 0))
        foot = feet[s]
        ankle = foot + Vector((0.0, -D["shoe_len"] * 0.18, D["shoe_h"] * 0.45))
        knee = _two_bone_ik(hipj, ankle, D["thigh_len"], D["shank_len"], knee_n)
        limb(coll, "Thigh", hipj, knee, D["thigh_dia"] * 0.5)
        limb(coll, "Shank", knee, ankle, D["calf_dia"] * 0.5)
        box(coll, "Shoe", foot + Vector((0.0, D["shoe_len"] * 0.18, 0.0)),
            (D["shoe_w"], D["shoe_len"], D["shoe_h"]))


def _torso_len(D):
    return D["shoulder_height"] - (D["crotch_height"] + 0.055)


# ---------------------------------------------------------------------------
# scene / render
# ---------------------------------------------------------------------------


def _world_and_lights():
    scene = bpy.context.scene
    scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = GRAY_BG
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 1.0

    for name, energy, rot in (("Key", 3.2, (58, 0, 38)),
                              ("Fill", 1.1, (72, 0, -120)),
                              ("Rim", 1.5, (105, 0, 195))):
        d = bpy.data.lights.new(name, type='SUN')
        d.energy = energy
        d.angle = math.radians(12.0)
        o = bpy.data.objects.new(name, d)
        bpy.context.collection.objects.link(o)
        o.rotation_euler = tuple(math.radians(a) for a in rot)


VIEWS = {
    # name: (camera direction offset, screen matches which reference)
    "left": (Vector((-1, 0, 0)), "03_riding_left"),
    "right": (Vector((1, 0, 0)), "04_riding_right"),
    "front": (Vector((0, 1, 0)), "05_standing_front"),
    "back": (Vector((0, -1, 0)), "06_standing_back"),
}


def render_views(out_dir, prefix, views, centre, ortho_h, res=(620, 1040)):
    scene = bpy.context.scene
    try:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    if hasattr(scene, "view_settings"):
        scene.view_settings.view_transform = 'Standard'

    cam_data = bpy.data.cameras.new("BlockoutCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ortho_h
    cam = bpy.data.objects.new("BlockoutCam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    written = []
    for v in views:
        d, _ = VIEWS[v]
        cam.location = Vector(centre) + d * 6.0
        cam.rotation_mode = 'QUATERNION'
        cam.rotation_quaternion = (-d).to_track_quat('-Z', 'Y')
        path = os.path.join(out_dir, f"{prefix}_{v}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)
        print(f"[blockout] wrote {path}")
    bpy.data.objects.remove(cam, do_unlink=True)
    return written


def export_glb(path, coll_name):
    objs = [o for o in bpy.data.collections[coll_name].objects]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB',
                              use_selection=True, export_apply=False,
                              export_yup=True)
    print(f"[blockout] exported {path}")


# ---------------------------------------------------------------------------
# entry point
# ---------------------------------------------------------------------------


def build(pose, D):
    _clear()
    coll = bpy.data.collections.new(f"Kuro_Blockout_{pose}")
    bpy.context.scene.collection.children.link(coll)
    rig = None
    if pose == "riding":
        rig = build_bike(coll, D, D["ride_crank_phase"])
    build_rider(coll, D, pose, rig)
    _world_and_lights()
    return coll


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    root = None
    do_render = "--no-render" not in argv
    if "--out" in argv:
        root = argv[argv.index("--out") + 1]
    root = root or os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

    asset_dir = os.path.join(root, "Assets", "Kuro", "Blockout")
    render_dir = os.path.join(root, "good_graphics", "kuro_blockout")
    os.makedirs(asset_dir, exist_ok=True)
    os.makedirs(render_dir, exist_ok=True)

    D = DIMS

    # ---- riding rig (refs 03 / 04) -------------------------------------------
    coll = build("riding", D)
    export_glb(os.path.join(asset_dir, "kuro_blockout_riding.glb"), coll.name)
    if do_render:
        render_views(render_dir, "blockout_riding", ["left", "right"],
                     Vector((0.0, 0.06, 0.75)), 1.72, res=(660, 1040))

    # ---- standing rig (ref 05) -----------------------------------------------
    coll = build("standing", D)
    export_glb(os.path.join(asset_dir, "kuro_blockout_standing.glb"), coll.name)
    if do_render:
        render_views(render_dir, "blockout_standing", ["front", "right", "back"],
                     Vector((0.0, 0.0, 0.70)), 1.60, res=(420, 1040))

    # ---- bare bike (ref 09 side) ---------------------------------------------
    _clear()
    coll = bpy.data.collections.new("Kuro_Blockout_bike")
    bpy.context.scene.collection.children.link(coll)
    build_bike(coll, D, D["ride_crank_phase"])
    _world_and_lights()
    export_glb(os.path.join(asset_dir, "kuro_blockout_bike.glb"), coll.name)
    if do_render:
        render_views(render_dir, "blockout_bike", ["right"],
                     Vector((0.0, 0.06, 0.38)), 1.55, res=(1040, 660))

    print("[blockout] DONE")


main()

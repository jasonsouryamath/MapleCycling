"""
build_sakura_living.py - Authors 3D assets for L1 "Sakura Pass: alive".

Models generated:
  1. Sakura_Train_Car.glb        - Japanese Hakone/Enoden style 2-car mountain train
  2. Sakura_Trestle_Viaduct.glb  - Timber/steel trestle viaduct bent & deck span
  3. Sakura_Level_Crossing.glb   - Japanese rural railway crossing post (踏切) with pivoting barrier arm
  4. Sakura_Gondola_Cabin.glb    - Panoramic alpine ropeway gondola cabin with suspension hanger
  5. Sakura_Gondola_Station.glb  - Terminal station with 3.2m rotating bullwheel
  6. Sakura_Gondola_Pylon.glb    - Valley steel lattice tower with cable guide sheaves
  7. Sakura_Koi.glb              - Japanese Nishikigoi (ornamental carp)
  8. Sakura_Crow.glb             - Countryside soaring crow / black kite

Run via:
  tools/blender-4.5.10-windows-x64/blender.exe -b -P tools/blender/build_sakura_living.py
"""

import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S
import minato_lib as M

OUT_DIR = os.path.join(S.repo_root(), "Assets", "Environment", "SakuraPass", "Living")
os.makedirs(OUT_DIR, exist_ok=True)

# --------------------------------------------------------------------------- PBR Palette
PALETTE = {
    "SakuraLiving_Train_Green":     (0.11, 0.27, 0.19, 1.0),  # Enoden dark evergreen
    "SakuraLiving_Train_Cream":     (0.96, 0.92, 0.82, 1.0),  # Classic ivory/cream
    "SakuraLiving_Train_Stripe":    (0.85, 0.25, 0.12, 1.0),  # Vermilion accent stripe
    "SakuraLiving_Train_Glass":     (0.40, 0.55, 0.65, 0.8),  # Tinted window glass
    "SakuraLiving_Steel_Dark":      (0.18, 0.20, 0.22, 1.0),  # Underframe, bogies, pylons
    "SakuraLiving_Steel_Silver":    (0.68, 0.70, 0.72, 1.0),  # Rails, window sashes, wheels
    "SakuraLiving_Headlight":       (1.00, 0.92, 0.65, 1.0),  # Warm halogen headlight
    "SakuraLiving_Taillight":       (0.95, 0.08, 0.08, 1.0),  # Ruby taillight
    "SakuraLiving_Trestle_Timber":  (0.24, 0.18, 0.13, 1.0),  # Weathered mountain cedar
    "SakuraLiving_Warning_Yellow":  (0.96, 0.78, 0.05, 1.0),  # JIS crossing yellow
    "SakuraLiving_Warning_Black":   (0.12, 0.12, 0.13, 1.0),  # JIS crossing black
    "SakuraLiving_Gondola_Body":    (0.95, 0.88, 0.88, 1.0),  # Sakura blush ivory
    "SakuraLiving_Gondola_Trim":    (0.82, 0.24, 0.32, 1.0),  # Cherry blossom magenta
    "SakuraLiving_Koi_White":       (0.95, 0.95, 0.95, 1.0),  # White porcelain koi scales
    "SakuraLiving_Koi_Red":         (0.92, 0.18, 0.08, 1.0),  # Kohaku vermilion patches
    "SakuraLiving_Koi_Gold":        (0.98, 0.74, 0.18, 1.0),  # Ogon metallic gold
    "SakuraLiving_Plumage_Dark":    (0.15, 0.15, 0.18, 1.0),  # Raven/crow feathers
}


def get_mat(name, roughness=0.6, metallic=0.0):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    col = PALETTE.get(name, (0.5, 0.5, 0.5, 1.0))
    if "Steel" in name or "Silver" in name or "Gold" in name:
        metallic = 0.75
        roughness = 0.35
    elif "Glass" in name:
        roughness = 0.1
        metallic = 0.2
    elif "Timber" in name or "Warning" in name:
        roughness = 0.8
    return S.pbr_material(name, base_color=col, roughness=roughness, metallic=metallic)


def export_living_glb(objs, filename):
    filepath = os.path.join(OUT_DIR, filename)
    S.select_only(objs)
    # Ensure export directory exists
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    # Temporarily override S.blender_assets_dir to export directly to OUT_DIR
    old_fn = S.blender_assets_dir
    S.blender_assets_dir = lambda: OUT_DIR
    try:
        S.export_glb(objs, filename)
    finally:
        S.blender_assets_dir = old_fn


# --------------------------------------------------------------------------- 1. Train Car
def build_train_car():
    print("[sakura-living] building Sakura_Train_Car.glb...")
    S.reset_scene()

    parts = []
    # Car body dimensions: Length 12m, Width 2.6m, Height 3.2m
    # Centered at (0, 1.6, 0) in Unity space
    L, W, H = 12.0, 2.6, 3.0

    # 1. Lower Body (Green) - from Y=0.7 to Y=1.9
    lower = M.box("Body_Lower", (0.0, 1.3, 0.0), (W, 1.2, L))
    S.assign_material(lower, get_mat("SakuraLiving_Train_Green"))
    parts.append(lower)

    # 2. Upper Body (Cream) - from Y=1.9 to Y=3.1
    upper = M.box("Body_Upper", (0.0, 2.5, 0.0), (W * 0.98, 1.2, L * 0.99))
    S.assign_material(upper, get_mat("SakuraLiving_Train_Cream"))
    parts.append(upper)

    # 3. Curved Roof (Dark Green / Charcoal)
    roof = M.box("Roof", (0.0, 3.2, 0.0), (W * 0.92, 0.25, L * 0.98))
    S.assign_material(roof, get_mat("SakuraLiving_Train_Green"))
    parts.append(roof)

    # 4. Accent Pinstripe along belt line
    stripe_l = M.box("Stripe_L", (-W * 0.505, 1.9, 0.0), (0.02, 0.08, L * 0.99))
    stripe_r = M.box("Stripe_R", (W * 0.505, 1.9, 0.0), (0.02, 0.08, L * 0.99))
    S.assign_material(stripe_l, get_mat("SakuraLiving_Train_Stripe"))
    S.assign_material(stripe_r, get_mat("SakuraLiving_Train_Stripe"))
    parts.extend([stripe_l, stripe_r])

    # 5. Side Windows (Glass)
    for z in [-4.0, -2.4, -0.8, 0.8, 2.4, 4.0]:
        win_l = M.box(f"Win_L_{z}", (-W * 0.502, 2.5, z), (0.02, 0.8, 1.1))
        win_r = M.box(f"Win_R_{z}", (W * 0.502, 2.5, z), (0.02, 0.8, 1.1))
        S.assign_material(win_l, get_mat("SakuraLiving_Train_Glass"))
        S.assign_material(win_r, get_mat("SakuraLiving_Train_Glass"))
        parts.extend([win_l, win_r])

    # 6. Front Windshield & Cab (at +Z end = forward)
    windshield = M.box("Windshield_Fwd", (0.0, 2.5, L * 0.502), (W * 0.75, 0.85, 0.02))
    S.assign_material(windshield, get_mat("SakuraLiving_Train_Glass"))
    parts.append(windshield)

    # Headlights & Taillights
    hl_l = M.tube("HL_L", (-0.75, 1.5, L * 0.505), (-0.75, 1.5, L * 0.515), radius=0.14, segments=8)
    hl_r = M.tube("HL_R", (0.75, 1.5, L * 0.505), (0.75, 1.5, L * 0.515), radius=0.14, segments=8)
    S.assign_material(hl_l, get_mat("SakuraLiving_Headlight"))
    S.assign_material(hl_r, get_mat("SakuraLiving_Headlight"))
    parts.extend([hl_l, hl_r])

    tail_l = M.tube("TL_L", (-0.95, 2.8, -L * 0.505), (-0.95, 2.8, -L * 0.515), radius=0.08, segments=8)
    tail_r = M.tube("TL_R", (0.95, 2.8, -L * 0.505), (0.95, 2.8, -L * 0.515), radius=0.08, segments=8)
    S.assign_material(tail_l, get_mat("SakuraLiving_Taillight"))
    S.assign_material(tail_r, get_mat("SakuraLiving_Taillight"))
    parts.extend([tail_l, tail_r])

    # 7. Underframe & Bogies (Y=0.0 to 0.7)
    underframe = M.box("Underframe", (0.0, 0.55, 0.0), (W * 0.85, 0.3, L * 0.95))
    S.assign_material(underframe, get_mat("SakuraLiving_Steel_Dark"))
    parts.append(underframe)

    for bz in [-3.5, 3.5]:
        bogie = M.box(f"Bogie_{bz}", (0.0, 0.35, bz), (W * 0.75, 0.25, 2.2))
        S.assign_material(bogie, get_mat("SakuraLiving_Steel_Dark"))
        parts.append(bogie)
        # Wheels
        for wz in [bz - 0.75, bz + 0.75]:
            wl = M.tube(f"Wheel_L_{wz}", (-W * 0.42, 0.35, wz), (-W * 0.40, 0.35, wz), radius=0.35, segments=12)
            wr = M.tube(f"Wheel_R_{wz}", (W * 0.40, 0.35, wz), (W * 0.42, 0.35, wz), radius=0.35, segments=12)
            S.assign_material(wl, get_mat("SakuraLiving_Steel_Silver"))
            S.assign_material(wr, get_mat("SakuraLiving_Steel_Silver"))
            parts.extend([wl, wr])

    # 8. Diamond Pantograph on roof near rear (Z = -3.2)
    p_base = M.box("Panto_Base", (0.0, 3.35, -3.2), (1.4, 0.08, 1.2))
    S.assign_material(p_base, get_mat("SakuraLiving_Steel_Dark"))
    p_arm1 = M.beam("Panto_Arm1", (-0.5, 3.38, -3.6), (0.0, 4.05, -3.2), width=0.05, height=0.05)
    p_arm2 = M.beam("Panto_Arm2", (0.5, 3.38, -3.6), (0.0, 4.05, -3.2), width=0.05, height=0.05)
    p_arm3 = M.beam("Panto_Arm3", (-0.5, 3.38, -2.8), (0.0, 4.05, -3.2), width=0.05, height=0.05)
    p_arm4 = M.beam("Panto_Arm4", (0.5, 3.38, -2.8), (0.0, 4.05, -3.2), width=0.05, height=0.05)
    p_shoe = M.beam("Panto_Shoe", (-1.1, 4.08, -3.2), (1.1, 4.08, -3.2), width=0.08, height=0.04)
    for p in [p_arm1, p_arm2, p_arm3, p_arm4, p_shoe]:
        S.assign_material(p, get_mat("SakuraLiving_Steel_Silver"))
        parts.append(p)
    parts.append(p_base)

    export_living_glb(parts, "Sakura_Train_Car.glb")


# --------------------------------------------------------------------------- 2. Trestle Viaduct
def build_trestle_viaduct():
    print("[sakura-living] building Sakura_Trestle_Viaduct.glb...")
    S.reset_scene()

    parts = []
    # Viaduct Bent: Span length = 8m, Height = 10m, Width = 3.6m
    deck = M.box("Viaduct_Deck", (0.0, 0.0, 0.0), (3.4, 0.45, 8.0))
    S.assign_material(deck, get_mat("SakuraLiving_Trestle_Timber"))
    parts.append(deck)

    # Rails and Sleepers on top of deck
    for sz in range(-4, 5):
        sleeper = M.box(f"Sleeper_{sz}", (0.0, 0.28, sz * 0.9), (2.2, 0.12, 0.22))
        S.assign_material(sleeper, get_mat("SakuraLiving_Trestle_Timber"))
        parts.append(sleeper)

    rail_l = M.beam("Rail_L", (-0.53, 0.38, -4.0), (-0.53, 0.38, 4.0), width=0.08, height=0.12)
    rail_r = M.beam("Rail_R", (0.53, 0.38, -4.0), (0.53, 0.38, 4.0), width=0.08, height=0.12)
    S.assign_material(rail_l, get_mat("SakuraLiving_Steel_Silver"))
    S.assign_material(rail_r, get_mat("SakuraLiving_Steel_Silver"))
    parts.extend([rail_l, rail_r])

    # Battered Timber Legs (4 posts sloping downward)
    for bz in [-3.2, 3.2]:
        p_l = M.beam(f"Post_L_{bz}", (-1.5, -0.2, bz), (-2.4, -9.8, bz), width=0.32, height=0.32)
        p_r = M.beam(f"Post_R_{bz}", (1.5, -0.2, bz), (2.4, -9.8, bz), width=0.32, height=0.32)
        tie1 = M.beam(f"Tie1_{bz}", (-1.8, -3.2, bz), (1.8, -3.2, bz), width=0.22, height=0.22)
        tie2 = M.beam(f"Tie2_{bz}", (-2.1, -6.5, bz), (2.1, -6.5, bz), width=0.22, height=0.22)
        tie3 = M.beam(f"Tie3_{bz}", (-2.35, -9.5, bz), (2.35, -9.5, bz), width=0.25, height=0.25)
        # Cross diagonals
        diag1 = M.beam(f"Diag1_{bz}", (-1.8, -3.2, bz), (2.1, -6.5, bz), width=0.14, height=0.14)
        diag2 = M.beam(f"Diag2_{bz}", (1.8, -3.2, bz), (-2.1, -6.5, bz), width=0.14, height=0.14)
        for post in [p_l, p_r, tie1, tie2, tie3, diag1, diag2]:
            S.assign_material(post, get_mat("SakuraLiving_Trestle_Timber"))
            parts.append(post)

    export_living_glb(parts, "Sakura_Trestle_Viaduct.glb")


# --------------------------------------------------------------------------- 3. Level Crossing (踏切)
def build_level_crossing():
    print("[sakura-living] building Sakura_Level_Crossing.glb...")
    S.reset_scene()

    parts = []
    # Main Post (Yellow & Black striped post)
    post = M.tube("Crossing_Post", (0.0, 0.0, 0.0), (0.0, 3.5, 0.0), radius=0.08, segments=10)
    S.assign_material(post, get_mat("SakuraLiving_Warning_Yellow"))
    parts.append(post)

    # Black warning rings on post
    for y in [0.7, 1.4, 2.1, 2.8]:
        ring = M.tube(f"Stripe_{y}", (0.0, y, 0.0), (0.0, y + 0.25, 0.0), radius=0.085, segments=10)
        S.assign_material(ring, get_mat("SakuraLiving_Warning_Black"))
        parts.append(ring)

    # Crossbuck X sign at top (Y = 3.2)
    cross1 = M.box("Crossbuck_1", (0.0, 3.2, 0.0), (0.16, 1.2, 0.03))
    cross2 = M.box("Crossbuck_2", (0.0, 3.2, 0.0), (0.16, 1.2, 0.03))
    cross1.rotation_euler = (0.0, 0.0, math.radians(45.0))
    cross2.rotation_euler = (0.0, 0.0, math.radians(-45.0))
    S.assign_material(cross1, get_mat("SakuraLiving_Warning_Yellow"))
    S.assign_material(cross2, get_mat("SakuraLiving_Warning_Yellow"))
    parts.extend([cross1, cross2])

    # Dual Red Flashing Lights (Y = 2.5)
    light_bar = M.beam("Light_Bar", (-0.45, 2.5, 0.0), (0.45, 2.5, 0.0), width=0.08, height=0.08)
    S.assign_material(light_bar, get_mat("SakuraLiving_Steel_Dark"))
    parts.append(light_bar)

    for lx in [-0.45, 0.45]:
        hood = M.tube(f"Light_Hood_{lx}", (lx, 2.5, 0.0), (lx, 2.5, 0.16), radius=0.15, segments=10)
        lens = M.tube(f"Light_Lens_{lx}", (lx, 2.5, 0.05), (lx, 2.5, 0.12), radius=0.11, segments=10)
        S.assign_material(hood, get_mat("SakuraLiving_Steel_Dark"))
        S.assign_material(lens, get_mat("SakuraLiving_Taillight"))
        parts.extend([hood, lens])

    # Top Gong Dome (Y = 3.55)
    gong = M.tube("Gong", (0.0, 3.5, 0.0), (0.0, 3.65, 0.0), radius=0.14, segments=10)
    S.assign_material(gong, get_mat("SakuraLiving_Steel_Dark"))
    parts.append(gong)

    # Barrier Gate Arm (Pivoting at Y = 1.0, X = 0.15)
    # 4.5m long boom arm with yellow & black stripes
    arm_pivot = (0.15, 1.0, 0.0)
    counterweight = M.box("Arm_Counterweight", (arm_pivot[0] - 0.35, arm_pivot[1], 0.0), (0.6, 0.22, 0.22))
    S.assign_material(counterweight, get_mat("SakuraLiving_Steel_Dark"))
    parts.append(counterweight)

    boom = M.beam("Barrier_Arm", (arm_pivot[0], arm_pivot[1], 0.0), (arm_pivot[0] + 4.5, arm_pivot[1], 0.0),
                  width=0.08, height=0.14)
    S.assign_material(boom, get_mat("SakuraLiving_Warning_Yellow"))
    parts.append(boom)

    # Boom black stripes
    for bx in [1.0, 2.0, 3.0, 4.0]:
        bstripe = M.box(f"BoomStripe_{bx}", (arm_pivot[0] + bx, arm_pivot[1], 0.0), (0.35, 0.145, 0.085))
        S.assign_material(bstripe, get_mat("SakuraLiving_Warning_Black"))
        parts.append(bstripe)

    export_living_glb(parts, "Sakura_Level_Crossing.glb")


# --------------------------------------------------------------------------- 4. Gondola Cabin
def build_gondola_cabin():
    print("[sakura-living] building Sakura_Gondola_Cabin.glb...")
    S.reset_scene()

    parts = []
    # Cable attachment / roller pivot at (0, 0, 0).
    # Cabin body hangs below from Y = -1.6 to Y = -3.8.
    # Length = 2.8m, Width = 1.8m, Height = 2.0m

    # Hanger Arm
    hanger_top = M.box("Hanger_Grip", (0.0, 0.0, 0.0), (0.25, 0.15, 0.45))
    hanger_rod = M.tube("Hanger_Rod", (0.0, -0.05, 0.0), (0.0, -1.6, 0.0), radius=0.06, segments=8)
    S.assign_material(hanger_top, get_mat("SakuraLiving_Steel_Dark"))
    S.assign_material(hanger_rod, get_mat("SakuraLiving_Steel_Dark"))
    parts.extend([hanger_top, hanger_rod])

    # Cabin Body (Blush Ivory)
    body = M.box("Cabin_Body", (0.0, -2.6, 0.0), (1.8, 1.8, 2.6))
    S.assign_material(body, get_mat("SakuraLiving_Gondola_Body"))
    parts.append(body)

    # Accent Trim Band
    trim = M.box("Cabin_Trim", (0.0, -2.6, 0.0), (1.82, 0.22, 2.62))
    S.assign_material(trim, get_mat("SakuraLiving_Gondola_Trim"))
    parts.append(trim)

    # Windows on all 4 sides
    win_fwd = M.box("Win_Fwd", (0.0, -2.45, 1.31), (1.5, 0.95, 0.02))
    win_back = M.box("Win_Back", (0.0, -2.45, -1.31), (1.5, 0.95, 0.02))
    win_l = M.box("Win_L", (-0.91, -2.45, 0.0), (0.02, 0.95, 2.1))
    win_r = M.box("Win_R", (0.91, -2.45, 0.0), (0.02, 0.95, 2.1))
    for win in [win_fwd, win_back, win_l, win_r]:
        S.assign_material(win, get_mat("SakuraLiving_Train_Glass"))
        parts.append(win)

    # Roof curved caps
    roof = M.box("Cabin_Roof", (0.0, -1.65, 0.0), (1.75, 0.15, 2.5))
    S.assign_material(roof, get_mat("SakuraLiving_Gondola_Body"))
    parts.append(roof)

    export_living_glb(parts, "Sakura_Gondola_Cabin.glb")


# --------------------------------------------------------------------------- 5. Gondola Station
def build_gondola_station():
    print("[sakura-living] building Sakura_Gondola_Station.glb...")
    S.reset_scene()

    parts = []
    # Station Platform: 10m long, 6m wide
    platform = M.box("Station_Platform", (0.0, 0.25, 0.0), (6.0, 0.5, 10.0))
    S.assign_material(platform, get_mat("SakuraLiving_Trestle_Timber"))
    parts.append(platform)

    # Canopy Roof Posts & Beams
    for sx in [-2.7, 2.7]:
        for sz in [-4.0, 0.0, 4.0]:
            col = M.tube(f"Col_{sx}_{sz}", (sx, 0.5, sz), (sx, 4.2, sz), radius=0.15, segments=8)
            S.assign_material(col, get_mat("SakuraLiving_Steel_Dark"))
            parts.append(col)

    roof_beam_l = M.beam("RBeam_L", (-2.7, 4.2, -4.8), (-2.7, 4.2, 4.8), width=0.25, height=0.35)
    roof_beam_r = M.beam("RBeam_R", (2.7, 4.2, -4.8), (2.7, 4.2, 4.8), width=0.25, height=0.35)
    canopy = M.box("Station_Roof", (0.0, 4.45, 0.0), (6.4, 0.25, 10.4))
    for r in [roof_beam_l, roof_beam_r, canopy]:
        S.assign_material(r, get_mat("SakuraLiving_Steel_Dark"))
        parts.append(r)

    # 3.2m Bullwheel (mounted horizontally at Y = 3.6, Z = 2.5)
    bw_hub = M.tube("Bullwheel_Hub", (0.0, 3.4, 2.5), (0.0, 3.8, 2.5), radius=0.35, segments=12)
    bw_rim = M.tube("Bullwheel_Rim", (0.0, 3.55, 2.5), (0.0, 3.65, 2.5), radius=1.6, segments=20)
    S.assign_material(bw_hub, get_mat("SakuraLiving_Steel_Silver"))
    S.assign_material(bw_rim, get_mat("SakuraLiving_Steel_Silver"))
    parts.extend([bw_hub, bw_rim])

    # Bullwheel spokes
    for k in range(6):
        ang = k * (math.pi / 3.0)
        sp_x = math.cos(ang) * 1.55
        sp_z = math.sin(ang) * 1.55
        spoke = M.beam(f"Spoke_{k}", (0.0, 3.6, 2.5), (sp_x, 3.6, 2.5 + sp_z), width=0.1, height=0.1)
        S.assign_material(spoke, get_mat("SakuraLiving_Steel_Silver"))
        parts.append(spoke)

    export_living_glb(parts, "Sakura_Gondola_Station.glb")


# --------------------------------------------------------------------------- 6. Gondola Pylon
def build_gondola_pylon():
    print("[sakura-living] building Sakura_Gondola_Pylon.glb...")
    S.reset_scene()

    parts = []
    # 16m Tapered Lattice Tower
    # Base: 3m x 3m, Top: 1.2m x 1.2m
    base_hw = 1.5
    top_hw = 0.6
    H = 16.0

    legs = [
        ((-base_hw, 0.0, -base_hw), (-top_hw, H, -top_hw)),
        ((base_hw, 0.0, -base_hw), (top_hw, H, -top_hw)),
        ((base_hw, 0.0, base_hw), (top_hw, H, top_hw)),
        ((-base_hw, 0.0, base_hw), (-top_hw, H, top_hw)),
    ]
    for idx, (a, b) in enumerate(legs):
        leg = M.beam(f"Leg_{idx}", a, b, width=0.22, height=0.22)
        S.assign_material(leg, get_mat("SakuraLiving_Steel_Dark"))
        parts.append(leg)

    # Cross Struts & Braces at 3 tiers
    for y in [4.0, 8.0, 12.0]:
        t = y / H
        w = base_hw * (1.0 - t) + top_hw * t
        strut1 = M.beam(f"Strut1_{y}", (-w, y, -w), (w, y, -w), width=0.12, height=0.12)
        strut2 = M.beam(f"Strut2_{y}", (w, y, -w), (w, y, w), width=0.12, height=0.12)
        strut3 = M.beam(f"Strut3_{y}", (w, y, w), (-w, y, w), width=0.12, height=0.12)
        strut4 = M.beam(f"Strut4_{y}", (-w, y, w), (-w, y, -w), width=0.12, height=0.12)
        for s in [strut1, strut2, strut3, strut4]:
            S.assign_material(s, get_mat("SakuraLiving_Steel_Dark"))
            parts.append(s)

    # Top Crossarm with Cable Sheaves
    crossarm = M.beam("Crossarm", (-2.8, H, 0.0), (2.8, H, 0.0), width=0.35, height=0.35)
    S.assign_material(crossarm, get_mat("SakuraLiving_Steel_Dark"))
    parts.append(crossarm)

    # Cable Sheaves (roller assemblies on both sides)
    for sx in [-2.4, 2.4]:
        sheave = M.tube(f"Sheave_{sx}", (sx, H - 0.25, -0.4), (sx, H - 0.25, 0.4), radius=0.28, segments=12)
        S.assign_material(sheave, get_mat("SakuraLiving_Steel_Silver"))
        parts.append(sheave)

    export_living_glb(parts, "Sakura_Gondola_Pylon.glb")


# --------------------------------------------------------------------------- 7. Koi Fish
def build_koi():
    print("[sakura-living] building Sakura_Koi.glb...")
    S.reset_scene()

    parts = []
    # Streamlined fish body: Length 0.65m, Width 0.16m, Height 0.18m
    body = M.box("Koi_Body", (0.0, 0.0, 0.0), (0.16, 0.18, 0.65), smooth=True)
    S.assign_material(body, get_mat("SakuraLiving_Koi_White"))
    parts.append(body)

    # Kohaku Vermilion Patch on back
    patch = M.box("Koi_Patch", (0.0, 0.06, 0.08), (0.14, 0.09, 0.28), smooth=True)
    S.assign_material(patch, get_mat("SakuraLiving_Koi_Red"))
    parts.append(patch)

    # Tail fin (fanned back)
    tail = M.beam("Koi_Tail", (0.0, 0.0, -0.32), (0.0, 0.0, -0.52), width=0.03, height=0.22)
    S.assign_material(tail, get_mat("SakuraLiving_Koi_Red"))
    parts.append(tail)

    # Pectoral fins (angled out)
    fin_l = M.beam("Fin_L", (-0.08, -0.04, 0.15), (-0.24, -0.08, 0.05), width=0.02, height=0.08)
    fin_r = M.beam("Fin_R", (0.08, -0.04, 0.15), (0.24, -0.08, 0.05), width=0.02, height=0.08)
    S.assign_material(fin_l, get_mat("SakuraLiving_Koi_White"))
    S.assign_material(fin_r, get_mat("SakuraLiving_Koi_White"))
    parts.extend([fin_l, fin_r])

    export_living_glb(parts, "Sakura_Koi.glb")


# --------------------------------------------------------------------------- 8. Soaring Crow / Kite
def build_crow():
    print("[sakura-living] building Sakura_Crow.glb...")
    S.reset_scene()

    parts = []
    # Soaring bird: Wingspan 1.1m, Body length 0.55m
    body = M.tube("Crow_Body", (0.0, 0.0, -0.22), (0.0, 0.04, 0.25), radius=0.08, segments=8)
    head = M.tube("Crow_Head", (0.0, 0.04, 0.22), (0.0, 0.08, 0.38), radius=0.05, segments=6)
    S.assign_material(body, get_mat("SakuraLiving_Plumage_Dark"))
    S.assign_material(head, get_mat("SakuraLiving_Plumage_Dark"))
    parts.extend([body, head])

    # Outstretched Wings (slight dihedral angle)
    wing_l = M.beam("Wing_L", (-0.05, 0.04, 0.05), (-0.55, 0.12, 0.0), width=0.18, height=0.03)
    wing_r = M.beam("Wing_R", (0.05, 0.04, 0.05), (0.55, 0.12, 0.0), width=0.18, height=0.03)
    tail = M.beam("Tail_Feathers", (0.0, 0.02, -0.22), (0.0, 0.01, -0.42), width=0.16, height=0.02)
    for w in [wing_l, wing_r, tail]:
        S.assign_material(w, get_mat("SakuraLiving_Plumage_Dark"))
        parts.append(w)

    export_living_glb(parts, "Sakura_Crow.glb")


def main():
    print("=== Building Sakura Pass Living World Assets ===")
    build_train_car()
    build_trestle_viaduct()
    build_level_crossing()
    build_gondola_cabin()
    build_gondola_station()
    build_gondola_pylon()
    build_koi()
    build_crow()
    print("=== All Sakura Pass Living World Assets Built Successfully ===")


if __name__ == "__main__":
    main()

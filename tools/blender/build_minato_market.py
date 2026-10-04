"""
MINATO COAST - CAFE / MARKET DISTRICT (milestone 2 of the "After" concept recreation).

The right-side plaza along the waterfront boulevard currently renders as several hundred metres
of BARE WHITE TILE (see reference/good_graphics/diag_minato_target_crowd.png and
diag_minato_target_skyline.png). In the "After" boards - Minamo_01_PortDeparture_After.png and
Minamo_04_FarShore_After.png - that same band is a lively Mediterranean market/cafe promenade:
striped-awning market stalls, blue-and-white pop-up tents, red and cream cafe parasols over
round tables, browsing and seated crowds.

This module authors that family:

  1. MARKET STALL (blue/white and red/white variants) - four timber posts, a boarded back wall,
     a serving counter, a shelf, and a STRIPED sloping awning with a hanging valance.
  2. CAFE SET (red/cream-striped and plain-cream variants) - a round pedestal table, three
     chairs, and a parasol on a mast.
  3. MARKET TENT - the square blue/white peaked pop-up canopy the boards dot through the market.
  4. CRATE PILE - produce crates and sacks, for filling the organic gaps between stalls.

Everything is authored in UNITY coordinates through ``minato_lib`` primitives (converted via
``S.u2b`` at mesh build time) with:

    +Z = forward (the stall's OPEN FRONT / the direction shoppers approach from)
    +X = to the stall's left as you face it      +Y = up      origin at ground centre

AUTHORING NOTES THAT MATTER
---------------------------
* STRIPES ARE GEOMETRY, NOT TEXTURE. Each awning and each parasol is built as N alternating
  spanwise panels carrying two different material slots. A 256 px stripe texture would mip
  down to a flat mush by 60 m, and the plaza is seen mostly from 40-200 m; alternating panels
  stay crisp at every distance and cost ~300 triangles.
* Every cloth panel is a CLOSED THIN SHELL via ``build_minato_boulevard.sheet`` (front sheet +
  back sheet + edge strips), never a single quad. MapleRide/SakuraCel and MinatoCel CULL
  BACKFACES, so a single-quad awning is invisible from underneath - which is exactly the angle
  a rider on the boulevard sees a cafe parasol from.
* ALL dimensions and colours here are PROVISIONAL illustrative tuning read off the concept
  boards; nothing in the design handoff fixes them.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402
from build_minato_boulevard import sheet, blob  # noqa: E402


# ============================================================ provisional tuning

# --- market stall
STALL_W = 2.80          # overall width across the front
STALL_D = 1.90          # front-to-back depth
POST_H = 2.22
AWN_HALF_W = 1.56       # the awning oversails the posts on both sides
AWN_BACK_Z = -1.02
AWN_FRONT_Z = 1.64
AWN_BACK_Y = 2.46
AWN_FRONT_Y = 2.03
AWN_SAG = 0.07
AWN_T = 0.045           # cloth thickness
VALANCE_DROP = 0.30
AWN_STRIPES = 11        # ODD, so both outer edges land on the colour stripe

# --- cafe set
TABLE_TOP_Y = 0.745
TABLE_R = 0.46
# Seat top height. Matched to MinatoCrowdPopulation.BenchSeatHeight (0.38 m) so the approved
# seated crowd archetype lands ON the chair instead of hovering over it or sinking through it.
CHAIR_SEAT_Y = 0.40
CHAIR_RING_R = 0.80     # chair centre distance from the table axis
CAFE_CHAIRS = 3
PARASOL_MAST_Y = 2.34
PARASOL_R = 1.36
PARASOL_RIM_Y = 1.86
PARASOL_WEDGES = 8

# --- pop-up tent
TENT_HALF = 1.55
TENT_EAVE_Y = 2.20
TENT_APEX_Y = 3.06
TENT_RIDGE_HALF = 0.17
TENT_STRIPES = 5
TENT_VALANCE = 0.22


# ============================================================ striped-cloth helper

def striped(tag, corner_fn, stripes, nv, thickness, nu=1):
    """
    Split a parametric cloth surface into ``stripes`` spanwise CLOSED shells.

    Returns ``(even_panels, odd_panels)`` so the caller can paint the two lists with two
    different palette slots - which is how every awning and parasol here gets its stripe.
    Doing this in geometry rather than in a texture is deliberate; see the module docstring.
    """
    even, odd = [], []
    for k in range(stripes):
        u0 = k / stripes
        u1 = (k + 1) / stripes

        def band(u, v, _u0=u0, _u1=u1):
            return corner_fn(_u0 + (_u1 - _u0) * u, v)

        panel = sheet(f"{tag}_s{k}", band, nu, nv, thickness)
        (even if k % 2 == 0 else odd).append(panel)
    return even, odd


def _cloth(parts, name, slot):
    """Finish a cloth group: no bevel, no weld (the shells must stay separate), flat shaded."""
    obj = M.finish(parts, name, bevel=0.0, weld=0.0, smooth_angle=None)
    M.paint(obj, slot)
    return obj


# ============================================================ 1. market stall

def _stall_awning(tag):
    """The sloping striped awning plus its hanging front valance, stripes aligned."""
    dz = AWN_FRONT_Z - AWN_BACK_Z
    dy = AWN_FRONT_Y - AWN_BACK_Y

    def roof(u, v):
        x = -AWN_HALF_W + u * 2.0 * AWN_HALF_W
        z = AWN_BACK_Z + v * dz
        y = AWN_BACK_Y + v * dy - AWN_SAG * math.sin(v * math.pi)
        slope_y = dy - AWN_SAG * math.pi * math.cos(v * math.pi)
        return (x, y, z), Vector((0.0, dz, -slope_y))

    def valance(u, v):
        x = -AWN_HALF_W + u * 2.0 * AWN_HALF_W
        z = AWN_FRONT_Z + 0.07 * v
        y = AWN_FRONT_Y - VALANCE_DROP * v
        return (x, y, z), Vector((0.0, 0.07, VALANCE_DROP))

    a1, b1 = striped(f"{tag}_roof", roof, AWN_STRIPES, 3, AWN_T)
    a2, b2 = striped(f"{tag}_val", valance, AWN_STRIPES, 1, AWN_T)
    return a1 + a2, b1 + b2


def _market_stall(tag, stripe_slot):
    """
    A booth: four posts, a boarded back wall, a serving counter with a shelf above it, crated
    and piled goods on the counter, under a striped awning.
    """
    r = M.rng(9001 + len(stripe_slot))
    px, pz = 1.32, 0.88
    timber, steel, goods_r, goods_g, goods_o = [], [], [], [], []

    # --- frame
    for sx in (-1, 1):
        for sz in (-1, 1):
            timber.append(M.beam(f"{tag}_post{sx}{sz}", (sx * px, 0.0, sz * pz),
                                 (sx * px, POST_H, sz * pz), 0.10, 0.10, uv_per_m=1.2))
    for sz in (-1, 1):
        timber.append(M.beam(f"{tag}_rail{sz}", (-px - 0.06, POST_H - 0.05, sz * pz),
                             (px + 0.06, POST_H - 0.05, sz * pz), 0.09, 0.09,
                             up_hint=(0, 1, 0), uv_per_m=1.2))
    # Boarded back wall - without it the stall is a see-through skeleton at every angle.
    for k in range(4):
        y = 0.34 + k * 0.44
        timber.append(M.box(f"{tag}_bw{k}", (0.0, y, -pz - 0.02), (2.62, 0.42, 0.07),
                            uv_per_m=1.3))

    # --- counter + shelf
    timber.append(M.box(f"{tag}_ctop", (0.0, 0.96, 0.52), (2.66, 0.09, 1.00), uv_per_m=1.2))
    timber.append(M.box(f"{tag}_cfront", (0.0, 0.46, 0.99), (2.66, 0.92, 0.08), uv_per_m=1.2))
    for sx in (-1, 1):
        timber.append(M.box(f"{tag}_cside{sx}", (sx * 1.29, 0.46, 0.52), (0.08, 0.92, 1.00),
                            uv_per_m=1.2))
    timber.append(M.box(f"{tag}_shelf", (0.0, 1.56, -0.70), (2.46, 0.07, 0.36), uv_per_m=1.2))
    steel.append(M.tube(f"{tag}_shbr0", (-1.0, 1.52, -0.86), (-1.0, 1.52, -0.54), 0.018,
                        segments=5))
    steel.append(M.tube(f"{tag}_shbr1", (1.0, 1.52, -0.86), (1.0, 1.52, -0.54), 0.018,
                        segments=5))

    # --- goods: crates on the counter and a produce pile in front of each crate
    crate_x = (-0.92, -0.05, 0.86)
    for ci, cx in enumerate(crate_x):
        h = 0.30 + float(r.uniform(0.0, 0.10))
        timber.append(M.box(f"{tag}_cr{ci}", (cx, 1.00 + h * 0.5, 0.18), (0.62, h, 0.52),
                            uv_per_m=1.6))
        # Produce mounded above the crate rim.
        for j in range(9):
            bx = cx + float(r.uniform(-0.24, 0.24))
            bz = 0.18 + float(r.uniform(-0.19, 0.19))
            by = 1.00 + h + float(r.uniform(0.02, 0.13))
            rad = float(r.uniform(0.075, 0.115))
            bucket = (goods_r, goods_g, goods_o)[(ci + j) % 3]
            bucket.append(blob(f"{tag}_p{ci}{j}", (bx, by, bz), rad, squash=(1.0, 0.9, 1.0)))
    # Sacks / boxes stacked on the ground behind the counter.
    for k in range(3):
        timber.append(M.box(f"{tag}_sk{k}", (float(r.uniform(-1.0, 1.0)), 0.20 + k * 0.02,
                                             -0.45), (0.46, 0.40, 0.40), uv_per_m=1.6))

    warm, cool = _stall_awning(tag)

    t = M.finish(timber, f"{tag}_t", bevel=0.010, smooth_angle=36.0)
    st = M.finish(steel, f"{tag}_st", bevel=0.005, smooth_angle=34.0)
    gr = M.finish(goods_r, f"{tag}_gr", bevel=0.0, weld=0.0, smooth_angle=40.0)
    gg = M.finish(goods_g, f"{tag}_gg", bevel=0.0, weld=0.0, smooth_angle=40.0)
    go = M.finish(goods_o, f"{tag}_go", bevel=0.0, weld=0.0, smooth_angle=40.0)
    M.paint(t, "timber")
    M.paint(st, "steel_dark")
    M.paint(gr, "produce_red")
    M.paint(gg, "produce_green")
    M.paint(go, "produce_orange")
    ca = _cloth(warm, f"{tag}_ca", stripe_slot)
    cb = _cloth(cool, f"{tag}_cb", "awning_cream")
    return S.join([t, st, gr, gg, go, ca, cb], f"Minato_City_{tag}_LOD0")


def market_stall_blue():
    return _market_stall("MarketStall", "awning_blue")


def market_stall_red():
    return _market_stall("MarketStallB", "awning_red")


# ============================================================ 2. cafe set

def _chair(tag, angle):
    """
    One cafe chair at ``angle`` radians around the table, facing inwards.

    Built from oriented beams rather than axis-aligned boxes so the three chairs around the
    table are genuinely rotated pieces of furniture, not three copies of the same box.
    """
    out = Vector((math.cos(angle), 0.0, math.sin(angle)))          # away from the table
    rt = Vector((-out.z, 0.0, out.x))                              # across the seat
    c = out * CHAIR_RING_R
    parts = []
    for sx in (-1, 1):
        for sz in (-1, 1):
            f = c + rt * (sx * 0.165) + out * (sz * 0.165)
            parts.append(M.tube(f"{tag}_lg{sx}{sz}", (f.x, 0.0, f.z),
                                (f.x, CHAIR_SEAT_Y - 0.03, f.z), 0.022, segments=5))
    a = c - out * 0.20 + Vector((0.0, CHAIR_SEAT_Y - 0.03, 0.0))
    b = c + out * 0.20 + Vector((0.0, CHAIR_SEAT_Y - 0.03, 0.0))
    parts.append(M.beam(f"{tag}_seat", a, b, 0.42, 0.06, up_hint=(0, 1, 0), uv_per_m=1.6))
    back = c + out * 0.185
    parts.append(M.beam(f"{tag}_back", (back.x, CHAIR_SEAT_Y + 0.06, back.z),
                        (back.x, CHAIR_SEAT_Y + 0.46, back.z), 0.40, 0.05,
                        up_hint=(out.x, 0.0, out.z), uv_per_m=1.6))
    return parts


def _parasol(tag, slot_a, slot_b):
    """A parasol: mast, hub, finial and a domed canopy of alternating radial wedges."""
    steel = [
        M.tube(f"{tag}_mast", (0.0, 0.0, 0.0), (0.0, PARASOL_MAST_Y, 0.0), 0.036, segments=8),
        M.tube(f"{tag}_hub", (0.0, PARASOL_MAST_Y - 0.44, 0.0),
               (0.0, PARASOL_MAST_Y - 0.28, 0.0), 0.062, segments=8),
        M.tube(f"{tag}_fin", (0.0, PARASOL_MAST_Y, 0.0), (0.0, PARASOL_MAST_Y + 0.14, 0.0),
               0.048, segments=8, taper=0.25),
        M.tube(f"{tag}_foot", (0.0, 0.0, 0.0), (0.0, 0.07, 0.0), 0.30, segments=12),
    ]

    apex_y = PARASOL_MAST_Y - 0.10
    drop = apex_y - PARASOL_RIM_Y

    def canopy(u, v):
        a = u * math.tau
        rad = 0.05 + v * (PARASOL_R - 0.05)
        y = apex_y - drop * (v ** 1.35)
        slope = drop * 1.35 * (max(v, 1e-3) ** 0.35) / (PARASOL_R - 0.05)
        n = Vector((math.cos(a) * slope, 1.0, math.sin(a) * slope))
        return (math.cos(a) * rad, y, math.sin(a) * rad), n

    even, odd = striped(f"{tag}_cn", canopy, PARASOL_WEDGES, 3, 0.035, nu=2)

    st = M.finish(steel, f"{tag}_ps", bevel=0.006, smooth_angle=34.0)
    M.paint(st, "steel_dark")
    ca = _cloth(even, f"{tag}_ca", slot_a)
    cb = _cloth(odd, f"{tag}_cb", slot_b)
    return [st, ca, cb]


def _cafe_set(tag, slot_a, slot_b):
    steel = [
        M.tube(f"{tag}_ped", (0.0, 0.04, 0.0), (0.0, TABLE_TOP_Y - 0.06, 0.0), 0.055,
               segments=10),
        M.tube(f"{tag}_base", (0.0, 0.0, 0.0), (0.0, 0.055, 0.0), 0.30, segments=14),
    ]
    for i in range(CAFE_CHAIRS):
        steel += _chair(f"{tag}_ch{i}", (i / CAFE_CHAIRS) * math.tau + 0.52)

    top = [M.tube(f"{tag}_top", (0.0, TABLE_TOP_Y - 0.06, 0.0), (0.0, TABLE_TOP_Y, 0.0),
                  TABLE_R, segments=16)]

    st = M.finish(steel, f"{tag}_st", bevel=0.006, smooth_angle=34.0)
    tp = M.finish(top, f"{tag}_tp", bevel=0.008, smooth_angle=34.0)
    M.paint(st, "steel_dark")
    M.paint(tp, "white_render")
    parts = [st, tp] + _parasol(tag, slot_a, slot_b)
    return S.join(parts, f"Minato_City_{tag}_LOD0")


def cafe_set_red():
    return _cafe_set("CafeSet", "awning_red", "awning_cream")


def cafe_set_cream():
    # SECOND CAFE VARIANT. The boards pair red umbrellas with cream ones, but on MinatoCoast's
    # bright white plaza tile under the midday ACES grade a cream-on-cream canopy clips to a
    # featureless white disc (proved on diag_minato_target_market_contact.png; a cream/sand
    # pairing was not enough either). Blue/white is the same Mediterranean market vocabulary,
    # matches the blue/white stall awnings and pop-up tents already in this family, and is the
    # one pairing proven to read at every distance in this scene. PROVISIONAL art call.
    return _cafe_set("CafeSetB", "awning_blue", "awning_cream")


# ============================================================ 3. pop-up market tent

def market_tent():
    h = TENT_HALF
    eave = [Vector((-h, TENT_EAVE_Y, -h)), Vector((h, TENT_EAVE_Y, -h)),
            Vector((h, TENT_EAVE_Y, h)), Vector((-h, TENT_EAVE_Y, h))]
    rg = TENT_RIDGE_HALF
    ridge = [Vector((-rg, TENT_APEX_Y, -rg)), Vector((rg, TENT_APEX_Y, -rg)),
             Vector((rg, TENT_APEX_Y, rg)), Vector((-rg, TENT_APEX_Y, rg))]

    blue, cream = [], []
    for f in range(4):
        c0, c1 = eave[f], eave[(f + 1) % 4]
        t0, t1 = ridge[f], ridge[(f + 1) % 4]

        def face(u, v, _c0=c0, _c1=c1, _t0=t0, _t1=t1):
            lo = _c0.lerp(_c1, u)
            hi = _t0.lerp(_t1, u)
            p = lo.lerp(hi, v)
            du = (_c1 - _c0).lerp((_t1 - _t0), v)
            dv = hi - lo
            n = du.cross(dv)
            if n.y < 0.0:
                n = -n
            return (p.x, p.y, p.z), n

        # Outward horizontal normal of this face, for the hanging valance.
        mid = (c0 + c1) * 0.5
        outn = Vector((mid.x, 0.0, mid.z)).normalized()

        def val(u, v, _c0=c0, _c1=c1, _o=outn):
            lo = _c0.lerp(_c1, u)
            p = lo + Vector((0.0, -TENT_VALANCE * v, 0.0)) + _o * (0.05 * v)
            return (p.x, p.y, p.z), _o

        a1, b1 = striped(f"Tent_f{f}", face, TENT_STRIPES, 3, 0.04)
        a2, b2 = striped(f"Tent_v{f}", val, TENT_STRIPES, 1, 0.04)
        blue += a1 + a2
        cream += b1 + b2

    steel = []
    for c in eave:
        steel.append(M.tube(f"Tent_leg{c.x:.0f}{c.z:.0f}", (c.x, 0.0, c.z),
                            (c.x, TENT_EAVE_Y, c.z), 0.042, segments=7))
        steel.append(M.tube(f"Tent_ft{c.x:.0f}{c.z:.0f}", (c.x, 0.0, c.z), (c.x, 0.05, c.z),
                            0.13, segments=8))
    for f in range(4):
        c0, c1 = eave[f], eave[(f + 1) % 4]
        steel.append(M.beam(f"Tent_eb{f}", c0, c1, 0.05, 0.05, up_hint=(0, 1, 0), uv_per_m=1.2))

    st = M.finish(steel, "Tent_st", bevel=0.005, smooth_angle=34.0)
    M.paint(st, "steel_dark")
    ca = _cloth(blue, "Tent_ca", "awning_blue")
    cb = _cloth(cream, "Tent_cb", "awning_cream")
    return S.join([st, ca, cb], "Minato_City_MarketTent_LOD0")


# ============================================================ 4. crate pile

def crate_pile():
    """Produce crates, sacks and a stacked pallet - the filler between stalls."""
    r = M.rng(12277)
    timber, goods_r, goods_g, goods_o = [], [], [], []
    stacks = ((-0.55, 0.0, 3), (0.34, 0.22, 2), (0.10, -0.62, 3), (0.86, -0.38, 1))
    for si, (cx, cz, n) in enumerate(stacks):
        for k in range(n):
            h = 0.32
            yaw = float(r.uniform(-0.18, 0.18))
            ax = cx - math.sin(yaw) * 0.30
            az = cz - math.cos(yaw) * 0.30
            bx = cx + math.sin(yaw) * 0.30
            bz = cz + math.cos(yaw) * 0.30
            timber.append(M.beam(f"cp{si}{k}", (ax, k * h + h * 0.5, az),
                                 (bx, k * h + h * 0.5, bz), 0.62, h, uv_per_m=1.6))
        # Open top crate: mound the produce.
        top = n * 0.32
        for j in range(8):
            bucket = (goods_r, goods_g, goods_o)[(si + j) % 3]
            bucket.append(blob(f"cg{si}{j}",
                               (cx + float(r.uniform(-0.24, 0.24)),
                                top + float(r.uniform(0.02, 0.12)),
                                cz + float(r.uniform(-0.24, 0.24))),
                               float(r.uniform(0.075, 0.12)), squash=(1.0, 0.9, 1.0)))

    t = M.finish(timber, "cp_t", bevel=0.010, smooth_angle=36.0)
    gr = M.finish(goods_r, "cp_gr", bevel=0.0, weld=0.0, smooth_angle=40.0)
    gg = M.finish(goods_g, "cp_gg", bevel=0.0, weld=0.0, smooth_angle=40.0)
    go = M.finish(goods_o, "cp_go", bevel=0.0, weld=0.0, smooth_angle=40.0)
    M.paint(t, "timber")
    M.paint(gr, "produce_red")
    M.paint(gg, "produce_green")
    M.paint(go, "produce_orange")
    return S.join([t, gr, gg, go], "Minato_City_CratePile_LOD0")


# ============================================================ export table

CATALOG = [
    (market_stall_blue, "Minato_City_MarketStall.fbx",  (0.50, 0.20)),
    (market_stall_red,  "Minato_City_MarketStallB.fbx", (0.50, 0.20)),
    (cafe_set_red,      "Minato_City_CafeSet.fbx",      (0.45, 0.18)),
    (cafe_set_cream,    "Minato_City_CafeSetB.fbx",     (0.45, 0.18)),
    (market_tent,       "Minato_City_MarketTent.fbx",   (0.45, 0.18)),
    (crate_pile,        "Minato_City_CratePile.fbx",    (0.40, 0.15)),
]


def main():
    total = 0
    for builder, filename, ratios in CATALOG:
        S.reset_scene()
        obj = builder()
        M.stat(obj, filename.replace(".fbx", ""))
        exports = [obj]
        base = obj.name.replace("_LOD0", "")
        for i, ratio in enumerate(ratios):
            exports.append(M.decimated_copy(obj, f"{base}_LOD{i + 1}", ratio))
        M.export_fbx(exports, filename)
        M.save_blend(filename.replace(".fbx", ".blend"))
        obj.data.calc_loop_triangles()
        total += len(obj.data.loop_triangles)
    print(f"[minato] market district complete: {len(CATALOG)} assets, {total:,} LOD0 tris")


if __name__ == "__main__":
    main()

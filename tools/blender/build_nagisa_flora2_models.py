"""
NAGISA BAY NB9 - tropical flora model kit (second pass), authored LOD0/1/2 per asset.

Run: blender.exe -b -P tools/blender/build_nagisa_flora2_models.py [-- Names...]

Uses the procedural NB9 textures from build_nagisa_flora2_textures.py (Assets/Environment/NagisaBay/
Textures/Flora2). Leaf cards: u across, v base -> tip. Foliage cards render on MapleRide/HDRP/Foliage
(two-sided cutout, height-weighted wind sway, so fronds sway and trunks stay planted); trunks and rock
on CelLit. Material slot names (registered in NagisaBayEnvironment.Flora.cs):
  foliage  NB9_Monstera NB9_Banana NB9_Fern NB9_Pandanus NB9_Frangipani NB9_DuneGrass NB9_GroundGrass
           NB9_GroundLeafy   (+ existing NB_PalmFrond NB_FanFrond NB_Hedge)
  solid    NB9_BananaStem NB9_RoyalTrunk NB9_CoralRock   (+ existing NB_PalmBark)

  Nagisa_NB9_JungleA/B/C   ~3 m jungle clumps (banana+fern+monstera / monstera+fern / pandanus+fern+monstera)
  Nagisa_NB9_RoyalPalm     13 m royal palm: grey ringed trunk, green crownshaft, 14 arching fronds
  Nagisa_NB9_CoconutLeanA/B  9-10 m coconut palms leaning 3-4 m out over the sand
  Nagisa_NB9_FanCluster    three fan palms of different heights
  Nagisa_NB9_Frangipani    3.6 m frangipani tree with flower rosettes
  Nagisa_NB9_DuneGrass     0.9 m beach grass fan
  Nagisa_NB9_GroundTuft    0.6 m lush grass tussock
  Nagisa_NB9_GroundLeafy   flat creeping ground cover patch (yellow + violet flowers)
  Nagisa_NB9_CoralOutcrop  2.2 m weathered coral-limestone outcrop
"""
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_nagisa_common as C                           # noqa: E402
from build_nagisa_common import Geo, add, mul, norm, sub  # noqa: E402
import build_nagisa_arch as A                           # noqa: E402

# palm helpers (trunk, frond, coconut_palm, fan_palm) without running that script's main()
_src = open(os.path.join(HERE, "build_nagisa_palms.py"), encoding="utf-8").read().split("\ndef main():")[0]
P = {"__file__": os.path.join(HERE, "build_nagisa_palms.py"), "__name__": "palm_kit"}
exec(compile(_src, "build_nagisa_palms.py", "exec"), P)
frond, coconut_palm, fan_palm = P["frond"], P["coconut_palm"], P["fan_palm"]

C.TILE.update({"NB9_BananaStem": (1.0, 1.0), "NB9_RoyalTrunk": (1.0, 1.0), "NB9_CoralRock": (1.0, 1.0)})


def cross_cards(g, mat, n, w, h, rng, lean=0.12, base=(0, 0, 0), up_uv=1.0):
    """n vertical cards fanned around the vertical axis, slightly leaning outwards at the top."""
    for k in range(n):
        a = k / n * math.pi + rng.uniform(-0.15, 0.15)
        dx, dz = math.cos(a), math.sin(a)
        lx, lz = rng.uniform(-lean, lean), rng.uniform(-lean, lean)
        b0 = add(base, (-dx * w / 2, 0, -dz * w / 2))
        b1 = add(base, (dx * w / 2, 0, dz * w / 2))
        t0 = add(b0, (lx, h, lz))
        t1 = add(b1, (lx, h, lz))
        g.quad(b0, b1, t1, t0, mat, uvs=[(0, 0), (1, 0), (1, up_uv), (0, up_uv)])


def leaf(g, mat, base, yaw, pitch, L, W, droop, segs):
    frond(g, base, yaw, pitch, L, W, segs, droop, mat=mat, fold=0.1)


def monstera(g, d, rng, at=(0, 0, 0), s=1.0):
    n = (7, 5, 3)[d]
    for k in range(n):
        y = k / n * math.tau + rng.uniform(-0.3, 0.3)
        leaf(g, "NB9_Monstera", add(at, (0, rng.uniform(0.3, 0.8) * s, 0)), y, rng.uniform(0.35, 0.95),
             rng.uniform(1.2, 1.6) * s, rng.uniform(1.1, 1.4) * s, rng.uniform(0.5, 1.0), (3, 2, 1)[d])


def fern(g, d, rng, at=(0, 0, 0), s=1.0):
    n = (14, 9, 5)[d]
    for k in range(n):
        y = k / n * math.tau + rng.uniform(-0.2, 0.2)
        leaf(g, "NB9_Fern", add(at, (0, 0.05 * s, 0)), y, rng.uniform(0.45, 1.2), rng.uniform(1.0, 1.4) * s,
             rng.uniform(0.35, 0.5) * s, rng.uniform(0.9, 1.4), (3, 2, 1)[d])


def banana(g, d, rng, at=(0, 0, 0), s=1.0, h=3.0):
    seg = (8, 6, 4)[d]
    g.cylinder(at, 0.17 * s, h * s, seg, "NB9_BananaStem", top=True, bottom=False, r_top=0.11 * s)
    top = add(at, (0, h * s, 0))
    n = (8, 6, 4)[d]
    for k in range(n):
        y = k / n * math.tau + rng.uniform(-0.25, 0.25)
        leaf(g, "NB9_Banana", add(top, (0, rng.uniform(-0.2, 0.1), 0)), y, rng.uniform(0.4, 1.15),
             rng.uniform(2.2, 2.9) * s, rng.uniform(0.75, 0.95) * s, rng.uniform(1.2, 1.9), (4, 3, 2)[d])


def pandanus(g, d, rng, at=(0, 0, 0), s=1.0):
    n = (13, 9, 5)[d]
    for k in range(n):
        y = k * 2.399 + rng.uniform(-0.2, 0.2)          # golden-angle spiral
        leaf(g, "NB9_Pandanus", add(at, (0, 0.9 * s, 0)), y, rng.uniform(0.5, 1.2), rng.uniform(1.8, 2.3) * s,
             0.3 * s, rng.uniform(0.9, 1.4), (3, 2, 1)[d])
    if d < 2:     # stilt-root cone
        g.cylinder(at, 0.22 * s, 1.0 * s, (8, 5, 4)[d], "NB_PalmBark", top=True, bottom=False, r_top=0.14 * s)


def jungle(d, seed, banana_n, monstera_n, fern_n, pand_n):
    g = Geo()
    rng = random.Random(seed)

    def ring(r0, r1):
        a = rng.uniform(0, math.tau)
        r = rng.uniform(r0, r1)
        return (r * math.cos(a), 0, r * math.sin(a))
    for _ in range(banana_n):
        banana(g, d, rng, ring(0.6, 1.8), rng.uniform(0.85, 1.15), rng.uniform(2.4, 3.4))
    for _ in range(pand_n):
        pandanus(g, d, rng, ring(0.8, 1.9), rng.uniform(0.9, 1.2))
    for _ in range(monstera_n):
        monstera(g, d, rng, ring(0.5, 1.9), rng.uniform(0.85, 1.2))
    for _ in range(fern_n):
        fern(g, d, rng, ring(0.2, 2.2), rng.uniform(0.8, 1.2))
    return g


def royal_palm(d):
    g = Geo()
    rng = random.Random(61)
    H = 12.4
    sides = (12, 8, 5)[d]
    rings = (12, 6, 3)[d]
    radius = lambda t: 0.30 - 0.12 * t + 0.07 * math.exp(-t * 9.0) + 0.045 * math.exp(-((t - 0.34) / 0.18) ** 2)  # base flare + belly
    vs = []
    for i in range(rings + 1):
        t = i / rings
        r = radius(t)
        vs.append([(r * math.cos(k / sides * math.tau), H * t, r * math.sin(k / sides * math.tau)) for k in range(sides + 1)])
    ln = 0.0
    for i in range(rings):
        seg = vs[i + 1][0][1] - vs[i][0][1]
        for k in range(sides):
            u0, u1 = k / sides, (k + 1) / sides
            g.quad(vs[i][k], vs[i + 1][k], vs[i + 1][k + 1], vs[i][k + 1], "NB9_RoyalTrunk",
                   uvs=[(u0, ln / 1.2), (u0, (ln + seg) / 1.2), (u1, (ln + seg) / 1.2), (u1, ln / 1.2)])
        ln += seg
    top = (0, H, 0)
    g.cylinder(top, 0.15, 1.15, (10, 7, 4)[d], "NB_Hedge", top=True, bottom=False, r_top=0.12)     # crownshaft
    crown = (0, H + 1.1, 0)
    n = (14, 10, 7)[d]
    for k in range(n):
        y = k / n * math.tau + rng.uniform(-0.12, 0.12)
        pitch = rng.uniform(0.2, 0.55) if k % 4 else rng.uniform(0.95, 1.25)
        frond(g, add(crown, (0, -0.1, 0)), y, pitch, rng.uniform(3.9, 4.8), rng.uniform(1.5, 1.9), (6, 4, 2)[d],
              rng.uniform(1.5, 2.0))
    return g


def fan_cluster(d):
    g = Geo()
    for off, yaw, sc in (((0, 0, 0), 0.0, 1.15), ((1.9, 0, 0.9), 2.1, 0.8), ((-1.3, 0, 1.6), 4.0, 0.62)):
        g.merge(fan_palm(d), off, yaw, sc)
    return g


def frangipani(d):
    g = Geo()
    rng = random.Random(77)
    # thick forking grey trunk (3 limbs)
    base = (0, 0, 0)
    g.cylinder(base, 0.2, 1.1, (8, 6, 4)[d], "NB_PalmBark", top=True, bottom=False, r_top=0.15)
    tips = []
    for k in range(3):
        a = k / 3 * math.tau + 0.4
        tip = (1.0 * math.cos(a), 3.0 + rng.uniform(-0.2, 0.3), 1.0 * math.sin(a))
        tips.append(tip)
        if d < 2:
            A.seg_box(g, (0, 1.05, 0), tip, 0.2, 0.2, "NB_PalmBark")
    for tip in tips:
        nr = (5, 3, 2)[d]
        for k in range(nr):
            y = k / nr * math.tau + rng.uniform(-0.3, 0.3)
            frond(g, add(tip, (0, 0.0, 0)), y, rng.uniform(0.05, 0.5), rng.uniform(1.0, 1.4), rng.uniform(0.9, 1.3),
                  2 if d < 2 else 1, rng.uniform(0.3, 0.7), mat="NB9_Frangipani", fold=0.05)
    return g


def dune_grass(d):
    g = Geo()
    cross_cards(g, "NB9_DuneGrass", (5, 3, 2)[d], 1.1, 0.95, random.Random(5), lean=0.18)
    return g


def ground_tuft(d):
    g = Geo()
    cross_cards(g, "NB9_GroundGrass", (4, 3, 2)[d], 0.95, 0.6, random.Random(9), lean=0.1)
    return g


def ground_leafy(d):
    g = Geo()
    rng = random.Random(15)
    S = 1.7
    # flat patch lying just above the ground (two offset quads so it does not read as one tile)
    for k, (ox, oz, rot, sc) in enumerate(((0.0, 0.0, 0.0, 1.0), (0.35, -0.2, 0.9, 0.7))[: (2, 2, 1)[d]]):
        c, s_ = math.cos(rot), math.sin(rot)
        pts = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
        q = [(ox + (px * c - pz * s_) * S * sc / 2, 0.05 + 0.02 * k, oz + (px * s_ + pz * c) * S * sc / 2) for px, pz in pts]
        g.quad(q[0], q[3], q[2], q[1], "NB9_GroundLeafy", uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
    if d < 2:
        cross_cards(g, "NB9_GroundLeafy", 2, 1.0, 0.35, rng, lean=0.05)
    return g


def coral_outcrop(d):
    g = Geo()
    rng = random.Random(23)
    seg = (9, 7, 5)[d]
    chunks = ((0, 0, 0, 0.95, 1.3), (0.8, 0.3, -0.2, 0.7, 0.9), (-0.7, -0.4, 0.3, 0.6, 0.75),
              (0.2, 0.6, 0.8, 0.5, 0.55), (-0.2, -0.9, -0.5, 0.45, 0.5))
    for ox, oz, _, r, h in chunks[: (5, 4, 3)[d]]:
        g.cylinder((ox, -0.15, oz), r, h + 0.15, seg, "NB9_CoralRock", top=True, bottom=False, r_top=r * rng.uniform(0.45, 0.7))
    return g


PROPS = {
    "Nagisa_NB9_JungleA": lambda d: jungle(d, 101, 2, 2, 3, 0),
    "Nagisa_NB9_JungleB": lambda d: jungle(d, 202, 0, 3, 4, 1),
    "Nagisa_NB9_JungleC": lambda d: jungle(d, 303, 1, 2, 3, 2),
    "Nagisa_NB9_RoyalPalm": royal_palm,
    "Nagisa_NB9_CoconutLeanA": lambda d: coconut_palm(d, 9.5, 3.4, 0.5, 14, 91, yaw=0.0),
    "Nagisa_NB9_CoconutLeanB": lambda d: coconut_palm(d, 8.0, 3.8, 0.3, 13, 97, yaw=0.15),
    "Nagisa_NB9_FanCluster": fan_cluster,
    "Nagisa_NB9_Frangipani": frangipani,
    "Nagisa_NB9_DuneGrass": dune_grass,
    "Nagisa_NB9_GroundTuft": ground_tuft,
    "Nagisa_NB9_GroundLeafy": ground_leafy,
    "Nagisa_NB9_CoralOutcrop": coral_outcrop,
}


def main():
    want = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(PROPS)
    for name in want:
        counts = C.build_lods(name, PROPS[name], name + ".glb")
        print("[nagisa-nb9] %-26s %s" % (name, " / ".join("{:,}".format(x) for x in counts)))


main()

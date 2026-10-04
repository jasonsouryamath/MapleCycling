"""
NAGISA BAY - climb / descent dressing kit (worker G, destination brief sections 9 + 11). Blender 4.5 -> GLB, LOD0..2.

  Nagisa_CL_BlackPine       9 m Japanese black pine: leaning twisted trunk, 5 cloud-pruned needle pads
  Nagisa_CL_BlackPineSmall  4.5 m shaped pine (niwaki) for resort gates and lookouts, 3 pads
  Nagisa_CL_Lantern         1.6 m stone toro lantern (base, post, fire box, roof) for resort gates / lookouts
  Nagisa_CL_Gatepost        2.4 m resort gate pier: stone plinth + timber post + lantern cap (pairs flank drives)

Material slots: NB_PalmBark (trunk, existing), NB_Stone (existing), NB_Teak (existing), NB_Lamp (existing),
NB_CL_Needle (new dark pine green, registered by NagisaBayEnvironment.ClimbDressing.cs).
Origin = base centre at ground. Run: blender -b -P tools/blender/build_nagisa_climb_kit.py     PROVISIONAL.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
import build_nagisa_common as C  # noqa: E402
from build_nagisa_common import Geo  # noqa: E402

C.TILE["NB_CL_Needle"] = (1.5, 1.5)
C.TILE["NB_PalmBark"] = (1.0, 1.4)


def pad(g, c, rx, rz, h, seg, mat="NB_CL_Needle"):
    """Cloud-pruned needle pad: a flattened dome (3 stacked rings) centred at c (x, y, z)."""
    rings = [(1.0, 0.0), (0.92, 0.45), (0.62, 0.85), (0.0, 1.0)]
    prev = None
    for k, (s, hh) in enumerate(rings):
        ring = []
        for i in range(seg):
            a = i / seg * math.tau
            jit = 1.0 + 0.10 * math.sin(a * 3 + c[0] * 1.7) + 0.06 * math.cos(a * 5 + c[2])
            ring.append((c[0] + math.cos(a) * rx * s * jit, c[1] + hh * h, c[2] + math.sin(a) * rz * s * jit))
        if prev is not None:
            for i in range(seg):
                j = (i + 1) % seg
                g.quad(prev[i], ring[i], ring[j], prev[j], mat, uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
        prev = ring
    # underside (flat disc facing down)
    base = []
    for i in range(seg):
        a = i / seg * math.tau
        jit = 1.0 + 0.10 * math.sin(a * 3 + c[0] * 1.7) + 0.06 * math.cos(a * 5 + c[2])
        base.append((c[0] + math.cos(a) * rx * jit, c[1], c[2] + math.sin(a) * rz * jit))
    g.face(list(base), mat, [(0.5 + 0.5 * math.cos(i / seg * math.tau), 0.5 + 0.5 * math.sin(i / seg * math.tau)) for i in range(seg)])


def twisted_trunk(g, height, lean, seg, r0, r1, rng, twist=0.35):
    """Series of tapered cylinders stepping up with a lateral lean + twist; returns branch anchor points."""
    pts = []
    x = z = 0.0
    n = 5
    for k in range(n):
        t = k / n
        h = height / n
        r = r0 + (r1 - r0) * t
        rt = r0 + (r1 - r0) * (k + 1) / n
        g.cylinder((x, t * height, z), r, h * 1.02, seg, "NB_PalmBark", top=False, r_top=rt, v_tile=1.4)
        pts.append((x, (k + 1) * h, z))
        x += lean * math.cos(twist * (k + 1)) * h * 0.55
        z += lean * math.sin(twist * (k + 1)) * h * 0.55
    return pts


def black_pine(detail, big=True):
    g = Geo()
    rng = random.Random(41 if big else 43)
    H = 9.0 if big else 4.5
    seg = (8, 6, 4)[detail]
    anchors = twisted_trunk(g, H * 0.82, 0.55 if big else 0.4, seg, 0.30 if big else 0.16, 0.12 if big else 0.07, rng)
    pads = [(0.46, 2.6, 1.0), (0.62, 3.1, 1.4), (0.78, 2.7, 0.9), (0.93, 2.0, 1.1), (1.08, 1.4, 0.8)] if big else \
           [(0.55, 1.5, 0.7), (0.80, 1.7, 0.9), (1.05, 1.0, 0.6)]
    for k, (fy, rr, hh) in enumerate(pads):
        ax = anchors[min(len(anchors) - 1, int(fy * len(anchors) * 0.9))]
        ang = k * 2.4 + 0.7
        ox, oz = math.cos(ang) * rr * 0.55, math.sin(ang) * rr * 0.55
        if detail < 2 and rr > 1.5:     # a limb to the pad
            g.cylinder((ax[0], ax[1] - 0.5, ax[2]), 0.09, 0.9, 4, "NB_PalmBark", top=False)
        pad(g, (ax[0] + ox, H * fy * 0.82 + 0.2, ax[2] + oz), rr, rr * 0.82, hh * (H / 9.0) * 1.2 + 0.4, (14, 10, 7)[detail])
    return g


def lantern(detail):
    g = Geo()
    g.box((0, 0.1, 0), (0.9, 0.2, 0.9), "NB_Stone", top=True)
    g.cylinder((0, 0.2, 0), 0.17, 0.75, (8, 6, 4)[detail], "NB_Stone", top=True)
    g.box((0, 1.05, 0), (0.75, 0.12, 0.75), "NB_Stone", top=True)
    g.box((0, 1.32, 0), (0.46, 0.42, 0.46), "NB_Lamp", top=True)
    for s in (-1, 1):
        g.box((s * 0.26, 1.32, 0), (0.07, 0.44, 0.52), "NB_Stone", top=True)
        g.box((0, 1.32, s * 0.26), (0.52, 0.44, 0.07), "NB_Stone", top=True)
    # hipped roof (pyramid)
    a, b = 0.62, 0.12
    for (p0, p1) in (((-a, 1.56, -a), (a, 1.56, -a)), ((a, 1.56, -a), (a, 1.56, a)), ((a, 1.56, a), (-a, 1.56, a)), ((-a, 1.56, a), (-a, 1.56, -a))):
        ra = (0.0, 1.78, 0.0)
        g.tri(p0, ra, p1, "NB_Stone")
        g.tri(p1, ra, p0, "NB_Stone")
    return g


def gatepost(detail):
    g = Geo()
    g.box((0, 0.35, 0), (0.9, 0.7, 0.9), "NB_Stone", top=True)
    g.box((0, 1.5, 0), (0.45, 1.6, 0.45), "NB_Teak", top=True)
    g.box((0, 2.35, 0), (0.7, 0.14, 0.7), "NB_Stone", top=True)
    g.box((0, 2.12, 0), (0.12, 0.2, 0.12), "NB_Lamp", top=True)
    return g


def main():
    C.build_lods("Nagisa_CL_BlackPine", lambda d: black_pine(d, True), "Nagisa_CL_BlackPine.glb")
    C.build_lods("Nagisa_CL_BlackPineSmall", lambda d: black_pine(d, False), "Nagisa_CL_BlackPineSmall.glb")
    C.build_lods("Nagisa_CL_Lantern", lantern, "Nagisa_CL_Lantern.glb")
    C.build_lods("Nagisa_CL_Gatepost", gatepost, "Nagisa_CL_Gatepost.glb")


main()

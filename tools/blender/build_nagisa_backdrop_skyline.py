"""
NAGISA BAY NB8A - distant resort-city skyline clusters so no sea horizon reads empty.

Run: blender.exe -b -P tools/blender/build_nagisa_backdrop_skyline.py [-- Name ...]

Seen only from kilometres away through HDRP fog, so even LOD0 uses the cheap facade detail (lit
NB_Interior window grid, bevelled crowns, slab edges). Each cluster sits on its own low headland
landmass (NB_Rock / NB_Groundcover) so it never floats on the sea plate. Front = local -z (toward
the island). Origin = sea level at cluster centre. All sizes PROVISIONAL.
"""
import os
import random
import sys

sys.path.append(os.path.dirname(__file__))
import build_nagisa_common as C          # noqa: E402
from build_nagisa_common import Geo      # noqa: E402
import build_nagisa_arch as A             # noqa: E402


def tower(g, cx, cz, hx, hz, floors, fh, detail, seed, wall, crown):
    """Cheap far-view tower: per face per floor one wall spandrel strip + N lit NB_Interior window
    panels (room-atlas cells, so windows read individually lit at dusk). N = 4/2/1 by LOD."""
    rng = random.Random(seed)
    poly = [(cx - hx, cz - hz), (cx + hx, cz - hz), (cx + hx, cz + hz), (cx - hx, cz + hz)]
    y0 = 3.0
    A.prism_poly(g, poly, -4.0, y0, "NB_Concrete", top=False)
    segs = (4, 2, 1)[detail]
    step = 1 if detail < 2 else 2
    ytop = y0 + floors * fh
    for a, b in A.poly_edges(poly):
        F = A.Frame(a, b)
        n = F.n
        for f in range(0, floors, step):
            ya = y0 + f * fh
            yb = min(ytop, ya + fh * step)
            yw = ya + fh * 0.3 * step
            A.qn(g, [F.P(0, ya), F.P(F.L, ya), F.P(F.L, yw), F.P(0, yw)], n, wall)
            for k in range(segs):
                s0, s1 = F.L * k / segs, F.L * (k + 1) / segs
                u0, v0, u1, v1 = A.interior_uv(rng.randrange(16))
                A.qn(g, [F.P(s0, yw), F.P(s1, yw), F.P(s1, yb), F.P(s0, yb)], n, "NB_Interior",
                     uvs=[(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        if detail < 2:      # corner piers
            A.qn(g, [F.P(-0.6, y0, 0.3), F.P(0.6, y0, 0.3), F.P(0.6, ytop, 0.3), F.P(-0.6, ytop, 0.3)], n,
                 "NB_Accent")
    A.prism_poly(g, poly, ytop, ytop + 1.2, wall, top=True, top_mat="NB_Concrete")
    if crown == 1:          # stepped crown
        A.bbox(g, (cx, ytop + 3.5, cz), (hx * 1.3, 4.6, hz * 1.3), wall, 0.2 if detail < 2 else 0.0, detail=detail)
        A.bbox(g, (cx, ytop + 7.3, cz), (hx * 0.6, 3.0, hz * 0.6), "NB_Accent", 0.2 if detail < 2 else 0.0,
               detail=detail)
    elif crown == 2:        # slim spire
        A.bbox(g, (cx, ytop + 2.7, cz), (hx * 1.2, 3.0, hz * 1.2), "NB_Concrete", 0.1 if detail < 2 else 0.0,
               detail=detail)
        A.cyl(g, (cx, ytop + 4.2, cz), 0.6, 18.0, 6, "NB_Metal", top=False, r_top=0.12)
    return ytop

def headland(g, hx, hz, h):
    """Low rounded headland mesh: 3 stacked chamfered stone/green slabs."""
    A.bbox(g, (0, -2.0, 0), (hx * 2 + 60, 4.0 + h, hz * 2 + 60), "NB_Stone", 3.0)
    A.bbox(g, (0, h * 0.5 + 0.3, 0), (hx * 2 + 30, 1.2, hz * 2 + 30), "NB_Groundcover", 1.0)
    A.bbox(g, (0, 1.2, 0), (hx * 2 + 4, 3.8, hz * 2 + 4), "NB_Concrete", 0.3)


def cluster(detail, seed, n, spread_x, spread_z, hmin, hmax):
    g = Geo()
    rng = random.Random(seed)
    headland(g, spread_x, spread_z, 3.0)
    walls = ["NB_Wall", "NB_Plaster", "NB_Concrete", "NB_Wall"]
    placed = []
    for i in range(n * 6):
        if len(placed) >= n:
            break
        cx = (rng.random() * 2 - 1) * spread_x
        cz = (rng.random() * 2 - 1) * spread_z
        hx = 9 + rng.random() * 8
        hz = 8 + rng.random() * 7
        if any(abs(cx - p[0]) < hx + p[2] + 6 and abs(cz - p[1]) < hz + p[3] + 6 for p in placed):
            continue
        placed.append((cx, cz, hx, hz))
        # taller toward the centre -> classic skyline silhouette
        centre = 1.0 - min(1.0, abs(cx) / spread_x)
        floors = int(hmin + (hmax - hmin) * (0.35 * rng.random() + 0.65 * centre * rng.random() ** 0.5))
        tower(g, cx, cz, hx, hz, max(floors, 6), 3.3, detail, seed + i, walls[i % len(walls)], rng.randrange(3))
    return g


BUILDERS = {
    "Nagisa_BD_SkylineA": lambda d: cluster(d, 1201, 26, 400.0, 120.0, 10, 44),
    "Nagisa_BD_SkylineB": lambda d: cluster(d, 1211, 15, 250.0, 90.0, 8, 30),
}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name in argv or list(BUILDERS):
        C.build_lods(name, BUILDERS[name], name + ".glb")


if __name__ == "__main__":
    main()

"""
Pure-python landform generator for the Shiosai hero backdrop (no bpy - importable and testable
anywhere). tools/blender/build_shiosai_backdrop_hero.py turns these grids into GLBs.

Every landform is a heightfield on a regular grid in UNITY metres, origin at the base centre:
  x = across (left/right as seen from the road), y = up, z = depth.
  Local -Z is the FRONT: the face that looks at the road / sea (cliffs live there).

Returns (verts, faces, uvs, rock_mask):
  verts     [(x, y, z)] Unity metres, row-major (iz * (nx + 1) + ix)
  faces     [(a, b, c, d)] quads, wound counter-clockwise seen from above (+Y)
  uvs       per-LOOP uv list (4 per face), world-metre tiling / 8 m
  rock_mask per-face bool: True -> rock face material, False -> grass/forest cap
"""
import math

UV_TILE_M = 8.0


# --------------------------------------------------------------------------- noise

def _hash(ix, iz, seed):
    h = (ix * 374761393 + iz * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFF) / float(0xFFFFFF)


def _smooth(t):
    return t * t * t * (t * (t * 6 - 15) + 10)


def value_noise(x, z, seed):
    ix, iz = math.floor(x), math.floor(z)
    fx, fz = x - ix, z - iz
    sx, sz = _smooth(fx), _smooth(fz)
    a = _hash(ix, iz, seed)
    b = _hash(ix + 1, iz, seed)
    c = _hash(ix, iz + 1, seed)
    d = _hash(ix + 1, iz + 1, seed)
    return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sz   # 0..1


def fbm(x, z, seed, octaves=5, lac=2.03, gain=0.5):
    amp, freq, tot, norm = 1.0, 1.0, 0.0, 0.0
    for o in range(octaves):
        tot += amp * value_noise(x * freq, z * freq, seed + o * 101)
        norm += amp
        amp *= gain
        freq *= lac
    return tot / norm                                                            # 0..1


def ridged(x, z, seed, octaves=5, lac=2.1, gain=0.52):
    """Ridged multifractal: sharp crests, the signature of real mountain ridgelines."""
    amp, freq, tot, norm, weight = 1.0, 1.0, 0.0, 0.0, 1.0
    for o in range(octaves):
        n = 1.0 - abs(value_noise(x * freq, z * freq, seed + o * 57) * 2.0 - 1.0)
        n *= n
        n *= weight
        weight = min(1.0, max(0.0, n * 2.0))
        tot += amp * n
        norm += amp
        amp *= gain
        freq *= lac
    return tot / norm


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# --------------------------------------------------------------------------- grid -> mesh

def _grid(nx, nz, W, D, height_fn, rock_fn):
    verts, faces, uvs, rock = [], [], [], []
    for iz in range(nz + 1):
        z = -D / 2 + D * iz / nz
        for ix in range(nx + 1):
            x = -W / 2 + W * ix / nx
            verts.append((x, height_fn(x, z), z))
    row = nx + 1
    for iz in range(nz):
        for ix in range(nx):
            a = iz * row + ix
            b, c, d = a + 1, a + row + 1, a + row
            # CCW seen from +Y in a right-handed (x, z) grid laid with z increasing "down" the
            # rows: a(x0,z0) -> d(x0,z1) -> c(x1,z1) -> b(x1,z0). recalc_normals() fixes any
            # residual winding in Blender anyway.
            faces.append((a, d, c, b))
            for v in (a, d, c, b):
                uvs.append((verts[v][0] / UV_TILE_M, verts[v][2] / UV_TILE_M))
            pa, pb, pd = verts[a], verts[b], verts[d]
            # face normal y component from the two edge vectors
            e1 = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2])
            e2 = (pd[0] - pa[0], pd[1] - pa[1], pd[2] - pa[2])
            nxv = e1[1] * e2[2] - e1[2] * e2[1]
            nyv = e1[2] * e2[0] - e1[0] * e2[2]
            nzv = e1[0] * e2[1] - e1[1] * e2[0]
            ln = math.sqrt(nxv * nxv + nyv * nyv + nzv * nzv) or 1.0
            slope_up = abs(nyv) / ln
            ymid = (pa[1] + verts[c][1]) * 0.5
            rock.append(rock_fn(slope_up, ymid))
    return verts, faces, uvs, rock


# --------------------------------------------------------------------------- landforms

def headland(seed, W=700.0, D=420.0, H=150.0, nx=180, nz=110):
    """
    A coastal headland massif: a sheer, strata-banded andesite cliff on the FRONT (-Z) face,
    a grass-capped crown with gullies running down the back, and flanks that sink below sea
    level so it seats on the water without a visible base.
    """
    s = seed

    def h(x, z):
        u = x / (W / 2)                       # -1..1 across
        v = (z + D / 2) / D                   # 0 at the front edge, 1 at the back
        across = max(0.0, 1.0 - abs(u) ** 2.2) ** 0.85
        # front: cliff rises from the sea within the first ~14 % of depth; crest ~30 %;
        # back: long grassy fall to the rear edge
        crest = 0.30 + 0.06 * (fbm(x * 0.004, 3.1, s) - 0.5)
        if v < crest:
            prof = smoothstep(0.02, 0.16, v) * 0.82 + 0.18 * (v / crest)
        else:
            prof = 1.0 - 0.75 * smoothstep(crest, 1.0, v)
        base = across * prof
        # gullies: ridged noise stretched along the fall line (z)
        g = ridged(x * 0.012, z * 0.004, s + 7)
        rough = fbm(x * 0.02, z * 0.02, s + 3)
        y = H * base * (0.78 + 0.34 * g) + 10.0 * (rough - 0.5) * base
        # strata terraces on the steep front band only
        if v < 0.2 and y > 8.0:
            step = 7.5 + 2.5 * fbm(x * 0.01, 0.0, s + 11)
            q = math.floor(y / step) * step
            y = q + (y - q) * 0.35 + 0.65 * step * smoothstep(0.0, 1.0, (y - q) / step) * 0.4
        # sink the skirt below sea level so the waterline is a coastline, not a slab edge
        edge = min(1.0, (1 - abs(u)) * 6.0, v * 22.0, (1 - v) * 10.0)
        return y * min(1.0, edge + 0.0) - 18.0 * (1.0 - min(1.0, edge))

    def is_rock(slope_up, y):
        return slope_up < 0.74 or y < 4.0

    return _grid(nx, nz, W, D, h, is_rock)


def islet(seed, W=260.0, D=200.0, H=58.0, nx=96, nz=74):
    """An offshore islet: an irregular cliff-ringed outline, a tilted, domed grassy top,
    wave-cut notches, and rocky skerries at the foot."""
    s = seed
    harm = [(k, 0.5 + 0.5 * _hash(k, 1, s), _hash(k, 2, s) * math.tau) for k in (2, 3, 5, 7, 11)]
    hsum = sum(a for _, a, _ in harm)

    def outline(a):
        return 1.0 + 0.22 * sum(amp * math.sin(k * a + ph) for k, amp, ph in harm) / hsum

    def h(x, z):
        r = math.sqrt((x / (W / 2)) ** 2 + (z / (D / 2)) ** 2)
        a = math.atan2(z / D, x / W)
        rr = r / (0.72 * outline(a))
        cliff = 1.0 - smoothstep(0.86, 1.0, rr)
        dome = 1.0 - 0.35 * rr * rr
        top = dome * (0.80 + 0.22 * (x / W) + 0.16 * (fbm(x * 0.025, z * 0.025, s) - 0.5))
        y = H * cliff * top
        # vertical fluting on the cliff band + strata
        y += H * 0.08 * (ridged(a * 3.0, rr * 2.0, s + 9) - 0.4) * cliff * smoothstep(0.7, 0.95, rr)
        # skerries: a few rocks poking out of the shallows just off the cliff foot
        sk = fbm(x * 0.05, z * 0.05, s + 17)
        if 1.0 < rr < 1.35 and sk > 0.66:
            y = max(y, (sk - 0.66) * 60.0 - 2.0)
        return y - 14.0 * smoothstep(1.0, 1.4, rr)

    def is_rock(slope_up, y):
        return slope_up < 0.80 or y < 6.0

    return _grid(nx, nz, W, D, h, is_rock)


def massif(seed, W=3200.0, D=1600.0, H=950.0, nx=200, nz=100):
    """
    A distant mountain massif: a ridged-multifractal main crest running across, spurs dropping
    toward the viewer, forested lower slopes and bare rock above the tree line.
    """
    s = seed

    def h(x, z):
        u = x / (W / 2)
        v = (z + D / 2) / D
        across = max(0.0, 1.0 - abs(u) ** 2.0) ** 0.7
        ridge_z = 0.55 + 0.10 * (fbm(x * 0.0008, 1.7, s) - 0.5)
        prof = math.exp(-((v - ridge_z) / 0.26) ** 2)
        # Broad crest first (low-frequency ridged noise, gentle weight), fine ridges on top at a
        # small amplitude - a full-strength high-frequency ridge made a hedgehog of spikes.
        r = ridged(x * 0.0011, z * 0.0011, s + 13, octaves=6)
        fine = ridged(x * 0.004, z * 0.004, s + 19, octaves=4)
        spurs = ridged(x * 0.0026, z * 0.0009, s + 29)
        y = (H * across * prof * (0.62 + 0.42 * r + 0.08 * fine)
             + 0.18 * H * across * spurs * smoothstep(0.0, 0.6, v) * (1 - prof * 0.5))
        return y - 60.0 * (1.0 - min(1.0, across * 4.0, v * 12.0, (1 - v) * 8.0))

    def is_rock(slope_up, y):
        return y > 0.62 * H or slope_up < 0.62

    return _grid(nx, nz, W, D, h, is_rock)


# Asset table: name -> (builder, kwargs, lod kwargs). LOD1 = ~25 % of the tris.
ASSETS = {
    "Shiosai_Hero_Headland_A": (headland, dict(seed=11), dict(seed=11, nx=90, nz=55)),
    "Shiosai_Hero_Headland_B": (headland, dict(seed=23, W=620.0, H=175.0), dict(seed=23, W=620.0, H=175.0, nx=90, nz=55)),
    "Shiosai_Hero_Headland_C": (headland, dict(seed=37, W=820.0, D=460.0, H=130.0), dict(seed=37, W=820.0, D=460.0, H=130.0, nx=90, nz=55)),
    "Shiosai_Hero_Islet_A": (islet, dict(seed=41), dict(seed=41, nx=48, nz=37)),
    "Shiosai_Hero_Islet_B": (islet, dict(seed=53, W=200.0, D=170.0, H=44.0), dict(seed=53, W=200.0, D=170.0, H=44.0, nx=48, nz=37)),
    "Shiosai_Hero_Islet_C": (islet, dict(seed=67, W=320.0, D=220.0, H=70.0), dict(seed=67, W=320.0, D=220.0, H=70.0, nx=48, nz=37)),
    "Shiosai_Hero_Massif_A": (massif, dict(seed=71), dict(seed=71, nx=100, nz=50)),
    "Shiosai_Hero_Massif_B": (massif, dict(seed=83, W=2800.0, H=1100.0), dict(seed=83, W=2800.0, H=1100.0, nx=100, nz=50)),
}

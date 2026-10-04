"""
M1/M2 - MapleRide world terrain generator (system Python: numpy + scipy + Pillow).

    python tools/blender/worldmap/world_terrain.py [--res 0.2]

Reads   Assets/Resources/World/world_layout.json   (canonical world, see author_world_layout.py)
Writes  tools/blender/worldmap/out/
          surf_m.npy     render surface height (m): terrain on land, water level on water
          land_m.npy     terrain height (m) incl. lake/river/sea beds
          water.npy      water coverage 0..1
          attr_*.npy     per-vertex densities for the Blender instancer (trees, buildings)
          albedo.png     ground + water colour at 2x the mesh resolution (north at the top)
          rough.png      roughness (water glossy, land matte)
          roads.json     A*-routed road network, region routes, bridges, ferries (km)
          meta.json      grid facts
          validation.txt geography checks (rivers downhill, snowline, summits, links)

The heightfield is built from the layout's features: fractal coast (signed distance), base
inland rise, rolling-hill zones, ridged mountain ranges, volcano cones, caldera, plateaus with
canyons, per-region summit corrections, rivers carved into monotone-downhill valleys, lakes and
a continental shelf.
"""
import argparse
import heapq
import json
import math
import os
import sys

import numpy as np
from matplotlib.path import Path as MplPath
from PIL import Image
from scipy import ndimage
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import dijkstra

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
LAYOUT = os.path.join(ROOT, "Assets", "Resources", "World", "world_layout.json")
OUT = os.path.join(HERE, "out")
PAD = 24.0          # km of open sea generated around the layout so no edge shows in the frame
X0 = Y0 = 0.0       # grid origin (km), set by build()


# ============================================================================ noise
class Noise:
    def __init__(self, seed):
        rng = np.random.RandomState(seed)
        p = rng.permutation(256)
        self.perm = np.concatenate([p, p]).astype(np.int32)
        ang = rng.uniform(0, 2 * np.pi, 256)
        self.gx = np.cos(ang).astype(np.float32)
        self.gy = np.sin(ang).astype(np.float32)

    def perlin(self, x, y):
        xi = np.floor(x)
        yi = np.floor(y)
        xf = (x - xi).astype(np.float32)
        yf = (y - yi).astype(np.float32)
        xi = xi.astype(np.int64) & 255
        yi = yi.astype(np.int64) & 255
        P = self.perm
        h00 = P[P[xi] + yi]
        h10 = P[P[xi + 1] + yi]
        h01 = P[P[xi] + yi + 1]
        h11 = P[P[xi + 1] + yi + 1]

        def g(h, dx, dy):
            return self.gx[h] * dx + self.gy[h] * dy

        n00 = g(h00, xf, yf)
        n10 = g(h10, xf - 1, yf)
        n01 = g(h01, xf, yf - 1)
        n11 = g(h11, xf - 1, yf - 1)
        u = xf * xf * xf * (xf * (xf * 6 - 15) + 10)
        v = yf * yf * yf * (yf * (yf * 6 - 15) + 10)
        a = n00 + u * (n10 - n00)
        b = n01 + u * (n11 - n01)
        return (a + v * (b - a)) * 1.414

    def fbm(self, x, y, octaves=5, lac=2.0, gain=0.5):
        s = np.zeros_like(x, dtype=np.float32)
        amp, f, norm = 1.0, 1.0, 0.0
        for o in range(octaves):
            s += amp * self.perlin(x * f + o * 17.31, y * f - o * 9.77)
            norm += amp
            amp *= gain
            f *= lac
        return s / norm

    def ridged(self, x, y, octaves=6, lac=2.05, gain=0.5):
        """Ridged multifractal in ~[0,1]: sharp crests, each octave weighted by the last."""
        s = np.zeros_like(x, dtype=np.float32)
        w = np.ones_like(x, dtype=np.float32)
        amp, f, norm = 1.0, 1.0, 0.0
        for o in range(octaves):
            n = 1.0 - np.abs(self.perlin(x * f + o * 5.13, y * f + o * 3.71))
            n = n * n
            n *= w
            w = np.clip(n * 1.6, 0, 1)
            s += n * amp
            norm += amp
            amp *= gain
            f *= lac
        return s / norm


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def smax(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0, 1)
    return a * (1 - h) + b * h + k * h * (1 - h)


def pts2(flat_list):
    a = np.asarray(flat_list, dtype=np.float64)
    return a.reshape(-1, 2)


def ellipse_r(X, Y, c, r, rot=0.0):
    """Normalised elliptic radius (1 on the boundary)."""
    ca, sa = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    dx, dy = X - c[0], Y - c[1]
    u = dx * ca + dy * sa
    v = -dx * sa + dy * ca
    return np.sqrt((u / r[0]) ** 2 + (v / r[1]) ** 2)


def polyline_field(X, Y, P, values=None, pad=None):
    """Distance (km) to a polyline, plus the along-line parameter (0..1 by length) and an
    interpolated per-vertex value at the nearest point. With `pad` (km) only the polyline's
    bounding box grown by pad is evaluated (the rest is inf / 0): X, Y must then be the regular
    meshgrid."""
    if pad is not None:
        xs, ys = X[0, :], Y[:, 0]
        i0 = int(np.searchsorted(xs, P[:, 0].min() - pad))
        i1 = int(np.searchsorted(xs, P[:, 0].max() + pad))
        j0 = int(np.searchsorted(ys, P[:, 1].min() - pad))
        j1 = int(np.searchsorted(ys, P[:, 1].max() + pad))
        full_d = np.full(X.shape, np.inf, np.float32)
        full_s = np.zeros(X.shape, np.float32)
        full_v = np.zeros(X.shape, np.float32)
        if i1 > i0 and j1 > j0:
            d, s, v = polyline_field(X[j0:j1, i0:i1], Y[j0:j1, i0:i1], P, values)
            full_d[j0:j1, i0:i1] = d
            full_s[j0:j1, i0:i1] = s
            full_v[j0:j1, i0:i1] = v
        return full_d, full_s, full_v
    best = np.full(X.shape, np.inf, dtype=np.float32)
    s_at = np.zeros(X.shape, dtype=np.float32)
    v_at = np.zeros(X.shape, dtype=np.float32)
    seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
    cum = np.concatenate([[0], np.cumsum(seg)])
    total = max(cum[-1], 1e-6)
    for i in range(len(P) - 1):
        a, b = P[i], P[i + 1]
        ab = b - a
        L2 = max(ab @ ab, 1e-9)
        t = np.clip(((X - a[0]) * ab[0] + (Y - a[1]) * ab[1]) / L2, 0, 1)
        qx = a[0] + t * ab[0]
        qy = a[1] + t * ab[1]
        d = np.hypot(X - qx, Y - qy).astype(np.float32)
        m = d < best
        best = np.where(m, d, best)
        s_at = np.where(m, (cum[i] + t * seg[i]) / total, s_at)
        if values is not None:
            v_at = np.where(m, values[i] + t * (values[i + 1] - values[i]), v_at)
    return best, s_at, v_at


def bilinear(A, x, y, res):
    fx, fy = (x - X0) / res, (y - Y0) / res
    i0 = int(np.clip(math.floor(fx), 0, A.shape[1] - 2))
    j0 = int(np.clip(math.floor(fy), 0, A.shape[0] - 2))
    tx, ty = fx - i0, fy - j0
    return float((A[j0, i0] * (1 - tx) + A[j0, i0 + 1] * tx) * (1 - ty) +
                 (A[j0 + 1, i0] * (1 - tx) + A[j0 + 1, i0 + 1] * tx) * ty)


# ============================================================================ build
def build(res):
    L = json.load(open(LAYOUT, encoding="utf-8"))
    global X0, Y0
    W, H = L["size_km"]
    X0 = Y0 = -PAD
    nx, ny = int(round((W + 2 * PAD) / res)) + 1, int(round((H + 2 * PAD) / res)) + 1
    xs = (X0 + np.arange(nx) * res).astype(np.float32)
    ys = (Y0 + np.arange(ny) * res).astype(np.float32)
    X, Y = np.meshgrid(xs, ys)          # row j = y (south -> north), col i = x
    N = Noise(1337)
    log = []
    print(f"grid {nx} x {ny} @ {res} km")

    # ---------------------------------------------------------------- coast
    wx = X + 4.2 * N.fbm(X / 26 + 3.1, Y / 26, 4) + 1.6 * N.fbm(X / 8, Y / 8 + 7, 3) \
        + 0.6 * N.fbm(X / 2.6, Y / 2.6 + 2, 3) + 0.2 * N.fbm(X / 0.9, Y / 0.9 - 4, 2)
    wy = Y + 4.2 * N.fbm(X / 26 - 5.3, Y / 26 + 1.7, 4) + 1.6 * N.fbm(X / 8 + 3, Y / 8, 3) \
        + 0.6 * N.fbm(X / 2.6 + 5, Y / 2.6, 3) + 0.2 * N.fbm(X / 0.9 + 8, Y / 0.9, 2)
    pts = np.column_stack([wx.ravel(), wy.ravel()])
    land = MplPath(pts2(L["coast"])).contains_points(pts).reshape(X.shape)
    for cut in L["sea_cuts"]:
        land &= ~MplPath(pts2(cut["poly"])).contains_points(pts).reshape(X.shape)
    for isl in L["islands"]:
        land |= ellipse_r(wx, wy, isl["c"], isl["r"], isl.get("rot", 0)) < 1.0
    del pts
    d_in = ndimage.distance_transform_edt(land) * res
    d_out = ndimage.distance_transform_edt(~land) * res
    dist = (d_in - d_out).astype(np.float32)        # km, + inland
    print("coast done")

    # ---------------------------------------------------------------- base + hills
    inland = smoothstep(0, 30, dist)
    base = 6 + 200 * inland ** 1.3
    amp = 240 * smoothstep(0, 10, dist)
    for z in L["hill_zones"]:
        e = ellipse_r(X, Y, z["c"], z["r"])
        w = smoothstep(1.0, 0.55, e)
        amp = amp * (1 - w) + z["amp_m"] * w
    hills = 0.5 + 0.5 * N.fbm(X / 14, Y / 14, 6)
    hills = hills ** 1.4
    h = base + amp * hills * 1.6
    # broad uplands: some of the interior is hill country, some stays open plain
    upl = smoothstep(-0.05, 0.45, N.fbm(X / 55 + 9, Y / 55 - 4, 3))
    h = h + 460 * upl * smoothstep(3, 22, dist) * N.ridged(X / 9 + 2, Y / 9 + 6, 5)
    # flat zones (deltas, plains, city basins) also flatten the base rise
    for z in L["hill_zones"]:
        if z["amp_m"] < 60:
            e = ellipse_r(X, Y, z["c"], z["r"])
            w = smoothstep(1.0, 0.5, e)
            h = h * (1 - w) + (4 + z["amp_m"] * hills) * w

    # ---------------------------------------------------------------- ranges
    rwx = X + 4.0 * N.fbm(X / 22 + 11, Y / 22, 3)
    rwy = Y + 4.0 * N.fbm(X / 22, Y / 22 + 11, 3)
    for rg in L["ranges"]:
        P = pts2(rg["pts"])
        dd, _, crest = polyline_field(rwx, rwy, P, np.asarray(rg["crest_m"], np.float32))
        q = dd / rg["width"]
        env = np.exp(-(q ** 2) * 2.0)
        sc = rg["width"] * 0.55
        R = N.ridged(X / sc + 40, Y / sc - 13, 6, gain=0.42)
        hr = crest * env ** 0.85 * (0.30 + 0.95 * R)
        h = smax(h, hr, 120)
        # foothills: a wider, lower apron of spurs either side of the main crest
        env2 = np.exp(-((dd / (rg["width"] * 2.3)) ** 2) * 2.0)
        R2 = N.ridged(X / (sc * 0.7) - 17, Y / (sc * 0.7) + 29, 6)
        h = smax(h, crest * 0.34 * env2 * (0.15 + 1.0 * R2), 90)
    print("ranges done")

    # ---------------------------------------------------------------- volcanoes / calderas
    for v in L["volcanoes"]:
        r = np.hypot(X - v["c"][0], Y - v["c"][1])
        ang = np.arctan2(Y - v["c"][1], X - v["c"][0])
        gully = N.ridged(ang * 9.0 + 3, r / 2.0, 4)
        prof = np.clip(1 - r / v["r"], 0, 1) ** 1.5
        hv = v["peak_m"] * prof * (0.93 + 0.10 * gully)
        cr = v["crater_r"]
        hv = hv - v["crater_m"] * np.clip(1 - (r / cr) ** 2, 0, 1)
        h = smax(h, hv, 80)
    for c in L["calderas"]:
        r = np.hypot(X - c["c"][0], Y - c["c"][1])
        rim, outer = c["rim_r"], c["outer_r"]
        out = c["rim_m"] * np.clip(1 - (r - rim) / (outer - rim), 0, 1) ** 1.4
        inner = c["floor_m"] + (c["rim_m"] - c["floor_m"]) * smoothstep(rim * 0.62, rim, r) ** 2
        hc = np.where(r > rim, out, inner) * (0.94 + 0.12 * N.fbm(X / 2, Y / 2, 3))
        inside = r < rim * 1.05
        h = np.where(inside, hc, smax(h, hc, 60))

    # ---------------------------------------------------------------- plateaus (+ canyons)
    for p in L["plateaus"]:
        e = ellipse_r(X, Y, p["c"], p["r"], p.get("rot", 0))
        rmean = 0.5 * (p["r"][0] + p["r"][1])
        ekm = (e - 1.0) * rmean + 6.0 * N.fbm(X / 10, Y / 10 + 21, 4) + 2.0 * N.fbm(X / 3, Y / 3 - 9, 3)
        m = smoothstep(p["edge"], -p["edge"] * 0.4, ekm)
        lv = p["level_m"] + 140 * N.fbm(X / 9 + 5, Y / 9, 5) \
            + 420 * (N.ridged(X / 6 + 13, Y / 6 - 8, 5) - 0.3)
        h = h * (1 - m) + np.maximum(h, lv) * m
        if p.get("canyons"):
            n = np.abs(N.fbm(X / 11 + 70, Y / 11 - 30, 4))
            cut = np.clip(1 - n / 0.07, 0, 1) ** 1.3
            terr = np.floor(cut * 4) / 4 * 0.6 + cut * 0.4     # stepped canyon walls
            h = h - 260 * terr * m
    print("relief done")

    # ---------------------------------------------------------------- land/sea height
    sea_floor = -(12 + 160 * smoothstep(0, 22, -dist) + 25 * N.fbm(X / 6, Y / 6, 3))
    coast_fac = smoothstep(0.0, 1.8, dist) ** 0.55
    h = np.where(land, 1.5 + (h - 1.5) * coast_fac, sea_floor).astype(np.float32)

    # ---------------------------------------------------------------- summit corrections
    for r in L["regions"]:
        sx, sy, target = r["summit"]
        cur = bilinear(h, sx, sy, res)
        delta = target - cur
        if abs(delta) > 25:
            sig = 3.2 if target > 400 else 5.5
            g = np.exp(-(((X - sx) ** 2 + (Y - sy) ** 2) / (sig * sig)))
            h = h + delta * g * land
        log.append(f"summit {r['id']:16s} target {target:5.0f} m  was {cur:7.1f}  "
                   f"now {bilinear(h, sx, sy, res):7.1f}")

    # ---------------------------------------------------------------- lakes
    water = (~land).astype(np.float32)
    level = np.zeros_like(h)
    for lk in L["lakes"]:
        e = ellipse_r(X + 0.6 * N.fbm(X / 2, Y / 2, 3), Y + 0.6 * N.fbm(X / 2 + 9, Y / 2, 3),
                      lk["c"], lk["r"], lk.get("rot", 0))
        inside = e < 1.0
        lvl = lk["level_m"]
        rim = (e >= 1.0) & (e < 1.9)
        h = np.where(rim, np.maximum(h, lvl + 6 * smoothstep(1.0, 1.9, e) + 1.5), h)
        h = np.where(inside, np.minimum(h, lvl - 4 - 30 * (1 - e * e)), h)
        cov = np.clip((1.0 - e) * 12, 0, 1)
        water = np.maximum(water, cov)
        level = np.where(cov > 0, lvl, level)

    # ---------------------------------------------------------------- rivers
    river_cov = np.zeros_like(h)
    G_rivers = []
    for ri, rv in enumerate(L["rivers"]):
        P = meander(pts2(rv["pts"]), N, 1.5 if len(rv["pts"]) > 6 else 0.6, 7.0, ri)
        G_rivers.append({"name": rv["name"], "width": rv["width"],
                         "pts": [round(float(v), 3) for v in P.ravel()]})
        # monotone bed profile sampled from the current terrain, source -> mouth
        seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
        cum = np.concatenate([[0], np.cumsum(seg)])
        ss = np.linspace(0, cum[-1], 200)
        bx = np.interp(ss, cum, P[:, 0])
        by = np.interp(ss, cum, P[:, 1])
        bed = np.array([bilinear(h, x, y, res) for x, y in zip(bx, by)])
        bed = np.minimum.accumulate(bed)
        ej, ei = int(round((by[-1] - Y0) / res)), int(round((bx[-1] - X0) / res))
        # ends in the sea / a lake -> that water level; ends on another river (a tributary or
        # a delta distributary) -> the already-carved bed there
        mouth_lvl = float(level[ej, ei]) if water[ej, ei] > 0.5 else bilinear(h, bx[-1], by[-1], res)
        mouth_lvl = max(mouth_lvl, 0.0)
        bed = np.maximum(bed - 6, mouth_lvl - 3)
        bed[-1] = min(bed[-1], mouth_lvl)
        bed = np.minimum.accumulate(bed)
        dd, s, _ = polyline_field(X, Y, P, pad=14)
        bed_at = np.interp(s * cum[-1], ss, bed).astype(np.float32)
        vw = (1.2 + 4.0 * s) * (1.0 + 0.5 * smoothstep(300, 1500, bed_at))
        f = smoothstep(0.0, 1.0, dd / vw) ** 0.75
        carve = bed_at + (h - bed_at) * f
        h = np.where(h > bed_at, carve, h)
        cw = (0.22 + (rv["width"] - 0.22) * s ** 1.5) * 0.5
        cov = np.clip((cw - dd) / res + 0.5, 0, 1) * (bed_at > -1)
        cov = cov * (land | (water > 0))
        h = np.where(cov > 0, np.minimum(h, bed_at - 2), h)
        river_cov = np.maximum(river_cov, cov)
        level = np.where(cov > 0.01, np.maximum(bed_at, 0.0), level)
        log.append(f"river {rv['name']:18s} source {bed[0]:7.1f} m -> mouth {bed[-1]:6.1f} m  "
                   f"monotone={bool(np.all(np.diff(bed) <= 1e-6))}")
    water = np.maximum(water, river_cov)
    print("water done")

    surf = np.where(water > 0.5, np.maximum(level, np.where(land, level, 0.0)), h)
    surf = np.where(~land & (river_cov < 0.5), 0.0, surf).astype(np.float32)
    return dict(L=L, W=W, H=H, res=res, X=X, Y=Y, N=N, land=land, dist=dist, h=h, surf=surf,
                water=water.astype(np.float32), level=level, river=river_cov, log=log,
                rivers=G_rivers)


def meander(P, N, amp, wavelength, seed):
    """Resamples a control polyline every 0.4 km and swings it sideways with 1D noise, tapered to
    zero at both ends so sources and mouths stay where the layout put them."""
    seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
    cum = np.concatenate([[0], np.cumsum(seg)])
    n = max(4, int(cum[-1] / 0.4))
    s = np.linspace(0, cum[-1], n)
    x = np.interp(s, cum, P[:, 0])
    y = np.interp(s, cum, P[:, 1])
    tx, ty = np.gradient(x), np.gradient(y)
    tl = np.hypot(tx, ty) + 1e-9
    nxv, nyv = -ty / tl, tx / tl
    off = N.fbm(s / wavelength + seed * 13.7, np.full_like(s, seed * 3.3), 4) * 2.0
    off += 0.35 * np.sin(s / (wavelength * 0.22) + seed)          # tight lowland loops
    taper = smoothstep(0, 3, s) * smoothstep(cum[-1], cum[-1] - 3, s)
    off = off * amp * taper
    return np.column_stack([x + nxv * off, y + nyv * off])


# ============================================================================ roads
def route_roads(G):
    L, h, land, res, water = G["L"], G["h"], G["land"], G["res"], G["water"]
    k = 2                                   # coarse grid for routing
    hc = h[::k, ::k]
    wc = water[::k, ::k] > 0.5
    cr = res * k
    ny, nx = hc.shape
    gy, gx = np.gradient(hc, cr * 1000)
    slope = np.hypot(gx, gy)
    node_cost = 1.0 + 60 * slope ** 1.5 + 0.00025 * np.maximum(hc, 0)
    node_cost = np.where(wc, 1e4, node_cost)
    idx = np.arange(nx * ny).reshape(ny, nx)
    rows, cols, vals = [], [], []
    for dj, di in [(0, 1), (1, 0), (1, 1), (1, -1)]:
        j0, j1 = max(0, -dj), ny - max(0, dj)
        i0, i1 = max(0, -di), nx - max(0, di)
        a = idx[j0:j1, i0:i1].ravel()
        b = idx[j0 + dj:j1 + dj, i0 + di:i1 + di].ravel()
        step = math.hypot(di, dj) * cr
        c = (node_cost.ravel()[a] + node_cost.ravel()[b]) * 0.5 * step
        rows += [a, b]
        cols += [b, a]
        vals += [c, c]
    A = coo_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))),
                   shape=(nx * ny, nx * ny)).tocsr()
    reg = {r["id"]: r for r in L["regions"]}

    def node(p):
        i = int(np.clip(round((p[0] - X0) / cr), 0, nx - 1))
        j = int(np.clip(round((p[1] - Y0) / cr), 0, ny - 1))
        if wc[j, i]:                        # snap to the nearest land node
            lj, li = np.nonzero(~wc)
            n = int(np.argmin((lj - j) ** 2 + (li - i) ** 2))
            j, i = int(lj[n]), int(li[n])
        return j * nx + i

    sources = sorted({reg[l["a"]]["id"] for l in L["links"]})
    src_nodes = [node(reg[s]["pos"]) for s in sources]
    dist, pred = dijkstra(A, directed=False, indices=src_nodes, return_predecessors=True)
    roads, report = [], []
    for l in L["links"]:
        si = sources.index(l["a"])
        tgt = node(reg[l["b"]]["pos"])
        path = []
        n = tgt
        while n >= 0 and n != src_nodes[si]:
            path.append(n)
            n = pred[si, n]
        ok = n == src_nodes[si]
        path.append(src_nodes[si])
        path = path[::-1]
        pts = [(X0 + (p % nx) * cr, Y0 + (p // nx) * cr) for p in path]
        wet = sum(1 for p in path if wc[p // nx, p % nx])
        pts = smooth_polyline(pts, 3)
        km = sum(math.dist(pts[i], pts[i + 1]) for i in range(len(pts) - 1))
        roads.append({"a": l["a"], "b": l["b"], "km": round(km, 1),
                      "pts": [round(v, 3) for p in pts for v in p]})
        report.append(f"road {l['a']:15s} -> {l['b']:15s} {km:6.1f} km  found={ok}  "
                      f"wet_nodes={wet}")
    return roads, report


def smooth_polyline(pts, iters):
    P = [tuple(p) for p in pts[::2]] + [tuple(pts[-1])]
    for _ in range(iters):
        Q = [P[0]]
        for a, b in zip(P[:-1], P[1:]):
            Q.append((0.75 * a[0] + 0.25 * b[0], 0.75 * a[1] + 0.25 * b[1]))
            Q.append((0.25 * a[0] + 0.75 * b[0], 0.25 * a[1] + 0.75 * b[1]))
        Q.append(P[-1])
        P = Q
    # decimate (keep ~1 point / 0.4 km)
    out = [P[0]]
    for p in P[1:-1]:
        if math.dist(p, out[-1]) > 0.4:
            out.append(p)
    out.append(P[-1])
    return out


# ============================================================================ biomes / colour
def biome_weights(L, X, Y, N):
    names = ["desert", "winter", "sakura", "autumn", "bamboo", "tea", "paddy", "boreal",
             "darkforest", "volcanic", "tropical"]
    Wt = {n: np.zeros(X.shape, np.float32) for n in names}
    for z in L["biome_zones"]:
        e = ellipse_r(X, Y, z["c"], z["r"])
        rmean = 0.5 * (z["r"][0] + z["r"][1])
        ekm = (e - 1) * rmean + 2.0 * N.fbm(X / 6 + 3, Y / 6, 3)
        w = smoothstep(z["feather"] * 0.5, -z["feather"] * 0.5, ekm)
        Wt[z["biome"]] = np.maximum(Wt[z["biome"]], w)
    return Wt


def C(*rgb):
    return np.array(rgb, np.float32)


def colourise(G, scale):
    """Albedo + roughness at `scale` x the mesh resolution, plus instancer densities at 1x."""
    L, N, res = G["L"], G["N"], G["res"]
    zoom = lambda A, o=1: ndimage.zoom(A, scale, order=o).astype(np.float32)
    h = zoom(G["h"], 3)
    surf = zoom(G["surf"], 1)
    water = np.clip(zoom(G["water"], 1), 0, 1)
    dist = zoom(G["dist"], 1)
    river = np.clip(zoom(G["river"], 1), 0, 1)
    ny, nx = h.shape
    X, Y = np.meshgrid(np.linspace(G["X"][0, 0], G["X"][0, -1], nx, dtype=np.float32),
                       np.linspace(G["Y"][0, 0], G["Y"][-1, 0], ny, dtype=np.float32))
    r2 = res / scale
    gy, gx = np.gradient(h, r2 * 1000)
    slope = np.hypot(gx, gy)
    Wt = biome_weights(L, X, Y, N)
    n1 = N.fbm(X / 3, Y / 3, 4)
    n2 = N.fbm(X / 0.7 + 50, Y / 0.7, 3)
    n3 = N.fbm(X / 12 - 20, Y / 12 + 8, 3)
    col = np.zeros((ny, nx, 3), np.float32)

    def mix(mask, c):
        nonlocal col
        m = np.clip(mask, 0, 1)[..., None]
        col = col * (1 - m) + c * m

    # temperate base: lowland grass -> meadow -> highland scrub
    lowg = C(0.34, 0.52, 0.21) + 0.05 * n2[..., None] * C(1, 1, 0.3)
    col[:] = lowg
    mix(smoothstep(250, 900, h) * 0.8, C(0.30, 0.46, 0.20))
    mix(smoothstep(1100, 1800, h), C(0.44, 0.47, 0.30))
    # farmland patchwork on flat low ground
    cell = np.floor(X / 1.1 + 0.4 * n1) * 57.0 + np.floor(Y / 0.8 + 0.4 * n1) * 113.0
    hsh = np.mod(np.sin(cell) * 43758.5453, 1.0)
    farm_pal = np.stack([C(0.52, 0.66, 0.24), C(0.70, 0.68, 0.30), C(0.40, 0.60, 0.22),
                         C(0.62, 0.52, 0.26), C(0.46, 0.62, 0.30)])
    farm = farm_pal[(hsh * 5).astype(int).clip(0, 4)]
    farm_m = smoothstep(0.035, 0.012, slope) * smoothstep(320, 120, h) * smoothstep(0.2, 1.5, dist)
    farm_m *= smoothstep(-0.1, 0.25, n3)
    mix(farm_m * 0.6, farm)
    # biome grounds
    mix(0.65 * Wt["paddy"] * smoothstep(0.05, 0.01, slope),
        np.where((hsh > 0.45)[..., None], C(0.52, 0.68, 0.32), C(0.42, 0.62, 0.38)))
    tea_stripe = 0.5 + 0.5 * np.sin(h / 9.0 + n1 * 2)
    mix(Wt["tea"] * smoothstep(0.02, 0.08, slope),
        C(0.20, 0.46, 0.18) * (0.75 + 0.35 * tea_stripe[..., None]))
    mix(Wt["bamboo"] * 0.7, C(0.44, 0.62, 0.26))
    mix(Wt["sakura"] * 0.55, C(0.52, 0.56, 0.32))
    mix(Wt["autumn"] * 0.75, C(0.62, 0.46, 0.20) + 0.06 * n2[..., None])
    mix(Wt["boreal"] * 0.8, C(0.24, 0.36, 0.24))
    mix(Wt["darkforest"] * 0.85, C(0.12, 0.24, 0.16))
    mix(Wt["tropical"] * 0.5, C(0.36, 0.64, 0.26))
    # woodland floor (the instanced canopy sits on this, so gaps read as shade, not lawn)
    fb = forest_base(N, X, Y, h, slope, Wt)
    mix(fb * 0.6 * (1 - Wt["desert"]), C(0.16, 0.31, 0.14) + 0.04 * n2[..., None])
    # desert + canyon strata
    strata = 0.5 + 0.5 * np.sin(h / 38.0 + n1 * 0.8)
    desert = C(0.84, 0.58, 0.32) * (0.85 + 0.25 * strata[..., None]) \
        + C(-0.12, -0.10, -0.06) * smoothstep(0.15, 0.5, slope)[..., None]
    mix(Wt["desert"], desert)
    mix(Wt["desert"] * smoothstep(0.25, 0.7, slope), C(0.62, 0.30, 0.18) * (0.8 + 0.3 * strata[..., None]))
    # rock on steep/high ground
    rock = C(0.44, 0.41, 0.38) + 0.07 * n2[..., None]
    rock_m = smoothstep(0.55, 1.1, slope) + smoothstep(2150, 2600, h)
    mix(np.clip(rock_m, 0, 1) * (1 - Wt["desert"]), rock)
    mix(Wt["volcanic"] * smoothstep(900, 1800, h), C(0.30, 0.26, 0.25) + 0.05 * n2[..., None])
    # snow: above the snowline (winter Azora from 1,150 m); thins on steep faces
    sl = L["snowline_m"] + 380 + 260 * n1 - 700 * Wt["winter"] + 260 * n3 * Wt["winter"]
    snow_m = smoothstep(sl - 80, sl + 220, h) * smoothstep(1.1, 0.55, slope)
    mix(snow_m, C(0.94, 0.96, 1.0))
    # beaches
    beach = smoothstep(0.55, 0.05, dist) * smoothstep(14, 3, h) * smoothstep(0.25, 0.05, slope)
    beach *= 1 - Wt["desert"] * 0.3
    mix(beach * (dist > -0.1), C(0.90, 0.83, 0.62))
    # settlements: grey-beige urban ground
    city = np.zeros((ny, nx), np.float32)
    for s in L["settlements"]:
        r = np.hypot(X - s["c"][0], Y - s["c"][1])
        city = np.maximum(city, smoothstep(s["r"], s["r"] * 0.55, r + 0.35 * s["r"] * n1) * s["density"])
    mix(city * 0.85 * (water < 0.5), C(0.62, 0.60, 0.57) + 0.04 * n2[..., None])

    # water: lagoon -> shallow -> deep; lakes/rivers get the shallow ramp by their own depth
    depth = np.maximum(surf - h, 0)
    sea = dist < 0
    dd = np.where(sea, -dist, 0) * (1.0 + 0.45 * n3)
    wc = np.where(sea[..., None],
                  np.where((dd < 0.7)[..., None],
                           C(0.20, 0.66, 0.66) * (1 - smoothstep(0, 0.7, dd)[..., None])
                           + C(0.07, 0.38, 0.55) * smoothstep(0, 0.7, dd)[..., None],
                           C(0.07, 0.38, 0.55) * (1 - smoothstep(0.7, 6.5, dd)[..., None])
                           + C(0.03, 0.15, 0.37) * smoothstep(0.7, 6.5, dd)[..., None]),
                  C(0.10, 0.44, 0.58) * (1 - smoothstep(2, 30, depth)[..., None])
                  + C(0.05, 0.25, 0.45) * smoothstep(2, 30, depth)[..., None])
    wc = wc + 0.02 * n2[..., None]
    mix(water, wc)
    # surf line
    foam = smoothstep(0.35, 0.0, np.abs(dist + 0.12)) * sea * (1 - Wt["tropical"] * 0.5)
    mix(foam * 0.55, C(0.92, 0.97, 1.0))
    col = np.clip(col, 0, 1)

    rough = np.where(water > 0.5, 0.06, 0.85 - 0.25 * snow_m).astype(np.float32)
    return col, rough, dict(slope=slope, Wt=Wt, city=city)


def forest_base(N, X, Y, h, slope, Wt):
    """Where woodland grows (0..1): patchy, thinning on steep ground, above the treeline and on
    farmland, deserts, paddies and tea terraces. Shared by the albedo and the tree instancer."""
    f = smoothstep(-0.12, 0.18, N.fbm(X / 8 + 30, Y / 8 - 12, 5) + 0.25 * smoothstep(200, 900, h))
    f = f * smoothstep(1.3, 0.8, slope)
    treeline = 2150 - 350 * Wt["winter"]
    f = f * smoothstep(treeline, treeline - 250, h)
    farm = smoothstep(0.035, 0.012, slope) * smoothstep(320, 120, h) * \
        smoothstep(-0.1, 0.25, N.fbm(X / 12 - 20, Y / 12 + 8, 3))
    f = f * (1 - 0.85 * farm) * (1 - Wt["desert"]) * (1 - Wt["paddy"]) * (1 - 0.8 * Wt["tea"])
    return f


def hamlets(roads, spacing=13.0):
    """Small villages strung along the road network, one every ~spacing km."""
    out = []
    for rd in roads:
        P = np.asarray(rd["pts"]).reshape(-1, 2)
        seg = np.linalg.norm(np.diff(P, axis=0), axis=1)
        cum = np.concatenate([[0], np.cumsum(seg)])
        for s in np.arange(spacing * 0.6, cum[-1] - 4, spacing):
            out.append({"name": "hamlet", "c": [float(np.interp(s, cum, P[:, 0])),
                                                  float(np.interp(s, cum, P[:, 1]))],
                        "r": 0.7, "density": 0.45, "towers": False})
    return out


def densities(G, extra_settlements=()):
    """Per-vertex instance densities (per km^2) at mesh resolution."""
    L, N, X, Y, res = G["L"], G["N"], G["X"], G["Y"], G["res"]
    h, water, dist = G["h"], G["water"], G["dist"]
    gy, gx = np.gradient(h, res * 1000)
    slope = np.hypot(gx, gy)
    Wt = biome_weights(L, X, Y, N)
    dry = (water < 0.05) & (dist > 0.15)
    forest = forest_base(N, X, Y, h, slope, Wt)
    city = np.zeros_like(h)
    towers = np.zeros_like(h)
    for s in list(L["settlements"]) + list(extra_settlements):
        r = np.hypot(X - s["c"][0], Y - s["c"][1])
        if s["r"] < 1.0:
            box = r < 2.0
            if not box.any():
                continue
            c = np.where(box, smoothstep(s["r"], s["r"] * 0.3, r), 0.0)
            city = np.maximum(city, c * s["density"])
            continue
        c = smoothstep(s["r"], s["r"] * 0.35, r + 0.3 * s["r"] * N.fbm(X / 1.5, Y / 1.5, 2))
        city = np.maximum(city, c * s["density"])
        if s.get("towers"):
            towers = np.maximum(towers, smoothstep(s["r"] * 0.45, 0.4, r))
    open_land = 1 - smoothstep(0.1, 0.5, city)
    forest = forest * dry * open_land
    conifer = np.clip(smoothstep(650, 1300, h) + Wt["boreal"] + Wt["winter"] + Wt["darkforest"], 0, 1)
    special = np.clip(Wt["sakura"] + Wt["autumn"] + Wt["bamboo"] + Wt["tropical"], 0, 1)
    D = {
        "attr_conifer": 4.2 * forest * conifer * (1 - special) * (1 - Wt["winter"]),
        "attr_snowpine": 4.0 * forest * Wt["winter"],
        "attr_broadleaf": 3.8 * forest * (1 - conifer) * (1 - special),
        "attr_sakura": 5.0 * forest * Wt["sakura"] * (1 - Wt["winter"]) + 2.0 * Wt["sakura"] * dry * open_land * smoothstep(0.9, 0.4, slope) * (1 - forest),
        "attr_autumn": 4.6 * forest * Wt["autumn"] + 1.2 * Wt["autumn"] * dry * open_land * (1 - forest),
        "attr_bamboo": 6.0 * forest * Wt["bamboo"],
        "attr_palm": 2.4 * forest * Wt["tropical"],
        "attr_darkpine": 5.5 * forest * Wt["darkforest"],
        "attr_shrub": 0.9 * Wt["desert"] * dry * smoothstep(0.4, 0.1, slope),
        "attr_house": 26.0 * city * (1 - towers) * (water < 0.05) * smoothstep(0.35, 0.1, slope),
        "attr_tower": 30.0 * towers * (water < 0.05),
    }
    return {k: np.clip(v, 0, None).astype(np.float32) for k, v in D.items()}


# ============================================================================ checks
def validate(G, roads_report):
    L, h, res, log = G["L"], G["h"], G["res"], G["log"]
    errors = []
    lines = ["# world_terrain validation", ""]
    lines += log
    lines += roads_report
    for line in log:
        if line.startswith("river") and "monotone=False" in line:
            errors.append(line)
    for r in L["regions"]:
        x, y = r["pos"]
        jj, ii = int(round((y - Y0) / res)), int(round((x - X0) / res))
        on_land = G["land"][jj, ii]
        if not on_land and G["water"][jj, ii] > 0.5:
            errors.append(f"region {r['id']} pin at {r['pos']} is in water")
        sx, sy, target = r["summit"]
        got = bilinear(h, sx, sy, res)
        if abs(got - target) > 60:
            errors.append(f"region {r['id']} summit {got:.0f} m != {target} m")
        sub = r.get("subtitle", "")
        if sub.endswith(" m") and sub.replace(",", "").replace(" m", "").isdigit():
            if int(sub.replace(",", "").replace(" m", "")) != int(target):
                errors.append(f"region {r['id']} subtitle '{sub}' != summit {target}")
    for line in roads_report:
        if "found=False" in line:
            errors.append(line)
    lines.append("")
    lines.append(f"max terrain {h.max():.0f} m, min {h.min():.0f} m, land {G['land'].mean() * 100:.1f} %")
    lines.append("ERRORS: " + ("none" if not errors else str(len(errors))))
    lines += ["  " + e for e in errors]
    return lines, errors


# ============================================================================ main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--res", type=float, default=0.2)
    ap.add_argument("--albedo-scale", type=int, default=2)
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    G = build(a.res)
    roads, rep = route_roads(G)
    print("roads done")
    lines, errors = validate(G, rep)
    open(os.path.join(OUT, "validation.txt"), "w").write("\n".join(lines) + "\n")
    print("\n".join(lines[-12:]))

    np.save(os.path.join(OUT, "surf_m.npy"), G["surf"])
    np.save(os.path.join(OUT, "land_m.npy"), G["h"].astype(np.float32))
    np.save(os.path.join(OUT, "water.npy"), G["water"])
    for k, v in densities(G, hamlets(roads)).items():
        np.save(os.path.join(OUT, k + ".npy"), v)
    col, rough, _ = colourise(G, a.albedo_scale)
    # palette values are authored as sRGB (Blender loads albedo.png as sRGB)
    Image.fromarray((np.flipud(col) * 255 + 0.5).astype(np.uint8)).save(
        os.path.join(OUT, "albedo.png"))
    Image.fromarray((np.flipud(rough) * 255).astype(np.uint8)).save(os.path.join(OUT, "rough.png"))
    L = G["L"]
    json.dump({
        "roads": roads,
        "routes": [{"id": r["id"], "status": r["status"], "pts": r["route"]} for r in L["regions"] if r["route"]],
        "bridges": L["bridges"], "ferries": L["ferries"], "rivers": G["rivers"],
    }, open(os.path.join(OUT, "roads.json"), "w"), indent=0)
    json.dump({"W": G["W"], "H": G["H"], "res": a.res, "nx": G["h"].shape[1], "ny": G["h"].shape[0],
               "x0": X0, "y0": Y0,
               "albedo_scale": a.albedo_scale}, open(os.path.join(OUT, "meta.json"), "w"))
    print(f"outputs in {OUT}")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()

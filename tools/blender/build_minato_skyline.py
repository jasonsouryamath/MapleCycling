"""
MINATO COAST - SKYLINE workstream: the "Glass Tide District" building families, plaza pieces
and elevated transit (MINATO_VISUAL_DESIGN.md zone 2 + the distant skyline).

Run (no Unity lock needed):
    tools/blender-4.5.10-windows-x64/blender.exe -b --factory-startup -P tools/blender/build_minato_skyline.py
    ... -- TowerSlimA PodiumA        (build only the named assets)
Textures come from build_minato_skyline_textures.py (plain python).

WHAT THIS REPLACES. The old Minato_City_SkyTowerA-E kit was five box stacks with a modelled
white mullion + spandrel grid on EVERY floor. Repeated 70 times it read as one generic tower
stamped over and over, and at range the grid became high-contrast graph paper. The families here
are built to be told apart by SILHOUETTE first (plan shape, setbacks, crowns, rooflines), then by
facade rhythm (curtain wall vs white ribbon vs shopfront), with the fine grid carried by a
low-contrast tiling texture:

  Slim glass      TowerSlimA  chamfered plan, two setbacks, teal lantern crown + corner fins
                  TowerSlimB  notched (cruciform) plan, lobby canopy, spire
  Curved glass    TowerRoundA cylinder, slab rings every 5 floors, white cornice + roof garden
                  TowerRoundB stadium plan, sloped glass crown, white vertical fins
  Stepped         TowerStepped four receding tiers, planted terrace edges, teal glass
  White midrise   MidriseA    white ribbon facade, brushed-metal fins, retail base + canopy
                  MidriseB    rounded plan, cantilevered white balcony slabs, roof garden
  Podium          PodiumA     2 storeys, cafe roof terrace with coral parasols
                  PodiumB     3 stepped storeys with planted edges
  Transit         GuidewayBeam / GuidewayPier / MonorailHead / MonorailCar
  Plaza           Skybridge, RingSculpture, Planter

CONVENTIONS (identical to the other Minato builders): authored in UNITY coordinates, converted
once through sakura_lib.u2b; origin at the ground contact point; local +Z faces the ROAD (Unity
places buildings with LookRotation(towards road)). Every asset ships _LOD0/_LOD1/_LOD2 siblings
BUILT, not decimated - a collapse-decimate of a curtain-wall box destroys its silhouette, while an
authored LOD simply drops fins, rings and gardens and keeps the masses.

UV CONTRACT: walls get u = perimeter metres / tile width, v = height metres / tile height, so the
facade texture tiles at true scale on any plan (chamfered, notched, round). Foliage blobs get the
Minato_Foliage_Gradient v (underside 0.25 -> crown 1.0), the contract of MedianFoliageMaterial.

Material SLOT NAMES are the contract with MinatoRedesign.Skyline.cs (Minato_sky_*). The Blender
colours are preview-only. ALL dimensions PROVISIONAL illustrative tuning.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sakura_lib as S  # noqa: E402
import minato_lib as M  # noqa: E402

u2b = S.u2b

# Preview colours for the Skyline slots (Unity re-materialises by slot name).
M.PALETTE.update({
    "sky_glass_blue":  (0.30, 0.50, 0.70, 1.0),
    "sky_glass_teal":  (0.26, 0.55, 0.58, 1.0),
    "sky_glass_deep":  (0.14, 0.26, 0.36, 1.0),
    "sky_frame":       (0.86, 0.88, 0.90, 1.0),
    "sky_concrete":    (0.92, 0.91, 0.88, 1.0),
    "sky_metal":       (0.70, 0.73, 0.76, 1.0),
    "sky_steel":       (0.82, 0.84, 0.86, 1.0),
    "sky_roof":        (0.55, 0.57, 0.58, 1.0),
    "sky_coral":       (0.93, 0.45, 0.33, 1.0),
    "sky_garden":      (0.30, 0.52, 0.26, 1.0),
    "sky_ribbon":      (0.90, 0.90, 0.88, 1.0),
    "sky_shop":        (0.90, 0.70, 0.45, 1.0),
    "sky_train":       (0.95, 0.95, 0.95, 1.0),
    "sky_livery":      (0.12, 0.34, 0.72, 1.0),
    "sky_dark":        (0.12, 0.14, 0.16, 1.0),
})

# Facade tile sizes in metres (u, v) - MUST match build_minato_skyline_textures.py.
TILE = {
    "sky_glass_blue": (6.0, 15.0), "sky_glass_teal": (6.0, 15.0), "sky_glass_deep": (6.0, 15.0),
    "sky_ribbon": (6.0, 7.5), "sky_shop": (9.0, 5.0),
}
DEFAULT_TILE = (4.0, 4.0)


# =================================================================== mesh accumulator

class Acc:
    """Unity-space polygon soup with per-loop UVs, per-face slot and smooth flag."""

    def __init__(self):
        self.v, self.f, self.uv, self.mi, self.sm = [], [], [], [], []
        self.slots = []

    def slot(self, name):
        if name not in self.slots:
            self.slots.append(name)
        return self.slots.index(name)

    def vert(self, p):
        self.v.append(tuple(float(c) for c in p))
        return len(self.v) - 1

    def face(self, idx, uvs, slot, want=None, smooth=False):
        """Add a polygon; ``want`` is the intended OUTWARD normal in Unity space. The winding is
        fixed in BLENDER space (u2b is a reflection, so it cannot be reasoned about in Unity
        space without a sign flip - measuring after conversion removes that trap)."""
        idx = list(idx)
        uvs = list(uvs)
        if want is not None:
            pts = [Vector(u2b(*self.v[i])) for i in idx]
            n = Vector((0.0, 0.0, 0.0))
            for k in range(len(pts)):
                a, b = pts[k], pts[(k + 1) % len(pts)]
                n.x += (a.y - b.y) * (a.z + b.z)
                n.y += (a.z - b.z) * (a.x + b.x)
                n.z += (a.x - b.x) * (a.y + b.y)
            if n.dot(Vector(u2b(*want))) < 0.0:
                idx.reverse()
                uvs.reverse()
        self.f.append(idx)
        self.uv.extend(uvs)
        self.mi.append(self.slot(slot))
        self.sm.append(bool(smooth))

    def poly(self, pts, uvs, slot, want=None, smooth=False):
        self.face([self.vert(p) for p in pts], uvs, slot, want, smooth)

    def build(self, name):
        verts = [u2b(*p) for p in self.v]
        obj = S.mesh_from_arrays(name, verts, self.f, uvs=self.uv, smooth=False)
        me = obj.data
        for s in self.slots:
            me.materials.append(M.mat(s))
        me.polygons.foreach_set("material_index", self.mi)
        me.polygons.foreach_set("use_smooth", self.sm)
        me.update()
        me.calc_loop_triangles()
        return obj


# =================================================================== plans (x, z) lists

def rect(w, d, cx=0.0, cz=0.0):
    hw, hd = w * 0.5, d * 0.5
    return [(cx - hw, cz - hd), (cx + hw, cz - hd), (cx + hw, cz + hd), (cx - hw, cz + hd)]


def chamfer(w, d, c, cx=0.0, cz=0.0):
    hw, hd = w * 0.5, d * 0.5
    return [(cx - hw + c, cz - hd), (cx + hw - c, cz - hd), (cx + hw, cz - hd + c),
            (cx + hw, cz + hd - c), (cx + hw - c, cz + hd), (cx - hw + c, cz + hd),
            (cx - hw, cz + hd - c), (cx - hw, cz - hd + c)]


def notched(w, d, n, cx=0.0, cz=0.0):
    """Rectangle with square re-entrant corner notches (a cruciform tower plan)."""
    hw, hd = w * 0.5, d * 0.5
    return [(cx - hw + n, cz - hd), (cx + hw - n, cz - hd), (cx + hw - n, cz - hd + n),
            (cx + hw, cz - hd + n), (cx + hw, cz + hd - n), (cx + hw - n, cz + hd - n),
            (cx + hw - n, cz + hd), (cx - hw + n, cz + hd), (cx - hw + n, cz + hd - n),
            (cx - hw, cz + hd - n), (cx - hw, cz - hd + n), (cx - hw + n, cz - hd + n)]


def rounded(w, d, r, seg, cx=0.0, cz=0.0):
    hw, hd = w * 0.5 - r, d * 0.5 - r
    pts = []
    for (ox, oz, a0) in ((hw, -hd, -90.0), (hw, hd, 0.0), (-hw, hd, 90.0), (-hw, -hd, 180.0)):
        for k in range(seg + 1):
            a = math.radians(a0 + 90.0 * k / seg)
            pts.append((cx + ox + math.cos(a) * r, cz + oz + math.sin(a) * r))
    return pts


def circle(r, n, cx=0.0, cz=0.0):
    return [(cx + math.cos(k / n * math.tau) * r, cz + math.sin(k / n * math.tau) * r)
            for k in range(n)]


def stadium(w, d, seg):
    return rounded(w, d, d * 0.5 - 0.001, seg)


def scale_plan(plan, s, sz=None, dz=0.0, dx=0.0):
    sz = s if sz is None else sz
    return [(x * s + dx, z * sz + dz) for (x, z) in plan]


def plan_area(plan):
    a = 0.0
    for k in range(len(plan)):
        x0, z0 = plan[k]
        x1, z1 = plan[(k + 1) % len(plan)]
        a += x0 * z1 - x1 * z0
    return a * 0.5


def edge_out(plan, k):
    """Outward unit normal (x, z) of edge k."""
    x0, z0 = plan[k]
    x1, z1 = plan[(k + 1) % len(plan)]
    dx, dz = x1 - x0, z1 - z0
    ln = math.hypot(dx, dz) or 1.0
    if plan_area(plan) > 0:
        return (dz / ln, -dx / ln)
    return (-dz / ln, dx / ln)


def offset_plan(plan, off):
    """Mitred outward offset (for proud bands, cornices, slab edges)."""
    n = len(plan)
    out = []
    for k in range(n):
        na = edge_out(plan, (k - 1) % n)
        nb = edge_out(plan, k)
        mx, mz = na[0] + nb[0], na[1] + nb[1]
        ln = math.hypot(mx, mz) or 1.0
        mx, mz = mx / ln, mz / ln
        cosh = max(0.35, mx * nb[0] + mz * nb[1])
        out.append((plan[k][0] + mx * off / cosh, plan[k][1] + mz * off / cosh))
    return out


def centroid(plan):
    return (sum(p[0] for p in plan) / len(plan), sum(p[1] for p in plan) / len(plan))


# =================================================================== primitives

def _yf(y):
    return y if callable(y) else (lambda x, z, _y=y: _y)


def walls(a, plan, y0, y1, slot, smooth=False, tile=None):
    """Extruded wall ring. ``y0``/``y1`` may be callables (x, z) -> y for sloped tops."""
    tw, th = tile or TILE.get(slot, DEFAULT_TILE)
    f0, f1 = _yf(y0), _yf(y1)
    n = len(plan)
    s = 0.0
    if smooth:
        bot = [a.vert((x, f0(x, z), z)) for (x, z) in plan]
        top = [a.vert((x, f1(x, z), z)) for (x, z) in plan]
    for k in range(n):
        (xa, za), (xb, zb) = plan[k], plan[(k + 1) % n]
        ln = math.hypot(xb - xa, zb - za)
        if ln < 1e-4:
            continue
        ox, oz = edge_out(plan, k)
        if smooth:
            # Smooth ring: average the neighbouring edge normals is done by Blender from the
            # shared vertices; the want vector only fixes the winding.
            idx = [bot[k], bot[(k + 1) % n], top[(k + 1) % n], top[k]]
        else:
            idx = [a.vert((xa, f0(xa, za), za)), a.vert((xb, f0(xb, zb), zb)),
                   a.vert((xb, f1(xb, zb), zb)), a.vert((xa, f1(xa, za), za))]
        ya0, yb0, yb1, ya1 = f0(xa, za), f0(xb, zb), f1(xb, zb), f1(xa, za)
        uvs = [(s / tw, ya0 / th), ((s + ln) / tw, yb0 / th),
               ((s + ln) / tw, yb1 / th), (s / tw, ya1 / th)]
        a.face(idx, uvs, slot, want=(ox, 0.0, oz), smooth=smooth)
        s += ln


def cap(a, plan, y, slot, up=True, tile=None):
    tw, th = tile or TILE.get(slot, DEFAULT_TILE)
    f = _yf(y)
    pts = [(x, f(x, z), z) for (x, z) in plan]
    uvs = [(x / tw, z / th) for (x, z) in plan]
    a.poly(pts, uvs, slot, want=(0.0, 1.0 if up else -1.0, 0.0))


def annulus(a, inner, outer, y, slot, up=True):
    n = len(inner)
    f = _yf(y)
    for k in range(n):
        k1 = (k + 1) % n
        pts = [(inner[k][0], f(*inner[k]), inner[k][1]), (inner[k1][0], f(*inner[k1]), inner[k1][1]),
               (outer[k1][0], f(*outer[k1]), outer[k1][1]), (outer[k][0], f(*outer[k]), outer[k][1])]
        uvs = [(p[0] * 0.25, p[2] * 0.25) for p in pts]
        a.poly(pts, uvs, slot, want=(0.0, 1.0 if up else -1.0, 0.0))


def band(a, plan, y, h, out, slot, smooth=False):
    """A proud horizontal band (slab edge, cornice, canopy lip) around ``plan``."""
    op = offset_plan(plan, out)
    walls(a, op, y, y + h, slot, smooth=smooth, tile=(4.0, 4.0))
    annulus(a, plan, op, y + h, slot, up=True)
    annulus(a, plan, op, y, slot, up=False)


def volume(a, plan, y0, y1, slot, roof="sky_roof", smooth=False, bottom=False):
    walls(a, plan, y0, y1, slot, smooth=smooth)
    if roof:
        cap(a, plan, y1, roof)
    if bottom:
        cap(a, plan, y0, roof or slot, up=False)


def obox(a, p0, p1, w, h, slot, up=(0.0, 1.0, 0.0), uvm=0.5):
    """Oriented box from p0 to p1 with section w (right) x h (up)."""
    p0, p1 = Vector(p0), Vector(p1)
    f = (p1 - p0)
    ln = f.length
    f.normalize()
    upv = Vector(up)
    if abs(f.dot(upv)) > 0.98:
        upv = Vector((1.0, 0.0, 0.0)) if abs(f.x) < 0.9 else Vector((0.0, 0.0, 1.0))
    r = f.cross(upv).normalized()
    u = r.cross(f).normalized()
    hw, hh = w * 0.5, h * 0.5
    c = [p0 - r * hw - u * hh, p0 + r * hw - u * hh, p0 + r * hw + u * hh, p0 - r * hw + u * hh]
    c += [q + f * ln for q in c]
    faces = [((0, 1, 2, 3), -f, w, h), ((4, 5, 6, 7), f, w, h), ((0, 1, 5, 4), -u, w, ln),
             ((3, 2, 6, 7), u, w, ln), ((1, 2, 6, 5), r, h, ln), ((0, 3, 7, 4), -r, h, ln)]
    for (idx, n, su, sv) in faces:
        pts = [tuple(c[i]) for i in idx]
        uvs = [(0, 0), (su * uvm, 0), (su * uvm, sv * uvm), (0, sv * uvm)]
        a.poly(pts, uvs, slot, want=tuple(n))


def box(a, centre, size, slot):
    cx, cy, cz = centre
    sx, sy, sz = size
    obox(a, (cx, cy, cz - sz * 0.5), (cx, cy, cz + sz * 0.5), sx, sy, slot)


def cyl(a, p0, p1, r, seg, slot, taper=1.0, caps=True, smooth=True):
    p0, p1 = Vector(p0), Vector(p1)
    f = (p1 - p0).normalized()
    upv = Vector((1.0, 0.0, 0.0)) if abs(f.x) < 0.9 else Vector((0.0, 0.0, 1.0))
    r1 = f.cross(upv).normalized()
    u1 = r1.cross(f).normalized()
    ring0, ring1 = [], []
    for k in range(seg):
        ang = k / seg * math.tau
        d = r1 * math.cos(ang) + u1 * math.sin(ang)
        ring0.append(a.vert(tuple(p0 + d * r)))
        ring1.append(a.vert(tuple(p1 + d * r * taper)))
    for k in range(seg):
        k1 = (k + 1) % seg
        ang = (k + 0.5) / seg * math.tau
        d = r1 * math.cos(ang) + u1 * math.sin(ang)
        a.face([ring0[k], ring0[k1], ring1[k1], ring1[k]],
               [(k / seg, 0), ((k + 1) / seg, 0), ((k + 1) / seg, 1), (k / seg, 1)],
               slot, want=tuple(d), smooth=smooth)
    if caps:
        a.face(list(ring0), [(0.5, 0.5)] * seg, slot, want=tuple(-f))
        if taper > 0.01:
            a.face(list(ring1), [(0.5, 0.5)] * seg, slot, want=tuple(f))


_ICO = None


def _icosphere(sub):
    t = (1.0 + 5 ** 0.5) / 2.0
    vs = [Vector(v).normalized() for v in
          [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t),
           (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]]
    fs = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4),
          (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8),
          (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    for _ in range(sub):
        cache = {}
        nf = []

        def mid(i, j):
            key = (min(i, j), max(i, j))
            if key not in cache:
                vs.append(((vs[i] + vs[j]) * 0.5).normalized())
                cache[key] = len(vs) - 1
            return cache[key]
        for (i, j, k) in fs:
            ij, jk, ki = mid(i, j), mid(j, k), mid(k, i)
            nf += [(i, ij, ki), (j, jk, ij), (k, ki, jk), (ij, jk, ki)]
        fs = nf
    return vs, fs


def blob(a, centre, radius, slot="sky_garden", squash=(1.0, 1.0, 1.0), sub=1, seed=0.0):
    """Leafy foliage mass: noisy icosphere, smooth shaded, foliage-gradient UVs."""
    vs, fs = _icosphere(sub)
    cx, cy, cz = centre
    idx = []
    u0 = (abs(cx) * 7.13 + abs(cz) * 3.7 + seed) % 1.0
    uvy = []
    for v in vs:
        wob = 1.0 + 0.14 * math.sin(v.x * 5.1 + seed) * math.cos(v.z * 4.3 + cx) \
              + 0.08 * math.sin(v.y * 7.7 + cz)
        p = (cx + v.x * radius * squash[0] * wob, cy + v.y * radius * squash[1] * wob,
             cz + v.z * radius * squash[2] * wob)
        idx.append(a.vert(p))
        uvy.append(0.25 + 0.75 * max(0.0, min(1.0, (v.y + 1.0) * 0.5)))
    for (i, j, k) in fs:
        n = (vs[i] + vs[j] + vs[k]) / 3.0
        a.face([idx[i], idx[j], idx[k]], [(u0, uvy[i]), (u0, uvy[j]), (u0, uvy[k])], slot,
               want=(n.x, n.y, n.z), smooth=True)


def torus(a, centre, R, r, seg, rseg, slot, rot):
    """Torus about local Y, then rotated by the 3x3 ``rot`` (mathutils Matrix)."""
    c = Vector(centre)
    rings = []
    for i in range(seg):
        th = i / seg * math.tau
        ring = []
        for j in range(rseg):
            ph = j / rseg * math.tau
            p = Vector(((R + r * math.cos(ph)) * math.cos(th), r * math.sin(ph),
                        (R + r * math.cos(ph)) * math.sin(th)))
            ring.append(a.vert(tuple(c + rot @ p)))
        rings.append(ring)
    for i in range(seg):
        i1 = (i + 1) % seg
        th = (i + 0.5) / seg * math.tau
        for j in range(rseg):
            j1 = (j + 1) % rseg
            ph = (j + 0.5) / rseg * math.tau
            n = rot @ Vector((math.cos(ph) * math.cos(th), math.sin(ph), math.cos(ph) * math.sin(th)))
            a.face([rings[i][j], rings[i1][j], rings[i1][j1], rings[i][j1]],
                   [(i / seg * 4, j / rseg), ((i + 1) / seg * 4, j / rseg),
                    ((i + 1) / seg * 4, (j + 1) / rseg), (i / seg * 4, (j + 1) / rseg)],
                   slot, want=tuple(n), smooth=True)


def tree(a, x, y, z, h, sub=1, seed=0.0):
    """A small roof-garden / terrace tree: slim trunk + two foliage masses."""
    cyl(a, (x, y, z), (x, y + h * 0.55, z), 0.16 * h / 4.0 + 0.08, 5, "sky_roof", caps=False)
    blob(a, (x, y + h * 0.66, z), h * 0.30, squash=(1.0, 0.8, 1.0), sub=sub, seed=seed)
    blob(a, (x + h * 0.12, y + h * 0.86, z - h * 0.05), h * 0.20, sub=sub, seed=seed + 1.7)


def planter_row(a, x0, x1, y, z, depth, lod, seed=0.0):
    """White concrete planter strip with shrub masses, along X at the given Z."""
    box(a, ((x0 + x1) * 0.5, y + 0.45, z), (x1 - x0, 0.9, depth), "sky_concrete")
    if lod >= 2:
        return
    n = max(2, int((x1 - x0) / (3.2 if lod == 0 else 7.0)))
    for k in range(n):
        x = x0 + (k + 0.5) * (x1 - x0) / n
        blob(a, (x, y + 1.15, z), 0.95 if lod == 0 else 1.2, squash=(1.25, 0.75, 0.85),
             sub=1, seed=seed + k * 1.3)


# =================================================================== building families

def tower_slim_a(lod):
    a = Acc()
    P0 = chamfer(24, 20, 3.5)
    PP = chamfer(32, 28, 5.0)
    P1 = scale_plan(P0, 0.78)
    P2 = scale_plan(P0, 0.58)
    # Retail podium.
    volume(a, PP, 0, 7, "sky_shop", roof="sky_concrete")
    if lod <= 1:
        band(a, PP, 6.4, 0.7, 0.9, "sky_concrete")            # canopy lip over the shopfronts
    volume(a, P0, 7, 84, "sky_glass_blue", roof="sky_roof")
    volume(a, P1, 84, 104, "sky_glass_blue", roof="sky_roof")
    volume(a, P2, 104, 112, "sky_glass_teal", roof="sky_roof")   # lantern crown
    if lod <= 1:
        band(a, P0, 83.6, 1.0, 0.35, "sky_frame")
        band(a, P1, 103.6, 0.9, 0.3, "sky_coral")             # the one restrained coral line
    if lod == 0:
        # Corner fins rise past the lantern: the tower's signature crown silhouette.
        for (x, z) in (P1[2], P1[3], P1[6], P1[7]):
            obox(a, (x * 1.02, 84.0, z * 1.02), (x * 1.02, 119.0, z * 1.02), 0.7, 2.2, "sky_steel")
        box(a, (0, 113.2, 0), (7, 2.4, 5), "sky_metal")
        # Mid-height slab band - breaks the long shaft into a readable rhythm.
        band(a, P0, 45.0, 0.6, 0.3, "sky_frame")
    return a


def tower_slim_b(lod):
    a = Acc()
    N0 = notched(22, 22, 3.2)
    N1 = scale_plan(N0, 0.72)
    walls(a, N0, 0, 6, "sky_shop")
    volume(a, N0, 6, 134, "sky_glass_teal", roof="sky_roof")
    volume(a, N1, 134, 148, "sky_glass_blue", roof="sky_roof")
    if lod <= 1:
        band(a, N0, 5.6, 0.6, 1.8, "sky_concrete")            # lobby canopy
        band(a, N0, 133.6, 1.0, 0.35, "sky_frame")
        cyl(a, (0, 148, 0), (0, 172, 0), 0.9, 8 if lod == 0 else 5, "sky_steel", taper=0.15)
    if lod == 0:
        # Brushed-metal ribs on the re-entrant corners catch the low sun.
        for k in (2, 5, 8, 11):
            x, z = N0[k]
            obox(a, (x, 6.0, z), (x, 134.0, z), 0.45, 0.45, "sky_steel")
        cyl(a, (0, 148, 0), (0, 150.2, 0), 2.4, 10, "sky_coral")   # beacon collar
        for y in (48.0, 91.0):
            band(a, N0, y, 0.6, 0.3, "sky_frame")
    return a


def tower_round_a(lod):
    a = Acc()
    n = (32, 20, 12)[lod]
    C = circle(15.0, n)
    walls(a, circle(19.0, n), 0, 6, "sky_shop", smooth=True)
    annulus(a, C, circle(19.0, n), 6, "sky_concrete")
    walls(a, C, 6, 118, "sky_glass_blue", smooth=True)
    if lod <= 1:
        band(a, circle(19.0, n), 5.5, 0.6, 0.8, "sky_concrete")
        band(a, C, 118, 1.6, 0.6, "sky_concrete", smooth=True)   # white cornice
        cap(a, C, 119.6, "sky_roof")
        walls(a, circle(12.0, n), 119.6, 120.6, "sky_concrete", smooth=True)
        cap(a, circle(12.0, n), 120.6, "sky_garden", tile=(40.0, 40.0))
    else:
        cap(a, C, 118, "sky_roof")
    if lod == 0:
        for y in range(1, 6):
            band(a, C, 6 + y * 18.75, 0.35, 0.22, "sky_frame", smooth=True)
        for k in range(6):
            ang = k / 6 * math.tau + 0.4
            tree(a, math.cos(ang) * 8.0, 120.6, math.sin(ang) * 8.0, 5.0 + (k % 3), sub=1, seed=k)
        blob(a, (0, 121.6, 0), 3.0, squash=(1.2, 0.7, 1.2), sub=1, seed=9)
    elif lod == 1:
        for k in range(3):
            ang = k / 3 * math.tau
            blob(a, (math.cos(ang) * 7.0, 123.0, math.sin(ang) * 7.0), 3.0, sub=0)
    return a


def tower_round_b(lod):
    a = Acc()
    seg = (8, 5, 3)[lod]
    P = stadium(36, 18, seg)
    PP = stadium(42, 24, seg)

    def top(x, z):
        return 88.0 + 14.0 * (x + 18.0) / 36.0

    walls(a, PP, 0, 6, "sky_shop", smooth=True)
    annulus(a, P, PP, 6, "sky_concrete")
    walls(a, P, 6, top, "sky_glass_teal", smooth=True)
    cap(a, P, top, "sky_glass_deep", tile=(6.0, 6.0))            # sloped glass crown
    if lod <= 1:
        band(a, PP, 5.5, 0.6, 0.8, "sky_concrete", smooth=True)
        # Metal edge along the sloped crown.
        op = offset_plan(P, 0.3)
        walls(a, op, lambda x, z: top(x, z) - 1.2, lambda x, z: top(x, z) + 0.4, "sky_steel",
              smooth=True, tile=(4.0, 4.0))
    if lod == 0:
        # White vertical fins on the two flat faces.
        for zf in (-9.0, 9.0):
            for k in range(9):
                x = -9.0 + k * 2.25
                s = 1.0 if zf > 0 else -1.0
                obox(a, (x, 6.0, zf + s * 0.55), (x, top(x, zf) - 0.8, zf + s * 0.55),
                     0.35, 1.1, "sky_frame", up=(0.0, 0.0, 1.0))
        for y in (32.0, 60.0):
            band(a, P, y, 0.5, 0.25, "sky_frame", smooth=True)
    return a


def tower_stepped(lod):
    a = Acc()
    tiers = [  # (w, d, zshift, y0, y1)
        (38, 30, 0.0, 6, 20), (32, 24, -3.0, 20, 34), (26, 18, -6.0, 34, 48), (20, 13, -8.5, 48, 60)]
    base = chamfer(38, 30, 1.5)
    walls(a, base, 0, 6, "sky_shop")
    for ti, (w, d, zs, y0, y1) in enumerate(tiers):
        P = chamfer(w, d, 1.5, cz=zs)
        walls(a, P, y0, y1, "sky_glass_teal")
        cap(a, P, y1, "sky_concrete")
        if lod <= 1:
            band(a, P, y1 - 0.3, 0.8, 0.35, "sky_frame")
        if ti + 1 < len(tiers):
            nw, nd, nzs, _, _ = tiers[ti + 1]
            front_next = nzs + nd * 0.5
            front_this = zs + d * 0.5
            zc = (front_next + front_this) * 0.5 + 0.6
            if lod <= 1:
                planter_row(a, -w * 0.5 + 1.8, w * 0.5 - 1.8, y1, zc, 1.3, lod, seed=ti)
            if lod == 0:
                for k in range(3):
                    tree(a, -w * 0.3 + k * w * 0.3, y1, zc - 1.6, 4.2 + k * 0.5, sub=1, seed=ti * 3 + k)
    if lod <= 1:
        w, d, zs, _, y1 = tiers[-1]
        box(a, (0, y1 + 1.6, zs - 1.0), (8, 3.2, 5), "sky_metal")
        blob(a, (4.5, y1 + 1.1, zs + 3.0), 1.4, squash=(1.3, 0.7, 1.0), sub=1)
    return a


def midrise_a(lod):
    a = Acc()
    P = rect(42, 20)
    walls(a, P, 0, 5, "sky_shop")
    walls(a, P, 5, 30, "sky_ribbon")
    cap(a, P, 30, "sky_roof")
    if lod <= 1:
        band(a, P, 30, 1.2, 0.15, "sky_concrete")
        box(a, (0, 4.6, 11.4), (42.6, 0.4, 2.8), "sky_metal")     # street canopy
        box(a, (-6, 32.9, -2), (12, 3.4, 7), "sky_metal")         # plant room
    if lod == 0:
        for zf in (-10.0, 10.0):
            s = 1.0 if zf > 0 else -1.0
            for k in range(15):
                x = -21.0 + 1.5 + k * 2.83
                obox(a, (x, 5.2, zf + s * 0.45), (x, 30.0, zf + s * 0.45), 0.22, 0.9, "sky_steel",
                     up=(0.0, 0.0, 1.0))
        planter_row(a, -18, 18, 31.2, -6.5, 1.2, 0, seed=3)
    return a


def midrise_b(lod):
    a = Acc()
    seg = (6, 3, 2)[lod]
    P = rounded(30, 26, 8, seg)
    walls(a, P, 0, 5, "sky_shop", smooth=True)
    walls(a, P, 5, 41, "sky_ribbon", smooth=True)
    cap(a, P, 41, "sky_roof")
    if lod <= 1:
        band(a, P, 41, 1.0, 0.2, "sky_concrete", smooth=True)
        step = 1 if lod == 0 else 3
        for f in range(1, 10, step):
            band(a, P, 5 + f * 3.75 - 0.15, 0.3, 1.5, "sky_concrete", smooth=True)
        cap(a, scale_plan(P, 0.7), 41.05, "sky_garden", tile=(40.0, 40.0))
    if lod == 0:
        for k in range(5):
            ang = k / 5 * math.tau
            tree(a, math.cos(ang) * 6.5, 41.0, math.sin(ang) * 5.5, 3.8 + (k % 2), sub=1, seed=k * 2.1)
    return a


def podium_a(lod):
    a = Acc()
    P0 = rect(36, 18)
    P1 = rect(36, 13, cz=-2.5)
    walls(a, P0, 0, 5, "sky_shop")
    cap(a, P0, 5, "sky_concrete")
    walls(a, P1, 5, 9.5, "sky_ribbon")
    cap(a, P1, 9.5, "sky_roof")
    if lod <= 1:
        band(a, P1, 9.5, 0.8, 0.3, "sky_concrete")
        box(a, (0, 4.3, 9.9), (36.4, 0.3, 1.8), "sky_metal")      # street canopy
        planter_row(a, -16.5, 16.5, 5.0, 8.3, 0.9, lod, seed=1)    # terrace-edge planter
    if lod == 0:
        # Handrail along the terrace edge (above the planter line).
        obox(a, (-17.8, 6.1, 8.9), (17.8, 6.1, 8.9), 0.06, 0.06, "sky_steel")
        # Cafe terrace: coral / white parasols with tables.
        for k, x in enumerate((-11.0, -4.0, 3.0, 10.0)):
            slot = "sky_coral" if k % 2 == 0 else "sky_concrete"
            cyl(a, (x, 5.0, 5.2), (x, 7.4, 5.2), 0.05, 5, "sky_steel", caps=False)
            cyl(a, (x, 7.0, 5.2), (x, 7.8, 5.2), 1.7, 10, slot, taper=0.05)
            cyl(a, (x, 5.0, 5.2), (x, 5.75, 5.2), 0.55, 8, "sky_concrete")
        tree(a, 15.5, 9.5, -2.0, 4.0, sub=1)
        tree(a, -15.0, 9.5, -5.0, 3.5, sub=1, seed=4)
    return a


def podium_b(lod):
    a = Acc()
    steps = [(16.0, 0.0, 0, 5, "sky_shop"), (13.0, -1.5, 5, 9, "sky_ribbon"),
             (10.0, -3.0, 9, 13, "sky_ribbon")]
    for si, (d, zs, y0, y1, slot) in enumerate(steps):
        P = rect(28, d, cz=zs)
        walls(a, P, y0, y1, slot)
        cap(a, P, y1, "sky_concrete" if si < 2 else "sky_roof")
        if lod <= 1:
            band(a, P, y1 - 0.2, 0.5, 0.25, "sky_frame")
            if si < 2:
                nd, nzs = steps[si + 1][0], steps[si + 1][1]
                zc = (zs + d * 0.5 + nzs + nd * 0.5) * 0.5 + 0.2
                planter_row(a, -12.5, 12.5, y1, zc, 0.9, lod, seed=si * 4)
    if lod == 0:
        cap(a, rect(20, 6, cz=-3.5), 13.05, "sky_garden", tile=(40.0, 40.0))
        for k in range(3):
            tree(a, -7.0 + k * 7.0, 13.0, -4.0, 3.6, sub=1, seed=k * 1.9)
        box(a, (0, 4.3, 8.9), (28.4, 0.3, 1.8), "sky_metal")
    return a


# =================================================================== transit + plaza

GUIDE_LEN = 24.0
BEAM_H = 1.9


def guideway_beam(lod):
    """Straddle-monorail box girder, +Z from 0 to GUIDE_LEN, TOP of beam at y = 0."""
    a = Acc()
    sec = [(-1.3, 0.0), (1.3, 0.0), (1.1, -BEAM_H * 0.55), (0.8, -BEAM_H), (-0.8, -BEAM_H),
           (-1.1, -BEAM_H * 0.55)]
    for k in range(len(sec)):
        (x0, y0), (x1, y1) = sec[k], sec[(k + 1) % len(sec)]
        nx, ny = (y1 - y0), -(x1 - x0)
        # Section wound clockwise when viewed down +Z, so (dy, -dx) points OUT of the girder.
        pts = [(x0, y0, 0.0), (x1, y1, 0.0), (x1, y1, GUIDE_LEN), (x0, y0, GUIDE_LEN)]
        ln = math.hypot(x1 - x0, y1 - y0)
        a.poly(pts, [(0, 0), (ln * 0.25, 0), (ln * 0.25, GUIDE_LEN * 0.25), (0, GUIDE_LEN * 0.25)],
               "sky_concrete", want=(nx, ny, 0.0))
    if lod == 0:
        # Brushed-metal fairing strips on both sides of the running surface.
        for s in (-1.0, 1.0):
            obox(a, (s * 1.36, -0.28, 0.0), (s * 1.36, -0.28, GUIDE_LEN), 0.1, 0.34, "sky_steel")
    return a


def guideway_pier(lod):
    """Column + cap. Origin at GROUND, cap top at y = 10 (Unity scales Y to the deck)."""
    a = Acc()
    seg = (12, 8, 6)[lod]
    cyl(a, (0, 0, 0), (0, 8.6, 0), 0.85, seg, "sky_concrete", taper=0.82)
    obox(a, (0, 8.4, -1.0), (0, 8.4, 1.0), 1.6, 0.6, "sky_concrete")
    # Tapered T-cap under the beam.
    obox(a, (0, 9.4, -1.1), (0, 9.4, 1.1), 3.2, 1.2, "sky_concrete")
    return a


def _car_section(scale=1.0, ys=1.0, yoff=0.0):
    sec = [(1.50, -0.90), (1.56, 1.90), (1.40, 2.95), (0.90, 3.38), (-0.90, 3.38), (-1.40, 2.95),
           (-1.56, 1.90), (-1.50, -0.90)]
    return [(x * scale, (y - 1.2) * ys + 1.2 + yoff) for (x, y) in sec]


def _loft(a, rings, zs, slots):
    n = len(rings[0])
    for r in range(len(rings) - 1):
        for k in range(n - 1):
            p = [(rings[r][k][0], rings[r][k][1], zs[r]), (rings[r][k + 1][0], rings[r][k + 1][1], zs[r]),
                 (rings[r + 1][k + 1][0], rings[r + 1][k + 1][1], zs[r + 1]),
                 (rings[r + 1][k][0], rings[r + 1][k][1], zs[r + 1])]
            mx = (p[0][0] + p[1][0]) * 0.5
            my = (p[0][1] + p[1][1]) * 0.5 - 1.2
            a.poly(p, [(0, 0), (1, 0), (1, 1), (0, 1)], slots(r, k), want=(mx, my, 0.0), smooth=True)
    # Underside between the skirts stays open (it sits over the beam).


def _car_body(a, z0, z1, nose, lod):
    sec = _car_section()
    rings, zs = [sec, sec], [z0, z1 - (2.6 if nose else 0.0)]
    if nose:
        rings += [_car_section(0.93, 0.93, -0.05), _car_section(0.72, 0.78, -0.25),
                  _car_section(0.40, 0.55, -0.55)]
        zs += [z1 - 1.5, z1 - 0.55, z1]

    def slots(r, k):
        if nose and r >= 1 and k in (2, 3, 4):
            return "sky_glass_deep"                # wrap-around windscreen
        return "sky_train"
    _loft(a, rings, zs, slots)
    last = rings[-1]
    tip = [(x, y, zs[-1]) for (x, y) in last]
    a.poly(tip, [(0.5, 0.5)] * len(tip), "sky_train", want=(0, 0, 1))
    first = [(x, y, zs[0]) for (x, y) in rings[0]]
    a.poly(first, [(0.5, 0.5)] * len(first), "sky_dark", want=(0, 0, -1))
    # Window band + livery stripes, slightly proud of the flank.
    zw1 = zs[1] - 0.2
    for s in (-1.0, 1.0):
        obox(a, (s * 1.58, 2.35, z0 + 0.6), (s * 1.58, 2.35, zw1), 0.06, 0.85, "sky_glass_deep")
        obox(a, (s * 1.60, 1.35, z0 + 0.1), (s * 1.60, 1.35, zw1 + 1.0), 0.05, 0.45, "sky_livery")
        if lod == 0:
            obox(a, (s * 1.60, 0.98, z0 + 0.1), (s * 1.60, 0.98, zw1 + 1.0), 0.05, 0.12, "sky_coral")
    if lod == 0:
        box(a, (0, 3.45, (z0 + zs[1]) * 0.5), (1.6, 0.2, (zs[1] - z0) * 0.5), "sky_metal")  # roof pod


def monorail_head(lod):
    """Lead car, 14 m, nose at +Z, origin at centre on the beam top."""
    a = Acc()
    _car_body(a, -7.0, 7.0, True, lod)
    return a


def monorail_car(lod):
    a = Acc()
    _car_body(a, -6.5, 6.5, False, lod)
    box(a, (0, 1.5, 6.75), (2.2, 2.6, 0.5), "sky_dark")       # gangway bellows
    return a


def skybridge(lod):
    """Enclosed glass skybridge, +Z from 0 to 10 m nominal (Unity scales Z to the gap)."""
    a = Acc()
    L = 10.0
    obox(a, (0, 0.2, 0), (0, 0.2, L), 5.0, 0.6, "sky_concrete")          # floor slab
    obox(a, (0, 4.4, 0), (0, 4.4, L), 5.0, 0.5, "sky_concrete")          # roof slab
    for s in (-1.0, 1.0):
        x = s * 2.3
        pts = [(x, 0.5, 0.0), (x, 0.5, L), (x, 4.15, L), (x, 4.15, 0.0)]
        a.poly(pts, [(0, 0.5 / 15), (L / 6, 0.5 / 15), (L / 6, 4.15 / 15), (0, 4.15 / 15)],
               "sky_glass_teal", want=(s, 0, 0))
    if lod == 0:
        for s in (-1.0, 1.0):
            obox(a, (s * 2.45, 2.3, 0.0), (s * 2.45, 2.3, L), 0.08, 0.08, "sky_steel")
    return a


def ring_sculpture(lod):
    """Brushed-metal double loop on a white plinth (reference 7). ~8 m tall."""
    from mathutils import Matrix
    a = Acc()
    seg, rseg = ((40, 12), (24, 8), (14, 6))[lod]
    cyl(a, (0, 0, 0), (0, 0.55, 0), 2.6, 16, "sky_concrete", smooth=True)
    cyl(a, (0, 0.55, 0), (0, 0.8, 0), 2.1, 16, "sky_concrete", smooth=True)
    r1 = Matrix.Rotation(math.radians(90), 3, 'X') @ Matrix.Rotation(math.radians(0), 3, 'Y')
    r2 = Matrix.Rotation(math.radians(62), 3, 'Y') @ Matrix.Rotation(math.radians(78), 3, 'X')
    torus(a, (0.0, 4.2, 0.0), 3.3, 0.34, seg, rseg, "sky_steel", r1)
    torus(a, (0.6, 4.4, 0.2), 2.7, 0.30, seg, rseg, "sky_steel", r2)
    cyl(a, (0, 0.8, 0), (0, 1.1, 0), 0.5, 10, "sky_steel")
    return a


def planter(lod):
    """Plaza planter, 4.2 x 1.4 m, origin at the ground centre."""
    a = Acc()
    box(a, (0, 0.45, 0), (4.2, 0.9, 1.4), "sky_concrete")
    if lod == 0:
        for k, x in enumerate((-1.3, 0.0, 1.3)):
            blob(a, (x, 1.15, 0.0), 0.72 + 0.1 * (k % 2), squash=(1.2, 0.8, 0.95), sub=2, seed=k)
    else:
        blob(a, (0, 1.1, 0), 1.2, squash=(1.8, 0.7, 0.8), sub=0)
    return a


# =================================================================== export table

ASSETS = {
    "TowerSlimA": tower_slim_a, "TowerSlimB": tower_slim_b,
    "TowerRoundA": tower_round_a, "TowerRoundB": tower_round_b,
    "TowerStepped": tower_stepped,
    "MidriseA": midrise_a, "MidriseB": midrise_b,
    "PodiumA": podium_a, "PodiumB": podium_b,
    "GuidewayBeam": guideway_beam, "GuidewayPier": guideway_pier,
    "MonorailHead": monorail_head, "MonorailCar": monorail_car,
    "Skybridge": skybridge, "RingSculpture": ring_sculpture, "Planter": planter,
}
# Tiny props get LOD0 + LOD1 only.
TWO_LODS = {"GuidewayBeam", "GuidewayPier", "Planter", "Skybridge", "MonorailHead", "MonorailCar"}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    wanted = argv or list(ASSETS)
    report = []
    for key in wanted:
        S.reset_scene()
        objs = []
        tris = []
        for lod in range(2 if key in TWO_LODS else 3):
            acc = ASSETS[key](lod)
            o = acc.build(f"Minato_Skyline_{key}_LOD{lod}")
            o.data.calc_loop_triangles()
            tris.append(len(o.data.loop_triangles))
            objs.append(o)
        M.stat(objs[0])
        M.export_fbx(objs, f"Minato_Skyline_{key}.fbx", verbose=False)
        report.append((key, tris))
    print("[skyline] ---- triangle report (LOD0 / LOD1 / LOD2) ----")
    for key, tris in report:
        print(f"[skyline] {key:<14s} " + " / ".join(f"{t:,}" for t in tris))


if __name__ == "__main__":
    main()

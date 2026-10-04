"""
MAPLE CITY TOWERS - a small kit of distinctive modern skyscrapers for the Maple City skyline
(board task C7, Copilot, 2026-09-25).

The previous skyline reused Minato's extruded glass slabs, so the downtown read as the same slab
repeated. This kit gives the skyline real silhouettes - setbacks, tapers, a twin, an art-deco
crown, spires and masts, a podium with a lobby canopy, a rounded glass slab and a cylinder.

Two halves, one file:

  * TEXTURES (plain Python + PIL/numpy - Blender's Python has no PIL):
        python tools/blender/build_maple_city_towers.py --textures
    -> Assets/Environment/MapleCity/Skyline/Textures/MCT_*.png
       curtain-wall glass (5 tints) with a mullion grid, spandrels and a random lit-window mask
       baked in, plus two stone facades with punched (partly lit) windows.

  * MESHES (Blender):
        blender -b -P tools/blender/build_maple_city_towers.py [-- Name Name ...]
    -> Assets/Environment/MapleCity/Skyline/Meshes/MapleTower_<Name>.glb
    The Blender run also regenerates the textures by shelling out to system python (skip with
    `-- --no-textures`).

Conventions (sakura_lib): every vertex is authored in UNITY coordinates and passed through
S.u2b; origin at the base centre, +Z forward, real metres. Walls start at y = -6 so a tower can
be sunk onto uneven ground without floating. Faces are wound deterministically (each face is
checked against its intended outward normal in Blender space), so no global flips.

Material slots (Unity re-materialises by token in the slot name - MapleCitySkyline.cs):
    mct_glass   curtain wall (UVs in metres / TILE -> floors line up across tiers)
    mct_stone   stone / concrete spandrel facade (same UV grid)
    mct_frame   mullion / frame metal: fins, bands, parapets, skybridge, canopy
    mct_accent  crown / spire accent (bronze-gold)
    mct_roof    flat roofs
Mullions and lit windows are TEXTURE, not geometry, which keeps every variant far under the
6k-tri budget.
"""

import math
import os
import random
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SKY_DIR = os.path.join(REPO, "Assets", "Environment", "MapleCity", "Skyline")
MESH_DIR = os.path.join(SKY_DIR, "Meshes")
TEX_DIR = os.path.join(SKY_DIR, "Textures")

# One texture tile = 16 bays x 16 floors. PROVISIONAL: 3 m bay, 4 m floor-to-floor.
BAY_M, FLOOR_M, CELLS = 3.0, 4.0, 16
TILE_W, TILE_H = BAY_M * CELLS, FLOOR_M * CELLS   # 48 m x 64 m per texture repeat

try:
    import bpy  # noqa: F401
    IN_BLENDER = True
except ImportError:
    IN_BLENDER = False


# =============================================================================== TEXTURES

def build_textures():
    import numpy as np
    from PIL import Image, ImageFilter

    os.makedirs(TEX_DIR, exist_ok=True)
    RES = 1024
    C = RES // CELLS                      # 64 px per bay / floor cell

    def tile_noise(seed, freqs=((1, 0), (0, 1), (1, 1), (2, 1), (1, 2), (3, 2)), amp=1.0):
        """Periodic low-frequency field in [-1, 1] - integer frequencies keep it seamless."""
        rng = np.random.default_rng(seed)
        u = np.linspace(0, 1, RES, endpoint=False)
        U, V = np.meshgrid(u, u)
        f = np.zeros((RES, RES))
        tot = 0.0
        for kx, ky in freqs:
            a = 1.0 / math.hypot(kx, ky)
            f += a * np.sin(2 * math.pi * (kx * U + ky * V) + rng.uniform(0, 6.3))
            tot += a
        return f / tot * amp

    def cell_rand(seed):
        return np.random.default_rng(seed).random((CELLS, CELLS))

    def expand(cells):
        return np.kron(cells, np.ones((C, C)))

    # Row 0 of the image is the TOP of the tile. Within a cell (floor) py 0 = ceiling.
    py = np.arange(RES) % C
    px = np.arange(RES) % C
    PY = np.repeat(py[:, None], RES, axis=1)
    PX = np.repeat(px[None, :], RES, axis=0)

    def lit_mask(seed, rate):
        """Floors light unevenly (whole office floors on, others dark) - reads as occupancy."""
        rng = np.random.default_rng(seed)
        floor_rate = rng.random(CELLS) ** 1.6 * rate * 2.4          # per-floor occupancy
        cells = rng.random((CELLS, CELLS)) < floor_rate[:, None]
        # runs: a lit cell tends to light its neighbour (open-plan floors)
        run = np.roll(cells, 1, axis=1) & (rng.random((CELLS, CELLS)) < 0.55)
        cells = cells | run
        warm = rng.random((CELLS, CELLS)) < 0.85                   # warm vs cool-white lamps
        level = 0.88 + 0.12 * rng.random((CELLS, CELLS))
        return cells, warm, level

    def glass(name, panel, refl, spandrel, mullion, seed, lit_rate=0.075):
        panel, refl, spandrel, mullion = (np.array(c, dtype=np.float64) / 255.0
                                          for c in (panel, refl, spandrel, mullion))
        # Sky reflection: large soft clouds + per-pane jitter + slight top-darkening per pane.
        cloud = tile_noise(seed, amp=1.0) * 0.5 + 0.5
        cloud = cloud ** 1.4
        jitter = expand(cell_rand(seed + 1) * 2 - 1)[..., None]
        # half-bay panes get their own jitter
        pane_j = np.kron(np.random.default_rng(seed + 2).random((CELLS, CELLS * 2)) * 2 - 1,
                         np.ones((C, C // 2)))[..., None]
        pane_grad = (PY / C)[..., None]                               # 0 top .. 1 bottom
        col = panel + (refl - panel) * (cloud[..., None] * 0.85 + 0.15 * (1 - pane_grad))
        col = col * (1 + 0.06 * jitter + 0.05 * pane_j)

        # lit windows (interior visible through the glass)
        cells, warm, level = lit_mask(seed + 3, lit_rate)
        L = expand(cells.astype(float))[..., None]
        W = expand(warm.astype(float))[..., None]
        LV = expand(level)[..., None]
        warm_c = np.array([1.00, 0.82, 0.50])
        cool_c = np.array([0.96, 0.93, 0.80])
        interior = (W * warm_c + (1 - W) * cool_c) * LV
        interior = interior * (0.86 + 0.14 * pane_grad)               # floor glow brighter low
        glass_zone = (PY < C * 0.80)[..., None]
        col = np.where((L > 0.5) & glass_zone, col * 0.08 + interior, col)

        # spandrel (bottom ~0.8 m of each floor, opaque shadow-box)
        sp = (PY >= int(C * 0.80))[..., None]
        col = np.where(sp, spandrel * (1 + 0.04 * jitter), col)
        # mullions: bay edge 3 px, half-bay 2 px, transoms at the spandrel line + floor line
        mull = ((PX < 3) | (np.abs(PX - C // 2) < 1) | (PY < 2) |
                (np.abs(PY - int(C * 0.80)) < 1))[..., None]
        col = np.where(mull, mullion, col)

        img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8))
        img.save(os.path.join(TEX_DIR, f"MCT_Glass_{name}.png"))
        print(f"[mct] texture MCT_Glass_{name}.png")

    def stone(name, base, pier, window, refl, seed, lit_rate=0.09):
        base, pier, window, refl = (np.array(c, dtype=np.float64) / 255.0
                                    for c in (base, pier, window, refl))
        rng = np.random.default_rng(seed)
        grain = rng.random((RES, RES))
        grain = np.array(Image.fromarray((grain * 255).astype(np.uint8)).filter(
            ImageFilter.GaussianBlur(1.2))) / 255.0
        n = tile_noise(seed, amp=1.0)[..., None]
        col = base * (1 + 0.05 * n + 0.10 * (grain[..., None] - 0.5))
        # vertical piers every bay (a touch darker), horizontal ledge at each floor line
        piers = ((PX < 10) | (PX >= C - 10))[..., None]
        col = np.where(piers, pier * (1 + 0.08 * (grain[..., None] - 0.5)), col)
        ledge = (PY < 3)[..., None]
        col = np.where(ledge, np.minimum(base * 1.12, 1), col)
        # punched window: inset in each cell
        win = ((PX >= 14) & (PX < C - 14) & (PY >= 10) & (PY < C - 16))[..., None]
        cloud = (tile_noise(seed + 9, amp=1.0) * 0.5 + 0.5)[..., None]
        wcol = window + (refl - window) * cloud * 0.8
        cells, warm, level = lit_mask(seed + 5, lit_rate)
        L = expand(cells.astype(float))[..., None]
        W = expand(warm.astype(float))[..., None]
        LV = expand(level)[..., None]
        interior = (W * np.array([1.0, 0.80, 0.50]) + (1 - W) * np.array([0.93, 0.94, 0.86])) * LV
        wcol = np.where(L > 0.5, interior * 0.9, wcol)
        # window frame 2 px
        wframe = win & ~((PX >= 16) & (PX < C - 16) & (PY >= 12) & (PY < C - 18))[..., None]
        col = np.where(win, wcol, col)
        col = np.where(wframe, pier * 0.7, col)
        # sill shadow under each window
        sill = ((PX >= 14) & (PX < C - 14) & (PY >= C - 16) & (PY < C - 13))[..., None]
        col = np.where(sill, col * 0.72, col)
        img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8))
        img.save(os.path.join(TEX_DIR, f"MCT_Stone_{name}.png"))
        print(f"[mct] texture MCT_Stone_{name}.png")

    # PROVISIONAL palette. Glass is baked per tint (lit windows must stay warm - a material
    # colour multiply would turn them grey-green on blue glass).
    #          name       panel            sky reflection    spandrel          mullion        seed
    glass("Blue",   (38, 70, 110),  (122, 168, 208), (34, 50, 72),   (186, 194, 204), 101)
    glass("Green",  (34, 78, 76),   (112, 168, 158), (30, 54, 52),   (180, 190, 186), 202)
    glass("Bronze", (78, 58, 42),   (170, 142, 110), (52, 40, 32),   (66, 52, 40),    303)
    glass("Silver", (78, 90, 104),  (164, 178, 194), (64, 70, 80),   (212, 216, 222), 404)
    glass("Deep",   (30, 42, 62),   (110, 136, 172), (26, 32, 44),   (58, 64, 74),    505)
    #          name         base             pier             window        reflection   seed
    stone("Limestone", (214, 196, 166), (196, 176, 146), (44, 54, 66), (130, 160, 190), 606)
    stone("Granite",   (158, 150, 144), (132, 126, 122), (38, 46, 58), (120, 148, 178), 707)


# =============================================================================== MESHES

def mesh_main():
    sys.path.insert(0, HERE)
    import bpy
    import bmesh
    import sakura_lib as S
    from mathutils import Vector

    SLOTS = ["mct_glass", "mct_stone", "mct_frame", "mct_accent", "mct_roof"]
    SLOT_COL = {
        "mct_glass": (0.25, 0.40, 0.55, 1), "mct_stone": (0.80, 0.74, 0.64, 1),
        "mct_frame": (0.70, 0.72, 0.74, 1), "mct_accent": (0.78, 0.60, 0.32, 1),
        "mct_roof": (0.30, 0.31, 0.33, 1),
    }

    def newell(ps):
        n = Vector((0.0, 0.0, 0.0))
        for k in range(len(ps)):
            a, b = ps[k], ps[(k + 1) % len(ps)]
            n.x += (a.y - b.y) * (a.z + b.z)
            n.y += (a.z - b.z) * (a.x + b.x)
            n.z += (a.x - b.x) * (a.y + b.y)
        return n

    class Acc:
        def __init__(self):
            self.v, self.faces, self.uv, self.mat, self.smooth = [], [], [], [], []

        def vert(self, p):
            self.v.append(S.u2b(*p))
            return len(self.v) - 1

        def face(self, idx, uvs, mat, outward, smooth=False):
            """idx: vertex indices; outward: intended normal in UNITY space."""
            ps = [Vector(self.v[i]) for i in idx]
            want = Vector(S.u2b(*outward))
            if newell(ps).dot(want) < 0:
                idx = list(reversed(idx))
                uvs = list(reversed(uvs))
            self.faces.append(tuple(idx))
            self.uv.extend(uvs)
            self.mat.append(SLOTS.index(mat))
            self.smooth.append(smooth)

        def poly(self, pts, uvs, mat, outward, smooth=False):
            self.face([self.vert(p) for p in pts], uvs, mat, outward, smooth)

        def build(self, name):
            used = sorted(set(self.mat))
            remap = {s: k for k, s in enumerate(used)}
            obj = S.mesh_from_arrays(name, self.v, self.faces, uvs=self.uv, smooth=False)
            for s in used:
                m = bpy.data.materials.get(SLOTS[s]) or S.pbr_material(
                    SLOTS[s], base_color=SLOT_COL[SLOTS[s]], roughness=0.5)
                obj.data.materials.append(m)
            assert len(obj.data.polygons) == len(self.mat), f"{name}: validate dropped faces"
            obj.data.polygons.foreach_set("material_index", [remap[m] for m in self.mat])
            obj.data.polygons.foreach_set("use_smooth", self.smooth)
            # glTF tangents need tris/quads: triangulate the n-gon caps here.
            bm = bmesh.new()
            bm.from_mesh(obj.data)
            bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
            bm.to_mesh(obj.data)
            bm.free()
            obj.data.update()
            return obj

    # ------------------------------------------------------------------ footprints (x, z)
    def rect(w, d, cx=0.0, cz=0.0):
        hw, hd = w / 2, d / 2
        return [(cx - hw, cz - hd), (cx + hw, cz - hd), (cx + hw, cz + hd), (cx - hw, cz + hd)]

    def chamfer(w, d, c, cx=0.0, cz=0.0):
        hw, hd = w / 2, d / 2
        return [(cx - hw + c, cz - hd), (cx + hw - c, cz - hd), (cx + hw, cz - hd + c),
                (cx + hw, cz + hd - c), (cx + hw - c, cz + hd), (cx - hw + c, cz + hd),
                (cx - hw, cz + hd - c), (cx - hw, cz - hd + c)]

    def rounded(w, d, r, n=5, cx=0.0, cz=0.0):
        hw, hd = w / 2 - r, d / 2 - r
        pts = []
        for (ox, oz, a0) in ((hw, -hd, -90), (hw, hd, 0), (-hw, hd, 90), (-hw, -hd, 180)):
            for k in range(n + 1):
                a = math.radians(a0 + 90 * k / n)
                pts.append((cx + ox + r * math.cos(a), cz + oz + r * math.sin(a)))
        return pts

    def circle(r, n=32, cx=0.0, cz=0.0):
        return [(cx + r * math.cos(2 * math.pi * k / n), cz + r * math.sin(2 * math.pi * k / n))
                for k in range(n)]

    def grow(fp, g):
        cx = sum(p[0] for p in fp) / len(fp)
        cz = sum(p[1] for p in fp) / len(fp)
        out = []
        for x, z in fp:
            dx, dz = x - cx, z - cz
            L = math.hypot(dx, dz) or 1.0
            out.append((x + dx / L * g, z + dz / L * g))
        return out

    def centroid(fp):
        return (sum(p[0] for p in fp) / len(fp), sum(p[1] for p in fp) / len(fp))

    # ------------------------------------------------------------------ primitives
    def loft(a, fp0, y0, fp1, y1, mat, top="mct_roof", uoff=0.0, smooth=False, ytop=None,
             cap=True):
        """Walls between two footprints of equal vertex count; optional flat/sloped cap."""
        n = len(fp0)
        cx, cz = centroid(fp0)
        yt = [ytop(x, z) if ytop else y1 for (x, z) in fp1]
        per = [0.0]
        for k in range(n):
            (x0, z0), (x1_, z1_) = fp0[k], fp0[(k + 1) % n]
            per.append(per[-1] + math.hypot(x1_ - x0, z1_ - z0))
        if smooth:
            bot = [a.vert((x, y0, z)) for (x, z) in fp0]
            tp = [a.vert((x, yt[k], z)) for k, (x, z) in enumerate(fp1)]
        for k in range(n):
            j = (k + 1) % n
            u0, u1 = uoff + per[k] / TILE_W, uoff + per[k + 1] / TILE_W
            (ax, az), (bx, bz) = fp0[k], fp0[j]
            (cx1, cz1), (dx1, dz1) = fp1[j], fp1[k]
            mx, mz = (ax + bx) / 2 - cx, (az + bz) / 2 - cz
            ex, ez = bx - ax, bz - az
            nx, nz = ez, -ex
            if nx * mx + nz * mz < 0:
                nx, nz = -nx, -nz
            out = (nx, 0.0, nz)
            uvs = [(u0, y0 / TILE_H), (u1, y0 / TILE_H), (u1, yt[j] / TILE_H), (u0, yt[k] / TILE_H)]
            if smooth:
                a.face([bot[k], bot[j], tp[j], tp[k]], uvs, mat, out, smooth=True)
            else:
                a.poly([(ax, y0, az), (bx, y0, bz), (cx1, yt[j], cz1), (dx1, yt[k], dz1)],
                       uvs, mat, out)
        if cap and top:
            pts = [(x, yt[k], z) for k, (x, z) in enumerate(fp1)]
            # sloped caps: outward = mostly up
            a.poly(pts, [(p[0] / TILE_W, p[2] / TILE_W) for p in pts], top, (0.0, 1.0, 0.0))

    def prism(a, fp, y0, y1, mat, top="mct_roof", **kw):
        loft(a, fp, y0, fp, y1, mat, top, **kw)

    def pyramid(a, fp, y0, apex_y, mat, apex=None):
        cx, cz = apex if apex else centroid(fp)
        n = len(fp)
        for k in range(n):
            (ax, az), (bx, bz) = fp[k], fp[(k + 1) % n]
            mx, mz = (ax + bx) / 2 - cx, (az + bz) / 2 - cz
            a.poly([(ax, y0, az), (bx, y0, bz), (cx, apex_y, cz)],
                   [(0, 0), (1, 0), (0.5, 1)], mat, (mx, 0.4 * math.hypot(mx, mz), mz))

    def spire(a, x, z, y0, h, r, mat="mct_accent", n=8):
        fp = circle(r, n, x, z)
        pyramid(a, fp, y0, y0 + h, mat, apex=(x, z))

    def mast(a, x, z, y0, h, r, mat="mct_frame", n=6):
        prism(a, circle(r, n, x, z), y0, y0 + h, mat, top=mat)

    def box(a, cx, cz, w, d, y0, y1, mat, top=None):
        prism(a, rect(w, d, cx, cz), y0, y1, mat, top=top or mat)

    def obox(a, px, pz, tx, tz, w, dep, y0, y1, mat):
        """Box centred on (px,pz) with its width along (tx,tz) and depth along the normal."""
        nx, nz = tz, -tx
        hw, hd = w / 2, dep / 2
        fp = [(px - tx * hw - nx * hd, pz - tz * hw - nz * hd),
              (px + tx * hw - nx * hd, pz + tz * hw - nz * hd),
              (px + tx * hw + nx * hd, pz + tz * hw + nz * hd),
              (px - tx * hw + nx * hd, pz - tz * hw + nz * hd)]
        prism(a, fp, y0, y1, mat, top=mat)

    def fins(a, fp, y0, y1, spacing, w, dep, mat="mct_frame", skip_short=4.0, max_len=1e9):
        """Vertical fins standing proud of each straight edge of a footprint."""
        n = len(fp)
        cx, cz = centroid(fp)
        for k in range(n):
            (ax, az), (bx, bz) = fp[k], fp[(k + 1) % n]
            L = math.hypot(bx - ax, bz - az)
            if L < skip_short or L > max_len:
                continue
            tx, tz = (bx - ax) / L, (bz - az) / L
            nx, nz = tz, -tx
            mx, mz = (ax + bx) / 2 - cx, (az + bz) / 2 - cz
            if nx * mx + nz * mz < 0:
                nx, nz = -nx, -nz
            cnt = max(1, int(L / spacing))
            for i in range(1, cnt):
                s = i * L / cnt
                px, pz = ax + tx * s + nx * dep * 0.5, az + tz * s + nz * dep * 0.5
                obox(a, px, pz, tx, tz, w, dep, y0, y1, mat)

    def band(a, fp, y, h, g=0.45, mat="mct_frame"):
        prism(a, grow(fp, g), y, y + h, mat, top=mat)

    # ------------------------------------------------------------------ the kit
    def setback_a(a, rnd):
        """Three-tier stepped setback tower with a stone plinth, penthouse and mast (~215 m)."""
        prism(a, chamfer(44, 44, 3), -6, 9, "mct_stone")
        t1, t2, t3 = chamfer(40, 40, 3), chamfer(32, 32, 3), chamfer(24, 24, 2.5)
        prism(a, t1, 9, 92, "mct_glass", uoff=rnd.random())
        band(a, t1, 90, 3.2, 0.5)
        prism(a, t2, 93, 142, "mct_glass", uoff=rnd.random())
        band(a, t2, 140, 3.0, 0.5)
        prism(a, t3, 143, 176, "mct_glass", uoff=rnd.random())
        band(a, t3, 176, 3.5, 0.35)
        box(a, 0, 0, 12, 12, 179, 186, "mct_frame")
        mast(a, 0, 0, 186, 30, 0.7)

    def setback_b(a, rnd):
        """Tall asymmetric setback tower with a stepped accent crown and spire (~262 m)."""
        prism(a, rect(48, 38), -6, 12, "mct_stone")
        t1, t2, t3 = rect(44, 34), rect(36, 28, -3, 1), rect(26, 22, 2, -1)
        prism(a, t1, 12, 112, "mct_glass", uoff=rnd.random())
        fins(a, t1, 12, 112, 4.0, 0.35, 0.7)
        band(a, t1, 110, 3, 0.4)
        prism(a, t2, 113, 172, "mct_glass", uoff=rnd.random())
        band(a, t2, 170, 3, 0.4)
        prism(a, t3, 173, 214, "mct_glass", uoff=rnd.random())
        prism(a, rect(19, 16, 2, -1), 214, 223, "mct_accent", top="mct_accent")
        prism(a, rect(12, 10, 2, -1), 223, 230, "mct_accent", top="mct_accent")
        spire(a, 2, -1, 230, 32, 1.4)

    def tapered(a, rnd):
        """Chamfered tower tapering 38 -> 24 m, glass pyramid crown, spire (~250 m)."""
        prism(a, chamfer(46, 46, 7), -6, 8, "mct_stone")
        f0, f1 = chamfer(38, 38, 6), chamfer(24, 24, 4)
        # two loft segments so the taper reads as gentle entasis rather than a cone
        fm = chamfer(33, 33, 5.3)
        loft(a, f0, 8, fm, 110, "mct_glass", top=None, uoff=rnd.random())
        loft(a, fm, 110, f1, 200, "mct_glass", top=None, uoff=rnd.random())
        band(a, f1, 199, 2.5, 0.35)
        pyramid(a, f1, 201.5, 224, "mct_glass")
        spire(a, 0, 0, 222, 30, 1.0)

    def twin(a, rnd):
        """Twin towers on a shared podium joined by a skybridge (~180 m)."""
        prism(a, rect(78, 36), -6, 20, "mct_stone")
        band(a, rect(78, 36), 20, 1.2, 0.3)
        for s in (-1, 1):
            fp = chamfer(26, 26, 2.5, s * 21, 0)
            prism(a, fp, 21, 150, "mct_glass", uoff=rnd.random())
            band(a, fp, 62, 1.5, 0.4)
            band(a, fp, 110, 1.5, 0.4)
            band(a, fp, 150, 4, 0.3)
            pyramid(a, chamfer(22, 22, 2, s * 21, 0), 154, 164, "mct_accent")
            mast(a, s * 21, 0, 162, 18, 0.5)
        prism(a, rect(18, 9, 0, 0), 96, 104, "mct_glass", uoff=rnd.random())
        band(a, rect(18, 9), 95, 1.2, 0.3)
        band(a, rect(18, 9), 104, 1.0, 0.3)

    def art_deco(a, rnd):
        """Stone art-deco tower: setbacks, corner piers, gold ziggurat crown, spire (~200 m)."""
        shaft = chamfer(34, 34, 4)
        prism(a, shaft, -6, 110, "mct_stone", uoff=rnd.random())
        for sx in (-1, 1):
            for sz in (-1, 1):
                box(a, sx * 15.5, sz * 15.5, 4.5, 4.5, -6, 114, "mct_stone")
        fins(a, shaft, 8, 110, 6.0, 0.9, 0.8, mat="mct_stone", skip_short=8)
        t2, t3 = chamfer(27, 27, 3), chamfer(21, 21, 2)
        prism(a, t2, 110, 135, "mct_stone", uoff=rnd.random())
        band(a, t2, 134, 1.4, 0.4, "mct_accent")
        prism(a, t3, 135, 150, "mct_stone", uoff=rnd.random())
        band(a, t3, 149, 1.4, 0.4, "mct_accent")
        c1, c2 = chamfer(16, 16, 2), chamfer(11, 11, 1.4)
        prism(a, c1, 150.4, 158, "mct_accent", top="mct_accent")
        fins(a, c1, 150.4, 160, 2.6, 0.5, 0.6, mat="mct_accent", skip_short=6)
        prism(a, c2, 158, 164, "mct_accent", top="mct_accent")
        pyramid(a, c2, 164, 172, "mct_accent")
        spire(a, 0, 0, 170, 30, 0.9)

    def cylinder(a, rnd):
        """Round glass tower with frame rings, a setback drum and a mast (~235 m)."""
        prism(a, circle(22, 32), -6, 10, "mct_stone", smooth=True)
        c = circle(18, 32)
        prism(a, c, 10, 186, "mct_glass", uoff=rnd.random(), smooth=True, top="mct_roof")
        for y in (58, 122):
            prism(a, circle(18.5, 32), y, y + 1.4, "mct_frame", top="mct_frame", smooth=True)
        prism(a, circle(18.6, 32), 185, 188, "mct_frame", top="mct_roof", smooth=True)
        prism(a, circle(13, 32), 188, 200, "mct_glass", uoff=rnd.random(), smooth=True)
        prism(a, circle(13.4, 32), 200, 203, "mct_frame", top="mct_roof", smooth=True)
        mast(a, 0, 0, 203, 32, 0.8)

    def rounded_slab(a, rnd):
        """Rounded-corner glass slab with a sloped roof and a stone plinth (~140 m)."""
        prism(a, rounded(54, 36, 12, 5), -6, 8, "mct_stone", smooth=True)
        fp = rounded(48, 30, 11, 5)
        prism(a, fp, 8, 130, "mct_glass", uoff=rnd.random(), smooth=True, top=None, cap=False)
        loft(a, fp, 130, fp, 130, "mct_glass", top="mct_frame", smooth=True,
             ytop=lambda x, z: 130 + 7 + 0.42 * z)
        fins(a, rect(24, 30.4), 8, 126, 3.0, 0.3, 0.6, skip_short=10, max_len=25)

    def podium_tower(a, rnd):
        """Stone podium with a glass lobby and front canopy; finned slab tower (~124 m)."""
        # recessed glass lobby under an overhanging stone podium
        prism(a, rect(60, 48), -6, 7.5, "mct_glass", uoff=rnd.random())
        prism(a, rect(66, 54), 7.5, 17, "mct_stone")
        band(a, rect(66, 54), 17, 1.0, 0.25)
        # lobby canopy on the +Z front, on two slim columns
        box(a, 0, 29.5, 26, 11, 6.2, 7.0, "mct_frame")
        for x in (-11, 11):
            mast(a, x, 33.5, -1, 7.2, 0.35)
        tw = rect(40, 24)
        prism(a, tw, 17, 118, "mct_glass", uoff=rnd.random())
        fins(a, tw, 17, 121, 4.0, 0.35, 0.8, skip_short=30)
        for s in (-1, 1):
            box(a, s * 21, 0, 3, 26, 17, 124, "mct_stone")
        box(a, 0, 0, 16, 10, 118, 124, "mct_frame")

    def midrise(a, rnd):
        """Stone office block with a glass setback top and rooftop plant (~62 m)."""
        fp = chamfer(50, 36, 2)
        prism(a, fp, -6, 44, "mct_stone", uoff=rnd.random())
        band(a, fp, 44, 1.2, 0.4)
        g = chamfer(44, 30, 2)
        prism(a, g, 45, 58, "mct_glass", uoff=rnd.random())
        band(a, g, 58, 1.4, 0.3)
        box(a, -8, 2, 12, 9, 59, 63, "mct_frame")
        box(a, 10, -4, 7, 7, 59, 62, "mct_frame")

    KIT = {
        "SetbackA": setback_a, "SetbackB": setback_b, "Tapered": tapered, "Twin": twin,
        "ArtDeco": art_deco, "Cylinder": cylinder, "Rounded": rounded_slab,
        "Podium": podium_tower, "Midrise": midrise,
    }

    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    no_tex = "--no-textures" in argv
    wanted = [k for k in argv if not k.startswith("--")] or list(KIT)

    if not no_tex:
        r = subprocess.run(f'python "{os.path.abspath(__file__)}" --textures', shell=True)
        if r.returncode != 0:
            print("[mct] WARNING: texture build failed (need system python with PIL + numpy)")

    os.makedirs(MESH_DIR, exist_ok=True)
    S.blender_assets_dir = lambda: MESH_DIR       # export_glb writes to the Skyline kit dir
    rows = []
    for key in wanted:
        S.reset_scene()
        acc = Acc()
        KIT[key](acc, random.Random(sum(ord(ch) * (k + 1) for k, ch in enumerate(key))))
        obj = acc.build(f"MapleTower_{key}")
        obj.data.calc_loop_triangles()
        tris = len(obj.data.loop_triangles)
        ys = [v.co.z for v in obj.data.vertices]
        S.export_glb([obj], f"MapleTower_{key}.glb")
        rows.append((key, tris, max(ys), len(obj.data.materials)))
    print("[mct] ---- kit report ----")
    for key, tris, h, nm in rows:
        flag = "" if tris <= 6000 else "  OVER BUDGET"
        print(f"[mct] {key:<10s} {tris:6,d} tris  top {h:6.1f} m  {nm} slots{flag}")


if __name__ == "__main__":
    if IN_BLENDER:
        mesh_main()
    else:
        build_textures()

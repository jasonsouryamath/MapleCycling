"""
SHUNTA METRO street props - shared Blender 4.5 helpers.

Conventions: authored in Blender coordinates (metres, Z up). The FRONT of every prop faces -Y (this is
Unity +Z after the FBX export with forward=-Z, up=Y, the same settings the Minato/Nagisa kits use).
Pivot = ground contact point at the prop's origin. Roadside props face the road with their front.
Materials are named slots 'SM_*'; materials.json (written next to the FBX files) carries the PBR
parameters the Unity importer (Assets/Editor/ShuntaMetroProps.cs) turns into HDRP/Lit materials.
"""
import bpy, bmesh, math, os, json, random
import numpy as np
from mathutils import Vector, Matrix, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Models", "ShuntaMetro", "Props")
TEXD = os.path.join(OUT, "Textures")
os.makedirs(TEXD, exist_ok=True)
FONT_CJK = r"C:\Windows\Fonts\YuGothB.ttc"
_font = None


def font():
    global _font
    if _font is None:
        _font = bpy.data.fonts.load(FONT_CJK)
    return _font


# ----------------------------------------------------------------------------- textures (numpy)
def _noise(n, g, rng):
    a = rng.random((g, g))
    x = np.linspace(0, g, n, endpoint=False)
    i0 = np.floor(x).astype(int); f = x - i0; f = f * f * (3 - 2 * f); i1 = (i0 + 1) % g
    rows = a[i0] * (1 - f)[:, None] + a[i1] * f[:, None]
    return rows[:, i0] * (1 - f)[None, :] + rows[:, i1] * f[None, :]


def fbm(n, base=4, octs=4, seed=0):
    rng = np.random.default_rng(seed)
    out = np.zeros((n, n)); amp = 1.0; tot = 0
    for o in range(octs):
        out += amp * _noise(n, base * 2 ** o, rng); tot += amp; amp *= 0.5
    return out / tot


def save_png(name, arr):
    """arr: HxWx3/4 float 0..1, row 0 = bottom (Blender image origin)."""
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1))], axis=2)
    path = os.path.join(TEXD, name)
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)
    return path


def normal_from_height(hm, strength=2.0):
    gx = (np.roll(hm, -1, 1) - np.roll(hm, 1, 1)) * strength
    gy = (np.roll(hm, -1, 0) - np.roll(hm, 1, 0)) * strength
    nz = np.ones_like(hm); l = np.sqrt(gx * gx + gy * gy + 1)
    return np.stack([(-gx / l) * 0.5 + 0.5, (-gy / l) * 0.5 + 0.5, nz / l * 0.5 + 0.5], axis=2)


def make_textures():
    n = 256
    # concrete
    f = fbm(n, 4, 5, 1); sp = np.random.default_rng(2).random((n, n))
    v = 0.50 + 0.28 * (f - 0.5) + 0.05 * (sp - 0.5)
    stain = np.clip((fbm(n, 2, 3, 3) - 0.55) * 2.0, 0, 1)
    v = v * (1 - 0.35 * stain)
    save_png("sm_concrete.png", np.stack([v, v * 1.0, v * 0.97], 2))
    save_png("sm_concrete_n.png", normal_from_height(f + 0.2 * sp, 3.0))
    # stone w/ moss
    f = fbm(n, 6, 5, 4); m = np.clip((fbm(n, 3, 4, 5) - 0.5) * 3, 0, 1)
    g = 0.42 + 0.3 * (f - 0.5)
    save_png("sm_stone.png", np.stack([g * (1 - 0.5 * m) + 0.02, g * (1 - 0.1 * m) + 0.05 * m, g * (1 - 0.6 * m)], 2))
    save_png("sm_stone_n.png", normal_from_height(f, 4.0))
    # torii red weathered paint with vertical wood streaks
    rng = np.random.default_rng(6)
    streak = _noise(n, 3, rng)[:, :1] * 0 + fbm(n, 2, 3, 7)
    xs = np.tile(_noise(n, 24, rng)[0:1, :], (n, 1))
    wear = np.clip((fbm(n, 4, 4, 8) - 0.52) * 3, 0, 1)
    red = np.array([0.55, 0.07, 0.04]); wood = np.array([0.20, 0.11, 0.07])
    base = red[None, None, :] * (0.75 + 0.35 * xs[:, :, None] * 0.6 + 0.2 * streak[:, :, None])
    img = base * (1 - wear[:, :, None]) + wood[None, None, :] * (0.7 + 0.5 * xs[:, :, None]) * wear[:, :, None]
    save_png("sm_torii.png", img)
    save_png("sm_torii_n.png", normal_from_height(xs * 0.6 + fbm(n, 6, 4, 9) * 0.4, 2.0))
    # bark
    xs = np.tile(_noise(n, 16, rng)[0:1, :], (n, 1)); f = fbm(n, 4, 5, 10)
    b = 0.12 + 0.18 * xs * (0.5 + f)
    save_png("sm_bark.png", np.stack([b * 1.1, b * 0.85, b * 0.7], 2))
    save_png("sm_bark_n.png", normal_from_height(xs + 0.3 * f, 5.0))
    # wood weathered
    xs = np.tile(_noise(n, 12, rng)[0:1, :], (n, 1)) * 0.7 + fbm(n, 3, 4, 11) * 0.3
    w = 0.25 + 0.25 * xs
    save_png("sm_wood.png", np.stack([w * 1.1, w * 0.82, w * 0.6], 2))
    # cloth indigo weave
    yy, xx = np.mgrid[0:n, 0:n]
    wv = 0.5 + 0.5 * np.sin(xx * 0.9) * np.sin(yy * 0.9)
    c = 0.8 + 0.2 * wv + 0.1 * fbm(n, 8, 3, 12)
    save_png("sm_cloth.png", np.stack([0.5 * c * 0.4, 0.55 * c * 0.45, 1.0 * c * 0.5], 2))
    # chochin: paper with horizontal bamboo rib lines (v runs bottom->top)
    r = 0.9 + 0.1 * fbm(n, 12, 3, 13)
    ribs = np.where((yy % 16) < 2, 0.35, 1.0)
    pap = np.stack([r * ribs, r * ribs * 0.18, r * ribs * 0.10], 2)
    save_png("sm_chochin.png", pap)
    # manhole
    N = 512; yy, xx = np.mgrid[0:N, 0:N]; cx = (xx + 0.5) / N - 0.5; cy = (yy + 0.5) / N - 0.5
    rr = np.sqrt(cx * cx + cy * cy)
    base = 0.17 + 0.08 * fbm(N, 6, 5, 14)
    grid = (((cx * 16) % 1) < 0.14) | (((cy * 16) % 1) < 0.14)
    h = np.where(rr < 0.36, np.where(grid, 0.25, 0.8), 0.5)
    h = np.where((np.abs(rr - 0.40) < 0.012) | (np.abs(rr - 0.435) < 0.01), 0.1, h)
    ring = np.where(rr > 0.47, 0.06, 1.0)
    rust = np.clip(fbm(N, 5, 4, 15) * 1.3 - 0.3, 0, 1)
    col = base * (0.55 + 0.7 * h) * ring
    save_png("sm_manhole.png", np.stack([col * (1 + 0.5 * rust), col * (1 + 0.1 * rust) * 0.95, col * 0.9], 2))
    save_png("sm_manhole_n.png", normal_from_height(h + 0.1 * fbm(N, 20, 3, 16), 4.0))
    # blossom card (alpha cutout)
    N = 256; rng = np.random.default_rng(21); yy, xx = np.mgrid[0:N, 0:N]
    A = np.zeros((N, N)); C = np.zeros((N, N, 3))
    pal = [(1.0, 0.82, 0.88), (0.98, 0.70, 0.80), (1.0, 0.9, 0.93), (0.95, 0.6, 0.74)]
    for i in range(70):
        px, py = rng.uniform(25, N - 25, 2); rad = rng.uniform(9, 20)
        d = np.sqrt((xx - px) ** 2 + (yy - py) ** 2)
        m = np.clip((rad - d) / 3.0, 0, 1)
        col = np.array(pal[rng.integers(0, 4)]) * rng.uniform(0.85, 1.05)
        upd = m > A * 0.999
        A = np.where(upd, np.maximum(A, m), A)
        for k in range(3): C[:, :, k] = np.where(upd & (m > 0.5), col[k], C[:, :, k])
    A = np.where(fbm(N, 16, 2, 22) > 0.3, A, 0)
    A = np.where(A > 0.45, 1.0, 0.0)
    C = C * (0.75 + 0.3 * fbm(N, 10, 2, 23)[:, :, None])
    save_png("sm_blossomcard.png", np.concatenate([C, A[:, :, None]], 2))
    # vending fronts (albedo == emission): rows of coloured cans
    for tag, seed, rows, cols in (("a", 31, 4, 8), ("b", 41, 3, 6), ("c", 51, 4, 9)):
        W, H = 512, 640; rng = np.random.default_rng(seed)
        img = np.full((H, W, 3), 0.05); img[:, :, 2] += 0.02
        pals = [(0.9, 0.1, 0.1), (0.1, 0.35, 0.95), (0.1, 0.7, 0.3), (1.0, 0.55, 0.05), (1.0, 0.85, 0.1),
                (0.95, 0.95, 0.95), (1.0, 0.45, 0.7), (0.42, 0.22, 0.1), (0.1, 0.8, 0.8), (0.6, 0.2, 0.9)]
        rh = H // rows; cw = W // cols
        for r in range(rows):
            y0 = r * rh
            img[y0:y0 + 6, :, :] = 0.55  # shelf
            for c in range(cols):
                col = np.array(pals[rng.integers(0, len(pals))])
                x0 = c * cw + int(cw * 0.18); x1 = c * cw + int(cw * 0.82)
                yb = y0 + 12; yt = y0 + int(rh * 0.74)
                img[yb:yt, x0:x1] = col
                img[yb:yb + 5, x0:x1] = 0.8; img[yt - 7:yt, x0:x1] = 0.8        # can rims
                hl = x0 + int((x1 - x0) * 0.12)
                img[yb + 6:yt - 8, hl:hl + 4] = np.minimum(col * 1.5 + 0.2, 1)  # highlight
                ym = (yb + yt) // 2
                img[ym - 12:ym + 12, x0 + 3:x1 - 3] = np.minimum(col * 0.3 + 0.65, 1)   # label
                img[ym - 3:ym + 3, x0 + 8:x1 - 8] = col * 0.6
                # price tag (dark chip with red digits)
                img[y0 + int(rh * 0.80):y0 + int(rh * 0.92), x0 + 4:x1 - 4] = (0.02, 0.0, 0.0)
                img[y0 + int(rh * 0.83):y0 + int(rh * 0.89), x0 + 10:x1 - 10] = (1.0, 0.1, 0.05)
        sh = 1.0 - 0.25 * np.linspace(0, 1, H)[:, None, None]
        save_png(f"sm_vendfront_{tag}.png", np.clip(img * sh, 0, 1))
    # rough galvanised steel scuffs (for the many metal parts)
    f = fbm(n, 8, 4, 61)
    save_png("sm_steel.png", np.stack([0.55 + 0.2 * (f - 0.5)] * 3, 2))


# ----------------------------------------------------------------------------- materials
MATDEF = {}


def M(name, color, metal=0.0, smooth=0.4, emit=None, ei=0.0, tex=None, ntex=None, etex=None, cutout=False, two=False):
    MATDEF[name] = dict(color=list(color) + [1.0], metallic=metal, smooth=smooth,
                        emissive=list(emit) if emit else None, emissiveIntensity=ei, tex=tex, ntex=ntex,
                        etex=etex, cutout=cutout, doubleSided=two)


def define_materials():
    M("SM_Concrete", (1, 1, 1), 0, .15, tex="sm_concrete.png", ntex="sm_concrete_n.png")
    M("SM_ConcreteDark", (.55, .55, .55), 0, .12, tex="sm_concrete.png", ntex="sm_concrete_n.png")
    M("SM_Stone", (1, 1, 1), 0, .2, tex="sm_stone.png", ntex="sm_stone_n.png")
    M("SM_PaintWhite", (.82, .82, .80), 0, .55)
    M("SM_PaintYellow", (.92, .68, .05), 0, .5)
    M("SM_PaintRed", (.62, .06, .05), 0, .55)
    M("SM_PaintGreen", (.07, .22, .13), 0, .5)
    M("SM_PaintBlue", (.05, .22, .62), 0, .5)
    M("SM_PaintBlack", (.025, .025, .03), 0, .45)
    M("SM_Steel", (.62, .63, .65), 1, .55, tex="sm_steel.png")
    M("SM_SteelDark", (.28, .29, .31), 1, .45, tex="sm_steel.png")
    M("SM_Rubber", (.03, .03, .03), 0, .2)
    M("SM_Plastic", (.55, .56, .58), 0, .5)
    M("SM_PlasticBlue", (.05, .18, .55), 0, .5)
    M("SM_PlasticGreen", (.06, .45, .2), 0, .5)
    M("SM_PlasticRed", (.7, .08, .06), 0, .5)
    M("SM_PlasticYellow", (.9, .7, .1), 0, .5)
    M("SM_Glass", (.04, .07, .09), 0, .95)
    M("SM_VendBodyRed", (.7, .04, .06), 0, .6)
    M("SM_VendBodyBlue", (.05, .16, .5), 0, .6)
    M("SM_VendBodyWhite", (.85, .86, .88), 0, .6)
    for t in "abc":
        M("SM_VendFront" + t.upper(), (.9, .9, .9), 0, .85, emit=(1, 1, 1), ei=2.2,
          tex=f"sm_vendfront_{t}.png", etex=f"sm_vendfront_{t}.png")
    M("SM_LampWarm", (1, .85, .6), 0, .5, emit=(1, .78, .45), ei=14)
    M("SM_LampCool", (.8, .9, 1), 0, .5, emit=(.8, .92, 1), ei=12)
    M("SM_LampRed", (1, .05, .03), 0, .5, emit=(1, .05, .02), ei=18)
    M("SM_LampGreen", (.05, 1, .5), 0, .5, emit=(.05, 1, .45), ei=18)
    M("SM_LampAmber", (.2, .12, .02), 0, .6)            # unlit lens
    M("SM_LampOffRed", (.2, .03, .02), 0, .6)
    M("SM_LampOffGreen", (.02, .15, .08), 0, .6)
    M("SM_Chochin", (1, 1, 1), 0, .3, emit=(1, .22, .1), ei=9, tex="sm_chochin.png", etex="sm_chochin.png", two=True)
    M("SM_ChochinWhite", (.95, .92, .85), 0, .3, emit=(1, .88, .6), ei=7)
    M("SM_Cloth", (1, 1, 1), 0, .1, tex="sm_cloth.png", two=True)
    M("SM_ClothRed", (.6, .06, .05), 0, .1, two=True)
    M("SM_TextWhite", (.9, .88, .8), 0, .3)
    M("SM_AwningRed", (.65, .06, .05), 0, .15, two=True)
    M("SM_AwningCream", (.85, .8, .68), 0, .15, two=True)
    M("SM_NeonPink", (1, .1, .55), 0, .3, emit=(1, .08, .5), ei=22)
    M("SM_NeonCyan", (.1, .9, 1), 0, .3, emit=(.05, .85, 1), ei=22)
    M("SM_NeonYellow", (1, .85, .1), 0, .3, emit=(1, .75, .05), ei=22)
    M("SM_NeonWhite", (1, 1, 1), 0, .3, emit=(1, .98, .95), ei=18)
    M("SM_NeonRed", (1, .08, .05), 0, .3, emit=(1, .05, .02), ei=20)
    M("SM_NeonGreen", (.2, 1, .4), 0, .3, emit=(.1, 1, .3), ei=20)
    M("SM_NeonBlue", (.15, .3, 1), 0, .3, emit=(.1, .25, 1), ei=20)
    M("SM_Wood", (1, 1, 1), 0, .2, tex="sm_wood.png")
    M("SM_Chalkboard", (.04, .08, .06), 0, .25)
    M("SM_ToriiRed", (1, 1, 1), 0, .3, tex="sm_torii.png", ntex="sm_torii_n.png")
    M("SM_Bark", (1, 1, 1), 0, .15, tex="sm_bark.png", ntex="sm_bark_n.png")
    M("SM_Blossom", (.95, .62, .75), 0, .25)
    M("SM_BlossomDeep", (.9, .45, .62), 0, .25)
    M("SM_BlossomCard", (1, 1, 1), 0, .25, tex="sm_blossomcard.png", cutout=True, two=True)
    M("SM_Manhole", (1, 1, 1), .6, .45, tex="sm_manhole.png", ntex="sm_manhole_n.png")
    M("SM_Cable", (.02, .02, .02), 0, .35)
    M("SM_Insulator", (.55, .3, .18), 0, .85)
    M("SM_Transformer", (.35, .4, .36), .4, .4)
    M("SM_Tank", (.7, .72, .74), .7, .45, tex="sm_steel.png")
    M("SM_Reflector", (1, .45, .05), 0, .8, emit=(1, .4, .05), ei=1.5)
    M("SM_SignBlue", (.03, .2, .6), 0, .4)
    M("SM_Pictogram", (.95, .95, .95), 0, .4)


def make_blender_materials():
    mats = {}
    for name, d in MATDEF.items():
        m = bpy.data.materials.new(name); m.use_nodes = True
        nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = d["color"]
        bsdf.inputs["Metallic"].default_value = d["metallic"]
        bsdf.inputs["Roughness"].default_value = 1.0 - d["smooth"]
        if d["tex"]:
            t = nt.nodes.new("ShaderNodeTexImage")
            t.image = bpy.data.images.load(os.path.join(TEXD, d["tex"]))
            nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
            if d["cutout"]:
                nt.links.new(t.outputs["Alpha"], bsdf.inputs["Alpha"])
                m.blend_method = 'CLIP' if hasattr(m, "blend_method") else None
        if d["ntex"]:
            t = nt.nodes.new("ShaderNodeTexImage")
            t.image = bpy.data.images.load(os.path.join(TEXD, d["ntex"]))
            t.image.colorspace_settings.name = "Non-Color"
            nm = nt.nodes.new("ShaderNodeNormalMap")
            nt.links.new(t.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        if d["emissive"]:
            if d["etex"]:
                t = nt.nodes.new("ShaderNodeTexImage")
                t.image = bpy.data.images.load(os.path.join(TEXD, d["etex"]))
                nt.links.new(t.outputs["Color"], bsdf.inputs["Emission Color"])
            else:
                bsdf.inputs["Emission Color"].default_value = d["emissive"] + [1.0]
            bsdf.inputs["Emission Strength"].default_value = min(d["emissiveIntensity"], 40) * 0.12
        mats[name] = m
    return mats


# ----------------------------------------------------------------------------- mesh builder
class B:
    """Accumulates geometry in one bmesh; every primitive is UV-mapped and given a material slot."""

    def __init__(self, mats):
        self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.matobjs = mats; self.slots = []

    def _mi(self, name):
        if name not in self.slots:
            self.slots.append(name)
        return self.slots.index(name)

    def _fin(self, n0, mat, tile=1.0, uvmode='box', plane=None):
        self.bm.faces.ensure_lookup_table(); self.bm.normal_update()
        mi = self._mi(mat)
        for f in [f for f in self.bm.faces if f not in n0]:
            f.material_index = mi
            if uvmode == 'keep':
                continue
            n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                p = l.vert.co
                if uvmode == 'planar':
                    u, v = p.x / plane + 0.5, p.y / plane + 0.5
                elif ax == 0:
                    u, v = p.y / tile, p.z / tile
                elif ax == 1:
                    u, v = p.x / tile, p.z / tile
                else:
                    u, v = p.x / tile, p.y / tile
                l[self.uv].uv = (u, v)

    def _xf(self, verts, center, rot):
        e = Euler([math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4()
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation(center) @ e, verts=verts)

    def box(self, c, s, mat, bevel=0.008, rot=(0, 0, 0), tile=1.0, seg=2):
        n0 = set(self.bm.faces)
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        vs = r["verts"]
        bmesh.ops.scale(self.bm, vec=Vector(s), verts=vs)
        self._xf(vs, c, rot)
        if bevel > 0:
            b = min(bevel, min(s) * 0.45)
            es = list({e for v in vs for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=es, offset=b, offset_type='OFFSET', segments=seg, profile=0.5, affect='EDGES')
        self._fin(n0, mat, tile)

    def cyl(self, p0, p1, r, mat, seg=12, r2=None, caps=True, tile=1.0):
        p0 = Vector(p0); p1 = Vector(p1); d = p1 - p0; L = d.length
        n0 = set(self.bm.faces)
        res = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=seg,
                                    radius1=r, radius2=(r if r2 is None else r2), depth=L)
        vs = res["verts"]
        q = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation((p0 + p1) / 2) @ q, verts=vs)
        self._fin(n0, mat, tile)

    def vcyl(self, x, y, z0, z1, r, mat, seg=12, r2=None, caps=True):
        self.cyl((x, y, z0), (x, y, z1), r, mat, seg, r2, caps)

    def sphere(self, c, r, mat, seg=10, scale=(1, 1, 1), rings=None):
        n0 = set(self.bm.faces)
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=rings or max(4, seg // 2), radius=r)
        vs = res["verts"]
        bmesh.ops.scale(self.bm, vec=Vector(scale), verts=vs)
        bmesh.ops.translate(self.bm, vec=Vector(c), verts=vs)
        self._fin(n0, mat)

    def disc(self, c, r, mat, seg=32, uv_r=None):
        """Upward-facing disc with planar 0..1 UVs (texture square = 2*uv_r)."""
        n0 = set(self.bm.faces); uv_r = uv_r or r
        ctr = self.bm.verts.new(c)
        ring = [self.bm.verts.new((c[0] + r * math.cos(i / seg * math.tau), c[1] + r * math.sin(i / seg * math.tau), c[2])) for i in range(seg)]
        for i in range(seg):
            f = self.bm.faces.new([ctr, ring[i], ring[(i + 1) % seg]])
            for l in f.loops:
                p = l.vert.co
                l[self.uv].uv = ((p.x - c[0]) / (2 * uv_r) + 0.5, (p.y - c[1]) / (2 * uv_r) + 0.5)
        self._fin(n0, mat, uvmode='keep')

    def quad(self, p0, p1, p2, p3, mat, uvs=((0, 0), (1, 0), (1, 1), (0, 1)), double=False):
        n0 = set(self.bm.faces)
        vs = [self.bm.verts.new(p) for p in (p0, p1, p2, p3)]
        f = self.bm.faces.new(vs)
        for l, uv in zip(f.loops, uvs):
            l[self.uv].uv = uv
        if double:
            f2 = self.bm.faces.new(list(reversed([self.bm.verts.new(p) for p in (p0, p1, p2, p3)])))
            for l, uv in zip(f2.loops, reversed(uvs)):
                l[self.uv].uv = uv
        self._fin(n0, mat, uvmode='keep')

    def front_panel(self, x0, x1, z0, z1, y, mat, back=False):
        """UV 0..1 panel facing -Y (or +Y when back)."""
        if not back:
            self.quad((x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1), mat)
        else:
            self.quad((x1, y, z0), (x0, y, z0), (x0, y, z1), (x1, y, z1), mat)

    def lathe(self, prof, mat, seg=12, vtile=1.0, smooth_uv=True, scale=(1, 1)):
        """prof: [(radius, z), ...] bottom -> top, revolved around Z at the origin."""
        n0 = set(self.bm.faces)
        rings = []
        for (r, z) in prof:
            if r < 1e-6:
                rings.append([self.bm.verts.new((0, 0, z))] * seg)
            else:
                rings.append([self.bm.verts.new((r * math.cos(i / seg * math.tau) * scale[0],
                                                 r * math.sin(i / seg * math.tau) * scale[1], z)) for i in range(seg)])
        acc = [0.0]
        for j in range(1, len(prof)):
            acc.append(acc[-1] + math.hypot(prof[j][0] - prof[j - 1][0], prof[j][1] - prof[j - 1][1]))
        circ = max(r for r, _ in prof) * math.tau
        for j in range(len(prof) - 1):
            for i in range(seg):
                i2 = (i + 1) % seg
                vs = [rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]]
                uvs = [(i / seg * circ / vtile, acc[j] / vtile), ((i + 1) / seg * circ / vtile, acc[j] / vtile),
                       ((i + 1) / seg * circ / vtile, acc[j + 1] / vtile), (i / seg * circ / vtile, acc[j + 1] / vtile)]
                keep = []
                for k in range(4):
                    if not keep or keep[-1][0] is not vs[k]:
                        keep.append((vs[k], uvs[k]))
                if len(keep) > 1 and keep[0][0] is keep[-1][0]:
                    keep.pop()
                if len(keep) < 3:
                    continue
                try:
                    f = self.bm.faces.new([k[0] for k in keep])
                except ValueError:
                    continue
                for l, k in zip(f.loops, keep):
                    l[self.uv].uv = k[1]
        self._fin(n0, mat, uvmode='keep')

    def tube(self, pts, r, mat, sides=5, taper=None, cap=True):
        pts = [Vector(p) for p in pts]; n0 = set(self.bm.faces); rings = []
        up = Vector((0, 0, 1))
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            a = up if abs(t.dot(up)) < 0.95 else Vector((1, 0, 0))
            u = t.cross(a).normalized(); v = t.cross(u).normalized()
            rr = r if taper is None else r + (taper - r) * i / max(1, len(pts) - 1)
            rings.append([self.bm.verts.new(p + (u * math.cos(k / sides * math.tau) + v * math.sin(k / sides * math.tau)) * rr)
                          for k in range(sides)])
        run = 0.0
        for j in range(len(pts) - 1):
            seglen = (pts[j + 1] - pts[j]).length
            for k in range(sides):
                k2 = (k + 1) % sides
                f = self.bm.faces.new([rings[j][k], rings[j + 1][k], rings[j + 1][k2], rings[j][k2]])
                us = (k / sides * 2, k2 / sides * 2 if k2 else 2.0)
                for l, uv in zip(f.loops, [(us[0], run), (us[0], run + seglen), (us[1], run + seglen), (us[1], run)]):
                    l[self.uv].uv = uv
            run += seglen
        if cap:
            for ring, flip in ((rings[0], True), (rings[-1], False)):
                f = self.bm.faces.new(list(reversed(ring)) if flip else ring)
                for l in f.loops:
                    l[self.uv].uv = (0.5, 0.5)
        self.bm.normal_update()
        # make the tube outward-facing regardless of ring winding
        c = sum((p for p in pts), Vector()) / len(pts)
        fs = [f for f in self.bm.faces if f not in n0]
        if fs:
            f = fs[0]
            if (f.calc_center_median() - pts[0]).dot(f.normal) < 0:
                bmesh.ops.reverse_faces(self.bm, faces=list(fs))
        self._fin(n0, mat, uvmode='keep')

    def text(self, body, pos, size, mat, extrude=0.01, rot=(90, 0, 0), align='CENTER', spacing=1.0, yalign='CENTER', xscale=1.0):
        c = bpy.data.curves.new("t", "FONT"); c.body = body; c.font = font(); c.size = size
        c.extrude = extrude; c.resolution_u = 1 if size < 0.12 else 2; c.align_x = align; c.align_y = yalign; c.space_line = spacing
        o = bpy.data.objects.new("t", c); bpy.context.scene.collection.objects.link(o)
        bpy.context.view_layer.update()
        me = bpy.data.meshes.new_from_object(o.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        e = Euler([math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4()
        sc = Matrix.Diagonal((xscale, 1, 1, 1))
        me.transform(Matrix.Translation(pos) @ e @ sc)
        n0 = set(self.bm.faces)
        self.bm.from_mesh(me)
        bpy.data.objects.remove(o); bpy.data.curves.remove(c); bpy.data.meshes.remove(me)
        self._fin(n0, mat, tile=0.5)

    def prism_profile(self, poly, x0, x1, mat, double=True):
        """Sweep an open profile polyline [(y,z)...] along x (thin ribbon, both sides)."""
        n0 = set(self.bm.faces)
        for i in range(len(poly) - 1):
            (ya, za), (yb, zb) = poly[i], poly[i + 1]
            a = (x0, ya, za); b = (x1, ya, za); c = (x1, yb, zb); d = (x0, yb, zb)
            for pts in ((a, b, c, d), (d, c, b, a)) if double else ((a, b, c, d),):
                f = self.bm.faces.new([self.bm.verts.new(p) for p in pts])
        self._fin(n0, mat)

    def build(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me); self.bm.free()
        for s in self.slots:
            me.materials.append(self.matobjs[s])
        o = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(o)
        bpy.context.view_layer.objects.active = o
        for ob in bpy.context.view_layer.objects:
            ob.select_set(False)
        o.select_set(True)
        try:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(38))
        except Exception:
            bpy.ops.object.shade_smooth()
        o.select_set(False)
        return o


def tri_count(o):
    return sum(max(1, len(p.vertices) - 2) for p in o.data.polygons)


def catenary(p0, p1, sag, n=12):
    p0 = Vector(p0); p1 = Vector(p1); out = []
    for i in range(n + 1):
        t = i / n
        q = p0.lerp(p1, t); q.z -= sag * 4 * t * (1 - t)
        out.append(q)
    return out

"""
Maple City East ("Harbour & Market District") landmark kit - hand-authored in Blender, bevelled and material-named so
MapleCityEnvironment.BuildEastLandmarks can swap each material slot for the project's CelLit look.

    blender -b -P tools/blender/build_maple_east_landmarks.py -- [asset ...] [--out DIR] [--preview DIR]

Assets (all Y-up in Unity after glTF export, origin on the ground at the footprint centre, +Z-forward = the street-facing side):
    gantry_crane      28 m portal crane: four legs, cross bracing, boom + counter-jib, cab, hoist cables
    lighthouse        19 m tapered tower with painted bands, gallery rail, lantern room and cone roof
    container_stack   3 x 2 x 3 stack of ribbed shipping containers in six liveries
    fishing_boat      12 m trawler: lofted hull, wheelhouse, mast, boom, rail, fenders
    warehouse         24 x 14 m brick warehouse: sawtooth roof, roller doors, windows, loading dock, downpipes
    ferry_canopy      30 m passenger shelter: curved roof on slim columns, benches, bollards, lamp posts
    market_stalls     row of 5 striped-awning fish-market stalls with crates, counters and hanging lamps

Material names (stable contract with the Unity side): steel, steel_dark, paint_red, paint_white, paint_yellow, paint_blue,
brick, concrete, roof_metal, glass, wood, rope, tarp_a, tarp_b, tarp_c, container_red, container_blue, container_green,
container_orange, container_grey, container_white, lamp.
"""
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[argv.index('--out') + 1]) if '--out' in argv else os.path.abspath('Assets/Environment/MapleCity/East')
PREVIEW = os.path.abspath(argv[argv.index('--preview') + 1]) if '--preview' in argv else None
WANT = [a for a in argv if not a.startswith('--') and (argv.index(a) == 0 or not argv[argv.index(a) - 1].startswith('--'))]

PALETTE = {
    'steel': (0.55, 0.58, 0.62), 'steel_dark': (0.20, 0.22, 0.25), 'paint_red': (0.72, 0.13, 0.11), 'paint_white': (0.90, 0.90, 0.88),
    'paint_yellow': (0.92, 0.72, 0.10), 'paint_blue': (0.12, 0.28, 0.55), 'brick': (0.50, 0.24, 0.18), 'concrete': (0.58, 0.58, 0.56),
    'roof_metal': (0.34, 0.38, 0.42), 'glass': (0.35, 0.55, 0.65), 'wood': (0.42, 0.28, 0.16), 'rope': (0.62, 0.55, 0.40),
    'tarp_a': (0.78, 0.18, 0.16), 'tarp_b': (0.12, 0.40, 0.55), 'tarp_c': (0.88, 0.70, 0.18),
    'container_red': (0.62, 0.14, 0.12), 'container_blue': (0.12, 0.28, 0.52), 'container_green': (0.14, 0.40, 0.26),
    'container_orange': (0.82, 0.38, 0.10), 'container_grey': (0.46, 0.48, 0.50), 'container_white': (0.84, 0.84, 0.82),
    'lamp': (1.0, 0.82, 0.5),
}
ROUGH = {'glass': 0.12, 'steel': 0.45, 'steel_dark': 0.5, 'brick': 0.85, 'concrete': 0.9, 'roof_metal': 0.5, 'wood': 0.75, 'rope': 0.9, 'lamp': 0.3}
METAL = {'steel': 0.7, 'steel_dark': 0.5, 'roof_metal': 0.5}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


_mats = {}


def mat(name):
    if name in _mats and name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    c = PALETTE[name]
    b.inputs['Base Color'].default_value = (c[0], c[1], c[2], 1)
    m.diffuse_color = (c[0], c[1], c[2], 1)
    b.inputs['Roughness'].default_value = ROUGH.get(name, 0.6)
    b.inputs['Metallic'].default_value = METAL.get(name, 0.0)
    if name == 'lamp':
        b.inputs['Emission Color'].default_value = (c[0], c[1], c[2], 1)
        b.inputs['Emission Strength'].default_value = 2.0
    if name == 'glass':
        b.inputs['Alpha'].default_value = 0.55
    _mats[name] = True
    return m


class Kit:
    """Accumulates geometry into one bmesh per material so the exported mesh has clean material slots."""

    def __init__(self, name):
        self.name = name
        self.bm = {}

    def _b(self, mname):
        if mname not in self.bm:
            self.bm[mname] = bmesh.new()
        return self.bm[mname]

    # ---- primitives (all in metres; z up inside Blender, exported as Y up)
    def box(self, m, c, size, rot=(0, 0, 0), bevel=0.0):
        b = self._b(m)
        geo = bmesh.ops.create_cube(b, size=1.0)
        mw = Matrix.Translation(c) @ Euler(rot, 'XYZ').to_matrix().to_4x4() @ Matrix.Diagonal((size[0], size[1], size[2], 1))
        bmesh.ops.transform(b, matrix=mw, verts=geo['verts'])
        if bevel > 0:
            edges = list({e for v in geo['verts'] for e in v.link_edges})
            bmesh.ops.bevel(b, geom=edges, offset=min(bevel, min(size) * 0.45), segments=2, affect='EDGES')

    def cyl(self, m, c, r, h, rot=(0, 0, 0), segs=20, r2=None):
        b = self._b(m)
        geo = bmesh.ops.create_cone(b, cap_ends=True, segments=segs, radius1=r, radius2=(r if r2 is None else r2), depth=h)
        mw = Matrix.Translation(c) @ Euler(rot, 'XYZ').to_matrix().to_4x4()
        bmesh.ops.transform(b, matrix=mw, verts=geo['verts'])
        for f in set(f for v in geo['verts'] for f in v.link_faces):
            f.smooth = True

    def beam(self, m, a, b_, w, w2=None, segs=8):
        """Round/square bar between two points."""
        a, b_ = Vector(a), Vector(b_)
        d = b_ - a
        L = d.length
        if L < 1e-5:
            return
        mid = (a + b_) / 2
        q = d.to_track_quat('Z', 'Y').to_euler()
        self.cyl(m, mid, w * 0.5, L, rot=q, segs=segs, r2=(w2 * 0.5 if w2 else None))

    def sphere(self, m, c, r, segs=16):
        b = self._b(m)
        geo = bmesh.ops.create_uvsphere(b, u_segments=segs, v_segments=max(6, segs // 2), radius=r)
        bmesh.ops.transform(b, matrix=Matrix.Translation(c), verts=geo['verts'])
        for f in set(f for v in geo['verts'] for f in v.link_faces):
            f.smooth = True

    def quad(self, m, p0, p1, p2, p3, flip=False):
        b = self._b(m)
        vs = [b.verts.new(Vector(p)) for p in (p0, p1, p2, p3)]
        try:
            f = b.faces.new(vs[::-1] if flip else vs)
            f.smooth = False
        except ValueError:
            pass

    def loft(self, m, rings, close=True, smooth=True):
        """rings: list of closed point loops with equal vertex counts."""
        b = self._b(m)
        vr = [[b.verts.new(Vector(p)) for p in ring] for ring in rings]
        n = len(rings[0])
        for i in range(len(vr) - 1):
            for j in range(n):
                j2 = (j + 1) % n
                try:
                    f = b.faces.new((vr[i][j], vr[i][j2], vr[i + 1][j2], vr[i + 1][j]))
                    f.smooth = smooth
                except ValueError:
                    pass
        if close:
            for ring in (vr[0], vr[-1]):
                try:
                    b.faces.new(ring)
                except ValueError:
                    pass

    def finish(self):
        """Merge material bmeshes into one object with material slots; returns the object."""
        me = bpy.data.meshes.new(self.name)
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        mats = list(self.bm.keys())
        big = bmesh.new()
        idx = {}
        for k, mname in enumerate(mats):
            b = self.bm[mname]
            bmesh.ops.recalc_face_normals(b, faces=b.faces)
            mp = {}
            for v in b.verts:
                mp[v] = big.verts.new(v.co)
            for f in b.faces:
                nf = big.faces.new([mp[v] for v in f.verts])
                nf.material_index = k
                nf.smooth = f.smooth
            b.free()
        big.to_mesh(me)
        big.free()
        for mname in mats:
            me.materials.append(mat(mname))
        return ob


def export(ob, name):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    ob.name = name
    # weighted normals + auto smooth via edge split on sharp angles keeps hard edges crisp, round parts smooth
    bpy.ops.object.shade_smooth()
    mod = ob.modifiers.new('es', 'EDGE_SPLIT')
    mod.split_angle = math.radians(38)
    bpy.ops.object.modifier_apply(modifier='es')
    path = os.path.join(OUT, f'MapleEast_{name}.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_apply=True)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f'EXPORTED {name}: {tris} tris, {len(ob.data.materials)} materials -> {path}')
    return tris


# ============================================================================ assets

def gantry_crane():
    k = Kit('gantry_crane')
    H, W, D = 26.0, 16.0, 11.0          # leg height, rail span (x), depth (y)
    # four legs (box girder look: outer + inner flanges) and portal beams
    for sx in (-1, 1):
        for sy in (-1, 1):
            k.box('paint_yellow', (sx * W / 2, sy * D / 2, H / 2), (1.1, 1.1, H), bevel=0.06)
            k.box('steel_dark', (sx * W / 2, sy * D / 2, 0.45), (1.6, 1.6, 0.9), bevel=0.05)       # wheel bogie block
    for sy in (-1, 1):
        k.box('paint_yellow', (0, sy * D / 2, H - 0.7), (W + 1.1, 1.3, 1.6), bevel=0.06)           # top beams
        k.box('paint_yellow', (0, sy * D / 2, H * 0.45), (W, 0.7, 0.7), bevel=0.04)                # mid beams
    for sx in (-1, 1):
        k.box('paint_yellow', (sx * W / 2, 0, H - 0.7), (1.3, D, 1.6), bevel=0.06)                  # end beams
    # X bracing on both long faces
    for sy in (-1, 1):
        for sx in (-1, 1):
            k.beam('steel', (sx * W / 2, sy * D / 2, 1.5), (0, sy * D / 2, H - 1.6), 0.28, segs=6)
            k.beam('steel', (-sx * W / 2, sy * D / 2, 1.5), (0, sy * D / 2, H - 1.6), 0.28, segs=6)
    # runway rails
    for sx in (-1, 1):
        k.box('steel_dark', (sx * W / 2, 0, 0.04), (0.5, D + 12, 0.12))
    # boom (lattice) over the quay (+y) and counter-jib behind (-y)
    BZ = H + 1.4
    boom_len, jib_len = 24.0, 9.0
    for sx in (-0.9, 0.9):
        k.beam('paint_red', (sx, -jib_len, BZ), (sx, boom_len, BZ), 0.38, segs=6)
        k.beam('paint_red', (sx, -jib_len, BZ + 1.8), (sx, boom_len * 0.55, BZ + 1.8), 0.28, segs=6)
    for i in range(14):
        y0 = -jib_len + i * (boom_len + jib_len) / 14
        y1 = y0 + (boom_len + jib_len) / 14
        k.beam('paint_red', (-0.9, y0, BZ), (0.9, y1, BZ), 0.2, segs=6)
        k.beam('paint_red', (0.9, y0, BZ), (-0.9, y1, BZ), 0.2, segs=6)
    k.box('paint_red', (0, boom_len * 0.5, BZ + 0.15), (2.2, boom_len + jib_len + 0.4, 0.28), bevel=0.04)
    # A-frame mast + stays
    k.beam('paint_red', (-1.1, 2.5, BZ), (0, 2.5, BZ + 6.5), 0.5, segs=6)
    k.beam('paint_red', (1.1, 2.5, BZ), (0, 2.5, BZ + 6.5), 0.5, segs=6)
    k.beam('rope', (0, 2.5, BZ + 6.5), (0, boom_len, BZ), 0.07, segs=4)
    k.beam('rope', (0, 2.5, BZ + 6.5), (0, -jib_len, BZ), 0.07, segs=4)
    # counterweight + machinery house + operator cab
    k.box('concrete', (0, -jib_len + 1.4, BZ - 0.6), (2.6, 2.4, 1.6), bevel=0.08)
    k.box('paint_yellow', (0, -2.0, BZ + 1.0), (3.4, 4.6, 2.2), bevel=0.1)
    k.box('paint_white', (0, 0.35, BZ - 2.4), (2.0, 2.0, 2.0), bevel=0.1)
    k.box('glass', (0, 1.38, BZ - 2.3), (1.7, 0.06, 1.2))
    # trolley + hoist cables + spreader
    ty = 16.0
    k.box('steel_dark', (0, ty, BZ - 0.5), (2.2, 2.6, 0.7), bevel=0.05)
    for sx in (-0.7, 0.7):
        for sy in (-0.9, 0.9):
            k.beam('rope', (sx, ty + sy, BZ - 0.8), (sx * 1.6, ty + sy * 1.6, BZ - 11.0), 0.05, segs=4)
    k.box('steel_dark', (0, ty, BZ - 11.2), (3.4, 4.6, 0.45), bevel=0.05)
    # cat-walk rails + warning stripes
    for sy in (-1, 1):
        k.box('paint_white', (0, sy * D / 2, H - 0.7 + 0.82), (W + 1.1, 0.06, 0.05))
    for i in range(8):
        k.box('paint_white', (-W / 2 + i * (W / 7), D / 2, 0.3), (0.5, 1.2, 0.1)) if False else None
    # ladders on two legs
    for z in [1.0 + i * 0.4 for i in range(int((H - 2) / 0.4))]:
        k.box('steel', (W / 2 + 0.62, D / 2, z), (0.05, 0.5, 0.05))
    k.beam('steel', (W / 2 + 0.62, D / 2 - 0.25, 0.8), (W / 2 + 0.62, D / 2 - 0.25, H - 1), 0.05, segs=4)
    k.beam('steel', (W / 2 + 0.62, D / 2 + 0.25, 0.8), (W / 2 + 0.62, D / 2 + 0.25, H - 1), 0.05, segs=4)
    return k.finish()


def lighthouse():
    k = Kit('lighthouse')
    H = 19.0
    # tapered tower in painted bands (loft of octagon rings -> smooth round)
    N = 28
    def ring(z, r):
        return [(math.cos(2 * math.pi * i / N) * r, math.sin(2 * math.pi * i / N) * r, z) for i in range(N)]
    bands = [('paint_white', 0.0, 5.0), ('paint_red', 5.0, 9.0), ('paint_white', 9.0, 13.0), ('paint_red', 13.0, 15.5)]
    def rad(z):
        return 2.9 - 1.15 * (z / 15.5)
    for m, z0, z1 in bands:
        zs = [z0 + (z1 - z0) * i / 4 for i in range(5)]
        k.loft(m, [ring(z, rad(z)) for z in zs], close=False)
    k.cyl('concrete', (0, 0, 0.35), 3.7, 0.7, segs=28)                      # plinth
    k.cyl('concrete', (0, 0, 0.9), 3.3, 0.35, segs=28)
    # door + windows
    k.box('wood', (0, -rad(1.1) + 0.02, 1.2), (1.0, 0.18, 2.0), bevel=0.04)
    k.box('steel_dark', (0, -rad(1.1) - 0.05, 2.3), (1.4, 0.2, 0.16), bevel=0.03)
    for z in (4.2, 7.6, 11.0):
        for a in (math.radians(90), math.radians(270)):
            x, y = math.cos(a) * rad(z), math.sin(a) * rad(z)
            k.box('glass', (x * 1.0, y * 1.0, z), (0.7, 0.7, 1.1), rot=(0, 0, a - math.pi / 2))
    # gallery deck, brackets and railing
    k.cyl('steel_dark', (0, 0, 15.6), 2.55, 0.28, segs=28)
    for i in range(12):
        a = 2 * math.pi * i / 12
        k.beam('steel_dark', (math.cos(a) * 1.5, math.sin(a) * 1.5, 14.9), (math.cos(a) * 2.45, math.sin(a) * 2.45, 15.5), 0.14, segs=5)
    M = 36
    for i in range(M):
        a = 2 * math.pi * i / M
        k.beam('steel_dark', (math.cos(a) * 2.5, math.sin(a) * 2.5, 15.74), (math.cos(a) * 2.5, math.sin(a) * 2.5, 16.8), 0.05, segs=4)
    for z in (16.8,):
        for i in range(M):
            a0, a1 = 2 * math.pi * i / M, 2 * math.pi * (i + 1) / M
            k.beam('steel_dark', (math.cos(a0) * 2.5, math.sin(a0) * 2.5, z), (math.cos(a1) * 2.5, math.sin(a1) * 2.5, z), 0.07, segs=4)
    # lantern room: glass drum with mullions, lamp, roof cone, finial
    k.cyl('glass', (0, 0, 17.4), 1.45, 1.9, segs=24)
    for i in range(12):
        a = 2 * math.pi * i / 12
        k.beam('steel_dark', (math.cos(a) * 1.47, math.sin(a) * 1.47, 16.45), (math.cos(a) * 1.47, math.sin(a) * 1.47, 18.35), 0.09, segs=5)
    k.cyl('steel_dark', (0, 0, 16.5), 1.6, 0.14, segs=24)
    k.cyl('steel_dark', (0, 0, 18.4), 1.62, 0.16, segs=24)
    k.sphere('lamp', (0, 0, 17.4), 0.55, segs=18)
    k.cyl('paint_red', (0, 0, 19.1), 1.75, 1.3, segs=24, r2=0.12)
    k.sphere('steel', (0, 0, 19.85), 0.18, segs=10)
    k.beam('steel', (0, 0, 19.8), (0, 0, 20.9), 0.07, segs=4)
    return k.finish()


def container(k, c, mname, rot_z=0.0, size=(2.44, 6.06, 2.59)):
    """Ribbed ISO container: body box + corrugation ribs on the long sides and doors on one end."""
    x, y, z = c
    c0 = Vector(c)
    R = Matrix.Rotation(rot_z, 4, 'Z')
    def P(dx, dy, dz):
        return c0 + (R @ Vector((dx, dy, dz)))
    sx, sy, sz = size
    k.box(mname, P(0, 0, 0), size, rot=(0, 0, rot_z), bevel=0.03)
    # corrugation ribs on both long faces
    n = 18
    for i in range(n):
        dy = -sy / 2 + 0.28 + i * (sy - 0.56) / (n - 1)
        for sgn in (-1, 1):
            k.box(mname, P(sgn * (sx / 2 + 0.012), dy, 0), (0.05, 0.14, sz - 0.34), rot=(0, 0, rot_z))
    # corner posts + door end bars
    for sxg in (-1, 1):
        for syg in (-1, 1):
            k.box('steel_dark', P(sxg * (sx / 2 - 0.08), syg * (sy / 2 - 0.08), 0), (0.16, 0.16, sz + 0.02), rot=(0, 0, rot_z))
    for dxo in (-0.45, -0.15, 0.15, 0.45):
        k.box('steel', P(dxo, sy / 2 + 0.03, 0), (0.05, 0.05, sz - 0.2), rot=(0, 0, rot_z))
    k.box('steel_dark', P(0, sy / 2 + 0.02, 0), (0.04, 0.03, sz - 0.1), rot=(0, 0, rot_z))


def container_stack():
    k = Kit('container_stack')
    colors = ['container_red', 'container_blue', 'container_green', 'container_orange', 'container_grey', 'container_white']
    rnd = random.Random(7)
    layout = [(0, 0, 0), (1, 0, 0), (2, 0, 0), (0, 1, 0), (1, 1, 0), (2, 1, 0),
              (0, 0, 1), (1, 0, 1), (2, 0, 1), (1, 1, 1),
              (1, 0, 2)]
    for (ix, iy, iz) in layout:
        c = ((ix - 1) * 2.5, (iy - 0.5) * 6.2, 1.3 + iz * 2.6)
        container(k, c, colors[rnd.randrange(len(colors))], rot_z=math.radians(rnd.uniform(-1.2, 1.2)) * (1 if iz else 0))
    return k.finish()


def fishing_boat():
    k = Kit('fishing_boat')
    L, Wd = 12.0, 3.6                                 # length (y), beam (x)
    N = 14
    ys = [-L / 2 + L * t for t in (0.0, 0.08, 0.2, 0.35, 0.5, 0.65, 0.8, 0.9, 0.97, 1.0)]

    def beam_at(t):
        b = Wd / 2 * (math.sin(min(1.0, t + 0.18) * math.pi * 0.5) ** 0.7) * (1.0 - 0.75 * max(0, t - 0.78) / 0.22)
        return max(b, 0.04)

    def sheer_at(t):
        return 0.9 + 0.55 * t * t

    secs = []
    for y in ys:
        t = (y + L / 2) / L
        beam, sheer = beam_at(t), sheer_at(t)
        keel = -0.8 + 0.5 * max(0.0, 0.4 - t) + 0.9 * max(0.0, t - 0.55)
        ring = []
        for i in range(N):
            a = math.pi * i / (N - 1)
            ring.append((-beam * math.cos(a), y, sheer + (keel - sheer) * max(0.0, math.sin(a)) ** 0.8))
        secs.append(ring)
    k.loft('paint_blue', secs, close=False)
    for sx in (-1, 1):
        pts = [(sx * beam_at((y + L / 2) / L), y, sheer_at((y + L / 2) / L)) for y in ys]
        for i in range(len(pts) - 1):
            k.beam('paint_white', pts[i], pts[i + 1], 0.16, segs=5)
    k.box('wood', (0, -0.4, 1.12), (Wd * 0.78, L * 0.86, 0.08), bevel=0.02)
    k.box('paint_white', (0, -1.2, 2.1), (2.3, 2.9, 1.9), bevel=0.08)
    k.box('glass', (0, 0.26, 2.3), (1.9, 0.05, 0.8))
    for sx in (-1, 1):
        k.box('glass', (sx * 1.17, -1.2, 2.3), (0.05, 2.0, 0.7))
    k.box('paint_red', (0, -1.2, 3.15), (2.6, 3.2, 0.14), bevel=0.04)
    k.beam('steel_dark', (0, 1.4, 1.1), (0, 1.4, 6.2), 0.14, segs=8)
    k.beam('steel_dark', (0, 1.4, 4.2), (0, 4.6, 2.6), 0.1, segs=6)
    k.beam('rope', (0, 1.4, 6.1), (0, -3.6, 1.25), 0.03, segs=4)
    k.beam('rope', (0, 1.4, 6.1), (0, 4.6, 2.6), 0.03, segs=4)
    k.sphere('lamp', (0, 1.4, 6.3), 0.16, segs=10)
    k.beam('steel', (-1.2, -4.6, 1.15), (-1.2, -4.6, 3.6), 0.13, segs=6)
    k.beam('steel', (1.2, -4.6, 1.15), (1.2, -4.6, 3.6), 0.13, segs=6)
    k.beam('steel', (-1.2, -4.6, 3.6), (1.2, -4.6, 3.6), 0.13, segs=6)
    for i in range(3):
        k.box('wood', (-0.9 + i * 0.9, -3.6, 1.35), (0.7, 0.55, 0.4), bevel=0.02)
    for sx in (-1, 1):
        for y in (-3.6, -1.0, 1.6):
            t = (y + L / 2) / L
            k.sphere('steel_dark', (sx * (beam_at(t) + 0.1), y, sheer_at(t) - 0.2), 0.2, segs=10)
    return k.finish()


def warehouse():
    k = Kit('warehouse')
    W, D, H = 24.0, 14.0, 7.0
    k.box('concrete', (0, 0, 0.45), (W + 0.3, D + 0.3, 0.9), bevel=0.05)
    k.box('brick', (0, 0, H / 2 + 0.45), (W, D, H), bevel=0.04)
    for i in range(9):
        x = -W / 2 + i * W / 8
        k.box('brick', (x, D / 2 + 0.12, H / 2 + 0.45), (0.7, 0.26, H), bevel=0.03)
        k.box('brick', (x, -D / 2 - 0.12, H / 2 + 0.45), (0.7, 0.26, H), bevel=0.03)
    k.box('concrete', (0, 0, H + 0.62), (W + 0.5, D + 0.5, 0.34), bevel=0.04)
    teeth = 6
    tw = D / teeth
    base = H + 0.8
    for i in range(teeth):
        y0 = -D / 2 + i * tw
        k.quad('roof_metal', (-W / 2, y0, base), (W / 2, y0, base), (W / 2, y0 + tw, base + 1.9), (-W / 2, y0 + tw, base + 1.9))
        k.box('glass', (0, y0 + tw - 0.04, base + 0.95), (W - 0.8, 0.05, 1.7))
        k.box('steel_dark', (0, y0 + tw, base + 1.92), (W, 0.14, 0.14))
        for sx in (-1, 1):
            k.box('steel_dark', (sx * (W / 2 - 0.05), y0 + tw / 2, base + 0.95), (0.1, tw, 1.9))
    fy = D / 2
    for i in range(3):
        x = -W / 3 + i * W / 3
        k.box('steel', (x, fy + 0.06, 2.7), (3.6, 0.14, 4.6), bevel=0.03)
        for j in range(12):
            k.box('steel_dark', (x, fy + 0.15, 0.7 + j * 0.37), (3.5, 0.04, 0.05))
        k.box('steel_dark', (x, fy + 0.1, 5.0), (4.0, 0.22, 0.35), bevel=0.03)
        k.box('paint_yellow', (x - 2.1, fy + 0.2, 1.0), (0.2, 0.2, 1.8), bevel=0.04)
        k.box('paint_yellow', (x + 2.1, fy + 0.2, 1.0), (0.2, 0.2, 1.8), bevel=0.04)
    for i in range(8):
        x = -W / 2 + 1.4 + i * (W - 2.8) / 7
        k.box('steel_dark', (x, fy + 0.05, 6.1), (1.3, 0.12, 1.5), bevel=0.02)
        k.box('glass', (x, fy + 0.1, 6.1), (1.1, 0.06, 1.3))
        k.box('concrete', (x, fy + 0.16, 5.3), (1.5, 0.3, 0.14))
    k.box('concrete', (0, fy + 1.0, 1.0), (W - 0.4, 2.0, 0.2), bevel=0.04)
    k.box('roof_metal', (0, fy + 1.4, 5.6), (W - 0.4, 2.6, 0.14), rot=(math.radians(-8), 0, 0))
    for x in (-W / 2 + 0.3, 0, W / 2 - 0.3):
        k.beam('steel_dark', (x, fy + 2.5, 1.0), (x, fy + 2.5, 5.5), 0.14, segs=6)
    for x in (-W / 2 + 0.2, W / 2 - 0.2):
        k.beam('steel_dark', (x, fy + 0.3, 0.9), (x, fy + 0.3, H + 0.5), 0.1, segs=6)
    k.cyl('brick', (-W / 2 + 3, -D / 2 + 2, H + 3.2), 0.55, 4.6, segs=12)
    k.cyl('steel_dark', (-W / 2 + 3, -D / 2 + 2, H + 5.6), 0.7, 0.2, segs=12)
    return k.finish()


def ferry_canopy():
    k = Kit('ferry_canopy')
    L, Wd, H = 30.0, 6.0, 4.2
    secs = []
    for i in range(9):
        y = -Wd / 2 + Wd * i / 8
        z = H + 0.9 * (1 - ((y / (Wd / 2)) ** 2)) + (0.25 if abs(y) > Wd / 2 * 0.9 else 0)
        secs.append([(-L / 2, y, z), (L / 2, y, z), (L / 2, y, z + 0.16), (-L / 2, y, z + 0.16)])
    k.loft('roof_metal', secs, close=True, smooth=True)
    for sy in (-1, 1):
        k.box('paint_white', (0, sy * Wd / 2, H + 0.12), (L + 0.1, 0.12, 0.4), bevel=0.03)
    cols = 7
    step = (L - 3.0) / (cols - 1)
    for i in range(cols):
        x = -L / 2 + 1.5 + i * step
        for sy in (-1, 1):
            k.beam('steel', (x, sy * (Wd / 2 - 0.5), 0.0), (x, sy * (Wd / 2 - 0.5), H), 0.26, segs=10)
            k.box('steel_dark', (x, sy * (Wd / 2 - 0.5), 0.1), (0.5, 0.5, 0.2), bevel=0.04)
        k.beam('steel', (x, -(Wd / 2 - 0.5), H - 0.1), (x, (Wd / 2 - 0.5), H - 0.1), 0.2, segs=8)
    for i in range(0, cols - 1, 2):
        x = -L / 2 + 1.5 + (i + 0.5) * step
        k.box('glass', (x, Wd / 2 - 0.5, 1.4), (step - 0.4, 0.05, 2.2))
        k.box('steel_dark', (x, Wd / 2 - 0.5, 0.25), (step - 0.3, 0.1, 0.1))
    for i in range(5):
        x = -L / 2 + 3.5 + i * 5.6
        k.box('wood', (x, 0.6, 0.48), (2.2, 0.5, 0.08), bevel=0.02)
        k.box('wood', (x, 0.85, 0.82), (2.2, 0.06, 0.5), bevel=0.02)
        for dx in (-0.9, 0.9):
            k.box('steel_dark', (x + dx, 0.6, 0.24), (0.08, 0.45, 0.48))
    for i in range(13):
        x = -L / 2 + 1.0 + i * 2.33
        k.cyl('steel_dark', (x, -Wd / 2 - 1.4, 0.4), 0.12, 0.8, segs=10)
        k.cyl('paint_yellow', (x, -Wd / 2 - 1.4, 0.82), 0.125, 0.06, segs=10)
    for x in (-L / 2 + 4, 0, L / 2 - 4):
        k.beam('steel_dark', (x, -Wd / 2 - 0.9, 0), (x, -Wd / 2 - 0.9, 5.2), 0.14, segs=8)
        k.beam('steel_dark', (x, -Wd / 2 - 0.9, 5.1), (x, -Wd / 2 - 0.2, 5.3), 0.09, segs=6)
        k.sphere('lamp', (x, -Wd / 2 - 0.2, 5.22), 0.2, segs=10)
    k.box('paint_blue', (-L / 2 + 6, Wd / 2 - 0.6, 1.9), (1.6, 0.08, 1.1), bevel=0.02)
    k.box('concrete', (0, 0, 0.06), (L + 2.0, Wd + 4.0, 0.12), bevel=0.02)
    return k.finish()


def market_stalls():
    k = Kit('market_stalls')
    tarps = ['tarp_a', 'tarp_b', 'tarp_c', 'tarp_a', 'tarp_b']
    rnd = random.Random(11)
    SW, SD = 3.0, 2.4
    for i in range(5):
        cx = (i - 2) * (SW + 0.5)
        tp = tarps[i]
        for sx in (-1, 1):
            k.beam('wood', (cx + sx * SW / 2, -SD / 2, 0), (cx + sx * SW / 2, -SD / 2, 2.1), 0.1, segs=6)
            k.beam('wood', (cx + sx * SW / 2, SD / 2, 0), (cx + sx * SW / 2, SD / 2, 2.55), 0.1, segs=6)
        strips = 8
        for j in range(strips):
            x0 = cx - SW / 2 - 0.15 + j * (SW + 0.3) / strips
            x1 = x0 + (SW + 0.3) / strips
            m = tp if j % 2 == 0 else 'paint_white'
            k.quad(m, (x0, SD / 2 + 0.4, 2.0), (x1, SD / 2 + 0.4, 2.0), (x1, -SD / 2, 2.6), (x0, -SD / 2, 2.6))
            k.quad(m, (x0, SD / 2 + 0.4, 2.0), (x0, SD / 2 + 0.4, 1.78), (x1, SD / 2 + 0.4, 1.78), (x1, SD / 2 + 0.4, 2.0))
        k.box('wood', (cx, SD / 2 - 0.35, 0.5), (SW - 0.2, 0.7, 1.0), bevel=0.03)
        k.box('steel', (cx, SD / 2 - 0.2, 1.02), (SW - 0.3, 0.55, 0.05))
        for t in range(3):
            k.box('paint_white', (cx - 0.9 + t * 0.9, SD / 2 - 0.2, 1.08), (0.7, 0.45, 0.08), bevel=0.02)
            for f in range(rnd.randint(2, 4)):
                k.sphere('steel', (cx - 0.9 + t * 0.9 + rnd.uniform(-0.2, 0.2), SD / 2 - 0.2 + rnd.uniform(-0.1, 0.1), 1.16), 0.08, segs=8)
        for c in range(rnd.randint(2, 4)):
            k.box('wood', (cx - SW / 2 + 0.4 + c * 0.55, -SD / 2 + 0.5, 0.2 + (c % 2) * 0.4), (0.5, 0.4, 0.38), bevel=0.02)
        k.beam('steel_dark', (cx, 0.2, 2.45), (cx, 0.2, 2.2), 0.03, segs=4)
        k.sphere('lamp', (cx, 0.2, 2.12), 0.1, segs=10)
        k.box('paint_red', (cx, -SD / 2 + 0.04, 1.4), (SW - 0.4, 0.05, 0.8), bevel=0.01)
    return k.finish()


ASSETS = {
    'gantry_crane': gantry_crane,
    'lighthouse': lighthouse,
    'container_stack': container_stack,
    'fishing_boat': fishing_boat,
    'warehouse': warehouse,
    'ferry_canopy': ferry_canopy,
    'market_stalls': market_stalls,
}


def preview(ob, name):
    if not PREVIEW:
        return
    os.makedirs(PREVIEW, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if hasattr(bpy.types, 'SceneEEVEE') else 'BLENDER_WORKBENCH'
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_cavity = True
    sc.view_settings.view_transform = 'Standard'
    sc.render.resolution_x, sc.render.resolution_y = 900, 900
    w = bpy.data.worlds.new('w')
    w.color = (0.55, 0.62, 0.7)
    sc.world = w
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    mn = Vector((min(v[i] for v in bb) for i in range(3)))
    mx = Vector((max(v[i] for v in bb) for i in range(3)))
    ctr = (mn + mx) / 2
    size = max(mx - mn)
    cam = bpy.data.cameras.new('c')
    cam.lens = 45
    co = bpy.data.objects.new('c', cam)
    sc.collection.objects.link(co)
    sc.camera = co
    for tag, az, el in (('a', 35, 18), ('b', 145, 12), ('c', -60, 30)):
        d = Vector((math.sin(math.radians(az)) * math.cos(math.radians(el)), -math.cos(math.radians(az)) * math.cos(math.radians(el)), math.sin(math.radians(el))))
        co.location = ctr + d * size * 1.9
        co.rotation_euler = (ctr - co.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(PREVIEW, f'{name}_{tag}.png')
        bpy.ops.render.render(write_still=True)


def main():
    names = WANT or list(ASSETS)
    for n in names:
        if n not in ASSETS:
            print('unknown asset', n)
            continue
        reset()
        _mats.clear()
        ob = ASSETS[n]()
        preview(ob, n)
        export(ob, n)


if __name__ == '__main__':
    main()

"""
Replace the lumpy generated helmet with a clean vented shell sized to the generated head.
  blender -b -P tools/blender/replace_helmet.py -- <in.glb> <out.glb> --dark u,v --grey u,v [--cut 1.005]
Faces above the cut height are removed; a vented dome (ribs + oval windows, solidified + subdivided) and a dark hair dome are
added. All new parts share the existing atlas: UVs point at two flat texels (graphite shell, grey stripe rib) passed in.
"""
import bpy, bmesh, sys, math
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[:2]
opt = {argv[i][2:]: argv[i + 1] for i in range(2, len(argv) - 1) if argv[i].startswith('--')}
CUT = float(opt.get('cut', '1.005'))
DARK = tuple(float(x) for x in opt['dark'].split(','))
GREY = tuple(float(x) for x in opt['grey'].split(','))
BLACK = tuple(float(x) for x in opt.get('black', opt['dark']).split(','))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = ob
me = ob.data

# measure the head at the rim to size the helmet
xs = [v.co.x for v in me.vertices if 0.97 < v.co.z < 1.03]
ys = [v.co.y for v in me.vertices if 0.97 < v.co.z < 1.03]
top = max(v.co.z for v in me.vertices)
cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
rx, ry = (max(xs) - min(xs)) / 2 + 0.012, (max(ys) - min(ys)) / 2 + 0.012
rz = float(opt.get('rz', top - 0.99 + 0.004))
rx = float(opt.get('rx', rx))
ry = float(opt.get('ry', ry))
c = Vector((cx, cy, 0.99))
print('HELMET fit centre', tuple(round(x, 3) for x in c), 'r', round(rx, 3), round(ry, 3), round(rz, 3), 'top', round(top, 3))

# 1) delete the old helmet above the cut
bm = bmesh.new()
bm.from_mesh(me)
kill = [f for f in bm.faces if f.calc_center_median().z > CUT]
bmesh.ops.delete(bm, geom=kill, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
uvl = bm.loops.layers.uv.active
print('removed faces', len(kill))


def add_obj(bm_, name):
    m = bpy.data.meshes.new(name)
    bm_.to_mesh(m)
    bm_.free()
    o = bpy.data.objects.new(name, m)
    bpy.context.scene.collection.objects.link(o)
    return o


def rim_z(y):
    f = max(0.0, min(1.0, (c.y - y) / ry + 0.30))
    return 0.935 + 0.075 * f


# 2) road-helmet shell: elongated dome, tapered tail; vent slots are smooth recessed channels coloured black (no boolean)
hb = bmesh.new()
NA, NE = 128, 32
SLOTS = (  # u0, drift with w, w centre, half length, half width (normalised top-down coords)
    (0.24, 0.05, 0.02, 0.72, 0.085), (0.54, 0.14, 0.06, 0.68, 0.090), (0.82, 0.10, 0.14, 0.52, 0.085),
    (0.14, 0.00, -0.86, 0.14, 0.065), (0.00, 0.00, 0.82, 0.15, 0.075), (0.34, 0.02, 0.80, 0.13, 0.060))


def slot_depth(u, w):
    """0 outside any slot .. 1 deep inside; smooth ramp so the recess edge is rounded."""
    au = abs(u)
    best = 0.0
    for (u0, dr, wc, hl, hw) in SLOTS:
        t = (w - wc) / hl
        if abs(t) >= 1.0:
            continue
        prof = math.sqrt(1.0 - t * t)
        half = hw * (0.55 + 0.75 * prof) * prof ** 0.5 + 0.004
        d = abs(au - (u0 + dr * w)) / half
        if d < 1.0:
            x = 1.0 - d
            best = max(best, x * x * (3 - 2 * x))
    return best


grid = []
dep = []
for j in range(NA):
    al = 2 * math.pi * (j - 0.5) / NA
    dx, dy = math.sin(al), -math.cos(al)
    yr = c.y + dy * ry * 0.92
    th_rim = math.acos(max(-1.0, min(1.0, (rim_z(yr) - c.z) / rz)))
    col, dcol = [], []
    for e in range(NE + 1):
        th = th_rim * e / NE
        x = dx * rx * math.sin(th)
        y = dy * ry * math.sin(th)
        z = rz * math.cos(th)
        if y > 0:                                   # tapered tail: longer and lower at the back
            w_ = y / ry
            y *= 1.0 + 0.16 * w_
            z -= 0.030 * w_ * w_
            x *= 1.0 - 0.10 * w_ * w_
        u_, w2 = x / rx, y / ry
        ramp = max(0.0, min(1.0, (e - 4) / 5.0)) * max(0.0, min(1.0, ((NE - 3) - e) / 5.0))
        ramp = ramp * ramp * (3 - 2 * ramp)
        d_ = slot_depth(u_, w2) * ramp
        pos = Vector((x, y, z))
        inward = -pos.normalized()
        col.append(hb.verts.new(c + pos + inward * (d_ * 0.026)))
        dcol.append(d_)
    grid.append(col)
    dep.append(dcol)
black_faces = []
for j in range(NA):
    j2 = (j + 1) % NA
    for e in range(NE):
        f = hb.faces.new((grid[j][e], grid[j2][e], grid[j2][e + 1], grid[j][e + 1]))
        f.smooth = True
        if (dep[j][e] + dep[j2][e] + dep[j2][e + 1] + dep[j][e + 1]) / 4 > 0.62:
            f.material_index = 1
            black_faces.append(f.index)
shell = add_obj(hb, 'HelmetShell')
bpy.context.view_layer.objects.active = shell
BLACKSET = set(black_faces)
print('shell tris', sum(len(p_.vertices) - 2 for p_ in shell.data.polygons), 'black slot faces', len(black_faces))
# rim lip
lb = bmesh.new()
ring = []
for i in range(64):
    a_ = 2 * math.pi * i / 64
    dx, dy = math.sin(a_), -math.cos(a_)
    y = c.y + dy * ry * 0.935
    ring.append(Vector((c.x + dx * rx * 0.935, y, rim_z(y) + 0.003)))
rows = []
for p_ in ring:
    d = (p_ - Vector((c.x, c.y, p_.z))).normalized()
    up = Vector((0, 0, 1))
    rows.append([lb.verts.new(p_ + d * (0.011 * math.cos(2 * math.pi * s_ / 8)) + up * (0.012 * math.sin(2 * math.pi * s_ / 8))) for s_ in range(8)])
for i in range(len(rows)):
    i2 = (i + 1) % len(rows)
    for s_ in range(8):
        lb.faces.new((rows[i][s_], rows[i][(s_ + 1) % 8], rows[i2][(s_ + 1) % 8], rows[i2][s_]))
lip = add_obj(lb, 'HelmetRim')
# brow ridge across the front of the rim + chin straps
def tube(name, pts, rx_, ry_, segs=8):
    tb = bmesh.new()
    n = len(pts)
    rows = []
    sv = Vector((1, 0, 0))
    for i in range(n):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        sd = sv - t * sv.dot(t)
        if sd.length < 1e-5:
            sd = Vector((0, 1, 0)) - t * t.y
        sd.normalize()
        sv = sd
        up = t.cross(sd).normalized()
        rows.append([tb.verts.new(pts[i] + sd * math.cos(2 * math.pi * k / segs) * rx_ + up * math.sin(2 * math.pi * k / segs) * ry_) for k in range(segs)])
    for i in range(n - 1):
        for k in range(segs):
            tb.faces.new((rows[i][k], rows[i][(k + 1) % segs], rows[i + 1][(k + 1) % segs], rows[i + 1][k]))
    return add_obj(tb, name)


brow_pts = []
for i in range(17):
    a_ = math.radians(-60 + 120 * i / 16)
    dx, dy = math.sin(a_), -math.cos(a_)
    y = c.y + dy * ry * 0.95
    brow_pts.append(Vector((c.x + dx * rx * 0.95, y, rim_z(y) + 0.020)))
brow = tube('HelmetBrow', brow_pts, 0.020, 0.010)
straps = []
for sgn in (-1, 1):
    pts = [Vector((c.x + sgn * rx * 0.94, c.y - 0.02, rim_z(c.y - 0.02))), Vector((c.x + sgn * 0.185, c.y - 0.01, 0.90)),
           Vector((c.x + sgn * 0.14, c.y - 0.09, 0.81)), Vector((c.x + sgn * 0.045, c.y - 0.165, 0.755))]
    straps.append(tube('HelmetStrap', pts, 0.008, 0.0028, 6))
# 3) dark hair dome under the shell so the windows read as dark depth, not as a hole to the skull
db = bmesh.new()
bmesh.ops.create_icosphere(db, subdivisions=3, radius=1.0)
for v in db.verts:
    v.co = Vector((cx, cy, 0.985)) + Vector((v.co.x * (rx - 0.036), v.co.y * (ry - 0.036), v.co.z * (rz - 0.030)))
bmesh.ops.bisect_plane(db, geom=list(db.verts) + list(db.edges) + list(db.faces), dist=1e-6, plane_co=(0, 0, 0.975), plane_no=(0, 0, 1), clear_inner=True)
dome = add_obj(db, 'HairDome')

# 4) UVs onto flat atlas texels, then merge everything back into one mesh
bm.to_mesh(me)
bm.free()
for o, uv in [(shell, DARK), (lip, GREY), (dome, BLACK), (brow, DARK)] + [(st, BLACK) for st in straps]:
    o.data.uv_layers.new(name='UVMap')
    for poly in o.data.polygons:
        for li in poly.loop_indices:
            o.data.uv_layers.active.data[li].uv = uv
    for p in o.data.polygons:
        p.use_smooth = True
# slot faces black
for poly in shell.data.polygons:
    if poly.material_index == 1:
        for li in poly.loop_indices:
            shell.data.uv_layers.active.data[li].uv = BLACK
    poly.material_index = 0
# ribs that are 'stripe' ribs: faces on the two central ribs get the grey texel
sd = shell.data
for poly in sd.polygons:
    cen = poly.center
    if abs(cen.x - cx) < 0.032 and cen.z > 1.05:
        for li in poly.loop_indices:
            sd.uv_layers.active.data[li].uv = GREY
for o in [shell, lip, dome, brow] + straps:
    o.data.materials.clear()
    o.data.materials.append(ob.data.materials[0])
bpy.ops.object.select_all(action='DESELECT')
for o in [ob, shell, lip, dome, brow] + straps:
    o.select_set(True)
bpy.context.view_layer.objects.active = ob
bpy.ops.object.join()
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB')
print('WROTE', OUT)

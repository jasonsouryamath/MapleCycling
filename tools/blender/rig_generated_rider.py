"""
Skin a generated rider onto the donor 24-bone armature by transferring weights from the donor's own sculpt.
  blender -b -P tools/blender/rig_generated_rider.py -- <donor_rigged.glb> <textured_generated.glb> <out.glb> [--xshift 0.045 --headwiden 1.25]
The donor mesh is aligned to the generated one first (same centring + head widening the cleanup uses), weights are copied by
nearest surface, then limited to 4 influences, smoothed and normalised. Armature is the donor's, untouched.
"""
import bpy, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
DONOR, GEN, OUT = argv[:3]
opt = {argv[i][2:]: argv[i + 1] for i in range(3, len(argv) - 1) if argv[i].startswith('--')}
XS = float(opt.get('xshift', '0.045'))
HK = float(opt.get('headwiden', '1.25'))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=DONOR)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
old = [o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.vertices) > 10000][0]
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o is not old:
        bpy.data.objects.remove(o, do_unlink=True)
old.name = 'DonorMesh'
old.parent = None
# align donor copy to the generated proportions
bpy.ops.object.select_all(action='DESELECT')
for v in old.data.vertices:
    x = v.co.x + XS
    if v.co.z > 0.80:
        t = max(0.0, min(1.0, (v.co.z - 0.80) / 0.14))
        k = 1.0 + (HK - 1.0) * t * t * (3 - 2 * t)
        hx = -0.043 + XS
        x = hx + (x - hx) * k
    v.co.x = x

before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=GEN)
gen = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
gen.name = 'AkihiroBody'
gen.parent = None
bpy.context.view_layer.update()
for bone in arm.data.bones:
    if bone.name not in gen.vertex_groups:
        gen.vertex_groups.new(name=bone.name)

METHOD = opt.get('method', 'geodesic')
if METHOD == 'geodesic':
    import numpy as np, subprocess, os, tempfile
    names = [b.name for b in arm.data.bones]
    me = gen.data
    me.calc_loop_triangles()
    verts = np.array([tuple(v.co) for v in me.vertices], np.float64)
    faces = np.array([tuple(t.vertices) for t in me.loop_triangles], np.int64)
    heads = np.array([tuple(arm.data.bones[n].head_local) for n in names])
    tails = np.array([tuple(arm.data.bones[n].tail_local) for n in names])
    # fit the arm chains to where this mesh's arms really are (weights only; the deforming rig stays the donor's)
    lat_all = np.abs(verts[:, 0] - 0.011)
    def arm_centroid(sgn, z0, z1, latmin):
        m = (verts[:, 2] >= z0) & (verts[:, 2] < z1) & (sgn * (verts[:, 0] - 0.011) > 0) & (lat_all > latmin)
        return verts[m].mean(0) if m.sum() > 10 else None
    for S_, sgn in (('Left', 1), ('Right', -1)):
        T_ = arm_centroid(sgn, 0.335, 0.385, 0.20)
        W_ = arm_centroid(sgn, 0.42, 0.46, 0.20)
        E_ = arm_centroid(sgn, 0.50, 0.54, 0.17)
        if T_ is None or W_ is None or E_ is None:
            print('arm fit skipped', S_, T_ is None, W_ is None, E_ is None)
            continue
        sh = np.array(arm.data.bones[S_ + 'Arm'].head_local)
        pts = {S_ + 'Arm': (sh, E_), S_ + 'ForeArm': (E_, W_), S_ + 'Hand': (W_, T_)}
        for nme, (h_, t_) in pts.items():
            k_ = names.index(nme)
            heads[k_], tails[k_] = h_, t_
        print('fitted', S_, 'elbow', E_.round(3), 'wrist', W_.round(3), 'tip', T_.round(3))
    def leg_centroid(sgn, z0, z1, latmax=0.215, ymin=-9, ymax=9):
        m = (verts[:, 2] >= z0) & (verts[:, 2] < z1) & (sgn * (verts[:, 0] - 0.011) > 0.01) & (lat_all < latmax) & (verts[:, 1] > ymin) & (verts[:, 1] < ymax)
        return verts[m].mean(0) if m.sum() > 10 else None
    for S_, sgn in (('Left', 1), ('Right', -1)):
        hip = np.array(arm.data.bones[S_ + 'UpLeg'].head_local)
        knee = leg_centroid(sgn, 0.19, 0.25)
        ank = leg_centroid(sgn, 0.09, 0.13)
        ball = leg_centroid(sgn, 0.0, 0.05, ymax=ank[1] - 0.05 if ank is not None else 0)
        if knee is None or ank is None or ball is None:
            print('leg fit skipped', S_)
            continue
        tip = ball + np.array([0.0, -0.05, -0.01])
        for nme, (h_, t_) in {S_ + 'UpLeg': (hip, knee), S_ + 'Leg': (knee, ank), S_ + 'Foot': (ank, ball), S_ + 'ToeBase': (ball, tip)}.items():
            k_ = names.index(nme)
            heads[k_], tails[k_] = h_, t_
        print('fitted leg', S_, 'knee', knee.round(3), 'ankle', ank.round(3), 'ball', ball.round(3))
    tmp = tempfile.mkdtemp()
    fin, fout = os.path.join(tmp, 'in.npz'), os.path.join(tmp, 'out.npz')
    np.savez(fin, verts=verts, faces=faces, names=np.array(names), heads=heads, tails=tails)
    script = os.path.join(os.path.dirname(os.path.abspath(__file__)) if '__file__' in globals() else 'tools/blender', 'geodesic_weights.py')
    if not os.path.exists(script):
        script = os.path.abspath('tools/blender/geodesic_weights.py')
    py = opt.get('py', 'C:/Users/jason/.pyenv/pyenv-win/versions/3.11.9/python.exe')
    r = subprocess.run([py, script, fin, fout, '--seed', opt.get('seed', '0.03'), '--p', opt.get('p', '3')], capture_output=True, text=True)
    print(r.stdout[-400:], r.stderr[-400:])
    W = np.load(fout)['W']
    nv = len(verts)
    for n in names:
        if n not in gen.vertex_groups:
            gen.vertex_groups.new(name=n)
    for k, n in enumerate(names):
        vg = gen.vertex_groups[n]
        for vi in np.nonzero(W[:, k] > 1e-3)[0]:
            vg.add([int(vi)], float(W[vi, k]), 'REPLACE')
    print('GEODESIC weights done, verts', nv)
    bpy.data.objects.remove(old, do_unlink=True)
    gen.parent = arm
    gen.matrix_parent_inverse = arm.matrix_world.inverted()
    am = gen.modifiers.new('Armature', 'ARMATURE')
    am.object = arm
    bpy.ops.object.select_all(action='DESELECT')
    gen.select_set(True)
    arm.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_skins=True, export_animations=False)
    print('WROTE', OUT)
    sys.exit(0)
if METHOD == 'analytic':
    import numpy as np
    def sst(x):
        x = np.clip(x, 0.0, 1.0)
        return x * x * (3 - 2 * x)
    names = [b.name for b in arm.data.bones]
    col = {n: k for k, n in enumerate(names)}
    H = {n: np.array(arm.data.bones[n].head_local, np.float64) for n in names}
    T = {n: np.array(arm.data.bones[n].tail_local, np.float64) for n in names}
    co = np.array([tuple(v.co) for v in gen.data.vertices], np.float64)
    nv = len(co)
    CX = float(opt.get('cx', '0.011'))

    def chain(bones, blend=0.035):
        segs = [(H[b], T[b]) for b in bones]
        lens = [np.linalg.norm(t - h) for h, t in segs]
        cum = np.concatenate([[0.0], np.cumsum(lens)])
        best = np.full(nv, 1e9)
        bi = np.zeros(nv, int)
        bt = np.zeros(nv)
        for i, (h, t) in enumerate(segs):
            d = t - h
            tt = np.clip(((co - h) @ d) / max(d @ d, 1e-12), 0, 1)
            dist = np.linalg.norm(co - (h + tt[:, None] * d), axis=1)
            upd = dist < best
            best[upd] = dist[upd]
            bi[upd] = i
            bt[upd] = tt[upd]
        spar = cum[bi] + bt * np.array(lens)[bi]
        W = np.zeros((nv, len(names)))
        for k, b in enumerate(bones):
            lo = sst((spar - (cum[k] - blend)) / (2 * blend)) if k > 0 else np.ones(nv)
            hi = sst((spar - (cum[k + 1] - blend)) / (2 * blend)) if k < len(bones) - 1 else np.zeros(nv)
            W[:, col[b]] = np.maximum(0.0, lo - hi)
        W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
        return W, best

    z = co[:, 2]
    lat = np.abs(co[:, 0] - CX)
    Wbody, _ = chain(['Hips', 'Spine02', 'Spine01', 'Spine', 'neck'], 0.04)
    # legs
    Wl, _ = chain(['LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase'], 0.04)
    Wr, _ = chain(['RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase'], 0.04)
    leg_amt = sst((0.545 - z) / 0.07) * sst((lat - 0.012) / 0.035)
    Wleg = np.where((co[:, 0] > CX)[:, None], Wl, Wr)
    W = (1 - leg_amt)[:, None] * Wbody + leg_amt[:, None] * Wleg
    # arms
    for S, sgn in (('Left', 1), ('Right', -1)):
        Wa, dist = chain([S + 'Shoulder', S + 'Arm', S + 'ForeArm', S + 'Hand'], 0.035)
        side_ok = (sgn * (co[:, 0] - CX)) > 0
        amt = sst((lat - float(opt.get('armx', '0.125'))) / 0.022) * side_ok * sst((z - 0.30) / 0.05)
        W = (1 - amt)[:, None] * W + amt[:, None] * Wa
    # head
    head_amt = sst((z - 0.790) / 0.05)
    Wh = np.zeros_like(W)
    Wh[:, col['Head']] = 1.0
    W = (1 - head_amt)[:, None] * W + head_amt[:, None] * Wh
    # top 4, normalise
    order = np.argsort(-W, axis=1)
    mask = np.zeros_like(W, bool)
    mask[np.arange(nv)[:, None], order[:, :4]] = True
    W = np.where(mask, W, 0)
    W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
    for n in names:
        if n not in gen.vertex_groups:
            gen.vertex_groups.new(name=n)
    for k, n in enumerate(names):
        vg = gen.vertex_groups[n]
        idxs = np.nonzero(W[:, k] > 1e-3)[0]
        for vi in idxs:
            vg.add([int(vi)], float(W[vi, k]), 'REPLACE')
    print('ANALYTIC weights done, verts', nv)
    bpy.data.objects.remove(old, do_unlink=True)
    gen.parent = arm
    gen.matrix_parent_inverse = arm.matrix_world.inverted()
    am = gen.modifiers.new('Armature', 'ARMATURE')
    am.object = arm
    bpy.ops.object.select_all(action='DESELECT')
    gen.select_set(True)
    arm.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_skins=True, export_animations=False)
    print('WROTE', OUT)
    sys.exit(0)
if METHOD == 'auto':
    # bone-heat automatic weights on the watertight generated mesh
    bpy.ops.object.select_all(action='DESELECT')
    gen.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    zero = sum(1 for v in gen.data.vertices if not any(g.weight > 1e-4 for g in v.groups))
    print('AUTO verts', len(gen.data.vertices), 'without weights', zero)
    bpy.data.objects.remove(old, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    gen.select_set(True)
    arm.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_skins=True, export_animations=False)
    print('WROTE', OUT)
    sys.exit(0)
bpy.context.view_layer.objects.active = gen
gen.select_set(True)
mod = gen.modifiers.new('xfer', 'DATA_TRANSFER')
mod.object = old
mod.use_vert_data = True
mod.data_types_verts = {'VGROUP_WEIGHTS'}
mod.vert_mapping = 'POLYINTERP_NEAREST'
mod.layers_vgroup_select_src = 'ALL'
mod.layers_vgroup_select_dst = 'NAME'
bpy.ops.object.modifier_apply(modifier='xfer')

# clean the weights: graph-smooth the weight matrix, keep the strongest 4, normalise
import numpy as np
names = [b.name for b in arm.data.bones]
idx = {n: gen.vertex_groups[n].index for n in names}
nv = len(gen.data.vertices)
W = np.zeros((nv, len(names)), np.float32)
gi = {idx[n]: k for k, n in enumerate(names)}
for v in gen.data.vertices:
    for g in v.groups:
        k = gi.get(g.group)
        if k is not None:
            W[v.index, k] = g.weight
ea = np.array([e.vertices[0] for e in gen.data.edges])
eb = np.array([e.vertices[1] for e in gen.data.edges])
deg = np.zeros(nv, np.float32)
np.add.at(deg, ea, 1)
np.add.at(deg, eb, 1)
deg[deg == 0] = 1
for it in range(int(opt.get('wsmooth', '10'))):
    acc = np.zeros_like(W)
    np.add.at(acc, ea, W[eb])
    np.add.at(acc, eb, W[ea])
    W = 0.5 * W + 0.5 * acc / deg[:, None]
# anatomical rules: arms cannot pull the torso, legs cannot pull anything above the hips, trunk bones cannot pull the outer arm
co = np.array([tuple(v.co) for v in gen.data.vertices], np.float32)
cx = 0.011
lat = np.abs(co[:, 0] - cx)
col = {n: k for k, n in enumerate(names)}
arm_cols = [col[n] for n in names if any(t in n for t in ('Arm', 'ForeArm', 'Hand'))]
shoulder_cols = [col[n] for n in names if 'Shoulder' in n]
leg_cols = [col[n] for n in names if any(t in n for t in ('UpLeg', 'Leg', 'Foot', 'ToeBase'))]
trunk_cols = [col[n] for n in names if n in ('Hips', 'Spine', 'Spine01', 'Spine02')]
torso_zone = (lat < 0.115) & (co[:, 2] > 0.45) & (co[:, 2] < 0.82)
for k in arm_cols + shoulder_cols:
    W[torso_zone, k] = 0
W[co[:, 2] > 0.60][:, leg_cols] = 0
hi = co[:, 2] > 0.60
for k in leg_cols:
    W[hi, k] = 0
outer = lat > 0.17
for k in trunk_cols:
    W[outer & (co[:, 2] > 0.30), k] = 0
low = co[:, 2] < 0.40
for k in arm_cols:
    W[low & (lat < 0.14), k] = 0
# rows that lost everything: nearest bone segment
bad = np.nonzero(W.sum(1) < 1e-4)[0]
if len(bad):
    segs = [(Vector(arm.data.bones[n].head_local), Vector(arm.data.bones[n].tail_local)) for n in names]
    for vi in bad:
        p_ = Vector(co[vi])
        best = None
        for k, (h, t) in enumerate(segs):
            dd = t - h
            tt = max(0.0, min(1.0, (p_ - h).dot(dd) / max(dd.length_squared, 1e-9)))
            dist = (p_ - (h + dd * tt)).length
            if best is None or dist < best[0]:
                best = (dist, k)
        W[vi, best[1]] = 1.0
for it in range(4):                                   # re-smooth after the cuts
    acc = np.zeros_like(W)
    np.add.at(acc, ea, W[eb])
    np.add.at(acc, eb, W[ea])
    W = 0.6 * W + 0.4 * acc / deg[:, None]
    for k in arm_cols + shoulder_cols:
        W[torso_zone, k] = 0
    for k in leg_cols:
        W[hi, k] = 0
order = np.argsort(-W, axis=1)
mask = np.zeros_like(W, bool)
rows = np.arange(nv)[:, None]
mask[rows, order[:, :4]] = True
W = np.where(mask, W, 0)
W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
allv = list(range(nv))
for n in names:
    gen.vertex_groups[n].remove(allv)
for k, n in enumerate(names):
    vg = gen.vertex_groups[n]
    for vi in np.nonzero(W[:, k] > 1e-3)[0]:
        vg.add([int(vi)], float(W[vi, k]), 'REPLACE')
zero = sum(1 for v in gen.data.vertices if not any(g.weight > 1e-4 for g in v.groups))
print('verts', len(gen.data.vertices), 'without weights', zero)

bpy.data.objects.remove(old, do_unlink=True)
gen.parent = arm
gen.matrix_parent_inverse = arm.matrix_world.inverted()
am = gen.modifiers.new('Armature', 'ARMATURE')
am.object = arm
bpy.ops.object.select_all(action='DESELECT')
gen.select_set(True)
arm.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_skins=True, export_animations=False)
print('WROTE', OUT)

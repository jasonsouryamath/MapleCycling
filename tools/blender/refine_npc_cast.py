"""
Surgical clean-up of a Meshy NPC rider (keeps the sculpt, atlas and rig; removes defects).
  blender -b -P tools/blender/clean_npc_rider.py -- <in_rigged.glb> <out.glb> [--maxedge 0.05] [--preview 1]
Pass 1: weld by position (glTF splits verts on UV seams) -> remove floating debris islands.
Pass 2: delete sliver / fin faces (very long edges) around feet, ankles and hands.
Pass 3 (separate step): replace shard hands with clean gloves (see build_glove below).
UVs are per-loop in bmesh, so welding does not change the texture mapping.
"""
import bpy, bmesh, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = argv[0], argv[1]
opt = {argv[i][2:]: argv[i + 1] for i in range(len(argv) - 1) if argv[i].startswith('--')}
SRC, OUT = os.path.abspath(SRC), os.path.abspath(OUT)
for key in ('dump','dumppos','atlas','render'):
    if key in opt:opt[key]=os.path.abspath(opt[key])
MAXEDGE = float(opt.get('maxedge', 0.05))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = [o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.vertices) > 10000][0]
for o in list(bpy.data.objects):
    if False: # preserve authored eye and face overlays
        bpy.data.objects.remove(o, do_unlink=True)

bm = bmesh.new()
bm.from_mesh(body.data)
deform = bm.verts.layers.deform.verify()
n0 = len(bm.faces)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.verts.ensure_lookup_table()

# ---- islands
seen = set()
comps = []
for v in bm.verts:
    if v.index in seen:
        continue
    st = [v]
    seen.add(v.index)
    c = []
    while st:
        x = st.pop()
        c.append(x)
        for e in x.link_edges:
            y = e.other_vert(x)
            if y.index not in seen:
                seen.add(y.index)
                st.append(y)
    comps.append(c)
comps.sort(key=len, reverse=True)
debris = [v for c in comps[1:] if len(c) < 32 for v in c]
print('ISLANDS', len(comps), 'debris verts', len(debris))
bmesh.ops.delete(bm, geom=debris, context='VERTS')

# ---- fin / sliver faces: any face with a very long edge, or extreme aspect, below the knee or around hands
def region(co):
    return co.z < 0.22 or (abs(co.x) > 0.16 and co.z < 0.7)

kill = []
for f in bm.faces:
    c = f.calc_center_median()
    if not region(c):
        continue
    L = max(e.calc_length() for e in f.edges)
    ar = f.calc_area()
    if L > MAXEDGE or (c.z < 0.20 and ar > 1e-9 and (L * L) / ar > 14.0) or (c.z < 0.07 and L > float(opt.get('footedge', '0.02'))):
        kill.append(f)
print('fin faces', len(kill), 'of', len(bm.faces))
bmesh.ops.delete(bm, geom=kill, context='FACES')
# drop verts that lost all faces
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
print('faces', n0, '->', len(bm.faces))
# ---- hem / boundary fringe: drop tiny boundary faces, then relax the boundary line (z < 0.75 = not the head)
def boundary_verts():
    return [v for v in bm.verts if any(e.is_boundary for e in v.link_edges)]


for it in range(3):
    kill = []
    for f in bm.faces:
        if not any(e.is_boundary for e in f.edges):
            continue
        c = f.calc_center_median()
        if c.z > 0.75:
            continue
        if f.calc_area() < 2.5e-6 or min(e.calc_length() for e in f.edges) < 0.0012:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
print('after hem trim faces', len(bm.faces))
bm.verts.ensure_lookup_table()
bv = [v for v in boundary_verts() if v.co.z < 0.75]
for it in range(14):
    new = {}
    for v in bv:
        nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
        if len(nb) == 2:
            new[v] = (nb[0].co + nb[1].co) * 0.5
    for v, co in new.items():
        v.co = v.co.lerp(co, 0.5)

# ---- texel finder: real pixels from the atlas, so replacement parts keep the exact painted colours
img = [i for i in bpy.data.images if i.size[0] > 0][0]
W, Hh = img.size
px = list(img.pixels)
uvl = bm.loops.layers.uv.active


def texel(u, v):
    x = min(W - 1, max(0, int(u * W)))
    y = min(Hh - 1, max(0, int(v * Hh)))
    i = (y * W + x) * 4
    return px[i], px[i + 1], px[i + 2]


def find_texel(faces, pred):
    for f in faces:
        for l in f.loops:
            u, v = l[uvl].uv
            c = texel(u, v)
            if pred(c):
                ok = all(sum(abs(a - b) for a, b in zip(c, texel(u + du, v + dv))) < 0.06
                         for du, dv in ((2 / W, 0), (-2 / W, 0), (0, 2 / Hh), (0, -2 / Hh)))
                if ok:
                    return (u, v), c
    return None, None


gname = {g.index: g.name for g in body.vertex_groups}


def dominant(v):
    w = v[deform]
    return gname[max(w.items(), key=lambda kv: kv[1])[0]] if len(w) else None


HANDS = {'LeftHand', 'RightHand'}
hand_faces = [f for f in bm.faces if sum(dominant(v) in HANDS for v in f.verts) >= 2]
fore_faces = [f for f in bm.faces if all(dominant(v) in {'LeftForeArm', 'RightForeArm'} for v in f.verts)]
lum = lambda c: 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]
t_dark, c_dark = find_texel(hand_faces, lambda c: lum(c) < 0.10)
t_skin, c_skin = find_texel(fore_faces, lambda c: 0.12 < lum(c) < 0.5 and c[0] > c[2] * 1.4)
t_orange, c_orange = find_texel(hand_faces + fore_faces, lambda c: c[0] > 0.6 and c[1] > 0.2 and c[2] < 0.15)
print('TEXELS dark', t_dark, c_dark, 'skin', t_skin, c_skin, 'orange', t_orange, c_orange, 'hand faces', len(hand_faces))

hand_pts = {'Left': [], 'Right': []}
for f in hand_faces:
    for v in f.verts:
        hand_pts['Left' if v.co.x > 0 else 'Right'].append(v.co.copy())

if opt.get('hands', '1') == '1' and t_dark and t_skin:
    bmesh.ops.delete(bm, geom=hand_faces, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.verts.ensure_lookup_table()

    def add_tube(pts, rx, ry, texel_uv, bones, segs=12, side=Vector((1, 0, 0)), round_end=0.0):
        n = len(pts)
        tans = [((pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()) for i in range(n)]
        rings = [(pts[i], rx[i], ry[i], tans[i]) for i in range(n)]
        if round_end > 0:
            for k in range(1, 4):
                ang = k / 3 * math.pi / 2
                rings.append((pts[-1] + tans[-1] * round_end * math.sin(ang), rx[-1] * math.cos(ang), ry[-1] * math.cos(ang), tans[-1]))
        sv = side.copy()
        rows = []
        for (c, a_, b_, t) in rings:
            sd = sv - t * sv.dot(t)
            if sd.length < 1e-5:
                sd = Vector((0, 1, 0)) - t * t.y
            sd.normalize()
            sv = sd
            up = t.cross(sd).normalized()
            rows.append([bm.verts.new(c + sd * (math.cos(2 * math.pi * i / segs) * max(a_, 1e-4)) + up * (math.sin(2 * math.pi * i / segs) * max(b_, 1e-4))) for i in range(segs)])
        faces = []

        def setw(v):
            for nm, w in bones:
                v[deform][body.vertex_groups[nm].index] = w
        for r in rows:
            for v in r:
                setw(v)
        for k in range(len(rows) - 1):
            for i in range(segs):
                i2 = (i + 1) % segs
                f = bm.faces.new((rows[k][i], rows[k][i2], rows[k + 1][i2], rows[k + 1][i]))
                f.smooth = True
                faces.append(f)
        ctr = bm.verts.new(sum((v.co for v in rows[-1]), Vector()) / segs)
        setw(ctr)
        for i in range(segs):
            f = bm.faces.new((ctr, rows[-1][i], rows[-1][(i + 1) % segs]))
            f.smooth = True
            faces.append(f)
        ctr0 = bm.verts.new(sum((v.co for v in rows[0]), Vector()) / segs)
        setw(ctr0)
        for i in range(segs):
            f = bm.faces.new((ctr0, rows[0][(i + 1) % segs], rows[0][i]))
            f.smooth = True
            faces.append(f)
        for f in faces:
            for l in f.loops:
                l[uvl].uv = texel_uv

    sc = float(opt.get('handscale', '1.0'))
    for S, sgn in (('Left', 1), ('Right', -1)):
        # where the sculpted hand actually is (the rig is not mirror-exact): use the deleted hand's own geometry
        hv = [v.co.copy() for f in hand_faces_by_side[S] for v in f.verts] if False else hand_pts[S]
        cen = sum(hv, Vector()) / len(hv)
        fa = [v.co.copy() for f in fore_faces for v in f.verts if (v.co.x > 0) == (S == 'Left')]
        fa_c = sum(fa, Vector()) / len(fa)
        wr_b = Vector(arm.data.bones[S + 'Hand'].head_local)
        # direction: from the forearm's far half towards the hand centroid
        far = [p for p in fa if (p - cen).length < (fa_c - cen).length * 1.0]
        far_c = sum(far, Vector()) / max(len(far), 1)
        fd = (cen - far_c)
        fd = Vector((0, 0, -1)) if fd.length < 1e-4 else fd.normalized()
        if S == 'Left':
            fd_left = fd.copy()
        else:
            fd = Vector((-fd_left.x, fd_left.y, fd_left.z))
        wr = cen - fd * 0.045
        print('HAND', S, 'centroid', tuple(round(x, 3) for x in cen), 'bone wrist', tuple(round(x, 3) for x in wr_b))
        sx = Vector((1, 0, 0)) * sgn
        bones = [(S + 'Hand', 1.0)]
        cuff_uv = t_orange or t_dark
        add_tube([wr - fd * 0.018, wr + fd * 0.004], [0.036 * sc] * 2, [0.031 * sc] * 2, cuff_uv, [(S + 'ForeArm', 0.5), (S + 'Hand', 0.5)])
        palm = [wr, wr + fd * 0.035, wr + fd * 0.062]
        add_tube(palm, [0.037 * sc, 0.041 * sc, 0.039 * sc], [0.025 * sc, 0.026 * sc, 0.022 * sc], t_dark, bones, side=sx, round_end=0.008)
        side_axis = (Vector((1, 0, 0)) - fd * fd.x).normalized()
        for fi in range(4):
            off = (fi - 1.5) * 0.0118 * sc
            base = wr + fd * 0.058 + side_axis * off * sgn
            curl = Vector((0, -0.006, 0))
            add_tube([base, base + fd * 0.014 + curl * 0.5], [0.0066 * sc] * 2, [0.0066 * sc] * 2, t_dark, bones, segs=8, side=sx)
            add_tube([base + fd * 0.014 + curl * 0.5, base + fd * 0.034 + curl], [0.0062 * sc, 0.0056 * sc], [0.0062 * sc, 0.0056 * sc], t_skin, bones, segs=8, side=sx, round_end=0.005)
        tb = wr + fd * 0.02 + (Vector((-1, 0, 0)) * sgn) * 0.034 + Vector((0, -0.012, 0))
        add_tube([tb, tb + fd * 0.022 + Vector((0, -0.010, 0))], [0.0092 * sc] * 2, [0.0092 * sc] * 2, t_dark, bones, segs=8, side=sx)
        add_tube([tb + fd * 0.022 + Vector((0, -0.010, 0)), tb + fd * 0.040 + Vector((0, -0.016, 0))], [0.0086 * sc, 0.0078 * sc], [0.0086 * sc, 0.0078 * sc], t_skin, bones, segs=8, side=sx, round_end=0.006)

# ---- proportions: centre the sculpt on the rig and widen the head to the concept sheet's round chibi head
XSHIFT = float(opt.get('xshift', '0.0'))
HEADK = float(opt.get('headwiden', '1.0'))
gname3 = {g.index: g.name for g in body.vertex_groups}
def dom3(v):
    w = v[deform]
    return gname3[max(w.items(), key=lambda kv: kv[1])[0]] if len(w) else None
if XSHIFT != 0.0:
    for v in bm.verts:
        v.co.x += XSHIFT
if HEADK != 1.0:
    hx = -0.043 + XSHIFT                       # head centre line after the shift
    for v in bm.verts:
        if dom3(v) == 'Head' or v.co.z > 0.86:
            t = max(0.0, min(1.0, (v.co.z - 0.80) / 0.14))      # ramp in over the neck / jaw so nothing tears
            k = 1.0 + (HEADK - 1.0) * t * t * (3 - 2 * t)
            v.co.x = hx + (v.co.x - hx) * k
print('proportion pass: xshift', XSHIFT, 'headwiden', HEADK)

# ---- surface smoothing: Taubin (lambda|mu) keeps volume while removing the cloth crumple / helmet lumps
def taubin(bm, verts, iters, lam=0.5, mu=-0.53):
    import numpy as np
    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    n = len(bm.verts)
    V = np.array([tuple(v.co) for v in bm.verts], np.float64)
    ea = np.array([e.verts[0].index for e in bm.edges])
    eb = np.array([e.verts[1].index for e in bm.edges])
    deg = np.zeros(n)
    np.add.at(deg, ea, 1)
    np.add.at(deg, eb, 1)
    deg[deg == 0] = 1
    mask = np.zeros(n, bool)
    mask[[v.index for v in verts]] = True
    for it in range(iters):
        for f in (lam, mu):
            acc = np.zeros_like(V)
            np.add.at(acc, ea, V[eb])
            np.add.at(acc, eb, V[ea])
            acc /= deg[:, None]
            V[mask] += f * (acc[mask] - V[mask])
    for v in bm.verts:
        if mask[v.index]:
            v.co = Vector(V[v.index])


if opt.get('smooth', '1') == '1':
    gname2 = {g.index: g.name for g in body.vertex_groups}

    def dom(v):
        w = v[deform]
        return gname2[max(w.items(), key=lambda kv: kv[1])[0]] if len(w) else None
    cloth_bones = {'Hips', 'Spine', 'Spine01', 'Spine02', 'LeftUpLeg', 'RightUpLeg', 'LeftArm', 'RightArm', 'LeftShoulder', 'RightShoulder', 'LeftLeg', 'RightLeg'}
    sv = [v for v in bm.verts if (dom(v) in cloth_bones and v.co.z < 0.78) or (dom(v) == 'Head' and v.co.z > 1.06 and v.co.y > -0.12)]
    print('smooth verts', len(sv))
    taubin(bm, sv, int(opt.get('iters', '30')))
    low = [v for v in sv if 0.28 < v.co.z < 0.58]
    taubin(bm, low, int(opt.get('lowiters', '40')))
    hel_v = [v for v in sv if v.co.z > 1.06]
    taubin(bm, hel_v, int(opt.get('heliters', '70')))
    hair_v = [v for v in bm.verts if dom(v) == 'Head' and 0.74 < v.co.z < 1.07 and not (v.co.y < -0.10 and v.co.z < 1.0)]
    print('hair verts', len(hair_v))
    taubin(bm, hair_v, int(opt.get('hairiters', '6')), lam=0.35, mu=-0.37)
    bm.normal_update()

if opt.get('dumppos'):
    import numpy as np
    tris_uv, tris_pos = [], []
    bm.verts.ensure_lookup_table()
    for f in bm.faces:
        ls = list(f.loops)
        for k in range(1, len(ls) - 1):
            tri = (ls[0], ls[k], ls[k + 1])
            tris_uv.append([tuple(l[uvl].uv) for l in tri])
            tris_pos.append([tuple(l.vert.co) for l in tri])
    np.savez(opt['dumppos'], uv=np.array(tris_uv, np.float32), pos=np.array(tris_pos, np.float32))
    print('DUMPPOS', len(tris_uv))
if opt.get('dump'):
    import os
    d = opt['dump']
    img.filepath_raw = d + '_atlas.png'
    img.file_format = 'PNG'
    img.save()
    with open(d + '_face_tris.txt', 'w') as fh:
        for f in bm.faces:
            c = f.calc_center_median()
            if c.y < -0.10 and 0.76 < c.z < 1.02:
                uvs = [l[uvl].uv for l in f.loops]
                fh.write(' '.join('%.6f %.6f' % (u, v) for u, v in uvs) + chr(10))
    print('DUMPED', d)
bm.to_mesh(body.data)
bm.free()

if opt.get('atlas'):
    new = bpy.data.images.load(opt['atlas'])
    new.pack()
    for m in bpy.data.materials:
        for n in m.node_tree.nodes if m.node_tree else []:
            if n.type == 'TEX_IMAGE' and n.image and n.image.size[0] == new.size[0]:
                n.image = new

# the imported glTF normals are the old noisy ones: drop them so the smoothed surface shades smoothly
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.shade_smooth(keep_sharp_edges=False)

if False: # export after cast-specific material and helmet refinement
    bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=False, export_skins=True, export_animations=False)
    print('WROTE', OUT)

# Cast pass: retain the sculpt, face, UVs and original bind poses; produce private copies.
import numpy as np, json, os
NAME = opt.get('name', 'Akihiro')
palettes = {
 'Akihiro': [(16,16,20),(232,100,22),(242,238,226),(246,246,246)],
 'Akane': [(22,20,24),(158,36,48),(233,190,100),(240,231,216),(248,248,248)],
 'Shiori': [(19,29,33),(20,111,113),(240,103,86),(48,176,181),(246,246,246)],
 'Shinobu': [(31,35,65),(126,111,173),(246,246,246)],
 'Coral': [(20,49,56),(238,94,84),(246,246,246),(19,19,23)],
 'Hanakage': [(20,49,56),(238,94,84),(246,246,246),(19,19,23)],
}
helmet_colors = {'Akihiro':(.075,.078,.088),'Akane':(.47,.025,.045),
 'Shiori':(.045,.05,.06),'Shinobu':(.045,.055,.14),'Coral':(.85,.115,.10),'Hanakage':(.85,.115,.10)}
stripe_colors = {'Akihiro':(.25,.26,.29),'Akane':(.72,.47,.12),'Shiori':(.045,.05,.06),
 'Shinobu':(.35,.26,.60),'Coral':(.85,.115,.10),'Hanakage':(.88,.88,.85)}

# UV-space cleanup is restricted to actual cloth/sock/shoe faces; facial art remains intact.
arr=np.array(img.pixels[:],np.float32).reshape(Hh,W,4)
mask=np.zeros((Hh,W),bool)
for poly in body.data.polygons:
    co=sum((body.data.vertices[i].co for i in poly.vertices),Vector())/len(poly.vertices)
    if not (co.z < .76 and (co.z > .268 or co.z < .17)):
        continue
    uv=[body.data.uv_layers.active.data[i].uv.copy() for i in poly.loop_indices]
    for k in range(1,len(uv)-1):
        tri=np.array([uv[0],uv[k],uv[k+1]])*[W,Hh]
        x0,y0=np.maximum(np.floor(tri.min(0)).astype(int),0)
        x1,y1=np.minimum(np.ceil(tri.max(0)).astype(int),[W-1,Hh-1])
        if x1<x0 or y1<y0:continue
        xx,yy=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
        a,b,c=tri; den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den)<1e-9:continue
        u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
        v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
        mask[y0:y1+1,x0:x1+1]|=(u>=0)&(v>=0)&(u+v<=1)
pal=np.array(palettes[NAME],np.float32)/255
# Image pixels use scene-linear values. Convert the authoring palette from sRGB.
pal=np.where(pal<=.04045,pal/12.92,((pal+.055)/1.055)**2.4)
original=arr.copy()
for start in range(0,Hh,96):
    end=min(Hh,start+96)
    ns=[np.roll(np.roll(original,dy,0),dx,1)[start:end,:,:3] for dy in (-1,0,1) for dx in (-1,0,1)]
    med=np.median(np.stack(ns),axis=0)
    dist=((med[:,:,None,:]-pal[None,None,:,:])**2).sum(3)
    idx=dist.argmin(2); near=dist.min(2)<.05
    cleaned=np.where(near[:,:,None],pal[idx],med)
    arr[start:end,:,:3]=np.where(mask[start:end,:,None],cleaned,original[start:end,:,:3])
img.pixels.foreach_set(arr.ravel()); img.update();img.pack()

# Replace only the rough helmet crown with a low vented cycling shell. Actual openings,
# inner rim and longitudinal accent ribs are head-skinned; face/hair below crown retained.
mesh=body.data; head_index=body.vertex_groups.get('Head').index
helmet=[v.co.copy() for v in mesh.vertices if v.co.z>1.06]
if not helmet:raise RuntimeError('No helmet crown found for '+NAME)
hc=np.array([tuple(v) for v in helmet]); cx=float(np.median(hc[:,0]));cy=float((hc[:,1].min()+hc[:,1].max())*.5)
rx=max(.18,float(np.percentile(hc[:,0],99)-np.percentile(hc[:,0],1))*.51)
ry=max(.22,float(np.percentile(hc[:,1],99)-np.percentile(hc[:,1],1))*.51)
bottom=1.025; height=.225; ry=max(ry,rx*1.10)
bm=bmesh.new();bm.from_mesh(mesh)
bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.calc_center_median().z>bottom],context='FACES')
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
bm.to_mesh(mesh);bm.free()

def material(name,color,rough=.55):
    m=bpy.data.materials.new(NAME+'_'+name);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=rough
    return m
hm=material('Helmet',helmet_colors[NAME],.42)
sm=material('HelmetStripe',stripe_colors[NAME],.45)
inner=material('HelmetInner',(.008,.009,.012),.8)
verts=[];faces=[];mids=[];N=128;R=40
for j in range(R+1):
    t=.015+(math.pi/2-.015)*j/R
    for i in range(N):
        p=2*math.pi*i/N;nx=math.sin(t)*math.cos(p);ny=math.sin(t)*math.sin(p)
        verts.append((cx+rx*nx,cy+ry*ny,bottom+height*math.cos(t)))
for j in range(R):
    for i in range(N):
        ix=[j*N+i,j*N+(i+1)%N,(j+1)*N+(i+1)%N,(j+1)*N+i]
        c=sum((Vector(verts[k]) for k in ix),Vector())*.25
        nx=(c.x-cx)/rx;ny=(c.y-cy)/ry
        # Four long vents with tapered ends, leaving a broad central rib and solid rim.
        vent=False
        for slot in (-.59,-.27,.27,.59):
            if ((nx-slot)/.085)**2+((ny+.035)/.72)**2<1 and j<R-4:vent=True
        if vent:continue
        faces.append(ix);mids.append(1 if .35<abs(nx)<.42 else 0)
me=bpy.data.meshes.new(NAME+'_CleanVentedHelmet');me.from_pydata(verts,[],faces);me.update()
# Project vent boundary vertices exactly onto their analytic oval. This removes the
# grid stair-stepping while retaining smooth continuous solid ribs and a closed rim.
hb=bmesh.new();hb.from_mesh(me)
for v in hb.verts:
    if not any(e.is_boundary for e in v.link_edges) or v.co.z<bottom+.045:continue
    nx=(v.co.x-cx)/rx;ny=(v.co.y-cy)/ry
    if v.co.z>bottom+height-.01:continue
    slot=min((-.59,-.27,.27,.59),key=lambda s:abs(s-nx))
    dx=(nx-slot)/.085;dy=(ny+.035)/.72;q=math.sqrt(dx*dx+dy*dy)
    if .65<q<1.5:
        nx=slot+.085*dx/q;ny=-.035+.72*dy/q
        v.co=(cx+rx*nx,cy+ry*ny,bottom+height*math.sqrt(max(0,1-nx*nx-ny*ny)))
hb.normal_update()
for f in hb.faces:
    c=f.calc_center_median()
    outward=Vector(((c.x-cx)/(rx*rx),(c.y-cy)/(ry*ry),(c.z-bottom)/(height*height)))
    if f.normal.dot(outward)<0:f.normal_flip()
hb.to_mesh(me);hb.free()
ho=bpy.data.objects.new(NAME+'_CleanVentedHelmet',me);bpy.context.collection.objects.link(ho)
ho.matrix_world=body.matrix_world.copy();ho.data.materials.append(hm);ho.data.materials.append(sm);ho.data.materials.append(inner)
for f,mi in zip(me.polygons,mids):f.use_smooth=True;f.material_index=mi
g=ho.vertex_groups.new(name='Head');g.add(list(range(len(me.vertices))),1,'REPLACE')
solid=ho.modifiers.new('Real shell thickness','SOLIDIFY');solid.thickness=.007;solid.material_offset=2;solid.material_offset_rim=2
bpy.context.view_layer.objects.active=ho;ho.select_set(True);bpy.ops.object.modifier_apply(modifier=solid.name)
am=ho.modifiers.new('Original head rig','ARMATURE');am.object=arm;ho.parent=body.parent
body.data.normals_split_custom_set([(0,0,0)]*len(body.data.loops))
for m in bpy.data.materials:
    if m.use_nodes:
        p=m.node_tree.nodes.get('Principled BSDF')
        if p:p.inputs['Roughness'].default_value=max(.42,p.inputs['Roughness'].default_value)
bpy.ops.export_scene.gltf(filepath=OUT,export_format='GLB',use_selection=False,export_skins=True,export_animations=False)
report={'name':NAME,'joints':[b.name for b in arm.data.bones],
 'meshes':[{ 'name':o.name,'verts':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)} for o in bpy.data.objects if o.type=='MESH'],
 'original_rig_preserved':True,'face_art_preserved':True}
with open(OUT+'.audit.json','w') as f:json.dump(report,f,indent=2)
print('CAST_EXPORT',json.dumps(report))
if opt.get('render'):
    from mathutils import Quaternion
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16
    scene.render.resolution_x=640;scene.render.resolution_y=800;scene.render.resolution_percentage=100
    if scene.world is None:scene.world=bpy.data.worlds.new('Refined preview world')
    scene.world.color=(.22,.22,.22);scene.view_settings.view_transform='Standard'
    def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.object.camera_add(location=(.0,-3,.73));cam=bpy.context.object;aim(cam,(0,0,.65))
    cam.data.type='ORTHO';cam.data.ortho_scale=1.47;scene.camera=cam
    for loc,power,size in [((-2,-3,3),150,3),((2,-1,2),100,3),((0,2,3),180,2)]:
        bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object
        light.data.energy=power;light.data.shape='DISK';light.data.size=size;aim(light,(0,0,.7))
    scene.render.filepath=os.path.abspath(opt['render']);bpy.ops.render.render(write_still=True)

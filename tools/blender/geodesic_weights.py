"""
Geodesic skin weights (system Python, scipy). Input npz: verts (n,3), faces (m,3), names, heads (b,3), tails (b,3), skip mask.
Each bone's seed = vertices within `seed` of its segment; weight ~ 1/(geodesic distance + eps)^p over the mesh surface,
so limbs that rest against the torso do not bleed into it. Head region is forced by height. Output npz: W (n,b).
  python geodesic_weights.py in.npz out.npz [--seed 0.03] [--p 3]
"""
import sys
import numpy as np
import scipy.sparse as sp
from scipy.sparse.csgraph import dijkstra

inp, outp = sys.argv[1], sys.argv[2]
opt = {sys.argv[i][2:]: float(sys.argv[i + 1]) for i in range(3, len(sys.argv) - 1) if sys.argv[i].startswith('--')}
SEED, P = opt.get('seed', 0.03), opt.get('p', 3.0)
d = np.load(inp, allow_pickle=True)
V, F = d['verts'], d['faces']
names = list(d['names'])
Hd, Tl = d['heads'], d['tails']
skip = set(['head_end', 'headfront', 'neutral_bone'])
# weld vertices that share a position (glTF splits them at UV seams) so the surface graph is connected
Vfull = V
key = np.round(V * 1e5).astype(np.int64)
_, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
inv = inv.reshape(-1)
V = V[first]
F = inv[F]
nfull = len(Vfull)
print('welded', nfull, '->', len(V))
n = len(V)
e = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
w = np.linalg.norm(V[e[:, 0]] - V[e[:, 1]], axis=1)
G = sp.coo_matrix((w, (e[:, 0], e[:, 1])), shape=(n, n)).tocsr()
G = G.maximum(G.T)
D = np.full((n, len(names)), 1e3, np.float64)
for k, nm in enumerate(names):
    if nm in skip:
        continue
    h, t = Hd[k], Tl[k]
    dd = t - h
    tt = np.clip(((V - h) @ dd) / max(dd @ dd, 1e-12), 0, 1)
    dist = np.linalg.norm(V - (h + tt[:, None] * dd), axis=1)
    # adaptive: the surface of a thick limb is ~its radius away from the bone axis
    rad = np.partition(dist, 40)[40] * float(opt.get('k', 1.18)) + 0.004
    seeds = np.nonzero(dist < max(SEED * 0.5, rad))[0]
    if len(seeds) == 0:
        seeds = np.array([int(np.argmin(dist))])
    gd = dijkstra(G, directed=False, indices=seeds, min_only=True)
    D[:, k] = np.where(np.isfinite(gd), gd, 1e3)
    print(nm, 'seeds', len(seeds))
# vertices on disconnected islands are unreachable over the surface: fall back to straight-line distance to the bone
unreached = np.nonzero(D.min(1) >= 999)[0]
print('unreached verts', len(unreached))
for k, nm in enumerate(names):
    if nm in skip:
        continue
    h, t = Hd[k], Tl[k]
    dd = t - h
    P_ = V[unreached]
    tt = np.clip(((P_ - h) @ dd) / max(dd @ dd, 1e-12), 0, 1)
    D[unreached, k] = np.linalg.norm(P_ - (h + tt[:, None] * dd), axis=1)
W = 1.0 / (D + 0.012) ** P
for k, nm in enumerate(names):
    if nm in skip:
        W[:, k] = 0
# head by height (the head bone core only covers a thin axis)
z = V[:, 2]
ht = np.clip((z - 0.78) / 0.06, 0, 1)
ht = ht * ht * (3 - 2 * ht)
kh = names.index('Head')
W /= np.maximum(W.sum(1, keepdims=True), 1e-12)
W *= (1 - ht)[:, None]
W[:, kh] += ht
# top-4
order = np.argsort(-W, axis=1)
m = np.zeros_like(W, bool)
m[np.arange(n)[:, None], order[:, :4]] = True
W = np.where(m, W, 0)
W /= np.maximum(W.sum(1, keepdims=True), 1e-12)
W = W[inv]
np.savez(outp, W=W.astype(np.float32))
print('wrote', outp)

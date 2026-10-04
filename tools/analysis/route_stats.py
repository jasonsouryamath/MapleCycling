import json, math

P = r"C:\Users\jason\OneDrive\Desktop\MapleRide\Assets\Environment\SakuraPass\SakuraRoute.json"
d = json.load(open(P))
print("top-level keys:", list(d.keys()))
for k, v in d.items():
    if isinstance(v, list):
        print(f"  {k}: list len={len(v)} first={json.dumps(v[0])[:200]}")
    else:
        print(f"  {k} = {v}")

samples = d["samples"]
print("\nsamples:", len(samples))
print("sample[0]:", json.dumps(samples[0])[:400])
print("fields:", sorted(samples[0].keys()))

pts = [tuple(s["p"]) for s in samples]
dd = [s["d"] for s in samples]
ys = [p[1] for p in pts]
total = dd[-1]
print(f"\narc length (baked d): {total:.2f} m")
print(f"elev min {min(ys):.2f} max {max(ys):.2f} start {ys[0]:.2f} end {ys[-1]:.2f}")
gain = sum(max(0.0, ys[i] - ys[i-1]) for i in range(1, len(ys)))
loss = sum(max(0.0, ys[i-1] - ys[i]) for i in range(1, len(ys)))
print(f"cumulative gain {gain:.1f} m  loss {loss:.1f} m")
imax = ys.index(max(ys))
print(f"summit at sample {imax}, d={dd[imax]:.1f} m, y={ys[imax]:.2f}")
xs = [p[0] for p in pts]; zs = [p[2] for p in pts]
print(f"X {min(xs):.1f}..{max(xs):.1f}   Z {min(zs):.1f}..{max(zs):.1f}")
print(f"mean sample spacing {total/(len(pts)-1):.2f} m")

print("\n25 m bin grades:")
b = 0.0; i0 = 0
while b < total - 1:
    tgt = min(b + 25.0, total)
    i1 = min(range(len(dd)), key=lambda j: abs(dd[j] - tgt))
    run = math.dist((pts[i0][0], pts[i0][2]), (pts[i1][0], pts[i1][2]))
    g = (ys[i1] - ys[i0]) / run * 100 if run else 0
    print(f"  {dd[i0]:7.1f}-{dd[i1]:7.1f} m : {g:6.2f}%   y {ys[i0]:6.1f} -> {ys[i1]:6.1f}")
    b = tgt; i0 = i1

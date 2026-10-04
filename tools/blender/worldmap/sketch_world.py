"""
M1 - labelled planning sketch of the MapleRide world (docs/world_sketch.png).

    python tools/blender/worldmap/sketch_world.py

Run world_terrain.py first. This is a flat, top-down hillshaded plot of the generated terrain
with every region, river, road, bridge and ferry labelled. It is the planning drawing that
docs/WORLD_GEOGRAPHY.md refers to, NOT the in-game map art (that is build_world_relief.py).
"""
import json
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.patheffects
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(HERE, "out")
L = json.load(open(os.path.join(ROOT, "Assets", "Resources", "World", "world_layout.json")))
R = json.load(open(os.path.join(OUT, "roads.json")))
meta = json.load(open(os.path.join(OUT, "meta.json")))
W, H, res = meta["W"], meta["H"], meta["res"]

h = np.load(os.path.join(OUT, "land_m.npy"))
alb = np.asarray(Image.open(os.path.join(OUT, "albedo.png")).convert("RGB"), np.float32) / 255
alb = np.flipud(alb)
k = alb.shape[0] / h.shape[0]
from scipy import ndimage
surf = ndimage.zoom(np.load(os.path.join(OUT, "surf_m.npy")), k, order=1)
gy, gx = np.gradient(surf * 4.0, res / k * 1000)
az, el = np.radians(315), np.radians(38)
lx, ly, lz = np.cos(el) * np.cos(az), np.cos(el) * np.sin(az), np.sin(el)
nz = 1 / np.sqrt(1 + gx * gx + gy * gy)
shade = np.clip((-gx * lx - gy * ly + lz) * nz, 0, 1)
img = alb * (0.35 + 0.8 * shade[..., None])
img = np.clip(img, 0, 1)

fig, ax = plt.subplots(figsize=(19, 12.5), dpi=110)
x0, y0 = meta.get("x0", 0.0), meta.get("y0", 0.0)
ax.imshow(img, origin="lower", extent=[x0, x0 + (h.shape[1] - 1) * res, y0, y0 + (h.shape[0] - 1) * res],
          interpolation="bilinear")

def xy(flat):
    a = np.asarray(flat).reshape(-1, 2)
    return a[:, 0], a[:, 1]

for rd in R["roads"]:
    ax.plot(*xy(rd["pts"]), color="#fff6d8", lw=1.3, alpha=0.9)
for b in R["bridges"]:
    ax.plot(*xy(b["pts"]), color="#ffffff", lw=2.4)
for f in R["ferries"]:
    ax.plot(*xy(f["pts"]), color="#ffffff", lw=1.0, ls=(0, (3, 3)), alpha=0.8)
for rt in R["routes"]:
    ax.plot(*xy(rt["pts"]), color="#ff4a2a" if rt["status"] == "built" else "#ffb030", lw=2.0)
for rv in L["rivers"]:
    x, y = xy(rv["pts"])
    ax.text(x[len(x) // 2], y[len(y) // 2], rv["name"], color="#bfe8ff", fontsize=7, style="italic")
for rg in L["ranges"]:
    x, y = xy(rg["pts"])
    ax.text(x.mean(), y.mean() + 4, rg["name"].upper(), color="#fff", fontsize=8, alpha=0.8,
            ha="center", weight="bold")
for v in L["volcanoes"] + L["calderas"]:
    ax.text(v["c"][0], v["c"][1] + 3, v["name"], color="#ffe", fontsize=7, ha="center")
for c in L["sea_cuts"]:
    x, y = xy(c["poly"])
    ax.text(x.mean(), y.mean(), c["name"], color="#9fd4ff", fontsize=10 if c["name"] == "Minato Bay" else 6.5, style="italic", ha="center")
for r in L["regions"]:
    x, y = r["pos"]
    col = {"built": "#e8322a", "planned": "#ff9a1a", "locked": "#9aa0aa"}[r["status"]]
    ax.plot(x, y, "o", ms=9, mfc=col, mec="white", mew=1.6)
    ax.text(x + 2.5, y + 1.5, r["name"], color="white", fontsize=9, weight="bold",
            path_effects=[matplotlib.patheffects.withStroke(linewidth=2.5, foreground="black")])
    ax.text(x + 2.5, y - 3.0, f"{r['tagline']}  .  {r['summit'][2]:,} m", color="#eee",
            fontsize=6.5,
            path_effects=[matplotlib.patheffects.withStroke(linewidth=2, foreground="black")])
ax.plot([20, 70], [12, 12], color="white", lw=3)
ax.text(45, 14, "50 km", color="white", ha="center", fontsize=9)
ax.set_xlim(0, W)
ax.set_ylim(0, H)
ax.set_title("Hondo - the MapleRide world (M1 planning sketch, km)  .  red = built, orange = planned (B5), "
             "grey = locked  .  cream = roads, dashed = ferries", fontsize=11)
ax.set_xlabel("km east")
ax.set_ylabel("km north")
os.makedirs(os.path.join(ROOT, "docs"), exist_ok=True)
fig.tight_layout()
fig.savefig(os.path.join(ROOT, "docs", "world_sketch.png"))
print("wrote docs/world_sketch.png")

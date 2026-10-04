"""
Sakura Pass expanded-route planning map.

Draws the BUILT route (straight from SakuraRoute.json) together with the PROPOSED expansion
segments from route_expansion_plan.py, the landmark/checkpoint set, the elevation profile of
the long course, the course/duration budget, and a 1:1 render of the in-game GPS window.

    python tools/analysis/make_route_map.py
"""

import math
import os

from PIL import Image, ImageDraw, ImageFilter

import gps_widget as G
from gps_widget import (GOLD, INK, INK_SOFT, LAKE, LAKE_DEEP, MOSS, MOSS_DEEP, PAPER,
                        PAPER_DEEP, SAKURA, SAKURA_DEEP, SLATE, SNOW, VERMILION, VIOLET,
                        dashed_line, elevation_strip, font, marker_pin, rounded, text)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT_DIRS = [os.path.join(REPO, "Assets", "Environment", "SakuraPass",
                         "Concepts"),
            os.path.join(REPO, "good_graphics", "design")]
NAME = "SakuraPass_Expansion_RouteMap.png"

W, H = 2560, 1760
plan = G.load_plan()
P = plan["polylines"]

img = Image.new("RGBA", (W, H), PAPER)
d = ImageDraw.Draw(img)

# paper grain
grain = Image.effect_noise((W, H), 12).convert("L").filter(ImageFilter.GaussianBlur(0.6))
img = Image.composite(Image.new("RGBA", (W, H), PAPER_DEEP), img, grain.point(
    lambda v: 40 if v > 150 else 0))
d = ImageDraw.Draw(img)

f_title = font("segoeuib.ttf", 46)
f_sub = font("segoeui.ttf", 22)
f_h = font("segoeuib.ttf", 26)
f_hs = font("seguisb.ttf", 19)
f_b = font("segoeui.ttf", 18)
f_bs = font("segoeui.ttf", 16)
f_pin = font("seguisb.ttf", 17)
f_pin_s = font("seguisb.ttf", 15)
f_num = font("segoeuib.ttf", 24)

# ---------------------------------------------------------------- header
d.rectangle([0, 0, W, 104], fill=(52, 41, 47))
d.rectangle([0, 104, W, 110], fill=VERMILION)
text(d, (48, 34), "SAKURA PASS — STAGED ROUTE EXPANSION", f_title, fill=(243, 234, 220))
text(d, (W - 48, 30), "MapleRide  ·  planning map  ·  Unity world space (X / Z, metres)",
     f_sub, fill=(214, 196, 176), anchor="ra")
uniq = plan["courses"]["C3 Gran Fondo (unique)"]["km"]
text(d, (W - 48, 62),
     f"built route 1 378.8 m  →  proposed network {uniq:.2f} km unique  ·  target ride 30–60 min",
     f_sub, fill=SAKURA, anchor="ra")

# ---------------------------------------------------------------- map panel
MAP = (40, 140, 980, 1700)
rounded(d, MAP, 18, fill=(236, 240, 230), outline=(168, 150, 140), width=3)

MW, MH = MAP[2] - MAP[0], MAP[3] - MAP[1]
mlayer = Image.new("RGBA", (MW, MH), (0, 0, 0, 0))
md = ImageDraw.Draw(mlayer)

WX0, WX1 = -640.0, 360.0
WZ0, WZ1 = -1250.0, 1020.0
k = min((MW - 60) / (WX1 - WX0), (MH - 60) / (WZ1 - WZ0))
cx_w, cz_w = (WX0 + WX1) / 2, (WZ0 + WZ1) / 2


def Wp(p):
    """Unity (x, y, z) -> map-layer pixel. +Z is north = up."""
    return (MW / 2 + (p[0] - cx_w) * k, MH / 2 - (p[2] - cz_w) * k)


# --- lake basin: between the pass road's waterline and the far-shore return road
lake = [Wp((p[0] + 105.0, 0, p[2])) for p in P["Pass Ascent (built)"]]
lake += [Wp((p[0] + 105.0, 0, p[2])) for p in P["Fuji Descent (built)"]]
lake += [Wp((p[0] - 40.0, 0, p[2])) for p in P["S1 Kawabe Lakeshore Return"][::-1]]
md.polygon(lake, fill=LAKE)
md.line(lake + [lake[0]], fill=LAKE_DEEP, width=2)

# --- terrain footprints
cur = [Wp((-285, 0, -295)), Wp((190, 0, -295)), Wp((190, 0, 900)), Wp((-285, 0, 900))]
dashed_line(md, cur + [cur[0]], MOSS_DEEP, width=2, dash=12, gap=9)
text(md, (cur[1][0] - 10, cur[1][1] - 10), "terrain today  475 × 1195 m", f_pin_s,
     fill=MOSS_DEEP, anchor="rd")
t = plan["terrain"]
nxt = [Wp((t["min_x"], 0, t["min_z"])), Wp((t["max_x"], 0, t["min_z"])),
       Wp((t["max_x"], 0, t["max_z"])), Wp((t["min_x"], 0, t["max_z"]))]
md.line(nxt + [nxt[0]], fill=(150, 132, 128), width=2)
text(md, (nxt[3][0] + 10, nxt[3][1] + 10),
     f"terrain required  {t['max_x']-t['min_x']:.0f} × {t['max_z']-t['min_z']:.0f} m"
     f"  ({t['area_vs_today']:.1f}× today)", f_pin_s, fill=(120, 104, 100))

# --- Mt Fuji: the hero backdrop sits 700 m off the map at (615, 992); drawn as a bearing
#     icon rather than at true scale, which would swamp the route it exists to frame.
fend = Wp(P["Fuji Descent (built)"][-1])
fic = (MW - 96, 92)
dashed_line(md, [fend, fic], (150, 136, 176), width=3, dash=12, gap=10)
md.polygon([(fic[0], fic[1] - 44), (fic[0] - 52, fic[1] + 34), (fic[0] + 52, fic[1] + 34)],
           fill=VIOLET)
md.polygon([(fic[0], fic[1] - 44), (fic[0] - 19, fic[1] - 15), (fic[0] - 9, fic[1] - 21),
            (fic[0], fic[1] - 12), (fic[0] + 10, fic[1] - 20), (fic[0] + 19, fic[1] - 15)],
           fill=SNOW)
text(md, (fic[0], fic[1] + 42), "Mt. Fuji  (615, 992)", f_pin_s, fill=(86, 74, 96),
     anchor="ma")

# --- lake village (built): x 164..294, z -70..250
v0, v1 = Wp((164, 0, 250)), Wp((294, 0, -70))
md.rectangle([v0[0], v0[1], v1[0], v1[1]], fill=(206, 188, 166), outline=(150, 132, 118))
text(md, (v1[0] + 8, (v0[1] + v1[1]) / 2), "Lake\nVillage", f_pin_s, fill=(110, 94, 100),
     anchor="lm")

# --- route lines
ROUTES = [
    ("Pass Ascent (built)", VERMILION, 9, False, "BUILT"),
    ("Fuji Descent (built)", VERMILION, 9, False, "BUILT"),
    ("S1 Kawabe Lakeshore Return", SAKURA_DEEP, 8, True, "S1"),
    ("S2a Aozora Ascent", (92, 128, 176), 8, True, "S2"),
    ("S2b Skyline Traverse", (92, 128, 176), 8, True, "S2"),
    ("S3 Maple City Road", GOLD, 8, True, "S3"),
]
for name, col, wdt, dash, _tag in ROUTES:
    pix = [Wp(p) for p in P[name]]
    md.line(pix, fill=(255, 255, 255), width=wdt + 6, joint="curve")
    if dash:
        dashed_line(md, pix, col, width=wdt, dash=22, gap=13)
    else:
        md.line(pix, fill=col, width=wdt, joint="curve")

# --- checkpoints / landmarks
LANDMARKS = [
    ("Pass Ascent (built)", 0.00, "Ride Gate  ·  km 0.0", VERMILION, "right"),
    ("Pass Ascent (built)", 0.16, "Shrine Torii", VERMILION, "right"),
    ("Pass Ascent (built)", 0.29, "Lakeside Overlook", VERMILION, "right"),
    ("Pass Ascent (built)", 0.55, "Cliff Tunnel  300–342 m", VERMILION, "left"),
    ("Pass Ascent (built)", 0.70, "Hillside Sign", VERMILION, "right"),
    ("Pass Ascent (built)", 1.00, "Summit Crest  +36.8 m", VERMILION, "right"),
    ("Fuji Descent (built)", 0.17, "Descent Overlook", VERMILION, "right"),
    ("Fuji Descent (built)", 0.30, "Hairpin Twins", VERMILION, "left"),
    ("Fuji Descent (built)", 1.00, "Fuji Foot  ·  km 1.38", VERMILION, "left"),
    ("S1 Kawabe Lakeshore Return", 0.28, "Minamo Causeway", SAKURA_DEEP, "right"),
    ("S1 Kawabe Lakeshore Return", 0.62, "Lake Village aid stop", SAKURA_DEEP, "right"),
    ("S1 Kawabe Lakeshore Return", 0.84, "Kawabe Bridge", SAKURA_DEEP, "left"),
    ("S2a Aozora Ascent", 0.02, "Aozora Junction", (92, 128, 176), "right"),
    ("S2a Aozora Ascent", 0.55, "Switchback Field", (92, 128, 176), "left"),
    ("S2a Aozora Ascent", 1.00, "Cloudline Shrine  +180 m", (92, 128, 176), "right"),
    ("S2b Skyline Traverse", 0.42, "Skyline Col", (92, 128, 176), "right"),
    ("S3 Maple City Road", 1.00, "Maple City Gate", GOLD, "right"),
]
for seg, f, label, col, side in LANDMARKS:
    pts_ = P[seg]
    p = Wp(pts_[min(len(pts_) - 1, int(f * (len(pts_) - 1)))])
    marker_pin(md, p, label, f_pin_s, fill=col, r=8, side=side)

# rider position sample on the map (same one the GPS window shows)
rp = P["Pass Ascent (built)"]
ri = int(0.30 * (len(rp) - 1))
a, b = Wp(rp[ri]), Wp(rp[min(len(rp) - 1, ri + 2)])
G.rider_chevron(md, a, math.degrees(math.atan2(b[0] - a[0], -(b[1] - a[1]))), size=13)

# --- north rose + scale bar
nx, ny = 70, 74
md.ellipse([nx - 30, ny - 30, nx + 30, ny + 30], fill=PAPER, outline=INK_SOFT, width=2)
md.polygon([(nx, ny - 22), (nx - 11, ny + 12), (nx, ny + 4), (nx + 11, ny + 12)],
           fill=VERMILION)
text(md, (nx, ny + 22), "N", font("segoeuib.ttf", 16), fill=INK, anchor="mm")
bx1 = 40
bx2 = bx1 + 500 * k
by = MH - 34
md.line([(bx1, by), (bx2, by)], fill=INK, width=4)
for bxx in (bx1, bx2, (bx1 + bx2) / 2):
    md.line([(bxx, by - 8), (bxx, by + 8)], fill=INK, width=3)
text(md, ((bx1 + bx2) / 2, by - 14), "500 m", f_pin_s, fill=INK, anchor="ms")
text(md, (24, 16), "north up  ·  built route solid  ·  proposed dashed", f_pin_s,
     fill=INK_SOFT)

# clip the whole map layer to the rounded panel and composite
mask = Image.new("L", (MW, MH), 0)
ImageDraw.Draw(mask).rounded_rectangle([2, 2, MW - 3, MH - 3], radius=16, fill=255)
img.paste(mlayer, (MAP[0], MAP[1]), Image.composite(
    mlayer.split()[3], Image.new("L", (MW, MH), 0), mask))
d = ImageDraw.Draw(img)
rounded(d, MAP, 18, fill=None, outline=(168, 150, 140), width=3)

# ---------------------------------------------------------------- GPS widget panel
GP = (1020, 140, 1620, 900)
text(d, (GP[0], GP[1] - 34), "IN-GAME GPS WINDOW  (RouteMapHud, 1:1)", f_h, fill=INK)
G.draw_gps_widget(img, GP, plan,
                  course=["Pass Ascent (built)", "Fuji Descent (built)",
                          "S1 Kawabe Lakeshore Return"],
                  progress=0.11, lap=(2, 4), course_label="SAKURA CIRCUIT", scale=1.05)
d = ImageDraw.Draw(img)

notes = [
    "course line · ridden portion in vermilion        rider chevron · heading from CyclingPhysics",
    "checkpoint pins · next one named in gold        distance / ascent / to-go / next-CP readouts",
    "elevation band · live gradient badge · the same RouteDefinition the physics samples",
]
for i, n in enumerate(notes):
    yy = GP[3] + 22 + i * 28
    d.ellipse([GP[0], yy + 2, GP[0] + 12, yy + 14], fill=VERMILION)
    text(d, (GP[0] + 24, yy), n, f_bs, fill=INK)

# ---------------------------------------------------------------- legend + stages
LG = (1660, 140, 2520, 900)
rounded(d, LG, 16, fill=(248, 242, 232), outline=(180, 162, 150), width=2)
text(d, (LG[0] + 24, LG[1] + 20), "STAGES", f_h, fill=INK)

S = plan["segments"]
stage_rows = [
    ("BUILT", VERMILION, False, "Pass Ascent + Fuji Descent",
     f"{(S['Pass Ascent (built)']['length_m']+S['Fuji Descent (built)']['length_m'])/1000:.2f} km"
     f" · +{S['Pass Ascent (built)']['gain_m']:.0f} m · "
     f"~{S['Pass Ascent (built)']['minutes']+S['Fuji Descent (built)']['minutes']:.1f} min"
     " · 483 samples, dressed, rendered"),
    ("S1", SAKURA_DEEP, True, "Kawabe Lakeshore Return  → closes the circuit",
     f"{S['S1 Kawabe Lakeshore Return']['length_m']/1000:.2f} km · "
     f"±{S['S1 Kawabe Lakeshore Return']['gain_m']:.0f} m · "
     f"~{S['S1 Kawabe Lakeshore Return']['minutes']:.1f} min · causeway, village, bridge"),
    ("S2", (92, 128, 176), True, "Aozora Ascent + Skyline Traverse  → figure-eight",
     f"{(S['S2a Aozora Ascent']['length_m']+S['S2b Skyline Traverse']['length_m'])/1000:.2f} km"
     f" · +{S['S2a Aozora Ascent']['gain_m']:.0f} m · "
     f"~{S['S2a Aozora Ascent']['minutes']+S['S2b Skyline Traverse']['minutes']:.1f} min"
     " · 5 switchbacks, Cloudline Shrine"),
    ("S3", GOLD, True, "Maple City Road  → hub link / warm-up",
     f"{S['S3 Maple City Road']['length_m']/1000:.2f} km each way · "
     f"{S['S3 Maple City Road']['loss_m']:.0f} m down · "
     f"~{S['S3 Maple City Road']['minutes']:.1f} min · neutral zone, Quick Ride spawn"),
]
yy = LG[1] + 66
for tag, col, dash, title, sub in stage_rows:
    if dash:
        dashed_line(d, [(LG[0] + 26, yy + 16), (LG[0] + 96, yy + 16)], col, width=7,
                    dash=16, gap=9)
    else:
        d.line([(LG[0] + 26, yy + 16), (LG[0] + 96, yy + 16)], fill=col, width=7)
    text(d, (LG[0] + 112, yy), tag, f_hs, fill=col)
    text(d, (LG[0] + 176, yy), title, f_hs, fill=INK)
    text(d, (LG[0] + 176, yy + 26), sub, f_bs, fill=INK_SOFT)
    yy += 74

d.line([(LG[0] + 24, yy + 4), (LG[2] - 24, yy + 4)], fill=(206, 190, 172), width=2)
text(d, (LG[0] + 24, yy + 20), "COURSES  ·  minutes at 158 W (0.72 × 220 W FTP, provisional)",
     f_hs, fill=INK)
yy += 56
head = ["course", "km", "gain", "min", "laps → 30 min", "laps → 60 min"]
colx = [LG[0] + 26, LG[0] + 330, LG[0] + 420, LG[0] + 520, LG[0] + 620, LG[0] + 760]
for hx, hh in zip(colx, head):
    text(d, (hx, yy), hh.upper(), f_pin_s, fill=INK_SOFT)
yy += 28
for name, c in plan["courses"].items():
    vals = [name.split(" ", 1)[1], f"{c['km']:.2f}", f"{c['gain_m']:.0f} m",
            f"{c['minutes']:.1f}", f"{30/c['minutes']:.1f} ×", f"{60/c['minutes']:.1f} ×"]
    for hx, vv, i in zip(colx, vals, range(6)):
        text(d, (hx, yy), vv, f_b if i else f_hs,
             fill=INK if i else (VERMILION if "Gran" in name or "Skyline" in name else INK))
    yy += 34

text(d, (LG[0] + 26, yy + 16),
     "Every number above is PROVISIONAL tuning: mass 76.5 kg, CdA 0.32, Crr 0.005,\n"
     "FTP 220 W, descent capped at 58 km/h. A 3.5 W/kg rider finishes ~22 % quicker, so\n"
     "duration is hit by choosing LAPS at run time, never by fixing course length.",
     f_bs, fill=INK_SOFT, spacing=7)

# ---------------------------------------------------------------- elevation profile
EP = (1020, 1070, 2520, 1450)
text(d, (EP[0], EP[1] - 40),
     f"ELEVATION PROFILE  ·  C3 Sakura Gran Fondo ({uniq:.2f} km unique)", f_h, fill=INK)
course = ["S3 Maple City Road", "S3 Maple City Road", "Pass Ascent (built)",
          "S2a Aozora Ascent", "S2b Skyline Traverse", "Fuji Descent (built)",
          "S1 Kawabe Lakeshore Return"]
pts, checks = [], []
for nme in course:
    p = [tuple(v) for v in P[nme]]
    if pts and math.dist(pts[-1], p[0]) > math.dist(pts[-1], p[-1]):
        p = p[::-1]
        flip = True
    else:
        flip = False
    base_i = len(pts)
    for f, label, major in G.CHECKPOINTS.get(nme, []):
        if not major:
            continue
        ff = 1.0 - f if flip else f
        checks.append((base_i + int(ff * (len(p) - 1)), label))
    pts += p
(exx, eyy), grade, (lo, hi, tl) = elevation_strip(
    d, EP, pts, 0.42, f_pin_s, f_num, line=SAKURA_DEEP,
    fill_done=(226, 176, 186), fill_todo=(214, 203, 186),
    bg=(248, 242, 232), border=(180, 162, 150))
dist = [0.0]
for aa, bb in zip(pts, pts[1:]):
    dist.append(dist[-1] + math.dist(aa, bb))
seen = []
for idx, label in sorted(set(checks)):
    frac = dist[min(idx, len(dist) - 1)] / dist[-1]
    xx = EP[0] + 10 + (EP[2] - EP[0] - 20) * frac
    if seen and xx - seen[-1] < 96:
        continue
    seen.append(xx)
    d.line([(xx, EP[1] + 8), (xx, EP[3] - 10)], fill=(198, 182, 166), width=1)
    text(d, (xx, EP[3] + 10), f"{label}\n{dist[idx]/1000:.1f} km", f_pin_s, fill=INK_SOFT,
         anchor="ma", spacing=4)
G.rider_chevron(d, (exx, eyy), 0, size=11)
text(d, (EP[0] + 14, EP[1] + 12), f"{hi:+.0f} m", f_pin_s, fill=INK_SOFT)
text(d, (EP[0] + 14, EP[3] - 26), f"{lo:+.0f} m", f_pin_s, fill=INK_SOFT)

# ---------------------------------------------------------------- footer notes
FT = (1020, 1500, 2520, 1712)
rounded(d, FT, 16, fill=(248, 242, 232), outline=(180, 162, 150), width=2)
text(d, (FT[0] + 24, FT[1] + 14), "PREREQUISITE — the built climb is not rideable yet",
     f_h, fill=VERMILION)
text(d, (FT[0] + 24, FT[1] + 52),
     "SakuraRoute.json delivers its honest 6.7 % average as a 37–40 % wall between 400 and 475 m "
     "(control points 15–19). Re-cut only\nthe y values of control points 0–21 — x/z and the 36.8 m "
     "summit stay exactly where they are — and the climb ramps to a 10.3 %\nmaximum instead. Every "
     "landmark and dressing window is anchored to ClimbLength, so none of them move.",
     f_b, fill=INK, spacing=7)
text(d, (FT[0] + 24, FT[3] - 44),
     "Streaming: terrain already exports as 5 × 14 tiles — the expansion needs 9 × 26 at the same "
     "tile size (284 k quads). Dressing at today's ~1.0\nobject/m would be ~10 500 objects: chunk per "
     "100 m of route, LOD + GPU instance, activate ±400 m around the rider.",
     f_bs, fill=INK_SOFT, spacing=6)

# panel caption for the map lives inside the clipped layer already

out = img.convert("RGB")
for folder in OUT_DIRS:
    os.makedirs(folder, exist_ok=True)
    p = os.path.join(folder, NAME)
    out.save(p, "PNG")
    print(f"wrote {p}")

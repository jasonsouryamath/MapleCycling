"""
In-game GPS / map-window mockup, composited over a REAL Sakura Pass render.

The frame underneath is good_graphics/diag_gate.png - an actual capture from the built scene -
so the widget is shown against the lighting, palette and road it must live on, not against an
invented illustration.

    python tools/analysis/make_gps_mockup.py
"""

import os

from PIL import Image, ImageDraw, ImageFilter

import gps_widget as G
from gps_widget import (GOLD, INK, INK_SOFT, LAKE_DEEP, PAPER, SAKURA, VERMILION, font,
                        marker_pin, rounded, text)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
RENDER = os.path.join(REPO, "good_graphics", "diag_gate.png")
OUT_DIRS = [os.path.join(REPO, "Assets", "Environment", "SakuraPass",
                         "Concepts"),
            os.path.join(REPO, "good_graphics", "design")]
NAME = "SakuraPass_GPS_HUD_Mockup.png"

W, H = 1920, 1080
plan = G.load_plan()

base = Image.open(RENDER).convert("RGBA").resize((W, H), Image.LANCZOS)
# Slight darkening vignette at the corners so UI reads without washing the art out.
vig = Image.new("L", (W, H), 0)
ImageDraw.Draw(vig).rounded_rectangle([-180, -180, W + 180, H + 180], radius=600, fill=90)
base = Image.alpha_composite(
    base, Image.merge("RGBA", (Image.new("L", (W, H), 26), Image.new("L", (W, H), 18),
                               Image.new("L", (W, H), 30),
                               vig.filter(ImageFilter.GaussianBlur(120)))))
d = ImageDraw.Draw(base)

f_cap = font("seguisb.ttf", 15)
f_cap_s = font("seguisb.ttf", 13)
f_big = font("segoeuib.ttf", 54)
f_unit = font("segoeui.ttf", 17)
f_mid = font("segoeuib.ttf", 30)
f_note = font("segoeui.ttf", 16)
f_title = font("segoeuib.ttf", 26)


def panel(box, radius=18, fill=(243, 234, 220, 232), outline=(120, 96, 90, 255)):
    box = tuple(int(v) for v in box)
    layer = Image.new("RGBA", (box[2] - box[0] + 40, box[3] - box[1] + 40), (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(
        [20, 24, box[2] - box[0] + 20, box[3] - box[1] + 20], radius=radius,
        fill=(30, 20, 28, 110))
    base.alpha_composite(layer.filter(ImageFilter.GaussianBlur(12)), (box[0] - 20, box[1] - 20))
    d.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=3)


# --------------------------------------------------------------- telemetry block (1)
tb = (44, 44, 470, 274)
panel(tb)
d.rounded_rectangle([tb[0], tb[1], tb[2], tb[1] + 40], radius=18, fill=(214, 196, 176, 240))
d.rectangle([tb[0], tb[1] + 24, tb[2], tb[1] + 40], fill=(214, 196, 176, 240))
d.line([(tb[0], tb[1] + 40), (tb[2], tb[1] + 40)], fill=VERMILION, width=3)
text(d, (tb[0] + 16, tb[1] + 20), "TELEMETRY  ·  FTMS + HR", f_cap, fill=INK, anchor="lm")
text(d, (tb[2] - 16, tb[1] + 20), "TRAINER ●  HR ●", f_cap_s, fill=(86, 132, 92), anchor="rm")

cells = [("POWER", "247", "W", VERMILION), ("CADENCE", "92", "rpm", INK),
         ("SPEED", "14.8", "km/h", INK), ("HEART", "154", "bpm", (176, 74, 92))]
for i, (cap, val, unit, col) in enumerate(cells):
    cx = tb[0] + 20 + (tb[2] - tb[0] - 40) / 4 * i
    cy = tb[1] + 58
    text(d, (cx, cy), cap, f_cap_s, fill=INK_SOFT)
    f_val = f_big if i == 0 else f_mid
    text(d, (cx, cy + 18), val, f_val, fill=col)
    vw = d.textlength(val, font=f_val)
    text(d, (cx + vw + 4, cy + (58 if i == 0 else 44)), unit, f_unit, fill=INK_SOFT)
# %FTP bar - the number every challenge is scaled against
text(d, (tb[0] + 20, tb[3] - 54), "112 % FTP  ·  3.6 W/kg", f_cap_s, fill=INK)
bar = (tb[0] + 20, tb[3] - 32, tb[2] - 20, tb[3] - 16)
d.rounded_rectangle(bar, radius=8, fill=(214, 203, 186, 255))
d.rounded_rectangle([bar[0], bar[1], bar[0] + (bar[2] - bar[0]) * 0.78, bar[3]],
                    radius=8, fill=SAKURA)

# --------------------------------------------------------------- segment banner (2)
sb = (W / 2 - 300, 44, W / 2 + 300, 112)
panel(sb, radius=14, fill=(52, 41, 47, 205), outline=(198, 62, 44, 255))
text(d, (W / 2, 66), "LAKESIDE SECTION  ·  KM 0.18 / 3.34", f_cap, fill=(238, 226, 214),
     anchor="mm")
text(d, (W / 2, 92), "NEXT CHECKPOINT — Cliff Tunnel  ·  120 m",
     font("segoeuib.ttf", 20), fill=GOLD, anchor="mm")

# --------------------------------------------------------------- GPS window (3)
box = (W - 44 - 580, H - 44 - 690, W - 44, H - 44)
G.draw_gps_widget(base, box, plan,
                  course=["Pass Ascent (built)", "Fuji Descent (built)",
                          "S1 Kawabe Lakeshore Return"],
                  progress=0.055, lap=(2, 4), course_label="SAKURA CIRCUIT", scale=1.0)

# --------------------------------------------------------------- ride goal card (4)
gb = (44, H - 44 - 196, 44 + 470, H - 44)
panel(gb)
d.rounded_rectangle([gb[0], gb[1], gb[2], gb[1] + 40], radius=18, fill=(214, 196, 176, 240))
d.rectangle([gb[0], gb[1] + 24, gb[2], gb[1] + 40], fill=(214, 196, 176, 240))
d.line([(gb[0], gb[1] + 40), (gb[2], gb[1] + 40)], fill=VERMILION, width=3)
text(d, (gb[0] + 16, gb[1] + 20), "RIDE PLAN  ·  30 MIN", f_cap, fill=INK, anchor="lm")
text(d, (gb[2] - 16, gb[1] + 20), "ELAPSED 08:42", f_cap_s, fill=INK_SOFT, anchor="rm")
rows = [("Sakura Circuit", "4 laps @ 8.5 min", 0.26),
        ("Club reputation — Maple CC", "Recognised  →  Respected", 0.55),
        ("Rival encounters met", "2 / 3", 0.66)]
for i, (label, val, prog) in enumerate(rows):
    yy = gb[1] + 58 + i * 44
    text(d, (gb[0] + 18, yy), label, font("seguisb.ttf", 16), fill=INK)
    text(d, (gb[2] - 18, yy), val, f_note, fill=INK_SOFT, anchor="ra")
    pb = (gb[0] + 18, yy + 24, gb[2] - 18, yy + 32)
    d.rounded_rectangle(pb, radius=4, fill=(214, 203, 186, 255))
    d.rounded_rectangle([pb[0], pb[1], pb[0] + (pb[2] - pb[0]) * prog, pb[3]],
                        radius=4, fill=GOLD if i else VERMILION)

# --------------------------------------------------------------- callouts
callouts = [((tb[2] + 24, tb[1] + 20), "1"), ((sb[0] - 24, sb[1] + 20), "2"),
            ((box[0] - 24, box[1] + 22), "3"), ((gb[2] + 24, gb[1] + 20), "4")]
for (cx, cy), n in callouts:
    d.ellipse([cx - 17, cy - 17, cx + 17, cy + 17], fill=VERMILION, outline=PAPER, width=3)
    text(d, (cx, cy), n, font("segoeuib.ttf", 20), fill=(255, 255, 255), anchor="mm")

# --------------------------------------------------------------- caption strip
cap = (0, H - 34, W, H)
d.rectangle(cap, fill=(38, 30, 38, 230))
legend = ("1  DeviceManager telemetry (watts / cadence / speed / HR, %FTP)     "
          "2  RouteDirector segment + next-checkpoint banner     "
          "3  RouteMapHud — GPS window: course line, rider marker, checkpoints, "
          "elevation + live gradient     4  Ride-plan / progression card")
text(d, (W / 2, H - 17), legend, font("seguisb.ttf", 15), fill=(236, 226, 216), anchor="mm")

# title plate
d.rectangle([0, 0, W, 0], fill=None)

out = base.convert("RGB")
for folder in OUT_DIRS:
    os.makedirs(folder, exist_ok=True)
    p = os.path.join(folder, NAME)
    out.save(p, "PNG")
    print(f"wrote {p}")

"""
Shared drawing helpers for the Sakura Pass expansion artefacts.

Both the planning map and the in-game HUD mockup draw the SAME GPS widget from the SAME
route polylines, so the mockup is a picture of the thing the plan specifies rather than an
unrelated illustration.

Palette is sampled from the established Sakura Pass art direction (good_graphics/diag_gate.png,
diag_descent_overview.png): dusty-rose sky, vermilion torii, sakura pink canopy, moss green
batter, slate asphalt, washi-cream UI.
"""

import json
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PLAN = os.path.join(HERE, "route_expansion_plan.json")

# ------------------------------------------------------------------ palette
PAPER = (243, 234, 220)
PAPER_DEEP = (232, 219, 201)
INK = (52, 41, 47)
INK_SOFT = (110, 94, 100)
VERMILION = (198, 62, 44)
SAKURA = (236, 152, 178)
SAKURA_DEEP = (205, 106, 140)
LAKE = (142, 190, 204)
LAKE_DEEP = (104, 158, 178)
MOSS = (126, 146, 98)
MOSS_DEEP = (92, 112, 72)
SLATE = (98, 92, 104)
GOLD = (206, 160, 74)
VIOLET = (150, 136, 176)
SNOW = (248, 246, 250)
NIGHT = (38, 30, 38)

FONT_DIR = r"C:\Windows\Fonts"


def font(name="segoeui.ttf", size=20):
    return ImageFont.truetype(os.path.join(FONT_DIR, name), size)


def load_plan():
    return json.load(open(PLAN, encoding="utf-8"))


# Checkpoints are expressed as a fraction of each segment so they survive any re-cut of the
# geometry, exactly the way SakuraPassEnvironment resolves its landmark windows against
# ClimbLength rather than against total route length.
CHECKPOINTS = {
    "Pass Ascent (built)": [
        (0.00, "Ride Gate", True), (0.16, "Shrine Torii", True),
        (0.29, "Lakeside Overlook", False), (0.55, "Cliff Tunnel", True),
        (0.70, "Hillside Sign", False), (1.00, "Summit Crest", True),
    ],
    "Fuji Descent (built)": [
        (0.17, "Descent Overlook", True), (0.30, "Hairpin Twins", False),
        (1.00, "Fuji Foot", True),
    ],
    "S1 Kawabe Lakeshore Return": [
        (0.28, "Minamo Causeway", True), (0.62, "Lake Village", True),
        (0.84, "Kawabe Bridge", True),
    ],
    "S2a Aozora Ascent": [
        (0.02, "Aozora Junction", True), (0.52, "Switchback Field", False),
        (1.00, "Cloudline Shrine", True),
    ],
    "S2b Skyline Traverse": [
        (0.40, "Skyline Col", True), (1.00, "Summit Crest", False),
    ],
    "S3 Maple City Road": [
        (0.50, "Water Stop", False), (1.00, "Maple City Gate", True),
    ],
}


def course_points(plan, course):
    """Concatenate segment polylines into one course polyline + checkpoint index list."""
    pts, checks = [], []
    for name in course:
        p = [tuple(v) for v in plan["polylines"][name]]
        if pts and math.dist(pts[-1], p[0]) > math.dist(pts[-1], p[-1]):
            p = p[::-1]
            flip = True
        else:
            flip = False
        base_i = len(pts)
        for f, label, major in CHECKPOINTS.get(name, []):
            ff = 1.0 - f if flip else f
            checks.append((base_i + int(ff * (len(p) - 1)), label, major))
        pts += p
    checks.sort()
    return pts, checks


# ------------------------------------------------------------------ helpers
def rounded(draw, box, r, fill=None, outline=None, width=2):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def text(draw, xy, s, f, fill=INK, anchor="la", spacing=4):
    draw.text(xy, s, font=f, fill=fill, anchor=anchor, spacing=spacing)


def dashed_line(draw, pts, fill, width=3, dash=14, gap=10):
    carry = 0.0
    on = True
    for a, b in zip(pts, pts[1:]):
        seg = math.dist(a, b)
        t = 0.0
        while t < seg:
            step = (dash if on else gap) - carry
            t2 = min(seg, t + step)
            if on:
                p0 = (a[0] + (b[0] - a[0]) * t / seg, a[1] + (b[1] - a[1]) * t / seg)
                p1 = (a[0] + (b[0] - a[0]) * t2 / seg, a[1] + (b[1] - a[1]) * t2 / seg)
                draw.line([p0, p1], fill=fill, width=width)
            if t2 - t >= step - 1e-9:
                carry = 0.0
                on = not on
            else:
                carry += t2 - t
            t = t2


def glow_line(img, pts, fill, width, blur=6, alpha=110):
    """Soft halo under a route line so it reads over busy terrain."""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.line(pts, fill=fill + (alpha,), width=width, joint="curve")
    img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(blur)))


def marker_pin(draw, xy, label, f, fill=VERMILION, r=9, text_fill=INK, side="right",
               ring=PAPER):
    x, y = xy
    draw.ellipse([x - r - 3, y - r - 3, x + r + 3, y + r + 3], fill=ring)
    draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)
    draw.ellipse([x - r * 0.38, y - r * 0.38, x + r * 0.38, y + r * 0.38], fill=ring)
    if label:
        if side == "right":
            text(draw, (x + r + 10, y), label, f, fill=text_fill, anchor="lm")
        else:
            text(draw, (x - r - 10, y), label, f, fill=text_fill, anchor="rm")


def rider_chevron(draw, xy, heading_deg, size=15, fill=(255, 255, 255), edge=VERMILION):
    """Player marker: a heading chevron inside a ring, the way a bike GPS draws it."""
    x, y = xy
    draw.ellipse([x - size * 1.65, y - size * 1.65, x + size * 1.65, y + size * 1.65],
                 fill=edge)
    draw.ellipse([x - size * 1.32, y - size * 1.32, x + size * 1.32, y + size * 1.32],
                 fill=(255, 255, 255))
    a = math.radians(heading_deg)
    pts = []
    for ang, rad in ((0, 1.0), (140, 0.86), (180, 0.42), (220, 0.86)):
        t = a + math.radians(ang)
        pts.append((x + math.sin(t) * size * rad, y - math.cos(t) * size * rad))
    draw.polygon(pts, fill=edge)


def elevation_strip(draw, box, pts, progress, f_small, f_num,
                    line=VERMILION, fill_done=(198, 62, 44), fill_todo=(205, 191, 172),
                    label_grade=True, bg=None, border=None):
    """
    Elevation profile of a (x, y, z) polyline inside `box`, with the rider at `progress`
    (0..1 of arc length). Returns the rider's pixel position and local grade.
    """
    x0, y0, x1, y1 = box
    if bg:
        rounded(draw, box, 10, fill=bg, outline=border, width=2)
    d = [0.0]
    for a, b in zip(pts, pts[1:]):
        d.append(d[-1] + math.dist(a, b))
    total = d[-1]
    ys = [p[1] for p in pts]
    lo, hi = min(ys), max(ys)
    span = max(6.0, hi - lo)
    pad = 10

    def px(i):
        return (x0 + pad + (x1 - x0 - 2 * pad) * d[i] / total,
                y1 - pad - (y1 - y0 - 2 * pad) * (ys[i] - lo) / span)

    poly = [px(i) for i in range(len(pts))]
    cut = progress * total
    done_i = max(1, min(len(pts) - 1, next((i for i, v in enumerate(d) if v >= cut),
                                           len(pts) - 1)))
    draw.polygon(poly + [(poly[-1][0], y1 - pad), (poly[0][0], y1 - pad)], fill=fill_todo)
    draw.polygon(poly[:done_i + 1] + [(poly[done_i][0], y1 - pad), (poly[0][0], y1 - pad)],
                 fill=fill_done)
    draw.line(poly, fill=line, width=3, joint="curve")
    rx, ry = poly[done_i]
    draw.line([(rx, y0 + 4), (rx, y1 - pad)], fill=INK, width=2)
    grade = 0.0
    j = min(len(pts) - 2, max(0, done_i))
    run = math.dist((pts[j][0], pts[j][2]), (pts[j + 1][0], pts[j + 1][2]))
    if run > 0.01:
        grade = (pts[j + 1][1] - pts[j][1]) / run * 100.0
    return (rx, ry), grade, (lo, hi, total)


# ------------------------------------------------------------------ GPS widget
def draw_gps_widget(base, box, plan, course, progress=0.62, lap=(2, 4),
                    course_label="SAKURA CIRCUIT", scale=1.0, opacity=242,
                    annotate=False):
    """
    Draws the in-game GPS / map window into `base` (RGBA) at `box` = (x0, y0, x1, y1).

    `course` is a list of segment names from route_expansion_plan.json; they are concatenated
    into one polyline, which is exactly what RouteGraph.BuildCourse() does at runtime.
    """
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    s = scale

    pts, checks = course_points(plan, course)

    # --- card -------------------------------------------------------------------
    card = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    cd = ImageDraw.Draw(card)
    shadow = Image.new("RGBA", (w + 40, h + 40), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([20, 24, w + 20, h + 20], radius=int(22 * s),
                                             fill=(30, 20, 28, 120))
    base.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)), (x0 - 20, y0 - 20))
    cd.rounded_rectangle([0, 0, w - 1, h - 1], radius=int(22 * s),
                         fill=PAPER + (opacity,), outline=(120, 96, 90, 255),
                         width=max(2, int(3 * s)))
    # washi header band
    cd.rounded_rectangle([0, 0, w - 1, int(54 * s)], radius=int(22 * s),
                         fill=(214, 196, 176, opacity))
    cd.rectangle([0, int(38 * s), w - 1, int(54 * s)], fill=(214, 196, 176, opacity))
    cd.line([(0, int(54 * s)), (w, int(54 * s))], fill=VERMILION + (255,),
            width=max(2, int(3 * s)))

    f_h = font("segoeuib.ttf", int(22 * s))
    f_s = font("seguisb.ttf", int(15 * s))
    f_t = font("segoeui.ttf", int(14 * s))
    f_n = font("segoeuib.ttf", int(30 * s))
    f_nn = font("segoeuib.ttf", int(19 * s))

    text(cd, (int(18 * s), int(27 * s)), course_label, f_h, fill=INK, anchor="lm")
    text(cd, (w - int(18 * s), int(27 * s)),
         f"LAP {lap[0]}/{lap[1]}", f_s, fill=VERMILION, anchor="rm")

    # --- map body ---------------------------------------------------------------
    m0 = (int(14 * s), int(66 * s))
    m1 = (w - int(14 * s), h - int(196 * s))
    mw, mh = m1[0] - m0[0], m1[1] - m0[1]
    cd.rounded_rectangle([m0[0], m0[1], m1[0], m1[1]], radius=int(12 * s),
                         fill=(224, 231, 218, 255), outline=(168, 150, 140, 255), width=2)

    xs = [p[0] for p in pts]
    zs = [p[2] for p in pts]
    pad = int(26 * s)
    k = min((mw - 2 * pad) / max(1.0, max(xs) - min(xs)),
            (mh - 2 * pad) / max(1.0, max(zs) - min(zs)))
    cx, cz = (max(xs) + min(xs)) / 2, (max(zs) + min(zs)) / 2

    def W(p):
        return (m0[0] + mw / 2 + (p[0] - cx) * k, m0[1] + mh / 2 - (p[2] - cz) * k)

    # lake basin: the water sits between the pass road and the far-shore return road
    lake_poly = []
    if "Pass Ascent (built)" in plan["polylines"]:
        for name in ("Pass Ascent (built)", "Fuji Descent (built)"):
            for p in plan["polylines"][name]:
                lake_poly.append(W((p[0] + 105.0, 0, p[2])))
    if "S1 Kawabe Lakeshore Return" in plan["polylines"]:
        for p in list(plan["polylines"]["S1 Kawabe Lakeshore Return"])[::-1]:
            lake_poly.append(W((p[0] - 34.0, 0, p[2])))
    if len(lake_poly) > 3:
        cd.polygon(lake_poly, fill=LAKE + (210,))

    pix = [W(p) for p in pts]
    cd.line(pix, fill=(150, 132, 128, 255), width=max(3, int(9 * s)), joint="curve")
    cd.line(pix, fill=(238, 232, 226, 255), width=max(2, int(6 * s)), joint="curve")

    d = [0.0]
    for a, b in zip(pts, pts[1:]):
        d.append(d[-1] + math.dist(a, b))
    total = d[-1]
    cut = progress * total
    ri = max(1, next((i for i, v in enumerate(d) if v >= cut), len(pts) - 1))
    cd.line(pix[:ri + 1], fill=VERMILION + (255,), width=max(3, int(6 * s)), joint="curve")

    # checkpoints along the course
    f_pin = font("seguisb.ttf", int(13 * s))
    for idx, label, major in checks:
        p = pix[min(idx, len(pix) - 1)]
        r = 6 * s if major else 4 * s
        cd.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r],
                   fill=PAPER + (255,), outline=INK + (255,), width=2)

    # the next checkpoint ahead of the rider gets the gold pin and the name
    nxt = next((c for c in checks if c[0] > ri), checks[-1])
    np_ = pix[min(nxt[0], len(pix) - 1)]
    marker_pin(cd, np_, "", f_pin, fill=GOLD, r=int(7 * s), ring=PAPER + (255,))
    side = "right" if np_[0] < m0[0] + mw * 0.6 else "left"
    anchor = "lm" if side == "right" else "rm"
    dx = 13 * s if side == "right" else -13 * s
    text(cd, (np_[0] + dx, np_[1] - 2 * s), nxt[1], f_pin, fill=INK, anchor=anchor)

    # rider
    hx = pix[min(ri + 1, len(pix) - 1)][0] - pix[ri][0]
    hy = pix[min(ri + 1, len(pix) - 1)][1] - pix[ri][1]
    heading = math.degrees(math.atan2(hx, -hy))
    rider_chevron(cd, pix[ri], heading, size=int(11 * s))

    # north rose + scale bar
    nx, ny = m1[0] - int(26 * s), m0[1] + int(28 * s)
    cd.ellipse([nx - 16 * s, ny - 16 * s, nx + 16 * s, ny + 16 * s],
               fill=PAPER + (220,), outline=INK_SOFT + (255,), width=1)
    cd.polygon([(nx, ny - 12 * s), (nx - 6 * s, ny + 6 * s), (nx, ny + 2 * s),
                (nx + 6 * s, ny + 6 * s)], fill=VERMILION + (255,))
    text(cd, (nx, ny + 13 * s), "N", font("segoeuib.ttf", int(11 * s)), fill=INK, anchor="mm")
    bar_m = 200.0
    bx1 = m0[0] + int(22 * s)
    bx2 = bx1 + bar_m * k
    by = m1[1] - int(18 * s)
    cd.line([(bx1, by), (bx2, by)], fill=INK + (255,), width=max(2, int(3 * s)))
    cd.line([(bx1, by - 4 * s), (bx1, by + 4 * s)], fill=INK + (255,), width=2)
    cd.line([(bx2, by - 4 * s), (bx2, by + 4 * s)], fill=INK + (255,), width=2)
    text(cd, ((bx1 + bx2) / 2, by - 9 * s), "200 m", f_pin, fill=INK, anchor="ms")

    # --- readouts + elevation strip ---------------------------------------------
    ry0 = h - int(186 * s)
    gain = sum(max(0.0, b[1] - a[1]) for a, b in zip(pts[:ri], pts[1:ri + 1]))
    done_km = cut / 1000.0
    tot_km = total / 1000.0

    cols = [("DISTANCE", f"{done_km:.2f}", f"of {tot_km:.2f} km"),
            ("ASCENT", f"{gain:.0f}", "m climbed"),
            ("TO GO", f"{(total - cut)/1000:.2f}", "km"),
            ("NEXT CP", f"{max(0.0, (d[min(nxt[0], len(d)-1)] - cut)):.0f}", "m")]
    cw = (w - int(28 * s)) / len(cols)
    for i, (cap, val, unit) in enumerate(cols):
        px_ = int(14 * s) + cw * i + cw / 2
        text(cd, (px_, ry0), cap, f_pin, fill=INK_SOFT, anchor="ma")
        text(cd, (px_, ry0 + int(18 * s)), val, f_n, fill=INK, anchor="ma")
        text(cd, (px_, ry0 + int(52 * s)), unit, f_t, fill=INK_SOFT, anchor="ma")

    ebox = (int(14 * s), h - int(108 * s), w - int(14 * s), h - int(14 * s))
    (ex, ey), grade, (lo, hi, tl) = elevation_strip(
        cd, ebox, pts, progress, f_pin, f_nn,
        fill_done=(226, 176, 186), fill_todo=(214, 203, 186), line=SAKURA_DEEP,
        bg=(236, 228, 214, 255), border=(180, 162, 150, 255))
    rider_chevron(cd, (ex, ey), 0, size=int(7 * s))
    text(cd, (ebox[0] + int(8 * s), ebox[1] + int(6 * s)), f"{hi:+.0f} m", f_pin,
         fill=INK_SOFT, anchor="la")
    text(cd, (ebox[0] + int(8 * s), ebox[3] - int(6 * s)), f"{lo:+.0f} m", f_pin,
         fill=INK_SOFT, anchor="ld")
    badge_w, badge_h = int(94 * s), int(32 * s)
    bx = ebox[2] - int(10 * s) - badge_w
    cd.rounded_rectangle([bx, ebox[1] + int(6 * s), bx + badge_w, ebox[1] + int(6 * s) + badge_h],
                         radius=int(8 * s),
                         fill=(VERMILION if grade >= 0 else LAKE_DEEP) + (245,))
    text(cd, (bx + badge_w / 2, ebox[1] + int(6 * s) + badge_h / 2),
         f"{grade:+.1f}%", font("segoeuib.ttf", int(18 * s)), fill=(255, 255, 255),
         anchor="mm")

    base.alpha_composite(card, (x0, y0))
    return {"total_m": total, "progress_m": cut}

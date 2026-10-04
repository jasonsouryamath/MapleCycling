"""Race marker + rank insignia sprites for the NPC race feature.

Writes Assets/Resources/Race/*.png (read at run time by RaceArt.cs):
  race_icon_<type>.png   256x256  glowing diamond marker per race type (race/sprint/kom/tt/elite)
  race_glow.png          256x256  soft radial glow (tinted in UI)
  race_ring.png          256x256  glowing ground ring (white, tinted by material)
  race_beam.png           64x256  vertical light beam (white, tinted)
  race_sparkle.png        64x64   four-point sparkle
  insignia_<00..16>.png  128x128  GunBound-style rank insignias, index = RaceMath.InsigniaIndex

Everything is drawn at 4x and downsampled, so edges are anti-aliased. Original art, code-drawn.
Run:  python tools/ui/make_race_icons.py
"""
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Race")
SS = 4

RACE_TYPES = {
    "race": (0x3F, 0xA2, 0xFF),
    "sprint": (0xFF, 0x47, 0x57),
    "kom": (0x3D, 0xDC, 0x74),
    "tt": (0xA9, 0x5C, 0xFF),
    "elite": (0xFF, 0xB4, 0x2E),
}

# (light, base, dark) per material
MAT = {
    "wood": ((238, 204, 150), (196, 150, 98), (112, 74, 40)),
    "bronze": ((246, 178, 116), (198, 112, 56), (104, 50, 22)),
    "silver": ((246, 248, 252), (180, 188, 200), (88, 96, 110)),
    "gold": ((255, 240, 160), (234, 180, 46), (136, 90, 12)),
    "jade": ((168, 242, 200), (52, 180, 122), (16, 92, 60)),
    "violet": ((218, 184, 255), (152, 92, 224), (72, 36, 128)),
    "leaf": ((190, 240, 140), (96, 190, 70), (36, 104, 30)),
    "crimson": ((255, 140, 130), (214, 40, 52), (110, 12, 22)),
    "sakura": ((255, 222, 236), (248, 160, 192), (170, 70, 110)),
    "azure": ((150, 214, 255), (42, 132, 232), (14, 50, 120)),
    "sun": ((255, 170, 120), (228, 48, 36), (120, 16, 10)),
    "soil": ((170, 120, 80), (120, 80, 50), (70, 44, 26)),
}


# ------------------------------------------------------------------ helpers
def canvas(w, h=None):
    return Image.new("RGBA", (w * SS, (h or w) * SS), (0, 0, 0, 0))


def mask(w, h=None):
    return Image.new("L", (w * SS, (h or w) * SS), 0)


def down(img, w, h=None):
    return img.resize((w, h or w), Image.LANCZOS)


def grad_fill(size, top, bottom, box=None):
    """Vertical gradient RGBA image of `size`, graded over box (y0, y1) in pixels."""
    w, h = size
    y0, y1 = box if box else (0, h)
    g = Image.new("RGBA", size)
    px = g.load()
    for y in range(h):
        t = min(1.0, max(0.0, (y - y0) / max(1, (y1 - y0))))
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        for x in range(w):
            px[x, y] = c
    return g


def bbox_y(m):
    b = m.getbbox()
    return (b[1], b[3]) if b else (0, m.size[1])


def paint(dst, m, mat, outline=True, shine=True):
    """Metal/material fill: vertical gradient light->base->dark, a dark outline and a top sheen."""
    light, base, dark = MAT[mat] if isinstance(mat, str) else mat
    y0, y1 = bbox_y(m)
    mid = (y0 + y1) // 2
    top = grad_fill(m.size, light, base, (y0, mid))
    bot = grad_fill(m.size, base, dark, (mid, y1))
    sel = Image.new("L", m.size, 0)
    ImageDraw.Draw(sel).rectangle([0, mid, m.size[0], m.size[1]], fill=255)
    fill = Image.composite(bot, top, sel)
    if outline:
        ring = m.filter(ImageFilter.MaxFilter(2 * SS + 1))
        edge = Image.new("RGBA", m.size, tuple(int(c * 0.45) for c in dark) + (255,))
        dst.alpha_composite(Image.composite(edge, Image.new("RGBA", m.size, (0, 0, 0, 0)), ring))
    dst.alpha_composite(Image.composite(fill, Image.new("RGBA", m.size, (0, 0, 0, 0)), m))
    if shine:
        # thin light line along the upper-inner edge
        inner = m.filter(ImageFilter.MinFilter(3 * SS + 1))
        rim = ImageChops.subtract(m, inner)
        up = Image.new("L", m.size, 0)
        ImageDraw.Draw(up).rectangle([0, 0, m.size[0], mid], fill=150)
        rim = ImageChops.multiply(rim, up)
        dst.alpha_composite(Image.composite(Image.new("RGBA", m.size, (255, 255, 255, 255)),
                                            Image.new("RGBA", m.size, (0, 0, 0, 0)), rim))


def S(v):
    return v * SS


# ------------------------------------------------------------------ shape masks (units: 128 grid)
def wheel_mask(cx, cy, r, w=128):
    m = mask(w)
    d = ImageDraw.Draw(m)
    d.ellipse([S(cx - r), S(cy - r), S(cx + r), S(cy + r)], fill=255)
    ri = r * 0.74
    d.ellipse([S(cx - ri), S(cy - ri), S(cx + ri), S(cy + ri)], fill=0)
    for k in range(8):
        a = k * math.pi / 4 + 0.2
        d.line([S(cx), S(cy), S(cx + math.cos(a) * ri * 1.02), S(cy + math.sin(a) * ri * 1.02)],
               fill=255, width=int(round(S(max(2, r * 0.09)))))
    rh = r * 0.2
    d.ellipse([S(cx - rh), S(cy - rh), S(cx + rh), S(cy + rh)], fill=255)
    return m


def crank_mask(cx, cy, r, w=128):
    m = mask(w)
    d = ImageDraw.Draw(m)
    teeth = 28
    pts = []
    for k in range(teeth * 2):
        a = k * math.pi / teeth
        rr = r if k % 2 == 0 else r * 0.9
        pts.append((S(cx + math.cos(a) * rr), S(cy + math.sin(a) * rr)))
    d.polygon(pts, fill=255)
    ri = r * 0.64
    d.ellipse([S(cx - ri), S(cy - ri), S(cx + ri), S(cy + ri)], fill=0)
    for k in range(5):  # spider arms
        a = k * 2 * math.pi / 5 - math.pi / 2
        d.line([S(cx), S(cy), S(cx + math.cos(a) * ri), S(cy + math.sin(a) * ri)], fill=255,
               width=int(round(S(r * 0.14))))
    # crank arm to the lower right + pedal
    a = math.radians(40)
    ex, ey = cx + math.cos(a) * r * 1.35, cy + math.sin(a) * r * 1.35
    d.line([S(cx), S(cy), S(ex), S(ey)], fill=255, width=int(round(S(r * 0.26))))
    d.ellipse([S(ex - r * 0.2), S(ey - r * 0.2), S(ex + r * 0.2), S(ey + r * 0.2)], fill=255)
    d.ellipse([S(cx - r * 0.22), S(cy - r * 0.22), S(cx + r * 0.22), S(cy + r * 0.22)], fill=255)
    return m


def draw_wing(img, mat, ox, oy, s, flip=False, w=128):
    """Emblem wing: six primaries fanning up and out from the shoulder at (ox, oy), with a
    rounded covert over their roots. Each feather is painted separately so its own outline
    separates it from its neighbour. Flip mirrors it (the left wing of a pair)."""
    sign = -1 if flip else 1

    def P(x, y):
        return (ox + sign * x * s, oy - y * s)   # local y is UP

    def feather(ang_deg, length, half_w, start):
        a = math.radians(ang_deg)
        dx, dy = math.cos(a), math.sin(a)
        nx, ny = -dy, dx
        pts = []
        for i in range(48):
            th = i / 48 * 2 * math.pi
            along = (math.sin(th) * 0.5 + 0.5) * length + start
            across = math.cos(th) * half_w
            # rounded, slightly pointed tip; full width near the root
            f = (along - start) / length
            across *= 1.0 - 0.45 * max(0.0, f - 0.55) / 0.45
            pts.append(P(dx * along + nx * across, dy * along + ny * across))
        m = mask(w)
        ImageDraw.Draw(m).polygon([(S(x), S(y)) for x, y in pts], fill=255)
        paint(img, m, mat)

    # top (longest, most upright) feathers first so the lower ones overlap them
    for k in reversed(range(6)):
        feather(10 + k * 14.5, 44 + k * 8.5, 9.6 - k * 0.4, 2)
    m = mask(w)
    cx, cy = P(5, 9)
    r = 11.5 * s
    ImageDraw.Draw(m).ellipse([S(cx - r), S(cy - r * 0.9), S(cx + r), S(cy + r * 0.9)], fill=255)
    paint(img, m, mat)


def maple_mask(cx, cy, r, w=128):
    m = mask(w)
    # classic maple leaf outline in unit coords (y up), from a hand-traced 11-lobe profile
    pts = [(0, 1.0), (0.12, 0.66), (0.38, 0.8), (0.3, 0.42), (0.62, 0.52), (0.58, 0.36), (0.9, 0.38),
           (0.72, 0.12), (0.84, 0.02), (0.44, -0.2), (0.5, -0.36), (0.06, -0.3), (0.06, -0.72),
           (-0.06, -0.72), (-0.06, -0.3), (-0.5, -0.36), (-0.44, -0.2), (-0.84, 0.02), (-0.72, 0.12),
           (-0.9, 0.38), (-0.58, 0.36), (-0.62, 0.52), (-0.3, 0.42), (-0.38, 0.8), (-0.12, 0.66)]
    ImageDraw.Draw(m).polygon([(S(cx + x * r), S(cy - y * r)) for x, y in pts], fill=255)
    return m


def flower(d, cx, cy, r, fill=255):
    for k in range(5):
        a = k * 2 * math.pi / 5 - math.pi / 2
        px, py = cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55
        d.ellipse([S(px - r * 0.48), S(py - r * 0.48), S(px + r * 0.48), S(py + r * 0.48)], fill=fill)


# ------------------------------------------------------------------ insignias
def insignia(idx):
    W = 128
    img = canvas(W)

    def twin(fn, mat, a=(50, 70), b=(76, 56), r=30):
        paint(img, fn(a[0], a[1], r), mat)
        paint(img, fn(b[0], b[1], r), mat)

    if idx == 0:  # Sprout
        soil = mask(W)
        ImageDraw.Draw(soil).ellipse([S(30), S(88), S(98), S(114)], fill=255)
        paint(img, soil, "soil")
        stem = mask(W)
        ImageDraw.Draw(stem).line([S(64), S(96), S(64), S(56)], fill=255, width=int(round(S(7))))
        paint(img, stem, "leaf", shine=False)
        for sign in (-1, 1):
            leaf = mask(W)
            # a leaf lying out to one side of the stem top, then tilted up
            x0 = 64 if sign > 0 else 64 - 40
            ImageDraw.Draw(leaf).ellipse([S(x0), S(40), S(x0 + 40), S(60)], fill=255)
            leaf = leaf.rotate(sign * 30, center=(S(64), S(56)), resample=Image.BICUBIC)
            paint(img, leaf, "leaf")
    elif idx in (1, 2):
        if idx == 1:
            paint(img, wheel_mask(64, 64, 44), "wood")
        else:
            twin(wheel_mask, "wood", (48, 72), (80, 54), 36)
    elif idx in (3, 4):
        if idx == 3:
            paint(img, wheel_mask(64, 64, 44), "bronze")
        else:
            twin(wheel_mask, "bronze", (48, 72), (80, 54), 36)
    elif idx in (5, 6, 7, 8):
        mat = "silver" if idx in (5, 6) else "gold"
        if idx in (5, 7):
            paint(img, crank_mask(58, 58, 38), mat)
        else:
            twin(crank_mask, mat, (46, 68), (76, 48), 31)
    elif idx in (9, 10, 11, 12):
        mat = "jade" if idx in (9, 10) else "violet"
        if idx in (9, 11):
            draw_wing(img, mat, 30, 112, 1.12)
        else:
            draw_wing(img, mat, 58, 108, 0.74, flip=True)
            draw_wing(img, mat, 70, 108, 0.74)
    elif idx == 13:  # Crimson Maple
        paint(img, maple_mask(64, 60, 54), "crimson")
    elif idx == 14:  # Sakura Crown
        crown = mask(W)
        d = ImageDraw.Draw(crown)
        pts = [(18, 100), (14, 44), (38, 70), (52, 30), (64, 60), (76, 30), (90, 70), (114, 44), (110, 100)]
        d.polygon([(S(x), S(y)) for x, y in pts], fill=255)
        d.rectangle([S(18), S(96), S(110), S(110)], fill=255)
        paint(img, crown, "gold")
        for (x, y) in ((14, 42), (52, 28), (76, 28), (114, 42)):
            b = mask(W)
            ImageDraw.Draw(b).ellipse([S(x - 6), S(y - 6), S(x + 6), S(y + 6)], fill=255)
            paint(img, b, "gold")
        f = mask(W)
        flower(ImageDraw.Draw(f), 64, 80, 20)
        paint(img, f, "sakura")
        for x in (36, 92):
            f = mask(W)
            flower(ImageDraw.Draw(f), x, 86, 11)
            paint(img, f, "sakura")
        c = mask(W)
        ImageDraw.Draw(c).ellipse([S(58), S(74), S(70), S(86)], fill=255)
        paint(img, c, "gold", outline=False)
    elif idx == 15:  # Azure Dragon (coiled serpent)
        body = mask(W)
        d = ImageDraw.Draw(body)
        cx, cy, R = 64, 66, 40
        for k in range(0, 300, 2):  # tapering coil
            a = math.radians(k + 60)
            t = k / 300.0
            wd = 15 * (1 - t) + 4
            x, y = cx + math.cos(a) * R, cy + math.sin(a) * R
            d.ellipse([S(x - wd / 2), S(y - wd / 2), S(x + wd / 2), S(y + wd / 2)], fill=255)
        # head at angle 60 deg (lower right) looking up-right
        hx, hy = cx + math.cos(math.radians(60)) * R, cy + math.sin(math.radians(60)) * R
        head = [(hx - 12, hy + 6), (hx - 6, hy - 18), (hx + 10, hy - 26), (hx + 26, hy - 18),
                (hx + 22, hy - 8), (hx + 8, hy - 6), (hx + 2, hy + 8)]
        d.polygon([(S(x), S(y)) for x, y in head], fill=255)
        for hxo in (-2, 6):  # horns
            d.line([S(hx + hxo), S(hy - 20), S(hx + hxo - 10), S(hy - 40)], fill=255, width=int(round(S(4))))
        paint(img, body, "azure")
        eye = mask(W)
        ImageDraw.Draw(eye).ellipse([S(hx + 8), S(hy - 20), S(hx + 14), S(hy - 14)], fill=255)
        paint(img, eye, "gold", outline=False, shine=False)
        # scale ticks
        sc = mask(W)
        d2 = ImageDraw.Draw(sc)
        for k in range(20, 260, 22):
            a = math.radians(k + 60)
            x, y = cx + math.cos(a) * R, cy + math.sin(a) * R
            d2.arc([S(x - 5), S(y - 5), S(x + 5), S(y + 5)], 200, 340, fill=255, width=int(round(S(1.5))))
        img.alpha_composite(Image.composite(Image.new("RGBA", sc.size, (220, 240, 255, 200)),
                                            Image.new("RGBA", sc.size, (0, 0, 0, 0)), sc))
    elif idx == 16:  # Rising Sun
        badge = mask(W)
        ImageDraw.Draw(badge).ellipse([S(6), S(6), S(122), S(122)], fill=255)
        paint(img, badge, "gold")
        rays = mask(W)
        d = ImageDraw.Draw(rays)
        for k in range(16):
            a0 = k * 2 * math.pi / 16
            a1 = a0 + math.pi / 16
            d.polygon([(S(64), S(64)), (S(64 + math.cos(a0) * 54), S(64 + math.sin(a0) * 54)),
                       (S(64 + math.cos(a1) * 54), S(64 + math.sin(a1) * 54))], fill=255)
        ImageDraw.Draw(rays).ellipse([S(12), S(12), S(116), S(116)], outline=0, width=int(round(S(0))))
        paint(img, rays, "sun", outline=False)
        disc = mask(W)
        ImageDraw.Draw(disc).ellipse([S(38), S(38), S(90), S(90)], fill=255)
        paint(img, disc, "sun")
    return down(img, W)


# ------------------------------------------------------------------ race icons
def glyph_flags(d, cx, cy, s, crossed=True):
    """Two crossed checkered flags. Draws into an RGBA ImageDraw (white + dark checks)."""
    for sign in (-1, 1):
        # pole from bottom-centre outward and up
        bx, by = cx - sign * 30 * s, cy + 44 * s
        tx, ty = cx + sign * 22 * s, cy - 30 * s
        d.line([S(bx), S(by), S(tx), S(ty)], fill=(255, 255, 255, 255), width=int(round(S(5 * s))))
        # flag hangs off the pole top toward the outside
        fw, fh = 46 * s, 34 * s
        ox, oy = tx, ty
        quad = [(ox, oy), (ox + sign * fw, oy - 6 * s), (ox + sign * fw - sign * 6 * s, oy + fh - 6 * s),
                (ox - sign * 8 * s, oy + fh)]
        n = 4
        for i in range(n):
            for j in range(3):
                def lerp(p, q, t):
                    return (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t)
                def at(u, v):
                    top = lerp(quad[0], quad[1], u)
                    bot = lerp(quad[3], quad[2], u)
                    return lerp(top, bot, v)
                cell = [at(i / n, j / 3), at((i + 1) / n, j / 3), at((i + 1) / n, (j + 1) / 3), at(i / n, (j + 1) / 3)]
                col = (255, 255, 255, 255) if (i + j) % 2 == 0 else (22, 30, 58, 255)
                d.polygon([(S(x), S(y)) for x, y in cell], fill=col)
        d.line([(S(x), S(y)) for x, y in quad + [quad[0]]], fill=(255, 255, 255, 255), width=int(round(S(2 * s))))


def glyph(kind, size):
    g = canvas(size)
    d = ImageDraw.Draw(g)
    c = size / 2
    white = (255, 255, 255, 255)
    if kind == "race":
        glyph_flags(d, c, c + 4, 1.0)
    elif kind == "sprint":
        pts = [(c + 8, c - 58), (c - 30, c + 6), (c - 4, c + 6), (c - 14, c + 58), (c + 30, c - 10),
               (c + 4, c - 10), (c + 18, c - 58)]
        d.polygon([(S(x), S(y)) for x, y in pts], fill=white)
    elif kind == "kom":
        d.polygon([(S(c - 60), S(c + 40)), (S(c - 16), S(c - 40)), (S(c + 28), S(c + 40))], fill=white)
        d.polygon([(S(c - 6), S(c + 40)), (S(c + 26), S(c - 14)), (S(c + 60), S(c + 40))], fill=white)
        # snow line cut
        d.polygon([(S(c - 30), S(c - 14)), (S(c - 16), S(c - 4)), (S(c - 4), S(c - 16)), (S(c + 6), S(c - 6)),
                   (S(c + 2), S(c + 4)), (S(c - 34), S(c + 4))], fill=(22, 30, 58, 180))
    elif kind == "tt":
        r = 44
        d.ellipse([S(c - r), S(c - r + 8), S(c + r), S(c + r + 8)], outline=white, width=int(round(S(9))))
        d.rectangle([S(c - 9), S(c - r - 12), S(c + 9), S(c - r - 2)], fill=white)
        d.rectangle([S(c - 4), S(c - r - 4), S(c + 4), S(c - r + 4)], fill=white)
        d.line([S(c + r * 0.72), S(c - r * 0.72 + 8), S(c + r * 0.9), S(c - r * 0.9 + 8)], fill=white, width=int(round(S(7))))
        d.line([S(c), S(c + 8), S(c + 18), S(c - 20)], fill=white, width=int(round(S(7))))
        d.ellipse([S(c - 6), S(c + 2), S(c + 6), S(c + 14)], fill=white)
    elif kind == "elite":
        glyph_flags(d, c, c + 18, 0.78)
        crown = [(c - 34, c - 18), (c - 38, c - 52), (c - 18, c - 34), (c, c - 60), (c + 18, c - 34),
                 (c + 38, c - 52), (c + 34, c - 18)]
        d.polygon([(S(x), S(y)) for x, y in crown], fill=(255, 226, 120, 255))
        d.line([(S(x), S(y)) for x, y in crown + [crown[0]]], fill=white, width=int(round(S(3))))
    return g


def race_icon(kind, rgb):
    W = 256
    img = canvas(W)
    # diamond = rounded square rotated 45 deg
    sq = mask(W)
    inset = 52
    ImageDraw.Draw(sq).rounded_rectangle([S(inset), S(inset), S(W - inset), S(W - inset)], radius=S(22), fill=255)
    dia = sq.rotate(45, resample=Image.BICUBIC)
    # outer glow
    glow = dia.filter(ImageFilter.MaxFilter(S(4) + 1)).filter(ImageFilter.GaussianBlur(S(9)))
    img.alpha_composite(Image.composite(Image.new("RGBA", glow.size, rgb + (255,)),
                                        Image.new("RGBA", glow.size, (0, 0, 0, 0)),
                                        glow.point(lambda v: int(v * 0.85))))
    # border stroke
    border = dia.filter(ImageFilter.MaxFilter(S(3) + 1))
    light = tuple(min(255, int(c + (255 - c) * 0.55)) for c in rgb)
    img.alpha_composite(Image.composite(grad_fill(border.size, light, rgb),
                                        Image.new("RGBA", border.size, (0, 0, 0, 0)), border))
    # navy fill
    inner = dia.filter(ImageFilter.MinFilter(S(4) + 1))
    y0, y1 = bbox_y(inner)
    fill = grad_fill(inner.size, (44, 66, 128), (18, 26, 62), (y0, y1))
    img.alpha_composite(Image.composite(fill, Image.new("RGBA", fill.size, (0, 0, 0, 0)), inner))
    # inner tint wash + pointer triangle near the bottom tip
    wash = Image.new("RGBA", img.size, rgb + (0,))
    wmask = inner.point(lambda v: int(v * 0.16))
    img.alpha_composite(Image.composite(Image.new("RGBA", img.size, rgb + (255,)), wash, wmask))
    d = ImageDraw.Draw(img)
    c = W / 2
    d.polygon([(S(c - 13), S(c + 70)), (S(c + 13), S(c + 70)), (S(c), S(c + 84))], fill=light + (255,))
    # glyph, scaled into the diamond centre
    g = glyph(kind, 160)
    img.alpha_composite(g, (S(48), S(38)))
    return down(img, W)


def radial(size, inner=0.0, soft=True):
    img = Image.new("RGBA", (size, size))
    px = img.load()
    c = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            r = math.hypot(x - c, y - c) / c
            a = max(0.0, 1 - r) ** 2 if soft else 0
            px[x, y] = (255, 255, 255, int(255 * a))
    return img


def ring(size):
    img = Image.new("RGBA", (size, size))
    px = img.load()
    c = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            r = math.hypot(x - c, y - c) / c
            band = math.exp(-((r - 0.78) / 0.07) ** 2)          # bright ring
            halo = math.exp(-((r - 0.78) / 0.22) ** 2) * 0.45   # soft glow around it
            fill = 0.18 * max(0.0, 1 - r / 0.78) if r < 0.78 else 0  # faint disc inside
            a = min(1.0, band + halo + fill)
            px[x, y] = (255, 255, 255, int(255 * a))
    return img


def beam(w, h):
    img = Image.new("RGBA", (w, h))
    px = img.load()
    for y in range(h):
        v = y / (h - 1)  # 0 top .. 1 bottom
        vert = (v ** 1.5) * (1 - max(0, v - 0.96) / 0.04)
        for x in range(w):
            u = abs(x / (w - 1) - 0.5) * 2
            core = math.exp(-(u / 0.18) ** 2)
            soft = math.exp(-(u / 0.55) ** 2) * 0.5
            px[x, y] = (255, 255, 255, int(255 * min(1, (core + soft) * vert)))
    return img


def sparkle(size):
    img = canvas(size)
    d = ImageDraw.Draw(img)
    c = size / 2
    # 4-point star as two thin diamonds
    d.polygon([(S(c), S(2)), (S(c + 5), S(c)), (S(c), S(size - 2)), (S(c - 5), S(c))], fill=(255, 255, 255, 255))
    d.polygon([(S(2), S(c)), (S(c), S(c - 5)), (S(size - 2), S(c)), (S(c), S(c + 5))], fill=(255, 255, 255, 255))
    img = down(img, size)
    glow = radial(size)
    glow.putalpha(glow.getchannel("A").point(lambda v: int(v * 0.6)))
    glow.alpha_composite(img)
    return glow



# ------------------------------------------------------------------ banners and boards
def font(size):
    from PIL import ImageFont
    for f in ("C:/Windows/Fonts/ariblk.ttf", "C:/Windows/Fonts/arialbd.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def checker_band(d, x0, y0, x1, y1, cell):
    n = 0
    y = y0
    while y < y1:
        x = x0
        k = n
        while x < x1:
            if k % 2 == 0:
                d.rectangle([x, y, min(x1, x + cell) - 1, min(y1, y + cell) - 1], fill=(250, 250, 250, 255))
            else:
                d.rectangle([x, y, min(x1, x + cell) - 1, min(y1, y + cell) - 1], fill=(20, 22, 30, 255))
            x += cell
            k += 1
        y += cell
        n += 1


def banner(text, accent):
    W, H = 1024, 192
    img = Image.new("RGBA", (W, H), (18, 26, 58, 255))
    d = ImageDraw.Draw(img)
    for y in range(H):  # navy gradient
        t = y / (H - 1)
        c = tuple(int(a + (b - a) * t) for a, b in zip((40, 60, 122), (14, 20, 48)))
        d.line([(0, y), (W, y)], fill=c + (255,))
    checker_band(d, 0, 0, W, 24, 24)
    checker_band(d, 0, H - 24, W, H, 24)
    d.rectangle([0, 24, W, 30], fill=accent + (255,))
    d.rectangle([0, H - 30, W, H - 24], fill=accent + (255,))
    f = font(104)
    tw = d.textlength(text, font=f)
    d.text(((W - tw) / 2 + 4, 28 + 4), text, font=f, fill=(0, 0, 0, 160))
    d.text(((W - tw) / 2, 28), text, font=f, fill=(255, 255, 255, 255))
    lm = maple_mask(64, 64, 56)
    lc = canvas(128)
    paint(lc, lm, "crimson")
    lc = down(lc, 128).resize((96, 96), Image.LANCZOS)
    img.alpha_composite(lc, (40, 48))
    img.alpha_composite(lc, (W - 136, 48))
    return img


def board(text, accent):
    W = 256
    img = Image.new("RGBA", (W, W), accent + (255,))
    d = ImageDraw.Draw(img)
    d.rectangle([10, 10, W - 11, W - 11], outline=(255, 255, 255, 255), width=10)
    f = font(96)
    tw = d.textlength(text, font=f)
    d.text(((W - tw) / 2, 50), text, font=f, fill=(255, 255, 255, 255))
    f2 = font(56)
    tw = d.textlength("m", font=f2)
    d.text(((W - tw) / 2, 150), "m", font=f2, fill=(255, 255, 255, 255))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    for kind, rgb in RACE_TYPES.items():
        race_icon(kind, rgb).save(os.path.join(OUT, f"race_icon_{kind}.png"))
    radial(256).save(os.path.join(OUT, "race_glow.png"))
    ring(256).save(os.path.join(OUT, "race_ring.png"))
    beam(64, 256).save(os.path.join(OUT, "race_beam.png"))
    sparkle(64).save(os.path.join(OUT, "race_sparkle.png"))
    banner("START", (0x3F, 0xA2, 0xFF)).save(os.path.join(OUT, "race_banner_start.png"))
    banner("FINISH", (0xFF, 0xB4, 0x2E)).save(os.path.join(OUT, "race_banner_finish.png"))
    board("200", (0x2F, 0x6F, 0xD8)).save(os.path.join(OUT, "race_board_200.png"))
    board("100", (0xE0, 0x3A, 0x48)).save(os.path.join(OUT, "race_board_100.png"))
    for i in range(17):
        insignia(i).save(os.path.join(OUT, f"insignia_{i:02d}.png"))
    # contact sheet for review (not imported: lives outside Assets)
    sheet = Image.new("RGBA", (17 * 132 + 4, 140 + 264), (24, 28, 40, 255))
    for i in range(17):
        sheet.alpha_composite(Image.open(os.path.join(OUT, f"insignia_{i:02d}.png")), (4 + i * 132, 6))
    for k, kind in enumerate(RACE_TYPES):
        sheet.alpha_composite(Image.open(os.path.join(OUT, f"race_icon_{kind}.png")), (4 + k * 262, 140))
    ref = os.path.join(ROOT, "reference", "good_graphics", "race")
    os.makedirs(ref, exist_ok=True)
    sheet.save(os.path.join(ref, "race_icon_sheet.png"))
    print("[race-icons] wrote", OUT)


if __name__ == "__main__":
    main()

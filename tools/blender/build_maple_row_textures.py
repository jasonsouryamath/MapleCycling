"""Maple Row boutique street textures (task C6, Maple City route metres 450-700).

Pure Python + PIL - no Blender needed:
    python tools/blender/build_maple_row_textures.py

Writes Assets/Environment/MapleCity/MapleRow/Textures/MapleRow_*.png:
  Sign_<BRAND>    1024x256  fascia wordmark panel (also used on the blade signs)
  Window_<BRAND>  1024x512  lit shop-window interior card
  Facade_<BRAND>  512/1024  upper-storey cladding (tiling, or a full-face mural)
  Paving, Carpet, Totem, Banner, Screen, Door

Every brand and mark here is fictional and drawn from plain geometry. No real names or logos.
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "MapleCity", "MapleRow", "Textures")
FONTS = r"C:\Windows\Fonts"
SS = 2  # supersample factor


def hexc(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def font(name, size):
    try:
        return ImageFont.truetype(os.path.join(FONTS, name), size)
    except OSError:
        return ImageFont.truetype(os.path.join(FONTS, "arialbd.ttf"), size)


def canvas(w, h, col):
    return Image.new("RGBA", (w * SS, h * SS), col)


def save(img, name, size=None):
    if SS != 1:
        img = img.resize((img.width // SS, img.height // SS), Image.LANCZOS)
    if size:
        img = img.resize(size, Image.LANCZOS)
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, f"MapleRow_{name}.png")
    img.convert("RGB").save(path, optimize=True)
    print(f"[maple-row-tex] {path}  {img.width}x{img.height}")


def tracked_text(draw, xy, text, fnt, fill, tracking=0, anchor_center=True):
    """Draws text with extra letter spacing; xy is the centre when anchor_center."""
    widths = [draw.textlength(ch, font=fnt) for ch in text]
    total = sum(widths) + tracking * (len(text) - 1)
    x, y = xy
    if anchor_center:
        x -= total / 2
    for ch, w in zip(text, widths):
        draw.text((x, y), ch, font=fnt, fill=fill, anchor="lm")
        x += w + tracking
    return total


def noise(img, amount, seed, blur=0.0):
    """Deterministic monochrome grain, +-amount per channel."""
    rng = np.random.default_rng(seed)
    n = rng.uniform(-amount, amount, (img.height, img.width)).astype(np.float32)
    if blur:
        nimg = Image.fromarray(np.clip(n + 128, 0, 255).astype(np.uint8), "L")
        n = np.asarray(nimg.filter(ImageFilter.GaussianBlur(blur)), dtype=np.float32) - 128
    a = np.asarray(img.convert("RGB"), dtype=np.float32) + n[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def glow(img, box, col, radius):
    """Soft additive light blob."""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse(box, fill=col)
    layer = layer.filter(ImageFilter.GaussianBlur(radius))
    return Image.alpha_composite(img, layer)


# ------------------------------------------------------------------------------------ brands
BRANDS = {
    "ARDENT": dict(bg="#2B2D33", fg="#F2EFE8", accent="#E86A8E", sub="CYCLE CLUB",
                   font=("segoeuil.ttf", 150), track=46, subfont=("segoeui.ttf", 40)),
    "HALCYON": dict(bg="#16334A", fg="#FF6F59", accent="#1FA3A0", sub="",
                    font=("impact.ttf", 170), track=10, subfont=("segoeuib.ttf", 40)),
    "ALPENTEK": dict(bg="#F4F6F8", fg="#0F1012", accent="#D4002A", sub="SWISS PERFORMANCE",
                     font=("arialbd.ttf", 150), track=-4, subfont=("arial.ttf", 34)),
    "CARBONFORGE": dict(bg="#101114", fg="#D9DDE2", accent="#FF7A1A", sub="SUPERBIKE FRAMES",
                        font=("ARIALNB.TTF", 160), track=6, subfont=("ARIALNB.TTF", 44)),
    "APEX": dict(bg="#0B1622", fg="#EAF6FF", accent="#27D3F5", sub="INSTRUMENTS",
                 font=("segoeuisl.ttf", 170), track=60, subfont=("segoeui.ttf", 40)),
    "AEROLITE": dict(bg="#17181B", fg="#FFFFFF", accent="#FF3B30", sub="HELMETS",
                     font=("arialbi.ttf", 160), track=0, subfont=("arialbi.ttf", 38)),
}


def mark(d, brand, cx, cy, s, b):
    """Small original geometric brand mark, centred at (cx, cy), size s."""
    acc, fg = hexc(b["accent"]), hexc(b["fg"])
    if brand == "ARDENT":        # thin chevron over a bar
        w = s * 0.08
        d.line([(cx - s * .5, cy + s * .15), (cx, cy - s * .3), (cx + s * .5, cy + s * .15)], fill=acc, width=int(w))
        d.line([(cx - s * .5, cy + s * .42), (cx + s * .5, cy + s * .42)], fill=acc, width=int(w))
    elif brand == "HALCYON":     # half sun over waves
        d.pieslice([cx - s * .5, cy - s * .5, cx + s * .5, cy + s * .5], 180, 360, fill=acc)
        for k in range(3):
            y = cy + s * (.08 + k * .16)
            pts = [(cx - s * .5 + i * s / 20, y + math.sin(i * 0.9) * s * .04) for i in range(21)]
            d.line(pts, fill=fg, width=int(s * .06))
    elif brand == "ALPENTEK":    # red square with a white peak
        d.rectangle([cx - s * .45, cy - s * .45, cx + s * .45, cy + s * .45], fill=acc)
        d.polygon([(cx - s * .3, cy + s * .25), (cx - s * .05, cy - s * .25), (cx + s * .08, cy),
                   (cx + s * .15, cy - s * .1), (cx + s * .32, cy + s * .25)], fill=(255, 255, 255, 255))
    elif brand == "CARBONFORGE":  # wheel: rim ring + hub + 5 bladed spokes
        d.ellipse([cx - s * .5, cy - s * .5, cx + s * .5, cy + s * .5], outline=fg, width=int(s * .12))
        d.ellipse([cx - s * .1, cy - s * .1, cx + s * .1, cy + s * .1], fill=acc)
        for k in range(5):
            a = k * 2 * math.pi / 5 - math.pi / 2
            d.line([(cx, cy), (cx + math.cos(a) * s * .42, cy + math.sin(a) * s * .42)], fill=fg, width=int(s * .05))
    elif brand == "APEX":        # ring with a peak trace inside and a glowing tip
        d.ellipse([cx - s * .5, cy - s * .5, cx + s * .5, cy + s * .5], outline=fg, width=int(s * .06))
        d.line([(cx - s * .34, cy + s * .2), (cx - s * .1, cy - s * .05), (cx + s * .04, cy + s * .1),
                (cx + s * .22, cy - s * .22)], fill=acc, width=int(s * .07))
        d.ellipse([cx + s * .14, cy - s * .30, cx + s * .30, cy - s * .14], fill=acc)
    elif brand == "AEROLITE":    # swept wing
        d.polygon([(cx - s * .55, cy + s * .25), (cx + s * .55, cy - s * .35), (cx + s * .2, cy + s * .05),
                   (cx + s * .5, cy + s * .05), (cx - s * .1, cy + s * .35)], fill=acc)


def sign(brand, b):
    W, H = 1024, 256
    img = canvas(W, H, hexc(b["bg"]))
    d = ImageDraw.Draw(img)
    S = SS
    f = font(b["font"][0], b["font"][1] * S)
    fg, acc = hexc(b["fg"]), hexc(b["accent"])
    has_sub = bool(b["sub"])
    ty = (H * 0.44 if has_sub else H * 0.52) * S
    # measure, then place the mark to the left of the wordmark
    tmp = ImageDraw.Draw(Image.new("RGBA", (10, 10)))
    widths = [tmp.textlength(ch, font=f) for ch in brand]
    track = b["track"] * S
    tw = sum(widths) + track * (len(brand) - 1)
    ms = 120 * S
    gap = 40 * S
    total = ms + gap + tw
    if total > (W - 60) * S:  # scale the wordmark down for long names
        k = ((W - 60) * S - ms - gap) / tw
        f = font(b["font"][0], int(b["font"][1] * S * k))
        widths = [tmp.textlength(ch, font=f) for ch in brand]
        track *= k
        tw = sum(widths) + track * (len(brand) - 1)
        total = ms + gap + tw
    x0 = (W * S - total) / 2
    mark(d, brand, x0 + ms / 2, H * S * 0.5, ms, b)
    tracked_text(d, (x0 + ms + gap + tw / 2, ty), brand, f, fg,
                 tracking=track, anchor_center=True)
    if has_sub:
        sf = font(b["subfont"][0], b["subfont"][1] * S)
        tracked_text(d, (x0 + ms + gap + tw / 2, H * S * 0.80), b["sub"], sf, acc, tracking=14 * S)
    # accent keyline + a thin frame
    if brand == "ARDENT":
        d.rectangle([0, H * S - 16 * S, W * S, H * S], fill=acc)
    d.rectangle([6 * S, 6 * S, W * S - 6 * S, H * S - 6 * S], outline=(*fg[:3], 70), width=3 * S)
    save(img, f"Sign_{brand}")


# ------------------------------------------------------------------------------ window cards
def jersey(d, cx, top, w, h, body, band):
    """Short-sleeve jersey silhouette on a hanger."""
    sh = w * 0.5
    pts = [(cx - w * .18, top), (cx - sh, top + h * .1), (cx - sh * 1.05, top + h * .35),
           (cx - w * .34, top + h * .38), (cx - w * .32, top + h), (cx + w * .32, top + h),
           (cx + w * .34, top + h * .38), (cx + sh * 1.05, top + h * .35), (cx + sh, top + h * .1),
           (cx + w * .18, top)]
    d.polygon(pts, fill=body)
    d.rectangle([cx - w * .33, top + h * .45, cx + w * .33, top + h * .56], fill=band)
    d.line([(cx, top - h * .12), (cx, top)], fill=(40, 40, 40, 255), width=max(2, int(w * .03)))


def interior(brand, b):
    W, H = 1024, 512
    S = SS
    rnd = random.Random(sum(map(ord, brand)))
    if brand == "ARDENT":
        img = canvas(W, H, hexc("#3A3530"))
        d = ImageDraw.Draw(img)
        d.rectangle([0, H * S * .78, W * S, H * S], fill=hexc("#6B4A32"))           # oak floor
        for k in range(12):
            d.line([(k * W * S / 12, H * S * .78), (k * W * S / 12 - 90 * S, H * S)], fill=hexc("#5A3E2A"), width=2 * S)
        for x in (180, 512, 844):
            img = glow(img, [(x - 150) * S, 20 * S, (x + 150) * S, 330 * S], (255, 214, 160, 90), 40 * S)
        d = ImageDraw.Draw(img)
        d.line([(60 * S, 150 * S), (W * S - 60 * S, 150 * S)], fill=hexc("#B08D57"), width=5 * S)  # brass rail
        cols = [("#2B2D33", "#E86A8E"), ("#F2EFE8", "#1E2A44"), ("#1B1C20", "#E86A8E"),
                ("#E86A8E", "#2B2D33"), ("#2B2D33", "#F2EFE8"), ("#1E2A44", "#E86A8E")]
        for k, (c1, c2) in enumerate(cols):
            jersey(d, (130 + k * 153) * S, 168 * S, 120 * S, 190 * S, hexc(c1), hexc(c2))
        d.rectangle([380 * S, 400 * S, 644 * S, 440 * S], fill=hexc("#2B2D33"))   # low table
        d.rectangle([400 * S, 386 * S, 470 * S, 400 * S], fill=hexc("#E86A8E"))
        d.rectangle([520 * S, 386 * S, 600 * S, 400 * S], fill=hexc("#F2EFE8"))
    elif brand == "HALCYON":
        img = canvas(W, H, hexc("#1FA3A0"))
        d = ImageDraw.Draw(img)
        d.ellipse([620 * S, -160 * S, 1120 * S, 340 * S], fill=hexc("#FF6F59"))
        d.polygon([(0, 330 * S), (420 * S, 60 * S), (560 * S, 60 * S), (140 * S, 330 * S)], fill=hexc("#16334A"))
        d.rectangle([0, H * S * .80, W * S, H * S], fill=hexc("#F3E9DA"))
        palette = [("#1FA3A0", "#FF6F59"), ("#FF8A3D", "#3A1C5C"), ("#16334A", "#1FA3A0"),
                   ("#FF6F59", "#16334A"), ("#FFFFFF", "#FF6F59"), ("#3A1C5C", "#FF8A3D"),
                   ("#FFD23F", "#16334A"), ("#FF6F59", "#1FA3A0")]
        for k, (c1, c2) in enumerate(palette):
            col, row = k % 4, k // 4
            cx = (160 + col * 235) * S
            d.rectangle([cx - 90 * S, (40 + row * 185) * S, cx + 90 * S, (205 + row * 185) * S], fill=hexc("#F3E9DA"))
            d.rectangle([cx - 90 * S, (40 + row * 185) * S, cx + 90 * S, (205 + row * 185) * S], outline=hexc("#16334A"), width=2 * S)
            jersey(d, cx, (60 + row * 185) * S, 110 * S, 130 * S, hexc(c1), hexc(c2))
    elif brand == "ALPENTEK":
        img = canvas(W, H, hexc("#EEF1F4"))
        d = ImageDraw.Draw(img)
        img = glow(img, [100 * S, -200 * S, 924 * S, 250 * S], (255, 255, 255, 160), 60 * S)
        d = ImageDraw.Draw(img)
        d.rectangle([0, 230 * S, W * S, 236 * S], fill=hexc("#D4002A"))            # red keyline
        d.rectangle([0, H * S * .82, W * S, H * S], fill=hexc("#C9CFD6"))
        for row, y in enumerate((150, 330)):
            d.rectangle([80 * S, y * S, 944 * S, (y + 12) * S], fill=hexc("#8E979F"))   # steel shelves
            for k in range(6):
                x = (120 + k * 140) * S
                c = hexc("#0F1012") if (k + row) % 3 else hexc("#E9EEF2")
                d.rectangle([x, (y - 70) * S, x + 100 * S, y * S], fill=c)
                d.rectangle([x, (y - 22) * S, x + 100 * S, (y - 14) * S], fill=hexc("#C8CDD3"))
    elif brand == "CARBONFORGE":
        img = canvas(W, H, hexc("#34373E"))
        d = ImageDraw.Draw(img)
        for y in range(0, H * S, 16 * S):
            for x in range(0, W * S, 16 * S):
                if ((x // (16 * S)) + (y // (16 * S))) % 2:
                    d.rectangle([x, y, x + 16 * S, y + 16 * S], fill=hexc("#3D4048"))
        for k in range(5):
            cx = (112 + k * 200) * S
            img = glow(img, [cx - 120 * S, 60 * S, cx + 120 * S, 400 * S], (255, 236, 210, 90), 36 * S)
        d = ImageDraw.Draw(img)
        d.rectangle([0, 440 * S, W * S, 452 * S], fill=hexc("#FF7A1A"))
        img = glow(img, [0, 400 * S, W * S, 500 * S], (255, 122, 26, 90), 30 * S)
        d = ImageDraw.Draw(img)
        for k in range(5):
            cx, cy, r = (112 + k * 200) * S, 230 * S, 92 * S
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=hexc("#0A0A0B"))              # tyre
            d.ellipse([cx - r * .88, cy - r * .88, cx + r * .88, cy + r * .88], fill=hexc("#2A2C31"))  # deep rim
            d.ellipse([cx - r * .62, cy - r * .62, cx + r * .62, cy + r * .62], fill=hexc("#141518"))
            for s_ in range(18):
                a = s_ * 2 * math.pi / 18
                d.line([(cx, cy), (cx + math.cos(a) * r * .62, cy + math.sin(a) * r * .62)], fill=hexc("#6E737B"), width=S)
            d.ellipse([cx - r * .1, cy - r * .1, cx + r * .1, cy + r * .1], fill=hexc("#FF7A1A"))
            d.text((cx, cy + r * .75), "CF", font=font("ARIALNB.TTF", 22 * S), fill=hexc("#D9DDE2"), anchor="mm")
    elif brand == "APEX":
        img = canvas(W, H, hexc("#0B1622"))
        d = ImageDraw.Draw(img)
        d.rectangle([0, H * S * .80, W * S, H * S], fill=hexc("#101E2C"))
        d.rectangle([0, 300 * S, W * S, 306 * S], fill=hexc("#27D3F5"))                 # display counter edge
        img = glow(img, [0, 270 * S, W * S, 340 * S], (39, 211, 245, 80), 20 * S)
        d = ImageDraw.Draw(img)
        readouts = [("312", "W"), ("42.6", "KM/H"), ("148", "BPM")]
        for k, (big, small) in enumerate(readouts):
            col, row = k, 0
            x0, y0 = (70 + col * 310) * S, (40 + row * 190) * S
            img = glow(img, [x0 - 20 * S, y0 - 20 * S, x0 + 280 * S, y0 + 170 * S], (39, 211, 245, 70), 22 * S)
            d = ImageDraw.Draw(img)
            d.rounded_rectangle([x0, y0, x0 + 260 * S, y0 + 150 * S], radius=16 * S, fill=hexc("#07121B"), outline=hexc("#27D3F5"), width=3 * S)
            d.text((x0 + 130 * S, y0 + 68 * S), big, font=font("bahnschrift.ttf", 70 * S), fill=hexc("#EAF6FF"), anchor="mm")
            d.text((x0 + 130 * S, y0 + 125 * S), small, font=font("segoeuib.ttf", 24 * S), fill=hexc("#27D3F5"), anchor="mm")
    else:  # AEROLITE
        img = canvas(W, H, hexc("#D9DCE0"))
        d = ImageDraw.Draw(img)
        d.rectangle([0, H * S * .78, W * S, H * S], fill=hexc("#B9BDC3"))
        cols = ["#101114", "#F4F4F4", "#FF3B30", "#2F80ED", "#101114"]
        for k, c in enumerate(cols):
            cx = (120 + k * 196) * S
            img = glow(img, [cx - 110 * S, 40 * S, cx + 110 * S, 380 * S], (255, 250, 240, 120), 30 * S)
            d = ImageDraw.Draw(img)
            d.rectangle([cx - 55 * S, 300 * S, cx + 55 * S, 420 * S], fill=hexc("#F7F7F7"))   # plinth
            d.rectangle([cx - 55 * S, 296 * S, cx + 55 * S, 304 * S], fill=hexc("#FFFBEA"))
            d.chord([cx - 80 * S, 190 * S, cx + 80 * S, 330 * S], 180, 360, fill=hexc(c))       # helmet shell
            for v in range(3):
                vx = cx + (v - 1) * 36 * S
                d.ellipse([vx - 10 * S, 222 * S, vx + 10 * S, 246 * S], fill=hexc("#2A2B2F"))
    img = noise(img, 6, sum(map(ord, brand)))
    save(img, f"Window_{brand}")


# ----------------------------------------------------------------------------------- facades
def facade(brand, b):
    S = 1
    if brand == "ARDENT":        # charcoal fluted panels, tiles every 2 m (512 px)
        img = Image.new("RGBA", (512, 512), hexc("#3A3C44"))
        d = ImageDraw.Draw(img)
        for x in range(0, 512, 32):
            d.rectangle([x, 0, x + 3, 512], fill=hexc("#24262B"))
            d.rectangle([x + 4, 0, x + 7, 512], fill=hexc("#3A3D45"))
        img = noise(img, 5, 11)
        img.convert("RGB").save(os.path.join(OUT, "MapleRow_Facade_ARDENT.png")); print("[maple-row-tex] Facade_ARDENT")
        return
    if brand == "HALCYON":       # full-face mural, not tiling
        img = Image.new("RGBA", (1024, 512), hexc("#F3E9DA"))
        d = ImageDraw.Draw(img)
        d.polygon([(0, 512), (0, 250), (380, 0), (620, 0), (180, 512)], fill=hexc("#1FA3A0"))
        d.polygon([(260, 512), (700, 0), (820, 0), (400, 512)], fill=hexc("#16334A"))
        d.ellipse([640, 120, 1000, 480], fill=hexc("#FF6F59"))
        d.polygon([(560, 512), (1024, 140), (1024, 300), (760, 512)], fill=hexc("#1FA3A0"))
        for k in range(7):
            y = 60 + k * 26
            d.line([(40, y), (300, y - 150)], fill=hexc("#FF6F59"), width=8)
        d.ellipse([90, 330, 200, 440], outline=hexc("#16334A"), width=14)
        img = noise(img, 7, 12)
        img.convert("RGB").save(os.path.join(OUT, "MapleRow_Facade_HALCYON.png")); print("[maple-row-tex] Facade_HALCYON")
        return
    if brand == "ALPENTEK":      # white panels + steel-framed glass, precise 1 m grid (tile 2 m)
        img = Image.new("RGBA", (512, 512), hexc("#F1F3F5"))
        d = ImageDraw.Draw(img)
        d.rectangle([0, 150, 512, 400], fill=hexc("#4F6272"))                        # glass band
        for x in range(0, 512, 128):
            d.rectangle([x, 150, x + 6, 400], fill=hexc("#AEB6BE"))
        d.rectangle([0, 150, 512, 156], fill=hexc("#AEB6BE")); d.rectangle([0, 394, 512, 400], fill=hexc("#AEB6BE"))
        grad = Image.new("RGBA", (512, 250), (0, 0, 0, 0))
        gd = ImageDraw.Draw(grad)
        for y in range(250):
            gd.line([(0, y), (512, y)], fill=(255, 255, 255, int(70 * (1 - y / 250))))
        img.alpha_composite(grad, (0, 150))
        d = ImageDraw.Draw(img)
        for x in range(0, 512, 256):
            d.line([(x, 0), (x, 150)], fill=hexc("#D5DADF"), width=2)
            d.line([(x, 400), (x, 512)], fill=hexc("#D5DADF"), width=2)
        img = noise(img, 3, 13)
        img.convert("RGB").save(os.path.join(OUT, "MapleRow_Facade_ALPENTEK.png")); print("[maple-row-tex] Facade_ALPENTEK")
        return
    if brand == "CARBONFORGE":   # 2x2 twill carbon weave (tile 1 m)
        img = Image.new("RGBA", (512, 512), hexc("#111214"))
        d = ImageDraw.Draw(img)
        t = 32
        for j in range(512 // t):
            for i in range(512 // t):
                x, y = i * t, j * t
                warp = ((i + j) // 2) % 2 == 0
                for k in range(t):
                    shade = int(58 + 40 * math.sin(k / t * math.pi))
                    if warp:
                        d.line([(x + k, y), (x + k, y + t - 1)], fill=(shade, shade + 1, shade + 4, 255))
                    else:
                        d.line([(x, y + k), (x + t - 1, y + k)], fill=(shade - 8, shade - 7, shade - 4, 255))
        img = noise(img, 4, 14)
        img.convert("RGB").save(os.path.join(OUT, "MapleRow_Facade_CARBONFORGE.png")); print("[maple-row-tex] Facade_CARBONFORGE")
        return
    if brand == "APEX":          # light tech cladding, dark ribbon + cyan line (tile 2 m)
        img = Image.new("RGBA", (512, 512), hexc("#E3E7EB"))
        d = ImageDraw.Draw(img)
        for y in range(0, 512, 64):
            d.line([(0, y), (512, y)], fill=hexc("#C7CDD3"), width=2)
        d.rectangle([0, 220, 512, 330], fill=hexc("#1A2633"))
        d.rectangle([0, 272, 512, 277], fill=hexc("#27D3F5"))
        img = noise(img, 3, 15)
        img.convert("RGB").save(os.path.join(OUT, "MapleRow_Facade_APEX.png")); print("[maple-row-tex] Facade_APEX")
        return
    # AEROLITE: graphite with sweeping speed lines, full face
    img = Image.new("RGBA", (1024, 512), hexc("#26282C"))
    d = ImageDraw.Draw(img)
    for k in range(9):
        y0 = 80 + k * 38
        pts = [(x, y0 + 90 * math.sin(x / 1024 * math.pi) - x * 0.12) for x in range(0, 1025, 16)]
        d.line(pts, fill=hexc("#FFFFFF") if k != 4 else hexc("#FF3B30"), width=6 if k != 4 else 14)
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    img = noise(img, 4, 16)
    img.convert("RGB").save(os.path.join(OUT, "MapleRow_Facade_AEROLITE.png")); print("[maple-row-tex] Facade_AEROLITE")


# ----------------------------------------------------------------------------------- shared
def paving():
    """Honed limestone slabs, 0.6 x 1.2 m running bond. 512 px = 2.4 m."""
    img = Image.new("RGBA", (512, 512), hexc("#CFC6B6"))
    d = ImageDraw.Draw(img)
    rnd = random.Random(7)
    ph, pw = 128, 256
    for j in range(4):
        off = (j % 2) * (pw // 2)
        for i in range(-1, 3):
            x0, y0 = i * pw + off, j * ph
            t = rnd.uniform(-10, 10)
            d.rectangle([x0 + 2, y0 + 2, x0 + pw - 2, y0 + ph - 2],
                        fill=(int(207 + t), int(199 + t), int(183 + t * 0.8), 255))
    for j in range(5):
        d.line([(0, j * ph), (512, j * ph)], fill=hexc("#9E9585"), width=3)
    for j in range(4):
        off = (j % 2) * (pw // 2)
        for i in range(-1, 3):
            x = i * pw + off
            d.line([(x, j * ph), (x, (j + 1) * ph)], fill=hexc("#9E9585"), width=3)
    img = noise(img, 8, 21, blur=0.6)
    img.convert("RGB").save(os.path.join(OUT, "MapleRow_Paving.png")); print("[maple-row-tex] Paving")


def carpet():
    img = Image.new("RGBA", (256, 256), hexc("#8E1022"))
    d = ImageDraw.Draw(img)
    for x in (10, 246):
        d.rectangle([x - 5, 0, x + 5, 256], fill=hexc("#C9A45C"))
    img = noise(img, 10, 22, blur=0.5)
    img.convert("RGB").save(os.path.join(OUT, "MapleRow_Carpet.png")); print("[maple-row-tex] Carpet")


def maple_leaf(d, cx, cy, s, col):
    """A simple original 5-lobed leaf polygon (not any flag or logo)."""
    half = [(0, -1), (0.14, -0.66), (0.32, -0.74), (0.26, -0.34), (0.58, -0.56), (0.56, -0.38),
            (0.92, -0.36), (0.74, -0.14), (0.84, -0.04), (0.42, 0.12), (0.48, 0.30), (0.08, 0.22),
            (0.05, 0.62)]
    right = [(cx + x * s, cy + y * s) for x, y in half]
    left = [(cx - x * s, cy + y * s) for x, y in reversed(half[1:])]
    d.polygon(right + left, fill=col)


def totem():
    W, H = 256, 1024
    S = SS
    img = canvas(W, H, hexc("#1D1F24"))
    d = ImageDraw.Draw(img)
    gold = hexc("#D9B26A")
    d.rectangle([14 * S, 14 * S, W * S - 14 * S, H * S - 14 * S], outline=gold, width=4 * S)
    maple_leaf(d, W * S / 2, 150 * S, 62 * S, hexc("#D9536E"))
    f = font("georgia.ttf", 96 * S)
    for k, ch in enumerate("MAPLE"):
        d.text((W * S / 2, (300 + k * 104) * S), ch, font=f, fill=gold, anchor="mm")
    d.line([(60 * S, 830 * S), (196 * S, 830 * S)], fill=gold, width=3 * S)
    f2 = font("georgia.ttf", 64 * S)
    d.text((W * S / 2, 900 * S), "ROW", font=f2, fill=gold, anchor="mm")
    d.text((W * S / 2, 965 * S), "BOUTIQUES", font=font("segoeui.ttf", 26 * S), fill=hexc("#E8E2D6"), anchor="mm")
    save(img, "Totem")


def banner():
    W, H = 256, 512
    S = SS
    img = canvas(W, H, hexc("#7A1F2E"))
    d = ImageDraw.Draw(img)
    gold = hexc("#E3C27E")
    maple_leaf(d, W * S / 2, 140 * S, 70 * S, gold)
    d.text((W * S / 2, 300 * S), "MAPLE", font=font("georgia.ttf", 58 * S), fill=hexc("#F6EFE2"), anchor="mm")
    d.text((W * S / 2, 370 * S), "ROW", font=font("georgia.ttf", 58 * S), fill=hexc("#F6EFE2"), anchor="mm")
    d.line([(50 * S, 430 * S), (206 * S, 430 * S)], fill=gold, width=3 * S)
    save(img, "Banner")


def screen():
    W, H = 512, 256
    S = SS
    img = canvas(W, H, hexc("#06111A"))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([8 * S, 8 * S, W * S - 8 * S, H * S - 8 * S], radius=20 * S, outline=hexc("#27D3F5"), width=4 * S)
    d.text((W * S * .5, H * S * .42), "305 W", font=font("bahnschrift.ttf", 96 * S), fill=hexc("#EAF6FF"), anchor="mm")
    d.text((W * S * .25, H * S * .80), "41.2 KM/H", font=font("segoeuib.ttf", 30 * S), fill=hexc("#27D3F5"), anchor="mm")
    d.text((W * S * .75, H * S * .80), "HR 151", font=font("segoeuib.ttf", 30 * S), fill=hexc("#7CFFB2"), anchor="mm")
    save(img, "Screen")


def door():
    W, H = 256, 512
    img = Image.new("RGBA", (W, H), hexc("#1A1D22"))
    d = ImageDraw.Draw(img)
    grad = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    for y in range(H):
        a = int(110 * (1 - abs(y / H - 0.45) * 1.8))
        gd.line([(0, y), (W, y)], fill=(255, 220, 170, max(0, a)))
    img.alpha_composite(grad)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W, H], outline=hexc("#0C0D10"), width=10)
    d.line([(W // 2, 0), (W // 2, H)], fill=hexc("#0C0D10"), width=8)
    for x in (W // 2 - 26, W // 2 + 20):
        d.rectangle([x, 200, x + 6, 330], fill=hexc("#C9A45C"))
    img.convert("RGB").save(os.path.join(OUT, "MapleRow_Door.png")); print("[maple-row-tex] Door")


def gallery_window():
    """Generic boutique / gallery interior for the unbranded infill shops."""
    W, H = 1024, 512
    S = SS
    img = canvas(W, H, hexc("#E9DFCC"))
    d = ImageDraw.Draw(img)
    d.rectangle([0, H * S * .80, W * S, H * S], fill=hexc("#7A5A3E"))
    for x in (200, 512, 824):
        img = glow(img, [(x - 160) * S, -40 * S, (x + 160) * S, 360 * S], (255, 226, 180, 110), 44 * S)
    d = ImageDraw.Draw(img)
    frames = [(90, 110, 250, 300, "#2F5D62"), (330, 90, 470, 290, "#C9784A"), (560, 120, 700, 280, "#3B3F63"),
              (780, 100, 940, 300, "#9C3D3D")]
    for x0, y0, x1, y1, c in frames:
        d.rectangle([x0 * S, y0 * S, x1 * S, y1 * S], fill=hexc("#2A2522"))
        d.rectangle([(x0 + 12) * S, (y0 + 12) * S, (x1 - 12) * S, (y1 - 12) * S], fill=hexc(c))
        d.ellipse([(x0 + 40) * S, (y0 + 50) * S, (x1 - 40) * S, (y1 - 40) * S], fill=hexc("#E9DFCC"))
    d.rectangle([380 * S, 350 * S, 644 * S, 410 * S], fill=hexc("#2A2522"))           # bench plinth
    img = noise(img, 5, 31)
    save(img, "Window_GALLERY")


def flat_window():
    """Warm-lit apartment window for the upper storeys: curtains, a lamp, a soft interior."""
    W, H = 256, 320
    S = SS
    img = canvas(W, H, hexc("#8A6A48"))
    img = glow(img, [30 * S, 40 * S, 226 * S, 280 * S], (255, 214, 150, 200), 40 * S)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 46 * S, H * S], fill=hexc("#E8DCC4"))
    d.rectangle([W * S - 46 * S, 0, W * S, H * S], fill=hexc("#E8DCC4"))
    d.rectangle([110 * S, 200 * S, 146 * S, 290 * S], fill=hexc("#3A2E24"))           # lamp stand
    d.polygon([(96 * S, 200 * S), (160 * S, 200 * S), (146 * S, 160 * S), (110 * S, 160 * S)], fill=hexc("#FFE8B8"))
    d.rectangle([0, 0, W * S, 10 * S], fill=hexc("#2B2B2E"))
    d.rectangle([0, H * S - 10 * S, W * S, H * S], fill=hexc("#2B2B2E"))
    d.rectangle([W * S // 2 - 4 * S, 0, W * S // 2 + 4 * S, H * S], fill=hexc("#2B2B2E"))
    save(img, "Window_FLAT")


def main():
    os.makedirs(OUT, exist_ok=True)
    for brand, b in BRANDS.items():
        sign(brand, b)
        interior(brand, b)
        facade(brand, b)
    gallery_window()
    flat_window()
    paving()
    carpet()
    totem()
    banner()
    screen()
    door()


if __name__ == "__main__":
    main()

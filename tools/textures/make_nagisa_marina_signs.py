"""
NAGISA BAY marina signage (worker G, brief section 7). Fictional branding only.
Writes Assets/Environment/NagisaBay/Textures/Marina/M2_Sign_*.png (1024 x 256, sRGB).
Run: python tools/textures/make_nagisa_marina_signs.py
"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures", "Marina")
os.makedirs(OUT, exist_ok=True)
FONTS = "C:/Windows/Fonts/"


def font(name, size):
    for n in (name, "arialbd.ttf"):
        try:
            return ImageFont.truetype(FONTS + n, size)
        except OSError:
            continue
    return ImageFont.load_default()


def centred(d, text, f, y, fill, W=1024):
    w = d.textlength(text, font=f)
    d.text(((W - w) / 2, y), text, font=f, fill=fill)


def sign(name, top, jp, sub, bg, accent, fg=(255, 255, 255)):
    W, H = 1024, 256
    im = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W - 1, H - 1], outline=accent, width=10)
    d.rectangle([16, H - 40, W - 17, H - 24], fill=accent)             # stripe
    centred(d, top, font("arialbd.ttf", 92), 22, fg)
    centred(d, jp, font("YuGothB.ttc", 52), 124, fg)
    if sub:
        centred(d, sub, font("arialbd.ttf", 28), 186, (225, 235, 240))
    im.save(os.path.join(OUT, f"M2_Sign_{name}.png"))


sign("Marina", "NAGISA BAY MARINA", "\u6e1a\u30d9\u30a4\u30fb\u30de\u30ea\u30fc\u30ca", "YACHT CLUB  -  CAFES  -  BIKE PARKING",
     (16, 46, 92), (36, 168, 168))
sign("Harbour", "NAGISA HARBOUR", "\u6e1a\u6f01\u6e2f\u30fb\u9b5a\u5e02\u5834", "WORKING FISH MARKET  -  FRESH DAILY",
     (28, 80, 120), (236, 130, 60))
sign("Cruise", "WATER TAXI & CRUISES", "\u30af\u30eb\u30fc\u30ba\u30fb\u6c34\u4e0a\u30bf\u30af\u30b7\u30fc", "DEPARTS EVERY 30 MIN",
     (24, 120, 130), (255, 255, 255))
sign("Club", "NAGISA YACHT CLUB", "\u30ca\u30ae\u30b5\u30fb\u30e8\u30c3\u30c8\u30af\u30e9\u30d6", "MEMBERS & GUESTS",
     (240, 240, 235), (16, 46, 92), fg=(16, 46, 92))
print("wrote 4 signs to", OUT)

"""
NB3 - Nagisa coastal highway gantry sign faces (plain Python + Pillow, no Blender needed).

Run: python tools/blender/build_nagisa_highway_signs.py
Out: Assets/Environment/NagisaBay/Textures/Highway/NB3_Sign_<k>.png (1024 x 384, sRGB)

Japanese-expressway style: green field, white border, kanji + romaji place names, distances,
lane arrows. ALL names are fictional (Nagisa IC, Palm Ridge, Shiokaze, Minato-ura, Kaigan JCT).
Fonts are the local Windows system fonts (rendered to pixels only; no font file is shipped).
PROVISIONAL: destinations/distances are illustrative placeholders.
"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures", "Highway")
W, H = 1024, 384
GREEN = (0, 112, 74)
BLUE = (22, 70, 150)
WHITE = (246, 248, 244)
FONT_JP = r"C:\Windows\Fonts\YuGothB.ttc"
FONT_EN = r"C:\Windows\Fonts\arialbd.ttf"

# (field colour, [(kanji, romaji, distance, arrow)], route shield)
SIGNS = [
    (GREEN, [("渚 IC", "Nagisa IC", "2 km", "↑"), ("汐風", "Shiokaze", "6 km", "↗")], "E71"),
    (GREEN, [("パームリッジ", "Palm Ridge", "4 km", "↑"), ("港浦", "Minato-ura", "9 km", "↑")], "E71"),
    (BLUE, [("渚ビーチ", "Nagisa Beach", "1 km", "←"), ("渚マリーナ", "Nagisa Marina", "3 km", "↖")], "71"),
    (GREEN, [("海岸 JCT", "Kaigan JCT", "3 km", "↑"), ("汐風", "Shiokaze", "5 km", "↗")], "E71"),
    (GREEN, [("汐風 出口", "Shiokaze EXIT", "500 m", "↗"), ("パームリッジ", "Palm Ridge", "7 km", "↑")], "E71"),
    (BLUE, [("渚ベイブリッジ", "Nagisa Bay Bridge", "2 km", "↑"), ("渚 IC", "Nagisa IC", "8 km", "↑")], "71"),
]


def font(path, size):
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        return ImageFont.load_default()


def shield(d, x, y, text):
    d.rounded_rectangle([x, y, x + 150, y + 78], 14, fill=(20, 120, 60) if text.startswith("E") else BLUE,
                        outline=WHITE, width=5)
    f = font(FONT_EN, 50)
    tw = d.textlength(text, font=f)
    d.text((x + 75 - tw / 2, y + 10), text, font=f, fill=WHITE)


def make(k, field, rows, route):
    img = Image.new("RGB", (W, H), field)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([10, 10, W - 11, H - 11], 26, outline=WHITE, width=8)
    fj, fe, fd, fa = font(FONT_JP, 92), font(FONT_EN, 40), font(FONT_EN, 62), font(FONT_JP, 110)
    for r, (jp, en, dist, arrow) in enumerate(rows):
        y = 34 + r * 166
        d.text((56, y - 6), arrow, font=fa, fill=WHITE)
        d.text((196, y), jp, font=fj, fill=WHITE)
        d.text((200, y + 104), en, font=fe, fill=WHITE)
        tw = d.textlength(dist, font=fd)
        d.text((W - (250 if r == 0 else 60) - tw, y + 40), dist, font=fd, fill=WHITE)
        if r == 0:
            d.line([40, y + 160, W - 40, y + 160], fill=WHITE, width=4)
    shield(d, W - 214, 26, route)
    path = os.path.join(OUT, "NB3_Sign_%d.png" % k)
    img.save(path)
    print("[nb3-signs] wrote", path)


def main():
    os.makedirs(OUT, exist_ok=True)
    for k, (field, rows, route) in enumerate(SIGNS):
        make(k, field, rows, route)


main()

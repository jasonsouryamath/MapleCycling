"""Paint the two localised sign faces used by Shiosai's tuna auction frontage."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Environment/ShiosaiCoast/Textures"
OUT.mkdir(parents=True, exist_ok=True)


def font(size, japanese=False):
    names = (["YuGothB.ttc", "msgothic.ttc"] if japanese else ["GOTHICB.TTF", "arialbd.ttf"])
    for name in names:
        path = Path("C:/Windows/Fonts") / name
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def centred(draw, y, s, face, fill, width):
    bounds = draw.textbbox((0, 0), s, font=face)
    draw.text(((width - (bounds[2] - bounds[0])) / 2, y), s, font=face, fill=fill)


w, h = 2048, 512
im = Image.new("RGB", (w, h), "#102E43")
d = ImageDraw.Draw(im)
d.rectangle((22, 22, w - 23, h - 23), outline="#E7DFCB", width=9)
d.rectangle((46, 47, w - 47, 54), fill="#BD4738")
centred(d, 67, "潮彩まぐろ市場", font(205, True), "#F5EEDB", w)
centred(d, 316, "SHIOSAI TUNA MARKET  •  鮪  •  FRESH DAILY", font(78), "#D8D7C9", w)
im.save(OUT / "Shiosai_TunaMarket_Sign.png")

im = Image.new("RGB", (512, 1024), "#EAE1CE")
d = ImageDraw.Draw(im)
d.rectangle((18, 18, 494, 1006), outline="#17455A", width=18)
d.rectangle((32, 32, 480, 140), fill="#17455A")
centred(d, 170, "まぐろ", font(125, True), "#123E53", 512)
centred(d, 345, "鮪", font(280, True), "#B63C35", 512)
centred(d, 765, "TUNA", font(90), "#123E53", 512)
im.save(OUT / "Shiosai_Tuna_Banner.png")
print(f"[tuna-sign] wrote two signs to {OUT}")

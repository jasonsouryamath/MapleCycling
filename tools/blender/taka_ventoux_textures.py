"""Taka (Mt. Ventoux theme) sign + road-paint textures. PIL only.
python tools/blender/taka_ventoux_textures.py -> Assets/Environment/TakaMountains/Textures/"""
from PIL import Image, ImageDraw, ImageFont
import os
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Environment', 'TakaMountains', 'Textures')
def font(sz):
    for f in ['C:/Windows/Fonts/arialbd.ttf', 'C:/Windows/Fonts/impact.ttf', 'arial.ttf']:
        try: return ImageFont.truetype(f, sz)
        except Exception: pass
    return ImageFont.load_default()
# road paint atlas: 4 rows of names, white on transparent (cutout)
names = ['ALLEZ KURO', 'VAS-Y HANA!', 'KENJI  KENJI', 'ALLEZ TAKA']
im = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0)); d = ImageDraw.Draw(im); f = font(150)
for k, n in enumerate(names):
    w = d.textlength(n, font=f)
    d.text(((1024 - w) / 2, k * 256 + 40), n, font=f, fill=(245, 245, 238, 255))
im.save(os.path.join(OUT, 'Taka_RoadNames.png'))
# summit sign: brown board, white text
im = Image.new('RGB', (1024, 384), (78, 50, 30)); d = ImageDraw.Draw(im)
d.rectangle([12, 12, 1011, 371], outline=(235, 230, 215), width=10)
for txt, y, sz in [('SOMMET DU MONT TAKA', 50, 86), ('ALTITUDE 2842 m', 175, 110)]:
    ff = font(sz); w = d.textlength(txt, font=ff); d.text(((1024 - w) / 2, y), txt, font=ff, fill=(240, 236, 222))
im.save(os.path.join(OUT, 'Taka_SummitSign.png'))
# shop signs
for name, txt, bg in [('Taka_Sign_Cafe', 'CAFE DU COL', (40, 70, 120)), ('Taka_Sign_Boulangerie', 'BOULANGERIE', (120, 40, 40)),
                      ('Taka_Sign_Chalet', 'CHALET REYNAUD', (60, 90, 50)), ('Taka_Sign_Crepes', 'CREPES  SOUVENIRS', (150, 70, 30))]:
    im = Image.new('RGB', (1024, 192), bg); d = ImageDraw.Draw(im); ff = font(110)
    w = d.textlength(txt, font=ff); d.text(((1024 - w) / 2, 30), txt, font=ff, fill=(250, 244, 225))
    im.save(os.path.join(OUT, name + '.png'))
print('ok')

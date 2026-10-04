"""
E2 signage trim sheet (Agent HQ ticket 'Build one signage trim sheet to replace per-sign 2k
textures', 2026-09-30). Packs the 9 existing Taka Mountains sign faces into ONE shared sheet so
TakaMountains.Ventoux.cs / TakaMountains.Living.cs load one texture + one Material instead of 9.

Does NOT redraw any art: reads the current PNGs byte-for-byte and pastes them into a packed
sheet, following the precedent of Sakura Pass's Sakura_Sign_Trim.png
(tools/textures/make_sakura_photoreal_textures.py).

Run: python tools/textures/make_taka_sign_trimsheet.py
Out: Assets/Environment/TakaMountains/Textures/Taka_Sign_TrimSheet.png (2048 x 1024, sRGB)
     Prints the per-sign pixel rects AND the UV rects (U left->right, V bottom->top, matching
     Unity's texture V convention) for TakaMountains.Ventoux.cs's TakaSignUV table.
"""
import os
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "design_assets", "taka_sign_sources")  # moved out of Assets 2026-10-01 (trim sheet replaced them)
OUT_DIR = os.path.join(ROOT, "Assets", "Environment", "TakaMountains", "Textures")
OUT_NAME = "Taka_Sign_TrimSheet.png"
SHEET_W, SHEET_H = 2048, 1024

# (filename, x0, y0) - sizes are read from each source file. Packed as two columns of the
# 192px-tall shop/gantry signs beside/under the one 384px-tall summit sign, with the half-width
# KOM banner tucked beside Village. Unused cells are filled neutral grey (same approach as
# Sakura's trim sheet).
LAYOUT = [
    ("Taka_SummitSign.png", 0, 0),
    ("Taka_Sign_Auberge.png", 1024, 0),
    ("Taka_Sign_Boulangerie.png", 1024, 192),
    ("Taka_Sign_Cafe.png", 0, 384),
    ("Taka_Sign_Chalet.png", 1024, 384),
    ("Taka_Sign_Crepes.png", 0, 576),
    ("Taka_Sign_Finish.png", 1024, 576),
    ("Taka_Sign_Village.png", 0, 768),
    ("Taka_Sign_KOM.png", 1024, 768),
]


def main():
    sheet = Image.new("RGB", (SHEET_W, SHEET_H), (128, 128, 128))
    rects = []
    for name, x0, y0 in LAYOUT:
        src = Image.open(os.path.join(SRC, name)).convert("RGB")
        w, h = src.size
        sheet.paste(src, (x0, y0))
        x1, y1 = x0 + w, y0 + h
        # Unity V is bottom-up; PIL y is top-down, so the sub-rect's top edge (pixel y0) is the
        # HIGHER V value and its bottom edge (pixel y1) is the LOWER V value.
        u0, u1 = x0 / SHEET_W, x1 / SHEET_W
        v0, v1 = 1.0 - y1 / SHEET_H, 1.0 - y0 / SHEET_H
        rects.append((name, x0, y0, x1, y1, u0, v0, u1, v1))

    out_path = os.path.join(OUT_DIR, OUT_NAME)
    sheet.save(out_path)
    print("[taka-sign-trim] wrote", out_path, sheet.size)
    print("[taka-sign-trim] C# table (paste into TakaMountains.Ventoux.cs TakaSignUV):")
    for name, x0, y0, x1, y1, u0, v0, u1, v1 in rects:
        print('        { "%s", new Rect(%sf, %sf, %sf, %sf) }, // px (%d,%d)-(%d,%d)' % (
            name, round(u0, 6), round(v0, 6), round(u1 - u0, 6), round(v1 - v0, 6), x0, y0, x1, y1))


main()

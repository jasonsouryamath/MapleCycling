"""Shunta Metro - 100 unique neon signs, 10 x 10 atlas of 512 x 256 cells (5120 x 2560).
Every sign has its own wording, icon, tube frame and colour pairing. Cell index n = 0..99: col = n % 10, row = n // 10 (row 0 = top).
Outputs Sign100_Atlas_Albedo.png, Sign100_Atlas_Emission.png and Sign100_Contact.png (preview, not for the game).
"""
import math
import os
import numpy as np
from shunta_tex_lib import *

W, H = 512, 256
PAL = {
    "pink": (255, 40, 150), "cyan": (0, 215, 255), "amber": (255, 165, 30), "violet": (140, 95, 255), "mint": (40, 255, 170),
    "red": (255, 60, 60), "yellow": (255, 235, 110), "lime": (120, 255, 70), "blue": (60, 120, 255), "magenta": (230, 50, 255),
    "orange": (255, 110, 30), "white": (235, 245, 255),
}

# (text, subtext, icon, frame, colour A, colour B, font)   font: j = Japanese gothic, i = impact, a = arial bold
S = [
    ("ラーメン", "RAMEN", "bowl", "round", "red", "yellow", "j"), ("居酒屋", "IZAKAYA", "mug", "double", "amber", "pink", "j"),
    ("カラオケ", "KARAOKE", "mic", "pill", "magenta", "cyan", "j"), ("薬局", "PHARMACY", "cross", "brackets", "mint", "white", "j"),
    ("焼肉", "YAKINIKU", "flame", "round", "orange", "red", "j"), ("寿司", "SUSHI", "fish", "arch", "cyan", "pink", "j"),
    ("麻雀", "MAHJONG", "dice", "double", "lime", "yellow", "j"), ("酒場", "SAKABA", "bottle", "ticket", "amber", "violet", "j"),
    ("純喫茶", "COFFEE", "cup", "round", "yellow", "orange", "j"), ("旅館", "RYOKAN", "torii", "arch", "red", "amber", "j"),
    ("天ぷら", "TEMPURA", "shrimp", "pill", "orange", "yellow", "j"), ("ホテル", "HOTEL", "bed", "double", "violet", "cyan", "j"),
    ("営業中", "OPEN", "none", "dashed", "lime", "mint", "j"), ("整骨院", "CLINIC", "cross", "round", "mint", "cyan", "j"),
    ("歯科", "DENTAL", "tooth", "pill", "white", "cyan", "j"), ("古書店", "BOOKS", "book", "brackets", "amber", "yellow", "j"),
    ("24時間営業", "OPEN 24H", "moon", "double", "cyan", "violet", "j"), ("パチンコ", "PACHINKO", "star", "dashed", "magenta", "yellow", "j"),
    ("ゲームセンター", "GAME CENTER", "pad", "round", "pink", "cyan", "j"), ("お台場", "ODAIBA", "wave", "arch", "blue", "cyan", "j"),
    ("中華そば", "CHUKA SOBA", "bowl", "ticket", "yellow", "red", "j"), ("焼き鳥", "YAKITORI", "skewer", "pill", "orange", "amber", "j"),
    ("BAR", "COCKTAILS", "glass", "round", "pink", "violet", "i"), ("COFFEE", "ROASTERS", "cup", "double", "amber", "white", "i"),
    ("SALE", "50% OFF", "tag", "dashed", "red", "yellow", "i"), ("OPEN", "WELCOME", "none", "round", "lime", "white", "i"),
    ("たこ焼き", "TAKOYAKI", "octopus", "pill", "orange", "pink", "j"), ("うどん", "UDON", "bowl", "brackets", "amber", "white", "j"),
    ("そば", "SOBA", "bowl", "arch", "mint", "yellow", "j"), ("カレー", "CURRY", "flame", "ticket", "yellow", "orange", "j"),
    ("餃子", "GYOZA", "dumpling", "round", "amber", "red", "j"), ("とんかつ", "TONKATSU", "pig", "pill", "orange", "yellow", "j"),
    ("うなぎ", "UNAGI", "fish", "double", "yellow", "lime", "j"), ("焼き鳥横丁", "YAKITORI ALLEY", "lantern", "dashed", "red", "amber", "j"),
    ("たい焼き", "TAIYAKI", "fish", "round", "yellow", "pink", "j"), ("おでん", "ODEN", "skewer", "ticket", "amber", "white", "j"),
    ("ビール", "BEER", "mug", "pill", "yellow", "amber", "j"), ("ワイン", "WINE BAR", "glass", "arch", "magenta", "red", "j"),
    ("日本酒", "SAKE", "bottle", "double", "white", "cyan", "j"), ("ウイスキー", "WHISKY", "glass", "brackets", "amber", "orange", "j"),
    ("スナック", "SNACK", "heart", "round", "pink", "magenta", "j"), ("クラブ", "CLUB NEO", "note", "dashed", "violet", "pink", "j"),
    ("ライブ", "LIVE HOUSE", "mic", "double", "cyan", "magenta", "j"), ("映画館", "CINEMA", "film", "arch", "red", "white", "j"),
    ("劇場", "THEATER", "mask", "round", "violet", "amber", "j"), ("書店", "BOOKSTORE", "book", "pill", "cyan", "yellow", "j"),
    ("レコード", "VINYL", "disc", "round", "magenta", "cyan", "j"), ("楽器", "MUSIC SHOP", "note", "brackets", "mint", "violet", "j"),
    ("ゲーム", "ARCADE", "pad", "dashed", "lime", "pink", "j"), ("プリクラ", "PHOTO BOOTH", "star", "pill", "pink", "yellow", "j"),
    ("漫画喫茶", "MANGA CAFE", "book", "ticket", "orange", "cyan", "j"), ("ネットカフェ", "NET CAFE", "cloud", "double", "blue", "mint", "j"),
    ("銭湯", "SENTO", "steam", "arch", "cyan", "white", "j"), ("温泉", "ONSEN", "steam", "round", "orange", "pink", "j"),
    ("コインランドリー", "LAUNDRY", "drop", "pill", "cyan", "blue", "j"), ("理容室", "BARBER", "scissors", "brackets", "red", "blue", "j"),
    ("美容室", "SALON", "scissors", "round", "pink", "white", "j"), ("ネイル", "NAILS", "heart", "dashed", "magenta", "pink", "j"),
    ("占い", "FORTUNE", "eye", "double", "violet", "yellow", "j"), ("お守り", "OMAMORI", "torii", "ticket", "red", "yellow", "j"),
    ("神社", "SHRINE", "torii", "arch", "red", "amber", "j"), ("寺", "TEMPLE", "lantern", "round", "amber", "red", "j"),
    ("花屋", "FLOWERS", "sakura", "pill", "pink", "lime", "j"), ("桜", "SAKURA", "sakura", "round", "pink", "white", "j"),
    ("ペットショップ", "PET SHOP", "cat", "double", "orange", "cyan", "j"), ("ねこカフェ", "CAT CAFE", "cat", "pill", "amber", "pink", "j"),
    ("水族館", "AQUARIUM", "fish", "arch", "cyan", "mint", "j"), ("動物園", "ZOO", "pig", "round", "lime", "yellow", "j"),
    ("遊園地", "FUNFAIR", "star", "dashed", "yellow", "magenta", "j"), ("観覧車", "FERRIS", "wheel", "round", "cyan", "pink", "j"),
    ("花火", "HANABI", "burst", "round", "magenta", "yellow", "j"), ("祭り", "MATSURI", "lantern", "dashed", "red", "yellow", "j"),
    ("駅前", "STATION", "train", "brackets", "mint", "cyan", "j"), ("地下鉄", "SUBWAY", "train", "pill", "blue", "white", "j"),
    ("タクシー", "TAXI", "car", "ticket", "yellow", "orange", "j"), ("駐車場", "PARKING", "car", "double", "cyan", "white", "j"),
    ("ガソリン", "GAS", "drop", "round", "red", "amber", "j"), ("コンビニ", "24H STORE", "none", "double", "lime", "cyan", "j"),
    ("100円", "100 YEN", "coin", "round", "yellow", "orange", "j"), ("質屋", "PAWN", "coin", "brackets", "amber", "white", "j"),
    ("銀行", "BANK", "coin", "arch", "mint", "yellow", "j"), ("郵便局", "POST", "envelope", "pill", "red", "white", "j"),
    ("電気街", "ELECTRIC TOWN", "bolt", "dashed", "yellow", "cyan", "j"), ("ロボット", "ROBOT", "robot", "double", "cyan", "magenta", "j"),
    ("ネオン", "NEON", "bolt", "round", "pink", "cyan", "j"), ("未来", "FUTURE", "star", "brackets", "violet", "cyan", "j"),
    ("宇宙", "COSMOS", "moon", "arch", "violet", "blue", "j"), ("月", "LUNA", "moon", "round", "white", "violet", "j"),
    ("夜", "NIGHT", "moon", "pill", "blue", "magenta", "j"), ("雨", "RAIN", "drop", "dashed", "cyan", "blue", "j"),
    ("雷", "THUNDER", "bolt", "double", "yellow", "white", "j"), ("龍", "DRAGON", "flame", "round", "red", "orange", "j"),
    ("虎", "TIGER", "flame", "brackets", "orange", "yellow", "j"), ("福", "LUCK", "coin", "ticket", "red", "yellow", "j"),
    ("愛", "LOVE", "heart", "round", "pink", "red", "j"), ("夢", "DREAM", "cloud", "pill", "violet", "pink", "j"),
    ("KISS", "LOUNGE", "heart", "double", "pink", "white", "i"), ("VIP", "ROOM", "crown", "dashed", "amber", "yellow", "i"),
    ("PIZZA", "SLICE", "slice", "round", "orange", "yellow", "i"), ("TOKYO", "2099", "none", "double", "magenta", "cyan", "i"),
]
assert len(S) == 100, len(S)


def mask(w=W, h=H):
    return Image.new("L", (w, h), 0)


def icon(kind, dt, df, cx, cy, r):
    """Draw icon strokes: text-mask (dt) = filled shapes, frame-mask (df) = outlines in the second colour. r = half size."""
    lw = max(5, r // 9)
    k = kind
    if k == "none":
        return
    if k in ("bowl", "cup", "mug"):
        dt.pieslice((cx - r, cy - r * 0.9, cx + r, cy + r * 1.1), 0, 180, fill=255) if k == "bowl" else dt.rectangle((cx - r * 0.7, cy - r * 0.6, cx + r * 0.6, cy + r * 0.8), fill=255)
        if k != "bowl":
            df.arc((cx + r * 0.3, cy - r * 0.3, cx + r * 1.1, cy + r * 0.5), -90, 90, fill=255, width=lw)
        for dx in (-r * 0.35, 0, r * 0.35):
            df.arc((cx + dx - 8, cy - r * 1.35, cx + dx + 8, cy - r * 0.75), 90, 270, fill=255, width=lw // 2 + 1)
    elif k == "glass":
        dt.polygon([(cx - r * 0.85, cy - r * 0.8), (cx + r * 0.85, cy - r * 0.8), (cx, cy + r * 0.15)], fill=255)
        df.line((cx, cy + r * 0.15, cx, cy + r * 0.9), fill=255, width=lw)
        df.line((cx - r * 0.45, cy + r * 0.9, cx + r * 0.45, cy + r * 0.9), fill=255, width=lw)
    elif k == "bottle":
        dt.rounded_rectangle((cx - r * 0.38, cy - r * 0.2, cx + r * 0.38, cy + r), r // 6, fill=255)
        dt.rectangle((cx - r * 0.16, cy - r * 0.9, cx + r * 0.16, cy - r * 0.15), fill=255)
        df.line((cx - r * 0.2, cy - r, cx + r * 0.2, cy - r), fill=255, width=lw)
    elif k == "mic":
        dt.rounded_rectangle((cx - r * 0.3, cy - r, cx + r * 0.3, cy + r * 0.25), r // 3, fill=255)
        df.arc((cx - r * 0.65, cy - r * 0.45, cx + r * 0.65, cy + r * 0.7), 0, 180, fill=255, width=lw)
        df.line((cx, cy + r * 0.7, cx, cy + r), fill=255, width=lw)
    elif k == "cross":
        dt.rectangle((cx - r * 0.25, cy - r, cx + r * 0.25, cy + r), fill=255)
        dt.rectangle((cx - r, cy - r * 0.25, cx + r, cy + r * 0.25), fill=255)
    elif k == "flame":
        pts = [(cx, cy - r), (cx + r * 0.55, cy - r * 0.1), (cx + r * 0.7, cy + r * 0.45), (cx, cy + r), (cx - r * 0.7, cy + r * 0.45), (cx - r * 0.45, cy - r * 0.15), (cx - r * 0.15, cy + r * 0.05)]
        dt.polygon(pts, fill=255)
        df.polygon([(cx, cy + r * 0.1), (cx + r * 0.25, cy + r * 0.55), (cx, cy + r * 0.85), (cx - r * 0.25, cy + r * 0.55)], fill=255)
    elif k == "fish":
        dt.ellipse((cx - r, cy - r * 0.5, cx + r * 0.45, cy + r * 0.5), fill=255)
        dt.polygon([(cx + r * 0.3, cy), (cx + r, cy - r * 0.5), (cx + r, cy + r * 0.5)], fill=255)
        df.ellipse((cx - r * 0.7, cy - r * 0.18, cx - r * 0.5, cy + r * 0.02), fill=255)
    elif k == "dice":
        df.rounded_rectangle((cx - r * 0.85, cy - r * 0.85, cx + r * 0.85, cy + r * 0.85), r // 5, outline=255, width=lw)
        for dx, dy in ((-0.4, -0.4), (0.4, 0.4), (0, 0), (-0.4, 0.4), (0.4, -0.4)):
            dt.ellipse((cx + dx * r - r * 0.12, cy + dy * r - r * 0.12, cx + dx * r + r * 0.12, cy + dy * r + r * 0.12), fill=255)
    elif k == "torii":
        dt.rectangle((cx - r, cy - r * 0.8, cx + r, cy - r * 0.5), fill=255)
        df.rectangle((cx - r * 0.7, cy - r * 0.3, cx + r * 0.7, cy - r * 0.1), fill=255)
        dt.rectangle((cx - r * 0.55, cy - r * 0.5, cx - r * 0.35, cy + r), fill=255)
        dt.rectangle((cx + r * 0.35, cy - r * 0.5, cx + r * 0.55, cy + r), fill=255)
    elif k == "shrimp":
        df.arc((cx - r, cy - r, cx + r, cy + r), 200, 90, fill=255, width=lw * 2)
        dt.polygon([(cx + r * 0.1, cy + r), (cx + r * 0.7, cy + r * 0.8), (cx + r * 0.5, cy + r * 0.45)], fill=255)
    elif k == "bed":
        dt.rectangle((cx - r, cy, cx + r, cy + r * 0.4), fill=255)
        dt.rectangle((cx - r, cy - r * 0.6, cx - r * 0.8, cy + r * 0.8), fill=255)
        df.rounded_rectangle((cx - r * 0.7, cy - r * 0.35, cx - r * 0.1, cy), r // 6, outline=255, width=lw)
    elif k == "tooth":
        dt.polygon([(cx - r * 0.7, cy - r * 0.7), (cx + r * 0.7, cy - r * 0.7), (cx + r * 0.6, cy + r * 0.2), (cx + r * 0.3, cy + r), (cx, cy + r * 0.2), (cx - r * 0.3, cy + r), (cx - r * 0.6, cy + r * 0.2)], fill=255)
    elif k == "book":
        dt.polygon([(cx, cy - r * 0.6), (cx - r, cy - r * 0.8), (cx - r, cy + r * 0.7), (cx, cy + r * 0.9)], fill=255)
        df.polygon([(cx, cy - r * 0.6), (cx + r, cy - r * 0.8), (cx + r, cy + r * 0.7), (cx, cy + r * 0.9)], outline=255, width=lw)
    elif k == "moon":
        dt.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
        dt.ellipse((cx - r * 0.35, cy - r * 1.1, cx + r * 1.3, cy + r * 0.7), fill=0)
        df.regular_polygon((cx + r * 0.65, cy - r * 0.55, r * 0.22), 4, fill=255)
    elif k == "star":
        pts = []
        for i in range(10):
            a = -math.pi / 2 + i * math.pi / 5
            rr = r if i % 2 == 0 else r * 0.42
            pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
        dt.polygon(pts, fill=255)
    elif k == "pad":
        dt.rounded_rectangle((cx - r, cy - r * 0.5, cx + r, cy + r * 0.55), r // 2, fill=255)
        df.rectangle((cx - r * 0.65, cy - r * 0.08, cx - r * 0.25, cy + r * 0.08), fill=255)
        df.rectangle((cx - r * 0.5, cy - r * 0.23, cx - r * 0.4, cy + r * 0.23), fill=255)
        df.ellipse((cx + r * 0.25, cy - r * 0.2, cx + r * 0.4, cy - r * 0.05), fill=255)
        df.ellipse((cx + r * 0.5, cy, cx + r * 0.65, cy + r * 0.15), fill=255)
    elif k == "wave":
        for i in range(3):
            y = cy - r * 0.55 + i * r * 0.55
            df.arc((cx - r, y - r * 0.3, cx - r * 0.1, y + r * 0.3), 180, 360, fill=255, width=lw)
            dt.arc((cx - r * 0.1, y - r * 0.3, cx + r * 0.8, y + r * 0.3), 180, 360, fill=255, width=lw)
    elif k == "skewer":
        df.line((cx - r, cy + r * 0.8, cx + r, cy - r * 0.8), fill=255, width=lw)
        for t in (-0.45, 0.0, 0.45):
            dt.ellipse((cx + t * r - r * 0.28, cy - t * r * 0.8 - r * 0.28, cx + t * r + r * 0.28, cy - t * r * 0.8 + r * 0.28), fill=255)
    elif k == "tag":
        dt.polygon([(cx - r, cy), (cx - r * 0.3, cy - r * 0.8), (cx + r, cy - r * 0.8), (cx + r, cy + r * 0.8), (cx - r * 0.3, cy + r * 0.8)], fill=255)
        df.ellipse((cx - r * 0.55, cy - r * 0.12, cx - r * 0.3, cy + r * 0.12), fill=255)
    elif k == "octopus":
        dt.ellipse((cx - r * 0.8, cy - r, cx + r * 0.8, cy + r * 0.3), fill=255)
        for i in range(5):
            x = cx - r * 0.7 + i * r * 0.35
            df.line((x, cy + r * 0.2, x + (6 if i % 2 else -6), cy + r), fill=255, width=lw)
    elif k == "dumpling":
        dt.pieslice((cx - r, cy - r * 0.6, cx + r, cy + r * 1.0), 180, 360, fill=255)
        dt.rectangle((cx - r, cy + r * 0.2, cx + r, cy + r * 0.4), fill=255)
        for i in range(5):
            df.line((cx - r * 0.6 + i * r * 0.3, cy - r * 0.55, cx - r * 0.6 + i * r * 0.3, cy - r * 0.2), fill=255, width=lw // 2 + 1)
    elif k == "pig":
        dt.ellipse((cx - r, cy - r * 0.7, cx + r, cy + r * 0.8), fill=255)
        dt.polygon([(cx - r * 0.7, cy - r * 0.5), (cx - r * 0.45, cy - r * 1.0), (cx - r * 0.15, cy - r * 0.6)], fill=255)
        dt.polygon([(cx + r * 0.7, cy - r * 0.5), (cx + r * 0.45, cy - r * 1.0), (cx + r * 0.15, cy - r * 0.6)], fill=255)
        df.ellipse((cx - r * 0.35, cy - r * 0.1, cx + r * 0.35, cy + r * 0.4), fill=255)
    elif k == "lantern":
        dt.rounded_rectangle((cx - r * 0.6, cy - r * 0.7, cx + r * 0.6, cy + r * 0.7), r // 2, fill=255)
        df.rectangle((cx - r * 0.35, cy - r * 0.95, cx + r * 0.35, cy - r * 0.7), fill=255)
        df.rectangle((cx - r * 0.35, cy + r * 0.7, cx + r * 0.35, cy + r * 0.95), fill=255)
        df.line((cx - r * 0.6, cy, cx + r * 0.6, cy), fill=0, width=lw // 2)
    elif k == "heart":
        dt.ellipse((cx - r, cy - r * 0.8, cx, cy + r * 0.2), fill=255)
        dt.ellipse((cx, cy - r * 0.8, cx + r, cy + r * 0.2), fill=255)
        dt.polygon([(cx - r * 0.95, cy - r * 0.15), (cx + r * 0.95, cy - r * 0.15), (cx, cy + r)], fill=255)
    elif k == "note":
        dt.ellipse((cx - r * 0.7, cy + r * 0.2, cx - r * 0.1, cy + r * 0.8), fill=255)
        dt.ellipse((cx + r * 0.2, cy, cx + r * 0.8, cy + r * 0.6), fill=255)
        df.line((cx - r * 0.1, cy + r * 0.5, cx - r * 0.1, cy - r * 0.8), fill=255, width=lw)
        df.line((cx + r * 0.8, cy + r * 0.3, cx + r * 0.8, cy - r * 1.0), fill=255, width=lw)
        df.line((cx - r * 0.1, cy - r * 0.8, cx + r * 0.8, cy - r * 1.0), fill=255, width=lw * 2)
    elif k == "film":
        df.rectangle((cx - r, cy - r * 0.7, cx + r, cy + r * 0.7), outline=255, width=lw)
        for i in range(4):
            dt.rectangle((cx - r * 0.85 + i * r * 0.45, cy - r * 0.55, cx - r * 0.65 + i * r * 0.45, cy - r * 0.35), fill=255)
            dt.rectangle((cx - r * 0.85 + i * r * 0.45, cy + r * 0.35, cx - r * 0.65 + i * r * 0.45, cy + r * 0.55), fill=255)
    elif k == "mask":
        dt.pieslice((cx - r, cy - r, cx + r, cy + r), 0, 180, fill=255)
        dt.rectangle((cx - r, cy - r * 0.05, cx + r, cy + r * 0.05), fill=255)
        df.ellipse((cx - r * 0.55, cy - r * 0.2, cx - r * 0.15, cy + r * 0.1), fill=0)
        df.ellipse((cx + r * 0.15, cy - r * 0.2, cx + r * 0.55, cy + r * 0.1), fill=0)
        df.arc((cx - r * 0.5, cy + r * 0.15, cx + r * 0.5, cy + r * 0.75), 200, 340, fill=255, width=lw)
    elif k == "disc":
        df.ellipse((cx - r, cy - r, cx + r, cy + r), outline=255, width=lw)
        dt.ellipse((cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3), fill=255)
        df.arc((cx - r * 0.65, cy - r * 0.65, cx + r * 0.65, cy + r * 0.65), 200, 300, fill=255, width=lw // 2 + 1)
    elif k == "cloud":
        for ox, oy, rr in ((-0.5, 0.1, 0.5), (0.0, -0.25, 0.65), (0.55, 0.1, 0.5)):
            dt.ellipse((cx + ox * r - rr * r, cy + oy * r - rr * r, cx + ox * r + rr * r, cy + oy * r + rr * r), fill=255)
        dt.rectangle((cx - r * 0.5, cy + r * 0.1, cx + r * 0.55, cy + r * 0.6), fill=255)
    elif k == "steam":
        for dx in (-0.6, 0.0, 0.6):
            for s in range(2):
                df.arc((cx + dx * r - r * 0.2, cy - r + s * r * 0.8, cx + dx * r + r * 0.2, cy - r * 0.4 + s * r * 0.8), 90 + 180 * s, 270 + 180 * s, fill=255, width=lw)
        dt.rounded_rectangle((cx - r, cy + r * 0.5, cx + r, cy + r * 0.9), r // 5, fill=255)
    elif k == "drop":
        dt.polygon([(cx, cy - r), (cx + r * 0.65, cy + r * 0.25), (cx - r * 0.65, cy + r * 0.25)], fill=255)
        dt.ellipse((cx - r * 0.65, cy - r * 0.2, cx + r * 0.65, cy + r), fill=255)
    elif k == "scissors":
        df.line((cx - r, cy - r * 0.7, cx + r * 0.8, cy + r * 0.5), fill=255, width=lw)
        df.line((cx - r, cy + r * 0.7, cx + r * 0.8, cy - r * 0.5), fill=255, width=lw)
        dt.ellipse((cx + r * 0.5, cy - r * 0.9, cx + r * 1.0, cy - r * 0.4), outline=255, width=lw)
        dt.ellipse((cx + r * 0.5, cy + r * 0.4, cx + r * 1.0, cy + r * 0.9), outline=255, width=lw)
    elif k == "eye":
        dt.polygon([(cx - r, cy), (cx, cy - r * 0.7), (cx + r, cy), (cx, cy + r * 0.7)], outline=255, width=lw)
        dt.ellipse((cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3), fill=255)
    elif k == "sakura":
        for i in range(5):
            a = -math.pi / 2 + i * 2 * math.pi / 5
            px, py = cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55
            dt.ellipse((px - r * 0.42, py - r * 0.42, px + r * 0.42, py + r * 0.42), fill=255)
        df.ellipse((cx - r * 0.15, cy - r * 0.15, cx + r * 0.15, cy + r * 0.15), fill=255)
    elif k == "cat":
        dt.ellipse((cx - r * 0.85, cy - r * 0.6, cx + r * 0.85, cy + r * 0.9), fill=255)
        dt.polygon([(cx - r * 0.85, cy - r * 0.1), (cx - r * 0.75, cy - r * 1.0), (cx - r * 0.2, cy - r * 0.55)], fill=255)
        dt.polygon([(cx + r * 0.85, cy - r * 0.1), (cx + r * 0.75, cy - r * 1.0), (cx + r * 0.2, cy - r * 0.55)], fill=255)
        for sx in (-1, 1):
            df.ellipse((cx + sx * r * 0.4 - 7, cy - r * 0.05, cx + sx * r * 0.4 + 7, cy + r * 0.2), fill=255)
    elif k == "wheel":
        df.ellipse((cx - r, cy - r, cx + r, cy + r), outline=255, width=lw)
        for i in range(8):
            a = i * math.pi / 4
            dt.line((cx, cy, cx + math.cos(a) * r, cy + math.sin(a) * r), fill=255, width=lw // 2 + 1)
    elif k == "burst":
        for i in range(12):
            a = i * math.pi / 6
            dt.line((cx + math.cos(a) * r * 0.25, cy + math.sin(a) * r * 0.25, cx + math.cos(a) * r, cy + math.sin(a) * r), fill=255, width=lw)
        df.ellipse((cx - r * 0.18, cy - r * 0.18, cx + r * 0.18, cy + r * 0.18), fill=255)
    elif k == "train":
        dt.rounded_rectangle((cx - r, cy - r * 0.8, cx + r, cy + r * 0.5), r // 3, fill=255)
        df.rectangle((cx - r * 0.7, cy - r * 0.55, cx + r * 0.7, cy - r * 0.1), fill=255)
        df.ellipse((cx - r * 0.6, cy + r * 0.55, cx - r * 0.3, cy + r * 0.85), fill=255)
        df.ellipse((cx + r * 0.3, cy + r * 0.55, cx + r * 0.6, cy + r * 0.85), fill=255)
    elif k == "car":
        dt.rounded_rectangle((cx - r, cy - r * 0.1, cx + r, cy + r * 0.55), r // 4, fill=255)
        dt.polygon([(cx - r * 0.55, cy - r * 0.1), (cx - r * 0.3, cy - r * 0.65), (cx + r * 0.4, cy - r * 0.65), (cx + r * 0.7, cy - r * 0.1)], fill=255)
        df.ellipse((cx - r * 0.65, cy + r * 0.35, cx - r * 0.25, cy + r * 0.75), fill=255)
        df.ellipse((cx + r * 0.25, cy + r * 0.35, cx + r * 0.65, cy + r * 0.75), fill=255)
    elif k == "coin":
        df.ellipse((cx - r, cy - r, cx + r, cy + r), outline=255, width=lw)
        dt.rectangle((cx - r * 0.12, cy - r * 0.55, cx + r * 0.12, cy + r * 0.55), fill=255)
        dt.rectangle((cx - r * 0.45, cy - r * 0.2, cx + r * 0.45, cy - r * 0.05), fill=255)
    elif k == "envelope":
        dt.rectangle((cx - r, cy - r * 0.65, cx + r, cy + r * 0.65), outline=255, width=lw)
        df.line((cx - r, cy - r * 0.65, cx, cy + r * 0.1, cx + r, cy - r * 0.65), fill=255, width=lw)
    elif k == "bolt":
        dt.polygon([(cx + r * 0.2, cy - r), (cx - r * 0.65, cy + r * 0.1), (cx - r * 0.05, cy + r * 0.1), (cx - r * 0.2, cy + r), (cx + r * 0.65, cy - r * 0.2), (cx + r * 0.05, cy - r * 0.2)], fill=255)
    elif k == "robot":
        dt.rounded_rectangle((cx - r * 0.8, cy - r * 0.6, cx + r * 0.8, cy + r * 0.8), r // 4, outline=255, width=lw)
        df.ellipse((cx - r * 0.5, cy - r * 0.25, cx - r * 0.15, cy + r * 0.1), fill=255)
        df.ellipse((cx + r * 0.15, cy - r * 0.25, cx + r * 0.5, cy + r * 0.1), fill=255)
        df.line((cx, cy - r * 0.6, cx, cy - r), fill=255, width=lw)
        dt.line((cx - r * 0.4, cy + r * 0.45, cx + r * 0.4, cy + r * 0.45), fill=255, width=lw)
    elif k == "crown":
        dt.polygon([(cx - r, cy + r * 0.6), (cx - r, cy - r * 0.6), (cx - r * 0.45, cy), (cx, cy - r * 0.8), (cx + r * 0.45, cy), (cx + r, cy - r * 0.6), (cx + r, cy + r * 0.6)], fill=255)
    elif k == "slice":
        dt.polygon([(cx - r, cy - r * 0.7), (cx + r, cy - r * 0.7), (cx, cy + r)], fill=255)
        for ox, oy in ((-0.35, -0.3), (0.3, -0.35), (0.0, 0.2)):
            df.ellipse((cx + ox * r - 9, cy + oy * r - 9, cx + ox * r + 9, cy + oy * r + 9), fill=255)


def frame(df, style, w=W, h=H):
    lw = 7
    if style == "round":
        df.rounded_rectangle((10, 10, w - 11, h - 11), 36, outline=255, width=lw)
    elif style == "double":
        df.rectangle((10, 10, w - 11, h - 11), outline=255, width=lw)
        df.rectangle((24, 24, w - 25, h - 25), outline=255, width=3)
    elif style == "pill":
        df.rounded_rectangle((10, 10, w - 11, h - 11), (h - 20) // 2, outline=255, width=lw)
    elif style == "brackets":
        L = 62
        for (x, y, sx, sy) in ((10, 10, 1, 1), (w - 11, 10, -1, 1), (10, h - 11, 1, -1), (w - 11, h - 11, -1, -1)):
            df.line((x, y + sy * L, x, y, x + sx * L, y), fill=255, width=lw)
    elif style == "arch":
        df.arc((10, 10, w - 11, h * 1.6), 180, 360, fill=255, width=lw)
        df.line((10, h * 0.8, 10, h - 11, w - 11, h - 11, w - 11, h * 0.8), fill=255, width=lw)
        df.line((10, 120, 10, h - 11), fill=255, width=lw)
    elif style == "ticket":
        df.rectangle((10, 10, w - 11, h - 11), outline=255, width=lw)
        for x, y in ((10, h // 2), (w - 11, h // 2)):
            df.ellipse((x - 20, y - 20, x + 20, y + 20), fill=0, outline=255, width=lw)
    elif style == "dashed":
        for x in range(14, w - 40, 54):
            df.line((x, 12, x + 34, 12), fill=255, width=lw)
            df.line((x, h - 13, x + 34, h - 13), fill=255, width=lw)
        for y in range(14, h - 40, 54):
            df.line((12, y, 12, y + 34), fill=255, width=lw)
            df.line((w - 13, y, w - 13, y + 34), fill=255, width=lw)


FONTS = {"j": "YuGothB.ttc", "i": "impact.ttf", "a": "arialbd.ttf"}


def fit_text(dt, txt, fname, box_w, box_h, start):
    for size in range(start, 14, -2):
        f = font(FONTS.get(fname), size) if fname == "j" else ImageFont.truetype(os.path.join(r"C:\Windows\Fonts", FONTS[fname]), size)
        bb = dt.textbbox((0, 0), txt, font=f)
        if bb[2] - bb[0] <= box_w and bb[3] - bb[1] <= box_h:
            return f, bb
    return f, bb


def draw_sign(n, spec):
    text, sub, ic, fr, ka, kb, fn = spec
    mt, mf = mask(), mask()
    dt, df = ImageDraw.Draw(mt), ImageDraw.Draw(mf)
    frame(df, fr)
    has_icon = ic != "none"
    layout = n % 3          # 0 icon left, 1 icon right, 2 icon above-left small; varies composition across the set
    pad = 56
    if has_icon:
        r = 58
        cx = pad + r if layout != 1 else W - pad - r
        icon(ic, dt, df, cx, H // 2 - (8 if sub else 0), r)
        tx0 = pad + 2 * r + 24 if layout != 1 else pad
        tx1 = W - pad if layout != 1 else W - pad - 2 * r - 24
    else:
        tx0, tx1 = pad, W - pad
    tw = tx1 - tx0
    if sub:
        f1, b1 = fit_text(dt, text, fn, tw, 104, 110)
        f2, b2 = fit_text(dt, sub, "a", tw, 34, 34)
        th1 = b1[3] - b1[1]; th2 = b2[3] - b2[1]
        y1 = (H - (th1 + 14 + th2)) // 2
        dt.text((tx0 + (tw - (b1[2] - b1[0])) // 2 - b1[0], y1 - b1[1]), text, font=f1, fill=255)
        df.text((tx0 + (tw - (b2[2] - b2[0])) // 2 - b2[0], y1 + th1 + 14 - b2[1]), sub, font=f2, fill=255)
    else:
        f1, b1 = fit_text(dt, text, fn, tw, 130, 150)
        dt.text((tx0 + (tw - (b1[2] - b1[0])) // 2 - b1[0], (H - (b1[3] - b1[1])) // 2 - b1[1]), text, font=f1, fill=255)
    return mt, mf, PAL[ka], PAL[kb]


def glow(mt, mf, ca, cb, seed):
    f = lambda m, s: np.asarray(m.filter(ImageFilter.GaussianBlur(s)), np.float32) / 255.0
    a = lambda m: np.asarray(m, np.float32) / 255.0
    t, fr = a(mt), a(mf)
    ht, hf, ct, cf = f(mt, 8), f(mf, 7), f(mt, 2.5), f(mf, 2)
    ca = np.array(ca, np.float32); cb = np.array(cb, np.float32); wh = np.array([255, 255, 255], np.float32)
    tc = ca + (wh - ca) * (0.22 * ct[..., None] ** 2)
    fc = cb + (wh - cb) * (0.22 * cf[..., None] ** 2)
    emi = np.clip(t[..., None] * tc + fr[..., None] * fc + 0.30 * ht[..., None] * ca + 0.30 * hf[..., None] * cb, 0, 255)
    panel = np.array([22, 23, 29], np.float32) + 6 * np.random.default_rng(seed).random((H, W, 1)).astype(np.float32)
    alb = panel * np.ones((H, W, 3), np.float32)
    tube = np.clip(t + fr, 0, 1)[..., None]
    alb = alb * (1 - tube) + np.array([120, 120, 125], np.float32) * tube
    rim = np.zeros((H, W), np.float32); rim[:5, :] = 1; rim[-5:, :] = 1; rim[:, :5] = 1; rim[:, -5:] = 1
    alb = alb * (1 - rim[..., None]) + np.array([58, 60, 68], np.float32) * rim[..., None]
    return alb, emi


def main():
    A = np.zeros((10 * H, 10 * W, 3), np.float32)
    E = np.zeros_like(A)
    for n, spec in enumerate(S):
        mt, mf, ca, cb = draw_sign(n, spec)
        a, e = glow(mt, mf, ca, cb, 500 + n)
        c, r = n % 10, n // 10
        A[r * H:(r + 1) * H, c * W:(c + 1) * W] = a
        E[r * H:(r + 1) * H, c * W:(c + 1) * W] = e
    d = out_dir()
    A8, E8 = A.clip(0, 255).astype(np.uint8), E.clip(0, 255).astype(np.uint8)
    save_png(os.path.join(d, "Sign100_Atlas_Albedo.png"), A8)
    save_png(os.path.join(d, "Sign100_Atlas_Emission.png"), E8)
    # preview: emission on night panel with simple bloom
    bloom = np.asarray(Image.fromarray(E8).filter(ImageFilter.GaussianBlur(14)), np.float32)
    prev = np.clip(E.astype(np.float32) * 1.0 + 0.6 * bloom + 0.12 * A, 0, 255).astype(np.uint8)
    Image.fromarray(prev).resize((2560, 1280), Image.LANCZOS).save(os.path.join(d, "Sign100_Contact.png"))
    print("signs:", len(S), "unique texts:", len({s[0] + s[1] for s in S}), "->", d)


if __name__ == "__main__":
    main()

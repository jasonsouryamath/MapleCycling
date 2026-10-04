"""NB-QOL Nagisa Bay wayfinding atlas (COORDINATION.md, NB-QOL log line).

Reads Assets/Environment/NagisaBay/NagisaRoute.json (single source of truth for distances) and
writes one 4096x2048 sign atlas (8x8 cells of 512x256) + a manifest listing the cell order:

    Assets/Environment/NagisaBay/Textures/Wayfinding/NB_Wayfinding_Atlas.png
    Assets/Environment/NagisaBay/Textures/Wayfinding/NB_Wayfinding_Atlas.json

Consumed by Assets/Editor/NagisaBayEnvironment.Wayfinding.cs. All text is fictional/original.
Every number printed comes from the route (no hand-typed distances). Run:
    python tools/textures/make_nagisa_wayfinding.py
"""
import json, os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ROUTE = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "NagisaRoute.json")
OUT = os.path.join(ROOT, "Assets", "Environment", "NagisaBay", "Textures", "Wayfinding")
CW, CH, COLS, ROWS = 512, 256, 8, 8

# palette (PROVISIONAL art direction: resort teal boards, coral accent, white type)
TEAL = (18, 116, 128); TEAL_D = (10, 78, 88); CORAL = (255, 122, 92); WHITE = (250, 250, 244)
AMBER = (255, 190, 40); INK = (22, 24, 28); POLKA = (214, 40, 46)
F = "C:/Windows/Fonts/arialbd.ttf"


def fit(d, text, max_w, px):
    while px > 10:
        f = ImageFont.truetype(F, px)
        if d.textlength(text, font=f) <= max_w:
            return f
        px -= 2
    return ImageFont.truetype(F, px)


def board(bg=TEAL, stripe=CORAL):
    im = Image.new("RGB", (CW, CH), bg)
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, CW - 1, CH - 1], outline=WHITE, width=10)
    d.rectangle([10, CH - 38, CW - 11, CH - 11], fill=stripe)
    return im, d


def centre(d, y, text, px, fill=WHITE, max_w=CW - 60):
    f = fit(d, text, max_w, px)
    d.text(((CW - d.textlength(text, font=f)) / 2, y), text, font=f, fill=fill)


def km(m):
    return f"{m / 1000:.1f} km"


def main():
    r = json.load(open(ROUTE, encoding="utf-8"))
    z, cps, samples = r["zones"], r["checkpoints"], r["samples"]
    length = samples[-1]["d"]
    ds = [s["d"] for s in samples]
    ys = [s["p"][1] for s in samples]

    def y_at(m):
        for i in range(1, len(ds)):
            if ds[i] >= m:
                t = (m - ds[i - 1]) / max(1e-3, ds[i] - ds[i - 1])
                return ys[i - 1] + (ys[i] - ys[i - 1]) * t
        return ys[-1]

    c0, c1 = z["climbStart"], z["komM"]
    climb_len = c1 - c0
    avg = 100 * (y_at(c1) - y_at(c0)) / climb_len
    mx, m = 0.0, c0
    while m + 200 <= c1:  # steepest 200 m
        mx = max(mx, 100 * (y_at(m + 200) - y_at(m)) / 200)
        m += 25

    cells = []
    for k in range(1, int(length // 1000) + 1):
        im, d = board(bg=WHITE, stripe=TEAL)
        d.rectangle([0, 0, CW - 1, CH - 1], outline=TEAL_D, width=10)
        centre(d, 22, f"{k} km", 118, fill=TEAL_D)
        centre(d, 150, f"{km(length - k * 1000)} to finish", 46, fill=INK)
        cells.append((f"km_{k}", im))
    for i, c in enumerate(cps):
        im, d = board()
        centre(d, 18, "NAGISA BAY LOOP", 28, fill=(190, 236, 232))
        name = c["name"]
        if fit(d, name, CW - 60, 70).size < 54 and " " in name:   # wrap long names onto 2 lines
            w = name.split(" ")
            h = (len(w) + 1) // 2
            centre(d, 50, " ".join(w[:h]), 48)
            centre(d, 102, " ".join(w[h:]), 48)
        else:
            centre(d, 60, name, 70)
        if i + 1 < len(cps):
            line = f"Next checkpoint  {km(cps[i + 1]['distance'] - c['distance'])}"
        else:
            line = f"Finish  {km(length - c['distance'])}"
        centre(d, 166, line, 34)
        cells.append((f"cp_{i}", im))
    im, d = board(bg=TEAL_D, stripe=AMBER)
    centre(d, 18, "PALM RIDGE CLIMB", 58)
    centre(d, 92, f"{km(climb_len)}   avg {avg:.1f}%", 52, fill=AMBER)
    centre(d, 158, f"max {mx:.1f}%  -  KOM at {km(c1)}", 36)
    cells.append(("climb_info", im))
    for n, lab in ((1000, "1 km"), (500, "500 m"), (200, "200 m")):
        im = Image.new("RGB", (CW, CH), WHITE)
        d = ImageDraw.Draw(im)
        for yy in range(30, CH, 58):
            for xx in range(24 + (yy // 58 % 2) * 29, CW, 58):
                d.ellipse([xx - 9, yy - 9, xx + 9, yy + 9], fill=(245, 196, 196))
        d.rectangle([0, 0, CW - 1, CH - 1], outline=POLKA, width=12)
        centre(d, 26, "KOM", 90, fill=POLKA)
        centre(d, 138, lab, 84, fill=INK)
        cells.append((f"kom_{n}", im))
    for n, lab in ((1000, "1 km"), (500, "500 m"), (200, "200 m")):
        im, d = board(bg=INK, stripe=WHITE)
        for x in range(10, CW - 38, 28):
            for row in range(2):
                if (x // 28 + row) % 2 == 0:
                    d.rectangle([x, 12 + row * 14, x + 27, 25 + row * 14], fill=WHITE)
        centre(d, 50, "FINISH", 80)
        centre(d, 134, lab, 74, fill=AMBER)
        cells.append((f"fin_{n}", im))
    for side in ("L", "R"):
        im = Image.new("RGB", (CW, CH), AMBER)
        d = ImageDraw.Draw(im)
        for k in range(3):
            x0 = 90 + k * 115
            pts = [(x0, 40), (x0 + 70, 128), (x0, 216), (x0 + 42, 216), (x0 + 112, 128), (x0 + 42, 40)]
            if side == "L":
                pts = [(CW - x, y) for x, y in pts]
            d.polygon(pts, fill=INK)
        d.rectangle([0, 0, CW - 1, CH - 1], outline=INK, width=10)
        cells.append((f"chev_{side}", im))
    im, d = board()
    centre(d, 22, "WELCOME TO", 38, fill=(190, 236, 232))
    centre(d, 66, "NAGISA BAY", 88)
    centre(d, 162, f"Loop {km(length)}  -  Climb {km(climb_len)} @ {avg:.1f}%", 32)
    cells.append(("welcome", im))

    assert len(cells) <= COLS * ROWS, len(cells)
    atlas = Image.new("RGB", (CW * COLS, CH * ROWS), (40, 40, 40))
    for i, (_, im) in enumerate(cells):
        atlas.paste(im, ((i % COLS) * CW, (i // COLS) * CH))
    os.makedirs(OUT, exist_ok=True)
    atlas.save(os.path.join(OUT, "NB_Wayfinding_Atlas.png"))
    with open(os.path.join(OUT, "NB_Wayfinding_Atlas.json"), "w") as fh:
        json.dump({"cols": COLS, "rows": ROWS, "names": [n for n, _ in cells]}, fh, indent=1)
    print(f"atlas cells={len(cells)} climb={climb_len:.0f} m avg={avg:.2f}% max200={mx:.2f}% length={length:.0f}")


if __name__ == "__main__":
    main()

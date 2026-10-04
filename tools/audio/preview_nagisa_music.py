"""Renders a listening preview of the Nagisa Drive stems as the rider would hear them: the whole 16.2 km route is
compressed into 192 s (three 64 s loops) and the stems are crossfaded with the SAME zone keyframes the game uses
(parsed from Assets/Ride/Audio/NagisaMusicDirector.cs, so it cannot drift). Output: reference/audio/NagisaDrive_route_preview.wav
Run: python tools/audio/preview_nagisa_music.py"""
import os, re, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nagisa_dsp as D

src = open(os.path.join(D.ROOT, "Assets", "Ride", "Audio", "NagisaMusicDirector.cs")).read()
amap = open(os.path.join(D.ROOT, "Assets", "Ride", "Audio", "NagisaAudioMap.cs")).read()
zone = {m.group(1): float(m.group(2)) for m in re.finditer(r"Zone_(\w+) = ([\d.]+)f", amap)}
body = src[src.index("return new[]"):src.index("};", src.index("return new[]"))]
rows = []
for line in body.splitlines():
    line = line.split("//")[0].strip().rstrip(",")
    m = re.match(r"^([A-Za-z0-9_+\-. ]+?),\s*((?:[\d.]+f?,\s*){5}[\d.]+f?)$", line)
    if not m: continue
    dexpr = m.group(1).strip()
    d = 0.0
    for part in re.split(r"\s*\+\s*", dexpr.replace("f", "")):
        part = part.strip()
        if part in ("M", "BS", "H", "BE", "C", "K", "R", "CO", "BR", "F"):
            d += zone[{"M": "marinaEnd", "BS": "beachStart", "H": "hotelM", "BE": "beachEnd", "C": "climbStart", "K": "komM", "R": "ridgeEnd", "CO": "coastStart", "BR": "bridgeStart", "F": "finish"}[part]]
        elif "-" in part:
            a, b = part.split("-"); d += zone[{"K": "komM"}[a.strip()]] - float(b)
        else: d += float(part)
    rows.append([d] + [float(x.replace("f", "")) for x in m.group(2).split(",")])
rows = np.array(rows)
print("keyframes parsed:", len(rows))
names = ["keys", "bass", "guitar", "perc", "drive", "lead"]
stems = []
for n in names:
    a, _ = D.read_wav(os.path.join(D.OUT, "Music", f"Nagisa_{n}.wav"))
    stems.append(a if a.shape[0] == 2 else np.vstack([a[0], a[0]]))
loop = stems[0].shape[1]
total_s, route = 192, zone["finish"]
n = total_s * D.SR
t = np.arange(n) / D.SR
dist = t / total_s * route
w = np.stack([np.interp(dist, rows[:, 0], rows[:, 1 + i]) for i in range(6)])
out = np.zeros((2, n))
for i in range(6):
    idx = np.arange(n) % loop
    out += stems[i][:, idx] * w[i]
master = 0.9 / max(np.abs(out).max(), 1e-9)
out *= min(master, 1.6)
os.makedirs(os.path.join(D.ROOT, "reference", "audio"), exist_ok=True)
D.OUT = os.path.join(D.ROOT, "reference", "audio")
D.write_wav("NagisaDrive_route_preview", out)

import json

d = json.load(open(r"C:\Users\jason\OneDrive\Desktop\MapleRide\Assets\Environment\SakuraPass\SakuraRoute.json"))
s = d["samples"]
for i in range(1, len(s)):
    if 370 <= s[i]["d"] <= 520:
        a, b = s[i - 1], s[i]
        arc = b["d"] - a["d"]
        g = (b["p"][1] - a["p"][1]) / arc * 100 if arc else 0
        print(f"{i:3d} d={b['d']:7.2f} y={b['p'][1]:6.2f} x={b['p'][0]:7.2f} z={b['p'][2]:7.2f} step={arc:5.2f} grade={g:6.1f}%")

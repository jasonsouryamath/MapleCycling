"""
NAGISA BAY - "Where the City Meets the Sea" (region id ``nagisa_bay``, board task B5).

Publishes, from ONE source of truth (this file):
  Assets/Environment/NagisaBay/NagisaRoute.json   route samples + course + checkpoints + zones
  Assets/Environment/NagisaBay/NagisaGround.bytes baked ground height + surface-class grid
  reference/good_graphics/nagisa_bay/plan_preview.png  top-down plan for review

The geography is the circled peninsula of reference/bad_graphics/new_map.png: a tropical resort
beach town between Maple City (north, across the inner harbour) and Minato (south-west).
  * SW shore  : marina basin + waterfront town (start)
  * S shore   : white-sand resort beach, hero resort hotel between road and sand
  * centre    : forested tropical hills, the climb and the Palm Ridge KOM, a ridge loop
  * E shore   : coastal bluff road running south
  * SE        : bridge approach viaduct out over the water (finish)

Everything numeric here is PROVISIONAL (design handoff: illustrative tuning).

Run with plain python (numpy + scipy + PIL), NOT Blender:
    python tools/blender/build_nagisa_route.py
"""
import json
import math
import os
import struct

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.spatial import cKDTree

REGION_ID = "nagisa_bay"
SEGMENT_ID = "nagisa"
SEGMENT_NAME = "Nagisa Bay Coast Road"
COURSE_ID = "nagisa_bay_loop"
COURSE_NAME = "Nagisa Bay - Where the City Meets the Sea"

# World placement: a clear area of the one shared scene (all other regions sit at z > -11.1 km).
ORIGIN_X = 0.0
ORIGIN_Z = -32000.0

SAMPLE_SPACING = 4.0
ROAD_HALF_WIDTH = 3.5
SHOULDER_WIDTH = 0.6
TARGET_MINUTES = 48.0          # PROVISIONAL
BANK_GAIN = 0.85
MAX_BANK_DEG = 4.0

# ------------------------------------------------------------------ plan (local metres, +x E, +z N)
CONTROL = [
    (-2470, -420),   # start: marina front (marina pontoons offshore to the SW)
    (-2340, -780),
    (-2170, -1060),
    (-1920, -1300),  # town waterfront
    (-1560, -1500),
    (-1110, -1640),
    (-560, -1720),   # resort beach begins
    (-80, -1690),
    (300, -1590),    # road swings inland round the hero hotel porte-cochere
    (720, -1660),
    (1150, -1690),   # beach cafes
    (1380, -1560),   # beach end, turn inland
    (1560, -1180),   # climb starts
    (1500, -840),
    (1210, -610),
    (1380, -300),
    (1130, 20),
    (740, 120),
    (470, 420),
    (170, 640),      # Palm Ridge KOM
    (-280, 790),
    (-720, 980),
    (-800, 1390),
    (-420, 1660),
    (180, 1720),
    (780, 1570),
    (1380, 1470),
    (1930, 1300),
    (2380, 1040),    # east coast reached
    (2520, 520),
    (2510, -80),
    (2390, -690),
    (2230, -1230),
    (2170, -1680),   # bridge approach begins
    (2360, -2130),
    (2660, -2540),   # finish: bridge approach end
]

# Coastline, land inside (local). Mainland continues NE.
COAST = [
    (-250, 2350), (-900, 2230), (-1500, 1950), (-2050, 1500), (-2420, 950), (-2640, 300),
    (-2620, -250), (-2480, -620),  # marina cove sits just south of here
    (-2330, -940), (-2080, -1230), (-1720, -1480), (-1250, -1680), (-700, -1790),
    (-100, -1830), (500, -1840), (1050, -1800), (1350, -1720), (1700, -1850), (2020, -1930),
    (2260, -1860), (2420, -1560), (2600, -1100), (2740, -500), (2800, 150), (2780, 800),
    (2830, 1500), (3050, 2300), (3600, 3000), (4400, 3600), (4400, 4200), (2000, 4200),
    (1300, 3400), (700, 2800), (200, 2480),
]

# Tropical hills: (x, z, radius, height)
HILLS = [
    (320, 300, 950, 215), (-520, 1150, 620, 165), (1320, 520, 620, 140), (880, 1480, 520, 105),
    (-1450, 350, 700, 110), (-1000, -350, 520, 70), (1950, 400, 480, 80), (100, 1900, 500, 80),
]

# Flat pads (cx, cz, half_x, half_z, y, name). Town blocks, hero hotel, marina quay.
PADS = [
    (300, -1690, 150, 55, 3.2, "hotel"),        # hero resort (tower + podium + pool deck)
    (-380, -1640, 62, 36, 3.0, "boutique"),     # boutique resort block
    (-1330, -1400, 120, 60, 3.2, "town_a"),     # pastel mid-rises inland of the road
    (-1800, -1150, 90, 60, 3.2, "town_b"),
    (-2230, -760, 90, 80, 3.0, "town_c"),
    (-2330, -900, 60, 45, 2.2, "marina_quay"),
    (900, -1740, 80, 22, 3.0, "cafes"),
]

MARINA = {"cx": -2560, "cz": -980, "rx": 200, "rz": 150}  # water basin offshore, local (not carved)

# ------------------------------------------------------------------ chapters (fraction-free: by nearest control)
CHECKPOINTS = [
    (0, "Marina Front"),
    (3, "Pastel Waterfront"),
    (6, "Nagisa Beach Promenade"),
    (8, "Grand Shiokaze Resort"),
    (12, "Hill Road Foot"),
    (19, "Palm Ridge KOM"),
    (23, "Ridge Loop"),
    (28, "East Coast Road"),
    (33, "Bridge Approach"),
]

GRID_X0, GRID_Z0 = -3600.0, -3200.0
GRID_X1, GRID_Z1 = 4400.0, 4200.0
CELL = 6.0


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


# ------------------------------------------------------------------ helpers

def catmull(points, per_span=160):
    p = np.asarray(points, float)
    p = np.vstack([p[:1], p, p[-1:]])
    t = [0.0]
    for i in range(1, len(p)):
        t.append(t[-1] + max(np.linalg.norm(p[i] - p[i - 1]), 1e-9) ** 0.5)
    out = []
    for i in range(1, len(p) - 2):
        t0, t1, t2, t3 = t[i - 1], t[i], t[i + 1], t[i + 2]
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for k in range(per_span):
            tt = t1 + (t2 - t1) * (k / per_span)
            a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1
            a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2
            a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3
            b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2
            b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3
            out.append((t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2)
    out.append(p[-2])
    return np.asarray(out)


def resample(p, spacing):
    seg = np.hypot(*np.diff(p, axis=0).T)
    arc = np.concatenate([[0], np.cumsum(seg)])
    n = int(round(arc[-1] / spacing)) + 1
    want = np.linspace(0, arc[-1], n)
    return np.stack([np.interp(want, arc, p[:, 0]), np.interp(want, arc, p[:, 1])], 1), want


def smooth(v, w):
    w = max(1, int(w) | 1)
    pad = w // 2
    head = 2 * v[0] - v[1:pad + 1][::-1]
    tail = 2 * v[-1] - v[-pad - 1:-1][::-1]
    return np.convolve(np.concatenate([head, v, tail]), np.ones(w) / w, mode="valid")


def value_noise(x, z, freq, seed):
    rng = np.random.default_rng(seed)
    lat = rng.random((257, 257))
    fx, fz = x * freq, z * freq
    ix, iz = np.floor(fx).astype(int), np.floor(fz).astype(int)
    tx, tz = fx - ix, fz - iz
    tx = tx * tx * (3 - 2 * tx)
    tz = tz * tz * (3 - 2 * tz)
    ix &= 255
    iz &= 255
    a = lat[ix, iz]; b = lat[ix + 1, iz]; c = lat[ix, iz + 1]; d = lat[ix + 1, iz + 1]
    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz


def fbm(x, z, freq, octaves, seed):
    s, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        s = s + amp * (value_noise(x, z, freq, seed + o * 17) * 2 - 1)
        norm += amp
        amp *= 0.5
        freq *= 2.03
    return s / norm


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ landform

def coast_sdf(gx, gz):
    """Signed distance to the coast (metres, + inland) on the grid, with a wobbly shoreline."""
    pts = np.asarray(COAST, float)
    dense = catmull(np.vstack([pts, pts[:1]]), 40)
    # perturb the shoreline a little so it reads natural
    n = fbm(dense[:, 0], dense[:, 1], 1 / 180.0, 3, 5) * 28.0
    tang = np.gradient(dense, axis=0)
    nrm = np.stack([-tang[:, 1], tang[:, 0]], 1)
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
    dense = dense + nrm * n[:, None]
    nx, nz = gx.shape[1], gx.shape[0]
    img = Image.new("L", (nx, nz), 0)
    poly = [((x - GRID_X0) / CELL, (z - GRID_Z0) / CELL) for x, z in dense]
    ImageDraw.Draw(img).polygon(poly, fill=255)
    land = np.asarray(img) > 127
    din = ndimage.distance_transform_edt(land) * CELL
    dout = ndimage.distance_transform_edt(~land) * CELL
    return np.where(land, din, -dout)


def landform(gx, gz, sd):
    east_cliff = smoothstep(1700, 2350, gx) * smoothstep(-1950, -1500, gz)
    beach = 0.12 + np.clip(sd, 0, 150) * 0.019
    coast_rise = np.where(east_cliff > 0, 1.0, 0.0)
    hills = np.zeros_like(gx)
    for hx, hz, r, h in HILLS:
        hills += h * np.exp(-(((gx - hx) ** 2 + (gz - hz) ** 2) / (r * r)))
    detail = fbm(gx, gz, 1 / 420.0, 5, 11)
    inland = smoothstep(90, 620, sd)
    land_y = beach + inland * (hills * (0.85 + 0.25 * detail) + 18 * detail + 6)
    # east shore: a 14-22 m bluff close to the water
    bluff = 16 + 6 * fbm(gx, gz, 1 / 300.0, 3, 21)
    land_y = np.maximum(land_y, east_cliff * smoothstep(8, 70, sd) * bluff + beach * (1 - east_cliff))
    land_y = np.where(sd > 0, land_y, 0)
    # sea floor: long turquoise shelf then a drop
    sea_y = -(np.clip(-sd, 0, 200) * 0.028 + np.clip(-sd - 200, 0, 800) * 0.05) - 0.12
    sea_y += east_cliff * np.clip(-sd, 0, 60) * -0.08
    y = np.where(sd > 0, land_y, sea_y)
    # smooth waterline transition
    return y


def main():
    root = repo_root()
    out_dir = os.path.join(root, "Assets", "Environment", "NagisaBay")
    prev_dir = os.path.join(root, "reference", "good_graphics", "nagisa_bay")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(prev_dir, exist_ok=True)

    nx = int(round((GRID_X1 - GRID_X0) / CELL)) + 1
    nz = int(round((GRID_Z1 - GRID_Z0) / CELL)) + 1
    xs = GRID_X0 + np.arange(nx) * CELL
    zs = GRID_Z0 + np.arange(nz) * CELL
    gx, gz = np.meshgrid(xs, zs)          # [nz, nx]
    sd = coast_sdf(gx, gz)
    land = landform(gx, gz, sd)

    # pads flatten the landform
    for cx, cz, hx, hz, py, _ in PADS:
        dx = np.maximum(np.abs(gx - cx) - hx, 0)
        dz = np.maximum(np.abs(gz - cz) - hz, 0)
        d = np.hypot(dx, dz)
        w = 1 - smoothstep(0, 40, d)
        land = np.where(sd > -5, land * (1 - w) + py * w, land)

    # ---- route plan
    dense = catmull(CONTROL, 220)
    plan, arc = resample(dense, SAMPLE_SPACING)
    total = float(arc[-1])

    def sample_grid(arr, px, pz):
        fx = (px - GRID_X0) / CELL
        fz = (pz - GRID_Z0) / CELL
        return ndimage.map_coordinates(arr, [fz, fx], order=1, mode="nearest")

    ground_on_route = sample_grid(land, plan[:, 0], plan[:, 1])
    sd_route = sample_grid(sd, plan[:, 0], plan[:, 1])

    # nearest control index per sample (for chapters/checkpoints)
    ctrl = np.asarray(CONTROL, float)
    ci = np.array([int(np.argmin(np.hypot(*(ctrl - p).T))) for p in plan])
    # monotone chapter progression
    ci = np.maximum.accumulate(ci)

    # bridge: from control 33 on, road leaves the land
    bridge_start_i = int(np.argmax(ci >= 33))
    y = smooth(ground_on_route, 31)
    y = np.maximum(y, 2.6)                      # always above the waterline
    y = smooth(y, 61)
    # bridge approach: rise to the deck
    deck = 30.0
    b0 = arc[bridge_start_i]
    t = np.clip((arc - b0) / max(total - b0, 1), 0, 1)
    y = np.where(arc >= b0, y[bridge_start_i] + (deck - y[bridge_start_i]) * (t ** 0.9), y)
    y = smooth(y, 41)
    # clamp grades (a real climb, but no walls)
    maxg = 0.095
    for _ in range(4):
        for i in range(1, len(y)):
            dy = y[i] - y[i - 1]
            lim = maxg * (arc[i] - arc[i - 1])
            if dy > lim: y[i] = y[i - 1] + lim
            elif dy < -lim: y[i] = y[i - 1] - lim
        y = smooth(y, 15)
    grade = smooth(np.gradient(y, arc), 21)

    # ---- frames
    p = np.stack([plan[:, 0], y, plan[:, 1]], 1)
    tang = np.gradient(p, axis=0)
    tang /= np.linalg.norm(tang, axis=1, keepdims=True)
    up = np.array([0.0, 1.0, 0.0])
    sides = np.cross(up, tang)
    sides /= np.maximum(np.linalg.norm(sides, axis=1, keepdims=True), 1e-9)
    prev_t = np.vstack([tang[:1], tang[:-1]])
    next_t = np.vstack([tang[1:], tang[-1:]])
    cr = np.cross(prev_t, next_t) @ up / (2 * SAMPLE_SPACING)
    banks = smooth(np.clip(smooth(cr, 9) * BANK_GAIN * 180.0, -MAX_BANK_DEG, MAX_BANK_DEG), 9)
    ups = np.zeros_like(p)
    for i in range(len(p)):
        r = math.radians(banks[i])
        s = sides[i]
        u = np.cross(tang[i], s)
        u /= max(np.linalg.norm(u), 1e-9)
        ups[i] = u * math.cos(r) + s * math.sin(r)
        sides[i] = s * math.cos(r) - u * math.sin(r)

    # ---- final ground: blend landform into the road corridor (skip over the viaduct)
    tree = cKDTree(plan[::2])
    dist, idx = tree.query(np.stack([gx.ravel(), gz.ravel()], 1), k=1)
    dist = dist.reshape(gx.shape)
    idx = (idx * 2).reshape(gx.shape)
    road_y = y[idx]
    on_bridge = arc[idx] > b0 + 40
    w = 1 - smoothstep(ROAD_HALF_WIDTH + SHOULDER_WIDTH + 1.6, 34.0, dist)
    w = np.where(on_bridge, 0.0, w)
    ground = land * (1 - w) + (road_y - 0.12) * w
    # cut: never let ground poke through the carriageway band
    band = (dist < ROAD_HALF_WIDTH + SHOULDER_WIDTH + 1.4) & ~on_bridge
    ground = np.where(band, np.minimum(ground, road_y - 0.12), ground)

    # ---- surface classes: 0 sea, 1 sand, 2 grass, 3 forest, 4 paved, 5 rock
    cls = np.full(gx.shape, 2, np.uint8)
    slope = np.hypot(*np.gradient(ground, CELL))
    east_cliff = smoothstep(1700, 2350, gx) * smoothstep(-1950, -1500, gz)
    sand = (sd > -2) & (sd < 110) & (east_cliff < 0.5) & (ground < 4.5) & (gz < -1480) & (gx > -1150) & (gx < 1420)
    cls[sand] = 1
    forest = (sd > 180) & (ground > 14) & (fbm(gx, gz, 1 / 260.0, 3, 31) > -0.35)
    cls[forest] = 3
    for cx, cz, hx, hz, py, name in PADS:
        m = (np.abs(gx - cx) < hx) & (np.abs(gz - cz) < hz)
        cls[m & (name != "hotel")] = 4
    cls[(slope > 0.75) & (sd > 0)] = 5
    cls[(east_cliff > 0.5) & (sd > 0) & (sd < 40)] = 5
    cls[(sd > 0) & (sd < 14) & (cls == 2) & (gz < 0)] = 5   # revetment rock on the non-beach shore
    cls[ground < -0.05] = 0
    cls[(sd <= 0)] = 0

    # ---- write ground
    with open(os.path.join(out_dir, "NagisaGround.bytes"), "wb") as f:
        f.write(struct.pack("<4sii3f", b"NBG1", nx, nz, GRID_X0 + ORIGIN_X, GRID_Z0 + ORIGIN_Z, CELL))
        f.write(ground.astype("<f4").tobytes())
        f.write(cls.astype(np.uint8).tobytes())
        f.write(np.clip(sd, -3000, 3000).astype("<f4").tobytes())

    # ---- chapters in metres
    def metre_at_ctrl(k):
        return float(arc[int(np.argmax(ci >= k))])

    zones = {
        "marinaEnd": metre_at_ctrl(3),
        "beachStart": metre_at_ctrl(6) - 80,
        "hotelM": metre_at_ctrl(8),
        "beachEnd": metre_at_ctrl(11),
        "climbStart": metre_at_ctrl(12),
        "komM": float(arc[int(np.argmax(y))]),
        "ridgeEnd": metre_at_ctrl(27),
        "coastStart": metre_at_ctrl(28),
        "bridgeStart": float(b0),
        "finish": total,
    }

    wp = lambda v: [round(float(v[0]) + ORIGIN_X, 4), round(float(v[1]), 4), round(float(v[2]) + ORIGIN_Z, 4)]
    wv = lambda v: [round(float(c), 4) for c in v]
    samples = [{"p": wp(p[i]), "t": wv(tang[i]), "s": wv(sides[i]), "u": wv(ups[i]),
                "bank": round(float(banks[i]), 3), "d": round(float(arc[i]), 3)} for i in range(len(p))]

    cps = []
    for k, name in CHECKPOINTS:
        cps.append({"segment": SEGMENT_ID, "distance": round(metre_at_ctrl(k) if k > 0 else 0.0, 2), "name": name})
    # KOM checkpoint exactly at the summit
    for c in cps:
        if c["name"].endswith("KOM"):
            c["distance"] = round(zones["komM"], 2)

    payload = {
        "_comment": "Generated by tools/blender/build_nagisa_route.py - do not edit by hand.",
        "regionId": REGION_ID,
        "roadHalfWidth": ROAD_HALF_WIDTH,
        "shoulderWidth": SHOULDER_WIDTH,
        "seaLevel": 0.0,
        "origin": [ORIGIN_X, 0.0, ORIGIN_Z],
        "zones": zones,
        "pads": [{"name": n, "c": [cx + ORIGIN_X, py, cz + ORIGIN_Z], "h": [hx, hz]} for cx, cz, hx, hz, py, n in PADS],
        "marina": {"c": [MARINA["cx"] + ORIGIN_X, 0, MARINA["cz"] + ORIGIN_Z], "r": [MARINA["rx"], MARINA["rz"]]},
        "samples": samples,
        "segments": [{"id": SEGMENT_ID, "name": SEGMENT_NAME, "samples": samples}],
        "courses": [{"id": COURSE_ID, "name": COURSE_NAME, "closed": False, "targetMinutes": TARGET_MINUTES,
                     "legs": [{"segment": SEGMENT_ID, "from": 0.0, "to": round(total, 3)}]}],
        "checkpoints": cps,
    }
    with open(os.path.join(out_dir, "NagisaRoute.json"), "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=0)

    # ---- report
    asc = float(np.sum(np.clip(np.diff(y), 0, None)))
    kom = int(np.argmax(y))
    cs = int(np.argmax(arc >= zones["climbStart"]))
    print(f"[nagisa] {len(samples)} samples, {total / 1000:.3f} km, ascent +{asc:.0f} m")
    print(f"[nagisa] elev {y.min():.1f}..{y.max():.1f} m; KOM {y[kom]:.1f} m at {arc[kom] / 1000:.2f} km; "
          f"climb {arc[cs] / 1000:.2f}->{arc[kom] / 1000:.2f} km gain {y[kom] - y[cs]:.0f} m "
          f"avg {(y[kom] - y[cs]) / max(arc[kom] - arc[cs], 1) * 100:.1f}%")
    print(f"[nagisa] grade {grade.min() * 100:.1f}%..{grade.max() * 100:.1f}%")
    print(f"[nagisa] min sd on route (m from coast, + inland) before bridge: {sd_route[:bridge_start_i].min():.0f}")
    print(f"[nagisa] zones {json.dumps({k: round(v) for k, v in zones.items()})}")
    for c in cps:
        print(f"[nagisa]   cp {c['distance'] / 1000:6.2f} km  {c['name']}")
    for k, (cx_, cz_) in enumerate(CONTROL[:12]):
        print(f"[nagisa]   ctrl {k:2d} sd {float(sample_grid(sd, np.array([cx_]), np.array([cz_]))[0]):6.0f} m")
    print(f"[nagisa] grid {nx}x{nz} @ {CELL} m")

    # ---- preview plan
    pal = np.array([[40, 120, 170], [240, 228, 196], [120, 170, 90], [50, 110, 60], [170, 170, 175], [120, 110, 100]], np.uint8)
    img = pal[cls]
    shade = np.clip(0.75 + np.gradient(ground, axis=1) * 0.08, 0.5, 1.2)
    img = np.clip(img * shade[..., None], 0, 255).astype(np.uint8)
    im = Image.fromarray(img[::-1]).convert("RGB")
    dr = ImageDraw.Draw(im)
    pts = [((q[0] - GRID_X0) / CELL, (GRID_Z1 - q[1]) / CELL) for q in plan[::5]]
    dr.line(pts, fill=(255, 255, 255), width=3)
    for c in cps:
        i = int(np.argmax(arc >= c["distance"]))
        x, z = (plan[i, 0] - GRID_X0) / CELL, (GRID_Z1 - plan[i, 1]) / CELL
        dr.ellipse([x - 6, z - 6, x + 6, z + 6], fill=(255, 60, 60))
        dr.text((x + 8, z - 6), c["name"], fill=(0, 0, 0))
    for cx, cz, hx, hz, py, n in PADS:
        dr.rectangle([(cx - hx - GRID_X0) / CELL, (GRID_Z1 - cz - hz) / CELL, (cx + hx - GRID_X0) / CELL, (GRID_Z1 - cz + hz) / CELL], outline=(255, 0, 255))
    im = im.resize((im.width // 2, im.height // 2))
    im.save(os.path.join(prev_dir, "plan_preview.png"))

    # elevation profile preview
    W, H = 1400, 360
    pr = Image.new("RGB", (W, H), (250, 250, 250))
    d2 = ImageDraw.Draw(pr)
    ys = y
    pts = [(20 + (arc[i] / total) * (W - 40), H - 20 - (ys[i] - ys.min()) / (ys.max() - ys.min() + 1e-6) * (H - 60)) for i in range(0, len(arc), 4)]
    d2.line(pts, fill=(30, 90, 200), width=2)
    for c in cps:
        x = 20 + c["distance"] / total * (W - 40)
        d2.line([(x, 20), (x, H - 20)], fill=(220, 80, 80))
        d2.text((x + 2, 22), c["name"][:14], fill=(0, 0, 0))
    pr.save(os.path.join(prev_dir, "profile_preview.png"))


if __name__ == "__main__":
    main()

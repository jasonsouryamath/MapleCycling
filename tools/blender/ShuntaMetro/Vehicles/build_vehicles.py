"""Shunta Metro vehicles: taxi, kei car, sedan, delivery van, kei truck, city bus, scooter.
Unity axes: +z forward, +y up, +x right; metres; origin at ground centre.
blender.exe -b --python build_vehicles.py [-- only_name]"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from sm_lib import *  # noqa: E402,F401,F403
import sm_lib as S  # noqa: E402


def shell_hw_at(keys, z, y, nx=3.6, ny=4.0):
    yb, yt, hw = keyinterp(keys, z)
    ym, hh = (yb + yt) / 2, (yt - yb) / 2
    s = max(-0.999, min(0.999, (y - ym) / hh))
    c = max(0.0, 1 - abs(s) ** ny) ** (1.0 / nx)
    return hw * c


def car(p, d):
    """Generic road car. p: dict of keys/params. d: 0 = LOD0, 1 = LOD1."""
    g, sg = Geo(), SGeo()
    z0, z1 = p["z0"], p["z1"]
    nst = 40 if d == 0 else 13
    seg = 28 if d == 0 else 14
    paint = p["paint"]
    # ---- lower shell
    sk = p["shell"]
    st = []
    for i in range(nst):
        z = lerp(z0, z1, i / (nst - 1))
        yb, yt, hw = keyinterp(sk, z)
        st.append((z, 0.0, (yb + yt) / 2, hw, (yt - yb) / 2, p.get("nx", 3.6), p.get("ny", 4.0)))
    sg.loft(st, seg, paint, capmat=paint)
    # ---- greenhouse
    ck = p["cabin"]
    cz0, cz1 = ck[0][0], ck[-1][0]
    cy0 = p["cabin_y0"]
    cst = []
    nc = 24 if d == 0 else 9
    for i in range(nc):
        z = lerp(cz0, cz1, i / (nc - 1))
        yt, hw = keyinterp(ck, z)
        cst.append((z, 0.0, (yt + cy0) / 2, hw, max((yt - cy0) / 2, 0.01), 3.0, 5.0))
    gz = p.get("glass_z", (-9, 9))
    roofmat = p.get("roof", paint)

    def cmat(k, segn, si):
        t = -math.pi / 2 + math.tau * k / segn
        zc = lerp(cz0, cz1, (si + 0.5) / (nc - 1))
        if math.sin(t) > 0.86 or not (gz[0] <= zc <= gz[1]):
            return roofmat
        return "SM_Glass"
    sg.loft(cst, seg, paint, matfn=cmat, capmat="SM_Glass")
    geos = [sg, g]
    r, tw, ww = p["wr"], p["track"], p["tw"]
    for wz in p["wheels_z"]:
        for sx in (-1, 1):
            wheel(g, sx * tw, r, wz, r, ww, sx, d)
    if d == 0:
        # wheel-arch shadows
        for wz in p["wheels_z"]:
            for sx in (-1, 1):
                ax = shell_hw_at(sk, wz, r, p.get("nx", 3.6), p.get("ny", 4.0))
                disc_x(g, sx * (ax + 0.003), r + 0.02, wz, r * 1.2, 16, "SM_Trim", sx)
        # underbody
        g.box((0, 0.2, (z0 + z1) / 2), (p["shell"][2][3] * 1.5, 0.04, z1 - z0 - 0.5), "SM_Trim")
    # ---- front / rear fascia details
    hw_f = keyinterp(sk, z1 - 0.12)
    hw_r = keyinterp(sk, z0 + 0.12)
    yb_f, yt_f = hw_f[0], hw_f[1]
    yb_r, yt_r = hw_r[0], hw_r[1]
    hy = p.get("light_y", (yb_f + yt_f) / 2 + 0.05)
    hx = p.get("light_x", hw_f[2] * 0.68)
    lw = p.get("light_w", 0.36)
    # headlights (emissive, own material) + indicators
    for sx in (-1, 1):
        g.box((sx * hx, hy, z1 - 0.12), (lw, 0.13 if d == 0 else 0.15, 0.14), "SM_Headlight")
        g.box((sx * (hx + lw * 0.52), hy - 0.02, z1 - 0.16), (0.1, 0.1, 0.1), "SM_Indicator")
    ty = p.get("tail_y", (yb_r + yt_r) / 2 + 0.12)
    for sx in (-1, 1):
        g.box((sx * (hw_r[2] * 0.7), ty, z0 + 0.1), (0.42, 0.14, 0.12), "SM_Taillight")
    g.box((0, (yb_f + yt_f) / 2 - 0.08, z1 - 0.05), (hx * 1.2, 0.14, 0.1), "SM_Trim")      # grille
    g.box((0, p.get("plate_y", 0.4), z1 - 0.02), (0.34, 0.17, 0.03), "SM_Plate")
    g.box((0, p.get("plate_y", 0.4) + 0.03, z0 + 0.04), (0.34, 0.17, 0.03), "SM_Plate")
    if d == 0:
        g.box((0, yb_f + 0.1, z1 - 0.1), (hw_f[2] * 1.5, 0.2, 0.14), "SM_Trim")             # bumpers
        g.box((0, yb_r + 0.1, z0 + 0.1), (hw_r[2] * 1.5, 0.2, 0.14), "SM_Trim")
        # mirrors, door lines, handles
        mzc = cz1 - (cz1 - cz0) * 0.22
        ym_b = p["cabin_y0"] + 0.05
        for sx in (-1, 1):
            hwm = keyinterp(sk, mzc)[2]
            g.box((sx * (hwm + 0.1), ym_b + 0.1, mzc + 0.1), (0.18, 0.1, 0.1), paint)
            for dz in p.get("door_lines", []):
                hwd = shell_hw_at(sk, dz, (ym_b + 0.1), p.get("nx", 3.6), p.get("ny", 4.0))
                g.box((sx * (hwd + 0.002), ym_b - 0.05, dz), (0.012, 0.62, 0.012), "SM_Trim")
            for hz in p.get("handles", []):
                hwd = shell_hw_at(sk, hz, ym_b, p.get("nx", 3.6), p.get("ny", 4.0))
                g.box((sx * (hwd + 0.01), ym_b + 0.02, hz), (0.03, 0.03, 0.14), "SM_Chrome")
    # extras hook
    if "extras" in p:
        p["extras"](g, sg, d)
    return geos


# ------------------------------------------------------------------------------------ models
def taxi_extras(g, sg, d):
    roof_y = 1.69
    g.box((0, roof_y + 0.03, -0.35), (0.50, 0.04, 0.22), "SM_Trim")
    g.box((0, roof_y + 0.17, -0.35), (0.56, 0.2, 0.26), "SM_TaxiSignYellow")
    g.box((0, roof_y + 0.17, -0.2), (0.4, 0.12, 0.02), "SM_TaxiSignGreen")
    g.box((0, roof_y + 0.17, -0.5), (0.4, 0.12, 0.02), "SM_TaxiSignGreen")
    for sx in (-1, 1):
        g.box((sx * 0.29, roof_y + 0.17, -0.35), (0.02, 0.14, 0.2), "SM_TaxiSignGreen")
    if d == 0:
        g.box((0.0, roof_y - 0.01, 0.35), (0.04, 0.02, 0.25), "SM_Chrome")


def vehicle_defs():
    D = {}
    D["Taxi"] = dict(
        z0=-2.345, z1=2.345, paint="SM_PaintTaxi", wr=0.33, track=0.70, tw=0.21, wheels_z=(-1.39, 1.39),
        shell=[(-2.345, .36, .60, .64), (-2.28, .28, .78, .78), (-1.9, .22, .95, .85), (1.5, .22, .97, .85),
               (2.0, .25, .90, .83), (2.28, .30, .74, .78), (2.345, .36, .60, .66)],
        cabin=[(-1.62, .98, .66), (-1.25, 1.62, .74), (-0.9, 1.69, .77), (0.5, 1.69, .77), (0.85, 1.46, .75), (1.2, 1.02, .70)],
        cabin_y0=0.82, door_lines=(-0.55, 0.45, 1.1), handles=(-0.35, 0.85), extras=taxi_extras, roof="SM_PaintTaxi")
    D["Sedan"] = dict(
        z0=-2.38, z1=2.38, paint="SM_PaintBlack", wr=0.34, track=0.78, tw=0.23, wheels_z=(-1.45, 1.45),
        shell=[(-2.38, .34, .62, .70), (-2.3, .28, .8, .84), (-1.8, .22, .94, .90), (1.7, .22, .90, .90),
               (2.15, .24, .78, .86), (2.38, .30, .60, .74)],
        cabin=[(-1.25, .98, .72), (-0.7, 1.43, .80), (0.1, 1.46, .81), (0.8, 1.27, .78), (1.28, 0.98, .72)],
        cabin_y0=0.80, door_lines=(-0.45, 0.45, 1.05), handles=(-0.3, 0.8), roof="SM_PaintBlack")
    D["KeiCar"] = dict(
        z0=-1.7, z1=1.7, paint="SM_PaintWhite", wr=0.27, track=0.62, tw=0.17, wheels_z=(-1.0, 1.0),
        shell=[(-1.7, .34, .90, .62), (-1.62, .28, 1.05, .70), (-1.2, .22, 1.12, .72), (1.0, .22, 1.02, .72),
               (1.5, .26, .86, .68), (1.7, .32, .70, .60)],
        cabin=[(-1.5, 1.1, .64), (-1.42, 1.72, .66), (-0.9, 1.78, .69), (0.6, 1.76, .69), (0.95, 1.4, .67), (1.12, 1.05, .63)],
        cabin_y0=0.95, door_lines=(-0.2, 0.7), handles=(0.0, 0.85), nx=3.8, ny=4.5, roof="SM_PaintWhite",
        light_w=0.3, plate_y=0.45)
    D["DeliveryVan"] = dict(
        z0=-2.4, z1=2.4, paint="SM_VanBox", wr=0.34, track=0.76, tw=0.22, wheels_z=(-1.4, 1.4),
        shell=[(-2.4, .38, 1.0, .75), (-2.3, .3, 1.15, .84), (-1.9, .26, 1.18, .86), (1.3, .26, 1.1, .86),
               (2.0, .28, .92, .84), (2.4, .34, .72, .74)],
        cabin=[(-2.28, 1.2, .80), (-2.2, 1.97, .82), (0.9, 2.0, .83), (1.35, 1.55, .80), (1.9, 1.08, .74)],
        cabin_y0=1.05, glass_z=(0.55, 1.9), door_lines=(0.8,), handles=(0.95,), nx=4.0, ny=5.0, roof="SM_VanBox",
        plate_y=0.5, extras=van_extras if False else None)
    return D


def van_extras(g, sg, d):
    pass


def van_stripes(g, sg, d):
    for sx in (-1, 1):
        g.box((sx * 0.865, 1.0, -0.6), (0.01, 0.22, 2.6), "SM_BusStripe")
        g.box((sx * 0.865, 1.45, -0.6), (0.01, 0.4, 1.6), "SM_NeonRed" if sx > 0 else "SM_NeonCyan")


# ------------------------------------------------------------------------------------ kei truck
def kei_truck(d):
    p = dict(
        z0=-0.25, z1=1.7, paint="SM_PaintSilver", wr=0.27, track=0.62, tw=0.17, wheels_z=(-1.0, 1.0),
        shell=[(-0.25, .42, 1.15, .72), (0.0, .3, 1.1, .74), (1.0, .26, 1.0, .74), (1.5, .26, .86, .70), (1.7, .32, .7, .62)],
        cabin=[(-0.2, 1.15, .66), (-0.15, 1.75, .68), (0.5, 1.82, .69), (1.0, 1.62, .67), (1.25, 1.1, .63)],
        cabin_y0=1.0, door_lines=(0.2, 0.9), handles=(0.35,), nx=3.8, ny=4.5, roof="SM_PaintSilver",
        light_w=0.3, plate_y=0.45)
    geos = car(p, d)
    g = geos[1]
    # frame + bed
    g.box((0, 0.5, -1.0), (1.2, 0.12, 2.0), "SM_Trim")
    g.box((0, 0.64, -1.0), (1.4, 0.06, 2.0), "SM_Metal")
    for sx in (-1, 1):
        g.box((sx * 0.7, 0.9, -1.0), (0.06, 0.4, 2.0), "SM_PaintSilver")
        # fenders over rear wheels
        g.box((sx * 0.72, 0.58, -1.0), (0.1, 0.12, 0.7), "SM_PaintSilver")
    g.box((0, 0.9, -2.0), (1.46, 0.4, 0.06), "SM_PaintSilver")
    g.box((0, 1.0, -0.2), (1.46, 0.6, 0.06), "SM_PaintSilver")
    g.box((0, 0.66, -1.0), (1.3, 0.02, 1.9), "SM_Metal")
    # cargo (boxes tied down) at LOD0
    if d == 0:
        g.box((-0.3, 0.95, -1.4), (0.6, 0.55, 0.6), "SM_ConcreteB")
        g.box((0.35, 0.9, -1.0), (0.5, 0.45, 0.5), "SM_FabricBlue")
        g.box((0.0, 0.7, -0.4), (0.9, 0.03, 0.05), "SM_FabricRed")
    for sx in (-1, 1):
        wheel(g, sx * 0.62, 0.27, -1.3, 0.27, 0.17, sx, d)
    g.box((0.0, 0.58, -2.02), (0.4, 0.22, 0.03), "SM_Taillight") if False else None
    for sx in (-1, 1):
        g.box((sx * 0.55, 0.78, -2.04), (0.22, 0.12, 0.04), "SM_Taillight")
    return geos


# ------------------------------------------------------------------------------------ bus
def city_bus(d):
    g, sg = Geo(), SGeo()
    z0, z1 = -5.25, 5.25
    nst = 22 if d == 0 else 9
    seg = 24 if d == 0 else 12
    keys = [(-5.25, .62, 2.7, .95), (-5.15, .5, 3.05, 1.18), (-4.6, .45, 3.15, 1.25), (4.5, .45, 3.1, 1.25),
            (5.1, .5, 2.9, 1.2), (5.25, .6, 2.55, 1.1)]
    st = []
    for i in range(nst):
        z = lerp(z0, z1, i / (nst - 1))
        yb, yt, hw = keyinterp(keys, z)
        st.append((z, 0, (yb + yt) / 2, hw, (yt - yb) / 2, 8.0, 8.0))
    sg.loft(st, seg, "SM_PaintBus", capmat="SM_PaintBus")
    geos = [sg, g]
    # windows: continuous dark glass bands with paint pillars (LOD0) / one band (LOD1)
    xs = 1.263
    n_win = 7 if d == 0 else 1
    zA, zB = -4.3, 3.6
    seglen = (zB - zA) / n_win
    for sx in (-1, 1):
        for i in range(n_win):
            za = zA + i * seglen + (0.06 if d == 0 else 0)
            zb = zA + (i + 1) * seglen - (0.06 if d == 0 else 0)
            # front door gap on the right side
            if sx > 0 and d == 0 and i in (5,):
                continue
            hexa(g, [(sx * xs, 1.45, za), (sx * xs, 1.45, zb), (sx * (xs + 0.012), 1.45, zb), (sx * (xs + 0.012), 1.45, za)],
                 [(sx * xs, 2.62, za), (sx * xs, 2.62, zb), (sx * (xs + 0.012), 2.62, zb), (sx * (xs + 0.012), 2.62, za)], "SM_Glass")
        # skirt stripe
        g.box((sx * 1.262, 1.1, 0.0), (0.012, 0.3, 9.6), "SM_BusStripe")
    # right-side doors (passenger side, +x): front and mid
    for zc in ((3.9, -0.8) if d == 0 else (3.9,)):
        g.box((1.262, 1.62, zc), (0.016, 2.2, 1.1), "SM_Glass")
        g.box((1.268, 1.62, zc), (0.012, 2.28, 0.04), "SM_Trim")
        g.box((1.268, 1.62, zc + 0.55), (0.012, 2.28, 0.04), "SM_Trim")
        g.box((1.268, 1.62, zc - 0.55), (0.012, 2.28, 0.04), "SM_Trim")
    # windscreen
    hexa(g, [(-1.0, 1.0, 5.12), (1.0, 1.0, 5.12), (1.0, 1.0, 5.2), (-1.0, 1.0, 5.2)],
         [(-1.0, 2.62, 5.04), (1.0, 2.62, 5.04), (1.0, 2.62, 5.18), (-1.0, 2.62, 5.18)], "SM_Glass")
    # destination sign + housing
    g.box((0, 2.88, 5.17), (1.9, 0.34, 0.1), "SM_Trim")
    g.box((0, 2.88, 5.225), (1.7, 0.24, 0.03), "SM_DestSign")
    for sx in (-1, 1):
        g.box((sx * 1.245, 2.78, 3.2), (0.04, 0.2, 0.9), "SM_DestSign")     # side route display
    g.box((0, 2.88, -5.27), (0.9, 0.2, 0.03), "SM_DestSign")                # rear number
    # lights
    for sx in (-1, 1):
        g.box((sx * 0.95, 0.85, 5.24), (0.4, 0.2, 0.08), "SM_Headlight")
        g.box((sx * 1.08, 0.62, 5.2), (0.2, 0.12, 0.08), "SM_Indicator")
        g.box((sx * 1.0, 1.0, -5.28), (0.3, 0.4, 0.06), "SM_Taillight")
        g.box((sx * 1.0, 0.62, -5.28), (0.3, 0.14, 0.06), "SM_Indicator")
    g.box((0, 0.55, 5.27), (0.5, 0.2, 0.03), "SM_Plate")
    g.box((0, 0.6, -5.3), (0.5, 0.2, 0.03), "SM_Plate")
    g.box((0, 0.5, 5.2), (2.0, 0.14, 0.14), "SM_Trim")
    # interior glow strip (visible through windows)
    g.box((0, 2.4, -0.3), (2.0, 0.04, 8.4), "SM_InteriorLight")
    # roof AC
    g.box((0, 3.3, -1.2), (1.5, 0.24, 2.6), "SM_ACUnit")
    if d == 0:
        g.box((0, 3.42, -1.2), (1.3, 0.04, 2.4), "SM_Metal")
        for sx in (-1, 1):
            g.box((sx * 1.28, 2.9, 5.0), (0.05, 0.5, 0.28), "SM_Trim")      # mirror arms
            g.box((sx * 1.45, 2.9, 5.05), (0.04, 0.45, 0.22), "SM_Chrome")
        g.box((0, 1.0, 0), (2.1, 0.06, 10), "SM_Trim")
    # wheels (front steer, rear)
    for wz in (3.4, -3.2):
        for sx in (-1, 1):
            wheel(g, sx * 1.02, 0.5, wz, 0.5, 0.3, sx, d, spokes=8)
            if d == 0:
                disc_x(g, sx * 1.255, 0.5, wz, 0.62, 16, "SM_Trim", sx)
    g.box((0, 0.35, 0), (2.0, 0.05, 10.2), "SM_Trim")
    return geos


# ------------------------------------------------------------------------------------ scooter
def scooter(d):
    g, sg = Geo(), SGeo()
    geos = [sg, g]
    r = 0.22
    for wz in (-0.55, 0.62):
        cyl_x(0, r, wz, r, 0.1, 18 if d == 0 else 9, "SM_Tire", g)
        for sx in (-1, 1):
            disc_x(g, sx * 0.052, r, wz, r * 0.62, 12, "SM_Rim", sx)
    # floor + body loft along z (rounded)
    keys = [(-0.95, .45, .8, .09), (-0.8, .35, .86, .17), (-0.3, .26, .72, .21), (0.1, .2, .3, .22), (0.35, .26, .42, .19),
            (0.5, .32, 1.1, .12), (0.62, .36, 1.2, .08)]
    st = []
    n = 20 if d == 0 else 8
    for i in range(n):
        z = lerp(-0.95, 0.62, i / (n - 1))
        yb, yt, hw = keyinterp(keys, z)
        st.append((z, 0, (yb + yt) / 2, hw, (yt - yb) / 2, 3.0, 3.0))
    sg.loft(st, 14 if d == 0 else 8, "SM_PaintRed", capmat="SM_PaintRed")
    # seat
    g.box((0, 0.82, -0.52), (0.28, 0.08, 0.66), "SM_Seat")
    # rear light, headlight, indicators
    g.box((0, 0.74, -0.97), (0.22, 0.09, 0.06), "SM_Taillight")
    g.box((0, 1.0, 0.7), (0.22, 0.2, 0.1), "SM_Headlight")
    for sx in (-1, 1):
        g.box((sx * 0.25, 1.12, 0.62), (0.06, 0.05, 0.05), "SM_Indicator")
        g.box((sx * 0.21, 1.0, -0.93), (0.05, 0.05, 0.05), "SM_Indicator")
    # fork, handlebar, mirrors
    beam(g, (0.06, 0.25, 0.62), (0.06, 1.0, 0.5), 0.045, "SM_Chrome")
    beam(g, (-0.06, 0.25, 0.62), (-0.06, 1.0, 0.5), 0.045, "SM_Chrome")
    beam(g, (-0.42, 1.12, 0.46), (0.42, 1.12, 0.46), 0.04, "SM_Trim")
    g.box((0, 1.05, 0.52), (0.28, 0.12, 0.14), "SM_Trim")
    if d == 0:
        for sx in (-1, 1):
            beam(g, (sx * 0.38, 1.14, 0.46), (sx * 0.45, 1.36, 0.42), 0.02, "SM_Trim")
            g.box((sx * 0.45, 1.38, 0.42), (0.1, 0.07, 0.02), "SM_Chrome")
        g.box((0, 0.35, -0.7), (0.1, 0.08, 0.35), "SM_Chrome")           # exhaust
        g.box((0, 0.44, -0.95), (0.16, 0.1, 0.02), "SM_Plate")
        g.box((0.0, 0.42, 0.65), (0.04, 0.35, 0.06), "SM_PaintRed")        # front fender
        for sx in (-1, 1):
            g.box((sx * 0.13, 0.35, -0.2), (0.06, 0.04, 0.5), "SM_Trim")     # footboard
    return geos


# ------------------------------------------------------------------------------------ main
if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else None
    write_materials_json()
    D = vehicle_defs()
    D["DeliveryVan"]["extras"] = van_stripes
    jobs = {
        "Taxi": lambda d: car(D["Taxi"], d),
        "Sedan": lambda d: car(D["Sedan"], d),
        "KeiCar": lambda d: car(D["KeiCar"], d),
        "DeliveryVan": lambda d: car(D["DeliveryVan"], d),
        "KeiTruck": kei_truck,
        "CityBus": city_bus,
        "Scooter": scooter,
    }
    for k, fn in jobs.items():
        if only and k != only:
            continue
        export_lods("SM_" + k, fn, "Vehicles")

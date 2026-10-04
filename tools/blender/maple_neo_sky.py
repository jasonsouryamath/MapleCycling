"""
Maple City vaporwave neo-Tokyo night sky - baked equirectangular panorama (python + numpy + Pillow; no Blender needed).

    python tools/blender/maple_neo_sky.py            -> Assets/Environment/MapleCity/Textures/MapleNeoSky.png (4096 x 2048)

Used as an HDRP HDRI sky (an HDRI is NOT fogged and can carry a real moon, unlike a mesh disc, which the region's exponential fog
swallows). Layers, bottom to top: zenith indigo -> violet mid -> hot-pink horizon with a magenta glow band; a faint cyan counter-glow
on the opposite horizon; ~9000 stars (colour-tinted, brightness power-law, faded near the horizon); a large pale moon with maria
and limb darkening, a soft halo and a 22-degree ring; two bands of high clouds lit pink from below and silvered by the moon.
"""
import os
import numpy as np
from PIL import Image

W, H = 4096, 2048
rng = np.random.default_rng(20261003)
# pixel -> direction (equirect, +Y up). u: azimuth 0..2pi (0 = +Z, increasing toward +X), v: elevation +90..-90
az = (np.arange(W) + 0.5) / W * 2 * np.pi
el = (0.5 - (np.arange(H) + 0.5) / H) * np.pi
AZ, EL = np.meshgrid(az, el)
dx, dy, dz = np.cos(EL) * np.sin(AZ), np.sin(EL), np.cos(EL) * np.cos(AZ)
D = np.stack([dx, dy, dz], -1)


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def hsv_lerp(c0, c1, t):
    return c0[None, None, :] * (1 - t[..., None]) + c1[None, None, :] * t[..., None]


elev = np.degrees(EL)
ZEN = np.array([0.030, 0.024, 0.165]); MID = np.array([0.30, 0.10, 0.48]); HOR = np.array([0.95, 0.30, 0.62]); GND = np.array([0.06, 0.04, 0.14])
t_up = smooth(0, 70, elev)
t_mid = smooth(0, 24, elev)
sky = hsv_lerp(HOR, MID, t_mid)
sky = sky * (1 - t_up[..., None]) + ZEN[None, None, :] * t_up[..., None]
# below the horizon: dark violet, with the pink glow continuing a little under the line then falling away
below = smooth(0, -18, elev)
sky = sky * (1 - below[..., None]) + GND[None, None, :] * below[..., None]
# magenta glow band hugging the horizon, stronger toward the "city" side (azimuth ~ 200 deg), cyan counter-glow opposite
city_az = np.radians(200.0)
side = 0.5 + 0.5 * np.cos(AZ - city_az)
band = np.exp(-(elev / 7.5) ** 2)
sky += (np.array([1.0, 0.22, 0.58])[None, None, :] * (0.38 * band * (0.45 + 0.8 * side))[..., None])
sky += (np.array([0.10, 0.75, 1.0])[None, None, :] * (0.22 * band * (1 - side) ** 2)[..., None])


# ------------------------------------------------------------------ clouds (fbm on the sphere), two bands
def fbm(p, octaves=5):
    # cheap value-noise fbm via random lattice on a 3D grid, trilinear lookups through hashing
    out = np.zeros(p.shape[:2])
    amp, freq = 0.5, 3.0
    for o in range(octaves):
        q = p * freq
        i = np.floor(q).astype(np.int64)
        f = q - i
        f = f * f * (3 - 2 * f)

        def h(ix, iy, iz):
            n = (ix * 374761393 + iy * 668265263 + iz * 2147483647 + o * 1013904223) & 0xFFFFFFFF
            n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
            return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0
        acc = 0
        for cx in (0, 1):
            for cy in (0, 1):
                for cz in (0, 1):
                    w = (f[..., 0] if cx else 1 - f[..., 0]) * (f[..., 1] if cy else 1 - f[..., 1]) * (f[..., 2] if cz else 1 - f[..., 2])
                    acc = acc + w * h(i[..., 0] + cx, i[..., 1] + cy, i[..., 2] + cz)
        out += amp * acc
        amp *= 0.5
        freq *= 2.03
    return out


Ds = D[::2, ::2]                                           # clouds at half resolution, upsampled (they are soft anyway)
cl = fbm(Ds * np.array([1.0, 2.6, 1.0]) + 11.7)
cl = np.array(Image.fromarray((cl * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)).astype(np.float64) / 255.0
cl_mask = smooth(0.46, 0.74, cl) * smooth(4, 14, elev) * (1 - smooth(34, 62, elev))
cloud_col = np.array([1.0, 0.42, 0.82])[None, None, :] * (0.55 + 0.45 * (1 - smooth(0, 30, elev)))[..., None]
sky = sky * (1 - 0.55 * cl_mask[..., None]) + cloud_col * (0.55 * cl_mask[..., None]) * (0.7 + 0.3 * side[..., None])
cl2 = fbm(Ds * np.array([2.2, 4.4, 2.2]) + 3.1)
cl2 = np.array(Image.fromarray((cl2 * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)).astype(np.float64) / 255.0
cl2_mask = smooth(0.55, 0.8, cl2) * smooth(26, 40, elev) * (1 - smooth(60, 80, elev))
sky += np.array([0.35, 0.22, 0.65])[None, None, :] * (0.28 * cl2_mask)[..., None]

# ------------------------------------------------------------------ stars
N = 9000
v = rng.normal(size=(N, 3)); v /= np.linalg.norm(v, axis=1, keepdims=True)
v = v[v[:, 1] > 0.06]
mag = rng.power(0.35, size=len(v))                         # most faint, a few bright
col = np.array([[1.0, 0.92, 0.95], [0.80, 0.88, 1.0], [1.0, 0.75, 0.90], [0.75, 1.0, 1.0]])[rng.integers(0, 4, size=len(v))]
stars = np.zeros((H, W, 3))
u = (np.arctan2(v[:, 0], v[:, 2]) % (2 * np.pi)) / (2 * np.pi) * W
vv = (0.5 - np.arcsin(v[:, 1]) / np.pi) * H
for k in range(len(v)):
    x, y = int(u[k]) % W, int(vv[k])
    elk = np.degrees(np.arcsin(v[k, 1]))
    b = (0.25 + 1.6 * mag[k] ** 2) * smooth(2, 22, elk)
    r = 1 + int(mag[k] > 0.93)
    for oy in range(-r, r + 1):
        for ox in range(-r, r + 1):
            yy, xx = y + oy, (x + ox) % W
            if 0 <= yy < H:
                stars[yy, xx] += col[k] * b * np.exp(-(ox * ox + oy * oy) / (0.6 * r * r + 0.4))
fade = (1 - 0.8 * np.clip(cl_mask, 0, 1))[..., None]
sky += stars * fade

# ------------------------------------------------------------------ moon: big, pale lavender, maria + limb darkening + halo + ring
m_az, m_el, m_r = np.radians(58.0), np.radians(27.0), np.radians(4.6)
md = np.array([np.cos(m_el) * np.sin(m_az), np.sin(m_el), np.cos(m_el) * np.cos(m_az)])
cosang = np.clip((D * md).sum(-1), -1, 1)
ang = np.arccos(cosang)
disc = 1 - smooth(m_r * 0.97, m_r * 1.0, ang)
# local disc coords for surface detail
up = np.array([0, 1, 0.0]); rgt = np.cross(up, md); rgt /= np.linalg.norm(rgt); upv = np.cross(md, rgt)
px = (D * rgt).sum(-1) / np.maximum(np.sin(m_r), 1e-6); py = (D * upv).sum(-1) / np.maximum(np.sin(m_r), 1e-6)
rr = np.sqrt(px * px + py * py).clip(0, 1)
limb = 1 - 0.38 * rr ** 2.2
maria = fbm(np.stack([px * 1.4 + 5, py * 1.4 + 2, np.zeros_like(px)], -1)[::2, ::2], 4)
maria = np.array(Image.fromarray((maria * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)).astype(np.float64) / 255.0
surf = 0.78 + 0.34 * (1 - smooth(0.42, 0.62, maria))
moon_col = np.array([0.93, 0.88, 1.0])
sky = sky * (1 - disc[..., None]) + (moon_col[None, None, :] * (limb * surf * 1.05)[..., None]) * disc[..., None]
halo = np.exp(-(ang / np.radians(9.0)) ** 2) * 0.55 + np.exp(-(ang / np.radians(24.0)) ** 2) * 0.22
sky += np.array([0.55, 0.50, 0.95])[None, None, :] * halo[..., None] * (1 - disc[..., None])
ring = np.exp(-((ang - np.radians(22.0)) / np.radians(0.55)) ** 2) * 0.04
sky += np.array([0.7, 0.8, 1.0])[None, None, :] * ring[..., None]

sky = np.clip(sky, 0, 1.6)
# gentle filmic compress so the bright moon keeps shape, then sRGB
sky = sky / (1 + 0.18 * sky)
sky = np.clip(sky * 1.12, 0, 1) ** (1 / 2.2)
out = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'Environment', 'MapleCity', 'Textures', 'MapleNeoSky.png'))
os.makedirs(os.path.dirname(out), exist_ok=True)
Image.fromarray((sky * 255 + 0.5).astype(np.uint8)).save(out)
print('wrote', out)
prev = Image.fromarray((sky * 255 + 0.5).astype(np.uint8)).resize((1600, 800), Image.LANCZOS)
prev.save(os.path.join(os.path.dirname(out), 'MapleNeoSky_preview.png'))

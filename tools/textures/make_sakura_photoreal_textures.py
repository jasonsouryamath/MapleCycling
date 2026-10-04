"""
E4 / Agent HQ huddle batch 2 (2026-09-30) - procedural textures for Sakura Pass.

Writes to Assets/Environment/SakuraPass/Textures/:
  Sakura_Sign_Trim.png / _Normal.png   2048^2 trim sheet shared by the summit board + chevrons
                                       (idea 8). Layout (pixels, origin TOP-left):
                                         S  summit face   x 0-1536,  y 0-512
                                         C  chevron board x 0-1536,  y 512-1000
                                         G  galvanised    x 1536-2048, y 0-1488 (posts)
                                         W  wood planks   x 0-2048,  y 1536-2048 (tiles in U)
  Sakura_Wood_Normal.png               512^2 tiling wood-grain normal (timber rails)
  Sakura_EdgeDirt_Decal.png            1024x256 road-edge dirt strip, U tiles along the road,
                                       V 0 = asphalt side, alpha fades out with a noisy edge (idea 5)
  Sakura_Ground_Splat.png              1024^2, 2x2 atlas of irregular soil/moss blotches with
                                       ragged alpha (critic: "perfect stamped circles")
  Sakura_Macro_Variation.png           256^2 tiling low-frequency grey (idea 9 tunnel overlay)
  Sakura_Bark_Tile_Normal.png          (not written - existing Sakura_Bark_Normal.png is reused)

All sizes / colours are PROVISIONAL art tuning. Run:  python make_sakura_photoreal_textures.py
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Environment", "SakuraPass", "Textures")
FONT_JP = r"C:\Windows\Fonts\YuGothB.ttc"
FONT_EN = r"C:\Windows\Fonts\segoeuib.ttf"
rng = np.random.default_rng(20260930)


# ------------------------------------------------------------------ noise helpers
def tile_noise(h, w, cells_y, cells_x, seed):
    """Periodic value noise (tiles in both axes), bicubic-upsampled lattice."""
    r = np.random.default_rng(seed)
    lat = r.random((cells_y, cells_x)).astype(np.float32)
    lat = np.pad(lat, ((0, 1), (0, 1)), mode="wrap")
    img = Image.fromarray((lat * 255).astype(np.uint8)).resize(
        (w + w // cells_x, h + h // cells_y), Image.BICUBIC)
    a = np.asarray(img, np.float32)[:h, :w] / 255.0
    return a


def fbm(h, w, base, octaves, seed, gain=0.5):
    acc = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        c = base * (2 ** o)
        acc += tile_noise(h, w, max(1, int(c * h / max(h, w))), max(1, int(c * w / max(h, w))), seed + o) * amp
        tot += amp
        amp *= gain
    return acc / tot


def height_to_normal(hgt, strength):
    gy, gx = np.gradient(hgt)
    nx, ny = -gx * strength, gy * strength          # OpenGL +Y (image rows grow downward)
    nz = np.ones_like(hgt)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save(name, arr, mode=None):
    Image.fromarray(arr, mode).save(os.path.join(OUT, name))
    print(f"[sakura-tex] {name} {arr.shape}")


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ wood grain
def wood_height(h, w, seed):
    """Grain runs along U (x). Tiles in x; plank joints every h/4 rows."""
    y = np.arange(h, dtype=np.float32)[:, None]
    warp = fbm(h, w, 3, 3, seed) * 14.0
    grain = np.sin((y + warp) * 0.55) * 0.5 + 0.5
    fine = fbm(h, w, 40, 2, seed + 9)
    knots = smoothstep(0.78, 0.9, fbm(h, w, 5, 2, seed + 20))
    hgt = grain * 0.55 + fine * 0.35 - knots * 0.4
    plank = h // 4
    joint = (np.abs((y % plank) - 0) < 3) | (np.abs((y % plank) - plank) < 3)
    hgt = np.where(joint, hgt - 1.2, hgt)
    return hgt, grain, knots


def make_wood_normal():
    hgt, _, _ = wood_height(512, 512, 5)
    save("Sakura_Wood_Normal.png", height_to_normal(hgt, 3.0))


# ------------------------------------------------------------------ trim sheet
def make_trim():
    W = H = 2048
    col = np.zeros((H, W, 3), np.float32)
    hgt = np.zeros((H, W), np.float32)

    # W: weathered cedar planks, bottom band
    wh, grain, knots = wood_height(512, W, 11)
    base = np.array([0.46, 0.33, 0.22])
    wood = base[None, None, :] * (0.78 + 0.32 * grain[..., None]) * (1 - 0.35 * knots[..., None])
    wood *= (0.9 + 0.2 * fbm(512, W, 6, 3, 31))[..., None]
    col[1536:] = wood
    hgt[1536:] = wh

    # G: galvanised post steel
    g = fbm(1488, 512, 10, 4, 41)
    col[0:1488, 1536:] = (np.array([0.56, 0.57, 0.58]) * (0.85 + 0.25 * g[..., None]))
    hgt[0:1488, 1536:] = g * 0.2

    # S: summit board - cream enamel, dark border, original lettering
    grime = fbm(512, 1536, 4, 4, 51)
    enamel = np.array([0.93, 0.90, 0.82]) * (0.92 + 0.08 * grime[..., None])
    col[0:512, 0:1536] = enamel
    s_img = Image.new("L", (1536, 512), 0)
    d = ImageDraw.Draw(s_img)
    d.rounded_rectangle((18, 18, 1517, 493), radius=28, outline=255, width=16)
    fj = ImageFont.truetype(FONT_JP, 250)
    fe = ImageFont.truetype(FONT_EN, 92)
    fs = ImageFont.truetype(FONT_JP, 64)
    d.text((768, 205), "桜峠", font=fj, fill=255, anchor="mm")
    d.text((768, 390), "SAKURA PASS", font=fe, fill=255, anchor="mm")
    d.text((140, 100), "頂上", font=fs, fill=255, anchor="mm")
    d.text((1396, 100), "SUMMIT", font=ImageFont.truetype(FONT_EN, 52), fill=255, anchor="mm")
    ink = np.asarray(s_img.filter(ImageFilter.GaussianBlur(0.8)), np.float32) / 255.0
    inkc = np.array([0.10, 0.16, 0.30])                      # deep indigo lettering
    col[0:512, 0:1536] = col[0:512, 0:1536] * (1 - ink[..., None]) + inkc * ink[..., None]
    hgt[0:512, 0:1536] = ink * 0.6 + grime * 0.05

    # C: chevron board - amber with black chevrons pointing +U (right)
    ch = 488
    c_img = Image.new("L", (1536, ch), 0)
    d = ImageDraw.Draw(c_img)
    for k in range(3):
        x0 = 150 + k * 450
        th = 120
        d.polygon([(x0, 50), (x0 + th, 50), (x0 + th + 190, ch // 2),
                   (x0 + th, ch - 50), (x0, ch - 50), (x0 + 190, ch // 2)], fill=255)
    cm = np.asarray(c_img.filter(ImageFilter.GaussianBlur(1.0)), np.float32) / 255.0
    amber = np.array([0.95, 0.74, 0.10]) * (0.9 + 0.1 * fbm(ch, 1536, 5, 3, 61)[..., None])
    col[512:1000, 0:1536] = amber * (1 - cm[..., None]) + np.array([0.05, 0.05, 0.06]) * cm[..., None]
    hgt[512:1000, 0:1536] = cm * 0.4

    # unused gutter: neutral grey
    col[1000:1536, 0:1536] = 0.5
    col[1488:1536, 1536:] = 0.5

    rgb = (np.clip(col, 0, 1) ** (1 / 1.0) * 255).astype(np.uint8)
    save("Sakura_Sign_Trim.png", rgb)
    save("Sakura_Sign_Trim_Normal.png", height_to_normal(hgt, 4.0))


# ------------------------------------------------------------------ decals
def ragged_alpha(h, w, edge01, seed, soft=0.08):
    """edge01: 0 inside -> 1 at the boundary; noisy threshold so the edge is torn, not ringed."""
    n = fbm(h, w, 6, 5, seed) * 0.55 + fbm(h, w, 24, 2, seed + 3) * 0.45
    return 1.0 - smoothstep(0.55 - soft, 0.55 + soft, edge01 + (n - 0.5) * 0.75)


def soil_colour(h, w, seed, moss=False):
    n = fbm(h, w, 8, 5, seed)
    grit = fbm(h, w, 64, 2, seed + 7)
    if moss:
        base = np.array([0.30, 0.36, 0.19])
        alt = np.array([0.40, 0.34, 0.22])
    else:
        base = np.array([0.40, 0.31, 0.22])
        alt = np.array([0.55, 0.50, 0.43])                    # gravel fleck
    mix = smoothstep(0.55, 0.8, grit)[..., None]
    c = base * (0.75 + 0.45 * n[..., None])
    return c * (1 - mix * 0.6) + alt * mix * 0.6


def make_edge_decal():
    h, w = 256, 1024
    v = np.linspace(0, 1, h, dtype=np.float32)[:, None] * np.ones((1, w), np.float32)
    a = ragged_alpha(h, w, v * 0.95 + 0.05, 71, soft=0.06)
    a = np.clip(a * (1.0 - 0.25 * smoothstep(0.0, 0.12, 0.12 - v)), 0, 1)
    c = soil_colour(h, w, 81)
    rgba = np.dstack([(c * 255).clip(0, 255), a * 255]).astype(np.uint8)
    save("Sakura_EdgeDirt_Decal.png", rgba, "RGBA")


def make_splat():
    S = 512
    out = np.zeros((1024, 1024, 4), np.uint8)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    for i in range(4):
        # Build a loose, asymmetric cluster instead of one radial island.  The old single
        # ellipse produced a recognisable stamp/ring once the card was viewed obliquely.
        # Uneven lobes plus small satellite bites make the silhouette read like soil that
        # has crept into the grass, rather than a decal stamped onto it.
        ang = rng.uniform(0, np.pi)
        ca, sa = np.cos(ang), np.sin(ang)
        cx, cy = S / 2 + rng.uniform(-24, 24), S / 2 + rng.uniform(-20, 20)
        edge = np.full((S, S), 9.0, np.float32)
        for lobe in range(4):
            lx = cx + (lobe - 1.5) * rng.uniform(34, 62) + rng.uniform(-24, 24)
            ly = cy + rng.uniform(-42, 42)
            rx0, ry0 = rng.uniform(S * 0.14, S * 0.27), rng.uniform(S * 0.10, S * 0.20)
            dx, dy = xx - lx, yy - ly
            ux, uy = dx * ca + dy * sa, -dx * sa + dy * ca
            radial = np.sqrt((ux / rx0) ** 2 + (uy / ry0) ** 2)
            lobe_warp = 1.0 + 0.16 * np.sin(np.arctan2(uy, ux) * (3 + lobe) + i)
            edge = np.minimum(edge, radial / lobe_warp)
        warp = (fbm(S, S, 6, 4, 420 + i) - 0.5) * 0.30
        edge = edge + warp
        # Keep the centre mostly opaque, then feather over a generous outer band.  The
        # broad transition is intentional: it gives the renderer alpha to blend into the
        # surrounding grass instead of collapsing to a hard circular cutoff.
        a = 1.0 - smoothstep(0.58, 1.18, edge)
        # Bites are restricted to the rim, while a quieter coarse modulation remains in
        # the core. This removes the repeated bullseye/ring read without making the patch
        # look like noisy camouflage.
        bite = fbm(S, S, 22, 3, 510 + i)
        rim = smoothstep(0.48, 0.98, edge)
        a *= 1.0 - rim * smoothstep(0.56, 0.76, bite) * 0.52
        core = fbm(S, S, 5, 2, 530 + i)
        a *= 0.90 + 0.10 * core
        a = np.clip(a, 0.0, 1.0)
        a[:6, :] = 0; a[-6:, :] = 0; a[:, :6] = 0; a[:, -6:] = 0
        c = soil_colour(S, S, 200 + i, moss=(i >= 2))
        # The last 35% of coverage picks up grass green. This is subtle in the solid
        # centre, but prevents the feather from reading as a grey/brown outline.
        grass = np.array([0.24, 0.34, 0.16], np.float32)
        fringe = 1.0 - smoothstep(0.08, 0.48, a)
        c = c * (1.0 - fringe[..., None] * 0.72) + grass * (fringe[..., None] * 0.72)
        y0, x0 = (i // 2) * S, (i % 2) * S
        out[y0:y0 + S, x0:x0 + S, :3] = (c * 255).clip(0, 255)
        out[y0:y0 + S, x0:x0 + S, 3] = (a * 255).clip(0, 255)
    save("Sakura_Ground_Splat.png", out, "RGBA")


def make_macro():
    n = fbm(256, 256, 3, 4, 301, gain=0.55)
    n = (n - n.min()) / (n.max() - n.min())
    g = (0.30 + 0.40 * n)                                    # centred at 0.5 => neutral overlay
    save("Sakura_Macro_Variation.png", (g * 255).astype(np.uint8), "L")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    make_wood_normal()
    make_trim()
    make_edge_decal()
    make_splat()
    make_macro()
    print("[sakura-tex] done")

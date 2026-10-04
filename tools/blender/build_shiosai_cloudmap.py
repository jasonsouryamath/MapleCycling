"""
Shiosai Coast cloud map (M6).

WHY THIS FILE EXISTS
--------------------
HDRP draws the sky from the Volume stack, not from RenderSettings.skybox, so the coast sky is
an HDRP GradientSky + CloudLayer (see ShiosaiCoastEnvironment.ConfigureCoastSkyVolume).
CloudLayer's *default* cloud map lives in the HDRP package's render-pipeline resources and
resolves to NULL in batchmode - verified in the Apply log: "cloudMapA=<null>" - so the clouds
silently rendered as nothing even with opacity 1.0. The coast therefore ships its own cloud
map, exactly like it ships its own rock textures.

FORMAT
------
Lat-long (equirectangular). CloudLayer reads ONE channel per layer; layerA is configured with
opacityR = 1, so the CUMULUS COVERAGE lives in the RED channel. G/B/A carry the same mask so
the texture is also readable as a plain greyscale preview.

Run with plain python (no bpy needed):
    python tools/blender/build_shiosai_cloudmap.py
"""

import os
import numpy as np
from PIL import Image

W, H = 2048, 1024
OUT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "Environment", "ShiosaiCoast", "Textures", "Shiosai_CloudMap.png")

# --- PROVISIONAL tuning -------------------------------------------------------------------
# References 03/10/14/18 are fair-weather cumulus: clearly separated puffs over roughly a
# third of the sky, not overcast. COVERAGE is the fbm threshold (higher = fewer clouds).
COVERAGE = 0.50
SOFTNESS = 0.16     # width of the smoothstep band; larger = softer, wispier edges
OCTAVES = 5
BASE_LATTICE = 22    # cells across the full 360 deg at octave 0
SEED = 20260915


def _lattice(rng, nx, ny):
    return rng.random((ny + 1, nx))          # wraps in x by construction


def _sample(grid, w, h):
    """Bilinear upsample of a wrapping lattice to (h, w), tileable in x."""
    ny, nx = grid.shape[0] - 1, grid.shape[1]
    xs = np.linspace(0.0, nx, w, endpoint=False)
    ys = np.linspace(0.0, ny, h, endpoint=False)
    x0 = np.floor(xs).astype(int) % nx
    x1 = (x0 + 1) % nx
    fx = (xs - np.floor(xs))[None, :]
    y0 = np.clip(np.floor(ys).astype(int), 0, ny - 1)
    y1 = y0 + 1
    fy = (ys - np.floor(ys))[:, None]
    # smootherstep so the octaves read as billows rather than diamonds
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)
    top = grid[y0][:, x0] * (1 - fx) + grid[y0][:, x1] * fx
    bot = grid[y1][:, x0] * (1 - fx) + grid[y1][:, x1] * fx
    return top * (1 - fy) + bot * fy


def main():
    rng = np.random.default_rng(SEED)
    fbm = np.zeros((H, W), dtype=np.float64)
    amp, total, nx = 1.0, 0.0, BASE_LATTICE
    for _ in range(OCTAVES):
        ny = max(2, nx // 2)
        fbm += amp * _sample(_lattice(rng, nx, ny), W, H)
        total += amp
        amp *= 0.5
        nx *= 2
    fbm /= total

    # Cumulus: threshold the noise, then soften the edge.
    t = np.clip((fbm - COVERAGE) / SOFTNESS, 0.0, 1.0)
    clouds = t * t * (3 - 2 * t)

    # Latitude shaping.
    #
    # THE GOLD HORIZON BAND. upperHemisphereOnly remaps this texture's V range onto the top half
    # of the sky, and with v = linspace(1, 0) row 0 is the ZENITH, so v -> 0 is the HORIZON. The
    # old shaping thinned only a 0.06-wide sliver at each end, which left full cloud coverage
    # running all the way down to v = 0. At the horizon the equirect mapping compresses an
    # enormous solid angle into a few pixels, so that coverage smeared into one continuous,
    # sun-lit bar wrapped around the whole skyline - the "solid tan/gold strip along the far
    # horizon" in diag_shiosai_ch8_highway.png, and the pale haze bar in the overlook shot. It
    # survived every other probe (ocean extent, camera far plane, GradientSky bottom, backdrop
    # headland tint) because it is painted on the sky dome at infinity, not by any of them.
    #
    # Real cumulus thin out and vanish well before the horizon, so the fade is now wide. The
    # zenith keeps its narrow sliver, which is only there to stop a lat-long map pinching into
    # a rosette at the pole.
    HORIZON_FADE = 0.30   # PROVISIONAL: fraction of V over which cloud dies out at the horizon
    ZENITH_FADE = 0.06    # PROVISIONAL: anti-rosette sliver at the pole only
    v = np.linspace(1.0, 0.0, H)[:, None]
    clouds *= np.clip(v / HORIZON_FADE, 0.0, 1.0)
    clouds *= np.clip((1.0 - v) / ZENITH_FADE, 0.0, 1.0)

    img = (np.clip(clouds, 0.0, 1.0) * 255.0).astype(np.uint8)
    rgba = np.dstack([img, img, img, np.full_like(img, 255)])
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    Image.fromarray(rgba, "RGBA").save(OUT)
    cover = float((clouds > 0.5).mean())
    print(f"[cloudmap] wrote {OUT} ({W}x{H}), sky coverage {cover*100:.1f}%")


if __name__ == "__main__":
    main()

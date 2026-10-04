"""
MAPLE CITY - boulevard asphalt texture generator.

WHY THIS EXISTS
---------------
The city road borrowed Shiosai_Asphalt_Albedo.png. That map is dark (mean ~0.165), slightly
BLUE (b > r), and carries large grunge blotches sized for a coastal B-road. On the boulevard
it needed a 1.7x tint to read at all, the blotches repeated every 4 m as muddy stains, and
under a blue sky ambient the blue bias pushed the sunlit road to lavender (life_chase_4300m,
2026-09-25 22:02 capture: sunlit road averaged RGB 85/79/109).

This map is a city surface instead: warm-neutral mid-dark grey, fine aggregate, a few light
chips, and faint wheel-track polish + lane-centre oil drip bands aligned to the carriageway.
MapleRideCelLit has no normal input, so all the texture has to live in the albedo.

UV contract (MapleCityEnvironment.SweptCarriageway): u = lateral offset / 4 m, v = distance / 4 m,
lateral offset 0 = the centre line. So in tile space u = frac(o / 4).

Output (tiling, 2048 x 2048 = 4 x 4 m):
    Assets/Environment/MapleCity/Textures/MapleCity_Asphalt_Albedo.png

All tuning below is PROVISIONAL and named.
"""

import os
import numpy as np
from PIL import Image

RES = 2048
TILE_METRES = 4.0
SEED = 20260925

BASE = np.array([0.264, 0.261, 0.252])  # slightly warm: the grade lift is cool (RegionDirector)
MACRO_STRENGTH = 0.018                   # large soft variation - kept LOW (the old map's flaw)
MID_STRENGTH = 0.028
FINE_STRENGTH = 0.07                     # aggregate grain
CHIP_DENSITY = 0.012                     # fraction of pixels seeding a light stone chip
CHIP_LIGHT = 0.22
PIT_DENSITY = 0.006
PIT_DARK = 0.20
# Wheel paths sit ~0.8 m either side of each lane centre; lanes are centred ~2.3 m off the
# centre line. Offsets in metres from the centre line (both signs).
WHEEL_TRACKS_M = (1.5, 3.1)
TRACK_WIDTH_M = 0.35
TRACK_POLISH = 0.045                     # tyres polish the binder: slightly lighter
LANE_CENTRE_M = 2.3
OIL_WIDTH_M = 0.45
OIL_DARK = 0.05

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "Environment", "MapleCity", "Textures")


def periodic_noise(rng, res, cutoff, power=1.0):
    """Tileable band-limited noise via the FFT (periodic by construction). cutoff in cycles/tile."""
    f = np.fft.fftfreq(res) * res
    fx, fy = np.meshgrid(f, f)
    r = np.sqrt(fx * fx + fy * fy)
    amp = np.where(r > 0, 1.0 / np.maximum(r, 1.0) ** power, 0.0) * np.exp(-(r / cutoff) ** 2)
    phase = rng.random((res, res)) * 2 * np.pi
    field = np.real(np.fft.ifft2(amp * np.exp(1j * phase)))
    field -= field.mean()
    return field / (field.std() + 1e-9)


def band(u_m, centre_m, width_m):
    """Soft band at +/- centre (metres) on a 4 m periodic lateral axis."""
    out = np.zeros_like(u_m)
    for c in (centre_m, -centre_m):
        d = np.mod(u_m - c + TILE_METRES / 2, TILE_METRES) - TILE_METRES / 2
        out += np.exp(-(d / width_m) ** 2)
    return out


def build():
    rng = np.random.default_rng(SEED)
    macro = periodic_noise(rng, RES, cutoff=6, power=1.2)
    mid = periodic_noise(rng, RES, cutoff=60, power=0.6)
    fine = rng.normal(0, 1, (RES, RES))
    fine = (fine + np.roll(fine, 1, 0) + np.roll(fine, 1, 1) + np.roll(fine, 1, (0, 1))) / 2.0

    # Aggregate chips: sparse seeds dilated to 2-4 px stones.
    seeds = rng.random((RES, RES))
    chips = (seeds < CHIP_DENSITY).astype(np.float64)
    chips = np.maximum.reduce([chips, np.roll(chips, 1, 0), np.roll(chips, 1, 1),
                               np.roll(chips, (1, 1), (0, 1))])
    chips *= 0.5 + 0.5 * rng.random((RES, RES))
    pits = (rng.random((RES, RES)) < PIT_DENSITY).astype(np.float64)
    pits = np.maximum(pits, np.roll(pits, 1, 1))

    u_m = (np.arange(RES) / RES * TILE_METRES)[None, :].repeat(RES, 0)
    tracks = sum(band(u_m, c, TRACK_WIDTH_M) for c in WHEEL_TRACKS_M)
    oil = band(u_m, LANE_CENTRE_M, OIL_WIDTH_M)
    # Break the bands up along the road so they read as wear, not stripes.
    wear_mod = 0.6 + 0.4 * (periodic_noise(rng, RES, cutoff=10) * 0.5 + 0.5)

    light = (1.0 + macro * MACRO_STRENGTH + mid * MID_STRENGTH + fine * FINE_STRENGTH
             + chips * CHIP_LIGHT - pits * PIT_DARK
             + tracks * TRACK_POLISH * wear_mod - oil * OIL_DARK * wear_mod)
    albedo = np.clip(BASE[None, None, :] * light[:, :, None], 0, 1)
    # Chips are slightly warm (granite/limestone aggregate).
    albedo[..., 0] += chips * 0.012
    albedo = np.clip(albedo, 0, 1)

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, "MapleCity_Asphalt_Albedo.png")
    Image.fromarray((albedo * 255).astype(np.uint8)).save(path)
    print(f"[asphalt] albedo mean {albedo.reshape(-1, 3).mean(axis=0)}")
    print(f"[asphalt] wrote {path}")


if __name__ == "__main__":
    build()

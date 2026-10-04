"""
Nagisa Bay positional ambience clips (worker H, 2026-10-01). Original, procedural, numpy/scipy only.

Renders mono 32 kHz clips into Assets/Resources/Audio/Nagisa/Amb/. LOOPS are exactly periodic (loop-safe
noise + wrapped events); ONE-SHOTS fade in/out. Runtime: Assets/Ride/Audio/NagisaAmbienceDirector.cs
places them as 3D sources at the route's landmarks, so nothing here is a global bed.

Run: python tools/audio/build_nagisa_ambience.py [name ...]
"""
import sys, os
import numpy as np
from scipy import signal
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nagisa_dsp as D
from nagisa_dsp import SR, lp, hp, bp, circ, pnoise, wadd, normalize, fade, write_wav

SUB = "Amb"
LOOP_RMS = 0.07
CLIPS = {}


def clip(fn):
    CLIPS[fn.__name__] = fn
    return fn


def T(n): return np.arange(n) / SR


def adsr_sin(n): return np.sin(np.linspace(0, np.pi, n)) ** 0.8


# ---------------------------------------------------------------- ocean
def _wave_event(n):
    """One breaking wave: rising roar, crash transient, long fizzing backwash."""
    t = T(n)
    rise = np.clip(t / 1.8, 0, 1) ** 2
    fall = np.exp(-np.clip(t - 1.8, 0, None) / 2.4)
    env = rise * fall
    roar = lp(D.RNG.normal(size=n), 2200) * env
    fizz = bp(D.RNG.normal(size=n), 3000, 9500) * (env ** 2) * 0.55
    crash = lp(D.RNG.normal(size=n), 1400) * np.exp(-np.clip(t - 1.7, 0, None) * 4.5) * (t > 1.7) * 0.9
    return roar + fizz + crash


@clip
def surf_loop():
    n = 24 * SR
    out = 0.35 * circ(lambda x: lp(x, 700), pnoise(n, "brown"))
    for k in range(7):
        ev = _wave_event(int(7.5 * SR))
        wadd(out, int((k + D.RNG.uniform(0.05, 0.85)) / 7 * n), ev, D.RNG.uniform(0.6, 1.0))
    return normalize(out, rms=LOOP_RMS)


@clip
def distant_breakers():
    n = 30 * SR
    out = circ(lambda x: lp(x, 380), pnoise(n, "brown")) * 0.6
    for k in range(5):
        t = T(int(9 * SR))
        env = np.clip(t / 3.5, 0, 1) ** 1.5 * np.exp(-np.clip(t - 3.5, 0, None) / 2.8)
        ev = lp(D.RNG.normal(size=len(t)), 900) * env + bp(D.RNG.normal(size=len(t)), 900, 2500) * env * 0.25
        wadd(out, int((k + D.RNG.uniform(0, 0.8)) / 5 * n), ev, D.RNG.uniform(0.7, 1.0))
    return normalize(out, rms=LOOP_RMS)


def _mk_seawall(i):
    def f():
        n = int(2.6 * SR); t = T(n)
        fr = 62 - 22 * np.clip(t / 0.3, 0, 1)
        body = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t * (7.5 + i))
        slap = lp(D.RNG.normal(size=n), 1100 + 200 * i) * np.exp(-t * 9) * 0.9
        spray = hp(lp(D.RNG.normal(size=n), 7500), 2600) * np.exp(-t * (2.2 + 0.4 * i)) * (0.35 + 0.1 * i)
        crackle = (D.RNG.random(n) < 0.012) * D.RNG.normal(size=n) * np.exp(-t * 3) * 0.6
        return normalize(fade(body * 1.1 + slap + spray + hp(crackle, 2000), 0.002, 0.25), 0.85)
    return f


for _i in range(4):
    CLIPS[f"seawall_hit_{_i}"] = _mk_seawall(_i)


@clip
def marina_lap():
    n = 24 * SR
    base = circ(lambda x: bp(x, 120, 900), pnoise(n, "pink"))
    mod = 0.45 + 0.55 * D.smooth_env(n, [3, 5, 8, 13], [1, 0.7, 0.5, 0.3])
    out = base * mod * 0.6
    for _ in range(95):                                  # little laps / gloops
        f0 = D.RNG.uniform(260, 620); dur = D.RNG.uniform(0.07, 0.16)
        m = int(dur * SR + 0.2 * SR); t = T(m)
        fr = f0 * (1 + 0.8 * np.clip(t / dur, 0, 1))
        g = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t * 14)
        g += bp(D.RNG.normal(size=m), 500, 2400) * np.exp(-t * 22) * 0.5
        wadd(out, D.RNG.integers(0, n), g, D.RNG.uniform(0.05, 0.22))
    for _ in range(5):                                    # hull knock against the pontoon
        m = int(0.5 * SR); t = T(m)
        g = np.sin(2 * np.pi * 150 * t) * np.exp(-t * 11) + 0.4 * np.sin(2 * np.pi * 310 * t) * np.exp(-t * 16)
        wadd(out, D.RNG.integers(0, n), g, D.RNG.uniform(0.08, 0.16))
    return normalize(out, rms=LOOP_RMS)


# ---------------------------------------------------------------- voices (distant crowds)
VOWELS = [(730, 1090), (660, 1720), (270, 2290), (570, 840), (300, 870), (530, 1840), (400, 1900)]


def _voiced(f0_curve, dur, formants, breath=0.06, vib=5.0):
    n = int(dur * SR); t = T(n)
    f0 = np.interp(t, np.linspace(0, dur, len(f0_curve)), f0_curve) * (1 + 0.012 * np.sin(2 * np.pi * vib * t))
    ph = np.cumsum(f0) / SR
    src = signal.sawtooth(2 * np.pi * ph) * 0.6 + D.RNG.normal(size=n) * breath
    y = np.zeros(n)
    for (fc, bw, g) in formants:
        y += g * bp(src, max(fc - bw, 80), fc + bw, 2)
    return y


def _babble(n, talkers, level, kids=0, lp_hz=3200):
    out = np.zeros(n)
    for k in range(talkers + kids):
        child = k >= talkers
        f_base = D.RNG.uniform(330, 470) if child else (D.RNG.uniform(105, 150) if k % 2 == 0 else D.RNG.uniform(190, 260))
        pos = D.RNG.uniform(0, 1.2)
        while pos < n / SR + 0.4:
            for _ in range(D.RNG.integers(3, 8)):            # a short phrase, then a pause
                dur = D.RNG.uniform(0.09, 0.24)
                v1, v2 = VOWELS[D.RNG.integers(len(VOWELS))]
                f0 = f_base * D.RNG.uniform(0.85, 1.25)
                curve = [f0 * D.RNG.uniform(0.9, 1.1), f0 * D.RNG.uniform(0.85, 1.2), f0 * D.RNG.uniform(0.8, 1.05)]
                sc = 1.18 if child else 1.0
                s = _voiced(curve, dur, [(v1 * sc, 90, 1.0), (v2 * sc, 140, 0.55), (2800, 300, 0.12)])
                s *= np.sin(np.pi * np.clip(T(len(s)) / dur, 0, 1)) ** 1.5
                wadd(out, int(pos * SR), s, D.RNG.uniform(0.5, 1.0))
                pos += dur * D.RNG.uniform(0.9, 1.4)
            pos += D.RNG.uniform(0.5, 2.6)
    out = circ(lambda x: lp(x, lp_hz, 2), out)
    out = circ(lambda x: hp(x, 160, 1), out)
    return out * level / (np.abs(out).max() + 1e-9)


@clip
def beach_crowd():
    n = 28 * SR
    out = _babble(n, 12, 0.7, kids=4, lp_hz=2800)
    out += 0.25 * circ(lambda x: lp(x, 900), pnoise(n, "brown"))      # air / distance wash
    for _ in range(4):                                                  # a distant whoop
        v = _voiced([420, 560, 470], D.RNG.uniform(0.35, 0.6), [(850, 100, 1), (1500, 150, 0.4)])
        v = lp(v, 3000) * np.sin(np.pi * np.clip(T(len(v)) / (len(v) / SR), 0, 1))
        wadd(out, D.RNG.integers(0, n), v, 0.25)
    return normalize(out, rms=LOOP_RMS)


def _laugh(idx, child):
    pulses = [5, 6, 7][idx % 3] + 1
    f_start, f_end = (430, 340) if child else ((290, 190) if idx % 2 == 0 else (220, 140))
    n = int(1.9 * SR); out = np.zeros(n); pos = 0.05
    for p in range(pulses):
        dur = 0.11
        fr = np.interp(p, [0, pulses - 1], [f_start, f_end])
        s = _voiced([fr * 1.08, fr, fr * 0.92], dur, [(820, 110, 1), (1250, 160, 0.55), (2600, 300, 0.15)], breath=0.18, vib=0)
        s *= np.sin(np.pi * np.clip(T(len(s)) / dur, 0, 1)) ** 1.2
        a = int(pos * SR)
        out[a:a + len(s)] += s[:max(0, n - a)] * (1.0 - 0.07 * p)
        pos += 0.155 + 0.01 * p
    return normalize(fade(lp(out, 3600), 0.01, 0.2), 0.7)


def _mk_laugh(i):
    def f(): return _laugh(i, child=(i == 2))
    return f


for _i in range(3):
    CLIPS[f"laugh_{_i}"] = _mk_laugh(_i)


@clip
def child_shout():
    s = _voiced([380, 520, 600, 470], 0.95, [(900, 110, 1), (1800, 200, 0.5), (3100, 300, 0.12)], breath=0.07, vib=6)
    s *= adsr_sin(len(s))
    return normalize(fade(lp(s, 4200), 0.02, 0.3), 0.7)


# ---------------------------------------------------------------- cycling
def _tyre_hum(n, level=1.0):
    return level * (bp(D.RNG.normal(size=n), 700, 2400) * 0.5 + bp(D.RNG.normal(size=n), 120, 400) * 0.25)


def _ticks(n, rate, jitter=0.12, gain=1.0):
    out = np.zeros(n); pos = 0.0
    while pos < n / SR:
        m = int(0.02 * SR); t = T(m)
        tick = bp(D.RNG.normal(size=m), 2200, 6500) * np.exp(-t * 520) + np.sin(2 * np.pi * 3100 * t) * np.exp(-t * 400) * 0.3
        a = int(pos * SR)
        if a + m <= n: out[a:a + m] += tick * gain * D.RNG.uniform(0.7, 1.0)
        pos += (1.0 / rate) * (1 + D.RNG.uniform(-jitter, jitter))
    return out


@clip
def freehub_pass_a():
    n = int(4.2 * SR); out = _ticks(n, 58, gain=0.9) + _tyre_hum(n, 0.35)
    return normalize(fade(out * adsr_sin(n) ** 0.5, 0.25, 0.4), 0.75)


@clip
def drivetrain_pass():
    n = int(4.0 * SR); t = T(n)
    chain = _ticks(n, 21, jitter=0.03, gain=0.5)
    whine = np.sin(2 * np.pi * 980 * t) * 0.05 + np.sin(2 * np.pi * 1960 * t) * 0.02
    whir = bp(D.RNG.normal(size=n), 1500, 4200) * (0.5 + 0.5 * np.sin(2 * np.pi * 1.5 * t)) * 0.12
    return normalize(fade((chain + whine + whir + _tyre_hum(n, 0.3)) * adsr_sin(n) ** 0.5, 0.25, 0.4), 0.7)


@clip
def peloton_pass():
    n = int(6.0 * SR); out = _tyre_hum(n, 0.9)
    for r in (51, 57, 62, 66):
        out += _ticks(n, r, gain=0.45)
    out += _ticks(n, 21, gain=0.4)
    return normalize(fade(out * adsr_sin(n) ** 0.6, 0.4, 0.6), 0.75)


@clip
def bike_bell():
    n = int(1.8 * SR); out = np.zeros(n)
    for st in (0.0, 0.32):
        a = int(st * SR); tt = T(n - a)
        for fr, g, dk in ((2650, 1.0, 5.0), (4000, 0.6, 7), (6900, 0.3, 9)):
            out[a:] += np.sin(2 * np.pi * fr * tt) * np.exp(-tt * dk) * g
    return normalize(fade(out, 0.001, 0.2), 0.6)


def _mk_clack(i):
    def f():
        n = int(0.9 * SR); t = T(n)
        thunk = np.sin(2 * np.pi * (120 + 25 * i) * t) * np.exp(-t * 28)
        click = bp(D.RNG.normal(size=n), 1500, 6000) * np.exp(-t * 90) * 0.7
        ping = np.sin(2 * np.pi * (2200 + 300 * i) * t) * np.exp(-t * 26) * 0.25
        return normalize(fade(thunk + click + ping, 0.0005, 0.1), 0.7)
    return f


for _i in range(3):
    CLIPS[f"bike_clack_{_i}"] = _mk_clack(_i)


# ---------------------------------------------------------------- traffic
@clip
def traffic_far():
    n = 28 * SR
    out = 0.7 * circ(lambda x: lp(x, 260), pnoise(n, "brown"))
    out += 0.12 * circ(lambda x: bp(x, 500, 1600), pnoise(n, "pink")) * (0.4 + 0.6 * D.smooth_env(n, [4, 7, 11], [1, .6, .4]))
    for _ in range(9):                                     # a car swishing by far away
        dur = D.RNG.uniform(2.5, 4.5); m = int(dur * SR); t = T(m)
        env = np.sin(np.pi * t / dur) ** 2
        w = bp(D.RNG.normal(size=m), 350, 1500) * env + lp(D.RNG.normal(size=m), 200) * env * 0.6
        wadd(out, D.RNG.integers(0, n), w, D.RNG.uniform(0.25, 0.6))
    return normalize(out, rms=LOOP_RMS)


def _vehicle(kind, dur=5.0):
    n = int(dur * SR); t = T(n)
    if kind == "car":
        f0 = 78; harm = sum(np.sin(2 * np.pi * f0 * k * t + k) / k ** 1.2 for k in range(1, 12))
        eng = lp(harm, 650) * (0.6 + 0.4 * np.sin(2 * np.pi * 3 * t))
        out = eng * 0.8 + bp(D.RNG.normal(size=n), 500, 2600) * 0.55
    elif kind == "luxury":
        f0 = 96; harm = sum(np.sin(2 * np.pi * f0 * k * t) / k ** 1.6 for k in range(1, 8))
        out = lp(harm, 520) * 0.7 + bp(D.RNG.normal(size=n), 600, 2400) * 0.38 + np.sin(2 * np.pi * 820 * t) * 0.015
    elif kind == "scooter":
        f0 = 165 + 6 * np.sin(2 * np.pi * 0.7 * t)
        saw = signal.sawtooth(2 * np.pi * np.cumsum(f0) / SR) * (0.65 + 0.35 * np.sign(np.sin(2 * np.pi * 82 * t)))
        out = bp(saw, 150, 2600) * 0.7 + bp(D.RNG.normal(size=n), 800, 3500) * 0.25
    else:  # bus: lumpy diesel + tyre/air
        f0 = 44; pulses = 0.55 + 0.45 * np.sin(2 * np.pi * 15 * t) ** 2
        harm = sum(np.sin(2 * np.pi * f0 * k * t + 2 * k) / k for k in range(1, 14))
        out = lp(harm * pulses, 480) + bp(D.RNG.normal(size=n), 400, 1800) * 0.4 + lp(D.RNG.normal(size=n), 250) * 0.5
    return normalize(fade(out * np.sin(np.pi * t / dur) ** 0.7, 0.3, 0.5), 0.75)


def _mk_veh(k):
    def f(): return _vehicle(k, 6.5 if k == "bus" else 5.0)
    return f


for _k in ("car", "luxury", "scooter", "bus"):
    CLIPS[f"veh_{_k}"] = _mk_veh(_k)


# ---------------------------------------------------------------- marina
@clip
def marina_engine():
    n = 8 * SR; out = np.zeros(n); rate = 11
    for k in range(8 * rate):
        m = int(0.12 * SR); t = T(m)
        p = (np.sin(2 * np.pi * 55 * t) * 0.8 + lp(D.RNG.normal(size=m), 380) * 0.8) * np.exp(-t * 26)
        wadd(out, int(k / rate * SR) + D.RNG.integers(-60, 60), p, D.RNG.uniform(0.75, 1.0))
    out += 0.35 * circ(lambda x: bp(x, 150, 700), pnoise(n, "pink"))
    out = circ(lambda x: lp(x, 900), out)
    return normalize(out, rms=LOOP_RMS)


@clip
def marina_outboard():
    n = 8 * SR; t = T(n); rate = 27
    ph = 2 * np.pi * (rate * t + 0.15 * np.sin(2 * np.pi * 0.5 * t))
    buzz = signal.square(ph, 0.35) * 0.6 + circ(lambda x: lp(x, 2000), pnoise(n)) * 0.4
    out = circ(lambda x: lp(x, 1500), buzz) + 0.4 * circ(lambda x: bp(x, 200, 900), pnoise(n))
    return normalize(out, rms=LOOP_RMS)


@clip
def rigging():
    n = 28 * SR
    out = 0.25 * circ(lambda x: lp(x, 500), pnoise(n, "brown"))
    for _ in range(26):                                    # halyards pinging on masts
        base = D.RNG.uniform(1700, 4200)
        for burst in range(D.RNG.integers(1, 5)):
            m = int(0.5 * SR); t = T(m)
            ping = sum(np.sin(2 * np.pi * base * r * t) * np.exp(-t * (14 + 6 * i)) / (1 + i)
                       for i, r in enumerate((1.0, 2.76, 5.4)))
            wadd(out, D.RNG.integers(0, n) + burst * int(D.RNG.uniform(0.06, 0.14) * SR), ping, D.RNG.uniform(0.04, 0.16))
    for _ in range(4):                                     # rope / fender creak
        m = int(1.1 * SR); t = T(m)
        fr = 180 + 90 * np.sin(2 * np.pi * 1.3 * t) + 40 * t
        cr = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.sin(np.pi * t / 1.1) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 37 * t)))
        wadd(out, D.RNG.integers(0, n), lp(cr, 1400), 0.05)
    return normalize(out, rms=LOOP_RMS)


def _horn(f0s, hold, n_sec):
    n = int(n_sec * SR); t = T(n); out = np.zeros(n)
    for f0 in f0s:
        w = sum(np.sin(2 * np.pi * f0 * k * t + 0.3 * k) / k for k in range(1, 18))
        out += lp(w, 1500)
    env = D.adsr(n, 0.14, 0.05, 0.9, 0.9, hold)
    return out * env * (1 + 0.02 * np.sin(2 * np.pi * 6 * t))


@clip
def ship_horn():
    return normalize(_horn((131, 165), 2.2, 3.6), 0.8)


@clip
def yacht_toot():
    a = _horn((233, 294), 0.7, 1.4); out = np.zeros(int(3.0 * SR))
    out[:len(a)] += a; out[int(1.4 * SR):int(1.4 * SR) + len(a)] += a
    return normalize(out, 0.7)


# ---------------------------------------------------------------- birds / nature
def _gull(dur, f_lo, f_hi, calls=1, gap=0.0, rasp=0.25):
    n = int((dur + gap * (calls - 1)) * SR + 0.2 * SR); out = np.zeros(n)
    for c in range(calls):
        m = int(dur * SR); t = T(m); u = t / dur
        f = f_lo + (f_hi - f_lo) * np.sin(np.pi * u) ** 0.8 + 70 * np.sin(2 * np.pi * 14 * t)
        ph = 2 * np.pi * np.cumsum(f) / SR
        y = np.sin(ph) + 0.55 * np.sin(2 * ph) + 0.3 * np.sin(3 * ph)
        y *= (1 + rasp * np.sin(2 * np.pi * 90 * t)) * np.sin(np.pi * u) ** 0.6
        a = int(c * gap * SR)
        out[a:a + m] += y
    return out


@clip
def gull_cry():
    return normalize(fade(bp(_gull(1.3, 1350, 2300, rasp=0.3), 700, 6500), 0.01, 0.2), 0.7)


@clip
def gull_short():
    return normalize(fade(bp(_gull(0.28, 1500, 2100, calls=4, gap=0.34), 700, 6500), 0.01, 0.15), 0.7)


@clip
def seabird_tern():
    return normalize(fade(bp(_gull(0.11, 3200, 4200, calls=6, gap=0.17, rasp=0.1), 1500, 8000), 0.005, 0.1), 0.6)


@clip
def seabird_distant():
    return normalize(fade(lp(_gull(1.5, 1200, 1900, rasp=0.2), 2600), 0.15, 0.4), 0.45)


def _wind(n, lo, hi, gust, rustle, whistle=False):
    base = circ(lambda x: bp(x, lo, hi), pnoise(n, "pink"))
    g = 0.25 + 0.75 * D.smooth_env(n, [2, 3, 5, 8], gust)
    out = base * g
    rs = circ(lambda x: bp(x, 2200, 6500), pnoise(n)) * (0.2 + 0.8 * D.smooth_env(n, [5, 9, 14, 21], [1, .8, .6, .4])) ** 2.2
    out = out + rs * rustle
    if whistle:
        out += circ(lambda x: bp(x, 600, 900), pnoise(n)) * 0.12 * g ** 2
    return out


@clip
def wind_loop():
    return normalize(_wind(28 * SR, 140, 1100, [1, .7, .5, .3], 0.22), rms=LOOP_RMS)


@clip
def wind_ridge():
    return normalize(_wind(28 * SR, 90, 1400, [1, .9, .6, .4], 0.14, whistle=True), rms=LOOP_RMS * 1.15)


@clip
def wind_sea():
    return normalize(_wind(28 * SR, 80, 700, [1, .5, .3, .2], 0.05), rms=LOOP_RMS)


def _cicada(n, voices, hi=False):
    out = np.zeros(n); t = T(n)
    for v in range(voices):
        fc = D.RNG.uniform(5600, 7600) if hi else D.RNG.uniform(3700, 5200)
        pulse = D.RNG.integers(70, 110)
        carrier = np.sin(2 * np.pi * fc * t) * (0.55 + 0.45 * np.sign(np.sin(2 * np.pi * pulse * t)) ** 2)
        carrier += 0.5 * bp(D.RNG.normal(size=n), fc * 0.7, fc * 1.4)
        env = np.zeros(n); pos = D.RNG.uniform(0, 4)
        while pos < n / SR:
            dur = D.RNG.uniform(2.5, 5.5); m = int(dur * SR)
            wadd(env, int(pos * SR), np.sin(np.pi * np.linspace(0, 1, m)) ** 0.5)
            pos += dur + D.RNG.uniform(0.8, 3.5)
        out += carrier * np.minimum(env, 1.0) * D.RNG.uniform(0.5, 1.0)
    return out


@clip
def cicadas():
    n = 24 * SR
    out = _cicada(n, 5)
    out += 0.4 * circ(lambda x: bp(x, 5000, 9000), pnoise(n)) * (0.4 + 0.6 * D.smooth_env(n, [11, 17, 29], [1, .7, .5]))
    return normalize(out, rms=LOOP_RMS * 0.9)


# ---------------------------------------------------------------- resort
def _clinks(out, n, count, level):
    for _ in range(count):
        m = int(0.4 * SR); t = T(m); f = D.RNG.uniform(2600, 4600)
        s = (np.sin(2 * np.pi * f * t) * np.exp(-t * 55) + 0.5 * np.sin(2 * np.pi * f * 1.62 * t) * np.exp(-t * 80)
             + 0.25 * np.sin(2 * np.pi * f * 2.9 * t) * np.exp(-t * 110))
        wadd(out, D.RNG.integers(0, n), s, level * D.RNG.uniform(0.3, 1.0))


@clip
def cafe_loop():
    n = 28 * SR
    out = _babble(n, 8, 0.55, kids=1, lp_hz=2600)
    _clinks(out, n, 36, 0.14)
    for _ in range(3):                                      # espresso steam
        m = int(2.2 * SR); t = T(m)
        wadd(out, D.RNG.integers(0, n), hp(D.RNG.normal(size=m), 3500) * np.sin(np.pi * t / 2.2) ** 1.5, 0.08)
    out += 0.12 * circ(lambda x: lp(x, 700), pnoise(n, "brown"))
    return normalize(out, rms=LOOP_RMS)


@clip
def plaza_loop():
    n = 28 * SR
    out = _babble(n, 9, 0.5, kids=3, lp_hz=2800)
    water = circ(lambda x: bp(x, 700, 7000), pnoise(n, "white")) * (0.55 + 0.45 * D.smooth_env(n, [40, 63, 97], [1, .8, .6]))
    out += 0.35 * water
    for _ in range(30):                                     # footsteps on stone
        m = int(0.05 * SR); t = T(m)
        wadd(out, D.RNG.integers(0, n), lp(D.RNG.normal(size=m), 1800) * np.exp(-t * 90), 0.05)
    return normalize(out, rms=LOOP_RMS)


@clip
def bike_park_loop():
    """Cycling cafe forecourt: soft chatter, cleat clicks, freehub ticks of bikes being wheeled, chain rattle."""
    n = 24 * SR
    out = _babble(n, 5, 0.3, lp_hz=2400)
    for _ in range(16):
        m = int(1.3 * SR); r = _ticks(m, D.RNG.uniform(18, 40), gain=0.4)
        wadd(out, D.RNG.integers(0, n), r * np.sin(np.pi * T(m) / 1.3), D.RNG.uniform(0.15, 0.3))
    for _ in range(18):
        m = int(0.15 * SR); t = T(m)
        s = bp(D.RNG.normal(size=m), 1800, 6500) * np.exp(-t * 60) + np.sin(2 * np.pi * 2700 * t) * np.exp(-t * 90) * 0.4
        wadd(out, D.RNG.integers(0, n), s, D.RNG.uniform(0.05, 0.12))
    _clinks(out, n, 14, 0.10)
    return normalize(out, rms=LOOP_RMS)


def render(names):
    for i, name in enumerate(names):
        D.seed(20261001 + 31 * i + sum(map(ord, name)))
        write_wav(name, CLIPS[name](), SUB)


if __name__ == "__main__":
    render(sys.argv[1:] or list(CLIPS))

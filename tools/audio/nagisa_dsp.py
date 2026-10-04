"""Shared DSP helpers for the Nagisa Bay audio generators (worker H, 2026-10-01). numpy/scipy only.
All Nagisa audio renders at SR = 32000 Hz and is written under Assets/Resources/Audio/Nagisa/."""
import os, wave
import numpy as np
from scipy import signal

SR = 32000
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Audio", "Nagisa")
RNG = np.random.default_rng(20261001)


def seed(s):
    global RNG
    RNG = np.random.default_rng(s)
    return RNG


def midi(n):
    return 440.0 * 2.0 ** ((np.asarray(n, dtype=float) - 69) / 12.0)


def _sos(kind, fc, order):
    ny = SR / 2
    if kind == "band":
        lo, hi = fc
        return signal.butter(order, [max(lo, 5) / ny, min(hi, ny * 0.98) / ny], "band", output="sos")
    return signal.butter(order, min(max(fc, 5), ny * 0.98) / ny, kind, output="sos")


def lp(x, fc, order=2): return signal.sosfilt(_sos("low", fc, order), x)
def hp(x, fc, order=2): return signal.sosfilt(_sos("high", fc, order), x)
def bp(x, lo, hi, order=2): return signal.sosfilt(_sos("band", (lo, hi), order), x)


def circ(fn, x):
    """Apply a causal filter to a loop without a seam (filter 3 copies, keep the middle)."""
    n = len(x)
    return fn(np.concatenate([x, x, x]))[n:2 * n]


def noise(n, color="white"):
    return RNG.normal(size=n)


def pnoise(n, color="pink"):
    """Periodic noise of exactly n samples (FFT-shaped) so it loops without a seam."""
    spec = RNG.normal(size=n // 2 + 1) + 1j * RNG.normal(size=n // 2 + 1)
    f = np.fft.rfftfreq(n, 1 / SR); f[0] = f[1]
    if color == "pink": spec /= np.sqrt(f)
    elif color == "brown": spec /= f
    x = np.fft.irfft(spec, n)
    return x / (np.abs(x).max() + 1e-9)


def wadd(buf, start, sig, gain=1.0):
    """Mix mono `sig` into `buf` at sample `start`, wrapping around the end (loop-safe tails)."""
    n = len(buf); L = len(sig); start = int(start) % n
    while L > 0:
        m = min(L, n - start)
        buf[start:start + m] += sig[:m] * gain
        sig = sig[m:]; L -= m; start = 0


def sadd(buf, start, sig, gain=1.0, pan=0.0):
    """Mix mono `sig` into stereo `buf` (2, n) with constant-power pan, wrapping at the end."""
    th = (pan + 1) * np.pi / 4
    wadd(buf[0], start, sig, gain * np.cos(th)); wadd(buf[1], start, sig, gain * np.sin(th))


def smooth_env(n, cycles_list, amps, phases=None):
    """Periodic slow envelope from integer-cycle sinusoids (loop-safe). Returns 0..1."""
    t = np.arange(n) / n
    e = np.zeros(n)
    for i, (c, a) in enumerate(zip(cycles_list, amps)):
        ph = RNG.uniform(0, 2 * np.pi) if phases is None else phases[i]
        e += a * np.sin(2 * np.pi * c * t + ph)
    e -= e.min(); e /= (e.max() + 1e-9)
    return e


def adsr(n, a, d, s, r, hold):
    out = np.zeros(n)
    an, dn, rn = int(a * SR), int(d * SR), int(r * SR)
    sn = max(0, int(hold * SR) - an - dn)
    e = np.concatenate([np.linspace(0, 1, max(an, 1), endpoint=False), np.linspace(1, s, max(dn, 1), endpoint=False),
                        np.full(sn, s), np.linspace(s, 0, max(rn, 1))])[:n]
    out[:len(e)] = e
    return out


def fade(x, a=0.05, b=0.05):
    n = len(x)
    e = np.ones(n)
    an, bn = int(a * SR), int(b * SR)
    if an: e[:an] = np.sin(np.linspace(0, np.pi / 2, an)) ** 2
    if bn: e[-bn:] = np.cos(np.linspace(0, np.pi / 2, bn)) ** 2
    return x * e


def normalize(x, peak=0.8, rms=None):
    x = x - np.mean(x)
    if rms is not None:
        x = x * (rms / (np.sqrt(np.mean(x ** 2)) + 1e-12))
        p = np.abs(x).max()
        if p > peak: x *= peak / p      # soft safety: never exceed the peak ceiling
        return x
    return x * (peak / (np.abs(x).max() + 1e-9))


def write_wav(name, data, sub=None):
    """data: mono (n,) or (ch, n) float in [-1, 1]."""
    folder = OUT if sub is None else os.path.join(OUT, sub)
    os.makedirs(folder, exist_ok=True)
    data = np.atleast_2d(data)
    pcm = (np.clip(data, -1, 1) * 32767).astype(np.int16).T.copy()
    path = os.path.join(folder, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(data.shape[0]); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"[nagisa-audio] {name:28s} {data.shape[1] / SR:6.2f}s ch={data.shape[0]} "
          f"peak={np.abs(data).max():.2f} rms={np.sqrt((data ** 2).mean()):.3f}")
    return path


def read_wav(path):
    with wave.open(path, "rb") as w:
        ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    a = np.frombuffer(raw, dtype="<i2").astype(float) / 32768.0
    return a.reshape(-1, ch).T, sr

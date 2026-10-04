"""Clarity master for the region BGM + bike loops (2026-10-02).

The shipped renders were bass-dominated (20-120 Hz carried nearly all the energy) and, for
Sakura/Fuji, had almost nothing above 4 kHz - heard as muffled. This re-masters them:
sub-bass high-pass, low-shelf cut, mud dip, presence + air lift, harmonic exciter on the dark
tracks, then loudness-match and soft-limit. Zero-phase filtering on a wrap-padded copy, so the
loop seam stays seamless. Originals are kept in tools/audio/_originals_20261002 and the script
always re-reads from there, so it is safe to re-run with new settings.
"""
import os, shutil, sys, wave
import numpy as np
from scipy import signal

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
AUDIO = os.path.join(ROOT, "Assets", "Resources", "Audio")
ORIG = os.path.join(ROOT, "tools", "audio", "_originals_20261002")

def biquad(kind, f, sr, gain_db=0.0, q=0.707):
    A = 10 ** (gain_db / 40); w = 2 * np.pi * f / sr; c, s = np.cos(w), np.sin(w); al = s / (2 * q)
    if kind == "hp":   b = [(1+c)/2, -(1+c), (1+c)/2]; a = [1+al, -2*c, 1-al]
    elif kind == "peak": b = [1+al*A, -2*c, 1-al*A]; a = [1+al/A, -2*c, 1-al/A]
    elif kind == "lowshelf":
        t = 2*np.sqrt(A)*al
        b = [A*((A+1)-(A-1)*c+t), 2*A*((A-1)-(A+1)*c), A*((A+1)-(A-1)*c-t)]
        a = [(A+1)+(A-1)*c+t, -2*((A-1)+(A+1)*c), (A+1)+(A-1)*c-t]
    elif kind == "highshelf":
        t = 2*np.sqrt(A)*al
        b = [A*((A+1)+(A-1)*c+t), -2*A*((A-1)+(A+1)*c), A*((A+1)+(A-1)*c-t)]
        a = [(A+1)-(A-1)*c+t, 2*((A-1)-(A+1)*c), (A+1)-(A-1)*c-t]
    return signal.tf2sos(b, a)

def read(path):
    w = wave.open(path); sr, ch = w.getframerate(), w.getnchannels()
    d = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768
    return d.reshape(-1, ch), sr

def write(path, d, sr):
    d = np.clip(d, -1, 1)
    w = wave.open(path, "wb"); w.setnchannels(d.shape[1]); w.setsampwidth(2); w.setframerate(sr)
    w.writeframes((d * 32767).astype("<i2").tobytes()); w.close()

def softlimit(x, ceil=0.89):
    k = 0.8 * ceil
    over = np.abs(x) > k
    y = x.copy(); y[over] = np.sign(x[over]) * (k + (ceil - k) * np.tanh((np.abs(x[over]) - k) / (ceil - k)))
    return y

def master(d, sr, chain, exciter, rms_target):
    pad = min(len(d) // 2, sr * 3)
    p = np.concatenate([d[-pad:], d, d[:pad]])              # wrap-pad: loop seam stays continuous
    sos = np.vstack([biquad(*step[:2], sr, *step[2:]) for step in chain])
    out = signal.sosfiltfilt(sos, p, axis=0)
    if exciter:
        band = signal.sosfiltfilt(signal.butter(2, [1200, 3500], "band", fs=sr, output="sos"), p, axis=0)
        harm = np.tanh(band * 6.0) - band * 6.0 * 0.0
        harm = signal.sosfiltfilt(signal.butter(2, 3200, "high", fs=sr, output="sos"), harm, axis=0)
        out = out + exciter * harm
    out = out[pad:pad + len(d)]
    rms = np.sqrt((out ** 2).mean())
    out *= rms_target / max(rms, 1e-6)
    return softlimit(out)

# (kind, freq, gain_db[, q])
MUSIC = [("hp", 32), ("lowshelf", 140, -6.5), ("peak", 350, -2.0, 0.9), ("peak", 3000, 2.2, 0.8), ("highshelf", 7000, 1.0)]
# Sakura/Fuji have almost nothing above 4 kHz, so they get a real air shelf instead.
MUSIC_DARK = [("hp", 32), ("lowshelf", 140, -7.5), ("peak", 350, -2.0, 0.9), ("peak", 3000, 3.0, 0.8), ("highshelf", 6500, 3.5)]
BIKE_TYRE = [("hp", 45), ("lowshelf", 120, -8.0), ("peak", 2500, 2.5, 0.8)]

JOBS = {
    "BGM_AzoraHighlands.wav": (MUSIC, 0.0,  0.150),
    "BGM_FujiRidge.wav":      (MUSIC_DARK, 0.22, 0.150),   # nothing above 4 kHz: synthesise some air
    "BGM_MapleCity.wav":      (MUSIC, 0.0,  0.150),
    "BGM_MinatoCoast.wav":    (MUSIC, 0.0,  0.150),
    "BGM_SakuraPass.wav":     (MUSIC_DARK, 0.18, 0.150),
    "BGM_ShiosaiCoast.wav":   (MUSIC, 0.0,  0.150),
    "BGM_TakaMountains.wav":  (MUSIC, 0.0,  0.150),
    "Bike_TyreRoll.wav":      (BIKE_TYRE, 0.0, 0.200),
}

if __name__ == "__main__":
    os.makedirs(ORIG, exist_ok=True)
    for name, (chain, ex, rms) in JOBS.items():
        src, bak = os.path.join(AUDIO, name), os.path.join(ORIG, name)
        if not os.path.exists(bak): shutil.copy2(src, bak)
        d, sr = read(bak)
        write(src, master(d, sr, chain, ex, rms), sr)
        print("mastered", name)

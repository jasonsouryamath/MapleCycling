"""Shunta Metro BGM "Harbour Lights": a ~100 s loopable night-coast city-pop piece, synthesised offline (numpy/scipy only).
C major / A minor, 96 BPM. Fmaj7 - G6 - Em7 - Am9 (one bar each). Warm detuned pad, FM electric piano, sub bass,
soft four-on-the-floor kick, plucked arp, a singing lead in the second half, and an ocean-surf bed underneath.
Writes Assets/Resources/ShuntaMetro/shunta_bgm.wav (32 kHz stereo 16-bit). The tail wraps onto the head, so it loops seamlessly.
"""
import os
import wave
import numpy as np
from scipy import signal

SR = 32000
BPM = 96.0
BEAT = 60.0 / BPM
BARS = 40
N = int(round(BARS * 4 * BEAT * SR))
rng = np.random.default_rng(2099)
OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "ShuntaMetro", "shunta_bgm.wav"))

L = np.zeros(N + SR * 4, np.float32)
R = np.zeros(N + SR * 4, np.float32)   # extra 4 s of tail, wrapped onto the head at the end


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


def add(buf, start_s, x, gain=1.0):
    i = int(start_s * SR)
    if i >= len(buf):
        return
    x = x[: len(buf) - i]
    buf[i:i + len(x)] += (x * gain).astype(np.float32)


def stereo(start_s, x, gain=1.0, pan=0.0):
    a = np.sqrt(0.5 * (1 - pan))
    b = np.sqrt(0.5 * (1 + pan))
    add(L, start_s, x, gain * a)
    add(R, start_s, x, gain * b)


def lp(x, fc, order=2):
    return signal.sosfilt(signal.butter(order, fc / (SR / 2), "low", output="sos"), x)


def hp(x, fc, order=2):
    return signal.sosfilt(signal.butter(order, fc / (SR / 2), "high", output="sos"), x)


def tvec(d):
    return np.arange(int(d * SR)) / SR


def saw(f, t, ph=0.0):
    return 2.0 * ((f * t + ph) % 1.0) - 1.0


def pad_note(m, d, vel=1.0):
    t = tvec(d + 1.5)
    x = np.zeros_like(t)
    for det, ph in ((-0.07, 0.0), (0.0, 0.33), (0.07, 0.66), (0.13, 0.15)):
        x += saw(hz(m + det), t, ph)
    x = lp(x, 1500, 2) * 0.25
    env = np.minimum(t / 0.9, 1.0) * np.where(t < d, 1.0, np.exp(-(t - d) / 0.55))
    return x * env * vel


def epiano(m, d, vel=1.0):
    t = tvec(d)
    f = hz(m)
    env = np.exp(-t * 3.2)
    mod = np.sin(2 * np.pi * f * 2.0 * t) * (1.3 * np.exp(-t * 6.0) + 0.25)
    x = np.sin(2 * np.pi * f * t + mod) * env
    x += 0.25 * np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t * 12.0)
    x *= np.minimum(t / 0.004, 1.0)
    return x * vel * 0.5


def pluck(m, d, vel=1.0):
    t = tvec(d)
    f = hz(m)
    env = np.exp(-t * 9.0)
    x = np.sin(2 * np.pi * f * t + 1.8 * env * np.sin(2 * np.pi * f * 3 * t)) * env
    return x * vel * 0.4


def bass_note(m, d, vel=1.0):
    t = tvec(d)
    f = hz(m)
    x = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) + 0.18 * lp(saw(f, t), 400)
    env = np.minimum(t / 0.01, 1.0) * np.exp(-t * 2.2) * np.clip((d - t) / 0.05, 0, 1)
    return x * env * vel * 0.55


def lead_note(m, d, vel=1.0):
    t = tvec(d + 0.35)
    f = hz(m)
    vib = 0.0045 * np.minimum(t / 0.35, 1.0) * np.sin(2 * np.pi * 5.2 * t)
    ph = 2 * np.pi * np.cumsum(f * (1 + vib)) / SR
    x = np.sin(ph) + 0.28 * np.sin(2 * ph) + 0.10 * np.sin(3 * ph)
    env = np.minimum(t / 0.03, 1.0) * np.where(t < d, 1.0, np.exp(-(t - d) / 0.2)) * (0.85 + 0.15 * np.exp(-t * 2))
    return x * env * vel * 0.34


def kick(vel=1.0):
    t = tvec(0.38)
    f = 46 + 90 * np.exp(-t * 28)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * 9.5) * vel * 0.9


def hat(d=0.05, vel=1.0):
    t = tvec(d)
    return hp(rng.standard_normal(len(t)), 7000) * np.exp(-t * 90) * vel * 0.12


def clap(vel=1.0):
    t = tvec(0.28)
    n = signal.sosfilt(signal.butter(2, [1200 / (SR / 2), 7000 / (SR / 2)], "band", output="sos"), rng.standard_normal(len(t)))
    env = np.exp(-t * 17)
    return n * env * vel * 0.22


# (bass root, pad voicing, electric-piano voicing, arp notes)
CH = [
    (41, [53, 57, 60, 64], [60, 64, 67, 72], [65, 72, 69, 76]),    # Fmaj7
    (43, [55, 59, 62, 69], [59, 62, 67, 71], [67, 74, 71, 79]),    # G6
    (40, [52, 55, 59, 62], [59, 62, 67, 71], [64, 71, 67, 74]),    # Em7
    (45, [57, 60, 64, 71], [60, 64, 67, 71], [69, 72, 76, 71]),    # Am9
]
# lead phrases per chord: (beat offset, midi, beats); two different 4-bar phrases
LEAD = [
    [[(0, 76, 1.5), (1.5, 74, 0.5), (2, 72, 1.0), (3, 74, 1.0)], [(0, 71, 1.5), (1.5, 72, 0.5), (2, 74, 2.0)],
     [(0, 76, 1.0), (1, 79, 1.5), (2.5, 76, 0.5), (3, 74, 1.0)], [(0, 72, 3.0)]],
    [[(0, 79, 1.0), (1, 76, 1.0), (2, 74, 1.0), (3, 72, 1.0)], [(0, 74, 1.5), (1.5, 71, 0.5), (2, 72, 2.0)],
     [(0, 71, 1.0), (1, 74, 1.0), (2, 76, 1.0), (3, 79, 1.0)], [(0, 81, 2.0), (2, 79, 1.0), (3, 76, 1.0)]],
]
BASS_PAT = ((0, 0), (1.5, 0), (2, 12), (3, 0), (3.5, 7))

for bar in range(BARS):
    c = bar % 4
    root, padv, epv, arp = CH[c]
    t0 = bar * 4 * BEAT
    sec = "intro" if bar < 4 else "A" if bar < 16 else "B" if bar < 32 else "outro"
    full = sec in ("A", "B")
    for k, m in enumerate(padv):
        stereo(t0, pad_note(m, 4 * BEAT, 0.9 if sec != "outro" else 0.8), 0.55, pan=(k - 1.5) * 0.28)
    if sec != "intro":
        pat = [0, 1, 2, 3, 2, 1, 2, 3] if full else [0, 2]
        step = 0.5 if full else 2.0
        for i, p in enumerate(pat):
            stereo(t0 + i * step * BEAT, epiano(epv[p % 4], 1.2), 0.5 * (1.0 if i % 2 == 0 else 0.7), pan=-0.25 + 0.5 * (i % 2))
    if full:
        for off, o in BASS_PAT:
            stereo(t0 + off * BEAT, bass_note(root + o, 0.8 * BEAT), 0.9, 0.0)
        for b in range(4):
            stereo(t0 + b * BEAT, kick(), 0.8 if sec == "A" else 0.9, 0.0)
        for h in range(8):
            stereo(t0 + h * 0.5 * BEAT, hat(0.05 if h % 2 else 0.09, 1.0 if h % 2 else 0.6), 1.0, pan=0.3 if h % 2 else -0.3)
        if sec == "B":
            stereo(t0 + 1 * BEAT, clap(), 0.8, 0.0)
            stereo(t0 + 3 * BEAT, clap(), 0.8, 0.0)
            for i in range(16):
                stereo(t0 + i * 0.25 * BEAT, pluck(arp[i % 4] + 12, 0.25), 0.28, pan=float(np.sin(i * 0.9)) * 0.6)
    elif sec == "intro" and bar == 3:
        stereo(t0 + 3 * BEAT, kick(), 0.5)
    if sec == "outro" and bar < 38:
        stereo(t0, bass_note(root, 3.6 * BEAT), 0.6)
    if sec == "B":
        for off, m, dur in LEAD[((bar - 16) // 4) % 2][c]:
            stereo(t0 + off * BEAT, lead_note(m, dur * BEAT), 0.85, pan=0.08)
    elif sec == "A" and bar >= 12:     # a teaser echo of the hook before the lead enters
        off, m, dur = LEAD[0][c][0]
        stereo(t0 + off * BEAT, lead_note(m - 12, dur * BEAT), 0.35, pan=-0.1)

# ocean surf bed: a swell every ~9.6 s, decorrelated between the ears
T = np.arange(len(L)) / SR
for ch, buf in enumerate((L, R)):
    n = rng.standard_normal(len(buf)).astype(np.float32)
    n = lp(n, 900, 2) * 0.7 + lp(n, 220, 1) * 0.8
    swell = (0.5 + 0.5 * np.sin(2 * np.pi * (T / 9.6) - 1.2 + ch * 0.9)) ** 2.2
    swell = 0.25 + 0.75 * swell
    bed = (n * swell).astype(np.float32)
    bed *= np.interp(T, [0, 6, 14, N / SR - 10, N / SR], [0.9, 0.55, 0.42, 0.55, 0.9]).astype(np.float32)
    buf += bed * 0.20


def reverb(x, rt=2.3, damp=3500, mix=0.30):
    n = int(rt * SR)
    ir = rng.standard_normal(n) * np.exp(-np.arange(n) / (rt * SR / 5.5))
    ir = lp(ir, damp, 1)
    ramp = int(0.012 * SR)
    ir[:ramp] *= np.linspace(0, 1, ramp)
    ir /= np.sqrt((ir ** 2).sum())
    return x + mix * signal.fftconvolve(x, ir)[: len(x)]


L2 = reverb(L, 2.4, 3200, 0.34)
R2 = reverb(R, 2.6, 3000, 0.34)
dl = int(BEAT * 0.75 * SR)       # dotted-eighth ping-pong
for k in range(1, 4):
    amt = 0.11 * (0.55 ** (k - 1))
    a, b = L2.copy(), R2.copy()
    L2[dl * k:] += amt * b[: len(b) - dl * k]
    R2[dl * k:] += amt * a[: len(a) - dl * k]

tail = len(L2) - N
L2[:tail] += L2[N:N + tail]
R2[:tail] += R2[N:N + tail]
L2, R2 = L2[:N], R2[:N]
fade = int(0.01 * SR)
for a in (L2, R2):
    a[:fade] *= np.sqrt(np.linspace(0, 1, fade))
    a[-fade:] *= np.sqrt(np.linspace(1, 0, fade))

st = np.stack([hp(L2, 28, 1), hp(R2, 28, 1)], 1)
st = np.tanh(st * 1.1) / np.tanh(1.1)
st *= 0.70 / np.max(np.abs(st))
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with wave.open(OUT, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((st * 32767).astype("<i2").tobytes())
print("wrote", OUT, f"{N / SR:.1f}s", f"{os.path.getsize(OUT) / 1e6:.1f} MB")

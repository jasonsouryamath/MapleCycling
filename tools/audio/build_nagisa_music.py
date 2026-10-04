"""
"Nagisa Drive" - original city-pop / coastal-electronic cycling theme for Nagisa Bay (worker H, 2026-10-01).

Procedural (numpy/scipy). Renders SIX loop-safe, sample-aligned stems, 120 BPM, A major, 32 bars (64.0 s) at
32 kHz into Assets/Resources/Audio/Nagisa/Music/. NagisaMusicDirector.cs crossfades the stems by route zone.

  stem0 Keys   warm detuned-saw pad + subtle piano comping (always present, the harmonic floor)
  stem1 Bass   melodic round bass with syncopated octave leaps (mono)
  stem2 Guitar bright clean 16th-note funk strums with muted ghost strokes, chorused
  stem3 Perc   light electronic percussion: soft kick, rim, 8th hats, shaker, tambourine
  stem4 Drive  full-energy layer: four-on-the-floor kick, clap/snare, 16th hats, syncopated pluck-synth arp, fills
  stem5 Lead   warm synth melody + bell doubling + dotted-8th echo (opens up at the overlook)

Form (8 bars each): A (Dmaj9 E7 C#m7 F#m9 | Dmaj9 E7 Amaj9 Bm7-E7sus), B (Amaj9 F#m7 Bm9 E13 | Amaj7 C#m7 Dmaj9 Dm6-E7),
A', B'. Everything wraps at the loop point (tails fold back), so any single stem loops seamlessly.

Run: python tools/audio/build_nagisa_music.py [stem ...]
"""
import sys, os
import numpy as np
from scipy import signal
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nagisa_dsp as D
from nagisa_dsp import SR, lp, hp, bp, wadd, sadd, write_wav, midi

BPM = 120
BAR = 4 * SR * 60 // BPM          # 64000 samples (2.0 s)
STEP = BAR // 16                  # 4000 samples per 16th
BARS = 32
N = BAR * BARS                    # 2,048,000 samples = 64.0 s
SUB = "Music"

# ----------------------------------------------------------------------------- harmony (A major)
# chord = (root midi in the bass octave (A1=33 .. G#2=44), intervals)
MAJ9 = (0, 4, 7, 11, 14); MIN7 = (0, 3, 7, 10); MIN9 = (0, 3, 7, 10, 14); DOM7 = (0, 4, 7, 10)
DOM13 = (0, 4, 10, 14, 21); SUS4 = (0, 5, 7, 10); MAJ7 = (0, 4, 7, 11); MIN6 = (0, 3, 7, 9)
Dm = lambda: (38, MAJ9)
CH = {
    "D":   (38, MAJ9), "E7": (40, DOM7), "C#m7": (37, MIN7), "F#m9": (42, MIN9), "A9": (33, MAJ9),
    "Bm7": (35, MIN7), "E7s": (40, SUS4), "F#m7": (42, MIN7), "Bm9": (35, MIN9), "E13": (40, DOM13),
    "A7": (33, MAJ7), "Dm6": (38, MIN6),
}
SEC_A = [("D", "D"), ("E7", "E7"), ("C#m7", "C#m7"), ("F#m9", "F#m9"),
         ("D", "D"), ("E7", "E7"), ("A9", "A9"), ("Bm7", "E7s")]
SEC_B = [("A9", "A9"), ("F#m7", "F#m7"), ("Bm9", "Bm9"), ("E13", "E13"),
         ("A7", "A7"), ("C#m7", "C#m7"), ("D", "D"), ("Dm6", "E7")]
PROG = []                         # (bar, beat_in_bar, chord_name) in order
for si, sec in enumerate([SEC_A, SEC_B, SEC_A, SEC_B]):
    for bi, (c1, c2) in enumerate(sec):
        bar = si * 8 + bi
        if c1 == c2: PROG.append((bar, 0, c1))
        else: PROG.extend([(bar, 0, c1), (bar, 2, c2)])


def chord_spans():
    """Yield (start_step, end_step, name) over the whole loop (16 steps per bar)."""
    out = []
    for i, (bar, beat, name) in enumerate(PROG):
        s = bar * 16 + beat * 4
        if i + 1 < len(PROG): e = PROG[i + 1][0] * 16 + PROG[i + 1][1] * 4
        else: e = BARS * 16
        out.append((s, e, name))
    return out


SPANS = chord_spans()


def voicing(prev, name, lo=57, hi=80):
    """Close-voiced upper structure (3rd, 7th, 9th/5th) chosen to move as little as possible from `prev`."""
    root, ints = CH[name]
    pcs = [(root + i) % 12 for i in ints[1:5]]
    best, bs = None, 1e9
    import itertools
    for shifts in itertools.product((-12, 0, 12), repeat=len(pcs)):
        notes = []
        for pc, sh in zip(pcs, shifts):
            base = lo + ((pc - lo) % 12)
            notes.append(base + sh + (12 if base + sh < lo else 0))
        notes = sorted(notes)
        if notes[0] < lo - 2 or notes[-1] > hi + 4 or len(set(notes)) < len(notes): continue
        if notes[-1] - notes[0] > 17: continue
        cost = 0 if prev is None else sum(abs(a - b) for a, b in zip(notes, prev)) + 0.2 * (notes[-1] - notes[0])
        if cost < bs: best, bs = notes, cost
    return best


def voicings():
    out, prev = {}, None
    for s, e, name in SPANS:
        v = voicing(prev, name); prev = v; out[(s, e, name)] = v
    return out


VOIC = voicings()

# ----------------------------------------------------------------------------- melody (beat grid, midi)
MEL_A = [
    # bar 1 Dmaj9
    (0, 78, 1.5), (1.5, 76, .5), (2, 74, 1), (3, 76, .5), (3.5, 78, .5),
    # bar 2 E7
    (4, 80, 1), (5, 78, .5), (5.5, 76, .5), (6, 73, 1.5), (7.5, 76, .5),
    # bar 3 C#m7
    (8, 76, 1.5), (9.5, 73, .5), (10, 71, 1), (11, 73, 1),
    # bar 4 F#m9
    (12, 69, 1), (13, 73, 1), (14, 76, 1.5), (15.5, 78, .5),
    # bar 5 Dmaj9 (higher)
    (16, 81, 1.5), (17.5, 78, .5), (18, 76, 1), (19, 74, .5), (19.5, 76, .5),
    # bar 6 E7
    (20, 78, 1), (21, 76, .5), (21.5, 74, .5), (22, 71, 1.5), (23.5, 73, .5),
    # bar 7 Amaj9
    (24, 76, 2), (26, 73, 1), (27, 69, 1),
    # bar 8 Bm7 | E7sus4
    (28, 74, 1), (29, 71, .5), (29.5, 69, .5), (30, 71, 1), (31, 68, 1),
]
MEL_B = [
    (0, 81, 1), (1, 80, .5), (1.5, 78, .5), (2, 76, 2),
    (4, 78, 1.5), (5.5, 76, .5), (6, 73, 1), (7, 76, 1),
    (8, 74, 1), (9, 78, 1), (10, 81, 1.5), (11.5, 80, .5),
    (12, 80, 2), (14, 76, 1), (15, 73, .5), (15.5, 71, .5),
    (16, 69, .5), (16.5, 73, .5), (17, 76, 1), (18, 81, 2),
    (20, 80, 1), (21, 76, 1), (22, 73, 1.5), (23.5, 76, .5),
    (24, 78, 1.5), (25.5, 74, .5), (26, 81, 1), (27, 78, 1),
    (28, 77, 1), (29, 74, 1), (30, 76, 1), (31, 80, 1),
]


def melody(sec_index):
    base = MEL_A if sec_index % 2 == 0 else MEL_B
    out = []
    for (b, m, d) in base:
        out.append([sec_index * 32 + b, m, d])
    if sec_index == 2:                       # A': tag the held notes with an octave-up answer in bar 4/8
        out += [[sec_index * 32 + 15.5, 85, .5]]
    if sec_index == 3:                       # B': final climb
        out += [[sec_index * 32 + 30.0, 85, 1.0], [sec_index * 32 + 31.0, 88, 1.0]]
    return out


# ----------------------------------------------------------------------------- voices
def reverb(st, seconds=2.0, wet=0.2, lpf=5200, pre=0.012):
    n = int(seconds * SR); t = np.arange(n) / SR
    out = np.zeros_like(st)
    for ch in range(st.shape[0]):
        ir = D.RNG.normal(size=n) * np.exp(-t * 6.9 / seconds)
        ir = lp(ir, lpf); ir[:int(pre * SR)] = 0
        ir /= np.sqrt((ir ** 2).sum()) + 1e-12
        w = signal.fftconvolve(st[ch], ir)
        f = w[:st.shape[1]].copy(); f[:len(w) - st.shape[1]] += w[st.shape[1]:]
        out[ch] = st[ch] * (1 - wet) + f * wet * 2.0
    return out


def fb_delay(st, delay_s, fb=0.35, mix=0.25, cross=True, lpf=4500):
    """Wrap-around (loop-safe) ping-pong delay."""
    d = int(delay_s * SR); out = st.copy(); cur = st.copy()
    for k in range(6):
        cur = np.roll(cur, d, axis=1)
        cur = np.stack([lp(cur[1], lpf), lp(cur[0], lpf)]) if cross else lp(cur, lpf)
        cur = cur * fb
        out += cur * mix / max(fb, 1e-3) * (fb ** 0)  # level handled by fb^k via cur
    return out


def chorus(x, depth=0.0035, rate=0.35, mix=0.5, ph=0.0):
    """Stereo-ish chorus on a mono signal -> (2, n). Loop-safe: rate*len(x)/SR is made an integer cycle count."""
    n = len(x); t = np.arange(n) / SR
    cyc = max(1, round(rate * n / SR)); r = cyc * SR / n
    outs = []
    for k, p in enumerate((ph, ph + np.pi * 0.9)):
        d = (depth * (1 + np.sin(2 * np.pi * r * t + p)) * SR).astype(float)
        idx = (np.arange(n) - d) % n
        i0 = np.floor(idx).astype(int); fr = idx - i0
        outs.append(x * (1 - mix) + (x[i0 % n] * (1 - fr) + x[(i0 + 1) % n] * fr) * mix)
    return np.stack(outs)


def ks(f, dur, bright=0.5, decay=0.995, vel=0.7):
    n = int((dur + 0.15) * SR); N0 = max(2, int(round(SR / f)))
    burst = lp(D.RNG.uniform(-1, 1, N0), 1800 + bright * 7000)
    x = np.zeros(n); x[:N0] = burst
    a = np.zeros(N0 + 2); a[0] = 1.0; a[N0] = -decay * 0.5; a[N0 + 1] = -decay * 0.5
    y = signal.lfilter([1.0], a, x)
    y *= np.clip((dur + 0.15 - np.arange(n) / SR) / 0.15, 0, 1)
    return y / (np.abs(y).max() + 1e-9) * vel


def piano(f, dur, vel=0.6):
    n = int((dur + 0.6) * SR); t = np.arange(n) / SR
    y = np.zeros(n)
    for k in range(1, 9):
        y += np.sin(2 * np.pi * f * k * t * (1 + 0.0004 * k * k)) * np.exp(-t * (1.6 + 1.7 * k)) / k ** 1.1
    y += lp(D.RNG.normal(size=n), 2200) * np.exp(-t * 55) * 0.18      # hammer
    y *= np.clip((dur + 0.6 - t) / 0.5, 0, 1)
    return y / (np.abs(y).max() + 1e-9) * vel


def saw_pad(f, dur, vel=0.5):
    n = int((dur + 1.0) * SR); t = np.arange(n) / SR
    y = np.zeros(n)
    for dt in (-0.07, -0.03, 0.0, 0.035, 0.08):               # detune in semitone-ish fractions
        ff = f * 2 ** (dt / 12)
        y += signal.sawtooth(2 * np.pi * ff * t + D.RNG.uniform(0, 6.28))
    k = np.clip(t / 1.2, 0, 1)
    y = lp(y / 5, 1100) * (1 - k) + lp(y / 5, 2300) * k      # filter opens as the pad blooms
    e = np.minimum(np.clip(t / 0.35, 0, 1), np.clip((dur + 1.0 - t) / 0.9, 0, 1))
    return y * e * vel


def round_bass(f, dur, vel=0.9, accent=False):
    n = int((dur + 0.08) * SR); t = np.arange(n) / SR
    y = signal.sawtooth(2 * np.pi * f * t) * 0.55 + signal.square(2 * np.pi * f * t, 0.45) * 0.25 + np.sin(2 * np.pi * f * t) * 0.8
    cut = 260 + (900 if accent else 520) * np.exp(-t * 14)
    # cheap time-varying low-pass: blend two fixed filters by the envelope
    lo, hi = lp(y, 330), lp(y, 1400)
    mix = np.exp(-t * (11 if not accent else 8))
    y = lo * (1 - mix) + hi * mix
    y += np.sin(2 * np.pi * f * 2 * t) * 0.08 * np.exp(-t * 6)
    e = np.minimum(np.clip(t / 0.004, 0, 1), np.clip((dur + 0.08 - t) / 0.06, 0, 1)) * np.exp(-t * 0.9)
    return y * e * vel


def pluck_synth(f, dur, vel=0.5):
    n = int((dur + 0.1) * SR); t = np.arange(n) / SR
    y = signal.sawtooth(2 * np.pi * f * t) + 0.6 * signal.square(2 * np.pi * f * 1.004 * t, 0.3)
    lo, hi = lp(y, 900), lp(y, 5200)
    m = np.exp(-t * 16)
    y = (lo * (1 - m) + hi * m) * np.exp(-t * 7) * np.clip((dur + 0.1 - t) / 0.04, 0, 1)
    return y / (np.abs(y).max() + 1e-9) * vel


def warm_lead(f, dur, vel=0.6, glide_from=None):
    n = int((dur + 0.5) * SR); t = np.arange(n) / SR
    vib = 1 + 0.0045 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.18) / 0.3, 0, 1)
    fr = f * vib
    if glide_from:
        fr = fr * (glide_from / f) ** np.exp(-t * 40)
    ph = 2 * np.pi * np.cumsum(fr) / SR
    y = signal.sawtooth(ph) * 0.5 + signal.sawtooth(ph * 1.0045 + 0.7) * 0.4 + np.sin(ph) * 0.45 + signal.square(ph * 0.5) * 0.12
    k = np.clip(t / 0.25, 0, 1) * np.exp(-t * 0.8)
    y = lp(y, 2300) * (1 - k) + lp(y, 4800) * k
    e = D.adsr(n, 0.02, 0.15, 0.78, 0.3, dur)
    return y * e * vel


def bell(f, vel=0.4):
    n = int(1.6 * SR); t = np.arange(n) / SR
    y = np.sin(2 * np.pi * f * t) * np.exp(-t * 4) + 0.5 * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t * 6) \
        + 0.25 * np.sin(2 * np.pi * f * 3.01 * t) * np.exp(-t * 9)
    return y * vel


# --- drums
def kick(vel=0.9, punch=1.0):
    n = int(0.5 * SR); t = np.arange(n) / SR
    fr = 48 + 130 * np.exp(-t * 34)
    y = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t * 7.5) * vel
    y += lp(D.RNG.normal(size=n), 2600) * np.exp(-t * 90) * 0.35 * punch
    return y


def soft_kick(vel=0.6): return lp(kick(vel, 0.5), 700) * 0.9


def snare(vel=0.8):
    n = int(0.4 * SR); t = np.arange(n) / SR
    body = np.sin(2 * np.pi * (190 + 70 * np.exp(-t * 30)) * t) * np.exp(-t * 22) * 0.5
    nz = bp(D.RNG.normal(size=n), 1400, 9000) * np.exp(-t * 15) * 0.9
    return (body + nz) * vel


def clap(vel=0.7):
    n = int(0.35 * SR); t = np.arange(n) / SR; y = np.zeros(n)
    for dly in (0.0, 0.011, 0.023):
        a = int(dly * SR); y[a:] += bp(D.RNG.normal(size=n - a), 1000, 5200) * np.exp(-t[:n - a] * 70)
    y += bp(D.RNG.normal(size=n), 1000, 5200) * np.exp(-t * 14) * 0.35
    return y * vel * 0.8


def rim(vel=0.5):
    n = int(0.1 * SR); t = np.arange(n) / SR
    return (np.sin(2 * np.pi * 1700 * t) * 0.6 + np.sin(2 * np.pi * 480 * t) * 0.5) * np.exp(-t * 90) * vel


def hat(open_=False, vel=0.5):
    n = int((0.34 if open_ else 0.06) * SR); t = np.arange(n) / SR
    return hp(D.RNG.normal(size=n), 7000, 2) * np.exp(-t * (11 if open_ else 80)) * vel


def shaker(vel=0.4):
    n = int(0.09 * SR); t = np.arange(n) / SR
    return bp(D.RNG.normal(size=n), 4500, 12000) * np.sin(np.pi * np.clip(t / 0.09, 0, 1)) ** 2 * vel


def tamb(vel=0.4):
    n = int(0.16 * SR); t = np.arange(n) / SR
    return (bp(D.RNG.normal(size=n), 5500, 13000) * np.exp(-t * 28) + np.sin(2 * np.pi * 7000 * t) * np.exp(-t * 60) * 0.1) * vel


def tom(f, vel=0.7):
    n = int(0.45 * SR); t = np.arange(n) / SR
    fr = f * (1 + 0.6 * np.exp(-t * 18))
    return np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t * 9) * vel


def crash(vel=0.5):
    n = int(2.0 * SR); t = np.arange(n) / SR
    return hp(D.RNG.normal(size=n), 3500) * np.exp(-t * 2.4) * vel


# ----------------------------------------------------------------------------- stems
def at(step): return int(step * STEP)


def stem_keys():
    st = np.zeros((2, N))
    for (s, e, name) in SPANS:
        notes = VOIC[(s, e, name)]
        root, ints = CH[name]
        dur = (e - s) * STEP / SR
        for k, m in enumerate([root + 12] + notes[:4]):
            sadd(st, at(s), saw_pad(midi(m), dur, 0.16), 1.0, pan=(-0.45 + 0.3 * k) * 0.8)
    st = reverb(st, 2.8, 0.25)
    pn = np.zeros((2, N))
    # subtle piano comping (dotted rhythm); fuller in the B sections
    pat_a = [0, 6, 10]; pat_b = [0, 3, 6, 10, 12]
    for (s, e, name) in SPANS:
        notes = VOIC[(s, e, name)]; sec = (s // 16) // 8
        pat = pat_a if sec % 2 == 0 else pat_b
        bar0 = (s // 16) * 16
        for off in pat:
            step = bar0 + off
            if not (s <= step < e): continue
            for k, m in enumerate(notes[1:]):
                sadd(pn, at(step) + k * 70, piano(midi(m + 12 if m < 66 else m), 0.5, 0.18), 1.0, pan=0.25 + 0.15 * k - 0.25)
        # small high arpeggio sparkle on the last chord of each section
        if e % 128 == 0 or e == BARS * 16:
            for j, m in enumerate(notes):
                sadd(pn, at(e - 8) + j * 1500, piano(midi(m + 24), 0.4, 0.1), 1.0, pan=(j - 1.5) * 0.3)
    st += reverb(pn, 1.6, 0.22)
    return st


def stem_bass():
    st = np.zeros(N)
    for si, (s, e, name) in enumerate(SPANS):
        root, ints = CH[name]
        nxt = SPANS[(si + 1) % len(SPANS)][2]
        nroot = CH[nxt][0]
        sec = (s // 16) // 8
        r = lambda o: midi(root + o)
        bar0 = (s // 16) * 16
        # (step offset in bar, semitones from root, length steps, accent)
        if sec % 2 == 0:
            pat = [(0, 0, 3, True), (3, 12, 1, False), (4, 7, 2, False), (6, 0, 2, False), (8, 0, 3, True),
                   (11, 12, 1, False), (12, 10 if "E" in name else 7, 2, False), (14, -1 if False else 0, 1, False)]
        else:
            pat = [(0, 0, 2, True), (2, 12, 1, False), (3, 12, 1, False), (4, 7, 2, False), (6, 4 if name not in ("C#m7", "F#m7", "Bm9") else 3, 1, False),
                   (7, 7, 1, False), (8, 0, 2, True), (10, 12, 1, False), (11, 7, 1, False), (12, 0, 2, False), (14, 5, 1, False)]
        half = (e - s) < 16 and True
        for (off, semis, ln, acc) in pat:
            step = bar0 + off
            if not (s <= step < e): continue
            ln = min(ln, e - step)
            wadd(st, at(step), round_bass(r(semis) if semis != -1 else r(0), ln * STEP / SR * 0.92, 0.55, acc))
        # chromatic approach to the next chord root on the very last 16th of the span
        a = e - 1
        if (e - s) >= 2:
            appr = nroot + (1 if nroot < root else -1) + (12 if abs(nroot - root) > 7 else 0)
            wadd(st, at(a), round_bass(midi(appr + (0 if appr >= 33 else 12)), STEP / SR * 0.9, 0.4))
    st = D.circ(lambda x: lp(x, 2500), st)
    return np.stack([st, st])


def stem_guitar():
    st = np.zeros((2, N))
    hits = {  # step in bar -> (muted, vel)
        "a": [(0, 0, .8), (3, 0, .6), (6, 0, .7), (8, 0, .7), (10, 1, .4), (11, 0, .6), (14, 1, .45)],
        "b": [(0, 0, .85), (2, 1, .4), (3, 0, .65), (6, 0, .75), (7, 1, .35), (8, 0, .75), (10, 0, .6), (11, 1, .4), (12, 0, .65), (14, 0, .6), (15, 1, .35)],
    }
    mono = np.zeros(N)
    for (s, e, name) in SPANS:
        notes = VOIC[(s, e, name)]; sec = (s // 16) // 8
        pat = hits["a" if sec % 2 == 0 else "b"]
        bar0 = (s // 16) * 16
        for (off, muted, vel) in pat:
            step = bar0 + off
            if not (s <= step < e): continue
            down = (off % 2 == 0)
            order = notes if down else notes[::-1]
            for k, m in enumerate(order):
                f = midi(m + (12 if m < 60 else 0))
                dur = 0.07 if muted else min(0.42, (e - step) * STEP / SR * 0.95)
                y = ks(f, dur, bright=0.75 if not muted else 0.3, decay=0.9955 if not muted else 0.97, vel=vel * (0.5 if muted else 1.0))
                wadd(mono, at(step) + k * 140, y, 0.2)
    mono = D.circ(lambda x: hp(x, 140), mono)
    st = chorus(mono, 0.0042, 0.31, 0.55)
    # a little presence shelf
    st = np.stack([st[0] + 0.18 * bp(st[0], 2000, 5000), st[1] + 0.18 * bp(st[1], 2000, 5000)])
    st = reverb(st, 1.3, 0.16)
    return st


def stem_perc():
    st = np.zeros((2, N))
    for bar in range(BARS):
        b = bar * 16
        sec = bar // 8
        kicks = [0, 8] + ([11] if sec % 2 else [])
        for s_ in kicks: sadd(st, at(b + s_), soft_kick(0.55), 1.0, 0.0)
        for s_ in (4, 12): sadd(st, at(b + s_), rim(0.38), 1.0, 0.18)
        for s_ in range(0, 16, 2):
            v = 0.34 if s_ % 4 == 0 else 0.27
            sadd(st, at(b + s_) + D.RNG.integers(-30, 30), hat(False, v), 1.0, -0.25)
        for s_ in range(16):
            sadd(st, at(b + s_), shaker(0.2 if s_ % 2 else 0.3), 1.0, 0.3)
        if bar % 2 == 1: sadd(st, at(b + 14), hat(True, 0.28), 1.0, -0.3)
        if sec % 2 == 1:
            for s_ in (2, 6, 10, 14): sadd(st, at(b + s_), tamb(0.14 + 0.04 * (s_ == 6)), 1.0, 0.35)
    return reverb(st, 0.9, 0.12, lpf=7000)


def stem_drive():
    st = np.zeros((2, N)); arp = np.zeros(N)
    for bar in range(BARS):
        b = bar * 16
        for s_ in (0, 4, 8, 12): sadd(st, at(b + s_), kick(0.95), 1.0, 0.0)
        for s_ in (4, 12):
            sadd(st, at(b + s_), clap(0.7), 1.0, 0.1); sadd(st, at(b + s_), snare(0.45), 1.0, -0.05)
        for s_ in range(16):
            sadd(st, at(b + s_), hat(False, 0.4 if s_ % 4 == 2 else 0.22), 1.0, -0.2)
        for s_ in (2, 6, 10, 14): sadd(st, at(b + s_), hat(True, 0.22) if s_ == 14 else hat(False, 0.35), 1.0, 0.25)
        if bar % 8 == 7:                                                # fill into the next section
            for j, s_ in enumerate((8, 10, 12, 13, 14, 15)):
                sadd(st, at(b + s_), snare(0.35 + 0.08 * j), 1.0, (j - 3) * 0.12)
            for j, s_ in enumerate((12, 13, 14, 15)):
                sadd(st, at(b + s_), tom(180 - 22 * j, 0.5), 1.0, -0.4 + 0.25 * j)
        if bar % 8 == 0: sadd(st, at(b), crash(0.35), 1.0, 0.2)
    # syncopated 16th arpeggio of chord tones, ducked on the kick
    for (s, e, name) in SPANS:
        notes = VOIC[(s, e, name)]; bar0 = (s // 16) * 16
        seq = [notes[0], notes[2], notes[1], notes[3 % len(notes)], notes[2], notes[1], notes[0], notes[1]]
        steps = [0, 2, 3, 6, 8, 10, 11, 14]
        for k, off in enumerate(steps):
            step = bar0 + off
            if not (s <= step < e): continue
            wadd(arp, at(step), pluck_synth(midi(seq[k % len(seq)] + 12), 0.22, 0.5))
    duck = np.ones(N)
    for bar in range(BARS):
        for s_ in (0, 4, 8, 12):
            a = at(bar * 16 + s_); m = int(0.11 * SR); curve = 1 - 0.55 * np.exp(-np.arange(m) / (0.04 * SR))
            duck[a:a + m] = np.minimum(duck[a:a + m], curve)
    arp = arp * duck
    st += chorus(arp, 0.003, 0.5, 0.5) * 0.7
    # pulsing octave bass in the drive layer, off-beat 8ths
    pb = np.zeros(N)
    for (s, e, name) in SPANS:
        root, _ = CH[name]; bar0 = (s // 16) * 16
        for off in (2, 6, 10, 14):
            step = bar0 + off
            if s <= step < e: wadd(pb, at(step), round_bass(midi(root + 12), 0.16, 0.5, True), 0.6)
    st += np.stack([pb, pb]) * duck
    return reverb(st, 1.0, 0.14, lpf=6500)


def stem_lead():
    st = np.zeros((2, N)); mel = np.zeros(N); bl = np.zeros(N)
    notes_all = []
    for sec in range(4): notes_all += melody(sec)
    prev = None
    for (b, m, d) in notes_all:
        step = b * 4
        dur = d * 4 * STEP / SR
        f = midi(m)
        gl = midi(prev) if prev is not None and abs(prev - m) <= 4 and D.RNG.random() < 0.5 else None
        wadd(mel, at(step), warm_lead(f, dur * 0.97, 0.55, glide_from=gl), 1.0)
        wadd(bl, at(step), bell(midi(m + 12), 0.35 if b >= 64 else 0.22), 1.0)
        prev = m
    # in A' and B' the lead is doubled an octave below, very softly
    st[0] += mel * 0.9 + bl * 0.4; st[1] += mel * 0.9 + bl * 0.4
    st = fb_delay(st, 0.375, 0.42, 0.22)                      # dotted-8th @ 120 BPM
    return reverb(st, 1.8, 0.2)


STEMS = {"keys": stem_keys, "bass": stem_bass, "guitar": stem_guitar, "perc": stem_perc, "drive": stem_drive, "lead": stem_lead}
# target RMS per stem (relative balance; the master gain below keeps the full sum under 0 dBFS)
TARGET_RMS = {"keys": 0.050, "bass": 0.060, "guitar": 0.040, "perc": 0.032, "drive": 0.052, "lead": 0.048}
STEM_PEAK_CAP = 0.55


def render(names):
    out = {}
    for i, name in enumerate(names):
        D.seed(20261001 + 977 * sum(map(ord, name)))
        st = STEMS[name]()
        st = st - st.mean(axis=1, keepdims=True)
        rms = np.sqrt((st ** 2).mean())
        st = st * (TARGET_RMS[name] / (rms + 1e-12))
        pk = np.abs(st).max()
        if pk > STEM_PEAK_CAP: st = st * (STEM_PEAK_CAP / pk)
        out[name] = st
        mono = name == "bass"
        write_wav(f"Nagisa_{name}", st[:1] if mono else st, SUB)
    return out


if __name__ == "__main__":
    render(sys.argv[1:] or list(STEMS))

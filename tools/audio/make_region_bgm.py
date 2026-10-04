"""
MapleRide - offline renderer for the region BGM that was missing (2026-09-25, claude-cowork).

Before this, only Minato Coast had music (tools/audio/make_ride_audio.py). This renders one
original, seamless stereo loop for every other region, reusing that script's instruments,
reverb and loop-safe mixing (note tails and reverb wrap around the loop point).

Everything is original and synthesised with numpy/scipy. No samples, no licensed material.

Outputs (Assets/Resources/Audio/, loaded by Assets/Ride/Audio/RideAudio.cs BgmFor):
  BGM_SakuraPass.wav       84 BPM, G yo-pentatonic: koto plucks, breathy bamboo flute, soft taiko.
  BGM_ShiosaiCoast.wav    104 BPM, A major: bright plucked-guitar arpeggios, bells, sea wash.
  BGM_MapleCity.wav       112 BPM, Bb: funky city groove, electric piano, clap backbeat.
  BGM_AzoraHighlands.wav   92 BPM, F lydian: airy open anthem, pads, flute lead.
  BGM_TakaMountains.wav   120 BPM, D minor: driving taiko and strings, heroic lead.
  BGM_FujiRidge.wav        72 BPM, E major: ethereal cloud pads, bells, slow flute.

Run: python tools/audio/make_region_bgm.py [region ...]
"""

import os
import sys

import numpy as np
from scipy import signal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_ride_audio as base  # noqa: E402  (shared instruments + loop-safe mixing)
from make_ride_audio import (SR, midi, env_adsr, lp, hp, bp, add, write_wav, loop_noise,  # noqa: E402
                             circ_filter, ep_note, bass_note, pad_chord, bell_note, kick,
                             snare, hat, shaker, reverb)

RNG = np.random.default_rng(20260926)


# ----------------------------------------------------------------------------- new voices

def pluck(f, dur, vel=0.7, bright=0.5, decay=0.996):
    """Karplus-Strong string (koto / nylon guitar). IIR comb via lfilter, so it is fast."""
    n = int((dur + 1.2) * SR)
    N = max(2, int(round(SR / f)))
    burst = RNG.uniform(-1, 1, N)
    burst = lp(burst, 1500 + bright * 6000)
    x = np.zeros(n)
    x[:N] = burst
    a = np.zeros(N + 2)
    a[0] = 1.0
    a[N] = -decay * 0.5
    a[N + 1] = -decay * 0.5
    y = signal.lfilter([1.0], a, x)
    t = np.arange(n) / SR
    rel = np.clip((dur + 0.6 - t) / 0.6, 0, 1)
    y = y * rel
    return y / (np.abs(y).max() + 1e-9) * vel


def flute(f, dur, vel=0.6, breath=0.18):
    """Bamboo-flute-ish: sine + weak 2nd/3rd partials, delayed vibrato, band-passed breath."""
    n = int((dur + 0.5) * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.0 * t) * np.clip((t - 0.25) / 0.3, 0, 1)
    ph = 2 * np.pi * f * np.cumsum(vib) / SR
    tone = np.sin(ph) + 0.22 * np.sin(2 * ph) + 0.08 * np.sin(3 * ph)
    nz = bp(RNG.normal(size=n), f * 0.8, min(f * 3.0, 16000)) * breath
    e = env_adsr(n, 0.07, 0.2, 0.8, 0.35, dur)
    chiff = np.exp(-t * 30) * 0.3
    return (tone + nz * (1 + chiff * 3)) * e * vel * 0.6


def taiko(pitch=62.0, vel=0.9):
    n = int(0.9 * SR)
    t = np.arange(n) / SR
    f = pitch + pitch * 0.6 * np.exp(-t * 18)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 4.2)
    skin = lp(RNG.normal(size=n), 900) * np.exp(-t * 25) * 0.6
    return (body + skin) * vel


def clap():
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    env = np.zeros(n)
    for k, off in enumerate((0.0, 0.011, 0.022)):
        env += np.exp(-np.clip(t - off, 0, None) * 90) * (t >= off) * (0.7 if k < 2 else 1.0)
    env += np.exp(-t * 18) * 0.3
    return bp(RNG.normal(size=n), 900, 6000) * env * 0.6


def strings(freqs, dur, bright=1800):
    """Slow-attack ensemble: pad_chord's detuned saws with a softer, longer envelope."""
    n = int((dur + 1.5) * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for f in freqs:
        for det in (-0.09, -0.03, 0.04, 0.1):
            ff = f * 2 ** (det / 12) * (1 + 0.002 * np.sin(2 * np.pi * RNG.uniform(4, 6) * t))
            x += signal.sawtooth(2 * np.pi * np.cumsum(ff) / SR + RNG.uniform(0, 6.28))
    x = lp(x, bright, 2)
    e = env_adsr(n, 0.35, 0.4, 0.85, 1.2, dur)
    return x * e / (len(freqs) * 4)


def sea_bed(L, gain=0.05, swells=8):
    wash = circ_filter(loop_noise(L, "brown"), lambda x: lp(x, 600))
    swell = 0.55 + 0.45 * np.sin(2 * np.pi * np.arange(L) / L * swells)
    wash = wash / np.abs(wash).max() * swell
    out = np.zeros((2, L))
    out[0] += wash * gain
    out[1] += np.roll(wash, SR // 3) * gain
    return out


def wind_bed(L, gain=0.035):
    w = circ_filter(loop_noise(L, "pink"), lambda x: bp(x, 250, 1800))
    mod = 0.5 + 0.5 * np.sin(2 * np.pi * np.arange(L) / L * 5 + 1.0)
    w = w / np.abs(w).max() * mod
    return np.vstack([w * gain, np.roll(w, SR // 2) * gain])


# ----------------------------------------------------------------------------- melody

def make_melody(prog, scale, rng, bars_per_phrase=2, register=72, density=0.55, beats=4):
    """
    A phrase-structured melody: build one 2-bar MOTIF from chord tones and scale steps, then
    lay it out A A' B A'' across the progression (A' = varied ending, B = new motif up a third),
    so it sounds composed rather than random. Returns [(beat, midi, length_beats, vel)].
    """
    def nearest(pc_set, target):
        cands = [n for n in range(register - 14, register + 17) if n % 12 in pc_set]
        return min(cands, key=lambda n: abs(n - target))

    def motif(start_bar, shift):
        root, tones = prog[start_bar % len(prog)]
        chord_pcs = {(root + t) % 12 for t in tones}
        scale_pcs = {(scale[0] + s) % 12 for s in scale[1]}
        notes, beat, cur = [], 0.0, register + shift
        total = bars_per_phrase * beats
        rhythm = [1.0, 0.5, 0.5, 1.0, 1.5, 0.5, 1.0, 2.0]
        k = 0
        while beat < total - 0.5:
            ln = rhythm[k % len(rhythm)] if rng.random() > 0.25 else rng.choice([0.5, 1.0])
            k += 1
            strong = abs(beat - round(beat)) < 1e-6 and int(round(beat)) % 2 == 0
            if rng.random() < density or strong:
                step = rng.choice([-4, -2, -1, 1, 2, 3, 5])
                pool = chord_pcs if strong else scale_pcs
                cur = nearest(pool, cur + step)
                cur = int(np.clip(cur, register - 9, register + 12))
                notes.append((beat, cur, min(ln, total - beat), 0.65 + 0.2 * strong))
            beat += ln
        return notes

    A = motif(0, 0)
    B = motif(len(prog) // 2, 4)
    out = []
    n_phr = len(prog) // bars_per_phrase
    for p in range(n_phr):
        sect = (p * 4) // n_phr               # 0,1,2,3 quarters of the loop
        src = B if sect == 2 else A
        base_beat = p * bars_per_phrase * beats
        for i, (b, m, ln, v) in enumerate(src):
            if sect == 1 and i == len(src) - 1:
                # A': resolve the last note onto the current chord's root instead
                root = prog[(p * bars_per_phrase) % len(prog)][0]
                m = min(range(m - 7, m + 8), key=lambda x: (x % 12 != root % 12, abs(x - m)))
            if sect == 3 and i == len(src) - 1:
                ln = max(ln, 1.5)
            out.append((base_beat + b, m, ln, v))
    return out


# ----------------------------------------------------------------------------- track engine

M7, m7, dom7, m9, maj9, add9, sus2, sus4 = ((0, 4, 7, 11), (0, 3, 7, 10), (0, 4, 7, 10),
                                            (0, 3, 7, 10, 14), (0, 4, 7, 11, 14), (0, 4, 7, 14),
                                            (0, 2, 7), (0, 5, 7))
MAJ, MIN = (0, 4, 7), (0, 3, 7)


def render(spec):
    bpm, bars = spec["bpm"], spec.get("bars", 32)
    beat = 60.0 / bpm
    L = int(round(bars * 4 * beat * SR))
    mix = {k: np.zeros((2, L)) for k in ("harm", "bass", "drums", "pad", "lead", "spark", "amb")}
    rng = np.random.default_rng(spec["seed"])

    def s(b):
        return int(round(b * beat * SR))

    prog = spec["prog"]
    while len(prog) < bars:
        prog = prog + prog
    prog = prog[:bars]

    for bar, (root, tones) in enumerate(prog):
        b0 = bar * 4
        section = (bar * 4) // bars            # 0..3
        voicing = [midi(root + spec.get("harm_oct", 12) + t) for t in tones]

        # --- harmony instrument
        h = spec["harm"]
        if h == "ep":
            for hit, length, vel in ((0.0, 1.3, 0.75), (1.5, 0.9, 0.55), (3.0, 0.8, 0.6)):
                for k, f in enumerate(voicing):
                    add(mix["harm"], s(b0 + hit) + k * 80, ep_note(f, length * beat, vel), 0.15,
                        pan=-0.25 + 0.5 * k / max(1, len(voicing) - 1))
        elif h in ("koto", "guitar"):
            order = voicing + [voicing[1] * 2] if len(voicing) > 2 else voicing
            patt = spec.get("arp", [0, 1, 2, 3, 2, 1, 0, 2])
            step = 4.0 / len(patt)
            for q, idx in enumerate(patt):
                f = order[idx % len(order)]
                add(mix["harm"], s(b0 + q * step), pluck(f, step * beat * 1.8,
                    0.55 if q % 2 else 0.7, bright=0.35 if h == "koto" else 0.6,
                    decay=0.994 if h == "koto" else 0.997), 0.20,
                    pan=-0.35 + 0.7 * (q % 4) / 3)
        elif h == "strings":
            add(mix["harm"], s(b0), strings(voicing, 4 * beat, spec.get("str_bright", 1800)), 0.22)

        # --- pad
        if spec.get("pad", True) and section >= spec.get("pad_from", 1):
            add(mix["pad"], s(b0), pad_chord([midi(root + 12 + t) for t in tones[:4]], 4 * beat),
                spec.get("pad_gain", 0.10))

        # --- bass
        r = midi(root - 12)
        for hit, mult, ln, v in spec["bass"]:
            add(mix["bass"], s(b0 + hit), bass_note(r * mult, ln * beat * 2, v), spec.get("bass_gain", 0.4))

        # --- drums
        if bar >= spec.get("drums_from", 2):
            for inst, hits, gain, pan in spec["drums"]:
                for k in hits:
                    add(mix["drums"], s(b0 + k), inst(), gain, pan=pan)
        for q in range(spec.get("hat_div", 8)):
            if spec.get("hats", True):
                sw = spec.get("swing", 0.0) if q % 2 else 0.0
                add(mix["drums"], s(b0 + q * 4 / spec.get("hat_div", 8) + sw), hat(),
                    0.2 if q % 2 else 0.27, pan=0.3)

        # --- sparkle bells
        if spec.get("bells") and section in spec["bells"]:
            arp = [root + 24 + t for t in tones[:4]]
            for q, nte in enumerate(arp + arp[::-1][1:3]):
                add(mix["spark"], s(b0 + 0.5 + q * 0.5), bell_note(midi(nte), 0.35), 0.11,
                    pan=0.4 * np.sin(q + bar))

    # --- lead melody over sections listed
    mel = make_melody(prog, spec["scale"], rng, register=spec.get("register", 74),
                      density=spec.get("density", 0.55))
    voice = spec.get("lead", "flute")
    for b, m, ln, v in mel:
        section = int((b / 4) * 4 // bars)
        if section not in spec.get("lead_sections", (1, 2, 3)):
            continue
        if voice == "flute":
            sig = flute(midi(m), ln * beat * 0.95, v)
        elif voice == "koto":
            sig = pluck(midi(m), ln * beat, v, bright=0.45, decay=0.995)
        else:
            sig = base.lead_note(midi(m), ln * beat * 0.95, v)
        add(mix["lead"], s(b), sig, spec.get("lead_gain", 0.13), pan=0.1)

    # --- ambience
    if spec.get("sea"):
        mix["amb"] += sea_bed(L, spec["sea"])
    if spec.get("wind"):
        mix["amb"] += wind_bed(L, spec["wind"])

    music = (mix["harm"] + mix["bass"] + mix["drums"] * spec.get("drum_gain", 0.9) + mix["pad"]
             + mix["lead"] + mix["spark"])
    music = reverb(music, spec.get("rev_s", 2.4), spec.get("rev_wet", 0.2)) + mix["amb"]
    music = np.tanh(music * 1.4) / np.tanh(1.4)
    music *= 0.89 / np.abs(music).max()
    return music


def T(root_midi, tones):
    return (root_midi, tones)


# ----------------------------------------------------------------------------- the six regions

YO = (0, 2, 5, 7, 9)            # yo pentatonic (major-ish, Japanese folk)
MAJ_PENT = (0, 2, 4, 7, 9)
MAJOR = (0, 2, 4, 5, 7, 9, 11)
LYDIAN = (0, 2, 4, 6, 7, 9, 11)
NAT_MIN = (0, 2, 3, 5, 7, 8, 10)

TRACKS = {
    # Sakura Pass - "Petals on the Climb": gentle, hopeful, Japanese folk colour.
    "SakuraPass": dict(
        seed=101, bpm=84, harm="koto", arp=[0, 2, 1, 3, 0, 2, 1, 2],
        prog=[T(43, sus2), T(40, (0, 3, 7)), T(36, add9), T(38, sus4),
              T(43, sus2), T(45, (0, 3, 7)), T(36, add9), T(38, sus2)],
        scale=(7, YO), register=76, lead="flute", lead_sections=(1, 2, 3), lead_gain=0.12,
        bass=[(0.0, 1, 1.5, 0.85), (2.0, 1.5, 1.0, 0.6)], bass_gain=0.34,
        drums=[(lambda: taiko(64, 0.9), (0.0, 2.5), 0.55, 0.0),
               (lambda: taiko(96, 0.6), (3.0, 3.5), 0.35, 0.25),
               (shaker, (1.0, 1.5, 3.0), 0.3, -0.3)],
        hats=False, pad_from=1, pad_gain=0.08, bells=(0, 2), wind=0.03, rev_s=2.8, rev_wet=0.26),

    # Shiosai Coast - "Ride the Breeze": bright seaside, plucked guitar and sea air.
    "ShiosaiCoast": dict(
        seed=202, bpm=104, harm="guitar", arp=[0, 1, 2, 3, 1, 2, 3, 2],
        prog=[T(45, add9), T(40, MAJ), T(42, m7), T(38, maj9),
              T(45, add9), T(40, MAJ), T(38, M7), T(40, sus4)],
        scale=(9, MAJ_PENT), register=76, lead="lead", lead_sections=(1, 2, 3), lead_gain=0.11,
        bass=[(0.0, 1, 0.9, 0.9), (1.5, 2, 0.4, 0.55), (2.5, 1.5, 0.45, 0.7), (3.5, 2, 0.3, 0.5)],
        drums=[(kick, (0.0, 2.5), 0.75, 0.0), (snare, (1.0, 3.0), 0.4, -0.05),
               (shaker, tuple(q * 0.25 for q in range(16)), 0.28, -0.35)],
        swing=0.03, bells=(0, 3), sea=0.06, rev_wet=0.2),

    # Maple City - "The Heart": funky, busy, confident city groove.
    "MapleCity": dict(
        seed=303, bpm=112, harm="ep",
        prog=[T(46, maj9), T(43, m9), T(39, maj9), T(41, dom7),
              T(46, maj9), T(43, m9), T(48, m7), T(41, (0, 5, 7, 10))],
        scale=(10, MAJ_PENT), register=74, lead="lead", lead_sections=(1, 2, 3), lead_gain=0.12,
        density=0.62,
        bass=[(0.0, 1, 0.35, 0.95), (0.75, 1, 0.2, 0.6), (1.5, 2, 0.25, 0.7), (2.0, 1, 0.3, 0.8),
              (2.75, 1.5, 0.2, 0.6), (3.5, 2, 0.25, 0.65)], bass_gain=0.42,
        drums=[(kick, (0.0, 1.5, 2.0, 3.25), 0.8, 0.0), (clap, (1.0, 3.0), 0.5, 0.0),
               (snare, (3.75,), 0.2, -0.1), (lambda: hat(True), (1.5, 3.5), 0.2, 0.3)],
        hat_div=16, swing=0.02, pad_from=2, pad_gain=0.08, bells=(3,), rev_s=1.9, rev_wet=0.16),

    # Azora Highlands - "Earn the View": airy, open, lydian lift.
    "AzoraHighlands": dict(
        seed=404, bpm=92, harm="strings", str_bright=2200,
        prog=[T(41, add9), T(43, MAJ), T(45, (0, 3, 7)), T(41, maj9),
              T(38, (0, 3, 7, 10)), T(43, MAJ), T(41, add9), T(36, sus4)],
        scale=(5, LYDIAN), register=77, lead="flute", lead_sections=(1, 2, 3), lead_gain=0.14,
        density=0.45,
        bass=[(0.0, 1, 1.8, 0.8), (2.0, 1.5, 1.8, 0.55)], bass_gain=0.32,
        drums=[(lambda: taiko(58, 0.8), (0.0,), 0.5, 0.0), (kick, (2.5,), 0.45, 0.0),
               (snare, (2.0,), 0.25, 0.0)],
        hat_div=8, pad_from=0, pad_gain=0.07, bells=(0, 2), wind=0.04, rev_s=3.2, rev_wet=0.3),

    # Taka Mountains - "The High Road": driving, heroic, minor-key climb.
    "TakaMountains": dict(
        seed=505, bpm=120, harm="strings", str_bright=2600,
        prog=[T(38, MIN), T(46, MAJ), T(41, MAJ), T(45, MAJ),
              T(38, MIN), T(43, MIN), T(46, MAJ), T(45, (0, 4, 7, 10))],
        scale=(2, NAT_MIN), register=74, lead="lead", lead_sections=(1, 2, 3), lead_gain=0.14,
        density=0.6,
        bass=[(q * 0.5, 1 if q % 4 != 3 else 2, 0.22, 0.85 if q % 2 == 0 else 0.6) for q in range(8)],
        bass_gain=0.36,
        drums=[(lambda: taiko(55, 1.0), (0.0, 1.5, 2.0), 0.7, 0.0),
               (lambda: taiko(88, 0.7), (0.75, 2.75, 3.5, 3.75), 0.4, 0.2),
               (snare, (1.0, 3.0), 0.45, -0.05), (kick, (0.0, 2.0), 0.5, 0.0)],
        hat_div=8, pad_from=2, pad_gain=0.07, bells=None, rev_s=2.2, rev_wet=0.18, drums_from=1),

    # Fuji Ridge - "Clouds Above": slow, ethereal, floating over the cloud sea.
    "FujiRidge": dict(
        seed=606, bpm=72, harm="strings", str_bright=1500, bars=24,
        prog=[T(40, maj9), T(45, add9), T(37, (0, 3, 7, 10)), T(45, maj9),
              T(40, add9), T(44, (0, 3, 7, 10)), T(45, M7), T(47, sus4)],
        scale=(4, MAJ_PENT), register=79, lead="flute", lead_sections=(1, 2, 3), lead_gain=0.12,
        density=0.38,
        bass=[(0.0, 1, 3.8, 0.7)], bass_gain=0.28,
        drums=[(lambda: taiko(50, 0.6), (0.0,), 0.35, 0.0)],
        hats=False, drums_from=4, pad_from=0, pad_gain=0.11, bells=(0, 1, 2, 3),
        wind=0.045, rev_s=4.0, rev_wet=0.34),
}


def main():
    want = sys.argv[1:] or list(TRACKS)
    for name in want:
        spec = TRACKS[name]
        base.RNG = np.random.default_rng(spec["seed"] + 7)   # shared voices draw from base.RNG
        music = render(spec)
        write_wav(f"BGM_{name}.wav", music)


if __name__ == "__main__":
    main()

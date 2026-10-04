"""
MapleRide - offline renderer for the Minato Coast BGM and the bike sound loops (2026-09-25).

The project ships no licensed audio (see Assets/Ride/UI/MapleRideTitleBgm.cs, which synthesises
the title theme live). Everything here is original and synthesised with numpy, rendered once to
WAV so the game pays no runtime synthesis cost and the mix can use reverb.

Outputs (Assets/Resources/Audio/, loaded by Assets/Ride/Audio/RideAudio.cs):
  BGM_MinatoCoast.wav   ~80 s seamless stereo loop: relaxed city-pop harbour groove, 96 BPM, D major.
  Bike_Drivetrain.wav   2 s loop, chain-over-cassette whir + pedal-stroke pulse at 90 rpm (pitch = cadence/90).
  Bike_Freewheel.wav    1 s loop, hub ratchet ticks at 64/s (pitch = tick rate / 64).
  Bike_TyreRoll.wav     3 s loop, tyre on asphalt rumble (volume/pitch by speed).
  Bike_Wind.wav         4 s loop, air rush past the ears (volume ~ speed^2).

Run: python tools/audio/make_ride_audio.py
"""

import os
import wave

import numpy as np
from scipy import signal

SR = 44100
RNG = np.random.default_rng(20260925)
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "Audio")


# ----------------------------------------------------------------------------- helpers

def midi(n):
    return 440.0 * 2.0 ** ((n - 69) / 12.0)


def env_adsr(n, a, d, s, r, sustain_len):
    """Sample-count ADSR (seconds in), total length n samples."""
    out = np.zeros(n)
    a_n, d_n, r_n = int(a * SR), int(d * SR), int(r * SR)
    s_n = max(0, int(sustain_len * SR) - a_n - d_n)
    seg = []
    seg.append(np.linspace(0, 1, max(a_n, 1), endpoint=False))
    seg.append(np.linspace(1, s, max(d_n, 1), endpoint=False))
    seg.append(np.full(s_n, s))
    seg.append(np.linspace(s, 0, max(r_n, 1)))
    e = np.concatenate(seg)[:n]
    out[:len(e)] = e
    return out


def lp(x, fc, order=2):
    b, a = signal.butter(order, min(fc / (SR / 2), 0.99), "low")
    return signal.lfilter(b, a, x)


def hp(x, fc, order=2):
    b, a = signal.butter(order, fc / (SR / 2), "high")
    return signal.lfilter(b, a, x)


def bp(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), min(hi / (SR / 2), 0.99)], "band")
    return signal.lfilter(b, a, x)


def add(buf, start, sig, gain=1.0, pan=0.0):
    """Mix a mono signal into a stereo buffer with constant-power pan, wrapping at the end so
    note tails that cross the loop point come back in at the start (seamless loop)."""
    L = buf.shape[1]
    th = (pan + 1) * np.pi / 4
    gl, gr = np.cos(th) * gain, np.sin(th) * gain
    idx = (start + np.arange(len(sig))) % L
    np.add.at(buf[0], idx, sig * gl)
    np.add.at(buf[1], idx, sig * gr)


def write_wav(name, data):
    """data: (channels, n) float in [-1, 1]."""
    os.makedirs(OUT, exist_ok=True)
    data = np.atleast_2d(data)
    pcm = (np.clip(data, -1, 1) * 32767).astype(np.int16).T.copy()
    path = os.path.abspath(os.path.join(OUT, name))
    with wave.open(path, "wb") as w:
        w.setnchannels(data.shape[0])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"[audio] {name}: {data.shape[1] / SR:5.1f} s, {data.shape[0]} ch, "
          f"peak {np.abs(data).max():.2f}, rms {np.sqrt((data ** 2).mean()):.3f}")


def loop_noise(n, color="pink"):
    """Periodic noise of exactly n samples (FFT-shaped, so the loop point is seamless)."""
    spec = RNG.normal(size=n // 2 + 1) + 1j * RNG.normal(size=n // 2 + 1)
    f = np.fft.rfftfreq(n, 1 / SR)
    f[0] = f[1]
    if color == "pink":
        spec /= np.sqrt(f)
    elif color == "brown":
        spec /= f
    x = np.fft.irfft(spec, n)
    return x / np.abs(x).max()


def circ_filter(x, fn):
    """Apply a causal filter to a loop without a seam: filter 3 copies, keep the middle."""
    y = fn(np.concatenate([x, x, x]))
    n = len(x)
    return y[n:2 * n]


# ----------------------------------------------------------------------------- instruments

def ep_note(f, dur, vel=0.8):
    """Electric-piano (Rhodes-like) FM voice: 1:1 body + 14:1 tine bell, both decaying."""
    n = int((dur + 1.6) * SR)
    t = np.arange(n) / SR
    idx_body = 1.6 * vel * np.exp(-t * 2.2)
    idx_tine = 0.9 * vel * np.exp(-t * 14.0)
    mod = idx_body * np.sin(2 * np.pi * f * t) + idx_tine * np.sin(2 * np.pi * f * 14.0 * t)
    x = np.sin(2 * np.pi * f * t + mod)
    amp = np.exp(-t * (1.1 + f / 900.0))
    rel = np.clip((dur + 0.25 - t) / 0.25, 0, 1) ** 2
    trem = 1.0 + 0.08 * np.sin(2 * np.pi * 4.6 * t)
    return x * amp * np.maximum(rel, 0) * trem * vel


def bass_note(f, dur, vel=0.9):
    n = int((dur + 0.3) * SR)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) + 0.12 * np.sin(6 * np.pi * f * t)
    e = np.minimum(t / 0.006, 1) * np.exp(-t * 1.8) * np.clip((dur - t) / 0.06 + 1, 0, 1)
    return lp(x * e, 900) * vel


def pad_chord(freqs, dur):
    n = int((dur + 1.0) * SR)
    t = np.arange(n) / SR
    x = np.zeros(n)
    for f in freqs:
        for det in (-0.12, 0.0, 0.12):
            ph = RNG.uniform(0, 2 * np.pi)
            ff = f * 2 ** (det / 12)
            x += signal.sawtooth(2 * np.pi * ff * t + ph)
    x = lp(x, 1400, 2)
    e = env_adsr(n, 0.8, 0.5, 0.8, 1.0, dur)
    return x * e / (len(freqs) * 3)


def lead_note(f, dur, vel=0.7):
    """Soft synth lead: triangle + a little square, delayed vibrato."""
    n = int((dur + 0.4) * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.005 * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.15) / 0.2, 0, 1)
    ph = 2 * np.pi * f * np.cumsum(vib) / SR
    x = 0.8 * signal.sawtooth(ph, 0.5) + 0.2 * signal.square(ph)
    x = lp(x, 3200)
    e = env_adsr(n, 0.02, 0.15, 0.7, 0.3, dur)
    return x * e * vel


def bell_note(f, vel=0.5):
    n = int(2.2 * SR)
    t = np.arange(n) / SR
    x = np.sin(2 * np.pi * f * t + 1.2 * np.exp(-t * 6) * np.sin(2 * np.pi * f * 3.5 * t))
    return x * np.exp(-t * 2.6) * vel


def kick():
    n = int(0.45 * SR)
    t = np.arange(n) / SR
    f = 48 + 90 * np.exp(-t * 28)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 7.5)
    click = RNG.normal(size=n) * np.exp(-t * 400) * 0.15
    return (x + click) * 0.95


def snare():
    n = int(0.3 * SR)
    t = np.arange(n) / SR
    body = np.sin(2 * np.pi * 190 * t) * np.exp(-t * 22) * 0.5
    nz = bp(RNG.normal(size=n), 1500, 9000) * np.exp(-t * 16) * 0.8
    return body + nz


def hat(open_=False):
    n = int((0.35 if open_ else 0.06) * SR)
    t = np.arange(n) / SR
    x = hp(RNG.normal(size=n), 7000, 2)
    return x * np.exp(-t * (9 if open_ else 70)) * 0.5


def shaker():
    n = int(0.08 * SR)
    t = np.arange(n) / SR
    x = bp(RNG.normal(size=n), 4000, 11000)
    return x * np.sin(np.pi * np.clip(t / 0.08, 0, 1)) ** 2 * 0.25


def reverb(stereo, seconds=2.2, wet=0.22):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    out = np.zeros_like(stereo)
    for ch in range(2):
        ir = RNG.normal(size=n) * np.exp(-t * 6.9 / seconds)
        ir = lp(ir, 5200)
        ir[: int(0.012 * SR)] = 0          # pre-delay
        ir /= np.sqrt((ir ** 2).sum())
        L = stereo.shape[1]
        wetsig = signal.fftconvolve(stereo[ch], ir)
        folded = wetsig[:L].copy()
        folded[: len(wetsig) - L] += wetsig[L:]   # wrap the tail -> seamless loop
        out[ch] = stereo[ch] * (1 - wet) + folded * wet * 2.0
    return out


# ----------------------------------------------------------------------------- BGM

def bgm_minato():
    bpm = 96.0
    beat = 60.0 / bpm
    bars = 32
    L = int(round(bars * 4 * beat * SR))
    mix = {k: np.zeros((2, L)) for k in ("ep", "bass", "drums", "pad", "lead", "bell", "sea")}

    def s(b):  # beat -> sample
        return int(round(b * beat * SR))

    # D major city-pop. (root midi, chord tones as semitone offsets from root)
    M7, m7, dom9, m9, maj9 = (0, 4, 7, 11), (0, 3, 7, 10), (0, 4, 7, 10, 14), (0, 3, 7, 10, 14), (0, 4, 7, 11, 14)
    A = [(43, M7), (42, m7), (40, m9), (45, dom9), (47, m7), (40, m9), (43, M7), (45, (0, 5, 7, 10))]
    B = [(43, maj9), (45, dom9), (42, m7), (47, m9), (40, m9), (45, dom9), (38, maj9), (38, maj9)]
    prog = A + A + B + A          # 32 bars

    for bar, (root, tones) in enumerate(prog):
        b0 = bar * 4
        section = bar // 8                       # 0 A, 1 A', 2 B, 3 A''
        # --- electric piano comping: voicing around middle C, syncopated
        voicing = [midi(root + 12 + t) for t in tones]
        for hit, length, vel in ((0.0, 1.3, 0.75), (1.5, 0.9, 0.55), (3.0, 0.8, 0.6)):
            for k, f in enumerate(voicing):
                add(mix["ep"], s(b0 + hit) + k * 90, ep_note(f, length * beat, vel), 0.16,
                    pan=-0.25 + 0.5 * k / max(1, len(voicing) - 1))
        # --- bass
        r = midi(root - 12)
        pat = [(0.0, r, 0.9, 0.95), (1.5, r * 2, 0.4, 0.6), (2.5, r * 1.5, 0.45, 0.7),
               (3.5, midi(prog[(bar + 1) % bars][0] - 13), 0.35, 0.55)]
        for hit, f, ln, v in pat:
            add(mix["bass"], s(b0 + hit), bass_note(f, ln * beat * 2, v), 0.42)
        # --- pad (sections 1..3)
        if section >= 1:
            add(mix["pad"], s(b0), pad_chord([midi(root + 12 + t) for t in tones[:4]], 4 * beat), 0.10)
        # --- drums: intro bars 0-1 light, then full groove
        for q in range(8):
            swing = 0.06 if q % 2 else 0.0
            add(mix["drums"], s(b0 + q * 0.5 + swing), hat(), 0.22 if q % 2 else 0.3,
                pan=0.3)
        if bar >= 2:
            for k in (0.0, 1.75, 2.5):
                add(mix["drums"], s(b0 + k), kick(), 0.8)
            for k in (1.0, 3.0):
                add(mix["drums"], s(b0 + k), snare(), 0.45, pan=-0.05)
            for q in range(16):
                add(mix["drums"], s(b0 + q * 0.25), shaker(), 0.5 if q % 4 == 2 else 0.3, pan=-0.35)
            if section == 2 and bar % 2 == 1:
                add(mix["drums"], s(b0 + 3.5), hat(True), 0.25, pan=0.3)
        # --- bell arpeggio sparkle in the A sections
        if section in (0, 3):
            arp = [root + 24 + t for t in tones[:4]]
            for q, nte in enumerate(arp + arp[::-1][1:3]):
                add(mix["bell"], s(b0 + 0.5 + q * 0.5), bell_note(midi(nte), 0.35), 0.12,
                    pan=0.4 * np.sin(q))

    # --- lead melody in A' and B (bars 8-23): a lilting D-major pentatonic line
    phrase = [  # (beat offset within 2 bars, midi, length beats)
        (0.0, 78, 1.0), (1.0, 76, 0.5), (1.5, 74, 1.0), (2.5, 76, 0.5), (3.0, 81, 1.5),
        (5.0, 78, 0.5), (5.5, 76, 0.5), (6.0, 74, 1.5), (7.5, 71, 0.5)]
    phrase_b = [
        (0.0, 83, 1.5), (1.5, 81, 0.5), (2.0, 78, 1.0), (3.0, 76, 1.0), (4.0, 78, 2.0),
        (6.0, 81, 0.5), (6.5, 83, 0.5), (7.0, 85, 1.0)]
    for bar in range(8, 24, 2):
        ph = phrase_b if bar >= 16 else phrase
        for off, nte, ln in ph:
            add(mix["lead"], s(bar * 4 + off), lead_note(midi(nte), ln * beat * 0.95, 0.7), 0.12, pan=0.1)

    # --- harbour ambience bed: slow sea wash (periodic so it loops)
    wash = circ_filter(loop_noise(L, "brown"), lambda x: lp(x, 600))
    swell = 0.55 + 0.45 * np.sin(2 * np.pi * np.arange(L) / L * 8)
    wash = wash / np.abs(wash).max() * swell
    mix["sea"][0] += wash * 0.05
    mix["sea"][1] += np.roll(wash, SR // 3) * 0.05

    music = mix["ep"] + mix["bass"] + mix["drums"] * 0.9 + mix["pad"] + mix["lead"] + mix["bell"]
    music = reverb(music, 2.4, 0.20) + mix["sea"]
    # Gentle glue: soft-knee saturation then normalise.
    music = np.tanh(music * 1.4) / np.tanh(1.4)
    music *= 0.89 / np.abs(music).max()
    return music


# ----------------------------------------------------------------------------- bike loops

def bike_drivetrain():
    """A clean, lubed road drivetrain is nearly SILENT under load (Sheldon Brown, Park Tool):
    only a soft, smooth chain whir. v1 had a 75 Hz roller pulse train + a 28 % pedal-stroke
    swell and read as an exaggerated mechanical "whomp" (user playtest). Now: band-limited hiss
    2-7 kHz with a faint 1.5 kHz roller tone and only 6 % stroke modulation."""
    L = 2 * SR                                 # 3 pedal revs at 90 rpm = 2.0 s exactly
    t = np.arange(L) / SR
    hiss = circ_filter(loop_noise(L, "pink"), lambda x: bp(x, 2000, 7000, 2))
    hiss /= np.abs(hiss).max()
    # Faint tonal trace of the rollers meshing (75 hits/s -> harmonic near 1.5 kHz), very low.
    tone = np.sin(2 * np.pi * 1500.0 * t) * 0.04 * (1 + 0.3 * np.sin(2 * np.pi * 75.0 * t))
    stroke = 0.94 + 0.06 * np.sin(2 * np.pi * 3.0 * t) ** 2
    x = (hiss * 0.8 + tone) * stroke
    return x / np.abs(x).max() * 0.6


def bike_freewheel():
    """Hub pawl ratchet: 64 ticks per second (pitch-shifted at runtime by wheel speed)."""
    L = SR
    x = np.zeros(L)
    tick_n = int(0.012 * SR)
    tt = np.arange(tick_n) / SR
    for k in range(64):
        i = int(round(k * SR / 64.0))
        f1 = 3400 * (1 + RNG.normal() * 0.03)
        tick = (np.sin(2 * np.pi * f1 * tt) * 0.6 + np.sin(2 * np.pi * f1 * 2.37 * tt) * 0.3
                + RNG.normal(size=tick_n) * 0.4) * np.exp(-tt * 900)
        tick *= 0.8 + 0.2 * RNG.random()
        idx = (i + np.arange(tick_n)) % L
        x[idx] += tick
    x = circ_filter(x, lambda s: hp(s, 1200))
    return x / np.abs(x).max() * 0.8


def bike_tyre_roll():
    """The dominant riding sound: tyre "whoosh" on asphalt - low rumble plus road-texture hiss
    (1-4 kHz) that brightens with speed via runtime pitch."""
    L = 3 * SR
    rumble = circ_filter(loop_noise(L, "brown"), lambda x: lp(x, 380))
    whoosh = circ_filter(loop_noise(L, "pink"), lambda x: bp(x, 900, 4200))
    x = rumble / np.abs(rumble).max() * 0.75 + whoosh / np.abs(whoosh).max() * 0.35
    return x / np.abs(x).max() * 0.8


def bike_wind():
    L = 4 * SR
    t = np.arange(L) / SR
    n = circ_filter(loop_noise(L, "pink"), lambda x: bp(x, 150, 3000))
    gust = 0.75 + 0.25 * np.sin(2 * np.pi * t / 4.0) * np.sin(2 * np.pi * 0.5 * t + 1.0)
    x = n * gust
    return x / np.abs(x).max() * 0.8


def main():
    import sys
    if "--bike-only" not in sys.argv:
        write_wav("BGM_MinatoCoast.wav", bgm_minato())
    write_wav("Bike_Drivetrain.wav", bike_drivetrain())
    write_wav("Bike_Freewheel.wav", bike_freewheel())
    write_wav("Bike_TyreRoll.wav", bike_tyre_roll())
    write_wav("Bike_Wind.wav", bike_wind())


if __name__ == "__main__":
    main()

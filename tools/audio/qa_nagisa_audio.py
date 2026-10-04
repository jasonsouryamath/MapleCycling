"""Objective QA for the Nagisa Bay audio (worker H): duration, peak, RMS, DC, clipping, loop seam, spectrum, tempo.
Run: python tools/audio/qa_nagisa_audio.py"""
import os, sys, glob
import numpy as np
from scipy import signal
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nagisa_dsp as D

LOOPS = {"surf_loop", "distant_breakers", "marina_lap", "beach_crowd", "traffic_far", "marina_engine", "marina_outboard",
         "rigging", "wind_loop", "wind_ridge", "wind_sea", "cicadas", "cafe_loop", "plaza_loop", "bike_park_loop"}
fails = []


def seam(x):
    """Jump across the loop point vs the largest ordinary step within +-0.5 s (so a smooth bass is judged fairly),
    plus the 0.25 s RMS on either side of the seam (a loop must not step in level)."""
    w = int(0.5 * D.SR)
    loc = np.abs(np.diff(np.concatenate([x[-w:], x[:w]])))
    jump = abs(x[0] - x[-1])
    ordinary = np.sort(np.delete(loc, w - 1))[-max(3, len(loc) // 200)]   # ~99.5th percentile step
    w2 = int(0.25 * D.SR)
    a, b = np.sqrt(np.mean(x[-w2:] ** 2)), np.sqrt(np.mean(x[:w2] ** 2))
    return jump / (ordinary + 1e-9), max(a, b) / (min(a, b) + 1e-9)


def band(x, lo, hi):
    f, p = signal.welch(x, D.SR, nperseg=4096)
    m = (f >= lo) & (f < hi)
    return p[m].sum() / (p.sum() + 1e-18)


def check(path, loop):
    a, sr = D.read_wav(path)
    x = a.mean(axis=0); name = os.path.basename(path)[:-4]
    pk = np.abs(a).max(); rms = np.sqrt((a ** 2).mean()); dc = abs(a.mean())
    msg = f"{name:24s} {a.shape[1] / sr:6.2f}s ch={a.shape[0]} pk={pk:.2f} rms={rms:.3f} dc={dc:.4f}"
    ok = True
    if sr != D.SR: ok = False; msg += " BAD-SR"
    if pk > 0.98: ok = False; msg += " CLIP"
    if rms < 0.004: ok = False; msg += " SILENT"
    if dc > 0.01: ok = False; msg += " DC"
    if loop:
        j, e = seam(x)
        msg += f" seam jump x{j:.1f} energy x{e:.2f}"
        if j > 1.5 or e > 3.0: ok = False; msg += " SEAM?"
    msg += "  bands lo/mid/hi(<250/250-4k/>4k)=%.2f/%.2f/%.2f" % (band(x, 0, 250), band(x, 250, 4000), band(x, 4000, 16000))
    print(("PASS " if ok else "FAIL ") + msg)
    if not ok: fails.append(name)
    return a


for p in sorted(glob.glob(os.path.join(D.OUT, "Amb", "*.wav"))):
    check(p, os.path.basename(p)[:-4] in LOOPS)

stems = {}
for p in sorted(glob.glob(os.path.join(D.OUT, "Music", "*.wav"))):
    a = check(p, True)
    stems[os.path.basename(p)[:-4]] = a
    if abs(a.shape[1] / D.SR - 64.0) > 1e-6: fails.append(p + " length")

if stems:
    n = min(v.shape[1] for v in stems.values())
    mix = np.zeros((2, n))
    for v in stems.values(): mix += v[:2] if v.shape[0] >= 2 else np.vstack([v[0], v[0]])
    print(f"ALL-STEMS-FULL mix: peak={np.abs(mix).max():.2f} rms={np.sqrt((mix ** 2).mean()):.3f}", "(OK)" if np.abs(mix).max() < 0.98 else "(CLIPS)")
    # tempo: onset-envelope autocorrelation of the percussion stem
    if "Nagisa_perc" in stems:
        x = stems["Nagisa_perc"].mean(axis=0)
        env = np.abs(signal.hilbert(hp := D.hp(x, 4000)))
        env = signal.resample_poly(env, 1, 160)            # 200 Hz
        env -= env.mean()
        ac = np.correlate(env, env, "full")[len(env) - 1:]
        lo, hi = int(0.30 * 200), int(1.0 * 200)
        lag = lo + np.argmax(ac[lo:hi]); print(f"perc stem dominant period {lag / 200:.3f}s = {60 / (lag / 200):.1f} BPM (expect 120 or 240)")
    # pitch-class energy of keys+lead: A major expected
    k = (stems["Nagisa_keys"].mean(axis=0) + stems["Nagisa_lead"].mean(axis=0))
    spec = np.abs(np.fft.rfft(k * np.hanning(len(k)))); fr = np.fft.rfftfreq(len(k), 1 / D.SR)
    pc = np.zeros(12)
    for f_, m_ in zip(fr, spec):
        if 55 < f_ < 2000: pc[int(round(12 * np.log2(f_ / 440) + 69)) % 12] += m_ ** 2
    pc /= pc.sum(); names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
    diat = {9, 11, 1, 2, 4, 6, 8}
    print("pitch-class energy:", " ".join(f"{names[i]}={pc[i]:.2f}" for i in range(12)), "| in A-major scale: %.0f%%" % (100 * sum(pc[i] for i in diat)))
print("FAILS:", fails if fails else "none")
sys.exit(1 if fails else 0)

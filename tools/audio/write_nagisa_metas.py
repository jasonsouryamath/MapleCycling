"""Writes Unity .meta importer settings for the generated Nagisa audio: no per-clip normalisation (stem balance is
baked), preload on, Vorbis, ambience forced to mono. If Unity already generated a .meta (default settings), its GUID
is kept and only the importer block is rewritten. Run after the build scripts."""
import os, re, uuid, glob
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASE = os.path.join(ROOT, "Assets", "Resources", "Audio", "Nagisa")

AUDIO = """fileFormatVersion: 2
guid: @GUID@
AudioImporter:
  externalObjects: {}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: @LOAD@
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: @Q@
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {}
  forceToMono: @MONO@
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: @THREE@
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write_meta(path, load, q, mono, three):
    meta = path + ".meta"
    guid = uuid.uuid4().hex
    if os.path.exists(meta):
        m = re.search(r"guid:\s*([0-9a-f]{32})", open(meta).read())
        if m: guid = m.group(1)
    text = (AUDIO.replace("@GUID@", guid).replace("@LOAD@", str(load)).replace("@Q@", q)
            .replace("@MONO@", str(mono)).replace("@THREE@", str(three)))
    old = open(meta).read() if os.path.exists(meta) else None
    if old != text:
        open(meta, "w", newline="\n").write(text)
        return 1
    return 0


n = 0
for p in glob.glob(os.path.join(BASE, "Amb", "*.wav")):
    n += write_meta(p, 1 if os.path.getsize(p) > 400000 else 0, "0.55", 1, 1)
for p in glob.glob(os.path.join(BASE, "Music", "*.wav")):
    n += write_meta(p, 1, "0.65", 0, 0)
print("meta files written/updated:", n)

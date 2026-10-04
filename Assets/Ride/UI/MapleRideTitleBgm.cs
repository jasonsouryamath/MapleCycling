using System;
using UnityEngine;

/// <summary>
/// Original procedural title music: bright, fast Japanese-Eurobeat energy without depending on
/// a licensed music file. It lives with the title root, so it starts only on the title screen
/// and stops the instant that screen is dismissed.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapleRideTitleBgm : MonoBehaviour
{
    [Range(0f, 0.5f)] public float volume = 0.30f;
    [Range(120f, 180f)] public float bpm = 154f;

    [Tooltip("PROVISIONAL: semitone transpose. The game-selection cue uses a non-zero value so " +
             "it reads as a different piece from the title theme without a second audio file.")]
    [Range(-12, 12)] public int transposeSemitones = 0;

    private static readonly int[] ChordRoots = { 40, 36, 43, 38 }; // E minor, C, G, D
    private static readonly int[] LeadPattern = { 12, 14, 16, 19, 16, 14, 11, 12 };

    private AudioSource _source;
    private AudioClip _clip;
    private int _sampleRate;
    private long _sampleCursor;

    private void Awake()
    {
        _sampleRate = Mathf.Max(44100, AudioSettings.outputSampleRate);
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = true;
        _source.spatialBlend = 0f;
        _source.volume = 1f;

        // Streaming keeps the project small: the composition is generated as audio is needed,
        // instead of adding a large uneditable placeholder WAV to source control.
        _clip = AudioClip.Create("MapleRide_Original_Title_Eurobeat", _sampleRate * 2, 2,
                                 _sampleRate, true, FillAudio, IgnoreSeek);
        _source.clip = _clip;
        _source.Play();
    }

    private void Start()
    {
        // Sakura Pass currently has no listener. Check after the first scene finishes loading so
        // a future scene-supplied listener wins; only the title root supplies one as a fallback.
        if (FindAnyObjectByType<AudioListener>() == null)
            gameObject.AddComponent<AudioListener>();
    }

    private void OnDestroy()
    {
        if (_source != null) _source.Stop();
        if (_clip != null) Destroy(_clip);
    }

    private void IgnoreSeek(int position)
    {
        // The stream clip itself loops every two seconds. The musical position is kept in our
        // own cursor so a loop boundary cannot restart a drum fill or melody mid-phrase.
    }

    private void FillAudio(float[] data)
    {
        const int channels = 2;
        double secondsPerBeat = 60.0 / bpm;
        long samplesPerLoop = (long)Math.Round(_sampleRate * secondsPerBeat * 64.0);

        for (int i = 0; i < data.Length; i += channels)
        {
            long absoluteSample = _sampleCursor++;
            long loopSample = absoluteSample % samplesPerLoop;
            double beat = loopSample / (double)_sampleRate / secondsPerBeat;
            double beatFraction = beat - Math.Floor(beat);
            int bar = ((int)Math.Floor(beat / 4.0)) & 3;
            int root = ChordRoots[bar] + transposeSemitones;

            float kick = Kick(beatFraction * secondsPerBeat);
            float snare = Snare(beat, absoluteSample, secondsPerBeat);
            float hats = Hats(beat, absoluteSample, secondsPerBeat);
            float bass = Bass(beat, root);
            float chords = Chords(beat, root);
            float lead = Lead(beat, root, bar);

            // Kick used to dominate (0.60) with the hats/chords/lead buried under it, which read as
            // a muffled thump. Pulled the low end back and lifted the top so the melody is clear.
            float mix = (kick * 0.42f) + (snare * 0.30f) + (hats * 0.20f) +
                        (bass * 0.26f) + (chords * 0.20f) + (lead * 0.30f);
            float fadeIn = Mathf.Clamp01(absoluteSample / (_sampleRate * 1.5f));
            mix = 0.92f * (float)Math.Tanh(mix * volume * fadeIn / 0.92f);   // soft limit, no hard clipping

            // Slight motion gives the synths width while the kick stays centred.
            float width = 0.06f * (float)Math.Sin(beat * Math.PI * 0.5);
            data[i] = mix * (1f - width);
            if (i + 1 < data.Length) data[i + 1] = mix * (1f + width);
        }
    }

    private static float Kick(double seconds)
    {
        if (seconds > 0.22) return 0f;
        double sweep = 145.0 * Math.Exp(-seconds * 23.0) + 53.0;
        return (float)(Math.Sin(Math.PI * 2.0 * sweep * seconds) * Math.Exp(-seconds * 19.0));
    }

    private static float Snare(double beat, long sample, double secondsPerBeat)
    {
        int beatInBar = ((int)Math.Floor(beat)) & 3;
        if (beatInBar != 1 && beatInBar != 3) return 0f;
        double seconds = (beat - Math.Floor(beat)) * secondsPerBeat;
        if (seconds > 0.17) return 0f;
        return Noise(sample * 3 + 19) * (float)Math.Exp(-seconds * 26.0);
    }

    private static float Hats(double beat, long sample, double secondsPerBeat)
    {
        double eighth = beat * 2.0;
        double seconds = (eighth - Math.Floor(eighth)) * secondsPerBeat * 0.5;
        if (seconds > 0.055) return 0f;
        return Noise(sample * 11 + 7) * (float)Math.Exp(-seconds * 64.0);
    }

    private float Bass(double beat, int root)
    {
        double eighth = beat * 2.0;
        int step = (int)Math.Floor(eighth);
        double phaseInStep = eighth - step;
        int note = root + ((step & 3) == 3 ? 7 : 0);
        float frequency = MidiToHz(note);
        double phase = beat * (60.0 / bpm) * frequency;
        float envelope = (float)Math.Exp(-phaseInStep * 4.2);
        return Saw(phase) * envelope;
    }

    private float Chords(double beat, int root)
    {
        double phaseInBeat = beat - Math.Floor(beat);
        float envelope = 0.52f + 0.48f * (float)Math.Exp(-phaseInBeat * 2.2);
        double seconds = beat * (60.0 / bpm);
        float triad = Saw(seconds * MidiToHz(root + 12)) +
                      Saw(seconds * MidiToHz(root + 15)) +
                      Saw(seconds * MidiToHz(root + 19));
        return triad * envelope / 3f;
    }

    private float Lead(double beat, int root, int bar)
    {
        // Let the first half establish the groove, then answer it with an upbeat eight-note hook.
        if ((bar & 1) == 0) return 0f;
        double eighth = beat * 2.0;
        int step = (int)Math.Floor(eighth);
        double phaseInStep = eighth - step;
        int note = root + LeadPattern[step & 7];
        float frequency = MidiToHz(note);
        double seconds = beat * (60.0 / bpm);
        float envelope = (float)Math.Exp(-phaseInStep * 3.8);
        float brightSaw = Saw(seconds * frequency);
        float octave = 0.38f * Saw(seconds * frequency * 2.0);
        return (brightSaw + octave) * envelope;
    }

    private static float MidiToHz(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

    private static float Saw(double phase) => (float)(2.0 * (phase - Math.Floor(phase + 0.5)));

    private static float Noise(long index)
    {
        uint value = (uint)index;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return (value & 0xFFFF) / 32767.5f - 1f;
    }
}

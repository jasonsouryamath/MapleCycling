using UnityEngine;

/// <summary>
/// Procedural synth-wave / city-pop loop for Shunta Metro, rendered in OnAudioFilterRead (no assets, no audio-thread
/// allocation). 4-bar progression Am9 - Fmaj7 - Cmaj7 - G6, 16 steps per bar: detuned saw pad, octave-jumping saw/sub
/// bass, plucked arpeggio, four-on-the-floor kick with side-chain pump, snare and hats. Tempo (BPM) and brightness
/// (filter cutoff) are set by the director from the time-of-day arc and the rider's effort; layer gains come from the
/// zone map. <see cref="TunnelFx"/> darkens the output (low-pass) and adds a longer echo.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class ShuntaMusicSynth : MonoBehaviour
{
    public const int Layers = ShuntaAudioMap.MusCount;
    public readonly float[] Target = new float[Layers];
    public float Bpm = 104f, Brightness = 0.6f, Effort = 0f, TunnelFx, Master = 0.5f;

    private static readonly int[][] Chords =
    {
        new[] { 57, 60, 64, 67 }, new[] { 53, 57, 60, 64 }, new[] { 55, 60, 64, 67 }, new[] { 55, 59, 62, 64 },
    };
    private static readonly int[] BassRoot = { 33, 29, 36, 31 };
    private static readonly int[] BassPat = { 0, -1, 12, 0, -1, 0, 12, -1, 0, -1, 12, 0, -1, 7, 12, -1 };
    private static readonly int[] ArpPat = { 0, 1, 2, 3, 2, 1, 2, 3, 0, 1, 2, 3, 2, 3, 2, 1 };

    private float[] _g0, _g1;
    private float _sr = 48000f, _bpm = 104f, _bright = 0.6f, _fx, _eff;
    private bool _ready;
    private uint _rng = 88172645u;

    private int _step, _bar;
    private float _stepLeft;
    private readonly float[] _padPh = new float[8], _padF = new float[4];
    private float _bassPh, _bassF, _bassEnv, _arpPh, _arpF, _arpEnv;
    private float _kickEnv, _kickPh, _snEnv, _hatEnv, _duck;
    private float _pLpL1, _pLpL2, _pLpR1, _pLpR2, _aLp1, _aLp2, _bLp, _hLp, _sLp;
    private float _outL, _outR;
    private float[] _echo; private int _echoPos;

    private void Awake()
    {
        _sr = AudioSettings.outputSampleRate;
        _g0 = new float[Layers]; _g1 = new float[Layers];
        _echo = new float[(int)(_sr * 0.375f) + 8];
        _bpm = Bpm; _bright = Brightness;
        var src = GetComponent<AudioSource>();
        src.clip = AudioClip.Create("shunta_mus_carrier", 2048, 1, (int)_sr, false);
        src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 1f;
        src.priority = 20; src.bypassReverbZones = true; src.dopplerLevel = 0f;
        _ready = true;
        src.Play();
    }

    public void SnapTo(float[] w) { for (int i = 0; i < Layers; i++) { Target[i] = w[i]; _g0[i] = w[i]; } }

    private static float Midi(int n) { return 440f * Mathf.Pow(2f, (n - 69) / 12f); }
    private float Noise()
    {
        _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) * (2f / 16777215f) - 1f;
    }

    private void Trigger()
    {
        int s = _step;
        if (s == 0) { var ch = Chords[_bar]; for (int i = 0; i < 4; i++) _padF[i] = Midi(ch[i]); }
        int bp = BassPat[s];
        if (bp >= 0) { _bassF = Midi(BassRoot[_bar] + bp); _bassEnv = 1f; }
        var c = Chords[_bar];
        _arpF = Midi(c[ArpPat[s]] + 12 + ((s & 8) != 0 && (s & 3) == 3 ? 12 : 0));
        _arpEnv = 1f;
        if ((s & 3) == 0) { _kickEnv = 1f; _kickPh = 0f; _duck = 1f; }
        if (s == 4 || s == 12) _snEnv = 1f;
        if ((s & 3) == 2 || (_eff > 0.45f && (s & 1) == 1)) _hatEnv = (s & 3) == 2 ? 1f : 0.45f;
    }

    /// <summary>Test hook: render one buffer on the calling thread (stop the AudioSource first so the audio thread is not also in here).</summary>
    public void RenderForTest(float[] data, int channels) { OnAudioFilterRead(data, channels); }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_ready || channels < 1) return;
        int frames = data.Length / channels;
        float inv = 1f / frames, dt = 1f / _sr;
        for (int i = 0; i < Layers; i++) _g1[i] = Mathf.Clamp01(Target[i]);
        _bpm += (Mathf.Clamp(Bpm, 60f, 160f) - _bpm) * 0.05f;
        float bt = Mathf.Clamp01(Brightness); _bright += (bt - _bright) * 0.05f;
        _eff += (Mathf.Clamp01(Effort) - _eff) * 0.05f;
        float fx0 = _fx; _fx += (Mathf.Clamp01(TunnelFx) - _fx) * 0.12f;
        float stepLen = _sr * 60f / (_bpm * 4f);
        float fc = Mathf.Lerp(260f, 6500f, _bright * _bright) * (1f - 0.55f * _fx);
        float aF = 1f - Mathf.Exp(-6.2832f * fc / _sr);
        float aBass = 1f - Mathf.Exp(-6.2832f * Mathf.Lerp(180f, 700f, _bright) / _sr);
        float arpDecay = Mathf.Exp(-5f / stepLen), bassDecay = Mathf.Exp(-3.2f / stepLen);
        float outA = 1f - Mathf.Exp(-6.2832f * Mathf.Lerp(18000f, 1400f, _fx) / _sr);
        float master = Master * (1f + 0.1f * _eff);
        int echoLen = Mathf.Min(_echo.Length - 1, (int)(stepLen * 3f));
        float echoAmt = 0.16f + 0.30f * _fx;

        for (int f = 0; f < frames; f++)
        {
            float k = f * inv;
            if (_stepLeft <= 0f)
            {
                _stepLeft += stepLen;
                Trigger();
                if (++_step >= 16) { _step = 0; _bar = (_bar + 1) & 3; }
            }
            _stepLeft -= 1f;

            float gPad = _g0[0] + (_g1[0] - _g0[0]) * k, gBass = _g0[1] + (_g1[1] - _g0[1]) * k;
            float gArp = _g0[2] + (_g1[2] - _g0[2]) * k, gDrum = _g0[3] + (_g1[3] - _g0[3]) * k;
            gDrum *= 0.7f + 0.3f * _eff;

            // pad: 4 notes x 2 detuned saws, split L/R
            float pl = 0f, pr = 0f;
            for (int v = 0; v < 4; v++)
            {
                float fr = _padF[v];
                float p1 = _padPh[v * 2] + fr * 0.997f * dt, p2 = _padPh[v * 2 + 1] + fr * 1.004f * dt;
                if (p1 > 1f) p1 -= 1f; if (p2 > 1f) p2 -= 1f;
                _padPh[v * 2] = p1; _padPh[v * 2 + 1] = p2;
                float a = 2f * p1 - 1f, b = 2f * p2 - 1f;
                if ((v & 1) == 0) { pl += a; pr += b; } else { pl += b; pr += a; }
            }
            _pLpL1 += aF * (pl * 0.16f - _pLpL1); _pLpL2 += aF * (_pLpL1 - _pLpL2);
            _pLpR1 += aF * (pr * 0.16f - _pLpR1); _pLpR2 += aF * (_pLpR1 - _pLpR2);
            _duck *= 0.99985f;
            float duck = 1f - 0.45f * _duck;
            float padL = _pLpL2 * gPad * duck, padR = _pLpR2 * gPad * duck;

            // bass: saw + sub through low-pass, plucky decay
            _bassPh += _bassF * dt; if (_bassPh > 1f) _bassPh -= 1f;
            _bassEnv *= bassDecay;
            _bLp += aBass * ((2f * _bassPh - 1f) * 0.6f + Mathf.Sin(_bassPh * 6.2832f) * 0.7f - _bLp);
            float bass = _bLp * _bassEnv * gBass * 0.55f * (0.35f + 0.65f * (1f - 0.6f * _duck));

            // arp: saw pluck, filtered, brightness-dependent
            _arpPh += _arpF * dt; if (_arpPh > 1f) _arpPh -= 1f;
            _arpEnv *= arpDecay;
            _aLp1 += aF * ((2f * _arpPh - 1f) - _aLp1); _aLp2 += aF * (_aLp1 - _aLp2);
            float arp = _aLp2 * _arpEnv * gArp * 0.22f;

            // drums
            float kick = 0f;
            if (_kickEnv > 0.001f)
            {
                _kickPh += (48f + 110f * _kickEnv * _kickEnv) * dt; if (_kickPh > 1f) _kickPh -= 1f;
                kick = Mathf.Sin(_kickPh * 6.2832f) * _kickEnv * 0.55f;
                _kickEnv *= 0.9992f;
            }
            float n = Noise();
            float snare = 0f;
            if (_snEnv > 0.001f) { _sLp += 0.35f * (n - _sLp); snare = (n - _sLp * 0.4f) * _snEnv * 0.22f; _snEnv *= 0.9994f; }
            float hat = 0f;
            if (_hatEnv > 0.001f) { _hLp += 0.6f * (n - _hLp); hat = (n - _hLp) * _hatEnv * 0.12f; _hatEnv *= 0.9965f; }
            float drums = (kick + snare + hat) * gDrum;

            float L = padL + bass + arp + drums, R = padR + bass + arp * 0.9f + drums;

            // echo (dotted-8th feel) + tunnel darkening
            float e = _echo[_echoPos];
            _echo[_echoPos] = (arp + padL * 0.3f) + e * 0.42f;
            if (++_echoPos > echoLen) _echoPos = 0;
            L += e * echoAmt; R += e * echoAmt * 0.8f;
            _outL += outA * (L - _outL); _outR += outA * (R - _outR);

            float oL = _outL * master, oR = _outR * master;
            oL = oL / (1f + Mathf.Abs(oL)); oR = oR / (1f + Mathf.Abs(oR));
            int idx = f * channels;
            if (channels == 1) data[idx] = (oL + oR) * 0.5f;
            else { data[idx] = oL; data[idx + 1] = oR; for (int c = 2; c < channels; c++) data[idx + c] = 0f; }
        }
        for (int i = 0; i < Layers; i++) _g0[i] = _g1[i];
    }
}

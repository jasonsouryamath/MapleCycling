using UnityEngine;

/// <summary>
/// Procedural ambience for Shunta Metro, synthesised sample by sample in OnAudioFilterRead (no assets, no allocation
/// on the audio thread: every buffer is allocated in Awake on the main thread). The main thread only writes the
/// <see cref="Target"/> layer gains / rates; gains are ramped linearly across each buffer to avoid zipper noise.
/// Layers (ShuntaAudioMap indices): crowd hum, narrow-street murmur, tunnel drone + wheel hum, wind, passing cars,
/// rain on road, train rumble, bridge wind/cables/expansion joints, harbour waves, gulls.
/// A tunnel effect (comb-filter reverb + echo + low-pass) is applied to the whole mix by <see cref="TunnelFx"/>.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class ShuntaAmbienceSynth : MonoBehaviour
{
    public const int N = ShuntaAudioMap.AmbCount;
    /// <summary>Main-thread written layer gains (0..1).</summary>
    public readonly float[] Target = new float[N];
    public float Master = 0.8f, TunnelFx, SpeedMps, CarsPerMin = 10f, GullsPerMin = 8f;

    private float[] _g0, _g1;
    private float _fx;
    private float _sr = 48000f;
    private bool _ready;
    private uint _rng = 2463534242u;

    private float _c1, _c2, _m1, _m2, _brown, _w1, _r1, _t1, _t2, _br1, _bw1, _wv1, _wv2;
    private float _lfoA, _lfoB, _lfoC, _lfoD;
    private float _wheelPh, _humPh;
    private readonly float[] _wT = { 9f, 9f, 9f }, _wDur = new float[3], _wPan = new float[3], _wLp = new float[3], _wSpd = new float[3];
    private float _wNext = 1f;
    private readonly float[] _gT = { 9f, 9f }, _gDur = new float[2], _gPan = new float[2], _gF = new float[2], _gPh = new float[2];
    private float _gNext = 3f;
    private float _trainT = 6f, _trainEnv, _trainTarget, _clackPh, _clackEnv;
    private float _jointT, _jointEnv, _creakPh, _creakEnv, _creakNext = 2f;
    private float[][] _comb; private int[] _combPos; private float[] _combLp;
    private float[] _echo; private int _echoPos; private float _outLpL, _outLpR;
    private bool _echoLive;

    private static readonly int[] CombMs = { 29, 37, 43, 53 };

    private void Awake()
    {
        _sr = AudioSettings.outputSampleRate;
        _g0 = new float[N]; _g1 = new float[N];
        _comb = new float[CombMs.Length][]; _combPos = new int[CombMs.Length]; _combLp = new float[CombMs.Length];
        for (int i = 0; i < CombMs.Length; i++) _comb[i] = new float[Mathf.Max(64, (int)(_sr * CombMs[i] / 1000f))];
        _echo = new float[Mathf.Max(64, (int)(_sr * 0.21f))];

        var src = GetComponent<AudioSource>();
        var clip = AudioClip.Create("shunta_amb_carrier", 2048, 1, (int)_sr, false);   // silent carrier so the filter runs
        src.clip = clip; src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 1f;
        src.priority = 30; src.bypassReverbZones = true; src.dopplerLevel = 0f;
        _ready = true;
        src.Play();
    }

    /// <summary>Jump straight to these layer gains (no fade-in from silence on a mid-route start).</summary>
    public void SnapTo(float[] weights, float tunnelFx)
    {
        for (int i = 0; i < N; i++) { Target[i] = weights[i]; _g0[i] = weights[i]; }
        TunnelFx = tunnelFx; _fx = tunnelFx;
    }

    private float Noise()
    {
        _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) * (2f / 16777215f) - 1f;
    }

    private float Rand01() { return (Noise() + 1f) * 0.5f; }
    private static float Lp(ref float s, float x, float a) { s += a * (x - s); return s; }
    private static float Tri(float ph) { float x = ph - Mathf.Floor(ph); return x < .5f ? x * 4f - 1f : 3f - x * 4f; }

    /// <summary>Test hook: render one buffer on the calling thread (stop the AudioSource first so the audio thread is not also in here).</summary>
    public void RenderForTest(float[] data, int channels) { OnAudioFilterRead(data, channels); }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_ready || channels < 1) return;
        int frames = data.Length / channels;
        float inv = 1f / frames, dt = 1f / _sr;
        for (int i = 0; i < N; i++) _g1[i] = Mathf.Clamp01(Target[i]);
        float fx0 = _fx; _fx += (Mathf.Clamp01(TunnelFx) - _fx) * 0.12f;
        float spd = SpeedMps, spdN = Mathf.Clamp01(spd / 14f), master = Master;
        float wheelF = 120f + spd * 16f;
        float carRate = Mathf.Max(0.1f, CarsPerMin) / 60f, gullRate = Mathf.Max(0.05f, GullsPerMin) / 60f;
        float jointInterval = Mathf.Max(0.12f, 12f / Mathf.Max(1f, spd));

        for (int f = 0; f < frames; f++)
        {
            float k = f * inv;
            float w = Noise();
            _lfoA += 0.37f * dt; _lfoB += 3.1f * dt; _lfoC += 0.23f * dt; _lfoD += 0.09f * dt;
            if (_lfoA > 1) _lfoA -= 1; if (_lfoB > 1) _lfoB -= 1; if (_lfoC > 1) _lfoC -= 1; if (_lfoD > 1) _lfoD -= 1;
            float lA = Mathf.Sin(_lfoA * 6.2832f), lB = Mathf.Sin(_lfoB * 6.2832f), lC = Mathf.Sin(_lfoC * 6.2832f), lD = Mathf.Sin(_lfoD * 6.2832f);
            float L = 0f, R = 0f, m, g;

            // 0 crowd hum: 230..900 Hz band with syllabic wobble
            g = _g0[0] + (_g1[0] - _g0[0]) * k;
            float c1 = Lp(ref _c1, w, 0.12f), c2 = Lp(ref _c2, w, 0.03f);
            if (g > 0.001f) { m = (c1 - c2) * 3.2f * (0.75f + 0.25f * lB * lA); L += m * g; R += m * g * 0.92f; }

            // 1 narrow-street murmur: tighter band + voice-like hum
            g = _g0[1] + (_g1[1] - _g0[1]) * k;
            float m1 = Lp(ref _m1, w, 0.07f), m2 = Lp(ref _m2, w, 0.02f);
            _humPh += 190f * dt; if (_humPh > 1) _humPh -= 1;
            if (g > 0.001f)
            {
                m = (m1 - m2) * 3.5f * (0.6f + 0.4f * lB) + Tri(_humPh) * 0.05f * (0.5f + 0.5f * lA) * Mathf.Max(0f, lB);
                L += m * g * 0.9f; R += m * g;
            }

            // 2 tunnel: low drone + wheel hum tracking speed + brown rumble
            g = _g0[2] + (_g1[2] - _g0[2]) * k;
            Lp(ref _brown, w, 0.02f);
            _wheelPh += wheelF * dt; if (_wheelPh > 1) _wheelPh -= 1;
            if (g > 0.001f)
            {
                m = _brown * 3.0f + Mathf.Sin(_wheelPh * 6.2832f) * (0.05f + 0.07f * spdN) * (0.8f + 0.2f * lC) + Tri(_wheelPh * 0.45f) * 0.05f;
                L += m * g; R += m * g;
            }

            // 3 wind
            g = _g0[3] + (_g1[3] - _g0[3]) * k;
            float ww = Lp(ref _w1, w, 0.03f + 0.03f * spdN);
            if (g > 0.001f) { m = ww * 4f * (0.65f + 0.35f * lC) * (0.5f + 0.7f * spdN); L += m * g; R += m * g * 0.95f; }

            // 4 passing cars: up to 3 overlapping whooshes, panned, band swept
            g = _g0[4] + (_g1[4] - _g0[4]) * k;
            _wNext -= dt;
            if (_wNext <= 0f)
            {
                _wNext = (0.4f + Rand01() * 1.2f) / carRate;
                for (int s = 0; s < _wT.Length; s++)
                    if (_wT[s] >= _wDur[s])
                    { _wT[s] = 0f; _wDur[s] = 0.9f + Rand01() * 1.6f; _wPan[s] = Noise(); _wSpd[s] = 0.6f + Rand01() * 0.8f; break; }
            }
            for (int s = 0; s < _wT.Length; s++)
            {
                if (_wT[s] >= _wDur[s]) continue;
                float u = _wT[s] / _wDur[s];
                _wT[s] += dt;
                float env = Mathf.Sin(u * 3.1416f); env *= env;
                float v = Lp(ref _wLp[s], w, 0.02f + 0.16f * env * _wSpd[s]) * env * 3.5f;
                float pan = _wPan[s] * (1f - u * 2f);
                float pl = Mathf.Clamp01(0.5f - pan * 0.5f);
                L += v * g * pl * 1.4f; R += v * g * (1f - pl) * 1.4f;
            }

            // 5 rain on road: dense hiss + patter (decorrelated per channel)
            g = _g0[5] + (_g1[5] - _g0[5]) * k;
            if (g > 0.001f)
            {
                float hp = w - Lp(ref _r1, w, 0.35f);
                float wob = 0.85f + 0.15f * lA;
                float pa = Mathf.Abs(Noise()) > 0.997f ? Noise() * 0.5f : 0f;
                float pb = Mathf.Abs(Noise()) > 0.997f ? Noise() * 0.5f : 0f;
                L += (hp * 0.5f * wob + pa) * g; R += (hp * 0.5f * wob * 0.9f + pb) * g;
            }

            // 6 train rumble: constant low rumble, passes every ~10 s carry carriage clacks
            g = _g0[6] + (_g1[6] - _g0[6]) * k;
            _trainT -= dt;
            if (_trainT <= 0f)
            {
                if (_trainTarget < 0.5f) { _trainTarget = 1f; _trainT = 4f + Rand01() * 2f; }
                else { _trainTarget = 0f; _trainT = 5f + Rand01() * 6f; }
            }
            _trainEnv += (_trainTarget - _trainEnv) * 0.00012f;
            _clackPh += (7.5f + _trainEnv * 2f) * dt; if (_clackPh > 1f) { _clackPh -= 1f; _clackEnv = 1f; }
            _clackEnv *= 0.9985f;
            if (g > 0.001f)
            {
                float rum = Lp(ref _t1, w, 0.008f) * 9f, rum2 = Lp(ref _t2, w, 0.05f);
                m = rum * (0.3f + 0.9f * _trainEnv) + rum2 * 1.2f * _trainEnv * (0.7f + 0.3f * lB) + w * _clackEnv * 0.4f * _trainEnv;
                L += m * g; R += m * g;
            }

            // 7 bridge: gusty wind + resonant cable creaks + expansion-joint thumps
            g = _g0[7] + (_g1[7] - _g0[7]) * k;
            _jointT -= dt; if (_jointT <= 0f) { _jointT = jointInterval; _jointEnv = 1f; }
            _jointEnv *= 0.9992f;
            _creakNext -= dt; if (_creakNext <= 0f) { _creakNext = 1.5f + Rand01() * 4f; _creakEnv = 1f; }
            _creakEnv *= 0.99985f;
            _creakPh += (380f + 140f * lA + 60f * _creakEnv) * dt; if (_creakPh > 1f) _creakPh -= 1f;
            if (g > 0.001f)
            {
                float bw = Lp(ref _bw1, w, 0.045f);
                m = bw * 4.5f * (0.5f + 0.5f * lC * lC) * (0.5f + 0.6f * spdN)
                  + Mathf.Sin(_creakPh * 6.2832f) * 0.05f * _creakEnv * _creakEnv
                  + Lp(ref _br1, w, 0.01f) * _jointEnv * 6f * (0.4f + spdN * 0.6f);
                L += m * g; R += m * g * 0.9f;
            }

            // 8 waves: swells every ~11 s, rumble + foam hiss
            g = _g0[8] + (_g1[8] - _g0[8]) * k;
            float sw = Mathf.Max(0f, lD); sw *= sw;
            float wv = Lp(ref _wv1, w, 0.02f), foam = w - Lp(ref _wv2, w, 0.25f);
            if (g > 0.001f) { m = wv * 6f * (0.25f + 0.85f * sw) + foam * 0.22f * sw; L += m * g; R += m * g * 0.93f; }

            // 9 gulls: pitch-gliding cries, 3 notes per call
            g = _g0[9] + (_g1[9] - _g0[9]) * k;
            _gNext -= dt;
            if (_gNext <= 0f)
            {
                _gNext = (0.5f + Rand01()) / gullRate;
                for (int s = 0; s < _gT.Length; s++)
                    if (_gT[s] >= _gDur[s]) { _gT[s] = 0f; _gDur[s] = 0.6f + Rand01() * 0.7f; _gPan[s] = Noise() * 0.8f; _gF[s] = 1500f + Rand01() * 700f; break; }
            }
            for (int s = 0; s < _gT.Length; s++)
            {
                if (_gT[s] >= _gDur[s]) continue;
                float u = _gT[s] / _gDur[s]; _gT[s] += dt;
                float note = u * 3f, nu = note - Mathf.Floor(note);
                float env = Mathf.Sin(nu * 3.1416f) * (1f - u * 0.5f);
                float fr = _gF[s] * (1.25f - 0.45f * nu) * (1f + 0.02f * Mathf.Sin(u * 90f));
                _gPh[s] += fr * dt; if (_gPh[s] > 1f) _gPh[s] -= 1f;
                float v = (Mathf.Sin(_gPh[s] * 6.2832f) + 0.45f * Mathf.Sin(_gPh[s] * 12.566f)) * env * 0.09f * g;
                float pl = 0.5f - _gPan[s] * 0.5f;
                L += v * pl * 1.6f; R += v * (1f - pl) * 1.6f;
            }

            // tunnel effect: comb reverb + echo + low-pass, smoothed across the buffer
            float fx = fx0 + (_fx - fx0) * k;
            if (fx > 0.002f || _echoLive)
            {
                float mono = (L + R) * 0.5f, wet = 0f, fb = 0.70f + 0.19f * fx;
                for (int c = 0; c < _comb.Length; c++)
                {
                    float[] buf = _comb[c]; int p = _combPos[c];
                    float y = buf[p];
                    Lp(ref _combLp[c], y, 0.45f);
                    buf[p] = mono + _combLp[c] * fb;
                    if (++p >= buf.Length) p = 0;
                    _combPos[c] = p;
                    wet += y;
                }
                float e = _echo[_echoPos];
                _echo[_echoPos] = mono + e * 0.45f; if (++_echoPos >= _echo.Length) _echoPos = 0;
                float lpA = Mathf.Lerp(1f, 0.16f, fx);
                _outLpL += lpA * (L - _outLpL); _outLpR += lpA * (R - _outLpR);
                L = _outLpL * (1f - 0.2f * fx) + (wet * 0.18f + e * 0.3f) * fx;
                R = _outLpR * (1f - 0.2f * fx) + (wet * 0.18f - e * 0.24f) * fx;
                _echoLive = fx > 0.002f || Mathf.Abs(e) > 1e-4f;
            }

            L *= master; R *= master;
            L = L / (1f + Mathf.Abs(L)); R = R / (1f + Mathf.Abs(R));      // soft limiter
            int idx = f * channels;
            if (channels == 1) data[idx] = (L + R) * 0.5f;
            else { data[idx] = L; data[idx + 1] = R; for (int c = 2; c < channels; c++) data[idx + c] = 0f; }
        }
        for (int i = 0; i < N; i++) _g0[i] = _g1[i];
    }
}

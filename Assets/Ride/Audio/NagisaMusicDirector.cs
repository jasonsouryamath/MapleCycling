using System.Text;
using UnityEngine;

/// <summary>
/// "Nagisa Drive": the Nagisa Bay theme as six sample-aligned loop stems, crossfaded by route distance.
/// Stems are rendered by tools/audio/build_nagisa_music.py (120 BPM, 32 bars = 64 s, A major, city-pop harmony)
/// into Assets/Resources/Audio/Nagisa/Music. All six start on the same scheduled DSP tick and run for exactly the
/// same length, so they never drift. Only volumes move (cheap: 6 two-dimensional sources, no per-frame allocation).
///
/// Zone feel (keyframes below, ride order along the Nagisa course):
///   marina        relaxed: keys + soft bass/guitar, hardly any drums
///   promenade     groove arrives: bass, guitar, light percussion
///   beachfront    bright and energetic: everything but the full drive layer is up, drive half in
///   climb         builds: percussion then the drive layer ramp up the slope, bass holds
///   overlook      opens up: drive drops away, the LEAD melody comes in over keys and bass
///   descent       percussion and drive return, lead recedes
///   coast/bridge  bright reprise, lead returns for the bridge and finish
/// </summary>
[DisallowMultipleComponent]
public sealed class NagisaMusicDirector : MonoBehaviour
{
    public const int KeysI = 0, BassI = 1, GuitarI = 2, PercI = 3, DriveI = 4, LeadI = 5, Count = 6;
    public static readonly string[] StemNames = { "keys", "bass", "guitar", "perc", "drive", "lead" };
    public const string ResourceFolder = "Audio/Nagisa/Music/Nagisa_";

    // Packed (route metres, keys, bass, guitar, perc, drive, lead) keyframes, linearly interpolated.
    private static readonly float[] Frames = BuildFrames();

    private INagisaAudioContext _ctx;
    private AudioSource[] _src;
    private readonly float[] _target = new float[Count];
    private readonly float[] _cur = new float[Count];
    private float _enter;                 // 0..1 fade-in after the director starts
    [Range(0f, 1f)] public float stemFadeSeconds = 2.4f;

    private static float[] BuildFrames()
    {
        const float M = NagisaAudioMap.Zone_marinaEnd, BS = NagisaAudioMap.Zone_beachStart, H = NagisaAudioMap.Zone_hotelM,
                    BE = NagisaAudioMap.Zone_beachEnd, C = NagisaAudioMap.Zone_climbStart, K = NagisaAudioMap.Zone_komM,
                    R = NagisaAudioMap.Zone_ridgeEnd, CO = NagisaAudioMap.Zone_coastStart, BR = NagisaAudioMap.Zone_bridgeStart,
                    F = NagisaAudioMap.Zone_finish;
        //          d          keys  bass  gtr   perc  drive lead
        return new[]
        {
            0f,       .90f, .55f, .55f, .35f, 0.00f, .15f,   // marina: relaxed
            M,        .85f, .70f, .70f, .50f, 0.00f, .20f,
            M + 520f, .80f, .90f, .90f, .75f, 0.15f, .30f,   // promenade
            BS,       .70f, 1.0f, 1.0f, .95f, 0.55f, .45f,   // beachfront: bright / energetic
            H,        .70f, 1.0f, 1.0f, 1.0f, 0.60f, .50f,
            BE,       .70f, 1.0f, .95f, .95f, 0.50f, .40f,
            C,        .80f, 1.0f, .70f, .80f, 0.35f, 0.0f,   // climb foot
            C + 1300f,.85f, 1.0f, .60f, .90f, 0.65f, .05f,   // building
            K - 900f, .90f, 1.0f, .50f, 1.00f, 0.90f, .10f,
            K - 250f, .95f, 1.0f, .50f, 1.00f, 1.00f, .15f,  // intensity peak below the summit
            K + 60f,  1.0f, .85f, .70f, .55f, 0.30f, 1.00f,  // overlook: lead opens, drums fall away
            K + 900f, 1.0f, .80f, .80f, .50f, 0.15f, 1.00f,
            K + 1300f,.90f, .90f, .90f, .75f, 0.45f, .70f,
            K + 1800f,.85f, 1.0f, 1.0f, 1.00f, 0.80f, .45f,  // descent: percussion back
            R,        .80f, 1.0f, 1.0f, 1.00f, 0.85f, .40f,
            CO,       .70f, 1.0f, 1.0f, .95f, 0.60f, .50f,   // coast: bright reprise
            BR,       .75f, 1.0f, .90f, .90f, 0.60f, .70f,
            F,        .80f, .90f, .80f, .70f, 0.40f, .90f,
        };
    }

    /// <summary>Zone weights (0..1 per stem) at route distance <paramref name="d"/>. Static so tests can sample it.</summary>
    public static void WeightsAt(float d, float[] outW)
    {
        int rows = Frames.Length / 7;
        if (d <= Frames[0]) { for (int s = 0; s < Count; s++) outW[s] = Frames[1 + s]; return; }
        for (int r = 1; r < rows; r++)
        {
            float d1 = Frames[r * 7];
            if (d <= d1)
            {
                float d0 = Frames[(r - 1) * 7];
                float t = d1 - d0 < 1e-3f ? 1f : (d - d0) / (d1 - d0);
                for (int s = 0; s < Count; s++) outW[s] = Mathf.Lerp(Frames[(r - 1) * 7 + 1 + s], Frames[r * 7 + 1 + s], t);
                return;
            }
        }
        for (int s = 0; s < Count; s++) outW[s] = Frames[(rows - 1) * 7 + 1 + s];
    }

    public void Init(INagisaAudioContext ctx)
    {
        _ctx = ctx;
        _src = new AudioSource[Count];
        double start = AudioSettings.dspTime + 0.4;
        for (int i = 0; i < Count; i++)
        {
            var clip = Resources.Load<AudioClip>(ResourceFolder + StemNames[i]);
            if (clip == null) { Debug.LogWarning($"[nagisa-audio] missing music stem {StemNames[i]}"); continue; }
            var go = new GameObject("Stem " + StemNames[i]); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
            s.priority = 40; s.dopplerLevel = 0f; s.bypassReverbZones = true;
            s.PlayScheduled(start);
            _src[i] = s;
        }
        // Start at the weights for the rider's current position so a mid-route start never fades in from silence.
        var w = new float[Count];
        WeightsAt(ctx.RouteD, w);
        for (int i = 0; i < Count; i++) _cur[i] = _target[i] = w[i];
    }

    private void Update()
    {
        if (_ctx == null || _src == null) return;
        float dt = Time.unscaledDeltaTime;
        _enter = Mathf.MoveTowards(_enter, 1f, dt / 3f);
        WeightsAt(_ctx.RouteD, _target);
        // Speed adds a little drive energy: flat-out riding feels a notch more urgent than cruising.
        float push = Mathf.Clamp01((_ctx.SpeedMps - 9f) / 9f) * 0.18f;
        _target[DriveI] = Mathf.Clamp01(_target[DriveI] + push);
        _target[PercI] = Mathf.Clamp01(_target[PercI] + push * 0.5f);

        float master = _ctx.MusicLevel * _ctx.Gate * _enter;
        float step = dt / Mathf.Max(0.2f, stemFadeSeconds);
        for (int i = 0; i < Count; i++)
        {
            if (_src[i] == null) continue;
            _cur[i] = Mathf.MoveTowards(_cur[i], _target[i], step);
            _src[i].volume = _cur[i] * master;
        }
    }

    public float Current(int stem) => _cur[stem];

    public string Describe()
    {
        var sb = new StringBuilder("music");
        for (int i = 0; i < Count; i++) sb.Append(' ').Append(StemNames[i][0]).Append('=').Append(_cur[i].ToString("F2"));
        return sb.ToString();
    }
}

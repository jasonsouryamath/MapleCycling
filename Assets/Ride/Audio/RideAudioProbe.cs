using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Opt-in smoke test for <see cref="RideAudio"/>. It never runs unless the environment variable
/// MAPLERIDE_AUDIO_PROBE is set to 1, so normal play mode and normal captures are untouched.
///
/// Why it exists: RideAudio has never been listened to. A headless batchmode run cannot prove
/// anything is AUDIBLE (Unity uses a null audio driver in batchmode), but it can prove the three
/// things that actually break silently:
///   1. every Resources clip resolves (no "[ride-audio] missing ..." warning, clip != null),
///   2. the AudioSources are playing and their playback time ADVANCES (the audio graph is live),
///   3. the volume/pitch envelopes respond to the ride instead of sitting pinned at zero.
/// Audibility and mix balance stay Not Verified until someone runs a windowed build.
///
/// Run it by setting MAPLERIDE_AUDIO_PROBE=1 alongside the normal capture entry point:
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod MinatoPlaymodeCapture.Run
/// </summary>
[DisallowMultipleComponent]
public sealed class RideAudioProbe : MonoBehaviour
{
    private const string EnvFlag = "MAPLERIDE_AUDIO_PROBE";

    // PROVISIONAL: sample times (seconds of play) chosen to straddle the BGM's 2.5 s fade-in and
    // to land well after the rider is up to speed, so the speed-driven beds have opened up.
    private static readonly float[] SampleAtSeconds = { 3f, 12f, 45f, 120f };

    private static readonly string[] SourceFields =
        { "_musicA", "_musicB", "_drive", "_freewheel", "_tyre", "_wind" };

    private int _errors, _exceptions;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSpawn()
    {
        if (System.Environment.GetEnvironmentVariable(EnvFlag) != "1") return;
        var go = new GameObject("~RideAudioProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<RideAudioProbe>();
    }

    private void Awake()
    {
        Application.logMessageReceived += OnLog;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
    }

    private void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception) _exceptions++;
        else if (type == LogType.Error || type == LogType.Assert) _errors++;
    }

    private IEnumerator Start()
    {
        var cfg = AudioSettings.GetConfiguration();
        Debug.Log($"[ride-audio-probe] start. audio driver output={AudioSettings.speakerMode} " +
                  $"sampleRate={cfg.sampleRate} dspBuffer={cfg.dspBufferSize} " +
                  $"numRealVoices={cfg.numRealVoices} listeners={FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}");

        RideAudio audio = null;
        float waited = 0f;
        while (audio == null && waited < 30f)
        {
            audio = FindAnyObjectByType<RideAudio>();
            if (audio == null) { yield return new WaitForSeconds(0.5f); waited += 0.5f; }
        }
        if (audio == null)
        {
            Debug.LogWarning("[ride-audio-probe] FAIL: no RideAudio component in the scene after 30 s.");
            yield break;
        }
        Debug.Log($"[ride-audio-probe] found RideAudio on '{audio.gameObject.name}' after {waited:0.0}s " +
                  $"(musicVolume={audio.musicVolume:0.00} bikeVolume={audio.bikeVolume:0.00})");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var prevTime = new float[SourceFields.Length];
        float clock = 0f;

        foreach (float mark in SampleAtSeconds)
        {
            while (clock < mark) { yield return null; clock += Time.unscaledDeltaTime; }

            var session = audio.session;
            var devices = audio.devices;
            var tm = devices != null ? devices.Telemetry : default;
            // Listener count is logged EVERY sample on purpose: RideAudio only adds its fallback
            // AudioListener from Update(), so a count taken once at spawn proves nothing about
            // whether the ride is actually audible.
            int listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
            var wd = WeatherDirector.Instance;
            Debug.Log($"[ride-audio-probe] t={clock:0.0}s ride speed={(session != null ? session.SpeedKph : 0f):0.0}kph " +
                      $"watts={tm.Watts:0} cadence={tm.CadenceRpm:0} listeners={listeners} " +
                      $"apparentWind={(wd != null ? wd.Apparent.SpeedMps : -1f):0.00}m/s " +
                      $"errors={_errors} exceptions={_exceptions}");

            for (int i = 0; i < SourceFields.Length; i++)
            {
                var f = typeof(RideAudio).GetField(SourceFields[i], flags);
                var src = f != null ? f.GetValue(audio) as AudioSource : null;
                if (src == null) { Debug.LogWarning($"[ride-audio-probe]   {SourceFields[i]}: SOURCE NULL"); continue; }
                string clip = src.clip != null ? $"{src.clip.name} ({src.clip.length:0.0}s)" : "NO CLIP";
                float t = src.time;
                // NOTE: `advanced` compares against the PREVIOUS sample, which may be tens of
                // seconds earlier. The bike loops are 1-4 s long, so a false here usually just
                // means the loop wrapped past the old position, not that playback stalled.
                bool advanced = t > prevTime[i] + 1e-4f;
                Debug.Log($"[ride-audio-probe]   {SourceFields[i],-11} clip={clip,-28} playing={src.isPlaying,-5} " +
                          $"vol={src.volume:0.000} pitch={src.pitch:0.000} time={t:0.00}s advanced={advanced}");
                prevTime[i] = t;
            }
        }

        Debug.Log($"[ride-audio-probe] done. errors={_errors} exceptions={_exceptions}");
    }
}

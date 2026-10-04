using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play-mode smoke test for the Shunta Metro runtime hosts (ShuntaHud, ShuntaLifeDirector, ShuntaAudioHost).
/// Boots the playable shunta_metro ride, seeks mid-zone through all 12 zones (dwell a few seconds each) and asserts:
/// hosts exist/active, no exceptions (Application.logMessageReceived), HUD banner fired 12x, train/traffic pools
/// near the rider in the right zones, rain/petals in zones 7-8/11-12, audio buffers finite and not clipping,
/// per-second managed allocation under a sane bound, Life director's route point agrees with the real rider.
/// Run: pwsh -NoProfile -File tools/unity/run_steps.ps1 "ShuntaMetroHostSmoke.Run|claude_shunta_smoke.log|0"
/// </summary>
[InitializeOnLoad]
public static class ShuntaMetroHostSmoke
{
    // per-project key: EditorPrefs are shared by every Unity instance of this user (Lab 1 / Lab 2 run concurrently)
    static string Key => "mapleride.shuntasmoke.run." + Application.dataPath.GetHashCode();
    const string Scene = "Assets/Scenes/Playable/shunta_metro.unity";
    const float Dwell = 4f;             // real seconds per zone
    const float AllocBoundBytesPerSec = 3f * 1024f * 1024f;
    const float MaxRiderOffsetM = 40f;

    static int _stage, _fails, _frames, _zone;
    static double _deadline, _zoneT0, _allocT0;
    static long _alloc0;
    static ShuntaCourseData _data;
    static RideSession _s;
    static float _lenM;

    // exceptions
    static int _exceptions, _shuntaErrors, _otherErrors;
    static readonly List<string> _firstProblems = new List<string>();

    // per-zone accumulators
    static int _maxTrain, _maxTraffic, _maxRainP, _maxSakuraP;
    static bool _sawRainOn, _sawSakuraOn;
    static float _maxOffset;
    static int _bannersAtZoneStart;
    static int _audioPhase; static float _ambPeak, _musPeak, _ambRms, _musRms; static bool _audioBad; static string _audioWhy;
    static float _globalAmbRms, _globalMusRms;
    static readonly float[] _buf = new float[2048];   // 1024 frames x 2

    static ShuntaMetroHostSmoke()
    {
        // A killed batch must never arm the next human play session.
        EditorPrefs.DeleteKey("mapleride.shuntasmoke.run");
        EditorPrefs.DeleteKey(Key);
        EditorApplication.playModeStateChanged += OnState;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool ok, string what)
    {
        Debug.Log("[shunta-smoke] " + (ok ? "PASS " : "FAIL ") + what);
        if (!ok) _fails++;
    }

    static void OnLog(string msg, string stack, LogType t)
    {
        if (msg != null && msg.StartsWith("[shunta-smoke]")) return;
        bool exc = t == LogType.Exception || (msg != null && msg.Contains("NullReferenceException"));
        if (exc) { _exceptions++; Note(msg, stack); return; }
        if (t == LogType.Error || t == LogType.Assert)
        {
            bool mine = (msg != null && msg.Contains("shunta")) || (stack != null && stack.Contains("Shunta"));
            if (mine) { _shuntaErrors++; Note(msg, stack); } else _otherErrors++;
        }
    }

    static void Note(string msg, string stack)
    {
        if (_firstProblems.Count >= 6) return;
        string st = stack ?? "";
        int nl = st.IndexOf('\n'); if (nl > 0) st = st.Substring(0, Math.Min(st.Length, 200));
        _firstProblems.Add((msg ?? "").Replace('\n', ' ') + " | " + st.Replace('\n', ' '));
    }

    static void OnState(PlayModeStateChange c)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (c == PlayModeStateChange.EnteredPlayMode)
        {
            _stage = 0; _frames = 0; _fails = 0; _zone = 0; _exceptions = _shuntaErrors = _otherErrors = 0; _firstProblems.Clear();
            _globalAmbRms = _globalMusRms = 0f;
            _deadline = EditorApplication.timeSinceStartup + 420;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }
        else if (c == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            bool ok = _fails == 0 && _stage >= 99;
            Debug.Log(ok ? "[shunta-smoke] SMOKE PASS" : "[shunta-smoke] SMOKE FAIL (fails " + _fails + ", stage " + _stage + ")");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 2);
        }
    }

    static void Finish() { EditorApplication.ExitPlaymode(); }

    static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now > _deadline) { Check(false, "timed out at stage " + _stage + " zone " + _zone); Finish(); return; }
        _frames++;
        switch (_stage)
        {
            case 0:   // boot + travel
            {
                if (_frames < 30) return;
                _s = UnityEngine.Object.FindFirstObjectByType<RideSession>();
                var dir = UnityEngine.Object.FindFirstObjectByType<RegionDirector>();
                if (_s == null || dir == null) { Check(false, "session/director missing"); _stage = -1; Finish(); return; }
                RegionCatalog.Find(RegionCatalog.ShuntaMetro).BuiltCourseId = ShuntaRouteProvider.CourseId;
                _s.EnsureCourse();
                Check(dir.FastTravel(RegionCatalog.ShuntaMetro), "FastTravel(shunta_metro)");
                _data = ShuntaCourseData.Load();
                _lenM = _s.Course != null ? _s.Course.Length : 0f;
                Check(_data != null && _lenM > 1000f, "course data + course length " + _lenM.ToString("0") + " m");
                _stage = 1; _frames = 0; break;
            }
            case 1:   // wait for all three hosts to come up
            {
                var hud = ShuntaHud.Instance; var life = ShuntaLifeDirector.Instance; var au = ShuntaAudioHost.Instance;
                bool up = hud != null && hud.IsActive && life != null && life.Active && au != null && au.Director != null;
                if (!up && _frames < 900) return;   // ~15 s at 60 fps; frame-based so a slow editor still gets time
                Check(hud != null, "ShuntaHud instantiated");
                Check(life != null, "ShuntaLifeDirector instantiated");
                Check(au != null, "ShuntaAudioHost instantiated");
                Check(hud != null && hud.IsActive, "HUD active on shunta_metro");
                Check(life != null && life.Active, "Life director active");
                Check(au != null && au.Director != null && au.Director.Ambience != null && au.Director.Music != null, "Audio director + both synths started");
                if (!up) { _stage = -1; Finish(); return; }
                _zone = 0; _stage = 2; _frames = 0; break;
            }
            case 2:   // begin a zone dwell
            {
                if (_zone >= _data.zones.Length) { _stage = 90; break; }
                var z = _data.zones[_zone];
                float km = (z.startKm + z.endKm) * 0.5f;
                _s.SeekTo(km / _data.distanceKm * _lenM);
                _zoneT0 = now; _frames = 0;
                _maxTrain = _maxTraffic = _maxRainP = _maxSakuraP = 0; _sawRainOn = _sawSakuraOn = false; _maxOffset = 0f;
                _bannersAtZoneStart = ShuntaHud.Instance.BannersShown;
                _alloc0 = 0; _audioPhase = 0; _audioBad = false; _audioWhy = "";
                _stage = 3; break;
            }
            case 3:   // dwell and accumulate
            {
                var life = ShuntaLifeDirector.Instance;
                if (life == null) { Check(false, "life director vanished in zone " + (_zone + 1)); _stage = -1; Finish(); return; }
                _maxTrain = Mathf.Max(_maxTrain, life.VisibleTrainCars);
                _maxTraffic = Mathf.Max(_maxTraffic, life.VisibleTraffic);
                _maxRainP = Mathf.Max(_maxRainP, life.RainParticles);
                _maxSakuraP = Mathf.Max(_maxSakuraP, life.SakuraParticles);
                _sawRainOn |= life.RainOn; _sawSakuraOn |= life.SakuraOn;
                if (now - _zoneT0 > 1.5) _maxOffset = Mathf.Max(_maxOffset, life.RiderOffsetM);
                // allocation window: from 1 s to the end of the dwell minus the audio probe
                double t = now - _zoneT0;
                if (_alloc0 == 0 && t > 1.0) { _alloc0 = GC.GetAllocatedBytesForCurrentThread(); _allocT0 = now; }
                if (t < Dwell) return;
                _stage = 4; break;
            }
            case 4:   // allocation sample, then audio probe (stop the synth sources one frame before rendering)
            {
                long a = GC.GetAllocatedBytesForCurrentThread();
                double span = Math.Max(0.1, now - _allocT0);
                _allocPerSec = (float)((a - _alloc0) / span);
                var d = ShuntaAudioHost.Instance.Director;
                SetAudioSources(d, false);
                _stage = 5; break;
            }
            case 5:
            {
                var d = ShuntaAudioHost.Instance.Director;
                ProbeSynth(d.Ambience, out _ambPeak, out _ambRms);
                ProbeSynth(d.Music, out _musPeak, out _musRms);
                SetAudioSources(d, true);
                _globalAmbRms = Mathf.Max(_globalAmbRms, _ambRms); _globalMusRms = Mathf.Max(_globalMusRms, _musRms);
                EvaluateZone();
                _zone++; _stage = 2; break;
            }
            case 90:  // final
            {
                var hud = ShuntaHud.Instance;
                Check(hud != null && hud.BannersShown == 12, "HUD zone banner fired 12 times (" + (hud != null ? hud.BannersShown : -1) + ")");
                Check(_exceptions == 0, "zero exceptions / NullReferenceExceptions in the log (" + _exceptions + ")");
                Check(_shuntaErrors == 0, "zero Shunta error logs (" + _shuntaErrors + "; unrelated errors seen: " + _otherErrors + ")");
                foreach (var p in _firstProblems) Debug.Log("[shunta-smoke] problem: " + p);
                Debug.Log("[shunta-smoke] audio best RMS: ambience " + _globalAmbRms.ToString("0.0000") + ", music " + _globalMusRms.ToString("0.0000"));
                Check(_globalAmbRms > 1e-5f, "ambience produced audible output in at least one zone");
                _stage = 99; Finish(); break;
            }
        }
    }

    static float _allocPerSec;

    static void SetAudioSources(ShuntaDirector d, bool on)
    {
        foreach (var src in d.GetComponentsInChildren<AudioSource>(true))
            if (on) { if (!src.isPlaying) src.Play(); } else src.Stop();
    }

    static void ProbeSynth(ShuntaAmbienceSynth s, out float peak, out float rms) { Probe(s == null ? null : (Action<float[], int>)s.RenderForTest, out peak, out rms); }
    static void ProbeSynth(ShuntaMusicSynth s, out float peak, out float rms) { Probe(s == null ? null : (Action<float[], int>)s.RenderForTest, out peak, out rms); }

    static void Probe(Action<float[], int> render, out float peak, out float rms)
    {
        peak = 0f; rms = 0f;
        if (render == null) { _audioBad = true; _audioWhy += " synth missing;"; return; }
        double sum = 0; int n = 0;
        for (int b = 0; b < 12; b++)
        {
            Array.Clear(_buf, 0, _buf.Length);
            render(_buf, 2);
            for (int i = 0; i < _buf.Length; i++)
            {
                float v = _buf[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) { _audioBad = true; _audioWhy += " NaN/Inf;"; return; }
                float a = Mathf.Abs(v);
                if (b >= 4) { peak = Mathf.Max(peak, a); sum += (double)v * v; n++; }
            }
        }
        rms = n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
        if (peak > 1.0001f) { _audioBad = true; _audioWhy += " clipping peak " + peak.ToString("0.000") + ";"; }
    }

    static bool InZones(int[] zones, int z) { foreach (var q in zones) if (q == z) return true; return false; }

    static void EvaluateZone()
    {
        int z = _data.zones[_zone].index;
        var hud = ShuntaHud.Instance; var life = ShuntaLifeDirector.Instance; var set = life.settings;
        string tag = "zone " + z + " (" + _data.zones[_zone].id + ")";
        Debug.Log($"[shunta-smoke] {tag}: rider {_s.DistanceM:0} m, train {_maxTrain}, traffic {_maxTraffic}, rain {_maxRainP}/{(_sawRainOn ? "on" : "off")}, petals {_maxSakuraP}/{(_sawSakuraOn ? "on" : "off")}, offset {_maxOffset:0.0} m, alloc {_allocPerSec / 1024f:0} KB/s, amb peak/rms {_ambPeak:0.000}/{_ambRms:0.0000}, mus {_musPeak:0.000}/{_musRms:0.0000}");
        Check(hud.LastBannerZone == z && hud.BannersShown >= _bannersAtZoneStart, tag + " HUD banner zone == " + hud.LastBannerZone);
        if (z == set.trainZone) Check(_maxTrain > 0, tag + " train cars visible near rider (" + _maxTrain + ")");
        else if (Mathf.Abs(z - set.trainZone) > 1) Check(_maxTrain == 0, tag + " no train far from its zone (" + _maxTrain + ")");
        if (InZones(set.trafficZones, z)) Check(_maxTraffic > 0, tag + " traffic pool active (" + _maxTraffic + ")");
        else Check(_maxTraffic == 0, tag + " no traffic outside traffic zones (" + _maxTraffic + ")");
        bool rainZ = InZones(set.rainZones, z), pet = InZones(set.sakuraZones, z);
        Check(rainZ == _sawRainOn, tag + " rain " + (rainZ ? "on" : "off") + " as expected");
        Check(pet == _sawSakuraOn, tag + " petals " + (pet ? "on" : "off") + " as expected");
        if (rainZ) Check(_maxRainP > 0, tag + " rain particles alive (" + _maxRainP + ")");
        if (pet) Check(_maxSakuraP > 0, tag + " petal particles alive (" + _maxSakuraP + ")");
        Check(_maxOffset < MaxRiderOffsetM, tag + " life director route point within " + MaxRiderOffsetM + " m of the rider (" + _maxOffset.ToString("0.0") + ")");
        Check(_allocPerSec < AllocBoundBytesPerSec, tag + " main-thread alloc " + (_allocPerSec / 1024f).ToString("0") + " KB/s < " + (AllocBoundBytesPerSec / 1024f).ToString("0"));
        Check(!_audioBad, tag + " audio finite and not clipping" + _audioWhy);
    }
}

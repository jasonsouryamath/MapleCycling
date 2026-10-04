using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Play-mode self test for ShuntaRideHost. Run via run_steps "ShuntaRideHostTest.Run|claude_shunta_host.log|0".</summary>
[InitializeOnLoad]
public static class ShuntaRideHostTest
{
    const string Key = "mapleride.shuntahost.test";
    static int _stage, _frames, _fails;
    static double _deadline;
    static float _gripZ1, _gripZ7, _draftZ7;
    static ShuntaCourseData _data;
    static RideSession _s;

    static ShuntaRideHostTest()
    {
        // A killed batch must never arm the next human play session.
        EditorPrefs.DeleteKey(Key);
        EditorApplication.playModeStateChanged += OnState;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Playable/shunta_metro.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool ok, string what)
    {
        Debug.Log("[shunta-host-test] " + (ok ? "PASS " : "FAIL ") + what);
        if (!ok) _fails++;
    }

    static void OnState(PlayModeStateChange c)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (c == PlayModeStateChange.EnteredPlayMode)
        {
            _stage = 0; _frames = 0; _fails = 0; _deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update += Tick;
        }
        else if (c == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            bool ok = _fails == 0 && _stage >= 10;
            Debug.Log(ok ? "[shunta-host-test] SELFTEST PASS" : "[shunta-host-test] SELFTEST FAIL");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 2);
        }
    }

    static void Finish() { EditorApplication.ExitPlaymode(); }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > _deadline) { Check(false, "timed out at stage " + _stage); Finish(); return; }
        _frames++;
        if (_stage >= 1) { var live = Object.FindFirstObjectByType<RideSession>(); if (live != null && live != _s) { Debug.Log("[shunta-host-test] session instance changed; following the live one"); _s = live; } }
        ShuntaRideHost h;
        switch (_stage)
        {
            case 0:
                if (_frames < 30) return;
                _s = Object.FindFirstObjectByType<RideSession>();
                var dir = Object.FindFirstObjectByType<RegionDirector>();
                if (_s == null || dir == null) { Check(false, "session/director missing"); _stage = -1; Finish(); return; }
                RegionCatalog.Find(RegionCatalog.ShuntaMetro).BuiltCourseId = ShuntaRouteProvider.CourseId;
                _s.EnsureCourse();
                Check(dir.FastTravel(RegionCatalog.ShuntaMetro), "FastTravel(shunta_metro)");
                _data = ShuntaCourseData.Load();
                _stage = 1; _frames = 0; break;
            case 1:
                if (_frames < 5) return;
                Check(ShuntaRideHost.Instance != null, "ShuntaRideHost bootstrapped");
                _stage = 2; _frames = 0; Seek(1); break;
            case 2:
                if (_frames < 8) return;
                h = ShuntaRideHost.Instance;
                Check(h != null && h.Active, "host active on shunta_metro");
                _gripZ1 = h != null ? h.Current.gripMultiplier : 0f;
                _stage = 3; _frames = 0; Seek(7); break;
            case 3:
                if (_frames < 8) return;
                h = ShuntaRideHost.Instance;
                _gripZ7 = h != null ? h.Current.gripMultiplier : 1f;
                Debug.Log("[shunta-host-test] dbg distM " + _s.DistanceM.ToString("0") + " course " + _s.courseId + " len " + _s.Course.Length.ToString("0") + " active " + (h != null && h.Active) + " hosts " + Object.FindObjectsByType<ShuntaRideHost>(FindObjectsSortMode.None).Length);
                _draftZ7 = h != null ? h.Current.draftMultiplier : 1f;
                Debug.Log("[shunta-host-test] dbg dist " + _s.DistanceM + " len " + _s.Course.Length + " active " + (h != null && h.Active) + " inst " + (h != null ? h.GetInstanceID() : 0) + " nhosts " + Object.FindObjectsByType<ShuntaRideHost>(FindObjectsSortMode.None).Length);
                Debug.Log("[shunta-host-test] zone1 grip " + _gripZ1.ToString("0.00") + ", zone7 grip " + _gripZ7.ToString("0.00") + " draft " + _draftZ7.ToString("0.00"));
                Check(_gripZ7 < _gripZ1 - 0.1f, "zone 7 (wet) grip lower than zone 1");
                _stage = 4; _frames = 0; Seek(2); break;
            case 4:
                if (_frames < 8) return;
                float d = _s.physics.dragMultiplier;
                Check(d > 0f && d <= 1.0001f, "drag multiplier sane " + d.ToString("0.000"));
                // ---- in-play: zone 4 (tunnel draft), km held constant, drag must not compound ----
                _s.ExternalControl = true;            // (set here, after the live-session refresh above)   RideSession.Tick returns early, so only our SeekTo moves the rider
                _holdM = Seek(4);
                _s.physics.dragMultiplier = 0.9f;     // stand-in for a traffic director writing a draft saving ONCE
                _dMin = float.MaxValue; _dMax = float.MinValue;
                _stage = 5; _frames = 0; break;
            case 5:
                _s.SeekTo(_holdM);
                h = ShuntaRideHost.Instance;
                if (_frames == 2) _expect = 1f - Mathf.Clamp01(0.1f * (h != null ? h.Current.draftMultiplier : 1f));
                if (_frames >= 3 && h != null) { _dMin = Mathf.Min(_dMin, h.DragApplied); _dMax = Mathf.Max(_dMax, h.DragApplied); }
                if (_frames < 123) return;
                h = ShuntaRideHost.Instance;
                Debug.Log("[shunta-host-test] zone4 draft " + h.Current.draftMultiplier.ToString("0.00") + " drag min/max over 120 frames " + _dMin.ToString("0.0000") + "/" + _dMax.ToString("0.0000") + " expected " + _expect.ToString("0.0000"));
                Check(h.Current.draftMultiplier > 1.2f, "zone 4 (tunnel) draft multiplier > 1.2");
                Check(_dMax - _dMin < 1e-4f, "drag multiplier stable over 120 frames at constant km (no compounding)");
                Check(Mathf.Abs(_dMax - _expect) < 1e-3f, "drag = baseline-derived draft saving, not compounded");
                Check(Mathf.Abs(h.DragBaseline - 0.9f) < 1e-4f, "drag baseline preserved (0.9)");
                Check(!h.Boost.Boosting && h.Boost.Collected == 0, "no chevron boost in zone 4");
                // ---- zone 6: collect a chevron ----
                _chev = ShuntaChevronLayout.Place(_data);
                _holdM = (_chev[0].km - 0.02f) * 1000f;
                _stage = 6; _frames = 0; break;
            case 6:
                _holdM += 1f;                           // 1 m per frame, approaching chevron 0
                _s.SeekTo(_holdM);
                h = ShuntaRideHost.Instance;
                if (h.Boost.Collected >= 1)
                {
                    float mult = h.SpeedMultiplierApplied;
                    float exp = Mathf.Max(0.2f, (1f - (1f - h.DragBaseline) * h.Current.draftMultiplier) / (mult * mult));
                    Check(h.Boost.Boosting && mult > 1f, "chevron in zone 6 registers a boost (x" + mult.ToString("0.000") + ")");
                    Check(h.LastChevronId == 0, "collected chevron id 0");
                    Check(Mathf.Abs(h.DragApplied - exp) < 1e-3f && Mathf.Abs(h.DragBaseline - 0.9f) < 1e-4f, "boost drag derived from baseline (" + h.DragApplied.ToString("0.0000") + " vs " + exp.ToString("0.0000") + ")");
                    Check(ShuntaHud.Instance == null || ShuntaHud.Instance.Boost == h.Boost, "HUD Boost linked to host state");
                    _holdM = _chev[1].km * 1000f;       // jump straight onto the next chevron while on cooldown
                    _stage = 7; _frames = 0; break;
                }
                if (_frames > 80) { Check(false, "chevron 0 never collected"); _stage = -1; Finish(); }
                break;
            case 7:
                _s.SeekTo(_holdM);
                if (_frames < 6) return;
                h = ShuntaRideHost.Instance;
                Check(h.Boost.Collected == 1 && h.Boost.OnCooldown, "cooldown blocks the next chevron (collected " + h.Boost.Collected + ")");
                _stage = 8; _frames = 0; break;
            case 8:
                _s.SeekTo(_holdM);
                h = ShuntaRideHost.Instance;
                if (h.Boost.Collected >= 2)
                {
                    Check(true, "after the cooldown expired, the next chevron collects");
                    _stage = 10; Finish(); break;
                }
                if (_frames > 3000) { Check(false, "cooldown never expired"); _stage = -1; Finish(); }
                break;
        }
    }

    // 1-based zone index -> seek to mid-zone
    static float _holdM, _dMin, _dMax, _expect;
    static ShuntaChevron[] _chev;
    static float Seek(int zone)
    {
        var z = _data.zones[Mathf.Clamp(zone - 1, 0, _data.zones.Length - 1)];
        float km = (z.startKm + z.endKm) * 0.5f;
        _s.SeekTo(km * 1000f);
        Debug.Log("[shunta-host-test] seek zone " + zone + " '" + z.id + "' km " + km.ToString("0.0"));
        return km * 1000f;
    }
}

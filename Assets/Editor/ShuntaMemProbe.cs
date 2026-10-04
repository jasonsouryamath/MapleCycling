using System;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

/// <summary>
/// Shunta play-mode memory probe (2026-10-04, after a user-side "System out of memory" crash while playing Shunta). Rides the course for
/// five 25 s phases and logs process private memory every 2.5 s; each phase switches one more subsystem off so the slope that disappears
/// names the leak:  A all on | B cyclist traffic off | C street crowd off | D sky trains + street dressing off | E life director off.
///   run: ShuntaMemProbe.Run   log lines: [shunta-mem]
/// </summary>
[InitializeOnLoad]
public static class ShuntaMemProbe
{
    static ShuntaMemProbe() { EditorApplication.playModeStateChanged += OnMode; }

    const string Key = "mapleride.shunta.memprobe";
    static RideBootstrap _boot; static int _frames, _stage; static double _t0, _nextLog, _phaseStart; static int _phase;
    static long _phaseStartMem;
    static readonly string[] Names = { "A all on", "B cyclists off", "C crowd off", "D skytrains+dressing off", "E life off" };

    public static void Run()
    {
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro), OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnMode(PlayModeStateChange c)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (c == PlayModeStateChange.EnteredPlayMode) { _stage = 0; _frames = 0; _boot = null; _phase = 0; EditorApplication.update += Tick; }
        else if (c == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            Debug.Log("[shunta-mem] left play mode");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    static void Log(string m) { Debug.Log("[shunta-mem] " + m); try { System.IO.File.AppendAllText("shunta_mem_probe.txt", m + System.Environment.NewLine); } catch { } }

    static long Mem() => Process.GetCurrentProcess().PrivateMemorySize64;

    static void Tick()
    {
        try { Step(); } catch (Exception e) { Debug.LogError("[shunta-mem] " + e); EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }
    }

    static void Step()
    {
        if (_stage == 0)
        {
            _boot = UnityEngine.Object.FindFirstObjectByType<RideBootstrap>();
            if (_boot == null || ++_frames < 30) return;
            _boot.Resolve();
            if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.ShuntaMetro)) throw new Exception("FastTravel failed");
            _boot.session.autoLapsFromTarget = false; _boot.session.plannedLaps = 1;
            _boot.devices.acceptKeyboardEffort = false; _boot.devices.HoldZeroPower = false;
            _stage = 1; _t0 = EditorApplication.timeSinceStartup; _nextLog = _t0; _phaseStart = _t0; _phaseStartMem = Mem();
            Debug.Log("[shunta-mem] start, private " + Mem() / 1048576 + " MB");
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        _boot.devices.EffortInput = Environment.GetEnvironmentVariable("MR_MEM_IDLE") == "1" ? 0f : 0.6f;
        if (Environment.GetEnvironmentVariable("MR_MEM_FAST") == "1")
        {
            // stress streaming / pools: advance ~3 m per frame (~180 m/s) and wrap before the finish
            float next = _boot.session.DistanceM + 3f;
            if (next > _boot.session.Course.Length - 200f) next = 100f;
            _boot.session.SeekTo(next); if (_boot.follower != null) _boot.follower.Apply();
        }
        if (now >= _nextLog)
        {
            _nextLog = now + 1.0;
            if (Profiler.GetTotalReservedMemoryLong() > 12L * 1073741824L) { Log("ABORT private > 12 GB in phase " + Names[_phase]); EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); return; }
            Log($"t={now - _t0:F0}s phase {Names[_phase]} private {Mem() / 1048576} MB, unityReserved {Profiler.GetTotalReservedMemoryLong() / 1048576} MB, allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, mono {Profiler.GetMonoUsedSizeLong() / 1048576} MB, gfx {Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576} MB, d={_boot.session.DistanceM:F0} m");
        }
        if (now - _phaseStart >= (Environment.GetEnvironmentVariable("MR_MEM_FAST") == "1" ? 40.0 : 12.0))
        {
            long dm = Mem() - _phaseStartMem;
            Log($"PHASE {Names[_phase]} grew {dm / 1048576} MB in 12 s");
            _phase++; _phaseStart = now; _phaseStartMem = Mem();
            if (_phase >= Names.Length) { EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); return; }
            if (_phase == 1) Off<ShuntaCyclistTraffic>();
            if (_phase == 2) Off<ShuntaStreetCrowd>();
            if (_phase == 3) { Off<ShuntaSkyTrains>(); Off<ShuntaStreetDressing>(); }
            if (_phase == 4) Off<ShuntaLifeDirector>();
        }
    }

    static void Off<T>() where T : MonoBehaviour
    {
        var t = UnityEngine.Object.FindFirstObjectByType<T>();
        if (t == null) { Debug.Log("[shunta-mem] " + typeof(T).Name + " not found"); return; }
        t.enabled = false; for (int i = 0; i < t.transform.childCount; i++) t.transform.GetChild(i).gameObject.SetActive(false);
        Debug.Log("[shunta-mem] disabled " + typeof(T).Name);
    }
}

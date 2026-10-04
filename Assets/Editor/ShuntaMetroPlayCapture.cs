using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play-mode scene QA for Shunta Metro: boots Assets/Scenes/Playable/shunta_metro.unity as a real ride,
/// seeks the rider to the middle of chosen zones and writes one downscaled PNG per zone to docs/shunta_captures/.
/// Run: pwsh tools/unity/run_steps.ps1 "ShuntaMetroPlayCapture.Run|claude_shunta_cap.log|0"
/// Env MR_SHUNTA_ZONES="1,3,4" overrides the zone list; MR_SHUNTA_TAG adds a filename suffix.
/// </summary>
[InitializeOnLoad]
public static class ShuntaMetroPlayCapture
{
    // per-project key: EditorPrefs are shared by every Unity instance of this user (Lab 1 / Lab 2 run concurrently)
    static string PrefKey => "mapleride.shunta.playcapture." + Application.dataPath.GetHashCode();
    const int W = 640, H = 360;
    static int[] _zones = { 1, 3, 4, 6, 7, 8, 10, 12 };
    static string _tag = "";
    static int _stage, _frames, _zi;
    static RideBootstrap _boot; static Camera _cam; static ShuntaCourseData _data;
    static double _deadline; static bool _failed;
    static string _outDir;

    static ShuntaMetroPlayCapture()
    {
        // A killed batch must never arm the next human play session.
        EditorPrefs.DeleteKey("mapleride.shunta.playcapture");
        EditorPrefs.DeleteKey(PrefKey);
        EditorApplication.playModeStateChanged += OnMode;
    }

    public static void Run()
    {
        var z = Environment.GetEnvironmentVariable("MR_SHUNTA_ZONES");
        if (!string.IsNullOrEmpty(z)) { var l = new System.Collections.Generic.List<int>(); foreach (var s in z.Split(',')) if (int.TryParse(s, out var v)) l.Add(v); if (l.Count > 0) _zones = l.ToArray(); }
        _tag = Environment.GetEnvironmentVariable("MR_SHUNTA_TAG") ?? "";
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro), OpenSceneMode.Single);
        SessionState.SetBool(PrefKey, true);
        Debug.Log("[shunta-cap] entering play mode");
        EditorApplication.EnterPlaymode();
    }

    static void OnMode(PlayModeStateChange c)
    {
        if (!SessionState.GetBool(PrefKey, false)) return;
        if (c == PlayModeStateChange.EnteredPlayMode)
        {
            _stage = 0; _frames = 0; _zi = 0; _failed = false; _boot = null;
            _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../docs/shunta_captures"));
            Directory.CreateDirectory(_outDir);
            _deadline = EditorApplication.timeSinceStartup + 900;
            EditorApplication.update += Tick;
        }
        else if (c == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(PrefKey, false);
            EditorApplication.update -= Tick;
            Debug.Log("[shunta-cap] left play mode");
            if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        }
    }

    static void Fail(string why) { Debug.LogError("[shunta-cap] " + why); _failed = true; _stage = 99; }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > _deadline) Fail("timeout");
        if (_stage == 99) { EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); return; }
        try { Step(); } catch (Exception e) { Fail(e.ToString()); }
    }

    static void Step()
    {
        if (_stage == 0)
        {
            _boot = UnityEngine.Object.FindFirstObjectByType<RideBootstrap>();
            if (_boot == null || ++_frames < 30) return;
            _boot.Resolve();
            _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
            if (_cam == null) { Fail("no camera"); return; }
            _data = ShuntaCourseData.Load();
            if (_data == null) { Fail("no course data"); return; }
            if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.ShuntaMetro)) { Fail("FastTravel failed"); return; }
            _boot.session.autoLapsFromTarget = false; _boot.session.plannedLaps = 1;
            _boot.devices.acceptKeyboardEffort = false; _boot.devices.HoldZeroPower = true;
            Debug.Log($"[shunta-cap] booted, course {_boot.session.courseId} len {_boot.session.Course.Length:0} m");
            _stage = 1; _frames = 0; return;
        }
        if (_stage == 1)   // seek
        {
            if (_zi >= _zones.Length) { _stage = 99; Debug.Log("[shunta-cap] DONE"); return; }
            var zone = _data.zones[Mathf.Clamp(_zones[_zi] - 1, 0, _data.zones.Length - 1)];
            float km = (zone.startKm + zone.endKm) * 0.5f;
            float m = km / _data.distanceKm * _boot.session.Course.Length;
            _boot.devices.EffortInput = 0f;
            _boot.session.SeekTo(m);
            if (_boot.follower != null) _boot.follower.Apply();
            _stage = 2; _frames = 0; return;
        }
        if (_stage == 2)   // settle, pin position every frame
        {
            _boot.devices.EffortInput = 0f;
            float pin = _data.zones[Mathf.Clamp(_zones[_zi] - 1, 0, _data.zones.Length - 1)].startKm;
            var zone = _data.zones[Mathf.Clamp(_zones[_zi] - 1, 0, _data.zones.Length - 1)];
            float m = (zone.startKm + zone.endKm) * 0.5f / _data.distanceKm * _boot.session.Course.Length;
            _boot.session.SeekTo(m);
            if (_boot.follower != null) _boot.follower.Apply();
            SnapCamera();
            if (++_frames < 40) return;
            Save(); _zi++; _stage = 1; return;
        }
    }

    static void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    static void Save()
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture; _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = prev;
        var pa = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = pa;
        int z = _zones[_zi];
        string path = Path.Combine(_outDir, $"zone{z:00}{_tag}.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        var rider = _boot.follower != null ? _boot.follower.rider : null;
        // mean brightness for black/blank detection
        var px = tex.GetPixels32(); long sum = 0; for (int i = 0; i < px.Length; i += 7) sum += px[i].r + px[i].g + px[i].b;
        Debug.Log($"[shunta-cap] zone {z} d={_boot.session.DistanceM:0} rider={(rider ? rider.position.ToString("F1") : "null")} cam={_cam.transform.position.ToString("F1")} meanLum={sum / (px.Length / 7 * 3f):0.0} -> {path}");
        UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
    }
}

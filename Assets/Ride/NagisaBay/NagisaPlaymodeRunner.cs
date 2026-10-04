using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// B5 Nagisa Bay play-mode proof (copilot, 2026-09-27; pattern of TakaNpcPlaymodeRunner). Driven by
/// NagisaPlaymodeCapture (Assets/Editor/NagisaBayDiagnostics.cs). Checks, logged
/// "[nagisa-play] PASS/FAIL ...":
///   * the region travels (RegionDirector.FastTravel) and the nagisa_bay_loop course is active;
///   * every roster rider exists with a Kuro body + real bike, moves its own way and pedals, and
///     sits at road height;
///   * the ambient boats MOVE: two frames of the water taxi a few seconds apart from a fixed
///     camera (nagisa_play_mover_1/2.png) plus the measured displacement; boats beyond
///     activeRadiusM do not;
///   * people on foot exist in NB looks and none stands on the road corridor.
/// Frames: reference/good_graphics/nagisa_bay/play/*.png
/// </summary>
public sealed class NagisaPlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public string[] riderNames = new string[0];
    public string regionId = "nagisa_bay";
    public static bool Finished, Failed;

    private int _pass, _fail;
    private RideBootstrap _boot;
    private Camera _cam;
    private RenderTexture _rt;
    private const int W = 1600, H = 900;
    private readonly Dictionary<string, NPCCyclist> _riders = new Dictionary<string, NPCCyclist>();
    private RouteCourse _course;
    private readonly List<float> _frameMs = new List<float>(180);

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[nagisa-play] PASS " : "[nagisa-play] FAIL ") + what);
    }

    private IEnumerator Start()
    {
        Finished = Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        _cam.targetTexture = _rt;
        AmbientCull.CameraOverride = _cam;
        NagisaRiderLod.ViewCamera = _cam;
        yield return null;

        if (_boot.regions != null) _boot.regions.FastTravel(regionId);
        var reg = RegionCatalog.Find(regionId);
        Check(reg != null, $"region '{regionId}' in RegionCatalog");
        if (reg != null && _boot.session.courseId != reg.BuiltCourseId) _boot.session.SelectCourse(reg.BuiltCourseId);
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.HoldZeroPower = true;
        for (int i = 0; i < 10; i++) yield return null;
        _course = _boot.session.Course;
        Check(_course != null && _course.Length > 12000f && _boot.session.courseId == "nagisa_bay_loop",
              $"Nagisa course active ({_boot.session.courseId}, {(_course != null ? _course.Length : 0):0} m)");
        if (_course == null) { Finish(); yield break; }
        var env = GameObject.Find("Nagisa Bay Environment");
        Check(env != null && env.activeInHierarchy, "Nagisa Bay Environment root active after travel");
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;
        SeekPlayer(20f);

        // ---------------------------------------------------------------- wave clock / spray / audio contract
        yield return VerifySurfClockAndSpray();
        VerifyNagisaAudioZones();

        // ---------------------------------------------------------------- frame-time budget (sampled in the same live scene as the visual checks)
        yield return MeasureFrameTime();

        // ---------------------------------------------------------------- riders
        foreach (var c in FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var n in riderNames)
                if (c.gameObject.name == "Nagisa NPC " + n) _riders[n] = c;
        Check(_riders.Count == riderNames.Length, $"all {riderNames.Length} Nagisa riders present (found {_riders.Count})");
        foreach (var kv in _riders)
        {
            var c = kv.Value;
            var bike = FindDeep(c.transform, "Bike");
            int bikeMeshes = bike == null ? 0 : bike.GetComponentsInChildren<MeshRenderer>(true)
                .Count(r => r.enabled && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null);
            int body = c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(s => s.sharedMesh != null && s.enabled);
            int nullMats = c.GetComponentsInChildren<Renderer>(true).Sum(r => r.sharedMaterials.Count(m => m == null));
            Check(c.isActiveAndEnabled && bikeMeshes > 10 && body > 0 && nullMats == 0,
                  $"{kv.Key}: active {c.isActiveAndEnabled}, bike parts {bikeMeshes}, body meshes {body}, null mats {nullMats}");
        }
        var m0 = new Dictionary<string, float>(); var k0 = new Dictionary<string, float>(); var kSum = new Dictionary<string, float>();
        foreach (var kv in _riders) { m0[kv.Key] = CourseM(kv.Value.transform.position); k0[kv.Key] = Crank(kv.Value); kSum[kv.Key] = 0f; }

        // ---------------------------------------------------------------- boats: frame 1
        var boats = FindObjectsByType<AmbientPathMover>(FindObjectsSortMode.None);
        AmbientPathMover taxiMover = null;
        foreach (var bm in boats) if (bm.name.StartsWith("Nagisa_WaterTaxi")) taxiMover = bm;
        if (taxiMover == null && boats.Length > 0) taxiMover = boats[0];
        Check(boats.Length > 0, $"ambient boats present (W0 AmbientPathMover x{boats.Length})");
        Transform taxi = taxiMover != null && taxiMover.cars != null && taxiMover.cars.Length > 0 ? taxiMover.cars[0] : null;
        Vector3 camPos = Vector3.zero, taxi0 = Vector3.zero;
        if (taxi != null)
        {
            SeekPlayer(Mathf.Clamp(CourseM(taxi.position) - 30f, 0f, _course.Length));
            yield return Settle(3);
            camPos = taxi.position + new Vector3(0f, 26f, 0f) - taxi.forward * 70f + taxi.right * 55f;
            _cam.transform.position = camPos;
            _cam.transform.LookAt(taxi.position + taxi.forward * 18f);
            _cam.fieldOfView = 42f;
            yield return Settle(2);
            taxi0 = taxi.position;
            yield return Capture("nagisa_play_mover_1");
        }

        // 4 s of play: riders ride, boats sail
        float t = 0f;
        while (t < 4f)
        {
            yield return null;
            t += Time.deltaTime;
            foreach (var kv in _riders)
            {
                float k = Crank(kv.Value);
                kSum[kv.Key] += Mathf.Abs(Mathf.DeltaAngle(k0[kv.Key], k));
                k0[kv.Key] = k;
            }
        }
        if (taxi != null)
        {
            _cam.transform.position = camPos;
            yield return Capture("nagisa_play_mover_2");
            float moved = Vector3.Distance(taxi0, taxi.position);
            Check(moved > 0.5f * taxiMover.speed * t, $"water taxi ({taxiMover.name}) moved {moved:0.0} m in {t:0.0} s (speed {taxiMover.speed:0.0} m/s), frames mover_1/2");
            Check(Mathf.Abs(taxi.position.y) < 0.6f, $"water taxi at the waterline (y {taxi.position.y:0.00})");
        }
        foreach (var kv in _riders)
        {
            var c = kv.Value;
            float m1 = CourseM(c.transform.position), dm = m1 - m0[kv.Key];
            bool dirOk = c.reverse ? dm < -1f : dm > 1f;
            Check(dirOk, $"{kv.Key} rides {(c.reverse ? "oncoming" : "with the player")}: course {m0[kv.Key]:0} -> {m1:0} m in {t:0.0} s");
            Check(kSum[kv.Key] > 60f, $"{kv.Key} pedals: crank turned {kSum[kv.Key]:0} deg");
            float h = Vector3.Dot(c.transform.position - _course.PositionAt(m1), _course.UpAt(m1).normalized);
            Check(h > -0.05f && h < 0.25f, $"{kv.Key} at road height ({h * 100f:0.0} cm)");
        }

        // far boats freeze: put the camera 3 km away from everything and step a few frames
        if (boats.Length > 0)
        {
            _cam.transform.position = _course.PositionAt(_course.Length * 0.5f) + Vector3.up * 3000f;
            yield return new WaitForSecondsRealtime(0.8f); yield return Settle(3);  // W0 cull check is throttled to 0.35 s
            int active = 0; foreach (var bm in boats) if (!bm.IsCulled) active++;
            Check(active == 0, $"boats freeze beyond {boats[0].cullDistance:0} m (W0 AmbientCull; active {active}/{boats.Length})");
        }

        // ---------------------------------------------------------------- highway traffic motion
        yield return VerifyTrafficMotion();

        // ---------------------------------------------------------------- people
        var people = env != null ? env.GetComponentsInChildren<MinatoCrowdActor>(true) : new MinatoCrowdActor[0];
        int onRoad = 0, helmeted = 0, nb = 0;
        foreach (var p in people)
        {
            float cm = CourseM(p.transform.position);
            Vector3 flat = p.transform.position - _course.PositionAt(cm); flat.y = 0f;
            if (flat.magnitude < 6.5f) onRoad++;
            var look = p.GetComponent<MapleCityLook>();
            if (look == null) look = p.GetComponentInParent<MapleCityLook>();
            if (look == null || string.IsNullOrEmpty(look.look) || !look.civilianAtlas) helmeted++;
            else if (look.look.StartsWith("NB")) nb++;
        }
        Check(people.Length > 20, $"{people.Length} people on foot under the Nagisa root");
        Check(onRoad == 0, $"no person inside the 6.5 m road corridor ({onRoad})");
        Check(helmeted == 0 && nb > people.Length / 2, $"every person wears a civilian look ({people.Length - helmeted}/{people.Length}; NB resort looks {nb})");

        // ---------------------------------------------------------------- frames
        yield return Wide("nagisa_play_promenade", 3000f, 8f, 16f);
        yield return Wide("nagisa_play_marina", 300f, 7f, 0f);
        yield return Wide("nagisa_play_climb", 5800f, 6f, 0f);
        foreach (var n in new[] { "Leilani", "Kaimana", "Kailani" })
            if (_riders.TryGetValue(n, out var c)) yield return Roadside(c, $"nagisa_play_rider_{n}");
        Finish();
    }

    private IEnumerator Wide(string shot, float m, float height, float seaward)
    {
        SeekPlayer(Mathf.Max(0f, m - 40f));
        Vector3 p = _course.PositionAt(m - 30f), look = _course.PositionAt(m + 60f);
        Vector3 side = _course.SideAt(m);
        _cam.transform.position = p + Vector3.up * height + side * seaward;
        _cam.transform.LookAt(look + Vector3.up * 1f + side * seaward * 0.5f);
        _cam.fieldOfView = 55f;
        yield return Settle(6);
        yield return Capture(shot);
    }

    private IEnumerator Roadside(NPCCyclist c, string shot)
    {
        float m = CourseM(c.transform.position);
        float ahead = c.reverse ? -10f : 10f;
        SeekPlayer(Mathf.Clamp(m - (c.reverse ? -60f : 60f), 0f, _course.Length));
        Vector3 road = _course.PositionAt(m + ahead), side = _course.SideAt(m + ahead);
        _cam.transform.position = road + side * (c.reverse ? 4.2f : -4.2f) + Vector3.up * 1.7f;
        _cam.fieldOfView = 42f;
        yield return Settle(2);
        for (int k = 0; k < 2; k++)
        {
            _cam.transform.LookAt(c.transform.position + Vector3.up * 0.75f);
            yield return Capture($"{shot}_{k + 1}");
            yield return Wait(1.1f);
        }
    }

    private void SeekPlayer(float m)
    {
        _boot.session.SeekTo(m);
        if (_boot.follower != null) _boot.follower.Apply();
    }

    private IEnumerator Settle(int frames) { for (int i = 0; i < frames; i++) yield return null; }

    private IEnumerator Wait(float s) { float t = 0f; while (t < s) { yield return null; t += Time.deltaTime; } }

    private IEnumerator VerifySurfClockAndSpray()
    {
        var surf = FindFirstObjectByType<NagisaSurf>();
        Check(surf != null && NagisaSurf.HasField, $"Nagisa wave field driver present (field {NagisaSurf.HasField})");
        if (surf != null)
        {
            float t0 = NagisaSurf.Now;
            float shader0 = Shader.GetGlobalFloat("_NagisaSurfTime");
            yield return new WaitForSecondsRealtime(0.65f);
            float dt = NagisaSurf.Now - t0;
            float shaderDt = Shader.GetGlobalFloat("_NagisaSurfTime") - shader0;
            Check(dt > 0.20f, $"wave clock advances ({dt:0.000}s in play mode)");
            Check(shaderDt > 0.20f && Mathf.Abs(shaderDt - dt) < 0.15f,
                  $"wave shader clock follows runtime ({shaderDt:0.000}s vs {dt:0.000}s)");
        }

        var sprays = FindObjectsByType<NagisaSurfSpray>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int burstPoints = 0, playingSystems = 0;
        foreach (var spray in sprays)
        {
            burstPoints += spray.burstPoints == null ? 0 : spray.burstPoints.Length;
            var ps = spray.GetComponentInChildren<ParticleSystem>(true);
            if (ps != null && ps.isPlaying) playingSystems++;
        }
        Check(sprays.Length > 0 && burstPoints > 0,
              $"surf spray staged ({sprays.Length} emitters, {burstPoints} breakwater points)");
        Check(playingSystems > 0, $"surf spray particle system live ({playingSystems}/{sprays.Length})");
    }

    private void VerifyNagisaAudioZones()
    {
        // The clips are a content contract; the AudioSource check catches a missing zone director
        // even when the WAVs import successfully. The runtime director remains owned by worker H.
        string[] clips = { "surf_loop", "traffic_far", "marina_lap", "beach_crowd", "plaza_loop" };
        int loaded = 0;
        foreach (var clip in clips) if (Resources.Load<AudioClip>("Audio/Nagisa/Amb/" + clip) != null) loaded++;
        Check(loaded == clips.Length, $"Nagisa zone clips load ({loaded}/{clips.Length})");

        int zoneSources = 0;
        foreach (var src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (src.clip == null) continue;
            string n = src.clip.name;
            if (clips.Contains(n)) zoneSources++;
        }
        Check(zoneSources > 0,
              zoneSources > 0
                  ? $"Nagisa positional zone sources live ({zoneSources})"
                  : "Nagisa positional zone sources missing (clips exist; audio director/emitters not staged)");
        if (zoneSources == 0)
            Debug.LogWarning("[nagisa-play] FIX LIST audio: stage the H-owned positional zone director and verify surf, beach, marina, resort, and traffic sources play while riding.");
    }

    private IEnumerator VerifyTrafficMotion()
    {
        var movers = new List<AmbientPathMover>();
        foreach (var tag in FindObjectsByType<NagisaTrafficVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mover = tag.GetComponent<AmbientPathMover>();
            if (mover != null && !mover.IsCulled && mover.speed > 0.1f) movers.Add(mover);
        }
        Check(movers.Count > 0, $"coastal highway traffic movers active ({movers.Count})");
        if (movers.Count == 0) yield break;

        int sampleCount = Mathf.Min(8, movers.Count);
        var before = new Vector3[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            var m = movers[i];
            before[i] = m.cars != null && m.cars.Length > 0 && m.cars[0] != null ? m.cars[0].position : m.transform.position;
        }
        yield return new WaitForSecondsRealtime(1.25f);
        int moved = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            var m = movers[i];
            Vector3 now = m.cars != null && m.cars.Length > 0 && m.cars[0] != null ? m.cars[0].position : m.transform.position;
            if (Vector3.Distance(before[i], now) > Mathf.Max(0.25f, m.speed * 1.25f * 0.25f)) moved++;
        }
        Check(moved > 0, $"highway traffic advances ({moved}/{sampleCount} sampled vehicles in 1.25s)");
        if (moved == 0)
            Debug.LogWarning("[nagisa-play] FIX LIST traffic: active coastal AmbientPathMover vehicles did not advance; inspect culling, path points, and mover speed.");
    }

    private IEnumerator MeasureFrameTime()
    {
        _frameMs.Clear();
        for (int i = 0; i < 150; i++)
        {
            yield return null;
            _frameMs.Add(Time.unscaledDeltaTime * 1000f);
        }
        if (_frameMs.Count == 0) yield break;
        var ordered = _frameMs.OrderBy(v => v).ToArray();
        float avg = _frameMs.Average();
        float p95 = ordered[Mathf.Clamp(Mathf.CeilToInt(ordered.Length * 0.95f) - 1, 0, ordered.Length - 1)];
        float max = ordered[ordered.Length - 1];
        Check(avg <= 33.3f && p95 <= 50f, $"frame time budget avg {avg:0.0}ms p95 {p95:0.0}ms max {max:0.0}ms (150 samples)");
        if (avg > 33.3f || p95 > 50f)
            Debug.LogWarning($"[nagisa-play] FIX LIST frame time: avg {avg:0.0}ms / p95 {p95:0.0}ms exceeds 30 FPS avg / 20 FPS worst-case budget; inspect crowd, ocean spray, and LOD activation.");
    }

    private static float Crank(NPCCyclist c)
    {
        var rig = c.GetComponentInChildren<KuroBikeRig>();
        return rig != null ? rig.CrankAngleDegrees : 0f;
    }

    private float CourseM(Vector3 p)
    {
        float best = 0f, bestD = float.MaxValue;
        for (float m = 0f; m <= _course.Length; m += 3f)
        {
            float d = (_course.PositionAt(m) - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = m; }
        }
        for (float m = best - 3f; m <= best + 3f; m += 0.25f)
        {
            float d = (_course.PositionAt(Mathf.Clamp(m, 0f, _course.Length)) - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform ch in root) { var f = FindDeep(ch, name); if (f != null) return f; }
        return null;
    }

    private IEnumerator Capture(string name)
    {
        yield return null;   // not WaitForEndOfFrame: it never fires in -batchmode
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
    }

    private void Fail(string why)
    {
        Debug.LogError("[nagisa-play] " + why);
        _fail++;
        Finish();
    }

    private void Finish()
    {
        Failed = _fail > 0;
        AmbientCull.CameraOverride = null;
        NagisaRiderLod.ViewCamera = null;
        Debug.Log($"[nagisa-play] RESULT {(Failed ? "FAIL" : "PASS")} {_pass}/{_pass + _fail}");
        Finished = true;
    }
}

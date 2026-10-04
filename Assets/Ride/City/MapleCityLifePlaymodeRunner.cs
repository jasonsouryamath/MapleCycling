using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Play-mode proof for the Maple City LIFE pass (claude-city): pedestrians actually walk,
/// cafes are populated and their customers act. HUD-free frames (the ride camera only) go to
/// reference/good_graphics/maple_life/. Driven by MapleCityLifeCapture (editor).
///
/// Frames: chase views round the lap, a street-level walker close-up (feet on the pavement?),
/// and for up to four cafes a front view, a terrace close-up and the same terrace 1.3 s later
/// (the cup should have moved).
/// </summary>
public class MapleCityLifePlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public float[] chaseStations = { 120f, 900f, 1600f, 2500f, 3400f, 4300f };
    public float[] sidewalkStations = { 200f, 1200f, 2000f, 2800f };

    public static bool Finished, Failed;
    private int _pass, _fail;
    private RideBootstrap _boot;
    private Camera _cam;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[maple-life-play] PASS " : "[maple-life-play] FAIL ") + what);
    }

    private IEnumerator Start()
    {
        Finished = Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        yield return null;
        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.MapleCity))
            Debug.LogWarning("[maple-life-play] could not fast travel to Maple City.");
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse(RegionCatalog.Find(RegionCatalog.MapleCity).BuiltCourseId);
        _boot.devices.acceptKeyboardEffort = false;
        yield return null;
        yield return null;

        var life = GameObject.Find("Maple City Life");
        Check(life != null, "'Maple City Life' is in the scene and active");
        if (life == null) { Fail("no life group"); yield break; }

        var walkers = life.GetComponentsInChildren<MapleCityWalker>(true);
        var gestures = life.GetComponentsInChildren<MapleCityCafeGesture>(true);
        Check(walkers.Length > 100, $"{walkers.Length} walkers");
        Check(gestures.Length > 10, $"{gestures.Length} cafe/chat figures with gestures");

        // --- C8 population rules (copilot): no helmeted figure walks, runs, stands or sits.
        PopulationCheck(life.transform);
        var runners = life.GetComponentsInChildren<MapleCityRunner>(true);
        int runReady = runners.Count(r => r.Ready);
        Check(runners.Length >= 40 && runners.Length <= 80 && runReady == runners.Length,
              $"{runners.Length} runners, {runReady} with a retargeted run cycle");
        // Sample only figures the camera would actually see move: walkers stop updating beyond
        // MapleCityWalker.updateDistance (260 m) and runners beyond MapleCityRunner.animationDistance
        // (90 m) by design. Sampling "the first N anywhere" made these checks pass or fail
        // depending on where the camera happened to be (claude, 2026-09-25).
        // So the sampled figures are ungated for the 1.5 s test (restored right after).
        var gatedRunners = runners.Take(12).ToArray();
        var gatedWalkers = walkers.Take(40).ToArray();
        var runGate = gatedRunners.Select(r => r.animationDistance).ToArray();
        var walkGate = gatedWalkers.Select(w => w.updateDistance).ToArray();
        // A runner is ALSO a MapleCityWalker, and walkers beyond updateDistance re-place only
        // every 30th frame - so a far runner read stale positions and this check's pass rate
        // tracked the frame rate (11/12 -> 4/12 as the scene got heavier). Ungate both.
        var runWalkGate = gatedRunners.Select(r => r.walker != null ? r.walker.updateDistance : 0f).ToArray();
        foreach (var r in gatedRunners)
        {
            r.animationDistance = 1e5f;
            if (r.walker != null) r.walker.updateDistance = 1e5f;
        }
        foreach (var w in gatedWalkers) w.updateDistance = 1e5f;
        var runBefore = runners.Take(12).Select(r => r.transform.position).ToArray();

        // --- walkers move
        var before = walkers.Take(40).Select(w => w.transform.position).ToArray();
        for (float t = 0f; t < 1.5f; t += Time.deltaTime) yield return null;
        int moved = 0;
        for (int k = 0; k < before.Length; k++)
            if ((walkers[k].transform.position - before[k]).sqrMagnitude > 0.25f * 0.25f) moved++;
        Check(moved >= before.Length * 0.9f, $"{moved}/{before.Length} sampled walkers moved in 1.5 s");
        int ranOn = 0;
        for (int k = 0; k < runBefore.Length; k++)
            if ((runners[k].transform.position - runBefore[k]).sqrMagnitude > 3f * 3f) ranOn++;
        Check(runBefore.Length > 0 && ranOn >= runBefore.Length * 0.9f,
              $"{ranOn}/{runBefore.Length} sampled runners covered > 3 m in 1.5 s");
        for (int k = 0; k < gatedRunners.Length; k++)
        {
            gatedRunners[k].animationDistance = runGate[k];
            if (gatedRunners[k].walker != null) gatedRunners[k].walker.updateDistance = runWalkGate[k];
        }
        for (int k = 0; k < gatedWalkers.Length; k++) gatedWalkers[k].updateDistance = walkGate[k];

        // --- chase frames round the lap
        foreach (float d in chaseStations)
        {
            yield return Seek(d, 0.6f, 30);
            yield return Capture($"life_chase_{d:0000}m");
        }

        var follow = _cam.GetComponent<KuroFollowCamera>();

        // --- a walker close-up, from the road, to check feet on the pavement and the gait
        yield return Seek(1600f, 0f, 20);
        var rider = follow != null ? follow.target : null;
        if (rider != null)
        {
            var near = walkers.Where(w => w.GetComponent<MapleCityRunner>() == null)
                              .OrderBy(w => (w.transform.position - rider.position).sqrMagnitude).First();
            if (follow != null) follow.enabled = false;
            for (int f = 0; f < 3; f++)
            {
                var w = near.transform;
                _cam.transform.position = w.position + w.right * 3.2f + w.forward * 1.5f + Vector3.up * 1.0f;
                _cam.transform.LookAt(w.position + Vector3.up * 0.6f);
                yield return Capture($"life_walker_close_{f}");
                for (float t = 0f; t < 0.35f; t += Time.deltaTime) yield return null;
            }
            if (follow != null) follow.enabled = true;
        }

        // --- C8: sidewalk views (from the pavement, behind the nearest pedestrian) and a
        // side-on runner close-up tracking the gait
        foreach (float d in sidewalkStations)
        {
            yield return Seek(d, 0f, 20);
            var r0 = follow != null ? follow.target : null;
            if (r0 == null) break;
            var ped = walkers.Where(w => w.GetComponent<MapleCityRunner>() == null)
                             .OrderBy(w => (w.transform.position - r0.position).sqrMagnitude).First().transform;
            if (follow != null) follow.enabled = false;
            _cam.transform.position = ped.position - ped.forward * 6f + Vector3.up * 1.6f;
            _cam.transform.LookAt(ped.position + ped.forward * 4f + Vector3.up * 0.7f);
            yield return Capture($"life_sidewalk_{d:0000}m");
            if (follow != null) follow.enabled = true;
        }
        yield return Seek(1600f, 0f, 20);
        var r1 = follow != null ? follow.target : null;
        if (r1 != null && runners.Length > 0)
        {
            var runner = runners.OrderBy(r => (r.transform.position - r1.position).sqrMagnitude).First().transform;
            if (follow != null) follow.enabled = false;
            for (int f = 0; f < 4; f++)
            {
                // one frame near it so it poses, then frame it where it is NOW and render at once
                PlaceSideOn(runner);
                for (float t = 0f; t < 0.09f + f * 0.03f; t += Time.deltaTime) yield return null;
                PlaceSideOn(runner);
                CaptureNow($"life_runner_close_{f}");
            }
            if (follow != null) follow.enabled = true;
        }

        // --- cafes
        var cafes = new List<(Transform t, float d)>();
        foreach (Transform c in life.transform.Find("Cafes"))
        {
            int at = c.name.IndexOf('@');
            if (at > 0 && float.TryParse(c.name.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                cafes.Add((c, d));
        }
        Check(cafes.Count >= 6, $"{cafes.Count} cafes");
        foreach (var (cafe, d) in cafes.Take(4))
        {
            yield return Seek(Mathf.Max(5f, d - 30f), 0.6f, 20);
            yield return Capture($"life_cafe_{cafe.name.Split('@')[0]}_approach");
            yield return Seek(d, 0f, 20);
            if (follow != null) follow.enabled = false;
            var fwd = cafe.forward; var right = cafe.right;
            _cam.transform.position = cafe.position + fwd * 9.5f + Vector3.up * 1.7f;
            _cam.transform.LookAt(cafe.position + fwd * 1.0f + Vector3.up * 1.6f);
            yield return Capture($"life_cafe_{cafe.name.Split('@')[0]}_front");
            _cam.transform.position = cafe.position + fwd * 4.2f + right * 2.6f + Vector3.up * 1.25f;
            _cam.transform.LookAt(cafe.position + fwd * 1.25f + Vector3.up * 0.55f);
            yield return Capture($"life_cafe_{cafe.name.Split('@')[0]}_terrace_a");
            for (float t = 0f; t < 1.3f; t += Time.deltaTime) yield return null;
            yield return Capture($"life_cafe_{cafe.name.Split('@')[0]}_terrace_b");
            if (follow != null) follow.enabled = true;
        }

        Debug.Log($"[maple-life-play] RESULT {_pass} passed, {_fail} failed; frames in {outDir}");
        Failed = _fail > 0;
        Finished = true;
    }

    private IEnumerator Seek(float d, float effort, int frames)
    {
        _boot.session.SeekTo(d);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < frames; f++) { _boot.devices.EffortInput = effort; yield return null; }
        SnapCamera();
    }

    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null || !follow.enabled) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    private void Fail(string why)
    {
        Debug.LogError("[maple-life-play] " + why);
        Failed = true;
        Finished = true;
    }

    /// <summary>
    /// C8: every crowd figure in Maple City must be either a cyclist ON a bike (motion Cyclist) or
    /// wear a civilian/running look with a hair cap over the donor's baked-in helmet. FAILS if
    /// any Life figure (walker, runner, sitter, cafe regular, chat) is unmasked, or any other
    /// crowd figure stands/walks/sits under the Maple City root in its cycling kit.
    /// </summary>
    private void PopulationCheck(Transform life)
    {
        var perRole = new SortedDictionary<string, int>();
        var bad = new List<string>();
        int figures = 0;
        foreach (var a in life.GetComponentsInChildren<MinatoCrowdActor>(true))
        {
            figures++;
            var look = a.GetComponent<MapleCityLook>();
            string role = look != null ? look.role.ToString() : "Untagged";
            perRole[role] = perRole.TryGetValue(role, out int n) ? n + 1 : 1;
            if (look == null || !look.Masked || !HelmetSunk(a.transform)) bad.Add(a.name);
        }
        var city = life.parent;
        int others = 0;
        if (city != null)
            foreach (var a in city.GetComponentsInChildren<MinatoCrowdActor>(true))
            {
                if (a.transform.IsChildOf(life)) continue;
                others++;
                if (a.motion == MinatoCrowdActor.MotionKind.Cyclist) continue;
                var look = a.GetComponent<MapleCityLook>();
                if (look == null || !look.Masked) bad.Add(a.name);
            }
        // Outside Maple City (e.g. the Minato Coast promenade the Life cast is cloned from):
        // reported, not failed - those regions have their own builders.
        int elsewhere = 0;
        foreach (var a in FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if ((city == null || !a.transform.IsChildOf(city)) && a.motion != MinatoCrowdActor.MotionKind.Cyclist &&
                a.GetComponent<MapleCityLook>() == null)
                elsewhere++;
        string roles = string.Join(", ", perRole.Select(p => $"{p.Key} {p.Value}"));
        Check(figures > 0 && bad.Count == 0,
              $"helmeted pedestrians = {bad.Count} in Maple City ({figures} Life figures: {roles}; " +
              $"{others} other crowd figures under the city root)" +
              (bad.Count > 0 ? " e.g. " + string.Join(", ", bad.Take(6)) : ""));
        Debug.Log($"[maple-life-play] INFO {elsewhere} kitted crowd figures outside Maple City (other regions, not checked).");
    }

    /// <summary>The live skin wears the per-donor copy with the helmet shell sunk under the cap.</summary>
    private static bool HelmetSunk(Transform figure)
    {
        foreach (var smr in figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.name == "Mesh_0")
                return smr.sharedMesh != null && smr.sharedMesh.name.StartsWith("MapleLife_Helmetless_");
        return false;
    }

    private void PlaceSideOn(Transform t)
    {
        _cam.transform.position = t.position + t.right * 2.6f + t.forward * 0.4f + Vector3.up * 0.75f;
        _cam.transform.LookAt(t.position + t.forward * 0.25f + Vector3.up * 0.5f);
    }

    /// <summary>Render immediately (no frame wait) so a fast-moving subject stays framed.</summary>
    private void CaptureNow(string name)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);
        Debug.Log($"[maple-life-play] {name}: d {_boot.session.DistanceM:0} m");
    }

    private IEnumerator Capture(string name)
    {
        yield return null;
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);
        Debug.Log($"[maple-life-play] {name}: d {_boot.session.DistanceM:0} m");
    }
}

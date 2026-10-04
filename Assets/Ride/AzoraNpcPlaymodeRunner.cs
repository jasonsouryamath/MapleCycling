using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Play-mode proof for the Azora Highlands MOVING CYCLISTS (winter) (AzoraNpcRoster.MovingCyclists;
/// copilot A2 2026-09-26, a copy of TakaNpcPlaymodeRunner). Driven by the editor-side
/// AzoraNpcPlaymodeCapture, which passes the rider names.
///
/// Checks (logged "[azora-move] PASS/FAIL ..."):
///   * every named rider exists, is active, has a Kuro-based body and a bike with real meshes;
///   * over a few seconds every rider MOVES along the road in its own direction (ascenders
///     gain course metres, descenders lose them) and its crank angle CHANGES (pedalling);
///   * each rider's root stays at road height (no floating / sinking) against the course line;
/// Frames: reference/good_graphics/azora_concept/moving/*.png - roadside sequences, pedalling
/// close-ups, a Kuro-beside-rider scale shot, and zone wide shots (lake, village, river,
/// second village, ridge, summit).
/// </summary>
public sealed class AzoraNpcPlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public string[] riderNames = new string[0];
    public static bool Finished, Failed;

    private int _pass, _fail;
    private RideBootstrap _boot;
    private Camera _cam;
    private RenderTexture _rt;
    private const int W = 1600, H = 900;
    private readonly Dictionary<string, NPCCyclist> _riders = new Dictionary<string, NPCCyclist>();
    private RouteCourse _course;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[azora-move] PASS " : "[azora-move] FAIL ") + what);
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
        AzoraRiderLod.ViewCamera = _cam;
        yield return null;

        // --- travel to Azora, player parked
        if (_boot.regions != null) _boot.regions.FastTravel(RegionCatalog.AzoraHighlands);
        var reg = RegionCatalog.Find(RegionCatalog.AzoraHighlands);
        if (reg != null && _boot.session.courseId != reg.BuiltCourseId) _boot.session.SelectCourse(reg.BuiltCourseId);
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.HoldZeroPower = true;
        for (int i = 0; i < 10; i++) yield return null;
        _course = _boot.session.Course;
        Check(_course != null && _course.Length > 15000f, $"Azora course active ({_boot.session.courseId}, {(_course != null ? _course.Length : 0):0} m)");
        if (_course == null) { Finish(); yield break; }
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;
        SeekPlayer(20f);

        // --- roster present
        foreach (var c in FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var n in riderNames)
                if (c.gameObject.name == "Azora NPC " + n) _riders[n] = c;
        Check(_riders.Count == riderNames.Length, $"all {riderNames.Length} moving cyclists present (found {_riders.Count})");
        foreach (var n in riderNames)
        {
            if (!_riders.TryGetValue(n, out var c)) { Check(false, $"{n} missing"); continue; }
            var bike = FindDeep(c.transform, "Bike");
            int bikeMeshes = bike == null ? 0 : bike.GetComponentsInChildren<MeshRenderer>(true)
                .Count(r => r.enabled && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null);
            int body = c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(s => s.sharedMesh != null && s.enabled);
            int nullMats = c.GetComponentsInChildren<Renderer>(true).Sum(r => r.sharedMaterials.Count(m => m == null));
            Check(c.isActiveAndEnabled && bikeMeshes > 10 && body > 0 && nullMats == 0,
                  $"{n}: active {c.isActiveAndEnabled}, bike parts {bikeMeshes}, body meshes {body}, null materials {nullMats}, reverse {c.reverse}, speed {c.speed:0.00} m/s");
        }

        // --- everyone moves the right way and pedals (5 s of play)
        var m0 = new Dictionary<string, float>();
        var p0 = new Dictionary<string, Vector3>();
        var k0 = new Dictionary<string, float>();
        var kSum = new Dictionary<string, float>();
        foreach (var kv in _riders)
        {
            m0[kv.Key] = CourseM(kv.Value.transform.position);
            p0[kv.Key] = kv.Value.transform.position;
            k0[kv.Key] = Crank(kv.Value);
            kSum[kv.Key] = 0f;
        }
        float t = 0f;
        while (t < 5f)
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
        foreach (var kv in _riders)
        {
            var c = kv.Value;
            float m1 = CourseM(c.transform.position);
            float dm = m1 - m0[kv.Key];
            float moved = Vector3.Distance(p0[kv.Key], c.transform.position);
            bool dirOk = c.reverse ? dm < -1f : dm > 1f;
            Check(moved > 0.6f * c.speed * t && dirOk,
                  $"{kv.Key} rides {(c.reverse ? "DOWN" : "UP")}: moved {moved:0.0} m in {t:0.0} s (expected ~{c.speed * t:0.0}), course {m0[kv.Key]:0} -> {m1:0} m");
            Check(kSum[kv.Key] > 90f, $"{kv.Key} pedals: crank turned {kSum[kv.Key]:0} deg in {t:0.0} s");
            float h = HeightAboveRoad(c.transform.position, m1);
            Check(h > -0.05f && h < 0.25f, $"{kv.Key} at road height: root {h * 100f:0.0} cm above the course line at {m1:0} m");
        }

        // --- zone wide shots (camera over the road, looking up the route)
        yield return Wide("azora_move_lake_wide", 1150f);
        yield return Wide("azora_move_village_wide", 2450f);
        yield return Wide("azora_move_river_wide", 4550f);
        yield return Wide("azora_move_village2_wide", 12450f);
        yield return Wide("azora_move_ridge_wide", 15150f);
        // WP-H3: club groups and the slow cafe roller
        yield return Wide("azora_move_lakegroup_wide", 700f);
        yield return Wide("azora_move_falseflat_wide", 10810f);
        yield return Wide("azora_move_ridgegroup_wide", 16640f);

        // --- roadside sequences + pedalling close-ups + scale beside Kuro
        foreach (var (name, tag) in new[] { ("Noah", "lake"), ("Lea", "village"), ("Elias", "village"),
                                           ("Seraina", "village2"), ("Reto", "ridge"), ("Gian", "summit") })
        {
            if (!_riders.TryGetValue(name, out var c)) continue;
            yield return Roadside(c, $"azora_move_{tag}_{name}");
            yield return CloseUp(c, $"azora_move_{tag}_{name}_pedal");
        }
        foreach (var (name, tag) in new[] { ("Andrin", "lakegroup"), ("Pascal", "quartet"), ("Rahel", "ridgegroup"),
                                           ("Urs", "couple"), ("Timo", "cafe") })
        {
            if (!_riders.TryGetValue(name, out var c)) continue;
            yield return Roadside(c, $"azora_move_{tag}_{name}");
        }
        if (_riders.TryGetValue("Laura", out var rr)) yield return BesideKuro(rr, "azora_move_scale_Laura");

        Finish();
    }

    // ---------------------------------------------------------------- shots

    private IEnumerator Wide(string shot, float m)
    {
        SeekPlayer(Mathf.Max(0f, m - 40f));
        Vector3 p = _course.PositionAt(m - 30f), look = _course.PositionAt(m + 60f);
        _cam.transform.position = p + Vector3.up * 7f;
        _cam.transform.LookAt(look + Vector3.up * 1f);
        _cam.fieldOfView = 55f;
        yield return Settle(6);
        int near = _riders.Values.Count(c => Mathf.Abs(CourseM(c.transform.position) - m) < 160f);
        yield return Capture(shot);
        Debug.Log($"[azora-move] {shot}: {near} moving cyclists within 160 m of {m:0} m");
    }

    private IEnumerator CloneShot(Transform[] clones)
    {
        var c = clones[0].position;
        foreach (var x in clones.Skip(1)) c += x.position;
        c /= clones.Length;
        SeekPlayer(Mathf.Max(0f, CourseM(c) - 30f));
        Vector3 dir = (c - _course.PositionAt(CourseM(c))); dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.right;
        _cam.transform.position = c - dir.normalized * 9f + Vector3.up * 2.5f;
        _cam.transform.LookAt(c + Vector3.up * 0.7f);
        _cam.fieldOfView = 45f;
        yield return Settle(4);
        yield return Capture("azora_move_clones");
        foreach (var x in clones) Debug.Log($"[azora-move] clone {x.name} at {x.position:F1}");
    }

    /// <summary>Fixed roadside camera a few metres ahead; the rider rides through three frames.</summary>
    private IEnumerator Roadside(NPCCyclist c, string shot)
    {
        float m = CourseM(c.transform.position);
        float ahead = c.reverse ? -10f : 10f;
        SeekPlayer(Mathf.Clamp(m - (c.reverse ? -60f : 60f), 0f, _course.Length));
        Vector3 road = _course.PositionAt(m + ahead);
        Vector3 side = _course.SideAt(m + ahead);
        float lateral = c.reverse ? 4.2f : -4.2f;   // stand on the rider's own kerb side
        _cam.transform.position = road + side * lateral + Vector3.up * 1.7f;
        _cam.fieldOfView = 42f;
        yield return Settle(2);
        var rig = c.GetComponentInChildren<KuroBikeRig>();
        for (int k = 0; k < 3; k++)
        {
            _cam.transform.LookAt(c.transform.position + Vector3.up * 0.75f);
            yield return Capture($"{shot}_{k + 1}");
            Debug.Log($"[azora-move] {shot}_{k + 1}: {c.name} at {c.transform.position:F2} course {CourseM(c.transform.position):0.0} m, crank {Crank(c):0} deg, lod {Lod(c)}");
            yield return Wait(1.1f);
        }
    }

    /// <summary>Three-quarter front close-up that travels with the rider: two crank phases.</summary>
    private IEnumerator CloseUp(NPCCyclist c, string shot)
    {
        for (int k = 0; k < 2; k++)
        {
            var tr = c.transform;
            Vector3 centre = tr.position + Vector3.up * 0.7f;
            _cam.transform.position = centre + tr.forward * 2.9f + tr.right * 1.9f + Vector3.up * 0.4f;
            _cam.transform.LookAt(centre);
            _cam.fieldOfView = 40f;
            yield return null;
            _cam.transform.position = tr.position + Vector3.up * 0.7f + tr.forward * 2.9f + tr.right * 1.9f + Vector3.up * 0.4f;
            _cam.transform.LookAt(tr.position + Vector3.up * 0.7f);
            yield return Capture($"{shot}_{k + 1}");
            Debug.Log($"[azora-move] {shot}_{k + 1}: crank {Crank(c):0} deg, lod {Lod(c)}");
            yield return Wait(0.45f);
        }
    }

    /// <summary>Kuro parked beside a passing rider, both side-on: the scale comparison.</summary>
    private IEnumerator BesideKuro(NPCCyclist c, string shot)
    {
        float m = CourseM(c.transform.position);
        SeekPlayer(m);
        yield return Settle(2);
        m = CourseM(c.transform.position);
        SeekPlayer(m);
        Transform kuro = _boot.follower != null ? _boot.follower.rider : null;
        Vector3 mid = kuro != null ? (kuro.position + c.transform.position) * 0.5f : c.transform.position;
        Vector3 tan = _course.TangentAt(m), side = _course.SideAt(m);
        _cam.transform.position = mid + tan * 6.5f + side * 0.6f + Vector3.up * 1.2f;
        _cam.transform.LookAt(mid + Vector3.up * 0.6f);
        _cam.fieldOfView = 45f;
        yield return null;
        yield return Capture(shot);
        if (kuro != null)
            Debug.Log($"[azora-move] {shot}: Kuro at {kuro.position:F2}, {c.name} at {c.transform.position:F2} (gap {Vector3.Distance(kuro.position, c.transform.position):0.00} m)");
    }

    // ---------------------------------------------------------------- helpers

    private void SeekPlayer(float m)
    {
        _boot.session.SeekTo(m);
        if (_boot.follower != null) _boot.follower.Apply();
    }

    private IEnumerator Settle(int frames) { for (int i = 0; i < frames; i++) yield return null; }

    private IEnumerator Wait(float s)
    {
        float t = 0f;
        while (t < s) { yield return null; t += Time.deltaTime; }
    }

    private static float Crank(NPCCyclist c)
    {
        var rig = c.GetComponentInChildren<KuroBikeRig>();
        return rig != null ? rig.CrankAngleDegrees : 0f;
    }

    private static int Lod(NPCCyclist c)
    {
        var l = c.GetComponent<AzoraRiderLod>();
        return l != null ? l.Level : -1;
    }

    /// <summary>Nearest course metre to a world point (3 m search, then refine).</summary>
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

    /// <summary>Rider root height above the banked road surface at its lateral position.</summary>
    private float HeightAboveRoad(Vector3 p, float m)
    {
        Vector3 c = _course.PositionAt(m), up = _course.UpAt(m);
        return Vector3.Dot(p - c, up.sqrMagnitude > 0.01f ? up.normalized : Vector3.up);
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
        Debug.LogError("[azora-move] " + why);
        _fail++;
        Finish();
    }

    private void Finish()
    {
        Failed = _fail > 0;
        AzoraRiderLod.ViewCamera = null;
        Debug.Log($"[azora-move] RESULT {(Failed ? "FAIL" : "PASS")} {_pass}/{_pass + _fail}");
        Finished = true;
    }
}

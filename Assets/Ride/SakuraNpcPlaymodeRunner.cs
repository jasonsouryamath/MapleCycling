using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Play-mode proof for the Sakura Pass N1 NPC layer (claude 2026-09-30; a copy of
/// TakaNpcPlaymodeRunner): the MOVING + STOPPED cyclists (SakuraNpcRoster.MovingCyclists) and the
/// townsfolk on foot (SakuraPassTownsfolk, root "Sakura Townsfolk"). Driven by the editor-side
/// SakuraNpcPlaymodeCapture, which passes the rider names.
///
/// Checks (logged "[sakura-npc] PASS/FAIL ..."):
///   * every named rider exists, is active, has a Kuro body, a bike with real meshes, no null mats;
///   * moving riders MOVE along the road in their own direction and pedal; STOPPED riders do not
///     move and sit at road height;
///   * the townsfolk exist, are civilian-dressed (no helmeted walkers) and none stands within the
///     ride corridor of ANY road (distance to the route graph).
/// Frames: reference/good_graphics/sakura_npc/*.png - torii wide + people, roadside group, scale
/// beside Kuro with a stopped rider, a tree photographer, the lake village.
/// </summary>
public sealed class SakuraNpcPlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public string[] riderNames = new string[0];
    public string[] stoppedNames = new string[0];
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
        Debug.Log((ok ? "[sakura-npc] PASS " : "[sakura-npc] FAIL ") + what);
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
        SakuraRiderLod.ViewCamera = _cam;
        yield return null;

        if (_boot.regions != null) _boot.regions.FastTravel(RegionCatalog.SakuraPass);
        var reg = RegionCatalog.Find(RegionCatalog.SakuraPass);
        if (reg != null && _boot.session.courseId != reg.BuiltCourseId) _boot.session.SelectCourse(reg.BuiltCourseId);
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.HoldZeroPower = true;
        for (int i = 0; i < 10; i++) yield return null;
        _course = _boot.session.Course;
        Check(_course != null && _course.Length > 1300f, $"Sakura course active ({_boot.session.courseId}, {(_course != null ? _course.Length : 0):0} m)");
        if (_course == null) { Finish(); yield break; }
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;
        SeekPlayer(20f);

        // --- riders present
        foreach (var c in FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var n in riderNames)
                if (c.gameObject.name == "Sakura NPC " + n) _riders[n] = c;
        Check(_riders.Count == riderNames.Length, $"all {riderNames.Length} N1 cyclists present (found {_riders.Count})");
        foreach (var n in riderNames)
        {
            if (!_riders.TryGetValue(n, out var c)) { Check(false, $"{n} missing"); continue; }
            var bike = FindDeep(c.transform, "Bike");
            int bikeMeshes = bike == null ? 0 : bike.GetComponentsInChildren<MeshRenderer>(true)
                .Count(r => r.enabled && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null);
            int body = c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(s => s.sharedMesh != null && s.enabled);
            int nullMats = c.GetComponentsInChildren<Renderer>(true).Sum(r => r.sharedMaterials.Count(m => m == null));
            Check(c.isActiveAndEnabled && bikeMeshes > 10 && body > 0 && nullMats == 0,
                  $"{n}: active {c.isActiveAndEnabled}, bike parts {bikeMeshes}, body meshes {body}, null materials {nullMats}, " +
                  $"seg {c.segmentId}, reverse {c.reverse}, speed {c.speed:0.00} m/s{(stoppedNames.Contains(n) ? " (STOPPED)" : "")}");
        }

        // --- everyone moves the right way and pedals (5 s of play); stopped riders stay put
        var p0 = new Dictionary<string, Vector3>();
        var k0 = new Dictionary<string, float>();
        var kSum = new Dictionary<string, float>();
        foreach (var kv in _riders) { p0[kv.Key] = kv.Value.transform.position; k0[kv.Key] = Crank(kv.Value); kSum[kv.Key] = 0f; }
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
            float moved = Vector3.Distance(p0[kv.Key], c.transform.position);
            if (stoppedNames.Contains(kv.Key))
            {
                Check(moved < 0.3f, $"{kv.Key} is STOPPED: moved {moved:0.00} m in {t:0.0} s");
                continue;
            }
            Check(moved > 0.6f * c.speed * t, $"{kv.Key} rides {(c.reverse ? "oncoming" : "with you")}: moved {moved:0.0} m in {t:0.0} s (expected ~{c.speed * t:0.0})");
            Check(kSum[kv.Key] > 90f, $"{kv.Key} pedals: crank turned {kSum[kv.Key]:0} deg in {t:0.0} s");
        }

        // --- townsfolk audit
        AuditTownsfolk();

        // --- frames
        var toriiGo = GameObject.Find("Torii Gate");
        Vector3 torii = toriiGo != null ? toriiGo.transform.position : _course.PositionAt(88f);
        float tm = CourseM(torii);
        yield return ViewFrom("sakura_npc_torii_wide", tm - 28f, torii, 7f, 55f, 12f);
        yield return ViewFrom("sakura_npc_torii_people", tm + 6f, torii, 3.2f, 48f, 9f);
        if (_riders.TryGetValue("Haruto", out var hc)) yield return Roadside(hc, "sakura_npc_group_Haruto");
        if (_riders.TryGetValue("Yuzu", out var yz)) yield return BesideKuro(yz, "sakura_npc_scale_stopped_Yuzu");
        var photog = GameObject.Find("Sakura Townsfolk") != null
            ? GameObject.Find("Sakura Townsfolk").GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.StartsWith("Sakura Photographer"))
            : null;
        if (photog != null)
        {
            float pm = CourseM(photog.position);
            Vector3 fwd = photog.forward;
            SeekPlayer(Mathf.Max(0f, pm - 20f));
            _cam.transform.position = photog.position + fwd * 3.6f + photog.right * 0.9f + Vector3.up * 1.4f;
            _cam.transform.LookAt(photog.position + Vector3.up * 0.9f);
            _cam.fieldOfView = 42f;
            yield return Settle(4);
            yield return Capture("sakura_npc_photographer");
        }
        var vgo = GameObject.Find("Lake Village");
        if (vgo != null)
        {
            var rs = vgo.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            yield return ViewFrom("sakura_npc_village", CourseM(b.center) - 20f, b.center, 4f, 58f, 24f);
        }
        Finish();
    }

    // ---------------------------------------------------------------- audits
    private void AuditTownsfolk()
    {
        var rootGo = GameObject.Find("Sakura Townsfolk");
        Check(rootGo != null, "Sakura Townsfolk root exists");
        if (rootGo == null) return;
        var actors = rootGo.GetComponentsInChildren<MinatoCrowdActor>(true);
        Check(actors.Length >= 60, $"townsfolk count {actors.Length} (>= 60)");
        int civilian = 0, helm = 0;
        foreach (var a in actors)
        {
            var look = a.GetComponent<MapleCityLook>();
            if (look != null && look.civilianAtlas) civilian++;
            var high = a.transform.Find("LOD0 High Skinned");
            if (high != null && high.GetComponentsInChildren<Transform>(true).Any(x => x.name.ToLowerInvariant().Contains("helmet"))) helm++;
        }
        Check(civilian >= actors.Length * 0.95f, $"{civilian}/{actors.Length} townsfolk wear a Sakura civilian atlas");
        Check(helm == 0, $"helmet-named objects on people: {helm}");
        var graph = RouteGraph.Load();
        float worst = float.MaxValue; string worstName = "";
        foreach (var a in actors)
        {
            var q = a.transform.position;
            foreach (var s in graph.segments)
                for (int i = 0; i < s.position.Length; i++)
                {
                    float dx = s.position[i].x - q.x, dz = s.position[i].z - q.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < worst) { worst = d; worstName = a.name; }
                }
        }
        Check(worst > 6.0f, $"nearest townsperson to any road sample: {worst:0.0} m ({worstName}); corridor 6 m");
    }

    // ---------------------------------------------------------------- shots
    private IEnumerator ViewFrom(string shot, float seekM, Vector3 look, float camHeight, float fov, float back)
    {
        SeekPlayer(Mathf.Clamp(seekM, 0f, _course.Length));
        _cam.transform.position = _course.PositionAt(Mathf.Max(0f, CourseM(look) - back)) + Vector3.up * camHeight;
        _cam.transform.LookAt(look + Vector3.up * 1.2f);
        _cam.fieldOfView = fov;
        yield return Settle(8);
        yield return Capture(shot);
        Debug.Log($"[sakura-npc] {shot}: camera {_cam.transform.position:F1} looking at {look:F1}");
    }

    private IEnumerator Roadside(NPCCyclist c, string shot)
    {
        float m = CourseM(c.transform.position);
        float ahead = c.reverse ? -10f : 10f;
        SeekPlayer(Mathf.Clamp(m - (c.reverse ? -60f : 60f), 0f, _course.Length));
        Vector3 road = _course.PositionAt(m + ahead);
        Vector3 side = _course.SideAt(m + ahead);
        float lateral = c.reverse ? 4.2f : -4.2f;
        _cam.transform.position = road + side * lateral + Vector3.up * 1.7f;
        _cam.fieldOfView = 42f;
        yield return Settle(2);
        for (int k = 0; k < 2; k++)
        {
            _cam.transform.LookAt(c.transform.position + Vector3.up * 0.75f);
            yield return Capture($"{shot}_{k + 1}");
            Debug.Log($"[sakura-npc] {shot}_{k + 1}: {c.name} at {c.transform.position:F2} course {CourseM(c.transform.position):0.0} m, crank {Crank(c):0} deg");
            yield return Wait(1.2f);
        }
    }

    private IEnumerator BesideKuro(NPCCyclist c, string shot)
    {
        float m = CourseM(c.transform.position);
        SeekPlayer(m);
        yield return Settle(2);
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
            Debug.Log($"[sakura-npc] {shot}: Kuro at {kuro.position:F2}, {c.name} at {c.transform.position:F2} (gap {Vector3.Distance(kuro.position, c.transform.position):0.00} m)");
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
        Debug.LogError("[sakura-npc] " + why);
        _fail++;
        Finish();
    }

    private void Finish()
    {
        Failed = _fail > 0;
        SakuraRiderLod.ViewCamera = null;
        Debug.Log($"[sakura-npc] RESULT {(Failed ? "FAIL" : "PASS")} {_pass}/{_pass + _fail}");
        Finished = true;
    }
}

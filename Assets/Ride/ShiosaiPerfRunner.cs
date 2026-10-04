using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// MEASURES Shiosai Coast frame cost in REAL PLAY MODE, from the REAL ride camera.
///
/// Why this exists: "should be faster" is not a performance claim. Every optimisation on this
/// zone has to be defended with a before/after number taken the same way, on the same stretch
/// of road, with the same traffic density. So this harness:
///
///   * fast travels to the coast and selects "Shiosai Breeze",
///   * rides a fixed, busy window of the route under a fixed scripted effort,
///   * times BOTH the whole player-loop frame (Time.unscaledDeltaTime) AND an explicit
///     full-resolution Camera.Render() (which in the editor includes culling, the shadow pass,
///     every draw call and SakuraPostFX's OnRenderImage chain),
///   * and reads Unity's own draw-call / triangle / SetPass counters through UnityStats.
///
/// The render timing is the honest one to compare: in batchmode the editor does not necessarily
/// present a game view every frame, so the loop delta alone can flatter a change that only moved
/// GPU work around. Both are reported.
///
/// Stage label comes from MR_PERF_STAGE so a before/after pair lands side by side.
/// </summary>
public class ShiosaiPerfRunner : MonoBehaviour
{
    public string outDir;
    public string stage = "stage";

    /// <summary>PROVISIONAL: the busy window this benchmark rides, in course metres.</summary>
    public float startM = 1300f;
    /// <summary>Frames measured per sample station, after a warm-up.</summary>
    public int framesPerStation = 180;
    public int warmupFrames = 60;
    /// <summary>PROVISIONAL scripted effort, so the rider is actually riding.</summary>
    [Range(0f, 1f)] public float playerEffort = 0.72f;

    /// <summary>Stations sampled, chosen to cover both directions of the out-and-back.</summary>
    public float[] stations = { 1300f, 2600f, 4250f };

    /// <summary>When true, runs an A/B isolation sweep that attributes the frame cost.</summary>
    public bool sweep;

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private Camera _cam;

    // UnityStats, reached by reflection so this can live in the runtime assembly.
    private static PropertyInfo _pDraw, _pTri, _pBatches, _pSetPass;

    private static void ResolveStats()
    {
        if (_pDraw != null) return;
        var t = System.Type.GetType("UnityEditor.UnityStats, UnityEditor");
        if (t == null) return;
        _pDraw = t.GetProperty("drawCalls", BindingFlags.Public | BindingFlags.Static);
        _pTri = t.GetProperty("triangles", BindingFlags.Public | BindingFlags.Static);
        _pBatches = t.GetProperty("batches", BindingFlags.Public | BindingFlags.Static);
        _pSetPass = t.GetProperty("setPassCalls", BindingFlags.Public | BindingFlags.Static);
    }

    private static int Stat(PropertyInfo p) => p == null ? -1 : (int)p.GetValue(null);

    private IEnumerator Start()
    {
        Finished = false; Failed = false;
        ResolveStats();

        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }

        yield return null;
        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.ShiosaiCoast))
            Debug.LogWarning("[shiosai-perf] could not fast travel to the coast.");

        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("shiosai_breeze");
        _boot.devices.acceptKeyboardEffort = false;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        yield return null;

        var traffic = FindFirstObjectByType<ShiosaiTrafficDirector>();

        double allLoop = 0, allRender = 0; int allFrames = 0;
        float worstRender = 0f;

        foreach (float s in stations)
        {
            _boot.session.SeekTo(s);
            if (_boot.follower != null) _boot.follower.Apply();
            for (int f = 0; f < warmupFrames; f++)
            {
                _boot.devices.EffortInput = playerEffort;
                yield return null;
            }

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            double loopMs = 0, renderMs = 0;
            float stationWorst = 0f;
            int drawCalls = 0, tris = 0, batches = 0, setPass = 0, samples = 0;
            int activeRiders = 0;

            for (int f = 0; f < framesPerStation; f++)
            {
                _boot.devices.EffortInput = playerEffort;
                yield return null;                      // one real player-loop frame

                loopMs += Time.unscaledDeltaTime * 1000.0;

                // Explicit full-res render of the real gameplay camera: culling + shadows +
                // every draw call + SakuraPostFX. This is the number optimisations must move.
                var prev = _cam.targetTexture;
                float t0 = Time.realtimeSinceStartup;
                _cam.targetTexture = rt;
                _cam.Render();
                _cam.targetTexture = prev;
                float ms = (Time.realtimeSinceStartup - t0) * 1000f;
                renderMs += ms;
                if (ms > stationWorst) stationWorst = ms;

                int dc = Stat(_pDraw);
                if (dc >= 0)
                {
                    drawCalls += dc; tris += Stat(_pTri);
                    batches += Stat(_pBatches); setPass += Stat(_pSetPass);
                    samples++;
                }
                if (traffic != null) activeRiders += traffic.activeRiders;
            }

            rt.Release(); Destroy(rt);

            float n = framesPerStation;
            allLoop += loopMs; allRender += renderMs; allFrames += framesPerStation;
            if (stationWorst > worstRender) worstRender = stationWorst;

            Debug.Log($"[shiosai-perf] {stage} @ {s:0000} m: " +
                      $"loop {loopMs / n:0.00} ms ({1000f / (loopMs / n):0.0} fps)  " +
                      $"render {renderMs / n:0.00} ms (worst {stationWorst:0.00})  " +
                      $"draws {(samples > 0 ? drawCalls / samples : -1)}  " +
                      $"batches {(samples > 0 ? batches / samples : -1)}  " +
                      $"setPass {(samples > 0 ? setPass / samples : -1)}  " +
                      $"tris {(samples > 0 ? tris / samples : -1)}  " +
                      $"riders {activeRiders / framesPerStation}");
        }

        if (sweep) yield return IsolationSweep();

        string summary = $"[shiosai-perf] SUMMARY {stage}: loop {allLoop / allFrames:0.00} ms " +
                         $"({1000.0 / (allLoop / allFrames):0.0} fps), render {allRender / allFrames:0.00} ms, " +
                         $"worst render {worstRender:0.00} ms, over {allFrames} frames.";
        Debug.Log(summary);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
            File.AppendAllText(Path.Combine(outDir, "shiosai_perf.txt"),
                               System.DateTime.Now.ToString("s") + "  " + summary + "\n");
        }
        Finished = true;
    }

    private void Fail(string why)
    {
        Debug.LogError("[shiosai-perf] " + why);
        Failed = true; Finished = true;
    }

    // ===================================================================== isolation sweep

    /// <summary>
    /// Attributes the frame cost by switching one suspect off at a time and re-measuring the
    /// SAME window. Guessing which of thirty riders, a post chain and a hillside of props costs
    /// the most has been wrong on this project before; this measures it.
    /// </summary>
    private IEnumerator IsolationSweep()
    {
        _boot.session.SeekTo(startM);
        if (_boot.follower != null) _boot.follower.Apply();

        var traffic = FindFirstObjectByType<ShiosaiTrafficDirector>();
        var trafficRoot = traffic != null ? traffic.gameObject : null;
        var fx = _cam.GetComponent<SakuraPostFX>();
        var dressing = FindChild("Shiosai Coast Environment", "Coast Dressing");
        var distance = FindChild("Shiosai Coast Environment", "Coast Distance");
        var landform = FindChild("Shiosai Coast Environment", "Coast Landform");

        // Inventory first: how many skinned bodies, how many triangles, how many still
        // defeating frustum culling with updateWhenOffscreen.
        if (trafficRoot != null)
        {
            int smrs = 0, offscreen = 0; long tris = 0;
            foreach (var smr in trafficRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smrs++;
                if (smr.updateWhenOffscreen) offscreen++;
                if (smr.sharedMesh != null) tris += smr.sharedMesh.triangles.Length / 3;
            }
            int mrs = trafficRoot.GetComponentsInChildren<MeshRenderer>(true).Length;
            Debug.Log($"[shiosai-perf] traffic inventory: {smrs} skinned renderers " +
                      $"({offscreen} with updateWhenOffscreen ON), {tris} skinned tris total, " +
                      $"{mrs} mesh renderers (bikes).");
        }

        yield return Measure("A full");

        if (trafficRoot != null) { trafficRoot.SetActive(false); yield return Measure("B no traffic"); trafficRoot.SetActive(true); }

        if (trafficRoot != null)
        {
            var rigs = trafficRoot.GetComponentsInChildren<KuroBikeRig>(true);
            foreach (var r in rigs) r.enabled = false;
            yield return Measure("C traffic, rigs off");
            foreach (var r in rigs) r.enabled = true;
        }

        if (trafficRoot != null)
        {
            var rends = trafficRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) r.enabled = false;
            yield return Measure("D traffic, renderers off");
            foreach (var r in rends) r.enabled = true;
        }

        if (fx != null) { fx.enabled = false; yield return Measure("E no postFX"); fx.enabled = true; }

        float keepShadow = QualitySettings.shadowDistance;
        QualitySettings.shadowDistance = 0.01f;
        yield return Measure("F no shadows");
        QualitySettings.shadowDistance = keepShadow;

        if (dressing != null) { dressing.SetActive(false); yield return Measure("G no dressing"); dressing.SetActive(true); }
        if (distance != null) { distance.SetActive(false); yield return Measure("H no distant depth"); distance.SetActive(true); }
        if (landform != null) { landform.SetActive(false); yield return Measure("I no landform"); landform.SetActive(true); }
    }

    private static GameObject FindChild(string root, string child)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == child && t.parent != null && t.parent.name == root) return t.gameObject;
        return null;
    }

    private IEnumerator Measure(string label)
    {
        const int Warm = 25, N = 90;
        for (int f = 0; f < Warm; f++) { _boot.devices.EffortInput = playerEffort; yield return null; }
        double loop = 0, render = 0;
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        int draws = 0, samples = 0;
        for (int f = 0; f < N; f++)
        {
            _boot.devices.EffortInput = playerEffort;
            yield return null;
            loop += Time.unscaledDeltaTime * 1000.0;
            var prev = _cam.targetTexture;
            float t0 = Time.realtimeSinceStartup;
            _cam.targetTexture = rt; _cam.Render(); _cam.targetTexture = prev;
            render += (Time.realtimeSinceStartup - t0) * 1000f;
            int dc = Stat(_pDraw);
            if (dc >= 0) { draws += dc; samples++; }
        }
        rt.Release(); Destroy(rt);
        Debug.Log($"[shiosai-perf] SWEEP {stage} | {label,-26} loop {loop / N:0.00} ms  " +
                  $"render {render / N:0.00} ms  draws {(samples > 0 ? draws / samples : -1)}");
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Photographs MINATO COAST from the REAL gameplay camera, in REAL play mode, specifically to
/// prove the ambient cyclist traffic staged by <see cref="MinatoNpcTraffic"/> is actually ON the
/// boulevard while the player rides.
///
/// Why a play-mode runner rather than an editor diagnostic camera: the traffic pool is PARKED
/// (inactive) in the saved scene and only spawned by <see cref="ShiosaiTrafficDirector"/> at
/// runtime, so an editor diagnostic render is structurally incapable of showing a single rider.
/// Trimmed copy of <see cref="ShiosaiPlaymodeRunner"/> - chase frames plus HUD-free drone frames
/// looking straight down the carriageway, which is the view that makes density, lane discipline
/// and wheel contact judgeable. The coast's draft/overtake proof sequence is deliberately NOT
/// duplicated here: that mechanic is already verified, and this milestone is about presence.
///
/// Driven by MinatoPlaymodeCapture (editor side), which enters play mode and spawns this.
/// </summary>
public class MinatoPlaymodeRunner : MonoBehaviour
{
    /// <summary>Where the PNGs land (absolute; set by the editor side).</summary>
    public string outDir;

    /// <summary>
    /// PROVISIONAL: distances along the 19 km Minato crossing to photograph, in metres.
    /// Spread across the port departure, the bridge approach, the open-ocean deck and the
    /// far-shore landfall, so traffic is verified on every kind of carriageway the route has.
    /// </summary>
    public float[] stations = { 400f, 900f, 2150f, 4200f, 6400f, 9000f, 10600f, 13500f, 15500f, 17500f, 18800f };

    /// <summary>PROVISIONAL scripted effort so the rider actually rolls while framing.</summary>
    [Range(0f, 1f)] public float playerEffort = 0.7f;

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private Camera _cam;
    private Camera _uiCam;
    private bool _hideHud;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        _boot.Resolve();

        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }

        yield return null;

        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.MinatoCoast))
            Debug.LogWarning("[minato-play] could not fast travel to the Minato region.");

        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("minato_crossing");
        _boot.devices.acceptKeyboardEffort = false;

        RouteCanvasesToCamera();

        yield return null;
        Debug.Log($"[minato-play] course '{_boot.session.courseId}', " +
                  $"{(_boot.session.Course != null ? _boot.session.Course.Length : 0f):0} m.");

        var traffic = FindTraffic();
        Debug.Log(traffic == null
            ? "[minato-play] WARNING: no traffic director found for the Minato region."
            : $"[minato-play] traffic director '{traffic.name}' segment '{traffic.segmentId}', " +
              $"pool {(traffic.pool != null ? traffic.pool.Length : 0)}.");

        for (int i = 0; i < stations.Length; i++)
        {
            _boot.session.SeekTo(stations[i]);
            if (_boot.follower != null) _boot.follower.Apply();
            // The chase camera LERPS, so after a teleport it spends about a second out over the
            // bay looking back. Snap it onto its own desired pose first.
            SnapCamera();
            for (int f = 0; f < 24; f++)
            {
                _boot.devices.EffortInput = playerEffort;
                yield return null;
            }
            SnapCamera();
            yield return Capture($"play_minato_{i + 1}_{stations[i]:00000}m");
        }

        yield return TrafficBurst();
        yield return TrafficAudit();
        yield return CrowdGaitAndPerformanceValidation();

        Debug.Log($"[minato-play] done, frames written to {outDir}");
        Finished = true;
    }

    private ShiosaiTrafficDirector FindTraffic()
    {
        foreach (var d in FindObjectsByType<ShiosaiTrafficDirector>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.segmentId == "minato") return d;
        return null;
    }

    private void Fail(string why)
    {
        Debug.LogError("[minato-play] " + why);
        Failed = true;
        Finished = true;
    }

    /// <summary>PROVISIONAL: where the pace burst is shot from, and how it is spaced.</summary>
    public float burstStationM = 2150f;
    public int burstFrames = 5;
    public float burstIntervalS = 1.2f;

    /// <summary>
    /// Photographs one fixed viewpoint several times with a known interval, so ambient rider
    /// speed can be READ OFF the images instead of taken on trust from a log line. The player is
    /// held stationary (zero effort) so everything that moves between frames is traffic.
    ///
    /// QA fix: "zero effort" here used to still free-run the simulator's autonomous endurance
    /// band (~150-165 W) - this is now a real hold via DeviceManager.HoldZeroPower.
    /// </summary>
    private IEnumerator TrafficBurst()
    {
        _boot.session.SeekTo(burstStationM);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        _boot.devices.HoldZeroPower = true;
        for (int f = 0; f < 24; f++) { _boot.devices.EffortInput = 0f; yield return null; }
        SnapCamera();

        var traffic = FindTraffic();
        for (int i = 0; i < burstFrames; i++)
        {
            if (i > 0)
            {
                float until = Time.time + burstIntervalS;
                while (Time.time < until) { _boot.devices.EffortInput = 0f; yield return null; }
                SnapCamera();
            }
            if (traffic != null)
                Debug.Log($"[minato-play] burst {i}: t={Time.time:0.00}s " +
                          $"traffic active={traffic.activeRiders} " +
                          $"sameWay={traffic.sameWayRiders} oncoming={traffic.oncomingRiders} " +
                          $"playerRoadD={traffic.playerRoadDistanceM:0} m " +
                          $"dir={traffic.playerRoadDirection}");
            yield return Capture($"play_minato_burst_{i}_{burstIntervalS * i:0.0}s");
        }
        _boot.devices.HoldZeroPower = false;
    }

    /// <summary>PROVISIONAL: elevated, HUD-free audit viewpoints used to READ traffic density.</summary>
    public float[] auditStations = { 900f, 2150f, 6400f, 10600f };
    public float auditHeightM = 26f;
    public float auditBackM = 36f;
    public float auditAheadM = 170f;

    /// <summary>
    /// The chase camera frames the rider and the minimap covers the quadrant the road recedes
    /// into - between them a busy road can photograph empty. This drone pass lifts the camera,
    /// drops the HUD and looks straight down the carriageway so density, lane discipline and
    /// wheel-on-road contact can be judged honestly.
    /// </summary>
    private IEnumerator TrafficAudit()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        _hideHud = true;
        _boot.devices.HoldZeroPower = true;
        foreach (float s in auditStations)
        {
            _boot.session.SeekTo(s);
            if (_boot.follower != null) _boot.follower.Apply();
            if (follow != null) follow.enabled = true;
            for (int f = 0; f < 26; f++) { _boot.devices.EffortInput = 0f; yield return null; }

            var t = follow != null ? follow.target : null;
            if (t == null) continue;
            if (follow != null) follow.enabled = false;
            _cam.transform.position = t.position - t.forward * auditBackM + Vector3.up * auditHeightM;
            _cam.transform.LookAt(t.position + t.forward * auditAheadM);
            yield return Capture($"play_minato_audit_{s:00000}m");

            // Second, LOW frame from just off the rider's shoulder: an overhead drone shot can
            // hide a floating rider, a low one cannot - wheels and attached shadows are legible.
            _cam.transform.position = t.position + t.right * 4.2f - t.forward * 7.0f + Vector3.up * 2.0f;
            _cam.transform.LookAt(t.position + t.forward * 26f + Vector3.up * 0.8f);
            yield return Capture($"play_minato_contact_{s:00000}m");
        }
        if (follow != null) follow.enabled = true;
        _hideHud = false;
    }

    /// <summary>
    /// Genuine runtime proof for the environment crowd: census the complete staged population,
    /// then hold one fixed low three-quarter camera on a real moving walker for more than one
    /// gait period. Nothing calls SampleForDiagnostics here; every pose and root step comes from
    /// MinatoCrowdActor.Update in play mode.
    /// </summary>
    private IEnumerator CrowdGaitAndPerformanceValidation()
    {
        // HoldZeroPower stays on from TrafficAudit - still just "player stationary" intent.
        _boot.session.SeekTo(900f);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int i = 0; i < 20; i++) { _boot.devices.EffortInput = 0f; yield return null; }

        var actors = FindObjectsByType<MinatoCrowdActor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var walkers = actors.Where(a => a.motion == MinatoCrowdActor.MotionKind.Walk).ToArray();
        var skins = actors.SelectMany(a => a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            .Distinct().ToArray();
        var renderers = actors.SelectMany(a => a.GetComponentsInChildren<Renderer>(true))
            .Distinct().ToArray();
        int rootCount = FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Count(t => t.parent == null && t.name == "Minato Coast Environment");

        string[] canonicalContainers =
        {
            "Boulevard Cyclists", "Waterfront Pedestrians", "City Beach", "Market Shoppers"
        };
        var allTransforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        string containers = string.Join(", ", canonicalContainers.Select(
            n => $"{n}={allTransforms.Count(t => t.name == n)}"));
        int duplicateSignatures = actors.GroupBy(ActorSignature).Sum(g => Mathf.Max(0, g.Count() - 1));
        string groups = string.Join(", ", actors.GroupBy(PlacementGroup)
            .OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
        Debug.Log($"[minato-play-crowd] roots={rootCount}, actors={actors.Length}, " +
                  $"walkers={walkers.Length}, SMRs={skins.Length}, renderers={renderers.Length}, " +
                  $"duplicateSignatures={duplicateSignatures}; containers: {containers}; " +
                  $"placement groups: {groups}");

        if (rootCount != 1 || duplicateSignatures != 0 ||
            canonicalContainers.Any(n => allTransforms.Count(t => t.name == n) != 1))
        {
            Fail("crowd hierarchy is not canonical/idempotent");
            yield break;
        }

        var sceneRenderers = FindObjectsByType<Renderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        var rideCamera = _cam;
        var follow = rideCamera.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;
        _hideHud = true;

        string rideTag = rideCamera.tag;
        bool rideEnabled = rideCamera.enabled;
        rideCamera.tag = "Untagged";
        rideCamera.enabled = false;
        var gaitGo = new GameObject("~MinatoRuntimeGaitCamera", typeof(Camera));
        var gaitCamera = gaitGo.GetComponent<Camera>();
        gaitCamera.tag = "MainCamera";
        gaitCamera.cullingMask = rideCamera.cullingMask;
        gaitCamera.clearFlags = rideCamera.clearFlags;
        gaitCamera.backgroundColor = rideCamera.backgroundColor;
        gaitCamera.allowHDR = true;
        gaitGo.AddComponent<SakuraPostFX>();
        _cam = gaitCamera;
        _cam.fieldOfView = 28f;
        _cam.nearClipPlane = 0.05f;
        _cam.farClipPlane = 500f;
        _cam.allowHDR = true;

        Directory.CreateDirectory(outDir);
        foreach (string old in Directory.GetFiles(outDir, "play_minato_walker_*gait_*.png"))
            File.Delete(old);

        int previousCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = 60;
        var used = new HashSet<MinatoCrowdActor>();
        string[] labels = { "market_pavement_a", "market_pavement_b", "beach_sand" };
        for (int sequence = 0; sequence < labels.Length; sequence++)
        {
            string label = labels[sequence];
            IEnumerable<MinatoCrowdActor> pool = walkers
                .Where(a => !used.Contains(a))
                .Where(a => used.All(previous =>
                    Vector3.Distance(previous.transform.position, a.transform.position) > 25f))
                .Where(a => Vector3.Distance(a.pathStart, a.pathEnd) >= 5f);
            if (label.StartsWith("market_pavement", StringComparison.Ordinal))
                pool = pool.Where(a => HasAncestor(a.transform, "Market Shoppers") &&
                                       IsRenderedPavement(a.transform.position));
            else
                pool = pool.Where(a => HasAncestor(a.transform, "City Beach") &&
                                       IsRenderedBeach(a.transform.position));

            MinatoCrowdActor walker = SelectValidationWalker(
                pool, actors, sceneRenderers, out float selectedSideSign);
            if (walker == null)
            {
                Time.captureFramerate = previousCaptureFramerate;
                Fail($"no unobstructed {label} walker available for gait validation");
                yield break;
            }
            used.Add(walker);
            float savedAnimationDistance = walker.animationDistance;
            // The dedicated camera may still be parked at the previous validation site while
            // this subject settles. Force only this selected actor to update before deriving
            // its baked visual centre; otherwise the first/far subject can be framed from stale
            // skinned geometry even though the later contact numbers are correct.
            walker.animationDistance = float.MaxValue;

            for (int i = 0; i < 12; i++) yield return null;

            Vector3 livePath = Vector3.ProjectOnPlane(
                walker.pathEnd - walker.pathStart, Vector3.up);
            Vector3 liveForward = livePath.sqrMagnitude > 0.1f
                ? livePath.normalized
                : walker.transform.forward;
            Vector3 liveSide = Vector3.Cross(Vector3.up, liveForward).normalized;
            Vector3 centre = BakedVisualCentre(walker.transform);
            _cam.transform.position = centre +
                                      liveSide * (3.25f * selectedSideSign) -
                                      liveForward * 1.65f + Vector3.up * 0.42f;
            _cam.transform.rotation = Quaternion.LookRotation(
                centre - _cam.transform.position, Vector3.up);
            yield return null;

            float startTime = Time.time;
            float interval = walker.GaitPeriodSeconds / 12f;
            Vector3 startPosition = walker.transform.position;
            for (int n = 0; n <= 12; n++)
            {
                float due = startTime + n * interval;
                while (Time.time < due) yield return null;
                float elapsed = Time.time - startTime;
                string frameName = $"play_minato_walker_{sequence + 1:00}_{label}_" +
                                   $"gait_{n:00}_t{elapsed:0.000}s";
                yield return Capture(frameName);
                bool contactPass = walker.RuntimeRenderedContactPass(out string contactFailure);
                Debug.Log($"[minato-play-gait] subject={sequence + 1:00}/{label} " +
                          $"frame={n:00} t={elapsed:0.000}s root={walker.transform.position} " +
                          $"moved={Vector3.Distance(startPosition, walker.transform.position):0.000}m " +
                          $"{walker.RuntimeContactSummary()} gate={(contactPass ? "PASS" : "FAIL")}" +
                          (contactPass ? "" : $" reason={contactFailure}"));
                if (!contactPass)
                {
                    Time.captureFramerate = previousCaptureFramerate;
                    Fail($"{label} rendered-sole gate rejected {frameName}: {contactFailure}");
                    yield break;
                }
            }
            walker.animationDistance = savedAnimationDistance;
        }
        Time.captureFramerate = previousCaptureFramerate;

        _cam = rideCamera;
        UnityEngine.Object.Destroy(gaitGo);
        rideCamera.tag = rideTag;
        rideCamera.enabled = rideEnabled;
        if (follow != null) follow.enabled = true;
        _hideHud = false;

        _boot.session.SeekTo(900f);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int i = 0; i < 20; i++) yield return null;
        yield return MeasureCrowdFrameTiming(actors, skins);
    }

    private static MinatoCrowdActor SelectValidationWalker(
        IEnumerable<MinatoCrowdActor> pool, MinatoCrowdActor[] actors,
        Renderer[] sceneRenderers, out float selectedSideSign)
    {
        var candidates = pool.Select(a => new
            {
                Actor = a,
                Clearance = actors.Where(other => other != a)
                    .Min(other => Vector3.Distance(a.transform.position,
                                                  other.transform.position))
            })
            .OrderByDescending(x => x.Clearance)
            .Take(30).ToArray();

        MinatoCrowdActor selected = null;
        selectedSideSign = 1f;
        int bestObstructions = int.MaxValue;
        float bestClearance = float.NegativeInfinity;
        foreach (var option in candidates)
        {
            Vector3 path = Vector3.ProjectOnPlane(
                option.Actor.pathEnd - option.Actor.pathStart, Vector3.up);
            Vector3 forward = path.sqrMagnitude > 0.1f
                ? path.normalized
                : option.Actor.transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 target = option.Actor.transform.position + Vector3.up * 0.63f;
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 eye = target + side * (3.25f * sign) -
                              forward * 1.65f + Vector3.up * 0.42f;
                bool eyeBlocked = Physics.OverlapSphere(
                        eye, 0.22f, ~0, QueryTriggerInteraction.Ignore)
                    .Any(c => c != null && !c.transform.IsChildOf(option.Actor.transform) &&
                              !IsWalkableSurfaceName(c.name));
                int obstructions = ViewObstructionCount(
                    eye, target, option.Actor.transform, sceneRenderers);
                if (eyeBlocked) obstructions += 1000;
                if (obstructions > bestObstructions ||
                    obstructions == bestObstructions && option.Clearance <= bestClearance)
                    continue;
                selected = option.Actor;
                selectedSideSign = sign;
                bestObstructions = obstructions;
                bestClearance = option.Clearance;
            }
        }
        return selected;
    }

    private static Vector3 BakedVisualCentre(Transform subject)
    {
        bool found = false;
        Bounds bounds = default;
        foreach (var renderer in subject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || !HasAncestor(renderer.transform, "LOD0 High Skinned"))
                continue;
            var baked = new Mesh();
            renderer.BakeMesh(baked, true);
            foreach (Vector3 vertex in baked.vertices)
            {
                Vector3 world = renderer.transform.TransformPoint(vertex);
                if (!found)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    found = true;
                }
                else bounds.Encapsulate(world);
            }
            UnityEngine.Object.Destroy(baked);
        }
        return found ? bounds.center : subject.position + Vector3.up * 0.63f;
    }

    private static bool IsWalkableSurfaceName(string name)
    {
        return name.StartsWith("Connected Port", StringComparison.Ordinal) ||
               name.StartsWith("Terrain_Chunk", StringComparison.Ordinal) ||
               name.IndexOf("Beach Sand", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IEnumerator MeasureCrowdFrameTiming(MinatoCrowdActor[] actors,
                                                SkinnedMeshRenderer[] skins)
    {
        var mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 256);
        var renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 256);
        var drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 256);
        var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 256);
        for (int i = 0; i < 30; i++) yield return null;

        const int frames = 180;
        var frameMs = new List<float>(frames);
        FrameTimingManager.CaptureFrameTimings();
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            frameMs.Add(Time.unscaledDeltaTime * 1000f);
            FrameTimingManager.CaptureFrameTimings();
        }
        frameMs.Sort();

        var timing = new FrameTiming[64];
        uint got = FrameTimingManager.GetLatestTimings((uint)timing.Length, timing);
        double cpu = 0d, gpu = 0d;
        for (int i = 0; i < got; i++)
        {
            cpu += timing[i].cpuFrameTime;
            gpu += timing[i].gpuFrameTime;
        }
        if (got > 0) { cpu /= got; gpu /= got; }

        int visibleHighSkins = skins.Count(s =>
            s.enabled && s.gameObject.activeInHierarchy && s.isVisible &&
            HasAncestor(s.transform, "LOD0 High Skinned"));
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_cam);
        int frustumHighSkins = skins.Count(s =>
        {
            if (!s.enabled || !s.gameObject.activeInHierarchy ||
                !HasAncestor(s.transform, "LOD0 High Skinned") ||
                !GeometryUtility.TestPlanesAABB(planes, s.bounds))
                return false;
            float distance = Vector3.Distance(_cam.transform.position, s.bounds.center);
            float screenHeight = s.bounds.size.y /
                Mathf.Max(0.01f, 2f * distance *
                    Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            return screenHeight >= 0.016f;
        });
        float avg = frameMs.Average();
        float p95 = frameMs[Mathf.Clamp(Mathf.CeilToInt(frames * 0.95f) - 1, 0, frames - 1)];
        Debug.Log($"[minato-play-perf] bridge crowd actors={actors.Length}, " +
                  $"visibleHighLODskins={visibleHighSkins}, frustumHighLODskins={frustumHighSkins}, " +
                  $"frameDelta avg={avg:0.00}ms " +
                  $"p95={p95:0.00}ms, FrameTiming samples={got} cpu={cpu:0.00}ms " +
                  $"gpu={gpu:0.00}ms, mainThread={RecorderMs(mainThread):0.00}ms, " +
                  $"renderThread={RecorderMs(renderThread):0.00}ms, " +
                  $"drawCalls={RecorderValue(drawCalls)}, triangles={RecorderValue(triangles)}");

        mainThread.Dispose();
        renderThread.Dispose();
        drawCalls.Dispose();
        triangles.Dispose();
    }

    private static string ActorSignature(MinatoCrowdActor actor)
    {
        Vector3 p = actor.transform.position;
        Vector3 a = actor.pathStart;
        Vector3 b = actor.pathEnd;
        return $"{actor.name}|{actor.motion}|" +
               $"{Mathf.RoundToInt(p.x * 100f)},{Mathf.RoundToInt(p.y * 100f)}," +
               $"{Mathf.RoundToInt(p.z * 100f)}|" +
               $"{Mathf.RoundToInt(a.x * 100f)},{Mathf.RoundToInt(a.z * 100f)}|" +
               $"{Mathf.RoundToInt(b.x * 100f)},{Mathf.RoundToInt(b.z * 100f)}";
    }

    private static string PlacementGroup(MinatoCrowdActor actor)
    {
        foreach (string name in new[]
                 { "Boulevard Cyclists", "Waterfront Pedestrians", "City Beach", "Market Shoppers" })
            if (HasAncestor(actor.transform, name)) return name;
        return "Other";
    }

    private static bool HasAncestor(Transform child, string name)
    {
        for (Transform t = child; t != null; t = t.parent)
            if (t.name == name) return true;
        return false;
    }

    private static int ViewObstructionCount(Vector3 eye, Vector3 target, Transform subject,
                                            Renderer[] renderers)
    {
        Vector3 delta = target - eye;
        float targetDistance = delta.magnitude;
        if (targetDistance < 0.1f) return int.MaxValue;
        var ray = new Ray(eye, delta / targetDistance);
        int count = 0;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled ||
                renderer.transform == subject || renderer.transform.IsChildOf(subject))
                continue;
            string n = renderer.name;
            if (n.StartsWith("Connected Port", StringComparison.Ordinal) ||
                n.StartsWith("Terrain_Chunk", StringComparison.Ordinal) ||
                n.IndexOf("Beach Sand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Road", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (renderer.bounds.Contains(eye))
            {
                count += 1000;
                continue;
            }
            if (renderer.bounds.IntersectRay(ray, out float distance) &&
                distance < targetDistance - 0.12f)
                count++;
        }
        return count;
    }

    private static bool IsRenderedPavement(Vector3 point)
    {
        return RenderedSurfaceName(point).StartsWith("Connected Port", StringComparison.Ordinal);
    }

    private static bool IsRenderedBeach(Vector3 point)
    {
        return RenderedSurfaceName(point).IndexOf(
            "Beach Sand", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string RenderedSurfaceName(Vector3 point)
    {
        var ray = new Ray(point + Vector3.up * 50f, Vector3.down);
        var hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
        float highest = float.NegativeInfinity;
        string surface = "";
        foreach (var hit in hits)
        {
            var renderer = hit.collider.GetComponent<MeshRenderer>();
            var filter = hit.collider.GetComponent<MeshFilter>();
            if (renderer == null || !renderer.enabled || filter == null ||
                filter.sharedMesh == null || hit.point.y <= highest)
                continue;
            highest = hit.point.y;
            surface = hit.collider.name;
        }
        return surface;
    }

    private static long RecorderValue(ProfilerRecorder recorder) =>
        recorder.Valid && recorder.Count > 0 ? recorder.LastValue : -1L;

    private static double RecorderMs(ProfilerRecorder recorder) =>
        recorder.Valid && recorder.Count > 0 ? recorder.LastValue * 1e-6 : -1d;

    /// <summary>Teleports the chase camera onto the pose it is smoothing toward.</summary>
    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    /// <summary>
    /// HUD canvases onto a dedicated depth-only UI camera sharing the ride camera's target
    /// texture - pointing them straight at a perspective ride camera skews and doubles them.
    /// </summary>
    private void RouteCanvasesToCamera()
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);

        var uiGo = new GameObject("~MinatoPlayHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.fieldOfView = _cam.fieldOfView;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;
        StripHdrpWorldPasses(_uiCam);

        var c = _boot.hud != null ? _boot.hud.Canvas : null;
        if (c != null)
        {
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = _uiCam;
            c.planeDistance = 1f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
    }

    /// <summary>
    /// An HDRP camera still evaluates the global Minato volume, so the depth-cleared HUD camera
    /// drew the golden-hour volumetric fog a second time over the whole finished frame (at far
    /// depth, since it sees no geometry): a blotchy brown veil across the bottom of every chase
    /// capture. The real game draws the HUD as a ScreenSpaceOverlay and never had it.
    /// </summary>
    private static void StripHdrpWorldPasses(Camera cam)
    {
        var hd = cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        if (hd == null) hd = cam.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.None;
        hd.volumeLayerMask = 0;
        hd.customRenderingSettings = true;
        var fields = new[]
        {
            UnityEngine.Rendering.HighDefinition.FrameSettingsField.AtmosphericScattering,
            UnityEngine.Rendering.HighDefinition.FrameSettingsField.Volumetrics,
            UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess,
        };
        foreach (var f in fields)
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, false);
        }
    }

    private IEnumerator Capture(string name)
    {
        yield return null;

        const int W = 1920, H = 1080;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        if (_uiCam != null && !_hideHud)
        {
            _uiCam.fieldOfView = _cam.fieldOfView;
            var prevUi = _uiCam.targetTexture;
            _uiCam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            _uiCam.Render();
            _uiCam.targetTexture = prevUi;
        }

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

        var traffic = FindTraffic();
        Debug.Log($"[minato-play] {name}: d {_boot.session.DistanceM:0} m, " +
                  $"cam {_cam.transform.position}" +
                  (traffic == null ? "" :
                   $", traffic active={traffic.activeRiders} sameWay={traffic.sameWayRiders} " +
                   $"oncoming={traffic.oncomingRiders} roadD={traffic.playerRoadDistanceM:0} m " +
                   $"dir={traffic.playerRoadDirection}"));
    }
}

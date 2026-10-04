using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// QA DIAGNOSTIC ONLY - not part of the shipping traffic pipeline.
///
/// Reproduces the user-reported "missing bike" condition as closely as an automated pass can:
/// the player is fast-travelled to the very start of Minato Coast and held at ZERO effort for
/// about 90 real seconds (matching the reported KM 0.01/19.02, ELAPSED 01:22, SPEED 0 km/h),
/// while ambient traffic keeps running. Existing capture harnesses (MinatoPlaymodeRunner) only
/// ever hold zero effort for ~1s mid-route during TrafficBurst, never for a long idle period at
/// the actual route start - this pass exists purely to close that gap and either confirm or
/// refute the "long idle at spawn" hypothesis by rendering, not by log line.
///
/// Every capture also dumps ShiosaiRiderLod's raw renderer-enabled state for the nearest riders,
/// so a render that LOOKS fine can still be cross-checked against the internal flags, and a
/// render that looks broken has a concrete internal signature attached to it.
///
/// Run headless WITHOUT -quit:
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -executeMethod MinatoIdleStartCapture.Run
/// </summary>
public class MinatoIdleStartRunner : MonoBehaviour
{
    public string outDir;
    public float idleSeconds = 90f;
    public float[] captureAtSeconds = { 0f, 5f, 10f, 15f, 20f, 30f, 45f, 60f, 75f, 90f };

    /// <summary>
    /// QA fix verification toggle. FALSE (default) matches this runner's original behaviour:
    /// the road position is pinned directly every frame, which is what let this diagnostic
    /// hold the rider still BEFORE DeviceManager.HoldZeroPower existed to do it properly. TRUE
    /// is the "free-roll" mode requested for verifying that fix: EffortInput is still driven to
    /// zero every frame, but nothing pins DistanceM, so an unbounded climb in speed/distance
    /// would be visible exactly as QA originally found it (power stayed ~153-165 W, ~578 m over
    /// 90 s) instead of being masked by the pin.
    /// </summary>
    public bool freeRoll = false;

    /// <summary>FI DIAGNOSTIC ONLY - see MinatoIdleStartCapture.RunHideSmile. When true, every
    /// "SmileDecal" GameObject spawned in the traffic pool is force-deactivated right before the
    /// first capture, to empirically bisect whether the "helmet tear" finding is the SmileDecal
    /// quad showing through the helmet rather than a mesh/texture defect on the helmet itself.</summary>
    public bool fiHideSmileDecals = false;

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private Camera _cam;
    private Camera _uiCam;

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
            Debug.LogWarning("[minato-idle] could not fast travel to the Minato region.");

        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("minato_crossing");
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.HoldZeroPower = freeRoll;

        RouteCanvasesToCamera();

        yield return null;

        // Match the report as closely as possible: a few metres in (KM 0.01 of 19.02), not
        // exactly the start banner, then hold dead stop.
        _boot.session.SeekTo(10f);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < 12; f++) { _boot.devices.EffortInput = 0f; yield return null; }
        SnapCamera();

        var traffic = FindTraffic();
        Debug.Log(traffic == null
            ? "[minato-idle] WARNING: no traffic director found for the Minato region."
            : $"[minato-idle] traffic director '{traffic.name}' segment '{traffic.segmentId}', " +
              $"pool {(traffic.pool != null ? traffic.pool.Length : 0)}.");

        Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath,
            "../reference/good_graphics/minato_play")));

        if (fiHideSmileDecals)
        {
            int hidden = 0;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name != "SmileDecal") continue;
                t.gameObject.SetActive(false);
                hidden++;
            }
            Debug.Log($"[minato-idle] FI DIAGNOSTIC: deactivated {hidden} SmileDecal object(s) " +
                      "before capture.");
        }

        // Zero EffortInput alone did NOT hold the player still (a background telemetry/trainer
        // simulator kept producing ~160W and the player kept accelerating up to 25 km/h within
        // 30s - itself logged as a separate finding, now fixed via DeviceManager.HoldZeroPower).
        // Default mode still pins the player's road position directly every frame, to faithfully
        // reproduce the reported "KM 0.01, ELAPSED 01:22, SPEED 0 km/h" state independently of
        // that fix. freeRoll=true instead trusts HoldZeroPower alone - no position pin - which is
        // exactly what proves the fix: an unfixed regression would show DistanceM/SpeedMps
        // climbing here exactly as originally measured.
        float pinnedDistanceM = _boot.session.DistanceM;
        float startTime = Time.time;
        int idx = 0;
        while (Time.time - startTime < idleSeconds + 0.5f)
        {
            _boot.devices.EffortInput = 0f;
            if (!freeRoll)
            {
                _boot.session.SeekTo(pinnedDistanceM);
                if (_boot.follower != null) _boot.follower.Apply();
            }
            float elapsed = Time.time - startTime;
            if (idx < captureAtSeconds.Length && elapsed >= captureAtSeconds[idx])
            {
                SnapCamera();
                yield return null;
                LogNearbyRiderLodState(elapsed);
                if (freeRoll)
                    Debug.Log($"[minato-idle] free-roll t={elapsed:0.0}s " +
                              $"d={_boot.session.DistanceM:0}m (started {pinnedDistanceM:0}m, " +
                              $"drift {_boot.session.DistanceM - pinnedDistanceM:0.0}m) " +
                              $"speed={_boot.session.SpeedMps * 3.6f:0.0}km/h " +
                              $"watts={_boot.devices.Telemetry.Watts:0}W");
                yield return Capture($"idle_minato_chase_{idx:00}_t{elapsed:000}s");
                yield return CaptureLowContact($"idle_minato_contact_{idx:00}_t{elapsed:000}s");
                idx++;
            }
            yield return null;
        }

        _boot.devices.HoldZeroPower = false;
        Debug.Log($"[minato-idle] done, frames written to {outDir}");
        Finished = true;
    }

    private ShiosaiTrafficDirector FindTraffic()
    {
        foreach (var d in FindObjectsByType<ShiosaiTrafficDirector>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.segmentId == "minato") return d;
        return null;
    }

    /// <summary>
    /// Dumps the renderer-enabled state of every rider within ~50m so a "the render looks
    /// broken/fine" claim always has the exact internal signature attached to it.
    /// </summary>
    private void LogNearbyRiderLodState(float elapsed)
    {
        var player = _boot.follower != null ? _boot.follower.rider : null;
        if (player == null) { Debug.LogWarning("[minato-idle] no player rider transform found."); return; }

        var lods = FindObjectsByType<ShiosaiRiderLod>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(l => l.gameObject.activeInHierarchy)
            .Select(l => (lod: l, dist: Vector3.Distance(l.transform.position, player.position)))
            .Where(x => x.dist < 50f)
            .OrderBy(x => x.dist)
            .ToArray();

        Debug.Log($"[minato-idle] t={elapsed:0.0}s d={_boot.session.DistanceM:0}m " +
                  $"speed={_boot.session.SpeedMps * 3.6f:0.0}km/h nearbyRiders={lods.Length}");

        foreach (var (lod, dist) in lods)
        {
            int bodyOn = lod.body != null ? lod.body.Count(r => r != null && r.enabled) : -1;
            int bodyTotal = lod.body != null ? lod.body.Length : 0;
            int detailOn = lod.bikeDetail != null ? lod.bikeDetail.Count(r => r != null && r.enabled) : -1;
            int detailTotal = lod.bikeDetail != null ? lod.bikeDetail.Length : 0;
            bool farOn = lod.bikeFar != null && lod.bikeFar.enabled;
            bool farExists = lod.bikeFar != null;
            Debug.Log($"[minato-idle]   rider '{lod.name}' dist={dist:0.0}m " +
                      $"body={bodyOn}/{bodyTotal} bikeDetail={detailOn}/{detailTotal} " +
                      $"bikeFar={(farExists ? farOn.ToString() : "NULL")} " +
                      $"rig={(lod.rig != null ? "present" : "NULL")}");
            // FI DIAGNOSTIC: dump the RUNTIME body material's weathering knobs, to check whether
            // an asset-level fix (edited on disk) actually reached the instantiated renderer, as
            // opposed to a stale in-memory clone made before the fix was applied.
            if (lod.body != null)
                foreach (var r in lod.body)
                {
                    if (r == null) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        string wear = m.HasProperty("_WearAmount") ? m.GetFloat("_WearAmount").ToString("F2") : "-";
                        string moss = m.HasProperty("_MossAmount") ? m.GetFloat("_MossAmount").ToString("F2") : "-";
                        string grime = m.HasProperty("_GrimeAmount") ? m.GetFloat("_GrimeAmount").ToString("F2") : "-";
                        string detail = m.HasProperty("_DetailAmount") ? m.GetFloat("_DetailAmount").ToString("F2") : "-";
                        string dapple = m.HasProperty("_DappleStrength") ? m.GetFloat("_DappleStrength").ToString("F2") : "-";
                        Debug.Log($"[minato-idle]     FI body mat '{m.name}' on '{r.name}': " +
                                  $"wear={wear} moss={moss} grime={grime} detail={detail} dapple={dapple}");
                    }
                }
            if (detailTotal == 0 && !farOn)
                Debug.LogError($"[minato-idle]   *** rider '{lod.name}' at {dist:0.0}m has " +
                               "ZERO enabled bike renderers (no bikeDetail parts, bikeFar off) " +
                               "while body is rendered - THIS IS THE MISSING-BIKE SIGNATURE.");
        }
    }

    private void Fail(string why)
    {
        Debug.LogError("[minato-idle] " + why);
        Failed = true;
        Finished = true;
    }

    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    private IEnumerator CaptureLowContact(string name)
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        var t = follow != null ? follow.target : null;
        if (t == null) { yield break; }
        bool wasEnabled = follow.enabled;
        follow.enabled = false;
        // Close, low, off to the side - deliberately the angle most likely to show/expose an
        // absent bike frame, since it looks straight across the crank/wheel height.
        _cam.transform.position = t.position + t.right * 2.6f - t.forward * 4.2f + Vector3.up * 1.0f;
        _cam.transform.LookAt(t.position + Vector3.up * 0.6f);
        yield return Capture(name);
        follow.enabled = wasEnabled;
    }

    private void RouteCanvasesToCamera()
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);

        var uiGo = new GameObject("~MinatoIdleHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.fieldOfView = _cam.fieldOfView;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;

        var c = _boot.hud != null ? _boot.hud.Canvas : null;
        if (c != null)
        {
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = _uiCam;
            c.planeDistance = 1f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
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
        if (_uiCam != null)
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

        Debug.Log($"[minato-idle] {name}: d {_boot.session.DistanceM:0} m, cam {_cam.transform.position}");
    }
}

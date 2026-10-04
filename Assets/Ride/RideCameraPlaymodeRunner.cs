using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Photographs the ride chase camera in REAL PLAY MODE, at the sharpest corners on the course,
/// with the old banked frame and the new heading-only frame, from the same arc metres.
///
/// WHY: the player reported "I'm looking at my character from the side" during normal riding.
/// That is not something a log can settle - it is a framing complaint, so the fix has to be
/// judged from the same two frames a player would compare. And it has to be play mode: outside
/// it there is no player loop, the rider's SkinnedMeshRenderer draws the pose it was last
/// evaluated at, and the framing in the shot is not the framing the player gets.
///
/// The corners are FOUND, not hard-coded: the harness walks the course, measures heading change
/// per metre, and shoots the tightest turns it finds plus a straight as a control. So it still
/// points at the worst case if the route is ever re-baked.
/// </summary>
public class RideCameraPlaymodeRunner : MonoBehaviour
{
    /// <summary>Absolute output folder.</summary>
    public string outDir;

    /// <summary>PROVISIONAL: how many of the sharpest corners to photograph.</summary>
    public int cornerShots = 2;

    /// <summary>PROVISIONAL: scripted effort so the rider is moving, not parked.</summary>
    [Range(0f, 1f)] public float playerEffort = 0.8f;

    /// <summary>
    /// PROVISIONAL: fast-forward, so two full passes fit inside a batchmode run.
    ///
    /// Kept LOW on purpose. At 8x the camera's damping lag is 8x what a player ever sees, which
    /// exaggerated the very defect under test and drove the camera through the terrain; 2x is
    /// fast enough for two laps and close enough to honest that the measured off-axis angles
    /// mean something.
    /// </summary>
    public float timeScale = 2f;

    /// <summary>Watchdog.</summary>
    public float maxSeconds = 320f;

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private Camera _cam;
    private Camera _uiCam;
    private KuroFollowCamera _follow;
    private RouteFollower _route;
    private Vector3 _fixedOffset;
    private RouteDressingStreamer _streamer;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        _boot.Resolve();

        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }
        _follow = _cam.GetComponent<KuroFollowCamera>();
        if (_follow == null) { Fail("the ride camera has no KuroFollowCamera"); yield break; }
        _fixedOffset = _follow.offset;   // as staged - the "after" case
        _route = FindFirstObjectByType<RouteFollower>();
        _streamer = FindFirstObjectByType<RouteDressingStreamer>(FindObjectsInactive.Include);

        yield return null;

        // Normal riding, no encounter: this is a complaint about the everyday camera.
        if (_boot.encounter != null) _boot.encounter.config.encounterEnabled = false;

        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("pass_sprint");
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.EffortInput = playerEffort;

        var course = _boot.session.Course;
        if (course == null) { Fail("no course"); yield break; }

        var marks = FindCorners(course);
        RouteCanvasesToCamera();

        // TWO CONTINUOUS PASSES, not teleports. The first version of this harness seeked
        // straight to each corner, and photographed a rider hanging in an empty blue void: the
        // world is brought up around the ride as it progresses, and a jump lands outside all of
        // it. Riding the course for real also means the camera damping is in a genuine state
        // when the shutter opens, which is the whole point of the complaint.
        yield return Pass(false, "before", "BANKED (old)", marks);
        yield return Pass(true, "after", "HEADING-ONLY (new)", marks);

        Debug.Log("[ride-cam] done, frames written to " + outDir);
        Finished = true;
    }

    /// <summary>One lap of the climb with a given chase frame, shooting as each mark goes by.</summary>
    private IEnumerator Pass(bool yawOnly, string tag, string label, List<Mark> marks)
    {
        _follow.yawOnlyFrame = yawOnly;
        // The "before" pass has to reproduce what the player actually rode, and that was as much
        // about the SERIALIZED offset as about the frame: the scene carried (2.55, 1.18, -3.35),
        // i.e. 2.55 m of lateral bias - 37 degrees off-axis before the bike has leaned at all.
        _follow.offset = yawOnly ? _fixedOffset : new Vector3(2.55f, 1.18f, -3.35f);

        // NO ResetRide() here. The environment root and the roadside dressing are brought up by
        // RegionDirector / RouteDressingStreamer around the rider's CURRENT position, and
        // resetting the ride underneath them photographed a rider and a bicycle hanging over an
        // empty sea with no road at all. Seek, then re-apply the streamer explicitly - which is
        // exactly what RegionDirector does after a fast travel.
        _boot.session.SeekTo(20f);
        if (_route != null) _route.Apply();
        if (_streamer != null && _boot.rider != null) _streamer.Apply(_boot.rider.position);
        Time.timeScale = timeScale;

        var shot = new HashSet<string>();
        float t0 = Time.realtimeSinceStartup;

        while (shot.Count < marks.Count && Time.realtimeSinceStartup - t0 < maxSeconds)
        {
            _boot.devices.EffortInput = playerEffort;
            float d = _boot.session.DistanceM;

            foreach (var m in marks)
            {
                if (shot.Contains(m.name)) continue;
                if (d < m.metres) continue;
                shot.Add(m.name);
                yield return Shoot("cam_" + tag + "_" + m.name, m, label);
                break;
            }
            yield return null;
        }

        Time.timeScale = 1f;
        if (shot.Count < marks.Count)
            Debug.LogWarning($"[ride-cam] {tag} pass only got {shot.Count}/{marks.Count} marks " +
                             $"(reached {_boot.session.DistanceM:0} m).");
    }

    private struct Mark
    {
        public float metres;
        public float turnDegPerM;
        public string name;
    }

    /// <summary>
    /// Walks the course and returns the sharpest corners plus a straight control. Sharpness is
    /// heading change per metre, which is the same quantity the rider's lean is derived from.
    /// </summary>
    private List<Mark> FindCorners(RouteCourse course)
    {
        const float Step = 2f;
        var all = new List<Mark>();
        for (float d = 20f; d < course.Length - 20f; d += Step)
        {
            float turn = Mathf.Abs(Mathf.DeltaAngle(course.HeadingAt(d), course.HeadingAt(d + Step)))
                         / Step;
            all.Add(new Mark { metres = d, turnDegPerM = turn });
        }
        all.Sort((a, b) => b.turnDegPerM.CompareTo(a.turnDegPerM));

        var picked = new List<Mark>();
        foreach (var m in all)
        {
            if (picked.Count >= cornerShots) break;
            bool tooClose = false;
            foreach (var p in picked)
                if (Mathf.Abs(p.metres - m.metres) < 60f) { tooClose = true; break; }
            if (tooClose) continue;
            var mm = m;
            mm.name = "corner" + (picked.Count + 1);
            picked.Add(mm);
        }

        // A straight as the control: the flattest bit of road we can find.
        var straight = all[all.Count - 1];
        straight.name = "straight";
        picked.Add(straight);

        foreach (var p in picked)
            Debug.Log($"[ride-cam] mark {p.name}: {p.metres:0} m, turn {p.turnDegPerM:0.00} deg/m, " +
                      $"bank {course.BankAt(p.metres):0.0} deg");
        return picked;
    }

    /// <summary>
    /// Removed: this seeked straight to an arc metre, which lands outside the world the ride
    /// brings up around itself. Kept as a note so it is not reinvented.
    /// </summary>
    private IEnumerator Shoot(string name, Mark m, string label)
    {
        yield return null;

        // How far off "directly behind" the camera actually sits, measured in the TARGET's own
        // horizontal frame. The target is what the camera is written against; measuring against
        // some other rider object produced 221 m of nonsense on the first run.
        var rider = _follow.target != null ? _follow.target : _boot.rider;
        Vector3 flatFwd = Vector3.ProjectOnPlane(rider.forward, Vector3.up).normalized;
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatFwd);
        Vector3 toCam = _cam.transform.position - rider.position;
        float lateral = Vector3.Dot(toCam, flatRight);
        float behind = -Vector3.Dot(toCam, flatFwd);
        float up = toCam.y;
        float offAxisDeg = Mathf.Atan2(Mathf.Abs(lateral), Mathf.Max(0.01f, behind)) * Mathf.Rad2Deg;

        // Where the rider lands in the frame. 0.5 is dead centre.
        Vector3 vp = _cam.WorldToViewportPoint(rider.position + Vector3.up * 1.0f);

        var bakes = new List<GameObject>();
        if (rider != null) bakes.AddRange(BeginBake(_boot.rider.gameObject, true));

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
        EndBake(bakes);

        float riderRoll = Mathf.Asin(Mathf.Clamp(rider.right.y, -1f, 1f)) * Mathf.Rad2Deg;
        float camRoll = Vector3.SignedAngle(_cam.transform.right,
                                            Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up),
                                            _cam.transform.forward);
        Debug.Log($"[ride-cam] {name} [{label}] at {m.metres:0} m (turn {m.turnDegPerM:0.00} deg/m): " +
                  $"rider lean {riderRoll:0.0} deg, camera roll {camRoll:0.0} deg, " +
                  $"lateral {lateral:+0.00;-0.00} m, behind {behind:0.00} m, up {up:0.00} m, " +
                  $"off-axis {offAxisDeg:0.0} deg, rider at viewport x {vp.x:0.000} y {vp.y:0.000}");
    }

    private void RouteCanvasesToCamera()
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);

        var uiGo = new GameObject("~RideCamHudCam");
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

    // ---- skinned-mesh bake. A manual Camera.Render() does not line up with Unity's per-frame
    // skinning submission and the player comes back shredded. This is the Hanakage runner's
    // helper verbatim, INCLUDING fixTransformScale: the first version of this harness dropped
    // that argument and Kuro vanished from the shot entirely, leaving a riderless bicycle - his
    // "char1" renderer carries a leftover ~0.01 import scale that a naive bake inherits.
    private static List<GameObject> BeginBake(GameObject root, bool fixTransformScale)
    {
        var temps = new List<GameObject>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var baked = new Mesh { name = smr.name + "_BakedPose" };
            smr.BakeMesh(baked, false);

            var go = new GameObject("~RideCamBakedPose_" + smr.name);
            go.transform.SetParent(smr.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = smr.sharedMaterials;
            mr.shadowCastingMode = smr.shadowCastingMode;
            mr.receiveShadows = smr.receiveShadows;

            if (fixTransformScale)
            {
                Vector3 wp = go.transform.position;
                Quaternion wr = go.transform.rotation;
                go.transform.SetParent(null, true);
                go.transform.position = wp;
                go.transform.rotation = wr;
                go.transform.localScale = Vector3.one;
            }

            smr.enabled = false;
            temps.Add(go);
        }
        return temps;
    }

    private void EndBake(List<GameObject> temps)
    {
        if (_boot != null && _boot.rider != null)
            foreach (var smr in _boot.rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.enabled = true;

        foreach (var t in temps)
        {
            if (t == null) continue;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(t);
        }
    }

    private void Fail(string why)
    {
        Debug.LogError("[ride-cam] " + why);
        Failed = true;
        Finished = true;
    }
}
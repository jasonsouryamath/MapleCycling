using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// FIX-IMPLEMENTER reproduction / verification harness for the "Kuro blows out in real Sakura
/// gameplay" defect (legs clip pure white, black jersey crushes to a detail-less blob, shoulders
/// vanish). It photographs the player from a TIGHT DIRECT-REAR framing that matches the user's
/// gameplay_defect_sakura_rear render, but does it in REAL PLAY MODE, down the real ride camera,
/// on the real Sakura road with the real HDRP exposure.
///
/// WHY PLAY MODE (and why the edit-mode fi_harsh_repro harness gave a false pass): those harnesses
/// build a throwaway Camera and bolt a Built-in <c>SakuraPostFX</c> (an OnRenderImage grade) onto
/// it. Under HDRP, OnRenderImage never fires, so that harness renders at a DIFFERENT exposure than
/// the shipped game camera - it can pass while the real game still clips. Reproducing has to use
/// the real ride camera in play, exactly as SakuraBenchmarkPlaymodeRunner does.
///
/// It also DUMPS the live material properties the player is actually rendering with (rolloff,
/// shadow-ambient, matte floor) so "which material clips" is answered from what is on screen, not
/// from a .mat file on disk that may not be what the prefab instance resolves to.
/// </summary>
public class FiKuroRearRunner : MonoBehaviour
{
    public string outDir;
    public string tag = "shot";
    [Range(0f, 1f)] public float playerEffort = 0.75f;
    public float timeScale = 2f;
    public float shootAtM = 430f;    // steep open climb - the brightest lit stretch, worst case for skin
    public float maxSeconds = 240f;
    public string courseId = "pass_sprint";

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private Camera _cam;
    private RouteFollower _route;
    private RouteDressingStreamer _streamer;
    private bool _viewFront;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        _boot.Resolve();

        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }
        _route = FindFirstObjectByType<RouteFollower>();
        _streamer = FindFirstObjectByType<RouteDressingStreamer>(FindObjectsInactive.Include);

        yield return null;

        if (_boot.encounter != null) _boot.encounter.config.encounterEnabled = false;

        // Region + distance + view are env-overridable so the SAME build can be shot in Sakura
        // (bright) and Shiosai (cool/overcast hydrangeas), rear and front, without recompiling.
        var regionSel = (System.Environment.GetEnvironmentVariable("MR_REAR_REGION") ?? "sakura").Trim().ToLowerInvariant();
        bool shiosai = regionSel.StartsWith("shio");
        string regionId = shiosai ? RegionCatalog.ShiosaiCoast : RegionCatalog.SakuraPass;
        _viewFront = (System.Environment.GetEnvironmentVariable("MR_REAR_VIEW") ?? "rear").Trim().ToLowerInvariant().StartsWith("front");

        var sm = System.Environment.GetEnvironmentVariable("MR_REAR_M");
        if (float.TryParse(sm, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out var mOverride))
            shootAtM = mOverride;
        else if (shiosai) shootAtM = 150f;   // a built-course default for Shiosai

        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        // Fast travel brings the region's environment roots up and applies its grade; a bare
        // SelectCourse leaves the ride over open water.
        if (_boot.regions != null) _boot.regions.FastTravel(regionId);
        // Sakura: keep the short pass_sprint climb (matches the prior captures). Shiosai: use the
        // course FastTravel already selected (its built course).
        if (!shiosai) _boot.session.SelectCourse(courseId);
        if (_boot.regions != null) _boot.regions.SyncFromSession();
        _boot.devices.acceptKeyboardEffort = false;
        _boot.devices.EffortInput = playerEffort;

        var course = _boot.session.Course;
        if (course == null) { Fail("no course for region " + regionId); yield break; }
        if (course.Length < shootAtM + 10f) shootAtM = Mathf.Max(30f, course.Length * 0.4f);
        Debug.Log($"[fi-rear] region={regionId} course='{course}' len={course.Length:0} shootAtM={shootAtM:0} front={_viewFront}");

        DumpMaterials();

        _boot.session.SeekTo(Mathf.Max(10f, shootAtM - 40f));
        if (_route != null) _route.Apply();
        if (_streamer != null && _boot.rider != null) _streamer.Apply(_boot.rider.position);
        yield return null;
        yield return null;

        Time.timeScale = timeScale;
        float t0 = Time.realtimeSinceStartup;
        while (_boot.session.DistanceM < shootAtM && Time.realtimeSinceStartup - t0 < maxSeconds)
        {
            _boot.devices.EffortInput = playerEffort;
            if (_streamer != null && _boot.rider != null) _streamer.Apply(_boot.rider.position);
            yield return null;
        }
        Time.timeScale = 1f;
        yield return null;

        yield return Shoot();

        Debug.Log("[fi-rear] done -> " + outDir);
        Finished = true;
    }

    private void DumpMaterials()
    {
        if (_boot.rider == null) { Debug.LogWarning("[fi-rear] no rider to dump"); return; }
        foreach (var smr in _boot.rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var m in smr.sharedMaterials)
            {
                if (m == null) continue;
                Debug.Log(string.Format(
                    "[fi-rear] LIVE MAT '{0}' (shader {1}) rolloff={2} shadowAmb={3} matte={4} specStr={5} amb={6}",
                    m.name, m.shader != null ? m.shader.name : "null",
                    m.HasProperty("_HighlightRolloff") ? m.GetFloat("_HighlightRolloff").ToString("F2") : "n/a",
                    m.HasProperty("_ShadowAmbient") ? m.GetFloat("_ShadowAmbient").ToString("F2") : "n/a",
                    m.HasProperty("_MatteFloor") ? m.GetColor("_MatteFloor").ToString("F3") : "n/a",
                    m.HasProperty("_SpecStrength") ? m.GetFloat("_SpecStrength").ToString("F2") : "n/a",
                    m.HasProperty("_AmbientStrength") ? m.GetFloat("_AmbientStrength").ToString("F2") : "n/a"));
            }
        }
    }

    private IEnumerator Shoot()
    {
        yield return null;

        var rider = _boot.rider;
        Vector3 up = Vector3.up;
        Vector3 flatFwd = Vector3.ProjectOnPlane(rider.forward, up).normalized;

        // Rear: tight direct-rear (matches the defect images). Front: a 3/4-front of the face/chest.
        Vector3 camPos, look;
        if (_viewFront)
        {
            camPos = rider.position + flatFwd * 2.7f + up * 1.35f + Vector3.Cross(up, flatFwd) * 0.5f;
            look = rider.position + up * 1.0f;
        }
        else
        {
            camPos = rider.position - flatFwd * 3.1f + up * 1.45f;
            look = rider.position + up * 0.95f;
        }

        // Reposition the REAL ride camera so we inherit its HDRP exposure/post exactly.
        var prevPos = _cam.transform.position;
        var prevRot = _cam.transform.rotation;
        float prevFov = _cam.fieldOfView;
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow != null) follow.enabled = false;   // stop it snapping back this frame
        _cam.transform.position = camPos;
        _cam.transform.rotation = Quaternion.LookRotation(look - camPos, up);
        _cam.fieldOfView = 46f;

        var bakes = new List<GameObject>();
        if (rider != null) bakes.AddRange(BeginBake(rider.gameObject, true));

        const int W = 1080, H = 1760;
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
        File.WriteAllBytes(Path.Combine(outDir, tag + ".png"), tex.EncodeToPNG());

        ReportTone(tex, W, H);

        Destroy(tex);
        rt.Release();
        Destroy(rt);
        EndBake(bakes);

        _cam.transform.position = prevPos;
        _cam.transform.rotation = prevRot;
        _cam.fieldOfView = prevFov;
        if (follow != null) follow.enabled = true;

        Debug.Log($"[fi-rear] wrote {tag}.png at {_boot.session.DistanceM:0} m");
    }

    // Legs live in the lower-centre of the frame; torso in the mid-centre. Sample those bands and
    // report the clip metrics so the "white legs / black torso" defect is quantified as well as
    // seen. The RENDER is the judge; these are only a tripwire.
    private void ReportTone(Texture2D tex, int W, int H)
    {
        var px = tex.GetPixels();
        int legWhite = 0, legN = 0, torsoBlack = 0, torsoN = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                // centre 40% horizontally only (the rider), skip road/sky at the sides
                if (x < W * 0.30f || x > W * 0.70f) continue;
                var p = px[y * W + x];
                float fy = (float)y / H;   // 0 bottom, 1 top
                if (fy > 0.18f && fy < 0.42f)   // legs band
                {
                    legN++;
                    if (p.r > 0.90f && p.g > 0.90f && p.b > 0.90f) legWhite++;
                }
                else if (fy > 0.48f && fy < 0.66f)   // torso band
                {
                    torsoN++;
                    if (p.r < 0.06f && p.g < 0.06f && p.b < 0.06f) torsoBlack++;
                }
            }
        }
        Debug.Log(string.Format("[fi-rear] TONE legs whiteClip={0:F1}% (of centre-leg band)  torso nearBlack={1:F1}% (of centre-torso band)",
            legN > 0 ? 100f * legWhite / legN : 0f,
            torsoN > 0 ? 100f * torsoBlack / torsoN : 0f));
    }

    private static List<GameObject> BeginBake(GameObject root, bool fixTransformScale)
    {
        var temps = new List<GameObject>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var baked = new Mesh { name = smr.name + "_BakedPose" };
            smr.BakeMesh(baked, false);

            var go = new GameObject("~FiRearBaked_" + smr.name);
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
        Debug.LogError("[fi-rear] " + why);
        Failed = true;
        Finished = true;
    }
}

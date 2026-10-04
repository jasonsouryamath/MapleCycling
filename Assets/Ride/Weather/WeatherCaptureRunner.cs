using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Play-mode capture for the weather visuals (spawned only by the editor tool
/// <c>WeatherCapture</c>; never part of a normal ride). For each preset it rides to a Minato
/// station, snaps the weather to the preset, and photographs (a) the chase view and (b) the
/// view toward the arriving front, logging fog scale, wetness and curtain clearance.
/// </summary>
public sealed class WeatherCaptureRunner : MonoBehaviour
{
    public string outDir;
    public string prefix = "wx";
    public string[] presets = { "ClearCoastalBreeze", "MarineHaze", "SunShower" };
    public float stationM = 4200f;
    public int settleFrames = 90;

    public static bool Finished, Failed;

    private RideBootstrap _boot;
    private Camera _cam;

    private IEnumerator Start()
    {
        Finished = Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }
        yield return null;

        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.MinatoCoast))
            Debug.LogWarning("[wx-cap] could not fast travel to Minato.");
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("minato_crossing");
        _boot.devices.acceptKeyboardEffort = false;
        yield return null;

        float t0 = Time.realtimeSinceStartup;
        while (WeatherDirector.Instance == null && Time.realtimeSinceStartup - t0 < 30f) yield return null;
        var wd = WeatherDirector.Instance;
        if (wd == null) { Fail("no WeatherDirector"); yield break; }

        var route = new List<Vector2>();
        var course = _boot.session.Course;
        if (course != null)
            for (float m = 0f; m <= course.Length; m += 50f) { var p = course.PositionAt(m); route.Add(new Vector2(p.x, p.z)); }

        foreach (var name in presets)
        {
            var p = Resources.Load<WeatherPreset>("Weather/" + name);
            if (p == null) { Debug.LogError($"[wx-cap] missing preset {name}"); Failed = true; continue; }
            wd.SetPreset(p, 0.05f);
            _boot.session.SeekTo(stationM);
            if (_boot.follower != null) _boot.follower.Apply();
            for (int f = 0; f < settleFrames; f++) { _boot.devices.EffortInput = 0.6f; SnapCamera(); yield return null; }

            var s = wd.State;
            var atm = WeatherAtmosphere.Instance;
            var wet = WeatherWetness.Instance;
            Debug.Log($"[wx-cap] {name}: precip {s.Precipitation:0.00} hum {s.Humidity:0.00} fog {s.Fog:0.00} " +
                      $"-> fog x{(atm != null ? atm.FogScale : -1f):0.00}, HDRP base mfp {(atm != null ? atm.BaseMeanFreePath : -1f):0}, " +
                      $"RenderSettings density {RenderSettings.fogDensity:0.000000}, road wet {(wet != null ? wet.Wetness : -1f):0.00}, " +
                      $"curtains {(atm != null ? atm.VisibleCurtains : -1)}");

            Transform nearest = null; float bestAlpha = 0f;
            var rider = wd.rider != null ? wd.rider.position : _cam.transform.position;
            if (atm != null)
                for (int ci = 0; ci < atm.Curtains.Count; ci++)
                {
                    var c = atm.Curtains[ci];
                    var r = c.GetComponent<Renderer>();
                    if (r == null || !r.enabled) continue;
                    float alpha = atm.CurtainAlpha[ci];
                    var c2 = new Vector2(c.position.x, c.position.z);
                    float dR = (c2 - new Vector2(rider.x, rider.z)).magnitude;
                    float dRoute = route.Count > 1 ? WeatherAtmosphere.DistanceToPolyline(c2, route) : -1f;
                    Debug.Log($"[wx-cap]   curtain {c.name}: alpha {alpha:0.00}, {dR:0} m from rider, {dRoute:0} m from route, " +
                              $"width {c.localScale.x:0} height {c.localScale.y:0}, pos {c.position}");
                    if (dRoute >= 0f && dRoute < atm.routeClearanceM) { Debug.LogError("[wx-cap] FAIL curtain over route"); Failed = true; }
                    if (dR < 1000f - 1f || dR > 3000f + 1f) { Debug.LogError("[wx-cap] FAIL curtain outside 1-3 km"); Failed = true; }
                    if (alpha > bestAlpha) { bestAlpha = alpha; nearest = c; }
                }

            SnapCamera();
            Capture($"{prefix}_{name}_chase");
            // Toward the arriving front (or the nearest curtain), from rider eye height.
            Vector3 look;
            if (nearest != null) look = nearest.position + Vector3.up * 350f;
            else
            {
                float a = s.FrontDeg * Mathf.Deg2Rad;
                look = rider + new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a)) * 2000f + Vector3.up * 150f;
            }
            _cam.transform.position = rider + Vector3.up * 3f;
            _cam.transform.LookAt(new Vector3(look.x, rider.y + 3f + (look.y - rider.y) * 0.08f, look.z));
            float fov = _cam.fieldOfView;
            _cam.fieldOfView = 70f;
            Capture($"{prefix}_{name}_front");
            _cam.fieldOfView = fov;
            yield return null;
        }

        Debug.Log($"[wx-cap] done -> {outDir}");
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

    private void Capture(string name)
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
        Debug.Log($"[wx-cap] wrote {name}.png, cam {_cam.transform.position} fwd {_cam.transform.forward}");
    }

    private void Fail(string why)
    {
        Debug.LogError("[wx-cap] " + why);
        Failed = Finished = true;
    }
}

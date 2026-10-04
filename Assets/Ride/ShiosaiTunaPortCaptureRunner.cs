using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Play-mode photographs of the Shiosai Grand Coast TUNA PORT (Fishing Village chapter,
/// 4.95-5.5 km), taken from the real ride camera with the HUD, plus a few HUD-free free-camera
/// views (aerial, plan, market interior, fishmonger row, quay). Driven by ShiosaiTunaPortCapture.
/// Frames: reference/good_graphics/shiosai_tunaport/&lt;tag&gt;_*.png (tag from MR_TUNAPORT_TAG).
/// </summary>
public class ShiosaiTunaPortCaptureRunner : MonoBehaviour
{
    public string outDir;
    public string shotTag = "after";

    /// <summary>Chase-camera stations along the course, metres.</summary>
    public float[] stations = { 4880f, 5010f, 5075f, 5110f, 5150f, 5185f, 5240f, 5300f, 5360f, 5430f };

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
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }
        yield return null;

        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.ShiosaiCoast))
            Debug.LogWarning("[tunaport-cap] could not fast travel to the coast region.");
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse("shiosai_breeze");
        _boot.devices.acceptKeyboardEffort = false;
        RouteCanvasesToCamera();
        yield return null;

        string only = System.Environment.GetEnvironmentVariable("MR_TUNAPORT_ONLY") ?? "";
        bool chase = only == "" || only.Contains("chase");
        bool free = only == "" || only.Contains("free");

        if (chase)
            foreach (float s in stations)
            {
                yield return Seek(s, 0.7f);
                yield return Capture($"{shotTag}_chase_{s:0000}m");
            }

        if (free)
        {
            var follow = _cam.GetComponent<KuroFollowCamera>();
            _hideHud = true;
            _boot.devices.HoldZeroPower = true;

            // name, station, eye (right, up, fwd), look (right, up, fwd), fov
            var shots = new (string n, float s, Vector3 eye, Vector3 look, float fov)[]
            {
                ("plan",          5190f, new Vector3(-40f, 330f, 0f),  new Vector3(-40f, 0f, 1f),   60f),
                ("aerial_bay",    5120f, new Vector3(45f, 60f, -70f),  new Vector3(-45f, 0f, 60f),  55f),
                ("aerial_low",    5060f, new Vector3(-5f, 22f, -40f),  new Vector3(-30f, 4f, 70f),  60f),
                ("market_front",  5105f, new Vector3(1.5f, 1.7f, 0f),  new Vector3(-22f, 1.2f, 10f), 62f),
                ("market_inside", 5140f, new Vector3(-14f, 1.7f, -18f), new Vector3(-22f, 0.6f, 12f), 62f),
                ("stalls",        5130f, new Vector3(-2f, 1.6f, -6f),  new Vector3(12f, 2.0f, 10f), 62f),
                ("quay",          5160f, new Vector3(-61f, -5.8f, -22f), new Vector3(-66f, -6.6f, 30f), 62f),
                ("from_sea",      5150f, new Vector3(-130f, 14f, 10f), new Vector3(0f, 6f, 10f),    50f),
                ("north_street",  5330f, new Vector3(0.8f, 1.7f, -4f), new Vector3(-6f, 1.8f, 30f), 62f),
                // pass 2 close-ups: people and the new dressing
                ("kaitai",        5180f, new Vector3(-19f, 2.2f, 3f),  new Vector3(-25.5f, 1.1f, 7f), 48f),
                ("npc_auction",   5100f, new Vector3(-18f, 1.9f, 5f),  new Vector3(-22.4f, 1.1f, 10f), 42f),
                ("npc_shop",      5130f, new Vector3(5.6f, 1.7f, -3f), new Vector3(11f, 1.2f, 1.5f),  52f),
                ("npc_tent",      5360f, new Vector3(-3.2f, 1.7f, -3f), new Vector3(-7.4f, 1.1f, 2.5f), 52f),
                ("landing",       5074f, new Vector3(-57f, -5.4f, -7f), new Vector3(-66f, -7.2f, 0f), 55f),
                ("shrine",        5064f, new Vector3(-54f, -5.2f, 5f), new Vector3(-47f, -6.4f, 0f),  55f),
                ("lanterns",      5370f, new Vector3(0.5f, 1.6f, -5f), new Vector3(0f, 4.2f, 30f),   62f),
                ("breakwater",    5064f, new Vector3(-100f, -3.6f, 1f), new Vector3(-120f, -6.2f, -4.5f), 50f),
            };
            foreach (var sh in shots)
            {
                if (only != "" && only.Contains("free:") && !only.Contains(sh.n)) continue;
                yield return Seek(sh.s, 0f);
                var t = follow != null ? follow.target : null;
                if (t == null) continue;
                if (follow != null) follow.enabled = false;
                Vector3 R = t.right; R.y = 0f; R.Normalize();
                Vector3 F = t.forward; F.y = 0f; F.Normalize();
                Vector3 P = t.position;
                // the rider rolls on for a few metres after the seek: frame the shot on the
                // REQUESTED station so close-ups land on their subject
                P -= F * (_boot.session.DistanceM - sh.s);
                Vector3 eye = P + R * sh.eye.x + Vector3.up * sh.eye.y + F * sh.eye.z;
                Vector3 look = P + R * sh.look.x + Vector3.up * sh.look.y + F * sh.look.z;
                _cam.transform.position = eye;
                _cam.transform.LookAt(look, sh.n == "plan" ? F : Vector3.up);
                float fov0 = _cam.fieldOfView;
                _cam.fieldOfView = sh.fov;
                yield return Capture($"{shotTag}_{sh.n}");
                _cam.fieldOfView = fov0;
                if (follow != null) follow.enabled = true;
            }
            _boot.devices.HoldZeroPower = false;
            _hideHud = false;
        }

        Debug.Log($"[tunaport-cap] done -> {outDir}");
        Finished = true;
    }

    private IEnumerator Seek(float s, float effort)
    {
        _boot.session.SeekTo(s);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < 24; f++) { _boot.devices.EffortInput = effort; yield return null; }
        SnapCamera();
    }

    private void Fail(string why)
    {
        Debug.LogError("[tunaport-cap] " + why);
        Failed = true;
        Finished = true;
    }

    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null || !follow.enabled) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    private void RouteCanvasesToCamera()
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);
        var uiGo = new GameObject("~TunaPortHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.fieldOfView = _cam.fieldOfView;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;
        var hd = _uiCam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        if (hd == null) hd = uiGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.None;
        hd.volumeLayerMask = 0;
        hd.customRenderingSettings = true;
        foreach (var f in new[]
                 {
                     UnityEngine.Rendering.HighDefinition.FrameSettingsField.AtmosphericScattering,
                     UnityEngine.Rendering.HighDefinition.FrameSettingsField.Volumetrics,
                     UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess,
                     UnityEngine.Rendering.HighDefinition.FrameSettingsField.CustomPass,
                 })
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, false);
        }
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
        if (_uiCam != null && !_hideHud)
        {
            _uiCam.fieldOfView = _cam.fieldOfView;
            _uiCam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            _uiCam.Render();
            _uiCam.targetTexture = null;
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
        Debug.Log($"[tunaport-cap] {name}: d {_boot.session.DistanceM:0} m, cam {_cam.transform.position}");
    }
}

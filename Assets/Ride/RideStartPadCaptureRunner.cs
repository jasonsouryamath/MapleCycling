using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// QA capture only: fast-travels to each listed region, parks the rider on the start line and
/// photographs the <see cref="RideStartPad"/> from four fixed angles (chase, look-back, side,
/// aerial) so a start-pad change can be judged per region by render. Driven by
/// <c>RideStartPadCapture.Run</c>; regions come from MR_PAD_REGIONS (comma list of region ids).
/// </summary>
public class RideStartPadCaptureRunner : MonoBehaviour
{
    public string outDir;
    public bool forcePlain;
    public string[] regions = { RegionCatalog.MinatoCoast, RegionCatalog.MapleCity, RegionCatalog.SakuraPass };

    public static bool Finished;
    public static bool Failed;

    private RideBootstrap _boot;
    private Camera _cam;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        RideStartPad.ForcePlainStyle = forcePlain;
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        if (_cam == null) { Fail("no ride camera"); yield break; }
        yield return null;

        foreach (var id in regions)
        {
            if (_boot.regions == null || !_boot.regions.FastTravel(id))
            {
                Debug.LogWarning($"[pad-cap] could not fast travel to '{id}'.");
                continue;
            }
            _boot.devices.acceptKeyboardEffort = false;
            _boot.devices.HoldZeroPower = true;
            var s = _boot.session;
            float d0 = s.startDistanceM;
            for (int f = 0; f < 40; f++)
            {
                _boot.devices.EffortInput = 0f;
                s.SeekTo(d0);
                if (_boot.follower != null) _boot.follower.Apply();
                yield return null;
            }

            var pad = FindFirstObjectByType<RideStartPad>();
            if (pad != null) pad.TryBuild();
            yield return null;
            int nPads = FindObjectsByType<RideStartPad>(FindObjectsSortMode.None).Length;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name != "Ride Start Pad") continue;
                Debug.Log($"[pad-cap] pad root active={t.gameObject.activeInHierarchy} layer={t.gameObject.layer} " +
                          $"children={t.childCount} components={nPads}");
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    var mf = r.GetComponent<MeshFilter>();
                    Debug.Log($"[pad-cap]   {r.name}: on={r.enabled && r.gameObject.activeInHierarchy} " +
                              $"bounds={r.bounds.center} size={r.bounds.size} verts={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1)} " +
                              $"shader={(m != null ? m.shader.name : "null")}");
                }
            }

            var c = s.Course;
            Vector3 p0 = c.PositionAt(d0);
            Vector3 H = c.PositionAt(Mathf.Min(d0 + 4f, d0 + c.Length * 0.5f)) - p0;
            H.y = 0f;
            if (H.sqrMagnitude < 1e-4f) H = Vector3.forward;
            H.Normalize();
            Vector3 R = Vector3.Cross(Vector3.up, H).normalized;

            var follow = _cam.GetComponent<KuroFollowCamera>();
            bool wasOn = follow != null && follow.enabled;
            if (follow != null && follow.target != null)
            {
                var t = follow.target;
                _cam.transform.position = t.position + t.TransformDirection(follow.offset);
                _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
            }
            yield return Capture($"{id}_chase");
            if (follow != null) follow.enabled = false;

            yield return Shot($"{id}_lookback", p0 + H * 12f + Vector3.up * 2.0f, p0 - H * 8f + Vector3.up * 1.0f);
            yield return Shot($"{id}_side", p0 + R * 13f - H * 4f + Vector3.up * 4.0f, p0 - H * 5f);
            yield return Shot($"{id}_aerial", p0 - H * 34f + R * 12f + Vector3.up * 24f, p0 - H * 4f);
            yield return Shot($"{id}_gate", p0 - H * 16f + Vector3.up * 1.7f, p0 + H * 6f + Vector3.up * 2.5f);
            if (System.Environment.GetEnvironmentVariable("MR_PAD_WIDE") == "1")
            {
                // Precinct shots (E7): the start's surroundings rather than the pad itself.
                yield return Shot($"{id}_wide_road", p0 + H * 45f - R * 2f + Vector3.up * 3f, p0 + R * 45f + H * 5f + Vector3.up * 8f);
                yield return Shot($"{id}_wide_aerial", p0 + H * 110f - R * 60f + Vector3.up * 70f, p0 + R * 40f - H * 5f);
                yield return Shot($"{id}_wide_terminus", p0 + H * 22f + Vector3.up * 3f, p0 - H * 60f + Vector3.up * 3f);
                yield return Shot($"{id}_wide_facade", p0 + R * 24f + H * 22f + Vector3.up * 1.7f, p0 + R * 48f - H * 6f + Vector3.up * 7f);
                yield return Shot($"{id}_wide_sheds", p0 + H * 140f + R * 70f + Vector3.up * 8f, p0 + H * 210f + R * 230f + Vector3.up * 6f);
            }

            if (follow != null) follow.enabled = wasOn;
            _boot.devices.HoldZeroPower = false;
        }

        Debug.Log($"[pad-cap] done, frames in {outDir}");
        RideStartPad.ForcePlainStyle = false;
        Finished = true;
    }

    private IEnumerator Shot(string name, Vector3 from, Vector3 at)
    {
        _cam.transform.position = from;
        _cam.transform.LookAt(at);
        yield return Capture(name);
    }

    private void Fail(string why)
    {
        Debug.LogError("[pad-cap] " + why);
        Failed = true;
        Finished = true;
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
        Debug.Log($"[pad-cap] {name} written");
    }
}

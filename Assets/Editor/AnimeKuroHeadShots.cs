using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Close head/face captures for the anime Kuro head-regen milestone. Frames off the solved
/// 'Head' bone rather than a hard-coded height, because the grafted head sits at a different
/// offset than the photoreal one and the old fixed-aim face shot landed on the chest.
/// </summary>
public static class AnimeKuroHeadShots
{
    private const string PocName = "Shiosai POC Realistic Rider";

    [MenuItem("MapleRide/Shiosai/Capture Anime Kuro Head Shots", priority = 45)]
    public static void Run()
    {
        string path = ShiosaiCoastEnvironment.ScenePath;
        if (EditorSceneManager.GetActiveScene().path != path)
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/shiosai_real_rider"));
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = null;
        if (regions != null)
        {
            regions.Resolve();
            previous = regions.currentRegionId;
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }

        try
        {
            Transform poc = null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == PocName) { poc = t; break; }
            if (poc == null) { Debug.LogError("[head-shot] POC not staged."); return; }

            var rig = poc.GetComponent<KuroBikeRig>();
            if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }

            // Batchmode re-draws the first solved pose unless this is forced per render.
            foreach (var s in poc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                s.forceMatrixRecalculationPerRender = true;
                s.updateWhenOffscreen = true;
            }

            Transform head = null;
            foreach (var t in poc.GetComponentsInChildren<Transform>(true))
                if (t.name == "Head") { head = t; break; }
            if (head == null) { Debug.LogError("[head-shot] no 'Head' bone."); return; }

            // Frame off the real skinned head bounds, not a guessed radius - the grafted head is
            // a different size to the photoreal one and a fixed distance put the camera INSIDE
            // the helmet shell.
            var hb = new Bounds(head.position, Vector3.zero);
            foreach (var s in poc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                s.BakeMesh(baked, true);
                var verts = baked.vertices; var w = s.transform.localToWorldMatrix;
                foreach (var v in verts)
                {
                    var p = w.MultiplyPoint3x4(v);
                    if (p.y >= head.position.y - 0.04f) hb.Encapsulate(p);
                }
                Object.DestroyImmediate(baked);
            }
            Vector3 aim = hb.center;
            float radius = hb.extents.magnitude;
            Debug.Log($"[head-shot] head bone {head.position} bounds c={hb.center} size={hb.size} r={radius:F3}");

            Vector3 f = poc.forward, r = poc.right, up = Vector3.up;
            const float Fov = 30f;
            // 2.2x the bounding radius leaves comfortable headroom at this FOV.
            float D = Mathf.Max(0.5f, radius * 2.2f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));
            Debug.Log($"[head-shot] camera distance {D:F3}");

            Shot(dir, "anime_kuro_head_front", aim + f * D + up * D * 0.10f, aim, Fov);
            Shot(dir, "anime_kuro_head_face", aim + (f * 0.80f + r * 0.55f).normalized * D + up * D * 0.06f, aim, Fov);
            Shot(dir, "anime_kuro_head_side", aim + r * D, aim, Fov);
            Shot(dir, "anime_kuro_head_rear", aim - f * D + up * D * 0.16f, aim, Fov);
            Debug.Log("[head-shot] wrote 4 head closeups.");
        }
        finally
        {
            if (regions != null && previous != null)
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
        }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~HeadShotCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.ShiosaiAmbience;
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;

        const int w = 1100, h = 1100;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[head-shot] wrote {name}.png");
    }
}

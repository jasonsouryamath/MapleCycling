using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ADDITIVE, READ-ONLY verification harness for the Shiosai realistic-rider POC.
///
/// The shipped POC capture (ShiosaiRealisticRiderPoc.Capture) shoots controlled hero framings -
/// a side/high three-quarter that lights Kuro's near side. The USER'S complaint is the exact
/// GAMEPLAY framing: the real chase camera sits directly behind and slightly above, so the
/// camera only ever sees Kuro's SHADOW side, and the material crushed it to a black blob while
/// the chibi traffic riders (different material, higher ambient floor) stayed lit next to him.
///
/// This harness reproduces THAT frame: the serialized chase offset (0, 1.60, -4.20) from
/// RideCameraSetup, aim at rider + up*1.1, FOV 52 (the serialized "Sakura Camera" FOV), with a
/// pair of coast traffic chibis woken and parked flanking the POC so the lit-vs-Kuro comparison
/// is in ONE frame - the evidence a hero crop cannot give. It never saves the scene.
/// </summary>
public static class ShiosaiPocGameplayCapture
{
    private const string PocName = "Shiosai POC Realistic Rider";
    private static readonly Vector3 ChaseOffset = new Vector3(0f, 1.60f, -4.20f);
    private const float ChaseFov = 52f;
    private const float AimHeight = 1.1f;

    [MenuItem("MapleRide/Shiosai/Capture POC Gameplay Chase (follow-cam + chibi)", priority = 43)]
    public static void Capture()
    {
        CaptureInternal(false);
    }

    [MenuItem("MapleRide/Shiosai/Capture POC Leg Study (behind, crank phases)", priority = 44)]
    public static void CaptureLegStudy()
    {
        CaptureInternal(true);
    }

    private static void CaptureInternal(bool legStudy)
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

        var wokenChibis = new List<GameObject>();
        try
        {
            Transform poc = null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == PocName) { poc = t; break; }
            if (poc == null) { Debug.LogError("[poc-gameplay] POC not staged - run Stage first."); return; }

            // Solve + force per-render skinning on the POC (batchmode has no player loop).
            var rig = poc.GetComponent<KuroBikeRig>();
            if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
            ForceSkin(poc);

            // Wake two coast traffic chibis and park them flanking the POC, facing the same way,
            // exactly like the user's screenshot. They were seated + frozen at stage time, so the
            // saved-scene bone transforms already hold a real pose; we only re-home the root.
            var p = poc.position; var f = poc.forward; var r = poc.right; var up = Vector3.up;
            var flanks = new[] { -1.55f, 1.65f };
            int fi = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (legStudy) break;   // leg study is Kuro-only so nothing occludes the knees
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (fi >= flanks.Length) break;
                    if (t.name.StartsWith("Shiosai Rider ") && !t.gameObject.activeSelf && t.parent != null)
                    {
                        var g = t.gameObject;
                        g.SetActive(true);
                        // slightly behind and to the side so both flank riders sit in the chase frame
                        g.transform.SetPositionAndRotation(
                            p + r * flanks[fi] - f * 0.35f, poc.rotation);
                        var crig = g.GetComponentInChildren<CoralBikeRig>();
                        if (crig != null) { crig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); crig.ForceSolveOnce(); }
                        ForceSkin(g.transform);
                        wokenChibis.Add(g);
                        fi++;
                    }
                }
                if (fi >= flanks.Length) break;
            }
            Debug.Log($"[poc-gameplay] woke {wokenChibis.Count} flanking chibi(s) for comparison.");

            // THE gameplay chase frame. yaw-only frame == rider heading (POC rides straight), so
            // ChaseFrame is just the heading rotation; position = rider + heading * offset.
            Vector3 camPos = p + poc.forward * ChaseOffset.z + up * ChaseOffset.y + r * ChaseOffset.x;
            Vector3 aim = p + up * AimHeight;

            if (!legStudy)
            {
                Shot(dir, "poc_rider_gameplay_chase", camPos, aim, ChaseFov);
                Debug.Log("[poc-gameplay] captured chase frame to " + dir);
            }
            else
            {
                // Behind view, tighter and lower, at four crank phases so the NEAR leg is caught
                // mid-bend (not just top/bottom dead centre). This is the frame the leg-deformation
                // defect has to be answered in - straight behind, where a lateral knee collapse or
                // a splayed foot reads that the side view hides.
                if (rig != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (i > 0) rig.AdvanceCrank(90f);
                        rig.ForceSolveOnce();
                        ForceSkin(poc);
                        string tag = (i * 90).ToString("000");
                        // Slightly closer/lower than the chase cam so the knees fill the frame.
                        Vector3 lp = p - poc.forward * 3.0f + up * 1.15f;
                        Vector3 la = p + up * 0.70f;
                        Shot(dir, "poc_leg_behind_" + tag, lp, la, 40f);
                    }
                    Debug.Log("[poc-gameplay] captured 4 behind-view leg phases to " + dir);
                }
            }
        }
        finally
        {
            foreach (var g in wokenChibis) if (g != null) g.SetActive(false);
            if (regions != null && previous != null)
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
            MapleRideSceneBootstrap.DiscardChanges();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    private static void ForceSkin(Transform root)
    {
        foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            s.forceMatrixRecalculationPerRender = true;
            s.updateWhenOffscreen = true;
        }
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~PocChaseCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
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
        if (a.aerialRange > 0f)
        {
            fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange;
            fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
            fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
        }

        const int w = 1280, h = 960;
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
        Object.DestroyImmediate(go);
        rt.Release();
    }
}

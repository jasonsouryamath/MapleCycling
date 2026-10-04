using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Photographs the four showcase riders WHERE THE PLAYER MEETS THEM, on Sakura Pass, so the
/// quartet can be judged by eye rather than by a scale number and a material dump - both of
/// which have previously reported success on a rider that looked wrong.
///
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -quit -executeMethod SakuraQuartetShowcaseCapture.Capture
///
/// Four sets of frames are written to &lt;renders&gt;/sakura_quartet:
///   sakq_insitu        the group as actually staged, from behind, down the pass
///   sakq_line          all four abreast, front on, for a like-for-like comparison
///   sakq_&lt;name&gt;_rider  three-quarter front, whole rider and bike
///   sakq_&lt;name&gt;_face   close enough to read eyes, brows, mouth and helmet
///   sakq_&lt;name&gt;_side   side on, where a floating foot or a bad hair cut shows first
///
/// The riders are moved for the line-up and the isolated shots and then put back; the scene is
/// NEVER saved. Framing is plain and the grade is the region's own play-mode grade with depth of
/// field off: this project has repeatedly produced "verified" renders that were simply blown
/// out or soft, and a white face is not a pale rider, it is an unreadable render.
/// </summary>
public static class SakuraQuartetShowcaseCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    [MenuItem("MapleRide/NPCs/QA Capture Sakura Quartet Showcase", priority = 44)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // The scene can be saved with another region visible if a previous batch run fast
        // travelled; without this the whole capture photographs open water.
        SakuraSceneDefaults.Fix();

        string dir = MapleRidePaths.RenderDir("sakura_quartet");

        var group = SakuraQuartetShowcase.Group();
        if (group == null)
        {
            Debug.LogWarning("[sakq-cap] no showcase group - run SakuraQuartetShowcase.Stage.");
            return;
        }

        var riders = new List<(Transform tr, string name)>();
        foreach (var name in SakuraQuartetShowcase.Names)
        {
            var go = SakuraQuartetShowcase.Find(name);
            if (go == null) { Debug.LogWarning($"[sakq-cap] {name} is not staged."); continue; }
            riders.Add((go.transform, name));
        }
        if (riders.Count == 0) { Debug.LogWarning("[sakq-cap] nothing to photograph."); return; }
        Debug.Log($"[sakq-cap] {riders.Count} rider(s): " +
                  string.Join(", ", riders.ConvertAll(r => r.tr.name)));

        var restore = new List<(Transform tr, Vector3 pos, Quaternion rot, bool active)>();
        foreach (var (tr, _) in riders)
            restore.Add((tr, tr.position, tr.rotation, tr.gameObject.activeSelf));

        // A rider woken or moved by an edit-time tool is NOT posed: [ExecuteAlways] runs Setup,
        // but a headless run has no player loop, so LateUpdate never ticks and the skin stays in
        // its raw upright bind pose - arms down, feet on the road, bicycle through the body.
        foreach (var (tr, _) in riders) Solve(tr);

        var lead = riders[0].tr;
        var fwd = Flat(lead.forward);
        var side = Vector3.Cross(Vector3.up, fwd).normalized;

        // 1. AS STAGED. Behind the leading rider, at rider height, looking up the pass: this is
        //    roughly what the player sees on arrival, and it is the only shot that shows the
        //    real spacing and the real road context.
        {
            var eye = lead.position - fwd * 8f + Vector3.up * 1.7f - side * 1.2f;
            Shot(dir, "sakq_insitu", eye, lead.position + Vector3.up * 0.85f, 38f, 1800, 900);
        }

        // 2. ALL FOUR ABREAST. Moved for one frame, across the carriageway at the leading
        //    rider's point, all facing the camera-side down the road.
        for (int r = 0; r < riders.Count; r++)
        {
            var tr = riders[r].tr;
            var pos = lead.position + side * (-2.7f + r * 1.8f);
            pos.y = lead.position.y;
            tr.position = pos;
            tr.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Solve(tr);
            // Frozen local bounds are measured for the staged position; once a rider is moved
            // they can cull them out of the frame entirely.
            foreach (var smr in tr.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;
        }
        {
            var centre = lead.position + side * (-2.7f + (riders.Count - 1) * 0.9f);
            var eye = centre + fwd * 6.4f + Vector3.up * 1.05f;
            Shot(dir, "sakq_line", eye, centre + Vector3.up * 0.95f, 40f, 1800, 900);
        }

        // 3. ONE RIDER AT A TIME, ALL FOUR AT THE SAME SPOT.
        //
        //    Two things are deliberate here. First, everybody else is hidden: the riders stand
        //    1.8 m apart and the side camera sits ~2.5 m out along that same axis, which puts it
        //    inside the NEXT rider's head, and the three-quarter shot skims the neighbour too.
        //    Second, each rider is photographed at the SAME hero point - the middle of the
        //    carriageway at the leading rider's position - instead of at her place in the line.
        //    The outside places in the line sit over the verge under the trees, where the first
        //    pass came back correctly posed but too dark to judge a kit by; one spot on the open
        //    road gives all four identical, readable light.
        var hero = lead.position;
        foreach (var (tr, _) in riders) tr.gameObject.SetActive(false);

        foreach (var (tr, name) in riders)
        {
            tr.gameObject.SetActive(true);
            tr.position = hero;
            tr.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Solve(tr);

            float top = HeadTop(tr);
            // THE CROWN IS THE HELMET, NOT THE FACE: the eye line sits ~0.42 m below the top
            // vertex at staged scale. Aiming at the crown fills the frame with dome.
            var head = new Vector3(tr.position.x, top - 0.42f, tr.position.z);
            var body = tr.position + Vector3.up * (top - tr.position.y) * 0.55f;

            // 3.0 m rather than 2.35: at the closer range the rear wheel fell out of frame, and
            // a cropped bike cannot show a floating foot or a mis-scaled frame.
            Shot(dir, $"sakq_{name}_rider", body + fwd * 3.0f + side * 1.55f + Vector3.up * 0.55f,
                 body, 38f, 1000, 1250);
            // 1.9 m rather than 1.25: at 32 deg the closer range framed 0.72 m, which on this
            // lineage is less than the head - the first pass cropped the chin and the helmet.
            Shot(dir, $"sakq_{name}_face", head + fwd * 1.9f + side * 0.5f + Vector3.up * 0.08f,
                 head, 32f, 1000, 1000);
            // From the ROAD side, not the verge side: the bank and its tree shadow behind the
            // rider swallowed the first side pass.
            Shot(dir, $"sakq_{name}_side", body - side * 2.6f + Vector3.up * 0.30f,
                 body, 36f, 1000, 1250);

            tr.gameObject.SetActive(false);
        }

        foreach (var (tr, pos, rot, active) in restore)
        {
            tr.gameObject.SetActive(active);
            tr.position = pos;
            tr.rotation = rot;
        }

        Debug.Log($"[sakq-cap] done -> {dir}. Scene NOT saved.");
        MapleRideSceneBootstrap.DiscardChanges();
    }

    static void Solve(Transform rider)
    {
        var rig = rider.GetComponentInChildren<CoralBikeRig>(true);
        if (rig == null) { Debug.LogWarning($"[sakq-cap] '{rider.name}' has no CoralBikeRig."); return; }
        rig.ForceSolveOnce();
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
    }

    /// <summary>Highest BAKED vertex - Renderer.bounds on an imported skin is the import-time
    /// estimate and lies.</summary>
    static float HeadTop(Transform rider)
    {
        float top = float.NegativeInfinity;
        foreach (var smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var m = new Mesh();
            smr.BakeMesh(m, true);
            foreach (var v in m.vertices) top = Mathf.Max(top, smr.transform.TransformPoint(v).y);
            Object.DestroyImmediate(m);
        }
        return float.IsNegativeInfinity(top) ? rider.position.y + 1.30f : top;
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, int w, int h)
    {
        var go = new GameObject("~SakqCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        // The region's own play-mode grade, so the render shows what the player will see - with
        // depth of field off, because every defect this is looking for needs a sharp frame.
        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.SakuraAmbience;
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
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        Debug.Log($"[sakq-cap] wrote {name}.png");
    }
}

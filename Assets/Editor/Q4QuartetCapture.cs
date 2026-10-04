using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Photographs the four concept-sheet riders IN THE GAME, so the quartet can be checked by eye
/// rather than by a scale number and a material dump - both of which have previously reported
/// success on a rider that looked wrong.
///
///   Unity.exe -projectPath &lt;abs&gt; -batchmode -quit -executeMethod Q4QuartetCapture.Capture
///
/// The Shiosai coast pool is the one place all four are staged together, so this wakes that
/// region, lifts the four riders out of the parked pool, lines them up across the carriageway
/// and takes a full-rider row plus a per-rider three-quarter and a per-rider face/bike close-up.
/// Every rider is put back and the region restored; the scene is NEVER saved.
///
/// Framing is deliberately plain and the post FX exposure is the region's own: this project has
/// repeatedly produced "verified" renders that were simply blown out, and a white face is not a
/// pale rider, it is an unreadable render.
/// </summary>
public static class Q4QuartetCapture
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string EnvironmentRootName = "Shiosai Coast Environment";
    const string TrafficRootName = "Shiosai Traffic";

    /// <summary>Pool slots holding the four rigs - see ShiosaiNpcTraffic.Liveries order.</summary>
    /// <remarks>
    /// The last entry is deliberately NOT one of the new four. Slot 0 is an original
    /// Coral-lineage rider in an original livery, and having it in the same line-up, lit and
    /// framed identically, is the only way to tell "the new riders look odd" from "this is what
    /// every rider in this game has always looked like" - the parked pool pose, the open hands
    /// and the porcelain face are all the donor's, not something the quartet introduced.
    /// A null expected-name skips the slot identity check.
    /// </remarks>
    static readonly (int slot, string name)[] Picks =
    {
        (12, "Shiori"), (13, "Akihiro"), (14, "Shinobu"), (15, "Akane"), (0, null),
    };

    [MenuItem("MapleRide/NPCs/QA Capture Quartet", priority = 41)]
    public static void Capture()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        string dir = MapleRidePaths.Renders;
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions == null) { Debug.LogWarning("[q4cap] no RegionDirector."); return; }
        regions.Resolve();
        string previous = regions.currentRegionId;
        regions.currentRegionId = RegionCatalog.ShiosaiCoast;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();

        var env = FindRoot(EnvironmentRootName);
        if (env == null) { Debug.LogWarning("[q4cap] no Shiosai env root."); return; }
        if (!env.gameObject.activeSelf) env.gameObject.SetActive(true);
        var traffic = env.Find(TrafficRootName);
        if (traffic == null) { Debug.LogWarning("[q4cap] no Shiosai Traffic root."); return; }
        if (!traffic.gameObject.activeSelf) traffic.gameObject.SetActive(true);

        var kids = new List<Transform>();
        foreach (Transform c in traffic) kids.Add(c);

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        int i = route.IndexAt(760f);
        var p = route.Position[i];
        var t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
        var s = route.SideFlat(i);

        var riders = new List<(Transform tr, string name)>();
        foreach (var (slot, name) in Picks)
        {
            if (slot >= kids.Count) { Debug.LogWarning($"[q4cap] no pool slot {slot} for {name}."); continue; }
            var tr = kids[slot];
            if (name != null && !tr.name.Contains(name))
            {
                Debug.LogWarning($"[q4cap] slot {slot} is '{tr.name}', expected {name} - " +
                                 "re-stage the coast traffic.");
                continue;
            }
            riders.Add((tr, name ?? "reference"));
        }
        Debug.Log($"[q4cap] woke {riders.Count} riders: " +
                  string.Join(", ", riders.ConvertAll(r => r.tr.name)));

        // Across the carriageway, evenly spaced, all facing down the road.
        for (int r = 0; r < riders.Count; r++)
        {
            var tr = riders[r].tr;
            ActivateAndSolve(tr);
            var pos = p + s * (-2.7f + r * 1.8f);
            pos.y = p.y;
            tr.position = pos;
            tr.rotation = Quaternion.LookRotation(t, Vector3.up);
            ForceSolve(tr);
            foreach (var smr in tr.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;   // frozen bounds would cull them once moved
        }

        // The whole line, from the front, at rider height.
        {
            var centre = p + s * (-2.7f + (riders.Count - 1) * 0.9f);
            var eye = centre + t * 6.4f + Vector3.up * 1.05f;
            Shot(dir, "q4_quartet_line", eye, centre + Vector3.up * 0.95f, 40f, 1800, 900);
        }

        // ONE RIDER AT A TIME FROM HERE ON. The riders stand 1.8 m apart across the
        // carriageway and the side camera sits 2.45 m out along that same axis, which puts it
        // inside the NEXT rider's head: every q4_*_side.png came back as an unreadable close-up
        // of the neighbour's hair with the subject's own bike a smudge in the corner. The
        // three-quarter shot skims the neighbour too. Hiding the rest is the only framing that
        // is guaranteed unobstructed whatever the spacing.
        foreach (var (tr, _) in riders) tr.gameObject.SetActive(false);

        foreach (var (tr, name) in riders)
        {
            ActivateAndSolve(tr);
            float top = HeadTop(tr);
            // THE CROWN IS THE HELMET, NOT THE FACE. HeadTop bakes the mesh and takes the
            // highest vertex, which on this lineage is the top of the shell; the eye line sits
            // 0.41-0.43 m below it at staged scale (the donor's crown is z 1.357 and her eye
            // centres z 0.944, scaled by ~1.06 to a 1.39 m rider). Aiming at top - 0.10 filled
            // every q4_*_face.png with nothing but dome.
            var head = new Vector3(tr.position.x, top - 0.42f, tr.position.z);
            var body = tr.position + Vector3.up * (top - tr.position.y) * 0.55f;

            // Three-quarter front, whole rider AND bike in frame, nothing between camera and it.
            var q34 = body + t * 2.35f + s * 1.35f + Vector3.up * 0.42f;
            Shot(dir, $"q4_{name}_rider", q34, body, 38f, 1000, 1250);

            // Face: close enough to read eyes, brows, mouth and the helmet flashes. 1.25 m at
            // 32 deg frames 0.72 m, which is the whole head plus the collar.
            var face = head + t * 1.25f + s * 0.34f + Vector3.up * 0.06f;
            Shot(dir, $"q4_{name}_face", face, head, 32f, 1000, 1000);

            // Side-on, which is where a bad hair cut or a floating foot shows first.
            var side = body + s * 2.45f + Vector3.up * 0.30f;
            Shot(dir, $"q4_{name}_side", side, body, 36f, 1000, 1250);

            tr.gameObject.SetActive(false);
        }

        regions.currentRegionId = previous;
        regions.ApplyEnvironmentVisibility();
        regions.ApplyAmbience();
        Debug.Log($"[q4cap] done; restored region '{regions.currentRegionId}'. Scene NOT saved.");
        MapleRideSceneBootstrap.DiscardChanges();
    }

    /// <summary>
    /// Waking a parked pooled rider runs KuroBikeRig.Setup through OnEnable, but a headless
    /// execute-method capture has no player loop and therefore never reaches the rig's
    /// LateUpdate pose solve. The imported skin consequently remains in its valid but raw
    /// upright bind pose: arms down, feet on the road and the bicycle intersecting the body.
    ///
    /// Runtime traffic is already correct because LateUpdate runs before the gameplay camera
    /// renders. Captures must explicitly invoke the public one-shot solve after every wake,
    /// including the second wake for each isolated rider shot.
    /// </summary>
    static void ActivateAndSolve(Transform rider)
    {
        rider.gameObject.SetActive(true);
        ForceSolve(rider);
    }

    static void ForceSolve(Transform rider)
    {
        var rig = rider.GetComponentInChildren<CoralBikeRig>(true);
        if (rig == null)
        {
            Debug.LogWarning($"[q4cap] '{rider.name}' has no CoralBikeRig.");
            return;
        }
        rig.ForceSolveOnce();
    }

    static float HeadTop(Transform rider)
    {
        float top = float.NegativeInfinity;
        foreach (var smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = new Mesh();
            smr.BakeMesh(m, true);
            foreach (var v in m.vertices)
                top = Mathf.Max(top, smr.transform.TransformPoint(v).y);
            Object.DestroyImmediate(m);
        }
        return float.IsNegativeInfinity(top) ? rider.position.y + 1.30f : top;
    }

    static Transform FindRoot(string name)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go.transform;
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr.name == name) return tr;
        return null;
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov, int w, int h)
    {
        var go = new GameObject("~Q4Cam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 6000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.ShiosaiAmbience;
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;   // every defect this is looking for needs a sharp frame
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
        Debug.Log($"[q4cap] wrote {name}.png");
    }
}

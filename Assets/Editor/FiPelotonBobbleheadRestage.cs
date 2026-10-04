using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fix-implementer pass for the reported "bobblehead / missing body" defect on background
/// road-peloton riders.
///
/// Root cause (confirmed by direct inspection of the saved scene): NpcCanonicalConformance.cs
/// was patched earlier the same day to fix rig.poseExtraHeadLiftDegrees from -18.5 to -12 (see
/// its own comment - the "systemic bobblehead defect"), and Sakura Pass's own roster, the Sakura
/// Quartet showcase, Minato Coast Traffic and Shiosai Coast Traffic were all re-staged and now
/// carry the corrected value. FOUR rosters were never re-staged after that fix and still carry
/// the pre-fix -18.5 value baked into their serialized CoralBikeRig components: Azora
/// Highlands, Fuji Ridge, Maple City and Taka Mountains (127 of 199 CoralBikeRig instances in
/// the shared scene, confirmed by scanning the raw YAML for poseExtraHeadLiftDegrees).
///
/// This script reproduces the defect on one rider per un-fixed region, re-runs each region's
/// own idempotent "Stage X Roster" pass (the documented, safe way to push a canonical-fit
/// constant change into the saved scene - see NpcCanonicalConformance.cs / SakuraNpcRoster.cs
/// PaceScale precedent), and re-captures the same riders afterwards so the fix can be verified
/// by eye, not by the field value alone.
///
/// Run headless:
///   Unity.exe -projectPath <abs> -batchmode -quit -executeMethod FiPelotonBobbleheadRestage.Run
/// </summary>
public static class FiPelotonBobbleheadRestage
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    static readonly string OutDir = Path.GetFullPath(Path.Combine(
        Application.dataPath, "..", "fi_bobblehead_restage"));

    // One representative rider per un-fixed region, each name confirmed unique to its own
    // roster file (Aki - Maple City, Midori - Azora, Sayaka - Fuji, Iwao - Taka) so
    // GameObject.Find cannot resolve to the wrong region's rider of the same name.
    static readonly string[] CheckNames =
        { "AkiRider", "MidoriRider", "SayakaRider", "IwaoRider" };

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Debug.Log("[bobblehead-fix] ---- BEFORE ----");
        CaptureAll("before");

        Debug.Log("[bobblehead-fix] restaging Azora...");
        AzoraNpcRoster.AddAllToScene();
        Debug.Log("[bobblehead-fix] restaging Fuji...");
        FujiNpcRoster.AddAllToScene();
        Debug.Log("[bobblehead-fix] restaging Maple City...");
        MapleCityNpcRoster.AddAllToScene();
        Debug.Log("[bobblehead-fix] restaging Taka...");
        TakaNpcRoster.AddAllToScene();

        // AddAllToScene() re-opens/saves the scene itself each time; make sure we are on the
        // freshly saved version before the after-capture.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Debug.Log("[bobblehead-fix] ---- AFTER ----");
        CaptureAll("after");

        // Regression check: a Sakura-native and a Minato-traffic rider, already fixed earlier,
        // must be untouched by this pass.
        CheckUnchanged("MikaRider");    // Sakura roster
        CheckUnchanged("MinoriRider");  // Minato traffic

        Debug.Log("[bobblehead-fix] done. Scene saved by each region's own Stage pass.");
    }

    static void CheckUnchanged(string name)
    {
        var rig = FindRig(name);
        if (rig == null) { Debug.LogWarning($"[bobblehead-fix] regression check: {name} not found."); return; }
        Debug.Log($"[bobblehead-fix] regression check {name}: poseExtraHeadLiftDegrees=" +
                  rig.poseExtraHeadLiftDegrees + " (expect -12)");
    }

    static void CaptureAll(string tag)
    {
        foreach (var name in CheckNames)
            CaptureOne(name, tag);
    }

    static CoralBikeRig FindRig(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) return null;
        return go.GetComponentInChildren<CoralBikeRig>(true);
    }

    static void CaptureOne(string name, string tag)
    {
        var go = GameObject.Find(name);
        if (go == null) { Debug.LogWarning($"[bobblehead-fix] {name} not found in scene."); return; }
        var rig = go.GetComponentInChildren<CoralBikeRig>(true);
        if (rig != null) rig.ForceSolveOnce();
        Debug.Log($"[bobblehead-fix] {tag} {name}: poseExtraHeadLiftDegrees=" +
                  (rig != null ? rig.poseExtraHeadLiftDegrees.ToString() : "N/A"));

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { Debug.LogWarning($"[bobblehead-fix] {name} has no renderers."); return; }
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        var camGo = new GameObject("~FiBobbleheadCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        cam.allowHDR = true;
        cam.fieldOfView = 35f;
        float dist = Mathf.Max(b.size.x, b.size.y, b.size.z) * 2.2f + 0.4f;
        // Front-three-quarter, matching the chase-cam framing used to report the defect.
        Vector3 dir = (new Vector3(0.5f, 0.25f, 1f)).normalized;
        cam.transform.position = b.center + dir * dist;
        cam.transform.LookAt(b.center);

        var tex = new RenderTexture(900, 900, 24);
        cam.targetTexture = tex;
        cam.Render();
        RenderTexture.active = tex;
        var img = new Texture2D(900, 900, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, 900, 900), 0, 0);
        img.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);

        string path = Path.Combine(OutDir, $"{tag}_{name}.png");
        File.WriteAllBytes(path, img.EncodeToPNG());
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(camGo);
        Debug.Log($"[bobblehead-fix] wrote {path}");
    }
}
